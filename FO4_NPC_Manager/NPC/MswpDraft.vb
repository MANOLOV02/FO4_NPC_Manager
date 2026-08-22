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
        Return New MswpDraft With {.Record = Canon.CanonRecords.MswpNuevo(game),
                                   .FormID = formID, .IsOverride = False, .IsNew = True}
    End Function

    ''' <summary>Una edición de un record que ya existe. Se trabaja sobre una COPIA: cancelar el
    ''' editor tiene que dejar el original como estaba.</summary>
    Public Shared Function Edicion(rec As PluginRecord, plugins As PluginManager) As MswpDraft
        Dim abierto = Canon.CanonRecords.Mswp(rec, plugins)
        If abierto Is Nothing Then Return Nothing
        Return New MswpDraft With {.Record = abierto.Copia(), .FormID = rec.Header.FormID,
                                   .IsOverride = True, .IsNew = False}
    End Function

    Public Function Clone() As MswpDraft
        Return New MswpDraft With {
            .Record = Record?.Copia(),
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
