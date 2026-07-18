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

    ' SSE (Skyrim) face editing is RaceMenu-based (NAM9/NAMA sliders + sculpt + overlays), not LooksMenu.
    ' When _isSSE, the FO4-only tabs (Vertex Morphs, Bone Regions) are hidden and their FO4 cache/seed builds
    ' are skipped; the SSE Morphs tab (built in code) drives the NAM9/NAMA morph sliders instead. Everything
    ' game-gated so the FO4 path is byte-identical.
    Private ReadOnly _isSSE As Boolean = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
    Private _sseNam9 As Single() = New Single(SseNam9MorphMap.Nam9SliderCount - 1) {}
    Private _sseNama As UInteger() = New UInteger(SseNam9MorphMap.NamaFamilyCount - 1) {}
    Private _sseNam9Sliders As FO4_Base_Library.TinySliderTextBox() = New FO4_Base_Library.TinySliderTextBox(SseNam9MorphMap.Nam9SliderCount - 1) {}
    Private _sseNamaCombos As System.Windows.Forms.ComboBox() = New System.Windows.Forms.ComboBox(SseNam9MorphMap.NamaFamilyCount - 1) {}
    ' RaceMenu NiOverride CUSTOM morphs (arbitrary named morphs from a preset/mod). Value sliders rebuilt from
    ' Preset.SseCustomMorphs; the render applies them by name via the chargen TriHead (NpcMorphResolver).
    ' RaceMenu tab controls, keyed by slider name (TinySliderTextBox for Slider / ComboBox for Preset).
    Private _sseRaceMenuControls As New Dictionary(Of String, Control)(StringComparer.OrdinalIgnoreCase)
    ''' <summary>Label showing the effective face TextureSet (NPC_.FTST) on the SSE Face Parts tab.</summary>
    Private _sseHeadTexLabel As Label = Nothing
    ' Face overlays (RaceMenu "Face [Ovl{n}]" face-paint) editor controls. The overlays live in Preset.SseBodyOverlays
    ' (the whole .jslot "overrides" array); this tab filters to the Face nodes. Body/Hands/Feet ones stay in Edit Body.
    Private _sseFaceOvList As ListBox = Nothing
    Private _sseFaceOvCatalog As ListBox = Nothing
    Private _sseFaceOvFilter As TextBox = Nothing
    Private ReadOnly _sseFacePaintShown As New List(Of FO4_Base_Library.RaceMenuPaintCatalog.Entry)
    Private _sseFaceOvDiffuse As TextBox = Nothing
    Private _sseFaceOvNormal As TextBox = Nothing
    Private _sseFaceOvTintEnable As CheckBox = Nothing
    Private _sseFaceOvTintColor As Button = Nothing
    Private _sseFaceOvTintAlpha As FO4_Base_Library.TinySliderTextBox = Nothing

    ''' <summary>SSE face-morph CK categories (matches the Creation Kit Character Gen grouping the user referenced):
    ''' each groups the NAM9 slider indices + NAMA type-family indices for a facial feature. Covers all 18 sliders
    ''' (0-17) and all 4 families (0-3). FO4 has its own face pipeline; this is SSE-only.</summary>
    Private Shared ReadOnly _sseMorphCategories As (Name As String, Sliders As Integer(), Families As Integer())() = {
        ("Nose", New Integer() {0, 1}, New Integer() {0}),
        ("Jaw", New Integer() {2, 3, 4}, New Integer() {}),
        ("Cheeks", New Integer() {5, 6}, New Integer() {}),
        ("Eyes", New Integer() {7, 8, 17}, New Integer() {2}),
        ("Brow", New Integer() {9, 10, 11}, New Integer() {1}),
        ("Mouth", New Integer() {12, 13}, New Integer() {3}),
        ("Chin", New Integer() {14, 15, 16}, New Integer() {})}
    ' SSE face-tint editing: one entry per authored tint layer (TINI/TINC/TINV/TIAS).
    ' INVARIANT (engine-faithful, measured against vanilla Skyrim.esm): R/G/B (TINC) is ALWAYS the effective colour
    ' the engine composes (render + bake + QNAM all read TINC directly; TIAS is never consulted for colour). Tias is
    ' the CK editor's preset selector kept CONSISTENT with the colour: Tias = a RACE preset's TIRS ⇒ TINC == that
    ' preset's CLFM colour; Tias = -1 ⇒ custom RGB. Vanilla proof: TIAS≥0 ⟹ TINC==preset.CLFM (1673/1673), and a
    ' custom colour stays -1 even when it coincides with a preset (284 vanilla layers) — so we NEVER re-match a
    ' custom colour to a preset index (that is the FO4 rule; the SSE CK does not do it).
    Private Structure SseTintEdit
        Public Index As Integer
        Public R As Byte, G As Byte, B As Byte, A As Byte
        Public V As Double        ' TINV/100 (coverage 0..1)
        Public Tias As Short      ' preset selector: a RACE preset's TIRS (≥0) or -1 = custom RGB
        Public Authored As Boolean ' True = the NPC authors this layer (emit TINI/TINC/TINV/TIAS); False = RACE default only
        Public MaskName As String  ' TINT mask filename, for the UI label
        Public MaskPathOverride As String ' RaceMenu-only custom mask texture path (Nothing/empty = use the RACE layer's own mask)
        Public MaskPath As String  ' effective full mask path (override if set, else the RACE layer's TINT) — for the missing-texture check
        Public Customized As Boolean ' came from a preset/NPC AND differs from the RACE default (colour, coverage, or a custom mask) — UI highlight
        Public MaskType As Integer  ' RACE TINP (mask type; 6 = SkinTone). Diagnostic/label only.
        Public DefaultClfm As UInteger ' RACE TIND — colour of the layer's default preset (for "reset to RACE default")
        Public DefaultValue As Double  ' RACE default coverage (for "reset to RACE default")
        Public Presets As List(Of SseFaceTintComposer.SseTintPreset) ' the RACE dropdown swatches for this layer (may be empty)
    End Structure
    Private _sseTintLayers As New List(Of SseTintEdit)
    Private ReadOnly _sseTintToolTip As New ToolTip()
    ' SSE Tints tab = master-detail (mirrors the FO4 tint tab): a list of the RACE's layers on the left, a detail
    ' panel on the right (preset dropdown + custom colour + coverage + RaceMenu warpaint mask + reset).
    Private _sseTintList As ListBox = Nothing
    Private _sseTintSelIndex As Integer = -1
    Private _sseTintPresetCombo As ComboBox = Nothing
    Private _sseTintSwatch As Button = Nothing
    Private _sseTintCustomBtn As Button = Nothing
    Private _sseTintCoverage As FO4_Base_Library.TinySliderTextBox = Nothing
    Private _sseTintMaskLabel As Label = Nothing
    Private _sseTintMaskPick As Button = Nothing
    Private _sseTintMaskClear As Button = Nothing
    Private _sseTintResetBtn As Button = Nothing
    Private _sseTintDetailHost As Panel = Nothing
    ' Combo item for the preset dropdown: Tirs = -1 → "(custom RGB)", else a RACE preset (colour resolved for the label).
    Private NotInheritable Class SseTintPresetItem
        Public Tirs As Integer
        Public Display As String
        Public Swatch As System.Drawing.Color
        Public Overrides Function ToString() As String
            Return Display
        End Function
    End Class

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
        ' Fold LooksMenu custom tint templates into the race so the tint list shows (and can edit) any
        ' mod-added tints the NPC applies, same as the render/bake path. Idempotent no-op without them.
        LmCustomTintLoader.EnsureMerged(_race, _pluginManager)
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
        ' Todas las tabs viven en el Designer; aca solo se MUESTRAN/OCULTAN por juego (removiendo del TabControl)
        ' y se llena el contenido data-driven. SSE: tabs "Morphs (SSE)" + "Tints (SSE)"; se ocultan las FO4-only
        ' (Face Tints FO4, Vertex Morphs LM, Bone Regions). FO4: se ocultan las SSE.
        If _isSSE Then
            PopulateSseMorphTab()
            PopulateSseTintTab()
            PopulateSseSculptTab()  ' read-only list of RaceMenu per-shape sculpt blocks (head/brows/eyes/mouth)
            BuildSseRaceMenuTab()   ' RaceMenu EXTENDED sliders (per-race .slider catalog) — separate from vanilla NAM9/NAMA
            BuildSseFaceOverlaysTab()   ' RaceMenu "Face [Ovl]" face-paint overlays
            BuildSseHeadTextureSection()   ' vanilla NPC_.FTST — RaceMenu applies it too (PresetInterface.cpp:160)
            ' Tab names carry the SYSTEM, not the game: unprefixed tabs are vanilla data written to the NPC_
            ' record in the ESP; "RaceMenu ·" tabs are skee64 co-save data (.jslot + sidecar) that needs
            ' RaceMenu installed to show in-game. "(SSE)" told the user nothing — every tab here is SSE.
            TabPageSseMorphs.Text = "Face Morphs"
            TabPageSseTints.Text = "Tints"
            TabPageSseSculpt.Text = "RaceMenu · Sculpt"
            If TabsFace.TabPages.Contains(TabPageTints) Then TabsFace.TabPages.Remove(TabPageTints)
            If TabsFace.TabPages.Contains(TabPageVertex) Then TabsFace.TabPages.Remove(TabPageVertex)
            If TabsFace.TabPages.Contains(TabPageBoneRegions) Then TabsFace.TabPages.Remove(TabPageBoneRegions)
            ' Meatcaps(7)/Teeth(8)/Head Rear(9) are FO4-only HDPT types — the Skyrim HDPT enum stops at
            ' Eyebrows(6). Hide their Add buttons on SSE so the Face Parts tab only offers valid part types.
            ButtonAddMeatcaps.Visible = False
            ButtonAddTeeth.Visible = False
            ButtonAddHeadRear.Visible = False
        Else
            If TabsFace.TabPages.Contains(TabPageSseMorphs) Then TabsFace.TabPages.Remove(TabPageSseMorphs)
            If TabsFace.TabPages.Contains(TabPageSseTints) Then TabsFace.TabPages.Remove(TabPageSseTints)
            ' Sculpt is a RaceMenu (skee64) subsystem — it has no Fallout 4 analogue and its tab is never
            ' populated here, so it must be removed alongside the other SSE-only tabs.
            If TabsFace.TabPages.Contains(TabPageSseSculpt) Then TabsFace.TabPages.Remove(TabPageSseSculpt)
            BuildMorphGroupSections()
            BuildBoneRegionsUI()
            BuildTintGroupRanks()
        End If

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
    ' =====================================================================
    ' SSE (Skyrim) face morphs — NAM9 sliders + NAMA type combos (built in code, game-gated)
    ' =====================================================================

    ''' <summary>SSE: fill the Designer's "Morphs (SSE)" tab panel — 18 NAM9 sliders + 4 NAMA type combos +
    ''' Load/Save .jslot, seeded from the NPC's NAM9/NAMA. The tab itself lives in the Designer (shown/hidden
    ''' by game); this only populates PanelSseMorphs.</summary>
    ''' <summary>Read-only view of the NPC's RaceMenu per-shape sculpt blocks (head + brows + eyes + mouth). Each
    ''' block is routed to its shape at render/bake by Host (the chargen tri). This tab exists so the sculpt data is
    ''' VISIBLE (that it's there and which shapes it covers) even though the app has no 3D sculpt brush yet — you
    ''' can't edit here, only confirm presence. Falls back to the head-only SseSculptHead for legacy overlays.</summary>
    Private Sub PopulateSseSculptTab()
        If ListSseSculpt Is Nothing Then Return
        ListSseSculpt.BeginUpdate()
        ListSseSculpt.Columns.Clear()
        ListSseSculpt.Items.Clear()
        ListSseSculpt.Columns.Add("Shape (host chargen tri)", 360)
        ListSseSculpt.Columns.Add("Sculpted vertices", 140)
        ListSseSculpt.Columns.Add("Source", 120)
        Dim p = Preset
        Dim parts As List(Of NPC_SculptPart) = If(p IsNot Nothing, p.SseSculptParts, Nothing)
        If parts IsNot Nothing AndAlso parts.Count > 0 Then
            For Each blk In parts
                If blk Is Nothing Then Continue For
                Dim host = If(blk.Host, "")
                Dim shapeName = If(String.IsNullOrEmpty(host), "(no host)", IO.Path.GetFileName(host))
                Dim it As New ListViewItem(shapeName)
                it.SubItems.Add(If(blk.Verts IsNot Nothing, blk.Verts.Count, 0).ToString())
                it.SubItems.Add("per-shape")
                it.ToolTipText = host
                ListSseSculpt.Items.Add(it)
            Next
        ElseIf p IsNot Nothing AndAlso p.SseSculptHead IsNot Nothing AndAlso p.SseSculptHead.Count > 0 Then
            Dim it As New ListViewItem("Head (legacy, head-only)")
            it.SubItems.Add(p.SseSculptHead.Count.ToString())
            it.SubItems.Add("head-only")
            ListSseSculpt.Items.Add(it)
        Else
            Dim it As New ListViewItem("(no RaceMenu sculpt)")
            it.SubItems.Add("0")
            it.SubItems.Add("—")
            it.ForeColor = SystemColors.GrayText
            ListSseSculpt.Items.Add(it)
        End If
        ListSseSculpt.ShowItemToolTips = True
        ListSseSculpt.EndUpdate()
    End Sub

    Private Sub PopulateSseMorphTab()
        ' Flat vertical TableLayoutPanel (proven layout) with a bold CK-category HEADER row before each group's
        ' NAM9 sliders + NAMA "type" combos: Nose/Jaw/Cheeks/Eyes/Brow/Mouth/Chin. Matches the Creation Kit
        ' Character Gen grouping. Sliders are TinySliderTextBox (same style as the rest of the app).
        Dim panel As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoScroll = True, .ColumnCount = 2, .Padding = New Padding(4)}
        panel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 160))
        panel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        Dim row As Integer = 0
        For Each cat In _sseMorphCategories
            ' Category header spanning both columns.
            panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 28))
            Dim hdr As New Label With {.Text = cat.Name, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
                                       .Font = New Font(Me.Font, FontStyle.Bold), .ForeColor = SystemColors.HotTrack, .Margin = New Padding(0, 8, 0, 0)}
            panel.Controls.Add(hdr, 0, row)
            panel.SetColumnSpan(hdr, 2)
            row += 1
            For Each si In cat.Sliders
                Dim idx = si
                panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 30))
                panel.Controls.Add(New Label With {.Text = SseNam9MorphMap.Sliders(si).Label, .Anchor = AnchorStyles.Left, .AutoSize = True, .Margin = New Padding(14, 8, 3, 0)}, 0, row)
                Dim tb As New FO4_Base_Library.TinySliderTextBox With {
                    .Minimum = -1.0R, .Maximum = 1.0R, .DisplayFormat = "0.00", .SmallChange = 0.01R, .LargeChange = 0.1R,
                    .Height = 26, .Dock = DockStyle.Fill, .Margin = New Padding(3, 3, 8, 3), .Value = 0.0R}
                AddHandler tb.ValueChanged, Sub(sender, e) OnSseSliderChanged(idx)
                AddHandler tb.DragEnded, AddressOf OnSliderDragEnded
                panel.Controls.Add(tb, 1, row)
                _sseNam9Sliders(si) = tb
                row += 1
            Next
            For Each fi2 In cat.Families
                Dim fi = fi2
                panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 30))
                panel.Controls.Add(New Label With {.Text = SseNam9MorphMap.Families(fi).Label, .Anchor = AnchorStyles.Left, .AutoSize = True, .Margin = New Padding(14, 8, 3, 0)}, 0, row)
                Dim cb As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Anchor = AnchorStyles.Left Or AnchorStyles.Right, .Margin = New Padding(3, 4, 8, 3)}
                cb.Items.Add("Default")
                For t = 1 To 15 : cb.Items.Add(SseNam9MorphMap.Families(fi).Prefix & t.ToString()) : Next
                AddHandler cb.SelectedIndexChanged, Sub(sender, e) OnSseTypeChanged(fi)
                panel.Controls.Add(cb, 1, row)
                _sseNamaCombos(fi) = cb
                row += 1
            Next
        Next

        ' NOTE: RaceMenu EXTENDED custom morphs are NOT here — this tab is the VANILLA (native chargen) NAM9/NAMA
        ' morphs that live in the NPC record. The RaceMenu "on top" custom sliders live in their own "RaceMenu" tab
        ' (BuildSseRaceMenuTab), driven by the per-race .slider catalog. Clear separation (user request).
        PanelSseMorphs.Controls.Clear()
        PanelSseMorphs.Controls.Add(panel)
        LoadSseMorphValues()
    End Sub

    ''' <summary>SSE-only "Head texture" row on the (vanilla) Face Parts tab: the NPC's face TextureSet override,
    ''' <c>NPC_.FTST</c>. It is vanilla record data — but it had no UI, so until now it could only be set by
    ''' importing a preset. RaceMenu writes the same field when a preset is applied
    ''' (<c>npc-&gt;headData-&gt;headTexture = presetData-&gt;headTexture</c>, PresetInterface.cpp:160).
    ''' Empty (FormID 0) = the head keeps the texture set its race/head-part declares.</summary>
    Private Sub BuildSseHeadTextureSection()
        Dim grp As New GroupBox With {.Text = "Head texture (FTST)", .Dock = DockStyle.Fill, .AutoSize = True,
                                      .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(3, 3, 3, 3)}
        Dim flow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .FlowDirection = FlowDirection.LeftToRight,
                                              .WrapContents = False, .Padding = New Padding(4)}
        _sseHeadTexLabel = New Label With {.AutoSize = True, .Margin = New Padding(3, 9, 12, 3)}
        Dim btnPick As New Button With {.Text = "Change…", .AutoSize = True}
        Dim btnClear As New Button With {.Text = "Use record default", .AutoSize = True}
        AddHandler btnPick.Click, AddressOf OnPickSseHeadTexture
        AddHandler btnClear.Click, Sub(s, e) SetSseHeadTexture(0UI)
        flow.Controls.Add(_sseHeadTexLabel) : flow.Controls.Add(btnPick) : flow.Controls.Add(btnClear)
        grp.Controls.Add(flow)

        FacePartsLayout.RowCount += 1
        FacePartsLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        FacePartsLayout.Controls.Add(grp, 0, FacePartsLayout.RowCount - 1)
        UpdateSseHeadTextureLabel()
    End Sub

    ''' <summary>Effective face TextureSet: the overlay's override when set, else the NPC record's own FTST.</summary>
    Private Function EffectiveSseHeadTextureFormID() As UInteger
        Dim p = Preset
        If p IsNot Nothing AndAlso p.SseHeadTextureFormID <> 0UI Then Return p.SseHeadTextureFormID
        Dim raw = TryGetRawNpc()
        Return If(raw IsNot Nothing, raw.HeadTextureFormID, 0UI)
    End Function

    Private Sub UpdateSseHeadTextureLabel()
        If _sseHeadTexLabel Is Nothing Then Return
        Dim fid = EffectiveSseHeadTextureFormID()
        If fid = 0UI Then
            _sseHeadTexLabel.Text = "(none — race / head part default)"
            Return
        End If
        Dim rec = _pluginManager.GetRecord(fid)
        Dim edid = If(rec IsNot Nothing AndAlso Not String.IsNullOrEmpty(rec.EditorID), rec.EditorID, "?")
        _sseHeadTexLabel.Text = $"{edid}  [{fid:X8}]"
    End Sub

    Private Sub OnPickSseHeadTexture(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_pluginManager, {"TXST"}, "Head texture (TXST)",
                                           EffectiveSseHeadTextureFormID(), allowNull:=True)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            SetSseHeadTexture(dlg.SelectedFormID)
        End Using
    End Sub

    Private Sub SetSseHeadTexture(fid As UInteger)
        Dim p = Preset
        If p Is Nothing Then Return
        p.SseHeadTextureFormID = fid
        UpdateSseHeadTextureLabel()
        ScheduleRefresh(FaceRefreshScope.FullReload)
    End Sub

    ''' <summary>SSE-only "RaceMenu" tab — the EXTENDED face sliders from RaceMenu's per-race .slider catalog
    ''' (RaceMenuSliderCatalog, faithful to skee64 FaceMorphInterface), kept SEPARATE from the vanilla NAM9/NAMA
    ''' "Morphs (SSE)" tab. Each slider's value lives in the NiOverride ValueSet keyed by the SLIDER NAME =
    ''' Preset.SseCustomMorphs; the render/bake (NpcMorphResolver) resolves the name→morph via the same catalog.
    ''' Only added when the loaded RaceMenu config actually declares extended sliders for this race+gender.</summary>
    Private Sub BuildSseRaceMenuTab()
        _sseRaceMenuControls.Clear()
        Dim tab As New TabPage("RaceMenu · Sliders") With {.Name = "TabPageSseRaceMenu", .Padding = New Padding(6)}
        TabsFace.TabPages.Add(tab)   ' always present under SSE so the section is visible (empty ⇒ shows why)

        Dim cat = NpcMorphResolver.SliderCatalog
        Dim sliders As List(Of FO4_Base_Library.RaceMenuSliderCatalog.SliderDef) =
            If(cat IsNot Nothing AndAlso _race IsNot Nothing, cat.GetSliders(_race.EditorID, _isFemale), Nothing)
        If sliders Is Nothing Then sliders = New List(Of FO4_Base_Library.RaceMenuSliderCatalog.SliderDef)()

        ' Morphs the PRESET carries that the installed catalog does not describe. This is the normal case, not an
        ' edge case: a .jslot records custom morphs by bare name (e.g. "CME_EyeballUpDown", "EFM_Chin_Shape") and
        ' the slider definitions that name them live in a separate mod's races.ini/.slider, which the user may not
        ' have installed. The resolver still applies them — AddCustomMorphChannel falls back to a direct
        ' name→TRI-morph lookup (NpcMorphResolver.vb:484-486) — so without these rows the morphs would deform the
        ' face while being invisible and uneditable here.
        Dim catalogued As New HashSet(Of String)(sliders.Select(Function(s) s.Name), StringComparer.OrdinalIgnoreCase)
        Dim extras As New List(Of String)
        If Preset IsNot Nothing AndAlso Preset.SseCustomMorphs IsNot Nothing Then
            For Each cm In Preset.SseCustomMorphs
                If cm Is Nothing OrElse String.IsNullOrEmpty(cm.Name) Then Continue For
                If Not catalogued.Contains(cm.Name) Then extras.Add(cm.Name)
            Next
            extras.Sort(StringComparer.OrdinalIgnoreCase)
        End If

        If sliders.Count = 0 AndAlso extras.Count = 0 Then
            Dim msg As String
            If cat Is Nothing Then
                msg = "RaceMenu extended-slider catalog not loaded (only built on a Skyrim session, after game data finishes loading)."
            ElseIf Not cat.HasAny() Then
                ' Catalog built but EMPTY = no races.ini was found at all. That is a config/load-order problem, not a
                ' property of this race — say so instead of blaming the race (which sent one report down the wrong path).
                msg = "No RaceMenu slider config was found in the loaded plugins." & vbCrLf & vbCrLf &
                      "The extended sliders live in Meshes\actors\character\FaceGenMorphs\<plugin>\races.ini (RaceMenu ships its own " &
                      "inside RaceMenu.bsa, under the racemenu.esp folder), and are only read for plugins this session LOADED. " &
                      "Check that RaceMenu is installed and that its plugin is ticked in the Preflight list." & vbCrLf & vbCrLf &
                      "The vanilla nose/jaw/eyes/… sliders are unaffected — they live in the ""Face Morphs"" tab."
            Else
                msg = $"No RaceMenu extended sliders are installed for this race ({If(_race?.EditorID, "?")}, {If(_isFemale, "Female", "Male")}), and this NPC carries no custom morphs." & vbCrLf & vbCrLf &
                      $"The loaded config does cover {cat.RaceCount()} other race(s), so RaceMenu itself is fine — this race simply is not listed in any " &
                      "races.ini (custom races need their mod to ship one). " & vbCrLf & vbCrLf &
                      "The vanilla nose/jaw/eyes/… sliders live in the ""Face Morphs"" tab (they go to the NPC record)."
            End If
            Logger.LogLazy(Function() $"[RACEMENU-TAB] empty for race='{If(_race?.EditorID, "?")}' female={_isFemale} " &
                                      $"catalog={If(cat Is Nothing, "NULL", $"races={cat.RaceCount()} configs={String.Join(", ", cat.LoadedConfigMods())}")}")
            tab.Controls.Add(New Label With {.Text = msg, .Dock = DockStyle.Fill, .Padding = New Padding(10), .AutoSize = False, .ForeColor = SystemColors.GrayText})
            Return
        End If

        Dim panel As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoScroll = True, .ColumnCount = 2, .Padding = New Padding(4)}
        panel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 200))
        panel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        ' Category display order (skee64 bitflags): Face, Eyes, Brow, Mouth, Head, Hair, Body, Extra, Expressions.
        Dim catOrder = New Integer() {16, 32, 64, 128, 8, 256, 4, 512, 1024}
        Dim row As Integer = 0
        For Each catVal In catOrder
            Dim inCat = sliders.Where(Function(s) s.Category = catVal AndAlso s.Type <> FO4_Base_Library.RaceMenuSliderCatalog.SliderType.HeadPart).ToList()
            If inCat.Count = 0 Then Continue For
            panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 28))
            Dim hdr As New Label With {.Text = FO4_Base_Library.RaceMenuSliderCatalog.CategoryName(catVal), .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
                                       .Font = New Font(Me.Font, FontStyle.Bold), .ForeColor = SystemColors.HotTrack, .Margin = New Padding(0, 8, 0, 0)}
            panel.Controls.Add(hdr, 0, row) : panel.SetColumnSpan(hdr, 2) : row += 1
            For Each def0 In inCat
                Dim def = def0
                panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 30))
                panel.Controls.Add(New Label With {.Text = def.Name, .Anchor = AnchorStyles.Left, .AutoSize = True, .Margin = New Padding(14, 8, 3, 0)}, 0, row)
                If def.Type = FO4_Base_Library.RaceMenuSliderCatalog.SliderType.Preset Then
                    Dim cb As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Anchor = AnchorStyles.Left Or AnchorStyles.Right, .Margin = New Padding(3, 4, 8, 3)}
                    cb.Items.Add("Default")
                    For n = 1 To Math.Max(1, def.PresetCount) : cb.Items.Add(def.LowerBound & n.ToString()) : Next
                    cb.SelectedIndex = Math.Max(0, Math.Min(cb.Items.Count - 1, CInt(Math.Truncate(CDbl(GetSseCustomValue(def.Name))))))
                    AddHandler cb.SelectedIndexChanged, Sub(s, e) OnSseRaceMenuChanged(def.Name, CSng(cb.SelectedIndex))
                    panel.Controls.Add(cb, 1, row)
                    _sseRaceMenuControls(def.Name) = cb
                Else
                    ' Slider: range from the bounds (skee64 LoadSliders:1318-1319): has lower ⇒ min −1, has upper ⇒ max +1.
                    Dim lo As Double = If(String.IsNullOrEmpty(def.LowerBound), 0.0R, -1.0R)
                    Dim hi As Double = If(String.IsNullOrEmpty(def.UpperBound), 0.0R, 1.0R)
                    If lo = 0.0R AndAlso hi = 0.0R Then hi = 1.0R
                    Dim tb As New FO4_Base_Library.TinySliderTextBox With {
                        .Minimum = lo, .Maximum = hi, .DisplayFormat = "0.00", .SmallChange = 0.01R, .LargeChange = 0.1R,
                        .Height = 26, .Dock = DockStyle.Fill, .Margin = New Padding(3, 3, 8, 3), .Value = Math.Max(lo, Math.Min(hi, CDbl(GetSseCustomValue(def.Name))))}
                    AddHandler tb.ValueChanged, Sub(s, e) OnSseRaceMenuChanged(def.Name, CSng(tb.Value))
                    AddHandler tb.DragEnded, AddressOf OnSliderDragEnded
                    panel.Controls.Add(tb, 1, row)
                    _sseRaceMenuControls(def.Name) = tb
                End If
                row += 1
            Next
        Next

        ' Uncatalogued custom morphs carried by this NPC's preset (see the comment above). RaceMenu stores a
        ' custom morph as a plain name→value pair with no declared bounds, and skee64 applies negative values to
        ' the slider's lower morph and positive to the upper (ApplyMorphs), so −1..1 is the faithful range.
        If extras.Count > 0 Then
            panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 28))
            Dim hdr As New Label With {.Text = If(sliders.Count = 0, "Custom morphs (from this NPC)", "Custom morphs (no slider definition installed)"),
                                       .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
                                       .Font = New Font(Me.Font, FontStyle.Bold), .ForeColor = SystemColors.HotTrack, .Margin = New Padding(0, 8, 0, 0)}
            panel.Controls.Add(hdr, 0, row) : panel.SetColumnSpan(hdr, 2) : row += 1
            For Each name0 In extras
                Dim nm = name0
                panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 30))
                panel.Controls.Add(New Label With {.Text = nm, .Anchor = AnchorStyles.Left, .AutoSize = True, .Margin = New Padding(14, 8, 3, 0)}, 0, row)
                Dim tb As New FO4_Base_Library.TinySliderTextBox With {
                    .Minimum = -1.0R, .Maximum = 1.0R, .DisplayFormat = "0.00", .SmallChange = 0.01R, .LargeChange = 0.1R,
                    .Height = 26, .Dock = DockStyle.Fill, .Margin = New Padding(3, 3, 8, 3),
                    .Value = Math.Max(-1.0R, Math.Min(1.0R, CDbl(GetSseCustomValue(nm))))}
                AddHandler tb.ValueChanged, Sub(s, e) OnSseRaceMenuChanged(nm, CSng(tb.Value))
                AddHandler tb.DragEnded, AddressOf OnSliderDragEnded
                panel.Controls.Add(tb, 1, row)
                _sseRaceMenuControls(nm) = tb
                row += 1
            Next
        End If

        tab.Controls.Add(panel)   ' tab was already added to TabsFace above
    End Sub

    ''' <summary>Current value of a RaceMenu custom morph (ValueSet entry) by slider name; 0 when absent.</summary>
    Private Function GetSseCustomValue(sliderName As String) As Single
        Dim p = Preset
        If p Is Nothing OrElse p.SseCustomMorphs Is Nothing Then Return 0.0F
        For Each cm In p.SseCustomMorphs
            If cm IsNot Nothing AndAlso String.Equals(cm.Name, sliderName, StringComparison.OrdinalIgnoreCase) Then Return cm.Value
        Next
        Return 0.0F
    End Function

    ''' <summary>A RaceMenu extended slider moved → upsert the ValueSet entry (keyed by slider name). A ~0 value
    ''' removes the entry (skee64 stores/exports only non-zero morphs, PresetInterface.cpp:450). Live re-render.</summary>
    Private Sub OnSseRaceMenuChanged(sliderName As String, value As Single)
        If _suspendEvents Then Return
        Dim p = Preset
        If p Is Nothing Then Return
        If p.SseCustomMorphs Is Nothing Then p.SseCustomMorphs = New List(Of NPC_CustomMorph)()
        Dim idx = p.SseCustomMorphs.FindIndex(Function(cm) cm IsNot Nothing AndAlso String.Equals(cm.Name, sliderName, StringComparison.OrdinalIgnoreCase))
        If Math.Abs(value) < 0.0001F Then
            If idx >= 0 Then p.SseCustomMorphs.RemoveAt(idx)
        ElseIf idx >= 0 Then
            p.SseCustomMorphs(idx) = New NPC_CustomMorph With {.Name = sliderName, .Value = value}
        Else
            p.SseCustomMorphs.Add(New NPC_CustomMorph With {.Name = sliderName, .Value = value})
        End If
        p.HasSseMorphs = True
        ScheduleRefresh(FaceRefreshScope.Morphs)
    End Sub

    ''' <summary>Reflect the preset's current custom-morph values onto the RaceMenu tab controls (after a .jslot load).</summary>
    Private Sub RefreshSseRaceMenuControls()
        _suspendEvents = True
        Try
            For Each kv In _sseRaceMenuControls
                Dim v = GetSseCustomValue(kv.Key)
                Dim tb = TryCast(kv.Value, FO4_Base_Library.TinySliderTextBox)
                If tb IsNot Nothing Then
                    tb.Value = Math.Max(tb.Minimum, Math.Min(tb.Maximum, CDbl(v)))
                Else
                    Dim cb = TryCast(kv.Value, ComboBox)
                    If cb IsNot Nothing Then cb.SelectedIndex = Math.Max(0, Math.Min(cb.Items.Count - 1, CInt(Math.Truncate(CDbl(v)))))
                End If
            Next
        Finally
            _suspendEvents = False
        End Try
    End Sub

    ''' <summary>SSE-only "Face Overlays" tab — RaceMenu "Face [Ovl{n}]" face-paint (OverlayInterface,
    ''' g_enableFaceOverlays). Same path-based decal model as the body overlays (Preset.SseBodyOverlays holds the
    ''' whole .jslot "overrides" array); this tab filters to the FACE nodes and the render composites them on the
    ''' FaceTint head shape (ResolveSseOverlayLayers). Generic (apply any texture to any NPC), not authored-only.</summary>
    Private Sub BuildSseFaceOverlaysTab()
        Dim tab As New TabPage("RaceMenu · Face Paint") With {.Name = "TabPageSseFaceOverlays", .Padding = New Padding(6)}
        Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2}
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 30)) : root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        Dim header As New Label With {.Dock = DockStyle.Fill, .Text = "Face paint overlays. Choose a paint on the left, then Add → to apply it to this NPC.", .Padding = New Padding(3, 6, 3, 0)}
        root.Controls.Add(header, 0, 0)

        ' Two-list body (same paradigm as the FO4 / EditBody overlay editor): catalog | buttons | applied+detail.
        Dim body As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42))
        body.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58))
        body.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        ' Col 0: the FACE paint catalog (filter + list) — the union of mods' AddFacePaint registrations.
        Dim catBox As New GroupBox With {.Dock = DockStyle.Fill, .Text = "Face paints (RaceMenu)"}
        Dim catLay As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2}
        catLay.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        catLay.RowStyles.Add(New RowStyle(SizeType.Absolute, 28)) : catLay.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        _sseFaceOvFilter = New TextBox With {.Dock = DockStyle.Fill, .Margin = New Padding(3)}
        _sseFaceOvFilter.PlaceholderText = "Filter paints…"
        AddHandler _sseFaceOvFilter.TextChanged, Sub(s, e) RefreshSseFacePaintCatalog()
        catLay.Controls.Add(_sseFaceOvFilter, 0, 0)
        _sseFaceOvCatalog = New ListBox With {.Dock = DockStyle.Fill, .IntegralHeight = False, .DrawMode = DrawMode.OwnerDrawFixed}
        AddHandler _sseFaceOvCatalog.DoubleClick, AddressOf OnFaceOvAdd
        AddHandler _sseFaceOvCatalog.DrawItem, AddressOf DrawFacePaintCatalogItem
        catLay.Controls.Add(_sseFaceOvCatalog, 0, 1)
        catBox.Controls.Add(catLay)
        body.Controls.Add(catBox, 0, 0)

        ' Col 1: Add →/← Remove + Up/Down (reorder the Ovl{n} draw order).
        Dim mid As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoSize = True, .Margin = New Padding(4, 40, 4, 0)}
        Dim bAdd As New Button With {.Text = "Add →", .AutoSize = True, .Margin = New Padding(2)}
        Dim bRem As New Button With {.Text = "← Remove", .AutoSize = True, .Margin = New Padding(2)}
        Dim bUp As New Button With {.Text = "Up", .AutoSize = True, .Margin = New Padding(2, 14, 2, 2)}
        Dim bDown As New Button With {.Text = "Down", .AutoSize = True, .Margin = New Padding(2)}
        AddHandler bAdd.Click, AddressOf OnFaceOvAdd
        AddHandler bRem.Click, AddressOf OnFaceOvRemove
        AddHandler bUp.Click, Sub(s, e) OnFaceOvMove(-1)
        AddHandler bDown.Click, Sub(s, e) OnFaceOvMove(1)
        mid.Controls.Add(bAdd) : mid.Controls.Add(bRem) : mid.Controls.Add(bUp) : mid.Controls.Add(bDown)
        body.Controls.Add(mid, 1, 0)

        ' Col 2: applied face overlays (top) + the selected overlay's tint/opacity detail (below).
        Dim rightBox As New GroupBox With {.Dock = DockStyle.Fill, .Text = "Applied face overlays"}
        Dim rightLay As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2}
        rightLay.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        rightLay.RowStyles.Add(New RowStyle(SizeType.Percent, 45)) : rightLay.RowStyles.Add(New RowStyle(SizeType.Percent, 55))
        _sseFaceOvList = New ListBox With {.Dock = DockStyle.Fill, .IntegralHeight = False, .DrawMode = DrawMode.OwnerDrawFixed}
        AddHandler _sseFaceOvList.SelectedIndexChanged, Sub(s, e) UpdateFaceOvDetail()
        AddHandler _sseFaceOvList.DrawItem, AddressOf DrawFaceAppliedOverlayItem
        rightLay.Controls.Add(_sseFaceOvList, 0, 0)

        ' Detail: texture is READ-ONLY display (chosen from the catalog at Add time); tint + opacity are editable.
        Dim d As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 5, .AutoScroll = True}
        d.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 62)) : d.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For k = 0 To 3 : d.RowStyles.Add(New RowStyle(SizeType.AutoSize)) : Next
        d.RowStyles.Add(New RowStyle(SizeType.Percent, 100))   ' filler
        Dim rr = 0
        d.Controls.Add(New Label With {.Text = "Texture:", .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 9, 3, 0)}, 0, rr)
        _sseFaceOvDiffuse = New TextBox With {.ReadOnly = True}
        d.Controls.Add(FaceOvFlow(_sseFaceOvDiffuse), 1, rr) : rr += 1
        d.Controls.Add(New Label With {.Text = "Normal:", .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 9, 3, 0)}, 0, rr)
        _sseFaceOvNormal = New TextBox With {.ReadOnly = True}
        d.Controls.Add(FaceOvFlow(_sseFaceOvNormal), 1, rr) : rr += 1
        _sseFaceOvTintEnable = New CheckBox With {.Text = "Tint", .AutoSize = True, .Margin = New Padding(3, 9, 3, 0)}
        AddHandler _sseFaceOvTintEnable.CheckedChanged, AddressOf OnFaceOvTintToggled
        d.Controls.Add(_sseFaceOvTintEnable, 0, rr)
        _sseFaceOvTintColor = New Button With {.Text = "Color…", .AutoSize = True, .Anchor = AnchorStyles.Left} : AddHandler _sseFaceOvTintColor.Click, AddressOf OnFaceOvTintColor
        d.Controls.Add(_sseFaceOvTintColor, 1, rr) : rr += 1
        ' skee64 kParam_ShaderAlpha (key 8) — the overlay's opacity, independent of the tint colour.
        d.Controls.Add(New Label With {.Text = "Opacity:", .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 9, 3, 0)}, 0, rr)
        _sseFaceOvTintAlpha = New FO4_Base_Library.TinySliderTextBox With {.Minimum = 0.0R, .Maximum = 1.0R, .DisplayFormat = "0.00", .SmallChange = 0.01R, .LargeChange = 0.1R, .Height = 26, .Anchor = AnchorStyles.Left Or AnchorStyles.Right, .Margin = New Padding(3, 4, 8, 3), .Value = 1.0R}
        AddHandler _sseFaceOvTintAlpha.ValueChanged, AddressOf OnFaceOvTintAlpha
        d.Controls.Add(_sseFaceOvTintAlpha, 1, rr) : rr += 1
        rightLay.Controls.Add(d, 0, 1)
        rightBox.Controls.Add(rightLay)
        body.Controls.Add(rightBox, 2, 0)

        root.Controls.Add(body, 0, 1)
        tab.Controls.Add(root)
        TabsFace.TabPages.Add(tab)
        RefreshFaceOvList(-1)
        RefreshSseFacePaintCatalog()
    End Sub

    ''' <summary>Owner-draw a face-paint catalog row red when its texture is not in the load order (the renderer
    ''' would skip it — a mod may register a paint whose .dds it doesn't ship).</summary>
    Private Sub DrawFacePaintCatalogItem(sender As Object, e As DrawItemEventArgs)
        e.DrawBackground()
        If e.Index >= 0 AndAlso e.Index < _sseFaceOvCatalog.Items.Count Then
            Dim missing As Boolean = e.Index < _sseFacePaintShown.Count AndAlso Not SseCatalogs.TextureResolves(_sseFacePaintShown(e.Index).Path)
            Dim fore = If(missing, SseCatalogs.MissingTextureColor,
                          If((e.State And DrawItemState.Selected) <> 0, SystemColors.HighlightText, e.ForeColor))
            TextRenderer.DrawText(e.Graphics, _sseFaceOvCatalog.Items(e.Index).ToString(), e.Font, e.Bounds, fore,
                                  TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
        End If
        e.DrawFocusRectangle()
    End Sub

    ''' <summary>Owner-draw an applied face-overlay row red when its diffuse texture is missing from the load order.</summary>
    Private Sub DrawFaceAppliedOverlayItem(sender As Object, e As DrawItemEventArgs)
        e.DrawBackground()
        Dim lst = FaceOverlaysList()
        If e.Index >= 0 AndAlso e.Index < _sseFaceOvList.Items.Count Then
            Dim missing As Boolean = e.Index < lst.Count AndAlso Not SseCatalogs.TextureResolves(lst(e.Index).DiffusePath)
            Dim fore = If(missing, SseCatalogs.MissingTextureColor,
                          If((e.State And DrawItemState.Selected) <> 0, SystemColors.HighlightText, e.ForeColor))
            TextRenderer.DrawText(e.Graphics, _sseFaceOvList.Items(e.Index).ToString(), e.Font, e.Bounds, fore,
                                  TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
        End If
        e.DrawFocusRectangle()
    End Sub

    ''' <summary>Fill the LEFT face-paint catalog from RaceMenuPaintCatalog (Face category), honouring the filter.
    ''' Parallel <see cref="_sseFacePaintShown"/> maps a shown row back to its entry.</summary>
    Private Sub RefreshSseFacePaintCatalog()
        If _sseFaceOvCatalog Is Nothing Then Return
        _sseFacePaintShown.Clear()
        _sseFaceOvCatalog.BeginUpdate()
        Try
            _sseFaceOvCatalog.Items.Clear()
            Dim cat = FO4_Base_Library.RaceMenuPaintCatalog.Current
            If cat Is Nothing Then Return
            Dim filter = If(_sseFaceOvFilter Is Nothing, "", _sseFaceOvFilter.Text.Trim())
            For Each en In cat.Entries(FO4_Base_Library.RaceMenuPaintCatalog.PaintCategory.Face)
                If filter.Length = 0 OrElse en.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 Then
                    _sseFaceOvCatalog.Items.Add(en.DisplayName)
                    _sseFacePaintShown.Add(en)
                End If
            Next
        Finally
            _sseFaceOvCatalog.EndUpdate()
        End Try
    End Sub

    ''' <summary>One "textbox + buttons" row that stretches to its cell: the textbox fills the remaining width and
    ''' each button is auto-sized on the right, so nothing clips regardless of panel width.</summary>
    Private Function FaceOvFlow(tb As TextBox, ParamArray buttons As Button()) As Control
        Dim p As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .Margin = New Padding(0, 4, 6, 0),
                                            .ColumnCount = 1 + buttons.Length, .RowCount = 1}
        p.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        tb.Dock = DockStyle.Fill
        p.Controls.Add(tb, 0, 0)
        Dim c = 1
        For Each b In buttons
            b.Anchor = AnchorStyles.Left
            p.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            p.Controls.Add(b, c, 0) : c += 1
        Next
        Return p
    End Function

    ''' <summary>The Face-node overlays inside Preset.SseBodyOverlays (the whole .jslot overrides array).</summary>
    ''' <summary>Face overlays (Face [Ovl{n}] nodes) in DRAW ORDER — highest Ovl index first, since skee64 draws
    ''' higher indices on top, so top-of-list = on top and Up/Down are intuitive.</summary>
    Private Function FaceOverlaysList() As List(Of RaceMenuJslot.JslotOverlayNode)
        Dim p = Preset
        If p Is Nothing OrElse p.SseBodyOverlays Is Nothing Then Return New List(Of RaceMenuJslot.JslotOverlayNode)
        Return p.SseBodyOverlays.
            Where(Function(o) o IsNot Nothing AndAlso SseCatalogs.ZoneOfNode(o.NodeName).HasValue AndAlso
                              SseCatalogs.ZoneOfNode(o.NodeName).Value = SseCatalogs.OverlayZone.Face).
            OrderByDescending(Function(o) SseCatalogs.IndexOfNode(o.NodeName)).ToList()
    End Function

    ''' <summary>Reorder face paint by swapping two overlays' Ovl{n} node indices (RaceMenu's draw order). All face
    ''' overlays share the Face zone, so any list neighbour is a valid swap.</summary>
    Private Sub OnFaceOvMove(delta As Integer)
        Dim l = FaceOverlaysList()
        Dim row = _sseFaceOvList.SelectedIndex
        Dim targetRow = row + delta
        If row < 0 OrElse row >= l.Count OrElse targetRow < 0 OrElse targetRow >= l.Count Then Return
        Dim ov = l(row), neighbour = l(targetRow)
        Dim ni = SseCatalogs.IndexOfNode(ov.NodeName), nj = SseCatalogs.IndexOfNode(neighbour.NodeName)
        If ni < 0 OrElse nj < 0 Then Return
        ov.NodeName = SseCatalogs.OverlayNodeName(SseCatalogs.OverlayZone.Face, nj)
        neighbour.NodeName = SseCatalogs.OverlayNodeName(SseCatalogs.OverlayZone.Face, ni)
        p_HasOverlaysTrue()
        RefreshFaceOvList(targetRow)
        ScheduleRefresh(FaceRefreshScope.FullReload)
    End Sub

    Private Function SelectedFaceOverlay() As RaceMenuJslot.JslotOverlayNode
        Dim l = FaceOverlaysList()
        Dim i = _sseFaceOvList.SelectedIndex
        If i < 0 OrElse i >= l.Count Then Return Nothing
        Return l(i)
    End Function

    ''' <summary>List label for an applied face overlay: node identity plus the RaceMenu face-paint NAME re-derived
    ''' from the catalog by the stored texture path (the only link the <c>.jslot</c> keeps), so a registered texture
    ''' shows its friendly name rather than the anonymous file name. Falls back to the file name when unregistered.</summary>
    Private Shared Function FaceOvLabel(ov As RaceMenuJslot.JslotOverlayNode) As String
        Dim diff As String
        If String.IsNullOrEmpty(ov.DiffusePath) Then
            diff = "(no texture)"
        Else
            Dim name As String = Nothing
            Dim z = SseCatalogs.ZoneOfNode(ov.NodeName)
            If z.HasValue Then name = SseCatalogs.PaintNameForPath(z.Value, ov.DiffusePath)
            diff = If(Not String.IsNullOrEmpty(name), name, IO.Path.GetFileName(ov.DiffusePath))
        End If
        Return $"{ov.NodeName} — {diff}{If(ov.HasTint, "  ●", "")}"
    End Function

    Private Sub RefreshFaceOvList(selectIndex As Integer)
        _suspendEvents = True
        Try
            _sseFaceOvList.BeginUpdate()
            _sseFaceOvList.Items.Clear()
            For Each ov In FaceOverlaysList() : _sseFaceOvList.Items.Add(FaceOvLabel(ov)) : Next
            _sseFaceOvList.EndUpdate()
            Dim n = _sseFaceOvList.Items.Count
            If n > 0 Then _sseFaceOvList.SelectedIndex = Math.Max(0, Math.Min(selectIndex, n - 1))
        Finally
            _suspendEvents = False
        End Try
        UpdateFaceOvDetail()
    End Sub

    Private Sub UpdateFaceOvDetail()
        Dim ov = SelectedFaceOverlay()
        _suspendEvents = True
        Try
            Dim has = ov IsNot Nothing
            _sseFaceOvDiffuse.Enabled = has : _sseFaceOvNormal.Enabled = has : _sseFaceOvTintEnable.Enabled = has
            _sseFaceOvTintColor.Enabled = has AndAlso ov.HasTint
            _sseFaceOvDiffuse.Text = If(has, If(ov.DiffusePath, ""), "")
            _sseFaceOvNormal.Text = If(has, If(ov.NormalPath, ""), "")
            _sseFaceOvTintEnable.Checked = has AndAlso ov.HasTint
            _sseFaceOvTintColor.BackColor = If(has AndAlso ov.HasTint,
                Color.FromArgb(FaceClampByte(ov.TintR), FaceClampByte(ov.TintG), FaceClampByte(ov.TintB)), Color.White)
            ' Opacity (key 8) is independent of the tint colour (key 7).
            _sseFaceOvTintAlpha.Enabled = has
            _sseFaceOvTintAlpha.Value = If(has, CDbl(Math.Max(0.0F, Math.Min(1.0F, If(ov.HasAlpha, ov.Alpha, 1.0F)))), 1.0R)
        Finally
            _suspendEvents = False
        End Try
    End Sub

    Private Shared Function FaceClampByte(v As Single) As Integer
        Return Math.Min(255, Math.Max(0, CInt(Math.Round(v * 255.0F))))
    End Function

    ''' <summary>Add a face overlay in the next free <c>Face [Ovl n]</c> slot. Bounded by
    ''' <c>[Overlays/Face] iNumOverlays</c> in skee64.ini: the engine instantiates exactly that many face
    ''' overlay nodes, so anything beyond would render here and be ignored in-game.</summary>
    Private Sub OnFaceOvAdd(sender As Object, e As EventArgs)
        Dim p = Preset
        If p Is Nothing Then Return
        If p.SseBodyOverlays Is Nothing Then p.SseBodyOverlays = New List(Of RaceMenuJslot.JslotOverlayNode)()
        ' The face paint chosen from the LEFT catalog (like the FO4 overlay editor: Add moves the selected entry in).
        Dim ai = _sseFaceOvCatalog.SelectedIndex
        If ai < 0 OrElse ai >= _sseFacePaintShown.Count Then
            MessageBox.Show(Me, "Choose a face paint from the list on the left, then press Add →.",
                            "No paint selected", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim entry = _sseFacePaintShown(ai)
        Dim used As New HashSet(Of Integer)
        For Each o In p.SseBodyOverlays
            If o Is Nothing Then Continue For
            Dim z = SseCatalogs.ZoneOfNode(o.NodeName)
            If z.HasValue AndAlso z.Value = SseCatalogs.OverlayZone.Face Then
                Dim n0 = SseCatalogs.IndexOfNode(o.NodeName)
                If n0 >= 0 Then used.Add(n0)
            End If
        Next
        Dim limit = SseCatalogs.OverlayCount(SseCatalogs.OverlayZone.Face)
        Dim k = 0 : While used.Contains(k) : k += 1 : End While
        If k >= limit Then
            MessageBox.Show(Me,
                $"RaceMenu only creates {limit} face overlay slot(s) ([Ovl0]…[Ovl{limit - 1}]), per iNumOverlays in skee64.ini." & vbCrLf & vbCrLf &
                "Remove one, or raise iNumOverlays in skee64.ini and reopen the editor.",
                "No free overlay slot", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim nrm As String = ""
        If entry.Slots IsNot Nothing AndAlso entry.Slots.Length > 1 Then
            Dim s1 = entry.Slots(1)
            If Not String.IsNullOrEmpty(s1) AndAlso Not s1.Equals("ignore", StringComparison.OrdinalIgnoreCase) Then nrm = s1
        End If
        p.SseBodyOverlays.Add(New RaceMenuJslot.JslotOverlayNode With {
            .NodeName = SseCatalogs.OverlayNodeName(SseCatalogs.OverlayZone.Face, k), .DiffusePath = entry.Path, .NormalPath = nrm,
            .TintR = 1, .TintG = 1, .TintB = 1, .TintA = 1, .HasTint = False,
            .Alpha = 1.0F, .HasAlpha = True})
        p.HasOverlays = True
        RefreshFaceOvList(_sseFaceOvList.Items.Count)
        ScheduleRefresh(FaceRefreshScope.FullReload)
    End Sub

    Private Sub OnFaceOvRemove(sender As Object, e As EventArgs)
        Dim ov = SelectedFaceOverlay()
        Dim p = Preset
        If ov Is Nothing OrElse p Is Nothing OrElse p.SseBodyOverlays Is Nothing Then Return
        p.SseBodyOverlays.Remove(ov)
        Dim idx = _sseFaceOvList.SelectedIndex
        RefreshFaceOvList(idx - 1)
        ScheduleRefresh(FaceRefreshScope.FullReload)
    End Sub

    Private Sub OnFaceOvTintToggled(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim ov = SelectedFaceOverlay()
        If ov Is Nothing Then Return
        ov.HasTint = _sseFaceOvTintEnable.Checked
        ' Opacity stays editable regardless of the tint colour (different skee64 override key).
        _sseFaceOvTintColor.Enabled = ov.HasTint
        Dim i = _sseFaceOvList.SelectedIndex
        If i >= 0 Then _sseFaceOvList.Items(i) = FaceOvLabel(ov)
        p_HasOverlaysTrue()
        ScheduleRefresh(FaceRefreshScope.FullReload)
    End Sub

    Private Sub OnFaceOvTintColor(sender As Object, e As EventArgs)
        Dim ov = SelectedFaceOverlay()
        If ov Is Nothing Then Return
        Using dlg As New ColorDialog() With {.Color = Color.FromArgb(FaceClampByte(ov.TintR), FaceClampByte(ov.TintG), FaceClampByte(ov.TintB))}
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                ov.TintR = dlg.Color.R / 255.0F : ov.TintG = dlg.Color.G / 255.0F : ov.TintB = dlg.Color.B / 255.0F : ov.HasTint = True
                _sseFaceOvTintColor.BackColor = dlg.Color
                p_HasOverlaysTrue()
                ScheduleRefresh(FaceRefreshScope.FullReload)
            End If
        End Using
    End Sub

    ''' <summary>Opacity slider → skee64's kParam_ShaderAlpha (key 8), not the tint colour's alpha byte.</summary>
    Private Sub OnFaceOvTintAlpha(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim ov = SelectedFaceOverlay()
        If ov Is Nothing Then Return
        ov.Alpha = CSng(_sseFaceOvTintAlpha.Value)
        ov.HasAlpha = True
        p_HasOverlaysTrue()
        ScheduleRefresh(FaceRefreshScope.FullReload)
    End Sub

    Private Sub p_HasOverlaysTrue()
        Dim p = Preset
        If p IsNot Nothing Then p.HasOverlays = True
    End Sub


    ''' <summary>SSE: seed the morph sliders/combos. Source = the overlay's authored NAM9/NAMA when it has taken
    ''' ownership (a loaded .jslot / Paste / a prior committed Edit Face set them via ApplySseMorphOverlay), else
    ''' the raw NPC record. Seeding from the overlay-EFFECTIVE state (mirrors FO4's SeedFromOverlayOrRaw) is what
    ''' keeps iterative editing from silently reverting: without it, re-opening the tab shows the raw morphs and
    ''' the next slider touch clones the raw-seeded arrays over the real edits.</summary>
    Private Sub LoadSseMorphValues()
        _suspendEvents = True
        Try
            Dim p = Preset
            Dim raw = TryGetRawNpc()
            Dim useOverlayNam9 = p IsNot Nothing AndAlso p.HasSseMorphs AndAlso p.SseNam9 IsNot Nothing
            For i = 0 To SseNam9MorphMap.Nam9SliderCount - 1
                Dim v As Single = 0
                If useOverlayNam9 AndAlso i < p.SseNam9.Length Then
                    v = p.SseNam9(i)
                ElseIf raw IsNot Nothing AndAlso raw.Nam9Raw IsNot Nothing AndAlso raw.Nam9Raw.Length >= (i + 1) * 4 Then
                    v = BitConverter.ToSingle(raw.Nam9Raw, i * 4)
                End If
                If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then v = 0
                _sseNam9(i) = v
                If _sseNam9Sliders(i) IsNot Nothing Then _sseNam9Sliders(i).Value = Math.Max(-1.0R, Math.Min(1.0R, CDbl(v)))
            Next
            ' NAMA: same overlay-first sourcing. PRESERVE the 0xFFFFFFFF "unset" sentinel in _sseNama (mapping it
            ' to 0 only for the combo DISPLAY), so a family the user never touches round-trips byte-exact instead
            ' of being materialized to an explicit type 0 on save.
            Dim useOverlayNama = p IsNot Nothing AndAlso p.HasSseMorphs AndAlso p.SseNama IsNot Nothing
            For f = 0 To SseNam9MorphMap.NamaFamilyCount - 1
                Dim tv As UInteger = SseNam9MorphMap.NamaUnset
                If useOverlayNama AndAlso f < p.SseNama.Length Then
                    tv = p.SseNama(f)
                ElseIf raw IsNot Nothing AndAlso raw.NamaRaw IsNot Nothing AndAlso raw.NamaRaw.Length >= (f + 1) * 4 Then
                    tv = BitConverter.ToUInt32(raw.NamaRaw, f * 4)
                End If
                _sseNama(f) = tv
                Dim displayIdx As Integer = If(tv = SseNam9MorphMap.NamaUnset, 0, CInt(Math.Min(tv, 15UI)))
                If _sseNamaCombos(f) IsNot Nothing Then _sseNamaCombos(f).SelectedIndex = Math.Max(0, Math.Min(_sseNamaCombos(f).Items.Count - 1, displayIdx))
            Next
        Finally
            _suspendEvents = False
        End Try
    End Sub

    Private Sub OnSseSliderChanged(idx As Integer)
        If _suspendEvents Then Return
        If idx < 0 OrElse idx >= _sseNam9Sliders.Length OrElse _sseNam9Sliders(idx) Is Nothing Then Return
        Dim v As Single = CSng(_sseNam9Sliders(idx).Value)
        _sseNam9(idx) = v
        ApplySseMorphOverlay()
        ScheduleRefresh(FaceRefreshScope.Morphs)
    End Sub

    Private Sub OnSseTypeChanged(fi As Integer)
        If _suspendEvents Then Return
        If fi < 0 OrElse fi >= _sseNamaCombos.Length Then Return
        _sseNama(fi) = CUInt(Math.Max(0, _sseNamaCombos(fi).SelectedIndex))
        ApplySseMorphOverlay()
        ScheduleRefresh(FaceRefreshScope.Morphs)
    End Sub

    ''' <summary>Push the edited NAM9/NAMA into the overlay preset so the live render + bake reflect the edit.
    ''' The overlay (NpcRecordOverlay) writes these into shadow.Nam9Raw/NamaRaw; the SSE morph resolver reads
    ''' them. Marks HasSseMorphs so the overlay applies them (and Save ESP emits NAM9/NAMA).</summary>
    Private Sub ApplySseMorphOverlay()
        Dim p = Preset
        If p Is Nothing Then Return
        p.SseNam9 = DirectCast(_sseNam9.Clone(), Single())
        p.SseNama = DirectCast(_sseNama.Clone(), UInteger())
        p.HasSseMorphs = True
    End Sub

    ' =====================================================================
    ' SSE (Skyrim) face tints — TINI/TINC/TINV/TIAS per-layer color + coverage (built in code, game-gated)
    ' =====================================================================

    ''' <summary>Build the editable SSE tint layer list the ENGINE composes: ALL of the RACE's tint layers in
    ''' RACE order (defaults from TIND→CLFM colour + default TINV), with the NPC's AUTHORED tint (TINI/TINC/TINV)
    ''' overriding per index — exactly the model SseFaceTintComposer uses (race defaults + NPC override + any
    ''' mod-added RACE tint layers). Mirrors the FO4 tint tab (race groups + NPC + customs).</summary>
    Private Sub ParseSseTintLayers()
        _sseTintLayers.Clear()
        Dim raw = TryGetRawNpc()
        Dim p = Preset
        ' 1) NPC-authored layers, keyed by index. Source = the overlay's authored tint override when it has taken
        ' ownership (loaded .jslot / Paste / a prior committed Edit Face), else the raw record — same overlay-first
        ' rule as the morph tab, so re-opening the editor shows the EFFECTIVE tints and a subsequent edit doesn't
        ' drop overlay-authored layers absent from the raw record.
        Dim tintSource As List(Of NPC_RawSubrecord) = If(p IsNot Nothing AndAlso p.HasSseTints AndAlso p.SseTintRawOverride IsNot Nothing,
                                                         p.SseTintRawOverride, If(raw IsNot Nothing, raw.SseTintRaw, Nothing))
        Dim authored As New Dictionary(Of Integer, SseTintEdit)
        If tintSource IsNot Nothing Then
            Dim cur As New SseTintEdit With {.Index = -1, .A = 255}
            Dim have As Boolean = False
            For Each sr In tintSource
                Select Case sr.Sig
                    Case "TINI" : cur = New SseTintEdit With {.Index = BitConverter.ToUInt16(sr.Data, 0), .A = 255} : have = True
                    Case "TINC"
                        If sr.Data.Length >= 3 Then cur.R = sr.Data(0) : cur.G = sr.Data(1) : cur.B = sr.Data(2)
                        If sr.Data.Length >= 4 Then cur.A = sr.Data(3)
                    Case "TINV" : If sr.Data.Length >= 4 Then cur.V = BitConverter.ToUInt32(sr.Data, 0) / 100.0
                    Case "TIAS"
                        If sr.Data.Length >= 2 Then cur.Tias = BitConverter.ToInt16(sr.Data, 0)
                        If have Then cur.Authored = True : authored(cur.Index) = cur : have = False
                End Select
            Next
        End If
        ' 2) All RACE tint layers (defaults) in RACE order; authored overrides per index.
        Dim layers = SseFaceTintComposer.GetRaceLayersOrdered(_pluginManager, _raceFormID, _isFemale)
        If layers IsNot Nothing AndAlso layers.Count > 0 Then
            For Each lay In layers
                Dim e As SseTintEdit
                If authored.ContainsKey(lay.Index) Then
                    e = authored(lay.Index)
                Else
                    Dim rgb = ResolveClfmRgb(lay.DefaultClfm)
                    e = New SseTintEdit With {.Index = lay.Index, .R = rgb.R, .G = rgb.G, .B = rgb.B, .A = 255, .V = lay.DefaultValue, .Authored = False, .Tias = 0}
                End If
                ' RACE-layer context for the detail panel (preset dropdown + reset-to-default), from GetRaceLayersOrdered.
                e.MaskType = lay.MaskType
                e.DefaultClfm = lay.DefaultClfm
                e.DefaultValue = lay.DefaultValue
                e.Presets = lay.Presets
                e.MaskName = MaskFileName(lay.Path)
                ' RaceMenu custom mask texture override for this layer (from a loaded .jslot / Paste), if any.
                Dim ovr As String = Nothing
                If p IsNot Nothing AndAlso p.SseTintTexOverride IsNot Nothing Then p.SseTintTexOverride.TryGetValue(lay.Index, ovr)
                e.MaskPathOverride = ovr
                e.MaskPath = If(Not String.IsNullOrEmpty(ovr), ovr, lay.Path)
                ' Blue = the MASK (.dds) differs from the RACE's own mask, i.e. a custom mask override (only a
                ' preset / RaceMenu can set one; the NPC record can't). A layer where only the colour/coverage
                ' value differs is NOT blue — that's an ordinary authored value (black), per the user's rule.
                e.Customized = Not String.IsNullOrEmpty(ovr) AndAlso
                               Not String.Equals(ovr, lay.Path, StringComparison.OrdinalIgnoreCase)
                _sseTintLayers.Add(e)
            Next
        Else
            ' RACE layers unresolved → fall back to the authored layers only.
            For Each kv In authored : _sseTintLayers.Add(kv.Value) : Next
        End If
    End Sub

    ''' <summary>CLFM (color form) → sRGB (R,G,B) bytes from its CNAM, for the default (unauthored) tint colour.</summary>
    Private Function ResolveClfmRgb(clfmFid As UInteger) As (R As Byte, G As Byte, B As Byte)
        If clfmFid = 0UI OrElse _pluginManager Is Nothing Then Return (128, 128, 128)
        Dim rec = _pluginManager.GetRecord(clfmFid)
        If rec Is Nothing OrElse rec.Header.Signature <> "CLFM" Then Return (128, 128, 128)
        For Each sr In rec.Subrecords
            If sr.Signature = "CNAM" AndAlso sr.Data.Length >= 3 Then Return (sr.Data(0), sr.Data(1), sr.Data(2))
        Next
        Return (128, 128, 128)
    End Function

    ''' <summary>CLFM (color form) → a human-usable name for the preset dropdown: the FULL display name if the
    ''' record has one (e.g. "Skin Tone 05"), else the EditorID (the identifier used in the ESP/CK), else "".</summary>
    Private Function ResolveClfmName(clfmFid As UInteger) As String
        If clfmFid = 0UI OrElse _pluginManager Is Nothing Then Return ""
        Dim rec = _pluginManager.GetRecord(clfmFid)
        If rec Is Nothing OrElse rec.Header.Signature <> "CLFM" Then Return ""
        Dim full = rec.GetSubrecord("FULL")
        If full.HasValue AndAlso full.Value.Data IsNot Nothing AndAlso full.Value.Data.Length > 0 Then
            Dim s = _pluginManager.ResolveFieldString(rec, full.Value)   ' localized-string aware (FULL is translatable)
            If Not String.IsNullOrWhiteSpace(s) Then Return s
        End If
        Return If(rec.EditorID, "")
    End Function

    Private Shared Function MaskFileName(path As String) As String
        If String.IsNullOrEmpty(path) Then Return ""
        Dim p = path.Replace("/"c, "\"c)
        Dim i = p.LastIndexOf("\"c)
        Return If(i >= 0, p.Substring(i + 1), p)
    End Function

    ''' <summary>Does this tint mask .dds exist in the load order (loose + BSA)? Uses the SAME normalisation the
    ''' compositor's mask loader does (SseFaceTintComposer.DecodeMask: lowercase, backslashes, prepend "textures\"),
    ''' so a label flagged missing here is exactly a mask the render couldn't load. An empty path is "present"
    ''' (nothing to load), so it isn't flagged red.</summary>
    Private Shared Function TintMaskExists(maskPath As String) As Boolean
        If String.IsNullOrEmpty(maskPath) Then Return True
        Dim key = maskPath.Replace("/"c, "\"c).ToLowerInvariant()
        If Not key.StartsWith("textures\") Then key = "textures\" & key
        Return FO4_Base_Library.FilesDictionary_class.Dictionary.ContainsKey(key)
    End Function

    ''' <summary>SSE: fill the Designer's "Tints (SSE)" tab panel as a MASTER-DETAIL editor (mirrors the FO4 tint
    ''' tab): a list of every RACE tint layer on the left, and a detail panel on the right for the selected layer —
    ''' vanilla preset dropdown (TIAS), custom RGB colour, coverage (TINV), the RaceMenu warpaint mask override, and
    ''' a reset-to-RACE-default. Edits rebuild SseTintRaw and push it through the overlay so the composer (render +
    ''' bake), the body skin tone (QNAM), and Save ESP all reflect them — TINC stays the effective colour, TIAS the
    ''' consistent preset selector (a preset's TIRS, or -1 = custom).</summary>
    Private Sub PopulateSseTintTab()
        Dim prevSel = _sseTintSelIndex
        ParseSseTintLayers()

        Dim split As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1}
        split.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 300))   ' 0: layer list
        split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))    ' 1: detail

        ' --- Master list (left) ---
        Dim listHost As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2}
        listHost.RowStyles.Add(New RowStyle(SizeType.Absolute, 22))
        listHost.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        listHost.Controls.Add(New Label With {.Text = "RACE tint layers", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft, .Font = New System.Drawing.Font(Me.Font, System.Drawing.FontStyle.Bold)}, 0, 0)
        _sseTintList = New DoubleBufferedListBox With {.Dock = DockStyle.Fill, .IntegralHeight = False, .DrawMode = DrawMode.OwnerDrawFixed, .ItemHeight = 20}
        RemoveHandler _sseTintList.DrawItem, AddressOf DrawSseTintListItem
        AddHandler _sseTintList.DrawItem, AddressOf DrawSseTintListItem
        AddHandler _sseTintList.SelectedIndexChanged, Sub(s, e) If Not _suspendEvents Then SelectSseTintLayer(_sseTintList.SelectedIndex)
        listHost.Controls.Add(_sseTintList, 0, 1)
        split.Controls.Add(listHost, 0, 0)

        ' --- Detail (right) ---
        _sseTintDetailHost = New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(10, 2, 4, 4)}
        split.Controls.Add(_sseTintDetailHost, 1, 0)
        BuildSseTintDetailControls()

        PanelSseTints.Controls.Clear()
        PanelSseTints.Controls.Add(split)

        ' Fill list
        _suspendEvents = True
        _sseTintList.Items.Clear()
        For i = 0 To _sseTintLayers.Count - 1
            _sseTintList.Items.Add(SseTintRowLabel(i))
        Next
        _suspendEvents = False

        If _sseTintLayers.Count = 0 Then
            _sseTintDetailHost.Controls.Clear()
            _sseTintDetailHost.Controls.Add(New Label With {.Text = "(this race declares no tint layers for this gender)", .AutoSize = True})
            _sseTintSelIndex = -1
            Return
        End If
        Dim sel = If(prevSel >= 0 AndAlso prevSel < _sseTintLayers.Count, prevSel, 0)
        _sseTintList.SelectedIndex = sel   ' fires SelectSseTintLayer
    End Sub

    ''' <summary>Display label for a layer row: mask name (or custom-mask filename with a * marker) + a (default) tag
    ''' when the NPC doesn't author it.</summary>
    Private Function SseTintRowLabel(i As Integer) As String
        Dim t = _sseTintLayers(i)
        Dim baseNm = If(String.IsNullOrEmpty(t.MaskName), $"Tint {t.Index}", t.MaskName)
        Dim nm = If(String.IsNullOrEmpty(t.MaskPathOverride), baseNm, "* " & MaskFileName(t.MaskPathOverride))
        Return nm & If(t.Authored, "", "   (default)")
    End Function

    ''' <summary>Owner-draw the master list: LooksMenu-style colour coding (RED missing mask / BLUE custom mask /
    ''' GRAY unauthored default / normal authored) plus a small swatch of the layer's effective colour.</summary>
    Private Sub DrawSseTintListItem(sender As Object, e As DrawItemEventArgs)
        e.DrawBackground()
        If e.Index < 0 OrElse e.Index >= _sseTintLayers.Count Then Return
        Dim t = _sseTintLayers(e.Index)
        Dim isMissing = Not TintMaskExists(t.MaskPath)
        Dim fore As System.Drawing.Color =
            If(isMissing, System.Drawing.Color.FromArgb(200, 40, 40),
               If(t.Customized, System.Drawing.Color.FromArgb(40, 100, 210),
                  If(Not t.Authored, SystemColors.GrayText, e.ForeColor)))
        Dim swRect As New System.Drawing.Rectangle(e.Bounds.Left + 3, e.Bounds.Top + 3, 22, e.Bounds.Height - 6)
        Using b As New System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(t.R, t.G, t.B))
            e.Graphics.FillRectangle(b, swRect)
        End Using
        e.Graphics.DrawRectangle(System.Drawing.Pens.Gray, swRect)
        Using b As New System.Drawing.SolidBrush(fore)
            e.Graphics.DrawString(SseTintRowLabel(e.Index), e.Font, b, e.Bounds.Left + 30, e.Bounds.Top + 2)
        End Using
        e.DrawFocusRectangle()
    End Sub

    ''' <summary>Build the (empty) detail controls once; SelectSseTintLayer fills them per layer. Two-column grid
    ''' (label + content) so every control and button stays inside the detail panel — never under the preview. The
    ''' two action buttons (Custom… / Clear) share the same right-hand column so they line up (symmetric).</summary>
    Private Sub BuildSseTintDetailControls()
        Const LabelCol As Integer = 128
        Const BtnCol As Integer = 92          ' width of each right-hand action button column
        _sseTintDetailHost.Controls.Clear()

        Dim lay As New TableLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 2, .RowCount = 6}
        lay.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, LabelCol))
        lay.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For r = 0 To 5 : lay.RowStyles.Add(New RowStyle(SizeType.Absolute, 34)) : Next

        ' Row 0: preset dropdown (TIAS)
        lay.Controls.Add(SseTintDetailLabel("Color source:"), 0, 0)
        _sseTintPresetCombo = New ComboBox With {.Dock = DockStyle.Fill, .DropDownStyle = ComboBoxStyle.DropDownList, .DrawMode = DrawMode.OwnerDrawFixed, .Margin = New Padding(3, 4, 3, 3)}
        AddHandler _sseTintPresetCombo.DrawItem, AddressOf DrawSseTintPresetItem
        AddHandler _sseTintPresetCombo.SelectedIndexChanged, AddressOf OnSseTintPresetChanged
        lay.Controls.Add(_sseTintPresetCombo, 1, 0)

        ' Row 1: colour swatch (fills) + Custom… (right column)
        lay.Controls.Add(SseTintDetailLabel("Color:"), 0, 1)
        Dim colorRow As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1, .Margin = New Padding(0)}
        colorRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        colorRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, BtnCol))
        colorRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, BtnCol))
        _sseTintSwatch = New Button With {.Dock = DockStyle.Fill, .Height = 26, .Enabled = False, .FlatStyle = FlatStyle.Popup, .Margin = New Padding(3, 4, 3, 3)}
        colorRow.Controls.Add(_sseTintSwatch, 0, 0)
        _sseTintCustomBtn = New Button With {.Text = "Custom…", .Dock = DockStyle.Fill, .Height = 26, .Margin = New Padding(3, 4, 3, 3)}
        AddHandler _sseTintCustomBtn.Click, AddressOf OnSseTintCustomColor
        _sseTintToolTip.SetToolTip(_sseTintCustomBtn, "Pick a free RGB colour (TIAS = -1 = custom, like RaceMenu / the CK colour picker).")
        colorRow.Controls.Add(_sseTintCustomBtn, 2, 0)   ' rightmost column → aligns with Clear below
        lay.Controls.Add(colorRow, 1, 1)

        ' Row 2: coverage (TINV)
        lay.Controls.Add(SseTintDetailLabel("Coverage (TINV):"), 0, 2)
        _sseTintCoverage = New FO4_Base_Library.TinySliderTextBox With {
            .Minimum = 0.0R, .Maximum = 1.0R, .DisplayFormat = "0.00", .SmallChange = 0.01R, .LargeChange = 0.1R, .Height = 26, .Dock = DockStyle.Fill, .Margin = New Padding(3, 4, 3, 3)}
        AddHandler _sseTintCoverage.ValueChanged, AddressOf OnSseTintCoverageChanged
        AddHandler _sseTintCoverage.DragEnded, AddressOf OnSliderDragEnded
        lay.Controls.Add(_sseTintCoverage, 1, 2)

        ' Row 3: warpaint mask (RaceMenu) — the filename gets the FULL content width so long paths aren't cut off.
        lay.Controls.Add(SseTintDetailLabel("Warpaint mask:"), 0, 3)
        _sseTintMaskLabel = New Label With {.Dock = DockStyle.Fill, .AutoEllipsis = True, .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(3, 0, 3, 0)}
        lay.Controls.Add(_sseTintMaskLabel, 1, 3)

        ' Row 4: mask buttons UNDER the filename (Choose… / Clear, right-aligned → Clear lines up with Custom…).
        Dim maskBtns As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1, .Margin = New Padding(0)}
        maskBtns.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        maskBtns.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, BtnCol))
        maskBtns.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, BtnCol))
        ' col 0 (Percent 100) stays empty → pushes the two buttons to the right (Clear lines up with Custom…).
        _sseTintMaskPick = New Button With {.Text = "Choose…", .Dock = DockStyle.Fill, .Height = 26, .Margin = New Padding(3, 2, 3, 3)}
        AddHandler _sseTintMaskPick.Click, Sub(s, e) OnSseTintTextureClick(_sseTintSelIndex)
        _sseTintToolTip.SetToolTip(_sseTintMaskPick, "Warpaint (RaceMenu): pick a tint mask registered by a mod. Empty = uses the RACE's own mask.")
        maskBtns.Controls.Add(_sseTintMaskPick, 1, 0)
        _sseTintMaskClear = New Button With {.Text = "Clear", .Dock = DockStyle.Fill, .Height = 26, .Margin = New Padding(3, 2, 3, 3)}
        AddHandler _sseTintMaskClear.Click, AddressOf OnSseTintMaskClear
        maskBtns.Controls.Add(_sseTintMaskClear, 2, 0)     ' rightmost → aligns with Custom… above
        lay.Controls.Add(maskBtns, 1, 4)

        ' Row 5: reset to RACE default (in the content column, left-aligned)
        _sseTintResetBtn = New Button With {.Text = "Reset to RACE default", .AutoSize = True, .Height = 28, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 5, 3, 3)}
        AddHandler _sseTintResetBtn.Click, AddressOf OnSseTintResetLayer
        lay.Controls.Add(_sseTintResetBtn, 1, 5)

        _sseTintDetailHost.Controls.Add(lay)
    End Sub

    ''' <summary>A detail-row label, vertically centred against the 34px rows (consistent column start).</summary>
    Private Function SseTintDetailLabel(text As String) As Label
        Return New Label With {.Text = text, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(3, 0, 3, 0)}
    End Function

    ''' <summary>Owner-draw a preset-dropdown item: a swatch of the preset colour + its label.</summary>
    Private Sub DrawSseTintPresetItem(sender As Object, e As DrawItemEventArgs)
        e.DrawBackground()
        Dim cb = TryCast(sender, ComboBox)
        If cb Is Nothing OrElse e.Index < 0 OrElse e.Index >= cb.Items.Count Then Return
        Dim it = TryCast(cb.Items(e.Index), SseTintPresetItem)
        If it Is Nothing Then Return
        Dim swRect As New System.Drawing.Rectangle(e.Bounds.Left + 2, e.Bounds.Top + 2, 20, e.Bounds.Height - 4)
        If it.Tirs >= 0 Then
            Using b As New System.Drawing.SolidBrush(it.Swatch)
                e.Graphics.FillRectangle(b, swRect)
            End Using
            e.Graphics.DrawRectangle(System.Drawing.Pens.Gray, swRect)
        End If
        Using b As New System.Drawing.SolidBrush(e.ForeColor)
            e.Graphics.DrawString(it.Display, e.Font, b, e.Bounds.Left + 26, e.Bounds.Top + 1)
        End Using
        e.DrawFocusRectangle()
    End Sub

    ''' <summary>Fill the detail controls from the selected layer. Preset combo = "(custom RGB)" (Tirs -1) + the RACE
    ''' presets for this layer; selected item = the one whose TIRS == the layer's TIAS, else "(custom RGB)".</summary>
    Private Sub SelectSseTintLayer(i As Integer)
        _sseTintSelIndex = i
        If i < 0 OrElse i >= _sseTintLayers.Count OrElse _sseTintPresetCombo Is Nothing Then Return
        Dim t = _sseTintLayers(i)
        _suspendEvents = True
        Try
            ' Preset dropdown
            _sseTintPresetCombo.Items.Clear()
            _sseTintPresetCombo.Items.Add(New SseTintPresetItem With {.Tirs = -1, .Display = "(custom RGB)", .Swatch = System.Drawing.Color.White})
            Dim selComboIdx As Integer = 0   ' default to custom
            If t.Presets IsNot Nothing Then
                For Each pr In t.Presets
                    Dim rgb = ResolveClfmRgb(pr.Clfm)
                    Dim sw = System.Drawing.Color.FromArgb(rgb.R, rgb.G, rgb.B)
                    Dim nm = ResolveClfmName(pr.Clfm)
                    Dim label = If(String.IsNullOrEmpty(nm), $"Preset {pr.Tirs}", nm)
                    _sseTintPresetCombo.Items.Add(New SseTintPresetItem With {
                        .Tirs = pr.Tirs, .Swatch = sw,
                        .Display = $"{label}   ({rgb.R},{rgb.G},{rgb.B})"})
                    If t.Authored AndAlso t.Tias >= 0 AndAlso pr.Tirs = t.Tias Then selComboIdx = _sseTintPresetCombo.Items.Count - 1
                Next
            End If
            _sseTintPresetCombo.SelectedIndex = selComboIdx

            _sseTintSwatch.BackColor = System.Drawing.Color.FromArgb(t.R, t.G, t.B)
            _sseTintCoverage.Value = Math.Max(0.0R, Math.Min(1.0R, CDbl(t.V)))

            ' Mask row
            Dim raceMask = ResolveRaceLayerMaskPath(t.Index)
            If Not String.IsNullOrEmpty(t.MaskPathOverride) Then
                _sseTintMaskLabel.Text = "★ " & MaskFileName(t.MaskPathOverride)
                _sseTintToolTip.SetToolTip(_sseTintMaskLabel, t.MaskPathOverride)
            Else
                _sseTintMaskLabel.Text = If(String.IsNullOrEmpty(raceMask), "(no mask)", MaskFileName(raceMask) & "  (RACE)")
                _sseTintToolTip.SetToolTip(_sseTintMaskLabel, If(raceMask, ""))
            End If
            _sseTintMaskClear.Enabled = Not String.IsNullOrEmpty(t.MaskPathOverride)
        Finally
            _suspendEvents = False
        End Try
    End Sub

    ''' <summary>Re-draw ONLY the given list row + the detail swatch after a value edit, keeping selection. The
    ''' owner-draw reads live data straight from <c>_sseTintLayers</c> (never the <c>Items()</c> string), so we
    ''' invalidate just that row's rectangle — NOT the whole owner-draw ListBox, which repainted every row on each
    ''' slider tick (the slowness). We also never re-assign Items() (that too invalidates the whole control).</summary>
    Private Sub RefreshSseTintRow(i As Integer)
        If _sseTintList IsNot Nothing AndAlso i >= 0 AndAlso i < _sseTintList.Items.Count Then
            _sseTintList.Invalidate(_sseTintList.GetItemRectangle(i))
        End If
        If _sseTintSwatch IsNot Nothing AndAlso i = _sseTintSelIndex AndAlso i >= 0 AndAlso i < _sseTintLayers.Count Then
            Dim t = _sseTintLayers(i)
            _sseTintSwatch.BackColor = System.Drawing.Color.FromArgb(t.R, t.G, t.B)
        End If
    End Sub

    ''' <summary>Preset dropdown changed. Selecting a RACE preset ⇒ TIAS = its TIRS AND TINC = its CLFM colour
    ''' (the two stay consistent, exactly as vanilla stores them). Selecting "(custom RGB)" ⇒ TIAS = -1 (custom),
    ''' colour unchanged — the user then clicks Custom… to pick. Either way the layer becomes authored.</summary>
    Private Sub OnSseTintPresetChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim i = _sseTintSelIndex
        If i < 0 OrElse i >= _sseTintLayers.Count Then Return
        Dim it = TryCast(_sseTintPresetCombo.SelectedItem, SseTintPresetItem)
        If it Is Nothing Then Return
        Dim t = _sseTintLayers(i)
        If it.Tirs < 0 Then
            ' Custom: keep the colour, mark custom. SSE rule — a custom colour keeps TIAS = -1 even if it matches a
            ' preset (verified: 284 vanilla layers). Never re-derive an index from the colour (that is the FO4 rule).
            t.Tias = -1S
        Else
            t.Tias = CShort(it.Tirs)
            t.R = it.Swatch.R : t.G = it.Swatch.G : t.B = it.Swatch.B   ' TINC = the preset's exact CLFM colour
        End If
        t.Authored = True
        _sseTintLayers(i) = t
        ApplySseTintOverlay()
        RefreshSseTintRow(i)
        ScheduleRefresh(FaceRefreshScope.TexturesOnly)
    End Sub

    ''' <summary>Custom colour picker → TINC = chosen RGB, TIAS = -1 (custom). The combo snaps to "(custom RGB)".</summary>
    Private Sub OnSseTintCustomColor(sender As Object, e As EventArgs)
        Dim i = _sseTintSelIndex
        If i < 0 OrElse i >= _sseTintLayers.Count Then Return
        Using dlg As New ColorDialog()
            dlg.FullOpen = True
            dlg.AnyColor = True
            dlg.Color = System.Drawing.Color.FromArgb(_sseTintLayers(i).R, _sseTintLayers(i).G, _sseTintLayers(i).B)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim t = _sseTintLayers(i)
            t.R = dlg.Color.R : t.G = dlg.Color.G : t.B = dlg.Color.B
            t.Tias = -1S           ' custom RGB — no preset (SSE-faithful: no colour→index re-match)
            t.Authored = True
            _sseTintLayers(i) = t
            _suspendEvents = True
            _sseTintSwatch.BackColor = dlg.Color
            If _sseTintPresetCombo.Items.Count > 0 Then _sseTintPresetCombo.SelectedIndex = 0   ' "(custom RGB)"
            _suspendEvents = False
            ApplySseTintOverlay()
            RefreshSseTintRow(i)
            ScheduleRefresh(FaceRefreshScope.TexturesOnly)
        End Using
    End Sub

    Private Sub OnSseTintCoverageChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim i = _sseTintSelIndex
        If i < 0 OrElse i >= _sseTintLayers.Count Then Return
        Dim t = _sseTintLayers(i)
        t.V = CSng(_sseTintCoverage.Value) : t.Authored = True
        _sseTintLayers(i) = t
        ApplySseTintOverlay()
        RefreshSseTintRow(i)
        ScheduleRefresh(FaceRefreshScope.TexturesOnly)
    End Sub

    ''' <summary>Clear the RaceMenu warpaint mask override on the selected layer (revert to the RACE's own mask).</summary>
    Private Sub OnSseTintMaskClear(sender As Object, e As EventArgs)
        Dim i = _sseTintSelIndex
        If i < 0 OrElse i >= _sseTintLayers.Count Then Return
        Dim t = _sseTintLayers(i)
        If String.IsNullOrEmpty(t.MaskPathOverride) Then Return
        t.MaskPathOverride = Nothing
        t.MaskPath = ResolveRaceLayerMaskPath(t.Index)
        t.Customized = False
        _sseTintLayers(i) = t
        ApplySseTintOverlay()
        SelectSseTintLayer(i)   ' refresh mask label + custom flag
        RefreshSseTintRow(i)
        ScheduleRefresh(FaceRefreshScope.TexturesOnly)
    End Sub

    ''' <summary>Revert the selected layer to the RACE default: unauthored (dropped from the emitted record), colour
    ''' + coverage back to the RACE default preset, and the warpaint mask override cleared.</summary>
    Private Sub OnSseTintResetLayer(sender As Object, e As EventArgs)
        Dim i = _sseTintSelIndex
        If i < 0 OrElse i >= _sseTintLayers.Count Then Return
        Dim t = _sseTintLayers(i)
        Dim rgb = ResolveClfmRgb(t.DefaultClfm)
        t.R = rgb.R : t.G = rgb.G : t.B = rgb.B
        t.V = t.DefaultValue
        t.Tias = 0S
        t.Authored = False
        t.MaskPathOverride = Nothing
        _sseTintLayers(i) = t
        ApplySseTintOverlay()
        SelectSseTintLayer(i)
        RefreshSseTintRow(i)
        ScheduleRefresh(FaceRefreshScope.TexturesOnly)
    End Sub

    ''' <summary>Normalise a warpaint path to the textures-relative form the RACE TINT convention (and the composer)
    ''' expect: backslashes, no leading <c>textures\</c>. Warpaint registrations are usually already in this form
    ''' (e.g. <c>Actors\Character\...\x.dds</c>), but a mod may prefix it.</summary>
    Private Function NormalizeTintMaskRel(p As String) As String
        If String.IsNullOrEmpty(p) Then Return p
        Dim s = p.Replace("/"c, "\"c)
        If s.StartsWith("textures\", StringComparison.OrdinalIgnoreCase) Then s = s.Substring("textures\".Length)
        Return s.TrimStart("\"c)
    End Function

    ''' <summary>Set/clear a RaceMenu WARPAINT (custom tint mask) for a tint layer. Faithful to
    ''' PresetInterface.cpp:203 (tintMask-&gt;texture-&gt;str = tint.name): the path overrides the RACE layer's own TINT
    ''' mask by index and is composited as a red-channel mask (render + bake) + persisted (.bssliders + .jslot).
    ''' The texture is chosen from RaceMenu's named WARPAINT list — the union of mods' <c>AddWarpaint(name,path)</c>
    ''' registrations (RaceMenuPaintCatalog) — NOT a file browser: RaceMenu never lets you browse a .dds. Picking a
    ''' warpaint the app couldn't otherwise resolve is what fixes the black-face case. The stored path is
    ''' textures-relative (no <c>textures\</c> prefix), matching the RACE TINT convention the composer expects.</summary>
    Private Sub OnSseTintTextureClick(idx As Integer)
        If idx < 0 OrElse idx >= _sseTintLayers.Count Then Return
        Dim t0 = _sseTintLayers(idx)
        ' Pre-select the effective mask for this layer: the current override if any, else the RACE layer's own path.
        Dim curRel As String = If(Not String.IsNullOrEmpty(t0.MaskPathOverride), t0.MaskPathOverride, ResolveRaceLayerMaskPath(t0.Index))

        Dim res = SseCatalogs.PickPaint(Me, RaceMenuPaintCatalog.PaintCategory.Warpaint, curRel, allowNone:=True)
        If res.Kind = SseCatalogs.PaintPickKind.Cancel Then Return
        Dim chosenRel As String = Nothing
        Dim cleared As Boolean = (res.Kind = SseCatalogs.PaintPickKind.Clear)
        If Not cleared Then chosenRel = NormalizeTintMaskRel(res.Entry.Path)

        Dim t = _sseTintLayers(idx)
        Dim raceMask = ResolveRaceLayerMaskPath(t.Index)
        ' A pick equal to the RACE default mask = no override (keeps the .jslot/sidecar clean).
        If cleared OrElse String.Equals(chosenRel, raceMask, StringComparison.OrdinalIgnoreCase) Then
            t.MaskPathOverride = Nothing
        Else
            t.MaskPathOverride = chosenRel
        End If
        ' Recompute effective mask path + BLUE "custom mask" flag without a full re-parse (keeps selection).
        t.MaskPath = If(Not String.IsNullOrEmpty(t.MaskPathOverride), t.MaskPathOverride, raceMask)
        t.Customized = Not String.IsNullOrEmpty(t.MaskPathOverride) AndAlso
                       Not String.Equals(t.MaskPathOverride, raceMask, StringComparison.OrdinalIgnoreCase)
        _sseTintLayers(idx) = t
        ApplySseTintOverlay()
        SelectSseTintLayer(idx)   ' refresh mask label + clear-button state
        RefreshSseTintRow(idx)    ' relabel the row (override filename / marker) + swatch
        ScheduleRefresh(FaceRefreshScope.TexturesOnly)
    End Sub

    ''' <summary>The RACE tint layer's own mask texture path (textures-relative, no prefix) for a given layer index,
    ''' from the parsed RACE tint layers. Empty when the race has no such layer. Used to pre-select the picker and to
    ''' detect "picked the default" (→ no override).</summary>
    Private Function ResolveRaceLayerMaskPath(layerIndex As Integer) As String
        Dim layers = SseFaceTintComposer.GetRaceLayersOrdered(_pluginManager, _raceFormID, _isFemale)
        If layers IsNot Nothing Then
            For Each lay In layers
                If lay.Index = layerIndex Then Return If(lay.Path, "")
            Next
        End If
        Return ""
    End Function

    ''' <summary>Rebuild the SseTintRaw list from the edited layers and push it into the overlay preset so the
    ''' composer (render + bake) uses it and Save ESP emits it.</summary>
    Private Sub ApplySseTintOverlay()
        Dim p = Preset
        If p Is Nothing Then Return
        ' Emit ONLY authored layers (the engine composes RACE default for the rest) — matches how the NPC record
        ' stores tints and keeps the write minimal/faithful. RACE order preserved (list is in RACE order).
        Dim outList As New List(Of NPC_RawSubrecord)
        ' RaceMenu-only per-layer custom mask texture map (index → path). A custom mask can ride on a layer even
        ' when its colour is the RACE default, so it is emitted independently of the Authored gate below.
        Dim texMap As Dictionary(Of Integer, String) = Nothing
        For Each t In _sseTintLayers
            If Not String.IsNullOrEmpty(t.MaskPathOverride) Then
                If texMap Is Nothing Then texMap = New Dictionary(Of Integer, String)
                texMap(t.Index) = t.MaskPathOverride
            End If
            If Not t.Authored Then Continue For
            outList.Add(New NPC_RawSubrecord With {.Sig = "TINI", .Data = BitConverter.GetBytes(CUShort(t.Index))})
            outList.Add(New NPC_RawSubrecord With {.Sig = "TINC", .Data = New Byte() {t.R, t.G, t.B, t.A}})
            outList.Add(New NPC_RawSubrecord With {.Sig = "TINV", .Data = BitConverter.GetBytes(CUInt(Math.Max(0, Math.Round(t.V * 100))))})
            outList.Add(New NPC_RawSubrecord With {.Sig = "TIAS", .Data = BitConverter.GetBytes(t.Tias)})
        Next
        p.SseTintRawOverride = outList
        p.HasSseTints = True
        p.SseTintTexOverride = texMap
    End Sub

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
                ' This list is a COMPLETE superset of the raw PNAM (extras included — see the
                ' "We do NOT filter by IsExtra here" note above). Mark it so Save treats it as
                ' authoritative and does NOT union the raw record back in — otherwise a freestanding
                ' Misc the user later removes (an orphan hairline) gets resurrected from raw.
                p.HeadPartFormIDsIncludeRawExtras = True
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

            ' --- Tints --- (FO4 FaceTintLayers TETI/TEND. SSE usa el tab "Tints (SSE)" con SseTintRaw
            ' TINI/TINC/TINV, sembrado por LoadSseTintValues; el tab FO4 esta oculto y sus controles no aplican.)
            If Not _isSSE Then
                ' Mark as "present in this preset" the moment the editor takes ownership: from now
                ' on the user's edits (including deletions) authoritatively define the field. If the
                ' overlay was empty, seed it from the raw NPC so the user sees the current state to edit.
                If p.FaceTintLayers.Count = 0 AndAlso rawNpc IsNot Nothing AndAlso rawNpc.FaceTintLayers.Count > 0 Then
                    For Each tl In rawNpc.FaceTintLayers
                        p.FaceTintLayers.Add(CloneFaceTint(tl))
                    Next
                End If
                p.HasFaceTintLayers = True
                RefreshTintsList()
            End If

            ' --- Vertex morphs (MSDK/MSDV) + Face bone regions (FMRI/FMRS): FO4-only. SSE usa el tab
            ' "Morphs (SSE)" (NAM9/NAMA), sembrado por LoadSseMorphValues; sus controles FO4 no existen. ---
            If Not _isSSE Then
                If p.ChargenFaceMorphs.Count = 0 AndAlso rawNpc IsNot Nothing AndAlso rawNpc.MorphValues.Count > 0 Then
                    For Each kv In rawNpc.MorphValues
                        p.ChargenFaceMorphs(kv.Key) = kv.Value
                    Next
                End If
                p.HasChargenFaceMorphs = True
                BuildMorphGroupRows()
                LoadMorphGroupValues()

                If p.FaceBoneRegions.Count = 0 AndAlso rawNpc IsNot Nothing AndAlso rawNpc.FaceMorphs.Count > 0 Then
                    For Each fm In rawNpc.FaceMorphs
                        p.FaceBoneRegions(fm.Index) = fm.Values.ToArray()
                    Next
                End If
                p.HasFaceBoneRegions = True
                LoadBoneRegionValues()
            End If

            ' --- FMIN ---
            ' If there's no prior overlay (NPC never touched by LM or by a prior Edit Face), seed
            ' the slider from the raw NPC record so it reflects the actual current value (records
            ' authored at 1.4 / 0.7 / etc. shouldn't snap to 1.0 just because the editor opened).
            ' If an overlay DOES exist (LM preset load or prior edit), trust p.FacialMorphIntensity
            ' verbatim — 1.0F is a valid explicit value per LM contract (omitted key parses to 1.0
            ' identical to an explicit "Intensity":1.0), NOT a "default sentinel" we can overwrite.
            ' Prior heuristic (Math.Abs(p.FMIN - 1.0F) < epsilon → fallback to raw) wrongly clobbered
            ' LM presets that explicitly carried FMIN=1.0.
            ' FMIN is a Fallout 4 subrecord with no Skyrim analogue and its host tab (Bone Regions) is removed
            ' for SSE — don't seed the orphaned control or write the FO4-only channel into an SSE preset.
            If Not _isSSE Then
                If Not _hadPriorOverlay AndAlso rawNpc IsNot Nothing Then
                    p.FacialMorphIntensity = If(rawNpc.FacialMorphIntensity > 0.0F, rawNpc.FacialMorphIntensity, 1.0F)
                End If
                TrackBarFmin.Value = p.FacialMorphIntensity
            End If
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
        ComboBoxTintPalette.DrawMode = DrawMode.OwnerDrawFixed
        AddHandler ComboBoxTintPalette.DrawItem, AddressOf DrawTintPaletteItem
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
                ' NPC already has "Eyes green" replaces the green entry. We do NOT re-ADD the new
                ' parent's HNAM extras — the render's recursive HNAM walk in CollectHeadPartCandidate
                ' (MainForm.vb:6782) is the single source of truth for which addons attach to which
                ' parent, and duplicating that walk to add extras produced parent-swap bugs. But we
                ' DO cascade-REMOVE the OLD parent's now-orphaned standalone Misc children below
                ' (symmetric with OnRemoveHeadPart) — otherwise replacing a hair leaves its old
                ' hairline as a Misc root with no palette.
                Dim existingIdx = p.HeadPartFormIDs.FindIndex(Function(fid)
                                                                  Dim hd As HDPT_Data = Nothing
                                                                  Return _allHeadPartsByFid.TryGetValue(fid, hd) AndAlso hd.PartType = partType
                                                              End Function)
                If existingIdx >= 0 Then
                    Dim oldParentFid = p.HeadPartFormIDs(existingIdx)
                    p.HeadPartFormIDs(existingIdx) = newFid
                    ' Set the new parent FIRST so its own HNAM protects any Misc the old and new
                    ' parent share (a hairline declared by both stays a live HNAM child).
                    If oldParentFid <> newFid Then CascadeRemoveOrphanedHnamMisc(p.HeadPartFormIDs, oldParentFid)
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

        ' Cascade-clean the removed parent's orphaned standalone Misc children. Vanilla NPC.PNAM
        ' frequently lists a hairline both in the parent's HNAM and standalone in PNAM; once the
        ' parent is gone the standalone becomes an orphan Misc root that breaks the render. Shared
        ' with the Add/replace path so both behave identically.
        CascadeRemoveOrphanedHnamMisc(p.HeadPartFormIDs, tag.FormID)

        RefreshHeadPartsList()
        _refresh?.Invoke(FaceRefreshScope.FullReload)
    End Sub

    ''' <summary>Cascade-remove the now-orphaned standalone Misc children of a parent head part that
    ''' was just removed OR replaced. A standalone Misc entry that lived in
    ''' <paramref name="removedParentFid"/>'s ExtraPartFormIDs (HNAM) becomes an orphan once its
    ''' parent is gone: its effective type collapses to Misc(0), no hair/beard palette applies, and it
    ''' renders with the BGSM-default colour while showing as a Misc root row. We drop those — EXCEPT
    ''' any extra still claimed by another parent currently in <paramref name="headParts"/> (this
    ''' includes the replacement parent on the Add path, so a hairline shared by the old and new hair
    ''' survives as a live HNAM child). Non-Misc extras, and Misc the user added as independent addons
    ''' (mouth shadow, AO/wet — not in the removed parent's HNAM), are left untouched.</summary>
    ''' <summary>Thin wrapper over the shared <see cref="HeadPartResolver.CascadeRemoveOrphanedHnamMisc"/>
    ''' (single source of truth, also used by NpcOverrideSaver's preset-load orphan suppression) resolving
    ''' HDPTs through this form's <c>_allHeadPartsByFid</c> cache. Behaviour is unchanged from the former
    ''' private implementation.</summary>
    Private Sub CascadeRemoveOrphanedHnamMisc(headParts As List(Of UInteger), removedParentFid As UInteger)
        HeadPartResolver.CascadeRemoveOrphanedHnamMisc(headParts, removedParentFid,
            Function(fid As UInteger) As HDPT_Data
                Dim hd As HDPT_Data = Nothing
                _allHeadPartsByFid.TryGetValue(fid, hd)
                Return hd
            End Function)
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
            raw = NpcMaterialResolver.ResolveHairPaletteTexture(host, host.LastRenderedState, _pluginManager)
        End If
        ' TRANSIENT: ResolveHairPaletteTexture returns "" while the host/state (and the hair mesh
        ' material it samples) aren't loaded yet — a construction-time paint can hit this before the
        ' first render populates host.PreviewCtl.Model. Do NOT latch _hairPaletteResolveAttempted here;
        ' the palette may resolve on a later Paint once the host is ready.
        If String.IsNullOrEmpty(raw) Then Return
        Dim chosen = FO4UnifiedMaterial_Class.CorrectTexturePath(raw)
        ' A resolved path that isn't in FilesDictionary: only latch as TERMINAL once the render model
        ' is loaded, so ResolveHairPaletteTexture has had its BGSM-first chance. Before the model is up,
        ' the RACE fallback returns a (possibly non-dictionary) lookup path that the per-mesh BGSM path
        ' would supersede on a later Paint — latching here would leave the swatch permanently blank.
        ' Treat the model-not-ready case as TRANSIENT and retry; latch only when the model is loaded and
        ' the path is genuinely absent.
        If chosen = "" OrElse Not FilesDictionary_class.Dictionary.ContainsKey(chosen) Then
            Dim modelReady = host IsNot Nothing AndAlso host.PreviewCtl IsNot Nothing AndAlso host.PreviewCtl.Model IsNot Nothing
            If modelReady Then _hairPaletteResolveAttempted = True
            Return
        End If
        Try
            Dim loc As FilesDictionary_class.File_Location = Nothing
            If Not FilesDictionary_class.Dictionary.TryGetValue(chosen, loc) Then
                _hairPaletteResolveAttempted = True   ' TERMINAL: ContainsKey passed but lookup failed; not transitory
                Return
            End If
            Dim ddsBytes = loc.GetBytes()
            If ddsBytes Is Nothing OrElse ddsBytes.Length = 0 Then
                _hairPaletteResolveAttempted = True   ' TERMINAL: empty/unreadable DDS bytes; stop retrying
                Return
            End If
            ' Decode DDS level 0 into a managed Bitmap via the shared library API (same
            ' Loader.ConvertForBitmap path, with pixel copy + level-data release handled there).
            ' Returns Nothing on decode failure / bad level — treated as terminal below.
            Dim decoded = DirectXDDSLoader.CreateBitmapFromDDS(ddsBytes)
            If decoded Is Nothing Then
                _hairPaletteResolveAttempted = True   ' TERMINAL: decode-side failure is not transitory; stop retrying
                Return
            End If
            _hairPaletteBitmap = decoded
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
            Dim merged = FaceTintInputBuilder.MergeTintLayersWithRaceDefaults(p.FaceTintLayers, _race, _isFemale, _pluginManager)
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
                Dim optForTag = _race?.FindTintOption(tl.Index, _isFemale)
                ' Missing tint (2026-07-09): an NPC-authored layer whose Index doesn't resolve against
                ' this race (e.g. a LooksMenu custom tint whose mod isn't installed) is SHOWN but tagged
                ' "Missing". It stays inert: preserved verbatim in p.FaceTintLayers (round-trips on Save),
                ' the compositor skips it (FindTintOption Nothing) so it never paints, and UpdateTintDetail
                ' keeps its editors disabled. Race-default rows always resolve, so this only tags orphan
                ' NPC-authored layers.
                Dim isMissing As Boolean = Not entry.Merged.IsRaceDefault AndAlso optForTag Is Nothing
                If isMissing Then
                    Dim idxLocal = tl.Index
                    Logger.LogLazy(Function() $"[LMLoad] Face editor shows tint layer Index {idxLocal} as MISSING (no option in race for this gender) — preserved verbatim, not applied.")
                    grp = "Missing"
                End If
                Dim isCustomLm As Boolean = optForTag IsNot Nothing AndAlso optForTag.IsCustomLm
                If isCustomLm Then layerName &= "  [LM]"
                If isMissing Then layerName &= " (MISSING — not applied)"
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
                If isMissing Then
                    row.ForeColor = Color.FromArgb(180, 60, 40)   ' missing/orphan tint accent (muted red)
                ElseIf entry.Merged.IsRaceDefault Then
                    row.ForeColor = SystemColors.GrayText
                ElseIf isCustomLm Then
                    row.ForeColor = Color.FromArgb(40, 90, 200)   ' LM custom tint accent
                End If
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
        If opt Is Nothing Then Return $"(missing tint #{tl.Index})"
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
                ' PRIORIDAD: el índice del layer manda. El editor muestra la entrada que apunta
                ' tl.TemplateColorIndex (consistente con el render, que matchea por TemplateIndex); el
                ' match por color es SOLO fallback (custom genuino si ni el índice ni el color matchean).
                ' Sin esto, un layer con índice válido pero color no-exacto al CLFM quedaba en "(custom RGB)".
                Dim indexMatchIdx As Integer = -1
                Dim colorMatchIdx As Integer = -1
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
                    Dim thisComboIdx = ComboBoxTintPalette.Items.Count - 1
                    ' PRIORIDAD 1: el índice del layer (si está) → esta entrada, sin re-matchear por color.
                    If tl.TemplateColorIndex >= 0 AndAlso CInt(tplCol.TemplateIndex) = tl.TemplateColorIndex Then
                        indexMatchIdx = thisComboIdx
                    End If
                    ' PRIORIDAD 2 (fallback): primera entrada cuyo CLFM RGB matchea exacto el color del layer.
                    If colorMatchIdx < 0 AndAlso clfm IsNot Nothing AndAlso clfm.HasColor _
                       AndAlso clfm.Color.R = tl.Color.R _
                       AndAlso clfm.Color.G = tl.Color.G _
                       AndAlso clfm.Color.B = tl.Color.B Then
                        colorMatchIdx = thisComboIdx
                    End If
                Next
                ' Índice gana; si no, color; si ninguno, 0 = "(custom RGB)".
                ComboBoxTintPalette.SelectedIndex = If(indexMatchIdx >= 0, indexMatchIdx, Math.Max(0, colorMatchIdx))
            End If

            ' Force alpha=255: tl.Color can carry alpha=0 (RACE-default seeded from CLFM bytes
            ' whose engine-unused alpha vanilla often leaves at 0; same in LM-loaded layers).
            ' WinForms Panel.BackColor with alpha<255 renders as parent fill — the combo path
            ' already forces opaque at the TintPaletteItem construction (line 1381), which is
            ' why changing the combo lit the swatch but list-selection didn't.
            ' TextureSet and Mask entries: leave swatch blank (no visual preview here).
            PanelTintColorSwatch.BackColor = If(isPalette, Color.FromArgb(255, tl.Color.R, tl.Color.G, tl.Color.B), SystemColors.Control)

            ' A missing tint (opt Is Nothing, not a race default) is read-only: it can't be
            ' composited so there's nothing meaningful to edit. It stays in p.FaceTintLayers
            ' verbatim regardless. Palette/RGB are already gated off by isPalette above.
            TrackBarTintPercent.Enabled = (opt IsNot Nothing)
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

    ''' <summary>Owner-draw a FO4 tint-palette combo item: a swatch of the preset colour + its label (like the SSE
    ''' preset dropdown). The "(custom RGB)" entry draws no swatch.</summary>
    Private Sub DrawTintPaletteItem(sender As Object, e As DrawItemEventArgs)
        e.DrawBackground()
        Dim cb = TryCast(sender, ComboBox)
        If cb Is Nothing OrElse e.Index < 0 OrElse e.Index >= cb.Items.Count Then Return
        Dim it = TryCast(cb.Items(e.Index), TintPaletteItem)
        If it Is Nothing Then Return
        Dim textLeft As Integer = e.Bounds.Left + 4
        If Not it.IsCustom AndAlso Not it.SwatchColor.IsEmpty Then
            Dim swRect As New System.Drawing.Rectangle(e.Bounds.Left + 2, e.Bounds.Top + 2, 20, e.Bounds.Height - 4)
            Using b As New System.Drawing.SolidBrush(it.SwatchColor)
                e.Graphics.FillRectangle(b, swRect)
            End Using
            e.Graphics.DrawRectangle(System.Drawing.Pens.Gray, swRect)
            textLeft = e.Bounds.Left + 26
        End If
        Using b As New System.Drawing.SolidBrush(e.ForeColor)
            e.Graphics.DrawString(If(it.Display, ""), e.Font, b, textLeft, e.Bounds.Top + 1)
        End Using
        e.DrawFocusRectangle()
    End Sub

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
        ' Canonical truth is the layer's RGB; the index is always re-derived from the colour via the
        ' single resolver (alpha-closest to opacity among equal-colour presets), identical to Save.
        ' Picking a swatch sets the colour; the resolver then picks the matching preset, so two
        ' presets sharing a colour collapse to the same index the Save path would compute.
        tl.Color = it.SwatchColor
        Dim optPick = _race?.FindTintOption(tl.Index, _isFemale)
        If optPick IsNot Nothing Then
            tl.TemplateColorIndex = FaceTintInputBuilder.ResolveTemplateColorIndex(tl.Color, tl.Value / 100.0F, optPick, _pluginManager)
        Else
            tl.TemplateColorIndex = CInt(it.TemplateIndex)
        End If
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
            ' Re-derive the index from the new colour through the single resolver, identical to Save:
            ' a palette preset whose CLFM RGB matches → that preset (alpha-closest to opacity among
            ' equal-colour presets); no match → -1 (custom). Keeps live preview and Save consistent.
            Dim optCustom = _race?.FindTintOption(tl.Index, _isFemale)
            If optCustom IsNot Nothing Then
                tl.TemplateColorIndex = FaceTintInputBuilder.ResolveTemplateColorIndex(tl.Color, tl.Value / 100.0F, optCustom, _pluginManager)
            End If
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
        Return _race?.MorphValues?.FirstOrDefault(Function(mv) mv.Index = key)
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

    ''' <summary>Build the bone-regions editor as a TabControl. Tabs come from a data-derived
    ''' group chain (AssociatedMorphGroup → else the Name prefix before " - " → else leftovers
    ''' bucketed by shared bone-set and labelled by their common Name prefix). Inside each tab,
    ''' regions that drive the same bone-set are collapsed into one card (GroupBox) that stacks
    ''' each member's LIVE axes only. Bone-less placeholder regions and axes whose range equals
    ''' the region Default (render no-ops) are hidden. All derived from the active race+gender
    ''' JSON — no hardcoded region IDs, names or bone lists.</summary>
    Private Sub BuildBoneRegionsUI()
        BoneRegionsContainer.Controls.Clear()
        _regionBars.Clear()

        Dim regionsFile = NpcMorphPoseResolver.GetFacialBoneRegionsForRace(_race, _isFemale)
        If regionsFile Is Nothing OrElse regionsFile.Regions Is Nothing OrElse regionsFile.Regions.Count = 0 Then
            Dim empty As New Label() With {
                .Text = $"No FacialBoneRegions JSON for {_race?.EditorID}/{(If(_isFemale, "Female", "Male"))}.",
                .AutoSize = True, .ForeColor = Color.Gray, .Padding = New Padding(8)}
            BoneRegionsContainer.Controls.Add(empty)
            Return
        End If

        ' Build tabs from a data-derived group chain, then collapse same-bone regions into one
        ' card per tab. Everything is derived from the active race+gender JSON (no hardcoded IDs,
        ' names or bone lists). See TabGroupForRegion / BuildBoneCard.
        Dim grouped As New Dictionary(Of String, List(Of FacialBoneRegion))(StringComparer.OrdinalIgnoreCase)
        Dim groupOrder As New List(Of String)
        Dim addToGroup As Action(Of String, FacialBoneRegion) =
            Sub(g As String, rd As FacialBoneRegion)
                Dim list As List(Of FacialBoneRegion) = Nothing
                If Not grouped.TryGetValue(g, list) Then
                    list = New List(Of FacialBoneRegion)
                    grouped(g) = list
                    groupOrder.Add(g)
                End If
                list.Add(rd)
            End Sub

        ' Pass 1: AssociatedMorphGroup, else the Name prefix before " - ". Regions with no bones
        ' are skipped (placeholder menu separators shipped by some sculpt mods are render no-ops;
        ' vanilla has none). Leftovers (no group, no " - ") go to pass 2.
        Dim leftovers As New List(Of FacialBoneRegion)
        For Each rd As FacialBoneRegion In regionsFile.Regions.Values.OrderBy(Function(r) r.Name)
            If rd.Bones.Count = 0 Then Continue For
            Dim g As String = TabGroupForRegion(rd)
            If g Is Nothing Then leftovers.Add(rd) Else addToGroup(g, rd)
        Next

        ' Pass 2: bucket the leftovers by shared bone-set, labelled by the common Name prefix, so
        ' nothing falls into a generic "Other" (e.g. the mod's group-less "Nose Side *" set). The
        ' "Other" name only appears as a last resort for unknown data with no shared prefix.
        For Each bsGroup In leftovers.GroupBy(Function(r) RegionBoneSetKey(r))
            Dim members = bsGroup.ToList()
            Dim label As String = CommonNamePrefix(members.Select(Function(r) r.Name))
            If String.IsNullOrEmpty(label) Then label = "Other"
            For Each rd In members
                addToGroup(label, rd)
            Next
        Next

        ' Tabs alphabetical, any "Other" (unknown-data fallback) last.
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
            ' No bone-set collapse: one full single-member card per region (sorted by Name). Keeps
            ' vanilla cards exactly as in vanilla; the user opted out of collapse since a mod's extra
            ' regions can't be told apart from vanilla without the vanilla baseline (BA2 diff / RACE).
            For Each rd As FacialBoneRegion In grouped(groupName).OrderBy(Function(r) r.Name)
                Dim card = BuildBoneCard(rd.Name, New List(Of FacialBoneRegion) From {rd})
                If card IsNot Nothing Then flow.Controls.Add(card)
            Next
            page.Controls.Add(flow)
            tabs.TabPages.Add(page)
        Next
        BoneRegionsContainer.Controls.Add(tabs)
    End Sub

    ''' <summary>Tab group for a region: AssociatedMorphGroup if present, else the Name prefix
    ''' before " - " (vanilla leaves some regions — Eyebrows, Jowls, Nose-Ridge — without a
    ''' MorphGroup but their Name carries the feature). Returns Nothing when neither applies so
    ''' the caller can bucket the region by shared bone-set. Data-derived; no hardcoded names.</summary>
    Private Shared Function TabGroupForRegion(rd As FacialBoneRegion) As String
        If Not String.IsNullOrEmpty(rd.AssociatedMorphGroup) Then Return rd.AssociatedMorphGroup
        Dim n As String = If(rd.Name, "")
        Dim idx As Integer = n.IndexOf(" - ", StringComparison.Ordinal)
        If idx > 0 Then Return n.Substring(0, idx)
        Return Nothing
    End Function

    ''' <summary>Stable key for the set of bones a region drives (sorted, case-insensitive).
    ''' Regions sharing it are variants of one physical control and collapse into one card.</summary>
    Private Shared Function RegionBoneSetKey(rd As FacialBoneRegion) As String
        Return String.Join("|", rd.Bones.Select(Function(b) b.Bone).
                                 OrderBy(Function(nm) nm, StringComparer.OrdinalIgnoreCase))
    End Function

    ''' <summary>Longest common leading word-sequence of the given names ("" if none). Used to
    ''' label a collapsed card / leftover bucket from its members' Names (e.g. "Forehead",
    ''' "Nose Side").</summary>
    Private Shared Function CommonNamePrefix(names As IEnumerable(Of String)) As String
        Dim lists = names.Where(Function(n) Not String.IsNullOrEmpty(n)).
                          Select(Function(n) n.Split(" "c)).ToList()
        If lists.Count = 0 Then Return ""
        Dim minLen As Integer = lists.Min(Function(w) w.Length)
        Dim out As New List(Of String)
        For i = 0 To minLen - 1
            Dim idx As Integer = i
            Dim w As String = lists(0)(idx)
            If lists.All(Function(arr) String.Equals(arr(idx), w, StringComparison.Ordinal)) Then
                out.Add(w)
            Else
                Exit For
            End If
        Next
        Return String.Join(" ", out)
    End Function

    ''' <summary>Which of the 7 FMRS components (PosX/Y/Z, RotX/Y/Z, Scale) can produce a non-zero
    ''' bone delta. An axis is live iff Maxima or Minima differ from the region's Default on that
    ''' axis — that is exactly LerpFmrs (max-default / min-default, FaceBonePoseBuilder.vb). Comparing
    ''' against 0 would mis-handle a non-zero Default (hide a live axis, or show a dead min=max=default
    ''' one). Component 6 (Scale) drives all three scale axes from the single FMRS scale value, so it
    ''' is live if any scale axis ranges off its Default.</summary>
    Private Shared Function RegionLiveComponents(rd As FacialBoneRegion) As Boolean()
        Dim live(6) As Boolean
        Dim dp = rd.DefaultPosition, dr = rd.DefaultRotation, ds = rd.DefaultScale
        For Each b In rd.Bones
            If b.MinimaPosition.X <> dp.X OrElse b.MaximaPosition.X <> dp.X Then live(0) = True
            If b.MinimaPosition.Y <> dp.Y OrElse b.MaximaPosition.Y <> dp.Y Then live(1) = True
            If b.MinimaPosition.Z <> dp.Z OrElse b.MaximaPosition.Z <> dp.Z Then live(2) = True
            If b.MinimaRotation.X <> dr.X OrElse b.MaximaRotation.X <> dr.X Then live(3) = True
            If b.MinimaRotation.Y <> dr.Y OrElse b.MaximaRotation.Y <> dr.Y Then live(4) = True
            If b.MinimaRotation.Z <> dr.Z OrElse b.MaximaRotation.Z <> dr.Z Then live(5) = True
            If b.MinimaScale.X <> ds.X OrElse b.MaximaScale.X <> ds.X OrElse
               b.MinimaScale.Y <> ds.Y OrElse b.MaximaScale.Y <> ds.Y OrElse
               b.MinimaScale.Z <> ds.Z OrElse b.MaximaScale.Z <> ds.Z Then live(6) = True
        Next
        Return live
    End Function

    Private Shared Function AxisName(componentIdx As Integer) As String
        Select Case componentIdx
            Case 0 : Return "Position X"
            Case 1 : Return "Position Y"
            Case 2 : Return "Position Z"
            Case 3 : Return "Rotation X"
            Case 4 : Return "Rotation Y"
            Case 5 : Return "Rotation Z"
            Case Else : Return "Scale"
        End Select
    End Function

    ''' <summary>Label distinguishing a member region inside a collapsed card: its Name with the
    ''' shared card prefix removed ("Forehead Extended" → "Extended"); "Base" when the member IS
    ''' the prefix.</summary>
    Private Shared Function VariantLabel(regionName As String, cardLabel As String) As String
        Dim n As String = If(regionName, "")
        If Not String.IsNullOrEmpty(cardLabel) AndAlso n.StartsWith(cardLabel, StringComparison.Ordinal) Then
            Dim rest As String = n.Substring(cardLabel.Length).Trim()
            Return If(rest.Length > 0, rest, "Base")
        End If
        Return n
    End Function

    ''' <summary>Add a full-width bold, centered header row. When <paramref name="isBand"/> (a member
    ''' separator inside a collapsed multi-region card) it gets a light-grey background so the members
    ''' read as distinct blocks; otherwise it's a plain section header (Translation / Rotation / Scale).</summary>
    Private Sub AddCardHeaderRow(layout As TableLayoutPanel, text As String, isBand As Boolean,
                                 ByRef row As Integer, ByRef contentH As Integer, hdrH As Integer)
        Dim h As New Label() With {.Text = text, .AutoSize = False,
            .Font = New Font(Me.Font, FontStyle.Bold), .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleCenter}
        If isBand Then
            h.BackColor = SystemColors.ControlDark
            h.ForeColor = SystemColors.ControlLightLight
        End If
        layout.RowCount = row + 1
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, hdrH))
        layout.SetColumnSpan(h, 2)
        layout.Controls.Add(h, 0, row)
        row += 1
        contentH += hdrH
    End Sub

    ''' <summary>Build one card (GroupBox) for the regions that share a bone-set. Title is the
    ''' precomputed common-Name label; tooltip carries the bone name(s) and FMRI index. Each member
    ''' shows ALL 7 axes under Translation / Rotation / Scale headers; axes that are no-ops for the
    ''' region (min/max == Default) are rendered DISABLED rather than omitted, so every single-member
    ''' card has the same size. With more than one member each member also gets a bold variant
    ''' sub-header. Returns Nothing if the set is empty or every member is a render no-op.</summary>
    Private Function BuildBoneCard(cardLabel As String, members As List(Of FacialBoneRegion)) As Control
        If members Is Nothing OrElse members.Count = 0 Then Return Nothing
        ' RowH ≥ TinySliderTextBox.MinimumSize.Height (24): a shorter row pins the slider at its min
        ' while the reset button shrinks, so they stop matching. The button height equals the slider's
        ' inner textbox height (22) so the two line up at the top.
        Const RowH As Integer = 26
        Const HdrH As Integer = 18
        Dim showSub As Boolean = members.Count > 1

        ' FMRS component layout (binary spec, not game data): Translation = PosX/Y/Z (0..2),
        ' Rotation = RotX/Y/Z (3..5), Scale = a single slider (6, drives all 3 scale axes).
        Dim secNames = {"Translation", "Rotation", "Scale"}
        Dim secComps = {New Integer() {0, 1, 2}, New Integer() {3, 4, 5}, New Integer() {6}}
        Dim secLabels = {New String() {"X", "Y", "Z"}, New String() {"X", "Y", "Z"}, New String() {"S"}}

        Dim layout As New TableLayoutPanel() With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .AutoSize = False, .Margin = New Padding(0)}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 26))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        Dim tip As New ToolTip()
        Dim row As Integer = 0
        Dim contentH As Integer = 0

        For Each rd As FacialBoneRegion In members
            Dim live = RegionLiveComponents(rd)
            If Not live.Any(Function(x) x) Then Continue For

            Dim bars(6) As FO4_Base_Library.TinySliderTextBox

            If showSub Then AddCardHeaderRow(layout, VariantLabel(rd.Name, cardLabel), True, row, contentH, HdrH + 4)

            For s = 0 To secNames.Length - 1
                Dim comps = secComps(s)
                AddCardHeaderRow(layout, secNames(s), False, row, contentH, HdrH)
                For k = 0 To comps.Length - 1
                    Dim ci As Integer = comps(k)
                    Dim isLive As Boolean = live(ci)
                    ' FMRS values are signed [-1..+1]; 0 = bind pose. LerpFmrs maps -1→min, +1→max.
                    ' No-op axes (min/max == Default) are still built but DISABLED, so every card keeps
                    ' the same layout/size instead of shrinking to its live axes.
                    Dim bar As New FO4_Base_Library.TinySliderTextBox() With {.Minimum = -1R, .Maximum = 1R,
                        .DisplayFormat = "0.00%", .InputScale = 0.01R,
                        .SmallChange = 0.01R, .LargeChange = 0.1R,
                        .FillMode = FO4_Base_Library.TinySliderFillMode.Center,
                        .Value = 0R, .Dock = DockStyle.Fill, .Margin = New Padding(0, 1, 0, 1),
                        .Enabled = isLive}
                    Dim resetBtn As New Button() With {.Text = secLabels(s)(k),
                        .Dock = DockStyle.Top, .Height = 22, .Margin = New Padding(0, 1, 2, 1),
                        .TabStop = False, .Padding = New Padding(0), .Enabled = isLive}
                    Dim regId As UInteger = rd.ID
                    Dim compIdx As Integer = ci
                    Dim theBar = bar
                    If isLive Then
                        AddHandler bar.ValueChanged, Sub(sndr, e) OnRegionSliderChanged(regId, compIdx)
                        AddHandler bar.DragEnded, AddressOf OnSliderDragEnded
                        AddHandler resetBtn.Click, Sub(sndr, e) theBar.Value = 0
                    End If
                    tip.SetToolTip(resetBtn, AxisName(ci) & If(isLive, " — reset", " — (not used by this region)"))
                    layout.RowCount = row + 1
                    layout.RowStyles.Add(New RowStyle(SizeType.Absolute, RowH))
                    layout.Controls.Add(resetBtn, 0, row)
                    layout.Controls.Add(bar, 1, row)
                    bars(ci) = bar
                    row += 1 : contentH += RowH
                Next
            Next

            _regionBars(rd.ID) = bars
        Next

        If row = 0 Then Return Nothing

        ' Trailing flexible row: absorbs leftover height in the fixed-size card so the content rows
        ' above keep their exact heights (buttons stay aligned with sliders) instead of the last row
        ' stretching. An empty AutoSize row would be 0 and absorb nothing — Percent(100) takes the slack.
        layout.RowCount = row + 1
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim group As New GroupBox() With {
            .Text = cardLabel, .Width = 230, .Height = contentH + 34,
            .Margin = New Padding(4), .Padding = New Padding(6, 4, 6, 4)}
        Dim boneList As String = String.Join(", ", members(0).Bones.Select(Function(b) b.Bone))
        tip.SetToolTip(group, If(showSub, $"Bones: {boneList}", $"Bones: {boneList}  •  FMRI 0x{members(0).ID:X8}"))
        group.Controls.Add(layout)
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
                    If bars(i) Is Nothing Then Continue For   ' dead axes aren't built (BuildBoneCard)
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
        ElseIf active Is TabPageSseMorphs Then
            ResetSseMorphsSection()
        ElseIf active Is TabPageSseTints Then
            ResetSseTintsSection()
        End If
    End Sub

    ''' <summary>SSE: revert NAM9/NAMA sliders + custom morphs to the construction snapshot (_seedPreset), else
    ''' to the NPC's authored NAM9/NAMA. Mirrors ResetVertexMorphsSection but for the SSE morph channel.</summary>
    Private Sub ResetSseMorphsSection()
        Dim p = Preset
        Dim src = _seedPreset
        ' Revert the preset's SSE morph overrides to the snapshot (deep-copied so later edits don't touch _seedPreset).
        p.SseNam9 = If(src IsNot Nothing AndAlso src.SseNam9 IsNot Nothing, DirectCast(src.SseNam9.Clone(), Single()), Nothing)
        p.SseNama = If(src IsNot Nothing AndAlso src.SseNama IsNot Nothing, DirectCast(src.SseNama.Clone(), UInteger()), Nothing)
        p.SseCustomMorphs = CloneSseCustomMorphList(If(src Is Nothing, Nothing, src.SseCustomMorphs))
        p.HasSseMorphs = If(src IsNot Nothing, src.HasSseMorphs, False)
        PopulateSseMorphTab()   ' rebuilds rows (custom count may change); LoadSseMorphValues seeds sliders from the base NPC
        ' Re-assert the snapshot override on top of the base seeding (only when the snapshot actually held one).
        _suspendEvents = True
        Try
            If p.SseNam9 IsNot Nothing Then
                For i = 0 To SseNam9MorphMap.Nam9SliderCount - 1
                    If i < p.SseNam9.Length Then
                        _sseNam9(i) = p.SseNam9(i)
                        If _sseNam9Sliders(i) IsNot Nothing Then _sseNam9Sliders(i).Value = Math.Max(-1.0R, Math.Min(1.0R, CDbl(p.SseNam9(i))))
                    End If
                Next
            End If
            If p.SseNama IsNot Nothing Then
                For f = 0 To SseNam9MorphMap.NamaFamilyCount - 1
                    If f < p.SseNama.Length Then
                        _sseNama(f) = p.SseNama(f)
                        If _sseNamaCombos(f) IsNot Nothing Then _sseNamaCombos(f).SelectedIndex = Math.Max(0, Math.Min(_sseNamaCombos(f).Items.Count - 1, CInt(p.SseNama(f))))
                    End If
                Next
            End If
        Finally
            _suspendEvents = False
        End Try
        ScheduleRefresh(FaceRefreshScope.Morphs)
    End Sub

    ''' <summary>SSE: revert the face-tint layers to the construction snapshot and re-render.</summary>
    Private Sub ResetSseTintsSection()
        Dim p = Preset
        Dim src = _seedPreset
        p.SseTintRawOverride = CloneRawSubrecordList(If(src Is Nothing, Nothing, src.SseTintRawOverride))
        p.HasSseTints = If(src IsNot Nothing, src.HasSseTints, False)
        p.SseTintTexOverride = If(src IsNot Nothing AndAlso src.SseTintTexOverride IsNot Nothing,
                                  New Dictionary(Of Integer, String)(src.SseTintTexOverride), Nothing)
        PopulateSseTintTab()   ' re-parses the layers (base + reverted override) and rebuilds the rows
        ScheduleRefresh(FaceRefreshScope.TexturesOnly)
    End Sub

    ''' <summary>Deep-copy an SSE custom-morph list (Nothing → Nothing) so a reset snapshot stays isolated.</summary>
    Private Shared Function CloneSseCustomMorphList(src As List(Of NPC_CustomMorph)) As List(Of NPC_CustomMorph)
        If src Is Nothing Then Return Nothing
        Dim c As New List(Of NPC_CustomMorph)(src.Count)
        For Each cm In src : c.Add(New NPC_CustomMorph With {.Name = cm.Name, .Value = cm.Value}) : Next
        Return c
    End Function

    ''' <summary>Deep-copy a raw-subrecord list (Nothing → Nothing).</summary>
    Private Shared Function CloneRawSubrecordList(src As List(Of NPC_RawSubrecord)) As List(Of NPC_RawSubrecord)
        If src Is Nothing Then Return Nothing
        Dim c As New List(Of NPC_RawSubrecord)(src.Count)
        For Each r In src
            c.Add(New NPC_RawSubrecord With {.Sig = r.Sig, .Data = If(r.Data Is Nothing, Nothing, DirectCast(r.Data.Clone(), Byte()))})
        Next
        Return c
    End Function

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
                    Logger.LogLazy(Function() $"[EDIT-FACE] tint-only refresh failed for NPC 0x{_rootNpcFormID:X8}: {ex.GetType().Name}: {ex.Message}")
                End Try
                Return
            Case FaceRefreshScope.Morphs
                If _editorHost.LastRenderData IsNot Nothing Then
                    Dim intent = _editorHost.PreviewCtl.Intent
                    intent.MorphResolver = _mainForm.BuildCompositeMorphResolver(_editorHost.LastRenderedState, _editorHost.LastRenderData, _editorHost)
                    intent.MarkDirty(RenderDirtyFlags.Morphs, _editorHost.LastRenderData.Shapes)
                End If
                ' FO4 ONLY. MPPI Morph Group presets like Murphy's "Arrugado" do TWO things: vertex
                ' deformation (MSDV — handled by the resolver above) AND a per-region MPPT TXST
                ' texture swap (Wrinkled skin texture inside the Forehead/Cheeks/Neck region
                ' mask, applied by BuildFaceRegionSwaps → ApplyRegionSwapChannelOnto inside
                ' TryApplyFaceTints). Re-running the resolver alone updates geometry but leaves
                ' the textures stale — switching from Smooth to Wrinkled would deform the mesh
                ' but show the previous texture. Refresh the tint pipeline too. No-op for NPCs
                ' whose active presets carry no MPPT (BuildFaceRegionSwaps returns 0 swaps).
                '
                ' SKYRIM HAS NO SUCH MECHANISM: its face morphs (NAM9 / NAMA / RaceMenu custom / sculpt) are pure
                ' vertex deformation and touch NO texture — the facetint depends only on the tint layers
                ' (TINI/TINC/TINV), which a morph edit cannot change. So running the tint pipeline here was pure
                ' waste, and MEASURED it is expensive: the SSE fold (complexion + facetint + detail) is per-pixel,
                ' so a 1024² vanilla head costs ~0.4s but a 4096² modded head (COtR ships 4K faces) costs
                ' 2.6–4.5s — i.e. every slider drag stalled the editor for seconds. Gated to FO4.
                If Config_App.Current.Game = Config_App.Game_Enum.Fallout4 Then
                    Try
                        _mainForm.RefreshFaceTintLivePreview(_editorHost)
                    Catch ex As Exception
                        Logger.LogLazy(Function() $"[EDIT-FACE] tint refresh failed for NPC 0x{_rootNpcFormID:X8}: {ex.GetType().Name}: {ex.Message}")
                    End Try
                End If
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

        ' Tooltip game-aware + deshabilitado del gore bajo Skyrim (no hay meatcaps). Mismo criterio que
        ' la toolbar del MainForm — ver RenderToggleLabels.
        RenderToggleLabels.Apply(Nothing, Nothing, Nothing, Nothing, Nothing, Nothing, Nothing, Nothing, Nothing, CheckBoxRenderGore)

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

        ' Release the cached hair-palette LUT bitmap. EnsureHairPaletteLoaded builds it once
        ' (DirectXDDSLoader.CreateBitmapFromDDS → a managed Bitmap owning its own pixels) and never
        ' disposed it, leaking GDI memory each time the editor opened.
        _hairPaletteBitmap?.Dispose()
        _hairPaletteBitmap = Nothing
    End Sub

    ' =====================================================================
    ' Helpers
    ' =====================================================================

    ''' <summary>Render-gore checkbox toggle. Mutates the editor host's Toggles in place
    ''' (the only visibility flag the EditFace surface exposes — head meshes don't have
    ''' Underarmor/Armor/Headwear categories) and runs the standard visibility pass.</summary>
    Private Sub OnRenderGoreChanged(sender As Object, e As EventArgs) Handles CheckBoxRenderGore.CheckedChanged
        If _seedingToggles Then Return
        If _editorHost Is Nothing Then Return
        _editorHost.Toggles.RenderGore = CheckBoxRenderGore.Checked
        _editorHost.ApplyRenderToggleVisibility()
    End Sub

End Class
