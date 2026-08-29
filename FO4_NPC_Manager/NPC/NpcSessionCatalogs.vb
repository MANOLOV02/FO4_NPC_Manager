''' <summary>LOS CATÁLOGOS DE SESIÓN, EN UN SOLO LUGAR AL QUE ENTRAN LOS TRES ENTRY POINTS.
''' <para>ESTABAN DENTRO DE <c>MainForm.EnsureAssetDictionaryAsync</c>, o sea que sólo existían si el
''' usuario abría la GUI. <c>BakeAllRunner</c> y <c>FO4_FaceTint_CLI</c> nunca ejecutan <c>MainForm</c>,
''' así que en una sesión de Skyrim el bake headless corría con <c>RaceCompatCatalog = Nothing</c> ⇒
''' <c>IsHeadPartValidForRace</c> devolvía False para todo el pelo vanilla en razas COtR y
''' <b>el CLI horneaba head-parts DISTINTOS de los que horneaba la GUI para el mismo NPC</b>. Un A/A
''' entre entry points que no podía dar cero. El CLI ya sabía que el problema existía: guarda y restaura
''' <c>RaceCompatCatalog</c> alrededor de un diagnóstico puntual, pero su camino principal no lo poblaba.</para>
''' <para>Los dos catálogos son FUNCIÓN PURA de los plugins cargados —no dependen de nada de la UI— así
''' que no hay motivo para que vivan en un formulario. Idempotentes: si ya están, no se recargan.</para>
''' <para>Lo que NO entra acá y no es divergencia: <c>NpcRecordOverlay.EffectiveRaceResolver</c>. Ese
''' resuelve la raza pisada por el editor de NPC, que existe sólo en memoria de la GUI; que el CLI lo deje
''' en Nothing es correcto y está documentado en <c>MainForm_Load</c>.</para></summary>
''' <para>Es <c>Public</c> y no <c>Friend</c> porque el consumidor está en OTRO ensamblado:
''' <c>FO4_FaceTint_CLI</c>. Es un exe de la app, no una DLL que se distribuya como librería.</para>
Public Module NpcSessionCatalogs

    ''' <summary>Puebla los catálogos que el render y el bake comparten. Llamar DESPUÉS de que el
    ''' diccionario de assets esté listo: el catálogo de sliders lee su config a través de
    ''' <c>FilesDictionary</c>.
    ''' <param name="pm">El PluginManager de la sesión. Si es Nothing no hace nada.</param></summary>
    Public Sub EnsureLoaded(pm As PluginManager)
        If pm Is Nothing Then Return

        ' RaceMenu EXTENDED face-slider catalog — SKYRIM ONLY (RaceMenu es un mod de Skyrim; FO4 no tiene
        ' análogo, así que en FO4 queda Nothing y el camino de morphs custom cae a aplicación por nombre
        ' directo). Se construye UNA vez, con los nombres de los plugins cargados: skee64 escanea
        ' Meshes\actors\character\FaceGenMorphs\<pluginName>\races.ini por mod (LoadMods→ForEachMod).
        ' Alimenta el camino compartido de morphs vía NpcMorphResolver.SliderCatalog.
        If Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            ' El interruptor va CON el catálogo y por la misma puerta: los dos describen la misma instalación
            ' de RaceMenu, y el camino compartido de morphs (render + bake, NpcMorphResolver) los lee juntos.
            ' Fuera de Skyrim NO se toca: en FO4 no hay skee64.ini y el default (True) es el que corresponde.
            NpcMorphResolver.ExtendedMorphsEnabled = SseCatalogs.FaceExtendedMorphsEnabled()
            If Not NpcMorphResolver.ExtendedMorphsEnabled Then
                Logger.LogLazy(Function() $"[RACEMENU-CATALOG] bExtendedMorphs=0 — el motor NO aplica morphs " &
                                          $"extendidos de cara en esta instalación [{SseCatalogs.SkeeIniSource()}]")
            End If
        End If
        If Config_App.Current.Game = Config_App.Game_Enum.Skyrim AndAlso NpcMorphResolver.SliderCatalog Is Nothing Then
            Try
                ' La lista de carpetas son los plugins que ESTA SESIÓN cargó, en load order — el mismo
                ' conjunto que Preflight le pasó a FilesDictionary, así que la config que escaneamos y los
                ' archives de los que la podemos leer siempre coinciden. Escanear un conjunto MÁS AMPLIO
                ' sería lo peor: listaría sliders cuyo .tri extendido vive en un archive que nunca
                ' indexamos — visibles en la UI y no-op en render y bake.
                Dim modNames = pm.Plugins.Select(Function(pl) pl.FileName).Where(Function(n) Not String.IsNullOrEmpty(n)).ToList()
                Dim cat As New FO4_Base_Library.RaceMenuSliderCatalog()
                cat.Load(modNames)
                NpcMorphResolver.SliderCatalog = cat
                Logger.LogLazy(Function() $"[RACEMENU-CATALOG] scanned {modNames.Count} loaded mod folders " &
                                          $"for FaceGenMorphs\...\races.ini; races={cat.RaceCount()} hasAny={cat.HasAny()} " &
                                          $"configs={String.Join(", ", cat.LoadedConfigMods())}")
            Catch ex As Exception
                Logger.LogLazy(Function() $"[RACEMENU-CATALOG] load failed: {ex.Message}")
            End Try
        End If

        ' RaceCompatibility proxyRaces — SKYRIM ONLY. Un mod de raza custom (COtR & co) declara sus razas a
        ' través del GenericRaceController de RaceCompatibility, cuyo OnInit las INSERTA en las FormList
        ' vanilla de head-parts en runtime. Esa mutación no llega nunca a un plugin, así que por records
        ' solos todo pelo vanilla parece "inválido" para esas razas y los pickers no ofrecerían más que las
        ' piezas del propio mod. Se reconstruye la inserción (QUST VMAD + el script compilado del mod) y se
        ' le pasa al filtro del catálogo. Se detecta POR FORMA (cualquier QUST que cargue un
        ' GenericRaceController), nunca por nombre de mod.
        ' Decia "se rearma en cada carga" y era FALSO: la guarda `Is Nothing` lo construye UNA vez por
        ' proceso, y abajo se sueltan los QUST, asi que ni siquiera habria con que reconstruirlo. No es un
        ' defecto: NPC Manager carga el load order UNA VEZ por juego y no lo recarga en caliente — un mod
        ' agregado o sacado se refleja al reabrir, que es el ciclo real. Los caminos headless arrancan
        ' proceso nuevo por corrida, asi que para ellos la distincion no existe.
        If Config_App.Current.Game = Config_App.Game_Enum.Skyrim AndAlso HeadPartResolver.RaceCompatCatalog Is Nothing Then
            Try
                HeadPartResolver.RaceCompatCatalog = FO4_Base_Library.RaceCompatibilityCatalog.Load(pm, Config_App.Current.Game)
            Catch ex As Exception
                Logger.LogLazy(Function() $"[RACECOMPAT] load failed: {ex.Message}")
            End Try
        End If

        ' ESTOS DOS SE HABÍAN PERDIDO AL MOVER EL BLOQUE. Vivían en el mismo tramo de
        ' `MainForm.EnsureAssetDictionaryAsync` que se extrajo acá, y en la extracción quedaron afuera: no los
        ' poblaba NADIE en toda la app (el único `.Current =` que sobrevivía era el de una probe de Tools\).
        ' No rompe la compilación ni tira, porque los tres lectores ya toleraban Nothing —era el estado del
        ' primer arranque— así que el síntoma habría sido, en una sesión de Skyrim, pickers VACÍOS en
        ' Edit Face (face paints / warpaints) y en Edit Body (overlays de cuerpo/manos/pies), más la mitad
        ' dinámica de la lista de Body Scale. Silencioso de punta a punta.

        ' RaceMenu PAINT lists — SKYRIM ONLY. Los warpaints (máscaras de tint de cara) y los paints de
        ' cuerpo/manos/pies/cara (overlays) son listas con nombre (name;;path) que RaceMenu acumula desde el
        ' handler Papyrus On*PaintRequest de cada mod; no hay catálogo estático ni file browser. Se reconstruye
        ' la misma unión leyendo los scripts shipeados (scripts\*.pex, loose + BSA), que es el artefacto
        ' compilado que el juego realmente carga. Alimenta los pickers de textura de los editores SSE
        ' (SseCatalogs) para que ofrezcan las listas de RaceMenu y no un explorador de archivos crudo.
        If Config_App.Current.Game = Config_App.Game_Enum.Skyrim AndAlso FO4_Base_Library.RaceMenuPaintCatalog.Current Is Nothing Then
            Try
                Dim paintCat As New FO4_Base_Library.RaceMenuPaintCatalog()
                paintCat.Load()
                FO4_Base_Library.RaceMenuPaintCatalog.Current = paintCat
            Catch ex As Exception
                Logger.LogLazy(Function() $"[PAINT-CATALOG] load failed: {ex.Message}")
            End Try
        End If
        ' Misma idea para la lista de sliders de node-transform (body scale) de RaceMenu: RaceMenu no escanea
        ' el esqueleto — su lista de nodos es la unión de lo que RaceMenuPlugin/XPMSE/… registran vía
        ' NiOverride.AddNodeTransform*. Se rearma desde los scripts shipeados para que la pestaña Body Scale
        ' ofrezca el set real de RaceMenu y no un volcado de huesos.
        If Config_App.Current.Game = Config_App.Game_Enum.Skyrim AndAlso FO4_Base_Library.RaceMenuNodeCatalog.Current Is Nothing Then
            Try
                Dim nodeCat As New FO4_Base_Library.RaceMenuNodeCatalog()
                nodeCat.Load()
                FO4_Base_Library.RaceMenuNodeCatalog.Current = nodeCat
            Catch ex As Exception
                Logger.LogLazy(Function() $"[NODE-CATALOG] load failed: {ex.Message}")
            End Try
        End If

        ' QUST se carga SÓLO para alimentar el catálogo de arriba (el VMAD de las quests que llevan
        ' GenericRaceController) y nada más en la app resuelve una quest. Son records pesados, así que se
        ' sueltan apenas se consumen en vez de dejar miles residentes por una lectura única. FO4 ni siquiera
        ' construye el catálogo, así que ahí también los libera. Una feature futura que necesite QUST en
        ' runtime (stages, aliases, propiedades de script) tiene que BORRAR esta llamada, no agregar un
        ' segundo pase de carga — los records ya están parseados al cargar.
        ' INCONDICIONAL, y por eso vive acá: los TRES entry points cargan QUST (está en
        ' SIGS_NPC_RENDERING, que es el filtro del CLI, y los otros dos cargan sin filtro) y ninguno de los
        ' tres resuelve una quest para otra cosa. Gatearlo por llamador sería volver a tener una decisión
        ' que cada entry point toma por su cuenta — que es exactamente el defecto que este módulo arregla.
        pm.DropRecordsOfType("QUST")
    End Sub

End Module
