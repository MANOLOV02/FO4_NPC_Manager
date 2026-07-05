Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports FO4_Base_Library

''' <summary>Standalone editor to author a single Armor Addon (ARMA) record — its models, biped slots,
''' race/skin/material-swap and per-bone sculpt. ARMA-centric (the ARMO that equips it comes in a later
''' task): the form mutates the MainForm ARMA/MSWP draft lists only; the existing Save ESP flow persists
''' the transitive closure. Once a draft exists the draft-aware render resolver shows it.
'''
''' Flow: a <b>Template</b> button loads an existing ARMA (real record OR an ARMA draft) into the panels; a
''' <b>New</b>/<b>Override</b> mode radio decides whether Apply commits to a brand-new draft (New) or an
''' override of the loaded REAL record (Override, only enabled for a real template). If an <c>editDraft</c>
''' is passed to the constructor it's loaded directly (edit an existing draft). <b>Apply</b> commits the
''' panel state into the draft and registers it; <b>Close</b> ends.
'''
''' Layout mirrors <see cref="OutfitPicker_Form"/>: a SplitContainer with the field tabs on the left and a
''' dedicated preview panel hosting an <see cref="NpcRenderHost"/> on the right. The WYSIWYG preview wraps
''' the ARMA draft in a throwaway ARMO draft (single addon INDX 0) referenced by a throwaway
''' <see cref="OutfitDraft"/> at <see cref="OutfitDraft.PreviewDraftFormID"/>, then renders it equipped on
''' the current NPC via <see cref="MainForm.PreviewOutfitInHostAsync"/>. Re-preview is debounced.</summary>
Public Class ArmaEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _previewNpcFormID As UInteger
    Private ReadOnly _raceFormID As UInteger
    Private ReadOnly _isFemale As Boolean

    ''' <summary>The ARMA draft currently being edited (the authoring model). Always non-Nothing after the
    ''' constructor: either the passed-in editDraft, or a fresh empty draft seeded with the preview race.</summary>
    Private _draft As ArmaDraft
    ''' <summary>Suppresses preview re-render + dirty marking while panels are being LOADED programmatically.</summary>
    Private _loading As Boolean

    ''' <summary>The fixed type prefix for this editor's stored base EditorID, driven through the shared
    ''' <see cref="EditorIdField"/> helper (NEW = editable name under this prefix; OVERRIDE = kept verbatim).</summary>
    Private ReadOnly _edidPrefix As String = ArmaDraft.EditorIdPrefix

    ''' <summary>Per-gender absolute sculpt rows held in memory while editing, keyed by gender (0=Male,1=Female).
    ''' The visible <see cref="SculptPanel"/> shows ONE gender; switching gender saves the slider rows into here
    ''' and reloads the other. Values are ABSOLUTE (1.0 = unchanged) — converted to/from BSMS deltas on load/Apply.</summary>
    Private ReadOnly _sculptByGender As New Dictionary(Of UInteger, List(Of SclpFile.SclpBoneAbsolute)) From {
        {0UI, New List(Of SclpFile.SclpBoneAbsolute)}, {1UI, New List(Of SclpFile.SclpBoneAbsolute)}}
    ''' <summary>The gender the sculpt panel is currently SHOWING (so a gender switch can flush it first).</summary>
    Private _sculptShownGender As UInteger = 0UI

    ''' <summary>The live slider rows currently shown in <see cref="SculptPanel"/> (one per bone for the shown
    ''' gender). Read back by <see cref="SaveSculptGrid"/>; rebuilt by <see cref="LoadSculptGrid"/>.</summary>
    Private ReadOnly _sculptRows As New List(Of SculptRow)

    ''' <summary>A single bone row in the sculpt panel: the row container plus its name textbox and the 3 absolute
    ''' per-axis sliders. Mirrors the EditBody BodySlide row pattern (TableLayoutPanel + TinySliderTextBox).</summary>
    Private NotInheritable Class SculptRow
        Public Container As TableLayoutPanel
        Public NameBox As TextBox
        Public SliderX As TinySliderTextBox
        Public SliderY As TinySliderTextBox
        Public SliderZ As TinySliderTextBox
    End Class

    ''' <summary>The biped slots laid out as granular checkboxes (FO4 BOD2 bit = slot − 30), built by the
    ''' shared <see cref="BipedSlotCheckboxes"/> helper into the Designer-declared <see cref="FlowSlots"/>.
    ''' Maps slot number → CheckBox.</summary>
    Private _slotChecks As Dictionary(Of Integer, CheckBox)

    ' === preview (mirror OutfitPicker / the deleted v1 host setup) ===
    Private _preview As PreviewControl
    Private _host As NpcRenderHost
    Private _previewDraftRegistered As Boolean
    Private _previewArmaWrapperRegistered As Boolean
    Private _lastPreviewKey As String = Nothing
    Private _previewInProgress As Boolean
    Private _pendingApply As Boolean
    Private WithEvents _previewDebounce As Timer

    ''' <summary>The FormID of the draft this editor committed on a successful (validated) Apply — provisional
    ''' sentinel for New, the real global FormID for Override. 0 until the first successful Apply (so a caller
    ''' that opened this editor on an addon row can tell whether to rewire the row to a new draft).</summary>
    Public ReadOnly Property ResultArmaFormID As UInteger
        Get
            Return _resultArmaFormID
        End Get
    End Property
    Private _resultArmaFormID As UInteger = 0UI

    ''' <summary>Snapshot of the draft taken at open (after load/create + panel populate). On Cancel, editing an
    ''' EXISTING/override draft re-registers this snapshot to revert the live-commit mutations. See <see cref="OnCancel"/>.</summary>
    Private _openSnapshot As ArmaDraft
    ''' <summary>True when the draft was created fresh in THIS editor (not passed in as editDraft, not an override
    ''' of an existing registered record) — i.e. <see cref="ArmaDraft.IsNew"/> at open. On Cancel a brand-new draft
    ''' is UNregistered (discarded); an existing/override draft is reverted from <see cref="_openSnapshot"/>.</summary>
    Private _draftWasNew As Boolean

    ''' <summary>Throwaway ARMO wrapper FormID for previewing a STANDALONE ARMA draft — a draft sentinel just
    ''' below the OTFT preview sentinel (<see cref="OutfitDraft.PreviewDraftFormID"/>) so the resolver picks
    ''' it up but it's never persisted (filtered out of the save set).</summary>
    Private Const PreviewArmoWrapperFormID As UInteger = &HFF0007FEUI

    ''' <param name="mainForm">Owner — supplies the draft registrars, the PluginManager for the FormID pickers,
    ''' parsed-record access and the WYSIWYG preview host.</param>
    ''' <param name="previewNpcFormID">The currently-selected NPC for preview context. 0 = no preview.</param>
    ''' <param name="raceFormID">The preview NPC's race (pre-fills a new ARMA's RNAM).</param>
    ''' <param name="isFemale">The preview NPC's gender (drives which mesh/priority is previewed).</param>
    ''' <param name="editDraft">When supplied, edit this existing ARMA draft directly (skip the empty seed).</param>
    ''' <param name="initialTemplateArmaFormID">When nonzero AND no <paramref name="editDraft"/> is given,
    ''' pre-load this REAL ARMA as the template on open (same as the user clicking Template then picking it) —
    ''' used by the ARMO editor's Addons tab to "edit a real addon as New".</param>
    Public Sub New(mainForm As MainForm, previewNpcFormID As UInteger, raceFormID As UInteger, isFemale As Boolean,
                   Optional editDraft As ArmaDraft = Nothing, Optional initialTemplateArmaFormID As UInteger = 0UI,
                   Optional templateAsOverride As Boolean = True)
        InitializeComponent()
        _mainForm = mainForm
        _previewNpcFormID = previewNpcFormID
        _raceFormID = raceFormID
        _isFemale = isFemale

        BuildSlotCheckBoxes()
        SeedSculptBoneCombo()

        _previewDebounce = New Timer() With {.Interval = 400}

        ' Top bar — explicit intent actions (xEdit model: New / New-from-template copy / Override / Edit draft).
        AddHandler ButtonNewBlank.Click, AddressOf OnActionNewBlank
        AddHandler ButtonNewFromTemplate.Click, AddressOf OnActionNewFromTemplate
        AddHandler ButtonOverrideExisting.Click, AddressOf OnActionOverrideExisting
        AddHandler ButtonEditDraft.Click, AddressOf OnActionEditDraft
        AddHandler TextBoxEdid.TextChanged, AddressOf OnEdidChanged

        ' Models tab.
        AddHandler ButtonBrowseMod2.Click, Sub() BrowseMeshInto(TextBoxMod2)
        AddHandler ButtonBrowseMod3.Click, Sub() BrowseMeshInto(TextBoxMod3)
        AddHandler ButtonBrowseMod4.Click, Sub() BrowseMeshInto(TextBoxMod4)
        AddHandler ButtonBrowseMod5.Click, Sub() BrowseMeshInto(TextBoxMod5)

        ' Skin & Material tab FormID pickers.
        AddHandler ButtonPickRace.Click, AddressOf OnPickRace
        AddHandler ButtonPickSndd.Click, Sub() PickFidInto(TextBoxSndd, {"FSTS"}, "Select Footstep (FSTS)", allowNull:=True)
        AddHandler ButtonAddRace.Click, AddressOf OnAddRace
        AddHandler ButtonRemoveRace.Click, AddressOf OnRemoveRace
        AddHandler ButtonPickNam0.Click, Sub() PickTxstInto(TextBoxNam0)
        AddHandler ButtonPickNam1.Click, Sub() PickTxstInto(TextBoxNam1)
        AddHandler ButtonPickNam2.Click, Sub() PickFlstInto(TextBoxNam2)
        AddHandler ButtonPickNam3.Click, Sub() PickFlstInto(TextBoxNam3)
        AddHandler ButtonPickMo2s.Click, Sub() PickMswpInto(TextBoxMo2s)
        AddHandler ButtonPickMo3s.Click, Sub() PickMswpInto(TextBoxMo3s)
        AddHandler ButtonEditMo2s.Click, Sub() OnNewEditMswp(isFemaleGender:=False)
        AddHandler ButtonEditMo3s.Click, Sub() OnNewEditMswp(isFemaleGender:=True)
        AddHandler ButtonPickOnam.Click, Sub() PickFidInto(TextBoxOnam, {"ARTO"}, "Select Art Object (ONAM)", allowNull:=True)
        AddHandler ButtonPickMo4s.Click, Sub() PickMswpInto(TextBoxMo4s)
        AddHandler ButtonPickMo5s.Click, Sub() PickMswpInto(TextBoxMo5s)

        ' Sculpt tab.
        AddHandler RadioSculptMale.CheckedChanged, AddressOf OnSculptGenderChanged
        AddHandler RadioSculptFemale.CheckedChanged, AddressOf OnSculptGenderChanged
        AddHandler ButtonSculptAddRow.Click, AddressOf OnSculptAddRow
        AddHandler ButtonSculptLoad.Click, AddressOf OnSculptLoad
        AddHandler ButtonSculptEstimate.Click, AddressOf OnSculptEstimate
        AddHandler ButtonSculptSave.Click, AddressOf OnSculptSave

        ' Bottom (OK finalizes + validates + closes; Cancel discards the live-commit mutations + closes).
        AddHandler ButtonOk.Click, AddressOf OnOk
        AddHandler ButtonCancel.Click, AddressOf OnCancel

        ' Re-preview (debounced) on edits to render-relevant fields.
        AddHandler TextBoxMod2.TextChanged, AddressOf OnFieldEdited
        AddHandler TextBoxMod3.TextChanged, AddressOf OnFieldEdited
        AddHandler TextBoxMo2s.TextChanged, AddressOf OnFieldEdited
        AddHandler TextBoxMo3s.TextChanged, AddressOf OnFieldEdited

        ' Seed the draft: edit the passed one, else a fresh empty draft (NEW) seeded with the preview race.
        If editDraft IsNot Nothing Then
            _draft = editDraft
            _editingExistingDraft = True   ' continuing to edit one of the user's registered drafts
            LoadDraftIntoPanels()
        Else
            _draft = New ArmaDraft With {.FormID = _mainForm.AllocateDraftFormID(),
                                         .EditorID = ArmaDraft.EditorIdPrefix & "new",
                                         .RaceFormID = _raceFormID, .IsNew = True}
            LoadDraftIntoPanels()
            ' Optional pre-load of a real ARMA template — defaults to OVERRIDE (edit the existing ARMA; your
            ' plugin replaces it), the usual intent when editing an existing addon. Use the in-editor
            ' "New from template…" action for a copy-as-new instead.
            If initialTemplateArmaFormID <> 0UI Then LoadRealArmaTemplate(initialTemplateArmaFormID, asOverride:=templateAsOverride)
        End If
        UpdateStatusBanner()

        LabelPreviewHint.Text = If(_previewNpcFormID = 0UI,
                                   "Select an NPC in the main window to preview.",
                                   "Preview: this ARMA equipped on the current NPC.")

        ' Snapshot the draft AS OPENED (pre-edit state) so a not-OK'd close can revert the live-commit mutations
        ' that the debounced preview writes back into the registered draft. Remember whether it's brand-new.
        SnapshotCurrentDraft()
    End Sub

    ' =====================================================================
    ' One-time UI construction in code-behind (Designer rule: many repeated
    ' controls + variable grid columns are added to their containers here).
    ' =====================================================================

    Private Sub BuildSlotCheckBoxes()
        _slotChecks = BipedSlotCheckboxes.Build(FlowSlots, AddressOf OnFieldEdited)
    End Sub

    ''' <summary>Seed the "Add bone…" combo with the common vanilla sculpt bone names (DropDown style → the
    ''' user picks a known bone or free-types one). The list is a convenience, NOT a whitelist — any typed name
    ''' is accepted.</summary>
    Private Sub SeedSculptBoneCombo()
        ComboSculptAddBone.Items.Clear()
        ComboSculptAddBone.Items.AddRange(CommonSculptBones)
    End Sub

    ''' <summary>Common vanilla CBBE/body sculpt bones (the "_skin" virtual scale bones used by BSMS sculpt
    ''' data). Seeds the Add-bone combo; free-text still allowed.</summary>
    Private Shared ReadOnly CommonSculptBones As String() = {
        "Breast_skin", "LBreast_skin", "RBreast_skin",
        "ButtFat_skin", "LButtFat_skin", "RButtFat_skin",
        "Belly_skin", "Pelvis_skin", "Pelvis_Rear_skin", "Spine1_Rear_skin",
        "LLeg_Thigh_skin", "RLeg_Thigh_skin",
        "LArm_ForeArm1_skin", "RArm_ForeArm1_skin", "ShoulderFat_skin"}

    ' =====================================================================
    ' Explicit intent actions (xEdit model) + status banner
    ' =====================================================================

    ''' <summary>The real ARMA FormID/EditorID an Override / New-from-template copy descends from — the SOURCE
    ''' record. Kept for the banner's source-plugin line and the Override EditorID-change check on Apply.</summary>
    Private _templateRealFormID As UInteger = 0UI
    Private _templateRealEditorID As String = ""

    ''' <summary>True while this editor is continuing to edit one of the user's ALREADY-registered drafts (via
    ''' the editDraft ctor arg or the "Edit draft…" action) — drives the "Editing draft" banner wording, distinct
    ''' from a brand-new record.</summary>
    Private _editingExistingDraft As Boolean = False

    ''' <summary>"New (blank)" → a fresh empty New-record draft (new draft FormID, npcm_ prefix, IsNew=True,
    ''' IsOverride=False), seeded with the preview race. xEdit's "new record" from scratch.</summary>
    Private Sub OnActionNewBlank(sender As Object, e As EventArgs)
        RevertOrDiscardCurrentDraft()
        _draft = New ArmaDraft With {.FormID = _mainForm.AllocateDraftFormID(),
                                     .EditorID = ArmaDraft.EditorIdPrefix & "new",
                                     .RaceFormID = _raceFormID, .IsNew = True}
        _templateRealFormID = 0UI
        _templateRealEditorID = ""
        _editingExistingDraft = False
        LoadDraftIntoPanels()
        SnapshotCurrentDraft()
        UpdateStatusBanner()
        RequestPreview()
    End Sub

    ''' <summary>"New from template…" → pick a REAL ARMA and COPY it into a NEW record (fresh draft FormID,
    ''' IsOverride=False) — xEdit "copy as new record into". Race-filtered like the old Template picker.</summary>
    Private Sub OnActionNewFromTemplate(sender As Object, e As EventArgs)
        Dim fid = PickRealArma("Copy ARMA into a NEW record")
        If fid = 0UI Then Return
        RevertOrDiscardCurrentDraft()
        If Not LoadRealArmaTemplate(fid, asOverride:=False) Then
            MessageBox.Show(Me, "Could not parse that ARMA record.", "New from template",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If
    End Sub

    ''' <summary>"Override existing…" → pick a REAL ARMA and edit it as an OVERRIDE (keep its global FormID +
    ''' EditorID, IsOverride=True) — xEdit "copy as override into"; your plugin replaces that record on Save.</summary>
    Private Sub OnActionOverrideExisting(sender As Object, e As EventArgs)
        Dim fid = PickRealArma("Override an existing ARMA")
        If fid = 0UI Then Return
        RevertOrDiscardCurrentDraft()
        If Not LoadRealArmaTemplate(fid, asOverride:=True) Then
            MessageBox.Show(Me, "Could not parse that ARMA record.", "Override existing",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If
    End Sub

    ''' <summary>"Edit draft…" → pick one of the user's existing ARMA drafts (shown with "(new)") and continue
    ''' editing it directly (no re-key). No-op with a message when there are no drafts yet.</summary>
    Private Sub OnActionEditDraft(sender As Object, e As EventArgs)
        ' List the user's unsaved ARMA drafts AND their already-SAVED authored ARMA records (EDID npcm_).
        Dim entries = _mainForm.ArmaDrafts().Select(Function(d) New FormIdPickerEntry With {
            .FormID = d.FormID, .EditorID = d.EditorID, .DisplayName = d.EditorID, .Signature = "ARMA"}).ToList()
        Dim draftFids As New HashSet(Of UInteger)(entries.Select(Function(x) x.FormID))
        For Each r In _mainForm.GetAuthoredRecords("ARMA")
            If draftFids.Contains(r.FormID) Then Continue For
            entries.Add(New FormIdPickerEntry With {
                .FormID = r.FormID, .EditorID = r.EditorID, .DisplayName = r.DisplayName, .Signature = "ARMA", .PluginName = "(saved)"})
        Next
        If entries.Count = 0 Then
            MessageBox.Show(Me, "No ARMA drafts or saved authored ARMA yet. Use New / New from template first.", "Edit mine",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        ' Empty sigs → the picker lists ONLY the entries we pass (no full-ARMA enumeration). "(new)" = draft, "(saved)" = real.
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, New String() {},
                                           "Edit my ARMA (drafts + saved)", _draft.FormID, allowNull:=False,
                                           extraDraftEntries:=entries,
                                           onDeleteEntry:=AddressOf OnDeleteDraftEntry)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            Dim fid = dlg.SelectedFormID
            ' Clean up the draft we're leaving BEFORE switching to the picked one.
            RevertOrDiscardCurrentDraft()
            Dim existingDraft = _mainForm.TryGetArmaDraft(fid)
            If existingDraft IsNot Nothing Then
                _draft = existingDraft
                _templateRealFormID = 0UI
                _templateRealEditorID = ""
                _editingExistingDraft = True
                LoadDraftIntoPanels()
                SnapshotCurrentDraft()
                UpdateStatusBanner()
                RequestPreview()
            ElseIf Not LoadRealArmaTemplate(fid, asOverride:=True) Then   ' a saved authored ARMA → re-open as OVERRIDE
                MessageBox.Show(Me, "Could not parse that ARMA record.", "Edit mine",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If
        End Using
    End Sub

    ''' <summary>Delete/revert handler for the "Edit draft…" picker's "Delete / Revert…" button. Overrides
    ''' (<c>Not IsNew</c>) revert to the original record; new drafts are deleted only when nothing references
    ''' them. Guards the draft that's currently open. Returns True when the draft was removed (picker drops the row).</summary>
    Private Function OnDeleteDraftEntry(entry As FormIdPickerEntry) As Boolean
        Dim fid = entry.FormID
        If fid = 0UI Then Return False
        Dim isCurrent = (_draft IsNot Nothing AndAlso fid = _draft.FormID)
        Dim d = _mainForm.TryGetArmaDraft(fid)
        If d IsNot Nothing Then
            If Not d.IsNew Then
                ' OVERRIDE draft → REVERT (discard my edits; the original record wins). Allowed even when it's the
                ' one currently open — we reload the pristine original so the editor stays in a valid state.
                If MessageBox.Show(Me, $"Revert '{d.EditorID}' to the original record? Your edits to this draft will be discarded.",
                                   "Revert to original", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return False
                _mainForm.UnregisterArmaDraft(fid)
                If isCurrent Then LoadRealArmaTemplate(fid, asOverride:=True)   ' reload the pristine original for continued editing
                Return True
            End If
            ' NEW draft → DELETE, but not the one you're currently building.
            If isCurrent Then
                MessageBox.Show(Me, "This is the NEW draft you're currently editing — switch to another first, then delete it.",
                                "Delete draft", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return False
            End If
            Dim referrers = _mainForm.GetDraftReferrers(fid)
            If referrers.Count > 0 Then
                MessageBox.Show(Me, "Can't delete — this draft is still referenced by:" & vbCrLf & vbCrLf & String.Join(vbCrLf, referrers),
                                "Delete draft", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return False
            End If
            If MessageBox.Show(Me, $"Delete draft '{d.EditorID}'? This cannot be undone.",
                               "Delete draft", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return False
            _mainForm.UnregisterArmaDraft(fid)
            Return True
        End If
        ' An already-SAVED authored record → mark it for removal on the next Save. A NEW record (npcm_ EDID)
        ' is DELETED; an OVERRIDE (keeps the original EDID) is REVERTED (dropped → the original record wins).
        Dim isNewRec = entry.EditorID IsNot Nothing AndAlso entry.EditorID.StartsWith("npcm_", StringComparison.OrdinalIgnoreCase)
        Dim verb = If(isNewRec, "Delete", "Revert")
        Dim detail = If(isNewRec, "It will be removed from your plugin on the next Save.",
                                  "The override will be dropped on the next Save — the original record wins again.")
        Dim savedReferrers = _mainForm.GetDraftReferrers(fid)
        Dim refWarn = If(savedReferrers.Count > 0, vbCrLf & vbCrLf & "Still referenced by:" & vbCrLf & String.Join(vbCrLf, savedReferrers), "")
        If MessageBox.Show(Me, $"{verb} saved record '{entry.DisplayName}'?" & vbCrLf & detail & refWarn,
                           $"{verb} saved record", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return False
        _mainForm.MarkRecordForRemoval(fid)
        Return True
    End Function

    ''' <summary>FormIdPicker over REAL ARMA records (race-filtered to the preview NPC's race), no draft entries.
    ''' Returns the chosen global FormID, or 0 when cancelled. Shared by New-from-template + Override.</summary>
    Private Function PickRealArma(title As String) As UInteger
        ' Pre-select the CURRENT source record (if any) so switching Override ⇄ New-from-template keeps it selected.
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"ARMA"},
                                           title, CurrentTemplateSelection(), allowNull:=False,
                                           formIdFilter:=Function(fid) _mainForm.IsArmaRaceCompatible(fid, _raceFormID))
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return 0UI
            Return dlg.SelectedFormID
        End Using
    End Function

    ''' <summary>The real global FormID the picker should PRE-SELECT: the explicitly-loaded template/override
    ''' source (<see cref="_templateRealFormID"/>) when set this session; else the current draft's real FormID
    ''' when it's an existing/override record (an override draft has <c>IsNew = False</c> and its FormID IS the
    ''' real global FormID); else 0 (a blank new record — nothing to pre-select). Lets re-opening the picker
    ''' land on the record currently being edited even when the editor was opened directly over a real ARMA.</summary>
    Private Function CurrentTemplateSelection() As UInteger
        If _templateRealFormID <> 0UI Then Return _templateRealFormID
        If _draft IsNot Nothing AndAlso Not _draft.IsNew Then Return _draft.FormID
        Return 0UI
    End Function

    ''' <summary>Load a REAL ARMA record as the editing target: build a draft copy (asOverride=False → NEW copy
    ''' with a fresh draft FormID; =True → an override keeping the real global FormID + EditorID), remember the
    ''' source for the banner, refresh panels + preview. Returns False if the record couldn't be parsed (draft
    ''' unchanged). Shared by the actions + the initialTemplateArmaFormID constructor path.</summary>
    Private Function LoadRealArmaTemplate(fid As UInteger, asOverride As Boolean) As Boolean
        Dim copy = BuildDraftFromExisting(fid, asOverride)
        If copy Is Nothing Then Return False
        _draft = copy
        _templateRealFormID = fid
        _templateRealEditorID = copy.EditorID
        _editingExistingDraft = False
        LoadDraftIntoPanels()
        SnapshotCurrentDraft()
        UpdateStatusBanner()
        RequestPreview()
        Return True
    End Function

    ''' <summary>Update the persistent status banner to state EXACTLY the current target + what Save will do:
    ''' OVERRIDE (real record replaced), Editing draft (an already-registered draft), or NEW record. Called after
    ''' every target/EditorID change (ctor, each action, EditorID textbox edit).</summary>
    Private Sub UpdateStatusBanner()
        If _draft Is Nothing OrElse LabelStatusBanner Is Nothing Then Return
        ' The name box holds only the <name> for a NEW draft (the prefix is fixed) — show the composed base
        ' EDID in the banner so it reads as the actual record identity, not just the bare name.
        Dim edid = If(_draft.IsNew, EditorIdField.Compose(_edidPrefix, TextBoxEdid.Text), TextBoxEdid.Text.Trim())
        If edid.Length = 0 Then edid = If(_draft.EditorID, "")
        If _editingExistingDraft Then
            LabelStatusBanner.Text = $"Editing draft — {edid} ({If(_draft.IsOverride, "override", "new")})"
        ElseIf _draft.IsOverride Then
            Dim plug = SourcePluginName(_draft.FormID)
            Dim tail = If(String.IsNullOrEmpty(plug), "", $" · {plug} → your plugin replaces it")
            LabelStatusBanner.Text = $"OVERRIDE — {edid} [0x{_draft.FormID:X8}]{tail}"
        Else
            LabelStatusBanner.Text = $"NEW record — {edid} (new FormID)"
        End If
    End Sub

    ''' <summary>Originating (source) plugin name for a real global FormID, via the ESL-aware PluginManager
    ''' helper. "" when unknown (draft sentinel / unattributed) → the banner omits the plugin clause.</summary>
    Private Function SourcePluginName(fid As UInteger) As String
        If fid = 0UI OrElse OutfitDraft.IsDraftFormID(fid) Then Return ""
        Try
            Return If(_mainForm.PluginManagerForEditor?.GetOriginatingPluginName(fid), "")
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Present the EditorID field for the current draft's mode via the shared <see cref="EditorIdField"/>
    ''' helper: NEW → fixed prefix label + editable name box + live "Saves as:" preview; OVERRIDE → kept EDID
    ''' shown read-only. Setting the name box fires <see cref="OnEdidChanged"/>, which only refreshes the banner /
    ''' preview (never recomposes), so there is no feedback loop.</summary>
    Private Sub RefreshEditorIdField()
        If _draft Is Nothing Then Return
        If _draft.IsNew Then
            EditorIdField.ConfigureNew(LabelEdid, TextBoxEdid, LabelEdidPreview, _edidPrefix, _draft.EditorID)
        Else
            EditorIdField.ConfigureOverride(LabelEdid, TextBoxEdid, LabelEdidPreview, _draft.EditorID)
        End If
    End Sub

    ''' <summary>EditorID textbox edit → keep the banner in sync (it echoes the current EditorID).</summary>
    Private Sub OnEdidChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        UpdateStatusBanner()
        If _draft IsNot Nothing AndAlso _draft.IsNew Then EditorIdField.UpdatePreview(LabelEdidPreview, _edidPrefix, TextBoxEdid.Text)
    End Sub

    ' =====================================================================
    ' Load a real ARMA into a draft (copy of every editor-relevant field)
    ' =====================================================================

    ''' <summary>Build an ArmaDraft from a real ARMA record (via the draft-aware parsed view). asOverride=False →
    ''' a NEW copy with a fresh draft FormID; =True → an override keeping the real global FormID. Bone-scale +
    ''' all fields are copied so a template starts identical to its source.</summary>
    Private Function BuildDraftFromExisting(fid As UInteger, asOverride As Boolean) As ArmaDraft
        Dim a = _mainForm.GetParsedArmaForEditor(fid)
        If a Is Nothing Then Return Nothing
        Dim d As New ArmaDraft With {
            .FormID = If(asOverride, fid, _mainForm.AllocateDraftFormID()),
            .EditorID = If(Not String.IsNullOrEmpty(a.EditorID), a.EditorID, ArmaDraft.EditorIdPrefix & fid.ToString("X8")),
            .SlotMask = a.SlotMask,
            .RaceFormID = a.RaceFormID,
            .FootstepSetFormID = a.FootstepSetFormID,
            .MalePriority = ClampByte(a.MalePriority),
            .FemalePriority = ClampByte(a.FemalePriority),
            .MaleWeightSliderFlags = a.MaleWeightSliderFlags,
            .FemaleWeightSliderFlags = a.FemaleWeightSliderFlags,
            .DetectionSoundValue = a.DetectionSoundValue,
            .WeaponAdjust = a.WeaponAdjust,
            .MaleMeshPath = a.MaleMeshPath,
            .FemaleMeshPath = a.FemaleMeshPath,
            .MaleFPMeshPath = a.MaleFPMeshPath,
            .FemaleFPMeshPath = a.FemaleFPMeshPath,
            .MaleModelFlags = a.MaleModelFlags,
            .FemaleModelFlags = a.FemaleModelFlags,
            .MaleFPModelFlags = a.MaleFPModelFlags,
            .FemaleFPModelFlags = a.FemaleFPModelFlags,
            .MaleColorRemapIndex = a.MaleColorRemapIndex,
            .FemaleColorRemapIndex = a.FemaleColorRemapIndex,
            .MaleSkinTextureFormID = a.MaleSkinTextureFormID,
            .FemaleSkinTextureFormID = a.FemaleSkinTextureFormID,
            .MaleSkinTextureSwapListFormID = a.MaleSkinTextureSwapListFormID,
            .FemaleSkinTextureSwapListFormID = a.FemaleSkinTextureSwapListFormID,
            .MaleMaterialSwapFormID = a.MaleMaterialSwapFormID,
            .FemaleMaterialSwapFormID = a.FemaleMaterialSwapFormID,
            .MaleFPMaterialSwapFormID = a.MaleFPMaterialSwapFormID,
            .FemaleFPMaterialSwapFormID = a.FemaleFPMaterialSwapFormID,
            .ArtObjectFormID = a.ArtObjectFormID,
            .NoUnderarmorScaling = a.NoUnderarmorScaling,
            .HasSculptData = a.HasSculptData,
            .HiRes1stPersonOnly = a.HiRes1stPersonOnly,
            .IsOverride = asOverride, .IsNew = Not asOverride, .IsModified = False
        }
        d.AdditionalRaces.AddRange(a.AdditionalRaces)
        For Each g In a.BoneScaleData
            Dim cg As New ARMA_BoneScaleGender With {.Gender = g.Gender}
            For Each b In g.Bones
                cg.Bones.Add(New ARMA_BoneScaleDelta With {.BoneName = b.BoneName, .DeltaX = b.DeltaX, .DeltaY = b.DeltaY, .DeltaZ = b.DeltaZ})
            Next
            d.BoneScaleData.Add(cg)
        Next
        Return d
    End Function

    Private Shared Function ClampByte(v As Integer) As Byte
        If v < 0 Then Return 0
        If v > 255 Then Return CByte(255)
        Return CByte(v)
    End Function

    ' =====================================================================
    ' Draft → panels
    ' =====================================================================

    Private Sub LoadDraftIntoPanels()
        _loading = True
        Try
            RefreshEditorIdField()

            ' Models.
            TextBoxMod2.Text = _draft.MaleMeshPath
            TextBoxMod3.Text = _draft.FemaleMeshPath
            TextBoxMod4.Text = _draft.MaleFPMeshPath
            TextBoxMod5.Text = _draft.FemaleFPMeshPath
            CheckMo2fFaceBones.Checked = (_draft.MaleModelFlags And &H1) <> 0
            CheckMo2f1stPerson.Checked = (_draft.MaleModelFlags And &H2) <> 0
            CheckMo3fFaceBones.Checked = (_draft.FemaleModelFlags And &H1) <> 0
            CheckMo3f1stPerson.Checked = (_draft.FemaleModelFlags And &H2) <> 0

            ' Slots.
            SetSlotChecks(_draft.SlotMask)

            ' Skin & material.
            SetFidText(TextBoxRace, _draft.RaceFormID)
            SetFidText(TextBoxSndd, _draft.FootstepSetFormID)
            SetFidText(TextBoxNam0, _draft.MaleSkinTextureFormID)
            SetFidText(TextBoxNam1, _draft.FemaleSkinTextureFormID)
            SetFidText(TextBoxNam2, _draft.MaleSkinTextureSwapListFormID)
            SetFidText(TextBoxNam3, _draft.FemaleSkinTextureSwapListFormID)
            SetFidText(TextBoxMo2s, _draft.MaleMaterialSwapFormID)
            SetFidText(TextBoxMo3s, _draft.FemaleMaterialSwapFormID)
            SetFidText(TextBoxMo4s, _draft.MaleFPMaterialSwapFormID)
            SetFidText(TextBoxMo5s, _draft.FemaleFPMaterialSwapFormID)
            SetFidText(TextBoxOnam, _draft.ArtObjectFormID)
            NumMalePrio.Value = ClampDec(CDec(_draft.MalePriority), NumMalePrio)
            NumFemalePrio.Value = ClampDec(CDec(_draft.FemalePriority), NumFemalePrio)
            NumDetectionSound.Value = ClampDec(CDec(_draft.DetectionSoundValue), NumDetectionSound)
            NumWeaponAdjust.Value = ClampDec(CDec(_draft.WeaponAdjust), NumWeaponAdjust)
            CheckMaleWeight.Checked = (_draft.MaleWeightSliderFlags And &H2) <> 0
            CheckFemaleWeight.Checked = (_draft.FemaleWeightSliderFlags And &H2) <> 0
            RefreshAddRacesList()

            ' Sculpt: expand BSMS deltas → absolute per-gender working sets.
            _sculptByGender(0UI) = AbsRowsForGender(0UI)
            _sculptByGender(1UI) = AbsRowsForGender(1UI)
            _sculptShownGender = If(RadioSculptFemale.Checked, 1UI, 0UI)
            LoadSculptGrid(_sculptShownGender)

            ' Flags.
            CheckNoUnderarmorScaling.Checked = _draft.NoUnderarmorScaling
            CheckHasSculptData.Checked = _draft.HasSculptData
        Finally
            _loading = False
        End Try
    End Sub

    ''' <summary>Absolute sculpt rows for a gender, from the draft's BSMS delta block (delta + 1.0).</summary>
    Private Function AbsRowsForGender(gender As UInteger) As List(Of SclpFile.SclpBoneAbsolute)
        Dim block = _draft.BoneScaleData.FirstOrDefault(Function(g) g.Gender = gender)
        If block Is Nothing Then Return New List(Of SclpFile.SclpBoneAbsolute)
        Return SclpFile.FromGenderBlock(block)
    End Function

    ' =====================================================================
    ' Apply (commit panels → draft + register)
    ' =====================================================================

    ''' <summary>OK: commit + validate the panels into the draft; on success finalize (record the result FormID,
    ''' DialogResult.OK, close). On validation failure CommitPanelsToDraft already showed the message and returned
    ''' False → stay open. The live preview already reflects the committed draft, so no extra re-render is needed.</summary>
    Private Sub OnOk(sender As Object, e As EventArgs)
        If CommitPanelsToDraft(validate:=True) Then
            _resultArmaFormID = _draft.FormID
            ' Set DialogResult.OK BEFORE Close() so the ensuing FormClosing sees OK and does NOT
            ' revert/discard the draft we just finalized.
            DialogResult = DialogResult.OK
            Close()
        End If
    End Sub

    ''' <summary>Cancel: just mark the result Cancel and close — <see cref="ArmaEditor_Form_FormClosing"/> does the
    ''' revert/discard of the current (not-OK'd) draft. Centralizing it there means the window X does the same
    ''' thing as this button.</summary>
    Private Sub OnCancel(sender As Object, e As EventArgs)
        DialogResult = DialogResult.Cancel
        Close()
    End Sub

    ''' <summary>Clean up the CURRENT draft when the editor is abandoned WITHOUT an OK — either switching to
    ''' another intent action, or closing via Cancel / the window X. A draft that PRE-EXISTED this editor
    ''' (<see cref="_editingExistingDraft"/>: the editDraft ctor arg or the "Edit draft…" action) is REVERTED by
    ''' re-registering its open-time snapshot (RegisterArmaDraft replaces by FormID) — never unregistered, it
    ''' existed before us. A SESSION-created draft (New / New-from-template / Override, not yet finalized) is
    ''' UNregistered outright (discarded). Guards against a missing draft/snapshot.</summary>
    Private Sub RevertOrDiscardCurrentDraft()
        If _draft Is Nothing Then Return
        If _editingExistingDraft Then
            If _openSnapshot IsNot Nothing Then _mainForm.RegisterArmaDraft(_openSnapshot)
        Else
            _mainForm.UnregisterArmaDraft(_draft.FormID)
        End If
    End Sub

    ''' <summary>Re-take the pre-edit baseline of the CURRENT draft (in the ctor and after each intent action
    ''' loads/creates a new <see cref="_draft"/>), so <see cref="RevertOrDiscardCurrentDraft"/> targets the right
    ''' state instead of the very first draft opened.</summary>
    Private Sub SnapshotCurrentDraft()
        If _draft Is Nothing Then Return
        _openSnapshot = _draft.Clone()
        _draftWasNew = _draft.IsNew
    End Sub

    ''' <summary>Commit the panel state into <see cref="_draft"/> and register it on MainForm. When
    ''' <paramref name="validate"/> the EditorID is checked (non-empty + unique for New); on failure a message
    ''' is shown and the draft is NOT registered. Returns True on a successful commit.</summary>
    Private Function CommitPanelsToDraft(validate As Boolean) As Boolean
        ' Flush the visible sculpt grid into its gender bucket first.
        SaveSculptGrid(_sculptShownGender)

        Dim edid = If(_draft.IsNew, EditorIdField.Compose(_edidPrefix, TextBoxEdid.Text), TextBoxEdid.Text.Trim())
        If validate Then
            If edid.Length = 0 OrElse (_draft.IsNew AndAlso TextBoxEdid.Text.Trim().Length = 0) Then
                MessageBox.Show(Me, "Enter an EditorID for the ARMA.", "Apply", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return False
            End If
            ' For New: the EditorID must be free (unless unchanged on this draft). For Override the original
            ' EDID is kept; a changed one is allowed but warned.
            If _draft.IsOverride Then
                If Not String.Equals(edid, _templateRealEditorID, StringComparison.OrdinalIgnoreCase) _
                   AndAlso Not String.Equals(edid, _draft.EditorID, StringComparison.OrdinalIgnoreCase) Then
                    If MessageBox.Show(Me, "Changing the EditorID of an override record is unusual. Keep this change?",
                                       "Apply", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
                        Return False
                    End If
                End If
            ElseIf Not String.Equals(edid, _draft.EditorID, StringComparison.OrdinalIgnoreCase) _
                   AndAlso Not _mainForm.IsRecordEditorIdAvailable(edid) Then
                MessageBox.Show(Me, $"EditorID '{edid}' is already in use. Choose another.", "Apply",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return False
            End If
        End If
        _draft.EditorID = edid

        ' Models.
        _draft.MaleMeshPath = TextBoxMod2.Text.Trim()
        _draft.FemaleMeshPath = TextBoxMod3.Text.Trim()
        _draft.MaleFPMeshPath = TextBoxMod4.Text.Trim()
        _draft.FemaleFPMeshPath = TextBoxMod5.Text.Trim()
        _draft.MaleModelFlags = BuildModelFlags(CheckMo2fFaceBones.Checked, CheckMo2f1stPerson.Checked)
        _draft.FemaleModelFlags = BuildModelFlags(CheckMo3fFaceBones.Checked, CheckMo3f1stPerson.Checked)

        ' Slots.
        _draft.SlotMask = ReadSlotChecks()

        ' Skin & material.
        _draft.RaceFormID = GetFid(TextBoxRace)
        _draft.FootstepSetFormID = GetFid(TextBoxSndd)
        _draft.MaleSkinTextureFormID = GetFid(TextBoxNam0)
        _draft.FemaleSkinTextureFormID = GetFid(TextBoxNam1)
        _draft.MaleSkinTextureSwapListFormID = GetFid(TextBoxNam2)
        _draft.FemaleSkinTextureSwapListFormID = GetFid(TextBoxNam3)
        _draft.MaleMaterialSwapFormID = GetFid(TextBoxMo2s)
        _draft.FemaleMaterialSwapFormID = GetFid(TextBoxMo3s)
        _draft.MaleFPMaterialSwapFormID = GetFid(TextBoxMo4s)
        _draft.FemaleFPMaterialSwapFormID = GetFid(TextBoxMo5s)
        _draft.ArtObjectFormID = GetFid(TextBoxOnam)
        _draft.MalePriority = CByte(NumMalePrio.Value)
        _draft.FemalePriority = CByte(NumFemalePrio.Value)
        _draft.DetectionSoundValue = CByte(NumDetectionSound.Value)
        _draft.WeaponAdjust = CSng(NumWeaponAdjust.Value)
        _draft.MaleWeightSliderFlags = If(CheckMaleWeight.Checked, CByte(&H2), CByte(0))
        _draft.FemaleWeightSliderFlags = If(CheckFemaleWeight.Checked, CByte(&H2), CByte(0))

        ' Sculpt: rebuild BoneScaleData from both gender working sets (skips identity rows).
        _draft.BoneScaleData.Clear()
        For Each gender As UInteger In {0UI, 1UI}
            Dim block = SclpFile.ToGenderBlock(_sculptByGender(gender), gender)
            If block.Bones.Count > 0 Then _draft.BoneScaleData.Add(block)
        Next

        ' Flags. HasSculptData auto-set when there's bone data, otherwise honor the checkbox.
        _draft.NoUnderarmorScaling = CheckNoUnderarmorScaling.Checked
        _draft.HasSculptData = CheckHasSculptData.Checked OrElse _draft.BoneScaleData.Count > 0

        ' Dirty only on a REAL change. The preview commits the panels on every render, so setting IsModified
        ' unconditionally marked an untouched OVERRIDE dirty → the saver re-emitted an identical override. Compare
        ' the flushed content against the open-time snapshot instead (two-way: reverting a change clears it). NEW
        ' drafts are always dirty by definition. If the snapshot is missing (shouldn't happen), fall back to dirty.
        If Not _draft.IsNew Then
            _draft.IsModified = (_openSnapshot Is Nothing) OrElse Not _draft.ContentEquals(_openSnapshot)
        End If
        _mainForm.RegisterArmaDraft(_draft)
        Return True
    End Function

    Private Shared Function BuildModelFlags(faceBones As Boolean, firstPerson As Boolean) As Byte
        Dim f As Byte = 0
        If faceBones Then f = CByte(f Or &H1)
        If firstPerson Then f = CByte(f Or &H2)
        Return f
    End Function

    ' =====================================================================
    ' Models tab — mesh browse (DictionaryFilePicker Meshes\ .nif)
    ' =====================================================================

    Private Sub BrowseMeshInto(target As TextBox)
        Dim exts As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {".nif"}
        Dim keys = FilesDictionary_class.GetFilteredKeys(MeshesPrefix, exts)
        ' MeshPicker_Form = the preview-enabled .nif picker (live GL render of the selected mesh).
        Using dlg As New MeshPicker_Form(keys, MeshesPrefix, exts, target.Text.Trim())
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                Dim sel = dlg.SelectedKey
                ' The picker key carries the "Meshes\" root prefix, but ARMA model fields store the path
                ' RELATIVE to Meshes\ (the render re-adds it idempotently, so preview still works, but the
                ' SAVED record must be prefix-free or the engine can't find the mesh).
                If Not String.IsNullOrEmpty(sel) Then target.Text = sel.StripPrefix(MeshesPrefix)
            End If
        End Using
    End Sub

    ' =====================================================================
    ' Skin & Material tab — FormID pickers + additional races + material swap
    ' =====================================================================

    Private Sub OnPickRace(sender As Object, e As EventArgs)
        PickFidInto(TextBoxRace, {"RACE"}, "Select Race (RNAM)", allowNull:=True)
    End Sub

    Private Sub PickTxstInto(target As TextBox)
        PickFidInto(target, {"TXST"}, "Select skin texture (TXST)", allowNull:=True)
    End Sub

    Private Sub PickFlstInto(target As TextBox)
        PickFidInto(target, {"FLST"}, "Select skin-texture swap list (FLST)", allowNull:=True)
    End Sub

    Private Sub PickMswpInto(target As TextBox)
        PickFidInto(target, {"MSWP"}, "Select material swap (MSWP)", allowNull:=True, includeMswpDrafts:=True)
    End Sub

    ''' <summary>Open a FormIdPicker over <paramref name="sigs"/> (the xEdit wbFormIDCk allowed signatures for
    ''' the field) seeded with the textbox's current FormID, and write the chosen FormID (or 0/NULL) back as a
    ''' "Name [0xFORMID]" display. MSWP fields additionally pass the in-memory MSWP drafts so an unsaved swap is
    ''' selectable.</summary>
    Private Sub PickFidInto(target As TextBox, sigs As String(), title As String, allowNull As Boolean,
                            Optional includeMswpDrafts As Boolean = False)
        Dim drafts As List(Of FormIdPickerEntry) = Nothing
        If includeMswpDrafts Then
            drafts = _mainForm.MswpDrafts().Select(Function(d) New FormIdPickerEntry With {
                .FormID = d.FormID, .EditorID = d.EditorID, .DisplayName = d.EditorID, .Signature = "MSWP"}).ToList()
        End If
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, sigs, title, GetFid(target),
                                           allowNull, drafts)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                SetFidText(target, dlg.SelectedFormID)
                OnFieldEdited(Me, EventArgs.Empty)
            End If
        End Using
    End Sub

    Private Sub OnAddRace(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"RACE"},
                                           "Add additional race (MODL)", 0UI, allowNull:=False)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            If Not _draft.AdditionalRaces.Contains(dlg.SelectedFormID) Then
                _draft.AdditionalRaces.Add(dlg.SelectedFormID)
                RefreshAddRacesList()
            End If
        End Using
    End Sub

    Private Sub OnRemoveRace(sender As Object, e As EventArgs)
        If ListAddRaces.SelectedItems.Count = 0 Then Return
        Dim fid = CUInt(ListAddRaces.SelectedItems(0).Tag)
        _draft.AdditionalRaces.Remove(fid)
        RefreshAddRacesList()
    End Sub

    Private Sub RefreshAddRacesList()
        ListAddRaces.BeginUpdate()
        Try
            ListAddRaces.Items.Clear()
            For Each r In _draft.AdditionalRaces
                Dim row As New ListViewItem(DisplayFor(r))
                row.Tag = r
                ListAddRaces.Items.Add(row)
            Next
        Finally
            ListAddRaces.EndUpdate()
        End Try
    End Sub

    ''' <summary>"New / Edit MSWP…" for a gender: if the gender's field already points at an MSWP DRAFT, edit
    ''' that one; otherwise create a fresh MswpDraft, register it, and set the field to it. The sub-editor
    ''' sources its Original-Material dropdown from this gender's mesh NIF (MOD2 male / MOD3 female).</summary>
    Private Sub OnNewEditMswp(isFemaleGender As Boolean)
        Dim target = If(isFemaleGender, TextBoxMo3s, TextBoxMo2s)
        Dim meshPath = If(isFemaleGender, TextBoxMod3.Text.Trim(), TextBoxMod2.Text.Trim())
        Dim genderLabel = If(isFemaleGender, "Female", "Male")

        Dim currentFid = GetFid(target)
        Dim draft = _mainForm.TryGetMswpDraft(currentFid)
        Dim isNewDraft As Boolean = (draft Is Nothing)
        If isNewDraft Then
            ' Field already points at a REAL MSWP (from the ESP / load order) → edit it as an OVERRIDE seeded with
            ' its existing substitutions, not a blank one. Nothing ⇒ field empty/unresolved ⇒ fresh NEW draft.
            draft = _mainForm.BuildMswpOverrideDraftFromReal(currentFid)
            If draft Is Nothing Then
                draft = New MswpDraft With {.FormID = _mainForm.AllocateDraftFormID(),
                                            .EditorID = MswpDraft.EditorIdPrefix & "new", .IsNew = True}
                _mainForm.RegisterMswpDraft(draft)
            End If
        End If

        Using dlg As New MswpSubEditor_Form(_mainForm, draft, meshPath, genderLabel)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                ' The sub-editor wrote into the draft + (re)registered nothing; ensure it's registered.
                _mainForm.RegisterMswpDraft(draft)
                SetFidText(target, draft.FormID)
                OnFieldEdited(Me, EventArgs.Empty)
            ElseIf isNewDraft Then
                ' Cancelled a freshly-created draft → drop it so it doesn't leak into the save set.
                _mainForm.UnregisterMswpDraft(draft.FormID)
            End If
        End Using
    End Sub

    ' =====================================================================
    ' Sculpt tab
    ' =====================================================================

    Private Sub OnSculptGenderChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        ' Only act on the radio that became CHECKED (CheckedChanged fires for both).
        Dim newGender As UInteger = If(RadioSculptFemale.Checked, 1UI, 0UI)
        If newGender = _sculptShownGender Then Return
        SaveSculptGrid(_sculptShownGender)
        _sculptShownGender = newGender
        LoadSculptGrid(newGender)
    End Sub

    ''' <summary>Rebuild the slider rows in <see cref="SculptPanel"/> from the gender's absolute working set
    ''' (mirrors EditBody's CreateBodySlideRows). One row per bone = [name | X | Y | Z | ✕].</summary>
    Private Sub LoadSculptGrid(gender As UInteger)
        SculptPanel.SuspendLayout()
        Try
            SculptPanel.Controls.Clear()
            _sculptRows.Clear()
            For Each b In _sculptByGender(gender)
                AddSculptRow(b.Name, b.X, b.Y, b.Z)
            Next
        Finally
            SculptPanel.ResumeLayout()
        End Try
    End Sub

    ''' <summary>Read the live slider rows back into the gender's absolute working set. Blank-named rows are
    ''' dropped (no bone to attach to). Values are ABSOLUTE (the slider value IS the absolute scale).</summary>
    Private Sub SaveSculptGrid(gender As UInteger)
        Dim rows As New List(Of SclpFile.SclpBoneAbsolute)
        For Each r In _sculptRows
            Dim name = r.NameBox.Text.Trim()
            If name.Length = 0 Then Continue For
            rows.Add(New SclpFile.SclpBoneAbsolute With {
                .Name = name,
                .X = CSng(r.SliderX.Value),
                .Y = CSng(r.SliderY.Value),
                .Z = CSng(r.SliderZ.Value)})
        Next
        _sculptByGender(gender) = rows
    End Sub

    ''' <summary>Build one bone slider row into <see cref="SculptPanel"/> and register it in
    ''' <see cref="_sculptRows"/>. Columns line up with the Designer header row (Bone 200 | X 100 | Y 100 |
    ''' Z 100 | ✕ 30, each axis preceded by a tiny caption). Each slider is absolute (1.0 = unchanged) over
    ''' 0.5..2.5; edits trigger the debounced preview.</summary>
    Private Sub AddSculptRow(boneName As String, x As Single, y As Single, z As Single)
        Dim row As New TableLayoutPanel With {
            .ColumnCount = 8,
            .RowCount = 1,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Margin = New Padding(0, 0, 0, 4)
        }
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 16))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 16))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 16))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 30))
        row.RowStyles.Add(New RowStyle(SizeType.AutoSize))

        ' Bone name is NOT user-editable — bones are added via the "Add bone" combo below and removed with ✕.
        ' Read-only avoids accidental typos into a bone name that would silently drop the row on save.
        Dim nameBox As New TextBox With {
            .Text = If(boneName, ""),
            .ReadOnly = True,
            .TabStop = False,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right,
            .Margin = New Padding(2)
        }

        Dim sliderX = MakeSculptSlider(x)
        Dim sliderY = MakeSculptSlider(y)
        Dim sliderZ = MakeSculptSlider(z)

        Dim remove As New Button With {
            .Text = "✕",
            .Width = 24,
            .Height = 24,
            .Anchor = AnchorStyles.None,
            .Margin = New Padding(2),
            .UseVisualStyleBackColor = True
        }

        row.Controls.Add(nameBox, 0, 0)
        row.Controls.Add(MakeAxisCaption("X"), 1, 0)
        row.Controls.Add(sliderX, 2, 0)
        row.Controls.Add(MakeAxisCaption("Y"), 3, 0)
        row.Controls.Add(sliderY, 4, 0)
        row.Controls.Add(MakeAxisCaption("Z"), 5, 0)
        row.Controls.Add(sliderZ, 6, 0)
        row.Controls.Add(remove, 7, 0)

        Dim entry As New SculptRow With {
            .Container = row, .NameBox = nameBox,
            .SliderX = sliderX, .SliderY = sliderY, .SliderZ = sliderZ}
        AddHandler remove.Click, Sub() RemoveSculptRow(entry)

        SculptPanel.Controls.Add(row)
        _sculptRows.Add(entry)
    End Sub

    ''' <summary>One absolute per-axis sculpt slider (default 1.0 = unchanged). Range 0.5..2.5 covers all vanilla
    ''' sculpt spans with headroom (measured: bulk 0.65..1.35, extremes 0.71..2.25; the engine applies no clamp).</summary>
    Private Function MakeSculptSlider(value As Single) As TinySliderTextBox
        Dim bar As New TinySliderTextBox With {
            .Minimum = 0.5R,
            .Maximum = 2.5R,
            .Value = ClampSculpt(value),
            .SmallChange = 0.01R,
            .LargeChange = 0.1R,
            .DisplayFormat = "0.00",
            .FillMode = TinySliderFillMode.Center,
            .Height = 28,
            .Width = 126,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right,
            .Margin = New Padding(2)
        }
        AddHandler bar.ValueChanged, AddressOf OnFieldEdited
        AddHandler bar.DragEnded, AddressOf OnFieldEdited
        Return bar
    End Function

    ''' <summary>Tiny "X"/"Y"/"Z" caption before an axis slider.</summary>
    Private Shared Function MakeAxisCaption(text As String) As Label
        Return New Label With {.Text = text, .AutoSize = True, .Anchor = AnchorStyles.None,
                               .ForeColor = Color.DimGray, .Margin = New Padding(0)}
    End Function

    ''' <summary>Clamp an absolute scale into the slider range, defaulting NaN/Infinity to 1.0 (unchanged).</summary>
    Private Shared Function ClampSculpt(v As Single) As Double
        If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then Return 1.0R
        If v < 0.5F Then Return 0.5R
        If v > 2.5F Then Return 2.5R
        Return CDbl(v)
    End Function

    ''' <summary>Add a bone row from the "Add bone…" combo (or its free-typed text), defaulting X/Y/Z = 1.0.
    ''' A blank name is allowed (the user can type it into the new row); duplicates are not blocked.</summary>
    Private Sub OnSculptAddRow(sender As Object, e As EventArgs)
        Dim boneName = ComboSculptAddBone.Text.Trim()
        SculptPanel.SuspendLayout()
        Try
            AddSculptRow(boneName, 1.0F, 1.0F, 1.0F)
        Finally
            SculptPanel.ResumeLayout()
        End Try
        ComboSculptAddBone.Text = ""
        OnFieldEdited(Me, EventArgs.Empty)
    End Sub

    ''' <summary>Remove a single bone row (its per-row ✕ button).</summary>
    Private Sub RemoveSculptRow(entry As SculptRow)
        If entry Is Nothing Then Return
        SculptPanel.Controls.Remove(entry.Container)
        entry.Container.Dispose()
        _sculptRows.Remove(entry)
        OnFieldEdited(Me, EventArgs.Empty)
    End Sub

    ''' <summary>Load a .sclp file (ABSOLUTE per-bone scale) into the slider rows for the CURRENT gender.</summary>
    Private Sub OnSculptLoad(sender As Object, e As EventArgs)
        Using ofd As New OpenFileDialog With {.Filter = "Armor sculpt (*.sclp)|*.sclp|All files (*.*)|*.*", .Title = "Load .sclp"}
            If ofd.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Dim bones = SclpFile.Load(ofd.FileName)
                _sculptByGender(_sculptShownGender) = bones
                LoadSculptGrid(_sculptShownGender)
                OnFieldEdited(Me, EventArgs.Empty)
            Catch ex As Exception
                MessageBox.Show(Me, $"Could not read .sclp:{vbCrLf}{ex.Message}", "Load .sclp",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    ''' <summary>Save the CURRENT gender's slider rows as a .sclp file (ABSOLUTE values, no identity filtering).</summary>
    Private Sub OnSculptSave(sender As Object, e As EventArgs)
        SaveSculptGrid(_sculptShownGender)
        Using sfd As New SaveFileDialog With {.Filter = "Armor sculpt (*.sclp)|*.sclp", .Title = "Save .sclp", .DefaultExt = "sclp", .AddExtension = True}
            If sfd.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                SclpFile.Save(sfd.FileName, _sculptByGender(_sculptShownGender))
            Catch ex As Exception
                MessageBox.Show(Me, $"Could not write .sclp:{vbCrLf}{ex.Message}", "Save .sclp",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    ''' <summary>Estimate the per-bone SCLP seed for BOTH genders from this ARMA's own underarmor meshes,
    ''' measured against the race's naked reference body (RACE.WNAM skin ARMO → its body ARMA MOD2/MOD3).
    ''' Fills <see cref="_sculptByGender"/> with the estimate and refreshes the grid; behaves like a Load of a
    ''' .sclp (same dirty/preview path). A seed only — the values are approximate; the user reviews them.</summary>
    Private Sub OnSculptEstimate(sender As Object, e As EventArgs)
        Try
            ' Flush ALL panel state into the draft (mesh paths, race, and the currently-shown sculpt grid) so
            ' the estimate reads the user's latest typed values, not a stale pre-debounce snapshot.
            CommitPanelsToDraft(validate:=False)

            Dim anyEstimated As Boolean = False
            Dim messages As New List(Of String)
            For g As UInteger = 0UI To 1UI
                Dim uaRaw = If(g = 0UI, _draft.MaleMeshPath, _draft.FemaleMeshPath)
                If String.IsNullOrWhiteSpace(uaRaw) Then Continue For   ' this gender has no mesh of its own → leave as-is

                Dim bodyPaths = ResolveNakedBodyMeshPaths(_draft.RaceFormID, g)
                If bodyPaths Is Nothing OrElse bodyPaths.Count = 0 Then
                    messages.Add($"{If(g = 0UI, "Male", "Female")}: could not resolve the naked body.")
                    Continue For
                End If

                Dim uaBytes = MeshPathHelpers.TryLoadMeshBytes(MeshPathHelpers.NormalizeMeshKey(uaRaw))
                If uaBytes Is Nothing Then
                    messages.Add($"{If(g = 0UI, "Male", "Female")}: could not read the underarmor mesh.")
                    Continue For
                End If
                ' All naked-skin parts (body + hands + feet) of this gender → one combined reference.
                Dim bodyBytesList As New List(Of Byte())
                For Each bp In bodyPaths
                    Dim bb = MeshPathHelpers.TryLoadMeshBytes(MeshPathHelpers.NormalizeMeshKey(bp))
                    If bb IsNot Nothing Then bodyBytesList.Add(bb)
                Next
                If bodyBytesList.Count = 0 Then
                    messages.Add($"{If(g = 0UI, "Male", "Female")}: could not read the naked body mesh.")
                    Continue For
                End If

                Dim est = SclpEstimator.EstimateSclp(uaBytes, bodyBytesList)
                If est IsNot Nothing AndAlso est.Count > 0 Then
                    _sculptByGender(g) = est
                    anyEstimated = True
                Else
                    messages.Add($"{If(g = 0UI, "Male", "Female")}: estimate produced no bones (check the underarmor has _skin bones).")
                End If
            Next

            LoadSculptGrid(_sculptShownGender)

            If anyEstimated Then
                CheckHasSculptData.Checked = True
                OnFieldEdited(Me, EventArgs.Empty)   ' same dirty/preview path as OnSculptLoad
            End If

            Dim summary = If(anyEstimated,
                             "Estimate applied — review the values (approximate seed).",
                             "Could not estimate either gender.")
            If messages.Count > 0 Then summary &= vbCrLf & vbCrLf & String.Join(vbCrLf, messages)
            MessageBox.Show(Me, summary, "Estimate sculpt", MessageBoxButtons.OK,
                            If(anyEstimated, MessageBoxIcon.Information, MessageBoxIcon.Warning))
        Catch ex As Exception
            MessageBox.Show(Me, $"Could not estimate sculpt:{vbCrLf}{ex.Message}", "Estimate sculpt",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>Resolve the naked reference body NIF path (raw, un-normalized) for a race + gender:
    ''' RACE.WNAM (skin ARMO) → its ArmorAddons → the ARMA that carries the body (skin TXST NAM0/NAM1, else the
    ''' ALL same-gender part meshes (MOD2 male / MOD3 female). Returns an empty list when unresolved. Reuses the
    ''' editor's draft-aware parsed views (<see cref="MainForm.GetParsedArmoForEditor"/> /
    ''' <see cref="MainForm.GetParsedArmaForEditor"/>) and the master plugin manager for the RACE parse.</summary>
    Private Function ResolveNakedBodyMeshPaths(raceFormID As UInteger, gender As UInteger) As List(Of String)
        ' Symmetric per gender — does NOT branch on the NPC's own gender: always read the requested gender's body
        ' from the NPC's resolved skin, race as fallback. So female-NPC→male and male-NPC→female work identically.
        Dim requestedIsFemale = (gender = 1UI)
        Dim npcSkinFid As UInteger = _mainForm.GetCurrentPreviewSkinFormID()
        Dim raceSkinFid = ResolveRaceSkinFormID(raceFormID)

        ' The body that WOULD come from THIS NPC for the requested gender = the NPC's resolved skin
        ' (NPC.WNAM ?? RACE.WNAM) — the SAME ARMO for either gender — read for the requested gender's meshes.
        ' Only when that skin has no mesh for this gender (e.g. a single-gender custom skin) fall back to the
        ' race skin. So each gender is the NPC's own body re-read per gender, race as last resort.
        Dim meshes = CollectSkinPartMeshes(npcSkinFid, requestedIsFemale)
        If meshes.Count = 0 AndAlso raceSkinFid <> npcSkinFid Then
            meshes = CollectSkinPartMeshes(raceSkinFid, requestedIsFemale)
        End If
        Return meshes
    End Function

    ''' <summary>All the naked skin's same-gender part meshes (body + hands + feet) of a skin ARMO, unioned — the
    ''' body reference, no picking. Empty when unresolved. The estimator merges their per-bone vertices so every
    ''' bone the underarmor touches has a body counterpart (hands/feet carry the extremity bones).</summary>
    Private Function CollectSkinPartMeshes(skinFid As UInteger, requestedIsFemale As Boolean) As List(Of String)
        Dim result As New List(Of String)
        If skinFid = 0UI Then Return result
        Dim armo = _mainForm.GetParsedArmoForEditor(skinFid)
        If armo Is Nothing Then Return result

        Dim armaFids As New List(Of UInteger)
        If armo.ArmorAddons IsNot Nothing AndAlso armo.ArmorAddons.Count > 0 Then
            For Each ent In armo.ArmorAddons
                armaFids.Add(ent.ArmaFormID)
            Next
        ElseIf armo.ArmorAddonFormIDs IsNot Nothing Then
            armaFids.AddRange(armo.ArmorAddonFormIDs)
        End If

        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each fid In armaFids
            Dim arma = _mainForm.GetParsedArmaForEditor(fid)
            If arma Is Nothing Then Continue For
            Dim mesh = If(requestedIsFemale, arma.FemaleMeshPath, arma.MaleMeshPath)
            If String.IsNullOrWhiteSpace(mesh) Then Continue For
            If seen.Add(mesh) Then result.Add(mesh)
        Next
        Return result
    End Function

    ''' <summary>The RACE's default skin ARMO FormID (RACE.WNAM), or 0 when unresolved. Fallback body when the
    ''' NPC's own skin has no mesh for a gender.</summary>
    Private Function ResolveRaceSkinFormID(raceFormID As UInteger) As UInteger
        If raceFormID = 0UI Then Return 0UI
        Dim pm = _mainForm.PluginManagerForEditor
        If pm Is Nothing Then Return 0UI
        Dim raceRec = pm.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return 0UI
        Dim race = RecordParsers.ParseRACE(raceRec, pm)
        Return If(race IsNot Nothing, race.SkinFormID, 0UI)
    End Function

    ' =====================================================================
    ' Slot checkbox helpers (BOD2 bit = slot − 30)
    ' =====================================================================

    Private Sub SetSlotChecks(mask As UInteger)
        BipedSlotCheckboxes.SetMask(_slotChecks, mask)
    End Sub

    Private Function ReadSlotChecks() As UInteger
        Return BipedSlotCheckboxes.ReadMask(_slotChecks)
    End Function

    ' =====================================================================
    ' FormID textbox helpers ("Name [0xFORMID]"; Tag carries the raw UInteger)
    ' =====================================================================

    ''' <summary>Write a FormID into a display textbox as "Name [0xFORMID]" (or "(none)" for 0), stashing the
    ''' raw UInteger in the control's Tag so <see cref="GetFid"/> reads it back without re-parsing the text.</summary>
    Private Sub SetFidText(tb As TextBox, fid As UInteger)
        tb.Tag = fid
        tb.Text = If(fid = 0UI, "(none)", $"{DisplayFor(fid)} [0x{fid:X8}]")
    End Sub

    Private Shared Function GetFid(tb As TextBox) As UInteger
        If tb.Tag Is Nothing Then Return 0UI
        Return CUInt(tb.Tag)
    End Function

    Private Function DisplayFor(fid As UInteger) As String
        Return _mainForm.GetRecordDisplayNameForEditor(fid)
    End Function

    Private Shared Function ClampDec(v As Decimal, num As NumericUpDown) As Decimal
        If v < num.Minimum Then Return num.Minimum
        If v > num.Maximum Then Return num.Maximum
        Return v
    End Function

    ' =====================================================================
    ' Field-edit → debounced preview
    ' =====================================================================

    Private Sub OnFieldEdited(sender As Object, e As EventArgs)
        If _loading Then Return
        _pendingApply = True
        RequestPreview()
    End Sub

    Private Sub RequestPreview()
        If _host Is Nothing OrElse _previewDebounce Is Nothing Then Return
        _previewDebounce.Stop()
        _previewDebounce.Start()
    End Sub

    Private Async Sub PreviewDebounce_Tick(sender As Object, e As EventArgs) Handles _previewDebounce.Tick
        _previewDebounce.Stop()
        ' A render is still in flight: re-arm and retry rather than committing now and dropping this edit
        ' (RenderPreviewAsync would early-return on _previewInProgress, losing the update with no reschedule).
        If _previewInProgress Then
            _previewDebounce.Start()
            Return
        End If
        ' No commit gating here: RenderPreviewAsync now ALWAYS commits+registers _draft (validate:=False) at
        ' its top, so the draft is guaranteed registered before the first preview render (the earlier
        ' _pendingApply gate skipped commit on the initial render → unregistered draft → naked).
        _pendingApply = False
        Await RenderPreviewAsync()
    End Sub

    ' =====================================================================
    ' WYSIWYG preview — wrap the ARMA draft in a throwaway ARMO → throwaway OTFT
    ' =====================================================================

    ''' <summary>Cache key over EVERY render-relevant ARMA field, so a no-op re-render is skipped but ANY
    ''' edit that changes the equipped result (mesh, slots, race, material swaps, skin TXST/FLST, priorities,
    ''' model flags, no-underarmor-scaling) re-renders. Earlier the key only covered mesh+slots, so material-
    ''' swap / skin / race edits silently left a stale preview.</summary>
    Private Function BuildPreviewKey(armoFid As UInteger) As String
        Return String.Join(":", {
            armoFid.ToString("X8"), _draft.FormID.ToString("X8"), _draft.SlotMask.ToString("X8"),
            _draft.MaleMeshPath, _draft.FemaleMeshPath,
            _draft.RaceFormID.ToString("X8"),
            _draft.MaleMaterialSwapFormID.ToString("X8"), _draft.FemaleMaterialSwapFormID.ToString("X8"),
            _mainForm.GetMswpDraftSignature(_draft.MaleMaterialSwapFormID), _mainForm.GetMswpDraftSignature(_draft.FemaleMaterialSwapFormID),
            _draft.MaleSkinTextureFormID.ToString("X8"), _draft.FemaleSkinTextureFormID.ToString("X8"),
            _draft.MaleSkinTextureSwapListFormID.ToString("X8"), _draft.FemaleSkinTextureSwapListFormID.ToString("X8"),
            _draft.MalePriority.ToString(CultureInfo.InvariantCulture), _draft.FemalePriority.ToString(CultureInfo.InvariantCulture),
            _draft.MaleModelFlags.ToString(CultureInfo.InvariantCulture), _draft.FemaleModelFlags.ToString(CultureInfo.InvariantCulture),
            If(_draft.NoUnderarmorScaling, "1", "0")})
    End Function

    Private Async Function RenderPreviewAsync() As Task
        If _host Is Nothing OrElse _preview Is Nothing OrElse _preview.IsDisposed Then Return
        If _previewNpcFormID = 0UI Then Return

        ' Flush the current panel state into _draft AND register it (RegisterArmaDraft) BEFORE the wrapper +
        ' preview key are built. The very first render (from _Shown / template-load) reaches here with
        ' _pendingApply=False, so without this the draft would never be registered and the draft-aware
        ' resolver (TryGetArmaDraft) returns Nothing → naked. validate:=False never early-returns, so the
        ' draft is always registered; the key is computed from _draft AFTER this so it stays correct.
        CommitPanelsToDraft(validate:=False)

        Dim armoFid = EnsureArmaPreviewWrapper(_draft.FormID)
        Dim key As String = BuildPreviewKey(armoFid)
        If key = _lastPreviewKey Then Return
        If _previewInProgress Then Return
        _previewInProgress = True
        Try
            Dim otft As New OutfitDraft With {.FormID = OutfitDraft.PreviewDraftFormID,
                                              .EditorID = OutfitDraft.EditorIdPrefix & "(armapreview)"}
            otft.ItemFormIDs.Add(armoFid)
            _mainForm.RegisterOutfitDraft(otft)
            _previewDraftRegistered = True

            Try
                Await _mainForm.PreviewOutfitInHostAsync(_host, _previewNpcFormID, OutfitDraft.PreviewDraftFormID)
                _lastPreviewKey = key
            Catch
                ' A failed preview render must not break the dialog.
            End Try
        Finally
            _previewInProgress = False
        End Try
    End Function

    ''' <summary>(Re)register a throwaway ARMO draft (single addon INDX 0) that references the ARMA draft, so
    ''' the OTFT preview can render it. Returns the wrapper ARMO FormID.</summary>
    Private Function EnsureArmaPreviewWrapper(armaFid As UInteger) As UInteger
        Dim wrapper As New ArmoDraft With {.FormID = PreviewArmoWrapperFormID,
                                           .EditorID = ArmoDraft.EditorIdPrefix & "(armapreview)",
                                           .RaceFormID = _draft.RaceFormID}
        wrapper.ArmorAddons.Add(New ARMO_AddonEntry With {.AddonIndex = 0US, .ArmaFormID = armaFid})
        _mainForm.RegisterArmoDraft(wrapper)
        _previewArmaWrapperRegistered = True
        Return PreviewArmoWrapperFormID
    End Function

    ' =====================================================================
    ' Form lifecycle (preview host setup/teardown — mirror OutfitPicker)
    ' =====================================================================

    Private Sub ArmaEditor_Form_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        If _previewNpcFormID = 0UI Then Return   ' no preview without a context NPC

        If _preview Is Nothing OrElse _preview.IsDisposed Then
            _preview = New PreviewControl() With {.Dock = DockStyle.Fill}
            PreviewControlPanel.Controls.Add(_preview)
            _preview.BringToFront()
            _preview.ApplyResize(True)
        End If

        If _host Is Nothing Then
            _host = New NpcRenderHost(_preview) With {
                .AppliedPresets = _mainForm.AppliedPresetsForEditor,
                .Toggles = _mainForm.BuildOutfitPickerToggles()
            }
        End If

        RequestPreview()
    End Sub

    Private Sub ArmaEditor_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        ' Not finalized (Cancel or the window X → DialogResult ≠ OK): revert/discard the current session draft so
        ' abandoned drafts don't accumulate. OK finalized the draft in OnOk (which set DialogResult.OK first), so
        ' this is skipped and the finalized draft survives.
        If DialogResult <> DialogResult.OK Then RevertOrDiscardCurrentDraft()

        ' Drop the throwaway preview drafts so they never leak into the save set / other pickers.
        If _previewDraftRegistered Then
            Try
                _mainForm.UnregisterOutfitDraft(OutfitDraft.PreviewDraftFormID)
            Catch
            End Try
            _previewDraftRegistered = False
        End If
        If _previewArmaWrapperRegistered Then
            Try
                _mainForm.UnregisterArmoDraft(PreviewArmoWrapperFormID)
            Catch
            End Try
            _previewArmaWrapperRegistered = False
        End If

        If _previewDebounce IsNot Nothing Then
            Try
                _previewDebounce.Stop()
            Catch
            End Try
        End If

        ' Quiesce render loop → host Dispose → control Clean/Dispose (same ordering as OutfitPicker/EditBody).
        If _preview IsNot Nothing AndAlso Not _preview.IsDisposed Then
            Try
                _preview.BeginTeardown()
            Catch
            End Try
        End If
        If _host IsNot Nothing Then
            Try
                _host.Dispose()
            Catch
            End Try
            _host = Nothing
        End If
        If _preview IsNot Nothing AndAlso Not _preview.IsDisposed Then
            Try
                _preview.Clean()
            Catch
            End Try
            Try
                _preview.Dispose()
            Catch
            End Try
        End If
    End Sub

End Class
