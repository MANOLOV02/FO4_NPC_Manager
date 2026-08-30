Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Una armadura que se está editando y todavía no se guardó.
'''
''' <para>El borrador NO copia el record: LO ES. <see cref="Record"/> es el árbol de campos, y
''' editarlo es editar lo que se va a guardar. Antes esta clase repetía cada campo del record y
''' había que acordarse de volcarlos de un lado al otro al abrir el editor y al guardar; el campo
''' que alguien se olvidaba de copiar se perdía sin ruido.</para>
'''
''' <para>Lo único que agrega son los datos de AUTORÍA, que no viven en el record: si es nuevo o una
''' edición de uno existente, y si tiene cambios sin guardar.</para>
'''
''' <para>Dos formas:</para>
''' <list type="bullet">
''' <item><b>Nuevo</b>: el identificador es provisional (byte alto 0xFF) para que un NPC o un atuendo
''' puedan referenciarlo antes de guardar; al guardar se le asigna el real y se reindexa.</item>
''' <item><b>Edición</b>: el identificador ES el real del record que se está sobrescribiendo.</item>
''' </list>
'''
''' <para>Ya no hace falta una marca aparte de "se tocaron las combinaciones": comparar por los
''' bytes que produce el record detecta cualquier cambio, y además lo detecta en las dos
''' direcciones, así que deshacer una edición vuelve a dejar el borrador limpio.</para></summary>
Public Class ArmoDraft

    ''' <summary>Prefijo del identificador de editor. Al guardar se le inyecta el nombre del archivo
    ''' destino, para que sea reconocible y no choque entre plugins.</summary>
    Public Const EditorIdPrefix As String = "npcm_ARMO_"

    ''' <summary>El record que se está editando. Todo lo que el usuario cambia va acá.</summary>
    Public Property Record As Canon.IArmo

    ''' <summary>Nuevo: identificador provisional. Edición: el real del record original.</summary>
    Public Property FormID As UInteger

    ''' <summary>True = edita un record existente. False = uno nuevo.</summary>
    Public Property IsOverride As Boolean

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

    ''' <summary>Una armadura nueva, vacía.</summary>
    Public Shared Function Nuevo(formID As UInteger, game As Canon.WbGame) As ArmoDraft
        Return New ArmoDraft With {.Record = Canon.CanonRecords.ArmoNuevo(game),
                                   .FormID = formID, .IsOverride = False, .IsNew = True}
    End Function

    ''' <summary>Una edición de un record que ya existe. Se trabaja sobre una COPIA: cancelar el
    ''' editor tiene que dejar el original como estaba.</summary>
    Public Shared Function Edicion(rec As PluginRecord, plugins As PluginManager) As ArmoDraft
        ' ⛔ SIN PluginManager NO HAY BORRADOR. `NormalizarReferencias` se va sin hacer nada cuando
        ' `plugins` es Nothing, así que el árbol quedaría con los FormID LOCALES del archivo fuente
        ' mientras todo lo de arriba —las propiedades de referencia, el resolvedor del render y el
        ' reindexado del guardado— asume espacio de orden de carga. Antes no mordía porque los editores
        ' volcaban valores ya globales sobre un record en blanco; ahora el árbol crudo ES el borrador, y
        ' un ESP con las referencias sin reindexar apunta al mod equivocado sin un solo aviso.
        If plugins Is Nothing Then
            Throw New ArgumentNullException(NameOf(plugins),
                "Un árbol sin normalizar no es un borrador editable: sus referencias quedan en el espacio " &
                "LOCAL del archivo de origen y el guardado las reindexaría una segunda vez.")
        End If
        If rec Is Nothing Then Return Nothing
        Dim abierto = Canon.CanonRecords.Armo(rec, plugins)
        If abierto Is Nothing Then Return Nothing
        Return New ArmoDraft With {.Record = abierto.Copia(), .FormID = rec.Header.FormID,
                                   .IsOverride = True, .IsNew = False}
    End Function

    ''' <summary>Un record NUEVO a partir de uno que ya existe (una plantilla). Igual que
    ''' <see cref="Edicion"/> —se trabaja sobre una COPIA con contexto propio— pero con identidad nueva.
    ''' <para>⛔ No se reconstruye campo por campo. Copiar el árbol trae TODO lo que el record tenía,
    ''' incluidos los campos que la app no modela y los que ningún editor muestra; enumerarlos a mano
    ''' garantiza que alguno falte, y el que falta no se nota hasta que alguien lo busca en el archivo.
    ''' En Skyrim, además, la construcción a mano normalizaba la plantilla de cuerpo de BODT a BOD2 y
    ''' perdía General Flags y Armor Type — el 85 % de los ARMA del juego.</para>
    ''' <para>El EditorID NO se toca acá: lo pone el editor, que es quien sabe si el usuario le dio uno
    ''' o hay que sintetizarlo.</para></summary>
    Public Shared Function Clon(rec As PluginRecord, plugins As PluginManager,
                                formIDNuevo As UInteger) As ArmoDraft
        Dim d = Edicion(rec, plugins)
        If d Is Nothing Then Return Nothing
        d.FormID = formIDNuevo
        d.IsOverride = False
        d.IsNew = True
        ' LA IDENTIDAD DEL CONTEXTO, decidida campo por campo y no heredada por descuido:
        Dim v = TryCast(d.Record, Canon.CanonRecordView)
        If v IsNot Nothing AndAlso v.Context IsNot Nothing Then
            ' FormID: el emisor reporta sus avisos con el del contexto, y un clon que arrastre el de la
            ' fuente los publica con el identificador equivocado.
            v.Context.FormID = formIDNuevo
            ' EditorId: mismo argumento, campo de al lado. `SaveNpcEspWriter` arma su mensaje de error
            ' con `Context.EditorId`. El del ÁRBOL lo pone el editor; el del contexto se limpia para que
            ' nadie confunda al clon con su fuente.
            v.Context.EditorId = ""
            ' ⛔ Y NO SE HEREDA `Deleted` (bit 5). El guardado emite `Context.RecordFlags` tal cual, así
            ' que clonar un record marcado como borrado daría un record NUEVO nacido borrado: el usuario
            ' lo ve en la lista, lo guarda, y el motor lo ignora. Un clon es un record nuevo; nace vivo.
            v.Context.RecordFlags = v.Context.RecordFlags And Not &H20UI
        End If
        Return d
    End Function

    '==============================================================================================
    ' Copiar y comparar
    '==============================================================================================

    Public Function Clone() As ArmoDraft
        Return New ArmoDraft With {
            .Record = Record?.Copia(),
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
    Public Function ContentEquals(o As ArmoDraft) As Boolean
        If o Is Nothing Then Return False
        If Record Is Nothing OrElse o.Record Is Nothing Then Return Record Is o.Record
        Return Record.MismoContenido(o.Record)
    End Function

End Class
