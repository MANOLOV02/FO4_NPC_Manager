''' <summary>EL CENSO de los campos por los que un borrador puede apuntar a OTRO borrador, y la única
''' lista que consumen sus DOS lectores: el remapeo de la promoción
''' (<see cref="Borradores.RemapearSupervivientes"/>) y el censo de referrers que decide si un borrador
''' se puede borrar (<c>MainForm.GetDraftReferrers</c>).
'''
''' <para>⛔ <b>ESTE ARCHIVO MATCHEA LA REGLA POR-FORMA DEL GATE DE ESCRITURAS SUELTAS A PROPÓSITO, Y
''' ESTÁ BARRIDO.</b> <c>ContextoPropioGate</c> reconoce una costura de escritura de referencias por su
''' FORMA —un miembro que recibe un <c>Action(Of UInteger)</c>, la regla anti-esquive-por-renombre— y
''' <see cref="ReferenciaDeBorrador.Poner"/> la matchea. Eso es correcto: acá se escriben referencias de
''' record, así que este archivo TIENE que estar bajo el barrido.</para>
'''
''' <para>⛔ <b>Y POR ESO VIVE APARTE DE <see cref="Borradores"/>.</b> Cuando esto estaba adentro de
''' aquél, la forma arrastraba al barrido un archivo cuya ley es OTRA —identidad y precondiciones de un
''' borrador— y su escritura legítima <c>v.Context.FormID = …</c> de
''' <see cref="Borradores.ReidentificarComoClon"/> pasaba a contar como escritura suelta: el detector
''' identifica la escritura por el NOMBRE de la propiedad y <c>FormID</c> colisiona, límite que el
''' propio gate declara. Son dos leyes distintas conviviendo en una casa; separarlas no es esquivar el
''' gate —el archivo que escribe referencias sigue barrido— es ponerle a cada ley su frontera.</para>
'''
''' <para>⛔ <b>NO se resuelve cambiando <c>Action(Of UInteger)</c> por un delegado propio.</b> Eso es
''' exactamente el esquive por renombre que la regla por-forma existe para impedir.</para>
'''
''' <para>⛔⛔ <b>ESTE CENSO NO ES TODO: HAY UN SEGUNDO ALMACÉN DE REFERENCIAS A BORRADORES.</b> Éste
''' recorre el <b>record</b>, y un borrador de atuendo guarda referencias <b>fuera</b> de su record: las
''' realizaciones selladas —<c>OutfitDraft.Realizaciones</c>, o sea el sorteo ya resuelto, cuyos
''' <c>OutfitArmorPick.ArmoFormID</c> pueden apuntar a un ARMO borrador—. Ése lo remapea
''' <see cref="OutfitDraft.RemapearPicks"/>, y el <see cref="Borradores.RemapearSupervivientes"/> lo llama
''' <b>al lado</b> de <c>RemapearUno</c> en la vuelta de los atuendos, no a través de esta lista. Queda
''' escrito acá porque la cabecera de arriba dice «la única lista que consumen sus DOS lectores» y eso es
''' cierto <b>del record</b>: quien venga a agregar un campo de referencia tiene que saber que existe una
''' segunda casa, o va a creer que tocando ésta cubrió todo.</para>
'''
''' <para>✅ <b>CERRADO — los DOS lectores cubren las dos casas.</b> Hubo una asimetría real: el remapeo
''' llamaba a las dos y el censo de referrers sólo a ésta, así que un ARMO borrador al que únicamente
''' apuntaba un pick sellado salía «no lo referencia nadie», «Delete draft» lo borraba y la realización
''' quedaba apuntando a un FormID muerto. Ya no: la ley del censo vive en
''' <see cref="Borradores.CensarReferrers"/> —<c>MainForm.GetDraftReferrers</c> delega en ella— y ahí las
''' dos casas se recorren una al lado de la otra, esta lista por <c>CensoDeReferencias.DeBorrador</c> y los
''' picks por <see cref="OutfitDraft.ReferenciasDePicks"/>. El testigo es <b>C51</b> de
''' <c>Tools\OutfitDraftSaveGate</c>, que llama al sujeto de producción y muere si se saca esa línea.</para>
'''
''' <para>Lo que SÍ sigue siendo cierto de esta lista, y es la razón de la nota de arriba: enumera campos
''' <b>DEL RECORD</b>. Un pick no es uno, así que la segunda casa nunca va a entrar acá — quien agregue un
''' campo de referencia tiene que tocar las dos, no ésta sola.</para></summary>
Public Module CensoDeReferencias

    ''' <summary>UNA referencia de un borrador que puede apuntar a OTRO borrador: qué vale hoy, cómo se
    ''' le escribe el destino real, y con qué nombre se la nombra cuando hay que decirle al usuario
    ''' quién referencia a qué.</summary>
    Friend Structure ReferenciaDeBorrador
        ''' <summary>A qué apunta hoy (0 = el campo no está puesto).</summary>
        Public Valor As UInteger
        ''' <summary>Le escribe el destino. Ver la nota de ESCRITURA DIRECTA en <see cref="DeBorrador"/>.</summary>
        Public Poner As Action(Of UInteger)
        ''' <summary>Cómo se llama el campo en la lista de referrers que ve el usuario.</summary>
        Public Que As String
    End Structure

    ''' <summary>Arma una <see cref="ReferenciaDeBorrador"/> sobre <paramref name="elem"/>.
    ''' <para>⛔ Existe para que el lambda capture un PARÁMETRO y no la variable de un <c>For Each</c>.
    ''' En VB la variable del bucle es UNA sola para todas las vueltas, así que un lambda armado adentro
    ''' del bucle captura la ÚLTIMA — y todas las referencias de un array terminarían escribiendo sobre
    ''' el mismo elemento. Con el elemento pasado por parámetro, cada llamada tiene el suyo.</para>
    ''' <para><c>Friend</c> y no <c>Private</c> porque el OTRO almacén de referencias
    ''' —<see cref="OutfitDraft.ReferenciasDePicks"/>, las realizaciones selladas— tiene el mismo bucle y
    ''' la misma trampa. La ley anti-captura vive acá, una sola vez.</para></summary>
    Friend Function RefDe(Of TE)(elem As TE, leer As Func(Of TE, UInteger),
                                  escribir As Action(Of TE, UInteger), que As String) As ReferenciaDeBorrador
        Return New ReferenciaDeBorrador With {.Valor = leer(elem),
                                              .Poner = Sub(v) escribir(elem, v),
                                              .Que = que}
    End Function

    ''' <summary>EL CENSO CERRADO de los campos por los que un borrador puede apuntar a OTRO borrador.
    ''' Sale de la reflexión de las vistas, no de una lista de memoria.
    '''
    ''' <para>⛔ <b>UNA enumeración, DOS consumidores</b>: el remapeo de la promoción
    ''' (<see cref="Borradores.RemapearSupervivientes"/>) y el censo de referrers que decide si un
    ''' borrador se puede borrar. Estaban escritos como dos listas a mano, y <b>ya habían derivado</b>:
    ''' los cuatro material swap del ARMA son <c>MO2S/MO3S/MO4S/MO5S</c> y los dos lados cubrían sólo los
    ''' dos primeros, aunque los CUATRO botones del editor abren el mismo selector con los borradores de
    ''' MSWP adentro. O sea: la referencia quedaba muerta tras guardar, Y «Delete draft» le decía al
    ''' usuario que a ese MSWP no lo apuntaba nadie. Dos listas se separan; ésta no puede.</para>
    '''
    ''' <para><b>Sólo entran las cinco clases que TIENEN borrador</b> —OTFT, LVLI, ARMO, ARMA, MSWP—:
    ''' un identificador provisional (byte alto 0xFF) no puede llegar a un campo que apunta a RACE,
    ''' ENCH, SNDR, TXST o KYWD, porque de esos no hay borrador que crear. Por eso quedan afuera, y no
    ''' por olvido: <c>Race</c>, <c>Enchantment</c>, <c>SoundPickUp/PutDown</c>, <c>EquipmentType</c>,
    ''' <c>BlockBashImpactDataSet</c>, <c>AlternateBlockMaterial</c>, <c>PreviewTransform</c>,
    ''' <c>InstanceNaming</c>, <c>Male/FemaleSkinTexture</c>, <c>*SkinTextureSwapList</c>,
    ''' <c>FootstepSound</c>, <c>ArtObject</c> y las keywords.</para>
    '''
    ''' <para><b>MSWP no rinde NADA, y es correcto.</b> Un material swap no declara ni un campo de
    ''' referencia: sus sustituciones son tres cadenas y un índice de color
    ''' (<c>SubstitutionOriginalMaterial</c>, <c>SubstitutionReplacementMaterial</c>,
    ''' <c>SubstitutionTreeFolderObsolete</c>, <c>SubstitutionColorRemappingIndex</c> —
    ''' <c>WbViews_Interfaces.vb:1895-1903</c>). ⛔ Queda escrito para que nadie lo «arregle»: que el
    ''' remapeo no toque los MSWP no es un hueco, es que no hay nada que remapear.</para>
    '''
    ''' <para><b><c>TemplateArmor</c> entra POR CONSTRUCCIÓN.</b> Hoy no lo alcanza nadie —el clon le
    ''' SACA el <c>TNAM</c> tras materializar, el override lo copia de un record REAL (nunca un 0xFF) y
    ''' no hay selector que escriba uno—, así que remapearlo no cambia ningún byte todavía. Va igual
    ''' porque el criterio de esta lista es <b>qué campo PUEDE apuntar a un borrador según el formato</b>,
    ''' no qué camino de la interfaz existe hoy: el día que alguien agregue el selector de plantilla, el
    ''' remapeo y el censo ya lo cubren, sin que nadie tenga que acordarse.</para>
    '''
    ''' <para>⛔ <b>ESCRITURA DIRECTA, a propósito.</b> Los <c>Poner</c> asignan el campo sin pasar por la
    ''' ley de «poner una referencia». Tres razones: (1) esa ley gobierna el gesto «sin valor ⇒ sacar el
    ''' subrecord / cero significa NINGUNO», y acá el destino <b>nunca</b> es cero por construcción del
    ''' mapa —sólo entran los identificadores reales que resolvieron—, así que esto es una reescritura de
    ''' VALOR sobre un subrecord que ya existe, otro gesto; (2) meter el caso bajo un régimen cuya
    ''' semántica no aplica sería disfrazar la ley, no cumplirla.</para></summary>
    Friend Iterator Function DeBorrador(record As Object) As IEnumerable(Of ReferenciaDeBorrador)
        If record Is Nothing Then Return

        Dim otft = TryCast(record, Canon.IOtft)
        If otft IsNot Nothing Then
            For Each it In otft.Items
                Yield RefDe(it, Function(x) x.Item, Sub(x, v) x.Item = v, "prenda")
            Next
            Return
        End If

        Dim lvli = TryCast(record, Canon.ILvli)
        If lvli IsNot Nothing Then
            For Each en In lvli.LeveledListEntries
                Yield RefDe(en, Function(x) x.LeveledListEntryItem,
                            Sub(x, v) x.LeveledListEntryItem = v, "entrada")
            Next
            Return
        End If

        Dim armo = TryCast(record, Canon.IArmo)
        If armo IsNot Nothing Then
            ' El modelo de addons (INDX+referencia vs. array de referencias) es distinto por juego, y el
            ' material swap a nivel ARMO sólo existe en Fallout 4.
            Dim armoFo4 = TryCast(record, Canon.ArmoFO4)
            Dim armoSse = TryCast(record, Canon.ArmoSSE)
            If armoFo4 IsNot Nothing Then
                For Each mdl In armoFo4.Models
                    Yield RefDe(mdl, Function(x) x.ModelArmorAddon,
                                Sub(x, v) x.ModelArmorAddon = v, "addon")
                Next
                Yield RefDe(armoFo4, Function(x) x.WorldModelMaterialSwap,
                            Sub(x, v) x.WorldModelMaterialSwap = v, "material swap")
                Yield RefDe(armoFo4, Function(x) x.WorldModelMaterialSwap2,
                            Sub(x, v) x.WorldModelMaterialSwap2 = v, "material swap")
            ElseIf armoSse IsNot Nothing Then
                For Each mdl In armoSse.Armature
                    Yield RefDe(mdl, Function(x) x.ModelFilename,
                                Sub(x, v) x.ModelFilename = v, "addon")
                Next
            End If
            ' La plantilla está en la interfaz común: la declaran los dos juegos.
            Yield RefDe(armo, Function(x) x.TemplateArmor, Sub(x, v) x.TemplateArmor = v, "plantilla")
            Return
        End If

        ' ARMA: los CUATRO material swap, que sólo existen en Fallout 4. Skyrim no declara MSWP.
        Dim armaFo4 = TryCast(record, Canon.ArmaFO4)
        If armaFo4 IsNot Nothing Then
            Yield RefDe(armaFo4, Function(x) x.MaleMaterialSwap,
                        Sub(x, v) x.MaleMaterialSwap = v, "material swap")
            Yield RefDe(armaFo4, Function(x) x.FemaleMaterialSwap,
                        Sub(x, v) x.FemaleMaterialSwap = v, "material swap")
            Yield RefDe(armaFo4, Function(x) x.MaleMaterialSwap2,
                        Sub(x, v) x.MaleMaterialSwap2 = v, "material swap 1ra persona")
            Yield RefDe(armaFo4, Function(x) x.FemaleMaterialSwap2,
                        Sub(x, v) x.FemaleMaterialSwap2 = v, "material swap 1ra persona")
        End If
        ' MSWP no rinde nada. Ver el párrafo del doc: no es un hueco.
    End Function

End Module
