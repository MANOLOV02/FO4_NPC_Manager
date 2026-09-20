Imports FO4_Base_Library

''' <summary>
''' App-side wrapper over the generic <see cref="FaceTintInputBuilder"/> (in FO4_Base_Library).
''' This thin layer is the ONLY app-specific part of FaceTint input building: it resolves the
''' NPC record + LooksMenu preset OVERLAY (NpcRecordOverlay / LooksmenuLoader — both app-specific)
''' into a concrete npcData, parses the RACE, then delegates the actual generic, record-driven
''' layer/region-swap composition to the library. Callers (live render MainForm.TryApplyFaceTints,
''' offline bake FaceGenBuilder.BakeFaceTextures) keep calling Build(...) with the same signature.
''' The generic helpers (TintSlotName, LoadTintLayerBytes, ResolveTemplateColorIndex, ...) now live
''' on FaceTintInputBuilder; call those directly.
''' </summary>
Public Module FaceTintLayerBuilder

    ''' <summary>Resolve the NPC at <paramref name="modelFormID"/> (record + LooksMenu overlay for
    ''' <paramref name="rootFormID"/>) + the RACE, then build the face tint inputs via the generic
    ''' <see cref="FaceTintInputBuilder.Build"/>. Returns an empty result when the NPC or RACE can't
    ''' be resolved. Signature is preserved verbatim so existing render/bake callers don't change.</summary>
    ''' <param name="parseRace">Optional cached RACE parser (NpcRenderContext.ParseRaceCanonCached). Threaded into
    ''' the overlay + the local RACE parse; falls back to direct <c>Canon.CanonRecords.Race</c> when Nothing.</param>
    Public Function Build(modelFormID As UInteger,
                          rootFormID As UInteger,
                          raceFormID As UInteger,
                          isFemale As Boolean,
                          pluginManager As PluginManager,
                          lectura As LecturaDeCadena,
                          appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                          overlayPreset As LooksmenuLoader.LooksmenuPreset,
                          tintBytesCache As Dictionary(Of String, Byte()),
                          Optional hairColorFormID As UInteger = 0UI,
                          Optional hasTextureLighting As Boolean = False,
                          Optional textureLightingColorArgb As Integer = 0,
                          Optional parseRace As Func(Of PluginRecord, Canon.IRace) = Nothing,
                          Optional dataPath As String = Nothing) As FaceTintInputBuilder.TintBuildResult
        If pluginManager Is Nothing Then Return New FaceTintInputBuilder.TintBuildResult()

        ' App-specific: NPC record + LooksMenu preset overlay -> concrete npcData.
        ' ⛔⛔ EL PRESET LO PASA EL LLAMADOR, no se deriva aca. Los dos llamadores tienen politicas
        ' distintas y legitimas: el RENDER compone con el overlay de DIBUJO (un heredero se dibuja con los
        ' tintes de su plantilla) y el BAKE con la AUTORIA (hornear es authorear, y G10 lo mide). Cuando se
        ' derivaba adentro, los dos se llevaban la autoria y el render de un heredero salia con los tintes
        ' VACIOS de X. Que la politica se lea en el llamador es justamente el punto.
        ' ⛔⛔ LA CARA SALE DE LA PLANTILLA, Y ESO ES CANONICO -- medido, no heredado de la version
        ' vieja. El juego arma la ruta del FaceGen caminando la cadena hasta el ULTIMO eslabon y usando SU
        ' FormID: Fallout la recorre en 0x140658E80..0x140658EAA y formatea en 0x140658EE4; Skyrim en
        ' 0x1403C2E20..0x1403C2E49 y formatea en 0x1403C2E83. O sea que la cara de un heredero es la que
        ' su plantilla tiene horneada, y componerla desde el record de la plantilla es reproducir eso.
        ' ⛔ NO cambiar esto por `state.RecordBase`: la base es para el CUERPO y para los campos que el
        ' bit 0 copia. Si el usuario edita la cara, la puerta desprende y a partir de ahi el NPC ya no
        ' hereda -- la cara pasa a salir de su propio record por el mismo camino, sin ninguna excepcion.
        ' ⛔ Esa frase era FALSA para las capas TINI, el sculpt y los custom morphs de Skyrim: la puerta
        ' preguntaba solo «¿los copia el bit 0?», y no los copia, asi que no desprendia -- y el juego igual
        ' no los mostraba, porque abre el FaceGen del ULTIMO eslabon. Desde el punto 1 (14-sep) la puerta
        ' pregunta `PresetCategories.DesprendeElCanal` y la frase vale para todos los canales de cara.
        ' ⛔⛔ LA PLANTILLA VA CON SU PROPIO OVERLAY. Aca se componia la autoria del NPC sobre el
        ' record CRUDO de su plantilla, asi que si el usuario le habia cargado un preset a la PLANTILLA, el
        ' heredero seguia mostrando la cara vieja de ella -- justo lo contrario de lo que promete el cartel
        ' del desprendimiento ("keeps a copy of what you are seeing now").
        ' ⛔ `SombraDelTerminal` es la sede de esa pregunta y ya la usan la base del dibujo y el
        ' congelado del editor; este era el CUARTO lector del terminal y el unico que no la usaba.
        ' ⛔ Solo cuando la cara viene de OTRO record: para un no heredero el modelo es el propio NPC, y
        ' estamparle su autoria dos veces es la ida y vuelta que ya perdio un escalon de 255 una vez.
        ' ⛔⛔ RONDA 20b (D2): el record del modelo sale de LA LECTURA del llamador, no de un parse del plugin. El render
        ' pasa la cache de la sesion; el horneado, su lectura congelada. Con el parse, un modelo editado en la sesion
        ' (la plantilla con otra raza, otra cara) se componia con el record VIEJO. La instancia que llega puede ser la
        ' de la cache: `SombraDelTerminal`/`AplicarOverlay` no la mutan (devuelven copia) y la raza se estampa sobre esa copia.
        ' ⛔ LOS DOS `Nothing` DE ABAJO SON EL RESOLVEDOR DE LA PLANTILLA DE PIEL DE LOOKSMENU, y
        ' están bien: sin resolvedor, `AplicarOverlay` nunca arma un `lmTemplate` y nunca entra a la
        ' rama que reemplaza head parts por PartType, que es la ÚNICA que usa la sede de head parts
        ' (`NpcRecordOverlay:580` — `lmTemplate` sólo se arma si el resolvedor no es Nothing). Por eso
        ' este archivo no necesita la sede y no la lleva en la firma. Queda escrito porque la
        ' revisión lo levantó como omisión: la diferencia con los tres sitios que sí la omitían mal
        ' es justo ésta — ellos pasan resolvedor. Y la guarda de la entrada de `AplicarOverlay` hace
        ' que el día que este archivo pase un resolvedor, tenga que traer la sede.
        Dim recordDelModelo = lectura.Resuelta(rootFormID).Leer(modelFormID)
        If modelFormID <> rootFormID Then
            recordDelModelo = NpcRecordOverlay.SombraDelTerminal(recordDelModelo, appliedPresets,
                                                                 pluginManager, Nothing, parseRace)
        End If
        Dim npcData = NpcRecordOverlay.AplicarOverlay(
            recordDelModelo,
            overlayPreset,
            rootFormID, pluginManager, Nothing, parseRace)
        If npcData Is Nothing Then Return New FaceTintInputBuilder.TintBuildResult()
        ' El caller pasa la raza EFECTIVA (state.RaceFormID, con el override del editor); el npcData recién
        ' parseado trae la cruda del récord. Alinearlas acá deja el resultado auto-consistente (built.race y
        ' built.npcData.RaceFormID = la misma raza) — sin esto, tras un cambio de raza los consumidores que
        ' leían npcData.RaceFormID componían la CARA con el catálogo de la raza vieja. Mutar es seguro:
        ' `AplicarOverlay` devuelve SIEMPRE una copia (con o sin preset), nunca la instancia de la lectura.
        If raceFormID <> 0UI AndAlso npcData.Record.Race <> raceFormID Then
            npcData.Record.Race = raceFormID
        End If

        Dim raceRec = pluginManager.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return New FaceTintInputBuilder.TintBuildResult()
        Dim race = If(parseRace IsNot Nothing, parseRace(raceRec), Canon.CanonRecords.Race(raceRec, pluginManager))

        ' App-specific: fold LooksMenu CUSTOM tint templates (Data\F4SE\Plugins\F4EE\Tints\...) into the
        ' race's tint groups so an NPC's applied tints against a mod-added template resolve + compose. This
        ' is the SINGLE seam both live render (NpcFaceTintResolver) and the offline bake (FaceGenBuilder)
        ' route through, so it also covers the bake. Idempotent + no-op when no custom tints exist.
        ' La lista fusionada NO se guarda dentro de race (no hay dónde colgarla en una vista canónica):
        ' se arma acá y se pasa aparte al builder. Mismo criterio que el registro de LUTs: el Data\
        ' efectivo del caller, no el global. Con dataPath Nothing (camino de la app) la sobrecarga
        ' resuelve el Config_App y queda igual que antes.
        Dim tintGroups = LmCustomTintLoader.Fusionar(race, isFemale, pluginManager, If(dataPath, Config_App.Current?.DataPath))

        ' Generic, record-driven composition lives in the library.
        ' dataPath viaja hasta el builder: es de donde sale el registro de LUTs de pelo. Nothing = el
        ' Config_App global (camino de la app). El CLI headless honra --data y NO puebla ese global, asi
        ' que sin este paso su bake leia el LUTs\ del Data de ESCRITURA en vez del de lectura.
        Return FaceTintInputBuilder.Build(npcData, race, isFemale, pluginManager, tintBytesCache, tintGroups,
                                          hairColorFormID, hasTextureLighting, textureLightingColorArgb,
                                          dataPath)
    End Function

End Module
