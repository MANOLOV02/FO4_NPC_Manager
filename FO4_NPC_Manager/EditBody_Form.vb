Imports System.Globalization
Imports FO4_Base_Library

''' <summary>Editor for an NPC's body weight (MWGT — 3 sliders), MRSV body morph regions
''' (5 vanilla regions per wbDefinitionsFO4.pas:10793) and BodySlide vertex sliders (PIRT
''' .tri morphs from the loaded body NIF, F4SE-only field).
'''
''' Live edit: every slider drag mutates the LooksMenu preset overlay (_appliedPresets) on
''' the host MainForm and triggers a granular repaint via the supplied refresh callback.
''' OK confirms (commits the live edit). Cancel restores the snapshot taken when the form
''' opened, then refreshes one last time so the preview reverts.
'''
''' Pipeline reminder (vanilla bones first, BodySlide vertex on top):
'''   1. BuildBodyWeightPose applies MWGT (Layer 1) + NNAM + MRSV (Layer 3) + ARMA (Layer 4)
'''      to the skeleton — bone scaling. No .tri.
'''   2. MorphEngine.ApplyMorphPlan applies face FRTRI003 morphs + BodySlide PIRT morphs to
'''      NifLocalVertices pre-skin. Skinning then transforms the morphed verts with the
'''      already-scaled bones.
''' </summary>
Public Class EditBody_Form

    ' SSE (Skyrim) body editing mirrors the EditFace idiom (EditFace_Form.vb:54): under _isSSE the FO4-only
    ' sections (MWGT 3-axis triangle, MRSV, LM Skin template) are hidden and their FO4 seed/build paths are
    ' skipped; a code-built single 0-100 weight slider (BuildSseWeightSection, mirror BuildSseMorphTab) drives
    ' the vanilla body weight (NAM7 → _0/_1 LERP) plus Load/Save .jslot. Everything game-gated so FO4 is
    ' byte-identical.
    Private ReadOnly _isSSE As Boolean = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
    ' SSE weight section control (built in code under _isSSE; Nothing on FO4). A TinySliderTextBox — the
    ' same slider style the BodySlide / MRSV rows use — replacing the earlier raw TrackBar.
    Private _sseWeightSlider As FO4_Base_Library.TinySliderTextBox = Nothing
    ' SSE overlays reuse the FO4 overlay controls; these two read-only fields show the applied overlay's texture
    ' (RaceMenu overlays carry a texture path directly, where FO4 resolves it from an f4ee template).
    Private _sseTexDiffuse As TextBox = Nothing
    Private _sseTexNormal As TextBox = Nothing
    ''' <summary>Which overlay zone a new overlay is added to (Body/Hands/Feet). skee64 keeps a separate node
    ''' set per zone; without this only Body overlays could be authored.</summary>
    Private _sseOverlayZone As ComboBox = Nothing
    ' Parallel index→entry map for the LEFT paint catalog (ListBoxOverlayAvailable) in SSE mode; the ListBox
    ' index can't be used directly because the filter box removes rows.
    Private ReadOnly _ssePaintShown As New List(Of FO4_Base_Library.RaceMenuPaintCatalog.Entry)
    ' SSE "Skin Overrides" tab controls (RaceMenu NiOverride body-paint per slot; SSE-only, code-built).
    Private ReadOnly _sseSkinToolTip As New ToolTip()
    Private _sseSkinList As ListBox = Nothing
    ' Biped-slot flag grid (BipedSlotCheckboxes) that builds the selected override's slotMask — the SAME control
    ' the ARMA/ARMO editors use. RaceMenu keys a skin override by a slotMask BITMASK (any combination of biped
    ' slots), matched in-game by an EXACT find(armorMask & addonMask) (OverrideInterface.cpp:1190/2506), so the
    ' user builds the exact combination here rather than picking from a fixed list.
    Private _sseSkinSlotChecks As Dictionary(Of Integer, CheckBox) = Nothing
    ' One read-only textbox per texture-set slot the skin-override editor exposes (index → box). RaceMenu replaces
    ' each key-9 slot in place, so the editor lets the user set any slot, not just diffuse/normal.
    Private ReadOnly _sseSkinSlotBoxes As New Dictionary(Of Integer, TextBox)
    ' The ONLY texture-set slots skee's skin override actually applies to a skin (FaceGenRGBTint) material:
    ' GetTextureFromIndex maps 0→diffuse, 1→normal, 2→subsurface(_sk), 7→specular; indices 3-6 are engine no-ops
    ' on a skin, so we don't offer them (a preset that carries them still round-trips — see the model).
    Private Shared ReadOnly SseSkinTexSlots As (Index As Integer, Label As String)() = {
        (0, "Diffuse"), (1, "Normal"), (2, "Subsurface (SK)"), (7, "Specular")}
    Private _sseSkinTintEnable As CheckBox = Nothing
    Private _sseSkinTintColor As Button = Nothing
    Private _sseSkinTintAlpha As FO4_Base_Library.TinySliderTextBox = Nothing
    ' The NPC's effective NAM7 weight captured at open time (for the Body-tab Reset).
    Private _initialSseWeight As Single = 100.0F

    Private ReadOnly _rootNpcFormID As UInteger
    Private ReadOnly _appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)
    Private ReadOnly _refresh As Action
    Private ReadOnly _availableSliders As List(Of String)

    ' Phase D wiring — see EditFace_Form for the full rationale. _editorHost owns this form's
    ' embedded preview; _mainForm is referenced only to invoke pipeline methods that still live
    ' on MainForm but accept an arbitrary host.
    ' _editorHost / HasUncommittedChanges lifted to EditorFormBase (shared with EditFace_Form).
    Private ReadOnly _mainForm As MainForm = Nothing
    Private ReadOnly _mainGore As Boolean = False

    ' Snapshot for Cancel rollback. Cloned at construction; if the user cancels we restore.
    Private ReadOnly _hadPriorOverlay As Boolean
    Private ReadOnly _priorPreset As LooksmenuLoader.LooksmenuPreset

    ' Snapshot for per-tab Reset (mirrors EditFace_Form's _seedPreset). Captures the NPC's
    ' effective state at form-open time so Reset on a tab reverts that tab's fields back to
    ' "the way the NPC looked when Edit Body opened", scoped per-tab so the user can throw away
    ' one tab's edits without touching the other.
    Private ReadOnly _initialSeed As InitialValues
    Private ReadOnly _initialWnamFormID As UInteger


    ' Per-MRSV slot labels + UI references. Populated in CreateMrsvRows.
    Private _mrsvBars(4) As FO4_Base_Library.TinySliderTextBox
    ' _suspendEvents / _seedingToggles lifted to EditorFormBase (shared with EditFace_Form).

    ' Per-BodySlide-slider UI references. Key = sliderName (case-insensitive).
    ' Rows are fixed-height (slider Height 28 + its 2px top/bottom margin) because they can't AutoSize.
    Private Const BodySlideRowHeight As Integer = 32
    Private ReadOnly _bodySlideBars As New Dictionary(Of String, FO4_Base_Library.TinySliderTextBox)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _bodySlideRows As New Dictionary(Of String, Control)(StringComparer.OrdinalIgnoreCase)

    ' BodySlide preset selector state (mirrors WM's ComboBoxPresets + ComboBoxSize —
    ' Wardrobe_Manager_Form.vb:2649/2177). The catalog reads <BS exe dir>\SliderPresets\*.xml;
    ' the exe path / size persist per-game in NPC_Config (npc_config.json). The preset NAME is
    ' NOT persisted and the combo always opens at "(none)": it reflects THIS NPC's state, and a
    ' restored name would claim a preset the NPC's sliders don't carry. Values only move on a
    ' user pick; _seedingBsPresetCombo guards the combos while they're being (re)populated.
    Private Const BsPresetNone As String = "(none)"
    Private _bsPresetCatalog As BodySlidePresetCatalog = Nothing
    Private _seedingBsPresetCombo As Boolean = False
    Private ReadOnly _bsPresetToolTip As New ToolTip()

    ' Overlays tab state -------------------------------------------------------------------
    ' Full gender-filtered template universe (display order = GetOverlayTemplateCandidates order),
    ' captured once at construction. ListBoxOverlayAvailable's Items are a FILTERED projection of
    ' this (the filter narrows by display name), so the list index can't be used to look up the
    ' template directly — _availableShown maps the shown index → template instead. Mirrors the
    ' _bodySlideRows parallel-mapping idiom: ListBox.Items hold display strings (data), the
    ' template lives in a parallel list.
    Private ReadOnly _overlayCandidates As New List(Of OverlayTemplate)
    Private ReadOnly _availableShown As New List(Of OverlayTemplate)
    ' Whether any overlay templates exist for this NPC's gender. False → UpdateOverlayPropsForSelection
    ' shows the empty-state message in LabelOverlaySelected and disables the prop controls.
    Private _hasOverlayTemplates As Boolean = False

    ' Slider drag throttle: model writes happen synchronously inside On...Changed (so Save/OK
    ' captures fresh state) but the costly _refresh callback is deferred. Same pattern as
    ' Editor_Form.vb (WM): timer fires after the user pauses; DragEnded forces an immediate flush
    ' so releasing the mouse always shows the final preview without waiting for the timer tick.
    Private WithEvents RefreshTimer As New Timer() With {.Interval = 500, .Enabled = False}
    Private _pendingRefresh As Boolean = False
    ' Overlay edits need a FULL reload (re-resolve overlay layers), not the morph/pose-only
    ' _refresh path. The shared throttle timer carries this separate pending flag so overlay
    ' slider drags flush to TriggerSkinChangeReload instead of OnLocalBodyRefresh.
    Private _pendingOverlayReload As Boolean = False

    ''' <summary>Initial values seeded from the live NPC (post-overlay-applied). Used to
    ''' populate sliders the very first time the editor opens against an NPC that has no
    ''' overlay yet — without this we'd show all zeros even when the record carries values.</summary>
    Public Class InitialValues
        Public Thin As Single
        Public Muscular As Single
        Public Fat As Single
        Public Mrsv As Single() = New Single() {0, 0, 0, 0, 0}
        Public BodySlide As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>SSE-only: the NPC's current effective body weight (NAM7 float, 0..100, default 100).
        ''' Seeds the SSE weight slider. Unused on FO4 (the SSE section is never built).</summary>
        Public SseWeight As Single = 100.0F
    End Class

    ' NPC race/gender + currently-effective WNAM (post-overlay), captured from MainForm at open
    ' time. Used to build the two skin combos in PopulateSkinCombos.
    Private ReadOnly _npcRaceFID As UInteger
    Private ReadOnly _npcIsFemale As Boolean
    Private ReadOnly _currentWnamFormID As UInteger
    ' Maps the WNAM combo's selected index to the FormID it represents. Index 0 reserved for
    ' the "(use RACE default)" sentinel = FormID 0; index 1 reserved for the pinned current WNAM
    ' when it falls outside the filtered universe; the rest are the filtered candidates.
    Private ReadOnly _wnamComboFormIDs As New List(Of UInteger)
    ' Maps the LM Skin template combo's selected index to the template id (Nothing = "(none)").
    Private ReadOnly _lmTemplateComboIds As New List(Of String)

    Public Sub New(rootNpcFormID As UInteger,
                   appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                   hasMwgt As Boolean,
                   hasMrsv As Boolean,
                   availableSliders As List(Of String),
                   initial As InitialValues,
                   mainForm As MainForm,
                   mainGore As Boolean,
                   npcRaceFID As UInteger,
                   npcIsFemale As Boolean,
                   currentWnamFormID As UInteger)
        InitializeComponent()
        _rootNpcFormID = rootNpcFormID
        _appliedPresets = appliedPresets
        _availableSliders = If(availableSliders, New List(Of String))
        _refresh = AddressOf OnLocalBodyRefresh
        _mainForm = mainForm
        _mainGore = mainGore
        _npcRaceFID = npcRaceFID
        _npcIsFemale = npcIsFemale
        _currentWnamFormID = currentWnamFormID
        _initialSeed = initial
        _initialWnamFormID = currentWnamFormID

        ' Snapshot the existing overlay so Cancel can restore it byte-for-byte.
        Dim existing As LooksmenuLoader.LooksmenuPreset = Nothing
        _hadPriorOverlay = _appliedPresets.TryGetValue(rootNpcFormID, existing)
        _priorPreset = If(_hadPriorOverlay, ClonePreset(existing), Nothing)

        ' Ensure an overlay preset exists for live editing — even if the NPC currently has none.
        ' We'll roll it back in Cancel if the user bails out.
        Dim p As LooksmenuLoader.LooksmenuPreset = Nothing
        If Not _appliedPresets.TryGetValue(rootNpcFormID, p) OrElse p Is Nothing Then
            p = New LooksmenuLoader.LooksmenuPreset()
            _appliedPresets(rootNpcFormID) = p
        End If

        ' Seed missing slots in the overlay from the NPC's current effective values, so the
        ' sliders open at the NPC's real state instead of all zeros. Only fills slots the
        ' overlay didn't already define (preserves any prior preset/edit).
        SeedOverlayFromInitial(p, initial, seedMrsv:=Not _isSSE)


        ApplyAvailability(hasMwgt, hasMrsv, _availableSliders.Count > 0)

        ' MRSV is an FO4-only channel (SSE has no MRSV) — skip the row build under _isSSE (mirror
        ' EditFace_Form's FO4 seed gating). The SSE weight section replaces the MWGT triangle.
        If Not _isSSE AndAlso hasMrsv Then CreateMrsvRows()
        CreateBodySlideRows()
        PopulateSkinCombos()

        ' SSE (Skyrim) body editing — mirror EditFace_Form.vb:221-232. Build the code-built weight slider,
        ' hide the FO4-only controls, and re-source the Overlays tab from the RaceMenu (path-based) carrier
        ' (Phase 3). The BodySlide tab + Skin(WNAM) group are game-agnostic and stay as-is.
        If _isSSE Then
            _initialSseWeight = If(initial IsNot Nothing, initial.SseWeight, 100.0F)
            ' Seed the overlay's SSE weight from the NPC's current effective NAM7 (unless a prior preset
            ' already carries one), so the slider opens at the real value and edits ride on top.
            If Not p.SseWeight.HasValue Then p.SseWeight = _initialSseWeight
            BuildSseWeightSection()
            BuildSseBodyScaleTab()   ' RaceMenu NiOverride node transform sliders (SSE-only tab; FO4 has no analogue)
            BuildSseSkinOverridesTab()  ' RaceMenu NiOverride skin body-paint per slot (SSE-only tab; FO4 has no analogue)
            ' Same sliders, different carrier: under Skyrim these are the .jslot's bodyMorphs read through
            ' skee64/RaceMenu, not F4SE's PIRT field — so the FO4 caption would be a lie here.
            GroupBoxBodySlide.Text = "BodySlide Sliders (BODYTRI .tri — vertex morphs, RaceMenu/skee64 field)"
            GroupBoxWeight.Visible = False    ' FO4 MWGT 3-axis triangle
            GroupBoxMrsv.Visible = False      ' FO4 MRSV 5 regions
            ComboBoxLmSkinTemplate.Visible = False : LabelLmSkinTemplate.Visible = False  ' F4SE-only
            ' Overlays: reuse the FO4 controls (set up in BuildSseOverlaysSection AFTER InitOverlaysTab, so the
            ' FO4 init doesn't overwrite the SSE-populated applied list). See below.
        End If

        AddHandler WeightTriangle.WeightChanged, AddressOf OnWeightTriangleChanged
        AddHandler ButtonOk.Click, AddressOf OnOk
        AddHandler ButtonCancel.Click, AddressOf OnCancel
        AddHandler ButtonResetSection.Click, AddressOf OnResetSection
        AddHandler TextBoxBodySlideFilter.TextChanged, AddressOf OnBodySlideFilterChanged
        ' BodySlide preset selector — init BEFORE the handlers are attached so the initial
        ' population/restore can't fire an apply even without the seeding guard.
        InitBodySlidePresetSelector()
        AddHandler ComboBoxBsPreset.SelectedIndexChanged, AddressOf OnBsPresetComboChanged
        AddHandler ComboBoxBsSize.SelectedIndexChanged, AddressOf OnBsSizeComboChanged
        AddHandler ButtonBsPresetClear.Click, AddressOf OnBsPresetClear
        AddHandler ButtonBsPresetBrowse.Click, AddressOf OnBsPresetBrowseExe
        AddHandler ComboBoxWnam.SelectedIndexChanged, AddressOf OnWnamComboChanged
        AddHandler ComboBoxLmSkinTemplate.SelectedIndexChanged, AddressOf OnLmSkinTemplateComboChanged

        ' Overlays tab — handlers + initial population. The tab is ALWAYS present (unlike
        ' Weight/MRSV which hide); when no templates exist for this gender we show the empty
        ' legend and leave the controls inert (InitOverlaysTab handles both branches).
        AddHandler TextBoxOverlayFilter.TextChanged, AddressOf OnOverlayFilterChanged
        AddHandler ButtonOverlayAdd.Click, AddressOf OnOverlayAdd
        AddHandler ButtonOverlayRemove.Click, AddressOf OnOverlayRemove
        AddHandler ButtonOverlayUp.Click, AddressOf OnOverlayUp
        AddHandler ButtonOverlayDown.Click, AddressOf OnOverlayDown
        AddHandler ListBoxOverlayApplied.SelectedIndexChanged, AddressOf OnOverlayAppliedSelectionChanged
        AddHandler SliderOverlayOffsetU.ValueChanged, AddressOf OnOverlayOffsetChanged
        AddHandler SliderOverlayOffsetV.ValueChanged, AddressOf OnOverlayOffsetChanged
        AddHandler SliderOverlayScaleU.ValueChanged, AddressOf OnOverlayScaleChanged
        AddHandler SliderOverlayScaleV.ValueChanged, AddressOf OnOverlayScaleChanged
        AddHandler SliderOverlayOffsetU.DragEnded, AddressOf OnSliderDragEnded
        AddHandler SliderOverlayOffsetV.DragEnded, AddressOf OnSliderDragEnded
        AddHandler SliderOverlayScaleU.DragEnded, AddressOf OnSliderDragEnded
        AddHandler SliderOverlayScaleV.DragEnded, AddressOf OnSliderDragEnded
        AddHandler SliderOverlayTintAlpha.ValueChanged, AddressOf OnOverlayTintAlphaChanged
        AddHandler SliderOverlayTintAlpha.DragEnded, AddressOf OnSliderDragEnded
        AddHandler CheckBoxOverlayTint.CheckedChanged, AddressOf OnOverlayTintToggled
        AddHandler ButtonOverlayTintColor.Click, AddressOf OnOverlayTintColorClicked

        LoadValuesFromOverlay()
        InitOverlaysTab()
        ' SSE: after the FO4 InitOverlaysTab, re-point the reused overlay controls at the RaceMenu carrier.
        If _isSSE Then BuildSseOverlaysSection()
    End Sub

    ''' <summary>Populate ComboBoxWnam from MainForm.GetSkinArmoCandidates (race+gender filter)
    ''' and ComboBoxLmSkinTemplate from MainForm.GetLmSkinTemplateCandidates. Pinned entries:
    '''   • WNAM index 0 = "(use RACE default)" → FormID 0 (xEdit allows NULL — wbDefinitionsFO4.pas:11434).
    '''   • WNAM index 1 (only when applicable) = the NPC's current effective WNAM, even if it
    '''     falls outside the filter (so opening EditBody on an oddly-flagged NPC doesn't lose
    '''     the live skin).
    '''   • LM Skin index 0 = "(none)" → empty id.
    ''' Selection is then driven from the overlay preset's SkinFormIDOverride / SkinTemplateId.</summary>
    Private Sub PopulateSkinCombos()
        _suspendEvents = True
        Try
            ' WNAM combo
            ComboBoxWnam.Items.Clear()
            _wnamComboFormIDs.Clear()
            ComboBoxWnam.Items.Add("(use RACE default)")
            _wnamComboFormIDs.Add(0UI)
            Dim filtered = _mainForm.GetSkinArmoCandidates(_npcRaceFID, _npcIsFemale)
            ' Pin current WNAM at the top if it's non-zero AND not already in the filtered list.
            If _currentWnamFormID <> 0UI AndAlso Not filtered.Any(Function(x) x.FormID = _currentWnamFormID) Then
                Dim disp = _mainForm.GetSkinArmoDisplayName(_currentWnamFormID)
                If String.IsNullOrEmpty(disp) Then disp = _currentWnamFormID.ToString("X8")
                ComboBoxWnam.Items.Add(disp & "  ⚠ outside race/gender filter")
                _wnamComboFormIDs.Add(_currentWnamFormID)
            End If
            For Each cand In filtered
                ComboBoxWnam.Items.Add(cand.DisplayName)
                _wnamComboFormIDs.Add(cand.FormID)
            Next

            ' LM Skin template combo — F4SE-only (FO4). Skip populating under SSE (the control is hidden).
            ComboBoxLmSkinTemplate.Items.Clear()
            _lmTemplateComboIds.Clear()
            ComboBoxLmSkinTemplate.Items.Add("(none)")
            _lmTemplateComboIds.Add("")
            If Not _isSSE Then
                For Each tpl In _mainForm.GetLmSkinTemplateCandidates(_npcIsFemale)
                    ComboBoxLmSkinTemplate.Items.Add(tpl.DisplayName)
                    _lmTemplateComboIds.Add(tpl.Id)
                Next
            End If

            ' Initialize the selections from the overlay preset.
            Dim p = Preset
            ' WNAM: SkinFormIDOverride.HasValue = user explicitly set; otherwise pin to current
            ' effective WNAM (which is what the NPC is rendering right now).
            Dim selectedFid As UInteger
            If p.SkinFormIDOverride.HasValue Then
                selectedFid = p.SkinFormIDOverride.Value
            Else
                selectedFid = _currentWnamFormID
            End If
            Dim wnamIdx = _wnamComboFormIDs.IndexOf(selectedFid)
            ComboBoxWnam.SelectedIndex = If(wnamIdx >= 0, wnamIdx, 0)
            ' LM SkinTemplateId — empty maps to (none).
            Dim selId = If(p.SkinTemplateId, "")
            Dim lmIdx = _lmTemplateComboIds.IndexOf(selId)
            ComboBoxLmSkinTemplate.SelectedIndex = If(lmIdx >= 0, lmIdx, 0)
        Finally
            _suspendEvents = False
        End Try
    End Sub

    Private Async Sub OnWnamComboChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim idx = ComboBoxWnam.SelectedIndex
        If idx < 0 OrElse idx >= _wnamComboFormIDs.Count Then Return
        Dim p = Preset
        ' Index 0 = "(use RACE default)" → FormID 0 (xEdit allows NULL on NPC.WNAM). Encoded as
        ' SkinFormIDOverride = Some(0) so the writer can later emit "no WNAM subrecord" for the
        ' Save ESP path; the runtime overlay merge already maps 0 to RACE.WNAM fallback
        ' (MainForm.vb:6185-6187). Any other index encodes the chosen ARMO FormID.
        p.SkinFormIDOverride = _wnamComboFormIDs(idx)
        Await TriggerSkinChangeReload()
    End Sub

    Private Async Sub OnLmSkinTemplateComboChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim idx = ComboBoxLmSkinTemplate.SelectedIndex
        If idx < 0 OrElse idx >= _lmTemplateComboIds.Count Then Return
        Dim p = Preset

        ' Origin-based template swap. The preset tracks exactly which HDPTs were injected by an
        ' LM template (LmTemplateInjectedHdptFormIDs) and whether HasHeadPartFormIDs was flipped
        ' True specifically by Materialize (HasHeadPartFormIDsSetByTemplate). Retract uses both
        ' to remove ONLY the template's contribution, leaving anything Edit Face / Paste / Load
        ' LM HeadParts arrays put in the preset untouched.
        '
        ' Sequence: Retract previous template → set new SkinTemplateId → Materialize new bundle.
        ' "(none)" goes through the same path; Materialize is a no-op for an empty id, so the
        ' result is "previous template's HDPTs gone, nothing else changed".
        NpcRecordOverlay.RetractLmTemplateBundleFromPreset(p)

        p.SkinTemplateId = _lmTemplateComboIds(idx)
        NpcRecordOverlay.MaterializeLmTemplateBundleToPreset(p, _npcIsFemale, AddressOf _mainForm.ResolveLmSkinTemplate_Friend)
        Await TriggerSkinChangeReload()
    End Sub

    ''' <summary>Append <paramref name="hdptFormID"/> to <paramref name="list"/> if non-zero and
    ''' not already present. Kept around for any future caller; the LM template path now goes
    ''' through <see cref="NpcRecordOverlay.MaterializeLmTemplateBundleToPreset"/>.</summary>
    Private Shared Sub AddHdptIfMissing(list As List(Of UInteger), hdptFormID As UInteger)
        If hdptFormID = 0UI Then Return
        If list.Contains(hdptFormID) Then Return
        list.Add(hdptFormID)
    End Sub

    ''' <summary>Refresh the preview after an NPC.WNAM / LM SkinTemplate combo change. Tries
    ''' the fast-path first (RefreshBodySkinLivePreview): when the new skin ARMO points to the
    ''' same mesh path as the previously-loaded one, only TXST + MaterialSwap fields are
    ''' re-resolved and material parameters are mutated in place — no VBO regeneration,
    ''' subsecond. Falls back to the full RenderInHostAsync when the mesh path differs (different
    ''' .nif, different bone palette / vertex count) or when the host state is incomplete.
    '''
    ''' The fast-path shares CollectArmoCandidates + ApplyShapeMaterialOverrides with the normal
    ''' render so TXST/MSWP resolution stays byte-identical between the two code paths.</summary>
    Private Async Function TriggerSkinChangeReload() As Task
        If _editorHost Is Nothing OrElse _mainForm Is Nothing Then Return
        Try
            If _mainForm.RefreshBodySkinLivePreview(_editorHost) Then Return
            Await _mainForm.RenderInHostAsync(_editorHost, _rootNpcFormID)
        Catch ex As Exception
            Logger.LogLazy(Function() $"[EDIT-BODY] skin-change reload failed for NPC 0x{_rootNpcFormID:X8}: {ex.GetType().Name}: {ex.Message}")
        End Try
    End Function

    ''' <summary>Seed the overlay preset's editable channels from the NPC's current effective
    ''' values, but only when the overlay hasn't already taken ownership of that channel. This
    ''' lets a user open Edit Body on a fresh NPC and see its real Weight/MRSV/BodySlide state
    ''' rather than zeros, without trampling a preset they previously loaded.
    '''
    ''' Sets HasBodyMorphValues=True regardless of whether the seed actually fired: opening
    ''' Edit Body declares ownership of MRSV. If the user adjusts sliders to all zero (or starts
    ''' from an empty MRSV race) and clicks OK, the resulting overlay must wipe MRSV on the NPC,
    ''' not preserve raw — Has*=True is what tells ApplyPresetOverlayToNpcData "treat the list
    ''' as authoritative even when empty".
    '''
    ''' <paramref name="seedMrsv"/> = False under SKYRIM: MRSV does not exist in the TES5 NPC_ schema
    ''' (it is FO4-only, wbDefinitionsFO4.pas:10793 'Body Morph Region Values'), and the MRSV section is
    ''' not even built there. Seeding it anyway wrote 5 zero floats + ownership into the overlay, which the
    ''' NPC_ writer then emitted as a real MRSV subrecord into the SSE plugin — xEdit flags the record as
    ''' erroneous. Under Skyrim the channel is left untouched: no values, no ownership claim.</summary>
    Private Shared Sub SeedOverlayFromInitial(p As LooksmenuLoader.LooksmenuPreset, initial As InitialValues,
                                              seedMrsv As Boolean)
        If initial Is Nothing Then Return
        If Not p.WeightThin.HasValue Then p.WeightThin = initial.Thin
        If Not p.WeightMuscular.HasValue Then p.WeightMuscular = initial.Muscular
        If Not p.WeightFat.HasValue Then p.WeightFat = initial.Fat
        If seedMrsv Then
            If p.BodyMorphValues.Count = 0 AndAlso initial.Mrsv IsNot Nothing Then
                ' Always carry exactly 5 slots (vanilla MRSV layout), zero-padding if needed.
                For i = 0 To 4
                    p.BodyMorphValues.Add(If(i < initial.Mrsv.Length, initial.Mrsv(i), 0.0F))
                Next
            End If
            p.HasBodyMorphValues = True
        End If
        If p.BodyMorphSliders.Count = 0 AndAlso initial.BodySlide IsNot Nothing Then
            For Each kv In initial.BodySlide
                p.BodyMorphSliders(kv.Key) = kv.Value
            Next
        End If
    End Sub

    ''' <summary>Hide / disable sections that don't apply to this race + body. Each section is
    ''' independent; we hide rather than gray out so the form stays compact when only one or two
    ''' apply (e.g. Ghoul race with no BSMS at all → only BodySlide section visible).</summary>
    Private Sub ApplyAvailability(hasMwgt As Boolean, hasMrsv As Boolean, hasBodySlide As Boolean)
        GroupBoxWeight.Visible = hasMwgt
        GroupBoxMrsv.Visible = hasMrsv
        ' BodySlide tab: GroupBoxBodySlide carries the actual sliders; LabelBodySlideEmpty is the
        ' empty-state legend shown when the body has no PIRT BODYTRI (engine wouldn't apply any
        ' BodyMorphs either). Mirrors EditFace_Form's empty-section pattern.
        GroupBoxBodySlide.Visible = hasBodySlide
        LabelBodySlideEmpty.Visible = Not hasBodySlide
    End Sub

    ''' <summary>Deep-clone for snapshot/restore. Delegates to LooksmenuLoader.ClonePreset
    ''' (canonical) so any new field added to LooksmenuPreset propagates through every
    ''' snapshot path automatically.</summary>
    Private Shared Function ClonePreset(p As LooksmenuLoader.LooksmenuPreset) As LooksmenuLoader.LooksmenuPreset
        Return LooksmenuLoader.ClonePreset(p)
    End Function

    Private ReadOnly Property Preset As LooksmenuLoader.LooksmenuPreset
        Get
            Dim p As LooksmenuLoader.LooksmenuPreset = Nothing
            _appliedPresets.TryGetValue(_rootNpcFormID, p)
            Return p
        End Get
    End Property

    ''' <summary>Build the 5 MRSV slider rows (Head/UpperTorso/Arms/LowerTorso/Legs).</summary>
    Private Sub CreateMrsvRows()
        For i = 0 To 4
            Dim idx = i  ' capture for closures
            Dim lblText As New Label() With {
                .Text = NpcMorphResolver.BodyRegionLabels(idx),
                .AutoSize = True,
                .MinimumSize = New Size(80, 0),
                .TextAlign = ContentAlignment.MiddleLeft,
                .Anchor = AnchorStyles.Left Or AnchorStyles.Right
            }
            Dim bar As New FO4_Base_Library.TinySliderTextBox() With {
                .Minimum = -1.0R,
                .Maximum = 1.0R,
                .DisplayFormat = "0.00%",
                .InputScale = 0.01R,
                .SmallChange = 0.01R,
                .LargeChange = 0.1R,
                .FillMode = FO4_Base_Library.TinySliderFillMode.Center,
                .Height = 28,
                .Value = 0R,
                .Dock = DockStyle.Fill,
                .Margin = New Padding(2)
            }
            AddHandler bar.ValueChanged, Sub(s, e) OnMrsvChanged(idx)
            AddHandler bar.DragEnded, AddressOf OnSliderDragEnded
            MrsvLayout.Controls.Add(lblText, 0, idx)
            MrsvLayout.Controls.Add(bar, 1, idx)
            _mrsvBars(idx) = bar
        Next
    End Sub

    ''' <summary>Build the dynamic BodySlide slider rows from the union of morph names
    ''' present in the loaded body shapes' PIRT .tri files. Each row stretches to the panel's
    ''' current client width via <see cref="BodySlidePanel_Resize"/>; FlowLayoutPanel doesn't
    ''' honour Anchor on its children so we resize the rows ourselves on every parent resize.</summary>
    Private Sub CreateBodySlideRows()
        BodySlidePanel.SuspendLayout()
        Try
            BodySlidePanel.Controls.Clear()
            _bodySlideBars.Clear()
            _bodySlideRows.Clear()
            If _availableSliders.Count = 0 Then
                Dim empty As New Label() With {
                    .Text = "No BodySlide PIRT .tri found for any body shape on this NPC.",
                    .AutoSize = True,
                    .ForeColor = Color.Gray,
                    .Padding = New Padding(8)
                }
                BodySlidePanel.Controls.Add(empty)
                ' Don't disable ButtonResetSection here — OnResetSection dispatches by active tab
                ' (Body → MWGT/MRSV/Skin reset, BodySlide → wipe sliders). The Body-tab reset is
                ' always meaningful regardless of whether this NPC has BodySlide PIRT data, and the
                ' BodySlide-tab reset is a harmless no-op when there are no sliders.
                Return
            End If
            Dim rowWidth = ComputeBodySlideRowWidth()
            For Each sliderName In _availableSliders
                ' AutoSize must stay off: with it on the layout engine shrinks the row back to its
                ' preferred width (label + slider) and silently discards the Width we assign here and
                ' in BodySlidePanel_Resize, so the rows never reach the panel's right edge.
                Dim row As New TableLayoutPanel() With {
                    .ColumnCount = 2,
                    .RowCount = 1,
                    .AutoSize = False,
                    .Width = rowWidth,
                    .Height = BodySlideRowHeight,
                    .Margin = New Padding(0, 0, 0, 2)
                }
                row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 180))
                row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
                row.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

                Dim lbl As New Label() With {
                    .Text = sliderName,
                    .AutoSize = False,
                    .Width = 180,
                    .TextAlign = ContentAlignment.MiddleLeft,
                    .Dock = DockStyle.Fill
                }
                Dim bar As New FO4_Base_Library.TinySliderTextBox() With {
                    .Minimum = 0R,
                    .Maximum = 100.0R,
                    .AllowExtremeValues = True,
                    .DisplayFormat = "0\%",
                    .SmallChange = 1.0R,
                    .LargeChange = 10.0R,
                    .Height = 28,
                    .Value = 0R,
                    .Dock = DockStyle.Fill,
                    .Margin = New Padding(2)
                }
                Dim capturedName = sliderName
                AddHandler bar.ValueChanged, Sub(s, e) OnBodySlideChanged(capturedName)
                AddHandler bar.DragEnded, AddressOf OnSliderDragEnded
                row.Controls.Add(lbl, 0, 0)
                row.Controls.Add(bar, 1, 0)

                BodySlidePanel.Controls.Add(row)
                _bodySlideBars(sliderName) = bar
                _bodySlideRows(sliderName) = row
            Next
        Finally
            BodySlidePanel.ResumeLayout()
        End Try
    End Sub

    ''' <summary>Compute the width for a single BodySlide row inside <see cref="BodySlidePanel"/>:
    ''' panel client width minus a vertical-scrollbar reserve so a row never gets clipped when
    ''' the scrollbar appears. Used by both initial layout (CreateBodySlideRows) and resize
    ''' (BodySlidePanel_Resize).</summary>
    Private Function ComputeBodySlideRowWidth() As Integer
        Dim w = BodySlidePanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4
        Return Math.Max(200, w)
    End Function

    ''' <summary>FlowLayoutPanel doesn't honour Anchor on its children, so we resize each row
    ''' manually whenever the panel changes width (form resize, splitter drag, tab activate).
    ''' Cheap: one Width assignment per row, and the rows are fixed-height so nothing reflows
    ''' vertically.</summary>
    Private Sub BodySlidePanel_Resize(sender As Object, e As EventArgs) Handles BodySlidePanel.Resize
        If _bodySlideRows.Count = 0 Then Return
        Dim w = ComputeBodySlideRowWidth()
        BodySlidePanel.SuspendLayout()
        Try
            For Each kv In _bodySlideRows
                kv.Value.Width = w
            Next
        Finally
            BodySlidePanel.ResumeLayout()
        End Try
    End Sub

    ''' <summary>Initialize all slider positions from the current overlay preset state.</summary>
    Private Sub LoadValuesFromOverlay()
        _suspendEvents = True
        Try
            Dim p = Preset
            ' Weight (MWGT) — barycentric triangle. Values are normalized to sum=1 by the control.
            Dim t = p.WeightThin.GetValueOrDefault(0.0F)
            Dim m = p.WeightMuscular.GetValueOrDefault(0.0F)
            Dim f = p.WeightFat.GetValueOrDefault(0.0F)
            WeightTriangle.SetWeights(t, m, f)
            ' Echo the (already-normalized) values back into the linked sliders so the on-screen
            ' numbers match what the engine will see post-render. SyncMwgtSliders runs under
            ' _suspendEvents so the slider ValueChanged handlers don't recurse back into
            ' OnMwgtSliderChanged.
            SyncMwgtSliders(WeightTriangle.Thin, WeightTriangle.Muscular, WeightTriangle.Fat)
            ' MRSV — preset.BodyMorphValues already mirrors NPC.MRSV (5 floats in [-1..+1]).
            ' Null-guard: si la raza no expone MRSV regions (ej. Feral Ghoul), CreateMrsvRows no
            ' corrió y _mrsvBars(i) queda Nothing. Skip el set en ese caso — la sección entera
            ' ya está oculta por ApplyAvailability(hasMrsv=False).
            For i = 0 To 4
                If _mrsvBars(i) Is Nothing Then Continue For
                Dim v As Single = If(i < p.BodyMorphValues.Count, p.BodyMorphValues(i), 0.0F)
                _mrsvBars(i).Value = v
            Next
            ' BodySlide — model stores 0..1 fractional; slider works in 0..100 scale (BodySlide canon).
            For Each kv In p.BodyMorphSliders
                Dim bar As FO4_Base_Library.TinySliderTextBox = Nothing
                If _bodySlideBars.TryGetValue(kv.Key, bar) Then
                    bar.Value = kv.Value * 100.0R
                End If
            Next
        Finally
            _suspendEvents = False
        End Try
    End Sub

    ''' <summary>Push (t, m, f) into the three linked TinySliderTextBox controls without
    ''' re-triggering their ValueChanged handlers. Caller mutates state — this only updates
    ''' the on-screen numbers.</summary>
    Private Sub SyncMwgtSliders(t As Single, m As Single, f As Single)
        Dim wasSuspended = _suspendEvents
        _suspendEvents = True
        Try
            SliderThin.Value = CDec(t)
            SliderMuscular.Value = CDec(m)
            SliderFat.Value = CDec(f)
        Finally
            _suspendEvents = wasSuspended
        End Try
    End Sub

    ''' <summary>Single mutation path for MWGT. Both the WeightTriangle drag handler and the
    ''' three linked sliders converge here. Writes the overlay preset, syncs the editor host's
    ''' dual cache (LastRenderedState + CurrentBaseState — required because BuildBodyWeightPose
    ''' reads from state.WeightX, NOT from the overlay; the overlay→state sync only runs on
    ''' full reload via ResolveNPCBaseState — see arch_npc_state_dual_cache.md), refreshes the
    ''' UI mirrors, and schedules a throttled refresh via the existing 500ms timer.</summary>
    Private Sub ApplyMwgt(t As Single, m As Single, f As Single)
        Dim p = Preset
        p.WeightThin = t
        p.WeightMuscular = m
        p.WeightFat = f
        ' Dual-cache sync per arch_npc_state_dual_cache.md: BuildBodyWeightPose reads
        ' state.WeightX (sentinel-substituted by ApplyRaceFallbacks) — not the overlay. During
        ' a live slider edit there is no full reload to re-run that sync, so we mutate both
        ' caches in place. Without this, the editor's preview would not reflect MWGT changes
        ' until the user closes the editor with OK and triggers a full reload.
        If _editorHost IsNot Nothing AndAlso _editorHost.LastRenderedState IsNot Nothing Then
            _editorHost.LastRenderedState.WeightThin = t
            _editorHost.LastRenderedState.WeightMuscular = m
            _editorHost.LastRenderedState.WeightFat = f
        End If
        If _editorHost IsNot Nothing AndAlso _editorHost.CurrentBaseState IsNot Nothing Then
            _editorHost.CurrentBaseState.WeightThin = t
            _editorHost.CurrentBaseState.WeightMuscular = m
            _editorHost.CurrentBaseState.WeightFat = f
        End If
        ' Throttled refresh — same path the BodySlide / MRSV sliders use. Drag many values
        ' through the slider without slamming the render pipeline; FlushRefresh on DragEnded
        ' guarantees the final value renders immediately.
        ScheduleRefresh()
    End Sub

    ''' <summary>Triangle drag handler. The triangle control already enforces sum=1 internally
    ''' (barycentric coordinates), so we just read its three values, mirror them into the linked
    ''' sliders, and route through ApplyMwgt.</summary>
    Private Sub OnWeightTriangleChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim t As Single = WeightTriangle.Thin
        Dim m As Single = WeightTriangle.Muscular
        Dim f As Single = WeightTriangle.Fat
        SyncMwgtSliders(t, m, f)
        ApplyMwgt(t, m, f)
    End Sub

    ''' <summary>Distribute the new value of one MWGT axis across the constrained simplex
    ''' (t + m + f = 1). Strategy: clamp the changed axis to [0..1], then split the remaining
    ''' (1 - changed) across the other two axes proportionally to their CURRENT relative ratio
    ''' so the user perceives the existing distribution being preserved as much as possible.
    '''
    ''' Edge cases:
    '''   • The other two are both 0 (corner of the simplex) → split (1 - changed) 50/50 so we
    '''     never divide by zero AND the user can see two non-zero handles to drag from.
    '''   • changed = 1 → others = 0 (vertex of the simplex).
    '''
    ''' axisIdx: 0=Thin, 1=Muscular, 2=Fat. Returns the new (t, m, f) triple.</summary>
    Private Function RedistributeMwgt(axisIdx As Integer, newValue As Single,
                                       currT As Single, currM As Single, currF As Single) _
                                       As (T As Single, M As Single, F As Single)
        Dim v As Single = Math.Max(0.0F, Math.Min(1.0F, newValue))
        Dim remaining As Single = 1.0F - v
        ' Pick the two "other" current values for proportional split.
        Dim aIdx As Integer, bIdx As Integer
        Select Case axisIdx
            Case 0 : aIdx = 1 : bIdx = 2  ' changed Thin → split between M and F
            Case 1 : aIdx = 0 : bIdx = 2  ' changed Muscular → split between T and F
            Case Else : aIdx = 0 : bIdx = 1  ' changed Fat → split between T and M
        End Select
        Dim curr() As Single = {currT, currM, currF}
        Dim a As Single = curr(aIdx)
        Dim b As Single = curr(bIdx)
        Dim sum As Single = a + b
        Dim na As Single, nb As Single
        If sum < 0.0001F Then
            ' Both other axes were at 0 — split remaining equally so the user has handles to grab.
            na = remaining * 0.5F
            nb = remaining * 0.5F
        Else
            na = remaining * (a / sum)
            nb = remaining * (b / sum)
        End If
        Dim res() As Single = {0.0F, 0.0F, 0.0F}
        res(axisIdx) = v
        res(aIdx) = na
        res(bIdx) = nb
        Return (res(0), res(1), res(2))
    End Function

    ''' <summary>One slider moved → redistribute, push the new triple into the WeightTriangle
    ''' (which absorbs them as its new barycentric position) and the other two sliders, then
    ''' apply.</summary>
    Private Sub OnMwgtSliderChanged(axisIdx As Integer, sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim newVal As Single
        Select Case axisIdx
            Case 0 : newVal = CSng(SliderThin.Value)
            Case 1 : newVal = CSng(SliderMuscular.Value)
            Case Else : newVal = CSng(SliderFat.Value)
        End Select
        Dim currT As Single = WeightTriangle.Thin
        Dim currM As Single = WeightTriangle.Muscular
        Dim currF As Single = WeightTriangle.Fat
        Dim r = RedistributeMwgt(axisIdx, newVal, currT, currM, currF)
        ' Push into triangle + sliders under suspend so we don't recurse.
        Dim wasSuspended = _suspendEvents
        _suspendEvents = True
        Try
            WeightTriangle.SetWeights(r.T, r.M, r.F)
        Finally
            _suspendEvents = wasSuspended
        End Try
        SyncMwgtSliders(r.T, r.M, r.F)
        ApplyMwgt(r.T, r.M, r.F)
    End Sub

    Private Sub OnSliderThinChanged(sender As Object, e As EventArgs) Handles SliderThin.ValueChanged
        OnMwgtSliderChanged(0, sender, e)
    End Sub

    Private Sub OnSliderMuscularChanged(sender As Object, e As EventArgs) Handles SliderMuscular.ValueChanged
        OnMwgtSliderChanged(1, sender, e)
    End Sub

    Private Sub OnSliderFatChanged(sender As Object, e As EventArgs) Handles SliderFat.ValueChanged
        OnMwgtSliderChanged(2, sender, e)
    End Sub

    ''' <summary>Force-flush the throttle on slider DragEnded so releasing the mouse always
    ''' renders the final value without waiting for the timer tick. Mirrors the same wiring
    ''' the MRSV / BodySlide sliders already use.</summary>
    Private Sub OnMwgtSliderDragEnded(sender As Object, e As EventArgs) _
        Handles SliderThin.DragEnded, SliderMuscular.DragEnded, SliderFat.DragEnded
        FlushRefresh()
    End Sub

    Private Sub OnMrsvChanged(idx As Integer)
        If _suspendEvents Then Return
        Dim v As Single = CSng(_mrsvBars(idx).Value)
        Dim p = Preset
        ' Ensure BodyMorphValues has 5 slots — overlay-apply expects positional MRSV.
        While p.BodyMorphValues.Count < 5
            p.BodyMorphValues.Add(0.0F)
        End While
        p.BodyMorphValues(idx) = v
        ScheduleRefresh()
    End Sub

    Private Sub OnBodySlideChanged(sliderName As String)
        If _suspendEvents Then Return
        Dim bar As FO4_Base_Library.TinySliderTextBox = Nothing
        If Not _bodySlideBars.TryGetValue(sliderName, bar) Then Return
        Dim v As Single = CSng(bar.Value / 100.0R)
        Dim p = Preset
        If Math.Abs(v) < 0.001F Then
            p.BodyMorphSliders.Remove(sliderName)
        Else
            p.BodyMorphSliders(sliderName) = v
        End If
        ScheduleRefresh()
    End Sub

    ''' <summary>Mark a refresh as pending and start the throttle timer if it isn't already
    ''' running. The model is already written; this only defers the costly _refresh callback.</summary>
    Private Sub ScheduleRefresh()
        _pendingRefresh = True
        If Not RefreshTimer.Enabled Then RefreshTimer.Start()
    End Sub

    ''' <summary>Force-flush any pending refresh immediately. Bound to every slider's DragEnded
    ''' so releasing the mouse shows the final preview without waiting for the timer tick.
    ''' Two pending channels: _pendingRefresh = morph/pose-only (Weight/MRSV/BodySlide) via the
    ''' lightweight _refresh callback; _pendingOverlayReload = a full reload (re-resolve overlay
    ''' layers) via TriggerSkinChangeReload. Both can be pending; both flush here.</summary>
    Private Sub FlushRefresh()
        If _pendingRefresh Then
            _pendingRefresh = False
            _refresh?.Invoke()
        End If
        If _pendingOverlayReload Then
            _pendingOverlayReload = False
            ' Fire-and-forget the async full reload; the embedded preview updates when it completes.
            FlushOverlayReload()
        End If
        RefreshTimer.Stop()
    End Sub

    ''' <summary>Await the full preview reload then swallow/log faults — separated so FlushRefresh
    ''' stays a plain Sub (DragEnded / timer tick are sync entry points).</summary>
    Private Async Sub FlushOverlayReload()
        ' Lightweight path: offset/scale/alpha slider drags only change overlay material UV/tint params,
        ' not geometry/textures, so re-resolve the layer materials on the existing render data + repaint
        ' instead of a full RenderInHostAsync. Falls back to the full reload if the host has no render
        ' data yet (TriggerOverlayLiveRefresh).
        Try
            Await TriggerOverlayLiveRefresh()
        Catch ex As Exception
            Logger.LogLazy(Function() $"[EDIT-BODY] overlay live refresh failed for NPC 0x{_rootNpcFormID:X8}: {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    Private Sub RefreshTimer_Tick(sender As Object, e As EventArgs) Handles RefreshTimer.Tick
        FlushRefresh()
    End Sub

    Private Sub OnSliderDragEnded(sender As Object, e As EventArgs)
        FlushRefresh()
    End Sub

    Private Sub OnBodySlideFilterChanged(sender As Object, e As EventArgs)
        Dim filter = TextBoxBodySlideFilter.Text.Trim()
        BodySlidePanel.SuspendLayout()
        Try
            For Each kv In _bodySlideRows
                Dim visible = (filter.Length = 0) OrElse
                              (kv.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
                kv.Value.Visible = visible
            Next
        Finally
            BodySlidePanel.ResumeLayout()
        End Try
    End Sub

    ' =====================================================================
    ' BodySlide preset selector — mirrors Wardrobe Manager's preset flow:
    '   • combo of presets from <BS exe dir>\SliderPresets\*.xml (Relee_Presets, WM_Form.vb:2649)
    '   • size combo Default/Big/Small (ComboBoxSize; FO4 ignores the size → disabled there)
    '   • picking a preset moves the tab's sliders to the game-aware resolved values
    '     (SliderSet_Class.SetPreset semantics via BodySlidePresetCatalog.ResolveValues)
    '   • "(none)" / Clear = all sliders to 0 (the rows' baseline — no slider-set XML defaults here)
    '   • exe path + size persist per-game in NPC_Config; the preset NAME does NOT (combo always
    '     opens "(none)" — it reflects this NPC's state, unlike WM's stateless preview combo)
    ' =====================================================================

    Private Function BsExePathForGame() As String
        Return If(_isSSE, NPC_Config.Current.BodySlideExePath_SSE, NPC_Config.Current.BodySlideExePath_FO4)
    End Function

    Private Sub SetBsExePathForGame(path As String)
        If _isSSE Then
            NPC_Config.Current.BodySlideExePath_SSE = If(path, "")
        Else
            NPC_Config.Current.BodySlideExePath_FO4 = If(path, "")
        End If
    End Sub

    Private Function BsSizeForGame() As Integer
        Return If(_isSSE, NPC_Config.Current.BodySlideSize_SSE, NPC_Config.Current.BodySlideSize_FO4)
    End Function

    Private Sub SetBsSizeForGame(idx As Integer)
        If _isSSE Then
            NPC_Config.Current.BodySlideSize_SSE = idx
        Else
            NPC_Config.Current.BodySlideSize_FO4 = idx
        End If
    End Sub

    ''' <summary>Seed the preset selector at form open: size combo from config, exe autodetect
    ''' when unset (game-aware <gameDir>\Data\Tools\Bodyslide (FO4) / Data\CalienteTools\BodySlide
    ''' (SSE) — WM_Config.AutoDetectBSPaths + Config_Form's per-game folder), catalog load, combo
    ''' population with the persisted selection restored WITHOUT applying.</summary>
    Private Sub InitBodySlidePresetSelector()
        ' Size combo: restore persisted index. Game-aware: FO4 presets carry no size variants
        ' (SetPreset ignores the weight under FO4 — OSP_Clases.vb:2498), so the combo is disabled
        ' there; under SSE, Default behaves as Big (SSE presets don't use Default entries).
        _seedingBsPresetCombo = True
        Try
            Dim szIdx = BsSizeForGame()
            ComboBoxBsSize.SelectedIndex = If(szIdx >= 0 AndAlso szIdx <= 2, szIdx, 0)
        Finally
            _seedingBsPresetCombo = False
        End Try
        ComboBoxBsSize.Enabled = _isSSE
        _bsPresetToolTip.SetToolTip(ComboBoxBsSize, If(_isSSE,
            "Body size variant applied with the preset (SSE Big/Small support; Default = Big).",
            "FO4 presets have no size variants (the Default value applies) — SSE-only control."))
        _bsPresetToolTip.SetToolTip(ComboBoxBsPreset, "BodySlide preset: picking one moves the sliders below to the preset's values. ""(none)"" sets all to 0.")
        _bsPresetToolTip.SetToolTip(ButtonBsPresetClear, "Set every BodySlide slider to 0 (and the preset to ""(none)"").")
        _bsPresetToolTip.SetToolTip(ButtonBsPresetBrowse, "Choose the BodySlide/OutfitStudio executable of the current game — presets are read from its SliderPresets folder.")

        ' Exe: autodetect from the game folder when not configured (WM_Config.AutoDetectBSPaths
        ' idiom; the per-game subfolder comes from WM's Config_Form: Tools vs CalienteTools).
        If String.IsNullOrEmpty(BsExePathForGame()) Then
            Dim gameExe = If(Config_App.Current IsNot Nothing, Config_App.Current.FO4ExePath, "")
            If Not String.IsNullOrEmpty(gameExe) AndAlso IO.File.Exists(gameExe) Then
                Try
                    Dim bsDir = IO.Path.Combine(IO.Path.GetDirectoryName(gameExe),
                                                If(_isSSE, "Data\CalienteTools\BodySlide", "Data\Tools\Bodyslide"))
                    Dim exe = BodySlidePresetCatalog.ResolveBsSuiteExePath(bsDir, "BodySlide")
                    If IO.File.Exists(exe) Then
                        SetBsExePathForGame(exe)
                        NPC_Config.SaveConfig()
                    End If
                Catch
                End Try
            End If
        End If

        ReloadBsPresetCatalog()
    End Sub

    ''' <summary>(Re)load the preset catalog from the configured exe's SliderPresets folder and
    ''' repopulate the combo (persisted selection restored, no apply). With no valid exe the combo
    ''' holds just "(none)" and is disabled — the "Set BS exe…" button is the way in.</summary>
    Private Sub ReloadBsPresetCatalog()
        _bsPresetCatalog = Nothing
        Dim exePath = BsExePathForGame()
        If Not String.IsNullOrEmpty(exePath) AndAlso IO.File.Exists(exePath) Then
            Dim presetsDir = IO.Path.Combine(IO.Path.GetDirectoryName(exePath), "SliderPresets")
            If IO.Directory.Exists(presetsDir) Then
                Dim cat As New BodySlidePresetCatalog()
                cat.LoadFolder(presetsDir)
                _bsPresetCatalog = cat
            End If
        End If
        PopulateBsPresetCombo()
        ComboBoxBsPreset.Enabled = (_bsPresetCatalog IsNot Nothing)
    End Sub

    ''' <summary>Fill the preset combo: "(none)" + the catalog's presets (SortedDictionary =
    ''' alphabetical, same as WM's Relee_Presets), selection at "(none)". The combo describes THIS
    ''' NPC's state — it always opens at "(none)" because nothing has been applied yet; restoring a
    ''' remembered name would claim a preset the NPC's sliders don't carry.</summary>
    Private Sub PopulateBsPresetCombo()
        _seedingBsPresetCombo = True
        Try
            ComboBoxBsPreset.BeginUpdate()
            ComboBoxBsPreset.Items.Clear()
            ComboBoxBsPreset.Items.Add(BsPresetNone)
            If _bsPresetCatalog IsNot Nothing Then
                For Each presetName In _bsPresetCatalog.Presets.Keys
                    ComboBoxBsPreset.Items.Add(presetName)
                Next
            End If
            ComboBoxBsPreset.SelectedIndex = 0
            ComboBoxBsPreset.EndUpdate()
        Finally
            _seedingBsPresetCombo = False
        End Try
    End Sub

    ''' <summary>User picked a preset (or "(none)"): move the sliders. Nothing persists here —
    ''' the pick is an action on this NPC, not an app preference.</summary>
    Private Sub OnBsPresetComboChanged(sender As Object, e As EventArgs)
        If _seedingBsPresetCombo Then Return
        If ComboBoxBsPreset.SelectedIndex < 0 Then Return
        ApplyBsPresetSelection()
    End Sub

    ''' <summary>Size changed: persist and re-apply the current preset so the sliders reflect the
    ''' new variant. With "(none)" selected there's nothing to re-apply — the user's manual slider
    ''' edits must not be wiped by a size flip.</summary>
    Private Sub OnBsSizeComboChanged(sender As Object, e As EventArgs)
        If _seedingBsPresetCombo Then Return
        If ComboBoxBsSize.SelectedIndex < 0 Then Return
        SetBsSizeForGame(ComboBoxBsSize.SelectedIndex)
        NPC_Config.SaveConfig()
        If ComboBoxBsPreset.SelectedIndex >= 1 Then ApplyBsPresetSelection()
    End Sub

    ''' <summary>Clear: every slider to 0 (the existing per-tab wipe) and the combo back to
    ''' "(none)".</summary>
    Private Sub OnBsPresetClear(sender As Object, e As EventArgs)
        _seedingBsPresetCombo = True
        Try
            If ComboBoxBsPreset.Items.Count > 0 Then ComboBoxBsPreset.SelectedIndex = 0
        Finally
            _seedingBsPresetCombo = False
        End Try
        ResetBodySlideSection()
    End Sub

    ''' <summary>Pick the BodySlide/OutfitStudio exe for the current game (NPC_Manager's preflight
    ''' doesn't ask for it — only WM's config dialog does). Requires a SliderPresets sibling folder,
    ''' the actual thing the selector consumes; both suite exes live in the same folder.</summary>
    Private Sub OnBsPresetBrowseExe(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog() With {
            .Title = "Select the BodySlide or OutfitStudio executable",
            .Filter = "BodySlide / OutfitStudio (*.exe)|*.exe",
            .CheckFileExists = True
        }
            Dim current = BsExePathForGame()
            If Not String.IsNullOrEmpty(current) AndAlso IO.Directory.Exists(IO.Path.GetDirectoryName(current)) Then
                dlg.InitialDirectory = IO.Path.GetDirectoryName(current)
            End If
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim presetsDir = IO.Path.Combine(IO.Path.GetDirectoryName(dlg.FileName), "SliderPresets")
            If Not IO.Directory.Exists(presetsDir) Then
                MsgBox("No 'SliderPresets' folder found next to the selected executable — that's where BodySlide keeps its presets. Pick the exe inside a BodySlide installation.",
                       MsgBoxStyle.Exclamation, "BodySlide presets")
                Return
            End If
            SetBsExePathForGame(dlg.FileName)
            NPC_Config.SaveConfig()
            ReloadBsPresetCatalog()
        End Using
    End Sub

    ''' <summary>Move the tab's sliders to the selected preset's game-aware values: baseline 0 for
    ''' every row (WM re-baselines to the slider-set XML defaults first — SetPreset, OSP_Clases.vb:2488;
    ''' the PIRT rows have no XML default, so the baseline is 0), then the preset's matching values on
    ''' top. Model + bars updated together, one refresh at the end (same shape as ResetBodySlideSection).
    ''' "(none)" resolves to no values = all zeros.</summary>
    Private Sub ApplyBsPresetSelection()
        Dim values As Dictionary(Of String, Single) = Nothing   ' slider → percent (0-100)
        If ComboBoxBsPreset.SelectedIndex >= 1 AndAlso _bsPresetCatalog IsNot Nothing Then
            Dim def As BodySlidePresetCatalog.PresetDef = Nothing
            If _bsPresetCatalog.Presets.TryGetValue(ComboBoxBsPreset.SelectedItem.ToString(), def) Then
                Dim size = CType(Math.Max(0, ComboBoxBsSize.SelectedIndex), BodySlidePresetCatalog.PresetSliderSize)
                Dim game = If(_isSSE, Config_App.Game_Enum.Skyrim, Config_App.Game_Enum.Fallout4)
                values = BodySlidePresetCatalog.ResolveValues(def, size, game)
            End If
        End If
        Dim p = Preset
        p.BodyMorphSliders.Clear()
        _suspendEvents = True
        Try
            For Each kv In _bodySlideBars
                Dim v As Single = 0
                If values IsNot Nothing Then values.TryGetValue(kv.Key, v)
                kv.Value.Value = CDbl(v)
                ' Same dead-zone the per-slider handler uses (OnBodySlideChanged): |v|<0.1% drops the key.
                If Math.Abs(v) >= 0.1F Then p.BodyMorphSliders(kv.Key) = v / 100.0F
            Next
        Finally
            _suspendEvents = False
        End Try
        _refresh?.Invoke()
    End Sub

    ''' <summary>Per-tab reset, mirroring EditFace_Form.OnResetSection. Active-tab dispatch:
    '''   • Body tab → revert MWGT + MRSV + Skin combos to the values captured at form-open time.
    '''   • BodySlide tab → wipe all BodySlide sliders to 0.
    ''' Same idea as Cancel but scoped to the active tab so the user can discard one tab's edits
    ''' without losing the others.</summary>
    Private Async Sub OnResetSection(sender As Object, e As EventArgs)
        Dim active = TabsBody.SelectedTab
        If active Is TabPageBody Then
            Await ResetBodySection()
        ElseIf active Is TabPageBodySlide Then
            ResetBodySlideSection()
        ElseIf active Is TabPageOverlays Then
            Await ResetOverlaysSection()
        ElseIf active IsNot Nothing AndAlso active.Name = "TabPageSseBodyScale" Then
            ResetSseBodyScaleSection()
        ElseIf active IsNot Nothing AndAlso active.Name = "TabPageSseSkinOverrides" Then
            Await ResetSseSkinOverridesSection()
        End If
    End Sub

    ''' <summary>SSE: revert RaceMenu body-scale node transforms to the pre-edit snapshot and re-apply the pose.</summary>
    Private Sub ResetSseBodyScaleSection()
        Dim p = Preset
        If p Is Nothing Then Return
        p.SseNodeTransforms = LooksmenuLoader.CloneSseNodeTransforms(If(_priorPreset Is Nothing, Nothing, _priorPreset.SseNodeTransforms))
        RefreshSseBodyScaleBars()
        _mainForm.RebuildAndApplyMergedPose(_editorHost)
        _editorHost.PreviewCtl.InvalidateRender()
    End Sub

    ''' <summary>SSE: revert RaceMenu skin overrides (body-paint) to the pre-edit snapshot and re-render.</summary>
    Private Async Function ResetSseSkinOverridesSection() As Task
        Dim p = Preset
        If p Is Nothing Then Return
        p.SseSkinOverrides = LooksmenuLoader.CloneSseSkinOverrides(If(_priorPreset Is Nothing, Nothing, _priorPreset.SseSkinOverrides))
        RefreshSseSkinList(-1)
        Await TriggerOverlayReload()
    End Function

    ''' <summary>Revert MWGT (Weight triangle + 3 sliders), MRSV (5 region bars), and Skin combos
    ''' (NPC.WNAM + LM template) to the snapshot taken at form-open time. The combos go back to
    ''' whatever the prior overlay carried, falling back to "(use RACE default)" / "(none)" when
    ''' the user opened Edit Body on an NPC with no prior overlay.</summary>
    Private Async Function ResetBodySection() As Task
        Dim p = Preset
        _suspendEvents = True
        Try
            ' SSE weight — revert NAM7 to the value captured at open time. (MWGT/MRSV below are hidden
            ' under _isSSE but their reverts are harmless: WeightTriangle is hidden, _mrsvBars are Nothing.)
            If _isSSE Then
                p.SseWeight = _initialSseWeight
                If _sseWeightSlider IsNot Nothing Then
                    _sseWeightSlider.Value = Math.Max(0.0R, Math.Min(100.0R, Math.Round(CDbl(_initialSseWeight))))
                End If
            End If
            ' MWGT — back to initial values seeded from the live NPC at open time.
            If _initialSeed IsNot Nothing Then
                p.WeightThin = _initialSeed.Thin
                p.WeightMuscular = _initialSeed.Muscular
                p.WeightFat = _initialSeed.Fat
                WeightTriangle.SetWeights(_initialSeed.Thin, _initialSeed.Muscular, _initialSeed.Fat)
                SyncMwgtSliders(_initialSeed.Thin, _initialSeed.Muscular, _initialSeed.Fat)
            End If

            ' MRSV — back to initial 5-region values. ApplyPresetOverlayToNpcData reads
            ' BodyMorphValues positionally, so we keep exactly 5 entries. Skipped under Skyrim, where the
            ' channel doesn't exist and must stay untouched (see SeedOverlayFromInitial's seedMrsv).
            If Not _isSSE Then
                p.BodyMorphValues.Clear()
                For i = 0 To 4
                    Dim v As Single = If(_initialSeed IsNot Nothing AndAlso _initialSeed.Mrsv IsNot Nothing AndAlso i < _initialSeed.Mrsv.Length, _initialSeed.Mrsv(i), 0.0F)
                    p.BodyMorphValues.Add(v)
                    If i < _mrsvBars.Length AndAlso _mrsvBars(i) IsNot Nothing Then _mrsvBars(i).Value = v
                Next
                p.HasBodyMorphValues = True
            End If

            ' Skin combos — revert to the prior overlay's choices. Without a prior overlay the
            ' user-explicit override is cleared (Nothing) so the combo falls back to the NPC's
            ' raw effective WNAM (whatever PopulateSkinCombos pinned at index 1 / "(use RACE default)").
            If _priorPreset IsNot Nothing Then
                p.SkinFormIDOverride = _priorPreset.SkinFormIDOverride
                p.SkinTemplateId = If(_priorPreset.SkinTemplateId, "")
            Else
                p.SkinFormIDOverride = Nothing
                p.SkinTemplateId = ""
            End If
            ' Reflect the new preset state in the combo selection without re-firing the change
            ' handlers (they would otherwise trigger a render reload per combo, two reloads total).
            Dim wnamFid As UInteger = If(p.SkinFormIDOverride, _initialWnamFormID)
            Dim wIdx = _wnamComboFormIDs.IndexOf(wnamFid)
            ComboBoxWnam.SelectedIndex = If(wIdx >= 0, wIdx, 0)
            Dim lmIdx = _lmTemplateComboIds.IndexOf(If(p.SkinTemplateId, ""))
            ComboBoxLmSkinTemplate.SelectedIndex = If(lmIdx >= 0, lmIdx, 0)
        Finally
            _suspendEvents = False
        End Try
        ' SSE weight is a morph channel — the skin fast-path may not re-run morphs, so rebuild the
        ' morph resolver explicitly (OnLocalBodyRefresh) so the weight revert lands. Harmless on FO4.
        If _isSSE Then _refresh?.Invoke()
        ' Skin change requires a full reload (mesh + TXST + MSWP resolve from state.SkinFormID),
        ' which TriggerSkinChangeReload handles. It also re-runs MWGT / MRSV pose so the weight
        ' + region revert lands in the same render pass.
        Await TriggerSkinChangeReload()
    End Function

    ''' <summary>Wipe all BodySlide sliders to 0 (no PIRT vertex morph applied). Prior behaviour
    ''' of the (now-renamed) OnResetBodySlide handler.</summary>
    Private Sub ResetBodySlideSection()
        Dim p = Preset
        p.BodyMorphSliders.Clear()
        _suspendEvents = True
        Try
            For Each kv In _bodySlideBars
                kv.Value.Value = 0R
            Next
        Finally
            _suspendEvents = False
        End Try
        _refresh?.Invoke()
    End Sub

    ' =====================================================================
    ' SSE (Skyrim) body weight — code-built single 0-100 slider + Load/Save .jslot.
    ' Mirror EditFace_Form.BuildSseMorphTab / OnLoadJslot / OnSaveJslot. Game-gated: only built /
    ' called under _isSSE. The weight lives on the overlay preset (SseWeight); the SSE vanilla _0/_1
    ' LERP resolver reads the overlay-applied NAM7, so slider edits render live through the SAME cheap
    ' morph-dirty path (OnLocalBodyRefresh) the BodySlide sliders use.
    ' =====================================================================

    ''' <summary>Build the code-built SSE weight section into the Body tab: a top bar with Load/Save
    ''' .jslot buttons and a single 0..100 <see cref="FO4_Base_Library.TinySliderTextBox"/> (the same slider
    ''' style the BodySlide / MRSV rows use), in the cell GroupBoxWeight occupied (GroupBoxWeight is hidden
    ''' under _isSSE). Mirrors BuildSseMorphTab.</summary>
    Private Sub BuildSseWeightSection()
        Dim grp As New GroupBox() With {
            .Text = "Weight (NPC.NAM7 — SSE _0 / _1 body morph)",
            .Dock = DockStyle.Fill,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .TabStop = False
        }
        Dim layout As New TableLayoutPanel() With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 2,
            .RowCount = 2,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(4)
        }
        layout.ColumnStyles.Add(New ColumnStyle())
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        layout.RowStyles.Add(New RowStyle())
        layout.RowStyles.Add(New RowStyle())

        ' A .jslot is a whole-character snapshot (face + body + RaceMenu overrides). Loading one from inside the
        ' body editor applied only its body half, leaving face and body from different presets. Preset load/save
        ' lives at the main window, where it is a single action over the whole preset — and where the presets are
        ' LISTED (as RaceMenu lists them) instead of being fished out of a file dialog.
        Dim topBar As New Label() With {
            .Dock = DockStyle.Fill, .AutoSize = True,
            .Text = "Vanilla — stored in the NPC record (NPC.NAM7). Load/Save a RaceMenu preset from the main window.",
            .Margin = New Padding(0, 0, 0, 4)
        }
        layout.Controls.Add(topBar, 0, 0)
        layout.SetColumnSpan(topBar, 2)

        Dim lbl As New Label() With {
            .Text = "Weight (NPC.NAM7)", .AutoSize = True, .Anchor = AnchorStyles.Left,
            .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(2, 6, 8, 2)
        }
        layout.Controls.Add(lbl, 0, 1)
        ' Same TinySliderTextBox the BodySlide / MRSV rows use (0..100, integer display), replacing the raw
        ' TrackBar so the SSE weight matches the app's slider style.
        _sseWeightSlider = New FO4_Base_Library.TinySliderTextBox() With {
            .Minimum = 0R,
            .Maximum = 100.0R,
            .DisplayFormat = "0",
            .SmallChange = 1.0R,
            .LargeChange = 10.0R,
            .Height = 28,
            .Value = 100.0R,
            .Dock = DockStyle.Fill,
            .Margin = New Padding(2)
        }
        AddHandler _sseWeightSlider.ValueChanged, AddressOf OnSseWeightChanged
        AddHandler _sseWeightSlider.DragEnded, AddressOf OnSliderDragEnded
        layout.Controls.Add(_sseWeightSlider, 1, 1)

        grp.Controls.Add(layout)
        BodyTabLayout.Controls.Add(grp, 0, 0)   ' same cell as the hidden GroupBoxWeight

        ' Seed the slider from the overlay's SseWeight (already seeded in the ctor from the effective NAM7).
        Dim p = Preset
        Dim w As Single = If(p IsNot Nothing AndAlso p.SseWeight.HasValue, p.SseWeight.Value, _initialSseWeight)
        Dim iv As Double = Math.Max(0.0R, Math.Min(100.0R, Math.Round(CDbl(w))))
        _suspendEvents = True
        Try
            _sseWeightSlider.Value = iv
        Finally
            _suspendEvents = False
        End Try
    End Sub

    ''' <summary>SSE weight slider moved → write preset.SseWeight and schedule the throttled morph
    ''' refresh. The SSE _0/_1 LERP is a morph channel: OnLocalBodyRefresh rebuilds the composite morph
    ''' resolver, which reads the overlay-applied NAM7 (preset.SseWeight → shadow.Nam7Raw). DragEnded
    ''' (OnSliderDragEnded → FlushRefresh) forces the final value to render immediately.</summary>
    Private Sub OnSseWeightChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        If _sseWeightSlider Is Nothing Then Return
        Dim v As Single = CSng(_sseWeightSlider.Value)
        Dim p = Preset
        If p IsNot Nothing Then p.SseWeight = v
        ScheduleRefresh()
    End Sub

    ' --- RaceMenu · Body Scale (NiOverride node transforms — full TRS) -------------------------------------
    ' A node transform is a per-bone override RaceMenu writes: scale (key 30), position (key 31) and rotation
    ' (key 32). Rather than one flat slider per node (scale-only), the tab is a NODE LIST + a per-node TRS detail:
    ' pick a bone, edit its Scale / Position X-Y-Z / Rotation X-Y-Z. Editing writes Preset.SseNodeTransforms and
    ' re-applies the merged pose live (a node transform is a BONE pose, not a vertex morph). Rotation is edited in
    ' euler DEGREES but stored as the axis-angle the render consumes (Transform_Class.BSRotationToMatrix33), so it
    ' reproduces the .jslot's 3×3 matrix exactly. FO4 has no node-transform system → SSE-only tab.
    Private _sseNodeList As ListBox
    Private ReadOnly _sseNodeItems As New List(Of (Label As String, Node As String))
    Private _sseNodeDetailPanel As Control
    Private _sseNodeScaleBar As FO4_Base_Library.TinySliderTextBox
    Private _sseNodePosX As FO4_Base_Library.TinySliderTextBox
    Private _sseNodePosY As FO4_Base_Library.TinySliderTextBox
    Private _sseNodePosZ As FO4_Base_Library.TinySliderTextBox
    Private _sseNodeRotX As FO4_Base_Library.TinySliderTextBox
    Private _sseNodeRotY As FO4_Base_Library.TinySliderTextBox
    Private _sseNodeRotZ As FO4_Base_Library.TinySliderTextBox
    Private _sseNodeSelected As String
    Private _sseShowAllNodes As Boolean = False
    Private _sseShowAllCheck As CheckBox

    ''' <summary>Piso de la escala de un node transform. Un factor 0 deja la matriz del hueso singular
    ''' (geometría colapsada, normal indefinida ⇒ inversa imposible), así que el editor no permite bajar
    ''' de 0.01 — visualmente equivale a "invisible" sin producir una matriz degenerada.</summary>
    Private Const MinNodeScale As Double = 0.01R

    ''' <summary>Fill <see cref="_sseNodeItems"/> for the current show-all state. Faithful to RaceMenu: the node list
    ''' is the union of what plugins REGISTER, not a skeleton scan. (1) RaceMenu's built-in body nodes (verified from
    ''' RaceMenuPlugin.pex) present on this rig; (2) the dynamic <see cref="FO4_Base_Library.RaceMenuNodeCatalog"/> —
    ''' nodes other installed scripts (XPMSE, …) register through the same NiOverride API, rig-filtered so a stray
    ''' non-bone string is dropped; (3) any node the loaded preset carries (never hide authored data). With show-all
    ''' on, also the weapon nodes and EVERY remaining rig bone (RaceMenu accepts any node — power-user view).</summary>
    Private Sub RebuildSseNodeItems()
        Dim rigBones As HashSet(Of String) = Nothing
        Dim skel = _editorHost?.LastSkeletonInstance
        If skel IsNot Nothing AndAlso skel.SkeletonDictionary IsNot Nothing AndAlso skel.SkeletonDictionary.Count > 0 Then
            rigBones = New HashSet(Of String)(skel.SkeletonDictionary.Keys, StringComparer.OrdinalIgnoreCase)
        End If
        _sseNodeItems.Clear()
        ' RaceMenu's built-in body nodes are shown IN FULL, NOT rig-filtered: RaceMenu registers these ~12 sliders
        ' regardless of the actor's skeleton (a vanilla rig has no breast/butt bones, so those sliders exist but do
        ' nothing — exactly RaceMenu's own behaviour). Rig-filtering here would hide them on a vanilla NPC.
        For Each n In SseCatalogs.RaceMenuBaseBodyNodes
            AddSseNodeItem(n.Label, n.Node)
        Next
        ' Dynamic catalog: nodes OTHER installed scripts (XPMSE, …) register. This one IS rig-filtered because it is
        ' heuristic (string-table scan) and its extra CME/… nodes only mean something on a rig that actually has them
        ' — so a CME node surfaces only when XPMSE's skeleton is present. The verified base above already guarantees
        ' the RaceMenu set shows in full regardless.
        Dim cat = FO4_Base_Library.RaceMenuNodeCatalog.Current
        If cat IsNot Nothing Then
            For Each node In cat.Nodes()
                ' Weapon/equip nodes (from RaceMenuPlugin's Body Scales too) are gated behind "show all" — the
                ' NPC-appearance preview does not render equipped gear, so scaling them shows nothing here.
                If IsWeaponNode(node) AndAlso Not _sseShowAllNodes Then Continue For
                If rigBones Is Nothing OrElse rigBones.Contains(node) Then AddSseNodeItem(FriendlyNodeLabel(node), node)
            Next
        End If
        Dim p = Preset
        If p IsNot Nothing AndAlso p.SseNodeTransforms IsNot Nothing Then
            For Each nt In p.SseNodeTransforms
                If nt IsNot Nothing AndAlso Not String.IsNullOrEmpty(nt.NodeName) Then AddSseNodeItem(FriendlyNodeLabel(nt.NodeName), nt.NodeName)
            Next
        End If
        If _sseShowAllNodes Then
            For Each n In SseCatalogs.RaceMenuBaseWeaponNodes
                If rigBones Is Nothing OrElse rigBones.Contains(n.Node) Then AddSseNodeItem(n.Label, n.Node)
            Next
            If rigBones IsNot Nothing Then
                For Each bone In rigBones.OrderBy(Function(s) s, StringComparer.OrdinalIgnoreCase)
                    AddSseNodeItem(bone, bone)
                Next
            End If
        End If
    End Sub

    ''' <summary>A weapon/equip node (WEAPON/SHIELD/QUIVER/Weapon*) — RaceMenu scales these too, but they are gated
    ''' behind "show all" here since the appearance preview does not render equipped gear.</summary>
    Private Shared Function IsWeaponNode(node As String) As Boolean
        If String.IsNullOrEmpty(node) Then Return False
        If node.StartsWith("Weapon", StringComparison.OrdinalIgnoreCase) Then Return True
        Select Case node.ToUpperInvariant()
            Case "WEAPON", "SHIELD", "QUIVER" : Return True
        End Select
        Return False
    End Function

    Private Sub AddSseNodeItem(label As String, node As String)
        If String.IsNullOrEmpty(node) Then Return
        If _sseNodeItems.Any(Function(x) String.Equals(x.Node, node, StringComparison.OrdinalIgnoreCase)) Then Return
        _sseNodeItems.Add((label, node))
    End Sub

    ''' <summary>Friendly label for a node if it is one of RaceMenu's known base nodes, else the raw node name.</summary>
    Private Shared Function FriendlyNodeLabel(node As String) As String
        For Each n In SseCatalogs.RaceMenuBaseBodyNodes
            If String.Equals(n.Node, node, StringComparison.OrdinalIgnoreCase) Then Return n.Label
        Next
        For Each n In SseCatalogs.RaceMenuBaseWeaponNodes
            If String.Equals(n.Node, node, StringComparison.OrdinalIgnoreCase) Then Return n.Label
        Next
        Return node
    End Function

    Private Sub OnSseShowAllNodesChanged(sender As Object, e As EventArgs)
        If _sseShowAllCheck Is Nothing Then Return
        _sseShowAllNodes = _sseShowAllCheck.Checked
        RebuildSseNodeItems()
        PopulateSseNodeList(0)
    End Sub

    Private Sub BuildSseBodyScaleTab()
        Dim tab As New TabPage("Body Transform") With {.Name = "TabPageSseBodyScale", .Padding = New Padding(6)}
        Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 2}
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42))
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 40))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        Dim header As New Label With {.Dock = DockStyle.Fill, .AutoSize = False, .Padding = New Padding(3, 4, 3, 0),
            .Text = "RaceMenu node transforms (NiOverride): per-bone scale, position and rotation. Pick a node, then edit its TRS. Loaded/saved with the .jslot + sidecar."}
        root.Controls.Add(header, 0, 0) : root.SetColumnSpan(header, 2)

        ' Left column: "show all" toggle above the node list. The list itself is filled by RebuildSseNodeItems
        ' (RaceMenu's registered body nodes ∪ the dynamic node catalog ∪ preset nodes; + weapons + all rig bones
        ' when show-all is on).
        Dim leftCol As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2}
        leftCol.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        leftCol.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        _sseShowAllCheck = New CheckBox With {.Text = "Show all rig bones (+ weapons)", .AutoSize = True,
                                              .Checked = _sseShowAllNodes, .Margin = New Padding(3, 3, 3, 3)}
        _sseShowAllCheck.FlatStyle = FlatStyle.Standard
        AddHandler _sseShowAllCheck.CheckedChanged, AddressOf OnSseShowAllNodesChanged
        Dim tip As New ToolTip()
        tip.SetToolTip(_sseShowAllCheck, "RaceMenu only exposes a registered set of nodes (RaceMenuPlugin + XPMSE). " &
                       "Off = that faithful set (present on this rig) plus any node the preset uses. On = also the " &
                       "weapon nodes and every other bone the skeleton has.")
        leftCol.Controls.Add(_sseShowAllCheck, 0, 0)
        _sseNodeList = New ListBox With {.Dock = DockStyle.Fill, .IntegralHeight = False}
        AddHandler _sseNodeList.SelectedIndexChanged, AddressOf OnSseNodeSelChanged
        leftCol.Controls.Add(_sseNodeList, 0, 1)
        root.Controls.Add(leftCol, 0, 1)
        RebuildSseNodeItems()

        ' Detail: labeled TinySlider rows — Scale (0..2), Position X/Y/Z (centred), Rotation X/Y/Z in degrees
        ' (centred), then a per-node Reset RIGHT under the last slider. The rows live in an AutoSize/Dock.Top
        ' TableLayoutPanel inside a scrollable panel so the Reset button hugs the sliders instead of a stretched
        ' filler pushing it to the bottom. AllowExtremeValues lets a typed value exceed the slider range.
        Dim rightPanel As New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True}
        _sseNodeDetailPanel = rightPanel
        Dim detail As New TableLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .ColumnCount = 2}
        detail.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 118))
        detail.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        _sseNodeScaleBar = MakeNodeSlider(MinNodeScale, 2.0R, "0.00", FO4_Base_Library.TinySliderFillMode.Left)
        _sseNodePosX = MakeNodeSlider(-20.0R, 20.0R, "0.00", FO4_Base_Library.TinySliderFillMode.Center)
        _sseNodePosY = MakeNodeSlider(-20.0R, 20.0R, "0.00", FO4_Base_Library.TinySliderFillMode.Center)
        _sseNodePosZ = MakeNodeSlider(-20.0R, 20.0R, "0.00", FO4_Base_Library.TinySliderFillMode.Center)
        _sseNodeRotX = MakeNodeSlider(-180.0R, 180.0R, "0.0", FO4_Base_Library.TinySliderFillMode.Center)
        _sseNodeRotY = MakeNodeSlider(-180.0R, 180.0R, "0.0", FO4_Base_Library.TinySliderFillMode.Center)
        _sseNodeRotZ = MakeNodeSlider(-180.0R, 180.0R, "0.0", FO4_Base_Library.TinySliderFillMode.Center)
        AddHandler _sseNodeScaleBar.ValueChanged, Sub(s, e) OnSseNodeScaleChanged()
        AddHandler _sseNodePosX.ValueChanged, Sub(s, e) OnSseNodePosChanged()
        AddHandler _sseNodePosY.ValueChanged, Sub(s, e) OnSseNodePosChanged()
        AddHandler _sseNodePosZ.ValueChanged, Sub(s, e) OnSseNodePosChanged()
        AddHandler _sseNodeRotX.ValueChanged, Sub(s, e) OnSseNodeRotChanged()
        AddHandler _sseNodeRotY.ValueChanged, Sub(s, e) OnSseNodeRotChanged()
        AddHandler _sseNodeRotZ.ValueChanged, Sub(s, e) OnSseNodeRotChanged()
        For Each b In {_sseNodeScaleBar, _sseNodePosX, _sseNodePosY, _sseNodePosZ, _sseNodeRotX, _sseNodeRotY, _sseNodeRotZ}
            AddHandler b.DragEnded, AddressOf OnSliderDragEnded
        Next
        Dim r = 0
        AddNodeDetailRow(detail, r, "Scale", _sseNodeScaleBar) : r += 1
        AddNodeDetailRow(detail, r, "Position X", _sseNodePosX) : r += 1
        AddNodeDetailRow(detail, r, "Position Y", _sseNodePosY) : r += 1
        AddNodeDetailRow(detail, r, "Position Z", _sseNodePosZ) : r += 1
        AddNodeDetailRow(detail, r, "Rotation X (°)", _sseNodeRotX) : r += 1
        AddNodeDetailRow(detail, r, "Rotation Y (°)", _sseNodeRotY) : r += 1
        AddNodeDetailRow(detail, r, "Rotation Z (°)", _sseNodeRotZ) : r += 1
        Dim btnReset As New Button With {.Text = "Reset node", .AutoSize = True, .Margin = New Padding(0, 4, 3, 3)}
        AddHandler btnReset.Click, AddressOf OnSseNodeResetClick
        ' Button row sits directly below the slider grid (Dock.Top stacking: the LAST-added Dock.Top control ends up
        ' on top, so add the button row FIRST and the slider grid second).
        Dim btnRow As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True,
                                                .Padding = New Padding(118, 0, 0, 0), .Margin = New Padding(0)}
        btnRow.Controls.Add(btnReset)
        rightPanel.Controls.Add(btnRow)
        rightPanel.Controls.Add(detail)
        root.Controls.Add(rightPanel, 1, 1)

        tab.Controls.Add(root)
        TabsBody.TabPages.Add(tab)
        PopulateSseNodeList(0)
    End Sub

    Private Function MakeNodeSlider(min As Double, max As Double, fmt As String, fill As FO4_Base_Library.TinySliderFillMode) As FO4_Base_Library.TinySliderTextBox
        Return New FO4_Base_Library.TinySliderTextBox With {
            .Minimum = min, .Maximum = max, .DisplayFormat = fmt, .SmallChange = 0.01R, .LargeChange = 0.1R,
            .FillMode = fill, .AllowExtremeValues = True, .Height = 26, .Dock = DockStyle.Fill}
    End Function

    Private Shared Sub AddNodeDetailRow(detail As TableLayoutPanel, row As Integer, label As String, bar As Control)
        detail.RowStyles.Add(New RowStyle(SizeType.Absolute, 32))
        detail.Controls.Add(New Label With {.Text = label, .Anchor = AnchorStyles.Left, .AutoSize = True, .Margin = New Padding(3, 8, 3, 0)}, 0, row)
        detail.Controls.Add(bar, 1, row)
    End Sub

    ''' <summary>Fill the node ListBox from <see cref="_sseNodeItems"/>, marking nodes that carry a non-identity
    ''' transform with a ●, then load the selected node's TRS into the detail sliders.</summary>
    Private Sub PopulateSseNodeList(selectIndex As Integer)
        If _sseNodeList Is Nothing Then Return
        _suspendEvents = True
        Try
            _sseNodeList.BeginUpdate()
            _sseNodeList.Items.Clear()
            For Each it In _sseNodeItems
                _sseNodeList.Items.Add(SseNodeRowLabel(it.Label, it.Node))
            Next
            _sseNodeList.EndUpdate()
            If _sseNodeList.Items.Count > 0 Then _sseNodeList.SelectedIndex = Math.Max(0, Math.Min(selectIndex, _sseNodeList.Items.Count - 1))
        Finally
            _suspendEvents = False
        End Try
        LoadSseNodeDetail()
    End Sub

    Private Function SseNodeRowLabel(label As String, node As String) As String
        Dim nt = FindSseNodeTransform(node)
        Return If(nt IsNot Nothing AndAlso Not nt.IsIdentity, label & "  ●", label)
    End Function

    Private Function SelectedSseNode() As String
        If _sseNodeList Is Nothing Then Return Nothing
        Dim i = _sseNodeList.SelectedIndex
        If i < 0 OrElse i >= _sseNodeItems.Count Then Return Nothing
        Return _sseNodeItems(i).Node
    End Function

    Private Function FindSseNodeTransform(node As String) As RaceMenuJslot.JslotNodeTransform
        Dim p = Preset
        If p Is Nothing OrElse p.SseNodeTransforms Is Nothing OrElse String.IsNullOrEmpty(node) Then Return Nothing
        Return p.SseNodeTransforms.FirstOrDefault(Function(x) x IsNot Nothing AndAlso String.Equals(x.NodeName, node, StringComparison.OrdinalIgnoreCase))
    End Function

    Private Sub OnSseNodeSelChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        LoadSseNodeDetail()
    End Sub

    ''' <summary>Load the selected node's TRS into the detail sliders (rotation shown as euler degrees).</summary>
    Private Sub LoadSseNodeDetail()
        _sseNodeSelected = SelectedSseNode()
        Dim nt = FindSseNodeTransform(_sseNodeSelected)
        Dim has = Not String.IsNullOrEmpty(_sseNodeSelected)
        _suspendEvents = True
        Try
            If _sseNodeDetailPanel IsNot Nothing Then _sseNodeDetailPanel.Enabled = has
            _sseNodeScaleBar.Value = If(nt IsNot Nothing AndAlso nt.HasScale, CDbl(nt.Scale), 1.0R)
            _sseNodePosX.Value = If(nt IsNot Nothing AndAlso nt.HasPosition, CDbl(nt.PosX), 0.0R)
            _sseNodePosY.Value = If(nt IsNot Nothing AndAlso nt.HasPosition, CDbl(nt.PosY), 0.0R)
            _sseNodePosZ.Value = If(nt IsNot Nothing AndAlso nt.HasPosition, CDbl(nt.PosZ), 0.0R)
            Dim deg = SseNodeRotDegrees(nt)
            _sseNodeRotX.Value = deg.X : _sseNodeRotY.Value = deg.Y : _sseNodeRotZ.Value = deg.Z
        Finally
            _suspendEvents = False
        End Try
    End Sub

    ''' <summary>The node's rotation as STANDARD euler degrees (X = rotation about X, Y about Y, Z about Z) for the
    ''' UI sliders. Convention taken from the app's byte-verified TRS reference <see cref="FaceBonePoseBuilder"/>
    ''' (validated against CK FaceGen), which converts a standard euler to the render's axis-angle as
    ''' <c>Matrix33ToBSRotation(EulerXYZToMatrix33(-x, -y, -z))</c> — the NEGATION undoes EulerXYZToMatrix33's
    ''' J·R·J permutation, whose raw params are (yaw=Z, pitch=Y, roll=X). This function is the exact inverse of
    ''' <see cref="OnSseNodeRotChanged"/>, so display→edit→display round-trips.</summary>
    Private Shared Function SseNodeRotDegrees(nt As RaceMenuJslot.JslotNodeTransform) As System.Numerics.Vector3
        If nt Is Nothing OrElse Not nt.HasRotation Then Return New System.Numerics.Vector3(0, 0, 0)
        Dim m = FO4_Base_Library.Transform_Class.BSRotationToMatrix33(New System.Numerics.Vector3(nt.RotX, nt.RotY, nt.RotZ))
        Dim e = FO4_Base_Library.Transform_Class.Matrix33ToEulerXYZ(m)
        Return New System.Numerics.Vector3(-e.X, -e.Y, -e.Z)
    End Function

    ''' <summary>The transform for the selected node, creating a fresh (Raw-less) one if absent.</summary>
    Private Function EnsureSseNodeTransform() As RaceMenuJslot.JslotNodeTransform
        If String.IsNullOrEmpty(_sseNodeSelected) Then Return Nothing
        Dim p = Preset
        If p Is Nothing Then Return Nothing
        If p.SseNodeTransforms Is Nothing Then p.SseNodeTransforms = New List(Of RaceMenuJslot.JslotNodeTransform)()
        Dim nt = FindSseNodeTransform(_sseNodeSelected)
        If nt Is Nothing Then
            nt = New RaceMenuJslot.JslotNodeTransform With {.NodeName = _sseNodeSelected}
            p.SseNodeTransforms.Add(nt)
        End If
        Return nt
    End Function

    Private Sub OnSseNodeScaleChanged()
        If _suspendEvents Then Return
        ' Escala 0 (o negativa) hace singular la matriz del hueso: la geometría colapsa y la normal
        ' queda indefinida. El slider ya arranca en MinNodeScale, pero AllowExtremeValues deja tipear
        ' un valor fuera de rango, así que el piso se aplica acá también. Re-asignar Value re-entra en
        ' este handler con el valor ya capeado (AreClose corta la recursión).
        If _sseNodeScaleBar.Value < MinNodeScale Then
            _sseNodeScaleBar.Value = MinNodeScale
            Return
        End If
        Dim nt = EnsureSseNodeTransform() : If nt Is Nothing Then Return
        nt.Scale = CSng(_sseNodeScaleBar.Value) : nt.HasScale = True
        ApplySseNodeEdit()
    End Sub

    Private Sub OnSseNodePosChanged()
        If _suspendEvents Then Return
        Dim nt = EnsureSseNodeTransform() : If nt Is Nothing Then Return
        nt.PosX = CSng(_sseNodePosX.Value) : nt.PosY = CSng(_sseNodePosY.Value) : nt.PosZ = CSng(_sseNodePosZ.Value)
        nt.HasPosition = True
        ApplySseNodeEdit()
    End Sub

    Private Sub OnSseNodeRotChanged()
        If _suspendEvents Then Return
        Dim nt = EnsureSseNodeTransform() : If nt Is Nothing Then Return
        ' STANDARD euler degrees (X/Y/Z about the X/Y/Z axes) → matrix → axis-angle radians (the model/render
        ' canonical form). The NEGATED args are the app's established TRS convention — identical to the
        ' byte-verified FaceBonePoseBuilder: Matrix33ToBSRotation(EulerXYZToMatrix33(-x, -y, -z)). Without the
        ' negation the sliders would be mislabelled (raw EulerXYZToMatrix33 params are yaw=Z, pitch=Y, roll=X, so
        ' "Rotation X" would actually rotate about Z). RotationDirty tells Save to rebuild the key-32 matrix from
        ' this axis-angle (untouched rotations stay byte-exact from Raw).
        Dim m = FO4_Base_Library.Transform_Class.EulerXYZToMatrix33(-_sseNodeRotX.Value, -_sseNodeRotY.Value, -_sseNodeRotZ.Value)
        Dim aa = FO4_Base_Library.Transform_Class.Matrix33ToBSRotation(m)
        nt.RotX = aa.X : nt.RotY = aa.Y : nt.RotZ = aa.Z
        nt.HasRotation = True : nt.RotationDirty = True
        ApplySseNodeEdit()
    End Sub

    Private Sub OnSseNodeResetClick(sender As Object, e As EventArgs)
        If String.IsNullOrEmpty(_sseNodeSelected) Then Return
        Dim p = Preset
        If p Is Nothing OrElse p.SseNodeTransforms Is Nothing Then Return
        p.SseNodeTransforms.RemoveAll(Function(x) x IsNot Nothing AndAlso String.Equals(x.NodeName, _sseNodeSelected, StringComparison.OrdinalIgnoreCase))
        LoadSseNodeDetail()
        RefreshSseNodeMarker()
        _mainForm.RebuildAndApplyMergedPose(_editorHost)
        _editorHost.PreviewCtl.InvalidateRender()
    End Sub

    ''' <summary>Common tail of an edit: refresh the ● marker on the selected row, rebuild + re-apply the merged
    ''' pose (a node transform is a bone pose, not a vertex morph) and re-render.</summary>
    Private Sub ApplySseNodeEdit()
        RefreshSseNodeMarker()
        _mainForm.RebuildAndApplyMergedPose(_editorHost)
        _editorHost.PreviewCtl.InvalidateRender()
    End Sub

    ''' <summary>Update just the selected row's ● marker (a transform may have become identity or non-identity).</summary>
    Private Sub RefreshSseNodeMarker()
        If _sseNodeList Is Nothing Then Return
        Dim i = _sseNodeList.SelectedIndex
        If i < 0 OrElse i >= _sseNodeItems.Count Then Return
        Dim it = _sseNodeItems(i)
        Dim newText = SseNodeRowLabel(it.Label, it.Node)
        If String.Equals(CStr(_sseNodeList.Items(i)), newText) Then Return
        Dim keep = _suspendEvents
        _suspendEvents = True
        Try
            _sseNodeList.Items(i) = newText
        Finally
            _suspendEvents = keep
        End Try
    End Sub

    ''' <summary>Reflect the preset's current node transforms onto the tab (after a .jslot load / reset).</summary>
    Private Sub RefreshSseBodyScaleBars()
        If _sseNodeList Is Nothing Then Return
        PopulateSseNodeList(_sseNodeList.SelectedIndex)
    End Sub

    ''' <summary>SSE-only "Skin Overrides" tab: RaceMenu NiOverride body-paint per biped slot (diffuse/normal
    ''' texture + tint that replace/tint the worn skin). FO4 has no analogue → code-built SSE tab, as full as the
    ''' others: a list of overrides (left) with Add/Remove, and a detail panel (right) with slot + diffuse/normal
    ''' Browse fields + tint enable/color/alpha. Editing writes Preset.SseSkinOverrides and live re-renders (the
    ''' render composes them under the tattoo overlays in ResolveSseOverlayLayers).</summary>
    Private Sub BuildSseSkinOverridesTab()
        Dim tab As New TabPage("Skin Overrides") With {.Name = "TabPageSseSkinOverrides", .Padding = New Padding(6)}
        Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 3}
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 34))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 55))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 45))
        Dim header As New Label With {.Dock = DockStyle.Fill, .AutoSize = False, .Padding = New Padding(3, 6, 3, 0),
            .Text = "RaceMenu skin overrides (NiOverride body-paint per slot). Loaded/saved with the .jslot + sidecar."}
        root.Controls.Add(header, 0, 0) : root.SetColumnSpan(header, 2)

        ' Left (top row): the list of overrides with Add/Remove. The biped-slot FLAG grid that builds the SELECTED
        ' override's slotMask lives in its OWN full-width row underneath (spanning both columns) so it uses the whole
        ' tab width. RaceMenu keys a skin override by a slotMask bitmask — any combination of biped slots — so the
        ' user checks the exact slots there.
        Dim leftPanel As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2}
        leftPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        leftPanel.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        _sseSkinList = New ListBox With {.Dock = DockStyle.Fill, .IntegralHeight = False, .DrawMode = DrawMode.OwnerDrawFixed}
        AddHandler _sseSkinList.SelectedIndexChanged, AddressOf OnSseSkinSelChanged
        AddHandler _sseSkinList.DrawItem, AddressOf DrawSseSkinOverrideItem
        leftPanel.Controls.Add(_sseSkinList, 0, 0)
        Dim btnRow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.LeftToRight, .WrapContents = False, .AutoSize = True, .Margin = New Padding(0, 3, 0, 3)}
        Dim btnAdd As New Button With {.Text = "Add", .AutoSize = True}
        Dim btnRemove As New Button With {.Text = "Remove", .AutoSize = True}
        AddHandler btnAdd.Click, AddressOf OnSseSkinAdd
        AddHandler btnRemove.Click, AddressOf OnSseSkinRemove
        btnRow.Controls.Add(btnAdd) : btnRow.Controls.Add(btnRemove)
        leftPanel.Controls.Add(btnRow, 0, 1)
        root.Controls.Add(leftPanel, 0, 1)

        ' Full-width row: the biped-slot flag grid (same control the ARMA/ARMO editors use). Spans both columns so
        ' the category boxes flow across the whole tab width instead of being squeezed into the left column.
        Dim slotGroup As New GroupBox With {.Dock = DockStyle.Fill, .Text = "Biped slots — this override's slotMask (check the slots it targets)"}
        Dim slotFlow As New FlowLayoutPanel With {.Dock = DockStyle.Fill}
        _sseSkinSlotChecks = BipedSlotCheckboxes.Build(slotFlow, AddressOf OnSseSkinSlotFlagsChanged, columns:=5)
        slotFlow.WrapContents = True
        slotGroup.Controls.Add(slotFlow)
        root.Controls.Add(slotGroup, 0, 2) : root.SetColumnSpan(slotGroup, 2)

        ' Right: detail — a 4-column grid so the path fields STRETCH and are readable: [label | path (fills) | Pick |
        ' Clear]. One row per BSShaderTextureSet slot (skee replaces each key-9 slot independently, keeping the
        ' skin's own texture in the untouched slots), then Tint (key 7, RGB) and Opacity (key 8) on their own rows —
        ' the two are independent (skee unpacks the tint as an NiColor with no alpha and reads key 8 as the material
        ' alpha). A trailing filler row packs everything at the top.
        Dim detail As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = SseSkinTexSlots.Length + 3, .AutoScroll = True}
        detail.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 108))    ' label
        detail.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))     ' path (fills)
        detail.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))         ' Pick
        detail.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))         ' Clear
        ' One AutoSize row per slot + the Tint row + the Opacity row, THEN a single Percent filler as the LAST row
        ' (rows = slots + 3). Getting this count right is what keeps Opacity packed under Tint instead of floating
        ' at the bottom of the panel.
        For k = 0 To SseSkinTexSlots.Length + 1 : detail.RowStyles.Add(New RowStyle(SizeType.AutoSize)) : Next
        detail.RowStyles.Add(New RowStyle(SizeType.Percent, 100))   ' filler (last row)
        _sseSkinSlotBoxes.Clear()
        Dim rr = 0
        For Each sl In SseSkinTexSlots
            Dim slotIndex = sl.Index
            detail.Controls.Add(New Label With {.Text = sl.Label & ":", .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 8, 3, 0)}, 0, rr)
            Dim box As New TextBox With {.ReadOnly = True, .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 3, 0)}
            _sseSkinSlotBoxes(slotIndex) = box
            detail.Controls.Add(box, 1, rr)
            ' Compact buttons (… = pick, × = clear) so the path textbox keeps the width and stays readable.
            Dim brPick As New Button With {.Text = "…", .AutoSize = False, .Width = 26, .Height = 23, .Margin = New Padding(0, 3, 2, 0)}
            Dim brClear As New Button With {.Text = "×", .AutoSize = False, .Width = 26, .Height = 23, .Margin = New Padding(0, 3, 3, 0)}
            _sseSkinToolTip.SetToolTip(brPick, "Pick texture…") : _sseSkinToolTip.SetToolTip(brClear, "Clear")
            AddHandler brPick.Click, Sub(s, e) PickSseSkinSlotTexture(slotIndex)
            AddHandler brClear.Click, Sub(s, e) SetSseSkinSlotTexture(slotIndex, "")
            detail.Controls.Add(brPick, 2, rr)
            detail.Controls.Add(brClear, 3, rr)
            rr += 1
        Next
        _sseSkinTintEnable = New CheckBox With {.Text = "Tint", .AutoSize = True, .Margin = New Padding(3, 10, 3, 0)}
        AddHandler _sseSkinTintEnable.CheckedChanged, AddressOf OnSseSkinTintToggled
        detail.Controls.Add(_sseSkinTintEnable, 0, rr)
        _sseSkinTintColor = New Button With {.Text = "Color…", .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 6, 3, 0)}
        AddHandler _sseSkinTintColor.Click, AddressOf OnSseSkinTintColor
        detail.Controls.Add(_sseSkinTintColor, 1, rr) : detail.SetColumnSpan(_sseSkinTintColor, 3) : rr += 1
        detail.Controls.Add(New Label With {.Text = "Opacity:", .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 10, 3, 0)}, 0, rr)
        _sseSkinTintAlpha = New FO4_Base_Library.TinySliderTextBox With {
            .Minimum = 0.0R, .Maximum = 1.0R, .DisplayFormat = "0.00", .SmallChange = 0.01R, .LargeChange = 0.1R,
            .Height = 26, .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 8, 3), .Value = 1.0R}
        AddHandler _sseSkinTintAlpha.ValueChanged, AddressOf OnSseSkinTintAlpha
        AddHandler _sseSkinTintAlpha.DragEnded, AddressOf OnSliderDragEnded
        detail.Controls.Add(_sseSkinTintAlpha, 1, rr) : detail.SetColumnSpan(_sseSkinTintAlpha, 3) : rr += 1
        root.Controls.Add(detail, 1, 1)

        tab.Controls.Add(root)
        TabsBody.TabPages.Add(tab)
        RefreshSseSkinList(-1)
    End Sub

    ''' <summary>Owner-draw a skin-override row red when its diffuse (slot 0) texture is missing from the load
    ''' order — the renderer skips a missing texture, so it should read as missing here too.</summary>
    Private Sub DrawSseSkinOverrideItem(sender As Object, e As DrawItemEventArgs)
        e.DrawBackground()
        Dim p = Preset
        If e.Index >= 0 AndAlso e.Index < _sseSkinList.Items.Count Then
            Dim missing As Boolean = False
            If p IsNot Nothing AndAlso p.SseSkinOverrides IsNot Nothing AndAlso e.Index < p.SseSkinOverrides.Count Then
                Dim sk = p.SseSkinOverrides(e.Index)
                missing = sk IsNot Nothing AndAlso Not String.IsNullOrEmpty(sk.DiffusePath) AndAlso Not SseCatalogs.TextureResolves(sk.DiffusePath)
            End If
            Dim fore = If(missing, SseCatalogs.MissingTextureColor,
                          If((e.State And DrawItemState.Selected) <> 0, SystemColors.HighlightText, e.ForeColor))
            TextRenderer.DrawText(e.Graphics, _sseSkinList.Items(e.Index).ToString(), e.Font, e.Bounds, fore,
                                  TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
        End If
        e.DrawFocusRectangle()
    End Sub

    ''' <summary>The skin override selected in the list, or Nothing.</summary>
    Private Function SelectedSseSkinOverride() As RaceMenuJslot.JslotSkinOverride
        Dim p = Preset
        If p Is Nothing OrElse p.SseSkinOverrides Is Nothing Then Return Nothing
        Dim idx = _sseSkinList.SelectedIndex
        If idx < 0 OrElse idx >= p.SseSkinOverrides.Count Then Return Nothing
        Return p.SseSkinOverrides(idx)
    End Function

    Private Function SseSkinLabel(sk As RaceMenuJslot.JslotSkinOverride) As String
        Dim slotLbl = SseSkinSlotLabel(sk.SlotMask)
        Dim diff = If(String.IsNullOrEmpty(sk.DiffusePath), "(no texture)", IO.Path.GetFileName(sk.DiffusePath))
        Return $"{slotLbl} — {diff}{If(sk.HasTint, "  ●", "")}"
    End Function

    Private Sub RefreshSseSkinList(selectIndex As Integer)
        _suspendEvents = True
        Try
            _sseSkinList.BeginUpdate()
            Try
                _sseSkinList.Items.Clear()
                Dim p = Preset
                If p IsNot Nothing AndAlso p.SseSkinOverrides IsNot Nothing Then
                    For Each sk In p.SseSkinOverrides
                        If sk Is Nothing Then Continue For
                        _sseSkinList.Items.Add(SseSkinLabel(sk))
                    Next
                End If
            Finally
                _sseSkinList.EndUpdate()
            End Try
            Dim n = _sseSkinList.Items.Count
            If n > 0 Then _sseSkinList.SelectedIndex = Math.Max(0, Math.Min(selectIndex, n - 1))
        Finally
            _suspendEvents = False
        End Try
        UpdateSseSkinDetail()
    End Sub

    Private Sub UpdateSseSkinDetail()
        Dim sk = SelectedSseSkinOverride()
        _suspendEvents = True
        Try
            Dim has = sk IsNot Nothing
            _sseSkinTintEnable.Enabled = has
            _sseSkinTintColor.Enabled = has AndAlso sk.HasTint
            ' Alpha (key 8) is independent of the tint colour — enabled whenever an override is selected.
            _sseSkinTintAlpha.Enabled = has
            If _sseSkinSlotChecks IsNot Nothing Then
                For Each cb In _sseSkinSlotChecks.Values : cb.Enabled = has : Next
            End If
            For Each kvp In _sseSkinSlotBoxes
                If kvp.Value IsNot Nothing Then kvp.Value.Enabled = has
            Next
            If has Then
                If _sseSkinSlotChecks IsNot Nothing Then BipedSlotCheckboxes.SetMask(_sseSkinSlotChecks, sk.SlotMask)
                For Each kvp In _sseSkinSlotBoxes
                    Dim path As String = ""
                    If sk.Slots IsNot Nothing Then sk.Slots.TryGetValue(kvp.Key, path)
                    If kvp.Value IsNot Nothing Then kvp.Value.Text = If(path, "")
                Next
                _sseSkinTintEnable.Checked = sk.HasTint
                _sseSkinTintColor.BackColor = If(sk.HasTint, Color.FromArgb(ClampByte(sk.TintR), ClampByte(sk.TintG), ClampByte(sk.TintB)), Color.White)
                _sseSkinTintAlpha.Value = CDbl(Math.Max(0.0F, Math.Min(1.0F, If(sk.HasAlpha, sk.Alpha, 1.0F))))
            Else
                If _sseSkinSlotChecks IsNot Nothing Then BipedSlotCheckboxes.SetMask(_sseSkinSlotChecks, 0UI)
                For Each kvp In _sseSkinSlotBoxes
                    If kvp.Value IsNot Nothing Then kvp.Value.Text = ""
                Next
                _sseSkinTintEnable.Checked = False
                _sseSkinTintColor.BackColor = Color.White : _sseSkinTintAlpha.Value = 1.0R
            End If
        Finally
            _suspendEvents = False
        End Try
    End Sub

    Private Sub OnSseSkinSelChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        UpdateSseSkinDetail()
    End Sub

    Private Async Sub OnSseSkinAdd(sender As Object, e As EventArgs)
        Dim p = Preset
        If p Is Nothing Then Return
        If p.SseSkinOverrides Is Nothing Then p.SseSkinOverrides = New List(Of RaceMenuJslot.JslotSkinOverride)()
        ' Default the new override's slotMask to a REAL skin shape's mask (armorMask & addonMask), so it matches
        ' skee's exact find() and actually applies out of the box. The user can then tick/untick slots to refine it.
        Dim defaultMask As UInteger = 0UI
        Dim masks = NpcMorphPoseResolver.BodySkinSlotMasks(_editorHost?.LastRenderData)
        If masks IsNot Nothing AndAlso masks.Count > 0 Then defaultMask = masks(0)
        p.SseSkinOverrides.Insert(0, New RaceMenuJslot.JslotSkinOverride With {
            .SlotMask = defaultMask, .DiffusePath = "", .NormalPath = "", .TintR = 1, .TintG = 1, .TintB = 1, .TintA = 1, .HasTint = False})
        RefreshSseSkinList(0)
        Await TriggerOverlayReload()
    End Sub

    Private Async Sub OnSseSkinRemove(sender As Object, e As EventArgs)
        Dim p = Preset
        Dim idx = _sseSkinList.SelectedIndex
        If p Is Nothing OrElse p.SseSkinOverrides Is Nothing OrElse idx < 0 OrElse idx >= p.SseSkinOverrides.Count Then Return
        p.SseSkinOverrides.RemoveAt(idx)
        RefreshSseSkinList(If(p.SseSkinOverrides.Count = 0, -1, Math.Min(idx, p.SseSkinOverrides.Count - 1)))
        Await TriggerOverlayReload()
    End Sub

    ''' <summary>Human label for a body-skin slot mask: the biped slot names it covers (bit b = slot 30+b), e.g.
    ''' "Body, Hands, Feet (32/33/37)" for an all-in-one mesh, "Body (32)" for a body-only mesh.</summary>
    Private Shared Function SseSkinSlotLabel(mask As UInteger) As String
        Dim names As New List(Of String), nums As New List(Of String)
        For b = 0 To 31
            If (mask And (1UI << b)) <> 0UI Then
                names.Add(BipedSlotCheckboxes.SlotName(30 + b))
                nums.Add((30 + b).ToString())
            End If
        Next
        If names.Count = 0 Then Return $"mask 0x{mask:X}"
        Return $"{String.Join(", ", names)} ({String.Join("/", nums)})"
    End Function

    ''' <summary>The biped-slot checkboxes changed → rebuild the selected override's slotMask from the checked
    ''' slots (RaceMenu stores the override under this exact bitmask). Re-labels the list row and re-renders.</summary>
    Private Sub OnSseSkinSlotFlagsChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim sk = SelectedSseSkinOverride()
        If sk Is Nothing OrElse _sseSkinSlotChecks Is Nothing Then Return
        sk.SlotMask = BipedSlotCheckboxes.ReadMask(_sseSkinSlotChecks)
        Dim i = _sseSkinList.SelectedIndex
        If i >= 0 Then _sseSkinList.Items(i) = SseSkinLabel(sk)
        TriggerSseOverlayLive()
    End Sub

    ''' <summary>Set a texture-set slot on the selected skin override. An empty path removes that slot (skee then
    ''' keeps the skin's own texture there). Slots 0/1 mirror into <c>DiffusePath</c>/<c>NormalPath</c> so the jslot
    ''' save (which emits those for slots 0/1) stays in sync.</summary>
    Private Sub SetSseSkinSlotTexture(slotIndex As Integer, path As String)
        Dim sk = SelectedSseSkinOverride()
        If sk Is Nothing Then Return
        If sk.Slots Is Nothing Then sk.Slots = New Dictionary(Of Integer, String)()
        If String.IsNullOrEmpty(path) Then sk.Slots.Remove(slotIndex) Else sk.Slots(slotIndex) = path
        If slotIndex = 0 Then sk.DiffusePath = path
        If slotIndex = 1 Then sk.NormalPath = path
        _suspendEvents = True
        Try
            Dim box As TextBox = Nothing
            If _sseSkinSlotBoxes.TryGetValue(slotIndex, box) AndAlso box IsNot Nothing Then box.Text = path
        Finally
            _suspendEvents = False
        End Try
        Dim i = _sseSkinList.SelectedIndex
        If i >= 0 Then _sseSkinList.Items(i) = SseSkinLabel(sk)
        TriggerSseOverlayLive()
    End Sub

    ''' <summary>Pick a slot texture from the merged loose+BSA dictionary. Skin overrides are preset/mod-driven —
    ''' skee64 has no catalog of "available" ones (verified: PapyrusNiOverride only queries an actor's already-applied
    ''' overrides), so unlike overlays there is no named list to offer; the picker over the texture tree is the
    ''' faithful way to author one applicable to any NPC. Not a file dialog (which can't see inside a BSA).</summary>
    Private Sub PickSseSkinSlotTexture(slotIndex As Integer)
        Dim sk = SelectedSseSkinOverride()
        If sk Is Nothing Then Return
        Dim cur As String = ""
        If sk.Slots IsNot Nothing Then sk.Slots.TryGetValue(slotIndex, cur)
        Dim picked = SseCatalogs.PickSkinTexture(Me, cur)
        If picked Is Nothing Then Return
        SetSseSkinSlotTexture(slotIndex, picked)
    End Sub

    Private Sub OnSseSkinTintToggled(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim sk = SelectedSseSkinOverride()
        If sk Is Nothing Then Return
        sk.HasTint = _sseSkinTintEnable.Checked
        _sseSkinTintColor.Enabled = sk.HasTint   ' alpha (key 8) is independent — stays enabled
        Dim i = _sseSkinList.SelectedIndex
        If i >= 0 Then _sseSkinList.Items(i) = SseSkinLabel(sk)
        TriggerSseOverlayLive()
    End Sub

    Private Sub OnSseSkinTintColor(sender As Object, e As EventArgs)
        Dim sk = SelectedSseSkinOverride()
        If sk Is Nothing Then Return
        Using dlg As New ColorDialog() With {.Color = Color.FromArgb(ClampByte(sk.TintR), ClampByte(sk.TintG), ClampByte(sk.TintB))}
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                sk.TintR = dlg.Color.R / 255.0F : sk.TintG = dlg.Color.G / 255.0F : sk.TintB = dlg.Color.B / 255.0F
                sk.HasTint = True
                _sseSkinTintColor.BackColor = dlg.Color
                TriggerSseOverlayLive()
            End If
        End Using
    End Sub

    Private Sub OnSseSkinTintAlpha(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim sk = SelectedSseSkinOverride()
        If sk Is Nothing Then Return
        ' key 8 (kParam_ShaderAlpha) — the override's material alpha, independent of the tint colour.
        sk.Alpha = CSng(_sseSkinTintAlpha.Value) : sk.HasAlpha = True
        TriggerSseOverlayLive()
    End Sub

    ''' <summary>Adapts the FO4 overlay tab for RaceMenu, keeping its two-list shape: the available list
    ''' (<see cref="ListBoxOverlayAvailable"/>) holds the RaceMenu paint catalog for the selected zone, the applied
    ''' list (<see cref="ListBoxOverlayApplied"/>) holds this NPC's overlays, and Add/Remove/Up/Down move between
    ''' them. The FO4 overlay handlers branch on <c>_isSSE</c> to the path-based carrier
    ''' (<c>Preset.SseBodyOverlays</c>). Two SSE differences from f4ee: the UV offset/scale sliders are hidden
    ''' (RaceMenu overlays have no UV override), and the applied overlay's texture is a read-only display (it is set
    ''' from the catalog on Add, not browsed). Run AFTER InitOverlaysTab.</summary>
    Private Sub BuildSseOverlaysSection()
        ' RaceMenu DOES present an authored paint catalog for overlays: mods register body/hand/feet paints via
        ' AddBodyPaint/AddHandPaint/AddFeetPaint (RaceMenuPaintCatalog reconstructs those name;;path lists). So the
        ' SSE overlay editor uses the SAME two-list paradigm as the FO4 overlay editor: LEFT = the paint catalog for
        ' the selected zone (choose from), RIGHT = the applied overlays. "Add →" creates an overlay node from the
        ' selected paint; "← Remove" deletes it. Keep the FO4 three-column layout (available | buttons | applied).
        OverlayListsLayout.SuspendLayout()
        GroupBoxOverlayAvailable.Visible = True
        GroupBoxOverlayAvailable.Text = "Paints (RaceMenu)"
        GroupBoxOverlayApplied.Text = "Applied overlays"
        OverlayListsLayout.SetCellPosition(GroupBoxOverlayAvailable, New TableLayoutPanelCellPosition(0, 0))
        OverlayListsLayout.SetCellPosition(OverlayCenterLayout, New TableLayoutPanelCellPosition(1, 0))
        OverlayListsLayout.SetCellPosition(GroupBoxOverlayApplied, New TableLayoutPanelCellPosition(2, 0))
        OverlayListsLayout.ColumnStyles(0).SizeType = SizeType.Percent : OverlayListsLayout.ColumnStyles(0).Width = 50.0F
        OverlayListsLayout.ColumnStyles(1).SizeType = SizeType.AutoSize
        OverlayListsLayout.ColumnStyles(2).SizeType = SizeType.Percent : OverlayListsLayout.ColumnStyles(2).Width = 50.0F
        OverlayListsLayout.ResumeLayout(True)
        TextBoxOverlayFilter.PlaceholderText = "Filter paints…"
        ' Mark rows whose texture isn't in the load order in red (same convention as the tint tab). A mod can
        ' register a paint whose .dds it doesn't ship — RaceMenu (and this app) then render nothing; showing it
        ' missing is clearer than a silent no-op. Owner-draw is set here (SSE only); FO4 keeps the default draw.
        ListBoxOverlayAvailable.DrawMode = DrawMode.OwnerDrawFixed
        AddHandler ListBoxOverlayAvailable.DrawItem, AddressOf DrawSsePaintCatalogItem
        ListBoxOverlayApplied.DrawMode = DrawMode.OwnerDrawFixed
        AddHandler ListBoxOverlayApplied.DrawItem, AddressOf DrawSseAppliedOverlayItem
        ' Hide the UV offset/scale rows — RaceMenu overlays have no UV-offset/scale override (those are LooksMenu-
        ' only). Up/Down STAY: they reorder the overlay stack, which in RaceMenu is the Ovl{n} node index (skee64
        ' attaches Ovl0..N in order, higher index on top) — SseMoveOverlay swaps that index.
        For Each c As Control In New Control() {LabelOverlayOffsetU, SliderOverlayOffsetU, LabelOverlayOffsetV, SliderOverlayOffsetV,
                                                LabelOverlayScaleU, SliderOverlayScaleU, LabelOverlayScaleV, SliderOverlayScaleV}
            If c IsNot Nothing Then c.Visible = False
        Next
        ButtonOverlayAdd.Text = "Add →" : ButtonOverlayRemove.Text = "← Remove"
        ' The FO4 "tint alpha" slider becomes the overlay OPACITY (skee64 kParam_ShaderAlpha, key 8) — a
        ' separate override from the tint colour, and the only one the engine actually reads for opacity.
        ' It must therefore stay enabled even when no tint is set.
        LabelOverlayTintAlpha.Text = "Opacity:"

        ' Zone selector: skee64 instantiates overlay nodes for Body/Hands/Feet independently
        ' (OverlayInterface.h:33-46). Sits DIRECTLY ABOVE the Add button (center column). It drives BOTH which
        ' paint category the LEFT catalog shows AND which zone "Add →" creates the overlay on.
        _sseOverlayZone = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 120, .Margin = New Padding(3, 3, 3, 6)}
        _sseOverlayZone.Items.AddRange({"Body", "Hands", "Feet"})
        _sseOverlayZone.SelectedIndex = 0
        AddHandler _sseOverlayZone.SelectedIndexChanged, Sub(s, e) RefreshSsePaintCatalog()
        Dim zoneRow As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.LeftToRight, .WrapContents = False, .Margin = New Padding(0)}
        zoneRow.Controls.Add(New Label With {.Text = "Zone:", .AutoSize = True, .Margin = New Padding(0, 7, 3, 0)})
        zoneRow.Controls.Add(_sseOverlayZone)
        OverlayCenterLayout.SuspendLayout()
        OverlayCenterLayout.RowCount = 3
        OverlayCenterLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        OverlayCenterLayout.SetRow(ButtonOverlayAdd, 1)
        OverlayCenterLayout.SetRow(ButtonOverlayRemove, 2)
        OverlayCenterLayout.Controls.Add(zoneRow, 0, 0)
        ' Top-align the whole center column (default is Anchor.None = vertically centred) so the Zone row sits at the
        ' same height as the filter box; AlignSseOverlayCenterColumn supplies the exact offset once layout is real.
        OverlayCenterLayout.Anchor = AnchorStyles.Top
        ' Add/Remove: drop AutoSize and stretch them to the column width (which the zone row — label + combo —
        ' defines), so both buttons are the same width and symmetric with "Zone: [combo]" above.
        For Each b As Button In New Button() {ButtonOverlayAdd, ButtonOverlayRemove}
            b.AutoSize = False
            b.Anchor = AnchorStyles.Left Or AnchorStyles.Right
            b.Height = 25
        Next
        OverlayCenterLayout.ResumeLayout(True)
        AddHandler OverlayListsLayout.Layout, AddressOf OnOverlayListsLayout

        ' Applied-overlay texture rows: READ-ONLY display. The paint is chosen from the LEFT catalog at Add time
        ' (RaceMenu overlays have no per-overlay texture browser); Normal shows an Ex paint's slot 1 when present.
        Dim r0 = OverlayPropsLayout.RowCount
        OverlayPropsLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        OverlayPropsLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        OverlayPropsLayout.RowCount = r0 + 2
        OverlayPropsLayout.Controls.Add(New Label With {.Text = "Texture:", .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 6, 3, 0)}, 0, r0)
        _sseTexDiffuse = New TextBox With {.Width = 300, .ReadOnly = True}
        OverlayPropsLayout.Controls.Add(SseTexFlow(_sseTexDiffuse), 1, r0)
        OverlayPropsLayout.Controls.Add(New Label With {.Text = "Normal:", .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 6, 3, 0)}, 0, r0 + 1)
        _sseTexNormal = New TextBox With {.Width = 300, .ReadOnly = True}
        OverlayPropsLayout.Controls.Add(SseTexFlow(_sseTexNormal), 1, r0 + 1)
        RefreshSseOverlayList()
        RefreshSsePaintCatalog()
    End Sub

    ''' <summary>Keep the center column's top edge level with the filter box. The offset can't be a constant (the
    ''' GroupBox caption height is DPI/font dependent), so it is measured from the live control and applied as the
    ''' panel's top margin. Guarded against re-entry: only writes when the value actually changes.</summary>
    Private Sub OnOverlayListsLayout(sender As Object, e As LayoutEventArgs)
        If Not TextBoxOverlayFilter.IsHandleCreated OrElse Not OverlayListsLayout.IsHandleCreated Then Return
        Dim top = OverlayListsLayout.PointToClient(TextBoxOverlayFilter.PointToScreen(System.Drawing.Point.Empty)).Y
        If top < 0 Then Return
        If OverlayCenterLayout.Margin.Top = top Then Return
        OverlayCenterLayout.Margin = New Padding(OverlayCenterLayout.Margin.Left, top, OverlayCenterLayout.Margin.Right, OverlayCenterLayout.Margin.Bottom)
    End Sub

    ''' <summary>Fill the LEFT catalog (ListBoxOverlayAvailable) with the RaceMenu paint list for the selected
    ''' zone, honouring the filter box. Parallel <see cref="_ssePaintShown"/> maps a shown row back to its paint
    ''' entry (the ListBox index can't be used directly once filtered). This is the union of every mod's
    ''' Add{Body,Hand,Feet}Paint registration — exactly the list RaceMenu shows.</summary>
    Private Sub RefreshSsePaintCatalog()
        If Not _isSSE OrElse ListBoxOverlayAvailable Is Nothing OrElse _sseOverlayZone Is Nothing Then Return
        _ssePaintShown.Clear()
        ListBoxOverlayAvailable.BeginUpdate()
        Try
            ListBoxOverlayAvailable.Items.Clear()
            Dim cat = FO4_Base_Library.RaceMenuPaintCatalog.Current
            If cat Is Nothing Then Return
            Dim zone As SseCatalogs.OverlayZone = CType(Math.Max(0, _sseOverlayZone.SelectedIndex), SseCatalogs.OverlayZone)
            Dim pcat = SseCatalogs.PaintCategoryForZone(zone)
            Dim filter = If(TextBoxOverlayFilter Is Nothing, "", TextBoxOverlayFilter.Text.Trim())
            For Each en In cat.Entries(pcat)
                If filter.Length = 0 OrElse en.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 Then
                    ListBoxOverlayAvailable.Items.Add(en.DisplayName)
                    _ssePaintShown.Add(en)
                End If
            Next
        Finally
            ListBoxOverlayAvailable.EndUpdate()
        End Try
    End Sub

    ''' <summary>Owner-draw a paint-catalog row: red when its texture is not in the load order (the renderer would
    ''' skip it), otherwise the normal fore/highlight colour.</summary>
    Private Sub DrawSsePaintCatalogItem(sender As Object, e As DrawItemEventArgs)
        e.DrawBackground()
        If e.Index >= 0 AndAlso e.Index < ListBoxOverlayAvailable.Items.Count Then
            Dim missing As Boolean = e.Index < _ssePaintShown.Count AndAlso Not SseCatalogs.TextureResolves(_ssePaintShown(e.Index).Path)
            Dim fore = If(missing, SseCatalogs.MissingTextureColor,
                          If((e.State And DrawItemState.Selected) <> 0, SystemColors.HighlightText, e.ForeColor))
            TextRenderer.DrawText(e.Graphics, ListBoxOverlayAvailable.Items(e.Index).ToString(), e.Font, e.Bounds, fore,
                                  TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
        End If
        e.DrawFocusRectangle()
    End Sub

    ''' <summary>Owner-draw an applied-overlay row: red when its diffuse texture is missing from the load order.</summary>
    Private Sub DrawSseAppliedOverlayItem(sender As Object, e As DrawItemEventArgs)
        e.DrawBackground()
        If e.Index >= 0 AndAlso e.Index < ListBoxOverlayApplied.Items.Count Then
            Dim missing As Boolean = e.Index < _sseShownOverlays.Count AndAlso Not SseCatalogs.TextureResolves(_sseShownOverlays(e.Index).DiffusePath)
            Dim fore = If(missing, SseCatalogs.MissingTextureColor,
                          If((e.State And DrawItemState.Selected) <> 0, SystemColors.HighlightText, e.ForeColor))
            TextRenderer.DrawText(e.Graphics, ListBoxOverlayApplied.Items(e.Index).ToString(), e.Font, e.Bounds, fore,
                                  TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
        End If
        e.DrawFocusRectangle()
    End Sub

    ''' <summary>One "textbox + buttons" row that stretches to its cell: the textbox fills the remaining width and
    ''' each button auto-sizes on the right, so nothing clips regardless of panel width.</summary>
    Private Function SseTexFlow(tb As TextBox, ParamArray buttons As Button()) As Control
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

    ''' <summary>The Body/Hands/Feet overlays of <c>Preset.SseBodyOverlays</c>, in list order. The carrier holds
    ''' the whole <c>.jslot</c> <c>overrides</c> array, which also contains the <c>Face [Ovl]</c> nodes edited on
    ''' Edit Face's "RaceMenu · Face Paint" tab — those must not appear here. Parallel to the ListBox rows, so a
    ''' row index maps back to the right node even though the carrier is not filtered.</summary>
    Private ReadOnly _sseShownOverlays As New List(Of RaceMenuJslot.JslotOverlayNode)

    ''' <summary>The SSE overlay selected in the reused ListBoxOverlayApplied, or Nothing.</summary>
    Private Function SelectedSseOverlay() As RaceMenuJslot.JslotOverlayNode
        Dim idx = ListBoxOverlayApplied.SelectedIndex
        If idx < 0 OrElse idx >= _sseShownOverlays.Count Then Return Nothing
        Return _sseShownOverlays(idx)
    End Function

    ''' <summary>Repopulate the reused ListBoxOverlayApplied from the SSE carrier, keeping selection.</summary>
    Private Sub RefreshSseOverlayList()
        RefreshSseAppliedList(ListBoxOverlayApplied.SelectedIndex)
    End Sub

    Private Sub RefreshSseAppliedList(selectIndex As Integer)
        _suspendEvents = True
        Try
            ListBoxOverlayApplied.BeginUpdate()
            Try
                ListBoxOverlayApplied.Items.Clear()
                _sseShownOverlays.Clear()
                Dim p = Preset
                If p IsNot Nothing AndAlso p.SseBodyOverlays IsNot Nothing Then
                    ' Show in DRAW ORDER so Up/Down are intuitive: group by zone (Body, Hands, Feet), and within a
                    ' zone list the HIGHEST Ovl index first — skee64 draws higher indices on top, so top-of-list =
                    ' on top. Face overlays are edited on the Face Paint tab and excluded here.
                    Dim shown = p.SseBodyOverlays.
                        Where(Function(o) o IsNot Nothing AndAlso SseCatalogs.ZoneOfNode(o.NodeName).HasValue AndAlso
                                          SseCatalogs.ZoneOfNode(o.NodeName).Value <> SseCatalogs.OverlayZone.Face).
                        OrderBy(Function(o) CInt(SseCatalogs.ZoneOfNode(o.NodeName).Value)).
                        ThenByDescending(Function(o) SseCatalogs.IndexOfNode(o.NodeName)).ToList()
                    For Each ov In shown
                        _sseShownOverlays.Add(ov)
                        ListBoxOverlayApplied.Items.Add(SseOverlayLabel(ov))
                    Next
                End If
            Finally
                ListBoxOverlayApplied.EndUpdate()
            End Try
            Dim n = ListBoxOverlayApplied.Items.Count
            If n > 0 Then ListBoxOverlayApplied.SelectedIndex = Math.Max(0, Math.Min(selectIndex, n - 1))
        Finally
            _suspendEvents = False
        End Try
        UpdateSseOverlayDetail()
    End Sub

    ''' <summary>List label for an applied overlay: the node identity (<c>Body [Ovl2]</c>) plus the RaceMenu paint
    ''' NAME re-derived from the catalog by the stored texture path — the path is the only link the <c>.jslot</c>
    ''' keeps, so a texture registered by an installed mod shows its friendly name ("CO 15 Body Blackout Tri")
    ''' instead of the anonymous file name. Falls back to the file name when no mod registered that texture.</summary>
    Private Shared Function SseOverlayLabel(ov As RaceMenuJslot.JslotOverlayNode) As String
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

    ''' <summary>Load the selected SSE overlay into the reused FO4 tint controls + the SSE texture fields.</summary>
    Private Sub UpdateSseOverlayDetail()
        Dim ov = SelectedSseOverlay()
        _suspendEvents = True
        Try
            Dim has = ov IsNot Nothing
            LabelOverlaySelected.Text = If(has, $"Overlay: {ov.NodeName}", "(no overlay selected — Add one)")
            ' Point "Add to:" at the zone of whatever is selected, so adding a second Hands overlay doesn't
            ' silently create a Body one.
            If has AndAlso _sseOverlayZone IsNot Nothing Then
                Dim z = SseCatalogs.ZoneOfNode(ov.NodeName)
                If z.HasValue AndAlso CInt(z.Value) < _sseOverlayZone.Items.Count Then _sseOverlayZone.SelectedIndex = CInt(z.Value)
            End If
            If _sseTexDiffuse IsNot Nothing Then
                _sseTexDiffuse.Text = If(has, If(ov.DiffusePath, ""), "") : _sseTexDiffuse.Enabled = has
            End If
            If _sseTexNormal IsNot Nothing Then
                _sseTexNormal.Text = If(has, If(ov.NormalPath, ""), "") : _sseTexNormal.Enabled = has
            End If
            CheckBoxOverlayTint.Checked = has AndAlso ov.HasTint
            CheckBoxOverlayTint.Enabled = has
            ButtonOverlayTintColor.Enabled = has AndAlso ov.HasTint
            If has AndAlso ov.HasTint Then
                ButtonOverlayTintColor.BackColor = Color.FromArgb(ClampByte(ov.TintR), ClampByte(ov.TintG), ClampByte(ov.TintB))
            Else
                ButtonOverlayTintColor.BackColor = Color.White
            End If
            ' Opacity (key 8) is independent of the tint colour (key 7) — enabled whenever an overlay is
            ' selected, not only when it is tinted.
            SliderOverlayTintAlpha.Enabled = has
            SliderOverlayTintAlpha.Value = If(has, CDbl(Math.Max(0.0F, Math.Min(1.0F, If(ov.HasAlpha, ov.Alpha, 1.0F)))), 1.0R)
        Finally
            _suspendEvents = False
        End Try
    End Sub

    ''' <summary>Write the SSE overlay tint COLOUR from the reused FO4 swatch. skee64 unpacks the tint into an
    ''' NiColor (RGB only), so the colour carries no opacity; opacity is the separate key-8 alpha override
    ''' written by <see cref="OnOverlayTintAlphaChanged"/>.</summary>
    Private Sub WriteSseOverlayTint(ov As RaceMenuJslot.JslotOverlayNode)
        Dim c = ButtonOverlayTintColor.BackColor
        ov.TintR = c.R / 255.0F : ov.TintG = c.G / 255.0F : ov.TintB = c.B / 255.0F
    End Sub

    ''' <summary>Add an overlay to the next free <c>[Ovl n]</c> slot of the chosen zone. The slot count comes
    ''' from <c>skee64.ini</c>: the engine only ever instantiates <c>iNumOverlays</c> nodes per zone, so an
    ''' overlay authored past that bound would render here and do nothing in-game.</summary>
    Private Async Function SseAddOverlay() As Task
        Dim p = Preset
        If p Is Nothing Then Return
        If p.SseBodyOverlays Is Nothing Then p.SseBodyOverlays = New List(Of RaceMenuJslot.JslotOverlayNode)()

        ' Which paint the user picked from the LEFT catalog. Like FO4, Add moves the SELECTED catalog entry into
        ' the applied list — the overlay's texture IS the chosen paint.
        Dim ai = ListBoxOverlayAvailable.SelectedIndex
        If ai < 0 OrElse ai >= _ssePaintShown.Count Then
            MessageBox.Show(Me, "Choose a paint from the list on the left, then press Add →.",
                            "No paint selected", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim entry = _ssePaintShown(ai)

        Dim zone As SseCatalogs.OverlayZone = CType(Math.Max(0, _sseOverlayZone.SelectedIndex), SseCatalogs.OverlayZone)
        Dim limit = SseCatalogs.OverlayCount(zone)
        Dim used As New HashSet(Of Integer)
        For Each o In p.SseBodyOverlays
            If o Is Nothing Then Continue For
            Dim z = SseCatalogs.ZoneOfNode(o.NodeName)
            If z.HasValue AndAlso z.Value = zone Then
                Dim n0 = SseCatalogs.IndexOfNode(o.NodeName)
                If n0 >= 0 Then used.Add(n0)
            End If
        Next

        Dim n = 0
        While used.Contains(n) : n += 1 : End While
        If n >= limit Then
            MessageBox.Show(Me,
                $"RaceMenu only creates {limit} {zone} overlay slot(s) ([Ovl0]…[Ovl{limit - 1}]), per iNumOverlays in skee64.ini." & vbCrLf & vbCrLf &
                "Remove one, or raise iNumOverlays in skee64.ini and reopen the editor.",
                "No free overlay slot", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        ' An Ex registration carries the full texture set; slot 1 is the normal. Plain paints have diffuse only.
        Dim nrm As String = ""
        If entry.Slots IsNot Nothing AndAlso entry.Slots.Length > 1 Then
            Dim s1 = entry.Slots(1)
            If Not String.IsNullOrEmpty(s1) AndAlso Not s1.Equals("ignore", StringComparison.OrdinalIgnoreCase) Then nrm = s1
        End If
        p.SseBodyOverlays.Insert(0, New RaceMenuJslot.JslotOverlayNode With {
            .NodeName = SseCatalogs.OverlayNodeName(zone, n), .DiffusePath = entry.Path, .NormalPath = nrm,
            .TintR = 1, .TintG = 1, .TintB = 1, .TintA = 1, .HasTint = False,
            .Alpha = 1.0F, .HasAlpha = True})
        p.HasOverlays = True
        RefreshSseAppliedList(0)
        Await TriggerOverlayReload()
    End Function

    ''' <summary>Remove the selected overlay. Resolved through the SHOWN list, because the carrier also holds the
    ''' face overlays this tab hides — a raw ListBox-index removal would delete the wrong node.</summary>
    Private Async Function SseRemoveOverlay() As Task
        Dim p = Preset
        Dim ov = SelectedSseOverlay()
        If p Is Nothing OrElse p.SseBodyOverlays Is Nothing OrElse ov Is Nothing Then Return
        Dim row = ListBoxOverlayApplied.SelectedIndex
        p.SseBodyOverlays.Remove(ov)
        p.HasOverlays = True
        RefreshSseAppliedList(If(_sseShownOverlays.Count = 0, -1, Math.Max(0, row - 1)))
        Await TriggerOverlayReload()
    End Function

    ''' <summary>Move the selected overlay one row up/down AMONG THE SHOWN overlays: swap it with its shown
    ''' neighbour inside the carrier, so the hidden face overlays keep their positions.</summary>
    ''' <summary>Reorder the overlay stack by SWAPPING the two overlays' <c>Ovl{n}</c> node indices — that index IS
    ''' RaceMenu's draw order (skee64 attaches Ovl0..N in order, higher index on top). Only reorders within the
    ''' same zone (Body/Hands/Feet stacks are independent). Moving list-Up = toward the top of the stack (higher
    ''' index); the shown list is sorted highest-index-first so the row math matches.</summary>
    Private Async Function SseMoveOverlay(delta As Integer) As Task
        Dim p = Preset
        Dim ov = SelectedSseOverlay()
        If p Is Nothing OrElse ov Is Nothing Then Return
        Dim row = ListBoxOverlayApplied.SelectedIndex
        Dim targetRow = row + delta
        If targetRow < 0 OrElse targetRow >= _sseShownOverlays.Count Then Return
        Dim neighbour = _sseShownOverlays(targetRow)
        Dim zA = SseCatalogs.ZoneOfNode(ov.NodeName)
        Dim zB = SseCatalogs.ZoneOfNode(neighbour.NodeName)
        If Not zA.HasValue OrElse Not zB.HasValue OrElse zA.Value <> zB.Value Then Return   ' different zone → no cross-stack move
        Dim ni = SseCatalogs.IndexOfNode(ov.NodeName)
        Dim nj = SseCatalogs.IndexOfNode(neighbour.NodeName)
        If ni < 0 OrElse nj < 0 Then Return
        ov.NodeName = SseCatalogs.OverlayNodeName(zA.Value, nj)
        neighbour.NodeName = SseCatalogs.OverlayNodeName(zB.Value, ni)
        RefreshSseAppliedList(targetRow)
        Await TriggerOverlayReload()
    End Function

    ''' <summary>Re-resolve the SSE overlay layers + re-render (live).</summary>
    Private Async Sub TriggerSseOverlayLive()
        Try
            Await TriggerOverlayReload()
        Catch
        End Try
    End Sub


    ' =====================================================================
    ' Overlays tab (body tattoos) — add/remove/reorder applied overlays + edit LooksMenu
    ' per-overlay properties (offset/scale/tint) live against the embedded preview.
    '
    ' Order/priority convention (load-bearing — must match NpcMorphPoseResolver.ResolveOverlayLayers
    ' which sorts ascending by Priority so the HIGHEST Priority is drawn LAST = on top):
    '   ListBoxOverlayApplied index 0 = TOP of list = drawn ON TOP.
    '   Preset.Overlays is stored in the SAME order as the list (index 0 = top).
    '   After every add/remove/reorder we renumber: Priority(i) = (n-1) - i, so index 0 gets the
    '   highest Priority value. The resolver's ascending sort then puts index 0 last = on top.
    '
    ' Refresh path: overlay changes need the render plan to re-run ResolveOverlayLayers, which only
    ' happens on a FULL preview reload — so overlays use TriggerSkinChangeReload (NOT the lightweight
    ' OnLocalBodyRefresh / _refresh which is morph/pose-only and does NOT re-resolve overlays).
    ' Add/remove/reorder = immediate reload; slider drags = throttled (ScheduleRefresh → RefreshTimer,
    ' DragEnded flushes). The throttled path is routed to the full reload via _pendingOverlayReload.
    ' =====================================================================

    ''' <summary>Populate the Available + Applied lists and set the empty-state. Called once at
    ''' construction. When no templates exist for this NPC's gender the (now-empty) lists +
    ''' property controls stay inert and the empty-state message is shown in LabelOverlaySelected
    ''' (surfaced by UpdateOverlayPropsForSelection).</summary>
    Private Sub InitOverlaysTab()
        _overlayCandidates.Clear()
        If _mainForm IsNot Nothing Then
            Dim cands = _mainForm.GetOverlayTemplateCandidates(_npcIsFemale)
            If cands IsNot Nothing Then _overlayCandidates.AddRange(cands)
        End If
        _hasOverlayTemplates = _overlayCandidates.Count > 0

        RefreshAvailableList()
        RefreshAppliedList(-1)
        ' No selection yet → property controls disabled, label shows the placeholder.
        UpdateOverlayPropsForSelection()
    End Sub

    ''' <summary>Strip a single leading "$" used by LooksMenu for localization keys, falling back
    ''' to the template id when DisplayName is empty.</summary>
    Private Shared Function OverlayDisplay(tpl As OverlayTemplate) As String
        Dim s = If(tpl.DisplayName, "")
        If s.StartsWith("$") Then s = s.Substring(1)
        If s.Length = 0 Then s = tpl.Id
        Return s
    End Function

    ''' <summary>Rebuild ListBoxOverlayAvailable as a filtered projection of _overlayCandidates.
    ''' Items hold display strings; _availableShown is the parallel index→template map (the
    ''' ListBox index can't be used directly because the filter removes rows). Mirrors the
    ''' _bodySlideRows parallel-mapping idiom + OnBodySlideFilterChanged case-insensitive Contains.</summary>
    Private Sub RefreshAvailableList()
        Dim filter = TextBoxOverlayFilter.Text.Trim()
        ListBoxOverlayAvailable.BeginUpdate()
        Try
            ListBoxOverlayAvailable.Items.Clear()
            _availableShown.Clear()
            For Each tpl In _overlayCandidates
                Dim disp = OverlayDisplay(tpl)
                If filter.Length = 0 OrElse disp.Contains(filter, StringComparison.OrdinalIgnoreCase) Then
                    ListBoxOverlayAvailable.Items.Add(disp)
                    _availableShown.Add(tpl)
                End If
            Next
        Finally
            ListBoxOverlayAvailable.EndUpdate()
        End Try
    End Sub

    ''' <summary>Rebuild ListBoxOverlayApplied from Preset.Overlays in stored order (index 0 = top
    ''' = on top). Preset.Overlays is kept pre-sorted in display order by RenumberOverlayPriorities,
    ''' so this is a straight projection. Each row shows the resolved template display name (or the
    ''' raw id when the template isn't installed, so the user still sees what's there).
    ''' selectIndex chooses which row to re-select afterwards (-1 = none).</summary>
    Private Sub RefreshAppliedList(selectIndex As Integer)
        If _isSSE Then RefreshSseAppliedList(selectIndex) : Return
        Dim p = Preset
        _suspendEvents = True
        Try
            ListBoxOverlayApplied.BeginUpdate()
            Try
                ListBoxOverlayApplied.Items.Clear()
                If p IsNot Nothing Then
                    For Each ov In p.Overlays
                        ListBoxOverlayApplied.Items.Add(AppliedOverlayLabel(ov))
                    Next
                End If
            Finally
                ListBoxOverlayApplied.EndUpdate()
            End Try
            Dim n = ListBoxOverlayApplied.Items.Count
            If n > 0 Then
                ListBoxOverlayApplied.SelectedIndex = Math.Max(0, Math.Min(selectIndex, n - 1))
            End If
        Finally
            _suspendEvents = False
        End Try
        ' Selection (or lack of it) drives the property pane; refresh it now that suspend is lifted.
        UpdateOverlayPropsForSelection()
    End Sub

    ''' <summary>Label for an applied overlay row: resolved template display name when installed,
    ''' else the raw template id (so a missing/foreign template is still visible, not blank).</summary>
    Private Function AppliedOverlayLabel(ov As LooksmenuLoader.OverlayEntry) As String
        Dim tpl = ResolveAppliedTemplate(ov)
        If tpl IsNot Nothing Then Return OverlayDisplay(tpl)
        ' Missing overlay (2026-07-09, same rule as missing tints): the template mod isn't installed
        ' / not for this gender. Kept verbatim in p.Overlays (round-trips on Save), the render can't
        ' resolve it so it's never applied, and the props pane stays disabled. Shown with a MISSING
        ' marker so the user can still identify and remove it.
        Dim id = If(String.IsNullOrEmpty(ov.TemplateId), "(unknown)", ov.TemplateId)
        Logger.LogLazy(Function() $"[LMLoad] Overlay '{id}' shown as MISSING (template not installed for this gender) — preserved verbatim, not applied.")
        Return $"[MISSING] {id}"
    End Function

    ''' <summary>Resolve the OverlayTemplate behind an applied entry via MainForm (gender-aware).
    ''' Returns Nothing when the template id isn't installed for this gender.</summary>
    Private Function ResolveAppliedTemplate(ov As LooksmenuLoader.OverlayEntry) As OverlayTemplate
        If ov Is Nothing OrElse String.IsNullOrEmpty(ov.TemplateId) OrElse _mainForm Is Nothing Then Return Nothing
        Return _mainForm.ResolveOverlayTemplate_Friend(ov.TemplateId, _npcIsFemale)
    End Function

    ''' <summary>Renumber Preset.Overlays so list index 0 = highest Priority = drawn on top.
    ''' Priority(i) = (n-1) - i. Keeps the stored list in display order (index 0 = top).</summary>
    Private Sub RenumberOverlayPriorities()
        Dim p = Preset
        If p Is Nothing Then Return
        Dim n = p.Overlays.Count
        For i = 0 To n - 1
            p.Overlays(i).Priority = (n - 1) - i
        Next
    End Sub

    Private Sub OnOverlayFilterChanged(sender As Object, e As EventArgs)
        If _isSSE Then RefreshSsePaintCatalog() Else RefreshAvailableList()
    End Sub

    ''' <summary>Add the selected available template to the TOP of the applied list (index 0 = on
    ''' top), unless its TemplateId is already applied (no duplicates). Renumber + full reload.</summary>
    Private Async Sub OnOverlayAdd(sender As Object, e As EventArgs)
        If _isSSE Then Await SseAddOverlay() : Return
        Dim p = Preset
        If p Is Nothing Then Return
        Dim idx = ListBoxOverlayAvailable.SelectedIndex
        If idx < 0 OrElse idx >= _availableShown.Count Then Return
        Dim tpl = _availableShown(idx)
        ' Prevent duplicate TemplateId (the engine multimap would allow it, but a single editor
        ' row per template keeps the applied list coherent).
        If p.Overlays.Any(Function(o) String.Equals(o.TemplateId, tpl.Id, StringComparison.OrdinalIgnoreCase)) Then Return
        p.Overlays.Insert(0, New LooksmenuLoader.OverlayEntry With {.TemplateId = tpl.Id})
        p.HasOverlays = True
        RenumberOverlayPriorities()
        RefreshAppliedList(0)
        Await TriggerOverlayReload()
    End Sub

    ''' <summary>Remove the selected applied overlay. Renumber, keep a sensible neighbour selected,
    ''' full reload (kept full so the texture set stays consistent: a later Reset can restore a
    ''' removed overlay whose textures may no longer be loaded — see TriggerOverlayLiveRefresh).</summary>
    Private Async Sub OnOverlayRemove(sender As Object, e As EventArgs)
        If _isSSE Then Await SseRemoveOverlay() : Return
        Dim p = Preset
        If p Is Nothing Then Return
        Dim idx = ListBoxOverlayApplied.SelectedIndex
        If idx < 0 OrElse idx >= p.Overlays.Count Then Return
        p.Overlays.RemoveAt(idx)
        ' Opening Edit Body declares ownership of Overlays; an emptied list must still be
        ' authoritative (wipe), matching HasBodyMorphValues semantics.
        p.HasOverlays = True
        RenumberOverlayPriorities()
        Dim newSel = If(p.Overlays.Count = 0, -1, Math.Min(idx, p.Overlays.Count - 1))
        RefreshAppliedList(newSel)
        Await TriggerOverlayReload()
    End Sub

    Private Async Sub OnOverlayUp(sender As Object, e As EventArgs)
        Await MoveSelectedOverlay(-1)
    End Sub

    Private Async Sub OnOverlayDown(sender As Object, e As EventArgs)
        Await MoveSelectedOverlay(1)
    End Sub

    ''' <summary>Move the selected applied overlay one slot (delta -1 = up/toward top,
    ''' +1 = down). Renumber, keep it selected, lightweight refresh: reorder is a pure permutation
    ''' of the SAME overlay set — same templates, same materials, same textures (already loaded),
    ''' only the draw order changes — so no full reload / texture pass is needed.</summary>
    Private Async Function MoveSelectedOverlay(delta As Integer) As Task
        If _isSSE Then Await SseMoveOverlay(delta) : Return
        Dim p = Preset
        If p Is Nothing Then Return
        Dim idx = ListBoxOverlayApplied.SelectedIndex
        Dim target = idx + delta
        If idx < 0 OrElse target < 0 OrElse target >= p.Overlays.Count Then Return
        Dim moved = p.Overlays(idx)
        p.Overlays.RemoveAt(idx)
        p.Overlays.Insert(target, moved)
        RenumberOverlayPriorities()
        RefreshAppliedList(target)
        Await TriggerOverlayLiveRefresh()
    End Function

    ''' <summary>Returns the currently-selected applied OverlayEntry, or Nothing.</summary>
    Private Function SelectedOverlay() As LooksmenuLoader.OverlayEntry
        Dim p = Preset
        If p Is Nothing Then Return Nothing
        Dim idx = ListBoxOverlayApplied.SelectedIndex
        If idx < 0 OrElse idx >= p.Overlays.Count Then Return Nothing
        Return p.Overlays(idx)
    End Function

    Private Sub OnOverlayAppliedSelectionChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        UpdateOverlayPropsForSelection()
    End Sub

    ''' <summary>Load the selected overlay's Offset/Scale/Tint into the property controls (under
    ''' _suspendEvents) and enable/disable them per the template's Transformable/Tintable flags.
    ''' Updates LabelOverlaySelected so the user sees why some controls are disabled. With no
    ''' selection everything is disabled and the label shows the placeholder.</summary>
    Private Sub UpdateOverlayPropsForSelection()
        If _isSSE Then UpdateSseOverlayDetail() : Return
        ' No templates for this gender → surface the empty-state in the selected-overlay label and
        ' leave the prop controls disabled (the lists are empty, so there is nothing to select).
        If Not _hasOverlayTemplates Then
            LabelOverlaySelected.Text = "No LooksMenu overlay templates installed for this gender."
            SetOverlayPropControlsEnabled(transformable:=False, tintable:=False)
            Return
        End If

        Dim ov = SelectedOverlay()
        _suspendEvents = True
        Try
            If ov Is Nothing Then
                LabelOverlaySelected.Text = "(no overlay selected)"
                SetOverlayPropControlsEnabled(transformable:=False, tintable:=False)
                SliderOverlayOffsetU.Value = 0R
                SliderOverlayOffsetV.Value = 0R
                SliderOverlayScaleU.Value = 1.0R
                SliderOverlayScaleV.Value = 1.0R
                CheckBoxOverlayTint.Checked = False
                SliderOverlayTintAlpha.Value = 1.0R
                ButtonOverlayTintColor.BackColor = Color.White
                Return
            End If

            Dim tpl = ResolveAppliedTemplate(ov)
            Dim transformable = tpl IsNot Nothing AndAlso tpl.Transformable
            Dim tintable = tpl IsNot Nothing AndAlso tpl.Tintable
            LabelOverlaySelected.Text = BuildOverlaySelectedLabel(ov, tpl, transformable, tintable)

            ' Offset (default 0,0 when array is Nothing).
            Dim off = ov.OffsetUV
            SliderOverlayOffsetU.Value = CDbl(If(off IsNot Nothing AndAlso off.Length > 0, off(0), 0.0F))
            SliderOverlayOffsetV.Value = CDbl(If(off IsNot Nothing AndAlso off.Length > 1, off(1), 0.0F))
            ' Scale (default 1,1 when array is Nothing).
            Dim sc = ov.ScaleUV
            SliderOverlayScaleU.Value = CDbl(If(sc IsNot Nothing AndAlso sc.Length > 0, sc(0), 1.0F))
            SliderOverlayScaleV.Value = CDbl(If(sc IsNot Nothing AndAlso sc.Length > 1, sc(1), 1.0F))
            ' Tint (Nothing = unchecked, white swatch, full alpha).
            Dim tint = ov.Tint
            If tint IsNot Nothing AndAlso tint.Length >= 3 Then
                CheckBoxOverlayTint.Checked = True
                ButtonOverlayTintColor.BackColor = Color.FromArgb(
                    ClampByte(tint(0)), ClampByte(tint(1)), ClampByte(tint(2)))
                SliderOverlayTintAlpha.Value = CDbl(If(tint.Length >= 4, tint(3), 1.0F))
            Else
                CheckBoxOverlayTint.Checked = False
                ButtonOverlayTintColor.BackColor = Color.White
                SliderOverlayTintAlpha.Value = 1.0R
            End If

            SetOverlayPropControlsEnabled(transformable, tintable)
        Finally
            _suspendEvents = False
        End Try
    End Sub

    ''' <summary>Compose the read-only status label: template id + transformable/tintable flags,
    ''' annotating when the template isn't installed for this gender (controls forced off).</summary>
    Private Shared Function BuildOverlaySelectedLabel(ov As LooksmenuLoader.OverlayEntry,
                                                      tpl As OverlayTemplate,
                                                      transformable As Boolean,
                                                      tintable As Boolean) As String
        Dim id = If(String.IsNullOrEmpty(ov.TemplateId), "(unknown)", ov.TemplateId)
        If tpl Is Nothing Then
            Return $"[MISSING] {id} — template not installed for this gender; preserved verbatim, not applied (properties disabled)"
        End If
        Return $"{id} — transformable: {If(transformable, "yes", "no")}, tintable: {If(tintable, "yes", "no")}"
    End Function

    ''' <summary>Gate the offset/scale controls by Transformable and the tint controls by Tintable.</summary>
    Private Sub SetOverlayPropControlsEnabled(transformable As Boolean, tintable As Boolean)
        SliderOverlayOffsetU.Enabled = transformable
        SliderOverlayOffsetV.Enabled = transformable
        SliderOverlayScaleU.Enabled = transformable
        SliderOverlayScaleV.Enabled = transformable
        LabelOverlayOffsetU.Enabled = transformable
        LabelOverlayOffsetV.Enabled = transformable
        LabelOverlayScaleU.Enabled = transformable
        LabelOverlayScaleV.Enabled = transformable
        CheckBoxOverlayTint.Enabled = tintable
        ' Color swatch + alpha follow Tintable AND the checkbox (no tint applied → editing the
        ' colour is meaningless). When tint is off we leave them disabled.
        Dim tintActive = tintable AndAlso CheckBoxOverlayTint.Checked
        ButtonOverlayTintColor.Enabled = tintActive
        LabelOverlayTintAlpha.Enabled = tintActive
        SliderOverlayTintAlpha.Enabled = tintActive
    End Sub

    Private Shared Function ClampByte(f As Single) As Integer
        Return Math.Max(0, Math.Min(255, CInt(Math.Round(f * 255.0F))))
    End Function

    ''' <summary>Offset slider edit → write OffsetUV on the selected entry (Nothing when at the
    ''' 0,0 default to keep the JSON clean), throttled reload.</summary>
    Private Sub OnOverlayOffsetChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim ov = SelectedOverlay()
        If ov Is Nothing Then Return
        Dim u = CSng(SliderOverlayOffsetU.Value)
        Dim v = CSng(SliderOverlayOffsetV.Value)
        If IsDefaultPair(u, v, 0.0F) Then
            ov.OffsetUV = Nothing
        Else
            ov.OffsetUV = New Single() {u, v}
        End If
        ScheduleOverlayReload()
    End Sub

    ''' <summary>Scale slider edit → write ScaleUV (Nothing when at the 1,1 default), throttled.</summary>
    Private Sub OnOverlayScaleChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim ov = SelectedOverlay()
        If ov Is Nothing Then Return
        Dim u = CSng(SliderOverlayScaleU.Value)
        Dim v = CSng(SliderOverlayScaleV.Value)
        If IsDefaultPair(u, v, 1.0F) Then
            ov.ScaleUV = Nothing
        Else
            ov.ScaleUV = New Single() {u, v}
        End If
        ScheduleOverlayReload()
    End Sub

    Private Shared Function IsDefaultPair(a As Single, b As Single, def As Single) As Boolean
        Return Math.Abs(a - def) < 0.0005F AndAlso Math.Abs(b - def) < 0.0005F
    End Function

    ''' <summary>Tint checkbox toggled → materialize or clear the entry's Tint array, re-gate the
    ''' colour/alpha controls, immediate reload (structural change, not a drag).</summary>
    Private Async Sub OnOverlayTintToggled(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        If _isSSE Then
            Dim sov = SelectedSseOverlay()
            If sov IsNot Nothing Then
                sov.HasTint = CheckBoxOverlayTint.Checked
                If sov.HasTint Then WriteSseOverlayTint(sov)
                _suspendEvents = True
                Try
                    ' Opacity (key 8) is not gated by the tint colour (key 7); leave its slider enabled.
                    ButtonOverlayTintColor.Enabled = sov.HasTint
                Finally
                    _suspendEvents = False
                End Try
                Dim si = ListBoxOverlayApplied.SelectedIndex
                If si >= 0 Then ListBoxOverlayApplied.Items(si) = SseOverlayLabel(sov)
                Await TriggerOverlayReload()
            End If
            Return
        End If
        Dim ov = SelectedOverlay()
        If ov Is Nothing Then Return
        If CheckBoxOverlayTint.Checked Then
            WriteOverlayTintFromControls(ov)
        Else
            ov.Tint = Nothing
        End If
        ' Re-gate swatch + alpha (they follow the checkbox). Run under suspend so the slider's
        ' own ValueChanged doesn't re-fire while we toggle Enabled.
        Dim tpl = ResolveAppliedTemplate(ov)
        _suspendEvents = True
        Try
            SetOverlayPropControlsEnabled(tpl IsNot Nothing AndAlso tpl.Transformable,
                                          tpl IsNot Nothing AndAlso tpl.Tintable)
        Finally
            _suspendEvents = False
        End Try
        Await TriggerOverlayLiveRefresh()
    End Sub

    ''' <summary>Colour swatch clicked → ColorDialog for RGB; paint the swatch + write Tint
    ''' (preserving the current alpha), immediate reload.</summary>
    Private Async Sub OnOverlayTintColorClicked(sender As Object, e As EventArgs)
        If _isSSE Then
            Dim sov = SelectedSseOverlay()
            If sov Is Nothing Then Return
            Using dlg As New ColorDialog() With {.FullOpen = True, .Color = ButtonOverlayTintColor.BackColor}
                If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
                ButtonOverlayTintColor.BackColor = dlg.Color
            End Using
            sov.HasTint = True
            _suspendEvents = True
            Try
                CheckBoxOverlayTint.Checked = True : ButtonOverlayTintColor.Enabled = True : SliderOverlayTintAlpha.Enabled = True
            Finally
                _suspendEvents = False
            End Try
            WriteSseOverlayTint(sov)
            Dim si = ListBoxOverlayApplied.SelectedIndex
            If si >= 0 Then ListBoxOverlayApplied.Items(si) = SseOverlayLabel(sov)
            Await TriggerOverlayReload()
            Return
        End If
        Dim ov = SelectedOverlay()
        If ov Is Nothing Then Return
        Using dlg As New ColorDialog() With {.FullOpen = True, .Color = ButtonOverlayTintColor.BackColor}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            ButtonOverlayTintColor.BackColor = dlg.Color
        End Using
        ' Picking a colour implies the tint is on.
        If Not CheckBoxOverlayTint.Checked Then
            _suspendEvents = True
            Try
                CheckBoxOverlayTint.Checked = True
            Finally
                _suspendEvents = False
            End Try
            SetOverlayPropControlsEnabled(True, True)
        End If
        WriteOverlayTintFromControls(ov)
        Await TriggerOverlayLiveRefresh()
    End Sub

    ''' <summary>Alpha slider edit → FO4: rewrite Tint preserving RGB. SSE: write the overlay's OPACITY, which
    ''' is skee64's own key-8 alpha override and is independent of whether a tint colour is set.</summary>
    Private Sub OnOverlayTintAlphaChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        If _isSSE Then
            Dim sov = SelectedSseOverlay()
            If sov IsNot Nothing Then
                sov.Alpha = CSng(SliderOverlayTintAlpha.Value)
                sov.HasAlpha = True
                ScheduleOverlayReload()
            End If
            Return
        End If
        Dim ov = SelectedOverlay()
        If ov Is Nothing Then Return
        If Not CheckBoxOverlayTint.Checked Then Return
        WriteOverlayTintFromControls(ov)
        ScheduleOverlayReload()
    End Sub

    ''' <summary>Compose entry.Tint = {r/255, g/255, b/255, alpha} from the swatch + alpha slider.</summary>
    Private Sub WriteOverlayTintFromControls(ov As LooksmenuLoader.OverlayEntry)
        Dim c = ButtonOverlayTintColor.BackColor
        Dim a = CSng(SliderOverlayTintAlpha.Value)
        ov.Tint = New Single() {c.R / 255.0F, c.G / 255.0F, c.B / 255.0F, a}
    End Sub

    ''' <summary>Revert Preset.Overlays to the form-open snapshot (deep-cloned from _priorPreset,
    ''' or empty when the NPC had no prior overlay), repopulate the applied list, full reload.
    ''' Mirrors ResetBodySection's shape but for the Overlays channel.</summary>
    Private Async Function ResetOverlaysSection() As Task
        Dim p = Preset
        If p Is Nothing Then Return
        ' SSE: revert the path-based RaceMenu overlay carrier to the form-open snapshot, refresh the list,
        ' full reload. The FO4 template-overlay logic below is skipped (its controls are hidden under SSE).
        If _isSSE Then
            p.SseBodyOverlays = If(_hadPriorOverlay AndAlso _priorPreset IsNot Nothing,
                                   LooksmenuLoader.CloneSseBodyOverlays(_priorPreset.SseBodyOverlays), Nothing)
            RefreshSseOverlayList()
            Await TriggerOverlayReload()
            Return
        End If
        p.Overlays.Clear()
        If _hadPriorOverlay AndAlso _priorPreset IsNot Nothing Then
            ' Deep-clone each entry from the snapshot so later edits don't mutate the snapshot.
            For Each ov In _priorPreset.Overlays
                p.Overlays.Add(New LooksmenuLoader.OverlayEntry With {
                    .TemplateId = ov.TemplateId,
                    .Priority = ov.Priority,
                    .Tint = If(ov.Tint Is Nothing, Nothing, CType(ov.Tint.Clone(), Single())),
                    .OffsetUV = If(ov.OffsetUV Is Nothing, Nothing, CType(ov.OffsetUV.Clone(), Single())),
                    .ScaleUV = If(ov.ScaleUV Is Nothing, Nothing, CType(ov.ScaleUV.Clone(), Single()))
                })
            Next
            p.HasOverlays = _priorPreset.HasOverlays
        Else
            p.HasOverlays = False
        End If
        ' Keep stored order = display order (snapshot is already priority-sorted from its own
        ' lifetime, but re-sort defensively so index 0 = highest Priority = top).
        SortOverlaysForDisplay()
        RefreshAppliedList(If(p.Overlays.Count > 0, 0, -1))
        Await TriggerOverlayReload()
    End Function

    ''' <summary>Order Preset.Overlays so index 0 = highest Priority (= top = on top), matching the
    ''' applied-list convention, then renumber to compact the priorities. Used after a Reset where
    ''' the snapshot's priorities may be sparse/arbitrary.</summary>
    Private Sub SortOverlaysForDisplay()
        Dim p = Preset
        If p Is Nothing Then Return
        Dim ordered = p.Overlays.OrderByDescending(Function(o) o.Priority).ToList()
        p.Overlays.Clear()
        p.Overlays.AddRange(ordered)
        RenumberOverlayPriorities()
    End Sub

    ''' <summary>Immediate full preview reload after a structural overlay change (add/remove/
    ''' reorder/tint-toggle). Overlays are re-resolved ONLY inside BuildRenderPlan
    ''' (MainForm.ResolveOverlayLayers, MainForm.vb:4340) — which runs in RenderInHostAsync, NOT in
    ''' the RefreshBodySkinLivePreview fast path. So we MUST do a full RenderInHostAsync and CANNOT
    ''' route through TriggerSkinChangeReload: that helper short-circuits on the fast path when the
    ''' skin mesh is unchanged (which it always is for an overlay-only edit), skipping
    ''' ResolveOverlayLayers and leaving the tattoos stale.</summary>
    Private Async Function TriggerOverlayReload() As Task
        If _editorHost Is Nothing OrElse _mainForm Is Nothing Then Return
        Try
            Await _mainForm.RenderInHostAsync(_editorHost, _rootNpcFormID)
        Catch ex As Exception
            Logger.LogLazy(Function() $"[EDIT-BODY] overlay reload failed for NPC 0x{_rootNpcFormID:X8}: {ex.GetType().Name}: {ex.Message}")
        End Try
    End Function

    ''' <summary>Lightweight overlay refresh for property-only edits (offset/scale/tint): re-resolve the
    ''' layer materials on the existing render data + repaint, no full reload. Falls back to the full
    ''' reload when the host has no render data yet.</summary>
    Private Async Function TriggerOverlayLiveRefresh() As Task
        If _editorHost Is Nothing OrElse _mainForm Is Nothing Then Return
        If Not _mainForm.RefreshOverlayLayersLive(_editorHost) Then Await TriggerOverlayReload()
    End Function

    ''' <summary>Throttled overlay reload for slider drags: marks an overlay-specific pending flag
    ''' and starts the shared RefreshTimer. FlushRefresh routes the pending flag to the full reload
    ''' (see RefreshTimer_Tick / FlushRefresh). DragEnded → OnSliderDragEnded → FlushRefresh forces
    ''' the final value to render immediately.</summary>
    Private Sub ScheduleOverlayReload()
        _pendingOverlayReload = True
        If Not RefreshTimer.Enabled Then RefreshTimer.Start()
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' Live edits already applied to the overlay; flag MainForm so it reloads its preview.
        HasUncommittedChanges = True
        DialogResult = DialogResult.OK
        Close()
    End Sub

    ''' <summary>Cancel = sólo marcar el resultado y cerrar. El rollback vive en
    ''' <see cref="EditBodyForm_FormClosing"/> para que la X haga exactamente lo mismo que este botón —
    ''' mismo diseño que ArmoEditor_Form/ArmaEditor_Form. ⛔ No puede ir aquí también: este handler
    ''' llama a Close(), así que invocarlo desde FormClosing re-entraría en el cierre.</summary>
    Private Sub OnCancel(sender As Object, e As EventArgs)
        DialogResult = DialogResult.Cancel
        Close()
    End Sub

    ''' <summary>Deshace las ediciones en vivo restaurando el snapshot del constructor. Las ediciones
    ''' mutan _appliedPresets[npc] POR REFERENCIA, así que sin esto un cierre no-OK dejaba el overlay ya
    ''' modificado mientras el caller (MainForm.vb:9920, exige DialogResult=OK) se saltaba MarkNpcDirty y
    ''' el re-render. Idempotente y sólo toca campos ReadOnly del ctor ⇒ seguro respecto al teardown GL.</summary>
    Private Sub RevertOverlay()
        If _hadPriorOverlay Then
            _appliedPresets(_rootNpcFormID) = _priorPreset
        Else
            _appliedPresets.Remove(_rootNpcFormID)
        End If
        HasUncommittedChanges = False
    End Sub

    ''' <summary>Refresh dispatcher targeting the editor's embedded NpcRenderHost. MWGT and
    ''' MRSV affect the bone-scale pose (BuildBodyWeightPose), so we need a Pose dirty pass;
    ''' BodySlide sliders affect the vertex morph plan, so we also rebuild the MorphResolver
    ''' and mark Morphs dirty. Both flags can be set on the same intent.</summary>
    Private Sub OnLocalBodyRefresh()
        If _editorHost Is Nothing OrElse _mainForm Is Nothing Then Return
        If _editorHost.LastRenderedState Is Nothing OrElse _editorHost.LastRenderData Is Nothing Then Return
        Dim intent = _editorHost.PreviewCtl.Intent
        intent.MorphResolver = _mainForm.BuildCompositeMorphResolver(_editorHost.LastRenderedState, _editorHost.LastRenderData, _editorHost)
        intent.MarkDirty(RenderDirtyFlags.Morphs Or RenderDirtyFlags.Pose, _editorHost.LastRenderData.Shapes)
        ' Body-weight pose depends on overlay weights — rebuild it on the editor's host.
        _mainForm.RebuildAndApplyMergedPose(_editorHost)
        _editorHost.PreviewCtl.InvalidateRender()
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        ' Flush any in-flight throttled refresh so OK doesn't leave a deferred render hanging,
        ' then stop the timer so its tick doesn't fire on a disposed form.
        FlushRefresh()
        RefreshTimer.Stop()
        RefreshTimer.Dispose()
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
    ' =====================================================================
    Private WithEvents EditPreviewControl As PreviewControl = Nothing

    Private Async Sub EditBodyForm_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        EditPreviewControl = New PreviewControl() With {.Dock = System.Windows.Forms.DockStyle.Fill}
        PreviewHostPanel.Controls.Add(EditPreviewControl)

        ' Seed the per-editor visibility checkboxes from MainForm so the embedded preview opens
        ' with whatever the user had in the main viewer. Done BEFORE the Toggles snapshot below
        ' so the snapshot reads the editor's own checkboxes (single source of truth from now on).
        ' _seedingToggles guards against the CheckedChanged handlers firing N times during seed —
        ' each .Checked = ... assignment would otherwise trigger a full visibility pass per box.
        ' Rótulos + tooltips game-aware de las 4 visibilidades del editor (bajo Skyrim: "Render outfit" /
        ' "Render accessories", y gore deshabilitado — no hay meatcaps). Mismo helper que el MainForm.
        RenderToggleLabels.Apply(Nothing, Nothing, Nothing, Nothing, Nothing, Nothing,
                                 CheckBoxRenderUnderarmor, CheckBoxRenderArmor, CheckBoxRenderHeadwear, CheckBoxRenderGore)

        _seedingToggles = True
        Try
            CheckBoxRenderUnderarmor.Checked = _mainForm.CheckBoxRenderUnderarmor.Checked
            CheckBoxRenderArmor.Checked = _mainForm.CheckBoxRenderArmor.Checked
            CheckBoxRenderHeadwear.Checked = _mainForm.CheckBoxRenderHeadwear.Checked
            CheckBoxRenderGore.Checked = _mainForm.CheckBoxRenderGore.Checked
        Finally
            _seedingToggles = False
        End Try

        ' Toggle preset uses FullBody as the morph/sculpt baseline (everything ON), then
        ' OVERWRITES the 4 visibility flags from the editor's own checkboxes. The editor
        ' checkboxes own the truth post-seed; CheckedChanged handlers below mutate them and
        ' rebuild the same way. _mainGore is no longer special — the editor's gore checkbox
        ' replaces it as the visibility input.
        _editorHost = New NpcRenderHost(EditPreviewControl) With {
            .AppliedPresets = _appliedPresets,
            .Toggles = BuildTogglesFromEditorCheckboxes()
        }
        ' Camera GPU/CPU toggle debe re-aplicar el tint de ESTE preview (no sólo la geometría). Ver MainForm.
        _mainForm?.HookSkinningToggleRefresh(EditPreviewControl, _editorHost)
        ' Face tint deferral is now handled by the library's PostTextureUploadAction hook on
        ' RenderIntent — wired by RenderCurrentStateAsync inside the render dispatch path so
        ' editor hosts get the same generic post-texture sequencing the MainForm uses.

        If _mainForm IsNot Nothing Then
            Try
                Await _mainForm.RenderInHostAsync(_editorHost, _rootNpcFormID)
            Catch ex As Exception
                Logger.LogLazy(Function() $"[EDIT-BODY] initial render failed for NPC 0x{_rootNpcFormID:X8}: {ex.GetType().Name}: {ex.Message}")
            End Try
            ' The Body Scale rows can only be filtered against the actor's skeleton once it exists, and the
            ' skeleton is built by the first render — the tab was created in the .ctor, before the preview host.
            If _isSSE Then
                RebuildSseBodyScaleTab()
                UpdateSseSkinDetail()   ' sync the biped-slot flags to the selected override now that shapes exist
            End If
        End If
    End Sub

    ''' <summary>Rebuild the "RaceMenu · Body Scale" tab now that <c>_editorHost.LastSkeletonInstance</c> exists, so
    ''' its node list can be intersected with the bones this rig actually has.</summary>
    Private Sub RebuildSseBodyScaleTab()
        Dim existing = TabsBody.TabPages.Cast(Of TabPage)().FirstOrDefault(Function(t) t.Name = "TabPageSseBodyScale")
        Dim wasSelected = existing IsNot Nothing AndAlso TabsBody.SelectedTab Is existing
        If existing IsNot Nothing Then TabsBody.TabPages.Remove(existing)
        _sseNodeList = Nothing
        BuildSseBodyScaleTab()
        If wasSelected Then
            Dim rebuilt = TabsBody.TabPages.Cast(Of TabPage)().FirstOrDefault(Function(t) t.Name = "TabPageSseBodyScale")
            If rebuilt IsNot Nothing Then TabsBody.SelectedTab = rebuilt
        End If
    End Sub

    Private Sub EditBodyForm_FormClosing(sender As Object, e As System.Windows.Forms.FormClosingEventArgs) Handles Me.FormClosing
        ' ⭐ Rollback ANTES del teardown y para CUALQUIER cierre que no sea OK: botón Cancel, X, Esc y
        ' Alt+F4 (WinForms pone DialogResult=Cancel al cerrar un modal con la X, así que este único test
        ' cubre las cuatro vías). Mismo diseño que ArmoEditor_Form.vb:1677 y EditFace_Form.
        If DialogResult <> DialogResult.OK Then RevertOverlay()

        ' Quiesce the render loop FIRST — see EditFace_Form for the full rationale.
        ' Without this the safety-repaint heartbeat can drain a paint mid-host-Dispose
        ' against GL handles the host has already deleted.
        If EditPreviewControl IsNot Nothing AndAlso Not EditPreviewControl.IsDisposed Then
            Try
                EditPreviewControl.BeginTeardown()
            Catch
            End Try
        End If

        ' Tear down host BEFORE the preview control — same ordering rationale as EditFace_Form.
        If _editorHost IsNot Nothing Then
            Try
                _editorHost.Dispose()
            Catch
            End Try
            _editorHost = Nothing
        End If
        If EditPreviewControl IsNot Nothing AndAlso Not EditPreviewControl.IsDisposed Then
            Try
                EditPreviewControl.Clean()
            Catch
            End Try
            Try
                EditPreviewControl.Dispose()
            Catch
            End Try
        End If
        EditPreviewControl = Nothing
    End Sub

    ''' <summary>Snapshot the editor's 4 visibility checkboxes into a fresh RenderToggles. The
    ''' morph/sculpt/body-weight/body-tri baseline is taken from FullBody (everything ON — body
    ''' editing wants the full pipeline running so the user can judge MWGT/MRSV/BodySlide
    ''' against the outfit). Only the 4 visibility flags come from the editor checkboxes;
    ''' RenderBody stays True (the editor never exposes the master-gate-of-3 toggle).</summary>
    Private Function BuildTogglesFromEditorCheckboxes() As RenderToggles
        Dim t = RenderToggles.FullBody(False) ' mainGore ignored — overwritten below.
        t.RenderUnderarmor = CheckBoxRenderUnderarmor.Checked
        t.RenderArmor = CheckBoxRenderArmor.Checked
        t.RenderHeadwear = CheckBoxRenderHeadwear.Checked
        t.RenderGore = CheckBoxRenderGore.Checked
        Return t
    End Function

    ''' <summary>Single CheckedChanged handler for all 4 render-visibility checkboxes. Same
    ''' shape MainForm uses (rebuild Toggles → ApplyRenderToggleVisibility) but pointed at
    ''' the editor's host instead of _renderHost. _seedingToggles short-circuits during the
    ''' Shown seed so we don't run 4 redundant visibility passes.</summary>
    Private Sub OnRenderToggleChanged(sender As Object, e As EventArgs) _
        Handles CheckBoxRenderUnderarmor.CheckedChanged,
                CheckBoxRenderArmor.CheckedChanged,
                CheckBoxRenderHeadwear.CheckedChanged,
                CheckBoxRenderGore.CheckedChanged
        If _seedingToggles Then Return
        If _editorHost Is Nothing Then Return
        _editorHost.Toggles = BuildTogglesFromEditorCheckboxes()
        _editorHost.ApplyRenderToggleVisibility()
    End Sub
End Class
