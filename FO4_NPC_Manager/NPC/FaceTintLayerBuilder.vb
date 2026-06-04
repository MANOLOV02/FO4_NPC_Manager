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
                ' Gate de msdv<=0.001 REMOVIDO (a pedido): se incluye el swap aunque el msdv sea bajo o 0
                ' (Intensity baja -> running con cov chica; 0 = no-op). build_3 compone todo msdv>0; el
                ' gate viejo se comia valores fraccionarios bajos. (El TryGetValue de arriba sigue: si el
                ' NPC no tiene ese morph, no hay swap.)

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
                    .Intensity = msdvVal,
                    .DebugName = $"{g.Name}/{p.PresetName}"
                }
                Logger.LogLazy(Function() $"[REGIONSWAP-BUILD] '{g.Name}/{p.PresetName}' presetIdx={p.Index} msdv(intensity)={msdvVal:F3} txst=0x{p.TextureFormID:X8}")
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

        ' SkipEyebrowsTone.ini (appdir, case-insensitive): si existe, en vez del color de pelo (HCLF)
        ' las cejas usan una LUT SINTÉTICA de degradé entre Dark y Light (campos del INI, default negro).
        Dim eyebrowLut = BuildSyntheticEyebrowLut(tintBytesCache)

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
        ' Orden de composicion = ORDEN FISICO del record del template del RACE (reverse). El engine itera las
        ' tint-options en el orden en que estan ALMACENADAS en el RACE (grupo por grupo, opcion por opcion) y la
        ' PRIMERA listada queda ENCIMA (over-running: la de MAYOR posicion fisica se pinta PRIMERO=fondo, la de
        ' MENOR ULTIMO=encima). UNA sola clave = physIndexByOption (posicion fisica global de la opcion).
        ' 2026-06-03 DERIVADO + VALIDADO (Tools/FaceTintDerive, vs bakes de CK de Alana 0x0005E564):
        '   - PHYS-desc full-face 3.222 (mejor que group+teti 3.558 y que group+entry+slot+teti 3.543).
        '   - Matchea 10/10 los tops AISLADOS de CK del batch de composicion (OrderBatch.esp batch80), INCLUIDO
        '     el maquillaje (Lapiz de ojos 2 > Lapiz de ojos 6, etc.) y g1 (Cara emborronada ENCIMA de Resplandor
        '     doble) -- casos que group/slot/entry/teti-numero/blendop NO explicaban.
        '   - group_idx, slot, entry, teti-NUMERO y blendop eran PROXIES imperfectos del orden fisico. teti-numero
        '     ~ orden fisico salvo donde el record desordena: Cara (teti 1916) esta FISICAMENTE ANTES que
        '     Resplandor (teti 1915) -> teti-desc dejaba Resplandor VISIBLE; el orden fisico lo OCLUYE como CK.
        '   - Es propiedad del RACE (no de la lista del NPC) -> clone-independiente (compatible con batch79, que
        '     solo refuto el orden de la LISTA del NPC -idx-).
        ' TakesSkinTone: las flagged que componen ANTES del skintone no necesitan pre-toneo (el skintone compone
        ' encima); la regla de pre-toneo del compositor es INERTE para Alana pero DEBE estar (otros NPCs pueden
        ' tener flagged en posicion fisica <= skintone).
        Dim physIndexByOption As New Dictionary(Of UShort, Integer)
        Dim physPos As Integer = 0
        If tintGroupsForRender IsNot Nothing Then
            For giOrder As Integer = 0 To tintGroupsForRender.Count - 1
                Dim grpOrder = tintGroupsForRender(giOrder)
                If grpOrder.Options Is Nothing Then Continue For
                For Each optOrder In grpOrder.Options
                    If Not physIndexByOption.ContainsKey(optOrder.Index) Then physIndexByOption(optOrder.Index) = physPos
                    physPos += 1
                Next
            Next
        End If
        ' Over-running: mayor posicion fisica = fondo (compone primero); menor = encima (compone ultimo).
        ' Tiebreak = orden original de la lista del NPC ascendente (las opciones del template son unicas, asi que
        ' el tiebreak solo aplica a opciones AUSENTES del template -> ambas Integer.MaxValue -> al fondo).
        Dim orderedLayers = mergedLayers.
            Select(Function(m, originalIdx) New With {m.Layer, .Idx = originalIdx,
                       .PhysIdx = If(physIndexByOption.ContainsKey(m.Layer.Index), physIndexByOption(m.Layer.Index), Integer.MaxValue)}).
            OrderByDescending(Function(x) x.PhysIdx).
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
            ' Gate de zero-opacity REMOVIDO (a pedido): se incluyen TODAS las capas sin filtrar por
            ' intensidad. Una capa con op=0 se compone como no-op (cov=g22_encode(mask)*0=0 -> acc sin
            ' cambio), = build_3 que compone toda capa con intensity>0. Los gates de option/textura/
            ' discriminador faltantes siguen abajo (esos SI saltan capas que no se pueden componer).

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
                .IsSkinTone = (opt.Slot = CUShort(TintSlot.SkinTone)),
                .Slot = opt.Slot,
                .IsTextureSet = (tl.Discriminator = 2),
                .DebugName = opt.Name
            }

            If tl.Discriminator = 1 Then
                layerInput.Kind = FaceTintLayerKind.PaletteMask
                Dim resolved = ResolvePaletteLayerEffective(tl, opt, pluginManager)
                layerInput.R = resolved.Color.R
                layerInput.G = resolved.Color.G
                layerInput.B = resolved.Color.B
                layerInput.BlendOp = CInt(resolved.BlendOp)
                ' OpacityScale (tplCol.Alpha del match) NO se aplica al opacity. Verificado
                ' empíricamente con analyzer 2026-05-28 (B01_idx11 Barra de labios): al multiplicar
                ' op por Alpha=0.5 el residual byte saltó de 0.39 a 8.46 → el engine NO multiplica
                ' TEND.Value por tplCol.Alpha. OpacityScale del resolver se preserva como info
                ' contextual del match pero no afecta el cómputo.
                layerInput.Opacity = opacity
                Dim resolveMode As String = If(resolved.Matched, "MATCH (Step1 idx or Step2 color)", "FALLBACK (Step3 mode)")
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
                layerInput.BlendOp = CInt(ResolveFallbackBlendOp(opt, opacity))
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
            If opt.Slot = CUShort(TintSlot.Brows) AndAlso eyebrowLut.Enabled Then
                ' SkipEyebrowsTone.ini presente: en vez del color de pelo (HCLF), la ceja usa la LUT
                ' sintética de degradé Dark->Light. X = grayscale del brow (col 0 = Dark, col 255 =
                ' Light). HairPaletteRow=0 (la LUT replica una sola fila).
                If eyebrowLut.Bytes IsNot Nothing Then
                    layerInput.UseHairPalette = True
                    layerInput.HairLutDdsBytes = eyebrowLut.Bytes
                    layerInput.HairLutCacheKey = eyebrowLut.Key
                    layerInput.HairPaletteRow = 0.0F
                End If
                Dim browSynthIdx = tl.Index
                Logger.LogLazy(Function() $"[BROW-TINT] tl.Index={browSynthIdx} -> SYNTHETIC LUT (SkipEyebrowsTone.ini): degrade Dark->Light")
            ElseIf opt.Slot = CUShort(TintSlot.Brows) Then
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

            ' [FACETINT-BUILD] one line per built layer with EVERY metadata field that goes into
            ' the shader (and several that do not, for the python derivation tool to see what CK
            ' might be using). Correlate with [FACETINT-LAYER] by layer index within the npc.
            Dim buildIdx = layerInputs.Count
            Dim npcFidLocal = npcData.FormID
            Dim tlIdxLocal = tl.Index
            Dim tlDiscLocal = tl.Discriminator
            Dim tlTplIdxLocal = tl.TemplateColorIndex
            Dim tlValueLocal = tl.Value
            Dim tlColorLocal = tl.Color
            Dim tendBytesLocal = If(tl.RawTendBytes Is Nothing, 0, tl.RawTendBytes.Length)
            Dim optSlotLocal = opt.Slot
            Dim optNameLocal = opt.Name
            Dim optFlagsHexLocal = $"0x{opt.Flags:X4}"
            Dim optFlagsNameLocal = FormatTintFlagsName(opt.Flags)
            Dim optEntryTypeLocal = opt.EntryType.ToString()
            Dim optBlendOpLocal = opt.BlendOperation
            Dim optHasDefaultLocal = opt.HasDefaultValue
            Dim optDefaultValLocal = opt.DefaultValue
            Dim optTplColorsCountLocal = If(opt.TemplateColors Is Nothing, 0, opt.TemplateColors.Count)
            Dim optTexturesCountLocal = If(opt.Textures Is Nothing, 0, opt.Textures.Count)
            Dim lyrR = layerInput.R
            Dim lyrG = layerInput.G
            Dim lyrBlue = layerInput.B
            Dim lyrBlendOp = layerInput.BlendOp
            Dim lyrOpacity = layerInput.Opacity
            Dim lyrKind = layerInput.Kind.ToString()
            Dim lyrTakesSkin = layerInput.TakesSkinTone
            Dim lyrIsSkin = layerInput.IsSkinTone
            Dim lyrHairPal = layerInput.UseHairPalette
            Dim lyrForceUni = layerInput.ForceUniformColor
            Dim lyrRow = layerInput.HairPaletteRow
            ' Resolved palette context (only meaningful for disc=1): which TTEC entry matched?
            Dim palMatched As String = "-"
            Dim palAlpha As String = "-"
            If tl.Discriminator = 1 AndAlso opt IsNot Nothing _
               AndAlso opt.TemplateColors IsNot Nothing AndAlso opt.TemplateColors.Count > 0 _
               AndAlso tl.TemplateColorIndex >= 0 Then
                Dim needle As UShort = CUShort(tl.TemplateColorIndex)
                Dim hit = opt.TemplateColors.FirstOrDefault(Function(t) t.TemplateIndex = needle)
                If hit IsNot Nothing Then
                    palMatched = $"pos={opt.TemplateColors.IndexOf(hit)} clfm=0x{hit.ColorFormID:X8} blendOp={hit.BlendOperation}"
                    palAlpha = hit.Alpha.ToString("F3")
                End If
            End If
            Dim tlRed = tlColorLocal.R
            Dim tlGreen = tlColorLocal.G
            Dim tlBlue = tlColorLocal.B
            Logger.LogLazy(Function() $"[FACETINT-BUILD] npc=0x{npcFidLocal:X8} buildIdx={buildIdx} tl(idx={tlIdxLocal} disc={tlDiscLocal} tplIdx={tlTplIdxLocal} val={tlValueLocal} color=({tlRed},{tlGreen},{tlBlue}) tendLen={tendBytesLocal}) opt(slot={optSlotLocal}/{slotNm} '{optNameLocal}' flags={optFlagsHexLocal}/{optFlagsNameLocal} entryType={optEntryTypeLocal} blendOp={optBlendOpLocal} hasDefault={optHasDefaultLocal} defaultVal={optDefaultValLocal:F3} tplColors={optTplColorsCountLocal} textures={optTexturesCountLocal}) palette({palMatched} alpha={palAlpha}) li(kind={lyrKind} rgb=({lyrR},{lyrG},{lyrBlue}) blend={lyrBlendOp}/{opName} op={lyrOpacity:F3} takesSkin={lyrTakesSkin} isSkin={lyrIsSkin} hairPal={lyrHairPal} forceUni={lyrForceUni} row={lyrRow:F3}) chans={chans}")

            ' [FACETINT-PALETTE] for Palette-type layers (tplColors > 0): dump every CLFM
            ' colour the palette holds so the python derivation can test "src varies per pixel
            ' via mask-byte palette lookup" hypothesis. One line per palette entry.
            If opt.TemplateColors IsNot Nothing AndAlso opt.TemplateColors.Count > 0 Then
                For tcI As Integer = 0 To opt.TemplateColors.Count - 1
                    Dim tc = opt.TemplateColors(tcI)
                    Dim tcR As Integer = -1, tcG As Integer = -1, tcB As Integer = -1
                    Dim tcHasColor As Boolean = False
                    Dim tcHasRemap As Boolean = False
                    Dim tcRemapIdx As Single = 0.0F
                    If tc.ColorFormID <> 0UI AndAlso pluginManager IsNot Nothing Then
                        Dim cr = pluginManager.GetRecord(tc.ColorFormID)
                        If cr IsNot Nothing AndAlso cr.Header.Signature = "CLFM" Then
                            Dim cc = RecordParsers.ParseCLFM(cr, pluginManager)
                            If cc IsNot Nothing Then
                                tcHasColor = cc.HasColor
                                If cc.HasColor Then
                                    tcR = cc.Color.R : tcG = cc.Color.G : tcB = cc.Color.B
                                End If
                                tcHasRemap = cc.HasRemappingIndex
                                tcRemapIdx = cc.RemappingIndex
                            End If
                        End If
                    End If
                    Dim pIdxLocal = tcI
                    Dim pRedLocal = tcR
                    Dim pGreenLocal = tcG
                    Dim pBlueLocal = tcB
                    Dim pHasColorLocal = tcHasColor
                    Dim pHasRemapLocal = tcHasRemap
                    Dim pRemapLocal = tcRemapIdx
                    Dim pClfmLocal = tc.ColorFormID
                    Dim pTplIdxLocal = tc.TemplateIndex
                    Dim pBlendLocal = tc.BlendOperation
                    Dim pAlphaLocal = tc.Alpha
                    Dim npcFidLocal2 = npcFidLocal
                    Dim buildIdxLocal = buildIdx
                    Dim optIdxLocal = tl.Index
                    Logger.LogLazy(Function() $"[FACETINT-PALETTE] npc=0x{npcFidLocal2:X8} buildIdx={buildIdxLocal} optIdx={optIdxLocal} pIdx={pIdxLocal} tplIdx={pTplIdxLocal} clfm=0x{pClfmLocal:X8} hasColor={pHasColorLocal} rgb=({pRedLocal},{pGreenLocal},{pBlueLocal}) hasRemap={pHasRemapLocal} remap={pRemapLocal:F4} blendOp={pBlendLocal} alpha={pAlphaLocal:F3}")
                Next
            End If

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
    ''' <summary>Delegate to FO4_Base_Library.FaceTintPaletteResolver — single source of truth.
    ''' Mantenido como wrapper local para compat con call sites de NPC_Manager.</summary>
    Public Function ResolveFallbackBlendOp(opt As RACE_TintTemplateOption, npcOpacity As Single) As UInteger
        Return FaceTintPaletteResolver.ResolveFallbackBlendOp(opt, npcOpacity)
    End Function

    ''' <summary>Delegate to FO4_Base_Library.FaceTintPaletteResolver — single source of truth.
    ''' Mantenido como wrapper local para compat con call sites de NPC_Manager.</summary>
    Public Function ResolvePaletteLayerEffective(tl As NPC_FaceTintLayerData, opt As RACE_TintTemplateOption, pm As PluginManager) As (Color As Color, BlendOp As UInteger, Matched As Boolean, OpacityScale As Single)
        Return FaceTintPaletteResolver.ResolvePaletteLayerEffective(tl, opt, pm)
    End Function

    ''' <summary>Resolve the TemplateColorIndex (TEND ColorID) of an NPC face-tint Palette layer
    ''' purely from its colour: the TemplateIndex of the preset matched by FindTemplateColorByColor
    ''' (exact CLFM RGB, tie-broken to the Alpha closest to <paramref name="npcOpacity"/> = the
    ''' layer's Value/100), or -1 when no preset's colour matches (custom RGB → no CLFM link).
    ''' Delegates to FaceTintPaletteResolver.FindTemplateColorByColor so the index resolver and the
    ''' render/bake resolver (ResolvePaletteLayerEffective) pick the SAME preset. Called by the
    ''' editor (EditFace_Form custom-RGB and palette-pick) and Save
    ''' (MainForm.ResolveTemplateColorIdToAbsolute).</summary>
    Public Function ResolveTemplateColorIndex(layerColor As Color, npcOpacity As Single, opt As RACE_TintTemplateOption, pm As PluginManager) As Integer
        Dim m = FaceTintPaletteResolver.FindTemplateColorByColor(layerColor, npcOpacity, opt, pm)
        Return If(m Is Nothing, -1, CInt(m.TemplateIndex))
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

    ''' <summary>SkipEyebrowsTone.ini en el appdir (case-insensitive via File.Exists): si existe,
    ''' sintetiza una LUT de degradé Dark->Light (campos "Light=R,G,B" / "Dark=R,G,B", ambos default
    ''' negro -> cejas negras) en BGRA8 sin compresión ni mips, devuelta como bytes DDS para el path
    ''' UseHairPalette del override de cejas. Cachea por color en tintBytesCache.</summary>
    Private Function BuildSyntheticEyebrowLut(tintBytesCache As Dictionary(Of String, Byte())) As (Enabled As Boolean, Bytes As Byte(), Key As String)
        Dim iniPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SkipEyebrowsTone.ini")
        If Not System.IO.File.Exists(iniPath) Then Return (False, Nothing, Nothing)

        Dim dark() As Integer = {0, 0, 0}
        Dim light() As Integer = {0, 0, 0}
        Try
            For Each rawLine In System.IO.File.ReadAllLines(iniPath)
                Dim line = rawLine.Trim()
                If line.Length = 0 OrElse line.StartsWith(";") OrElse line.StartsWith("#") OrElse line.StartsWith("[") Then Continue For
                Dim eq = line.IndexOf("="c)
                If eq <= 0 Then Continue For
                Dim key = line.Substring(0, eq).Trim().ToLowerInvariant()
                If key <> "light" AndAlso key <> "dark" Then Continue For
                Dim parts = line.Substring(eq + 1).Trim().Split(","c)
                If parts.Length < 3 Then Continue For
                Dim rgb(2) As Integer
                Dim ok = True
                For i = 0 To 2
                    Dim n As Integer
                    If Not Integer.TryParse(parts(i).Trim(), n) Then ok = False : Exit For
                    rgb(i) = Math.Max(0, Math.Min(255, n))
                Next
                If Not ok Then Continue For
                If key = "light" Then light = rgb Else dark = rgb
            Next
        Catch
            ' INI presente pero ilegible: degradé negro->negro (default)
        End Try

        Dim cacheKey = $"::synthetic-eyebrow::dark={dark(0)},{dark(1)},{dark(2)}::light={light(0)},{light(1)},{light(2)}"
        Dim cached As Byte() = Nothing
        If tintBytesCache IsNot Nothing AndAlso tintBytesCache.TryGetValue(cacheKey, cached) AndAlso cached IsNot Nothing Then
            Return (True, cached, cacheKey)
        End If

        ' Degradé Dark (col 0 = grayscale 0) -> Light (col W-1 = grayscale 1), BGRA8, H filas replicadas.
        Const W As Integer = 256
        Const H As Integer = 4
        Dim px(W * H * 4 - 1) As Byte
        For x = 0 To W - 1
            Dim t = CSng(x) / CSng(W - 1)
            Dim rr = CByte(Math.Max(0, Math.Min(255, CInt(Math.Round(dark(0) + (light(0) - dark(0)) * t)))))
            Dim gg = CByte(Math.Max(0, Math.Min(255, CInt(Math.Round(dark(1) + (light(1) - dark(1)) * t)))))
            Dim bb = CByte(Math.Max(0, Math.Min(255, CInt(Math.Round(dark(2) + (light(2) - dark(2)) * t)))))
            For y = 0 To H - 1
                Dim o = (y * W + x) * 4
                px(o) = bb : px(o + 1) = gg : px(o + 2) = rr : px(o + 3) = CByte(255)
            Next
        Next

        Dim dds As Byte() = Nothing
        Try
            dds = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(W, H, px, DirectXTextureConversionHelper.DxgiFormatB8G8R8A8Unorm, generateMipMaps:=False)
        Catch ex As Exception
            Logger.LogLazy(Function() $"[BROW-TINT] synthetic LUT build failed: {ex.GetType().Name}: {ex.Message}")
            Return (True, Nothing, cacheKey)
        End Try
        If tintBytesCache IsNot Nothing AndAlso dds IsNot Nothing Then tintBytesCache(cacheKey) = dds
        Return (True, dds, cacheKey)
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
