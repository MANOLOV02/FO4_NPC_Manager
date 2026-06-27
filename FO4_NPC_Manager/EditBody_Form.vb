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
    Private ReadOnly _bodySlideBars As New Dictionary(Of String, FO4_Base_Library.TinySliderTextBox)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _bodySlideRows As New Dictionary(Of String, Control)(StringComparer.OrdinalIgnoreCase)

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
        SeedOverlayFromInitial(p, initial)


        ApplyAvailability(hasMwgt, hasMrsv, _availableSliders.Count > 0)

        If hasMrsv Then CreateMrsvRows()
        CreateBodySlideRows()
        PopulateSkinCombos()

        AddHandler WeightTriangle.WeightChanged, AddressOf OnWeightTriangleChanged
        AddHandler ButtonOk.Click, AddressOf OnOk
        AddHandler ButtonCancel.Click, AddressOf OnCancel
        AddHandler ButtonResetSection.Click, AddressOf OnResetSection
        AddHandler TextBoxBodySlideFilter.TextChanged, AddressOf OnBodySlideFilterChanged
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

            ' LM Skin template combo
            ComboBoxLmSkinTemplate.Items.Clear()
            _lmTemplateComboIds.Clear()
            ComboBoxLmSkinTemplate.Items.Add("(none)")
            _lmTemplateComboIds.Add("")
            For Each tpl In _mainForm.GetLmSkinTemplateCandidates(_npcIsFemale)
                ComboBoxLmSkinTemplate.Items.Add(tpl.DisplayName)
                _lmTemplateComboIds.Add(tpl.Id)
            Next

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
    ''' as authoritative even when empty".</summary>
    Private Shared Sub SeedOverlayFromInitial(p As LooksmenuLoader.LooksmenuPreset, initial As InitialValues)
        If initial Is Nothing Then Return
        If Not p.WeightThin.HasValue Then p.WeightThin = initial.Thin
        If Not p.WeightMuscular.HasValue Then p.WeightMuscular = initial.Muscular
        If Not p.WeightFat.HasValue Then p.WeightFat = initial.Fat
        If p.BodyMorphValues.Count = 0 AndAlso initial.Mrsv IsNot Nothing Then
            ' Always carry exactly 5 slots (vanilla MRSV layout), zero-padding if needed.
            For i = 0 To 4
                p.BodyMorphValues.Add(If(i < initial.Mrsv.Length, initial.Mrsv(i), 0.0F))
            Next
        End If
        p.HasBodyMorphValues = True
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
                Dim row As New TableLayoutPanel() With {
                    .ColumnCount = 2,
                    .RowCount = 1,
                    .AutoSize = True,
                    .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    .Width = rowWidth,
                    .Margin = New Padding(0, 0, 0, 2)
                }
                row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 180))
                row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
                row.RowStyles.Add(New RowStyle(SizeType.AutoSize))

                Dim lbl As New Label() With {
                    .Text = sliderName,
                    .AutoSize = False,
                    .Width = 180,
                    .TextAlign = ContentAlignment.MiddleLeft,
                    .Anchor = AnchorStyles.Left Or AnchorStyles.Right
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
    ''' Cheap: one Width assignment per row, no relayout cascade because rows are AutoSize on
    ''' height only.</summary>
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
        End If
    End Sub

    ''' <summary>Revert MWGT (Weight triangle + 3 sliders), MRSV (5 region bars), and Skin combos
    ''' (NPC.WNAM + LM template) to the snapshot taken at form-open time. The combos go back to
    ''' whatever the prior overlay carried, falling back to "(use RACE default)" / "(none)" when
    ''' the user opened Edit Body on an NPC with no prior overlay.</summary>
    Private Async Function ResetBodySection() As Task
        Dim p = Preset
        _suspendEvents = True
        Try
            ' MWGT — back to initial values seeded from the live NPC at open time.
            If _initialSeed IsNot Nothing Then
                p.WeightThin = _initialSeed.Thin
                p.WeightMuscular = _initialSeed.Muscular
                p.WeightFat = _initialSeed.Fat
                WeightTriangle.SetWeights(_initialSeed.Thin, _initialSeed.Muscular, _initialSeed.Fat)
                SyncMwgtSliders(_initialSeed.Thin, _initialSeed.Muscular, _initialSeed.Fat)
            End If

            ' MRSV — back to initial 5-region values. ApplyPresetOverlayToNpcData reads
            ' BodyMorphValues positionally, so we keep exactly 5 entries.
            p.BodyMorphValues.Clear()
            For i = 0 To 4
                Dim v As Single = If(_initialSeed IsNot Nothing AndAlso _initialSeed.Mrsv IsNot Nothing AndAlso i < _initialSeed.Mrsv.Length, _initialSeed.Mrsv(i), 0.0F)
                p.BodyMorphValues.Add(v)
                If i < _mrsvBars.Length AndAlso _mrsvBars(i) IsNot Nothing Then _mrsvBars(i).Value = v
            Next
            p.HasBodyMorphValues = True

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
        Return If(String.IsNullOrEmpty(ov.TemplateId), "(unknown)", ov.TemplateId)
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
        RefreshAvailableList()
    End Sub

    ''' <summary>Add the selected available template to the TOP of the applied list (index 0 = on
    ''' top), unless its TemplateId is already applied (no duplicates). Renumber + full reload.</summary>
    Private Async Sub OnOverlayAdd(sender As Object, e As EventArgs)
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
                SliderOverlayScaleU.Value = 1R
                SliderOverlayScaleV.Value = 1R
                CheckBoxOverlayTint.Checked = False
                SliderOverlayTintAlpha.Value = 1R
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
                SliderOverlayTintAlpha.Value = 1R
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
            Return $"{id} — template not installed for this gender (properties disabled)"
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

    ''' <summary>Alpha slider edit → rewrite Tint preserving RGB, throttled.</summary>
    Private Sub OnOverlayTintAlphaChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
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

    Private Sub OnCancel(sender As Object, e As EventArgs)
        ' Restore the snapshot taken when the form opened.
        If _hadPriorOverlay Then
            _appliedPresets(_rootNpcFormID) = _priorPreset
        Else
            _appliedPresets.Remove(_rootNpcFormID)
        End If
        HasUncommittedChanges = False
        DialogResult = DialogResult.Cancel
        Close()
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
        ' Face tint deferral is now handled by the library's PostTextureUploadAction hook on
        ' RenderIntent — wired by RenderCurrentStateAsync inside the render dispatch path so
        ' editor hosts get the same generic post-texture sequencing the MainForm uses.

        If _mainForm IsNot Nothing Then
            Try
                Await _mainForm.RenderInHostAsync(_editorHost, _rootNpcFormID)
            Catch ex As Exception
                Logger.LogLazy(Function() $"[EDIT-BODY] initial render failed for NPC 0x{_rootNpcFormID:X8}: {ex.GetType().Name}: {ex.Message}")
            End Try
        End If
    End Sub

    Private Sub EditBodyForm_FormClosing(sender As Object, e As System.Windows.Forms.FormClosingEventArgs) Handles Me.FormClosing
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
