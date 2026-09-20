Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Una lista de formularios (<c>FLST</c>) que se está editando y todavía no se guardó.
'''
''' <para>Gemelo de <see cref="HdptDraft"/> y de <see cref="ArmaDraft"/>: el borrador NO copia el
''' record, LO ES. La ley compartida vive en <see cref="Borradores"/> y en
''' <c>TomaDeBorrador(Of TD)</c>; el porqué de cada decisión de forma está en <c>ArmoDraft</c>.</para>
'''
''' <para>Existe por el <c>RNAM</c> de un head part: «en qué razas es válido» es una FLST, así que sin
''' borrador de FLST un head part propio no puede declararse válido para una raza custom.</para>
'''
''' <para>⛔ <b><c>LNAM</c> es un arreglo de FormID SIN firma declarada</b> en el esquema
''' (<c>Wb.Fid("FormID")</c>, sin lista de firmas permitidas): una FLST puede contener cualquier cosa.
''' Para el uso que trae esta ola contiene RACE, y por eso el editor abierto desde un <c>RNAM</c>
''' arranca filtrado a RACE — pero el FILTRO es de la UI, no del record, y el record se preserva tal
''' como viene.</para>
'''
''' <para>Medido sobre el corpus: de los 2.546 HDPT de Fallout 4, los <b>2.528</b> con <c>RNAM</c> distinto de cero apuntan a <b>sólo SEIS
''' FLST distintas</b> en todo el juego (52 en Skyrim, contando el ganador por FormID). Por eso el
''' gesto por defecto del editor es <b>clonar una de ésas y agregarle una raza</b>, no armar una lista
''' desde cero.</para></summary>
Public Class FlstDraft

    ''' <summary>Prefijo del identificador de editor. Al guardar se le inyecta el nombre del archivo
    ''' destino, para que sea reconocible y no choque entre plugins.</summary>
    Public Const EditorIdPrefix As String = "npcm_FLST_"

    ''' <summary>El record que se está editando. Todo lo que el usuario cambia va acá.</summary>
    Public Property Record As Canon.IFlst

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

    ''' <summary>Una lista de formularios nueva, vacía.</summary>
    Public Shared Function Nuevo(formID As UInteger, game As Canon.WbGame) As FlstDraft
        Dim r = Canon.CanonRecords.FlstNuevo(game)
        Borradores.ExigirRecord(r, "FLST", $"el formato de {game} no declara ese record")
        Return New FlstDraft With {.Record = r, .FormID = formID, .IsOverride = False, .IsNew = True}
    End Function

    ''' <summary>Una edición de un record que ya existe. Se trabaja sobre una COPIA: cancelar el editor
    ''' tiene que dejar el original como estaba.</summary>
    Public Shared Function Edicion(rec As PluginRecord, plugins As PluginManager) As FlstDraft
        Borradores.ExigirPluginsNormalizados(plugins)
        If rec Is Nothing Then Return Nothing
        Dim abierto = Canon.CanonRecords.Flst(rec, plugins)
        If abierto Is Nothing Then Return Nothing
        Dim copia = abierto.Copia()
        Borradores.ExigirRecord(copia, "FLST", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Return New FlstDraft With {.Record = copia, .FormID = rec.Header.FormID,
                                   .IsOverride = True, .IsNew = False}
    End Function

    ''' <summary>Un record NUEVO a partir de uno que ya existe (una plantilla). ⛔ No se reconstruye
    ''' campo por campo: copiar el árbol trae TODO lo que el record tenía, incluidos los campos que la
    ''' app no modela y los que ningún editor muestra, y
    ''' enumerarlos a mano garantiza que alguno falte. El porqué largo está en <c>ArmoDraft.Clon</c>,
    ''' con el caso medido que lo pagó.
    ''' <para>El EditorID NO se toca acá: lo pone el editor, que es quien sabe si el usuario le dio uno
    ''' o hay que sintetizarlo.</para></summary>
    Public Shared Function Clon(rec As PluginRecord, plugins As PluginManager,
                                formIDNuevo As UInteger) As FlstDraft
        Dim d = Edicion(rec, plugins)
        If d Is Nothing Then Return Nothing
        Return ClonDesdeCopia(d.Record, formIDNuevo)
    End Function

    ''' <summary>La cola COMÚN de todo clon: exigir la copia y darle identidad nueva. Gemela de la de
    ''' <c>ArmoDraft</c>; el porqué —incluido el defecto del clon que nacía <c>Deleted</c> por heredar
    ''' <c>RecordFlags</c>— está allá.</summary>
    Private Shared Function ClonDesdeCopia(copia As Canon.IFlst, formIDNuevo As UInteger) As FlstDraft
        Borradores.ExigirRecord(copia, "FLST", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Dim d As New FlstDraft With {.Record = copia, .FormID = formIDNuevo,
                                     .IsOverride = False, .IsNew = True}
        Borradores.ReidentificarComoClon(d.Record, formIDNuevo)
        Return d
    End Function

    ''' <summary>Un record NUEVO a partir de un BORRADOR PROPIO que el usuario ya está editando. Gemela
    ''' de <c>ArmoDraft.ClonDeBorrador</c>: «copiar» tiene que copiar LO QUE EL USUARIO VE, no la versión
    ''' del archivo. El porqué —incluido por qué NO se le cambia la firma a <see cref="Clon"/>— está allá.</summary>
    Public Shared Function ClonDeBorrador(origen As FlstDraft, formIDNuevo As UInteger) As FlstDraft
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

    Public Function Clone() As FlstDraft
        ' ⛔ `Clone` es la TERCERA puerta: también CONSTRUYE un borrador, y `Copia()` puede devolver
        ' Nothing por los mismos tres caminos. Su resultado se registra en producción (el snapshot de
        ' apertura que la reversión vuelve a meter en el mapa que consultan el render y el guardado).
        Dim copiaClone = Record?.Copia()
        Borradores.ExigirRecord(copiaClone, "FLST", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Return New FlstDraft With {
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
    Public Function ContentEquals(o As FlstDraft) As Boolean
        If o Is Nothing Then Return False
        If Record Is Nothing OrElse o.Record Is Nothing Then Return Record Is o.Record
        Return Record.MismoContenido(o.Record)
    End Function

End Class
