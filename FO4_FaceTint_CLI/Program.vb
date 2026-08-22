Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

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
        Public Game As Config_App.Game_Enum = Config_App.Game_Enum.Fallout4  ' --game fo4|skyrim: motor destino (afecta encoding, load order, y la rama SSE del bake)
        ' Valor CRUDO de --game tal cual lo tipeo el usuario (Nothing si no se paso). Solo para el
        ' banner: permite ver en el log que se PIDIO, no solo que se resolvio.
        Public GameRaw As String = Nothing
        Public CompareCk As Boolean = False      ' --compareck: tras --buildfacegen, diff del NIF baked REAL (on-disk) + facetint DDS vs la ref del CK (BSA/loose)
        ' --comparefiles "<ckNif>|<ourNif>": diff EXHAUSTIVO de DOS NIFs sueltos on-disk, SIN hornear y SIN montar plugins.
        ' Reusa CompareShapeExhaustive/Shader/Alpha (posiciones, tris, normals/tangents/UV/vcol/skin, VertexDesc, bounds,
        ' texslots, TODO el BSLightingShaderProperty y el NiAlphaProperty). El CK sale del path suelto que se le pasa
        ' (NO del archive), asi que sirve para comparar contra la salida canonica del CK que el usuario dejo loose.
        ' Requiere --game sse|fo4 (gobierna la rama de flags de shader). La textura (TGA) se compara aparte.
        Public CompareFiles As String = ""
        ' --dumpnif <nif>: volcado COMPLETO sin umbrales ni clasificacion, ordenado por nombre para diff limpio.
        ' Por nodo/shape: transform (T/R/S). Por shape: vdesc/bounds + TODO el BSLightingShaderProperty (type,
        ' flags, escalares, colores, UV), TODOS los texslots (0..N incl. vacios) y el NiAlphaProperty. Correr en
        ' los dos NIFs y hacer `diff` revela CUALQUIER diferencia de campo que el comparador con umbral esconde.
        Public DumpNif As String = ""
        ' --skincheck "<ckNif>|<ourNif>": calcula la posicion RENDERIZADA en bind-pose de cada vertice
        ' (Sum_k w_k * (boneWorld_k o skinToBone_k) * V) en LOS DOS NIFs y las diffea por shape. Es la
        ' verdad de terreno de "se ven distintos": si el nodo rotado esta compensado por el bind, la
        ' posicion skinneada es identica (delta ~0) y la rotacion del nodo es INERTE. Sin hornear, sin plugins.
        Public SkinCheck As String = ""
        Public SseCompareBatch As Boolean = False ' --ssecomparebatch [N]: barrido 100% — bakea+compara TODOS los NPC_ vanilla con FaceGeom, agrega diffs por categoría
        Public SseCompareBatchLimit As Integer = 0
        ''' <summary>--headfidelity: corre el mismo barrido que --ssecomparebatch y ADEMÁS mide, por shape,
        ''' preview-vs-juego (FaceGenBuildPipeline.CollectHeadFidelity). Implica --ssecomparebatch. Sólo
        ''' mide: no cambia nada de lo que el bake escribe.</summary>
        Public HeadFidelity As Boolean = False
        Public VertexBatch As Boolean = False    ' --vertexbatch [N]: game-aware (usa --game); bakea TODOS los vanilla con FaceGeom y reporta max vertex diff + outliers (posición)
        Public VertexBatchLimit As Integer = 0
        Public VertexBatchOut As String = ""     ' --vbout <csv>: CSV resumible del batch (append+flush por NPC; sentinel .cur para saltear el NPC que crashea el proceso)
        Public PosDump As String = ""            ' --posdump <nifPath|key>|<outcsv>: vuelca posiciones por-vértice de cada shape (para analizar deformación CK vs fuente)
        Public MeshShaders As String = ""        ' --meshshaders <meshkey>: dump SSPF1/2 + type de cada shape del mesh source
        Public BuildFaceGen As Boolean = False   ' --buildfacegen: bake completo (NIF + 3 DDS) headless via FaceGenBuilder (path CPU, _2 sandbox)
        Public PosThresh As Double = 0.05       ' --posthresh <v>: umbral de reporte de la categoria "positions"
                                                ' del comparador exhaustivo. Default 0,05 = el historico. Bajarlo
                                                ' expone la banda 0,02-0,05, donde vive el grueso del drift del
                                                ' neutral en FO4 (con 0,05 solo se ve la COLA).
        ' --engineskinblend / --noengineskinblend: fuerza ON/OFF la replica de la normalizacion de
        ' pesos de skin del MOTOR (w3 = 1 - Sum(w0..w2); si w3 <= 0 se descarta SIN renormalizar).
        ' Nothing = usar el default de la app (hoy True, gateado a FO4). La rama OFF se mantiene a
        ' proposito como control de regresion. Fuente/VAs: FO4_Base_Library.EngineSkinWeightNormalization
        ' (CreationKit.exe 0x142B73230 y Fallout4.exe 0x141837390 son la MISMA funcion).
        ' --ddscompare: NO saltear el bloque DDS del comparador ni los skips de imagen del bake. Es la unica
        ' forma de validar CONTENIDO DE PIXELES: la comparacion existe en LOS DOS motores — SSE diffea el
        ' facetint _d y FO4 los tres canales de FaceCustomization (_d/_msn/_s), cada uno como su propia
        ' categoria del reporte (ver CompareFo4FaceCustomizationDds). Si el bloque fuera SSE-ONLY, correr FO4
        ' con --ddscompare pagaria el costo del encode y compararia CERO pixeles = cobertura FALSA.
        ' OJO igual: en FO4 encarece mucho el barrido (compone Y encodea 3 canales a 1024/1024/512 por
        ' NPC contra el unico 512x512 de SSE).
        Public DdsCompare As Boolean = False
        ' --defaults: IGNORA npc_config.json y corre con los defaults COMPILADOS de NPC_Config. Para
        ' barridos de medicion reproducibles, donde heredar el estado mutable del usuario (un checkbox de
        ' la GUI) haria que dos corridas del mismo commit no sean comparables. Sin el flag, el CLI honra la
        ' config del usuario (= la GUI y BakeAllRunner). En AMBOS casos el arranque imprime el valor
        ' efectivo + procedencia de cada opcion que afecta el bake.
        Public Defaults As Boolean = False
        ' --rawdds: hornea los DDS SIN comprimir (B8G8R8A8). Acelera el barrido de GEOMETRIA (el encode BCn es
        ' el grueso del texture-bake) y no altera un solo byte del NIF. Incompatible en INTENCION con
        ' --ddscompare (cambiaria el piso de codec de la comparacion de pixeles contra el CK).
        Public RawDds As Boolean = False
        ' --alphagatescan: mide los DOS gates del alpha de la cabeza sobre TODO el load order, sin hornear.
        ' (1) NPCs con ACBS\Diffuse Alpha Test (0x01000000) — decide si el bit SF2 y la fabricacion del
        '     NiAlphaProperty pueden compartir UN interruptor (el bit SF2 esta en 1 sola shape en todo FO4).
        ' (2) TXST con MNAM cuyo BGSM declara alpha, y QUIEN los referencia — dimensiona el gate
        '     isFaceHeadPart (si ningun referente ajeno a un head part de cara declara alpha, sacarlo es inerte).
        Public AlphaGateScan As Boolean = False
        ' --tintcountscan: capas de tint efectivas por NPC. Test de FALSACION de la hipotesis "los outliers
        ' del facetint son los NPC SIN capas": si hay NPCs sin capas que salen BIEN, la hipotesis se cae.
        Public TintCountScan As Boolean = False
        ' --ddsprobe <formID hex>: vuelca los VALORES DE PIXEL reales del facetint (nuestro vs CK). El RMS y
        ' el maxD son agregados y no distinguen "plano con otro valor" de "plano + variacion espacial": con
        ' maxD=9 y meanD=5 en el mismo caso, la unica forma de saber cual es, es mirar los pixeles.
        Public DdsProbe As String = ""
        ' --recscan "SIG|substr": dump generico de records por signatura + substring del EditorID.
        Public RecScan As String = ""
        ' --meshcollide: alcance real de la colision "dos head parts, una malla" (ver el bloque en Main).
        Public MeshCollide As Boolean = False
        ' --texslotdiff: (NPC, shape, slot) donde nuestro NIF difiere del del CK en un path de textura.
        Public TexSlotDiff As Boolean = False
        ' --dumpacc: acumulador float del facetint, para derivar la regla de redondeo del CK.
        Public DumpAcc As String = ""
        ' --shapeorder: compara la SECUENCIA de shapes del NIF del CK contra nuestro orden de emision.
        Public ShapeOrder As Boolean = False
        Public EngineSkinNorm As Boolean? = Nothing
        Public VanillaOnly As Boolean = False    ' --vanillaonly: con --buildfacegen, SALTEA NPCs cuyo record GANADOR no es vanilla/DLC (overridden por un mod) — para comparar fiel vs CK del BA2
        Public NoCreationClub As Boolean = False   ' --nocc: con --vanillaonly, deja el corpus en base+DLC (sin Creation Club), que es lo unico reproducible entre maquinas
        Public Info As Boolean = False
        Public Tints As Boolean = False
        Public TtedScan As Boolean = False
        Public ScanDiff As Boolean = False      ' --scandiff: NPCs donde el blend-op del app difiere del CK (color-match LAST-wins)
        Public RaceAnim As Boolean = False      ' --raceanim: dump del behavior resuelto por raza (project+subgraphs+SRAC)
        Public RaceCompat As Boolean = False    ' --racecompat: reconstruye proxyRaces de RaceCompatibility (VMAD+.pex) y valida el filtro de raza de los catálogos
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
        ''' <summary>--facegengate: mide el blast radius de haber cambiado el gate de FaceGen del render
        ''' (de la heurística "¿existe el FaceGeom horneado?" al canónico RACE.DATA bit 0x2). Parte A =
        ''' clasificación exhaustiva de TODOS los NPC_; parte B = A/B de geometría horneada vs cruda sobre
        ''' una muestra de los que entran nuevos. READ-ONLY: no escribe ni un NIF.</summary>
        Public FaceGenGate As Boolean = False
        ''' <summary>Corre los self-tests del gate de paridad y sale. No hornea, no monta plugins.</summary>
        Public ParityGate As Boolean = False
        ''' <summary>Con --paritygate: además vuelca los golden del fold listos para pegar. Es un volcado,
        ''' no un gate — se usa para re-congelarlos cuando un cambio de ley los mueve a propósito.</summary>
        Public DumpGolden As Boolean = False
        ''' <summary>--fggsample N: cuántos NPCs del conjunto nuevo se hornean en la parte B (0 = ninguno).</summary>
        Public FaceGenGateSample As Integer = 40
        Public Provenance As Boolean = False      ' --provenance: SourcePluginName de NPC/RACE/CLFMs del dirt (chequeo vanilla-vs-vanilla)
        Public DumpRef As String = ""             ' --dumpref "<filesDictKey>|<outFile>": vuelca GetBytes(key) crudo a outFile (ref vanilla del BA2)
        Public NifSlots As String = ""       ' --nifslots "<nifA>[|<nifB>]": DIAGNOSTICO shape -> shaderType + texslots
        Public NifDump As String = ""             ' --nifdump <nif>: árbol de nodos (local+world) + skin binds (inv(bind)) por shape
        Public AnimSyncCheck As String = ""       ' --animsynccheck "<chunkNif>|<rigHkx>|<clipHkx>[|frame][|boneFilter]": FK del chunk BUGGY (clip full) vs HONORED (No Anim Sync) → tear
        Public BlendHintScan As String = ""       ' --blendhintscan "<all|substr|path.bsa/.ba2>": tally blendHint (0=NORMAL,1=ADDITIVE_DEPRECATED,2=ADDITIVE) + ejemplos + flag ∉{0,1,2}; path=monta archivo (cross-game)
        Public CatProfile As Boolean = False      ' --catprofile [--edid X]: perfila ejes de categoría (folder ground-truth, Perspective, STKD, BlendHint) por raza
        Public RankBy As String = "n"   ' canal por el que rankea el sweep: n (default) / d / s
        Public NeckSeam As Boolean = False       ' --neckseam --esp X --edid Y: diagnóstico costura cuello/cabeza con body-weight (NNAM + _skin, math)
        Public OutfitScan As String = ""         ' --outfitscan <archivo con FormIDs hex, uno por linea>: DOFT + si el outfit es DETERMINISTA o LEVELED
        Public EstimateSclp As String = ""        ' --estimatesclp "<underarmorNifKey>|<bodyNifKey>[|<sclpKey>]": estima SCLP por ratio de extents en espacio-hueso vs body de ref, contra el .sclp autorado
        Public SclpDiag As String = ""            ' --sclpdiag "<uaNifKey>|<bodyNifKey>|<boneSubstr>": vuelca geometría cruda por hueso (allSet/domSet, percentiles, ratios candidatos) para derivar a mano la fórmula del SCLP
        Public SclpBatch As String = ""           ' --sclpbatch <manifestPath>: evalúa MUCHAS combinaciones (una línea por caso: label|uaKeyOrPath|bodyKeyOrPath|authoredSclpPath) en UNA sola corrida (un solo mount); imprime medErr/meanErr/within del estNN vs SCLP autorado por caso
        Public Ba2Extract As String = ""          ' --ba2extract "<archivePath>|<internalKey>|<outFile>": extrae UNA entry (por FullPath interno) de un BA2/BSA a un archivo de disco (ref vanilla directa, sin loose override). File-only, no monta plugins.
        Public BindDiff As String = ""            ' --binddiff "<uaNifKey|path>|<bodyNifKey|path>|<boneSubstr opcional>": compara los binds skin→bone (SkinToBone) de cada hueso *_skin entre dos NIFs (¿la escala del SCLP vive en el bind?)
        Public ShapeFilter As String = ""         ' --shapefilter <substr>: acompaña a --estimatesclp/--sclpdiag; restringe la malla UNDERARMOR (ua) a las shapes cuyo ShapeName contenga el substring (case-insensitive). El body de referencia NO se filtra. Sirve para excluir un cuerpo desnudo embebido (ej. BaseFemaleBody:0) y quedarse solo con la shape del outfit (ej. RaiderUnderArmorF:0).
    End Class

    ' TETI.Slot enum — nombre por valor de cada slot de tinte, para --tints.
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
        ' HERRAMIENTA, NO APP. `Logger.Enabled` esta forzado a False en builds Release (ver Logger.vb) para que
        ' ninguna app de usuario pague los calculos de diagnostico. Este CLI es la excepcion DOCUMENTADA: usa
        ' `Logger.Enabled` como interruptor SEMANTICO — `FaceGenBuilder.DebugMode` lo lee y de ahi sale el sufijo
        ' `_2` del sandbox. Sin esta linea, un `--buildfacegen` compilado en Release escribiria los nombres
        ' CANONICOS y pisaria el bake del CK, que es la referencia contra la que compara.
        Logger.AllowInReleaseBuilds = True
        Try
            ' El componente nativo de texturas se chequea ANTES de trabajar. Este CLI no tiene a nadie
            ' mirando: con un DirectXTexWrapper.dll de otra plataforma cada textura DX10 de un BA2 se lee
            ' como 0 bytes —esta medido, ver el comentario del .vbproj— y el barrido termina en verde
            ' habiendo escrito basura. Ver DirectXTexWrapperGate.
            Dim fallaWrapper = DirectXTexWrapperGate.Verificar()
            If fallaWrapper <> "" Then
                Console.Error.WriteLine(fallaWrapper)
                Environment.ExitCode = 1
                Return
            End If
            Run(args)
        Catch ex As Exception
            Console.Error.WriteLine("FATAL: " & ex.ToString())
            Environment.ExitCode = 1
        End Try
    End Sub

    Private Sub Run(args As String())
        Dim opt = ParseArgs(args)
        ' Argumentos invalidos => exit code != 0. Antes se hacia `Return` a secas y el proceso
        ' terminaba con 0: un script de barrido no podia distinguir "corrio bien" de "ni arranco".
        ' (--help tambien cae aca; se acepta el 2 porque este CLI es un arnes de medicion, no una
        ' herramienta interactiva: es preferible que un exit 0 signifique SIEMPRE "midio".)
        If opt Is Nothing Then
            Environment.ExitCode = 2
            Return
        End If

        ' --- BA2EXTRACT: extrae UNA entry cruda de un BA2/BSA a disco (ref vanilla directa, sin loose override).
        '     File-only y despachado ANTES del bootstrap de plugins/FilesDictionary: es rápido y no depende
        '     del load order. ---
        If opt.Ba2Extract <> "" Then Ba2ExtractRun(opt.Ba2Extract) : Return

        ' --- PARITYGATE: corre los self-tests del gate SIN hornear. Hasta ahora el gate sólo se podía
        '     ejercitar desde un bake (EnsureSimdParityGate lo llama BuildCharGen), así que verificar un
        '     cambio de ley costaba una corrida entera. No toca disco, no monta plugins, no necesita --data. ---
        If opt.ParityGate Then ParityGateRun(opt.DumpGolden) : Return

        ' --- 1. Config (app config.json local) ---
        ' Exe SEPARADO: no pasa por el RealMain de NPC Manager, asi que declara su propio default.
        Config_App.DefaultShowHelperShapes = False
        Config_App.LoadConfig()
        Config_App.Current.Game = opt.Game

        ' --- COMPAREFILES: diff de dos NIFs sueltos on-disk, sin hornear ni montar plugins. Solo necesita
        '     Config_App.Current.Game (rama de flags de shader). Se despacha aca para NO pagar el bootstrap. ---
        If opt.CompareFiles <> "" Then
            PosReportThreshold = opt.PosThresh
            CompareLooseFilesRun(opt.CompareFiles)
            Return
        End If
        If opt.DumpNif <> "" Then
            DumpNifFull(opt.DumpNif)
            Return
        End If
        If opt.SkinCheck <> "" Then
            SkinCheckRun(opt.SkinCheck)
            Return
        End If

        ' NPC_Config por el MISMO camino que la GUI y el runner del batch.
        ' De todas las propiedades de NPC_Config sólo DOS pueden mover los bytes del bake; el resto es
        ' estado de UI. Son `ReplicateEngineSkinWeightNormalization` (posiciones skinneadas) y
        ' `ApplyGhoulHeadRearFix` (D/N/S del shape). Si se agrega una tercera, hay que auditarla acá.
        ' `--defaults` NO lee la config del usuario: un CLI de MEDICIÓN que hereda estado mutable es una
        ' fuente de irreproducibilidad (dos corridas del mismo commit difieren porque alguien tocó un
        ' checkbox). Los dos roles conviven —bake headless, donde honrar la config es lo correcto, y harness
        ' de barrido, donde lo correcto son los defaults— y el flag los separa. El banner imprime siempre el
        ' valor EFECTIVO y su procedencia, así que ninguna corrida queda ambigua.
        Dim npcCfgExists = IO.File.Exists(FO4_NPC_Manager.NPC_Config.ConfigFilePath)
        Dim npcCfgSource As String
        If opt.Defaults Then
            npcCfgSource = "compiled defaults (--defaults)"
        ElseIf npcCfgExists Then
            FO4_NPC_Manager.NPC_Config.LoadConfig()
            npcCfgSource = $"npc_config.json ({FO4_NPC_Manager.NPC_Config.ConfigFilePath})"
        Else
            ' LoadConfig() con archivo ausente deja Current intacto (JsonConfigIO.Load devuelve Nothing),
            ' pero se llama igual para no divergir del camino de la GUI si eso cambiara.
            FO4_NPC_Manager.NPC_Config.LoadConfig()
            npcCfgSource = $"compiled defaults (no npc_config.json at {FO4_NPC_Manager.NPC_Config.ConfigFilePath})"
        End If

        ' Replica de la normalizacion de pesos del MOTOR. Sin flag se respeta el valor de la config (o el
        ' default si no hay config); con --engineskinblend/--noengineskinblend se fuerza. El gate por juego
        ' es el MISMO que usa la app (solo FO4), asi que el CLI no puede encenderla en un motor donde no
        ' esta verificada por RE. VA DESPUES de LoadConfig(): LoadConfig REEMPLAZA Current, asi que al
        ' reves el override quedaria pisado por la config.
        If opt.EngineSkinNorm.HasValue Then
            FO4_NPC_Manager.NPC_Config.Current.ReplicateEngineSkinWeightNormalization = opt.EngineSkinNorm.Value
        End If
        FO4_NPC_Manager.NPC_Config.ApplyEngineSkinWeightNormalizationGate(opt.Game)
        FO4_NPC_Manager.NPC_Config.ApplyGlDecodeSetting()
        FO4_NPC_Manager.NPC_Config.ApplyDownsizeFromMip0Setting()
        PosReportThreshold = opt.PosThresh
        DdsCompareRequested = opt.DdsCompare
        RawDdsRequested = opt.RawDds

        ' --rawdds: fuerza los CINCO settings de compresion del bake a Uncompressed (B8G8R8A8). Solo cambia el
        ' CODEC del DDS de salida: el NIF no referencia el formato de la textura, asi que un barrido de
        ' GEOMETRIA con --rawdds es comparable byte a byte con uno sin el flag, y se ahorra el encode BCn
        ' (la parte cara del texture-bake). NO usar junto con --ddscompare para juzgar pixeles contra el CK:
        ' el CK escribe BC1/BC3/BC5 y la comparacion cambiaria de piso de codec.
        ' Se setean los CINCO (no solo el diffuse) porque en modo per-layer el N/S NO derivan del diffuse.
        If opt.RawDds Then
            Config_App.Current.Setting_FaceGenDiffuseCompression = FaceTintConvention.FaceTintDiffuseCompression.Uncompressed
            Config_App.Current.Setting_FaceGenDiffuseCompression_SSE = FaceTintConvention.FaceTintDiffuseCompression.Uncompressed
            Config_App.Current.Setting_FaceGenNormalCompression = FaceTintConvention.FaceTintNormalSpecularCompression.Uncompressed
            Config_App.Current.Setting_FaceGenNormalCompression_SSE = FaceTintConvention.FaceTintNormalSpecularCompression.Uncompressed
            Config_App.Current.Setting_FaceGenSpecularCompression = FaceTintConvention.FaceTintNormalSpecularCompression.Uncompressed
        End If

        ' === BANNER: valor EFECTIVO de cada opcion que afecta el bake, con su procedencia. ===
        ' Requisito duro: ninguna corrida futura puede ser ambigua sobre con que opciones se horneo.
        Console.WriteLine($"[cfg] npc_config source: {npcCfgSource}")
        ' Config_App (compresiones del bake) NO lo cubre --defaults: sale del config.json del usuario. Se
        ' imprime el valor efectivo para que una corrida no quede ambigua sobre con que codec se horneo.
        Console.WriteLine($"[cfg] dds codec: D={Config_App.Current.Setting_FaceGenDiffuseCompression}/{Config_App.Current.Setting_FaceGenDiffuseCompression_SSE}(SSE)" &
                          $" N={Config_App.Current.Setting_FaceGenNormalCompression}/{Config_App.Current.Setting_FaceGenNormalCompression_SSE}(SSE)" &
                          $" S={Config_App.Current.Setting_FaceGenSpecularCompression}" &
                          $"  (--rawdds={opt.RawDds})")
        ' Se imprime el valor CRUDO pedido junto al resuelto: `game=Fallout4` a secas no delataba
        ' que lo pedido habia sido `--game sse`. Con los dos, un barrido rotulado con el juego
        ' equivocado se ve en la primera linea del log.
        Console.WriteLine($"[cfg] game={opt.Game}   (--game '{If(opt.GameRaw, "<no pasado; default>")}')")
        Console.WriteLine($"[cfg]   ReplicateEngineSkinWeightNormalization = {FO4_NPC_Manager.NPC_Config.Current.ReplicateEngineSkinWeightNormalization}" &
                          $" (override={If(opt.EngineSkinNorm.HasValue, opt.EngineSkinNorm.Value.ToString(), "none")})" &
                          $" -> EngineSkinWeightNormalization.Enabled={EngineSkinWeightNormalization.Enabled} (gate: solo FO4)")
        Console.WriteLine($"[cfg]   ApplyGhoulHeadRearFix                  = {FO4_NPC_Manager.NPC_Config.Current.ApplyGhoulHeadRearFix}" &
                          " (afecta D/N/S de la nuca ghoul-female FO4)")
        ' GAME-AWARE: el bake ESCRIBE en Config_App.Current.DataPath, que es ReadOnly y deriva de FO4ExePath
        ' (= el exe del juego ACTIVO del config). Sin esto, `--game sk --data <SkyrimData>` montaba Skyrim para LEER
        ' pero escribía los artefactos al Data del juego del config (p.ej. Fallout 4\Data). Con --data, se apunta el
        ' exe al del juego elegido ⇒ DataPath == --data ⇒ lee y escribe en el MISMO Data del juego pedido.
        If opt.DataPath <> "" Then
            Dim gameDir = IO.Path.GetDirectoryName(opt.DataPath.TrimEnd(IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar))
            Dim exeName = If(opt.Game = Config_App.Game_Enum.Skyrim, "SkyrimSE.exe", "Fallout4.exe")
            Dim exePath = If(String.IsNullOrEmpty(gameDir), "", IO.Path.Combine(gameDir, exeName))
            If Not String.IsNullOrEmpty(exePath) AndAlso IO.File.Exists(exePath) Then
                Config_App.Current.FO4ExePath = exePath
            Else
                Console.Error.WriteLine($"[warn] could not find '{exeName}' next to --data ('{gameDir}'): the bake WILL WRITE to '{Config_App.Current.DataPath}' (config's Data), not to --data.")
            End If
        End If
        Dim dataPath = If(opt.DataPath <> "", opt.DataPath, Config_App.Current.FO4EDataPath)
        ' Se imprime ACA, DESPUES de resolver --data y de reapuntar FO4ExePath: mas arriba mostraba el
        ' Data del config (p.ej. Fallout 4\Data) en una corrida de Skyrim, que es justo el dato que
        ' uno mira para detectar un barrido cruzado. Un banner que miente es peor que no tenerlo.
        Console.WriteLine($"[cfg] data(read)={dataPath}")
        Console.WriteLine($"[cfg] data(bake write)={Config_App.Current.DataPath}")
        ' Comparar por TEXTO crudo daba un falso positivo SIEMPRE: '--data F:/.../Data' (barra normal,
        ' como lo tipea el script) vs el DataPath del config (barra invertida) son LA MISMA ruta y el warn
        ' saltaba en todas las corridas. Un aviso que salta siempre deja de significar algo justo cuando el
        ' caso sea real. Se normaliza separador + barra final + mayusculas antes de comparar.
        If Not SamePath(dataPath, Config_App.Current.DataPath) Then
            Console.Error.WriteLine("[warn] the READ Data and the bake's WRITE Data do NOT match: the artifacts" &
                                    " will land in a different Data than the one that was measured.")
            Console.Error.WriteLine($"       read  ='{dataPath}'")
            Console.Error.WriteLine($"       write ='{Config_App.Current.DataPath}'")
        End If
        If String.IsNullOrEmpty(dataPath) OrElse Not Directory.Exists(dataPath) Then
            Console.Error.WriteLine($"Invalid Data path: '{dataPath}'. Use --data <path to Data\> or configure config.json.")
            Environment.ExitCode = 1 : Return
        End If

        ' --- 2. Config base: secciones FaceTint del config.json del APP (sin copiarlo a la bin) ---
        If opt.ConfigPath <> "" Then
            ' Mismo cuerpo que el cargador del barrido: se delega en vez de repetirlo (eran dos copias del
            ' mismo lector y sólo una se iba a arreglar).
            ApplyConfigJson(opt.ConfigPath)
        End If

        ' --- 2b. Override granular de convencion / orden (opcional, pisa lo anterior) ---
        If opt.ConventionPath <> "" Then
            ' El archivo de --convention es un FaceTintConventionSettings PELADO (sin la clave envolvente),
            ' así que acá no hay clave que elegir: sólo el SLOT, que sí depende del juego. Éste es el sitio
            ' por el que entra el barrido de convenciones de SSE.
            Dim written = FaceTintConvention.SetActiveSettings(Config_App.Current,
                JsonSerializer.Deserialize(Of FaceTintConvention.FaceTintConventionSettings)(File.ReadAllText(opt.ConventionPath)))
            Console.WriteLine($"[cfg] convention <- {opt.ConventionPath}   -> slot '{written}'")
        End If
        If opt.SortPath <> "" Then
            Config_App.Current.Setting_FaceTintSort =
                JsonSerializer.Deserialize(Of FaceTintSortSettings)(File.ReadAllText(opt.SortPath))
            Console.WriteLine($"[cfg] order <- {opt.SortPath}")
        End If

        ' --- 3. Lista de trabajo (esp, edid) ---
        Dim work = BuildWorkList(opt)
        If work.Count = 0 AndAlso opt.DdsProbe = "" AndAlso opt.RecScan = "" AndAlso Not opt.MeshCollide AndAlso opt.DumpAcc = "" AndAlso Not opt.TexSlotDiff AndAlso Not opt.ShapeOrder AndAlso Not opt.TintCountScan AndAlso Not opt.AlphaGateScan AndAlso Not opt.TtedScan AndAlso Not opt.ScanDiff AndAlso Not opt.RaceAnim AndAlso Not opt.RaceCompat AndAlso Not opt.MountValidate AndAlso opt.FindHkx = "" AndAlso opt.ChunkCompare = "" AndAlso opt.DumpBehavior = "" AndAlso Not opt.HkxCoverage AndAlso opt.KwType = "" AndAlso Not opt.StateMap AndAlso Not opt.ClipResolve AndAlso opt.HkxBone = "" AndAlso opt.ClipBase = "" AndAlso opt.FindFile = "" AndAlso opt.NifDump = "" AndAlso opt.NifSlots = "" AndAlso opt.AnimSyncCheck = "" AndAlso opt.BlendHintScan = "" AndAlso Not opt.CatProfile AndAlso Not opt.Provenance AndAlso opt.DumpRef = "" AndAlso opt.EstimateSclp = "" AndAlso opt.SclpDiag = "" AndAlso opt.SclpBatch = "" AndAlso opt.BindDiff = "" AndAlso opt.Ba2Extract = "" AndAlso Not opt.SseCompareBatch AndAlso Not opt.VertexBatch AndAlso opt.PosDump = "" AndAlso opt.MeshShaders = "" AndAlso opt.OutfitScan = "" AndAlso Not opt.FaceGenGate Then
            Console.Error.WriteLine("No NPCs to process (check --edid / --list).") : Environment.ExitCode = 1 : Return
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
        If opt.NoCreationClub Then PluginManager.ExcludeCreationClub = True
        Console.WriteLine("[load] plugins..." & If(opt.VanillaOnly, If(opt.NoCreationClub, " (ONLY base game + DLC — NO Creation Club)", " (ONLY official — vanilla/DLC/cc)"), ""))
        Dim pm As New PluginManager()
        Dim loadList = PluginManager.ReadActiveLoadOrder()
        For Each esp In work.Select(Function(w) w.Esp).Distinct(StringComparer.OrdinalIgnoreCase)
            EnsureEspInLoadList(loadList, esp, dataPath)
        Next
        ' Carga el whitelist de render del NPC Manager + IDLE/AACT (sistema de idles/gestos: GNAM=archivo de anim,
        ' ENAM=evento, ANAM=árbol Parent/Previous por Action) para auditar la cobertura estructural de los huérfanos PoseA.
        Dim sigFilter As New HashSet(Of String)(SIGS_NPC_RENDERING, StringComparer.Ordinal) From {"IDLE", "AACT"}
        pm.LoadAllPlugins(dataPath, loadList, Nothing, sigFilter)

        Console.WriteLine("[load] mounting archives...")
        Dim cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Caches")
        Directory.CreateDirectory(cacheDir)
        FilesDictionary_class.CacheDirectory = cacheDir
        FilesDictionary_class.RegisterExtensions(".ssf", ".sclp", ".hkx", ".hkt")
        Dim noProg As New Progress(Of (Stepn As String, Value As Integer, Max As Integer))()
        FilesDictionary_class.Fill_DictionaryAsync(dataPath, noProg).GetAwaiter().GetResult()

        ' LOS CATÁLOGOS DE SESIÓN, QUE ANTES SÓLO POBLABA LA GUI. `RaceCompatCatalog` y `SliderCatalog`
        ' se construían dentro de `MainForm.EnsureAssetDictionaryAsync`, y este CLI nunca ejecuta MainForm:
        ' en Skyrim horneaba con `RaceCompatCatalog = Nothing` ⇒ `IsHeadPartValidForRace` daba False para
        ' todo el pelo vanilla en razas COtR y salían head-parts DISTINTOS de los de la GUI para el mismo
        ' NPC. Este archivo ya sabía que el problema existía —guarda y restaura RaceCompatCatalog alrededor
        ' de un diagnóstico puntual más abajo— pero el camino principal no lo poblaba.
        ' Va DESPUÉS de montar el diccionario: el catálogo de sliders lee su config a través de él.
        FO4_NPC_Manager.NpcSessionCatalogs.EnsureLoaded(pm)

        ' --- OUTFITSCAN: por cada NPC de la lista, su DOFT y si el outfit es DETERMINISTA o LEVELED ---
        ' Existe para poner a prueba una hipotesis concreta sobre el bit Hidden: el CK oculta head parts
        ' tapadas por el casco/sombrero del outfit con el que hornea. Si el outfit sale de una lista
        ' NIVELADA, lo que el CK resolvio es UNA tirada entre varias y el bit no es reproducible por
        ' definicion. Distinguir "deterministico" de "nivelado" es lo que decide si hay algo que arreglar.
        If opt.OutfitScan <> "" Then
            OutfitScanRun(pm, opt.OutfitScan)
            Return
        End If

        ' --- MESHSHADERS: dump SSPF1/2+type de cada shape de un mesh source (para entender flags heredados) ---
        If opt.MeshShaders <> "" Then
            Dim bytes As Byte()
            If IO.File.Exists(opt.MeshShaders) Then
                bytes = IO.File.ReadAllBytes(opt.MeshShaders)
            Else
                Dim key = opt.MeshShaders.Replace("/"c, "\"c).ToLowerInvariant()
                If Not key.StartsWith("meshes\") Then key = "meshes\" & key
                bytes = FilesDictionary_class.GetBytes(key)
            End If
            If bytes Is Nothing Then Console.WriteLine($"not found: {opt.MeshShaders}") : Return
            Dim snif As New Nifcontent_Class_Manolo() : snif.Load_Manolo(bytes)
            Console.WriteLine($"=== {opt.MeshShaders}  ({snif.NifShapes.Count()} shapes) ===")
            For Each shp In snif.NifShapes.ToList()
                Dim vd = TryCast(shp, NiflySharp.Blocks.BSTriShape)
                Dim vdesc = If(vd IsNot Nothing, $"VDesc=0x{vd.VertexDesc.Value:X16} type={vd.GetType().Name}", "")
                Dim lsp = TryCast(snif.GetShader(shp), NiflySharp.Blocks.BSLightingShaderProperty)
                Console.WriteLine($"  '{shp.Name?.String}' {If(lsp IsNot Nothing, $"shType={lsp.ShaderType_SK_FO4} SSPF1=0x{CUInt(lsp.ShaderFlags_SSPF1):X8}", "no-shader")}  {vdesc}")
                ' skin bones (para RE de weight/skin del pelo)
                Try
                    Dim sir = shp.SkinInstanceRef
                    If sir IsNot Nothing AndAlso sir.Index >= 0 Then
                        Dim si = TryCast(snif.Blocks(sir.Index), NiflySharp.Blocks.NiSkinInstance)
                        If si IsNot Nothing AndAlso si.Bones IsNot Nothing Then
                            Dim names As New List(Of String)
                            For bi = 0 To si.Bones.References.Count - 1
                                Dim br = si.Bones.GetBlockRef(bi)
                                Dim bn = TryCast(snif.Blocks(br), NiflySharp.Blocks.NiNode)
                                names.Add(If(bn?.Name?.String, "?"))
                            Next
                            Console.WriteLine($"      skinBones[{names.Count}]: {String.Join(", ", names)}")
                        End If
                    End If
                Catch : End Try
            Next
            Return
        End If

        ' --- POSDUMP: vuelca posiciones por-vértice de cada shape de un NIF (file o key BSA) a CSV ---
        If opt.PosDump <> "" Then
            Dim invp = System.Globalization.CultureInfo.InvariantCulture
            Dim parts = opt.PosDump.Split({"|"c}, 2)
            Dim src = parts(0).Trim()
            Dim outc = If(parts.Length > 1, parts(1).Trim(), "posdump.csv")
            Dim bytes As Byte()
            If IO.File.Exists(src) Then
                bytes = IO.File.ReadAllBytes(src)
            Else
                bytes = FilesDictionary_class.GetBytes(src.Replace("/"c, "\"c).ToLowerInvariant())
            End If
            If bytes Is Nothing OrElse bytes.Length = 0 Then Console.WriteLine($"[posdump] no bytes: {src}") : Return
            Dim nif As New Nifcontent_Class_Manolo() : nif.Load_Manolo(bytes)
            ' --nodes: vuelca TODOS los NiNode (nombre + global transform) — para el esqueleto real.
            Dim nodesc = outc & ".nodes.csv"
            Try
                Dim invn = System.Globalization.CultureInfo.InvariantCulture
                Using nw = New IO.StreamWriter(nodesc, False)
                    nw.WriteLine("node,tx,ty,tz,m11,m12,m13,m21,m22,m23,m31,m32,m33,scale")
                    For Each blk In nif.Blocks
                        Dim nn = TryCast(blk, NiflySharp.Blocks.NiNode)
                        If nn Is Nothing OrElse nn.Name Is Nothing Then Continue For
                        Dim nm2 = If(nn.Name.String, "") : If nm2 = "" Then Continue For
                        Dim gt = Transform_Class.GetGlobalTransform(nn, nif)
                        If gt Is Nothing Then Continue For
                        Dim rr = gt.Rotation
                        nw.WriteLine($"{nm2},{gt.Translation.X.ToString("R", invn)},{gt.Translation.Y.ToString("R", invn)},{gt.Translation.Z.ToString("R", invn)}," &
                                     $"{rr.M11.ToString("R", invn)},{rr.M12.ToString("R", invn)},{rr.M13.ToString("R", invn)},{rr.M21.ToString("R", invn)},{rr.M22.ToString("R", invn)},{rr.M23.ToString("R", invn)}," &
                                     $"{rr.M31.ToString("R", invn)},{rr.M32.ToString("R", invn)},{rr.M33.ToString("R", invn)},{gt.Scale.ToString("R", invn)}")
                    Next
                End Using
            Catch : End Try
            Dim skinc = outc & ".skin.csv"
            Dim bonesc = outc & ".bones.csv"
            Dim fmtT = Function(t As Transform_Class) As String
                           Dim r = t.Rotation
                           Return $"{t.Translation.X.ToString("R", invp)},{t.Translation.Y.ToString("R", invp)},{t.Translation.Z.ToString("R", invp)}," &
                                  $"{r.M11.ToString("R", invp)},{r.M12.ToString("R", invp)},{r.M13.ToString("R", invp)}," &
                                  $"{r.M21.ToString("R", invp)},{r.M22.ToString("R", invp)},{r.M23.ToString("R", invp)}," &
                                  $"{r.M31.ToString("R", invp)},{r.M32.ToString("R", invp)},{r.M33.ToString("R", invp)},{t.Scale.ToString("R", invp)}"
                       End Function
            Dim trisc = outc & ".tris.csv"
            Using w = New IO.StreamWriter(outc, False), sw = New IO.StreamWriter(skinc, False), bw = New IO.StreamWriter(bonesc, False), tw = New IO.StreamWriter(trisc, False)
                w.WriteLine("shape,vidx,x,y,z")
                sw.WriteLine("shape,vidx,bone,weight")
                bw.WriteLine("shape,bone,kind,tx,ty,tz,m11,m12,m13,m21,m22,m23,m31,m32,m33,scale")
                tw.WriteLine("shape,v1,v2,v3")
                For Each shp In nif.NifShapes.ToList()
                    Try
                        Dim gt2 = ShapeGeometryFactory.[For](shp, nif).GetTriangles()
                        If gt2 IsNot Nothing Then
                            Dim snm = If(shp.Name?.String, "")
                            For Each tri In gt2 : tw.WriteLine($"{snm},{tri.V1},{tri.V2},{tri.V3}") : Next
                        End If
                    Catch : End Try
                    ' --- bones: bind (skin-to-bone) + node global transform por hueso ---
                    Try
                        Dim rs As New NifRenderableShape(nif, shp, 0)
                        Dim bns = rs.ShapeBones.ToArray()
                        Dim bts = rs.ShapeBoneTransforms.ToArray()
                        For bi = 0 To bns.Length - 1
                            Dim bnm = If(bns(bi)?.Name?.String, $"#{bi}")
                            If bi < bts.Length AndAlso bts(bi) IsNot Nothing Then bw.WriteLine($"{If(shp.Name?.String, "")},{bnm},bind,{fmtT(bts(bi))}")
                            Dim gw = Transform_Class.GetGlobalTransform(bns(bi), nif)
                            If gw IsNot Nothing Then bw.WriteLine($"{If(shp.Name?.String, "")},{bnm},nodeworld,{fmtT(gw)}")
                        Next
                    Catch : End Try
                    Dim nm = If(shp.Name?.String, "")
                    Dim g = ShapeGeometryFactory.[For](shp, nif)
                    Dim vp = g.GetVertexPositions()
                    If vp Is Nothing Then Continue For
                    For i = 0 To vp.Count - 1
                        w.WriteLine($"{nm},{i},{vp(i).X.ToString("R", invp)},{vp(i).Y.ToString("R", invp)},{vp(i).Z.ToString("R", invp)}")
                    Next
                    ' --- skin: nombre de hueso + peso por vértice (slots no-cero) ---
                    Try
                        Dim sk = g.GetSkinning()
                        If sk.BoneWeights IsNot Nothing AndAlso sk.WeightsPerVertex > 0 AndAlso sk.BoneRefIndices IsNot Nothing Then
                            Dim wpv = sk.WeightsPerVertex
                            Dim names(sk.BoneRefIndices.Length - 1) As String
                            For p = 0 To sk.BoneRefIndices.Length - 1
                                Dim bn = TryCast(nif.Blocks(sk.BoneRefIndices(p)), NiflySharp.Blocks.NiNode)
                                names(p) = If(bn?.Name?.String, $"#{sk.BoneRefIndices(p)}")
                            Next
                            For i = 0 To vp.Count - 1
                                For k = 0 To wpv - 1
                                    Dim idx = i * wpv + k
                                    Dim wt = CSng(sk.BoneWeights(idx))
                                    If wt <= 0.0001F Then Continue For
                                    Dim pal = sk.BoneIndices(idx)
                                    Dim bnm = If(pal < names.Length, names(pal), $"pal{pal}")
                                    sw.WriteLine($"{nm},{i},{bnm},{wt.ToString("R", invp)}")
                                Next
                            Next
                        End If
                    Catch : End Try
                Next
            End Using
            Console.WriteLine($"[posdump] {nif.NifShapes.Count()} shapes -> {outc} (+ {skinc})")
            Return
        End If

        ' --- BATCH 100%: bakea+compara TODOS los NPC_ vanilla con FaceGeom vs CK, agrega diffs por categoría ---
        If opt.SseCompareBatch Then
            FO4_NPC_Manager.FaceGenBuilder.WriteGPUSandboxOutput = False
            ' Compose de DDS ENCENDIDO: medido, NO era el cuello de botella (el logger sí lo era). Y apagarlo
            ' altera el NIF — falta un BSShaderTextureSet (la cara no recibe su set propio y se deduplica),
            ' además de dejar sin escribir el slot 6 y el fold del slot 0. Con el logger apagado el barrido
            ' tarda minutos igual, así que se hornea completo y válido.
            FO4_NPC_Manager.FaceGenBuilder.BakeFaceTexturesEnabled = True
            ' Logger APAGADO a propósito. Antes se prendía sólo para que DebugMode(=Logger.Enabled) diera el
            ' sufijo sandbox "_2", por miedo a pisar la referencia del CK. Ese miedo era infundado:
            '   (a) la referencia sale del BA2/BSA — un loose nuestro NO puede pisar el contenido del archivo;
            '   (b) el FilesDictionary se arma AL MONTAR (snapshot), así que los loose escritos DURANTE la
            '       corrida no entran en él y no contaminan las lecturas de la ref en esa misma corrida.
            ' Único cuidado REAL: contaminación ENTRE corridas (los loose de la corrida N estarían en el
            ' snapshot de la N+1 y ahí sí taparían al BA2) ⇒ al terminar hay que MOVER el árbol de FaceGeom
            ' generado a su carpeta (asis/ | nuevo/) y dejar Data limpio. Sin logger el barrido es órdenes de
            ' magnitud más rápido (el log arma strings por shape/slot/decisión en todo el pipeline).
            Logger.Enabled = False
            ' --headfidelity: además de comparar contra el CK, mide preview-vs-juego por shape (ETAPA 1
            ' del diagnóstico de fidelidad). Sólo mide; no cambia lo que el bake escribe.
            FO4_NPC_Manager.FaceGenBuildPipeline.HeadFidelityEnabled = opt.HeadFidelity
            RunSseCompareBatch(pm, opt.SseCompareBatchLimit)
            If opt.HeadFidelity Then ReportHeadFidelity()
            Return
        End If

        ' --- VERTEXBATCH: game-aware; bakea todos los vanilla con FaceGeom y reporta max vertex diff + outliers (posición) ---
        If opt.VertexBatch Then
            FO4_NPC_Manager.FaceGenBuilder.WriteGPUSandboxOutput = False
            Logger.Enabled = True
            RunVertexOutlierBatch(pm, opt.VertexBatchLimit, opt.VertexBatchOut)
            Return
        End If

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

        ' --- ALPHAGATESCAN: dimensiona los DOS gates del alpha de la cabeza antes de tocarlos.
        If opt.AlphaGateScan Then
            AlphaGateScan(pm)
            Return
        End If

        ' --shapeorder: ¿NUESTRO orden de emisión coincide con el del CK? Compara, por NPC, la SECUENCIA de
        ' nombres de shape del NIF horneado por el CK (leído del BSA, no de un suelto) contra el orden que
        ' produce el sort del bake: OrderBy(PartType).ThenBy(EditorID) — ver FaceGenBuilder.vb, función BuildCharGen.
        ' No hornea nada: el orden nuestro se computa de los records. Responde dos cosas de una:
        '   (a) si el sort primario por PartType reproduce al CK sobre TODO el corpus (medido en 1 NPC no vale)
        '   (b) dónde el desempate DENTRO de un mismo PartType difiere — que es el hueco que decide el fix.
        If opt.ShapeOrder Then
            Dim tot = 0, same = 0, diffType = 0, diffWithin = 0, noRef = 0, chainMatch = 0
            Dim hyp As New Dictionary(Of String, Integer)
            Dim sigToSeq As New Dictionary(Of String, String)
            Dim sigAgree = 0, sigConflict = 0, dumped = 0
            Dim monoA As New Dictionary(Of String, Integer)
            Dim monoD As New Dictionary(Of String, Integer)
            Dim ejemplos As New List(Of String)
            For Each kv In pm.AllRecords
                Dim rc = kv.Value
                If rc Is Nothing OrElse rc.Header.Signature <> "NPC_" Then Continue For
                Dim origin = pm.GetOriginatingPluginName(kv.Key)
                If Not PluginManager.IsOfficialPlugin(origin) Then Continue For
                Dim fgL = PluginManager.ToFaceGenLocalFormID(kv.Key)
                Dim ckKey = ($"meshes\actors\character\facegendata\facegeom\{origin}\{fgL:X8}.nif").ToLowerInvariant()
                Dim ckb = FilesDictionary_class.GetArchiveOriginalBytes(ckKey)
                If ckb Is Nothing OrElse ckb.Length = 0 Then Continue For
                Dim npc = RecordParsers.ParseNPC(rc, pm)
                If npc Is Nothing Then Continue For
                Dim ckNames As New List(Of String)
                Try
                    Dim n As New Nifcontent_Class_Manolo() : n.Load_Manolo(ckb)
                    For Each sh In n.GetShapes() : ckNames.Add(If(sh.Name?.String, "")) : Next
                Catch : noRef += 1 : Continue For
                End Try
                ' NUESTRO orden, de records: misma cadena que BuildAllowedShapeMap + el sort de :478.
                Dim roots = FO4_NPC_Manager.HeadPartResolver.MergeHeadPartsWithRaceDefaults(npc.Record.Race, npc.Record.ConfigurationFlagsFemale, npc.Record.PartesDeCabeza(), pm)
                Dim mine As New List(Of Tuple(Of Integer, String))
                Dim mineFid As New List(Of Tuple(Of UInteger, Integer, String))
                Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                For Each e In FO4_NPC_Manager.HeadPartResolver.EnumerateHdptChain(roots, pm)
                    Dim eid = If(e.Hdpt.EditorID, "")
                    If eid = "" OrElse Not seen.Add(eid) Then Continue For
                    mine.Add(Tuple.Create(CInt(e.Hdpt.TipoDeParte()), CStr(eid)))
                    mineFid.Add(Tuple.Create(e.Hdpt.FormID, CInt(e.Hdpt.TipoDeParte()), CStr(eid)))
                Next
                Dim mineSorted = mine.OrderBy(Function(z) z.Item1).ThenBy(Function(z) z.Item2).Select(Function(z) z.Item2).ToList()
                ' HIPOTESIS B: el orden del motor = la lista del NPC en orden, con las extra parts expandidas
                ' inmediatamente despues de su padre (depth-first). Es lo que hace EnumerateHdptChain SIN sort.
                Dim mineChain = mine.Select(Function(z) z.Item2).ToList()
                ' Sólo se comparan los nombres presentes en AMBOS lados (el CK omite shapes que no hornea).
                Dim ckFiltered = ckNames.Where(Function(z) mineSorted.Contains(z, StringComparer.OrdinalIgnoreCase)).ToList()
                Dim mineFiltered = mineSorted.Where(Function(z) ckFiltered.Contains(z, StringComparer.OrdinalIgnoreCase)).ToList()
                If ckFiltered.Count < 2 Then Continue For
                tot += 1
                ' BANCO DE HIPOTESIS: en vez de probar una por corrida, se evaluan TODAS contra la
                ' secuencia real del CK. La que gane es la regla; si ninguna gana, la regla no es un orden
                ' derivable de los records y hay que buscarla en el binario.
                Dim cand As New Dictionary(Of String, List(Of String))
                cand("1 PartType,EditorID (ACTUAL)") = mine.OrderBy(Function(z) z.Item1).ThenBy(Function(z) z.Item2).Select(Function(z) z.Item2).ToList()
                cand("2 cadena PNAM+extras (chain)") = mine.Select(Function(z) z.Item2).ToList()
                cand("3 chain INVERTIDA") = Enumerable.Reverse(mine.Select(Function(z) z.Item2).ToList()).ToList()
                cand("4 EditorID solo") = mine.OrderBy(Function(z) z.Item2).Select(Function(z) z.Item2).ToList()
                cand("5 PartType,chain") = mine.Select(Function(z, ix) Tuple.Create(z.Item1, ix, z.Item2)).OrderBy(Function(z) z.Item1).ThenBy(Function(z) z.Item2).Select(Function(z) z.Item3).ToList()
                ' 11: POST-ORDEN — por cada raiz: primero sus extras (recursivo), despues ella misma.
                Dim h11 As New List(Of String)
                Dim visited As New HashSet(Of UInteger)
                Dim emit As Action(Of UInteger) = Nothing
                emit = Sub(fid As UInteger)
                           If fid = 0UI OrElse Not visited.Add(fid) Then Return
                           Dim hr2 = pm.GetRecord(fid)
                           If hr2 Is Nothing OrElse hr2.Header.Signature <> "HDPT" Then Return
                           Dim hd2 = Canon.CanonRecords.Hdpt(hr2, pm)
                           If hd2 Is Nothing Then Return
                           If hd2.PartesExtra() IsNot Nothing Then
                               For Each ex In hd2.PartesExtra() : emit(ex) : Next
                           End If
                           If Not String.IsNullOrEmpty(hd2.EditorID) Then h11.Add(hd2.EditorID)
                       End Sub
                For Each rt In roots : emit(rt) : Next
                cand("11 POST-ORDER (extras BEFORE the parent)") = h11
                ' 12: post-orden pero con las raices ordenadas por PartType
                Dim h12 As New List(Of String)
                Dim visited2 As New HashSet(Of UInteger)
                Dim emit2 As Action(Of UInteger) = Nothing
                emit2 = Sub(fid As UInteger)
                            If fid = 0UI OrElse Not visited2.Add(fid) Then Return
                            Dim hr3 = pm.GetRecord(fid)
                            If hr3 Is Nothing OrElse hr3.Header.Signature <> "HDPT" Then Return
                            Dim hd3 = Canon.CanonRecords.Hdpt(hr3, pm)
                            If hd3 Is Nothing Then Return
                            If hd3.PartesExtra() IsNot Nothing Then
                                For Each ex In hd3.PartesExtra() : emit2(ex) : Next
                            End If
                            If Not String.IsNullOrEmpty(hd3.EditorID) Then h12.Add(hd3.EditorID)
                        End Sub
                Dim rootsByType = roots.Select(Function(rf)
                                                   Dim rr2 = pm.GetRecord(rf)
                                                   Dim pt2 = 99
                                                   If rr2 IsNot Nothing AndAlso rr2.Header.Signature = "HDPT" Then
                                                       Dim hh = Canon.CanonRecords.Hdpt(rr2, pm)
                                                       If hh IsNot Nothing Then pt2 = hh.TipoDeParte()
                                                   End If
                                                   Return Tuple.Create(pt2, rf)
                                               End Function).OrderBy(Function(z) z.Item1).Select(Function(z) z.Item2).ToList()
                For Each rt In rootsByType : emit2(rt) : Next
                cand("12 POST-ORDER + roots by PartType") = h12
                Dim rootsByTypeL = rootsByType
                ' 13/14: POST-ORDEN con las extras en orden INVERSO al HNAM (comportamiento de PILA), que es
                ' lo que muestra la data: MaleDremoraHair01 declara HNAM=[HairHorns,HairLine] y el CK emite
                ' [HairLine,HairHorns,Hair]; el otro Dremora declara [HairLine,HairHorns] y emite [HairHorns,HairLine,Hair].
                Dim buildRev = Function(rootSeq As List(Of UInteger)) As List(Of String)
                                   Dim outL As New List(Of String)
                                   Dim vis As New HashSet(Of UInteger)
                                   Dim rec As Action(Of UInteger) = Nothing
                                   rec = Sub(fid As UInteger)
                                             If fid = 0UI OrElse Not vis.Add(fid) Then Return
                                             Dim r4 = pm.GetRecord(fid)
                                             If r4 Is Nothing OrElse r4.Header.Signature <> "HDPT" Then Return
                                             Dim h4 = Canon.CanonRecords.Hdpt(r4, pm)
                                             If h4 Is Nothing Then Return
                                             If h4.PartesExtra() IsNot Nothing Then
                                                 For Each ex In Enumerable.Reverse(h4.PartesExtra()) : rec(ex) : Next
                                             End If
                                             If Not String.IsNullOrEmpty(h4.EditorID) Then outL.Add(h4.EditorID)
                                         End Sub
                                   For Each rt In rootSeq : rec(rt) : Next
                                   Return outL
                               End Function
                cand("13 POST-ORDER + extras REVERSED") = buildRev(roots.ToList())
                cand("14 POST-ORD+extras INV, roots x type") = buildRev(rootsByType)
                ' 15: ORDEN DE LA RACE. El CK recorre la lista de head parts de la RAZA en su orden, y en cada
                ' posicion usa el override del NPC del MISMO PartType si existe. Los head parts del NPC que no
                ' corresponden a ninguna posicion de la raza se agregan al final. Extras en post-orden.
                Dim raceRec2 = pm.GetRecord(npc.Record.Race)
                Dim raceD = If(raceRec2 Is Nothing, Nothing, Canon.CanonRecords.Race(raceRec2, pm))
                If raceD IsNot Nothing Then
                    Dim raceList = raceD.HeadPartsDe(npc.Record.ConfigurationFlagsFemale)
                    Dim typeOfFid = Function(f As UInteger) As Integer
                                        Dim rr3 = pm.GetRecord(f)
                                        If rr3 Is Nothing OrElse rr3.Header.Signature <> "HDPT" Then Return 99
                                        Dim hh3 = Canon.CanonRecords.Hdpt(rr3, pm)
                                        Return If(hh3 Is Nothing, 99, hh3.TipoDeParte())
                                    End Function
                    Dim npcByType As New Dictionary(Of Integer, UInteger)
                    For Each rf In If(npc.Record.PartesDeCabeza(), New List(Of UInteger)())
                        Dim t3 = typeOfFid(rf)
                        If Not npcByType.ContainsKey(t3) Then npcByType(t3) = rf
                    Next
                    Dim ordered As New List(Of UInteger)
                    Dim used As New HashSet(Of UInteger)
                    For Each rf In If(raceList, New List(Of UInteger)())
                        Dim t3 = typeOfFid(rf)
                        Dim pick As UInteger = rf
                        Dim ov As UInteger = 0UI
                        If npcByType.TryGetValue(t3, ov) Then pick = ov
                        If used.Add(pick) Then ordered.Add(pick)
                    Next
                    For Each rf In If(npc.Record.PartesDeCabeza(), New List(Of UInteger)())
                        If used.Add(rf) Then ordered.Add(rf)
                    Next
                    cand("15 RACE order + override x type") = buildRev(ordered)
                End If
                ' 16: defaults de la RAZA primero (en orden de la raza, salvo los que el NPC pisa por tipo),
                ' y despues el PNAM del NPC en orden INVERSO. Extras en post-orden invertido.
                If raceD IsNot Nothing Then
                    Dim raceList2 = raceD.HeadPartsDe(npc.Record.ConfigurationFlagsFemale)
                    Dim tOfFid = Function(f As UInteger) As Integer
                                     Dim rr5 = pm.GetRecord(f)
                                     If rr5 Is Nothing OrElse rr5.Header.Signature <> "HDPT" Then Return 99
                                     Dim hh5 = Canon.CanonRecords.Hdpt(rr5, pm)
                                     Return If(hh5 Is Nothing, 99, hh5.TipoDeParte())
                                 End Function
                    Dim pnam = If(npc.Record.PartesDeCabeza(), New List(Of UInteger)())
                    Dim npcTypes As New HashSet(Of Integer)(pnam.Select(Function(f) tOfFid(f)))
                    Dim ord2 As New List(Of UInteger)
                    For Each rf In If(raceList2, New List(Of UInteger)())
                        If Not npcTypes.Contains(tOfFid(rf)) Then ord2.Add(rf)
                    Next
                    ord2.AddRange(Enumerable.Reverse(pnam))
                    cand("16 RACE defaults + PNAM INVERSO") = buildRev(ord2)
                End If
                ' 17-20: claves INTRINSECAS al head part que faltaba probar. El dato que las motiva: mismo
                ' conjunto -> misma secuencia en 705/762, o sea el orden ES funcion del conjunto (sale de los
                ' records), sólo que no de PartType/EditorID/FormID/PNAM.
                Dim meshOf As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                For Each z In mineFid
                    Dim r6 = pm.GetRecord(z.Item1)
                    If r6 IsNot Nothing AndAlso r6.Header.Signature = "HDPT" Then
                        Dim h6 = Canon.CanonRecords.Hdpt(r6, pm)
                        If h6 IsNot Nothing Then meshOf(z.Item3) = If(h6.ModelFileName, "")
                    End If
                Next
                Dim allN = mineFid.Select(Function(z) z.Item3).ToList()
                cand("17 MeshPath asc") = allN.OrderBy(Function(z) If(meshOf.ContainsKey(z), meshOf(z), "")).ToList()
                cand("18 MeshPath desc") = allN.OrderByDescending(Function(z) If(meshOf.ContainsKey(z), meshOf(z), "")).ToList()
                cand("19 PartType,MeshPath") = mineFid.OrderBy(Function(z) z.Item2).ThenBy(Function(z) If(meshOf.ContainsKey(z.Item3), meshOf(z.Item3), "")).Select(Function(z) z.Item3).ToList()
                cand("20 mesh name (no dir)") = allN.OrderBy(Function(z) IO.Path.GetFileName(If(meshOf.ContainsKey(z), meshOf(z), ""))).ToList()
                ' 21-24: HASH DEL PATH (idea del usuario). Un contenedor indexado por hash explica los TRES
                ' sintomas: orden estable para el mismo conjunto (705/762), CERO monotonia en todo atributo
                ' (<16%), y el fracaso de cualquier orden derivado de campos. Se usa el MISMO hash TES4 que
                ' la BSA de Skyrim (Ba2_Bsa_Library.BSAWriter), no uno inventado.
                cand("21 TES4 hash of the mesh asc") = allN.OrderBy(Function(z) Tes4HashOf(If(meshOf.ContainsKey(z), meshOf(z), ""))).ToList()
                cand("22 TES4 hash of the mesh desc") = allN.OrderByDescending(Function(z) Tes4HashOf(If(meshOf.ContainsKey(z), meshOf(z), ""))).ToList()
                cand("23 PartType,hash TES4") = mineFid.OrderBy(Function(z) z.Item2).ThenBy(Function(z) Tes4HashOf(If(meshOf.ContainsKey(z.Item3), meshOf(z.Item3), ""))).Select(Function(z) z.Item3).ToList()
                cand("24 TES4 hash of the EditorID") = allN.OrderBy(Function(z) Tes4HashOf(z)).ToList()
                ' 25+: BUCKET = hash mod capacidad. Si el CK recorre un hash map, el orden es por bucket,
                ' no por el valor del hash. Se prueban capacidades tipicas; el desempate dentro de un bucket
                ' es el orden de insercion (chaining), que aproximamos con el orden de la cadena.
                For Each capN In New Integer() {8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096}
                    Dim cc = capN
                    cand($"25 bucket hash mod {cc}") = mineFid.Select(Function(z, ixx) Tuple.Create(CInt(Tes4HashOf(If(meshOf.ContainsKey(z.Item3), meshOf(z.Item3), "")) Mod CULng(cc)), ixx, z.Item3)).
                                                              OrderBy(Function(z) z.Item1).ThenBy(Function(z) z.Item2).Select(Function(z) z.Item3).ToList()
                Next
                cand("7 FormID asc") = mineFid.OrderBy(Function(z) z.Item1).Select(Function(z) z.Item3).ToList()
                cand("8 PartType,FormID asc") = mineFid.OrderBy(Function(z) z.Item2).ThenBy(Function(z) z.Item1).Select(Function(z) z.Item3).ToList()
                cand("9 FormID desc") = mineFid.OrderByDescending(Function(z) z.Item1).Select(Function(z) z.Item3).ToList()
                ' 10: PNAM CRUDO (sin merge ni extras) primero, extras despues en orden de aparicion
                Dim rawRoots = If(npc.Record.PartesDeCabeza(), New List(Of UInteger)())
                Dim fidToName = mineFid.GroupBy(Function(z) z.Item1).ToDictionary(Function(g) g.Key, Function(g) g.First().Item3)
                Dim h10 As New List(Of String)
                For Each rf In rawRoots
                    Dim nm As String = Nothing
                    If fidToName.TryGetValue(rf, nm) Then h10.Add(nm)
                Next
                For Each z In mineFid
                    If Not h10.Contains(z.Item3, StringComparer.OrdinalIgnoreCase) Then h10.Add(z.Item3)
                Next
                cand("10 PNAM crudo + extras al final") = h10
                cand("6 PartType DESC,chain") = mine.Select(Function(z, ix) Tuple.Create(z.Item1, ix, z.Item2)).OrderByDescending(Function(z) z.Item1).ThenBy(Function(z) z.Item2).Select(Function(z) z.Item3).ToList()
                For Each hk In cand.Keys.ToList()
                    Dim seq = cand(hk).Where(Function(z) ckFiltered.Contains(z, StringComparer.OrdinalIgnoreCase)).ToList()
                    If ckFiltered.SequenceEqual(seq, StringComparer.OrdinalIgnoreCase) Then
                        hyp(hk) = If(hyp.ContainsKey(hk), hyp(hk), 0) + 1
                    End If
                Next
                ' ¿el orden del CK es GLOBAL o POR NPC? misma firma de head parts -> ¿misma secuencia?
                If dumped < 5 AndAlso ckFiltered.Count >= 5 Then
                    dumped += 1
                    Dim tmap = mineFid.GroupBy(Function(z) z.Item3, StringComparer.OrdinalIgnoreCase).ToDictionary(Function(g) g.Key, Function(g) g.First(), StringComparer.OrdinalIgnoreCase)
                    Console.WriteLine($"--- 0x{kv.Key:X8} {npc.EditorID} ---")
                    Console.WriteLine("   CK    : " & String.Join("  ", ckFiltered.Select(Function(z) $"{z}(t{tmap(z).Item2},0x{tmap(z).Item1:X8})")))
                    Console.WriteLine("   chain : " & String.Join("  ", mineChain.Where(Function(z) tmap.ContainsKey(z)).Select(Function(z) $"{z}(t{tmap(z).Item2})")))
                    Console.WriteLine("   PNAM  : " & String.Join("  ", If(npc.Record.PartesDeCabeza(), New List(Of UInteger)()).Select(Function(z) $"0x{z:X8}")))
                End If
                ' ANALISIS INVERSO: ¿que atributo es MONOTONO a lo largo de la secuencia REAL del CK?
                ' No propone ordenes: mide, sobre la secuencia que el CK realmente produjo, si cada atributo
                ' viene no-decreciente. El atributo que sea monotono en (casi) todos los NPC es la clave.
                Dim attrOf As New Dictionary(Of String, Func(Of String, IComparable))(StringComparer.OrdinalIgnoreCase)
                Dim tmap2 = mineFid.GroupBy(Function(z) z.Item3, StringComparer.OrdinalIgnoreCase).ToDictionary(Function(g) g.Key, Function(g) g.First(), StringComparer.OrdinalIgnoreCase)
                Dim meshOf2 As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                Dim flagOf As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
                For Each z In mineFid
                    Dim r7 = pm.GetRecord(z.Item1)
                    If r7 IsNot Nothing AndAlso r7.Header.Signature = "HDPT" Then
                        Dim h7 = Canon.CanonRecords.Hdpt(r7, pm)
                        If h7 IsNot Nothing Then
                            meshOf2(z.Item3) = If(h7.ModelFileName, "")
                            flagOf(z.Item3) = h7.Flags
                        End If
                    End If
                Next
                For Each nm2 In New String() {"FormID", "PartType", "EditorID", "MeshPath", "Flags"}
                    Dim vals As New List(Of IComparable)
                    Dim ok2 = True
                    For Each cn In ckFiltered
                        If Not tmap2.ContainsKey(cn) Then ok2 = False : Exit For
                        Select Case nm2
                            Case "FormID" : vals.Add(tmap2(cn).Item1)
                            Case "PartType" : vals.Add(tmap2(cn).Item2)
                            Case "EditorID" : vals.Add(cn)
                            Case "MeshPath" : vals.Add(If(meshOf2.ContainsKey(cn), meshOf2(cn), ""))
                            Case "Flags" : vals.Add(If(flagOf.ContainsKey(cn), flagOf(cn), 0))
                        End Select
                    Next
                    If Not ok2 Then Continue For
                    Dim monoAsc = True, monoDesc = True
                    For q = 1 To vals.Count - 1
                        If vals(q).CompareTo(vals(q - 1)) < 0 Then monoAsc = False
                        If vals(q).CompareTo(vals(q - 1)) > 0 Then monoDesc = False
                    Next
                    If monoAsc Then monoA(nm2) = If(monoA.ContainsKey(nm2), monoA(nm2), 0) + 1
                    If monoDesc Then monoD(nm2) = If(monoD.ContainsKey(nm2), monoD(nm2), 0) + 1
                Next
                Dim sig = String.Join("|", ckFiltered.OrderBy(Function(z) z))
                Dim seqKey = String.Join("|", ckFiltered)
                If sigToSeq.ContainsKey(sig) Then
                    If sigToSeq(sig) <> seqKey Then sigConflict += 1 Else sigAgree += 1
                Else
                    sigToSeq(sig) = seqKey
                End If
                Dim chainFiltered = mineChain.Where(Function(z) ckFiltered.Contains(z, StringComparer.OrdinalIgnoreCase)).ToList()
                If ckFiltered.SequenceEqual(chainFiltered, StringComparer.OrdinalIgnoreCase) Then chainMatch += 1
                If ckFiltered.SequenceEqual(mineFiltered, StringComparer.OrdinalIgnoreCase) Then
                    same += 1
                Else
                    ' ¿la discrepancia es de TIPO o dentro del mismo tipo?
                    Dim tOf = mine.ToDictionary(Function(z) z.Item2, Function(z) z.Item1, StringComparer.OrdinalIgnoreCase)
                    Dim ckTypes = ckFiltered.Select(Function(z) tOf(z)).ToList()
                    If ckTypes.SequenceEqual(ckTypes.OrderBy(Function(z) z)) Then
                        diffWithin += 1
                        If ejemplos.Count < 6 Then ejemplos.Add($"[within-type] 0x{kv.Key:X8} {npc.EditorID}: CK={String.Join(",", ckFiltered)} | OURS={String.Join(",", mineFiltered)}")
                    Else
                        diffType += 1
                        If ejemplos.Count < 6 Then ejemplos.Add($"[TYPE] 0x{kv.Key:X8} {npc.EditorID}: CK={String.Join(",", ckFiltered)} types={String.Join(",", ckTypes)}")
                    End If
                End If
            Next
            Console.WriteLine($"[shapeorder] comparable NPCs: {tot}")
            Console.WriteLine($"[shapeorder]   order IDENTICAL to the CK   : {same} ({100.0 * same / Math.Max(1, tot):F2}%)")
            Console.WriteLine($"[shapeorder]   differs WITHIN a type       : {diffWithin}")
            Console.WriteLine($"[shapeorder]   the CK does NOT sort by PartType: {diffType}   <- if >0, the primary sort is WRONG")
            Console.WriteLine($"[shapeorder] HYPOTHESIS B (PNAM chain + extras depth-first, NO sort): {chainMatch} ({100.0 * chainMatch / Math.Max(1, tot):F2}%)")
            Console.WriteLine("[shapeorder] === MONOTONICITY over the CK's REAL sequence ===")
            For Each k In New String() {"FormID", "PartType", "EditorID", "MeshPath", "Flags"}
                Dim a = If(monoA.ContainsKey(k), monoA(k), 0)
                Dim d = If(monoD.ContainsKey(k), monoD(k), 0)
                Console.WriteLine($"   {k,-10} ascendente {a,6} ({100.0 * a / Math.Max(1, tot):F1}%)   descendente {d,6} ({100.0 * d / Math.Max(1, tot):F1}%)")
            Next
            Console.WriteLine("[shapeorder] === HYPOTHESIS BENCH (exact sequence match) ===")
            For Each h In hyp.OrderByDescending(Function(z) z.Value)
                Console.WriteLine($"   {h.Key,-34} {h.Value,6}  ({100.0 * h.Value / Math.Max(1, tot):F2}%)")
            Next
            Console.WriteLine($"[shapeorder] === GLOBAL vs PER-NPC ===")
            Console.WriteLine($"   same head-part set -> SAME sequence: {sigAgree}   DIFFERENT: {sigConflict}")
            Console.WriteLine("   (conflicts>0 ⇒ the order is NOT a function of the set: it depends on the NPC)")
            For Each e In ejemplos : Console.WriteLine("   " & e) : Next
            Return
        End If

        ' --dumpacc "<formID>|<out>": vuelca el ACUMULADOR del facetint SSE en float64 crudo (RGBA por píxel,
        ' [0,1] lineal), ANTES de cuantizar a byte. Es el insumo para DERIVAR la regla de redondeo del CK sin
        ' adivinarla: pareando este float con el byte del TGA del CK (lossless) hay ~786k muestras por NPC de
        ' (valor exacto -> byte que el CK eligió). Comparar bytes contra bytes NO sirve: sólo da el delta.
        If opt.DumpAcc <> "" Then
            Dim pp = opt.DumpAcc.Split("|"c)
            Dim fid = Convert.ToUInt32(pp(0).Replace("0x", ""), 16)
            Dim rc2 = pm.GetRecord(fid)
            Dim npc2 = RecordParsers.ParseNPC(rc2, pm)
            Dim race2 = Canon.CanonRecords.Race(pm.GetRecord(npc2.Record.Race), pm)
            Dim acc = SseFaceGenBaker.ComposeFacetintAcc(pm, rc2, race2, npc2.Record.Race, npc2.Record.ConfigurationFlagsFemale, 512, 512)
            If acc Is Nothing Then Console.Error.WriteLine("[dumpacc] compose returned Nothing") : Environment.ExitCode = 2 : Return
            Using fs = IO.File.Create(pp(1).Trim())
                Using bw As New IO.BinaryWriter(fs)
                    For Each d In acc : bw.Write(d) : Next
                End Using
            End Using
            Console.WriteLine($"[dumpacc] 0x{fid:X8} -> {pp(1).Trim()}  ({acc.Length} doubles = {512 * 512} px RGBA)")
            Return
        End If

        ' --texslotdiff: lista EXACTA de (NPC, shape, slot) donde nuestro NIF horneado difiere del NIF del CK
        ' en un path de textura. Existe porque la categoría del barrido da el CONTEO pero no los FormIDs (la
        ' salida por NPC va a TextWriter.Null), y sin los FormIDs se investiga a ciegas: probé 3 NPCs elegidos
        ' por heurística de nombre y los 3 coincidían con el CK. Compara el NIF SUELTO (nuestro bake) contra
        ' GetArchiveOriginalBytes (el del CK, sólo del BSA).
        If opt.TexSlotDiff Then
            Dim npcs = 0, withDiff = 0, shapesDiff = 0, ovr = 0, noOvr = 0
            Dim ckBug = 0, ckOtro = 0, nuestro = 0, ninguno = 0
            Dim porSlot As New Dictionary(Of Integer, Integer)
            Dim ejem As New List(Of String)
            For Each kv In pm.AllRecords
                Dim rc = kv.Value
                If rc Is Nothing OrElse rc.Header.Signature <> "NPC_" Then Continue For
                Dim origin = pm.GetOriginatingPluginName(kv.Key)
                If Not PluginManager.IsOfficialPlugin(origin) Then Continue For
                Dim fgL = PluginManager.ToFaceGenLocalFormID(kv.Key)
                Dim key = ($"meshes\actors\character\facegendata\facegeom\{origin}\{fgL:X8}.nif").ToLowerInvariant()
                Dim ckb = FilesDictionary_class.GetArchiveOriginalBytes(key)
                If ckb Is Nothing OrElse ckb.Length = 0 Then Continue For
                Dim minePath = IO.Path.Combine(Config_App.Current.DataPath, key.Replace("/"c, "\"c))
                If Not IO.File.Exists(minePath) Then Continue For
                npcs += 1
                Dim ckN As New Nifcontent_Class_Manolo(), myN As New Nifcontent_Class_Manolo()
                Try
                    ckN.Load_Manolo(ckb) : myN.Load_Manolo(IO.File.ReadAllBytes(minePath))
                Catch : Continue For
                End Try
                Dim slotsOf = Function(nif As Nifcontent_Class_Manolo, sh As NiflySharp.INiShape) As List(Of String)
                                  Dim outL As New List(Of String)
                                  Dim ts = GetTexSet(nif, sh)
                                  If ts IsNot Nothing AndAlso ts.Textures IsNot Nothing Then
                                      For q = 0 To ts.Textures.Count - 1
                                          outL.Add(If(ts.Textures(q)?.Content, ""))
                                      Next
                                  End If
                                  Return outL
                              End Function
                Dim myMap = myN.GetShapes().GroupBy(Function(sh) If(sh.Name?.String, ""), StringComparer.OrdinalIgnoreCase).
                                            ToDictionary(Function(g) g.Key, Function(g) g.First(), StringComparer.OrdinalIgnoreCase)
                Dim any = False
                For Each cs In ckN.GetShapes()
                    Dim nm = If(cs.Name?.String, "")
                    Dim ms As NiflySharp.INiShape = Nothing
                    If Not myMap.TryGetValue(nm, ms) Then Continue For
                    Dim a = slotsOf(ckN, cs), b = slotsOf(myN, ms)
                    For q = 0 To Math.Max(a.Count, b.Count) - 1
                        Dim va = If(q < a.Count, a(q), ""), vb = If(q < b.Count, b(q), "")
                        If Not String.Equals(va.Replace("/"c, "\"c), vb.Replace("/"c, "\"c), StringComparison.OrdinalIgnoreCase) Then
                            any = True : shapesDiff += 1
                            porSlot(q) = If(porSlot.ContainsKey(q), porSlot(q), 0) + 1
                            ' ¿La diferencia la explica un OVERRIDE de DLC? El FaceGeom del CK esta shipeado
                            ' en el BSA del plugin que ORIGINA al NPC y se horneo con el load order de ESE
                            ' momento. Si el HDPT que da nombre a la shape, o su TXST, los gana un plugin
                            ' POSTERIOR, el CK no pudo verlo: su NIF quedo con el valor viejo y el nuestro
                            ' resuelve el actual. No es defecto nuestro: es una REFERENCIA OBSOLETA.
                            ' CLASIFICACION COMPLETA de cada (shape,slot) divergente. Criterio explicito:
                            '   esperado = el TNAM del HDPT cuyo EditorID ES el nombre de la shape (TX00=D, TX01=N).
                            '   - NUESTRO == esperado  -> somos FIELES AL RECORD
                            '   - CK      == esperado  -> el CK tambien es fiel (entonces la culpa es nuestra)
                            '   - CK != esperado y CK == el TNAM de OTRO HDPT que comparte la MISMA malla
                            '                          -> el CK PISO la textura entre hermanas (bug del CK)
                            '   - si el record lo gana un plugin POSTERIOR al que shippea el FaceGeom
                            '                          -> referencia OBSOLETA (se horneo antes del override)
                            Dim culpa = "no override"
                            Dim hpRec = pm.AllRecords.Where(Function(z) z.Value IsNot Nothing AndAlso
                                                                        z.Value.Header.Signature = "HDPT").
                                                      Select(Function(z) z.Value).
                                                      FirstOrDefault(Function(z)
                                                                         Dim hh = Canon.CanonRecords.Hdpt(z, pm)
                                                                         Return hh IsNot Nothing AndAlso String.Equals(hh.EditorID, nm, StringComparison.OrdinalIgnoreCase)
                                                                     End Function)
                            If hpRec IsNot Nothing Then
                                Dim hh2 = Canon.CanonRecords.Hdpt(hpRec, pm)
                                Dim srcHdpt = hpRec.SourcePluginName
                                Dim srcTxst = ""
                                If hh2 IsNot Nothing AndAlso hh2.TextureSet <> 0UI Then
                                    Dim tr2 = pm.GetRecord(hh2.TextureSet)
                                    If tr2 IsNot Nothing Then srcTxst = tr2.SourcePluginName
                                End If
                                Dim later = Function(pl As String) As Boolean
                                                Return pl <> "" AndAlso Not String.Equals(pl, origin, StringComparison.OrdinalIgnoreCase)
                                            End Function
                                If later(srcTxst) Then
                                    culpa = $"TXST wins it: {srcTxst} (NPC from {origin})"
                                ElseIf later(srcHdpt) Then
                                    culpa = $"HDPT wins it: {srcHdpt} (NPC from {origin})"
                                End If
                            End If
                            ' esperado segun el record de la shape
                            Dim esperado As String = Nothing
                            Dim myMesh As String = Nothing
                            If hpRec IsNot Nothing Then
                                Dim hx = Canon.CanonRecords.Hdpt(hpRec, pm)
                                If hx IsNot Nothing Then
                                    myMesh = hx.ModelFileName
                                    If hx.TextureSet <> 0UI Then
                                        Dim txr = pm.GetRecord(hx.TextureSet)
                                        If txr IsNot Nothing AndAlso txr.Header.Signature = "TXST" Then
                                            Dim tt = Canon.CanonRecords.Txst(txr, pm)
                                            If tt IsNot Nothing Then esperado = If(q = 0, tt.Ranura(0), If(q = 1, tt.Ranura(1), Nothing))
                                        End If
                                    End If
                                End If
                            End If
                            Dim eq = Function(x As String, y As String) As Boolean
                                         Return x IsNot Nothing AndAlso y IsNot Nothing AndAlso
                                                String.Equals(x.Replace("/"c, "\"c), y.Replace("/"c, "\"c), StringComparison.OrdinalIgnoreCase)
                                     End Function
                            Dim clase As String
                            If culpa <> "no override" Then
                                clase = "STALE REFERENCE (later override)" : ovr += 1
                            ElseIf eq(vb, esperado) AndAlso Not eq(va, esperado) Then
                                ' nosotros fieles, el CK no. ¿el CK copio de una hermana de la misma malla?
                                Dim hermana = False
                                If myMesh IsNot Nothing Then
                                    For Each kv2 In pm.AllRecords
                                        If kv2.Value Is Nothing OrElse kv2.Value.Header.Signature <> "HDPT" Then Continue For
                                        Dim ho = Canon.CanonRecords.Hdpt(kv2.Value, pm)
                                        If ho Is Nothing OrElse Not String.Equals(ho.ModelFileName, myMesh, StringComparison.OrdinalIgnoreCase) Then Continue For
                                        If String.Equals(ho.EditorID, nm, StringComparison.OrdinalIgnoreCase) Then Continue For
                                        If ho.TextureSet = 0UI Then Continue For
                                        Dim tr3 = pm.GetRecord(ho.TextureSet)
                                        If tr3 Is Nothing OrElse tr3.Header.Signature <> "TXST" Then Continue For
                                        Dim t3d = Canon.CanonRecords.Txst(tr3, pm)
                                        If t3d Is Nothing Then Continue For
                                        Dim otro = If(q = 0, t3d.Ranura(0), If(q = 1, t3d.Ranura(1), Nothing))
                                        If eq(va, otro) Then hermana = True : Exit For
                                    Next
                                End If
                                If hermana Then
                                    clase = "CK BUG (overwritten with a sibling's texture from the same mesh)" : ckBug += 1
                                Else
                                    clase = "CK != record and NOT from a sibling" : ckOtro += 1
                                End If
                            ElseIf eq(va, esperado) Then
                                clase = "⛔ OURS != record  -> OUR DEFECT" : nuestro += 1
                            Else
                                clase = "none matches the record" : ninguno += 1
                            End If
                            culpa = clase
                            If ejem.Count < 30 Then ejem.Add($"0x{kv.Key:X8} '{nm}' TX{q:D2}  CK='{IO.Path.GetFileName(va)}'  OURS='{IO.Path.GetFileName(vb)}'   [{culpa}]")
                        End If
                    Next
                Next
                If any Then withDiff += 1
            Next
            Console.WriteLine($"[texslotdiff] NPCs compared: {npcs}   with any difference: {withDiff}   (slot,shape) differing: {shapesDiff}")
            Console.WriteLine("[texslotdiff] === CLASSIFICATION of the diverging (shape,slot) ===")
            Console.WriteLine($"   STALE REFERENCE (later override)         : {ovr}")
            Console.WriteLine($"   CK BUG (overwritten with sibling texture) : {ckBug}")
            Console.WriteLine($"   CK != record, not from a sibling         : {ckOtro}")
            Console.WriteLine($"   ⛔ OUR DEFECT                             : {nuestro}")
            Console.WriteLine($"   none matches the record                  : {ninguno}")
            Console.WriteLine($"[texslotdiff] by slot: {String.Join(" · ", porSlot.OrderBy(Function(z) z.Key).Select(Function(z) $"TX{z.Key:D2}={z.Value}"))}")
            For Each e In ejem : Console.WriteLine("   " & e) : Next
            Return
        End If

        ' --meshcollide: ¿en cuántos NPCs hay DOS O MÁS head parts que resuelven a la MISMA malla? Es el
        ' alcance exacto de la ley del motor "cada head part aplica su propio TNAM sobre el mismo nodo, gana
        ' el último" (SkyrimSE BSFaceGenManager::PrepareHeadPartForShaders 0x14042BD90 + el bucle 0x14042BCC0).
        ' Sin este número, "arreglarlo" seria fitear los 2 casos que el comparador encontró.
        If opt.MeshCollide Then
            Dim tot = 0, colNpc = 0
            Dim pairs As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            Dim byType As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            For Each kv In pm.AllRecords
                Dim rc = kv.Value
                If rc Is Nothing OrElse rc.Header.Signature <> "NPC_" Then Continue For
                Dim npc = RecordParsers.ParseNPC(rc, pm)
                If npc Is Nothing OrElse npc.Record.PartesDeCabeza().Count = 0 Then Continue For
                tot += 1
                ' malla -> lista de (hdpt, tnam). Se EXPANDEN las extra parts, que es lo que hace el motor.
                Dim byMesh As New Dictionary(Of String, List(Of Canon.IHdpt))(StringComparer.OrdinalIgnoreCase)
                Dim queue As New Queue(Of UInteger)(npc.Record.PartesDeCabeza())
                Dim seenHp As New HashSet(Of UInteger)
                While queue.Count > 0
                    Dim fid = queue.Dequeue()
                    If fid = 0UI OrElse Not seenHp.Add(fid) Then Continue While
                    Dim hr = pm.GetRecord(fid)
                    If hr Is Nothing OrElse hr.Header.Signature <> "HDPT" Then Continue While
                    Dim h = Canon.CanonRecords.Hdpt(hr, pm)
                    If h Is Nothing OrElse String.IsNullOrEmpty(h.ModelFileName) Then Continue While
                    If h.PartesExtra() IsNot Nothing Then
                        For Each e In h.PartesExtra() : queue.Enqueue(e) : Next
                    End If
                    If Not byMesh.ContainsKey(h.ModelFileName) Then byMesh(h.ModelFileName) = New List(Of Canon.IHdpt)
                    byMesh(h.ModelFileName).Add(h)
                End While
                Dim hasCol = False
                For Each mk In byMesh
                    If mk.Value.Count < 2 Then Continue For
                    ' sólo cuenta si los TNAM DIFIEREN: con el mismo TNAM el "gana el último" es inerte.
                    Dim distinct = mk.Value.Select(Function(z) z.TextureSet).Distinct().Count()
                    If distinct < 2 Then Continue For
                    hasCol = True
                    Dim key = String.Join(" + ", mk.Value.Select(Function(z) z.EditorID).OrderBy(Function(z) z))
                    pairs(key) = If(pairs.ContainsKey(key), pairs(key), 0) + 1
                    Dim tk = String.Join("/", mk.Value.Select(Function(z) z.TipoDeParte().ToString()).Distinct().OrderBy(Function(z) z))
                    byType(tk) = If(byType.ContainsKey(tk), byType(tk), 0) + 1
                Next
                If hasCol Then
                    colNpc += 1
                    If colNpc <= 8 Then Console.WriteLine($"   [affected] 0x{kv.Key:X8} {npc.EditorID} origin={pm.GetOriginatingPluginName(kv.Key)}")
                End If
            Next
            Console.WriteLine($"[meshcollide] NPCs with head parts: {tot}")
            Console.WriteLine($"[meshcollide] NPCs with >=2 head parts on the SAME mesh and a DIFFERENT TNAM: {colNpc} ({100.0 * colNpc / Math.Max(1, tot):F2}%)")
            Console.WriteLine($"[meshcollide] by PartType: {String.Join(" · ", byType.OrderByDescending(Function(z) z.Value).Select(Function(z) $"type {z.Key}: {z.Value}"))}")
            Console.WriteLine("[meshcollide] combinations:")
            For Each p In pairs.OrderByDescending(Function(z) z.Value).Take(15)
                Console.WriteLine($"   x{p.Value,5}  {p.Key}")
            Next
            Return
        End If

        ' --recscan "SIG|substr": vuelca records de una signatura cuyo EditorID matchea, con los campos que
        ' importan para diagnosticar (HDPT: partType/mesh/TNAM + las texturas del TNAM). Generico a proposito:
        ' cada vez que hizo falta mirar "que dicen los records de esta familia" hubo que escribir un flag nuevo.
        If opt.RecScan <> "" Then
            Dim parts = opt.RecScan.Split("|"c)
            Dim sig = parts(0).Trim().ToUpperInvariant()
            Dim sub_ = If(parts.Length > 1, parts(1).Trim(), "")
            ' ABORTAR con una firma no implementada. Antes el loop filtraba por `sig` y despues
            ' entraba SOLO en la rama HDPT: para cualquier otra firma imprimia "[recscan] 0 records
            ' XXXX matchean '...'" — un CERO que se lee como "no hay ninguno" cuando en realidad no
            ' se miro nada. Un falso negativo silencioso en una herramienta de verificacion es peor
            ' que no tenerla (me hizo descartar la hipotesis correcta del slot 5 de FO4).
            Dim supported = New HashSet(Of String)(StringComparer.Ordinal) From {"HDPT", "TXST"}
            If Not supported.Contains(sig) Then
                Console.Error.WriteLine($"--recscan: signature '{sig}' is not implemented (supported: {String.Join(", ", supported.OrderBy(Function(x) x))}). Refusing to report 0 matches for a scan that was never performed.")
                Environment.ExitCode = 2
                Return
            End If
            Dim n = 0
            For Each kv In pm.AllRecords
                Dim rc = kv.Value
                If rc Is Nothing OrElse rc.Header.Signature <> sig Then Continue For
                If sig = "HDPT" Then
                    Dim h = Canon.CanonRecords.Hdpt(rc, pm)
                    If h Is Nothing OrElse (sub_ <> "" AndAlso h.EditorID.IndexOf(sub_, StringComparison.OrdinalIgnoreCase) < 0) Then Continue For
                    n += 1
                    Dim extra = If(h.PartesExtra() Is Nothing OrElse h.PartesExtra().Count = 0, "-",
                                   String.Join(",", h.PartesExtra().Select(Function(e) $"0x{e:X8}")))
                    Console.WriteLine($"HDPT 0x{kv.Key:X8}[{rc.SourcePluginName}] '{h.EditorID}' partType={h.TipoDeParte()} flags=0x{h.Flags:X2} mesh='{h.ModelFileName}' TNAM=0x{h.TextureSet:X8} HNAM/extra={extra}")
                    ' NAM0/NAM1: el .tri sale del RECORD, no de una convencion de nombres sobre el mesh
                    ' (ver la ley "tri = solo del record"). Sin esto no se puede saber contra que .tri
                    ' se aplican los sliders MSDK/MSDV de un NPC.
                    Console.WriteLine($"        tri: raceMorph='{h.ArchivoDeDeformacion(0UI)}' tri='{h.ArchivoDeDeformacion(1UI)}' chargenMorph='{h.ArchivoDeDeformacion(2UI)}'")
                    If h.TextureSet <> 0UI Then
                        Dim tr = pm.GetRecord(h.TextureSet)
                        If tr IsNot Nothing AndAlso tr.Header.Signature = "TXST" Then
                            Dim t = Canon.CanonRecords.Txst(tr, pm)
                            Console.WriteLine($"        TNAM(gana {tr.SourcePluginName}) D='{t.Ranura(0)}' N='{t.Ranura(1)}'")
                        End If
                    End If
                ElseIf sig = "TXST" Then
                    ' TXST: los 8 slots TX00..TX07 + MNAM. TX02 = Wrinkles, que es el que
                    ' `NpcMaterialResolver` puede imponer sobre el material y que termina en el
                    ' slot 5 del texture set del NIF cuando el shader es FaceTint.
                    Dim t = Canon.CanonRecords.Txst(rc, pm)
                    If t Is Nothing Then Continue For
                    Dim hay = New String() {t.EditorID, t.Ranura(0), t.Ranura(1), t.Ranura(2),
                                            t.Ranura(3), t.Ranura(4), t.Ranura(5),
                                            t.Ranura(6), t.Ranura(7), t.MaterialDe()}
                    If sub_ <> "" AndAlso Not hay.Any(Function(h) If(h, "").IndexOf(sub_, StringComparison.OrdinalIgnoreCase) >= 0) Then Continue For
                    n += 1
                    Console.WriteLine($"TXST 0x{kv.Key:X8}[{rc.SourcePluginName}] '{t.EditorID}' flags=0x{t.Flags:X4} facegen={t.EsDeCaraGenerada()}")
                    Console.WriteLine($"        TX00(D)='{t.Ranura(0)}'  TX01(N)='{t.Ranura(1)}'  TX02(Wrinkles)='{t.Ranura(2)}'")
                    Console.WriteLine($"        TX03(G)='{t.Ranura(3)}'  TX04(H)='{t.Ranura(4)}'  TX05(Env)='{t.Ranura(5)}'  TX06(ML)='{t.Ranura(6)}'  TX07(S)='{t.Ranura(7)}'  MNAM='{t.MaterialDe()}'")
                End If
            Next
            Console.WriteLine($"[recscan] {n} records {sig} matchean '{sub_}'")
            Return
        End If

        If opt.DdsProbe <> "" Then
            DdsProbe(pm, Convert.ToUInt32(opt.DdsProbe.Replace("0x", ""), 16))
            Return
        End If

        ' --- TINTCOUNTSCAN: capas de tint por NPC (NPC autoradas + merge con defaults de RACE).
        If opt.TintCountScan Then
            TintCountScan(pm)
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

        ' --- RACECOMPAT: reconstruye la inyección de razas que RaceCompatibility hace EN RUNTIME sobre las
        ' FormLists vanilla de head parts (ver RaceCompatibilityCatalog) y comprueba, contra el load order real,
        ' que un HDPT vanilla pase a ser válido para las razas custom. Diagnóstico: no altera nada.
        If opt.RaceCompat Then
            RaceCompatScan(pm)
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

        ' --- FACEGENGATE: blast radius del cambio de gate de FaceGen (heurística → RACE.DATA bit 0x2).
        If opt.FaceGenGate Then
            _fggEdidFilter = opt.Edid
            FaceGenGateBlastRun(pm, opt.FaceGenGateSample)
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

        ' DIAGNOSTICO --nifslots: shape -> shaderType + 10 texslots (file-only).
        If opt.NifSlots <> "" Then
            NifSlotsRun(opt.NifSlots)
            Return
        End If

        ' --- ESTIMATESCLP: estima SCLP (bone-scale por hueso *_skin) por ratio de extents en espacio-hueso
        '     de un underarmor vs un body de referencia, y lo compara contra el .sclp autorado vanilla.
        If opt.EstimateSclp <> "" Then EstimateSclpRun(opt.EstimateSclp, opt.ShapeFilter) : Return

        ' --- SCLPDIAG: vuelca la geometría cruda por hueso (allSet/domSet, percentiles, ratios candidatos)
        '     para analizar a mano qué fórmula recupera el SCLP autorado.
        If opt.SclpDiag <> "" Then SclpDiagRun(opt.SclpDiag, opt.ShapeFilter) : Return

        ' --- SCLPBATCH: evalúa muchas combinaciones (una línea por caso en el manifiesto) en UNA sola corrida,
        '     reusando el mismo pipeline NN (BuildNNPairs/AccumulateNNScales) y comparando vs el SCLP autorado.
        If opt.SclpBatch <> "" Then SclpBatchRun(opt.SclpBatch) : Return

        ' --- BINDDIFF: compara los binds skin→bone (SkinToBone) de cada hueso *_skin entre dos NIFs
        '     (underarmor vs body). Si difieren, la escala del SCLP vive en el bind; si son idénticos, no.
        If opt.BindDiff <> "" Then BindDiffRun(opt.BindDiff) : Return

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
        ' EL MOTIVO SE DICE. Descartarlo dejaba al usuario del CLI sin saber si su
        ' FGBAKE_DECODE_CACHE_MB se leyó o si corrió sin techo — un techo que no se ve no se
        ' puede diagnosticar. Es el mismo argumento que MainForm ya aplica.
        Console.WriteLine("[decode-cache] " & FaceTintCpuCompositor.BeginBatchDecodeCacheConMotivo())
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
                    okOne = BuildFaceGenNpc(pm, w.Esp, w.Edid, opt.CompareCk, dataPath)
                Else
                    okOne = BakeNpc(pm, w.Esp, w.Edid, dataPath, opt.OutDir, tintBytesCache, opt.DumpDir)
                End If
                If okOne Then ok += 1 Else fail += 1
            Next
        Finally
            FaceTintCpuCompositor.EndBatchDecodeCache()
        End Try
        Console.WriteLine($"[done] {ok} ok / {fail} fail of {work.Count}")
        If ok = 0 Then Environment.ExitCode = 1
    End Sub

    ''' <summary>--buildfacegen: bake COMPLETO (NIF + 3 DDS `_2` sandbox) de UN NPC headless via la MISMA
    ''' ruta que la app (FaceGenBuilder.BuildCharGen). Con --vanillaonly el entorno ya cargó SOLO plugins
    ''' oficiales (PluginManager.OfficialPluginsOnly), así que el record + las texturas son vanilla por
    ''' construcción (sin override de mods). Devuelve True si Success.</summary>
    Private Function BuildFaceGenNpc(pm As PluginManager, espName As String, edid As String, Optional compareCk As Boolean = False, Optional dataPath As String = Nothing) As Boolean
        Dim npcFormID = ResolveEdid(pm, espName, edid)
        If npcFormID = 0UI Then
            Console.Error.WriteLine($"[skip] EDID='{edid}' not provided by '{espName}'.") : Return False
        End If
        Try
            Dim presets As New Dictionary(Of UInteger, FO4_NPC_Manager.LooksmenuLoader.LooksmenuPreset)
            ' Resolver de materiales por-shape = el MISMO que el render (texture-paths/BGSM/tints fieles a CK).
            ' NpcRenderContext solo necesita el PluginManager (sin GL). overlay = identidad (sin presets LM).
            Dim ctx As New FO4_NPC_Manager.NpcRenderContext(pm, dataPath)
            Dim mres As New FO4_NPC_Manager.NpcMaterialResolver(ctx, Function(raw As NPC_Data, fid As UInteger) raw)
            Dim res = FO4_NPC_Manager.FaceGenBuilder.BuildCharGen(
                npcFormID, pm, presets, Nothing,
                AddressOf mres.ApplyShapeMaterialOverrides,
                willBePacked:=False,
                lutDataPath:=dataPath)
            If res Is Nothing OrElse Not res.Success Then
                Console.Error.WriteLine($"[fail] {edid} (0x{npcFormID:X8}): {If(res Is Nothing, "null result", res.Summary)}") : Return False
            End If
            Console.WriteLine($"[ok] {edid} 0x{npcFormID:X8} -> {res.OutputPath} (kept={res.ShapesKept} dropped={res.ShapesDropped})")
            If compareCk Then CompareBakedVsCk(pm, npcFormID, res.OutputPath)
            Return True
        Catch ex As Exception
            Console.Error.WriteLine($"[fail] {edid} (0x{npcFormID:X8}): {ex.GetType().Name}: {ex.Message}") : Return False
        End Try
    End Function

    ''' <summary>Apaga el bloque de comparación del DDS facetint en CompareBakedVsCk. El barrido de NIF
    ''' (--ssecomparebatch) lo enciende: ese bloque RE-COMPONE el facetint completo por NPC (BakeFaceTintDds)
    ''' y además decodifica ambos DDS y los compara 512x512 píxel a píxel — es el costo dominante del barrido
    ''' y no aporta NADA a la validación del NIF. No afecta el bake (el NIF sale idéntico).</summary>
    Private SkipDdsCompare As Boolean = False

    ''' <summary>REFERENCIA DEL CK PARA EL NIF — SIEMPRE del BSA/BA2, nunca de un suelto.
    ''' El NIF que horneamos queda LOOSE en el mismo Data, y <c>FilesDictionary</c> hace que el suelto GANE sobre
    ''' el archive. Con <c>GetBytes</c> la corrida siguiente se comparaba CONTRA SI MISMA y daba ~0 diferencias
    ''' (el fallo silencioso de 10-stack-arnes-de-medicion: termina con END BATCH y no midio nada).
    ''' El camino del DDS ya lo hacia bien; el del NIF —el que sostiene TODAS las cifras de geometria— no.
    ''' <paramref name="fromArchive"/> sale False solo si NO hay entrada archivada: el caller lo reporta como
    ''' categoria REAL en vez de degradar a un PASS.</summary>
    Private Function CkNifRefBytes(ckKey As String, ByRef fromArchive As Boolean) As Byte()
        Dim b = FilesDictionary_class.GetArchiveOriginalBytes(ckKey)
        fromArchive = b IsNot Nothing AndAlso b.Length > 0
        Return b
    End Function

    ''' <summary>Diff EXHAUSTIVO de dos NIFs sueltos on-disk (CK canonico vs nuestro _2), SIN hornear y SIN
    ''' montar plugins. Reusa la MISMA maquinaria por-shape que CompareBakedVsCk (CompareShapeExhaustive →
    ''' Shader + Alpha). La ref del CK es el path suelto que se pasa (no el archive): asi compara contra la
    ''' salida canonica que el CK dejo loose, no contra el BA2 shipped.</summary>
    Private Sub CompareLooseFilesRun(spec As String)
        Dim parts = spec.Split("|"c)
        If parts.Length < 2 Then
            Console.Error.WriteLine("--comparefiles necesita '<ckNif>|<ourNif>'") : Environment.ExitCode = 2 : Return
        End If
        Dim ckPath = parts(0).Trim(), myPath = parts(1).Trim()
        If Not File.Exists(ckPath) Then Console.Error.WriteLine($"CK nif does not exist: {ckPath}") : Environment.ExitCode = 2 : Return
        If Not File.Exists(myPath) Then Console.Error.WriteLine($"our nif does not exist: {myPath}") : Environment.ExitCode = 2 : Return

        Dim ckBytes = File.ReadAllBytes(ckPath), myBytes = File.ReadAllBytes(myPath)
        Dim ckNif As New Nifcontent_Class_Manolo() : ckNif.Load_Manolo(ckBytes)
        Dim myNif As New Nifcontent_Class_Manolo() : myNif.Load_Manolo(myBytes)

        Dim real As New List(Of String)()   ' diferencias REALES
        Dim noop As New List(Of String)()   ' diferencias esperadas / cosmeticas
        Dim normP = Function(p As String) If(p, "").Replace("/"c, "\"c).ToLowerInvariant().Replace("data\", "").TrimStart("\"c)

        Console.WriteLine($"======== COMPARE LOOSE FILES (no bake, game={Config_App.Current.Game}) ========")
        Console.WriteLine($"  CK  = {ckPath}")
        Console.WriteLine($"  OUR = {myPath}")

        ' ---- estructura NIF ----
        Console.WriteLine($"  [NIF/struct] bytes CK={ckBytes.Length} our={myBytes.Length}  blocks CK={ckNif.Blocks.Count} our={myNif.Blocks.Count}")
        If ckBytes.Length <> myBytes.Length Then noop.Add($"NIF byte-size CK={ckBytes.Length} vs our={myBytes.Length} (framing/table order — may be a NO-OP)")
        Dim ckTypes = String.Join(",", ckNif.Blocks.GroupBy(Function(b) b.GetType().Name).OrderBy(Function(g) g.Key).Select(Function(g) $"{g.Key}x{g.Count()}"))
        Dim myTypes = String.Join(",", myNif.Blocks.GroupBy(Function(b) b.GetType().Name).OrderBy(Function(g) g.Key).Select(Function(g) $"{g.Key}x{g.Count()}"))
        If ckTypes <> myTypes Then
            real.Add($"NIF block-type histogram DIFF:{Environment.NewLine}      CK ={ckTypes}{Environment.NewLine}      our={myTypes}")
        Else
            Console.WriteLine($"  [NIF/struct] block-type histogram OK ({myNif.Blocks.Count} blocks)")
        End If
        Dim ckRoot = TryCast(ckNif.Blocks.FirstOrDefault(), NiflySharp.Blocks.NiAVObject)
        Dim myRoot = TryCast(myNif.Blocks.FirstOrDefault(), NiflySharp.Blocks.NiAVObject)
        If ckRoot IsNot Nothing AndAlso myRoot IsNot Nothing Then
            Dim ckR = $"{ckRoot.GetType().Name} '{ckRoot.Name?.String}' flags=0x{ckRoot.Flags_ui:X4}"
            Dim myR = $"{myRoot.GetType().Name} '{myRoot.Name?.String}' flags=0x{myRoot.Flags_ui:X4}"
            If ckR <> myR Then real.Add($"NIF root DIFF: CK[{ckR}] vs our[{myR}]") Else Console.WriteLine($"  [NIF/struct] root OK: {myR}")
        End If

        ' ---- por shape (misma maquinaria que el comparador de produccion) ----
        Dim ckShapes = ckNif.NifShapes.ToList()
        Dim myShapes = myNif.NifShapes.ToList()
        Console.WriteLine($"  [NIF] CK shapes={ckShapes.Count}  our shapes={myShapes.Count}")
        If ckShapes.Count <> myShapes.Count Then real.Add($"NIF shape count CK={ckShapes.Count} vs our={myShapes.Count}")
        For Each cs In ckShapes
            Dim nm = If(cs.Name?.String, "")
            Dim ms = myShapes.FirstOrDefault(Function(s) String.Equals(If(s.Name?.String, ""), nm, StringComparison.OrdinalIgnoreCase))
            If ms Is Nothing Then real.Add($"shape '{nm}': PRESENT in CK, ABSENT in our") : Continue For
            CompareShapeExhaustive(nm, cs, ckNif, ms, myNif, real, noop, normP)
        Next
        For Each ms In myShapes
            Dim nm = If(ms.Name?.String, "")
            If Not ckShapes.Any(Function(s) String.Equals(If(s.Name?.String, ""), nm, StringComparison.OrdinalIgnoreCase)) Then real.Add($"shape '{nm}': PRESENT in our, ABSENT in CK")
        Next

        Console.WriteLine()
        Console.WriteLine($"==== REAL diffs: {real.Count} ====")
        For Each r In real : Console.WriteLine("  [REAL] " & r) : Next
        Console.WriteLine($"==== NO-OP / cosmetic: {noop.Count} ====")
        For Each nn In noop : Console.WriteLine("  [noop] " & nn) : Next
        Console.WriteLine($"======== END COMPARE  (REAL={real.Count}  NOOP={noop.Count}) ========")
    End Sub

    ''' <summary>Compara la DATA DE SKINNING que el juego realmente usa en una malla skinneada: el bind
    ''' skinToBone (ShapeBoneTransforms) de cada hueso, emparejado por NOMBRE, mas los pesos/indices por
    ''' vertice. La transform del NODO es INERTE en skinned (el juego usa el esqueleto del personaje), asi
    ''' que aca NO se mira: solo el bind + pesos. Si estos son identicos, la malla skinneada renderiza igual.</summary>
    Private Sub SkinCheckRun(spec As String)
        Dim parts = spec.Split("|"c)
        If parts.Length < 2 Then Console.Error.WriteLine("--skincheck necesita '<ckNif>|<ourNif>'") : Environment.ExitCode = 2 : Return
        Dim ckPath = parts(0).Trim(), myPath = parts(1).Trim()
        If Not File.Exists(ckPath) OrElse Not File.Exists(myPath) Then Console.Error.WriteLine("nif does not exist") : Environment.ExitCode = 2 : Return
        Dim ckNif As New Nifcontent_Class_Manolo() : ckNif.Load_Manolo(File.ReadAllBytes(ckPath))
        Dim myNif As New Nifcontent_Class_Manolo() : myNif.Load_Manolo(File.ReadAllBytes(myPath))
        Console.WriteLine("======== SKINNING DATA CHECK (bind skinToBone + weights; node IGNORED) ========")
        Console.WriteLine($"  CK  = {ckPath}")
        Console.WriteLine($"  OUR = {myPath}")
        Dim ckMap = SkinBinds(ckNif)
        Dim myMap = SkinBinds(myNif)
        Dim anyDiff = False
        For Each kv In ckMap
            Dim nm = kv.Key
            If Not myMap.ContainsKey(nm) Then Console.WriteLine($"  shape '{nm}': no match in OUR") : anyDiff = True : Continue For
            Dim a = kv.Value, b = myMap(nm)
            Dim worstBone = "", worstT = 0.0, worstR = 0.0, worstS = 0.0
            Dim onlyCk = 0, onlyOu = 0
            For Each bk In a.Keys
                If Not b.ContainsKey(bk) Then onlyCk += 1 : Continue For
                Dim ta = a(bk), tb = b(bk)
                Dim dt = Math.Sqrt((CDbl(ta.Translation.X) - CDbl(tb.Translation.X)) ^ 2 + (CDbl(ta.Translation.Y) - CDbl(tb.Translation.Y)) ^ 2 + (CDbl(ta.Translation.Z) - CDbl(tb.Translation.Z)) ^ 2)
                Dim ds = Math.Abs(CDbl(ta.Scale) - CDbl(tb.Scale))
                Dim dr = Rot33MaxDiff(ta.Rotation, tb.Rotation)
                If dt > worstT Then worstT = dt : worstBone = bk
                If dr > worstR Then worstR = dr
                If ds > worstS Then worstS = ds
            Next
            For Each bk In b.Keys
                If Not a.ContainsKey(bk) Then onlyOu += 1
            Next
            If worstT > 0.001 OrElse worstR > 0.001 OrElse worstS > 0.001 OrElse onlyCk > 0 OrElse onlyOu > 0 Then anyDiff = True
            Console.WriteLine($"  shape '{nm}': bones CK={a.Count} OUR={b.Count} (onlyCK={onlyCk} onlyOUR={onlyOu})  bind maxΔ: T={worstT:F5}(@{worstBone}) R={worstR:F6} S={worstS:F6}")
        Next
        Console.WriteLine($"======== {(If(anyDiff, "LOS BINDS DIFIEREN -> la malla skinneada renderiza distinto", "BINDS IDENTICOS -> skinning identico (la diferencia visible NO viene del skinning del NIF)"))} ========")
    End Sub

    Private Function Rot33MaxDiff(a As NiflySharp.Structs.Matrix33, b As NiflySharp.Structs.Matrix33) As Double
        Dim m = 0.0
        m = Math.Max(m, Math.Abs(CDbl(a.M11) - CDbl(b.M11))) : m = Math.Max(m, Math.Abs(CDbl(a.M12) - CDbl(b.M12))) : m = Math.Max(m, Math.Abs(CDbl(a.M13) - CDbl(b.M13)))
        m = Math.Max(m, Math.Abs(CDbl(a.M21) - CDbl(b.M21))) : m = Math.Max(m, Math.Abs(CDbl(a.M22) - CDbl(b.M22))) : m = Math.Max(m, Math.Abs(CDbl(a.M23) - CDbl(b.M23)))
        m = Math.Max(m, Math.Abs(CDbl(a.M31) - CDbl(b.M31))) : m = Math.Max(m, Math.Abs(CDbl(a.M32) - CDbl(b.M32))) : m = Math.Max(m, Math.Abs(CDbl(a.M33) - CDbl(b.M33)))
        Return m
    End Function

    ''' <summary>Por shape, el bind skinToBone (ShapeBoneTransforms) indexado por NOMBRE de hueso.</summary>
    Private Function SkinBinds(nif As Nifcontent_Class_Manolo) As Dictionary(Of String, Dictionary(Of String, Transform_Class))
        Dim res As New Dictionary(Of String, Dictionary(Of String, Transform_Class))(StringComparer.OrdinalIgnoreCase)
        For blkIdx = 0 To nif.Blocks.Count - 1
            Dim shp = TryCast(nif.Blocks(blkIdx), NiflySharp.INiShape)
            If shp Is Nothing Then Continue For
            Dim rs As NifRenderableShape
            Try
                rs = New NifRenderableShape(nif, shp, blkIdx)
            Catch
                Continue For
            End Try
            If rs.ShapeBones.Count = 0 Then Continue For
            Dim nm = If(shp.Name?.String, "")
            Dim d As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
            For k = 0 To Math.Min(rs.ShapeBones.Count, rs.ShapeBoneTransforms.Count) - 1
                Dim bnNode = TryCast(rs.ShapeBones(k), NiflySharp.Blocks.NiNode)
                Dim bnm = If(bnNode?.Name?.String, $"?{k}")
                If Not d.ContainsKey(bnm) Then d(bnm) = rs.ShapeBoneTransforms(k)
            Next
            res(nm) = d
        Next
        Return res
    End Function

    ''' <summary>Posicion skinneada en ESPACIO DE HUESO: <c>Σ_b w_b · (skinToBone_b · v)</c>, SIN el transform
    ''' de nodo del NIF. Es la forma engine-faithful de comparar dos FaceGeom: el juego los monta sobre el
    ''' MISMO esqueleto del personaje, asi que si coinciden aca renderizan igual. Incluir el boneWorld del
    ''' propio NIF mide una jerarquia que el motor descarta (y que el CK escribe con rotaciones identidad),
    ''' lo que hace diferir el 100 % de las shapes por una razon inerte.</summary>
    Private Function SkinnedPositionsBoneSpace(nif As Nifcontent_Class_Manolo) As Dictionary(Of String, List(Of System.Numerics.Vector3))
        Dim res As New Dictionary(Of String, List(Of System.Numerics.Vector3))(StringComparer.OrdinalIgnoreCase)
        For blkIdx = 0 To nif.Blocks.Count - 1
            Dim shp = TryCast(nif.Blocks(blkIdx), NiflySharp.INiShape)
            If shp Is Nothing Then Continue For
            Dim rs As NifRenderableShape
            Try
                rs = New NifRenderableShape(nif, shp, blkIdx)
            Catch
                Continue For
            End Try
            Dim geo = rs.Geometry
            If geo Is Nothing Then Continue For
            Dim verts = geo.GetVertexPositions()
            If verts Is Nothing OrElse verts.Count = 0 Then Continue For
            Dim nB = Math.Min(rs.ShapeBones.Count, rs.ShapeBoneTransforms.Count)
            If nB = 0 Then Continue For   ' sin skin: no aplica esta metrica
            Dim nm = If(shp.Name?.String, "")
            Dim sk = geo.GetSkinning()
            Dim wpv = sk.WeightsPerVertex
            Dim lst As New List(Of System.Numerics.Vector3)(verts.Count)
            For i = 0 To verts.Count - 1
                Dim accX As Single = 0, accY As Single = 0, accZ As Single = 0, wsum As Single = 0
                For j = 0 To wpv - 1
                    Dim fi = i * wpv + j
                    If fi >= sk.BoneWeights.Length Then Exit For
                    Dim w = CSng(sk.BoneWeights(fi))
                    If w = 0.0F Then Continue For
                    Dim bi = CInt(sk.BoneIndices(fi))
                    If bi < 0 OrElse bi >= nB Then Continue For
                    Dim p = ApplyT(rs.ShapeBoneTransforms(bi), verts(i))
                    accX += w * p.X : accY += w * p.Y : accZ += w * p.Z : wsum += w
                Next
                If wsum > 0.0001F Then
                    lst.Add(New System.Numerics.Vector3(accX / wsum, accY / wsum, accZ / wsum))
                Else
                    lst.Add(verts(i))
                End If
            Next
            res(nm) = lst
        Next
        Return res
    End Function

    Private Function SkinnedPositions(nif As Nifcontent_Class_Manolo) As Dictionary(Of String, List(Of System.Numerics.Vector3))
        Dim res As New Dictionary(Of String, List(Of System.Numerics.Vector3))(StringComparer.OrdinalIgnoreCase)
        For blkIdx = 0 To nif.Blocks.Count - 1
            Dim shp = TryCast(nif.Blocks(blkIdx), NiflySharp.INiShape)
            If shp Is Nothing Then Continue For
            Dim rs As NifRenderableShape
            Try
                rs = New NifRenderableShape(nif, shp, blkIdx)
            Catch
                Continue For
            End Try
            Dim geo = rs.Geometry
            If geo Is Nothing Then Continue For
            Dim verts = geo.GetVertexPositions()
            If verts Is Nothing OrElse verts.Count = 0 Then Continue For
            Dim nm = If(shp.Name?.String, "")
            Dim nB = Math.Min(rs.ShapeBones.Count, rs.ShapeBoneTransforms.Count)
            If nB = 0 Then
                ' sin skin: transform global del shape
                Dim gt = Transform_Class.GetGlobalTransform(shp, nif)
                Dim lst0 As New List(Of System.Numerics.Vector3)(verts.Count)
                For Each v In verts : lst0.Add(ApplyT(gt, v)) : Next
                res(nm) = lst0
                Continue For
            End If
            ' composite por hueso: boneWorld ∘ skinToBone
            Dim M(nB - 1) As Transform_Class
            For k = 0 To nB - 1
                Dim bnNode = TryCast(rs.ShapeBones(k), NiflySharp.Blocks.NiNode)
                Dim bw = If(bnNode IsNot Nothing, Transform_Class.GetGlobalTransform(bnNode, nif), New Transform_Class())
                M(k) = bw.ComposeTransforms(rs.ShapeBoneTransforms(k))
            Next
            Dim sk = geo.GetSkinning()
            Dim wpv = sk.WeightsPerVertex
            Dim lst As New List(Of System.Numerics.Vector3)(verts.Count)
            For i = 0 To verts.Count - 1
                Dim accX As Single = 0, accY As Single = 0, accZ As Single = 0, wsum As Single = 0
                For j = 0 To wpv - 1
                    Dim fi = i * wpv + j
                    If fi >= sk.BoneWeights.Length Then Exit For
                    Dim w = CSng(sk.BoneWeights(fi))
                    If w = 0.0F Then Continue For
                    Dim bi = CInt(sk.BoneIndices(fi))
                    If bi < 0 OrElse bi >= nB Then Continue For
                    Dim p = ApplyT(M(bi), verts(i))
                    accX += w * p.X : accY += w * p.Y : accZ += w * p.Z : wsum += w
                Next
                If wsum > 0.0001F Then
                    lst.Add(New System.Numerics.Vector3(accX / wsum, accY / wsum, accZ / wsum))
                Else
                    lst.Add(verts(i))
                End If
            Next
            res(nm) = lst
        Next
        Return res
    End Function

    Private Function FmtRot(r As NiflySharp.Structs.Matrix33) As String
        Return $"{r.M11:F5},{r.M12:F5},{r.M13:F5}; {r.M21:F5},{r.M22:F5},{r.M23:F5}; {r.M31:F5},{r.M32:F5},{r.M33:F5}"
    End Function

    ''' <summary>Volcado COMPLETO de un NIF, sin umbrales ni clasificacion, ordenado por nombre para que un
    ''' `diff` de dos volcados exponga cualquier diferencia de campo (transform, shader, texslot, alpha) que el
    ''' comparador con umbral esconde. NO hornea ni monta plugins.</summary>
    Private Sub DumpNifFull(path As String)
        If Not File.Exists(path) Then Console.Error.WriteLine($"nif does not exist: {path}") : Environment.ExitCode = 2 : Return
        Dim bytes = File.ReadAllBytes(path)
        Dim nif As New Nifcontent_Class_Manolo() : nif.Load_Manolo(bytes)
        Console.WriteLine($"# FILE {IO.Path.GetFileName(path)}  bytes={bytes.Length} blocks={nif.Blocks.Count}")
        Dim root = TryCast(nif.Blocks.FirstOrDefault(), NiflySharp.Blocks.NiAVObject)
        If root IsNot Nothing Then Console.WriteLine($"ROOT {root.GetType().Name} name='{root.Name?.String}' flags=0x{root.Flags_ui:X4}")

        ' ---- NODOS (ordenados por nombre) ----
        Dim nodes = nif.Blocks.OfType(Of NiflySharp.Blocks.NiNode)().OrderBy(Function(n) If(n.Name?.String, ""), StringComparer.Ordinal).ToList()
        For Each nn In nodes
            Dim nm = If(nn.Name?.String, "")
            Dim t As New Transform_Class(nn)
            Console.WriteLine($"NODE '{nm}' flags=0x{nn.Flags_ui:X4} T=({t.Translation.X:F5},{t.Translation.Y:F5},{t.Translation.Z:F5}) S={t.Scale:F6} R=[{FmtRot(t.Rotation)}]")
        Next

        ' ---- SHAPES (ordenados por nombre) ----
        For Each s In nif.NifShapes.OrderBy(Function(x) If(x.Name?.String, ""), StringComparer.Ordinal)
            Dim nm = If(s.Name?.String, "")
            Dim t As New Transform_Class(s)
            Console.WriteLine($"SHAPE '{nm}' flags=0x{s.Flags_ui:X4} T=({t.Translation.X:F5},{t.Translation.Y:F5},{t.Translation.Z:F5}) S={t.Scale:F6} R=[{FmtRot(t.Rotation)}]")
            Dim bts = TryCast(s, NiflySharp.Blocks.BSTriShape)
            If bts IsNot Nothing Then
                Console.WriteLine($"SHAPE '{nm}' vdesc=0x{bts.VertexDesc.Value:X16} bounds.c=({bts.Bounds.Center.X:F4},{bts.Bounds.Center.Y:F4},{bts.Bounds.Center.Z:F4}) bounds.r={bts.Bounds.Radius:F4}")
            End If
            Dim sh = TryCast(nif.GetShader(s), NiflySharp.Blocks.BSLightingShaderProperty)
            If sh IsNot Nothing Then
                Console.WriteLine($"SHADER '{nm}' type={sh.ShaderType_SK_FO4} SSPF1=0x{CUInt(sh.ShaderFlags_SSPF1):X8} SSPF2=0x{CUInt(sh.ShaderFlags_SSPF2):X8}")
                Console.WriteLine($"SHADER '{nm}' Alpha={sh.Alpha:F6} Gloss={sh.Glossiness:F4} SpecStr={sh.SpecularStrength:F6} Smooth={sh.Smoothness:F6} EmMul={sh.EmissiveMultiple:F6}")
                Console.WriteLine($"SHADER '{nm}' Refr={sh.RefractionStrength:F6} Soft={sh.Softlight:F6} SSSRoll={sh.SubsurfaceRolloff:F6} Backl={sh.BacklightPower:F6} Fres={sh.FresnelPower:F6}")
                Console.WriteLine($"SHADER '{nm}' G2P={sh.GrayscaleToPaletteScale:F6} SkinAlpha={sh.SkinTintAlpha:F6} Rim={sh.RimlightPower:F6}")
                Console.WriteLine($"SHADER '{nm}' SpecColor=({sh.SpecularColor.R:F4},{sh.SpecularColor.G:F4},{sh.SpecularColor.B:F4}) SkinTint=({sh.SkinTintColor.R:F4},{sh.SkinTintColor.G:F4},{sh.SkinTintColor.B:F4}) HairTint=({sh.HairTintColor.R:F4},{sh.HairTintColor.G:F4},{sh.HairTintColor.B:F4})")
                Console.WriteLine($"SHADER '{nm}' Emissive=({sh.EmissiveColor.R:F4},{sh.EmissiveColor.G:F4},{sh.EmissiveColor.B:F4},{sh.EmissiveColor.A:F4}) UVoff=({sh.UVOffset.U:F4},{sh.UVOffset.V:F4}) UVscale=({sh.UVScale.U:F4},{sh.UVScale.V:F4})")
            End If
            Dim ts = GetTexSet(nif, s)
            If ts IsNot Nothing AndAlso ts.Textures IsNot Nothing Then
                For si = 0 To ts.Textures.Count - 1
                    Console.WriteLine($"TEX '{nm}'[{si}]='{ts.Textures(si)?.Content}'")
                Next
            End If
            Dim ap As NiflySharp.Blocks.NiAlphaProperty = Nothing
            If s.AlphaPropertyRef IsNot Nothing AndAlso s.AlphaPropertyRef.Index >= 0 Then ap = TryCast(nif.Blocks(s.AlphaPropertyRef.Index), NiflySharp.Blocks.NiAlphaProperty)
            If ap IsNot Nothing Then Console.WriteLine($"ALPHA '{nm}' flags=0x{ap.Flags.Value:X4} threshold={ap.Threshold}")

            ' ---- EXTRA DATA LIST del shape. Se imprime el INDICE de bloque de cada entrada para poder
            '      distinguir "dos ECED distintos" de "el MISMO bloque referenciado dos veces" (que es lo
            '      que la categoria `ExtraDataList.Count CK=2 baked=1` no puede decir por si sola), y el
            '      CONTENIDO del BSEyeCenterExtraData (NumData + floats) para probar si la data coincide. ----
            If s.ExtraDataList IsNot Nothing Then
                Dim idxs = s.ExtraDataList.Indices.ToList()
                Console.WriteLine($"EXTRALIST '{nm}' count={idxs.Count} blockIdx=[{String.Join(",", idxs)}]")
                For k = 0 To idxs.Count - 1
                    Dim bi = idxs(k)
                    If bi < 0 OrElse bi >= nif.Blocks.Count Then Console.WriteLine($"EXTRA '{nm}' [{k}] #{bi} (FUERA DE RANGO)") : Continue For
                    Dim xb = nif.Blocks(bi)
                    Dim xnm = TryCast(xb, NiflySharp.Blocks.NiExtraData)
                    Console.WriteLine($"EXTRA '{nm}' [{k}] #{bi} type={xb.GetType().Name} name='{If(xnm?.Name?.String, "")}'")
                    Dim ec = TryCast(xb, NiflySharp.Blocks.BSEyeCenterExtraData)
                    If ec IsNot Nothing Then
                        Dim d = If(ec.Data Is Nothing, "", String.Join(",", ec.Data.Select(Function(x) x.ToString("F6"))))
                        Console.WriteLine($"ECED '{nm}' [{k}] #{bi} numData={ec.NumData} data=[{d}]")
                    End If
                Next
            End If

            ' ---- SKIN INSTANCE (solo FO4: `BSSkin::Instance` es el unico tipo con NumScales/Scales, un
            '      Vector3 POR HUESO; en el schema el bloque es #FO4# #F76#). Ese array es EL MISMO que
            '      escribe el applier de body-weight del CK (0x140A8CFD0, reserva nBones*16 inicializado
            '      en 1.0) y que el exportador de FaceGeom RE-ESCRIBE con la escala en NULL para los
            '      shapes que traen `CustomizationRemapNewBonesData` — ver la ley del body-weight.
            '      Se volcaba todo el shape menos esto, asi que la categoria `skinInstance.Scales` del
            '      comparador solo se podia ver como "CANTIDAD CK=10 baked=0", sin los VALORES. ----
            Dim sir = s.SkinInstanceRef
            If sir IsNot Nothing AndAlso sir.Index >= 0 AndAlso sir.Index < nif.Blocks.Count Then
                Dim bsk = TryCast(nif.Blocks(sir.Index), NiflySharp.Blocks.BSSkin_Instance)
                If bsk IsNot Nothing Then
                    Dim bnames As New List(Of String)
                    If bsk.Bones IsNot Nothing Then
                        For Each bi In bsk.Bones.Indices
                            Dim bn = If(bi >= 0 AndAlso bi < nif.Blocks.Count, TryCast(nif.Blocks(bi), NiflySharp.Blocks.NiNode), Nothing)
                            bnames.Add(If(bn?.Name?.String, $"#{bi}"))
                        Next
                    End If
                    Console.WriteLine($"SKIN '{nm}' bones={bsk.NumBones} numScales={bsk.NumScales}")
                    If bsk.Scales IsNot Nothing Then
                        For k = 0 To bsk.Scales.Count - 1
                            Dim sv = bsk.Scales(k)
                            Dim bnm = If(k < bnames.Count, bnames(k), "?")
                            Console.WriteLine($"SKINSCALE '{nm}' [{k}] bone='{bnm}' S=({sv.X:F6},{sv.Y:F6},{sv.Z:F6})")
                        Next
                    End If
                End If
            End If
        Next
    End Sub

    Private Function CompareBakedVsCk(pm As PluginManager, npcFormID As UInteger, bakedNifPath As String,
                                      Optional verbose As Boolean = True) As (Real As List(Of String), Noop As List(Of String))
        Dim origin = pm.GetOriginatingPluginName(npcFormID)
        Dim fgL = PluginManager.ToFaceGenLocalFormID(npcFormID)
        If verbose Then Console.WriteLine($"======== EXHAUSTIVE COMPARE vs CK  0x{npcFormID:X8}  origin='{origin}'  local=0x{fgL:X8} ========")
        Dim real As New List(Of String)()   ' diferencias REALES (defecto del bake)
        Dim noop As New List(Of String)()   ' diferencias esperadas / cosméticas

        ' path-normalize + strip sufijo sandbox _2 para detectar diffs de path que son NO-OP.
        Dim normP = Function(p As String) If(p, "").Replace("/"c, "\"c).ToLowerInvariant().Replace("data\", "").TrimStart("\"c)
        Dim stripSandbox = Function(p As String) normP(p).Replace("_d_2.dds", "_d.dds").Replace("_msn_2.dds", "_msn.dds").Replace("_s_2.dds", "_s.dds").Replace("_2.dds", ".dds").Replace("_2.nif", ".nif")

        Dim npcRec = pm.GetRecord(npcFormID)
        Dim npcData = RecordParsers.ParseNPC(npcRec, pm)
        Dim isSse = npcData IsNot Nothing AndAlso npcData.Game = Config_App.Game_Enum.Skyrim

        ' ================= NIF =================
        Dim ckKey = ($"meshes\actors\character\facegendata\facegeom\{origin}\{fgL:X8}.nif").ToLowerInvariant()
        Dim ckNifFromArchive As Boolean
        Dim ckBytes = CkNifRefBytes(ckKey, ckNifFromArchive)
        If Not ckNifFromArchive AndAlso ckBytes IsNot Nothing AndAlso ckBytes.Length > 0 Then
            real.Add($"NIF: the CK ref did NOT come from a BA2/BSA (loose) — CIRCULAR comparison against an old bake of our own")
        End If
        If ckBytes Is Nothing Then
            Console.WriteLine($"  [NIF] no CK ref ({ckKey}) — not comparing NIF")
        ElseIf Not File.Exists(bakedNifPath) Then
            Console.WriteLine($"  [NIF] the baked NIF is not on disk: {bakedNifPath}")
        Else
            Dim ckNif As New Nifcontent_Class_Manolo() : ckNif.Load_Manolo(ckBytes)
            Dim myBytes = File.ReadAllBytes(bakedNifPath)
            Dim myNif As New Nifcontent_Class_Manolo() : myNif.Load_Manolo(myBytes)

            ' ---- estructura NIF ----
            Console.WriteLine($"  [NIF/struct] bytes CK={ckBytes.Length} baked={myBytes.Length}  blocks CK={ckNif.Blocks.Count} baked={myNif.Blocks.Count}")
            If ckBytes.Length <> myBytes.Length Then noop.Add($"NIF byte-size CK={ckBytes.Length} vs baked={myBytes.Length} (framing/paths _2 — NO-OP)")
            Dim ckTypes = String.Join(",", ckNif.Blocks.GroupBy(Function(b) b.GetType().Name).OrderBy(Function(g) g.Key).Select(Function(g) $"{g.Key}x{g.Count()}"))
            Dim myTypes = String.Join(",", myNif.Blocks.GroupBy(Function(b) b.GetType().Name).OrderBy(Function(g) g.Key).Select(Function(g) $"{g.Key}x{g.Count()}"))
            If ckTypes <> myTypes Then
                real.Add($"NIF block-type histogram DIFF:{Environment.NewLine}      CK   ={ckTypes}{Environment.NewLine}      baked={myTypes}")
            Else
                Console.WriteLine($"  [NIF/struct] block-type histogram OK ({myNif.Blocks.Count} blocks)")
            End If
            Dim ckRoot = TryCast(ckNif.Blocks.FirstOrDefault(), NiflySharp.Blocks.NiAVObject)
            Dim myRoot = TryCast(myNif.Blocks.FirstOrDefault(), NiflySharp.Blocks.NiAVObject)
            If ckRoot IsNot Nothing AndAlso myRoot IsNot Nothing Then
                Dim ckR = $"{ckRoot.GetType().Name} '{ckRoot.Name?.String}' flags=0x{ckRoot.Flags_ui:X4}"
                Dim myR = $"{myRoot.GetType().Name} '{myRoot.Name?.String}' flags=0x{myRoot.Flags_ui:X4}"
                If ckR <> myR Then real.Add($"NIF root DIFF: CK[{ckR}] vs baked[{myR}]") Else Console.WriteLine($"  [NIF/struct] root OK: {myR}")
            End If

            ' ---- por shape ----
            Dim ckShapes = ckNif.NifShapes.ToList()
            Dim myShapes = myNif.NifShapes.ToList()
            Console.WriteLine($"  [NIF] CK shapes={ckShapes.Count}  baked shapes={myShapes.Count}")
            If ckShapes.Count <> myShapes.Count Then real.Add($"NIF shape count CK={ckShapes.Count} vs baked={myShapes.Count}")
            For Each cs In ckShapes
                Dim nm = If(cs.Name?.String, "")
                Dim ms = myShapes.FirstOrDefault(Function(s) String.Equals(If(s.Name?.String, ""), nm, StringComparison.OrdinalIgnoreCase))
                If ms Is Nothing Then real.Add($"shape '{nm}': PRESENT in CK, ABSENT in baked") : Continue For
                CompareShapeExhaustive(nm, cs, ckNif, ms, myNif, real, noop, normP)
            Next
            For Each ms In myShapes
                Dim nm = If(ms.Name?.String, "")
                If Not ckShapes.Any(Function(s) String.Equals(If(s.Name?.String, ""), nm, StringComparison.OrdinalIgnoreCase)) Then real.Add($"shape '{nm}': PRESENT in baked, ABSENT in CK")
            Next

            ' ---- ARBOL DE NODOS (huesos + nodos intermedios), emparejado por NOMBRE ----
            ' El comparador solo miraba shapes. Los NiNode (huesos del rig, sus transforms y sus flags) son
            ' parte del NIF y nunca se habian comparado en el barrido: una diferencia de bind o de jerarquia
            ' ahi es invisible en las metricas de vertices.
            Dim ckNodes = ckNif.Blocks.OfType(Of NiflySharp.Blocks.NiNode)().
                              GroupBy(Function(x) If(x.Name?.String, ""), StringComparer.OrdinalIgnoreCase).
                              ToDictionary(Function(g) g.Key, Function(g) g.First(), StringComparer.OrdinalIgnoreCase)
            Dim myNodes = myNif.Blocks.OfType(Of NiflySharp.Blocks.NiNode)().
                              GroupBy(Function(x) If(x.Name?.String, ""), StringComparer.OrdinalIgnoreCase).
                              ToDictionary(Function(g) g.Key, Function(g) g.First(), StringComparer.OrdinalIgnoreCase)
            For Each kv In ckNodes
                Dim mn As NiflySharp.Blocks.NiNode = Nothing
                If Not myNodes.TryGetValue(kv.Key, mn) Then
                    real.Add($"node '{kv.Key}': PRESENT in CK, ABSENT in baked")
                    Continue For
                End If
                ReflectDiffBlock($"node '{kv.Key}'", kv.Value, mn, ckNif, myNif, real, 1)
            Next
            For Each kv In myNodes
                If Not ckNodes.ContainsKey(kv.Key) Then real.Add($"node '{kv.Key}': PRESENT in baked, ABSENT in CK")
            Next

            ' ---- POSICION SKINNEADA, EN ESPACIO DE HUESO (lo que el motor realmente usa) ----
            ' NO se compone con el transform de NODO del NIF. Primer intento: use boneWorld ∘ skinToBone
            ' y dio diferencia en el 100 % de las shapes — no era un hallazgo, era la metrica mal elegida.
            ' En una malla SKINNEADA el transform del nodo del FaceGeom es INERTE: el juego re-skinea con el
            ' esqueleto del personaje, no con la jerarquia que trae el FaceGeom (y esta medido que el CK
            ' escribe rotaciones identidad en esos nodos mientras nosotros escribimos la matriz real).
            ' La metrica engine-faithful es Σ_b w_b · (skinToBone_b · v): si dos NIF coinciden en ESO,
            ' renderizan igual bajo el MISMO esqueleto, que es exactamente el caso.
            Try
                Dim ckSkin = SkinnedPositionsBoneSpace(ckNif)
                Dim mySkin = SkinnedPositionsBoneSpace(myNif)
                For Each kv In ckSkin
                    Dim other As List(Of System.Numerics.Vector3) = Nothing
                    If Not mySkin.TryGetValue(kv.Key, other) Then Continue For
                    If kv.Value.Count = 0 OrElse other.Count <> kv.Value.Count Then
                        Threading.Interlocked.Increment(SkinPosNoData)
                        Continue For
                    End If
                    Dim sr = MaxRmsVec(kv.Value, other)
                    Threading.Interlocked.Increment(SkinPosTotal)
                    If sr.Max = 0.0 Then Threading.Interlocked.Increment(SkinPosExact)
                    For hi = 0 To PosHistThresholds.Length - 1
                        If sr.Max > PosHistThresholds(hi) Then Threading.Interlocked.Increment(SkinPosHistCounts(hi))
                    Next
                    SyncLock PosHistThresholds
                        If sr.Max > SkinPosMax Then SkinPosMax = sr.Max : SkinPosWorst = $"0x{npcFormID:X8} '{kv.Key}'"
                    End SyncLock
                    If sr.Max > PosReportThreshold Then real.Add($"shape '{kv.Key}': SKINNED positions maxΔ={sr.Max:F4} RMS={sr.Rms:F4}")
                Next
            Catch ex As Exception
                ' Nunca degradar en silencio: si el skinning no se puede componer hay que saberlo.
                Threading.Interlocked.Increment(SkinPosNoData)
                Console.WriteLine($"  [SKIN] could not compare the skinned position: {ex.GetType().Name}: {ex.Message}")
            End Try
        End If

        ' ================= DDS (pixeles) — SSE: facetint _d · FO4: FaceCustomization _d/_msn/_s ==========
        If Not SkipDdsCompare Then
            ' UNA sola ruta para los DOS juegos, y SIEMPRE leyendo el .dds DE DISCO (ver CompareOnDiskDds):
            ' recomponer el facetint en memoria en vez de leer el archivo haria que el numero de SSE y el
            ' de FO4 no midieran lo mismo.
            CompareOnDiskDds(npcData, origin, fgL, npcFormID, isSse, real, noop)
        Else
            Console.WriteLine("  [DDS] pixel compare SKIPPED (no --ddscompare) — only the NIF was validated")
        End If

        ' ================= RESUMEN: REAL vs NO-OP =================
        If verbose Then
            Console.WriteLine($"  ---- REAL DIFFERENCES: {real.Count} ----")
            For Each d In real : Console.WriteLine($"    [REAL] {d}") : Next
            Console.WriteLine($"  ---- NO-OP DIFFERENCES (expected): {noop.Count} ----")
            For Each d In noop : Console.WriteLine($"    [noop] {d}") : Next
            Console.WriteLine($"======== END COMPARE 0x{npcFormID:X8}  (REAL={real.Count} NO-OP={noop.Count}) ========")
        End If
        Return (real, noop)
    End Function

    ''' <summary>True si dos rutas apuntan al MISMO directorio. Normaliza separador ('/' vs '\'), barra
    ''' final y mayusculas via GetFullPath. Si alguna no se puede resolver (ruta invalida) cae a una
    ''' comparacion textual tolerante en vez de tirar: esto alimenta un WARN, no una decision.</summary>
    Private Function SamePath(a As String, b As String) As Boolean
        Dim norm = Function(s As String) As String
                       If String.IsNullOrWhiteSpace(s) Then Return ""
                       Dim t = s.Trim()
                       Try
                           t = IO.Path.GetFullPath(t)
                       Catch
                           t = t.Replace("/"c, "\"c)
                       End Try
                       Return t.TrimEnd("\"c, "/"c)
                   End Function
        Return String.Equals(norm(a), norm(b), StringComparison.OrdinalIgnoreCase)
    End Function

    Private Function Fo4DdsRmsThreshold() As Double
        Dim t As Double = 2.0
        Dim raw = If(Environment.GetEnvironmentVariable("FGCMP_DDS_RMS"), "").Trim()
        If raw <> "" Then Double.TryParse(raw, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, t)
        Return t
    End Function

    ''' <summary>Canales que el bake FO4 escribe en FaceCustomization. MEDIDO sobre los BA2 vanilla+DLC
    ''' (4476 entries / 1489 NPCs): el CK hornea SIEMPRE los TRES, nunca un subconjunto —
    ''' <c>_d</c> 1024x1024 BC1_UNORM, <c>_msn</c> 1024x1024 BC1_UNORM, <c>_s</c> 512x512 BC5_UNORM.
    ''' El <c>_s</c> a MITAD de resolucion no es un error: la textura fuente de spec de las cabezas ya es
    ''' 512x512 (basemalehead_s, ghoul*head_s, piperhead_s... todas BC5 512), asi que con
    ''' Setting_FaceGenSpecularResolution=Inherit nuestro bake sale al mismo tamaño.</summary>
    Private ReadOnly Fo4DdsChannels As String() = {"_d", "_msn", "_s"}

    ''' <summary>SSE hornea UN solo artefacto de cara y SIN sufijo de canal:
    ''' <c>Textures\Actors\Character\FaceGenData\FaceTint\&lt;plugin&gt;\&lt;id&gt;.dds</c>. La cadena vacia ES el
    ''' nombre del canal; se rotula "facetint" para el log y se acumula bajo la clave "_d".</summary>
    Private ReadOnly SseDdsChannels As String() = {""}

    ''' <summary>Acumulador por canal de la comparacion de pixeles FO4. IMPRESCINDIBLE, no es un lujo:
    ''' RunSseCompareBatch redirige <c>Console.Out</c> a <c>TextWriter.Null</c> mientras corre cada NPC, asi que
    ''' TODO lo que CompareFo4FaceCustomizationDds imprime por NPC se pierde en el barrido. Sin este agregado el
    ''' barrido solo veria las categorias (los casos que pasan el umbral) y NUNCA los numeros — es decir, no se
    ''' podria saber si el RMS tipico es 0,3 o 1,9, solo que "no paso de 2". El resumen final lo imprime con
    ''' Console.Out ya restaurado.</summary>
    Private Class DdsChannelStats
        Public N As Integer
        Public SumRms As Double
        Public MaxRms As Double
        Public WorstNpc As UInteger
        Public SumByteExactPct As Double
        Public MxR As Double, MxG As Double, MxB As Double
        Public Absent As Integer, NoCkRef As Integer, DimMismatch As Integer, LooseRef As Integer, DecodeFail As Integer
        ' ALPHA agregado: el alpha por-NPC se imprime a Console.Out, que en el barrido va a TextWriter.Null ⇒
        ' sin este acumulador no queda registro del alpha ni siquiera en FO4, donde el bloque sí corre.
        ' AlphaMismatch es el conteo que importa (CK varía != nuestro varía); los dos CkVaria/MineVaria dan la
        ' POBLACIÓN, sin la cual "0 mismatches" no se puede distinguir de "el detector nunca tuvo nada que ver".
        Public SumRmsA As Double, MxA As Double
        Public AlphaMismatch As Integer, CkVariaCount As Integer, MineVariaCount As Integer
    End Class
    Private ReadOnly Fo4DdsStats As New Dictionary(Of String, DdsChannelStats)(StringComparer.Ordinal)
    Private Function DdsStat(suffix As String) As DdsChannelStats
        If Not Fo4DdsStats.ContainsKey(suffix) Then Fo4DdsStats(suffix) = New DdsChannelStats()
        Return Fo4DdsStats(suffix)
    End Function

    ''' <summary>True si el canal alpha de la textura NO es plano-opaco (hay al menos un pixel &lt; 0,98).
    ''' Propiedad de la IMAGEN (no un diff), asi que sirve aunque dos texturas no compartan dimensiones.
    ''' El umbral 0,98 (≈250/255) absorbe el ruido de bloque del codec BCn sin tragarse alpha real: el
    ''' unico caso del corpus vanilla (Valentine) tiene 24 % de px con alpha ≤ 128, muy por debajo.</summary>
    Private Function AlphaVaria(t As FaceTintCpuCompositor.DecodedTex) As Boolean
        If t Is Nothing OrElse t.Rgba8 Is Nothing Then Return False
        For i = 0 To (t.Width * t.Height) - 1
            If t.Unit(i * 4 + 3) < 0.98 Then Return True
        Next
        Return False
    End Function

    ''' <summary>Comparacion de PIXELES del artefacto REAL on-disk contra la referencia del CK, para LOS DOS
    ''' juegos, LEYENDO SIEMPRE EL .dds DE DISCO — nunca recomponiendo el facetint en memoria. Recomponer
    ''' valida el COMPOSITOR pero NO que el bake haya escrito ese resultado a disco, que es el artefacto que
    ''' consume el juego: un fallo de escritura, un codec mal aplicado o un naming equivocado quedarian
    ''' invisibles si se comparara contra el resultado recompuesto en vez del archivo real.
    '''
    ''' Diferencias legitimas que se respetan: FO4 hornea TRES canales (_d/_msn/_s) en
    ''' <c>FaceCustomization\</c>, SSE uno solo en <c>FaceGenData\FaceTint\</c> (sin sufijo de canal).
    ''' Las dimensiones NO se hardcodean: salen del decode y un desajuste es categoria REAL.</summary>
    Private Sub CompareOnDiskDds(npcData As NPC_Data, origin As String, fgL As UInteger, npcFormID As UInteger,
                                 isSse As Boolean, real As List(Of String), noop As List(Of String))
        ' Sufijo on-disk = la MISMA condicion que uso el bake (FaceGenBuilder.BakeFaceTextures): DebugMode
        ' escribe <id>_d_2.dds y release <id>_d.dds. NO se hardcodea ni se prueban los dos: --buildfacegen
        ' prende el Logger (DebugMode=True) y --ssecomparebatch lo apaga (False), asi que leer el naming
        ' equivocado seria leer el artefacto de OTRA corrida.
        Dim dbg = FO4_NPC_Manager.FaceGenBuilder.DebugMode
        Dim outDir As String, ckDir As String, channels As String()
        If isSse Then
            outDir = IO.Path.Combine(Config_App.Current.DataPath, "Textures", "Actors", "Character", "FaceGenData", "FaceTint", origin)
            ckDir = $"textures\actors\character\facegendata\facetint\{origin}\"
            channels = SseDdsChannels
        Else
            outDir = IO.Path.Combine(Config_App.Current.DataPath, "Textures", "Actors", "Character", "FaceCustomization", origin)
            ckDir = $"textures\actors\character\facecustomization\{origin}\"
            channels = Fo4DdsChannels
        End If
        Dim thr = Fo4DdsRmsThreshold()

        For Each suffix In channels
            ' El acumulador de SSE vive bajo "_d" (su unico canal) para no romper la tabla del reporte.
            Dim statKey = If(isSse, "_d", suffix)
            ' Etiqueta legible: en SSE el canal no tiene sufijo, asi que "[DDS/]" quedaria mudo.
            Dim chLabel = If(suffix = "", "facetint", suffix)
            Try
                Dim minePath = IO.Path.Combine(outDir, $"{fgL:X8}{suffix}{If(dbg, "_2", "")}.dds")
                If Not IO.File.Exists(minePath) Then
                    ' NO es necesariamente un defecto: el CK tampoco escribe slots si el material del shape no
                    ' es Face/FaceTint (ley RE CK 0x140ed9020). Se informa sin clasificar.
                    DdsStat(statKey).Absent += 1
                    Console.WriteLine($"  [DDS/{chLabel}] baked ABSENT ({minePath})")
                    Continue For
                End If

                Dim ckKey = ($"{ckDir}{fgL:X8}{suffix}.dds").ToLowerInvariant()
                Dim ckBytes = FilesDictionary_class.GetArchiveOriginalBytes(ckKey)
                Dim ckFromArchive = ckBytes IsNot Nothing AndAlso ckBytes.Length > 0
                If Not ckFromArchive Then ckBytes = FilesDictionary_class.GetBytes(ckKey)
                If ckBytes Is Nothing OrElse ckBytes.Length = 0 Then
                    DdsStat(statKey).NoCkRef += 1
                    Console.WriteLine($"  [DDS/{chLabel}] no CK ref ({ckKey})")
                    Continue For
                End If
                If Not ckFromArchive Then
                    DdsStat(statKey).LooseRef += 1
                    real.Add($"DDS {chLabel}: the CK ref did NOT come from a BA2/BSA (loose) — CIRCULAR comparison against an old bake of our own")
                End If

                Dim mineBytes = IO.File.ReadAllBytes(minePath)
                Dim mine = FaceTintCpuCompositor.DecodeDds(mineBytes)
                Dim ckd = FaceTintCpuCompositor.DecodeDds(ckBytes)
                If mine Is Nothing OrElse mine.Rgba8 Is Nothing OrElse ckd Is Nothing OrElse ckd.Rgba8 Is Nothing Then
                    DdsStat(statKey).DecodeFail += 1
                    Console.WriteLine($"  [DDS/{chLabel}] decode failed (mine={mine IsNot Nothing}, ck={ckd IsNot Nothing})")
                    Continue For
                End If
                ' ALPHA plano-vs-variable ANTES del abort por dimensiones. Es una propiedad de la IMAGEN,
                ' no un diff por-pixel, asi que no necesita que las dimensiones coincidan — y si se dejara
                ' despues del Continue For, el unico NPC del corpus con alpha real (Valentine 0x00002F24)
                ' quedaria SIN cubrir justo por dimensionar distinto: nuestro _d sale 2048 porque
                ' DLCUltraHighResolution reemplaza su head diffuse, y el ref del CK es 1024. Arreglar el bug
                ' y dejar el detector ciego para el caso que lo motivo no sirve de nada.
                Dim ckVaria = AlphaVaria(ckd), mineVaria = AlphaVaria(mine)
                If statKey = "_d" AndAlso ckVaria <> mineVaria Then
                    real.Add($"DDS {chLabel} ALPHA flat/varying does NOT match (CK varies={ckVaria}, ours varies={mineVaria}) — base alpha lost or invented")
                End If
                ' Los contadores de alpha se acumulan ACA, del mismo lado del abort por dimensiones que el
                ' chequeo que los produce. Si se acumularan abajo (junto al RMS) volveria el MISMO bug que motivo
                ' subir el chequeo: Valentine 0x00002F24 — el UNICO caso vanilla FO4 con alpha real — dimensiona
                ' 2048 vs 1024 del CK (DLCUltraHighResolution), sale por el Continue For, y el agregado de alpha
                ' quedaria en cero justo para el unico NPC que puede ejercerlo.
                Dim stA = DdsStat(statKey)
                If ckVaria Then stA.CkVariaCount += 1
                If mineVaria Then stA.MineVariaCount += 1
                If ckVaria <> mineVaria Then stA.AlphaMismatch += 1

                If mine.Width <> ckd.Width OrElse mine.Height <> ckd.Height Then
                    DdsStat(statKey).DimMismatch += 1
                    real.Add($"DDS {chLabel} DIMENSION mine={mine.Width}x{mine.Height} vs CK={ckd.Width}x{ckd.Height}")
                    Continue For
                End If

                ' maxΔ POR CANAL a proposito (la rama SSE colapsa los tres en uno): el _s del CK es BC5 = 2
                ' canales utiles, asi que un desvio que viva SOLO en B es informacion sobre el codec, no sobre
                ' el compose. Reportarlos separados deja que el dato lo muestre en vez de asumirlo.
                Dim n = mine.Width * mine.Height
                Dim ss As Double = 0, byteExact As Integer = 0
                Dim mxR As Double = 0, mxG As Double = 0, mxB As Double = 0
                ' ALPHA: se mide APARTE y NO entra al RMS. Dos razones: (a) el RMS historico es RGB-only, y
                ' meterle un cuarto canal rompe la comparabilidad con todo baseline previo; (b) el alpha del
                ' CK viaja verbatim desde el head diffuse (no se compone), asi que su desvio dice otra cosa
                ' que el desvio del compose. Sin esto el canal alpha NO se validaba en absoluto: el defecto
                ' "forzamos alpha opaca" convivio con --ddscompare dando PASS porque nadie lo miraba.
                Dim ssA As Double = 0, mxA As Double = 0
                ' DELTA MEDIA CON SIGNO (nuestro - CK) por canal. RMS y maxD son ABSOLUTOS y no distinguen
                ' "toda la imagen corrida un poquito" de "una region muy distinta": las dos pueden dar el mismo
                ' RMS. El signo separa un problema de gamma/tinte (offset uniforme) de ruido de codec. Estaba
                ' solo en la rama SSE; al unificar lo heredan los tres canales de FO4.
                Dim sR As Double = 0, sG As Double = 0, sB As Double = 0
                For i = 0 To n - 1
                    Dim vr = mine.Unit(i * 4) - ckd.Unit(i * 4)
                    Dim vg = mine.Unit(i * 4 + 1) - ckd.Unit(i * 4 + 1)
                    Dim vb = mine.Unit(i * 4 + 2) - ckd.Unit(i * 4 + 2)
                    sR += vr : sG += vg : sB += vb
                    Dim dr = Math.Abs(vr), dg = Math.Abs(vg), db = Math.Abs(vb)
                    Dim da = Math.Abs(mine.Unit(i * 4 + 3) - ckd.Unit(i * 4 + 3))
                    ss += dr * dr + dg * dg + db * db
                    ssA += da * da
                    If dr > mxR Then mxR = dr
                    If dg > mxG Then mxG = dg
                    If db > mxB Then mxB = db
                    If da > mxA Then mxA = da
                    If Math.Round(dr * 255) = 0 AndAlso Math.Round(dg * 255) = 0 AndAlso Math.Round(db * 255) = 0 Then byteExact += 1
                Next
                Dim rms = Math.Sqrt(ss / (3.0 * n)) * 255
                Dim rmsA = Math.Sqrt(ssA / n) * 255

                DdsCsvRows.Add(String.Format(Globalization.CultureInfo.InvariantCulture,
                    "0x{0:X8},{1},{2},{3},{4:F4},{5:F2},{6:F0},{7:F0},{8:F0},{9:F4},{10:F0},{11},{12},{13},{14},{15:F3},{16:F3},{17:F3},{18}",
                    npcFormID, CsvSafe(origin), CsvSafe(If(npcData?.EditorID, "")), chLabel,
                    rms, 100.0 * byteExact / n,
                    mxR * 255, mxG * 255, mxB * 255, rmsA, mxA * 255, ckVaria, mineVaria,
                    mine.Width, mine.Height,
                    sR / n * 255, sG / n * 255, sB / n * 255,
                    If(npcData IsNot Nothing AndAlso npcData.Record.ConfigurationFlagsFemale, "F", "M")))

                Dim st = DdsStat(statKey)
                st.N += 1
                st.SumRms += rms
                st.SumByteExactPct += 100.0 * byteExact / n
                If rms > st.MaxRms Then st.MaxRms = rms : st.WorstNpc = npcFormID
                If mxR > st.MxR Then st.MxR = mxR
                If mxG > st.MxG Then st.MxG = mxG
                If mxB > st.MxB Then st.MxB = mxB
                ' ALPHA por-pixel: SOLO esto necesita dimensiones iguales, asi que va aca. Los contadores
                ' plano/variable ya se acumularon arriba, antes del abort por dimensiones.
                st.SumRmsA += rmsA
                If mxA > st.MxA Then st.MxA = mxA

                Console.WriteLine($"  [DDS/{chLabel}] {mine.Width}x{mine.Height}  RMS={rms:F2}/255" &
                                  $"  maxΔ R={mxR * 255:F0} G={mxG * 255:F0} B={mxB * 255:F0}" &
                                  $"  meanΔ R={sR / n * 255:+0.00;-0.00} G={sG / n * 255:+0.00;-0.00} B={sB / n * 255:+0.00;-0.00}" &
                                  $"  ALPHA rms={rmsA:F2} maxΔ={mxA * 255:F0} (CK varies={ckVaria}, ours varies={mineVaria})" &
                                  $"  byte-exact px={byteExact}/{n} ({100.0 * byteExact / n:F1}%)" &
                                  $"  (mine={mineBytes.Length}b, CK={ckBytes.Length}b {If(ckFromArchive, "ARCHIVE", "LOOSE!")})")

                If rms > thr Then real.Add($"DDS {chLabel} RMS={rms:F2}/255 (>{thr:F2}) — revisar compose")
                If mineBytes.Length <> ckBytes.Length Then
                    noop.Add($"DDS {chLabel} byte-size mine={mineBytes.Length} vs CK={ckBytes.Length} (our codec vs the CK's — NO-OP)")
                End If
            Catch ex As Exception
                Console.WriteLine($"  [DDS/{chLabel}] compare failed: {ex.GetType().Name}: {ex.Message}")
            End Try
        Next
    End Sub

    ''' <summary>ETAPA 1 del diagnóstico de fidelidad del preview (<c>--headfidelity</c>). Agrega las filas
    ''' que <see cref="FO4_NPC_Manager.FaceGenBuildPipeline.CollectHeadFidelity"/> juntó durante el barrido.
    ''' <para>Lee así: la población SIN <c>CustomizationRemapNewBonesData</c> es el CONTROL — el bake mete el
    ''' body-weight en sus dos pasadas, se cancela, y max tiene que dar 0 EXACTO. Si no da 0, la medición
    ''' está mal y el resto del reporte no vale. La población CON la flag es la que mide la divergencia real
    ''' del preview.</para></summary>
    Private Sub ReportHeadFidelity()
        Dim rows = FO4_NPC_Manager.FaceGenBuildPipeline.GetHeadFidelityRows()
        Console.WriteLine()
        Console.WriteLine("======== HEAD FIDELITY (preview vs juego) ========")
        If rows.Count = 0 Then
            Console.WriteLine("  (no rows — no shape went through the bake with measuring enabled)")
            Return
        End If

        Dim report = Sub(label As String, subset As List(Of FO4_NPC_Manager.FaceGenBuildPipeline.HeadFidelityRow))
                         Console.WriteLine($"  ---- {label}: {subset.Count} shapes ----")
                         If subset.Count = 0 Then Return
                         Dim maxAll = subset.Max(Function(r) r.MaxD)
                         Dim rmsAll = Math.Sqrt(subset.Sum(Function(r) r.Rms * r.Rms * r.VertexCount) /
                                                Math.Max(1, subset.Sum(Function(r) CDbl(r.VertexCount))))
                         Console.WriteLine($"      max  = {maxAll:G6}")
                         Console.WriteLine($"      rms  = {rmsAll:G6}   (weighted by vertices)")
                         For Each th In {0.0, 0.005, 0.01, 0.02, 0.05, 0.1}
                             ' .Where(...).Count() y no .Count(pred): en VB `List.Count` es propiedad y gana
                             ' sobre la extensión de LINQ (BC32016).
                             Console.WriteLine($"      shapes with max > {th:F3} : {subset.Where(Function(r) r.MaxD > th).Count()}")
                         Next
                         Dim sb = subset.Sum(Function(r) CDbl(r.SingleBoneVerts))
                         Dim mb = subset.Sum(Function(r) CDbl(r.MultiBoneVerts))
                         Dim tot = Math.Max(1, sb + mb)
                         Console.WriteLine($"      vertices with ONE bone of the flat rig  : {sb:F0} ({100.0 * sb / tot:F2}%)")
                         Console.WriteLine($"      vertices with SEVERAL bones            : {mb:F0} ({100.0 * mb / tot:F2}%)")
                         Console.WriteLine("      worst 15 shapes:")
                         For Each r In subset.OrderByDescending(Function(x) x.MaxD).Take(15)
                             Console.WriteLine($"        0x{r.NpcFormID:X8} '{r.ShapeName}' max={r.MaxD:G6} rms={r.Rms:G6} verts={r.VertexCount}")
                         Next
                     End Sub

        Dim ctrl = rows.Where(Function(r) Not r.HasRemapFlag).ToList()
        Dim flagged = rows.Where(Function(r) r.HasRemapFlag).ToList()
        report("CONTROL - shapes WITHOUT CustomizationRemapNewBonesData (max MUST be 0)", ctrl)
        Console.WriteLine()
        report("MEASUREMENT - shapes WITH CustomizationRemapNewBonesData", flagged)
        Console.WriteLine()
        Dim ctrlMax = If(ctrl.Count > 0, ctrl.Max(Function(r) r.MaxD), 0.0)
        If ctrlMax > 0.0000001 Then
            Console.WriteLine($"  !!!! CONTROL BROKEN: the group without the flag gives max={ctrlMax:G6}, it should give 0.")
            Console.WriteLine("       The measurement is wrong; do NOT read the group with the flag.")
            Environment.ExitCode = 4
        Else
            Console.WriteLine("  control OK (max=0 in the group without the flag) => the number for the group with the flag is valid.")
        End If
        Console.WriteLine("======== END HEAD FIDELITY ========")
    End Sub

    Private Sub RunSseCompareBatch(pm As PluginManager, limit As Integer)
        ' GAME-AWARE: PluginManager.IsOfficialPlugin cubre LOS DOS motores (Fallout4.esm+DLC, Skyrim.esm+DLC, VR y cc*),
        ' así el barrido 100% vanilla+DLC corre igual en FO4 y SSE con el MISMO filtro (imprescindible para
        ' que baseline y post-fix sean comparables).
        Dim cands As New List(Of UInteger)()
        For Each kv As KeyValuePair(Of UInteger, PluginRecord) In pm.AllRecords
            Dim r = kv.Value
            If r Is Nothing OrElse r.Header.Signature <> "NPC_" Then Continue For
            Dim origin = pm.GetOriginatingPluginName(kv.Key)
            If Not PluginManager.IsOfficialPlugin(origin) Then Continue For
            Dim fgL = PluginManager.ToFaceGenLocalFormID(kv.Key)
            Dim ckKey = ($"meshes\actors\character\facegendata\facegeom\{origin}\{fgL:X8}.nif").ToLowerInvariant()
            ' SOLO del archive (ver CkNifRefBytes): un NIF NUESTRO suelto en el Data haria entrar al NPC como
            ' "candidato con referencia del CK" cuando la unica referencia es nuestra propia salida previa.
            Dim ckArch As Boolean
            Dim ckb = CkNifRefBytes(ckKey, ckArch)
            ' Length>0: GetBytes devuelve un array VACÍO (no Nothing) para claves sin archivo real — p.ej. NPCs
            ' templated/genéricos y el jugador que NO tienen FaceGeom horneado por el CK. Esos no son comparables.
            If ckb IsNot Nothing AndAlso ckb.Length > 0 Then cands.Add(kv.Key)
        Next
        Dim gameTagB = If(Config_App.Current.Game = Config_App.Game_Enum.Skyrim, "SSE", "FO4")
        Console.WriteLine($"[batch] {cands.Count} vanilla+DLC {gameTagB} NPCs with CK FaceGeom")
        ' INSTRUMENTACIÓN DE DIAGNÓSTICO: el barrido entero puede morir en silencio a mitad de camino
        ' (exit 127, sin excepción ni evento WER) con crecimiento MONÓTONO de memoria (605 MB → 4,6 GB a
        ' los 1100 NPCs), en índices DISTINTOS entre corridas ⇒ agotamiento de recursos, no un NPC concreto.
        ' RunVertexOutlierBatch ya tiene resume por CSV + centinela .cur ("el NPC que crasheó el proceso
        ' anterior"); este barrido agrega lo mismo. Ambos OFF por defecto (sin env var el comportamiento es
        ' idéntico):
        '   · orden ESTABLE de candidatos (no el orden de hash del Dictionary AllRecords) — imprescindible
        '     para que los tramos particionen el corpus sin solaparse ni dejar huecos. NO cambia ningún
        '     resultado agregado: las categorías se suman por NPC y por shape, que son order-independent.
        '   · FGCMP_SKIP=<n>: saltea los primeros n candidatos ⇒ con FGCMP_SKIP + limit se corre por tramos
        '     en procesos separados y se consolidan las tablas.
        cands = cands.OrderBy(Function(x) x).ToList()
        ' FGCMP_ONLY=<hex[,hex...]>: restringe el corpus a esos FormIDs. DIAGNOSTICO — sirve para partir el
        ' residuo por CLASE de NPC (sin morph / con morph .tri / con FMRS) y ver a cuál se le atribuye, que
        ' con el corpus completo queda promediado y es inobservable.
        ' Una corrida con FGCMP_ONLY NO es comparable contra el baseline: el corpus es otro. Se marca.
        Dim onlyRaw = If(Environment.GetEnvironmentVariable("FGCMP_ONLY"), "").Trim()
        If onlyRaw <> "" Then
            Dim want As New HashSet(Of UInteger)()
            For Each tok In onlyRaw.Split({","c, ";"c, " "c}, StringSplitOptions.RemoveEmptyEntries)
                Dim t = tok.Trim().TrimStart("0"c, "x"c, "X"c)
                Dim v As UInteger
                If UInteger.TryParse(If(t = "", "0", t), Globalization.NumberStyles.HexNumber,
                                     Globalization.CultureInfo.InvariantCulture, v) Then want.Add(v)
            Next
            Dim before = cands.Count
            cands = cands.Where(Function(x) want.Contains(x)).ToList()
            Console.WriteLine($"[batch] !!!! FGCMP_ONLY active: {cands.Count} of {before} candidates " &
                              $"({want.Count} FormIDs requested). DIAGNOSTIC RUN, NOT comparable against a baseline.")
            ' Un FormID pedido que no esta en el corpus es un error de la peticion, no un detalle: se lista.
            For Each v In want
                If Not cands.Contains(v) Then Console.WriteLine($"[batch]      requested 0x{v:X8} is NOT in the corpus (no CK FaceGeom / not official)")
            Next
        End If
        Dim skipN As Integer = 0
        Integer.TryParse(If(Environment.GetEnvironmentVariable("FGCMP_SKIP"), "").Trim(), skipN)
        If skipN > 0 Then
            cands = cands.Skip(skipN).ToList()
            Console.WriteLine($"[batch] FGCMP_SKIP={skipN} -> {cands.Count} restantes")
        End If
        If limit > 0 AndAlso cands.Count > limit Then cands = cands.Take(limit).ToList()
        Console.WriteLine($"[batch] CHUNK range=[{skipN}..{skipN + cands.Count - 1}] size={cands.Count}")

        ' categoría = string de la diff con shape-name y números removidos → agrupa el TIPO de diferencia.
        Dim catCount As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        Dim catNpcs As New Dictionary(Of String, HashSet(Of UInteger))(StringComparer.Ordinal)
        Dim catExample As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim rxNum = New System.Text.RegularExpressions.Regex("[-+]?[0-9]*\.?[0-9]+([eE][-+]?[0-9]+)?")
        Dim rxHex = New System.Text.RegularExpressions.Regex("0x[0-9A-Fa-f]+")
        Dim normCat = Function(d As String) As String
                          Dim s = rxHex.Replace(d, "0x#")
                          s = rxNum.Replace(s, "#")
                          ' quitar el nombre de shape entre comillas
                          s = System.Text.RegularExpressions.Regex.Replace(s, "'[^']*'", "'…'")
                          Return s
                      End Function

        SkipDdsCompare = Not DdsCompareRequested   ' barrido de NIF: sin el bloque DDS (recompone el facetint por NPC = costo dominante)
        ' Este barrido valida el NIF, no los pixeles: se saltea el trabajo de imagen del bake en AMBOS juegos.
        ' MEDIDO (sin esto): SSE ~237 NPC/min pero FO4 ~1,7 NPC/min = 15 h el barrido completo, porque
        ' FO4 compone Y encodea 3 canales a resolucion nativa (1024x1024+) por NPC contra el unico 512x512 de SSE.
        ' Ninguno de los dos flags cambia lo que el bake escribe en el NIF (ver sus docs) — validado por byte-diff.
        FO4_NPC_Manager.FaceGenBuilder.SkipDdsEncode = Not DdsCompareRequested
        FaceTintCpuCompositor.SkipPixelCompose = Not DdsCompareRequested
        Dim preBaked = If(Environment.GetEnvironmentVariable("FGCMP_PREBAKED"), "").Trim()
        If preBaked <> "" Then Console.WriteLine($"[batch] PREBAKED mode: comparing NIFs from '{preBaked}' (no bake)")
        Dim catDetail As New Dictionary(Of String, List(Of String))(StringComparer.Ordinal)
        ' Agregados del cubo NO-OP (ver el loop de abajo): mismas claves normalizadas que los REAL.
        Dim noopCount As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        Dim noopNpcs As New Dictionary(Of String, HashSet(Of UInteger))(StringComparer.Ordinal)
        Dim noopExample As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim okCount = 0, failCount = 0, processed = 0
        Dim presets As New Dictionary(Of UInteger, FO4_NPC_Manager.LooksmenuLoader.LooksmenuPreset)
        Dim ctx As New FO4_NPC_Manager.NpcRenderContext(pm)
        Dim mres As New FO4_NPC_Manager.NpcMaterialResolver(ctx, Function(raw As NPC_Data, fid As UInteger) raw)
        Dim savedOut = Console.Out
        ' FALLOS NUNCA SILENCIOSOS: cada fallo registra NPC + ruta + causa (nunca `failCount += 1` a
        ' secas ni un Catch que se traga la excepcion) — sin eso un "fail=2" no dice que NPCs son ni por que.
        ' No es cosmetico: un NPC que falla NO SE COMPARA, asi que no aparece en ninguna categoria. Si un NPC
        ' pasa de "diferente" a "explota", su categoria BAJA de conteo y el criterio "ninguna categoria sube"
        ' lo lee como una MEJORA. Los fallos silenciosos corrompen el criterio de aceptacion del barrido.
        Dim failures As New List(Of String)()
        Dim failByRoute As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
        ' EXCLUIDOS != FALLOS. Un NPC cuya RAZA no tiene FaceGen (RACE.DATA flags bit 0x2 claro) no es un
        ' fallo del bake: el motor tampoco le arma cabeza. Verificado por RE en LOS DOS binarios —
        ' CreationKit.exe 0x140AAE521-52B aborta el driver del bake, y Fallout4.exe 0x1406E22AB-B9 sale del
        ' armado de cabeza antes de las DOS ramas (FaceGeom 0x1406ED9F0 y fallback por head-parts 0x1406ED4D0).
        ' FaceGenBuilder ya lo marca con Skipped=True (FaceGenBuilder.vb, gate RaceSupportsFaceGen); lo que
        ' faltaba era que el batch lo LEYERA en vez de mirar solo Success.
        ' Van a su propio cubo, NO al corpus comparado: no se bakean ni se comparan, asi que no pueden
        ' aparecer en ninguna categoria. Sumarlos a okCount seria mentir sobre el tamaño del corpus.
        ' Medido sobre el corpus de referencia completo (todos los FaceGeom de los BA2 de los 7 masters,
        ' 1490 NPCs unicos): 1487 tienen la raza con el bit puesto y EXACTAMENTE 2 lo tienen claro
        ' (DLC05WorkshopArmorRack{Male,Female}01, raza DLC05ArmorRackRace 0x02706200). 94 de las 110 razas
        ' tienen el bit claro, pero sus NPCs no tienen FaceGeom horneado, asi que nunca entran a `cands`.
        Dim excluded As New List(Of String)()
        Dim recordExcluded = Sub(fid As UInteger, detail As String)
                                 Dim rec = pm.GetRecord(fid)
                                 Dim edid = If(rec IsNot Nothing AndAlso Not String.IsNullOrEmpty(rec.EditorID), rec.EditorID, "?")
                                 Dim line = $"0x{fid:X8} [{pm.GetOriginatingPluginName(fid)}] EDID='{edid}' :: {detail}"
                                 excluded.Add(line)
                                 Console.WriteLine($"[batch][EXCLUDED] {line}")
                             End Sub
        Dim recordFail = Sub(fid As UInteger, route As String, detail As String)
                             failCount += 1
                             failByRoute(route) = If(failByRoute.ContainsKey(route), failByRoute(route), 0) + 1
                             Dim rec = pm.GetRecord(fid)
                             Dim edid = If(rec IsNot Nothing AndAlso Not String.IsNullOrEmpty(rec.EditorID), rec.EditorID, "?")
                             Dim line = $"0x{fid:X8} [{pm.GetOriginatingPluginName(fid)}] EDID='{edid}' route={route} :: {detail}"
                             failures.Add(line)
                             ' Se imprime EN EL MOMENTO ademas de en el resumen: si el proceso muere a mitad
                             ' (este barrido ya murio por agotamiento de recursos), el fallo igual quedo en el log.
                             Console.WriteLine($"[batch][FAIL] {line}")
                         End Sub

        For Each fid In cands
            processed += 1
            Try
                Console.SetOut(IO.TextWriter.Null)   ' silenciar el ruido del bake+compare
                ' DIAGNOSTICO: FGCMP_PREBAKED=<dir> compara NIFs YA horneados (<dir>\facegeom\<origin>\<ID>.NIF)
                ' en vez de rehornear — mismo comparador, sin el costo del bake.
                Dim bakedPath As String
                If preBaked <> "" Then
                    bakedPath = IO.Path.Combine(preBaked, "facegeom", pm.GetOriginatingPluginName(fid),
                                                $"{PluginManager.ToFaceGenLocalFormID(fid):X8}.NIF")
                    If Not IO.File.Exists(bakedPath) Then
                        Console.SetOut(savedOut)
                        recordFail(fid, "prebaked-missing", $"'{bakedPath}' does not exist")
                        Continue For
                    End If
                Else
                    Dim res = FO4_NPC_Manager.FaceGenBuilder.BuildCharGen(fid, pm, presets, Nothing, AddressOf mres.ApplyShapeMaterialOverrides, willBePacked:=False)
                    ' Skipped: el bake decidio a proposito no emitir NIF (raza sin FaceGen / sin head parts).
                    ' Se contabiliza aparte ANTES del chequeo de Success — si no, cae en la rama de fallo.
                    If res IsNot Nothing AndAlso res.Skipped Then
                        Console.SetOut(savedOut)
                        recordExcluded(fid, $"summary='{res.Summary}'")
                        Continue For
                    End If
                    If res Is Nothing OrElse Not res.Success OrElse String.IsNullOrEmpty(res.OutputPath) Then
                        Console.SetOut(savedOut)
                        ' El Summary del propio BuildCharGen dice POR QUE fallo — es el dato util, no el booleano.
                        Dim why = If(res Is Nothing, "BuildCharGen returned Nothing",
                                     If(Not res.Success, $"Success=False summary='{res.Summary}'",
                                        $"OutputPath vacio (Success=True) summary='{res.Summary}'"))
                        recordFail(fid, "buildchargen", why)
                        Continue For
                    End If
                    bakedPath = res.OutputPath
                End If
                Dim rr = CompareBakedVsCk(pm, fid, bakedPath, verbose:=False)
                Console.SetOut(savedOut)
                okCount += 1
                ' EL CUBO NO-OP TAMBIEN SE AGREGA: toda diferencia clasificada como "esperada" (tamaños de
                ' byte por codec, sufijo sandbox, GrayscaleToPaletteScale inerte) se registra igual, no se
                ' descarta — "esperado" es una CLASIFICACION, no una licencia para no mostrarlo; ocultarla
                ' haria parecer exhaustiva a una tabla de REAL que no lo es.
                For Each d In rr.Noop
                    Dim ncat = normCat(d)
                    noopCount(ncat) = If(noopCount.ContainsKey(ncat), noopCount(ncat), 0) + 1
                    If Not noopNpcs.ContainsKey(ncat) Then noopNpcs(ncat) = New HashSet(Of UInteger)()
                    noopNpcs(ncat).Add(fid)
                    If Not noopExample.ContainsKey(ncat) Then noopExample(ncat) = d
                Next
                For Each d In rr.Real
                    Dim cat = normCat(d)
                    catCount(cat) = If(catCount.ContainsKey(cat), catCount(cat), 0) + 1
                    If Not catNpcs.ContainsKey(cat) Then catNpcs(cat) = New HashSet(Of UInteger)()
                    catNpcs(cat).Add(fid)
                    If Not catExample.ContainsKey(cat) Then catExample(cat) = d
                    ' DIAGNOSTICO: guarda FormID+EditorID+diff literal por categoría (para categorías chicas).
                    If Not catDetail.ContainsKey(cat) Then catDetail(cat) = New List(Of String)()
                    If catDetail(cat).Count < 200 Then
                        Dim rec = pm.GetRecord(fid)
                        catDetail(cat).Add($"0x{fid:X8} [{pm.GetOriginatingPluginName(fid)}] EDID='{If(rec IsNot Nothing, rec.EditorID, "?")}' :: {d}")
                    End If
                Next
            Catch ex As Exception
                Console.SetOut(savedOut)
                ' NUNCA tragarse la excepcion: tipo + mensaje + la primera linea del stack (barato y suele
                ' bastar para ubicar el sitio) — un `failCount += 1` a secas pierde la causa.
                Dim stk = If(ex.StackTrace, "").Split({Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)
                Dim at0 = If(stk.Length > 0, stk(0).Trim(), "<no stack>")
                recordFail(fid, "exception", $"{ex.GetType().Name}: {ex.Message} | {at0}")
            End Try
            If processed Mod 50 = 0 Then Console.WriteLine($"[batch] {processed}/{cands.Count}  ok={okCount} fail={failCount} excl={excluded.Count}")
        Next
        Console.SetOut(savedOut)

        Console.WriteLine($"======== BATCH SSE: {okCount} NPCs compared ({failCount} fail, {excluded.Count} excluded) ========")

        ' CONSERVACION DEL CORPUS. Todo candidato tiene que terminar en exactamente UNO de los tres cubos.
        ' Si no cierra, algun camino esta saliendo del loop sin contabilizar y el barrido es un verde falso:
        ' se marca fuerte y se cambia el ExitCode. Jamas se degrada a "habra sido un caso raro".
        If okCount + failCount + excluded.Count <> cands.Count Then
            Console.WriteLine($"  !!!! ACCOUNTING BROKEN: ok({okCount}) + fail({failCount}) + excl({excluded.Count}) = " &
                              $"{okCount + failCount + excluded.Count} != candidates({cands.Count}). INVALID RUN.")
            Environment.ExitCode = 3
        End If

        ' ==== EXCLUIDOS: no son fallos y NO invalidan la corrida, pero se listan enteros ====
        ' Su conteo SI es parte de la firma de la corrida: si cambia entre dos barridos, cambio el corpus
        ' efectivo y los conteos por categoria dejan de ser comparables igual que con failCount.
        If excluded.Count > 0 Then
            Console.WriteLine($"  ---- {excluded.Count} EXCLUDED (race with no FaceGen / no head parts): not baked, not compared, NOT failures ----")
            For Each ln In excluded : Console.WriteLine($"      {ln}") : Next
        End If

        ' ==== FALLOS: lista completa + veredicto de VALIDEZ de la corrida ====
        ' fail > 0 INVALIDA la comparacion contra un baseline: los NPCs que fallan no se comparan, asi que
        ' las categorias estan calculadas sobre un corpus MAS CHICO. Comparar conteos entre dos corridas con
        ' distinto failCount no es valido — una categoria puede "bajar" solo porque el NPC exploto. No se
        ' aborta (el barrido igual da informacion), pero se marca fuerte y se cambia el ExitCode para que un
        ' script que encadene corridas pueda frenar.
        If failCount > 0 Then
            ' ASCII puro en esta linea: la consola del CLI la degrada a '??' y es la linea que un script
            ' grepea para decidir si la corrida es valida.
            Console.WriteLine($"  !!!! {failCount} FAILURES -- effective corpus {okCount}/{cands.Count} (excl {excluded.Count}). RUN NOT COMPARABLE AGAINST A BASELINE.")
            Console.WriteLine($"      The per-category counts are NOT comparable against a baseline with a different failCount:")
            Console.WriteLine($"      an NPC going from 'different' to 'crashes' DROPS a category and looks like an improvement.")
            Console.WriteLine($"  ---- failures by path ----")
            For Each kv In failByRoute.OrderByDescending(Function(x) x.Value)
                Console.WriteLine($"      {kv.Value,5}  {kv.Key}")
            Next
            Console.WriteLine($"  ---- failures (full list: FormID + EditorID + plugin + cause) ----")
            For Each ln In failures : Console.WriteLine($"      {ln}") : Next
            Environment.ExitCode = 2
        Else
            Console.WriteLine($"  failures: 0 — corpus compared {okCount}/{cands.Count} (excl {excluded.Count}), counts comparable against a baseline with the SAME excluded count.")
        End If

        Console.WriteLine($"  REAL difference categories (sorted by # affected NPCs):")
        For Each kv In catNpcs.OrderByDescending(Function(x) x.Value.Count)
            Console.WriteLine($"    [{kv.Value.Count} NPCs / {catCount(kv.Key)} shapes] {kv.Key}")
            Console.WriteLine($"        e.g.: {catExample(kv.Key)}")
            ' DIAGNOSTICO: detalle FormID+EditorID por categoría cuando afecta a pocos NPCs.
            ' Umbral del desglose por NPC. Default 25 (comportamiento historico); FGCMP_DETAIL_MAX lo sube
            ' para poder ABRIR una categoria mediana sin tener que salir a buscar los casos a mano.
            Dim detailMax As Integer = 25
            Integer.TryParse(If(Environment.GetEnvironmentVariable("FGCMP_DETAIL_MAX"), "").Trim(), detailMax)
            If detailMax <= 0 Then detailMax = 25
            If kv.Value.Count <= detailMax AndAlso catDetail.ContainsKey(kv.Key) Then
                For Each ln In catDetail(kv.Key) : Console.WriteLine($"        -> {ln}") : Next
            End If
        Next
        ' ==== PIXELES FO4 (FaceCustomization D/N/S) — agregado por canal ====
        ' Se imprime aca porque durante el loop Console.Out esta redirigido a Null (ver DdsChannelStats).
        ' Sin --ddscompare el diccionario queda vacio y no se imprime nada.
        If Fo4DdsStats.Count > 0 Then
            Dim isSseB = (Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
            Dim scope = If(isSseB, "SSE FaceTint _d", "FO4 FaceCustomization _d/_msn/_s")
            Console.WriteLine($"  ---- PIXELES vs CK ({scope}, --ddscompare) ----")
            Console.WriteLine("     chan   N     RMS mean   RMS max   (worst NPC)  byte-exact mean    maxD R/G/B   absent noCK dim loose decFail")
            For Each suffix In Fo4DdsChannels
                If Not Fo4DdsStats.ContainsKey(suffix) Then Continue For
                Dim st = Fo4DdsStats(suffix)
                Dim meanRms = If(st.N > 0, st.SumRms / st.N, 0.0)
                Dim meanBe = If(st.N > 0, st.SumByteExactPct / st.N, 0.0)
                Console.WriteLine($"     {suffix,-6} {st.N,-5} {meanRms,9:F3}  {st.MaxRms,7:F2}  0x{st.WorstNpc:X8}   {meanBe,7:F1}%          " &
                                  $"{st.MxR * 255:F0}/{st.MxG * 255:F0}/{st.MxB * 255:F0}      " &
                                  $"{st.Absent} {st.NoCkRef} {st.DimMismatch} {st.LooseRef} {st.DecodeFail}")
            Next
            ' ---- ALPHA: tabla propia. No entra al RMS (que es RGB-only por compatibilidad con todo baseline
            ' previo) y su desvio dice OTRA cosa: el alpha viaja verbatim desde el head diffuse, no se compone.
            Console.WriteLine("     ---- ALPHA (outside the RMS: travels verbatim from the head diffuse, it is not composed) ----")
            Console.WriteLine("     chan   ALPHA rms mean   ALPHA maxD   CK varies ours varies   MISMATCH")
            For Each suffix In Fo4DdsChannels
                If Not Fo4DdsStats.ContainsKey(suffix) Then Continue For
                Dim st = Fo4DdsStats(suffix)
                Dim meanRmsA = If(st.N > 0, st.SumRmsA / st.N, 0.0)
                Console.WriteLine($"     {suffix,-6} {meanRmsA,14:F3}  {st.MxA * 255,10:F0}   {st.CkVariaCount,8} {st.MineVariaCount,14}  {st.AlphaMismatch,8}")
            Next
            Console.WriteLine("     MISMATCH>0 = alpha lost or invented with respect to the CK (REAL category).")
            Console.WriteLine("     'CK varies'=0 over the WHOLE corpus means the detector was never exercised:")
            Console.WriteLine("     0 mismatches there is NOT evidence that the alpha is preserved.")
            Console.WriteLine($"     (REAL category threshold: RMS > {Fo4DdsRmsThreshold():F2}/255 — env FGCMP_DDS_RMS)")
            If isSseB Then
                Console.WriteLine("     CODEC FLOOR: the SSE facetint container is always DXT5/BC3 on both sides,")
                Console.WriteLine("     so here the floor is the re-encode, not a format change like in FO4.")
            Else
                Console.WriteLine("     CODEC FLOOR: the CK stores _d and _msn as BC1 and we use BC3/BC5 by default,")
                Console.WriteLine("     so byte-exact=100% is unreachable; what matters is whether the RMS MOVES between commits.")
            End If
            ' GAME-AWARE, y NO es un detalle: --rawdds cambia el codec del DDS que el bake ESCRIBE A DISCO.
            ' · FO4: el comparador LEE ese archivo ⇒ nuestro lado sale sin comprimir ⇒ el piso de codec pasa a
            '   ser SOLO el del CK y los RMS bajan POR CONSTRUCCION (no comparables contra baselines BC3/BC5).
            ' · SSE: el comparador NO lee el archivo, RE-HORNEA con BakeFaceTintDds(...) sin pasar dxgiFormat,
            '   y ese Optional vale -1 = BC3 ⇒ nuestro lado sigue comprimido pase lo que pase con --rawdds.
            '   Los RMS de SSE SI son comparables contra baselines viejos.
            ' Imprimir el mismo cartel en los dos juegos era declarar una advertencia FALSA en SSE.
            If RawDdsRequested Then
                Console.WriteLine("     ##############################################################################")
                If isSseB Then
                    Console.WriteLine("     ## RUN WITH --rawdds: the SSE branch RE-BAKES the facetint (it does not read the DDS")
                    Console.WriteLine("     ## from disk) but it now passes the EXPLICIT format from the same setting the")
                    Console.WriteLine("     ## bake uses ⇒ our side comes out UNCOMPRESSED. The codec floor is ONLY the CK's,")
                    Console.WriteLine("     ## so these RMS are NOT comparable against BC3 baselines: they are lower")
                    Console.WriteLine("     ## BY CONSTRUCTION. (Separate corollary: the SSE branch validates the COMPOSITOR, not")
                    Console.WriteLine("     ## that the bake wrote that result to disk.)")
                Else
                    Console.WriteLine("     ## RUN WITH --rawdds: our DDS came out UNCOMPRESSED (B8G8R8A8) and the")
                    Console.WriteLine("     ## comparator READS them from disk. The codec floor above is ONLY the CK's.")
                    Console.WriteLine("     ## These RMS are NOT comparable against baselines made with BC3/BC5: they are")
                    Console.WriteLine("     ## lower BY CONSTRUCTION. They measure the COMPOSITOR, not the codec.")
                End If
                Console.WriteLine("     ##############################################################################")
            End If
            ' Volcado por NPC: sin esto una categoria de cientos de casos no se puede abrir (ver DdsCsvRows).
            If DdsCsvRows.Count > 0 Then
                Try
                    Dim csvPath = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"dds_{If(isSseB, "SSE", "FO4")}.csv")
                    Dim header = "formID,origin,editorID,channel,rms255,byteExactPct,maxR,maxG,maxB,rmsAlpha,maxAlpha,ckAlphaVaria,mineAlphaVaria,width,height,meanDR,meanDG,meanDB,sex"
                    IO.File.WriteAllLines(csvPath, New String() {header}.Concat(DdsCsvRows))
                    Console.WriteLine($"     [csv] per-NPC detail ({DdsCsvRows.Count} rows) -> {csvPath}")
                Catch ex As Exception
                    ' No degradar en silencio: si el volcado falla hay que saberlo, no quedarse sin el detalle.
                    Console.Error.WriteLine($"     [csv] the per-NPC dump FAILED: {ex.GetType().Name}: {ex.Message}")
                End Try
            End If
        End If

        Console.WriteLine("  " & EngineSkinWeightNormalization.StatsLine())
        Console.WriteLine("  ---- _faceBones + base bones RULE (BAKETEST3 prediction) ----")
        Console.WriteLine("    prediction: the maxD 0.02-0.05 band appears ONLY in shapes whose rig has base bones.")
        Dim popBase = RigBaseDrift + RigBaseClean, popNoBase = RigEyeDrift + RigEyeClean
        Console.WriteLine($"    population  WITH base bones : {popBase} shapes  (drift>0={RigBaseDrift}  exact={RigBaseClean})")
        Console.WriteLine($"    population  NO base bones   : {popNoBase} shapes  (drift>0={RigEyeDrift}  exact={RigEyeClean})")
        Console.WriteLine($"    in the BAND 0.02-0.05       : with base={BandBase}   without base={BandNoBase}")
        ' Un veredicto solo vale si el instrumento PUDO dar el resultado contrario. Dos formas de no poder:
        ' (a) una clase vacia => el clasificador no separa nada; (b) la banda vacia => la prediccion no se
        ' ejerce ni una vez. En ambos casos esto es NO APLICABLE, no "refutada".
        If popBase = 0 OrElse popNoBase = 0 Then
            Dim vacia = If(popBase = 0, "WITH base bones", "WITHOUT base bones")
            Console.WriteLine($"    => NOT APPLICABLE: the class '{vacia}' came out EMPTY; the classifier does not discriminate in this corpus.")
            ' MEDIDO, y vale la pena dejarlo escrito porque invalida las DOS lecturas previas:
            ' el clasificador es degenerado en AMBOS motores, cada uno para el lado contrario.
            '   SSE : 0 shapes CON huesos base  (20977 SIN)  -> ademas 0 shapes en la banda
            '   FO4 : 0 shapes SIN huesos base  (17121 CON)  -> la banda cae entera del lado 'con base'
            ' En FO4 eso hace que "la banda solo aparece con huesos base" sea VACUAMENTE cierta: no hay
            ' ningun shape que pudiera haberla falsado. Ni confirmacion ni refutacion: SIN MEDIR.
            If popNoBase = 0 Then
                Console.WriteLine("       ALL shapes have base bones => the prediction is VACUOUSLY true here;")
                Console.WriteLine("       no shape could have falsified it. It does NOT count as a confirmation.")
            Else
                Console.WriteLine("       NO shape has base bones => the rule presupposes a rig this corpus does not have.")
            End If
        ElseIf BandBase + BandNoBase = 0 Then
            Console.WriteLine("    => NOT APPLICABLE: NO shape fell in the 0.02-0.05 band; the prediction was never put to the test.")
        ElseIf BandNoBase > 0 Then
            Console.WriteLine($"    => REFUTED in this corpus: {BandNoBase} shapes in the band WITHOUT base bones.")
            For Each sm In BandNoBaseSamples : Console.WriteLine($"      contraejemplo: {sm}") : Next
        Else
            Console.WriteLine($"    => CONSISTENT in this corpus: ALL {BandBase} shapes in the band have base bones.")
        End If
        Console.WriteLine("  ---- SCOPE of conditional rules (0 = demonstrably inert in this corpus) ----")
        Console.WriteLine($"    channels dropped by weight outside [-1,1]    : {FO4_NPC_Manager.NpcMorphResolver.DroppedOutOfRangeChannels}")
        For Each sm In FO4_NPC_Manager.NpcMorphResolver.DroppedWeightSamples : Console.WriteLine($"      weight: {sm}") : Next
        Console.WriteLine($"    times the LerpFmrs clamp changed the value    : {FO4_NPC_Manager.FaceBonePoseBuilder.ClampHits}")
        For Each sm In FO4_NPC_Manager.FaceBonePoseBuilder.ClampSamples : Console.WriteLine($"      clamp: {sm}") : Next
        Console.WriteLine("  ---- PER-VERTEX (the metric that discriminates this law) ----")
        Console.WriteLine($"    vertices compared={VertTotal}  EXACT={VertExact}  ({(If(VertTotal > 0, 100.0 * VertExact / VertTotal, 0.0)).ToString("F2", Globalization.CultureInfo.InvariantCulture)}%)")
        Dim ulpLbl = {"exact", "<=0.5ulp", "<=1ulp", "<=2ulp", "<=4ulp", ">4ulp"}
        For bi = 0 To 5
            Console.WriteLine($"    residuo {ulpLbl(bi),-9} : {UlpBins(bi)}")
        Next
        Console.WriteLine("  ---- POSITION maxD HISTOGRAM (shapes over threshold; one run, all thresholds) ----")
        For hi = 0 To PosHistThresholds.Length - 1
            Console.WriteLine($"    shapes with maxD > {PosHistThresholds(hi).ToString("F3", Globalization.CultureInfo.InvariantCulture)} : {PosHistCounts(hi)}")
        Next
        Console.WriteLine("  ---- POSITION EXACTNESS (threshold-independent) ----")
        Console.WriteLine($"    shapes compared={ShapePosTotal}  BYTE-EXACT (maxΔ=0)={ShapePosExact}  ({(If(ShapePosTotal > 0, 100.0 * ShapePosExact / ShapePosTotal, 0.0)).ToString("F2", Globalization.CultureInfo.InvariantCulture)}%)")
        ' ==== CATEGORIAS NO-OP: clasificadas como esperadas, pero SE MUESTRAN ====
        ' No mostrarlas hacia que la tabla de REAL pareciera la lista completa de diferencias. Lo es de
        ' las diferencias que el comparador juzga defectos; NO de las diferencias que existen.
        Console.WriteLine($"  NO-OP difference categories (classified as expected — listed anyway): {noopNpcs.Count}")
        For Each kv In noopNpcs.OrderByDescending(Function(x) x.Value.Count)
            Console.WriteLine($"    [{kv.Value.Count} NPCs / {noopCount(kv.Key)} casos] {kv.Key}")
            Console.WriteLine($"        e.g.: {noopExample(kv.Key)}")
        Next

        ' ---- COBERTURA DEL BARRIDO REFLECTIVO (todo el NIF, campo por campo) ----
        Console.WriteLine("  ---- COVERAGE of the REFLECTIVE diff (blocks + sub-blocks + every field) ----")
        Console.WriteLine($"    blocks compared : {ReflectBlocksCompared}")
        Console.WriteLine($"    fields compared : {ReflectFieldsCompared}")
        If ReflectFieldsCompared = 0 Then
            Console.WriteLine("    !!!! NOT A SINGLE field was compared by reflection: the net was never exercised, do not read as PASS.")
        End If
        Console.WriteLine("    ---- EXCLUSIONS (explicit, with a reason) ----")
        For Each kv In ReflectSkipReasons
            Dim hits = 0
            ReflectSkippedProps.TryGetValue(kv.Key, hits)
            Console.WriteLine($"      {kv.Key} ({hits} times): {kv.Value}")
        Next
        Dim volHits = 0
        ReflectSkippedProps.TryGetValue("(collection >5000: count only)", volHits)
        Console.WriteLine($"      collections >5000 elements ({volHits} times): only the COUNT was compared;")
        Console.WriteLine("        the content is the geometry arrays, compared separately with a tolerance")
        Console.WriteLine("        numeric (maxD / RMS / ULP bins) instead of exact bit equality.")
        Console.WriteLine("      block indices (NiBlockRef.Index): NEVER compared — the CK's emission order")
        Console.WriteLine("        differs from ours in 97% of the NIFs, so every index differs by")
        Console.WriteLine("        construction. What is compared is the ref's TARGET (type + name), which is the semantic part.")

        ' ---- POSICION SKINNEADA: la que decide lo que se VE ----
        Console.WriteLine("  ---- SKINNED POSITION (world = boneWorld or skinToBone, weighted by weights) ----")
        Console.WriteLine($"    shapes compared={SkinPosTotal}  EXACT (maxΔ=0)={SkinPosExact}  ({(If(SkinPosTotal > 0, 100.0 * SkinPosExact / SkinPosTotal, 0.0)).ToString("F2", Globalization.CultureInfo.InvariantCulture)}%)")
        For hi = 0 To PosHistThresholds.Length - 1
            Console.WriteLine($"    shapes with maxD > {PosHistThresholds(hi).ToString("F3", Globalization.CultureInfo.InvariantCulture)} : {SkinPosHistCounts(hi)}")
        Next
        Console.WriteLine($"    worst: {SkinPosMax.ToString("G6", Globalization.CultureInfo.InvariantCulture)}  at {SkinPosWorst}")
        Console.WriteLine($"    shapes with no comparable skinning data (no match or different count): {SkinPosNoData}")
        If SkinPosTotal = 0 Then
            Console.WriteLine("    !!!! NO shape could be compared skinned: the metric was NOT exercised, do not read it as PASS.")
        End If
        Console.WriteLine($"    posThresh used for the positions category = {PosReportThreshold.ToString("F4", Globalization.CultureInfo.InvariantCulture)}")
        Console.WriteLine($"======== END BATCH ({catNpcs.Count} distinct categories) ========")
    End Sub

    ''' <summary>BATCH game-aware (--vertexbatch): bakea TODOS los NPC_ vanilla (IsOfficialPlugin = vanilla+DLC+cc,
    ''' sin mods) con FaceGeom horneado por el CK y mide, por NPC, la mayor diferencia de POSICIÓN de vértice
    ''' (maxΔ + RMS) sobre la peor shape emparejada por nombre. Reporta el máximo global, la distribución por
    ''' umbral y el ranking de outliers. Sirve para AMBOS juegos (FO4 y SSE, según --game) — solo geometría de
    ''' posición, agnóstico de facetint/DDS. La ref CK sale del FilesDictionary (BA2/loose).</summary>
    Private Sub RunVertexOutlierBatch(pm As PluginManager, limit As Integer, csvPath As String)
        Dim inv = System.Globalization.CultureInfo.InvariantCulture
        Dim gameTag = If(Config_App.Current.Game = Config_App.Game_Enum.Skyrim, "SSE", "FO4")
        If String.IsNullOrEmpty(csvPath) Then csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"vbatch_{gameTag}.csv")
        Dim curPath = csvPath & ".cur"

        Dim cands As New List(Of UInteger)()
        For Each kv As KeyValuePair(Of UInteger, PluginRecord) In pm.AllRecords
            Dim r = kv.Value
            If r Is Nothing OrElse r.Header.Signature <> "NPC_" Then Continue For
            Dim origin = pm.GetOriginatingPluginName(kv.Key)
            If Not PluginManager.IsOfficialPlugin(origin) Then Continue For
            Dim fgL = PluginManager.ToFaceGenLocalFormID(kv.Key)
            Dim ckKey = ($"meshes\actors\character\facegendata\facegeom\{origin}\{fgL:X8}.nif").ToLowerInvariant()
            ' SOLO del archive (ver CkNifRefBytes): un NIF NUESTRO suelto en el Data haria entrar al NPC como
            ' "candidato con referencia del CK" cuando la unica referencia es nuestra propia salida previa.
            Dim ckArch As Boolean
            Dim ckb = CkNifRefBytes(ckKey, ckArch)
            ' Length>0: GetBytes devuelve un array VACÍO (no Nothing) para claves sin archivo real — p.ej. NPCs
            ' templated/genéricos y el jugador que NO tienen FaceGeom horneado por el CK. Esos no son comparables.
            If ckb IsNot Nothing AndAlso ckb.Length > 0 Then cands.Add(kv.Key)
        Next
        cands = cands.OrderBy(Function(x) x).ToList()   ' orden estable para resume determinista
        If limit > 0 AndAlso cands.Count > limit Then cands = cands.Take(limit).ToList()
        Console.WriteLine($"[vbatch {gameTag}] {cands.Count} vanilla NPCs with CK FaceGeom  csv={csvPath}")

        ' ----- resume: leer CSV existente + marcar el NPC que crasheó el proceso anterior (sentinel .cur) -----
        Dim done As New HashSet(Of UInteger)()
        If File.Exists(csvPath) Then
            For Each ln In File.ReadAllLines(csvPath)
                If ln.StartsWith("fid", StringComparison.OrdinalIgnoreCase) OrElse ln.Trim() = "" Then Continue For
                Dim f = ln.Split(","c)(0).Trim()
                Dim v As UInteger
                If UInteger.TryParse(f, Globalization.NumberStyles.HexNumber, inv, v) Then done.Add(v)
            Next
        End If
        If File.Exists(curPath) Then
            Dim crashedHex = File.ReadAllText(curPath).Trim()
            Dim cv As UInteger
            If UInteger.TryParse(crashedHex, Globalization.NumberStyles.HexNumber, inv, cv) AndAlso Not done.Contains(cv) Then
                File.AppendAllText(csvPath, $"{cv:X8},,,,0,0,CRASH" & Environment.NewLine)
                done.Add(cv)
                Console.WriteLine($"[vbatch {gameTag}] NPC 0x{cv:X8} crashed the previous process -> marked CRASH, skipped")
            End If
            Try : File.Delete(curPath) : Catch : End Try
        End If
        If Not File.Exists(csvPath) Then File.WriteAllText(csvPath, "fid,origin,shape,maxD,rms,verts,status" & Environment.NewLine)

        Dim remaining = cands.Where(Function(f) Not done.Contains(f)).ToList()
        Console.WriteLine($"[vbatch {gameTag}] already done={done.Count}  remaining={remaining.Count}")

        Dim presets As New Dictionary(Of UInteger, FO4_NPC_Manager.LooksmenuLoader.LooksmenuPreset)
        Dim savedOut = Console.Out
        Dim processed = 0
        For Each fid In remaining
            processed += 1
            ' sentinel: escribir el fid ANTES de procesarlo. Si el proceso muere (crash nativo no atrapable),
            ' el .cur sobrevive y el próximo arranque lo marca CRASH y lo saltea.
            Try : File.WriteAllText(curPath, $"{fid:X8}") : Catch : End Try
            Dim line As String = Nothing
            Try
                Console.SetOut(IO.TextWriter.Null)   ' silenciar el ruido del bake
                ' Resolver FRESCO por NPC: reusar un NpcMaterialResolver/NpcRenderContext entre bakes acumula
                ' estado y CORROMPE la geometría de NPCs posteriores (medido: outlier 0x1995C daba 3.46 en batch
                ' compartido vs 0.033 con resolver fresco). El path --list ya crea uno por NPC — lo replicamos.
                Dim ctx As New FO4_NPC_Manager.NpcRenderContext(pm)
                Dim mres As New FO4_NPC_Manager.NpcMaterialResolver(ctx, Function(raw As NPC_Data, fid2 As UInteger) raw)
                Dim res = FO4_NPC_Manager.FaceGenBuilder.BuildCharGen(fid, pm, presets, Nothing, AddressOf mres.ApplyShapeMaterialOverrides, willBePacked:=False)
                Console.SetOut(savedOut)
                Dim origin = pm.GetOriginatingPluginName(fid)
                If res Is Nothing OrElse Not res.Success OrElse String.IsNullOrEmpty(res.OutputPath) OrElse Not File.Exists(res.OutputPath) Then
                    line = $"{fid:X8},{origin},,,0,0,SKIP"   ' sin FaceGen (criatura/robot) o bake falló
                Else
                    Dim fgL = PluginManager.ToFaceGenLocalFormID(fid)
                    Dim ckKey = ($"meshes\actors\character\facegendata\facegeom\{origin}\{fgL:X8}.nif").ToLowerInvariant()
                    Dim ckArchV As Boolean
                    Dim ckBytes = CkNifRefBytes(ckKey, ckArchV)
                    If ckBytes Is Nothing OrElse ckBytes.Length = 0 Then
                        line = $"{fid:X8},{origin},,,0,0,NOCK"   ' NPC sin FaceGeom vanilla (templated/genérico/jugador)
                    Else
                        ' --- CK load (referencia vanilla) en su propio try para localizar el EndOfStream ---
                        Dim ckNif As Nifcontent_Class_Manolo = Nothing
                        Try
                            ckNif = New Nifcontent_Class_Manolo() : ckNif.Load_Manolo(ckBytes)
                        Catch exck As Exception
                            line = $"{fid:X8},{origin},CKLEN{ckBytes.Length},,0,0,EXC_CK" : GoTo persist
                        End Try
                        ' --- baked load (nuestro output on-disk) en su propio try ---
                        Dim myNif As Nifcontent_Class_Manolo = Nothing
                        Dim bakedBytes = File.ReadAllBytes(res.OutputPath)
                        Try
                            myNif = New Nifcontent_Class_Manolo() : myNif.Load_Manolo(bakedBytes)
                        Catch exbk As Exception
                            line = $"{fid:X8},{origin},BAKEDLEN{bakedBytes.Length},,0,0,EXC_BAKED" : GoTo persist
                        End Try
                        Dim myShapes = myNif.NifShapes.ToList()
                        Dim wMax As Double = 0, wRms As Double = 0, wVerts As Integer = 0, wShape As String = ""
                        For Each cs In ckNif.NifShapes.ToList()
                            Dim nm = If(cs.Name?.String, "")
                            Dim ms = myShapes.FirstOrDefault(Function(s) String.Equals(If(s.Name?.String, ""), nm, StringComparison.OrdinalIgnoreCase))
                            If ms Is Nothing Then Continue For
                            Dim cg = ShapeGeometryFactory.[For](cs, ckNif), mg = ShapeGeometryFactory.[For](ms, myNif)
                            Dim cvp = cg.GetVertexPositions(), mvp = mg.GetVertexPositions()
                            If cvp Is Nothing OrElse mvp Is Nothing OrElse cvp.Count = 0 OrElse cvp.Count <> mvp.Count Then Continue For
                            Dim pr = MaxRmsVec(cvp, mvp)
                            If pr.Max > wMax Then wMax = pr.Max : wRms = pr.Rms : wShape = nm : wVerts = cvp.Count
                        Next
                        If wShape = "" Then
                            line = $"{fid:X8},{origin},,,0,0,NOMATCH"
                        Else
                            line = $"{fid:X8},{origin},{wShape},{wMax.ToString("F5", inv)},{wRms.ToString("F5", inv)},{wVerts},OK"
                        End If
                    End If
                End If
            Catch ex As Exception
                Console.SetOut(savedOut)
                line = $"{fid:X8},{pm.GetOriginatingPluginName(fid)},{ex.GetType().Name},,0,0,EXC"
            End Try
persist:
            ' persistir el resultado (append+flush) y limpiar el sentinel
            Try : File.AppendAllText(csvPath, line & Environment.NewLine) : Catch : End Try
            Try : File.Delete(curPath) : Catch : End Try
            If processed Mod 100 = 0 Then Console.WriteLine($"[vbatch {gameTag}] {processed}/{remaining.Count} (total done={done.Count + processed}/{cands.Count})")
        Next
        Console.SetOut(savedOut)

        ' ----- reporte (lee el CSV COMPLETO — incluye lo hecho en corridas anteriores) -----
        ReportVertexBatchCsv(csvPath, gameTag, cands.Count)
        Console.WriteLine("VBATCH_COMPLETE")
    End Sub

    ''' <summary>Lee el CSV del vertexbatch y emite el reporte agregado: máximo global de posición, distribución
    ''' por umbral, top-40 outliers y peor maxΔ por shape (nombre). Solo cuenta filas status=OK.</summary>
    Private Sub ReportVertexBatchCsv(csvPath As String, gameTag As String, totalCands As Integer)
        Dim inv = System.Globalization.CultureInfo.InvariantCulture
        Dim rows As New List(Of (Fid As UInteger, Origin As String, Shape As String, MaxD As Double, Rms As Double, Verts As Integer))()
        Dim nSkip = 0, nExc = 0, nCrash = 0, nOther = 0, nRows = 0
        For Each ln In File.ReadAllLines(csvPath)
            If ln.StartsWith("fid", StringComparison.OrdinalIgnoreCase) OrElse ln.Trim() = "" Then Continue For
            nRows += 1
            Dim p = ln.Split(","c)
            If p.Length < 7 Then Continue For
            Dim status = p(6).Trim()
            Select Case status
                Case "OK"
                    Dim fid As UInteger, mx As Double, rm As Double, vt As Integer
                    UInteger.TryParse(p(0), Globalization.NumberStyles.HexNumber, inv, fid)
                    Double.TryParse(p(3), Globalization.NumberStyles.Float, inv, mx)
                    Double.TryParse(p(4), Globalization.NumberStyles.Float, inv, rm)
                    Integer.TryParse(p(5), vt)
                    rows.Add((fid, p(1), p(2), mx, rm, vt))
                Case "SKIP", "NOCK", "NOMATCH" : nSkip += 1
                Case "EXC" : nExc += 1
                Case "CRASH" : nCrash += 1
                Case Else : nOther += 1
            End Select
        Next

        Console.WriteLine($"======== VERTEX BATCH {gameTag}: {rows.Count} NPCs compared (OK)  |  skip/no-facegen={nSkip} exc={nExc} crash={nCrash} others={nOther}  |  candidates={totalCands} csv-rows={nRows} ========")
        If rows.Count = 0 Then Console.WriteLine("  (no comparable OK rows)") : Return
        Dim sorted = rows.OrderByDescending(Function(x) x.MaxD).ToList()
        Dim g = sorted(0)
        Console.WriteLine($"  MAX vertex diff GLOBAL: maxΔ={g.MaxD.ToString("F4", inv)}  (NPC 0x{g.Fid:X8} [{g.Origin}] shape '{g.Shape}' RMS={g.Rms.ToString("F4", inv)} verts={g.Verts})")
        Console.WriteLine($"  mean(maxΔ per NPC)={rows.Average(Function(x) x.MaxD).ToString("F4", inv)}  median={sorted(sorted.Count \ 2).MaxD.ToString("F4", inv)}")
        For Each t In {0.01, 0.05, 0.1, 0.25, 0.5, 1.0, 2.0}
            Dim c = rows.Where(Function(x) x.MaxD > t).Count()
            Console.WriteLine($"    NPCs with maxΔ > {t.ToString("F2", inv)}: {c} ({(100.0 * c / rows.Count).ToString("F1", inv)}%)")
        Next
        Console.WriteLine($"  ---- TOP 40 OUTLIERS (by position maxΔ) ----")
        For Each x In sorted.Take(40)
            Console.WriteLine($"    0x{x.Fid:X8} [{x.Origin}] maxΔ={x.MaxD.ToString("F4", inv)} RMS={x.Rms.ToString("F4", inv)}  shape '{x.Shape}' ({x.Verts}v)")
        Next
        ' peor maxΔ por shape-name (usando la peor-shape de cada NPC)
        Dim shapeWorst As New Dictionary(Of String, (MaxD As Double, Fid As UInteger))(StringComparer.OrdinalIgnoreCase)
        For Each x In rows
            Dim prev As (MaxD As Double, Fid As UInteger) = Nothing
            If Not shapeWorst.TryGetValue(x.Shape, prev) OrElse x.MaxD > prev.MaxD Then shapeWorst(x.Shape) = (x.MaxD, x.Fid)
        Next
        Console.WriteLine($"  ---- Worst maxΔ by SHAPE (name, over each NPC's worst-shape) ----")
        For Each kv In shapeWorst.OrderByDescending(Function(k) k.Value.MaxD).Take(25)
            Console.WriteLine($"    '{kv.Key}': maxΔ={kv.Value.MaxD.ToString("F4", inv)} (0x{kv.Value.Fid:X8})")
        Next
        Console.WriteLine($"======== END VERTEX BATCH {gameTag} ========")
    End Sub

    ''' <summary>Umbral de reporte de la categoria "positions" (--posthresh). Default = el historico 0,05.</summary>
    Private DdsCompareRequested As Boolean = False
    ' --rawdds efectivo en ESTA corrida. Se refleja aca para que el reporte agregado del DDS pueda marcar
    ' que el piso de codec de la corrida es SOLO el del CK (el nuestro no existe si horneamos sin comprimir).
    Private RawDdsRequested As Boolean = False
    ''' <summary>Una fila por (NPC, canal) del compare de DDS. EXISTE PORQUE EL AGREGADO NO ALCANZA: la
    ''' categoria "242 NPCs con RMS > umbral" no dice QUIENES son — la salida por NPC va a TextWriter.Null
    ''' dentro del loop del batch, asi que una categoria de cientos de casos quedaba imposible de abrir.
    ''' Misma familia que "fail=N sin identificar el NPC". Se vuelca al terminar; agrupar/ordenar se hace
    ''' offline sobre el CSV, sin re-correr el barrido.</summary>
    Private DdsCsvRows As New List(Of String)

    ''' <summary>Hash TES4 (el de la BSA de Skyrim) de una ruta, como UInt64. RÉPLICA EXACTA de
    ''' <c>Ba2_Bsa_Library.BsaWriter.BASTes4Hashing</c> (que es <c>Friend</c> y no se ve desde acá) —
    ''' incluida la LUT de extensiones. Es SÓLO para el diagnóstico <c>--shapeorder</c>: se usa para probar
    ''' si el orden de shapes del CK corresponde al recorrido de un contenedor indexado por este hash.
    ''' Si alguna vez se necesita en producción, exponer el de la librería en vez de duplicar esto.</summary>
    Private Function Crc1003F(bytes As Byte()) As UInteger
        Dim crc As ULong = 0UL
        If bytes IsNot Nothing Then
            For i = 0 To bytes.Length - 1
                crc = (crc * &H1003FUL + CULng(bytes(i))) And &HFFFFFFFFUL
            Next
        End If
        Return CUInt(crc)
    End Function

    Private Function Tes4DirHash(path As String) As Byte()
        path = If(path, "").Replace("/"c, "\"c).Trim("\"c).ToLowerInvariant()
        If String.IsNullOrEmpty(path) Then path = "."
        Dim b = Text.Encoding.Latin1.GetBytes(path)
        Dim last As Byte = If(b.Length >= 1, b(b.Length - 1), CByte(0))
        Dim last2 As Byte = If(b.Length >= 2, b(b.Length - 2), CByte(0))
        Dim first As Byte = If(b.Length >= 1, b(0), CByte(0))
        Dim length As Byte = CByte(Math.Min(b.Length, 255))
        Dim crc As UInteger = 0UI
        If b.Length > 3 Then
            Dim sliceLen = b.Length - 3
            Dim tmp(sliceLen - 1) As Byte
            Array.Copy(b, 1, tmp, 0, sliceLen)
            crc = Crc1003F(tmp)
        End If
        Dim outB(7) As Byte
        outB(0) = last : outB(1) = last2 : outB(2) = length : outB(3) = first
        Buffer.BlockCopy(BitConverter.GetBytes(crc), 0, outB, 4, 4)
        Return outB
    End Function

    Private Function Tes4HashOf(path As String) As ULong
        Dim p = If(path, "").Replace("/"c, "\"c).ToLowerInvariant()
        Dim posSlash = p.LastIndexOf("\"c)
        If posSlash >= 0 Then p = p.Substring(posSlash + 1)
        Dim stem = p, ext = ""
        Dim dot = p.LastIndexOf("."c)
        If dot >= 0 Then stem = p.Substring(0, dot) : ext = p.Substring(dot)
        If stem.Length = 0 OrElse stem.Length >= 260 OrElse ext.Length >= 16 Then Return 0UL
        Dim h = Tes4DirHash(stem)
        Dim crcBase = BitConverter.ToUInt32(h, 4)
        crcBase = CUInt((CULng(crcBase) + CULng(Crc1003F(Text.Encoding.Latin1.GetBytes(ext)))) And &HFFFFFFFFUL)
        Buffer.BlockCopy(BitConverter.GetBytes(crcBase), 0, h, 4, 4)
        Dim lut = New String() {"", ".nif", ".kf", ".dds", ".wav", ".adp"}
        Dim ix = Array.IndexOf(lut, ext)
        If ix >= 0 Then
            Dim f = CInt(h(3)), l = CInt(h(0)), l2 = CInt(h(1))
            f = (f + 32 * (ix And &HFC)) And &HFF
            l = (l + ((ix And &HFE) << 6)) And &HFF
            l2 = (l2 + (ix << 7)) And &HFF
            h(3) = CByte(f) : h(0) = CByte(l) : h(1) = CByte(l2)
        End If
        Return BitConverter.ToUInt64(h, 0)
    End Function

    ''' <summary>Neutraliza comas y comillas para que un EditorID raro no corra las columnas del CSV.</summary>
    Private Function CsvSafe(s As String) As String
        If String.IsNullOrEmpty(s) Then Return ""
        Return s.Replace(","c, ";"c).Replace(""""c, "'"c)
    End Function
    Private PosReportThreshold As Double = 0.05

    ''' <summary>Tally global de shapes comparados por posicion, para reportar cuantos son BYTE-EXACTOS
    ''' (maxD = 0 exacto). Es la cifra que discrimina de verdad entre ramas: los conteos por categoria
    ''' dependen del umbral, y "byte-exacto" no depende de ningun umbral.</summary>
    Private ShapePosTotal As Integer = 0
    Private ShapePosExact As Integer = 0

    ''' <summary>Histograma de shapes por maxD de posicion, para reportar el efecto REAL a varios umbrales
    ''' en UNA sola corrida (el grueso del drift del neutral en FO4 vive en 0,02-0,05, bajo el umbral
    ''' historico de 0,05, asi que un solo umbral no alcanza para decidir nada).</summary>
    Private ReadOnly PosHistThresholds As Double() = {0.0, 0.005, 0.01, 0.02, 0.03, 0.05, 0.1, 0.25}
    Private PosHistCounts As Integer() = New Integer(7) {}

    ''' <summary>Mismo tally pero sobre la posicion SKINNEADA (world = boneWorld ∘ skinToBone, promediada por
    ''' pesos), no sobre el vertice crudo del archivo.
    ''' <para>POR QUE HACE FALTA: el tally de arriba compara los vertices TAL COMO ESTAN ALMACENADOS. Dos
    ''' NIF pueden tener vertices crudos distintos y renderizar IGUAL (si el bind compensa), y al reves:
    ''' vertices identicos con binds distintos renderizan DISTINTO. Lo que el jugador ve es esto, no aquello.
    ''' Sin esta metrica el barrido no podia distinguir esos dos casos.</para></summary>
    Private SkinPosTotal As Integer = 0
    Private SkinPosExact As Integer = 0
    Private SkinPosHistCounts As Integer() = New Integer(7) {}
    Private SkinPosMax As Double = 0
    Private SkinPosWorst As String = ""
    Private SkinPosNoData As Integer = 0

    ''' <summary>PREDICCION A FALSAR (experimento BAKETEST3, separacion 25/25): la banda 0,02-0,05 aparece
    ''' SOLO en shapes cuyo rig contiene huesos BASE (HEAD / Head_skin / Neck*); los de ojos, cuyo rig solo
    ''' declara skin_bone_{L,R}_Eye, driftean 0 exacto. Tabla 2x2 sobre los 17.121 shapes del corpus.</summary>
    Private RigBaseDrift As Integer = 0     ' con huesos base  Y  maxD > 0
    Private RigBaseClean As Integer = 0     ' con huesos base  Y  maxD = 0
    Private RigEyeDrift As Integer = 0      ' SIN huesos base  Y  maxD > 0
    Private RigEyeClean As Integer = 0      ' SIN huesos base  Y  maxD = 0
    ''' <summary>La prediccion NO es sobre "maxD > 0" sino sobre la BANDA 0,02-0,05. Contar drift>0 como
    ''' contraejemplo es un ERROR DE INSTRUMENTO: en SSE dio 17.706 "contraejemplos" cuando el histograma de
    ''' posiciones de ESA MISMA corrida reportaba 0 shapes por encima de 0,010 — o sea CERO shapes en la banda,
    ''' con lo cual la prediccion no fue puesta a prueba ni una sola vez. Estos son los contadores que
    ''' corresponden a la prediccion real; los de arriba quedan como contexto, no como veredicto.</summary>
    Private BandBase As Integer = 0         ' con huesos base  Y  maxD en [0,02 ; 0,05]
    Private BandNoBase As Integer = 0       ' SIN huesos base  Y  maxD en [0,02 ; 0,05]  <- contraejemplos REALES
    Private ReadOnly BandNoBaseSamples As New List(Of String)
    ''' <summary>Muestras de drift>0 sin huesos base. Se imprimen con precision suficiente para que un valor
    ''' chico-pero-no-cero NUNCA se renderice como "0,00000" (el F5 anterior hacia aparecer como byte-exacto
    ''' a shapes que si driftean; el contador estaba bien, lo que engañaba era el formato).</summary>
    Private ReadOnly RigEyeDriftSamples As New List(Of String)

    ''' <summary>True si el rig del shape declara algun hueso BASE. Nombres del rig real, no heuristica de
    ''' nombre de shape: es justo lo que la prediccion dice que separa.</summary>
    Private Function RigHasBaseBones(nif As Nifcontent_Class_Manolo, shp As NiflySharp.INiShape) As Boolean
        Try
            Dim wrap As New NifRenderableShape(nif, shp, 0)
            For Each b In wrap.ShapeBones
                Dim n = If(b?.Name?.String, "").ToLowerInvariant()
                If n = "head" OrElse n = "head_skin" OrElse n.StartsWith("neck") Then Return True
            Next
        Catch
        End Try
        Return False
    End Function

    ''' <summary>Metricas POR VERTICE. "Shape byte-exacto" es un AND sobre todos sus vertices, asi que
    ''' satura y NO discrimina esta ley (medido aparte: la simulacion IDEAL tampoco produce shapes
    ''' byte-exactos nuevos — los unicos exactos son los de ojos, que ya tenian eps=0). Lo que si
    ''' discrimina es la fraccion de VERTICES exactos y como se reparte el residuo en ULP de half.</summary>
    Private VertTotal As Long = 0
    Private VertExact As Long = 0
    ''' <summary>Bins de |CK-nuestro| medido en ULP de half a la magnitud de la propia coordenada:
    ''' 0 (exacto), &lt;=0,5, &lt;=1, &lt;=2, &lt;=4, &gt;4.</summary>
    Private UlpBins As Long() = New Long(5) {}

    ''' <summary>ULP de half (10 bits de mantisa) a la magnitud <paramref name="v"/>, con piso en el
    ''' subnormal mas chico (2^-24). Es la unidad natural del residuo: los pesos de skin vienen
    ''' cuantizados a half, asi que el error irreducible vive a esa escala.</summary>
    Private Function HalfUlp(v As Double) As Double
        Dim a = Math.Abs(v)
        If a < 6.103515625E-05 Then Return 5.9604644775390625E-08
        Return Math.Pow(2, Math.Floor(Math.Log(a, 2)) - 10)
    End Function

    Private Sub CompareShapeExhaustive(nm As String, cs As NiflySharp.INiShape, ckNif As Nifcontent_Class_Manolo,
                                       ms As NiflySharp.INiShape, myNif As Nifcontent_Class_Manolo,
                                       real As List(Of String), noop As List(Of String), normP As Func(Of String, String))
        Dim cg = ShapeGeometryFactory.[For](cs, ckNif), mg = ShapeGeometryFactory.[For](ms, myNif)
        Dim cvp = cg.GetVertexPositions(), mvp = mg.GetVertexPositions()
        If cvp.Count <> mvp.Count Then
            real.Add($"shape '{nm}': vertex count CK={cvp.Count} vs baked={mvp.Count}")
            Return
        End If
        Dim n = cvp.Count
        Dim line As New System.Text.StringBuilder($"    '{nm}' verts={n}")

        ' posiciones
        Dim pr = MaxRmsVec(cvp, mvp) : line.Append($"  pos[RMS={pr.Rms:F4} max={pr.Max:F4}]")
        Threading.Interlocked.Increment(ShapePosTotal)
        If pr.Max = 0.0 Then Threading.Interlocked.Increment(ShapePosExact)
        ' Tabla 2x2 de la prediccion _faceBones + huesos base (ver RigBaseDrift).
        Dim hasBase = RigHasBaseBones(ckNif, cs)
        ' Banda de la prediccion. Inclusiva en ambos extremos; es la unica particion que puede
        ' FALSAR la regla. Fuera de la banda un shape no dice nada ni a favor ni en contra.
        Dim inBand = (pr.Max >= 0.02 AndAlso pr.Max <= 0.05)
        If inBand Then
            If hasBase Then
                Threading.Interlocked.Increment(BandBase)
            Else
                Threading.Interlocked.Increment(BandNoBase)
                SyncLock BandNoBaseSamples
                    If BandNoBaseSamples.Count < 25 Then BandNoBaseSamples.Add($"{nm} maxD={pr.Max:G6}")
                End SyncLock
            End If
        End If
        If hasBase Then
            If pr.Max > 0.0 Then Threading.Interlocked.Increment(RigBaseDrift) Else Threading.Interlocked.Increment(RigBaseClean)
        Else
            If pr.Max > 0.0 Then
                Threading.Interlocked.Increment(RigEyeDrift)
                SyncLock RigEyeDriftSamples
                    ' G6 (no F5): un maxD de 4e-6 se veia como "0,00000" y parecia byte-exacto.
                    If RigEyeDriftSamples.Count < 25 Then RigEyeDriftSamples.Add($"{nm} maxD={pr.Max:G6}")
                End SyncLock
            Else
                Threading.Interlocked.Increment(RigEyeClean)
            End If
        End If
        ' Tally POR VERTICE (exactos + reparto del residuo en ULP de half).
        For vi = 0 To n - 1
            Dim dx = Math.Abs(CDbl(cvp(vi).X) - CDbl(mvp(vi).X))
            Dim dy = Math.Abs(CDbl(cvp(vi).Y) - CDbl(mvp(vi).Y))
            Dim dz = Math.Abs(CDbl(cvp(vi).Z) - CDbl(mvp(vi).Z))
            Threading.Interlocked.Increment(VertTotal)
            If dx = 0.0 AndAlso dy = 0.0 AndAlso dz = 0.0 Then
                Threading.Interlocked.Increment(VertExact)
                Threading.Interlocked.Increment(UlpBins(0))
            Else
                Dim r = Math.Max(Math.Max(dx / HalfUlp(cvp(vi).X), dy / HalfUlp(cvp(vi).Y)), dz / HalfUlp(cvp(vi).Z))
                Dim b As Integer
                If r <= 0.5 Then
                    b = 1
                ElseIf r <= 1.0 Then
                    b = 2
                ElseIf r <= 2.0 Then
                    b = 3
                ElseIf r <= 4.0 Then
                    b = 4
                Else
                    b = 5
                End If
                Threading.Interlocked.Increment(UlpBins(b))
            End If
        Next
        For hi = 0 To PosHistThresholds.Length - 1
            If pr.Max > PosHistThresholds(hi) Then Threading.Interlocked.Increment(PosHistCounts(hi))
        Next
        If pr.Max > PosReportThreshold Then real.Add($"shape '{nm}': positions maxΔ={pr.Max:F4} RMS={pr.Rms:F4}")

        ' triangulos / index
        Dim ctr = cg.GetTriangles(), mtr = mg.GetTriangles()
        If ctr.Count <> mtr.Count Then
            real.Add($"shape '{nm}': triangle count CK={ctr.Count} vs baked={mtr.Count}")
            line.Append($"  tris[CK={ctr.Count}!=baked={mtr.Count}]")
        Else
            Dim triDiff = 0
            For i = 0 To ctr.Count - 1
                If ctr(i).V1 <> mtr(i).V1 OrElse ctr(i).V2 <> mtr(i).V2 OrElse ctr(i).V3 <> mtr(i).V3 Then triDiff += 1
            Next
            line.Append($"  tris={ctr.Count}{(If(triDiff = 0, "=", $"!({triDiff})"))}")
            If triDiff > 0 Then real.Add($"shape '{nm}': {triDiff}/{ctr.Count} triangles with differing indices")
        End If

        ' normals / tangents / bitangents
        If cg.HasNormals AndAlso mg.HasNormals Then
            Dim nr = MaxRmsVec(cg.GetNormals(), mg.GetNormals()) : line.Append($"  nrm[max={nr.Max:F4}]")
            If nr.Max > 0.02 Then real.Add($"shape '{nm}': normals maxΔ={nr.Max:F4}")
        ElseIf cg.HasNormals <> mg.HasNormals Then
            real.Add($"shape '{nm}': HasNormals CK={cg.HasNormals} vs baked={mg.HasNormals}")
        End If
        If cg.HasTangents AndAlso mg.HasTangents Then
            Dim tr = MaxRmsVec(cg.GetTangents(), mg.GetTangents()) : line.Append($"  tan[max={tr.Max:F4}]")
            If tr.Max > 0.02 Then real.Add($"shape '{nm}': tangents maxΔ={tr.Max:F4}")
            Dim br = MaxRmsVec(cg.GetBitangents(), mg.GetBitangents()) : line.Append($"  bit[max={br.Max:F4}]")
            If br.Max > 0.02 Then real.Add($"shape '{nm}': bitangents maxΔ={br.Max:F4}")
        ElseIf cg.HasTangents <> mg.HasTangents Then
            real.Add($"shape '{nm}': HasTangents CK={cg.HasTangents} vs baked={mg.HasTangents}")
        End If

        ' UVs
        If cg.HasUVs AndAlso mg.HasUVs Then
            Dim cu = cg.GetUVs(), mu = mg.GetUVs()
            Dim uMax As Double = 0
            For i = 0 To Math.Min(cu.Count, mu.Count) - 1
                uMax = Math.Max(uMax, Math.Max(Math.Abs(cu(i).U - mu(i).U), Math.Abs(cu(i).V - mu(i).V)))
            Next
            line.Append($"  uv[max={uMax:F5}]")
            If uMax > 0.0005 Then real.Add($"shape '{nm}': UVs maxΔ={uMax:F5}")
        ElseIf cg.HasUVs <> mg.HasUVs Then
            real.Add($"shape '{nm}': HasUVs CK={cg.HasUVs} vs baked={mg.HasUVs}")
        End If

        ' vertex colors
        If cg.HasVertexColors AndAlso mg.HasVertexColors Then
            Dim cc = cg.GetVertexColors(), mc = mg.GetVertexColors()
            Dim cMax As Double = 0
            For i = 0 To Math.Min(cc.Count, mc.Count) - 1
                cMax = Math.Max(cMax, Math.Max(Math.Abs(cc(i).R - mc(i).R), Math.Max(Math.Abs(cc(i).G - mc(i).G), Math.Max(Math.Abs(cc(i).B - mc(i).B), Math.Abs(cc(i).A - mc(i).A)))))
            Next
            line.Append($"  vcol[max={cMax:F4}]")
            If cMax > 0.004 Then real.Add($"shape '{nm}': vertex colors maxΔ={cMax:F4}")
        ElseIf cg.HasVertexColors <> mg.HasVertexColors Then
            real.Add($"shape '{nm}': HasVertexColors CK={cg.HasVertexColors} vs baked={mg.HasVertexColors}")
        End If

        ' bone indices / weights
        Try
            Dim cs2 = cg.GetSkinning(), ms2 = mg.GetSkinning()
            If cs2.BoneWeights IsNot Nothing OrElse ms2.BoneWeights IsNot Nothing Then
                If cs2.WeightsPerVertex <> ms2.WeightsPerVertex Then real.Add($"shape '{nm}': WeightsPerVertex CK={cs2.WeightsPerVertex} vs baked={ms2.WeightsPerVertex}")
                Dim wMax As Double = 0, idxDiff As Integer = 0
                If cs2.BoneWeights IsNot Nothing AndAlso ms2.BoneWeights IsNot Nothing Then
                    Dim ln = Math.Min(cs2.BoneWeights.Length, ms2.BoneWeights.Length)
                    For i = 0 To ln - 1 : wMax = Math.Max(wMax, Math.Abs(CDbl(CSng(cs2.BoneWeights(i))) - CDbl(CSng(ms2.BoneWeights(i))))) : Next
                End If
                If cs2.BoneIndices IsNot Nothing AndAlso ms2.BoneIndices IsNot Nothing Then
                    Dim ln = Math.Min(cs2.BoneIndices.Length, ms2.BoneIndices.Length)
                    For i = 0 To ln - 1 : If cs2.BoneIndices(i) <> ms2.BoneIndices(i) Then idxDiff += 1
                    Next
                End If
                line.Append($"  bw[max={wMax:F4}] bidx[dif={idxDiff}]")
                If wMax > 0.004 Then real.Add($"shape '{nm}': bone weights maxΔ={wMax:F4}")
                If idxDiff > 0 Then real.Add($"shape '{nm}': {idxDiff} distinct bone-index slots (note: could be bone-list order; verify by name)")
            End If
        Catch
        End Try

        ' VertexDesc + bounds
        Dim cbts = TryCast(cs, NiflySharp.Blocks.BSTriShape), mbts = TryCast(ms, NiflySharp.Blocks.BSTriShape)
        If cbts IsNot Nothing AndAlso mbts IsNot Nothing Then
            If cbts.VertexDesc.Value <> mbts.VertexDesc.Value Then real.Add($"shape '{nm}': VertexDesc CK=0x{cbts.VertexDesc.Value:X16} vs baked=0x{mbts.VertexDesc.Value:X16}") Else line.Append($"  vdesc=OK")
            Dim bc = (New OpenTK.Mathematics.Vector3d(cbts.Bounds.Center.X - mbts.Bounds.Center.X, cbts.Bounds.Center.Y - mbts.Bounds.Center.Y, cbts.Bounds.Center.Z - mbts.Bounds.Center.Z)).Length
            Dim br = Math.Abs(cbts.Bounds.Radius - mbts.Bounds.Radius)
            line.Append($"  bounds[dc={bc:F3} dr={br:F3}]")
            If bc > 0.05 OrElse br > 0.05 Then real.Add($"shape '{nm}': bounds centerΔ={bc:F3} radiusΔ={br:F3}")
        End If

        ' texture-set slots
        Dim cts = GetTexSet(ckNif, cs), mts = GetTexSet(myNif, ms)
        If cts IsNot Nothing AndAlso mts IsNot Nothing AndAlso cts.Textures IsNot Nothing AndAlso mts.Textures IsNot Nothing Then
            Dim slots = Math.Max(cts.Textures.Count, mts.Textures.Count)
            For si = 0 To slots - 1
                Dim cp = If(si < cts.Textures.Count, cts.Textures(si)?.Content, "")
                Dim mp = If(si < mts.Textures.Count, mts.Textures(si)?.Content, "")
                If String.IsNullOrEmpty(cp) AndAlso String.IsNullOrEmpty(mp) Then Continue For
                Dim cN = normP(cp), mN = normP(mp)
                If cN = mN Then Continue For
                ' base igual salvo sufijo _2 sandbox → NO-OP
                Dim cB = cN.Replace("_2.dds", ".dds"), mB = mN.Replace("_2.dds", ".dds")
                If cB = mB Then
                    noop.Add($"shape '{nm}' texslot[{si}]: _2 sandbox suffix (my='{mp}' ck='{cp}' — NO-OP)")
                Else
                    real.Add($"shape '{nm}' texslot[{si}]: my='{mp}' ck='{cp}'")
                End If
            Next
        End If

        ' shader / material inline (COLORES, tints, flags, params)
        CompareShaderExhaustive(nm, cs, ckNif, ms, myNif, real, noop, line)

        ' alpha property
        CompareAlphaExhaustive(nm, cs, ckNif, ms, myNif, real)

        ' ---- BARRIDO REFLECTIVO de la shape y de TODOS sus sub-bloques ----
        ' Lo de arriba es la lista de campos "que sabemos que importan". Esto es la red que atrapa lo que
        ' NO esta en esa lista: cualquier propiedad de la shape, del shader, del texture set, del alpha o
        ' de la cadena de skinning que difiera y que nadie penso en mirar.
        ReflectDiffBlock($"shape '{nm}'", cs, ms, ckNif, myNif, real, 0)
        ReflectDiffBlock($"shape '{nm}'.shader", ckNif.GetShader(cs), myNif.GetShader(ms), ckNif, myNif, real, 1)
        ReflectDiffBlock($"shape '{nm}'.texset", GetTexSet(ckNif, cs), GetTexSet(myNif, ms), ckNif, myNif, real, 1)
        ReflectSubBlockPair($"shape '{nm}'.skinInstance", cs.SkinInstanceRef, ms.SkinInstanceRef, ckNif, myNif, real)
        ReflectSubBlockPair($"shape '{nm}'.alphaProp", cs.AlphaPropertyRef, ms.AlphaPropertyRef, ckNif, myNif, real)

        Console.WriteLine(line.ToString())
    End Sub

    ''' <summary>Resuelve dos refs a bloque y los diffea reflectivamente. Los indices de bloque NO se
    ''' comparan (el orden de emision difiere por construccion): se compara el CONTENIDO del destino.</summary>
    Private Sub ReflectSubBlockPair(path As String, ra As Object, rb As Object,
                                    ckNif As Nifcontent_Class_Manolo, myNif As Nifcontent_Class_Manolo,
                                    real As List(Of String))
        Try
            Dim ia As Integer = -1, ib As Integer = -1
            If ra IsNot Nothing Then ia = CInt(ra.GetType().GetProperty("Index").GetValue(ra))
            If rb IsNot Nothing Then ib = CInt(rb.GetType().GetProperty("Index").GetValue(rb))
            Dim oa = If(ia >= 0 AndAlso ia < ckNif.Blocks.Count, ckNif.Blocks(ia), Nothing)
            Dim ob = If(ib >= 0 AndAlso ib < myNif.Blocks.Count, myNif.Blocks(ib), Nothing)
            If oa Is Nothing AndAlso ob Is Nothing Then Return
            ReflectDiffBlock(path, oa, ob, ckNif, myNif, real, 1)
        Catch
        End Try
    End Sub

    ''' <summary>Diff a fondo del BSLightingShaderProperty inline de un shape emparejado: shader-type, flags
    ''' SSPF1/2, todos los escalares (Alpha/Glossiness/Specular/Emissive/Softlight/Subsurface/Rimlight/
    ''' Backlight/Fresnel/GrayscaleToPaletteScale/…) y TODOS los colores (SpecularColor, EmissiveColor,
    ''' SkinTintColor, HairTintColor) + UV. Acumula cada diferencia en real.</summary>
    Private Sub CompareShaderExhaustive(nm As String, cs As NiflySharp.INiShape, ckNif As Nifcontent_Class_Manolo,
                                        ms As NiflySharp.INiShape, myNif As Nifcontent_Class_Manolo,
                                        real As List(Of String), noop As List(Of String), line As System.Text.StringBuilder)
        Dim cl = TryCast(ckNif.GetShader(cs), NiflySharp.Blocks.BSLightingShaderProperty)
        Dim ml = TryCast(myNif.GetShader(ms), NiflySharp.Blocks.BSLightingShaderProperty)
        If cl Is Nothing OrElse ml Is Nothing Then
            If (cl Is Nothing) <> (ml Is Nothing) Then real.Add($"shape '{nm}': shader presence CK={cl IsNot Nothing} baked={ml IsNot Nothing}")
            Return
        End If
        ' shader TYPE: gobierna la ley de composición del bake (CK SSE 0x141d0ea00 dispatcha por este valor:
        ' 4 FaceTint / 5 SkinTint / 6 HairTint / else = no escribe nada). Sin esto un cambio de tipo pasaba
        ' invisible aunque cambie qué slots se escriben.
        If cl.ShaderType_SK_FO4 <> ml.ShaderType_SK_FO4 Then real.Add($"shape '{nm}' shader.TYPE CK={cl.ShaderType_SK_FO4} baked={ml.ShaderType_SK_FO4}")
        ' flags — GAME-AWARE: NiflySharp sólo puebla SSPF1/SSPF2 con StreamVersion menor a 130 (= SSE). En FO4
        ' (==130) los flags viven en F4SPF1/F4SPF2 y los SSPF quedan en su default (MEDIDO: 0x00000000 en
        ' 284/284 shapes de AMBOS lados) ⇒ comparar siempre SSPF1/SSPF2 deja esas dos líneas como código
        ' muerto en FO4 y el barrido nunca detecta una divergencia de shader flags ahí.
        ' Ver BSMain.BSLightingShaderProperty.g.cs (el parser branchea por StreamVersion).
        Dim isFo4Shader As Boolean = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game <> Config_App.Game_Enum.Skyrim)
        Dim ckF1 As UInteger = If(isFo4Shader, CUInt(cl.ShaderFlags_F4SPF1), CUInt(cl.ShaderFlags_SSPF1))
        Dim myF1 As UInteger = If(isFo4Shader, CUInt(ml.ShaderFlags_F4SPF1), CUInt(ml.ShaderFlags_SSPF1))
        Dim ckF2 As UInteger = If(isFo4Shader, CUInt(cl.ShaderFlags_F4SPF2), CUInt(cl.ShaderFlags_SSPF2))
        Dim myF2 As UInteger = If(isFo4Shader, CUInt(ml.ShaderFlags_F4SPF2), CUInt(ml.ShaderFlags_SSPF2))
        Dim f1Label = If(isFo4Shader, "F4SPF1", "SSPF1"), f2Label = If(isFo4Shader, "F4SPF2", "SSPF2")
        ' FLAG POR FLAG, no la palabra entera. Comparar el UInt32 completo produce UNA sola categoria
        ' ("SSPF2 CK=0x… baked=0x…") que mezcla defectos distintos y, tras normalizar los numeros, colapsa
        ' TODOS los bits en la misma linea del reporte: no se puede saber si falta Soft_Lighting, sobra
        ' Vertex_Alpha o cambio Model_Space_Normals. Ahora se emite una entrada por BIT que difiere, con su
        ' nombre del enum, asi cada flag es su propia categoria y se clasifica sola.
        EmitFlagBitDiffs(nm, f1Label, ckF1, myF1, isFo4Shader, False, real)
        EmitFlagBitDiffs(nm, f2Label, ckF2, myF2, isFo4Shader, True, real)
        If ckF1 <> myF1 OrElse ckF2 <> myF2 Then
            line.Append($"  {f1Label}=0x{ckF1:X8}/0x{myF1:X8} {f2Label}=0x{ckF2:X8}/0x{myF2:X8}")
        End If
        ' escalares
        DiffF(nm, "Alpha", cl.Alpha, ml.Alpha, 0.002F, real)
        DiffF(nm, "Glossiness", cl.Glossiness, ml.Glossiness, 0.05F, real)
        DiffF(nm, "SpecularStrength", cl.SpecularStrength, ml.SpecularStrength, 0.002F, real)
        DiffF(nm, "Smoothness", cl.Smoothness, ml.Smoothness, 0.002F, real)
        DiffF(nm, "EmissiveMultiple", cl.EmissiveMultiple, ml.EmissiveMultiple, 0.002F, real)
        DiffF(nm, "RefractionStrength", cl.RefractionStrength, ml.RefractionStrength, 0.002F, real)
        DiffF(nm, "Softlight", cl.Softlight, ml.Softlight, 0.002F, real)
        DiffF(nm, "SubsurfaceRolloff", cl.SubsurfaceRolloff, ml.SubsurfaceRolloff, 0.002F, real)
        DiffF(nm, "BacklightPower", cl.BacklightPower, ml.BacklightPower, 0.002F, real)
        DiffF(nm, "FresnelPower", cl.FresnelPower, ml.FresnelPower, 0.01F, real)
        ' GrayscaleToPaletteScale: el motor SÓLO samplea este campo dentro de la rama GreyscaleToPalette
        ' (flag Color 1<<4 = 0x10 / Alpha 1<<5 = 0x20). Con el flag APAGADO en ambos lados el valor es INERTE
        ' y el CK escribe ahí basura no-inicializada — MEDIDO sobre 284 pares de shapes: separación perfecta,
        ' 207/207 de los que difieren tienen el flag OFF en ambos lados, y los 77 con el flag ON coinciden
        ' EXACTAMENTE. Además el valor del CK no es una constante (0,675 n=58 · 0,488 n=36 · 0,738 n=30 ·
        ' 0,394 n=18 …), lo que confirma que es residuo del builder, no dato. Misma regla que ya aplica el
        ' comparador de producción en FaceGenComparator.vb, función IsCosmeticDiff; el barrido no la tenía.
        Const G2pMask As UInteger = &H30UI
        If ((ckF1 And G2pMask) <> 0UI) OrElse ((myF1 And G2pMask) <> 0UI) Then
            DiffF(nm, "GrayscaleToPaletteScale", cl.GrayscaleToPaletteScale, ml.GrayscaleToPaletteScale, 0.002F, real)
        ElseIf Math.Abs(cl.GrayscaleToPaletteScale - ml.GrayscaleToPaletteScale) > 0.002F Then
            noop.Add($"shape '{nm}' shader.GrayscaleToPaletteScale: CK={cl.GrayscaleToPaletteScale} baked={ml.GrayscaleToPaletteScale} (G2P flag OFF on both ⇒ INERT — NO-OP)")
        End If
        DiffF(nm, "SkinTintAlpha", cl.SkinTintAlpha, ml.SkinTintAlpha, 0.002F, real)
        ' Rimlight/Softlight suelen ser FLT_MAX (no lit) — DiffF ignora NaN pero no Inf; comparar sólo si ambos finitos
        If Not Single.IsInfinity(cl.RimlightPower) OrElse Not Single.IsInfinity(ml.RimlightPower) Then DiffF(nm, "RimlightPower", cl.RimlightPower, ml.RimlightPower, 0.01F, real)
        ' colores
        DiffC3(nm, "SpecularColor", cl.SpecularColor, ml.SpecularColor, real, line)
        DiffC3(nm, "SkinTintColor", cl.SkinTintColor, ml.SkinTintColor, real, line)
        DiffC3(nm, "HairTintColor", cl.HairTintColor, ml.HairTintColor, real, line)
        DiffC4(nm, "EmissiveColor", cl.EmissiveColor, ml.EmissiveColor, real, line)
        ' UV
        If Math.Abs(cl.UVOffset.U - ml.UVOffset.U) > 0.0005 OrElse Math.Abs(cl.UVOffset.V - ml.UVOffset.V) > 0.0005 Then real.Add($"shape '{nm}' shader.UVOffset CK=({cl.UVOffset.U:F3},{cl.UVOffset.V:F3}) baked=({ml.UVOffset.U:F3},{ml.UVOffset.V:F3})")
        If Math.Abs(cl.UVScale.U - ml.UVScale.U) > 0.0005 OrElse Math.Abs(cl.UVScale.V - ml.UVScale.V) > 0.0005 Then real.Add($"shape '{nm}' shader.UVScale CK=({cl.UVScale.U:F3},{cl.UVScale.V:F3}) baked=({ml.UVScale.U:F3},{ml.UVScale.V:F3})")
    End Sub

    ''' <summary>Diff del NiAlphaProperty de un shape (flags raw + threshold).</summary>
    Private Sub CompareAlphaExhaustive(nm As String, cs As NiflySharp.INiShape, ckNif As Nifcontent_Class_Manolo,
                                       ms As NiflySharp.INiShape, myNif As Nifcontent_Class_Manolo, real As List(Of String))
        Dim ca As NiflySharp.Blocks.NiAlphaProperty = Nothing, ma As NiflySharp.Blocks.NiAlphaProperty = Nothing
        If cs.AlphaPropertyRef IsNot Nothing AndAlso cs.AlphaPropertyRef.Index >= 0 Then ca = TryCast(ckNif.Blocks(cs.AlphaPropertyRef.Index), NiflySharp.Blocks.NiAlphaProperty)
        If ms.AlphaPropertyRef IsNot Nothing AndAlso ms.AlphaPropertyRef.Index >= 0 Then ma = TryCast(myNif.Blocks(ms.AlphaPropertyRef.Index), NiflySharp.Blocks.NiAlphaProperty)
        If ca Is Nothing AndAlso ma Is Nothing Then Return
        If (ca Is Nothing) <> (ma Is Nothing) Then real.Add($"shape '{nm}': alpha-prop presence CK={ca IsNot Nothing} baked={ma IsNot Nothing}") : Return
        If ca.Flags.Value <> ma.Flags.Value Then real.Add($"shape '{nm}' alpha.flags CK=0x{ca.Flags.Value:X4} baked=0x{ma.Flags.Value:X4}")
        If ca.Threshold <> ma.Threshold Then real.Add($"shape '{nm}' alpha.threshold CK={ca.Threshold} baked={ma.Threshold}")
    End Sub

    ''' <summary>Nombre del bit <paramref name="bit"/> segun el enum de flags que corresponde al juego y a la
    ''' palabra (1 o 2). Si el bit no tiene nombre declarado devuelve <c>bit&lt;n&gt;</c> — un bit sin nombre es
    ''' informacion (significa que NiflySharp no lo modela), no algo que se deba ocultar.</summary>
    ' ==========================================================================================
    ' DIFF REFLECTIVO — "todo el NIF": bloques, sub-bloques y CADA campo
    ' ==========================================================================================
    ' Por que reflectivo y no una lista de campos a mano: una lista se olvida de algo y el olvido es
    ' INVISIBLE (el barrido informa "0 diferencias" sobre un campo que nunca miro). Recorriendo todas
    ' las propiedades publicas legibles, lo que NO se compara tiene que ser una exclusion EXPLICITA y
    ' nombrada — y esas se reportan aparte, no se ocultan.
    '
    ' LOS INDICES DE BLOQUE NO SE COMPARAN. El orden de emision del CK difiere del nuestro en el 97 %
    ' de los NIF (medido), asi que todo NiBlockRef tiene un Index distinto POR CONSTRUCCION. Compararlos
    ' daria decenas de miles de falsos positivos y taparia lo real. Se compara el DESTINO del ref
    ' (tipo + nombre), que es lo que tiene significado semantico.

    ''' <summary>Propiedades EXCLUIDAS del diff reflectivo, con su motivo. Se listan en el reporte para que
    ''' la omision sea auditable y nunca silenciosa.</summary>
    Private ReadOnly ReflectSkipReasons As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"BlockSize", "serialized size: depends on the emission order and the string table, not on the content"},
        {"StreamVersion", "container version, not the data itself"},
        {"BlockName", "TYPE name: already covered by the block-type histogram"},
        {"References", "NiflySharp plumbing: enumerates raw block INDICES, which differ because of the emission order"},
        {"ReferenceArrays", "idem References"},
        {"Pointers", "same as References (pointers by index)"},
        {"Indices", "idem References (indices crudos)"},
        {"VertexData", "geometry: compared separately with a numeric tolerance (maxD/RMS/ULP bins)"},
        {"VertexPositions", "geometry: compared separately with a numeric tolerance"},
        {"Triangles", "geometry: compared separately (count + differing indices)"},
        {"Normals", "geometry: compared separately with a threshold"},
        {"Tangents", "geometry: compared separately with a threshold"},
        {"Bitangents", "geometry: compared separately with a threshold"},
        {"UVs", "geometry: compared separately with a threshold"},
        {"VertexColors", "geometry: compared separately with a threshold"},
        {"BoneWeights", "skinning: compared separately with a threshold"},
        {"BoneIndices", "skinning: compared separately (count of differing slots)"},
        {"VertexDesc", "already compared explicitly, field by field, above"},
        {"GrayscaleToPaletteScale", "already compared above WITH its gate: the engine only samples it with the G2P flag on; with the flag off the CK writes uninitialized garbage"}
    }

    ''' <summary>Cuantos campos/bloques comparo de verdad el diff reflectivo. Sin este contador,
    ''' "0 diferencias" no se puede distinguir de "no se comparo nada".</summary>
    Private ReflectFieldsCompared As Long = 0
    Private ReflectBlocksCompared As Long = 0
    Private ReadOnly ReflectSkippedProps As New System.Collections.Concurrent.ConcurrentDictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Etiqueta del destino de un ref: "&lt;Tipo&gt;'nombre'". Es lo que se compara en vez del indice.</summary>
    Private Function RefTargetLabel(nif As Nifcontent_Class_Manolo, idx As Integer) As String
        If idx < 0 OrElse idx >= nif.Blocks.Count Then Return "<null>"
        Dim b = nif.Blocks(idx)
        If b Is Nothing Then Return "<null>"
        Dim nmVal As String = ""
        Try
            Dim nmProp = b.GetType().GetProperty("Name")
            If nmProp IsNot Nothing Then
                Dim o = nmProp.GetValue(b)
                If o IsNot Nothing Then
                    Dim sProp = o.GetType().GetProperty("String")
                    nmVal = If(sProp IsNot Nothing, TryCast(sProp.GetValue(o), String), o.ToString())
                End If
            End If
        Catch
        End Try
        Return $"{b.GetType().Name}'{If(nmVal, "")}'"
    End Function

    ''' <summary>Diff reflectivo de dos bloques emparejados.</summary>
    Private Sub ReflectDiffBlock(path As String, a As Object, b As Object,
                                 ckNif As Nifcontent_Class_Manolo, myNif As Nifcontent_Class_Manolo,
                                 real As List(Of String), depth As Integer)
        If a Is Nothing OrElse b Is Nothing Then
            If (a Is Nothing) <> (b Is Nothing) Then real.Add($"{path}: presence CK={a IsNot Nothing} baked={b IsNot Nothing}")
            Return
        End If
        If depth > 4 Then Return
        If a.GetType() IsNot b.GetType() Then
            real.Add($"{path}: block TYPE CK={a.GetType().Name} baked={b.GetType().Name}")
            Return
        End If
        Threading.Interlocked.Increment(ReflectBlocksCompared)
        For Each pi In a.GetType().GetProperties(Reflection.BindingFlags.Public Or Reflection.BindingFlags.Instance)
            If Not pi.CanRead OrElse pi.GetIndexParameters().Length > 0 Then Continue For
            If ReflectSkipReasons.ContainsKey(pi.Name) Then
                ReflectSkippedProps.AddOrUpdate(pi.Name, 1, Function(k, v) v + 1)
                Continue For
            End If
            Dim va As Object = Nothing, vb As Object = Nothing
            Try
                va = pi.GetValue(a) : vb = pi.GetValue(b)
            Catch
                Continue For
            End Try
            ReflectDiffValue($"{path}.{pi.Name}", va, vb, ckNif, myNif, real, depth)
        Next
    End Sub

    ''' <summary>Compara UN valor: escalar directo, ref por destino, coleccion resumida, struct por recursion.</summary>
    Private Sub ReflectDiffValue(path As String, va As Object, vb As Object,
                                 ckNif As Nifcontent_Class_Manolo, myNif As Nifcontent_Class_Manolo,
                                 real As List(Of String), depth As Integer)
        If va Is Nothing AndAlso vb Is Nothing Then Return
        If va Is Nothing OrElse vb Is Nothing Then
            real.Add($"{path}: presence CK={va IsNot Nothing} baked={vb IsNot Nothing}")
            Return
        End If
        Dim t = va.GetType()

        ' La cadena envuelta VA PRIMERO. NiStringRef tambien tiene .Index y su nombre contiene "Ref",
        ' asi que con el orden inverso se lo trataba como ref A BLOQUE y se resolvia su indice contra la
        ' lista de bloques — pero ese indice es de la TABLA DE STRINGS. Resultado: cientos de categorias
        ' fantasma del tipo "Name -> CK=NiNode'' baked=BSSubIndexTriShape''".
        Dim strProp0 = t.GetProperty("String")
        If strProp0 IsNot Nothing AndAlso strProp0.PropertyType Is GetType(String) Then
            Threading.Interlocked.Increment(ReflectFieldsCompared)
            Dim sa0 = TryCast(strProp0.GetValue(va), String), sb0 = TryCast(strProp0.GetValue(vb), String)
            If Not String.Equals(sa0, sb0, StringComparison.Ordinal) Then real.Add($"{path}: CK='{sa0}' baked='{sb0}'")
            Return
        End If

        ' ref a otro bloque -> comparar DESTINO, nunca el indice. Se exige el tipo EXACTO de NiflySharp
        ' (NiBlockRef/NiBlockPtr), no un "contiene Ref": ver arriba por que.
        Dim idxProp = t.GetProperty("Index")
        If idxProp IsNot Nothing AndAlso idxProp.PropertyType Is GetType(Integer) AndAlso
           (t.Name.StartsWith("NiBlockRef", StringComparison.Ordinal) OrElse t.Name.StartsWith("NiBlockPtr", StringComparison.Ordinal)) Then
            Threading.Interlocked.Increment(ReflectFieldsCompared)
            Try
                Dim la = RefTargetLabel(ckNif, CInt(idxProp.GetValue(va)))
                Dim lb = RefTargetLabel(myNif, CInt(idxProp.GetValue(vb)))
                If la <> lb Then real.Add($"{path} -> CK={la} baked={lb}")
            Catch
            End Try
            Return
        End If

        ' cadena envuelta (NiStringRef)
        Dim strProp = t.GetProperty("String")
        If strProp IsNot Nothing AndAlso strProp.PropertyType Is GetType(String) Then
            Threading.Interlocked.Increment(ReflectFieldsCompared)
            Dim sa = TryCast(strProp.GetValue(va), String), sb = TryCast(strProp.GetValue(vb), String)
            If Not String.Equals(sa, sb, StringComparison.Ordinal) Then real.Add($"{path}: CK='{sa}' baked='{sb}'")
            Return
        End If

        ' escalares / enums / strings
        If t.IsPrimitive OrElse t.IsEnum OrElse t Is GetType(String) OrElse t Is GetType(Decimal) Then
            Threading.Interlocked.Increment(ReflectFieldsCompared)
            If t Is GetType(Single) OrElse t Is GetType(Double) Then
                Dim da = Convert.ToDouble(va), db = Convert.ToDouble(vb)
                If Double.IsNaN(da) AndAlso Double.IsNaN(db) Then Return
                If Double.IsInfinity(da) AndAlso Double.IsInfinity(db) Then Return
                If Math.Abs(da - db) > 0.0005 Then real.Add($"{path}: CK={da} baked={db} (Δ={da - db})")
            ElseIf Not va.Equals(vb) Then
                real.Add($"{path}: CK={va} baked={vb}")
            End If
            Return
        End If

        ' colecciones: se RESUME (cantidad + cuantos difieren), no se vuelca elemento por elemento
        Dim ea = TryCast(va, System.Collections.IEnumerable)
        Dim eb = TryCast(vb, System.Collections.IEnumerable)
        If ea IsNot Nothing AndAlso eb IsNot Nothing Then
            Threading.Interlocked.Increment(ReflectFieldsCompared)
            Dim la As New List(Of Object)(), lb As New List(Of Object)()
            Try
                For Each o In ea
                    la.Add(o)
                    If la.Count > 300000 Then Exit For
                Next
                For Each o In eb
                    lb.Add(o)
                    If lb.Count > 300000 Then Exit For
                Next
            Catch
                Return
            End Try
            If la.Count <> lb.Count Then
                real.Add($"{path}: COUNT CK={la.Count} baked={lb.Count}")
                Return
            End If
            ' GUARDA POR VOLUMEN, declarada. Arriba de 5000 elementos se compara SOLO la cantidad.
            ' Motivo: son los arrays de geometria (vertices, triangulos, normales, pesos), que YA se
            ' comparan aparte con tolerancia numerica y metricas (maxD / RMS / bins de ULP). Compararlos
            ' aca por igualdad EXACTA de struct duplicaria el costo y ademas reportaria como "diferencia"
            ' cada ultimo bit de float, que es justo lo que las metricas numericas saben clasificar.
            If la.Count > 5000 Then
                ReflectSkippedProps.AddOrUpdate("(collection >5000: count only)", 1, Function(k, v) v + 1)
                Return
            End If
            ' NO usar x.Equals(y) a secas. Los elementos suelen ser CLASES de NiflySharp (NiString,
            ' BSGeometrySegmentData, …) sin override de igualdad ⇒ Equals = igualdad por REFERENCIA ⇒
            ' SIEMPRE distinto. Eso producia "10/10 elementos difieren" en TODOS los texture sets y
            ' "2/2" en todos los Segments: 100 % de falsos positivos, que es exactamente el modo de fallo
            ' peligroso (ruido con forma de hallazgo). Se compara por CONTENIDO, reusando el mismo
            ' comparador recursivo (que ya sabe de NiStringRef, refs a bloque y structs).
            Dim dif = 0
            ' Se guarda QUE indices difieren y el primer detalle: contar solamente ("1/10 elementos
            ' difieren") no permite saber CUAL de los 10 slots del texture set fallaba — localizaria el
            ' problema en el shape pero no en el campo, que es justo lo que hace falta para atribuirla a
            ' una ley de resolucion de paths.
            Dim difIdx As New List(Of Integer)()
            Dim firstDetail As String = Nothing
            For i = 0 To la.Count - 1
                Dim x = la(i), y = lb(i)
                If x Is Nothing AndAlso y Is Nothing Then Continue For
                If x Is Nothing OrElse y Is Nothing Then
                    dif += 1 : difIdx.Add(i)
                    If firstDetail Is Nothing Then firstDetail = $"[{i}] CK={If(x Is Nothing, "<null>", "<set>")} baked={If(y Is Nothing, "<null>", "<set>")}"
                    Continue For
                End If
                Dim xt = x.GetType()
                If xt.IsPrimitive OrElse xt.IsEnum OrElse xt Is GetType(String) Then
                    If Not x.Equals(y) Then
                        dif += 1 : difIdx.Add(i)
                        If firstDetail Is Nothing Then firstDetail = $"[{i}] CK='{x}' baked='{y}'"
                    End If
                Else
                    Dim tmp As New List(Of String)()
                    ReflectDiffValue($"{path}[{i}]", x, y, ckNif, myNif, tmp, depth + 1)
                    If tmp.Count > 0 Then
                        dif += 1 : difIdx.Add(i)
                        If firstDetail Is Nothing Then firstDetail = tmp(0)
                    End If
                End If
            Next
            If dif > 0 Then
                real.Add($"{path}: {dif}/{la.Count} elementos difieren (idx={String.Join(",", difIdx)}){If(firstDetail Is Nothing, "", " :: " & firstDetail)}")
            End If
            Return
        End If

        ' struct compuesto de NiflySharp (Vector3, Matrix33, Color4, Triangle…): recursion
        If t.IsValueType OrElse (t.Namespace IsNot Nothing AndAlso t.Namespace.StartsWith("NiflySharp")) Then
            ReflectDiffBlock(path, va, vb, ckNif, myNif, real, depth + 1)
        End If
    End Sub

    ''' <summary>Por cada NPC de la lista imprime su outfit por default (DOFT) y si ese outfit es
    ''' DETERMINISTA (solo ARMO directas) o LEVELED (contiene al menos una LVLI). Sirve para decidir si una
    ''' divergencia que depende de lo que el NPC lleva puesto es reproducible o es una tirada del CK.</summary>
    Private Sub OutfitScanRun(pm As PluginManager, listPath As String)
        If Not IO.File.Exists(listPath) Then
            Console.Error.WriteLine($"[outfitscan] the list does not exist: {listPath}") : Environment.ExitCode = 2 : Return
        End If
        Dim det = 0, lvl = 0, sinOutfit = 0, noRes = 0
        Console.WriteLine("[outfitscan] formID | EDID | DOFT | veredicto | contenido")
        For Each raw In IO.File.ReadAllLines(listPath)
            Dim tok = raw.Trim()
            If tok = "" OrElse tok.StartsWith("#") Then Continue For
            Dim m = System.Text.RegularExpressions.Regex.Match(tok, "0x([0-9A-Fa-f]{8})")
            If Not m.Success Then Continue For
            Dim fid = Convert.ToUInt32(m.Groups(1).Value, 16)
            Dim rec = pm.GetRecord(fid)
            If rec Is Nothing Then Console.WriteLine($"  0x{fid:X8} | <no record>") : noRes += 1 : Continue For
            Dim npc = RecordParsers.ParseNPC(rec, pm)
            If npc Is Nothing Then Console.WriteLine($"  0x{fid:X8} | <no parsea>") : noRes += 1 : Continue For
            Dim edid = If(rec.EditorID, "")
            If Not npc.Record.DefaultOutfitPresente OrElse npc.Record.DefaultOutfit = 0UI Then
                Console.WriteLine($"  0x{fid:X8} | {edid} | (no DOFT) | NO OUTFIT |")
                sinOutfit += 1 : Continue For
            End If
            Dim oft = pm.GetRecord(npc.Record.DefaultOutfit)
            If oft Is Nothing Then
                Console.WriteLine($"  0x{fid:X8} | {edid} | 0x{npc.Record.DefaultOutfit:X8} | NO RESUELVE |")
                noRes += 1 : Continue For
            End If
            ' INAM del OTFT = lista de items. Se clasifica por la SIGNATURE del record apuntado:
            ' LVLI = nivelada (lo que el CK vio es una tirada), ARMO = pieza fija.
            Dim sigs As New List(Of String)
            Dim anyLvl = False
            For Each sr In oft.SubRecords
                If sr.Signature <> "INAM" OrElse sr.Data Is Nothing Then Continue For
                For off = 0 To sr.Data.Length - 4 Step 4
                    Dim itemFid = pm.ResolveReferencedFormID(oft.SourcePluginName, BitConverter.ToUInt32(sr.Data, off))
                    Dim ir = pm.GetRecord(itemFid)
                    Dim s = If(ir Is Nothing, "?", ir.Header.Signature)
                    sigs.Add(s)
                    If s = "LVLI" Then anyLvl = True
                Next
            Next
            If anyLvl Then lvl += 1 Else det += 1
            Console.WriteLine($"  0x{fid:X8} | {edid} | 0x{npc.Record.DefaultOutfit:X8} | {If(anyLvl, "LEVELED", "DETERMINISTA")} | {String.Join(",", sigs)}")
        Next
        Console.WriteLine($"[outfitscan] SUMMARY: LEVELED={lvl}  DETERMINISTIC={det}  no outfit={sinOutfit}  unresolved={noRes}")
    End Sub

    Private Function ShaderFlagBitName(isFo4 As Boolean, isSecondWord As Boolean, bit As Integer) As String
        Dim t As Type
        If isFo4 Then
            t = If(isSecondWord, GetType(NiflySharp.Enums.Fallout4ShaderPropertyFlags2), GetType(NiflySharp.Enums.Fallout4ShaderPropertyFlags1))
        Else
            t = If(isSecondWord, GetType(NiflySharp.Enums.SkyrimShaderPropertyFlags2), GetType(NiflySharp.Enums.SkyrimShaderPropertyFlags1))
        End If
        Dim want As ULong = 1UL << bit
        Try
            For Each n In [Enum].GetNames(t)
                Dim ev = Convert.ToUInt64([Enum].Parse(t, n))
                If ev = want Then Return n
            Next
        Catch
        End Try
        Return $"bit{bit}"
    End Function

    ''' <summary>Emite UNA diferencia por cada BIT que difiere entre las dos palabras de flags, nombrada.
    ''' Decir "ON en el CK / OFF en el nuestro" (y no los dos hex) es lo que permite que el agregado del
    ''' barrido agrupe por flag y no por valor numerico.</summary>
    Private Sub EmitFlagBitDiffs(nm As String, label As String, ck As UInteger, mine As UInteger,
                                 isFo4 As Boolean, isSecondWord As Boolean, real As List(Of String))
        Dim x = ck Xor mine
        If x = 0UI Then Return
        For b = 0 To 31
            Dim mask As UInteger = 1UI << b
            If (x And mask) = 0UI Then Continue For
            Dim ckOn = (ck And mask) <> 0UI
            real.Add($"shape '{nm}' shader.{label}.{ShaderFlagBitName(isFo4, isSecondWord, b)}: CK={If(ckOn, "ON", "OFF")} baked={If(ckOn, "OFF", "ON")}")
        Next
    End Sub

    Private Sub DiffF(nm As String, field As String, a As Single, b As Single, thr As Single, real As List(Of String))
        If Single.IsNaN(a) AndAlso Single.IsNaN(b) Then Return
        If Single.IsInfinity(a) AndAlso Single.IsInfinity(b) Then Return
        If Math.Abs(a - b) > thr Then real.Add($"shape '{nm}' shader.{field}: CK={a} baked={b} (Δ={a - b})")
    End Sub

    Private Sub DiffC3(nm As String, field As String, a As NiflySharp.Structs.Color3, b As NiflySharp.Structs.Color3, real As List(Of String), line As System.Text.StringBuilder)
        Dim d = Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)))
        If d > 0.002 Then
            real.Add($"shape '{nm}' shader.{field}: CK=({a.R:F3},{a.G:F3},{a.B:F3}) baked=({b.R:F3},{b.G:F3},{b.B:F3}) maxΔ={d:F3}")
            line.Append($"  {field}!Δ{d:F2}")
        End If
    End Sub

    Private Sub DiffC4(nm As String, field As String, a As NiflySharp.Structs.Color4, b As NiflySharp.Structs.Color4, real As List(Of String), line As System.Text.StringBuilder)
        Dim d = Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A))))
        If d > 0.002 Then
            real.Add($"shape '{nm}' shader.{field}: CK=({a.R:F3},{a.G:F3},{a.B:F3},{a.A:F3}) baked=({b.R:F3},{b.G:F3},{b.B:F3},{b.A:F3}) maxΔ={d:F3}")
            line.Append($"  {field}!Δ{d:F2}")
        End If
    End Sub

    ''' <summary>maxΔ (componente) + RMS (magnitud) entre dos listas de Vector3.</summary>
    Private Function MaxRmsVec(a As List(Of System.Numerics.Vector3), b As List(Of System.Numerics.Vector3)) As (Rms As Double, Max As Double)
        Dim n = Math.Min(a.Count, b.Count)
        Dim ss As Double = 0, mx As Double = 0
        For i = 0 To n - 1
            Dim dx = a(i).X - b(i).X, dy = a(i).Y - b(i).Y, dz = a(i).Z - b(i).Z
            Dim d2 = dx * dx + dy * dy + dz * dz
            ss += d2
            mx = Math.Max(mx, Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))))
        Next
        Return (Math.Sqrt(ss / Math.Max(1, n)), mx)
    End Function

    ''' <summary>Índice (posición en la bone-list del skin) del hueso <paramref name="boneName"/> en el shape, o -1.</summary>
    Private Function BoneIndexInShape(nif As Nifcontent_Class_Manolo, shp As NiflySharp.INiShape, boneName As String) As Integer
        Dim sir = shp.SkinInstanceRef
        If sir Is Nothing OrElse sir.Index < 0 Then Return -1
        Dim si = TryCast(nif.Blocks(sir.Index), NiflySharp.Blocks.NiSkinInstance)
        If si Is Nothing OrElse si.Bones Is Nothing Then Return -1
        For bi = 0 To si.Bones.References.Count - 1
            Dim br = si.Bones.GetBlockRef(bi)
            Dim bn = TryCast(nif.Blocks(br), NiflySharp.Blocks.NiNode)
            If bn IsNot Nothing AndAlso String.Equals(If(bn.Name?.String, ""), boneName, StringComparison.OrdinalIgnoreCase) Then Return bi
        Next
        Return -1
    End Function

    ''' <summary>El BSShaderTextureSet de un shape (vía su BSLightingShaderProperty), o Nothing.</summary>
    Private Function GetTexSet(nif As Nifcontent_Class_Manolo, shp As NiflySharp.INiShape) As NiflySharp.Blocks.BSShaderTextureSet
        Dim shad = TryCast(nif.GetShader(shp), NiflySharp.Blocks.BSLightingShaderProperty)
        If shad Is Nothing OrElse shad.TextureSetRef Is Nothing OrElse shad.TextureSetRef.Index < 0 Then Return Nothing
        Return TryCast(nif.Blocks(shad.TextureSetRef.Index), NiflySharp.Blocks.BSShaderTextureSet)
    End Function

    ''' <summary>DIAGNOSTICO (--nifslots "&lt;nifA&gt;[|&lt;nifB&gt;]"): por shape, vuelca ShaderType,
    ''' MNAM del shader y los 10 texslots. File-only, no monta plugins.</summary>
    Private Sub NifSlotsRun(spec As String)
        For Each p In spec.Split("|"c)
            p = p.Trim()
            If p = "" Then Continue For
            Console.WriteLine($"======== {p}")
            If Not IO.File.Exists(p) Then Console.WriteLine("   (does not exist)") : Continue For
            Dim nif As New Nifcontent_Class_Manolo() : nif.Load_Manolo(IO.File.ReadAllBytes(p))
            For Each shp In nif.GetShapes()
                Dim shad = TryCast(nif.GetShader(shp), NiflySharp.Blocks.BSLightingShaderProperty)
                Dim st = If(shad Is Nothing, "?", shad.ShaderType_SK_FO4.ToString())
                Dim mn = If(shad Is Nothing, "", If(shad.Name?.String, ""))
                Console.WriteLine($"  shape '{shp.Name?.String}'  shaderType={st}  shaderName='{mn}'")
                Dim ts = GetTexSet(nif, shp)
                If ts Is Nothing OrElse ts.Textures Is Nothing Then Console.WriteLine("     (no texture set)") : Continue For
                For si = 0 To ts.Textures.Count - 1
                    Console.WriteLine($"     TX{si:D2} = '{ts.Textures(si)?.Content}'")
                Next
            Next
        Next
    End Sub

    ''' <summary>Compone + escribe los TGA `_3` de UN NPC. Devuelve True si escribio. tintBytesCache es
    ''' compartido por todo el batch (bytes crudos de las texturas de layers/swaps leidos una sola vez);
    ''' el decode cacheado entre NPCs lo maneja FaceTintCpuCompositor.BatchDecodeCache via las keys.</summary>
    Private Function BakeNpc(pm As PluginManager, espName As String, edid As String, dataPath As String,
                             outOverride As String, tintBytesCache As Dictionary(Of String, Byte()),
                             dumpDir As String) As Boolean
        Dim npcFormID = ResolveEdid(pm, espName, edid)
        If npcFormID = 0UI Then
            Console.Error.WriteLine($"[skip] EDID='{edid}' not provided by '{espName}'.") : Return False
        End If
        Dim originPlugin = pm.GetOriginatingPluginName(npcFormID)

        Dim npcRec = pm.GetRecord(npcFormID)
        Dim npcData = RecordParsers.ParseNPC(npcRec, pm)
        If npcData Is Nothing Then Console.Error.WriteLine($"[skip] {edid}: ParseNPC failed.") : Return False
        Dim raceRec = pm.GetRecord(npcData.Record.Race)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then
            Console.Error.WriteLine($"[skip] {edid}: RACE 0x{npcData.Record.Race:X8} not resolved.") : Return False
        End If
        Dim race = Canon.CanonRecords.Race(raceRec, pm)
        ' Merge any LooksMenu custom face-tint templates (Data\F4SE\Plugins\F4EE\Tints\) into the RACE's
        ' tint groups before compositing, exactly as the GUI does via FaceTintLayerBuilder — otherwise an
        ' NPC using a mod-added tint index resolves to no option and the layer is silently dropped in the
        ' headless bake. La lista fusionada vive aparte de race (no se muta el record). Pass the CLI's own
        ' dataPath so --data overrides are honoured. No-op when there are none.
        Dim tintGroups = FO4_NPC_Manager.LmCustomTintLoader.Fusionar(race, npcData.Record.ConfigurationFlagsFemale, pm, dataPath)

        Dim dPath As String = "", nPath As String = "", sPath As String = ""
        ResolveFaceSkin(npcData, race, pm, dPath, nPath, sPath)
        If String.IsNullOrEmpty(dPath) Then Console.Error.WriteLine($"[skip] {edid}: no face diffuse texture.") : Return False
        Dim dKey = FO4UnifiedMaterial_Class.CorrectTexturePath(dPath)
        Dim nKey = FO4UnifiedMaterial_Class.CorrectTexturePath(nPath)
        Dim sKey = FO4UnifiedMaterial_Class.CorrectTexturePath(sPath)
        Dim dBytes = FilesDictionary_class.GetBytes(dKey)
        Dim nBytes = FilesDictionary_class.GetBytes(nKey)
        Dim sBytes = FilesDictionary_class.GetBytes(sKey)
        If dBytes Is Nothing OrElse dBytes.Length = 0 Then Console.Error.WriteLine($"[skip] {edid}: empty diffuse bytes (key='{dKey}').") : Return False

        ' Texture lighting (QNAM) leido del record, NO hardcodeado: el app inyecta la capa slot-12
        ' SkinTone sintetica desde el QNAM (FaceTintInputBuilder.InjectSyntheticSkinToneLayer) y el
        ' bake debe hacer lo mismo o la cara diverge (la capa SoftLight pisa D y R/G de N/S).
        ' El LUT de la ceja ya no se pasa: lo resuelve el builder desde el RACE (ver
        ' LmHairColorLutLoader.ResolveBrowPaletteTexture). dataPath va explicito porque el CLI honra --data
        ' y no puebla el Config_App global.
        Dim built = FaceTintInputBuilder.Build(npcData, race, npcData.Record.ConfigurationFlagsFemale, pm, tintBytesCache, tintGroups,
                                               npcData.Record.HairColor,
                                               npcData.Record.TextureLightingRedPresente, npcData.Record.ColorDeIluminacionDeTextura().ToArgb(),
                                               dataPath)
        Dim cpu = FaceTintCpuCompositor.ComposeCpuPipeline(dBytes, nBytes, sBytes, built.Layers, built.RegionSwaps,
                                                           resolution:=Nothing, diffuseKey:=dKey, normalKey:=nKey, specKey:=sKey,
                                                           headDiffuseAlphaTest:=(npcData.Game = Config_App.Game_Enum.Fallout4) AndAlso (npcData.Record.ConfigurationFlags And &H1000000UI) <> 0UI)

        Dim outDir = If(outOverride <> "", outOverride,
                        Path.Combine(dataPath, "Textures", "Actors", "Character", "FaceCustomization", originPlugin))
        Directory.CreateDirectory(outDir)
        Dim localId = PluginManager.ToFaceGenLocalFormID(npcFormID)
        WriteChannel(outDir, localId, "d", cpu.Diffuse)
        WriteChannel(outDir, localId, "msn", cpu.Normal)
        WriteChannel(outDir, localId, "s", cpu.Specular)
        If dumpDir <> "" Then DumpMasks(built, dKey, nKey, sKey, Path.Combine(dumpDir, $"{localId:X8}"))
        Console.WriteLine($"[ok] {edid} 0x{npcFormID:X8} -> {localId:X8}_*_3.tga (layers={built.Layers.Count} swaps={built.RegionSwaps.Count} browLut='{LmHairColorLutLoader.ResolveBrowPaletteTexture(race, npcData.Record.HairColor)}')")
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
        Public Race As Canon.IRace
        Public IsFemale As Boolean
        Public HairColorFormID As UInteger
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

    ''' <summary>Carga un ref CK del BA2 VANILLA (FilesDictionary loose&gt;BA2; para NPCs vanilla sin loose =
    ''' el BA2 oficial) por DecodeDds → RGB. Reemplaza LoadTgaRgb (loose _d.tga = bakes viejos contaminados).
    ''' key = textures\actors\character\facecustomization\&lt;plugin&gt;\&lt;localId&gt;_d.dds (o _msn/_s).</summary>
    Private Function LoadBa2Ref(key As String) As ChRef
        Dim b = FilesDictionary_class.GetBytes(key.ToLowerInvariant())
        If b Is Nothing Then Return Nothing
        Dim t = FaceTintCpuCompositor.DecodeDds(b)
        If t Is Nothing OrElse t.Rgba8 Is Nothing Then Return Nothing
        Dim rgb(t.Width * t.Height * 3 - 1) As Byte
        For i = 0 To t.Width * t.Height - 1
            rgb(i * 3) = CByte(Math.Max(0, Math.Min(255, Math.Round(t.Unit(i * 4) * 255))))
            rgb(i * 3 + 1) = CByte(Math.Max(0, Math.Min(255, Math.Round(t.Unit(i * 4 + 1) * 255))))
            rgb(i * 3 + 2) = CByte(Math.Max(0, Math.Min(255, Math.Round(t.Unit(i * 4 + 2) * 255))))
        Next
        Return New ChRef With {.W = t.Width, .H = t.Height, .Rgb = rgb}
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
        ' VANILLA-ONLY guard (USA LOS DEL BA2 vanilla): skip if the NPC is DEFINED in or OVERRIDDEN by any
        ' non-Fallout4.esm plugin — a mod contaminates both the record and its FaceCustomization ref. Only
        ' NPCs whose winning record AND facegen origin are 100% Fallout4.esm count as vanilla.
        If Not String.Equals(originPlugin, esp, StringComparison.OrdinalIgnoreCase) OrElse
           Not String.Equals(npcRec.SourcePluginName, esp, StringComparison.OrdinalIgnoreCase) Then
            Console.Error.WriteLine($"[skip-modded] {edid} 0x{fid:X8} origin='{originPlugin}' source='{npcRec.SourcePluginName}' (no vanilla)")
            Return Nothing
        End If
        Dim npcData = RecordParsers.ParseNPC(npcRec, pm)
        If npcData Is Nothing Then Return Nothing
        Dim raceRec = pm.GetRecord(npcData.Record.Race)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = Canon.CanonRecords.Race(raceRec, pm)
        Dim dP As String = "", nP As String = "", sP As String = ""
        ResolveFaceSkin(npcData, race, pm, dP, nP, sP)
        If String.IsNullOrEmpty(dP) Then Return Nothing
        Dim dk = FO4UnifiedMaterial_Class.CorrectTexturePath(dP)
        Dim nk = FO4UnifiedMaterial_Class.CorrectTexturePath(nP)
        Dim sk = FO4UnifiedMaterial_Class.CorrectTexturePath(sP)
        Dim ctx As New NpcSweepCtx With {
            .Edid = edid, .NpcData = npcData, .Race = race, .IsFemale = npcData.Record.ConfigurationFlagsFemale,
            .HairColorFormID = npcData.Record.HairColor,
            .HasTextureLighting = npcData.Record.TextureLightingRedPresente, .TextureLightingArgb = npcData.Record.ColorDeIluminacionDeTextura().ToArgb(),
            .DKey = dk, .NKey = nk, .SKey = sk,
            .DBytes = FilesDictionary_class.GetBytes(dk), .NBytes = FilesDictionary_class.GetBytes(nk), .SBytes = FilesDictionary_class.GetBytes(sk)}
        Dim localId = PluginManager.ToFaceGenLocalFormID(fid)
        ' CK ref del BA2 VANILLA (no loose): USA LOS DEL BA2. FaceCustomization\<plugin>\<localId>_d/_msn/_s.dds
        Dim ckBase = $"textures\actors\character\facecustomization\{originPlugin}\{localId:X8}"
        ctx.CkD = LoadBa2Ref(ckBase & "_d.dds")
        ctx.CkN = LoadBa2Ref(ckBase & "_msn.dds")
        ctx.CkS = LoadBa2Ref(ckBase & "_s.dds")
        Return ctx
    End Function

    ''' <summary>Setea las secciones FaceTint de Config_App.Current desde un config json (convencion + orden).
    ''' <para>La convencion se lee de la CLAVE DEL JUEGO ACTIVO y se escribe en su SLOT. Un config.json trae
    ''' las DOS leyes (FO4 y SSE); leer la de FO4 con SSE activo mide la ley equivocada, y escribirla en el slot
    ''' de FO4 la deja INVISIBLE para ActiveSettings ⇒ un barrido de N convenciones medía N veces la misma sin
    ''' fallar. Si la clave del juego activo no está, NO se degrada a la otra: se avisa y se deja intacta.</para></summary>
    Private Sub ApplyConfigJson(path As String)
        Dim slot = FaceTintConvention.ActiveSettingsSlotName(Config_App.Current)
        Using doc = JsonDocument.Parse(File.ReadAllText(path))
            Dim el As JsonElement
            If doc.RootElement.TryGetProperty(slot, el) Then
                Dim written = FaceTintConvention.SetActiveSettings(Config_App.Current,
                    JsonSerializer.Deserialize(Of FaceTintConvention.FaceTintConventionSettings)(el.GetRawText()))
                Console.WriteLine($"[cfg] convention <- {path}   key='{slot}' -> slot '{written}'")
            Else
                Console.Error.WriteLine($"[warn] '{path}' no tiene la clave '{slot}' del juego activo" &
                                        $" ({Config_App.Current.Game}): la convención queda SIN TOCAR." &
                                        " Un barrido en este estado mide siempre la misma ley.")
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

    ''' <summary>Corre los self-tests del gate de paridad y reporta la cobertura POR EJE. No hornea ni monta
    ''' nada: es el instrumento que faltaba para poder verificar un cambio de ley en segundos en vez de en un
    ''' bake. ExitCode 2 si algún test falla — un barrido tiene que poder distinguirlo del éxito.
    ''' <para>El veredicto va por eje a propósito: sin SIMD acelerado los siete tests de espejo vectorial
    ''' hacen early-return y un "todo OK" plano sería mentira.</para></summary>
    Private Sub ParityGateRun(dumpGolden As Boolean)
        Console.WriteLine($"[paritygate] SIMD: {If(FastPow.AcceleratedV, $"Vector(Of T) de {FastPow.LaneCount * 32} bits", "scalar (SIN SIMD acelerado)")}   lanes={FastPow.LaneCount}")
        Dim fail = FO4_NPC_Manager.FaceGenBuilder.SimdParityFailure()
        Console.WriteLine(FO4_NPC_Manager.FaceGenBuilder.ParityAxesReport(fail))
        ' MEDICIONES, no gates: no fallan, informan. Son las que el plan pide ANTES de colapsar dos formas
        ' de la misma ley (decisión 4 del soft-light y la cuarta transcripción de la curva sRGB): sin el
        ' número, unificarlas es apostar a que "son lo mismo" porque algebraicamente lo parecen.
        Console.WriteLine("[medicion] " & FaceTintCpuCompositor.SoftLightShapeReport())
        Console.WriteLine("[medicion] " & FaceTintCpuCompositor.SrgbCurveShapeReport())
        If fail.Length > 0 Then
            Console.Error.WriteLine("[paritygate] *** FAIL *** " & fail)
            Environment.ExitCode = 2
        Else
            Console.WriteLine("[paritygate] PASS (ver la cobertura por eje arriba)")
        End If
        If dumpGolden Then
            Console.WriteLine()
            Console.WriteLine("--- FoldGoldenBits (pegar tal cual en SseFaceGenBaker) ---")
            Console.Write(SseFaceGenBaker.FoldGoldenDump())
        End If
    End Sub

    ''' <summary>Barre cada config .json de sweepDir contra CK con UNA sola carga. Resuelve los NPCs +
    ''' carga refs CK una vez; el BatchDecodeCache + tintBytesCache persisten entre TODAS las convenciones
    ''' (los inputs no cambian, solo la math/orden) -> cada DDS se decodifica una sola vez en todo el sweep.
    ''' Reporta el ranking por NORMAL (mean vs CK).</summary>
    Private Sub RunSweep(pm As PluginManager, work As List(Of (Esp As String, Edid As String)),
                         dataPath As String, sweepDir As String, Optional rankBy As String = "n")
        Console.WriteLine("[sweep] resolving NPCs + loading CK refs...")
        Dim ctxs As New List(Of NpcSweepCtx)
        For Each w In work
            Dim c = ResolveSweepNpc(pm, w.Esp, w.Edid, dataPath)
            If c IsNot Nothing AndAlso c.CkN IsNot Nothing Then
                ctxs.Add(c)
            Else
                Console.Error.WriteLine($"[skip] {w.Edid}: unresolved or no CK ref _msn")
            End If
        Next
        If ctxs.Count = 0 Then Console.Error.WriteLine("No NPC with CK ref.") : Environment.ExitCode = 1 : Return
        Dim configs = Directory.GetFiles(sweepDir, "*.json").OrderBy(Function(p) p).ToList()
        If configs.Count = 0 Then Console.Error.WriteLine($"No *.json in {sweepDir}") : Environment.ExitCode = 1 : Return
        Console.WriteLine($"[sweep] {ctxs.Count} NPCs x {configs.Count} conventions (decode cached across all)")

        ' EL MOTIVO SE DICE. Descartarlo dejaba al usuario del CLI sin saber si su
        ' FGBAKE_DECODE_CACHE_MB se leyó o si corrió sin techo — un techo que no se ve no se
        ' puede diagnosticar. Es el mismo argumento que MainForm ya aplica.
        Console.WriteLine("[decode-cache] " & FaceTintCpuCompositor.BeginBatchDecodeCacheConMotivo())
        Dim tintCache As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)
        Dim rows As New List(Of (Name As String, Dn As Double, Dx As Integer, Nn As Double, Nx As Integer, Sn As Double, Sx As Integer))
        Try
            For Each cfg In configs
                ApplyConfigJson(cfg)
                Dim dd As New List(Of Double), nn As New List(Of Double), ss As New List(Of Double)
                Dim dmx As Integer = 0, nmx As Integer = 0, smx As Integer = 0
                For Each ctx In ctxs
                    ' dataPath EXPLICITO, igual que el camino de un solo NPC. Sin esto caia al
                    ' Config_App global, que es el Data de ESCRITURA — y el CLI admite read<>write a
                    ' proposito (ver el [warn] de arriba). Con --data apuntando a otro lado, el sweep leia
                    ' el LUTs\ del Data equivocado; y como el registro latchea en la primera carga, el que
                    ' corriera primero fijaba el de todo el proceso: render y bake dejaban de coincidir.
                    Dim sweepGroups = TryCast(ctx.Race, Canon.RaceFO4).TintesDelRecord(ctx.IsFemale)
                    Dim built = FaceTintInputBuilder.Build(ctx.NpcData, ctx.Race, ctx.IsFemale, pm, tintCache, sweepGroups,
                                                           ctx.HairColorFormID,
                                                           ctx.HasTextureLighting, ctx.TextureLightingArgb,
                                                           dataPath)
                    Dim cpu = FaceTintCpuCompositor.ComposeCpuPipeline(ctx.DBytes, ctx.NBytes, ctx.SBytes, built.Layers, built.RegionSwaps,
                                                                       resolution:=Nothing, diffuseKey:=ctx.DKey, normalKey:=ctx.NKey, specKey:=ctx.SKey,
                                                                       headDiffuseAlphaTest:=(ctx.NpcData.Game = Config_App.Game_Enum.Fallout4) AndAlso (ctx.NpcData.Record.ConfigurationFlags And &H1000000UI) <> 0UI)
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
        Console.WriteLine($"=== RANKING by {rankBy.ToUpperInvariant()} (mean vs CK, asc) | {ctxs.Count} NPCs ===")
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
                    Console.Error.WriteLine($"[warn] line without esp or --esp default: '{line}'")
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
            Console.Error.WriteLine($"[warn] esp '{espName}' does not exist in {dataPath}; skipping.") : Return
        End If
        Dim probe As New PluginReader() : probe.Load(espFull)
        For Each m In probe.Masters
            If Not loadList.Any(Function(p) String.Equals(p, m, StringComparison.OrdinalIgnoreCase)) Then loadList.Add(m)
        Next
        loadList.Add(espName)
        Console.WriteLine($"[load] +esp NON-active '{espName}' (masters: {String.Join(", ", probe.Masters)})")
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
                Console.WriteLine($"[warn] FormID '{edid}' not provided by '{esp}' but sole match in another plugin; using 0x{hexFallback:X8}.")
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
            Console.WriteLine($"[warn] EDID '{edid}' not provided by '{esp}' but sole match in another plugin; using 0x{fallback:X8}.")
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
        If npcFormID = 0UI Then Console.WriteLine($"[tints] {edid}: not resolved in {espName}") : Return
        Dim npcRec = pm.GetRecord(npcFormID)
        Dim npcData = RecordParsers.ParseNPC(npcRec, pm)
        Dim raceRec = pm.GetRecord(npcData.Record.Race)
        Dim race = Canon.CanonRecords.Race(raceRec, pm)
        Dim isFemale = npcData.Record.ConfigurationFlagsFemale
        Console.WriteLine($"=== TINTS {edid} 0x{npcFormID:X8} race=0x{npcData.Record.Race:X8} female={isFemale} ===")

        Dim npcIdx As New HashSet(Of UShort)()
        Console.WriteLine("-- NPC authored FaceTintLayers --")
        Dim capasAutoradas = FaceTintInputBuilder.CapasAutoradasDelRecord(npcData.Record)
        If capasAutoradas.Count > 0 Then
            For Each tl In capasAutoradas
                npcIdx.Add(tl.Index)
                Console.WriteLine($"  idx={tl.Index} value={tl.Value} disc={tl.Discriminator} tplColIdx={tl.TemplateColorIndex} color=ARGB(0x{tl.Color.ToArgb():X8})")
            Next
        Else
            Console.WriteLine("  (none)")
        End If

        Dim groups = TryCast(race, Canon.RaceFO4).TintesDelRecord(isFemale)
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
                                Dim clfm = Canon.CanonRecords.Clfm(crec, pm)
                                If clfm IsNot Nothing AndAlso clfm.TieneColor() Then col = $"ARGB(0x{clfm.ColorDe().ToArgb():X8})"
                            End If
                            Console.WriteLine($"        tplCol tplIdx={tc.TemplateIndex} alpha={tc.Alpha:G6} blendOp={tc.BlendOperation}/{BlendName(tc.BlendOperation)} clfm=0x{tc.ColorFormID:X8} {col}")
                        Next
                    End If
                Next
            Next
        End If
        Console.WriteLine($"-- TTED summary: with-TTED={nTted}, denormals(=integer read as float)={nDenorm} -> {If(nDenorm = 0, "ALL healthy floats", "HAS integers/denormals")} --")

        Dim merged = FaceTintInputBuilder.MergeTintLayersWithRaceDefaults(npcData.Record, groups, pm)
        Console.WriteLine($"-- MERGED ({merged.Count}) -- (RESOLVED = ResolvePaletteLayerEffective: which BlendOp/color the compositor uses)")
        For Each m In merged
            Dim lyr = m
            Dim resolvedStr As String = ""
            If lyr.Discriminator = 1 Then   ' Palette: resolver Step1(idx)/Step2(color)/Step3(fallback)
                Dim opt = groups.BuscarOpcion(lyr.Index)
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
            Console.Error.WriteLine("[dumpref] format: --dumpref ""<filesDictKey>|<outFile>""") : Environment.ExitCode = 1 : Return
        End If
        Dim key = parts(0).Trim()
        Dim outFile = parts(1).Trim()
        ' DEL ARCHIVE PRIMERO: GetBytes deja que los SUELTOS SOMBREEN el archive, asi que con nuestro propio
        ' bake suelto en el Data esta herramienta devolvia NUESTRO archivo rotulado como "referencia del
        ' CK" (ej. 0x00074F90: mismo MD5 porque era el MISMO archivo, contra el que se "verificaba" con
        ' exito). Ahora se pide del archive y, si no hay entrada archivada, se AVISA en vez de devolver el
        ' suelto en silencio.
        Dim bytes = FilesDictionary_class.GetArchiveOriginalBytes(key)
        If bytes Is Nothing OrElse bytes.Length = 0 Then
            bytes = FilesDictionary_class.GetBytes(key)
            If bytes IsNot Nothing AndAlso bytes.Length > 0 Then
                Console.Error.WriteLine($"[dumpref] ⚠ '{key}' is NOT in any BA2/BSA: what comes out is the LOOSE file," &
                                        " which may be a bake of OURS. Do NOT use it as a CK reference.")
            End If
        End If
        If bytes Is Nothing OrElse bytes.Length = 0 Then
            Console.Error.WriteLine($"[dumpref] empty or not found key: '{key}'") : Environment.ExitCode = 1 : Return
        End If
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile)))
        File.WriteAllBytes(outFile, bytes)
        Console.WriteLine($"[dumpref] {key} -> {outFile} ({bytes.Length} bytes)")
    End Sub

    ''' <summary>DIAGNOSTICO (--ba2extract "&lt;archivePath&gt;|&lt;internalKey&gt;|&lt;outFile&gt;"): abre UN BA2/BSA
    ''' directo por ruta de disco y extrae la entry cuyo FullPath interno coincide con internalKey (exacto
    ''' case-insensitive, o EndsWith como fallback), escribiéndola a outFile. Sirve para leer un asset VANILLA
    ''' directo del archivo SIN pasar por el FilesDictionary (que resuelve loose &gt; BA2 y sería contaminado por
    ''' un replacer loose). File-only: no monta plugins ni load order.</summary>
    Private Sub Ba2ExtractRun(spec As String)
        Dim parts = spec.Split({"|"c}, 3)
        If parts.Length <> 3 Then
            Console.Error.WriteLine("[ba2extract] format: --ba2extract ""<archivePath>|<internalKey>|<outFile>""") : Environment.ExitCode = 1 : Return
        End If
        Dim archivePath = parts(0).Trim()
        Dim internalKey = parts(1).Trim()
        Dim outFile = parts(2).Trim()
        If Not IO.File.Exists(archivePath) Then
            Console.Error.WriteLine($"[ba2extract] file does not exist: '{archivePath}'") : Environment.ExitCode = 1 : Return
        End If
        Dim k = internalKey.Replace("/"c, "\"c).Trim().ToLowerInvariant()
        ' DIAGNOSTICO bulk: si el key termina en '*', extrae TODAS las entries cuyo FullPath empieza con el
        ' prefijo, a outFile tratado como DIRECTORIO (conserva el nombre de archivo interno).
        If k.EndsWith("*") Then
            Dim pref = k.Substring(0, k.Length - 1)
            Try
                Using fs As New IO.FileStream(archivePath, IO.FileMode.Open, IO.FileAccess.Read)
                    Using r As New BSA_BA2_Library_DLL.BethesdaArchive.Core.BethesdaReader(fs)
                        Directory.CreateDirectory(outFile)
                        Dim n = 0
                        For Each e In r.EntriesFiles
                            If Not e.FullPath.Replace("/"c, "\"c).ToLowerInvariant().StartsWith(pref) Then Continue For
                            IO.File.WriteAllBytes(IO.Path.Combine(outFile, IO.Path.GetFileName(e.FullPath)), r.ExtractToMemory(e.Index))
                            n += 1
                        Next
                        Console.WriteLine($"[ba2extract] bulk '{pref}*' -> {outFile} ({n} entries)")
                    End Using
                End Using
            Catch ex As Exception
                Console.Error.WriteLine($"[ba2extract] error: {ex.Message}") : Environment.ExitCode = 1
            End Try
            Return
        End If
        Try
            Using fs As New IO.FileStream(archivePath, IO.FileMode.Open, IO.FileAccess.Read)
                Using r As New BSA_BA2_Library_DLL.BethesdaArchive.Core.BethesdaReader(fs)
                    ' Match exacto por FullPath normalizado; si no hay, la primera cuyo FullPath termina en el key.
                    Dim hit = r.EntriesFiles.FirstOrDefault(
                        Function(e) e.FullPath.Replace("/"c, "\"c).ToLowerInvariant() = k)
                    If hit Is Nothing Then
                        hit = r.EntriesFiles.FirstOrDefault(
                            Function(e) e.FullPath.Replace("/"c, "\"c).ToLowerInvariant().EndsWith(k))
                    End If
                    If hit Is Nothing Then
                        Console.Error.WriteLine($"[ba2extract] not found: '{internalKey}' in '{archivePath}' ({r.EntriesFiles.Count} entries)")
                        ' Diagnóstico: hasta 10 entries cuyo FullPath contenga el nombre de archivo del key.
                        Dim fname = IO.Path.GetFileName(k)
                        If fname <> "" Then
                            Dim near = r.EntriesFiles.
                                Where(Function(e) e.FullPath.ToLowerInvariant().Contains(fname)).
                                Take(10).ToList()
                            If near.Count > 0 Then
                                Console.Error.WriteLine($"[ba2extract] entries containing '{fname}':")
                                For Each e In near
                                    Console.Error.WriteLine($"    {e.FullPath}")
                                Next
                            End If
                        End If
                        Environment.ExitCode = 1 : Return
                    End If
                    Dim bytes = r.ExtractToMemory(hit.Index)
                    Dim dir = IO.Path.GetDirectoryName(IO.Path.GetFullPath(outFile))
                    If dir <> "" Then Directory.CreateDirectory(dir)
                    IO.File.WriteAllBytes(outFile, bytes)
                    Console.WriteLine($"[ba2extract] {hit.FullPath} -> {outFile} ({bytes.Length} bytes)")
                End Using
            End Using
        Catch ex As Exception
            Console.Error.WriteLine($"[ba2extract] error: {ex.Message}") : Environment.ExitCode = 1
        End Try
    End Sub

    ''' <summary>Lee bytes de un NIF/.sclp aceptando O una ruta de disco existente (loose/extraído) O un key del
    ''' FilesDictionary (loose &gt; BA2). Permite pasar al probe (--estimatesclp / --sclpdiag) rutas absolutas de
    ''' assets vanilla extraídos, evitando el override loose que contamina la referencia del FilesDictionary.</summary>
    Private Function GetNifOrFileBytes(keyOrPath As String) As Byte()
        If IO.File.Exists(keyOrPath) Then Return IO.File.ReadAllBytes(keyOrPath)
        Return FilesDictionary_class.GetBytes(keyOrPath)
    End Function

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
        If npcFormID = 0UI Then Console.WriteLine($"[prov] {edid}: not resolved in {espName}") : Return
        Dim npcRec = pm.GetRecord(npcFormID)
        Dim npcSrc = npcRec.SourcePluginName
        Dim npcData = RecordParsers.ParseNPC(npcRec, pm)
        Dim raceRec = pm.GetRecord(npcData.Record.Race)
        Dim raceSrc = If(raceRec IsNot Nothing, raceRec.SourcePluginName, "(none)")
        Console.WriteLine($"=== PROVENANCE {edid} 0x{npcFormID:X8} ===")
        Console.WriteLine($"  NPC_  0x{npcFormID:X8}  src={npcSrc} {VanillaTag(npcSrc)}")
        Console.WriteLine($"  RACE  0x{npcData.Record.Race:X8}  src={raceSrc} {VanillaTag(raceSrc)}")
        Dim race = Canon.CanonRecords.Race(raceRec, pm)
        Dim isFemale = npcData.Record.ConfigurationFlagsFemale
        Dim groups = TryCast(race, Canon.RaceFO4).TintesDelRecord(isFemale)
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
        Console.WriteLine($"  => {If(allVanilla, "ALL VANILLA: vanilla-vs-vanilla comparison VALID", "MOD OVERRIDE PRESENT: DO NOT BAKE (contaminated comparison)")}")
    End Sub

    ''' <summary>--ddsprobe &lt;formID&gt; — VALORES DE PIXEL del facetint SSE, nuestro vs el del CK. Existe
    ''' porque RMS y maxD son agregados que NO distinguen dos cosas muy distintas: "las dos imagenes son
    ''' planas y difieren en una constante" vs "una de las dos tiene variacion espacial". En el defecto de
    ''' raceLayers=0 el agregado daba meanD=(5,3,5) pero maxD=(9,5,9): si ambas fueran planas serian IGUALES.
    ''' Se imprimen los colores distintos de cada lado con su frecuencia, que responde la pregunta sin
    ''' inferir nada.</summary>
    Private Sub DdsProbe(pm As PluginManager, npcFormID As UInteger)
        Dim rec = pm.GetRecord(npcFormID)
        If rec Is Nothing Then Console.Error.WriteLine($"0x{npcFormID:X8}: does not exist") : Environment.ExitCode = 2 : Return
        Dim npc = RecordParsers.ParseNPC(rec, pm)
        Dim race = Canon.CanonRecords.Race(pm.GetRecord(npc.Record.Race), pm)
        Dim origin = pm.GetOriginatingPluginName(npcFormID)
        Dim fgL = PluginManager.ToFaceGenLocalFormID(npcFormID)
        Console.WriteLine($"=== DDS PROBE 0x{npcFormID:X8} {npc.EditorID}  race=0x{npc.Record.Race:X8} {If(npc.Record.ConfigurationFlagsFemale, "F", "M")} ===")
        Dim rlay = SseFaceTintComposer.GetRaceLayersOrdered(pm, npc.Record.Race, npc.Record.ConfigurationFlagsFemale)
        Dim capasSse = SseFaceTintComposer.CapasDeTinteSse(npc.Record)
        Console.WriteLine($"  RACE layers = {If(rlay Is Nothing, -1, rlay.Count)}   NPC TINI = {capasSse.Where(Function(x) x.LayerTintIndexPresente).Count()}")

        ' Capas de la RACE una por una: es donde vive el color de un NPC SIN tints autorados. Se imprime el
        ' TIND (CLFM del preset default) y si RESUELVE, porque ResolveClfmColor DEGRADA A BLANCO en silencio
        ' cuando el formID es 0, el record no existe, no es CLFM, o no trae CNAM — y N capas hacia blanco
        ' saturan el acumulador a 255.
        If rlay IsNot Nothing AndAlso rlay.Count > 0 Then
            Console.WriteLine($"  --- RACE layers (TIND -> color) ---")
            Dim nBad = 0
            For Each L In rlay
                Dim st = "?"
                If L.DefaultClfm = 0UI Then
                    st = "TIND=0 -> WHITE"
                Else
                    Dim cr = pm.GetRecord(L.DefaultClfm)
                    If cr Is Nothing Then
                        st = "record MISSING -> WHITE"
                    ElseIf cr.Header.Signature <> "CLFM" Then
                        st = $"sig={cr.Header.Signature} (no CLFM) -> WHITE"
                    Else
                        Dim cnl = cr.Subrecords.Where(Function(s) s.Signature = "CNAM").ToList()
                        Dim cn = If(cnl.Count > 0, cnl(0), Nothing)
                        If cnl.Count = 0 OrElse cn.Data Is Nothing OrElse cn.Data.Length < 3 Then
                            st = "no CNAM -> WHITE"
                        Else
                            st = $"CNAM=({cn.Data(0)},{cn.Data(1)},{cn.Data(2)})"
                        End If
                    End If
                End If
                ' Si no resuelve, probar el MISMO id local bajo el indice del plugin del propio RACE. Si ahi
                ' SI aparece, el defecto no es "el record no existe" sino el REMAPEO del indice de master
                ' (auto-referencia: local index == cantidad de masters ⇒ el plugin se apunta a si mismo).
                If st.EndsWith("WHITE") AndAlso L.DefaultClfm <> 0UI Then
                    Dim selfIdx = npc.Record.Race And &HFF000000UI
                    Dim alt = selfIdx Or (L.DefaultClfm And &HFFFFFFUI)
                    Dim ar = pm.GetRecord(alt)
                    If ar IsNot Nothing Then
                        Dim acn = ar.Subrecords.Where(Function(s) s.Signature = "CNAM").ToList()
                        Dim acs = If(acn.Count > 0 AndAlso acn(0).Data IsNot Nothing AndAlso acn(0).Data.Length >= 3,
                                     $"CNAM=({acn(0).Data(0)},{acn(0).Data(1)},{acn(0).Data(2)})", "no CNAM")
                        st &= $"   [!] but 0x{alt:X8} DOES exist (sig={ar.Header.Signature} {acs}) => REMAP"
                    End If
                End If
                If st.EndsWith("WHITE") Then nBad += 1
                Console.WriteLine($"    idx={L.Index,3} TIND=0x{L.DefaultClfm:X8} val={L.DefaultValue:F3} presets={If(L.Presets Is Nothing, 0, L.Presets.Count),2} mask='{If(L.Path, "")}' -> {st}")
            Next
            Console.WriteLine($"  ==> layers degrading to WHITE: {nBad}/{rlay.Count}")
        End If

        ' Tints AUTORADOS del NPC (SSE = SseTintRaw: TINI/TINC/TINV/TIAS). `--tints` los muestra vacíos
        ' porque lee FaceTintLayers, que es el campo de FO4 — en un corpus de Skyrim da "(none)" SIEMPRE.
        ' Sin esto no se puede ver de dónde sale la variación del composite.
        If capasSse.Count > 0 Then
            Console.WriteLine("  --- NPC AUTHORED tints (idx -> color, coverage) ---")
            Dim ti As Integer = -1, tr As Integer = 0, tg As Integer = 0, tb As Integer = 0
            Dim tv As Double = 0
            Dim raceIdx As New HashSet(Of Integer)(If(rlay Is Nothing, New List(Of SseFaceTintComposer.SseTintMask), rlay).Select(Function(z) z.Index))
            For Each capa In capasSse
                If capa.LayerTintIndexPresente Then ti = CInt(capa.LayerTintIndex)
                If capa.TintColorRedPresente AndAlso capa.TintColorGreenPresente AndAlso capa.TintColorBluePresente Then
                    tr = capa.TintColorRed : tg = capa.TintColorGreen : tb = capa.TintColorBlue
                End If
                If capa.LayerInterpolationValuePresente Then tv = capa.LayerInterpolationValue / 100.0
                If capa.LayerPresetPresente Then
                    Dim inRace = raceIdx.Contains(ti)
                    Dim maskP = ""
                    If rlay IsNot Nothing Then
                        For Each L2 In rlay
                            If L2.Index = ti Then maskP = IO.Path.GetFileName(If(L2.Path, "")) : Exit For
                        Next
                    End If
                    Console.WriteLine($"    idx={ti,3} color=({tr},{tg},{tb}) coverage={tv:F3}  {If(inRace, "mask=" & maskP, "⛔ INDEX DOES NOT EXIST IN THE RACE -> layer IGNORED")}")
                    ti = -1 : tr = 0 : tg = 0 : tb = 0 : tv = 0
                End If
            Next
        End If

        Dim mineDds = SseFaceGenBaker.BakeFaceTintDds(pm, rec, race, npc.Record.Race, npc.Record.ConfigurationFlagsFemale, 512, 512,
                                                      dxgiFormat:=FO4_NPC_Manager.FaceGenBuilder.DiffuseDxgiFromSetting())
        Dim ckKey = ($"textures\actors\character\facegendata\facetint\{origin}\{fgL:X8}.dds").ToLowerInvariant()
        Dim ckDds = FilesDictionary_class.GetArchiveOriginalBytes(ckKey)
        If mineDds Is Nothing OrElse ckDds Is Nothing OrElse ckDds.Length = 0 Then
            Console.Error.WriteLine($"  missing side: mine={mineDds IsNot Nothing} ck={ckDds IsNot Nothing}") : Environment.ExitCode = 2 : Return
        End If
        Dim mine = FaceTintCpuCompositor.DecodeDds(mineDds, 512, 512), ckd = FaceTintCpuCompositor.DecodeDds(ckDds, 512, 512)
        For Each pair In {Tuple.Create("OURS   ", mine), Tuple.Create("CK     ", ckd)}
            Dim t = pair.Item2
            Dim hist As New Dictionary(Of String, Integer)
            For i = 0 To 512 * 512 - 1
                Dim k = $"{t.Unit(i * 4) * 255:F1},{t.Unit(i * 4 + 1) * 255:F1},{t.Unit(i * 4 + 2) * 255:F1}"
                hist(k) = If(hist.ContainsKey(k), hist(k), 0) + 1
            Next
            Console.WriteLine($"  {pair.Item1}: {hist.Count} colores distintos en 262144 px")
            For Each kv In hist.OrderByDescending(Function(z) z.Value).Take(6)
                Console.WriteLine($"      ({kv.Key})  x{kv.Value}  ({100.0 * kv.Value / 262144.0:F2}%)")
            Next
        Next
    End Sub

    ''' <summary>Los morfos por region de cara del record. Vacio cuando el record no es de Fallout 4,
    ''' que es el unico juego que declara esos subrecords.</summary>
    Private Function MorfosDeRegionDe(npc As Canon.INpc) As IReadOnlyList(Of Canon.NpcFO4_FaceMorphs)
        Dim nf = TryCast(npc, Canon.NpcFO4)
        If nf Is Nothing Then Return Array.Empty(Of Canon.NpcFO4_FaceMorphs)()
        Return nf.FaceMorphs
    End Function

    ''' <summary>Los siete valores de un morfo por region, en el orden del formato.</summary>
    Private Function ValoresDeMorfo(fm As Canon.NpcFO4_FaceMorphs) As Single()
        If fm Is Nothing Then Return Array.Empty(Of Single)()
        Return New Single() {fm.ValuesPositionX, fm.ValuesPositionY, fm.ValuesPositionZ,
                             fm.ValuesRotationX, fm.ValuesRotationY, fm.ValuesRotationZ, fm.ValuesScale}
    End Function

    Private Sub TintCountScan(pm As PluginManager)
        Dim csv As New List(Of String) From {"formID,origin,editorID,race,sex,tini,merged,tinc,tias,winner"}
        Dim n = 0, zero = 0
        For Each kv In pm.AllRecords
            Dim rec = kv.Value
            If rec Is Nothing OrElse rec.Header.Signature <> "NPC_" Then Continue For
            Dim npc = RecordParsers.ParseNPC(rec, pm)
            If npc Is Nothing Then Continue For
            n += 1
            Dim raceRec = pm.GetRecord(npc.Record.Race)
            Dim race = If(raceRec Is Nothing, Nothing, Canon.CanonRecords.Race(raceRec, pm))
            ' EL CAMPO CORRECTO POR JUEGO. `FaceTintLayers` es el de FO4; en SSE los tints viven en
            ' SseTintRaw (TINI/TINC/TINV/TIAS, RecordParsers:3122). La primera version de este scan leyo
            ' FaceTintLayers sobre un corpus de Skyrim y devolvio "0 capas en 6462/6462" — un 100% que
            ' delataba el error, porque el compositor SI produce facetints distintos por NPC. Un scan que
            ' mide el campo equivocado da un numero perfectamente formado y perfectamente falso.
            Dim isSseScan = (Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
            Dim capasSseScan = SseFaceTintComposer.CapasDeTinteSse(npc.Record)
            Dim npcN As Integer, mergedN As Integer
            If isSseScan Then
                npcN = capasSseScan.Where(Function(x) x.LayerTintIndexPresente).Count()
                mergedN = npcN   ' SSE no tiene merge con defaults de RACE por este camino
            Else
                npcN = FaceTintInputBuilder.CapasAutoradasDelRecord(npc.Record).Count
                Try
                    Dim scanGroups As List(Of GrupoDeTinteEfectivo) = Nothing
                    If race IsNot Nothing Then
                        scanGroups = TryCast(race, Canon.RaceFO4).TintesDelRecord(npc.Record.ConfigurationFlagsFemale)
                    End If
                    Dim merged = FaceTintInputBuilder.MergeTintLayersWithRaceDefaults(npc.Record, scanGroups, pm)
                    mergedN = If(merged Is Nothing, 0, merged.Count)
                Catch ex As Exception
                    ' No degradar a 0 en silencio: un merge que explota NO es "sin capas".
                    mergedN = -1
                    Console.Error.WriteLine($"[merge-FAIL] 0x{kv.Key:X8} {npc.EditorID}: {ex.GetType().Name}: {ex.Message}")
                End Try
            End If
            If mergedN = 0 Then zero += 1
            Dim nTinc = capasSseScan.Where(Function(x) x.TintColorRedPresente).Count()
            Dim nTias = capasSseScan.Where(Function(x) x.LayerPresetPresente).Count()
            ' winner = plugin que GANA el record del NPC. Si es POSTERIOR al que shippea su FaceGeom, el NIF/
            ' DDS del BA2 se horneo con datos VIEJOS: la referencia esta desactualizada, no nuestro bake.
            csv.Add($"0x{kv.Key:X8},{CsvSafe(pm.GetOriginatingPluginName(kv.Key))},{CsvSafe(npc.EditorID)}," &
                    $"0x{npc.Record.Race:X8},{If(npc.Record.ConfigurationFlagsFemale, "F", "M")},{npcN},{mergedN},{nTinc},{nTias},{CsvSafe(rec.SourcePluginName)}")
        Next
        ' CAPAS DE LA *RACE* por (raza,sexo). Es el dato que decide: con TINI=0 el compositor compone
        ' ENTERAMENTE desde los defaults de la RACE (SseFaceTintComposer: acc arranca en 0.5 y cada capa toma
        ' el color autorado del NPC o, si no hay, el default TIND→CLFM). Si para una raza esto da 0, emitimos
        ' el seed 0.5 PLANO y el CK emite el tono real de esa raza.
        If Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            Dim rl As New List(Of String) From {"race,sex,raceLayers"}
            Dim seen As New HashSet(Of String)
            For Each line In csv.Skip(1)
                Dim p = line.Split(","c)
                Dim k = p(3) & p(4)
                If Not seen.Add(k) Then Continue For
                Dim rfid = Convert.ToUInt32(p(3).Substring(2), 16)
                Dim n2 = SseFaceTintComposer.GetRaceLayersOrdered(pm, rfid, p(4) = "F")
                rl.Add($"{p(3)},{p(4)},{If(n2 Is Nothing, -1, n2.Count)}")
            Next
            Dim rp = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "racelayers.csv")
            IO.File.WriteAllLines(rp, rl)
            Console.WriteLine($"[racelayers] {rl.Count - 1} (race,gender) combinations -> {rp}")
        End If

        Dim outPath = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tintcount.csv")
        IO.File.WriteAllLines(outPath, csv)
        Console.WriteLine($"[tintcount] {n} NPC_ · with 0 effective layers: {zero} ({100.0 * zero / n:F1}%) -> {outPath}")
    End Sub

    Private Sub AlphaGateScan(pm As PluginManager)
        Dim isSse = (Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
        Console.WriteLine($"===== ALPHA GATE SCAN ({If(isSse, "SSE", "FO4")}) =====")

        ' ---------- (1) NPCs con ACBS\Diffuse Alpha Test (0x01000000) ----------
        ' SSE: el bit 24 no está documentado como usado en el schema ACBS de Skyrim, sin uso — se
        ' cuenta igual como CONTROL: si en SSE diera > 0 hay que revisar la premisa, no asumirla.
        Dim acbsNpcs As New List(Of String)
        Dim npcTotal = 0
        For Each kv In pm.AllRecords
            Dim r = kv.Value
            If r Is Nothing OrElse r.Header.Signature <> "NPC_" Then Continue For
            npcTotal += 1
            Dim npc = RecordParsers.ParseNPC(r, pm)
            If npc Is Nothing Then Continue For
            If (npc.Record.ConfigurationFlags And &H1000000UI) <> 0UI Then
                acbsNpcs.Add($"0x{kv.Key:X8} {npc.EditorID} (origin={pm.GetOriginatingPluginName(kv.Key)} ACBS=0x{npc.Record.ConfigurationFlags:X8})")
            End If
        Next
        Console.WriteLine($"[ACBS] NPC_ with 'Diffuse Alpha Test' (0x01000000): {acbsNpcs.Count} of {npcTotal}")
        For Each s In acbsNpcs : Console.WriteLine($"  {s}") : Next
        Console.WriteLine("[ACBS] VERDICT: 1 ⇒ a single agnostic switch serves both the SF2 bit and the" &
                          " fabrication of the NiAlphaProperty. >1 ⇒ they are DIFFERENT gates (the SF2 bit is on a single shape).")

        ' ---------- (2) TXST con MNAM y su alpha ----------
        Dim txstTotal = 0, withMnam = 0, mnamLoadFail = 0
        Dim alphaTxst As New Dictionary(Of UInteger, String)   ' FormID -> descripcion
        For Each kv In pm.AllRecords
            Dim r = kv.Value
            If r Is Nothing OrElse r.Header.Signature <> "TXST" Then Continue For
            txstTotal += 1
            Dim t = Canon.CanonRecords.Txst(r, pm)
            If t Is Nothing OrElse String.IsNullOrEmpty(t.MaterialDe()) Then Continue For
            withMnam += 1
            Dim mat = MaterialResolver.TryLoadMaterialFromDictionary(t.MaterialDe(), New FO4UnifiedMaterial_Class(), Nothing, Nothing)
            If mat Is Nothing Then
                ' NO degradar a "sin alpha": un MNAM que no carga es un dato DESCONOCIDO, no un cero.
                mnamLoadFail += 1
                Console.WriteLine($"  [MNAM-FAIL] txst=0x{kv.Key:X8} {t.EditorID} mnam='{t.MaterialDe()}' → DID NOT LOAD (alpha UNKNOWN)")
                Continue For
            End If
            ' El predicado es AlphaBlendEnabled (el booleano real), NO `AlphaBlendMode <> 0`: el enum tiene
            ' `Unknown` como default y su setter NO deriva los campos en ese caso, asi que compararlo contra
            ' cero mediria otra cosa. Los 3 campos que el resolver copia son AlphaTest / AlphaTestRef /
            ' AlphaBlendMode, y los dos que hacen VISIBLE el alpha son AlphaTest y AlphaBlendEnabled.
            If mat.AlphaTest OrElse mat.AlphaBlendEnabled Then
                alphaTxst(kv.Key) = $"0x{kv.Key:X8} {t.EditorID} mnam='{t.MaterialDe()}' alphaTest={mat.AlphaTest} ref={mat.AlphaTestRef} blendEnabled={mat.AlphaBlendEnabled} blendMode={mat.AlphaBlendMode}"
            End If
        Next
        Console.WriteLine($"[TXST] total={txstTotal} conMNAM={withMnam} MNAM-no-carga={mnamLoadFail} conALPHA={alphaTxst.Count}")
        For Each s In alphaTxst.Values : Console.WriteLine($"  {s}") : Next
        If withMnam = 0 Then
            Console.WriteLine("[TXST] 0 with MNAM ⇒ the MNAM block (and its alpha gate) is UNREACHABLE in this game.")
        End If

        ' ---------- (3) Quien referencia esos TXST ----------
        ' La pregunta es la del comentario de NpcMaterialResolver: cuantos consumidores de un TXST con MNAM
        ' que declara alpha NO son un head part de cara. PartType 0 = Face (HDPT.PartType).
        ' Face = 1, NO 0. La primera version de este scan puso 0 y habria mal-clasificado cualquier
        ' HDPT de cara como NO-CARA — justo la categoria que el scan existe para contar. El valor sale del
        ' schema del record (HDPT\PNAM 'Type': 0=Misc 1=Face 2=Eyes 3=Hair 4=Facial Hair 5=Scar
        ' 6=Eyebrows) y coincide con las constantes de la app (FaceGenBuilder.PartTypeFace=1, MainForm/
        ' NpcMaterialResolver.HeadPartTypeFace=1). El veredicto medido NO cambia: el unico referente HDPT
        ' del corpus es partType=2 (Eyes), NO-CARA con cualquiera de los dos valores.
        Const PartTypeFace As Integer = FO4_NPC_Manager.FaceGenBuilder.PartTypeFace
        Dim faceRefs = 0, nonFaceRefs = 0
        If alphaTxst.Count > 0 Then
            Console.WriteLine("[REF] referrers of the TXSTs with alpha:")
            For Each kv In pm.AllRecords
                Dim r = kv.Value
                If r Is Nothing Then Continue For
                Select Case r.Header.Signature
                    Case "HDPT"
                        Dim h = Canon.CanonRecords.Hdpt(r, pm)
                        If h Is Nothing OrElse h.TextureSet = 0UI OrElse Not alphaTxst.ContainsKey(h.TextureSet) Then Continue For
                        Dim isFace = (h.TipoDeParte() = PartTypeFace)
                        If isFace Then faceRefs += 1 Else nonFaceRefs += 1
                        Console.WriteLine($"  HDPT.TNAM 0x{kv.Key:X8} {h.EditorID} partType={h.TipoDeParte()} usesBodyTex={h.UsaTexturaDelCuerpo()} → txst=0x{h.TextureSet:X8}  [{If(isFace, "CARA", "NO-CARA")}]")
                    Case "NPC_"
                        Dim n = RecordParsers.ParseNPC(r, pm)
                        If n Is Nothing OrElse n.Record.HeadTexture = 0UI OrElse Not alphaTxst.ContainsKey(n.Record.HeadTexture) Then Continue For
                        faceRefs += 1   ' NPC.FTST ES la cara por definicion
                        Console.WriteLine($"  NPC.FTST   0x{kv.Key:X8} {n.EditorID} → txst=0x{n.Record.HeadTexture:X8}  [FACE]")
                    Case "RACE"
                        Dim rc = Canon.CanonRecords.Race(r, pm)
                        If rc Is Nothing Then Continue For
                        For Each fid In {rc.DefaultFaceTextureDe(False), rc.DefaultFaceTextureDe(True)}
                            If fid <> 0UI AndAlso alphaTxst.ContainsKey(fid) Then
                                faceRefs += 1
                                Console.WriteLine($"  RACE.DFT   0x{kv.Key:X8} {rc.EditorID} → txst=0x{fid:X8}  [FACE]")
                            End If
                        Next
                    Case "ARMA"
                        Dim a = Canon.CanonRecords.Arma(r, pm)
                        If a Is Nothing Then Continue For
                        For Each fid In {a.MaleSkinTexture, a.FemaleSkinTexture}
                            If fid <> 0UI AndAlso alphaTxst.ContainsKey(fid) Then
                                nonFaceRefs += 1
                                Console.WriteLine($"  ARMA.NAM0/1 0x{kv.Key:X8} {a.EditorID} slots=0x{a.SlotMaskDe():X8} → txst=0x{fid:X8}  [NON-FACE]")
                            End If
                        Next
                End Select
            Next
        End If
        Console.WriteLine($"[REF] referrers FACE={faceRefs}  NON-FACE={nonFaceRefs}")
        Console.WriteLine("[REF] VERDICT isFaceHeadPart: NON-FACE=0 ⇒ removing the gate is INERT over vanilla" &
                          " (it does not validate it: vanilla is a biased corpus). NON-FACE>0 ⇒ counterexample: today those shapes" &
                          " lose their material's alpha.")
        Console.WriteLine("===== END ALPHA GATE SCAN =====")
    End Sub

    Private Sub TtedScan(pm As PluginManager)
        ' Por raw-u32 de TTED: cuenta por EntryType + ejemplos. Asi se ve si TextureSet usa int-indice o
        ' float y si Palette alguna vez usa float. raw normal (>=0x00800000) = float real; raw chico = int.
        Dim byRaw As New Dictionary(Of UInteger, Dictionary(Of String, Integer))
        Dim examples As New Dictionary(Of UInteger, String)
        Dim nonPaletteTted As New List(Of String)   ' TextureSet/Mask (tc=0) con TTED -> listar todas
        Dim total As Integer = 0, races As Integer = 0, withTted As Integer = 0
        For Each rec In pm.GetRecordsOfType("RACE")
            Dim race As Canon.IRace = Nothing
            Try
                race = Canon.CanonRecords.Race(rec, pm)
            Catch
                Continue For
            End Try
            If race Is Nothing Then Continue For
            races += 1
            Dim raceFo4Tint = TryCast(race, Canon.RaceFO4)
            For Each gs In {raceFo4Tint.TintesDelRecord(False), raceFo4Tint.TintesDelRecord(True)}
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
        Console.WriteLine($"=== TTED SCAN: {races} RACE, {total} options, {withTted} with TTED ===")
        Console.WriteLine("by raw-u32 (float / int / denormal=int-index) -> EntryType breakdown:")
        For Each kv In byRaw.OrderBy(Function(x) x.Key)
            Dim raw = kv.Key
            Dim asFloat = BitConverter.ToSingle(BitConverter.GetBytes(raw), 0)
            Dim isNormalFloat = raw >= &H800000UI   ' >= smallest normal float -> es float real
            Dim kind = If(raw = 0UI, "zero", If(isNormalFloat, $"FLOAT={asFloat:G6}", $"INT-index={raw}(denormal)"))
            Dim ets = String.Join(", ", kv.Value.OrderByDescending(Function(x) x.Value).Select(Function(x) $"{x.Key}:{x.Value}"))
            Console.WriteLine($"  raw=0x{raw:X8}  {kind,-22}  [{ets}]   e.g.: {examples(raw)}")
        Next
        Console.WriteLine($"-- TextureSet/Mask (tc=0) WITH TTED ({nonPaletteTted.Count}) --")
        For Each s In nonPaletteTted : Console.WriteLine("  " & s) : Next
    End Sub

    ''' <summary>DIAGNOSTICO (--scandiff): recorre TODOS los NPC_ y reporta los que tienen alguna Palette
    ''' layer VISIBLE (disc=1, value>0) donde el BlendOp resuelto por el APP
    ''' (FaceTintPaletteResolver.ResolvePaletteLayerEffective, index/alpha-closest) DIFIERE del BlendOp
    ''' del CK (ResolveBlendOpCk: color-match exacto sobre TemplateColors, LAST gana, early-out por alpha
    ''' exacto). Net SkinTone: ambos motores fuerzan 0->3 en slot 12. Diagnostico puro, no escribe nada.</summary>
    Private Sub ScanDiff(pm As PluginManager)
        Console.WriteLine("=== SCANDIFF: app (index/alpha-closest) vs CK (color-match LAST-wins) per visible Palette layer ===")
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
                Dim npc = RecordParsers.ParseNPC(rec, pm)
                Dim capasDiff = FaceTintInputBuilder.CapasAutoradasDelRecord(npc?.Record)
                If npc Is Nothing OrElse capasDiff.Count = 0 Then Continue For
                withTints += 1
                Dim raceRec = pm.GetRecord(npc.Record.Race)
                If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Continue For
                Dim race = Canon.CanonRecords.Race(raceRec, pm)
                If race Is Nothing Then Continue For
                scanned += 1
                Dim isFemale = npc.Record.ConfigurationFlagsFemale
                Dim npcHadDiff As Boolean = False
                Dim diffGroups = TryCast(race, Canon.RaceFO4).TintesDelRecord(isFemale)

                For Each tl In capasDiff
                    If tl Is Nothing OrElse tl.Discriminator <> 1US OrElse tl.Value <= 0 Then Continue For
                    Dim opt = diffGroups.BuscarOpcion(tl.Index)
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

        Console.WriteLine($"=== SCANDIFF summary: {withTints} NPCs with FaceTintLayers, {scanned} with resolved RACE (scanned), {diffLines} DIFF lines in {diffNpcs} NPCs ===")
        If groupCounts.Count > 0 Then
            Console.WriteLine("-- by (slot, app->ck) --")
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
        Console.WriteLine($"[findhkx] '{substr}': {keys.Count} .hkx/.hkt files in the load order")
        Dim parsed = 0
        For Each k In keys
            Dim bytes = FilesDictionary_class.GetBytes(k)
            If bytes Is Nothing OrElse bytes.Length = 0 Then Console.WriteLine($"  {k}  (does not load)") : Continue For
            Dim tags As New List(Of String)
            Try
                Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(bytes))
                For Each o In sg.GetObjectsByClassName("hkaSkeleton")
                    Dim sk = sg.ParseSkeleton(o)
                    If sk Is Nothing OrElse sk.Bones Is Nothing Then Continue For
                    Dim faceB = sk.Bones.Select(Function(b) b.Name).Where(Function(n) Not String.IsNullOrEmpty(n) AndAlso rxFace.IsMatch(n)).Take(30).ToList()
                    tags.Add($"hkaSkeleton '{sk.Name}' bones={sk.Bones.Count}{If(faceB.Count > 0, " FACE-BONES(" & faceB.Count & "):[" & String.Join(",", faceB) & "]", " (no face-bones)")}")
                    If keys.Count <= 2 Then Console.WriteLine($"     ALL bones [{sk.Bones.Count}]: {String.Join(", ", sk.Bones.Select(Function(b) b.Name))}")
                Next
                For Each o In sg.GetObjectsByClassName("hkbCharacterStringData")
                    Dim csd = sg.ParseCharacterStringData(o)
                    If csd IsNot Nothing Then tags.Add($"character '{csd.CharacterName}' rig='{csd.RigName}'")
                Next
            Catch ex As Exception
                tags.Add("(parse fail: " & ex.GetType().Name & ")")
            End Try
            parsed += 1
            Console.WriteLine($"  {k}  →  {If(tags.Count > 0, String.Join("  |  ", tags), "(no skeleton/character)")}")
        Next
        Console.WriteLine($"[findhkx] {parsed} parsed.")
    End Sub

    ''' <summary>Valida el armado del skeleton base como lo hace PrepareSkeleton: LoadFromBytes(nif) +
    ''' MergeHkxSkeleton(hkx). Dumpea pre/post bone count, cuántos mergeó, InjectedBones (debe ser 0 sin
    ''' shapes/cloth), y el world de bones clave (Root + chunk-bones de robot) para confirmar que quedan en
    ''' la posición ensamblada del HKX.</summary>
    Private Sub ValidateMergeHkx(label As String, hkxPath As String, nifPath As String)
        Dim nifBytes = LoadAnimCand(nifPath) : Dim hkxBytes = LoadAnimCand(hkxPath)
        If nifBytes Is Nothing OrElse hkxBytes Is Nothing Then
            Console.WriteLine($"  [MERGE-VALIDATE] missing file (nif={nifBytes IsNot Nothing}, hkx={hkxBytes IsNot Nothing})") : Return
        End If
        Dim s As New SkeletonInstance()
        If Not s.LoadFromBytes(nifBytes) Then Console.WriteLine("  [MERGE-VALIDATE] LoadFromBytes(nif) failed") : Return
        Dim pre = s.SkeletonDictionary.Count
        Dim merged = s.MergeHkxSkeleton(hkxBytes)
        Dim post = s.SkeletonDictionary.Count
        Console.WriteLine($"  [MERGE-VALIDATE] {label}: NIF={pre} bones → +HKX merge={merged} → total={post} | InjectedBones={s.InjectedBones.Count} (expected 0)")
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
        If b Is Nothing Then Console.WriteLine($"[hkxbone] '{hkxPath}' does not load") : Return
        Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(b))
        Dim skels = g.GetObjectsByClassName("hkaSkeleton").Select(Function(o) g.ParseSkeleton(o)).Where(Function(s) s IsNot Nothing).ToList()
        Dim skel = skels.FirstOrDefault(Function(s) Not If(s.Name, "").Contains("Ragdoll", StringComparison.OrdinalIgnoreCase))
        If skel Is Nothing Then Console.WriteLine("[hkxbone] no animation hkaSkeleton") : Return
        Console.WriteLine($"[hkxbone] '{hkxPath}' skel='{skel.Name}' bones={skel.Bones.Count} | filter='{boneSubstr}'")
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

#Region "--facegengate: blast radius of the FaceGen gate change"

    ''' <summary>Fila por NPC de la clasificación de la parte A.</summary>
    Private Class FggRow
        Public Fid As UInteger                  ' NPC_ propio
        Public SrcFid As UInteger               ' fuente de apariencia (cadena Use Traits)
        Public Eid As String = ""
        Public RaceFid As UInteger
        Public RaceEid As String = ""
        Public IsFemale As Boolean
        Public RaceBit As Boolean               ' RACE.DATA 0x2
        Public HasFaceGeom As Boolean           ' FaceGeom horneado presente en el árbol
        Public HasFmrs As Boolean               ' FMRS con al menos un valor != 0
        Public HasChargen As Boolean            ' MSDK/MSDV
        Public HasWeight As Boolean             ' MWGT efectivo (mismo gate que BuildBakeBodyWeightPose)
        ''' <summary>Entra NUEVO al head-bake por este cambio.</summary>
        Public ReadOnly Property IsNew As Boolean
            Get
                Return RaceBit AndAlso Not HasFaceGeom
            End Get
        End Property
        ''' <summary>El bake no tiene NADA que aplicar ⇒ el cambio es estructuralmente no-op para este NPC.</summary>
        Public ReadOnly Property Inert As Boolean
            Get
                Return Not HasFmrs AndAlso Not HasChargen AndAlso Not HasWeight
            End Get
        End Property
    End Class

    ''' <summary>Mide el blast radius de gatear el FaceGen del render por <c>RACE.DATA</c> bit 0x2 en vez de
    ''' por "¿existe el FaceGeom horneado?". Parte A clasifica TODOS los NPC_ del load order y aísla el
    ''' conjunto que cambia; parte B corre sobre una muestra el MISMO cálculo que el render y mide
    ''' |horneado − crudo| por vértice, que es literalmente cuánto se mueve la cabeza dibujada.
    ''' <para>READ-ONLY a propósito: no pasa por el bake, así que no puede ensuciar el árbol del juego con
    ''' sueltos que después sombreen el BA2 (ver 10-stack-arnes-de-medicion.md).</para>
    ''' <para>Corre sobre el record CRUDO, sin overlays de LooksMenu: un NPC con FMRS sólo en un preset
    ''' cuenta como "sin FMRS", así que SUBestima el conjunto que se mueve.</para></summary>
    Private _fggEdidFilter As String = ""

    Private Sub FaceGenGateBlastRun(pm As PluginManager, sampleN As Integer)
        Console.WriteLine("=== --facegengate: blast radius of the FaceGen gate (heuristic → RACE.DATA bit 0x2) ===")
        Console.WriteLine("Changing set = race with bit 0x2 AND WITHOUT a baked FaceGeom (before: no _faceBones input ⇒ no head-bake).")
        Console.WriteLine()

        ' ---------------------------------------------------------------- Parte A
        Dim rows As New List(Of FggRow)
        Dim parseFail As Integer = 0
        Dim npcCache As New Dictionary(Of UInteger, NPC_Data)

        Dim parseNpc = Function(fid As UInteger) As NPC_Data
                           If fid = 0UI Then Return Nothing
                           Dim cached As NPC_Data = Nothing
                           If npcCache.TryGetValue(fid, cached) Then Return cached
                           Dim rec = pm.GetRecord(fid)
                           If rec Is Nothing OrElse rec.Header.Signature <> "NPC_" Then npcCache(fid) = Nothing : Return Nothing
                           Dim parsed As NPC_Data = Nothing
                           Try : parsed = RecordParsers.ParseNPC(rec, pm) : Catch : parsed = Nothing : End Try
                           npcCache(fid) = parsed
                           Return parsed
                       End Function

        For Each rec In pm.GetNPCs()
            Dim npc = parseNpc(rec.Header.FormID)
            If npc Is Nothing Then parseFail += 1 : Continue For

            ' Fuente de apariencia = cadena "Use Traits" (misma regla que NpcStateResolver.ResolveTraitsStateFromNPC
            ' → NpcStateFactory.FaceAppearanceSourceFormID). El FaceGeom y la raza salen de la FUENTE.
            Dim src = npc
            Dim visited As New HashSet(Of UInteger) From {npc.FormID}
            Do While FO4_NPC_Manager.NpcTemplateHelpers.HasTemplateFlag(src.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.Traits)
                Dim nextFid = FO4_NPC_Manager.NpcTemplateHelpers.ResolveTemplateSourceFormID(src, NPC_TemplateCategory.Traits)
                If nextFid = 0UI OrElse visited.Contains(nextFid) Then Exit Do
                Dim nxt = parseNpc(nextFid)
                If nxt Is Nothing Then Exit Do          ' apunta a LVLN u otro tipo ⇒ se queda con el propio
                visited.Add(nextFid)
                src = nxt
            Loop

            Dim raceEid = ""
            Dim raceRec = pm.GetRecord(src.Record.Race)
            If raceRec IsNot Nothing Then raceEid = If(Canon.CanonRecords.Race(raceRec, pm)?.EditorID, "")

            ' FaceGeom horneado: MISMA convención de nombre que usaba HasFaceGenAssets (local FormID ESL-aware).
            Dim geomKey = ""
            Dim plug = pm.GetOriginatingPluginName(src.FormID)
            If Not String.IsNullOrEmpty(plug) Then
                geomKey = $"meshes\actors\character\facegendata\facegeom\{plug}\{PluginManager.ToFaceGenLocalFormID(src.FormID):X8}.nif".ToLowerInvariant()
            End If

            Dim wt = src.Record.PesoDelCuerpo(0) : Dim wm = src.Record.PesoDelCuerpo(1) : Dim wf = src.Record.PesoDelCuerpo(2)
            rows.Add(New FggRow With {
                .Fid = npc.FormID,
                .SrcFid = src.FormID,
                .Eid = If(npc.EditorID, ""),
                .RaceFid = src.Record.Race,
                .RaceEid = raceEid,
                .IsFemale = src.Record.ConfigurationFlagsFemale,
                .RaceBit = RaceUtil.RaceSupportsFaceGen(src.Record.Race, pm),
                .HasFaceGeom = (geomKey <> "" AndAlso FilesDictionary_class.Dictionary.ContainsKey(geomKey)),
                .HasFmrs = MorfosDeRegionDe(src.Record).Any(Function(fm) ValoresDeMorfo(fm).Any(Function(x) Math.Abs(x) > 0.0001F)),
                .HasChargen = (src.Record.MorfosDeCara().Count > 0),
                .HasWeight = (wt.HasValue AndAlso wm.HasValue AndAlso wf.HasValue AndAlso (wt.Value + wm.Value + wf.Value) >= 0.001F)
            })
        Next

        Dim newSet = rows.Where(Function(r) r.IsNew).ToList()
        Dim movers = newSet.Where(Function(r) Not r.Inert).ToList()

        Console.WriteLine($"NPC_ in the load order : {rows.Count}  (ParseNPC failed on {parseFail}, excluded)")
        Console.WriteLine($"  race WITHOUT bit 0x2             : {rows.Where(Function(r) Not r.RaceBit).Count(),6}   unchanged (they already left through the head-parts early return)")
        Console.WriteLine($"  bit 0x2 + WITH baked FaceGeom    : {rows.Where(Function(r) r.RaceBit AndAlso r.HasFaceGeom).Count(),6}   unchanged (they already entered the head-bake)")
        Console.WriteLine($"  bit 0x2 + NO FaceGeom   = NEW    : {newSet.Count,6}   ⬅ the blast radius")
        Console.WriteLine()
        ' CONJUNTO INVERSO: raza SIN bit 0x2 pero CON FaceGeom horneado. Bajo la regla VIEJA daban
        ' useFaceGen=True; bajo la nueva dan False. En el camino de HEAD PARTS da igual (el early-return por
        ' el bit ya los excluía), pero CollectArmoCandidates NO tiene ese early-return ⇒ estos PIERDEN el
        ' insumo `_faceBones` de la ARMA. Engine-faithful: sin el bit el motor no arma cabeza facegen, así
        ' que perderlo es correcto — pero hay que saber a cuántos toca.
        Dim losers = rows.Where(Function(r) Not r.RaceBit AndAlso r.HasFaceGeom).ToList()
        Console.WriteLine($"  INVERSE — race WITHOUT bit 0x2 but WITH FaceGeom : {losers.Count,6}   (they lose the ARMA's _faceBones input)")
        If losers.Count > 0 Then
            For Each g In losers.GroupBy(Function(r) If(r.RaceEid, "?")).OrderByDescending(Function(g2) g2.Count()).Take(8)
                Console.WriteLine($"        {g.Count(),6}  {g.Key}")
            Next
        End If
        Console.WriteLine()
        Console.WriteLine($"  of the NEW ones, STRUCTURAL no-op (no FMRS, no morphs, no MWGT): {newSet.Where(Function(r) r.Inert).Count(),6}")
        Console.WriteLine($"  of the NEW ones, with some bake input     = THEY MOVE            : {movers.Count,6}")
        Console.WriteLine($"      with FMRS  : {newSet.Where(Function(r) r.HasFmrs).Count(),6}")
        Console.WriteLine($"      with morphs: {newSet.Where(Function(r) r.HasChargen).Count(),6}")
        Console.WriteLine($"      with MWGT  : {newSet.Where(Function(r) r.HasWeight).Count(),6}")
        Console.WriteLine()

        Console.WriteLine("NEW by race (top 15):")
        For Each g In newSet.GroupBy(Function(r) If(r.RaceEid, "?")).OrderByDescending(Function(g2) g2.Count()).Take(15)
            Console.WriteLine($"   {g.Count(),6}  {g.Key}")
        Next
        Console.WriteLine()

        ' ---------------------------------------------------------------- Parte B
        If sampleN <= 0 OrElse movers.Count = 0 Then
            Console.WriteLine("(part B skipped: --fggsample 0, or no NPCs move)")
            Return
        End If

        ' Muestra determinista y REPARTIDA por el conjunto (paso uniforme), no los primeros N: los primeros
        ' N vienen ordenados por FormID y quedarían todos del mismo plugin/población.
        Dim ordered = movers.OrderBy(Function(r) r.SrcFid).ToList()
        Dim take = Math.Min(sampleN, ordered.Count)
        Dim sample As New List(Of FggRow)
        For k = 0 To take - 1
            sample.Add(ordered(CInt(CLng(k) * (ordered.Count - 1) \ Math.Max(1, take - 1))))
        Next
        sample = sample.GroupBy(Function(r) r.SrcFid).Select(Function(g) g.First()).ToList()

        ' --edid <substr>: fuerza a incluir en la muestra los NPCs cuyo EditorID matchea (verificación dirigida
        ' de un caso puntual sin depender de que el paso uniforme lo agarre).
        If _fggEdidFilter <> "" Then
            For Each extra In movers.Where(Function(r) r.Eid.IndexOf(_fggEdidFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                If Not sample.Any(Function(x) x.SrcFid = extra.SrcFid) Then sample.Insert(0, extra)
            Next
        End If

        Console.WriteLine($"--- Part B: geometry A/B over {sample.Count} of the {movers.Count} that move ---")
        Console.WriteLine("delta = |baked vertex − raw vertex| of the FLAT NIF, in NIF units. It is exactly")
        Console.WriteLine("what the preview starts showing extra for these NPCs.")
        Console.WriteLine()
        Console.WriteLine($"{"NPC",-10} {"shapes",6} {"rms",10} {"max",10} {"ms",8}  editorID")

        Dim allRms As New List(Of Double)
        Dim allMax As New List(Of Double)
        Dim allMs As New List(Of Double)
        Dim allAdded As New List(Of Double)
        Dim noInput As Integer = 0
        Dim bakeFail As Integer = 0
        ' NPCs donde ALGUNA shape de una malla apareó y otra NO. Importa porque la app tiene guarda
        ' "todas o ninguna" por malla (BuildHeadBakeService pasada 1): ahí la app NO hornea esa malla,
        ' mientras este probe sí mide las que aparean ⇒ si esto es > 0, mis rms SOBRESTIMAN a la app.
        Dim partialPair As Integer = 0

        For Each r In sample
            Dim raceRec = pm.GetRecord(r.RaceFid)
            If raceRec Is Nothing Then Continue For
            Dim race = Canon.CanonRecords.Race(raceRec, pm)
            Dim regions = FO4_NPC_Manager.NpcMorphPoseResolver.GetFacialBoneRegionsForFmriResolution(race, r.IsFemale)
            Dim st = FO4_NPC_Manager.FaceGenBuildPipeline.BuildBakeState(r.SrcFid, pm,
                                                         New Dictionary(Of UInteger, FO4_NPC_Manager.LooksmenuLoader.LooksmenuPreset)(), regions)
            If st Is Nothing Then bakeFail += 1 : Continue For

            Dim merged = FO4_NPC_Manager.HeadPartResolver.MergeHeadPartsWithRaceDefaults(r.RaceFid, r.IsFemale, st.NpcData.Record.PartesDeCabeza(), pm)
            Dim sw = System.Diagnostics.Stopwatch.StartNew()
            Dim msAdded As Double = 0
            Dim meshesWithFbns As Integer = 0
            Dim meshesNoFbns As Integer = 0
            Dim pairFails As Integer = 0
            Dim shapesMeasured As Integer = 0
            Dim sumSq As Double = 0 : Dim nVerts As Long = 0 : Dim maxD As Double = 0

            Dim verbose As Boolean = False   ' poner True para trazar el apareo shape↔_faceBones
            If verbose Then Console.WriteLine($"   [traza 0x{r.SrcFid:X8}] headParts={merged.Count}")
            For Each entry In FO4_NPC_Manager.HeadPartResolver.EnumerateHdptChain(merged, pm)
                Dim hd = entry.Hdpt
                If hd Is Nothing OrElse String.IsNullOrEmpty(hd.ModelFileName) Then Continue For
                Dim flatKey = FO4_NPC_Manager.NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(hd.ModelFileName)
                Dim fbnsKey = FO4_NPC_Manager.MeshPathHelpers.TryGetFaceBonesVariant(flatKey)
                If verbose Then Console.WriteLine($"      hdpt='{hd.EditorID}' mesh='{hd.ModelFileName}' flatKey='{flatKey}' inDict={FilesDictionary_class.Dictionary.ContainsKey(flatKey)} fbns='{fbnsKey}'")
                If fbnsKey = "" Then meshesNoFbns += 1 : Continue For   ' sin `_faceBones` no hay insumo ⇒ sin cambio
                meshesWithFbns += 1

                Dim flatBytes = FO4_NPC_Manager.MeshPathHelpers.TryLoadMeshBytes(flatKey)
                Dim fbnsBytes = FO4_NPC_Manager.MeshPathHelpers.TryLoadMeshBytes(fbnsKey)
                If flatBytes Is Nothing OrElse fbnsBytes Is Nothing Then Continue For

                ' El NIF PLANO se carga IGUAL hoy (es el que se dibuja) ⇒ su costo NO es agregado.
                ' Lo AGREGADO por este cambio es: cargar el `_faceBones` + hornear.
                Dim flatNif As New Nifcontent_Class_Manolo() : flatNif.Load_Manolo(flatBytes)
                Dim swAdd = System.Diagnostics.Stopwatch.StartNew()
                Dim fbnsNif As New Nifcontent_Class_Manolo() : fbnsNif.Load_Manolo(fbnsBytes)
                swAdd.Stop() : msAdded += swAdd.Elapsed.TotalMilliseconds

                ' Apareo shape plana ↔ `_faceBones` por nombre + conteo de vértices: la MISMA regla del
                ' driver del motor (0x140AA31B0) que usa BuildHeadBakeService.
                Dim fbnsByName As New Dictionary(Of String, NiflySharp.INiShape)(StringComparer.OrdinalIgnoreCase)
                For Each fs In fbnsNif.GetShapes()
                    Dim fn = FO4_NPC_Manager.NameUtils.StripFaceBonesSuffix(If(fs.Name?.String, ""))
                    If fn = "" Then Continue For
                    fbnsByName($"{fn}|{ShapeGeometryFactory.[For](fs, fbnsNif).VertexCount}") = fs
                Next

                For Each flatShape In flatNif.GetShapes()
                    Dim nm = $"{FO4_NPC_Manager.NameUtils.StripFaceBonesSuffix(If(flatShape.Name?.String, ""))}|{ShapeGeometryFactory.[For](flatShape, flatNif).VertexCount}"
                    Dim fbnsShape As NiflySharp.INiShape = Nothing
                    If Not fbnsByName.TryGetValue(nm, fbnsShape) Then
                        pairFails += 1
                        If verbose Then Console.WriteLine($"         shape '{nm}': NO match in the _faceBones")
                        Continue For
                    End If

                    Dim origVerts = ShapeGeometryFactory.[For](flatShape, flatNif).GetVertexPositions().ToList()
                    If origVerts.Count = 0 Then Continue For
                    Dim fbnsCount = ShapeGeometryFactory.[For](fbnsShape, fbnsNif).GetVertexPositions().Count
                    If fbnsCount <> origVerts.Count Then
                        If verbose Then Console.WriteLine($"         shape '{nm}': different count flat={origVerts.Count} fbns={fbnsCount}")
                        Continue For
                    End If

                    Dim baked As List(Of System.Numerics.Vector3) = Nothing
                    Dim swBake = System.Diagnostics.Stopwatch.StartNew()
                    Try
                        baked = FO4_NPC_Manager.FaceGenBuildPipeline.ComputeBakedVertices(
                            st, flatNif, flatShape, fbnsNif, fbnsShape,
                            hd.ArchivoDeDeformacion(2UI), srcNif:=flatNif, srcShape:=flatShape,
                            raceMorphTriPath:=hd.ArchivoDeDeformacion(0UI))
                        If verbose AndAlso baked Is Nothing Then Console.WriteLine($"         shape '{nm}': ComputeBakedVertices returned Nothing")
                    Catch ex As Exception
                        If verbose Then Console.WriteLine($"         shape '{nm}': EXCEPTION {ex.GetType().Name}: {ex.Message}")
                        baked = Nothing
                    End Try
                    swBake.Stop() : msAdded += swBake.Elapsed.TotalMilliseconds
                    If baked Is Nothing OrElse baked.Count <> origVerts.Count Then Continue For

                    For i = 0 To origVerts.Count - 1
                        Dim d As Double = (baked(i) - origVerts(i)).Length()
                        sumSq += d * d : nVerts += 1
                        If d > maxD Then maxD = d
                    Next
                    shapesMeasured += 1
                Next
            Next

            sw.Stop()
            If shapesMeasured = 0 Then
                noInput += 1
                    Continue For
            End If
            Dim rms = Math.Sqrt(sumSq / Math.Max(1L, nVerts))
            allRms.Add(rms) : allMax.Add(maxD) : allMs.Add(sw.Elapsed.TotalMilliseconds) : allAdded.Add(msAdded)
            If pairFails > 0 Then partialPair += 1
        Next

        Console.WriteLine()
        If allRms.Count = 0 Then
            Console.WriteLine($"No shape measured (no `_faceBones` available on {noInput}, bakeState failed on {bakeFail}).")
            Return
        End If
        Console.WriteLine($"Measured {allRms.Count} NPCs · no `_faceBones` input {noInput} · bakeState failed {bakeFail}")
        Console.WriteLine($"  rms:  mean {allRms.Average():F4}   median {allRms.OrderBy(Function(x) x).ElementAt(allRms.Count \ 2):F4}   max {allRms.Max():F4}")
        Console.WriteLine($"  max:  mean {allMax.Average():F4}   worst {allMax.Max():F4}")
        Console.WriteLine($"  NPCs with delta ~0 (rms < 1e-4): {allRms.Where(Function(x) x < 0.0001).Count(),4} de {allRms.Count}")
        Console.WriteLine($"  NPCs with a PARTIAL match (the app would skip them by its all-or-none guard): {partialPair,4} of {allRms.Count}")
        Dim msSorted = allMs.OrderBy(Function(x) x).ToList()
        Console.WriteLine($"  COST of the head-bake (NIF load + bake, offline, cold cache per NPC):")
        Console.WriteLine($"     A/B TOTAL (includes loading the flat NIF, which is ALREADY paid today):")
        Console.WriteLine($"        mean {allMs.Average():F0} ms · median {msSorted(msSorted.Count \ 2):F0} ms · p90 {msSorted(CInt(msSorted.Count * 0.9)):F0} ms · worst {allMs.Max():F0} ms")
        Dim addSorted = allAdded.OrderBy(Function(x) x).ToList()
        Console.WriteLine($"     ⭐ ADDED by the change (load the `_faceBones` + bake, PER NPC):")
        Console.WriteLine($"        mean {allAdded.Average():F0} ms · median {addSorted(addSorted.Count \ 2):F0} ms · p90 {addSorted(CInt(addSorted.Count * 0.9)):F0} ms · worst {allAdded.Max():F0} ms")
    End Sub

#End Region

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
        If keys.Count > 300 Then Console.WriteLine($"  ... ({keys.Count - 300} more)")
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
        ' Set de animaciones referenciadas por records IDLE (GNAM = 'Animation File').
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
        Console.WriteLine($"[idle] IDLE records={idleRecs.Count} | with GNAM(file)={idleWithGnam} ({idleGnamBases.Count} basenames) | with ENAM(event)={idleWithEnam} ({idleEvents.Count} events) | with DNAM={idleWithDnam} ({idleDnam.Count} distinct)")
        ' IDLE records relacionados con el pool PoseA (talk/dialogue/listen/flavor/patrol/pose) — ver su estructura REAL.
        Dim poseIdles = idleRecs.Where(Function(r) {r.EditorID}.Concat(r.Subrecords.Where(Function(s) s.Data IsNot Nothing).Select(Function(s) System.Text.Encoding.ASCII.GetString(s.Data).TrimEnd(ChrW(0)))).
                                                Any(Function(t) t IsNot Nothing AndAlso System.Text.RegularExpressions.Regex.IsMatch(t, "(?i)posea|_talk|dialogue|listen|flavor|patrolsearch"))).Take(10).ToList()
        Console.WriteLine($"[idle] IDLE related to PoseA/talk/dialogue ({poseIdles.Count} sample):")
        For Each r In poseIdles
            Dim fields = r.Subrecords.Where(Function(s) s.Data IsNot Nothing AndAlso (s.Signature = "DNAM" OrElse s.Signature = "ENAM" OrElse s.Signature = "GNAM")).
                            Select(Function(s) s.Signature & "='" & System.Text.Encoding.ASCII.GetString(s.Data).TrimEnd(ChrW(0)) & "'")
            Console.WriteLine($"      {r.EditorID}: {String.Join(" ", fields)}")
        Next
        ' Eventos ENAM relacionados a talk/dialogue (¿el behavior tiene un evento que dispare estos gestos?).
        Dim talkEvents = idleEvents.Where(Function(e) System.Text.RegularExpressions.Regex.IsMatch(e, "(?i)talk|dialogue|gesture|pose|listen")).Take(20)
        Console.WriteLine($"[idle] ENAM events talk/dialogue/gesture: {String.Join(", ", talkEvents)}")
        ' PATRONES GNAM: token $(Subgraph)/$(...) + wildcard * → el mecanismo ESTRUCTURAL de los gestos PoseA.
        Dim withToken = idleGnamRaw.Where(Function(g) g.Contains("$(") OrElse g.Contains("*")).ToList()
        Console.WriteLine($"[idle] distinct raw GNAM={idleGnamRaw.Count} | with token/wildcard={withToken.Count}:")
        For Each g In withToken.Take(40) : Console.WriteLine($"        GNAM-pat: {g}") : Next
        ' Patrones IDLE relacionados a furniture-entry/exit/sync (para cazar los 291 furniture-direccional residuales).
        Dim furnPats = idleGnamRaw.Where(Function(g) System.Text.RegularExpressions.Regex.IsMatch(g, "(?i)enter|exit|sync|furniture|sit|chair|getup|getin")).ToList()
        Console.WriteLine($"[idle] GNAM enter/exit/sync/furniture ({furnPats.Count}):")
        For Each g In furnPats.Take(60) : Console.WriteLine($"        FURN-pat: {g}") : Next
        For Each rec In pm.GetRecordsOfType("RACE")
            Dim race As Canon.IRace = Nothing
            Try : race = Canon.CanonRecords.Race(rec, pm) : Catch : Continue For : End Try
            If race Is Nothing Then Continue For
            If race.MaleBehaviorGraphModelFileName = "" AndAlso race.FemaleBehaviorGraphModelFileName = "" Then Continue For
            If edidFilter <> "" AndAlso race.EditorID.IndexOf(edidFilter, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            Dim rb = RaceBehaviorResolver.ResolveRaceBehavior(race.FormID, pm)
            If rb Is Nothing Then Continue For
            Dim clips = BehaviorClipEnumerator.EnumerateClips(rb, loader)
            Console.WriteLine($"===== {race.EditorID} [0x{race.FormID:X8}] | subgraphs={rb.Subgraphs.Count} | clips(dedup-file)={clips.Count} =====")
            ' Cobertura por FUENTE: behavior-walk vs IDLE-pattern (con Category) vs folder-scan (sin Category).
            Dim nBeh = clips.Where(Function(c) c.FromBehaviorGraph).Count()
            Dim nIdle = clips.Where(Function(c) Not c.FromBehaviorGraph AndAlso Not String.IsNullOrEmpty(c.Category)).Count()
            Dim nFolder = clips.Where(Function(c) Not c.FromBehaviorGraph AndAlso String.IsNullOrEmpty(c.Category)).Count()
            Console.WriteLine($"  SOURCE: behavior-walk={nBeh} | IDLE-pattern(with category)={nIdle} | clip-gen-variant={nFolder} | rb.IdleAnimations(patterns)={rb.IdleAnimations.Count}")
            ' Over-inclusion: clips cuyo path NO está bajo la carpeta propia del actor (ni _1stPerson). Para robots de
            ' carpeta dedicada debería ser ~0 (si trae Actors\Character\… o de otro actor = bug de gating).
            Dim ownPrefix = CanonHkx(DirNameC(rb.Project) & "\Animations\")
            Dim foreign = clips.Where(Function(c) Not CanonHkx(c.AnimationFile).StartsWith(ownPrefix, StringComparison.OrdinalIgnoreCase) AndAlso CanonHkx(c.AnimationFile).IndexOf("\_1stperson\", StringComparison.OrdinalIgnoreCase) < 0).ToList()
            Console.WriteLine($"  OVER-INCLUSION: clips OUTSIDE of '{DirNameC(rb.Project)}\Animations\' = {foreign.Count}" & If(foreign.Count = 0, "", " → " & String.Join(" ; ", foreign.Take(6).Select(Function(c) TopSegOf(FolderRelOf(c.AnimationFile)) & ":" & System.IO.Path.GetFileName(c.AnimationFile)))))
            Dim catTop = clips.Where(Function(c) Not String.IsNullOrEmpty(c.Category)).GroupBy(Function(c) c.Category).OrderByDescending(Function(g) g.Count()).Take(20)
            Console.WriteLine($"  IDLE categories: " & String.Join(" ; ", catTop.Select(Function(g) $"{g.Key}={g.Count()}")))
            ' ── OUTLIERS: clips por PROFUNDIDAD de carpeta (0=cuelgan directo de Animations\; 1=directo bajo un top-seg
            '    ej Weapon\X.hkx sin subtipo). Acá la jerarquía Role→carpeta no tiene niveles → revisar que categoricen bien.
            Dim depthOf = Function(p As String) As Integer
                              Dim fr = FolderRelOf(p)
                              Return If(fr = "" OrElse fr = "(top)", 0, fr.Split("\"c).Length)
                          End Function
            Dim byDepth = clips.GroupBy(Function(c) depthOf(c.AnimationFile)).OrderBy(Function(g) g.Key).ToList()
            Console.WriteLine($"  OUTLIERS depth: " & String.Join(" ; ", byDepth.Select(Function(g) $"depth{g.Key}={g.Count()}")))
            Dim tops = clips.Where(Function(c) depthOf(c.AnimationFile) = 0).ToList()
            Console.WriteLine($"  (top) hang directly off Animations\\ ({tops.Count}) — roles/cat:")
            For Each grp In tops.GroupBy(Function(c) "[" & String.Join(",", c.Roles) & "]" & If(c.Category <> "", "/" & c.Category, "")).OrderByDescending(Function(x) x.Count()).Take(10)
                Console.WriteLine($"        {grp.Count(),4}  {grp.Key}  e.g.: {String.Join(", ", grp.Take(4).Select(Function(c) System.IO.Path.GetFileNameWithoutExtension(c.AnimationFile)))}")
            Next
            Dim d1 = clips.Where(Function(c) depthOf(c.AnimationFile) = 1).ToList()
            Console.WriteLine($"  depth-1 (directly under top-seg, e.g. Weapon\\X.hkx) ({d1.Count}) by top-seg: " & String.Join(" ; ", d1.GroupBy(Function(c) TopSegOf(FolderRelOf(c.AnimationFile))).OrderByDescending(Function(x) x.Count()).Take(14).Select(Function(x) $"{x.Key}={x.Count()}")))
            For Each grp In d1.GroupBy(Function(c) TopSegOf(FolderRelOf(c.AnimationFile))).Where(Function(x) {"Weapon", "Furniture", "MT"}.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).Take(3)
                Console.WriteLine($"        {grp.Key}\\ direct e.g.: {String.Join(", ", grp.Take(8).Select(Function(c) System.IO.Path.GetFileNameWithoutExtension(c.AnimationFile)))}")
            Next

            ' (a) GROUND TRUTH: histograma de TOP-SEG (actividad macro) + carpetas completas.
            Dim byTop = clips.GroupBy(Function(c) TopSegOf(FolderRelOf(c.AnimationFile))).OrderByDescending(Function(g) g.Count()).ToList()
            Console.WriteLine($"  (a) TOP-SEG (macro activity) — {byTop.Count} distinct:")
            For Each g In byTop : Console.WriteLine($"        {g.Count(),5}  {g.Key}") : Next
            Dim byFolder = clips.GroupBy(Function(c) FolderRelOf(c.AnimationFile)).OrderByDescending(Function(g) g.Count()).ToList()
            Console.WriteLine($"  (a') full FOLDERS — {byFolder.Count} distinct (top 50):")
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
            Dim persp = rb.Subgraphs.GroupBy(Function(s) CInt(s.FlagsPerspective)).OrderBy(Function(g) g.Key).
                          Select(Function(g) $"{If(g.Key = 0, "3rd", If(g.Key = 1, "1st", "none"))}={g.Count()}")
            Console.WriteLine($"  (c) Perspective(subgraphs SRAF): {String.Join(" ; ", persp)}")

            ' (d) STKD (target keywords) distintos sobre los subgraphs.
            Dim stkd = rb.Subgraphs.SelectMany(Function(s) s.TargetKeywords.Select(Function(k) k.Keyword)).Distinct().ToList()
            Console.WriteLine($"  (d) STKD target-keywords distinct: {stkd.Count}" &
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
                Console.WriteLine($"  (e) BlendHint over {loadedOk} files: " &
                                  String.Join(" ; ", hintTally.OrderBy(Function(x) x.Key).Select(Function(x) $"{If(x.Key = 0, "normal", If(x.Key = 2, "additive", "hint" & x.Key))}={x.Value}")))
                If additiveSample.Count > 0 Then
                    Console.WriteLine($"      additives (sample): ")
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
            Console.WriteLine($"  [FINAL] folder='{actorRoot}\Animations\' exist={existing.Count} MAPPED(all sources)={existing.Count - finalOrphans.Count} | NOT-MAPPED={finalOrphans.Count}")
            ' Desglose de los NO-mapeados por STEM (nombre sin números/dirección) → patrón (to_mood / alt / etc.).
            Dim stemOf = Function(p As String) As String
                             Dim b = System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant()
                             b = System.Text.RegularExpressions.Regex.Replace(b, "[0-9]+$", "")
                             Return b
                         End Function
            For Each grp In finalOrphans.GroupBy(Function(o) stemOf(o)).OrderByDescending(Function(g) g.Count()).Take(18)
                Console.WriteLine($"        NO-MAP x{grp.Count(),-3} stem='{grp.Key}'  e.g.: {LastTwoSeg(grp.First())}")
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
            Console.WriteLine($"      [NAME-CHECK] orphans SAME-name (collapsed mood variant)={orphanSameNameAsResolved.Count} (of IDLE.GNAM-base={sameByIdle}) | UNIQUE-name unresolved={orphanUniqueName.Count} (of IDLE.GNAM-base={uniqueByIdle})")
            Dim uniqueNonIdle = orphanUniqueName.Where(Function(o) Not idleGnamBases.Contains(baseOf(o))).ToList()
            Console.WriteLine($"      [NAME-CHECK] UNIQUE-name that NOT EVEN IDLE references ({uniqueNonIdle.Count}) — the true residual:")
            For Each o In uniqueNonIdle.Take(8) : Console.WriteLine($"        residual: {o}") : Next

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
            Console.WriteLine($"      (h) DEEP-SCAN behaviors={nBehFiles} clipGens={nClip} gamebryo={nGamebryo} refBases-distinct={refBases.Count} | of the {uniqueNonIdle.Count} non-IDLE: covered by deep-scan={uniqueByDeep} | FINAL RESIDUAL={deepResidual.Count}")
            ' [94-CHECK] de los NO-mapeados (todas las fuentes), ¿cuántos son nombre de algún clip-gen de Character\Behaviors
            ' (= walk-gap recuperable) vs NINGUNO (= runtime puro: mood-transition/alt elegidos por variable/azar)?
            Dim fByRef = finalOrphans.Where(Function(o) refBases.Contains(baseOf(o))).Count()
            Console.WriteLine($"      [94-CHECK] NOT-mapped={finalOrphans.Count} | basename ∈ Character\Behaviors clip-gen={fByRef} (walk-gap) | NO clip-gen (runtime)={finalOrphans.Count - fByRef}")

            ' (i) EXPANSIÓN DE PATRONES IDLE.GNAM ($(Subgraph) + wildcard *) contra las carpetas SAPT aplicadas =
            ' el mecanismo ESTRUCTURAL real de los gestos PoseA/Turn. ¿Cubre los huérfanos sin heurística de carpeta?
            Dim kwSetI As New HashSet(Of UInteger)(rb.ActorKeywords)
            Dim saptDirListI As New List(Of String)
            For Each sg In rb.Subgraphs
                Dim fidI = sg.ActorKeywords.Select(Function(k) k.Keyword).
                           FirstOrDefault(Function(k) RaceBehaviorResolver.IsRaceIdentityKeyword(k) AndAlso Not kwSetI.Contains(k))
                If fidI <> 0UI Then Continue For
                For Each sp In sg.AnimationPaths
                    If Not String.IsNullOrWhiteSpace(sp.Path) Then saptDirListI.Add(CanonHkx(sp.Path.Replace("/"c, "\"c).TrimEnd("\"c)))
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
            Console.WriteLine($"      (i) IDLE-PATTERN-EXPAND: matched by IDLE patterns={idleExpanded.Count} | orphans covered by IDLE={orphans.Count - orphansAfterIdle.Count} | rest={orphansAfterIdle.Count} (of which same-name→recoverable per-subgraph={recoverablePerSubgraph}) | TRUE RESIDUAL={trueResidual.Count}")
            For Each o In trueResidual.Take(30) : Console.WriteLine($"        TRUE-residual: {o}") : Next
            Dim enumInFolder = enumSet.Where(Function(e) e.StartsWith(animPrefix, StringComparison.OrdinalIgnoreCase)).Count()
            Dim enumOutside = enumSet.Where(Function(e) Not e.StartsWith(animPrefix, StringComparison.OrdinalIgnoreCase)).OrderBy(Function(e) e).ToList()
            Console.WriteLine($"  (f) ORPHANHOOD '{actorRoot}\Animations\': exist={existing.Count} enumSet-total={enumSet.Count} enumSet-in-this-folder={enumInFolder} enumSet-OUTSIDE={enumOutside.Count} | ORPHANS(exist∧¬enum)={orphans.Count}")
            ' Carpetas TOP de los orphans (qué CLASE de animación queda fuera).
            Dim orphanByTop = orphans.GroupBy(Function(o) TopSegOf(FolderRelOf(o))).OrderByDescending(Function(grp) grp.Count()).ToList()
            Console.WriteLine($"      orphans by TOP-SEG: " & String.Join(" ; ", orphanByTop.Take(20).Select(Function(grp) $"{grp.Key}={grp.Count()}")))
            ' Set de carpetas que las rutas SAPT declaran (canon, dir). ¿Los orphans caen en carpetas SAPT (buscadas pero
            ' no referenciadas por ningún clip-generator) o en carpetas que NINGÚN SAPT busca?
            ' MISMO filtro de identidad que EnumerateClips: solo subgraphs APLICADOS (excluye los que piden identidad
            ' de OTRA raza) → para robots de carpeta compartida, su SAPT queda DENTRO de su subcarpeta (no trae Protectron).
            Dim kwSet As New HashSet(Of UInteger)(rb.ActorKeywords)
            Dim saptDirs As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each sg In rb.Subgraphs
                Dim foreignId = sg.ActorKeywords.Select(Function(k) k.Keyword).
                                FirstOrDefault(Function(k) RaceBehaviorResolver.IsRaceIdentityKeyword(k) AndAlso Not kwSet.Contains(k))
                If foreignId <> 0UI Then Continue For
                For Each sp In sg.AnimationPaths
                    If String.IsNullOrWhiteSpace(sp.Path) Then Continue For
                    saptDirs.Add(CanonHkx(sp.Path.Replace("/"c, "\"c).TrimEnd("\"c)))
                Next
            Next
            Dim dirOf = Function(p As String) As String
                            Dim k = p.LastIndexOf("\"c) : Return If(k > 0, p.Substring(0, k), "")
                        End Function
            Dim orphanInSapt = orphans.Where(Function(o) saptDirs.Contains(dirOf(o))).ToList()
            Console.WriteLine($"      SAPT-dirs declared={saptDirs.Count} | orphans IN SAPT-folder (searched, not-referenced)={orphanInSapt.Count} | orphans in NON-SAPT folder={orphans.Count - orphanInSapt.Count}")
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
            Console.WriteLine($"      COVERAGE by SAPT-subtree: orphans recoverable(under SAPT)={orphansUnderSapt.Count} | residual(NOT under SAPT)={residual.Count}")
            Console.WriteLine($"      residual NON-SAPT (what not even the search-path searches) sample:")
            For Each o In residual.Take(20) : Console.WriteLine($"        residual: {o}") : Next
        Next
    End Sub

    ''' <summary>Dump completo de un NIF: árbol de NiNodes (parent, local.T, world.T) + por shape
    ''' skinneada el palette (bone, bind.T, inv(bind).T = world que el skin exige). Para auditar el
    ''' PLACEMENT de chunks (¿el C-X interno del chunk coincide con el socket del rig? ¿inv(bind)
    ''' coincide con el world del rig o está corrido por el local del socket = double-count?).</summary>
    Private Sub NifDumpRun(path As String)
        Dim nbx = LoadAnimCand(path)
        If nbx Is Nothing Then Console.WriteLine($"[nifdump] '{path}' does not load") : Return
        Dim nif As New Nifcontent_Class_Manolo() : nif.Load_Manolo(nbx)
        Console.WriteLine($"[nifdump] {path}")
        Console.WriteLine("  ── NODES ──")
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
        Console.WriteLine("  ── BSConnectPoint::Parents (sockets it PUBLISHES, local to the parent bone) ──")
        For Each cp In BSConnectPointReader.ReadParents(nif)
            Console.WriteLine($"   '{cp.Name}' parentBone='{cp.ParentBoneName}'  T=({cp.Translation.X:F3},{cp.Translation.Y:F3},{cp.Translation.Z:F3})  R=({cp.Rotation.X:F3},{cp.Rotation.Y:F3},{cp.Rotation.Z:F3},{cp.Rotation.W:F3}) scale={cp.Scale:F3}")
        Next
        Dim childNames = BSConnectPointReader.ReadChildrenNames(nif)
        If childNames.Count > 0 Then Console.WriteLine($"  ── BSConnectPoint::Children (sockets it CONSUMES): {String.Join(", ", childNames)}")
        Console.WriteLine("  ── SKIN BINDS (inv(bind) = world the skin requires, in the chunk frame) ──")
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

    ' =====================================================================================
    ' --estimatesclp : estimador APROXIMADO de valores SCLP (bone-scale por hueso *_skin)
    ' =====================================================================================

    ''' <summary>Acumulador de extents de vértices en el ESPACIO LOCAL DE UN HUESO, por eje. Se llena
    ''' pasando cada vértice skinneado a ese hueso (con el bind skin→bone) y ponderando por el peso.
    ''' Rms(a)=√(Σw·local_a² / Σw) y MeanAbs(a)=Σw·|local_a| / Σw dan dos medidas del "tamaño" de la
    ''' malla vista desde el hueso, sobre el eje a (0=X,1=Y,2=Z). El ratio underarmor/body de esas
    ''' medidas es el estimador de escala.</summary>
    Private Class BoneAccum
        Public SumW As Single = 0.0F
        Public SumWsq(2) As Single    ' por eje: Σ w·local²
        Public SumWabs(2) As Single   ' por eje: Σ w·|local|
        Public BindT As System.Numerics.Vector3 = New System.Numerics.Vector3(0, 0, 0)  ' traslación del bind (sanity de frame)
        Public HasBind As Boolean = False

        ' --- Set DOMINADO (vértices cuyo peso DOMINANTE > 0.5 apunta a este hueso), en espacio local ---
        ' Sin blend ni ponderación: geometría limpia. Box=(max-min)/2 por eje; RmsC=desvío alrededor del centroide.
        Public DomCount As Integer = 0
        Public DomMin() As Single = {Single.MaxValue, Single.MaxValue, Single.MaxValue}
        Public DomMax() As Single = {Single.MinValue, Single.MinValue, Single.MinValue}
        Public DomSum(2) As Single     ' por eje: Σ local
        Public DomSumSq(2) As Single   ' por eje: Σ local²

        ''' <summary>RMS ponderado del extent local sobre el eje (0=X,1=Y,2=Z). 0 si no hay peso.</summary>
        Public Function Rms(axis As Integer) As Single
            If SumW <= 0.0F Then Return 0.0F
            Return CSng(Math.Sqrt(SumWsq(axis) / SumW))
        End Function

        ''' <summary>Media ponderada del |extent| local sobre el eje (0=X,1=Y,2=Z). 0 si no hay peso.</summary>
        Public Function MeanAbs(axis As Integer) As Single
            If SumW <= 0.0F Then Return 0.0F
            Return SumWabs(axis) / SumW
        End Function

        ''' <summary>Agrega un vértice DOMINADO por este hueso (posición local) al set limpio (sin blend).</summary>
        Public Sub AddDominated(lp As System.Numerics.Vector3)
            DomCount += 1
            Dim v0 = lp.X, v1 = lp.Y, v2 = lp.Z
            If v0 < DomMin(0) Then DomMin(0) = v0
            If v1 < DomMin(1) Then DomMin(1) = v1
            If v2 < DomMin(2) Then DomMin(2) = v2
            If v0 > DomMax(0) Then DomMax(0) = v0
            If v1 > DomMax(1) Then DomMax(1) = v1
            If v2 > DomMax(2) Then DomMax(2) = v2
            DomSum(0) += v0 : DomSum(1) += v1 : DomSum(2) += v2
            DomSumSq(0) += v0 * v0 : DomSumSq(1) += v1 * v1 : DomSumSq(2) += v2 * v2
        End Sub

        ''' <summary>Semi-rango (bounding box / 2) del set dominado sobre el eje. 0 si vacío.</summary>
        Public Function DomHalfRange(axis As Integer) As Single
            If DomCount <= 0 Then Return 0.0F
            Return (DomMax(axis) - DomMin(axis)) / 2.0F
        End Function

        ''' <summary>RMS del set dominado alrededor de SU centroide sobre el eje (desvío estándar poblacional). 0 si vacío.</summary>
        Public Function DomRmsC(axis As Integer) As Single
            If DomCount <= 0 Then Return 0.0F
            Dim mean = DomSum(axis) / DomCount
            Dim varr = DomSumSq(axis) / DomCount - mean * mean
            If varr < 0.0F Then varr = 0.0F   ' guard numérico
            Return CSng(Math.Sqrt(varr))
        End Function
    End Class

    ''' <summary>Acumulador de error de UNA métrica sobre los ejes que el artista MOVIÓ (authored ≠ 1.0):
    ''' lista de |est−authored| + conteos dentro de ±0.05 y ±0.10. Usado para el RESUMEN por-métrica.</summary>
    Private Class MetricAcc
        Public Errs As New List(Of Single)
        Public In05 As Integer = 0
        Public In10 As Integer = 0
        Public Sub Add(err As Single)
            Errs.Add(err)
            If err <= 0.05F Then In05 += 1
            If err <= 0.1F Then In10 += 1
        End Sub
    End Class

    ''' <summary>Carga un NIF del FilesDictionary y acumula, por nombre de hueso, los extents de sus
    ''' vértices skinneados en el ESPACIO LOCAL DE CADA HUESO (aplicando el bind skin→bone del shape).
    ''' Un vértice contribuye a un hueso solo si su peso para ese hueso ≥ <paramref name="wThreshold"/>.
    ''' Devuelve Nothing (y avisa por consola) si el NIF no existe o no carga.</summary>
    Private Function AccumulateBoneExtents(nifKey As String, wThreshold As Single, Optional shapeNameFilter As String = "") As Dictionary(Of String, BoneAccum)
        Dim bytes As Byte() = Nothing
        Try
            bytes = GetNifOrFileBytes(nifKey)
        Catch ex As Exception
            Console.WriteLine($"[estimatesclp] error reading '{nifKey}': {ex.Message}")
            Return Nothing
        End Try
        If bytes Is Nothing OrElse bytes.Length = 0 Then
            Console.WriteLine($"[estimatesclp] '{nifKey}' does not exist / empty in the FilesDictionary")
            Return Nothing
        End If
        Dim nif As New Nifcontent_Class_Manolo()
        Try
            nif.Load_Manolo(bytes)
        Catch ex As Exception
            Console.WriteLine($"[estimatesclp] '{nifKey}' does not load as NIF: {ex.Message}")
            Return Nothing
        End Try

        Dim acc As New Dictionary(Of String, BoneAccum)(StringComparer.OrdinalIgnoreCase)
        Dim shapesSkinned = 0
        Dim shapesKept = 0   ' shapes que pasan el filtro por nombre (solo relevante si shapeNameFilter <> "")
        For blkIdx = 0 To nif.Blocks.Count - 1
            Dim shp = TryCast(nif.Blocks(blkIdx), NiflySharp.INiShape)
            If shp Is Nothing Then Continue For
            Dim rs As NifRenderableShape
            Try
                rs = New NifRenderableShape(nif, shp, blkIdx)
            Catch
                Continue For
            End Try
            If shapeNameFilter <> "" Then
                If rs.ShapeName Is Nothing OrElse rs.ShapeName.IndexOf(shapeNameFilter, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
                shapesKept += 1
            End If
            If rs.ShapeBones Is Nothing OrElse rs.ShapeBones.Count = 0 Then Continue For  ' shape sin skin
            Dim verts As List(Of System.Numerics.Vector3) = Nothing
            Try
                verts = rs.Geometry?.GetVertexPositions()
            Catch
            End Try
            If verts Is Nothing OrElse verts.Count = 0 Then Continue For
            Dim skin As ShapeSkinningData
            Try
                skin = rs.Geometry.GetSkinning()
            Catch
                Continue For
            End Try
            Dim wpv = skin.WeightsPerVertex
            If wpv <= 0 OrElse skin.BoneIndices Is Nothing OrElse skin.BoneWeights Is Nothing Then Continue For
            Dim bones = rs.ShapeBones
            Dim binds = rs.ShapeBoneTransforms
            If binds Is Nothing Then Continue For
            shapesSkinned += 1
            Dim nVerts = Math.Min(verts.Count, skin.VertexCount)
            For i = 0 To nVerts - 1
                Dim vp = verts(i)

                ' --- Slot DOMINANTE del vértice: el de mayor peso entre sus slots (para el set dominado limpio) ---
                Dim domSlot = -1
                Dim domW As Single = 0.0F
                For j = 0 To wpv - 1
                    Dim idx = i * wpv + j
                    If idx >= skin.BoneWeights.Length Then Continue For
                    Dim wj As Single = CType(skin.BoneWeights(idx), Single)
                    If wj > domW Then domW = wj : domSlot = j
                Next

                ' --- Acumulación BLENDED (ponderada por peso) — sin cambios ---
                For j = 0 To wpv - 1
                    Dim idx = i * wpv + j
                    If idx >= skin.BoneWeights.Length OrElse idx >= skin.BoneIndices.Length Then Continue For
                    Dim w As Single = CType(skin.BoneWeights(idx), Single)
                    If w < wThreshold Then Continue For
                    Dim bi = CInt(skin.BoneIndices(idx))
                    If bi < 0 OrElse bi >= bones.Count OrElse bi >= binds.Count Then Continue For
                    Dim bn = TryCast(bones(bi), NiflySharp.Blocks.NiNode)?.Name?.String
                    If String.IsNullOrEmpty(bn) Then Continue For
                    Dim bind = binds(bi)
                    If bind Is Nothing Then Continue For
                    ' Bind skin→bone aplicado al vértice: componer con una traslación-pura por el punto
                    ' (Rotation=identidad, Scale=1 por default) ⇒ .Translation = bind aplicado al punto.
                    Dim pT As New Transform_Class With {.Translation = vp}
                    Dim lp = bind.ComposeTransforms(pT).Translation
                    Dim ba As BoneAccum = Nothing
                    If Not acc.TryGetValue(bn, ba) Then
                        ba = New BoneAccum()
                        ba.BindT = bind.Translation
                        ba.HasBind = True
                        acc(bn) = ba
                    End If
                    ba.SumW += w
                    ba.SumWsq(0) += w * lp.X * lp.X
                    ba.SumWsq(1) += w * lp.Y * lp.Y
                    ba.SumWsq(2) += w * lp.Z * lp.Z
                    ba.SumWabs(0) += w * Math.Abs(lp.X)
                    ba.SumWabs(1) += w * Math.Abs(lp.Y)
                    ba.SumWabs(2) += w * Math.Abs(lp.Z)
                Next

                ' --- Acumulación del SET DOMINADO (una sola vez por vértice, al hueso dominante si domW > 0.5) ---
                If domSlot >= 0 AndAlso domW > 0.5F Then
                    Dim idx = i * wpv + domSlot
                    If idx < skin.BoneIndices.Length Then
                        Dim bi = CInt(skin.BoneIndices(idx))
                        If bi >= 0 AndAlso bi < bones.Count AndAlso bi < binds.Count Then
                            Dim bn = TryCast(bones(bi), NiflySharp.Blocks.NiNode)?.Name?.String
                            Dim bind = binds(bi)
                            If Not String.IsNullOrEmpty(bn) AndAlso bind IsNot Nothing Then
                                Dim pT As New Transform_Class With {.Translation = vp}
                                Dim lp = bind.ComposeTransforms(pT).Translation
                                Dim ba As BoneAccum = Nothing
                                If Not acc.TryGetValue(bn, ba) Then
                                    ba = New BoneAccum()
                                    ba.BindT = bind.Translation
                                    ba.HasBind = True
                                    acc(bn) = ba
                                End If
                                ba.AddDominated(lp)
                            End If
                        End If
                    End If
                End If
            Next
        Next
        Dim filterNote = If(shapeNameFilter <> "", $" [filter '{shapeNameFilter}': {shapesKept} shape(s) after filter]", "")
        Console.WriteLine($"[estimatesclp]   '{nifKey}': {shapesSkinned} skinned shape(s), {acc.Count} bone(s) with weight{filterNote}")
        Return acc
    End Function

    ''' <summary>Acumulador de la regresión least-squares POR-ORIGEN de la métrica <c>estNN</c>, por eje.
    ''' Para cada par (vértice underarmor <c>u</c> ↔ su body-match <c>p</c>) en el frame local del hueso:
    ''' <c>Sxx(a) += w·lp.a²</c> y <c>Sxy(a) += w·lu.a·lp.a</c>. La escala estimada es <c>Sxy/Sxx</c>
    ''' (pendiente por el origen del hueso).</summary>
    Private Class NNAccum
        Public Sxx(2) As Double    ' por eje: Σ w·lp²  (denominador)
        Public Sxy(2) As Double    ' por eje: Σ w·lu·lp (numerador)
        Public Syy(2) As Double    ' por eje: Σ w·lu²  (para el residual de reconstrucción)
        Public NPairs As Integer = 0
    End Class

    ''' <summary>Carga un NIF del FilesDictionary y devuelve, por NOMBRE de hueso (uniendo shapes), la lista de
    ''' vértices skinneados a ese hueso con peso &gt; <paramref name="wEps"/>: <c>model</c> = posición cruda del
    ''' vértice (espacio modelo, sin transformar), <c>local</c> = esa posición llevada al frame local del hueso
    ''' con el bind skin→bone del shape, y <c>w</c> = peso continuo del vértice a ese hueso. Devuelve Nothing (y
    ''' avisa por consola) si el NIF no existe o no carga. Base de datos para el nearest-neighbor de estNN.</summary>
    Private Function CollectBoneVertexData(nifKey As String, wEps As Single, Optional shapeNameFilter As String = "") As Dictionary(Of String, List(Of (model As System.Numerics.Vector3, local As System.Numerics.Vector3, w As Single)))
        Dim bytes As Byte() = Nothing
        Try
            bytes = GetNifOrFileBytes(nifKey)
        Catch ex As Exception
            Console.WriteLine($"[estimatesclp] error reading '{nifKey}': {ex.Message}")
            Return Nothing
        End Try
        If bytes Is Nothing OrElse bytes.Length = 0 Then
            Console.WriteLine($"[estimatesclp] '{nifKey}' does not exist / empty in the FilesDictionary")
            Return Nothing
        End If
        Dim nif As New Nifcontent_Class_Manolo()
        Try
            nif.Load_Manolo(bytes)
        Catch ex As Exception
            Console.WriteLine($"[estimatesclp] '{nifKey}' does not load as NIF: {ex.Message}")
            Return Nothing
        End Try

        Dim acc As New Dictionary(Of String, List(Of (model As System.Numerics.Vector3, local As System.Numerics.Vector3, w As Single)))(StringComparer.OrdinalIgnoreCase)
        For blkIdx = 0 To nif.Blocks.Count - 1
            Dim shp = TryCast(nif.Blocks(blkIdx), NiflySharp.INiShape)
            If shp Is Nothing Then Continue For
            Dim rs As NifRenderableShape
            Try
                rs = New NifRenderableShape(nif, shp, blkIdx)
            Catch
                Continue For
            End Try
            If shapeNameFilter <> "" AndAlso (rs.ShapeName Is Nothing OrElse rs.ShapeName.IndexOf(shapeNameFilter, StringComparison.OrdinalIgnoreCase) < 0) Then Continue For
            If rs.ShapeBones Is Nothing OrElse rs.ShapeBones.Count = 0 Then Continue For
            Dim verts As List(Of System.Numerics.Vector3) = Nothing
            Try
                verts = rs.Geometry?.GetVertexPositions()
            Catch
            End Try
            If verts Is Nothing OrElse verts.Count = 0 Then Continue For
            Dim skin As ShapeSkinningData
            Try
                skin = rs.Geometry.GetSkinning()
            Catch
                Continue For
            End Try
            Dim wpv = skin.WeightsPerVertex
            If wpv <= 0 OrElse skin.BoneIndices Is Nothing OrElse skin.BoneWeights Is Nothing Then Continue For
            Dim bones = rs.ShapeBones
            Dim binds = rs.ShapeBoneTransforms
            If binds Is Nothing Then Continue For
            Dim nVerts = Math.Min(verts.Count, skin.VertexCount)
            For i = 0 To nVerts - 1
                Dim vp = verts(i)
                For j = 0 To wpv - 1
                    Dim idx = i * wpv + j
                    If idx >= skin.BoneWeights.Length OrElse idx >= skin.BoneIndices.Length Then Continue For
                    Dim w As Single = CType(skin.BoneWeights(idx), Single)
                    If w <= wEps Then Continue For   ' excluye pesos numéricamente nulos (no es heurística de dominancia)
                    Dim bi = CInt(skin.BoneIndices(idx))
                    If bi < 0 OrElse bi >= bones.Count OrElse bi >= binds.Count Then Continue For
                    Dim bn = TryCast(bones(bi), NiflySharp.Blocks.NiNode)?.Name?.String
                    If String.IsNullOrEmpty(bn) Then Continue For
                    Dim bind = binds(bi)
                    If bind Is Nothing Then Continue For
                    Dim pT As New Transform_Class With {.Translation = vp}
                    Dim lp = bind.ComposeTransforms(pT).Translation
                    Dim lst As List(Of (model As System.Numerics.Vector3, local As System.Numerics.Vector3, w As Single)) = Nothing
                    If Not acc.TryGetValue(bn, lst) Then
                        lst = New List(Of (model As System.Numerics.Vector3, local As System.Numerics.Vector3, w As Single))()
                        acc(bn) = lst
                    End If
                    lst.Add((vp, lp, w))
                Next
            Next
        Next
        Return acc
    End Function

    ''' <summary>Construcción COMPARTIDA de los pares nearest-neighbor por hueso (base de <c>estNN</c> y
    ''' <c>estFull</c>). Para cada vértice del underarmor influido por el hueso b (peso &gt; <paramref name="wEps"/>),
    ''' matchea el vértice del BODY más cercano en ESPACIO MODELO entre los candidatos por NOMBRE de hueso, y devuelve
    ''' por hueso la lista de pares en frame LOCAL: <c>lu</c> = vértice del underarmor en local, <c>lp</c> = su match
    ''' del body en local, <c>w</c> = peso continuo del vértice al hueso. Solo aparecen huesos con ≥1 par (presentes
    ''' en AMBOS NIFs). NIF que no carga → Nothing.</summary>
    Private Function BuildNNPairs(uaKey As String, bodyKey As String, wEps As Single, Optional uaShapeFilter As String = "") As Dictionary(Of String, List(Of (lu As System.Numerics.Vector3, lp As System.Numerics.Vector3, w As Double)))
        Dim bodyData = CollectBoneVertexData(bodyKey, wEps)                 ' body de referencia: SIN filtro (completo)
        If bodyData Is Nothing Then Return Nothing
        Dim uaData = CollectBoneVertexData(uaKey, wEps, uaShapeFilter)       ' underarmor: filtro por nombre de shape (si activo)
        If uaData Is Nothing Then Return Nothing

        Dim pairs As New Dictionary(Of String, List(Of (lu As System.Numerics.Vector3, lp As System.Numerics.Vector3, w As Double)))(StringComparer.OrdinalIgnoreCase)
        For Each kv In uaData
            Dim bn = kv.Key
            Dim uaList = kv.Value
            Dim cand As List(Of (model As System.Numerics.Vector3, local As System.Numerics.Vector3, w As Single)) = Nothing
            If Not bodyData.TryGetValue(bn, cand) OrElse cand Is Nothing OrElse cand.Count = 0 Then Continue For   ' hueso solo en un NIF → skip
            Dim lst As List(Of (lu As System.Numerics.Vector3, lp As System.Numerics.Vector3, w As Double)) = Nothing
            For Each u In uaList
                ' Nearest body candidate por distancia² en espacio MODELO (naive O(n·m), cientos de verts por hueso).
                Dim best = -1
                Dim bestD As Single = Single.MaxValue
                For ci = 0 To cand.Count - 1
                    Dim d = System.Numerics.Vector3.DistanceSquared(u.model, cand(ci).model)
                    If d < bestD Then bestD = d : best = ci
                Next
                If best < 0 Then Continue For
                If lst Is Nothing Then
                    lst = New List(Of (lu As System.Numerics.Vector3, lp As System.Numerics.Vector3, w As Double))()
                    pairs(bn) = lst
                End If
                lst.Add((u.local, cand(best).local, CDbl(u.w)))   ' (lu, lp, w)
            Next
        Next
        Return pairs
    End Function

    ''' <summary>Métrica <c>estNN</c>: escala diagonal por hueso y eje por least-squares POR-ORIGEN sobre los pares
    ''' NN de <see cref="BuildNNPairs"/>. Por eje: <c>Sxx += w·lp²</c>, <c>Sxy += w·lu·lp</c>, escala = <c>Sxy/Sxx</c>.
    ''' Guard: <c>Sxx &lt; 1e-6</c> → n/a para ese eje. <paramref name="pairs"/> Nothing → Nothing.</summary>
    Private Function AccumulateNNScales(pairs As Dictionary(Of String, List(Of (lu As System.Numerics.Vector3, lp As System.Numerics.Vector3, w As Double)))) As Dictionary(Of String, (sx As Double, sy As Double, sz As Double, nPairs As Integer, okX As Boolean, okY As Boolean, okZ As Boolean, residX As Double, residY As Double, residZ As Double))
        If pairs Is Nothing Then Return Nothing

        ' Acumulá la regresión diagonal por hueso a partir de los pares NN ya construidos.
        Dim acc As New Dictionary(Of String, NNAccum)(StringComparer.OrdinalIgnoreCase)
        For Each kv In pairs
            Dim na As New NNAccum()
            For Each pr In kv.Value
                Dim lu = pr.lu
                Dim lp = pr.lp
                Dim w As Double = pr.w
                na.Sxx(0) += w * CDbl(lp.X) * CDbl(lp.X)
                na.Sxx(1) += w * CDbl(lp.Y) * CDbl(lp.Y)
                na.Sxx(2) += w * CDbl(lp.Z) * CDbl(lp.Z)
                na.Sxy(0) += w * CDbl(lu.X) * CDbl(lp.X)
                na.Sxy(1) += w * CDbl(lu.Y) * CDbl(lp.Y)
                na.Sxy(2) += w * CDbl(lu.Z) * CDbl(lp.Z)
                na.Syy(0) += w * CDbl(lu.X) * CDbl(lu.X)
                na.Syy(1) += w * CDbl(lu.Y) * CDbl(lu.Y)
                na.Syy(2) += w * CDbl(lu.Z) * CDbl(lu.Z)
                na.NPairs += 1
            Next
            acc(kv.Key) = na
        Next

        Const SXX_EPS As Double = 0.000001
        Dim res As New Dictionary(Of String, (sx As Double, sy As Double, sz As Double, nPairs As Integer, okX As Boolean, okY As Boolean, okZ As Boolean, residX As Double, residY As Double, residZ As Double))(StringComparer.OrdinalIgnoreCase)
        For Each kv In acc
            Dim a = kv.Value
            Dim okX = a.Sxx(0) >= SXX_EPS
            Dim okY = a.Sxx(1) >= SXX_EPS
            Dim okZ = a.Sxx(2) >= SXX_EPS
            Dim sx = If(okX, a.Sxy(0) / a.Sxx(0), Double.NaN)
            Dim sy = If(okY, a.Sxy(1) / a.Sxx(1), Double.NaN)
            Dim sz = If(okZ, a.Sxy(2) / a.Sxx(2), Double.NaN)
            ' Residual normalizado por eje: fracción de la varianza-por-origen de lu NO explicada por s·lp.
            ' resid = sqrt( max(0, Syy − s·Sxy) / max(1e-9, Syy) ). 0 = escala-por-hueso perfecta. NaN si el eje no cuenta.
            Dim residX = If(okX, Math.Sqrt(Math.Max(0.0, a.Syy(0) - sx * a.Sxy(0)) / Math.Max(0.000000001, a.Syy(0))), Double.NaN)
            Dim residY = If(okY, Math.Sqrt(Math.Max(0.0, a.Syy(1) - sy * a.Sxy(1)) / Math.Max(0.000000001, a.Syy(1))), Double.NaN)
            Dim residZ = If(okZ, Math.Sqrt(Math.Max(0.0, a.Syy(2) - sz * a.Sxy(2)) / Math.Max(0.000000001, a.Syy(2))), Double.NaN)
            res(kv.Key) = (sx, sy, sz, a.NPairs, okX, okY, okZ, residX, residY, residZ)
        Next
        Return res
    End Function

    ''' <summary>Inversa de una matriz 3×3 por cofactores/adjugada. Devuelve <c>Nothing</c> si
    ''' <c>|det| &lt; 1e-12</c> (matriz singular). Índices [fila,columna] 0..2.</summary>
    Private Function Inverse3x3(m(,) As Double) As Double(,)
        Dim a = m(0, 0), b = m(0, 1), c = m(0, 2)
        Dim d = m(1, 0), e = m(1, 1), f = m(1, 2)
        Dim g = m(2, 0), h = m(2, 1), i = m(2, 2)
        Dim cof00 = e * i - f * h
        Dim cof01 = -(d * i - f * g)
        Dim cof02 = d * h - e * g
        Dim det = a * cof00 + b * cof01 + c * cof02
        If Math.Abs(det) < 0.000000000001 Then Return Nothing   ' 1e-12: guard singular → L no disponible
        Dim invDet = 1.0 / det
        Dim inv(2, 2) As Double
        ' inv = adjugada / det (adjugada = transpuesta de la matriz de cofactores)
        inv(0, 0) = cof00 * invDet
        inv(0, 1) = -(b * i - c * h) * invDet
        inv(0, 2) = (b * f - c * e) * invDet
        inv(1, 0) = cof01 * invDet
        inv(1, 1) = (a * i - c * g) * invDet
        inv(1, 2) = -(a * f - c * d) * invDet
        inv(2, 0) = cof02 * invDet
        inv(2, 1) = -(a * h - b * g) * invDet
        inv(2, 2) = (a * e - b * d) * invDet
        Return inv
    End Function

    ''' <summary>Métrica <c>estFull</c>: ajuste de la MATRIZ LINEAL 3×3 completa <c>L</c> que mejor mapea
    ''' <c>lp → lu</c> por el origen (least-squares ponderado por <c>w</c>), sobre los MISMOS pares NN que
    ''' <c>estNN</c> (<see cref="BuildNNPairs"/>). Acumula por hueso en frame LOCAL:
    ''' <c>P = Σ w·(lp ⊗ lp)</c>, <c>M = Σ w·(lu ⊗ lp)</c> (M[i][j] = Σ w·lu[i]·lp[j]), <c>Syy = Σ w·|lu|²</c>.
    ''' Resuelve <c>L = M · inv(P)</c>. Devuelve por hueso:
    ''' <list type="bullet">
    ''' <item><c>Lxx/Lyy/Lzz</c> = diag(L) = escala per-eje FRAME-AWARE (candidato a .sclp).</item>
    ''' <item><c>offNorm</c> = ‖off-diagonal‖ / ‖L‖ (0 = escala de eje pura; alto = rotación/shear no representable).</item>
    ''' <item><c>residFull</c> = sqrt(max(0, Σw|lu|² − Σ_ij L[i][j]·M[i][j]) / max(1e-9, Σw|lu|²)) — residual del ajuste
    ''' lineal completo; debería ser ≤ el residual diagonal de estNN.</item>
    ''' </list>
    ''' <c>ok = False</c> (todo NaN) si <c>P</c> es singular. <paramref name="pairs"/> Nothing → Nothing.</summary>
    Private Function AccumulateFullFit(pairs As Dictionary(Of String, List(Of (lu As System.Numerics.Vector3, lp As System.Numerics.Vector3, w As Double)))) As Dictionary(Of String, (Lxx As Double, Lyy As Double, Lzz As Double, offNorm As Double, residFull As Double, ok As Boolean))
        If pairs Is Nothing Then Return Nothing
        Dim res As New Dictionary(Of String, (Lxx As Double, Lyy As Double, Lzz As Double, offNorm As Double, residFull As Double, ok As Boolean))(StringComparer.OrdinalIgnoreCase)
        For Each kv In pairs
            Dim P(2, 2) As Double   ' Σ w·(lp ⊗ lp)  (simétrica)
            Dim M(2, 2) As Double   ' Σ w·(lu ⊗ lp)  (M[i][j] = Σ w·lu[i]·lp[j])
            Dim Syy As Double = 0.0 ' Σ w·|lu|²
            For Each pr In kv.Value
                Dim w As Double = pr.w
                Dim px = CDbl(pr.lp.X), py = CDbl(pr.lp.Y), pz = CDbl(pr.lp.Z)
                Dim ux = CDbl(pr.lu.X), uy = CDbl(pr.lu.Y), uz = CDbl(pr.lu.Z)
                P(0, 0) += w * px * px : P(0, 1) += w * px * py : P(0, 2) += w * px * pz
                P(1, 0) += w * py * px : P(1, 1) += w * py * py : P(1, 2) += w * py * pz
                P(2, 0) += w * pz * px : P(2, 1) += w * pz * py : P(2, 2) += w * pz * pz
                M(0, 0) += w * ux * px : M(0, 1) += w * ux * py : M(0, 2) += w * ux * pz
                M(1, 0) += w * uy * px : M(1, 1) += w * uy * py : M(1, 2) += w * uy * pz
                M(2, 0) += w * uz * px : M(2, 1) += w * uz * py : M(2, 2) += w * uz * pz
                Syy += w * (ux * ux + uy * uy + uz * uz)
            Next

            Dim Pinv = Inverse3x3(P)
            If Pinv Is Nothing Then
                res(kv.Key) = (Double.NaN, Double.NaN, Double.NaN, Double.NaN, Double.NaN, False)
                Continue For
            End If

            ' L = M · inv(P)  (3×3).
            Dim L(2, 2) As Double
            For ii = 0 To 2
                For jj = 0 To 2
                    Dim s As Double = 0.0
                    For kk = 0 To 2
                        s += M(ii, kk) * Pinv(kk, jj)
                    Next
                    L(ii, jj) = s
                Next
            Next

            ' offNorm = ‖off-diagonal‖ / ‖L‖ ; residual = sqrt(max(0, Syy − Σ L·M) / max(1e-9, Syy)).
            Dim sumOff As Double = 0.0, sumAll As Double = 0.0, dotLM As Double = 0.0
            For ii = 0 To 2
                For jj = 0 To 2
                    Dim v = L(ii, jj)
                    sumAll += v * v
                    If ii <> jj Then sumOff += v * v
                    dotLM += v * M(ii, jj)
                Next
            Next
            Dim offNorm = If(sumAll > 0.0, Math.Sqrt(sumOff) / Math.Sqrt(sumAll), 0.0)
            Dim residFull = Math.Sqrt(Math.Max(0.0, Syy - dotLM) / Math.Max(0.000000001, Syy))
            res(kv.Key) = (L(0, 0), L(1, 1), L(2, 2), offNorm, residFull, True)
        Next
        Return res
    End Function

    ' =====================================================================================
    ' estGlobal : SOLVE GLOBAL CONJUNTO por mínimos cuadrados de TODAS las escalas de hueso a la vez.
    ' =====================================================================================

    ''' <summary>Aplica un <see cref="Transform_Class"/> a un PUNTO: componer con una traslación-pura por el
    ''' punto (Rotation=identidad, Scale=1) ⇒ <c>.Translation</c> = el transform aplicado al punto. Mismo patrón
    ''' que usa el resto del archivo (<c>bind.ComposeTransforms(New Transform_Class With {.Translation=pt}).Translation</c>);
    ''' evita tocar matrices crudas y mantiene la convención de columnas/inverse consistente.</summary>
    Private Function ApplyT(tr As Transform_Class, pt As System.Numerics.Vector3) As System.Numerics.Vector3
        Return tr.ComposeTransforms(New Transform_Class With {.Translation = pt}).Translation
    End Function

    ''' <summary>Resuelve el sistema lineal <c>A·x = b</c> (A cuadrada N×N, Double) por ELIMINACIÓN GAUSSIANA
    ''' con PIVOTEO PARCIAL (Gauss-Jordan: elimina hacia arriba y abajo). Copia la matriz aumentada, no muta
    ''' A/b. Si un pivote queda &lt; 1e-15 (columna singular pese a la regularización) esa incógnita cae al
    ''' fallback identidad (1.0). Devuelve x (longitud N).</summary>
    Private Function SolveLinearSystem(A(,) As Double, b() As Double) As Double()
        Dim n = b.Length
        Dim M(n - 1, n) As Double   ' aumentada [A | b]
        For i = 0 To n - 1
            For j = 0 To n - 1
                M(i, j) = A(i, j)
            Next
            M(i, n) = b(i)
        Next
        For col = 0 To n - 1
            ' Pivoteo parcial: fila con |M(r,col)| máximo de col..n-1.
            Dim piv = col
            Dim mx = Math.Abs(M(col, col))
            For r = col + 1 To n - 1
                Dim av = Math.Abs(M(r, col))
                If av > mx Then mx = av : piv = r
            Next
            If piv <> col Then
                For j = 0 To n
                    Dim tmp = M(col, j) : M(col, j) = M(piv, j) : M(piv, j) = tmp
                Next
            End If
            Dim d = M(col, col)
            If Math.Abs(d) < 0.000000000000001 Then Continue For   ' 1e-15: pivote singular → se resuelve por fallback
            For r = 0 To n - 1
                If r = col Then Continue For
                Dim f = M(r, col) / d
                If f = 0.0 Then Continue For
                For j = col To n
                    M(r, j) -= f * M(col, j)
                Next
            Next
        Next
        Dim x(n - 1) As Double
        For i = 0 To n - 1
            Dim d = M(i, i)
            x(i) = If(Math.Abs(d) < 0.000000000000001, 1.0, M(i, n) / d)   ' singular → identidad
        Next
        Return x
    End Function

    ''' <summary>Carga un NIF y devuelve, por VÉRTICE, su posición MODELO cruda y su lista de influencias de
    ''' skinning <c>(boneName, w)</c> con peso &gt; <paramref name="wEps"/>. Además rellena <paramref name="binds"/>
    ''' = por NOMBRE de hueso, el bind skin→bone (<c>ShapeBoneTransforms</c>; primero gana, uniendo shapes).
    ''' Devuelve Nothing (y avisa) si el NIF no existe/ no carga. Base del solve conjunto <c>estGlobal</c>.</summary>
    Private Function CollectVertsWithInfluences(nifKey As String, wEps As Single,
                                                ByRef binds As Dictionary(Of String, Transform_Class),
                                                Optional shapeNameFilter As String = "") As List(Of (model As System.Numerics.Vector3, infl As List(Of (bone As String, w As Double))))
        binds = New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        Dim bytes As Byte() = Nothing
        Try
            bytes = GetNifOrFileBytes(nifKey)
        Catch ex As Exception
            Console.WriteLine($"[estimatesclp] error reading '{nifKey}': {ex.Message}")
            Return Nothing
        End Try
        If bytes Is Nothing OrElse bytes.Length = 0 Then
            Console.WriteLine($"[estimatesclp] '{nifKey}' does not exist / empty in the FilesDictionary")
            Return Nothing
        End If
        Dim nif As New Nifcontent_Class_Manolo()
        Try
            nif.Load_Manolo(bytes)
        Catch ex As Exception
            Console.WriteLine($"[estimatesclp] '{nifKey}' does not load as NIF: {ex.Message}")
            Return Nothing
        End Try

        Dim result As New List(Of (model As System.Numerics.Vector3, infl As List(Of (bone As String, w As Double))))()
        For blkIdx = 0 To nif.Blocks.Count - 1
            Dim shp = TryCast(nif.Blocks(blkIdx), NiflySharp.INiShape)
            If shp Is Nothing Then Continue For
            Dim rs As NifRenderableShape
            Try
                rs = New NifRenderableShape(nif, shp, blkIdx)
            Catch
                Continue For
            End Try
            If shapeNameFilter <> "" AndAlso (rs.ShapeName Is Nothing OrElse rs.ShapeName.IndexOf(shapeNameFilter, StringComparison.OrdinalIgnoreCase) < 0) Then Continue For
            If rs.ShapeBones Is Nothing OrElse rs.ShapeBones.Count = 0 Then Continue For
            Dim verts As List(Of System.Numerics.Vector3) = Nothing
            Try
                verts = rs.Geometry?.GetVertexPositions()
            Catch
            End Try
            If verts Is Nothing OrElse verts.Count = 0 Then Continue For
            Dim skin As ShapeSkinningData
            Try
                skin = rs.Geometry.GetSkinning()
            Catch
                Continue For
            End Try
            Dim wpv = skin.WeightsPerVertex
            If wpv <= 0 OrElse skin.BoneIndices Is Nothing OrElse skin.BoneWeights Is Nothing Then Continue For
            Dim bones = rs.ShapeBones
            Dim bindsArr = rs.ShapeBoneTransforms
            If bindsArr Is Nothing Then Continue For
            ' Registrá el bind skin→bone por NOMBRE de hueso (primero gana; consistente con el resto del archivo).
            For k = 0 To Math.Min(bones.Count, bindsArr.Count) - 1
                Dim bnNode = TryCast(bones(k), NiflySharp.Blocks.NiNode)
                Dim nm = If(bnNode?.Name?.String, "")
                If nm = "" OrElse bindsArr(k) Is Nothing Then Continue For
                If Not binds.ContainsKey(nm) Then binds(nm) = bindsArr(k)
            Next
            Dim nVerts = Math.Min(verts.Count, skin.VertexCount)
            For i = 0 To nVerts - 1
                Dim vp = verts(i)
                Dim infl As New List(Of (bone As String, w As Double))()
                For j = 0 To wpv - 1
                    Dim idx = i * wpv + j
                    If idx >= skin.BoneWeights.Length OrElse idx >= skin.BoneIndices.Length Then Continue For
                    Dim w As Single = CType(skin.BoneWeights(idx), Single)
                    If w <= wEps Then Continue For
                    Dim bi = CInt(skin.BoneIndices(idx))
                    If bi < 0 OrElse bi >= bones.Count Then Continue For
                    Dim bnNode = TryCast(bones(bi), NiflySharp.Blocks.NiNode)
                    Dim bn = If(bnNode?.Name?.String, "")
                    If bn = "" Then Continue For
                    infl.Add((bn, CDbl(w)))
                Next
                If infl.Count > 0 Then result.Add((vp, infl))
            Next
        Next
        Return result
    End Function

    ''' <summary>Métrica <c>estGlobal</c>: SOLVE GLOBAL CONJUNTO por mínimos cuadrados de TODAS las escalas de
    ''' hueso a la vez. Modelo: la malla del underarmor = body deformado por skinning con los <c>_skin</c> escalados
    ''' por <c>S_b=diag(sx,sy,sz)</c> en el frame local del hueso (alrededor del origen):
    ''' <c>u = Σ_b w_b · Bind_b · S_b · Bind_b⁻¹ · p</c>, LINEAL en las incógnitas. Para cada vértice <c>u</c> del
    ''' underarmor su rest <c>p</c> = vértice del BODY más cercano en espacio MODELO (nearest-neighbor GLOBAL, un
    ''' solo <c>p</c> por <c>u</c> sobre TODOS los verts del body). Se ensamblan las ecuaciones normales
    ''' <c>ATA·x = ATy</c> (con regularización λ hacia identidad) y se resuelve por eliminación gaussiana con
    ''' pivoteo parcial. Devuelve por NOMBRE de hueso <c>(sx,sy,sz)</c>. Imprime un SELF-CHECK del residual
    ''' <c>‖A·x−y‖</c> del solve vs el de x=identidad. NIF que no carga → Nothing.</summary>
    Private Function EstimateGlobalScales(uaKey As String, bodyKey As String, wEps As Single, Optional uaShapeFilter As String = "") As Dictionary(Of String, (sx As Double, sy As Double, sz As Double))
        ' Regularización relativa hacia identidad: λ = LAMBDA · (traza(ATA)/N). Subilo para más pull a s=1.0.
        Const LAMBDA As Double = 0.01
        Dim inv = System.Globalization.CultureInfo.InvariantCulture

        Dim uaBinds As Dictionary(Of String, Transform_Class) = Nothing
        Dim bodyBinds As Dictionary(Of String, Transform_Class) = Nothing
        Dim uaVerts = CollectVertsWithInfluences(uaKey, wEps, uaBinds, uaShapeFilter)   ' underarmor: filtro por nombre de shape (si activo)
        If uaVerts Is Nothing Then Return Nothing
        Dim bodyVerts = CollectVertsWithInfluences(bodyKey, wEps, bodyBinds)             ' body de referencia: SIN filtro (completo)
        If bodyVerts Is Nothing Then Return Nothing
        If uaVerts.Count = 0 OrElse bodyVerts.Count = 0 Then
            Console.WriteLine("[estimatesclp] estGlobal: NIF without skinned vertices — abort")
            Return Nothing
        End If

        ' Binds por NOMBRE: underarmor primero (define el frame de cada hueso); huesos solo-en-body toman el del body.
        Dim binds As New Dictionary(Of String, Transform_Class)(uaBinds, StringComparer.OrdinalIgnoreCase)
        For Each kv In bodyBinds
            If Not binds.ContainsKey(kv.Key) Then binds(kv.Key) = kv.Value
        Next

        ' Posiciones MODELO de TODOS los verts del body (base del nearest-neighbor GLOBAL).
        Dim bodyPos(bodyVerts.Count - 1) As System.Numerics.Vector3
        For i = 0 To bodyVerts.Count - 1
            bodyPos(i) = bodyVerts(i).model
        Next

        ' Incógnitas: por cada NOMBRE de hueso que influye algún vértice UA y tiene bind, 3 índices (sx,sy,sz).
        Dim boneIdx As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
        Dim boneList As New List(Of String)()
        For Each vx In uaVerts
            For Each infl In vx.infl
                If binds.ContainsKey(infl.bone) AndAlso Not boneIdx.ContainsKey(infl.bone) Then
                    boneIdx(infl.bone) = boneList.Count
                    boneList.Add(infl.bone)
                End If
            Next
        Next
        Dim nBones = boneList.Count
        If nBones = 0 Then
            Console.WriteLine("[estimatesclp] estGlobal: 0 bones with bind — abort")
            Return Nothing
        End If
        Dim N = 3 * nBones

        ' Precomputá por hueso (UNA vez): SkinToBone, t_b y las columnas de la parte lineal de Bind_b = SkinToBone⁻¹.
        Dim skinToBone(nBones - 1) As Transform_Class
        Dim tB(nBones - 1) As System.Numerics.Vector3
        Dim col0(nBones - 1) As System.Numerics.Vector3
        Dim col1(nBones - 1) As System.Numerics.Vector3
        Dim col2(nBones - 1) As System.Numerics.Vector3
        For bb = 0 To nBones - 1
            Dim s2b = binds(boneList(bb))
            skinToBone(bb) = s2b
            Dim bindB = s2b.Inverse()
            Dim t = ApplyT(bindB, System.Numerics.Vector3.Zero)
            tB(bb) = t
            col0(bb) = ApplyT(bindB, New System.Numerics.Vector3(1, 0, 0)) - t
            col1(bb) = ApplyT(bindB, New System.Numerics.Vector3(0, 1, 0)) - t
            col2(bb) = ApplyT(bindB, New System.Numerics.Vector3(0, 0, 1)) - t
        Next

        Console.WriteLine($"[estimatesclp] estGlobal: joint solve — {uaVerts.Count} verts UA × {bodyPos.Length} verts body, {nBones} bones ({N} unknowns). NN naive O(Nua·Nbody), may take a while…")

        ' Ensamblado de las ecuaciones normales ATA·x = ATy. Se guardan las filas dispersas para el self-check.
        Dim ATA(N - 1, N - 1) As Double
        Dim ATy(N - 1) As Double
        Dim rows As New List(Of (T As System.Numerics.Vector3, ents As List(Of (idx As Integer, cx As Double, cy As Double, cz As Double))))()

        For Each vx In uaVerts
            ' p = body-vertex más cercano a u en espacio MODELO (nearest-neighbor GLOBAL, sobre TODOS los verts del body).
            Dim u = vx.model
            Dim best = -1
            Dim bestD As Single = Single.MaxValue
            For ci = 0 To bodyPos.Length - 1
                Dim d = System.Numerics.Vector3.DistanceSquared(u, bodyPos(ci))
                If d < bestD Then bestD = d : best = ci
            Next
            If best < 0 Then Continue For
            Dim p = bodyPos(best)

            ' T = u − Σ_b w_b·t_b ; coeficientes (Csx,Csy,Csz) por hueso influyente.
            Dim T = u
            Dim ents As New List(Of (idx As Integer, cx As Double, cy As Double, cz As Double))()
            For Each infl In vx.infl
                Dim bIdx As Integer
                If Not boneIdx.TryGetValue(infl.bone, bIdx) Then Continue For
                Dim w = infl.w
                Dim tb0 = tB(bIdx)
                T -= New System.Numerics.Vector3(CSng(w) * tb0.X, CSng(w) * tb0.Y, CSng(w) * tb0.Z)
                ' localp = p en el frame local del hueso b.
                Dim lp = ApplyT(skinToBone(bIdx), p)
                Dim a = CDbl(lp.X), b_ = CDbl(lp.Y), c = CDbl(lp.Z)
                Dim c0 = col0(bIdx), c1 = col1(bIdx), c2 = col2(bIdx)
                Dim baseIx = 3 * bIdx
                ' Csx_b = w·a·col0_b ; Csy_b = w·b_·col1_b ; Csz_b = w·c·col2_b  (Vector3 c/u → 3 componentes).
                ents.Add((baseIx + 0, w * a * CDbl(c0.X), w * a * CDbl(c0.Y), w * a * CDbl(c0.Z)))
                ents.Add((baseIx + 1, w * b_ * CDbl(c1.X), w * b_ * CDbl(c1.Y), w * b_ * CDbl(c1.Z)))
                ents.Add((baseIx + 2, w * c * CDbl(c2.X), w * c * CDbl(c2.Y), w * c * CDbl(c2.Z)))
            Next
            If ents.Count = 0 Then Continue For

            ' Cada componente d∈{X,Y,Z} es una fila del sistema: coeficientes dispersos + lado derecho T[d].
            Dim Td = New Double() {CDbl(T.X), CDbl(T.Y), CDbl(T.Z)}
            For d = 0 To 2
                For ii = 0 To ents.Count - 1
                    Dim ci = If(d = 0, ents(ii).cx, If(d = 1, ents(ii).cy, ents(ii).cz))
                    If ci = 0.0 Then Continue For
                    Dim gi = ents(ii).idx
                    ATy(gi) += ci * Td(d)
                    For jj = 0 To ents.Count - 1
                        Dim cj = If(d = 0, ents(jj).cx, If(d = 1, ents(jj).cy, ents(jj).cz))
                        If cj = 0.0 Then Continue For
                        ATA(gi, ents(jj).idx) += ci * cj
                    Next
                Next
            Next
            rows.Add((T, ents))
        Next

        ' Regularización hacia identidad: minimiza λ·(s−1)² por incógnita ⇒ +λ en la diagonal de ATA y +λ·1.0 en ATy.
        Dim trace As Double = 0.0
        For i = 0 To N - 1
            trace += ATA(i, i)
        Next
        Dim lambdaEff As Double = If(trace > 0.0, LAMBDA * (trace / N), LAMBDA)
        For i = 0 To N - 1
            ATA(i, i) += lambdaEff
            ATy(i) += lambdaEff * 1.0
        Next

        Dim x = SolveLinearSystem(ATA, ATy)

        ' SELF-CHECK: residual ‖A·x−y‖ del solve vs ‖A·1−y‖ (todas las incógnitas=1). Si el solve mejora → menor.
        Dim residSolve As Double = 0.0, residId As Double = 0.0
        For Each r In rows
            Dim Td = New Double() {CDbl(r.T.X), CDbl(r.T.Y), CDbl(r.T.Z)}
            For d = 0 To 2
                Dim predSolve As Double = 0.0, predId As Double = 0.0
                For Each e In r.ents
                    Dim ci = If(d = 0, e.cx, If(d = 1, e.cy, e.cz))
                    predSolve += ci * x(e.idx)
                    predId += ci * 1.0
                Next
                residSolve += (predSolve - Td(d)) * (predSolve - Td(d))
                residId += (predId - Td(d)) * (predId - Td(d))
            Next
        Next
        residSolve = Math.Sqrt(residSolve)
        residId = Math.Sqrt(residId)
        Dim verdict = If(residSolve < residId, "solve IMPROVES vs identity", "solve does NOT improve (review model/λ)")
        Console.WriteLine($"[estimatesclp] estGlobal SELF-CHECK: ‖A·x−y‖(solve)={residSolve.ToString("F4", inv)}  vs  ‖A·1−y‖(identity)={residId.ToString("F4", inv)}  ⇒ {verdict}   (N={N}, {rows.Count} verts, λ={lambdaEff.ToString("E3", inv)})")

        ' Empaquetá la solución por NOMBRE de hueso.
        Dim res As New Dictionary(Of String, (sx As Double, sy As Double, sz As Double))(StringComparer.OrdinalIgnoreCase)
        For bb = 0 To nBones - 1
            Dim baseIx = 3 * bb
            res(boneList(bb)) = (x(baseIx + 0), x(baseIx + 1), x(baseIx + 2))
        Next
        Return res
    End Function

    ''' <summary>Carga un .sclp del FilesDictionary (JSON de valores ABSOLUTOS 1.0=sin cambio) y devuelve
    ''' un dict boneName→{x,y,z}. Parseo inline con System.Text.Json (SclpFile.Load toma un PATH de disco;
    ''' el .sclp vive en el BA2). Acepta array top-level o un objeto que envuelve un único array, y claves
    ''' de eje "x"/"X" (lower/upper). Devuelve Nothing si el key no existe o el JSON no parsea.</summary>
    Private Function LoadSclpAbsolute(sclpKey As String) As Dictionary(Of String, Single())
        Dim sb As Byte() = Nothing
        Try
            sb = GetNifOrFileBytes(sclpKey)
        Catch
        End Try
        If sb Is Nothing OrElse sb.Length = 0 Then Return Nothing
        Dim result As New Dictionary(Of String, Single())(StringComparer.OrdinalIgnoreCase)
        Try
            Dim opts As New JsonDocumentOptions With {
                .CommentHandling = JsonCommentHandling.Skip,
                .AllowTrailingCommas = True
            }
            Using doc = JsonDocument.Parse(New ReadOnlyMemory(Of Byte)(sb), opts)
                ' Localizar el array de huesos: root si es array; si es objeto, la única propiedad array.
                Dim arr As JsonElement = doc.RootElement
                If arr.ValueKind = JsonValueKind.Object Then
                    Dim found As JsonElement = Nothing
                    Dim cnt = 0
                    For Each prop In arr.EnumerateObject()
                        If prop.Value.ValueKind = JsonValueKind.Array Then found = prop.Value : cnt += 1
                    Next
                    If cnt = 1 Then arr = found
                End If
                If arr.ValueKind <> JsonValueKind.Array Then Return Nothing
                For Each el In arr.EnumerateArray()
                    If el.ValueKind <> JsonValueKind.Object Then Continue For
                    Dim nm = ""
                    Dim nameEl As JsonElement
                    If el.TryGetProperty("Name", nameEl) AndAlso nameEl.ValueKind = JsonValueKind.String Then
                        nm = nameEl.GetString()
                    ElseIf el.TryGetProperty("name", nameEl) AndAlso nameEl.ValueKind = JsonValueKind.String Then
                        nm = nameEl.GetString()
                    End If
                    If String.IsNullOrEmpty(nm) Then Continue For
                    ' Formato vanilla REAL: los ejes van anidados bajo un objeto "Scale"
                    ' (ej. {"Name":"Spine1_Rear_skin","Scale":{"x":1.0,"y":1.2999,"z":1.1499}}).
                    ' Si existe "Scale" como objeto, leer x/y/z de ahí; si no, fallback al formato flat (ejes al tope).
                    Dim axisObj As JsonElement = el
                    Dim scaleEl As JsonElement
                    If el.TryGetProperty("Scale", scaleEl) AndAlso scaleEl.ValueKind = JsonValueKind.Object Then
                        axisObj = scaleEl
                    ElseIf el.TryGetProperty("scale", scaleEl) AndAlso scaleEl.ValueKind = JsonValueKind.Object Then
                        axisObj = scaleEl
                    End If
                    Dim xyz = New Single() {SclpAxis(axisObj, "x", "X"), SclpAxis(axisObj, "y", "Y"), SclpAxis(axisObj, "z", "Z")}
                    result(nm) = xyz
                Next
            End Using
        Catch ex As Exception
            Console.WriteLine($"[estimatesclp] .sclp '{sclpKey}' does not parse: {ex.Message}")
            Return Nothing
        End Try
        Return result
    End Function

    ''' <summary>Lee un eje del objeto .sclp (acepta clave lower/upper). Falta/no-numérico/NaN/Inf → 1.0.</summary>
    Private Function SclpAxis(obj As JsonElement, lowerKey As String, upperKey As String) As Single
        Dim el As JsonElement
        If Not obj.TryGetProperty(lowerKey, el) Then
            If Not obj.TryGetProperty(upperKey, el) Then Return 1.0F
        End If
        Dim v As Single
        Select Case el.ValueKind
            Case JsonValueKind.Number
                v = el.GetSingle()
            Case JsonValueKind.String
                If Not Single.TryParse(el.GetString(), System.Globalization.NumberStyles.Float Or System.Globalization.NumberStyles.AllowThousands,
                                       System.Globalization.CultureInfo.InvariantCulture, v) Then Return 1.0F
            Case Else
                Return 1.0F
        End Select
        If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then Return 1.0F
        Return v
    End Function

    ''' <summary>Mediana de una lista de Single (0 si vacía). Ordena una copia.</summary>
    Private Function MedianOf(vals As List(Of Single)) As Single
        If vals Is Nothing OrElse vals.Count = 0 Then Return 0.0F
        Dim s = vals.OrderBy(Function(x) x).ToList()
        Dim n = s.Count
        If (n And 1) = 1 Then Return s(n \ 2)
        Return (s(n \ 2 - 1) + s(n \ 2)) / 2.0F
    End Function

    ''' <summary>ESTIMADOR APROXIMADO de valores SCLP (bone-scale por hueso <c>*_skin</c>) de un underarmor.
    ''' <para>MÉTODO: se cargan el NIF del underarmor y un NIF de cuerpo de REFERENCIA; para cada uno, cada
    ''' vértice skinneado se lleva al ESPACIO LOCAL DE SU HUESO (con el bind skin→bone del shape) y se mide
    ''' el extent por eje X/Y/Z (RMS y media-de-|·| ponderados por peso). El ratio underarmor/body de esas
    ''' medidas, por hueso y eje, es el estimador de escala. Se compara contra el <c>.sclp</c> autorado
    ''' vanilla (ground truth, valores ABSOLUTOS 1.0=sin cambio) y se imprime el error.</para>
    ''' <para>NATURALEZA: es un ESTIMADOR, NO una replicación del motor. Un ratio de extents ≠ el SCLP
    ''' autorado: el artista puede escalar un hueso sin que el extent de la malla cambie en la misma
    ''' proporción (vértices no centrados en el hueso, solape entre huesos, pesos parciales). Sirve para
    ''' ver CUÁNTO se acerca el probe geométrico al valor real, no para derivar el .sclp de producción.</para>
    ''' <para>Spec: <c>--estimatesclp "&lt;underarmorNifKey&gt;|&lt;bodyNifKey&gt;[|&lt;sclpKey&gt;]"</c>. Si se omite
    ''' sclpKey se deriva del underarmor cambiando la extensión a <c>.sclp</c>.</para></summary>
    Private Sub EstimateSclpRun(spec As String, Optional shapeFilter As String = "")
        ' Parámetros tuneables (constantes locales para iterar fácil sin exponerlos por CLI):
        Const WT As Single = 0.1F     ' peso mínimo para que un vértice cuente hacia un hueso
        Const MINW As Single = 1.0F   ' Σpeso mínimo por hueso (en AMBOS NIFs) para reportarlo — filtra ruido
        Dim inv = System.Globalization.CultureInfo.InvariantCulture
        Dim axisName = New String() {"X", "Y", "Z"}

        Dim parts = spec.Split("|"c)
        If parts.Length < 2 OrElse parts(0).Trim() = "" OrElse parts(1).Trim() = "" Then
            Console.WriteLine("[estimatesclp] usage: --estimatesclp ""<underarmorNifKey>|<bodyNifKey>[|<sclpKey>]""")
            Return
        End If
        Dim uaKey = parts(0).Trim()
        Dim bodyKey = parts(1).Trim()
        Dim sclpKey = If(parts.Length >= 3 AndAlso parts(2).Trim() <> "", parts(2).Trim(), Path.ChangeExtension(uaKey, ".sclp"))

        Console.WriteLine("[estimatesclp] APPROXIMATE SCLP ESTIMATOR (ratio of extents in bone-space; does NOT replicate the engine)")
        Console.WriteLine($"   underarmor = {uaKey}")
        Console.WriteLine($"   body(ref)  = {bodyKey}")
        Console.WriteLine($"   sclp(auth) = {sclpKey}")
        Console.WriteLine($"   wThreshold = {WT.ToString(inv)}   minSumW = {MINW.ToString(inv)}")
        If shapeFilter <> "" Then Console.WriteLine($"   shapefilter = {shapeFilter}   (only ua shapes whose name contains it; the body is NOT filtered)")

        Dim uaAcc = AccumulateBoneExtents(uaKey, WT, shapeFilter)
        If uaAcc Is Nothing Then Return
        Dim bodyAcc = AccumulateBoneExtents(bodyKey, WT)
        If bodyAcc Is Nothing Then Return

        ' estNN: nearest-neighbor least-squares (sin umbral de dominancia; cubre también huesos blend).
        ' wEps=0.01 SOLO excluye pesos numéricamente nulos; el peso real pondera la regresión.
        Const NN_WEPS As Single = 0.01F
        Dim nnPairs = BuildNNPairs(uaKey, bodyKey, NN_WEPS, shapeFilter)   ' pares NN compartidos por estNN y estFull (filtro ua)
        Dim nn = AccumulateNNScales(nnPairs)
        If nn Is Nothing Then nn = New Dictionary(Of String, (sx As Double, sy As Double, sz As Double, nPairs As Integer, okX As Boolean, okY As Boolean, okZ As Boolean, residX As Double, residY As Double, residZ As Double))(StringComparer.OrdinalIgnoreCase)
        Dim full = AccumulateFullFit(nnPairs)   ' estFull: ajuste de matriz 3×3 completa por hueso (frame-aware)
        If full Is Nothing Then full = New Dictionary(Of String, (Lxx As Double, Lyy As Double, Lzz As Double, offNorm As Double, residFull As Double, ok As Boolean))(StringComparer.OrdinalIgnoreCase)
        Console.WriteLine($"   estNN: {nn.Count} bone(s) with nearest-neighbor estimate (wEps={NN_WEPS.ToString(inv)})   estFull: {full.Count} bone(s) with 3×3 fit")

        ' estGlobal: solve GLOBAL CONJUNTO por mínimos cuadrados de TODAS las escalas de hueso a la vez.
        Dim glob = EstimateGlobalScales(uaKey, bodyKey, NN_WEPS, shapeFilter)
        If glob Is Nothing Then glob = New Dictionary(Of String, (sx As Double, sy As Double, sz As Double))(StringComparer.OrdinalIgnoreCase)
        Console.WriteLine($"   estGlobal: {glob.Count} bone(s) with joint solve")

        Dim authored = LoadSclpAbsolute(sclpKey)   ' Nothing si no existe / no parsea
        If authored Is Nothing Then
            Console.WriteLine($"   (warning) .sclp '{sclpKey}' not found/parseable — continuing without authored/error column")
        Else
            Console.WriteLine($"   .sclp: {authored.Count} authored entry(ies)")
        End If

        ' Huesos *_skin presentes en AMBOS acumuladores con Σpeso suficiente en ambos.
        Dim skinBones = uaAcc.Keys.
            Where(Function(k) k.EndsWith("_skin", StringComparison.OrdinalIgnoreCase) AndAlso
                              bodyAcc.ContainsKey(k) AndAlso
                              uaAcc(k).SumW >= MINW AndAlso bodyAcc(k).SumW >= MINW).
            OrderBy(Function(k) k, StringComparer.OrdinalIgnoreCase).ToList()

        Const DOMMIN As Integer = 8   ' mínimo de vértices dominados (en AMBOS NIFs) para que estDomBox/estDomRms cuenten
        Console.WriteLine("")
        Console.WriteLine($"   {skinBones.Count} comparable *_skin bone(s) (with Σweight ≥ {MINW.ToString(inv)} in both NIFs)")
        Console.WriteLine($"   estRMS/estMean = weight-weighted extent (blended, with noise).  estDomBox/estDomRms = ONLY dominated vertices (weight>0.5), clean geometry (gate ≥{DOMMIN} dominated verts in both).")
        Console.WriteLine("")
        ' Tabla
        Dim fmtRow = "   {0,-24} {1,-3} {2,8} {3,8} {4,8} {5,9} {6,9} {7,8} {8}"
        Console.WriteLine("   " & New String("-"c, 108))
        Console.WriteLine(String.Format(fmtRow,
                                        "bone", "ax", "authored", "estRMS", "estMean", "estDomBox", "estDomRms", "bindΔT", "flag"))
        Console.WriteLine("   " & New String("-"c, 108))

        ' Acumuladores del resumen por-métrica (solo sobre ejes que el artista MOVIÓ: authored ≠ 1.0).
        ' Para cada métrica: "All" (X/Y/Z) y "YZ" (excluye X, que el motor casi no usa — el veredicto).
        Dim rmsAll As New MetricAcc, rmsYZ As New MetricAcc
        Dim meanAll As New MetricAcc, meanYZ As New MetricAcc
        Dim boxAll As New MetricAcc, boxYZ As New MetricAcc
        Dim drmsAll As New MetricAcc, drmsYZ As New MetricAcc
        Dim nnAll As New MetricAcc, nnYZ As New MetricAcc
        Dim fullAll As New MetricAcc, fullYZ As New MetricAcc   ' estFull: error de diag(L) vs authored
        Dim globAll As New MetricAcc, globYZ As New MetricAcc   ' estGlobal: error del solve conjunto vs authored
        Dim offNormMoved As New List(Of Single)   ' offNorm de L sobre huesos con eje Y/Z movido
        Dim residFullMoved As New List(Of Single) ' residFull del ajuste 3×3 sobre esos huesos
        Dim movedCount = 0
        Dim falsePos = 0, falseNeg = 0   ' probe vs authored (usando estRMS como ratio)
        ' Coverage por HUESO (huesos con algún eje authored ≠ 1.0): cuántos cubre estNN vs estDomBox vs estGlobal.
        Dim movedBonesTotal = 0, nnCoverBones = 0, domBoxCoverBones = 0, globCoverBones = 0
        ' Residual de reconstrucción del ajuste NN (calidad intrínseca del modelo escala-por-hueso, indep. del authored).
        Dim residYZ As New List(Of Single)   ' residuales de los ejes Y/Z movidos
        Dim bigErrHiResid = 0, bigErrLoResid = 0   ' de ejes Y/Z con |estNN−authored|>0.10: residual alto (>0.25) vs bajo

        ' Helper: |est−authored| si est no es NaN → alimenta la métrica (All y, si eje≠X, YZ).
        Dim feed = Sub(est As Single, auVal As Single, axis As Integer, maAll As MetricAcc, maYZ As MetricAcc)
                       If Single.IsNaN(est) Then Return
                       Dim e = Math.Abs(est - auVal)
                       maAll.Add(e)
                       If axis > 0 Then maYZ.Add(e)
                   End Sub

        For Each bn In skinBones
            Dim ua = uaAcc(bn)
            Dim bd = bodyAcc(bn)
            Dim bindDelta = (ua.BindT - bd.BindT).Length()
            Dim frameFlag = If(bindDelta > 1.0F, "*FRAME?*", "")
            Dim domOk = ua.DomCount >= DOMMIN AndAlso bd.DomCount >= DOMMIN   ' gate del set dominado

            Dim auRow As Single() = Nothing
            Dim hasAu = authored IsNot Nothing AndAlso authored.TryGetValue(bn, auRow)

            Dim nnRow As (sx As Double, sy As Double, sz As Double, nPairs As Integer, okX As Boolean, okY As Boolean, okZ As Boolean, residX As Double, residY As Double, residZ As Double) = Nothing
            Dim hasNN = nn.TryGetValue(bn, nnRow)
            Dim fullRow As (Lxx As Double, Lyy As Double, Lzz As Double, offNorm As Double, residFull As Double, ok As Boolean) = Nothing
            Dim hasFull = full.TryGetValue(bn, fullRow) AndAlso fullRow.ok
            Dim globRow As (sx As Double, sy As Double, sz As Double) = Nothing
            Dim hasGlob = glob.TryGetValue(bn, globRow)
            Dim boneMoved = False, nnCov = False, boxCov = False, globCov = False   ' coverage por hueso (ejes movidos)
            Dim boneMovedYZ = False   ' el hueso tiene algún eje Y/Z autorado ≠ 1.0 (para offNorm/residFull, que son por-hueso)

            For a = 0 To 2
                Dim rmsB = bd.Rms(a)
                Dim meanB = bd.MeanAbs(a)
                Dim estRms = If(rmsB > 0.0F, ua.Rms(a) / rmsB, Single.NaN)
                Dim estMean = If(meanB > 0.0F, ua.MeanAbs(a) / meanB, Single.NaN)
                Dim boxB = bd.DomHalfRange(a)
                Dim drmsB = bd.DomRmsC(a)
                Dim estDomBox = If(domOk AndAlso boxB > 0.0F, ua.DomHalfRange(a) / boxB, Single.NaN)
                Dim estDomRms = If(domOk AndAlso drmsB > 0.0F, ua.DomRmsC(a) / drmsB, Single.NaN)

                ' estNN (nearest-neighbor LSQ): disponible si el hueso tiene par NN y el eje pasó el guard Sxx≥1e-6.
                Dim estNN As Single = Single.NaN
                Dim residNN As Single = Single.NaN
                If hasNN Then
                    Select Case a
                        Case 0 : If nnRow.okX Then estNN = CSng(nnRow.sx) : residNN = CSng(nnRow.residX)
                        Case 1 : If nnRow.okY Then estNN = CSng(nnRow.sy) : residNN = CSng(nnRow.residY)
                        Case 2 : If nnRow.okZ Then estNN = CSng(nnRow.sz) : residNN = CSng(nnRow.residZ)
                    End Select
                End If

                ' estFull (ajuste 3×3): la escala FRAME-AWARE de este eje = diag(L) del hueso.
                Dim estFull As Single = Single.NaN
                If hasFull Then
                    Select Case a
                        Case 0 : estFull = CSng(fullRow.Lxx)
                        Case 1 : estFull = CSng(fullRow.Lyy)
                        Case 2 : estFull = CSng(fullRow.Lzz)
                    End Select
                End If

                ' estGlobal (solve conjunto): la escala de este eje = (sx/sy/sz) del hueso en el vector solución.
                Dim estGlobal As Single = Single.NaN
                If hasGlob Then
                    Select Case a
                        Case 0 : estGlobal = CSng(globRow.sx)
                        Case 1 : estGlobal = CSng(globRow.sy)
                        Case 2 : estGlobal = CSng(globRow.sz)
                    End Select
                End If

                Dim auTxt = "-"
                If hasAu Then
                    Dim auVal = auRow(a)
                    auTxt = auVal.ToString("F4", inv)
                    Dim authoredMoved = Math.Abs(auVal - 1.0F) > 0.0000001F
                    If authoredMoved Then
                        movedCount += 1
                        feed(estRms, auVal, a, rmsAll, rmsYZ)
                        feed(estMean, auVal, a, meanAll, meanYZ)
                        feed(estDomBox, auVal, a, boxAll, boxYZ)
                        feed(estDomRms, auVal, a, drmsAll, drmsYZ)
                        feed(estNN, auVal, a, nnAll, nnYZ)
                        feed(estFull, auVal, a, fullAll, fullYZ)
                        feed(estGlobal, auVal, a, globAll, globYZ)
                        boneMoved = True
                        If a > 0 Then boneMovedYZ = True
                        If Not Single.IsNaN(estNN) Then nnCov = True
                        If Not Single.IsNaN(estDomBox) Then boxCov = True
                        If Not Single.IsNaN(estGlobal) Then globCov = True
                        ' Residual del ajuste NN sobre ejes Y/Z movidos + correlación error-vs-authored ↔ residual.
                        If a > 0 AndAlso Not Single.IsNaN(residNN) Then
                            residYZ.Add(residNN)
                            If Not Single.IsNaN(estNN) AndAlso Math.Abs(estNN - auVal) > 0.1F Then
                                If residNN > 0.25F Then bigErrHiResid += 1 Else bigErrLoResid += 1
                            End If
                        End If
                    End If

                    ' Falsos positivos/negativos del probe (ratio = estRMS) vs authored:
                    Dim probeMoved = (Not Single.IsNaN(estRms)) AndAlso (estRms < 0.97F OrElse estRms > 1.03F)
                    If authoredMoved Then
                        If Not probeMoved Then falseNeg += 1   ' authored≠1 pero probe estimó ≈1
                    Else
                        If probeMoved Then falsePos += 1        ' authored=1 pero probe estimó movido
                    End If
                End If

                Dim estRmsTxt = If(Single.IsNaN(estRms), "-", estRms.ToString("F4", inv))
                Dim estMeanTxt = If(Single.IsNaN(estMean), "-", estMean.ToString("F4", inv))
                Dim estBoxTxt = If(Single.IsNaN(estDomBox), "n/a", estDomBox.ToString("F4", inv))
                Dim estDrmsTxt = If(Single.IsNaN(estDomRms), "n/a", estDomRms.ToString("F4", inv))
                Dim boneCol = If(a = 0, bn, "")
                Console.WriteLine(String.Format(fmtRow,
                                                boneCol, axisName(a), auTxt, estRmsTxt, estMeanTxt,
                                                estBoxTxt, estDrmsTxt, bindDelta.ToString("F3", inv), frameFlag))
            Next
            If boneMoved Then
                movedBonesTotal += 1
                If nnCov Then nnCoverBones += 1
                If boxCov Then domBoxCoverBones += 1
                If globCov Then globCoverBones += 1
            End If
            ' offNorm / residFull son POR-HUESO (no por-eje): recolectá una vez sobre huesos con eje Y/Z movido.
            If boneMovedYZ AndAlso hasFull Then
                offNormMoved.Add(CSng(fullRow.offNorm))
                residFullMoved.Add(CSng(fullRow.residFull))
            End If
        Next

        ' Resumen por-métrica
        Console.WriteLine("   " & New String("-"c, 108))
        Console.WriteLine("")
        Console.WriteLine("   ── SUMMARY (only axes with authored ≠ 1.0 = those the artist MOVED) ──")
        If authored Is Nothing Then
            Console.WriteLine("   (no authored .sclp — no ground truth to compute error)")
        ElseIf movedCount = 0 Then
            Console.WriteLine("   0 authored axes ≠ 1.0 among the comparable bones (nothing to evaluate).")
        Else
            Console.WriteLine($"   moved axes evaluated (X/Y/Z): {movedCount}")
            Dim printMetric = Sub(label As String, ma As MetricAcc)
                                  Dim mean = If(ma.Errs.Count > 0, ma.Errs.Average(), 0.0F)
                                  Console.WriteLine($"   {label,-20} n={ma.Errs.Count,3}  meanErr={mean.ToString("F4", inv)}  median={MedianOf(ma.Errs).ToString("F4", inv)}  ±0.05={ma.In05,3}  ±0.10={ma.In10,3}")
                              End Sub
            printMetric("estRMS   [X/Y/Z]", rmsAll)
            printMetric("estRMS   [Y/Z]", rmsYZ)
            printMetric("estMean  [X/Y/Z]", meanAll)
            printMetric("estMean  [Y/Z]", meanYZ)
            printMetric("estDomBox[X/Y/Z]", boxAll)
            printMetric("estDomBox[Y/Z]", boxYZ)
            printMetric("estDomRms[X/Y/Z]", drmsAll)
            printMetric("estDomRms[Y/Z]", drmsYZ)
            printMetric("estNN    [X/Y/Z]", nnAll)
            printMetric("estNN    [Y/Z]", nnYZ)
            printMetric("estFull  [X/Y/Z]", fullAll)
            printMetric("estFull  [Y/Z]", fullYZ)
            printMetric("estGlobal[X/Y/Z]", globAll)
            printMetric("estGlobal[Y/Z]", globYZ)
            Console.WriteLine($"   coverage moved bones (authored≠1.0): total={movedBonesTotal}  with estNN={nnCoverBones}  with estDomBox={domBoxCoverBones}  with estGlobal={globCoverBones}  (estNN/estGlobal also cover blend bones)")
            ' Residual de reconstrucción del ajuste NN (0 = escala-por-hueso perfecta; alto = la prenda NO es una escala-por-hueso pura).
            Console.WriteLine($"   estNN residual [Y/Z]  n={residYZ.Count,3}  median={MedianOf(residYZ).ToString("F4", inv)}  (fraction of lu variance NOT explained by s·lp; INTRINSIC quality, indep. of authored)")
            Console.WriteLine($"   correlation error↔residual (Y/Z axes with |estNN−authored|>0.10): high residual(>0.25)={bigErrHiResid}  vs low={bigErrLoResid}")
            Console.WriteLine("     └ if high error-vs-authored coincides with high residual → the garment is not pure per-bone scale (limit of the SCLP model, not the estimator).")
            ' estFull: cuán NO-diagonal es el ajuste 3×3 (offNorm) y su residual (≤ residual estNN si el ajuste diagonal perdía señal fuera de eje).
            Console.WriteLine($"   estFull offdiag [Y/Z]  n={offNormMoved.Count,3}  median={MedianOf(offNormMoved).ToString("F4", inv)}  (0=pure axis scale; high=rotation/shear not representable as axis scale)")
            Console.WriteLine($"   estFull residual [Y/Z]  n={residFullMoved.Count,3}  median={MedianOf(residFullMoved).ToString("F4", inv)}  (residual of the full 3×3 linear fit; if it drops a lot vs estNN, the diagonal fit was losing signal to off-axis terms)")
        End If
        Console.WriteLine($"   false positives (authored=1.0 but estRMS∉[0.97,1.03]): {falsePos}")
        Console.WriteLine($"   false negatives (authored≠1.0 but estRMS∈[0.97,1.03]): {falseNeg}")
        Console.WriteLine("")
        Console.WriteLine("   NOTE: approximate geometric estimator; ratio of extents ≠ authored SCLP (see doc-comment).")
        Console.WriteLine("   [Y/Z] excludes the X axis (the engine barely uses it) — it's the most relevant row for the verdict.")
    End Sub

    ' =====================================================================================
    ' --sclpdiag : VOLCADO DIAGNÓSTICO de geometría cruda por hueso (para derivar a mano la fórmula SCLP)
    ' =====================================================================================

    ''' <summary>Geometría cruda de UN hueso en su ESPACIO LOCAL, guardada como listas por eje (X/Y/Z) para
    ''' poder computar percentiles a mano. <c>All*</c> = vértices con peso &gt; umbral hacia el hueso (con su
    ''' peso paralelo en <c>AllW</c>); <c>Dom*</c> = vértices cuyo slot de MAYOR peso es este hueso y ese peso
    ''' &gt; 0.5 (sin peso, geometría limpia).</summary>
    Private Class BoneLocals
        ' allSet (peso > umbral): valor local por eje + peso paralelo
        Public AllX As New List(Of Single)
        Public AllY As New List(Of Single)
        Public AllZ As New List(Of Single)
        Public AllW As New List(Of Single)
        ' domSet (slot dominante > 0.5): valor local por eje, sin peso
        Public DomX As New List(Of Single)
        Public DomY As New List(Of Single)
        Public DomZ As New List(Of Single)

        Public ReadOnly Property NAll As Integer
            Get
                Return AllW.Count
            End Get
        End Property
        Public ReadOnly Property NDom As Integer
            Get
                Return DomX.Count
            End Get
        End Property

        Public Function AllAxis(a As Integer) As List(Of Single)
            Select Case a
                Case 0 : Return AllX
                Case 1 : Return AllY
                Case Else : Return AllZ
            End Select
        End Function
        Public Function DomAxis(a As Integer) As List(Of Single)
            Select Case a
                Case 0 : Return DomX
                Case 1 : Return DomY
                Case Else : Return DomZ
            End Select
        End Function
    End Class

    ''' <summary>Estadísticos calculados por (hueso,eje) para UN NIF, listos para imprimir.</summary>
    Private Class AxisStats
        Public NAll As Integer = 0
        Public NDom As Integer = 0
        ' del domSet (sin peso)
        Public DMin As Single = Single.NaN
        Public DMax As Single = Single.NaN
        Public DMean As Single = Single.NaN
        Public DP05 As Single = Single.NaN
        Public DP95 As Single = Single.NaN
        Public DMaxAbsO As Single = Single.NaN   ' p95 de |axis| respecto al ORIGEN (domSet)
        Public DHalfRange As Single = Single.NaN ' (max-min)/2
        ' del allSet (ponderado por peso)
        Public WMean As Single = Single.NaN      ' Σw·axis/Σw
        Public WRmsO As Single = Single.NaN      ' sqrt(Σw·axis²/Σw) — RMS al ORIGEN
        Public AP95Abs As Single = Single.NaN    ' p95 de |axis| sobre el allSet (sin peso)
    End Class

    ''' <summary>Percentil por rango-cercano simple sobre una lista YA ORDENADA ascendente: índice
    ''' <c>floor(p·(n−1))</c>, clamp a [0,n−1]. NaN si vacía.</summary>
    Private Function Pctl(sorted As List(Of Single), p As Single) As Single
        If sorted Is Nothing OrElse sorted.Count = 0 Then Return Single.NaN
        Dim n = sorted.Count
        Dim idx = CInt(Math.Floor(p * (n - 1)))
        If idx < 0 Then idx = 0
        If idx >= n Then idx = n - 1
        Return sorted(idx)
    End Function

    ''' <summary>Calcula todos los estadísticos de un (hueso,eje) a partir de sus listas crudas.</summary>
    Private Function ComputeAxisStats(bl As BoneLocals, axis As Integer) As AxisStats
        Dim s As New AxisStats()
        If bl Is Nothing Then Return s
        s.NAll = bl.NAll
        s.NDom = bl.NDom

        ' --- domSet (sin peso) ---
        Dim dom = bl.DomAxis(axis)
        If dom IsNot Nothing AndAlso dom.Count > 0 Then
            Dim n = dom.Count
            Dim sd = dom.OrderBy(Function(x) x).ToList()
            s.DMin = sd(0)
            s.DMax = sd(n - 1)
            Dim sum As Double = 0.0
            For Each v In dom : sum += v : Next
            s.DMean = CSng(sum / n)
            s.DP05 = Pctl(sd, 0.05F)
            s.DP95 = Pctl(sd, 0.95F)
            Dim sdAbs = dom.Select(Function(x) Math.Abs(x)).OrderBy(Function(x) x).ToList()
            s.DMaxAbsO = Pctl(sdAbs, 0.95F)
            s.DHalfRange = (s.DMax - s.DMin) / 2.0F
        End If

        ' --- allSet (ponderado por peso) ---
        Dim allv = bl.AllAxis(axis)
        Dim allw = bl.AllW
        If allv IsNot Nothing AndAlso allv.Count > 0 AndAlso allw IsNot Nothing AndAlso allw.Count = allv.Count Then
            Dim sw As Double = 0.0, swv As Double = 0.0, swv2 As Double = 0.0
            For k = 0 To allv.Count - 1
                Dim w As Double = allw(k)
                Dim v As Double = allv(k)
                sw += w
                swv += w * v
                swv2 += w * v * v
            Next
            If sw > 0.0 Then
                s.WMean = CSng(swv / sw)
                Dim rms = swv2 / sw
                If rms < 0.0 Then rms = 0.0
                s.WRmsO = CSng(Math.Sqrt(rms))
            End If
            Dim aAbs = allv.Select(Function(x) Math.Abs(x)).OrderBy(Function(x) x).ToList()
            s.AP95Abs = Pctl(aAbs, 0.95F)
        End If
        Return s
    End Function

    ''' <summary>Carga un NIF del FilesDictionary y recolecta, por nombre de hueso, la geometría CRUDA en el
    ''' ESPACIO LOCAL DE CADA HUESO (mismo <c>bind.ComposeTransforms(traslación-por-punto)</c> que
    ''' <see cref="AccumulateBoneExtents"/>): <c>allSet</c> (peso &gt; <paramref name="wThreshold"/>, con peso) y
    ''' <c>domSet</c> (slot dominante &gt; 0.5, plano). Devuelve Nothing (y avisa) si el NIF no existe/no carga.</summary>
    Private Function AccumulateBoneLocals(nifKey As String, wThreshold As Single, Optional shapeNameFilter As String = "") As Dictionary(Of String, BoneLocals)
        Dim bytes As Byte() = Nothing
        Try
            bytes = GetNifOrFileBytes(nifKey)
        Catch ex As Exception
            Console.WriteLine($"[sclpdiag] error reading '{nifKey}': {ex.Message}")
            Return Nothing
        End Try
        If bytes Is Nothing OrElse bytes.Length = 0 Then
            Console.WriteLine($"[sclpdiag] '{nifKey}' does not exist / empty in the FilesDictionary")
            Return Nothing
        End If
        Dim nif As New Nifcontent_Class_Manolo()
        Try
            nif.Load_Manolo(bytes)
        Catch ex As Exception
            Console.WriteLine($"[sclpdiag] '{nifKey}' does not load as NIF: {ex.Message}")
            Return Nothing
        End Try

        Dim acc As New Dictionary(Of String, BoneLocals)(StringComparer.OrdinalIgnoreCase)
        Dim shapesSkinned = 0
        Dim shapesKept = 0   ' shapes que pasan el filtro por nombre (solo relevante si shapeNameFilter <> "")
        For blkIdx = 0 To nif.Blocks.Count - 1
            Dim shp = TryCast(nif.Blocks(blkIdx), NiflySharp.INiShape)
            If shp Is Nothing Then Continue For
            Dim rs As NifRenderableShape
            Try
                rs = New NifRenderableShape(nif, shp, blkIdx)
            Catch
                Continue For
            End Try
            If shapeNameFilter <> "" Then
                If rs.ShapeName Is Nothing OrElse rs.ShapeName.IndexOf(shapeNameFilter, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
                shapesKept += 1
            End If
            If rs.ShapeBones Is Nothing OrElse rs.ShapeBones.Count = 0 Then Continue For
            Dim verts As List(Of System.Numerics.Vector3) = Nothing
            Try
                verts = rs.Geometry?.GetVertexPositions()
            Catch
            End Try
            If verts Is Nothing OrElse verts.Count = 0 Then Continue For
            Dim skin As ShapeSkinningData
            Try
                skin = rs.Geometry.GetSkinning()
            Catch
                Continue For
            End Try
            Dim wpv = skin.WeightsPerVertex
            If wpv <= 0 OrElse skin.BoneIndices Is Nothing OrElse skin.BoneWeights Is Nothing Then Continue For
            Dim bones = rs.ShapeBones
            Dim binds = rs.ShapeBoneTransforms
            If binds Is Nothing Then Continue For
            shapesSkinned += 1
            Dim nVerts = Math.Min(verts.Count, skin.VertexCount)
            For i = 0 To nVerts - 1
                Dim vp = verts(i)

                ' Slot dominante del vértice (mayor peso entre sus slots).
                Dim domSlot = -1
                Dim domW As Single = 0.0F
                For j = 0 To wpv - 1
                    Dim idx = i * wpv + j
                    If idx >= skin.BoneWeights.Length Then Continue For
                    Dim wj As Single = CType(skin.BoneWeights(idx), Single)
                    If wj > domW Then domW = wj : domSlot = j
                Next

                ' allSet: cada slot con peso > umbral.
                For j = 0 To wpv - 1
                    Dim idx = i * wpv + j
                    If idx >= skin.BoneWeights.Length OrElse idx >= skin.BoneIndices.Length Then Continue For
                    Dim w As Single = CType(skin.BoneWeights(idx), Single)
                    If w <= wThreshold Then Continue For
                    Dim bi = CInt(skin.BoneIndices(idx))
                    If bi < 0 OrElse bi >= bones.Count OrElse bi >= binds.Count Then Continue For
                    Dim bn = TryCast(bones(bi), NiflySharp.Blocks.NiNode)?.Name?.String
                    If String.IsNullOrEmpty(bn) Then Continue For
                    Dim bind = binds(bi)
                    If bind Is Nothing Then Continue For
                    Dim pT As New Transform_Class With {.Translation = vp}
                    Dim lp = bind.ComposeTransforms(pT).Translation
                    Dim bl As BoneLocals = Nothing
                    If Not acc.TryGetValue(bn, bl) Then
                        bl = New BoneLocals()
                        acc(bn) = bl
                    End If
                    bl.AllX.Add(lp.X) : bl.AllY.Add(lp.Y) : bl.AllZ.Add(lp.Z) : bl.AllW.Add(w)
                Next

                ' domSet: una vez por vértice, al hueso dominante si domW > 0.5.
                If domSlot >= 0 AndAlso domW > 0.5F Then
                    Dim idx = i * wpv + domSlot
                    If idx < skin.BoneIndices.Length Then
                        Dim bi = CInt(skin.BoneIndices(idx))
                        If bi >= 0 AndAlso bi < bones.Count AndAlso bi < binds.Count Then
                            Dim bn = TryCast(bones(bi), NiflySharp.Blocks.NiNode)?.Name?.String
                            Dim bind = binds(bi)
                            If Not String.IsNullOrEmpty(bn) AndAlso bind IsNot Nothing Then
                                Dim pT As New Transform_Class With {.Translation = vp}
                                Dim lp = bind.ComposeTransforms(pT).Translation
                                Dim bl As BoneLocals = Nothing
                                If Not acc.TryGetValue(bn, bl) Then
                                    bl = New BoneLocals()
                                    acc(bn) = bl
                                End If
                                bl.DomX.Add(lp.X) : bl.DomY.Add(lp.Y) : bl.DomZ.Add(lp.Z)
                            End If
                        End If
                    End If
                End If
            Next
        Next
        Dim filterNote = If(shapeNameFilter <> "", $" [filter '{shapeNameFilter}': {shapesKept} shape(s) after filter]", "")
        Console.WriteLine($"[sclpdiag]   '{nifKey}': {shapesSkinned} skinned shape(s), {acc.Count} bone(s) with weight{filterNote}")
        Return acc
    End Function

    ''' <summary>Formatea un Single a ancho fijo, mostrando "n/a" si es NaN.</summary>
    Private Function FmtStat(v As Single, inv As System.Globalization.CultureInfo) As String
        If Single.IsNaN(v) Then Return "n/a"
        Return v.ToString("F4", inv)
    End Function

    ''' <summary>Ratio ua/body con guardas: NaN si algún operando es NaN o el body es ~0.</summary>
    Private Function RatioStat(ua As Single, bd As Single) As Single
        If Single.IsNaN(ua) OrElse Single.IsNaN(bd) OrElse Math.Abs(bd) < 0.0000001F Then Return Single.NaN
        Return ua / bd
    End Function

    ''' <summary>Parte LINEAL de un <see cref="Transform_Class"/> aplicada a un vector, a prueba de convención de
    ''' matriz: <c>L·v = tr.ComposeTransforms(v).Translation − tr.ComposeTransforms(0).Translation</c> (mismo
    ''' patrón applyT que ya usa el estimador). No depende de si el scale vive en Rotation, ScaleVector o Scale.</summary>
    Private Function BindApplyLinear(tr As Transform_Class, v As System.Numerics.Vector3) As System.Numerics.Vector3
        Dim zero = tr.ComposeTransforms(New Transform_Class With {.Translation = System.Numerics.Vector3.Zero}).Translation
        Dim p = tr.ComposeTransforms(New Transform_Class With {.Translation = v}).Translation
        Return New System.Numerics.Vector3(p.X - zero.X, p.Y - zero.Y, p.Z - zero.Z)
    End Function

    ''' <summary>Normas de las 3 columnas del bloque lineal 3×3 de un <see cref="Transform_Class"/>, vía
    ''' <see cref="BindApplyLinear"/> (col j = |L·e_j|). Detecta escala horneada en la parte lineal — uniforme o
    ''' no — sin depender de la convención de columnas de la matriz.</summary>
    Private Function BindColNorms(tr As Transform_Class) As (n0 As Double, n1 As Double, n2 As Double)
        Dim c0 = BindApplyLinear(tr, New System.Numerics.Vector3(1.0F, 0.0F, 0.0F))
        Dim c1 = BindApplyLinear(tr, New System.Numerics.Vector3(0.0F, 1.0F, 0.0F))
        Dim c2 = BindApplyLinear(tr, New System.Numerics.Vector3(0.0F, 0.0F, 1.0F))
        Return (CDbl(c0.Length()), CDbl(c1.Length()), CDbl(c2.Length()))
    End Function

    ''' <summary>Carga un NIF (key del FilesDictionary o ruta de disco, vía <see cref="GetNifOrFileBytes"/>) y
    ''' construye, por NOMBRE de hueso, el bind skin→bone (<c>ShapeBoneTransforms</c>; primero gana si el hueso
    ''' aparece en varios shapes). Devuelve Nothing (y avisa) si el NIF no existe/no carga.</summary>
    Private Function LoadBindsByBone(nifKey As String) As Dictionary(Of String, Transform_Class)
        Dim bytes As Byte() = Nothing
        Try
            bytes = GetNifOrFileBytes(nifKey)
        Catch ex As Exception
            Console.WriteLine($"[binddiff] error reading '{nifKey}': {ex.Message}")
            Return Nothing
        End Try
        If bytes Is Nothing OrElse bytes.Length = 0 Then
            Console.WriteLine($"[binddiff] '{nifKey}' does not exist / empty in the FilesDictionary")
            Return Nothing
        End If
        Dim nif As New Nifcontent_Class_Manolo()
        Try
            nif.Load_Manolo(bytes)
        Catch ex As Exception
            Console.WriteLine($"[binddiff] '{nifKey}' does not load as NIF: {ex.Message}")
            Return Nothing
        End Try

        Dim acc As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        Dim shapesSkinned = 0
        For blkIdx = 0 To nif.Blocks.Count - 1
            Dim shp = TryCast(nif.Blocks(blkIdx), NiflySharp.INiShape)
            If shp Is Nothing Then Continue For
            Dim rs As NifRenderableShape
            Try
                rs = New NifRenderableShape(nif, shp, blkIdx)
            Catch
                Continue For
            End Try
            If rs.ShapeBones Is Nothing OrElse rs.ShapeBones.Count = 0 Then Continue For
            Dim binds = rs.ShapeBoneTransforms
            If binds Is Nothing Then Continue For
            shapesSkinned += 1
            For k = 0 To Math.Min(rs.ShapeBones.Count, binds.Count) - 1
                Dim bn = rs.ShapeBones(k)?.Name?.String
                If String.IsNullOrEmpty(bn) Then Continue For
                Dim bind = binds(k)
                If bind Is Nothing Then Continue For
                If Not acc.ContainsKey(bn) Then acc(bn) = bind   ' primero gana
            Next
        Next
        Console.WriteLine($"[binddiff]   '{nifKey}': {shapesSkinned} skinned shape(s), {acc.Count} bone(s) with bind")
        Return acc
    End Function

    ''' <summary>DIAGNÓSTICO (--binddiff): compara los binds skin→bone (SkinToBone) de cada hueso <c>*_skin</c>
    ''' entre dos NIFs (underarmor vs body). Objetivo: determinar si la escala del SCLP está codificada en el
    ''' bind del NIF del underarmor. Si los binds difieren (más allá de ruido), la escala vive en el NIF; si son
    ''' idénticos, no (está horneada en los vértices).
    ''' <para>Spec: <c>--binddiff "&lt;uaNifKey|path&gt;|&lt;bodyNifKey|path&gt;|&lt;boneSubstr opcional&gt;"</c>.
    ''' El 3er campo filtra huesos por substring case-insensitive; si falta, se usan todos los <c>*_skin</c>.</para></summary>
    Private Sub BindDiffRun(spec As String)
        Const EPS As Double = 0.0001   ' umbral de significancia (ruido por debajo)
        Dim inv = System.Globalization.CultureInfo.InvariantCulture

        Dim parts = spec.Split("|"c)
        If parts.Length < 2 OrElse parts(0).Trim() = "" OrElse parts(1).Trim() = "" Then
            Console.WriteLine("[binddiff] usage: --binddiff ""<uaNifKey|path>|<bodyNifKey|path>|<boneSubstr optional>""")
            Return
        End If
        Dim uaKey = parts(0).Trim()
        Dim bodyKey = parts(1).Trim()
        Dim boneSubstr = If(parts.Length >= 3, parts(2).Trim(), "")

        Console.WriteLine("[binddiff] COMPARES the skin→bone (SkinToBone) binds of each bone between two NIFs")
        Console.WriteLine($"   underarmor(ua) = {uaKey}")
        Console.WriteLine($"   body           = {bodyKey}")
        Console.WriteLine($"   bone filter    = {If(boneSubstr = "", "(all *_skin)", boneSubstr)}   EPS = {EPS.ToString(inv)}")

        Dim uaBinds = LoadBindsByBone(uaKey)
        If uaBinds Is Nothing Then Return
        Dim bodyBinds = LoadBindsByBone(bodyKey)
        If bodyBinds Is Nothing Then Return

        ' Huesos candidatos: matchean substr (o *_skin si vacío) en el underarmor Y presentes en el body.
        Dim cand = uaBinds.Keys.
            Where(Function(k)
                      If boneSubstr = "" Then
                          Return k.EndsWith("_skin", StringComparison.OrdinalIgnoreCase)
                      End If
                      Return k.IndexOf(boneSubstr, StringComparison.OrdinalIgnoreCase) >= 0
                  End Function).
            Where(Function(k) bodyBinds.ContainsKey(k)).
            OrderBy(Function(k) k, StringComparer.OrdinalIgnoreCase).ToList()

        Console.WriteLine("")
        Console.WriteLine($"   {cand.Count} comparable bone(s) (present in BOTH NIFs)")
        Console.WriteLine("")

        Dim maxDT As Double = 0.0
        Dim maxDScale As Double = 0.0
        Dim maxDCol As Double = 0.0
        Dim divergBones As New List(Of String)()

        For Each bn In cand
            Dim ua = uaBinds(bn)
            Dim bd = bodyBinds(bn)
            Dim ucn = BindColNorms(ua)
            Dim bcn = BindColNorms(bd)

            Console.WriteLine("   " & New String("="c, 108))
            Console.WriteLine($"   {bn}")
            Console.WriteLine(String.Format(inv,
                "     ua  : T=({0:F4},{1:F4},{2:F4})  scaleUniform={3:F4}  scaleVec=({4:F4},{5:F4},{6:F4})  colNorms=({7:F4},{8:F4},{9:F4})",
                ua.Translation.X, ua.Translation.Y, ua.Translation.Z, ua.Scale,
                ua.ScaleVector.X, ua.ScaleVector.Y, ua.ScaleVector.Z, ucn.n0, ucn.n1, ucn.n2))
            Console.WriteLine(String.Format(inv,
                "     body: T=({0:F4},{1:F4},{2:F4})  scaleUniform={3:F4}  scaleVec=({4:F4},{5:F4},{6:F4})  colNorms=({7:F4},{8:F4},{9:F4})",
                bd.Translation.X, bd.Translation.Y, bd.Translation.Z, bd.Scale,
                bd.ScaleVector.X, bd.ScaleVector.Y, bd.ScaleVector.Z, bcn.n0, bcn.n1, bcn.n2))

            Dim dtx = CDbl(ua.Translation.X) - CDbl(bd.Translation.X)
            Dim dty = CDbl(ua.Translation.Y) - CDbl(bd.Translation.Y)
            Dim dtz = CDbl(ua.Translation.Z) - CDbl(bd.Translation.Z)
            Dim dt = Math.Sqrt(dtx * dtx + dty * dty + dtz * dtz)
            Dim dscale = Math.Abs(CDbl(ua.Scale) - CDbl(bd.Scale))
            Dim d0 = Math.Abs(ucn.n0 - bcn.n0)
            Dim d1 = Math.Abs(ucn.n1 - bcn.n1)
            Dim d2 = Math.Abs(ucn.n2 - bcn.n2)
            Console.WriteLine(String.Format(inv,
                "     DELTA: |ΔT|={0:F4}  Δscale={1:F4}  Δcolnorms=({2:F4},{3:F4},{4:F4})", dt, dscale, d0, d1, d2))

            maxDT = Math.Max(maxDT, dt)
            maxDScale = Math.Max(maxDScale, dscale)
            Dim dcolMax = Math.Max(d0, Math.Max(d1, d2))
            maxDCol = Math.Max(maxDCol, dcolMax)
            If dt > EPS OrElse dscale > EPS OrElse dcolMax > EPS Then divergBones.Add(bn)
        Next

        Console.WriteLine("")
        Console.WriteLine("   " & New String("="c, 108))
        Console.WriteLine($"   SUMMARY ({cand.Count} *_skin bone(s) compared):")
        Console.WriteLine($"     MAX |ΔT|       = {maxDT.ToString("F6", inv)}")
        Console.WriteLine($"     MAX |Δscale|   = {maxDScale.ToString("F6", inv)}")
        Console.WriteLine($"     MAX |Δcolnorm| = {maxDCol.ToString("F6", inv)}")
        If maxDT < EPS AndAlso maxDScale < EPS AndAlso maxDCol < EPS Then
            Console.WriteLine("     ⇒ BINDS IDENTICAL ua≈body → the scale is NOT in the bind (baked into vertices)")
        Else
            Console.WriteLine($"     ⇒ BINDS DIFFER → possible scale encoded in the bind; bones: {String.Join(", ", divergBones)}")
        End If
    End Sub

    ''' <summary>VOLCADO DIAGNÓSTICO de la geometría cruda por hueso, para analizar A MANO qué fórmula recupera
    ''' el SCLP autorado. Para el underarmor y un body de referencia recolecta, por hueso y eje (X/Y/Z), los
    ''' estadísticos del <c>domSet</c> (min/max/mean/p05/p95/maxAbsOrigin/halfRange) y del <c>allSet</c>
    ''' ponderado (wMean/wRmsOrigin/p95abs), imprime ambos NIFs lado a lado y una fila de RATIOS ua/body para
    ''' cada candidato de fórmula, con el valor <c>authored</c> del <c>.sclp</c> al lado.
    ''' <para>Spec: <c>--sclpdiag "&lt;uaNifKey&gt;|&lt;bodyNifKey&gt;|&lt;boneSubstr&gt;"</c>. El 3er campo filtra
    ''' huesos por substring case-insensitive; si falta, se usan todos los <c>*_skin</c>.</para></summary>
    Private Sub SclpDiagRun(spec As String, Optional shapeFilter As String = "")
        Const WT As Single = 0.1F     ' umbral de peso para el allSet
        Dim inv = System.Globalization.CultureInfo.InvariantCulture
        Dim axisName = New String() {"X", "Y", "Z"}

        Dim parts = spec.Split("|"c)
        If parts.Length < 2 OrElse parts(0).Trim() = "" OrElse parts(1).Trim() = "" Then
            Console.WriteLine("[sclpdiag] usage: --sclpdiag ""<uaNifKey>|<bodyNifKey>|<boneSubstr>""")
            Return
        End If
        Dim uaKey = parts(0).Trim()
        Dim bodyKey = parts(1).Trim()
        Dim boneSubstr = If(parts.Length >= 3, parts(2).Trim(), "")
        Dim sclpKey = Path.ChangeExtension(uaKey, ".sclp")

        Console.WriteLine("[sclpdiag] DIAGNOSTIC DUMP of raw per-bone geometry (to derive the SCLP formula by hand)")
        Console.WriteLine($"   underarmor = {uaKey}")
        Console.WriteLine($"   body(ref)  = {bodyKey}")
        Console.WriteLine($"   sclp(auth) = {sclpKey}")
        Console.WriteLine($"   bone filter = {If(boneSubstr = "", "(all *_skin)", boneSubstr)}   wThreshold(allSet) = {WT.ToString(inv)}")
        If shapeFilter <> "" Then Console.WriteLine($"   shapefilter = {shapeFilter}   (only ua shapes whose name contains it; the body is NOT filtered)")

        Dim uaLoc = AccumulateBoneLocals(uaKey, WT, shapeFilter)
        If uaLoc Is Nothing Then Return
        Dim bodyLoc = AccumulateBoneLocals(bodyKey, WT)
        If bodyLoc Is Nothing Then Return

        ' estNN nearest-neighbor LSQ por hueso (sx/sy/sz = pendiente por origen + residual de reconstrucción).
        ' estFull = ajuste de matriz 3×3 completa por hueso (frame-aware), sobre los MISMOS pares NN.
        Dim nnPairs = BuildNNPairs(uaKey, bodyKey, 0.01F, shapeFilter)
        Dim nn = AccumulateNNScales(nnPairs)
        If nn Is Nothing Then nn = New Dictionary(Of String, (sx As Double, sy As Double, sz As Double, nPairs As Integer, okX As Boolean, okY As Boolean, okZ As Boolean, residX As Double, residY As Double, residZ As Double))(StringComparer.OrdinalIgnoreCase)
        Dim full = AccumulateFullFit(nnPairs)
        If full Is Nothing Then full = New Dictionary(Of String, (Lxx As Double, Lyy As Double, Lzz As Double, offNorm As Double, residFull As Double, ok As Boolean))(StringComparer.OrdinalIgnoreCase)

        ' estGlobal = solve GLOBAL CONJUNTO por mínimos cuadrados de TODAS las escalas de hueso a la vez.
        Dim glob = EstimateGlobalScales(uaKey, bodyKey, 0.01F, shapeFilter)
        If glob Is Nothing Then glob = New Dictionary(Of String, (sx As Double, sy As Double, sz As Double))(StringComparer.OrdinalIgnoreCase)

        Dim authored = LoadSclpAbsolute(sclpKey)
        If authored Is Nothing Then
            Console.WriteLine($"   (warning) .sclp '{sclpKey}' not found/parseable — continuing without authored column")
        Else
            Console.WriteLine($"   .sclp: {authored.Count} authored entry(ies)")
        End If

        ' Huesos candidatos: matchean substr (o *_skin si vacío) en el underarmor.
        Dim cand = uaLoc.Keys.
            Where(Function(k)
                      If boneSubstr = "" Then
                          Return k.EndsWith("_skin", StringComparison.OrdinalIgnoreCase)
                      End If
                      Return k.IndexOf(boneSubstr, StringComparison.OrdinalIgnoreCase) >= 0
                  End Function).
            OrderBy(Function(k) k, StringComparer.OrdinalIgnoreCase).ToList()

        Console.WriteLine("")
        Console.WriteLine($"   {cand.Count} candidate bone(s) in the underarmor")
        Console.WriteLine("   Objective: for each bone/axis, see which r_* ≈ authored.")
        Console.WriteLine("")

        Dim printed = 0
        For Each bn In cand
            Dim uaBl = uaLoc(bn)
            Dim bdBl As BoneLocals = Nothing
            If Not bodyLoc.TryGetValue(bn, bdBl) Then
                Console.WriteLine($"   [skip] '{bn}': no data in the reference body")
                Continue For
            End If

            Dim auRow As Single() = Nothing
            Dim hasAu = authored IsNot Nothing AndAlso authored.TryGetValue(bn, auRow)

            Console.WriteLine("   " & New String("="c, 120))
            Dim auHdr = "-"
            If hasAu Then auHdr = $"X={auRow(0).ToString("F4", inv)}  Y={auRow(1).ToString("F4", inv)}  Z={auRow(2).ToString("F4", inv)}"
            Console.WriteLine($"   {bn}    authored[{auHdr}]")
            Console.WriteLine($"     ua : nAll={uaBl.NAll,6}  nDom={uaBl.NDom,6}      body: nAll={bdBl.NAll,6}  nDom={bdBl.NDom,6}")

            ' estNN (nearest-neighbor LSQ, sin umbral de dominancia): pendiente por origen sx/sy/sz + residual de reconstrucción.
            Dim nnRow As (sx As Double, sy As Double, sz As Double, nPairs As Integer, okX As Boolean, okY As Boolean, okZ As Boolean, residX As Double, residY As Double, residZ As Double) = Nothing
            If nn.TryGetValue(bn, nnRow) Then
                Dim sxT = If(nnRow.okX, nnRow.sx.ToString("F4", inv), "n/a")
                Dim syT = If(nnRow.okY, nnRow.sy.ToString("F4", inv), "n/a")
                Dim szT = If(nnRow.okZ, nnRow.sz.ToString("F4", inv), "n/a")
                Dim rxT = If(nnRow.okX, nnRow.residX.ToString("F4", inv), "n/a")
                Dim ryT = If(nnRow.okY, nnRow.residY.ToString("F4", inv), "n/a")
                Dim rzT = If(nnRow.okZ, nnRow.residZ.ToString("F4", inv), "n/a")
                Console.WriteLine($"     estNN: sx={sxT}  sy={syT}  sz={szT}   nPairs={nnRow.nPairs}   resid[X={rxT} Y={ryT} Z={rzT}]  (0=perfect per-bone scale)")
            Else
                Console.WriteLine("     estNN: (no NN pair for this bone — only in one NIF)")
            End If

            ' estFull (ajuste de matriz 3×3 completa): diag(L) = escala per-eje frame-aware; offNorm = cuán no-diagonal; resid = residual del ajuste 3×3.
            Dim fullRow As (Lxx As Double, Lyy As Double, Lzz As Double, offNorm As Double, residFull As Double, ok As Boolean) = Nothing
            If full.TryGetValue(bn, fullRow) AndAlso fullRow.ok Then
                Console.WriteLine($"     estFull: Lxx={fullRow.Lxx.ToString("F4", inv)}  Lyy={fullRow.Lyy.ToString("F4", inv)}  Lzz={fullRow.Lzz.ToString("F4", inv)}  offNorm={fullRow.offNorm.ToString("F4", inv)}  resid={fullRow.residFull.ToString("F4", inv)}  (offNorm 0=pure axis scale; resid ≤ estNN)")
            Else
                Console.WriteLine("     estFull: (no 3×3 fit — P singular or no NN pair)")
            End If

            ' estGlobal (solve conjunto least-squares global de TODAS las escalas a la vez).
            Dim globRow As (sx As Double, sy As Double, sz As Double) = Nothing
            If glob.TryGetValue(bn, globRow) Then
                Console.WriteLine($"     estGlobal: sx={globRow.sx.ToString("F4", inv)}  sy={globRow.sy.ToString("F4", inv)}  sz={globRow.sz.ToString("F4", inv)}  (global least-squares joint solve)")
            Else
                Console.WriteLine("     estGlobal: (no solution in the joint solve)")
            End If

            ' Encabezado de la tabla por eje.
            Dim hdr = "     {0,-2} {1,-4} | {2,9} {3,9} {4,9} {5,9} {6,9} {7,9} {8,9} | {9,9} {10,9} {11,9}"
            Console.WriteLine(String.Format(hdr,
                                            "ax", "src", "min", "max", "mean", "p05", "p95", "maxAbsO", "halfRng", "wMean", "wRmsO", "p95abs"))
            Console.WriteLine("     " & New String("-"c, 118))

            For a = 0 To 2
                Dim su = ComputeAxisStats(uaBl, a)
                Dim sb = ComputeAxisStats(bdBl, a)
                ' Fila ua
                Console.WriteLine(String.Format(hdr,
                                                axisName(a), "ua",
                                                FmtStat(su.DMin, inv), FmtStat(su.DMax, inv), FmtStat(su.DMean, inv),
                                                FmtStat(su.DP05, inv), FmtStat(su.DP95, inv), FmtStat(su.DMaxAbsO, inv), FmtStat(su.DHalfRange, inv),
                                                FmtStat(su.WMean, inv), FmtStat(su.WRmsO, inv), FmtStat(su.AP95Abs, inv)))
                ' Fila body
                Console.WriteLine(String.Format(hdr,
                                                "", "body",
                                                FmtStat(sb.DMin, inv), FmtStat(sb.DMax, inv), FmtStat(sb.DMean, inv),
                                                FmtStat(sb.DP05, inv), FmtStat(sb.DP95, inv), FmtStat(sb.DMaxAbsO, inv), FmtStat(sb.DHalfRange, inv),
                                                FmtStat(sb.WMean, inv), FmtStat(sb.WRmsO, inv), FmtStat(sb.AP95Abs, inv)))
                ' Fila de RATIOS ua/body + authored
                Dim rHalf = RatioStat(su.DHalfRange, sb.DHalfRange)
                Dim rMaxAbs = RatioStat(su.DMaxAbsO, sb.DMaxAbsO)
                Dim rP95abs = RatioStat(su.AP95Abs, sb.AP95Abs)
                Dim rWrms = RatioStat(su.WRmsO, sb.WRmsO)
                Dim uaSpan = If(Single.IsNaN(su.DP95) OrElse Single.IsNaN(su.DP05), Single.NaN, su.DP95 - su.DP05)
                Dim bdSpan = If(Single.IsNaN(sb.DP95) OrElse Single.IsNaN(sb.DP05), Single.NaN, sb.DP95 - sb.DP05)
                Dim rSpan = RatioStat(uaSpan, bdSpan)
                Dim auTxt = If(hasAu, auRow(a).ToString("F4", inv), "-")
                Console.WriteLine($"     {axisName(a),-2} RATIO| r_halfRange={FmtStat(rHalf, inv)}  r_maxAbsOrigin={FmtStat(rMaxAbs, inv)}  r_p95abs={FmtStat(rP95abs, inv)}  r_wRmsOrigin={FmtStat(rWrms, inv)}  r_span={FmtStat(rSpan, inv)}  |  authored={auTxt}")
            Next
            printed += 1
        Next

        Console.WriteLine("   " & New String("="c, 120))
        Console.WriteLine("")
        Console.WriteLine($"   {printed} bone(s) dumped with data in both NIFs.")
        Console.WriteLine("   LEGEND: domSet=dominant slot>0.5 (clean geom); allSet=weight>threshold (weighted).")
        Console.WriteLine("            maxAbsO=p95(|axis|) at ORIGIN (domSet); p95abs=p95(|axis|) at ORIGIN (allSet); wRmsO=sqrt(Σw·axis²/Σw).")
        Console.WriteLine("            r_span=(p95−p05)_ua/body. Look for the r_* that matches each axis's 'authored'.")
    End Sub

    ''' <summary>Modo BATCH del estimador SCLP: evalúa muchas combinaciones en UNA sola corrida (un solo mount ya hecho).
    ''' Manifiesto = texto, una línea por caso: <c>label|uaKeyOrPath|bodyKeyOrPath|authoredSclpPath</c> (líneas vacías o
    ''' que empiezan con '#' se ignoran). Por caso: construye los pares NN (<see cref="BuildNNPairs"/>, wEps=0.01) y el
    ''' estNN (<see cref="AccumulateNNScales"/>), carga el SCLP autorado (<see cref="LoadSclpAbsolute"/>) y, para cada
    ''' hueso *_skin en ambos, en ejes Y y Z 'movidos' (|authored−1|&gt;0.02), acumula err=|estNN_eje − authored_eje|.
    ''' Imprime UNA línea por caso: <c>label  n=…  medErr=…  meanErr=…  within0.10=…  within0.05=…</c> (InvariantCulture).
    ''' Cada línea va en su propio Try/Catch: un caso que falle NO aborta el batch.</summary>
    Private Sub SclpBatchRun(manifestPath As String)
        Const WEPS As Single = 0.01F   ' MISMO wEps que estNN (estimatesclp/sclpdiag): solo excluye pesos numéricamente nulos
        Const MOVED As Single = 0.02F  ' |authored−1| > MOVED ⇒ el eje fue escalado (contra ruido ≈1.0)
        Dim inv = System.Globalization.CultureInfo.InvariantCulture

        If Not IO.File.Exists(manifestPath) Then
            Console.WriteLine($"[sclpbatch] manifest not found: {manifestPath}")
            Return
        End If

        Dim lines As String()
        Try
            lines = IO.File.ReadAllLines(manifestPath)
        Catch ex As Exception
            Console.WriteLine($"[sclpbatch] could not read manifest '{manifestPath}': {ex.Message}")
            Return
        End Try

        Console.WriteLine($"[sclpbatch] {manifestPath}  ({lines.Length} line(s))")
        Console.WriteLine("   format: label|uaKeyOrPath|bodyKeyOrPath|authoredSclpPath   (axis 'moved' if |authored−1|>" & MOVED.ToString(inv) & "; err=|estNN−authored| in Y,Z)")
        Console.WriteLine("")

        Dim nCases = 0
        For Each raw In lines
            Dim line = If(raw, "").Trim()
            If line = "" OrElse line.StartsWith("#") Then Continue For
            nCases += 1

            Dim label = line   ' fallback para mensajes de error antes de parsear el label
            Try
                Dim parts = line.Split("|"c)
                If parts.Length < 4 Then
                    Console.WriteLine($"{label,-24}  ERROR/skip format (expected 4 fields 'label|ua|body|sclp' separated by '|')")
                    Continue For
                End If
                label = parts(0).Trim()
                Dim uaKey = parts(1).Trim()
                Dim bodyKey = parts(2).Trim()
                Dim sclpKey = parts(3).Trim()
                If label = "" Then label = "(no label)"
                If uaKey = "" OrElse bodyKey = "" OrElse sclpKey = "" Then
                    Console.WriteLine($"{label,-24}  ERROR/skip empty fields (ua/body/sclp)")
                    Continue For
                End If

                ' Pipeline NN EXACTO de estimatesclp/sclpdiag (mismo wEps, misma métrica estNN).
                Dim nnPairs = BuildNNPairs(uaKey, bodyKey, WEPS)   ' Nothing si ua o body no cargan
                Dim nn = AccumulateNNScales(nnPairs)               ' Nothing si nnPairs Nothing
                If nn Is Nothing OrElse nn.Count = 0 Then
                    Console.WriteLine($"{label,-24}  ERROR/skip ua or body do not load (or no NN pairs)")
                    Continue For
                End If

                Dim authored = LoadSclpAbsolute(sclpKey)
                If authored Is Nothing OrElse authored.Count = 0 Then
                    Console.WriteLine($"{label,-24}  ERROR/skip .sclp not found/parseable: {sclpKey}")
                    Continue For
                End If

                ' Por hueso *_skin en AMBOS (estNN y authored), ejes Y y Z movidos → err=|estNN−authored|.
                Dim errs As New List(Of Double)()
                For Each kv In nn
                    Dim bn = kv.Key
                    If Not bn.EndsWith("_skin", StringComparison.OrdinalIgnoreCase) Then Continue For
                    Dim au As Single() = Nothing
                    If Not authored.TryGetValue(bn, au) OrElse au Is Nothing OrElse au.Length < 3 Then Continue For
                    Dim r = kv.Value
                    ' Eje Y (índice 1)
                    If r.okY AndAlso Math.Abs(au(1) - 1.0F) > MOVED Then errs.Add(Math.Abs(r.sy - au(1)))
                    ' Eje Z (índice 2)
                    If r.okZ AndAlso Math.Abs(au(2) - 1.0F) > MOVED Then errs.Add(Math.Abs(r.sz - au(2)))
                Next

                If errs.Count = 0 Then
                    Console.WriteLine($"{label,-24}  ERROR/skip no comparable moved Y/Z axes (valid estNN + authored)")
                    Continue For
                End If

                errs.Sort()
                Dim c = errs.Count
                Dim med As Double
                If c Mod 2 = 1 Then
                    med = errs((c - 1) \ 2)
                Else
                    med = (errs(c \ 2 - 1) + errs(c \ 2)) / 2.0
                End If
                Dim mean = errs.Average()
                Dim w10 = errs.Where(Function(e) e <= 0.1).Count()
                Dim w05 = errs.Where(Function(e) e <= 0.05).Count()

                Console.WriteLine($"{label,-24}  n={c,3}  medErr={med.ToString("F4", inv)}  meanErr={mean.ToString("F4", inv)}  within0.10={w10,3}  within0.05={w05,3}")
            Catch ex As Exception
                Console.WriteLine($"{label,-24}  ERROR {ex.Message}")
            End Try
        Next

        Console.WriteLine("")
        Console.WriteLine($"[sclpbatch] {nCases} case(s) processed.")
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
                    Console.WriteLine($"[blendhintscan] mounted: {p}")
                    substr = ""
                Catch ex As Exception
                    Console.WriteLine($"[blendhintscan] could NOT mount '{p}': {ex.Message}")
                End Try
            End If
        Next
        Dim keys = FilesDictionary_class.Dictionary.Keys.
                       Where(Function(k) k.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) AndAlso
                                         (substr = "" OrElse k.IndexOf(substr, StringComparison.OrdinalIgnoreCase) >= 0)).
                       OrderBy(Function(k) k, StringComparer.OrdinalIgnoreCase).ToList()
        Console.WriteLine($"[blendhintscan] {keys.Count} .hkx (filter='{filter}') — parsing bindings...")
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
        Console.WriteLine($"[blendhintscan] files parseOk={fileOk} parseErr={parseErr} noBinding={noBinding}")
        Console.WriteLine("[blendhintscan] === blendHint distribution (per binding) ===")
        For Each kv In tally.OrderBy(Function(x) x.Key)
            Dim label = If(kv.Key = 0, "NORMAL", If(kv.Key = 1, "ADDITIVE_DEPRECATED", If(kv.Key = 2, "ADDITIVE", "⚠ RARE ∉{0,1,2}")))
            Console.WriteLine($"   blendHint={kv.Key,-4} {label,-22} = {kv.Value} binding(s)")
        Next
        For Each kv In examples.OrderBy(Function(x) x.Key)
            Console.WriteLine($"[blendhintscan] --- examples blendHint={kv.Key} ({If(kv.Key = 1, "ADDITIVE_DEPRECATED", If(kv.Key = 2, "ADDITIVE", "RARE"))}) ---")
            For Each ex In kv.Value : Console.WriteLine($"      {ex}") : Next
        Next
        Dim raros = tally.Keys.Where(Function(h) h <> 0 AndAlso h <> 1 AndAlso h <> 2).ToList()
        Console.WriteLine($"[blendhintscan] ⇒ values ∉{{0,1,2}} = {raros.Count} ({If(raros.Count = 0, "NONE ⇒ app ≠0 and engine {1,2} agree on ALL content", String.Join(",", raros))})")
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
        If parts.Length < 3 Then Console.WriteLine("[animsync] usage: --animsynccheck ""<chunkNif>|<rigHkx>|<clipHkx>[|frame][|boneFilter]""") : Return
        Dim chunkPath = parts(0).Trim(), rigPath = parts(1).Trim(), clipPath = parts(2).Trim()
        Dim frameArg = If(parts.Length > 3 AndAlso parts(3).Trim() <> "", parts(3).Trim(), "mid")
        Dim boneFilter = If(parts.Length > 4, parts(4).Trim(), "")

        ' ── CHUNK NIF: name → bindLocal, bindWorld, parent, flags ──
        Dim nbx = LoadAnimCand(chunkPath)
        If nbx Is Nothing Then Console.WriteLine($"[animsync] chunk '{chunkPath}' does not load") : Return
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
        If rb Is Nothing Then Console.WriteLine($"[animsync] rig '{rigPath}' does not load") : Return
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(rb))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.ReferencePose IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        If skel Is Nothing Then Console.WriteLine("[animsync] rig without hkaSkeleton") : Return
        Dim nBk = skel.Bones.Count
        Dim cbts = LoadAnimCand(clipPath)
        If cbts Is Nothing Then Console.WriteLine($"[animsync] clip '{clipPath}' does not load") : Return
        Dim cg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(cbts))
        Dim anim = cg.ParseAnimations().FirstOrDefault()
        If anim Is Nothing Then anim = cg.ParseLosslessAnimations().FirstOrDefault()
        If anim Is Nothing OrElse anim.NumFrames <= 0 Then Console.WriteLine("[animsync] clip without readable animation") : Return
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
        Console.WriteLine($"[animsync] control FK vs GetGlobalTransform: maxErr={maxBindErr:F4} (should be ~0)")

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
            Dim mkTxt = If(animated, $"T:{If(mk.Tx, "x", "-")}{If(mk.Ty, "y", "-")}{If(mk.Tz, "z", "-")} R:{If(mk.R, "a", "-")} S:{If(mk.S, "a", "-")}", "(not in clip)")
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
        Console.WriteLine($"[animsync] MAX TEAR (BUGGY vs HONORED) = {maxTear:F3}u in '{maxTearBone}'  |  bone-length honored preserved = {(Not anyLenBreak)}")
        Console.WriteLine($"[animsync] ⇒ honoring No Anim Sync, the clip's translation/scale is discarded on flagged bones: rigid arm (bone-len=bind) ⇒ CONNECTED. Without honoring (app today): tear={maxTear:F3}u = the tear.")
        Console.WriteLine($"[animsync] T-pose: no clip ⇒ localFn=bindLocal in both ⇒ world=bindWorld (control maxErr={maxBindErr:F4}) ⇒ the fix does NOT alter the T-pose.")
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
        If parts.Length < 2 Then Console.WriteLine("[clipbase] usage: --clipbase ""<rigHkx>|<clipHkx>[|boneFilter[|chunkNif;chunkNif...]]""") : Return
        Dim rigPath = parts(0), clipPath = parts(1)
        Dim boneFilter = If(parts.Length > 2, parts(2), "")
        Dim chunkPaths = If(parts.Length > 3 AndAlso parts(3) <> "",
                            parts(3).Split(";"c).Where(Function(p) p.Trim() <> "").Select(Function(p) p.Trim()).ToList(),
                            New List(Of String))

        ' ── RIG: skeleton de animación (no-Ragdoll) → locals (refPose) + parent names ──
        Dim rb = LoadAnimCand(rigPath)
        If rb Is Nothing Then Console.WriteLine($"[clipbase] rig '{rigPath}' does not load") : Return
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(rb))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.ParentIndices IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        If skel Is Nothing Then Console.WriteLine("[clipbase] rig without animation hkaSkeleton") : Return
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
        If cbts Is Nothing Then Console.WriteLine($"[clipbase] clip '{clipPath}' does not load") : Return
        Dim cg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(cbts))
        Dim anim = cg.ParseAnimations().FirstOrDefault()
        If anim Is Nothing Then anim = cg.ParseLosslessAnimations().FirstOrDefault()
        If anim Is Nothing OrElse anim.NumFrames <= 0 Then Console.WriteLine("[clipbase] clip without readable animation") : Return
        Dim embSkels = cg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) cg.ParseSkeleton(o)).
                          Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.Bones.Count > 0).ToList()
        Dim idxArr = If(anim.Binding?.TransformTrackToBoneIndices, New List(Of Short)())
        Console.WriteLine($"[clipbase] rig='{rigPath}' skel='{skel.Name}' bones={nB}")
        Console.WriteLine($"[clipbase] clip='{clipPath}' frames={anim.NumFrames} tracks={anim.NumTransformTracks} bindingTracks={idxArr.Count} blendHint={If(anim.Binding Is Nothing, "?", anim.Binding.BlendHint.ToString())} origSkel='{If(anim.Binding?.OriginalSkeletonName, "")}' embeddedSkeletons={embSkels.Count}{If(embSkels.Count > 0, " (" & String.Join(",", embSkels.Select(Function(s) $"'{s.Name}'×{s.Bones.Count}")) & ")", "")}")

        ' ── CHUNKS: por chunk NIF, world ensamblado por bone vía skin binds (inv(bind)) + node tree ──
        Dim chunkData As New List(Of (Name As String, SkinW As Dictionary(Of String, Transform_Class), NodeW As Dictionary(Of String, Transform_Class)))
        For Each cp In chunkPaths
            Dim nbx = LoadAnimCand(cp)
            If nbx Is Nothing Then Console.WriteLine($"[clipbase] chunk '{cp}' does not load — SKIP") : Continue For
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
                        If dPrev > 0.1F Then Console.WriteLine($"[clipbase] ⚠ bind CONFLICT '{nm}' in '{cp}': dT={dPrev:F3} (keeping the first)")
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
        Console.WriteLine("   track bone                       mask        rig.T                clip0.T              dT_rig θ_rig | asm.T (src)            dT_asm θ_asm | verdict")
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
                If Not nearRig Then cntNeither += 1 : neitherList.Add($"{nm} (dT_rig={dTr:F2} θ={thr:F1}, no asm)") Else cntRig += 1
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
        Console.WriteLine($"[clipbase] SUMMARY: CLIP≈RIG={cntRig}  CLIP≈ASM={cntAsm}  RIG==ASM={cntBoth}  NEITHER={cntNeither}  (noAsmAvailable={cntNoAsm}; thresholds dT<{T_EPS} θ<{R_EPS}°)")
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
        If nbx Is Nothing OrElse hbx Is Nothing Then Console.WriteLine($"[chunkcompare] missing file (nif={nbx IsNot Nothing}, hkx={hbx IsNot Nothing})") : Return
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
        If skel Is Nothing Then Console.WriteLine("[chunkcompare] CreateABot without anim skeleton") : Return
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
        If sharedBones.Count = 0 Then Console.WriteLine("[chunkcompare] no bones shared with CreateABot") : Return
        Dim refBone = sharedBones(0)
        Console.WriteLine($"[chunkcompare] {chunkNifPath}")
        Console.WriteLine($"   shared with CreateABot = {sharedBones.Count} | anchor (root-most) = '{refBone}'")
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
        Console.WriteLine($"[chunkcompare] maxDT={maxDT:F2} maxDR={maxDR:F2} ⇒ {If(match, "LAYOUT == CreateABot (the assembly offset comes from the CHAIN/host-P-X, NOT the chunk → re-bind to the HKX possible)", "LAYOUT DIFFERS from CreateABot (chunk authored differently → does not rigidly fit the HKX)")}")
    End Sub

    ''' <summary>Vuelca de un HKX: clip generators (Name + AnimationName) + characters (animationNames +
    ''' behaviorFilename + rigName). Para ver el linking DIRECTO clip↔anim sin heurísticas de path.</summary>
    Private Sub DumpBehaviorClips(path As String)
        Dim b = LoadAnimCand(path)
        If b Is Nothing Then Console.WriteLine($"[dumpbeh] '{path}' does not load") : Return
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
        Console.WriteLine("   ── graph classes ──")
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
        Console.WriteLine("   ── clip generator referencers (parent → clip) ──")
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
        Console.WriteLine($"[hkxcoverage] total .hkx/.hkt in load order (canon dedup) = {allFiles.Count}")

        ' (2) Conjunto REFERENCIADO: caminar todas las razas con behavior.
        Dim referenced As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim behVisited As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim loader As Func(Of String, Byte()) = Function(p) FO4_NPC_Manager.MeshPathHelpers.TryLoadMeshBytes(p, 0)
        Dim races = pm.GetRecordsOfType("RACE")
        Dim nRaces = 0
        For Each rec In races
            Dim race As Canon.IRace = Nothing
            Try : race = Canon.CanonRecords.Race(rec, pm) : Catch : Continue For : End Try
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
                CollectBehaviorRefs(sg.DataBehaviourGraph, referenced, behVisited, 0)
            Next
            Try
                For Each c In BehaviorClipEnumerator.EnumerateClips(rb, loader)
                    AddRefC(referenced, c.AnimationFile)
                Next
            Catch
            End Try
        Next
        Console.WriteLine($"[hkxcoverage] races with behavior walked = {nRaces} | referenced files = {referenced.Count}")

        ' (3) Reporte por categoría: referenciado vs NO-referenciado-por-raza.
        Dim allOrphans As New List(Of String)
        For Each cat In {"Animation", "Behavior", "Character", "Skeleton/Ragdoll", "Project", "Other"}
            Dim inCat = allFiles.Where(Function(kv) kv.Value = cat).Select(Function(kv) kv.Key).ToList()
            Dim orph = inCat.Where(Function(k) Not referenced.Contains(k)).ToList()
            allOrphans.AddRange(orph)
            Console.WriteLine($"  {cat,-16}: total={inCat.Count,5}  ref-by-race={inCat.Count - orph.Count,5}  NOT-ref-by-race={orph.Count,5}")
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
                      If p.StartsWith("actors\") Then Return "actors\… (actor body — REVIEW)"
                      Return "other"
                  End Function
        Console.WriteLine($"  --- NOT-referenced by RACE, by pattern (total {allOrphans.Count}) ---")
        For Each grp In allOrphans.GroupBy(Function(o) pat(o)).OrderByDescending(Function(g) g.Count())
            Console.WriteLine($"     {grp.Key,-34}: {grp.Count()}")
        Next
        Dim suspect = allOrphans.Where(Function(o) pat(o).StartsWith("actors\…")).OrderBy(Function(o) o).ToList()
        Console.WriteLine($"  --- SUSPECTS (actors\… orphan body = possible race gap): {suspect.Count} ---")
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

    ' KYWD TNAM Type enum.
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
        Console.WriteLine($"[kwtype] {kws.Count} KYWD records | filter edid='{substr}'")
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
        Console.WriteLine($"[kwtype] matched={shown}")
        For Each kv In byType.OrderByDescending(Function(x) x.Value)
            Console.WriteLine($"   type {kv.Key,-22}: {kv.Value}")
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
            Dim race As Canon.IRace = Nothing
            Try : race = Canon.CanonRecords.Race(rec, pm) : Catch : Continue For : End Try
            If race Is Nothing Then Continue For
            For Each kEntry In race.Keywords
                Dim k = kEntry.Keyword
                Dim tt As UInteger = 0 : kwType.TryGetValue(k, tt)
                If tt = 0UI AndAlso Not owner.ContainsKey(k) Then owner(k) = race.EditorID  ' None-typed ∧ en KWDA = identidad
            Next
        Next
        Console.WriteLine($"[statemap] KYWD={kwType.Count} | race-identities(None∧∈KWDA)={owner.Count} | filter='{edidFilter}'")

        Dim isIdentity = Function(k As UInteger) owner.ContainsKey(k)               ' identidad de alguna raza
        Dim isState = Function(k As UInteger) As Boolean                           ' eje de estado = tipo ≠ None
                          Dim tt As UInteger = 0 : kwType.TryGetValue(k, tt) : Return tt <> 0UI
                      End Function

        Dim gEntries = 0, gOld = 0, gNew = 0, gRecovered = 0, gExcluded = 0
        Dim gAxisEntries As New Dictionary(Of String, Integer)
        For Each rec In races
            Dim race As Canon.IRace = Nothing
            Try : race = Canon.CanonRecords.Race(rec, pm) : Catch : Continue For : End Try
            If race Is Nothing Then Continue For
            If race.MaleBehaviorGraphModelFileName = "" AndAlso race.FemaleBehaviorGraphModelFileName = "" Then Continue For
            If edidFilter <> "" AndAlso race.EditorID.IndexOf(edidFilter, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            Dim rb = RaceBehaviorResolver.ResolveRaceBehavior(race.FormID, pm)
            If rb Is Nothing Then Continue For
            Dim thisKw As New HashSet(Of UInteger)(race.Keywords.Select(Function(k) k.Keyword))
            Dim ownId = thisKw.Where(Function(k) isIdentity(k)).Select(Function(k) kwEdid.GetValueOrDefault(k, $"0x{k:X8}"))

            ' Clasificar cada entry.
            Dim byAxis As New Dictionary(Of String, List(Of Canon.RaceFO4_SubgraphData))(StringComparer.OrdinalIgnoreCase)
            Dim excluded As New List(Of (sd As Canon.RaceFO4_SubgraphData, needs As String))
            Dim nOld = 0, nNew = 0, nRec = 0
            For Each sd In rb.Subgraphs
                ' Regla VIEJA (heurística): aplica si SAKD vacío o ∩ KWDA ≠ ∅.
                Dim sdKw = sd.ActorKeywords.Select(Function(k) k.Keyword).ToList()
                Dim oldApply = sdKw.Count = 0 OrElse sdKw.Any(Function(k) thisKw.Contains(k))
                ' Regla NUEVA (type-driven): excluir solo si requiere identidad AJENA (None ∧ de otra raza).
                Dim foreignId = sdKw.FirstOrDefault(Function(k) isIdentity(k) AndAlso Not thisKw.Contains(k))
                Dim newApply = (foreignId = 0UI)
                If oldApply Then nOld += 1
                If newApply Then
                    nNew += 1
                    If Not oldApply Then nRec += 1   ' recuperado: lo aplicamos ahora y antes NO
                    ' Eje de estado = los tipos de las keywords de estado (TNAM ≠ None); si ninguna → Normal.
                    Dim stateTypes = sdKw.Where(Function(k) isState(k)).
                                       Select(Function(k) KwTypeName(kwType.GetValueOrDefault(k, 0UI))).Distinct().OrderBy(Function(s) s).ToList()
                    Dim axis = If(stateTypes.Count = 0, "Normal", String.Join("+", stateTypes))
                    If Not byAxis.ContainsKey(axis) Then byAxis(axis) = New List(Of Canon.RaceFO4_SubgraphData)
                    byAxis(axis).Add(sd)
                Else
                    excluded.Add((sd, kwEdid.GetValueOrDefault(foreignId, $"0x{foreignId:X8}") & "(None, from " & owner.GetValueOrDefault(foreignId, "?") & ")"))
                End If
            Next

            Console.WriteLine($"=== {race.EditorID} [0x{race.FormID:X8}] | own identity=[{String.Join(", ", ownId)}] ===")
            Console.WriteLine($"    subgraphs={rb.Subgraphs.Count} | OLD-applies={nOld} | NEW-applies={nNew} | RECOVERED(state)={nRec} | OUTSIDE(foreign identity)={excluded.Count}")
            For Each kv In byAxis.OrderBy(Function(x) If(x.Key = "Normal", "", x.Key))
                Dim saptSet = kv.Value.SelectMany(Function(s) s.AnimationPaths).Select(Function(p) LastTwoSeg(p.Path)).Distinct().Take(6)
                Dim tag = If(kv.Key <> "Normal" AndAlso kv.Value.Any(Function(s) Not (s.ActorKeywords.Count = 0 OrElse s.ActorKeywords.Any(Function(k) thisKw.Contains(k.Keyword)))), "  <<< RECOVERED", "")
                Console.WriteLine($"      [{kv.Key,-26}] x{kv.Value.Count,-3} roles={String.Join(",", kv.Value.Select(Function(s) RoleName(CInt(s.FlagsRole))).Distinct())}  SAPT≈[{String.Join(" ; ", saptSet)}]{tag}")
            Next
            If excluded.Count > 0 Then
                Console.WriteLine($"    OUTSIDE by FOREIGN identity:")
                For Each e In excluded.GroupBy(Function(x) System.IO.Path.GetFileName(x.sd.DataBehaviourGraph) & " ⟵ " & x.needs).OrderByDescending(Function(g) g.Count()).Take(10)
                    Console.WriteLine($"       x{e.Count(),-3} {e.Key}")
                Next
            End If

            gEntries += rb.Subgraphs.Count : gOld += nOld : gNew += nNew : gRecovered += nRec : gExcluded += excluded.Count
            For Each kv In byAxis : gAxisEntries(kv.Key) = gAxisEntries.GetValueOrDefault(kv.Key, 0) + kv.Value.Count : Next
        Next
        Console.WriteLine($"[statemap-TOTAL] entries={gEntries} | OLD-applies={gOld} | NEW-applies={gNew} | RECOVERED(state-gated)={gRecovered} | OUTSIDE(foreign identity)={gExcluded}")
        Console.WriteLine($"[statemap-TOTAL] entries NEW-applied by STATE AXIS:")
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
            Dim rr As Canon.IRace = Nothing
            Try : rr = Canon.CanonRecords.Race(rec, pm) : Catch : Continue For : End Try
            If rr Is Nothing Then Continue For
            For Each kEntry In rr.Keywords
                Dim k = kEntry.Keyword
                If kwType.GetValueOrDefault(k, 0UI) = 0UI Then owner.Add(k)
            Next
        Next
        Console.WriteLine($"[clipresolve] index .hkx/.hkt={animSet.Count} | race-identities={owner.Count} | filter='{edidFilter}'")

        Dim loader As Func(Of String, Byte()) = Function(p) FO4_NPC_Manager.MeshPathHelpers.TryLoadMeshBytes(p, 0)
        Dim gTot = 0, gRes = 0, gFull = 0, gStrip = 0, gAmbig = 0, gWeColl = 0
        For Each rec In races
            Dim race As Canon.IRace = Nothing
            Try : race = Canon.CanonRecords.Race(rec, pm) : Catch : Continue For : End Try
            If race Is Nothing Then Continue For
            If race.MaleBehaviorGraphModelFileName = "" AndAlso race.FemaleBehaviorGraphModelFileName = "" Then Continue For
            If edidFilter <> "" AndAlso race.EditorID.IndexOf(edidFilter, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            Dim rb = RaceBehaviorResolver.ResolveRaceBehavior(race.FormID, pm)
            If rb Is Nothing OrElse String.IsNullOrWhiteSpace(rb.Project) Then Continue For
            Dim thisKw As New HashSet(Of UInteger)(race.Keywords.Select(Function(k) k.Keyword))
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
                Dim foreignId = sd.ActorKeywords.Select(Function(k) k.Keyword).
                                FirstOrDefault(Function(k) owner.Contains(k) AndAlso Not thisKw.Contains(k))
                If foreignId <> 0UI Then Continue For  ' identidad de OTRA raza → no aplica
                Dim anims As New List(Of String)
                CollectClipAnims(NormHkx(sd.DataBehaviourGraph), loader, graphCache, New HashSet(Of String)(StringComparer.OrdinalIgnoreCase), anims, 0)
                Dim sapt = sd.AnimationPaths.Select(Function(x) x.Path).ToList()
                For Each an In anims
                    Dim we As Boolean = False : Dim how = ResolveExist(an, sapt, actorRoot, animSet, we) : tally(an, we, how)
                Next
            Next

            Console.WriteLine($"=== {race.EditorID,-32} repertoire={resolvedFiles.Count,5}  NOT-resolved={unresDistinct.Count,4}  ambig(fallback)={ambig,6}  weColl(full≠strip SAME path)={weColl}")
            For Each u In unresolved : Console.WriteLine($"      NOT-RESOLVED {u}") : Next
            For Each a In weCollSamples : Console.WriteLine($"      weColl {a}") : Next
            ' Dump del repertorio (para verificar sharing entre razas vía intersección externa) cuando hay filtro.
            If edidFilter <> "" Then
                Dim dumpPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rep_{race.EditorID}.txt")
                System.IO.File.WriteAllLines(dumpPath, resolvedFiles.OrderBy(Function(x) x, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine($"      [dump] repertoire → {dumpPath}")
            End If
            gTot += tot : gRes += res : gFull += full : gStrip += strip : gAmbig += ambig : gWeColl += weColl
        Next
        Dim gp = If(gTot > 0, 100.0 * gRes / gTot, 100.0)
        Console.WriteLine($"[clipresolve-TOTAL] attempts={gTot} resolved={gRes} ({gp:F1}%) | full={gFull} strip={gStrip}")
        Console.WriteLine($"[clipresolve-TOTAL] ambig(fallback cross-entry, benign)={gAmbig} | weColl(full≠strip SAME path = own tiebreak)={gWeColl}")
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
        Console.WriteLine($"[raceanim] {races.Count} RACE records | filter edid='{edidFilter}'")
        Dim shown As Integer = 0
        Dim gTotClips = 0, gOk = 0, gMis = 0, gMiss = 0
        Dim gBadRaces As New List(Of String), gMissRaces As New List(Of String)
        Dim gSkelAnom As New List(Of String), gSkelSeen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each rec In races
            Dim race As Canon.IRace = Nothing
            Try
                race = Canon.CanonRecords.Race(rec, pm)
            Catch
                Continue For
            End Try
            If race Is Nothing Then Continue For
            If edidFilter <> "" AndAlso race.EditorID.IndexOf(edidFilter, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            If race.MaleBehaviorGraphModelFileName = "" AndAlso race.FemaleBehaviorGraphModelFileName = "" Then Continue For  ' solo razas con behavior

            Dim rb = RaceBehaviorResolver.ResolveRaceBehavior(race.FormID, pm)
            If rb Is Nothing Then Continue For
            shown += 1
            Console.WriteLine($"=== {race.EditorID} [0x{race.FormID:X8}] ===")
            Console.WriteLine($"  project  M='{rb.MaleProject}'  F='{rb.FemaleProject}'")
            Console.WriteLine($"  skeleton M='{rb.MaleSkeleton}'  F='{rb.FemaleSkeleton}'")
            Console.WriteLine($"  subgraphs={rb.Subgraphs.Count}  source={rb.SubgraphSource}")
            Dim byGraph = rb.Subgraphs.GroupBy(Function(s) s.DataBehaviourGraph, StringComparer.OrdinalIgnoreCase).
                             OrderByDescending(Function(g) g.Count()).ToList()
            For Each g In byGraph.Take(8)
                Dim roles = String.Join(",", g.Select(Function(s) RoleName(CInt(s.FlagsRole))).Distinct())
                Console.WriteLine($"     x{g.Count(),3}  {g.Key}   [{roles}]")
            Next
            If byGraph.Count > 8 Then Console.WriteLine($"     … (+{byGraph.Count - 8} more graphs)")
            Console.WriteLine($"  distinct .hkx files to load: {rb.DistinctBehaviorFiles().Count}")
            ' [RACE-RECORD] keywords del RACE + subgraphs (SAKD/SAPT) marcando los que matchean → filtro por raza.
            If edidFilter <> "" Then
                Dim raceKw = String.Join(", ", race.Keywords.Select(Function(k) EdidOf(pm, k.Keyword)))
                Dim raceFo4Rec = TryCast(race, Canon.RaceFO4)
                Dim ownSubgraphCount = If(raceFo4Rec Is Nothing, 0, raceFo4Rec.SubgraphData.Count)
                Dim subgraphTemplateRace = If(raceFo4Rec Is Nothing, 0UI, raceFo4Rec.SubgraphTemplateRace)
                Console.WriteLine($"  [RACE-RECORD] {race.EditorID}: Keywords=[{raceKw}] | OWN={ownSubgraphCount} SRAC=0x{subgraphTemplateRace:X8}")
                Dim kwSet = New HashSet(Of UInteger)(race.Keywords.Select(Function(k) k.Keyword))
                For Each sd In rb.Subgraphs
                    Dim sakd = String.Join("+", sd.ActorKeywords.Select(Function(k) EdidOf(pm, k.Keyword)))
                    Dim apply = sd.ActorKeywords.Count = 0 OrElse sd.ActorKeywords.Any(Function(k) kwSet.Contains(k.Keyword))
                    Console.WriteLine($"     {If(apply, "✓APPLIES", "·skip  ")} SGNM='{System.IO.Path.GetFileName(sd.DataBehaviourGraph)}' SAKD=[{sakd}] SAPT: {String.Join(" ; ", sd.AnimationPaths.Select(Function(x) x.Path))}")
                Next
            End If

            ' Skeleton SÓLIDO (rigName del behavior character) + enumeración de clips.
            Dim loader As Func(Of String, Byte()) = Function(p) FO4_NPC_Manager.MeshPathHelpers.TryLoadMeshBytes(p, 0)
            Dim havokSkel = BehaviorClipEnumerator.ResolveHavokSkeleton(rb, loader)
            Console.WriteLine($"  skeleton (Havok, character's rigName) = '{havokSkel}'")
            ' Comparación de SETS de huesos: NIF (render, rb.Skeleton) vs HKX (behavior, havokSkel).
            CompareNifHkxBoneSets(race.EditorID, havokSkel, rb.Skeleton)
            ComposeAndCompareSkeleton(race.EditorID, havokSkel, rb.Skeleton)
            Dim clips = BehaviorClipEnumerator.EnumerateClips(rb, loader)
            Console.WriteLine($"  playable CLIPS (dedup by file): {clips.Count}")
            For Each rg In clips.SelectMany(Function(c) c.Roles).GroupBy(Function(r) r).OrderByDescending(Function(g) g.Count())
                Console.WriteLine($"     role {rg.Key,-10} : {rg.Count()} clips")
            Next
            For Each c In clips.Take(12)
                Console.WriteLine($"     · {c.AnimationFile}  [{String.Join(",", c.Roles)}]  speed={c.PlaybackSpeed:0.##}")
            Next
            If clips.Count > 12 Then Console.WriteLine($"     … (+{clips.Count - 12} more clips)")

            ' === VALIDACIÓN compacta (TODAS las razas): cada clip debe existir y maxBoneIdx < bones del skel.
            Dim vOk = 0, vLow = 0, vMiss = 0
            ValidateRaceClipsCompact(havokSkel, clips, vOk, vLow, vMiss, edidFilter <> "")
            Dim badTag = If(vLow > 0, "  <<< LOW-COVERAGE(no mapping)", "") & If(vMiss > 0, "  <missing " & vMiss & ">", "")
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
        Console.WriteLine($"[raceanim] races with behavior shown: {shown}")
        Console.WriteLine($"[VALIDATE-TOTAL] clips={gTotClips} ok={gOk} mismatch={gMis} missing={gMiss}")
        Console.WriteLine($"[VALIDATE-TOTAL] races with MISMATCH (would deform): {If(gBadRaces.Count = 0, "NONE ✓", String.Join(", ", gBadRaces))}")
        Console.WriteLine($"[VALIDATE-TOTAL] races with missing: {If(gMissRaces.Count = 0, "none", String.Join(", ", gMissRaces))}")
        Console.WriteLine($"[SKEL-CHECK] distinct skeletons verified={gSkelSeen.Count}; with AMBIGUOUS selection (not exactly 1 non-ragdoll): {If(gSkelAnom.Count = 0, "NONE ✓ (1 anim + ragdoll in all)", String.Join("  |  ", gSkelAnom))}")
    End Sub

    ''' <summary>--racecompat: reconstruye la inyección de razas de RaceCompatibility (proxyRaces) desde el load
    ''' order REAL y mide su efecto en el filtro de raza de los catálogos (picker / gate de presets): cuántos HDPT
    ''' pasan a ser válidos para cada raza custom con el catálogo puesto vs sin él. Solo diagnóstico.</summary>
    Private Sub RaceCompatScan(pm As PluginManager)
        Dim game = Config_App.Current.Game
        Console.WriteLine($"[racecompat] game={game}")
        Dim cat = FO4_Base_Library.RaceCompatibilityCatalog.Load(pm, game)
        Console.WriteLine($"[racecompat] augmented FormLists={cat.AugmentedListCount}  injected races={cat.InjectedRaceCount}")
        If cat.AugmentedListCount = 0 Then
            Console.WriteLine("[racecompat] no mod uses RaceCompatibility in the load order (or it isn't Skyrim) → the filter behaves as usual.")
            Return
        End If

        ' Razas custom detectadas = las que el script habría insertado en alguna lista.
        Dim races As New HashSet(Of UInteger)
        For Each hdptRec In pm.GetRecordsOfType("HDPT")
            Dim h = Canon.CanonRecords.Hdpt(hdptRec, pm)
            If h Is Nothing OrElse h.ValidRaces = 0UI Then Continue For
        Next
        For Each raceRec In pm.GetRecordsOfType("RACE")
            Dim rfid = pm.ResolveReferencedFormID(raceRec.SourcePluginName, raceRec.Header.FormID)
            Dim edid = Canon.CanonRecords.Race(raceRec, pm)?.EditorID
            ' ¿el catálogo mete esta raza en alguna lista?
            Dim injected = False
            For Each flstRec In pm.GetRecordsOfType("FLST")
                Dim ffid = pm.ResolveReferencedFormID(flstRec.SourcePluginName, flstRec.Header.FormID)
                If cat.ContainsRace(ffid, rfid) Then injected = True : Exit For
            Next
            If Not injected Then Continue For
            races.Add(rfid)

            ' Efecto real en el filtro de los catálogos: HDPT válidos con y sin la reconstrucción.
            Dim withCat = 0, withoutCat = 0
            Dim cacheA As New Dictionary(Of UInteger, Canon.IFlst)
            Dim cacheB As New Dictionary(Of UInteger, Canon.IFlst)
            Dim saved = FO4_NPC_Manager.HeadPartResolver.RaceCompatCatalog
            For Each hdptRec In pm.GetRecordsOfType("HDPT")
                Dim hfid = pm.ResolveReferencedFormID(hdptRec.SourcePluginName, hdptRec.Header.FormID)
                FO4_NPC_Manager.HeadPartResolver.RaceCompatCatalog = Nothing
                If FO4_NPC_Manager.HeadPartResolver.IsHdptValidForRace(hfid, rfid, True, pm, cacheA, Nothing, True) Then withoutCat += 1
                FO4_NPC_Manager.HeadPartResolver.RaceCompatCatalog = cat
                If FO4_NPC_Manager.HeadPartResolver.IsHdptValidForRace(hfid, rfid, True, pm, cacheB, Nothing, True) Then withCat += 1
            Next
            FO4_NPC_Manager.HeadPartResolver.RaceCompatCatalog = saved
            Console.WriteLine($"   race {edid,-28} 0x{rfid:X8}: valid HDPT WITHOUT reconstruction={withoutCat}  WITH reconstruction={withCat}  (+{withCat - withoutCat})")
        Next
        Console.WriteLine($"[racecompat] custom races injected: {races.Count}")

        ' Control: la raza VANILLA equivalente. Si la reconstrucción es correcta, la raza custom debe ofrecer
        ' aproximadamente el mismo catálogo que la vanilla a la que suplanta (es literalmente lo que hace el script).
        Console.WriteLine("[racecompat] control — vanilla races (same filter, no reconstruction):")
        For Each raceRec In pm.GetRecordsOfType("RACE")
            Dim r = Canon.CanonRecords.Race(raceRec, pm)
            If r Is Nothing Then Continue For
            If r.EditorID <> "NordRace" AndAlso r.EditorID <> "BretonRace" AndAlso r.EditorID <> "OrcRace" AndAlso r.EditorID <> "NordRaceVampire" Then Continue For
            Dim rfid = pm.ResolveReferencedFormID(raceRec.SourcePluginName, raceRec.Header.FormID)
            Dim cache As New Dictionary(Of UInteger, Canon.IFlst)
            Dim n = 0
            For Each hdptRec In pm.GetRecordsOfType("HDPT")
                Dim hfid = pm.ResolveReferencedFormID(hdptRec.SourcePluginName, hdptRec.Header.FormID)
                If FO4_NPC_Manager.HeadPartResolver.IsHdptValidForRace(hfid, rfid, True, pm, cache, Nothing, True) Then n += 1
            Next
            Console.WriteLine($"   race {r.EditorID,-28} 0x{rfid:X8}: valid HDPT={n}")
        Next
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
            Console.WriteLine($"  [NIF-vs-HKX] missing file (hkx '{hkxPath}'={hbx IsNot Nothing}, nif '{nifPath}'={nbx IsNot Nothing})")
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
        Console.WriteLine($"     [SKEL-PICK] {allSk.Count} hkaSkeleton in file: {String.Join(" | ", allSk.Select(Function(s) $"name='{s.Name}' root='{rootOf(s)}' bones={s.Bones.Count}"))}")
        If skel Is Nothing Then Console.WriteLine("  [NIF-vs-HKX] HKX without animation skeleton") : Return
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
        Console.WriteLine($"     [SKEL-PICK] NIF root(s): {String.Join(", ", nifRoots)}   | HKX(chosen) root='{rootOf(skel)}' name='{skel.Name}'")
        Console.WriteLine($"     [SKEL-PICK] root∈NIF? {String.Join(" | ", allSk.Select(Function(s) $"'{rootOf(s)}'={If(nWorld.ContainsKey(rootOf(s)), "yes", "no")}"))}")
        Console.WriteLine($"  [NIF-vs-HKX] {label}: HKX={nB} NIF={nWorld.Count} | both={both} | parent-mismatch={pMis} | transform-mismatch={tMis} | onlyHKX={onlyHkx.Count} onlyNIF={onlyNif.Count}")
        If pMisList.Count > 0 Then Console.WriteLine($"     PARENT different ({pMisList.Count}): {String.Join("  |  ", pMisList.Take(12))}")
        If tMisList.Count > 0 Then Console.WriteLine($"     TRANSFORM different ({tMisList.Count}): {String.Join("  |  ", tMisList.Take(12))}")
        If onlyHkx.Count > 0 Then Console.WriteLine($"     HKX bones MISSING in the NIF ({onlyHkx.Count}): {String.Join(", ", onlyHkx.Take(25))}")
        If onlyNif.Count > 0 Then
            ' Categorizar los soloNIF: _Offset/_skin (estructurales descartables) vs huesos reales.
            Dim offset = onlyNif.Where(Function(n) n.IndexOf("_Offset", StringComparison.OrdinalIgnoreCase) >= 0 OrElse n.IndexOf("_skin", StringComparison.OrdinalIgnoreCase) >= 0 OrElse n.EndsWith("_Offset", StringComparison.OrdinalIgnoreCase)).ToList()
            Dim reales = onlyNif.Where(Function(n) Not offset.Contains(n)).ToList()
            Console.WriteLine($"     onlyNIF ({onlyNif.Count}): _Offset/_skin structural={offset.Count} | OTHER real bones={reales.Count}")
            Console.WriteLine($"     OTHER (no _Offset) [{reales.Count}]: {String.Join(", ", reales)}")
        End If
        ' Búsqueda de nodos de REGIÓN (Torso/Upper/Lower/Leg/Arm/Limb/Region/Hip/Body) en NIF y HKX.
        Dim rxRegion = New System.Text.RegularExpressions.Regex("torso|upper|lower|leg|arm|limb|region|hip|body|skin|jaw|lip|cheek|brow|mouth|tongue|eye|face|teeth|tongue", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        Dim regNif = nWorld.Keys.Where(Function(k) rxRegion.IsMatch(k)).OrderBy(Function(k) k).ToList()
        Dim regHkx = skel.Bones.Select(Function(b) b.Name).Where(Function(n) Not String.IsNullOrEmpty(n) AndAlso rxRegion.IsMatch(n)).OrderBy(Function(n) n).ToList()
        Console.WriteLine($"     REGION in HKX ({regHkx.Count}): {String.Join(", ", regHkx)}")
        Console.WriteLine($"     REGION in NIF ({regNif.Count}): {String.Join(", ", regNif.Where(Function(k) k.IndexOf("_Offset", StringComparison.OrdinalIgnoreCase) < 0))}")
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
        If skel Is Nothing Then Console.WriteLine("  [COMPOSE] HKX without animation skeleton") : Return
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
        Console.WriteLine($"  [COMPOSE A+B+C vs NIF] {label}: composed={composedTotal} | NIF pure={nWorld.Count} | A∩NIF+B reconstructs NIF with residual={resid}/{nWorld.Count} (maxDT={maxDT:F2} maxDR={maxDR:F2}) | A-only(Weapon/IK…)={aOnly.Count} | cloth(C)={clothBones.Count}")
        If resid > 0 Then Console.WriteLine($"     RESIDUAL (composed≠NIF) e.g.: {String.Join("  |  ", residList.Take(10))}")
        If aOnly.Count > 0 Then Console.WriteLine($"     A-only (HKX, ADDED to the NIF): {String.Join(", ", aOnly.Take(25))}")
        If clothBones.Count > 0 Then Console.WriteLine($"     cloth-bones (C, from BSClothExtraData): {String.Join(", ", clothBones.Take(25))}")
    End Sub

    ''' <summary>Dump de la cadena de resolución del skeleton HKX: project → hkbProjectStringData.CharacterFilenames
    ''' → character → hkbCharacterStringData.rigName. Muestra cuántos character files hay y a qué rig apunta cada uno.</summary>
    Private Sub DumpBehaviorChain(rb As ResolvedRaceBehavior)
        If rb Is Nothing OrElse String.IsNullOrWhiteSpace(rb.Project) Then Return
        Dim proj = rb.Project
        Dim slash = proj.LastIndexOf("\"c)
        Dim actorRoot = If(slash > 0, proj.Substring(0, slash), "")
        Dim pb = LoadAnimCand(proj)
        If pb Is Nothing Then Console.WriteLine($"  [CHAIN] project '{proj}' does not load") : Return
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
            If cb Is Nothing Then Console.WriteLine($"     character '{cf}' does not load") : Continue For
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
        Console.WriteLine($"=== COMPARE robot live skeleton (log) vs clip skeleton (CreateABot.hkx) ===")
        If Not IO.File.Exists(logPath) Then Console.WriteLine($"  log not found: {logPath}") : Return
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
        Console.WriteLine($"  bones parsed from log = {live.Count}")
        If live.Count = 0 Then Return

        ' CreateABot.hkx world binds.
        Dim hbx = LoadAnimCand(hkxPath) : If hbx Is Nothing Then Console.WriteLine("  hkx not found") : Return
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
        Console.WriteLine($"  bone               | dT (position) | dR (rotation) | reading")
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
            Dim lec = If(r.dR > 0.05, "different rotation", If(r.dT > 1.0, "different position (mount/assembly)", "≈ same"))
            Console.WriteLine($"    {r.Bone,-18} | {r.dT,9:F2} | {r.dR,9:F3} | {lec}")
        Next
        Console.WriteLine($"  (O·Mount = assembled live bind. dR≈0 ⇒ same orientation as the clip; dT>0 ⇒ position moved by the mount)")
    End Sub

    ''' <summary>Compara el WORLD bind de cada hueso entre el skeleton.hkx (animación) y el
    ''' skeleton.nif (render), por nombre. Determina con datos si el render skeleton == el clip skeleton.</summary>
    Private Sub CompareSkeletonNifVsHkx(label As String, hkxPath As String, nifPath As String)
        Console.WriteLine($"=== COMPARE skeleton.nif vs skeleton.hkx — {label} ===")
        Dim hkxBytesC = LoadAnimCand(hkxPath) : Dim nifBytesC = LoadAnimCand(nifPath)
        If hkxBytesC Is Nothing Then Console.WriteLine($"  HKX not found: {hkxPath}") : Return
        If nifBytesC Is Nothing Then Console.WriteLine($"  NIF not found: {nifPath}") : Return

        ' HKX: world binds por bone (compose ReferencePose via ParentIndices, skeleton de animación = no-ragdoll).
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(hkxBytesC))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso s.ParentIndices IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        If skel Is Nothing Then Console.WriteLine("  no animation skeleton in the HKX") : Return
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
        Console.WriteLine($"  HKX bones={hkxWorld.Count} | NIF nodes={nifWorld.Count} | matched={matched} | MISMATCH={mism} | only-in-HKX={onlyHkx}")
        Console.WriteLine($"  ⇒ {If(mism = 0, "skeleton.nif == skeleton.hkx (same bind) in the shared bones", "skeleton.nif ≠ skeleton.hkx — there IS a real bind mismatch")}")
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

        Console.WriteLine("=== MOUNT/POSE ORDER VALIDATION — Assaultron (real data) ===")
        Dim skelBytes = LoadAnimCand("Actors\CreateABot\CharacterAssets\skeleton.hkx")
        If skelBytes Is Nothing Then Console.WriteLine("skeleton CreateABot NOT found") : Return
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(skelBytes))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        If skel Is Nothing Then Console.WriteLine("no animation skeleton") : Return
        Console.WriteLine($"skeleton='{skel.Name}' bones={skel.Bones.Count} referencePose={skel.ReferencePose.Count}")
        ' ¿CreateABot.hkx trae los huesos de connect-point C-/P-?
        Dim cBones = skel.Bones.Where(Function(b) b.Name IsNot Nothing AndAlso (b.Name.StartsWith("C-", StringComparison.OrdinalIgnoreCase) OrElse b.Name.StartsWith("C_", StringComparison.OrdinalIgnoreCase))).Select(Function(b) b.Name).ToList()
        Dim pBones = skel.Bones.Where(Function(b) b.Name IsNot Nothing AndAlso (b.Name.StartsWith("P-", StringComparison.OrdinalIgnoreCase) OrElse b.Name.StartsWith("P_", StringComparison.OrdinalIgnoreCase))).Select(Function(b) b.Name).ToList()
        Console.WriteLine($"  C-* in CreateABot.hkx ({cBones.Count}): {String.Join(", ", cBones)}")
        Console.WriteLine($"  P-* in CreateABot.hkx ({pBones.Count}): {String.Join(", ", pBones)}")

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
        If clipBytes Is Nothing Then Console.WriteLine("Assaultron clip NOT found") : Return
        Dim anim = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(clipBytes)).ParseAnimations().FirstOrDefault()
        If anim Is Nothing OrElse anim.Binding Is Nothing Then Console.WriteLine("anim without binding") : Return
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
        Console.WriteLine("=== FIX TEST: delta(CreateABot) clean across the WHOLE body and the WHOLE animation ===")
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
            If tr2 < 0 Then Console.WriteLine($"  {bn,-16} (not animated in this clip)") : Continue For
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
            sb.Append($"   ⇒ {If(maxT < 2.0 OrElse isRoot, "CLEAN", "⚠ high translation")}")
            Console.WriteLine(sb.ToString())
        Next
        Console.WriteLine("Reading: |T|<2 in all non-root bones ⇒ the fix delta is CLEAN joint motion (no")
        Console.WriteLine("  contamination). + the assembled-live comparison (above) gave orientation = CreateABot")
        Console.WriteLine("  (Neck dR=0, arms ≤0.13) ⇒ that clean motion is applied on the correct axis. Limbs OK.")

        For Each m In mounts
            Dim boneIdx = skel.Bones.FindIndex(Function(b) String.Equals(b.Name, m.Name, StringComparison.OrdinalIgnoreCase))
            If boneIdx < 0 Then Console.WriteLine($"--- {m.Name}: not in the skeleton") : Continue For
            Dim track = -1
            For t = 0 To Math.Min(anim.NumTransformTracks, idxArr.Count) - 1
                If idxArr(t) = boneIdx Then track = t : Exit For
            Next
            If track < 0 Then Console.WriteLine($"--- {m.Name}: not animated in this clip") : Continue For
            Dim mountMag = Math.Sqrt(m.Tx * m.Tx + m.Ty * m.Ty + m.Tz * m.Tz)
            Dim O = HkxTransformConventionHelper.ToTransform(skel.ReferencePose(boneIdx))
            Dim Oinv = O.Inverse()
            Dim mountT As New Transform_Class With {.Translation = New System.Numerics.Vector3(m.Tx, m.Ty, m.Tz)}
            Console.WriteLine($"--- {m.Name}  mount.T=({m.Tx:F2},{m.Ty:F2},{m.Tz:F2}) |T|={mountMag:F2}  (mount {If(mountMag < 0.001, "≈I → NOT affected", "≠I")}) ---")
            Console.WriteLine("    frame |  θ(°) | |offNEW−Mount| (rigid⇒0) | |offOLD−Mount| (old deforms)")
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
        Console.WriteLine("Rest: Delta=I is a no-op ⇒ O·Δ·Mount = O·Mount = the old order, byte-identical (symbolic, trivial).")
        Console.WriteLine("Reading: |offNEW−Mount|≈0 in ALL frames ⇒ the chunk stays RIGID to the bone the clip poses (offset=Mount")
        Console.WriteLine("         constant) = CORRECT. |offOLD−Mount| grows with θ ⇒ the old one deforms the offset frame by frame = the bug.")

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
        Console.WriteLine("=== clipMotion WORLD (pure motion of the bone relative to the clip's bind) ===")
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
        Console.WriteLine("  --- bindWorld CreateABot (E) — compare R with log [BONE-WORLD] originalGlobal (L·M) ---")
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
        Console.WriteLine("  Reading: |T| of clipMotion ≈0 ⇒ pure rotation (the clip's bind = CreateABot, model closes).")
        Console.WriteLine("           large |T| ⇒ the clip carries assembly translation in its frames (neutral ≠ CreateABot).")
    End Sub

    ''' <summary>Carga skel+clip y dumpea, por track al frame medio, el bone (nombre vía skeleton),
    ''' θ del delta y |traslación del delta|. Un joint sano: el origen NO se traslada del bind (|T|≈0).
    ''' |T| grande en bone no-root ⇒ el track mapea a un bone que no corresponde / bind ≠ (skeleton malo).</summary>
    Private Sub MappingSanity(label As String, skelPath As String, clipPath As String)
        Console.WriteLine()
        Console.WriteLine($"=== MAPPING SANITY — {label} ===")
        Dim sb = LoadAnimCand(skelPath)
        If sb Is Nothing Then Console.WriteLine($"  skeleton not found: {skelPath}") : Return
        Dim sg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(sb))
        Dim skel = sg.GetObjectsByClassName("hkaSkeleton").Select(Function(o) sg.ParseSkeleton(o)).
                      Where(Function(s) s IsNot Nothing AndAlso s.Bones IsNot Nothing AndAlso
                            (String.IsNullOrEmpty(s.Name) OrElse s.Name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) < 0)).FirstOrDefault()
        If skel Is Nothing Then Console.WriteLine("  no animation skeleton") : Return
        Dim cb = LoadAnimCand(clipPath)
        If cb Is Nothing Then Console.WriteLine($"  clip not found: {clipPath}") : Return
        Dim anim = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(cb)).ParseAnimations().FirstOrDefault()
        If anim Is Nothing OrElse anim.Binding Is Nothing Then Console.WriteLine("  anim without binding") : Return
        Dim idxArr = anim.Binding.TransformTrackToBoneIndices
        Dim maxIdx = -1
        For t = 0 To idxArr.Count - 1
            If idxArr(t) > maxIdx Then maxIdx = idxArr(t)
        Next
        Console.WriteLine($"skeleton='{skel.Name}' bones={skel.Bones.Count} | tracks={anim.NumTransformTracks} | binding idx max={maxIdx}")
        If maxIdx >= skel.Bones.Count Then Console.WriteLine($"  ⛔ ANOMALY: binding idx ({maxIdx}) >= bones ({skel.Bones.Count}) → clip NOT bound against this skeleton.")

        Dim midF = Math.Max(0, anim.NumFrames \ 2)
        Dim rows = New List(Of (Bone As String, Theta As Double, TMag As Double))()
        For t = 0 To Math.Min(anim.NumTransformTracks, idxArr.Count) - 1
            Dim bi = idxArr(t)
            If bi < 0 OrElse bi >= skel.Bones.Count Then rows.Add(($"<idx {bi} out-of-range>", 0, 9999)) : Continue For
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
        Console.WriteLine($"mid frame={midF} | bones with |Tdelta|>5u: {big}/{rows.Count} | median |Tdelta|={med:F2}")
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
        If bytes Is Nothing Then Console.WriteLine($"  [INV] {label}: NOT FOUND") : Return
        Try
            Dim g = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(bytes))
            Console.WriteLine($"  [INV] {label}: {g.Objects.Count} objects")
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
        Console.WriteLine("  ---- DIAGNOSTIC ----")
        Console.WriteLine($"  render skeleton (RACE .nif) = '{renderSkelNif}'")
        Dim skelBoneCount As Integer = -1
        Dim skelBoneNames As New List(Of String)
        Dim skelBytes = LoadAnimCand(havokSkelPath)
        If skelBytes Is Nothing Then
            Console.WriteLine($"  [SKEL] behavior skeleton '{havokSkelPath}' NOT FOUND")
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
        Console.WriteLine($"  [CLIPS] status per file (skel {scannedRoot}={skelBoneCount} bones)  |  binding.OriginalSkeletonName  |  own variant exists {scannedRoot}\?")
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
        Console.WriteLine($"  [CLIPS] summary: ok={ok} mismatch={mismatch} missing={missing}  withOwnVariant={ownVariant}  total={clips.Count}")

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
                Console.WriteLine($"  [CMP Stingwing vs {srcRoot}] skeleton '{srcSkelPath}' NOT FOUND/parse-fail")
                Continue For
            End If
            Dim n = Math.Max(skelBoneNames.Count, srcNames.Count)
            Dim sameIdx = 0
            For i = 0 To Math.Min(skelBoneNames.Count, srcNames.Count) - 1
                If skelBoneNames(i).Equals(srcNames(i), StringComparison.OrdinalIgnoreCase) Then sameIdx += 1
            Next
            Dim common = skelBoneNames.Intersect(srcNames, StringComparer.OrdinalIgnoreCase).ToList()
            Console.WriteLine($"  [CMP Stingwing({skelBoneNames.Count}) vs {srcRoot}({srcNames.Count})]  sameIdx={sameIdx}/{Math.Min(skelBoneNames.Count, srcNames.Count)}  sameName(set)={common.Count}")
            Console.WriteLine($"      bones in common (by name): [{String.Join(", ", common)}]")
            ' Detalle de las primeras divergencias por índice (hasta 18 filas).
            Dim shownRows = 0
            For i = 0 To Math.Min(skelBoneNames.Count, srcNames.Count) - 1
                Dim a = skelBoneNames(i) : Dim b = srcNames(i)
                If Not a.Equals(b, StringComparison.OrdinalIgnoreCase) Then
                    Console.WriteLine($"      idx {i,2}: Stingwing='{a}'  <>  {srcRoot.Replace("Actors\", "")}='{b}'")
                    shownRows += 1
                    If shownRows >= 18 Then Console.WriteLine("      … (more divergences)") : Exit For
                End If
            Next
            If sameIdx = Math.Min(skelBoneNames.Count, srcNames.Count) AndAlso srcNames.Count = skelBoneNames.Count Then
                Console.WriteLine($"      → IDENTICAL order+names: the anims of {srcRoot} map WELL onto Stingwing")
            End If
        Next
    End Sub

    ' Dump CRUDO del mecanismo real: lista animationNames del character (indexada) + cada clip con su
    ' animationBindingIndex y su animationName literal. Para verificar que animationNames[bindingIndex]
    ' apunta al archivo NATIVO del actor (no heurística).
    Private Sub RawClipPathDump(rb As ResolvedRaceBehavior, loader As Func(Of String, Byte()))
        Console.WriteLine("  ---- RAW (engine mechanism) ----")
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
                    If cbytes Is Nothing Then Console.WriteLine($"    character '{cfp}' NOT FOUND") : Continue For
                    Dim cg = HkxObjectGraphParser_Class.BuildGraph(HkxPackfileParser_Class.Parse(cbytes))
                    For Each co In cg.GetObjectsByClassName("hkbCharacterStringData")
                        Dim csd = cg.ParseCharacterStringData(co)
                        If csd Is Nothing Then Continue For
                        charAnimNames = csd.AnimationFilenames.ToList()
                        Console.WriteLine($"    character='{cfp}'  animationNames(filtered)={charAnimNames.Count}  allStrings={csd.AllStrings.Count}")
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
                    Dim resolved = If(c.AnimationBindingIndex >= 0 AndAlso c.AnimationBindingIndex < charAnimNames.Count, charAnimNames(c.AnimationBindingIndex), "<idx out of range>")
                    Console.WriteLine($"        clip='{c.Name}' bindIdx={c.AnimationBindingIndex} rawAnimName='{c.AnimationName}'  →animNames[idx]='{resolved}'")
                    shown += 1
                    If shown >= 10 Then Console.WriteLine("        … (more clips)") : Exit For
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
        If npcFormID = 0UI Then Console.WriteLine($"[info] {edid}: not resolved in {espName}") : Return
        Dim npcRec = pm.GetRecord(npcFormID)
        Dim npcData = RecordParsers.ParseNPC(npcRec, pm)
        Dim raceRec = pm.GetRecord(npcData.Record.Race)
        Dim race = Canon.CanonRecords.Race(raceRec, pm)
        Console.WriteLine($"=== INFO {edid} 0x{npcFormID:X8} src='{npcRec.SourcePluginName}' race=0x{npcData.Record.Race:X8} female={npcData.Record.ConfigurationFlagsFemale} ===")

        Console.WriteLine("-- NPC.HeadTexture (FTST NPC-level, field 418) --")
        PrintTxst(pm, "NPC.HeadTexture", npcData.Record.HeadTexture)
        Console.WriteLine("-- RACE default face TXST (by gender) --")
        PrintTxst(pm, "RACE.default", race.DefaultFaceTextureDe(npcData.Record.ConfigurationFlagsFemale))

        Console.WriteLine("-- §3 ResolveFaceSkin (what the CLI uses TODAY) --")
        Dim d3 As String = "", n3 As String = "", s3 As String = ""
        ResolveFaceSkin(npcData, race, pm, d3, n3, s3)
        Console.WriteLine($"      D={DdsInfo(d3)}")
        Console.WriteLine($"      N={DdsInfo(n3)}")
        Console.WriteLine($"      S={DdsInfo(s3)}")

        ' -- FMRI/FMRS (deformacion facial per-NPC por regiones de hueso) + resolucion del JSON de la RACE.
        '    Existe porque `GetFacialBoneRegionsForRace` arma la ruta con `race.EditorID` y, si la key no
        '    esta en el FilesDictionary, devuelve Nothing EN SILENCIO: el bake sale con cabeza NEUTRA y
        '    nada lo reporta. Sin este volcado no se puede distinguir "el NPC no tiene FMRS" de
        '    "el NPC tiene FMRS y el archivo de regiones de su raza no existe".
        Console.WriteLine("-- FMRI/FMRS (per-NPC facial bone regions) --")
        Console.WriteLine($"  RACE.EditorID='{race.EditorID}'  MorphRace=0x{race.MorphRace:X8}  NPC.FMIN={npcData.Record.IntensidadDeMorfoFacial()}")
        Dim genderKey = If(npcData.Record.ConfigurationFlagsFemale, "Female", "Male")
        Dim fbrKey = $"meshes\actors\character\characterassets\{race.EditorID}FacialBoneRegions{genderKey}.txt".ToLowerInvariant()
        Dim fbrLoc As FilesDictionary_class.File_Location = Nothing
        Dim fbrFound = FilesDictionary_class.Dictionary.TryGetValue(fbrKey, fbrLoc)
        Console.WriteLine($"  FacialBoneRegions file: '{fbrKey}' -> {If(fbrFound, "FOUND", "*** NOT FOUND ***")}")
        Dim fbrMerged = FO4_NPC_Manager.NpcMorphPoseResolver.GetFacialBoneRegionsForFmriResolution(race, npcData.Record.ConfigurationFlagsFemale)
        Console.WriteLine($"  merged regions table: {If(fbrMerged Is Nothing, "Nothing (no bone-region morph can be applied)", $"{fbrMerged.Regions.Count} regions")}")
        Dim fms = MorfosDeRegionDe(npcData.Record)
        If fms.Count = 0 Then
            Console.WriteLine("  NPC FMRI entries: 0 (neutral face by bone regions)")
        Else
            Dim nonZero = fms.Where(Function(f) ValoresDeMorfo(f).Any(Function(v) v <> 0.0F)).Count()
            Console.WriteLine($"  NPC FMRI entries: {fms.Count} ({nonZero} with a non-zero FMRS value)")
            For Each fm In fms
                Dim vals = String.Join(",", ValoresDeMorfo(fm).Select(Function(v) v.ToString("F4")))
                Dim known = fbrMerged IsNot Nothing AndAlso fbrMerged.Regions.ContainsKey(fm.FaceMorphIndex)
                Console.WriteLine($"    FMRI={fm.FaceMorphIndex} region={If(known, "IN TABLE", "NOT IN TABLE -> DROPPED")}  FMRS=[{vals}]")
            Next
        End If

        ' -- MSDK/MSDV: el OTRO canal de deformacion facial per-NPC (sliders de chargen contra el .tri).
        '    Va junto al de FMRS porque son los dos unicos caminos por los que la cara de un NPC se
        '    aparta de la malla neutra, y hasta ahora ninguno de los dos se podia ver desde el CLI.
        Console.WriteLine("-- MSDK/MSDV (per-NPC chargen slider morphs) --")
        Dim mv = npcData.Record.MorfosDeCara()
        If mv Is Nothing OrElse mv.Count = 0 Then
            Console.WriteLine("  NPC MSDK/MSDV entries: 0 (neutral face by sliders)")
        Else
            Dim nz = mv.Where(Function(kv) kv.Value <> 0.0F).Count()
            Console.WriteLine($"  NPC MSDK/MSDV entries: {mv.Count} ({nz} non-zero)")
            Dim razaSliders = TryCast(race, Canon.RaceFO4)
            Dim raceSliders As IReadOnlyList(Of Canon.RaceFO4_MorphValues) =
                If(razaSliders Is Nothing, New List(Of Canon.RaceFO4_MorphValues)(), razaSliders.MorphValues)
            For Each kv In mv.OrderBy(Function(x) x.Key)
                Dim def = raceSliders.FirstOrDefault(Function(x) x.ValueIndex = kv.Key)
                Dim nm = If(def Is Nothing, "*** no MSID in RACE -> DROPPED ***", $"min='{def.ValueMinName}' max='{def.ValueMaxName}'")
                Console.WriteLine($"    MSDK={kv.Key} MSDV={kv.Value:F4}  {nm}")
            Next
        End If

        Console.WriteLine("-- HeadParts (HDPT: PartType, HDPT.TextureSet, NIF inline material) --")
        If npcData.Record.PartesDeCabeza().Count > 0 Then
            For Each hpId In npcData.Record.PartesDeCabeza()
                Dim rec = pm.GetRecord(hpId)
                If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Console.WriteLine($"  HDPT 0x{hpId:X8} not resolved") : Continue For
                Dim hdpt = Canon.CanonRecords.Hdpt(rec, pm)
                Console.WriteLine($"  HDPT 0x{hpId:X8} '{rec.EditorID}' partType={hdpt.TipoDeParte()} src='{rec.SourcePluginName}' mesh='{hdpt.ModelFileName}'")
                PrintTxst(pm, "    HDPT.TextureSet", hdpt.TextureSet)
                If String.IsNullOrWhiteSpace(hdpt.ModelFileName) Then Continue For
                Dim mp = hdpt.ModelFileName.Replace("/"c, "\"c).TrimStart("\"c)
                If Not mp.StartsWith("meshes\", StringComparison.OrdinalIgnoreCase) Then mp = "Meshes\" & mp
                Dim nifBytes = FilesDictionary_class.GetBytes(mp)
                If nifBytes Is Nothing OrElse nifBytes.Length = 0 Then Console.WriteLine($"    NIF no bytes (key='{mp}')") : Continue For
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
                    Console.WriteLine($"    NIF load failed: {ex.Message}")
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
        If npcFormID = 0UI Then Console.WriteLine($"[neckseam] {edid}: not resolved in {espName}") : Return
        Dim npcRec = pm.GetRecord(npcFormID)
        Dim npcData = RecordParsers.ParseNPC(npcRec, pm)
        Dim raceRec = pm.GetRecord(npcData.Record.Race)
        Dim race = Canon.CanonRecords.Race(raceRec, pm)
        Dim female = npcData.Record.ConfigurationFlagsFemale
        Console.WriteLine($"=== NECKSEAM {edid} 0x{npcFormID:X8} race='{race.EditorID}' female={female} ===")

        ' Default weight y NNAM son exclusivos de Fallout 4 — Skyrim no los declara en RACE.
        Dim raceFo4Neck = TryCast(race, Canon.RaceFO4)
        Dim raceDefaultThin As Single? = Nothing
        Dim raceDefaultMuscular As Single? = Nothing
        Dim raceDefaultFat As Single? = Nothing
        Dim nnamX As Single = 0.0F
        Dim nnamY As Single = 0.0F
        If raceFo4Neck IsNot Nothing Then
            If female Then
                If raceFo4Neck.FemaleDefaultWeightThinPresente Then raceDefaultThin = raceFo4Neck.FemaleDefaultWeightThin
                If raceFo4Neck.FemaleDefaultWeightMuscularPresente Then raceDefaultMuscular = raceFo4Neck.FemaleDefaultWeightMuscular
                If raceFo4Neck.FemaleDefaultWeightFatPresente Then raceDefaultFat = raceFo4Neck.FemaleDefaultWeightFat
                nnamX = raceFo4Neck.FemaleNeckFatAdjustmentsScaleX
                nnamY = raceFo4Neck.FemaleNeckFatAdjustmentsScaleY
            Else
                If raceFo4Neck.MaleDefaultWeightThinPresente Then raceDefaultThin = raceFo4Neck.MaleDefaultWeightThin
                If raceFo4Neck.MaleDefaultWeightMuscularPresente Then raceDefaultMuscular = raceFo4Neck.MaleDefaultWeightMuscular
                If raceFo4Neck.MaleDefaultWeightFatPresente Then raceDefaultFat = raceFo4Neck.MaleDefaultWeightFat
                nnamX = raceFo4Neck.MaleNeckFatAdjustmentsScaleX
                nnamY = raceFo4Neck.MaleNeckFatAdjustmentsScaleY
            End If
        End If
        Dim wt = ResolveW(npcData.Record.PesoDelCuerpo(0), raceDefaultThin)
        Dim wm = ResolveW(npcData.Record.PesoDelCuerpo(1), raceDefaultMuscular)
        Dim wf = ResolveW(npcData.Record.PesoDelCuerpo(2), raceDefaultFat)
        Console.WriteLine($"MWGT thin={wt:F3} musc={wm:F3} fat={wf:F3}  (npc raw: t={FmtN(npcData.Record.PesoDelCuerpo(0))} m={FmtN(npcData.Record.PesoDelCuerpo(1))} f={FmtN(npcData.Record.PesoDelCuerpo(2))})")
        Console.WriteLine($"RACE NNAM (gender): X={nnamX:F4} Y={nnamY:F4}")

        Dim fmin = If(npcData.Record.IntensidadDeMorfoFacial() <= 0.0F, 1.0F, npcData.Record.IntensidadDeMorfoFacial())
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
                    Dim fm = MorfosDeRegionDe(npcData.Record).FirstOrDefault(Function(f) f.FaceMorphIndex = neck.Key)
                    If fm IsNot Nothing Then block2 = fm.ValuesPositionZ
                End If
            Catch ex As Exception
                Console.WriteLine($"  [FacialBoneRegions parse failed: {ex.Message}]")
            End Try
        Else
            Console.WriteLine($"  [FacialBoneRegions not found: {frPath}]")
        End If
        Dim neckScaleY As Single = 1.0F, neckScaleZ As Single = 1.0F
        If block2 > 0.0F Then
            neckScaleY = 1.0F + nnamX * fmin * block2
            neckScaleZ = 1.0F + nnamY * fmin * block2
        End If
        Console.WriteLine($"FMIN={fmin:F3}  IsNeckRegion id={neckRegionId}  block2(FMRS posZ)={block2:F4}")
        Console.WriteLine($"==> NNAM neckScale: Y={neckScaleY:F4} Z={neckScaleZ:F4}  {If(block2 > 0.0F AndAlso (nnamX <> 0 OrElse nnamY <> 0), "NNAM ACTIVE", "NNAM no-op")}")

        Dim mrsv = npcData.Record.ValoresDeRegionCorporal()
        Console.WriteLine($"MRSV=[{If(mrsv Is Nothing, "null", String.Join(",", mrsv.Select(Function(x) x.ToString("F3"))))}]")

        Dim skelBind As New SkeletonInstance()
        Dim skelNnam As New SkeletonInstance()
        If Not skelBind.LoadFromKey("meshes\actors\character\characterassets\skeleton.nif") OrElse
           Not skelNnam.LoadFromKey("meshes\actors\character\characterassets\skeleton.nif") Then
            Console.WriteLine("[neckseam] skeleton.nif does not load -> no seam math.") : Return
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
        Console.WriteLine("-- NNAM propagation: |Δorigin| of each bone between skelBind and skelNnam (>0 = NNAM moves it) --")
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
            Console.WriteLine("-- direct children of 'Neck' (to evaluate per-pose compensation): --")
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

        Console.WriteLine("-- HEAD PARTS: nape verts (bottom-Z) + weighting bones + NNAM gap per vert attached to 'Neck' (world X/Y/Z) --")
        If npcData.Record.PartesDeCabeza().Count > 0 Then
            For Each hpId In npcData.Record.PartesDeCabeza()
                Dim rec = pm.GetRecord(hpId)
                If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Continue For
                Dim hdpt = Canon.CanonRecords.Hdpt(rec, pm)
                If String.IsNullOrWhiteSpace(hdpt.ModelFileName) Then Continue For
                Dim mp = hdpt.ModelFileName.Replace("/"c, "\"c).TrimStart("\"c)
                If Not mp.StartsWith("meshes\", StringComparison.OrdinalIgnoreCase) Then mp = "Meshes\" & mp
                Dim nifBytes = FilesDictionary_class.GetBytes(mp)
                If nifBytes Is Nothing OrElse nifBytes.Length = 0 Then Continue For
                Dim nif As New Nifcontent_Class_Manolo() : nif.Load_Manolo(nifBytes)
                Console.WriteLine($"  HDPT '{rec.EditorID}' mesh='{hdpt.ModelFileName}'")
                AnalyzeShapeSeam(nif, skelBind, skelNnam, False)
            Next
        End If

        ' --- BODY side (vanilla female body como proxy del cuello del body/outfit): verts del cuello (top-Z) ---
        Console.WriteLine("-- BODY (femalebody.nif): neck verts (top-Z) + bones + NNAM gap per vert attached to 'Neck' --")
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
                Console.WriteLine($"    [Neck-literal] verts attached to 'Neck': {nNeck}/{verts.Count} (wmax={wmax:F2} wsum={wsum:F1} Zrange=[{zmin:F1}..{zmax:F1}])")
            Else
                Console.WriteLine($"    [Neck-literal] 'Neck' is NOT in this shape's palette")
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
            Console.WriteLine($"      NNAM gap @seam (propagation incl.): vertsWithGap={nWithGap}/{seamCount} avg=({gapAccX / seamCount:F3},{gapAccY / seamCount:F3},{gapAccZ / seamCount:F3}) max|gap|={maxGap:F3}")
        Next
    End Sub

    ''' <summary>Escala body-weight por hueso (Layer1 weight K-term + Layer3 MRSV), tabla engine.</summary>
    Private Sub DumpNeckBoneScales(race As Canon.IRace, female As Boolean, wt As Single, wm As Single, wf As Single, mrsv As List(Of Single))
        Dim region As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase) From {
            {"Head_skin", 0}, {"Face_skin", 0}, {"Neck1_skin", 0},
            {"Neck_skin", 1}, {"chest_skin", 1}, {"Chest_Rear_Skin", 1}, {"Chest_Upper_Skin", 1}, {"Neck_Low_skin", 1}, {"Spine2_skin", 1}, {"Spine2_Rear_skin", 1}}
        Dim tx = wm * 0.5F + wf - 0.5F
        Dim ty = (wt + wf) * 0.866025F - 0.577350F
        Dim kk = (0.866025F - CSng(Math.Sqrt(tx * tx + ty * ty))) * 1.154701F
        ' Bone Data es exclusivo de Fallout 4 — Skyrim no lo declara en RACE.
        Dim razaHuesos = TryCast(race, Canon.RaceFO4)
        Dim gb As Canon.RaceFO4_BoneScaleData = Nothing
        If razaHuesos IsNot Nothing Then
            gb = razaHuesos.BoneScaleData.
                 FirstOrDefault(Function(b) b.BoneWeightScaleDataWeightScaleTargetGender = If(female, 1UI, 0UI))
        End If
        If gb Is Nothing Then Console.WriteLine("   (no BoneData for the gender)") : Return
        Dim names = {"Neck1_skin", "Neck_skin", "Neck_Low_skin", "Spine2_skin", "Chest_skin", "Chest_Upper_Skin", "Head_skin", "Face_skin"}
        For Each nm In names
            Dim ws = gb.BoneWeightScales.FirstOrDefault(Function(x) x.BoneWeightScaleSetName.Equals(nm, StringComparison.OrdinalIgnoreCase))
            Dim rm = gb.BoneRangeModifiers.FirstOrDefault(Function(x) x.BoneRangeModifierName.Equals(nm, StringComparison.OrdinalIgnoreCase))
            If ws Is Nothing AndAlso rm Is Nothing Then Console.WriteLine($"   {nm}: <not in RACE.BoneData>") : Continue For
            Dim sx = 1.0F, sy = 1.0F, sz = 1.0F
            If ws IsNot Nothing Then
                sx = ws.ThinX * wt + ws.MuscularX * wm + ws.FatX * wf - ((ws.ThinX + ws.MuscularX + ws.FatX) / 3.0F - 1.0F) * kk
                sy = ws.ThinY * wt + ws.MuscularY * wm + ws.FatY * wf - ((ws.ThinY + ws.MuscularY + ws.FatY) / 3.0F - 1.0F) * kk
                sz = ws.ThinZ * wt + ws.MuscularZ * wm + ws.FatZ * wf - ((ws.ThinZ + ws.MuscularZ + ws.FatZ) / 3.0F - 1.0F) * kk
            End If
            Dim regIdx As Integer = -1
            Dim tmp As Integer
            If region.TryGetValue(nm, tmp) Then regIdx = tmp
            If rm IsNot Nothing AndAlso mrsv IsNot Nothing AndAlso regIdx >= 0 AndAlso regIdx < mrsv.Count Then
                Dim slider = mrsv(regIdx)
                If slider >= 0 Then
                    sy += slider * rm.RangeMaxY : sz += slider * rm.RangeMaxZ
                Else
                    sy += (-slider) * rm.RangeMinY : sz += (-slider) * rm.RangeMinZ
                End If
            End If
            Console.WriteLine($"   {nm}: region={regIdx} WS={ws IsNot Nothing} RM={rm IsNot Nothing} -> scale=({sx:F4},{sy:F4},{sz:F4})")
        Next
    End Sub

    ''' <summary>Imprime un TXST (D/N/S + dims + MNAM/bgsm) para --info.</summary>
    Private Sub PrintTxst(pm As PluginManager, label As String, formId As UInteger)
        If formId = 0UI Then Console.WriteLine($"  {label}: 0 (none)") : Return
        Dim rec = pm.GetRecord(formId)
        If rec Is Nothing OrElse rec.Header.Signature <> "TXST" Then
            Console.WriteLine($"  {label}: 0x{formId:X8} is not TXST (sig={rec?.Header.Signature})") : Return
        End If
        Dim t = Canon.CanonRecords.Txst(rec, pm)
        Console.WriteLine($"  {label}: 0x{formId:X8} src='{rec.SourcePluginName}'")
        Console.WriteLine($"      D={DdsInfo(t.Ranura(0))}")
        Console.WriteLine($"      N={DdsInfo(t.Ranura(1))}")
        Console.WriteLine($"      S={DdsInfo(t.Ranura(7))}")
        If Not String.IsNullOrEmpty(t.MaterialDe()) Then Console.WriteLine($"      MNAM(bgsm)='{t.MaterialDe()}'")
    End Sub

    ''' <summary>Path + dims (WxH) + tamaño del DDS para --info (sin decodificar full; lee el header).</summary>
    Private Function DdsInfo(rawPath As String) As String
        If String.IsNullOrEmpty(rawPath) Then Return "<empty>"
        Dim key = FO4UnifiedMaterial_Class.CorrectTexturePath(rawPath)
        Dim b = FilesDictionary_class.GetBytes(key)
        If b Is Nothing OrElse b.Length < 20 Then Return $"'{rawPath}' (no bytes)"
        Dim h = BitConverter.ToInt32(b, 12), w = BitConverter.ToInt32(b, 16)
        Return $"'{rawPath}' {w}x{h} {b.Length}b"
    End Function

    ''' <summary>Diffuse/Normal/SmoothSpec de la cara: NPC.HeadTexture (FTST) o, si 0, el default
    ''' de la RACE por genero. Mismo source que el fallback Face del render (record-puro).</summary>
    Private Sub ResolveFaceSkin(npcData As NPC_Data, race As Canon.IRace, pm As PluginManager,
                                ByRef d As String, ByRef n As String, ByRef s As String)
        Dim txstId = npcData.Record.HeadTexture
        If txstId = 0UI AndAlso race IsNot Nothing Then
            txstId = race.DefaultFaceTextureDe(npcData.Record.ConfigurationFlagsFemale)
        End If
        If txstId = 0UI Then Return
        Dim rec = pm.GetRecord(txstId)
        If rec Is Nothing OrElse rec.Header.Signature <> "TXST" Then Return
        Dim txst = Canon.CanonRecords.Txst(rec, pm)
        d = txst.Ranura(0)
        n = txst.Ranura(1)
        s = txst.Ranura(7)
    End Sub

    Private Sub WriteChannel(dir As String, localId As UInteger, suffix As String, ch As FaceTintCpuCompositor.CpuChannelResult)
        If ch Is Nothing OrElse ch.Bgra Is Nothing Then
            Console.Error.WriteLine($"[warn] {localId:X8} channel '{suffix}' empty, skip") : Return
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
                Case "--game"
                    ' NO caer al default en silencio: un valor no reconocido de --game NUNCA debe
                    ' interpretarse como Fallout4. Ejemplo concreto que esto evita: `--game sse` (el
                    ' alias natural de Skyrim Special Edition) corriendo un barrido entero de FO4
                    ' rotulado como SSE — un "verde" sobre un juego que nunca se midio. Misma familia
                    ' que los otros fallos silenciosos del arnes: no falla nada, simplemente no se mide
                    ' lo que uno cree. Tabla explicita de alias + ABORTO con exit != 0 si no se reconoce.
                    a.GameRaw = v
                    Dim gv = v.Trim().ToLowerInvariant()
                    Select Case gv
                        Case "sse", "sk", "skyrim", "skyrimse", "skyrimspecialedition"
                            a.Game = Config_App.Game_Enum.Skyrim
                        Case "fo4", "fallout4", "fallout", "f4"
                            a.Game = Config_App.Game_Enum.Fallout4
                        Case Else
                            Console.Error.WriteLine($"--game: unrecognized value '{v}'.")
                            Console.Error.WriteLine("  Skyrim  : sse | sk | skyrim | skyrimse | skyrimspecialedition")
                            Console.Error.WriteLine("  Fallout4: fo4 | fallout4 | fallout | f4")
                            Console.Error.WriteLine("  (before, this fell back to Fallout4 SILENTLY and the sweep measured the wrong game)")
                            Return Nothing
                    End Select
                    i += 2
                Case "--compareck" : a.CompareCk = True : i += 1
                Case "--comparefiles" : a.CompareFiles = v : i += 2
                Case "--dumpnif" : a.DumpNif = v : i += 2
                Case "--skincheck" : a.SkinCheck = v : i += 2
                Case "--ssecomparebatch"
                    a.SseCompareBatch = True
                    If i + 1 < args.Length AndAlso Integer.TryParse(args(i + 1), a.SseCompareBatchLimit) Then i += 2 Else i += 1
                Case "--headfidelity"
                    ' Implica el barrido: la medición se engancha adentro del bake, así que necesita el
                    ' mismo corpus y el mismo recorrido.
                    a.HeadFidelity = True
                    a.SseCompareBatch = True
                    If i + 1 < args.Length AndAlso Integer.TryParse(args(i + 1), a.SseCompareBatchLimit) Then i += 2 Else i += 1
                Case "--vertexbatch"
                    a.VertexBatch = True
                    If i + 1 < args.Length AndAlso Integer.TryParse(args(i + 1), a.VertexBatchLimit) Then i += 2 Else i += 1
                Case "--vbout" : a.VertexBatchOut = v : i += 2
                Case "--posdump" : a.PosDump = v : i += 2
                Case "--meshshaders" : a.MeshShaders = v : i += 2
                Case "--buildfacegen" : a.BuildFaceGen = True : i += 1
                Case "--posthresh"
                    If i + 1 < args.Length AndAlso Double.TryParse(v, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, a.PosThresh) Then i += 2 Else i += 1
                Case "--ddscompare" : a.DdsCompare = True : i += 1
                Case "--rawdds" : a.RawDds = True : i += 1
                Case "--alphagatescan" : a.AlphaGateScan = True : i += 1
                Case "--tintcountscan" : a.TintCountScan = True : i += 1
                Case "--ddsprobe" : a.DdsProbe = v : i += 2
                Case "--recscan" : a.RecScan = v : i += 2
                Case "--meshcollide" : a.MeshCollide = True : i += 1
                Case "--texslotdiff" : a.TexSlotDiff = True : i += 1
                Case "--dumpacc" : a.DumpAcc = v : i += 2
                Case "--shapeorder" : a.ShapeOrder = True : i += 1
                Case "--defaults" : a.Defaults = True : i += 1
                Case "--engineskinblend" : a.EngineSkinNorm = True : i += 1
                Case "--noengineskinblend" : a.EngineSkinNorm = False : i += 1
                Case "--vanillaonly" : a.VanillaOnly = True : i += 1
                Case "--nocc" : a.NoCreationClub = True : i += 1
                Case "--rankby" : a.RankBy = v.ToLowerInvariant() : i += 2
                Case "--info" : a.Info = True : i += 1
                Case "--tints" : a.Tints = True : i += 1
                Case "--ttedscan" : a.TtedScan = True : i += 1
                Case "--scandiff" : a.ScanDiff = True : i += 1
                Case "--raceanim" : a.RaceAnim = True : i += 1
                Case "--racecompat" : a.RaceCompat = True : i += 1
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
                Case "--paritygate" : a.ParityGate = True : i += 1
                Case "--dump-golden" : a.DumpGolden = True : i += 1
                Case "--facegengate" : a.FaceGenGate = True : i += 1
                Case "--fggsample"
                    Dim n As Integer : If Integer.TryParse(v, n) Then a.FaceGenGateSample = Math.Max(0, n)
                    i += 2
                Case "--findfile" : a.FindFile = v : i += 2
                Case "--provenance" : a.Provenance = True : i += 1
                Case "--dumpref" : a.DumpRef = v : i += 2
                Case "--nifdump" : a.NifDump = v : i += 2
                Case "--nifslots" : a.NifSlots = v : i += 2
                Case "--estimatesclp" : a.EstimateSclp = v : i += 2
                Case "--sclpdiag" : a.SclpDiag = v : i += 2
                Case "--shapefilter" : a.ShapeFilter = v : i += 2
                Case "--sclpbatch" : a.SclpBatch = v : i += 2
                Case "--binddiff" : a.BindDiff = v : i += 2
                Case "--ba2extract" : a.Ba2Extract = v : i += 2
                Case "--animsynccheck" : a.AnimSyncCheck = v : i += 2
                Case "--catprofile" : a.CatProfile = True : i += 1
                Case "--neckseam" : a.NeckSeam = True : i += 1
                Case "--outfitscan" : a.OutfitScan = v : i += 2
                Case "-h", "--help" : PrintUsage() : Return Nothing
                Case Else
                    Console.Error.WriteLine($"Unknown arg: {args(i)}") : PrintUsage() : Return Nothing
            End Select
        End While
        If a.ListPath = "" AndAlso (a.Esp = "" OrElse a.Edid = "") AndAlso a.DdsProbe = "" AndAlso a.RecScan = "" AndAlso Not a.MeshCollide AndAlso a.DumpAcc = "" AndAlso Not a.TexSlotDiff AndAlso Not a.ShapeOrder AndAlso Not a.TintCountScan AndAlso Not a.AlphaGateScan AndAlso Not a.TtedScan AndAlso Not a.ScanDiff AndAlso Not a.RaceAnim AndAlso Not a.RaceCompat AndAlso Not a.MountValidate AndAlso a.FindHkx = "" AndAlso a.ChunkCompare = "" AndAlso a.DumpBehavior = "" AndAlso Not a.HkxCoverage AndAlso a.KwType = "" AndAlso Not a.StateMap AndAlso Not a.ClipResolve AndAlso a.HkxBone = "" AndAlso a.ClipBase = "" AndAlso a.FindFile = "" AndAlso a.NifDump = "" AndAlso a.NifSlots = "" AndAlso a.AnimSyncCheck = "" AndAlso a.BlendHintScan = "" AndAlso Not a.CatProfile AndAlso a.DumpRef = "" AndAlso a.EstimateSclp = "" AndAlso a.SclpDiag = "" AndAlso a.SclpBatch = "" AndAlso a.BindDiff = "" AndAlso a.Ba2Extract = "" AndAlso Not a.SseCompareBatch AndAlso Not a.VertexBatch AndAlso a.PosDump = "" AndAlso a.MeshShaders = "" AndAlso a.CompareFiles = "" AndAlso a.DumpNif = "" AndAlso a.SkinCheck = "" AndAlso a.OutfitScan = "" AndAlso Not a.FaceGenGate AndAlso Not a.ParityGate Then
            Console.Error.WriteLine("Missing --esp and --edid (or use --list).") : PrintUsage() : Return Nothing
        End If
        ' --rawdds + --ddscompare: COMBINACION DELIBERADA, marcada a los gritos (no abortada).
        ' Verificado en el codigo del comparador antes de habilitarla: CompareFo4FaceCustomizationDds
        ' DECODIFICA los dos DDS a RGBA y compara pixel a pixel; el unico abort es por DIMENSIONES, no por
        ' formato ⇒ hornear sin comprimir NO deja la comparacion sin medir ni inventa una categoria falsa.
        ' Lo que SI cambia es el piso: desaparece el ruido de NUESTRO codec y queda solo el del CK, asi que
        ' el RMS resultante NO es comparable contra un baseline hecho con BC3/BC5.
        ' Se marca en TRES lugares (aca, en el banner y en el reporte agregado) para que ningun numero pueda
        ' leerse sin el contexto.
        If a.RawDds AndAlso a.DdsCompare Then
            Console.Error.WriteLine("################################################################")
            Console.Error.WriteLine("## --rawdds + --ddscompare: the DDS are baked UNCOMPRESSED.")
            Console.Error.WriteLine("## The RMS of this run is NOT comparable against BC3/BC5 baselines:")
            Console.Error.WriteLine("## only the CK's codec noise remains, not ours.")
            Console.Error.WriteLine("################################################################")
        End If
        Return a
    End Function

    Private Sub PrintUsage()
        Console.WriteLine("FO4_FaceTint_CLI (--esp <plugin> --edid <EditorID> | --list <file>) [options]")
        Console.WriteLine("  --list <file>          one line per NPC: 'esp|edid' or just 'edid' (uses --esp). '#'=comment")
        Console.WriteLine("  --esp <plugin>         NPC plugin (default for --list lines without esp)")
        Console.WriteLine("  --config <config.json> read the FaceTint sections from the app's config.json")
        Console.WriteLine("  --convention <f.json>  override of Setting_FaceTintConvention")
        Console.WriteLine("  --sort <f.json>        override of Setting_FaceTintSort")
        Console.WriteLine("  --data <Data\ path>    Data path (default: config.json)")
        Console.WriteLine("  --out <dir>            output folder (default: FaceCustomization\<plugin>)")
        Console.WriteLine("  --defaults             IGNORE npc_config.json; run with NPC_Config's COMPILED defaults (reproducible sweeps)")
        Console.WriteLine("  --ddscompare           compare PIXELS vs CK (SSE: facetint _d | FO4: FaceCustomization _d/_msn/_s)")
        Console.WriteLine("  --sweep <dir-configs>  sweeps each config .json in the dir vs CK (ONE load) and ranks by Normal")
        Console.WriteLine("  --dump <dir>           also writes the MASKS (inputs: BASEIN + layers + swaps + regionmasks + LUT) to <dir>\<localId>")
        Console.WriteLine("Output per NPC: <localId>_d_3.tga / _msn_3.tga / _s_3.tga")
        Console.WriteLine("Batch (--list): mounts plugins+archives ONCE; each DDS is decoded only once.")
    End Sub

End Module
