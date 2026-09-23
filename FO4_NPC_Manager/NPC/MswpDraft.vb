Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Un cambio de materiales que se está editando y todavía no se guardó.
'''
''' <para>El borrador NO copia el record: LO ES. <see cref="Record"/> es el árbol de campos, y
''' editarlo es editar lo que se va a guardar. Antes esta clase repetía cada campo del record y
''' había que acordarse de volcarlos de un lado al otro al abrir el editor y al guardar; el campo
''' que alguien se olvidaba de copiar se perdía sin ruido.</para>
'''
''' <para>Lo único que agrega son los datos de AUTORÍA, que no viven en el record: si es nuevo o
''' una edición de uno existente, y si tiene cambios sin guardar.</para>
'''
''' <para>Dos formas:</para>
''' <list type="bullet">
''' <item><b>Nuevo</b>: el identificador es provisional (byte alto 0xFF) para que otros borradores
''' puedan referenciarlo antes de guardar; al guardar se le asigna el real y se reindexa.</item>
''' <item><b>Edición</b>: el identificador ES el real del record que se está sobrescribiendo.</item>
''' </list></summary>
Public Class MswpDraft

    ''' <summary>Prefijo del identificador de editor. Al guardar se le inyecta el nombre del archivo
    ''' destino, para que sea reconocible y no choque entre plugins.</summary>
    Public Const EditorIdPrefix As String = "npcm_MSWP_"

    ''' <summary>El record que se está editando. Todo lo que el usuario cambia va acá.</summary>
    Public Property Record As Canon.IMswp

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
    ' Creación
    '==============================================================================================

    ''' <summary>Un cambio de materiales nuevo, vacío.</summary>
    Public Shared Function Nuevo(formID As UInteger, game As Canon.WbGame) As MswpDraft
        Dim r = Canon.CanonRecords.MswpNuevo(game)
        Borradores.ExigirRecord(r, "MSWP", $"el formato de {game} no declara ese record")
        Return New MswpDraft With {.Record = r,
                                   .FormID = formID, .IsOverride = False, .IsNew = True}
    End Function

    ''' <summary>Una edición de un record que ya existe. Se trabaja sobre una COPIA: cancelar el
    ''' editor tiene que dejar el original como estaba.</summary>
    Public Shared Function Edicion(rec As PluginRecord, plugins As PluginManager) As MswpDraft
        Borradores.ExigirPluginsNormalizados(plugins)
        If rec Is Nothing Then Return Nothing
        Dim abierto = Canon.CanonRecords.Mswp(rec, plugins)
        If abierto Is Nothing Then Return Nothing
        Dim copia = abierto.Copia()
        Borradores.ExigirRecord(copia, "MSWP", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Return New MswpDraft With {.Record = copia, .FormID = rec.Header.FormID,
                                   .IsOverride = True, .IsNew = False}
    End Function

    ''' <summary>Un record NUEVO a partir de uno que ya existe (una plantilla).
    ''' <para>⛔ FALTABA, y por eso el editor de MSWP no tenía «New from template…»: era la Única de
    ''' las seis clases con borrador sin su `Clon`. Las otras cinco (ARMO, ARMA, HDPT, TXST, FLST) la
    ''' tienen con esta misma forma.</para>
    ''' <para>⛔ No se reconstruye campo por campo: copiar el ÁRBOL trae TODO lo que el record tenía,
    ''' incluidos los campos que la app no modela y los que ningún editor muestra. El porqué largo
    ''' está en <c>ArmoDraft.Clon</c>, con el caso medido que lo pagó.</para>
    ''' <para>El EditorID NO se toca acá: lo pone el editor, que es quien sabe si el usuario le dio
    ''' uno o hay que sintetizarlo.</para></summary>
    Public Shared Function Clon(rec As PluginRecord, plugins As PluginManager,
                                formIDNuevo As UInteger) As MswpDraft
        Dim d = Edicion(rec, plugins)
        If d Is Nothing Then Return Nothing
        Return ClonDesdeCopia(d.Record, formIDNuevo)
    End Function

    ''' <summary>La cola COMÚN de todo clon: exigir la copia y darle identidad nueva. Gemela de la de
    ''' <c>TxstDraft</c>; el porqué —incluido el defecto del clon que nacía <c>Deleted</c> por heredar
    ''' <c>RecordFlags</c>— está en <c>ArmoDraft</c>.</summary>
    Private Shared Function ClonDesdeCopia(copia As Canon.IMswp, formIDNuevo As UInteger) As MswpDraft
        Borradores.ExigirRecord(copia, "MSWP", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Dim d As New MswpDraft With {.Record = copia, .FormID = formIDNuevo,
                                     .IsOverride = False, .IsNew = True}
        Borradores.ReidentificarComoClon(d.Record, formIDNuevo)
        Return d
    End Function

    Public Function Clone() As MswpDraft
        ' ⛔ `Clone` es la TERCERA puerta: también CONSTRUYE un borrador, y `Copia()` puede
        ' devolver Nothing por los mismos tres caminos. Su resultado se registra en producción —
        ' `_openSnapshot = _draft.Clone()`, y `RevertOrDiscardCurrentDraft` lo vuelve a meter en el
        ' mapa que consultan el render y el guardado.
        Dim copiaClone = Record?.Copia()
        Borradores.ExigirRecord(copiaClone, "MSWP", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Return New MswpDraft With {
            .Record = copiaClone,
            .FormID = FormID,
            .IsOverride = IsOverride,
            .IsNew = IsNew,
            .IsModified = IsModified
        }
    End Function

    ''' <summary>Mismo contenido que <paramref name="o"/>, sin mirar identidad ni estado.
    ''' <para>Se compara por los bytes que produciría cada uno. Comparar campo por campo obliga a
    ''' acordarse de todos, y el que se olvida es justo el que después aparece como "editado" sin
    ''' que nadie lo haya tocado.</para></summary>
    Public Function ContentEquals(o As MswpDraft) As Boolean
        If o Is Nothing Then Return False
        If Record Is Nothing OrElse o.Record Is Nothing Then Return Record Is o.Record
        Return Record.MismoContenido(o.Record)
    End Function

End Class
