Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Un conjunto de texturas (<c>TXST</c>) que se está editando y todavía no se guardó.
'''
''' <para>Gemelo de <see cref="HdptDraft"/> y de <see cref="ArmaDraft"/>: el borrador NO copia el
''' record, LO ES. La ley compartida vive en <see cref="Borradores"/> y en
''' <c>TomaDeBorrador(Of TD)</c>; el porqué de cada decisión de forma está en <c>ArmoDraft</c>.</para>
'''
''' <para>Existe porque un head part propio necesita su propia textura: el <c>TNAM</c> de un HDPT
''' apunta a un TXST, y en los ojos <b>ese TXST ES la diffuse</b>. Sin borrador de TXST, un pelo o unos
''' ojos nuevos sólo podrían apuntar a texturas que ya existan en el orden de carga.</para>
'''
''' <para>⛔ <b><c>OBND</c> está en el 100 % del corpus</b> (1.000 de 1.000 en Fallout 4, 1.579 de
''' 1.579 en Skyrim), así que un TXST nuevo lo tiene que emitir. Y su valor NO se inventa: medido, en
''' FO4 hay <b>sólo DOS valores distintos</b> —<c>(-8,-30,-20, 7,30,20)</c> en 514 records y
''' <b>todo en cero en 486</b>—, y en Skyrim el cero está en 1.483 de 1.579. Un clon copia el de su
''' plantilla; uno en blanco lleva ceros, que es un valor TRANSCRITO del archivo.</para>
'''
''' <para>⛔ <b>Las ocho ranuras de textura se llaman distinto en cada juego</b>: <c>TX02</c> es
''' <i>Wrinkles</i> en Fallout 4 y <i>Environment Mask/Subsurface Tint</i> en Skyrim; ídem <c>TX03</c> y
''' <c>TX07</c>. Las etiquetas salen del esquema del juego de la sesión, no de una constante.</para>
'''
''' <para>⛔ <b><c>DODT</c> y <c>MNAM</c> se PRESERVAN sin editarse.</b> El bloque de decal está en 317
''' de los 1.000 TXST de FO4 y en 62 de los 1.579 de Skyrim, y <c>MNAM</c> (material, sólo FO4) en 187:
''' ninguno lo necesita un head part, y los dos vienen gratis en la copia del árbol. El editor los
''' muestra como presentes y no los toca — que es distinto de perderlos.</para></summary>
Public Class TxstDraft

    ''' <summary>Prefijo del identificador de editor. Al guardar se le inyecta el nombre del archivo
    ''' destino, para que sea reconocible y no choque entre plugins.</summary>
    Public Const EditorIdPrefix As String = "npcm_TXST_"

    ''' <summary>El record que se está editando. Todo lo que el usuario cambia va acá.</summary>
    Public Property Record As Canon.ITxst

    ''' <summary>Nuevo: identificador provisional. Edición: el real del record original.</summary>
    Public Property FormID As UInteger

    ''' <summary>True = edita un record existente. False = uno nuevo.</summary>
    Public Property IsOverride As Boolean

    ''' <summary>Todavía no se escribió nunca.</summary>
    Public Property IsNew As Boolean = True

    ''' <summary>Ya se escribió antes y se volvió a editar.</summary>
    Public Property IsModified As Boolean = False

    ''' <summary>Cualquiera de las dos obliga a (re)escribirlo al guardar.</summary>
    Public ReadOnly Property IsDirty As Boolean
        Get
            Return IsNew OrElse IsModified
        End Get
    End Property

    '==============================================================================================
    ' Creación — las cinco puertas, iguales a las de ArmaDraft
    '==============================================================================================

    ''' <summary>Un conjunto de texturas nuevo, vacío.</summary>
    Public Shared Function Nuevo(formID As UInteger, game As Canon.WbGame) As TxstDraft
        Dim r = Canon.CanonRecords.TxstNuevo(game)
        Borradores.ExigirRecord(r, "TXST", $"el formato de {game} no declara ese record")
        Return New TxstDraft With {.Record = r, .FormID = formID, .IsOverride = False, .IsNew = True}
    End Function

    ''' <summary>Una edición de un record que ya existe. Se trabaja sobre una COPIA: cancelar el editor
    ''' tiene que dejar el original como estaba.</summary>
    Public Shared Function Edicion(rec As PluginRecord, plugins As PluginManager) As TxstDraft
        Borradores.ExigirPluginsNormalizados(plugins)
        If rec Is Nothing Then Return Nothing
        Dim abierto = Canon.CanonRecords.Txst(rec, plugins)
        If abierto Is Nothing Then Return Nothing
        Dim copia = abierto.Copia()
        Borradores.ExigirRecord(copia, "TXST", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Return New TxstDraft With {.Record = copia, .FormID = rec.Header.FormID,
                                   .IsOverride = True, .IsNew = False}
    End Function

    ''' <summary>Un record NUEVO a partir de uno que ya existe (una plantilla). ⛔ No se reconstruye
    ''' campo por campo: copiar el árbol trae TODO lo que el record tenía, incluidos los campos que la
    ''' app no modela y los que ningún editor muestra —acá, el bloque <c>DODT</c> de decal—, y
    ''' enumerarlos a mano garantiza que alguno falte. El porqué largo está en <c>ArmoDraft.Clon</c>,
    ''' con el caso medido que lo pagó.
    ''' <para>El EditorID NO se toca acá: lo pone el editor, que es quien sabe si el usuario le dio uno
    ''' o hay que sintetizarlo.</para></summary>
    Public Shared Function Clon(rec As PluginRecord, plugins As PluginManager,
                                formIDNuevo As UInteger) As TxstDraft
        Dim d = Edicion(rec, plugins)
        If d Is Nothing Then Return Nothing
        Return ClonDesdeCopia(d.Record, formIDNuevo)
    End Function

    ''' <summary>La cola COMÚN de todo clon: exigir la copia y darle identidad nueva. Gemela de la de
    ''' <c>ArmoDraft</c>; el porqué —incluido el defecto del clon que nacía <c>Deleted</c> por heredar
    ''' <c>RecordFlags</c>— está allá.</summary>
    Private Shared Function ClonDesdeCopia(copia As Canon.ITxst, formIDNuevo As UInteger) As TxstDraft
        Borradores.ExigirRecord(copia, "TXST", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Dim d As New TxstDraft With {.Record = copia, .FormID = formIDNuevo,
                                     .IsOverride = False, .IsNew = True}
        Borradores.ReidentificarComoClon(d.Record, formIDNuevo)
        Return d
    End Function

    ''' <summary>Un record NUEVO a partir de un BORRADOR PROPIO que el usuario ya está editando. Gemela
    ''' de <c>ArmoDraft.ClonDeBorrador</c>: «copiar» tiene que copiar LO QUE EL USUARIO VE, no la versión
    ''' del archivo. El porqué —incluido por qué NO se le cambia la firma a <see cref="Clon"/>— está allá.</summary>
    Public Shared Function ClonDeBorrador(origen As TxstDraft, formIDNuevo As UInteger) As TxstDraft
        If origen Is Nothing OrElse origen.Record Is Nothing Then
            Throw New ArgumentException(
                "ClonDeBorrador necesita un borrador CON record: sin él no hay árbol que copiar y el " &
                "editor abriría vacío en vez de con la copia que el usuario pidió.", NameOf(origen))
        End If
        Return ClonDesdeCopia(origen.Record.Copia(), formIDNuevo)
    End Function

    '==============================================================================================
    ' Copiar y comparar
    '==============================================================================================

    Public Function Clone() As TxstDraft
        ' ⛔ `Clone` es la TERCERA puerta: también CONSTRUYE un borrador, y `Copia()` puede devolver
        ' Nothing por los mismos tres caminos. Su resultado se registra en producción (el snapshot de
        ' apertura que la reversión vuelve a meter en el mapa que consultan el render y el guardado).
        Dim copiaClone = Record?.Copia()
        Borradores.ExigirRecord(copiaClone, "TXST", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Return New TxstDraft With {
            .Record = copiaClone,
            .FormID = FormID,
            .IsOverride = IsOverride,
            .IsNew = IsNew,
            .IsModified = IsModified
        }
    End Function

    ''' <summary>Mismo contenido que <paramref name="o"/>, sin mirar identidad ni estado. Se compara por
    ''' los bytes que produciría cada uno: comparar campo por campo obliga a acordarse de todos, y el que
    ''' se olvida es justo el que después aparece como "editado" sin que nadie lo haya tocado.</summary>
    Public Function ContentEquals(o As TxstDraft) As Boolean
        If o Is Nothing Then Return False
        If Record Is Nothing OrElse o.Record Is Nothing Then Return Record Is o.Record
        Return Record.MismoContenido(o.Record)
    End Function

End Class
