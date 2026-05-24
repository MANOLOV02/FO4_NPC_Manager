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
''' pick up the effective values. OnLocalFaceRefresh then issues the right MarkDirty pass on
''' the editor's embedded host — granular for tints/morphs/pose, full reload for HeadParts/Skin
''' (which change the rendered geometry).
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

    ' Phase D wiring: editor owns its own NpcRenderHost and drives the embedded preview through
    ' it (no longer the MainForm's _renderHost). _mainForm is held only to invoke pipeline methods
    ' (RenderInHostAsync, RefreshFaceTintLivePreview, RebuildAndApplyMergedPose) that still live
    ' on MainForm but accept an arbitrary host. _mainGore is captured at .ctor as a snapshot of the
    ' MainForm's RenderGore checkbox so the editor honours the user's global gore preference.
    ' _editorHost / HasUncommittedChanges lifted to EditorFormBase (shared with EditBody_Form).
    Private ReadOnly _mainForm As MainForm = Nothing
    Private ReadOnly _mainGore As Boolean = False

    ' Slider drag throttle: model writes happen synchronously inside On...Changed (so OK captures
    ' fresh state) but the costly _refresh callback is deferred. Each slider emits a different
    ' FaceRefreshScope (Tint→TexturesOnly, FMIN/Region→Pose, Bidi/PresetIntensity→Morphs); the
    ' flush emits one invocation per distinct scope requested during the window. Same shape as
    ' Editor_Form.vb (WM): timer fires after a pause; DragEnded forces immediate flush.
    Private WithEvents _refreshTimer As New Timer() With {.Interval = 500, .Enabled = False}
    Private ReadOnly _pendingScopes As New HashSet(Of FaceRefreshScope)

    ' Snapshot for Cancel rollback (the overlay BEFORE we touched it — Nothing if there was
    ' nothing) and for Reset (the post-seed preset, which always carries the original NPC values
    ' even when there was no prior overlay).
    Private ReadOnly _hadPriorOverlay As Boolean
    Private ReadOnly _priorPreset As LooksmenuLoader.LooksmenuPreset
    Private _seedPreset As LooksmenuLoader.LooksmenuPreset
    Private ReadOnly _priorAcbsFlagsRaw As UInteger

    ' _suspendEvents / _seedingToggles lifted to EditorFormBase (shared with EditBody_Form).

    ' Vertex morph UI: one section per RACE MorphGroup (mirrors CK chargen UI). Each section
    ' has a preset ListBox + intensity slider (if the group has presets) and N MPGS sliders
    ' (bidirectional, one per group-attached MorphValue key). Keys not referenced by any group
    ' MPGS go to a synthetic "Other Sliders" section at the end if any exist.
    Private ReadOnly _groupSections As New List(Of MorphGroupSection)
    Private ReadOnly _bidiBars As New Dictionary(Of UInteger, FO4_Base_Library.TinySliderTextBox)
    Private ReadOnly _bidiKeyToGroup As New Dictionary(Of UInteger, MorphGroupSection)
    Private ReadOnly _presetKeyToGroup As New Dictionary(Of UInteger, MorphGroupSection)

    ' Bone region per-row controls. Each region gets 7 sliders + 7 labels (PosX/Y/Z, RotX/Y/Z,
    ' Scale). We index by (regionId, componentIdx 0..6) so OnRegionSliderChanged can route the
    ' value back into preset.FaceBoneRegions[regionId][componentIdx]. Built once in
    ' BuildBoneRegionsUI from the JSON FacialBoneRegions list of the active race+gender.
    Private ReadOnly _regionBars As New Dictionary(Of UInteger, FO4_Base_Library.TinySliderTextBox())

    ' Currently selected tint, for routing slider events. _currentTintIndex points into
    ' p.FaceTintLayers when the selection is an NPC override; for race-default rows it stays
    ' at -1 and _currentTintVirtualLayer carries the synthesized data so the detail panel can
    ' read it (palette swatch, percent display) but every mutate path bails on IsRaceDefault.
    Private _currentTintIndex As Integer = -1
    Private _currentTintIsRaceDefault As Boolean = False
    Private _currentTintVirtualLayer As NPC_FaceTintLayerData = Nothing

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
    ''' (one selection per group, "(none)" entry at top) + intensity slider [0..1] for the
    ''' chosen preset, plus one bidirectional [-1..+1] slider per MPGS key (group-attached
    ''' slider). The synthetic "Other" section uses GroupName="" and Presets=Nothing to flag
    ''' it as a slider-only bucket for MorphValue keys not referenced by any MorphGroup.</summary>
    Private Class MorphGroupSection
        Public GroupName As String
        Public Presets As List(Of RACE_MorphPresetDef)   ' Nothing = "Other Sliders" section
        Public BidiKeys As New List(Of UInteger)         ' MPGS keys; resolved to MorphValueDef for MSM0/MSM1
        ' Live UI controls (set during build).
        Public PresetListBox As ListBox
        Public PresetIntensityBar As FO4_Base_Library.TinySliderTextBox
    End Class

    Public Sub New(rootNpcFormID As UInteger,
                   appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                   pluginManager As PluginManager,
                   race As RACE_Data,
                   raceFormID As UInteger,
                   isFemale As Boolean,
                   formatNpcRef As Func(Of UInteger, String),
                   priorAcbsFlagsRaw As UInteger,
                   mainForm As MainForm,
                   mainGore As Boolean)
        InitializeComponent()
        _rootNpcFormID = rootNpcFormID
        _appliedPresets = appliedPresets
        _pluginManager = pluginManager
        _race = race
        _raceFormID = raceFormID
        _isFemale = isFemale
        _refresh = AddressOf OnLocalFaceRefresh
        _formatNpcRef = formatNpcRef
        _priorAcbsFlagsRaw = priorAcbsFlagsRaw
        _mainForm = mainForm
        _mainGore = mainGore

        ' Snapshot any existing overlay so Cancel can restore byte-equivalent.
        Dim existing As LooksmenuLoader.LooksmenuPreset = Nothing
        _hadPriorOverlay = _appliedPresets.TryGetValue(rootNpcFormID, existing)
        _priorPreset = If(_hadPriorOverlay, ClonePreset(existing), Nothing)

        ' Ensure an overlay exists for live editing. Removed in Cancel if it didn't exist.
        Dim p As LooksmenuLoader.LooksmenuPreset = Nothing
        If Not _appliedPresets.TryGetValue(rootNpcFormID, p) OrElse p Is Nothing Then
            p = New LooksmenuLoader.LooksmenuPreset With {
                .Gender = If(_isFemale, CByte(1), CByte(0))
            }
            _appliedPresets(rootNpcFormID) = p
        End If

        BuildHeadPartCache()
        BuildHairColorCache()
        BuildMorphGroupSections()
        BuildTintGroupRanks()
        BuildBoneRegionsUI()

        ' Click-to-sort on the two ListViews (HeadParts + Tints). The helper subscribes to
        ' ColumnClick and rewires ListViewItemSorter on every click; Refresh*List() repopulates
        ' rows but the sorter persists, so a user-chosen sort survives subsequent overlay
        ' mutations (Add/Remove HeadPart, Add/Remove Tint).
        SortableListView.Attach(ListViewHeadParts)
        SortableListView.Attach(ListViewTints)

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

        ' Filter sliders/presets to those whose morph names actually exist in the chargen TRI
        ' loaded for the face shape — replicates engine in-game behavior of silently skipping
        ' MSDV entries with names not present in the TRI. Vanilla data is inconsistent for some
        ' races (HumanChildRace declares Brow/Chin sliders but its HDPT points at the adult
        ' BaseFemaleHeadChargen.tri which lacks those names). Without this filter the editor
        ' offers controls with zero visible effect.
        '
        ' Empty set means "TRI not yet loaded / unknown" — fall back to no-filter so we don't
        ' block the user when the editor opens before the renderer published the morph names.
        '
        ' Source: MainForm._renderHost (NOT _editorHost). BuildMorphGroupSections runs in the
        ' editor's CONSTRUCTOR, before the editor's Shown handler creates _editorHost and fires
        ' its first render. MainForm's host always has the latest render state by the time
        ' ButtonEditFace_Click runs (it's the host that just rendered the NPC the user is
        ' editing). So we read the set from there.
        Dim availableMorphs As HashSet(Of String) = Nothing
        If _mainForm IsNot Nothing AndAlso _mainForm._renderHost IsNot Nothing _
           AndAlso _mainForm._renderHost.LastFaceTriMorphNames IsNot Nothing _
           AndAlso _mainForm._renderHost.LastFaceTriMorphNames.Count > 0 Then
            availableMorphs = _mainForm._renderHost.LastFaceTriMorphNames
        End If
        Dim sliderIsAvailable = Function(mvDef As RACE_MorphValueDef) As Boolean
                                    If availableMorphs Is Nothing Then Return True
                                    If Not String.IsNullOrEmpty(mvDef.MinName) AndAlso availableMorphs.Contains(mvDef.MinName) Then Return True
                                    If Not String.IsNullOrEmpty(mvDef.MaxName) AndAlso availableMorphs.Contains(mvDef.MaxName) Then Return True
                                    Return False
                                End Function
        Dim mvDefByIndex As New Dictionary(Of UInteger, RACE_MorphValueDef)
        If _race.MorphValues IsNot Nothing Then
            For Each mv In _race.MorphValues
                mvDefByIndex(mv.Index) = mv
            Next
        End If

        Dim groups = If(_isFemale, _race.FemaleMorphGroups, _race.MaleMorphGroups)
        Dim consumedBidi As New HashSet(Of UInteger)

        ' Group rule: a group is shown ONLY when it has at least one usable preset (declared in
        ' RACE.MorphGroups[*].Presets AND present in the loaded chargen TRI). Sliders are
        ' secondary fine-tuning controls inside a preset-driven editor — without a preset there
        ' is no top-level choice for the user, so the group has no UI meaning. This matches CK's
        ' chargen behaviour (HumanChildRace declares slider-only groups but vanilla CK does not
        ' offer a face-morph editor for children, exactly because no presets are authored).
        If groups IsNot Nothing Then
            For Each g In groups
                Dim filteredPresets As New List(Of RACE_MorphPresetDef)
                If g.Presets IsNot Nothing Then
                    For Each p In g.Presets
                        If availableMorphs Is Nothing _
                           OrElse (Not String.IsNullOrEmpty(p.MorphName) AndAlso availableMorphs.Contains(p.MorphName)) Then
                            filteredPresets.Add(p)
                        End If
                    Next
                End If
                If filteredPresets.Count = 0 Then Continue For

                Dim filteredSliders As New List(Of UInteger)
                If g.SliderIndices IsNot Nothing Then
                    For Each k In g.SliderIndices
                        Dim mvDef As RACE_MorphValueDef = Nothing
                        If mvDefByIndex.TryGetValue(k, mvDef) AndAlso sliderIsAvailable(mvDef) Then
                            filteredSliders.Add(k)
                        End If
                    Next
                End If

                Dim section As New MorphGroupSection With {
                    .GroupName = If(g.Name, ""),
                    .Presets = filteredPresets}
                For Each k In filteredSliders
                    section.BidiKeys.Add(k)
                    consumedBidi.Add(k)
                    _bidiKeyToGroup(k) = section
                Next
                For Each p In filteredPresets
                    _presetKeyToGroup(p.Index) = section
                Next
                _groupSections.Add(section)
            Next
        End If

        ' Orphan sliders fallback: only meaningful when the race uses a preset-driven editor
        ' overall. If no preset-bearing group survived the filter above, the race effectively
        ' has no face-morph editor for this gender (vanilla HumanChildRace) — surfacing orphans
        ' in that case would be inconsistent with the "no presets → no sliders" rule.
        If _race.MorphValues IsNot Nothing AndAlso _groupSections.Count > 0 Then
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
                If Not sliderIsAvailable(mv) Then Continue For
                orphans.Add(mv.Index)
                Dim mvLocal = mv
            Next
            If orphans.Count > 0 Then
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

    ''' <summary>Snapshot/restore clone — delegates to the canonical helper so any new
    ''' LooksmenuPreset field propagates here automatically.</summary>
    Private Shared Function ClonePreset(p As LooksmenuLoader.LooksmenuPreset) As LooksmenuLoader.LooksmenuPreset
        Return LooksmenuLoader.ClonePreset(p)
    End Function

    ''' <summary>Per-layer clone — delegates to the canonical helper.</summary>
    Private Shared Function CloneFaceTint(tl As NPC_FaceTintLayerData) As NPC_FaceTintLayerData
        Return LooksmenuLoader.CloneFaceTintLayer(tl)
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
                ' WYSIWYG: LM SkinTemplate head/headRear HDPTs win over race defaults + raw NPC
                ' PNAM (mirrors NpcRecordOverlay.ApplyLmHdptReplacement order, which runs AFTER
                ' race defaults + preset.HeadPartFormIDs are merged into the shadow). Without
                ' this, opening Edit Face on an NPC with an active LM template hides the
                ' template's headRear from the user, who would lose it the moment they touch
                ' any HeadParts control (the editor takes ownership via HasHeadPartFormIDs=True).
                If Not String.IsNullOrEmpty(p.SkinTemplateId) Then
                    Dim tpl = _mainForm.GetLmSkinTemplateCandidates(_isFemale).
                        FirstOrDefault(Function(t) String.Equals(t.Id, p.SkinTemplateId, StringComparison.Ordinal))
                    If tpl IsNot Nothing Then
                        Dim genderIdx As Integer = If(_isFemale, 1, 0)
                        Dim tplHdpts As New List(Of UInteger)
                        If tpl.HeadHdptFormID(genderIdx) <> 0UI Then tplHdpts.Add(tpl.HeadHdptFormID(genderIdx))
                        If tpl.HeadRearHdptFormID(genderIdx) <> 0UI Then tplHdpts.Add(tpl.HeadRearHdptFormID(genderIdx))
                        seedFromList(tplHdpts)
                    End If
                End If

                For Each t In mergedByType.Keys.OrderBy(Function(k) k)
                    p.HeadPartFormIDs.Add(mergedByType(t))
                Next
                p.HeadPartFormIDs.AddRange(freestandingMisc)
            End If
            ' Editor takes ownership: from now on the user's edits (incl. removing all parts) are
            ' authoritative. Without HasHeadPartFormIDs=True, a wipe would look like "preset never
            ' carried HeadParts" and Save would preserve raw NPC PNAM instead of emitting empty.
            p.HasHeadPartFormIDs = True
            ' Edit Face seizes the Has* authority. If an LM template's Materialize had previously
            ' set HasHeadPartFormIDsSetByTemplate=True, that's stale now — Retract should NOT
            ' flip Has* back to False just because the user later switches templates in EditBody,
            ' because Edit Face just claimed ownership. Clear the tracker so the user's edits
            ' survive any subsequent template change.
            p.HasHeadPartFormIDsSetByTemplate = False
            RefreshHeadPartsList()

            ' --- HairColor ---
            ' Do NOT copy rawNpc.HairColorFormID into the overlay. preset.HairColorFormID = 0
            ' is the "preserve" semantic per LM contract (CharGenInterface.cpp:344-359 — missing
            ' key = preserve runtime value) AND per ESP contract (HCLF subrecord is optional per
            ' wbDefinitionsFO4.pas:10749, missing = inherit from template chain or RACE.HCLF
            ' default per wbDefinitionsFO4.pas:11575). The combo arranges in "(none / preserve)"
            ' when the overlay carries no override; the swatch resolves the effective color
            ' (race default chain) so the user sees what's currently visible on the NPC.
            PopulateHairColorCombo()
            UpdateHairColorSwatch()

            ' --- IsCharGenFacePreset ---
            If Not p.IsCharGenFacePreset.HasValue Then
                p.IsCharGenFacePreset = (_priorAcbsFlagsRaw And AcbsBitIsCharGenFacePreset) <> 0UI
            End If
            CheckBoxIsCharGenFacePreset.Checked = p.IsCharGenFacePreset.GetValueOrDefault(False)

            ' --- Tints ---
            ' Mark as "present in this preset" the moment the editor takes ownership: from now
            ' on the user's edits (including deletions) authoritatively define the field. If the
            ' overlay was empty, seed it from the raw NPC so the user sees the current state to
            ' edit. If the overlay already had content, leave it.
            Dim presetTintCountBefore = p.FaceTintLayers.Count
            Dim rawTintCount = If(rawNpc IsNot Nothing, rawNpc.FaceTintLayers.Count, -1)
            If p.FaceTintLayers.Count = 0 AndAlso rawNpc IsNot Nothing AndAlso rawNpc.FaceTintLayers.Count > 0 Then
                For Each tl In rawNpc.FaceTintLayers
                    p.FaceTintLayers.Add(CloneFaceTint(tl))
                Next
            End If
            p.HasFaceTintLayers = True
            RefreshTintsList()

            ' --- Vertex morphs (MSDK/MSDV) ---
            If p.ChargenFaceMorphs.Count = 0 AndAlso rawNpc IsNot Nothing AndAlso rawNpc.MorphValues.Count > 0 Then
                For Each kv In rawNpc.MorphValues
                    p.ChargenFaceMorphs(kv.Key) = kv.Value
                Next
            End If
            p.HasChargenFaceMorphs = True
            BuildMorphGroupRows()
            LoadMorphGroupValues()

            ' --- Face bone regions (FMRI/FMRS) ---
            If p.FaceBoneRegions.Count = 0 AndAlso rawNpc IsNot Nothing AndAlso rawNpc.FaceMorphs.Count > 0 Then
                For Each fm In rawNpc.FaceMorphs
                    p.FaceBoneRegions(fm.Index) = fm.Values.ToArray()
                Next
            End If
            p.HasFaceBoneRegions = True
            LoadBoneRegionValues()

            ' --- FMIN ---
            ' If there's no prior overlay (NPC never touched by LM or by a prior Edit Face), seed
            ' the slider from the raw NPC record so it reflects the actual current value (records
            ' authored at 1.4 / 0.7 / etc. shouldn't snap to 1.0 just because the editor opened).
            ' If an overlay DOES exist (LM preset load or prior edit), trust p.FacialMorphIntensity
            ' verbatim — 1.0F is a valid explicit value per LM contract (omitted key parses to 1.0
            ' identical to an explicit "Intensity":1.0), NOT a "default sentinel" we can overwrite.
            ' Prior heuristic (Math.Abs(p.FMIN - 1.0F) < epsilon → fallback to raw) wrongly clobbered
            ' LM presets that explicitly carried FMIN=1.0.
            If Not _hadPriorOverlay AndAlso rawNpc IsNot Nothing Then
                p.FacialMorphIntensity = If(rawNpc.FacialMorphIntensity > 0.0F, rawNpc.FacialMorphIntensity, 1.0F)
            End If
            TrackBarFmin.Value = p.FacialMorphIntensity
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
        AddHandler ButtonRemoveAllInCategory.Click, AddressOf OnRemoveAllInCategory
        AddHandler ButtonRemoveZeroedTints.Click, AddressOf OnRemoveZeroedTints
        AddHandler TextBoxTintFilter.TextChanged, AddressOf OnTintFilterChanged
        AddHandler ListViewTints.SelectedIndexChanged, AddressOf OnTintSelectionChanged
        AddHandler ComboBoxTintPalette.SelectedIndexChanged, AddressOf OnTintPaletteChanged
        AddHandler ButtonTintCustomRGB.Click, AddressOf OnTintCustomRGB
        AddHandler TrackBarTintPercent.ValueChanged, AddressOf OnTintPercentChanged
        AddHandler TrackBarTintPercent.DragEnded, AddressOf OnSliderDragEnded

        ' Bone region slider handlers are wired per-control inside BuildBoneRegionsUI.

        AddHandler TrackBarFmin.ValueChanged, AddressOf OnFminChanged
        AddHandler TrackBarFmin.DragEnded, AddressOf OnSliderDragEnded
    End Sub

    ' =====================================================================
    ' Section 5 — Head Parts (NPC.PNAM, full reload on change)
    '
    ' Round-trip: ListBox displays preset.HeadPartFormIDs. Add → HeadPartPicker_Form (filtered
    ' by partType + RACE.RNAM + gender). Remove → drop entry. Each mutation triggers
    ' refresh(FullReload), which the host translates to LoadNPCOnDemandAsyncFromExisting →
    ' ResolveNPCBaseState consumes preset.HeadPartFormIDs at MainForm:3841-3842.
    ' =====================================================================

    ''' <summary>Tag payload for ListViewHeadParts rows. IsRaceDefault=True means the entry comes
    ''' from RACE.{Male,Female}HeadParts (gender-specific) because the NPC override has no entry of
    ''' that PartType. The render's MergeHeadPartsWithRaceDefaults (MainForm.vb:6582) does the same
    ''' merge — the editor mirrors it so the user sees what the render will draw, not just the raw
    ''' NPC override list. Race defaults are read-only here: removing them requires a different
    ''' mechanism (explicit "no part" override) which the model doesn't currently support.
    '''
    ''' IsHnamExtra=True means the row is a sub-part derived from a parent HDPT's ExtraPartFormIDs
    ''' (hairlines, eyelashes, AO/wet, mouth shadow). The render's CollectHeadPartCandidate walks
    ''' the HNAM chain (MainForm.vb:7544) and pulls these automatically; they don't need to be
    ''' stored in preset.HeadPartFormIDs. The editor displays them indented under their parent so
    ''' the user can see what the render will draw without making them removable independently
    ''' (removing the parent cascade-removes any duplicate Misc in preset that matches HNAM).</summary>
    Private Class HeadPartRowTag
        Public FormID As UInteger
        Public IsRaceDefault As Boolean
        Public IsHnamExtra As Boolean
    End Class

    Private Sub RefreshHeadPartsList()
        ListViewHeadParts.BeginUpdate()
        Try
            ListViewHeadParts.Items.Clear()
            Dim p = Preset

            ' Pre-compute: para cada Misc en preset, ¿algún parent non-Misc del preset o de los
            ' RACE defaults visibles la declara en su HNAM? Si sí, sale como sub-row del parent y
            ' NO como top-level entry. Esto elimina la duplicación visual que vanilla NPC.PNAM
            ' frecuentemente trae (hairline listada tanto en HNAM como standalone Misc en PNAM).
            Dim raceDefaults = If(_isFemale, _race?.FemaleHeadPartFormIDs, _race?.MaleHeadPartFormIDs)
            Dim visibleParents As New List(Of UInteger)
            Dim visibleParentIsRaceDefault As New Dictionary(Of UInteger, Boolean)

            ' Parents NPC-override (no Misc).
            Dim overriddenTypes As New HashSet(Of Integer)
            For Each fid In p.HeadPartFormIDs
                Dim hd As HDPT_Data = Nothing
                If _allHeadPartsByFid.TryGetValue(fid, hd) AndAlso hd.PartType <> HdptTypeMisc Then
                    overriddenTypes.Add(hd.PartType)
                    visibleParents.Add(fid)
                    visibleParentIsRaceDefault(fid) = False
                End If
            Next
            ' Parents RACE-default (no Misc) que llenan PartTypes no override-ados.
            If raceDefaults IsNot Nothing Then
                For Each fid In raceDefaults
                    Dim hd As HDPT_Data = Nothing
                    If Not _allHeadPartsByFid.TryGetValue(fid, hd) Then Continue For
                    If hd.PartType = HdptTypeMisc Then Continue For
                    If overriddenTypes.Contains(hd.PartType) Then Continue For
                    visibleParents.Add(fid)
                    visibleParentIsRaceDefault(fid) = True
                Next
            End If

            ' Set de FormIDs que serán mostrados como HNAM-extras debajo de algún parent visible.
            ' Estos se excluyen de la sección top-level Misc para no duplicar visualmente.
            Dim claimedAsExtra As New HashSet(Of UInteger)
            Dim extrasByParent As New Dictionary(Of UInteger, List(Of UInteger))
            For Each parentFid In visibleParents
                Dim hd As HDPT_Data = Nothing
                If Not _allHeadPartsByFid.TryGetValue(parentFid, hd) Then Continue For
                If hd.ExtraPartFormIDs Is Nothing OrElse hd.ExtraPartFormIDs.Count = 0 Then Continue For
                Dim list As New List(Of UInteger)
                For Each ex In hd.ExtraPartFormIDs
                    Dim exData As HDPT_Data = Nothing
                    If Not _allHeadPartsByFid.TryGetValue(ex, exData) Then Continue For
                    list.Add(ex)
                    claimedAsExtra.Add(ex)
                Next
                If list.Count > 0 Then extrasByParent(parentFid) = list
            Next

            ' Emisión: para cada FID en preset que sea Parent (non-Misc), fila top-level + sub-rows
            ' HNAM extras. Misc en preset que ya estén claimedAsExtra se omiten (saldrán bajo su
            ' parent). Misc en preset NO claimedAsExtra (addons standalone legítimos: mouth shadow,
            ' AO/wet sueltos) salen como top-level normal.
            For Each fid In p.HeadPartFormIDs
                Dim hd As HDPT_Data = Nothing
                If Not _allHeadPartsByFid.TryGetValue(fid, hd) Then
                    ' Unresolved: lo mostramos top-level para que el usuario vea el FormID roto.
                    ListViewHeadParts.Items.Add(BuildHeadPartRow(fid, isRaceDefault:=False))
                    Continue For
                End If
                If hd.PartType = HdptTypeMisc AndAlso claimedAsExtra.Contains(fid) Then
                    ' Va a salir como sub-row del parent que la reclama. Skip top-level.
                    Continue For
                End If
                ListViewHeadParts.Items.Add(BuildHeadPartRow(fid, isRaceDefault:=False))
                ' Si es parent non-Misc, emit las HNAM-extras como sub-rows readonly.
                Dim extras As List(Of UInteger) = Nothing
                If hd.PartType <> HdptTypeMisc AndAlso extrasByParent.TryGetValue(fid, extras) Then
                    For Each ex In extras
                        ListViewHeadParts.Items.Add(BuildHeadPartRow(ex, isRaceDefault:=False, isHnamExtra:=True))
                    Next
                End If
            Next

            ' RACE defaults non-Misc que llenen PartTypes que el NPC no claimeó. Mismo flujo:
            ' fila top-level + sub-rows HNAM-extras (también readonly por IsRaceDefault).
            If raceDefaults IsNot Nothing Then
                For Each fid In raceDefaults
                    Dim hd As HDPT_Data = Nothing
                    If Not _allHeadPartsByFid.TryGetValue(fid, hd) Then Continue For
                    If hd.PartType = HdptTypeMisc Then Continue For
                    If overriddenTypes.Contains(hd.PartType) Then Continue For
                    ListViewHeadParts.Items.Add(BuildHeadPartRow(fid, isRaceDefault:=True))
                    Dim extras As List(Of UInteger) = Nothing
                    If extrasByParent.TryGetValue(fid, extras) Then
                        For Each ex In extras
                            ListViewHeadParts.Items.Add(BuildHeadPartRow(ex, isRaceDefault:=True, isHnamExtra:=True))
                        Next
                    End If
                Next
            End If
        Finally
            ListViewHeadParts.EndUpdate()
        End Try
    End Sub

    ''' <summary>Build a 5-column ListViewItem for a head-part FormID. Columns mirror the picker
    ''' layout (Type / Editor ID / Name / Plugin / FormID) so the eye doesn't have to translate
    ''' between the two views. Unresolved FormIDs (e.g. plugin missing) still get a row showing
    ''' the FormID so the user can see what's broken instead of getting a silent gap. Race-default
    ''' and HNAM-extra rows are rendered in gray and tagged so OnRemoveHeadPart can refuse to
    ''' mutate them. HNAM-extra rows are also indented in the Type column to make the
    ''' parent-child relationship visible.</summary>
    Private Function BuildHeadPartRow(fid As UInteger, isRaceDefault As Boolean, Optional isHnamExtra As Boolean = False) As ListViewItem
        Dim tag As New HeadPartRowTag With {.FormID = fid, .IsRaceDefault = isRaceDefault, .IsHnamExtra = isHnamExtra}
        Dim hd As HDPT_Data = Nothing
        Dim hex = fid.ToString("X8")
        Dim indent As String = If(isHnamExtra, "    └─ ", "")
        If Not _allHeadPartsByFid.TryGetValue(fid, hd) Then
            Dim missing As New ListViewItem(indent & "(unresolved)")
            missing.SubItems.Add("")
            missing.SubItems.Add("")
            missing.SubItems.Add("")
            missing.SubItems.Add(hex)
            missing.Tag = tag
            If isRaceDefault OrElse isHnamExtra Then missing.ForeColor = SystemColors.GrayText
            Return missing
        End If
        Dim plugin As String = ""
        Dim rec = _pluginManager.GetRecord(fid)
        If rec IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(rec.SourcePluginName) Then plugin = rec.SourcePluginName
        Dim typeText = indent & HdptTypeName(hd.PartType)
        If isRaceDefault Then typeText &= " (RACE)"
        If isHnamExtra Then typeText &= " (HNAM)"
        Dim row As New ListViewItem(typeText)
        row.SubItems.Add(If(hd.EditorID, ""))
        row.SubItems.Add(If(hd.FullName, ""))
        row.SubItems.Add(plugin)
        row.SubItems.Add(hex)
        row.Tag = tag
        If isRaceDefault OrElse isHnamExtra Then row.ForeColor = SystemColors.GrayText
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
        Dim tag = TryCast(ListViewHeadParts.SelectedItems(0).Tag, HeadPartRowTag)
        If tag Is Nothing Then Return
        ' Race defaults are read-only — they come from RACE.{Male,Female}HeadParts and aren't
        ' part of NPC.HeadPartFormIDs. The user can override them via Add (which will replace
        ' the default in the merge) but can't outright "remove" them from this view.
        If tag.IsRaceDefault Then Return
        ' HNAM-extra sub-rows are read-only too — they're derived from the parent's HDPT.HNAM,
        ' not stored in preset. Removing the parent cascade-removes them (below); the row itself
        ' is a view of the parent's HNAM, not an independent entry.
        If tag.IsHnamExtra Then Return
        Dim p = Preset
        Dim idx = p.HeadPartFormIDs.IndexOf(tag.FormID)
        If idx < 0 Then Return
        p.HeadPartFormIDs.RemoveAt(idx)

        ' Cascade: si el HDPT removido es un parent non-Misc con HNAM-extras, los Misc del preset
        ' que estén declarados en su ExtraPartFormIDs quedan como orphans (effective type=0, paleta
        ' no aplica → color BGSM default). Vanilla NPC.PNAM frecuentemente lista hairlines/etc. tanto
        ' en HNAM del parent como standalone Misc en PNAM; cuando el usuario borra el parent, esos
        ' standalone se vuelven huérfanos y rompen el render. Los limpiamos acá. No tocamos Misc del
        ' preset cuyo FormID NO esté en el HNAM del parent removido — pueden ser addons legítimos
        ' independientes (mouth shadow, AO/wet) que el usuario quiere conservar.
        Dim removedHdpt As HDPT_Data = Nothing
        If _allHeadPartsByFid.TryGetValue(tag.FormID, removedHdpt) AndAlso
           removedHdpt.PartType <> HdptTypeMisc AndAlso
           removedHdpt.ExtraPartFormIDs IsNot Nothing AndAlso
           removedHdpt.ExtraPartFormIDs.Count > 0 Then
            Dim extras As New HashSet(Of UInteger)(removedHdpt.ExtraPartFormIDs)
            ' Defensive: si otra entrada del preset también declara este extra en su HNAM, NO lo
            ' removemos — sigue siendo HNAM-child de un parent vivo. Caso raro en vanilla pero
            ' barato cubrirlo.
            Dim claimedByOtherParent As New HashSet(Of UInteger)
            For Each otherFid In p.HeadPartFormIDs
                Dim otherHdpt As HDPT_Data = Nothing
                If Not _allHeadPartsByFid.TryGetValue(otherFid, otherHdpt) Then Continue For
                If otherHdpt.ExtraPartFormIDs Is Nothing Then Continue For
                For Each ex In otherHdpt.ExtraPartFormIDs
                    If extras.Contains(ex) Then claimedByOtherParent.Add(ex)
                Next
            Next
            For i = p.HeadPartFormIDs.Count - 1 To 0 Step -1
                Dim fid = p.HeadPartFormIDs(i)
                If Not extras.Contains(fid) Then Continue For
                If claimedByOtherParent.Contains(fid) Then Continue For
                Dim extraHdpt As HDPT_Data = Nothing
                If Not _allHeadPartsByFid.TryGetValue(fid, extraHdpt) Then Continue For
                If extraHdpt.PartType <> HdptTypeMisc Then Continue For
                p.HeadPartFormIDs.RemoveAt(i)
            Next
        End If

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

            ' Select current. Overlay takes priority (explicit user override); if absent (= 0)
            ' fall back to the effective HCLF the renderer would paint with (raw NPC -> Traits
            ' template chain -> RACE.AHCM/AHCF default). Display only — does NOT mutate the
            ' overlay (preserve semantic stays intact until the user actively touches the combo).
            Dim p = Preset
            Dim targetFid = If(p.HairColorFormID <> 0UI,
                               p.HairColorFormID,
                               _mainForm.ResolveEffectiveHairColorFormID(_rootNpcFormID))
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

        ' "(none / preserve)" selected — read the EFFECTIVE color directly from the resolved
        ' render state. ResolveNPCBaseState walks the full chain (NPC.HCLF → TPLT M/A template
        ' → RACE.HCLF default per ApplyRaceFallbacks) once at load time and writes the result
        ' to host.LastRenderedState.HairColorFormID. The swatch is the SAME consumer of the
        ' SAME resolved value the renderer paints with — no parallel chain walk in the editor,
        ' no duplicated semantics that could drift apart.
        If it.FormID = 0UI Then
            Dim effectiveFid As UInteger = 0UI
            If _editorHost IsNot Nothing AndAlso _editorHost.LastRenderedState IsNot Nothing Then
                effectiveFid = _editorHost.LastRenderedState.HairColorFormID
            End If
            If effectiveFid <> 0UI Then
                Dim rec = _pluginManager.GetRecord(effectiveFid)
                If rec IsNot Nothing AndAlso rec.Header.Signature = "CLFM" Then
                    Dim clfm = RecordParsers.ParseCLFM(rec, _pluginManager)
                    If clfm IsNot Nothing Then
                        it = New HairColorItem With {
                            .FormID = effectiveFid,
                            .Display = "",
                            .Color = clfm.Color,
                            .HasColor = clfm.HasColor,
                            .HasRemappingIndex = clfm.HasRemappingIndex,
                            .RemappingIndex = clfm.RemappingIndex
                        }
                    End If
                End If
            End If
        End If

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

    ''' <summary>Decode the hair LUT (HairColor_Lgrad_d.dds or equivalent) once and cache it for
    ''' swatch sampling. Lazy: only attempted on first request, and only once (failures stick — no
    ''' retry storm if the DDS is unreadable).
    ''' <para>Path priority mirrors the renderer (MainForm.ApplyShapeMaterialOverrides /
    ''' RefreshFaceTintLivePreview): the BGSM's own GreyscaleTexture wins (it's per-shape, picked
    ''' by the hair stylist for THIS mesh), falling back to RACE.HNAM/HLTX only if the loaded hair
    ''' shape has no BGSM palette path. This matches engine behaviour (verified against F4SE
    ''' CharGenInterface.cpp: the in-game shader binds the LUT from material TXST slot 3, not from
    ''' the RACE record). Vanilla HumanChildRace ships without HNAM/HLTX precisely because the
    ''' BGSM carries it; without BGSM-first, the swatch shows no preview for child NPCs.</para></summary>
    Private Sub EnsureHairPaletteLoaded()
        If _hairPaletteResolveAttempted Then Return

        ' Single source of truth for the BGSM-first / RACE-fallback rule lives in MainForm so the
        ' renderer and the swatch never disagree. Resolve via the helper, then check the chosen
        ' path actually exists in FilesDictionary before attempting to decode.
        ' Host source: _editorHost is created in this form's Shown handler (post-constructor), but
        ' the first swatch Paint can fire during construction (SeedFromOverlayOrRaw → Invalidate).
        ' Fall back to MainForm._renderHost — same NPC, same state, palette path is identical.
        ' Mirrors the BuildMorphGroupSections pattern documented above at line 303-304.
        Dim host As NpcRenderHost = _editorHost
        If host Is Nothing OrElse host.LastRenderedState Is Nothing Then host = _mainForm?._renderHost
        Dim raw As String = ""
        If _mainForm IsNot Nothing AndAlso host IsNot Nothing AndAlso host.LastRenderedState IsNot Nothing Then
            raw = MainForm.ResolveHairPaletteTexture(host, host.LastRenderedState, _pluginManager)
        End If
        If String.IsNullOrEmpty(raw) Then Return
        Dim chosen = FO4UnifiedMaterial_Class.CorrectTexturePath(raw)
        If chosen = "" OrElse Not FilesDictionary_class.Dictionary.ContainsKey(chosen) Then Return
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
            _hairPaletteResolveAttempted = True
        Catch ex As Exception
            _hairPaletteResolveAttempted = True   ' decode-side failures are not transitory; stop retrying
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

    ''' <summary>Tag payload for ListViewTints rows. OriginalIdx points at p.FaceTintLayers when
    ''' IsRaceDefault=False (mutable NPC override). When IsRaceDefault=True the row was injected
    ''' from the RACE.TintTemplateGroups defaults — OriginalIdx=-1 and VirtualLayer holds the
    ''' synthesized data so the detail panel can describe it but Remove/edit are blocked.
    ''' Mirrors HeadPartRowTag (line 654) so Add/Remove semantics are consistent across both
    ''' RACE-default surfaces.</summary>
    Private Class TintRowTag
        Public OriginalIdx As Integer = -1
        Public IsRaceDefault As Boolean = False
        Public VirtualLayer As NPC_FaceTintLayerData
    End Class

    Private Sub RefreshTintsList()
        ListViewTints.BeginUpdate()
        Try
            ListViewTints.Items.Clear()
            Dim p = Preset
            ' Apply filter (group / layer name / slot, case-insensitive substring). Empty string
            ' disables the filter and shows everything. Filtering happens at row-build time so the
            ' Tag still maps cleanly to the original index in p.FaceTintLayers — selecting a
            ' filtered row goes through the same OnTintSelectionChanged / OnRemoveTint code path.
            Dim filter As String = TextBoxTintFilter.Text.Trim()
            ' Build the merged layer list using the same rule the renderer uses (FaceTintLayerBuilder
            ' .MergeTintLayersWithRaceDefaults). For each TintTemplateGroup the NPC doesn't touch,
            ' every Option whose TTED is present is injected as a virtual default. This mirrors
            ' the engine's CK behaviour and keeps the editor's view 1:1 with what the render draws.
            Dim merged = FaceTintLayerBuilder.MergeTintLayersWithRaceDefaults(p.FaceTintLayers, _race, _isFemale, _pluginManager)
            ' Display in RACE-Group order (the same order the compositor uses), tied-broken by the
            ' layer's original position in the merged list so two layers with the same Index keep
            ' a stable relative order. NPC-authored layers carry their p.FaceTintLayers index in
            ' OriginalIdx so OnTintSelectionChanged / OnRemoveTint can still mutate the underlying
            ' list; race-default rows carry OriginalIdx=-1 so the same paths can refuse to mutate.
            Dim npcOriginalIdxByRef As New Dictionary(Of NPC_FaceTintLayerData, Integer)
            For i = 0 To p.FaceTintLayers.Count - 1
                npcOriginalIdxByRef(p.FaceTintLayers(i)) = i
            Next
            Dim ordered = merged.
                Select(Function(m, mergedIdx)
                           Dim r As Integer = Integer.MaxValue
                           _tintRankByIndex.TryGetValue(m.Layer.Index, r)
                           Dim originalIdx As Integer = -1
                           If Not m.IsRaceDefault Then npcOriginalIdxByRef.TryGetValue(m.Layer, originalIdx)
                           Return New With {.Merged = m, mergedIdx, .Rank = r, originalIdx}
                       End Function).
                OrderBy(Function(x) x.Rank).
                ThenBy(Function(x) x.MergedIdx).
                ToList()
            For Each entry In ordered
                Dim tl = entry.Merged.Layer
                Dim grp = DescribeTintGroup(tl)
                Dim slot = DescribeTintSlot(tl)
                Dim layerName = DescribeTintLayer(tl)
                If entry.Merged.IsRaceDefault Then layerName &= " (RACE)"
                If filter.Length > 0 _
                   AndAlso grp.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 _
                   AndAlso slot.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 _
                   AndAlso layerName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 Then
                    Continue For
                End If
                Dim row As New ListViewItem(grp)
                row.SubItems.Add(slot)
                row.SubItems.Add(layerName)
                row.SubItems.Add(DescribeTintColor(tl))
                row.SubItems.Add(tl.Value.ToString(CultureInfo.InvariantCulture))
                row.Tag = New TintRowTag With {
                    .OriginalIdx = entry.OriginalIdx,
                    .IsRaceDefault = entry.Merged.IsRaceDefault,
                    .VirtualLayer = If(entry.Merged.IsRaceDefault, tl, Nothing)
                }
                If entry.Merged.IsRaceDefault Then row.ForeColor = SystemColors.GrayText
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
        _currentTintIsRaceDefault = False
        _currentTintVirtualLayer = Nothing
        If ListViewTints.SelectedItems.Count = 0 Then
            _currentTintIndex = -1
        Else
            Dim tag = TryCast(ListViewTints.SelectedItems(0).Tag, TintRowTag)
            If tag Is Nothing Then
                _currentTintIndex = -1
            Else
                _currentTintIndex = tag.OriginalIdx
                _currentTintIsRaceDefault = tag.IsRaceDefault
                _currentTintVirtualLayer = tag.VirtualLayer
            End If
        End If
        UpdateTintDetail()
    End Sub

    Private Sub UpdateTintDetail()
        _suspendEvents = True
        Try
            Dim p = Preset
            Dim tl As NPC_FaceTintLayerData = Nothing
            If _currentTintIsRaceDefault Then
                tl = _currentTintVirtualLayer
            ElseIf _currentTintIndex >= 0 AndAlso _currentTintIndex < p.FaceTintLayers.Count Then
                tl = p.FaceTintLayers(_currentTintIndex)
            End If
            If tl Is Nothing Then
                LabelTintLayerName.Text = "(none — select a layer above)"
                ComboBoxTintPalette.Items.Clear()
                ComboBoxTintPalette.Enabled = False
                ButtonTintCustomRGB.Enabled = False
                TrackBarTintPercent.Enabled = False
                PanelTintColorSwatch.BackColor = SystemColors.Control
                Return
            End If
            LabelTintLayerName.Text = DescribeTintLayer(tl) & If(_currentTintIsRaceDefault, " (RACE default — edit to override)", "")

            Dim opt = _race?.FindTintOption(tl.Index, _isFemale)
            Dim isPalette = (opt IsNot Nothing AndAlso opt.EntryType = RACE_TintEntryType.Palette)
            ' Race-default rows ARE editable: the first edit (palette pick / custom RGB / percent
            ' slider) materializes the virtual layer into p.FaceTintLayers as a real NPC override.
            ' From that point on the row goes from gray to black and behaves like any other layer.
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

            ' Force alpha=255: tl.Color can carry alpha=0 (RACE-default seeded from CLFM bytes
            ' whose engine-unused alpha vanilla often leaves at 0; same in LM-loaded layers).
            ' WinForms Panel.BackColor with alpha<255 renders as parent fill — the combo path
            ' already forces opaque at the TintPaletteItem construction (line 1381), which is
            ' why changing the combo lit the swatch but list-selection didn't.
            ' TextureSet and Mask entries: leave swatch blank (no visual preview here).
            PanelTintColorSwatch.BackColor = If(isPalette, Color.FromArgb(255, tl.Color.R, tl.Color.G, tl.Color.B), SystemColors.Control)

            TrackBarTintPercent.Enabled = True
            TrackBarTintPercent.Value = tl.Value
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

    ''' <summary>If the currently selected row is a RACE default (virtual, not in
    ''' p.FaceTintLayers), materialize it as a real NPC override by appending a clone of the
    ''' virtual layer to p.FaceTintLayers and updating selection state to point at the new
    ''' index. After this returns, _currentTintIsRaceDefault=False and _currentTintIndex is
    ''' a valid index into p.FaceTintLayers, so the caller can mutate the layer normally.
    ''' Returns True if a promotion happened (caller should RefreshTintsList because the gray
    ''' row is replaced by a black one). Returns False when the selection is already an NPC
    ''' override or when there's no virtual layer to promote.</summary>
    Private Function PromoteRaceDefaultIfNeeded() As Boolean
        If Not _currentTintIsRaceDefault Then Return False
        If _currentTintVirtualLayer Is Nothing Then Return False
        Dim p = Preset
        Dim cloned = CloneFaceTint(_currentTintVirtualLayer)
        p.FaceTintLayers.Add(cloned)
        _currentTintIndex = p.FaceTintLayers.Count - 1
        _currentTintIsRaceDefault = False
        _currentTintVirtualLayer = Nothing
        Return True
    End Function

    Private Sub OnTintPaletteChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim promoted = PromoteRaceDefaultIfNeeded()
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
        If promoted Then
            RefreshTintsList()
            ReselectNpcLayerByIndex(_currentTintIndex)
        Else
            UpdateTintRowDisplay(_currentTintIndex)
        End If
        _refresh?.Invoke(FaceRefreshScope.TexturesOnly)
    End Sub

    Private Sub OnTintCustomRGB(sender As Object, e As EventArgs)
        ' Source the dialog's seed color from the active layer (real or virtual) WITHOUT
        ' promoting yet — promotion only happens if the user actually picks a colour. Cancel
        ' must leave p.FaceTintLayers untouched.
        Dim seedTl As NPC_FaceTintLayerData = Nothing
        If _currentTintIsRaceDefault Then
            seedTl = _currentTintVirtualLayer
        ElseIf _currentTintIndex >= 0 AndAlso _currentTintIndex < Preset.FaceTintLayers.Count Then
            seedTl = Preset.FaceTintLayers(_currentTintIndex)
        End If
        If seedTl Is Nothing Then Return
        Using dlg As New ColorDialog()
            dlg.AllowFullOpen = True
            dlg.FullOpen = True
            dlg.AnyColor = True
            dlg.Color = If(seedTl.Color.IsEmpty, Color.White, seedTl.Color)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim promoted = PromoteRaceDefaultIfNeeded()
            Dim p = Preset
            If _currentTintIndex < 0 OrElse _currentTintIndex >= p.FaceTintLayers.Count Then Return
            Dim tl = p.FaceTintLayers(_currentTintIndex)
            tl.Color = Color.FromArgb(255, dlg.Color.R, dlg.Color.G, dlg.Color.B)
            ' TemplateColorIndex left as-is — Save (BuildPresetFromState → ResolveTemplateColorIdToAbsolute)
            ' will re-resolve by RGB-match, falling back to opt.TemplateColors[0].TemplateIndex when
            ' the custom RGB isn't in the palette. That's the same fallback LooksMenu in-game uses.
            PanelTintColorSwatch.BackColor = tl.Color
            If promoted Then
                RefreshTintsList()
                ReselectNpcLayerByIndex(_currentTintIndex)
            Else
                UpdateTintRowDisplay(_currentTintIndex)
                UpdateTintDetail()  ' re-pick combo (will land on "Custom RGB" since no CLFM matches)
            End If
            _refresh?.Invoke(FaceRefreshScope.TexturesOnly)
        End Using
    End Sub

    Private Sub OnTintPercentChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim promoted = PromoteRaceDefaultIfNeeded()
        Dim p = Preset
        If _currentTintIndex < 0 OrElse _currentTintIndex >= p.FaceTintLayers.Count Then Return
        Dim tl = p.FaceTintLayers(_currentTintIndex)
        tl.Value = CInt(Math.Round(TrackBarTintPercent.Value))
        If promoted Then
            ' Promote happened: the gray RACE row must be replaced by a real NPC row in the
            ' ListView. Refreshing here means each first-touch of the slider on a virtual row
            ' costs one extra refresh; subsequent moves on the now-NPC row are still light.
            RefreshTintsList()
            ReselectNpcLayerByIndex(_currentTintIndex)
        Else
            UpdateTintRowDisplay(_currentTintIndex)
        End If
        ScheduleRefresh(FaceRefreshScope.TexturesOnly)
    End Sub

    ''' <summary>Re-select the ListView row whose Tag points at the given p.FaceTintLayers
    ''' index. Used after RefreshTintsList rebuilt the list following a promote, so the user's
    ''' selection follows the row they were editing instead of falling back to no-selection.</summary>
    Private Sub ReselectNpcLayerByIndex(idx As Integer)
        For Each item As ListViewItem In ListViewTints.Items
            Dim tag = TryCast(item.Tag, TintRowTag)
            If tag IsNot Nothing AndAlso Not tag.IsRaceDefault AndAlso tag.OriginalIdx = idx Then
                item.Selected = True
                item.EnsureVisible()
                Exit For
            End If
        Next
    End Sub

    Private Sub UpdateTintRowDisplay(idx As Integer)
        ' idx is the index into p.FaceTintLayers (the underlying mutable list). The ListView
        ' rows are sorted by RACE-Group rank, so we have to search by Tag rather than indexing.
        Dim p = Preset
        If idx < 0 OrElse idx >= p.FaceTintLayers.Count Then Return
        Dim tl = p.FaceTintLayers(idx)
        Dim row As ListViewItem = Nothing
        For Each item As ListViewItem In ListViewTints.Items
            Dim tag = TryCast(item.Tag, TintRowTag)
            If tag IsNot Nothing AndAlso Not tag.IsRaceDefault AndAlso tag.OriginalIdx = idx Then
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
        ' filters out Mask-typed options (region masks, not paintable colour layers) AND any
        ' option Index already present in the active layer list — so the user only sees
        ' additions still available to add. Vanilla NPCs carry one layer per option Index;
        ' duplicates would over-saturate the compositor with no way to disambiguate in the
        ' detail panel. Pre-filtering at the picker is cleaner than a post-Add MessageBox.
        If _race Is Nothing Then Return
        Dim groups = If(_isFemale, _race.FemaleTintTemplateGroups, _race.MaleTintTemplateGroups)
        If groups Is Nothing OrElse groups.Count = 0 Then Return

        Dim p = Preset
        Dim alreadyPresent As New HashSet(Of UShort)(p.FaceTintLayers.Select(Function(tl) tl.Index))

        Using dlg As New TintPickerDialog(groups, alreadyPresent)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim opt = dlg.SelectedOption
            If opt Is Nothing Then Return

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
                Dim tag = TryCast(item.Tag, TintRowTag)
                If tag IsNot Nothing AndAlso Not tag.IsRaceDefault AndAlso tag.OriginalIdx = newLayerIdx Then
                    item.Selected = True
                    item.EnsureVisible()
                    Exit For
                End If
            Next
            _refresh?.Invoke(FaceRefreshScope.TexturesOnly)
        End Using
    End Sub

    Private Sub OnRemoveTint(sender As Object, e As EventArgs)
        ' Race-default rows are read-only; the user must Add the option to materialize it
        ' as an NPC override before it can be removed. Mirrors HeadParts (line 851).
        If _currentTintIsRaceDefault Then Return
        Dim p = Preset
        If _currentTintIndex < 0 OrElse _currentTintIndex >= p.FaceTintLayers.Count Then Return
        Dim selectedRowIdx As Integer = If(ListViewTints.SelectedIndices.Count > 0, ListViewTints.SelectedIndices(0), -1)
        p.FaceTintLayers.RemoveAt(_currentTintIndex)
        _currentTintIndex = -1
        RefreshTintsList()
        SelectNeighborTintRow(selectedRowIdx)
        _refresh?.Invoke(FaceRefreshScope.TexturesOnly)
    End Sub

    ''' <summary>Remove every NPC-override layer whose TintTemplateGroup matches the group of
    ''' the currently selected row. The group is resolved via <see cref="_tintGroupByIndex"/>
    ''' (built from RACE) and works for both NPC-override and RACE-default selections — the
    ''' selection just tells us which category to drop. RACE-default rows themselves are not
    ''' stored in p.FaceTintLayers so they're untouched by this; they'll simply re-appear at the
    ''' top of their group after the merge if all overrides for that group are gone.</summary>
    Private Sub OnRemoveAllInCategory(sender As Object, e As EventArgs)
        Dim p = Preset
        Dim selectedTintIndex As UShort = 0
        Dim hasSelection As Boolean = False
        If _currentTintIsRaceDefault AndAlso _currentTintVirtualLayer IsNot Nothing Then
            selectedTintIndex = _currentTintVirtualLayer.Index
            hasSelection = True
        ElseIf _currentTintIndex >= 0 AndAlso _currentTintIndex < p.FaceTintLayers.Count Then
            selectedTintIndex = p.FaceTintLayers(_currentTintIndex).Index
            hasSelection = True
        End If
        If Not hasSelection Then Return

        Dim groupName As String = Nothing
        If Not _tintGroupByIndex.TryGetValue(selectedTintIndex, groupName) Then Return

        Dim selectedRowIdx As Integer = If(ListViewTints.SelectedIndices.Count > 0, ListViewTints.SelectedIndices(0), -1)
        Dim removed As Integer = p.FaceTintLayers.RemoveAll(
            Function(tl)
                Dim g As String = Nothing
                _tintGroupByIndex.TryGetValue(tl.Index, g)
                Return String.Equals(g, groupName, StringComparison.Ordinal)
            End Function)
        If removed = 0 Then Return
        _currentTintIndex = -1
        RefreshTintsList()
        SelectNeighborTintRow(selectedRowIdx)
        _refresh?.Invoke(FaceRefreshScope.TexturesOnly)
    End Sub

    ''' <summary>After a Remove (single or category) rebuilt the ListView, place the selection
    ''' on the row at the same visual position as before — clamped to the new row count, no-op
    ''' if the list is now empty. Lets the user keep tabbing through layers without re-clicking.</summary>
    Private Sub SelectNeighborTintRow(originalRowIdx As Integer)
        If ListViewTints.Items.Count = 0 OrElse originalRowIdx < 0 Then Return
        Dim newIdx As Integer = Math.Min(originalRowIdx, ListViewTints.Items.Count - 1)
        If newIdx < 0 Then Return
        ListViewTints.Items(newIdx).Selected = True
        ListViewTints.Items(newIdx).EnsureVisible()
    End Sub

    ''' <summary>Drop every tint layer with Value &lt;= 0. Save LM already filters these at
    ''' write time (LooksmenuLoader.vb:502) so they never round-trip; this lets the user trim
    ''' them ahead of time so the editor list is uncluttered. Idempotent — running twice does
    ''' nothing the second time.</summary>
    Private Sub OnRemoveZeroedTints(sender As Object, e As EventArgs)
        Dim p = Preset
        Dim selectedRowIdx As Integer = If(ListViewTints.SelectedIndices.Count > 0, ListViewTints.SelectedIndices(0), -1)
        Dim removed As Integer = p.FaceTintLayers.RemoveAll(Function(tl) tl.Value <= 0)
        If removed = 0 Then Return
        _currentTintIndex = -1
        RefreshTintsList()
        SelectNeighborTintRow(selectedRowIdx)
        _refresh?.Invoke(FaceRefreshScope.TexturesOnly)
    End Sub

    Private Sub OnTintFilterChanged(sender As Object, e As EventArgs)
        ' Re-render the list with the new substring filter. _currentTintIndex still points at
        ' the model layer, but the row may no longer be in the visible set — guard
        ' OnTintSelectionChanged so it tolerates a missing row.
        RefreshTintsList()
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
            For Each s In _groupSections
                s.PresetListBox = Nothing
                s.PresetIntensityBar = Nothing
            Next
            If _groupSections.Count = 0 Then
                Dim empty As New Label() With {
                    .Text = "RACE record declares no vertex morphs (MorphValues / MorphPresets / MorphGroups).",
                    .AutoSize = True, .ForeColor = Color.Gray, .Padding = New Padding(8)}
                VertexMorphsPanel.Controls.Add(empty)
                Return
            End If

            ' Owner-draw the tab headers so we can colour the "Other" tab text red — the tab
            ' control's default rendering ignores TabPage.ForeColor.
            Dim tabs As New TabControl With {
                .Dock = DockStyle.Fill,
                .DrawMode = TabDrawMode.OwnerDrawFixed
            }
            AddHandler tabs.DrawItem, AddressOf OnVertexTabDrawItem
            For Each section In _groupSections
                Dim isOther = (section.Presets Is Nothing)
                Dim title = If(isOther, "⚠ " & section.GroupName, section.GroupName)
                Dim page As New TabPage(title) With {
                    .AutoScroll = True, .Padding = New Padding(6),
                    .Tag = section ' carry section ref for the owner-draw handler
                }
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

        Dim bar As New FO4_Base_Library.TinySliderTextBox() With {
            .Minimum = 0R, .Maximum = 1R,
            .DisplayFormat = "0.00%", .InputScale = 0.01R,
            .SmallChange = 0.01R, .LargeChange = 0.1R,
            .Dock = DockStyle.Top, .Height = 28, .Margin = New Padding(0, 4, 0, 0)}
        AddHandler bar.ValueChanged, Sub(s, e) OnPresetIntensityChanged(section)
        AddHandler bar.DragEnded, AddressOf OnSliderDragEnded
        block.Controls.Add(bar, 0, 1)

        section.PresetListBox = list
        section.PresetIntensityBar = bar
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
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        row.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        row.RowStyles.Add(New RowStyle(SizeType.AutoSize))

        ' Title row: min-name on the left, max-name on the right, both flush to the slider edges
        ' below.
        Dim lblMin As New Label() With {.Text = minName, .AutoSize = False,
            .TextAlign = ContentAlignment.MiddleLeft, .Dock = DockStyle.Fill,
            .Margin = New Padding(0)}
        Dim lblMax As New Label() With {.Text = maxName, .AutoSize = False,
            .TextAlign = ContentAlignment.MiddleRight, .Dock = DockStyle.Fill,
            .Margin = New Padding(0)}
        row.Controls.Add(lblMin, 0, 0)
        row.Controls.Add(lblMax, 1, 0)

        Dim bar As New FO4_Base_Library.TinySliderTextBox() With {
            .Minimum = -1R, .Maximum = 1R,
            .DisplayFormat = "0.00%", .InputScale = 0.01R,
            .SmallChange = 0.01R, .LargeChange = 0.1R,
            .FillMode = FO4_Base_Library.TinySliderFillMode.Center,
            .Value = 0R,
            .Height = 28,
            .Dock = DockStyle.Fill, .Margin = New Padding(0)}
        Dim capturedIdx = key
        AddHandler bar.ValueChanged, Sub(s, e) OnBidiSliderChanged(capturedIdx)
        AddHandler bar.DragEnded, AddressOf OnSliderDragEnded

        row.SetColumnSpan(bar, 2)
        row.Controls.Add(bar, 0, 1)

        _bidiBars(key) = bar
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
                    section.PresetIntensityBar.Value = activeWeight
                End If
                For Each k In section.BidiKeys
                    Dim w As Single = 0
                    p.ChargenFaceMorphs.TryGetValue(k, w)
                    Dim bar As FO4_Base_Library.TinySliderTextBox = Nothing
                    If _bidiBars.TryGetValue(k, bar) Then
                        bar.Value = w
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
        Dim bar As FO4_Base_Library.TinySliderTextBox = Nothing
        If Not _bidiBars.TryGetValue(key, bar) Then Return
        Dim v As Single = CSng(bar.Value)
        Dim p = Preset
        If Math.Abs(v) < 0.001F Then
            p.ChargenFaceMorphs.Remove(key)
        Else
            p.ChargenFaceMorphs(key) = v
        End If
        ScheduleRefresh(FaceRefreshScope.Morphs)
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
            Dim weight As Single = CSng(section.PresetIntensityBar.Value)
            If weight < 0.001F Then weight = 1.0F  ' default to full intensity on first selection
            p.ChargenFaceMorphs(sel.Index) = weight
            _suspendEvents = True
            Try
                section.PresetIntensityBar.Value = weight
            Finally
                _suspendEvents = False
            End Try
        Else
            _suspendEvents = True
            Try
                section.PresetIntensityBar.Value = 0R
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
        Dim v As Single = CSng(section.PresetIntensityBar.Value)
        Dim p = Preset
        If Math.Abs(v) < 0.001F Then
            p.ChargenFaceMorphs.Remove(sel.Index)
        Else
            p.ChargenFaceMorphs(sel.Index) = v
        End If
        ScheduleRefresh(FaceRefreshScope.Morphs)
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
            .Width = 270, .Height = 265,
            .Margin = New Padding(4),
            .Padding = New Padding(6)}
        Dim tip As New ToolTip()
        tip.SetToolTip(group, $"FMRI Index: 0x{rd.ID:X8}")

        Dim bars(6) As FO4_Base_Library.TinySliderTextBox

        Dim layout As New TableLayoutPanel() With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 10}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 24))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        For r = 0 To 9
            layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Next

        Dim addHeader = Sub(text As String, row As Integer)
                            Dim h As New Label() With {.Text = text, .AutoSize = False,
                                .Font = New Font(Font, FontStyle.Bold),
                                .Dock = DockStyle.Fill,
                                .TextAlign = ContentAlignment.MiddleCenter}
                            layout.SetColumnSpan(h, 2)
                            layout.Controls.Add(h, 0, row)
                        End Sub

        Dim addAxisRow = Sub(componentIdx As Integer, axisLabel As String, row As Integer)
                             Dim resetBtn As New Button() With {.Text = axisLabel, .Width = 22, .Height = RowHeight,
                                 .Margin = New Padding(0, 0, 2, 0), .TabStop = False}
                             ' FMRS values are signed [-1..+1] with 0 = bind pose (default).
                             ' LerpFmrs (MainForm.vb:5447) maps -1 → minima, 0 → no delta,
                             ' +1 → maxima. NPC values in vanilla land between -1 and +1 directly,
                             ' not 0..1 lerped around 0.5. Slider must mirror that exactly.
                             Dim bar As New FO4_Base_Library.TinySliderTextBox() With {.Minimum = -1R, .Maximum = 1R,
                                 .DisplayFormat = "0.00%", .InputScale = 0.01R,
                                 .SmallChange = 0.01R, .LargeChange = 0.1R,
                                 .FillMode = FO4_Base_Library.TinySliderFillMode.Center,
                                 .Height = RowHeight, .Value = 0R,
                                 .Dock = DockStyle.Fill, .Margin = New Padding(0)}
                             Dim regId = rd.ID
                             Dim compIdx = componentIdx
                             AddHandler bar.ValueChanged, Sub(s, e) OnRegionSliderChanged(regId, compIdx)
                             AddHandler bar.DragEnded, AddressOf OnSliderDragEnded
                             AddHandler resetBtn.Click, Sub(s, e)
                                                            bar.Value = 0
                                                        End Sub
                             layout.Controls.Add(resetBtn, 0, row)
                             layout.Controls.Add(bar, 1, row)
                             bars(componentIdx) = bar
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
                Dim arr As Single() = Nothing
                p.FaceBoneRegions.TryGetValue(regId, arr)
                For i = 0 To 6
                    Dim v As Single = If(arr IsNot Nothing AndAlso i < arr.Length, arr(i), 0.0F)
                    bars(i).Value = v
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
        Dim v As Single = CSng(bars(componentIdx).Value)

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
        ScheduleRefresh(FaceRefreshScope.Pose)
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
        Dim v As Single = CSng(TrackBarFmin.Value)
        Preset.FacialMorphIntensity = v
        ScheduleRefresh(FaceRefreshScope.Pose)
    End Sub

    ''' <summary>Mark a scope as pending and start the throttle timer. Multiple distinct scopes
    ''' across slider events accumulate; FlushRefresh emits one _refresh invocation per scope.</summary>
    Private Sub ScheduleRefresh(scope As FaceRefreshScope)
        _pendingScopes.Add(scope)
        If Not _refreshTimer.Enabled Then _refreshTimer.Start()
    End Sub

    ''' <summary>Force-flush every pending scope immediately. Bound to slider DragEnded so
    ''' releasing the mouse always shows the final preview without waiting for the timer.</summary>
    Private Sub FlushRefresh()
        If _pendingScopes.Count > 0 Then
            ' Snapshot then clear before invoking — _refresh callbacks may take a while and we
            ' don't want a re-entrant ScheduleRefresh during invoke to be lost or double-fired.
            Dim scopes = _pendingScopes.ToList()
            _pendingScopes.Clear()
            For Each s In scopes
                _refresh?.Invoke(s)
            Next
        End If
        _refreshTimer.Stop()
    End Sub

    Private Sub RefreshTimer_Tick(sender As Object, e As EventArgs) Handles _refreshTimer.Tick
        FlushRefresh()
    End Sub

    Private Sub OnSliderDragEnded(sender As Object, e As EventArgs)
        FlushRefresh()
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
            TrackBarFmin.Value = p.FacialMorphIntensity
        Finally
            _suspendEvents = False
        End Try
        LoadBoneRegionValues()
        _refresh?.Invoke(FaceRefreshScope.Pose)
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' Live overlay edits already mutated _appliedPresets[npc]; flag the MainForm to recompose
        ' its main preview from the now-final overlay state.
        HasUncommittedChanges = True
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
        ' Phase D: the MainForm's preview never reflected our intermediate edits (we render into
        ' the editor's own embedded host), so on Cancel there's nothing to repaint there. The
        ' overlay rollback above is enough; HasUncommittedChanges stays False and ButtonEditFace
        ' caller skips the post-modal MainForm reload.
        HasUncommittedChanges = False
        DialogResult = DialogResult.Cancel
        Close()
    End Sub

    ''' <summary>Refresh dispatcher that targets the editor's embedded NpcRenderHost. The form
    ''' classifies each edit by <see cref="FaceRefreshScope"/>; we translate that into the
    ''' cheapest MarkDirty pass that still produces a correct preview. The MainForm's preview
    ''' is left untouched during the modal session.</summary>
    Private Async Sub OnLocalFaceRefresh(scope As FaceRefreshScope)
        If _editorHost Is Nothing OrElse _mainForm Is Nothing Then Return
        Select Case scope
            Case FaceRefreshScope.FlagOnly
                ' No render side-effect.
                Return
            Case FaceRefreshScope.TexturesOnly
                ' Live tint / hair-color edit on the editor's own preview.
                Try
                    _mainForm.RefreshFaceTintLivePreview(_editorHost)
                    _editorHost.PreviewCtl.RefreshRender()
                Catch ex As Exception
                End Try
                Return
            Case FaceRefreshScope.Morphs
                If _editorHost.LastRenderData IsNot Nothing Then
                    Dim intent = _editorHost.PreviewCtl.Intent
                    intent.MorphResolver = _mainForm.BuildCompositeMorphResolver(_editorHost.LastRenderedState, _editorHost.LastRenderData, _editorHost)
                    intent.MarkDirty(RenderDirtyFlags.Morphs, _editorHost.LastRenderData.Shapes)
                End If
                ' MPPI Morph Group presets like Murphy's "Arrugado" do TWO things: vertex
                ' deformation (MSDV — handled by the resolver above) AND a per-region MPPT TXST
                ' texture swap (Wrinkled skin texture inside the Forehead/Cheeks/Neck region
                ' mask, applied by BuildFaceRegionSwaps → ApplyRegionSwapChannelOnto inside
                ' TryApplyFaceTints). Re-running the resolver alone updates geometry but leaves
                ' the textures stale — switching from Smooth to Wrinkled would deform the mesh
                ' but show the previous texture. Refresh the tint pipeline too. No-op for NPCs
                ' whose active presets carry no MPPT (BuildFaceRegionSwaps returns 0 swaps).
                Try
                    _mainForm.RefreshFaceTintLivePreview(_editorHost)
                Catch ex As Exception
                    Logger.LogLazy(Function() $"[EDIT-FACE] tint refresh failed for NPC 0x{_rootNpcFormID:X8}: {ex.GetType().Name}: {ex.Message}")
                End Try
                _editorHost.PreviewCtl.InvalidateRender()
                Return
            Case FaceRefreshScope.Pose
                _mainForm.RebuildAndApplyMergedPose(_editorHost)
                If _editorHost.LastRenderData IsNot Nothing Then
                    _editorHost.PreviewCtl.Intent.MarkDirty(RenderDirtyFlags.Pose, _editorHost.LastRenderData.Shapes)
                End If
                _editorHost.PreviewCtl.InvalidateRender()
                Return
            Case FaceRefreshScope.FullReload
                Try
                    Await _mainForm.RenderInHostAsync(_editorHost, _rootNpcFormID)
                Catch ex As Exception
                    Logger.LogLazy(Function() $"[EDIT-FACE] full reload failed for NPC 0x{_rootNpcFormID:X8}: {ex.GetType().Name}: {ex.Message}")
                End Try
                Return
        End Select
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        ' Flush any in-flight throttled refresh so closing doesn't leave a deferred render
        ' hanging, then stop the timer so its tick doesn't fire on a disposed form.
        FlushRefresh()
        _refreshTimer.Stop()
        _refreshTimer.Dispose()
        MyBase.OnFormClosed(e)
    End Sub

    ' =====================================================================
    ' Embedded preview lifecycle (Shown / FormClosing)
    '
    ' Pattern adopted from Wardrobe_Manager Editor_Form.vb:1046 and
    ' CreatefromNif_Form.vb:36 — the PreviewControl is created in Shown (NOT in
    ' .ctor / Designer) so its OpenGL context is created when the form is actually
    ' visible. FormClosing tears it down explicitly so the GL resources are released
    ' before the form's own Dispose runs.
    '
    ' Multiple PreviewControl instances coexist with the MainForm's preview at runtime:
    ' each control owns its own GL context and shaders (see Render.vb:677 OnLoad),
    ' textures and buffers are not shared. WM ships this pattern in production with
    ' Editor_Form + CreatefromNif_Form so it is a known-good baseline.
    ' =====================================================================
    Private WithEvents EditPreviewControl As PreviewControl = Nothing

    Private Async Sub EditFaceForm_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        ' Defer GL context creation until the form is on screen. Building it here means an
        ' exception inside OnLoad surfaces in-context (the form is already alive) instead of
        ' aborting construction.
        EditPreviewControl = New PreviewControl() With {.Dock = DockStyle.Fill}
        PreviewHostPanel.Controls.Add(EditPreviewControl)
        ' Force handle creation (and therefore PreviewControl.OnLoad — shader/program/default
        ' textures init) synchronously BEFORE we await the first render. Without this, the
        ' Await Task.Run inside RenderInHostAsync resumes before HandleCreated has fired, the
        ' pipeline calls RenderShapes against a control whose SharedActiveShader is still
        ' Nothing, and we get GL_INVALID_VALUE on the program handle plus VAO bind errors.
        EditPreviewControl.CreateControl()

        ' Seed the per-editor gore checkbox from MainForm so the embedded preview opens with
        ' the user's global gore preference. _seedingToggles short-circuits the CheckedChanged
        ' handler during the assignment so we don't run a redundant visibility pass before the
        ' first render.
        _seedingToggles = True
        Try
            CheckBoxRenderGore.Checked = _mainForm.CheckBoxRenderGore.Checked
        Finally
            _seedingToggles = False
        End Try

        ' Phase D — own host + initial render. AppliedPresets shares the dict by reference with
        ' MainForm so live overlay edits inside the modal write through to the same source
        ' MainForm will resolve from after OK.
        _editorHost = New NpcRenderHost(EditPreviewControl) With {
            .AppliedPresets = _appliedPresets
        }
        ' Toggles baseline = OnlyFace (everything ON, gore overwritten below from the editor's
        ' own checkbox). RenderToggles.OnlyFace is now a no-op for visibility (the head-only
        ' filter happens at OnlyFaceCollect below) — see RenderToggles.vb.
        Dim t = RenderToggles.OnlyFace(False)
        t.RenderGore = CheckBoxRenderGore.Checked
        _editorHost.Toggles = t
        ' Match MainForm's "Only Face" PreviewMode at the COLLECT path: skin + outfit meshes
        ' are skipped entirely so the editor preview shows just the head.
        _editorHost.OnlyFaceCollect = True
        ' Face tint deferral is now handled by the library's PostTextureUploadAction hook on
        ' RenderIntent — wired by RenderCurrentStateAsync inside the render dispatch path so
        ' editor hosts get the same generic post-texture sequencing the MainForm uses.

        If _mainForm IsNot Nothing Then
            Try
                Await _mainForm.RenderInHostAsync(_editorHost, _rootNpcFormID)
            Catch ex As Exception
                Logger.LogLazy(Function() $"[EDIT-FACE] initial render failed for NPC 0x{_rootNpcFormID:X8}: {ex.GetType().Name}: {ex.Message}")
            End Try
        End If
    End Sub

    Private Sub EditFaceForm_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        ' Quiesce the render loop FIRST so paints queued by the safety-repaint heartbeat
        ' cannot drain while the host disposes GL caches (TintGpuCache, etc.). Without
        ' this, an OnPaint between _editorHost.Dispose() and EditPreviewControl.Clean()
        ' draws against shaders/textures the host has already deleted from the shared
        ' GL context, surfacing as "Program handle does not refer to..." errors.
        If EditPreviewControl IsNot Nothing AndAlso Not EditPreviewControl.IsDisposed Then
            Try
                EditPreviewControl.BeginTeardown()
            Catch
            End Try
        End If

        ' Tear down the editor's host BEFORE the PreviewControl so the host's Dispose can drop
        ' refs to last render artifacts while the GL context (still alive) can still reclaim
        ' GPU caches via the TintGpuCache.Clear path.
        If _editorHost IsNot Nothing Then
            Try
                _editorHost.Dispose()
            Catch
                ' Defensive — host disposal walks dictionaries / nullable refs; swallow so the
                ' form still closes if anything throws.
            End Try
            _editorHost = Nothing
        End If
        ' Guard against double-Close / FormClosing firing twice: the WM pattern checks
        ' IsDisposed before touching the GL handle so we mirror it exactly.
        If EditPreviewControl IsNot Nothing AndAlso Not EditPreviewControl.IsDisposed Then
            Try
                EditPreviewControl.Clean()
            Catch
                ' Defensive — Clean() walks GL state; swallow so the form still closes if a
                ' GL teardown corner-case throws.
            End Try
            Try
                EditPreviewControl.Dispose()
            Catch
            End Try
        End If
        EditPreviewControl = Nothing
    End Sub

    ' =====================================================================
    ' Helpers
    ' =====================================================================

    Private Shared Function ClampInt(v As Integer, lo As Integer, hi As Integer) As Integer
        If v < lo Then Return lo
        If v > hi Then Return hi
        Return v
    End Function

    ''' <summary>Render-gore checkbox toggle. Mutates the editor host's Toggles in place
    ''' (the only visibility flag the EditFace surface exposes — head meshes don't have
    ''' Underarmor/Armor/Headwear categories) and runs the standard visibility pass.</summary>
    Private Sub OnRenderGoreChanged(sender As Object, e As EventArgs) Handles CheckBoxRenderGore.CheckedChanged
        If _seedingToggles Then Return
        If _editorHost Is Nothing Then Return
        _editorHost.Toggles.RenderGore = CheckBoxRenderGore.Checked
        _editorHost.ApplyRenderToggleVisibility()
    End Sub

    Private Sub EditFace_Form_Load(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class
