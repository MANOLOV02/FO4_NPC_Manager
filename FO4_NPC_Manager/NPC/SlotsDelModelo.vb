Imports FO4_Base_Library

''' <summary>La POLÍTICA del botón «Slots declared by the models…» del editor de ARMA: qué se le propone
''' al usuario a partir de lo que declaran los dos modelos (MOD2 male + MOD3 female). Sin UI, para que un
''' testigo la recorra sin levantar un formulario.
''' <para>La LEY de qué declara un archivo NO vive acá: vive en <see cref="SlotsDeLaMalla"/>, en la
''' librería. Acá sólo está la decisión de producto — qué se ofrece, qué nunca se toca, y qué se le
''' cuenta al usuario.</para>
''' <para>⛔ Las tres decisiones que sostienen este módulo salen de MEDICIÓN sobre el load order
''' instalado, no de criterio: (1) el botón NUNCA destilda, porque 550 ARMA de Skyrim y 29 de Fallout
''' declaran slots SIN geometría a propósito y así es como el motor desaloja lo que había; (2) la unión
''' de los dos géneros no se aplica en bloque, porque 21 de los 28 ARMA de Skyrim (⛔ el denominador es del load order INSTALADO y se mueve cuando el usuario edita: era 39) a los que el botón
''' agregaría algo traen ese bit de UN SOLO género; (3) el tag del Pip-Boy se descuenta, porque sin eso
''' el botón le propondría el slot 60 a los 51 brazos izquierdos de armadura del vanilla.</para></summary>
Friend Class SlotsDelModelo

    ''' <summary>Por qué un género no aportó máscara. Son TRES estados y no dos: «este addon no declara
    ''' modelo para ese género» (~278 ARMA de Skyrim y ~190 de Fallout) es una cosa, y «declara uno y no se
    ''' pudo leer» es otra — la primera es normal y la segunda es un problema del usuario.</summary>
    Friend Enum EstadoDeLectura
        ''' <summary>El ARMA no declara modelo para ese género.</summary>
        SinPath = 0
        ''' <summary>Hay path y no se pudo leer (no está suelto ni en un archivo del juego, o está roto).</summary>
        Ilegible = 1
        ''' <summary>Se leyó. La máscara puede ser 0 igual: eso es «no declara ningún slot».</summary>
        Leida = 2
    End Enum

    ''' <summary>Lo que aportó UN género.</summary>
    Friend Structure LecturaGenero
        ''' <summary>Ruta ya normalizada que se intentó leer ("" si el ARMA no declara modelo).</summary>
        Public Ruta As String
        ''' <summary>Cuál de los tres estados.</summary>
        Public Estado As EstadoDeLectura
        ''' <summary>Máscara CRUDA del archivo. El descuento del Pip-Boy lo hace <see cref="Proponer"/>.</summary>
        Public Mascara As UInteger
        ''' <summary>Particiones o tags mirados.</summary>
        Public Declaraciones As Integer
        ''' <summary>Cuántos de ésos cayeron fuera de [30,61].</summary>
        Public FueraDeBanda As Integer
    End Structure

    ''' <summary>Lo que el diálogo tiene que mostrar y lo único que se puede llegar a aplicar.</summary>
    Friend Structure Propuesta
        ''' <summary>Lectura del modelo masculino (MOD2).</summary>
        Public Male As LecturaGenero
        ''' <summary>Lectura del modelo femenino (MOD3).</summary>
        Public Female As LecturaGenero
        ''' <summary>Al menos un género se leyó.</summary>
        Public HuboLectura As Boolean
        ''' <summary>Unión de los dos géneros, ya SIN el tag del Pip-Boy.</summary>
        Public Declarado As UInteger
        ''' <summary>Máscara del modelo masculino ya sin el tag del Pip-Boy. La usa el diálogo para decir,
        ''' fila por fila, QUÉ género declara ese slot: sin eso la etiqueta tendría que re-derivar el
        ''' descuento del Pip-Boy por su cuenta, que es la misma ley en dos lugares.</summary>
        Public MaleNeto As UInteger
        ''' <summary>Ídem para el modelo femenino.</summary>
        Public FemaleNeto As UInteger
        ''' <summary>Lo declarado que el BOD2 todavía no tiene. Es lo único que el diálogo ofrece.</summary>
        Public Faltan As UInteger
        ''' <summary>Lo que el BOD2 declara y la malla no. INFORMATIVO: así desaloja el motor, no se toca.</summary>
        Public Sobran As UInteger
        ''' <summary>Hubo declaraciones y NINGUNA cayó en [30,61]. Estado propio, porque la consecuencia
        ''' es OPUESTA en los dos motores — ver <see cref="FueraDeBandaSeOculta"/>.</summary>
        Public TodoFueraDeBanda As Boolean
    End Structure

    ''' <summary>Lee lo que declara el modelo de un género. <paramref name="rutaCruda"/> es lo que el
    ''' usuario tiene en el panel; se normaliza con la sede de normalización de rutas de malla, la misma
    ''' que usa el render, para que el botón y el render busquen el mismo archivo.</summary>
    ''' <param name="rutaCruda">MOD2 o MOD3 tal como está en el TextBox.</param>
    ''' <param name="modo">Lector del juego, de <c>SlotsDeLaMalla.LecturaDelJuego</c>.</param>
    Friend Shared Function Leer(rutaCruda As String, modo As SlotsDeLaMalla.ModoDeLectura) As LecturaGenero
        Dim r As LecturaGenero = Nothing
        r.Ruta = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(If(rutaCruda, "").Trim())
        If String.IsNullOrEmpty(r.Ruta) Then
            r.Estado = EstadoDeLectura.SinPath
            Return r
        End If
        Dim c = SlotsDeLaMalla.DeLaMalla(r.Ruta, modo)
        If Not c.HasValue Then
            r.Estado = EstadoDeLectura.Ilegible
            Return r
        End If
        r.Estado = EstadoDeLectura.Leida
        r.Mascara = c.Value.Mascara
        r.Declaraciones = c.Value.Declaraciones
        r.FueraDeBanda = c.Value.FueraDeBanda
        Return r
    End Function

    ''' <summary>La propuesta. Pura: no toca UI ni disco.</summary>
    ''' <param name="male">Lectura del modelo masculino.</param>
    ''' <param name="female">Lectura del modelo femenino.</param>
    ''' <param name="actual">BOD2 que el usuario tiene tildado ahora.</param>
    ''' <param name="bitPipboy">Unión de los biped objects que reservan para el Pip-Boy TODAS las razas
    ''' que este ARMA sirve. 0 en Skyrim. Se descuenta porque el resolver le da regla propia a ese tag
    ''' (0x14035E3B0: N == D+30 ⇒ visible = NOT(pip-boy puesto)), o sea que NO declara ocupación de slot.</param>
    Friend Shared Function Proponer(male As LecturaGenero, female As LecturaGenero,
                                    actual As UInteger, bitPipboy As UInteger) As Propuesta
        Dim p As Propuesta = Nothing
        p.Male = male
        p.Female = female
        p.HuboLectura = (male.Estado = EstadoDeLectura.Leida) OrElse (female.Estado = EstadoDeLectura.Leida)
        If Not p.HuboLectura Then Return p

        Dim mNeto = male.Mascara And Not bitPipboy
        Dim fNeto = female.Mascara And Not bitPipboy
        p.MaleNeto = mNeto
        p.FemaleNeto = fNeto
        p.Declarado = mNeto Or fNeto
        p.Faltan = p.Declarado And Not actual
        ' ⛔ El bit del Pip-Boy sale de `Declarado` a proposito, pero eso NO alcanza para sacarlo de
        ' `Sobran`: hay que mirar si la malla lo declara. Si LA MALLA LO TRAE, no es «declarado sin
        ' geometria» (la malla lo dibuja) y no debe listarse. Si NO lo trae, es un «declarado sin
        ' geometria» LEGITIMO —el mecanismo de desalojo— y el usuario tiene que verlo: sacarlo siempre
        ' le esconderia justo el slot por el que su armadura desaloja el Pip-Boy.
        ' ⛔ Y una guarda mas: para AFIRMAR que la malla no trae el tag del Pip-Boy hay que haber
        ' podido LEERLA. Con un modelo ilegible, `Mascara` vale 0 por no saber, no por no declarar, y
        ' listarlo como «declarado sin geometria» seria afirmar algo sobre el archivo que no se leyo.
        ' En ese caso no se afirma: el bit sale de `Sobran` y punto.
        Dim pipboyEnLaMalla = (male.Mascara Or female.Mascara) And bitPipboy
        p.Sobran = actual And Not (p.Declarado Or pipboyEnLaMalla)

        ' ⛔ Y la guarda que vale para los TREINTA Y DOS bits, no para uno: `Sobran` es la AFIRMACION
        ' «este slot lo declaras sin geometria», y con un modelo que no se pudo leer no se sabe que
        ' declara — `Mascara` vale 0 por no saber, no por no declarar. Entonces no se afirma nada.
        ' (Aplicarla solo al bit del Pip-Boy era peor que inutil: en Skyrim `bitPipboy` es 0, asi que
        ' no protegia ningun bit, y dejaba la misma ley con dos respuestas en lineas contiguas.)
        If male.Estado = EstadoDeLectura.Ilegible OrElse female.Estado = EstadoDeLectura.Ilegible Then
            p.Sobran = 0UI
        End If

        Dim decl = male.Declaraciones + female.Declaraciones
        Dim fuera = male.FueraDeBanda + female.FueraDeBanda
        ' ⛔ Este flag describe LO QUE SE LEYO, y se queda exacto a proposito. Guardarlo contra el
        ' caso «un modelo ilegible» parecia la misma ley que la de `Sobran`, pero NO lo es: desviaba el
        ' caso al cartel de `Declarado = 0`, que en Skyrim dice «(no BSDismemberSkinInstance
        ' partitions)» sobre una malla que SI las tiene, y le sacaba al usuario el unico cartel que le
        ' nombra la causa real (caer fuera de banda). La diferencia con `Sobran` es que `Sobran` es una
        ' AFIRMACION sobre el archivo que falta, y esto es un DIAGNOSTICO del que se leyo: lo que no
        ' puede afirmar de mas es el TEXTO, y por eso el cartel dice «the models that could be read» y
        ' la cabecera nombra al que fallo.
        p.TodoFueraDeBanda = (p.Declarado = 0UI) AndAlso decl > 0 AndAlso fuera = decl
        Return p
    End Function

    ''' <summary>Quién declara un slot dado. Vive acá y no dentro del diálogo porque es lo que el
    ''' usuario LEE en cada fila y lo que decide si le conviene tildarla: escrita adentro del formulario
    ''' seria la misma ley en dos lugares y ningun testigo la alcanzaria.</summary>
    Friend Enum Atribucion
        ''' <summary>Los dos modelos lo declaran.</summary>
        Ambos = 0
        ''' <summary>Sólo el masculino, y el femenino SÍ se leyó.</summary>
        SoloMale = 1
        ''' <summary>Sólo el femenino, y el masculino SÍ se leyó.</summary>
        SoloFemale = 2
        ''' <summary>Lo declara el masculino y el ARMA no tiene modelo femenino.</summary>
        MaleYNoHayFemenino = 3
        ''' <summary>Lo declara el masculino y el femenino NO SE PUDO LEER.</summary>
        MaleYFemeninoIlegible = 4
        ''' <summary>Lo declara el femenino y el ARMA no tiene modelo masculino.</summary>
        FemaleYNoHayMasculino = 5
        ''' <summary>Lo declara el femenino y el masculino NO SE PUDO LEER.</summary>
        FemaleYMasculinoIlegible = 6
        ''' <summary>⛔ Nadie lo declara. Inalcanzable desde el diálogo (que sólo itera lo que FALTA),
        ''' pero la función es <c>Friend</c> y sin este miembro le atribuiría a un género un slot que
        ''' no declara.</summary>
        Ninguno = 7
    End Enum

    ''' <summary>La atribución de UN slot. Medido: 21 de los 28 ARMA de Skyrim (⛔ el denominador es del load order INSTALADO y se mueve cuando el usuario edita: era 39) a los que el botón
    ''' agregaría algo traen ese bit de un solo género, así que esta etiqueta no es cosmética — es lo
    ''' que evita que el usuario le regale a un género un slot que sólo dibuja el otro.</summary>
    ''' <param name="p">La propuesta.</param>
    ''' <param name="bit">Bit del slot (slot menos 30).</param>
    Friend Shared Function AtribucionDe(p As Propuesta, bit As Integer) As Atribucion
        ' ⛔ VB enmascara el contador del shift a 5 bits: sin esta guarda, bit=32 mediría el bit 0 y
        ' bit=-1 el 31, y la respuesta sería una mentira sin excepción de por medio.
        If bit < 0 OrElse bit > 31 Then Return Atribucion.Ninguno
        Dim b As UInteger = 1UI << bit
        Dim enM = (p.MaleNeto And b) <> 0UI
        Dim enF = (p.FemaleNeto And b) <> 0UI
        If Not enM AndAlso Not enF Then Return Atribucion.Ninguno
        Dim leidoM = p.Male.Estado = EstadoDeLectura.Leida
        Dim leidoF = p.Female.Estado = EstadoDeLectura.Leida
        If leidoM AndAlso leidoF Then
            If enM AndAlso enF Then Return Atribucion.Ambos
            Return If(enM, Atribucion.SoloMale, Atribucion.SoloFemale)
        End If
        ' El otro género: «no hay modelo» y «no se pudo leer» NO son lo mismo, y el cartel de arriba
        ' del diálogo ya los distingue. Decirlo distinto acá evita que las dos líneas del mismo modal
        ' se contradigan.
        If enM Then
            Return If(p.Female.Estado = EstadoDeLectura.SinPath,
                      Atribucion.MaleYNoHayFemenino, Atribucion.MaleYFemeninoIlegible)
        End If
        Return If(p.Male.Estado = EstadoDeLectura.SinPath,
                  Atribucion.FemaleYNoHayMasculino, Atribucion.FemaleYMasculinoIlegible)
    End Function

    ''' <summary>⛔ LA LEY: el botón sólo AGREGA. Vive acá y no dentro del manejador del formulario para
    ''' que un testigo la pueda ejercer y para que la mutación «aplicar <c>Declarado</c> en vez de la
    ''' unión con lo actual» ponga rojo al gate. Medido: 550 ARMA de Skyrim y 29 de Fallout declaran slots
    ''' sin geometría a propósito, que es como el motor desaloja lo que estaba puesto (loop 1 del attach,
    ''' Fallout 0x1403597E0 / Skyrim 0x140218AE0); destildarlos sería romperlas.</summary>
    ''' <param name="actual">Lo que el usuario tiene tildado.</param>
    ''' <param name="elegidos">Los bits que el usuario tildó en el diálogo.</param>
    Friend Shared Function MascaraAplicada(actual As UInteger, elegidos As UInteger) As UInteger
        Return actual Or elegidos
    End Function

    ''' <summary>Qué BOD2 declara el ARMO DESCARTABLE que sostiene una ARMA en el alcance «sólo este
    ''' addon» de la vista previa. Sin esto nace en 0, y como <c>EquipResolver.ArmaGeometryMask</c>
    ''' hereda del ARMO cuando la ARMA no declara nada, el addon no es dueño de NINGÚN slot:
    ''' <c>MeterAdjunto</c> lo rechaza y la fase 1 —donde «sin dueño = cubierto»— le esconde la
    ''' geometría entera. Ése es el motivo por el que «sólo este addon» mostraba vacío mientras
    ''' «Full armor» con el ARMO padre real mostraba bien.
    ''' <para>⛔ SÓLO cuando la ARMA no declara. Es la MISMA condición que gobierna
    ''' <c>ArmaGeometryMask</c> —el ARMO sólo importa cuando el ARMA no declara— y es lo que mantiene
    ''' inertes las otras puertas del BOD2 del ARMO. Sin ella, un ARMA que YA declara vería cambiar
    ''' <c>wornEquipMask</c> y se le apagaría el pelo del NPC, que hoy funciona bien.</para>
    ''' <para>⛔ El tag del Pip-Boy se DESCUENTA, y NO por lo mismo que en el botón: su visibilidad no
    ''' depende del dueño (<c>BSTriShapeGeometry.SegmentoOculto</c> devuelve el estado del dispositivo
    ''' sin mirar la cobertura), así que declararlo no lo muestra — y en cambio dejaría la máscara
    ''' valiendo EXACTAMENTE ese bit, que <c>NpcMountingResolver</c> compara por IGUALDAD y toma por un
    ''' Pip-Boy suelto. Medido (<c>Tools/SlotsDelModeloProbe</c>, línea «mallas cuya mascara es
    ''' EXACTAMENTE ese bit»): en Fallout, <b>146</b> mallas del corpus tienen por máscara EXACTAMENTE
    ''' ese bit — que es la propiedad fuerte que hace falta acá, no la de «lo incluyen».</para>
    ''' <para>⛔ El fail-closed de la raza es FO4-only: en Skyrim no hay tag con regla propia ni
    ''' consumidor que se confunda (<c>NpcMountingResolver</c> corta con un gate de juego), así que
    ''' cerrar allá sólo costaría el caso que el usuario reportó. Y el juego va POR PARÁMETRO y no
    ''' derivado adentro, por lo mismo que <see cref="SlotsDeLaMalla.ModoDeLectura"/>: el default de
    ''' <c>Config_App.Current.Game</c> es Skyrim.</para></summary>
    ''' <param name="slotsDeLaMalla">Unión de lo que declaran MOD2 y MOD3.</param>
    ''' <param name="bod2DelArma">BOD2 propio de la ARMA.</param>
    ''' <param name="bitPipboy">Unión de los biped objects de Pip-Boy de las razas que sirve.</param>
    ''' <param name="razaResuelta">False si no se pudo resolver NINGUNA de sus razas.</param>
    ''' <param name="juego">El juego de la sesión, resuelto por el llamador.</param>
    Friend Shared Function BipedDelEnvoltorio(slotsDeLaMalla As UInteger, bod2DelArma As UInteger,
                                              bitPipboy As UInteger, razaResuelta As Boolean,
                                              juego As Config_App.Game_Enum) As UInteger
        If bod2DelArma <> 0UI Then Return 0UI
        If juego <> Config_App.Game_Enum.Skyrim AndAlso Not razaResuelta Then Return 0UI
        Return slotsDeLaMalla And Not bitPipboy
    End Function

    ''' <summary>¿El motor OCULTA la geometría cuya declaración cae FUERA de [30,61]? La respuesta es POR
    ''' JUEGO y es OPUESTA.
    ''' <para>Las dos salen de la sede de cada juego, pero NO en el mismo sentido, y el
    ''' matiz importa: en Fallout 4 la respuesta se DERIVA (la sede tiene una rama propia que devuelve
    ''' False para todo lo que no matchea); en Skyrim la sede devuelve el <c>hideOutOfBand</c> que se le
    ''' pasa, así que lo derivado es la BANDA —que bodyPart 0 caiga afuera— y el True es una CONSTANTE
    ''' del camino de worn item, citada: la fase 1 oculta fuera de banda
    ''' (<c>lea eax,[rdi-0x1e]; cmp ax,0x1f; ja</c>) y el render pone
    ''' <c>OcclusionAsWornItem = (categoria &lt;&gt; HeadPart)</c>, que para la malla de un ARMA es siempre
    ''' True. Lo que este método sí garantiza es que si el plegado o la banda cambian, el cartel cambia
    ''' con ellos.</para></summary>
    ''' <param name="modo">Lector del juego.</param>
    Friend Shared Function FueraDeBandaSeOculta(modo As SlotsDeLaMalla.ModoDeLectura) As Boolean
        If modo = SlotsDeLaMalla.ModoDeLectura.ParticionesDismember Then
            ' BodyPart 0 no pliega a nada de [30,61] — y no es un valor inventado: es el que traen las 18
            ' particiones medidas en las mallas de UBE del corpus.
            Return Nifcontent_Class_Manolo.ParticionOculta(0, 0UI, hideOutOfBand:=True)
        End If
        ' Tag 62: fuera de [30,61] y fuera de la banda 130..161, así que cae en la rama «todo lo demás».
        Return BSTriShapeGeometry.SegmentoOculto(62, 0UI, 0UI, False)
    End Function

End Class
