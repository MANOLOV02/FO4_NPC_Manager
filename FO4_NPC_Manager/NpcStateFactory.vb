Imports System.Globalization
Imports System.IO
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FO4_Base_Library
Imports MaterialLib
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Mathematics
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Build per-bucket NPC state (traits/inventory/model) + resolve body weights. Extracted from MainForm (pure stateless, no instance state, no UI). Real separate
''' class (NOT a partial). See 61-perf-mainform-split.</summary>
Friend NotInheritable Class NpcStateFactory
    Private Sub New()
    End Sub

    ''' <summary>Materialize NPC.MWGT into 3 concrete floats, applying the engine's "Default"
    ''' sentinel substitution rule. Each NPC.MWGT slot may come as Nothing (the parser flagged
    ''' it as Single.MaxValue, the wire encoding of "field not assigned" — see
    ''' RecordParsers.ReadOptionalFloat). Substitution rule:
    '''   • 0 Defaults → return as-is, do NOT renormalize (respect the record's data even if
    '''     it doesn't sum to 1).
    '''   • 1 Default  → fill the missing slot with clamp(1 - sum(other 2), 0, +∞). The two
    '''     explicit values stay untouched. Result sums to 1 unless the two explicit values
    '''     exceeded 1 (in which case the missing slot is 0 and the sum stays > 1).
    '''   • 2 Defaults → fill the missing slots from RACE.{Male|Female}DefaultWeight{X}, then
    '''     renormalize the 3 to sum=1 (skip if total is 0).
    '''   • 3 Defaults → use RACE.{Male|Female}DefaultWeight{X} verbatim; do NOT renormalize.
    ''' RACE defaults are read per-gender. If RACE doesn't carry the field (record &lt; v109),
    ''' fallback is 0.
    ''' Logs the raw → resolved transition when any substitution happened, for audit.</summary>
    Public Shared Function ResolveBodyWeights(traits As MainForm.TraitsState, race As Canon.IRace, isFemale As Boolean) As (Thin As Single, Muscular As Single, Fat As Single)
        Dim rawT = traits.WeightThin
        Dim rawM = traits.WeightMuscular
        Dim rawF = traits.WeightFat
        Dim defaultCount = 0
        If Not rawT.HasValue Then defaultCount += 1
        If Not rawM.HasValue Then defaultCount += 1
        If Not rawF.HasValue Then defaultCount += 1

        Dim resT As Single, resM As Single, resF As Single

        Select Case defaultCount
            Case 0
                resT = rawT.Value
                resM = rawM.Value
                resF = rawF.Value
            Case 1
                Dim a As Single, b As Single
                If Not rawT.HasValue Then
                    a = rawM.Value : b = rawF.Value
                    resT = Math.Max(0.0F, 1.0F - a - b) : resM = a : resF = b
                ElseIf Not rawM.HasValue Then
                    a = rawT.Value : b = rawF.Value
                    resT = a : resM = Math.Max(0.0F, 1.0F - a - b) : resF = b
                Else
                    a = rawT.Value : b = rawM.Value
                    resT = a : resM = b : resF = Math.Max(0.0F, 1.0F - a - b)
                End If
            Case 2
                Dim raceT = DefaultWeight(race, isFemale, DefaultWeightAxis.Thin)
                Dim raceM = DefaultWeight(race, isFemale, DefaultWeightAxis.Muscular)
                Dim raceF = DefaultWeight(race, isFemale, DefaultWeightAxis.Fat)
                resT = If(rawT, raceT)
                resM = If(rawM, raceM)
                resF = If(rawF, raceF)
                Dim sum = resT + resM + resF
                If sum > 0.0F Then
                    resT /= sum : resM /= sum : resF /= sum
                End If
            Case Else  ' 3
                resT = DefaultWeight(race, isFemale, DefaultWeightAxis.Thin)
                resM = DefaultWeight(race, isFemale, DefaultWeightAxis.Muscular)
                resF = DefaultWeight(race, isFemale, DefaultWeightAxis.Fat)
        End Select

        If defaultCount > 0 Then
            Dim rawStr = $"({(If(rawT.HasValue, rawT.Value.ToString("F3"), "Default"))},{(If(rawM.HasValue, rawM.Value.ToString("F3"), "Default"))},{(If(rawF.HasValue, rawF.Value.ToString("F3"), "Default"))})"
        End If

        Return (resT, resM, resF)
    End Function

    Private Enum DefaultWeightAxis
        Thin
        Muscular
        Fat
    End Enum

    ''' <summary>RACE.{Male|Female}DefaultWeight{Thin|Muscular|Fat}: exclusivo de Fallout 4 (el DATA de
    ''' Skyrim no declara esos floats — el parser viejo tampoco los llenaba nunca para Skyrim, así que
    ''' 0 acá reproduce el mismo comportamiento).
    ''' <para>Que el campo ESTÉ no significa que traiga un valor: puede traer el centinela que le dice
    ''' al motor que use el suyo. Se filtra con el mismo predicado que los pesos del NPC — antes acá
    ''' sólo se miraba la presencia, y una raza con el centinela devolvía una magnitud absurda que
    ''' terminaba escalando los huesos.</para></summary>
    Private Shared Function DefaultWeight(race As Canon.IRace, isFemale As Boolean, axis As DefaultWeightAxis) As Single
        Dim raceFo4 = TryCast(race, Canon.RaceFO4)
        If raceFo4 Is Nothing Then Return 0.0F
        Select Case axis
            Case DefaultWeightAxis.Thin
                If isFemale Then
                    Return PesoOCero(raceFo4.FemaleDefaultWeightThinPresente, raceFo4.FemaleDefaultWeightThin)
                End If
                Return PesoOCero(raceFo4.MaleDefaultWeightThinPresente, raceFo4.MaleDefaultWeightThin)
            Case DefaultWeightAxis.Muscular
                If isFemale Then
                    Return PesoOCero(raceFo4.FemaleDefaultWeightMuscularPresente, raceFo4.FemaleDefaultWeightMuscular)
                End If
                Return PesoOCero(raceFo4.MaleDefaultWeightMuscularPresente, raceFo4.MaleDefaultWeightMuscular)
            Case Else ' Fat
                If isFemale Then
                    Return PesoOCero(raceFo4.FemaleDefaultWeightFatPresente, raceFo4.FemaleDefaultWeightFat)
                End If
                Return PesoOCero(raceFo4.MaleDefaultWeightFatPresente, raceFo4.MaleDefaultWeightFat)
        End Select
    End Function

    ''' <summary>El peso por defecto de la raza, o cero cuando el campo no está o trae el centinela
    ''' de "sin valor".</summary>
    Private Shared Function PesoOCero(presente As Boolean, valor As Single) As Single
        If Not presente Then Return 0.0F
        If Canon.CanonInterpretacion.EsPesoSinValor(valor) Then Return 0.0F
        Return valor
    End Function

    Public Shared Function CreateOwnTraitsState(npc As NPC_Data) As MainForm.TraitsState
        ' [TEST: TPLT-traits-bucket] HeadTexture/HairColor/FacialHairColor/HeadParts/QNAM
        ' now seeded here so they ride the Traits chain walk.
        Dim state As New MainForm.TraitsState With {
            .SourceFormID = npc.FormID,
            .IsFemale = npc.Record.ConfigurationFlagsFemale,
            .RaceFormID = npc.Record.Race,
            .SkinFormID = npc.Record.Skin,
            .WeightThin = npc.Record.PesoDelCuerpo(0),
            .WeightMuscular = npc.Record.PesoDelCuerpo(1),
            .WeightFat = npc.Record.PesoDelCuerpo(2),
            .HeadTextureFormID = npc.Record.HeadTexture,
            .HairColorFormID = npc.Record.HairColor,
            .FacialHairColorFormID = npc.Record.ColorDeBarba(),
            .HasTextureLighting = npc.Record.TextureLightingRedPresente,
            .TextureLightingColor = npc.Record.ColorDeIluminacionDeTextura()
        }
        state.HeadPartFormIDs.AddRange(npc.Record.PartesDeCabeza())
        ' [TEST: TPLT-traits-bucket] OBTE/OBTS rides the Traits walk (measured: inherited via Use Traits,
        ' never Use Model/Animation — see TraitsState). Own OBTS for a non-inheriting NPC; the chain walk
        ' replaces it with the template source's when Use Traits is set (e.g. Mr Gutsy rank variants).
        state.ObjectTemplateOMODFormIDs.AddRange(npc.Record.OmodsDeLaPrimeraCombinacion())
        state.ObjectTemplateCombinations.AddRange(npc.Record.CombinacionesDelNpc())
        state.HasObjectTemplate = npc.Record.TieneCombinaciones()
        state.AttachParentSlotFormIDs.AddRange(npc.Record.RanurasDeEnganche())
        Return state
    End Function

    Public Shared Function CreateOwnInventoryState(npc As NPC_Data) As MainForm.InventoryState
        Return New MainForm.InventoryState With {
            .SourceFormID = npc.FormID,
            .DefaultOutfitFormID = npc.Record.DefaultOutfit,
            .SleepOutfitFormID = npc.Record.SleepingOutfit
        }
    End Function


    ''' <summary>The FormID to read FACE/BODY appearance (tint, chargen + face-bone morphs, MRSV,
    ''' skin-tone, FaceGen NIF) from: the resolved Traits source (inherited) when set, else the NPC's
    ''' own FormID. For a non-inheriting NPC this equals the root, so every read is byte-identical to
    ''' before — only template-inheriting NPCs change. Mirrors how HeadPartFormIDs/Hair already resolve
    ''' from the Traits source. Replaces the old ModelSourceFormID-or-root pattern, which always fell to
    ''' root because ModelSourceFormID was never wired in the render path.</summary>
    ''' <summary>⛔⛔ LA SEDE UNICA de "de un record sale un estado visual". Antes esta ley estaba
    ''' escrita DOS veces — una en el render y otra en el bake— y las dos **ya divergian por ORDEN**.
    ''' <para>⛔ <paramref name="npcData"/> es de donde salen los DATOS (en el render, la SOMBRA: el
    ''' terminal con el overlay de autoria aplicado). <paramref name="root"/> es de donde sale la
    ''' IDENTIDAD (el NPC que el usuario esta mirando). No son el mismo, y confundirlos es lo que
    ''' rompia `_appliedPresets(Root)`.</para>
    ''' <para>⛔ `traits` **no es parametro**: se computa aca y se DEVUELVE, asi que ningun llamador
    ''' puede pasar otro. El bake y el render quedan obligados a la misma ley — que es todo el punto
    ''' de que exista esta funcion.</para>
    ''' <para>Escribe 21 de los 30 campos de `NPCVisualState`. Los otros NUEVE quedan sin escribir A
    ''' PROPOSITO y estan nombrados abajo: `ModelSourceFormID` (el render lo deja en 0),
    ''' `ExplicitHeadTextureFormID` y los tres pesos (los pone `ApplyRaceFallbacks`, que corre
    ''' despues), `VariantLabel` (lo pone el
    ''' llamador de variantes) y los dos de `Loadout*` (los escribe solo `MainForm`, sobre un clon).
    ''' El A/B compara los 30 por REFLEXION: si manana aparece un campo 31 y esta funcion no lo
    ''' escribe, el gate lo ve.</para></summary>
    ''' <param name="traitsSourceFid">⛔⛔ EL TERMINAL DE LA CADENA, dicho por el que ya lo camino.
    ''' <para>Antes esto se DEDUCIA de `npcData`, y funcionaba por accidente: la sombra ERA el record de la
    ''' plantilla, asi que preguntarle "de quien sos" daba la plantilla. Desde que la sombra se arma sobre
    ''' la BASE —el record propio del NPC con los campos del bit 0— preguntarle da el NPC mismo, y el estado
    ''' perdia de que plantilla hereda.</para>
    ''' <para>⛔ Importa mas de lo que parece: `FaceAppearanceSourceFormID` sale de aca y decide DE QUIEN
    ''' se cargan los archivos de FaceGen. Con el bit 0 arriba el juego carga los de la PLANTILLA, asi que
    ''' un heredero que dijera "yo" cargaria los suyos, que no existen.</para></param>
    ''' <summary>⛔⛔ EL ESTADO CON EL QUE SE HORNEA, de punta a punta y en UNA sede: resolver el
    ''' overlay --caminando la cadena-- proyectar sobre la base del horneado con SU terminal, sellar
    ''' `ModelSourceFormID` y correr los fallbacks de raza.
    ''' <para>⛔ Existe porque el unico caso que compara la sede del render con la del bake tenia el lado
    ''' del bake escrito a mano: `ProyectarEstado(npc, npc, ..., npc)`, sin caminar la cadena, con el record
    ''' propio como base y sin terminal. Es decir que la comparacion no median dos sedes sino una sede y una
    ''' tercera cosa -- y encima en el campo que esta ola vino a unificar.</para>
    ''' <para>⛔ `presetDeDibujo:=Nothing` es deliberado y NO se toca desde aca: el estado del horneado no
    ''' lleva `SkinToneOffset`, y el unico consumidor del offset tiene todos sus llamadores en el render.
    ''' Pasarle el preset «porque parece mas completo» cambiaria BYTES HORNEADOS.</para>
    ''' <para>Devuelve los tres, no solo el estado: el horneado necesita tambien el record resuelto (raza,
    ''' genero, capas de tinte) y el `traits` de ESTA proyeccion, no uno recalculado.</para></summary>
    ''' <param name="lectura">⛔⛔ RONDA 20b (D2): la lectura de la cadena (records y hoja). OBLIGATORIA; ver
    ''' <see cref="NpcRecordOverlay.ResolveOverlaidNpcData"/>.</param>
    Public Shared Function EstadoDelHorneado(npcFormID As UInteger,
                                             pluginManager As PluginManager,
                                             lectura As LecturaDeCadena,
                                             appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                             Optional lmSkinTemplateResolver As NpcRecordOverlay.ResolveLmSkinTemplateDelegate = Nothing,
                                             Optional resHeadParts As ResolucionDeHeadParts = Nothing) _
                                             As (Datos As NPC_Data,
                                                 Estado As MainForm.NPCVisualState,
                                                 Traits As MainForm.TraitsState)
        Dim baseDelHorneado As NPC_Data = Nothing
        Dim terminalDelHorneado As UInteger = 0UI
        Dim npcData = NpcRecordOverlay.ResolveOverlaidNpcData(npcFormID, pluginManager, lectura, appliedPresets,
                                                              lmSkinTemplateResolver,
                                                              baseDelHorneado, terminalDelHorneado,
                                                              resHeadParts)
        If npcData Is Nothing Then Return (Nothing, Nothing, Nothing)
        ' ⛔ SIN FECHA A PROPOSITO, y es seguro por construccion: el estado del horneado NO se publica en
        ' `LastRenderedState`, asi que nadie lo usa como cache de la base; y si algun dia se publicara, `Nothing`
        ' se lee como VENCIDA y el consumidor recalcula. Poner una fecha inventada aca -- `npcData`, que es el
        ' record YA compuesto con el overlay y no el parse del que salio la base -- seria peor: diria "fresca"
        ' sobre una instancia que no es la que la sesion tiene.
        Dim proy = ProyectarEstado(npcData, npcData,
                                   CreateOwnInventoryState(npcData),
                                   presetDeDibujo:=Nothing,
                                   recordBase:=If(baseDelHorneado, npcData),
                                   baseArmadaDesde:=Nothing,
                                   baseArmadaDesdeTerminal:=Nothing,
                                   traitsSourceFid:=terminalDelHorneado)
        ' `ModelSourceFormID` es dato del BAKE, no de la proyeccion: el render lo deja en 0.
        proy.Estado.ModelSourceFormID = npcFormID
        ' Fallbacks de raza del motor (WNAM=0 -> RACE.SkinFormID, partes/textura/pelo -> defaults de la
        ' raza, centinela de MWGT). ⛔ Con el `traits` de LA PROYECCION, no con uno recalculado.
        NpcStateResolver.ApplyRaceFallbacks(proy.Estado, proy.Traits, pluginManager)
        Return (npcData, proy.Estado, proy.Traits)
    End Function

    ''' <param name="baseArmadaDesde">⛔⛔ LA FECHA DE LA BASE, y viaja por el MISMO camino OBLIGATORIO que la
    ''' base por el mismo motivo: son una cosa. Es la instancia de parse del record PROPIO de la que se derivo
    ''' <paramref name="recordBase"/>; <paramref name="baseArmadaDesdeTerminal"/> es la del TERMINAL de Traits
    ''' cuando el NPC hereda. `Nothing` = SIN FECHA, y el consumidor
    ''' (<c>MainForm.RecordEfectivoParaAutoria</c>) trata la base sin fecha como VENCIDA. Ver
    ''' <c>MainForm.NPCVisualState.BaseArmadaDesde</c>.
    ''' <para>⛔ Se sella ACA y no en el resolver: la base entra al estado en esta funcion, y con la fecha puesta
    ''' afuera quedaban DOS dueños de la misma cosa -- el horneado, que proyecta por esta misma fabrica, nacia con
    ''' base y sin fecha. Es el agujero que el comentario de la base dice haber cerrado a proposito.</para></param>
    Public Shared Function ProyectarEstado(npcData As NPC_Data, root As NPC_Data,
                                           inventory As MainForm.InventoryState,
                                           presetDeDibujo As LooksmenuLoader.LooksmenuPreset,
                                           recordBase As NPC_Data,
                                           baseArmadaDesde As NPC_Data,
                                           baseArmadaDesdeTerminal As NPC_Data,
                                           Optional traitsSourceFid As UInteger = 0UI) _
                                           As (Estado As MainForm.NPCVisualState, Traits As MainForm.TraitsState)
        Dim traits = CreateOwnTraitsState(npcData)
        If traitsSourceFid <> 0UI Then traits.SourceFormID = traitsSourceFid

        ' ⛔⛔ LA BASE VIAJA DESDE ACA, no desde el resolver. Mientras la asignaba el
        ' resolver, el HORNEADO -que proyecta por esta misma fabrica- nacia sin ella, y los tres
        ' compositores del bake devuelven Nothing con un estado sin base: el color de piel del
        ' material horneado dejaba de salir de la capa de tono y salia del QNAM crudo. Es
        ' obligatorio y no opcional A PROPOSITO: un `Optional ... = Nothing` habria dejado el mismo
        ' agujero abierto en silencio para el proximo que proyecte un estado.
        Dim st As New MainForm.NPCVisualState With {
            .FormID = root.FormID,
            .RecordBase = recordBase,
            .BaseArmadaDesde = baseArmadaDesde,
            .BaseArmadaDesdeTerminal = baseArmadaDesdeTerminal,
            .RootNpcFormID = root.FormID,
            .IsFemale = traits.IsFemale,
            .RaceFormID = traits.RaceFormID,
            .SkinFormID = traits.SkinFormID,
            .HeadTextureFormID = traits.HeadTextureFormID,
            .HairColorFormID = traits.HairColorFormID,
            .FacialHairColorFormID = traits.FacialHairColorFormID,
            .HasTextureLighting = traits.HasTextureLighting,
            .TextureLightingColor = traits.TextureLightingColor,
            .SseHairColorRgb = npcData.SseHairColorRgb,
            .TraitsSourceFormID = traits.SourceFormID,
            .InventorySourceFormID = inventory.SourceFormID,
            .DefaultOutfitFormID = inventory.DefaultOutfitFormID,
            .SleepOutfitFormID = inventory.SleepOutfitFormID,
            .HeadDiffuseAlphaTest = (root.Game = Config_App.Game_Enum.Fallout4) AndAlso
                                    (root.Record.ConfigurationFlags And &H1000000UI) <> 0UI,
            .SkinToneOffset = SkinToneQnamOffset.CloneOrNothing(
                                  If(presetDeDibujo Is Nothing, Nothing, presetDeDibujo.SkinToneOffset))
        }
        st.HeadPartFormIDs.AddRange(traits.HeadPartFormIDs)
        st.ObjectTemplateOMODFormIDs.AddRange(traits.ObjectTemplateOMODFormIDs)
        st.ObjectTemplateCombinations.AddRange(traits.ObjectTemplateCombinations)
        st.HasObjectTemplate = traits.HasObjectTemplate
        st.AttachParentSlotFormIDs.AddRange(traits.AttachParentSlotFormIDs)
        Return (st, traits)
    End Function

    ''' <summary>⛔⛔ DE QUE NPC SALEN LOS ARCHIVOS DE CARA. Medido en los dos motores, no supuesto:
    ''' el juego camina la cadena de plantillas hasta el ULTIMO eslabon y usa EL FORMID DE ESE para armar la
    ''' ruta del FaceGen.
    ''' <para>Fallout 4: recorre `TESNPC+0x270` en 0x140658E80..0x140658EAA, lee el FormID en 0x140658ECE
    ''' (`mov ebx, [rbx+0x14]`) y formatea en 0x140658EE4.
    ''' Skyrim SE: recorre `TESNPC+0x1F0` en 0x1403C2E20..0x1403C2E49, lee el FormID en 0x1403C2E6D y
    ''' formatea en 0x1403C2E83.</para>
    ''' <para>⛔ Ese campo de la cadena es de los que el bit 0 ESCRIBE (+0x270 en Fallout, +0x1F0 en
    ''' Skyrim; los dos estan en el censo de lo que la copia escribe en el destino), o sea que la copia de
    ''' plantilla es la que lo deja apuntando. Por eso un heredero se dibuja con la cara de su plantilla
    ''' aunque el resto sea suyo: no es una regla de la app, es el nombre del archivo que el motor abre.</para>
    ''' <para>⛔ Es el ULTIMO eslabon, no el primero: si X hereda de T1 y T1 de T2, la cara sale de T2.</para></summary>
    Public Shared Function FaceAppearanceSourceFormID(state As MainForm.NPCVisualState) As UInteger
        If state Is Nothing Then Return 0UI
        Return If(state.TraitsSourceFormID <> 0UI, state.TraitsSourceFormID, state.FormID)
    End Function
End Class
