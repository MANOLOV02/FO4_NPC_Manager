Imports System.IO
Imports System.Linq

''' <summary>LOS DOS .ini DE BODYGEN SE ESCRIBEN COMO UNO SOLO.
'''
''' <para>⛔ <b>Son un PAR REFERENCIAL, no dos archivos que se guardan juntos.</b> <c>templates.ini</c>
''' DEFINE los nombres de plantilla y <c>morphs.ini</c> los REFERENCIA: cada renglón de morphs nombra una
''' plantilla que templates tiene que declarar. Cada archivo tenía su red por separado
''' (<c>EscrituraEnElLugar.GuardarConCopia</c>, con su copia de seguridad) y el PAR no tenía ninguna: se
''' escribía templates, y si morphs fallaba quedaba <b>templates NUEVO con morphs VIEJO</b>.</para>
'''
''' <para>⛔ <b>Y ese estado no es «falta un archivo»: es peor que no haber guardado.</b> Con la migración de
''' claves los nombres de plantilla CAMBIAN (llevan el object id embebido), así que el morphs viejo nombra
''' plantillas que el templates nuevo ya no declara. f4ee no encuentra la clave y esos NPC se quedan
''' <b>SIN body morphs en el juego</b>, en silencio — y andaban antes del guardado. El usuario pidió guardar
''' y perdió lo que tenía.</para>
'''
''' <para>La transacción es la mínima que arregla eso: los dos cuerpos se serializan en memoria ANTES de
''' tocar el disco, se saca una foto del templates que había, y si morphs falla se vuelve templates a esa
''' foto. El par queda como estaba —consistente— y lo que se pierde es el guardado, que es lo correcto: un
''' guardado que no se pudo completar no tiene por qué llevarse lo que ya funcionaba.</para>
'''
''' <para>⛔ La foto se saca ACÁ y no se confía en la copia de <c>GuardarConCopia</c>: esa numera sus slots
''' (<c>.prev1</c>, <c>.prev2</c>, …) según cuántas herede, así que desde afuera no se sabe cuál es la de
''' esta corrida. La foto propia son los bytes que acabamos de leer, y no depende de ese esquema.</para></summary>
Friend Module EscrituraDelParBodyGen

    ''' <summary>Qué quedó en el disco cuando la escritura del par falló.</summary>
    Friend Class ParDeBodyGenException
        Inherits IOException

        ''' <summary>True → el par quedó CONSISTENTE (se pudo deshacer templates): no se guardó nada nuevo,
        ''' pero lo que ya andaba sigue andando. False → quedó DESAJUSTADO y hay que volver a guardar.</summary>
        Public ReadOnly Consistente As Boolean

        ''' <summary>⛔ El parámetro NO se llama <c>consistente</c>, y no es cosmético: en VB los identificadores
        ''' son insensibles a mayúsculas, así que un parámetro <c>consistente</c> TAPA al campo
        ''' <c>Consistente</c> y <c>Consistente = consistente</c> se asigna a sí mismo — el campo queda en
        ''' False para siempre. Estaba escrito así y el testigo C53 lo cazó: la vuelta atrás funcionaba, el
        ''' mensaje decía «quedaron como estaban», y la bandera decía DESAJUSTADO. El aviso habría mandado al
        ''' usuario a arreglar un daño inexistente cada vez.</summary>
        Public Sub New(mensaje As String, quedoConsistente As Boolean, inner As Exception)
            MyBase.New(mensaje, inner)
            Me.Consistente = quedoConsistente
        End Sub
    End Class

    ''' <summary>Escribe el par o lo deja como estaba. Tira <see cref="ParDeBodyGenException"/> si no pudo
    ''' completarlo, diciendo en qué estado quedó el disco.</summary>
    Friend Sub EscribirElPar(templatesPath As String, bytesTemplates As Byte(),
                             morphsPath As String, bytesMorphs As Byte())
        ' La foto de lo que había, ANTES de tocar nada. Nothing = no existía.
        Dim fotoTemplates As Byte() = Nothing
        Dim habiaTemplates = File.Exists(templatesPath)
        If habiaTemplates Then
            Try
                fotoTemplates = File.ReadAllBytes(templatesPath)
            Catch ex As Exception
                ' Sin foto no hay vuelta atrás, y escribir igual es apostar a que el segundo no falle. Se
                ' corta ANTES de tocar el disco: el par queda entero.
                Throw New ParDeBodyGenException(
                    $"no se pudo leer el templates.ini que ya estaba ('{Path.GetFileName(templatesPath)}'), " &
                    "así que no había cómo deshacer si fallaba el segundo archivo. No se tocó ninguno de los dos.",
                    quedoConsistente:=True, inner:=ex)
            End Try
        End If

        Escribir(templatesPath, bytesTemplates)

        Try
            Escribir(morphsPath, bytesMorphs)
        Catch exMorphs As Exception
            ' VUELTA ATRÁS de la primera mitad: es lo único que deja el par consistente.
            Dim porQueNoVolvio As String = ""
            Try
                If habiaTemplates Then
                    Escribir(templatesPath, fotoTemplates)
                Else
                    File.Delete(templatesPath)      ' no existía: el par consistente es que siga sin existir
                End If
            Catch exVuelta As Exception
                ' Se sigue: que haya saltado NO decide nada — lo decide el disco, abajo. Pero la causa se
                ' GUARDA: si además resulta que no volvió, es lo único que le dice al usuario por qué.
                porQueNoVolvio = exVuelta.Message
            End Try
            ' ⛔ EL ESTADO SE MIDE, NO SE DEDUCE DE QUE NO HAYA SALTADO UNA EXCEPCIÓN. Y no es teoría: el
            ' testigo C53 lo cazó. `GuardarConCopia` tiene su PROPIA red —si su escritura falla a medias,
            ' restaura el destino desde la copia que tomó y RE-TIRA—, así que hay un camino real en el que la
            ' vuelta atrás SÍ ocurrió y la excepción salió igual. Deduciendo del `Catch`, ese caso se reportaba
            ' como «par DESAJUSTADO» y mandaba al usuario a arreglar un daño que no existía. Se leen los bytes.
            Dim volvio As Boolean
            Try
                If habiaTemplates Then
                    volvio = File.Exists(templatesPath) AndAlso
                             File.ReadAllBytes(templatesPath).SequenceEqual(fotoTemplates)
                Else
                    volvio = Not File.Exists(templatesPath)
                End If
            Catch
                volvio = False      ' no se pudo ni comprobar ⇒ no se puede afirmar que quedó consistente
            End Try
            If volvio Then
                Throw New ParDeBodyGenException(
                    $"no se pudo escribir '{Path.GetFileName(morphsPath)}' ({exMorphs.Message}). " &
                    $"Se deshizo '{Path.GetFileName(templatesPath)}': los dos quedaron como estaban antes de " &
                    "guardar, así que los body morphs que ya funcionaban siguen funcionando.",
                    quedoConsistente:=True, inner:=exMorphs)
            End If
            Throw New ParDeBodyGenException(
                $"no se pudo escribir '{Path.GetFileName(morphsPath)}' ({exMorphs.Message}) y TAMPOCO se pudo " &
                $"deshacer '{Path.GetFileName(templatesPath)}'" &
                If(porQueNoVolvio = "", "", $" ({porQueNoVolvio})") & ". Los dos archivos quedaron DESAJUSTADOS: " &
                "morphs.ini nombra plantillas que templates.ini ya no declara, así que esos NPC pierden sus " &
                "body morphs en el juego hasta que se vuelva a guardar.",
                quedoConsistente:=False, inner:=exMorphs)
        End Try
    End Sub

    Private Sub Escribir(path As String, bytes As Byte())
        BSA_BA2_Library_DLL.EscrituraEnElLugar.GuardarConCopia(path, Sub(fs) fs.Write(bytes, 0, bytes.Length))
    End Sub

End Module
