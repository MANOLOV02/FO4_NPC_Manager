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

    ''' <summary>skee64 hardcoded fallbacks when the ini is missing or the key is absent (main.cpp:120-127).</summary>
    Private ReadOnly DefaultOverlayCounts As Integer() = {3, 3, 3, 3}

    ''' <summary>⛔ GEMELO DE <c>OVL_SWEEP_MAX</c> EN <c>NPCM_Manolov_ApplySSE.psc</c>. Hasta dónde el
    ''' apply-script saca de la partida del jugador un overlay cuyo nodo skee nunca instancia.
    ''' <para>Es el TOPE DEL MOTOR: skee clampea todo contador de overlay a <c>0x7F</c> (main.cpp:810-828) y los
    ''' crea con <c>for (i = 0; i &lt; count; i++)</c> (OverlayInterface.cpp:659-689), así que <c>[Ovl126]</c> es
    ''' el nodo más alto que puede existir en cualquier instalación, y 127 es el tope EXCLUSIVO que lo cubre sin
    ''' barrer un índice de más. Así NADA de lo que la app pueda autorar queda irrecuperable — no hace falta
    ''' preguntarse cuánto vale el <c>iNumOverlays</c> del jugador, que es suyo y no lo podemos saber.</para>
    ''' <para>Si cambia, hay que cambiarlo en el <c>.psc</c> Y recompilar el <c>.pex</c>: son artefactos
    ''' distintos y nadie los verifica en build. Lo chequea <c>Papyrus\tools\check_sweep_ceiling.py</c>.</para></summary>
    Friend Const OverlaySweepCeiling As Integer = 127

    Private ReadOnly IniSectionByZone As String() = {"Overlays/Body", "Overlays/Hands", "Overlays/Feet", "Overlays/Face"}
    Private ReadOnly NodePrefixByZone As String() = {"Body", "Hands", "Feet", "Face"}

    ''' <summary>Root under which RaceMenu keeps overlay textures. Taken from the shipped RaceMenu UI asset
    ''' (the string <c>textures\actors\character\overlays</c> is embedded in RaceMenu.bsa) and corroborated by
    ''' <c>skee64.ini</c>'s <c>sDefaultTexture=textures\actors\character\overlays\default.dds</c>. Real mods
    ''' nest below it (e.g. <c>…\Overlays\Skin Features\Freckles\Body\x.dds</c>), so the scan is recursive.</summary>
    Friend Const OverlayTextureRoot As String = "Textures\Actors\Character\Overlays\"

    Private ReadOnly _lock As New Object()
    Private _overlayCounts As Integer()
    Private _overlayStamp As String
    Private _overlaySource As String
    Private _overlayLimitWarned As Integer
    Private _faceDisabledByFlag As Boolean

    ''' <summary>True cuando el cero de la cara NO viene de <c>iNumOverlays</c> sino de
    ''' <c>[Features] bEnableFaceOverlays=0</c>. Es la diferencia entre mandar al usuario a la key correcta o a
    ''' una que ya tiene puesta en 6.</summary>
    Friend Function FaceOverlaysDisabledByIni() As Boolean
        EnsureOverlayCounts()
        Return _faceDisabledByFlag
    End Function

    Private ReadOnly OverlayIniNames As String() = {"skee64.ini", "skee64_custom.ini"}

    ''' <summary>Number of overlay slots the engine will instantiate for <paramref name="zone"/>, i.e. the
    ''' valid range of <c>[Ovl{n}]</c> indices skee64 CREATES is <c>0 .. count-1</c>. Read from
    ''' <c>Data\SKSE\Plugins\skee64.ini</c> (then <c>skee64_custom.ini</c>, which overrides it — main.cpp:239,249).
    ''' <para>This is an ADVISORY bound, not a hard one: an overlay past it is stored by NiOverride keyed by node
    ''' name and simply never matches a node (OverrideInterface.cpp:750-764 looks the node up and returns when it
    ''' is absent), so it is inert in-game and becomes live the day the count is raised. The editors therefore
    ''' warn once instead of refusing.</para></summary>
    Friend Function OverlayCount(zone As OverlayZone) As Integer
        EnsureOverlayCounts()
        Return _overlayCounts(CInt(zone))
    End Function

    ''' <summary>Which file the counts actually came from, for the warning text. ⛔ It is the whole point of the
    ''' message: "I raised iNumOverlays and nothing changed" is almost always a DIFFERENT copy of the ini (a mod
    ''' manager's virtual Data, another install), and naming the path we read settles it without guessing.</summary>
    Friend Function OverlayCountSource() As String
        EnsureOverlayCounts()
        Return _overlaySource
    End Function

    ''' <summary>⛔ The counts are re-read whenever an ini's timestamp/size changes. They used to be read ONCE per
    ''' process, so editing skee64.ini while the app was open changed nothing and the editor still told the user to
    ''' "reopen the editor" — which re-entered the same cached value. A stat of two files per Add click is free.</summary>
    Private Sub EnsureOverlayCounts()
        SyncLock _lock
            ' ⛔ EL STAMP SE SACA DENTRO DEL LOCK. Afuera, entre el stat y la lectura hay una ventana: si el ini
            ' se reemplaza ahí en el medio y después vuelve con su mtime y su largo originales (swap de perfil de
            ' un mod manager, robocopy /COPY:DT, extraer un 7z — todos preservan mtime), el cache se quedaba con
            ' los números del intruso bajo el stamp del archivo bueno, y ya no relee nunca.
            Dim stamp = OverlayIniStamp()
            If _overlayCounts IsNot Nothing AndAlso String.Equals(_overlayStamp, stamp, StringComparison.Ordinal) Then Return
            ' ⛔⛔ SE MERGEAN LOS STRINGS CRUDOS, NO LOS NÚMEROS. skee no parsea por archivo: junta el VALOR
            ' TEXTUAL de la key —base, y encima el custom si trae algo (main.cpp:258-282, "Only take custom if
            ' we have it")— y recién ahí corre UN sscanf sobre el resultado. La diferencia se ve con
            ' base `iNumOverlays=6` + custom `iNumOverlays=hola`: skee se queda con "hola", el sscanf falla, y
            ' el valor queda en el DEFAULT HARDCODEADO (3) — el 6 lo destruyó la línea basura del custom. Si se
            ' parsea por archivo, en cambio, el 6 sobrevive. Diríamos 6 donde el motor da 3, que es justo el
            ' error de "te ofrezco un slot que el motor no crea".
            Dim raw = NewRawOverlayValues()
            Dim read As New List(Of String), failed As New List(Of String)
            For Each iniPath In OverlayIniPaths()
                If Not File.Exists(iniPath) Then Continue For
                Try
                    ' El todo-o-nada por archivo vive DENTRO de MergeOverlayIni, pegado a la mutación.
                    MergeOverlayIni(File.ReadLines(iniPath), raw)
                    read.Add(iniPath)
                Catch ex As Exception
                    ' El archivo EXISTE y no se pudo leer: eso va al mensaje, no se calla como "no está".
                    failed.Add($"{iniPath} (present but unreadable: {ex.GetType().Name})")
                    Logger.LogLazy(Function() $"[SSE-CATALOG] could not read {iniPath}: {ex.GetType().Name}: {ex.Message}")
                End Try
            Next
            Dim faceOff As Boolean
            Dim counts = ResolveOverlayCounts(raw, faceOff)
            _faceDisabledByFlag = faceOff
            Dim folder = OverlayIniFolder()
            If read.Count > 0 OrElse failed.Count > 0 Then
                _overlaySource = String.Join(" + ", read.Concat(failed))
                If failed.Count > 0 Then _overlaySource &= " — the unreadable one contributed NOTHING"
            ElseIf folder = "" Then
                _overlaySource = "no game folder configured — RaceMenu's built-in defaults"
            Else
                _overlaySource = $"no skee64.ini in {folder} — RaceMenu's built-in defaults"
            End If
            _overlayCounts = counts
            ' ⛔⛔ UNA LECTURA FALLIDA NO SE CACHEA. El stamp sale de FileInfo (Exists/mtime/Length), que son
            ' consultas de METADATO: funcionan igual sobre un archivo abierto con FileShare.None. O sea que sólo
            ' falla el ReadLines — y guardar el stamp igual dejaba los defaults pegados PARA TODA LA SESIÓN bajo
            ' un stamp válido que nunca vuelve a cambiar. Es el mismo bug que vino a arreglar todo esto ("reabrí
            ' el editor y sigue igual"), entrando por el camino de error. Con _overlayStamp = Nothing el próximo
            ' click reintenta, que es lo que el comentario del stamp siempre dijo y no hacía.
            _overlayStamp = If(failed.Count = 0, stamp, Nothing)
        End SyncLock
    End Sub

    Private Function OverlayIniFolder() As String
        Dim data = Config_App.Current?.DataPath
        If String.IsNullOrEmpty(data) Then Return ""
        Return Path.Combine(data, "SKSE", "Plugins")
    End Function

    Private Iterator Function OverlayIniPaths() As IEnumerable(Of String)
        Dim folder = OverlayIniFolder()
        If folder = "" Then Return
        For Each name In OverlayIniNames
            Yield Path.Combine(folder, name)
        Next
    End Function

    ''' <summary>Identity of the ini pair on disk: path + write time + length. Any edit moves it.</summary>
    Private Function OverlayIniStamp() As String
        Dim sb As New Text.StringBuilder()
        For Each iniPath In OverlayIniPaths()
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
    ''' <para>⛔ UNA SOLA CLASE, Y ESO ES CONSECUENCIA DEL BARRIDO COMPLETO. Mientras el script barría hasta un
    ''' techo bajo existía un segundo aviso —"una partida que lo tome ya no lo suelta"— que NO podía compartir
    ''' disparo con el benigno. Subiendo <c>OVL_SWEEP_MAX</c> al tope del motor esa categoría dejó de existir:
    ''' todo lo autorable se puede deshacer, así que vuelve a haber un solo mensaje.</para></summary>
    Friend Function ClaimOverlayLimitWarning() As Boolean
        Return Threading.Interlocked.Exchange(_overlayLimitWarned, 1) = 0
    End Function

    ''' <summary>The one-per-session notice: qué va a hacer el motor con este overlay, y de qué archivo salió el
    ''' número — que es la mitad del diagnóstico cuando alguien dice "subí iNumOverlays y no cambió nada".</summary>
    Friend Function OverlayLimitNotice(zone As OverlayZone, index As Integer, limit As Integer) As String
        ' ⛔ LA KEY A LA QUE SE MANDA AL USUARIO DEPENDE DE POR QUÉ EL CONTADOR ES EL QUE ES: con
        ' bEnableFaceOverlays=0, subir iNumOverlays no hace absolutamente nada — y decirle que lo suba es
        ' recrear el bug que originó todo esto (sube la key, no pasa nada, la app repite el mismo cartel).
        Dim byFlag = zone = OverlayZone.Face AndAlso limit = 0 AndAlso FaceOverlaysDisabledByIni()
        Dim created = If(limit > 0, $"[Ovl0]…[Ovl{limit - 1}]", "none")
        Dim because = If(byFlag, "because [Features] bEnableFaceOverlays=0 turns face overlays off entirely, whatever iNumOverlays says",
                                 "per iNumOverlays in skee64.ini")
        Dim toRaise = If(byFlag, "bEnableFaceOverlays is set back to 1", "iNumOverlays is raised")
        Return $"{NodePrefixByZone(CInt(zone))} [Ovl{index}] is past the {limit} {zone.ToString().ToLowerInvariant()} overlay slot(s) " &
               $"RaceMenu creates ({created}), {because}." & vbCrLf & vbCrLf &
               "It is added anyway: it is saved, rendered in the preview and written to the NPC. In-game skee64 creates no such " &
               $"node, so it paints nothing — no error, no broken script — and it starts painting if {toRaise}." & vbCrLf & vbCrLf &
               "Removing it later is safe on any install: the apply-script clears every slot skee can create, node or no node." & vbCrLf & vbCrLf &
               $"Counts read from: {OverlayCountSource()}" & vbCrLf &
               "(Shown once per session, for every zone.)"
    End Function

    ''' <summary>Los cinco valores CRUDOS (sin parsear) que deciden los contadores: índices 0-3 = el
    ''' <c>iNumOverlays</c> de cada zona, índice 4 = <c>[Features] bEnableFaceOverlays</c>. <c>""</c> = ausente,
    ''' que es lo que devuelve <c>GetPrivateProfileString</c> cuando la key no está.</summary>
    Private Const RawFaceFlagSlot As Integer = 4

    Private Function NewRawOverlayValues() As String()
        Return {"", "", "", "", ""}
    End Function

    ''' <summary>Mergea UN archivo sobre <paramref name="raw"/> con la regla de skee: el valor de este archivo
    ''' pisa al anterior SÓLO si trae algo (<c>if (resultLen &gt; 0)</c>, main.cpp:276). Como el orden de llamada
    ''' es base y después custom, eso da "custom gana si no está vacío".
    ''' <para>⛔ TODO O NADA. La fuente es perezosa: si tira a mitad (VFS de un mod manager, Data en red, ACL) las
    ''' keys ya vistas quedarían aplicadas y el resto no — ni un ini ni el otro. Se junta en un array aparte y se
    ''' mergea recién cuando la enumeración terminó bien.</para></summary>
    ''' <param name="lines">La fuente de líneas, NO un path — <c>File.ReadLines</c> va tirando del
    ''' <c>StreamReader</c> a medida que se enumera, así que la excepción del medio llega ACÁ. Recibir el
    ''' enumerable en vez de abrir el archivo adentro es lo que deja al gate reproducir ese fallo exacto sin
    ''' ningún hook de test en el binario.</param>
    Friend Sub MergeOverlayIni(lines As IEnumerable(Of String), raw As String())
        Dim fileValues = CollectOverlayIniValues(lines)
        For i = 0 To raw.Length - 1
            If fileValues(i) <> "" Then raw(i) = fileValues(i)
        Next
    End Sub

    ''' <summary>Minimal INI reader for the keys that decide how many overlay nodes skee builds:
    ''' <c>[Overlays/*] iNumOverlays</c> y <c>[Features] bEnableFaceOverlays</c>. Deliberately not a general INI
    ''' parser — sólo hacen falta esos cinco valores, y SIN parsear: el número sale después, de una sola pasada
    ''' sobre el string ya mergeado, que es como lo hace skee.
    ''' <para>⚠️ Sin modelar: <c>GetPrivateProfileString</c> saca las comillas de <c>"6"</c>. Acá un valor
    ''' entrecomillado no parsea y la zona queda en su default. Nadie entrecomilla un entero en skee64.ini.</para>
    ''' </summary>
    Private Function CollectOverlayIniValues(lines As IEnumerable(Of String)) As String()
        Dim values = NewRawOverlayValues()
        Dim seen(values.Length - 1) As Boolean
        Dim section As String = ""
        ' ⛔ GetPrivateProfileString entra a la PRIMERA sección con ese nombre y no sale de ahí: si el archivo
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
                ' ⛔ EL HEADER TERMINA EN EL PRIMER "]", NO AL FINAL DE LA LÍNEA. Win32 acepta
                ' `[Overlays/Body] ; six slots` como sección y tira el resto. Exigiendo EndsWith("]"), esa línea
                ' NO abría sección — y entonces el `iNumOverlays=6` de abajo se le atribuía a la sección que
                ' seguía abierta, o sea a OTRA ZONA. No era una lectura perdida: era una inventada (la app
                ' ofrecía Hands [Ovl0..5] donde skee crea 3). Comentar al lado del header es de las formas más
                ' comunes de editar estos archivos a mano.
                ' ⛔ Y una línea que EMPIEZA con "[" es header aunque NUNCA cierre: Win32 toma el nombre hasta
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
    ''' Una key ausente o que no parsea deja el default hardcodeado (el <c>res = false</c> que no asigna).</summary>
    ''' <summary>⛔ NO agregar una sobrecarga sin el <c>ByRef</c> "para que el gate quede más lindo": la que
    ''' había no tenía UN SOLO llamador de producto, o sea superficie que existe únicamente para el test y que
    ''' igual viaja en el binario que se distribuye. El gate declara su propio local. Ver
    ''' 00-reglas-self-tests-no-van-en-el-binario.</summary>
    Friend Function ResolveOverlayCounts(raw As String(), ByRef faceDisabledByFlag As Boolean) As Integer()
        Dim counts = DirectCast(DefaultOverlayCounts.Clone(), Integer())
        Dim n As UInteger
        faceDisabledByFlag = False
        For z = 0 To IniSectionByZone.Length - 1
            If ScanUInt32Like(raw(z), n) Then counts(z) = CInt(Math.Min(n, CUInt(&H7F)))
        Next
        ' g_enableFaceOverlays: default true (main.cpp:139); el bool de skee también sale por "%u" y vale
        ' `tmp > 0` (main.cpp:313-326). ⛔ El cero va DESPUÉS del clamp y sólo sobre la cara: sin esto la app
        ' ofrecía slots de cara que el motor no instancia — decía que sí y no pintaba nada.
        ' (No se modela g_numSpellFaceOverlays: la app nunca autora nodos [SOvl], así que no hay número que dar.)
        If ScanUInt32Like(raw(RawFaceFlagSlot), n) AndAlso n = 0UI Then
            counts(CInt(OverlayZone.Face)) = 0
            ' ⛔ EL MOTIVO VIAJA CON EL NÚMERO. Sin esto el mensaje decía "0 slots, per iNumOverlays" y mandaba a
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
    ''' <para>⛔ EN OVERFLOW <c>strtoul</c> SATURA en <c>ULONG_MAX</c> (errno ERANGE), no envuelve. Envolver en
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
        ' ⛔ `0UL - acc` DESBORDA: VB chequea overflow por default y `0-4` en ULong tira OverflowException —
        ' que acá subía como "ini presente pero ilegible" y se comía el archivo entero. El complemento a 2^32
        ' se calcula restando DESDE 2^32, que nunca baja de 1 porque acc ya está enmascarado a 32 bits.
        If negate Then acc = (&H100000000UL - acc) And &HFFFFFFFFUL
        result = CUInt(acc)
        Return True
    End Function

    ''' <summary>The skee64 node name for a slot, e.g. <c>Body [Ovl0]</c> (OverlayInterface.h:33-46). This is
    ''' the identity the render, the preset and the engine all key on.</summary>
    Friend Function OverlayNodeName(zone As OverlayZone, index As Integer) As String
        Return $"{NodePrefixByZone(CInt(zone))} [Ovl{index}]"
    End Function

    ''' <summary>Zone of an existing node name, or Nothing when the node is not an overlay node we author
    ''' (spell overlays <c>[SOvl{n}]</c> and any other NiOverride node are round-tripped, never edited).</summary>
    Friend Function ZoneOfNode(nodeName As String) As OverlayZone?
        If String.IsNullOrEmpty(nodeName) Then Return Nothing
        For z = 0 To NodePrefixByZone.Length - 1
            If nodeName.StartsWith(NodePrefixByZone(z) & " [Ovl", StringComparison.OrdinalIgnoreCase) Then Return CType(z, OverlayZone)
        Next
        Return Nothing
    End Function

    ''' <summary>Index parsed out of <c>… [Ovl{n}]</c>, or -1.</summary>
    Friend Function IndexOfNode(nodeName As String) As Integer
        If String.IsNullOrEmpty(nodeName) Then Return -1
        Dim open = nodeName.IndexOf("[Ovl", StringComparison.OrdinalIgnoreCase)
        If open < 0 Then Return -1
        Dim close = nodeName.IndexOf("]"c, open)
        If close < 0 Then Return -1
        Dim digits = nodeName.Substring(open + 4, close - open - 4)
        Dim n As Integer
        Return If(Integer.TryParse(digits, n), n, -1)
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

    ''' <summary>RaceMenu's BUILT-IN body-scale node sliders, as (friendly label, skeleton node). This is NOT
    ''' invented: it is the exact set RaceMenu's own <c>RaceMenuPlugin.psc</c> registers (recovered by decompiling
    ''' <c>RaceMenuPlugin.pex</c> inside <c>RaceMenu.bsa</c> — the <c>$labels</c> and <c>NINODE_*</c> node literals
    ''' it binds each slider to via <c>NiOverride.AddNodeTransformScale</c>). RaceMenu has NO skeleton scan — the UI
    ''' list is exactly what plugins register (PapyrusNiOverride.cpp:1381 enumerates only already-registered nodes).
    ''' Other mods (XPMSE) register MORE through the same mechanism; <see cref="RaceMenuNodeCatalog"/> picks those up
    ''' dynamically from the installed scripts, and the editor also unions any node a loaded preset carries.</summary>
    Friend ReadOnly RaceMenuBaseBodyNodes As (Label As String, Node As String)() = {
        ("Height", "NPC"),
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

End Module
