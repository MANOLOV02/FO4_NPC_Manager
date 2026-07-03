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
        Public BuildFaceGen As Boolean = False   ' --buildfacegen: bake completo (NIF + 3 DDS) headless via FaceGenBuilder (path CPU, _2 sandbox)
        Public VanillaOnly As Boolean = False    ' --vanillaonly: con --buildfacegen, SALTEA NPCs cuyo record GANADOR no es vanilla/DLC (overridden por un mod) — para comparar fiel vs CK del BA2
        Public Info As Boolean = False
        Public Tints As Boolean = False
        Public TtedScan As Boolean = False
        Public ScanDiff As Boolean = False      ' --scandiff: NPCs donde el blend-op del app difiere del CK (color-match LAST-wins)
        Public RaceAnim As Boolean = False      ' --raceanim: dump del behavior resuelto por raza (project+subgraphs+SRAC)
        Public MountValidate As Boolean = False ' --mountvalidate: valida orden mount/pose con datos reales
        Public FindHkx As String = ""           ' --findhkx <substr>: lista .hkx del load order que matchean + face-bones
        Public ChunkCompare As String = ""       ' --chunkcompare <chunkNif>: layout interno del chunk vs CreateABot
        Public DumpBehavior As String = ""       ' --dumpbehavior <hkx>: clip generators + character animationNames/behaviorFilename
        Public HkxCoverage As Boolean = False    ' --hkxcoverage: cuenta todos los .hkx/.hkt y verifica que estén referenciados
        Public KwType As String = ""             ' --kwtype <substr>: lista KYWD (EDID + TNAM Type enum) que matchean (discriminador identidad-vs-estado)
        Public StateMap As Boolean = False       ' --statemap: por raza, clasifica subgraphs por EJE DE ESTADO (TNAM type) y muestra qué entra/queda afuera
        Public ClipResolve As Boolean = False     ' --clipresolve: valida la resolución clip→archivo por EXISTENCIA sobre rutas SAPT (cobertura % por raza)
        Public HkxBone As String = ""             ' --hkxbone "<hkxPath>|<boneSubstr>": local+world del hueso en el hkaSkeleton (rig canónico)
        Public ClipBase As String = ""            ' --clipbase "<rigHkx>|<clipHkx>[|boneFilter[|chunkNif;...]]": frame-0 del clip vs rig refPose vs assembled (skin binds del chunk)
        Public FindFile As String = ""            ' --findfile <substr>: lista keys del FilesDictionary que matchean (cualquier extensión)
        Public Provenance As Boolean = False      ' --provenance: SourcePluginName de NPC/RACE/CLFMs del dirt (chequeo vanilla-vs-vanilla)
        Public DumpRef As String = ""             ' --dumpref "<filesDictKey>|<outFile>": vuelca GetBytes(key) crudo a outFile (ref vanilla del BA2)
        Public NifDump As String = ""             ' --nifdump <nif>: árbol de nodos (local+world) + skin binds (inv(bind)) por shape
        Public AnimSyncCheck As String = ""       ' --animsynccheck "<chunkNif>|<rigHkx>|<clipHkx>[|frame][|boneFilter]": FK del chunk BUGGY (clip full) vs HONORED (No Anim Sync) → tear
        Public BlendHintScan As String = ""       ' --blendhintscan "<all|substr|path.bsa/.ba2>": tally blendHint (0=NORMAL,1=ADDITIVE_DEPRECATED,2=ADDITIVE) + ejemplos + flag ∉{0,1,2}; path=monta archivo (cross-game)
        Public CatProfile As Boolean = False      ' --catprofile [--edid X]: perfila ejes de categoría (folder ground-truth, Perspective, STKD, BlendHint) por raza
        Public RankBy As String = "n"   ' canal por el que rankea el sweep: n (default) / d / s
        Public NeckSeam As Boolean = False       ' --neckseam --esp X --edid Y: diagnóstico costura cuello/cabeza con body-weight (NNAM + _skin, math)
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

    ' BlendOp (FaceTint): 0=Replace 1=Multiply 2=Overlay 3=SoftLight 4=HardLight.
    Private Function BlendName(b As UInteger) As String
        Select Case b
            Case 0UI : Return "Replace"
            Case 1UI : Return "Multiply"
            Case 2UI : Return "Overlay"
            Case 3UI : Return "SoftLight"
            Case 4UI : Return "HardLight"
            Case Else : Return $"bop{b}"
        End Select
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
        If work.Count = 0 AndAlso Not opt.TtedScan AndAlso Not opt.ScanDiff AndAlso Not opt.RaceAnim AndAlso Not opt.MountValidate AndAlso opt.FindHkx = "" AndAlso opt.ChunkCompare = "" AndAlso opt.DumpBehavior = "" AndAlso Not opt.HkxCoverage AndAlso opt.KwType = "" AndAlso Not opt.StateMap AndAlso Not opt.ClipResolve AndAlso opt.HkxBone = "" AndAlso opt.ClipBase = "" AndAlso opt.FindFile = "" AndAlso opt.NifDump = "" AndAlso opt.AnimSyncCheck = "" AndAlso opt.BlendHintScan = "" AndAlso Not opt.CatProfile AndAlso Not opt.Provenance AndAlso opt.DumpRef = "" Then
            Console.Error.WriteLine("No hay NPCs para procesar (revisa --edid / --list).") : Environment.ExitCode = 1 : Return
        End If

        ' --- 4. Encoding (mismo orden que el exe: antes de cargar plugins) ---
        PluginEncodingSettings.InitializeForGame(Config_App.Current.Game)
        PluginEncodingSettings.SetLanguage(PluginEncodingSettings.ReadLanguageFromIni())

        ' --- 5. Bootstrap headless: plugins (load order activa + TODOS los esps de la lista, aunque NO
        '        esten activos, + sus masters) + archivos (BA2+loose). Se monta UNA sola vez. ---
        ' --vanillaonly: cargar SOLO plugins OFICIALES (vanilla+DLC+cc), excluyendo los mods del usuario
        ' del Plugins.txt. Afecta plugins Y archivos (FilesDictionary también llama a ReadActiveLoadOrder)
        ' ⇒ el bake sale 100% vanilla (records Y texturas), fiel al CK del BA2. Setear ANTES de cargar.
        If opt.VanillaOnly Then PluginManager.OfficialPluginsOnly = True
        Console.WriteLine("[load] plugins..." & If(opt.VanillaOnly, " (SOLO oficiales — vanilla/DLC/cc)", ""))
        Dim pm As New PluginManager()
        Dim loadList = PluginManager.ReadActiveLoadOrder()
        For Each esp In work.Select(Function(w) w.Esp).Distinct(StringComparer.OrdinalIgnoreCase)
            EnsureEspInLoadList(loadList, esp, dataPath)
        Next
        ' Carga el whitelist de render del NPC Manager + IDLE/AACT (sistema de idles/gestos: GNAM=archivo de anim,
        ' ENAM=evento, ANAM=árbol Parent/Previous por Action) para auditar la cobertura estructural de los huérfanos PoseA.
        Dim sigFilter As New HashSet(Of String)(SIGS_NPC_RENDERING, StringComparer.Ordinal) From {"IDLE", "AACT"}
        pm.LoadAllPlugins(dataPath, loadList, Nothing, sigFilter)

        Console.WriteLine("[load] montando archivos...")
        Dim cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Caches")
        Directory.CreateDirectory(cacheDir)
        FilesDictionary_class.CacheDirectory = cacheDir
        FilesDictionary_class.RegisterExtensions(".ssf", ".sclp", ".hkx", ".hkt")
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

        ' --- NECKSEAM: diagnóstico de la costura cuello/cabeza con body-weight (NNAM + _skin scale, math validada).
        If opt.NeckSeam Then
            For Each w In work
                NeckSeamDiag(pm, w.Esp, w.Edid)
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

        ' --- PROVENANCE: SourcePluginName de NPC/RACE y de los CLFM del slot Dirt (chequeo vanilla-vs-vanilla).
        If opt.Provenance Then
            For Each w In work
                ProvenanceNpc(pm, w.Esp, w.Edid)
            Next
            Return
        End If

        ' --- DUMPREF: vuelca los bytes crudos de un key del FilesDictionary (DDS de ref vanilla del BA2) a un archivo.
        '     Reusa exactamente el mismo path de lectura que el bake (incluido el DirectXTexWrapper para DX10 BA2).
        If opt.DumpRef <> "" Then
            DumpRefRun(opt.DumpRef)
            Return
        End If

        ' --- 6''''. TTEDSCAN: recorre TODAS las RACE, junta los TTED, reporta distintos + no-enteros ---
        If opt.TtedScan Then
            TtedScan(pm)
            Return
        End If

        ' --- SCANDIFF: NPCs donde el blend-op resuelto por el APP difiere del CK (color-match LAST-wins).
        If opt.ScanDiff Then
            ScanDiff(pm)
            Return
        End If

        ' --- FINDHKX: lista .hkx/.hkt del load order que matchean un substr + tags (skeleton/face-bones/rig).
        If opt.FindHkx <> "" Then
            FindHkxScan(opt.FindHkx)
            Return
        End If

        ' --- CHUNKCOMPARE: layout interno del chunk-NIF (relativo a su ancla compartida) vs CreateABot.
        If opt.ChunkCompare <> "" Then
            ChunkLayoutCompare(opt.ChunkCompare)
            Return
        End If

        ' --- DUMPBEHAVIOR: clip generators (animationName) + character animationNames/behaviorFilename de un HKX.
        If opt.DumpBehavior <> "" Then
            DumpBehaviorClips(opt.DumpBehavior)
            Return
        End If

        ' --- RACEANIM: resuelve el behavior por raza (project+skeleton por gender + subgraphs vía SRAC/SADD).
        If opt.RaceAnim Then
            RaceAnimScan(pm, opt.Edid)
            Return
        End If

        ' --- HKXCOVERAGE: cuenta todos los .hkx/.hkt del load order y verifica que cada uno esté referenciado.
        If opt.HkxCoverage Then
            HkxCoverageScan(pm)
            Return
        End If

        ' --- KWTYPE: lista KYWD (EDID + TNAM Type) que matchean un substr → discriminador identidad-vs-estado.
        If opt.KwType <> "" Then
            KwTypeScan(pm, opt.KwType)
            Return
        End If

        ' --- STATEMAP: por raza, clasifica subgraphs por EJE DE ESTADO (TNAM type) y muestra entra/fuera.
        If opt.StateMap Then
            StateMapScan(pm, opt.Edid)
            Return
        End If

        ' --- CLIPRESOLVE: valida resolución clip→archivo por EXISTENCIA sobre rutas SAPT (cobertura por raza).
        If opt.ClipResolve Then
            ClipResolveScan(pm, opt.Edid)
            Return
        End If

        ' --- HKXBONE: local+world de huesos del hkaSkeleton de animación (rig canónico) que matchean substr.
        If opt.HkxBone <> "" Then
            Dim parts = opt.HkxBone.Split("|"c)
            HkxBoneDump(parts(0), If(parts.Length > 1, parts(1), ""))
            Return
        End If

        ' --- FINDFILE: lista keys del FilesDictionary (cualquier extensión) que matchean substr.
        If opt.FindFile <> "" Then
            FindFileScan(opt.FindFile)
            Return
        End If

        ' --- NIFDUMP: árbol de nodos (local+world) + skin binds (inv(bind)) por shape de un NIF.
        If opt.NifDump <> "" Then
            NifDumpRun(opt.NifDump)
            Return
        End If

        ' --- ANIMSYNCCHECK: valida el modelo No-Anim-Sync (rot clip + T/S estructural) vs el buggy (clip full).
        If opt.AnimSyncCheck <> "" Then
            AnimSyncCheck(opt.AnimSyncCheck)
            Return
        End If

        ' --- BLENDHINTSCAN: distribución de blendHint sobre todos los .hkx (casos aditivos + valores raros ∉{0,1,2}).
        If opt.BlendHintScan <> "" Then
            BlendHintScanRun(opt.BlendHintScan)
            Return
        End If

        ' --- CATPROFILE: perfila los EJES DE CATEGORÍA candidatos por raza para diseñar el selector profundo.
        If opt.CatProfile Then
            CatProfileScan(pm, opt.Edid)
            Return
        End If

        ' --- CLIPBASE: frame-0 del clip (clipBaseLocal) vs rig refPose vs assembled (skin binds chunk).
        If opt.ClipBase <> "" Then
            ClipBaseDump(opt.ClipBase)
            Return
        End If

        ' --- MOUNTVALIDATE: valida el orden de composición mount/pose con datos reales (Assaultron).
        If opt.MountValidate Then
            MountValidateRun()
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
        ' --buildfacegen: bake COMPLETO (NIF + 3 DDS) via FaceGenBuilder, headless. DebugMode=Logger.Enabled
        ' (naming `_2` sandbox, NO pisa el CK bake del juego) + se APAGA el toggle GL (WriteGPUSandboxOutput)
        ' para correr 100% CPU sin contexto GL. El `_2` queda en el FaceGeom/FaceCustomization del juego, que
        ' es donde el compare tool lee "ours".
        If opt.BuildFaceGen Then
            Logger.Enabled = True
            FO4_NPC_Manager.FaceGenBuilder.WriteGPUSandboxOutput = False
        End If
        Try
            For Each w In work
                Dim okOne As Boolean
                If opt.BuildFaceGen Then
                    okOne = BuildFaceGenNpc(pm, w.Esp, w.Edid)
                Else
                    okOne = BakeNpc(pm, w.Esp, w.Edid, dataPath, opt.OutDir, tintBytesCache, opt.DumpDir)
                End If
                If okOne Then ok += 1 Else fail += 1
            Next
        Finally
            FaceTintCpuCompositor.EndBatchDecodeCache()
        End Try
        Console.WriteLine($"[done] {ok} ok / {fail} fail de {work.Count}")
        If ok = 0 Then Environment.ExitCode = 1
    End Sub

    ''' <summary>--buildfacegen: bake COMPLETO (NIF + 3 DDS `_2` sandbox) de UN NPC via la MISMA ruta que la
    ''' app (FaceGenBuilder.BuildCharGen), pero headless: host=Nothing (sin GL; el toggle WriteGPUSandboxOutput
    ''' ya se apagó), appliedPresets vacío, willBePacked=False (loose), delegate de materiales = el del render
    ''' (NpcMaterialResolver.ApplyShapeMaterialOverrides). El estado del NPC lo arma el propio BuildCharGen
    ''' desde el record. Devuelve True si Success.</summary>
    ''' <summary>--buildfacegen: bake COMPLETO (NIF + 3 DDS `_2` sandbox) de UN NPC headless via la MISMA
    ''' ruta que la app (FaceGenBuilder.BuildCharGen). Con --vanillaonly el entorno ya cargó SOLO plugins
    ''' oficiales (PluginManager.OfficialPluginsOnly), así que el record + las texturas son vanilla por
    ''' construcción (sin override de mods). Devuelve True si Success.</summary>
    Private Function BuildFaceGenNpc(pm As PluginManager, espName As String, edid As String) As Boolean
        Dim npcFormID = ResolveEdid(pm, espName, edid)
        If npcFormID = 0UI Then
            Console.Error.WriteLine($"[skip] EDID='{edid}' no provisto por '{espName}'.") : Return False
        End If
        Try
            Dim presets As New Dictionary(Of UInteger, FO4_NPC_Manager.LooksmenuLoader.LooksmenuPreset)
            ' Resolver de materiales por-shape = el MISMO que el render (texture-paths/BGSM/tints fieles a CK).
            ' NpcRenderContext solo necesita el PluginManager (sin GL). overlay = identidad (sin presets LM).
            Dim ctx As New FO4_NPC_Manager.NpcRenderContext(pm)
            Dim mres As New FO4_NPC_Manager.NpcMaterialResolver(ctx, Function(raw As NPC_Data, fid As UInteger) raw)
            Dim res = FO4_NPC_Manager.FaceGenBuilder.BuildCharGen(
                npcFormID, pm, presets, Nothing,
                AddressOf mres.ApplyShapeMaterialOverrides,
                willBePacked:=False)
            If res Is Nothing OrElse Not res.Success Then
                Console.Error.WriteLine($"[fail] {edid} (0x{npcFormID:X8}): {If(res Is Nothing, "null result", res.Summary)}") : Return False
            End If
            Console.WriteLine($"[ok] {edid} 0x{npcFormID:X8} -> {res.OutputPath} (kept={res.ShapesKept} dropped={res.ShapesDropped})")
            Return True
        Catch ex As Exception
            Console.Error.WriteLine($"[fail] {edid} (0x{npcFormID:X8}): {ex.GetType().Name}: {ex.Message}") : Return False
        End Try
    End Function

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
        Dim localId = PluginManager.ToFaceGenLocalFormID(npcFormID)
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
        Dim localId = PluginManager.ToFaceGenLocalFormID(fid)
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
        ' Si 'edid' es un FormID hex (0x... o 8 digitos), resolver por PluginManager.ToFaceGenLocalFormID + plugin que
        ' PROVEE el record ganador (SourcePluginName), igual que el path EDID. Asi --esp <override.esp>
        ' selecciona la version overridden (ej. NPC_BAKETEST.esp sobre Alana de Fallout4.esm), no el originante.
        Dim hexId As UInteger
        If TryHexId(edid, hexId) Then
            ' Match on the FaceGen-local FormID of BOTH sides so ESL (0xFE) FormIDs compare correctly:
            ' the record key is reduced via ToFaceGenLocalFormID, so the wanted hex must be too (a plain
            ' 24-bit mask never matches an ESL record, whose local id is the 12-bit object id).
            Dim want = PluginManager.ToFaceGenLocalFormID(hexId)
            Dim hexFallback As UInteger = 0UI, hexFallbackCount As Integer = 0
            For Each kv As KeyValuePair(Of UInteger, PluginRecord) In pm.AllRecords
                Dim r = kv.Value
                If r Is Nothing OrElse r.Header.Signature <> "NPC_" Then Continue For
                If PluginManager.ToFaceGenLocalFormID(kv.Key) <> want Then Continue For
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
                            Console.WriteLine($"        tplCol tplIdx={tc.TemplateIndex} alpha={tc.Alpha:G6} blendOp={tc.BlendOperation}/{BlendName(tc.BlendOperation)} clfm=0x{tc.ColorFormID:X8} {col}")
                        Next
                    End If
                Next
            Next
        End If
        Console.WriteLine($"-- TTED summary: con-TTED={nTted}, denormales(=entero leido como float)={nDenorm} -> {If(nDenorm = 0, "TODOS floats sanos", "HAY enteros/denormales")} --")

        Dim merged = FaceTintInputBuilder.MergeTintLayersWithRaceDefaults(npcData.FaceTintLayers, race, isFemale, pm)
        Console.WriteLine($"-- MERGED ({merged.Count}) -- (RESOLVED = ResolvePaletteLayerEffective: que BlendOp/color usa el compositor)")
        For Each m In merged
            Dim lyr = m.Layer
            Dim resolvedStr As String = ""
            If lyr.Discriminator = 1 Then   ' Palette: resolver Step1(idx)/Step2(color)/Step3(fallback)
                Dim opt = race.FindTintOption(lyr.Index, isFemale)
                If opt IsNot Nothing Then
                    Dim r = FaceTintInputBuilder.ResolvePaletteLayerEffective(lyr, opt, pm)
                    resolvedStr = $"  -> RESOLVED matched={r.Matched} blendOp={r.BlendOp}/{BlendName(r.BlendOp)} color=ARGB(0x{r.Color.ToArgb():X8}) opScale={r.OpacityScale:G4}"
                End If
            End If
            Console.WriteLine($"  idx={lyr.Index} disc={lyr.Discriminator} value={lyr.Value} tplColIdx={lyr.TemplateColorIndex} color=ARGB(0x{lyr.Color.ToArgb():X8}) raceDefault={m.IsRaceDefault}{resolvedStr}")
        Next
    End Sub

    ''' <summary>DIAGNOSTICO (--dumpref "&lt;key&gt;|&lt;outFile&gt;"): vuelca GetBytes(key) crudo a outFile. El key es
    ''' una ruta del FilesDictionary (ej. textures\actors\character\facecustomization\fallout4.esm\000226DC_d.dds);
    ''' el archivo escrito es el DDS vanilla TAL CUAL sale del BA2/loose (mismo path de lectura que el bake).</summary>
    Private Sub DumpRefRun(spec As String)
        Dim parts = spec.Split({"|"c}, 2)
        If parts.Length <> 2 Then
            Console.Error.WriteLine("[dumpref] formato: --dumpref ""<filesDictKey>|<outFile>""") : Environment.ExitCode = 1 : Return
        End If
        Dim key = parts(0).Trim()
        Dim outFile = parts(1).Trim()
        Dim bytes = FilesDictionary_class.GetBytes(key)
        If bytes Is Nothing OrElse bytes.Length = 0 Then
            Console.Error.WriteLine($"[dumpref] key vacio o no encontrado: '{key}'") : Environment.ExitCode = 1 : Return
        End If
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile)))
        File.WriteAllBytes(outFile, bytes)
        Console.WriteLine($"[dumpref] {key} -> {outFile} ({bytes.Length} bytes)")
    End Sub

    ''' <summary>DIAGNOSTICO (--provenance): imprime SourcePluginName del record GANADOR para NPC + RACE +
    ''' cada CLFM referenciado por las opciones del slot Dirt (slot 20) de la raza. Marca como [VANILLA] los
    ''' que vienen de Fallout4.esm o DLC oficial; [MOD] cualquier otro. Para garantizar la comparacion
    ''' vanilla-vs-vanilla: si algun record relevante esta overrideado por un mod, NO bakear.</summary>
    Private ReadOnly VanillaPlugins As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCworkshop02.esm", "DLCworkshop03.esm",
        "DLCCoast.esm", "DLCNukaWorld.esm", "DLCUltraHighResolution.esm"}

    Private Function VanillaTag(plugin As String) As String
        Return If(VanillaPlugins.Contains(plugin), "[VANILLA]", "[MOD!!]")
    End Function

    Private Sub ProvenanceNpc(pm As PluginManager, espName As String, edid As String)
        Dim npcFormID = ResolveEdid(pm, espName, edid)
        If npcFormID = 0UI Then Console.WriteLine($"[prov] {edid}: no resuelto en {espName}") : Return
        Dim npcRec = pm.GetRecord(npcFormID)
        Dim npcSrc = npcRec.SourcePluginName
        Dim npcData = RecordParsers.ParseNPC(npcRec, npcSrc, pm)
        Dim raceRec = pm.GetRecord(npcData.RaceFormID)
        Dim raceSrc = If(raceRec IsNot Nothing, raceRec.SourcePluginName, "(none)")
        Console.WriteLine($"=== PROVENANCE {edid} 0x{npcFormID:X8} ===")
        Console.WriteLine($"  NPC_  0x{npcFormID:X8}  src={npcSrc} {VanillaTag(npcSrc)}")
        Console.WriteLine($"  RACE  0x{npcData.RaceFormID:X8}  src={raceSrc} {VanillaTag(raceSrc)}")
        Dim race = RecordParsers.ParseRACE(raceRec, pm)
        Dim isFemale = npcData.IsFemale
        Dim groups = If(isFemale, race.FemaleTintTemplateGroups, race.MaleTintTemplateGroups)
        Dim seenClfm As New HashSet(Of UInteger)()
        Dim allVanilla As Boolean = VanillaPlugins.Contains(npcSrc) AndAlso VanillaPlugins.Contains(raceSrc)
        If groups IsNot Nothing Then
            For Each grp In groups
                If grp.Options Is Nothing Then Continue For
                For Each o In grp.Options
                    If o.Slot <> 20US Then Continue For   ' Dirt
                    If o.TemplateColors Is Nothing Then Continue For
                    For Each tc In o.TemplateColors
                        If tc.ColorFormID = 0UI OrElse Not seenClfm.Add(tc.ColorFormID) Then Continue For
                        Dim crec = pm.GetRecord(tc.ColorFormID)
                        Dim csrc = If(crec IsNot Nothing, crec.SourcePluginName, "(none)")
                        If Not VanillaPlugins.Contains(csrc) Then allVanilla = False
                        Console.WriteLine($"  CLFM(Dirt) 0x{tc.ColorFormID:X8}  src={csrc} {VanillaTag(csrc)}")
                    Next
                Next
            Next
        End If
        Console.WriteLine($"  => {If(allVanilla, "TODOS VANILLA: comparacion vanilla-vs-vanilla VALIDA", "HAY OVERRIDE DE MOD: NO BAKEAR (comparacion contaminada)")}")
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

    ''' <summary>DIAGNOSTICO (--scandiff): recorre TODOS los NPC_ y reporta los que tienen alguna Palette
    ''' layer VISIBLE (disc=1, value>0) donde el BlendOp resuelto por el APP
    ''' (FaceTintPaletteResolver.ResolvePaletteLayerEffective, index/alpha-closest) DIFIERE del BlendOp
    ''' del CK (ResolveBlendOpCk: color-match exacto sobre TemplateColors, LAST gana, early-out por alpha
    ''' exacto). Net SkinTone: ambos motores fuerzan 0->3 en slot 12. Diagnostico puro, no escribe nada.</summary>
    Private Sub ScanDiff(pm As PluginManager)
        Console.WriteLine("=== SCANDIFF: app (index/alpha-closest) vs CK (color-match LAST-wins) por capa Palette visible ===")
        Dim scanned As Integer = 0      ' NPCs con FaceTintLayers (Palette visible considerada)
        Dim withTints As Integer = 0    ' NPCs con al menos 1 FaceTintLayer (cualquiera)
        Dim diffLines As Integer = 0
        Dim diffNpcs As Integer = 0
        ' Agrupado por (slot, app, ck) para el resumen del patron.
        Dim groupCounts As New Dictionary(Of String, Integer)(StringComparer.Ordinal)

        For Each kv As KeyValuePair(Of UInteger, PluginRecord) In pm.AllRecords
            Dim rec = kv.Value
            If rec Is Nothing OrElse rec.Header.Signature <> "NPC_" Then Continue For
            Try
                Dim npc = RecordParsers.ParseNPC(rec, rec.SourcePluginName, pm)
                If npc Is Nothing OrElse npc.FaceTintLayers Is Nothing OrElse npc.FaceTintLayers.Count = 0 Then Continue For
                withTints += 1
                Dim raceRec = pm.GetRecord(npc.RaceFormID)
                If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Continue For
                Dim race = RecordParsers.ParseRACE(raceRec, pm)
                If race Is Nothing Then Continue For
                scanned += 1
                Dim isFemale = npc.IsFemale
                Dim npcHadDiff As Boolean = False

                For Each tl In npc.FaceTintLayers
                    If tl Is Nothing OrElse tl.Discriminator <> 1US OrElse tl.Value <= 0 Then Continue For
                    Dim opt = race.FindTintOption(tl.Index, isFemale)
                    If opt Is Nothing Then Continue For
                    Dim appRes = FaceTintPaletteResolver.ResolvePaletteLayerEffective(tl, opt, pm)
                    Dim appBop As UInteger = appRes.BlendOp
                    Dim ckBop As UInteger = FaceTintPaletteResolver.ResolveBlendOpCk(opt, appRes.Color, tl.Value, pm)
                    ' Net SkinTone: ambos motores fuerzan 0 -> 3 en slot 12.
                    If opt.Slot = CUShort(TintSlot.SkinTone) AndAlso ckBop = 0UI Then ckBop = 3UI
                    If appBop <> ckBop Then
                        Dim c = appRes.Color
                        Console.WriteLine($"DIFF 0x{kv.Key:X8} {rec.EditorID} [{rec.SourcePluginName}] slot={opt.Slot}({SlotName(opt.Slot)}) idx={tl.Index} val={tl.Value} tplColIdx={tl.TemplateColorIndex} color={c.R},{c.G},{c.B} app={appBop}/{BlendName(appBop)} ck={ckBop}/{BlendName(ckBop)}")
                        diffLines += 1
                        npcHadDiff = True
                        Dim gkey = $"slot={opt.Slot}({SlotName(opt.Slot)}) app={appBop}/{BlendName(appBop)} ck={ckBop}/{BlendName(ckBop)}"
                        groupCounts(gkey) = If(groupCounts.ContainsKey(gkey), groupCounts(gkey) + 1, 1)
                    End If
                Next
                If npcHadDiff Then diffNpcs += 1
            Catch ex As Exception
                Console.Error.WriteLine($"[scandiff] 0x{kv.Key:X8} {rec.EditorID}: {ex.GetType().Name}: {ex.Message}")
            End Try
        Next

        Console.WriteLine($"=== SCANDIFF resumen: {withTints} NPCs con FaceTintLayers, {scanned} con RACE resuelta (scaneados), {diffLines} DIFF lines en {diffNpcs} NPCs ===")
        If groupCounts.Count > 0 Then
            Console.WriteLine("-- por (slot, app->ck) --")
            For Each g In groupCounts.OrderByDescending(Function(x) x.Value)
                Console.WriteLine($"  {g.Key} : {g.Value}")
            Next
        End If
    End Sub

    ''' <summary>DIAGNOSTICO (--info): vuelca la cadena de resolucion de base D/N/S de la cara para UN NPC
    ''' (NPC.HeadTexture/FTST, RACE default por genero, lo que devuelve §3 HOY, y por cada HeadPart su
    ''' PartType + HDPT.TextureSet + el material inline del NIF) con dims de cada DDS. Permite ver con keys
    ''' exactos que usa el app vs el CLI sin componer. No escribe nada.</summary>
    ' Dump del behavior resuelto por raza: project+skeleton por gender + subgraphs (propios o vía SRAC/SADD).
    ''' <summary>Lista los .hkx/.hkt del load order que matchean un substring, y para cada uno taggea:
    ''' hkaSkeleton (name + #bones + face-bones detectados), hkbCharacterStringData (name + rigName).
    ''' Sirve para responder "¿hay un HKX específico de cara (face skeleton/behavior)?".</summary>
    Private Sub FindHkxScan(substr As String)
        Dim rxFace = New System.Text.RegularExpressions.Regex("jaw|lip|cheek|brow|mouth|tongue|teeth|nose|chin|forehead|eyelid|^eye|_eye|face|phoneme", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        Dim keys = FilesDictionary_class.Dictionary.Keys.
            Where(Function(k) (k.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) OrElse k.EndsWith(".hkt", StringComparison.OrdinalIgnoreCase)) AndAlso
                              k.IndexOf(substr, StringComparison.OrdinalIgnoreCase) >= 0).
            OrderBy(Function(k) k, StringComparer.OrdinalIgnoreCase).ToList()
        Console.WriteLine($"[findhkx] '{substr}': {keys.Count} archivos .hkx/.hkt en el load order")
        Dim parsed = 0
        For Each k In keys
            Dim bytes = FilesDictionary_class.GetBytes(k)
            If bytes Is Nothing OrElse bytes.Length = 0 Then Console.WriteLine($"  {k}  (no carga)") : Continue For
            Dim tags As New List(Of String)
            Try
                Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(bytes))
                For Each o In sg.GetObjectsByClassName("hkaSkeleton")
                    Dim sk = sg.ParseSkeleton(o)
                    If sk Is Nothing OrElse sk.Bones Is Nothing Then Continue For
                    Dim faceB = sk.Bones.Select(Function(b) b.Name).Where(Function(n) Not String.IsNullOrEmpty(n) AndAlso rxFace.IsMatch(n)).Take(30).ToList()
                    tags.Add($"hkaSkeleton '{sk.Name}' bones={sk.Bones.Count}{If(faceB.Count > 0, " FACE-BONES(" & faceB.Count & "):[" & String.Join(",", faceB) & "]", " (sin face-bones)")}")
                    If keys.Count <= 2 Then Console.WriteLine($"     TODOS los bones [{sk.Bones.Count}]: {String.Join(", ", sk.Bones.Select(Function(b) b.Name))}")
                Next
                For Each o In sg.GetObjectsByClassName("hkbCharacterStringData")
                    Dim csd = sg.ParseCharacterStringData(o)
                    If csd IsNot Nothing Then tags.Add($"character '{csd.CharacterName}' rig='{csd.RigName}'")
                Next
            Catch ex As Exception
                tags.Add("(parse fail: " & ex.GetType().Name & ")")
            End Try
            parsed += 1
            Console.WriteLine($"  {k}  →  {If(tags.Count > 0, String.Join("  |  ", tags), "(sin skeleton/character)")}")
        Next
        Console.WriteLine($"[findhkx] {parsed} parseados.")
    End Sub

    ''' <summary>Valida el armado del skeleton base como lo hace PrepareSkeleton: LoadFromBytes(nif) +
    ''' MergeHkxSkeleton(hkx). Dumpea pre/post bone count, cuántos mergeó, InjectedBones (debe ser 0 sin
    ''' shapes/cloth), y el world de bones clave (Root + chunk-bones de robot) para confirmar que quedan en
    ''' la posición ensamblada del HKX.</summary>
    Private Sub ValidateMergeHkx(label As String, hkxPath As String, nifPath As String)
        Dim nifBytes = LoadAnimCand(nifPath) : Dim hkxBytes = LoadAnimCand(hkxPath)
        If nifBytes Is Nothing OrElse hkxBytes Is Nothing Then
            Console.WriteLine($"  [MERGE-VALIDATE] falta archivo (nif={nifBytes IsNot Nothing}, hkx={hkxBytes IsNot Nothing})") : Return
        End If
        Dim s As New SkeletonInstance()
        If Not s.LoadFromBytes(nifBytes) Then Console.WriteLine("  [MERGE-VALIDATE] LoadFromBytes(nif) falló") : Return
        Dim pre = s.SkeletonDictionary.Count
        Dim merged = s.MergeHkxSkeleton(hkxBytes)
        Dim post = s.SkeletonDictionary.Count
        Console.WriteLine($"  [MERGE-VALIDATE] {label}: NIF={pre} bones → +HKX merge={merged} → total={post} | InjectedBones={s.InjectedBones.Count} (esperado 0)")
        ' Dump del world de bones clave (los que existan): root, locomoción, chunk-bones de robot.
        Dim probes = {"Root", "COM", "Neck", "Head", "C-BotCore", "C-BotLegs", "BUpperLeg", "LArm_UpperArm", "Camera"}
        For Each bn In probes
            Dim hb As HierarchiBone_class = Nothing
            If s.SkeletonDictionary.TryGetValue(bn, hb) Then
                Dim w = hb.OriginalGetGlobalTransform
                Dim parent = If(hb.Parent IsNot Nothing, hb.Parent.BoneName, "<root>")
                Console.WriteLine($"     '{bn}' parent='{parent}' world.T=({w.Translation.X:F1},{w.Translation.Y:F1},{w.Translation.Z:F1})")
            End If
        Next
    End Sub

    ''' <summary>Dump del hkaSkeleton de ANIMACIÓN de un .hkx: para cada hueso que matchea el substr,
    ''' imprime índice, parent, LOCAL (referencePose) y WORLD compuesto. Para verificar si el OriginalLocaL
    ''' del esqueleto vivo coincide con el rig canónico (la animación se autorea contra ESTE local).</summary>
    Private Sub HkxBoneDump(hkxPath As String, boneSubstr As String)
        Dim b = LoadAnimCand(hkxPath)
        If b Is Nothing Then Console.WriteLine($"[hkxbone] '{hkxPath}' no carga") : Return
        Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(b))
        Dim skels = g.GetObjectsByClassName("hkaSkeleton").Select(Function(o) g.ParseSkeleton(o)).Where(Function(s) s IsNot Nothing).ToList()
        Dim skel = skels.FirstOrDefault(Function(s) Not If(s.Name, "").Contains("Ragdoll", StringComparison.OrdinalIgnoreCase))
        If skel Is Nothing Then Console.WriteLine("[hkxbone] sin hkaSkeleton de animación") : Return
        Console.WriteLine($"[hkxbone] '{hkxPath}' skel='{skel.Name}' bones={skel.Bones.Count} | filtro='{boneSubstr}'")
        Dim world(skel.Bones.Count - 1) As Transform_Class
        For i = 0 To skel.Bones.Count - 1
            Dim loc = HkxTransformConventionHelper.ToTransform(skel.ReferencePose(i))
            Dim p = If(i < skel.ParentIndices.Count, CInt(skel.ParentIndices(i)), -1)
            world(i) = If(p < 0 OrElse p >= i, loc, world(p).ComposeTransforms(loc))
            Dim nm = If(skel.Bones(i).Name, "")
            If boneSubstr <> "" AndAlso nm.IndexOf(boneSubstr, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            Dim pn = If(p >= 0 AndAlso p < skel.Bones.Count, If(skel.Bones(p).Name, "?"), "<root>")
            Dim lT = loc.Translation, wT = world(i).Translation
            Dim lr = loc.Rotation
            Console.WriteLine($"   [{i,3}] '{nm}' parent='{pn}'  LOCAL.T=({lT.X:F3},{lT.Y:F3},{lT.Z:F3}) R11/22/33=({lr.M11:F3},{lr.M22:F3},{lr.M33:F3})  WORLD.T=({wT.X:F3},{wT.Y:F3},{wT.Z:F3})")
        Next
    End Sub

    ''' <summary>Lista keys del FilesDictionary que matchean un substring (cualquier extensión) — para
    ''' localizar chunk NIFs / clips sin adivinar paths.</summary>
    Private Sub FindFileScan(substr As String)
        Dim keys = FilesDictionary_class.Dictionary.Keys.
            Where(Function(k) k.IndexOf(substr, StringComparison.OrdinalIgnoreCase) >= 0).
            OrderBy(Function(k) k, StringComparer.OrdinalIgnoreCase).ToList()
        Console.WriteLine($"[findfile] '{substr}': {keys.Count} matches")
        For Each k In keys.Take(300)
            Console.WriteLine("  " & k)
        Next
        If keys.Count > 300 Then Console.WriteLine($"  ... ({keys.Count - 300} más)")
    End Sub

    ' Carpeta del clip relativa a "Animations\" (ground truth de la organización Bethesda). "(top)" si el clip
    ' está directo bajo Animations\; "(no-anim)" si el path no contiene Animations\.
    Private Function FolderRelOf(animFile As String) As String
        If String.IsNullOrWhiteSpace(animFile) Then Return "(none)"
        Dim p = animFile.Replace("/"c, "\"c)
        Dim i = p.IndexOf("Animations\", StringComparison.OrdinalIgnoreCase)
        If i < 0 Then Return "(no-anim)"
        Dim rest = p.Substring(i + "Animations\".Length)
        Dim j = rest.LastIndexOf("\"c)
        Return If(j <= 0, "(top)", rest.Substring(0, j))
    End Function

    ' Primer segmento de la carpeta rel (la "actividad" macro: MT / 1HM / Furniture / Injured / ...).
    Private Function TopSegOf(folderRel As String) As String
        If folderRel.StartsWith("(") Then Return folderRel
        Dim k = folderRel.IndexOf("\"c)
        Return If(k < 0, folderRel, folderRel.Substring(0, k))
    End Function

    ''' <summary>--catprofile: perfila los EJES DE CATEGORÍA candidatos por raza (con --edid). Para cada raza con
    ''' behavior: (a) histograma de carpetas resueltas = GROUND TRUTH de la organización Bethesda; (b) por Role,
    ''' el desglose por carpeta (¿subdivide el bucket gigante "Weapon"?); (c) Perspective de los subgraphs (SRAF);
    ''' (d) STKD (target keywords) usados; (e) BlendHint (hkaAnimationBinding) de cada archivo resuelto = aditivos.
    ''' Solo lee API pública de la lib (no la modifica).</summary>
    Private Sub CatProfileScan(pm As PluginManager, edidFilter As String)
        Dim loader As Func(Of String, Byte()) = Function(p) FO4_NPC_Manager.MeshPathHelpers.TryLoadMeshBytes(p, 0)
        ' Set de animaciones referenciadas por records IDLE (GNAM = 'Animation File', wbDefinitionsFO4.pas:10010).
        ' Los IDLE son el sistema de idles/gestos/diálogo (PoseA_*), SEPARADO del behavior-graph por-raza → sirve para
        ' explicar la ORFANDAD (cuántos huérfanos son anims de IDLE que el enumerador por-raza no camina).
        Dim idleGnam As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim idleGnamRaw As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)     ' GNAM crudo (con tokens $(…) y wildcards *)
        Dim idleGnamBases As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)   ' por BASENAME (GNAM suele ser relativo)
        Dim idleRecs = pm.GetRecordsOfType("IDLE").ToList()
        Dim idleWithGnam = 0, idleWithEnam = 0, idleWithDnam = 0
        Dim idleEvents As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)   ' ENAM = evento de behavior que dispara la anim
        Dim idleDnam As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)     ' DNAM = behavior graph / nodo
        For Each ir In idleRecs
            For Each sr In ir.Subrecords
                If sr.Data Is Nothing OrElse sr.Data.Length = 0 Then Continue For
                Dim s = System.Text.Encoding.ASCII.GetString(sr.Data).TrimEnd(ChrW(0))
                Select Case sr.Signature
                    Case "GNAM"
                        If Not String.IsNullOrWhiteSpace(s) Then idleGnam.Add(CanonHkx(s)) : idleGnamBases.Add(System.IO.Path.GetFileNameWithoutExtension(s)) : idleGnamRaw.Add(s) : idleWithGnam += 1
                    Case "ENAM" : If Not String.IsNullOrWhiteSpace(s) Then idleEvents.Add(s) : idleWithEnam += 1
                    Case "DNAM" : If Not String.IsNullOrWhiteSpace(s) Then idleDnam.Add(s) : idleWithDnam += 1
                End Select
            Next
        Next
        Console.WriteLine($"[idle] IDLE records={idleRecs.Count} | con GNAM(file)={idleWithGnam} ({idleGnamBases.Count} basenames) | con ENAM(event)={idleWithEnam} ({idleEvents.Count} eventos) | con DNAM={idleWithDnam} ({idleDnam.Count} distintos)")
        ' IDLE records relacionados con el pool PoseA (talk/dialogue/listen/flavor/patrol/pose) — ver su estructura REAL.
        Dim poseIdles = idleRecs.Where(Function(r) {r.EditorID}.Concat(r.Subrecords.Where(Function(s) s.Data IsNot Nothing).Select(Function(s) System.Text.Encoding.ASCII.GetString(s.Data).TrimEnd(ChrW(0)))).
                                                Any(Function(t) t IsNot Nothing AndAlso System.Text.RegularExpressions.Regex.IsMatch(t, "(?i)posea|_talk|dialogue|listen|flavor|patrolsearch"))).Take(10).ToList()
        Console.WriteLine($"[idle] IDLE relacionados a PoseA/talk/dialogue ({poseIdles.Count} muestra):")
        For Each r In poseIdles
            Dim fields = r.Subrecords.Where(Function(s) s.Data IsNot Nothing AndAlso (s.Signature = "DNAM" OrElse s.Signature = "ENAM" OrElse s.Signature = "GNAM")).
                            Select(Function(s) s.Signature & "='" & System.Text.Encoding.ASCII.GetString(s.Data).TrimEnd(ChrW(0)) & "'")
            Console.WriteLine($"      {r.EditorID}: {String.Join(" ", fields)}")
        Next
        ' Eventos ENAM relacionados a talk/dialogue (¿el behavior tiene un evento que dispare estos gestos?).
        Dim talkEvents = idleEvents.Where(Function(e) System.Text.RegularExpressions.Regex.IsMatch(e, "(?i)talk|dialogue|gesture|pose|listen")).Take(20)
        Console.WriteLine($"[idle] eventos ENAM talk/dialogue/gesture: {String.Join(", ", talkEvents)}")
        ' 🔑 PATRONES GNAM: token $(Subgraph)/$(...) + wildcard * → el mecanismo ESTRUCTURAL de los gestos PoseA.
        Dim withToken = idleGnamRaw.Where(Function(g) g.Contains("$(") OrElse g.Contains("*")).ToList()
        Console.WriteLine($"[idle] GNAM crudos distintos={idleGnamRaw.Count} | con token/wildcard={withToken.Count}:")
        For Each g In withToken.Take(40) : Console.WriteLine($"        GNAM-pat: {g}") : Next
        ' Patrones IDLE relacionados a furniture-entry/exit/sync (para cazar los 291 furniture-direccional residuales).
        Dim furnPats = idleGnamRaw.Where(Function(g) System.Text.RegularExpressions.Regex.IsMatch(g, "(?i)enter|exit|sync|furniture|sit|chair|getup|getin")).ToList()
        Console.WriteLine($"[idle] GNAM enter/exit/sync/furniture ({furnPats.Count}):")
        For Each g In furnPats.Take(60) : Console.WriteLine($"        FURN-pat: {g}") : Next
        For Each rec In pm.GetRecordsOfType("RACE")
            Dim race As RACE_Data = Nothing
            Try : race = RecordParsers.ParseRACE(rec, pm) : Catch : Continue For : End Try
            If race Is Nothing Then Continue For
            If race.MaleBehaviorGraphProject = "" AndAlso race.FemaleBehaviorGraphProject = "" Then Continue For
            If edidFilter <> "" AndAlso race.EditorID.IndexOf(edidFilter, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            Dim rb = RaceBehaviorResolver.ResolveRaceBehavior(race.FormID, pm)
            If rb Is Nothing Then Continue For
            Dim clips = BehaviorClipEnumerator.EnumerateClips(rb, loader)
            Console.WriteLine($"===== {race.EditorID} [0x{race.FormID:X8}] | subgraphs={rb.Subgraphs.Count} | clips(dedup-file)={clips.Count} =====")
            ' Cobertura por FUENTE: behavior-walk vs IDLE-pattern (con Category) vs folder-scan (sin Category).
            Dim nBeh = clips.Where(Function(c) c.FromBehaviorGraph).Count()
            Dim nIdle = clips.Where(Function(c) Not c.FromBehaviorGraph AndAlso Not String.IsNullOrEmpty(c.Category)).Count()
            Dim nFolder = clips.Where(Function(c) Not c.FromBehaviorGraph AndAlso String.IsNullOrEmpty(c.Category)).Count()
            Console.WriteLine($"  FUENTE: behavior-walk={nBeh} | IDLE-pattern(con categoría)={nIdle} | clip-gen-variant={nFolder} | rb.IdleAnimations(patrones)={rb.IdleAnimations.Count}")
            ' Over-inclusion: clips cuyo path NO está bajo la carpeta propia del actor (ni _1stPerson). Para robots de
            ' carpeta dedicada debería ser ~0 (si trae Actors\Character\… o de otro actor = bug de gating).
            Dim ownPrefix = CanonHkx(DirNameC(rb.Project) & "\Animations\")
            Dim foreign = clips.Where(Function(c) Not CanonHkx(c.AnimationFile).StartsWith(ownPrefix, StringComparison.OrdinalIgnoreCase) AndAlso CanonHkx(c.AnimationFile).IndexOf("\_1stperson\", StringComparison.OrdinalIgnoreCase) < 0).ToList()
            Console.WriteLine($"  OVER-INCLUSION: clips FUERA de '{DirNameC(rb.Project)}\Animations\' = {foreign.Count}" & If(foreign.Count = 0, "", " → " & String.Join(" ; ", foreign.Take(6).Select(Function(c) TopSegOf(FolderRelOf(c.AnimationFile)) & ":" & System.IO.Path.GetFileName(c.AnimationFile)))))
            Dim catTop = clips.Where(Function(c) Not String.IsNullOrEmpty(c.Category)).GroupBy(Function(c) c.Category).OrderByDescending(Function(g) g.Count()).Take(20)
            Console.WriteLine($"  IDLE categorías: " & String.Join(" ; ", catTop.Select(Function(g) $"{g.Key}={g.Count()}")))
            ' ── OUTLIERS: clips por PROFUNDIDAD de carpeta (0=cuelgan directo de Animations\; 1=directo bajo un top-seg
            '    ej Weapon\X.hkx sin subtipo). Acá la jerarquía Role→carpeta no tiene niveles → revisar que categoricen bien.
            Dim depthOf = Function(p As String) As Integer
                              Dim fr = FolderRelOf(p)
                              Return If(fr = "" OrElse fr = "(top)", 0, fr.Split("\"c).Length)
                          End Function
            Dim byDepth = clips.GroupBy(Function(c) depthOf(c.AnimationFile)).OrderBy(Function(g) g.Key).ToList()
            Console.WriteLine($"  OUTLIERS profundidad: " & String.Join(" ; ", byDepth.Select(Function(g) $"depth{g.Key}={g.Count()}")))
            Dim tops = clips.Where(Function(c) depthOf(c.AnimationFile) = 0).ToList()
            Console.WriteLine($"  (top) cuelgan directo de Animations\\ ({tops.Count}) — roles/cat:")
            For Each grp In tops.GroupBy(Function(c) "[" & String.Join(",", c.Roles) & "]" & If(c.Category <> "", "/" & c.Category, "")).OrderByDescending(Function(x) x.Count()).Take(10)
                Console.WriteLine($"        {grp.Count(),4}  {grp.Key}  ej: {String.Join(", ", grp.Take(4).Select(Function(c) System.IO.Path.GetFileNameWithoutExtension(c.AnimationFile)))}")
            Next
            Dim d1 = clips.Where(Function(c) depthOf(c.AnimationFile) = 1).ToList()
            Console.WriteLine($"  depth-1 (directo bajo top-seg, ej Weapon\\X.hkx) ({d1.Count}) por top-seg: " & String.Join(" ; ", d1.GroupBy(Function(c) TopSegOf(FolderRelOf(c.AnimationFile))).OrderByDescending(Function(x) x.Count()).Take(14).Select(Function(x) $"{x.Key}={x.Count()}")))
            For Each grp In d1.GroupBy(Function(c) TopSegOf(FolderRelOf(c.AnimationFile))).Where(Function(x) {"Weapon", "Furniture", "MT"}.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).Take(3)
                Console.WriteLine($"        {grp.Key}\\ directo ej: {String.Join(", ", grp.Take(8).Select(Function(c) System.IO.Path.GetFileNameWithoutExtension(c.AnimationFile)))}")
            Next

            ' (a) GROUND TRUTH: histograma de TOP-SEG (actividad macro) + carpetas completas.
            Dim byTop = clips.GroupBy(Function(c) TopSegOf(FolderRelOf(c.AnimationFile))).OrderByDescending(Function(g) g.Count()).ToList()
            Console.WriteLine($"  (a) TOP-SEG (actividad macro) — {byTop.Count} distintos:")
            For Each g In byTop : Console.WriteLine($"        {g.Count(),5}  {g.Key}") : Next
            Dim byFolder = clips.GroupBy(Function(c) FolderRelOf(c.AnimationFile)).OrderByDescending(Function(g) g.Count()).ToList()
            Console.WriteLine($"  (a') CARPETAS completas — {byFolder.Count} distintas (top 50):")
            For Each g In byFolder.Take(50) : Console.WriteLine($"        {g.Count(),5}  {g.Key}") : Next

            ' (b) ¿Subdivide el Role gigante? Por cada Role, su histograma de TOP-SEG.
            Console.WriteLine($"  (b) Role × TOP-SEG:")
            For Each role In {"Core", "MT", "Weapon", "Furniture", "Idle", "Pipboy"}
                Dim inRole = clips.Where(Function(c) c.Roles.Contains(role)).ToList()
                If inRole.Count = 0 Then Continue For
                Dim segs = inRole.GroupBy(Function(c) TopSegOf(FolderRelOf(c.AnimationFile))).OrderByDescending(Function(g) g.Count()).ToList()
                Console.WriteLine($"      Role {role} ({inRole.Count}) → {segs.Count} top-segs: " &
                                  String.Join(" ; ", segs.Take(18).Select(Function(g) $"{g.Key}={g.Count()}")))
            Next

            ' (c) Perspective de los subgraphs aplicados (SRAF): 0=3rd 1=1st -1=none.
            Dim persp = rb.Subgraphs.GroupBy(Function(s) s.Perspective).OrderBy(Function(g) g.Key).
                          Select(Function(g) $"{If(g.Key = 0, "3rd", If(g.Key = 1, "1st", "none"))}={g.Count()}")
            Console.WriteLine($"  (c) Perspective(subgraphs SRAF): {String.Join(" ; ", persp)}")

            ' (d) STKD (target keywords) distintos sobre los subgraphs.
            Dim stkd = rb.Subgraphs.SelectMany(Function(s) s.TargetKeywordFormIDs).Distinct().ToList()
            Console.WriteLine($"  (d) STKD target-keywords distintos: {stkd.Count}" &
                              If(stkd.Count = 0, "", " → [" & String.Join(", ", stkd.Take(30).Select(Function(k) EdidOf(pm, k))) & "]"))

            ' (e) BlendHint (hkaAnimationBinding) de cada archivo resuelto → aditivos (≠0) vs normal (=0).
            '     SOLO con --edid (carga 1 archivo por clip; muy lento si corre sobre TODAS las razas).
            If edidFilter <> "" Then
                Dim hintTally As New Dictionary(Of Integer, Integer)
                Dim additiveSample As New List(Of String)
                Dim loadedOk = 0
                For Each c In clips
                    Dim hb = LoadAnimCand(c.AnimationFile)
                    If hb Is Nothing Then Continue For
                    Dim hint As Integer = -99
                    Try
                        Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(hb))
                        Dim b = g.GetObjectsByClassName("hkaAnimationBinding").FirstOrDefault()
                        If b IsNot Nothing Then
                            Dim ab = g.ParseAnimationBinding(b)
                            If ab IsNot Nothing Then hint = ab.BlendHint
                        End If
                    Catch
                    End Try
                    If hint = -99 Then Continue For
                    loadedOk += 1
                    hintTally(hint) = hintTally.GetValueOrDefault(hint, 0) + 1
                    If hint <> 0 AndAlso additiveSample.Count < 25 Then additiveSample.Add($"{FolderRelOf(c.AnimationFile)}\{System.IO.Path.GetFileName(c.AnimationFile)} (hint={hint})")
                Next
                Console.WriteLine($"  (e) BlendHint sobre {loadedOk} archivos: " &
                                  String.Join(" ; ", hintTally.OrderBy(Function(x) x.Key).Select(Function(x) $"{If(x.Key = 0, "normal", If(x.Key = 2, "additive", "hint" & x.Key))}={x.Value}")))
                If additiveSample.Count > 0 Then
                    Console.WriteLine($"      additivos (muestra): ")
                    For Each s In additiveSample : Console.WriteLine($"        {s}") : Next
                End If
            End If

            ' (f) ORFANDAD: .hkx que EXISTEN en la carpeta Animations\ PROPIA del actor pero NO quedaron en la lista
            '     enumerada (ningún hkbClipGenerator alcanzable los referencia → invisibles en el selector). Esto es
            '     COBERTURA del enumerador (independiente de mi categorización; mide si "todos los hkx de la raza entran").
            Dim actorRoot = DirNameC(rb.Project)                          ' p.ej. "actors\Character"
            Dim animPrefix = CanonHkx(actorRoot & "\Animations\")         ' canon (lower, sin Meshes\)
            ' enumSet = SOLO los clips del behavior graph (FromBehaviorGraph) — para medir la orfandad REAL del walk
            ' (no la post-cobertura, que ya rellena). La cobertura file-driven se evalúa aparte.
            Dim behaviorClips = clips.Where(Function(c) c.FromBehaviorGraph).ToList()
            Dim enumSet As New HashSet(Of String)(behaviorClips.Select(Function(c) CanonHkx(c.AnimationFile)), StringComparer.OrdinalIgnoreCase)
            Dim existing = FilesDictionary_class.Dictionary.Keys.
                Select(Function(k) CanonHkx(k)).
                Where(Function(k) k.StartsWith(animPrefix, StringComparison.OrdinalIgnoreCase) AndAlso k.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase)).
                Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            Dim orphans = existing.Where(Function(k) Not enumSet.Contains(k)).OrderBy(Function(k) k).ToList()
            ' [FINAL] = archivos de la carpeta NO mapeados por NINGUNA fuente (walk + IDLE + clip-gen-variant). El residuo real.
            Dim allClipsSet As New HashSet(Of String)(clips.Select(Function(c) CanonHkx(c.AnimationFile)), StringComparer.OrdinalIgnoreCase)
            Dim finalOrphans = existing.Where(Function(k) Not allClipsSet.Contains(k)).OrderBy(Function(k) k).ToList()
            Console.WriteLine($"  [FINAL] carpeta='{actorRoot}\Animations\' existen={existing.Count} MAPEADOS(todas las fuentes)={existing.Count - finalOrphans.Count} | NO-MAPEADOS={finalOrphans.Count}")
            ' Desglose de los NO-mapeados por STEM (nombre sin números/dirección) → patrón (to_mood / alt / etc.).
            Dim stemOf = Function(p As String) As String
                             Dim b = System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant()
                             b = System.Text.RegularExpressions.Regex.Replace(b, "[0-9]+$", "")
                             Return b
                         End Function
            For Each grp In finalOrphans.GroupBy(Function(o) stemOf(o)).OrderByDescending(Function(g) g.Count()).Take(18)
                Console.WriteLine($"        NO-MAP x{grp.Count(),-3} stem='{grp.Key}'  ej: {LastTwoSeg(grp.First())}")
            Next
            ' ¿El huérfano es la MISMA animación (mismo nombre de archivo) que un clip YA resuelto, solo en otra carpeta
            ' mood? (= la resolución/dedup colapsó la variante a base → estructuralmente referenciado). O es un nombre
            ' ÚNICO que ningún clip-generator resuelve (= no referenciado por nombre, event-driven).
            Dim resolvedBases As New HashSet(Of String)(behaviorClips.Select(Function(c) System.IO.Path.GetFileNameWithoutExtension(c.AnimationFile)), StringComparer.OrdinalIgnoreCase)
            Dim baseOf = Function(p As String) System.IO.Path.GetFileNameWithoutExtension(p)
            Dim orphanSameNameAsResolved = orphans.Where(Function(o) resolvedBases.Contains(baseOf(o))).ToList()
            Dim orphanUniqueName = orphans.Where(Function(o) Not resolvedBases.Contains(baseOf(o))).ToList()
            ' ¿Los nombre-ÚNICO (no referenciados por clip-gen) están referenciados por un IDLE.GNAM? = fuente ESTRUCTURAL
            ' (records IDLE, no heurística de carpeta). Confirma/refuta que el pool PoseA es IDLE-driven.
            Dim uniqueByIdle = orphanUniqueName.Where(Function(o) idleGnamBases.Contains(baseOf(o))).Count()
            Dim sameByIdle = orphanSameNameAsResolved.Where(Function(o) idleGnamBases.Contains(baseOf(o))).Count()
            Console.WriteLine($"      [NAME-CHECK] orphans MISMO-nombre (variante mood colapsada)={orphanSameNameAsResolved.Count} (de IDLE.GNAM-base={sameByIdle}) | nombre-ÚNICO no resuelto={orphanUniqueName.Count} (de IDLE.GNAM-base={uniqueByIdle})")
            Dim uniqueNonIdle = orphanUniqueName.Where(Function(o) Not idleGnamBases.Contains(baseOf(o))).ToList()
            Console.WriteLine($"      [NAME-CHECK] nombre-ÚNICO que NI IDLE referencia ({uniqueNonIdle.Count}) — el residuo verdadero:")
            For Each o In uniqueNonIdle.Take(8) : Console.WriteLine($"        residuo: {o}") : Next

            ' (h) CAZA DE LA CLAVE: escanea TODOS los .hkx de behavior bajo el actor (\Behaviors\) + project/character,
            ' recolectando referencias de TODO tipo de generator (no solo hkbClipGenerator): clip(animationName@+0x90),
            ' BGSGamebryoSequenceGenerator(@+0x88), hkbBehaviorReferenceGenerator(@+0x88) + animationNames del character.
            ' ¿Cubre eso los huérfanos que ningún clip-gen-walkeado resuelve? (= el walk pierde gamebryo/otros behaviors).
            Dim refBases As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim nGamebryo = 0, nClip = 0, nBehFiles = 0
            Dim behKeys = FilesDictionary_class.Dictionary.Keys.
                Select(Function(k) CanonHkx(k)).
                Where(Function(k) k.StartsWith(animPrefix.Replace("\animations\", "\behaviors\"), StringComparison.OrdinalIgnoreCase) AndAlso k.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase)).
                Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            For Each bk In behKeys
                Dim bb = LoadAnimCand(bk) : If bb Is Nothing Then Continue For
                nBehFiles += 1
                Try
                    Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(bb))
                    For Each o In g.GetObjectsByClassName("hkbClipGenerator")
                        Dim an = g.ParseClipGenerator(o)?.AnimationName
                        If Not String.IsNullOrWhiteSpace(an) Then refBases.Add(System.IO.Path.GetFileNameWithoutExtension(an)) : nClip += 1
                    Next
                    For Each o In g.GetObjectsByClassName("BGSGamebryoSequenceGenerator")
                        Dim gn = g.ResolveLocalString(o.RelativeOffset + &H88)
                        If Not String.IsNullOrWhiteSpace(gn) Then refBases.Add(System.IO.Path.GetFileNameWithoutExtension(gn)) : nGamebryo += 1
                    Next
                Catch
                End Try
            Next
            Dim uniqueByDeep = uniqueNonIdle.Where(Function(o) refBases.Contains(baseOf(o))).Count()
            Dim deepResidual = uniqueNonIdle.Where(Function(o) Not refBases.Contains(baseOf(o))).ToList()
            Console.WriteLine($"      (h) DEEP-SCAN behaviors={nBehFiles} clipGens={nClip} gamebryo={nGamebryo} refBases-distintos={refBases.Count} | de los {uniqueNonIdle.Count} no-IDLE: cubiertos por deep-scan={uniqueByDeep} | RESIDUO FINAL={deepResidual.Count}")
            ' [94-CHECK] de los NO-mapeados (todas las fuentes), ¿cuántos son nombre de algún clip-gen de Character\Behaviors
            ' (= walk-gap recuperable) vs NINGUNO (= runtime puro: mood-transition/alt elegidos por variable/azar)?
            Dim fByRef = finalOrphans.Where(Function(o) refBases.Contains(baseOf(o))).Count()
            Console.WriteLine($"      [94-CHECK] NO-mapeados={finalOrphans.Count} | basename ∈ clip-gen de Character\Behaviors={fByRef} (walk-gap) | NINGÚN clip-gen (runtime)={finalOrphans.Count - fByRef}")

            ' (i) 🔑 EXPANSIÓN DE PATRONES IDLE.GNAM ($(Subgraph) + wildcard *) contra las carpetas SAPT aplicadas =
            ' el mecanismo ESTRUCTURAL real de los gestos PoseA/Turn. ¿Cubre los huérfanos sin heurística de carpeta?
            Dim kwSetI As New HashSet(Of UInteger)(rb.ActorKeywords)
            Dim saptDirListI As New List(Of String)
            For Each sg In rb.Subgraphs
                Dim fidI = sg.ActorKeywordFormIDs.FirstOrDefault(Function(k) RaceBehaviorResolver.IsRaceIdentityKeyword(k) AndAlso Not kwSetI.Contains(k))
                If fidI <> 0UI Then Continue For
                For Each sp In sg.AnimationPaths
                    If Not String.IsNullOrWhiteSpace(sp) Then saptDirListI.Add(CanonHkx(sp.Replace("/"c, "\"c).TrimEnd("\"c)))
                Next
            Next
            saptDirListI = saptDirListI.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            Dim existingSet As New HashSet(Of String)(existing, StringComparer.OrdinalIgnoreCase)
            Dim idleExpanded As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each pat0 In idleGnamRaw
                Dim pat = pat0.Replace("/"c, "\"c)
                Dim tails As New List(Of String)
                Dim tokIdx = pat.IndexOf("$(Subgraph)", StringComparison.OrdinalIgnoreCase)
                If tokIdx >= 0 Then
                    Dim tail = pat.Substring(tokIdx + "$(Subgraph)".Length).TrimStart("\"c)   ' p.ej. "PoseA_Talk_M*.hkx"
                    For Each d In saptDirListI : tails.Add(d & "\" & tail) : Next
                ElseIf pat.IndexOf("$(", StringComparison.Ordinal) >= 0 Then
                    Continue For   ' otro token desconocido → no expandir (lo reporto aparte si queda residual)
                Else
                    tails.Add(CanonHkx(pat))
                End If
                For Each full In tails
                    Dim cf = CanonHkx(full)
                    Dim star = cf.IndexOf("*"c)
                    If star < 0 Then
                        If existingSet.Contains(cf) Then idleExpanded.Add(cf)
                    Else
                        Dim pre = cf.Substring(0, star)
                        Dim suf = cf.Substring(star + 1)
                        For Each ef In existing
                            If ef.StartsWith(pre, StringComparison.OrdinalIgnoreCase) AndAlso ef.EndsWith(suf, StringComparison.OrdinalIgnoreCase) Then idleExpanded.Add(ef)
                        Next
                    End If
                Next
            Next
            ' Cobertura ESTRUCTURAL total = clips behavior ∪ IDLE-pattern-expanded.
            Dim structural As New HashSet(Of String)(enumSet, StringComparer.OrdinalIgnoreCase)
            structural.UnionWith(idleExpanded)
            Dim orphansAfterIdle = orphans.Where(Function(o) Not idleExpanded.Contains(o)).ToList()
            ' Residuo VERDADERO = ni IDLE-pattern, ni mismo-nombre-que-un-clip-resuelto (= recuperable por resolución
            ' por-subgraph sin colapsar variantes). Lo que quede acá es lo único sin fuente estructural conocida.
            Dim trueResidual = orphansAfterIdle.Where(Function(o) Not resolvedBases.Contains(baseOf(o))).ToList()
            Dim recoverablePerSubgraph = orphansAfterIdle.Count - trueResidual.Count
            Console.WriteLine($"      (i) IDLE-PATTERN-EXPAND: matcheados por patrones IDLE={idleExpanded.Count} | huérfanos cubiertos por IDLE={orphans.Count - orphansAfterIdle.Count} | resto={orphansAfterIdle.Count} (de ellos mismo-nombre→recuperable por-subgraph={recoverablePerSubgraph}) | RESIDUO VERDADERO={trueResidual.Count}")
            For Each o In trueResidual.Take(30) : Console.WriteLine($"        TRUE-residual: {o}") : Next
            Dim enumInFolder = enumSet.Where(Function(e) e.StartsWith(animPrefix, StringComparison.OrdinalIgnoreCase)).Count()
            Dim enumOutside = enumSet.Where(Function(e) Not e.StartsWith(animPrefix, StringComparison.OrdinalIgnoreCase)).OrderBy(Function(e) e).ToList()
            Console.WriteLine($"  (f) ORFANDAD '{actorRoot}\Animations\': existen={existing.Count} enumSet-total={enumSet.Count} enumSet-en-esta-carpeta={enumInFolder} enumSet-FUERA={enumOutside.Count} | ORFANOS(existen∧¬enum)={orphans.Count}")
            ' Carpetas TOP de los orphans (qué CLASE de animación queda fuera).
            Dim orphanByTop = orphans.GroupBy(Function(o) TopSegOf(FolderRelOf(o))).OrderByDescending(Function(grp) grp.Count()).ToList()
            Console.WriteLine($"      orphans por TOP-SEG: " & String.Join(" ; ", orphanByTop.Take(20).Select(Function(grp) $"{grp.Key}={grp.Count()}")))
            ' Set de carpetas que las rutas SAPT declaran (canon, dir). ¿Los orphans caen en carpetas SAPT (buscadas pero
            ' no referenciadas por ningún clip-generator) o en carpetas que NINGÚN SAPT busca?
            ' MISMO filtro de identidad que EnumerateClips: solo subgraphs APLICADOS (excluye los que piden identidad
            ' de OTRA raza) → para robots de carpeta compartida, su SAPT queda DENTRO de su subcarpeta (no trae Protectron).
            Dim kwSet As New HashSet(Of UInteger)(rb.ActorKeywords)
            Dim saptDirs As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each sg In rb.Subgraphs
                Dim foreignId = sg.ActorKeywordFormIDs.FirstOrDefault(Function(k) RaceBehaviorResolver.IsRaceIdentityKeyword(k) AndAlso Not kwSet.Contains(k))
                If foreignId <> 0UI Then Continue For
                For Each sp In sg.AnimationPaths
                    If String.IsNullOrWhiteSpace(sp) Then Continue For
                    saptDirs.Add(CanonHkx(sp.Replace("/"c, "\"c).TrimEnd("\"c)))
                Next
            Next
            Dim dirOf = Function(p As String) As String
                            Dim k = p.LastIndexOf("\"c) : Return If(k > 0, p.Substring(0, k), "")
                        End Function
            Dim orphanInSapt = orphans.Where(Function(o) saptDirs.Contains(dirOf(o))).ToList()
            Console.WriteLine($"      SAPT-dirs declarados={saptDirs.Count} | orphans EN carpeta-SAPT (buscada, no-referenciada)={orphanInSapt.Count} | orphans en carpeta NO-SAPT={orphans.Count - orphanInSapt.Count}")
            ' VALIDACIÓN del scope de cobertura por SAPT-SUBTREE: el set de archivos que la raza BUSCA = todo .hkx
            ' bajo algún SAPT-dir (o su subárbol). Si un orphan está bajo un SAPT-subtree, es recuperable como
            ' "presente en la search-path pero no referenciado estáticamente". Para robots de carpeta compartida esto
            ' se queda DENTRO de su subcarpeta (CreateABot\Animations\Assaultron) → NO trae anims de otras razas.
            Dim saptDirList = saptDirs.Where(Function(d) Not String.IsNullOrEmpty(d)).ToList()
            Dim underSapt = Function(p As String) As Boolean
                                For Each d In saptDirList
                                    If p.StartsWith(d & "\", StringComparison.OrdinalIgnoreCase) Then Return True
                                Next
                                Return False
                            End Function
            Dim orphansUnderSapt = orphans.Where(Function(o) underSapt(o)).ToList()
            Dim residual = orphans.Where(Function(o) Not underSapt(o)).ToList()
            Console.WriteLine($"      COVERAGE por SAPT-subtree: orphans recuperables(bajo SAPT)={orphansUnderSapt.Count} | residual(NO bajo SAPT)={residual.Count}")
            Console.WriteLine($"      residual NO-SAPT (lo que ni la search-path busca) muestra:")
            For Each o In residual.Take(20) : Console.WriteLine($"        residual: {o}") : Next
        Next
    End Sub

    ''' <summary>Dump completo de un NIF: árbol de NiNodes (parent, local.T, world.T) + por shape
    ''' skinneada el palette (bone, bind.T, inv(bind).T = world que el skin exige). Para auditar el
    ''' PLACEMENT de chunks (¿el C-X interno del chunk coincide con el socket del rig? ¿inv(bind)
    ''' coincide con el world del rig o está corrido por el local del socket = double-count?).</summary>
    Private Sub NifDumpRun(path As String)
        Dim nbx = LoadAnimCand(path)
        If nbx Is Nothing Then Console.WriteLine($"[nifdump] '{path}' no carga") : Return
        Dim nif As New Nifcontent_Class_Manolo() : nif.Load_Manolo(nbx)
        Console.WriteLine($"[nifdump] {path}")
        Console.WriteLine("  ── NODOS ──")
        For Each blk In nif.Blocks
            Dim nn = TryCast(blk, NiflySharp.Blocks.NiNode)
            If nn Is Nothing OrElse nn.Name Is Nothing Then Continue For
            Dim nm = If(nn.Name.String, "")
            If nm = "" Then Continue For
            Dim par = TryCast(nif.GetParentNode(nn), NiflySharp.Blocks.NiNode)
            Dim pn = If(par?.Name?.String, "<root>")
            Dim w = Transform_Class.GetGlobalTransform(nn, nif)
            Dim lw = If(par Is Nothing, w, Transform_Class.GetGlobalTransform(par, nif).Inverse().ComposeTransforms(w))
            Dim fl = nn.Flags_ui
            Dim nas = ""
            If (fl And &H10000UI) <> 0 Then nas &= "X"
            If (fl And &H20000UI) <> 0 Then nas &= "Y"
            If (fl And &H40000UI) <> 0 Then nas &= "Z"
            If (fl And &H80000UI) <> 0 Then nas &= "S"
            Dim nasTxt = If(nas = "", "", $"  ***NoAnimSync=[{nas}]***")
            Console.WriteLine($"   '{nm}' parent='{pn}'  flags=0x{fl:X6}{nasTxt}  local.T=({lw.Translation.X:F3},{lw.Translation.Y:F3},{lw.Translation.Z:F3})  world.T=({w.Translation.X:F3},{w.Translation.Y:F3},{w.Translation.Z:F3})")
        Next
        Console.WriteLine("  ── BSConnectPoint::Parents (sockets que PUBLICA, local al bone padre) ──")
        For Each cp In BSConnectPointReader.ReadParents(nif)
            Console.WriteLine($"   '{cp.Name}' parentBone='{cp.ParentBoneName}'  T=({cp.Translation.X:F3},{cp.Translation.Y:F3},{cp.Translation.Z:F3})  R=({cp.Rotation.X:F3},{cp.Rotation.Y:F3},{cp.Rotation.Z:F3},{cp.Rotation.W:F3}) scale={cp.Scale:F3}")
        Next
        Dim childNames = BSConnectPointReader.ReadChildrenNames(nif)
        If childNames.Count > 0 Then Console.WriteLine($"  ── BSConnectPoint::Children (sockets que CONSUME): {String.Join(", ", childNames)}")
        Console.WriteLine("  ── SKIN BINDS (inv(bind) = world que el skin exige, en frame del chunk) ──")
        For blkIdx = 0 To nif.Blocks.Count - 1
            Dim shp = TryCast(nif.Blocks(blkIdx), NiflySharp.INiShape)
            If shp Is Nothing Then Continue For
            Dim rs As NifRenderableShape
            Try
                rs = New NifRenderableShape(nif, shp, blkIdx)
            Catch ex As Exception
                Continue For
            End Try
            If rs.ShapeBones.Count = 0 Then Continue For
            ' Bounds de la malla en frame MODEL (para saber en qué frame están autoreados los binds).
            Dim boundsTxt = ""
            Try
                Dim verts = rs.Geometry?.GetVertexPositions()
                If verts IsNot Nothing AndAlso verts.Count > 0 Then
                    Dim mnX = Single.MaxValue, mnY = Single.MaxValue, mnZ = Single.MaxValue
                    Dim mxX = Single.MinValue, mxY = Single.MinValue, mxZ = Single.MinValue
                    For Each v In verts
                        mnX = Math.Min(mnX, v.X) : mnY = Math.Min(mnY, v.Y) : mnZ = Math.Min(mnZ, v.Z)
                        mxX = Math.Max(mxX, v.X) : mxY = Math.Max(mxY, v.Y) : mxZ = Math.Max(mxZ, v.Z)
                    Next
                    boundsTxt = $"  meshBounds=[({mnX:F1},{mnY:F1},{mnZ:F1})..({mxX:F1},{mxY:F1},{mxZ:F1})] center=({(mnX + mxX) / 2:F1},{(mnY + mxY) / 2:F1},{(mnZ + mxZ) / 2:F1})"
                End If
            Catch
            End Try
            Console.WriteLine($"   shape '{rs.ShapeName}': {rs.ShapeBones.Count} bones{boundsTxt}")
            For k = 0 To Math.Min(rs.ShapeBones.Count, rs.ShapeBoneTransforms.Count) - 1
                Dim bnNode = TryCast(rs.ShapeBones(k), NiflySharp.Blocks.NiNode)
                Dim nm = If(bnNode?.Name?.String, "?")
                Dim b = rs.ShapeBoneTransforms(k)
                If b Is Nothing Then Continue For
                Dim ib = b.Inverse()
                Console.WriteLine($"     bone '{nm}'  bind.T=({b.Translation.X:F3},{b.Translation.Y:F3},{b.Translation.Z:F3})  inv(bind).T=({ib.Translation.X:F3},{ib.Translation.Y:F3},{ib.Translation.Z:F3})")
            Next
        Next
    End Sub

    ''' <summary>FK helper memoizado: world(nm) = world(parent) ∘ localFn(nm) (raíz = localFn(nm)).</summary>
    Private Function FkWorld(nm As String, cache As Dictionary(Of String, Transform_Class),
                             parentOf As Dictionary(Of String, String),
                             localFn As Func(Of String, Transform_Class)) As Transform_Class
        Dim cached As Transform_Class = Nothing
        If cache.TryGetValue(nm, cached) Then Return cached
        Dim p As String = ""
        parentOf.TryGetValue(nm, p)
        Dim w As Transform_Class
        If Not String.IsNullOrEmpty(p) AndAlso parentOf.ContainsKey(p) Then
            w = FkWorld(p, cache, parentOf, localFn).ComposeTransforms(localFn(nm))
        Else
            w = localFn(nm)   ' raíz del chunk (relativa al origen del NIF)
        End If
        cache(nm) = w
        Return w
    End Function

    ''' <summary>Escanea todos los .hkx del FilesDictionary y tallya el blendHint (hkaAnimationBinding). Reporta la
    ''' distribución (0=NORMAL, 1=ADDITIVE, 2=ADDITIVE_DEPRECATED) + ejemplos por valor ≠0 + FLAGGEA cualquier valor
    ''' ∉{0,1,2} (donde el viejo `≠0` de la app difería del motor `∈{1,2}`). filter="all"/"*" = todos; si no, substring.</summary>
    Private Sub BlendHintScanRun(filter As String)
        Dim substr = If(filter = "all" OrElse filter = "*", "", filter)
        ' Cross-game: si el arg es un path a .bsa/.ba2 existente (ej. "Skyrim - Animations.bsa" de SSE), montarlo runtime
        ' y escanear TODO lo montado (usar --data apuntando a una Data sin auto-discovery ⇒ dict arranca vacío ⇒ puro SSE).
        For Each token In filter.Split(";"c)
            Dim p = token.Trim()
            If (p.EndsWith(".bsa", StringComparison.OrdinalIgnoreCase) OrElse p.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase)) AndAlso System.IO.File.Exists(p) Then
                Try
                    FilesDictionary_class.RegisterArchive(p)
                    Console.WriteLine($"[blendhintscan] montado: {p}")
                    substr = ""
                Catch ex As Exception
                    Console.WriteLine($"[blendhintscan] NO pudo montar '{p}': {ex.Message}")
                End Try
            End If
        Next
        Dim keys = FilesDictionary_class.Dictionary.Keys.
                       Where(Function(k) k.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) AndAlso
                                         (substr = "" OrElse k.IndexOf(substr, StringComparison.OrdinalIgnoreCase) >= 0)).
                       OrderBy(Function(k) k, StringComparer.OrdinalIgnoreCase).ToList()
        Console.WriteLine($"[blendhintscan] {keys.Count} .hkx (filtro='{filter}') — parseando bindings...")
        Dim tally As New Dictionary(Of Integer, Integer)
        Dim examples As New Dictionary(Of Integer, List(Of String))
        Dim fileOk = 0, parseErr = 0, noBinding = 0, done = 0
        For Each k In keys
            done += 1
            If done Mod 2000 = 0 Then Console.WriteLine($"   ...{done}/{keys.Count}")
            Dim bytes As Byte() = Nothing
            Try
                bytes = FilesDictionary_class.GetBytes(k)
            Catch
            End Try
            If bytes Is Nothing OrElse bytes.Length = 0 Then Continue For
            Try
                Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(bytes))
                Dim bindings = g.GetObjectsByClassName("hkaAnimationBinding").ToList()
                If bindings.Count = 0 Then noBinding += 1 : Continue For
                Dim anyRead = False
                For Each b In bindings
                    Dim ab = g.ParseAnimationBinding(b)
                    If ab Is Nothing Then Continue For
                    anyRead = True
                    Dim h = ab.BlendHint
                    tally(h) = tally.GetValueOrDefault(h, 0) + 1
                    If h <> 0 Then
                        If Not examples.ContainsKey(h) Then examples(h) = New List(Of String)
                        If examples(h).Count < 20 Then examples(h).Add(k)
                    End If
                Next
                If anyRead Then fileOk += 1 Else noBinding += 1
            Catch
                parseErr += 1
            End Try
        Next
        Console.WriteLine($"[blendhintscan] archivos parseOk={fileOk} parseErr={parseErr} sinBinding={noBinding}")
        Console.WriteLine("[blendhintscan] === distribución blendHint (por binding) ===")
        For Each kv In tally.OrderBy(Function(x) x.Key)
            Dim label = If(kv.Key = 0, "NORMAL", If(kv.Key = 1, "ADDITIVE_DEPRECATED", If(kv.Key = 2, "ADDITIVE", "⚠ RARO ∉{0,1,2}")))
            Console.WriteLine($"   blendHint={kv.Key,-4} {label,-22} = {kv.Value} binding(s)")
        Next
        For Each kv In examples.OrderBy(Function(x) x.Key)
            Console.WriteLine($"[blendhintscan] --- ejemplos blendHint={kv.Key} ({If(kv.Key = 1, "ADDITIVE_DEPRECATED", If(kv.Key = 2, "ADDITIVE", "RARO"))}) ---")
            For Each ex In kv.Value : Console.WriteLine($"      {ex}") : Next
        Next
        Dim raros = tally.Keys.Where(Function(h) h <> 0 AndAlso h <> 1 AndAlso h <> 2).ToList()
        Console.WriteLine($"[blendhintscan] ⇒ valores ∉{{0,1,2}} = {raros.Count} ({If(raros.Count = 0, "NINGUNO ⇒ app ≠0 y motor {1,2} coinciden en TODO el contenido", String.Join(",", raros))})")
    End Sub

    ''' <summary>VALIDACIÓN No-Anim-Sync (engine-exact). Para un chunk NIF montado, hace FK del árbol de bones
    ''' dos veces sobre un frame del clip: (BUGGY) = local del clip COMPLETO (rot+T+S) = lo que hace hoy la app;
    ''' (HONORED) = rotación del clip pero traslación/escala ESTRUCTURAL (del bind) para las componentes con el
    ''' flag NiAVObject No Anim Sync X/Y/Z/S (bits 16-19 de Flags_ui) = lo que hace el motor (pose-writer 0x1413995D0).
    ''' Reporta, por bone: flags, |clipT−bindT| (lo que el flag ignora), y el TEAR mundial (BUGGY vs HONORED).
    ''' Verifica que HONORED preserva el bone-length (=bind) ⇒ brazo rígido ⇒ conectado. El T-pose (sin clip) es
    ''' bindWorld en ambos (localFn=bindLocal) ⇒ el fix NO lo toca. spec="&lt;chunkNif&gt;|&lt;rigHkx&gt;|&lt;clipHkx&gt;[|frame][|boneFilter]".</summary>
    Private Sub AnimSyncCheck(spec As String)
        Dim parts = spec.Split("|"c)
        If parts.Length < 3 Then Console.WriteLine("[animsync] uso: --animsynccheck ""<chunkNif>|<rigHkx>|<clipHkx>[|frame][|boneFilter]""") : Return
        Dim chunkPath = parts(0).Trim(), rigPath = parts(1).Trim(), clipPath = parts(2).Trim()
        Dim frameArg = If(parts.Length > 3 AndAlso parts(3).Trim() <> "", parts(3).Trim(), "mid")
        Dim boneFilter = If(parts.Length > 4, parts(4).Trim(), "")

        ' ── CHUNK NIF: name → bindLocal, bindWorld, parent, flags ──
        Dim nbx = LoadAnimCand(chunkPath)
        If nbx Is Nothing Then Console.WriteLine($"[animsync] chunk '{chunkPath}' no carga") : Return
        Dim nif As New Nifcontent_Class_Manolo() : nif.Load_Manolo(nbx)
        Dim bindLocal As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        Dim bindWorld As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        Dim parentOf As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim flagsOf As New Dictionary(Of String, UInteger)(StringComparer.OrdinalIgnoreCase)
        Dim order As New List(Of String)
        For Each blk In nif.Blocks
            Dim nn = TryCast(blk, NiflySharp.Blocks.NiNode)
            If nn Is Nothing OrElse nn.Name Is Nothing Then Continue For
            Dim nm = nn.Name.String
            If String.IsNullOrEmpty(nm) OrElse bindLocal.ContainsKey(nm) Then Continue For
            bindLocal(nm) = New Transform_Class(nn)
            bindWorld(nm) = Transform_Class.GetGlobalTransform(nn, nif)
            Dim par = TryCast(nif.GetParentNode(nn), NiflySharp.Blocks.NiNode)
            parentOf(nm) = If(par?.Name?.String, "")
            flagsOf(nm) = nn.Flags_ui
            order.Add(nm)
        Next

        ' ── RIG (refPose) + CLIP (binding track→bone) ──
        Dim rb = LoadAnimCand(rigPath)
        If rb Is Nothing Then Console.WriteLine($"[animsync] rig '{rigPath}' no carga") : Return
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(rb))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.ReferencePose IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        If skel Is Nothing Then Console.WriteLine("[animsync] rig sin hkaSkeleton") : Return
        Dim nBk = skel.Bones.Count
        Dim cbts = LoadAnimCand(clipPath)
        If cbts Is Nothing Then Console.WriteLine($"[animsync] clip '{clipPath}' no carga") : Return
        Dim cg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(cbts))
        Dim anim = cg.ParseAnimations().FirstOrDefault()
        If anim Is Nothing Then anim = cg.ParseLosslessAnimations().FirstOrDefault()
        If anim Is Nothing OrElse anim.NumFrames <= 0 Then Console.WriteLine("[animsync] clip sin animación legible") : Return
        Dim idxArr = If(anim.Binding?.TransformTrackToBoneIndices, New List(Of Short)())
        Dim frame As Integer
        If frameArg.Equals("mid", StringComparison.OrdinalIgnoreCase) Then
            frame = (anim.NumFrames - 1) \ 2
        ElseIf frameArg.Equals("last", StringComparison.OrdinalIgnoreCase) Then
            frame = anim.NumFrames - 1
        Else
            Integer.TryParse(frameArg, frame) : frame = Math.Max(0, Math.Min(anim.NumFrames - 1, frame))
        End If

        ' clipLocal: valores del clip por componente (los NO animados aquí caen a refPose del rig SOLO como
        ' relleno; el uso real gatea por 'maskOf'). Fallback app-faithful (bind) se aplica en buildLocal.
        Dim clipLocal As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        Dim maskOf As New Dictionary(Of String, (Tx As Boolean, Ty As Boolean, Tz As Boolean, R As Boolean, S As Boolean))(StringComparer.OrdinalIgnoreCase)
        For t = 0 To anim.NumTransformTracks - 1
            Dim bi = If(idxArr.Count > 0 AndAlso t < idxArr.Count, CInt(idxArr(t)), t)
            If bi < 0 OrElse bi >= nBk Then Continue For
            Dim nm = If(skel.Bones(bi).Name, "")
            If nm = "" OrElse clipLocal.ContainsKey(nm) Then Continue For
            Dim ht = anim.GetTransform(frame, t) : If ht Is Nothing Then Continue For
            Dim refp = skel.ReferencePose(bi)
            Dim tx = If(ht.TranslationXAnimated, If(ht.Translation IsNot Nothing, ht.Translation.X, 0.0F), If(refp.Translation IsNot Nothing, refp.Translation.X, 0.0F))
            Dim ty = If(ht.TranslationYAnimated, If(ht.Translation IsNot Nothing, ht.Translation.Y, 0.0F), If(refp.Translation IsNot Nothing, refp.Translation.Y, 0.0F))
            Dim tz = If(ht.TranslationZAnimated, If(ht.Translation IsNot Nothing, ht.Translation.Z, 0.0F), If(refp.Translation IsNot Nothing, refp.Translation.Z, 0.0F))
            Dim rr = If(ht.RotationAnimated, ht.Rotation, refp.Rotation)
            Dim sx = If(ht.ScaleXAnimated, If(ht.Scale IsNot Nothing, ht.Scale.X, 1.0F), If(refp.Scale IsNot Nothing, refp.Scale.X, 1.0F))
            Dim sy = If(ht.ScaleYAnimated, If(ht.Scale IsNot Nothing, ht.Scale.Y, 1.0F), If(refp.Scale IsNot Nothing, refp.Scale.Y, 1.0F))
            Dim sz = If(ht.ScaleZAnimated, If(ht.Scale IsNot Nothing, ht.Scale.Z, 1.0F), If(refp.Scale IsNot Nothing, refp.Scale.Z, 1.0F))
            clipLocal(nm) = HkxTransformConventionHelper.ToTransform(tx, ty, tz, rr, sx, sy, sz)
            maskOf(nm) = (ht.TranslationXAnimated, ht.TranslationYAnimated, ht.TranslationZAnimated, ht.RotationAnimated, ht.ScaleXAnimated OrElse ht.ScaleYAnimated OrElse ht.ScaleZAnimated)
        Next

        Console.WriteLine($"[animsync] chunk='{chunkPath}'  frame={frame}/{anim.NumFrames - 1}  nodes={order.Count}  clipBones={clipLocal.Count}")

        ' ── locals reconstruidas APP-FAITHFUL: componente NO animada → bind (estructural), NO refPose del rig.
        ' Rotación → clip si anima (la rotación NUNCA es No-Anim-Sync). ignored (app hoy): componente animada → clip.
        ' honored (motor): idem PERO componente flagueada No-Anim-Sync → bind. Honored vs ignored SOLO difieren en
        ' componentes {animadas ∧ flagueadas}. ⇒ el tear medido es exactamente el que el flag evita.
        Dim buildLocal = Function(nm As String, honor As Boolean) As Transform_Class
                             Dim bl = bindLocal(nm)
                             ' Connect-points (C-/P-) los COLOCA el mounting (socket), NO los clip-anima la app
                             ' (consistente con el render: el brazo NO se va 78u, se desgarra 10-17u hacia la mano).
                             ' Anclarlos estructural en AMBOS modelos aísla el warp interno = el tear real.
                             If nm.StartsWith("C-", StringComparison.OrdinalIgnoreCase) OrElse nm.StartsWith("P-", StringComparison.OrdinalIgnoreCase) Then Return bl
                             Dim cl As Transform_Class = Nothing
                             If Not clipLocal.TryGetValue(nm, cl) Then Return bl   ' bone no en el clip → estructural
                             Dim mk As (Tx As Boolean, Ty As Boolean, Tz As Boolean, R As Boolean, S As Boolean)
                             maskOf.TryGetValue(nm, mk)
                             Dim fl As UInteger = 0 : flagsOf.TryGetValue(nm, fl)
                             Dim fx = (fl And &H10000UI) <> 0, fy = (fl And &H20000UI) <> 0, fz = (fl And &H40000UI) <> 0, fs = (fl And &H80000UI) <> 0
                             Dim bt = bl.Translation, ct = cl.Translation
                             Dim useCx = mk.Tx AndAlso Not (honor AndAlso fx)
                             Dim useCy = mk.Ty AndAlso Not (honor AndAlso fy)
                             Dim useCz = mk.Tz AndAlso Not (honor AndAlso fz)
                             Dim useCs = mk.S AndAlso Not (honor AndAlso fs)
                             Return New Transform_Class With {
                                 .Rotation = If(mk.R, cl.Rotation, bl.Rotation),
                                 .Translation = New System.Numerics.Vector3(If(useCx, ct.X, bt.X), If(useCy, ct.Y, bt.Y), If(useCz, ct.Z, bt.Z)),
                                 .Scale = If(useCs, cl.Scale, bl.Scale)
                             }
                         End Function
        Dim honoredLocal As Func(Of String, Transform_Class) = Function(nm As String) buildLocal(nm, True)
        Dim ignoredLocal As Func(Of String, Transform_Class) = Function(nm As String) buildLocal(nm, False)

        ' Sanity: FK(bindLocal) reproduce bindWorld (GetGlobalTransform) — control positivo del FK.
        Dim cBind As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        Dim maxBindErr As Single = 0
        For Each nm In order
            Dim w = FkWorld(nm, cBind, parentOf, Function(x) bindLocal(x))
            maxBindErr = Math.Max(maxBindErr, (w.Translation - bindWorld(nm).Translation).Length())
        Next
        Console.WriteLine($"[animsync] control FK vs GetGlobalTransform: maxErr={maxBindErr:F4} (debe ~0)")

        Dim cHon As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        Dim cIgn As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)

        Console.WriteLine("   bone                         flags     NoAnimSync  animMask     inject|ign-bind|  worldTear(BUGGY-HONORED)  boneLen(hon/bind)")
        Dim maxTear As Single = 0, maxTearBone As String = ""
        Dim anyLenBreak As Boolean = False
        For Each nm In order
            If boneFilter <> "" AndAlso nm.IndexOf(boneFilter, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            Dim fl As UInteger = 0 : flagsOf.TryGetValue(nm, fl)
            Dim nas = ""
            If (fl And &H10000UI) <> 0 Then nas &= "X"
            If (fl And &H20000UI) <> 0 Then nas &= "Y"
            If (fl And &H40000UI) <> 0 Then nas &= "Z"
            If (fl And &H80000UI) <> 0 Then nas &= "S"
            Dim cl As Transform_Class = Nothing
            Dim animated = clipLocal.TryGetValue(nm, cl)
            Dim mk As (Tx As Boolean, Ty As Boolean, Tz As Boolean, R As Boolean, S As Boolean)
            maskOf.TryGetValue(nm, mk)
            Dim mkTxt = If(animated, $"T:{If(mk.Tx, "x", "-")}{If(mk.Ty, "y", "-")}{If(mk.Tz, "z", "-")} R:{If(mk.R, "a", "-")} S:{If(mk.S, "a", "-")}", "(no en clip)")
            ' [ADD-ORDER DIAG] additive delta = {animado:clip, no-animado:identidad}. Comparo el effective bajo los DOS
            ' órdenes de composición: base×additive (S∘delta, lo que hace la app HOY) vs additive×base (delta∘S, el fix).
            ' Si additive×base deja el bone ~en rest (d≈0) y base×additive lo dispara (d grande) ⇒ el bug es el ORDEN.
            If animated Then
                Dim ident As New Transform_Class()
                Dim addLoc As New Transform_Class With {
                    .Rotation = If(mk.R, cl.Rotation, ident.Rotation),
                    .Translation = New System.Numerics.Vector3(If(mk.Tx, cl.Translation.X, 0.0F), If(mk.Ty, cl.Translation.Y, 0.0F), If(mk.Tz, cl.Translation.Z, 0.0F)),
                    .Scale = 1.0F
                }
                Dim bl2 = bindLocal(nm)
                Dim effBA = bl2.ComposeTransforms(addLoc)       ' base × additive  (S ∘ delta) = app HOY
                Dim effAB = addLoc.ComposeTransforms(bl2)        ' additive × base  (delta ∘ S) = fix propuesto
                Dim rest = bl2.Translation
                Console.WriteLine($"   [ADD-ORDER {nm}] rest=({rest.X:F1},{rest.Y:F1},{rest.Z:F1})  baseXadd=({effBA.Translation.X:F1},{effBA.Translation.Y:F1},{effBA.Translation.Z:F1}) d={(effBA.Translation - rest).Length():F1}  |  addXbase=({effAB.Translation.X:F1},{effAB.Translation.Y:F1},{effAB.Translation.Z:F1}) d={(effAB.Translation - rest).Length():F1}")
            End If
            Dim dInj = (ignoredLocal(nm).Translation - bindLocal(nm).Translation).Length()   ' local que la app inyecta vs estructural
            Dim wH = FkWorld(nm, cHon, parentOf, honoredLocal)
            Dim wI = FkWorld(nm, cIgn, parentOf, ignoredLocal)
            Dim tear = (wI.Translation - wH.Translation).Length()
            If tear > maxTear Then maxTear = tear : maxTearBone = nm
            Dim lenHon = honoredLocal(nm).Translation.Length()
            Dim lenBind = bindLocal(nm).Translation.Length()
            ' bone-length SOLO debe preservarse en bones FLAGUEADOS (para no-flag el clip legítimamente cambia el largo).
            Dim flagged = (nas <> "")
            Dim lenOk = (Not flagged) OrElse Math.Abs(lenHon - lenBind) < 0.01F
            If flagged AndAlso Not lenOk Then anyLenBreak = True
            Console.WriteLine($"   {nm,-28} 0x{fl:X6}  [{nas,-4}]     {mkTxt,-16} {dInj,10:F3}      {tear,10:F3}              {lenHon,7:F3}/{lenBind,7:F3}{If(flagged AndAlso Not lenOk, " ⚠LEN", "")}")
        Next
        Console.WriteLine($"[animsync] MAX TEAR (BUGGY vs HONORED) = {maxTear:F3}u en '{maxTearBone}'  |  bone-length honored preservado = {(Not anyLenBreak)}")
        Console.WriteLine($"[animsync] ⇒ honrando No Anim Sync, la traslación/escala del clip se descarta en bones flagueados: brazo rígido (bone-len=bind) ⇒ CONECTADO. Sin honrar (app hoy): tear={maxTear:F3}u = el desgarro.")
        Console.WriteLine($"[animsync] T-pose: sin clip ⇒ localFn=bindLocal en ambos ⇒ world=bindWorld (control maxErr={maxBindErr:F4}) ⇒ el fix NO altera el T-pose.")
    End Sub

    ''' <summary>REDISEÑO MOUNT — medición de clipBaseLocal. Por track del clip: el LOCAL que el clip
    ''' reproduce en FRAME 0 (componentes no animados resueltos del refPose del rig, igual que
    ''' HkxPoseImportSession.BuildFrameLocalTransform) comparado contra (a) el local del RIG
    ''' (referencePose del skeleton.hkx) y (b) el local ENSAMBLADO derivado de los skin binds del
    ''' chunk NIF (inv(bind) = world ensamblado, hecho probado [DIAG-BIND-BAKE]≡[MOUNTDELTA-WRITE]).
    ''' Verifica las predicciones del modelo M_b = assembledLocal × inv(clipBaseLocal):
    '''   Assaultron montados: clip0 ≈ ASM (M≈I) · Codsworth Pelvis: clip0 ≈ RIG (M=mount) · Humano: clip0 ≈ RIG (M=I).</summary>
    Private Sub ClipBaseDump(spec As String)
        Dim parts = spec.Split("|"c)
        If parts.Length < 2 Then Console.WriteLine("[clipbase] uso: --clipbase ""<rigHkx>|<clipHkx>[|boneFilter[|chunkNif;chunkNif...]]""") : Return
        Dim rigPath = parts(0), clipPath = parts(1)
        Dim boneFilter = If(parts.Length > 2, parts(2), "")
        Dim chunkPaths = If(parts.Length > 3 AndAlso parts(3) <> "",
                            parts(3).Split(";"c).Where(Function(p) p.Trim() <> "").Select(Function(p) p.Trim()).ToList(),
                            New List(Of String))

        ' ── RIG: skeleton de animación (no-Ragdoll) → locals (refPose) + parent names ──
        Dim rb = LoadAnimCand(rigPath)
        If rb Is Nothing Then Console.WriteLine($"[clipbase] rig '{rigPath}' no carga") : Return
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(rb))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.ParentIndices IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        If skel Is Nothing Then Console.WriteLine("[clipbase] rig sin hkaSkeleton de animación") : Return
        Dim nB = skel.Bones.Count
        Dim rigLocal(nB - 1) As Transform_Class
        Dim parentName(nB - 1) As String
        For i = 0 To nB - 1
            rigLocal(i) = HkxTransformConventionHelper.ToTransform(skel.ReferencePose(i))
            Dim p = If(i < skel.ParentIndices.Count, CInt(skel.ParentIndices(i)), -1)
            parentName(i) = If(p >= 0 AndAlso p < nB, If(skel.Bones(p).Name, ""), "")
        Next

        ' ── CLIP: spline o lossless; binding track→boneIdx; ¿skeleton embebido? ──
        Dim cbts = LoadAnimCand(clipPath)
        If cbts Is Nothing Then Console.WriteLine($"[clipbase] clip '{clipPath}' no carga") : Return
        Dim cg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(cbts))
        Dim anim = cg.ParseAnimations().FirstOrDefault()
        If anim Is Nothing Then anim = cg.ParseLosslessAnimations().FirstOrDefault()
        If anim Is Nothing OrElse anim.NumFrames <= 0 Then Console.WriteLine("[clipbase] clip sin animación legible") : Return
        Dim embSkels = cg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) cg.ParseSkeleton(o)).
                          Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.Bones.Count > 0).ToList()
        Dim idxArr = If(anim.Binding?.TransformTrackToBoneIndices, New List(Of Short)())
        Console.WriteLine($"[clipbase] rig='{rigPath}' skel='{skel.Name}' bones={nB}")
        Console.WriteLine($"[clipbase] clip='{clipPath}' frames={anim.NumFrames} tracks={anim.NumTransformTracks} bindingTracks={idxArr.Count} blendHint={If(anim.Binding Is Nothing, "?", anim.Binding.BlendHint.ToString())} origSkel='{If(anim.Binding?.OriginalSkeletonName, "")}' embeddedSkeletons={embSkels.Count}{If(embSkels.Count > 0, " (" & String.Join(",", embSkels.Select(Function(s) $"'{s.Name}'×{s.Bones.Count}")) & ")", "")}")

        ' ── CHUNKS: por chunk NIF, world ensamblado por bone vía skin binds (inv(bind)) + node tree ──
        Dim chunkData As New List(Of (Name As String, SkinW As Dictionary(Of String, Transform_Class), NodeW As Dictionary(Of String, Transform_Class)))
        For Each cp In chunkPaths
            Dim nbx = LoadAnimCand(cp)
            If nbx Is Nothing Then Console.WriteLine($"[clipbase] chunk '{cp}' no carga — SKIP") : Continue For
            Dim nif As New Nifcontent_Class_Manolo() : nif.Load_Manolo(nbx)
            Dim skinW As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
            Dim nodeW As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
            For blkIdx = 0 To nif.Blocks.Count - 1
                Dim shp = TryCast(nif.Blocks(blkIdx), NiflySharp.INiShape)
                If shp Is Nothing Then Continue For
                Dim rs As NifRenderableShape
                Try
                    rs = New NifRenderableShape(nif, shp, blkIdx)
                Catch ex As Exception
                    Continue For
                End Try
                For k = 0 To Math.Min(rs.ShapeBones.Count, rs.ShapeBoneTransforms.Count) - 1
                    Dim bnNode = TryCast(rs.ShapeBones(k), NiflySharp.Blocks.NiNode)
                    Dim nm = If(bnNode?.Name?.String, "")
                    If nm = "" OrElse rs.ShapeBoneTransforms(k) Is Nothing Then Continue For
                    Dim w = rs.ShapeBoneTransforms(k).Inverse()
                    Dim prev As Transform_Class = Nothing
                    If skinW.TryGetValue(nm, prev) Then
                        Dim dPrev = (prev.Translation - w.Translation).Length()
                        If dPrev > 0.1F Then Console.WriteLine($"[clipbase] ⚠ bind CONFLICT '{nm}' en '{cp}': dT={dPrev:F3} (se queda el primero)")
                    Else
                        skinW(nm) = w
                    End If
                Next
            Next
            For Each blk In nif.Blocks
                Dim nn = TryCast(blk, NiflySharp.Blocks.NiNode)
                If nn Is Nothing OrElse nn.Name Is Nothing Then Continue For
                Dim nm = nn.Name.String
                If String.IsNullOrEmpty(nm) OrElse nodeW.ContainsKey(nm) Then Continue For
                nodeW(nm) = Transform_Class.GetGlobalTransform(nn, nif)
            Next
            Dim shortName = cp.Substring(cp.LastIndexOf("\"c) + 1)
            chunkData.Add((shortName, skinW, nodeW))
            Console.WriteLine($"[clipbase] chunk '{shortName}': skinBinds={skinW.Count} nodes={nodeW.Count}")
        Next

        ' ── Por track: clip frame-0 local (mask-aware) vs rigLocal vs assembledLocal ──
        Dim rotAngle = Function(A As Transform_Class, B As Transform_Class) As Double
                           Dim d = A.Inverse().ComposeTransforms(B)
                           Dim trc = CDbl(d.Rotation.M11) + CDbl(d.Rotation.M22) + CDbl(d.Rotation.M33)
                           Return Math.Acos(Math.Max(-1.0, Math.Min(1.0, (trc - 1.0) / 2.0))) * 180.0 / Math.PI
                       End Function
        Const T_EPS As Single = 0.5F
        Const R_EPS As Double = 2.0
        Dim cntRig = 0, cntAsm = 0, cntBoth = 0, cntNeither = 0, cntNoAsm = 0
        Dim neitherList As New List(Of String)
        Console.WriteLine("   track bone                       mask        rig.T                clip0.T              dT_rig θ_rig | asm.T (src)            dT_asm θ_asm | veredicto")
        For t = 0 To anim.NumTransformTracks - 1
            Dim bi = If(idxArr.Count > 0 AndAlso t < idxArr.Count, CInt(idxArr(t)), t)
            If bi < 0 OrElse bi >= nB Then Continue For
            Dim nm = If(skel.Bones(bi).Name, "")
            If nm = "" Then Continue For
            Dim ht = anim.GetTransform(0, t)
            If ht Is Nothing Then Continue For
            Dim refp = skel.ReferencePose(bi)
            ' Componentes no animados → refPose del rig (misma semántica que BuildFrameLocalTransform).
            Dim tx = If(ht.TranslationXAnimated, If(ht.Translation IsNot Nothing, ht.Translation.X, 0.0F), If(refp.Translation IsNot Nothing, refp.Translation.X, 0.0F))
            Dim ty = If(ht.TranslationYAnimated, If(ht.Translation IsNot Nothing, ht.Translation.Y, 0.0F), If(refp.Translation IsNot Nothing, refp.Translation.Y, 0.0F))
            Dim tz = If(ht.TranslationZAnimated, If(ht.Translation IsNot Nothing, ht.Translation.Z, 0.0F), If(refp.Translation IsNot Nothing, refp.Translation.Z, 0.0F))
            Dim rr = If(ht.RotationAnimated, ht.Rotation, refp.Rotation)
            Dim sx = If(ht.ScaleXAnimated, If(ht.Scale IsNot Nothing, ht.Scale.X, 1.0F), If(refp.Scale IsNot Nothing, refp.Scale.X, 1.0F))
            Dim sy = If(ht.ScaleYAnimated, If(ht.Scale IsNot Nothing, ht.Scale.Y, 1.0F), If(refp.Scale IsNot Nothing, refp.Scale.Y, 1.0F))
            Dim sz = If(ht.ScaleZAnimated, If(ht.Scale IsNot Nothing, ht.Scale.Z, 1.0F), If(refp.Scale IsNot Nothing, refp.Scale.Z, 1.0F))
            Dim clip0 = HkxTransformConventionHelper.ToTransform(tx, ty, tz, rr, sx, sy, sz)
            Dim mask = $"T:{If(ht.TranslationXAnimated, "x", "-")}{If(ht.TranslationYAnimated, "y", "-")}{If(ht.TranslationZAnimated, "z", "-")} R:{If(ht.RotationAnimated, "a", "-")} S:{If(ht.ScaleXAnimated OrElse ht.ScaleYAnimated OrElse ht.ScaleZAnimated, "a", "-")}"

            Dim rigL = rigLocal(bi)
            Dim dTr = (clip0.Translation - rigL.Translation).Length()
            Dim thr = rotAngle(rigL, clip0)

            ' assembledLocal: bone Y parent (del RIG) presentes en el MISMO chunk (mismo frame).
            Dim asmL As Transform_Class = Nothing
            Dim asmSrc = ""
            Dim pn = parentName(bi)
            If pn <> "" Then
                ' SOLO bind/bind en el MISMO chunk: inv(bind) está en el frame del chunk; el placement
                ' cancela únicamente si AMBOS worlds salen de los binds del mismo NIF. El node-tree del
                ' chunk es otro frame (probado: mezclar bind/node da locals absurdos de 60-154u).
                For Each ch In chunkData
                    Dim wb As Transform_Class = Nothing, wp As Transform_Class = Nothing
                    If ch.SkinW.TryGetValue(nm, wb) AndAlso ch.SkinW.TryGetValue(pn, wp) Then
                        asmL = wp.Inverse().ComposeTransforms(wb)
                        asmSrc = $"bind:{ch.Name}"
                        Exit For
                    End If
                Next
            End If

            Dim dTa As Single = -1.0F
            Dim tha As Double = -1.0
            If asmL IsNot Nothing Then
                dTa = (clip0.Translation - asmL.Translation).Length()
                tha = rotAngle(asmL, clip0)
            End If

            ' Veredicto por TRASLACIÓN: la rotación del frame 0 es POSE del clip (stance), no estructura;
            ' los mounts de configuración medidos son traslacionales. θ se imprime como contexto.
            Dim nearRig = dTr < T_EPS
            Dim nearAsm = asmL IsNot Nothing AndAlso dTa < T_EPS
            Dim verdict As String
            If asmL Is Nothing Then
                cntNoAsm += 1
                verdict = If(nearRig, "CLIP≈RIG", "≠RIG ⚠")
                If Not nearRig Then cntNeither += 1 : neitherList.Add($"{nm} (dT_rig={dTr:F2} θ={thr:F1}, sin asm)") Else cntRig += 1
            ElseIf nearRig AndAlso nearAsm Then
                cntBoth += 1 : verdict = "RIG==ASM(≈clip)"
            ElseIf nearAsm Then
                cntAsm += 1 : verdict = "CLIP≈ASM (M≈I)"
            ElseIf nearRig Then
                cntRig += 1 : verdict = "CLIP≈RIG (M=mount)"
            Else
                cntNeither += 1 : verdict = "NEITHER ⚠"
                neitherList.Add($"{nm} (dT_rig={dTr:F2} θr={thr:F1} dT_asm={dTa:F2} θa={tha:F1})")
            End If

            If boneFilter <> "" AndAlso nm.IndexOf(boneFilter, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            Dim rT = rigL.Translation, cT = clip0.Translation
            Dim asmStr = If(asmL Is Nothing, "—", $"({asmL.Translation.X:F2},{asmL.Translation.Y:F2},{asmL.Translation.Z:F2}) ({asmSrc})")
            Dim dTaStr = If(asmL Is Nothing, "  —", $"{dTa,6:F2}")
            Dim thaStr = If(asmL Is Nothing, "  —", $"{tha,5:F1}")
            Console.WriteLine($"   [{t,3}] {nm,-26} {mask,-11} ({rT.X,7:F2},{rT.Y,7:F2},{rT.Z,7:F2}) ({cT.X,7:F2},{cT.Y,7:F2},{cT.Z,7:F2}) {dTr,6:F2} {thr,5:F1} | {asmStr,-22} {dTaStr} {thaStr} | {verdict}")
        Next
        Console.WriteLine($"[clipbase] RESUMEN: CLIP≈RIG={cntRig}  CLIP≈ASM={cntAsm}  RIG==ASM={cntBoth}  NEITHER={cntNeither}  (sinAsmDisponible={cntNoAsm}; umbrales dT<{T_EPS} θ<{R_EPS}°)")
        If neitherList.Count > 0 Then
            Console.WriteLine($"[clipbase] NEITHER ({neitherList.Count}): {String.Join("  |  ", neitherList.Take(20))}")
        End If
    End Sub

    ''' <summary>Compara el LAYOUT INTERNO de un chunk-NIF (posiciones de sus bones relativas al bone-ancla
    ''' compartido más root-most) contra CreateABot.hkx. Si el layout COINCIDE ⇒ el chunk está autoreado igual
    ''' a CreateABot y el offset de ensamblaje (9–18u) viene de la CADENA (host P-X), no del chunk ⇒ el re-bind
    ''' al HKX es posible (alinear el ancla). Si DIFIERE ⇒ el chunk está autoreado distinto ⇒ no encastra rígido.</summary>
    Private Sub ChunkLayoutCompare(chunkNifPath As String)
        Dim hkxPath = "Actors\CreateABot\CharacterAssets\skeleton.hkx"
        Dim nbx = LoadAnimCand(chunkNifPath) : Dim hbx = LoadAnimCand(hkxPath)
        If nbx Is Nothing OrElse hbx Is Nothing Then Console.WriteLine($"[chunkcompare] falta archivo (nif={nbx IsNot Nothing}, hkx={hbx IsNot Nothing})") : Return
        ' Chunk NIF: nodo → world + depth.
        Dim nif As New Nifcontent_Class_Manolo() : nif.Load_Manolo(nbx)
        Dim cWorld As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        Dim cDepth As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
        For Each blk In nif.Blocks
            Dim nn = TryCast(blk, NiflySharp.Blocks.NiNode)
            If nn Is Nothing OrElse nn.Name Is Nothing Then Continue For
            Dim nm = nn.Name.String
            If String.IsNullOrEmpty(nm) OrElse cWorld.ContainsKey(nm) Then Continue For
            cWorld(nm) = Transform_Class.GetGlobalTransform(nn, nif)
            Dim d = 0 : Dim cur = TryCast(nif.GetParentNode(nn), NiflySharp.Blocks.NiNode)
            While cur IsNot Nothing AndAlso d < 200 : d += 1 : cur = TryCast(nif.GetParentNode(cur), NiflySharp.Blocks.NiNode) : End While
            cDepth(nm) = d
        Next
        ' CreateABot HKX: bone → world.
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(hbx))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.ParentIndices IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        If skel Is Nothing Then Console.WriteLine("[chunkcompare] CreateABot sin skeleton anim") : Return
        Dim nB = skel.Bones.Count
        Dim hWorld(nB - 1) As Transform_Class
        Dim hByName As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        For i = 0 To nB - 1
            Dim loc = HkxTransformConventionHelper.ToTransform(skel.ReferencePose(i))
            Dim p = If(i < skel.ParentIndices.Count, CInt(skel.ParentIndices(i)), -1)
            hWorld(i) = If(p < 0 OrElse p >= i, loc, hWorld(p).ComposeTransforms(loc))
            Dim nm = skel.Bones(i).Name
            If Not String.IsNullOrEmpty(nm) AndAlso Not hByName.ContainsKey(nm) Then hByName(nm) = hWorld(i)
        Next
        ' Bones compartidos + ancla = el más root-most en el chunk.
        Dim sharedBones = cWorld.Keys.Where(Function(k) hByName.ContainsKey(k)).OrderBy(Function(k) cDepth(k)).ToList()
        If sharedBones.Count = 0 Then Console.WriteLine("[chunkcompare] sin bones compartidos con CreateABot") : Return
        Dim refBone = sharedBones(0)
        Console.WriteLine($"[chunkcompare] {chunkNifPath}")
        Console.WriteLine($"   shared con CreateABot = {sharedBones.Count} | ancla (root-most) = '{refBone}'")
        Dim cRefInv = cWorld(refBone).Inverse()
        Dim hRefInv = hByName(refBone).Inverse()
        Dim maxDT = 0.0F, maxDR = 0.0F
        For Each b In sharedBones
            Dim relC = cRefInv.ComposeTransforms(cWorld(b))
            Dim relH = hRefInv.ComposeTransforms(hByName(b))
            Dim dT = (relC.Translation - relH.Translation).Length()
            Dim cr = relC.Rotation, hr = relH.Rotation
            Dim dR = Math.Abs(cr.M11 - hr.M11) + Math.Abs(cr.M12 - hr.M12) + Math.Abs(cr.M13 - hr.M13) +
                     Math.Abs(cr.M21 - hr.M21) + Math.Abs(cr.M22 - hr.M22) + Math.Abs(cr.M23 - hr.M23) +
                     Math.Abs(cr.M31 - hr.M31) + Math.Abs(cr.M32 - hr.M32) + Math.Abs(cr.M33 - hr.M33)
            maxDT = Math.Max(maxDT, dT) : maxDR = Math.Max(maxDR, dR)
            If dT > 0.1F OrElse dR > 0.02F Then Console.WriteLine($"   {b,-22}: rel-vs-CreateABot dT={dT:F2} dR={dR:F2}")
        Next
        Dim match = maxDT < 0.5F AndAlso maxDR < 0.05F
        Console.WriteLine($"[chunkcompare] maxDT={maxDT:F2} maxDR={maxDR:F2} ⇒ {If(match, "LAYOUT == CreateABot (el offset de ensamblaje viene de la CADENA/host-P-X, NO del chunk → re-bind al HKX posible)", "LAYOUT DIFIERE de CreateABot (chunk autoreado distinto → no encastra rígido al HKX)")}")
    End Sub

    ''' <summary>Vuelca de un HKX: clip generators (Name + AnimationName) + characters (animationNames +
    ''' behaviorFilename + rigName). Para ver el linking DIRECTO clip↔anim sin heurísticas de path.</summary>
    Private Sub DumpBehaviorClips(path As String)
        Dim b = LoadAnimCand(path)
        If b Is Nothing Then Console.WriteLine($"[dumpbeh] '{path}' no carga") : Return
        Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(b))
        ' hkaAnimationBinding crudo (si el archivo es una ANIMACIÓN): el blendHint canónico vive acá.
        For Each o In g.GetObjectsByClassName("hkaAnimationBinding")
            Dim bytes As New System.Text.StringBuilder()
            For off = 0 To Math.Min(o.Size - 1, &H4F)
                If off Mod 16 = 0 Then bytes.Append($"{Environment.NewLine}       +{off:X2}: ")
                bytes.Append($"{g.ReadByte(o.RelativeOffset + off):X2} ")
            Next
            Console.WriteLine($"   [hkaAnimationBinding] size={o.Size}{bytes}")
        Next
        Dim cgs = g.GetObjectsByClassName("hkbClipGenerator").Select(Function(o) g.ParseClipGenerator(o)).Where(Function(c) c IsNot Nothing).ToList()
        Dim refs = g.GetObjectsByClassName("hkbBehaviorReferenceGenerator").Select(Function(o) g.ResolveLocalString(o.RelativeOffset + &H88)).Where(Function(s) Not String.IsNullOrEmpty(s)).Distinct().ToList()
        Console.WriteLine($"[dumpbeh] {path}: clipGenerators={cgs.Count} behaviorRefs={refs.Count}")
        For Each r In refs
            Console.WriteLine($"   behaviorRef→ {r}")
        Next
        For Each c In cgs.Take(80)
            Console.WriteLine($"   clipGen '{c.Name}' anim='{c.AnimationName}' mode={c.PlaybackMode} flags=0x{c.FlagsRaw:X2}")
        Next
        ' Histograma de clases del grafo + QUIÉN referencia a cada clip generator (jerarquía:
        ' la aditividad puede declararla el PADRE — layer/blender — no el clip generator).
        Dim classHist = g.Objects.GroupBy(Function(o) o.ClassName).OrderByDescending(Function(grp) grp.Count())
        Console.WriteLine("   ── clases del grafo ──")
        For Each grp In classHist
            Console.WriteLine($"     {grp.Count(),4}x {grp.Key}")
        Next
        ' Generators DINÁMICOS (DATG/BSiState/ManualSelector): qué generan (DescribeGenerator recursa a las hojas-clip)
        ' + clases referenciadas. Para ver si LISTAN estáticamente los variantes (to_<mood>/alt_) o los arman en runtime.
        For Each cls In {"DynamicAnimationTaggingGenerator", "BSiStateTaggingGenerator", "hkbManualSelectorGenerator"}
            For Each o In g.GetObjectsByClassName(cls)
                Dim nm = g.ReadNodeName(o)
                Dim refClasses As New List(Of String)
                For Each gf In g.GetGlobalFixupsInRange(o.RelativeOffset, o.Size)
                    Dim t = g.GetObject(gf.TargetRelativeOffset) : If t IsNot Nothing Then refClasses.Add(t.ClassName)
                Next
                Dim strs As New List(Of String)
                For Each lf In g.GetLocalFixupsInRange(o.RelativeOffset, o.Size)
                    Dim s = g.ReadNullTerminatedString(lf.DestinationRelativeOffset)
                    If Not String.IsNullOrWhiteSpace(s) AndAlso s.Length < 80 AndAlso s.All(Function(ch) AscW(ch) >= 32 AndAlso AscW(ch) <= 126) Then strs.Add(s)
                Next
                Console.WriteLine($"   [{cls}] '{nm}' → {g.DescribeGenerator(o)} | strings=[{String.Join(" | ", strs.Distinct())}] | refs=[{String.Join(",", refClasses.Distinct())}]")
                ' Decode COMPLETO del DATG (para hallar el tag): todos los global-fixups (offset→target), int32/float por offset.
                If cls = "DynamicAnimationTaggingGenerator" Then
                    For Each gf In g.GetGlobalFixupsInRange(o.RelativeOffset, o.Size)
                        Dim t = g.GetObject(gf.TargetRelativeOffset)
                        Console.WriteLine($"        +{gf.SourceRelativeOffset - o.RelativeOffset:X2} → {If(t Is Nothing, "?", t.ClassName & " '" & g.ReadNodeName(t) & "'")}")
                    Next
                    Dim ascii As New System.Text.StringBuilder()
                    For off = &H40 To o.Size - 1
                        Dim bb = g.ReadByte(o.RelativeOffset + off)
                        ascii.Append(If(bb >= 32 AndAlso bb <= 126, ChrW(bb), "."c))
                    Next
                    Console.WriteLine($"        ASCII +40..: {ascii}")
                End If
            Next
        Next
        ' hkbBlenderGenerator / hkbLayerGenerator / hkbLayer: hex + qué clips alcanza cada uno
        ' (vía sus children) — para ubicar el campo ADDITIVE binario por diff additive-vs-normal.
        Dim describeReach = Function(o As HkxVirtualObjectGraph_Class) As String
                                Dim reach As New List(Of String)
                                For Each gf In g.GetGlobalFixupsInRange(o.RelativeOffset, o.Size)
                                    Dim t1 = g.GetObject(gf.TargetRelativeOffset)
                                    If t1 Is Nothing Then Continue For
                                    If t1.ClassName = "hkbClipGenerator" Then reach.Add(g.ReadNodeName(t1))
                                    For Each gf2 In g.GetGlobalFixupsInRange(t1.RelativeOffset, t1.Size)
                                        Dim t2 = g.GetObject(gf2.TargetRelativeOffset)
                                        If t2 IsNot Nothing AndAlso t2.ClassName = "hkbClipGenerator" Then reach.Add(g.ReadNodeName(t2))
                                    Next
                                Next
                                Return String.Join(",", reach.Distinct().Take(6))
                            End Function
        For Each cls In {"hkbBlenderGenerator", "hkbLayerGenerator", "hkbLayer"}
            For Each o In g.GetObjectsByClassName(cls)
                Dim nm = g.ReadNodeName(o)
                Dim bytes As New System.Text.StringBuilder()
                For off = &H30 To Math.Min(o.Size - 1, &H9F)
                    If (off - &H30) Mod 16 = 0 Then bytes.Append($"{Environment.NewLine}       +{off:X2}: ")
                    bytes.Append($"{g.ReadByte(o.RelativeOffset + off):X2} ")
                Next
                Console.WriteLine($"   [{cls}] '{nm}' size={o.Size} reach=[{describeReach(o)}]{bytes}")
            Next
        Next
        Console.WriteLine("   ── referenciadores de clip generators (padre → clip) ──")
        Dim cgByOffset = cgs.Where(Function(x) x.SourceObject IsNot Nothing).ToDictionary(Function(x) x.SourceObject.RelativeOffset, Function(x) x)
        For Each o In g.Objects
            For Each gf In g.GetGlobalFixupsInRange(o.RelativeOffset, o.Size)
                Dim tgt As HkbClipGeneratorGraph_Class = Nothing
                If cgByOffset.TryGetValue(gf.TargetRelativeOffset, tgt) Then
                    Dim parentName = g.ReadNodeName(o)
                    Console.WriteLine($"     {o.ClassName} '{parentName}' → clipGen '{tgt.Name}'")
                End If
            Next
        Next
        For Each o In g.GetObjectsByClassName("hkbCharacterStringData")
            Dim csd = g.ParseCharacterStringData(o)
            If csd Is Nothing Then Continue For
            Console.WriteLine($"   [character] name='{csd.CharacterName}' rig='{csd.RigName}' behaviorFile='{csd.BehaviorFilename}' animNames={csd.AnimationFilenames.Count}")
            For Each an In csd.AnimationFilenames.Take(40)
                Console.WriteLine($"      animName: {an}")
            Next
        Next
    End Sub

    ''' <summary>EDID de un FormID (keyword, etc.) vía PluginManager; "" si fid=0, hex si no resuelve.</summary>
    Private Function EdidOf(pm As PluginManager, fid As UInteger) As String
        If fid = 0UI Then Return ""
        Try
            Dim r = pm.GetRecord(fid)
            Return If(r IsNot Nothing AndAlso Not String.IsNullOrEmpty(r.EditorID), r.EditorID, $"0x{fid:X8}")
        Catch
            Return $"0x{fid:X8}"
        End Try
    End Function

    ' ── Helpers de path (canon / combine) replicados de la lib para el audit de cobertura.
    Private Function CanonHkx(p As String) As String
        If String.IsNullOrEmpty(p) Then Return ""
        p = p.Replace("/"c, "\"c)
        If p.StartsWith("Meshes\", StringComparison.OrdinalIgnoreCase) Then p = p.Substring(7)
        If p.EndsWith(".hkt", StringComparison.OrdinalIgnoreCase) Then p = p.Substring(0, p.Length - 4) & ".hkx"
        Return p.ToLowerInvariant()
    End Function
    Private Function HkxCategory(p As String) As String
        Dim lp = p.ToLowerInvariant()
        If lp.Contains("\animations\") Then Return "Animation"
        If lp.Contains("\behaviors\") Then Return "Behavior"
        If lp.Contains("\characters\") Then Return "Character"
        If lp.Contains("\characterassets\") Then Return "Skeleton/Ragdoll"
        If lp.EndsWith("project.hkx") OrElse lp.EndsWith("project.hkt") Then Return "Project"
        Return "Other"
    End Function
    Private Function DirNameC(p As String) As String
        Dim i = p.LastIndexOf("\"c) : Return If(i > 0, p.Substring(0, i), "")
    End Function
    Private Function CombineC(root As String, rel As String) As String
        If String.IsNullOrWhiteSpace(rel) Then Return ""
        Dim lc = rel.Replace("/"c, "\"c).TrimStart("\"c)
        If lc.StartsWith("actors\", StringComparison.OrdinalIgnoreCase) OrElse lc.StartsWith("meshes\", StringComparison.OrdinalIgnoreCase) OrElse root = "" Then Return lc
        Return root.TrimEnd("\"c) & "\" & lc
    End Function
    Private Function ResolveRelC(root As String, rel As String) As String
        Dim combined = CombineC(root, rel)
        Dim stack As New List(Of String)
        For Each seg In combined.Split("\"c)
            If seg = "" OrElse seg = "." Then Continue For
            If seg = ".." Then
                If stack.Count > 0 Then stack.RemoveAt(stack.Count - 1)
            Else
                stack.Add(seg)
            End If
        Next
        Return String.Join("\", stack)
    End Function
    Private Function ActorRootC(p As String) As String
        For Each m In {"\Animations\", "\CharacterAssets\", "\Characters\", "\Behaviors\"}
            Dim i = p.IndexOf(m, StringComparison.OrdinalIgnoreCase)
            If i > 0 Then Return p.Substring(0, i)
        Next
        Return DirNameC(p)
    End Function

    ''' <summary>Audit de cobertura: cuenta TODOS los .hkx/.hkt del load order y verifica que cada uno esté
    ''' referenciado por alguna raza (project / character / skeleton-rig / behavior recursivo / animación).
    ''' Reporta referenciado vs HUÉRFANO por categoría. Los huérfanos = no alcanzables desde ningún RACE.</summary>
    Private Sub HkxCoverageScan(pm As PluginManager)
        ' (1) Inventario de todos los .hkx/.hkt (canon → categoría).
        Dim allFiles As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Each key In FilesDictionary_class.Dictionary.Keys
            If Not (key.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) OrElse key.EndsWith(".hkt", StringComparison.OrdinalIgnoreCase)) Then Continue For
            Dim c = CanonHkx(key)
            If Not allFiles.ContainsKey(c) Then allFiles(c) = HkxCategory(key)
        Next
        Console.WriteLine($"[hkxcoverage] total .hkx/.hkt en load order (canon dedup) = {allFiles.Count}")

        ' (2) Conjunto REFERENCIADO: caminar todas las razas con behavior.
        Dim referenced As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim behVisited As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim loader As Func(Of String, Byte()) = Function(p) FO4_NPC_Manager.MeshPathHelpers.TryLoadMeshBytes(p, 0)
        Dim races = pm.GetRecordsOfType("RACE")
        Dim nRaces = 0
        For Each rec In races
            Dim race As RACE_Data = Nothing
            Try : race = RecordParsers.ParseRACE(rec, pm) : Catch : Continue For : End Try
            If race Is Nothing Then Continue For
            Dim rb = RaceBehaviorResolver.ResolveRaceBehavior(race.FormID, pm)
            If rb Is Nothing OrElse (rb.MaleProject = "" AndAlso rb.FemaleProject = "") Then Continue For
            nRaces += 1
            AddRefC(referenced, rb.MaleProject) : AddRefC(referenced, rb.FemaleProject)
            AddRefC(referenced, BehaviorClipEnumerator.ResolveHavokSkeleton(rb, loader))
            For Each proj In {rb.MaleProject, rb.FemaleProject}.Where(Function(p) p <> "").Distinct(StringComparer.OrdinalIgnoreCase)
                CollectProjectRefs(proj, referenced)
            Next
            For Each sg In rb.Subgraphs
                CollectBehaviorRefs(sg.BehaviourGraph, referenced, behVisited, 0)
            Next
            Try
                For Each c In BehaviorClipEnumerator.EnumerateClips(rb, loader)
                    AddRefC(referenced, c.AnimationFile)
                Next
            Catch
            End Try
        Next
        Console.WriteLine($"[hkxcoverage] razas con behavior caminadas = {nRaces} | archivos referenciados = {referenced.Count}")

        ' (3) Reporte por categoría: referenciado vs NO-referenciado-por-raza.
        Dim allOrphans As New List(Of String)
        For Each cat In {"Animation", "Behavior", "Character", "Skeleton/Ragdoll", "Project", "Other"}
            Dim inCat = allFiles.Where(Function(kv) kv.Value = cat).Select(Function(kv) kv.Key).ToList()
            Dim orph = inCat.Where(Function(k) Not referenced.Contains(k)).ToList()
            allOrphans.AddRange(orph)
            Console.WriteLine($"  {cat,-16}: total={inCat.Count,5}  ref-por-raza={inCat.Count - orph.Count,5}  NO-ref-por-raza={orph.Count,5}")
        Next
        ' (4) Sub-categorizar los NO-referenciados por patrón (¿no-race legítimo, o gap real de raza?).
        Dim pat = Function(p As String) As String
                      If p.Contains("\_1stperson\") OrElse p.Contains("\1stperson\") Then Return "1stPerson"
                      If p.Contains("\weapons\") OrElse p.Contains("teslacannon") OrElse p.Contains("\weapon\") Then Return "Weapon-anim"
                      If p.Contains("\hair\") Then Return "Hair (cloth/facebones)"
                      If p.StartsWith("animobjects\") OrElse p.StartsWith("furniture\") Then Return "AnimObject/Furniture-obj"
                      If p.StartsWith("weapons\") Then Return "Weapon(root)"
                      If p.StartsWith("vehicles\") OrElse p.Contains("carproject") Then Return "Vehicle"
                      If p.StartsWith("pipboy\") OrElse p.Contains("\pipboy") Then Return "Pipboy"
                      If p.Contains("\_test") OrElse p.StartsWith("actors\_test") Then Return "Test"
                      If p.Contains("\critter") OrElse p.Contains("\crow\") OrElse p.Contains("\gulper\") OrElse p.Contains("\dlc03\") Then Return "Critter/Creature-DLC"
                      If p.StartsWith("actors\") Then Return "actors\… (cuerpo de actor — REVISAR)"
                      Return "otro"
                  End Function
        Console.WriteLine($"  --- NO-referenciados por RAZA, por patrón (total {allOrphans.Count}) ---")
        For Each grp In allOrphans.GroupBy(Function(o) pat(o)).OrderByDescending(Function(g) g.Count())
            Console.WriteLine($"     {grp.Key,-34}: {grp.Count()}")
        Next
        Dim suspect = allOrphans.Where(Function(o) pat(o).StartsWith("actors\…")).OrderBy(Function(o) o).ToList()
        Console.WriteLine($"  --- SOSPECHOSOS (actors\… cuerpo huérfano = posible gap de raza): {suspect.Count} ---")
        For Each s In suspect.Take(25) : Console.WriteLine($"      {s}") : Next
    End Sub

    Private Sub AddRefC(referenced As HashSet(Of String), p As String)
        If Not String.IsNullOrWhiteSpace(p) Then referenced.Add(CanonHkx(p))
    End Sub
    ' project → CharacterFilenames → character (rig + behaviorFile + animationNames). Todo referenciado.
    Private Sub CollectProjectRefs(proj As String, referenced As HashSet(Of String))
        Dim actorRoot = DirNameC(proj)
        Dim pb = LoadAnimCand(proj)
        If pb Is Nothing Then Return
        Try
            Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(pb))
            For Each o In g.GetObjectsByClassName("hkbProjectStringData")
                Dim psd = g.ParseProjectStringData(o)
                If psd Is Nothing Then Continue For
                For Each cf In psd.CharacterFilenames
                    If String.IsNullOrWhiteSpace(cf) Then Continue For
                    Dim charPath = CombineC(actorRoot, cf)
                    AddRefC(referenced, charPath)
                    Dim cb = LoadAnimCand(charPath)
                    If cb Is Nothing Then cb = LoadAnimCand(cf)
                    If cb Is Nothing Then Continue For
                    Dim gc = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(cb))
                    For Each co In gc.GetObjectsByClassName("hkbCharacterStringData")
                        Dim csd = gc.ParseCharacterStringData(co)
                        If csd Is Nothing Then Continue For
                        AddRefC(referenced, CombineC(actorRoot, csd.RigName))
                        AddRefC(referenced, CombineC(actorRoot, csd.BehaviorFilename))
                        For Each an In csd.AnimationFilenames
                            AddRefC(referenced, ResolveRelC(actorRoot, an))
                        Next
                    Next
                Next
            Next
        Catch
        End Try
    End Sub
    ' behavior + sus referencias (hkbBehaviorReferenceGenerator) recursivamente. visited global (coverage).
    Private Sub CollectBehaviorRefs(behFile As String, referenced As HashSet(Of String), visited As HashSet(Of String), depth As Integer)
        If String.IsNullOrWhiteSpace(behFile) OrElse depth > 12 Then Return
        Dim canon = CanonHkx(behFile)
        If Not visited.Add(canon) Then Return
        referenced.Add(canon)
        Dim bb = LoadAnimCand(behFile)
        If bb Is Nothing Then Return
        Try
            Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(bb))
            Dim behRoot = ActorRootC(behFile)
            For Each refObj In g.GetObjectsByClassName("hkbBehaviorReferenceGenerator")
                Dim refName = g.ResolveLocalString(refObj.RelativeOffset + &H88)
                If Not String.IsNullOrWhiteSpace(refName) Then CollectBehaviorRefs(CombineC(behRoot, refName), referenced, visited, depth + 1)
            Next
        Catch
        End Try
    End Sub

    ' KYWD TNAM Type enum (xEdit wbDefinitionsFO4.pas:5213 wbKeywordTypeEnum).
    Private ReadOnly KeywordTypeNames As String() = {
        "None", "Component Tech Level", "Attach Point", "Component Property", "Instantiation Filter",
        "Mod Association", "Sound", "Anim Archetype", "Function Call", "Recipe Filter", "Attraction Type",
        "Dialogue Subtype", "Quest Target", "Anim Flavor", "Anim Gender", "Anim Face", "Quest Group",
        "Anim Injured", "Dispel Effect"}
    Private Function KwTypeName(t As UInteger) As String
        Return If(t < CUInt(KeywordTypeNames.Length), KeywordTypeNames(CInt(t)), $"Type{t}")
    End Function

    ''' <summary>Lista KYWD (EDID + TNAM Type) que matchean un substr. El TNAM Type es el discriminador
    ''' AUTORITATIVO identidad-vs-estado de los SAKD: 'Anim Injured'(17)/'Anim Archetype'(7)/'Anim Flavor'(13)
    ''' = gates de ESTADO runtime (no filtrar por raza); 'None'(0) = keyword de identidad ('Anims&lt;X&gt;Race').</summary>
    Private Sub KwTypeScan(pm As PluginManager, substr As String)
        Dim kws = pm.GetRecordsOfType("KYWD")
        Console.WriteLine($"[kwtype] {kws.Count} KYWD records | filtro edid='{substr}'")
        Dim shown = 0
        Dim byType As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
        For Each rec In kws
            Dim edid = rec.EditorID
            If String.IsNullOrEmpty(edid) Then Continue For
            If substr <> "" AndAlso edid.IndexOf(substr, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            Dim t As UInteger = 0
            For Each sr In rec.Subrecords
                If sr.Signature = "TNAM" AndAlso sr.Data IsNot Nothing AndAlso sr.Data.Length >= 4 Then
                    t = BitConverter.ToUInt32(sr.Data, 0) : Exit For
                End If
            Next
            Dim tn = KwTypeName(t)
            byType(tn) = If(byType.ContainsKey(tn), byType(tn) + 1, 1)
            If shown < 60 Then Console.WriteLine($"   {edid,-42} TNAM={t} '{tn}'")
            shown += 1
        Next
        Console.WriteLine($"[kwtype] matcheados={shown}")
        For Each kv In byType.OrderByDescending(Function(x) x.Value)
            Console.WriteLine($"   tipo {kv.Key,-22}: {kv.Value}")
        Next
    End Sub

    ''' <summary>Mapa de ESTADO por raza: clasifica cada subgraph por el TIPO (KYWD.TNAM) de sus keywords SAKD,
    ''' NO por su string. Regla type-driven (sin listas hardcodeadas): una keyword de tipo 'None' que pertenece a
    ''' la KWDA de ALGUNA raza = discriminador de IDENTIDAD; cualquier otro tipo ('Anim Injured/Archetype/Flavor/
    ''' Gender/Face') = EJE DE ESTADO runtime. Un subgraph se EXCLUYE solo si requiere una identidad de OTRA raza
    ''' (kw tipo None, ∈ KWDA de alguna raza, ∉ esta raza). Muestra qué entra, por qué eje de estado, y qué queda
    ''' afuera (con la identidad ajena que lo gatea). Compara contra la regla VIEJA (SAKD ∩ KWDA) para ver el delta.</summary>
    Private Sub StateMapScan(pm As PluginManager, edidFilter As String)
        ' (1) KYWD → tipo (TNAM) y → EDID, leídos del record (no del nombre).
        Dim kwType As New Dictionary(Of UInteger, UInteger)
        Dim kwEdid As New Dictionary(Of UInteger, String)
        For Each rec In pm.GetRecordsOfType("KYWD")
            Dim t As UInteger = 0
            For Each sr In rec.Subrecords
                If sr.Signature = "TNAM" AndAlso sr.Data IsNot Nothing AndAlso sr.Data.Length >= 4 Then t = BitConverter.ToUInt32(sr.Data, 0) : Exit For
            Next
            kwType(rec.Header.FormID) = t
            kwEdid(rec.Header.FormID) = rec.EditorID
        Next
        ' (2) Identidades de raza = keywords tipo None que ALGUNA raza declara en KWDA (→ a quién pertenece).
        Dim owner As New Dictionary(Of UInteger, String)
        Dim races = pm.GetRecordsOfType("RACE")
        For Each rec In races
            Dim race As RACE_Data = Nothing
            Try : race = RecordParsers.ParseRACE(rec, pm) : Catch : Continue For : End Try
            If race Is Nothing Then Continue For
            For Each k In race.Keywords
                Dim tt As UInteger = 0 : kwType.TryGetValue(k, tt)
                If tt = 0UI AndAlso Not owner.ContainsKey(k) Then owner(k) = race.EditorID  ' None-typed ∧ en KWDA = identidad
            Next
        Next
        Console.WriteLine($"[statemap] KYWD={kwType.Count} | identidades-de-raza(None∧∈KWDA)={owner.Count} | filtro='{edidFilter}'")

        Dim isIdentity = Function(k As UInteger) owner.ContainsKey(k)               ' identidad de alguna raza
        Dim isState = Function(k As UInteger) As Boolean                           ' eje de estado = tipo ≠ None
                          Dim tt As UInteger = 0 : kwType.TryGetValue(k, tt) : Return tt <> 0UI
                      End Function

        Dim gEntries = 0, gOld = 0, gNew = 0, gRecovered = 0, gExcluded = 0
        Dim gAxisEntries As New Dictionary(Of String, Integer)
        For Each rec In races
            Dim race As RACE_Data = Nothing
            Try : race = RecordParsers.ParseRACE(rec, pm) : Catch : Continue For : End Try
            If race Is Nothing Then Continue For
            If race.MaleBehaviorGraphProject = "" AndAlso race.FemaleBehaviorGraphProject = "" Then Continue For
            If edidFilter <> "" AndAlso race.EditorID.IndexOf(edidFilter, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            Dim rb = RaceBehaviorResolver.ResolveRaceBehavior(race.FormID, pm)
            If rb Is Nothing Then Continue For
            Dim thisKw As New HashSet(Of UInteger)(race.Keywords)
            Dim ownId = race.Keywords.Where(Function(k) isIdentity(k)).Select(Function(k) kwEdid.GetValueOrDefault(k, $"0x{k:X8}"))

            ' Clasificar cada entry.
            Dim byAxis As New Dictionary(Of String, List(Of RACE_SubgraphData))(StringComparer.OrdinalIgnoreCase)
            Dim excluded As New List(Of (sd As RACE_SubgraphData, needs As String))
            Dim nOld = 0, nNew = 0, nRec = 0
            For Each sd In rb.Subgraphs
                ' Regla VIEJA (heurística): aplica si SAKD vacío o ∩ KWDA ≠ ∅.
                Dim oldApply = sd.ActorKeywordFormIDs.Count = 0 OrElse sd.ActorKeywordFormIDs.Any(Function(k) thisKw.Contains(k))
                ' Regla NUEVA (type-driven): excluir solo si requiere identidad AJENA (None ∧ de otra raza).
                Dim foreignId = sd.ActorKeywordFormIDs.FirstOrDefault(Function(k) isIdentity(k) AndAlso Not thisKw.Contains(k))
                Dim newApply = (foreignId = 0UI)
                If oldApply Then nOld += 1
                If newApply Then
                    nNew += 1
                    If Not oldApply Then nRec += 1   ' recuperado: lo aplicamos ahora y antes NO
                    ' Eje de estado = los tipos de las keywords de estado (TNAM ≠ None); si ninguna → Normal.
                    Dim stateTypes = sd.ActorKeywordFormIDs.Where(Function(k) isState(k)).
                                       Select(Function(k) KwTypeName(kwType.GetValueOrDefault(k, 0UI))).Distinct().OrderBy(Function(s) s).ToList()
                    Dim axis = If(stateTypes.Count = 0, "Normal", String.Join("+", stateTypes))
                    If Not byAxis.ContainsKey(axis) Then byAxis(axis) = New List(Of RACE_SubgraphData)
                    byAxis(axis).Add(sd)
                Else
                    excluded.Add((sd, kwEdid.GetValueOrDefault(foreignId, $"0x{foreignId:X8}") & "(None, de " & owner.GetValueOrDefault(foreignId, "?") & ")"))
                End If
            Next

            Console.WriteLine($"=== {race.EditorID} [0x{race.FormID:X8}] | identidad propia=[{String.Join(", ", ownId)}] ===")
            Console.WriteLine($"    subgraphs={rb.Subgraphs.Count} | OLD-aplica={nOld} | NEW-aplica={nNew} | RECUPERADOS(estado)={nRec} | FUERA(identidad ajena)={excluded.Count}")
            For Each kv In byAxis.OrderBy(Function(x) If(x.Key = "Normal", "", x.Key))
                Dim saptSet = kv.Value.SelectMany(Function(s) s.AnimationPaths).Select(Function(p) LastTwoSeg(p)).Distinct().Take(6)
                Dim tag = If(kv.Key <> "Normal" AndAlso kv.Value.Any(Function(s) Not (s.ActorKeywordFormIDs.Count = 0 OrElse s.ActorKeywordFormIDs.Any(Function(k) thisKw.Contains(k)))), "  <<< RECUPERADO", "")
                Console.WriteLine($"      [{kv.Key,-26}] x{kv.Value.Count,-3} roles={String.Join(",", kv.Value.Select(Function(s) RoleName(s.Role)).Distinct())}  SAPT≈[{String.Join(" ; ", saptSet)}]{tag}")
            Next
            If excluded.Count > 0 Then
                Console.WriteLine($"    FUERA por identidad AJENA:")
                For Each e In excluded.GroupBy(Function(x) System.IO.Path.GetFileName(x.sd.BehaviourGraph) & " ⟵ " & x.needs).OrderByDescending(Function(g) g.Count()).Take(10)
                    Console.WriteLine($"       x{e.Count(),-3} {e.Key}")
                Next
            End If

            gEntries += rb.Subgraphs.Count : gOld += nOld : gNew += nNew : gRecovered += nRec : gExcluded += excluded.Count
            For Each kv In byAxis : gAxisEntries(kv.Key) = gAxisEntries.GetValueOrDefault(kv.Key, 0) + kv.Value.Count : Next
        Next
        Console.WriteLine($"[statemap-TOTAL] entries={gEntries} | OLD-aplica={gOld} | NEW-aplica={gNew} | RECUPERADOS(estado-gated)={gRecovered} | FUERA(identidad ajena)={gExcluded}")
        Console.WriteLine($"[statemap-TOTAL] entries NEW-aplicados por EJE DE ESTADO:")
        For Each kv In gAxisEntries.OrderByDescending(Function(x) x.Value)
            Console.WriteLine($"      [{kv.Key,-26}] {kv.Value}")
        Next
    End Sub

    ''' <summary>Valida la resolución clip→archivo SIN heurística de nombres: por EXISTENCIA del archivo sobre las
    ''' rutas de búsqueda SAPT (el mecanismo de search-path del engine). Para cada clip prueba, en orden de
    ''' prioridad SAPT, (1) el path tal-como-autoreado y (2) el path sin el primer segmento (actor-autor del core
    ''' compartido); la EXISTENCIA en el índice de archivos decide. Camina root behavior + subgraphs aplicados
    ''' (filtro type-driven). Reporta cobertura % por raza + cómo resolvió (full/strip) + no-resueltos.</summary>
    Private Sub ClipResolveScan(pm As PluginManager, edidFilter As String)
        ' Índice de existencia: todos los .hkx/.hkt del load order, canon (sin Meshes\, .hkt→.hkx, lower).
        Dim animSet As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each key In FilesDictionary_class.Dictionary.Keys
            If key.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) OrElse key.EndsWith(".hkt", StringComparison.OrdinalIgnoreCase) Then animSet.Add(CanonHkx(key))
        Next
        ' KYWD type + identidades de raza (mismo discriminador type-driven que --statemap).
        Dim kwType As New Dictionary(Of UInteger, UInteger)
        For Each rec In pm.GetRecordsOfType("KYWD")
            Dim t As UInteger = 0
            For Each sr In rec.Subrecords
                If sr.Signature = "TNAM" AndAlso sr.Data IsNot Nothing AndAlso sr.Data.Length >= 4 Then t = BitConverter.ToUInt32(sr.Data, 0) : Exit For
            Next
            kwType(rec.Header.FormID) = t
        Next
        Dim races = pm.GetRecordsOfType("RACE")
        Dim owner As New HashSet(Of UInteger)
        For Each rec In races
            Dim rr As RACE_Data = Nothing
            Try : rr = RecordParsers.ParseRACE(rec, pm) : Catch : Continue For : End Try
            If rr Is Nothing Then Continue For
            For Each k In rr.Keywords
                If kwType.GetValueOrDefault(k, 0UI) = 0UI Then owner.Add(k)
            Next
        Next
        Console.WriteLine($"[clipresolve] index .hkx/.hkt={animSet.Count} | identidades-raza={owner.Count} | filtro='{edidFilter}'")

        Dim loader As Func(Of String, Byte()) = Function(p) FO4_NPC_Manager.MeshPathHelpers.TryLoadMeshBytes(p, 0)
        Dim gTot = 0, gRes = 0, gFull = 0, gStrip = 0, gAmbig = 0, gWeColl = 0
        For Each rec In races
            Dim race As RACE_Data = Nothing
            Try : race = RecordParsers.ParseRACE(rec, pm) : Catch : Continue For : End Try
            If race Is Nothing Then Continue For
            If race.MaleBehaviorGraphProject = "" AndAlso race.FemaleBehaviorGraphProject = "" Then Continue For
            If edidFilter <> "" AndAlso race.EditorID.IndexOf(edidFilter, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            Dim rb = RaceBehaviorResolver.ResolveRaceBehavior(race.FormID, pm)
            If rb Is Nothing OrElse String.IsNullOrWhiteSpace(rb.Project) Then Continue For
            Dim thisKw As New HashSet(Of UInteger)(race.Keywords)
            Dim actorRoot = DirNameC(rb.Project)
            Dim graphCache As New Dictionary(Of String, HkxObjectGraph_Class)(StringComparer.OrdinalIgnoreCase)

            Dim tot = 0, res = 0, full = 0, strip = 0, ambig = 0, weColl = 0
            Dim unresolved As New List(Of String)
            Dim ambigSamples As New List(Of String)
            Dim weCollSamples As New List(Of String)
            Dim resolvedFiles As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim unresDistinct As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            ' Conteo por clip: AMBIGÜEDAD (>1 candidato, casi todo fallback cross-entry benigno) + weColl (full Y
            ' strip distintos en la MISMA ruta = único desempate propio del código).
            Dim tally As Action(Of String, Boolean, (file As String, mode As String, cands As List(Of String))) =
                Sub(an, we, how)
                    tot += 1
                    If how.file <> "" Then
                        res += 1 : resolvedFiles.Add(how.file) : If how.mode = "strip" Then strip += 1 Else full += 1
                        If how.cands.Count > 1 Then
                            ambig += 1
                            If ambigSamples.Count < 4 Then ambigSamples.Add($"{an}  →  [{String.Join(" | ", how.cands)}]")
                        End If
                        If we Then
                            weColl += 1
                            If weCollSamples.Count < 6 Then weCollSamples.Add($"{an}  →  [{String.Join(" | ", how.cands)}]")
                        End If
                    Else
                        unresDistinct.Add(an) : If unresolved.Count < 6 Then unresolved.Add(an)
                    End If
                End Sub

            ' (1) ROOT behavior (project→character→behaviorFilename), SAPT nativo = actorRoot\Animations.
            Dim rootBeh = ResolveRootBehaviorFile(rb.Project, loader)
            If rootBeh <> "" Then
                Dim anims As New List(Of String)
                CollectClipAnims(rootBeh, loader, graphCache, New HashSet(Of String)(StringComparer.OrdinalIgnoreCase), anims, 0)
                For Each an In anims
                    Dim we As Boolean = False : Dim how = ResolveExist(an, New List(Of String), actorRoot, animSet, we) : tally(an, we, how)
                Next
            End If

            ' (2) Subgraphs APLICADOS (type-driven: excluir solo identidad ajena).
            For Each sd In rb.Subgraphs
                Dim foreignId = sd.ActorKeywordFormIDs.FirstOrDefault(Function(k) owner.Contains(k) AndAlso Not thisKw.Contains(k))
                If foreignId <> 0UI Then Continue For  ' identidad de OTRA raza → no aplica
                Dim anims As New List(Of String)
                CollectClipAnims(NormHkx(sd.BehaviourGraph), loader, graphCache, New HashSet(Of String)(StringComparer.OrdinalIgnoreCase), anims, 0)
                For Each an In anims
                    Dim we As Boolean = False : Dim how = ResolveExist(an, sd.AnimationPaths, actorRoot, animSet, we) : tally(an, we, how)
                Next
            Next

            Console.WriteLine($"=== {race.EditorID,-32} repertorio={resolvedFiles.Count,5}  NO-resueltos={unresDistinct.Count,4}  ambig(fallback)={ambig,6}  weColl(full≠strip MISMA ruta)={weColl}")
            For Each u In unresolved : Console.WriteLine($"      NO-RESUELTO {u}") : Next
            For Each a In weCollSamples : Console.WriteLine($"      weColl {a}") : Next
            ' Dump del repertorio (para verificar sharing entre razas vía intersección externa) cuando hay filtro.
            If edidFilter <> "" Then
                Dim dumpPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rep_{race.EditorID}.txt")
                System.IO.File.WriteAllLines(dumpPath, resolvedFiles.OrderBy(Function(x) x, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine($"      [dump] repertorio → {dumpPath}")
            End If
            gTot += tot : gRes += res : gFull += full : gStrip += strip : gAmbig += ambig : gWeColl += weColl
        Next
        Dim gp = If(gTot > 0, 100.0 * gRes / gTot, 100.0)
        Console.WriteLine($"[clipresolve-TOTAL] attempts={gTot} resueltos={gRes} ({gp:F1}%) | full={gFull} strip={gStrip}")
        Console.WriteLine($"[clipresolve-TOTAL] ambig(fallback cross-entry, benigno)={gAmbig} | weColl(full≠strip MISMA ruta = desempate propio)={gWeColl}")
    End Sub

    ' project → CharacterFilenames → character → behaviorFilename (root behavior del actor). "" si no resuelve.
    Private Function ResolveRootBehaviorFile(proj As String, loader As Func(Of String, Byte())) As String
        Dim actorRoot = DirNameC(proj)
        Dim pb = LoadAnimCand(proj) : If pb Is Nothing Then Return ""
        Try
            Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(pb))
            For Each o In g.GetObjectsByClassName("hkbProjectStringData")
                Dim psd = g.ParseProjectStringData(o) : If psd Is Nothing Then Continue For
                For Each cf In psd.CharacterFilenames
                    Dim cb = LoadAnimCand(CombineC(actorRoot, cf)) : If cb Is Nothing Then cb = LoadAnimCand(cf)
                    If cb Is Nothing Then Continue For
                    Dim gc = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(cb))
                    For Each co In gc.GetObjectsByClassName("hkbCharacterStringData")
                        Dim csd = gc.ParseCharacterStringData(co)
                        If csd IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(csd.BehaviorFilename) Then Return NormHkx(CombineC(actorRoot, csd.BehaviorFilename))
                    Next
                Next
            Next
        Catch
        End Try
        Return ""
    End Function

    ' Recolecta los animationName de los hkbClipGenerator de un behavior + sus hkbBehaviorReferenceGenerator (recursivo).
    Private Sub CollectClipAnims(behFile As String, loader As Func(Of String, Byte()), graphCache As Dictionary(Of String, HkxObjectGraph_Class),
                                 visited As HashSet(Of String), outAnims As List(Of String), depth As Integer)
        If depth > 12 OrElse String.IsNullOrWhiteSpace(behFile) OrElse Not visited.Add(behFile) Then Return
        Dim graph As HkxObjectGraph_Class = Nothing
        If Not graphCache.TryGetValue(behFile, graph) Then
            Dim bytes = LoadAnimCand(behFile)
            If bytes IsNot Nothing AndAlso bytes.Length > 0 Then
                Try : graph = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(bytes)) : Catch : End Try
            End If
            graphCache(behFile) = graph
        End If
        If graph Is Nothing Then Return
        For Each o In graph.GetObjectsByClassName("hkbClipGenerator")
            Dim cg = graph.ParseClipGenerator(o)
            If cg IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(cg.AnimationName) Then outAnims.Add(cg.AnimationName)
        Next
        Dim behRoot = ActorRootC(behFile)
        For Each ro In graph.GetObjectsByClassName("hkbBehaviorReferenceGenerator")
            Dim refName = graph.ResolveLocalString(ro.RelativeOffset + &H88)
            If Not String.IsNullOrWhiteSpace(refName) Then CollectClipAnims(NormHkx(CombineC(behRoot, refName)), loader, graphCache, visited, outAnims, depth + 1)
        Next
    End Sub

    ' Resolución por EXISTENCIA sobre rutas SAPT (search-path). Devuelve (file canon, mode: full/strip) o ("","").
    ' clipRel = parte del animName tras "Animations\". Para cada root SAPT (orden de prioridad): prueba el path
    ' completo (full) y luego sin el primer segmento (strip = saca el actor-autor del core compartido); la
    ' EXISTENCIA decide. Sin SAPT → root nativo = actorRoot\Animations.
    ' Resolución por EXISTENCIA sobre rutas SAPT (search-path). DETERMINÍSTICA: candidatos en orden de prioridad
    ' = (ruta SAPT en orden del record) × (full antes que strip); gana el PRIMERO que existe. cands = lista de
    ' archivos DISTINTOS que existen (cands.Count>1 ⇒ varios candidatos → el orden decide; informativo p/ auditar).
    ' weColl (ByRef): True si en ALGUNA ruta SAPT existen DOS archivos distintos por full Y strip a la vez — el
    ' único desempate que hace este código más allá del orden del record (full gana). Si nunca pasa ⇒ el código
    ' no agrega indeterminismo: el resultado lo fija el orden de rutas del record.
    Private Function ResolveExist(animName As String, saptFolders As List(Of String), actorRoot As String, animSet As HashSet(Of String), Optional ByRef weColl As Boolean = False) As (file As String, mode As String, cands As List(Of String))
        Dim none As (String, String, List(Of String)) = ("", "", New List(Of String))
        If String.IsNullOrWhiteSpace(animName) Then Return none
        Dim norm = animName.Replace("/"c, "\"c)
        Dim i = norm.IndexOf("Animations\", StringComparison.OrdinalIgnoreCase)
        Dim clipRel = If(i >= 0, norm.Substring(i + "Animations\".Length), norm.TrimStart("\"c, "."c))
        If clipRel = "" Then Return none
        Dim roots As New List(Of String)
        If saptFolders IsNot Nothing Then roots.AddRange(saptFolders.Where(Function(s) Not String.IsNullOrWhiteSpace(s)))
        ' SIN SAPT (root behavior): el animName es autoritativo relativo al actor — incluye redirects EXPLÍCITOS
        ' "..\OtroActor\Animations\X" (ej. SuperMutant reusa death humano). ResolveRelC resuelve el "..\".
        If roots.Count = 0 Then
            Dim cand = CanonHkx(ResolveRelC(actorRoot, norm))
            If animSet.Contains(cand) Then Return (cand, "native", New List(Of String) From {cand})
            Return none
        End If
        Dim found As New List(Of String)
        Dim firstMode As String = ""
        For Each s In roots
            Dim sn = s.Replace("/"c, "\"c).TrimEnd("\"c)
            Dim c1 = CanonHkx(sn & "\" & clipRel)
            Dim c1Ok = animSet.Contains(c1)
            If c1Ok AndAlso Not found.Contains(c1, StringComparer.OrdinalIgnoreCase) Then found.Add(c1) : If firstMode = "" Then firstMode = "full"
            Dim j = clipRel.IndexOf("\"c)
            If j >= 0 Then
                Dim c2 = CanonHkx(sn & "\" & clipRel.Substring(j + 1))
                Dim c2Ok = animSet.Contains(c2)
                If c1Ok AndAlso c2Ok AndAlso Not String.Equals(c1, c2, StringComparison.OrdinalIgnoreCase) Then weColl = True   ' full Y strip distintos en la MISMA ruta
                If c2Ok AndAlso Not found.Contains(c2, StringComparer.OrdinalIgnoreCase) Then found.Add(c2) : If firstMode = "" Then firstMode = "strip"
            End If
        Next
        If found.Count = 0 Then Return none
        Return (found(0), firstMode, found)
    End Function

    ' .hkt→.hkx (las refs internas usan .hkt; los archivos reales son .hkx). NO lowercasea ni strip.
    Private Function NormHkx(p As String) As String
        If String.IsNullOrWhiteSpace(p) Then Return ""
        If p.EndsWith(".hkt", StringComparison.OrdinalIgnoreCase) Then Return p.Substring(0, p.Length - 4) & ".hkx"
        Return p
    End Function

    ' Últimos 2 segmentos de un path (para mostrar la carpeta SAPT compacta).
    Private Function LastTwoSeg(p As String) As String
        If String.IsNullOrWhiteSpace(p) Then Return ""
        Dim segs = p.TrimEnd("\"c).Split("\"c)
        If segs.Length <= 2 Then Return p
        Return segs(segs.Length - 2) & "\" & segs(segs.Length - 1)
    End Function

    Private Sub RaceAnimScan(pm As PluginManager, edidFilter As String)
        Dim races = pm.GetRecordsOfType("RACE")
        Console.WriteLine($"[raceanim] {races.Count} RACE records | filtro edid='{edidFilter}'")
        Dim shown As Integer = 0
        Dim gTotClips = 0, gOk = 0, gMis = 0, gMiss = 0
        Dim gBadRaces As New List(Of String), gMissRaces As New List(Of String)
        Dim gSkelAnom As New List(Of String), gSkelSeen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each rec In races
            Dim race As RACE_Data = Nothing
            Try
                race = RecordParsers.ParseRACE(rec, pm)
            Catch
                Continue For
            End Try
            If race Is Nothing Then Continue For
            If edidFilter <> "" AndAlso race.EditorID.IndexOf(edidFilter, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            If race.MaleBehaviorGraphProject = "" AndAlso race.FemaleBehaviorGraphProject = "" Then Continue For  ' solo razas con behavior

            Dim rb = RaceBehaviorResolver.ResolveRaceBehavior(race.FormID, pm)
            If rb Is Nothing Then Continue For
            shown += 1
            Console.WriteLine($"=== {race.EditorID} [0x{race.FormID:X8}] ===")
            Console.WriteLine($"  project  M='{rb.MaleProject}'  F='{rb.FemaleProject}'")
            Console.WriteLine($"  skeleton M='{rb.MaleSkeleton}'  F='{rb.FemaleSkeleton}'")
            Console.WriteLine($"  subgraphs={rb.Subgraphs.Count}  source={rb.SubgraphSource}")
            Dim byGraph = rb.Subgraphs.GroupBy(Function(s) s.BehaviourGraph, StringComparer.OrdinalIgnoreCase).
                             OrderByDescending(Function(g) g.Count()).ToList()
            For Each g In byGraph.Take(8)
                Dim roles = String.Join(",", g.Select(Function(s) RoleName(s.Role)).Distinct())
                Console.WriteLine($"     x{g.Count(),3}  {g.Key}   [{roles}]")
            Next
            If byGraph.Count > 8 Then Console.WriteLine($"     … (+{byGraph.Count - 8} graphs más)")
            Console.WriteLine($"  archivos .hkx distintos a cargar: {rb.DistinctBehaviorFiles().Count}")
            ' [RACE-RECORD] keywords del RACE + subgraphs (SAKD/SAPT) marcando los que matchean → filtro por raza.
            If edidFilter <> "" Then
                Dim raceKw = String.Join(", ", race.Keywords.Select(Function(k) EdidOf(pm, k)))
                Console.WriteLine($"  [RACE-RECORD] {race.EditorID}: Keywords=[{raceKw}] | PROPIO={race.SubgraphData.Count} SRAC=0x{race.SubgraphTemplateRaceFormID:X8}")
                Dim kwSet = New HashSet(Of UInteger)(race.Keywords)
                For Each sd In rb.Subgraphs
                    Dim sakd = String.Join("+", sd.ActorKeywordFormIDs.Select(Function(k) EdidOf(pm, k)))
                    Dim apply = sd.ActorKeywordFormIDs.Count = 0 OrElse sd.ActorKeywordFormIDs.Any(Function(k) kwSet.Contains(k))
                    Console.WriteLine($"     {If(apply, "✓APLICA", "·skip  ")} SGNM='{System.IO.Path.GetFileName(sd.BehaviourGraph)}' SAKD=[{sakd}] SAPT: {String.Join(" ; ", sd.AnimationPaths)}")
                Next
            End If

            ' Skeleton SÓLIDO (rigName del behavior character) + enumeración de clips.
            Dim loader As Func(Of String, Byte()) = Function(p) FO4_NPC_Manager.MeshPathHelpers.TryLoadMeshBytes(p, 0)
            Dim havokSkel = BehaviorClipEnumerator.ResolveHavokSkeleton(rb, loader)
            Console.WriteLine($"  skeleton (Havok, rigName del character) = '{havokSkel}'")
            ' Comparación de SETS de huesos: NIF (render, rb.Skeleton) vs HKX (behavior, havokSkel).
            CompareNifHkxBoneSets(race.EditorID, havokSkel, rb.Skeleton)
            ComposeAndCompareSkeleton(race.EditorID, havokSkel, rb.Skeleton)
            Dim clips = BehaviorClipEnumerator.EnumerateClips(rb, loader)
            Console.WriteLine($"  CLIPS reproducibles (dedup por archivo): {clips.Count}")
            For Each rg In clips.SelectMany(Function(c) c.Roles).GroupBy(Function(r) r).OrderByDescending(Function(g) g.Count())
                Console.WriteLine($"     rol {rg.Key,-10} : {rg.Count()} clips")
            Next
            For Each c In clips.Take(12)
                Console.WriteLine($"     · {c.AnimationFile}  [{String.Join(",", c.Roles)}]  speed={c.PlaybackSpeed:0.##}")
            Next
            If clips.Count > 12 Then Console.WriteLine($"     … (+{clips.Count - 12} clips más)")

            ' === VALIDACIÓN compacta (TODAS las razas): cada clip debe existir y maxBoneIdx < bones del skel.
            Dim vOk = 0, vLow = 0, vMiss = 0
            ValidateRaceClipsCompact(havokSkel, clips, vOk, vLow, vMiss, edidFilter <> "")
            Dim badTag = If(vLow > 0, "  <<< LOW-COVERAGE(no mapea)", "") & If(vMiss > 0, "  <missing " & vMiss & ">", "")
            Console.WriteLine($"  [VALIDATE] clips={clips.Count} ok={vOk} lowcov={vLow} missing={vMiss}{badTag}")
            gTotClips += clips.Count : gOk += vOk : gMis += vLow : gMiss += vMiss
            If vLow > 0 Then gBadRaces.Add($"{race.EditorID}(low={vLow})")
            If vMiss > 0 Then gMissRaces.Add($"{race.EditorID}(miss={vMiss})")

            ' Confirmar (todas las razas): en cada skeleton.hkx usado, ¿el de animación es siempre 'Root'?
            For Each sp In clips.Select(Function(c) c.SourceSkeletonPath).Concat({havokSkel}).
                              Where(Function(p) Not String.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase)
                If gSkelSeen.Add(sp) Then
                    Dim r = CheckSkeletonFile(sp)
                    If r.Item2 Then gSkelAnom.Add($"{sp} → [{r.Item1}]")
                End If
            Next

            ' === DIAGNÓSTICO detallado (solo con filtro edid) ===========================
            If edidFilter <> "" Then
                DumpBehaviorChain(rb)
                RawClipPathDump(rb, loader)
                RaceAnimDiagnostics(havokSkel, rb.Skeleton, clips)
            End If
        Next
        Console.WriteLine($"[raceanim] razas con behavior mostradas: {shown}")
        Console.WriteLine($"[VALIDATE-TOTAL] clips={gTotClips} ok={gOk} mismatch={gMis} missing={gMiss}")
        Console.WriteLine($"[VALIDATE-TOTAL] razas con MISMATCH (deformarían): {If(gBadRaces.Count = 0, "NINGUNA ✓", String.Join(", ", gBadRaces))}")
        Console.WriteLine($"[VALIDATE-TOTAL] razas con missing: {If(gMissRaces.Count = 0, "ninguna", String.Join(", ", gMissRaces))}")
        Console.WriteLine($"[SKEL-CHECK] skeletons distintos verificados={gSkelSeen.Count}; con selección AMBIGUA (no hay exactamente 1 no-ragdoll): {If(gSkelAnom.Count = 0, "NINGUNO ✓ (1 anim + ragdoll en todos)", String.Join("  |  ", gSkelAnom))}")
    End Sub

    ' Como MainForm.LoadAnimHkxBytes: prueba candidatos (con/sin "Meshes\", .hkx/.hkt).
    Private Function LoadAnimCand(path As String) As Byte()
        Return BehaviorClipEnumerator.LoadFirstHkxCandidate(Function(p) FO4_NPC_Manager.MeshPathHelpers.TryLoadMeshBytes(p, 0), path)
    End Function

    ' Valida con DATOS REALES (Assaultron) el orden de composición mount/pose. Por frame de una anim real:
    ' computa el Delta real del bone y el DESPLAZAMIENTO de la cabeza entre el orden ACTUAL (O×Mount×Delta)
    ' y el orden A (O×Delta×Mount). El desplazamiento es independiente del punto de la cabeza:
    ' |trans(O×M×D) − trans(O×D×M)| = |R_O·(I−R_D)·T_M|. Si A está bien, el offset queda rígido al bone.
    ''' <summary>Compara, por hueso, el NIF (render) vs el HKX (behavior): el PADRE (parentazgo) y el
    ''' TRANSFORM (world bind R+T). Para ver si el NIF representa el MISMO skeleton que el HKX.</summary>
    Private Sub CompareNifHkxBoneSets(label As String, hkxPath As String, nifPath As String)
        Dim hbx = LoadAnimCand(hkxPath) : Dim nbx = LoadAnimCand(nifPath)
        If hbx Is Nothing OrElse nbx Is Nothing Then
            Console.WriteLine($"  [NIF-vs-HKX] falta archivo (hkx '{hkxPath}'={hbx IsNot Nothing}, nif '{nifPath}'={nbx IsNot Nothing})")
            Return
        End If
        ' HKX: bones + parent (vía ParentIndices) + world bind (compose ReferencePose).
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(hbx))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.ParentIndices IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        ' DIAG: todos los hkaSkeleton del archivo (name + root + #bones) — para evaluar selección por root.
        Dim allSk = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                        Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing).ToList()
        Dim rootOf = Function(s As HkaSkeletonGraph_Class) As String
                         For i = 0 To s.Bones.Count - 1
                             Dim pp = If(i < s.ParentIndices.Count, CInt(s.ParentIndices(i)), -1)
                             If pp < 0 OrElse pp >= s.Bones.Count Then Return s.Bones(i).Name
                         Next
                         Return If(s.Bones.Count > 0, s.Bones(0).Name, "(none)")
                     End Function
        Console.WriteLine($"     [SKEL-PICK] {allSk.Count} hkaSkeleton en archivo: {String.Join(" | ", allSk.Select(Function(s) $"name='{s.Name}' root='{rootOf(s)}' bones={s.Bones.Count}"))}")
        If skel Is Nothing Then Console.WriteLine("  [NIF-vs-HKX] HKX sin skeleton de animación") : Return
        Dim nB = skel.Bones.Count
        Dim hWorld(nB - 1) As Transform_Class, hParent(nB - 1) As String
        For i = 0 To nB - 1
            Dim loc = HkxTransformConventionHelper.ToTransform(skel.ReferencePose(i))
            Dim p = If(i < skel.ParentIndices.Count, CInt(skel.ParentIndices(i)), -1)
            hWorld(i) = If(p < 0 OrElse p >= i, loc, hWorld(p).ComposeTransforms(loc))
            hParent(i) = If(p < 0 OrElse p >= nB, "(root)", skel.Bones(p).Name)
        Next
        ' NIF: nodo → (parent name, world transform).
        Dim nif As New Nifcontent_Class_Manolo() : nif.Load_Manolo(nbx)
        Dim nParent As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim nWorld As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        For Each blk In nif.Blocks
            Dim nn = TryCast(blk, NiflySharp.Blocks.NiNode)
            If nn Is Nothing OrElse nn.Name Is Nothing Then Continue For
            Dim nm = nn.Name.String
            If String.IsNullOrEmpty(nm) OrElse nWorld.ContainsKey(nm) Then Continue For
            Dim par = TryCast(nif.GetParentNode(nn), NiflySharp.Blocks.NiNode)
            nParent(nm) = If(par?.Name?.String, "(root)")
            nWorld(nm) = Transform_Class.GetGlobalTransform(nn, nif)
        Next
        ' Comparar por nombre: parentazgo + transform.
        Dim both = 0, pMis = 0, tMis = 0
        Dim pMisList As New List(Of String), tMisList As New List(Of String)
        Dim onlyHkx As New List(Of String)
        For i = 0 To nB - 1
            Dim nm = skel.Bones(i).Name
            If String.IsNullOrEmpty(nm) Then Continue For
            If Not nWorld.ContainsKey(nm) Then onlyHkx.Add(nm) : Continue For
            both += 1
            ' parent
            If Not String.Equals(hParent(i), nParent(nm), StringComparison.OrdinalIgnoreCase) Then
                pMis += 1 : pMisList.Add($"{nm}: HKX↑'{hParent(i)}' vs NIF↑'{nParent(nm)}'")
            End If
            ' transform
            Dim hr = hWorld(i).Rotation, nr = nWorld(nm).Rotation
            Dim dT = (hWorld(i).Translation - nWorld(nm).Translation).Length()
            Dim dR = Math.Abs(hr.M11 - nr.M11) + Math.Abs(hr.M12 - nr.M12) + Math.Abs(hr.M13 - nr.M13) +
                     Math.Abs(hr.M21 - nr.M21) + Math.Abs(hr.M22 - nr.M22) + Math.Abs(hr.M23 - nr.M23) +
                     Math.Abs(hr.M31 - nr.M31) + Math.Abs(hr.M32 - nr.M32) + Math.Abs(hr.M33 - nr.M33)
            If dT > 0.5F OrElse dR > 0.05F Then tMis += 1 : tMisList.Add($"{nm}: dT={dT:F2} dR={dR:F2}")
        Next
        Dim onlyNif = nWorld.Keys.Where(Function(k) Not skel.Bones.Any(Function(b) String.Equals(b.Name, k, StringComparison.OrdinalIgnoreCase))).ToList()
        Dim nifRoots = nParent.Where(Function(kv) kv.Value = "(root)").Select(Function(kv) kv.Key).ToList()
        Console.WriteLine($"     [SKEL-PICK] NIF root(s): {String.Join(", ", nifRoots)}   | HKX(elegido) root='{rootOf(skel)}' name='{skel.Name}'")
        Console.WriteLine($"     [SKEL-PICK] root∈NIF? {String.Join(" | ", allSk.Select(Function(s) $"'{rootOf(s)}'={If(nWorld.ContainsKey(rootOf(s)), "SÍ", "no")}"))}")
        Console.WriteLine($"  [NIF-vs-HKX] {label}: HKX={nB} NIF={nWorld.Count} | both={both} | parent-mismatch={pMis} | transform-mismatch={tMis} | soloHKX={onlyHkx.Count} soloNIF={onlyNif.Count}")
        If pMisList.Count > 0 Then Console.WriteLine($"     PARENT distinto ({pMisList.Count}): {String.Join("  |  ", pMisList.Take(12))}")
        If tMisList.Count > 0 Then Console.WriteLine($"     TRANSFORM distinto ({tMisList.Count}): {String.Join("  |  ", tMisList.Take(12))}")
        If onlyHkx.Count > 0 Then Console.WriteLine($"     huesos del HKX que FALTAN en el NIF ({onlyHkx.Count}): {String.Join(", ", onlyHkx.Take(25))}")
        If onlyNif.Count > 0 Then
            ' Categorizar los soloNIF: _Offset/_skin (estructurales descartables) vs huesos reales.
            Dim offset = onlyNif.Where(Function(n) n.IndexOf("_Offset", StringComparison.OrdinalIgnoreCase) >= 0 OrElse n.IndexOf("_skin", StringComparison.OrdinalIgnoreCase) >= 0 OrElse n.EndsWith("_Offset", StringComparison.OrdinalIgnoreCase)).ToList()
            Dim reales = onlyNif.Where(Function(n) Not offset.Contains(n)).ToList()
            Console.WriteLine($"     soloNIF ({onlyNif.Count}): _Offset/_skin estructurales={offset.Count} | OTROS huesos reales={reales.Count}")
            Console.WriteLine($"     OTROS (no _Offset) [{reales.Count}]: {String.Join(", ", reales)}")
        End If
        ' Búsqueda de nodos de REGIÓN (Torso/Upper/Lower/Leg/Arm/Limb/Region/Hip/Body) en NIF y HKX.
        Dim rxRegion = New System.Text.RegularExpressions.Regex("torso|upper|lower|leg|arm|limb|region|hip|body|skin|jaw|lip|cheek|brow|mouth|tongue|eye|face|teeth|tongue", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        Dim regNif = nWorld.Keys.Where(Function(k) rxRegion.IsMatch(k)).OrderBy(Function(k) k).ToList()
        Dim regHkx = skel.Bones.Select(Function(b) b.Name).Where(Function(n) Not String.IsNullOrEmpty(n) AndAlso rxRegion.IsMatch(n)).OrderBy(Function(n) n).ToList()
        Console.WriteLine($"     REGIÓN en HKX ({regHkx.Count}): {String.Join(", ", regHkx)}")
        Console.WriteLine($"     REGIÓN en NIF ({regNif.Count}): {String.Join(", ", regNif.Where(Function(k) k.IndexOf("_Offset", StringComparison.OrdinalIgnoreCase) < 0))}")
        ' ¿Hay mapeo TAXATIVO de regiones en el HKX? Los únicos campos candidatos del hkaSkeleton:
        ' floatSlots (canales float nombrados) y partitions (agrupaciones de huesos del solver).
        Dim fs = If(skel.FloatSlotNames, New List(Of String))
        Console.WriteLine($"     HKX floatSlots ({fs.Count}): {String.Join(", ", fs)}")
        Dim parts = If(skel.Partitions, New List(Of HkaPartitionGraph_Class))
        Console.WriteLine($"     HKX partitions ({parts.Count}): {String.Join(" | ", parts.Select(Function(p) $"'{p.Name}'[start={p.StartBoneIndex} num={p.NumBones}]"))}")
        Console.WriteLine($"     [reader-check] rawCounts bones={skel.BonesField.Header.Count} refPose(ancla)={skel.ReferencePoseField.Header.Count} refFloats={skel.ReferenceFloatsField.Header.Count} floatSlots={skel.FloatSlotsField.Header.Count} localFrames={skel.LocalFramesField.Header.Count} partitions={skel.PartitionsField.Header.Count}")
    End Sub

    ''' <summary>Arma el skeleton CANDIDATO = A(HKX no-ragdoll, world) + B(nodos solo-NIF colgando de su
    ''' padre NIF, anclados en el world HKX de su ancestro A) + C(huesos de BSClothExtraData del NIF), y
    ''' mide world-vs-world contra el NIF PURO, por raza. Residual = cuánto se desvía cada hueso del NIF si
    ''' lo reconstruís así (la diferencia se PROPAGA desde los huesos A cuyo transform HKX≠NIF a todos sus
    ''' descendientes B). Humanoides: HKX==NIF ⇒ residual 0 (reconstrucción exacta) + agrega Weapon/IK + cloth.</summary>
    Private Sub ComposeAndCompareSkeleton(label As String, hkxPath As String, nifPath As String)
        Dim hbx = LoadAnimCand(hkxPath) : Dim nbx = LoadAnimCand(nifPath)
        If hbx Is Nothing OrElse nbx Is Nothing Then Return
        ' A: HKX (no-ragdoll) → world por hueso.
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(hbx))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.ParentIndices IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        If skel Is Nothing Then Console.WriteLine("  [COMPOSE] HKX sin skeleton de animación") : Return
        Dim nBn = skel.Bones.Count
        Dim hWorld(nBn - 1) As Transform_Class
        Dim aSet As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim aWorld As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        For i = 0 To nBn - 1
            Dim loc = HkxTransformConventionHelper.ToTransform(skel.ReferencePose(i))
            Dim p = If(i < skel.ParentIndices.Count, CInt(skel.ParentIndices(i)), -1)
            hWorld(i) = If(p < 0 OrElse p >= i, loc, hWorld(p).ComposeTransforms(loc))
            Dim bn = skel.Bones(i).Name
            If Not String.IsNullOrEmpty(bn) AndAlso aSet.Add(bn) Then aWorld(bn) = hWorld(i)
        Next
        ' NIF: parent + local + world por nodo.
        Dim nif As New Nifcontent_Class_Manolo() : nif.Load_Manolo(nbx)
        Dim nLocal As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        Dim nParentName As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim nWorld As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        For Each blk In nif.Blocks
            Dim nn = TryCast(blk, NiflySharp.Blocks.NiNode)
            If nn Is Nothing OrElse nn.Name Is Nothing Then Continue For
            Dim nm = nn.Name.String
            If String.IsNullOrEmpty(nm) OrElse nWorld.ContainsKey(nm) Then Continue For
            Dim par = TryCast(nif.GetParentNode(nn), NiflySharp.Blocks.NiNode)
            nParentName(nm) = If(par?.Name?.String, "(root)")
            nLocal(nm) = New Transform_Class(nn)
            nWorld(nm) = Transform_Class.GetGlobalTransform(nn, nif)
        Next
        ' Compose world: A-bone → world HKX (ancla); B-node → composed(parent) ∘ local NIF (recursivo, memo).
        Dim composed As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        Dim Build As Func(Of String, Transform_Class) = Nothing
        Build = Function(nm As String) As Transform_Class
                    If composed.ContainsKey(nm) Then Return composed(nm)
                    Dim r As Transform_Class
                    If aSet.Contains(nm) Then
                        r = aWorld(nm)                          ' hueso A: ancla = world HKX
                    ElseIf nLocal.ContainsKey(nm) Then
                        Dim pn = nParentName(nm)
                        If pn = "(root)" OrElse (Not aSet.Contains(pn) AndAlso Not nLocal.ContainsKey(pn)) Then
                            r = nLocal(nm)                      ' raíz / padre no resoluble → local como world
                        Else
                            composed(nm) = nLocal(nm)           ' guard anti-ciclo
                            r = Build(pn).ComposeTransforms(nLocal(nm))
                        End If
                    Else
                        r = New Transform_Class()
                    End If
                    composed(nm) = r
                    Return r
                End Function
        For Each nm In nWorld.Keys.ToList() : Build(nm) : Next
        ' Residual: composed(B+A∩NIF) vs NIF puro, por nodo del NIF.
        Dim resid = 0 : Dim maxDT = 0.0F : Dim maxDR = 0.0F
        Dim residList As New List(Of String)
        For Each nm In nWorld.Keys
            Dim c = composed(nm) : Dim n = nWorld(nm)
            Dim dT = (c.Translation - n.Translation).Length()
            Dim cr = c.Rotation, nr = n.Rotation
            Dim dR = Math.Abs(cr.M11 - nr.M11) + Math.Abs(cr.M12 - nr.M12) + Math.Abs(cr.M13 - nr.M13) +
                     Math.Abs(cr.M21 - nr.M21) + Math.Abs(cr.M22 - nr.M22) + Math.Abs(cr.M23 - nr.M23) +
                     Math.Abs(cr.M31 - nr.M31) + Math.Abs(cr.M32 - nr.M32) + Math.Abs(cr.M33 - nr.M33)
            maxDT = Math.Max(maxDT, dT) : maxDR = Math.Max(maxDR, dR)
            If dT > 0.5F OrElse dR > 0.05F Then resid += 1 : residList.Add($"{nm}: dT={dT:F2} dR={dR:F2}")
        Next
        ' Sets: A-only (HKX no en NIF) y NIF no cubierto por composed (debería ser 0).
        Dim aOnly = aSet.Where(Function(b) Not nWorld.ContainsKey(b)).ToList()
        ' C: cloth-bones del BSClothExtraData del NIF (item-sourced; a nivel skeleton.nif suele ser 0).
        Dim clothBones As New List(Of String)
        Dim clothBlk = nif.Blocks.OfType(Of NiflySharp.Blocks.BSClothExtraData)().FirstOrDefault()
        If clothBlk IsNot Nothing Then
            Dim cpf As HkxPackfile_Class = Nothing
            If HkxPackfileParser_Class.TryParse(clothBlk, cpf) Then
                Dim csk = HkxObjectGraphParser_Class.BuildGraph(cpf).GetObjectsByClassName("hkaSkeleton").
                            Select(Function(o) HkxObjectGraphParser_Class.BuildGraph(cpf).ParseSkeleton(o)).FirstOrDefault()
                If csk IsNot Nothing AndAlso csk.Bones IsNot Nothing Then
                    clothBones = csk.Bones.Select(Function(b) b.Name).Where(Function(s) Not String.IsNullOrEmpty(s) AndAlso Not aSet.Contains(s) AndAlso Not nWorld.ContainsKey(s)).Distinct().ToList()
                End If
            End If
        End If
        Dim composedTotal = nWorld.Count + aOnly.Count + clothBones.Count
        Console.WriteLine($"  [COMPOSE A+B+C vs NIF] {label}: composed={composedTotal} | NIF puro={nWorld.Count} | A∩NIF+B reconstruye NIF con residual={resid}/{nWorld.Count} (maxDT={maxDT:F2} maxDR={maxDR:F2}) | A-only(Weapon/IK…)={aOnly.Count} | cloth(C)={clothBones.Count}")
        If resid > 0 Then Console.WriteLine($"     RESIDUAL (composed≠NIF) ej: {String.Join("  |  ", residList.Take(10))}")
        If aOnly.Count > 0 Then Console.WriteLine($"     A-only (HKX, se AGREGAN al NIF): {String.Join(", ", aOnly.Take(25))}")
        If clothBones.Count > 0 Then Console.WriteLine($"     cloth-bones (C, del BSClothExtraData): {String.Join(", ", clothBones.Take(25))}")
    End Sub

    ''' <summary>Dump de la cadena de resolución del skeleton HKX: project → hkbProjectStringData.CharacterFilenames
    ''' → character → hkbCharacterStringData.rigName. Muestra cuántos character files hay y a qué rig apunta cada uno.</summary>
    Private Sub DumpBehaviorChain(rb As ResolvedRaceBehavior)
        If rb Is Nothing OrElse String.IsNullOrWhiteSpace(rb.Project) Then Return
        Dim proj = rb.Project
        Dim slash = proj.LastIndexOf("\"c)
        Dim actorRoot = If(slash > 0, proj.Substring(0, slash), "")
        Dim pb = LoadAnimCand(proj)
        If pb Is Nothing Then Console.WriteLine($"  [CHAIN] project '{proj}' no carga") : Return
        Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(pb))
        Dim charFiles As New List(Of String)
        Dim nPsd = 0
        For Each o In g.GetObjectsByClassName("hkbProjectStringData")
            nPsd += 1
            Dim psd = g.ParseProjectStringData(o)
            If psd IsNot Nothing Then charFiles.AddRange(psd.CharacterFilenames)
        Next
        Console.WriteLine($"  [CHAIN] project='{proj}' | hkbProjectStringData x{nPsd} | CharacterFilenames={charFiles.Count}: {String.Join(", ", charFiles)}")
        For Each cf In charFiles
            Dim lc = cf.TrimStart("\"c)
            Dim full = If(lc.StartsWith("actors\", StringComparison.OrdinalIgnoreCase) OrElse lc.StartsWith("meshes\", StringComparison.OrdinalIgnoreCase) OrElse actorRoot = "", lc, actorRoot & "\" & lc)
            Dim cb = LoadAnimCand(full)
            If cb Is Nothing Then cb = LoadAnimCand(cf)
            If cb Is Nothing Then Console.WriteLine($"     character '{cf}' no carga") : Continue For
            Dim gc = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(cb))
            Dim nCsd = 0
            For Each o In gc.GetObjectsByClassName("hkbCharacterStringData")
                nCsd += 1
                Dim csd = gc.ParseCharacterStringData(o)
                If csd IsNot Nothing Then Console.WriteLine($"     character '{cf}' [hkbCharacterStringData x{nCsd}] → name='{csd.CharacterName}' rigName='{csd.RigName}'")
            Next
        Next
    End Sub

    ''' <summary>Parsea números en formato es-AR del log (coma decimal Y coma separador): empareja
    ''' tokens (entero, fracción). "0,00,-3,92,110,65" → [0.00, -3.92, 110.65].</summary>
    Private Function ParseEsArNums(s As String) As Double()
        Dim toks = s.Split(","c)
        Dim r As New List(Of Double)
        Dim i = 0
        While i < toks.Length - 1
            Dim v As Double
            If Double.TryParse(toks(i).Trim() & "." & toks(i + 1).Trim(), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, v) Then r.Add(v)
            i += 2
        End While
        Return r.ToArray()
    End Function

    ''' <summary>Compara el live skeleton ENSAMBLADO del robot (parseado del log [BONE-WORLD]
    ''' originalGlobal = O·Mount) contra el skeleton del clip (CreateABot.hkx), hueso por hueso.</summary>
    Private Sub CompareLogLiveSkelVsHkx(logPath As String, hkxPath As String)
        Console.WriteLine($"=== COMPARA live skeleton del robot (log) vs clip skeleton (CreateABot.hkx) ===")
        If Not IO.File.Exists(logPath) Then Console.WriteLine($"  log no encontrado: {logPath}") : Return
        ' Parsear [BONE-WORLD] bone='X' originalGlobal: T=(...) S=... R=[r|r|r]
        Dim live As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        For Each ln In IO.File.ReadLines(logPath)
            Dim k = ln.IndexOf("originalGlobal:", StringComparison.Ordinal)
            If k < 0 Then Continue For
            Dim bm = System.Text.RegularExpressions.Regex.Match(ln, "bone='([^']+)'")
            Dim tm = System.Text.RegularExpressions.Regex.Match(ln, "T=\(([^)]*)\)")
            Dim rm = System.Text.RegularExpressions.Regex.Match(ln, "R=\[([^\]]*)\]")
            If Not (bm.Success AndAlso tm.Success AndAlso rm.Success) Then Continue For
            Dim nm = bm.Groups(1).Value
            If live.ContainsKey(nm) Then Continue For
            Dim tv = ParseEsArNums(tm.Groups(1).Value)
            Dim rows = rm.Groups(1).Value.Split("|"c)
            If tv.Length < 3 OrElse rows.Length < 3 Then Continue For
            Dim r0 = ParseEsArNums(rows(0)), r1 = ParseEsArNums(rows(1)), r2 = ParseEsArNums(rows(2))
            If r0.Length < 3 OrElse r1.Length < 3 OrElse r2.Length < 3 Then Continue For
            Dim t As New Transform_Class With {
                .Translation = New System.Numerics.Vector3(CSng(tv(0)), CSng(tv(1)), CSng(tv(2))),
                .Rotation = New NiflySharp.Structs.Matrix33 With {.M11 = CSng(r0(0)), .M12 = CSng(r0(1)), .M13 = CSng(r0(2)),
                                               .M21 = CSng(r1(0)), .M22 = CSng(r1(1)), .M23 = CSng(r1(2)),
                                               .M31 = CSng(r2(0)), .M32 = CSng(r2(1)), .M33 = CSng(r2(2))}}
            live(nm) = t
        Next
        Console.WriteLine($"  bones parseados del log = {live.Count}")
        If live.Count = 0 Then Return

        ' CreateABot.hkx world binds.
        Dim hbx = LoadAnimCand(hkxPath) : If hbx Is Nothing Then Console.WriteLine("  hkx no encontrado") : Return
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(hbx))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.ParentIndices IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        Dim nBb = skel.Bones.Count
        Dim bw(nBb - 1) As Transform_Class
        For i = 0 To nBb - 1
            Dim loc = HkxTransformConventionHelper.ToTransform(skel.ReferencePose(i))
            Dim p = If(i < skel.ParentIndices.Count, CInt(skel.ParentIndices(i)), -1)
            bw(i) = If(p < 0 OrElse p >= i, loc, bw(p).ComposeTransforms(loc))
        Next
        Dim hkxWorld As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        For i = 0 To nBb - 1
            If Not String.IsNullOrEmpty(skel.Bones(i).Name) Then hkxWorld(skel.Bones(i).Name) = bw(i)
        Next

        ' Comparar O·Mount (live ensamblado) vs E (CreateABot).
        Console.WriteLine($"  hueso              | dT (posición) | dR (rotación) | lectura")
        Dim rows2 As New List(Of (Bone As String, dT As Double, dR As Double))
        For Each kv In live
            Dim e As Transform_Class = Nothing
            If Not hkxWorld.TryGetValue(kv.Key, e) Then Continue For
            Dim lr = kv.Value.Rotation, erT = e.Rotation
            Dim dT = (kv.Value.Translation - e.Translation).Length()
            Dim dR = Math.Abs(lr.M11 - erT.M11) + Math.Abs(lr.M12 - erT.M12) + Math.Abs(lr.M13 - erT.M13) +
                     Math.Abs(lr.M21 - erT.M21) + Math.Abs(lr.M22 - erT.M22) + Math.Abs(lr.M23 - erT.M23) +
                     Math.Abs(lr.M31 - erT.M31) + Math.Abs(lr.M32 - erT.M32) + Math.Abs(lr.M33 - erT.M33)
            rows2.Add((kv.Key, dT, dR))
        Next
        For Each r In rows2.OrderByDescending(Function(x) x.dT + x.dR * 10)
            Dim lec = If(r.dR > 0.05, "rotación distinta", If(r.dT > 1.0, "posición distinta (mount/ensamblaje)", "≈ igual"))
            Console.WriteLine($"    {r.Bone,-18} | {r.dT,9:F2} | {r.dR,9:F3} | {lec}")
        Next
        Console.WriteLine($"  (O·Mount = bind ensamblado del live. dR≈0 ⇒ misma orientación que el clip; dT>0 ⇒ posición movida por el mount)")
    End Sub

    ''' <summary>Compara el WORLD bind de cada hueso entre el skeleton.hkx (animación) y el
    ''' skeleton.nif (render), por nombre. Determina con datos si el render skeleton == el clip skeleton.</summary>
    Private Sub CompareSkeletonNifVsHkx(label As String, hkxPath As String, nifPath As String)
        Console.WriteLine($"=== COMPARA skeleton.nif vs skeleton.hkx — {label} ===")
        Dim hkxBytesC = LoadAnimCand(hkxPath) : Dim nifBytesC = LoadAnimCand(nifPath)
        If hkxBytesC Is Nothing Then Console.WriteLine($"  HKX no encontrado: {hkxPath}") : Return
        If nifBytesC Is Nothing Then Console.WriteLine($"  NIF no encontrado: {nifPath}") : Return

        ' HKX: world binds por bone (compose ReferencePose via ParentIndices, skeleton de animación = no-ragdoll).
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(hkxBytesC))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.ParentIndices IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        If skel Is Nothing Then Console.WriteLine("  sin skeleton de animación en el HKX") : Return
        Dim nB = skel.Bones.Count
        Dim bw(nB - 1) As Transform_Class
        For i = 0 To nB - 1
            Dim loc = HkxTransformConventionHelper.ToTransform(skel.ReferencePose(i))
            Dim p = If(i < skel.ParentIndices.Count, CInt(skel.ParentIndices(i)), -1)
            bw(i) = If(p < 0 OrElse p >= i, loc, bw(p).ComposeTransforms(loc))
        Next
        Dim hkxWorld As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        For i = 0 To nB - 1
            If Not String.IsNullOrEmpty(skel.Bones(i).Name) Then hkxWorld(skel.Bones(i).Name) = bw(i)
        Next

        ' NIF: world transform por NiNode (nombre).
        Dim nif As New Nifcontent_Class_Manolo() : nif.Load_Manolo(nifBytesC)
        Dim nifWorld As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        For Each blk In nif.Blocks
            Dim nn = TryCast(blk, NiflySharp.Blocks.NiNode)
            If nn Is Nothing OrElse nn.Name Is Nothing Then Continue For
            Dim nm = nn.Name.String
            If Not String.IsNullOrEmpty(nm) AndAlso Not nifWorld.ContainsKey(nm) Then nifWorld(nm) = Transform_Class.GetGlobalTransform(nn, nif)
        Next

        ' Comparar world bind por nombre.
        Dim matched = 0, mism = 0, onlyHkx = 0, shown = 0
        For Each kv In hkxWorld
            Dim nw As Transform_Class = Nothing
            If Not nifWorld.TryGetValue(kv.Key, nw) Then onlyHkx += 1 : Continue For
            Dim h = kv.Value, hr = kv.Value.Rotation, nr = nw.Rotation
            Dim dT = (h.Translation - nw.Translation).Length()
            Dim dR = Math.Abs(hr.M11 - nr.M11) + Math.Abs(hr.M12 - nr.M12) + Math.Abs(hr.M13 - nr.M13) +
                     Math.Abs(hr.M21 - nr.M21) + Math.Abs(hr.M22 - nr.M22) + Math.Abs(hr.M23 - nr.M23) +
                     Math.Abs(hr.M31 - nr.M31) + Math.Abs(hr.M32 - nr.M32) + Math.Abs(hr.M33 - nr.M33)
            If dT > 0.01F OrElse dR > 0.01F Then
                mism += 1
                If shown < 15 Then Console.WriteLine($"  MISMATCH {kv.Key,-22} dT={dT,7:F2} dR={dR,5:F2}") : shown += 1
            Else
                matched += 1
            End If
        Next
        Console.WriteLine($"  HKX bones={hkxWorld.Count} | NIF nodes={nifWorld.Count} | matched={matched} | MISMATCH={mism} | solo-en-HKX={onlyHkx}")
        Console.WriteLine($"  ⇒ {If(mism = 0, "skeleton.nif == skeleton.hkx (mismo bind) en los huesos compartidos", "skeleton.nif ≠ skeleton.hkx — HAY mismatch real de bind")}")
    End Sub

    Private Sub MountValidateRun()
        ' ════════════════════════════════════════════════════════════════════════════════════════
        ' COMPARACIÓN skeleton.nif (render) vs skeleton.hkx (animación) — TODOS los huesos.
        ' Determina con datos (no asumiendo) si el render skeleton == el clip skeleton por raza.
        ' ════════════════════════════════════════════════════════════════════════════════════════
        ' skeleton.nif vs skeleton.hkx del MISMO actor, varias razas (¿el render skeleton == el del clip?).
        For Each rz In {("Character (humano)", "Character"), ("SuperMutant", "SuperMutant"), ("Ghoul", "Ghoul"),
                        ("Dog", "Dog"), ("DeathClaw", "DeathClaw"), ("Mirelurk", "Mirelurk"), ("Synth Gen1", "DLC01\Robot"),
                        ("Robot (stub)", "Robot"), ("CreateABot (stub)", "CreateABot")}
            CompareSkeletonNifVsHkx(rz.Item1, $"Actors\{rz.Item2}\CharacterAssets\skeleton.hkx", $"Actors\{rz.Item2}\CharacterAssets\skeleton.nif")
        Next
        Console.WriteLine()
        ' El robot NO tiene .nif real (stub) — su live skeleton se arma en runtime. Lo parseo del log
        ' [BONE-WORLD] (O·Mount, bind ensamblado real) y lo comparo contra el clip skeleton.
        CompareLogLiveSkelVsHkx("FO4_NPC_Manager\FO4_NPC_Manager\bin\x64\Debug\net8.0-windows\win-x64\fo4lib.log", "Actors\CreateABot\CharacterAssets\skeleton.hkx")
        Console.WriteLine()

        Console.WriteLine("=== VALIDACIÓN ORDEN MOUNT/POSE — Assaultron (datos reales) ===")
        Dim skelBytes = LoadAnimCand("Actors\CreateABot\CharacterAssets\skeleton.hkx")
        If skelBytes Is Nothing Then Console.WriteLine("skeleton CreateABot NO encontrado") : Return
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(skelBytes))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        If skel Is Nothing Then Console.WriteLine("sin skeleton de animación") : Return
        Console.WriteLine($"skeleton='{skel.Name}' bones={skel.Bones.Count} referencePose={skel.ReferencePose.Count}")
        ' ¿CreateABot.hkx trae los huesos de connect-point C-/P-?
        Dim cBones = skel.Bones.Where(Function(b) b.Name IsNot Nothing AndAlso (b.Name.StartsWith("C-", StringComparison.OrdinalIgnoreCase) OrElse b.Name.StartsWith("C_", StringComparison.OrdinalIgnoreCase))).Select(Function(b) b.Name).ToList()
        Dim pBones = skel.Bones.Where(Function(b) b.Name IsNot Nothing AndAlso (b.Name.StartsWith("P-", StringComparison.OrdinalIgnoreCase) OrElse b.Name.StartsWith("P_", StringComparison.OrdinalIgnoreCase))).Select(Function(b) b.Name).ToList()
        Console.WriteLine($"  C-* en CreateABot.hkx ({cBones.Count}): {String.Join(", ", cBones)}")
        Console.WriteLine($"  P-* en CreateABot.hkx ({pBones.Count}): {String.Join(", ", pBones)}")

        ' Mounts REALES del log de Assaultron (MOUNTDELTA-WRITE).
        Dim mounts = New List(Of (Name As String, Tx As Single, Ty As Single, Tz As Single)) From {
            ("Neck", 0.0F, -9.0F, 0.0F),
            ("HeadNod", 12.147F, 9.0F, -12.147F),
            ("LUPPERARM", 3.327F, -0.013F, -3.014F),
            ("Pelvis", 0.0F, 0.0F, 0.0F)
        }

        ' MISMO clip que usó el usuario en el render (log [ANIM-BAR] select).
        Dim clipBytes = LoadAnimCand("Actors\CreateABot\Animations\Protectron\CombatWalkBackwardRight.hkx")
        If clipBytes Is Nothing Then clipBytes = LoadAnimCand("Actors\CreateABot\Animations\Assaultron\PairedKillAssaultronRaiderLiftStab_AttackerLead.hkx")
        If clipBytes Is Nothing Then Console.WriteLine("clip de Assaultron NO encontrado") : Return
        Dim anim = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(clipBytes)).ParseAnimations().FirstOrDefault()
        If anim Is Nothing OrElse anim.Binding Is Nothing Then Console.WriteLine("anim sin binding") : Return
        Console.WriteLine($"clip frames={anim.NumFrames} tracks={anim.NumTransformTracks}")
        Dim idxArr = anim.Binding.TransformTrackToBoneIndices

        ' ── CONTAMINACIÓN-CHECK: delta = inv(CreateABot.O)·frameLocal (igual que HkxPoseImport línea 276,
        ' PERO con CreateABot como bind). Comparar con el log [ANIM-BONE]:
        '   Neck delta = T(0.01,0.03,-0.31) Rd=(0,1,0) | HeadNod delta = T(12.46,-0.03,-12.13) Rd=(1,1,1)
        ' Si COINCIDE ⇒ liveBone.O = CreateABot.O (delta limpio, el problema es la composición/mount).
        ' Si NO coincide ⇒ liveBone.O ≠ CreateABot.O (delta contaminado por skeleton mismatch).
        ' ════════════════════════════════════════════════════════════════════════════════════════
        ' PRUEBA EXTREMIDADES (y todo el cuerpo): el delta del FIX (= inv(CreateABot)·frameLocal) es el
        ' movimiento PURO del joint, a lo largo de TODA la animación. |T| chico ⇒ limpio (el joint rota,
        ' no se traslada — salvo root motion en Pelvis/COM). θ = ángulo del joint. Comparado con el delta
        ' viejo (vs liveBone.O) que metía traslaciones grandes (la contaminación).
        ' ════════════════════════════════════════════════════════════════════════════════════════
        Console.WriteLine("=== PRUEBA FIX: delta(CreateABot) limpio en TODO el cuerpo y TODA la animación ===")
        Dim nfP = Math.Max(1, anim.NumFrames)
        Dim framesP = {0, nfP \ 4, nfP \ 2, (3 * nfP) \ 4, nfP - 1}.Distinct().ToArray()
        For Each bn In {"Neck", "HeadNod", "HeadTwist", "LUPPERARM", "RUPPERARM", "LForearm1", "RForearm1",
                        "LUpperLegBiped", "LLowerLegBiped", "LFootBiped", "LToeBiped", "Chest", "SPINE1", "Pelvis"}
            Dim bi = skel.Bones.FindIndex(Function(b) String.Equals(b.Name, bn, StringComparison.OrdinalIgnoreCase))
            If bi < 0 Then Continue For
            Dim tr2 = -1
            For t = 0 To Math.Min(anim.NumTransformTracks, idxArr.Count) - 1
                If idxArr(t) = bi Then tr2 = t : Exit For
            Next
            If tr2 < 0 Then Console.WriteLine($"  {bn,-16} (no animado en este clip)") : Continue For
            Dim O0 = HkxTransformConventionHelper.ToTransform(skel.ReferencePose(bi))
            Dim maxT = 0.0, sb As New System.Text.StringBuilder($"  {bn,-16}")
            For Each frame In framesP
                Dim ht = anim.GetTransform(frame, tr2)
                If ht Is Nothing Then Continue For
                Dim fl = HkxTransformConventionHelper.ToTransform(ht.Translation, ht.Rotation, ht.Scale)
                Dim d = O0.Inverse().ComposeTransforms(fl)
                Dim trc = CDbl(d.Rotation.M11) + CDbl(d.Rotation.M22) + CDbl(d.Rotation.M33)
                Dim th = Math.Acos(Math.Max(-1.0, Math.Min(1.0, (trc - 1.0) / 2.0))) * 180.0 / Math.PI
                Dim tl = d.Translation.Length() : maxT = Math.Max(maxT, tl)
                sb.Append($" |T|={tl,5:F2} θ={th,3:F0}")
            Next
            Dim isRoot = (bn = "Pelvis" OrElse bn = "COM")
            sb.Append($"   ⇒ {If(maxT < 2.0 OrElse isRoot, "LIMPIO", "⚠ traslación alta")}")
            Console.WriteLine(sb.ToString())
        Next
        Console.WriteLine("Lectura: |T|<2 en todos los bones no-root ⇒ el delta del fix es movimiento de joint LIMPIO (sin")
        Console.WriteLine("  contaminación). + la comparación del live ensamblado (arriba) dio orientación = CreateABot")
        Console.WriteLine("  (Neck dR=0, brazos ≤0.13) ⇒ ese movimiento limpio se aplica en el eje correcto. Extremidades OK.")

        For Each m In mounts
            Dim boneIdx = skel.Bones.FindIndex(Function(b) String.Equals(b.Name, m.Name, StringComparison.OrdinalIgnoreCase))
            If boneIdx < 0 Then Console.WriteLine($"--- {m.Name}: no está en el skeleton") : Continue For
            Dim track = -1
            For t = 0 To Math.Min(anim.NumTransformTracks, idxArr.Count) - 1
                If idxArr(t) = boneIdx Then track = t : Exit For
            Next
            If track < 0 Then Console.WriteLine($"--- {m.Name}: no animado en este clip") : Continue For
            Dim mountMag = Math.Sqrt(m.Tx * m.Tx + m.Ty * m.Ty + m.Tz * m.Tz)
            Dim O = HkxTransformConventionHelper.ToTransform(skel.ReferencePose(boneIdx))
            Dim Oinv = O.Inverse()
            Dim mountT As New Transform_Class With {.Translation = New System.Numerics.Vector3(m.Tx, m.Ty, m.Tz)}
            Console.WriteLine($"--- {m.Name}  mount.T=({m.Tx:F2},{m.Ty:F2},{m.Tz:F2}) |T|={mountMag:F2}  (mount {If(mountMag < 0.001, "≈I → NO afectado", "≠I")}) ---")
            Console.WriteLine("    frame |  θ(°) | |offNEW−Mount| (rígido⇒0) | |offOLD−Mount| (viejo deforma)")
            Dim nf = Math.Max(1, anim.NumFrames)
            For Each frame In {0, nf \ 6, nf \ 3, nf \ 2, (2 * nf) \ 3, (5 * nf) \ 6, nf - 1}.Distinct()
                Dim ht = anim.GetTransform(frame, track)
                If ht Is Nothing Then Continue For
                Dim frameLocal = HkxTransformConventionHelper.ToTransform(ht.Translation, ht.Rotation, ht.Scale)
                Dim delta = Oinv.ComposeTransforms(frameLocal)
                Dim R = delta.Rotation
                Dim trace = CDbl(R.M11) + CDbl(R.M22) + CDbl(R.M33)
                Dim thetaDeg = Math.Acos(Math.Max(-1.0, Math.Min(1.0, (trace - 1.0) / 2.0))) * 180.0 / Math.PI
                Dim clipPosed = O.ComposeTransforms(delta)                              ' O·Δ = la pose que el clip quiere
                Dim boneNEW = O.ComposeTransforms(delta).ComposeTransforms(mountT)       ' O·Δ·Mount (nuevo getter)
                Dim boneOLD = O.ComposeTransforms(mountT).ComposeTransforms(delta)       ' O·Mount·Δ (viejo)
                ' offset del chunk respecto al bone que el clip posa = inv(clipPosed)·bone. NEW debe = Mount (rígido).
                Dim offNEW = clipPosed.Inverse().ComposeTransforms(boneNEW)
                Dim offOLD = clipPosed.Inverse().ComposeTransforms(boneOLD)
                Dim offNEWvsMount = (offNEW.Translation - mountT.Translation).Length()   ' ≈0 ⇒ offset rígido = Mount
                Dim offOLDvsMount = (offOLD.Translation - mountT.Translation).Length()   ' >0 ⇒ el offset se deforma
                Console.WriteLine($"    {frame,5} | {thetaDeg,5:F1} | {offNEWvsMount,24:F4} | {offOLDvsMount,22:F2}")
            Next
        Next
        Console.WriteLine("Reposo: Delta=I es no-op ⇒ O·Δ·Mount = O·Mount = el orden viejo, byte-idéntico (símbolico, trivial).")
        Console.WriteLine("Lectura: |offNEW−Mount|≈0 en TODOS los frames ⇒ el chunk queda RÍGIDO al bone que el clip posa (offset=Mount")
        Console.WriteLine("         constante) = CORRECTO. |offOLD−Mount| crece con θ ⇒ el viejo deforma el offset frame a frame = el bug.")

        ' ============================================================================
        ' SANITY DEL MAPEO animación→bone (hipótesis: "la lista no matchea el skeleton").
        ' Robot (Assaultron) vs CONTROL humano: si el humano da |Tdelta|≈0 y el robot grande,
        ' el skeleton/mapeo del robot está equivocado (no la composición mount/pose).
        ' ============================================================================
        MappingSanity("Assaultron (CreateABot)", "Actors\CreateABot\CharacterAssets\skeleton.hkx",
                      "Actors\CreateABot\Animations\Assaultron\PairedKillAssaultronRaiderLiftStab_AttackerLead.hkx")
        MappingSanity("CONTROL Humano (Character)", "Actors\Character\CharacterAssets\skeleton.hkx",
                      "Actors\Character\Animations\1HM\AttackLeftB.hkx")

        ' ============================================================================
        ' clipMotion REAL (WORLD): el movimiento del bone en el clip relativo al BIND del clip.
        ' clipMotion_B = clipFrameWorld_B × inv(clipBindWorld_B). Si es rotación PURA (|T|≈0) para
        ' TODOS los bones ⇒ el modelo holístico cierra (skin = clipMotion × W_B × skinBind, rest
        ' idéntico). Si |T| es grande en un bone (ej. brazo) ⇒ el clip trae el ensamblaje en sus
        ' FRAMES (su neutral ≠ el bind de CreateABot) y hay que tomar otro neutral.
        ' ============================================================================
        ClipMotionDump("Actors\CreateABot\CharacterAssets\skeleton.hkx",
                       "Actors\CreateABot\Animations\Assaultron\PairedKillAssaultronRaiderLiftStab_AttackerLead.hkx",
                       {"Neck", "HeadNod", "HeadTwist", "LUPPERARM", "RUPPERARM", "Pelvis", "Chest", "LForearm1"})
    End Sub

    ''' <summary>Compone los world transforms del clip y reporta el movimiento PURO de cada bone
    ''' (clipFrameWorld × inv(clipBindWorld)). Mide si el clip trae el ensamblaje en sus frames.</summary>
    Private Sub ClipMotionDump(skelPath As String, clipPath As String, bonesOfInterest As String())
        Console.WriteLine()
        Console.WriteLine("=== clipMotion WORLD (mov. puro del bone relativo al bind del clip) ===")
        Dim sb = LoadAnimCand(skelPath) : If sb Is Nothing Then Console.WriteLine("  no skel") : Return
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(sb))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.ParentIndices IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        If skel Is Nothing Then Console.WriteLine("  no anim skel") : Return
        Dim cb = LoadAnimCand(clipPath) : If cb Is Nothing Then Console.WriteLine("  no clip") : Return
        Dim anim = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(cb)).ParseAnimations().FirstOrDefault()
        If anim Is Nothing OrElse anim.Binding Is Nothing Then Console.WriteLine("  no binding") : Return
        Dim idxArr = anim.Binding.TransformTrackToBoneIndices
        Dim nB = skel.Bones.Count
        ' bind local + bind world
        Dim bindLocal(nB - 1) As Transform_Class
        For i = 0 To nB - 1 : bindLocal(i) = HkxTransformConventionHelper.ToTransform(skel.ReferencePose(i)) : Next
        Dim bindWorld(nB - 1) As Transform_Class
        For i = 0 To nB - 1
            Dim p = If(i < skel.ParentIndices.Count, CInt(skel.ParentIndices(i)), -1)
            bindWorld(i) = If(p < 0 OrElse p >= i, bindLocal(i), bindWorld(p).ComposeTransforms(bindLocal(i)))
        Next
        ' track por bone idx
        Dim trackOfBone As New Dictionary(Of Integer, Integer)
        For t = 0 To Math.Min(anim.NumTransformTracks, idxArr.Count) - 1 : trackOfBone(idxArr(t)) = t : Next
        Dim nf = Math.Max(1, anim.NumFrames)

        ' ── VERIFICACIÓN FINAL: bindWorld de CreateABot (E) en ROTACIÓN COMPLETA, para comparar con
        ' el log [BONE-WORLD] originalGlobal (= L·M, el live mounteado). Si E.R == (L·M).R ⇒ el mount
        ' cancela la diferencia de orientación ⇒ el fix rota bien (bone.local = L·M·J tiene la
        ' orientación de E). Log dice: Neck originalGlobal R=[0,0,1|0,1,0|-1,0,0] T=(0,-3.92,110.65).
        Console.WriteLine("  --- bindWorld CreateABot (E) — comparar R con log [BONE-WORLD] originalGlobal (L·M) ---")
        For Each bn In {"Neck", "HeadNod"}
            Dim bi2 = skel.Bones.FindIndex(Function(b) String.Equals(b.Name, bn, StringComparison.OrdinalIgnoreCase))
            If bi2 < 0 Then Continue For
            Dim e = bindWorld(bi2) : Dim er = e.Rotation : Dim et = e.Translation
            Console.WriteLine($"      {bn,-8} E.world R=[{er.M11:F3},{er.M12:F3},{er.M13:F3}|{er.M21:F3},{er.M22:F3},{er.M23:F3}|{er.M31:F3},{er.M32:F3},{er.M33:F3}] T=({et.X:F2},{et.Y:F2},{et.Z:F2})")
        Next
        For Each frame In {0, nf \ 2, nf - 1}.Distinct()
            ' frame local (clip si animado, bind si no) + frame world
            Dim frLocal(nB - 1) As Transform_Class
            For i = 0 To nB - 1
                If trackOfBone.ContainsKey(i) Then
                    Dim ht = anim.GetTransform(frame, trackOfBone(i))
                    frLocal(i) = If(ht IsNot Nothing, HkxTransformConventionHelper.ToTransform(ht.Translation, ht.Rotation, ht.Scale), bindLocal(i))
                Else
                    frLocal(i) = bindLocal(i)
                End If
            Next
            Dim frWorld(nB - 1) As Transform_Class
            For i = 0 To nB - 1
                Dim p = If(i < skel.ParentIndices.Count, CInt(skel.ParentIndices(i)), -1)
                frWorld(i) = If(p < 0 OrElse p >= i, frLocal(i), frWorld(p).ComposeTransforms(frLocal(i)))
            Next
            Console.WriteLine($"  --- frame {frame} ---")
            For Each bn In bonesOfInterest
                Dim bi = skel.Bones.FindIndex(Function(b) String.Equals(b.Name, bn, StringComparison.OrdinalIgnoreCase))
                If bi < 0 Then Continue For
                Dim motion = frWorld(bi).ComposeTransforms(bindWorld(bi).Inverse())  ' world: frame × inv(bind)
                Dim tr = CDbl(motion.Rotation.M11) + CDbl(motion.Rotation.M22) + CDbl(motion.Rotation.M33)
                Dim th = Math.Acos(Math.Max(-1.0, Math.Min(1.0, (tr - 1.0) / 2.0))) * 180.0 / Math.PI
                Dim bw = bindWorld(bi).Translation
                Console.WriteLine($"      {bn,-12} bindWorld.T=({bw.X,7:F2},{bw.Y,7:F2},{bw.Z,7:F2})  clipMotion: |T|={motion.Translation.Length(),6:F2}  θ={th,6:F1}°")
            Next
        Next
        Console.WriteLine("  Lectura: |T| del clipMotion ≈0 ⇒ rotación pura (el bind del clip = CreateABot, modelo cierra).")
        Console.WriteLine("           |T| grande ⇒ el clip trae traslación de ensamblaje en sus frames (neutral ≠ CreateABot).")
    End Sub

    ''' <summary>Carga skel+clip y dumpea, por track al frame medio, el bone (nombre vía skeleton),
    ''' θ del delta y |traslación del delta|. Un joint sano: el origen NO se traslada del bind (|T|≈0).
    ''' |T| grande en bone no-root ⇒ el track mapea a un bone que no corresponde / bind ≠ (skeleton malo).</summary>
    Private Sub MappingSanity(label As String, skelPath As String, clipPath As String)
        Console.WriteLine()
        Console.WriteLine($"=== SANITY MAPEO — {label} ===")
        Dim sb = LoadAnimCand(skelPath)
        If sb Is Nothing Then Console.WriteLine($"  skeleton no encontrado: {skelPath}") : Return
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(sb))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        If skel Is Nothing Then Console.WriteLine("  sin skeleton de animación") : Return
        Dim cb = LoadAnimCand(clipPath)
        If cb Is Nothing Then Console.WriteLine($"  clip no encontrado: {clipPath}") : Return
        Dim anim = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(cb)).ParseAnimations().FirstOrDefault()
        If anim Is Nothing OrElse anim.Binding Is Nothing Then Console.WriteLine("  anim sin binding") : Return
        Dim idxArr = anim.Binding.TransformTrackToBoneIndices
        Dim maxIdx = -1
        For t = 0 To idxArr.Count - 1
            If idxArr(t) > maxIdx Then maxIdx = idxArr(t)
        Next
        Console.WriteLine($"skeleton='{skel.Name}' bones={skel.Bones.Count} | tracks={anim.NumTransformTracks} | binding idx max={maxIdx}")
        If maxIdx >= skel.Bones.Count Then Console.WriteLine($"  ⛔ ANOMALÍA: idx binding ({maxIdx}) >= bones ({skel.Bones.Count}) → clip NO bindeado contra este skeleton.")

        Dim midF = Math.Max(0, anim.NumFrames \ 2)
        Dim rows = New List(Of (Bone As String, Theta As Double, TMag As Double))()
        For t = 0 To Math.Min(anim.NumTransformTracks, idxArr.Count) - 1
            Dim bi = idxArr(t)
            If bi < 0 OrElse bi >= skel.Bones.Count Then rows.Add(($"<idx {bi} fuera>", 0, 9999)) : Continue For
            Dim ht = anim.GetTransform(midF, t)
            If ht Is Nothing Then Continue For
            Dim O2 = HkxTransformConventionHelper.ToTransform(skel.ReferencePose(bi))
            Dim fl = HkxTransformConventionHelper.ToTransform(ht.Translation, ht.Rotation, ht.Scale)
            Dim d = O2.Inverse().ComposeTransforms(fl)
            Dim tr = CDbl(d.Rotation.M11) + CDbl(d.Rotation.M22) + CDbl(d.Rotation.M33)
            Dim th = Math.Acos(Math.Max(-1.0, Math.Min(1.0, (tr - 1.0) / 2.0))) * 180.0 / Math.PI
            rows.Add((skel.Bones(bi).Name, th, d.Translation.Length()))
        Next
        Dim big = rows.Where(Function(x) x.TMag > 5.0).Count()
        Dim med = If(rows.Count > 0, rows.OrderBy(Function(x) x.TMag).ElementAt(rows.Count \ 2).TMag, 0)
        Console.WriteLine($"frame medio={midF} | bones con |Tdelta|>5u: {big}/{rows.Count} | mediana |Tdelta|={med:F2}")
        For Each r In rows.OrderByDescending(Function(x) x.TMag).Take(12)
            Console.WriteLine($"    {r.Bone,-28} θ={r.Theta,6:F1}°  |Tdelta|={r.TMag,7:F2}{If(r.TMag > 5.0, "  ⚠", "")}")
        Next
    End Sub

    ' Character files resueltos del project (con el quirk del espacio en el nombre).
    Private Function CharFilesOf(rb As ResolvedRaceBehavior) As List(Of String)
        Dim r As New List(Of String)
        Dim pb = LoadAnimCand(rb.Project)
        If pb Is Nothing Then Return r
        Try
            Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(pb))
            Dim actorRoot = ActorRootOf(rb.Project)
            For Each o In g.GetObjectsByClassName("hkbProjectStringData")
                Dim psd = g.ParseProjectStringData(o)
                If psd Is Nothing Then Continue For
                For Each cf In psd.CharacterFilenames
                    r.Add(If(cf.StartsWith("Actors\", StringComparison.OrdinalIgnoreCase), cf, actorRoot & "\" & cf.TrimStart("\"c)))
                Next
            Next
        Catch
        End Try
        Return r
    End Function

    ' ¿En este skeleton.hkx, el esqueleto de ANIMACIÓN (el de más huesos que no sea ragdoll) se llama 'Root'?
    ' Devuelve (descripción "Root(95), Ragdoll_NPC COM(18)", anomalíaSiNoEsRoot).
    Private Function CheckSkeletonFile(skelPath As String) As (String, Boolean)
        Dim b = LoadAnimCand(skelPath)
        If b Is Nothing Then Return ("<no file>", False)
        Try
            Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(b))
            Dim skels = g.GetObjectsByClassName("hkaSkeleton").Select(Function(o) g.ParseSkeleton(o)).
                          Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.Bones.Count > 0).
                          Select(Function(s) (Name:=If(s.Name, ""), Count:=s.Bones.Count)).ToList()
            If skels.Count = 0 Then Return ("<no skel>", False)
            Dim desc = String.Join(", ", skels.Select(Function(s) $"{s.Name}({s.Count})"))
            ' Regla: el de animación = el ÚNICO que no es ragdoll. Anomalía = NO hay exactamente 1 no-ragdoll.
            Dim nonRag = skels.Where(Function(s) s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0).Count()
            Return (desc, nonRag <> 1)
        Catch ex As Exception
            Return ("<err " & ex.Message & ">", False)
        End Try
    End Function

    ' Inventario CRUDO de objetos de un hkx: histograma de clases + detalle de hkaSkeleton y hkaSkeletonMapper
    ' (el dato EXACTO de retargeting entre esqueletos, si existe).
    Private Sub DumpHkxInventory(label As String, bytes As Byte())
        If bytes Is Nothing Then Console.WriteLine($"  [INV] {label}: NO ENCONTRADO") : Return
        Try
            Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(bytes))
            Console.WriteLine($"  [INV] {label}: {g.Objects.Count} objetos")
            For Each h In g.Objects.GroupBy(Function(o) o.ClassName).OrderByDescending(Function(x) x.Count())
                Console.WriteLine($"        x{h.Count(),4}  {h.Key}")
            Next
            For Each o In g.GetObjectsByClassName("hkaSkeleton")
                Dim sk = g.ParseSkeleton(o)
                If sk IsNot Nothing Then Console.WriteLine($"        ► hkaSkeleton name='{sk.Name}' bones={If(sk.Bones IsNot Nothing, sk.Bones.Count, 0)}")
            Next
            For Each o In g.GetObjectsByClassName("hkaSkeletonMapper")
                Dim m = g.ParseSkeletonMapper(o)
                If m Is Nothing Then Continue For
                Console.WriteLine($"        ►► hkaSkeletonMapper A='{m.SkeletonAName}' B='{m.SkeletonBName}' simple={m.SimpleMappings.Count} chain={m.ChainMappings.Count}")
                For Each mm In m.SimpleMappings.Take(6)
                    Console.WriteLine($"             {mm.BoneAName} → {mm.BoneBName}")
                Next
            Next
        Catch ex As Exception
            Console.WriteLine($"  [INV] {label}: error {ex.Message}")
        End Try
    End Sub

    Private ReadOnly _skelNamesCache As New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)
    ' Bone names del Skeleton.hkx de un actor (cacheado). actorRoot="Actors\Character" → su skeleton.
    Private Function CachedActorSkelNames(actorRoot As String) As List(Of String)
        If String.IsNullOrWhiteSpace(actorRoot) Then Return Nothing
        Dim v As List(Of String) = Nothing
        If _skelNamesCache.TryGetValue(actorRoot, v) Then Return v
        v = LoadSkeletonBoneNames(actorRoot & "\CharacterAssets\Skeleton.hkx")
        _skelNamesCache(actorRoot) = v
        Return v
    End Function

    ' Validación REAL (mapeo por NOMBRE): cada clip se interpreta con el skeleton de SU actor de origen
    ' (del path del archivo) → nombres de hueso animados; se cuenta cuántos existen en el skeleton del
    ' actor CONSUMIDOR (destino). Cobertura alta = reproduce bien; baja = no mapea (estático/roto).
    Private Sub ValidateRaceClipsCompact(havokSkelPath As String, clips As List(Of ResolvedAnimationClip),
                                         ByRef ok As Integer, ByRef lowcov As Integer, ByRef missing As Integer,
                                         Optional verbose As Boolean = False)
        Dim consumingNames = CachedActorSkelNames(ActorRootOf(havokSkelPath))
        Dim consumingSet As New HashSet(Of String)(If(consumingNames, New List(Of String)()), StringComparer.OrdinalIgnoreCase)
        Dim notFound As New List(Of String), parseFail As New List(Of String), lowList As New List(Of String)
        For Each c In clips
            Dim cb = LoadAnimCand(c.AnimationFile)
            If cb Is Nothing Then missing += 1 : notFound.Add(c.AnimationFile) : Continue For
            Try
                Dim ag = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(cb))
                Dim anim = ag.ParseAnimations().FirstOrDefault()
                If anim Is Nothing Then anim = ag.ParseLosslessAnimations().FirstOrDefault()
                If anim Is Nothing Then missing += 1 : parseFail.Add(c.AnimationFile) : Continue For
                Dim idxs = anim.Binding?.TransformTrackToBoneIndices
                If idxs Is Nothing OrElse idxs.Count = 0 Then ok += 1 : Continue For
                ' Skeleton del actor de ORIGEN de la anim (del path), no el del consumidor.
                Dim srcNames = CachedActorSkelNames(ActorRootOf(c.AnimationFile))
                Dim srcLoaded = (srcNames IsNot Nothing AndAlso srcNames.Count > 0)
                If Not srcLoaded Then srcNames = consumingNames
                Dim total = 0, mapped = 0
                For Each bi In idxs
                    If bi >= 0 AndAlso srcNames IsNot Nothing AndAlso bi < srcNames.Count Then
                        total += 1
                        If consumingSet.Contains(srcNames(bi)) Then mapped += 1
                    End If
                Next
                Dim cov = If(total > 0, mapped / CDbl(total), 0.0)
                If cov >= 0.9 Then ok += 1 Else lowcov += 1 : lowList.Add($"{c.AnimationFile} cov={cov:P0} src='{ActorRootOf(c.AnimationFile)}' ({mapped}/{total})")
            Catch ex As Exception
                missing += 1 : parseFail.Add(c.AnimationFile & " [EX:" & ex.GetType().Name & "]")
            End Try
        Next
        If verbose Then
            If notFound.Count > 0 Then Console.WriteLine($"     [MISSING-NOTFOUND {notFound.Count}]: {String.Join(" | ", notFound)}")
            If parseFail.Count > 0 Then Console.WriteLine($"     [MISSING-PARSEFAIL {parseFail.Count}]: {String.Join(" | ", parseFail)}")
            If lowList.Count > 0 Then Console.WriteLine($"     [LOWCOV {lowList.Count}]: {String.Join("  ||  ", lowList)}")
        End If
    End Sub

    ' Dump del skeleton del behavior (rigName) + por-clip: ¿existe? frames/tracks/maxBoneIdx vs bones del
    ' skeleton. Revela (A) clips sin archivo y (B) anims con más bones que el skeleton (mismatch → deforma).
    Private Sub RaceAnimDiagnostics(havokSkelPath As String, renderSkelNif As String, clips As List(Of ResolvedAnimationClip))
        Console.WriteLine("  ---- DIAGNÓSTICO ----")
        Console.WriteLine($"  render skeleton (RACE .nif) = '{renderSkelNif}'")
        Dim skelBoneCount As Integer = -1
        Dim skelBoneNames As New List(Of String)
        Dim skelBytes = LoadAnimCand(havokSkelPath)
        If skelBytes Is Nothing Then
            Console.WriteLine($"  [SKEL] behavior skeleton '{havokSkelPath}' NO ENCONTRADO")
        Else
            Try
                Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(skelBytes))
                Dim sko = sg.GetObjectsByClassName("hkaSkeleton").FirstOrDefault()
                Dim skel = sg.ParseSkeleton(sko)
                skelBoneCount = skel.Bones.Count
                skelBoneNames = skel.Bones.Select(Function(b) b.Name).ToList()
                Console.WriteLine($"  [SKEL] behavior skeleton '{havokSkelPath}' name='{skel.Name}' bones={skelBoneCount} bytes={skelBytes.Length}")
                Console.WriteLine("    bones=[" & String.Join(", ", skelBoneNames) & "]")
            Catch ex As Exception
                Console.WriteLine($"  [SKEL] parse error: {ex.Message}")
            End Try
        End If

        Dim scannedRoot = ActorRootOf(renderSkelNif)   ' "Actors\Stingwing"
        Console.WriteLine($"  [CLIPS] estado por archivo (skel {scannedRoot}={skelBoneCount} bones)  |  binding.OriginalSkeletonName  |  ¿existe variante propia {scannedRoot}\?")
        Dim missing = 0, mismatch = 0, ok = 0, ownVariant = 0
        For Each c In clips
            ' ¿Existe el MISMO leaf bajo el actor escaneado? (test 'elegir animación por skeleton')
            Dim leaf = LeafAfterAnimations(c.AnimationFile)
            Dim ownPath = If(leaf <> "" AndAlso scannedRoot <> "", scannedRoot & "\Animations\" & leaf, "")
            Dim ownBytes = If(ownPath <> "" AndAlso Not ownPath.Equals(c.AnimationFile, StringComparison.OrdinalIgnoreCase), LoadAnimCand(ownPath), Nothing)
            Dim hasOwn = ownBytes IsNot Nothing
            Dim ownTag = ""
            If hasOwn Then
                ownVariant += 1
                Dim ot = -1, of_ = -1
                Try
                    Dim oa = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(ownBytes))
                    Dim oanim = oa.ParseAnimations().FirstOrDefault()
                    If oanim Is Nothing Then oanim = oa.ParseLosslessAnimations().FirstOrDefault()
                    If oanim IsNot Nothing Then
                        ot = oanim.NumTransformTracks
                        Dim oi = oanim.Binding?.TransformTrackToBoneIndices
                        of_ = If(oi IsNot Nothing AndAlso oi.Count > 0, CInt(oi.Max()), -1)
                    End If
                Catch
                End Try
                Dim fits = If(of_ >= 0 AndAlso of_ < skelBoneCount, "FITS✓", "no-fit")
                ownTag = $"  +OWN(t={ot} maxBone={of_} {fits}):{ownPath}"
            End If

            Dim cb = LoadAnimCand(c.AnimationFile)
            If cb Is Nothing Then
                missing += 1
                Console.WriteLine($"    MISSING   {c.AnimationFile}{ownTag}")
                Continue For
            End If
            Try
                Dim ag = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(cb))
                Dim anim = ag.ParseAnimations().FirstOrDefault()
                If anim Is Nothing Then anim = ag.ParseLosslessAnimations().FirstOrDefault()
                If anim Is Nothing Then
                    Console.WriteLine($"    NOANIM    {c.AnimationFile}")
                    Continue For
                End If
                Dim idxs = anim.Binding?.TransformTrackToBoneIndices
                Dim maxIdx = If(idxs IsNot Nothing AndAlso idxs.Count > 0, CInt(idxs.Max()), -1)
                Dim origSkel = If(anim.Binding IsNot Nothing, anim.Binding.OriginalSkeletonName, "")
                Dim flag = ""
                If skelBoneCount >= 0 AndAlso maxIdx >= skelBoneCount Then flag = "  <<< MISMATCH" : mismatch += 1 Else ok += 1
                Console.WriteLine($"    OK f={anim.NumFrames,3} t={anim.NumTransformTracks,3} maxBone={maxIdx,3} origSkel='{origSkel}'  {c.AnimationFile}{flag}{ownTag}")
            Catch ex As Exception
                Console.WriteLine($"    PARSEERR  {ex.Message}  {c.AnimationFile}")
            End Try
        Next
        Console.WriteLine($"  [CLIPS] resumen: ok={ok} mismatch={mismatch} missing={missing}  conVariantePropia={ownVariant}  total={clips.Count}")

        ' === COMPARACIÓN HUESO-POR-HUESO contra el skeleton de cada actor FUENTE ====================
        ' La anim no trae nombres de track (vacíos) → el binding mapea track→ÍNDICE del esqueleto del
        ' actor que la creó. Para saber qué hueso recibe cada track al usar el skel de Stingwing, hay
        ' que comparar índice-por-índice el skel de Stingwing vs el del actor fuente. Si difieren,
        ' el track va al hueso equivocado → deforma. Cargo Actors\<X>\CharacterAssets\Skeleton.hkx.
        If skelBoneNames.Count = 0 Then Return
        Dim sourceActors = clips.
            Select(Function(c) ActorRootOf(c.AnimationFile)).
            Where(Function(r) r <> "" AndAlso Not r.Equals("Actors\Stingwing", StringComparison.OrdinalIgnoreCase)).
            Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Function(r) r).ToList()

        For Each srcRoot In sourceActors
            Dim srcSkelPath = srcRoot & "\CharacterAssets\Skeleton.hkx"
            Dim srcNames = LoadSkeletonBoneNames(srcSkelPath)
            If srcNames Is Nothing Then
                Console.WriteLine($"  [CMP Stingwing vs {srcRoot}] skeleton '{srcSkelPath}' NO ENCONTRADO/parse-fail")
                Continue For
            End If
            Dim n = Math.Max(skelBoneNames.Count, srcNames.Count)
            Dim sameIdx = 0
            For i = 0 To Math.Min(skelBoneNames.Count, srcNames.Count) - 1
                If skelBoneNames(i).Equals(srcNames(i), StringComparison.OrdinalIgnoreCase) Then sameIdx += 1
            Next
            Dim common = skelBoneNames.Intersect(srcNames, StringComparer.OrdinalIgnoreCase).ToList()
            Console.WriteLine($"  [CMP Stingwing({skelBoneNames.Count}) vs {srcRoot}({srcNames.Count})]  mismoIdx={sameIdx}/{Math.Min(skelBoneNames.Count, srcNames.Count)}  mismoNombre(set)={common.Count}")
            Console.WriteLine($"      huesos en común (por nombre): [{String.Join(", ", common)}]")
            ' Detalle de las primeras divergencias por índice (hasta 18 filas).
            Dim shownRows = 0
            For i = 0 To Math.Min(skelBoneNames.Count, srcNames.Count) - 1
                Dim a = skelBoneNames(i) : Dim b = srcNames(i)
                If Not a.Equals(b, StringComparison.OrdinalIgnoreCase) Then
                    Console.WriteLine($"      idx {i,2}: Stingwing='{a}'  <>  {srcRoot.Replace("Actors\", "")}='{b}'")
                    shownRows += 1
                    If shownRows >= 18 Then Console.WriteLine("      … (más divergencias)") : Exit For
                End If
            Next
            If sameIdx = Math.Min(skelBoneNames.Count, srcNames.Count) AndAlso srcNames.Count = skelBoneNames.Count Then
                Console.WriteLine($"      → IDÉNTICO orden+nombres: las anims de {srcRoot} mapean BIEN sobre Stingwing")
            End If
        Next
    End Sub

    ' Dump CRUDO del mecanismo real: lista animationNames del character (indexada) + cada clip con su
    ' animationBindingIndex y su animationName literal. Para verificar que animationNames[bindingIndex]
    ' apunta al archivo NATIVO del actor (no heurística).
    Private Sub RawClipPathDump(rb As ResolvedRaceBehavior, loader As Func(Of String, Byte()))
        Console.WriteLine("  ---- RAW (mecanismo del engine) ----")
        Dim actorRoot = ActorRootOf(rb.Project)   ' "Actors\Stingwing\StingwingProject.hkx" → "Actors\Stingwing"

        ' 1) Project → CharacterFilenames → Character → animationNames (lista ordenada del actor).
        Dim projBytes = LoadAnimCand(rb.Project)
        Dim charAnimNames As New List(Of String)
        If projBytes IsNot Nothing Then
            Try
                Dim pg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(projBytes))
                Dim charFiles As New List(Of String)
                For Each o In pg.GetObjectsByClassName("hkbProjectStringData")
                    Dim psd = pg.ParseProjectStringData(o)
                    If psd IsNot Nothing Then charFiles.AddRange(psd.CharacterFilenames)
                Next
                Console.WriteLine($"    project='{rb.Project}'  characterFiles=[{String.Join(", ", charFiles)}]")
                For Each cf In charFiles
                    Dim cfp = If(cf.StartsWith("Actors\", StringComparison.OrdinalIgnoreCase), cf, actorRoot & "\" & cf.TrimStart("\"c))
                    Dim cbytes = LoadAnimCand(cfp)
                    If cbytes Is Nothing Then Console.WriteLine($"    character '{cfp}' NO ENCONTRADO") : Continue For
                    Dim cg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(cbytes))
                    For Each co In cg.GetObjectsByClassName("hkbCharacterStringData")
                        Dim csd = cg.ParseCharacterStringData(co)
                        If csd Is Nothing Then Continue For
                        charAnimNames = csd.AnimationFilenames.ToList()
                        Console.WriteLine($"    character='{cfp}'  animationNames(filtradas)={charAnimNames.Count}  allStrings={csd.AllStrings.Count}")
                        For i = 0 To Math.Min(charAnimNames.Count, 60) - 1
                            Console.WriteLine($"        [{i,3}] {charAnimNames(i)}")
                        Next
                    Next
                Next
            Catch ex As Exception
                Console.WriteLine($"    RAW project/character error: {ex.Message}")
            End Try
        End If

        ' 1b) INVENTARIO crudo de objetos: project + character + el SKELETON (rigName) + una anim reusada.
        DumpHkxInventory("PROJECT " & rb.Project, LoadAnimCand(rb.Project))
        For Each cf In CharFilesOf(rb)
            DumpHkxInventory("CHARACTER " & cf, LoadAnimCand(cf))
        Next
        Dim rigSkel = BehaviorClipEnumerator.ResolveHavokSkeleton(rb, loader)
        DumpHkxInventory("SKELETON(rigName) " & rigSkel, LoadAnimCand(rigSkel))
        DumpHkxInventory("SKELETON HUMANO Actors\Character\CharacterAssets\skeleton.hkx", LoadAnimCand("Actors\Character\CharacterAssets\skeleton.hkx"))
        DumpHkxInventory("SKELETON POWERARMOR Actors\PowerArmor\CharacterAssets\skeleton.hkx", LoadAnimCand("Actors\PowerArmor\CharacterAssets\skeleton.hkx"))
        DumpHkxInventory("ANIM PowerArmor SprintPainTrain", LoadAnimCand("Actors\PowerArmor\Animations\1HM\SprintPainTrain.hkx"))

        ' 2) Clips por behavior: Name | bindingIndex | animationName LITERAL.
        For Each bf In rb.DistinctBehaviorFiles()
            Dim bb = LoadAnimCand(bf)
            If bb Is Nothing Then Continue For
            Try
                Dim bg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(bb))
                Dim gens = bg.GetObjectsByClassName("hkbClipGenerator").ToList()
                Console.WriteLine($"    behavior='{bf}'  clipGenerators={gens.Count}")
                Dim shown = 0
                For Each o In gens
                    Dim c = bg.ParseClipGenerator(o)
                    If c Is Nothing Then Continue For
                    Dim resolved = If(c.AnimationBindingIndex >= 0 AndAlso c.AnimationBindingIndex < charAnimNames.Count, charAnimNames(c.AnimationBindingIndex), "<idx fuera de rango>")
                    Console.WriteLine($"        clip='{c.Name}' bindIdx={c.AnimationBindingIndex} rawAnimName='{c.AnimationName}'  →animNames[idx]='{resolved}'")
                    shown += 1
                    If shown >= 10 Then Console.WriteLine("        … (más clips)") : Exit For
                Next
            Catch ex As Exception
                Console.WriteLine($"    RAW behavior '{bf}' error: {ex.Message}")
            End Try
        Next
    End Sub

    ' Parte del path después de "...\Animations\": "Actors\Alien\Animations\Stagger\X.hkx" → "Stagger\X.hkx".
    Private Function LeafAfterAnimations(path As String) As String
        Dim i = path.IndexOf("\Animations\", StringComparison.OrdinalIgnoreCase)
        If i < 0 Then Return ""
        Return path.Substring(i + "\Animations\".Length)
    End Function

    ' Actor root de un path: el prefijo antes de la subcarpeta estándar. Maneja creatures DLC de 3
    ' segmentos: "Actors\DLC03\Angler\Animations\X.hkx" → "Actors\DLC03\Angler" (no "Actors\DLC03").
    Private Function ActorRootOf(path As String) As String
        If String.IsNullOrWhiteSpace(path) Then Return ""
        For Each marker In {"\CharacterAssets\", "\Animations\", "\Characters\", "\Behaviors\"}
            Dim i = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase)
            If i > 0 Then Return path.Substring(0, i)
        Next
        Dim parts = path.Split("\"c)
        If parts.Length >= 2 AndAlso parts(0).Equals("Actors", StringComparison.OrdinalIgnoreCase) Then Return parts(0) & "\" & parts(1)
        Return ""
    End Function

    ' Carga un Skeleton.hkx y devuelve los nombres de hueso del esqueleto de ANIMACIÓN (el de MÁS huesos;
    ' el skeleton.hkx trae además un ragdoll reducido — hay que ignorarlo, no agarrar el primero).
    Private Function LoadSkeletonBoneNames(skelPath As String) As List(Of String)
        Dim b = LoadAnimCand(skelPath)
        If b Is Nothing Then Return Nothing
        Try
            Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(b))
            Dim skels = g.GetObjectsByClassName("hkaSkeleton").Select(Function(o) g.ParseSkeleton(o)).
                          Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.Bones.Count > 0).ToList()
            If skels.Count = 0 Then Return Nothing
            ' El de ANIMACIÓN = el que NO es ragdoll (el ragdoll siempre se llama con 'Ragdoll').
            Dim sk = If(skels.FirstOrDefault(Function(s) String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0), skels(0))
            Return sk.Bones.Select(Function(x) x.Name).ToList()
        Catch
            Return Nothing
        End Try
    End Function

    Private Function RoleName(role As Integer) As String
        Select Case role
            Case 0 : Return "MT"
            Case 1 : Return "Weapon"
            Case 2 : Return "Furniture"
            Case 3 : Return "Idle"
            Case 4 : Return "Pipboy"
            Case Else : Return "?" & role.ToString()
        End Select
    End Function

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

    ''' <summary>Diagnóstico de la costura cuello/cabeza con body-weight. Mide si la separación
    ''' viene del NNAM (escala del hueso "Neck" que el body-skel aplica y el head-skel NO, por
    ''' suppressNeckNnam) o de las hojas _skin. Para cada vértice del nape pegado a "Neck", computa
    ''' el desplazamiento EXACTO que el NNAM mete (= la separación) con el MISMO compose del render
    ''' (boneWorld ∘ shapeBind). Validación: bindWorld de un seam vert debe ≈ su pos autoreada.</summary>
    Private Sub NeckSeamDiag(pm As PluginManager, espName As String, edid As String)
        Dim npcFormID = ResolveEdid(pm, espName, edid)
        If npcFormID = 0UI Then Console.WriteLine($"[neckseam] {edid}: no resuelto en {espName}") : Return
        Dim npcRec = pm.GetRecord(npcFormID)
        Dim npcData = RecordParsers.ParseNPC(npcRec, npcRec.SourcePluginName, pm)
        Dim raceRec = pm.GetRecord(npcData.RaceFormID)
        Dim race = RecordParsers.ParseRACE(raceRec, pm)
        Dim female = npcData.IsFemale
        Console.WriteLine($"=== NECKSEAM {edid} 0x{npcFormID:X8} race='{race.EditorID}' female={female} ===")

        Dim wt = ResolveW(npcData.WeightThin, If(female, race.FemaleDefaultWeightThin, race.MaleDefaultWeightThin))
        Dim wm = ResolveW(npcData.WeightMuscular, If(female, race.FemaleDefaultWeightMuscular, race.MaleDefaultWeightMuscular))
        Dim wf = ResolveW(npcData.WeightFat, If(female, race.FemaleDefaultWeightFat, race.MaleDefaultWeightFat))
        Console.WriteLine($"MWGT thin={wt:F3} musc={wm:F3} fat={wf:F3}  (npc raw: t={FmtN(npcData.WeightThin)} m={FmtN(npcData.WeightMuscular)} f={FmtN(npcData.WeightFat)})")

        Dim nnamX = If(female, race.FemaleNeckNNAMX, race.MaleNeckNNAMX)
        Dim nnamY = If(female, race.FemaleNeckNNAMY, race.MaleNeckNNAMY)
        Console.WriteLine($"RACE NNAM (gender): X={nnamX:F4} Y={nnamY:F4}")

        Dim fmin = If(npcData.FacialMorphIntensity <= 0.0F, 1.0F, npcData.FacialMorphIntensity)
        Dim neckRegionId As Long = -1
        Dim block2 As Single = 0.0F
        Dim genderKey = If(female, "Female", "Male")
        Dim frPath = $"meshes\actors\character\characterassets\{race.EditorID}FacialBoneRegions{genderKey}.txt".ToLowerInvariant()
        Dim frLoc As FilesDictionary_class.File_Location = Nothing
        If FilesDictionary_class.Dictionary.TryGetValue(frPath, frLoc) Then
            Try
                Dim fr = FacialBoneRegionsFile.ParseFromBytes(frLoc.GetBytes)
                Dim neck = fr.Regions.FirstOrDefault(Function(kv) kv.Value.IsNeckRegion)
                If neck.Value IsNot Nothing Then
                    neckRegionId = neck.Key
                    Dim fm = npcData.FaceMorphs.FirstOrDefault(Function(f) f.Index = neck.Key)
                    If fm IsNot Nothing Then block2 = fm.PositionZ
                End If
            Catch ex As Exception
                Console.WriteLine($"  [FacialBoneRegions parse fallo: {ex.Message}]")
            End Try
        Else
            Console.WriteLine($"  [FacialBoneRegions no encontrado: {frPath}]")
        End If
        Dim neckScaleY As Single = 1.0F, neckScaleZ As Single = 1.0F
        If block2 > 0.0F Then
            neckScaleY = 1.0F + nnamX * fmin * block2
            neckScaleZ = 1.0F + nnamY * fmin * block2
        End If
        Console.WriteLine($"FMIN={fmin:F3}  IsNeckRegion id={neckRegionId}  block2(FMRS posZ)={block2:F4}")
        Console.WriteLine($"==> NNAM neckScale: Y={neckScaleY:F4} Z={neckScaleZ:F4}  {If(block2 > 0.0F AndAlso (nnamX <> 0 OrElse nnamY <> 0), "NNAM ACTIVO", "NNAM no-op")}")

        Dim mrsv = npcData.BodyMorphRegionValues
        Console.WriteLine($"MRSV=[{If(mrsv Is Nothing, "null", String.Join(",", mrsv.Select(Function(x) x.ToString("F3"))))}]")

        Dim skelBind As New SkeletonInstance()
        Dim skelNnam As New SkeletonInstance()
        If Not skelBind.LoadFromKey("meshes\actors\character\characterassets\skeleton.nif") OrElse
           Not skelNnam.LoadFromKey("meshes\actors\character\characterassets\skeleton.nif") Then
            Console.WriteLine("[neckseam] skeleton.nif no carga -> sin math de costura.") : Return
        End If
        Dim neckBone As HierarchiBone_class = Nothing
        If skelBind.SkeletonDictionary.TryGetValue("Neck", neckBone) Then
            Dim parentName = If(neckBone.Parent IsNot Nothing, neckBone.Parent.BoneName, "<root>")
            Dim kids = String.Join(", ", neckBone.Childrens.Select(Function(c) c.BoneName))
            Console.WriteLine($"skel: Neck parent='{parentName}' worldT=({neckBone.GetGlobalTransform.Translation.X:F2},{neckBone.GetGlobalTransform.Translation.Y:F2},{neckBone.GetGlobalTransform.Translation.Z:F2})  children=[{kids}]")
        End If
        ' Pose NNAM-only sobre el literal "Neck" en skelNnam; skelBind queda sin morph. La diferencia
        ' de GLOBALS entre ambos en CUALQUIER bone = el efecto del NNAM incl. PROPAGACIÓN a descendientes.
        Dim pose As New Poses_class With {.Name = "nnam", .Source = Poses_class.Pose_Source_Enum.WardrobeManager, .Transforms = New Dictionary(Of String, PoseTransformData)(StringComparer.OrdinalIgnoreCase)}
        pose.Transforms("Neck") = New PoseTransformData With {.ScaleX = 1.0F, .ScaleY = neckScaleY, .ScaleZ = neckScaleZ}
        skelNnam.ApplyBoneMorphPose(pose)
        Console.WriteLine("-- propagación NNAM: |Δorigin| de cada bone entre skelBind y skelNnam (>0 = el NNAM lo mueve) --")
        For Each bn In {"Neck", "Neck1", "Neck1_skin", "Neck_skin", "Neck_Low_skin", "Chest_skin", "Chest", "Head", "HEAD", "Spine2", "Spine2_skin"}
            Dim bbb As HierarchiBone_class = Nothing, nnn As HierarchiBone_class = Nothing
            If skelBind.SkeletonDictionary.TryGetValue(bn, bbb) AndAlso skelNnam.SkeletonDictionary.TryGetValue(bn, nnn) Then
                Dim tb = bbb.GetGlobalTransform.Translation, tn = nnn.GetGlobalTransform.Translation
                Dim dx = tn.X - tb.X, dy = tn.Y - tb.Y, dz = tn.Z - tb.Z
                Console.WriteLine($"   '{bn}': |Δorigin|={Math.Sqrt(dx * dx + dy * dy + dz * dz):F4}  Δ=({dx:F3},{dy:F3},{dz:F3})")
            End If
        Next
        ' Locales de los hijos DIRECTOS de "Neck": si rotIdentity=True, la compensación S^-1 conjugada
        ' (L_C^-1·S^-1·L_C) es una escala+traslación LIMPIA (representable); si hay rotación, da shear.
        If neckBone IsNot Nothing Then
            Console.WriteLine("-- hijos directos de 'Neck' (para evaluar compensación por-pose): --")
            For Each ch In neckBone.Childrens
                Dim lt = ch.OriginalLocaLTransform
                If lt Is Nothing Then Continue For
                Dim r = lt.Rotation
                Dim isIdent = Math.Abs(r.M11 - 1) < 0.001F AndAlso Math.Abs(r.M22 - 1) < 0.001F AndAlso Math.Abs(r.M33 - 1) < 0.001F AndAlso
                              Math.Abs(r.M12) < 0.001F AndAlso Math.Abs(r.M13) < 0.001F AndAlso Math.Abs(r.M21) < 0.001F AndAlso
                              Math.Abs(r.M23) < 0.001F AndAlso Math.Abs(r.M31) < 0.001F AndAlso Math.Abs(r.M32) < 0.001F
                Console.WriteLine($"   child '{ch.BoneName}': localT=({lt.Translation.X:F3},{lt.Translation.Y:F3},{lt.Translation.Z:F3}) rotIdentity={isIdent}")
                Console.WriteLine($"      R=[{r.M11:F4},{r.M12:F4},{r.M13:F4} / {r.M21:F4},{r.M22:F4},{r.M23:F4} / {r.M31:F4},{r.M32:F4},{r.M33:F4}]")
                ' recursar un nivel: el hijo real (HEAD/Neck_skin) bajo el _Offset
                For Each gch In ch.Childrens
                    Dim glt = gch.OriginalLocaLTransform
                    If glt Is Nothing Then Continue For
                    Dim gr = glt.Rotation
                    Dim gIdent = Math.Abs(gr.M11 - 1) < 0.001F AndAlso Math.Abs(gr.M22 - 1) < 0.001F AndAlso Math.Abs(gr.M33 - 1) < 0.001F AndAlso
                                 Math.Abs(gr.M12) < 0.001F AndAlso Math.Abs(gr.M13) < 0.001F AndAlso Math.Abs(gr.M21) < 0.001F AndAlso
                                 Math.Abs(gr.M23) < 0.001F AndAlso Math.Abs(gr.M31) < 0.001F AndAlso Math.Abs(gr.M32) < 0.001F
                    Console.WriteLine($"        grandchild '{gch.BoneName}': localT=({glt.Translation.X:F3},{glt.Translation.Y:F3},{glt.Translation.Z:F3}) rotIdentity={gIdent}")
                Next
            Next
        End If

        Console.WriteLine("-- HEAD PARTS: verts del nape (bottom-Z) + bones que pesan + gap NNAM por vert pegado a 'Neck' (world X/Y/Z) --")
        If npcData.HeadPartFormIDs IsNot Nothing Then
            For Each hpId In npcData.HeadPartFormIDs
                Dim rec = pm.GetRecord(hpId)
                If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Continue For
                Dim hdpt = RecordParsers.ParseHDPT(rec, pm)
                If String.IsNullOrWhiteSpace(hdpt.MeshPath) Then Continue For
                Dim mp = hdpt.MeshPath.Replace("/"c, "\"c).TrimStart("\"c)
                If Not mp.StartsWith("meshes\", StringComparison.OrdinalIgnoreCase) Then mp = "Meshes\" & mp
                Dim nifBytes = FilesDictionary_class.GetBytes(mp)
                If nifBytes Is Nothing OrElse nifBytes.Length = 0 Then Continue For
                Dim nif As New Nifcontent_Class_Manolo() : nif.Load_Manolo(nifBytes)
                Console.WriteLine($"  HDPT '{rec.EditorID}' mesh='{hdpt.MeshPath}'")
                AnalyzeShapeSeam(nif, skelBind, skelNnam, False)
            Next
        End If

        ' --- BODY side (vanilla female body como proxy del cuello del body/outfit): verts del cuello (top-Z) ---
        Console.WriteLine("-- BODY (femalebody.nif): verts del cuello (top-Z) + bones + gap NNAM por vert pegado a 'Neck' --")
        For Each bodyKey In {"meshes\actors\character\characterassets\femalebody.nif", "meshes\actors\character\characterassets\femalebody_0.nif", "meshes\actors\character\characterassets\femalebody_1.nif"}
            Dim bb = FilesDictionary_class.GetBytes(bodyKey)
            If bb Is Nothing OrElse bb.Length = 0 Then Continue For
            Dim bnif As New Nifcontent_Class_Manolo() : bnif.Load_Manolo(bb)
            Console.WriteLine($"  BODY mesh='{bodyKey}'")
            AnalyzeShapeSeam(bnif, skelBind, skelNnam, True)
            Exit For
        Next

        Console.WriteLine("-- per-bone body-weight scale (Layer1 weight K-term + Layer3 MRSV), engine region table --")
        DumpNeckBoneScales(race, female, wt, wm, wf, mrsv)
    End Sub

    Private Function ResolveW(npcVal As Single?, raceDefault As Single?) As Single
        If npcVal.HasValue AndAlso Not Single.IsNaN(npcVal.Value) AndAlso npcVal.Value <> Single.MaxValue Then Return npcVal.Value
        If raceDefault.HasValue Then Return raceDefault.Value
        Return 0.0F
    End Function

    Private Function FmtN(v As Single?) As String
        Return If(v.HasValue, v.Value.ToString("F3"), "null")
    End Function

    ''' <summary>Por shape: verts de la costura (nape=bottom-Z para head, cuello=top-Z para body),
    ''' bones a los que pesan, y el gap NNAM por vert = posición skinned en skelNnam menos skelBind,
    ''' sumando TODOS los bones (captura la PROPAGACIÓN del scale de "Neck" a sus descendientes _skin).</summary>
    Private Sub AnalyzeShapeSeam(nif As Nifcontent_Class_Manolo, skelBind As SkeletonInstance, skelNnam As SkeletonInstance, useTop As Boolean)
        For blkIdx = 0 To nif.Blocks.Count - 1
            Dim shp = TryCast(nif.Blocks(blkIdx), NiflySharp.INiShape)
            If shp Is Nothing Then Continue For
            Dim rs As NifRenderableShape
            Try
                rs = New NifRenderableShape(nif, shp, blkIdx)
            Catch
                Continue For
            End Try
            Dim geom = rs.Geometry
            If geom Is Nothing OrElse Not geom.IsSkinned Then Continue For
            Dim verts = geom.GetVertexPositions()
            Dim skin = geom.GetSkinning()
            If verts Is Nothing OrElse verts.Count = 0 OrElse skin.BoneIndices Is Nothing Then Continue For
            Dim wpv = If(skin.WeightsPerVertex > 0, skin.WeightsPerVertex, 4)

            ' Precompute per-palette skin matrices (bind + nnam) summing the SAME shape-bind localT
            ' over the bind vs NNAM-posed skeleton globals. Diff != 0 sólo en bones que el NNAM mueve.
            Dim nB = rs.ShapeBones.Count
            Dim matBind(Math.Max(0, nB - 1)) As OpenTK.Mathematics.Matrix4d
            Dim matNnam(Math.Max(0, nB - 1)) As OpenTK.Mathematics.Matrix4d
            Dim hasBone(Math.Max(0, nB - 1)) As Boolean
            Dim boneNm(Math.Max(0, nB - 1)) As String
            For k = 0 To nB - 1
                Dim bn = TryCast(rs.ShapeBones(k), NiflySharp.Blocks.NiNode)
                Dim nm = If(bn?.Name?.String, "")
                boneNm(k) = nm
                Dim localT As Transform_Class = If(k < rs.ShapeBoneTransforms.Count, rs.ShapeBoneTransforms(k), Nothing)
                Dim bb As HierarchiBone_class = Nothing, nnb As HierarchiBone_class = Nothing
                If localT IsNot Nothing AndAlso skelBind.SkeletonDictionary.TryGetValue(nm, bb) AndAlso skelNnam.SkeletonDictionary.TryGetValue(nm, nnb) Then
                    matBind(k) = bb.GetGlobalTransform.ComposeTransforms(localT).ToMatrix4d()
                    matNnam(k) = nnb.GetGlobalTransform.ComposeTransforms(localT).ToMatrix4d()
                    hasBone(k) = True
                End If
            Next

            ' Full-mesh tally: ¿cuántos verts pesan al hueso LITERAL "Neck"? Decide si un NNAM per-bone
            ' (no-propagante, como el engine) haría ALGO o sería inert. Si 0 verts -> NNAM per-bone inert.
            Dim neckPal As Integer = -1
            For k = 0 To nB - 1
                If String.Equals(boneNm(k), "Neck", StringComparison.OrdinalIgnoreCase) Then neckPal = k : Exit For
            Next
            If neckPal >= 0 Then
                Dim nNeck = 0
                Dim zmin = Single.MaxValue, zmax = Single.MinValue
                Dim wmax As Single = 0
                Dim wsum As Double = 0
                For i = 0 To verts.Count - 1
                    For j = 0 To wpv - 1
                        If CInt(skin.BoneIndices(i * wpv + j)) = neckPal Then
                            Dim w = CType(skin.BoneWeights(i * wpv + j), Single)
                            If w > 0 Then
                                nNeck += 1 : wsum += w
                                If w > wmax Then wmax = w
                                zmin = Math.Min(zmin, verts(i).Z) : zmax = Math.Max(zmax, verts(i).Z)
                            End If
                        End If
                    Next
                Next
                Console.WriteLine($"    [Neck-literal] verts pegados a 'Neck': {nNeck}/{verts.Count} (wmax={wmax:F2} wsum={wsum:F1} Zrange=[{zmin:F1}..{zmax:F1}])")
            Else
                Console.WriteLine($"    [Neck-literal] 'Neck' NO está en la palette de este shape")
            End If

            Dim minZ = Single.MaxValue, maxZ = Single.MinValue
            For Each v In verts
                minZ = Math.Min(minZ, v.Z) : maxZ = Math.Max(maxZ, v.Z)
            Next
            Dim zThresh = If(useTop, maxZ - (maxZ - minZ) * 0.08F, minZ + (maxZ - minZ) * 0.08F)

            Dim seamCount = 0
            Dim boneWeightAcc As New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase)
            Dim maxGap As Double = 0
            Dim gapAccX As Double = 0, gapAccY As Double = 0, gapAccZ As Double = 0
            Dim nWithGap = 0
            Dim sampleShown = 0
            For i = 0 To verts.Count - 1
                If useTop Then
                    If verts(i).Z < zThresh Then Continue For
                Else
                    If verts(i).Z > zThresh Then Continue For
                End If
                seamCount += 1
                Dim vv As New OpenTK.Mathematics.Vector3d(verts(i).X, verts(i).Y, verts(i).Z)
                Dim sb As New OpenTK.Mathematics.Vector3d(0, 0, 0)
                Dim sn As New OpenTK.Mathematics.Vector3d(0, 0, 0)
                For j = 0 To wpv - 1
                    Dim pal = CInt(skin.BoneIndices(i * wpv + j))
                    Dim w = CType(skin.BoneWeights(i * wpv + j), Single)
                    If w <= 0 OrElse pal >= nB Then Continue For
                    boneWeightAcc(boneNm(pal)) = boneWeightAcc.GetValueOrDefault(boneNm(pal)) + w
                    If hasBone(pal) Then
                        sb += OpenTK.Mathematics.Vector3d.TransformPosition(vv, matBind(pal)) * CDbl(w)
                        sn += OpenTK.Mathematics.Vector3d.TransformPosition(vv, matNnam(pal)) * CDbl(w)
                    End If
                Next
                Dim gx = sn.X - sb.X, gy = sn.Y - sb.Y, gz = sn.Z - sb.Z
                Dim mag = Math.Sqrt(gx * gx + gy * gy + gz * gz)
                gapAccX += gx : gapAccY += gy : gapAccZ += gz
                If mag > 0.0005 Then nWithGap += 1
                If mag > maxGap Then maxGap = mag
                If mag > 0.0005 AndAlso sampleShown < 4 Then
                    Console.WriteLine($"      seam#{i} pos=({verts(i).X:F2},{verts(i).Y:F2},{verts(i).Z:F2}) bind=({sb.X:F2},{sb.Y:F2},{sb.Z:F2}) gap=({gx:F3},{gy:F3},{gz:F3}) |{mag:F3}|")
                    sampleShown += 1
                End If
            Next
            If seamCount = 0 Then Continue For
            Dim topBones = boneWeightAcc.OrderByDescending(Function(kv) kv.Value).Take(6).Select(Function(kv) $"{kv.Key}={kv.Value:F1}")
            Console.WriteLine($"    shape '{rs.ShapeName}': seamVerts={seamCount} (top={useTop}) bones: {String.Join(", ", topBones)}")
            Console.WriteLine($"      NNAM gap @seam (propagación incl.): vertsConGap={nWithGap}/{seamCount} avg=({gapAccX / seamCount:F3},{gapAccY / seamCount:F3},{gapAccZ / seamCount:F3}) max|gap|={maxGap:F3}")
        Next
    End Sub

    ''' <summary>Escala body-weight por hueso (Layer1 weight K-term + Layer3 MRSV), tabla engine.</summary>
    Private Sub DumpNeckBoneScales(race As RACE_Data, female As Boolean, wt As Single, wm As Single, wf As Single, mrsv As List(Of Single))
        Dim region As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase) From {
            {"Head_skin", 0}, {"Face_skin", 0}, {"Neck1_skin", 0},
            {"Neck_skin", 1}, {"chest_skin", 1}, {"Chest_Rear_Skin", 1}, {"Chest_Upper_Skin", 1}, {"Neck_Low_skin", 1}, {"Spine2_skin", 1}, {"Spine2_Rear_skin", 1}}
        Dim tx = wm * 0.5F + wf - 0.5F
        Dim ty = (wt + wf) * 0.866025F - 0.577350F
        Dim kk = (0.866025F - CSng(Math.Sqrt(tx * tx + ty * ty))) * 1.154701F
        Dim gb = race.BoneData.FirstOrDefault(Function(b) b.Gender = If(female, 1UI, 0UI))
        If gb Is Nothing Then Console.WriteLine("   (sin BoneData para el género)") : Return
        Dim names = {"Neck1_skin", "Neck_skin", "Neck_Low_skin", "Spine2_skin", "Chest_skin", "Chest_Upper_Skin", "Head_skin", "Face_skin"}
        For Each nm In names
            Dim bd = gb.Bones.FirstOrDefault(Function(x) x.BoneName.Equals(nm, StringComparison.OrdinalIgnoreCase))
            If bd Is Nothing Then Console.WriteLine($"   {nm}: <no en RACE.BoneData>") : Continue For
            Dim sx = 1.0F, sy = 1.0F, sz = 1.0F
            If bd.HasWeightScale Then
                sx = bd.ThinX * wt + bd.MuscularX * wm + bd.FatX * wf - ((bd.ThinX + bd.MuscularX + bd.FatX) / 3.0F - 1.0F) * kk
                sy = bd.ThinY * wt + bd.MuscularY * wm + bd.FatY * wf - ((bd.ThinY + bd.MuscularY + bd.FatY) / 3.0F - 1.0F) * kk
                sz = bd.ThinZ * wt + bd.MuscularZ * wm + bd.FatZ * wf - ((bd.ThinZ + bd.MuscularZ + bd.FatZ) / 3.0F - 1.0F) * kk
            End If
            Dim regIdx As Integer = -1
            Dim tmp As Integer
            If region.TryGetValue(nm, tmp) Then regIdx = tmp
            If bd.HasRangeModifier AndAlso mrsv IsNot Nothing AndAlso regIdx >= 0 AndAlso regIdx < mrsv.Count Then
                Dim slider = mrsv(regIdx)
                If slider >= 0 Then
                    sy += slider * bd.MaxY : sz += slider * bd.MaxZ
                Else
                    sy += (-slider) * bd.MinY : sz += (-slider) * bd.MinZ
                End If
            End If
            Console.WriteLine($"   {nm}: region={regIdx} WS={bd.HasWeightScale} RM={bd.HasRangeModifier} -> scale=({sx:F4},{sy:F4},{sz:F4})")
        Next
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
                Case "--buildfacegen" : a.BuildFaceGen = True : i += 1
                Case "--vanillaonly" : a.VanillaOnly = True : i += 1
                Case "--rankby" : a.RankBy = v.ToLowerInvariant() : i += 2
                Case "--info" : a.Info = True : i += 1
                Case "--tints" : a.Tints = True : i += 1
                Case "--ttedscan" : a.TtedScan = True : i += 1
                Case "--scandiff" : a.ScanDiff = True : i += 1
                Case "--raceanim" : a.RaceAnim = True : i += 1
                Case "--mountvalidate" : a.MountValidate = True : i += 1
                Case "--findhkx" : a.FindHkx = v : i += 2
                Case "--chunkcompare" : a.ChunkCompare = v : i += 2
                Case "--dumpbehavior" : a.DumpBehavior = v : i += 2
                Case "--hkxcoverage" : a.HkxCoverage = True : i += 1
                Case "--kwtype" : a.KwType = v : i += 2
                Case "--statemap" : a.StateMap = True : i += 1
                Case "--clipresolve" : a.ClipResolve = True : i += 1
                Case "--hkxbone" : a.HkxBone = v : i += 2
                Case "--clipbase" : a.ClipBase = v : i += 2
                Case "--blendhintscan" : a.BlendHintScan = v : i += 2
                Case "--findfile" : a.FindFile = v : i += 2
                Case "--provenance" : a.Provenance = True : i += 1
                Case "--dumpref" : a.DumpRef = v : i += 2
                Case "--nifdump" : a.NifDump = v : i += 2
                Case "--animsynccheck" : a.AnimSyncCheck = v : i += 2
                Case "--catprofile" : a.CatProfile = True : i += 1
                Case "--neckseam" : a.NeckSeam = True : i += 1
                Case "-h", "--help" : PrintUsage() : Return Nothing
                Case Else
                    Console.Error.WriteLine($"Arg desconocido: {args(i)}") : PrintUsage() : Return Nothing
            End Select
        End While
        If a.ListPath = "" AndAlso (a.Esp = "" OrElse a.Edid = "") AndAlso Not a.TtedScan AndAlso Not a.ScanDiff AndAlso Not a.RaceAnim AndAlso Not a.MountValidate AndAlso a.FindHkx = "" AndAlso a.ChunkCompare = "" AndAlso a.DumpBehavior = "" AndAlso Not a.HkxCoverage AndAlso a.KwType = "" AndAlso Not a.StateMap AndAlso Not a.ClipResolve AndAlso a.HkxBone = "" AndAlso a.ClipBase = "" AndAlso a.FindFile = "" AndAlso a.NifDump = "" AndAlso a.AnimSyncCheck = "" AndAlso a.BlendHintScan = "" AndAlso Not a.CatProfile AndAlso a.DumpRef = "" Then
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
