Imports System.Windows.Forms

''' <summary>EL CAMINO DE BAJA de las OCHO clases de borrador, en UNA sola sede.
'''
''' <para>⛔ <b>POR QUÉ EXISTE: la misma ley estaba escrita CUATRO veces.</b> Comparadas literalmente,
''' <c>BorradoDeHeadParts</c> (HDPT/TXST/FLST), <c>BorradoDeMswp</c> (MSWP, con la firma INVERTIDA), y
''' las copias inline de <c>ArmoEditor_Form.QuitarSegunSuClase</c>, <c>ArmaEditor_Form</c> y
''' <c>OutfitPicker_Form.OnDeleteOrRevertOutfit</c> eran gemelas con el nombre de la clase cambiado. Y
''' a la novena clase —LVLI— nunca le llegó: <c>UnregisterLeveledListDraft</c> tenía UN solo llamador,
''' el cierre del selector de atuendos, así que una lista propia sucia que el usuario quisiera sacar a
''' mitad de sesión no tenía ningún gesto que la bajara.</para>
'''
''' <para>⛔⛔ <b>SE DESPACHA POR FormID, NO POR FIRMA.</b> Las cuatro sedes que esta absorbe ya lo
''' hacían así (<c>BorradoDeHeadParts</c>, hoy borrado, buscaba en los tres registros y usaba la firma
''' SÓLO para
''' nombrar la clase en la rama «ya guardado»). Los ocho registros son DISJUNTOS por FormID, así que el
''' lookup no puede equivocarse de clase; la firma sí puede: se compara con <c>Ordinal</c> en el resto
''' del árbol, y un <c>"Mswp"</c> no falla — DESAPARECE. Despachar por FormID es la mudanza fiel;
''' despachar por firma habría sido el cambio.</para>
'''
''' <para>⛔⛔ <b>LA DECISIÓN ES PURA Y VIVE SEPARADA DE LOS DIÁLOGOS</b>, por lo mismo que
''' <c>OutfitPicker_Form.PlanDeCierreDeListas</c>: «dentro de <c>FormClosing</c> el gate no la puede
''' correr, y un caso que mira el TEXTO del manejador mide la letra en vez de la conducta». Acá el
''' motivo es todavía más concreto: <see cref="BorrarORevertir"/> abre <c>MessageBox</c>, y un modal
''' CUELGA a un gate. Con <see cref="Planear"/> y <see cref="Ejecutar"/> el testigo mide las ocho
''' clases y las cuatro ramas sin pump de mensajes.</para>
'''
''' <para>⛔ <b>LAS CINCO SEDES VIEJAS YA NO ESTÁN.</b> <c>NPC/BorradoDeHeadParts.vb</c> y
''' <c>NPC/BorradoDeMswp.vb</c> se BORRARON en esta misma ola una vez medidos sus llamadores en CERO,
''' y las tres copias inline (<c>ArmoEditor_Form</c>, <c>ArmaEditor_Form</c>,
''' <c>OutfitPicker_Form.OnDeleteOrRevertOutfit</c>) delegan acá. Dejarlas «por las dudas» era la
''' forma de tener seis sedes en vez de cinco: la del atuendo todavía BLOQUEABA lo referenciado y
''' habría revivido la conducta doble que D-3 sacó. Están en git si hace falta leerlas.</para>
'''
''' <para>⛔ <b>LAS DESVIACIONES DE TEXTO, DECLARADAS.</b> «Transcritos» no es «idénticos», y estas
''' cuatro se apartan de lo que decía alguna de las sedes viejas. Van escritas para que un diff no
''' las lea como un descuido:</para>
''' <list type="number">
''' <item><b>El cartel de lo referenciado cambió de SENTIDO</b>, no de redacción: era «Can't delete
''' — still referenced by:» con <c>OK</c>, y hoy es la misma confirmación de borrado con la lista y
''' un párrafo de consecuencia. Es D-3, decidido por el usuario.</item>
''' <item><b>Todo cartel destructivo llega con <c>MessageBoxDefaultButton.Button2</c></b>. Ninguna
''' de las cinco sedes viejas lo pasaba en estas ramas: llegaban con «Sí» bajo el Enter.</item>
''' <item><b>El atuendo ya guardado dice «Revert saved outfit …»</b> y no «Remove saved outfit …
''' from your plugin on the next Save?»: la rama <c>QuitarGuardado</c> es UNA para las ocho clases
''' y elige el verbo por el EDID (<c>npcm_</c> ⇒ Delete, si no Revert). La copia del selector de
''' atuendos decía «Remove» para los dos casos.</item>
''' <item><b>El aviso de COMPARTIDO ahora también sale al borrar un borrador NUEVO</b>, porque las
''' dos ramas comparten el armado del texto. Antes salía sólo al revertir.</item>
''' <item><b>El título de la reversión es <c>$"Revert {clase}"</c></b> y no el <c>"Revert to original"</c>
''' que traían las sedes de head parts: con ocho clases en una sede, un título que no nombra la clase
''' deja al usuario sin saber qué está revirtiendo.</item>
''' <item><b>El MSWP perdió su redacción propia</b> («…to this swap»): usa la de la rama, con su aviso
''' de compartido. Es la contracara de «un texto por rama y no uno por clase».</item>
''' <item><b>F1 se arregló en la SEDE y no en los dos llamadores</b> que el plan nombraba: el enable
''' delega en <c>_fidsDeLasFilas</c>, así que el botón visible-y-muerto lo reproduce CUALQUIER llamador
''' con lista vacía. Arreglarlo en dos sitios dejaba la ley en «acordarse».</item></list>
'''
''' <para>⛔ <b>LOS TEXTOS VAN POR RAMA Y TRANSCRITOS.</b> «Una ley, una sede» NO obliga a «un
''' string»: el aviso de que un head part es COMPARTIDO, o que un material swap le pega a toda armadura
''' del orden de carga, son datos de esa clase y se pierden si se unifica la redacción. Cada rama trae
''' el suyo, copiado literal de la sede que lo tenía.</para></summary>
Friend Module BorradoDeBorradores

    '==============================================================================================
    ' D0 · EL CONTRATO CON QUIEN ABRE EL SELECTOR
    '==============================================================================================

    ''' <summary>Lo que la baja necesita saber de QUIEN abre el selector para quedar completa.
    '''
    ''' <para>⛔⛔ <b>EXISTE PORQUE UN MANEJADOR SIN LLAMADOR NO TIENE DÓNDE PONER LA PRE NI LA
    ''' POSTCONDICIÓN.</b> El selector arma su propio camino de baja (ver <c>FormIdPicker_Form</c>), y si
    ''' ese camino fuera un lambda pelado que sólo llama a <see cref="BorrarORevertir"/>, cada sitio
    ''' cableado así sería la variante DESPROTEGIDA de la misma ley. Medido sobre dos casos reales:</para>
    ''' <list type="number">
    ''' <item>el editor de TXST toma el borrador y lo ABANDONA al cerrar
    ''' (<c>TextureSetEditor_Form:108</c>, <c>:390</c>, <c>:396</c>): sin nadie que SUELTE, el
    ''' <c>Abandonar</c> REPONE el borrador que el usuario acaba de borrar — el bug que el usuario
    ''' reportó como «si hago delete/revert se duplica»;</item>
    ''' <item>el editor de ARMO re-toma el objetivo tras la baja (<c>ArmoEditor_Form:524</c>): sin eso
    ''' queda sobre un FormID que <c>RevertAppOverrideInMemory</c> acaba de sacar de <c>AllRecords</c>, y
    ''' el guardado ENTERO tira.</item></list>
    ''' <para>Con el contrato, las dos cosas son del dueño y el selector no puede olvidarse de
    ''' pedirlas.</para></summary>
    Friend Interface IDuenoDeBorradores

        ''' <summary>El FormID que este dueño tiene TOMADO, o 0. Alimenta la PRECONDICIÓN.
        ''' <para>⛔ Es la mitad PROPIA de la pregunta «¿está abierto en un editor?». La otra mitad es
        ''' <c>Borradores.EstaTomado</c>, que sabe de TODAS las ventanas. Las dos se consultan en
        ''' <see cref="Planear"/> y dan resultados OPUESTOS a propósito: tomado por OTRO se bloquea;
        ''' tomado por MÍ se permite, porque yo sé recuperarme (<see cref="TrasLaBaja"/>).</para></summary>
        Function FormIdTomado() As UInteger

        ''' <summary>Referencias que este dueño tiene EN BUFFER y todavía no volcó al record registrado.
        ''' Vacío si no tiene ninguna.
        ''' <para>⛔ Existe por <c>ArmoAddonEditor_Form._armaFormID</c>, que NO sale de la ventana hasta
        ''' el OK (<c>:126</c> lo escribe, <c>:130-133</c> lo publica): mientras el modal está abierto, el
        ''' censo de referrers no lo ve, así que «Delete / Revert…» diría que a ese ARMA no lo apunta
        ''' nadie y lo borraría — y al aceptar, la fila del addon quedaría en un 0xFF muerto que el
        ''' remapeo no puede resolver.</para>
        ''' <para>⚠️ NO es para los buffers que SÍ llegan al record: los de <c>ArmoEditor</c> se vuelcan
        ''' por el debounce de 400 ms (<c>:175</c> → <c>:2238</c> → <c>:2271</c>), así que el censo los
        ''' ve y no hacen falta acá.</para></summary>
        Function ReferenciasNoVolcadas(formID As UInteger) As IEnumerable(Of String)

        ''' <summary>POSTCONDICIÓN: la baja OCURRIÓ y alcanzó a <paramref name="formID"/>. Acá va
        ''' <c>TomaDeBorrador.Soltar</c>, la re-toma del objetivo y la limpieza de los libros de sesión.
        ''' <para>⛔⛔ SE LLAMA SIEMPRE QUE LA BAJA TUVO ÉXITO, no sólo cuando el FormID es el que el
        ''' dueño tenía tomado. <c>OutfitPicker_Form._lvliSnapshotDeApertura</c> guarda FormID que el
        ''' dueño NUNCA tomó (<c>SnapshotAntesDeMutar:915-917</c> no es una toma), y si no se purgan, el
        ''' cierre sin OK RESTAURA la lista recién borrada (<c>MainForm.RestaurarBorradorDeLista</c>).
        ''' Condicionar esta llamada deja esa resurrección viva.</para></summary>
        Sub TrasLaBaja(formID As UInteger)

    End Interface

    '==============================================================================================
    ' D1 · LA DECISIÓN, PURA
    '==============================================================================================

    ''' <summary>Qué corresponde hacer con la fila. Lo decide <see cref="Planear"/> y lo ejecuta
    ''' <see cref="Ejecutar"/>; en el medio, la UI pregunta.</summary>
    Friend Enum Gesto
        ''' <summary>No hay nada que bajar: fila vacía, FormID 0, o una clase que esta sede no conoce.</summary>
        Nada = 0
        ''' <summary>Borrador OVERRIDE: se revierte y gana el record original.</summary>
        RevertirOverride
        ''' <summary>Borrador NUEVO sin referrers: se borra.</summary>
        BorrarNuevo
        ''' <summary>Borrador NUEVO que algo todavía apunta: <b>se borra igual</b>, pero listando
        ''' quién lo apunta y con «No» bajo el Enter.
        ''' <para>⛔⛔ <b>ANTES ESTO BLOQUEABA</b>, y el usuario lo unificó con la rama de los records
        ''' ya guardados —que SIEMPRE dejó borrar— en la versión PERMISIVA (orden del 22-sep: «borrador
        ''' también debe dejar borrar, o sea unificamos pero en la versión que permite borrar igual»).
        ''' La conducta doble era: un borrador referenciado te frenaba y no te daba salida; el mismo
        ''' record una vez guardado te avisaba y el «Sí» lo bajaba. Dos leyes para un gesto.</para>
        ''' <para>⛔ <b>LO QUE LO HACE DEFENDIBLE ES LA RED DEL GUARDADO</b>, no el optimismo:
        ''' <c>NpcOverrideSaver.ExigirReferenciasSinColgar</c> RECHAZA el guardado entero si algo
        ''' apunta a un provisional que ya no tiene borrador, y lo nombra (record + campo). Así que la
        ''' peor consecuencia de decir que sí es un guardado rebotado con el motivo escrito, nunca un
        ''' <c>.esp</c> con un 0xFF adentro.</para>
        ''' <para>⚠️⚠️ Esa red recorre las DOS casas de referencia —los campos del record y los picks
        ''' SELLADOS del atuendo— recién desde esta ola. Sin la segunda, borrar un ARMO al que sólo lo
        ''' apunta un pick salía limpio por la red y la prenda se dibujaba VACÍA. Si alguien vuelve a
        ''' dejar la red mirando una sola casa, esta rama deja de ser defendible.</para>
        ''' <para>⚠️⚠️ Y LA RED CUBRE DOS DE LAS <b>TRES</b> CASAS. La tercera —los campos del PRESET (head
        ''' texture, sleep outfit, skin, head parts)— NO la mira: borrar un borrador que <b>sólo</b> apunta
        ''' el preset pasa la red, y el guardado muere después en <c>SaveNpcEspWriter</c> con el FormID
        ''' <b>PELADO</b>, sin nombrar NPC ni campo. El <c>.esp</c> NO se corrompe —el tiro es ANTES de
        ''' escribir y <c>GuardarConCopia</c> restaura—, así que esto no es bytes: lo que se pierde es el
        ''' MENSAJE, que es justo lo que <c>ExigirReferenciasSinColgar</c> existe para dar. <b>Decisión del
        ''' usuario pendiente</b> (22-sep). Queda escrito acá porque el párrafo de arriba promete «un
        ''' guardado rebotado CON EL MOTIVO ESCRITO» y para esa casa es FALSO — y un comentario que declara
        ''' cumplida una ley que no se cumple es exactamente lo que hizo que un defecto de bytes se diera
        ''' por cubierto en esta misma ola.</para></summary>
        QuitarConReferencias
        ''' <summary>El borrador está abierto en OTRA ventana: no se toca desde acá.</summary>
        BloqueadoPorEdicionAbierta
        ''' <summary>Record YA GUARDADO en el plugin: se marca para quitar en el próximo guardado.</summary>
        QuitarGuardado
    End Enum

    ''' <summary>El plan de baja: el gesto más EXACTAMENTE el texto que la UI tiene que mostrar. No
    ''' toca nada.</summary>
    Friend NotInheritable Class PlanDeBaja
        Public Property Gesto As Gesto = Gesto.Nada
        ''' <summary>Nombre legible de la clase ("head part", "material swap", "leveled list"…).</summary>
        Public Property Clase As String = "record"
        Public Property Titulo As String = ""
        Public Property Mensaje As String = ""
        Public Property Botones As MessageBoxButtons = MessageBoxButtons.YesNo
        Public Property Icono As MessageBoxIcon = MessageBoxIcon.Warning
        ''' <summary>⛔ <b>«No» POR DEFECTO en todo gesto destructivo e irreversible.</b> La ley ya
        ''' estaba escrita en esta casa —el aviso de «Start from an existing list…» del selector de
        ''' atuendos llegaba con <c>Button2</c>— y sin ponerla acá se perdía al mudar los carteles.
        ''' Un borrado sin deshacer que llega con «Sí» bajo el Enter es peor que el bloqueo que D-3
        ''' está sacando: cambia «no te dejo» por «te lo borré antes de que leyeras».</summary>
        Public Property BotonPorDefecto As MessageBoxDefaultButton = MessageBoxDefaultButton.Button2
        ''' <summary>Quién lo apunta. Lleva filas en <see cref="Gesto.QuitarConReferencias"/> y, como
        ''' aviso, en <see cref="Gesto.QuitarGuardado"/>. En las dos VA DENTRO DEL CARTEL: desde D-3
        ''' la lista no es el motivo del bloqueo sino lo que el usuario necesita para decidir.</summary>
        Public ReadOnly Property Referrers As New List(Of String)
        ''' <summary>El FormID sobre el que se decidió. 0 ⇒ no hay sujeto.
        ''' <para>⛔⛔ SE LLAMABA <c>FormID</c> Y HUBO QUE RENOMBRARLO. `ContextoPropioGate` deriva del EMISOR
        ''' el conjunto de propiedades que son REFERENCIAS de record —295— y exige que ninguna se escriba
        ''' fuera de la ley (`PonerReferencia`). `FormID` está en ese conjunto, así que la asignación de `Planear`
        ''' de <c>Planear</c> se leía como una escritura suelta sobre una referencia de record y el gate salió
        ''' ROJO en LOS DOS JUEGOS: «escrituras sueltas: NPC\BorradoDeBorradores.vb:284».
        ''' <para>⛔ Y el gate tenía razón en el fondo, no sólo en la forma: esto NO es la referencia de un
        ''' record —es el sujeto sobre el que este plan decidió— y llamarlo igual que los 295 campos que sí lo
        ''' son lo vuelve indistinguible para cualquiera que lea, no sólo para el gate. La salida NO era
        ''' eximir al archivo: una exención necesita su propio control, y acá no hay nada que eximir.</para></summary>
        Public Property Objetivo As UInteger
        ''' <summary>La baja del registro que corresponde a la clase resuelta. Nothing para
        ''' <see cref="Gesto.QuitarGuardado"/> y para <see cref="Gesto.Nada"/>.</summary>
        Public Property Bajar As Action(Of UInteger)
        ''' <summary>True cuando el plan requiere confirmación del usuario. El único gesto que ya
        ''' sólo INFORMA es <see cref="Gesto.BloqueadoPorEdicionAbierta"/> — el otro bloqueo se
        ''' convirtió en confirmación con D-3.</summary>
        Public ReadOnly Property PideConfirmacion As Boolean
            Get
                Return Gesto = Gesto.RevertirOverride OrElse Gesto = Gesto.BorrarNuevo OrElse
                       Gesto = Gesto.QuitarGuardado OrElse Gesto = Gesto.QuitarConReferencias
            End Get
        End Property
    End Class

    ''' <summary>La clase que el FormID resuelve, con todo lo que la baja necesita de ella.</summary>
    Private NotInheritable Class ClaseResuelta
        Public Property Nombre As String
        Public Property Edid As String
        Public Property EsNuevo As Boolean
        Public Property Bajar As Action(Of UInteger)
        ''' <summary>El aviso de que el record es COMPARTIDO, o "" si esa clase no lo tiene hoy.</summary>
        Public Property Compartido As String = ""
    End Class

    ''' <summary>El borrador VIVO bajo <paramref name="fid"/>, buscado en los OCHO registros.
    ''' <para>⛔⛔ EL NOMBRE Y EL AVISO DE COMPARTIDO SALEN DE <see cref="NombreDeLaClase"/> y
    ''' <see cref="CompartidoDeLaFirma"/>, y acá estaban ESCRITOS OTRA VEZ, literales. Es la casa
    ''' cuyo doc dice «POR QUÉ EXISTE: la misma ley estaba escrita CUATRO veces», naciendo con la
    ''' duplicación adentro. Y no es estética: la rama de borrador y la rama de «ya guardado» leían
    ''' CADA UNA su copia, así que cambiar «A head part is SHARED…» en una sola hacía que el mismo
    ''' record dijera dos cosas distintas según estuviera guardado o no.</para>
    ''' <para>⛔ Por FormID y no por firma: ver el ⛔ de la clase. Los registros son disjuntos, así que
    ''' el primero que conteste es EL dueño del FormID.</para></summary>
    Private Function BuscarBorrador(mainForm As MainForm, fid As UInteger) As ClaseResuelta
        Dim hd = mainForm.HdptDraftPorFormId(fid)
        If hd IsNot Nothing Then Return New ClaseResuelta With {
            .Nombre = NombreDeLaClase("HDPT"), .Edid = hd.Record.EditorID, .EsNuevo = hd.IsNew,
            .Bajar = AddressOf mainForm.UnregisterHdptDraft,
            .Compartido = CompartidoDeLaFirma("HDPT")}

        Dim tx = mainForm.TxstDraftPorFormId(fid)
        If tx IsNot Nothing Then Return New ClaseResuelta With {
            .Nombre = NombreDeLaClase("TXST"), .Edid = tx.Record.EditorID, .EsNuevo = tx.IsNew,
            .Bajar = AddressOf mainForm.UnregisterTxstDraft,
            .Compartido = CompartidoDeLaFirma("TXST")}

        Dim fl = mainForm.FlstDraftPorFormId(fid)
        If fl IsNot Nothing Then Return New ClaseResuelta With {
            .Nombre = NombreDeLaClase("FLST"), .Edid = fl.Record.EditorID, .EsNuevo = fl.IsNew,
            .Bajar = AddressOf mainForm.UnregisterFlstDraft,
            .Compartido = CompartidoDeLaFirma("FLST")}

        Dim ms = mainForm.TryGetMswpDraft(fid)
        If ms IsNot Nothing Then Return New ClaseResuelta With {
            .Nombre = NombreDeLaClase("MSWP"), .Edid = ms.Record.EditorID, .EsNuevo = ms.IsNew,
            .Bajar = AddressOf mainForm.UnregisterMswpDraft,
            .Compartido = CompartidoDeLaFirma("MSWP")}

        Dim ao = mainForm.TryGetArmoDraft(fid)
        If ao IsNot Nothing Then Return New ClaseResuelta With {
            .Nombre = NombreDeLaClase("ARMO"), .Edid = ao.Record.EditorID, .EsNuevo = ao.IsNew,
            .Bajar = AddressOf mainForm.UnregisterArmoDraft}

        Dim aa = mainForm.TryGetArmaDraft(fid)
        If aa IsNot Nothing Then Return New ClaseResuelta With {
            .Nombre = NombreDeLaClase("ARMA"), .Edid = aa.Record.EditorID, .EsNuevo = aa.IsNew,
            .Bajar = AddressOf mainForm.UnregisterArmaDraft}

        Dim ot = mainForm.TryGetOutfitDraft(fid)
        ' ⛔ La condición de OTFT NO es `IsNew` a secas y se transcribe como estaba
        ' (`OutfitPicker_Form.OnDeleteOrRevertOutfit`): un atuendo con `IsOverride` se revierte aunque
        ' `IsNew` diga otra cosa.
        If ot IsNot Nothing Then Return New ClaseResuelta With {
            .Nombre = NombreDeLaClase("OTFT"), .Edid = ot.Record.EditorID,
            .EsNuevo = (Not ot.IsOverride) AndAlso ot.IsNew,
            .Bajar = AddressOf mainForm.UnregisterOutfitDraft}

        Dim lv = mainForm.TryGetLeveledListDraft(fid)
        ' ⛔ LA RAMA QUE NO EXISTÍA. Hasta esta ola, `UnregisterLeveledListDraft` se llamaba SÓLO desde
        ' el `FormClosing` del selector de atuendos, así que una lista propia y sucia no tenía ningún
        ' gesto que la sacara a mitad de sesión. Misma condición que OTFT por la misma razón.
        If lv IsNot Nothing Then Return New ClaseResuelta With {
            .Nombre = NombreDeLaClase("LVLI"), .Edid = lv.Record.EditorID,
            .EsNuevo = (Not lv.IsOverride) AndAlso lv.IsNew,
            .Bajar = AddressOf mainForm.UnregisterLeveledListDraft}

        Return Nothing
    End Function

    ''' <summary>PURA: decide qué corresponde y con qué texto. <b>No toca nada y no muestra nada.</b>
    ''' <para>⛔ Es la mitad que un testigo puede correr. Ver el ⛔ de la clase.</para></summary>
    ''' <param name="dueno">Quien abrió el selector, o Nothing cuando no hay ninguno (un picker que no
    ''' cuelga de un editor con borrador tomado).</param>
    Friend Function Planear(mainForm As MainForm, entry As FormIdPickerEntry,
                            dueno As IDuenoDeBorradores) As PlanDeBaja
        Dim plan As New PlanDeBaja
        If entry Is Nothing OrElse mainForm Is Nothing Then Return plan
        Dim fid = entry.FormID
        If fid = 0UI Then Return plan
        plan.Objetivo = fid

        Dim clase = BuscarBorrador(mainForm, fid)

        ' ---- ¿lo tiene abierto OTRA ventana? Se pregunta ANTES que nada, y sólo para los borradores
        ' vivos: un record ya guardado no lo toma nadie.
        ' ⛔⛔ LAS DOS TOMAS DAN RESULTADOS OPUESTOS, A PROPÓSITO. Hasta esta ola NADIE consultaba
        ' `EstaTomado` antes de BORRAR —sus tres llamadores preguntan antes de ABRIR
        ' (`HeadPartEditor:686`, `:1505`, `TomaDeBorrador:99`)—, así que dar de baja un borrador abierto
        ' en otra ventana dejaba a ESA con `_actual` vivo, y su `Abandonar` lo REPONÍA.
        '   Tomado por MÍ se permite: es la ley que el editor de head parts ya aplica —lista el
        ' borrador actual sin excluirlo (`:638-697`) y lo resuelve soltando la toma (`:1928-1943`)— y es
        ' para lo que `TomaDeBorrador.Soltar` existe. La guarda `isCurrent` que ARMO/ARMA tenían se
        ' retiró: bloqueaba un gesto que HDPT sí permite, y su recuperación ya estaba escrita
        ' (`ArmoEditor_Form.ReTomarObjetivoTrasLaBaja`).
        Dim tomadoPorMi = (dueno IsNot Nothing AndAlso dueno.FormIdTomado() = fid)
        If clase IsNot Nothing AndAlso Not tomadoPorMi AndAlso Borradores.EstaTomado(fid) Then
            plan.Gesto = Gesto.BloqueadoPorEdicionAbierta
            plan.Clase = clase.Nombre
            plan.Titulo = $"Delete {clase.Nombre}"
            plan.Mensaje = $"That {clase.Nombre} is already open in another editor window. Close it first."
            plan.Botones = MessageBoxButtons.OK
            plan.Icono = MessageBoxIcon.Information
            Return plan
        End If

        If clase IsNot Nothing Then
            plan.Clase = clase.Nombre
            plan.Bajar = clase.Bajar

            If Not clase.EsNuevo Then
                ' OVERRIDE → REVERTIR.
                plan.Gesto = Gesto.RevertirOverride
                plan.Titulo = $"Revert {clase.Nombre}"
                plan.Mensaje = $"Revert {clase.Nombre} '{clase.Edid}' to the original record?" & vbCrLf &
                               "Your edits will be discarded." &
                               If(clase.Compartido.Length = 0, "", vbCrLf & vbCrLf & clase.Compartido)
                plan.Icono = MessageBoxIcon.Question
                Return plan
            End If

            ' NUEVO → BORRAR, y sólo si no lo apunta nadie. El censo es la fuente única, y se le suma lo
            ' que el dueño tenga EN BUFFER (ver `ReferenciasNoVolcadas`).
            plan.Referrers.AddRange(mainForm.GetDraftReferrers(fid))
            If dueno IsNot Nothing Then
                Dim enBuffer = dueno.ReferenciasNoVolcadas(fid)
                If enBuffer IsNot Nothing Then plan.Referrers.AddRange(enBuffer)
            End If
            ' ⛔ EL MISMO CARTEL CON UN PÁRRAFO MÁS, no dos carteles distintos: el orden es
            ' verbo+detalle → compartido → referrers, EL MISMO que ya emite la rama «ya guardado» de
            ' más abajo. Que las dos ramas se lean igual es la mitad visible de la unificación.
            plan.Gesto = If(plan.Referrers.Count > 0, Gesto.QuitarConReferencias, Gesto.BorrarNuevo)
            plan.Titulo = $"Delete {clase.Nombre}"
            plan.Mensaje = $"Delete {clase.Nombre} draft '{clase.Edid}'? This cannot be undone." &
                           If(clase.Compartido.Length = 0, "", vbCrLf & vbCrLf & clase.Compartido) &
                           If(plan.Referrers.Count = 0, "",
                              vbCrLf & vbCrLf & "Still referenced by:" & vbCrLf &
                              String.Join(vbCrLf, plan.Referrers) & vbCrLf & vbCrLf &
                              "Those references will be left pointing at a record that no longer exists, " &
                              "and the next Save will refuse until you fix them.")
            Return plan
        End If

        ' ---- No hay borrador vivo. ¿Es un record YA GUARDADO de los nuestros?
        ' ⛔⛔ SE PREGUNTA, NO SE SUPONE. Esta rama se elegía por DESCARTE, y por descarte una fila
        ' VANILLA del orden de carga —que llega al selector como cualquier otra— caía acá: el EDID no
        ' empieza con `npcm_`, así que ofrecía «Revert saved …» y el «Sí» metía un record de Bethesda
        ' en `_recordsToRemove`. `Gesto.Nada` ya tiene su cartel («isn't one of your drafts or saved
        ' records») y su línea de log, así que la salida correcta ya estaba escrita: faltaba llegar.
        If Not mainForm.EsRecordPropioVivo(fid) Then Return plan
        ' ⛔ Recién ACÁ manda la firma, y es el único lugar donde manda: sirve para NOMBRAR la clase, que
        ' es para lo que la usaban las cuatro sedes que esta absorbe (`BorradoDeHeadParts.NombreDeLaClase`).
        plan.Clase = NombreDeLaClase(entry.Signature)
        Dim esNuevoRec = entry.EditorID IsNot Nothing AndAlso
                         entry.EditorID.StartsWith("npcm_", StringComparison.OrdinalIgnoreCase)
        Dim verbo = If(esNuevoRec, "Delete", "Revert")
        Dim detalle = If(esNuevoRec, "It will be removed from your plugin on the next Save.",
                                     "The override will be dropped on the next Save — the original record wins again.")
        plan.Referrers.AddRange(mainForm.GetDraftReferrers(fid))
        Dim aviso = If(plan.Referrers.Count > 0,
                       vbCrLf & vbCrLf & "Still referenced by:" & vbCrLf & String.Join(vbCrLf, plan.Referrers), "")
        ' El aviso de compartido también vale para un record guardado de esa clase.
        Dim compartidoGuardado = CompartidoDeLaFirma(entry.Signature)
        plan.Gesto = Gesto.QuitarGuardado
        plan.Titulo = $"{verbo} saved {plan.Clase}"
        plan.Mensaje = $"{verbo} saved {plan.Clase} '{entry.DisplayName}'?" & vbCrLf & detalle &
                       If(compartidoGuardado.Length = 0, "", vbCrLf & vbCrLf & compartidoGuardado) & aviso
        Return plan
    End Function

    ''' <summary>Ejecuta un plan YA CONFIRMADO. True si la baja ocurrió (el selector saca la fila).
    ''' <para>⛔ No pregunta nada: la confirmación es del llamador. Así el testigo puede recorrer las
    ''' cuatro ramas sin pump de mensajes.</para></summary>
    Friend Function Ejecutar(mainForm As MainForm, entry As FormIdPickerEntry, plan As PlanDeBaja) As Boolean
        If mainForm Is Nothing OrElse plan Is Nothing OrElse plan.Objetivo = 0UI Then Return False
        Dim fid = plan.Objetivo

        Select Case plan.Gesto
            Case Gesto.RevertirOverride
                ' ⛔ BAJAR EL BORRADOR NO ALCANZA. Si ese override ya se guardó, la fase 2a vuelve a
                ' preservar el record del plugin destino salvo que esté en `RecordsToRemove`, así que el
                ' record revertido se seguiría escribiendo. Con la marca, la 2a lo deja caer y gana el
                ' original. Las dos líneas van juntas y UNA sola vez, para las ocho clases.
                plan.Bajar?.Invoke(fid)
                mainForm.MarkRecordForRemoval(fid)
                mainForm.RevertAppOverrideInMemory(fid)
                Return True

            Case Gesto.BorrarNuevo, Gesto.QuitarConReferencias
                ' ⛔ LAS DOS RAMAS HACEN LO MISMO, y ésa es exactamente la unificación de D-3: lo único
                ' que las distingue es que la segunda le dijo al usuario quién lo apunta antes de
                ' preguntarle. Si alguna vez se separan acá, volvió la conducta doble.
                plan.Bajar?.Invoke(fid)
                Return True

            Case Gesto.QuitarGuardado
                mainForm.MarkRecordForRemoval(fid)
                mainForm.RevertAppOverrideInMemory(fid)
                Return True

            Case Else
                ' Nada / el bloqueo por edición abierta: no se hizo nada y la fila se queda.
                Return False
        End Select
    End Function

    ''' <summary>LA PUERTA DE LA UI: planear → preguntar → ejecutar → avisarle al dueño.
    ''' <para>Devuelve True recién cuando la baja OCURRIÓ, que es lo que el selector mira para sacar la
    ''' fila.</para></summary>
    Friend Function BorrarORevertir(mainForm As MainForm, owner As IWin32Window,
                                    entry As FormIdPickerEntry,
                                    dueno As IDuenoDeBorradores) As Boolean
        Dim plan = Planear(mainForm, entry, dueno)

        If plan.Gesto = Gesto.Nada Then
            ' ⛔ NO SE TIRA, Y NO ES COMODIDAD. La app corre con `UnhandledExceptionMode.ThrowException`
            ' (ver `MswpSubEditor_Form:51-54`: «un throw acá CIERRA la app — un `Using ... As New` no lo
            ' atrapa»), así que tirar desde un manejador de clic se lleva TODOS los borradores de la
            ' sesión. Y callarse sería reponer el defecto de fondo de esta ola: un gesto que no hace
            ' nada y no lo dice. Se avisa y se deja la fila.
            Logger.LogLazy(Function() $"[BAJA] sin rama para la fila '{entry?.DisplayName}' " &
                                      $"(firma '{entry?.Signature}', fid 0x{If(entry Is Nothing, 0UI, entry.FormID):X8})")
            MessageBox.Show(owner,
                            "This row can't be removed from here — it isn't one of your drafts or saved records.",
                            "Delete / Revert", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return False
        End If

        If Not plan.PideConfirmacion Then
            ' El único que ya sólo informa: abierto en otra ventana.
            MessageBox.Show(owner, plan.Mensaje, plan.Titulo, plan.Botones, plan.Icono)
            Return False
        End If

        ' ⛔ CON EL BOTÓN POR DEFECTO DEL PLAN: ver el ⛔ de `PlanDeBaja.BotonPorDefecto`.
        If MessageBox.Show(owner, plan.Mensaje, plan.Titulo, plan.Botones, plan.Icono,
                           plan.BotonPorDefecto) <> DialogResult.Yes Then Return False
        Return EjecutarYAvisar(mainForm, entry, plan, dueno)
    End Function

    ''' <summary>La mitad de <see cref="BorrarORevertir"/> que NO es UI: ejecutar el plan ya
    ''' confirmado y avisarle al dueño. True si la baja ocurrió.
    ''' <para>⛔⛔ ESTÁ SEPARADA PARA QUE UN TESTIGO LA CORRA. <c>BorrarORevertir</c> abre
    ''' <c>MessageBox</c> y un modal CUELGA a un gate sin pump de mensajes, así que la puerta
    ''' entera no se puede medir — y lo que vive acá adentro es justo la ley con historial: el
    ''' aviso al dueño va SIEMPRE que la baja tuvo éxito, no sólo cuando el FormID es el que el
    ''' dueño tenía tomado. Condicionarlo deja viva la resurrección que documenta
    ''' <see cref="IDuenoDeBorradores.TrasLaBaja"/>. Mismo movimiento que
    ''' <c>TomaDeBorrador.CambiarObjetivo</c> y <c>OutfitPicker_Form.PlanDeCierreDeListas</c>.</para></summary>
    Friend Function EjecutarYAvisar(mainForm As MainForm, entry As FormIdPickerEntry,
                                    plan As PlanDeBaja,
                                    dueno As IDuenoDeBorradores) As Boolean
        If Not Ejecutar(mainForm, entry, plan) Then Return False
        ' ⛔⛔ SIEMPRE, no sólo si el FormID era el que el dueño tenía tomado: ver el ⛔ de `TrasLaBaja`.
        dueno?.TrasLaBaja(plan.Objetivo)
        Return True
    End Function

    ''' <summary>EL CARTEL de «vas a perder lo que editaste» cuando el editor cambia de objetivo.
    ''' True ⇒ el usuario dijo que sí y se puede cambiar.
    ''' <para>⛔ Una sola redacción para los CINCO editores, por lo mismo que los carteles de baja
    ''' viven acá: con una copia por editor, cinco ventanas terminan diciendo cinco cosas distintas
    ''' sobre el mismo gesto. La decisión de SI hay que preguntar es
    ''' <c>TomaDeBorrador.CambiarDestruyeTrabajo</c>, que es la mitad que el testigo corre.</para>
    ''' <para>⛔ <c>Button2</c>: ver <see cref="PlanDeBaja.BotonPorDefecto"/>. Esto tampoco tiene
    ''' deshacer.</para></summary>
    Friend Function ConfirmarCambioDeObjetivo(owner As IWin32Window, clase As String, titulo As String) As Boolean
        Return MessageBox.Show(owner,
                               $"You have unsaved edits on the current {clase}. Switching to another one " &
                               "discards them — this cannot be undone." & vbCrLf & vbCrLf &
                               "Switch anyway?",
                               titulo, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                               MessageBoxDefaultButton.Button2) = DialogResult.Yes
    End Function

    '==============================================================================================
    ' LO QUE ES MÍO: la lista de filas propias, en UNA sede
    '==============================================================================================

    ''' <summary>Los records PROPIOS del usuario ya GUARDADOS en su plugin, como filas de selector,
    ''' sin repetir los que ya vienen como borrador.
    '''
    ''' <para>⛔⛔ <b>ES LA MITAD QUE FALTABA EN TODOS LOS SELECTORES</b>, y estaba escrita cinco veces
    ''' con el mismo bucle. Sin ella, guardar y reabrir la app hace DESAPARECER los propios records de
    ''' la lista: el remapeo de la promoción los saca del registro de borradores
    ''' (<c>Borradores.vb:516-523</c>), así que después de un Save ya no son borradores y el selector
    ''' —que sólo ofrecía borradores— no los mostraba. Y desde que el botón «Delete / Revert…» se
    ''' habilita SÓLO sobre las filas que el llamador pasa, no estar en esta lista es quedarse sin
    ''' salida.</para>
    ''' <para>⛔ Vive acá y no en un formulario porque el enable del selector DELEGA en ella la ley de
    ''' qué fila tiene salida: con una copia por editor, cambiar «qué cuenta como mío» obligaría a tocar
    ''' doce archivos.</para></summary>
    ''' <param name="yaEstan">Las filas ya armadas (los borradores), para no duplicar por FormID.</param>
    Friend Function EntradasPropias(mainForm As MainForm, sig As String,
                                    yaEstan As IEnumerable(Of FormIdPickerEntry)) As List(Of FormIdPickerEntry)
        Dim r As New List(Of FormIdPickerEntry)
        If mainForm Is Nothing OrElse String.IsNullOrEmpty(sig) Then Return r
        Dim vistos As New HashSet(Of UInteger)(
            If(yaEstan, Enumerable.Empty(Of FormIdPickerEntry)()).Where(Function(x) x IsNot Nothing).
                Select(Function(x) x.FormID))
        For Each g In mainForm.GetAuthoredRecords(sig)
            If vistos.Contains(g.FormID) Then Continue For
            r.Add(New FormIdPickerEntry With {.FormID = g.FormID, .EditorID = g.EditorID,
                                              .DisplayName = g.DisplayName, .Signature = sig,
                                              .PluginName = "(saved)"})
        Next
        Return r
    End Function

    '==============================================================================================
    ' Nombres
    '==============================================================================================

    Private Function NombreDeLaClase(firma As String) As String
        If String.IsNullOrEmpty(firma) Then Return "record"
        Select Case firma.ToUpperInvariant()
            Case "HDPT" : Return "head part"
            Case "TXST" : Return "texture set"
            Case "FLST" : Return "form list"
            Case "MSWP" : Return "material swap"
            Case "ARMO" : Return "armor"
            Case "ARMA" : Return "armor addon"
            Case "OTFT" : Return "outfit"
            Case "LVLI" : Return "leveled list"
            Case Else : Return firma
        End Select
    End Function

    ''' <summary>El aviso de COMPARTIDO de una clase, para la rama «ya guardado» — donde no hay borrador
    ''' del que sacarlo. Las clases que hoy no lo tienen devuelven "".</summary>
    Private Function CompartidoDeLaFirma(firma As String) As String
        If String.IsNullOrEmpty(firma) Then Return ""
        Select Case firma.ToUpperInvariant()
            Case "HDPT" : Return "A head part is SHARED: this affects every NPC in the load order that wears it."
            Case "TXST" : Return "A texture set is SHARED: this affects everything that points at it."
            Case "FLST" : Return "A form list is SHARED: this affects everything that points at it."
            Case "MSWP" : Return "A material swap is SHARED: this affects every armor in the load order that uses it."
            Case Else : Return ""
        End Select
    End Function

End Module
