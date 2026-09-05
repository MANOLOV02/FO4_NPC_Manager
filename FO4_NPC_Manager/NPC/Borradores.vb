''' <summary>Lo que vale para CUALQUIER borrador, sin importar de qué record sea.
''' <para>⛔ Esto vivía dentro de <c>OutfitDraft</c>, y no es de los atuendos: de los 12 llamadores de
''' <see cref="EsFormIdDeBorrador"/>, la mayoría no tienen nada que ver con un atuendo — el skin
''' (un ARMO) del guardado, una sustitución de materiales (MSWP), los editores de ARMA y ARMO, el censo de
''' referencias del guardado, y cuatro sitios de la ventana principal—, más <b>seis</b> que componen desde
''' <see cref="FormIdAltoDeBorrador"/>: el asignador de la ventana principal, los dos contadores de
''' respaldo del guardado, y los TRES centinelas que enumera la nota de más abajo.
''' Tener la ley ahí obligaba a que todos ellos nombraran
''' <c>OutfitDraft</c> para preguntar algo que no es de atuendos, y el día que otro tipo de borrador
''' necesitara su propia versión, la copia era el camino corto.</para>
''' <para>⚠️ Los dos números salen de CONTAR el árbol, no de acordarse: decían «cinco sitios» donde hay
''' cuatro y «dos que componen» donde hay seis —y el propio párrafo de abajo ya listaba tres centinelas,
''' o sea que el archivo se contradecía solo. Quien agregue o saque un consumidor, que los vuelva a
''' contar.</para></summary>
Public Module Borradores

    ''' <summary>Byte alto del identificador provisional de un borrador sin guardar.
    ''' <para>⛔ NO se redeclara: se REEXPORTA el de la librería. El valor decide qué FormID se
    ''' reindexa al guardar, y escrito en los dos lados era drift garantizado sobre bytes que el
    ''' usuario publica. Acá queda el nombre en castellano que usa la app; el valor es uno solo.</para>
    ''' <para>⛔ <b><c>ReadOnly</c> y NO <c>Const</c>.</b> Como <c>Const</c>, «el valor es uno solo» sólo
    ''' era cierto recompilando los dos ensamblados juntos: el compilador hornea el literal de la
    ''' librería acá adentro, así que una DLL nueva con otro valor y la app sin recompilar quedaban
    ''' divergentes en silencio — el drift que esta línea existe para impedir, hecho por el compilador en
    ''' vez de por una persona. Con el campo se LEE en ejecución y la promesa se sostiene sola.
    ''' <see cref="SaveNpcEspWriter.FormIdAltoDeBorrador"/> es <c>ReadOnly</c> por lo mismo: hacen falta
    ''' las DOS: si cualquiera vuelve a ser <c>Const</c>, la cadena se re-hornea entera.</para>
    ''' <para>⛔ Y por eso los tres centinelas que se componen desde acá
    ''' —<c>OutfitDraft.PreviewDraftFormID</c>, <c>OutfitPicker_Form.OutfitContextFormID</c> y
    ''' <c>ArmaEditor_Form.PreviewArmoWrapperFormID</c>— también son <c>Shared ReadOnly</c>: un
    ''' <c>Const</c> no puede inicializarse desde un campo, y volverlos <c>Const</c> exigiría volver a
    ''' escribir el literal a mano, que es de donde se venía.</para>
    ''' <para>Al guardar se reescribe como <c>(índice propio del plugin) &lt;&lt; 24 | número de objeto</c>.</para></summary>
    Public ReadOnly FormIdAltoDeBorrador As UInteger = SaveNpcEspWriter.FormIdAltoDeBorrador

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

    '==============================================================================================
    ' ABRIR, ENSUCIAR y ABANDONAR: las tres leyes que un editor de borrador aplica sobre el REGISTRO.
    '
    ' ⛔ Viven acá, sin UI y sin `MainForm`, por lo mismo que `ExigirPluginsNormalizados`: estaban
    ' escritas adentro de tres formularios de WinForms —ARMO, ARMA y MSWP—, o sea que ningún testigo
    ' podía recorrerlas. Un `Form` no se instancia desde una consola, así que una ley que vive ahí
    ' adentro NO SE PUEDE MEDIR: es exactamente la forma en que un gate pasa en vacío.
    '==============================================================================================

    ''' <summary>Qué edita el editor cuando le piden abrir un FormID.</summary>
    Public Enum AccionAlAbrir
        ''' <summary>No hay borrador: se construye desde el record REAL.</summary>
        IrAlDisco = 0
        ''' <summary>Hay borrador y piden OVERRIDE: ese borrador ES el objetivo.</summary>
        Adoptar = 1
        ''' <summary>Hay borrador y piden COPIA: el clon sale del borrador, no del disco.</summary>
        ClonarDeBorrador = 2
    End Enum

    ''' <summary>PARA UN FORMID, EL BORRADOR MANDA SOBRE EL DISCO.
    ''' <para>⛔ Ésta es la ley que faltaba, y su ausencia era el defecto: los editores de ARMO y ARMA
    ''' abrían por <c>PluginManager.GetRecord</c>, que devuelve SÓLO lo que se cargó de archivo. Un
    ''' borrador registrado bajo ese mismo FormID no está ahí y no va a estar nunca antes del guardado,
    ''' así que reabrir una armadura ya editada la rearmaba desde el record vanilla y el trabajo del
    ''' usuario desaparecía sin un aviso. Y un ARMO NUEVO ni siquiera tiene record que leer: el editor
    ''' abría EN BLANCO, mudo.</para>
    ''' <para>El hecho es UNO —«hay borrador bajo este FormID»— y la entrada es UNA —«¿me lo piden como
    ''' override o como copia?»—, así que la decisión es UNA. Partirla en «¿hay borrador?» más dos
    ''' interpretaciones en los llamadores devolvía la ley a los formularios, que es de donde vino.</para>
    ''' <para>Un CLON pide identidad nueva, así que nunca adopta; pero sí COPIA del borrador, porque lo
    ''' que el usuario quiere duplicar es lo que está viendo. Decisión del usuario, no derivada.</para></summary>
    ''' <param name="borrador">Sale con el borrador encontrado, o Nothing cuando hay que ir al disco.</param>
    Public Function QueHacerAlAbrir(Of TD As Class)(fid As UInteger, asOverride As Boolean,
                                                    buscar As Func(Of UInteger, TD),
                                                    ByRef borrador As TD) As AccionAlAbrir
        ' ⛔ TIRA, no vuelve callado — la misma regla que `ReidentificarComoClon`. Sin buscador esta
        ' función devolvería `IrAlDisco` siempre, o sea que apagaría la ley EN SILENCIO y el editor
        ' volvería a leer del disco un FormID que tiene borrador: el defecto entero, otra vez, por la
        ' puerta de al lado. Un buscador en Nothing es error de LLAMADOR, no un dato posible.
        If buscar Is Nothing Then
            Throw New ArgumentNullException(NameOf(buscar),
                "Sin buscador de borradores no hay adopción ni clon: el editor leería del disco un " &
                "FormID que tiene borrador, que es exactamente el defecto que esta ley cierra.")
        End If
        borrador = buscar(fid)
        If borrador Is Nothing Then Return AccionAlAbrir.IrAlDisco
        Return If(asOverride, AccionAlAbrir.Adoptar, AccionAlAbrir.ClonarDeBorrador)
    End Function

    ''' <summary>¿El borrador quedó SUCIO después de volcarle los paneles? La pregunta se le hace a la
    ''' LÍNEA DE BASE PRISTINA —el record tal como está en el archivo—, no al estado con el que se abrió
    ''' el editor.
    ''' <para>⛔ Preguntarle al estado de apertura contesta «¿cambió algo desde que abrí?», que NO es lo
    ''' que decide si hay que emitir un override. Sobre un borrador ADOPTADO —uno que ya venía editado de
    ''' una sesión anterior— la respuesta era «no cambió nada», así que aceptarlo sin retocar lo dejaba
    ''' LIMPIO: <c>IsDirty</c> en False, el saver lo salteaba
    ''' (<c>NpcOverrideSaver</c>: <c>If d.IsOverride AndAlso Not d.IsDirty Then Continue For</c>) y la
    ''' edición del usuario no llegaba al .esp. El render, que lee la foto y no mira <c>IsDirty</c>, la
    ''' seguía dibujando: el editor mostraba una cosa y el archivo guardaba otra.</para>
    ''' <para>Contra la base, la propiedad que declara <c>ArmoDraft</c> —«lo detecta en las DOS
    ''' direcciones, así que deshacer una edición vuelve a dejar el borrador limpio»— pasa a valer también
    ''' ENTRE sesiones, que es donde antes no valía.</para>
    ''' <para>Sin base (un record que no resuelve, o una copia que falló) ⇒ SUCIO. Es la dirección segura
    ''' y es la que ya elegía el código: un override de más es ruido; un cambio perdido es daño.</para></summary>
    Public Function SucioContraLaBase(hayBase As Boolean, igualALaBase As Boolean) As Boolean
        Return Not hayBase OrElse Not igualALaBase
    End Function

    ''' <summary>Qué hacer con el REGISTRO cuando un editor se abandona sin aceptar.</summary>
    Public Enum AccionAlAbandonar
        ''' <summary>El registro bajo ese FormID lo puso ESTE editor ⇒ se da de baja.</summary>
        DarDeBaja = 0
        ''' <summary>Había otra cosa registrada (o hay snapshot de apertura) ⇒ se repone.</summary>
        Restaurar = 1
        ''' <summary>Adoptamos el objeto registrado y no hay snapshot ⇒ se deja como está.</summary>
        NoTocar = 2
    End Enum

    ''' <summary>LA LEY DE LA REVERSIÓN, y la pregunta correcta es «¿el registro que hay bajo este FormID
    ''' lo puse YO?», no «¿me pasaron el borrador por constructor?».
    ''' <para>⛔ La versión anterior preguntaba lo segundo, y por eso un OVERRIDE abierto por plantilla
    ''' —que llega con esa bandera en False y cuyo FormID ES el del record real— daba de BAJA por FormID
    ''' el borrador que el usuario había construido en una sesión anterior del mismo editor. Dar de baja
    ''' un registro ajeno es pérdida de trabajo, y encima silenciosa: la ventana se cierra normal.</para>
    ''' <para>Los tres casos, y son los únicos que hay:</para>
    ''' <list type="number">
    ''' <item><b>No había nada</b> cuando tomamos el FormID ⇒ el registro es nuestro ⇒ BAJA.</item>
    ''' <item><b>Había OTRO objeto</b> ⇒ nunca lo mutamos (mutamos el nuestro) ⇒ se REPONE tal cual. No se
    '''       clona: clonar sería una segunda ley, y una copia que además puede fallar.</item>
    ''' <item><b>Había EL MISMO objeto</b> (lo adoptamos) ⇒ sí lo mutamos ⇒ se repone el SNAPSHOT de
    '''       apertura; y si el snapshot no existe (su <c>Clone()</c> tiró) NO SE TOCA, porque el otro
    '''       error —darlo de baja— destruye trabajo y éste sólo deja una edición sin revertir.</item>
    ''' </list></summary>
    ''' <param name="registroPrevio">Lo que había registrado bajo el FormID EN EL MOMENTO en que el editor
    ''' lo tomó. Capturado ahí y NO releído al cerrar: al cerrar, el registro ya es el nuestro.</param>
    Public Function QueHacerAlAbandonar(Of TD As Class)(registroPrevio As TD, actual As TD,
                                                        snapshotDeApertura As TD,
                                                        ByRef aRestaurar As TD) As AccionAlAbandonar
        aRestaurar = Nothing
        If registroPrevio Is Nothing Then Return AccionAlAbandonar.DarDeBaja
        If Not ReferenceEquals(registroPrevio, actual) Then
            aRestaurar = registroPrevio
            Return AccionAlAbandonar.Restaurar
        End If
        If snapshotDeApertura Is Nothing Then Return AccionAlAbandonar.NoTocar
        aRestaurar = snapshotDeApertura
        Return AccionAlAbandonar.Restaurar
    End Function

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
                                      fotosMswp As FotosDeBorrador(Of Canon.IMswp),
                                      fotosOtft As FotosDeBorrador(Of OutfitDraft),
                                      fotosLvli As FotosDeBorrador(Of Canon.ILvli))
        If realGlobal Is Nothing OrElse realGlobal.Count = 0 Then Return

        ' ⛔ Se publica SÓLO el que se escribió, y sólo si SOBREVIVE. `Publicar` clona el árbol entero
        ' (walk recursivo): republicar la sesión completa en cada guardado pagaría ese walk por
        ' borradores que nadie tocó. Y al PROMOVIDO no se le publica porque más abajo se lo dropea y se
        ' le retira la foto. La condición no es un umbral: es "se escribió ⇒ se publica".
        ' ⛔ PRIMERO SE REMAPEA TODO, DESPUES SE DROPEA Y RECIEN AL FINAL SE PUBLICA.
        ' El orden importa y no es estetico: `Publicar` clona, y un clonador puede TIRAR. Publicando
        ' antes del drop, ese throw atraviesa el GUARDADO y deja los promovidos SIN dropear — o sea
        ' borradores que ya son records reales siguen en los mapas y sus fotos siguen vivas, ganandole
        ' al record que acaba de nacer. Dropeando primero, un aborto deja el estado consistente: nadie
        ' tiene foto de un muerto.
        Dim tocados As New HashSet(Of UInteger)
        For Each d In outfitDrafts
            If d Is Nothing Then Continue For
            Dim t1 = RemapearUno(d.Record, realGlobal)
            Dim t2 = d.RemapearPicks(realGlobal)
            If t1 OrElse t2 Then tocados.Add(d.FormID)
        Next
        For Each d In leveledListDrafts
            If d Is Nothing Then Continue For
            If RemapearUno(d.Record, realGlobal) Then tocados.Add(d.FormID)
        Next
        For Each d In armoDrafts
            If d Is Nothing Then Continue For
            If RemapearUno(d.Record, realGlobal) Then tocados.Add(d.FormID)
        Next
        For Each d In armaDrafts
            If d Is Nothing Then Continue For
            If RemapearUno(d.Record, realGlobal) Then tocados.Add(d.FormID)
        Next
        For Each d In mswpDrafts
            If d Is Nothing Then Continue For
            ' Hoy el recorrido de un MSWP sale VACIO -no declara campos de referencia-. Va igual, por lo
            ' mismo que `TemplateArmor` entra al censo: el dia que el formato agregue uno, ya esta.
            If RemapearUno(d.Record, realGlobal) Then tocados.Add(d.FormID)
        Next

        ' ⛔ Drop de los promovidos + retiro de sus fotos. El centinela de previsualizacion nunca esta en
        ' el mapa, asi que sobrevive.
        outfitDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
        leveledListDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
        armoDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
        armaDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
        mswpDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
        For Each fidYaReal In realGlobal.Keys
            fotosArmo?.Retirar(fidYaReal)
            fotosArma?.Retirar(fidYaReal)
            fotosMswp?.Retirar(fidYaReal)
            fotosOtft?.Retirar(fidYaReal)
            fotosLvli?.Retirar(fidYaReal)
        Next

        ' ⛔ Y recien ahora las fotos de los SUPERVIVIENTES que quedaron tocados. `Publicar` clona el arbol
        ' entero, asi que se publica solo lo que cambio: es "se escribio => se publica", no un umbral.
        For Each d In outfitDrafts
            If d IsNot Nothing AndAlso tocados.Contains(d.FormID) Then fotosOtft?.Publicar(d.FormID, d)
        Next
        For Each d In leveledListDrafts
            If d IsNot Nothing AndAlso tocados.Contains(d.FormID) Then fotosLvli?.Publicar(d.FormID, d.Record)
        Next
        For Each d In armoDrafts
            If d IsNot Nothing AndAlso tocados.Contains(d.FormID) Then fotosArmo?.Publicar(d.FormID, d.Record)
        Next
        For Each d In armaDrafts
            If d IsNot Nothing AndAlso tocados.Contains(d.FormID) Then fotosArma?.Publicar(d.FormID, d.Record)
        Next
        For Each d In mswpDrafts
            If d IsNot Nothing AndAlso tocados.Contains(d.FormID) Then fotosMswp?.Publicar(d.FormID, d.Record)
        Next
    End Sub

    ''' <summary>QUIÉN apunta a <paramref name="formID"/> desde los borradores en memoria. Vacío ⇒ nadie,
    ''' así que borrarlo no deja ninguna referencia colgada. Es lo que decide si «Delete draft» puede
    ''' proceder.
    '''
    ''' <para>⛔ <b>MIRA LAS DOS CASAS, y esa es la razón de que exista.</b> Un borrador apunta a otro por
    ''' DOS caminos y hay que recorrer los dos:
    ''' <list type="number">
    ''' <item>los campos DEL RECORD — <see cref="CensoDeReferencias.DeBorrador"/>;</item>
    ''' <item>las realizaciones SELLADAS de un atuendo — <see cref="OutfitDraft.ReferenciasDePicks"/>, que
    ''' viven fuera del record porque son el sorteo ya resuelto.</item></list>
    ''' Miraba sólo la primera, y el defecto era éste: un ARMO borrador al que únicamente lo apuntaba un
    ''' pick sellado se reportaba «no lo referencia nadie», «Delete draft» lo borraba, y la realización
    ''' quedaba apuntando a un FormID muerto — la prenda se dibujaba VACÍA y nada lo explicaba. Es la misma
    ''' asimetría que el remapeo de la promoción ya cubría (<see cref="RemapearSupervivientes"/> llama a las
    ''' dos): el remapeo veía los picks y el censo no.</para>
    '''
    ''' <para>⛔ <b>Vive acá y no en la ventana principal</b>, por lo mismo que
    ''' <see cref="RemapearSupervivientes"/>: adentro de un formulario no hay testigo que la pueda correr
    ''' —el gate tendría que construir un <c>MainForm</c> entero, con su <c>InitializeComponent</c>— así que
    ''' en los hechos quedaba sin medir, y de hecho el defecto de arriba vivió ahí sin que ningún caso lo
    ''' viera. Acá el gate LLAMA al sujeto.</para>
    '''
    ''' <para>Las asignaciones POR NPC (el skin del WNAM o el atuendo por defecto de un preset aplicado) NO
    ''' entran acá: no son referencias de un borrador a otro sino de un NPC a un borrador, necesitan el
    ''' catálogo de presets y el resolvedor de nombres de la ventana, y se agregan del lado del llamador.
    ''' La frontera es la CLASE de referencia, no dónde es cómodo escribirla.</para></summary>
    Friend Function CensarReferrers(formID As UInteger,
                                    outfitDrafts As List(Of OutfitDraft),
                                    leveledListDrafts As List(Of LeveledListDraft),
                                    armoDrafts As List(Of ArmoDraft),
                                    armaDrafts As List(Of ArmaDraft),
                                    mswpDrafts As List(Of MswpDraft)) As List(Of String)
        Dim refs As New List(Of String)
        If formID = 0UI Then Return refs

        ' Distinct: un mismo borrador puede apuntar al mismo destino por DOS campos del mismo nombre (los
        ' dos material swap, dos addons iguales) y el usuario no necesita la linea repetida — necesita
        ' saber QUE lo referencia.
        Dim censar =
            Sub(clase As String, edid As String, refsDe As IEnumerable(Of CensoDeReferencias.ReferenciaDeBorrador))
                For Each que In refsDe.Where(Function(r) r.Valor = formID).Select(Function(r) r.Que).Distinct()
                    refs.Add($"{clase} draft '{edid}' ({que})")
                Next
            End Sub

        If armoDrafts IsNot Nothing Then
            For Each d In armoDrafts
                If d Is Nothing Then Continue For
                censar("ARMO", d.Record.EditorID, CensoDeReferencias.DeBorrador(d.Record))
            Next
        End If
        If armaDrafts IsNot Nothing Then
            For Each d In armaDrafts
                If d Is Nothing Then Continue For
                censar("ARMA", d.Record.EditorID, CensoDeReferencias.DeBorrador(d.Record))
            Next
        End If
        If outfitDrafts IsNot Nothing Then
            For Each d In outfitDrafts
                If d Is Nothing OrElse d.FormID = OutfitDraft.PreviewDraftFormID Then Continue For
                censar("Outfit", d.Record.EditorID, CensoDeReferencias.DeBorrador(d.Record))
                ' ⛔ LA SEGUNDA CASA. El sorteo sellado no esta en el record y el censo del record no lo
                ' ve: sin esta linea, un ARMO borrador apuntado SOLO por un pick sale «no lo referencia
                ' nadie» y se lo puede borrar.
                censar("Outfit", d.Record.EditorID, d.ReferenciasDePicks())
            Next
        End If
        If leveledListDrafts IsNot Nothing Then
            For Each d In leveledListDrafts
                If d Is Nothing Then Continue For
                censar("Leveled-list", d.Record.EditorID, CensoDeReferencias.DeBorrador(d.Record))
            Next
        End If
        If mswpDrafts IsNot Nothing Then
            For Each d In mswpDrafts
                If d Is Nothing Then Continue For
                ' Hoy no rinde nada (un MSWP no declara campos de referencia), pero se recorre igual: si el
                ' dia de manana declara uno, el censo ya lo ve. Ver `CensoDeReferencias.DeBorrador`.
                censar("MSWP", d.Record.EditorID, CensoDeReferencias.DeBorrador(d.Record))
            Next
        End If
        Return refs
    End Function

End Module
