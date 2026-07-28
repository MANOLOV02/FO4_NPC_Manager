Imports System.Text.RegularExpressions

''' <summary>Reescribe, dentro de un <c>.pex</c> ya compilado, el NOMBRE DEL SCRIPT y la GENERACIÓN del
''' payload. Es lo que permite que un único <c>.psc</c> compilado sirva como plantilla y que la app emita
''' un <c>.pex</c> distinto por plugin publicado, sin que el autor toque nada a mano.
'''
''' <para><b>Por qué es seguro tocar sólo strings.</b> Todo lo que no es la tabla de strings referencia por
''' ÍNDICE, nunca por texto: el nombre del objeto, el de cada property, el de cada variable y el del archivo
''' fuente son índices a esa tabla. Medido sobre los <c>.pex</c> reales (2026-07-28, tras agregar los body
''' morphs): SSE 261 strings de los que 73 traen el sufijo de generación, FO4 176 de los que 34 lo traen — y
''' en los dos casos son SÓLO nombres de property, nombres de variable (<c>::X_G000001_var</c>) y literales de
''' <c>Debug.Trace</c> que nombran una property. Ni un path ni un dato. Cambiar el CONTENIDO de esas entradas
''' cambia coherentemente todo lo que las referencia.</para>
''' <para>⚠️ Corolario para quien edite los <c>.psc</c>: un literal de string que contenga <c>_G000001</c> o el
''' nombre del script TAMBIÉN se reescribe. Es lo deseado para las trazas que nombran una property; no meter
''' ese texto en un literal que tenga que quedar fijo.</para>
'''
''' <para><b>⛔ NO SE DECODIFICAN LOS STRINGS.</b> El compilador de Papyrus no escribe UTF-8: los docstrings
''' con acentos salen en la codificación ANSI de la máquina. Decodificar y re-codificar los corrompería (se
''' comprobó con el dumper: los docstrings en español fallan al decodificar como UTF-8). Por eso todo el
''' reemplazo es a nivel de BYTES, y los dos tokens que se tocan son ASCII puro, así que la búsqueda es
''' exacta.</para>
'''
''' <para><b>Endianness.</b> Skyrim SE escribe el <c>.pex</c> en BIG-endian y Fallout 4 en LITTLE-endian (los
''' mismos 4 bytes de magic al revés). Se detecta del magic y se respeta en cada campo multi-byte, incluido
''' el prefijo de longitud de cada string.</para></summary>
Public Module PexPatcher

    Private Const Magic As UInteger = &HFA57C0DEUI

    ''' <summary>Ancho FIJO del sufijo de generación: <c>_G000001</c>. Fijo a propósito — así el reemplazo
    ''' del número es byte a byte del mismo largo y no puede desalinear nada.</summary>
    Public Const GenerationDigits As Integer = 6

    ''' <summary>Última generación antes de dar la vuelta. El wrap es seguro porque el motor DESCARTA la
    ''' variable que el script ya no declara — medido en Papyrus.0.log: <c>"Variable ::OvlNode_var ... loaded
    ''' from save not found within the actual object. This variable will be skipped."</c>. Para que morder,
    ''' un jugador tendría que arrastrar un save con la generación 0 y saltarse un millón de releases sin
    ''' cargar ninguna intermedia.</summary>
    Public Const MaxGeneration As Integer = 999999

    ''' <summary>Ancho FIJO de la SAL, en dígitos hex. Igual que el contador: fijo para que el reemplazo dentro
    ''' del <c>.pex</c> sea byte a byte del mismo largo.</summary>
    Public Const SaltDigits As Integer = 4

    ''' <summary>Sal que trae la PLANTILLA compilada (el <c>_G0000010000</c> de los <c>.psc</c>).</summary>
    Public Const BaselineSalt As String = "0000"

    ''' <summary>⛔⛔ POR QUÉ EXISTE LA SAL, ADEMÁS DEL CONTADOR.
    '''
    ''' <para>El contador da ORDEN (útil para leer un log o comparar un ESP contra un <c>.pex</c>), pero depende
    ''' de estado guardado: vive en el <c>.bssliders</c>. Si ese estado se pierde o RETROCEDE, el guardado
    ''' siguiente reemite una generación YA PUBLICADA — y entonces el savegame del jugador, que ya tiene
    ''' variables con esos nombres, las restaura RANCIAS y le gana al VMAD. El actor aplica el payload VIEJO
    ''' sin un solo error en ningún log. MEDIDO 2026-07-28 (restaurar un backup del sidecar bastó).</para>
    '''
    ''' <para><see cref="NpcOverrideSaver"/> ya pone un piso con la generación del <c>.pex</c> instalado. La sal
    ''' es la segunda línea: 4 hex sorteados en CADA Save ESP hacen que el nombre sea distinto aunque el número
    ''' se repita, así que la frescura deja de depender de que sobreviva ningún archivo.</para>
    '''
    ''' <para>Se usa <c>Guid.NewGuid</c> y no <c>Random</c> a propósito: dos guardados en el mismo tick no
    ''' pueden sacar la misma sal por comparti r semilla.</para></summary>
    Public Function NewSalt() As String
        Return Guid.NewGuid().ToString("N").Substring(0, SaltDigits).ToUpperInvariant()
    End Function

    ''' <summary>Sufijo textual de una generación: contador de ancho fijo + sal de ancho fijo.
    ''' (<c>16</c>, <c>"A3F2"</c>) ⇒ <c>_G000016A3F2</c>.</summary>
    Public Function GenerationSuffix(generation As Integer, salt As String) As String
        Dim s = If(salt, "")
        If s.Length <> SaltDigits Then s = s.PadRight(SaltDigits, "0"c).Substring(0, SaltDigits)
        Return "_G" & generation.ToString("D" & GenerationDigits, Globalization.CultureInfo.InvariantCulture) & s
    End Function

    ''' <summary>Siguiente generación, con wrap. Ver <see cref="MaxGeneration"/>.</summary>
    Public Function NextGeneration(current As Integer) As Integer
        If current >= MaxGeneration OrElse current < 0 Then Return 0
        Return current + 1
    End Function

    ' ------------------------------------------------------------------------------------------------
    ' Lectura
    ' ------------------------------------------------------------------------------------------------

    ''' <summary>Generación que declara este <c>.pex</c>, o -1 si no se pudo leer. Se usa para saber en qué
    ''' número está el <c>.pex</c> ya instalado y emitir el siguiente, sin que el autor lleve la cuenta.</summary>
    Public Function ReadGeneration(pex As Byte()) As Integer
        Dim strings As List(Of Byte()) = Nothing
        If Not TryParse(pex, Nothing, strings, Nothing) Then Return -1
        Dim best = -1
        For Each s In strings
            ' ASCII puro: comparar byte a byte evita cualquier decodificación.
            Dim txt = AsciiOf(s)
            ' ⭐ Regex A PROPOSITO TOLERANTE: matchea "_G000016" tanto si le sigue una sal ("_G000016A3F2",
            ' formato nuevo) como si no ("_G000016", .pex instalado por una version anterior de la app). Eso es
            ' lo que permite que el piso anti-retroceso funcione tambien al ACTUALIZAR desde una instalacion vieja.
            For Each m As Match In Regex.Matches(txt, "_G(\d{" & GenerationDigits & "})")
                Dim v = Integer.Parse(m.Groups(1).Value, Globalization.CultureInfo.InvariantCulture)
                If v > best Then best = v
            Next
        Next
        Return best
    End Function

    ''' <summary>Nombre del objeto que declara este <c>.pex</c> (el <c>Scriptname</c>), o Nothing.
    ''' Se toma del nombre del archivo fuente del header, que es el único string del que se puede
    ''' derivar sin parsear la sección de objetos.</summary>
    Public Function ReadSourceScriptName(pex As Byte()) As String
        Dim header As List(Of Byte()) = Nothing
        If Not TryParse(pex, header, Nothing, Nothing) Then Return Nothing
        Dim src = AsciiOf(header(0))                       ' p.ej. "NPCM_Manolov_ApplySSE.psc" o una ruta
        If String.IsNullOrEmpty(src) Then Return Nothing
        src = IO.Path.GetFileNameWithoutExtension(src)
        Return If(String.IsNullOrEmpty(src), Nothing, src)
    End Function


    ' ------------------------------------------------------------------------------------------------
    ' Escritura
    ' ------------------------------------------------------------------------------------------------

    ''' <summary>Devuelve una copia del <c>.pex</c> con el script renombrado y la generación reescrita.
    ''' <paramref name="oldScriptName"/> / <paramref name="oldGeneration"/> describen lo que trae la plantilla
    ''' embebida; si alguno ya coincide con el destino, ese reemplazo simplemente no cambia nada.</summary>
    ''' <exception cref="InvalidDataException">Si el archivo no parsea como .pex, o si el nombre viejo no
    ''' aparece en ningún string — eso significaría que la plantilla embebida no es la que creemos y el
    ''' resultado sería un .pex que el motor no puede bindear. Preferimos fallar el guardado a escribir uno roto.</exception>
    Public Function PatchScript(pex As Byte(),
                          oldScriptName As String, newScriptName As String,
                          oldGeneration As Integer, oldSalt As String,
                          newGeneration As Integer, newSalt As String) As Byte()
        Dim header As List(Of Byte()) = Nothing, strings As List(Of Byte()) = Nothing
        Dim tailOffset As Integer = 0
        If Not TryParse(pex, header, strings, tailOffset) Then
            Throw New IO.InvalidDataException("El .pex embebido no tiene un encabezado válido.")
        End If

        Dim subs As New List(Of (find As Byte(), repl As Byte()))
        If Not String.Equals(oldScriptName, newScriptName, StringComparison.Ordinal) Then
            subs.Add((AsciiBytes(oldScriptName), AsciiBytes(newScriptName)))
        End If
        Dim oldSuffix = GenerationSuffix(oldGeneration, oldSalt)
        Dim newSuffix = GenerationSuffix(newGeneration, newSalt)
        If Not String.Equals(oldSuffix, newSuffix, StringComparison.Ordinal) Then
            ' Mismo largo por construccion (contador y sal son de ancho fijo), asi que el reemplazo no desalinea.
            subs.Add((AsciiBytes(oldSuffix), AsciiBytes(newSuffix)))
        End If

        Dim hits = 0
        Dim apply = Function(b As Byte()) As Byte()
                        Dim cur = b
                        For Each s In subs
                            Dim n = 0
                            cur = ReplaceBytes(cur, s.find, s.repl, n)
                            hits += n
                        Next
                        Return cur
                    End Function

        Dim newHeader = header.Select(apply).ToList()
        Dim newStrings = strings.Select(apply).ToList()

        If subs.Count > 0 AndAlso hits = 0 Then
            Throw New IO.InvalidDataException(
                $"El .pex embebido no contiene '{oldScriptName}' ni '{GenerationSuffix(oldGeneration, oldSalt)}'. La plantilla " &
                "compilada no coincide con lo que el emisor espera: el .pex resultante no bindearía. " &
                "Recompilar los .psc y rebuildear la app (ver Papyrus\README.md).")
        End If

        Dim big = IsBigEndian(pex)
        Using ms As New IO.MemoryStream()
            ms.Write(pex, 0, 16)                             ' magic + major + minor + gameID + compileTime
            For Each h In newHeader : WriteStr(ms, h, big) : Next
            WriteU16(ms, CUShort(newStrings.Count), big)
            For Each s In newStrings : WriteStr(ms, s, big) : Next
            ms.Write(pex, tailOffset, pex.Length - tailOffset)   ' todo lo demás, intacto
            Return ms.ToArray()
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------------
    ' Parseo del prefijo (header + tabla de strings). El resto del archivo no se toca.
    ' ------------------------------------------------------------------------------------------------

    Private Function TryParse(pex As Byte(),
                              ByRef header As List(Of Byte()),
                              ByRef strings As List(Of Byte()),
                              ByRef tailOffset As Integer) As Boolean
        header = New List(Of Byte()) : strings = New List(Of Byte()) : tailOffset = 0
        If pex Is Nothing OrElse pex.Length < 24 Then Return False
        Dim big = IsBigEndian(pex)
        If Not big AndAlso ReadU32(pex, 0, False) <> Magic Then Return False

        Dim o = 16                                            ' magic(4) major(1) minor(1) gameID(2) time(8)
        For i = 0 To 2                                        ' sourceFileName, username, machinename
            Dim b = ReadStr(pex, o, big)
            If b Is Nothing Then Return False
            header.Add(b)
        Next
        ' ⛔ ReadU16 YA avanza el cursor (es ByRef). Sumarle 2 aca leia la tabla corrida 2 bytes y hacia
        ' fallar el parseo entero con 'encabezado no valido'.
        Dim count = ReadU16(pex, o, big)
        For i = 1 To count
            Dim b = ReadStr(pex, o, big)
            If b Is Nothing Then Return False
            strings.Add(b)
        Next
        tailOffset = o
        Return True
    End Function

    Private Function IsBigEndian(pex As Byte()) As Boolean
        Return ReadU32(pex, 0, True) = Magic
    End Function

    Private Function ReadU32(b As Byte(), o As Integer, big As Boolean) As UInteger
        If big Then Return (CUInt(b(o)) << 24) Or (CUInt(b(o + 1)) << 16) Or (CUInt(b(o + 2)) << 8) Or CUInt(b(o + 3))
        Return (CUInt(b(o + 3)) << 24) Or (CUInt(b(o + 2)) << 16) Or (CUInt(b(o + 1)) << 8) Or CUInt(b(o))
    End Function

    Private Function ReadU16(b As Byte(), ByRef o As Integer, big As Boolean) As Integer
        Dim v = If(big, (CInt(b(o)) << 8) Or CInt(b(o + 1)), (CInt(b(o + 1)) << 8) Or CInt(b(o)))
        o += 2
        Return v
    End Function

    Private Function ReadStr(b As Byte(), ByRef o As Integer, big As Boolean) As Byte()
        If o + 2 > b.Length Then Return Nothing
        Dim n = ReadU16(b, o, big)
        If o + n > b.Length Then Return Nothing
        Dim s(n - 1) As Byte
        If n > 0 Then Array.Copy(b, o, s, 0, n)
        o += n
        Return s
    End Function

    Private Sub WriteU16(ms As IO.MemoryStream, v As UShort, big As Boolean)
        If big Then
            ms.WriteByte(CByte((v >> 8) And &HFF)) : ms.WriteByte(CByte(v And &HFF))
        Else
            ms.WriteByte(CByte(v And &HFF)) : ms.WriteByte(CByte((v >> 8) And &HFF))
        End If
    End Sub

    Private Sub WriteStr(ms As IO.MemoryStream, s As Byte(), big As Boolean)
        WriteU16(ms, CUShort(s.Length), big)
        If s.Length > 0 Then ms.Write(s, 0, s.Length)
    End Sub

    Private Function ReplaceBytes(src As Byte(), find As Byte(), repl As Byte(), ByRef hits As Integer) As Byte()
        hits = 0
        If find.Length = 0 OrElse src.Length < find.Length Then Return src
        Dim outBuf As New List(Of Byte)(src.Length)
        Dim i = 0
        While i <= src.Length - find.Length
            Dim match = True
            For k = 0 To find.Length - 1
                If src(i + k) <> find(k) Then match = False : Exit For
            Next
            If match Then
                outBuf.AddRange(repl)
                i += find.Length
                hits += 1
            Else
                outBuf.Add(src(i))
                i += 1
            End If
        End While
        While i < src.Length
            outBuf.Add(src(i)) : i += 1
        End While
        Return If(hits = 0, src, outBuf.ToArray())
    End Function

    Private Function AsciiBytes(s As String) As Byte()
        Return Text.Encoding.ASCII.GetBytes(s)
    End Function

    ''' <summary>Vista ASCII de un string del .pex, SÓLO para buscar tokens ASCII. Los bytes &gt;= 0x80
    ''' salen como '?' y eso está bien: nunca se re-escribe desde acá.</summary>
    Private Function AsciiOf(b As Byte()) As String
        Return Text.Encoding.ASCII.GetString(b)
    End Function

End Module
