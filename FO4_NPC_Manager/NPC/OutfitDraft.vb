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
''' (<see cref="Realizaciones"/>).</para>
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
    ''' no lo seguiría — dejaría de ser reconocido como borrador sin que nada fallara al compilar.</para>
    ''' <para><c>Shared ReadOnly</c> porque <see cref="Borradores.FormIdAltoDeBorrador"/> es un campo y no
    ''' un <c>Const</c>: un <c>Const</c> no se puede inicializar desde un campo, y devolverlo a
    ''' <c>Const</c> exigiría escribir el literal a mano — que es justo lo que el párrafo de arriba
    ''' prohíbe. Ningún consumidor lo necesita constante (no hay <c>Select Case</c> ni atributo que lo
    ''' nombre): son todos comparaciones y argumentos.</para></summary>
    Public Shared ReadOnly PreviewDraftFormID As UInteger = Borradores.FormIdAltoDeBorrador Or &H7FFUI

    ''' <summary>El record que se está editando. Todo lo que el usuario cambia va acá.
    ''' <para>⛔ El setter es PRIVADO y re-alinea al REASIGNAR el record, que es como lo construyen las
    ''' tres fábricas. <b>No es una garantía universal</b>: una puerta futura que mutara <c>Items</c> SIN
    ''' reasignar <c>Record</c> no lo dispararía. Lo que sostiene el invariante de verdad son LAS PUERTAS
    ''' —<c>ReemplazarPiezas</c> y <c>ReemplazarPrendas</c>, que re-alinean al salir— más este setter para
    ''' las tres fábricas; el gate C39 las recorre a las cinco. Decirlo así y no «el setter lo garantiza»
    ''' importa: la frase de más es la que hace que el próximo no revise su puerta.</para>
    ''' <para>Medido: los ÚNICOS escritores son las tres fábricas de esta clase, así que volverlo privado
    ''' no le saca nada a nadie.</para></summary>
    Public Property Record As Canon.IOtft
        Get
            Return _record
        End Get
        Private Set(value As Canon.IOtft)
            _record = value
            SincronizarRealizaciones()
        End Set
    End Property
    Private _record As Canon.IOtft

    ''' <summary>Nuevo: identificador provisional. Edición: el real del record original.</summary>
    Public Property FormID As UInteger

    ''' <summary>Sólo para mostrar, no se guarda: qué prendas concretas salieron sorteadas para cada lista
    ''' por nivel del atuendo. <b>Alineada 1:1 con <c>Prendas()</c></b> —no con <c>Record.Items</c>, que
    ''' puede traer ceros que <c>Prendas()</c> filtra—: la entrada <c>i</c> es la realización de
    ''' <c>Prendas()(i)</c>, y <c>Nothing</c> significa «esta prenda no es una lista por nivel» o «todavía
    ''' no se sorteó». Es el índice con el que la leen TODOS los lectores; ver
    ''' <see cref="SincronizarRealizaciones"/>.
    '''
    ''' <para>Guarda los PICKS y no sólo los FormID: cada terminal viene con las keywords que heredó del
    ''' encadenado de LLKC en el camino, y ésas son las que deciden qué combinación OBTS aplica. Tirarlas
    ''' —el sorteo ya las traía— dejaba al borrador resolviendo sólo las combinaciones <c>Default</c>,
    ''' así que una prenda multi-variante se veía distinta antes y después de guardar.</para>
    '''
    ''' <para>⛔ <b>POR ÍNDICE DEL INAM, y antes era por FormID.</b> Con la llave puesta en el
    ''' identificador de la lista, un INAM que repitiera la MISMA lista dos veces —que un mod puede traer,
    ''' y el sembrado NO deduplica a propósito porque al leer manda el archivo— colapsaba las dos entradas
    ''' en una: cada fila sorteaba la suya, el volcado escribía la misma llave dos veces, la última pisaba,
    ''' y la lista mostraba un sorteo mientras el render dibujaba el otro.</para>
    '''
    ''' <para>⛔ <b>Y NO es la «posición» que el docstring viejo prohibía.</b> Son dos posiciones
    ''' distintas y ahí estaba el nudo:</para>
    ''' <list type="bullet">
    ''' <item>La de la GRILLA VIVA sí es peligrosa: el usuario reordena con ▲/▼ y una llave atada a la
    ''' fila le pegaría la realización de una prenda a otra.</item>
    ''' <item>La del INAM del BORRADOR no: el borrador se REESCRIBE ENTERO en cada cambio
    ''' —<c>OutfitPicker_Form.ArmarBorradorDeAtuendo</c> lo construye de cero desde las piezas ya
    ''' ordenadas, y el reorden sólo intercambia <c>Order</c> y vuelve a dibujar—, así que el INAM y esta
    ''' lista se escriben SIEMPRE en la misma pasada, desde la misma lista ordenada.</item></list>
    '''
    ''' <para>⛔ Esa pasada es <see cref="ReemplazarPiezas"/>, y es única A PROPÓSITO: la regla de que una
    ''' prenda en 0 no se agrega (ver <see cref="ReemplazarPrendas"/>) hace que el índice de la lista de
    ''' entrada NO sea el del INAM. Escribiendo las dos cosas en dos recorridos, un solo 0 las desalinea
    ''' y el render dibuja la realización de otra prenda. Por eso hay un solo recorrido y una sola regla
    ''' del cero.</para>
    ''' <para>⛔ Se expone como <c>IReadOnlyList</c> y la lista real es privada: lo que rompe el
    ''' invariante es el <c>Add</c>/<c>RemoveAt</c> desde afuera, y eso es justo lo que la interfaz de
    ''' sólo lectura no deja hacer. No se devuelve una COPIA porque copiar por lectura escondería que las
    ''' listas internas siguen siendo las mismas —daría una falsa sensación de aislamiento— y se paga en
    ''' cada repintado. Quien necesite escribir pasa por <see cref="PonerRealizacionEn"/>.</para></summary>
    Public ReadOnly Property Realizaciones As IReadOnlyList(Of List(Of OutfitArmorPick))
        Get
            Return _realizaciones
        End Get
    End Property
    Private ReadOnly _realizaciones As New List(Of List(Of OutfitArmorPick))

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
        ' ⛔ LA SALIDA TEMPRANA TAMBIÉN SINCRONIZA. `Nothing` se trata igual que la lista VACÍA —que es
        ' como esta puerta ya la trata: vaciar el INAM y seguir—, así que no se tira: se completa el
        ' gesto. El invariante tiene que sostenerse por TODA salida de TODA puerta; una que se escapa por
        ' arriba es exactamente el agujero que el resto de esta tanda vino a cerrar.
        If ids Is Nothing Then
            SincronizarRealizaciones()
            Return
        End If
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
        ' ⛔ El invariante `Realizaciones.Count = Items.Count` lo sostienen LAS DOS PUERTAS, no sólo
        ' `ReemplazarPiezas`. Ésta no sabe de sorteos, así que deja la lista en su largo con todo en
        ' Nothing: un lector por índice nunca se puede salir del rango ni leer la realización de otra
        ' prenda, entre por donde entre.
        SincronizarRealizaciones()
    End Sub

    ''' <summary>Deja el atuendo con exactamente estas prendas Y sus realizaciones, en UN SOLO recorrido.
    ''' <para>⛔ Es el único escritor que puede poner realizaciones, y por eso existe: la regla «una prenda
    ''' en 0 no se agrega» corre acá UNA vez para las dos listas. Con dos recorridos, un 0 en el medio
    ''' desalinea el INAM de <see cref="Realizaciones"/> y el render dibuja el sorteo de otra prenda.</para></summary>
    Public Sub ReemplazarPiezas(piezas As IEnumerable(Of (Fid As UInteger, Picks As List(Of OutfitArmorPick))))
        If Record Is Nothing Then Return
        While Record.Items.Count > 0
            If Not Record.QuitarItems(0) Then Exit While
        End While
        ' ⛔ LA MISMA RED QUE `ReemplazarPrendas`. Si `QuitarItems` falla quedan items VIEJOS adelante, y
        ' las prendas nuevas se agregan DESPUES: sin esto las realizaciones nuevas arrancarían en el
        ' índice 0 mientras sus prendas están en el `sobrantes`, o sea cada realización pegada a la
        ' prenda equivocada. Se cuenta lo que realmente quedó y se rellena por delante.
        ' ⛔ Se cuenta con `Prendas()`, el contrato de los lectores: un sobrante en 0 no lo ven, así que
        ' contarlo dejaría un hueco de más adelante y correría todo el resto.
        Dim sobrantes = Record.Prendas().Count
        Dim nuevas As New List(Of List(Of OutfitArmorPick))
        If piezas IsNot Nothing Then
            For Each p In piezas
                ' La MISMA regla del cero que `ReemplazarPrendas`, y acá decide las dos listas a la vez.
                If p.Fid = 0UI Then Continue For
                Dim e = Record.AgregarItems()
                If e Is Nothing Then Exit For
                e.Item = p.Fid
                nuevas.Add(ClonarPicks(p.Picks))
            Next
        End If
        _realizaciones.Clear()
        For i = 1 To sobrantes
            _realizaciones.Add(Nothing)
        Next
        _realizaciones.AddRange(nuevas)
        ' Última red: pase lo que pase arriba, el invariante se cumple al salir. Y el invariante es
        ' `Realizaciones.Count = Prendas().Count`, que es lo que indexan los lectores.
        Dim objetivo = Record.Prendas().Count
        While _realizaciones.Count > objetivo
            _realizaciones.RemoveAt(_realizaciones.Count - 1)
        End While
        While _realizaciones.Count < objetivo
            _realizaciones.Add(Nothing)
        End While
    End Sub

    ''' <summary>Copia PROFUNDA de una realización: picks nuevos con su propia lista de keywords.
    ''' <para>⛔ Medido, no supuesto: <c>OutfitResolver</c> MUTA los picks aguas abajo —
    ''' <c>merged(...).ContextKeywords.Add(kw)</c> en <c>:78-80</c> y <c>:106-107</c>, y
    ''' <c>pick.ContextKeywords.AddRange(...)</c> en <c>:250</c>—. Compartir el objeto entre el borrador
    ''' y quien se lo pasó (la fila del selector, o el clon) hace que mutar uno mueva al otro, y ésa es
    ''' exactamente la clase de carril compartido que esta tanda vino a cerrar. Copiar sólo la lista
    ''' externa no alcanzaba: lo que se muta es la lista de keywords ADENTRO del pick.</para></summary>
    Friend Shared Function ClonarPicks(picks As List(Of OutfitArmorPick)) As List(Of OutfitArmorPick)
        If picks Is Nothing Then Return Nothing
        Dim salida As New List(Of OutfitArmorPick)
        For Each pk In picks
            If pk Is Nothing Then Continue For
            Dim c As New OutfitArmorPick With {.ArmoFormID = pk.ArmoFormID}
            c.ContextKeywords.AddRange(pk.ContextKeywords)
            salida.Add(c)
        Next
        Return salida
    End Function

    ''' <summary>Deja <see cref="Realizaciones"/> con el largo de <c>Record.Prendas()</c> y TODO en Nothing.
    ''' <para>⛔ Contra <c>Prendas()</c> y NO contra <c>Record.Items</c>: <c>Prendas()</c> FILTRA los ceros
    ''' (<c>CanonInterpretacion:1114</c>) y es lo que indexan TODOS los lectores —<c>PicksSellados</c>
    ''' recorre <c>Me.Prendas()</c> y lee <c>RealizacionEn(i)</c> con ese mismo <c>i</c>—. Dimensionando
    ''' por <c>Items</c>, un solo 0 en el INAM corre todas las realizaciones a partir de ahí: cada prenda
    ''' queda con el sorteo de la siguiente, y la última lee fuera de rango. Hoy no se manifiesta porque
    ''' las dos puertas de escritura saltean el 0 al construir el INAM (la regla del cero de
    ''' <see cref="ReemplazarPrendas"/> y <see cref="ReemplazarPiezas"/>), así que para ellas los dos
    ''' largos coinciden; el que puede traer un 0 es un record que NO pasó por ellas —
    ''' <c>OutfitDraft.Edicion</c>, sembrado de un OTFT del archivo— y entra por el setter de
    ''' <see cref="Record"/>, que es justamente quien llama acá. El contrato de los lectores es
    ''' <c>Prendas()</c>: se dimensiona por el contrato, no por el almacenamiento.</para>
    ''' <para>⛔ LIMPIA SIEMPRE, también cuando el largo nuevo coincide con el viejo. Antes conservaba lo
    ''' que hubiera en las posiciones que seguían existiendo, y eso contradecía lo que su llamador
    ''' promete: <see cref="ReemplazarPrendas"/> reemplaza la lista ENTERA de prendas, así que la
    ''' realización de la posición <c>i</c> es la de OTRA prenda. Con dos listas del mismo largo la
    ''' conducta y la promesa divergían en silencio — no se manifestaba porque los llamadores de hoy la
    ''' usan sobre borradores recién creados, pero el que llegue mañana no tiene por qué saberlo. La
    ''' puerta que NO sabe de sorteos no puede arrastrar sorteos: el que los tiene es
    ''' <see cref="ReemplazarPiezas"/>.</para></summary>
    Private Sub SincronizarRealizaciones()
        Dim n = If(_record Is Nothing, 0, _record.Prendas().Count)
        _realizaciones.Clear()
        For i = 1 To n
            _realizaciones.Add(Nothing)
        Next
    End Sub

    ''' <summary>Reescribe en las realizaciones SELLADAS toda referencia a un borrador que se promovió.
    ''' Devuelve si tocó algo.
    ''' <para>⛔ Los picks NO están en el record —son el sorteo sellado— así que el censo de referencias
    ''' del record no los ve. Sin esto, promover un ARMO propio dejaba el atuendo dibujando el 0xFF ya
    ''' muerto hasta el próximo re-sorteo: el mismo defecto de la foto de ARMO, en el carril del sorteo.
    ''' Vive acá, con el dato, y es la ÚNICA puerta que los escribe.</para>
    ''' <para>⛔⛔ <b>ES LA SEGUNDA CASA DE UNA LEY QUE TIENE DOS</b>, y la otra es
    ''' <see cref="CensoDeReferencias.DeBorrador"/> — el censo CERRADO de los campos por los que un
    ''' borrador apunta a otro, que sale de la reflexión del record. Las dos se nombran mutuamente a
    ''' propósito: no se pueden fusionar (aquélla enumera campos DEL RECORD y un pick no es uno) y por eso
    ''' mismo es fácil tocar una creyendo que se cubrió todo.
    ''' <list type="bullet">
    ''' <item>El <b>remapeo de la promoción</b> (<see cref="Borradores.RemapearSupervivientes"/>) llama a
    ''' LAS DOS en la vuelta de los atuendos: <c>RemapearUno(d.Record, …)</c> para el record y
    ''' <c>d.RemapearPicks(…)</c> para esto. Si una de las dos devuelve <c>True</c>, el borrador se marca
    ''' tocado y se le re-publica la foto.</item>
    ''' <item>El <b>censo de referrers</b> (<c>Borradores.CensarReferrers</c>, el que decide si un
    ''' borrador se puede borrar) también las mira, por <see cref="ReferenciasDePicks"/>. Antes miraba
    ''' SÓLO el record y por eso un ARMO borrador al que únicamente lo apuntaba un pick sellado salía
    ''' «no lo referencia nadie» — y «Delete draft» lo borraba, dejando el pick colgado dibujando
    ''' vacío.</item></list></para></summary>
    Public Function RemapearPicks(realGlobal As Dictionary(Of UInteger, UInteger)) As Boolean
        If realGlobal Is Nothing OrElse realGlobal.Count = 0 Then Return False
        Dim tocado = False
        ' ⛔ POR LA MISMA ENUMERACION QUE EL CENSO DE REFERRERS. Escrito como un bucle propio acá, esto
        ' era la primera de dos listas que hay que mover juntas — y este archivo ya vio a esas dos
        ' derivar (los cuatro material swap del ARMA). Con `ReferenciasDePicks` el campo que se agregue
        ' lo ven los dos lados o ninguno.
        For Each r In ReferenciasDePicks()
            Dim mapped As UInteger
            If realGlobal.TryGetValue(r.Valor, mapped) Then
                r.Poner(mapped)
                tocado = True
            End If
        Next
        Return tocado
    End Function

    ''' <summary>EL CENSO de las referencias que viven FUERA del record: cada <c>ArmoFormID</c> de cada
    ''' realización sellada. Es la hermana de <see cref="CensoDeReferencias.DeBorrador"/> —aquélla enumera
    ''' los campos DEL RECORD, ésta el sorteo— y tiene los MISMOS DOS consumidores:
    ''' <see cref="RemapearPicks"/> (el remapeo de la promoción) y <c>Borradores.CensarReferrers</c> (el
    ''' censo que decide si un borrador se puede borrar).
    ''' <para>⛔ <b>UNA enumeración, DOS consumidores</b>, por la misma razón que allá: escritas como dos
    ''' recorridos, se separan. Ya pasó — el remapeo cubría los picks y el censo no, así que borrar un ARMO
    ''' borrador apuntado sólo por un pick estaba permitido y dejaba la realización apuntando a un FormID
    ''' muerto: la prenda se dibujaba VACÍA y nada lo explicaba.</para>
    ''' <para>⛔ Devuelve referencias VIVAS (el <c>Poner</c> escribe el pick real), así que sirve para
    ''' remapear. Quien sólo lee, lee <c>Valor</c> y no toca <c>Poner</c>.</para></summary>
    Friend Iterator Function ReferenciasDePicks() As IEnumerable(Of CensoDeReferencias.ReferenciaDeBorrador)
        For Each r In _realizaciones
            If r Is Nothing Then Continue For
            For Each pk In r
                If pk Is Nothing Then Continue For
                ' ⛔ Por `RefDe`, que pasa el elemento POR PARAMETRO: un lambda armado adentro del
                ' `For Each` captura la variable del bucle -que en VB es UNA sola para todas las
                ' vueltas- y todos los `Poner` terminarian escribiendo sobre el ULTIMO pick.
                Yield CensoDeReferencias.RefDe(pk, Function(x) x.ArmoFormID,
                                               Sub(x, v) x.ArmoFormID = v, "realización sorteada")
            Next
        Next
    End Function

    ''' <summary>La realización de la prenda <paramref name="indice"/>, o Nothing. Lectura por índice con
    ''' la guarda de rango en UN lugar, para que los lectores no la repitan.
    ''' <para>⛔ Fuera de rango <b>TIRA</b>, no devuelve Nothing. Con el invariante sostenido por el
    ''' setter y por las dos puertas de escritura, un índice inválido no es un dato posible: es un BUG
    ''' del llamador. Y tragarlo es lo que escondió el defecto de la lista por nivel repetida durante
    ''' tres saltos — el lector recibía Nothing y lo interpretaba como «todavía no se sorteó».</para>
    ''' <para>⛔ <b>Lo devuelto NO SE MUTA.</b> Es la lista VIVA del borrador, no una copia: mutarla
    ''' (Add/Clear sobre ella, o tocar las <c>ContextKeywords</c> de un pick) escribe en el borrador por
    ''' fuera de toda puerta. Las puertas de escritura son <see cref="ReemplazarPiezas"/> y
    ''' <see cref="PonerRealizacionEn"/>. Censado al escribir esta línea: los DOS consumidores de
    ''' producción sólo LEEN — <see cref="PicksSellados"/> hace <c>AddRange</c> (y es por donde pasa
    ''' <c>MainForm.ResolveDraftPicks</c>, que ya no la lee directo) y
    ''' <c>OutfitPicker_Form.LlaveDelBorrador</c> hace <c>Select</c>.</para></summary>
    Public Function RealizacionEn(indice As Integer) As List(Of OutfitArmorPick)
        ExigirIndice(indice)
        Return _realizaciones(indice)
    End Function

    ''' <summary>Fija la realización de la prenda <paramref name="indice"/>. Una de las DOS puertas de
    ''' escritura, junto con <see cref="ReemplazarPiezas"/>.
    ''' <para>⛔ Fuera de rango <b>TIRA</b>, igual que <see cref="RealizacionEn"/> y por la misma razón:
    ''' el invariante lo sostienen el setter de <see cref="Record"/> y las dos puertas, así que un índice
    ''' inválido no es un dato posible sino un BUG del llamador. El docstring decía «no-op fuera de
    ''' rango» y el cuerpo llama a <see cref="ExigirIndice"/> desde que se cerró el defecto de la lista
    ''' repetida: tragar el índice malo es lo que lo escondió tres saltos.</para>
    ''' <para>Y NO existe ningún «muestreo perezoso del resolvedor»: esa cita quedó de la versión que
    ''' muestreaba-y-escribía desde el camino de LECTURA, que es justo lo que la ley del sellado eliminó
    ''' (<see cref="PicksSellados"/> sólo lee y TIRA si falta el sello). El único llamador de producción
    ''' es <c>MainForm.RerollDraftLeveled</c>, que corre en el hilo de UI.</para></summary>
    Public Sub PonerRealizacionEn(indice As Integer, picks As List(Of OutfitArmorPick))
        ExigirIndice(indice)
        _realizaciones(indice) = picks
    End Sub

    ''' <summary>El índice tiene que caer dentro del INAM. Ver la nota de <see cref="RealizacionEn"/>:
    ''' con el invariante vivo esto es INALCANZABLE en producción, y por eso puede tirar.</summary>
    Private Sub ExigirIndice(indice As Integer)
        If indice >= 0 AndAlso indice < _realizaciones.Count Then Return
        Throw New ArgumentOutOfRangeException(
            NameOf(indice),
            $"La prenda {indice} no existe: el atuendo tiene {_realizaciones.Count}. " &
            "El invariante `Realizaciones.Count = Items.Count` lo sostienen el setter de `Record` y las " &
            "dos puertas de escritura, así que un índice fuera de rango es un error del LLAMADOR, no un " &
            "dato posible — devolver Nothing lo haría pasar por «todavía no se sorteó».")
    End Sub

    '==============================================================================================
    ' Copiar y comparar
    '==============================================================================================

    Public Function Clone() As OutfitDraft
        ' ⛔ `Clone` es la TERCERA puerta: también CONSTRUYE un borrador, y `Copia()` puede devolver
        ' Nothing por los mismos tres caminos. En ARMA, ARMO y MSWP su resultado se registra en
        ' producción (`_openSnapshot`, que `RevertOrDiscardCurrentDraft` vuelve a meter en el mapa que
        ' consultan el render y el guardado). Acá el llamador es la FOTO: `MainForm._fotosOtft` se
        ' construye con este `Clone` como clonador, porque el borrador de atuendo lleva estado FUERA del
        ' record —las realizaciones selladas— y copiar sólo el record lo fotografía por la mitad. O sea
        ' que esta guarda corre en CADA publicación, y por eso el que publica tiene que estar preparado
        ' para que TIRE: ver `FotosDeBorrador.Publicar` y el Try de `RequestPreviewAsync`.
        Dim copiaClone = Record?.Copia()
        Borradores.ExigirRecord(copiaClone, "OTFT", "la copia del record falló: árbol o contexto nulos, o la firma no corresponde a esta vista")
        Dim c As New OutfitDraft With {
            .Record = copiaClone,
            .FormID = FormID,
            .IsOverride = IsOverride,
            .IsNew = IsNew,
            .IsModified = IsModified
        }
        ' La copia mantiene el alineado por construcción: se copia POSICIÓN a POSICIÓN sobre un record
        ' que ya trae las mismas prendas, así que el índice significa lo mismo en las dos.
        ' ⛔ El clon copia POSICION a POSICION sobre un record que ya trae las mismas prendas (el setter
        ' ya lo dejó alineado con todo en Nothing), y los picks van en copia PROFUNDA: ver `ClonarPicks`.
        ' Se CONSTRUYE la lista, no se asigna por indice: asi el clon no depende de que el setter la
        ' haya dejado del largo justo. Cada puerta se sostiene sola — que es la ley de esta tanda.
        c._realizaciones.Clear()
        For Each r In _realizaciones
            c._realizaciones.Add(ClonarPicks(r))
        Next
        Return c
    End Function

    ''' <summary>Los picks SELLADOS del atuendo, en orden de equip. <b>LECTURA PURA</b>: no sortea, no
    ''' escribe, no toca el borrador.
    '''
    ''' <para>⛔ <b>EL PRODUCTOR SELLA, EL CONSUMIDOR NO MUESTREA.</b> Esto lo recorre el RENDER DE FONDO.
    ''' Cuando muestreaba al vuelo la realización que faltaba, ESCRIBÍA en el borrador desde un hilo
    ''' mientras el de UI lo editaba — y encima dibujaba un sorteo distinto del que la lista mostraba. El
    ''' sorteo ocurre en el hilo de UI: el volcado del selector, el registro, y el re-sorteo, que RE-SELLA
    ''' antes de disparar el render.</para>
    '''
    ''' <para>⛔ Una lista por nivel SIN SELLAR es un BUG y se dice fuerte. Y por eso <c>Nothing</c> y la
    ''' lista VACÍA dejan de ser lo mismo: el sampler
    ''' (<c>OutfitResolver.SampleItemWithKeywords</c>) arranca con <c>New List</c> y NUNCA devuelve
    ''' Nothing, así que <b>vacía = «sorteó y no salió nada»</b> (ChanceNone, y es pegajoso entre
    ''' repintados) y <b>Nothing = «nadie la selló»</b>.</para>
    '''
    ''' <para>⛔ Vive acá y no en la ventana principal —que es quien la usaba— porque es una ley sobre el
    ''' DATO: la sostiene el mismo invariante que el resto de esta clase, y acá el gate la puede CORRER.
    ''' El predicado «es lista por nivel» entra por parámetro porque depende del orden de carga, que el
    ''' borrador no conoce.</para></summary>
    Public Function PicksSellados(esLeveled As Func(Of UInteger, Boolean),
                                  Optional terminalDe As Func(Of UInteger, UInteger) = Nothing) As List(Of OutfitArmorPick)
        Dim picks As New List(Of OutfitArmorPick)
        Dim prendas = Me.Prendas()
        For i = 0 To prendas.Count - 1
            Dim itemFid = prendas(i)
            If esLeveled IsNot Nothing AndAlso esLeveled(itemFid) Then
                Dim realizada = RealizacionEn(i)
                If realizada Is Nothing Then
                    Throw New InvalidOperationException(
                        $"La prenda {i} del atuendo ({itemFid:X8}) es una lista por nivel SIN SELLAR. " &
                        "El sorteo se sella en el hilo de UI —volcado, registro o re-sorteo— y la lectura " &
                        "sólo LEE: muestrear acá escribiría en el borrador desde el hilo del render y " &
                        "dibujaría un sorteo distinto del que muestra la lista.")
                End If
                picks.AddRange(realizada)
            Else
                ' ⛔ LA MISMA LEY DE IDENTIDAD QUE EL CARRIL POR NIVEL: se emite el TERMINAL, no el hijo.
                ' El INAM sigue guardando lo que el usuario autoró —eso no se toca, es lo que el guardado
                ' emite y lo que trae un OTFT real—, pero TODA CONSULTA resuelve el terminal. Estaba
                ' aplicada a medias: la pieza por nivel salía resuelta y la DIRECTA salía cruda, así que
                ' el borrador trataba al hijo y a su terminal como DOS armaduras —torneo, contador,
                ' marcas, contexto OBTS y preview— y al guardar colapsaban en una. Un atuendo con una
                ' ARMO-con-plantilla directa MÁS una lista que termina en la misma cadena lo alcanza.
                ' El resolvedor entra INYECTADO porque la cadena TNAM necesita el orden de carga, que un
                ' borrador no conoce — igual que <paramref name="esLeveled"/>. Un resolvedor, dos
                ' carriles, cero copia de la ley.
                Dim idEfectiva = If(terminalDe Is Nothing, itemFid, terminalDe(itemFid))
                If idEfectiva = 0UI Then idEfectiva = itemFid
                ' Una prenda concreta no pasó por ningún LLKC: su contexto es VACÍO de verdad.
                picks.Add(New OutfitArmorPick With {.ArmoFormID = idEfectiva})
            End If
        Next
        Return picks
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
