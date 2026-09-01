''' <summary>Lo que vale para CUALQUIER borrador, sin importar de qué record sea.
''' <para>⛔ Esto vivía dentro de <c>OutfitDraft</c>, y no es de los atuendos: de los 12 llamadores de
''' <see cref="EsFormIdDeBorrador"/>, la mayoría no tienen nada que ver con un atuendo — el skin
''' (un ARMO) del guardado, una sustitución de materiales (MSWP), los editores de ARMA y ARMO, y cinco
''' sitios de la ventana principal—, más dos que componen desde <see cref="FormIdAltoDeBorrador"/>
''' (el asignador de la ventana principal y el centinela de previsualización de los atuendos).
''' Tener la ley ahí obligaba a que todos ellos nombraran
''' <c>OutfitDraft</c> para preguntar algo que no es de atuendos, y el día que otro tipo de borrador
''' necesitara su propia versión, la copia era el camino corto.</para></summary>
Public Module Borradores

    ''' <summary>Byte alto del identificador provisional de un borrador sin guardar.
    ''' <para>⛔ NO se redeclara: se REEXPORTA el de la librería. El valor decide qué FormID se
    ''' reindexa al guardar, y escrito en los dos lados era drift garantizado sobre bytes que el
    ''' usuario publica. Acá queda el nombre en castellano que usa la app; el valor es uno solo.</para>
    ''' <para>Al guardar se reescribe como <c>(índice propio del plugin) &lt;&lt; 24 | número de objeto</c>.</para></summary>
    Public Const FormIdAltoDeBorrador As UInteger = SaveNpcEspWriter.FormIdAltoDeBorrador

    ''' <summary>El identificador es el provisional de un borrador sin guardar. Deja que el render y los
    ''' resolvedores detecten que una referencia apunta a algo que todavía no existe en ningún archivo y
    ''' lo resuelvan desde el borrador.</summary>
    Public Function EsFormIdDeBorrador(formID As UInteger) As Boolean
        ' La MASCARA es la constante misma: el centinela ocupa exactamente el byte alto, asi que
        ' escribir &HFF000000 otra vez aca seria repetir el valor a dos lineas de donde se declara.
        Return (formID And FormIdAltoDeBorrador) = FormIdAltoDeBorrador
    End Function

    ''' <summary>La señal de que un borrador se quedó sin record.
    ''' <para>⛔ Tipo PROPIO y no <c>InvalidOperationException</c> pelada porque el gate lee la EXCEPCIÓN
    ''' como la señal de la ley: con el tipo de la BCL, cualquier <c>InvalidOperationException</c> que
    ''' tirara una fábrica por otro motivo —un <c>.First()</c> sobre vacío, un <c>ObjectDisposed</c>— se
    ''' leería como la señal correcta y el caso pasaría en verde con la ley borrada.</para></summary>
    Public Class BorradorSinRecordException
        Inherits InvalidOperationException
        Public Sub New(mensaje As String)
            MyBase.New(mensaje)
        End Sub
    End Class

    ''' <summary>Un borrador SIN record no es un borrador: tira.
    ''' <para>⛔ Va en LAS DOS PUERTAS, <c>Nuevo</c> y <c>Edicion</c>, porque las dos pueden quedarse sin
    ''' record y por caminos distintos:</para>
    ''' <list type="bullet">
    ''' <item><b>Nuevo</b>: la fábrica devuelve Nothing cuando el esquema del juego no declara el record.
    ''' Medido: el esquema de Skyrim declara CERO MSWP, así que <c>MswpDraft.Nuevo(…, Skyrim)</c> armaba
    ''' el borrador igual; la línea siguiente lo desreferenciaba (NRE) y la de más abajo lo REGISTRABA en
    ''' el mapa que después consultan el render y el guardado. Lo único que separaba al usuario de eso era
    ''' un <c>Visible = False</c> en otro archivo.</item>
    ''' <item><b>Edicion</b>: <c>Copia()</c> devuelve Nothing por TRES caminos (la vista no es un
    ''' <c>CanonRecordView</c>, su árbol o su contexto son nulos, o el <c>TryCast</c> final falla porque
    ''' <c>Reenvolver</c> desempata por la FIRMA y devolvió la clase de otro record). Los cinco
    ''' <c>Edicion</c> asignaban ese Nothing a <c>.Record</c> y devolvían un borrador NO-Nothing:
    ''' <c>MainForm.BuildMswpOverrideDraftFromReal</c> lo registra guardando sólo <c>d Is Nothing</c>.
    ''' Era el MISMO defecto por la otra puerta, y la premisa de que «Edicion avisa devolviendo Nothing
    ''' entero» era falsa: tenía un TERCER resultado.</item></list>
    ''' <para>Va como PRECONDICIÓN y no devolviendo Nothing porque las dos condiciones son errores de
    ''' LLAMADOR, no del dato: pedir un record que el juego no declara, o pedir una vista de una firma
    ''' que no corresponde. Un Nothing se puede ignorar —y de hecho se ignoraba—; esto no.</para>
    ''' <para>Genérica con <c>As Class</c> y no <c>As Object</c>: con <c>Object</c>, un argumento de tipo
    ''' valor se boáxea y NUNCA es Nothing, así que <c>ExigirRecord(d.FormID, …)</c> compilaba y devolvía
    ''' callado — la precondición desaparecía sin un solo aviso.</para></summary>
    Public Sub ExigirRecord(Of T As Class)(record As T, tipo As String, motivo As String)
        If record IsNot Nothing Then Return
        Throw New BorradorSinRecordException(
            $"No se puede armar un borrador de {tipo}: {motivo}. Un borrador sin record no es un " &
            "borrador — se registraría igual y lo consultarían el render y el guardado.")
    End Sub

    ''' <summary>Sin <c>PluginManager</c> NO HAY BORRADOR: tira.
    ''' <para>⛔ <c>NormalizarReferencias</c> se va sin hacer nada cuando <c>plugins</c> es Nothing, así que
    ''' el árbol quedaría con los FormID LOCALES del archivo fuente mientras todo lo de arriba —las
    ''' propiedades de referencia, el resolvedor del render y el reindexado del guardado— asume espacio de
    ''' orden de carga. Antes no mordía porque los editores volcaban valores ya globales sobre un record en
    ''' blanco; ahora el árbol crudo ES el borrador, y un ESP con las referencias sin reindexar apunta al
    ''' mod equivocado sin un solo aviso.</para>
    ''' <para>Vive acá porque estaba escrita CINCO veces, byte a byte, una por borrador: es una ley de
    ''' BYTES, y el sexto borrador que se la olvide falla en silencio sobre el archivo del usuario.</para></summary>
    Public Sub ExigirPluginsNormalizados(plugins As PluginManager)
        If plugins IsNot Nothing Then Return
        Throw New ArgumentNullException(NameOf(plugins),
            "Un árbol sin normalizar no es un borrador editable: sus referencias quedan en el espacio " &
            "LOCAL del archivo de origen y el guardado las reindexaría una segunda vez.")
    End Sub

    ''' <summary>Le da a un record COPIADO la identidad de un CLON: identificador nuevo, sin el del
    ''' editor de la fuente, y sobre todo NACIDO VIVO.
    ''' <para>⛔ Vive acá y no en cada borrador porque `Clon` existe hoy en DOS de los cinco (ARMA y
    ''' ARMO). El día que alguien agregue «clonar» a un MSWP, un atuendo o una lista por nivel, el camino
    ''' corto es copiar `Edicion` y pisar el FormID —que es exactamente lo que hacen las primeras cuatro
    ''' líneas de `Clon`— y ahí se hereda `RecordFlags` de la fuente. Con un record marcado `Deleted`,
    ''' el clon NACE BORRADO: el usuario lo ve en la lista, lo guarda, y el motor lo ignora sin un
    ''' aviso. Es el mismo defecto que ya se cazó una vez, y estaba gateado sólo para ARMA.</para>
    ''' <para>Toma <c>Object</c> porque las cinco vistas no comparten interfaz, pero lo único que se toca
    ''' es el <c>CanonRecordView</c>, que SÍ es un tipo concreto común: después del cast queda todo
    ''' tipado, sin enlace tardío.</para>
    ''' <para>⛔ Y NACIDO PROPIO: la marca de «vista efectiva» se apaga acá. Un clon es, por definición,
    ''' un record del usuario que SE VA A ESCRIBIR a su .esp, y un record con esa marca NO SE PUEDE
    ''' escribir nunca — el saver lo rechaza a propósito (<c>SaveNpcEspWriter.ArmoRecordEntry</c>: la
    ''' vista efectiva es un <c>CanonView</c> escribible e indistinguible de la cruda, y emitirla plegaría
    ''' los <c>MODL</c> del terminal contra la lista de masters del hijo). O sea que un clon que la
    ''' conserve es un borrador con la explosión garantizada al guardar. Va acá y no en el llamador por lo
    ''' mismo que `Deleted`: es qué ES un clon, no quién lo pidió. Hoy entra por una sola puerta —el clon
    ''' de un ARMO que hereda por <c>TNAM</c>, que nace de la vista MATERIALIZADA—; en las otras dos ya
    ''' viene apagada y esto es un no-op.</para></summary>
    Public Sub ReidentificarComoClon(record As Object, formIDNuevo As UInteger)
        Dim v = TryCast(record, Canon.CanonRecordView)
        ' ⛔ TIRA, no vuelve callado. El cast NO puede fallar por datos — `Copia()` devuelve siempre
        ' un CanonRecordView o Nothing —, así que llegar acá es un error de LLAMADOR: pasar el BORRADOR
        ' en vez de su `.Record` compila, y con un `Return` mudo el clon nacería borrado igual. O sea,
        ' exactamente el defecto que esta función vino a cerrar, por la puerta de al lado.
        If v Is Nothing OrElse v.Context Is Nothing Then
            Throw New ArgumentException(
                "ReidentificarComoClon necesita el RECORD (una vista canónica), no el borrador que lo " &
                "contiene ni Nothing: sin él no hay contexto que reidentificar y el clon nacería con la " &
                "identidad —y las banderas— de su fuente.", NameOf(record))
        End If
        ' El emisor reporta sus avisos con el FormID y el EditorId del CONTEXTO: un clon que arrastre
        ' los de la fuente los publica con la identidad equivocada.
        v.Context.FormID = formIDNuevo
        v.Context.EditorId = ""
        ' ⛔ `Deleted` (bit 5) NO se hereda. Un clon es un record nuevo; nace vivo.
        v.Context.RecordFlags = v.Context.RecordFlags And Not &H20UI
        ' ⛔ Y nace PROPIO: lo que se escribe al .esp es un record del usuario, no la vista que arma el
        ' motor con la herencia ya aplicada. Sin esto, el clon de un ARMO con TNAM se guarda... nunca:
        ' la ficha del saver lo rechaza.
        v.Context.EsVistaEfectiva = False
    End Sub

    ''' <summary>Reescribe en UN record toda referencia que apunte a un borrador promovido. Devuelve si
    ''' escribió algo — que es lo que decide si hay que re-publicar su foto.</summary>
    Private Function RemapearUno(record As Object,
                                 realGlobal As Dictionary(Of UInteger, UInteger)) As Boolean
        Dim tocado As Boolean = False
        For Each r In CensoDeReferencias.DeBorrador(record)
            If r.Valor = 0UI Then Continue For
            Dim mapped As UInteger
            If realGlobal.TryGetValue(r.Valor, mapped) Then
                r.Poner(mapped)
                tocado = True
            End If
        Next
        Return tocado
    End Function

    ''' <summary>Tras un guardado: reescribe en los borradores que SOBREVIVEN toda referencia al
    ''' identificador provisional de un borrador que se acaba de PROMOVER, re-publica la foto de los que
    ''' quedaron tocados, y dropea los promovidos (ya son records reales, los enumera el orden de carga).
    '''
    ''' <para><paramref name="realGlobal"/> va de (provisional del borrador) al FormID GLOBAL real que
    ''' quedó montado; lo arma el llamador, que es el único que sabe resolver archivo-local → global.</para>
    '''
    ''' <para>QUÉ campos se remapean: los que rinde <see cref="CensoDeReferencias.DeBorrador"/>, que es la MISMA
    ''' lista que consume el censo de referrers. Acá no hay una segunda enumeración.</para>
    '''
    ''' <para>⛔ <b>Vive acá y no en la ventana principal</b> porque es una ley que vale para CUALQUIER
    ''' borrador —igual que <see cref="EsFormIdDeBorrador"/> o <see cref="ReidentificarComoClon"/>— y
    ''' porque adentro de un formulario no hay testigo que la pueda correr: el gate tendría que armar un
    ''' <c>MainForm</c> entero (con su <c>InitializeComponent</c>) para tocarla, así que en los hechos
    ''' quedaba sin medir. Acá el gate LLAMA al sujeto.</para>
    '''
    ''' <para>⛔ <b>El remapeo ES UN PRODUCTOR DE FOTOS.</b> Reescribe el árbol VIVO del borrador que
    ''' sobrevive, y el render NO lee ese árbol: lee la foto
    ''' (<see cref="FotosDeBorrador(Of TVista).ParaRender"/>). Sin re-publicar, la foto se queda con la
    ''' referencia 0xFF que estas mismas líneas acaban de matar y la prenda superviviente se dibuja
    ''' VACÍA hasta el commit siguiente del editor. Corre en el hilo de UI —el mismo que muta—, así que
    ''' publicar acá cumple el contrato de la foto sin un solo cruce de hilos.</para></summary>
    Friend Sub RemapearSupervivientes(outfitDrafts As List(Of OutfitDraft),
                                      leveledListDrafts As List(Of LeveledListDraft),
                                      armoDrafts As List(Of ArmoDraft),
                                      armaDrafts As List(Of ArmaDraft),
                                      mswpDrafts As List(Of MswpDraft),
                                      realGlobal As Dictionary(Of UInteger, UInteger),
                                      fotosArmo As FotosDeBorrador(Of Canon.IArmo),
                                      fotosArma As FotosDeBorrador(Of Canon.IArma),
                                      fotosMswp As FotosDeBorrador(Of Canon.IMswp))
        If realGlobal Is Nothing OrElse realGlobal.Count = 0 Then Return

        ' ⛔ Se publica SÓLO el que se escribió, y sólo si SOBREVIVE. `Publicar` clona el árbol entero
        ' (walk recursivo): republicar la sesión completa en cada guardado pagaría ese walk por
        ' borradores que nadie tocó. Y al PROMOVIDO no se le publica porque más abajo se lo dropea y se
        ' le retira la foto. La condición no es un umbral: es "se escribió ⇒ se publica".
        For Each d In outfitDrafts
            If d IsNot Nothing Then RemapearUno(d.Record, realGlobal)
        Next
        For Each d In leveledListDrafts
            If d IsNot Nothing Then RemapearUno(d.Record, realGlobal)
        Next
        For Each d In armoDrafts
            If d Is Nothing Then Continue For
            If RemapearUno(d.Record, realGlobal) AndAlso
               Not realGlobal.ContainsKey(d.FormID) Then fotosArmo?.Publicar(d.FormID, d.Record)
        Next
        For Each d In armaDrafts
            If d Is Nothing Then Continue For
            If RemapearUno(d.Record, realGlobal) AndAlso
               Not realGlobal.ContainsKey(d.FormID) Then fotosArma?.Publicar(d.FormID, d.Record)
        Next
        For Each d In mswpDrafts
            If d Is Nothing Then Continue For
            ' Hoy el recorrido de un MSWP sale VACÍO —no declara campos de referencia— así que esto no
            ' publica nunca. Va igual, por lo mismo que `TemplateArmor` entra al censo: el día que el
            ' formato agregue uno, el camino ya está y nadie tiene que acordarse.
            If RemapearUno(d.Record, realGlobal) AndAlso
               Not realGlobal.ContainsKey(d.FormID) Then fotosMswp?.Publicar(d.FormID, d.Record)
        Next

        ' Drop the promoted drafts. The throwaway preview sentinel is never in the map, so it survives.
        outfitDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
        leveledListDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
        armoDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
        ' Idem: los borradores que acaban de pasar a ser records reales dejan de tener foto. Los tres
        ' juegos de fotos se retiran acá porque el identificador promovido es único entre las cinco
        ' clases (todas salen del MISMO contador), así que a lo sumo una de las tres lo tiene.
        For Each fidYaReal In realGlobal.Keys
            fotosArmo?.Retirar(fidYaReal)
            fotosArma?.Retirar(fidYaReal)
            fotosMswp?.Retirar(fidYaReal)
        Next
        armaDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
        mswpDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
    End Sub

End Module
