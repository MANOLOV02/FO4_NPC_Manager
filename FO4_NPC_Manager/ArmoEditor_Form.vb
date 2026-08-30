Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

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

    ''' <summary>The OTFT of the outfit being assembled in the OutfitPicker's Create tab, threaded in so this
    ''' editor's "Full Outfit" preview renders that whole outfit with THIS ARMO's edit substituted (draft-aware
    ''' resolver). Also re-passed to the ARMA editor (via ArmoAddonEditor) for its "Full Outfit" mode. 0 = no
    ''' outfit in context (standalone open) ⇒ "Full Outfit" falls back to the single-item throwaway.</summary>
    Private ReadOnly _outfitContextFormID As UInteger

    ''' <summary>The ARMO draft currently being edited (the authoring model). Always non-Nothing after the
    ''' constructor: either the passed-in editDraft, or a fresh empty draft seeded with the preview race.</summary>
    Private _draft As ArmoDraft
    ''' <summary>Suppresses preview re-render + dirty marking while panels are being LOADED programmatically.</summary>
    Private _loading As Boolean

    ''' <summary>True when authoring for Skyrim (SSE). Set by <see cref="ConfigureForGame"/> at construction. Drives
    ''' the armor-rating field binding (DNAM ×100 vs FO4 FNAM u16) and the hiding of FO4-only editor surfaces.</summary>
    Private _isSkyrim As Boolean

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

    ''' <summary>Las combinaciones del Object Template (OBTE/OBTS) del ARMO, en el orden de las filas. La
    ''' grilla "Object Template" es la vista editable de esta lista, y al aplicar se vuelca entera al record.
    ''' Cada elemento es una vista sobre <see cref="_comboHost"/>, nunca sobre el record de la caché.</summary>
    Private ReadOnly _combinations As New List(Of Canon.IBloque_Combinations)

    ''' <summary>El ARMO sobre el que viven las combinaciones que el usuario está armando. Es una COPIA del
    ''' record que se está editando: una vista no existe sin un nodo, y el nodo tiene que colgar de algún
    ''' árbol. Al ser copia del original hereda su contexto -entre otras cosas, si el archivo guarda los
    ''' textos en tablas de idioma-, y cancelar no le deja nada escrito al record de verdad.</summary>
    Private _comboHost As Canon.ArmoFO4 = Nothing

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
    ''' <summary>Ya se avisó que el volcado del preview falló. Se avisa UNA vez: el temporizador vuelve
    ''' a intentar en cada redibujo y un diálogo por tick es inusable.</summary>
    Private _previewCommitFallado As Boolean
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
    ''' OVERRIDE record; False → as a NEW record. Ignored when
    ''' no template FormID is supplied.</param>
    ''' <param name="outfitContextFormID">The OTFT of the outfit being assembled in the OutfitPicker. Enables
    ''' "Full Outfit" to render that outfit with this ARMO's edit substituted; also re-passed to the ARMA editor.
    ''' 0 (default, standalone open) ⇒ "Full Outfit" falls back to the single-item throwaway.</param>
    Public Sub New(mainForm As MainForm, previewNpcFormID As UInteger, raceFormID As UInteger, isFemale As Boolean,
                   Optional editDraft As ArmoDraft = Nothing, Optional initialTemplateArmoFormID As UInteger = 0UI,
                   Optional templateAsOverride As Boolean = True, Optional outfitContextFormID As UInteger = 0UI)
        InitializeComponent()
        _mainForm = mainForm
        _previewNpcFormID = previewNpcFormID
        _raceFormID = raceFormID
        _isFemale = isFemale
        _outfitContextFormID = outfitContextFormID

        BuildSlotCheckBoxes()
        BuildAddonsGridColumns()
        BuildCombinationsGridColumns()
        BuildDamageGridColumns()

        _previewDebounce = New Timer() With {.Interval = 400}

        ' Top bar — acciones de intencion explicita: Nuevo / Copia desde plantilla /
        ' Override / Editar draft.
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

        ' Game-gate the editor surface BEFORE loading panels (LoadDraftIntoPanels reads _isSkyrim to bind the
        ' armor-rating field to the right record field).
        ConfigureForGame()

        ' Seed the draft: edit the passed one, else a fresh empty draft (NEW) seeded with the preview race.
        If editDraft IsNot Nothing Then
            _draft = editDraft
            _editingExistingDraft = True   ' continuing to edit one of the user's registered drafts
        Else
            _draft = ArmoDraft.Nuevo(_mainForm.AllocateDraftFormID(),
                                     Canon.CanonBridge.SessionGame())
            _draft.Record.EditorID = ArmoDraft.EditorIdPrefix & "new"
            _draft.Record.Race = _raceFormID
        End If
        LoadDraftIntoPanels()

        ' Optional pre-load of a REAL ARMO template on open (Outfit Picker double-click). templateAsOverride
        ' chooses loading it as an OVERRIDE record (True) vs as a NEW record (False). Runs BEFORE
        ' the snapshot
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

    ''' <summary>Game-gate the editor UI. Under SKYRIM: relabel + rescale the Armor Rating field to DNAM (s32,
    ''' stored ×100, shown ÷100 with 2 decimals) and HIDE every FO4-only surface — a control the Skyrim
    ''' serializer never reads must not be on screen at all, since a visible-but-inert field reads as "this
    ''' setting applies and did nothing". Nothing here is merely disabled.
    ''' Hidden: FNAM (Base Addon Index / Stagger Rating), the DATA Health field, the Object Template (OBTS) tab,
    ''' the Damage Resist (DAMA) tab, the PTRN transform, the APPR attach-parent-slots block, the MO2S/MO4S
    ''' material-swap pickers (Skyrim's MO2S/MO4S are Alternate-Textures arrays, not an MSWP FormID — they are
    ''' preserved verbatim, never authored here), and the Armature grid's INDX column (Skyrim's armature is a
    ''' plain MODL list with no INDX). FO4 is unchanged.</summary>
    Private Sub ConfigureForGame()
        _isSkyrim = (Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
        If Not _isSkyrim Then Return
        LabelArmorRating.Text = "Armor Rating (DNAM):"
        NumArmorRating.DecimalPlaces = 2
        LabelHealth.Visible = False : NumHealth.Visible = False
        LabelBaseAddonIndex.Visible = False : NumBaseAddonIndex.Visible = False
        LabelStaggerRating.Visible = False : NumStaggerRating.Visible = False
        ' PTRN (Transform) — FO4-only.
        LabelPtrn.Visible = False : TextBoxPtrn.Visible = False : ButtonPickPtrn.Visible = False
        ' APPR (Attach Parent Slots) — FO4-only.
        LabelAppr.Visible = False : ListAppr.Visible = False : ApprButtons.Visible = False
        ' MO2S / MO4S material swaps — FO4-only (Skyrim uses Alternate Textures, preserved verbatim).
        LabelMo2s.Visible = False : TextBoxMo2s.Visible = False
        ButtonPickMo2s.Visible = False : ButtonEditMo2s.Visible = False
        LabelMo4s.Visible = False : TextBoxMo4s.Visible = False
        ButtonPickMo4s.Visible = False : ButtonEditMo4s.Visible = False
        ' Armature grid: INDX exists only in the Fallout 4 entry (INDX + MODL). Skyrim's Armature is a plain
        ' RArray of MODL FormIDs. Hidden here (not in BuildAddonsGridColumns) because the columns are built
        ' before this runs.
        If GridAddons.Columns.Count > 0 Then GridAddons.Columns(0).Visible = False
        If Tabs.TabPages.Contains(TabObts) Then Tabs.TabPages.Remove(TabObts)
        If Tabs.TabPages.Contains(TabDamage) Then Tabs.TabPages.Remove(TabDamage)
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
    ' Explicit intent actions + status banner
    ' =====================================================================

    ''' <summary>"New (blank)" → a fresh empty New-record draft (new draft FormID, npcm_ prefix, IsNew=True,
    ''' IsOverride=False), seeded with the preview race. A brand-new record from scratch.</summary>
    Private Sub OnActionNewBlank(sender As Object, e As EventArgs)
        RevertOrDiscardCurrentDraft()
        _draft = ArmoDraft.Nuevo(_mainForm.AllocateDraftFormID(), Canon.CanonBridge.SessionGame())
        _draft.Record.EditorID = ArmoDraft.EditorIdPrefix & "new"
        _draft.Record.Race = _raceFormID
        _templateRealFormID = 0UI
        _templateRealEditorID = ""
        _editingExistingDraft = False
        LoadDraftIntoPanels()
        SnapshotCurrentDraft()
        UpdateStatusBanner()
        RequestPreview()
    End Sub

    ''' <summary>"New from template…" → pick a REAL ARMO (race/gender-filtered) and copy it into a NEW record
    ''' (fresh draft FormID, IsOverride=False).</summary>
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
    ''' EditorID, IsOverride=True); your plugin replaces that record on Save.</summary>
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
            .FormID = d.FormID, .EditorID = d.Record.EditorID,
            .DisplayName = d.Record.EditorID, .Signature = "ARMO"}).ToList()
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
                If MessageBox.Show(Me, $"Revert '{d.Record.EditorID}' to the original record? " &
                                   "Your edits to this draft will be discarded.",
                                   "Revert to original", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return False
                _mainForm.UnregisterArmoDraft(fid)
                ' Dropping the in-memory draft is NOT enough: if this override was ALREADY SAVED into the plugin, the
                ' saver's Phase 2a re-preserves it (re-emits every target-plugin ARMO as an OVERRIDE entry unless it's
                ' in RecordsToRemove), so the reverted record would keep getting written. Mark it for removal so Phase 2a
                ' drops it and the true parent wins. No-op when no saved copy exists (removal only drops target-plugin
                ' records); when isCurrent reloads a pristine override draft it stays Not-IsDirty and is never re-emitted.
                _mainForm.MarkRecordForRemoval(fid)
                _mainForm.RevertAppOverrideInMemory(fid)   ' restore the mod's winning record in memory (not the ESP override)
                If isCurrent Then LoadRealArmoTemplate(fid, asOverride:=True)   ' reloads the now-restored original for continued editing
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
            If MessageBox.Show(Me, $"Delete draft '{d.Record.EditorID}'? This cannot be undone.",
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
        _mainForm.RevertAppOverrideInMemory(fid)   ' in-memory: restore the mod's winning record (override) / drop it (new)
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
        _templateRealEditorID = copy.Record.EditorID
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
        If edid.Length = 0 Then edid = If(_draft.Record.EditorID, "")
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
            EditorIdField.ConfigureNew(LabelEdid, TextBoxEdid, LabelEdidPreview,
                                       _edidPrefix, _draft.Record.EditorID)
        Else
            EditorIdField.ConfigureOverride(LabelEdid, TextBoxEdid, LabelEdidPreview,
                                            _draft.Record.EditorID)
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

    ''' <summary>Arma el borrador a partir de un ARMA/ARMO REAL. <c>asOverride:=True</c> ⇒ un override
    ''' que conserva el FormID global; <c>False</c> ⇒ una COPIA NUEVA con FormID de borrador.
    ''' <para>⛔ El record se COPIA —árbol y contexto propio—, no se reconstruye. Antes esta función
    ''' arrancaba de un record EN BLANCO y le volcaba los campos a mano, uno por uno, y su propio
    ''' docstring prometía que «todos los campos se copian». No era cierto y no podía serlo: la lista
    ''' enumera lo que alguien se acordó de poner. Lo que faltaba se perdía en silencio al duplicar —
    ''' el precio y el peso de un ARMO de Skyrim estuvieron ausentes hasta que alguien los buscó en el
    ''' archivo— y los campos que la app no modela no llegaban nunca. En Skyrim, además, la
    ''' construcción normalizaba la plantilla de cuerpo de BODT a BOD2 y perdía General Flags y Armor
    ''' Type, o sea el 85 % de los ARMA del juego.</para>
    ''' <para>Con el árbol copiado no hay nada que enumerar: viene TODO, incluidos el sculpt, las razas
    ''' adicionales, las combinaciones de object template, las resistencias y las banderas de
    ''' cabecera.</para></summary>
    Private Function BuildDraftFromExisting(fid As UInteger, asOverride As Boolean) As ArmoDraft
        Dim pm = _mainForm.PluginManagerForEditor
        If pm Is Nothing Then Return Nothing
        Dim rec = pm.GetRecord(fid)
        If rec Is Nothing OrElse rec.Header.Signature <> "ARMO" Then Return Nothing
        Dim d = If(asOverride,
                   ArmoDraft.Edicion(rec, pm),
                   ArmoDraft.Clon(rec, pm, _mainForm.AllocateDraftFormID()))
        If d Is Nothing Then Return Nothing
        ' El EditorID sólo se SINTETIZA si el record no traía uno: `EDID` no es requerido en el esquema,
        ' pero el commit exige uno no vacío para poder guardar.
        If String.IsNullOrEmpty(d.Record.EditorID) Then
            d.Record.EditorID = ArmoDraft.EditorIdPrefix & fid.ToString("X8")
        End If
        d.IsModified = False
        Return d
    End Function

    ' =====================================================================
    ' Conversores entre los buffers viejos (ARMO_AddonEntry / ARMO_DamageResist / UInteger)
    ' que siguen usando la grilla y el parser legado, y las listas del record nuevo.
    ' =====================================================================

    Friend Shared Function ReadKeywordList(rec As Canon.IArmo) As List(Of UInteger)
        Return rec.Keywords.Select(Function(k) k.Keyword).ToList()
    End Function

    Friend Shared Sub WriteKeywordList(rec As Canon.IArmo, ids As IEnumerable(Of UInteger))
        While rec.Keywords.Count > 0
            If Not rec.QuitarKeywords(0) Then Exit While
        End While
        If ids Is Nothing Then Return
        For Each fid In ids
            Dim e = rec.AgregarKeywords()
            If e IsNot Nothing Then e.Keyword = fid
        Next
    End Sub

    ''' <summary>El modelo de addons (INDX + referencia a la ARMA) es distinto por juego: FO4 =
    ''' índice y
    ''' referencia separados (Models); Skyrim = un array de referencias sin índice explícito
    ''' (Armature, el
    ''' índice ES la posición). No hay un campo compartido para "la lista de addons".</summary>
    Friend Shared Function ReadAddons(rec As Canon.IArmo) As List(Of ARMO_AddonEntry)
        Dim result As New List(Of ARMO_AddonEntry)
        Dim fo4 = TryCast(rec, Canon.ArmoFO4)
        If fo4 IsNot Nothing Then
            For Each m In fo4.Models
                result.Add(New ARMO_AddonEntry With {.AddonIndex = m.ModelAddonIndex,
                           .ArmaFormID = m.ModelArmorAddon})
            Next
            Return result
        End If
        Dim sse = TryCast(rec, Canon.ArmoSSE)
        If sse IsNot Nothing Then
            Dim idx As UShort = 0US
            For Each m In sse.Armature
                result.Add(New ARMO_AddonEntry With {.AddonIndex = idx,
                           .ArmaFormID = m.ModelFilename})
                idx += 1US
            Next
        End If
        Return result
    End Function

    Friend Shared Sub WriteAddons(rec As Canon.IArmo, addons As IEnumerable(Of ARMO_AddonEntry))
        Dim fo4 = TryCast(rec, Canon.ArmoFO4)
        If fo4 IsNot Nothing Then
            While fo4.Models.Count > 0
                If Not fo4.QuitarModels(0) Then Exit While
            End While
            If addons IsNot Nothing Then
                For Each ad In addons
                    Dim m = fo4.AgregarModels()
                    If m Is Nothing Then Continue For
                    m.ModelAddonIndex = ad.AddonIndex
                    m.ModelArmorAddon = ad.ArmaFormID
                Next
            End If
            Return
        End If
        Dim sse = TryCast(rec, Canon.ArmoSSE)
        If sse IsNot Nothing Then
            While sse.Armature.Count > 0
                If Not sse.QuitarArmature(0) Then Exit While
            End While
            If addons IsNot Nothing Then
                ' Skyrim no tiene índice propio: el orden en el array ES el índice, así que se emite
                ' en el
                ' orden que traía la lista (ya ordenada por AddonIndex por quien la arma).
                For Each ad In addons.OrderBy(Function(x) x.AddonIndex)
                    Dim m = sse.AgregarArmature()
                    If m IsNot Nothing Then m.ModelFilename = ad.ArmaFormID
                Next
            End If
        End If
    End Sub

    Friend Shared Function ReadDamageResistances(fo4 As Canon.ArmoFO4) As List(Of ARMO_DamageResist)
        If fo4 Is Nothing Then Return New List(Of ARMO_DamageResist)
        Return fo4.Resistances.Select(Function(r) New ARMO_DamageResist With {
            .DamageTypeFormID = r.ResistanceDamageType, .Value = r.ResistanceValue}).ToList()
    End Function

    Friend Shared Sub WriteDamageResistances(fo4 As Canon.ArmoFO4,
                                             list As IEnumerable(Of ARMO_DamageResist))
        While fo4.Resistances.Count > 0
            If Not fo4.QuitarResistances(0) Then Exit While
        End While
        If list Is Nothing Then Return
        For Each dr In list
            Dim e = fo4.AgregarResistances()
            If e Is Nothing Then Continue For
            e.ResistanceDamageType = dr.DamageTypeFormID
            e.ResistanceValue = dr.Value
        Next
    End Sub

    ' ReadAttachParentSlots/WriteAttachParentSlots y las operaciones de combinaciones viven en
    ' FO4_Base_Library.Canon.CanonInterpretacion: ObjectTemplateResolver (misma librería, otro
    ' proyecto que este editor) también las necesita para resolver el OBTS de un ARMO real.

    ' =====================================================================
    ' Draft → panels
    ' =====================================================================

    Private Sub LoadDraftIntoPanels()
        _loading = True
        Try
            RefreshEditorIdField()

            Dim rec = _draft.Record
            Dim fo4 = TryCast(rec, Canon.ArmoFO4)
            Dim sse = TryCast(rec, Canon.ArmoSSE)

            ' General.
            TextBoxFull.Text = rec.Name
            SetFidText(TextBoxRace, rec.Race)
            ' INRD/PTRN (Instance Naming / Preview Transform) sólo existen en Fallout 4.
            SetFidText(TextBoxInnr, If(fo4 IsNot Nothing, fo4.InstanceNaming, 0UI))
            SetFidText(TextBoxEitm, rec.Enchantment)
            SetFidText(TextBoxPtrn, If(fo4 IsNot Nothing, fo4.PreviewTransform, 0UI))
            CheckBoxNonPlayable.Checked = rec.NonPlayable
            TextBoxDesc.Text = rec.Description
            ' Explícito y no por efecto secundario: poblar el cuadro NO es una edición del usuario, y
            ' CommitPanelsToDraft usa Modified para decidir si toca el DESC. WinForms ya resetea
            ' Modified
            ' al asignar Text, pero de eso depende que un DESC vacío-pero-presente sobreviva.
            TextBoxDesc.Modified = False
            ' Por SlotMaskDe: en Skyrim el 85 % de los ARMA trae la plantilla por BODT, y leer
            ' BOD2 a pelo devuelve 0. Los checkboxes salían todos apagados y el volcado escribía
            ' ESE 0 de vuelta: el slot mask se borraba sin que nadie dijera nada.
            SetSlotChecks(rec.SlotMaskDe())
            ' Value/Weight/Health (DATA) y ArmorRating/BaseAddonIndex/StaggerRating (FNAM) sólo
            ' existen en Fallout 4; Skyrim tiene su propio Value/Weight (DataValue/DataWeight, sin
            ' Health) y su propio ArmorRating (DNAM, entero ×100) en la clase SSE.
            Dim value = If(fo4 IsNot Nothing, fo4.Value, If(sse IsNot Nothing, sse.DataValue, 0))
            Dim weight = If(fo4 IsNot Nothing, fo4.Weight,
                           If(sse IsNot Nothing, sse.DataWeight, 0.0F))
            NumValue.Value = ClampDec(CDec(value), NumValue)
            NumWeight.Value = ClampDec(CDec(weight), NumWeight)
            NumHealth.Value = ClampDec(CDec(If(fo4 IsNot Nothing, fo4.Health, 0UI)), NumHealth)
            ' Armor Rating: FO4 = FNAM u16 (integer); SKYRIM = DNAM s32 stored ×100 (se muestra
            ' ÷100). Bind to the game's field. ApplyArmorRatingFieldMode() (called once at init)
            ' relabels DNAM/FNAM and sets decimals.
            If _isSkyrim Then
                Dim skyrimRating = If(sse IsNot Nothing, sse.ArmorRating, 0)
                NumArmorRating.Value = ClampDec(CDec(skyrimRating) / 100D, NumArmorRating)
            Else
                Dim fo4Rating = If(fo4 IsNot Nothing, fo4.ArmorRating, CUShort(0))
                NumArmorRating.Value = ClampDec(CDec(fo4Rating), NumArmorRating)
            End If
            Dim baseAddonIndex = If(fo4 IsNot Nothing, fo4.BaseAddonIndex, CUShort(0))
            Dim staggerRating = If(fo4 IsNot Nothing, fo4.StaggerRating, CByte(0))
            NumBaseAddonIndex.Value = ClampDec(CDec(baseAddonIndex), NumBaseAddonIndex)
            NumStaggerRating.Value = ClampDec(CDec(staggerRating), NumStaggerRating)

            ' Misc & Sounds.
            SetFidText(TextBoxYnam, rec.SoundPickUp)
            SetFidText(TextBoxZnam, rec.SoundPutDown)
            SetFidText(TextBoxEtyp, rec.EquipmentType)
            SetFidText(TextBoxBamt, rec.AlternateBlockMaterial)
            NumObndX1.Value = ClampDec(CDec(rec.MinX), NumObndX1)
            NumObndY1.Value = ClampDec(CDec(rec.MinY), NumObndY1)
            NumObndZ1.Value = ClampDec(CDec(rec.MinZ), NumObndZ1)
            NumObndX2.Value = ClampDec(CDec(rec.MaxX), NumObndX2)
            NumObndY2.Value = ClampDec(CDec(rec.MaxY), NumObndY2)
            NumObndZ2.Value = ClampDec(CDec(rec.MaxZ), NumObndZ2)

            ' Damage Resist (DAMA): deep-copy into the working buffer, flushed on Apply. FO4-only.
            _damageResists.Clear()
            _damageResists.AddRange(ReadDamageResistances(fo4))
            RefreshDamageGrid()

            ' Addons.
            _addons.Clear()
            _addons.AddRange(ReadAddons(rec))
            RefreshAddonsGrid()

            ' Object Template (OBTS): las combinaciones se editan sobre una COPIA del record, así el sub-editor
            ' y el reordenamiento no tocan la caché. Cargar no es una edición del usuario. Sólo FO4.
            _comboHost = fo4.Copia()
            _combinations.Clear()
            If _comboHost IsNot Nothing Then _combinations.AddRange(_comboHost.Combinations)
            RefreshCombinationsGrid()

            ' Keywords + APPR: copy into the working buffers (flushed on Apply, like _addons). APPR
            ' is FO4-only.
            _keywords.Clear()
            _keywords.AddRange(ReadKeywordList(rec))
            _appr.Clear()
            _appr.AddRange(ReadAttachParentSlots(fo4))
            RefreshKwdaList()
            RefreshApprList()

            ' World Model & Material. El material swap a nivel ARMO (MOD2/MOD4) sólo existe en
            ' Fallout 4.
            TextBoxMod2.Text = rec.WorldModelModelFilename
            TextBoxMod4.Text = rec.WorldModelModelFilename2
            SetFidText(TextBoxMo2s, If(fo4 IsNot Nothing, fo4.WorldModelMaterialSwap, 0UI))
            SetFidText(TextBoxMo4s, If(fo4 IsNot Nothing, fo4.WorldModelMaterialSwap2, 0UI))
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
        ' Por CommitProtegido y no directo: el diálogo que sale cuando el volcado del preview falla
        ' invita a apretar Aceptar, y acá corre EL MISMO volcado. Sin la guarda, el clic siguiente al
        ' aviso mataba el proceso.
        ' Y se para el temporizador ANTES de volcar: el render lee la vista VIVA del borrador desde un
        ' worker, así que mutar el árbol mientras uno está en vuelo lo hace fallar — sin ruido, porque el
        ' await cae en el catch mudo del render, pero con una vista previa a medias. Parando el
        ' temporizador no arranca ninguno nuevo; el que ya esté en vuelo sigue siendo una ventana abierta,
        ' y cerrarla del todo pide un candado compartido con el render, que no está hecho.
        If _previewDebounce IsNot Nothing Then _previewDebounce.Stop()
        If CommitProtegido(validate:=True) Then
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
                   AndAlso Not String.Equals(edid, _draft.Record.EditorID,
                                             StringComparison.OrdinalIgnoreCase) Then
                    If MessageBox.Show(Me, "Changing the EditorID of an override record is unusual. Keep this change?",
                                       "Apply", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
                        Return False
                    End If
                End If
            ElseIf Not String.Equals(edid, _draft.Record.EditorID,
                                     StringComparison.OrdinalIgnoreCase) _
                   AndAlso Not _mainForm.IsRecordEditorIdAvailable(edid) Then
                MessageBox.Show(Me, $"EditorID '{edid}' is already in use. Choose another.", "Apply",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return False
            End If
            ' ⛔ LA RAZA ES OBLIGATORIA EN LA PRÁCTICA, aunque el esquema la declare opcional: está en
            ' 5.825 de 5.825 ARMA y ARMO de los dos juegos y no declara NULL. Dejarla vacía no es
            ' «borrarla», es entrada inválida — y sin este rechazo el volcado la conserva callado, que es
            ' peor que avisar.
            If GetFid(TextBoxRace) = 0UI Then
                MessageBox.Show(Me, "Elegiá una raza (RNAM): todos los ARMO del juego la traen.",
                                "Apply", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return False
            End If
        End If
        Dim rec = _draft.Record
        ' Los campos de TEXTO se escriben SOLO si cambiaron. Escribirlos siempre fuerza la
        ' expansion de lstring del escritor sobre un valor que nadie edito: el getter de un record
        ' localizado devuelve el TEXTO resuelto, y reasignarlo lo fija como texto perdiendo el id
        ' original. Abrir un ARMO para tocar su ARMA no tiene por que reescribir su nombre.
        If Not String.Equals(rec.EditorID, edid, StringComparison.Ordinal) Then rec.EditorID = edid
        Dim fo4 = TryCast(rec, Canon.ArmoFO4)
        Dim sse = TryCast(rec, Canon.ArmoSSE)

        ' General.
        Dim nombreNuevo = TextBoxFull.Text.Trim()
        If Not String.Equals(rec.Name, nombreNuevo, StringComparison.Ordinal) Then rec.Name = nombreNuevo
        ' ⛔ RNAM NO pasa por PonerRef. El esquema lo da por opcional, pero está en 5.825 de 5.825 ARMA
        ' y ARMO de los dos juegos y no declara NULL: una caja de raza vacía no es «borrar la raza», es
        ' entrada inválida. Sacarlo dejaría un ARMA sin raza, en silencio; escribir 0 dejaría una
        ' referencia nula. Así que no se toca, y `validate` lo rechaza (ver arriba).
        Dim razaPedida = GetFid(TextBoxRace)
        If razaPedida <> 0UI Then rec.Race = razaPedida
        Canon.CanonInterpretacion.PonerReferenciaOpcional(GetFid(TextBoxEitm), Sub(v) rec.Enchantment = v, Sub() rec.EnchantmentPresente = False)
        ' NonPlayable vive en la cabecera, que no sale del árbol de campos pero sí viaja en el
        ' contexto del record —de ahí la toma el grabado—, así que escribirla acá es editar lo
        ' que se va a guardar.
        rec.NonPlayable = CheckBoxNonPlayable.Checked
        ' La PRESENCIA sólo la cambia el USUARIO, y por eso el gate es `TextBoxDesc.Modified` y
        ' no el largo del texto. Derivarla del contenido destruye la única distinción que este
        ' campo existe para hacer ("" ≠ ausente) y lo hace SIN que nadie toque nada:
        ' `RenderPreviewAsync` llama a CommitPanelsToDraft(validate:=False) para el preview, así
        ' que abrir el editor sobre un ARMO cuyo DESC es un id de lstring que resuelve a ""
        ' bastaba para pisarlo y que el guardado dejara de emitir el subrecord — en Skyrim, donde
        ' el DESC es obligatorio. Ahora la ausencia se preserva simplemente NO escribiendo el campo
        ' cuando el usuario no lo tocó: escribir (aunque sea "") es lo único que lo materializa,
        ' así que no volver a escribir deja la presencia tal como estaba.
        ' `Modified` es exactamente el primitivo que hace falta: WinForms lo pone en True con la
        ' edición del usuario y lo vuelve a False cuando el texto se asigna por código (que es
        ' como se puebla el cuadro).
        If TextBoxDesc.Modified Then rec.Description = TextBoxDesc.Text.Trim()
        ' Por la rama QUE EL RECORD TRAE, no siempre BOD2: en Skyrim un ARMA con BODT quedaba
        ' con las dos ramas y al releerlo todo lo que sigue caía en passthrough. La ley vive en
        ' un solo lugar, junto a su lectura (SlotMaskDe).
        rec.PonerSlotMaskEn(ReadSlotChecks())
        ' Value/Weight/Health, ArmorRating/BaseAddonIndex/StaggerRating y el material swap a nivel
        ' ARMO sólo existen en Fallout 4; el ArmorRating y el Value/Weight de Skyrim viven en la
        ' clase SSE aparte.
        If fo4 IsNot Nothing Then
            fo4.Value = CInt(NumValue.Value)
            fo4.Weight = CSng(NumWeight.Value)
            fo4.Health = CUInt(NumHealth.Value)
            fo4.ArmorRating = CUShort(NumArmorRating.Value)
            fo4.BaseAddonIndex = CUShort(NumBaseAddonIndex.Value)
            fo4.StaggerRating = CByte(NumStaggerRating.Value)
        End If
        If sse IsNot Nothing Then
            sse.DataValue = CInt(NumValue.Value)
            sse.DataWeight = CSng(NumWeight.Value)
            sse.ArmorRating = CInt(Math.Round(NumArmorRating.Value * 100D,
                                   MidpointRounding.AwayFromZero))
        End If

        ' Misc & Sounds.
        Canon.CanonInterpretacion.PonerReferenciaOpcional(GetFid(TextBoxYnam), Sub(v) rec.SoundPickUp = v, Sub() rec.SoundPickUpPresente = False)
        Canon.CanonInterpretacion.PonerReferenciaOpcional(GetFid(TextBoxZnam), Sub(v) rec.SoundPutDown = v, Sub() rec.SoundPutDownPresente = False)
        Canon.CanonInterpretacion.PonerReferenciaOpcional(GetFid(TextBoxEtyp), Sub(v) rec.EquipmentType = v, Sub() rec.EquipmentTypePresente = False)
        Canon.CanonInterpretacion.PonerReferenciaOpcional(GetFid(TextBoxBamt), Sub(v) rec.AlternateBlockMaterial = v, Sub() rec.AlternateBlockMaterialPresente = False)
        rec.MinX = CShort(NumObndX1.Value)
        rec.MinY = CShort(NumObndY1.Value)
        rec.MinZ = CShort(NumObndZ1.Value)
        rec.MaxX = CShort(NumObndX2.Value)
        rec.MaxY = CShort(NumObndY2.Value)
        rec.MaxZ = CShort(NumObndZ2.Value)

        ' Addons (order matters — copy the working list in row order).
        WriteAddons(rec, _addons)

        ' Keywords: flush the working buffer (mutated by the Add/Remove handlers) into the draft.
        WriteKeywordList(rec, _keywords)

        If fo4 IsNot Nothing Then
            Canon.CanonInterpretacion.PonerReferenciaOpcional(GetFid(TextBoxInnr), Sub(v) fo4.InstanceNaming = v, Sub() fo4.InstanceNamingPresente = False)
            Canon.CanonInterpretacion.PonerReferenciaOpcional(GetFid(TextBoxPtrn), Sub(v) fo4.PreviewTransform = v, Sub() fo4.PreviewTransformPresente = False)
            Canon.CanonInterpretacion.PonerReferenciaOpcional(GetFid(TextBoxMo2s), Sub(v) fo4.WorldModelMaterialSwap = v, Sub() fo4.WorldModelMaterialSwapPresente = False)
            Canon.CanonInterpretacion.PonerReferenciaOpcional(GetFid(TextBoxMo4s), Sub(v) fo4.WorldModelMaterialSwap2 = v, Sub() fo4.WorldModelMaterialSwap2Presente = False)
            ' Damage Resist (DAMA): flush the working buffer (deep-copied so the draft never aliases
            ' the grid).
            WriteDamageResistances(fo4, _damageResists)
            ' Object Template (OBTS): se vuelca la lista entera al record. Cada combinación se clona al
            ' escribirla, así que el record no queda compartiendo nodos con la copia de trabajo.
            fo4.ReemplazarCombinations(_combinations)
            ' APPR: flush the working buffer.
            WriteAttachParentSlots(fo4, _appr)
        End If

        ' World Model & Material.
        Dim mod2 = TextBoxMod2.Text.Trim()
        Dim mod4 = TextBoxMod4.Text.Trim()
        If Not String.Equals(rec.WorldModelModelFilename, mod2, StringComparison.Ordinal) Then rec.WorldModelModelFilename = mod2
        If Not String.Equals(rec.WorldModelModelFilename2, mod4, StringComparison.Ordinal) Then rec.WorldModelModelFilename2 = mod4

        ' Dirty only on a REAL change (mirror of ArmaEditor). The preview commits the panels on
        ' every render, so setting IsModified unconditionally re-emitted an identical override at
        ' save time (e.g. opening an ARMO just to edit its ARMA marked the ARMO dirty). Compare
        ' content against the open-time snapshot instead — ContentEquals ahora compara el record
        ' entero (combinaciones incluidas) por bytes, así que ya no hace falta una marca aparte
        ' para "se tocó el Object Template". Two-way so reverting a change clears it; NEW drafts
        ' are always dirty.
        If Not _draft.IsNew Then
            Dim unchanged = (_openSnapshot IsNot Nothing) _
                            AndAlso _draft.ContentEquals(_openSnapshot)
            _draft.IsModified = Not unchanged
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
        SelectGridRow(GridDamage, selIdx)
    End Sub

    ''' <summary>Restore a row selection after a Rows.Clear + re-add. The current cell MUST land on a VISIBLE
    ''' column: <see cref="ConfigureForGame"/> hides the Addons grid's INDX column under Skyrim, and assigning
    ''' CurrentCell to a cell in a hidden column throws InvalidOperationException ("Current cell cannot be set
    ''' to an invisible cell"). FullRowSelect + read-only grids ⇒ any visible column is equivalent.
    ''' No visible column at all ⇒ leave CurrentCell alone (the row still shows as selected).</summary>
    Private Shared Sub SelectGridRow(grid As DataGridView, idx As Integer)
        If idx < 0 OrElse idx >= grid.Rows.Count Then Return
        grid.Rows(idx).Selected = True
        Dim col = grid.Columns.GetFirstColumn(DataGridViewElementStates.Visible)
        If col Is Nothing Then Return
        grid.CurrentCell = grid.Rows(idx).Cells(col.Index)
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
        SelectGridRow(GridAddons, selIdx)
    End Sub

    ''' <summary>The effective slot footprint shown read-only for an addon row: the ARMA's own BOD2 mask, or
    ''' the ARMO's when the ARMA declares none — por <see cref="EquipResolver.ArmaGeometryMask"/>, el mismo
    ''' átomo que usa la ley de equip, para que esta celda no pueda decir otra cosa que el render. El BOD2
    ''' del ARMO es el que el usuario está editando (checkboxes), que todavía no es un registro. Drafts
    ''' resolve via the draft-aware parsed view.</summary>
    Private Function EffectiveSlotsText(armaFid As UInteger) As String
        If armaFid = 0UI Then Return ""
        Dim arma = _mainForm.GetParsedArmaForEditor(armaFid)
        ' ArmaGeometryMask vive en EquipResolver (Records\, no se toca): sigue pidiendo el modelo legado.
        Return SlotsToText(EquipResolver.ArmaGeometryMask(arma, ReadSlotChecks()))
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
            .FormID = d.FormID, .EditorID = d.Record.EditorID,
            .DisplayName = d.Record.EditorID, .Signature = "ARMA"}).ToList()
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
        ' Parent ARMO for the ARMA editor's "Full armor" preview = the ARMO being edited here (_draft.FormID —
        ' real for override, provisional for new; the draft-aware resolver handles both). Outfit context is
        ' threaded straight through so the ARMA editor's "Full Outfit" preview sees the assembled outfit.
        Using dlg As New ArmoAddonEditor_Form(_mainForm, _draft.Record.Race, _addons(i),
                                              parentArmoFormID:=_draft.FormID, outfitContextFormID:=_outfitContextFormID)
            Dim ok = (dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultEntry IsNot Nothing)
            If ok Then
                _addons(i) = dlg.ResultEntry
                OnFieldEdited(Me, EventArgs.Empty)
            End If
            ' Refresh regardless of OK/Cancel: the addon modal's "Edit ARMA…" can change the referenced ARMA
            ' draft's slots/name even when the addon edit itself is cancelled, so the row's rendered Name/Slots
            ' (draft-aware via GetParsedArmaForEditor) could otherwise show a stale pre-edit snapshot.
            RefreshAddonsGrid()
            ' Re-render regardless of OK/Cancel too: editing the referenced ARMA (mesh/slots) changes the preview
            ' but NOT the addon entry's FormID, so OnFieldEdited (only fired on OK) plus the now-ARMA-content-aware
            ' AddonPreviewKey are what actually invalidate the cache — but a CANCELLED addon edit that still changed
            ' the ARMA draft never calls OnFieldEdited, so request the preview here so the render always catches up.
            RequestPreview()
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
        SelectGridRow(GridAddons, j)
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
            Dim name = If(String.IsNullOrEmpty(c.CombinationName), "(unnamed)", c.CombinationName)
            GridCombinations.Rows.Add((i + 1).ToString(CultureInfo.InvariantCulture),
                                      name,
                                      If(c.ObjectModTemplateItemDefault, "Yes", ""),
                                      c.ObjectModTemplateItemParentCombinationIndex.ToString(CultureInfo.InvariantCulture),
                                      c.ObjectModTemplateItemLevelMin.ToString(CultureInfo.InvariantCulture),
                                      c.ObjectModTemplateItemLevelMax.ToString(CultureInfo.InvariantCulture),
                                      c.Includes.Count.ToString(CultureInfo.InvariantCulture),
                                      c.Properties.Count.ToString(CultureInfo.InvariantCulture),
                                      c.Keywords.Count.ToString(CultureInfo.InvariantCulture),
                                      If(c.CombinationEditorOnly, "Yes", ""))
        Next
        SelectGridRow(GridCombinations, selIdx)
    End Sub

    Private Function SelectedComboIndex() As Integer
        If GridCombinations.CurrentRow Is Nothing Then Return -1
        Dim i = GridCombinations.CurrentRow.Index
        If i < 0 OrElse i >= _combinations.Count Then Return -1
        Return i
    End Function

    ''' <summary>Any OBTS mutation → request a debounced live preview. La comparación por bytes
    ''' contra el
    ''' snapshot de apertura (ver CommitPanelsToDraft) ya detecta un cambio en las combinaciones
    ''' sola, así
    ''' que no hace falta una marca aparte.</summary>
    Private Sub MarkCombinationsEdited()
        OnFieldEdited(Me, EventArgs.Empty)
    End Sub

    ''' <summary>Agregar: la combinación vacía se crea en la copia de trabajo y se la edita ahí. Si el usuario
    ''' cancela no entra a la lista, y como al aplicar el record se rehace desde la lista, el nodo que quedó
    ''' suelto en la copia no llega a ningún lado.</summary>
    Private Sub OnAddCombo(sender As Object, e As EventArgs)
        Dim nueva = _comboHost.AgregarCombinacion(Nothing)
        If nueva Is Nothing Then Return
        Using dlg As New ObtsCombinationEditor_Form(_mainForm, nueva)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                _combinations.Add(nueva)
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

    ''' <summary>Duplicar: una copia independiente de la seleccionada, insertada justo detrás.</summary>
    Private Sub OnDuplicateCombo(sender As Object, e As EventArgs)
        Dim i = SelectedComboIndex()
        If i < 0 Then Return
        Dim copia = _comboHost.AgregarCombinacion(_combinations(i))
        If copia Is Nothing Then Return
        _combinations.Insert(i + 1, copia)
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
        SelectGridRow(GridCombinations, j)
        MarkCombinationsEdited()
    End Sub

    Private Sub OnEditCombo(sender As Object, e As EventArgs)
        EditComboAt(SelectedComboIndex())
    End Sub

    Private Sub OnComboDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        EditComboAt(e.RowIndex)
    End Sub

    ''' <summary>Editar: el sub-editor trabaja sobre una copia aparte, así cancelar deja la fila como estaba;
    ''' al aceptar, la copia editada reemplaza a la fila.</summary>
    Private Sub EditComboAt(i As Integer)
        If i < 0 OrElse i >= _combinations.Count Then Return
        Dim trabajo = _comboHost.AgregarCombinacion(_combinations(i))
        If trabajo Is Nothing Then Return
        Using dlg As New ObtsCombinationEditor_Form(_mainForm, trabajo)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                _combinations(i) = trabajo
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
            Dim incl = String.Join("|", c.Includes.Select(Function(i) i.IncludeMod.ToString("X8") & ":" & i.IncludeAttachPointIndex.ToString(CultureInfo.InvariantCulture)))
            Dim props = String.Join("|", c.Properties.Select(Function(v) DescribirPropiedad(v)))
            parts.Add($"{If(c.ObjectModTemplateItemDefault, 1, 0)};{c.ObjectModTemplateItemParentCombinationIndex};{incl};{props}")
        Next
        Return String.Join("~", parts)
    End Function

    ''' <summary>La parte de una Property que cambia lo que el Object Template aplica, en texto.</summary>
    Private Shared Function DescribirPropiedad(vista As Canon.IBloque_Properties4) As String
        Dim p = vista.LeerPropiedad()
        Return $"{CInt(p.ValueType)}/{p.FunctionType}/{p.PropertyIndex}/{p.Value1FormID:X8}/" &
               $"{BitConverter.ToInt32(BitConverter.GetBytes(p.Value1), 0)}/" &
               $"{BitConverter.ToInt32(BitConverter.GetBytes(p.Value2), 0)}"
    End Function

    ''' <summary>Preview-key fragment for one addon row: the ARMA FormID + index PLUS a content signature of the
    ''' addon ARMA's referenced MSWP DRAFTS. An addon ARMA's material-swap draft keeps its FormID across an edit,
    ''' so without this the ARMO preview key wouldn't change and the swap wouldn't re-render.</summary>
    Private Function AddonPreviewKey(a As ARMO_AddonEntry) As String
        Dim mswpSig As String = ""
        Dim armaSig As String = ""
        Dim arma = _mainForm.GetParsedArmaForEditor(a.ArmaFormID)
        If arma IsNot Nothing Then
            ' MO2S/MO3S (material swap) y MO2F/MO3F (model flags) sólo existen en Fallout 4.
            Dim armaFo4 = TryCast(arma, Canon.ArmaFO4)
            Dim maleSwap As UInteger = If(armaFo4 IsNot Nothing, armaFo4.MaleMaterialSwap, 0UI)
            Dim femaleSwap As UInteger = If(armaFo4 IsNot Nothing, armaFo4.FemaleMaterialSwap, 0UI)
            mswpSig = _mainForm.GetMswpDraftSignature(maleSwap) & "/" & _mainForm.GetMswpDraftSignature(femaleSwap)
            ' Include the ARMA's render-relevant CONTENT (meshes / slots / model flags / skin TXST), not just its
            ' FormID — otherwise editing the referenced ARMA draft (e.g. the addon modal's "Edit ARMA…" changing a
            ' mesh path) leaves the key unchanged and RenderPreviewAsync early-returns on `key = _lastPreviewKey`,
            ' so the preview never reflects the edit. GetParsedArmaForEditor is draft-aware → live edited values.
            Dim maleFlags As Byte = If(armaFo4 IsNot Nothing, armaFo4.MaleFlags, CByte(0))
            Dim femaleFlags As Byte = If(armaFo4 IsNot Nothing, armaFo4.FemaleFlags, CByte(0))
            armaSig = String.Join("|", {arma.MaleModelFilename, arma.FemaleModelFilename,
                                        arma.SlotMaskDe().ToString("X"),
                                        maleFlags.ToString(CultureInfo.InvariantCulture),
                                        femaleFlags.ToString(CultureInfo.InvariantCulture),
                                        arma.MaleSkinTexture.ToString("X"), arma.FemaleSkinTexture.ToString("X")})
        End If
        Return a.ArmaFormID.ToString("X8") & "#" & a.AddonIndex.ToString(CultureInfo.InvariantCulture) & "@" & mswpSig & "~" & armaSig
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
            Dim armaMesh = If(isFemaleGender, arma.FemaleModelFilename, arma.MaleModelFilename)
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
                draft = MswpDraft.Nuevo(_mainForm.AllocateDraftFormID(), Canon.CanonBridge.SessionGame())
                If draft IsNot Nothing Then draft.Record.EditorID = MswpDraft.EditorIdPrefix & "new"
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
                .FormID = d.FormID, .EditorID = d.Record.EditorID, .DisplayName = d.Record.EditorID, .Signature = "MSWP"}).ToList()
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
            If arma IsNot Nothing Then mask = mask Or arma.SlotMaskDe()
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
        ' ⛔ Si el volcado ya falló, no se rearma: este método hace Stop+Start y lo llama CADA edición
        ' de campo, así que el Stop() del manejador de error duraba hasta la próxima tecla — y como el
        ' aviso sale una sola vez, a partir de ahí el commit fallaba en silencio en cada tecla.
        If _previewCommitFallado Then Return
        If _host Is Nothing OrElse _previewDebounce Is Nothing Then Return
        _previewDebounce.Stop()
        _previewDebounce.Start()
    End Sub

    ''' <summary>Push the preview-mode controls (scope radios / Include Body / Show other gender) onto the
    ''' render host BEFORE each render, so the knobs and the preview cache key stay in sync with the UI.
    '''
    ''' Only Armor collects just the edited ARMO; Full Outfit collects the full actor. Include Body forces
    ''' the body into collection (OnlyOutfitCollect skips Skin), so <c>OnlyOutfitCollect = onlyArmor AndAlso
    ''' NOT IncludeBody</c>; RenderBody visibility mirrors Include Body. Show other gender maps to a target-
    ''' gender DEFAULT actor via <c>PreviewGenderOverride</c> (opposite of the preview NPC's own gender).</summary>
    Private Sub ApplyPreviewControlsToHost()
        If _host Is Nothing Then Return
        Dim onlyArmor As Boolean = RadioOnlyArmor.Checked
        Dim includeBody As Boolean = CheckIncludeBody.Checked
        _host.OnlyOutfitCollect = onlyArmor AndAlso Not includeBody
        _host.Toggles.RenderBody = includeBody
        _host.PreviewGenderOverride = If(CheckShowOtherGender.Checked, CType(Not _isFemale, Boolean?), Nothing)
    End Sub

    ' Scope radios + "Show other gender" change COLLECTION / gender resolution ⇒ full (debounced) re-render.
    Private Sub OnPreviewModeChanged(sender As Object, e As EventArgs) _
        Handles RadioOnlyArmor.CheckedChanged, RadioFullOutfit.CheckedChanged, CheckShowOtherGender.CheckedChanged
        If _loading Then Return
        RequestPreview()
    End Sub

    ' "Include Body" is visibility (RenderBody) → apply instantly like EditBody, THEN request a debounced
    ' re-render so collection catches up when it flips OnlyOutfitCollect (only re-renders if the key changed).
    Private Sub OnIncludeBodyChanged(sender As Object, e As EventArgs) Handles CheckIncludeBody.CheckedChanged
        If _loading Then Return
        If _host IsNot Nothing Then
            _host.Toggles.RenderBody = CheckIncludeBody.Checked
            _host.ApplyRenderToggleVisibility()
        End If
        RequestPreview()
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

    ''' <summary>Vuelca los paneles al borrador sin que un fallo se lleve el proceso, y REVIRTIENDO si
    ''' falla. Gemelo de <c>ArmaEditor_Form.CommitProtegido</c>; el porqué está escrito allá.
    ''' <para>Duplicado a propósito: lo repetido es política de UI —el diálogo, el temporizador, la
    ''' bandera de "ya avisé"— y los dos formularios no comparten base. Unificarlo pediría pasar dueño,
    ''' temporizador, bandera por referencia y mensaje: cuatro parámetros para ahorrar catorce líneas.
    ''' Lo que sí es ley —qué se registra cuando falla— va a <c>Logger</c>, que es un solo lugar.</para></summary>
    Private Function CommitProtegido(validate As Boolean) As Boolean
        Dim antes = _draft?.Clone()
        Try
            Return CommitPanelsToDraft(validate)
        Catch ex As Exception
            If antes IsNot Nothing AndAlso _draft IsNot Nothing Then
                _draft.Record = antes.Record
                _draft.IsModified = antes.IsModified
                _mainForm.RegisterArmoDraft(_draft)
            End If
            Logger.Log("ArmoEditor.CommitProtegido: " & ex.ToString())
            If _previewDebounce IsNot Nothing Then _previewDebounce.Stop()
            If Not _previewCommitFallado Then
                _previewCommitFallado = True
                MessageBox.Show(Me,
                    "No se pudo armar esta armadura:" & vbCrLf & vbCrLf &
                    ex.Message & vbCrLf & vbCrLf &
                    "La vista previa queda detenida y el último cambio se deshizo. El detalle quedó en " &
                    "el log. Podés seguir editando, pero este cambio no se va a poder guardar.",
                    "Vista previa", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End If
            Return False
        End Try
    End Function

    Private Async Function RenderPreviewAsync() As Task
        If _host Is Nothing OrElse _preview Is Nothing OrElse _preview.IsDisposed Then Return
        If _previewNpcFormID = 0UI Then Return

        ' Flush the current panel state into _draft AND register it (RegisterArmoDraft) BEFORE the preview key
        ' is built. The very first render (from _Shown / template-load) reaches here with _pendingApply=False,
        ' so without this the throwaway OTFT's item (the ARMO draft) is never registered → the draft-aware
        ' resolver (TryGetArmoDraft) returns Nothing → naked. validate:=False never early-returns, so the
        ' draft is always registered; the key is computed from _draft AFTER this so it stays correct.
        If Not CommitProtegido(validate:=False) Then Return

        ' Push the preview-mode controls onto the host BEFORE the key is built so a scope / Include Body /
        ' gender change is reflected in both the render knobs and the cache key (else the key match early-returns).
        ApplyPreviewControlsToHost()

        ' "Full Outfit" + outfit context ⇒ render the whole outfit assembled in the OutfitPicker; this ARMO is
        ' one of its pieces and the draft-aware resolver swaps in the edited version by shared FormID, so no
        ' throwaway is needed. Otherwise (Only Armor, or Full Outfit with no context) render the throwaway
        ' single-item OTFT holding this ARMO over the actor — the existing behaviour.
        Dim fullOutfitWithContext As Boolean = RadioFullOutfit.Checked AndAlso _outfitContextFormID <> 0UI
        Dim previewOtftFid As UInteger = If(fullOutfitWithContext, _outfitContextFormID, OutfitDraft.PreviewDraftFormID)

        ' Key over EVERY render-relevant ARMO field; previously the material swaps + race were omitted, so
        ' editing them left a stale preview (key unchanged → early return).
        Dim fo4 = TryCast(_draft.Record, Canon.ArmoFO4)
        Dim maleSwap = If(fo4 IsNot Nothing, fo4.WorldModelMaterialSwap, 0UI)
        Dim femaleSwap = If(fo4 IsNot Nothing, fo4.WorldModelMaterialSwap2, 0UI)
        Dim key As String = String.Join(":", {
            previewOtftFid.ToString("X8"), _outfitContextFormID.ToString("X8"),
            _draft.FormID.ToString("X8"),
            _draft.Record.SlotMaskDe().ToString("X8"),
            _draft.Record.Race.ToString("X8"),
            _draft.Record.WorldModelModelFilename, _draft.Record.WorldModelModelFilename2,
            maleSwap.ToString("X8"), femaleSwap.ToString("X8"),
            _mainForm.GetMswpDraftSignature(maleSwap), _mainForm.GetMswpDraftSignature(femaleSwap),
            String.Join(",", _addons.Select(AddressOf AddonPreviewKey)),
            CombinationsKey(),
            If(_host IsNot Nothing AndAlso _host.OnlyOutfitCollect, "oc1", "oc0"),
            If(_host IsNot Nothing AndAlso _host.PreviewGenderOverride.HasValue, "g" & _host.PreviewGenderOverride.Value.ToString(), "g-")})
        If key = _lastPreviewKey Then Return
        If _previewInProgress Then Return
        _previewInProgress = True
        Try
            If Not fullOutfitWithContext Then
                Dim otft = OutfitDraft.Nuevo(OutfitDraft.PreviewDraftFormID,
                                             Canon.CanonBridge.SessionGame())
                otft.Record.EditorID = OutfitDraft.EditorIdPrefix & "(armopreview)"
                otft.ReemplazarPrendas({_draft.FormID})
                _mainForm.RegisterOutfitDraft(otft)
                _previewDraftRegistered = True
            End If

            Try
                Await _mainForm.PreviewOutfitInHostAsync(_host, _previewNpcFormID, previewOtftFid)
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
            ' Camera GPU/CPU toggle debe re-aplicar el tint de ESTE preview (no sólo la geometría). Ver MainForm.
            _mainForm?.HookSkinningToggleRefresh(_preview, _host)
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
