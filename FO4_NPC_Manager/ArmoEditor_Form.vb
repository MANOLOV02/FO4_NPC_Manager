Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports FO4_Base_Library

''' <summary>Standalone editor to author a single Armor (ARMO) record — its name/race/slots/DATA, its
''' Armor Addons (ARMA references, the important tab), its keywords (KWDA) + attach-parent-slots (APPR),
''' and its World Model + material swaps. Companion to <see cref="ArmaEditor_Form"/>: same look &amp; feel,
''' same components, same draft/preview model.
'''
''' Flow (mirror of ArmaEditor): a <b>Template</b> button loads an existing ARMO (real record OR an ARMO
''' draft) into the panels; a <b>New</b>/<b>Override</b> mode radio decides whether Apply commits to a
''' brand-new draft (New) or an override of the loaded REAL record (Override, only enabled for a real
''' template). If an <c>editDraft</c> is passed it's loaded directly. <b>Apply</b> commits the panel state
''' into the draft and registers it via the ARMO draft registrar (no ESP write); <b>Close</b> ends.
'''
''' Layout: a SplitContainer with the field tabs on the left and a dedicated preview panel hosting an
''' <see cref="NpcRenderHost"/> on the right. The WYSIWYG preview wraps the ARMO draft DIRECTLY in a
''' throwaway <see cref="OutfitDraft"/> at <see cref="OutfitDraft.PreviewDraftFormID"/> (ARMO is the outfit
''' item — no ARMO→ARMO wrapper) and renders it equipped on the current NPC via
''' <see cref="MainForm.PreviewOutfitInHostAsync"/>. Re-preview is debounced.</summary>
Public Class ArmoEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _previewNpcFormID As UInteger
    Private ReadOnly _raceFormID As UInteger
    Private ReadOnly _isFemale As Boolean

    ''' <summary>The ARMO draft currently being edited (the authoring model). Always non-Nothing after the
    ''' constructor: either the passed-in editDraft, or a fresh empty draft seeded with the preview race.</summary>
    Private _draft As ArmoDraft
    ''' <summary>Suppresses preview re-render + dirty marking while panels are being LOADED programmatically.</summary>
    Private _loading As Boolean

    ''' <summary>The fixed type prefix for this editor's stored base EditorID, driven through the shared
    ''' <see cref="EditorIdField"/> helper (NEW = editable name under this prefix; OVERRIDE = kept verbatim).</summary>
    Private ReadOnly _edidPrefix As String = ArmoDraft.EditorIdPrefix

    ''' <summary>The biped slots laid out as granular checkboxes (FO4 BOD2 bit = slot − 30), built by the
    ''' shared <see cref="BipedSlotCheckboxes"/> helper into the Designer-declared <see cref="FlowSlots"/>.</summary>
    Private _slotChecks As Dictionary(Of Integer, CheckBox)

    ''' <summary>The ARMO's armor addons (INDX + ARMA FormID), in row order. The Addons grid is the editable
    ''' view of this list — flushed on Apply (and into the throwaway preview on a debounced edit).</summary>
    Private ReadOnly _addons As New List(Of ARMO_AddonEntry)

    ''' <summary>Working buffers for KWDA / APPR (both KYWD FormID lists), edited by the Add/Remove handlers and
    ''' flushed into the draft only on Apply — same commit-on-Apply contract as <see cref="_addons"/>, so
    ''' cancelling the editor doesn't leave keyword edits baked into the registered draft.</summary>
    Private ReadOnly _keywords As New List(Of UInteger)
    Private ReadOnly _appr As New List(Of UInteger)

    ''' <summary>The ARMO's Object Template (OBTE/OBTS) combinations, in row order. The "Object Template" grid is
    ''' the editable view of this list; it is deep-copied from <see cref="ArmoDraft.Combinations"/> on load and
    ''' deep-copied back on Apply (never aliased — the parsed cache must stay pristine). Every mutation marks
    ''' <see cref="ArmoDraft.CombinationsEdited"/> and requests a debounced preview.</summary>
    Private ReadOnly _combinations As New List(Of ARMO_Combination)

    ''' <summary>The ARMO's damage resistances (DAMA: DMGT FormID + Value), in row order. The "Damage Resist" grid
    ''' is the read-only view of this list — deep-copied from the draft on load and flushed (deep-copied) back on
    ''' Apply, same commit-on-Apply contract as <see cref="_addons"/>.</summary>
    Private ReadOnly _damageResists As New List(Of ARMO_DamageResist)

    ' === preview (mirror ArmaEditor) ===
    Private _preview As PreviewControl
    Private _host As NpcRenderHost
    Private _previewDraftRegistered As Boolean
    Private _lastPreviewKey As String = Nothing
    Private _previewInProgress As Boolean
    Private _pendingApply As Boolean
    Private WithEvents _previewDebounce As Timer

    ''' <summary>The real ARMO FormID/EditorID an Override / New-from-template copy descends from — the SOURCE
    ''' record. Kept for the banner's source-plugin line and the Override EditorID-change check on Apply.</summary>
    Private _templateRealFormID As UInteger = 0UI
    Private _templateRealEditorID As String = ""

    ''' <summary>True while this editor is continuing to edit one of the user's ALREADY-registered drafts (via
    ''' the editDraft ctor arg or the "Edit draft…" action) — drives the "Editing draft" banner wording.</summary>
    Private _editingExistingDraft As Boolean = False

    ''' <summary>Snapshot of the draft taken at open (after load/create + panel populate). On Cancel, editing an
    ''' EXISTING/override draft re-registers this snapshot to revert the live-commit mutations. See <see cref="OnCancel"/>.</summary>
    Private _openSnapshot As ArmoDraft
    ''' <summary>True when the draft was created fresh in THIS editor (not passed in as editDraft, not an override
    ''' of an existing registered record) — i.e. <see cref="ArmoDraft.IsNew"/> at open. On Cancel a brand-new draft
    ''' is UNregistered (discarded); an existing/override draft is reverted from <see cref="_openSnapshot"/>.</summary>
    Private _draftWasNew As Boolean

    ''' <param name="mainForm">Owner — supplies the draft registrars, the PluginManager for the FormID pickers,
    ''' parsed-record access and the WYSIWYG preview host.</param>
    ''' <param name="previewNpcFormID">The currently-selected NPC for preview context. 0 = no preview.</param>
    ''' <param name="raceFormID">The preview NPC's race (pre-fills a new ARMO's RNAM).</param>
    ''' <param name="isFemale">The preview NPC's gender (drives which mesh/material is previewed).</param>
    ''' <param name="editDraft">When supplied, edit this existing ARMO draft directly (skip the empty seed).</param>
    ''' <param name="initialTemplateArmoFormID">When nonzero AND no <paramref name="editDraft"/> is given,
    ''' pre-load this REAL ARMO on open — as a NEW-record copy by default, or as an OVERRIDE when
    ''' <paramref name="templateAsOverride"/> is True (the Outfit Picker double-click "Override this armor").</param>
    ''' <param name="templateAsOverride">True → load <paramref name="initialTemplateArmoFormID"/> as an
    ''' OVERRIDE (xEdit "copy as override"); False → as a NEW record (xEdit "copy as new record"). Ignored when
    ''' no template FormID is supplied.</param>
    Public Sub New(mainForm As MainForm, previewNpcFormID As UInteger, raceFormID As UInteger, isFemale As Boolean,
                   Optional editDraft As ArmoDraft = Nothing, Optional initialTemplateArmoFormID As UInteger = 0UI,
                   Optional templateAsOverride As Boolean = True)
        InitializeComponent()
        _mainForm = mainForm
        _previewNpcFormID = previewNpcFormID
        _raceFormID = raceFormID
        _isFemale = isFemale

        BuildSlotCheckBoxes()
        BuildAddonsGridColumns()
        BuildCombinationsGridColumns()
        BuildDamageGridColumns()

        _previewDebounce = New Timer() With {.Interval = 400}

        ' Top bar — explicit intent actions (xEdit model: New / New-from-template copy / Override / Edit draft).
        AddHandler ButtonNewBlank.Click, AddressOf OnActionNewBlank
        AddHandler ButtonNewFromTemplate.Click, AddressOf OnActionNewFromTemplate
        AddHandler ButtonOverrideExisting.Click, AddressOf OnActionOverrideExisting
        AddHandler ButtonEditDraft.Click, AddressOf OnActionEditDraft
        AddHandler TextBoxEdid.TextChanged, AddressOf OnEdidChanged

        ' General tab.
        AddHandler ButtonPickRace.Click, AddressOf OnPickRace
        AddHandler ButtonPickInnr.Click, AddressOf OnPickInnr
        AddHandler ButtonPickEitm.Click, AddressOf OnPickEitm
        AddHandler ButtonPickPtrn.Click, AddressOf OnPickPtrn
        AddHandler CheckBoxNonPlayable.CheckedChanged, AddressOf OnFieldEdited
        AddHandler TextBoxDesc.TextChanged, AddressOf OnFieldEdited

        ' Misc & Sounds tab.
        AddHandler ButtonPickYnam.Click, AddressOf OnPickYnam
        AddHandler ButtonPickZnam.Click, AddressOf OnPickZnam
        AddHandler ButtonPickEtyp.Click, AddressOf OnPickEtyp
        AddHandler ButtonPickBamt.Click, AddressOf OnPickBamt
        AddHandler ButtonRecomputeObnd.Click, AddressOf OnRecomputeObnd

        ' Damage Resist tab — read-only grid; mutate via buttons / the double-click modal.
        AddHandler ButtonAddDamage.Click, AddressOf OnAddDamage
        AddHandler ButtonEditDamage.Click, AddressOf OnEditDamage
        AddHandler ButtonRemoveDamage.Click, AddressOf OnRemoveDamage
        AddHandler GridDamage.CellDoubleClick, AddressOf OnDamageDoubleClick

        ' Slots tab — recompute the BOD2 mask from the included ARMA addons.
        AddHandler ButtonRecalcSlots.Click, AddressOf OnRecalcSlotsFromArma

        ' Addons tab — grid is read-only; every mutation goes through a button / the double-click modal.
        AddHandler ButtonAddArma.Click, AddressOf OnAddArma
        AddHandler ButtonEditIndx.Click, AddressOf OnEditAddon
        AddHandler ButtonRemoveAddon.Click, AddressOf OnRemoveAddon
        AddHandler ButtonAddonUp.Click, Sub() MoveAddon(-1)
        AddHandler ButtonAddonDown.Click, Sub() MoveAddon(1)
        AddHandler GridAddons.CellDoubleClick, AddressOf OnAddonDoubleClick

        ' Keywords tab.
        AddHandler ButtonAddKwda.Click, AddressOf OnAddKwda
        AddHandler ButtonRemoveKwda.Click, AddressOf OnRemoveKwda
        AddHandler ButtonAddAppr.Click, AddressOf OnAddAppr
        AddHandler ButtonRemoveAppr.Click, AddressOf OnRemoveAppr

        ' Object Template (OBTS) tab.
        AddHandler ButtonAddCombo.Click, AddressOf OnAddCombo
        AddHandler ButtonRemoveCombo.Click, AddressOf OnRemoveCombo
        AddHandler ButtonDuplicateCombo.Click, AddressOf OnDuplicateCombo
        AddHandler ButtonComboUp.Click, Sub() MoveCombo(-1)
        AddHandler ButtonComboDown.Click, Sub() MoveCombo(1)
        AddHandler ButtonEditCombo.Click, AddressOf OnEditCombo
        AddHandler GridCombinations.CellDoubleClick, AddressOf OnComboDoubleClick

        ' World Model & Material tab.
        AddHandler ButtonBrowseMod2.Click, Sub() BrowseMeshInto(TextBoxMod2)
        AddHandler ButtonBrowseMod4.Click, Sub() BrowseMeshInto(TextBoxMod4)
        AddHandler ButtonPickMo2s.Click, Sub() PickMswpInto(TextBoxMo2s)
        AddHandler ButtonPickMo4s.Click, Sub() PickMswpInto(TextBoxMo4s)
        AddHandler ButtonEditMo2s.Click, Sub() OnNewEditMswp(isFemaleGender:=False)
        AddHandler ButtonEditMo4s.Click, Sub() OnNewEditMswp(isFemaleGender:=True)

        ' Bottom (OK finalizes + validates + closes; Cancel discards the live-commit mutations + closes).
        AddHandler ButtonOk.Click, AddressOf OnOk
        AddHandler ButtonCancel.Click, AddressOf OnCancel

        ' Re-preview (debounced) on edits to render-relevant fields.
        AddHandler TextBoxMod2.TextChanged, AddressOf OnFieldEdited
        AddHandler TextBoxMod4.TextChanged, AddressOf OnFieldEdited
        AddHandler TextBoxMo2s.TextChanged, AddressOf OnFieldEdited
        AddHandler TextBoxMo4s.TextChanged, AddressOf OnFieldEdited

        ' Seed the draft: edit the passed one, else a fresh empty draft (NEW) seeded with the preview race.
        If editDraft IsNot Nothing Then
            _draft = editDraft
            _editingExistingDraft = True   ' continuing to edit one of the user's registered drafts
        Else
            _draft = New ArmoDraft With {.FormID = _mainForm.AllocateDraftFormID(),
                                         .EditorID = ArmoDraft.EditorIdPrefix & "new",
                                         .RaceFormID = _raceFormID, .IsNew = True}
        End If
        LoadDraftIntoPanels()

        ' Optional pre-load of a REAL ARMO template on open (Outfit Picker double-click). templateAsOverride
        ' chooses xEdit "copy as override" (True) vs "copy as new record" (False). Runs BEFORE the snapshot
        ' below so Cancel reverts to the template-loaded state.
        If editDraft Is Nothing AndAlso initialTemplateArmoFormID <> 0UI Then _
            LoadRealArmoTemplate(initialTemplateArmoFormID, asOverride:=templateAsOverride)
        UpdateStatusBanner()

        LabelPreviewHint.Text = If(_previewNpcFormID = 0UI,
                                   "Select an NPC in the main window to preview.",
                                   "Preview: this ARMO equipped on the current NPC.")

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

    ''' <summary>Build the 3 Addons grid columns: INDX (editable u16), ARMA (read-only "Name [0xFORMID]"),
    ''' Slots (read-only effective slot-mask display).</summary>
    Private Sub BuildAddonsGridColumns()
        GridAddons.AutoGenerateColumns = False
        GridAddons.Columns.Clear()
        Dim colIndx As New DataGridViewTextBoxColumn With {.HeaderText = "INDX", .FillWeight = 12, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, .ReadOnly = True}
        Dim colArma As New DataGridViewTextBoxColumn With {.HeaderText = "ARMA", .FillWeight = 58, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, .ReadOnly = True}
        Dim colSlots As New DataGridViewTextBoxColumn With {.HeaderText = "Slots", .FillWeight = 30, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, .ReadOnly = True}
        GridAddons.Columns.Add(colIndx)
        GridAddons.Columns.Add(colArma)
        GridAddons.Columns.Add(colSlots)
    End Sub

    ''' <summary>Build the Object Template grid's read-only summary columns (the row is EDITED in the modal
    ''' <see cref="ObtsCombinationEditor_Form"/>, so every column here is display-only). Order = list order.</summary>
    Private Sub BuildCombinationsGridColumns()
        GridCombinations.AutoGenerateColumns = False
        GridCombinations.Columns.Clear()
        GridCombinations.Columns.Add(NewComboCol("#", 6))
        GridCombinations.Columns.Add(NewComboCol("Name", 26))
        GridCombinations.Columns.Add(NewComboCol("Default", 9))
        GridCombinations.Columns.Add(NewComboCol("Parent/Addon Idx", 13))
        GridCombinations.Columns.Add(NewComboCol("Level Min", 9))
        GridCombinations.Columns.Add(NewComboCol("Level Max", 9))
        GridCombinations.Columns.Add(NewComboCol("#Incl", 6))
        GridCombinations.Columns.Add(NewComboCol("#Props", 6))
        GridCombinations.Columns.Add(NewComboCol("#Kwds", 6))
        GridCombinations.Columns.Add(NewComboCol("EditorOnly", 10))
    End Sub

    Private Shared Function NewComboCol(header As String, weight As Single) As DataGridViewTextBoxColumn
        Return New DataGridViewTextBoxColumn With {
            .HeaderText = header, .FillWeight = weight, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, .ReadOnly = True}
    End Function

    ''' <summary>Build the 2 read-only Damage Resist grid columns: DMGT ("Name [0xFORMID]") + Value. The row is
    ''' edited in the modal <see cref="ArmoDamageResistEditor_Form"/>, so both columns are display-only.</summary>
    Private Sub BuildDamageGridColumns()
        GridDamage.AutoGenerateColumns = False
        GridDamage.Columns.Clear()
        Dim colType As New DataGridViewTextBoxColumn With {.HeaderText = "Damage Type [DMGT]", .FillWeight = 70, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, .ReadOnly = True}
        Dim colValue As New DataGridViewTextBoxColumn With {.HeaderText = "Value", .FillWeight = 30, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, .ReadOnly = True}
        GridDamage.Columns.Add(colType)
        GridDamage.Columns.Add(colValue)
    End Sub

    ' =====================================================================
    ' Explicit intent actions (xEdit model) + status banner
    ' =====================================================================

    ''' <summary>"New (blank)" → a fresh empty New-record draft (new draft FormID, npcm_ prefix, IsNew=True,
    ''' IsOverride=False), seeded with the preview race. xEdit's "new record" from scratch.</summary>
    Private Sub OnActionNewBlank(sender As Object, e As EventArgs)
        RevertOrDiscardCurrentDraft()
        _draft = New ArmoDraft With {.FormID = _mainForm.AllocateDraftFormID(),
                                     .EditorID = ArmoDraft.EditorIdPrefix & "new",
                                     .RaceFormID = _raceFormID, .IsNew = True}
        _templateRealFormID = 0UI
        _templateRealEditorID = ""
        _editingExistingDraft = False
        LoadDraftIntoPanels()
        SnapshotCurrentDraft()
        UpdateStatusBanner()
        RequestPreview()
    End Sub

    ''' <summary>"New from template…" → pick a REAL ARMO and COPY it into a NEW record (fresh draft FormID,
    ''' IsOverride=False) — xEdit "copy as new record into". Race/gender-filtered like the old Template picker.</summary>
    Private Sub OnActionNewFromTemplate(sender As Object, e As EventArgs)
        Dim fid = PickRealArmo("Copy ARMO into a NEW record")
        If fid = 0UI Then Return
        RevertOrDiscardCurrentDraft()
        If Not LoadRealArmoTemplate(fid, asOverride:=False) Then
            MessageBox.Show(Me, "Could not parse that ARMO record.", "New from template",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If
    End Sub

    ''' <summary>"Override existing…" → pick a REAL ARMO and edit it as an OVERRIDE (keep its global FormID +
    ''' EditorID, IsOverride=True) — xEdit "copy as override into"; your plugin replaces that record on Save.</summary>
    Private Sub OnActionOverrideExisting(sender As Object, e As EventArgs)
        Dim fid = PickRealArmo("Override an existing ARMO")
        If fid = 0UI Then Return
        RevertOrDiscardCurrentDraft()
        If Not LoadRealArmoTemplate(fid, asOverride:=True) Then
            MessageBox.Show(Me, "Could not parse that ARMO record.", "Override existing",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If
    End Sub

    ''' <summary>"Edit draft…" → pick one of the user's existing ARMO drafts (shown with "(new)") and continue
    ''' editing it directly (no re-key). No-op with a message when there are no drafts yet.</summary>
    Private Sub OnActionEditDraft(sender As Object, e As EventArgs)
        ' List the user's unsaved ARMO drafts AND their already-SAVED authored ARMO records (EDID npcm_), so an
        ' outfit/armor made here can be re-opened whether or not it's been saved to the ESP yet.
        Dim entries = _mainForm.ArmoDrafts().Select(Function(d) New FormIdPickerEntry With {
            .FormID = d.FormID, .EditorID = d.EditorID, .DisplayName = d.EditorID, .Signature = "ARMO"}).ToList()
        Dim draftFids As New HashSet(Of UInteger)(entries.Select(Function(x) x.FormID))
        For Each r In _mainForm.GetAuthoredRecords("ARMO")
            If draftFids.Contains(r.FormID) Then Continue For   ' a draft overriding it already covers this FormID
            entries.Add(New FormIdPickerEntry With {
                .FormID = r.FormID, .EditorID = r.EditorID, .DisplayName = r.DisplayName, .Signature = "ARMO", .PluginName = "(saved)"})
        Next
        If entries.Count = 0 Then
            MessageBox.Show(Me, "No ARMO drafts or saved authored ARMO yet. Use New / New from template first.", "Edit mine",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        ' Empty sigs → the picker lists ONLY the entries we pass (no full-ARMO enumeration). "(new)" = draft, "(saved)" = real.
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, New String() {},
                                           "Edit my ARMO (drafts + saved)", _draft.FormID, allowNull:=False,
                                           extraDraftEntries:=entries,
                                           onDeleteEntry:=AddressOf OnDeleteDraftEntry)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            Dim fid = dlg.SelectedFormID
            ' Clean up the draft we're leaving BEFORE switching to the picked one.
            RevertOrDiscardCurrentDraft()
            Dim existingDraft = _mainForm.TryGetArmoDraft(fid)
            If existingDraft IsNot Nothing Then
                _draft = existingDraft
                _templateRealFormID = 0UI
                _templateRealEditorID = ""
                _editingExistingDraft = True
                LoadDraftIntoPanels()
                SnapshotCurrentDraft()
                UpdateStatusBanner()
                RequestPreview()
            ElseIf Not LoadRealArmoTemplate(fid, asOverride:=True) Then   ' a saved authored ARMO → re-open as OVERRIDE
                MessageBox.Show(Me, "Could not parse that ARMO record.", "Edit mine",
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
        Dim d = _mainForm.TryGetArmoDraft(fid)
        If d IsNot Nothing Then
            If Not d.IsNew Then
                ' OVERRIDE draft → REVERT (discard my edits; the original record wins). Allowed even when it's the
                ' one currently open — we reload the pristine original so the editor stays in a valid state.
                If MessageBox.Show(Me, $"Revert '{d.EditorID}' to the original record? Your edits to this draft will be discarded.",
                                   "Revert to original", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return False
                _mainForm.UnregisterArmoDraft(fid)
                If isCurrent Then LoadRealArmoTemplate(fid, asOverride:=True)   ' reload the pristine original for continued editing
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
            _mainForm.UnregisterArmoDraft(fid)
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

    ''' <summary>FormIdPicker over REAL ARMO records (race/gender-filtered to the preview NPC), no draft entries.
    ''' Returns the chosen global FormID, or 0 when cancelled. Shared by New-from-template + Override.</summary>
    Private Function PickRealArmo(title As String) As UInteger
        ' Pre-select the CURRENT source record (if any) so switching Override ⇄ New-from-template keeps it selected.
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"ARMO"},
                                           title, CurrentTemplateSelection(), allowNull:=False,
                                           formIdFilter:=Function(fid) _mainForm.IsArmoRaceCompatible(fid, _raceFormID, _isFemale))
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return 0UI
            Return dlg.SelectedFormID
        End Using
    End Function

    ''' <summary>The real global FormID the picker should PRE-SELECT: the explicitly-loaded template/override
    ''' source (<see cref="_templateRealFormID"/>) when set this session; else the current draft's real FormID
    ''' when it's an existing/override record (an override draft has <c>IsNew = False</c> and its FormID IS the
    ''' real global FormID); else 0 (a blank new record — nothing to pre-select). Lets re-opening the picker
    ''' land on the record currently being edited even when the editor was opened directly over a real ARMO.</summary>
    Private Function CurrentTemplateSelection() As UInteger
        If _templateRealFormID <> 0UI Then Return _templateRealFormID
        If _draft IsNot Nothing AndAlso Not _draft.IsNew Then Return _draft.FormID
        Return 0UI
    End Function

    ''' <summary>Load a REAL ARMO record as the editing target: build a draft copy (asOverride=False → NEW copy
    ''' with a fresh draft FormID; =True → an override keeping the real global FormID + EditorID), remember the
    ''' source for the banner, refresh panels + preview. Returns False if unparseable (draft unchanged). Shared
    ''' by the actions + the initialTemplateArmoFormID constructor path (Outfit Picker double-click).</summary>
    Private Function LoadRealArmoTemplate(fid As UInteger, asOverride As Boolean) As Boolean
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
    ' Load a real ARMO into a draft (copy of every editor-relevant field)
    ' =====================================================================

    ''' <summary>Build an ArmoDraft from a real ARMO record (via the draft-aware parsed view). asOverride=False →
    ''' a NEW copy with a fresh draft FormID; =True → an override keeping the real global FormID. Every
    ''' editor-relevant field (addons, keywords, world models, material swaps) is copied so a template starts
    ''' identical to its source.</summary>
    Private Function BuildDraftFromExisting(fid As UInteger, asOverride As Boolean) As ArmoDraft
        Dim a = _mainForm.GetParsedArmoForEditor(fid)
        If a Is Nothing Then Return Nothing
        Dim d As New ArmoDraft With {
            .FormID = If(asOverride, fid, _mainForm.AllocateDraftFormID()),
            .EditorID = If(Not String.IsNullOrEmpty(a.EditorID), a.EditorID, ArmoDraft.EditorIdPrefix & fid.ToString("X8")),
            .FullName = a.FullName,
            .SlotMask = a.SlotMask,
            .RaceFormID = a.RaceFormID,
            .InstanceNamingFormID = a.InstanceNamingFormID,
            .EnchantmentFormID = a.EnchantmentFormID,
            .PatternFormID = a.PatternFormID,
            .EquipTypeFormID = a.EquipTypeFormID,
            .PickupSoundFormID = a.PickupSoundFormID,
            .DropSoundFormID = a.DropSoundFormID,
            .AlternateBlockMaterialFormID = a.AlternateBlockMaterialFormID,
            .Description = a.Description,
            .NonPlayable = a.NonPlayable,
            .ObndX1 = a.ObndX1, .ObndY1 = a.ObndY1, .ObndZ1 = a.ObndZ1,
            .ObndX2 = a.ObndX2, .ObndY2 = a.ObndY2, .ObndZ2 = a.ObndZ2,
            .TemplateArmorFormID = a.TemplateArmorFormID,
            .MaleWorldModelPath = a.MaleWorldModelPath,
            .FemaleWorldModelPath = a.FemaleWorldModelPath,
            .MaleMaterialSwapFormID = a.MaleMaterialSwapFormID,
            .FemaleMaterialSwapFormID = a.FemaleMaterialSwapFormID,
            .Value = a.Value,
            .Weight = a.Weight,
            .Health = a.Health,
            .ArmorRating = a.ArmorRating,
            .BaseAddonIndex = If(a.BaseAddonIndex >= 0, CUShort(a.BaseAddonIndex), CUShort(0)),
            .StaggerRating = a.StaggerRating,
            .IsOverride = asOverride, .IsNew = Not asOverride, .IsModified = False
        }
        For Each ad In a.ArmorAddons
            d.ArmorAddons.Add(New ARMO_AddonEntry With {.AddonIndex = ad.AddonIndex, .ArmaFormID = ad.ArmaFormID})
        Next
        d.KeywordFormIDs.AddRange(a.KeywordFormIDs)
        d.AttachParentSlotFormIDs.AddRange(a.AttachParentSlotFormIDs)
        ' DAMA damage resistances: deep-copy (new instances) so the draft never aliases the parsed cache.
        For Each dr In a.DamageResistances
            d.DamageResistances.Add(New ARMO_DamageResist With {.DamageTypeFormID = dr.DamageTypeFormID, .Value = dr.Value})
        Next
        ' OBTS combinations: DEEP COPY (never alias the parsed cache — the draft is mutated live). Carries the
        ' object template end-to-end so the preview applies its material swap and a NEW-from-template ARMO keeps
        ' its OBTS on save.
        d.Combinations.AddRange(ArmoDraft.CloneCombinations(a.Combinations))
        Return d
    End Function

    ' =====================================================================
    ' Draft → panels
    ' =====================================================================

    Private Sub LoadDraftIntoPanels()
        _loading = True
        Try
            RefreshEditorIdField()

            ' General.
            TextBoxFull.Text = _draft.FullName
            SetFidText(TextBoxRace, _draft.RaceFormID)
            SetFidText(TextBoxInnr, _draft.InstanceNamingFormID)
            SetFidText(TextBoxEitm, _draft.EnchantmentFormID)
            SetFidText(TextBoxPtrn, _draft.PatternFormID)
            CheckBoxNonPlayable.Checked = _draft.NonPlayable
            TextBoxDesc.Text = _draft.Description
            SetSlotChecks(_draft.SlotMask)
            NumValue.Value = ClampDec(CDec(_draft.Value), NumValue)
            NumWeight.Value = ClampDec(CDec(_draft.Weight), NumWeight)
            NumHealth.Value = ClampDec(CDec(_draft.Health), NumHealth)
            NumArmorRating.Value = ClampDec(CDec(_draft.ArmorRating), NumArmorRating)
            NumBaseAddonIndex.Value = ClampDec(CDec(_draft.BaseAddonIndex), NumBaseAddonIndex)
            NumStaggerRating.Value = ClampDec(CDec(_draft.StaggerRating), NumStaggerRating)

            ' Misc & Sounds.
            SetFidText(TextBoxYnam, _draft.PickupSoundFormID)
            SetFidText(TextBoxZnam, _draft.DropSoundFormID)
            SetFidText(TextBoxEtyp, _draft.EquipTypeFormID)
            SetFidText(TextBoxBamt, _draft.AlternateBlockMaterialFormID)
            NumObndX1.Value = ClampDec(CDec(_draft.ObndX1), NumObndX1)
            NumObndY1.Value = ClampDec(CDec(_draft.ObndY1), NumObndY1)
            NumObndZ1.Value = ClampDec(CDec(_draft.ObndZ1), NumObndZ1)
            NumObndX2.Value = ClampDec(CDec(_draft.ObndX2), NumObndX2)
            NumObndY2.Value = ClampDec(CDec(_draft.ObndY2), NumObndY2)
            NumObndZ2.Value = ClampDec(CDec(_draft.ObndZ2), NumObndZ2)

            ' Damage Resist (DAMA): deep-copy into the working buffer, flushed on Apply.
            _damageResists.Clear()
            For Each dr In _draft.DamageResistances
                _damageResists.Add(New ARMO_DamageResist With {.DamageTypeFormID = dr.DamageTypeFormID, .Value = dr.Value})
            Next
            RefreshDamageGrid()

            ' Addons.
            _addons.Clear()
            For Each ad In _draft.ArmorAddons
                _addons.Add(New ARMO_AddonEntry With {.AddonIndex = ad.AddonIndex, .ArmaFormID = ad.ArmaFormID})
            Next
            RefreshAddonsGrid()

            ' Object Template (OBTS): deep-copy the combinations into the working buffer (never alias the parsed
            ' cache — the sub-editor + reorder mutate them live). Loading is not a user mutation, so it does NOT
            ' set CombinationsEdited.
            _combinations.Clear()
            _combinations.AddRange(ArmoDraft.CloneCombinations(_draft.Combinations))
            RefreshCombinationsGrid()

            ' Keywords + APPR: copy into the working buffers (flushed on Apply, like _addons).
            _keywords.Clear()
            _keywords.AddRange(_draft.KeywordFormIDs)
            _appr.Clear()
            _appr.AddRange(_draft.AttachParentSlotFormIDs)
            RefreshKwdaList()
            RefreshApprList()

            ' World Model & Material.
            TextBoxMod2.Text = _draft.MaleWorldModelPath
            TextBoxMod4.Text = _draft.FemaleWorldModelPath
            SetFidText(TextBoxMo2s, _draft.MaleMaterialSwapFormID)
            SetFidText(TextBoxMo4s, _draft.FemaleMaterialSwapFormID)
        Finally
            _loading = False
        End Try
    End Sub

    ' =====================================================================
    ' Apply (commit panels → draft + register)
    ' =====================================================================

    ''' <summary>OK: commit + validate the panels into the draft; on success finalize (DialogResult.OK, close).
    ''' On validation failure CommitPanelsToDraft already showed the message and returned False → stay open. The
    ''' live preview already reflects the committed draft, so no extra re-render is needed.</summary>
    Private Sub OnOk(sender As Object, e As EventArgs)
        If CommitPanelsToDraft(validate:=True) Then
            ' Set DialogResult.OK BEFORE Close() so the ensuing FormClosing sees OK and does NOT
            ' revert/discard the draft we just finalized.
            DialogResult = DialogResult.OK
            Close()
        End If
    End Sub

    ''' <summary>Cancel: just mark the result Cancel and close — <see cref="ArmoEditor_Form_FormClosing"/> does the
    ''' revert/discard of the current (not-OK'd) draft. Centralizing it there means the window X does the same
    ''' thing as this button.</summary>
    Private Sub OnCancel(sender As Object, e As EventArgs)
        DialogResult = DialogResult.Cancel
        Close()
    End Sub

    ''' <summary>Clean up the CURRENT draft when the editor is abandoned WITHOUT an OK — either switching to
    ''' another intent action, or closing via Cancel / the window X. A draft that PRE-EXISTED this editor
    ''' (<see cref="_editingExistingDraft"/>: the editDraft ctor arg or the "Edit draft…" action) is REVERTED by
    ''' re-registering its open-time snapshot (RegisterArmoDraft replaces by FormID) — never unregistered, it
    ''' existed before us. A SESSION-created draft (New / New-from-template / Override, not yet finalized) is
    ''' UNregistered outright (discarded). Guards against a missing draft/snapshot.</summary>
    Private Sub RevertOrDiscardCurrentDraft()
        If _draft Is Nothing Then Return
        If _editingExistingDraft Then
            If _openSnapshot IsNot Nothing Then _mainForm.RegisterArmoDraft(_openSnapshot)
        Else
            _mainForm.UnregisterArmoDraft(_draft.FormID)
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
        ' Flush any in-progress INDX cell edit into the model first.
        GridAddons.EndEdit()

        Dim edid = If(_draft.IsNew, EditorIdField.Compose(_edidPrefix, TextBoxEdid.Text), TextBoxEdid.Text.Trim())
        If validate Then
            If edid.Length = 0 OrElse (_draft.IsNew AndAlso TextBoxEdid.Text.Trim().Length = 0) Then
                MessageBox.Show(Me, "Enter an EditorID for the ARMO.", "Apply", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return False
            End If
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

        ' General.
        _draft.FullName = TextBoxFull.Text.Trim()
        _draft.RaceFormID = GetFid(TextBoxRace)
        _draft.InstanceNamingFormID = GetFid(TextBoxInnr)
        _draft.EnchantmentFormID = GetFid(TextBoxEitm)
        _draft.PatternFormID = GetFid(TextBoxPtrn)
        _draft.NonPlayable = CheckBoxNonPlayable.Checked
        _draft.Description = TextBoxDesc.Text.Trim()
        _draft.SlotMask = ReadSlotChecks()
        _draft.Value = CInt(NumValue.Value)
        _draft.Weight = CSng(NumWeight.Value)
        _draft.Health = CUInt(NumHealth.Value)
        _draft.ArmorRating = CUShort(NumArmorRating.Value)
        _draft.BaseAddonIndex = CUShort(NumBaseAddonIndex.Value)
        _draft.StaggerRating = CByte(NumStaggerRating.Value)

        ' Misc & Sounds.
        _draft.PickupSoundFormID = GetFid(TextBoxYnam)
        _draft.DropSoundFormID = GetFid(TextBoxZnam)
        _draft.EquipTypeFormID = GetFid(TextBoxEtyp)
        _draft.AlternateBlockMaterialFormID = GetFid(TextBoxBamt)
        _draft.ObndX1 = CShort(NumObndX1.Value)
        _draft.ObndY1 = CShort(NumObndY1.Value)
        _draft.ObndZ1 = CShort(NumObndZ1.Value)
        _draft.ObndX2 = CShort(NumObndX2.Value)
        _draft.ObndY2 = CShort(NumObndY2.Value)
        _draft.ObndZ2 = CShort(NumObndZ2.Value)

        ' Damage Resist (DAMA): flush the working buffer (deep-copied so the draft never aliases the grid).
        _draft.DamageResistances.Clear()
        For Each dr In _damageResists
            _draft.DamageResistances.Add(New ARMO_DamageResist With {.DamageTypeFormID = dr.DamageTypeFormID, .Value = dr.Value})
        Next

        ' Addons (order matters — copy the working list in row order).
        _draft.ArmorAddons.Clear()
        For Each ad In _addons
            _draft.ArmorAddons.Add(New ARMO_AddonEntry With {.AddonIndex = ad.AddonIndex, .ArmaFormID = ad.ArmaFormID})
        Next

        ' Object Template (OBTS): flush the working buffer back into the draft (deep-copied so the draft never
        ' aliases the grid's live-edited instances). The CombinationsEdited flag is set by the mutation handlers,
        ' not here. The current OVERRIDE save preserves the source OBTS bytes and ignores this list; the NEW-record
        ' writer serializes it (EmitArmoObjectTemplate) — this flush feeds both the preview and that writer.
        _draft.Combinations.Clear()
        _draft.Combinations.AddRange(ArmoDraft.CloneCombinations(_combinations))

        ' World Model & Material.
        _draft.MaleWorldModelPath = TextBoxMod2.Text.Trim()
        _draft.FemaleWorldModelPath = TextBoxMod4.Text.Trim()
        _draft.MaleMaterialSwapFormID = GetFid(TextBoxMo2s)
        _draft.FemaleMaterialSwapFormID = GetFid(TextBoxMo4s)

        ' Keywords + APPR: flush the working buffers (mutated by the Add/Remove handlers) into the draft.
        _draft.KeywordFormIDs.Clear()
        _draft.KeywordFormIDs.AddRange(_keywords)
        _draft.AttachParentSlotFormIDs.Clear()
        _draft.AttachParentSlotFormIDs.AddRange(_appr)

        ' Dirty only on a REAL change (mirror of ArmaEditor). The preview commits the panels on every render, so
        ' setting IsModified unconditionally re-emitted an identical override at save time (e.g. opening an ARMO
        ' just to edit its ARMA marked the ARMO dirty). Compare content against the open-time snapshot instead;
        ' OBTS edits aren't part of ContentEquals (the override save preserves source bytes) so OR in the separate
        ' CombinationsEdited flag. Two-way so reverting a change clears it; NEW drafts are always dirty.
        If Not _draft.IsNew Then
            _draft.IsModified = (_openSnapshot Is Nothing) OrElse _draft.CombinationsEdited OrElse Not _draft.ContentEquals(_openSnapshot)
        End If
        _mainForm.RegisterArmoDraft(_draft)
        Return True
    End Function

    ' =====================================================================
    ' General tab — race picker
    ' =====================================================================

    Private Sub OnPickRace(sender As Object, e As EventArgs)
        PickFidInto(TextBoxRace, {"RACE"}, "Select Race (RNAM)", allowNull:=True)
    End Sub

    ''' <summary>INRD Instance Naming picker (filtered to INNR records; NULL clears it).</summary>
    Private Sub OnPickInnr(sender As Object, e As EventArgs)
        PickFidInto(TextBoxInnr, {"INNR"}, "Select Instance Naming (INRD)", allowNull:=True)
    End Sub

    ''' <summary>EITM Object Effect picker ([ENCH]; NULL clears it).</summary>
    Private Sub OnPickEitm(sender As Object, e As EventArgs)
        PickFidInto(TextBoxEitm, {"ENCH"}, "Select Object Effect (EITM)", allowNull:=True)
    End Sub

    ''' <summary>PTRN Preview Transform picker ([TRNS]; NULL clears it).</summary>
    Private Sub OnPickPtrn(sender As Object, e As EventArgs)
        PickFidInto(TextBoxPtrn, {"TRNS"}, "Select Preview Transform (PTRN)", allowNull:=True)
    End Sub

    ' =====================================================================
    ' Misc & Sounds tab — YNAM / ZNAM / ETYP / BAMT pickers + OBND recompute
    ' =====================================================================

    ''' <summary>YNAM Pickup Sound picker ([SNDR]; NULL clears it).</summary>
    Private Sub OnPickYnam(sender As Object, e As EventArgs)
        PickFidInto(TextBoxYnam, {"SNDR"}, "Select Pickup Sound (YNAM)", allowNull:=True)
    End Sub

    ''' <summary>ZNAM Drop Sound picker ([SNDR]; NULL clears it).</summary>
    Private Sub OnPickZnam(sender As Object, e As EventArgs)
        PickFidInto(TextBoxZnam, {"SNDR"}, "Select Drop Sound (ZNAM)", allowNull:=True)
    End Sub

    ''' <summary>ETYP Equip Type picker ([EQUP]; NULL clears it).</summary>
    Private Sub OnPickEtyp(sender As Object, e As EventArgs)
        PickFidInto(TextBoxEtyp, {"EQUP"}, "Select Equip Type (ETYP)", allowNull:=True)
    End Sub

    ''' <summary>BAMT Alternate Block Material picker ([MATT]; NULL clears it).</summary>
    Private Sub OnPickBamt(sender As Object, e As EventArgs)
        PickFidInto(TextBoxBamt, {"MATT"}, "Select Block Material (BAMT)", allowNull:=True)
    End Sub

    ''' <summary>"Recompute from mesh" → approximate the Object Bounds (OBND) AABB from the male world model
    ''' (MOD2) mesh vertices. NOT identical to the CK's authored value (a conservative floor/ceil over every
    ''' vertex of every shape); editable afterwards. Never crashes — every failure surfaces as a MessageBox.</summary>
    Private Sub OnRecomputeObnd(sender As Object, e As EventArgs)
        Try
            Dim path = TextBoxMod2.Text.Trim()
            If path.Length = 0 Then
                MessageBox.Show(Me, "This ARMO has no world model (MOD2) to compute bounds from.", "Recompute bounds",
                                MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim key = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(path)
            Dim loc As FilesDictionary_class.File_Location = Nothing
            If Not FilesDictionary_class.Dictionary.TryGetValue(key, loc) OrElse loc Is Nothing Then
                MessageBox.Show(Me, $"Mesh not found: {path}", "Recompute bounds", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            Dim bytes = loc.GetBytes()
            If bytes Is Nothing OrElse bytes.Length = 0 Then
                MessageBox.Show(Me, $"Mesh not found: {path}", "Recompute bounds", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            Dim nif As New Nifcontent_Class_Manolo()
            nif.Load_Manolo(bytes)
            Dim shapes = NifRenderableShape.FromNif(nif)

            Dim minX As Single = Single.MaxValue, minY As Single = Single.MaxValue, minZ As Single = Single.MaxValue
            Dim maxX As Single = Single.MinValue, maxY As Single = Single.MinValue, maxZ As Single = Single.MinValue
            Dim any As Boolean = False
            If shapes IsNot Nothing Then
                For Each shape In shapes
                    If shape Is Nothing OrElse shape.Geometry Is Nothing Then Continue For
                    For Each v In shape.Geometry.GetVertexPositions()
                        any = True
                        If v.X < minX Then minX = v.X
                        If v.Y < minY Then minY = v.Y
                        If v.Z < minZ Then minZ = v.Z
                        If v.X > maxX Then maxX = v.X
                        If v.Y > maxY Then maxY = v.Y
                        If v.Z > maxZ Then maxZ = v.Z
                    Next
                Next
            End If
            If Not any Then
                MessageBox.Show(Me, "No vertices found in mesh.", "Recompute bounds", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            NumObndX1.Value = ClampS16(Math.Floor(minX))
            NumObndY1.Value = ClampS16(Math.Floor(minY))
            NumObndZ1.Value = ClampS16(Math.Floor(minZ))
            NumObndX2.Value = ClampS16(Math.Ceiling(maxX))
            NumObndY2.Value = ClampS16(Math.Ceiling(maxY))
            NumObndZ2.Value = ClampS16(Math.Ceiling(maxZ))
            OnFieldEdited(Me, EventArgs.Empty)
        Catch ex As Exception
            MessageBox.Show(Me, $"Could not compute bounds: {ex.Message}", "Recompute bounds",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>Clamp a computed bound to the signed-16 OBND range [-32768, 32767] as a Decimal for the numerics.</summary>
    Private Shared Function ClampS16(v As Double) As Decimal
        If v < -32768.0R Then Return -32768D
        If v > 32767.0R Then Return 32767D
        Return CDec(v)
    End Function

    ' =====================================================================
    ' Damage Resist tab — DAMA (DMGT FormID + Value)
    ' =====================================================================

    ''' <summary>Repaint the Damage Resist grid from <see cref="_damageResists"/> (read-only summary rows; a row is
    ''' edited in the modal sub-editor). Preserves the selected row index across the refresh.</summary>
    Private Sub RefreshDamageGrid()
        Dim selIdx = If(GridDamage.CurrentRow IsNot Nothing, GridDamage.CurrentRow.Index, -1)
        GridDamage.Rows.Clear()
        For Each dr In _damageResists
            GridDamage.Rows.Add($"{DisplayFor(dr.DamageTypeFormID)} [0x{dr.DamageTypeFormID:X8}]",
                                dr.Value.ToString(CultureInfo.InvariantCulture))
        Next
        If selIdx >= 0 AndAlso selIdx < GridDamage.Rows.Count Then
            GridDamage.Rows(selIdx).Selected = True
            GridDamage.CurrentCell = GridDamage.Rows(selIdx).Cells(0)
        End If
    End Sub

    Private Function SelectedDamageIndex() As Integer
        If GridDamage.CurrentRow Is Nothing Then Return -1
        Dim i = GridDamage.CurrentRow.Index
        If i < 0 OrElse i >= _damageResists.Count Then Return -1
        Return i
    End Function

    ''' <summary>Add → open the modal on a fresh entry; on OK append the (deep-copied) result.</summary>
    Private Sub OnAddDamage(sender As Object, e As EventArgs)
        Using dlg As New ArmoDamageResistEditor_Form(_mainForm, New ARMO_DamageResist())
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultEntry IsNot Nothing Then
                _damageResists.Add(dlg.ResultEntry)
                RefreshDamageGrid()
                OnFieldEdited(Me, EventArgs.Empty)
            End If
        End Using
    End Sub

    Private Sub OnEditDamage(sender As Object, e As EventArgs)
        EditDamageAt(SelectedDamageIndex())
    End Sub

    Private Sub OnDamageDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        EditDamageAt(e.RowIndex)
    End Sub

    ''' <summary>Open the modal sub-editor on the entry at <paramref name="i"/> (deep-copied in/out by the modal);
    ''' on OK replace the row's entry with the edited result.</summary>
    Private Sub EditDamageAt(i As Integer)
        If i < 0 OrElse i >= _damageResists.Count Then Return
        Using dlg As New ArmoDamageResistEditor_Form(_mainForm, _damageResists(i))
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultEntry IsNot Nothing Then
                _damageResists(i) = dlg.ResultEntry
                RefreshDamageGrid()
                OnFieldEdited(Me, EventArgs.Empty)
            End If
        End Using
    End Sub

    Private Sub OnRemoveDamage(sender As Object, e As EventArgs)
        Dim i = SelectedDamageIndex()
        If i < 0 Then Return
        _damageResists.RemoveAt(i)
        RefreshDamageGrid()
        OnFieldEdited(Me, EventArgs.Empty)
    End Sub

    ' =====================================================================
    ' Addons tab
    ' =====================================================================

    Private Sub RefreshAddonsGrid()
        Dim selIdx = If(GridAddons.CurrentRow IsNot Nothing, GridAddons.CurrentRow.Index, -1)
        GridAddons.Rows.Clear()
        For Each ad In _addons
            GridAddons.Rows.Add(ad.AddonIndex.ToString(CultureInfo.InvariantCulture),
                                $"{DisplayFor(ad.ArmaFormID)} [0x{ad.ArmaFormID:X8}]",
                                EffectiveSlotsText(ad.ArmaFormID))
        Next
        If selIdx >= 0 AndAlso selIdx < GridAddons.Rows.Count Then
            GridAddons.Rows(selIdx).Selected = True
            GridAddons.CurrentCell = GridAddons.Rows(selIdx).Cells(0)
        End If
    End Sub

    ''' <summary>The effective slot footprint shown read-only for an addon row: the ARMA's own BOD2 mask, or
    ''' the editing ARMO's BOD2 when the ARMA declares none (<see cref="MainForm.EffectiveArmaSlotMask"/>
    ''' semantics). Drafts resolve via the draft-aware parsed view.</summary>
    Private Function EffectiveSlotsText(armaFid As UInteger) As String
        If armaFid = 0UI Then Return ""
        Dim arma = _mainForm.GetParsedArmaForEditor(armaFid)
        Dim mask As UInteger = If(arma IsNot Nothing AndAlso arma.SlotMask <> 0UI, arma.SlotMask, ReadSlotChecks())
        Return SlotsToText(mask)
    End Function

    ''' <summary>Compact "30,33,41" listing of the biped slot numbers set in <paramref name="mask"/>
    ''' (bit N = slot N+30).</summary>
    Private Shared Function SlotsToText(mask As UInteger) As String
        Dim slots As New List(Of String)
        For bit = 0 To 31
            If (mask And (1UI << bit)) <> 0UI Then slots.Add((bit + 30).ToString(CultureInfo.InvariantCulture))
        Next
        Return String.Join(", ", slots)
    End Function

    ''' <summary>Add ARMA → FormIdPicker over ARMA (+ ARMA drafts) → append a row with default INDX 0.</summary>
    Private Sub OnAddArma(sender As Object, e As EventArgs)
        Dim drafts = _mainForm.ArmaDrafts().Select(Function(d) New FormIdPickerEntry With {
            .FormID = d.FormID, .EditorID = d.EditorID, .DisplayName = d.EditorID, .Signature = "ARMA"}).ToList()
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"ARMA"},
                                           "Add Armor Addon (ARMA)", 0UI, allowNull:=False,
                                           extraDraftEntries:=drafts,
                                           formIdFilter:=Function(fid) _mainForm.IsArmaRaceCompatible(fid, _raceFormID))
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            _addons.Add(New ARMO_AddonEntry With {.AddonIndex = 0US, .ArmaFormID = dlg.SelectedFormID})
            RefreshAddonsGrid()
            OnFieldEdited(Me, EventArgs.Empty)
        End Using
    End Sub

    ''' <summary>Edit the selected addon row (Addon Index + ARMA reference) in the modal
    ''' <see cref="ArmoAddonEditor_Form"/>.</summary>
    Private Sub OnEditAddon(sender As Object, e As EventArgs)
        EditAddonAt(SelectedAddonIndex())
    End Sub

    ''' <summary>Open the addon-entry modal on the row at <paramref name="i"/> (deep-copied in/out); on OK
    ''' replace the row's entry and refresh Name/Slots. Safe: the grid is read-only ⇒ no cell in edit mode ⇒
    ''' no reentrant <c>SetCurrentCellAddressCore</c>.</summary>
    Private Sub EditAddonAt(i As Integer)
        If i < 0 OrElse i >= _addons.Count Then Return
        Using dlg As New ArmoAddonEditor_Form(_mainForm, _draft.RaceFormID, _addons(i))
            Dim ok = (dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultEntry IsNot Nothing)
            If ok Then
                _addons(i) = dlg.ResultEntry
                OnFieldEdited(Me, EventArgs.Empty)
            End If
            ' Refresh regardless of OK/Cancel: the addon modal's "Edit ARMA…" can change the referenced ARMA
            ' draft's slots/name even when the addon edit itself is cancelled, so the row's rendered Name/Slots
            ' (draft-aware via GetParsedArmaForEditor) could otherwise show a stale pre-edit snapshot.
            RefreshAddonsGrid()
        End Using
    End Sub

    Private Sub OnRemoveAddon(sender As Object, e As EventArgs)
        Dim i = SelectedAddonIndex()
        If i < 0 Then Return
        _addons.RemoveAt(i)
        RefreshAddonsGrid()
        OnFieldEdited(Me, EventArgs.Empty)
    End Sub

    ''' <summary>Move the selected addon row up (delta=-1) or down (delta=+1); order is preserved into the draft.</summary>
    Private Sub MoveAddon(delta As Integer)
        Dim i = SelectedAddonIndex()
        If i < 0 Then Return
        Dim j = i + delta
        If j < 0 OrElse j >= _addons.Count Then Return
        Dim tmp = _addons(i)
        _addons(i) = _addons(j)
        _addons(j) = tmp
        RefreshAddonsGrid()
        If j < GridAddons.Rows.Count Then
            GridAddons.Rows(j).Selected = True
            GridAddons.CurrentCell = GridAddons.Rows(j).Cells(0)
        End If
        OnFieldEdited(Me, EventArgs.Empty)
    End Sub

    ''' <summary>Double-click a row → edit that addon entry (Addon Index + ARMA reference) in the modal
    ''' <see cref="ArmoAddonEditor_Form"/>. Same modal as the Edit button — the grid stays read-only.</summary>
    Private Sub OnAddonDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        EditAddonAt(e.RowIndex)
    End Sub

    Private Function SelectedAddonIndex() As Integer
        If GridAddons.CurrentRow Is Nothing Then Return -1
        Dim i = GridAddons.CurrentRow.Index
        If i < 0 OrElse i >= _addons.Count Then Return -1
        Return i
    End Function

    ' =====================================================================
    ' Object Template tab — OBTS combinations
    ' =====================================================================

    ''' <summary>Repaint the combinations grid from <see cref="_combinations"/> (read-only summary rows; a row is
    ''' edited in the modal sub-editor). Preserves the selected row index across the refresh.</summary>
    Private Sub RefreshCombinationsGrid()
        Dim selIdx = If(GridCombinations.CurrentRow IsNot Nothing, GridCombinations.CurrentRow.Index, -1)
        GridCombinations.Rows.Clear()
        For i = 0 To _combinations.Count - 1
            Dim c = _combinations(i)
            Dim name = If(String.IsNullOrEmpty(c.DisplayName), "(unnamed)", c.DisplayName)
            GridCombinations.Rows.Add((i + 1).ToString(CultureInfo.InvariantCulture),
                                      name,
                                      If(c.IsDefault, "Yes", ""),
                                      c.ParentCombinationIndex.ToString(CultureInfo.InvariantCulture),
                                      c.LevelMin.ToString(CultureInfo.InvariantCulture),
                                      c.LevelMax.ToString(CultureInfo.InvariantCulture),
                                      c.Includes.Count.ToString(CultureInfo.InvariantCulture),
                                      c.Properties.Count.ToString(CultureInfo.InvariantCulture),
                                      c.Keywords.Count.ToString(CultureInfo.InvariantCulture),
                                      If(c.IsEditorOnly, "Yes", ""))
        Next
        If selIdx >= 0 AndAlso selIdx < GridCombinations.Rows.Count Then
            GridCombinations.Rows(selIdx).Selected = True
            GridCombinations.CurrentCell = GridCombinations.Rows(selIdx).Cells(0)
        End If
    End Sub

    Private Function SelectedComboIndex() As Integer
        If GridCombinations.CurrentRow Is Nothing Then Return -1
        Dim i = GridCombinations.CurrentRow.Index
        If i < 0 OrElse i >= _combinations.Count Then Return -1
        Return i
    End Function

    ''' <summary>Any OBTS mutation → flag the draft as OBTS-edited (consumed by the future save-override phase; it
    ''' does NOT affect the current save) and request a debounced live preview.</summary>
    Private Sub MarkCombinationsEdited()
        If _draft IsNot Nothing Then _draft.CombinationsEdited = True
        OnFieldEdited(Me, EventArgs.Empty)
    End Sub

    ''' <summary>Add → open the sub-editor on a fresh combination; on OK append the returned (deep-copied) result.</summary>
    Private Sub OnAddCombo(sender As Object, e As EventArgs)
        Using dlg As New ObtsCombinationEditor_Form(_mainForm, New ARMO_Combination())
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultCombination IsNot Nothing Then
                _combinations.Add(dlg.ResultCombination)
                RefreshCombinationsGrid()
                MarkCombinationsEdited()
            End If
        End Using
    End Sub

    Private Sub OnRemoveCombo(sender As Object, e As EventArgs)
        Dim i = SelectedComboIndex()
        If i < 0 Then Return
        _combinations.RemoveAt(i)
        RefreshCombinationsGrid()
        MarkCombinationsEdited()
    End Sub

    ''' <summary>Duplicate → deep-copy the selected combination and insert the copy right after it.</summary>
    Private Sub OnDuplicateCombo(sender As Object, e As EventArgs)
        Dim i = SelectedComboIndex()
        If i < 0 Then Return
        Dim copy = ArmoDraft.CloneCombinations(New List(Of ARMO_Combination) From {_combinations(i)})(0)
        _combinations.Insert(i + 1, copy)
        RefreshCombinationsGrid()
        MarkCombinationsEdited()
    End Sub

    Private Sub MoveCombo(delta As Integer)
        Dim i = SelectedComboIndex()
        If i < 0 Then Return
        Dim j = i + delta
        If j < 0 OrElse j >= _combinations.Count Then Return
        Dim tmp = _combinations(i)
        _combinations(i) = _combinations(j)
        _combinations(j) = tmp
        RefreshCombinationsGrid()
        If j < GridCombinations.Rows.Count Then
            GridCombinations.Rows(j).Selected = True
            GridCombinations.CurrentCell = GridCombinations.Rows(j).Cells(0)
        End If
        MarkCombinationsEdited()
    End Sub

    Private Sub OnEditCombo(sender As Object, e As EventArgs)
        EditComboAt(SelectedComboIndex())
    End Sub

    Private Sub OnComboDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        EditComboAt(e.RowIndex)
    End Sub

    ''' <summary>Open the modal sub-editor on the combination at <paramref name="i"/> (deep-copied in/out by the
    ''' sub-editor); on OK replace the row's combination with the edited result.</summary>
    Private Sub EditComboAt(i As Integer)
        If i < 0 OrElse i >= _combinations.Count Then Return
        Using dlg As New ObtsCombinationEditor_Form(_mainForm, _combinations(i))
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultCombination IsNot Nothing Then
                _combinations(i) = dlg.ResultCombination
                RefreshCombinationsGrid()
                MarkCombinationsEdited()
            End If
        End Using
    End Sub

    ''' <summary>Render-relevant signature of the OBTS combinations for the preview debounce key — covers the
    ''' fields that change what the object template applies (default flag, addon-index selector, OMOD includes and
    ''' the direct property overrides, incl. FormID-typed Value1 material swaps). Editing OBTS thus re-renders.</summary>
    Private Function CombinationsKey() As String
        Dim parts As New List(Of String)
        For Each c In _combinations
            Dim incl = String.Join("|", c.Includes.Select(Function(i) i.ModFormID.ToString("X8") & ":" & i.AttachPointIndex.ToString(CultureInfo.InvariantCulture)))
            Dim props = String.Join("|", c.Properties.Select(Function(p) $"{CInt(p.ValueType)}/{p.FunctionType}/{p.PropertyIndex}/{p.Value1FormID:X8}/{BitConverter.ToInt32(BitConverter.GetBytes(p.Value1), 0)}/{BitConverter.ToInt32(BitConverter.GetBytes(p.Value2), 0)}"))
            parts.Add($"{If(c.IsDefault, 1, 0)};{c.ParentCombinationIndex};{incl};{props}")
        Next
        Return String.Join("~", parts)
    End Function

    ''' <summary>Preview-key fragment for one addon row: the ARMA FormID + index PLUS a content signature of the
    ''' addon ARMA's referenced MSWP DRAFTS. An addon ARMA's material-swap draft keeps its FormID across an edit,
    ''' so without this the ARMO preview key wouldn't change and the swap wouldn't re-render.</summary>
    Private Function AddonPreviewKey(a As ARMO_AddonEntry) As String
        Dim mswpSig As String = ""
        Dim arma = _mainForm.GetParsedArmaForEditor(a.ArmaFormID)
        If arma IsNot Nothing Then
            mswpSig = _mainForm.GetMswpDraftSignature(arma.MaleMaterialSwapFormID) & "/" & _mainForm.GetMswpDraftSignature(arma.FemaleMaterialSwapFormID)
        End If
        Return a.ArmaFormID.ToString("X8") & "#" & a.AddonIndex.ToString(CultureInfo.InvariantCulture) & "@" & mswpSig
    End Function

    ' =====================================================================
    ' Keywords tab — KWDA + APPR (both KYWD FormIDs)
    ' =====================================================================

    Private Sub OnAddKwda(sender As Object, e As EventArgs)
        ' KWDA = general armor keywords → EXCLUDE attach-point keywords (those belong in APPR, not here). By type
        ' (KYWD.TNAM), not a name heuristic; "Show all" escapes the filter.
        AddKywdInto(_keywords, "Add keyword (KWDA)", Function(fid) Not _mainForm.IsAttachPointKeyword(fid))
        RefreshKwdaList()
    End Sub

    Private Sub OnRemoveKwda(sender As Object, e As EventArgs)
        If ListKwda.SelectedItems.Count = 0 Then Return
        Dim fid = CUInt(ListKwda.SelectedItems(0).Tag)
        _keywords.Remove(fid)
        RefreshKwdaList()
    End Sub

    Private Sub OnAddAppr(sender As Object, e As EventArgs)
        ' APPR entries are ATTACH-POINT keywords — filtered by the AUTHORITATIVE KYWD.TNAM Type == 'Attach Point'
        ' (not a name heuristic), with the picker's "Show all" checkbox to escape the filter if needed.
        AddKywdInto(_appr, "Add attach-parent-slot (APPR)", AddressOf _mainForm.IsAttachPointKeyword)
        RefreshApprList()
    End Sub

    Private Sub OnRemoveAppr(sender As Object, e As EventArgs)
        If ListAppr.SelectedItems.Count = 0 Then Return
        Dim fid = CUInt(ListAppr.SelectedItems(0).Tag)
        _appr.Remove(fid)
        RefreshApprList()
    End Sub

    ''' <summary>FormIdPicker over KYWD → add the chosen FormID to <paramref name="list"/> (dedup). Both KWDA
    ''' and APPR are KYWD FormID lists, so they share this helper. <paramref name="formIdFilter"/> (optional)
    ''' narrows the list by KYWD.TNAM type (APPR → only Attach Point; KWDA → exclude Attach Point) with the
    ''' picker's "Show all" override.</summary>
    Private Sub AddKywdInto(list As List(Of UInteger), title As String, Optional formIdFilter As Func(Of UInteger, Boolean) = Nothing)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"KYWD"}, title, 0UI, allowNull:=False,
                                           formIdFilter:=formIdFilter)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            If Not list.Contains(dlg.SelectedFormID) Then list.Add(dlg.SelectedFormID)
        End Using
    End Sub

    Private Sub RefreshKwdaList()
        RefreshFidList(ListKwda, _keywords)
    End Sub

    Private Sub RefreshApprList()
        RefreshFidList(ListAppr, _appr)
    End Sub

    Private Sub RefreshFidList(lv As ListView, fids As List(Of UInteger))
        lv.BeginUpdate()
        Try
            lv.Items.Clear()
            For Each fid In fids
                Dim row As New ListViewItem($"{DisplayFor(fid)} [0x{fid:X8}]")
                row.Tag = fid
                lv.Items.Add(row)
            Next
        Finally
            lv.EndUpdate()
        End Try
    End Sub

    ' =====================================================================
    ' World Model & Material tab — mesh browse + material swap
    ' =====================================================================

    Private Sub BrowseMeshInto(target As TextBox)
        Dim exts As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {".nif"}
        Dim keys = FilesDictionary_class.GetFilteredKeys(MeshesPrefix, exts)
        ' MeshPicker_Form = the preview-enabled .nif picker (live GL render of the selected mesh).
        Using dlg As New MeshPicker_Form(keys, MeshesPrefix, exts, target.Text.Trim())
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                Dim sel = dlg.SelectedKey
                ' The picker key carries the "Meshes\" root prefix, but ARMO world-model fields store the path
                ' RELATIVE to Meshes\ (the render re-adds it idempotently, so preview still works, but the
                ' SAVED record must be prefix-free or the engine can't find the mesh).
                If Not String.IsNullOrEmpty(sel) Then target.Text = sel.StripPrefix(MeshesPrefix)
            End If
        End Using
    End Sub

    Private Sub PickMswpInto(target As TextBox)
        PickFidInto(target, {"MSWP"}, "Select material swap (MSWP)", allowNull:=True, includeMswpDrafts:=True)
    End Sub

    ''' <summary>"New / Edit MSWP…" for a gender: if the gender's field already points at an MSWP DRAFT, edit
    ''' that one; otherwise create a fresh MswpDraft, register it, and set the field to it. The sub-editor's
    ''' Original-Material list now aggregates the materials of THIS gender's WORLD-MODEL mesh NIF (MOD2 male /
    ''' MOD4 female — the ARMO's own mesh) PLUS the gender-appropriate world-model mesh of every included ARMA
    ''' addon (MOD2 male / MOD3 female), read from the CURRENT <see cref="_addons"/> set at open time — so it
    ''' lists every real BGSM actually in play.</summary>
    Private Sub OnNewEditMswp(isFemaleGender As Boolean)
        Dim target = If(isFemaleGender, TextBoxMo4s, TextBoxMo2s)
        Dim meshPath = If(isFemaleGender, TextBoxMod4.Text.Trim(), TextBoxMod2.Text.Trim())
        Dim genderLabel = If(isFemaleGender, "Female", "Male")

        ' Gender-appropriate world-model mesh of every included ARMA addon (draft-aware parse), so the
        ' Original-Material list also lists the addon meshes' materials. Skip unresolved ARMAs / empty paths.
        Dim extraMeshPaths As New List(Of String)
        For Each ad In _addons
            Dim arma = _mainForm.GetParsedArmaForEditor(ad.ArmaFormID)
            If arma Is Nothing Then Continue For
            Dim armaMesh = If(isFemaleGender, arma.FemaleMeshPath, arma.MaleMeshPath)
            If Not String.IsNullOrWhiteSpace(armaMesh) Then extraMeshPaths.Add(armaMesh)
        Next

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

        Using dlg As New MswpSubEditor_Form(_mainForm, draft, meshPath, genderLabel, extraMeshPaths)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
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
    ' FormID picker plumbing (mirror ArmaEditor)
    ' =====================================================================

    ''' <summary>Open a FormIdPicker over <paramref name="sigs"/> seeded with the textbox's current FormID, and
    ''' write the chosen FormID (or 0/NULL) back as a "Name [0xFORMID]" display. MSWP fields additionally pass
    ''' the in-memory MSWP drafts so an unsaved swap is selectable.</summary>
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

    ' =====================================================================
    ' FormID textbox helpers ("Name [0xFORMID]"; Tag carries the raw UInteger)
    ' =====================================================================

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
    ' Slot checkbox helpers (shared builder)
    ' =====================================================================

    Private Sub SetSlotChecks(mask As UInteger)
        BipedSlotCheckboxes.SetMask(_slotChecks, mask)
    End Sub

    Private Function ReadSlotChecks() As UInteger
        Return BipedSlotCheckboxes.ReadMask(_slotChecks)
    End Function

    ''' <summary>"Recalculate from ARMA addons" → set the BOD2 slot checkboxes to the UNION of the biped-slot
    ''' masks of every included ARMA addon (draft-aware parse). No addons → leave the current checks untouched
    ''' and inform the user. Fires the same edited-notification the checkboxes raise so the banner/preview update.</summary>
    Private Sub OnRecalcSlotsFromArma(sender As Object, e As EventArgs)
        If _addons.Count = 0 Then
            MessageBox.Show(Me, "This armor has no ARMA addons to recalculate slots from.", "Recalculate slots",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim mask As UInteger = 0UI
        For Each ad In _addons
            Dim arma = _mainForm.GetParsedArmaForEditor(ad.ArmaFormID)
            If arma IsNot Nothing Then mask = mask Or arma.SlotMask
        Next
        SetSlotChecks(mask)
        ' Addon rows whose ARMA declares no slots render the ARMO's own mask (EffectiveSlotsText fallback), so
        ' refresh the grid too now that the mask changed.
        RefreshAddonsGrid()
        OnFieldEdited(Me, EventArgs.Empty)
    End Sub

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
    ' WYSIWYG preview — wrap the ARMO draft DIRECTLY in a throwaway OTFT
    ' =====================================================================

    Private Async Function RenderPreviewAsync() As Task
        If _host Is Nothing OrElse _preview Is Nothing OrElse _preview.IsDisposed Then Return
        If _previewNpcFormID = 0UI Then Return

        ' Flush the current panel state into _draft AND register it (RegisterArmoDraft) BEFORE the preview key
        ' is built. The very first render (from _Shown / template-load) reaches here with _pendingApply=False,
        ' so without this the throwaway OTFT's item (the ARMO draft) is never registered → the draft-aware
        ' resolver (TryGetArmoDraft) returns Nothing → naked. validate:=False never early-returns, so the
        ' draft is always registered; the key is computed from _draft AFTER this so it stays correct.
        CommitPanelsToDraft(validate:=False)

        ' Key over EVERY render-relevant ARMO field; previously the material swaps + race were omitted, so
        ' editing them left a stale preview (key unchanged → early return).
        Dim key As String = String.Join(":", {
            _draft.FormID.ToString("X8"), _draft.SlotMask.ToString("X8"),
            _draft.RaceFormID.ToString("X8"),
            _draft.MaleWorldModelPath, _draft.FemaleWorldModelPath,
            _draft.MaleMaterialSwapFormID.ToString("X8"), _draft.FemaleMaterialSwapFormID.ToString("X8"),
            _mainForm.GetMswpDraftSignature(_draft.MaleMaterialSwapFormID), _mainForm.GetMswpDraftSignature(_draft.FemaleMaterialSwapFormID),
            String.Join(",", _addons.Select(AddressOf AddonPreviewKey)),
            CombinationsKey()})
        If key = _lastPreviewKey Then Return
        If _previewInProgress Then Return
        _previewInProgress = True
        Try
            Dim otft As New OutfitDraft With {.FormID = OutfitDraft.PreviewDraftFormID,
                                              .EditorID = OutfitDraft.EditorIdPrefix & "(armopreview)"}
            otft.ItemFormIDs.Add(_draft.FormID)
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

    ' =====================================================================
    ' Form lifecycle (preview host setup/teardown — mirror ArmaEditor)
    ' =====================================================================

    Private Sub ArmoEditor_Form_Shown(sender As Object, e As EventArgs) Handles Me.Shown
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

    Private Sub ArmoEditor_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        ' Not finalized (Cancel or the window X → DialogResult ≠ OK): revert/discard the current session draft so
        ' abandoned drafts don't accumulate. OK finalized the draft in OnOk (which set DialogResult.OK first), so
        ' this is skipped and the finalized draft survives.
        If DialogResult <> DialogResult.OK Then RevertOrDiscardCurrentDraft()

        ' Drop the throwaway preview draft so it never leaks into the save set / other pickers.
        If _previewDraftRegistered Then
            Try
                _mainForm.UnregisterOutfitDraft(OutfitDraft.PreviewDraftFormID)
            Catch
            End Try
            _previewDraftRegistered = False
        End If

        If _previewDebounce IsNot Nothing Then
            Try
                _previewDebounce.Stop()
            Catch
            End Try
        End If

        ' Quiesce render loop → host Dispose → control Clean/Dispose (same ordering as ArmaEditor/OutfitPicker).
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
