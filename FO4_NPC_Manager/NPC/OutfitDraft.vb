Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Un atuendo que se está armando y todavía no se guardó.
'''
''' <para>El borrador NO copia el record: LO ES. <see cref="Record"/> es el árbol de campos, y
''' editarlo es editar lo que se va a guardar. Antes esta clase repetía los campos del record y
''' había que acordarse de volcarlos al abrir el editor y al guardar; el campo que alguien se
''' olvidaba se perdía sin ruido.</para>
'''
''' <para>Lo que agrega son los datos de AUTORÍA, que no viven en el record —si es nuevo o una
''' edición, y si tiene cambios sin guardar— y una caché de presentación que tampoco se guarda
''' (<see cref="LvliRealization"/>).</para>
'''
''' <para>Dos formas:</para>
''' <list type="bullet">
''' <item><b>Nuevo</b>: el identificador es provisional (byte alto 0xFF) para que un NPC pueda
''' referenciarlo antes de guardar; al guardar se le asigna el real y se reindexa.</item>
''' <item><b>Edición</b>: el identificador ES el real del record que se sobrescribe.</item>
''' </list></summary>
Public Class OutfitDraft

    ''' <summary>Byte alto del identificador provisional de un borrador sin guardar. 0xFF nunca es
    ''' un índice de master real (el tope son 254), así que no puede chocar con un record cargado.
    ''' Al guardar se reescribe como (índice propio del plugin) &lt;&lt; 24 | número de objeto.</summary>
    Public Const DraftFormIdHighByte As UInteger = &HFF000000UI

    ''' <summary>Prefijo del identificador de editor. Al guardar se le inyecta el nombre del archivo
    ''' destino, para que sea reconocible y no choque entre plugins.</summary>
    Public Const EditorIdPrefix As String = "npcm_Outfit_"

    ''' <summary>Identificador reservado del atuendo de PREVISUALIZACIÓN del selector: el conjunto
    ''' que se está armando y que se vuelve a registrar en cada cambio para que el render lo resuelva
    ''' como a cualquier borrador. El número de objeto 0x7FF queda justo debajo del piso de asignación
    ''' real, así que no puede chocar con uno confirmado. Nunca se persiste.</summary>
    Public Const PreviewDraftFormID As UInteger = &HFF0007FFUI

    ''' <summary>El record que se está editando. Todo lo que el usuario cambia va acá.</summary>
    Public Property Record As Canon.IOtft

    ''' <summary>Nuevo: identificador provisional. Edición: el real del record original.</summary>
    Public Property FormID As UInteger

    ''' <summary>Sólo para mostrar, no se guarda: qué prendas concretas salieron sorteadas para cada
    ''' lista por nivel del atuendo, por identificador de la lista. Se cachea para que la vista previa
    ''' no cambie sola entre dibujados; volver a sortear borra la entrada.</summary>
    Public ReadOnly Property LvliRealization As New Dictionary(Of UInteger, List(Of UInteger))

    ''' <summary>True = edita un atuendo existente. False = uno nuevo.</summary>
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

    ''' <summary>El identificador es el provisional de un borrador sin guardar. Deja que el render y
    ''' los resolvedores detecten que un atuendo apunta a algo que todavía no existe en ningún archivo
    ''' y lo resuelvan desde el borrador.</summary>
    Public Shared Function IsDraftFormID(formID As UInteger) As Boolean
        Return (formID And &HFF000000UI) = DraftFormIdHighByte
    End Function

    '==============================================================================================
    ' Creación
    '==============================================================================================

    ''' <summary>Un atuendo nuevo, vacío.</summary>
    Public Shared Function Nuevo(formID As UInteger, game As Canon.WbGame) As OutfitDraft
        Return New OutfitDraft With {.Record = Canon.CanonRecords.OtftNuevo(game),
                                     .FormID = formID, .IsOverride = False, .IsNew = True}
    End Function

    ''' <summary>Una edición de un atuendo que ya existe. Se trabaja sobre una COPIA: cancelar el
    ''' editor tiene que dejar el original como estaba.</summary>
    Public Shared Function Edicion(rec As PluginRecord, plugins As PluginManager) As OutfitDraft
        Dim abierto = Canon.CanonRecords.Otft(rec, plugins)
        If abierto Is Nothing Then Return Nothing
        Return New OutfitDraft With {.Record = abierto.Copia(), .FormID = rec.Header.FormID,
                                     .IsOverride = True, .IsNew = False}
    End Function

    '==============================================================================================
    ' Las prendas
    '==============================================================================================

    ''' <summary>Los identificadores de las prendas del atuendo, en orden.</summary>
    Public Function Prendas() As List(Of UInteger)
        If Record Is Nothing Then Return New List(Of UInteger)
        Return Record.Prendas()
    End Function

    ''' <summary>Deja el atuendo con exactamente esas prendas, en ese orden.
    ''' <para>Se reemplaza entero en vez de ir agregando y sacando porque el editor muestra una lista
    ''' que el usuario reordena libremente, y llevar la cuenta de qué se movió a dónde es la clase de
    ''' contabilidad que termina desincronizada.</para></summary>
    Public Sub ReemplazarPrendas(ids As IEnumerable(Of UInteger))
        If Record Is Nothing Then Return
        While Record.Items.Count > 0
            If Not Record.QuitarItems(0) Then Exit While
        End While
        If ids Is Nothing Then Return
        For Each id In ids
            Dim e = Record.AgregarItems()
            If e Is Nothing Then Exit For
            e.Item = id
        Next
    End Sub

    '==============================================================================================
    ' Copiar y comparar
    '==============================================================================================

    Public Function Clone() As OutfitDraft
        Dim c As New OutfitDraft With {
            .Record = Record?.Copia(),
            .FormID = FormID,
            .IsOverride = IsOverride,
            .IsNew = IsNew,
            .IsModified = IsModified
        }
        For Each kv In LvliRealization
            c.LvliRealization(kv.Key) = New List(Of UInteger)(kv.Value)
        Next
        Return c
    End Function

    ''' <summary>Mismo contenido que <paramref name="o"/>, sin mirar identidad ni estado.
    ''' <para>Se compara por los bytes que produciría cada uno. Comparar campo por campo obliga a
    ''' acordarse de todos, y el que se olvida es justo el que después aparece como "editado" sin que
    ''' nadie lo haya tocado.</para></summary>
    Public Function ContentEquals(o As OutfitDraft) As Boolean
        If o Is Nothing Then Return False
        If Record Is Nothing OrElse o.Record Is Nothing Then Return Record Is o.Record
        Return Record.MismoContenido(o.Record)
    End Function

End Class
