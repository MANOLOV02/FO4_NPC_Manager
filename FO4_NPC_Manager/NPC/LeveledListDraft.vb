Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Una lista por nivel que se está armando y todavía no se guardó.
'''
''' <para>El borrador NO copia el record: LO ES. <see cref="Record"/> es el árbol de campos, y
''' editarlo es editar lo que se va a guardar. Antes esta clase repetía los campos del record y
''' había que acordarse de volcarlos al abrir el editor y al guardar.</para>
'''
''' <para>Deja que un atuendo referencie una ranura por nivel armada por el usuario, y que una lista
''' contenga otras listas. Lo único que agrega sobre el record son los datos de AUTORÍA.</para>
'''
''' <para>Un borrador nuevo arranca con un identificador provisional (byte alto 0xFF, el mismo
''' esquema que <see cref="OutfitDraft"/>, y sale del mismo contador para que no choquen). Al
''' guardar se le asigna el real y se reapuntan todas las referencias.</para></summary>
Public Class LeveledListDraft

    ''' <summary>Prefijo del identificador de editor. Al guardar se le inyecta el nombre del archivo
    ''' destino, para que sea reconocible y no choque entre plugins.</summary>
    Public Const EditorIdPrefix As String = "npcm_LVLI_"

    ''' <summary>El record que se está editando. Todo lo que el usuario cambia va acá.</summary>
    Public Property Record As Canon.ILvli

    ''' <summary>Nuevo: identificador provisional. Edición: el real del record original.</summary>
    Public Property FormID As UInteger

    ''' <summary>True = edita una lista existente y conserva su identificador real. False = una
    ''' nueva, con identificador provisional hasta que se guarde.</summary>
    Public Property IsOverride As Boolean = False

    ''' <summary>Todavía no se escribió nunca.</summary>
    Public Property IsNew As Boolean = True

    ''' <summary>Ya se escribió antes y se volvió a editar.</summary>
    Public Property IsModified As Boolean = False

    ''' <summary>Cualquiera de las dos obliga a (re)escribirla al guardar.</summary>
    Public ReadOnly Property IsDirty As Boolean
        Get
            Return IsNew OrElse IsModified
        End Get
    End Property

    '==============================================================================================
    ' Creación
    '==============================================================================================

    ''' <summary>Una lista nueva, vacía.</summary>
    Public Shared Function Nuevo(formID As UInteger, game As Canon.WbGame) As LeveledListDraft
        Dim r = Canon.CanonRecords.LvliNuevo(game)
        Borradores.ExigirRecord(r, "LVLI", $"el formato de {game} no declara ese record")
        Return New LeveledListDraft With {.Record = r,
                                          .FormID = formID, .IsOverride = False, .IsNew = True}
    End Function

    ''' <summary>Una edición de una lista que ya existe. Se trabaja sobre una COPIA: cancelar el
    ''' editor tiene que dejar el original como estaba.</summary>
    Public Shared Function Edicion(rec As PluginRecord, plugins As PluginManager) As LeveledListDraft
        Borradores.ExigirPluginsNormalizados(plugins)
        If rec Is Nothing Then Return Nothing
        Dim abierto = Canon.CanonRecords.Lvli(rec, plugins)
        If abierto Is Nothing Then Return Nothing
        Dim copia = abierto.Copia()
        Borradores.ExigirRecord(copia, "LVLI", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Return New LeveledListDraft With {.Record = copia, .FormID = rec.Header.FormID,
                                          .IsOverride = True, .IsNew = False}
    End Function

    '==============================================================================================
    ' Copiar y comparar
    '==============================================================================================

    Public Function Clone() As LeveledListDraft
        ' ⛔ `Clone` es la TERCERA puerta: también CONSTRUYE un borrador, y `Copia()` puede devolver
        ' Nothing por los mismos tres caminos. En ARMA, ARMO y MSWP su resultado se registra en
        ' producción (`_openSnapshot`, que `RevertOrDiscardCurrentDraft` vuelve a meter en el mapa que
        ' consultan el render y el guardado). Acá el llamador es `OutfitPicker_Form.SnapshotAntesDeMutar`,
        ' que guarda el estado de APERTURA de una lista preexistente para revertirla si el diálogo se
        ' cancela — el mismo gesto, por la misma puerta. Por eso la guarda: un borrador sin record no es
        ' un borrador, y el que restaura no puede recibir uno vacío.
        Dim copiaClone = Record?.Copia()
        Borradores.ExigirRecord(copiaClone, "LVLI", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Return New LeveledListDraft With {
            .Record = copiaClone,
            .FormID = FormID,
            .IsOverride = IsOverride,
            .IsNew = IsNew,
            .IsModified = IsModified
        }
    End Function

    ''' <summary>Mismo contenido que <paramref name="o"/>, sin mirar identidad ni estado.
    ''' <para>Se compara por los bytes que produciría cada uno. Comparar campo por campo obliga a
    ''' acordarse de todos, y el que se olvida es justo el que después aparece como "editado" sin que
    ''' nadie lo haya tocado.</para></summary>
    Public Function ContentEquals(o As LeveledListDraft) As Boolean
        If o Is Nothing Then Return False
        If Record Is Nothing OrElse o.Record Is Nothing Then Return Record Is o.Record
        Return Record.MismoContenido(o.Record)
    End Function

End Class
