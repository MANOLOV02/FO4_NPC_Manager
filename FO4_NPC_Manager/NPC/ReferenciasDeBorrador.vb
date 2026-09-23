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

    ''' <summary>⛔⛔ LAS CLASES QUE TIENEN BORRADOR — <b>LA SEDE</b>, y no una lista mas.
    ''' <para>El parrafo de arriba ya declaraba la ley en PROSA («hoy son OCHO: OTFT, LVLI, ARMO, ARMA,
    ''' MSWP y, desde la ola de head parts, HDPT, TXST y FLST») y eso no alcanzo: un gate se escribio su
    ''' propia copia con CINCO y quedo atras cuando TXST y FLST recibieron borrador el 20-sep. MEDIDO el
    ''' 21-sep: `OutfitDraftSaveGate` derivaba 4 campos del ARMA de Fallout contra los 8 que el censo de
    ''' aca abajo rinde, y en Skyrim derivaba 0 contra 4. Un comentario no lo consume nadie; esto si.</para>
    ''' <para>⛔ El que la necesite la LEE. Copiarla es volver a tener dos listas, que es el defecto que
    ''' el propio doc de <see cref="DeBorrador"/> dice venir a cerrar: «Dos listas se separan; esta no
    ''' puede».</para></summary>
    Friend ReadOnly ClasesConBorrador As New HashSet(Of String)(
        New String() {"OTFT", "LVLI", "ARMO", "ARMA", "MSWP", "HDPT", "TXST", "FLST"},
        StringComparer.Ordinal)

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
    ''' <para><b>Sólo entran las clases que TIENEN borrador</b> — hoy son OCHO: OTFT, LVLI, ARMO,
    ''' ARMA, MSWP y, desde la ola de head parts, <b>HDPT, TXST y FLST</b>.</para>
    '''
    ''' <para>⚠️ <b>ACÁ HABÍA UNA RAZÓN QUE DEJÓ DE SER VERDAD, y por eso queda escrita.</b> Este párrafo
    ''' decía que <c>Male/FemaleSkinTexture</c> y <c>*SkinTextureSwapList</c> quedaban afuera «porque de
    ''' esos no hay borrador que crear» — o sea que la exclusión no era un olvido sino una consecuencia
    ''' de que TXST y FLST no tuvieran borrador. <b>Ahora lo tienen</b> (decisión del usuario, 20-sep),
    ''' así que la consecuencia se dio vuelta y los cuatro campos ENTRAN. El resto sigue afuera por la
    ''' razón original, que no cambió: <c>Race</c>, <c>Enchantment</c>, <c>SoundPickUp/PutDown</c>,
    ''' <c>EquipmentType</c>, <c>BlockBashImpactDataSet</c>, <c>AlternateBlockMaterial</c>,
    ''' <c>PreviewTransform</c>, <c>InstanceNaming</c>, <c>FootstepSound</c>, <c>ArtObject</c> y las
    ''' keywords — de ninguno de esos records hay borrador.</para>
    '''
    ''' <para><b>Y entran también las <i>Alternate Textures</i> de Skyrim</b> (<c>MODS</c> de ARMA y de
    ''' ARMO: <c>Alternate Texture\New Texture → TXST</c>). Hoy <b>ningún selector las escribe</b>, igual
    ''' que <c>TemplateArmor</c>: van por el mismo criterio declarado arriba —qué campo PUEDE apuntar a un
    ''' borrador <b>según el formato</b>, no qué camino de la interfaz existe— así que el día que aparezca
    ''' el selector, el remapeo y el censo ya lo cubren. En Fallout 4 ese campo es un material swap (otra
    ''' cosa) y ya estaba.</para>
    '''
    ''' <para><b>ARMO no apunta a TXST en Fallout 4.</b> Medido sobre el esquema: el <c>TNAM</c> de un ARMO
    ''' es la PLANTILLA (otro ARMO), no un conjunto de texturas, y las tres referencias a TXST que declara
    ''' el ARMO de Skyrim son las de <i>Alternate Textures</i> del párrafo anterior. Queda escrito porque
    ''' es fácil suponer la arista por analogía con ARMA, que sí la tiene.</para>
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
                ' Alternate Textures (MODS) → TXST. Ningún selector las escribe hoy; entran por formato.
                For Each at In armoSse.AlternateTextures
                    Yield RefDe(at, Function(x) x.AlternateTextureNewTexture,
                                Sub(x, v) x.AlternateTextureNewTexture = v, "textura alternativa")
                Next
            End If
            ' La plantilla está en la interfaz común: la declaran los dos juegos.
            Yield RefDe(armo, Function(x) x.TemplateArmor, Sub(x, v) x.TemplateArmor = v, "plantilla")

            ' ======================================================================================
            ' OBTS — las propiedades de Object Template. Sólo Fallout 4: medido, `PropertyValue1FormID`
            ' da CERO matches en `WbViews_TES5.vb`.
            '
            ' ⛔⛔ EL CAMINO ES `Combinations → Properties2`, Y LOS NOMBRES ESTÁN HECHOS PARA CONFUNDIR.
            ' MEDIDO en las vistas generadas:
            '   · `ArmoFO4_Combinations.Properties2` → `ArmoFO4_Properties2`, que implementa
            '     `IBloque_Properties4` y SÍ declara `PropertyValue1FormID`. Es el de OBTS.
            '   · `ArmoFO4_Combinations.Properties` es PRIVADO (`PropertiesDeLaForma`, sólo por la
            '     interfaz), así que sobre el tipo concreto ni existe.
            '   · Y `ArmoFO4_Properties` —el de la RAÍZ del record, sin el 2— da CERO matches de
            '     `PropertyValue1FormID`: es otro bloque.
            ' Enfilar el equivocado no falla por sí solo: devolvería NADA y la rama quedaría muerta con
            ' el gate en verde. Por eso el walk es TIPADO y no una ruta de texto — equivocarlo es error
            ' de compilación, que es como se encontró esto.
            '
            ' ⛔ EL CRITERIO ES LA RAMA MATERIALIZADA, NO EL DISCRIMINADOR, y no es preferencia: es lo
            ' que se EMITE. `WbUnionDef.Emit` (WbValueDefs.vb:1283-1287) escribe `node.Children(0)`, y
            ' el árbol se materializa al parsear/crear y NO se re-materializa cuando alguien escribe el
            ' discriminador (`WbEdit.RamaQueElDecisorElige`, WbEdit.vb:348-354: «leer el hijo devuelve
            ' esa rama RANCIA»; arreglar las 161 uniones es ola aparte, decisión del usuario). Una rama
            ' de FormID materializada con un fid de borrador SE ESCRIBE aunque el discriminador ya diga
            ' otra cosa.
            '
            ' ⛔ Y mirar ADEMÁS el discriminador (`Value Type ∈ {FormIDInt, FormIDFloat}`) no agrega
            ' NADA: `Presente` y la lectura resuelven por el MISMO `Find` (CanonBridge.vb:170-183), así
            ' que sin la rama materializada la lectura da 0 y `RemapearUno` lo saltea
            ' (Borradores.vb:408). Sería una rama que ningún mutante puede matar.
            ' ======================================================================================
            ' ⛔ EL OTRO DUEÑO DE UN OBJECT TEMPLATE ES EL NPC_, Y QUEDA AFUERA — MEDIDO, NO SUPUESTO.
            ' `NpcFO4` declara `Combinations` → `Properties3` → `NpcFO4_Properties3`, que implementa el
            ' MISMO `IBloque_Properties4` y por lo tanto también tiene `PropertyValue1FormID`. No entra
            ' por dos razones medidas, y las dos tienen que seguir siendo ciertas:
            '   1. un NPC_ NO es una de las ocho clases con borrador — este censo recorre records de
            '      borrador, y lo que la app le cambia a un NPC_ va por `NpcRecordOverlay`, cuyos campos
            '      de FormID se censan y se remapean aparte (`MainForm.PromoteSavedDrafts`);
            '   2. la app NUNCA escribe ahí: `Properties3`, `AgregarCombinations` y `NpcFO4_Combinations`
            '      dan CERO matches en `FO4_NPC_Manager` y en `Wardrobe_Manager`. No hay editor que pueda
            '      meter un 0xFF en esa rama.
            ' ⛔ Si mañana aparece un editor de object template del NPC_, esto se cae y la arista se agrega
            ' ACÁ — no en el editor nuevo.
            If armoFo4 IsNot Nothing Then
                For Each comb In armoFo4.Combinations
                    For Each p In comb.Properties2
                        If p.PropertyValue1FormIDPresente Then
                            Yield RefDe(p, Function(x) x.PropertyValue1FormID,
                                        Sub(x, v) x.PropertyValue1FormID = v, "object template property")
                        End If
                    Next
                Next
            End If
            Return
        End If

        ' ARMA — las texturas de skin y sus swap-list están en la interfaz COMÚN: las declaran los dos
        ' juegos, y desde la ola de head parts TXST y FLST tienen borrador, así que son aristas reales.
        Dim arma = TryCast(record, Canon.IArma)
        If arma IsNot Nothing Then
            Yield RefDe(arma, Function(x) x.MaleSkinTexture, Sub(x, v) x.MaleSkinTexture = v, "textura de piel")
            Yield RefDe(arma, Function(x) x.FemaleSkinTexture, Sub(x, v) x.FemaleSkinTexture = v, "textura de piel")
            Yield RefDe(arma, Function(x) x.MaleSkinTextureSwapList,
                        Sub(x, v) x.MaleSkinTextureSwapList = v, "lista de swap de piel")
            Yield RefDe(arma, Function(x) x.FemaleSkinTextureSwapList,
                        Sub(x, v) x.FemaleSkinTextureSwapList = v, "lista de swap de piel")
            Dim armaSse = TryCast(record, Canon.ArmaSSE)
            If armaSse IsNot Nothing Then
                ' Idem ARMO: por formato, sin selector todavía.
                For Each at In armaSse.AlternateTextures
                    Yield RefDe(at, Function(x) x.AlternateTextureNewTexture,
                                Sub(x, v) x.AlternateTextureNewTexture = v, "textura alternativa")
                Next
            End If
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

        ' ============================================================================
        ' HDPT — la ola de head parts. Cinco campos de referencia, y uno de ellos apunta a su PROPIA
        ' clase: ⛔ <c>HNAM</c> (Extra Parts) es HDPT→HDPT, UNA arista de este censo que puede
        ' (⚠️ no la primera: <c>LVLI</c> ya la tenia por su <c>LVLO\Item</c>, declarado sin firma, y su
        ' clausura con visitados de NpcOverrideSaver.vb:891-899 es el molde a copiar)
        ' formar un ciclo. Medido (`Tools\censo_hdpt.py`): 2.254 de los 2.546 HDPT de Fallout 4 (89 %) declaran al menos un
        ' extra, hasta 5. Quien recorra estas referencias para hacer una CLAUSURA tiene que llevar
        ' conjunto de visitados — el recorrido de la armadura no lo necesitaba y lo dice en su propio
        ' comentario («no ARMO→ARMO edge exists»).
        ' ============================================================================
        Dim hdpt = TryCast(record, Canon.IHdpt)
        If hdpt IsNot Nothing Then
            For Each ex In hdpt.ExtraParts
                Yield RefDe(ex, Function(x) x.Part, Sub(x, v) x.Part = v, "parte extra")
            Next
            Yield RefDe(hdpt, Function(x) x.TextureSet, Sub(x, v) x.TextureSet = v, "conjunto de texturas")
            Yield RefDe(hdpt, Function(x) x.ValidRaces, Sub(x, v) x.ValidRaces = v, "razas válidas")
            Yield RefDe(hdpt, Function(x) x.Color, Sub(x, v) x.Color = v, "color")
            ' El material swap del modelo sólo existe en Fallout 4 (en Skyrim ese MODS es un arreglo de
            ' texturas alternativas, que se enumera abajo).
            Dim hdptFo4 = TryCast(record, Canon.HdptFO4)
            If hdptFo4 IsNot Nothing Then
                Yield RefDe(hdptFo4, Function(x) x.ModelMaterialSwap,
                            Sub(x, v) x.ModelMaterialSwap = v, "material swap")
            End If
            Dim hdptSse = TryCast(record, Canon.HdptSSE)
            If hdptSse IsNot Nothing Then
                For Each at In hdptSse.AlternateTextures
                    Yield RefDe(at, Function(x) x.AlternateTextureNewTexture,
                                Sub(x, v) x.AlternateTextureNewTexture = v, "textura alternativa")
                Next
            End If
            Return
        End If

        ' FLST — <c>LNAM</c> es un arreglo de FormID SIN firma declarada en el esquema
        ' (<c>Wb.Fid("FormID")</c>): la lista puede contener CUALQUIER record, así que por el criterio de
        ' este censo —qué puede apuntar a un borrador según el formato— entra entera. El uso que trae
        ' esta ola son razas (el <c>RNAM</c> de un head part) y de RACE no hay borrador, pero el filtro a
        ' RACE es de la UI y no del record.
        Dim flst = TryCast(record, Canon.IFlst)
        If flst IsNot Nothing Then
            For Each en In flst.FormIDs
                Yield RefDe(en, Function(x) x.FormID, Sub(x, v) x.FormID = v, "miembro de la lista")
            Next
            Return
        End If

        ' TXST no rinde NADA, y es correcto: sus campos son ocho rutas de textura, el bloque de decal,
        ' las banderas, el material y OBND — ni un FormID. ⛔ Queda escrito por lo mismo que MSWP: que el
        ' remapeo no toque los TXST no es un hueco, es que no hay nada que remapear.
        Dim txst = TryCast(record, Canon.ITxst)
        If txst IsNot Nothing Then Return

        ' MSWP no rinde nada. Ver el párrafo del doc: no es un hueco.
    End Function

    ''' <summary>«ESTA CLASE NO TIENE SEGUNDA CASA» — dicho, no omitido.
    ''' <para>⛔ <c>NpcOverrideSaver.ExigirReferenciasSinColgar</c> pedía la segunda casa (los picks
    ''' SELLADOS del atuendo) con un <c>Optional … = Nothing</c>, y siete de sus ocho llamadas la omitían
    ''' en silencio. Un parámetro opcional que significa «no mires esa casa» es exactamente cómo se llega
    ''' a tener una casa sin mirar: pasó una vez —el ARMO al que sólo apuntaba un pick salía «no lo
    ''' referencia nadie»— y la ola siguiente encontró una TERCERA casa por el mismo camino.</para>
    ''' <para>Con el parámetro obligatorio, un llamador nuevo tiene que ESCRIBIR qué pasa, y escribir
    ''' esto es declarar que lo pensó.</para></summary>
    Friend ReadOnly SinSegundaCasa As IEnumerable(Of ReferenciaDeBorrador) =
        Array.Empty(Of ReferenciaDeBorrador)()

End Module
