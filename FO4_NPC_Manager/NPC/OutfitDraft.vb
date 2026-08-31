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

    ''' <summary>Prefijo del identificador de editor. Al guardar se le inyecta el nombre del archivo
    ''' destino, para que sea reconocible y no choque entre plugins.</summary>
    Public Const EditorIdPrefix As String = "npcm_Outfit_"

    ''' <summary>Identificador reservado del atuendo de PREVISUALIZACIÓN del selector: el conjunto
    ''' que se está armando y que se vuelve a registrar en cada cambio para que el render lo resuelva
    ''' como a cualquier borrador. El número de objeto 0x7FF queda justo debajo del piso de asignación
    ''' real, así que no puede chocar con uno confirmado. Nunca se persiste.
    ''' <para>Se compone del centinela COMPARTIDO en vez de escribir <c>&amp;HFF0007FF</c> a mano: escrito
    ''' como literal, el byte alto quedaba repetido acá, y el día que el centinela cambiara este número
    ''' no lo seguiría — dejaría de ser reconocido como borrador sin que nada fallara al compilar.</para></summary>
    Public Const PreviewDraftFormID As UInteger = Borradores.FormIdAltoDeBorrador Or &H7FFUI

    ''' <summary>El record que se está editando. Todo lo que el usuario cambia va acá.</summary>
    Public Property Record As Canon.IOtft

    ''' <summary>Nuevo: identificador provisional. Edición: el real del record original.</summary>
    Public Property FormID As UInteger

    ''' <summary>Sólo para mostrar, no se guarda: qué prendas concretas salieron sorteadas para cada
    ''' lista por nivel del atuendo, por identificador de la lista. Se cachea para que la vista previa
    ''' no cambie sola entre dibujados; volver a sortear borra la entrada.
    ''' <para>⚠️ LATENTE, declarado: la llave es el FormID, así que si el INAM repitiera la MISMA lista
    ''' por nivel dos veces, las dos entradas comparten una sola realización — la vista previa dibujaría
    ''' dos sorteos distintos y el render commiteado, el mismo dos veces. Medido: 0 duplicados en 1.241
    ''' OTFT del orden de carga (750 de Skyrim, 491 de FO4), así que hoy no llega — pero llegaría con un
    ''' atuendo de un mod que repita. Cerrarlo pide llavear por POSICIÓN del INAM, no por FormID.</para></summary>
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

    '==============================================================================================
    ' Creación
    '==============================================================================================

    ''' <summary>Un atuendo nuevo, vacío.</summary>
    Public Shared Function Nuevo(formID As UInteger, game As Canon.WbGame) As OutfitDraft
        Dim r = Canon.CanonRecords.OtftNuevo(game)
        Borradores.ExigirRecord(r, "OTFT", $"el formato de {game} no declara ese record")
        Return New OutfitDraft With {.Record = r,
                                     .FormID = formID, .IsOverride = False, .IsNew = True}
    End Function

    ''' <summary>Una edición de un atuendo que ya existe. Se trabaja sobre una COPIA: cancelar el
    ''' editor tiene que dejar el original como estaba.</summary>
    Public Shared Function Edicion(rec As PluginRecord, plugins As PluginManager) As OutfitDraft
        Borradores.ExigirPluginsNormalizados(plugins)
        If rec Is Nothing Then Return Nothing
        Dim abierto = Canon.CanonRecords.Otft(rec, plugins)
        If abierto Is Nothing Then Return Nothing
        Dim copia = abierto.Copia()
        Borradores.ExigirRecord(copia, "OTFT", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Return New OutfitDraft With {.Record = copia, .FormID = rec.Header.FormID,
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
            ' ⛔ Una prenda en 0 NO se agrega, y el porqué sale del FORMATO, no de una suposición mía:
            ' xEdit declara `wbArrayS(INAM, 'Items', wbFormIDCk('Item', [ARMO, LVLI]))` dentro de
            ' `wbRecord(OTFT, 'Outfit')` — wbDefinitionsFO4.pas:9359 (record en :9357) y
            ' wbDefinitionsTES5.pas:7443 (record en :7441) —, y esa lista de destinos NO incluye NULL.
            ' O sea que un INAM en 0 es ILEGAL para el formato, exactamente como el RNAM de NPC_.
            ' En una colección la ley no es «sacar el subrecord» —eso borraría el arreglo entero— sino
            ' NO AGREGAR LA ENTRADA.
            ' ⛔ CORRECCIÓN, y va escrita porque el porqué me salió mal DOS veces. Primero la saqué
            ' argumentando que «la lista se siembra del OTFT existente, así que un 0 sólo puede venir del
            ' archivo»: falso, `OutfitDraft.Edicion` tiene CERO llamadores. Después la repuse diciendo que
            ' «un 0 ya se descarta dos capas más arriba», y en ese momento tampoco era cierto: el 0 entraba
            ' por el carril de las prendas no mostrables. Hoy SÍ se descarta arriba —
            ' `OutfitPicker_Form.PlanDeSembrado` lo saca, y el caso C12 del gate lo mide—, pero
            ' esta guarda NO depende de eso: la sostiene la cita del formato, no el camino del llamador.
            If id = 0UI Then Continue For
            Dim e = Record.AgregarItems()
            If e Is Nothing Then Exit For
            e.Item = id
        Next
    End Sub

    '==============================================================================================
    ' Copiar y comparar
    '==============================================================================================

    Public Function Clone() As OutfitDraft
        ' ⛔ `Clone` es la TERCERA puerta: también CONSTRUYE un borrador, y `Copia()` puede devolver
        ' Nothing por los mismos tres caminos. En ARMA, ARMO y MSWP su resultado se registra en
        ' producción (`_openSnapshot`, que `RevertOrDiscardCurrentDraft` vuelve a meter en el mapa que
        ' consultan el render y el guardado). En OutfitDraft hoy no tiene llamadores — la guarda va
        ' igual, por la misma razón que está en las otras dos puertas: un borrador sin record no es un
        ' borrador, y el que agregue el primer llamador no tiene por qué acordarse.
        Dim copiaClone = Record?.Copia()
        Borradores.ExigirRecord(copiaClone, "OTFT", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Dim c As New OutfitDraft With {
            .Record = copiaClone,
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
