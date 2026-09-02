Option Strict On
Imports System.IO
Imports System.Threading
Imports BSA_BA2_Library_DLL.BethesdaArchive.Core
Imports FO4_Base_Library
Imports FO4_Base_Library.Archives

''' <summary>
''' Batches the FaceGen loose files produced by <c>FaceGenBuilder.BuildCharGen</c> (FO4: 1 NIF + 3
''' FaceCustomization DDS per NPC; SSE: 1 NIF + 1 FaceTint DDS — see <see cref="FaceGenFileSpecs"/>)
''' into the archive set anchored to the Save ESP plugin.
'''
''' Pattern mirrors Wardrobe_Manager.WM_PackUnpack.Pack (the proven shape):
'''   1) Walk the bundles → flat list of <see cref="LooseFileRef"/> (sourcePath + canonical entryPath
'''      + isTexture + debugSandbox). No bytes loaded yet.
'''   2) Micro-batch parallel compress (<see cref="MICRO_BATCH"/> entries per pass via Parallel.For).
'''      Transient peak RAM is bounded by MICRO_BATCH × max-file-size; workers free their slot as
'''      soon as the compressed VirtualEntry is folded into the main accumulator.
'''   3) Accumulator (chunkEntries) collects pre-compressed entries until the cumulative compressed
'''      size would exceed <see cref="MEMORY_CAP_BYTES"/>. At that point the buffer is flushed via
'''      a SINGLE <see cref="ArchivePackager.Pack"/> call (anchored to the same plugin, never split),
'''      then PreCompressedBytes are nulled out so the next chunk starts with clean memory.
'''   4) Final flush after the walk. Loose sources of committed refs are deleted (including
'''      the _2 sandbox files when <c>DebugSandbox</c> is True — BA2 is the source of truth
'''      once a bundle commits, regardless of which suffix the disk file carried).
'''
''' Override semantics preserved by ArchivePackager.Pack on every flush:
'''   - Existing BA2 entries not in the bundle: stream-copy.
'''   - Bundle entries that overlap with the existing archive: ComputeDiff CRC32 → stream-copy
'''     unchanged or rewrite changed.
'''   - Re-saving the same NPC: the 4 paths overlap → stream-copy / rewrite per ComputeDiff.
'''
''' Single archive anchor (Overflow=ThrowOnExceed). Unlike WM, NPC_Manager never splits across
''' numbered plugins — the Save ESP IS the anchor and the user expects one set of BA2s next to it.
''' If the accumulated archive (existing + new) exceeds the FO4 3 GB cap inside the packager,
''' Pack throws — caller surfaces the error in the save summary.
''' </summary>
Public Module NpcFaceGenPacker

    ''' <summary>One baked NPC's identity. The packer derives the loose paths from these three
    ''' fields via <see cref="FaceGenFileSpecs"/>, the same naming FaceGenBuilder used to write them.
    ''' Public (not Friend) so it can be exposed through the Public SaveContext delegates
    ''' (NpcOverrideSaver.SaveContext.RunChargenBake / RunChargenPackBatch).</summary>
    Public Class BakedNpcBundle
        ''' <summary>Plugin name segment in the FaceGen path (NPC's source master, e.g.
        ''' "Fallout4.esm" or the auto-gen plugin that owns the override).</summary>
        Public Property OriginPlugin As String = ""
        ''' <summary>NPC FormID with the master-index high byte cleared (matches the
        ''' FormID8hex naming FaceGenBuilder used for the loose files).</summary>
        Public Property FormIdLow As UInteger
        ''' <summary>True when the bake ran with FaceGenBuilder.DebugMode=True (loose written
        ''' with a _2 suffix). The packer reads the _2 files but stores entries under canonical
        ''' names. The _2 sources are deleted after a successful pack just like canonical ones
        ''' (2026-05-26 change — BA2 is the post-pack source of truth).</summary>
        Public Property DebugSandbox As Boolean
        ''' <summary>Sueltos que el bake de ESTE NPC dejo fuera del layout por NPC y que su NIF
        ''' referencia (rutas relativas a Data). Hoy: el clon de UV vanilla de la nuca de gul, que
        ''' vive en una raiz que se inventa la app y que por lo tanto no trae ni el juego ni un mod.
        ''' <para>Va POR BUNDLE y no en una lista de sesion: asi un bundle DESCARTADO no aporta los
        ''' suyos, un render de fondo no puede colar los de una NPC que no se esta guardando, y dos
        ''' Save con los mismos inputs producen el MISMO archive.</para></summary>
        Public Property ExtraLooseFiles As List(Of String)

        ''' <summary>Las salidas de textura de cara que el bake de ESTE NPC declaró que le correspondía
        ''' producir. El packer exige exactamente ésas y ninguna más. Viene de
        ''' <c>FaceGenBuilder.BuildResult.SalidasDeTexturaDeclaradas</c>.
        ''' <para>Friend y no Public a proposito: el tipo vive en el modulo Friend FaceGenPaths, y una
        ''' propiedad Public de una clase Public no puede exponerlo (BC30909). La CLASE sigue siendo Public
        ''' porque la nombra Tools\ChargenFlagSaveGate desde otro ensamblado.</para></summary>
        Friend Property SalidasDeTexturaDeclaradas As FaceGenPaths.SalidaDeTexturaDeCara

        ''' <summary>El bake POBLÓ <see cref="SalidasDeTexturaDeclaradas"/>. Existe porque el default de un
        ''' enum de banderas es cero, o sea "no declaró NADA", que como default es FAIL-OPEN: un camino
        ''' futuro que arme bundles y se olvide de poblarla dejaría de exigir las texturas de cara EN
        ''' SILENCIO. Con este flag apagado el packer exige TODO.
        ''' <para>OJO: "exige todo" NO es identico al comportamiento previo. Los dos specs per-NPC de SSE
        ''' llevaban IsOptional=True y no se exigian NUNCA; con la bandera apagada pasan a exigirse, y en un
        ''' NPC vanilla de Skyrim (sin overlays, el bake no los produce) eso descarta el bundle entero. Hoy
        ''' no hay camino que llegue con la bandera apagada -se prende en la cola comun de BuildCharGen, y
        ''' sin Success no se arma bundle-, asi que es LATENTE; si algun dia se arma un bundle por otra via,
        ''' hay que resolver esto antes.</para></summary>
        Friend Property DeclaracionDeSalidasPoblada As Boolean

        ''' <summary>Las salidas de textura de cara que ESTE bake escribio, por IDENTIDAD -el
        ''' <c>FaceGenBuilder.BuildResult.SalidasDeTexturaEscritas</c> del mismo NPC-.
        ''' <para>Contesta la SEGUNDA pregunta del packer, que la declaracion no contesta: no "si falta,
        ''' es error?" sino "este archivo es de este horneado?". Sin esto, un DDS sobreviviente de un bake
        ''' anterior se colaba al archive por el solo hecho de existir, y dos Save con los mismos inputs
        ''' podian dar archives distintos.</para>
        ''' <para>IDENTIDAD y no rutas: el bake escribe bajo <c>BakeOutputRoot.Current()</c>, que
        ''' <c>--outdir</c> MUEVE, y el packer arma las suyas con el <c>dataDir</c> que le pasa el Save.
        ''' Comparar strings de dos raices distintas da falso siempre, y como esto pesa sobre lo REQUERIDO
        ''' eso tumbaba todos los bundles.</para></summary>
        Friend Property SalidasDeTexturaEscritas As FaceGenPaths.SalidaDeTexturaDeCara
    End Class

    ''' <summary>One file of an NPC's FaceGen bake, as Data-relative paths. <c>Source</c> carries the
    ''' _2 debug suffix when the bake ran in DebugMode; <c>Entry</c> is always the canonical name the
    ''' engine (and the archive) expects.</summary>
    Friend Structure FaceGenFileSpec
        Public Source As String
        Public Entry As String
        Public IsTexture As Boolean
        ''' <summary>Qué salida de textura de cara ES este spec, y con eso, CUÁNDO se exige.
        ''' <para><c>Ninguna</c> ⇒ se exige SIEMPRE **y** queda exento del chequeo de pertenencia. Hoy lo
        ''' lleva SÓLO el NIF de FaceGeom, que el bake escribe siempre que llega a Success, así que su
        ''' presencia en disco ya prueba que es de este horneado.</para>
        ''' <para>Cualquier otro valor ⇒ se exige sólo si el bake DECLARÓ esa salida en el bundle… SALVO las
        ''' de <see cref="FaceGenPaths.SalidasSiempreRequeridas"/> —hoy el facetint de SSE—, que se exigen
        ''' SIEMPRE y ADEMÁS tienen que ser de este horneado. Las dos preguntas se contestan por separado:
        ''' <see cref="FaceGenPaths.SeExigeSiempre"/> y el tag.</para>
        ''' <para>⛔ Reemplazó a un `IsOptional As Boolean` que convivía con esto: eran DOS predicados para
        ''' la MISMA pregunta ("¿lo exijo?"), y el booleano ganaba en silencio. Los specs que llevaba
        ''' (diffuse y normal per-NPC de SSE) hoy no los declara nadie ⇒ nunca se exigen, que es
        ''' exactamente lo que hacía el booleano. La diferencia es que ahora hay UN predicado y declararlos
        ''' el día que se mida es una línea, no un cambio de semántica.</para></summary>
        Public Salida As FaceGenPaths.SalidaDeTexturaDeCara
    End Structure

    ''' <summary>Data-relative file specs for one NPC's FaceGen bake, GAME-AWARE — the single source of
    ''' truth for what <c>FaceGenBuilder</c> wrote and what the packer / delete flow must look for:
    '''   FO4: FaceGeom NIF + 3 FaceCustomization DDS (_d/_msn/_s), all with a _2 debug variant.
    '''   SSE: FaceGeom NIF + 1 FaceGenData\FaceTint DDS, also with a _2 debug variant. Matches the vanilla
    '''        CK layout in the BSA (textures\actors\character\facegendata\facetint\&lt;plugin&gt;\&lt;id&gt;.dds,
    '''        512² DXT5, _d only) and what <c>FaceGenBuilder.WriteSseFacetintDds</c> writes.
    ''' The _2b GPU-sandbox dumps are deliberately absent: they never enter an archive and are not deleted
    ''' with the bake — they exist for CPU-vs-GPU inspection.</summary>
    Friend Function FaceGenFileSpecs(game As Config_App.Game_Enum, originPlugin As String,
                                     formIdLow As UInteger, debugSandbox As Boolean) As List(Of FaceGenFileSpec)
        Dim specs As New List(Of FaceGenFileSpec)
        If String.IsNullOrEmpty(originPlugin) Then Return specs
        Dim hex = formIdLow.ToString("X8")

        Dim geomDir = FaceGenPaths.GeomDir(originPlugin)
        specs.Add(New FaceGenFileSpec With {
            .Source = geomDir & hex & If(debugSandbox, "_2.nif", ".nif"),
            .Entry = geomDir & hex & ".nif",
            .IsTexture = False})

        If game = Config_App.Game_Enum.Skyrim Then
            Dim tintDir = FaceGenPaths.TexturaDir(FaceGenPaths.CanalTint, originPlugin)
            ' Facetint: SIEMPRE per-NPC <id>.dds canónico. El engine ARMA `FaceTint\<plugin>\<id>.dds` él mismo
            ' (BuildFaceTintPath, RE SkyrimSE.exe) e IGNORA el slot 6 del NIF ⇒ NO se puede compartir ni omitir; si
            ' falta, el tint del material queda NULL → cara brown. Para NPCs plegados es un gris neutral (fgTint≈1).
            specs.Add(New FaceGenFileSpec With {
                .Source = tintDir & hex & If(debugSandbox, "_2.dds", ".dds"),
                .Entry = tintDir & hex & ".dds",
                .IsTexture = True,
                .Salida = FaceGenPaths.SalidaDeTexturaDeCara.SseFaceTint})
            ' (Eliminado el spec del `facedetailneutral.dds` COMPARTIDO por plugin: el fold ya no neutraliza el
            '  slot 3 — deja el detail REAL y pre-compensa el amplify en el diffuse. Ver
            '  SseFaceGenBaker.PreCompensateEngineChain. Era el único artefacto compartido entre NPCs/ESPs.)
            ' Diffuse per-NPC de la cabeza — sólo cuando el NPC tiene overlays de cara / máscaras skee
            ' horneadas (FaceGenBuilder.WriteSseFaceDiffuseWithOverlays). Ausente en un NPC vanilla.
            ' Lleva TAG en vez del viejo IsOptional=True, y hoy NADIE lo declara ⇒ nunca se exige, que es
            ' EXACTAMENTE lo que hacía IsOptional. La diferencia es que ahora hay UN solo predicado: el día
            ' que se pueda medir cuándo el bake SSE se compromete a emitirlo, declararlo es una línea y no
            ' un cambio de semántica.
            Dim diffDir = FaceGenPaths.TexturaDir(FaceGenPaths.CanalDiffuse, originPlugin)
            specs.Add(New FaceGenFileSpec With {
                .Source = diffDir & hex & If(debugSandbox, "_2.dds", ".dds"),
                .Entry = diffDir & hex & ".dds",
                .IsTexture = True,
                .Salida = FaceGenPaths.SalidaDeTexturaDeCara.SseHeadDiffuse})
            ' Normal (_msn) per-NPC — sólo cuando algún overlay de cara aporta normal. Mismo criterio.
            Dim normDir = FaceGenPaths.TexturaDir(FaceGenPaths.CanalNormal, originPlugin)
            specs.Add(New FaceGenFileSpec With {
                .Source = normDir & hex & If(debugSandbox, "_2.dds", ".dds"),
                .Entry = normDir & hex & ".dds",
                .IsTexture = True,
                .Salida = FaceGenPaths.SalidaDeTexturaDeCara.SseHeadNormal})
        Else
            ' Se RECORRE la tabla de FaceGenPaths en vez de repetir acá el conjunto {slot, sufijo}: el bake
            ' arma su plan de slots de la MISMA tabla, así que un canal nuevo es una fila y no dos listas que
            ' hay que acordarse de tocar juntas. La carpeta sale de CustomizacionDir, que existe para eso y
            ' esta rama esquivaba con un literal propio.
            For Each salidaFo4 In FaceGenPaths.SalidasFo4
                specs.Add(New FaceGenFileSpec With {
                    .Source = FaceGenPaths.CustomizacionDds(originPlugin, formIdLow, salidaFo4, debugSandbox),
                    .Entry = FaceGenPaths.CustomizacionDds(originPlugin, formIdLow, salidaFo4, False),
                    .IsTexture = True,
                    .Salida = salidaFo4.Salida})
            Next
        End If
        Return specs
    End Function

    ''' <summary>The CANONICAL archive entry paths (as stored inside the archive — no _2 debug suffix) for one
    ''' NPC's FaceGen bake: the FaceGeom NIF (→ Main archive) + the texture(s) (→ Textures archive). Count and
    ''' texture layout are game-aware — see <see cref="FaceGenFileSpecs"/>, the same source <see cref="PackBatch"/>
    ''' uses for its bundle spec. Used by the "mark to delete" flow to build the ExcludePaths passed to
    ''' <see cref="PackBatch"/>. Empty when the origin plugin can't be resolved.</summary>
    ''' <summary>Rutas relativas a Data que el NIF horneado de este NPC referencia y que NO trae ni el
    ''' juego ni ningun mod, porque las INVENTA la app. Hoy es una sola familia: el clon de UV vanilla
    ''' de la nuca de gul (<c>NpcMaterialResolver.HeadRearClonedTextureRoot</c>).
    ''' <para>MEDIDO: 24 de 2877 NIF horneados del corpus del usuario las referencian. Sin entregarlas,
    ''' el mod se publica con un NIF apuntando a texturas que no existen en la maquina del que instala
    ''' y esa nuca queda sin textura.</para>
    ''' <para>NO salen de <see cref="FaceGenFileSpecs"/> a proposito: esa lista sirve TAMBIEN al flujo de
    ''' borrado, y este activo es COMPARTIDO por todas las gules de la raza — entregarlo si, borrarlo no.
    ''' El camino del BA2 las obtiene del bundle, que las trae del bake; este camino no puede, porque
    ''' cuando se exporta el FOMOD el bake ya corrio (tal vez en otra sesion) y no queda nada en memoria.
    ''' Se averigua entonces del NIF EN DISCO, que es la misma invariante: se entrega lo que el NIF
    ''' referencia.</para>
    ''' <para>Es un ESCANEO del texto del NIF, no un parseo: las rutas de textura se guardan como cadenas
    ''' ASCII y lo unico que se busca es un prefijo de raiz que controla la app, asi que no hace falta
    ''' abrir el formato. Devuelve solo lo que ademas EXISTE en disco, igual que el resto de esta rama.</para></summary>
    Public Function InventedLooseFilesForNpc(npcFormID As UInteger, pluginManager As PluginManager,
                                             dataDir As String) As List(Of String)
        Dim salida As New List(Of String)
        If String.IsNullOrEmpty(dataDir) OrElse pluginManager Is Nothing Then Return salida
        Dim origin = pluginManager.GetOriginatingPluginName(npcFormID)
        If String.IsNullOrEmpty(origin) Then Return salida
        Dim local = PluginManager.ToFaceGenLocalFormID(npcFormID)
        ' Suelto a proposito: esta rama del FOMOD entrega SUELTOS, y su propio comentario dice que
        ' enumera "every FaceGen file that EXISTS on disk". Si el NIF esta adentro de un BA2/BSA, el
        ' FOMOD no pasa por aca sino por la rama del archive set, que empaqueta los .ba2 ENTEROS y ya
        ' se lleva el clon adentro. Leerlo por el diccionario seria PEOR: resolveria NIF que viven en
        ' archives de OTROS mods y sumaria al manifiesto texturas que no son de este.
        Dim nifAbs = Path.Combine(dataDir, FaceGenPaths.GeomNif(origin, local))
        If Not File.Exists(nifAbs) Then Return salida
        Try
            Dim texto = Text.Encoding.ASCII.GetString(File.ReadAllBytes(nifAbs))
            Dim patron = Text.RegularExpressions.Regex.Escape(NpcMaterialResolver.HeadRearClonedTextureRoot) &
                         "[^\x00-\x1F""<>|*?]+?\.dds"
            For Each m As Text.RegularExpressions.Match In
                Text.RegularExpressions.Regex.Matches(texto, patron, Text.RegularExpressions.RegexOptions.IgnoreCase)
                Dim rel = m.Value
                If salida.Contains(rel, StringComparer.OrdinalIgnoreCase) Then Continue For
                If File.Exists(Path.Combine(dataDir, rel)) Then salida.Add(rel)
            Next
        Catch ex As Exception
            ' Best-effort, igual que el resto de esta rama del FOMOD: si el NIF no se puede leer, el
            ' manifiesto sale sin estas texturas y el usuario lo ve en la grilla como ausente.
            Dim nL = nifAbs, mL = ex.Message
            Logger.LogLazy(Function() $"[FOMOD] no se pudo escanear '{nL}' por texturas inventadas: {mL}")
        End Try
        Return salida
    End Function

    Public Function CanonicalFaceGenEntryPathsForNpc(npcFormID As UInteger, pluginManager As PluginManager,
                                                     game As Config_App.Game_Enum) As List(Of String)
        Dim origin = pluginManager.GetOriginatingPluginName(npcFormID)
        If String.IsNullOrEmpty(origin) Then Return New List(Of String)()
        Dim local = PluginManager.ToFaceGenLocalFormID(npcFormID)
        Return FaceGenFileSpecs(game, origin, local, debugSandbox:=False).
               Select(Function(s) s.Entry).ToList()
    End Function

    ''' <summary>Aggregate result of <see cref="PackBatch"/> across one or more flushes.</summary>
    Friend Class PackResult
        Public Property Success As Boolean
        ''' <summary>Archive paths that were (re)written across all flushes.</summary>
        Public ReadOnly WrittenArchives As New List(Of String)
        ''' <summary>Archive paths the packer reported as skipped (byte-identical) on any flush.</summary>
        Public ReadOnly SkippedArchives As New List(Of String)
        ''' <summary>Loose files removed from disk after their containing flush succeeded.</summary>
        Public ReadOnly DeletedLoose As New List(Of String)
        ''' <summary>Now-empty directories pruned (up to, but excluding, Data) after deletions.</summary>
        Public ReadOnly RemovedDirs As New List(Of String)
        ''' <summary>How many flushes were committed to disk (one ArchivePackager.Pack call each).</summary>
        Public Property FlushesCommitted As Integer
        ''' <summary>How many input bundles produced at least one VirtualEntry that landed in a flush.
        ''' A bundle is "committed" iff every loose file its game's layout calls for existed on disk
        ''' AND all of its entries were part of a successful flush.</summary>
        Public Property BundlesCommitted As Integer
        ''' <summary>Loose source paths that the packer expected (from bundles) but did not find
        ''' on disk. Each missing source = one of the bake outputs (see FaceGenFileSpecs) for some NPC
        ''' that <c>FaceGenBuilder.BuildCharGen</c> reported as Success but did not actually produce.
        ''' Surfacing the count in the summary helps the user see how many bundles were dropped
        ''' before flush.</summary>
        Public ReadOnly MissingSources As New List(Of String)
        ''' <summary>Bundles descartados ENTEROS por faltarles un archivo REQUERIDO, ya formateados
        ''' "&lt;plugin&gt; 0x&lt;formId&gt;: faltan N archivo(s), p.ej. '&lt;nombre&gt;'". Existe porque el mensaje al usuario
        ''' era "N NPCs failed to pack (M files unaccounted for)" — sin decir QUÉ NPC ni QUÉ archivo — y el
        ''' desglose por path sólo vivía en el log, que en Release no se escribe (Logger.Enabled=False). Con esto
        ''' el MessageBox del save puede nombrarlos, como ya hace el batch loose.</summary>
        Public ReadOnly FailedBundles As New List(Of String)
        ''' <summary>Free-form failure message when Success = False.</summary>
        Public Property ErrorMessage As String = ""

        ''' <summary>Aviso de archives que no se pudieron VOLVER A MONTAR después de empaquetar. Vacío si
        ''' no hubo ninguno.
        ''' <para>⛔ SEPARADO DE <see cref="ErrorMessage"/> A PROPÓSITO, y la distinción es de conducta:
        ''' <see cref="Success"/> significa <b>"el archive en disco quedó bien"</b>. Un fallo de remonte
        ''' NO lo toca — los bytes están escritos y el juego los carga; lo que queda roto es la vista en
        ''' memoria de ESTA sesión, hasta un refresh. Mezclarlo con el error del pack ponía
        ''' <c>Success = False</c> sobre un pack correcto y hacía que el llamador cortara con un
        ''' early-return, perdiendo el diagnóstico por NPC en el caso mixto.</para></summary>
        Public Property RemountWarning As String = ""
    End Class

    ''' <summary>Progress phases reported through the optional progress callback.</summary>
    Friend Enum PackPhase
        BuildingBundle
        WritingArchive
        DeletingLoose
        Done
    End Enum

    ''' <summary>Lightweight progress envelope.</summary>
    Friend Class PackProgress
        Public Phase As PackPhase
        Public Detail As String = ""
        Public Current As Integer
        Public Max As Integer
    End Class

    ' --- Tunables -------------------------------------------------------------------------------
    ' Compressed-bytes ceiling for the accumulator. When folding the next compressed entry would
    ' cross this, the buffer is flushed to disk first. 500 MB lands ~300–3000 NPC bundles in a
    ' single flush (typical NPC compressed total: 150 KB–1.5 MB), which keeps almost every
    ' real-world save to one ArchivePackager.Pack call. Mass batches that exceed it incur
    ' K flushes (K = ceil(totalCompressedBytes / MEMORY_CAP_BYTES)) but never per-NPC.
    Private Const MEMORY_CAP_BYTES As Long = 500L << 20

    ' Per-pass parallel-compress micro-batch. Bounds transient RAM during compression to
    ' MICRO_BATCH × max-file-size across all worker threads. Lower than WM_PackUnpack's 64 because
    ' FaceGen DDS are small and compression overhead dominates — 32 is the sweet spot for typical
    ' face textures (256×256 to 1024×1024 BC1/BC3 mipped).
    Private Const MICRO_BATCH As Integer = 32

    ' Tope por archive adentro del packer. Acota UN SOLO archive (el existente + el bundle nuevo), y es
    ' independiente de MEMORY_CAP_BYTES, que acota el working set por flush.
    ' ⛔ Acá decía «Engine is unstable past 4 GB; 3 GB leaves headroom». Esa frase es la que
    ' `PackagerRequest` declara FALSA y reemplaza por la cita canónica: el límite duro es el OFFSET que
    ' el formato puede expresar, `BSA_MAX_OFFSET = High(Integer)` = 2 GiB−1 para el BSA
    ' (`3rd party references\TES5Edit\Core\wbBSArchive.pas:162`, y es lo que `DefaultSplitSize` devuelve
    ' para `baSSE`, `:1000-1005`); el BA2 usa offsets de 64 bits y no tiene ese techo. «4 GB» no sale de
    ' ningún lado. El 3 GiB de acá es el tope BLANDO del formato, y vive UNA sola vez en
    ' `PackagerRequest.MaxArchiveBytesDefault` — con su razón, en `ArchivePackager.vb`. No se repite acá.
    Private ReadOnly MAX_ARCHIVE_BYTES As Long = BSA_BA2_Library_DLL.BethesdaArchive.Core.PackagerRequest.MaxArchiveBytesDefault

    ''' <summary>One loose file in the bundle, with its canonical BA2 entry name. SourcePath is
    ''' the actual on-disk file (carries _2 suffix when bake ran in DebugMode); EntryPath is the
    ''' canonical name the entry takes inside the BA2 (never _2). The DebugSandbox flag is kept
    ''' on the ref for traceability but no longer gates deletion (post 2026-05-26).</summary>
    Private Class LooseFileRef
        Public Property SourcePath As String            ' actual file on disk (may carry _2 suffix)
        Public Property EntryPath As String             ' canonical path inside the BA2 (never _2)
        Public Property IsTexture As Boolean
        Public Property DebugSandbox As Boolean
        ''' <summary>El suelto NO se borra despues de empaquetar. Para activos COMPARTIDOS, que no son
        ''' de un NPC: hoy, el clon de UV vanilla de la nuca de gul. El paso 5 borra el suelto Y hace
        ''' RemoveDictionaryEntry por cada ref committeado; hacerle eso al clon lo sacaria del disco y
        ''' del diccionario, rompiendo el render y a TODAS las otras gules que lo comparten.</summary>
        Public Property ConservarSuelto As Boolean
    End Class

    ''' <summary>Pack the FaceGen loose for <paramref name="bundles"/> into the BA2 set anchored
    ''' to <paramref name="anchorPluginPath"/>. Loose files are read from disk and compressed in
    ''' bounded-memory micro-batches; the accumulator flushes whenever the running compressed
    ''' total would exceed <see cref="MEMORY_CAP_BYTES"/>. Per-flush ArchivePackager.Pack call
    ''' preserves override semantics (CRC32 diff against existing archive).
    ''' </summary>
    ''' <param name="anchorPluginPath">Full path to the Save ESP plugin written this same
    ''' transaction. Its file name (without extension) becomes the BA2 ModBaseName, so the
    ''' engine auto-loads "&lt;name&gt; - Main.ba2" + "&lt;name&gt; - Textures.ba2" alongside the plugin.</param>
    ''' <param name="dataDir">FO4 Data folder.</param>
    ''' <param name="game">Game variant. Only Fallout4 is exercised today; Skyrim path is provided
    ''' for parity with WM_PackUnpack and uses BSA / LZ4 frame.</param>
    ''' <param name="ba2Version">Header version for the FO4 BA2 writer. Caller must NOT pass 0
    ''' (the loose-only sentinel); that case is decided at the orchestrator level (no PackBatch call).</param>
    ''' <param name="bundles">One entry per baked NPC. The packer derives the loose paths from
    ''' (game, OriginPlugin, FormIdLow, DebugSandbox) via <see cref="FaceGenFileSpecs"/> — the same
    ''' naming FaceGenBuilder applied at bake.</param>
    ''' <param name="progress">Optional progress callback. Invoked synchronously on the worker
    ''' thread; the caller's IProgress(Of T) wrapper marshals back to the UI thread.</param>
    ''' <param name="ct">Cancellation token. Checked at safe checkpoints (between micro-batches
    ''' and before each flush) — never mid-flush, so the on-disk archive stays consistent.</param>
    ''' <param name="excludeEntries">Optional canonical archive entry paths (as stored inside the BA2, e.g.
    ''' "Meshes\Actors\Character\FaceGenData\FaceGeom\&lt;plugin&gt;\&lt;id&gt;.nif") to REMOVE from the app's
    ''' target archive set — the "mark to delete" flow strips a removed NPC's stale FaceGen bake. Applied on
    ''' the same Pack calls that write new bakes; when there are NO new bakes (delete-only) a single removal-only
    ''' Pack runs so the drop still happens. Nothing/empty = no removals (existing callers unaffected).</param>
    Friend Function PackBatch(anchorPluginPath As String,
                              dataDir As String,
                              game As Config_App.Game_Enum,
                              ba2Version As UInteger,
                              bundles As IReadOnlyList(Of BakedNpcBundle),
                              Optional progress As Action(Of PackProgress) = Nothing,
                              Optional ct As CancellationToken = Nothing,
                              Optional excludeEntries As IEnumerable(Of String) = Nothing) As PackResult
        Dim result As New PackResult()

        ' Canonical entry paths to strip from the target archive set (mark-to-delete). Empty = no removals.
        Dim excludeSet As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If excludeEntries IsNot Nothing Then
            For Each e In excludeEntries
                If Not String.IsNullOrEmpty(e) Then excludeSet.Add(e)
            Next
        End If

        If String.IsNullOrEmpty(anchorPluginPath) OrElse Not File.Exists(anchorPluginPath) Then
            result.ErrorMessage = $"Anchor plugin not found: '{anchorPluginPath}'."
            Return result
        End If
        If String.IsNullOrEmpty(dataDir) OrElse Not Directory.Exists(dataDir) Then
            result.ErrorMessage = $"Data folder not found: '{dataDir}'."
            Return result
        End If
        ' Nothing to pack AND nothing to remove → true no-op. With exclusions we still proceed (delete-only).
        If (bundles Is Nothing OrElse bundles.Count = 0) AndAlso excludeSet.Count = 0 Then
            result.Success = True
            Report(progress, PackPhase.Done, "No bundles to pack.", 0, 0)
            Return result
        End If
        If bundles Is Nothing Then bundles = New List(Of BakedNpcBundle)()

        ' --- Step 1: walk bundles → flat LooseFileRef list -----------------------------------
        ' One bundle = the game's FaceGenFileSpecs refs (NIF first, then the DDS in stable order).
        ' Refs whose source is missing on disk are dropped with a warning into the result; missing
        ' sources are a bake-phase bug (FaceGenBuilder should always produce every file), surfaced
        ' here so the user sees it but the rest of the batch still ships.
        Dim allRefs As New List(Of LooseFileRef)
        Dim refToBundleIdx As New List(Of Integer)
        Dim bundleRefCounts(bundles.Count - 1) As Integer   ' how many refs per bundle made it in
        Dim bundleExpected(bundles.Count - 1) As Integer    ' how many the game's layout calls for
        Dim missingSources As New List(Of String)

        ' B1 — EL BUNDLE ES ATÓMICO: o entran TODOS sus archivos requeridos, o NO ENTRA NINGUNO.
        ' EL BUG: el `Continue For` de un spec requerido faltante era POR SPEC, así que los demás archivos del
        ' MISMO NPC se empaquetaban igual. El bundle no contaba como committed, pero (a) PackResult.Success seguía
        ' True, (b) sus refs SÍ llegaban a `committedRefs` y por lo tanto (c) sus sueltos se BORRABAN en el paso 5.
        ' Caso concreto y medido en SSE: si WriteSseFacetintDds bailó, falta `FaceTint\<id>.dds` (spec REQUERIDO) y
        ' el NIF entraba al BSA SIN su facetint ⇒ ApplyFaceTintToHeadMaterial (0x1403BC400) hace LoadTexture NULL,
        ' [mat+0xA0]=NULL, CARA MARRÓN in-game. Y como los sueltos ya se borraron, tampoco se puede re-empacar.
        ' Ahora se resuelve la lista COMPLETA de specs primero y, si falta alguno requerido, el bundle entero se
        ' descarta ANTES de agregar un solo ref: el archive nunca queda a medias y los sueltos sobreviven para
        ' reintentar. El save lo reporta con nombre y archivo (ver failedBundles).
        For bi = 0 To bundles.Count - 1
            Dim b = bundles(bi)
            Dim bundleSpec = FaceGenFileSpecs(game, b.OriginPlugin, b.FormIdLow, b.DebugSandbox)

            Dim pending As New List(Of LooseFileRef)
            Dim missingForThisBundle As New List(Of String)
            For Each spec In bundleSpec
                Dim sourcePath = Path.Combine(dataDir, spec.Source)

                ' ⛔ SON DOS PREGUNTAS DISTINTAS, y confundirlas ya costó una regresión:
                '   (a) "¿si falta, es un error?"      -> la contesta la DECLARACIÓN (una decisión del bake)
                '   (b) "¿este archivo es DE ESTE bake?" -> la contesta lo que el bake ESCRIBIÓ (un resultado)
                ' La primera versión de esto sólo miraba (a), y encima adentro del `If Not File.Exists`, así
                ' que un DDS sobreviviente de un horneado ANTERIOR se colaba al archive: presente ⇒ se
                ' empaquetaba, sin preguntar de quién era. Pasa de verdad cuando un NPC deja de tener head
                ' part de tipo Face — el bake de texturas no corre, el barrido de restos tampoco (vive
                ' adentro del mismo gate) y los `_d/_msn/_s` viejos quedan en disco. Antes de la ley
                ' "requerido = declarado" no se notaba porque el bundle se descartaba entero.
                ' Rompía la invariante que este mismo módulo declara: dos Save con los MISMOS inputs tienen
                ' que producir el MISMO archive.
                ' ⛔ `SeExigeSiempre` NO es "spec.Salida = Ninguna" con otro nombre, y esa diferencia ES el
                ' defecto que este bloque tuvo. Al ponerle tag al facetint de SSE -que hacía falta, para el
                ' chequeo de pertenencia de abajo- se le fue con el tag la garantía de "requerido SIEMPRE":
                ' pasó a depender de que el bake lo DECLARE, y el bake sólo lo declara al entrar a
                ' WriteSseFacetintDds, al que sólo se entra si el NPC tiene head part de tipo Face. Un NPC de
                ' SSE sin head part Face -o uno cuya shape Face se cayó por excepción de material, ruta que no
                ' depende de los datos- commiteaba el bundle SIN facetint: cara marrón in-game, y como el paso
                ' 5 ya borró los sueltos, sin forma de reintentar.
                ' La ley vive en FaceGenPaths, junto al enum, y se deriva del motor: se exige siempre lo que el
                ' motor busca por su cuenta sin leer el NIF.
                Dim requerido As Boolean =
                    (Not b.DeclaracionDeSalidasPoblada) OrElse
                    FaceGenPaths.SeExigeSiempre(spec.Salida) OrElse
                    ((b.SalidasDeTexturaDeclaradas And spec.Salida) <> FaceGenPaths.SalidaDeTexturaDeCara.Ninguna)

                ' ¿ES DE ESTE HORNEADO? Para las salidas con TAG lo dice el bake, por IDENTIDAD: están en
                ' SalidasDeTexturaEscritas si y sólo si LAS ESCRIBIÓ. Sin comparar rutas — las dos puntas
                ' las arman de raíces distintas (el bake de BakeOutputRoot.Current(), que --outdir mueve).
                ' La ÚNICA que no lleva tag es el NIF de FaceGeom: el bake no lo registra, así que para él el
                ' único indicio posible sigue siendo que el archivo esté. El facetint de SSE SÍ lleva tag y el
                ' bake SÍ lo registra (FaceGenBuilder: lo declara al entrar a WriteSseFacetintDds y lo marca
                ' escrito tras el File.Write), que es justo lo que evita que un facetint de un horneado
                ' anterior viaje al BSA con el NIF nuevo. Que se exija SIEMPRE es OTRA ley y la contesta
                ' `requerido` arriba, no ésta.
                ' ⛔ ESTO VA FUERA DE LA RAMA "no requerido", y esa es la corrección importante. Cuando el
                ' chequeo vivía sólo del lado no-requerido quedaba abierto el caso: el bake DECLARA el _msn,
                ' la escritura de ESTE horneado FALLA, y el barrido del stale tampoco puede borrar el viejo
                ' (lock o permisos). Requerido + presente ⇒ se empaquetaba el VIEJO, el bundle commiteaba
                ' como si nada y el paso 5 borraba el suelto: la textura de otro horneado quedaba como la
                ' del archive, con el NIF nuevo al lado y sin forma de reintentar.
                ' Con la regla unificada eso es lo que es: declarado y NO producido ⇒ FALTA. Tumba el bundle,
                ' conserva los sueltos y se puede reintentar.
                Dim esDeEsteBake As Boolean =
                    (spec.Salida = FaceGenPaths.SalidaDeTexturaDeCara.Ninguna) OrElse
                    ((b.SalidasDeTexturaEscritas And spec.Salida) <> FaceGenPaths.SalidaDeTexturaDeCara.Ninguna)

                Dim disponible As Boolean = esDeEsteBake AndAlso File.Exists(sourcePath)

                If Not disponible AndAlso File.Exists(sourcePath) Then
                    Dim spL = sourcePath, opL2 = b.OriginPlugin, fidL2 = b.FormIdLow, reqL = requerido
                    Logger.LogLazy(Function() $"[PACK-BATCH] {opL2} 0x{fidL2:X8}: '{Path.GetFileName(spL)}' EXISTE pero este bake NO lo escribió " &
                                              $"(requerido={reqL}) — es resto de un horneado anterior: NO entra al archive.")
                End If

                If Not disponible Then
                    ' No hay nada de este bake para este spec. Si además ERA requerido, falta: tumba el
                    ' bundle ENTERO (ver la nota de B1 arriba). Si no era requerido, no pasa nada: no
                    ' correspondía producirlo.
                    ' Antes esto se exigía por constante de juego —los 3 DDS de FaceCustomization SIEMPRE,
                    ' produjera el bake lo que produjera—, así que un NPC cuya raza no declara ninguna head
                    ' part de tipo Face perdía los tres y el Save reportaba "N NPCs failed to pack" en CADA
                    ' guardado, por archivos que el bake nunca tuvo que escribir.
                    ' Se exige SIEMPRE lo que dice `FaceGenPaths.SeExigeSiempre`: el NIF, por `Salida =
                    ' Ninguna`, y el facetint de SSE, por estar en `SalidasSiempreRequeridas` — el motor arma
                    ' su ruta por su cuenta e ignora el slot 6, así que omitirlo es cara marrón. Para esos dos
                    ' no importa qué declaró el bake.
                    ' Fail-CLOSED: sin declaración poblada se exige todo — un bundle armado por un camino que
                    ' no pobló la declaración falla RUIDOSO en vez de empaquetar de menos en silencio.
                    If requerido Then missingForThisBundle.Add(sourcePath)
                    Continue For
                End If
                pending.Add(New LooseFileRef With {
                    .SourcePath = sourcePath,
                    .EntryPath = Path.Combine(dataDir, spec.Entry),
                    .IsTexture = spec.IsTexture,
                    .DebugSandbox = b.DebugSandbox
                })
            Next

            If missingForThisBundle.Count > 0 Then
                ' Bundle DESCARTADO: no se agrega NINGUNO de sus refs ⇒ no se empaqueta nada suyo y, al no llegar
                ' a committedRefs, sus sueltos NO se borran. bundleExpected queda > bundleRefCounts ⇒ tampoco
                ' cuenta como committed en el resumen.
                missingSources.AddRange(missingForThisBundle)
                bundleExpected(bi) = pending.Count + missingForThisBundle.Count
                Dim fidF = b.FormIdLow, opF = b.OriginPlugin
                Dim firstF = Path.GetFileName(missingForThisBundle(0))
                Dim nF = missingForThisBundle.Count
                result.FailedBundles.Add($"{opF} 0x{fidF:X8}: {nF} bake file(s) missing, e.g. '{firstF}'")
                Logger.LogLazy(Function() $"[PACK-BATCH] bundle DESCARTADO ENTERO {opF} 0x{fidF:X8}: {nF} archivo(s) requeridos ausentes (primero '{firstF}'). Sus sueltos NO se borran.")
                Continue For
            End If

            For Each rf In pending
                bundleExpected(bi) += 1
                allRefs.Add(rf)
                refToBundleIdx.Add(bi)
                bundleRefCounts(bi) += 1
            Next
        Next

        If missingSources.Count > 0 Then
            result.MissingSources.AddRange(missingSources)
            Logger.LogLazy(Function() $"[PACK-BATCH] {missingSources.Count} expected loose file(s) missing on disk before pack — first: '{missingSources(0)}'")
        End If

        ' --- Step 1b: sueltos INVENTADOS por la app que los NIF de estos bundles referencian ---
        ' Hoy son los clones de UV vanilla de la nuca de gul. Viven en una raiz que se inventa la app,
        ' asi que sin esto el mod se publica apuntando a texturas que no entrega: MEDIDO, 24 de 2877
        ' NIF del corpus las referencian.
        ' Salen de los bundles ACEPTADOS -no de una lista de sesion-, asi que un bundle descartado no
        ' aporta los suyos y dos Save con los mismos inputs dan el MISMO archive.
        ' Van UNA vez aunque los compartan varias NPC, y con ConservarSuelto: entran al archive pero NO
        ' al borrado del paso 5.
        If allRefs.Count > 0 Then
            Dim yaPuestos As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For bi2 = 0 To bundles.Count - 1
                If bundleRefCounts(bi2) = 0 Then Continue For   ' bundle DESCARTADO: no aporta nada
                Dim extras = bundles(bi2).ExtraLooseFiles
                If extras Is Nothing Then Continue For
                For Each rel In extras
                    If String.IsNullOrWhiteSpace(rel) OrElse Not yaPuestos.Add(rel) Then Continue For
                    Dim srcCompartido = Path.Combine(dataDir, rel)
                    If Not File.Exists(srcCompartido) Then
                        ' Contado Y nombrado, no un log: en Release el Logger esta apagado, asi que un
                        ' log suelto aca seria mudo justo en el modo de falla que esto cierra.
                        result.MissingSources.Add(srcCompartido)
                        Continue For
                    End If
                    allRefs.Add(New LooseFileRef With {
                        .SourcePath = srcCompartido,
                        .EntryPath = srcCompartido,
                        .IsTexture = True,
                        .DebugSandbox = False,
                        .ConservarSuelto = True
                    })
                    refToBundleIdx.Add(-1)   ' no es de ningun bundle: no cuenta para su atomicidad
                Next
            Next
        End If

        ' No bake outputs to pack. If there is also nothing to remove, that's the "bake reported Success but
        ' produced no files" error. With exclusions we proceed to the removal-only pack below (delete-only save).
        If allRefs.Count = 0 AndAlso excludeSet.Count = 0 Then
            Dim msg = "No bake outputs found on disk for any of the requested bundles."
            If missingSources.Count > 0 Then msg &= $" First missing: '{missingSources(0)}'."
            result.ErrorMessage = msg
            Return result
        End If

        ' --- Step 2: unregister current archives once ----------------------------------------
        ' NO "frees pooled FileStreams" en el sentido que hacía falta: un reader ALQUILADO ya salió del
        ' pool y su FileStream vive todo el ExtractToMemory, así que esto vuelve con ese handle abierto.
        ' Lo que permitia que el packager renombrara/borrara el .ba2 con lectores en vuelo era el
        ' FileShare.Delete de FilesDictionary_class.AbrirArchiveParaLectura.
        ' ⛔ El packager YA NO RENOMBRA: vuelca el archive nuevo encima del original para que no se salga
        ' del mod bajo Mod Organizer, y volcar pide ESCRITURA, que las lecturas no comparten. Este loop
        ' pasa a ser lo que MAS baja la probabilidad de chocar (cierra los readers pooleados), ademas de
        ' su razon original: dejar de SERVIR entradas cuyos indices dejan de valer al reescribir.
        Dim modBaseName = Path.GetFileNameWithoutExtension(anchorPluginPath)
        Dim preSet = ArchivePackager.DiscoverArchiveSet(dataDir, modBaseName)

        ' ⛔⛔ EL `SourceOrder` SE CAPTURA **ANTES** DE DESMONTAR, porque el desmontaje lo BORRA. Es la
        ' prioridad con la que cada archive gana o pierde un conflicto de ruta; volver a montar sin ella
        ' aplana todo a `ArchiveSourceOrder_RuntimeRegistered` (Integer.MaxValue-1), que le gana a TODO
        ' archive de plugin. El escenario que eso habilita —un mod de facegen-patch que le ganaba a este
        ' set y que después de un Save+bake pasa a perder, con la app mostrando una cabeza distinta de la
        ' que carga el juego— es DEDUCIDO de la ley de prioridades, no medido sobre el corpus: el orden
        ' relativo lo decide `BuildArchivePriority` y aplanarlo lo invierte por construcción.
        ' Mismo patrón y misma ley que `WM_PackUnpack`: capturar antes del desmontaje, restaurar al montar.
        ' ⚠️ Un archive cuyo orden no se pueda leer cae al default y NO se reporta: la captura es
        ' best-effort y no alimenta ningún aviso. Lo que sí se reporta es el fallo de MONTAJE (abajo).
        Dim ordenPrevioFg As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
        For Each archivePath In preSet.Archives
            Try
                Dim nombreArch = Path.GetFileName(archivePath)
                Dim ordenArch As Integer
                If FilesDictionary_class.TryGetArchiveSourceOrder(nombreArch, ordenArch) Then
                    ordenPrevioFg(nombreArch) = ordenArch
                End If
            Catch
                ' Best-effort por archive: el que falte cae al default. NO alimenta ningún reporte — ver
                ' el ⚠️ de arriba; prometer uno que no existe es lo que este comentario evita.
            End Try
        Next

        For Each archivePath In preSet.Archives
            Try
                FilesDictionary_class.UnregisterArchive(archivePath)
            Catch
            End Try
        Next

        ' --- Step 3: walk allRefs, parallel-compress in micro-batches, flush on cap ----------
        Dim totalEntries = allRefs.Count
        Dim entriesDone As Integer = 0

        Dim chunkEntries As New List(Of VirtualEntry)
        Dim chunkRefs As New List(Of LooseFileRef)
        Dim chunkCompBytes As Long = 0
        Dim committedRefs As New List(Of LooseFileRef)
        Dim cancelled As Boolean = False
        Dim packFailureMessage As String = ""

        Dim parOpts As New ParallelOptions With {
            .MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount),
            .CancellationToken = ct
        }

        Dim idx As Integer = 0
        While idx < allRefs.Count
            If ct.IsCancellationRequested Then
                cancelled = True
                Exit While
            End If

            Dim batchSize = Math.Min(MICRO_BATCH, allRefs.Count - idx)
            Dim micro(batchSize - 1) As VirtualEntry
            ' Per-ref compress errors are captured (instead of swallowed) so the user sees WHICH
            ' loose failed and why, rather than a silent "X NPCs dropped" with no breadcrumb.
            Dim microErrors(batchSize - 1) As String

            Try
                Parallel.For(0, batchSize, parOpts,
                    Sub(j)
                        Dim rf = allRefs(idx + j)
                        Try
                            micro(j) = If(rf.IsTexture,
                                          MakeTextureEntry(dataDir, rf.SourcePath, rf.EntryPath, game),
                                          MakeMaterialEntry(dataDir, rf.SourcePath, rf.EntryPath, game))
                        Catch ex As Exception
                            micro(j) = Nothing
                            microErrors(j) = $"{ex.GetType().Name}: {ex.Message}"
                        End Try
                    End Sub)
            Catch ex As OperationCanceledException
                cancelled = True
                Exit While
            End Try

            ' Surface every per-ref compress failure to the log + result so the post-pack summary
            ' can name them. Without this, a corrupt DDS or unreadable NIF disappeared into a
            ' silent count gap ("2 NPCs dropped"); now [PACK-BATCH-ERR] makes it debuggable.
            For j = 0 To batchSize - 1
                If microErrors(j) IsNot Nothing Then
                    Dim rf = allRefs(idx + j)
                    Dim msg = microErrors(j)
                    Dim src = rf.SourcePath
                    Logger.LogLazy(Function() $"[PACK-BATCH-ERR] compress failed for '{src}': {msg}")
                    result.MissingSources.Add(src)
                End If
            Next

            If ct.IsCancellationRequested Then
                cancelled = True
                Exit While
            End If

            ' Fold compressed entries into the accumulator, flushing on cap.
            For j = 0 To batchSize - 1
                Dim ve = micro(j)
                Dim rf = allRefs(idx + j)
                If ve Is Nothing Then Continue For

                Dim veCompSize As Long = If(ve.PreCompressedCompSize > 0UI,
                                            CLng(ve.PreCompressedCompSize),
                                            CLng(ve.PreCompressedDecompSize))

                If chunkEntries.Count > 0 AndAlso chunkCompBytes + veCompSize > MEMORY_CAP_BYTES Then
                    Try
                        FlushChunk(dataDir, modBaseName, game, ba2Version,
                                   chunkEntries, chunkRefs, chunkCompBytes,
                                   result, committedRefs, progress, entriesDone, totalEntries, ct, excludeSet)
                    Catch ex As Exception
                        packFailureMessage = $"BA2 packer failed mid-flush: {ex.GetType().Name}: {ex.Message}"
                        Exit For
                    End Try
                    entriesDone += chunkEntries.Count
                    chunkEntries = New List(Of VirtualEntry)
                    chunkRefs = New List(Of LooseFileRef)
                    chunkCompBytes = 0
                    If ct.IsCancellationRequested Then
                        cancelled = True
                        Exit For
                    End If
                End If

                chunkEntries.Add(ve)
                chunkRefs.Add(rf)
                chunkCompBytes += veCompSize
            Next

            If packFailureMessage <> "" Then Exit While

            idx += batchSize
            Report(progress, PackPhase.BuildingBundle,
                   $"Compressed {idx:N0}/{totalEntries:N0} (buffer {chunkCompBytes / (1024.0 * 1024.0):N0} MB / {MEMORY_CAP_BYTES / (1024.0 * 1024.0):N0} MB)",
                   idx, totalEntries)
        End While

        ' Final flush of whatever survived (carries the exclusions so both buckets get stripped even if the
        ' surviving entries only touch one).
        If packFailureMessage = "" AndAlso Not cancelled AndAlso chunkEntries.Count > 0 Then
            Try
                FlushChunk(dataDir, modBaseName, game, ba2Version,
                           chunkEntries, chunkRefs, chunkCompBytes,
                           result, committedRefs, progress, entriesDone, totalEntries, ct, excludeSet)
                entriesDone += chunkEntries.Count
            Catch ex As Exception
                packFailureMessage = $"BA2 packer failed on final flush: {ex.GetType().Name}: {ex.Message}"
            End Try
        End If

        ' Delete-only (or all-excluded) case: no flush ran the exclusions yet → do one removal-only Pack so the
        ' removed NPCs' bake entries are stripped from the target archive set while every other entry is preserved.
        If packFailureMessage = "" AndAlso Not cancelled AndAlso excludeSet.Count > 0 AndAlso result.FlushesCommitted = 0 Then
            Try
                FlushChunk(dataDir, modBaseName, game, ba2Version,
                           New List(Of VirtualEntry)(), New List(Of LooseFileRef)(), 0L,
                           result, committedRefs, progress, entriesDone, totalEntries, ct, excludeSet)
            Catch ex As Exception
                packFailureMessage = $"BA2 packer failed on removal flush: {ex.GetType().Name}: {ex.Message}"
            End Try
        End If

        ' --- Step 4: re-mount EVERY archive in the set (skipped ones too — we unregistered them) -
        ' ⛔⛔ EL Try VA ADENTRO DEL For Y CUBRE LAS DOS LLAMADAS. El `RegisterArchive` estaba AFUERA de
        ' cualquier Try, y desde que el montaje falla RUIDOSO sobre un archive que no se puede parsear
        ' (antes se tragaba el fallo y volvía como si nada) eso se volvió una bomba: el camino de
        ' `FlushChunk` fallando en un chunk —que ESTE MISMO método atrapa a propósito, unas líneas más
        ' arriba, porque es un fallo previsto— deja un archive a medias; al llegar acá, ese archive
        ' revienta el parseo, la excepción sale del `For` y los archives 3..N quedan DESMONTADOS por el
        ' resto de la sesión. Encima el paso 5 no corre y el diagnóstico por NPC se pierde detrás de un
        ' error genérico.
        ' Es el MISMO gesto que `WM_PackUnpack` hace en su re-montaje post-pack, con el mismo idiom y por
        ' la misma razón — allá está documentado: un archive que desaparece o no parsea entre el Discover
        ' y el Register no puede llevarse puestos a los demás.
        ' Lo que NO se hace es tragarlo en silencio: los que fallaron se acumulan y entran al reporte
        ' estructurado, que es el que le dice al usuario QUÉ NPC quedó sin su FaceGen.
        Dim postSet = ArchivePackager.DiscoverArchiveSet(dataDir, modBaseName)
        Dim archivesNoMontados As New List(Of String)
        For Each archivePath In postSet.Archives
            ' ⛔ DOS `Try` SEPARADOS, Y NO ES ESTILO. Con los dos en el mismo bloque, un `Unregister` que
            ' tira SALTEA el `Register` — y `DesmontarBajoCandado` baja el flag de `_registeredArchives`
            ' al FINAL, así que un fallo a mitad deja el flag PUESTO: el archive queda desmontado a medias
            ' y el guard de idempotencia de `RegisterArchive` convierte todo reintento posterior en un
            ' no-op durante el resto de la sesión. Separados, el fallo de la baja no impide intentar el
            ' alta, que es la que de verdad restaura el servicio.
            ' ⚠️ ESTADO RESIDUAL DECLARADO: si el `Unregister` tiró DESPUÉS de purgar algo pero ANTES de
            ' bajar el flag, el `Register` de abajo sale por ese guard sin montar nada y SIN tirar — o sea
            ' que no puede reportarse como fallo de montaje. Por eso el fallo de la BAJA también entra al
            ' reporte: es la única señal que queda de ese caso.
            Dim nombreArchivo = IO.Path.GetFileName(archivePath)
            Try
                FilesDictionary_class.UnregisterArchive(archivePath)
            Catch ex As Exception
                archivesNoMontados.Add($"{nombreArchivo} (unmount): {ex.Message}")
            End Try
            Try
                ' Y con el orden CAPTURADO, no con el default: ver el ⛔ del paso 2.
                Dim ordenArch As Integer
                If ordenPrevioFg.TryGetValue(nombreArchivo, ordenArch) Then
                    FilesDictionary_class.RegisterArchive(archivePath, ordenArch)
                Else
                    FilesDictionary_class.RegisterArchive(archivePath)
                End If
            Catch ex As Exception
                archivesNoMontados.Add($"{nombreArchivo}: {ex.Message}")
            End Try
        Next
        ' ⛔⛔ EL FALLO DE REMONTE **NO** ES UN FALLO DEL PACK. Iba a `packFailureMessage`, que abajo pone
        ' `Success = False`, y eso decía dos mentiras: (1) el pack SALIÓ BIEN — los bytes están en el
        ' archive, en disco, y el juego los carga; lo único roto es la vista EN MEMORIA de esta sesión; y
        ' (2) el llamador corta con un early-return sobre `Success = False`, así que en el caso MIXTO
        ' —algunos NPC fallaron al empaquetar Y además falló un remonte— se perdía el diagnóstico POR NPC,
        ' que es justo lo que el paso 4 vino a rescatar.
        ' Va en campo APARTE: `Success` sigue significando "el archive en disco quedó bien", y el aviso se
        ' ANEXA al resumen. Éxito con warning no es lo mismo que fallo.
        If archivesNoMontados.Count > 0 Then
            result.RemountWarning =
                $"⚠ {archivesNoMontados.Count} archive(s) could not be re-mounted after packing — the BA2 on " &
                "disk is correct and the game will load it, but this session will not resolve its contents " &
                "until you restart or refresh: " & String.Join(" | ", archivesNoMontados)
        End If

        ' --- Step 5: delete loose for every committed ref ------------------------------------
        ' DebugSandbox refs (the _2.xxx files) are deleted too — per user 2026-05-26: if the
        ' bundle landed in a BA2 (committed), the disk loose is redundant regardless of which
        ' suffix it carried. Old behavior preserved them for inspection; new behavior trusts the
        ' BA2 as the source of truth and lets the user inspect via Unpack if needed.
        If committedRefs.Count > 0 Then
            Report(progress, PackPhase.DeletingLoose, "Removing loose files…", 0, committedRefs.Count)
            Dim deletedAt As Integer = 0
            Dim affectedDirs As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each rf In committedRefs
                deletedAt += 1
                ' Activo COMPARTIDO: entro al archive pero el suelto se queda. Borrarlo -y sacarlo del
                ' diccionario, dos lineas mas abajo- romperia al render y a las demas NPC que lo usan.
                If rf.ConservarSuelto Then Continue For
                Try
                    If File.Exists(rf.SourcePath) Then
                        File.Delete(rf.SourcePath)
                        result.DeletedLoose.Add(rf.SourcePath)
                    End If
                    Dim relUnderData = Path.GetRelativePath(dataDir, rf.SourcePath).Correct_Path_Separator
                    FilesDictionary_class.RemoveDictionaryEntry(relUnderData)
                    Dim d = Path.GetDirectoryName(rf.SourcePath)
                    If Not String.IsNullOrEmpty(d) Then affectedDirs.Add(d)
                Catch ex As Exception
                    Dim srcL = rf.SourcePath
                    Dim msgL = ex.Message
                    Dim typeL = ex.GetType().Name
                    Logger.LogLazy(Function() $"[PACK-BATCH-DEL] delete failed '{srcL}': {typeL}: {msgL}")
                End Try
                If (deletedAt And &H1F) = 0 OrElse deletedAt = committedRefs.Count Then
                    Report(progress, PackPhase.DeletingLoose,
                           $"Removed {deletedAt}/{committedRefs.Count}", deletedAt, committedRefs.Count)
                End If
            Next
            For Each leaf In affectedDirs
                RemoveEmptyDirsUpTo(leaf, dataDir, result.RemovedDirs)
            Next
        End If

        ' --- Step 6: count fully-committed bundles for the summary -------------------------------
        ' Single pass over allRefs accumulating per-bundle committed-hit counts (was O(bundles×refs):
        ' for each bundle it re-scanned every ref). refToBundleIdx is parallel to allRefs.
        Dim committedSet As New HashSet(Of String)(committedRefs.Select(Function(r) r.SourcePath),
                                                   StringComparer.OrdinalIgnoreCase)
        Dim bundleHitCounts(bundles.Count - 1) As Integer
        For ri = 0 To allRefs.Count - 1
            ' -1 = ref COMPARTIDO (no es de ningun bundle): no cuenta para la atomicidad de nadie.
            ' Sin esta guarda es un indice fuera de rango, no un conteo equivocado.
            If refToBundleIdx(ri) >= 0 AndAlso committedSet.Contains(allRefs(ri).SourcePath) Then
                bundleHitCounts(refToBundleIdx(ri)) += 1
            End If
        Next
        For bi = 0 To bundles.Count - 1
            ' A bundle is fully committed when every file the game's layout calls for (4 on FO4,
            ' 2 on SSE — see FaceGenFileSpecs) was authored AND committed.
            If bundleExpected(bi) > 0 AndAlso bundleRefCounts(bi) = bundleExpected(bi) AndAlso
               bundleHitCounts(bi) = bundleExpected(bi) Then
                result.BundlesCommitted += 1
            End If
        Next

        If packFailureMessage <> "" Then
            result.ErrorMessage = packFailureMessage
            Report(progress, PackPhase.Done, "Pack failed (some bundles may have committed).", entriesDone, totalEntries)
            Return result
        End If

        If cancelled Then
            result.ErrorMessage = "Cancelled before all bundles committed."
            Report(progress, PackPhase.Done, "Cancelled.", entriesDone, totalEntries)
            Return result
        End If

        result.Success = True
        Report(progress, PackPhase.Done,
               $"Done. {result.BundlesCommitted}/{bundles.Count} bundles committed in {result.FlushesCommitted} flush(es).",
               totalEntries, totalEntries)
        Return result
    End Function

    ''' <summary>Hand the current accumulator to ArchivePackager.Pack as a single batch.
    ''' Mirror of WM_PackUnpack.FlushChunk: single Pack call, free PreCompressedBytes from the
    ''' flushed entries so the next chunk has clean memory, append committed refs to the global
    ''' list so the caller can delete their loose sources after every flush has run.</summary>
    Private Sub FlushChunk(dataDir As String,
                           modBaseName As String,
                           game As Config_App.Game_Enum,
                           ba2Version As UInteger,
                           chunkEntries As List(Of VirtualEntry),
                           chunkRefs As List(Of LooseFileRef),
                           chunkCompBytes As Long,
                           result As PackResult,
                           committedRefs As List(Of LooseFileRef),
                           progress As Action(Of PackProgress),
                           entriesDone As Integer,
                           totalEntries As Integer,
                           ct As CancellationToken,
                           Optional excludeSet As HashSet(Of String) = Nothing)
        ' Proceed with 0 entries only when there are exclusions to apply (delete-only removal pass).
        Dim hasExclusions = excludeSet IsNot Nothing AndAlso excludeSet.Count > 0
        If chunkEntries.Count = 0 AndAlso Not hasExclusions Then Return
        If ct.IsCancellationRequested Then Return

        Report(progress, PackPhase.WritingArchive,
               $"Writing BA2 (flush {result.FlushesCommitted + 1}: {chunkEntries.Count:N0} entries, {chunkCompBytes / (1024.0 * 1024.0):N0} MB)…",
               entriesDone, totalEntries)

        Dim req As New PackagerRequest With {
            .Game = MapGame(game),
            .Ba2Version = ba2Version,
            .ModBaseName = modBaseName,
            .OutputDir = dataDir,
            .Entries = chunkEntries,
            .BundleAlreadyCompressed = True,
            .MaxArchiveBytes = MAX_ARCHIVE_BYTES,
            .Overflow = ArchiveOverflowPolicy.ThrowOnExceed,
            .SingleAnchorOnly = True,
            .ExcludePaths = If(hasExclusions, excludeSet, Nothing),
            .PluginWriter = Sub(p As String, g As GameKind)
                                ' Anchor plugin already exists (Save ESP wrote it before we got
                                ' called). This callback would only fire if the packer split into
                                ' a numbered slot — but we set Overflow=ThrowOnExceed so we never
                                ' reach split. Emit a dummy as a defensive fallback in case the
                                ' policy changes; mirrors WM behavior so existing tests stay valid.
                                PluginWriter.WriteLightMasterDummy(p, MapGameBack(g), PluginWriter.NPC_MANAGER_AUTHOR_CNAM)
                            End Sub
        }

        ' [AUDIT-BA2] valida que el writer suelta cada payload comprimido al escribirlo. Se mide el
        ' pico de heap ADMINISTRADO del pack: antes los N payloads vivian a la vez hasta terminar el
        ' archivo, ahora sólo hasta que cada uno se escribe.
        Dim heapAntes As Long = 0
        If Logger.Enabled Then heapAntes = GC.GetTotalMemory(False)
        Dim chunkResult = ArchivePackager.Pack(req)
        If Logger.Enabled Then
            Dim hA = heapAntes, hD = GC.GetTotalMemory(False)
            Dim mi = GC.GetGCMemoryInfo()
            Dim entradas = req.Entries.Count
            Logger.LogLazy(Function() $"[AUDIT-BA2] pack de {entradas} entradas: heap {hA \ (1024 * 1024)} MB -> {hD \ (1024 * 1024)} MB, heapTotalDelGC={mi.HeapSizeBytes \ (1024 * 1024)} MB")
        End If

        result.WrittenArchives.AddRange(chunkResult.Archives)
        result.SkippedArchives.AddRange(chunkResult.Skipped)
        result.FlushesCommitted += 1
        committedRefs.AddRange(chunkRefs)

        ' Free compressed bytes so the next chunk starts with clean memory. WM does the same.
        For Each ve In chunkEntries
            ve.Data = Nothing
            ve.PreCompressedBytes = Nothing
        Next
    End Sub

    ''' <summary>Delete <paramref name="leafDir"/> and each empty ancestor, walking UP until
    ''' (and excluding) <paramref name="stopDir"/>. Stops at the first non-empty ancestor. Never
    ''' deletes stopDir itself nor anything outside it (StartsWith guard). Best-effort: IO failure
    ''' aborts the walk silently — a leftover empty dir is harmless.</summary>
    Private Sub RemoveEmptyDirsUpTo(leafDir As String, stopDir As String, removed As List(Of String))
        Try
            Dim sep = Path.DirectorySeparatorChar
            Dim stopFull = Path.GetFullPath(stopDir).TrimEnd(sep, Path.AltDirectorySeparatorChar)
            Dim current = Path.GetFullPath(leafDir).TrimEnd(sep, Path.AltDirectorySeparatorChar)
            While True
                If String.Equals(current, stopFull, StringComparison.OrdinalIgnoreCase) Then Exit While
                If Not current.StartsWith(stopFull & sep, StringComparison.OrdinalIgnoreCase) Then Exit While

                If Directory.Exists(current) Then
                    Dim hasAny As Boolean = False
                    For Each e In Directory.EnumerateFileSystemEntries(current)
                        hasAny = True
                        Exit For
                    Next
                    If hasAny Then Exit While
                    Directory.Delete(current, recursive:=False)
                    If removed IsNot Nothing Then removed.Add(current)
                End If
                Dim parent = Path.GetDirectoryName(current)
                If String.IsNullOrEmpty(parent) Then Exit While
                current = parent
            End While
        Catch
        End Try
    End Sub

    ' ============================================================================
    ' Entry builders — same shape as the previous per-NPC version. sourcePath may
    ' carry the _2 suffix (DebugSandbox); entryPath is always the canonical name
    ' the BA2 entry takes inside the archive, so a release-built archive is byte-
    ' compatible regardless of whether the bake ran in debug mode.
    ' ============================================================================





    Private Sub Report(progress As Action(Of PackProgress),
                       phase As PackPhase, detail As String,
                       current As Integer, max As Integer)
        If progress Is Nothing Then Return
        progress(New PackProgress With {
            .Phase = phase,
            .Detail = detail,
            .Current = current,
            .Max = max
        })
    End Sub

End Module
