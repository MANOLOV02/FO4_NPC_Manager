Imports System.Drawing
Imports FO4_Base_Library

''' <summary>
''' Builds the per-NPC face tint inputs (region swaps + ordered layer list) from records.
''' Pure data — no GL state, no Model touch, no Textures_Dictionary access. Consumed by
''' both the live render path (MainForm.TryApplyFaceTints) and the offline bake
''' (FaceGenBuilder.BakeFaceTextures) so they share one source of truth for layer
''' composition + ordering.
'''
''' This module contains zero hidden state: every dependency is passed in
''' (pluginManager, appliedPresets, tintBytesCache). MainForm owns the cache instance
''' and forwards it on each call so a single process keeps a single decode-once cache;
''' standalone callers (the bake) can pass a fresh dictionary when they want isolation.
''' </summary>
Public Module FaceTintLayerBuilder

    ''' <summary>Parsed inputs for one NPC, ready to feed FaceTintCompositor. Both lists
    ''' are always non-Nothing; npcData/race may be Nothing when inputs can't be resolved
    ''' (no NPC, no RACE, or NPC has no FaceTintLayers).</summary>
    Public Class TintBuildResult
        Public Property Layers As New List(Of FaceTintLayerInput)
        Public Property RegionSwaps As New List(Of FaceRegionSwapInput)
        Public Property NpcData As NPC_Data
        Public Property Race As RACE_Data
    End Class

    ''' <summary>Build the layer + region-swap inputs for the NPC at <paramref name="modelFormID"/>,
    ''' applying the LooksMenu preset overlay registered for <paramref name="rootFormID"/>
    ''' (typically the same FormID as modelFormID for offline bakes; differs from modelFormID
    ''' for live render only when a template chain dereferences traits ≠ visual root).
    '''
    ''' <paramref name="tintBytesCache"/> is a process-lifetime cache of decoded DDS bytes
    ''' keyed by normalized texture path. Pass <c>Nothing</c> for an uncached one-shot read.
    ''' </summary>
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
                          Optional textureLightingColorArgb As Integer = 0) As TintBuildResult
        Dim result As New TintBuildResult()
        If pluginManager Is Nothing Then Return result

        Dim npcData = NpcRecordOverlay.ApplyPresetOverlayToNpcData(
            NpcRecordOverlay.GetParsedNpc(modelFormID, pluginManager),
            rootFormID, appliedPresets, pluginManager)
        If npcData Is Nothing Then Return result

        Dim raceRec = pluginManager.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return result
        Dim race = RecordParsers.ParseRACE(raceRec, pluginManager)

        result.NpcData = npcData
        result.Race = race
        result.RegionSwaps = BuildFaceRegionSwaps(npcData, race, isFemale, pluginManager, tintBytesCache)

        ' Merge NPC-declared layers with RACE defaults: for each TintTemplateGroup the NPC
        ' doesn't touch, inject every Option whose TTED is present (HasDefaultValue=True).
        ' Mirrors the engine's CK behaviour: groups not overridden by the NPC fall back to
        ' the race-authored defaults. The merged list is what the compositor consumes.
        Dim mergedLayers = MergeTintLayersWithRaceDefaults(npcData.FaceTintLayers, race, isFemale, pluginManager)

        ' Single skin-tone path: when the compositor reaches the slot-12 (SkinTone) rank it must
        ' apply the NPC's authored layer if present, else a synthetic stand-in built from QNAM
        ' (state.TextureLightingColor). Injecting it as a real layer here -- instead of a separate
        ' full-face SoftLight post-pass -- means it sequences in engine tint order: lower-rank
        ' tints compose under it, higher-rank details (brow slot 23, scars slot 21) on top, so
        ' details are no longer washed out. No-op when QNAM is absent, when the race has no
        ' slot-12 catalog (non-skin races), or when the NPC already authors a slot-12 layer.
        InjectSyntheticSkinToneLayer(mergedLayers, npcData, race, isFemale, hasTextureLighting, textureLightingColorArgb)
        ' Pass the caller-supplied HCLF through verbatim -- it is the engine-effective value
        ' (NPC.HCLF + TPLT chain + LM overlay + RACE.HCLF fallback) resolved by the caller's
        ' state pipeline. The builder must not second-guess it; if the caller decides the NPC
        ' has no hair colour (0), the brow override silently no-ops.
        result.Layers = BuildLayerList(npcData, race, isFemale, mergedLayers, pluginManager, tintBytesCache, hairLutPath, hairColorFormID)
        Return result
    End Function

    ''' <summary>Append a synthetic SkinTone (slot 12) layer to <paramref name="mergedLayers"/>
    ''' built from the NPC's QNAM TextureLighting colour, used when the NPC authors no slot-12
    ''' layer. It is added unordered; BuildLayerList sorts the whole list by RACE tint rank, so
    ''' it lands at slot-12's position and the compositor composes it there (details ranked after
    ''' compose on top). Color = QNAM RGB, opacity = QNAM.A, BlendOp resolves to SoftLight via
    ''' the slot-12 fallback. No-op if QNAM absent, race has no slot-12 option, or the NPC already
    ''' authors one.</summary>
    Private Sub InjectSyntheticSkinToneLayer(mergedLayers As List(Of MergedTintLayer),
                                             npcData As NPC_Data,
                                             race As RACE_Data,
                                             isFemale As Boolean,
                                             hasTextureLighting As Boolean,
                                             textureLightingColorArgb As Integer)
        If Not hasTextureLighting OrElse race Is Nothing Then Return
        Dim skinOpts = race.FindTintOptionsBySlot(TintSlot.SkinTone, isFemale)
        If skinOpts Is Nothing OrElse skinOpts.Count = 0 Then Return

        ' Already authored? (NPC has a FaceTintLayer whose Index belongs to a slot-12 option.)
        Dim skinIndices As New HashSet(Of UShort)(skinOpts.Select(Function(o) o.Index))
        If npcData.FaceTintLayers IsNot Nothing _
           AndAlso npcData.FaceTintLayers.Any(Function(tl) skinIndices.Contains(tl.Index)) Then
            Return
        End If

        Dim skinOpt = skinOpts(0)
        Dim qa As Integer = (textureLightingColorArgb >> 24) And &HFF
        Dim qr As Integer = (textureLightingColorArgb >> 16) And &HFF
        Dim qg As Integer = (textureLightingColorArgb >> 8) And &HFF
        Dim qb As Integer = textureLightingColorArgb And &HFF
        ' QNAM.A is the SoftLight intensity (0..255). NPC_FaceTintLayerData.Value is 0..100
        ' (opacity = Value/100 downstream), matching what the old uniform pass used (opacity =
        ' QNAM.A/255). 0 alpha -> Value 0 -> skipped by the zero-opacity gate, same as before.
        Dim qValue As Integer = CInt(Math.Round(qa / 2.55))
        Dim disc As UShort = If(skinOpt.EntryType = RACE_TintEntryType.TextureSet, CUShort(2), CUShort(1))
        Dim synthSkin As New NPC_FaceTintLayerData With {
            .Index = skinOpt.Index,
            .Value = qValue,
            .Discriminator = disc,
            .Color = Color.FromArgb(255, qr, qg, qb),
            .TemplateColorIndex = -1
        }
        mergedLayers.Add(New MergedTintLayer With {.Layer = synthSkin, .IsRaceDefault = False})
    End Sub

    ''' <summary>Merge an NPC's authored FaceTintLayers with the race-authored defaults from
    ''' RACE.FemaleTintTemplateGroups / MaleTintTemplateGroups. Rule:
    '''   For each TintTemplateGroup: if the NPC has any layer whose Index belongs to one of
    '''   the group's Options, the group is considered "overridden" by the NPC and no
    '''   defaults are injected. Otherwise, every Option in the group whose TTED is present
    '''   (HasDefaultValue=True) is injected as a virtual layer with Value = TTED * 100.
    ''' Inject ALL such options (not just Options[0]); per the user-confirmed schema reading,
    ''' multiple options in a group can carry their own TTED. Layers whose TTED resolves to
    ''' Value=0 still enter the merged list and are filtered out downstream by the
    ''' zeroOpacity gate; this keeps the editor able to display them as "default OFF" rows.
    ''' For Palette options whose TemplateColors list is non-empty, the virtual layer seeds
    ''' Color + TemplateColorIndex from TemplateColors[0]'s CLFM (matches OnAddTint's
    ''' "first colour" rule and the LM in-game behaviour where unset Palette entries land on
    ''' position 0). Mask / TextureSet entries leave Color white because they don't read it.
    ''' Output preserves all of <paramref name="npcLayers"/> verbatim, then appends virtuals.
    ''' </summary>
    Public Function MergeTintLayersWithRaceDefaults(npcLayers As IList(Of NPC_FaceTintLayerData),
                                                    race As RACE_Data,
                                                    isFemale As Boolean,
                                                    pluginManager As PluginManager) As List(Of MergedTintLayer)
        Dim result As New List(Of MergedTintLayer)
        Dim safeNpc As IList(Of NPC_FaceTintLayerData) = If(npcLayers, CType(New List(Of NPC_FaceTintLayerData)(), IList(Of NPC_FaceTintLayerData)))

        For Each tl In safeNpc
            result.Add(New MergedTintLayer With {.Layer = tl, .IsRaceDefault = False})
        Next

        If race Is Nothing Then Return result

        Dim groups = If(isFemale, race.FemaleTintTemplateGroups, race.MaleTintTemplateGroups)
        If groups Is Nothing OrElse groups.Count = 0 Then Return result

        Dim npcIndices As New HashSet(Of UShort)(safeNpc.Select(Function(tl) tl.Index))
        For Each grp In groups
            If grp.Options Is Nothing OrElse grp.Options.Count = 0 Then Continue For
            Dim grpCovered As Boolean = grp.Options.Any(Function(o) npcIndices.Contains(o.Index))
            If grpCovered Then Continue For
            For Each opt In grp.Options
                If Not opt.HasDefaultValue Then Continue For
                Dim valueByte As Byte = CByte(Math.Max(0, Math.Min(100, CInt(Math.Round(opt.DefaultValue * 100.0F)))))
                Dim disc As UShort = If(opt.EntryType = RACE_TintEntryType.TextureSet, CUShort(2), CUShort(1))
                Dim seedColor As Color = Color.FromArgb(255, 255, 255, 255)
                Dim seedTplIdx As Integer = -1
                If opt.EntryType = RACE_TintEntryType.Palette _
                   AndAlso opt.TemplateColors IsNot Nothing AndAlso opt.TemplateColors.Count > 0 _
                   AndAlso pluginManager IsNot Nothing Then
                    Dim firstTpl = opt.TemplateColors(0)
                    If firstTpl.ColorFormID <> 0UI Then
                        Dim rec = pluginManager.GetRecord(firstTpl.ColorFormID)
                        If rec IsNot Nothing AndAlso rec.Header.Signature = "CLFM" Then
                            Dim clfm = RecordParsers.ParseCLFM(rec, pluginManager)
                            If clfm IsNot Nothing AndAlso clfm.HasColor Then
                                seedColor = clfm.Color
                                seedTplIdx = CInt(firstTpl.TemplateIndex)
                            End If
                        End If
                    End If
                End If
                Dim virtualLayer As New NPC_FaceTintLayerData With {
                    .Index = opt.Index,
                    .Value = valueByte,
                    .Discriminator = disc,
                    .Color = seedColor,
                    .TemplateColorIndex = seedTplIdx
                }
                result.Add(New MergedTintLayer With {.Layer = virtualLayer, .IsRaceDefault = True})
            Next
        Next
        Return result
    End Function

    ''' <summary>One layer fed to the compositor + a flag marking whether it came from the
    ''' NPC's own FaceTintLayers (False) or was synthesized from a RACE default (True). The
    ''' editor uses the flag to render race-default rows in gray and refuse Remove on them;
    ''' the compositor ignores it.</summary>
    Public Class MergedTintLayer
        Public Property Layer As NPC_FaceTintLayerData
        Public Property IsRaceDefault As Boolean
    End Class

    ''' <summary>Build per-region MPPT TXST swaps from the active Morph Group presets.
    ''' Empty for NPCs whose chosen presets are vertex-only (no MPPT) — the typical case
    ''' for non-aged NPCs.</summary>
    Private Function BuildFaceRegionSwaps(npcData As NPC_Data,
                                          race As RACE_Data,
                                          isFemale As Boolean,
                                          pluginManager As PluginManager,
                                          tintBytesCache As Dictionary(Of String, Byte())) As List(Of FaceRegionSwapInput)
        Dim swaps As New List(Of FaceRegionSwapInput)
        If npcData Is Nothing OrElse race Is Nothing Then Return swaps
        If npcData.MorphValues Is Nothing OrElse npcData.MorphValues.Count = 0 Then Return swaps

        Dim morphGroups = If(isFemale, race.FemaleMorphGroups, race.MaleMorphGroups)
        If morphGroups Is Nothing OrElse morphGroups.Count = 0 Then Return swaps

        For Each g In morphGroups
            Dim slot As TintSlot
            If Not g.TryGetMaskSlot(slot) Then Continue For
            Dim slotOpts = race.FindTintOptionsBySlot(slot, isFemale)
            If slotOpts.Count = 0 Then Continue For
            Dim maskOpt = slotOpts(0)
            If maskOpt.Textures Is Nothing OrElse maskOpt.Textures.Count = 0 Then Continue For
            Dim maskLoad = LoadTintLayerBytesAndKey(maskOpt.Textures(0), tintBytesCache)
            If maskLoad.Bytes Is Nothing Then Continue For

            For Each p In g.Presets
                If p.TextureFormID = 0UI Then Continue For
                Dim msdvVal As Single = 0F
                If Not npcData.MorphValues.TryGetValue(p.Index, msdvVal) Then Continue For
                If msdvVal <= 0.001F Then Continue For

                Dim txstRec = pluginManager.GetRecord(p.TextureFormID)
                If txstRec Is Nothing OrElse txstRec.Header.Signature <> "TXST" Then Continue For
                Dim txst = RecordParsers.ParseTXST(txstRec, pluginManager)
                If txst Is Nothing Then Continue For

                Dim diffLoad = LoadTintLayerBytesAndKey(txst.DiffuseTexture, tintBytesCache)
                Dim normLoad = LoadTintLayerBytesAndKey(txst.NormalTexture, tintBytesCache)
                Dim specLoad = LoadTintLayerBytesAndKey(txst.SmoothSpecTexture, tintBytesCache)

                If diffLoad.Bytes Is Nothing AndAlso normLoad.Bytes Is Nothing AndAlso specLoad.Bytes Is Nothing Then
                    Continue For
                End If

                Dim sw As New FaceRegionSwapInput With {
                    .RegionMaskDdsBytes = maskLoad.Bytes,
                    .RegionMaskCacheKey = maskLoad.Key,
                    .SwapDiffuseDdsBytes = diffLoad.Bytes,
                    .SwapDiffuseCacheKey = If(diffLoad.Bytes IsNot Nothing, diffLoad.Key, Nothing),
                    .SwapNormalDdsBytes = normLoad.Bytes,
                    .SwapNormalCacheKey = If(normLoad.Bytes IsNot Nothing, normLoad.Key, Nothing),
                    .SwapSpecularDdsBytes = specLoad.Bytes,
                    .SwapSpecularCacheKey = If(specLoad.Bytes IsNot Nothing, specLoad.Key, Nothing),
                    .DebugName = $"{g.Name}/{p.PresetName}"
                }
                swaps.Add(sw)
            Next
        Next
        Return swaps
    End Function

    ''' <summary>Build the full ordered FaceTintLayerInput list for the NPC. Layers are emitted
    ''' in RACE-Group order (the order Options appear in the gender's TintTemplateGroups), NOT
    ''' the ESP raw TETI order on the NPC record. Engine FO4 applies tints this way at runtime
    ''' (verified by diffing PiperESPM.json LM in-game TintOrder against the NPC's ESP order:
    ''' LM emits the RACE-Group order). SoftLight and other non-commutative blend ops give
    ''' visibly different results when the order changes.</summary>
    Private Function BuildLayerList(npcData As NPC_Data,
                                    race As RACE_Data,
                                    isFemale As Boolean,
                                    mergedLayers As List(Of MergedTintLayer),
                                    pluginManager As PluginManager,
                                    tintBytesCache As Dictionary(Of String, Byte()),
                                    hairLutPath As String,
                                    hairColorFormID As UInteger) As List(Of FaceTintLayerInput)
        Dim layerInputs As New List(Of FaceTintLayerInput)

        Dim stat_added_palette As Integer = 0
        Dim stat_added_textureSet As Integer = 0
        Dim stat_added_takesSkinTone As Integer = 0
        Dim stat_skip_zeroOpacity As Integer = 0
        Dim stat_skip_zeroOpacity_takesSkinTone As Integer = 0
        Dim stat_skip_missingOption As Integer = 0
        Dim stat_skip_missingMask As Integer = 0
        Dim stat_skip_unknownDiscriminator As Integer = 0
        Dim stat_byFlags_added As New Dictionary(Of UShort, Integer)
        Dim stat_byFlags_skipped As New Dictionary(Of UShort, Integer)

        Dim raceDefaultCount As Integer = Enumerable.Count(mergedLayers, Function(m) m.IsRaceDefault)
        Dim npcOwnCount As Integer = mergedLayers.Count - raceDefaultCount

        Dim raceTintRank As New Dictionary(Of UShort, Integer)
        Dim tintGroupsForRender = If(isFemale, race.FemaleTintTemplateGroups, race.MaleTintTemplateGroups)
        Dim totalOptionsAcrossGroups As Integer = 0
        If tintGroupsForRender IsNot Nothing Then
            For Each grpDiag In tintGroupsForRender
                totalOptionsAcrossGroups += If(grpDiag.Options Is Nothing, 0, grpDiag.Options.Count)
            Next
        End If
        Dim totalGroupsLog As Integer = If(tintGroupsForRender Is Nothing, 0, tintGroupsForRender.Count)

        For Each mDiag In mergedLayers
            Dim tlDiag = mDiag.Layer
            Dim optDiag = race.FindTintOption(tlDiag.Index, isFemale)
            Dim optName = If(optDiag IsNot Nothing AndAlso Not String.IsNullOrEmpty(optDiag.Name), optDiag.Name, "<no-option>")
            Dim slotName = If(optDiag IsNot Nothing, TintSlotName(optDiag.Slot), "?")
            Dim slotNum = If(optDiag IsNot Nothing, optDiag.Slot, CUShort(0))
            Dim flagsHex = If(optDiag IsNot Nothing, $"0x{optDiag.Flags:X4}", "?")
            Dim takesSkin = If(optDiag IsNot Nothing AndAlso (optDiag.Flags And &H4US) <> 0US, "TakesSkinTone", "-")
            Dim valueLog = tlDiag.Value
            Dim origin = If(mDiag.IsRaceDefault, "RACE-DEFAULT", "NPC")
        Next
        Dim renderRank As Integer = 0
        For Each grp In tintGroupsForRender
            For Each o In grp.Options
                If Not raceTintRank.ContainsKey(o.Index) Then
                    raceTintRank(o.Index) = renderRank
                    renderRank += 1
                End If
            Next
        Next
        Dim orderedLayers = mergedLayers.
            Select(Function(m, originalIdx)
                       Dim r As Integer = Integer.MaxValue
                       raceTintRank.TryGetValue(m.Layer.Index, r)
                       Return New With {.Layer = m.Layer, .Rank = r, .Idx = originalIdx}
                   End Function).
            OrderBy(Function(x) x.Rank).
            ThenBy(Function(x) x.Idx).
            Select(Function(x) x.Layer).
            ToList()

        For Each tl In orderedLayers
            Dim opt = race.FindTintOption(tl.Index, isFemale)
            Dim rawOptFlagsU = If(opt IsNot Nothing, opt.Flags, CUShort(0))
            Dim rawOptFlagsHex = If(opt IsNot Nothing, $"0x{opt.Flags:X4}", "?")
            Dim rawOptFlagsName = If(opt IsNot Nothing, FormatTintFlagsName(opt.Flags), "?")

            If opt Is Nothing OrElse opt.Textures Is Nothing OrElse opt.Textures.Count = 0 Then
                stat_skip_missingOption += 1
                If Not stat_byFlags_skipped.ContainsKey(rawOptFlagsU) Then stat_byFlags_skipped(rawOptFlagsU) = 0
                stat_byFlags_skipped(rawOptFlagsU) += 1
                Continue For
            End If

            Dim takesSkinTone As Boolean = (opt.Flags And &H4US) <> 0US

            If tl.RawTendBytes IsNot Nothing AndAlso tl.RawTendBytes.Length > 0 Then
                Dim hex As New System.Text.StringBuilder()
                For i As Integer = 0 To tl.RawTendBytes.Length - 1
                    If i > 0 Then hex.Append(",")
                    hex.Append($"0x{tl.RawTendBytes(i):X2}")
                Next
                Dim unusedByte As String = "N/A"
                Dim tplLo As String = "N/A"
                Dim tplHi As String = "N/A"
                Dim unusedFlag As String = ""
                If tl.RawTendBytes.Length >= 5 Then
                    unusedByte = $"0x{tl.RawTendBytes(4):X2}"
                    If tl.RawTendBytes(4) <> 0 Then unusedFlag = " *** UNUSED-BYTE NON-ZERO ***"
                End If
                If tl.RawTendBytes.Length >= 7 Then
                    tplLo = $"0x{tl.RawTendBytes(5):X2}"
                    tplHi = $"0x{tl.RawTendBytes(6):X2}"
                End If
            End If

            Dim opacity As Single = CSng(tl.Value) / 100.0F
            If opacity <= 0.001F Then
                stat_skip_zeroOpacity += 1
                If takesSkinTone Then stat_skip_zeroOpacity_takesSkinTone += 1
                Dim warn = If(takesSkinTone, " <<< takesSkinTone -- N/S also lost here", "")
                If Not stat_byFlags_skipped.ContainsKey(rawOptFlagsU) Then stat_byFlags_skipped(rawOptFlagsU) = 0
                stat_byFlags_skipped(rawOptFlagsU) += 1
                Continue For
            End If

            Dim ttet0Snap = If(opt.Textures.Count > 0, opt.Textures(0), "")
            Dim ttet1Snap = If(opt.Textures.Count > 1, opt.Textures(1), "")
            Dim ttet2Snap = If(opt.Textures.Count > 2, opt.Textures(2), "")
            Dim diffuseLoad = LoadTintLayerBytesAndKey(opt.Textures(0), tintBytesCache)
            If diffuseLoad.Bytes Is Nothing Then
                stat_skip_missingMask += 1
                If Not stat_byFlags_skipped.ContainsKey(rawOptFlagsU) Then stat_byFlags_skipped(rawOptFlagsU) = 0
                stat_byFlags_skipped(rawOptFlagsU) += 1
                Continue For
            End If
            Dim diffuseBytes = diffuseLoad.Bytes
            Dim diffuseKey = diffuseLoad.Key

            Dim normalBytes As Byte() = Nothing
            Dim normalKey As String = Nothing
            Dim specularBytes As Byte() = Nothing
            Dim specularKey As String = Nothing
            If tl.Discriminator = 2 Then
                If opt.Textures.Count >= 2 Then
                    Dim n = LoadTintLayerBytesAndKey(opt.Textures(1), tintBytesCache)
                    normalBytes = n.Bytes
                    If normalBytes IsNot Nothing Then normalKey = n.Key
                End If
                If opt.Textures.Count >= 3 Then
                    Dim s = LoadTintLayerBytesAndKey(opt.Textures(2), tintBytesCache)
                    specularBytes = s.Bytes
                    If specularBytes IsNot Nothing Then specularKey = s.Key
                End If
            End If

            Dim layerInput As New FaceTintLayerInput With {
                .LayerDdsBytes = diffuseBytes,
                .LayerCacheKey = diffuseKey,
                .NormalDdsBytes = normalBytes,
                .NormalCacheKey = normalKey,
                .SpecularDdsBytes = specularBytes,
                .SpecularCacheKey = specularKey,
                .Opacity = opacity,
                .TakesSkinTone = takesSkinTone,
                .DebugName = opt.Name
            }

            If tl.Discriminator = 1 Then
                layerInput.Kind = FaceTintLayerKind.PaletteMask
                Dim resolved = ResolvePaletteLayerEffective(tl, opt)
                layerInput.R = resolved.Color.R
                layerInput.G = resolved.Color.G
                layerInput.B = resolved.Color.B
                layerInput.BlendOp = CInt(resolved.BlendOp)
                layerInput.Opacity = opacity
                Dim resolveMode As String = If(resolved.Matched, "PRESET (match TTEC.TemplateIndex)", "CUSTOM (no match — tendRGB + TTEC(1).BlendOp)")
                If opt IsNot Nothing AndAlso opt.TemplateColors IsNot Nothing AndAlso opt.TemplateColors.Count > 0 Then
                    Dim sb As New System.Text.StringBuilder()
                    For i = 0 To opt.TemplateColors.Count - 1
                        Dim tc = opt.TemplateColors(i)
                        Dim rgbStr As String = "(?)"
                        If tc.ColorFormID <> 0UI AndAlso pluginManager IsNot Nothing Then
                            Dim cr = pluginManager.GetRecord(tc.ColorFormID)
                            If cr IsNot Nothing AndAlso cr.Header.Signature = "CLFM" Then
                                Dim cc = RecordParsers.ParseCLFM(cr, pluginManager)
                                If cc IsNot Nothing AndAlso cc.HasColor Then
                                    rgbStr = $"({cc.Color.R},{cc.Color.G},{cc.Color.B})"
                                End If
                            End If
                        End If
                        If i > 0 Then sb.Append(" | ")
                        sb.Append($"[pos={i} TemplateIndex={tc.TemplateIndex} CLFM={tc.ColorFormID:X8} rgb={rgbStr} blendOp={tc.BlendOperation}]")
                    Next
                End If
            ElseIf tl.Discriminator = 2 Then
                layerInput.Kind = FaceTintLayerKind.TextureSetDiffuse
                layerInput.BlendOp = CInt(ResolveFallbackBlendOp(opt))
            Else
                stat_skip_unknownDiscriminator += 1
                If Not stat_byFlags_skipped.ContainsKey(rawOptFlagsU) Then stat_byFlags_skipped(rawOptFlagsU) = 0
                stat_byFlags_skipped(rawOptFlagsU) += 1
                Continue For
            End If

            ' Slot Brows (23) override: regardless of the layer's authored RGB or
            ' TemplateColorIndex, the colour is sourced from the NPC's hair (HCLF). Applies to
            ' BOTH layer kinds (vanilla brow opts are TextureSet per RecordParsers.vb:1030 with
            ' T=3 C=0; PaletteMask is supported for completeness when modders author tint-style
            ' brow layers).
            '   HCLF.HasColor (RGB CLFM):
            '     - PaletteMask: override layerInput.R/G/B; existing shader path already uses uColor.
            '     - TextureSet : override layerInput.R/G/B AND set ForceUniformColor so the
            '                    shader's TS branch substitutes uColor for layerSample.rgb while
            '                    keeping shape via alpha.
            '   HCLF.HasRemappingIndex (palette CLFM): set UseHairPalette + LUT + row. The shader
            '     picks the X source per-kind (mask.r for Palette, grayscale of layerSample.rgb
            '     for TextureSet), mirroring the formula the brow MESH grayscale-to-palette uses.
            ' No-op when the NPC has no HCLF, when the CLFM resolves to neither flag, or (in the
            ' palette branch) when the LUT bytes don't load.
            If opt.Slot = CUShort(TintSlot.Brows) Then
                Dim browIdxLog = tl.Index, browDiscLog = tl.Discriminator, browKindLog = layerInput.Kind
                Dim browHairFidLog = hairColorFormID, browLutLog = hairLutPath
                Dim browAction As String = "no-op (default)"
                Dim browClfm As CLFM_Data = Nothing
                If hairColorFormID = 0UI Then
                    browAction = "no-op (NPC has no HCLF -- race fallback returned 0)"
                Else
                    Dim hairClfmRec = pluginManager.GetRecord(hairColorFormID)
                    If hairClfmRec Is Nothing OrElse hairClfmRec.Header.Signature <> "CLFM" Then
                        browAction = "no-op (HCLF record missing or wrong sig)"
                    Else
                        browClfm = RecordParsers.ParseCLFM(hairClfmRec, pluginManager)
                        If browClfm Is Nothing Then
                            browAction = "no-op (CLFM parse failed)"
                        ElseIf browClfm.HasColor Then
                            layerInput.R = browClfm.Color.R
                            layerInput.G = browClfm.Color.G
                            layerInput.B = browClfm.Color.B
                            If layerInput.Kind = FaceTintLayerKind.TextureSetDiffuse Then
                                layerInput.ForceUniformColor = True
                            End If
                            browAction = $"RGB override ({browClfm.Color.R},{browClfm.Color.G},{browClfm.Color.B}){If(layerInput.ForceUniformColor, " [ForceUniformColor=True]", "")}"
                        ElseIf browClfm.HasRemappingIndex Then
                            If String.IsNullOrEmpty(hairLutPath) Then
                                browAction = "no-op (HasRemappingIndex but hairLutPath empty)"
                            Else
                                Dim lutLoad = LoadTintLayerBytesAndKey(hairLutPath, tintBytesCache)
                                If lutLoad.Bytes Is Nothing Then
                                    browAction = $"no-op (LUT bytes failed to load from '{hairLutPath}')"
                                Else
                                    layerInput.UseHairPalette = True
                                    layerInput.HairLutDdsBytes = lutLoad.Bytes
                                    layerInput.HairLutCacheKey = lutLoad.Key
                                    layerInput.HairPaletteRow = browClfm.RemappingIndex
                                    browAction = $"LUT remap (row={browClfm.RemappingIndex:F4}, key='{lutLoad.Key}')"
                                End If
                            End If
                        Else
                            browAction = "no-op (CLFM has neither HasColor nor HasRemappingIndex)"
                        End If
                    End If
                End If
                Dim actLog = browAction
                Logger.LogLazy(Function() $"[BROW-TINT] tl.Index={browIdxLog} disc={browDiscLog} kind={browKindLog} hairFid=0x{browHairFidLog:X8} lutPath='{browLutLog}' -> {actLog}")

                ' Diagnostic: decode the brow diffuse (TTET[0]) and characterize its channels over
                ' the opaque region (alpha>16). Tells us whether the texture is grayscale (R==G==B)
                ' or coloured, and what range its luminance grayscale + green fall in -- that range
                ' is the X coordinate the shader feeds the LUT, so it explains a "too light/dark"
                ' brow directly. Sampled on a coarse stride to stay cheap.
                If Logger.Enabled AndAlso diffuseBytes IsNot Nothing Then
                    Dim browTexLog = ttet0Snap
                    Try
                        Dim tex = DirectXTexWrapperCLI.Loader.ConvertForBitmap(diffuseBytes)
                        If tex IsNot Nothing AndAlso tex.Loaded AndAlso tex.Levels IsNot Nothing AndAlso tex.Levels.Count > 0 Then
                            Dim lvl = tex.Levels(0)
                            If lvl IsNot Nothing AndAlso lvl.Data IsNot Nothing AndAlso lvl.Width > 0 AndAlso lvl.Height > 0 Then
                                Dim w = lvl.Width, h = lvl.Height
                                Dim stride = w * 4
                                Dim stepPx = Math.Max(1, CInt(Math.Min(w, h) \ 64))
                                Dim n As Long = 0
                                Dim sumR As Long = 0, sumG As Long = 0, sumB As Long = 0
                                Dim minR As Integer = 255, maxR As Integer = 0
                                Dim minG As Integer = 255, maxG As Integer = 0
                                Dim minB As Integer = 255, maxB As Integer = 0
                                ' Alpha tracked over ALL sampled pixels (not gated on opaque) so we see
                                ' whether the brow shape lives in alpha and what its peak/range is --
                                ' candidate X source if RGB is flat-dark.
                                Dim totalScanned As Long = 0
                                Dim sumAall As Long = 0, minAall As Integer = 255, maxAall As Integer = 0
                                Dim sumAopaque As Long = 0, minAop As Integer = 255, maxAop As Integer = 0
                                Dim y = 0
                                While y < h
                                    Dim x = 0
                                    While x < w
                                        Dim idx = y * stride + x * 4
                                        If idx + 3 < lvl.Data.Length Then
                                            ' ConvertForBitmap = BGRA byte order.
                                            Dim b = CInt(lvl.Data(idx + 0))
                                            Dim g = CInt(lvl.Data(idx + 1))
                                            Dim r = CInt(lvl.Data(idx + 2))
                                            Dim a = CInt(lvl.Data(idx + 3))
                                            totalScanned += 1
                                            sumAall += a
                                            If a < minAall Then minAall = a
                                            If a > maxAall Then maxAall = a
                                            If a > 16 Then
                                                n += 1
                                                sumR += r : sumG += g : sumB += b
                                                sumAopaque += a
                                                If a < minAop Then minAop = a
                                                If a > maxAop Then maxAop = a
                                                If r < minR Then minR = r
                                                If r > maxR Then maxR = r
                                                If g < minG Then minG = g
                                                If g > maxG Then maxG = g
                                                If b < minB Then minB = b
                                                If b > maxB Then maxB = b
                                            End If
                                        End If
                                        x += stepPx
                                    End While
                                    y += stepPx
                                End While
                                Dim avgAall = If(totalScanned > 0, CInt(sumAall \ totalScanned), 0)
                                If n > 0 Then
                                    Dim avgR = CInt(sumR \ n), avgG = CInt(sumG \ n), avgB = CInt(sumB \ n)
                                    Dim avgAop = CInt(sumAopaque \ n)
                                    Dim avgGray = (0.299F * avgR + 0.587F * avgG + 0.114F * avgB) / 255.0F
                                    Dim avgGN = avgG / 255.0F
                                    Dim avgAopN = avgAop / 255.0F
                                    Dim looksGray = (Math.Abs(avgR - avgG) <= 4 AndAlso Math.Abs(avgG - avgB) <= 4)
                                    Logger.LogLazy(Function() $"[BROW-TEX] tex='{browTexLog}' {w}x{h} scanned={totalScanned} opaque={n} avgRGB=({avgR},{avgG},{avgB}) R[{minR}..{maxR}] G[{minG}..{maxG}] B[{minB}..{maxB}] alphaOpaque(avg={avgAop} [{minAop}..{maxAop}]) alphaAll(avg={avgAall} [{minAall}..{maxAall}]) -> grayX={avgGray:F4} greenX={avgGN:F4} alphaX={avgAopN:F4} looksGrayscale={looksGray}")
                                Else
                                    Logger.LogLazy(Function() $"[BROW-TEX] tex='{browTexLog}' {w}x{h} scanned={totalScanned} -> no opaque samples; alphaAll(avg={avgAall} [{minAall}..{maxAall}])")
                                End If
                            End If
                        End If
                    Catch ex As Exception
                        Logger.LogLazy(Function() $"[BROW-TEX] tex='{browTexLog}' decode failed: {ex.Message}")
                    End Try
                End If
            End If

            Dim slotNm = TintSlotName(opt.Slot)
            Dim opName = BlendOpName(CUInt(layerInput.BlendOp))
            Dim chans = "D"
            If normalBytes IsNot Nothing Then chans &= "+N"
            If specularBytes IsNot Nothing Then chans &= "+S"
            layerInputs.Add(layerInput)

            If layerInput.Kind = FaceTintLayerKind.PaletteMask Then
                stat_added_palette += 1
            Else
                stat_added_textureSet += 1
            End If
            If takesSkinTone Then stat_added_takesSkinTone += 1
            If Not stat_byFlags_added.ContainsKey(rawOptFlagsU) Then stat_byFlags_added(rawOptFlagsU) = 0
            stat_byFlags_added(rawOptFlagsU) += 1
        Next

        Dim allFlagKeys As New SortedSet(Of UShort)
        For Each k In stat_byFlags_added.Keys : allFlagKeys.Add(k) : Next
        For Each k In stat_byFlags_skipped.Keys : allFlagKeys.Add(k) : Next
        For Each fk In allFlagKeys
            Dim a As Integer = 0 : stat_byFlags_added.TryGetValue(fk, a)
            Dim s As Integer = 0 : stat_byFlags_skipped.TryGetValue(fk, s)
        Next


        Return layerInputs
    End Function

    ''' <summary>Fallback BlendOp used whenever no preset match is available (disc=1 CUSTOM,
    ''' or disc=2 TextureSet). Rule: TTEC pos=0 is the "None/Nada" placeholder (Default blend);
    ''' the first real preset at pos=1 carries the authored BlendOp (usually SoftLight). The
    ''' option-level TTEB (opt.BlendOperation) is almost always empty in vanilla data, so it's
    ''' a last-resort fallback, not a primary source.</summary>
    Public Function ResolveFallbackBlendOp(opt As RACE_TintTemplateOption) As UInteger
        If opt Is Nothing Then Return 0UI
        Dim raw As UInteger
        If opt.TemplateColors IsNot Nothing AndAlso opt.TemplateColors.Count >= 2 Then
            raw = opt.TemplateColors(1).BlendOperation
        ElseIf opt.TemplateColors IsNot Nothing AndAlso opt.TemplateColors.Count = 1 Then
            raw = opt.TemplateColors(0).BlendOperation
        Else
            raw = opt.BlendOperation
        End If
        ' Slot SkinTone (12) + BlendOp Default (0) → promover a SoftLight (3). Default sobre
        ' skin tone aplica como overlay plano que aplasta la luminancia del diffuse autoreado;
        ' SoftLight es la fórmula vanilla canónica para slot 12 (matchea TryApplyFaceSkinSoftLight
        ' fallback + el body softlight path). Aplicado en el helper de resolución para que TODOS
        ' los consumers de ResolveFallbackBlendOp/ResolvePaletteLayerEffective hereden la regla.
        ' Convenio: 0=Default, 1=Multiply, 2=Overlay, 3=SoftLight, 4=HardLight (ver BlendOpName).
        If opt.Slot = CUShort(TintSlot.SkinTone) AndAlso raw = 0UI Then
            Return 3UI
        End If
        Return raw
    End Function

    ''' <summary>Resolve effective Color/BlendOp/OpacityScale for a Palette (disc=1) layer.
    ''' Lookup by VALUE of TTEC entry's TemplateIndex matching TEND.TemplateColorIndex (not by
    ''' array position). On match: TEND RGB (preserved verbatim) + preset BlendOp + preset Alpha.
    ''' On no match (CUSTOM): tendRGB + ResolveFallbackBlendOp(opt) + opacityScale 1.0.
    ''' </summary>
    Public Function ResolvePaletteLayerEffective(tl As NPC_FaceTintLayerData, opt As RACE_TintTemplateOption) As (Color As Color, BlendOp As UInteger, Matched As Boolean, OpacityScale As Single)
        Dim resolvedColor As Color = tl.Color
        Dim resolvedBlendOp As UInteger = ResolveFallbackBlendOp(opt)
        Dim matched As Boolean = False
        Dim opacityScale As Single = 1.0F

        If opt IsNot Nothing Then
            If opt.TemplateColors IsNot Nothing AndAlso opt.TemplateColors.Count > 0 _
               AndAlso tl.TemplateColorIndex >= 0 Then
                Dim needle As UShort = CUShort(tl.TemplateColorIndex)
                Dim tplCol As RACE_TintTemplateColor = opt.TemplateColors.FirstOrDefault(
                    Function(t) t.TemplateIndex = needle)
                If tplCol IsNot Nothing Then
                    If tplCol.Alpha <= 0.0F Then
                        ' "Default neutral" placeholder: skip the match so we fall back to TEND
                        ' RGB + value (same path as TemplateColorIndex < 0).
                    Else
                        matched = True
                        resolvedBlendOp = tplCol.BlendOperation
                        opacityScale = tplCol.Alpha
                    End If
                End If
            End If
        End If

        ' Slot SkinTone + BlendOp Default → SoftLight. Misma regla que ResolveFallbackBlendOp;
        ' acá la re-aplicamos por si el match preset trajo tplCol.BlendOperation=0 (autoría rara).
        If opt IsNot Nothing AndAlso opt.Slot = CUShort(TintSlot.SkinTone) AndAlso resolvedBlendOp = 0UI Then
            resolvedBlendOp = 3UI
        End If

        Return (resolvedColor, resolvedBlendOp, matched, opacityScale)
    End Function

    ''' <summary>Resolve a tint layer texture path to its raw DDS bytes via FilesDictionary.
    ''' Returns Nothing on empty path, missing entry, or read failure.
    ''' Uses <paramref name="tintBytesCache"/> when supplied; cache keys are normalized paths.
    ''' Negative results are NOT cached (a failed read leaves the slot empty so a later
    ''' FilesDictionary refresh can resolve it).</summary>
    Public Function LoadTintLayerBytes(rawPath As String,
                                       tintBytesCache As Dictionary(Of String, Byte())) As Byte()
        If String.IsNullOrEmpty(rawPath) Then Return Nothing
        Dim normalized = NormalizeDictionaryKeyWithTexturesPrefix(rawPath)
        Return LoadTintLayerBytesByKey(normalized, tintBytesCache)
    End Function

    ''' <summary>Two-output variant: returns the bytes AND the normalized cache key so the
    ''' caller can hand the key to the GPU cache (FaceTintTextureCache) for decode reuse.
    ''' Returns (Nothing, "") when rawPath is empty or unresolvable.</summary>
    Public Function LoadTintLayerBytesAndKey(rawPath As String,
                                             tintBytesCache As Dictionary(Of String, Byte())) As (Bytes As Byte(), Key As String)
        If String.IsNullOrEmpty(rawPath) Then Return (Nothing, "")
        Dim normalized = NormalizeDictionaryKeyWithTexturesPrefix(rawPath)
        Dim bytes = LoadTintLayerBytesByKey(normalized, tintBytesCache)
        Return (bytes, normalized)
    End Function

    ''' <summary>Cached lookup keyed by the already-normalized dictionary key. Same key is
    ''' used as the GPU cache key in FaceTintTextureCache, so the byte cache and the
    ''' GL-texture cache stay paired entry-for-entry.</summary>
    Public Function LoadTintLayerBytesByKey(normalizedKey As String,
                                            tintBytesCache As Dictionary(Of String, Byte())) As Byte()
        If String.IsNullOrEmpty(normalizedKey) Then Return Nothing
        Dim cached As Byte() = Nothing
        If tintBytesCache IsNot Nothing AndAlso tintBytesCache.TryGetValue(normalizedKey, cached) AndAlso cached IsNot Nothing Then Return cached

        Dim result As Byte() = Nothing
        Dim loc As FilesDictionary_class.File_Location = Nothing
        If FilesDictionary_class.Dictionary.TryGetValue(normalizedKey, loc) Then
            Try
                Dim bytes = loc.GetBytes()
                If bytes IsNot Nothing AndAlso bytes.Length > 0 Then result = bytes
            Catch
                result = Nothing
            End Try
        End If

        If result IsNot Nothing AndAlso tintBytesCache IsNot Nothing Then tintBytesCache(normalizedKey) = result
        Return result
    End Function

    Public Function TintSlotName(slot As UShort) As String
        Static names As String() = {
            "ForeheadMask", "EyesMask", "NoseMask", "EarsMask", "CheeksMask", "MouthMask", "NeckMask",
            "LipColor", "CheekColor", "Eyeliner", "EyeSocketUpper", "EyeSocketLower", "SkinTone",
            "Paint", "LaughLines", "CheekColorLower", "Nose", "Chin", "Neck", "Forehead", "Dirt",
            "Scars", "FaceDetail", "Brow", "Wrinkles", "Beard"
        }
        If slot >= names.Length Then Return "?"
        Return names(slot)
    End Function

    Public Function BlendOpName(op As UInteger) As String
        Select Case op
            Case 0 : Return "Default"
            Case 1 : Return "Multiply"
            Case 2 : Return "Overlay"
            Case 3 : Return "SoftLight"
            Case 4 : Return "HardLight"
            Case Else : Return $"?{op}"
        End Select
    End Function

    ''' <summary>Decode TTEF flags U16 to a readable name. Diagnostic only.</summary>
    Public Function FormatTintFlagsName(flags As UShort) As String
        Dim parts As New List(Of String)
        If (flags And &H1US) <> 0US Then parts.Add("OnOffOnly")
        If (flags And &H2US) <> 0US Then parts.Add("ChargenDetail")
        If (flags And &H4US) <> 0US Then parts.Add("TakesSkinTone")
        Dim unknown As UShort = CUShort(flags And &HFFF8US)
        If unknown <> 0US Then parts.Add($"unknown=0x{unknown:X4}")
        If parts.Count = 0 Then Return "none"
        Return String.Join("+", parts)
    End Function

    ''' <summary>Normalize a texture path for FilesDictionary lookup (ensures "textures\" prefix).</summary>
    Public Function NormalizeDictionaryKeyWithTexturesPrefix(rawPath As String) As String
        Return FO4UnifiedMaterial_Class.CorrectTexturePath(rawPath)
    End Function

End Module
