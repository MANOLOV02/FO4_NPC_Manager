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
    Public Function Build(modelFormID As UInteger,
                          rootFormID As UInteger,
                          raceFormID As UInteger,
                          isFemale As Boolean,
                          pluginManager As PluginManager,
                          appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                          tintBytesCache As Dictionary(Of String, Byte()),
                          Optional hairLutPath As String = "",
                          Optional hairColorFormID As UInteger = 0UI,
                          Optional hasTextureLighting As Boolean = False,
                          Optional textureLightingColorArgb As Integer = 0) As FaceTintInputBuilder.TintBuildResult
        If pluginManager Is Nothing Then Return New FaceTintInputBuilder.TintBuildResult()

        ' App-specific: NPC record + LooksMenu preset overlay -> concrete npcData.
        Dim npcData = NpcRecordOverlay.ApplyPresetOverlayToNpcData(
            NpcRecordOverlay.GetParsedNpc(modelFormID, pluginManager),
            rootFormID, appliedPresets, pluginManager)
        If npcData Is Nothing Then Return New FaceTintInputBuilder.TintBuildResult()

        Dim raceRec = pluginManager.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return New FaceTintInputBuilder.TintBuildResult()
        Dim race = RecordParsers.ParseRACE(raceRec, pluginManager)

        ' Generic, record-driven composition lives in the library.
        Return FaceTintInputBuilder.Build(npcData, race, isFemale, pluginManager, tintBytesCache,
                                          hairLutPath, hairColorFormID, hasTextureLighting, textureLightingColorArgb)
    End Function

End Module
