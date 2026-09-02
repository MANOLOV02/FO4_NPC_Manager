Imports System.Globalization
Imports System.IO
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion
Imports MaterialLib
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Mathematics

''' <summary>Phase 2 of the MainForm split: the single owner of per-load-order record-parse
''' services. Memoizes parsed ARMO/ARMA/RACE/HDPT/NPC_ records (thousands of records re-parsed
''' many times per render) behind FormID-keyed ConcurrentDictionary caches (background renders run
''' on Task.Run, so reads/writes can overlap). Injected by constructor (DI) into MainForm and the
''' extracted render subsystems so none of them re-implement parsing or reach into MainForm.
''' Real separate class, NOT a partial. See 61-perf-mainform-split.
'''
''' Lifetime: the PluginManager is immutable for this context's life, so a parsed result is stable
''' and the caches naturally die with the load order. InvalidateParseCaches clears ALL of them
''' together (owner-clears-its-own semantics); today ParseAllNPCs is the only caller and runs once
''' on still-empty caches, so it is defensive, not load-bearing.</summary>
Friend NotInheritable Class NpcRenderContext
    Public ReadOnly PluginManager As PluginManager

    Private ReadOnly _armoCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, Canon.IArmo)()
    Private ReadOnly _armaCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, Canon.IArma)()
    ''' <summary>Cache de la vista canónica de RACE, keyed por FormID. La caché plana aparte que
    ''' existía antes ya no hace falta: LmCustomTintLoader arma la lista de tinte fusionada aparte
    ''' (sin mutar el record) y RaceUtil ya trabaja sobre esta misma vista canónica.</summary>
    Private ReadOnly _raceCanonCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, Canon.IRace)()
    Private ReadOnly _hdptCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, Canon.IHdpt)()
    Private ReadOnly _armorRaceCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, HashSet(Of UInteger))()

    ''' <summary>Bytes del esqueleto extra de una RAZA (<c>RACE.GNAM</c> → <c>BPTD.MODL</c>), keyed por
    ''' FormID de la raza. Un <c>Nothing</c> ("esta raza no tiene BPTD/MODL") lo memoiza
    ''' <c>GetOrAdd</c> nativamente, así que un miss tampoco se re-intenta.
    ''' <para>⛔ Vive ACÁ, con las cachés de parseo, porque tenía DOS dueños: el render la memoizaba en
    ''' <c>MainForm._skelBptdBytesCache</c> y los DOS call sites de <see cref="NpcMountingResolver"/> no
    ''' la memoizaban en absoluto. Los mismos bytes, dos leyes, aplicadas en un solo lado.</para>
    ''' <para>MEDIDO (FO4, 57 plugins, Release x64, 6 corridas): <c>TryLoadBptdSkeletonBytes</c> cuesta
    ''' 6,1–11,3 ms POR LLAMADA, y de eso 5,0–8,8 ms son el <c>Canon.CanonRecords.Race</c> que hace
    ''' adentro SIN caché. Leer los bytes es 0,0002 ms: el costo es el parseo del record, no el I/O. En el
    ''' selector de atuendos eso se pagaba una vez POR PRENDA y por pasada de la lista.</para></summary>
    Private ReadOnly _bptdSkelBytesCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, Byte())()

    ''' <summary>Los sockets <c>BSConnectPoint::Parents</c> del ACTOR, keyed por (raza, género) — que son
    ''' EXACTAMENTE los dos campos de <c>NPCVisualState</c> que
    ''' <see cref="NpcMountingResolver.LoadActorBSConnectPoints"/> lee; todo lo demás lo deriva del
    ''' PluginManager y del FilesDictionary, fijos para el orden de carga.
    ''' <para>Se guardan los WARNINGS junto al diccionario y se REPITEN en cada hit. ⛔ No es adorno: sin
    ''' eso la primera llamada avisaría "No skeleton path resolved for race X" y la segunda no, o sea que
    ''' memoizar cambiaría la salida observable — que es justo lo que el A/B de esta tanda exige que NO
    ''' cambie. Si alguien "simplifica" esto tirando los warnings, rompe esa igualdad en silencio.</para>
    ''' <para>La LEY (las dos fuentes: <c>RACE.ANAM</c> + <c>BPTD.MODL</c>) NO vive acá: se recibe como
    ''' fábrica desde el resolvedor, que es su dueño. Acá vive sólo la caché, porque acá vive su
    ''' invalidación.</para></summary>
    Private ReadOnly _actorSocketsCache As New System.Collections.Concurrent.ConcurrentDictionary(Of (Race As UInteger, Female As Boolean),
                                                                                                     (Sockets As Dictionary(Of String, BSConnectPointReader.ConnectPointInfo),
                                                                                                      Warnings As List(Of String)))()

    ''' <summary>Optional draft-resolver hook (set by MainForm). Given a FormID, returns the draft's
    ''' <see cref="Canon.IArmo"/> when the FormID is an in-memory ARMO draft (provisional 0xFF sentinel or
    ''' an override draft), or Nothing when it is not a draft (the real-record cache path then runs).
    ''' Same injection contract as <c>OutfitResolver.LeveledListResolver</c>: Nothing ⇒ library/real
    ''' behavior, a non-Nothing return ⇒ the app's live draft view. The returned object is NEVER cached
    ''' (drafts mutate live), so a draft edit is reflected on the next render.</summary>
    Public ArmoDraftResolver As Func(Of UInteger, Canon.IArmo) = Nothing

    ''' <summary>Gate de power-armor de la app (necesita el catálogo de keywords), SOBRE LA VISTA que el
    ''' llamador ya abrió. Lo setea MainForm junto con los draft-resolvers y lo consume
    ''' <see cref="EquipCtx"/>, para que la ley única lo aplique una sola vez en vez de que cada caller lo
    ''' repita antes de pedir el footprint.
    ''' <para>⛔ POR VISTA y no por FormID: así <c>BuildFootprint</c> no resuelve el ARMO dos veces y no lo
    ''' puede resolver DISTINTO. El gate lee <c>KWDA</c>, que es heredado, así que preguntar por FormID
    ''' daba la respuesta del HIJO mientras el dibujo usaba la EFECTIVA.</para>
    ''' <para>⛔ Al lado vivía <c>ArmoIsPowerArmor</c>, la variante POR FORMID de este mismo gate. Se
    ''' borró: nadie la leía —sólo la asignaban los tres cableadores (MainForm y los dos arneses de
    ''' Tools)— porque el único consumidor del contexto es <see cref="EquipCtx"/> y ése usa ésta. Un campo
    ''' público que sólo se escribe es una segunda ley esperando a que alguien la use: es exactamente la
    ''' pregunta por FormID que el párrafo de arriba dice que da OTRA respuesta. El camino que sí necesita
    ''' el predicado por FormID —<c>NpcMeshCollector</c>— lo recibe por CONSTRUCTOR, no por acá.</para></summary>
    Public ArmoIsPowerArmorDeVista As Func(Of UInteger, Canon.IArmo, Boolean) = Nothing
    ''' <summary>Idem, del lado de la raza. Ver <see cref="ArmoIsPowerArmorDeVista"/>.</summary>
    Public RaceIsPowerArmor As Func(Of UInteger, Boolean) = Nothing
    ''' <summary>Optional draft-resolver hook for ARMA drafts. See <see cref="ArmoDraftResolver"/>.</summary>
    Public ArmaDraftResolver As Func(Of UInteger, Canon.IArma) = Nothing
    ''' <summary>Optional draft-resolver hook for MSWP drafts. Given a FormID, returns a synthesized
    ''' <see cref="Canon.IMswp"/> when the FormID is an in-memory MSWP draft, or Nothing when it is not.
    ''' Consumed by the material-override pipeline (NpcMaterialResolver) so an UNSAVED draft material-swap
    ''' applies in the live preview (a draft has no real record for the FormID overload to resolve).</summary>
    Public MswpDraftResolver As Func(Of UInteger, Canon.IMswp) = Nothing

    ''' <summary>El <c>Data\</c> EFECTIVO de este contexto. Existe para que los registros que se cargan de
    ''' disco (tints custom de LooksMenu, LUTs de pelo) se lean del MISMO Data que el resto del contexto.
    ''' <para>Sin esto, el CLI headless —que honra <c>--data</c> y NO puebla el <see cref="Config_App"/>
    ''' global— mezclaba dos orígenes en el mismo proceso: los caminos que recibían <c>--data</c> leían de
    ''' uno y los que caían al global, de otro. Y en <c>LmCustomTintLoader</c> eso no se puede arreglar
    ''' recargando: la lista fusionada queda cacheada por raza+género hasta el próximo
    ''' <see cref="LmCustomTintLoader.Invalidate"/>, así que releer con otro Data dejaría razas mezcladas
    ''' de dos orígenes. La divergencia hay que matarla en el ORIGEN, que es esto.</para>
    ''' <para>Vacío ⇒ el global, que es lo correcto para la app (donde son el mismo valor).</para></summary>
    Public ReadOnly DataPath As String

    Public Sub New(pluginManager As PluginManager, Optional dataPath As String = Nothing)
        Me.PluginManager = pluginManager
        Me.DataPath = If(String.IsNullOrWhiteSpace(dataPath), If(Config_App.Current?.DataPath, ""), dataPath)
    End Sub

    ''' <summary>O(1) record lookup, delegated to the PluginManager. Thin convenience so subsystems
    ''' depend on the context rather than capturing the PluginManager separately.</summary>
    Public Function GetRecord(formID As UInteger) As PluginRecord
        Return PluginManager.GetRecord(formID)
    End Function

    ''' <summary>EditorIDs of the DFOB (Default Object) records the engine uses to name the Pipboy DEVICE.
    ''' SOURCE: the engine identifies the Pipboy by FORM IDENTITY, comparing the equipped form against the
    ''' resolved default objects PipboyCleanObject_DO (@VA 0x1400F18B0) and PipboyDustyObject_DO (@VA
    ''' 0x1400F18F0) — NOT by any biped-slot test. These are the record KEYS we look the forms up by; the
    ''' FormIDs themselves are read from the records at load time, never hardcoded (a mod may point the same
    ''' default object at a different ARMO, and that ARMO must then count as the Pipboy).</summary>
    Private Shared ReadOnly PipboyDefaultObjectEditorIds As String() = {"PipboyCleanObject_DO", "PipboyDustyObject_DO"}

    Private _pipboyDeviceArmos As HashSet(Of UInteger) = Nothing
    Private ReadOnly _pipboyDeviceLock As New Object()

    ''' <summary>The set of ARMO FormIDs that ARE the Pipboy device for this load order, resolved from the
    ''' Pipboy DFOB default objects (see <see cref="PipboyDefaultObjectEditorIds"/>). This is the engine's
    ''' identity test. It REPLACES the old "its only worn slot is 60" heuristic, which mis-classified 3 of the
    ''' 7 vanilla slot-60-only ARMOs as Pipboys: AssaultronShield (0022BC24), MirelurkShield (000986CA) and
    ''' babybundled (000F468E) are slot-60-only but are not Pipboys.
    ''' Empty set (no such DFOB in the load order, e.g. Skyrim) ⇒ callers must fall back to their non-Pipboy
    ''' branch, never to the slot heuristic. Computed once and memoized; the load order is immutable for this
    ''' context's life (same lifetime contract as the parse caches above).</summary>
    Public Function PipboyDeviceArmoFormIDs() As HashSet(Of UInteger)
        If _pipboyDeviceArmos IsNot Nothing Then Return _pipboyDeviceArmos
        SyncLock _pipboyDeviceLock
            If _pipboyDeviceArmos IsNot Nothing Then Return _pipboyDeviceArmos
            Dim set_ As New HashSet(Of UInteger)()
            Dim dfobs = PluginManager.GetRecordsOfType("DFOB")
            If dfobs IsNot Nothing Then
                For Each rec In dfobs
                    If rec Is Nothing OrElse String.IsNullOrEmpty(rec.EditorID) Then Continue For
                    If Not PipboyDefaultObjectEditorIds.Any(Function(e) String.Equals(e, rec.EditorID, StringComparison.OrdinalIgnoreCase)) Then Continue For
                    Dim d = Canon.CanonRecords.Dfob(rec, PluginManager)
                    If d IsNot Nothing AndAlso d.Object <> 0UI Then set_.Add(d.Object)
                Next
            End If
            _pipboyDeviceArmos = set_
            Return _pipboyDeviceArmos
        End SyncLock
    End Function

    ''' <summary>The parsed NPC_ universe (shared instance). Public ReadOnly field — callers index/
    ''' mutate the contents (bulk-parse populate, GetParsedNpc memoize, tree iterate) while the
    ''' reference itself stays fixed. Not ReadOnly: VB rejects indexer-set (cache(k)=v) on a
    ''' ReadOnly field of an indexed type — same shape as the original _npcByIdCache field.</summary>
    Public NpcCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, NPC_Data)()

    ''' <summary>Parsed NPC_ by FormID. Cache hit avoids re-parsing the record 5+ times per frame;
    ''' a miss (FormID outside the placed-NPC universe, e.g. a TPLT model source) parses via the
    ''' shared helper and memoizes.</summary>
    Public Function GetParsedNpc(formID As UInteger) As NPC_Data
        Dim cached As NPC_Data = Nothing
        If NpcCache.TryGetValue(formID, cached) AndAlso cached IsNot Nothing Then Return cached
        Dim parsed = NpcRecordOverlay.GetParsedNpc(formID, PluginManager)
        If parsed IsNot Nothing Then NpcCache(formID) = parsed
        Return parsed
    End Function

    ''' <summary>Parse (and cache) an ARMO by FormID. Nothing if the FormID does not resolve to an
    ''' ARMO. Does NOT swallow parse exceptions — callers that must tolerate a malformed record keep
    ''' their own Try/Catch.</summary>
    Public Function GetParsedArmoCrudo(formID As UInteger) As Canon.IArmo
        If formID = 0UI Then Return Nothing
        ' Draft-aware resolution: an ARMO draft (MainForm._armoDrafts, provisional 0xFF FormID or an override
        ' draft keeping its real FormID) is NOT a real record and is NOT in this cache. Consult the app's draft
        ' resolver FIRST (mirror of OutfitResolver.LeveledListResolver / the OTFT-draft render path). A non-Nothing
        ' return is the live draft view — RETURN IT DIRECTLY, never store it in _armoCache: drafts mutate live and
        ' caching would stale them. Only real (non-draft) FormIDs fall through to the cached real-record path.
        If ArmoDraftResolver IsNot Nothing Then
            Dim draftView = ArmoDraftResolver(formID)
            If draftView IsNot Nothing Then Return draftView
        End If
        Return _armoCache.GetOrAdd(formID,
            Function(fid)
                Dim rec = PluginManager.GetRecord(fid)
                If rec Is Nothing OrElse rec.Header.Signature <> "ARMO" Then Return Nothing
                Return Canon.CanonRecords.Armo(rec, PluginManager)
            End Function)
    End Function

    ''' <summary>La vista EFECTIVA de un ARMO: lo que el MOTOR va a usar, con la herencia de
    ''' <c>ARMO.TNAM</c> ya aplicada. Ver <see cref="Canon.CanonHerencia.ArmoEfectivo"/>.
    ''' <para>⛔ Tiene NOMBRE PROPIO, y el crudo se llama <c>GetParsedArmoCrudo</c>, para que cada
    ''' call site tenga que ELEGIR al compilar. Un grep no alcanzaba por dos razones medidas: los dos
    ''' sitios donde la eleccion decide son <c>AddressOf</c> y no matchean <c>GetParsedArmo(</c>, y 10
    ''' de los 12 consumidores viven en <c>MainForm.vb</c> junto a consumidores legitimos del OTRO
    ''' lado, asi que una regla por archivo protege justo a los que ya estaban bien.</para>
    ''' <para>El criterio para elegir NO es donde vive el codigo, es QUE PREGUNTA hace: si la
    ''' respuesta dice que va a hacer el MOTOR, va la efectiva; si dice que dice el ARCHIVO, la
    ''' cruda.</para>
    ''' <para>⛔ NO se cachea: se construye sobre vistas crudas que YA estan cacheadas, asi que
    ''' cachearla agregaria un indice inverso terminal→hijos que habria que invalidar cuando el
    ''' usuario edita o revierte el TERMINAL. Y cachear la CADENA tampoco: medido, la profundidad
    ''' real es 1 en el 100% de los 2.679, o sea que ahorraria un lookup y pagaria el borrador EN EL
    ''' MEDIO —X real cuyo TNAM apunta a Y, e Y borrador con FormID real—, que ningun guard por
    ''' FormID puede cazar.</para></summary>
    Public Function GetParsedArmoEfectivo(formID As UInteger) As Canon.IArmo
        Return Canon.CanonHerencia.ArmoEfectivo(formID, AddressOf GetParsedArmoCrudo)
    End Function

    ''' <summary>Parse (and cache) an ARMA by FormID. Nothing if the FormID does not resolve to an ARMA.</summary>
    Public Function GetParsedArma(formID As UInteger) As Canon.IArma
        If formID = 0UI Then Return Nothing
        ' Draft-aware resolution — same rule as GetParsedArmo for ARMA drafts (MainForm._armaDrafts): consult the
        ' app's ArmaDraftResolver FIRST; a non-Nothing return is the live draft view and is RETURNED DIRECTLY,
        ' never stored in _armaCache (drafts mutate live). Real FormIDs fall through to the cached real-record path.
        If ArmaDraftResolver IsNot Nothing Then
            Dim draftView = ArmaDraftResolver(formID)
            If draftView IsNot Nothing Then Return draftView
        End If
        Return _armaCache.GetOrAdd(formID,
            Function(fid)
                Dim rec = PluginManager.GetRecord(fid)
                If rec Is Nothing OrElse rec.Header.Signature <> "ARMA" Then Return Nothing
                Return Canon.CanonRecords.Arma(rec, PluginManager)
            End Function)
    End Function


    ''' <summary>Espejo de <see cref="ArmoDataLegacy"/> para ARMA.</summary>


    ''' <summary>Adapta <see cref="GetParsedArmo"/> al modelo legado, para el ÚNICO consumidor que
    ''' todavía lo pide: <see cref="EquipCtx"/> (vía <see cref="EquipResolver.EquipContext.ArmoResolver"/>).</summary>


    ''' <summary>Espejo de <see cref="GetParsedArmoLegacy"/> para ARMA.</summary>


    ''' <summary>Parse (y cachea) un RACE por la vista canónica, keyed por FormID. La NPC's race record se
    ''' re-parsea ~20x/render (skeleton, body-weight, skin resolution, oclusión) — esto lo colapsa a un solo
    ''' parse. No funde acá los tints custom de LooksMenu: el record publicado es SÓLO lo que trae el
    ''' record. Quien necesita la lista de tinte fusionada la pide aparte con
    ''' <see cref="LmCustomTintLoader.Fusionar"/> (que tiene su propia caché por raza+género) — así el
    ''' record que vive en ESTA caché nunca se muta.</summary>
    Public Function ParseRaceCanonCached(rRec As PluginRecord) As Canon.IRace
        If rRec Is Nothing Then Return Nothing
        Return _raceCanonCache.GetOrAdd(rRec.Header.FormID,
            Function(fid) Canon.CanonRecords.Race(rRec, PluginManager))
    End Function

    ''' <summary>El contexto con el que TODA la app llama a la ley única de equip
    ''' (<see cref="EquipResolver"/>, FO4_Base_Library). Un solo constructor: los resolvedores draft-aware,
    ''' la cadena de razas del redirect RNAM y el gate de power-armor viven acá, que es el objeto que ya es
    ''' dueño de ese conocimiento. Ni el render, ni el bake, ni los editores arman el suyo.</summary>
    ' ⛔ El ARMO va por la vista EFECTIVA: este contexto contesta que va a DIBUJAR el motor, y con
    ' `TNAM` la lista de armatures sale del TERMINAL. Y es `AddressOf`, sin parentesis: un grep no lo
    ' ve — por eso el nombre viejo se retiro y cada call site elige AL COMPILAR.
    ' ⛔ Esto NO es BR-14: la efectiva CONSERVA la identidad (`EDID`, `FULL` y el FormID son del HIJO
    ' en los dos juegos), mientras que BR-14 fue sustitucion de identidad — el editor mostrando OTRO
    ' record. Y los editores no editan por aca: leen por `LeerComplementos` sobre el borrador CRUDO.
    Public Function EquipCtx(npcRaceFID As UInteger, isFemale As Boolean) As EquipResolver.EquipContext
        Return New EquipResolver.EquipContext With {
            .PluginManager = PluginManager,
            .RaceFormID = npcRaceFID,
            .IsFemale = isFemale,
            .EffectiveArmorRaces = GetEffectiveArmorRaces(npcRaceFID),
            .ArmoResolver = AddressOf GetParsedArmoEfectivo,
            .ArmaResolver = AddressOf GetParsedArma,
            .IsPowerArmorArmo = ArmoIsPowerArmorDeVista,
            .IsPowerArmorRace = (RaceIsPowerArmor IsNot Nothing AndAlso RaceIsPowerArmor(npcRaceFID))}
    End Function

    ''' <summary>The set of races an armature (ARMA) may be authored for and still fit an actor of
    ''' <paramref name="raceFID"/>: the race itself PLUS every race reached by following the RACE.RNAM
    ''' "Armor Race" redirect chain. Copy-races (e.g. the CC Enclave turret) reuse a base race's armatures;
    ''' the engine matches the ARMA against the Armor Race, not the actor's own race. Cycle-guarded via the
    ''' HashSet (Add returns False on a revisit). Cached per race; the returned set is SHARED — callers must
    ''' not mutate it. Empty when raceFID = 0. See [[23-armor-race-redirect-rnam]].</summary>
    Public Function GetEffectiveArmorRaces(raceFID As UInteger) As HashSet(Of UInteger)
        If raceFID = 0UI Then Return New HashSet(Of UInteger)()
        Return _armorRaceCache.GetOrAdd(raceFID,
            Function(fid) WalkArmorRaceChain(fid, AddressOf GetRecord, AddressOf ParseRaceCanonCached))
    End Function

    ''' <summary>Shared core of <see cref="GetEffectiveArmorRaces"/>: the race itself plus every race
    ''' reached via the RACE.RNAM "Armor Race" redirect chain, cycle-guarded (HashSet.Add returns False
    ''' on a revisit). Delegate-parameterized so the uncached bake path
    ''' (FaceGenBuilder.ResolveOutfitHeadwearSlots, RecordParsers-direct — no ctx there) walks the EXACT
    ''' same chain the render walks (RENDER == BAKE). Any change to the chain rule goes HERE.
    ''' RNAM\Armor Race está en la interfaz común a los dos juegos, así que esto vive en la vista canónica
    ''' sin TryCast.</summary>
    Friend Shared Function WalkArmorRaceChain(raceFID As UInteger,
                                              getRecord As Func(Of UInteger, PluginRecord),
                                              parseRace As Func(Of PluginRecord, Canon.IRace)) As HashSet(Of UInteger)
        Dim races As New HashSet(Of UInteger)()
        Dim cur = raceFID
        While cur <> 0UI AndAlso races.Add(cur)
            Dim rec = getRecord(cur)
            If rec Is Nothing OrElse rec.Header.Signature <> "RACE" Then Exit While
            Dim race = parseRace(rec)
            If race Is Nothing Then Exit While
            cur = race.ArmorRace
        End While
        Return races
    End Function

    ''' <summary>Parse (and cache) an HDPT from an already-fetched record, keyed by its FormID.</summary>
    Public Function ParseHdptCached(hRec As PluginRecord) As Canon.IHdpt
        If hRec Is Nothing Then Return Nothing
        Return _hdptCache.GetOrAdd(hRec.Header.FormID, Function(fid) Canon.CanonRecords.Hdpt(hRec, PluginManager))
    End Function

    ''' <summary>Los bytes del esqueleto extra de una raza (<c>RACE.GNAM</c> → <c>BPTD.MODL</c>), UNA vez
    ''' por raza y por orden de carga. La resolución sigue siendo la de
    ''' <see cref="BodyPartSkeletonResolver.TryLoadBptdSkeletonBytes"/> — acá sólo se memoiza.
    ''' <para>⛔ Los TRES call sites pasan por acá (el render en <c>MainForm.PrepareSkeleton</c> y los dos
    ''' de <see cref="NpcMountingResolver"/>). Una segunda caché de estos mismos bytes en otro objeto
    ''' vuelve a partir la ley en dos: si hace falta invalidar, se invalida en
    ''' <see cref="InvalidateParseCaches"/> y en ningún otro lado.</para></summary>
    Public Function BptdSkeletonBytesCached(raceFormID As UInteger) As Byte()
        Return _bptdSkelBytesCache.GetOrAdd(raceFormID,
            Function(fid) BodyPartSkeletonResolver.TryLoadBptdSkeletonBytes(fid, PluginManager))
    End Function

    ''' <summary>Los sockets del actor por (raza, género), calculados UNA vez con la
    ''' <paramref name="factory"/> del resolvedor —que es quien tiene la ley de las dos fuentes— y
    ''' devueltos después desde la caché.
    ''' <para>⛔ Devuelve una COPIA del diccionario y de la lista de warnings: el llamador sigue siendo
    ''' dueño de lo que recibe, igual que cuando esto se recalculaba entero. Hoy ningún consumidor muta ni
    ''' el diccionario ni un <c>ConnectPointInfo</c> (todos leen <c>.Translation</c>/<c>.Rotation</c>/
    ''' <c>.Scale</c> hacia un <c>Transform_Class</c> NUEVO), pero eso es una propiedad AUDITADA y la
    ''' copia la vuelve una propiedad CONSTRUIDA — que no se puede perder en un cambio futuro. Son 6
    ''' entradas en el corpus de FO4: el copiado no se mide.</para>
    ''' <para>⛔ Los warnings se REPITEN en cada llamada, no sólo en la primera: la salida observable
    ''' tiene que ser idéntica a la de recalcular. Ver el comentario de <c>_actorSocketsCache</c>.</para></summary>
    Public Function ActorSocketsCached(raceFormID As UInteger, isFemale As Boolean,
                                       factory As Func(Of (Sockets As Dictionary(Of String, BSConnectPointReader.ConnectPointInfo),
                                                           Warnings As List(Of String)))) _
                                       As (Sockets As Dictionary(Of String, BSConnectPointReader.ConnectPointInfo),
                                           Warnings As List(Of String))
        Dim guardado = _actorSocketsCache.GetOrAdd((raceFormID, isFemale), Function(k) factory())
        Return (New Dictionary(Of String, BSConnectPointReader.ConnectPointInfo)(guardado.Sockets, StringComparer.OrdinalIgnoreCase),
                New List(Of String)(guardado.Warnings))
    End Function

    ''' <summary>Cuántas entradas tienen las dos cachés de esqueleto. SÓLO LECTURA, para que un testigo
    ''' pueda comprobar que <see cref="InvalidateParseCaches"/> las VACÍA de verdad.
    ''' <para>⛔ Existe porque la propiedad "se vació" no se puede observar de afuera de ninguna otra
    ''' forma honesta: por identidad del arreglo NO se puede —<c>FilesDictionary</c> devuelve la MISMA
    ''' instancia de bytes para el mismo archivo, así que un recálculo real se ve idéntico a un hit (el
    ''' gate se puso rojo con ese testigo falso antes de existir esta propiedad)— y por tiempo sería un
    ''' umbral inventado. Si esta cuenta no baja a 0, un cambio del set de plugins deja vivo el esqueleto
    ''' de la raza del orden anterior.</para></summary>
    Friend ReadOnly Property CuentaDeCachesDeEsqueleto As (Bptd As Integer, Sockets As Integer)
        Get
            Return (_bptdSkelBytesCache.Count, _actorSocketsCache.Count)
        End Get
    End Property

    ''' <summary>Clear every parse cache. Call on load-order change (MainForm.ParseAllNPCs). Clears
    ''' ALL caches the context owns — consistent owner semantics (the old MainForm cleared RACE/HDPT/
    ''' NPC_ but not ARMO/ARMA; harmless given the immutable PluginManager, unified here).</summary>
    Public Sub InvalidateParseCaches()
        NpcCache.Clear()
        _armoCache.Clear()
        _armaCache.Clear()
        _raceCanonCache.Clear()
        _hdptCache.Clear()
        _armorRaceCache.Clear()
        ' Derivadas del MISMO orden de carga (bytes del BPTD.MODL y los sockets del actor): si sobreviven
        ' a un cambio del set de plugins, un FormID de raza reciclado devuelve el esqueleto del anterior.
        _bptdSkelBytesCache.Clear()
        _actorSocketsCache.Clear()
    End Sub

    ''' <summary>Drop the SINGLE record <paramref name="fid"/> from the parse caches (after an in-memory override
    ''' revert) so the next render re-parses it from the now-updated PluginManager. Deliberately NOT
    ''' <see cref="InvalidateParseCaches"/>: that <c>Clear()</c>s EVERY cache (incl. the current NPC + all races),
    ''' and doing so MID-SESSION races the background render threads reading those caches — blanking the whole scene
    ''' until a later render self-heals. A per-key <c>TryRemove</c> is atomic + leaves every other record intact, so
    ''' the reverted armor updates on the next render without breaking anything else. Covers the ARMO/ARMA record
    ''' types a revert touches (OTFT/LVLI aren't cached here — parsed on demand, so a revert already resolves fresh).</summary>
    Public Sub InvalidateRecord(fid As UInteger)
        If fid = 0UI Then Return
        Dim armo As Canon.IArmo = Nothing : _armoCache.TryRemove(fid, armo)
        Dim arma As Canon.IArma = Nothing : _armaCache.TryRemove(fid, arma)
    End Sub
End Class
