Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Aplica sobre la sombra de guardado el <see cref="NpcRecordOverride"/> que el usuario authored en
''' el NPC Editor. Vivía dentro de <c>MainForm</c> como <c>Private Sub ApplyNpcRecordOverrideToSpec</c>; se
''' mudó acá SIN cambiarle el cuerpo, por dos razones:
'''
''' <list type="number">
''' <item><b>Media ley del guardado era inalcanzable para un arnés.</b> El bit ACBS que decide cosas del
''' diálogo (0x04, "Is CharGen Face Preset") lo pisa la palabra entera que escribe este código, así que
''' cualquier gate que quisiera medir la PRECEDENCIA overlay-vs-override tenía que replicarlo — y una réplica
''' mide otra cosa.</item>
''' <item><b>El lado LECTOR no puede tirar.</b> <see cref="NpcTemplateMaterializer.MakeCategoryOwn"/> ya
''' devuelve <c>Unresolvable</c>; el que lanzaba era el envoltorio. Ahora esa rama se apaga por
''' <paramref name="strict"/> en vez de reescribir la función: con <c>strict:=False</c> el fallo VUELVE en
''' <c>fallo</c> y el llamador decide, que es lo que necesita el diálogo para no matar el proceso al abrirse.</item>
''' </list>
'''
''' <para>⛔ Son DOS funciones y no una: <see cref="MaterializarCategorias"/> corre ANTES del overlay y
''' <see cref="AplicarEscalares"/> DESPUÉS. Ese orden es la ley — mientras fueron una sola, el overlay se
''' estampaba primero y la materialización tenía que saltearle los campos con un booleano grueso por NPC.</para>
''' <para>⛔ Los wrappers de <c>MainForm</c> son finos a propósito: ya sólo enhebran <c>_npcRecordOverrides</c>.
''' El resolvedor de la cadena, el de LVLN y «¿tiene overlay?» desaparecieron — la cadena se resuelve UNA vez en
''' el NPC Editor y viaja congelada en <c>NpcRecordOverride.MaterializedSources</c>.
''' Mismo patrón que <c>MainForm.ApplyPresetOverlayToNpcData</c> sobre <see cref="NpcRecordOverlay"/>.</para></summary>
Public Module NpcRecordOverrideApplier

    ''' <summary>Materializa las categorias de plantilla que el usuario edito y que el NPC todavia
    ''' HEREDA, y baja el bit Use-X de cada una para que el <c>CopyFromTemplate</c> del motor no le pise
    ''' la edicion al cargar. No-op cuando el NPC no tiene override.
    ''' <para>⛔ Es la PRIMERA de las dos mitades en que se partio `Aplicar`, y el orden entre ellas es la
    ''' ley: materializar -> overlay -> escalares. Mientras fueron una sola funcion el overlay se estampaba
    ''' ANTES, y la materializacion tenia que saltear lo que el overlay habia puesto -- con un booleano
    ''' GRUESO por NPC, asi que un overlay de solo CUERPO hacia saltear TODA la cara. Ahora el overlay gana
    ''' por llegar ultimo, campo por campo, y el salteo no hace falta.</para></summary>
    ''' <param name="strict">True (el camino de ESCRITURA): una categoria que no se puede materializar LANZA
    ''' y aborta el guardado. False (el camino de LECTURA): no lanza, DEVUELVE el motivo y sigue; la sombra
    ''' queda INCOMPLETA y el llamador tiene que tratar su resultado como no resuelto, nunca como un valor.</param>
    ''' <returns>Nothing cuando todo resolvio. Con <c>strict:=False</c>, el motivo de la PRIMERA categoria que
    ''' no se pudo materializar. Se DEVUELVE en vez de tomarse <c>ByRef</c> porque esta es la forma exacta del
    ''' delegado <c>SaveContext.MaterializarCategorias</c>, y un ByRef no entra en una lambda.</returns>
    Public Function MaterializarCategorias(npcSpec As NPC_Data,
                                           npcFormID As UInteger,
                                           overridesPorNpc As Dictionary(Of UInteger, NpcRecordOverride),
                                           strict As Boolean) As String
        Dim fallo As String = Nothing
        Dim ov As NpcRecordOverride = Nothing
        If Not overridesPorNpc.TryGetValue(npcFormID, ov) OrElse ov Is Nothing Then Return Nothing

        ' ⛔⛔ LA GUARDA DE BANDERA VA EN LAS OCHO. Antes vivia adentro, en `ProbeCategoryOwn`, y desde
        ' afuera no se veia; ahora que la resolucion se EXIGE del snapshot, mirarla adentro llega tarde: un
        ' NPC que NO hereda Keywords y al que el editor le agrega uno nunca paso por la sede del
        ' desprendimiento, no tiene entrada, y se le rechazaria un guardado perfectamente legitimo.
        ' Reproducido: FO4, NPC no heredero -> NPC Editor -> agregar keyword -> OK -> Save.
        If ov.BaseDataChanged AndAlso NpcTemplateHelpers.HasTemplateFlag(
               npcSpec.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.BaseData) Then
            MaterializarUna(npcSpec, npcFormID, ov, NPC_TemplateCategory.BaseData, strict, fallo)
        End If
        ' ⛔ `ov.Properties` entra por ACA: el motor copia PRPS bajo el bit 1 (FO4 0x140658644
        ' `call sub_140256EF0` sobre TESNPC+0x1A0), asi que authorearlo obliga a materializar Stats.
        If (ov.StatsChanged OrElse ov.Properties IsNot Nothing) AndAlso NpcTemplateHelpers.HasTemplateFlag(
               npcSpec.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.Stats) Then
            MaterializarUna(npcSpec, npcFormID, ov, NPC_TemplateCategory.Stats, strict, fallo)
        End If
        If ov.Keywords IsNot Nothing AndAlso NpcTemplateHelpers.HasTemplateFlag(
               npcSpec.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.Keywords) Then
            MaterializarUna(npcSpec, npcFormID, ov, NPC_TemplateCategory.Keywords, strict, fallo)
        End If
        If ov.Factions IsNot Nothing AndAlso NpcTemplateHelpers.HasTemplateFlag(
               npcSpec.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.Factions) Then
            MaterializarUna(npcSpec, npcFormID, ov, NPC_TemplateCategory.Factions, strict, fallo)
        End If
        ' ⛔ `InventoryChanged` entra aca: un cambio SOLO de atuendo (DOFT/SOFT) o de armas listas no trae
        ' CNTO, y sin el latch el bit 8 quedaba arriba y el motor pisaba el atuendo al cargar. Citas del
        ' bucket: SSE 0x1403C2022 (DOFT) / 0x1403C2030 (SOFT) / 0x1403C203E; FO4 0x14065819B / 0x1406581A9.
        If (ov.InventoryChanged OrElse ov.Inventory IsNot Nothing) AndAlso NpcTemplateHelpers.HasTemplateFlag(
               npcSpec.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.Inventory) Then
            MaterializarUna(npcSpec, npcFormID, ov, NPC_TemplateCategory.Inventory, strict, fallo)
        End If
        ' ⛔ Actor Effects (SPLO) es la categoria SpellList: el nombre del campo y el de la categoria
        ' NO coinciden.
        ' ⛔⛔ ACA DECIA *"Perks (PRKR) y Properties (PRPS) no tienen categoria de plantilla -- el motor
        ' los copia bajo otros buckets -- y por eso se reemplazan abajo, sin materializar"*, y esa frase se
        ' contradice sola: que el motor los copie bajo otro bucket es justo la razon para materializar ESE
        ' bucket. Los perks viajan con los hechizos en el bit 3 (SSE 0x1403C24C3 `call sub_1401DB120` sobre
        ' +0x138, FO4 0x14065875A) y el PRPS en el bit 1 (arriba). Sin esto, el ESP salia con el bit puesto
        ' y la lista escrita, y el motor la pisaba al cargar: la edicion no existia en el juego.
        If (ov.ActorEffects IsNot Nothing OrElse ov.Perks IsNot Nothing) AndAlso NpcTemplateHelpers.HasTemplateFlag(
               npcSpec.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.SpellList) Then
            MaterializarUna(npcSpec, npcFormID, ov, NPC_TemplateCategory.SpellList, strict, fallo)
        End If
        ' ⛔ AI Data (ZNAM/AIDT/GNAM). Su ausencia era un bug entero: el editor bajaba el bit 4 sobre el
        ' parse VIVO, pero el guardado escribia el ZNAM sin materializar el bucket ni bajar el bit, asi
        ' que el motor se lo pisaba al cargar. El CNAM si estaba enhebrado por Stats: el cableado estaba
        ' asimetrico. ⛔ NO esta en la lista de siete del documento porque nacio despues, en el corte 4.
        If ov.AiDataChanged AndAlso NpcTemplateHelpers.HasTemplateFlag(
               npcSpec.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.AIData) Then
            MaterializarUna(npcSpec, npcFormID, ov, NPC_TemplateCategory.AIData, strict, fallo)
        End If
        ' ⛔ Traits es la unica que YA tenia guarda de bandera en el codigo viejo; ahora la tienen las
        ' ocho, y por eso desaparece de aca la asimetria. Las ediciones de Race/Voice/OBTS del usuario se
        ' escriben en la SEGUNDA mitad, encima del juego materializado.
        If ov.TraitsChanged AndAlso NpcTemplateHelpers.HasTemplateFlag(
               npcSpec.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.Traits) Then
            MaterializarUna(npcSpec, npcFormID, ov, NPC_TemplateCategory.Traits, strict, fallo)
        End If
        Return fallo
    End Function

    ''' <summary>Escribe sobre la sombra los ESCALARES y las LISTAS que el usuario authored. Segunda mitad de
    ''' `Aplicar`, y corre DESPUES del overlay: lo que el usuario escribio a mano es lo ultimo en llegar.
    ''' <para>⛔ Es un Sub y no una Function porque aca no hay nada que pueda quedar sin resolver: la cadena
    ''' ya se camino en la primera mitad.</para></summary>
    Public Sub AplicarEscalares(npcSpec As NPC_Data,
                                npcFormID As UInteger,
                                overridesPorNpc As Dictionary(Of UInteger, NpcRecordOverride))
        Dim ov As NpcRecordOverride = Nothing
        ' ⛔ El preambulo va en LAS DOS mitades. Sin el, `ov` es Nothing y todo el bloque de abajo revienta
        ' con NRE en el primer NPC sin override -- que es la mayoria.
        If Not overridesPorNpc.TryGetValue(npcFormID, ov) OrElse ov Is Nothing Then Exit Sub

        ' --- Scalars. ---
        ' Escribir un campo CREA su subrecord; una referencia en cero significa "ninguna" y por eso se
        ' saca en vez de escribirse.
        If ov.FullName IsNot Nothing Then
            If ov.FullName.Length > 0 OrElse npcSpec.Record.NamePresente Then
                npcSpec.Record.Name = ov.FullName
            Else
                npcSpec.Record.QuitarSubrecord("FULL")
            End If
        End If
        If ov.ShortName IsNot Nothing Then
            If ov.ShortName.Length > 0 OrElse npcSpec.Record.ShortNamePresente Then
                npcSpec.Record.ShortName = ov.ShortName
            Else
                npcSpec.Record.QuitarSubrecord("SHRT")
            End If
        End If
        ' ⛔ RNAM NO pasa por la ley del «sin valor SACA el campo»: xEdit lo declara
        ' `wbFormIDCk(RNAM, 'Race', [RACE]).SetRequired` dentro de `wbRecord(NPC_)` en LOS DOS juegos
        ' (wbDefinitionsFO4.pas:10370, dentro del record que abre en :10286; wbDefinitionsTES5.pas:8355,
        ' record en :8290). Un NPC_ sin RNAM es ilegal, así que una caja de raza vacía no es «borrar la
        ' raza» sino entrada INVÁLIDA — la misma excepción declarada que ya tienen ARMA y ARMO. La ley
        ' lo dice en su propio docstring: «NO va para campos REQUERIDOS de hecho, como el RNAM».
        ' ⚠️ CAMBIO DE SEMANTICA declarado: antes un 0 acá SACABA el RNAM; ahora es un no-op y el
        ' NPC conserva su raza. Es el sentido seguro —RNAM es requerido— y además ALINEA el guardado
        ' con el render, que ya trataba el 0 como «sin override». Hoy no llega un 0: el picker de raza
        ' abre con `allowNull:=False` y el override se escribe desde esa misma caja.
        If ov.RaceFormID.HasValue Then
            Canon.CanonInterpretacion.PonerReferenciaRequerida(ov.RaceFormID.Value, Sub(x) npcSpec.Record.Race = x)
        End If
        If ov.VoiceFormID.HasValue Then Canon.CanonInterpretacion.PonerReferenciaOSacarSubrecord(npcSpec.Record, ov.VoiceFormID.Value, "VTCK", Sub(v) npcSpec.Record.Voice = v)
        If ov.ClassFormID.HasValue Then Canon.CanonInterpretacion.PonerReferenciaOSacarSubrecord(npcSpec.Record, ov.ClassFormID.Value, "CNAM", Sub(v) npcSpec.Record.[Class] = v)
        If ov.CombatStyleFormID.HasValue Then Canon.CanonInterpretacion.PonerReferenciaOSacarSubrecord(npcSpec.Record, ov.CombatStyleFormID.Value, "ZNAM", Sub(v) npcSpec.Record.CombatStyle = v)
        ' NAM6 / NAM4 (Height). Written AFTER the Traits materialization above on purpose: height is a
        ' Traits-category field (MaterializeTraits copies it unconditionally), so on a Traits-inheriting NPC
        ' the materializer first fills the template's height and this then overwrites it with the user's.
        ' The editor latches TraitsChanged when it sets these, which is what clears the Use-Traits flag —
        ' without that the engine's CopyFromTemplate would overwrite the edit at runtime.
        ' Has* is forced True because a value here means the user authored one; NAM4 is FO4-only and the
        ' SSE editor path never sets it, so Skyrim records keep emitting no NAM4.
        If ov.HeightMin.HasValue Then npcSpec.Record.PonerAltura(ov.HeightMin.Value)
        If ov.HeightMax.HasValue Then npcSpec.Record.PonerAlturaMaxima(ov.HeightMax.Value)

        ' --- ACBS (banderas, nivel, rango de calculo, disposicion y los desplazamientos de Skyrim). ---
        ' ⛔ El bit 0x04 ("Is CharGen Face Preset") NO viaja en esta palabra, y por eso se preserva de la
        ' sombra en vez de dejarse pisar. El NPC Editor no expone ese bit a proposito y lo arrastra desde su
        ' snapshot, que sale del record CRUDO — o sea SIN lo que el overlay de Edit Face acaba de poner. Como
        ' aca se escribia la palabra ENTERA, tocar cualquier casilla del editor BORRABA el tilde del usuario y
        ' el ESP salia sin la bandera, en silencio. Ahora el overlay es el UNICO dueno del 0x04 y el editor
        ' manda en todos los demas bits.
        ' Sin preset el resultado no cambia: la sombra trae el bit del crudo, que es el mismo que ov.AcbsFlags.
        If ov.AcbsFlags.HasValue Then
            Dim chargenDelOverlay = npcSpec.Record.ConfigurationFlagsIsCharGenFacePreset
            ' ⛔ TODO menos los bits que la materializacion DERIVA. `ov.AcbsFlags` es el snapshot de
            ' ANTES de materializar; escribir la palabra entera se llevaba puesto el 0x100 que
            ' `CopiarSonidos` acababa de derivar, y dejaba un record donde el grupo `Actor Sounds` y
            ' su bit de propiedad se contradicen.
            Const derivados = NpcTemplateHelpers.AcbsBitsDerivadosPorMaterializacion
            npcSpec.Record.ConfigurationFlags =
                (ov.AcbsFlags.Value And Not derivados) Or (npcSpec.Record.ConfigurationFlags And derivados)
            npcSpec.Record.ConfigurationFlagsIsCharGenFacePreset = chargenDelOverlay
        End If
        If ov.Level.HasValue Then npcSpec.Record.PonerNivelDeConfiguracion(ov.Level.Value)
        If ov.CalcMinLevel.HasValue Then npcSpec.Record.ConfigurationCalcMinLevel = ov.CalcMinLevel.Value
        If ov.CalcMaxLevel.HasValue Then npcSpec.Record.ConfigurationCalcMaxLevel = ov.CalcMaxLevel.Value
        If ov.DispositionBase.HasValue Then npcSpec.Record.PonerBaseDeDisposicion(ov.DispositionBase.Value)
        If ov.TemplateFlags.HasValue Then npcSpec.Record.ConfigurationTemplateFlags = ov.TemplateFlags.Value
        Dim ovFo4 = TryCast(npcSpec.Record, Canon.NpcFO4)
        If ovFo4 IsNot Nothing AndAlso ov.XpValueOffset.HasValue Then ovFo4.ConfigurationXPValueOffset = ov.XpValueOffset.Value
        Dim ovSse = TryCast(npcSpec.Record, Canon.NpcSSE)
        If ovSse IsNot Nothing Then
            If ov.MagickaOffset.HasValue Then ovSse.ConfigurationMagickaOffset = ov.MagickaOffset.Value
            If ov.StaminaOffset.HasValue Then ovSse.ConfigurationStaminaOffset = ov.StaminaOffset.Value
            If ov.SpeedMultiplier.HasValue Then ovSse.ConfigurationSpeedMultiplier = ov.SpeedMultiplier.Value
            If ov.HealthOffset.HasValue Then ovSse.ConfigurationHealthOffset = ov.HealthOffset.Value
        End If

        ' --- DNAM de Skyrim, POR PORCION. ⛔ Aca se copiaba el subrecord ENTERO, y el DNAM tiene cuatro
        ' dueños: editar una habilidad escribia tambien la distancia lejana (Traits) y las armas listas
        ' (Inventory) que ese bucket no gobierna. Cada porcion va con la MISMA funcion que la materializa,
        ' y los tramos que nadie authored conservan los bytes del destino. ---
        If ov.SsePlayerSkills IsNot Nothing Then
            If ov.SseSkillArraysChanged Then
                NpcTemplateMaterializer.CopiarHabilidadesDelDnam(npcSpec.Record, ov.SsePlayerSkills)
            End If
            If ov.SseDerivedStatsChanged Then
                ' Health/Magicka/Stamina no son de ningun bucket: el motor los DERIVA. Van campo por campo.
                Dim dst = TryCast(npcSpec.Record, Canon.NpcSSE)
                If dst IsNot Nothing Then
                    dst.PlayerSkillsHealth = ov.SsePlayerSkills.PlayerSkillsHealth
                    dst.PlayerSkillsMagicka = ov.SsePlayerSkills.PlayerSkillsMagicka
                    dst.PlayerSkillsStamina = ov.SsePlayerSkills.PlayerSkillsStamina
                End If
            End If
            If ov.SseFarModelChanged Then
                NpcTemplateMaterializer.CopiarDistanciaDeModeloLejano(npcSpec.Record, ov.SsePlayerSkills)
            End If
            If ov.SseGearedWeaponsChanged Then
                NpcTemplateMaterializer.CopiarArmasListas(npcSpec.Record, ov.SsePlayerSkills)
            End If
        End If

        ' --- Listas. ---
        If ov.Keywords IsNot Nothing Then npcSpec.Record.PonerPalabrasClave(ov.Keywords)
        If ov.AttachParentSlots IsNot Nothing Then npcSpec.Record.PonerRanurasDeEnganche(ov.AttachParentSlots)
        If ov.Factions IsNot Nothing Then npcSpec.Record.PonerFacciones(ov.Factions)
        If ov.Inventory IsNot Nothing Then npcSpec.Record.PonerInventario(ov.Inventory)
        If ov.Perks IsNot Nothing Then npcSpec.Record.PonerVentajas(ov.Perks)
        If ov.ActorEffects IsNot Nothing Then npcSpec.Record.PonerEfectosDeActor(ov.ActorEffects)
        If ov.Properties IsNot Nothing Then npcSpec.Record.PonerPropiedades(ov.Properties)
        If ov.ObjectTemplateCombinations IsNot Nothing Then npcSpec.Record.ReemplazarCombinations(ov.ObjectTemplateCombinations)
    End Sub

    ''' <summary>Materializa la categoría y baja su bit Use-X. Una categoría irresoluble es un ABORTO en el
    ''' camino de escritura (si se bajara el bit igual, la plantilla dejaría de llenar el campo y el NPC se
    ''' quedaría con el valor propio vacío); en el de lectura sólo se anota, porque ahí nadie escribe nada.
    ''' <para>El primer fallo gana: <paramref name="fallo"/> no se pisa, así el motivo que ve el usuario es el
    ''' de la categoría que se rompió primero y no el de la última que se probó.</para></summary>
    ''' <summary>Materializa UNA categoria desde el snapshot CONGELADO en el override. ⛔ El llamador ya
    ''' verifico el bit de bandera: aca no se vuelve a mirar, porque duplicar esa guarda es duplicar la ley.
    ''' <para>⛔ La cadena NO se resuelve aca. Se resolvio una vez, en la sede del desprendimiento -- que es
    ''' donde el usuario estaba mirando -- y viaja congelada. Volver a caminarla al guardar daria OTRA
    ''' respuesta si el arbol cambio entre medio, y esa segunda respuesta no la eligio nadie.</para></summary>
    Private Sub MaterializarUna(npcSpec As NPC_Data, npcFormID As UInteger, ov As NpcRecordOverride,
                                cat As NPC_TemplateCategory, strict As Boolean, ByRef fallo As String)
        Dim resol As NpcTemplateMaterializer.TraitsResolution = Nothing
        ' ⛔ `TraitsResolution` es un STRUCTURE. Una clave ausente devuelve `Outcome` = 0, que es
        ' `NotInheriting` -- un desenlace LEGAL. Se discrimina por el BOOLEANO de `TryGetValue`, NUNCA por el
        ' contenido, o "no hay entrada" se vuelve "no hereda" y el fallo cerrado se evapora sin hacer ruido.
        If Not ov.MaterializedSources.TryGetValue(cat, resol) Then
            ' ⛔ La palabra SNAPSHOT se queda en el texto A PROPOSITO: `ChargenFlagSaveGate` la usa
            ' para distinguir ESTE rechazo del de la cadena irresoluble (caso H), que dice otra cosa.
            ' Sin un discriminador, los dos rechazos se ven iguales y el gate mediria cualquiera.
            ' ⛔ EN INGLES: este texto NO se queda en el log -- viaja a `result.ErrorMessage` y de ahi al
            ' cartel del guardado, y la UI de la app es toda en ingles. Y dice QUE hacer, porque un
            ' "no hay snapshot" no le dice nada a quien lo lee: el estado se rearma reabriendo el editor
            ' sobre ese NPC, o se descarta con Reset.
            Dim msg = $"NPC 0x{npcFormID:X8} inherits {cat} from a template and was edited, but the " &
                      "frozen chain SNAPSHOT is missing, so the edit cannot be materialised. Re-open the " &
                      "editor on this NPC and confirm the change, or use Reset to discard it."
            If strict Then Throw New InvalidOperationException(msg)
            ' ⛔ El primer fallo gana y SIGUE: no se hace Return, porque el bloque tiene que intentar las
            ' demas categorias. Abortar en la primera cambiaria la conducta del camino de LECTURA, que es el
            ' que compone la sombra para el dialogo.
            If fallo Is Nothing Then fallo = msg
            Exit Sub
        End If
        MakeCategoryOwnForSave(npcSpec, cat, resol, strict, fallo)
    End Sub

    Private Sub MakeCategoryOwnForSave(npcSpec As NPC_Data,
                                       category As NPC_TemplateCategory,
                                       resol As NpcTemplateMaterializer.TraitsResolution,
                                       strict As Boolean,
                                       ByRef fallo As String)
        ' ⛔ La cadena YA viene resuelta: aca no se camina ni se pinnea una LVLN. Por eso desaparecieron
        ' `resolver` y `resolveLvlnPick`, y con ellos la segunda respuesta que nadie eligio.
        ' ⛔⛔ FALLA CERRADO ante una foto CONTRADICTORIA: el crudo hereda la categoria y la foto dice que
        ' no. `MakeCategoryOwn` tomaba ese `NotInheriting` por respuesta valida, dejaba el bit arriba y
        ' volvia sin error: un ESP incorrecto y mudo. El editor ya no guarda esa foto (ver
        ' `NpcEditor_Form.RegisterRecordOverride`); esto ataja una entrada vieja o fabricada.
        If NpcTemplateHelpers.HasTemplateFlag(npcSpec.Record.ConfigurationTemplateFlags, category) AndAlso
           resol.Outcome = NpcTemplateMaterializer.MaterializeOutcome.NotInheriting Then
            Dim contradictorio = $"NPC 0x{npcSpec.FormID:X8}: the frozen chain snapshot for " &
                                 $"{NpcManagerFormat.GetTemplateCategoryLabel(category)} says it does not inherit, " &
                                 "but the record still inherits it. Re-open the editor on this NPC and confirm the change."
            If strict Then Throw New InvalidOperationException(contradictorio)
            If fallo Is Nothing Then fallo = contradictorio
            Return
        End If
        Dim outcome = NpcTemplateMaterializer.MakeCategoryOwn(npcSpec, category, resol)
        If outcome <> NpcTemplateMaterializer.MaterializeOutcome.Unresolvable AndAlso
           outcome <> NpcTemplateMaterializer.MaterializeOutcome.UnsupportedCategory Then Return

        Dim motivo = $"NPC 0x{npcSpec.FormID:X8}: cannot materialize {NpcManagerFormat.GetTemplateCategoryLabel(category)}"
        If strict Then
            Throw New InvalidOperationException(motivo & "; save aborted so the template cannot overwrite the edit.")
        End If
        If fallo Is Nothing Then fallo = motivo
    End Sub

End Module
