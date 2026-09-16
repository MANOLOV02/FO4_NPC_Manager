Imports FO4_Base_Library

''' <summary>UNIFIED preset-category model, shared by Paste Look and by the LooksMenu/RaceMenu preset
''' loader. One enum, one options struct, one "what does this preset actually carry" describer — so the
''' two features can never drift apart in which categories exist, what they mean, or which game shows them.
'''
''' <para>A category = ONE independent appearance pipeline (00-reglas-ui-y-vb "toggles granulares
''' por pipeline"). Checked = take this category from the SOURCE preset; unchecked = keep whatever the
''' TARGET NPC currently shows. The actual merge lives in <see cref="PresetCategoryFilter"/>.</para>
'''
''' <para>Game-awareness: some categories only exist in one engine (MRSV body regions, FMRS face bone
''' regions and the F4SE LM skin template are FO4-only; per-vertex sculpt and RaceMenu body scale are
''' SSE-only). <see cref="AppliesToGame"/> is the single source of truth for that, consumed by the UI to
''' hide rows and by the filter to skip carriers.</para></summary>
Public Module PresetCategories

    ''' <summary>Every independently-toggleable appearance category. Values are stable identifiers used by
    ''' the UI (one checkbox each) and by the filter (one revert branch each).</summary>
    Public Enum PresetCategory
        BodyWeight
        BodyRegions
        BodySliders
        BodyScale
        Overlays
        SkinOverride
        LmSkinTemplate
        Outfit
        FaceParts
        HairColor
        FaceTints
        FaceVertexMorphs
        CustomMorphs
        FaceBoneRegions
        Sculpt
        IsCharGenPreset
    End Enum

    ''' <summary>All categories in display order. Used by the panel to iterate rows and by the filter to
    ''' iterate revert branches — adding a category here (plus its Designer row) is the only place a new
    ''' pipeline has to be registered.</summary>
    Public ReadOnly AllCategories As PresetCategory() = {
        PresetCategory.BodyWeight, PresetCategory.BodyRegions, PresetCategory.BodySliders,
        PresetCategory.BodyScale, PresetCategory.Overlays, PresetCategory.SkinOverride,
        PresetCategory.LmSkinTemplate, PresetCategory.Outfit,
        PresetCategory.FaceParts, PresetCategory.HairColor, PresetCategory.FaceTints,
        PresetCategory.FaceVertexMorphs, PresetCategory.CustomMorphs, PresetCategory.FaceBoneRegions, PresetCategory.Sculpt,
        PresetCategory.IsCharGenPreset
    }

    ''' <summary>True if the category exists in the given game. FO4-only: MRSV body regions, FMRS face
    ''' bone regions, F4SE LM skin template. SSE-only: per-vertex sculpt, RaceMenu node-transform body
    ''' scale. Everything else is shared (record fields or an equivalent carrier in both engines).</summary>
    Public Function AppliesToGame(cat As PresetCategory, isSse As Boolean) As Boolean
        Select Case cat
            Case PresetCategory.BodyRegions, PresetCategory.FaceBoneRegions, PresetCategory.LmSkinTemplate
                Return Not isSse
            Case PresetCategory.Sculpt, PresetCategory.BodyScale, PresetCategory.CustomMorphs
                Return isSse
            Case Else
                Return True
        End Select
    End Function

    ''' <summary>¿esta categoría la HEREDA un NPC por el bucket Traits (ACBS Template Flags, bit 0)?
    '''
    ''' <para>⛔ El ancla NO es <c>TraitsState</c> — FaceTints, FaceVertexMorphs, FaceBoneRegions y BodyRegions
    ''' no están ahí. Es la función de copia del bucket 0 en los dos binarios, canal por canal.</para>
    '''
    ''' <list type="table">
    ''' <item><term>FaceParts (PNAM)</term><description>✅ los dos juegos</description></item>
    ''' <item><term>HairColor (HCLF)</term><description>✅ los dos</description></item>
    ''' <item><term>SkinOverride (WNAM)</term><description>✅ los dos</description></item>
    ''' <item><term>BodyWeight (NAM7 en SSE, MWGT en FO4)</term><description>✅ los dos</description></item>
    ''' <item><term>FaceVertexMorphs (NAM9 en SSE, MSDK/MSDV en FO4)</term><description>✅ los dos</description></item>
    ''' <item><term>FaceTints</term><description>⚠️ GAME-AWARE: en SSE el bit 0 NO copia TINI (REFUTADO, con control
    ''' de sujeto sobre offsets que sí se copian); en FO4 SÍ copia TETI/TEND (<c>0x140651752</c>) y además con
    ''' REEMPLAZO INCLUIDO EL VACÍO — <c>0x1406516D9</c> pregunta <c>cmp qword [r13+0x300], 0</c> y, si la
    ''' plantilla no trae tintes, <c>0x140651759</c>-<c>0x14065179B</c> LIBERA el contenedor del heredero. Un
    ''' heredero de FO4 cuya plantilla no tiene tintes NO conserva los suyos: los pierde.</description></item>
    ''' <item><term>BodyRegions (MRSV)</term><description>⛔ NO hereda: el motor no lo copia bajo el bit 0. La app
    ''' sí lo copiaba, y eso sale como copia de más en el gate del bucket.</description></item>
    ''' <item><term>FaceBoneRegions</term><description>⛔ PARTIDA, y por eso devuelve False: el bit 0 copia
    ''' FMRI/FMRS pero NO copia FMIN. Como la categoría de preset es UNA sola y arrastra las dos mitades
    ''' (regiones + intensidad), heredarla entera copiaría de más.</description></item>
    ''' </list>
    ''' <para>Todo lo demás (Outfit, BodySliders, Overlays, BodyScale, LmSkinTemplate, CustomMorphs, Sculpt,
    ''' IsCharGenPreset) queda FUERA: o es de otro bucket, o no tiene portador en el record, o no hay cita.</para></summary>
    ''' <summary>⛔⛔ EL DEFAULT POR CATEGORIA -- privado, y lo consume SOLO `HeredaElCanal`.
    ''' <para>Es la mitad de la tabla que no depende del canal. La publica es `HeredaElCanal`, que es la
    ''' granularidad a la que decide el MOTOR; y `HeredaPorTraits` se DERIVA de ella. Con dos tablas
    ''' escritas a mano habia dos respuestas para la misma pregunta, y el filtro del guardado uso la
    ''' gruesa: borro al guardar la intensidad de morfo facial de Fallout y las capas de tinte de Skyrim,
    ''' que el usuario habia editado y que el bit 0 NO copia.</para></summary>
    ''' <para>⛔ Los renglones de `FaceTints` y `FaceBoneRegions` SALIERON: `HeredaElCanal` los
    ''' resuelve por canal antes de caer aca, asi que eran INALCANZABLES -- y un segundo lugar que
    ''' "sabe" esas dos respuestas es justo la duplicacion que esta ola vino a cerrar.</para></summary>
    Private Function DefaultDeLaCategoria(cat As PresetCategory, isSse As Boolean) As Boolean
        Select Case cat
            Case PresetCategory.FaceParts, PresetCategory.HairColor, PresetCategory.SkinOverride,
                 PresetCategory.BodyWeight, PresetCategory.FaceVertexMorphs
                Return True
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>⛔⛔ LA MISMA PREGUNTA, PERO POR CANAL: ¿el bucket Traits copia ESTE campo?
    ''' <para>Existe porque el motor decide por CAMPO y dos categorias de la app mezclan campos con respuestas
    ''' distintas. `HeredaPorTraits` contesta "¿hereda alguno?" y sirve de guarda de bucle; esta contesta lo que
    ''' de verdad hay que saber para comparar un canal o revertirlo.</para>
    '''
    ''' <para>⛔ `FaceTints` / `SkinToneOffset` (QNAM) -- hereda en LOS DOS. Medido: SSE `sub_1403BDFC0` copia
    ''' +0x246/+0x247/+0x248 en 0x1403BE09A, 0x1403BE0AE y 0x1403BE0BB, sin test; FO4 `sub_140651470` copia
    ''' +0x2EA-0x2ED en 0x140651552, 0x140651560, 0x14065156E y 0x14065157C. Las CAPAS, en cambio, solo las
    ''' copia FO4: el censo de `sub_1403BDFC0` no toca +0x260 en ninguna instruccion.</para>
    '''
    ''' <para>⛔ `FaceBoneRegions` / `FacialMorphIntensity` (FMIN) -- NO hereda.</para>
    '''
    ''' <para>⛔⛔ LA EVIDENCIA ES LA DE LA TABLA LATERAL, NO LA DE LOS PARES. Aca se citaba "el censo
    ''' de pares origen->destino no trae un solo `movss`", y eso NO PRUEBA NADA para este campo: el FMIN
    ''' no vive en `TESNPC` sino en una TABLA GLOBAL, asi que un censo de pares es <b>ciego por
    ''' construccion</b> -- habria dicho "no se copia" aunque se copiara. Es la misma trampa que este
    ''' mismo materializador ya describe para el `ATKT`.</para>
    '''
    ''' <para>La pregunta que SI contesta es «¿alguien del camino del bit 0 toca la tabla?», y se midio
    ''' por LLAMADORES de las tres funciones de la tabla: `sub_140654DC0` &lt;- solo 0x14064F230 (el loader
    ''' del `NPC_`); `sub_1406637B0` &lt;- 0x140650FE0 y el setter; `sub_140667430` &lt;- 0x14064EF50,
    ''' 0x140650FE0 y el setter. Por RTTI, 0x140650FE0 es el slot 54 de `TESNPC` (`Copy`) y 0x14064EF50
    ''' el slot 8 (un Clear). Interseccion con los callees de `sub_1406580A0` + `sub_140651470` a
    ''' profundidad 2 (55 + 64 funciones): <b>VACIA</b>; y ninguna de las dos referencia 0x142F092B8 ni
    ''' 0x142F092B0. Los indirectos del bloque del bit 0 son los slots 13/68/70/76 y el slot 5 de tres
    ''' componentes -- ninguno es el 54 ni el 8.</para>
    '''
    ''' <para>COBERTURA DECLARADA: llamadas `E8` directas + resolucion de los tres llamadores por
    ''' vtable. NO se siguieron las indirectas dos niveles adentro.</para>
    '''
    ''' <para>⛔ El default es `HeredaPorTraits`: una categoria nueva no necesita renglon aca, y si lo necesita
    ''' es porque mezcla campos -- que es justo lo que hay que ver.</para></summary>
    Public Function HeredaElCanal(cat As PresetCategory, canal As String, isSse As Boolean) As Boolean
        Select Case cat
            Case PresetCategory.FaceTints
                If String.Equals(canal, "SkinToneOffset", StringComparison.Ordinal) Then Return True
                Return Not isSse
            Case PresetCategory.FaceBoneRegions
                If String.Equals(canal, "FacialMorphIntensity", StringComparison.Ordinal) OrElse
                   String.Equals(canal, "HasFacialMorphIntensity", StringComparison.Ordinal) Then Return False
                Return Not isSse
            Case Else
                Return DefaultDeLaCategoria(cat, isSse)
        End Select
    End Function

    ''' <summary>⛔⛔ ¿HEREDA ALGUNO DE LOS CANALES DE ESTA CATEGORIA? Se DERIVA de `HeredaElCanal`,
    ''' que es la unica tabla. Antes era una segunda tabla escrita a mano y las dos se contradecian: el motor
    ''' decide por CAMPO y dos categorias de la app mezclan campos con respuestas distintas.
    ''' <para>Sirve de guarda de bucle -- "¿me interesa mirar esta categoria?" -- y NADA MAS. Para decidir
    ''' sobre un campo concreto (comparar, revertir, filtrar al guardar) hay que preguntar por CANAL: usar
    ''' esta ahi fue exactamente el defecto que borro ediciones del usuario al guardar.</para></summary>
    Public Function HeredaPorTraits(cat As PresetCategory, isSse As Boolean) As Boolean
        Dim alguno As Boolean = False
        PresetCategoryFilter.PorCanal(cat, isSse,
                                      Sub(nombre, leerCanal, escribirCanal)
                                          If HeredaElCanal(cat, nombre, isSse) Then alguno = True
                                      End Sub)
        ' Una categoria sin canales en la tabla no hereda nada: no hay campo que el bit 0 pueda pisar.
        Return alguno
    End Function

    ''' <summary>⛔⛔ LA PREGUNTA DE LA PUERTA (punto 1, DECISIONES 14-sep): con el bit 0 arriba, ¿una edicion de
    ''' ESTE canal se PIERDE en el juego? Se pierde por DOS caminos, y `HeredaElCanal` contesta solo el primero.
    ''' <para>(1) El motor la PISA: el bucket Traits copia el campo desde la plantilla. Es `HeredaElCanal`.</para>
    ''' <para>(2) El motor NO la LEE: el canal no viaja en el record que el juego usa para dibujar sino en el
    ''' FaceGen horneado, y con el bit 0 arriba el FaceGen que se abre es el del ULTIMO eslabon, no el del
    ''' heredero. SSE arma la ruta con el FormID del eslabon de `+0x1F0` (0x1403C2E20, 0x1403BFCA0) y, si el
    ''' NIF falta, arma la cabeza SIN tinte (0x1403C513E, 0x1404335ED); las capas TINI ni se cargan salvo con
    ''' ACBS 0x04 o `bUseFaceGenPreprocessedHeads`=0 (0x1403BCA8F).</para>
    ''' <para>⛔ Existe porque la puerta preguntaba solo (1): un heredero de Skyrim al que se le editaban capas
    ''' de tinte, sculpt o custom morphs NO desprendia, el ESP salia con el bit arriba y el juego seguia
    ''' mostrando la cara horneada de la plantilla.</para>
    ''' <para>⛔ `HeredaElCanal` SIGUE siendo la del Revert y la del guardado (`AutoriaParaGuardado`): esas
    ''' preguntan "¿el bucket copia este campo?", no "¿se ve la edicion?".</para>
    ''' <para>⛔⛔ SIN EXCEPCION POR ACBS 0x04 (DECISIONES 1d, rev-15). Hubo un parametro `cargaLaCaraEnRuntime`
    ''' que, con 0x04, sacaba las capas de la lista. Era una ley FALSA: que el loader lea las TINI con 0x04
    ''' (0x1403BCA8F) no hace que la cara se arme con ellas. La composicion del tinte en runtime corre solo con
    ''' `bUseFaceGenPreprocessedHeads` == 0 (0x1404335ED: `cmp byte [rip+...], 0` / `jne` saltea el armado), y la
    ''' cabeza runtime solo con esa opcion en 0 o para el jugador (0x140433B7C). Con el valor de distribucion la
    ''' cara de un NPC con 0x04 TAMBIEN sale del FaceGen del ultimo eslabon, asi que sus tintes desprenden igual.</para></summary>
    Public Function DesprendeElCanal(cat As PresetCategory, canal As String, isSse As Boolean) As Boolean
        If HeredaElCanal(cat, canal, isSse) Then Return True
        Return SoloLlegaPorElFaceGenHorneado(cat, canal, isSse)
    End Function

    ''' <summary>⛔ ¿Hay ALGUN canal de esta categoria cuya edicion desprende? Se DERIVA de
    ''' <see cref="DesprendeElCanal"/>, igual que `HeredaPorTraits` de `HeredaElCanal`: es la guarda de bucle
    ''' de la puerta, y una segunda tabla escrita a mano seria una contradiccion esperando pasar.</summary>
    Public Function DesprendePorTraits(cat As PresetCategory, isSse As Boolean) As Boolean
        Dim alguno As Boolean = False
        PresetCategoryFilter.PorCanal(cat, isSse,
                                      Sub(nombre, leerCanal, escribirCanal)
                                          If DesprendeElCanal(cat, nombre, isSse) Then alguno = True
                                      End Sub)
        Return alguno
    End Function

    ''' <summary>⛔ Los canales que llegan al juego SOLO por el FaceGen horneado, y por lo tanto son
    ''' invisibles para un heredero con el bit 0 arriba (ver <see cref="DesprendeElCanal"/>).
    ''' <para>La lista NO sale de memoria: sale de lo que el HORNEADO de la app mete en el FaceGen y de que
    ''' ninguna otra via lo entrega. Verificado en el codigo:</para>
    ''' <list type="bullet">
    ''' <item><term>SSE FaceTints (capas)</term><description>`SseTintLayers` + `SseTintTexOverride` (+ su marca
    ''' `HasSseTints`) componen el `.dds` de FaceTint: `FaceGenBuilder` los pasa a `SseFaceGenBaker.BakeFaceTintDds`
    ''' (`SseFaceTintComposer.CapasDeTinteSse(npcData.Record)` + `npcData.SseTintTexOverride`). `SkinToneOffset`
    ''' NO esta aca: es QNAM y ya lo contesta `HeredaElCanal` (0x1403BE09A).</description></item>
    ''' <item><term>SSE Sculpt</term><description>canal `RaceMenuSculpt` del plan de morfos, que el horneado
    ''' aplica al NIF SIEMPRE (`FaceGenBuildPipeline.ApplyChargenMorphsInPlace` → `NpcMorphResolver.BuildFaceMorphPlan`
    ''' con `applySculpt` y `applyChargenMorphs` en su default True: headless no hay interruptor). El apply-script
    ''' no lo emite (`NpcApplyScriptEmitter`: "morphs de cara, sculpt y TINTS de cara ... se hornea en el
    ''' FaceGen").</description></item>
    ''' <item><term>SSE CustomMorphs</term><description>mismo plan, canal por slider, SOLO con
    ''' `NpcMorphResolver.ExtendedMorphsEnabled` (skee64 FaceMorphInterface.cpp:1126/:1204 cortan antes del
    ''' bucle con `bExtendedMorphs=0`): apagado, el canal no llega ni horneado ni en runtime, y desprender no
    ''' lo haria visible.</description></item>
    ''' </list>
    ''' <para>⛔ FO4 devuelve False SIEMPRE, y no por omision: FMIN y MRSV —los dos canales de cara/cuerpo que
    ''' el bit 0 no copia— se aplican en RUNTIME desde el NPC propio (QueuedHead 0x140652420, 0x14065F600), asi
    ''' que se ven sin desprender. Que el horneado FO4 los meta en el NIF no cambia nada: con el bit 0 arriba el
    ''' NIF del heredero no se abre (0x140658E80; con preprocesado y NIF de la raiz no se encola nada,
    ''' 0x1406E2625).</para>
    ''' <para>⛔ HUECO DECLARADO: los overlays de CARA de SSE tambien se hornean cuando
    ''' `Setting_BakeSseRaceMenuOverlays` esta prendido, pero viajan en `SseBodyOverlays` junto con los de
    ''' cuerpo, que el apply-script SI entrega. La tabla de canales no separa nodos de cara de nodos de cuerpo,
    ''' asi que ese caso NO esta aca. Hace falta un canal propio para los nodos de cara.</para>
    ''' <para>⛔ HUECO DECLARADO: la app no lee `bUseFaceGenPreprocessedHeads` del INI del jugador; la tabla
    ''' contesta para el valor con el que se distribuye el juego.</para></summary>
    Private Function SoloLlegaPorElFaceGenHorneado(cat As PresetCategory, canal As String, isSse As Boolean) As Boolean
        If Not isSse Then Return False
        Select Case cat
            Case PresetCategory.FaceTints
                ' ⛔ Con o sin ACBS 0x04: el tinte runtime solo se compone con `bUseFaceGenPreprocessedHeads` == 0
                ' (0x1404335ED) y la cabeza runtime solo con la opcion en 0 o para el jugador (0x140433B7C).
                Select Case canal
                    Case "SseTintLayers", "SseTintTexOverride", "HasSseTints" : Return True
                    Case Else : Return False
                End Select
            Case PresetCategory.Sculpt
                Return String.Equals(canal, "SseSculptHead", StringComparison.Ordinal) OrElse
                       String.Equals(canal, "SseSculptParts", StringComparison.Ordinal)
            Case PresetCategory.CustomMorphs
                Return NpcMorphResolver.ExtendedMorphsEnabled AndAlso
                       String.Equals(canal, "SseCustomMorphs", StringComparison.Ordinal)
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>⛔⛔ ¿Este canal lleva un VALOR que el bit 0 puede pisar? Es la pregunta de LA PUERTA,
    ''' y NO es la misma que <see cref="HeredaElCanal"/>.
    ''' <para>Un preset lleva, además de los valores, banderas de CONTABILIDAD DE LA APP: las `Has*` de
    ''' posesión, `HeadPartFormIDsIncludeRawExtras` (dice si la lista ya es un superset del PNAM crudo, para
    ''' que el saver no re-una) y `SseHeadPartsFiltradasPorMotor` (diagnóstico). El motor NO TIENE ninguno de
    ''' esos campos, así que no puede destruirlos al copiar el bucket: authorearlos no desprende a nadie.</para>
    ''' <para>⛔ Sin esto, «Edit Face → OK sin tocar nada» disparaba el aviso SIEMPRE: el editor pone
    ''' `HeadPartFormIDsIncludeRawExtras = True` y `Revert` lo deja en False, así que el comparador decía
    ''' «distinto» sin que hubiera un solo valor distinto.</para>
    ''' <para>⛔ `HeredaElCanal` sigue contestando lo otro —«¿el bucket copia este campo?»— y la usa el
    ''' REVERT, que SÍ necesita poner los `Has*` para que <c>AplicarOverlay</c> ESCRIBA en vez de preservar.
    ''' Son dos preguntas distintas sobre la misma tabla de canales, no dos dueños de una.</para></summary>
    Public Function EsCanalDeValor(canal As String) As Boolean
        If canal Is Nothing Then Return False
        If canal.StartsWith("Has", StringComparison.Ordinal) Then Return False
        Select Case canal
            Case "HeadPartFormIDsIncludeRawExtras", "SseHeadPartsFiltradasPorMotor" : Return False
            Case Else : Return True
        End Select
    End Function

    ''' <summary>Per-category boolean flags. True = take the field from the SOURCE preset; False = keep the
    ''' TARGET NPC's current value. Categories that don't apply to the running game are ignored by the
    ''' filter regardless of their flag.</summary>
    Public Structure PresetCategoryOptions
        Public BodyWeight As Boolean         ' FO4: WeightThin/Muscular/Fat (MWGT) — SSE: SseWeight (NAM7)
        Public BodyRegions As Boolean        ' BodyMorphValues (MRSV) — FO4-only
        Public BodySliders As Boolean        ' BodySlide vertex morphs (BodyMorphSliders / BodyMorphsKeyed)
        Public BodyScale As Boolean          ' SseNodeTransforms (RaceMenu NiOverride) — TRS COMPLETO por hueso
        '                                     (escala + posición + rotación), no sólo escala. SSE-only.
        Public Overlays As Boolean           ' Body tattoos/paint (FO4 Overlays — SSE SseBodyOverlays + SseSkinOverrides)
        Public SkinOverride As Boolean       ' SkinFormIDOverride (NPC.WNAM record skin)
        Public LmSkinTemplate As Boolean     ' SkinTemplateId (F4SE LM SkinInterface) — FO4-only
        Public Outfit As Boolean             ' DefaultOutfitFormIDOverride + SleepOutfitFormIDOverride (DOFT/SOFT)
        Public FaceParts As Boolean          ' HeadPartFormIDs (+ SSE head FTST override)
        Public HairColor As Boolean          ' HairColorFormID (+ SSE RaceMenu custom RGB)
        Public FaceTints As Boolean          ' FO4 FaceTintLayers — SSE SseTintLayers + mask textures
        Public FaceVertexMorphs As Boolean   ' FO4 ChargenFaceMorphs (MSDV) — SSE NAM9/NAMA (record-backed)
        Public CustomMorphs As Boolean       ' SseCustomMorphs (RaceMenu NiOverride, no record source) — SSE-only
        Public FaceBoneRegions As Boolean    ' FaceBoneRegions (FMRS) + FacialMorphIntensity — FO4-only
        Public Sculpt As Boolean             ' SseSculptHead/SseSculptParts (per-vertex) — SSE-only
        Public IsCharGenPreset As Boolean    ' ACBS bit 0x04

        ''' <summary>Read one flag by category.</summary>
        Public Function Value(cat As PresetCategory) As Boolean
            Select Case cat
                Case PresetCategory.BodyWeight : Return BodyWeight
                Case PresetCategory.BodyRegions : Return BodyRegions
                Case PresetCategory.BodySliders : Return BodySliders
                Case PresetCategory.BodyScale : Return BodyScale
                Case PresetCategory.Overlays : Return Overlays
                Case PresetCategory.SkinOverride : Return SkinOverride
                Case PresetCategory.LmSkinTemplate : Return LmSkinTemplate
                Case PresetCategory.Outfit : Return Outfit
                Case PresetCategory.FaceParts : Return FaceParts
                Case PresetCategory.HairColor : Return HairColor
                Case PresetCategory.FaceTints : Return FaceTints
                Case PresetCategory.FaceVertexMorphs : Return FaceVertexMorphs
                Case PresetCategory.CustomMorphs : Return CustomMorphs
                Case PresetCategory.FaceBoneRegions : Return FaceBoneRegions
                Case PresetCategory.Sculpt : Return Sculpt
                Case PresetCategory.IsCharGenPreset : Return IsCharGenPreset
                Case Else : Return False
            End Select
        End Function

        ''' <summary>Write one flag by category.</summary>
        Public Sub SetValue(cat As PresetCategory, v As Boolean)
            Select Case cat
                Case PresetCategory.BodyWeight : BodyWeight = v
                Case PresetCategory.BodyRegions : BodyRegions = v
                Case PresetCategory.BodySliders : BodySliders = v
                Case PresetCategory.BodyScale : BodyScale = v
                Case PresetCategory.Overlays : Overlays = v
                Case PresetCategory.SkinOverride : SkinOverride = v
                Case PresetCategory.LmSkinTemplate : LmSkinTemplate = v
                Case PresetCategory.Outfit : Outfit = v
                Case PresetCategory.FaceParts : FaceParts = v
                Case PresetCategory.HairColor : HairColor = v
                Case PresetCategory.FaceTints : FaceTints = v
                Case PresetCategory.FaceVertexMorphs : FaceVertexMorphs = v
                Case PresetCategory.CustomMorphs : CustomMorphs = v
                Case PresetCategory.FaceBoneRegions : FaceBoneRegions = v
                Case PresetCategory.Sculpt : Sculpt = v
                Case PresetCategory.IsCharGenPreset : IsCharGenPreset = v
            End Select
        End Sub

        ''' <summary>All categories on — the legacy "paste everything" / "load everything" behaviour.</summary>
        Public Shared Function All() As PresetCategoryOptions
            Dim o As New PresetCategoryOptions
            For Each c In AllCategories
                o.SetValue(c, True)
            Next
            Return o
        End Function
    End Structure

    ''' <summary>What a given preset actually carries for one category: whether it DECLARES the category at
    ''' all, and how much of it there is. <see cref="Available"/> False means the preset has nothing to give
    ''' (the UI greys the row out) — it is NOT the same as a declared-but-empty category, which is an
    ''' authoritative wipe and stays selectable.</summary>
    Public Class CategoryInfo
        ''' <summary>The preset declares this category (there is something to take).</summary>
        Public Available As Boolean
        ''' <summary>Short right-aligned amount shown next to the checkbox ("12", "yes", "—").</summary>
        Public Text As String = "—"
        ''' <summary>Longer breakdown for the row tooltip. Empty when the short text says it all.</summary>
        Public Detail As String = ""
    End Class

    ''' <summary>Describe every category of <paramref name="p"/> for the running game: what it carries and
    ''' how much. Single source of truth for the counts shown by BOTH the loader panel and the paste panel.
    ''' Nothing in → every category unavailable.</summary>
    Public Function Describe(p As LooksmenuLoader.LooksmenuPreset, isSse As Boolean) As Dictionary(Of PresetCategory, CategoryInfo)
        Dim d As New Dictionary(Of PresetCategory, CategoryInfo)
        For Each c In AllCategories
            d(c) = New CategoryInfo()
        Next
        If p Is Nothing Then Return d

        ' --- Body weight ---
        If isSse Then
            If p.SseWeight.HasValue Then Set0(d, PresetCategory.BodyWeight, $"{p.SseWeight.Value:0}", "NAM7 weight")
        Else
            Dim wn = If(p.WeightThin.HasValue, 1, 0) + If(p.WeightMuscular.HasValue, 1, 0) + If(p.WeightFat.HasValue, 1, 0)
            If wn > 0 Then Set0(d, PresetCategory.BodyWeight, "yes",
                                $"Thin {Fmt(p.WeightThin)} / Muscular {Fmt(p.WeightMuscular)} / Fat {Fmt(p.WeightFat)}")
        End If

        ' --- Body regions (MRSV, FO4-only) ---
        If Not isSse AndAlso (p.HasBodyMorphValues OrElse p.BodyMorphValues.Count > 0) Then
            Set0(d, PresetCategory.BodyRegions, p.BodyMorphValues.Count.ToString(), "MRSV per-region weights")
        End If

        ' --- Body sliders (BodySlide morphs) ---
        Dim sliderCount As Integer = p.BodyMorphSliders.Count
        Dim keyedDetail As String = ""
        If isSse AndAlso p.BodyMorphsKeyed IsNot Nothing Then
            Dim keys As Integer = 0
            For Each kv In p.BodyMorphsKeyed
                If kv.Value IsNot Nothing Then keys += kv.Value.Count
            Next
            sliderCount = p.BodyMorphsKeyed.Count
            keyedDetail = $"{p.BodyMorphsKeyed.Count} morphs across {keys} BodySlide keys"
        End If
        If sliderCount > 0 Then
            Set0(d, PresetCategory.BodySliders, sliderCount.ToString(), keyedDetail)
        ElseIf p.HasBodyMorphSliders Then
            ' Channel present but empty — `"BodyMorphs": null` in a LooksMenu file (f4ee CharGenInterface.cpp
            ' :568-585: the wipe runs when the key has members OR is present-and-null), a .jslot without
            ' bodyMorphs (skee64 PresetInterface.cpp:281 `ClearMorphs` runs UNCONDITIONALLY, outside the
            ' `applyType` if of :283-291 — the mapper declares the channel for every file), or a Copy Look of an
            ' NPC without sliders. An empty channel the engine WRITES has to be tickable, or the filter would
            ' preserve the target's sliders where the engine clears them.
            Set0(d, PresetCategory.BodySliders, "0", "no BodySlide sliders: applying clears the target's")
        End If

        ' --- Body scale (RaceMenu node transforms, SSE-only) ---
        ' Declarado = lista no nula (el mapper siempre la pone para un .jslot; el snapshot de Paste Look sólo si el
        ' NPC la tenía). Vacía y declarada = tickeable: skee64 PresetInterface.cpp:265
        ' `Impl_RemoveAllReferenceTransforms` corre SIEMPRE, fuera del `applyType` if de :267-278.
        If isSse AndAlso p.SseNodeTransforms IsNot Nothing Then
            If p.SseNodeTransforms.Count > 0 Then
                ' DECÍA "NiOverride node transforms": el tooltip re-metía la jerga que se le sacó al rótulo de la tilde.
                Set0(d, PresetCategory.BodyScale, p.SseNodeTransforms.Count.ToString(),
                     $"{p.SseNodeTransforms.Count} bone(s) moved, rotated or resized")
            Else
                Set0(d, PresetCategory.BodyScale, "0", "no bone transforms: applying clears the target's")
            End If
        End If

        ' --- Overlays (+ SSE skin overrides, which ride along as the other body texture layer) ---
        If isSse Then
            Dim ov = If(p.SseBodyOverlays Is Nothing, 0, p.SseBodyOverlays.Count)
            Dim sk = If(p.SseSkinOverrides Is Nothing, 0, p.SseSkinOverrides.Count)
            If ov + sk > 0 Then
                Set0(d, PresetCategory.Overlays, (ov + sk).ToString(), $"{ov} overlay nodes + {sk} skin overrides")
            ElseIf p.SseBodyOverlays IsNot Nothing OrElse p.SseSkinOverrides IsNot Nothing Then
                ' skee64 PresetInterface.cpp:232 `Impl_RemoveAllReferenceNodeOverrides` y :234 `RevertOverlays`
                ' corren SIEMPRE; los `applyType` ifs (:240-262) sólo condicionan los Add.
                Set0(d, PresetCategory.Overlays, "0", "no overlays or skin overrides: applying clears the target's")
            End If
        ElseIf p.HasOverlays OrElse p.Overlays.Count > 0 Then
            Set0(d, PresetCategory.Overlays, p.Overlays.Count.ToString(), "F4SE overlay templates")
        End If

        ' --- Skin override (NPC.WNAM) ---
        If p.SkinFormIDOverride.HasValue Then
            Set0(d, PresetCategory.SkinOverride, If(p.SkinFormIDOverride.Value = 0UI, "none", "yes"),
                 $"WNAM 0x{p.SkinFormIDOverride.Value:X8}")
        End If

        ' --- LM skin template (FO4-only) ---
        If Not isSse AndAlso Not String.IsNullOrEmpty(p.SkinTemplateId) Then
            Set0(d, PresetCategory.LmSkinTemplate, "yes", p.SkinTemplateId)
        End If

        ' --- Outfit (DOFT + SOFT) ---
        If p.DefaultOutfitFormIDOverride.HasValue OrElse p.SleepOutfitFormIDOverride.HasValue Then
            Dim n = If(p.DefaultOutfitFormIDOverride.HasValue, 1, 0) + If(p.SleepOutfitFormIDOverride.HasValue, 1, 0)
            Set0(d, PresetCategory.Outfit, n.ToString(),
                 $"DOFT {FmtFid(p.DefaultOutfitFormIDOverride)} / SOFT {FmtFid(p.SleepOutfitFormIDOverride)}")
        End If

        ' --- Face parts (head parts + el head TXST de SSE; los irresolubles van al tooltip) ---
        ' El gate incluye el head TXST y NO sólo los head parts: el override viaja en la MISMA categoría
        ' (PresetCategoryFilter, Case FaceParts), así que un preset que trae headTexture pero ningún head part
        ' —.jslot sin array `headParts`, o con todos irresolubles— no emitía fila, la categoría no aparecía en
        ' el diálogo, el usuario no podía tildarla y el Revert descartaba el headTexture sin decir nada.
        Dim hasFtstOv As Boolean = isSse AndAlso p.SseHeadTextureFormIDOverride.HasValue
        ' SSE: las que el archivo trae pero RaceMenu no aplicaría a este NPC (sexo/raza, skee64 PresetInterface.cpp
        ' :164-175) no cuentan como aplicadas, pero sí hacen que la categoría exista y se vean en el tooltip.
        Dim filtradas As Integer = If(isSse AndAlso p.SseHeadPartsFiltradasPorMotor IsNot Nothing, p.SseHeadPartsFiltradasPorMotor.Count, 0)
        If p.HasHeadPartFormIDs OrElse p.HeadPartFormIDs.Count > 0 OrElse hasFtstOv OrElse filtradas > 0 Then
            Dim det = ""
            If p.UnresolvedHeadParts.Count > 0 Then det = $"{p.UnresolvedHeadParts.Count} unresolved (owning plugin not loaded)"
            If filtradas > 0 Then det = If(det.Length > 0, det & "  •  ", "") & $"{filtradas} not applied by RaceMenu to this NPC's race/sex"
            ' El marcador del FTST va al TEXTO CORTO, no sólo al tooltip: el clear es destructivo sobre el target
            ' y el conteo de head parts NO cambia al agregarlo ⇒ sin hover era invisible.
            ' Tiene que ser CORTO: la celda del contador es una columna ABSOLUTA de 74px con un Label de ~68px
            ' sin AutoSize ni AutoEllipsis (PresetCategoryPanel.Designer :350), o sea ~9 caracteres. Un texto tipo
            ' "12 + FTST cleared" se recorta y el fix no sirve de nada. El detalle largo va al tooltip.
            Dim txt As String = p.HeadPartFormIDs.Count.ToString()
            If hasFtstOv Then
                If p.SseHeadTextureFormIDOverride.Value = 0UI Then
                    txt &= " ✕FTST"
                    det = If(det.Length > 0, det & "  •  ", "") & "head FTST: cleared (no FTST subrecord emitted)"
                Else
                    txt &= " +FTST"
                    det = If(det.Length > 0, det & "  •  ", "") & $"head FTST 0x{p.SseHeadTextureFormIDOverride.Value:X8}"
                End If
            End If
            Set0(d, PresetCategory.FaceParts, txt, det)
        End If

        ' --- Hair color ---
        If p.HairColorFormID <> 0UI Then
            Set0(d, PresetCategory.HairColor, "yes", $"HCLF 0x{p.HairColorFormID:X8}")
        ElseIf isSse AndAlso p.SseHairColorRgb.HasValue Then
            Set0(d, PresetCategory.HairColor, "yes", $"RaceMenu RGB 0x{p.SseHairColorRgb.Value:X6}")
        End If

        ' --- Face tints ---
        If isSse Then
            If p.HasSseTints AndAlso p.SseTintLayers IsNot Nothing Then
                Dim layers As Integer = 0
                For Each sr In p.SseTintLayers
                    If sr IsNot Nothing AndAlso sr.Indice.HasValue Then layers += 1
                Next
                Dim tex = If(p.SseTintTexOverride Is Nothing, 0, p.SseTintTexOverride.Count)
                Set0(d, PresetCategory.FaceTints, layers.ToString(),
                     If(tex > 0, $"{tex} custom mask textures", ""))
            End If
        ElseIf p.HasFaceTintLayers OrElse p.FaceTintLayers.Count > 0 Then
            Set0(d, PresetCategory.FaceTints, p.FaceTintLayers.Count.ToString(), "")
        End If
        ' El ajuste del tono del CUERPO viaja dentro de esta categoria (no es una categoria nueva: es un
        ' ajuste de tinte). Si el preset lo trae hay que DECIRLO, o el usuario acepta "Face tints" sin
        ' enterarse de que tambien le cambia el tono del cuerpo. Marca la categoria como disponible aunque el
        ' preset no tenga NINGUNA capa de tint: el ajuste solo ya es algo que tomar.
        If p.SkinToneOffset IsNot Nothing AndAlso Not p.SkinToneOffset.IsZero Then
            Dim info = d(PresetCategory.FaceTints)
            Dim extra = $"body skin tint adjustment ({p.SkinToneOffset})"
            info.Detail = If(String.IsNullOrEmpty(info.Detail), extra, info.Detail & " · " & extra)
            If Not info.Available Then
                info.Available = True
                info.Text = "yes"
            End If
        End If

        ' --- Face morphs ---
        If isSse Then
            Dim nam9Set As Integer = 0
            If p.SseNam9 IsNot Nothing Then
                For Each v In p.SseNam9
                    If v <> 0.0F Then nam9Set += 1
                Next
            End If
            If p.HasSseMorphs Then
                Set0(d, PresetCategory.FaceVertexMorphs, nam9Set.ToString(), "NAM9 sliders with a non-zero value")
            End If
            ' RaceMenu NiOverride custom morphs are a SEPARATE pipeline from the record's NAM9/NAMA: no record
            ' source, they live in the .jslot / sidecar. Own category so a preset's vanilla sliders can be taken
            ' without dragging in morphs that depend on mods the user may not have.
            Dim custom = If(p.SseCustomMorphs Is Nothing, 0, p.SseCustomMorphs.Count)
            If custom > 0 Then
                Set0(d, PresetCategory.CustomMorphs, custom.ToString(), "RaceMenu NiOverride morphs")
            ElseIf p.SseCustomMorphs IsNot Nothing Then
                ' Declarada y vacía (el mapper la pone para todo .jslot, RaceMenuPresetMapper :527-529): tickeable,
                ' porque skee64 PresetInterface.cpp:228 `EraseMorphData(npc)` corre SIEMPRE y sin entradas no hay
                ' SetMorphValue (:229-230) ⇒ aplicar deja al NPC sin custom morphs.
                Set0(d, PresetCategory.CustomMorphs, "0", "no RaceMenu custom morphs: applying clears the target's")
            End If
        ElseIf p.HasChargenFaceMorphs OrElse p.ChargenFaceMorphs.Count > 0 Then
            Set0(d, PresetCategory.FaceVertexMorphs, p.ChargenFaceMorphs.Count.ToString(), "chargen MSDV sliders")
        End If

        ' --- Face bone regions (FMRS, FO4-only) ---
        If Not isSse AndAlso (p.HasFaceBoneRegions OrElse p.FaceBoneRegions.Count > 0) Then
            Set0(d, PresetCategory.FaceBoneRegions, p.FaceBoneRegions.Count.ToString(),
                 $"morph intensity {p.FacialMorphIntensity:0.###}")
        End If

        ' --- Sculpt (SSE-only): head verts + per-shape parts ---
        If isSse Then
            Dim headVerts = If(p.SseSculptHead Is Nothing, 0, p.SseSculptHead.Count)
            Dim partVerts As Integer = 0
            Dim parts As Integer = 0
            If p.SseSculptParts IsNot Nothing Then
                parts = p.SseSculptParts.Count
                For Each sp In p.SseSculptParts
                    If sp IsNot Nothing AndAlso sp.Verts IsNot Nothing Then partVerts += sp.Verts.Count
                Next
            End If
            If headVerts + partVerts > 0 Then
                Set0(d, PresetCategory.Sculpt, (headVerts + partVerts).ToString(),
                     $"{headVerts} head verts + {partVerts} verts across {parts} shapes")
            ElseIf p.SseSculptParts IsNot Nothing Then
                ' Declarada y vacía (el mapper la pone para todo .jslot, RaceMenuPresetMapper :513-521): tickeable,
                ' porque skee64 PresetInterface.cpp:221 `EraseSculptData(npc)` corre SIEMPRE y `SetSculptTarget`
                ' (:222-226) sólo con `sculptData.size() > 0` ⇒ aplicar deja al NPC sin sculpt.
                Set0(d, PresetCategory.Sculpt, "0", "no sculpt: applying clears the target's")
            End If
        End If

        ' --- CharGen face preset flag ---
        If p.IsCharGenFacePreset.HasValue Then
            Set0(d, PresetCategory.IsCharGenPreset, If(p.IsCharGenFacePreset.Value, "on", "off"), "ACBS bit 0x04")
        End If

        Return d
    End Function

    Private Sub Set0(d As Dictionary(Of PresetCategory, CategoryInfo), cat As PresetCategory, text As String, detail As String)
        d(cat).Available = True
        d(cat).Text = text
        d(cat).Detail = If(detail, "")
    End Sub

    Private Function Fmt(v As Single?) As String
        Return If(v.HasValue, v.Value.ToString("0.###"), "—")
    End Function

    Private Function FmtFid(v As UInteger?) As String
        Return If(v.HasValue, $"0x{v.Value:X8}", "—")
    End Function

End Module
