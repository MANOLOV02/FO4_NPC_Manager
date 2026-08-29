Imports System.IO
Imports FO4_Base_Library

''' <summary>
''' Catalogs for the Skyrim (RaceMenu/skee64) editors: every list the user picks from is DERIVED from real
''' data — the installed <c>skee64.ini</c>, the merged loose+BSA file dictionary, the plugins, or the NPC's
''' own skeleton. Nothing here is typed by hand and nothing is hardcoded that the game reads from disk.
'''
''' Why this exists: RaceMenu itself has no catalogs in C++. Its menu either lists records the game already
''' loaded (HDPT, RACE tint masks) or runs a plain recursive directory scan through the generic
''' <c>GetExternalFiles</c> Scaleform hook (ScaleformCharGenFunctions.cpp:1497-1589). Our
''' <see cref="FilesDictionary_class"/> is a strict superset of that scan because it also sees inside BSAs.
''' </summary>
Friend Module SseCatalogs

    ''' <summary>The four overlay zones skee64 instantiates on an actor. Hair is declared in
    ''' OverlayInterface.h but Skyrim SE never reads a count for it (main.cpp:774-781 covers Body/Hands/
    ''' Feet/Face only), so it is deliberately absent — offering it would author nodes the engine never
    ''' creates.</summary>
    Friend Enum OverlayZone
        Body = 0
        Hands = 1
        Feet = 2
        Face = 3
    End Enum

    ''' <summary>skee64 hardcoded fallbacks when the ini is missing or the key is absent (main.cpp:120-127).
    ''' <para>OCHO valores: 0-3 = el pool NORMAL de cada zona (<c>iNumOverlays</c>, default 3) y 4-7 = el pool
    ''' MAGIC/spell (<c>iSpellOverlays</c>, default <b>1</b>). Son dos contadores independientes por zona en el
    ''' motor — <c>g_num*Overlays</c> y <c>g_numSpell*Overlays</c> (main.cpp:120-127), con su propia key
    ''' (:775-781), su propio clamp (:810-828) y su propio cero por <c>bEnableFaceOverlays</c> (:833-836).</para></summary>
    Private ReadOnly DefaultOverlayCounts As Integer() = {3, 3, 3, 3, 1, 1, 1, 1}

    ''' <summary>Desplazamiento del pool MAGIC dentro de los arrays de contadores: <c>counts(SpellSlotBase + zona)</c>.</summary>
    Private Const SpellSlotBase As Integer = 4

    ''' <summary>GEMELO DE <c>OVL_SWEEP_MAX</c> EN <c>NPCM_Manolov_ApplySSE.psc</c>. Hasta dónde el
    ''' apply-script saca de la partida del jugador un overlay cuyo nodo skee nunca instancia.
    ''' <para>Es el TOPE DEL MOTOR: skee clampea todo contador de overlay a <c>0x7F</c> (main.cpp:810-828) y los
    ''' crea con <c>for (i = 0; i &lt; count; i++)</c> (OverlayInterface.cpp:659-689), así que <c>[Ovl126]</c> es
    ''' el nodo más alto que puede existir en cualquier instalación, y 127 es el tope EXCLUSIVO que lo cubre sin
    ''' barrer un índice de más. Así NADA de lo que la app pueda autorar queda irrecuperable — no hace falta
    ''' preguntarse cuánto vale el <c>iNumOverlays</c> del jugador, que es suyo y no lo podemos saber.</para>
    ''' <para>Si cambia, hay que cambiarlo en el <c>.psc</c> Y recompilar el <c>.pex</c>: son artefactos
    ''' distintos y nadie los verifica en build. Lo chequea <c>Papyrus\tools\check_sweep_ceiling.py</c>.</para></summary>
    Friend Const OverlaySweepCeiling As Integer = 127

    ' ACA VIVIA `SpellOverlayClearCeiling = 8` y no tenia que existir. Su unica
    ' justificacion era que "Papyrus no expone getter del contador del pool magic", y es FALSO:
    ' `NiOverride.GetNumSpellBodyOverlays` y sus tres hermanas estan registradas
    ' (`PapyrusNiOverride.cpp:1844-1853`) y son `NoWait`. El apply-script ahora las usa, asi que
    ' apaga exactamente los nodos que el juego del jugador creo, y la promesa "lo que se escribe se puede
    ' deshacer" se cumple sin ningun techo inventado.
    ' Se fueron con ella: el descarte de overlays magic en el emisor (que perdia dato del usuario en
    ' silencio), la rama del reporte, la negativa de los editores con numero propio, el gemelo en el
    ' `.psc` y su entrada en `check_sweep_ceiling.py`. El limite del pool magic es ahora el mismo
    ' que el del normal: `SpellOverlayCount`, el contador del MOTOR.

    Private ReadOnly IniSectionByZone As String() = {"Overlays/Body", "Overlays/Hands", "Overlays/Feet", "Overlays/Face"}
    Private ReadOnly NodePrefixByZone As String() = {"Body", "Hands", "Feet", "Face"}

    ''' <summary>Root under which RaceMenu keeps overlay textures. Taken from the shipped RaceMenu UI asset
    ''' (the string <c>textures\actors\character\overlays</c> is embedded in RaceMenu.bsa) and corroborated by
    ''' <c>skee64.ini</c>'s <c>sDefaultTexture=textures\actors\character\overlays\default.dds</c>. Real mods
    ''' nest below it (e.g. <c>…\Overlays\Skin Features\Freckles\Body\x.dds</c>), so the scan is recursive.</summary>
    Friend Const OverlayTextureRoot As String = "Textures\Actors\Character\Overlays\"

    Private ReadOnly _lock As New Object()
    Private _overlayCounts As Integer()
    Private _skeeIniStamp As String
    Private _skeeIniSource As String
    Private _overlayLimitWarned As Integer
    Private _spellLimitWarned As Integer
    Private _faceDisabledByFlag As Boolean
    ' Los dos knobs de [FaceGen] viajan con los contadores: MISMA lectura, MISMO stamp, MISMO cache. Separarlos
    ' habría dado un segundo lector del mismo par de archivos que puede quedar desfasado del primero.
    Private _faceSliderMultiplier As Double = DefaultSliderMultiplier
    Private _faceSliderInterval As Double = DefaultSliderInterval
    ''' <summary>Default HARDCODEADO de skee: <c>g_extendedMorphs = true</c> (main.cpp:150).</summary>
    Private _faceExtendedMorphs As Boolean = True

    ''' <summary>True cuando el cero de la cara NO viene de <c>iNumOverlays</c> sino de
    ''' <c>[Features] bEnableFaceOverlays=0</c>. Es la diferencia entre mandar al usuario a la key correcta o a
    ''' una que ya tiene puesta en 6.</summary>
    Friend Function FaceOverlaysDisabledByIni() As Boolean
        EnsureSkeeIniValues()
        Return _faceDisabledByFlag
    End Function

    Private ReadOnly SkeeIniNames As String() = {"skee64.ini", "skee64_custom.ini"}

    ''' <summary>Number of overlay slots the engine will instantiate for <paramref name="zone"/>, i.e. the
    ''' valid range of <c>[Ovl{n}]</c> indices skee64 CREATES is <c>0 .. count-1</c>. Read from
    ''' <c>Data\SKSE\Plugins\skee64.ini</c> (then <c>skee64_custom.ini</c>, which overrides it — main.cpp:239,249).
    ''' <para>This is an ADVISORY bound, not a hard one: an overlay past it is stored by NiOverride keyed by node
    ''' name and simply never matches a node (OverrideInterface.cpp:750-764 looks the node up and returns when it
    ''' is absent), so it is inert in-game and becomes live the day the count is raised. The editors therefore
    ''' warn once instead of refusing.</para></summary>
    Friend Function OverlayCount(zone As OverlayZone) As Integer
        EnsureSkeeIniValues()
        Return _overlayCounts(CInt(zone))
    End Function

    ''' <summary>Lo mismo para el pool MAGIC: cuántos <c>[SOvl{n}]</c> instancia el MOTOR en esta zona
    ''' (<c>iSpellOverlays</c>, default 1 — main.cpp:124-127/775-781).
    ''' <para>ES EL NÚMERO DEL MOTOR, SIN CLAMPEAR — igual que <see cref="OverlayCount"/> para el pool normal,
    ''' y es el ÚNICO límite del pool magic. Estuvo clampeado a un techo propio de la app (8) y eso hacía que la app
    ''' AFIRMARA UN HECHO FALSO sobre el juego: con <c>iSpellOverlays=20</c> los reportes decían "los 8 slots que
    ''' RaceMenu crea". Ese techo se fue (ver el bloque donde vivía la constante): el apply-script pregunta el
    ''' contador real con <c>GetNumSpell*Overlays</c>, así que no hay una segunda pregunta que responder.</para></summary>
    Friend Function SpellOverlayCount(zone As OverlayZone) As Integer
        EnsureSkeeIniValues()
        Return _overlayCounts(SpellSlotBase + CInt(zone))
    End Function

    ' ACÁ VIVÍA `SpellOverlayAuthorLimit(zone) = Math.Min(SpellOverlayCount(zone), SpellOverlayClearCeiling)`.
    ' BORRADO por DOS razones, y la segunda es la que importa:
    '   1. No tenía NI UN llamador de producto (sólo el gate y el README lo nombraban): superficie que existe para
    ' el test y viaja igual en el binario que se distribuye — lo mismo que el de ResolveOverlayCounts
    '      prohíbe 100 líneas más abajo.
    '   2. Su DEFINICIÓN estaba mal para lo único que decía gobernar. Con `iSpellOverlays=1`, el mínimo da 1 y la
    '      app se habría negado a autorar `[SOvl1]`. Y hoy la pregunta ni existe: el único límite del pool magic
    '      es el contador del MOTOR (`SpellOverlayCount`), igual que en el pool normal — el techo propio de la app
    '      que esta función intentaba combinar también se fue.

    ''' <summary>El contador DEL MOTOR del pool que corresponda — el único lugar donde se elige entre los dos.</summary>
    Friend Function OverlayCount(zone As OverlayZone, isSpell As Boolean) As Integer
        Return If(isSpell, SpellOverlayCount(zone), OverlayCount(zone))
    End Function

    ' ACÁ VIVÍA `SpellPoolFullNotice(zone)`, el texto de la NEGATIVA a autorar en el pool magic.
    ' Se fue con la negativa: el pool magic ahora AVISA y deja seguir, igual que el normal, así que el único
    ' texto es `OverlayLimitNotice(..., isSpell:=True)` — que además es mejor, porque manda a la key que de
    ' verdad gobierna el nodo (`iSpellOverlays`, o `bEnableFaceOverlays` cuando ése es el que manda).
    ' Lo único suyo que valía —"o usá un overlay normal"— quedó plegado en ese aviso.
    ' Y si se hubiera dejado, habría quedado sin NI UN llamador de producto: superficie que sólo usa el
    ' gate y viaja igual en el binario que se distribuye, que es lo que prohíbe el de ResolveOverlayCounts.

    ''' <summary>Which file the counts actually came from, for the warning text. It is the whole point of the
    ''' message: "I raised iNumOverlays and nothing changed" is almost always a DIFFERENT copy of the ini (a mod
    ''' manager's virtual Data, another install), and naming the path we read settles it without guessing.</summary>
    Friend Function SkeeIniSource() As String
        EnsureSkeeIniValues()
        Return _skeeIniSource
    End Function

    ''' <summary>The counts are re-read whenever an ini's timestamp/size changes. They used to be read ONCE per
    ''' process, so editing skee64.ini while the app was open changed nothing and the editor still told the user to
    ''' "reopen the editor" — which re-entered the same cached value. A stat of two files per Add click is free.</summary>
    Private Sub EnsureSkeeIniValues()
        SyncLock _lock
            ' EL STAMP SE SACA DENTRO DEL LOCK. Afuera, entre el stat y la lectura hay una ventana: si el ini
            ' se reemplaza ahí en el medio y después vuelve con su mtime y su largo originales (swap de perfil de
            ' un mod manager, robocopy /COPY:DT, extraer un 7z — todos preservan mtime), el cache se quedaba con
            ' los números del intruso bajo el stamp del archivo bueno, y ya no relee nunca.
            Dim stamp = SkeeIniStamp()
            If _overlayCounts IsNot Nothing AndAlso String.Equals(_skeeIniStamp, stamp, StringComparison.Ordinal) Then Return
            ' SE MERGEAN LOS STRINGS CRUDOS, NO LOS NÚMEROS. skee no parsea por archivo: junta el VALOR
            ' TEXTUAL de la key —base, y encima el custom si trae algo (main.cpp:258-282, "Only take custom if
            ' we have it")— y recién ahí corre UN sscanf sobre el resultado. La diferencia se ve con
            ' base `iNumOverlays=6` + custom `iNumOverlays=hola`: skee se queda con "hola", el sscanf falla, y
            ' el valor queda en el DEFAULT HARDCODEADO (3) — el 6 lo destruyó la línea basura del custom. Si se
            ' parsea por archivo, en cambio, el 6 sobrevive. Diríamos 6 donde el motor da 3, que es justo el
            ' error de "te ofrezco un slot que el motor no crea".
            Dim raw = NewRawSkeeValues()
            Dim read As New List(Of String), failed As New List(Of String)
            For Each iniPath In SkeeIniPaths()
                If Not File.Exists(iniPath) Then Continue For
                Try
                    ' El todo-o-nada por archivo vive DENTRO de MergeSkeeIni, pegado a la mutación.
                    MergeSkeeIni(File.ReadLines(iniPath), raw)
                    read.Add(iniPath)
                Catch ex As Exception
                    ' El archivo EXISTE y no se pudo leer: eso va al mensaje, no se calla como "no está".
                    failed.Add($"{iniPath} (present but unreadable: {ex.GetType().Name})")
                    Logger.LogLazy(Function() $"[SSE-CATALOG] could not read {iniPath}: {ex.GetType().Name}: {ex.Message}")
                End Try
            Next
            Dim faceOff As Boolean
            Dim counts = ResolveOverlayCounts(raw, faceOff)
            Dim knobs = ResolveFaceGenSliderKnobs(raw)
            Dim extended = ResolveExtendedMorphsEnabled(raw)
            _faceDisabledByFlag = faceOff
            _faceSliderMultiplier = knobs.Multiplier
            _faceSliderInterval = knobs.Interval
            _faceExtendedMorphs = extended
            Dim folder = SkeeIniFolder()
            If read.Count > 0 OrElse failed.Count > 0 Then
                _skeeIniSource = String.Join(" + ", read.Concat(failed))
                If failed.Count > 0 Then _skeeIniSource &= " — the unreadable one contributed NOTHING"
            ElseIf folder = "" Then
                _skeeIniSource = "no game folder configured — RaceMenu's built-in defaults"
            Else
                _skeeIniSource = $"no skee64.ini in {folder} — RaceMenu's built-in defaults"
            End If
            _overlayCounts = counts
            ' UNA LECTURA FALLIDA NO SE CACHEA. El stamp sale de FileInfo (Exists/mtime/Length), que son
            ' consultas de METADATO: funcionan igual sobre un archivo abierto con FileShare.None. O sea que sólo
            ' falla el ReadLines — y guardar el stamp igual dejaba los defaults pegados PARA TODA LA SESIÓN bajo
            ' un stamp válido que nunca vuelve a cambiar. Es el mismo bug que vino a arreglar todo esto ("reabrí
            ' el editor y sigue igual"), entrando por el camino de error. Con _skeeIniStamp = Nothing el próximo
            ' click reintenta, que es lo que el comentario del stamp siempre dijo y no hacía.
            _skeeIniStamp = If(failed.Count = 0, stamp, Nothing)
        End SyncLock
    End Sub

    Private Function SkeeIniFolder() As String
        Dim data = Config_App.Current?.DataPath
        If String.IsNullOrEmpty(data) Then Return ""
        Return Path.Combine(data, "SKSE", "Plugins")
    End Function

    Private Iterator Function SkeeIniPaths() As IEnumerable(Of String)
        Dim folder = SkeeIniFolder()
        If folder = "" Then Return
        For Each name In SkeeIniNames
            Yield Path.Combine(folder, name)
        Next
    End Function

    ''' <summary>Identity of the ini pair on disk: path + write time + length. Any edit moves it.</summary>
    Private Function SkeeIniStamp() As String
        Dim sb As New Text.StringBuilder()
        For Each iniPath In SkeeIniPaths()
            sb.Append(iniPath).Append("|"c)
            Try
                Dim fi As New FileInfo(iniPath)
                If fi.Exists Then sb.Append(fi.LastWriteTimeUtc.Ticks).Append(":"c).Append(fi.Length)
            Catch
                ' An unreadable ini stamps as absent: the next click retries, which is what we want.
            End Try
            sb.Append(";"c)
        Next
        Return sb.ToString()
    End Function

    ''' <summary>True la PRIMERA vez, para toda la sesión. El overlay de más es legal (ver
    ''' <see cref="OverlayCount"/>), así que el aviso es información y repetirlo en cada Add sería un estorbo.
    ''' <para>UNA SOLA CLASE, Y ESO ES CONSECUENCIA DEL BARRIDO COMPLETO. Mientras el script barría hasta un
    ''' techo bajo existía un segundo aviso —"una partida que lo tome ya no lo suelta"— que NO podía compartir
    ''' disparo con el benigno. Subiendo <c>OVL_SWEEP_MAX</c> al tope del motor esa categoría dejó de existir:
    ''' todo lo autorable se puede deshacer, así que vuelve a haber un solo mensaje.</para></summary>
    Friend Function ClaimOverlayLimitWarning() As Boolean
        Return ClaimOverlayLimitWarning(False)
    End Function

    ''' <summary>Un one-shot POR POOL.
    ''' <para>ERA UNO SOLO PARA LOS DOS, y eso lo volvía peor que inútil: los dos mensajes NO dicen lo mismo (uno
    ''' manda a subir <c>iNumOverlays</c>; el del pool magic explica <c>iSpellOverlays</c> y por qué la app ofrece
    ''' menos slots de los que el ini declara). Con un one-shot compartido, el primero que se disparara se comía el
    ''' aviso del otro — o sea que el usuario que ya vio el del pool normal NUNCA se enteraba de lo del magic, que es
    ''' la única explicación de por qué se le ofrecen menos.</para></summary>
    Friend Function ClaimOverlayLimitWarning(isSpell As Boolean) As Boolean
        If isSpell Then Return Threading.Interlocked.Exchange(_spellLimitWarned, 1) = 0
        Return Threading.Interlocked.Exchange(_overlayLimitWarned, 1) = 0
    End Function

    ''' <summary>The one-per-session notice: qué va a hacer el motor con este overlay, y de qué archivo salió el
    ''' número — que es la mitad del diagnóstico cuando alguien dice "subí iNumOverlays y no cambió nada".</summary>
    Friend Function OverlayLimitNotice(zone As OverlayZone, index As Integer, limit As Integer,
                                       Optional isSpell As Boolean = False) As String
        ' LA KEY A LA QUE SE MANDA AL USUARIO DEPENDE DE POR QUÉ EL CONTADOR ES EL QUE ES: con
        ' bEnableFaceOverlays=0, subir iNumOverlays no hace absolutamente nada — y decirle que lo suba es
        ' recrear el bug que originó todo esto (sube la key, no pasa nada, la app repite el mismo cartel).
        ' Y DEPENDE DEL POOL: el magic tiene su PROPIA key (iSpellOverlays). Mandar a subir iNumOverlays por un
        ' [SOvl] es el mismo modo de falla — la key que se sube no es la que gobierna el nodo.
        Dim tag = If(isSpell, "SOvl", "Ovl")
        Dim keyName = If(isSpell, "iSpellOverlays", "iNumOverlays")
        Dim byFlag = zone = OverlayZone.Face AndAlso limit = 0 AndAlso FaceOverlaysDisabledByIni()
        Dim created = If(limit > 0, $"[{tag}0]…[{tag}{limit - 1}]", "none")
        Dim because = If(byFlag, $"because [Features] bEnableFaceOverlays=0 turns face overlays off entirely — both pools — whatever {keyName} says",
                                 $"per {keyName} in skee64.ini")
        Dim toRaise = If(byFlag, "bEnableFaceOverlays is set back to 1", $"{keyName} is raised")
        ' ACÁ NO VA NINGUNA SALVEDAD SOBRE UN TECHO DE LA APP, porque ya no hay ninguno: `limit` es el contador
        ' DEL MOTOR y es el único límite del pool magic. Mientras el aviso mezclaba dos números decía tres cosas
        ' falsas a la vez: que el motor crea 8 (crea los que diga el ini),
        ' que "no pinta nada" (pintaba), y que se arregla subiendo la key (no cambiaba nada).
        ' DECÍA "in-game they start switched off" y eso era una INFERENCIA presentada como hecho. MEDIDO sobre
        ' `*_magicoverlay.nif` (parseo del bloque, 2026-08-10): el controller es
        ' BSEffectShaderPropertyFloatController con typeOfControlledVariable=5 (=Alpha), target = el
        ' BSLightingShaderProperty, flags 0x4A = ACTIVE + CYCLE_REVERSE, frequency 8, keys (t=0,v=0)→(t=10,v=1)
        ' lineales ⇒ la alpha la ANIMA EL MOTOR, pulsando 0↔1. No hay un cuadro "en reposo": por eso el preview
        ' principal no lo dibuja (el retrato del NPC no es un efecto en curso) y el del editor lo muestra en su PICO.
        Dim previewNote = If(isSpell,
            "It is added anyway: it is saved, shown at full strength in THIS editor's preview (the main preview " &
            "leaves magic overlays out because the game animates their opacity, so they fade in and out instead of " &
            "sitting still) and written to the NPC.",
            "It is added anyway: it is saved, rendered in the preview and written to the NPC.")
        Return $"{NodePrefixByZone(CInt(zone))} [{tag}{index}] is past the {limit} {zone.ToString().ToLowerInvariant()} " &
               $"{If(isSpell, "MAGIC ", "")}overlay slot(s) RaceMenu creates ({created}), {because}." & vbCrLf & vbCrLf &
               previewNote & " In-game skee64 creates no such " &
               $"node, so it paints nothing — no error, no broken script — and it starts painting if {toRaise}." & vbCrLf & vbCrLf &
               If(isSpell, "You can also remove a magic overlay from this zone and reuse its slot, or use a normal " &
                            "(non-magic) overlay instead." & vbCrLf & vbCrLf, "") &
               "Removing it later: the helper script clears every slot RaceMenu can build, whether or not this game " &
               "ever built it, so it never " &
               "gets stuck in a save. (Within a running game a magic overlay's alpha is driven by the effect's own " &
               "animation, so it can keep showing until the actor's 3D reloads.)" & vbCrLf & vbCrLf &
               $"Counts read from: {SkeeIniSource()}" & vbCrLf &
               "(Shown once per session, for every zone.)"
    End Function

    ''' <summary>Los ONCE valores CRUDOS (sin parsear) que la app necesita del <c>skee64.ini</c>: índices 0-3 =
    ''' el <c>iNumOverlays</c> de cada zona, índice 4 = <c>[Features] bEnableFaceOverlays</c>, índices 5-8 = el
    ''' <c>iSpellOverlays</c> de cada zona (el pool MAGIC), índices 9-10 = <c>[FaceGen] fSliderMultiplier</c> y
    ''' <c>fSliderInterval</c>. <c>""</c> = ausente, que es lo que devuelve <c>GetPrivateProfileString</c>
    ''' cuando la key no está.
    ''' <para>El flag de la cara se queda en el índice 4 y los nuevos van DESPUÉS a propósito: así el layout
    ''' viejo (0-4) no se corre ni un lugar y ningún lector existente cambia de significado. Los dos de FaceGen
    ''' siguen la misma regla y por eso van al final.</para></summary>
    Private Const RawFaceFlagSlot As Integer = 4
    ''' <summary>Primer índice del <c>iSpellOverlays</c> crudo: <c>RawSpellSlotBase + zona</c>.</summary>
    Private Const RawSpellSlotBase As Integer = 5
    ''' <summary><c>[FaceGen] fSliderMultiplier</c> crudo — el ANCHO de los sliders extendidos de cara
    ''' (main.cpp:842).</summary>
    Private Const RawSliderMultiplierSlot As Integer = 9
    ''' <summary><c>[FaceGen] fSliderInterval</c> crudo — el PASO de esos sliders (main.cpp:843).</summary>
    Private Const RawSliderIntervalSlot As Integer = 10
    ''' <summary><c>[FaceGen] bExtendedMorphs</c> crudo — el interruptor que decide si RaceMenu aplica
    ''' SUS morphs de cara o ninguno (main.cpp:854).</summary>
    Private Const RawExtendedMorphsSlot As Integer = 11

    Private Function NewRawSkeeValues() As String()
        Return {"", "", "", "", "", "", "", "", "", "", "", ""}
    End Function

    ''' <summary>Mergea UN archivo sobre <paramref name="raw"/> con la regla de skee: el valor de este archivo
    ''' pisa al anterior SÓLO si trae algo (<c>if (resultLen &gt; 0)</c>, main.cpp:276). Como el orden de llamada
    ''' es base y después custom, eso da "custom gana si no está vacío".
    ''' <para>TODO O NADA. La fuente es perezosa: si tira a mitad (VFS de un mod manager, Data en red, ACL) las
    ''' keys ya vistas quedarían aplicadas y el resto no — ni un ini ni el otro. Se junta en un array aparte y se
    ''' mergea recién cuando la enumeración terminó bien.</para></summary>
    ''' <param name="lines">La fuente de líneas, NO un path — <c>File.ReadLines</c> va tirando del
    ''' <c>StreamReader</c> a medida que se enumera, así que la excepción del medio llega ACÁ. Recibir el
    ''' enumerable en vez de abrir el archivo adentro es lo que deja al gate reproducir ese fallo exacto sin
    ''' ningún hook de test en el binario.</param>
    Friend Sub MergeSkeeIni(lines As IEnumerable(Of String), raw As String())
        Dim fileValues = CollectSkeeIniValues(lines)
        For i = 0 To raw.Length - 1
            If fileValues(i) <> "" Then raw(i) = fileValues(i)
        Next
    End Sub

    ''' <summary>Minimal INI reader for the keys that decide how many overlay nodes skee builds:
    ''' <c>[Overlays/*] iNumOverlays</c>, <c>[Features] bEnableFaceOverlays</c> y los dos de <c>[FaceGen]</c>.
    ''' Deliberately not a general INI parser — sólo hacen falta esos ONCE valores, y SIN parsear: el número sale después, de una sola pasada
    ''' sobre el string ya mergeado, que es como lo hace skee.
    ''' <para>Sin modelar: <c>GetPrivateProfileString</c> saca las comillas de <c>"6"</c>. Acá un valor
    ''' entrecomillado no parsea y la zona queda en su default. Nadie entrecomilla un entero en skee64.ini.</para>
    ''' <para>(Eran CINCO hasta que se modeló el pool magic, nueve con él, y ONCE desde que entraron
    ''' <c>fSliderMultiplier</c>/<c>fSliderInterval</c>. Ver <see cref="RawSpellSlotBase"/> y
    ''' <see cref="RawSliderMultiplierSlot"/> — el literal del array vive en <see cref="NewRawSkeeValues"/> y el
    ''' gate tiene su gemelo, que YA se cayó una vez por no actualizarlo.)</para>
    ''' </summary>
    Private Function CollectSkeeIniValues(lines As IEnumerable(Of String)) As String()
        Dim values = NewRawSkeeValues()
        Dim seen(values.Length - 1) As Boolean
        Dim section As String = ""
        ' GetPrivateProfileString entra a la PRIMERA sección con ese nombre y no sale de ahí: si el archivo
        ' repite `[Overlays/Body]` más abajo, lo de la segunda NO EXISTE para el motor. Importa porque es
        ' exactamente cómo la gente edita estos archivos — pegan el bloque que dice la descripción del mod al
        ' final, dejando el original arriba. Sin esto la app leía el de abajo (20) y el motor el de arriba (6):
        ' ofrecía 14 slots que no se instancian y volvía a decir "subí iNumOverlays y no pasó nada".
        Dim closedSections As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim sectionIsRepeat = False
        For Each rawLine In lines
            Dim line = rawLine.Trim()
            If line.Length = 0 OrElse line.StartsWith(";") Then Continue For
            If line.StartsWith("[") Then
                ' EL HEADER TERMINA EN EL PRIMER "]", NO AL FINAL DE LA LÍNEA. Win32 acepta
                ' `[Overlays/Body] ; six slots` como sección y tira el resto. Exigiendo EndsWith("]"), esa línea
                ' NO abría sección — y entonces el `iNumOverlays=6` de abajo se le atribuía a la sección que
                ' seguía abierta, o sea a OTRA ZONA. No era una lectura perdida: era una inventada (la app
                ' ofrecía Hands [Ovl0..5] donde skee crea 3). Comentar al lado del header es de las formas más
                ' comunes de editar estos archivos a mano.
                ' Y una línea que EMPIEZA con "[" es header aunque NUNCA cierre: Win32 toma el nombre hasta
                ' el primer "]" o hasta el fin de línea. Descartándola (el `If close > 0` que había acá), un
                ' `[Overlays/Body` sin cerrar no abría sección y su iNumOverlays se le atribuía a la anterior —
                ' otra vez un número INVENTADO en la zona equivocada. Un "[" pelado da nombre "", que es
                ' exactamente la sección sin nombre de Win32: cierra la anterior y no matchea ninguna zona.
                Dim close = line.IndexOf("]"c)
                If section <> "" Then closedSections.Add(section)
                section = If(close > 0, line.Substring(1, close - 1), line.Substring(1)).Trim()
                sectionIsRepeat = closedSections.Contains(section)
                Continue For
            End If
            If sectionIsRepeat Then Continue For
            Dim eq = line.IndexOf("="c)
            If eq <= 0 Then Continue For
            Dim key = line.Substring(0, eq).Trim()
            Dim slot = -1
            If key.Equals("bEnableFaceOverlays", StringComparison.OrdinalIgnoreCase) AndAlso
               section.Equals("Features", StringComparison.OrdinalIgnoreCase) Then
                slot = RawFaceFlagSlot
            ElseIf key.Equals("iNumOverlays", StringComparison.OrdinalIgnoreCase) Then
                For z = 0 To IniSectionByZone.Length - 1
                    If section.Equals(IniSectionByZone(z), StringComparison.OrdinalIgnoreCase) Then slot = z
                Next
            ElseIf key.Equals("iSpellOverlays", StringComparison.OrdinalIgnoreCase) Then
                ' El pool MAGIC vive en la MISMA sección por zona que el normal, con otra key (main.cpp:775-781).
                For z = 0 To IniSectionByZone.Length - 1
                    If section.Equals(IniSectionByZone(z), StringComparison.OrdinalIgnoreCase) Then slot = RawSpellSlotBase + z
                Next
            ElseIf section.Equals("FaceGen", StringComparison.OrdinalIgnoreCase) Then
                ' Los dos knobs de los sliders EXTENDIDOS de cara (main.cpp:842-843). Van por el mismo camino
                ' crudo que los contadores: se juntan los strings de los dos ini y se parsea UNA sola vez.
                If key.Equals("fSliderMultiplier", StringComparison.OrdinalIgnoreCase) Then
                    slot = RawSliderMultiplierSlot
                ElseIf key.Equals("fSliderInterval", StringComparison.OrdinalIgnoreCase) Then
                    slot = RawSliderIntervalSlot
                ElseIf key.Equals("bExtendedMorphs", StringComparison.OrdinalIgnoreCase) Then
                    slot = RawExtendedMorphsSlot
                End If
            End If
            If slot < 0 Then Continue For
            ' GetPrivateProfileString devuelve la PRIMERA aparición de una key repetida, no la última.
            If seen(slot) Then Continue For
            seen(slot) = True
            values(slot) = line.Substring(eq + 1).Trim()
        Next
        Return values
    End Function

    ''' <summary>Del string mergeado al número, en el orden de skee: <c>sscanf("%u")</c> → clamp a 0x7F
    ''' (main.cpp:810-828) → y al final el cero de la cara por <c>bEnableFaceOverlays</c> (main.cpp:833-836).
    ''' Una key ausente o que no parsea deja el default hardcodeado (el <c>res = false</c> que no asigna).
    ''' <para>NO agregar una sobrecarga sin el <c>ByRef</c> "para que el gate quede más lindo": la que
    ''' había no tenía UN SOLO llamador de producto, o sea superficie que existe únicamente para el test y que
    ''' igual viaja en el binario que se distribuye. El gate declara su propio local. Ver
    ''' 00-reglas-self-tests-no-van-en-el-binario.</para></summary>
    Friend Function ResolveOverlayCounts(raw As String(), ByRef faceDisabledByFlag As Boolean) As Integer()
        Dim counts = DirectCast(DefaultOverlayCounts.Clone(), Integer())
        Dim n As UInteger
        faceDisabledByFlag = False
        For z = 0 To IniSectionByZone.Length - 1
            If ScanUInt32Like(raw(z), n) Then counts(z) = CInt(Math.Min(n, CUInt(&H7F)))
            ' El pool MAGIC: MISMO parseo y MISMO clamp 0x7F, variable aparte (main.cpp:812-813/827-828).
            If ScanUInt32Like(raw(RawSpellSlotBase + z), n) Then counts(SpellSlotBase + z) = CInt(Math.Min(n, CUInt(&H7F)))
        Next
        ' g_enableFaceOverlays: default true (main.cpp:139); el bool de skee también sale por "%u" y vale
        ' `tmp > 0` (main.cpp:313-326). El cero va DESPUÉS del clamp y sólo sobre la cara: sin esto la app
        ' ofrecía slots de cara que el motor no instancia — decía que sí y no pintaba nada.
        ' Y CERO EN LOS DOS POOLS DE LA CARA: el flag apaga `g_numFaceOverlays` **y**
        ' `g_numSpellFaceOverlays` (main.cpp:833-836), no sólo el primero. Decía "no se modela
        ' g_numSpellFaceOverlays porque la app nunca autora [SOvl]" — ahora los autora, así que se modela.
        If ScanUInt32Like(raw(RawFaceFlagSlot), n) AndAlso n = 0UI Then
            counts(CInt(OverlayZone.Face)) = 0
            counts(SpellSlotBase + CInt(OverlayZone.Face)) = 0
            ' EL MOTIVO VIAJA CON EL NÚMERO. Sin esto el mensaje decía "0 slots, per iNumOverlays" y mandaba a
            ' subir una key que el archivo ya tiene en 6: el usuario la sube, no cambia nada, y la app le vuelve
            ' a decir lo mismo. Es el mismo modo de falla que originó todo esto, recreado por su propio arreglo.
            faceDisabledByFlag = True
        End If
        Return counts
    End Function

    ''' <summary>Lo que hace <c>sscanf(s, "%u", &amp;out)</c>, que es <c>strtoul</c> (C11 7.21.6.2 §12): saltea
    ''' espacios, acepta un signo OPCIONAL —sí, también <c>-</c>— y toma todos los dígitos que siga habiendo.
    ''' Es PREFIJO: lo que venga después no invalida nada (<c>"6abc"</c> → 6), y devuelve False sólo si no llegó
    ''' a leer ni un dígito, que es el <c>res = false</c> de skee (deja el valor anterior).
    ''' <para>El signo NIEGA EN UNSIGNED (7.22.1.4 §3), que es de donde sale el 4294967292 de un <c>-4</c> — y
    ''' de ahí el clamp lo deja en 127, no en 0.</para>
    ''' <para>EN OVERFLOW <c>strtoul</c> SATURA en <c>ULONG_MAX</c> (errno ERANGE), no envuelve. Envolver en
    ''' 2^32 —como hacía esto— daba <c>iNumOverlays=4294967296</c> → 0 donde el motor da 127.</para></summary>
    Private Function ScanUInt32Like(s As String, ByRef result As UInteger) As Boolean
        result = 0UI
        If String.IsNullOrEmpty(s) Then Return False
        Dim i = 0
        While i < s.Length AndAlso Char.IsWhiteSpace(s(i)) : i += 1 : End While
        Dim negate = False
        If i < s.Length AndAlso (s(i) = "-"c OrElse s(i) = "+"c) Then
            negate = s(i) = "-"c
            i += 1
        End If
        Dim digits = 0
        Dim acc As ULong = 0UL
        Dim saturated = False
        While i < s.Length AndAlso s(i) >= "0"c AndAlso s(i) <= "9"c
            If Not saturated Then
                acc = acc * 10UL + CULng(Asc(s(i)) - Asc("0"c))
                If acc > &HFFFFFFFFUL Then saturated = True
            End If
            digits += 1
            i += 1
        End While
        If digits = 0 Then Return False
        If saturated Then
            ' ULONG_MAX, con signo o sin él: strtoul reporta ERANGE y devuelve el tope en los dos casos.
            result = UInteger.MaxValue
            Return True
        End If
        ' `0UL - acc` DESBORDA: VB chequea overflow por default y `0-4` en ULong tira OverflowException —
        ' que acá subía como "ini presente pero ilegible" y se comía el archivo entero. El complemento a 2^32
        ' se calcula restando DESDE 2^32, que nunca baja de 1 porque acc ya está enmascarado a 32 bits.
        If negate Then acc = (&H100000000UL - acc) And &HFFFFFFFFUL
        result = CUInt(acc)
        Return True
    End Function

    ''' <summary>Defaults HARDCODEADOS de skee para los dos knobs de <c>[FaceGen]</c> (main.cpp:156-157). Es lo
    ''' que vale sin ini, con la key ausente, o con un valor que no parsea — los tres casos en que
    ''' <c>SKEE64GetConfigValue</c> devuelve false y no asigna (main.cpp:301-310).</summary>
    Private Const DefaultSliderMultiplier As Double = 1.0R
    Private Const DefaultSliderInterval As Double = 0.01R

    ''' <summary>Lo que hace <c>sscanf(s, "%f", &amp;out)</c>, que es <c>strtof</c> (C11 7.22.1.3): saltea espacios,
    ''' acepta signo opcional, dígitos con punto decimal opcional y un exponente opcional. Es PREFIJO —
    ''' <c>"3.0 ; tres veces"</c> da 3.0— y devuelve False sólo si no llegó a leer ni un dígito, que es el
    ''' <c>res = false</c> de skee (deja el default).
    ''' <para>⛔ EL PUNTO ES SIEMPRE EL SEPARADOR DECIMAL, y esto SE PUEDE CITAR en vez de suponerlo: un
    ''' programa C arranca en la locale <c>"C"</c> (C11 7.11.1.1 §4) y sólo se mueve de ahí con
    ''' <c>setlocale</c> — y en todo <c>skee64</c> hay <b>CERO</b> llamadas a <c>setlocale</c> (y cero
    ''' <c>std::locale</c>/<c>imbue</c>), o sea que el CRT del plugin nunca sale de "C". Por lo tanto en un
    ''' Windows es-AR <c>3,5</c> NO vale 3,5 para el motor: <c>strtof</c> corta en la coma y devuelve <b>3</b>.
    ''' Por eso acá se parsea con <c>InvariantCulture</c> y SIN <c>AllowThousands</c>; usar la locale del
    ''' usuario nos daría 3,5 donde el juego usa 3 — el mismo modo de falla que documenta
    ''' <c>TinySliderTextBox.ParseInvariantOrLocal</c>, pero al revés y contra el motor.</para>
    ''' <para>⚠️ HUECOS DECLARADOS, los dos por lo mismo (se reconoce sólo la gramática decimal):
    ''' <c>strtof</c> del UCRT acepta además <c>inf</c>/<c>nan</c> y <b>hexadecimales</b> (<c>0x10</c> ⇒ 16).
    ''' Acá los tres devuelven False y dejan el default, donde skee tomaría el número. Ningún
    ''' <c>skee64.ini</c> real escribe un multiplicador así, y propagar un infinito a la UI sería peor que
    ''' declarar el hueco — pero es un hueco, no una equivalencia.</para>
    ''' <para>El resultado se estrecha a <c>Single</c> porque las dos globales de skee son <c>float</c>
    ''' (main.cpp:156-157): comparar en Double lo que el motor guarda en Single haría que la app y el juego
    ''' difieran justo en el borde del clamp.</para></summary>
    Private Function ScanFloatLike(s As String, ByRef result As Double) As Boolean
        result = 0.0R
        If String.IsNullOrEmpty(s) Then Return False
        Dim i = 0
        While i < s.Length AndAlso Char.IsWhiteSpace(s(i)) : i += 1 : End While
        Dim start = i
        If i < s.Length AndAlso (s(i) = "-"c OrElse s(i) = "+"c) Then i += 1
        Dim digits = 0
        While i < s.Length AndAlso s(i) >= "0"c AndAlso s(i) <= "9"c : digits += 1 : i += 1 : End While
        If i < s.Length AndAlso s(i) = "."c Then
            i += 1
            While i < s.Length AndAlso s(i) >= "0"c AndAlso s(i) <= "9"c : digits += 1 : i += 1 : End While
        End If
        If digits = 0 Then Return False
        ' El exponente sólo cuenta si trae al menos un dígito; si no, `strtof` retrocede y se queda con la
        ' mantisa (`"1e"` es 1, no un error). Por eso el índice sólo avanza cuando el exponente está completo.
        If i < s.Length AndAlso (s(i) = "e"c OrElse s(i) = "E"c) Then
            Dim j = i + 1
            If j < s.Length AndAlso (s(j) = "-"c OrElse s(j) = "+"c) Then j += 1
            Dim expDigits = 0
            While j < s.Length AndAlso s(j) >= "0"c AndAlso s(j) <= "9"c : expDigits += 1 : j += 1 : End While
            If expDigits > 0 Then i = j
        End If
        Dim parsed As Double
        If Not Double.TryParse(s.Substring(start, i - start),
                               Globalization.NumberStyles.AllowLeadingSign Or Globalization.NumberStyles.AllowDecimalPoint Or Globalization.NumberStyles.AllowExponent,
                               Globalization.CultureInfo.InvariantCulture, parsed) Then Return False
        result = CDbl(CSng(parsed))
        ' ⛔ NO FINITO ⇒ FALSE, IGUAL QUE UN "inf" ESCRITO A MANO. Un `fSliderMultiplier=1e40` es finito para
        ' Double pero el estrechamiento a Single —que es el que hace el motor— lo vuelve +∞. Devolverlo
        ' propagaba el infinito hasta `BoundsOf`, y ahí `TinySliderTextBox` DESCARTA en silencio un Minimum o
        ' Maximum no finito (`IsUsableNumber` hace Return sin asignar, :754-756): la pista se quedaba con los
        ' defaults del constructor —0..100— o sea un rango que no es ni el del ini ni el de skee, en TODAS las
        ' filas de la pestaña y sin un solo aviso. Con False queda el default de skee (1,0), que es la misma
        ' salida que ya se declara arriba para "inf"/"nan".
        If Double.IsNaN(result) OrElse Double.IsInfinity(result) Then Return False
        Return True
    End Function

    ''' <summary>Del string mergeado a los dos números, en el orden EXACTO de skee (main.cpp:842-850): primero
    ''' se leen las dos keys, y RECIÉN DESPUÉS corren los tres clamps. El orden importa porque los clamps de
    ''' <c>fSliderInterval</c> no dependen de si <c>fSliderMultiplier</c> se leyó o no.
    ''' <para>⛔ <c>fSliderMultiplier</c> NO TIENE TOPE SUPERIOR — el único ajuste es <c>&lt;= 0 ⇒ 0.01</c>
    ''' (:845-846). Ponerle un techo propio sería inventar una ley que el motor no tiene.</para>
    ''' <para>Privada a propósito: su único llamador es <see cref="EnsureSkeeIniValues"/>. Hacerla Friend
    ''' "para que el gate la vea" sería el ensanche gratuito que prohíbe el comentario de
    ''' <see cref="ResolveOverlayCounts"/> — el gate entra por <see cref="FaceSliderKnobs"/>, que es la puerta
    ''' que usa el producto.</para></summary>
    Private Function ResolveFaceGenSliderKnobs(raw As String()) As (Multiplier As Double, Interval As Double)
        Dim mult As Double = DefaultSliderMultiplier
        Dim interval As Double = DefaultSliderInterval
        Dim v As Double
        If ScanFloatLike(raw(RawSliderMultiplierSlot), v) Then mult = v
        If ScanFloatLike(raw(RawSliderIntervalSlot), v) Then interval = v
        If mult <= 0.0R Then mult = 0.01R
        If interval <= 0.0R Then interval = 0.01R
        If interval > 1.0R Then interval = 1.0R
        Return (mult, interval)
    End Function

    ''' <summary>El bool de <c>bExtendedMorphs</c>, con la ley del bool de skee: <c>sscanf("%u")</c> y
    ''' <c>tmp &gt; 0</c> (main.cpp:313-326). Una key ausente o que no parsea deja el default <c>True</c>
    ''' (main.cpp:150), que es el <c>res = false</c> que no asigna.</summary>
    Private Function ResolveExtendedMorphsEnabled(raw As String()) As Boolean
        Dim n As UInteger
        If Not ScanUInt32Like(raw(RawExtendedMorphsSlot), n) Then Return True
        Return n > 0UI
    End Function

    ''' <summary>Los dos knobs que dimensionan un slider extendido de cara, EN UNA SOLA LLAMADA:
    ''' <c>fSliderMultiplier</c> (el ANCHO: la ventana que RaceMenu dibuja es <c>±1 × mult</c>,
    ''' FaceMorphInterface.cpp:1321-1322 + :1354) y <c>fSliderInterval</c> (el PASO, :1320). Salen del ini
    ''' INSTALADO, no de una constante: con el ini de fábrica valen 1,0 y 0,01, y en la máquina de al lado 3,0 —
    ''' las dos respuestas son correctas.
    ''' <para>⛔ UNA PUERTA Y NO DOS. Con dos accessors independientes cada uno re-evalúa el stamp por su cuenta,
    ''' así que un ini reemplazado entre las dos llamadas arma la pestaña con el multiplicador de un archivo y el
    ''' intervalo del otro. El cache era uno solo; la API tenía que serlo también.</para>
    ''' <para>⛔ EL INTERVALO ES SÓLO PARA EL PASO DEL CONTROL. Ningún camino de guardado, carga ni aplicación de
    ''' skee64 lee <c>g_sliderInterval</c> —su único uso en todo el plugin es el argumento <c>interval</c> del
    ''' ctor de <c>RaceMenuSlider</c>— así que la app NO debe cuantizar valores a múltiplos del intervalo:
    ''' movería números que el motor no mueve.</para></summary>
    Friend Function FaceSliderKnobs() As RaceMenuSliderCatalog.SliderKnobs
        EnsureSkeeIniValues()
        SyncLock _lock
            Return New RaceMenuSliderCatalog.SliderKnobs(_faceSliderMultiplier, _faceSliderInterval)
        End SyncLock
    End Function

    ''' <summary><c>[FaceGen] bExtendedMorphs</c>: si está en 0, RaceMenu <b>NO APLICA NI UNO</b> de sus morphs
    ''' de cara extendidos — <c>ApplyMorphs</c> corta antes del bucle del ValueSet con
    ''' <c>if (!g_extendedMorphs) return;</c> (FaceMorphInterface.cpp:1126 y :1204) y <c>ReadSliders</c> ni
    ''' siquiera registra las líneas de tipo Slider y Preset (:934-935, :954-955). Default <b>true</b>
    ''' (main.cpp:150, leído en :854).
    ''' <para>⛔ NO APAGA EL SCULPT. El bloque que suma los deltas por vértice del <c>.jslot</c> está ARRIBA de
    ''' ese <c>return</c> (:1108-1125), así que el sculpt se aplica igual. Apagar los dos con la misma key sería
    ''' inventar una ley: son dos canales distintos y el interruptor gobierna uno solo.</para>
    ''' <para>El bool de skee sale del MISMO <c>%u</c> que los enteros y vale <c>tmp &gt; 0</c>
    ''' (main.cpp:313-326), así que se parsea con <see cref="ScanUInt32Like"/> y no con un lector de
    ''' "true/false": un <c>bExtendedMorphs=true</c> textual NO parsea y para el motor queda en su default.</para></summary>
    Friend Function FaceExtendedMorphsEnabled() As Boolean
        EnsureSkeeIniValues()
        Return _faceExtendedMorphs
    End Function

    ''' <summary>The skee64 node name for a slot, e.g. <c>Body [Ovl0]</c> (OverlayInterface.h:33-46). This is
    ''' the identity the render, the preset and the engine all key on.</summary>
    Friend Function OverlayNodeName(zone As OverlayZone, index As Integer) As String
        Return OverlayNodeName(zone, index, False)
    End Function

    ''' <summary>El nombre de nodo del pool que corresponda: <c>Body [Ovl0]</c> o <c>Body [SOvl0]</c>
    ''' (OverlayInterface.h:23-46). ÚNICO constructor de nombres de nodo de overlay de la app — el nombre es la
    ''' identidad del override en skee, en el co-save y en el <c>.jslot</c>, así que armarlo a mano en otro lado es
    ''' cómo se inventa un pool que no existe.</summary>
    Friend Function OverlayNodeName(zone As OverlayZone, index As Integer, isSpell As Boolean) As String
        Return $"{NodePrefixByZone(CInt(zone))} [{If(isSpell, "SOvl", "Ovl")}{index}]"
    End Function

    ''' <summary>Zone of an existing overlay node name, or Nothing when the node is not one of ours (cualquier otro
    ''' nodo de NiOverride — transform, texture-set de armadura — se round-trip-ea verbatim y no se edita).
    ''' <para>CUBRE LOS DOS POOLS: <c>[Ovl{n}]</c> y <c>[SOvl{n}]</c>. Antes reclamaba sólo el primero, porque la
    ''' app no autoraba el magic; ahora sí, y el editor los muestra y los edita. Qué POOL es lo dice
    ''' <see cref="IsSpellNode"/> — esta función responde SÓLO la geometría (zona), que es lo que el ruteo de shapes
    ''' y el filtrado de listas necesitan.</para></summary>
    Friend Function ZoneOfNode(nodeName As String) As OverlayZone?
        If String.IsNullOrEmpty(nodeName) Then Return Nothing
        For z = 0 To NodePrefixByZone.Length - 1
            If nodeName.StartsWith(NodePrefixByZone(z) & " [Ovl", StringComparison.OrdinalIgnoreCase) OrElse
               nodeName.StartsWith(NodePrefixByZone(z) & " [SOvl", StringComparison.OrdinalIgnoreCase) Then Return CType(z, OverlayZone)
        Next
        Return Nothing
    End Function

    ''' <summary>El primer índice LIBRE de un pool concreto (zona × normal/magic) dentro de una lista de overlays.
    ''' <para>FILTRA POR POOL, no sólo por zona: los dos pools tienen numeración INDEPENDIENTE en el motor
    ''' (<c>Body [Ovl0]</c> y <c>Body [SOvl0]</c> son dos nodos distintos que conviven). Contar los índices usados de
    ''' la zona sin mirar el pool desperdiciaba slots del pool normal por cada magic autorado — y al revés.</para>
    ''' <para>Existe acá y no en cada editor porque lo necesitan los dos (cuerpo y cara) y el Add y la conversión
    ''' normal↔magic: cuatro call sites que tienen que elegir el índice con la MISMA regla.</para></summary>
    Friend Function NextFreeOverlayIndex(overlays As IEnumerable(Of RaceMenuJslot.JslotOverlayNode),
                                         zone As OverlayZone, isSpell As Boolean) As Integer
        Dim used As New HashSet(Of Integer)
        If overlays IsNot Nothing Then
            For Each o In overlays
                If o Is Nothing Then Continue For
                Dim z = ZoneOfNode(o.NodeName)
                If Not z.HasValue OrElse z.Value <> zone Then Continue For
                If IsSpellNode(o.NodeName) <> isSpell Then Continue For
                Dim n0 = IndexOfNode(o.NodeName)
                If n0 >= 0 Then used.Add(n0)
            Next
        End If
        Dim n = 0
        While used.Contains(n) : n += 1 : End While
        Return n
    End Function

    ''' <summary>¿Este nodo es del pool MAGIC? Delega en el predicado ÚNICO de la librería
    ''' (<see cref="SseOverlayCompositor.IsSpellOverlayNodeName"/>) — acá existe sólo para que los editores no
    ''' tengan que importar el compositor para una pregunta de catálogo.</summary>
    Friend Function IsSpellNode(nodeName As String) As Boolean
        Return SseOverlayCompositor.IsSpellOverlayNodeName(nodeName)
    End Function

    ''' <summary>Index parsed out of <c>… [Ovl{n}]</c> o <c>… [SOvl{n}]</c>, or -1.
    ''' <para>ERA UN SEGUNDO PARSER, Y TENÍA EL MISMO DEFECTO QUE EL PRIMERO: buscaba el literal <c>"[Ovl"</c>
    ''' (que no matchea <c>"[SOvl"</c>) y encima cortaba con <c>open + 4</c>, asumiendo el largo del tag. Para un
    ''' nodo magic devolvía −1, y de ahí salían dos overlays magic autorados en el MISMO nodo (el "primer slot
    ''' libre" siempre daba 0). Ahora delega en la ÚNICA implementación, la de la librería, que es la que también
    ''' usan el render y el bake — dos parsers del mismo string es exactamente cómo se llega a que la UI y el
    ''' render no coincidan.</para></summary>
    Friend Function IndexOfNode(nodeName As String) As Integer
        Dim n = SseOverlayCompositor.ParseOvlIndex(nodeName)
        Return If(n = Integer.MaxValue, -1, n)
    End Function

    ''' <summary>Every <c>.dds</c> under <see cref="OverlayTextureRoot"/>, loose and inside BSAs, as full
    ''' dictionary keys. Empty when nothing is installed there.</summary>
    Friend Function OverlayTextureKeys() As List(Of String)
        Try
            Return FilesDictionary_class.GetFilteredKeys(OverlayTextureRoot, {".dds"})
        Catch ex As Exception
            Logger.LogLazy(Function() $"[SSE-CATALOG] overlay texture scan failed: {ex.GetType().Name}: {ex.Message}")
            Return New List(Of String)()
        End Try
    End Function


    ''' <summary>Skin-override textures replace the actor's body/hand/feet diffuse, so they are ordinary
    ''' character textures and are picked from the whole <c>Textures\</c> tree rather than the overlay folder.</summary>
    Friend Function PickSkinTexture(owner As IWin32Window, currentJslotPath As String) As String
        Dim cfg = FilesDictionary_class.TexturesDictionary_Filter
        Dim keys As List(Of String)
        Try
            keys = FilesDictionary_class.GetFilteredKeys(cfg)
        Catch ex As Exception
            Logger.LogLazy(Function() $"[SSE-CATALOG] texture scan failed: {ex.GetType().Name}: {ex.Message}")
            Return Nothing
        End Try
        If keys Is Nothing OrElse keys.Count = 0 Then Return Nothing
        Return PickTexture(owner, currentJslotPath, keys, cfg.RootPrefix)
    End Function

    ''' <summary>Shared body of the texture pickers: show the in-archive tree rooted at
    ''' <paramref name="rootPrefix"/>, preselect the current entry, and return the chosen path in the form a
    ''' <c>.jslot</c> stores it (relative to <c>Textures\</c>, no prefix). Nothing when cancelled.</summary>
    Private Function PickTexture(owner As IWin32Window, currentJslotPath As String,
                                 keys As List(Of String), rootPrefix As String) As String
        ' The picker preselects by full dictionary key; the preset stores the path without the Textures\ root.
        Dim initialKey As String = ""
        If Not String.IsNullOrWhiteSpace(currentJslotPath) Then
            initialKey = FO4UnifiedMaterial_Class.CorrectTexturePath(currentJslotPath)
        End If

        Using dlg As New DictionaryFilePicker_Form(keys, rootPrefix,
                                                   FilesDictionary_class.TexturesDictionary_Filter.AllowedExtensions,
                                                   initialKey)
            If dlg.ShowDialog(owner) <> DialogResult.OK Then Return Nothing
            Dim key = dlg.DictionaryPicker_Control1.SelectedKey
            If String.IsNullOrWhiteSpace(key) Then Return Nothing
            Return RaceMenuJslot.ToGameTexturePath(key)
        End Using
    End Function

    ' --- RaceMenu PAINT lists (bug #0) ---------------------------------------------------------------------
    ' RaceMenu presents warpaints and body/hand/feet/face paints as NAMED lists it accumulates from every mod's
    ' Add*Paint Papyrus registrations — never a file browser. RaceMenuPaintCatalog reconstructs those same lists
    ' from the installed scripts; these helpers show them with the PaintListPicker (name shown, path stored).

    Friend Enum PaintPickKind
        Cancel = 0
        Clear = 1
        Pick = 2
    End Enum

    Friend Structure PaintPickResult
        Public Kind As PaintPickKind
        Public Entry As RaceMenuPaintCatalog.Entry
    End Structure

    ''' <summary>The paint category for an overlay zone. Warpaint is a separate (face tint-mask) list and is not
    ''' reachable from a zone.</summary>
    Friend Function PaintCategoryForZone(zone As OverlayZone) As RaceMenuPaintCatalog.PaintCategory
        Select Case zone
            Case OverlayZone.Body : Return RaceMenuPaintCatalog.PaintCategory.Body
            Case OverlayZone.Hands : Return RaceMenuPaintCatalog.PaintCategory.Hands
            Case OverlayZone.Feet : Return RaceMenuPaintCatalog.PaintCategory.Feet
            Case Else : Return RaceMenuPaintCatalog.PaintCategory.Face
        End Select
    End Function

    ''' <summary>The friendly RaceMenu paint name registered for <paramref name="path"/> in the paint list of
    ''' <paramref name="zone"/>, or Nothing when no installed mod registered that exact texture. A <c>.jslot</c>
    ''' overlay stores ONLY the texture path — RaceMenu never persists the display name — so the name is re-derived
    ''' here by matching the stored path against the catalog the Add*Paint scripts built. The match is
    ''' prefix/slash/case-insensitive because the registration path and the stored path may differ in the leading
    ''' <c>textures\</c> and separator style.</summary>
    Friend Function PaintNameForPath(zone As OverlayZone, path As String) As String
        If String.IsNullOrWhiteSpace(path) Then Return Nothing
        Dim catalog = RaceMenuPaintCatalog.Current
        If catalog Is Nothing Then Return Nothing
        Dim want = NormalizePaintPath(path)
        For Each e In catalog.Entries(PaintCategoryForZone(zone))
            If NormalizePaintPath(e.Path) = want Then Return e.DisplayName
        Next
        Return Nothing
    End Function

    Private Function NormalizePaintPath(p As String) As String
        Dim s = If(p, "").Replace("/"c, "\"c).Trim().ToLowerInvariant()
        If s.StartsWith("textures\") Then s = s.Substring("textures\".Length)
        Return s
    End Function

    Private Function PaintCategoryLabel(cat As RaceMenuPaintCatalog.PaintCategory) As String
        Select Case cat
            Case RaceMenuPaintCatalog.PaintCategory.Warpaint : Return "warpaint"
            Case RaceMenuPaintCatalog.PaintCategory.Body : Return "body paint"
            Case RaceMenuPaintCatalog.PaintCategory.Hands : Return "hand paint"
            Case RaceMenuPaintCatalog.PaintCategory.Feet : Return "feet paint"
            Case Else : Return "face paint"
        End Select
    End Function

    ''' <summary>Show the RaceMenu named list for <paramref name="cat"/> and return the user's choice. This is the
    ''' replacement for the loose+BSA file browser: RaceMenu offers only what a mod registered, by name.
    ''' <paramref name="allowNone"/> adds a "(None — clear)" row. Returns Cancel when the list is empty or the user
    ''' backs out.</summary>
    Friend Function PickPaint(owner As IWin32Window, cat As RaceMenuPaintCatalog.PaintCategory,
                              currentPath As String, allowNone As Boolean) As PaintPickResult
        Dim catalog = RaceMenuPaintCatalog.Current
        If catalog Is Nothing OrElse catalog.CountFor(cat) = 0 Then
            MessageBox.Show(owner,
                $"No {PaintCategoryLabel(cat)} entries are registered." & vbCrLf & vbCrLf &
                "RaceMenu builds this list at runtime from the Add*Paint calls in installed mods' Papyrus scripts " &
                "(loose or inside a BSA) — there is no file browser and no static folder. Install a mod that " &
                "registers " & PaintCategoryLabel(cat) & " and reload.",
                "No " & PaintCategoryLabel(cat) & " found", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return New PaintPickResult With {.Kind = PaintPickKind.Cancel}
        End If
        Dim title = "Choose " & PaintCategoryLabel(cat) & $" ({catalog.CountFor(cat)} available)"
        Using dlg As New PaintListPicker_Form(title, catalog.Entries(cat), currentPath, allowNone)
            If dlg.ShowDialog(owner) <> DialogResult.OK Then Return New PaintPickResult With {.Kind = PaintPickKind.Cancel}
            If dlg.ChosenEntry.HasValue Then
                Return New PaintPickResult With {.Kind = PaintPickKind.Pick, .Entry = dlg.ChosenEntry.Value}
            End If
            Return New PaintPickResult With {.Kind = PaintPickKind.Clear}
        End Using
    End Function

    ''' <summary>Is a paint/overlay texture present in the load order (loose + BSA)? Uses the SAME normalisation the
    ''' material loader and the renderer's skip check use (lowercase, backslashes, prepend <c>textures\</c>), so
    ''' "resolves" here matches what the renderer can actually load — a mod may register a paint whose <c>.dds</c> it
    ''' does not ship (e.g. CommunityOverlays registers <c>…\27 Head.dds</c> but only ships <c>27 Head M.dds</c>),
    ''' and the editor should show that as missing rather than silently render nothing.</summary>
    Friend Function TextureResolves(gameRelPath As String) As Boolean
        If String.IsNullOrWhiteSpace(gameRelPath) Then Return False
        Dim key = gameRelPath.Replace("/"c, "\"c).ToLowerInvariant()
        If Not key.StartsWith("textures\") Then key = "textures\" & key
        Return FilesDictionary_class.Dictionary.ContainsKey(key)
    End Function

    ''' <summary>Colour for a missing-texture row — the same red the tint tab uses for a missing mask.</summary>
    Friend ReadOnly MissingTextureColor As System.Drawing.Color = System.Drawing.Color.FromArgb(200, 40, 40)

    ''' <summary>El nodo del slider "Height". Tiene nombre propio porque es el ÚNICO nodo del catálogo sobre
    ''' el que el motor compone algo suyo: skee le suma el lift de los tacos altos (<c>HH_OFFSET</c> sintetiza
    ''' <c>[{"name":"NPC","pos":[0,0,offset]}]</c> bajo su key <c>internal</c>).
    ''' <para>Un solo consumidor hoy: el aviso del editor —el jugador que sube la altura y le pone botas ve al NPC
    ''' más alto en el juego que en el preview, y eso es CORRECTO—. El doc anterior afirmaba un SEGUNDO
    ''' consumidor que no existe ("la exclusión de <c>internal</c> de la neutralización"): esa exclusión compara
    ''' contra el literal <c>"internal"</c>, no contra este nombre, y vive en <c>RaceMenuJslot</c>, o sea en otra
    ''' assembly, donde un <c>Friend Const</c> de acá no llega. El literal NO estaba duplicado; lo que estaba mal
    ''' era la justificación escrita.</para></summary>
    Friend Const HeightNodeName As String = "NPC"

    ''' <summary>RaceMenu's BUILT-IN body-scale node sliders, as (friendly label, skeleton node). This is NOT
    ''' invented: it is the exact set RaceMenu's own <c>RaceMenuPlugin.psc</c> registers (recovered by decompiling
    ''' <c>RaceMenuPlugin.pex</c> inside <c>RaceMenu.bsa</c> — the <c>$labels</c> and <c>NINODE_*</c> node literals
    ''' it binds each slider to via <c>NiOverride.AddNodeTransformScale</c>). RaceMenu has NO skeleton scan — the UI
    ''' list is exactly what plugins register (PapyrusNiOverride.cpp:1381 enumerates only already-registered nodes).
    ''' Other mods (XPMSE) register MORE through the same mechanism; <see cref="RaceMenuNodeCatalog"/> picks those up
    ''' dynamically from the installed scripts, and the editor also unions any node a loaded preset carries.</summary>
    Friend ReadOnly RaceMenuBaseBodyNodes As (Label As String, Node As String)() = {
        ("Height", HeightNodeName),
        ("Head", "NPC Head [Head]"),
        ("Breast L", "NPC L Breast"), ("Breast R", "NPC R Breast"),
        ("Breast Curve L", "NPC L Breast01"), ("Breast Curve R", "NPC R Breast01"),
        ("Glute L", "NPC L Butt"), ("Glute R", "NPC R Butt"),
        ("Biceps L", "NPC L UpperarmTwist1 [LUt1]"), ("Biceps R", "NPC R UpperarmTwist1 [RUt1]"),
        ("Biceps 2 L", "NPC L UpperarmTwist2 [LUt2]"), ("Biceps 2 R", "NPC R UpperarmTwist2 [RUt2]")
    }

    ''' <summary>RaceMenu's built-in WEAPON-scale node sliders (same <c>RaceMenuPlugin.psc</c> registration). They
    ''' scale the equipped weapon/shield/quiver, which the NPC-appearance preview does not render, so the editor
    ''' surfaces them only under the "show all" toggle rather than in the default body view.</summary>
    Friend ReadOnly RaceMenuBaseWeaponNodes As (Label As String, Node As String)() = {
        ("Weapon", "WEAPON"), ("Sword", "WeaponSword"), ("Axe", "WeaponAxe"), ("Mace", "WeaponMace"),
        ("Bow", "WeaponBow"), ("Weapon Back", "WeaponBack"), ("Shield", "SHIELD"), ("Quiver", "QUIVER")
    }

    ''' <summary>Un nodo de arma/equipo (<c>WEAPON</c>/<c>SHIELD</c>/<c>QUIVER</c>/<c>Weapon*</c>).
    ''' <para>Vive acá, al lado del array que define el conjunto, porque tiene DOS consumidores con
    ''' consecuencias distintas y no puede haber dos definiciones: el editor los esconde detrás de "show all"
    ''' (el preview no renderiza el equipo) y el reporte de compatibilidad avisa que sobre ellos la autoría de
    ''' la app es TRANSITORIA — XPMSE reescribe la colocación del arma en cada cambio de arma, así que su capa
    ''' vuelve en cada cambio de arma, encima de lo nuestro.</para>
    ''' <para>El prefijo es más ancho que el array a propósito: XPMSE registra nodos <c>Weapon…</c> que no están
    ''' en la lista de RaceMenu, y el catálogo dinámico los levanta del <c>.pex</c>.</para></summary>
    Friend Function IsWeaponNode(node As String) As Boolean
        If String.IsNullOrEmpty(node) Then Return False
        If node.StartsWith("Weapon", StringComparison.OrdinalIgnoreCase) Then Return True
        Select Case node.ToUpperInvariant()
            Case "WEAPON", "SHIELD", "QUIVER" : Return True
        End Select
        Return False
    End Function

End Module
