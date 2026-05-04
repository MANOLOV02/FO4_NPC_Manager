Imports System.Drawing
Imports System.Globalization
Imports FO4_Base_Library

''' <summary>Editor for an NPC's face: HeadParts, HairColor, Tints, ACBS IsCharGenFacePreset
''' flag, vanilla NPC.WNAM Skin override, vertex morphs (MSDK/MSDV chargen), bone region morphs
''' (FMRI/FMRS), and Facial Morph Intensity (FMIN).
'''
''' Round-trip semantics — for each editable channel, the form mutates the LooksMenu preset
''' overlay (_appliedPresets[npc]) on the host MainForm. The renderer reads the overlay-applied
''' NPC_Data via ApplyPresetOverlayToNpcData (MainForm.vb:8429) and the resolvers downstream
''' (NpcMorphResolver, BuildFaceBoneTransforms, FaceTintCompositor, MergeHeadPartsWithRaceDefaults)
''' pick up the effective values. The host's RefreshFaceEditOverlay then issues the right
''' MarkDirty pass — granular for tints/morphs/pose, full reload for HeadParts/Skin (which
''' change the rendered geometry).
'''
''' Cancel rolls back to a deep snapshot of the overlay taken at form construction. OK is a
''' no-op (live edits are already applied).
'''
''' Pipeline reminder per channel:
'''   HeadParts (NPC.PNAM)              → MergeHeadPartsWithRaceDefaults → mesh assembly. Full reload.
'''   HairColor (NPC.QNAM)              → ResolveColorFormColor → tint shader. Textures dirty.
'''   FaceTintLayers (TETI/TEND)        → FaceTintCompositor.ComposeOntoFaceTexture. Textures dirty.
'''   IsCharGenFacePreset (ACBS bit 2)  → no live consumer; overlay only persists to ESP later.
'''   SkinFormID (NPC.WNAM)             → CollectArmoCandidates → body/skin geometry. Full reload.
'''   MorphValues (MSDK/MSDV)           → NpcMorphResolver via RACE.MorphValueDefs/MorphPresets. Morphs dirty.
'''   FaceMorphs (FMRI/FMRS)            → MainForm.BuildFaceBoneTransforms → skeleton DeltaTransform. Pose dirty.
'''   FacialMorphIntensity (FMIN)       → multiplier in BuildFaceBoneTransforms. Pose dirty.
''' </summary>
Public Class EditFace_Form

    ' HDPT type constants — match wbDefinitionsFO4.pas:7373 PNAM enum (also mirrored at MainForm.vb:88-91).
    Private Const HdptTypeMisc As Integer = 0
    Private Const HdptTypeFace As Integer = 1
    Private Const HdptTypeEyes As Integer = 2
    Private Const HdptTypeHair As Integer = 3
    Private Const HdptTypeFacialHair As Integer = 4
    Private Const HdptTypeScar As Integer = 5
    Private Const HdptTypeEyebrows As Integer = 6
    Private Const HdptTypeMeatcaps As Integer = 7
    Private Const HdptTypeTeeth As Integer = 8
    Private Const HdptTypeHeadRear As Integer = 9

    Private Const FlagBitIsExtra As Byte = &H8

    ' ACBS bit for "Is CharGen Face Preset" (xEdit declares it as 0x04 literal at
    ' wbDefinitionsFO4.pas:10633; the codebase reads NPC.AcbsFlags raw at RecordParsers.vb:892).
    Private Const AcbsBitIsCharGenFacePreset As UInteger = &H4UI

    Private ReadOnly _rootNpcFormID As UInteger
    Private ReadOnly _appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)
    Private ReadOnly _pluginManager As PluginManager
    Private ReadOnly _race As RACE_Data
    Private ReadOnly _raceFormID As UInteger
    Private ReadOnly _isFemale As Boolean
    Private ReadOnly _refresh As Action(Of FaceRefreshScope)
    Private ReadOnly _formatNpcRef As Func(Of UInteger, String)

    ' Snapshot for Cancel rollback (the overlay BEFORE we touched it — Nothing if there was
    ' nothing) and for Reset (the post-seed preset, which always carries the original NPC values
    ' even when there was no prior overlay).
    Private ReadOnly _hadPriorOverlay As Boolean
    Private ReadOnly _priorPreset As LooksmenuLoader.LooksmenuPreset
    Private _seedPreset As LooksmenuLoader.LooksmenuPreset
    Private ReadOnly _priorAcbsFlagsRaw As UInteger

    ' UI state.
    Private _suspendEvents As Boolean

    ' Vertex morph UI: one section per RACE MorphGroup (mirrors CK chargen UI). Each section
    ' has a preset ListBox + intensity slider (if the group has presets) and N MPGS sliders
    ' (bidirectional, one per group-attached MorphValue key). Keys not referenced by any group
    ' MPGS go to a synthetic "Other Sliders" section at the end if any exist.
    Private ReadOnly _groupSections As New List(Of MorphGroupSection)
    Private ReadOnly _bidiBars As New Dictionary(Of UInteger, TrackBar)
    Private ReadOnly _bidiLabels As New Dictionary(Of UInteger, Label)
    Private ReadOnly _bidiKeyToGroup As New Dictionary(Of UInteger, MorphGroupSection)
    Private ReadOnly _presetKeyToGroup As New Dictionary(Of UInteger, MorphGroupSection)

    ' Bone region per-row controls. Each region gets 7 sliders + 7 labels (PosX/Y/Z, RotX/Y/Z,
    ' Scale). We index by (regionId, componentIdx 0..6) so OnRegionSliderChanged can route the
    ' value back into preset.FaceBoneRegions[regionId][componentIdx]. Built once in
    ' BuildBoneRegionsUI from the JSON FacialBoneRegions list of the active race+gender.
    Private ReadOnly _regionBars As New Dictionary(Of UInteger, TrackBar())
    Private ReadOnly _regionLabels As New Dictionary(Of UInteger, Label())

    ' Currently selected tint, for routing slider events.
    Private _currentTintIndex As Integer = -1

    ' Cached resolution dictionaries (built once at construction).
    Private ReadOnly _allHeadPartsByFid As New Dictionary(Of UInteger, HDPT_Data)
    Private ReadOnly _allHairColors As New List(Of CLFM_Data)

    ' Hair palette LUT (HairColor_Lgrad_d.dds) decoded once and reused for swatch sampling.
    ' For palette-mode CLFMs (HasRemappingIndex=True), the swatch fills with the row at
    ' RemappingIndex × paletteHeight from this bitmap. Loaded lazily on first request via
    ' EnsureHairPaletteLoaded so a race without a hair LUT (or an unreadable DDS) just falls
    ' back to a grey swatch instead of failing.
    Private _hairPaletteBitmap As Bitmap = Nothing
    Private _hairPaletteResolveAttempted As Boolean

    ' TintTemplate option Index -> GroupName / render-order rank. Built once from RACE for the
    ' active gender. The tint ListView shows Group as a column and rows are ordered by Rank so
    ' the user sees the same composition order the renderer uses (MainForm.vb:3014).
    Private ReadOnly _tintGroupByIndex As New Dictionary(Of UShort, String)
    Private ReadOnly _tintRankByIndex As New Dictionary(Of UShort, Integer)

    ''' <summary>Granularity hint — different channels need different MarkDirty passes. The host
    ''' decides how to translate this into RenderDirtyFlags + InvalidateRender; we just say what
    ''' we changed so it can pick the cheapest path.</summary>
    Public Enum FaceRefreshScope
        ''' <summary>Tints / hair color: re-tint textures (no geom change).</summary>
        TexturesOnly
        ''' <summary>Vertex morphs: rebuild MorphResolver, MarkDirty(Morphs).</summary>
        Morphs
        ''' <summary>Face bone morphs / FMIN: rebuild pose, MarkDirty(Pose).</summary>
        Pose
        ''' <summary>HeadParts / Skin override: full reload (LoadNPCOnDemandAsyncFromExisting).</summary>
        FullReload
        ''' <summary>Editor-only flags (IsCharGenFacePreset): no render side-effect.</summary>
        FlagOnly
    End Enum

    ''' <summary>One UI section per RACE MorphGroup. Mirrors CK chargen layout: preset ListBox
    ''' (one selection per group, "(none)" entry at top) + intensity TrackBar [0..1] for the
    ''' chosen preset, plus one bidirectional [-1..+1] TrackBar per MPGS key (group-attached
    ''' slider). The synthetic "Other" section uses GroupName="" and Presets=Nothing to flag
    ''' it as a slider-only bucket for MorphValue keys not referenced by any MorphGroup.</summary>
    Private Class MorphGroupSection
        Public GroupName As String
        Public Presets As List(Of RACE_MorphPresetDef)   ' Nothing = "Other Sliders" section
        Public BidiKeys As New List(Of UInteger)         ' MPGS keys; resolved to MorphValueDef for MSM0/MSM1
        ' Live UI controls (set during build).
        Public PresetListBox As ListBox
        Public PresetIntensityBar As TrackBar
        Public PresetIntensityLabel As Label
    End Class

    Public Sub New(rootNpcFormID As UInteger,
                   appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                   pluginManager As PluginManager,
                   race As RACE_Data,
                   raceFormID As UInteger,
                   isFemale As Boolean,
                   refresh As Action(Of FaceRefreshScope),
                   formatNpcRef As Func(Of UInteger, String),
                   priorAcbsFlagsRaw As UInteger)
        InitializeComponent()
        _rootNpcFormID = rootNpcFormID
        _appliedPresets = appliedPresets
        _pluginManager = pluginManager
        _race = race
        _raceFormID = raceFormID
        _isFemale = isFemale
        _refresh = refresh
        _formatNpcRef = formatNpcRef
        _priorAcbsFlagsRaw = priorAcbsFlagsRaw

        ' Snapshot any existing overlay so Cancel can restore byte-equivalent.
        Dim existing As LooksmenuLoader.LooksmenuPreset = Nothing
        _hadPriorOverlay = _appliedPresets.TryGetValue(rootNpcFormID, existing)
        _priorPreset = If(_hadPriorOverlay, ClonePreset(existing), Nothing)

        ' Ensure an overlay exists for live editing. Removed in Cancel if it didn't exist.
        Dim p As LooksmenuLoader.LooksmenuPreset = Nothing
        If Not _appliedPresets.TryGetValue(rootNpcFormID, p) OrElse p Is Nothing Then
            p = New LooksmenuLoader.LooksmenuPreset()
            p.Gender = If(_isFemale, CByte(1), CByte(0))
            _appliedPresets(rootNpcFormID) = p
        End If

        BuildHeadPartCache()
        BuildHairColorCache()
        BuildMorphGroupSections()
        BuildTintGroupRanks()
        BuildBoneRegionsUI()

        WireHandlers()
        SeedFromOverlayOrRaw()
        ' Snapshot AFTER seeding — this is the "original NPC state" that Reset reverts to. Differs
        ' from _priorPreset which only carries the prior overlay (or Nothing). Reset wants the full
        ' merged-from-raw state so the user gets the actual NPC defaults back, not "all sliders
        ' at 0.5" or empty lists.
        _seedPreset = ClonePreset(Preset)
    End Sub

    ' =====================================================================
    ' Section 1 — caches and lookups (built once per form lifetime)
    ' =====================================================================

    ''' <summary>Build the (FormID → HDPT_Data) lookup table for all loaded HDPTs. Used by:
    '''   - the HeadParts list to display each entry's name, type and plugin.
    '''   - the HeadPart picker's RACE/gender filter (delegated to HeadPartPicker_Form).</summary>
    Private Sub BuildHeadPartCache()
        Dim hdptRecords = _pluginManager.GetRecordsOfType("HDPT")
        If hdptRecords Is Nothing Then Return
        For Each rec In hdptRecords
            Dim hdpt = RecordParsers.ParseHDPT(rec, _pluginManager)
            If hdpt Is Nothing Then Continue For
            _allHeadPartsByFid(hdpt.FormID) = hdpt
        Next
    End Sub

    ''' <summary>Hair-color combo lists ONLY the CLFMs declared in RACE.AHCM/AHCF for this NPC's
    ''' gender — that's the same per-race+gender list the chargen UI offers. Anything else (skin
    ''' tones, eye colors, body-paint CLFMs) is not a valid hair tint and feeding it through QNAM
    ''' produces visual garbage. wbDefinitionsFO4.pas:11646 (AHCM Male) / 11664 (AHCF Female).
    ''' Sort by FullName then EditorID for stable presentation.</summary>
    Private Sub BuildHairColorCache()
        Dim allowed = If(_isFemale, _race?.FemaleHairColorFormIDs, _race?.MaleHairColorFormIDs)
        If allowed Is Nothing OrElse allowed.Count = 0 Then
            ' Race didn't declare any hair colors for this gender. Leave the combo empty (the
            ' "(none / preserve)" entry is added by PopulateHairColorCombo regardless).
            Return
        End If
        Dim allowedSet As New HashSet(Of UInteger)(allowed)
        For Each fid In allowedSet
            Dim rec = _pluginManager.GetRecord(fid)
            If rec Is Nothing OrElse rec.Header.Signature <> "CLFM" Then Continue For
            Dim clfm = RecordParsers.ParseCLFM(rec, _pluginManager)
            If clfm Is Nothing Then Continue For
            _allHairColors.Add(clfm)
        Next
        _allHairColors.Sort(Function(a, b)
                                Dim na = If(a.FullName, "")
                                Dim nb = If(b.FullName, "")
                                Dim cmp = String.Compare(na, nb, StringComparison.OrdinalIgnoreCase)
                                If cmp <> 0 Then Return cmp
                                Return String.Compare(If(a.EditorID, ""), If(b.EditorID, ""), StringComparison.OrdinalIgnoreCase)
                            End Function)
    End Sub

    ''' <summary>Enumerate the universe of vertex-morph slider rows we'll surface in the editor.
    ''' Each entry corresponds to one MSDK index (= MSID/MPPI key in RACE) — we want the user to
    ''' be able to dial in a value even if the NPC's record doesn't currently carry that key
    ''' (which is how a vanilla NPC starts: record has only the few keys CK authored).
    '''
    ''' Two sources, both per-RACE/gender:
    '''   - MorphValueDefs (MSID → MSM0/MSM1 names): bidirectional sliders. NPC value sign picks
    '''     MSM0 (negative) vs MSM1 (positive); abs value is the weight.
    '''   - MorphPresets (MPPI → MPPM): one-shot presets. NPC value is the weight directly.
    '''
    ''' Both lists can have overlapping indices in pathological RACE records; we keep the first
    ''' occurrence and tag whether the slider should be bidirectional or unidirectional.</summary>
    ''' <summary>Build the per-MorphGroup sections from RACE for the active gender. Mirrors the
    ''' CK chargen layout: one section per group with presets+sliders, plus a synthetic "Other
    ''' Sliders" tail for any MorphValueDef key not referenced by any group's MPGS. The number
    ''' of sections, presets and sliders is fully race-driven — HumanRace = 9 groups, other
    ''' races may differ.</summary>
    Private Sub BuildMorphGroupSections()
        _groupSections.Clear()
        _bidiKeyToGroup.Clear()
        _presetKeyToGroup.Clear()
        If _race Is Nothing Then Return

        Dim groups = If(_isFemale, _race.FemaleMorphGroups, _race.MaleMorphGroups)
        Dim consumedBidi As New HashSet(Of UInteger)

        If groups IsNot Nothing Then
            For Each g In groups
                Dim hasPresets = g.Presets IsNot Nothing AndAlso g.Presets.Count > 0
                Dim hasSliders = g.SliderIndices IsNot Nothing AndAlso g.SliderIndices.Count > 0
                If Not hasPresets AndAlso Not hasSliders Then Continue For

                Dim section As New MorphGroupSection With {
                    .GroupName = If(g.Name, ""),
                    .Presets = If(hasPresets, g.Presets, New List(Of RACE_MorphPresetDef))}
                If hasSliders Then
                    For Each k In g.SliderIndices
                        section.BidiKeys.Add(k)
                        consumedBidi.Add(k)
                        _bidiKeyToGroup(k) = section
                    Next
                End If
                If hasPresets Then
                    For Each p In g.Presets
                        _presetKeyToGroup(p.Index) = section
                    Next
                End If
                _groupSections.Add(section)
            Next
        End If

        ' "Other Sliders" — fallback section. RACE.MorphValues is a single race-wide table
        ' (wbDefinitionsFO4.pas:11702 places it OUTSIDE the gendered head blocks). A given MSID
        ' belongs to whichever gender's MorphGroup MPGS references it; entries owned by the
        ' OPPOSITE gender appear here as "orphan" relative to the active gender's MPGS but they
        ' are not really orphan — they're just not for this gender. So we exclude keys consumed
        ' by EITHER gender's MPGS. True orphans (not in any MPGS at all) are vanishingly rare in
        ' vanilla but we surface them in case a custom race authored some.
        If _race.MorphValues IsNot Nothing Then
            Dim consumedAnyGender As New HashSet(Of UInteger)
            For Each k In consumedBidi : consumedAnyGender.Add(k) : Next
            Dim oppositeGroups = If(_isFemale, _race.MaleMorphGroups, _race.FemaleMorphGroups)
            If oppositeGroups IsNot Nothing Then
                For Each g In oppositeGroups
                    If g.SliderIndices Is Nothing Then Continue For
                    For Each k In g.SliderIndices : consumedAnyGender.Add(k) : Next
                Next
            End If
            Dim orphans As New List(Of UInteger)
            For Each mv In _race.MorphValues
                If consumedAnyGender.Contains(mv.Index) Then Continue For
                orphans.Add(mv.Index)
                Dim mvLocal = mv
                NpcPreviewLog.LogLazy(Function() $"[EDITFACE-ORPHAN] MSID=0x{mvLocal.Index:X8} MSM0='{mvLocal.MinName}' MSM1='{mvLocal.MaxName}' (not in any MPGS, race={_race.EditorID})")
            Next
            If orphans.Count > 0 Then
                ' Keys that no MPGS references in any gender. Should be empty in vanilla — if it
                ' fires, the filter logic missed something or the RACE record is genuinely odd.
                ' Title screams red so the user notices and reports.
                Dim other As New MorphGroupSection With {.GroupName = "These shouldn't be here!!", .Presets = Nothing}
                other.BidiKeys.AddRange(orphans)
                For Each k In orphans
                    _bidiKeyToGroup(k) = other
                Next
                _groupSections.Add(other)
            End If
        End If
    End Sub

    ''' <summary>Walk the active gender's TintTemplateGroups and build:
    '''   * <see cref="_tintGroupByIndex"/> : TETI option Index -> the group it belongs to
    '''     (Cheek, Lip, Eyeliner, etc.). Used by the tint ListView Group column so the user
    '''     sees what region each layer paints.
    '''   * <see cref="_tintRankByIndex"/>  : TETI option Index -> render-order rank. Same dict
    '''     MainForm.BuildPresetFromState (line 8969) and the compositor (line 3021) build to
    '''     reorder NPC.FaceTintLayers by RACE-Group order. Mirroring it here makes the editor
    '''     row order match the actual composition order the renderer applies.
    ''' Both are stable for the lifetime of the form (RACE / gender don't change).</summary>
    Private Sub BuildTintGroupRanks()
        If _race Is Nothing Then Return
        Dim groups = If(_isFemale, _race.FemaleTintTemplateGroups, _race.MaleTintTemplateGroups)
        If groups Is Nothing Then Return
        Dim rank As Integer = 0
        For Each grp In groups
            Dim groupName = If(grp.GroupName, "")
            If grp.Options Is Nothing Then Continue For
            For Each opt In grp.Options
                If Not _tintGroupByIndex.ContainsKey(opt.Index) Then
                    _tintGroupByIndex(opt.Index) = groupName
                End If
                If Not _tintRankByIndex.ContainsKey(opt.Index) Then
                    _tintRankByIndex(opt.Index) = rank
                    rank += 1
                End If
            Next
        Next
    End Sub

    ' =====================================================================
    ' Section 2 — overlay accessors and snapshot helpers
    ' =====================================================================

    Private ReadOnly Property Preset As LooksmenuLoader.LooksmenuPreset
        Get
            Dim p As LooksmenuLoader.LooksmenuPreset = Nothing
            _appliedPresets.TryGetValue(_rootNpcFormID, p)
            Return p
        End Get
    End Property

    Private Shared Function ClonePreset(p As LooksmenuLoader.LooksmenuPreset) As LooksmenuLoader.LooksmenuPreset
        If p Is Nothing Then Return Nothing
        Dim c As New LooksmenuLoader.LooksmenuPreset()
        c.SourcePath = p.SourcePath
        c.Gender = p.Gender
        c.HeadPartFormIDs.AddRange(p.HeadPartFormIDs)
        c.UnresolvedHeadParts.AddRange(p.UnresolvedHeadParts)
        c.HairColorFormID = p.HairColorFormID
        c.WeightThin = p.WeightThin
        c.WeightMuscular = p.WeightMuscular
        c.WeightFat = p.WeightFat
        For Each kv In p.ChargenFaceMorphs : c.ChargenFaceMorphs(kv.Key) = kv.Value : Next
        c.BodyMorphValues.AddRange(p.BodyMorphValues)
        For Each kv In p.FaceBoneRegions
            c.FaceBoneRegions(kv.Key) = If(kv.Value Is Nothing, Nothing, CType(kv.Value.Clone(), Single()))
        Next
        c.FacialMorphIntensity = p.FacialMorphIntensity
        For Each tl In p.FaceTintLayers
            c.FaceTintLayers.Add(CloneFaceTint(tl))
        Next
        For Each kv In p.BodyMorphSliders : c.BodyMorphSliders(kv.Key) = kv.Value : Next
        c.UnsupportedCounts.Overlays = p.UnsupportedCounts.Overlays
        c.UnsupportedCounts.BodyMorphSliders = p.UnsupportedCounts.BodyMorphSliders
        c.UnsupportedCounts.HasSkinOverride = p.UnsupportedCounts.HasSkinOverride
        c.IsCharGenFacePreset = p.IsCharGenFacePreset
        c.SkinFormIDOverride = p.SkinFormIDOverride
        Return c
    End Function

    Private Shared Function CloneFaceTint(tl As NPC_FaceTintLayerData) As NPC_FaceTintLayerData
        Return New NPC_FaceTintLayerData With {
            .Discriminator = tl.Discriminator,
            .Index = tl.Index,
            .Value = tl.Value,
            .Color = tl.Color,
            .TemplateColorIndex = tl.TemplateColorIndex,
            .RawTetiBytes = If(tl.RawTetiBytes Is Nothing, Nothing, CType(tl.RawTetiBytes.Clone(), Byte())),
            .RawTendBytes = If(tl.RawTendBytes Is Nothing, Nothing, CType(tl.RawTendBytes.Clone(), Byte()))
        }
    End Function

    ' =====================================================================
    ' Section 3 — initial seed: open the form with the NPC's current effective values
    ' =====================================================================

    ''' <summary>Populate every UI control from the current overlay-merged-with-raw state. This is
    ''' the round-trip "load" half: whatever the renderer is showing right now, the form opens
    ''' with the same values so dragging immediately feels like an edit, not a reset to zero.
    '''
    ''' For NPCs without an overlay yet, we seed the overlay's editable channels with the raw
    ''' NPC values (HeadParts, HairColor, Tints, Morphs, FaceMorphs, FMIN, AcbsFlag, SkinFormID)
    ''' so subsequent edits ride on top of the displayed baseline. Nothing visible changes — the
    ''' overlay just mirrors the raw record until the user modifies a slider.</summary>
    Private Sub SeedFromOverlayOrRaw()
        _suspendEvents = True
        Try
            Dim p = Preset
            ' Pull the raw NPC record for fields not already in the overlay.
            Dim rawNpc = TryGetRawNpc()

            ' --- HeadParts ---
            If p.HeadPartFormIDs.Count = 0 AndAlso rawNpc IsNot Nothing AndAlso rawNpc.HeadPartFormIDs.Count > 0 Then
                ' Seed mirroring the render's MergeHeadPartsWithRaceDefaults rule
                ' (MainForm.vb:6549-6580): per non-Misc PartType keep exactly one (NPC wins
                ' over RACE-default), Misc accumulates without dedup-by-type. Without this we
                ' end up with two Eyes / two Hairs on screen at form-open whenever the NPC's
                ' PNAM declares a different one than the RACE default.
                '
                ' We do NOT filter by IsExtra here — vanilla NPCs put HNAM-extra HDPTs (lashes,
                ' AO, wet, hairlines, mouth shadow) directly into NPC.PNAM as freestanding
                ' Misc, and those need to show up in the editor list so the user can see /
                ' remove them.
                Dim mergedByType As New Dictionary(Of Integer, UInteger)
                Dim freestandingMisc As New List(Of UInteger)
                Dim seenMisc As New HashSet(Of UInteger)

                Dim raceDefaults = If(_isFemale, _race?.FemaleHeadPartFormIDs, _race?.MaleHeadPartFormIDs)
                Dim seedFromList = Sub(list As IEnumerable(Of UInteger))
                                       If list Is Nothing Then Return
                                       For Each fid In list
                                           If fid = 0UI Then Continue For
                                           Dim hd As HDPT_Data = Nothing
                                           If Not _allHeadPartsByFid.TryGetValue(fid, hd) Then Continue For
                                           If hd.PartType = 0 Then
                                               If seenMisc.Add(fid) Then freestandingMisc.Add(fid)
                                           ElseIf hd.PartType >= 1 AndAlso hd.PartType <= 9 Then
                                               ' NPC.PNAM passes after RACE defaults, so its
                                               ' assignment wins per type — last-write-wins.
                                               mergedByType(hd.PartType) = fid
                                           End If
                                       Next
                                   End Sub
                seedFromList(raceDefaults)
                seedFromList(rawNpc.HeadPartFormIDs)

                For Each t In mergedByType.Keys.OrderBy(Function(k) k)
                    p.HeadPartFormIDs.Add(mergedByType(t))
                Next
                p.HeadPartFormIDs.AddRange(freestandingMisc)
            End If
            RefreshHeadPartsList()

            ' --- HairColor ---
            If p.HairColorFormID = 0UI AndAlso rawNpc IsNot Nothing Then
                p.HairColorFormID = rawNpc.HairColorFormID
            End If
            PopulateHairColorCombo()
            UpdateHairColorSwatch()

            ' --- IsCharGenFacePreset ---
            If Not p.IsCharGenFacePreset.HasValue Then
                p.IsCharGenFacePreset = (_priorAcbsFlagsRaw And AcbsBitIsCharGenFacePreset) <> 0UI
            End If
            CheckBoxIsCharGenFacePreset.Checked = p.IsCharGenFacePreset.GetValueOrDefault(False)

            ' --- Tints ---
            If p.FaceTintLayers.Count = 0 AndAlso rawNpc IsNot Nothing AndAlso rawNpc.FaceTintLayers.Count > 0 Then
                For Each tl In rawNpc.FaceTintLayers
                    p.FaceTintLayers.Add(CloneFaceTint(tl))
                Next
            End If
            RefreshTintsList()

            ' --- Vertex morphs (MSDK/MSDV) ---
            If p.ChargenFaceMorphs.Count = 0 AndAlso rawNpc IsNot Nothing AndAlso rawNpc.MorphValues.Count > 0 Then
                For Each kv In rawNpc.MorphValues
                    p.ChargenFaceMorphs(kv.Key) = kv.Value
                Next
            End If
            BuildMorphGroupRows()
            LoadMorphGroupValues()

            ' --- Face bone regions (FMRI/FMRS) ---
            If p.FaceBoneRegions.Count = 0 AndAlso rawNpc IsNot Nothing AndAlso rawNpc.FaceMorphs.Count > 0 Then
                For Each fm In rawNpc.FaceMorphs
                    p.FaceBoneRegions(fm.Index) = fm.Values.ToArray()
                Next
            End If
            LoadBoneRegionValues()

            ' --- FMIN ---
            ' FacialMorphIntensity always carries 1.0F when the JSON omits it (LooksmenuLoader sets
            ' the default at parse time). For a fresh editor open, prefer the raw NPC value if the
            ' overlay still carries the parser default — Math.Abs(p.FMIN - 1.0F) < epsilon. This
            ' avoids the form opening at "1.00" for an NPC whose record actually says 1.4 or 0.7.
            If rawNpc IsNot Nothing AndAlso Math.Abs(p.FacialMorphIntensity - 1.0F) < 0.0001F Then
                p.FacialMorphIntensity = If(rawNpc.FacialMorphIntensity > 0.0F, rawNpc.FacialMorphIntensity, 1.0F)
            End If
            TrackBarFmin.Value = ClampInt(CInt(Math.Round(p.FacialMorphIntensity * 100.0F)), TrackBarFmin.Minimum, TrackBarFmin.Maximum)
            UpdateLabel(LabelFminValue, p.FacialMorphIntensity)
        Finally
            _suspendEvents = False
        End Try
    End Sub

    Private Function TryGetRawNpc() As NPC_Data
        If _pluginManager Is Nothing Then Return Nothing
        Dim rec = _pluginManager.GetRecord(_rootNpcFormID)
        If rec Is Nothing OrElse rec.Header.Signature <> "NPC_" Then Return Nothing
        Dim pluginName = If(rec.SourcePluginName <> "", rec.SourcePluginName, "Unknown")
        Return RecordParsers.ParseNPC(rec, pluginName, _pluginManager)
    End Function

    ' =====================================================================
    ' Section 4 — event wiring
    ' =====================================================================

    Private Sub WireHandlers()
        AddHandler ButtonOk.Click, AddressOf OnOk
        AddHandler ButtonCancel.Click, AddressOf OnCancel
        AddHandler ButtonResetSection.Click, AddressOf OnResetSection

        AddHandler ButtonAddFace.Click, Sub(s, e) OnAddHeadPart(HdptTypeFace, "Face")
        AddHandler ButtonAddHair.Click, Sub(s, e) OnAddHeadPart(HdptTypeHair, "Hair")
        AddHandler ButtonAddEyes.Click, Sub(s, e) OnAddHeadPart(HdptTypeEyes, "Eyes")
        AddHandler ButtonAddFacialHair.Click, Sub(s, e) OnAddHeadPart(HdptTypeFacialHair, "Facial Hair")
        AddHandler ButtonAddEyebrows.Click, Sub(s, e) OnAddHeadPart(HdptTypeEyebrows, "Eyebrows")
        AddHandler ButtonAddScar.Click, Sub(s, e) OnAddHeadPart(HdptTypeScar, "Scar")
        AddHandler ButtonAddTeeth.Click, Sub(s, e) OnAddHeadPart(HdptTypeTeeth, "Teeth")
        AddHandler ButtonAddHeadRear.Click, Sub(s, e) OnAddHeadPart(HdptTypeHeadRear, "Head Rear")
        AddHandler ButtonAddMeatcaps.Click, Sub(s, e) OnAddHeadPart(HdptTypeMeatcaps, "Meatcaps")
        AddHandler ButtonAddMisc.Click, Sub(s, e) OnAddHeadPart(HdptTypeMisc, "Misc")
        AddHandler ButtonRemoveHeadPart.Click, AddressOf OnRemoveHeadPart

        AddHandler ComboBoxHairColor.SelectedIndexChanged, AddressOf OnHairColorChanged
        AddHandler ButtonClearHairColor.Click, AddressOf OnClearHairColor
        AddHandler PanelHairColorSwatch.Paint, AddressOf OnPaintHairColorSwatch

        AddHandler CheckBoxIsCharGenFacePreset.CheckedChanged, AddressOf OnIsCharGenFacePresetChanged


        AddHandler ButtonAddTint.Click, AddressOf OnAddTint
        AddHandler ButtonRemoveTint.Click, AddressOf OnRemoveTint
        AddHandler ListViewTints.SelectedIndexChanged, AddressOf OnTintSelectionChanged
        AddHandler ComboBoxTintPalette.SelectedIndexChanged, AddressOf OnTintPaletteChanged
        AddHandler ButtonTintCustomRGB.Click, AddressOf OnTintCustomRGB
        AddHandler TrackBarTintPercent.ValueChanged, AddressOf OnTintPercentChanged

        ' Bone region slider handlers are wired per-control inside BuildBoneRegionsUI.

        AddHandler TrackBarFmin.ValueChanged, AddressOf OnFminChanged
    End Sub

    ' =====================================================================
    ' Section 5 — Head Parts (NPC.PNAM, full reload on change)
    '
    ' Round-trip: ListBox displays preset.HeadPartFormIDs. Add → HeadPartPicker_Form (filtered
    ' by partType + RACE.RNAM + gender). Remove → drop entry. Each mutation triggers
    ' refresh(FullReload), which the host translates to LoadNPCOnDemandAsyncFromExisting →
    ' ResolveNPCBaseState consumes preset.HeadPartFormIDs at MainForm:3841-3842.
    ' =====================================================================

    Private Sub RefreshHeadPartsList()
        ListViewHeadParts.BeginUpdate()
        Try
            ListViewHeadParts.Items.Clear()
            Dim p = Preset
            For Each fid In p.HeadPartFormIDs
                ListViewHeadParts.Items.Add(BuildHeadPartRow(fid))
            Next
        Finally
            ListViewHeadParts.EndUpdate()
        End Try
    End Sub

    ''' <summary>Build a 5-column ListViewItem for a head-part FormID. Columns mirror the picker
    ''' layout (Type / Editor ID / Name / Plugin / FormID) so the eye doesn't have to translate
    ''' between the two views. Unresolved FormIDs (e.g. plugin missing) still get a row showing
    ''' the FormID so the user can see what's broken instead of getting a silent gap.</summary>
    Private Function BuildHeadPartRow(fid As UInteger) As ListViewItem
        Dim hd As HDPT_Data = Nothing
        Dim hex = fid.ToString("X8")
        If Not _allHeadPartsByFid.TryGetValue(fid, hd) Then
            Dim missing As New ListViewItem("(unresolved)")
            missing.SubItems.Add("")
            missing.SubItems.Add("")
            missing.SubItems.Add("")
            missing.SubItems.Add(hex)
            missing.Tag = fid
            Return missing
        End If
        Dim plugin As String = ""
        Dim rec = _pluginManager.GetRecord(fid)
        If rec IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(rec.SourcePluginName) Then plugin = rec.SourcePluginName
        Dim row As New ListViewItem(HdptTypeName(hd.PartType))
        row.SubItems.Add(If(hd.EditorID, ""))
        row.SubItems.Add(If(hd.FullName, ""))
        row.SubItems.Add(plugin)
        row.SubItems.Add(hex)
        row.Tag = fid
        Return row
    End Function

    Private Shared Function HdptTypeName(t As Integer) As String
        Select Case t
            Case 0 : Return "Misc"
            Case 1 : Return "Face"
            Case 2 : Return "Eyes"
            Case 3 : Return "Hair"
            Case 4 : Return "Facial Hair"
            Case 5 : Return "Scar"
            Case 6 : Return "Eyebrows"
            Case 7 : Return "Meatcaps"
            Case 8 : Return "Teeth"
            Case 9 : Return "Head Rear"
            Case Else : Return $"Type{t}"
        End Select
    End Function

    Private Sub OnAddHeadPart(partType As Integer, partTypeLabel As String)
        Dim raceEditorID = If(_race?.EditorID, "?")
        ' Pass the race's gender-defaults so the picker accepts them even when the HDPT's own
        ' RNAM is inconsistent (vanilla mostly clean, mods sometimes diverge).
        Dim raceDefaults = If(_isFemale, _race?.FemaleHeadPartFormIDs, _race?.MaleHeadPartFormIDs)
        Using dlg As New HeadPartPicker_Form(_pluginManager, _raceFormID, raceEditorID, _isFemale, partType, partTypeLabel, raceDefaults)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim newFid = dlg.SelectedFormID
            If newFid = 0UI Then Return
            Dim p = Preset

            If partType = HdptTypeMisc Then
                ' Misc = freestanding addons (lashes, AO, wet, mouth shadow, hairlines, etc.).
                ' MergeHeadPartsWithRaceDefaults (MainForm.vb:6573-6575, 6587) accumulates ALL
                ' Misc entries from RACE + NPC without dedup-by-type — each contributes its own
                ' shape to the head. So Add for Misc is a real Add, not a replace. The only
                ' guard is exact-FormID dedup so the user can't add the SAME addon twice (the
                ' render would draw it twice with z-fighting on the overlapping geometry).
                If p.HeadPartFormIDs.Contains(newFid) Then
                    MessageBox.Show(Me,
                        "This Misc head part is already in the list. Misc entries can stack but the same FormID can only appear once.",
                        "Add Head Part", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Else
                    p.HeadPartFormIDs.Add(newFid)
                End If
            Else
                ' Non-Misc types 1..9: render's MergeHeadPartsWithRaceDefaults keeps exactly ONE
                ' HDPT per type (last one wins, MainForm.vb:6577). Adding "Eyes blue" when the
                ' NPC already has "Eyes green" replaces the green entry. The HNAM extras of the
                ' old vs the new parent are NOT touched here — the render's recursive HNAM
                ' walk in CollectHeadPartCandidate (MainForm.vb:6782) is the single source of
                ' truth for which addons attach to which parent. Duplicating that walk in the
                ' editor was producing parent-swap bugs; the render dedups by FormID via its
                ' visited set so no double-draw, and addon-orphan handling (a freestanding
                ' Misc whose old parent is gone) is the render's responsibility.
                Dim existingIdx = p.HeadPartFormIDs.FindIndex(Function(fid)
                                                                  Dim hd As HDPT_Data = Nothing
                                                                  Return _allHeadPartsByFid.TryGetValue(fid, hd) AndAlso hd.PartType = partType
                                                              End Function)
                If existingIdx >= 0 Then
                    p.HeadPartFormIDs(existingIdx) = newFid
                Else
                    p.HeadPartFormIDs.Add(newFid)
                End If
            End If

            RefreshHeadPartsList()
            _refresh?.Invoke(FaceRefreshScope.FullReload)
        End Using
    End Sub

    Private Sub OnRemoveHeadPart(sender As Object, e As EventArgs)
        If ListViewHeadParts.SelectedItems.Count = 0 Then Return
        Dim tag = ListViewHeadParts.SelectedItems(0).Tag
        If tag Is Nothing OrElse Not (TypeOf tag Is UInteger) Then Return
        Dim fid = CUInt(tag)
        Dim p = Preset
        Dim idx = p.HeadPartFormIDs.IndexOf(fid)
        If idx < 0 Then Return
        p.HeadPartFormIDs.RemoveAt(idx)
        RefreshHeadPartsList()
        _refresh?.Invoke(FaceRefreshScope.FullReload)
    End Sub

    ' =====================================================================
    ' Section 6 — Hair Color (NPC.QNAM, Textures dirty)
    '
    ' Round-trip: combo lists every CLFM. Selection → preset.HairColorFormID → state.HairColorFormID
    ' (overlay applied at MainForm:3844-3846) → ResolveColorFormColor at render time.
    ' =====================================================================

    Private Sub PopulateHairColorCombo()
        ComboBoxHairColor.BeginUpdate()
        Try
            ComboBoxHairColor.Items.Clear()
            ComboBoxHairColor.Items.Add(New HairColorItem With {.FormID = 0UI, .Display = "(none / preserve)"})
            For Each clfm In _allHairColors
                Dim disp = If(String.IsNullOrEmpty(clfm.FullName), clfm.EditorID, $"{clfm.FullName}  ({clfm.EditorID})")
                ComboBoxHairColor.Items.Add(New HairColorItem With {
                    .FormID = clfm.FormID,
                    .Display = disp,
                    .Color = clfm.Color,
                    .HasColor = clfm.HasColor,
                    .HasRemappingIndex = clfm.HasRemappingIndex,
                    .RemappingIndex = clfm.RemappingIndex
                })
            Next

            ' Select current.
            Dim p = Preset
            Dim targetFid = p.HairColorFormID
            For i = 0 To ComboBoxHairColor.Items.Count - 1
                Dim it = TryCast(ComboBoxHairColor.Items(i), HairColorItem)
                If it IsNot Nothing AndAlso it.FormID = targetFid Then
                    ComboBoxHairColor.SelectedIndex = i
                    Return
                End If
            Next
            ComboBoxHairColor.SelectedIndex = 0
        Finally
            ComboBoxHairColor.EndUpdate()
        End Try
    End Sub

    Private Class HairColorItem
        Public FormID As UInteger
        Public Display As String
        Public Color As Color = Color.Empty
        Public HasColor As Boolean
        Public HasRemappingIndex As Boolean
        Public RemappingIndex As Single
        Public Overrides Function ToString() As String
            Return Display
        End Function
    End Class

    Private Sub OnHairColorChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim it = TryCast(ComboBoxHairColor.SelectedItem, HairColorItem)
        If it Is Nothing Then Return
        Preset.HairColorFormID = it.FormID
        ' Diagnostic: dump what the CLFM resolver actually produced for the chosen entry so we
        ' can correlate "swatch shows black for pink" reports with the underlying parse result.
        ' Hair CLFMs in vanilla typically use a RemappingIndex (FNAM bit 1) and have NO RGB
        ' colour authored in CNAM — their visible colour comes from grayscale-to-palette remap
        ' of HairColor_Lgrad_d.dds at render time. If HasColor=False the swatch falls back to
        ' SystemColors.Control (grey), not black; if it shows black the parse is producing
        ' (0,0,0) which means CNAM is actually 0 in those CLFMs.
        NpcPreviewLog.LogLazy(Function() $"[EDITFACE-HAIRCOLOR] selected '{it.Display}' fid={it.FormID:X8} HasColor={it.HasColor} rgba=({it.Color.R},{it.Color.G},{it.Color.B},{it.Color.A})")
        UpdateHairColorSwatch()
        _refresh?.Invoke(FaceRefreshScope.TexturesOnly)
    End Sub

    Private Sub OnClearHairColor(sender As Object, e As EventArgs)
        ComboBoxHairColor.SelectedIndex = 0  ' (none / preserve)
    End Sub

    Private Sub UpdateHairColorSwatch()
        ' Swatch is repainted via the Paint handler — just invalidate so the next paint
        ' cycle re-reads the current selection. Background is always transparent-friendly
        ' (we paint the entire client rect) so set to a neutral grey for the "(none)" case.
        PanelHairColorSwatch.BackColor = SystemColors.Control
        PanelHairColorSwatch.Invalidate()
    End Sub

    ''' <summary>Paint handler for PanelHairColorSwatch. Three branches mirroring the render path:
    ''' (a) HasRemappingIndex (palette mode) — sample the row at RemappingIndex × paletteHeight
    '''     of the race's hair LUT (same texture the GrayscaleToPalette shader samples) and stretch
    '''     it across the panel.
    ''' (b) HasColor (legacy RGB CLFM) — fill with that opaque RGB.
    ''' (c) Neither — leave the SystemColors.Control background.</summary>
    Private Sub OnPaintHairColorSwatch(sender As Object, e As PaintEventArgs)
        Dim it = TryCast(ComboBoxHairColor.SelectedItem, HairColorItem)
        If it Is Nothing Then Return
        Dim rect = PanelHairColorSwatch.ClientRectangle
        If rect.Width <= 0 OrElse rect.Height <= 0 Then Return

        If it.HasRemappingIndex Then
            EnsureHairPaletteLoaded()
            If _hairPaletteBitmap IsNot Nothing Then
                ' RemappingIndex is the V coordinate into the palette LUT [0..1]. The LUT layout
                ' is: each ROW = one hair tone with a left→right gradient (highlight → shadow).
                ' Stretch that whole row vertically across the panel so the user sees the full
                ' gradient that hair shading actually samples per fragment, not a single mid-tone.
                Dim h = _hairPaletteBitmap.Height
                Dim w = _hairPaletteBitmap.Width
                Dim row = CInt(Math.Round(it.RemappingIndex * (h - 1)))
                If row < 0 Then row = 0
                If row >= h Then row = h - 1
                Dim src As New Rectangle(0, row, w, 1)
                Dim oldInterp = e.Graphics.InterpolationMode
                Dim oldPixelOffset = e.Graphics.PixelOffsetMode
                ' HighQualityBilinear + HalfPixel guarantees the source row is sampled across
                ' the full destination rect with no gaps at the edges (default PixelOffsetMode
                ' leaves a half-pixel seam on Stretch with a 1-px-tall source).
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear
                e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality
                Try
                    Using ia As New System.Drawing.Imaging.ImageAttributes()
                        ia.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY)
                        e.Graphics.DrawImage(_hairPaletteBitmap, rect, 0, row, w, 1, GraphicsUnit.Pixel, ia)
                    End Using
                Finally
                    e.Graphics.InterpolationMode = oldInterp
                    e.Graphics.PixelOffsetMode = oldPixelOffset
                End Try
                Return
            End If
        ElseIf it.HasColor Then
            Using br As New SolidBrush(Color.FromArgb(255, it.Color.R, it.Color.G, it.Color.B))
                e.Graphics.FillRectangle(br, rect)
            End Using
            Return
        End If
        ' Fall-through: neither palette nor RGB available — leave the BackColor showing.
    End Sub

    ''' <summary>Decode the race's hair LUT (HairColor_Lgrad_d.dds or its extended sibling) once
    ''' and cache it for swatch sampling. Lazy: only attempted on first request, and only once
    ''' (failures stick — no retry storm if the DDS is unreadable). Path resolution mirrors
    ''' MainForm.ResolveRaceHairLookupTexture: prefer the path that's actually in FilesDictionary,
    ''' fall back to whichever non-empty path the RACE record declares.</summary>
    Private Sub EnsureHairPaletteLoaded()
        If _hairPaletteResolveAttempted Then Return
        _hairPaletteResolveAttempted = True
        If _race Is Nothing Then Return
        Dim candidates = New String() {_race.HairColorLookupTexture, _race.HairColorExtendedLookupTexture}
        Dim chosen As String = ""
        For Each p In candidates
            Dim corrected = FO4UnifiedMaterial_Class.CorrectTexturePath(p)
            If corrected <> "" AndAlso FilesDictionary_class.Dictionary.ContainsKey(corrected) Then
                chosen = corrected
                Exit For
            End If
        Next
        If chosen = "" Then Return
        Try
            Dim loc As FilesDictionary_class.File_Location = Nothing
            If Not FilesDictionary_class.Dictionary.TryGetValue(chosen, loc) Then Return
            Dim ddsBytes = loc.GetBytes()
            If ddsBytes Is Nothing OrElse ddsBytes.Length = 0 Then Return
            Dim tex = DirectXTexWrapperCLI.Loader.ConvertForBitmap(ddsBytes)
            If tex Is Nothing OrElse Not tex.Loaded OrElse tex.Levels Is Nothing OrElse tex.Levels.Count = 0 Then Return
            Dim lvl = tex.Levels(0)
            If lvl Is Nothing OrElse lvl.Data Is Nothing OrElse lvl.Data.Length = 0 OrElse lvl.Width <= 0 OrElse lvl.Height <= 0 Then Return
            ' ConvertForBitmap returns BGRA byte order (GDI Format32bppArgb). Build the Bitmap
            ' directly from the raw pixels via a pinned handle, then clone into a managed
            ' Bitmap so the cached image survives the GCHandle release.
            Dim handle = System.Runtime.InteropServices.GCHandle.Alloc(lvl.Data, System.Runtime.InteropServices.GCHandleType.Pinned)
            Try
                Using bmp As New Bitmap(lvl.Width, lvl.Height, lvl.Width * 4, System.Drawing.Imaging.PixelFormat.Format32bppArgb, handle.AddrOfPinnedObject())
                    _hairPaletteBitmap = New Bitmap(bmp)
                End Using
            Finally
                handle.Free()
            End Try
        Catch ex As Exception
            NpcPreviewLog.LogLazy(Function() $"[EDITFACE-HAIRSWATCH] palette decode failed: {ex.Message}")
        End Try
    End Sub

    ' =====================================================================
    ' Section 7 — IsCharGenFacePreset (ACBS bit, persists to ESP — no live render effect)
    ' =====================================================================

    Private Sub OnIsCharGenFacePresetChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Preset.IsCharGenFacePreset = CheckBoxIsCharGenFacePreset.Checked
        ' FlagOnly: no MarkDirty needed — the renderer doesn't read this bit. Refresh callback
        ' still fired so the host can refresh the record-details TreeView if open.
        _refresh?.Invoke(FaceRefreshScope.FlagOnly)
    End Sub

    ' =====================================================================
    ' Section 8 — Face Tints (NPC.TETI/TEND, Textures dirty)
    '
    ' Round-trip: ListView lists preset.FaceTintLayers in render order. Add → pick a slot/option
    ' from the RACE TintTemplateGroups + initial Percent. Remove/MoveUp/MoveDown → mutate the list
    ' directly. Detail panel for the selected layer: Palette combo (when EntryType=Palette),
    ' Custom RGB button, Percent slider.
    ' =====================================================================

    Private Sub RefreshTintsList()
        ListViewTints.BeginUpdate()
        Try
            ListViewTints.Items.Clear()
            Dim p = Preset
            ' Display in RACE-Group order (the same order the compositor uses), tied-broken by
            ' the layer's original position in p.FaceTintLayers so two layers with the same
            ' Index keep a stable relative order. The Tag still points at the original index in
            ' p.FaceTintLayers so OnTintSelectionChanged / OnRemoveTint / etc. can mutate the
            ' underlying list directly.
            Dim ordered = p.FaceTintLayers.
                Select(Function(tl, originalIdx)
                           Dim r As Integer = Integer.MaxValue
                           _tintRankByIndex.TryGetValue(tl.Index, r)
                           Return New With {.Layer = tl, .OriginalIdx = originalIdx, .Rank = r}
                       End Function).
                OrderBy(Function(x) x.Rank).
                ThenBy(Function(x) x.OriginalIdx).
                ToList()
            For Each entry In ordered
                Dim tl = entry.Layer
                Dim row As New ListViewItem(DescribeTintGroup(tl))
                row.SubItems.Add(DescribeTintSlot(tl))
                row.SubItems.Add(DescribeTintLayer(tl))
                row.SubItems.Add(DescribeTintColor(tl))
                row.SubItems.Add(tl.Value.ToString(CultureInfo.InvariantCulture))
                row.Tag = entry.OriginalIdx
                ListViewTints.Items.Add(row)
            Next
        Finally
            ListViewTints.EndUpdate()
        End Try
        UpdateTintDetail()
    End Sub

    Private Function DescribeTintGroup(tl As NPC_FaceTintLayerData) As String
        Dim grpName As String = Nothing
        If _tintGroupByIndex.TryGetValue(tl.Index, grpName) Then Return grpName
        Return ""
    End Function

    Private Function DescribeTintSlot(tl As NPC_FaceTintLayerData) As String
        Dim opt = _race?.FindTintOption(tl.Index, _isFemale)
        If opt Is Nothing Then Return $"#{tl.Index}"
        Return $"#{tl.Index} (slot {opt.Slot})"
    End Function

    Private Function DescribeTintLayer(tl As NPC_FaceTintLayerData) As String
        Dim opt = _race?.FindTintOption(tl.Index, _isFemale)
        If opt Is Nothing Then Return "(unknown option)"
        Return If(String.IsNullOrEmpty(opt.Name), $"option {opt.Index}", opt.Name)
    End Function

    Private Function DescribeTintColor(tl As NPC_FaceTintLayerData) As String
        Dim opt = _race?.FindTintOption(tl.Index, _isFemale)
        If opt Is Nothing Then Return ""
        Select Case opt.EntryType
            Case RACE_TintEntryType.Palette
                Return $"#{tl.Color.R:X2}{tl.Color.G:X2}{tl.Color.B:X2}"
            Case RACE_TintEntryType.TextureSet
                Return "(texture)"
            Case Else
                Return "(mask)"
        End Select
    End Function

    Private Sub OnTintSelectionChanged(sender As Object, e As EventArgs)
        If ListViewTints.SelectedItems.Count = 0 Then
            _currentTintIndex = -1
        Else
            _currentTintIndex = CInt(ListViewTints.SelectedItems(0).Tag)
        End If
        UpdateTintDetail()
    End Sub

    Private Sub UpdateTintDetail()
        _suspendEvents = True
        Try
            Dim p = Preset
            If _currentTintIndex < 0 OrElse _currentTintIndex >= p.FaceTintLayers.Count Then
                LabelTintLayerName.Text = "(none — select a layer above)"
                ComboBoxTintPalette.Items.Clear()
                ComboBoxTintPalette.Enabled = False
                ButtonTintCustomRGB.Enabled = False
                TrackBarTintPercent.Enabled = False
                PanelTintColorSwatch.BackColor = SystemColors.Control
                LabelTintPercentValue.Text = "—"
                Return
            End If
            Dim tl = p.FaceTintLayers(_currentTintIndex)
            LabelTintLayerName.Text = DescribeTintLayer(tl)

            Dim opt = _race?.FindTintOption(tl.Index, _isFemale)
            Dim isPalette = (opt IsNot Nothing AndAlso opt.EntryType = RACE_TintEntryType.Palette)
            ComboBoxTintPalette.Enabled = isPalette
            ButtonTintCustomRGB.Enabled = isPalette

            ComboBoxTintPalette.Items.Clear()
            If isPalette Then
                ' Top entry = "Custom" — selected when no CLFM in TemplateColors matches the layer's
                ' RGB. Walking the palette is identical to ResolveTemplateColorIdToAbsolute except
                ' here we go RGB → palette index (display-time), not palette → RGB (save-time).
                ComboBoxTintPalette.Items.Add(New TintPaletteItem With {.IsCustom = True, .Display = "(custom RGB)"})
                Dim selectedIdx As Integer = 0
                For posIdx = 0 To opt.TemplateColors.Count - 1
                    Dim tplCol = opt.TemplateColors(posIdx)
                    Dim clfm As CLFM_Data = Nothing
                    If tplCol.ColorFormID <> 0UI Then
                        Dim rec = _pluginManager.GetRecord(tplCol.ColorFormID)
                        If rec IsNot Nothing AndAlso rec.Header.Signature = "CLFM" Then
                            clfm = RecordParsers.ParseCLFM(rec, _pluginManager)
                        End If
                    End If
                    ' WinForms Panel.BackColor renders alpha != 255 as fully transparent (the
                    ' panel falls back to its parent fill, looking "empty"). CLFM color bytes
                    ' carry an alpha channel that vanilla often leaves at 0 because the engine
                    ' only reads RGB — so we force opaque here for the UI swatch only. The
                    ' rendered tint still uses the layer's tl.Color (set on selection) which
                    ' itself comes from TEND bytes 1..3 (no alpha) so the render is unaffected.
                    Dim swatchColor As Color
                    If clfm IsNot Nothing AndAlso clfm.HasColor Then
                        swatchColor = Color.FromArgb(255, clfm.Color.R, clfm.Color.G, clfm.Color.B)
                    Else
                        swatchColor = Color.Gray
                    End If
                    Dim displayName As String = If(clfm IsNot Nothing AndAlso Not String.IsNullOrEmpty(clfm.FullName), clfm.FullName,
                                                  If(clfm IsNot Nothing AndAlso Not String.IsNullOrEmpty(clfm.EditorID), clfm.EditorID,
                                                     $"#{tplCol.TemplateIndex}"))
                    ComboBoxTintPalette.Items.Add(New TintPaletteItem With {
                        .IsCustom = False,
                        .TemplateIndex = tplCol.TemplateIndex,
                        .ColorFormID = tplCol.ColorFormID,
                        .SwatchColor = swatchColor,
                        .Display = $"#{tplCol.TemplateIndex} — {displayName}"
                    })
                    ' Pick the entry whose CLFM RGB matches the layer's current RGB.
                    If clfm IsNot Nothing AndAlso clfm.HasColor _
                       AndAlso clfm.Color.R = tl.Color.R _
                       AndAlso clfm.Color.G = tl.Color.G _
                       AndAlso clfm.Color.B = tl.Color.B Then
                        selectedIdx = ComboBoxTintPalette.Items.Count - 1
                    End If
                Next
                ComboBoxTintPalette.SelectedIndex = selectedIdx
            End If

            PanelTintColorSwatch.BackColor = If(isPalette, tl.Color, SystemColors.Control)

            TrackBarTintPercent.Enabled = True
            TrackBarTintPercent.Value = ClampInt(tl.Value, 0, 100)
            LabelTintPercentValue.Text = tl.Value.ToString(CultureInfo.InvariantCulture)
        Finally
            _suspendEvents = False
        End Try
    End Sub

    Private Class TintPaletteItem
        Public IsCustom As Boolean
        Public TemplateIndex As UShort
        Public ColorFormID As UInteger
        Public SwatchColor As Color = Color.Empty
        Public Display As String
        Public Overrides Function ToString() As String
            Return Display
        End Function
    End Class

    Private Sub OnTintPaletteChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim p = Preset
        If _currentTintIndex < 0 OrElse _currentTintIndex >= p.FaceTintLayers.Count Then Return
        Dim it = TryCast(ComboBoxTintPalette.SelectedItem, TintPaletteItem)
        If it Is Nothing OrElse it.IsCustom Then Return  ' "Custom RGB" is informational; user clicks the button to actually pick.
        Dim tl = p.FaceTintLayers(_currentTintIndex)
        ' BuildPresetFromState resolves ColorID by RGB-match at save time (MainForm.vb:8722-8748),
        ' so the canonical truth is the layer's RGB, not the TemplateColorIndex. Setting both keeps
        ' the in-memory state internally consistent.
        tl.Color = it.SwatchColor
        tl.TemplateColorIndex = CInt(it.TemplateIndex)
        PanelTintColorSwatch.BackColor = it.SwatchColor
        UpdateTintRowDisplay(_currentTintIndex)
        _refresh?.Invoke(FaceRefreshScope.TexturesOnly)
    End Sub

    Private Sub OnTintCustomRGB(sender As Object, e As EventArgs)
        Dim p = Preset
        If _currentTintIndex < 0 OrElse _currentTintIndex >= p.FaceTintLayers.Count Then Return
        Dim tl = p.FaceTintLayers(_currentTintIndex)
        Using dlg As New ColorDialog()
            dlg.AllowFullOpen = True
            dlg.FullOpen = True
            dlg.AnyColor = True
            dlg.Color = If(tl.Color.IsEmpty, Color.White, tl.Color)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            tl.Color = Color.FromArgb(255, dlg.Color.R, dlg.Color.G, dlg.Color.B)
            ' TemplateColorIndex left as-is — Save (BuildPresetFromState → ResolveTemplateColorIdToAbsolute)
            ' will re-resolve by RGB-match, falling back to opt.TemplateColors[0].TemplateIndex when
            ' the custom RGB isn't in the palette. That's the same fallback LooksMenu in-game uses.
            PanelTintColorSwatch.BackColor = tl.Color
            UpdateTintRowDisplay(_currentTintIndex)
            UpdateTintDetail()  ' re-pick combo (will land on "Custom RGB" since no CLFM matches)
            _refresh?.Invoke(FaceRefreshScope.TexturesOnly)
        End Using
    End Sub

    Private Sub OnTintPercentChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim p = Preset
        If _currentTintIndex < 0 OrElse _currentTintIndex >= p.FaceTintLayers.Count Then Return
        Dim tl = p.FaceTintLayers(_currentTintIndex)
        tl.Value = TrackBarTintPercent.Value
        LabelTintPercentValue.Text = tl.Value.ToString(CultureInfo.InvariantCulture)
        UpdateTintRowDisplay(_currentTintIndex)
        _refresh?.Invoke(FaceRefreshScope.TexturesOnly)
    End Sub

    Private Sub UpdateTintRowDisplay(idx As Integer)
        ' idx is the index into p.FaceTintLayers (the underlying mutable list). The ListView
        ' rows are sorted by RACE-Group rank, so we have to search by Tag rather than indexing.
        Dim p = Preset
        If idx < 0 OrElse idx >= p.FaceTintLayers.Count Then Return
        Dim tl = p.FaceTintLayers(idx)
        Dim row As ListViewItem = Nothing
        For Each item As ListViewItem In ListViewTints.Items
            If item.Tag IsNot Nothing AndAlso CInt(item.Tag) = idx Then
                row = item
                Exit For
            End If
        Next
        If row Is Nothing Then Return
        ' Columns (in order): Group | Slot | Layer | Color | %
        ' SubItems(0) is the Group cell (the row's Text); we touch the dynamic cells only:
        ' Color is index 3 and Percent is index 4.
        row.SubItems(3).Text = DescribeTintColor(tl)
        row.SubItems(4).Text = tl.Value.ToString(CultureInfo.InvariantCulture)
    End Sub

    Private Sub OnAddTint(sender As Object, e As EventArgs)
        ' Build a flat list of (group, option) candidates from RACE for this gender. The picker
        ' filters out Mask-typed options (those are spatial region declarations consumed by the
        ' REGION-SWAP render path, not paintable colour layers), so only Palette and TextureSet
        ' options reach this code.
        If _race Is Nothing Then Return
        Dim groups = If(_isFemale, _race.FemaleTintTemplateGroups, _race.MaleTintTemplateGroups)
        If groups Is Nothing OrElse groups.Count = 0 Then Return

        Using dlg As New TintPickerDialog(groups)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim opt = dlg.SelectedOption
            If opt Is Nothing Then Return
            Dim p = Preset

            ' Guard against duplicates: vanilla NPCs carry exactly one tint layer per RACE
            ' option (Cait fixture: 10 layers with 10 distinct TETI.Index values). The render
            ' compositor would gladly process two layers with the same Index, but they would
            ' over-saturate (each contributes coverage * uColor) and there's no way to tell
            ' which one the user is editing in the detail panel. If a layer already exists
            ' for this option, surface the existing row and skip the Add — same effect as
            ' "select the row that's already there".
            Dim existingLayerIdx As Integer = -1
            For idx = 0 To p.FaceTintLayers.Count - 1
                If p.FaceTintLayers(idx).Index = opt.Index Then
                    existingLayerIdx = idx
                    Exit For
                End If
            Next
            If existingLayerIdx >= 0 Then
                Dim optDisplay = If(String.IsNullOrEmpty(opt.Name), $"option #{opt.Index}", opt.Name)
                MessageBox.Show(Me,
                    $"This NPC already carries a tint layer for '{optDisplay}'. Select the existing row to edit its colour or percent.",
                    "Add Face Tint", MessageBoxButtons.OK, MessageBoxIcon.Information)
                For Each item As ListViewItem In ListViewTints.Items
                    If item.Tag IsNot Nothing AndAlso CInt(item.Tag) = existingLayerIdx Then
                        item.Selected = True
                        item.EnsureVisible()
                        Exit For
                    End If
                Next
                Return
            End If

            Dim newLayer As New NPC_FaceTintLayerData With {
                .Discriminator = If(opt.EntryType = RACE_TintEntryType.Palette, CUShort(1), CUShort(2)),
                .Index = opt.Index,
                .Value = 50,
                .Color = Color.FromArgb(255, 200, 200, 200),
                .TemplateColorIndex = -1
            }
            ' Seed RGB from the palette's first TemplateColor when available, matching LM-in-game
            ' behaviour (clicking Add on an unset layer lands on the first color).
            If opt.EntryType = RACE_TintEntryType.Palette AndAlso opt.TemplateColors IsNot Nothing AndAlso opt.TemplateColors.Count > 0 Then
                Dim firstTplCol = opt.TemplateColors(0)
                If firstTplCol.ColorFormID <> 0UI Then
                    Dim rec = _pluginManager.GetRecord(firstTplCol.ColorFormID)
                    If rec IsNot Nothing AndAlso rec.Header.Signature = "CLFM" Then
                        Dim clfm = RecordParsers.ParseCLFM(rec, _pluginManager)
                        If clfm IsNot Nothing AndAlso clfm.HasColor Then
                            newLayer.Color = clfm.Color
                            newLayer.TemplateColorIndex = CInt(firstTplCol.TemplateIndex)
                        End If
                    End If
                End If
            End If
            Dim newLayerIdx = p.FaceTintLayers.Count
            p.FaceTintLayers.Add(newLayer)
            RefreshTintsList()
            ' Select the newly added entry. Rows are sorted by RACE-Group rank, so the new
            ' entry's row position is determined by its Index group. Find by Tag (which equals
            ' the underlying p.FaceTintLayers index).
            For Each item As ListViewItem In ListViewTints.Items
                If item.Tag IsNot Nothing AndAlso CInt(item.Tag) = newLayerIdx Then
                    item.Selected = True
                    item.EnsureVisible()
                    Exit For
                End If
            Next
            _refresh?.Invoke(FaceRefreshScope.TexturesOnly)
        End Using
    End Sub

    Private Sub OnRemoveTint(sender As Object, e As EventArgs)
        Dim p = Preset
        If _currentTintIndex < 0 OrElse _currentTintIndex >= p.FaceTintLayers.Count Then Return
        p.FaceTintLayers.RemoveAt(_currentTintIndex)
        _currentTintIndex = -1
        RefreshTintsList()
        _refresh?.Invoke(FaceRefreshScope.TexturesOnly)
    End Sub

    ' Layer reorder buttons removed: composition order is determined entirely by the RACE
    ' TintTemplateGroups Options order, both at render time (MainForm.vb:3014) and when the
    ' preset is saved to JSON (MainForm.vb:8952). User-driven Up/Down would have no effect on
    ' either, so the buttons would mislead.

    ' =====================================================================
    ' Section 10 — Vertex morphs (MSDK/MSDV, Morphs dirty)
    '
    ' Round-trip: rows built from RACE.MorphValues (bidirectional) + RACE.MorphPresets (uni).
    ' Slider value → preset.ChargenFaceMorphs[index] → npcData.MorphValues at apply time →
    ' NpcMorphResolver picks MSM0/MSM1 by sign and looks up the chargen TRI delta.
    ' =====================================================================

    ''' <summary>Build the per-MorphGroup UI from <see cref="_groupSections"/>. Each section is a
    ''' GroupBox containing a left-side ListBox of presets (with "(none)" at top) + intensity
    ''' slider, and a right-side stack of N MPGS bidirectional sliders. The synthetic "Other"
    ''' section omits the preset column. Section count, preset count and slider count are all
    ''' driven by the active RACE — no race-specific layout assumptions.</summary>
    ''' <summary>Build a TabControl with one sub-tab per MorphGroup (mirrors the CK chargen
    ''' layout the user is used to). Inside each tab: ListBox of presets at top + intensity
    ''' slider, then MPGS bidi sliders stacked vertically below (not side-by-side). The synthetic
    ''' "Other" tab (keys not consumed by any MPGS, no presets) is a diagnostic — paints its
    ''' tab text RED with a marker so the user notices.</summary>
    Private Sub BuildMorphGroupRows()
        VertexMorphsPanel.SuspendLayout()
        Try
            VertexMorphsPanel.Controls.Clear()
            _bidiBars.Clear()
            _bidiLabels.Clear()
            For Each s In _groupSections
                s.PresetListBox = Nothing
                s.PresetIntensityBar = Nothing
                s.PresetIntensityLabel = Nothing
            Next
            If _groupSections.Count = 0 Then
                Dim empty As New Label() With {
                    .Text = "RACE record declares no vertex morphs (MorphValues / MorphPresets / MorphGroups).",
                    .AutoSize = True, .ForeColor = Color.Gray, .Padding = New Padding(8)}
                VertexMorphsPanel.Controls.Add(empty)
                Return
            End If

            Dim tabs As New TabControl() With {.Dock = DockStyle.Fill}
            ' Owner-draw the tab headers so we can colour the "Other" tab text red — the tab
            ' control's default rendering ignores TabPage.ForeColor.
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed
            AddHandler tabs.DrawItem, AddressOf OnVertexTabDrawItem
            For Each section In _groupSections
                Dim isOther = (section.Presets Is Nothing)
                Dim title = If(isOther, "⚠ " & section.GroupName, section.GroupName)
                Dim page As New TabPage(title) With {.AutoScroll = True, .Padding = New Padding(6)}
                page.Tag = section ' carry section ref for the owner-draw handler
                page.Controls.Add(BuildGroupSectionContent(section))
                tabs.TabPages.Add(page)
            Next
            VertexMorphsPanel.Controls.Add(tabs)
        Finally
            VertexMorphsPanel.ResumeLayout()
        End Try
    End Sub

    Private Sub OnVertexTabDrawItem(sender As Object, e As DrawItemEventArgs)
        Dim tabs = CType(sender, TabControl)
        If e.Index < 0 OrElse e.Index >= tabs.TabPages.Count Then Return
        Dim page = tabs.TabPages(e.Index)
        Dim section = TryCast(page.Tag, MorphGroupSection)
        Dim isOther = (section IsNot Nothing AndAlso section.Presets Is Nothing)
        Dim foreColor = If(isOther, Color.Red, SystemColors.ControlText)
        Dim sf As New StringFormat() With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
        e.Graphics.FillRectangle(SystemBrushes.Control, e.Bounds)
        Using br As New SolidBrush(foreColor)
            Dim font = If(isOther, New Font(tabs.Font, FontStyle.Bold), tabs.Font)
            e.Graphics.DrawString(page.Text, font, br, e.Bounds, sf)
            If isOther Then font.Dispose()
        End Using
    End Sub

    ''' <summary>Build the content of a single MorphGroup tab: vertical stack of [preset list +
    ''' intensity slider] (if the group has presets) followed by [MPGS bidi sliders] (one per
    ''' SliderIndex). All in a single column — sliders go BELOW the preset, not next to it.</summary>
    Private Function BuildGroupSectionContent(section As MorphGroupSection) As Control
        Dim hasPresets = section.Presets IsNot Nothing AndAlso section.Presets.Count > 0

        Dim col As New TableLayoutPanel() With {
            .Dock = DockStyle.Fill, .AutoScroll = True,
            .ColumnCount = 1}
        col.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        Dim row As Integer = 0
        If hasPresets Then
            col.RowCount = row + 1
            col.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            col.Controls.Add(BuildPresetBlock(section), 0, row)
            row += 1
        End If

        For Each k In section.BidiKeys
            col.RowCount = row + 1
            col.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            col.Controls.Add(BuildBidiSliderRow(k), 0, row)
            row += 1
        Next

        ' Trailing filler row so the stacked sliders don't expand to fill vertical space.
        col.RowCount = row + 1
        col.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        Return col
    End Function

    ''' <summary>Top block of a tab: ListBox of presets ("(none)" first) + intensity slider [0..1]
    ''' + value label. Selecting an item clears the previously-selected preset key from the overlay
    ''' and writes the new key with the current intensity.</summary>
    Private Function BuildPresetBlock(section As MorphGroupSection) As Control
        Dim block As New TableLayoutPanel() With {
            .Dock = DockStyle.Top, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 1, .RowCount = 2,
            .Margin = New Padding(0, 0, 0, 8)}
        block.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        block.RowStyles.Add(New RowStyle(SizeType.Absolute, 200))
        block.RowStyles.Add(New RowStyle(SizeType.AutoSize))

        Dim list As New ListBox() With {
            .Dock = DockStyle.Fill, .IntegralHeight = False,
            .HorizontalScrollbar = True}
        list.Items.Add(New PresetItem With {.Index = 0UI, .Display = "(none)"})
        For Each p In section.Presets
            Dim disp = If(String.IsNullOrEmpty(p.PresetName), p.MorphName, p.PresetName)
            list.Items.Add(New PresetItem With {.Index = p.Index, .Display = disp})
        Next
        AddHandler list.SelectedIndexChanged, Sub(s, e) OnPresetListChanged(section)
        block.Controls.Add(list, 0, 0)

        Dim intensityRow As New TableLayoutPanel() With {
            .Dock = DockStyle.Top, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2, .RowCount = 1, .Margin = New Padding(0, 4, 0, 0)}
        intensityRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        intensityRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 60))
        Dim bar As New TrackBar() With {
            .Minimum = 0, .Maximum = 100, .TickFrequency = 10, .TickStyle = TickStyle.None,
            .Dock = DockStyle.Fill, .AutoSize = False, .Height = 28, .Margin = New Padding(2)}
        Dim val As New Label() With {
            .Text = "0.00", .AutoSize = False, .Width = 60,
            .TextAlign = ContentAlignment.MiddleRight,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right}
        AddHandler bar.ValueChanged, Sub(s, e) OnPresetIntensityChanged(section)
        intensityRow.Controls.Add(bar, 0, 0)
        intensityRow.Controls.Add(val, 1, 0)
        block.Controls.Add(intensityRow, 0, 1)

        section.PresetListBox = list
        section.PresetIntensityBar = bar
        section.PresetIntensityLabel = val
        Return block
    End Function

    ''' <summary>One bidi slider row [-1..+1]. Top row: MSM0 label aligned LEFT, MSM1 label
    ''' aligned RIGHT (no ↔ separator). Bottom row: slider full-width + value label on the right.
    ''' Slider width matches the preset intensity slider.</summary>
    Private Function BuildBidiSliderRow(key As UInteger) As Control
        Dim mvDef = ResolveMorphValueDef(key)
        Dim minName = If(mvDef IsNot Nothing AndAlso Not String.IsNullOrEmpty(mvDef.MinName), mvDef.MinName, $"key 0x{key:X8}")
        Dim maxName = If(mvDef IsNot Nothing AndAlso Not String.IsNullOrEmpty(mvDef.MaxName), mvDef.MaxName, $"key 0x{key:X8}")

        Dim row As New TableLayoutPanel() With {
            .Dock = DockStyle.Top, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2, .RowCount = 2,
            .Margin = New Padding(0, 4, 0, 2)}
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 50))
        row.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        row.RowStyles.Add(New RowStyle(SizeType.AutoSize))

        ' Title row: a sub-table that spans both columns of the outer row, with min-name on the
        ' left and max-name on the right, both flush to the slider edges below.
        Dim titleRow As New TableLayoutPanel() With {
            .Dock = DockStyle.Top, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2, .RowCount = 1, .Margin = New Padding(0, 0, 0, 2)}
        titleRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        titleRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        titleRow.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Dim lblMin As New Label() With {.Text = minName, .AutoSize = False,
            .TextAlign = ContentAlignment.MiddleLeft, .Dock = DockStyle.Fill,
            .Margin = New Padding(0)}
        Dim lblMax As New Label() With {.Text = maxName, .AutoSize = False,
            .TextAlign = ContentAlignment.MiddleRight, .Dock = DockStyle.Fill,
            .Margin = New Padding(0)}
        titleRow.Controls.Add(lblMin, 0, 0)
        titleRow.Controls.Add(lblMax, 1, 0)
        row.SetColumnSpan(titleRow, 2)
        row.Controls.Add(titleRow, 0, 0)

        Dim bar As New TrackBar() With {
            .Minimum = -100, .Maximum = 100, .TickFrequency = 25, .TickStyle = TickStyle.None,
            .AutoSize = False, .Height = 22, .Value = 0,
            .Dock = DockStyle.Fill, .Margin = New Padding(0)}
        Dim val As New Label() With {.Text = "0.00", .AutoSize = False,
            .TextAlign = ContentAlignment.MiddleRight,
            .Dock = DockStyle.Fill}
        Dim capturedIdx = key
        AddHandler bar.ValueChanged, Sub(s, e) OnBidiSliderChanged(capturedIdx)

        row.Controls.Add(bar, 0, 1)
        row.Controls.Add(val, 1, 1)

        _bidiBars(key) = bar
        _bidiLabels(key) = val
        Return row
    End Function

    Private Function ResolveMorphValueDef(key As UInteger) As RACE_MorphValueDef
        If _race?.MorphValues Is Nothing Then Return Nothing
        For Each mv In _race.MorphValues
            If mv.Index = key Then Return mv
        Next
        Return Nothing
    End Function

    Private Class PresetItem
        Public Index As UInteger
        Public Display As String
        Public Overrides Function ToString() As String
            Return Display
        End Function
    End Class

    ''' <summary>Reflect overlay state into the group sections. For each section: figure out
    ''' which preset (if any) is active by scanning the group's preset keys against the overlay,
    ''' then sync the ListBox selection + intensity slider; for each MPGS key, sync the bidi
    ''' slider + label.</summary>
    Private Sub LoadMorphGroupValues()
        _suspendEvents = True
        Try
            Dim p = Preset
            For Each section In _groupSections
                If section.PresetListBox IsNot Nothing Then
                    Dim activeIdx As Integer = 0  ' "(none)"
                    Dim activeWeight As Single = 0
                    For i = 1 To section.PresetListBox.Items.Count - 1
                        Dim item = TryCast(section.PresetListBox.Items(i), PresetItem)
                        If item Is Nothing Then Continue For
                        Dim w As Single = 0
                        If p.ChargenFaceMorphs.TryGetValue(item.Index, w) AndAlso Math.Abs(w) > 0.001F Then
                            activeIdx = i
                            activeWeight = w
                            Exit For
                        End If
                    Next
                    section.PresetListBox.SelectedIndex = activeIdx
                    Dim barVal = ClampInt(CInt(Math.Round(activeWeight * 100.0F)), 0, 100)
                    section.PresetIntensityBar.Value = barVal
                    UpdateLabel(section.PresetIntensityLabel, activeWeight)
                End If
                For Each k In section.BidiKeys
                    Dim w As Single = 0
                    p.ChargenFaceMorphs.TryGetValue(k, w)
                    Dim bar As TrackBar = Nothing
                    Dim lbl As Label = Nothing
                    If _bidiBars.TryGetValue(k, bar) AndAlso _bidiLabels.TryGetValue(k, lbl) Then
                        bar.Value = ClampInt(CInt(Math.Round(w * 100.0F)), -100, 100)
                        UpdateLabel(lbl, w)
                    End If
                Next
            Next
        Finally
            _suspendEvents = False
        End Try
    End Sub

    ''' <summary>Bidi MPGS slider: value [-1..+1] writes directly to the overlay key. Zero
    ''' removes the entry so the JSON LM Save round-trip stays clean.</summary>
    Private Sub OnBidiSliderChanged(key As UInteger)
        If _suspendEvents Then Return
        Dim bar As TrackBar = Nothing
        If Not _bidiBars.TryGetValue(key, bar) Then Return
        Dim v As Single = bar.Value / 100.0F
        Dim p = Preset
        If Math.Abs(v) < 0.001F Then
            p.ChargenFaceMorphs.Remove(key)
        Else
            p.ChargenFaceMorphs(key) = v
        End If
        Dim lbl As Label = Nothing
        If _bidiLabels.TryGetValue(key, lbl) Then UpdateLabel(lbl, v)
        _refresh?.Invoke(FaceRefreshScope.Morphs)
    End Sub

    ''' <summary>ListBox selection: drop ALL keys of this group's presets from the overlay,
    ''' then if the new selection isn't "(none)" write the chosen preset's key with the current
    ''' intensity slider value (default 0 → entry would be removed; we add it as 0 anyway so
    ''' the user can immediately drag the intensity slider). Enforces "one preset per group".</summary>
    Private Sub OnPresetListChanged(section As MorphGroupSection)
        If _suspendEvents Then Return
        Dim p = Preset
        For Each presetDef In section.Presets
            p.ChargenFaceMorphs.Remove(presetDef.Index)
        Next
        Dim sel = TryCast(section.PresetListBox.SelectedItem, PresetItem)
        If sel IsNot Nothing AndAlso sel.Index <> 0UI Then
            Dim weight As Single = section.PresetIntensityBar.Value / 100.0F
            If weight < 0.001F Then weight = 1.0F  ' default to full intensity on first selection
            p.ChargenFaceMorphs(sel.Index) = weight
            _suspendEvents = True
            Try
                section.PresetIntensityBar.Value = ClampInt(CInt(Math.Round(weight * 100.0F)), 0, 100)
                UpdateLabel(section.PresetIntensityLabel, weight)
            Finally
                _suspendEvents = False
            End Try
        Else
            _suspendEvents = True
            Try
                section.PresetIntensityBar.Value = 0
                UpdateLabel(section.PresetIntensityLabel, 0.0F)
            Finally
                _suspendEvents = False
            End Try
        End If
        _refresh?.Invoke(FaceRefreshScope.Morphs)
    End Sub

    ''' <summary>Intensity slider for the currently-selected preset in a group. Writes the new
    ''' weight to the active preset's overlay entry; ignored if "(none)" is selected.</summary>
    Private Sub OnPresetIntensityChanged(section As MorphGroupSection)
        If _suspendEvents Then Return
        Dim sel = TryCast(section.PresetListBox?.SelectedItem, PresetItem)
        If sel Is Nothing OrElse sel.Index = 0UI Then Return
        Dim v As Single = section.PresetIntensityBar.Value / 100.0F
        Dim p = Preset
        If Math.Abs(v) < 0.001F Then
            p.ChargenFaceMorphs.Remove(sel.Index)
        Else
            p.ChargenFaceMorphs(sel.Index) = v
        End If
        UpdateLabel(section.PresetIntensityLabel, v)
        _refresh?.Invoke(FaceRefreshScope.Morphs)
    End Sub

    ' =====================================================================
    ' Section 11 — Face Bone Regions (FMRI/FMRS, Pose dirty)
    '
    ' Round-trip: ListBox displays preset.FaceBoneRegions keys (resolved to RACE.{Male/Female}FaceMorphs
    ' names). Right panel shows 7 sliders for the selected region (PosX/Y/Z, RotX/Y/Z, Scale)
    ' editing a Single[] of length 7 stored in preset.FaceBoneRegions[Index].
    ' BuildFaceBoneTransforms (MainForm:4960) consumes effective.FaceMorphs (filled from
    ' preset.FaceBoneRegions in ApplyPresetOverlayToNpcData:8498-8510) and constructs the
    ' skeleton DeltaTransform.
    '
    ' Slider semantics: every value is a [0..1] lerp anchor that the resolver later combines
    ' with FacialBoneRegions JSON minima/maxima (per-bone). 0.5 ≈ default. 0 ≈ fully toward
    ' minima, 1 ≈ fully toward maxima.
    ' =====================================================================

    ''' <summary>Build the per-MorphGroup TabControl + per-region GroupBoxes inside the bone
    ''' regions tab. One sub-tab per AssociatedMorphGroup the JSON declares; regions without a
    ''' group go to a synthetic "Other" sub-tab. Within each sub-tab, every region from the
    ''' active race+gender JSON appears as a GroupBox with 7 sliders (PosX/Y/Z, RotX/Y/Z, Scale)
    ''' — no Add/Remove picker because the JSON enumerates the universe of regions; the user
    ''' simply changes values from the bind pose (0.5 lerp midpoint).</summary>
    Private Sub BuildBoneRegionsUI()
        BoneRegionsContainer.Controls.Clear()
        _regionBars.Clear()
        _regionLabels.Clear()

        Dim regionsFile = MainForm.GetFacialBoneRegionsForRace(_race, _isFemale)
        If regionsFile Is Nothing OrElse regionsFile.Regions Is Nothing OrElse regionsFile.Regions.Count = 0 Then
            Dim empty As New Label() With {
                .Text = $"No FacialBoneRegions JSON for {_race?.EditorID}/{(If(_isFemale, "Female", "Male"))}.",
                .AutoSize = True, .ForeColor = Color.Gray, .Padding = New Padding(8)}
            BoneRegionsContainer.Controls.Add(empty)
            Return
        End If

        ' Group regions by AssociatedMorphGroup (with "Other" bucket for empty/missing).
        Dim grouped As New Dictionary(Of String, List(Of FacialBoneRegion))(StringComparer.OrdinalIgnoreCase)
        Dim groupOrder As New List(Of String)
        For Each rd As FacialBoneRegion In regionsFile.Regions.Values.OrderBy(Function(r) r.Name)
            Dim g = If(String.IsNullOrEmpty(rd.AssociatedMorphGroup), "Other", rd.AssociatedMorphGroup)
            Dim list As List(Of FacialBoneRegion) = Nothing
            If Not grouped.TryGetValue(g, list) Then
                list = New List(Of FacialBoneRegion)
                grouped(g) = list
                groupOrder.Add(g)
            End If
            list.Add(rd)
        Next

        ' Sort group tabs alphabetically except "Other" which goes last.
        groupOrder.Sort(Function(a, b)
                            If a.Equals("Other", StringComparison.OrdinalIgnoreCase) Then Return 1
                            If b.Equals("Other", StringComparison.OrdinalIgnoreCase) Then Return -1
                            Return String.Compare(a, b, StringComparison.OrdinalIgnoreCase)
                        End Function)

        Dim tabs As New TabControl() With {.Dock = DockStyle.Fill}
        For Each groupName In groupOrder
            Dim page As New TabPage(groupName) With {.AutoScroll = True, .Padding = New Padding(4)}
            Dim flow As New FlowLayoutPanel() With {
                .Dock = DockStyle.Fill, .AutoScroll = True,
                .FlowDirection = FlowDirection.LeftToRight,
                .WrapContents = True}
            For Each rd As FacialBoneRegion In grouped(groupName)
                flow.Controls.Add(BuildRegionGroupBox(rd))
            Next
            page.Controls.Add(flow)
            tabs.TabPages.Add(page)
        Next
        BoneRegionsContainer.Controls.Add(tabs)
    End Sub

    ''' <summary>Build one GroupBox per region with Position (X/Y/Z), Rotation (X/Y/Z), Scale +
    ''' a per-axis reset button that sets the slider back to 0.5 (bind pose). Title shows the
    ''' region name; tooltip carries the FMRI Index in hex for the technical user.</summary>
    Private Function BuildRegionGroupBox(rd As FacialBoneRegion) As Control
        Const RowHeight As Integer = 22
        Dim group As New GroupBox() With {
            .Text = rd.Name,
            .Width = 230, .Height = 270,
            .Margin = New Padding(4),
            .Padding = New Padding(6)}
        Dim tip As New ToolTip()
        tip.SetToolTip(group, $"FMRI Index: 0x{rd.ID:X8}")

        Dim bars(6) As TrackBar
        Dim lbls(6) As Label

        Dim layout As New TableLayoutPanel() With {
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 11}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 24))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 38))
        For r = 0 To 10
            layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Next

        Dim addHeader = Sub(text As String, row As Integer)
                            Dim h As New Label() With {.Text = text, .AutoSize = True,
                                .Font = New Font(Font, FontStyle.Bold),
                                .Anchor = AnchorStyles.Left}
                            layout.SetColumnSpan(h, 3)
                            layout.Controls.Add(h, 0, row)
                        End Sub

        Dim addAxisRow = Sub(componentIdx As Integer, axisLabel As String, row As Integer)
                             Dim resetBtn As New Button() With {.Text = axisLabel, .Width = 22, .Height = RowHeight,
                                 .Margin = New Padding(0, 0, 2, 0), .TabStop = False}
                             ' FMRS values are signed [-1..+1] with 0 = bind pose (default).
                             ' LerpFmrs (MainForm.vb:5447) maps -1 → minima, 0 → no delta,
                             ' +1 → maxima. NPC values in vanilla land between -1 and +1 directly,
                             ' not 0..1 lerped around 0.5. Slider must mirror that exactly.
                             Dim bar As New TrackBar() With {.Minimum = -100, .Maximum = 100,
                                 .TickFrequency = 25, .TickStyle = TickStyle.None,
                                 .AutoSize = False, .Height = RowHeight, .Value = 0,
                                 .Dock = DockStyle.Fill, .Margin = New Padding(0)}
                             Dim val As New Label() With {.Text = "0.00", .AutoSize = False, .Width = 38,
                                 .TextAlign = ContentAlignment.MiddleRight,
                                 .Anchor = AnchorStyles.Left Or AnchorStyles.Right}
                             Dim regId = rd.ID
                             Dim compIdx = componentIdx
                             AddHandler bar.ValueChanged, Sub(s, e) OnRegionSliderChanged(regId, compIdx)
                             AddHandler resetBtn.Click, Sub(s, e)
                                                            bar.Value = 0
                                                        End Sub
                             layout.Controls.Add(resetBtn, 0, row)
                             layout.Controls.Add(bar, 1, row)
                             layout.Controls.Add(val, 2, row)
                             bars(componentIdx) = bar
                             lbls(componentIdx) = val
                         End Sub

        addHeader("Position", 0)
        addAxisRow(0, "X", 1)
        addAxisRow(1, "Y", 2)
        addAxisRow(2, "Z", 3)

        addHeader("Rotation", 4)
        addAxisRow(3, "X", 5)
        addAxisRow(4, "Y", 6)
        addAxisRow(5, "Z", 7)

        addHeader("Scale", 8)
        addAxisRow(6, "S", 9)

        group.Controls.Add(layout)
        _regionBars(rd.ID) = bars
        _regionLabels(rd.ID) = lbls
        Return group
    End Function

    ''' <summary>Sync slider values from the overlay preset into all built region controls. For
    ''' regions absent from the preset, sliders stay at 0.5 (bind pose). Called on form open and
    ''' after Reset.</summary>
    Private Sub LoadBoneRegionValues()
        _suspendEvents = True
        Try
            Dim p = Preset
            For Each kv In _regionBars
                Dim regId = kv.Key
                Dim bars = kv.Value
                Dim lbls = _regionLabels(regId)
                Dim arr As Single() = Nothing
                p.FaceBoneRegions.TryGetValue(regId, arr)
                For i = 0 To 6
                    Dim v As Single = If(arr IsNot Nothing AndAlso i < arr.Length, arr(i), 0.0F)
                    bars(i).Value = ClampInt(CInt(Math.Round(v * 100.0F)), -100, 100)
                    UpdateLabel(lbls(i), v)
                Next
            Next
        Finally
            _suspendEvents = False
        End Try
    End Sub

    ''' <summary>Slider value [-1..+1] writes into preset.FaceBoneRegions[regionId][componentIdx].
    ''' If the region isn't yet in the overlay, lazily creates a 7-float array seeded at 0.0
    ''' (bind pose) so a single slider edit doesn't reset the rest on the next read. If after
    ''' the edit ALL 7 components are at 0 the entry is removed entirely so the JSON LM Save
    ''' round-trip stays clean.</summary>
    Private Sub OnRegionSliderChanged(regionId As UInteger, componentIdx As Integer)
        If _suspendEvents Then Return
        Dim bars = _regionBars(regionId)
        Dim lbls = _regionLabels(regionId)
        Dim v As Single = bars(componentIdx).Value / 100.0F
        UpdateLabel(lbls(componentIdx), v)

        Dim p = Preset
        Dim arr As Single() = Nothing
        If Not p.FaceBoneRegions.TryGetValue(regionId, arr) OrElse arr Is Nothing Then
            arr = NewDefaultRegionValues()
        ElseIf arr.Length < 7 Then
            Dim grown = NewDefaultRegionValues()
            Array.Copy(arr, grown, arr.Length)
            arr = grown
        End If
        arr(componentIdx) = v

        ' Drop entries that are entirely at the bind pose (all zero) so we don't pollute the
        ' overlay / JSON LM Save with no-op regions.
        Dim allBind = True
        For i = 0 To Math.Min(6, arr.Length - 1)
            If Math.Abs(arr(i)) > 0.001F Then
                allBind = False
                Exit For
            End If
        Next
        If allBind Then
            p.FaceBoneRegions.Remove(regionId)
        Else
            p.FaceBoneRegions(regionId) = arr
        End If
        _refresh?.Invoke(FaceRefreshScope.Pose)
    End Sub

    Private Shared Function NewDefaultRegionValues() As Single()
        ' 7 sliders defaulting to 0.0 (no delta = bind pose per LerpFmrs semantics in
        ' MainForm.vb:5447). FMRS in vanilla typically carries 7 floats + trailing unknowns;
        ' we author 7 here and extend on demand if the user's record had more.
        Return New Single() {0.0F, 0.0F, 0.0F, 0.0F, 0.0F, 0.0F, 0.0F}
    End Function

    ' =====================================================================
    ' Section 12 — FMIN (Pose dirty)
    ' =====================================================================

    Private Sub OnFminChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim v As Single = TrackBarFmin.Value / 100.0F
        Preset.FacialMorphIntensity = v
        UpdateLabel(LabelFminValue, v)
        _refresh?.Invoke(FaceRefreshScope.Pose)
    End Sub

    ' =====================================================================
    ' Section 13 — Reset section / OK / Cancel
    ' =====================================================================

    Private Sub OnResetSection(sender As Object, e As EventArgs)
        ' Reset = revert the active tab's fields to the snapshot taken at form construction
        ' (the state _appliedPresets[npc] had when Edit Face opened). Same idea as Cancel but
        ' scoped to the active tab so the user can throw away one tab's edits without losing
        ' the others. Source of truth: _priorPreset (deep clone at .ctor) if _hadPriorOverlay,
        ' else the tab's fields are wiped to their unedited defaults.
        Dim active = TabsFace.SelectedTab
        If active Is TabPageFaceParts Then
            ResetFacePartsSection()
        ElseIf active Is TabPageTints Then
            ResetTintsSection()
        ElseIf active Is TabPageVertex Then
            ResetVertexMorphsSection()
        ElseIf active Is TabPageBoneRegions Then
            ResetBoneRegionsSection()
        End If
    End Sub

    ''' <summary>Revert HeadParts + HairColor + IsCharGenFacePreset to the construction snapshot.</summary>
    Private Sub ResetFacePartsSection()
        Dim p = Preset
        Dim src = _seedPreset
        _suspendEvents = True
        Try
            p.HeadPartFormIDs.Clear()
            If src IsNot Nothing Then p.HeadPartFormIDs.AddRange(src.HeadPartFormIDs)
            p.HairColorFormID = If(src IsNot Nothing, src.HairColorFormID, 0UI)
            p.IsCharGenFacePreset = If(src IsNot Nothing, src.IsCharGenFacePreset, CType(Nothing, Boolean?))
            RefreshHeadPartsList()
            PopulateHairColorCombo()
            UpdateHairColorSwatch()
            CheckBoxIsCharGenFacePreset.Checked = p.IsCharGenFacePreset.GetValueOrDefault(
                (_priorAcbsFlagsRaw And AcbsBitIsCharGenFacePreset) <> 0UI)
        Finally
            _suspendEvents = False
        End Try
        _refresh?.Invoke(FaceRefreshScope.FullReload)
    End Sub

    ''' <summary>Revert FaceTintLayers to the construction snapshot.</summary>
    Private Sub ResetTintsSection()
        Dim p = Preset
        Dim src = _seedPreset
        _suspendEvents = True
        Try
            p.FaceTintLayers.Clear()
            If src IsNot Nothing Then
                For Each tl In src.FaceTintLayers
                    p.FaceTintLayers.Add(CloneFaceTint(tl))
                Next
            End If
            _currentTintIndex = -1
            RefreshTintsList()
        Finally
            _suspendEvents = False
        End Try
        _refresh?.Invoke(FaceRefreshScope.TexturesOnly)
    End Sub

    Private Sub ResetVertexMorphsSection()
        Dim p = Preset
        Dim src = _seedPreset
        p.ChargenFaceMorphs.Clear()
        If src IsNot Nothing Then
            For Each kv In src.ChargenFaceMorphs
                p.ChargenFaceMorphs(kv.Key) = kv.Value
            Next
        End If
        _suspendEvents = True
        Try
            LoadMorphGroupValues()
        Finally
            _suspendEvents = False
        End Try
        _refresh?.Invoke(FaceRefreshScope.Morphs)
    End Sub

    Private Sub ResetBoneRegionsSection()
        Dim p = Preset
        Dim src = _seedPreset
        p.FaceBoneRegions.Clear()
        If src IsNot Nothing Then
            For Each kv In src.FaceBoneRegions
                p.FaceBoneRegions(kv.Key) = CType(kv.Value.Clone(), Single())
            Next
        End If
        p.FacialMorphIntensity = If(src IsNot Nothing, src.FacialMorphIntensity, 1.0F)
        _suspendEvents = True
        Try
            TrackBarFmin.Value = ClampInt(CInt(Math.Round(p.FacialMorphIntensity * 100.0F)),
                                          TrackBarFmin.Minimum, TrackBarFmin.Maximum)
            UpdateLabel(LabelFminValue, p.FacialMorphIntensity)
        Finally
            _suspendEvents = False
        End Try
        LoadBoneRegionValues()
        _refresh?.Invoke(FaceRefreshScope.Pose)
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Sub OnCancel(sender As Object, e As EventArgs)
        ' Restore the snapshot taken at construction. If there was no overlay before, drop it.
        If _hadPriorOverlay Then
            _appliedPresets(_rootNpcFormID) = _priorPreset
        Else
            _appliedPresets.Remove(_rootNpcFormID)
        End If
        ' HeadParts and Skin both require full reload to revert (geometry changes). The host
        ' will issue the right MarkDirty when it sees FaceRefreshScope.FullReload.
        _refresh?.Invoke(FaceRefreshScope.FullReload)
        DialogResult = DialogResult.Cancel
        Close()
    End Sub

    ' =====================================================================
    ' Helpers
    ' =====================================================================

    Private Shared Sub UpdateLabel(lbl As Label, value As Single)
        lbl.Text = value.ToString("F2", CultureInfo.InvariantCulture)
    End Sub

    Private Shared Function ClampInt(v As Integer, lo As Integer, hi As Integer) As Integer
        If v < lo Then Return lo
        If v > hi Then Return hi
        Return v
    End Function

End Class
