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
    ' SSE weight (SliderSseWeight), overlay zone/texture-display/magic (FlowSseOverlayZone, ComboBoxSseOverlayZone,
    ' TextBoxSseOverlayDiffuse/Normal, CheckBoxSseOverlayMagic), node-transform (ListBoxSseNodes, TextBoxSseNodeFilter,
    ' the 7 TRS sliders, CheckBoxSseShowAllNodes) and skin-override (ListBoxSseSkinOverrides, the 4 texture-slot rows,
    ' tint controls) controls all live in the Designer now (00-reglas-ui-y-vb §1) — always instantiated, hidden under
    ' Fallout 4. The decisions those controls encode (single 0..100 weight slider mirroring BuildSseMorphTab; overlay
    ' texture fields are a read-only display set from the catalog on Add; the "Magic" checkbox MOVES the overlay
    ' between skee64's normal/spell pools by renaming its node; node transforms are TRS, not scale-only) are documented
    ' where those controls are declared in EditBody_Form.Designer.vb and at the populate methods below.
    ''' <summary>Opción de VISTA de este editor: dibujar el pool magic en su preview. El preview principal nunca
    ''' se los ve — de ahí el default ON.</summary>
    Private ReadOnly _sseOverlayToolTip As New ToolTip()
    ' Parallel index→entry map for the LEFT paint catalog (ListBoxOverlayAvailable) in SSE mode; the ListBox
    ' index can't be used directly because the filter box removes rows.
    Private ReadOnly _ssePaintShown As New List(Of FO4_Base_Library.RaceMenuPaintCatalog.Entry)
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
        ''' <summary>NPC_.NAM6 — FO4 "Height Min", Skyrim "Height". Seeded by MainForm from the effective
        ''' TRAITS source (so an inheriting NPC opens at the value the record tree shows), then from any
        ''' already-authored NpcRecordOverride so reopening the editor shows the pending edit rather than
        ''' the stale record value. 1.0 when the subrecord is absent.</summary>
        Public HeightMin As Single = 1.0F
        ''' <summary>NPC_.NAM4 "Height Max" — FO4 only; Skyrim has no NAM4 and leaves this at the default.</summary>
        Public HeightMax As Single = 1.0F
        ''' <summary>Whether the record actually CARRIES each subrecord. Without these, "absent" and "present
        ''' with value 1.0" seed the identical slider state, and the min&lt;=max cross-clamp cannot tell them
        ''' apart — it would push the sibling and mint a subrecord the user never authored.</summary>
        Public HasHeightMin As Boolean = False
        Public HasHeightMax As Boolean = False
    End Class

    ' --- Height (NPC.NAM6 / NAM4) ---
    ' Snapshot of what the sliders OPENED at, read back FROM the controls after seeding: a record value
    ' outside the CK's [0.1, 10] range gets clamped on assignment, and snapshotting the clamped value keeps
    ' that clamp from registering as a user edit and being written back over the record's real value.
    Private _snapHeightMin As Double
    Private _snapHeightMax As Double
    ' Re-entrancy guard for the min<=max cross-clamp: assigning the sibling slider re-fires ValueChanged.
    Private _heightSyncing As Boolean = False
    ' Does the record actually CARRY each subrecord? An absent NAM4 and a NAM4 of 1.0 look identical on the
    ' slider, so without this the cross-clamp would push the sibling and MINT a subrecord the user never
    ' authored — creating bytes is the user's call, not ours.
    Private _hadHeightMin As Boolean
    Private _hadHeightMax As Boolean
    ' hasMrsv arrives as a ctor parameter only, but ResetBodySection needs it too: without latching it, Reset
    ' re-claims MRSV ownership for a race that has no MRSV channel and mints an all-zero subrecord.
    Private ReadOnly _hasMrsv As Boolean
    ' Did the USER drag/type this slider (as opposed to the cross-clamp moving it)? Only a user-moved or
    ' already-present field is ever written back.
    Private _heightMinUserMoved As Boolean = False
    Private _heightMaxUserMoved As Boolean = False
    ' Did the seeded record value fall OUTSIDE [0.1, 10] and get clamped on the way into the slider? Then the
    ' number on screen is not the record's, so the cross-clamp must never write this field on the user's
    ' behalf — only a direct edit of THIS slider may replace a value the user was never shown.
    Private _heightMinSeedClamped As Boolean = False
    Private _heightMaxSeedClamped As Boolean = False
    ' Hard limits of the Creation Kit's height fields. The sliders' TRACK is narrower (20%..200%, spanning the vanilla
    ' spread) and they run with AllowExtremeValues so anything in between is representable — these are the
    ' outer bounds nothing may cross, enforced on seed, on user input and again on commit.
    Private Const HeightHardMin As Double = 0.1
    Private Const HeightHardMax As Double = 10.0
    ' Nominal slider track — must match the Designer's Minimum/Maximum on both height sliders. A seed outside
    ' this widens BOTH tracks to the hard limit (see InitHeightSection) so the thumb never sits pinned to a
    ' rail, which is what makes a stray click destructive.
    Private Const HeightTrackMin As Double = 0.2
    Private Const HeightTrackMax As Double = 2.0

    ' Spare last row of BodyTabLayout, deliberately left empty so BuildSseWeightSection has a deterministic
    ' place to park the unused FO4 weight group when it takes over cell (0,0). ⛔ Named rather than derived
    ' from RowCount: "the last row happens to be free" is exactly the implicit invariant that broke when the
    ' Height row was inserted. Adding a row means updating this AND the Designer's RowCount/RowStyles.
    Private Const BodyTabSpareRow As Integer = 4

    ' Decimals kept when committing. The sliders report an unquantised drag position, so without this the
    ' record would get 1.0240876 while the box reads 102.45%. FOUR, not two: the box shows two decimals of a
    ' PERCENT, which is four decimals of the stored multiplier — stored and displayed must agree.
    Private Const HeightDecimals As Integer = 4

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
        _hasMrsv = hasMrsv
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
        ' ⛔ seedMrsv also gates on hasMrsv. SeedOverlayFromInitial sets p.HasBodyMorphValues=True and fills 5
        ' floats unconditionally, and that flag is authoritative at save (EmitMrsv writes whenever the list is
        ' non-empty) — so seeding it for a race with NO MRSV channel MINTS an all-zero MRSV subrecord on a
        ' record that never had one. Latent until now because the Edit Body button was unreachable for those
        ' NPCs; making Height always available (BodyEditAvailability.HasHeight) exposes it.
        SeedOverlayFromInitial(p, initial, seedMrsv:=(Not _isSSE) AndAlso hasMrsv)

        ' Height rides the NpcRecordOverride, NOT the LooksMenu overlay: NAM6/NAM4 are plain record
        ' subrecords the preset never carries, and the override is applied at Save AFTER the round-trip
        ' copy so the edit wins. Seeded here, registered on OK only (Cancel writes nothing).
        InitHeightSection(initial)

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
            PopulateSseBodyScaleTab()   ' RaceMenu NiOverride node transform sliders (SSE-only tab; FO4 has no analogue)
            BuildSseSkinOverridesTab()  ' RaceMenu NiOverride skin body-paint per slot (SSE-only tab; FO4 has no analogue)
            ' Same sliders, different carrier: under Skyrim these are the .jslot's bodyMorphs read through
            ' skee64/RaceMenu, not F4SE's PIRT field — so the FO4 caption would be a lie here.
            GroupBoxBodySlide.Text = "BodySlide Sliders (BODYTRI .tri — vertex morphs, RaceMenu/skee64 field)"
            GroupBoxWeight.Visible = False    ' FO4 MWGT 3-axis triangle
            GroupBoxMrsv.Visible = False      ' FO4 MRSV 5 regions
            ComboBoxLmSkinTemplate.Visible = False : LabelLmSkinTemplate.Visible = False  ' F4SE-only
            ' Overlays: reuse the FO4 controls (set up in BuildSseOverlaysSection AFTER InitOverlaysTab, so the
            ' FO4 init doesn't overwrite the SSE-populated applied list). See below.
        Else
            ' Las dos pestañas RaceMenu viven en el Designer (regla de UI) y Fallout 4 no tiene análogo:
            ' no hay node transforms ni skin overrides de NiOverride. Se quitan del TabControl, que es la
            ' única forma de ocultar un TabPage en WinForms.
            If TabsBody.TabPages.Contains(TabPageSseBodyScale) Then TabsBody.TabPages.Remove(TabPageSseBodyScale)
            If TabsBody.TabPages.Contains(TabPageSseSkinOverrides) Then TabsBody.TabPages.Remove(TabPageSseSkinOverrides)
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

        ' Tab "Skin Tint Adjustment" (los dos juegos): offsets del QNAM del cuerpo. Los controles viven en el
        ' Designer; esto solo ajusta el texto game-aware y siembra los sliders desde el overlay.
        SkinTintPanelBody.Attach(Me)

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
            ' Pin fuera-de-filtro: la UNIÓN DE-DUPLICADA del WNAM actual del NPC y del valor EFECTIVO
            ' seleccionado (el override del preset, si hay).
            ' ⛔ NO uno "o" el otro. Este Sub se volvió REENTRANTE (el botón "…" lo re-llama), y pinear
            ' sólo el efectivo BORRARÍA del combo el WNAM real del NPC cuando ése está fuera del filtro —
            ' que es exactamente el caso para el que el pin existe — dejando al usuario sin forma de volver
            ' a la piel original. Y si después eligiera "(use RACE default)", el override pasa a 0, el
            ' guard `<> 0` no pinea nada, y el combo quedaría sin uno Y sin el otro.
            Dim presetForPin = Preset
            Dim pinCandidates = New UInteger() {_currentWnamFormID,
                                                If(presetForPin.SkinFormIDOverride.HasValue, presetForPin.SkinFormIDOverride.Value, 0UI)}
            For Each pinFid In pinCandidates
                Dim fidToPin = pinFid
                If fidToPin = 0UI Then Continue For
                If filtered.Any(Function(x) x.FormID = fidToPin) Then Continue For
                If _wnamComboFormIDs.Contains(fidToPin) Then Continue For
                Dim disp = _mainForm.GetSkinArmoDisplayName(fidToPin)
                If String.IsNullOrEmpty(disp) Then disp = fidToPin.ToString("X8")
                ComboBoxWnam.Items.Add(disp & "  ⚠ outside race/gender filter")
                _wnamComboFormIDs.Add(fidToPin)
            Next
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

    ''' <summary>Botón "…" del Skin Armor: elegir CUALQUIER ARMO que ocupe el slot BODY del juego activo,
    ''' sin el filtro de raza/género/TXST que aplica el combo.
    ''' <para>El combo es la lista CURADA; esto es la puerta del caso extremo, y la elección es del usuario
    ''' (requisito textual). El predicado vive en <c>MainForm.ArmoHasBodyArmature</c> — ahí está documentado
    ''' por qué no lleva gate de power armor ni filtro "inteligente". El checkbox <b>"Show all"</b> del
    ''' picker (que aparece solo porque le pasamos un filtro) es la vía a literalmente cualquier ARMO.</para>
    ''' <para>Aplicar la selección NO duplica la ley del combo: escribe el MISMO
    ''' <c>SkinFormIDOverride</c> (0 = "(use RACE default)", igual que el índice 0) y pasa por el MISMO
    ''' <see cref="TriggerSkinChangeReload"/>.</para></summary>
    Private Async Sub OnPickWnamClicked(sender As Object, e As EventArgs) Handles ButtonPickWnam.Click
        If _mainForm Is Nothing Then Return
        Dim p = Preset
        Dim currentFid As UInteger = If(p.SkinFormIDOverride.HasValue, p.SkinFormIDOverride.Value, _currentWnamFormID)

        ' Drafts propios sin guardar, con el mismo shape que usan los demás pickers del proyecto.
        Dim draftEntries = _mainForm.ArmoDrafts().Where(Function(d) d.IsDirty).
            Select(Function(d) New FormIdPickerEntry With {
                .FormID = d.FormID, .EditorID = d.EditorID, .DisplayName = d.EditorID, .Signature = "ARMO"}).ToList()

        ' ⛔⛔ EL VALOR ACTUAL PASA SIEMPRE EL FILTRO, aunque no ocupe el slot body.
        ' Sin esto, cuando el skin actual NO pasa el predicado, el picker no tiene esa fila y
        ' `PreselectCurrent` cae a la fila "(none / NULL)" ⇒ la lista arranca mostrando "none" como si
        ' FUERA el estado del NPC, y un OK a secas devuelve 0 = `SkinFormIDOverride = Some(0)`, que es la
        ' codificación EXPLÍCITA de "sin WNAM" ⇒ al guardar, el NPC PIERDE su piel. El usuario no eligió
        ' nada: se la comió la UI. Golpea justo a los NPC de CRIATURA, que son el conjunto que este filtro
        ' deja afuera a propósito (SkinBrahmin, SkinWisp, SkinBearCave…, sin slot body humanoide).
        ' Nota: éste es el ÚNICO de los 14 call sites del picker que combina `formIdFilter` con
        ' `allowNull:=True`; en los demás filtrados el `allowNull:=False` ES la barrera contra esto.
        Dim picked As UInteger
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"ARMO"},
                                           "Select Skin Armor (ARMO) — everything that occupies the body slot",
                                           currentFid, allowNull:=True,
                                           extraDraftEntries:=draftEntries,
                                           formIdFilter:=Function(fid) fid = currentFid OrElse _mainForm.ArmoHasBodyArmature(fid))
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            picked = dlg.SelectedFormID
        End Using

        ' ⛔ UN OK SIN CAMBIAR NADA NO DEBE FABRICAR UN OVERRIDE. `currentFid` sale de
        ' `_currentWnamFormID` = `LastRenderedState.SkinFormID`, o sea la piel YA RESUELTA (con el fallback
        ' de la RACE), no el WNAM propio del record. Escribirla de vuelta convertiría un WNAM HEREDADO en
        ' uno EXPLÍCITO: el Save ESP emitiría un WNAM que el record nunca tuvo y que deja de seguir a la
        ' raza/plantilla. Por el combo esto no puede pasar — WinForms no dispara al re-seleccionar el
        ' índice ya seleccionado — así que el botón no debe ser más destructivo que el combo.
        If p.SkinFormIDOverride.HasValue OrElse picked <> _currentWnamFormID Then
            p.SkinFormIDOverride = picked
        End If
        PopulateSkinCombos()
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

    ' =====================================================================
    ' Puente hacia SkinTintPanel (el tab "Skin tint match"). Ese tab era un PARCIAL de esta clase y leia estos
    ' miembros directo; desde que es un UserControl con clase propia -lo que saca de encima el choque de
    ' nombres de .resx que rompia el build con MSB3577- necesita verlos por una superficie Friend.
    ' Es SOLO relectura de estado que ya existe: no hay logica nueva aca.
    ' =====================================================================
    Friend ReadOnly Property SkinTintPreset As LooksmenuLoader.LooksmenuPreset
        Get
            Return Preset
        End Get
    End Property

    Friend ReadOnly Property SkinTintPriorPreset As LooksmenuLoader.LooksmenuPreset
        Get
            Return _priorPreset
        End Get
    End Property

    Friend ReadOnly Property SkinTintEditorHost As NpcRenderHost
        Get
            Return _editorHost
        End Get
    End Property

    Friend ReadOnly Property SkinTintMainForm As MainForm
        Get
            Return _mainForm
        End Get
    End Property

    Friend ReadOnly Property SkinTintPreview As PreviewControl
        Get
            Return EditPreviewControl
        End Get
    End Property

    Friend ReadOnly Property SkinTintNpcFormID As UInteger
        Get
            Return _rootNpcFormID
        End Get
    End Property

    Friend ReadOnly Property SkinTintIsSse As Boolean
        Get
            Return _isSSE
        End Get
    End Property

    ''' <summary>La supresion de eventos es del FORMULARIO, no del tab: cuando el panel siembra sus sliders
    ''' tiene que levantar el mismo flag que levanta el resto del editor.</summary>
    Friend Property SkinTintSuspendEvents As Boolean
        Get
            Return _suspendEvents
        End Get
        Set(value As Boolean)
            _suspendEvents = value
        End Set
    End Property

    ''' <summary>Reenvio al panel del cambio de tab. Antes lo escuchaba el propio parcial con un Handles sobre
    ''' TabsBody; ahora TabsBody es de este formulario y el panel no lo ve.</summary>
    Private Sub SkinTintTabsBody_SelectedIndexChanged(sender As Object, e As EventArgs) Handles TabsBody.SelectedIndexChanged
        If SkinTintPanelBody Is Nothing Then Return
        SkinTintPanelBody.OnHostTabChanged(TabsBody.SelectedTab Is TabPageSkinTint)
    End Sub

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
    ''' full reload via ResolveNPCBaseState — see 20-app-npc-state-doble-cache.md), refreshes the
    ''' UI mirrors, and schedules a throttled refresh via the existing 500ms timer.</summary>
    Private Sub ApplyMwgt(t As Single, m As Single, f As Single)
        Dim p = Preset
        p.WeightThin = t
        p.WeightMuscular = m
        p.WeightFat = f
        ' Dual-cache sync per 20-app-npc-state-doble-cache.md: BuildBodyWeightPose reads
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

    ''' <summary>Shared DragEnded flush, wired both by AddHandler (dynamic controls, and the FO4 Overlay-tab
    ''' Designer sliders that predate this migration) and, for the SSE controls migrated to the Designer here,
    ''' by Handles — Handles is mandatory for SliderSseNodeScale/PosX/Y/Z/RotX/Y/Z because their populate method
    ''' (PopulateSseBodyScaleTab) runs TWICE per form (ctor + RebuildSseBodyScaleTab); an AddHandler in there would
    ''' double-subscribe and fire the flush twice per drag. SliderSseWeight/SliderSseSkinAlpha are only ever built
    ''' once, so Handles there is a consistency choice, not a correctness requirement.</summary>
    Private Sub OnSliderDragEnded(sender As Object, e As EventArgs) _
        Handles SliderSseWeight.DragEnded, SliderSseSkinAlpha.DragEnded,
                SliderSseNodeScale.DragEnded, SliderSseNodePosX.DragEnded, SliderSseNodePosY.DragEnded, SliderSseNodePosZ.DragEnded,
                SliderSseNodeRotX.DragEnded, SliderSseNodeRotY.DragEnded, SliderSseNodeRotZ.DragEnded
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
        ' Comparacion por REFERENCIA, como las ramas de arriba: desde que las TabPage viven en el Designer
        ' hay un unico objeto por pestana y comparar el Name era la forma indirecta de lo mismo.
        ElseIf active Is TabPageSseBodyScale Then
            ResetSseBodyScaleSection()
        ElseIf active Is TabPageSseSkinOverrides Then
            Await ResetSseSkinOverridesSection()
        ElseIf active Is TabPageSkinTint Then
            SkinTintPanelBody.ResetSkinTintSection()
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

    ''' <summary>Revert MWGT (Weight triangle + 3 sliders), MRSV (5 region bars), Height (NAM6/NAM4) and
    ''' Skin combos (NPC.WNAM + LM template) to the snapshot taken at form-open time. The combos go back to
    ''' whatever the prior overlay carried, falling back to "(use RACE default)" / "(none)" when
    ''' the user opened Edit Body on an NPC with no prior overlay.</summary>
    Private Async Function ResetBodySection() As Task
        Dim p = Preset
        _suspendEvents = True
        Try
            ' Height lives on the NpcRecordOverride, not the overlay, so it has no live effect to undo — but
            ' it MUST be reverted here or the group would visually reset with the rest while OnOk still
            ' committed the edit the user just discarded.
            ResetHeightSection()
            ' SSE weight — revert NAM7 to the value captured at open time. (MWGT/MRSV below are hidden
            ' under _isSSE but their reverts are harmless: WeightTriangle is hidden, _mrsvBars are Nothing.)
            If _isSSE Then
                p.SseWeight = _initialSseWeight
                SliderSseWeight.Value = Math.Max(0.0R, Math.Min(100.0R, Math.Round(CDbl(_initialSseWeight))))
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
            ' ⛔ Also gated on _hasMrsv, exactly like the ctor seed: HasBodyMorphValues=True is an ownership
            ' claim the writer honours, so re-claiming it for a race with NO MRSV channel mints an all-zero
            ' 20-byte subrecord on a record that never had one. The ctor path was fixed first and this one is
            ' the same bug one button later — reachable for creatures/robots now that Height always opens the
            ' editor. The MRSV group is hidden in that case, so there is nothing on screen to revert either.
            If Not _isSSE AndAlso _hasMrsv Then
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

    ''' <summary>Wire up the Designer-built SSE weight group (GroupBoxSseWeight/SliderSseWeight — 00-reglas-ui-y-vb
    ''' §1): swap it into cell (0,0) of BodyTabLayout in place of the hidden FO4 GroupBoxWeight, and seed the slider
    ''' from the overlay's SseWeight. A single 0..100 <see cref="FO4_Base_Library.TinySliderTextBox"/> (the same
    ''' slider style the BodySlide / MRSV rows use) mirrors BuildSseMorphTab.
    ''' ⛔ Vacate cell (0,0) FIRST. This used to add into the cell the (hidden) FO4 GroupBoxWeight still occupied,
    ''' and TableLayoutPanel's behaviour for two controls in one explicit cell is not something to rely on — it may
    ''' overlap them or bump the newcomer to the next free cell, and which row is "next free" moved when the Height
    ''' row was added. Parking the unused FO4 group in the spare last row makes the placement deterministic: the SSE
    ''' weight group owns row 0, so Skyrim reads weight -> Height -> Skin whatever the collision policy is.
    ''' Re-positioned rather than Removed on purpose: a control taken out of the tree is no longer disposed with the
    ''' form, and ResetBodySection still calls WeightTriangle.SetWeights / SyncMwgtSliders on this group's children
    ''' under SSE. Hidden, it contributes no height to its row.</summary>
    Private Sub BuildSseWeightSection()
        GroupBoxSseWeight.Visible = True
        BodyTabLayout.SetCellPosition(GroupBoxWeight, New TableLayoutPanelCellPosition(0, BodyTabSpareRow))
        BodyTabLayout.SetCellPosition(GroupBoxSseWeight, New TableLayoutPanelCellPosition(0, 0))

        ' Seed the slider from the overlay's SseWeight (already seeded in the ctor from the effective NAM7).
        Dim p = Preset
        Dim w As Single = If(p IsNot Nothing AndAlso p.SseWeight.HasValue, p.SseWeight.Value, _initialSseWeight)
        Dim iv As Double = Math.Max(0.0R, Math.Min(100.0R, Math.Round(CDbl(w))))
        _suspendEvents = True
        Try
            SliderSseWeight.Value = iv
        Finally
            _suspendEvents = False
        End Try
    End Sub

    ''' <summary>SSE weight slider moved → write preset.SseWeight and schedule the throttled morph
    ''' refresh. The SSE _0/_1 LERP is a morph channel: OnLocalBodyRefresh rebuilds the composite morph
    ''' resolver, which reads the overlay-applied NAM7 (preset.SseWeight → shadow.Nam7Raw). DragEnded
    ''' (OnSliderDragEnded → FlushRefresh) forces the final value to render immediately.</summary>
    Private Sub OnSseWeightChanged(sender As Object, e As EventArgs) Handles SliderSseWeight.ValueChanged
        If _suspendEvents Then Return
        Dim v As Single = CSng(SliderSseWeight.Value)
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
    ' ListBoxSseNodes, TextBoxSseNodeFilter, PanelSseNodeDetail, LabelSseNodeNote, the 7 TRS sliders and
    ' CheckBoxSseShowAllNodes now live in the Designer (00-reglas-ui-y-vb §1; see EditBody_Form.Designer.vb).
    ' _sseNodeItems / _sseNodeShown / _sseNodeSelected / _sseShowAllNodes stay as plain data fields — they are
    ' not controls.
    Private ReadOnly _sseNodeItems As New List(Of (Label As String, Node As String))
    ' Filter over the node list. With "show all rig bones" on this is every bone of the skeleton (hundreds), so the
    ' box is the only way to reach one by name. _sseNodeShown is the parallel mapping shown-row → item (same idiom
    ' as _ssePaintShown / _bodySlideRows): once the list is filtered, the ListBox index can NOT index _sseNodeItems.
    Private ReadOnly _sseNodeShown As New List(Of (Label As String, Node As String))
    ' Aviso POR NODO, debajo de los sliders (texto puesto en LabelSseNodeNote, ver LoadSseNodeDetail). Hoy sólo
    ' tiene un caso, y es EL caso: el hueso rotulado "Height" es el nodo NPC, que es exactamente donde skee compone
    ' el lift de los tacos altos. O sea que el único NPC que un jugador va a ver más alto en el juego que en el
    ' editor es el que él mismo subió de altura y calzó con botas. Estaba dicho —dentro de una nota de 13 líneas,
    ' a dos diálogos de distancia, sin nombrar el slider— y ahí no le sirve a nadie.
    Private _sseNodeSelected As String
    Private _sseShowAllNodes As Boolean = False

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
                If SseCatalogs.IsWeaponNode(node) AndAlso Not _sseShowAllNodes Then Continue For
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

    Private Sub OnSseShowAllNodesChanged(sender As Object, e As EventArgs) Handles CheckBoxSseShowAllNodes.CheckedChanged
        _sseShowAllNodes = CheckBoxSseShowAllNodes.Checked
        RebuildSseNodeItems()
        PopulateSseNodeList(0)
    End Sub

    ''' <summary>Populate the "Body Transform" tab: rebuild the RaceMenu node catalog (RebuildSseNodeItems) and
    ''' (re)fill the node list. The controls themselves live in the Designer (00-reglas-ui-y-vb §1;
    ''' GroupBoxSseWeight's sibling tabs TabPageSseBodyScale/TabPageSseSkinOverrides) — this only does the
    ''' per-NPC population a Designer field can't do for itself. Runs once from the .ctor (before the preview
    ''' host exists — the catalog is still useful unfiltered) and again from RebuildSseBodyScaleTab once the
    ''' actor's skeleton is available, to intersect the dynamic catalog against the rig's actual bones.</summary>
    Private Sub PopulateSseBodyScaleTab()
        RebuildSseNodeItems()
        PopulateSseNodeList(0)
    End Sub

    ''' <summary>Fill the node ListBox from <see cref="_sseNodeShown"/> (= <see cref="_sseNodeItems"/> narrowed by the
    ''' filter box), marking nodes that carry a non-identity transform with a ●, then load the selected node's TRS
    ''' into the detail sliders. <paramref name="selectIndex"/> is an index into the SHOWN rows.</summary>
    Private Sub PopulateSseNodeList(selectIndex As Integer)
        RebuildSseNodeShown()
        _suspendEvents = True
        Try
            ListBoxSseNodes.BeginUpdate()
            ListBoxSseNodes.Items.Clear()
            For Each it In _sseNodeShown
                ListBoxSseNodes.Items.Add(SseNodeRowLabel(it.Label, it.Node))
            Next
            ListBoxSseNodes.EndUpdate()
            If ListBoxSseNodes.Items.Count > 0 Then ListBoxSseNodes.SelectedIndex = Math.Max(0, Math.Min(selectIndex, ListBoxSseNodes.Items.Count - 1))
        Finally
            _suspendEvents = False
        End Try
        LoadSseNodeDetail()
    End Sub

    ''' <summary>Project <see cref="_sseNodeItems"/> through the filter box into <see cref="_sseNodeShown"/>. Matches
    ''' on the friendly label OR the raw node name (case-insensitive substring), since the label is what the user
    ''' reads for RaceMenu's known nodes while the bone name is what a preset/skeleton carries.</summary>
    Private Sub RebuildSseNodeShown()
        Dim filter = TextBoxSseNodeFilter.Text.Trim()
        _sseNodeShown.Clear()
        For Each it In _sseNodeItems
            If filter.Length = 0 OrElse
               (it.Label IsNot Nothing AndAlso it.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)) OrElse
               (it.Node IsNot Nothing AndAlso it.Node.Contains(filter, StringComparison.OrdinalIgnoreCase)) Then
                _sseNodeShown.Add(it)
            End If
        Next
    End Sub

    ''' <summary>Filter changed → re-project the rows, keeping the selected node selected when it survives the
    ''' filter (otherwise the detail panel would silently jump to another node's TRS).</summary>
    Private Sub OnSseNodeFilterChanged(sender As Object, e As EventArgs) Handles TextBoxSseNodeFilter.TextChanged
        Dim keep = _sseNodeSelected
        RebuildSseNodeShown()
        Dim idx = If(String.IsNullOrEmpty(keep), -1,
                     _sseNodeShown.FindIndex(Function(x) String.Equals(x.Node, keep, StringComparison.OrdinalIgnoreCase)))
        PopulateSseNodeList(Math.Max(0, idx))
    End Sub

    Private Function SseNodeRowLabel(label As String, node As String) As String
        Dim nt = FindSseNodeTransform(node)
        Return If(nt IsNot Nothing AndAlso Not nt.IsIdentity, label & "  ●", label)
    End Function

    Private Function SelectedSseNode() As String
        Dim i = ListBoxSseNodes.SelectedIndex
        ' _sseNodeShown, NOT _sseNodeItems: with a filter typed the row index no longer indexes the full item list.
        If i < 0 OrElse i >= _sseNodeShown.Count Then Return Nothing
        Return _sseNodeShown(i).Node
    End Function

    Private Function FindSseNodeTransform(node As String) As RaceMenuJslot.JslotNodeTransform
        Dim p = Preset
        If p Is Nothing OrElse p.SseNodeTransforms Is Nothing OrElse String.IsNullOrEmpty(node) Then Return Nothing
        Return p.SseNodeTransforms.FirstOrDefault(Function(x) x IsNot Nothing AndAlso String.Equals(x.NodeName, node, StringComparison.OrdinalIgnoreCase))
    End Function

    Private Sub OnSseNodeSelChanged(sender As Object, e As EventArgs) Handles ListBoxSseNodes.SelectedIndexChanged
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
            PanelSseNodeDetail.Enabled = has
            SliderSseNodeScale.Value = If(nt IsNot Nothing AndAlso nt.HasScale, CDbl(nt.Scale), 1.0R)
            SliderSseNodePosX.Value = If(nt IsNot Nothing AndAlso nt.HasPosition, CDbl(nt.PosX), 0.0R)
            SliderSseNodePosY.Value = If(nt IsNot Nothing AndAlso nt.HasPosition, CDbl(nt.PosY), 0.0R)
            SliderSseNodePosZ.Value = If(nt IsNot Nothing AndAlso nt.HasPosition, CDbl(nt.PosZ), 0.0R)
            Dim deg = SseNodeRotDegrees(nt)
            SliderSseNodeRotX.Value = deg.X : SliderSseNodeRotY.Value = deg.Y : SliderSseNodeRotZ.Value = deg.Z
            ' El aviso de los tacos, sólo en el nodo donde el motor los compone (ver el doc de _sseNodeSelected,
            ' arriba, y el texto de LabelSseNodeNote en el Designer). El bloque de abajo es el UNICO texto de la
            ' pestana: la explicacion general (que antes era el encabezado) mas la advertencia del nodo
            ' seleccionado, cuando ese nodo tiene una.
            Dim t = "The value shown is FINAL: several preset sliders on one bone were added into this one " &
                    "number. Only this app's value is written, with ""Attach the helper script"" ticked in " &
                    "the Save dialog."
            If has AndAlso String.Equals(_sseNodeSelected, SseCatalogs.HeightNodeName, StringComparison.OrdinalIgnoreCase) Then
                t &= ControlChars.Lf & ControlChars.Lf &
                     "High-heeled boots add their own lift on top of this bone, so in game the NPC can stand " &
                     "taller than it does here. That is correct — the app does not remove the boots' lift."
            End If
            LabelSseNodeNote.Text = t
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

    Private Sub OnSseNodeScaleChanged(sender As Object, e As EventArgs) Handles SliderSseNodeScale.ValueChanged
        If _suspendEvents Then Return
        ' Escala 0 (o negativa) hace singular la matriz del hueso: la geometría colapsa y la normal
        ' queda indefinida. El slider ya arranca en MinNodeScale, pero AllowExtremeValues deja tipear
        ' un valor fuera de rango, así que el piso se aplica acá también. Re-asignar Value re-entra en
        ' este handler con el valor ya capeado (AreClose corta la recursión).
        If SliderSseNodeScale.Value < MinNodeScale Then
            SliderSseNodeScale.Value = MinNodeScale
            Return
        End If
        Dim nt = EnsureSseNodeTransform() : If nt Is Nothing Then Return
        nt.Scale = CSng(SliderSseNodeScale.Value) : nt.HasScale = True
        ApplySseNodeEdit()
    End Sub

    Private Sub OnSseNodePosChanged(sender As Object, e As EventArgs) _
        Handles SliderSseNodePosX.ValueChanged, SliderSseNodePosY.ValueChanged, SliderSseNodePosZ.ValueChanged
        If _suspendEvents Then Return
        Dim nt = EnsureSseNodeTransform() : If nt Is Nothing Then Return
        nt.PosX = CSng(SliderSseNodePosX.Value) : nt.PosY = CSng(SliderSseNodePosY.Value) : nt.PosZ = CSng(SliderSseNodePosZ.Value)
        nt.HasPosition = True
        ApplySseNodeEdit()
    End Sub

    Private Sub OnSseNodeRotChanged(sender As Object, e As EventArgs) _
        Handles SliderSseNodeRotX.ValueChanged, SliderSseNodeRotY.ValueChanged, SliderSseNodeRotZ.ValueChanged
        If _suspendEvents Then Return
        Dim nt = EnsureSseNodeTransform() : If nt Is Nothing Then Return
        ' STANDARD euler degrees (X/Y/Z about the X/Y/Z axes) → matrix → axis-angle radians (the model/render
        ' canonical form). The NEGATED args are the app's established TRS convention — identical to the
        ' byte-verified FaceBonePoseBuilder: Matrix33ToBSRotation(EulerXYZToMatrix33(-x, -y, -z)). Without the
        ' negation the sliders would be mislabelled (raw EulerXYZToMatrix33 params are yaw=Z, pitch=Y, roll=X, so
        ' "Rotation X" would actually rotate about Z). RotationDirty tells Save to rebuild the key-32 matrix from
        ' this axis-angle (untouched rotations stay byte-exact from Raw).
        Dim m = FO4_Base_Library.Transform_Class.EulerXYZToMatrix33(-SliderSseNodeRotX.Value, -SliderSseNodeRotY.Value, -SliderSseNodeRotZ.Value)
        Dim aa = FO4_Base_Library.Transform_Class.Matrix33ToBSRotation(m)
        ' ⛔ UNA sola llamada, y a propósito: acá se seteaba RotX/Y/Z + HasRotation + RotationDirty a mano y se
        ' OLVIDABA invalidar la matriz cruda, que el sidecar persiste. Ver SetRotationFromUi.
        nt.SetRotationFromUi(aa.X, aa.Y, aa.Z)
        ApplySseNodeEdit()
    End Sub

    Private Sub OnSseNodeResetClick(sender As Object, e As EventArgs) Handles ButtonSseNodeReset.Click
        If String.IsNullOrEmpty(_sseNodeSelected) Then Return
        Dim p = Preset
        If p Is Nothing OrElse p.SseNodeTransforms Is Nothing Then Return
        ' ⛔ ANTES ERA UN RemoveAll, y eso se llevaba el elemento COMPLETO del .jslot: con él la key 40 —el
        ' re-parenteo con el que XPMSE te cuelga la espada de la espalda— y cualquier value ajeno que no modelamos.
        ' La ley del subsistema es por COMPONENTE: "reset" saca lo que se compone (30/31/32) y deja lo demás. El nodo
        ' sólo desaparece de la lista si no quedaba nada más que conservar.
        For Each nt In p.SseNodeTransforms.ToList()
            If nt Is Nothing OrElse Not String.Equals(nt.NodeName, _sseNodeSelected, StringComparison.OrdinalIgnoreCase) Then Continue For
            If Not nt.ResetComposingComponents() Then p.SseNodeTransforms.Remove(nt)
        Next
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
        Dim i = ListBoxSseNodes.SelectedIndex
        If i < 0 OrElse i >= _sseNodeShown.Count Then Return
        Dim it = _sseNodeShown(i)
        Dim newText = SseNodeRowLabel(it.Label, it.Node)
        If String.Equals(CStr(ListBoxSseNodes.Items(i)), newText) Then Return
        Dim keep = _suspendEvents
        _suspendEvents = True
        Try
            ListBoxSseNodes.Items(i) = newText
        Finally
            _suspendEvents = keep
        End Try
    End Sub

    ''' <summary>Reflect the preset's current node transforms onto the tab (after a .jslot load / reset).</summary>
    Private Sub RefreshSseBodyScaleBars()
        PopulateSseNodeList(ListBoxSseNodes.SelectedIndex)
    End Sub

    ''' <summary>Populate the SSE-only "Skin Overrides" tab: RaceMenu NiOverride body-paint per biped slot
    ''' (diffuse/normal texture + tint that replace/tint the worn skin). FO4 has no analogue → the tab lives in
    ''' the Designer, quitado del TabControl bajo Fallout 4 en el .ctor (00-reglas-ui-y-vb §1). Only the dynamic
    ''' bits stay here: the 4-slot texture-box map (index → the Designer's fixed TextBoxSseSkinTex0/1/2/7 — the
    ''' indices are 0/1/2/7, not 0-3, so a map is still needed) and the biped-slot flag grid (BipedSlotCheckboxes,
    ''' data-driven, ítem E de la auditoría — permitido). Editing writes Preset.SseSkinOverrides and live
    ''' re-renders (the render composes them under the tattoo overlays in ResolveSseOverlayLayers).</summary>
    Private Sub BuildSseSkinOverridesTab()
        _sseSkinSlotBoxes.Clear()
        _sseSkinSlotBoxes(0) = TextBoxSseSkinTex0
        _sseSkinSlotBoxes(1) = TextBoxSseSkinTex1
        _sseSkinSlotBoxes(2) = TextBoxSseSkinTex2
        _sseSkinSlotBoxes(7) = TextBoxSseSkinTex7
        ' Full-width row: the biped-slot flag grid (same control the ARMA/ARMO editors use), dynamic/data-driven —
        ' permitted (ítem E). Lives inside GroupBoxSseSkinSlots/FlowSseSkinSlots, declared empty in the Designer.
        _sseSkinSlotChecks = BipedSlotCheckboxes.Build(FlowSseSkinSlots, AddressOf OnSseSkinSlotFlagsChanged, columns:=5)
        RefreshSseSkinList(-1)
    End Sub

    ''' <summary>Owner-draw a skin-override row red when its diffuse (slot 0) texture is missing from the load
    ''' order — the renderer skips a missing texture, so it should read as missing here too.</summary>
    Private Sub DrawSseSkinOverrideItem(sender As Object, e As DrawItemEventArgs) Handles ListBoxSseSkinOverrides.DrawItem
        e.DrawBackground()
        Dim p = Preset
        If e.Index >= 0 AndAlso e.Index < ListBoxSseSkinOverrides.Items.Count Then
            Dim missing As Boolean = False
            If p IsNot Nothing AndAlso p.SseSkinOverrides IsNot Nothing AndAlso e.Index < p.SseSkinOverrides.Count Then
                Dim sk = p.SseSkinOverrides(e.Index)
                missing = sk IsNot Nothing AndAlso Not String.IsNullOrEmpty(sk.DiffusePath) AndAlso Not SseCatalogs.TextureResolves(sk.DiffusePath)
            End If
            Dim fore = If(missing, SseCatalogs.MissingTextureColor,
                          If((e.State And DrawItemState.Selected) <> 0, SystemColors.HighlightText, e.ForeColor))
            TextRenderer.DrawText(e.Graphics, ListBoxSseSkinOverrides.Items(e.Index).ToString(), e.Font, e.Bounds, fore,
                                  TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
        End If
        e.DrawFocusRectangle()
    End Sub

    ''' <summary>The skin override selected in the list, or Nothing.</summary>
    Private Function SelectedSseSkinOverride() As RaceMenuJslot.JslotSkinOverride
        Dim p = Preset
        If p Is Nothing OrElse p.SseSkinOverrides Is Nothing Then Return Nothing
        Dim idx = ListBoxSseSkinOverrides.SelectedIndex
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
            ListBoxSseSkinOverrides.BeginUpdate()
            Try
                ListBoxSseSkinOverrides.Items.Clear()
                Dim p = Preset
                If p IsNot Nothing AndAlso p.SseSkinOverrides IsNot Nothing Then
                    For Each sk In p.SseSkinOverrides
                        If sk Is Nothing Then Continue For
                        ListBoxSseSkinOverrides.Items.Add(SseSkinLabel(sk))
                    Next
                End If
            Finally
                ListBoxSseSkinOverrides.EndUpdate()
            End Try
            Dim n = ListBoxSseSkinOverrides.Items.Count
            If n > 0 Then ListBoxSseSkinOverrides.SelectedIndex = Math.Max(0, Math.Min(selectIndex, n - 1))
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
            CheckBoxSseSkinTint.Enabled = has
            ButtonSseSkinTintColor.Enabled = has AndAlso sk.HasTint
            ' Alpha (key 8) is independent of the tint colour — enabled whenever an override is selected.
            SliderSseSkinAlpha.Enabled = has
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
                CheckBoxSseSkinTint.Checked = sk.HasTint
                ButtonSseSkinTintColor.BackColor = If(sk.HasTint, Color.FromArgb(ClampByte(sk.TintR), ClampByte(sk.TintG), ClampByte(sk.TintB)), Color.White)
                SliderSseSkinAlpha.Value = CDbl(Math.Max(0.0F, Math.Min(1.0F, If(sk.HasAlpha, sk.Alpha, 1.0F))))
            Else
                If _sseSkinSlotChecks IsNot Nothing Then BipedSlotCheckboxes.SetMask(_sseSkinSlotChecks, 0UI)
                For Each kvp In _sseSkinSlotBoxes
                    If kvp.Value IsNot Nothing Then kvp.Value.Text = ""
                Next
                CheckBoxSseSkinTint.Checked = False
                ButtonSseSkinTintColor.BackColor = Color.White : SliderSseSkinAlpha.Value = 1.0R
            End If
        Finally
            _suspendEvents = False
        End Try
    End Sub

    Private Sub OnSseSkinSelChanged(sender As Object, e As EventArgs) Handles ListBoxSseSkinOverrides.SelectedIndexChanged
        If _suspendEvents Then Return
        UpdateSseSkinDetail()
    End Sub

    Private Async Sub OnSseSkinAdd(sender As Object, e As EventArgs) Handles ButtonSseSkinAdd.Click
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

    Private Async Sub OnSseSkinRemove(sender As Object, e As EventArgs) Handles ButtonSseSkinRemove.Click
        Dim p = Preset
        Dim idx = ListBoxSseSkinOverrides.SelectedIndex
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
        Dim i = ListBoxSseSkinOverrides.SelectedIndex
        If i >= 0 Then ListBoxSseSkinOverrides.Items(i) = SseSkinLabel(sk)
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
        Dim i = ListBoxSseSkinOverrides.SelectedIndex
        If i >= 0 Then ListBoxSseSkinOverrides.Items(i) = SseSkinLabel(sk)
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

    ' 8 one-line Handles delegates (Pick/Clear × 4 fixed slots 0/1/2/7 — literal, per 00-reglas-ui-y-vb §InitializeComponent
    ' §9: N ≤ 8, no For/If in the Designer, so each button needs its own named handler instead of a sender-index lookup).
    Private Sub OnSseSkinTexPick0Click(sender As Object, e As EventArgs) Handles ButtonSseSkinTexPick0.Click
        PickSseSkinSlotTexture(0)
    End Sub
    Private Sub OnSseSkinTexClear0Click(sender As Object, e As EventArgs) Handles ButtonSseSkinTexClear0.Click
        SetSseSkinSlotTexture(0, "")
    End Sub
    Private Sub OnSseSkinTexPick1Click(sender As Object, e As EventArgs) Handles ButtonSseSkinTexPick1.Click
        PickSseSkinSlotTexture(1)
    End Sub
    Private Sub OnSseSkinTexClear1Click(sender As Object, e As EventArgs) Handles ButtonSseSkinTexClear1.Click
        SetSseSkinSlotTexture(1, "")
    End Sub
    Private Sub OnSseSkinTexPick2Click(sender As Object, e As EventArgs) Handles ButtonSseSkinTexPick2.Click
        PickSseSkinSlotTexture(2)
    End Sub
    Private Sub OnSseSkinTexClear2Click(sender As Object, e As EventArgs) Handles ButtonSseSkinTexClear2.Click
        SetSseSkinSlotTexture(2, "")
    End Sub
    Private Sub OnSseSkinTexPick7Click(sender As Object, e As EventArgs) Handles ButtonSseSkinTexPick7.Click
        PickSseSkinSlotTexture(7)
    End Sub
    Private Sub OnSseSkinTexClear7Click(sender As Object, e As EventArgs) Handles ButtonSseSkinTexClear7.Click
        SetSseSkinSlotTexture(7, "")
    End Sub

    Private Sub OnSseSkinTintToggled(sender As Object, e As EventArgs) Handles CheckBoxSseSkinTint.CheckedChanged
        If _suspendEvents Then Return
        Dim sk = SelectedSseSkinOverride()
        If sk Is Nothing Then Return
        sk.HasTint = CheckBoxSseSkinTint.Checked
        ButtonSseSkinTintColor.Enabled = sk.HasTint   ' alpha (key 8) is independent — stays enabled
        Dim i = ListBoxSseSkinOverrides.SelectedIndex
        If i >= 0 Then ListBoxSseSkinOverrides.Items(i) = SseSkinLabel(sk)
        TriggerSseOverlayLive()
    End Sub

    Private Sub OnSseSkinTintColor(sender As Object, e As EventArgs) Handles ButtonSseSkinTintColor.Click
        Dim sk = SelectedSseSkinOverride()
        If sk Is Nothing Then Return
        Using dlg As New ColorDialog() With {.Color = Color.FromArgb(ClampByte(sk.TintR), ClampByte(sk.TintG), ClampByte(sk.TintB))}
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                sk.TintR = dlg.Color.R / 255.0F : sk.TintG = dlg.Color.G / 255.0F : sk.TintB = dlg.Color.B / 255.0F
                sk.HasTint = True
                ButtonSseSkinTintColor.BackColor = dlg.Color
                TriggerSseOverlayLive()
            End If
        End Using
    End Sub

    Private Sub OnSseSkinTintAlpha(sender As Object, e As EventArgs) Handles SliderSseSkinAlpha.ValueChanged
        If _suspendEvents Then Return
        Dim sk = SelectedSseSkinOverride()
        If sk Is Nothing Then Return
        ' key 8 (kParam_ShaderAlpha) — the override's material alpha, independent of the tint colour.
        sk.Alpha = CSng(SliderSseSkinAlpha.Value) : sk.HasAlpha = True
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
        ' ⛔ Lo que este método hacía ANTES para reproducir el layout de 3 columnas ya declaradas del Designer
        ' (GroupBoxOverlayAvailable.Visible=True, 3× SetCellPosition, 3× ColumnStyles) eran no-ops: el Designer ya
        ' declara exactamente esa disposición (OverlayListsLayout 50%/AutoSize/50%, celdas (0,0)/(1,0)/(2,0)) —
        ' medido, no supuesto. Y el SetRow(ButtonOverlayAdd,1)/SetRow(ButtonOverlayRemove,2) desaparece porque las
        ' filas quedan declaradas en el Designer (FlowSseOverlayZone en fila 0).
        GroupBoxOverlayAvailable.Text = "Paints (RaceMenu)"
        GroupBoxOverlayApplied.Text = "Applied overlays"
        TextBoxOverlayFilter.PlaceholderText = "Filter paints…"
        ' Mark rows whose texture isn't in the load order in red (same convention as the tint tab). A mod can
        ' register a paint whose .dds it doesn't ship — RaceMenu (and this app) then render nothing; showing it
        ' missing is clearer than a silent no-op. Owner-draw is set here (SSE only); FO4 keeps the default draw.
        ' ⛔ DrawMode + AddHandler DrawItem de estos DOS ListBox se quedan por código (no en el Designer): son
        ' controles COMPARTIDOS con FO4 y ponerlos en el Designer le cambiaría el dibujado a Fallout 4.
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
        ' (OverlayInterface.h:33-46). Sits DIRECTLY ABOVE the Add button (center column, FlowSseOverlayZone —
        ' now Designer-built, row 0 of OverlayCenterLayout). It drives BOTH which paint category the LEFT catalog
        ' shows AND which zone "Add →" creates the overlay on.
        FlowSseOverlayZone.Visible = True
        ' ⛔ SelectedIndex se fija ACÁ, no en el Designer (00-reglas-ui-y-vb §2.4bis): el combo YA tiene sus 3
        ' ítems literales del Designer, pero fijar el índice ahí dispararía SelectedIndexChanged dentro de
        ' InitializeComponent(), antes de que el .ctor asigne _appliedPresets, y el handler llega a Preset.
        ' ⛔ Y NO se envuelve en _suspendEvents: OnSseOverlayZoneChanged NO consulta ese flag —igual que la lambda
        ' que tenía antes—, así que el wrap sería un gate que no puede fallar y un comentario que miente. Ponerle
        ' el guard al handler SÍ cambiaría comportamiento: UpdateSseOverlayDetail apunta este combo a la zona del
        ' overlay seleccionado DENTRO de _suspendEvents, y hoy eso re-filtra el catálogo a esa zona a propósito.
        ' Consecuencia asumida: esta línea dispara un RefreshSsePaintCatalog() de más, que el del final de este
        ' mismo método repite. Es un llenado de ListBox por apertura de editor, una vez.
        ComboBoxSseOverlayZone.SelectedIndex = 0
        ' Top-align the whole center column (default is Anchor.None = vertically centred) so the Zone row sits at the
        ' same height as the filter box; OnOverlayListsLayout supplies the exact offset once layout is real.
        OverlayCenterLayout.Anchor = AnchorStyles.Top
        ' Add/Remove: drop AutoSize and stretch them to the column width (which the zone row — label + combo —
        ' defines), so both buttons are the same width and symmetric with "Zone: [combo]" above.
        For Each b As Button In New Button() {ButtonOverlayAdd, ButtonOverlayRemove}
            b.AutoSize = False
            b.Anchor = AnchorStyles.Left Or AnchorStyles.Right
            b.Height = 25
        Next
        AddHandler OverlayListsLayout.Layout, AddressOf OnOverlayListsLayout

        ' Applied-overlay texture rows, magic checkbox and its note now live in the Designer (OverlayPropsLayout
        ' rows 6-9), hidden by default — just show them.
        LabelSseOverlayTexture.Visible = True
        SseOverlayDiffuseRow.Visible = True
        LabelSseOverlayNormal.Visible = True
        SseOverlayNormalRow.Visible = True
        CheckBoxSseOverlayMagic.Visible = True
        LabelSseOverlayMagicNote.Visible = True

        RefreshSseOverlayList()
        RefreshSsePaintCatalog()
    End Sub

    ''' <summary>Conmuta el overlay seleccionado entre el pool normal y el MAGIC renombrando su nodo.
    ''' <para>⛔ RENOMBRAR ES EL MECANISMO, no un efecto secundario: el nombre del nodo ES la identidad del override
    ''' en skee, en el co-save y en el <c>.jslot</c> (por eso <see cref="RaceMenuJslot.JslotOverlayNode.IsSpell"/> se
    ''' deriva del nombre y no es un campo aparte). El índice se recalcula con
    ''' <see cref="SseCatalogs.NextFreeOverlayIndex"/> porque los dos pools numeran INDEPENDIENTE: el
    ''' <c>[Ovl2]</c> que se convierte no puede quedar como <c>[SOvl2]</c> si ese slot magic ya está ocupado.</para>
    ''' <para>Aviso una vez por sesión si el destino no tiene tantos slots (misma regla y mismo texto que el Add;
    ''' el overlay se convierte igual — es legal e inerte hasta que suba la key).</para></summary>
    Private Async Sub OnSseOverlayMagicChanged(sender As Object, e As EventArgs) Handles CheckBoxSseOverlayMagic.CheckedChanged
        If _suspendEvents Then Return
        Dim p = Preset
        Dim ov = SelectedSseOverlay()
        If p Is Nothing OrElse ov Is Nothing Then Return
        Dim z = SseCatalogs.ZoneOfNode(ov.NodeName)
        If Not z.HasValue Then Return
        Dim toSpell = CheckBoxSseOverlayMagic.Checked
        If toSpell = ov.IsSpell Then Return   ' ya está en ese pool (re-seed de la UI) → nada que hacer
        Dim n = SseCatalogs.NextFreeOverlayIndex(p.SseBodyOverlays, z.Value, toSpell)
        ' ⛔⛔ ACÁ EL POOL MAGIC SE NEGABA, Y ERA INCOHERENTE CON LA DECISIÓN QUE YA ESTABA TOMADA PARA TODOS LOS
        ' OVERLAYS: avisar y dejar autorar. La negativa se apoyaba en "en la partida de algunos jugadores NO SE PUEDE
        ' SACAR", y eso dejó de ser cierto cuando el barrido pasó a recorrer los 127 slots que skee puede crear —
        ' el techo del que dependía ese argumento ya no existe. Consecuencias medidas de la negativa:
        '   · con el default del motor (iSpellOverlays=1) se podía autorar UN magic por zona y nada más;
        '   · con [Features] bEnableFaceOverlays=0 NO se podía autorar NI UNO de cara, nunca;
        '   · y como el Return salía primero, el aviso de abajo con isSpell:=True era INALCANZABLE desde producto —
        '     o sea superficie que sólo usaba el gate y viajaba igual en el binario que se distribuye.
        ' El pool normal, en la misma situación, avisa y sigue. Ahora los dos hacen lo mismo.
        ' El aviso usa el contador DEL MOTOR y tiene su one-shot POR POOL.
        Dim limit = SseCatalogs.OverlayCount(z.Value, toSpell)
        If n >= limit AndAlso SseCatalogs.ClaimOverlayLimitWarning(toSpell) Then
            MessageBox.Show(Me, SseCatalogs.OverlayLimitNotice(z.Value, n, limit, toSpell),
                            "Overlay past the RaceMenu slot count", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
        ov.NodeName = SseCatalogs.OverlayNodeName(z.Value, n, toSpell)
        p.HasOverlays = True
        RefreshSseAppliedList(0, ov)
        Await TriggerOverlayReload()
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
        ' El gate es el JUEGO. Los dos guards de nulo que habia aca no podian fallar desde que los dos
        ' controles viven en el Designer (00-reglas-epistemica §9).
        If Not _isSSE Then Return
        _ssePaintShown.Clear()
        ListBoxOverlayAvailable.BeginUpdate()
        Try
            ListBoxOverlayAvailable.Items.Clear()
            Dim cat = FO4_Base_Library.RaceMenuPaintCatalog.Current
            If cat Is Nothing Then Return
            Dim zone As SseCatalogs.OverlayZone = CType(Math.Max(0, ComboBoxSseOverlayZone.SelectedIndex), SseCatalogs.OverlayZone)
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

    ''' <summary>Zone combo changed → the LEFT paint catalog is filtered by zone. Named Handles sub (not the
    ''' original inline lambda) because ComboBoxSseOverlayZone is now a static Designer WithEvents field.</summary>
    Private Sub OnSseOverlayZoneChanged(sender As Object, e As EventArgs) Handles ComboBoxSseOverlayZone.SelectedIndexChanged
        RefreshSsePaintCatalog()
    End Sub

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

    ''' <summary>⛔ <paramref name="selectNode"/> GANA sobre <paramref name="selectIndex"/> cuando está en la lista.
    ''' Un número de fila NO identifica un overlay acá: la lista se muestra ordenada por zona y por índice de nodo
    ''' DESCENDENTE, no en el orden del carrier. Después de un Add la fila del recién agregado depende de su zona y
    ''' del hueco que tomó, así que pasar una constante (era <c>0</c>) seleccionaba OTRO overlay — típicamente uno
    ''' de Body cuando el agregado era de Hands/Feet — y el panel de detalle editaba el equivocado.</summary>
    Private Sub RefreshSseAppliedList(selectIndex As Integer, Optional selectNode As RaceMenuJslot.JslotOverlayNode = Nothing)
        _suspendEvents = True
        Try
            ListBoxOverlayApplied.BeginUpdate()
            Try
                ListBoxOverlayApplied.Items.Clear()
                _sseShownOverlays.Clear()
                Dim p = Preset
                If p IsNot Nothing AndAlso p.SseBodyOverlays IsNot Nothing Then
                    ' Show in DRAW ORDER so Up/Down are intuitive: group by zone (Body, Hands, Feet), and within a
                    ' zone list the topmost-drawn first. Face overlays are edited on the Face Paint tab, excluded here.
                    ' ⭐ EL ORDEN DENTRO DE LA ZONA ES LA CLAVE DE COMPOSICIÓN, no el índice pelado: el pool magic va
                    ' ENCIMA de TODO el pool normal (skee instala el pool primario y después el secundario), así que
                    ' un [SOvl0] se dibuja sobre un [Ovl5]. Ordenar por índice mostraba la lista al revés de lo que
                    ' se ve, y Up/Down "arreglaban" un orden que no era el que estaba mal.
                    Dim shown = p.SseBodyOverlays.
                        Where(Function(o) o IsNot Nothing AndAlso SseCatalogs.ZoneOfNode(o.NodeName).HasValue AndAlso
                                          SseCatalogs.ZoneOfNode(o.NodeName).Value <> SseCatalogs.OverlayZone.Face).
                        OrderBy(Function(o) CInt(SseCatalogs.ZoneOfNode(o.NodeName).Value)).
                        ThenByDescending(Function(o) SseOverlayCompositor.CompositeOrderKey(o.NodeName)).ToList()
                    For Each ov In shown
                        _sseShownOverlays.Add(ov)
                        ListBoxOverlayApplied.Items.Add(SseOverlayLabel(ov))
                    Next
                End If
            Finally
                ListBoxOverlayApplied.EndUpdate()
            End Try
            Dim n = ListBoxOverlayApplied.Items.Count
            Dim want = selectIndex
            If selectNode IsNot Nothing Then
                ' Referencia, no valor: JslotOverlayNode no redefine Equals, así que IndexOf busca ESTE objeto.
                Dim byId = _sseShownOverlays.IndexOf(selectNode)
                If byId >= 0 Then want = byId
            End If
            If n > 0 Then ListBoxOverlayApplied.SelectedIndex = Math.Max(0, Math.Min(want, n - 1))
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
        ' El nodo ya dice [SOvl] vs [Ovl], pero la etiqueta explícita es lo que hace la lista legible de un vistazo
        ' (y el pool magic no se ve en el preview principal, así que conviene que salte).
        Return $"{ov.NodeName} — {diff}{If(ov.IsSpell, "  [magic]", "")}{If(ov.HasTint, "  ●", "")}"
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
            ' Los cuatro controles son del Designer: NUNCA son Nothing, así que ya no se chequea. El guard de nulo
            ' que había acá existía porque bajo Fallout 4 estos controles no se construían; el gate real de esta
            ' pestaña es que no exista bajo FO4 (ver la rama Else del .ctor).
            If has Then
                Dim z = SseCatalogs.ZoneOfNode(ov.NodeName)
                If z.HasValue AndAlso CInt(z.Value) < ComboBoxSseOverlayZone.Items.Count Then ComboBoxSseOverlayZone.SelectedIndex = CInt(z.Value)
            End If
            TextBoxSseOverlayDiffuse.Text = If(has, If(ov.DiffusePath, ""), "") : TextBoxSseOverlayDiffuse.Enabled = has
            TextBoxSseOverlayNormal.Text = If(has, If(ov.NormalPath, ""), "") : TextBoxSseOverlayNormal.Enabled = has
            ' Se siembra del NOMBRE del nodo (IsSpell es derivado) — no hay estado paralelo que sincronizar.
            CheckBoxSseOverlayMagic.Checked = has AndAlso ov.IsSpell
            CheckBoxSseOverlayMagic.Enabled = has
            ' ⭐ LA OPACIDAD DE UN MAGIC OVERLAY LA MANEJA EL MOTOR. Medido en la plantilla del pool: un controller
            ' ACTIVE + CYCLE_REVERSE anima la Alpha 0↔1 (ver SseOverlayCompositor.IsSpellOverlayNodeName), así que el
            ' valor autorado se guarda y viaja, pero in-game lo pisa la animación mientras corre. Dejar el slider
            ' igual que en un overlay normal era prometer un control que el motor no respeta.
            ' ⛔ ERA "Opacity ⚠:" con el motivo SÓLO en el tooltip del slider. Un ⚠ pelado es una alarma sobre la que el
        ' usuario no puede actuar: no dice qué pasa ni qué hacer. El motivo va escrito, una vez, abajo.
        LabelOverlayTintAlpha.Text = "Opacity:"
            _sseOverlayToolTip.SetToolTip(SliderOverlayTintAlpha,
                If(has AndAlso ov.IsSpell,
                   "Saved and written to the NPC, but the engine ANIMATES a magic overlay's alpha (it pulses 0↔1)," & vbCrLf &
                   "so this value is what the preview shows, not what the game holds steady.",
                   "skee64 kParam_ShaderAlpha (key 8): the overlay's opacity, independent of the tint colour."))
            ' Up/Down sólo tienen sentido entre vecinos del MISMO pool (los stacks son independientes y el magic va
            ' entero encima). Se DESHABILITAN en vez de ignorar el click: un botón que no hace nada al apretarlo es
            ' el mismo defecto que este editor ya arrastró con "Up/Down parecían no funcionar".
            UpdateSseOverlayMoveEnabled()
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

    ''' <summary>Habilita Up/Down sólo cuando el movimiento es POSIBLE: el vecino de la fila tiene que estar en la
    ''' misma zona Y en el mismo pool.
    ''' <para>⛔ Sin esto los botones quedaban vivos y el click era un <c>Return</c> mudo — y en el caso MÁS COMÚN
    ''' (una zona con un solo overlay magic, que es el default <c>iSpellOverlays=1</c>) el magic y el normal más alto
    ''' son vecinos de fila, así que Up/Down no hacían absolutamente nada sin explicar por qué.</para></summary>
    Private Sub UpdateSseOverlayMoveEnabled()
        If Not _isSSE Then Return
        Dim row = ListBoxOverlayApplied.SelectedIndex
        ButtonOverlayUp.Enabled = SseCanMoveOverlay(row, -1)
        ButtonOverlayDown.Enabled = SseCanMoveOverlay(row, 1)
    End Sub

    ''' <summary>El MISMO predicado que aplica <see cref="SseMoveOverlay"/> — un solo lugar decide "se puede".</summary>
    Private Function SseCanMoveOverlay(row As Integer, delta As Integer) As Boolean
        If row < 0 OrElse row >= _sseShownOverlays.Count Then Return False
        Dim target = row + delta
        If target < 0 OrElse target >= _sseShownOverlays.Count Then Return False
        Dim a = _sseShownOverlays(row), b = _sseShownOverlays(target)
        If a Is Nothing OrElse b Is Nothing Then Return False
        Dim zA = SseCatalogs.ZoneOfNode(a.NodeName), zB = SseCatalogs.ZoneOfNode(b.NodeName)
        If Not zA.HasValue OrElse Not zB.HasValue OrElse zA.Value <> zB.Value Then Return False
        Return SseCatalogs.IsSpellNode(a.NodeName) = SseCatalogs.IsSpellNode(b.NodeName)
    End Function

    ''' <summary>Write the SSE overlay tint COLOUR from the reused FO4 swatch. skee64 unpacks the tint into an
    ''' NiColor (RGB only), so the colour carries no opacity; opacity is the separate key-8 alpha override
    ''' written by <see cref="OnOverlayTintAlphaChanged"/>.</summary>
    Private Sub WriteSseOverlayTint(ov As RaceMenuJslot.JslotOverlayNode)
        Dim c = ButtonOverlayTintColor.BackColor
        ov.TintR = c.R / 255.0F : ov.TintG = c.G / 255.0F : ov.TintB = c.B / 255.0F
    End Sub

    ''' <summary>Add an overlay to the next free <c>[Ovl n]</c> slot of the chosen zone. The slot count from
    ''' <c>skee64.ini</c> is ADVISORY: past it skee64 creates no node, so the overlay is inert in-game (and
    ''' becomes live if the count is raised later) — never an error. The Add proceeds and the user gets the
    ''' notice once per session (<see cref="SseCatalogs.ClaimOverlayLimitWarning"/>).</summary>
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

        Dim zone As SseCatalogs.OverlayZone = CType(Math.Max(0, ComboBoxSseOverlayZone.SelectedIndex), SseCatalogs.OverlayZone)
        ' Add crea en el pool NORMAL; el checkbox "Magic" del panel de detalle lo convierte después (un control, un
        ' significado). El índice libre se busca DENTRO del pool: los dos numeran independiente.
        Dim limit = SseCatalogs.OverlayCount(zone)
        Dim n = SseCatalogs.NextFreeOverlayIndex(p.SseBodyOverlays, zone, False)
        If n >= limit AndAlso SseCatalogs.ClaimOverlayLimitWarning() Then
            MessageBox.Show(Me, SseCatalogs.OverlayLimitNotice(zone, n, limit),
                            "Overlay past the RaceMenu slot count", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If

        ' An Ex registration carries the full texture set; slot 1 is the normal. Plain paints have diffuse only.
        Dim nrm As String = ""
        If entry.Slots IsNot Nothing AndAlso entry.Slots.Length > 1 Then
            Dim s1 = entry.Slots(1)
            If Not String.IsNullOrEmpty(s1) AndAlso Not s1.Equals("ignore", StringComparison.OrdinalIgnoreCase) Then nrm = s1
        End If
        Dim added As New RaceMenuJslot.JslotOverlayNode With {
            .NodeName = SseCatalogs.OverlayNodeName(zone, n), .DiffusePath = entry.Path, .NormalPath = nrm,
            .TintR = 1, .TintG = 1, .TintB = 1, .TintA = 1, .HasTint = False,
            .Alpha = 1.0F, .HasAlpha = True}
        p.SseBodyOverlays.Insert(0, added)
        p.HasOverlays = True
        RefreshSseAppliedList(0, added)
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
        ' ⛔ Y TAMPOCO ENTRE POOLS. Los stacks normal y magic son independientes (numeración propia, y el magic va
        ' entero encima), así que "intercambiar los índices" entre un [Ovl] y un [SOvl] no reordena nada: MUEVE de
        ' pool a los dos overlays, que es una conversión silenciosa — justo lo que el checkbox Magic hace explícito.
        If SseCatalogs.IsSpellNode(ov.NodeName) <> SseCatalogs.IsSpellNode(neighbour.NodeName) Then Return
        Dim ni = SseCatalogs.IndexOfNode(ov.NodeName)
        Dim nj = SseCatalogs.IndexOfNode(neighbour.NodeName)
        If ni < 0 OrElse nj < 0 Then Return
        Dim spell = SseCatalogs.IsSpellNode(ov.NodeName)
        ov.NodeName = SseCatalogs.OverlayNodeName(zA.Value, nj, spell)
        neighbour.NodeName = SseCatalogs.OverlayNodeName(zB.Value, ni, spell)
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

    ''' <summary>Seed the Height group from the record and gate it per GAME. Skyrim's NPC_ carries a single
    ''' NAM6 "Height" and no NAM4 at all (measured: 0 of 5118 records in Skyrim.esm have one), so under SSE the
    ''' Max row is hidden and never read — its TableLayoutPanel row is AutoSize, so hiding both controls
    ''' collapses it instead of leaving a gap. FO4 shows Min + Max.
    ''' No render hook on purpose: the height does not scale the preview (the viewport already frames the NPC),
    ''' and there is no single "correct" preview height anyway — the engine draws one per actor REFERENCE.</summary>
    Private Sub InitHeightSection(initial As InitialValues)
        _hadHeightMin = initial IsNot Nothing AndAlso initial.HasHeightMin
        _hadHeightMax = initial IsNot Nothing AndAlso initial.HasHeightMax
        ' raw* = exactly what the record carries, kept for the read-only caption below; seed* is what the
        ' sliders open at and may be substituted for a half-written pair.
        Dim rawMin As Double = CDbl(If(initial IsNot Nothing, initial.HeightMin, 1.0F))
        Dim rawMax As Double = CDbl(If(initial IsNot Nothing, initial.HeightMax, 1.0F))
        Dim seedMin As Double = rawMin
        Dim seedMax As Double = rawMax
        ' Half-written pair (FO4): seed the missing side FROM the present one so the dialog never opens showing
        ' an inverted pair. Both directions — an absent NAM6 with NAM4 = 0.8 would otherwise open at 100%/80%.
        ' This only affects what is DISPLAYED; the absent side is labelled and is never written (see the
        ' entitlement gate in RegisterHeightOverride).
        If Not _isSSE Then
            If _hadHeightMin AndAlso Not _hadHeightMax Then seedMax = seedMin
            If _hadHeightMax AndAlso Not _hadHeightMin Then seedMin = seedMax
        End If
        ' The sliders carry AllowExtremeValues, so they will NOT clamp for us — a value between the 20%..200%
        ' track and the CK's real [0.1, 10] limit (say 5.0) is shown and edited verbatim, which is the point.
        ' ClampHeight is what enforces the CK limit; a value it actually moves is one the record holds but the
        ' editor cannot represent, which flips the section read-only below.
        Dim shownMin As Double = ClampHeight(seedMin)
        Dim shownMax As Double = ClampHeight(seedMax)
        ' NaN has to be tested explicitly: ClampHeight maps it to 1.0, and every comparison against NaN is
        ' False, so the subtraction test alone would silently call a corrupt field "unchanged" and then let
        ' the cross-clamp overwrite it.
        _heightMinSeedClamped = Double.IsNaN(seedMin) OrElse Math.Abs(shownMin - seedMin) > 0.0000005
        _heightMaxSeedClamped = Double.IsNaN(seedMax) OrElse Math.Abs(shownMax - seedMax) > 0.0000005

        EnsureHeightTrackCovers(shownMin, shownMax)
        Dim prevSync As Boolean = _heightSyncing
        _heightSyncing = True
        Try
            SliderHeightMin.Value = shownMin
            SliderHeightMax.Value = shownMax
        Finally
            _heightSyncing = prevSync
        End Try
        ' Snapshot AFTER assigning, so nothing the seeding itself did can register as a user edit.
        _snapHeightMin = SliderHeightMin.Value
        _snapHeightMax = SliderHeightMax.Value

        If _isSSE Then
            ' Captions stay short on purpose: a GroupBox caption neither wraps nor ellipsizes, and the tab is
            ' only ~508 px. "100% = 1.0" is the unit bridge — the CK, xEdit and this app's record tree all show
            ' height as a bare multiplier, so without it a user retypes "1" here and silently gets 0.01.
            GroupBoxHeight.Text = "Height (NPC.NAM6 · 100% = 1.0)"
            LabelHeightMin.Text = "Height:"
            LabelHeightMax.Visible = False
            SliderHeightMax.Visible = False
        Else
            GroupBoxHeight.Text = "Height (NAM6 Min / NAM4 Max · 100% = 1.0)"
            ' Say which half the record does not actually carry, matching the record tree's "(absent)". The
            ' slider still shows a seeded number so the pair reads sanely, but it is not a stored value.
            If Not _hadHeightMin Then LabelHeightMin.Text = "Min (absent):"
            If Not _hadHeightMax Then LabelHeightMax.Text = "Max (absent):"
        End If

        ' Only fires when the record is outside the CK's hard [0.1, 10] — a value merely outside the 20%..200%
        ' TRACK (say 5.0) is shown and edited verbatim thanks to AllowExtremeValues. Past the hard limit the
        ' number cannot be represented at all, and editing from a substituted value would write something the
        ' user never saw, or leave Min > Max on disk while the screen says otherwise. Rather than guess, the
        ' section goes read-only and says what the record really holds. Does not occur in vanilla (FO4 heights
        ' span 0.30..2.00, Skyrim 0.60..2.00); this is for hand-edited mod records.
        If _heightMinSeedClamped OrElse _heightMaxSeedClamped Then
            SliderHeightMin.Enabled = False
            SliderHeightMax.Enabled = False
            ' Report the RECORD's values, not the seeds: the half-pair substitution above may have invented the
            ' missing side, and this caption exists precisely to state what the file actually holds. Skyrim has
            ' no NAM4, so it never gets a "max" clause.
            ' Kept short: the numbers are the only part the user cannot read anywhere else (the sliders show
            ' the CLAMPED values), so they must not be what gets clipped off a caption that cannot ellipsize.
            ' Six decimals, not three: this caption is the ONLY place the real value is legible, so rounding
            ' 0.0001 down to "0" here would recreate the very ambiguity the record tree's "(absent)" just fixed.
            Dim txtMin As String = If(_hadHeightMin, rawMin.ToString("G6"), "absent")
            If _isSSE Then
                GroupBoxHeight.Text = $"Height — read-only: {txtMin} outside [0.1, 10]"
            Else
                Dim txtMax As String = If(_hadHeightMax, rawMax.ToString("G6"), "absent")
                GroupBoxHeight.Text = $"Height — read-only: outside [0.1, 10] (min {txtMin} / max {txtMax})"
            End If
        End If

        AddHandler SliderHeightMin.ValueChanged, AddressOf OnHeightMinChanged
        AddHandler SliderHeightMax.ValueChanged, AddressOf OnHeightMaxChanged
        ' Re-fit once the button comes up: EnsureHeightTrackCovers defers while a mouse button is held, so a
        ' value pushed out of track mid-drag would otherwise keep its thumb pinned until the next edit.
        ' NOT OnSliderDragEnded — that one kicks a render reload, and height changes nothing on screen.
        AddHandler SliderHeightMin.DragEnded, AddressOf OnHeightDragEnded
        AddHandler SliderHeightMax.DragEnded, AddressOf OnHeightDragEnded
    End Sub

    Private Sub OnHeightDragEnded(sender As Object, e As EventArgs)
        EnsureHeightTrackCovers(SliderHeightMin.Value, SliderHeightMax.Value)
    End Sub

    ''' <summary>The Creation Kit's hard bound on a height field. Separate from the sliders' 20%..200% track:
    ''' the track is the useful drag range, this is the limit no value may cross.</summary>
    Private Shared Function ClampHeight(v As Double) As Double
        If Double.IsNaN(v) Then Return 1.0
        Return Math.Max(HeightHardMin, Math.Min(HeightHardMax, v))
    End Function

    ''' <summary>Widen BOTH height tracks to the hard limit as soon as either value falls outside the nominal
    ''' 20%..200%. A value outside its track draws its thumb pinned to a rail, and TinySliderTextBox assigns
    ''' XToValue(e.X) on mouse-DOWN — which can only return an in-track number — so a single click on that
    ''' pinned thumb (even just to focus it) would collapse the value and drag the sibling with it. The value
    ''' would survive the wheel and the arrow keys and die to one click.
    ''' Called at seed time AND from both ValueChanged handlers: with AllowExtremeValues the textbox can put an
    ''' out-of-track number in at any moment, so a one-shot check at seed time only covers half the problem.
    ''' Both sliders move together so the Min/Max pair always shares one scale — otherwise the same number
    ''' would sit at two different thumb positions and the cross-clamp would read as broken.
    ''' ⛔ Safe to call from inside a handler and needs no _heightSyncing guard: with AllowExtremeValues the
    ''' Minimum/Maximum setters skip their re-clamp (TinySliderTextBox.vb), so they never assign Value and
    ''' never raise ValueChanged.
    ''' MONOTONE, not self-fitting: it widens once and never narrows back. Re-narrowing mid-session would
    ''' shift every thumb under the user and could re-pin, so "type 500% then retype 100%" correctly leaves
    ''' the wide track in place for the rest of the dialog.</summary>
    Private Sub EnsureHeightTrackCovers(vMin As Double, vMax As Double)
        If vMin >= HeightTrackMin AndAlso vMin <= HeightTrackMax AndAlso
           vMax >= HeightTrackMin AndAlso vMax <= HeightTrackMax Then Return
        ' ⛔ Never re-scale while a mouse button is held. A drag keeps focus AND capture, so a wheel notch or
        ' arrow key mid-drag can push the value out of track; widening right then moves the thumb out from
        ' under the pointer, and the next mouse-move — which is what a drag IS — re-reads the pointer and
        ' leaps the value (201% to 1000% in the reported trace). Deferring keeps the drag authoritative; the
        ' DragEnded handler re-fits the moment the button comes up (it fires on every MouseUp that had a
        ' MouseDown, so a plain click re-fits too).
        ' ⛔ Test the sliders' own Capture, NOT Control.MouseButtons. TinySliderTextBox takes capture in
        ' OnMouseDown BEFORE it assigns the clicked value, so this is exactly "a height slider is mid-drag".
        ' A global "any button is down" looks equivalent and is not: the textbox commits through Validating,
        ' and WinForms raises Validating synchronously from the WM_LBUTTONDOWN of the click on the NEXT
        ' control — so the ordinary "type a value, then click away" flow runs with a button physically down.
        ' That suppressed the widen, left the thumb pinned with no DragEnded to follow (the slider never
        ' dragged), and the next click on it collapsed the value on button-DOWN, before the re-fit on
        ' button-UP could help. Capture belongs to whatever the user clicked, so that flow widens normally.
        If SliderHeightMin.Capture OrElse SliderHeightMax.Capture Then Return
        SliderHeightMin.Minimum = HeightHardMin : SliderHeightMin.Maximum = HeightHardMax
        SliderHeightMax.Minimum = HeightHardMin : SliderHeightMax.Maximum = HeightHardMax
    End Sub

    ''' <summary>Snap a slider back inside [0.1, 10] if a typed value escaped it (AllowExtremeValues means the
    ''' control itself won't). Assigns under <see cref="_heightSyncing"/>, so the re-entrant ValueChanged is
    ''' swallowed and the CALLER must carry on with the corrected value — bailing out here instead would skip
    ''' the cross-clamp, since the re-entrant pass returns at its own guard.</summary>
    Private Sub EnforceHeightHardLimit(slider As FO4_Base_Library.TinySliderTextBox)
        Dim clamped As Double = ClampHeight(slider.Value)
        If Math.Abs(clamped - slider.Value) <= 0.0000005 Then Return
        Dim prevSync As Boolean = _heightSyncing
        _heightSyncing = True
        Try
            slider.Value = clamped
        Finally
            _heightSyncing = prevSync
        End Try
    End Sub

    ''' <summary>Cross-clamp so Min never exceeds Max. Not cosmetic: no vanilla record has Min &gt; Max
    ''' (0 inverted across 8990 FO4 NPC_), so an inverted pair would be data this app invented.
    ''' ⛔ The sibling is only pushed when it is ALREADY AUTHORED (present in the record, or moved by the user
    ''' in this session). Pushing an absent sibling would either mint a subrecord nobody asked for, or — since
    ''' the write gate refuses to author it — move the slider on screen while the file keeps the old value.
    ''' No-op under SSE, where there is no NAM4 at all and the Max slider is hidden.</summary>
    Private Sub OnHeightMinChanged(sender As Object, e As EventArgs)
        If _heightSyncing Then Return
        _heightMinUserMoved = True
        ' AllowExtremeValues means the control accepts anything typed into its box, so the CK limit has to be
        ' enforced here as well as on commit — otherwise the box would read 5000% while the plugin gets 1000%.
        EnforceHeightHardLimit(SliderHeightMin)
        ' Re-fit the tracks: a typed value can land outside them at any moment, not just at seed time.
        EnsureHeightTrackCovers(SliderHeightMin.Value, SliderHeightMax.Value)
        If _isSSE Then Return
        If Not (_hadHeightMax OrElse _heightMaxUserMoved) Then Return
        If SliderHeightMax.Value >= SliderHeightMin.Value Then Return
        Dim prevSync As Boolean = _heightSyncing
        _heightSyncing = True
        Try
            SliderHeightMax.Value = SliderHeightMin.Value
        Finally
            _heightSyncing = prevSync
        End Try
    End Sub

    Private Sub OnHeightMaxChanged(sender As Object, e As EventArgs)
        If _heightSyncing Then Return
        _heightMaxUserMoved = True
        EnforceHeightHardLimit(SliderHeightMax)
        EnsureHeightTrackCovers(SliderHeightMin.Value, SliderHeightMax.Value)
        If _isSSE Then Return
        If Not (_hadHeightMin OrElse _heightMinUserMoved) Then Return
        If SliderHeightMin.Value <= SliderHeightMax.Value Then Return
        Dim prevSync As Boolean = _heightSyncing
        _heightSyncing = True
        Try
            SliderHeightMin.Value = SliderHeightMax.Value
        Finally
            _heightSyncing = prevSync
        End Try
    End Sub

    ''' <summary>Persist a Height edit as an <see cref="NpcRecordOverride"/> on the ROOT NPC, MERGING into any
    ''' override a previous session (or the NPC Editor) already authored. Only the sliders that actually moved
    ''' are written, so an untouched NAM6/NAM4 round-trips verbatim — bytes stay the user's call.
    ''' ⛔ TraitsChanged is latched because Height lives in the TRAITS template category
    ''' (NpcTemplateMaterializer.MaterializeTraits copies NAM6/NAM4 unconditionally): without it, a
    ''' Traits-INHERITING NPC keeps its Use-Traits flag and the engine's CopyFromTemplate overwrites the
    ''' edited height at runtime, so the save would look fine in xEdit and do nothing in game.</summary>
    Private Sub RegisterHeightOverride()
        If _mainForm Is Nothing Then Return
        ' Quantise to what the box actually shows: the slider reports an unquantised drag position, so without
        ' this a drag would store 1.0240876 while the user read 1.02.
        ' ClampHeight again on the way out: the sliders run with AllowExtremeValues, so the typed path can put
        ' any number in them and nothing else would stop a 5000% from reaching the plugin.
        Dim mn As Double = Math.Round(ClampHeight(SliderHeightMin.Value), HeightDecimals)
        Dim mx As Double = Math.Round(ClampHeight(SliderHeightMax.Value), HeightDecimals)
        Dim snapMn As Double = Math.Round(_snapHeightMin, HeightDecimals)
        Dim snapMx As Double = Math.Round(_snapHeightMax, HeightDecimals)

        ' A field is only written when we are ENTITLED to author it:
        '   - the user edited that slider directly, or
        '   - the subrecord already existed AND its seed was not clamped.
        ' The first clause is what stops the cross-clamp from MINTING a subrecord that was absent; the second
        ' is what stops it from replacing a value the record held outside [0.1, 10] — ClampHeight had to move
        ' that one to seed the slider, so the screen never showed it, and rewriting a number the user was
        ' never shown is not ours to do.
        ' ⛔ There is deliberately NO "complete the half-written pair" rule here. An earlier revision authored
        ' both NAM6 and NAM4 whenever either moved on a half pair, justified by a claim that the engine lerps
        ' against an uninitialised zero. That claim was NOT measured — TESNPC::GetHeight does read both fields
        ' (+0x304 / +0x308) and lerp, but what the struct holds when the subrecord is ABSENT was never checked,
        ' and the "zero" in that reasoning is this app's own parser default (NPC_Data.HeightMax has no
        ' initialiser), not the engine's. It also minted bytes the user never asked for, and the case does not
        ' occur: all 8990 NPC_ across the 69 plugins of the load order carry both subrecords.
        ' ⚠️ The SeedClamped clauses are defensive: today InitHeightSection disables BOTH sliders when either
        ' seed was clamped, so in any state where a slider can move both flags are already False. Keep them —
        ' they are what stops BUG "inverted pair on disk" from reopening if that read-only gate is ever
        ' narrowed. With them, `mayWrite*` reduces to the same condition the cross-clamp uses for the sibling,
        ' which is why the clamp pushes a sibling if and only if that sibling is writable: "slider moved on
        ' screen but the file kept the old value" is unreachable by construction.
        Dim mayWriteMin As Boolean = _heightMinUserMoved OrElse (_hadHeightMin AndAlso Not _heightMinSeedClamped)
        Dim mayWriteMax As Boolean = _heightMaxUserMoved OrElse (_hadHeightMax AndAlso Not _heightMaxSeedClamped)
        Dim minChanged As Boolean = mayWriteMin AndAlso mn <> snapMn
        ' NAM4 does not exist in Skyrim — the SSE path never authors it, whatever the hidden slider holds.
        Dim maxChanged As Boolean = (Not _isSSE) AndAlso mayWriteMax AndAlso mx <> snapMx
        If Not (minChanged OrElse maxChanged) Then Return

        Dim ov = _mainForm.TryGetNpcRecordOverride(_rootNpcFormID)
        If ov Is Nothing Then ov = New NpcRecordOverride()
        If minChanged Then ov.HeightMin = CSng(mn)
        If maxChanged Then ov.HeightMax = CSng(mx)
        ov.TraitsChanged = True
        _mainForm.SetNpcRecordOverride(_rootNpcFormID, ov)
    End Sub

    ''' <summary>Restore the Height sliders to their open-time state. Called from the Body tab's
    ''' "Reset Section" — without it the group would visually reset with everything else while
    ''' <see cref="RegisterHeightOverride"/> still committed the discarded edit on OK.</summary>
    Private Sub ResetHeightSection()
        Dim prevSync As Boolean = _heightSyncing
        _heightSyncing = True
        Try
            SliderHeightMin.Value = _snapHeightMin
            SliderHeightMax.Value = _snapHeightMax
        Finally
            _heightSyncing = prevSync
        End Try
        _heightMinUserMoved = False
        _heightMaxUserMoved = False
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' Height is the one field here that is NOT carried by the live LooksMenu overlay — commit it now,
        ' before the dialog result is set, so a Cancel/X path (which only rolls back the overlay) writes nothing.
        RegisterHeightOverride()
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
        ' principal no lo dibuja nunca. El checkbox "Preview magic" es la fuente de verdad (se LEE de él en vez de
        ' asumir True, así que si algún día se persiste su estado, esto lo sigue solo).
        ' ⛔ Y sin checkbox ⇒ FALSE, no True: el checkbox sólo se construye en SSE, así que bajo FO4 el `Is Nothing
        ' OrElse` que había acá arrancaba el host de los editores en True — el default INVERTIDO del que
        ' NpcRenderHost documenta lo contrario. Hoy sería inerte (el camino magic está gateado por juego), y por eso
        ' mismo es la clase de default que nadie descubre hasta que otro lector consulta el flag.
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
            ' El gate del tab de skin tint necesita el state del primer render (de ahi sale si el tono del
            ' cuerpo se deriva o no), asi que se evalua ACA y no en el .ctor.
            SkinTintPanelBody.OnPreviewReady()
        End If
    End Sub

    ''' <summary>Re-intersect the node list against the actor's skeleton now that
    ''' <c>_editorHost.LastSkeletonInstance</c> exists (the tab was first populated in the .ctor, before the
    ''' preview host). Pure repopulation now that the tab lives in the Designer (00-reglas-ui-y-vb §1): no
    ''' <c>TabPages.Remove</c>, no nulling ListBoxSseNodes/TextBoxSseNodeFilter, no <c>SelectedTab</c> restore —
    ''' those existed only to tear down and rebuild a code-instantiated TabPage that is no longer built by hand.
    ''' ⚠ Behaviour change: the filter TEXT now SURVIVES this rebuild (before, the TextBox itself was recreated
    ''' empty on every call — losing whatever the user had typed). Surviving is the correct behaviour; it just
    ''' wasn't what shipped.</summary>
    Private Sub RebuildSseBodyScaleTab()
        PopulateSseBodyScaleTab()
    End Sub

    Private Sub EditBodyForm_FormClosing(sender As Object, e As System.Windows.Forms.FormClosingEventArgs) Handles Me.FormClosing
        ' ⭐ Rollback ANTES del teardown y para CUALQUIER cierre que no sea OK: botón Cancel, X, Esc y
        ' Alt+F4 (WinForms pone DialogResult=Cancel al cerrar un modal con la X, así que este único test
        ' cubre las cuatro vías). Mismo diseño que ArmoEditor_Form.vb:1677 y EditFace_Form.
        If DialogResult <> DialogResult.OK Then RevertOverlay()

        ' El tab de skin tint desarma su picker y suelta sus dos Bitmaps ACA: su Dispose corre despues del
        ' teardown del preview y el picker tiene que apagarse mientras el PreviewControl sigue vivo.
        If SkinTintPanelBody IsNot Nothing Then SkinTintPanelBody.OnHostClosing()

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
