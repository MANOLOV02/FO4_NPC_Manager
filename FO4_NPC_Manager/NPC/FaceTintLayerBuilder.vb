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
    ''' <param name="parseRace">Optional cached RACE parser (NpcRenderContext.ParseRaceCached). Threaded into
    ''' the overlay + the local RACE parse; falls back to direct <c>RecordParsers.ParseRACE</c> when Nothing.</param>
    Public Function Build(modelFormID As UInteger,
                          rootFormID As UInteger,
                          raceFormID As UInteger,
                          isFemale As Boolean,
                          pluginManager As PluginManager,
                          appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                          tintBytesCache As Dictionary(Of String, Byte()),
                          Optional hairColorFormID As UInteger = 0UI,
                          Optional hasTextureLighting As Boolean = False,
                          Optional textureLightingColorArgb As Integer = 0,
                          Optional parseRace As Func(Of PluginRecord, RACE_Data) = Nothing,
                          Optional dataPath As String = Nothing) As FaceTintInputBuilder.TintBuildResult
        If pluginManager Is Nothing Then Return New FaceTintInputBuilder.TintBuildResult()

        ' App-specific: NPC record + LooksMenu preset overlay -> concrete npcData.
        Dim npcData = NpcRecordOverlay.ApplyPresetOverlayToNpcData(
            NpcRecordOverlay.GetParsedNpc(modelFormID, pluginManager),
            rootFormID, appliedPresets, pluginManager, Nothing, parseRace)
        If npcData Is Nothing Then Return New FaceTintInputBuilder.TintBuildResult()
        ' El caller pasa la raza EFECTIVA (state.RaceFormID, con el override del editor); el npcData recién
        ' parseado trae la cruda del récord. Alinearlas acá deja el resultado auto-consistente (built.race y
        ' built.npcData.RaceFormID = la misma raza) — sin esto, tras un cambio de raza los consumidores que
        ' leían npcData.RaceFormID componían la CARA con el catálogo de la raza vieja. Mutar es seguro:
        ' GetParsedNpc parsea fresco (sin cache) y el shadow del preset también es una copia propia.
        If raceFormID <> 0UI AndAlso npcData.RaceFormID <> raceFormID Then
            npcData.RaceFormID = raceFormID
            npcData.HasRace = True
        End If

        Dim raceRec = pluginManager.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return New FaceTintInputBuilder.TintBuildResult()
        Dim race = If(parseRace IsNot Nothing, parseRace(raceRec), RecordParsers.ParseRACE(raceRec, pluginManager))

        ' App-specific: fold LooksMenu CUSTOM tint templates (Data\F4SE\Plugins\F4EE\Tints\...) into the
        ' race's tint groups so an NPC's applied tints against a mod-added template resolve + compose. This
        ' is the SINGLE seam both live render (NpcFaceTintResolver) and the offline bake (FaceGenBuilder)
        ' route through, so it also covers the bake. Idempotent + no-op when no custom tints exist.
        ' Mismo criterio que el registro de LUTs: el Data\ efectivo del caller, no el global. Con dataPath
        ' Nothing (camino de la app) la sobrecarga resuelve el Config_App y queda igual que antes.
        LmCustomTintLoader.EnsureMerged(race, pluginManager, If(dataPath, Config_App.Current?.DataPath))

        ' Generic, record-driven composition lives in the library.
        ' dataPath viaja hasta el builder: es de donde sale el registro de LUTs de pelo. Nothing = el
        ' Config_App global (camino de la app). El CLI headless honra --data y NO puebla ese global, asi
        ' que sin este paso su bake leia el LUTs\ del Data de ESCRITURA en vez del de lectura.
        Return FaceTintInputBuilder.Build(npcData, race, isFemale, pluginManager, tintBytesCache,
                                          hairColorFormID, hasTextureLighting, textureLightingColorArgb,
                                          dataPath)
    End Function

End Module
