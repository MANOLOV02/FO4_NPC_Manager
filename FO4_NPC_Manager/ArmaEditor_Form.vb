Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

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

    ''' <summary>The ARMO that CONTAINS the ARMA being edited (real record FormID for an override, or the
    ''' provisional FormID of an ARMO draft), threaded in from the ARMO editor's Addons tab so the
    ''' "Full armor" preview mode can render the whole parent ARMO with this ARMA applied. 0 = no parent
    ''' in context (standalone "Edit mine…") ⇒ "Full armor" falls back to "Only Model" (the synthetic
    ''' single-addon wrapper). The draft-aware resolver handles both real and draft parents: an OVERRIDE
    ''' ARMA shares the parent addon's FormID, so ArmaDraftResolver returns the edited draft and the parent
    ''' renders with the edit.</summary>
    Private ReadOnly _parentArmoFormID As UInteger

    ''' <summary>The OTFT of the outfit being assembled in the OutfitPicker's Create tab (a stable throwaway
    ''' draft the picker registers for this purpose), threaded down OutfitPicker → ArmoEditor → ArmoAddonEditor
    ''' → here. Enables the "Full Outfit" preview mode to render that whole outfit with THIS ARMA's edit
    ''' substituted (the parent ARMO is one of the outfit's pieces; the draft-aware resolver swaps in the edited
    ''' version by shared FormID). 0 = no outfit in context (standalone open) ⇒ "Full Outfit" falls back to the
    ''' single-item throwaway over the actor.</summary>
    Private ReadOnly _outfitContextFormID As UInteger

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
    ''' <summary>Ya se avisó que el volcado del preview falló. Se avisa UNA vez: el temporizador vuelve
    ''' a intentar en cada redibujo y un diálogo por tick es inusable.</summary>
    Private _previewCommitFallado As Boolean
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
    ''' <summary>La TOMA de este editor sobre el registro. Gemela de la de <c>ArmoEditor_Form</c>; el porqué
    ''' —y por qué reemplaza al par (<c>_editingExistingDraft</c>, <c>_draftWasNew</c>)— está allá y en
    ''' <see cref="TomaDeBorrador(Of TD)"/>.</summary>
    Private _toma As TomaDeBorrador(Of ArmaDraft)

    ''' <summary>Throwaway ARMO wrapper FormID for previewing a STANDALONE ARMA draft — a draft sentinel just
    ''' below the OTFT preview sentinel (<see cref="OutfitDraft.PreviewDraftFormID"/>) so the resolver picks
    ''' it up but it's never persisted (filtered out of the save set).
    ''' <para><c>Shared ReadOnly</c> y no <c>Const</c>: se compone desde
    ''' <see cref="Borradores.FormIdAltoDeBorrador"/>, que es un campo — ver la nota de allá.</para></summary>
    Private Shared ReadOnly PreviewArmoWrapperFormID As UInteger = Borradores.FormIdAltoDeBorrador Or &H7FEUI

    ''' <param name="mainForm">Owner — supplies the draft registrars, the PluginManager for the FormID pickers,
    ''' parsed-record access and the WYSIWYG preview host.</param>
    ''' <param name="previewNpcFormID">The currently-selected NPC for preview context. 0 = no preview.</param>
    ''' <param name="raceFormID">The preview NPC's race (pre-fills a new ARMA's RNAM).</param>
    ''' <param name="isFemale">The preview NPC's gender (drives which mesh/priority is previewed).</param>
    ''' <param name="editDraft">When supplied, edit this existing ARMA draft directly (skip the empty seed).</param>
    ''' <param name="initialTemplateArmaFormID">When nonzero AND no <paramref name="editDraft"/> is given,
    ''' pre-load this REAL ARMA as the template on open (same as the user clicking Template then picking it) —
    ''' used by the ARMO editor's Addons tab to "edit a real addon as New".</param>
    ''' <param name="parentArmoFormID">The ARMO that contains this ARMA (real FormID for an override, or an
    ''' ARMO draft's provisional FormID). Enables the "Full armor" preview mode to render the whole parent
    ''' ARMO with this ARMA applied. 0 (default, standalone open) ⇒ "Full armor" falls back to "Only Model".</param>
    ''' <param name="outfitContextFormID">The OTFT of the outfit being assembled in the OutfitPicker. Enables
    ''' "Full Outfit" to render that outfit with this ARMA's edit substituted. 0 ⇒ fallback to the single-item
    ''' throwaway over the actor.</param>
    Public Sub New(mainForm As MainForm, previewNpcFormID As UInteger, raceFormID As UInteger, isFemale As Boolean,
                   Optional editDraft As ArmaDraft = Nothing, Optional initialTemplateArmaFormID As UInteger = 0UI,
                   Optional templateAsOverride As Boolean = True, Optional parentArmoFormID As UInteger = 0UI,
                   Optional outfitContextFormID As UInteger = 0UI)
        InitializeComponent()
        _mainForm = mainForm
        _previewNpcFormID = previewNpcFormID
        _raceFormID = raceFormID
        _isFemale = isFemale
        _parentArmoFormID = parentArmoFormID
        _outfitContextFormID = outfitContextFormID

        ' ⛔ Acá y no como inicializador de campo: un inicializador corre ANTES del cuerpo del
        ' constructor, con `_mainForm` todavía en Nothing, y los cinco delegados tiran.
        _toma = New TomaDeBorrador(Of ArmaDraft)(
            AddressOf _mainForm.TryGetArmaDraft, AddressOf _mainForm.RegisterArmaDraft,
            AddressOf _mainForm.UnregisterArmaDraft, Function(d) d.FormID,
            Function(f) OverridePristino(f, _mainForm.PluginManagerForEditor))

        BuildSlotCheckBoxes()
        SeedSculptBoneCombo()
        ConfigureForGame()

        _previewDebounce = New Timer() With {.Interval = 400}

        ' Top bar — explicit intent actions: New / New-from-template copy / Override / Edit draft.
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
        ' Slots tab: proponer los biped slots que DECLARAN los dos modelos (MOD2 male + MOD3 female).
        AddHandler ButtonSlotsFromModel.Click, AddressOf OnSlotsFromModel
        AddHandler ButtonPickRace.Click, AddressOf OnPickRace
        ' RNAM edited (picker or typed) ⇒ the race match can flip ⇒ re-gate the preview scopes. The Additional
        ' Races list re-gates from RefreshAddRacesList.
        AddHandler TextBoxRace.TextChanged, Sub() UpdatePreviewScopeGating()
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
        Dim tomado As Boolean = False
        If editDraft IsNot Nothing Then
            _draft = editDraft
            _editingExistingDraft = True   ' continuing to edit one of the user's registered drafts
            LoadDraftIntoPanels()
        Else
            _draft = ArmaDraft.Nuevo(_mainForm.AllocateDraftFormID(),
                                     Canon.CanonBridge.SessionGame())
            _draft.Record.EditorID = ArmaDraft.EditorIdPrefix & "new"
            _draft.Record.Race = _raceFormID
            LoadDraftIntoPanels()
            ' Optional pre-load of a real ARMA template — defaults to OVERRIDE (edit the existing ARMA; your
            ' plugin replaces it), the usual intent when editing an existing addon. Use the in-editor
            ' "New from template…" action for a copy-as-new instead.
            If initialTemplateArmaFormID <> 0UI Then
                tomado = LoadRealArmaTemplate(initialTemplateArmaFormID, asOverride:=templateAsOverride)
                If Not tomado Then
                    ' ⛔ AVISA SIEMPRE (gemelo del de ARMO). Por acá pasa AHORA todo «Edit ARMA…» del modal
                    ' de addons, así que una ARMA que no resuelve —un borrador ya borrado, un record que no
                    ' parsea— abría un editor EN BLANCO y mudo, y un OK recableaba la fila del addon a una
                    ' ARMA vacía recién creada.
                    MessageBox.Show(Me,
                        $"Could not open that armor addon [0x{initialTemplateArmaFormID:X8}]: it is not a record " &
                        "in the load order and has no draft in memory. The editor opened on a new, empty ARMA instead.",
                        "Cannot open this armor addon", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If
            End If
        End If
        UpdateStatusBanner()

        ' Preview hint + which scopes are offered both follow the race match — set together, from one place.
        UpdatePreviewScopeGating()

        ' Snapshot del borrador TAL COMO SE ABRIO. ⛔ UNA SOLA TOMA: si la plantilla cargo,
        ' `LoadRealArmaTemplate` ya la hizo. Ver el gemelo de ARMO.
        If Not tomado Then SnapshotCurrentDraft()
    End Sub

    ' =====================================================================
    ' One-time UI construction in code-behind (Designer rule: many repeated
    ' controls + variable grid columns are added to their containers here).
    ' =====================================================================

    Private Sub BuildSlotCheckBoxes()
        _slotChecks = BipedSlotCheckboxes.Build(FlowSlots, AddressOf OnFieldEdited)
    End Sub

    ''' <summary>Game-gate the ARMA editor. Under SKYRIM every FO4-only surface is HIDDEN, never merely disabled:
    ''' the Skyrim serializer does not read these fields, and a visible-but-inert control reads as a setting that
    ''' applied and silently did nothing. Hidden/removed:
    '''   • Sculpt tab (BSMP/BSMB/BSMS bone-scale) — FO4-only.
    '''   • Flags tab (No-Underarmor-Scaling, Has-Sculpt-Data) — FO4-only ARMA header flags. Skyrim's ARMA has no
    '''     named header flags, so its source flags are preserved verbatim.
    '''   • The MO2F/MO3F model-flags rows — those subrecords do not exist in Skyrim's ARMA.
    '''   • The MO2S/MO3S/MO4S/MO5S material-swap pickers — in Skyrim those subrecords are Alternate-Textures
    '''     arrays, not an MSWP FormID (Skyrim has no MSWP record at all). They round-trip verbatim and are
    '''     never authored here.
    ''' FO4 is unchanged.</summary>
    Private Sub ConfigureForGame()
        ' ⛔ Los controles de MSWP se apagan por lo que el ESQUEMA declara, no por el nombre del
        ' juego. La ley «Skyrim no declara MSWP» estaba escrita en TRES lugares —el esquema generado y
        ' dos listas de controles a mano— y nada ataba las listas al esquema: lo único que separaba al
        ' usuario de la excepción de `MswpDraft.Nuevo` era acordarse de nombrar cada botón acá. Se le
        ' pregunta a `SessionGame()`, que es EXACTAMENTE lo que consulta la fábrica.
        If Canon.WbSchema.Get(Canon.CanonBridge.SessionGame(), "MSWP") Is Nothing Then
            For Each c As Control In New Control() {LabelMo2s, TextBoxMo2s, ButtonPickMo2s, ButtonEditMo2s,
                                                    LabelMo3s, TextBoxMo3s, ButtonPickMo3s, ButtonEditMo3s,
                                                    LabelMo4s, TextBoxMo4s, ButtonPickMo4s,
                                                    LabelMo5s, TextBoxMo5s, ButtonPickMo5s}
                If c IsNot Nothing Then c.Visible = False
            Next
        End If
        If Config_App.Current.Game <> Config_App.Game_Enum.Skyrim Then Return
        If Tabs.TabPages.Contains(TabSculpt) Then Tabs.TabPages.Remove(TabSculpt)
        If Tabs.TabPages.Contains(TabFlags) Then Tabs.TabPages.Remove(TabFlags)
        For Each c As Control In New Control() {LabelMo2f, CheckMo2fFaceBones, CheckMo2f1stPerson,
                                                LabelMo3f, CheckMo3fFaceBones, CheckMo3f1stPerson}
            If c IsNot Nothing Then c.Visible = False
        Next
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
    ' Explicit intent actions + status banner
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
    ''' IsOverride=False), seeded with the preview race. A brand-new record built from scratch.</summary>
    Private Sub OnActionNewBlank(sender As Object, e As EventArgs)
        RevertOrDiscardCurrentDraft()
        EmpezarBorradorEnBlanco()
    End Sub

    ''' <summary>Arrancar un borrador NUEVO en blanco como objetivo, SIN abandonar nada: el abandono lo
    ''' decide el llamador. Gemelo del de ARMO; el porqué —el guardado TIRA si el editor queda sobre un
    ''' override de un FormID que ya no resuelve— está allá.</summary>
    Private Sub EmpezarBorradorEnBlanco()
        _draft = ArmaDraft.Nuevo(_mainForm.AllocateDraftFormID(), Canon.CanonBridge.SessionGame())
        _draft.Record.EditorID = ArmaDraft.EditorIdPrefix & "new"
        _draft.Record.Race = _raceFormID
        _templateRealFormID = 0UI
        _templateRealEditorID = ""
        _editingExistingDraft = False
        LoadDraftIntoPanels()
        SnapshotCurrentDraft()
        UpdateStatusBanner()
        RequestPreview()
    End Sub

    ''' <summary>"New from template…" → pick a REAL ARMA and COPY it into a NEW record (fresh draft FormID,
    ''' IsOverride=False). Race-filtered like the old Template picker.</summary>
    Private Sub OnActionNewFromTemplate(sender As Object, e As EventArgs)
        Dim fid = PickRealArma("Copy ARMA into a NEW record")
        If fid = 0UI Then Return
        If Not LoadRealArmaTemplate(fid, asOverride:=False) Then
            MessageBox.Show(Me, "Could not parse that ARMA record.", "New from template",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If
    End Sub

    ''' <summary>"Override existing…" → pick a REAL ARMA and edit it as an OVERRIDE (keep its global FormID +
    ''' EditorID, IsOverride=True); your plugin replaces that record on Save.</summary>
    Private Sub OnActionOverrideExisting(sender As Object, e As EventArgs)
        Dim fid = PickRealArma("Override an existing ARMA")
        If fid = 0UI Then Return
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
            .FormID = d.FormID, .EditorID = d.Record.EditorID,
            .DisplayName = d.Record.EditorID, .Signature = "ARMA"}).ToList()
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
            ' ⛔ La decisión «hay borrador ⇒ adoptarlo» NO se toma acá: la toma
            ' `Borradores.QueHacerAlAbrir` adentro de `LoadRealArmaTemplate`. Ver el gemelo de ARMO.
            If Not LoadRealArmaTemplate(fid, asOverride:=True) Then
                MessageBox.Show(Me, "Could not parse that ARMA record.", "Edit mine",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If
        End Using
    End Sub

    ''' <summary>Delete/revert handler for the "Edit draft…" picker's "Delete / Revert…" button. Overrides
    ''' (<c>Not IsNew</c>) revert to the original record; new drafts are deleted only when nothing references
    ''' them. Guards the draft that's currently open. Returns True when the draft was removed (picker drops the row).</summary>
    Private Function OnDeleteDraftEntry(entry As FormIdPickerEntry) As Boolean
        Dim eraElObjetivo = (_draft IsNot Nothing AndAlso entry IsNot Nothing AndAlso entry.FormID = _draft.FormID)
        Dim quitado = QuitarSegunSuClase(entry)
        ' ⛔ LA RECUPERACIÓN ES LA POSTCONDICIÓN DEL GESTO, una sola vez y después de TODAS las ramas.
        ' Ver el gemelo de ARMO: escrita adentro de cada rama eran dos copias y a una le faltaba.
        If quitado AndAlso eraElObjetivo Then ReTomarObjetivoTrasLaBaja(entry.FormID)
        Return quitado
    End Function

    ''' <summary>Volver a tomar el objetivo tras la baja: se SUELTA la toma (no se abandona, o repondría lo
    ''' que el usuario acaba de borrar) y, si el record ya no resuelve, se arranca uno NUEVO en blanco con
    ''' aviso. Gemela de la de ARMO; el porqué está allá.</summary>
    Private Sub ReTomarObjetivoTrasLaBaja(fid As UInteger)
        _toma.Soltar()
        If LoadRealArmaTemplate(fid, asOverride:=True) Then Return
        EmpezarBorradorEnBlanco()
        MessageBox.Show(Me,
            "That record no longer exists after the revert, so it can't be edited as an override. " &
            "The editor started a new, empty ARMA instead.",
            "Reverted", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    ''' <summary>El cuerpo del gesto: dar de baja lo que la fila representa y decir si se quitó. NO recupera
    ''' el editor: eso es la postcondición del llamador.</summary>
    Private Function QuitarSegunSuClase(entry As FormIdPickerEntry) As Boolean
        Dim fid = entry.FormID
        If fid = 0UI Then Return False
        Dim isCurrent = (_draft IsNot Nothing AndAlso fid = _draft.FormID)
        Dim d = _mainForm.TryGetArmaDraft(fid)
        If d IsNot Nothing Then
            If Not d.IsNew Then
                ' OVERRIDE draft → REVERT (discard my edits; the original record wins). Allowed even when it's the
                ' one currently open — we reload the pristine original so the editor stays in a valid state.
                If MessageBox.Show(Me, $"Revert '{d.Record.EditorID}' to the original record? " &
                                   "Your edits to this draft will be discarded.",
                                   "Revert to original", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return False
                _mainForm.UnregisterArmaDraft(fid)
                ' Dropping the in-memory draft is NOT enough: if this override was ALREADY SAVED into the plugin, the
                ' saver's Phase 2a re-preserves it (re-emits every target-plugin ARMA as an OVERRIDE entry unless it's
                ' in RecordsToRemove), so the reverted record would keep getting written. Mark it for removal so Phase 2a
                ' drops it and the true parent wins. No-op when no saved copy exists (removal only drops target-plugin
                ' records); when isCurrent reloads a pristine override draft it stays Not-IsDirty and is never re-emitted.
                _mainForm.MarkRecordForRemoval(fid)
                _mainForm.RevertAppOverrideInMemory(fid)   ' restore the mod's winning record in memory (not the ESP override)
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
        _mainForm.RevertAppOverrideInMemory(fid)   ' in-memory: restore the mod's winning record (override) / drop it (new)
        Return True
    End Function

    ''' <summary>FormIdPicker sobre las ARMA del orden de carga MÁS los borradores PROPIOS del usuario,
    ''' filtrado por la raza del NPC de preview. Lo comparten «New from template…» y «Override existing…».
    ''' Gemelo del de <c>ArmoEditor_Form.PickRealArmo</c>: el porqué de que entren los borradores, y de que
    ''' ESCAPEN al filtro de raza, está allá.</summary>
    Private Function PickRealArma(title As String) As UInteger
        Dim drafts = _mainForm.ArmaDrafts().Select(Function(d) New FormIdPickerEntry With {
            .FormID = d.FormID, .EditorID = d.Record.EditorID,
            .DisplayName = d.Record.EditorID, .Signature = "ARMA"}).ToList()
        ' Pre-select the CURRENT source record (if any) so switching Override ⇄ New-from-template keeps it selected.
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"ARMA"},
                                           title, CurrentTemplateSelection(), allowNull:=False,
                                           extraDraftEntries:=drafts,
                                           formIdFilter:=Function(fid) _mainForm.TryGetArmaDraft(fid) IsNot Nothing _
                                                                       OrElse _mainForm.IsArmaRaceCompatible(fid, _raceFormID))
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

    ''' <summary>TOMAR <paramref name="fid"/> como objetivo de edición, venga de donde venga: es la ÚNICA
    ''' puerta, y por eso también es la que abandona el borrador que se está dejando. Adopta el borrador que
    ''' ya exista bajo ese FormID, clona DESDE él cuando piden copia, o construye del disco cuando no hay
    ''' ninguno — la decisión es de <see cref="Borradores.QueHacerAlAbrir"/>.
    ''' <para>Devuelve False cuando no se pudo construir nada; el borrador actual queda intacto y SIGUE
    ''' registrado. ⛔ El llamador tiene que MIRAR ese False. Gemela de la de ARMO; el detalle está allá.</para></summary>
    Private Function LoadRealArmaTemplate(fid As UInteger, asOverride As Boolean) As Boolean
        ' ⛔ SE PREGUNTA POR EL BORRADOR ANTES DE IR AL DISCO — `Borradores.QueHacerAlAbrir`. Gemelo del
        ' de ARMO; el porqué está allá.
        Dim borrador As ArmaDraft = Nothing
        Dim accion = Borradores.QueHacerAlAbrir(fid, asOverride, AddressOf _mainForm.TryGetArmaDraft, borrador)
        ' ⛔ SE RESUELVE ANTES DE ABANDONAR, y se abandona DESPUÉS DE CONSTRUIR. Gemelo del de ARMO; el
        ' porqué de las dos cosas —y de que no se abandone la fuente de una copia— está allá.
        Select Case accion
            Case Borradores.AccionAlAbrir.Adoptar
                If ReferenceEquals(borrador, _draft) Then Return True
                RevertOrDiscardCurrentDraft()
                AdoptarBorradorRegistrado(borrador)
                Return True
            Case Borradores.AccionAlAbrir.ClonarDeBorrador
                Dim copiaDeBorrador = BuildDraftFromExisting(fid, asOverride:=False, origenBorrador:=borrador)
                If copiaDeBorrador Is Nothing Then Return False
                ' ⛔ Abandono INCONDICIONAL después de construir, también si la fuente es el borrador
                ' actual: la copia ya es independiente y no abandonarla dejaba un borrador sin aceptar
                ' que el guardado emite igual. Ver el gemelo de ARMO.
                RevertOrDiscardCurrentDraft()
                Return CargarComoObjetivo(copiaDeBorrador, fid)
            Case Else
                Dim copy = BuildDraftFromExisting(fid, asOverride)
                If copy Is Nothing Then Return False
                RevertOrDiscardCurrentDraft()
                Return CargarComoObjetivo(copy, fid)
        End Select
    End Function

    ''' <summary>La cola común de las dos ramas que construyen un borrador NUEVO como objetivo.</summary>
    Private Function CargarComoObjetivo(copy As ArmaDraft, fidOrigen As UInteger) As Boolean
        _draft = copy
        _templateRealFormID = fidOrigen
        _templateRealEditorID = copy.Record.EditorID
        _editingExistingDraft = False
        LoadDraftIntoPanels()
        SnapshotCurrentDraft()
        UpdateStatusBanner()
        RequestPreview()
        Return True
    End Function

    ''' <summary>Tomar un borrador YA REGISTRADO como objetivo de edición. Gemela de la de
    ''' <c>ArmoEditor_Form</c>; el porqué está allá.</summary>
    Private Sub AdoptarBorradorRegistrado(d As ArmaDraft)
        _draft = d
        _templateRealFormID = 0UI
        _templateRealEditorID = ""
        _editingExistingDraft = True
        LoadDraftIntoPanels()
        SnapshotCurrentDraft()
        UpdateStatusBanner()
        RequestPreview()
    End Sub

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
            ' La línea del plugin depende de que el borrador SEA un override, no de cómo se abrió: ahora
            ' la apertura por plantilla ADOPTA el que ya existe. Gemelo del de ARMO.
            Dim plugAdoptado = SourcePluginName(_draft.FormID)
            Dim tailAdoptado = If(String.IsNullOrEmpty(plugAdoptado), "",
                                  $" · {plugAdoptado} → your plugin replaces it")
            LabelStatusBanner.Text = If(_draft.IsOverride,
                $"Editing draft — {edid} (override{tailAdoptado}) [0x{_draft.FormID:X8}]",
                $"Editing draft — {edid} (new)")
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
        If fid = 0UI OrElse Borradores.EsFormIdDeBorrador(fid) Then Return ""
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
    ' Load a real ARMA into a draft (copy of every editor-relevant field)
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
    ''' <param name="origenBorrador">Cuando viene, el CLON se copia de ESTE borrador propio en vez del
    ''' record del disco. Gemelo del de <c>ArmoEditor_Form</c>; el porqué está allá.</param>
    Private Function BuildDraftFromExisting(fid As UInteger, asOverride As Boolean,
                                            Optional origenBorrador As ArmaDraft = Nothing) As ArmaDraft
        Dim pm = _mainForm.PluginManagerForEditor
        If pm Is Nothing Then Return Nothing
        ' La guarda del record vale SÓLO para lo que va al DISCO: un borrador NUEVO no tiene `PluginRecord`.
        If origenBorrador Is Nothing Then
            Dim recGuarda = pm.GetRecord(fid)
            If recGuarda Is Nothing OrElse recGuarda.Header.Signature <> "ARMA" Then Return Nothing
        End If
        Dim d As ArmaDraft
        If asOverride Then
            d = OverridePristino(fid, pm)
        ElseIf origenBorrador IsNot Nothing Then
            d = ArmaDraft.ClonDeBorrador(origenBorrador, _mainForm.AllocateDraftFormID())
        Else
            d = ArmaDraft.Clon(pm.GetRecord(fid), pm, _mainForm.AllocateDraftFormID())
        End If
        If d Is Nothing Then Return Nothing
        SintetizarEdidSiFalta(d, fid)
        d.IsModified = False
        Return d
    End Function

    ''' <summary>El EditorID se SINTETIZA sólo si el record no traía uno. Gemela de la de
    ''' <c>ArmoEditor_Form</c>; el porqué —que la recorran los DOS caminos, el borrador y su línea de
    ''' base— está allá.</summary>
    Friend Shared Sub SintetizarEdidSiFalta(d As ArmaDraft, fidOrigen As UInteger)
        If d Is Nothing OrElse d.Record Is Nothing Then Return
        If String.IsNullOrEmpty(d.Record.EditorID) Then
            d.Record.EditorID = ArmaDraft.EditorIdPrefix & fidOrigen.ToString("X8")
        End If
    End Sub

    ''' <summary>El borrador OVERRIDE PRISTINO de una ARMA real: LA FÁBRICA que usan los dos lados —el
    ''' borrador que el usuario edita y la línea de base contra la que se decide la suciedad—. Gemela de la
    ''' de <c>ArmoEditor_Form</c>; el porqué está allá. <c>Friend Shared</c> para que un testigo la recorra
    ''' sin instanciar el formulario.</summary>
    Friend Shared Function OverridePristino(fid As UInteger, pm As PluginManager) As ArmaDraft
        If pm Is Nothing Then Return Nothing
        Return OverridePristino(pm.GetRecord(fid), pm)
    End Function

    ''' <summary>LA MISMA FÁBRICA, por RECORD: la puerta por la que un testigo le mete un sujeto que no
    ''' está en <c>AllRecords</c>. Gemela de la de <c>ArmoEditor_Form</c>; el porqué está allá.</summary>
    Friend Shared Function OverridePristino(rec As PluginRecord, pm As PluginManager) As ArmaDraft
        If pm Is Nothing OrElse rec Is Nothing OrElse rec.Header.Signature <> "ARMA" Then Return Nothing
        Dim d = ArmaDraft.Edicion(rec, pm)
        If d Is Nothing Then Return Nothing
        SintetizarEdidSiFalta(d, rec.Header.FormID)
        d.IsModified = False
        Return d
    End Function

    ''' <summary>Vuelca el sculpt (BSMP/BSMB/BSMS) del parser viejo al Sculpt Data del record nuevo.
    ''' FO4-only: llamar sólo con un <see cref="Canon.ArmaFO4"/> ya confirmado.</summary>
    Friend Shared Sub WriteBoneScaleDataIntoRecord(fo4 As Canon.ArmaFO4,
                                                   source As List(Of ARMA_BoneScaleGender))
        If source Is Nothing Then Return
        For Each g In source
            If g.Bones Is Nothing OrElse g.Bones.Count = 0 Then Continue For
            Dim sculpt = fo4.AgregarSculptData()
            If sculpt Is Nothing Then Continue For
            sculpt.BoneScaleModifierSetTargetGender = g.Gender
            For Each b In g.Bones
                Dim bone = sculpt.AgregarBoneScaleModifiers()
                If bone Is Nothing Then Continue For
                bone.BoneScaleModifierBoneName = b.BoneName
                bone.BoneScaleDeltaX = b.DeltaX
                bone.BoneScaleDeltaY = b.DeltaY
                bone.BoneScaleDeltaZ = b.DeltaZ
            Next
        Next
    End Sub

    ''' <summary>Lee el Sculpt Data de un gender puntual de vuelta al formato viejo (BSMP/BSMB/BSMS)
    ''' que consume <see cref="SclpFile"/>. FO4-only; Nothing en Skyrim o sin bloque
    ''' para ese gender.</summary>
    Friend Shared Function ReadBoneScaleGenderFromRecord(fo4 As Canon.ArmaFO4,
                                                         gender As UInteger) As ARMA_BoneScaleGender
        If fo4 Is Nothing Then Return Nothing
        For Each sculpt In fo4.SculptData
            If sculpt.BoneScaleModifierSetTargetGender <> gender Then Continue For
            Dim block As New ARMA_BoneScaleGender With {.Gender = gender}
            For Each b In sculpt.BoneScaleModifiers
                block.Bones.Add(New ARMA_BoneScaleDelta With {
                    .BoneName = b.BoneScaleModifierBoneName, .DeltaX = b.BoneScaleDeltaX,
                    .DeltaY = b.BoneScaleDeltaY, .DeltaZ = b.BoneScaleDeltaZ})
            Next
            Return block
        Next
        Return Nothing
    End Function

    ''' <summary>Lee TODO el Sculpt Data de vuelta al formato viejo, un bloque por gender presente en el
    ''' record (sin asumir sólo 0/1: un mod puede declarar cualquier valor). FO4-only; lista vacía en
    ''' Skyrim o sin sculpt. Inverso de <see cref="WriteBoneScaleDataIntoRecord"/>.</summary>
    Friend Shared Function ReadAllBoneScaleFromRecord(fo4 As Canon.ArmaFO4) As List(Of ARMA_BoneScaleGender)
        Dim result As New List(Of ARMA_BoneScaleGender)
        If fo4 Is Nothing Then Return result
        For Each sculpt In fo4.SculptData
            Dim block As New ARMA_BoneScaleGender With {.Gender = sculpt.BoneScaleModifierSetTargetGender}
            For Each b In sculpt.BoneScaleModifiers
                block.Bones.Add(New ARMA_BoneScaleDelta With {
                    .BoneName = b.BoneScaleModifierBoneName, .DeltaX = b.BoneScaleDeltaX,
                    .DeltaY = b.BoneScaleDeltaY, .DeltaZ = b.BoneScaleDeltaZ})
            Next
            result.Add(block)
        Next
        Return result
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
            Dim rec = _draft.Record
            Dim fo4 = TryCast(rec, Canon.ArmaFO4)

            ' Models.
            TextBoxMod2.Text = rec.MaleModelFilename
            TextBoxMod3.Text = rec.FemaleModelFilename
            TextBoxMod4.Text = rec.MaleModelFilename2
            TextBoxMod5.Text = rec.FemaleModelFilename2
            ' MO2F/MO3F (model flags) sólo existen en Fallout 4.
            Dim maleFlags = If(fo4 IsNot Nothing, fo4.MaleFlags, CByte(0))
            Dim femaleFlags = If(fo4 IsNot Nothing, fo4.FemaleFlags, CByte(0))
            CheckMo2fFaceBones.Checked = (maleFlags And &H1) <> 0
            CheckMo2f1stPerson.Checked = (maleFlags And &H2) <> 0
            CheckMo3fFaceBones.Checked = (femaleFlags And &H1) <> 0
            CheckMo3f1stPerson.Checked = (femaleFlags And &H2) <> 0

            ' Slots.
            ' Por SlotMaskDe: en Skyrim el 85 % de los ARMA trae la plantilla por BODT, y leer
            ' BOD2 a pelo devuelve 0. Los checkboxes salían todos apagados y el volcado escribía
            ' ESE 0 de vuelta: el slot mask se borraba sin que nadie dijera nada.
            SetSlotChecks(rec.SlotMaskDe())

            ' Skin & material.
            ' ⛔ El llenado va POR EL PORTADOR, no por una segunda transcripción del mismo mapeo.
            ' Con DOS listas —ésta y `LeerReferencias`— la que se olvida un campo deja la caja vacía,
            ' `GetFid` devuelve 0 y la ley de `AplicarReferencias` BORRA el subrecord; y el gate E-1 no
            ' lo ve, porque usa `LeerReferencias` y nunca ejecuta este llenado. Con el portador en el
            ' medio, el testigo recorre EL MISMO lector que producción.
            ' MO2S..MO5S (swap de material) sólo existen en Fallout 4 — Skyrim no tiene MSWP, y el
            ' portador ya devuelve 0 ahí.
            Dim refsCarga = LeerReferencias(rec)
            SetFidText(TextBoxRace, refsCarga.Raza)
            SetFidText(TextBoxSndd, refsCarga.SonidoDePaso)
            SetFidText(TextBoxNam0, refsCarga.PielMasc)
            SetFidText(TextBoxNam1, refsCarga.PielFem)
            SetFidText(TextBoxNam2, refsCarga.SwapPielMasc)
            SetFidText(TextBoxNam3, refsCarga.SwapPielFem)
            SetFidText(TextBoxMo2s, refsCarga.SwapMasc)
            SetFidText(TextBoxMo3s, refsCarga.SwapFem)
            SetFidText(TextBoxMo4s, refsCarga.SwapMasc2)
            SetFidText(TextBoxMo5s, refsCarga.SwapFem2)
            SetFidText(TextBoxOnam, refsCarga.ObjetoDeArte)
            NumMalePrio.Value = ClampDec(CDec(rec.DataMalePriority), NumMalePrio)
            NumFemalePrio.Value = ClampDec(CDec(rec.DataFemalePriority), NumFemalePrio)
            NumDetectionSound.Value = ClampDec(CDec(rec.DataDetectionSoundValue), NumDetectionSound)
            NumWeaponAdjust.Value = ClampDec(CDec(rec.DataWeaponAdjust), NumWeaponAdjust)
            CheckMaleWeight.Checked = (rec.DataWeightSliderMale And &H2) <> 0
            CheckFemaleWeight.Checked = (rec.DataWeightSliderFemale And &H2) <> 0
            RefreshAddRacesList()

            ' Sculpt: expand BSMS deltas → absolute per-gender working sets.
            _sculptByGender(0UI) = AbsRowsForGender(0UI)
            _sculptByGender(1UI) = AbsRowsForGender(1UI)
            _sculptShownGender = If(RadioSculptFemale.Checked, 1UI, 0UI)
            LoadSculptGrid(_sculptShownGender)

            ' Flags. FO4-only, ver CommitPanelsToDraft para la escritura.
            CheckNoUnderarmorScaling.Checked = (fo4 IsNot Nothing AndAlso fo4.NoUnderarmorScaling)
            CheckHasSculptData.Checked = (fo4 IsNot Nothing AndAlso fo4.HasSculptData)
        Finally
            _loading = False
        End Try
        UpdatePreviewScopeGating()
    End Sub

    ''' <summary>Absolute sculpt rows for a gender, from the draft's BSMS delta block (delta + 1.0).
    ''' FO4-only.</summary>
    Private Function AbsRowsForGender(gender As UInteger) As List(Of SclpFile.SclpBoneAbsolute)
        Dim fo4 = TryCast(_draft.Record, Canon.ArmaFO4)
        Dim block = ReadBoneScaleGenderFromRecord(fo4, gender)
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

    ''' <summary>Limpiar el borrador ACTUAL cuando el editor se abandona SIN aceptar — al cambiar de acción
    ''' de intención, o al cerrar por Cancel / la X de la ventana.
    ''' <para>La ley entera vive en <see cref="Borradores.QueHacerAlAbandonar"/> y la aplica
    ''' <see cref="TomaDeBorrador(Of TD)"/>: la pregunta es «¿el registro que hay bajo este FormID lo puse
    ''' YO?». Antes preguntaba «¿me pasaron el borrador por constructor?» y por eso un OVERRIDE abierto por
    ''' plantilla daba de baja, por FormID, el borrador que el usuario había construido antes.</para></summary>
    Private Sub RevertOrDiscardCurrentDraft()
        _toma.Abandonar()
    End Sub

    ''' <summary>Re-take the pre-edit baseline of the CURRENT draft (in the ctor and after each intent action
    ''' loads/creates a new <see cref="_draft"/>), so <see cref="RevertOrDiscardCurrentDraft"/> targets the right
    ''' state instead of the very first draft opened.</summary>
    Private Sub SnapshotCurrentDraft()
        If _draft Is Nothing Then Return
        ' ⛔ Con `Try`, por lo mismo que en `CommitProtegido`: `Clone()` tiene una precondición que
        ' puede tirar, esto corre desde cuatro manejadores sin `Try`, y la app usa
        ' `UnhandledExceptionMode.ThrowException` — un throw acá CIERRA la app. Sin snapshot no hay
        ' reversión, que es peor que tenerla; con la app cerrada no hay nada.
        ' ⛔ Y la SEGUNDA consecuencia, declarada: sin snapshot, el cálculo de «no cambió nada»
        ' (`_openSnapshot IsNot Nothing AndAlso …`) da False, o sea que un OVERRIDE abierto y aceptado
        ' sin tocar nada sale marcado como modificado y se emite al .esp como override redundante.
        ' Es la dirección SEGURA a propósito: el otro error —darlo por no modificado— perdería un
        ' cambio real del usuario. Un override de más es ruido; un cambio perdido es daño.
        Try
            _openSnapshot = _draft.Clone()
        Catch ex As Exception
            _openSnapshot = Nothing
            Logger.Log(ex.ToString())
        End Try
        ' ⛔ EN EL MISMO GESTO que el snapshot y ANTES del primer volcado. El porqué del orden está en
        ' `TomaDeBorrador.Tomar` y en el gemelo de `ArmoEditor_Form`.
        _toma.Tomar(_draft, _openSnapshot)
    End Sub

    ''' <summary>Commit the panel state into <see cref="_draft"/> and register it on MainForm. When
    ''' <paramref name="validate"/> the EditorID is checked (non-empty + unique for New); on failure a message
    ''' is shown and the draft is NOT registered. Returns True on a successful commit.</summary>

    ''' <summary>Las REFERENCIAS que este editor escribe. Existe para que el volcado sea invocable
    ''' SIN UI: el editor la llena desde los controles y un testigo desde un record, y los dos
    ''' terminan en el MISMO <see cref="AplicarReferencias"/>.
    ''' <para>⛔ Sin esto, el único camino que escribe referencias vive dentro de un <c>Form</c> y
    ''' ningún gate puede llegar: un testigo que sólo ejercite la CONSTRUCCIÓN sale verde con el
    ''' defecto vivo, porque quien recreaba las nulas era el VOLCADO.</para></summary>
    Friend Class ReferenciasDeArma
        Public Raza As UInteger
        Public SonidoDePaso As UInteger
        Public PielMasc As UInteger
        Public PielFem As UInteger
        Public SwapPielMasc As UInteger
        Public SwapPielFem As UInteger
        Public SwapMasc As UInteger
        Public SwapFem As UInteger
        Public SwapMasc2 As UInteger
        Public SwapFem2 As UInteger
        Public ObjetoDeArte As UInteger
    End Class

    ''' <summary>Lee las referencias de un record: es «abrir el editor y no tocar nada».</summary>
    Friend Shared Function LeerReferencias(a As Canon.IArma) As ReferenciasDeArma
        Dim r As New ReferenciasDeArma
        If a Is Nothing Then Return r
        Dim af = TryCast(a, Canon.ArmaFO4)
        r.Raza = a.Race
        r.SonidoDePaso = a.FootstepSound
        r.PielMasc = a.MaleSkinTexture
        r.PielFem = a.FemaleSkinTexture
        r.SwapPielMasc = a.MaleSkinTextureSwapList
        r.SwapPielFem = a.FemaleSkinTextureSwapList
        If af IsNot Nothing Then r.SwapMasc = af.MaleMaterialSwap
        If af IsNot Nothing Then r.SwapFem = af.FemaleMaterialSwap
        If af IsNot Nothing Then r.SwapMasc2 = af.MaleMaterialSwap2
        If af IsNot Nothing Then r.SwapFem2 = af.FemaleMaterialSwap2
        r.ObjetoDeArte = a.ArtObject
        Return r
    End Function

    ''' <summary>Escribe las referencias en el record aplicando la ley: «sin valor» SACA el campo, no
    ''' graba un 0. La raza queda afuera a propósito — ver el comentario de abajo.</summary>
    Friend Shared Sub AplicarReferencias(v As ReferenciasDeArma, rec As Canon.IArma)
        ' ⛔ TIRA, no vuelve callado — igual que `ReidentificarComoClon`. Los dos son errores de
        ' LLAMADOR: en producción `rec` sale de `_draft.Record`, que la cuarta ley ya garantiza, y
        ' `v` se arma en la línea de arriba. Con el `Return` mudo, el testigo que le pase un cast
        ' fallido no corre el volcado, los bytes ya eran iguales y el caso sale VERDE con el camino
        ' sin recorrer: la forma exacta en que un gate pasa en vacío.
        If rec Is Nothing OrElse v Is Nothing Then
            Throw New ArgumentException(
                "AplicarReferencias necesita el record de ARMA y su portador: con alguno en Nothing el " &
                "volcado no corre y el editor descartaría en silencio lo que el usuario acaba de escribir.")
        End If
        Dim fo4 = TryCast(rec, Canon.ArmaFO4)
        ' ⛔ RNAM NO pasa por la ley. El esquema lo da por opcional, pero está en 5.825 de 5.825
        ' ARMA y ARMO de los dos juegos y no declara NULL: una caja de raza vacía no es «borrar la
        ' raza», es entrada inválida. Sacarlo dejaría un record sin raza, en silencio; escribir 0
        ' dejaría una referencia nula. Así que no se toca, y el commit lo rechaza al validar.
        Canon.CanonInterpretacion.PonerReferenciaRequerida(v.Raza, Sub(x) rec.Race = x)
        Canon.CanonInterpretacion.PonerReferenciaOpcional(v.SonidoDePaso, Sub(x) rec.FootstepSound = x, Sub() rec.FootstepSoundPresente = False)
        Canon.CanonInterpretacion.PonerReferenciaOpcional(v.PielMasc, Sub(x) rec.MaleSkinTexture = x, Sub() rec.MaleSkinTexturePresente = False)
        Canon.CanonInterpretacion.PonerReferenciaOpcional(v.PielFem, Sub(x) rec.FemaleSkinTexture = x, Sub() rec.FemaleSkinTexturePresente = False)
        Canon.CanonInterpretacion.PonerReferenciaOpcional(v.SwapPielMasc, Sub(x) rec.MaleSkinTextureSwapList = x, Sub() rec.MaleSkinTextureSwapListPresente = False)
        Canon.CanonInterpretacion.PonerReferenciaOpcional(v.SwapPielFem, Sub(x) rec.FemaleSkinTextureSwapList = x, Sub() rec.FemaleSkinTextureSwapListPresente = False)
        If fo4 IsNot Nothing Then
            Canon.CanonInterpretacion.PonerReferenciaOpcional(v.SwapMasc, Sub(x) fo4.MaleMaterialSwap = x, Sub() fo4.MaleMaterialSwapPresente = False)
        End If
        If fo4 IsNot Nothing Then
            Canon.CanonInterpretacion.PonerReferenciaOpcional(v.SwapFem, Sub(x) fo4.FemaleMaterialSwap = x, Sub() fo4.FemaleMaterialSwapPresente = False)
        End If
        If fo4 IsNot Nothing Then
            Canon.CanonInterpretacion.PonerReferenciaOpcional(v.SwapMasc2, Sub(x) fo4.MaleMaterialSwap2 = x, Sub() fo4.MaleMaterialSwap2Presente = False)
        End If
        If fo4 IsNot Nothing Then
            Canon.CanonInterpretacion.PonerReferenciaOpcional(v.SwapFem2, Sub(x) fo4.FemaleMaterialSwap2 = x, Sub() fo4.FemaleMaterialSwap2Presente = False)
        End If
        Canon.CanonInterpretacion.PonerReferenciaOpcional(v.ObjetoDeArte, Sub(x) rec.ArtObject = x, Sub() rec.ArtObjectPresente = False)
    End Sub

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
            Dim currentEdid = _draft.Record.EditorID
            If _draft.IsOverride Then
                If Not String.Equals(edid, _templateRealEditorID, StringComparison.OrdinalIgnoreCase) _
                   AndAlso Not String.Equals(edid, currentEdid,
                                             StringComparison.OrdinalIgnoreCase) Then
                    If MessageBox.Show(Me, "Changing the EditorID of an override record is unusual. Keep this change?",
                                       "Apply", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
                        Return False
                    End If
                End If
                ' ⛔ Por IDENTIDAD y no por texto: un clon hereda el EditorID de su fuente. Ver el gemelo
                ' de ARMO y `MainForm.IsRecordEditorIdAvailable`.
            ElseIf Not _mainForm.IsRecordEditorIdAvailable(edid, _draft) Then
                MessageBox.Show(Me, $"EditorID '{edid}' is already in use. Choose another.", "Apply",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return False
            End If
            ' ⛔ LA RAZA ES OBLIGATORIA EN LA PRÁCTICA, aunque el esquema la declare opcional: está en
            ' 5.825 de 5.825 ARMA de los dos juegos y no declara NULL. Dejarla vacía no es «borrarla»,
            ' es entrada inválida — y sin este rechazo el volcado la conserva callado, que es peor que
            ' avisar.
            If GetFid(TextBoxRace) = 0UI Then
                MessageBox.Show(Me, "Pick a race (RNAM): every ARMA in the game carries one.",
                                "Apply", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return False
            End If
        End If
        Dim rec = _draft.Record
        rec.EditorID = edid
        Dim fo4 = TryCast(rec, Canon.ArmaFO4)

        ' Models.
        rec.MaleModelFilename = TextBoxMod2.Text.Trim()
        rec.FemaleModelFilename = TextBoxMod3.Text.Trim()
        rec.MaleModelFilename2 = TextBoxMod4.Text.Trim()
        rec.FemaleModelFilename2 = TextBoxMod5.Text.Trim()
        ' MO2F/MO3F, MO2S..MO5S y el sculpt sólo existen en Fallout 4 — en Skyrim esos controles
        ' están ocultos (ConfigureForGame) y no hay adónde escribirlos.
        If fo4 IsNot Nothing Then
            fo4.MaleFlags = BuildModelFlags(CheckMo2fFaceBones.Checked, CheckMo2f1stPerson.Checked)
            fo4.FemaleFlags = BuildModelFlags(CheckMo3fFaceBones.Checked,
                                              CheckMo3f1stPerson.Checked)
        End If

        ' Slots.
        ' Por la rama QUE EL RECORD TRAE, no siempre BOD2: en Skyrim un ARMA con BODT quedaba
        ' con las dos ramas y al releerlo todo lo que sigue caía en passthrough. La ley vive en
        ' un solo lugar, junto a su lectura (SlotMaskDe).
        rec.PonerSlotMaskEn(ReadSlotChecks())

        ' Skin & material.
        ' ⛔ RNAM NO pasa por PonerRef. El esquema lo da por opcional, pero está en 5.825 de 5.825 ARMA
        ' y ARMO de los dos juegos y no declara NULL: una caja de raza vacía no es «borrar la raza», es
        ' entrada inválida. Sacarlo dejaría un ARMA sin raza, en silencio; escribir 0 dejaría una
        ' referencia nula. Así que no se toca, y `validate` lo rechaza (ver arriba).
        ' Las referencias van por el portador + AplicarReferencias, que son `Friend Shared` y por eso
        ' invocables sin UI: es lo que permite que un testigo recorra ESTE camino y no sólo la
        ' construcción. Lo que se lee de los controles es el Único paso que no se puede sacar del Form.
        Dim refs As New ReferenciasDeArma
        refs.Raza = GetFid(TextBoxRace)
        refs.SonidoDePaso = GetFid(TextBoxSndd)
        refs.PielMasc = GetFid(TextBoxNam0)
        refs.PielFem = GetFid(TextBoxNam1)
        refs.SwapPielMasc = GetFid(TextBoxNam2)
        refs.SwapPielFem = GetFid(TextBoxNam3)
        refs.SwapMasc = GetFid(TextBoxMo2s)
        refs.SwapFem = GetFid(TextBoxMo3s)
        refs.SwapMasc2 = GetFid(TextBoxMo4s)
        refs.SwapFem2 = GetFid(TextBoxMo5s)
        refs.ObjetoDeArte = GetFid(TextBoxOnam)
        AplicarReferencias(refs, rec)
        rec.DataMalePriority = CByte(NumMalePrio.Value)
        rec.DataFemalePriority = CByte(NumFemalePrio.Value)
        rec.DataDetectionSoundValue = CByte(NumDetectionSound.Value)
        rec.DataWeaponAdjust = CSng(NumWeaponAdjust.Value)
        rec.DataWeightSliderMale = If(CheckMaleWeight.Checked, CByte(&H2), CByte(0))
        rec.DataWeightSliderFemale = If(CheckFemaleWeight.Checked, CByte(&H2), CByte(0))

        ' Sculpt: rebuild Sculpt Data from both gender working sets (skips identity rows). FO4-only.
        If fo4 IsNot Nothing Then
            While fo4.SculptData.Count > 0
                If Not fo4.QuitarSculptData(0) Then Exit While
            End While
            For Each gender As UInteger In {0UI, 1UI}
                Dim block = SclpFile.ToGenderBlock(_sculptByGender(gender), gender)
                If block.Bones.Count > 0 Then
                    Dim oneBlock As New List(Of ARMA_BoneScaleGender) From {block}
                    WriteBoneScaleDataIntoRecord(fo4, oneBlock)
                End If
            Next
        End If

        ' Flags. La cabecera no sale del árbol de campos, pero viaja en el contexto del record —de
        ' ahí la toma el grabado— así que escribirla acá sí es editar lo que se va a guardar.
        ' HasSculptData se auto-prende cuando hay huesos de sculpt, si no honra el checkbox.
        If fo4 IsNot Nothing Then
            fo4.NoUnderarmorScaling = CheckNoUnderarmorScaling.Checked
            fo4.HasSculptData = CheckHasSculptData.Checked OrElse fo4.SculptData.Count > 0
        End If

        ' Sucio sólo ante un cambio REAL, y contra la LÍNEA DE BASE PRISTINA, no contra el estado de
        ' apertura: la ley y su porqué viven en `Borradores.SucioContraLaBase`. Acá mordía por el camino
        ' anidado —`ArmoAddonEditor` reabre esta ARMA pasándola como `editDraft`—, así que aceptar sin
        ' retocar una ARMA ya editada la dejaba LIMPIA y el saver no la emitía: la malla que el usuario
        ' había cambiado no llegaba al .esp. Dos direcciones, ahora también entre sesiones.
        If Not _draft.IsNew Then
            _draft.IsModified = _toma.Sucio(_draft.ContentEquals(_toma.Base))
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

    ''' <summary>Open a FormIdPicker over <paramref name="sigs"/> (the allowed record signatures for
    ''' the field) seeded with the textbox's current FormID, and write the chosen FormID (or 0/NULL) back as a
    ''' "Name [0xFORMID]" display. MSWP fields additionally pass the in-memory MSWP drafts so an unsaved swap is
    ''' selectable.</summary>
    Private Sub PickFidInto(target As TextBox, sigs As String(), title As String, allowNull As Boolean,
                            Optional includeMswpDrafts As Boolean = False)
        Dim drafts As List(Of FormIdPickerEntry) = Nothing
        Dim alBorrar As Func(Of FormIdPickerEntry, Boolean) = Nothing
        If includeMswpDrafts Then
            drafts = _mainForm.MswpDrafts().Select(Function(d) New FormIdPickerEntry With {
                .FormID = d.FormID, .EditorID = d.Record.EditorID, .DisplayName = d.Record.EditorID, .Signature = "MSWP"}).ToList()
            ' ⛔ Y CON SU CAMINO DE BAJA: ver la nota gemela en `ArmoEditor_Form.PickFidInto`. Sin esto el
            ' botón «Delete / Revert…» ni se ve, y el MSWP queda sin salida después del OK.
            alBorrar = Function(en) BorradoDeMswp.BorrarORevertir(Me, _mainForm, en)
        End If
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, sigs, title, GetFid(target),
                                           allowNull, drafts, onDeleteEntry:=alBorrar)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                SetFidText(target, dlg.SelectedFormID)
                OnFieldEdited(Me, EventArgs.Empty)
            End If
        End Using
    End Sub

    ''' <summary>Los FormID de Additional Races, en orden — la lista generada es de vistas
    ''' (una por MODL), no de UInteger sueltos.</summary>
    Private Function AdditionalRaceFids() As List(Of UInteger)
        Return _draft.Record.AdditionalRaces.Select(Function(x) x.Race).ToList()
    End Function

    Private Sub OnAddRace(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"RACE"},
                                           "Add additional race (MODL)", 0UI, allowNull:=False)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            If Not AdditionalRaceFids().Contains(dlg.SelectedFormID) Then
                Dim e2 = _draft.Record.AgregarAdditionalRaces()
                If e2 IsNot Nothing Then e2.Race = dlg.SelectedFormID
                ' ⛔ QUIEN MUTA, PUBLICA: el render lee la FOTO del borrador, no el arbol vivo.
                _mainForm.PublicarBorradorDeArma(_draft)
                RefreshAddRacesList()
            End If
        End Using
    End Sub

    Private Sub OnRemoveRace(sender As Object, e As EventArgs)
        If ListAddRaces.SelectedItems.Count = 0 Then Return
        Dim fid = CUInt(ListAddRaces.SelectedItems(0).Tag)
        Dim races = _draft.Record.AdditionalRaces
        For i = 0 To races.Count - 1
            If races(i).Race = fid Then
                _draft.Record.QuitarAdditionalRaces(i)
                Exit For
            End If
        Next
        ' ⛔ QUIEN MUTA, PUBLICA: ver `MainForm.PublicarBorradorDeArma`.
        _mainForm.PublicarBorradorDeArma(_draft)
        RefreshAddRacesList()
    End Sub

    Private Sub RefreshAddRacesList()
        ListAddRaces.BeginUpdate()
        Try
            ListAddRaces.Items.Clear()
            For Each r In AdditionalRaceFids()
                Dim row As New ListViewItem(DisplayFor(r))
                row.Tag = r
                ListAddRaces.Items.Add(row)
            Next
        Finally
            ListAddRaces.EndUpdate()
        End Try
        UpdatePreviewScopeGating()
    End Sub

    ''' <summary>True when the ARMA AS EDITED (RNAM + Additional Races straight off the panels, uncommitted)
    ''' passes the engine's per-ARMA race match against the race the preview actor is actually rendered as
    ''' (<see cref="MainForm.GetCurrentPreviewRaceFormID"/> — NOT <see cref="_raceFormID"/>, which is the owning
    ''' ARMO's RNAM and can differ). Same rule in FO4 and Skyrim; nothing here is game-gated. No preview NPC ⇒
    ''' race 0 ⇒ True (nothing to filter against).</summary>
    Private Function ArmaFitsPreviewRace() As Boolean
        Return _mainForm.IsArmaRaceCompatible(GetFid(TextBoxRace), AdditionalRaceFids(),
                                              _mainForm.GetCurrentPreviewRaceFormID())
    End Function

    ''' <summary>Gate every preview control that composes the edited ARMA with the ACTOR on the race match.
    ''' "Full armor" and "Full Outfit" render the ARMA the way an actor would wear it, so when the edited ARMA's
    ''' race doesn't cover the preview NPC's race the engine (and our collector) drops it and both scopes render
    ''' an ARMA-less body — nothing to see, no reason to offer them. "Include Body" is gated for the opposite
    ''' reason: it would still draw a body, but the body comes from the preview NPC's SKIN (its own race), so the
    ''' user would be looking at this ARMA's mesh sitting on ANOTHER race's body — a composite that exists nowhere
    ''' in the game and reads as if the fit were valid. All three are disabled (Include Body also unchecked) and the
    ''' scope snaps back to "Only Model", which bypasses the race filter for this one ARMA
    ''' (<see cref="NpcRenderHost.RaceFilterBypassArmaFormID"/>) so the mesh being edited is always visible — alone.
    ''' Re-evaluated on every race edit (RNAM field + Additional Races list), so fixing the race re-enables them.</summary>
    Private Sub UpdatePreviewScopeGating()
        Dim fits = ArmaFitsPreviewRace()
        RadioFullArmor.Enabled = fits
        RadioFullOutfit.Enabled = fits
        CheckIncludeBody.Enabled = fits
        If Not fits AndAlso Not RadioOnlyModel.Checked Then RadioOnlyModel.Checked = True
        If Not fits AndAlso CheckIncludeBody.Checked Then CheckIncludeBody.Checked = False
        LabelPreviewHint.Text = If(_previewNpcFormID = 0UI,
                                   "Select an NPC in the main window to preview.",
                                   If(fits,
                                      "Preview: this ARMA equipped on the current NPC.",
                                      "Preview: model only — this ARMA's race doesn't cover the current NPC's race, " &
                                      "so the engine would not equip it (Full armor / Full Outfit / Include Body disabled)."))
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
                ' ⛔ La guarda que estaba acá miraba `draft`, y lo que podía ser nulo era su
                ' `.Record`: chequeaba la variable equivocada a dos líneas del peligro. `Nuevo` ya no
                ' puede devolver un borrador sin record — si el juego no declara MSWP, tira.
                draft = MswpDraft.Nuevo(_mainForm.AllocateDraftFormID(), Canon.CanonBridge.SessionGame())
                draft.Record.EditorID = MswpDraft.EditorIdPrefix & "new"
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
    ''' (mirrors EditBody's CreateBodySlideRows). One row per bone = [name | X | Y | Z |].</summary>
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
    ''' Z 100 | 30, each axis preceded by a tiny caption). Each slider is absolute (1.0 = unchanged) over
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

        ' Bone name is NOT user-editable — bones are added via the "Add bone" combo below and removed with.
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

    ''' <summary>Remove a single bone row (its per-row button).</summary>
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
            If Not CommitProtegido(validate:=False) Then Return

            Dim anyEstimated As Boolean = False
            Dim messages As New List(Of String)
            For g As UInteger = 0UI To 1UI
                Dim uaRaw = If(g = 0UI, _draft.Record.MaleModelFilename,
                                        _draft.Record.FemaleModelFilename)
                If String.IsNullOrWhiteSpace(uaRaw) Then Continue For   ' this gender has no mesh of its own → leave as-is

                Dim bodyPaths = ResolveNakedBodyMeshPaths(_draft.Record.Race, g)
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

                ' Overload DETALLADO: trae, POR EJE, si el número es una MEDICIÓN o un fallback identidad.
                ' Antes se usaba EstimateSclp (que devuelve el Single pelado) y los cuatro casos —escala
                ' genuina 1.0, fit rechazado, degenerado y NaN— entraban a la grilla indistinguibles, escritos
                ' como si fueran valores autorados. Los NÚMEROS del estimador están bien; lo que faltaba era
                ' poder decir "no pude medir". El caso peligroso es el MIXTO (un eje medido y el otro fallado):
                ' el bone se emite igual porque el eje bueno es dato real, pero el eje fallado se REPORTA.
                Dim estDet = SclpEstimator.EstimateSclpDetailed(uaBytes, bodyBytesList)
                If estDet IsNot Nothing AndAlso estDet.Count > 0 Then
                    _sculptByGender(g) = estDet.Select(Function(b) b.ToAbsolute()).ToList()
                    anyEstimated = True
                    ' Ejes NO medidos que quedan en 1.0 en la grilla: se avisan en vez de pasar por autorados.
                    Dim unmeasured = estDet.Where(Function(b) Not b.YMeasured OrElse Not b.ZMeasured).ToList()
                    If unmeasured.Count > 0 Then
                        Dim detail = String.Join(", ", unmeasured.Take(6).Select(
                            Function(b)
                                Dim axes As New List(Of String)
                                If Not b.YMeasured Then axes.Add($"Y({b.YStatus})")
                                If Not b.ZMeasured Then axes.Add($"Z({b.ZStatus})")
                                Return $"{b.Name}:{String.Join("/", axes)}"
                            End Function))
                        If unmeasured.Count > 6 Then detail &= $", +{unmeasured.Count - 6} more"
                        messages.Add($"{If(g = 0UI, "Male", "Female")}: {unmeasured.Count} bone(s) have an axis that could NOT be measured — those axes are left at 1.0 and are NOT measurements: {detail}")
                    End If
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
        ' EFECTIVA: esto NO edita, pregunta que mallas de cuerpo tiene el actor RENDERIZADO. Con 0
        ' mallas cae a la piel de la raza y el estimador de underarmor fusiona vertices contra OTRO
        ' cuerpo, asi que la respuesta tiene que ser la del motor.
        Dim armo = _mainForm.GetParsedArmoEfectivoParaRender(skinFid)
        If armo Is Nothing Then Return result

        Dim armaFids = Canon.CanonInterpretacion.LeerComplementos(armo).Select(Function(ent) ent.ArmaFormID).ToList()

        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each fid In armaFids
            Dim arma = _mainForm.GetParsedArmaForEditor(fid)
            If arma Is Nothing Then Continue For
            Dim mesh = If(requestedIsFemale, arma.FemaleModelFilename, arma.MaleModelFilename)
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
        ' `SkinDe` YA trae la guarda de raza nula: escribirla otra vez acá era la misma ley dos veces.
        Return Canon.CanonInterpretacion.SkinDe(Canon.CanonRecords.Race(raceRec, pm))
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

    ''' <summary>Unión de los biped objects que reservan para el Pip-Boy TODAS las razas que este ARMA
    ''' viste: la RNAM del panel (que es la fuente viva; el volcado al borrador va por un debounce) más
    ''' las razas adicionales. Se necesita la UNIÓN porque el tag está gobernado por la raza del ACTOR y
    ''' un mismo ARMA viste a varias: medido, 25 ARMA del load order sirven razas cuyo Pip-Boy NO es el
    ''' mismo slot (0x40000001 = slots 60 y 30, el caso de los feral ghouls).
    ''' <para><paramref name="algunaResuelta"/> sale False cuando no se pudo resolver NINGUNA raza. El
    ''' llamador entonces NO propone: devolver 0 en silencio sería tratar el tag del Pip-Boy como si fuera
    ''' una declaración de slot, que es exactamente el daño que este descuento existe para evitar.</para></summary>
    ''' <param name="algunaResuelta">Sale True si al menos una raza resolvió a un record RACE.</param>
    Private Function BitPipboyDeLasRazas(ByRef algunaResuelta As Boolean) As UInteger
        algunaResuelta = False
        Dim pm = _mainForm.PluginManagerForEditor
        If pm Is Nothing Then Return 0UI

        Dim fids As New List(Of UInteger)()
        Dim rnam = GetFid(TextBoxRace)
        If rnam = 0UI AndAlso _draft IsNot Nothing AndAlso _draft.Record IsNot Nothing Then rnam = _draft.Record.Race
        If rnam = 0UI Then rnam = _raceFormID
        If rnam <> 0UI Then fids.Add(rnam)
        Try
            For Each r In AdditionalRaceFids()
                If r <> 0UI AndAlso Not fids.Contains(r) Then fids.Add(r)
            Next
        Catch ex As Exception
            ' Sin razas adicionales legibles se sigue con la RNAM sola: el fail-closed del llamador
            ' se apoya en algunaResuelta, no en que esta lista sea completa.
            Logger.Log($"[ARMA-SLOTS] no se pudieron leer las razas adicionales: {ex.Message}")
        End Try

        Dim bits As UInteger = 0UI
        For Each rf In fids
            Dim rec = pm.GetRecord(rf)
            If rec Is Nothing OrElse rec.Header.Signature <> "RACE" Then Continue For
            Try
                bits = bits Or RaceUtil.RacePipboyMask(Canon.CanonRecords.Race(rec, pm))
                algunaResuelta = True
            Catch ex As Exception
                Logger.Log($"[ARMA-SLOTS] no se pudo leer el Pipboy Biped Object de la raza 0x{rf:X8}: {ex.Message}")
            End Try
        Next
        Return bits
    End Function

    ''' <summary>Qué decirle al usuario sobre la HERENCIA cuando este addon todavía no declara ningún slot.
    ''' Tres ramas, porque el caso más frecuente del editor —crear una ARMA dentro de un ARMO que también
    ''' es nuevo— deja a los DOS en cero, y ahí no hay ninguna herencia que anunciar.
    ''' <para>La máscara del padre se resuelve draft-aware: el ARMO padre típico acá ES un borrador y
    ''' PluginManager.GetRecord no ve borradores.</para></summary>
    Private Function AvisoDeHerencia() As String
        If ReadSlotChecks() <> 0UI Then Return ""

        Dim nombrePadre As String = ""
        Dim mascaraPadre As UInteger = 0UI
        If _parentArmoFormID <> 0UI Then
            Dim armo = _mainForm.GetParsedArmoForEditor(_parentArmoFormID)
            If armo IsNot Nothing Then
                mascaraPadre = armo.SlotMaskDe()
                nombrePadre = $"{If(armo.EditorID, "")} [0x{_parentArmoFormID:X8}]".Trim()
            End If
        End If

        If mascaraPadre <> 0UI Then
            ' ⛔ Y la advertencia que falta acá: la herencia es del PLUGIN, no de la vista previa. Con
            ' el alcance «sólo este addon» el editor arma un ARMO descartable SIN slots, así que un
            ' addon con BOD2 vacío no es dueño de nada y la vista previa lo esconde igual. Sin esta
            ' línea el cartel dice «está todo bien» al lado de una vista previa vacía, que es
            ' exactamente el bucle del reporte original.
            Return "This addon declares no slot of its own, so in the plugin it INHERITS the armor's" &
                   If(nombrePadre = "", "", $" ({nombrePadre})") & ": " & TextoDeSlots(mascaraPadre) & "." &
                   Environment.NewLine &
                   "Ticking anything here makes it declare its own slots and STOP inheriting, so it will no " &
                   "longer occupy the rest." &
                   If(Not VistaPreviaUsaElEnvoltorio(), "",
                      Environment.NewLine &
                      "Note: with the preview scope you have selected, that inheritance does NOT apply. " &
                      "Previewing just this addon wraps it in a throwaway armor with no slots, so with an " &
                      "empty BOD2 nothing owns a slot for it and the preview hides its geometry anyway. " &
                      "That is a known limitation of the preview, not a problem with this addon: switching " &
                      "the preview to Full armor shows it inherited.")
        End If

        Dim quien As String
        If _parentArmoFormID = 0UI Then
            quien = "This addon declares no biped slot, and it was opened on its own, so there is no armor " &
                    "to inherit one from."
        ElseIf nombrePadre = "" Then
            quien = "This addon declares no biped slot, and the armor it belongs to could not be resolved, " &
                    "so it is not known whether there is anything to inherit."
        Else
            quien = "Neither this addon nor " & nombrePadre & " declares any biped slot."
        End If
        Return quien &
               " With no owner, the engine treats every slot as covered and the preview hides the geometry " &
               "of those slots - ticking a slot here is what makes it show."
    End Function

    ''' <summary>"37, 38" a partir de una máscara de biped slots (bit i = slot 30+i).</summary>
    ''' <param name="mask">La máscara.</param>
    Private Shared Function TextoDeSlots(mask As UInteger) As String
        Dim l As New List(Of String)()
        For i = 0 To 31
            If (mask And (1UI << i)) <> 0UI Then l.Add((30 + i).ToString(CultureInfo.InvariantCulture))
        Next
        Return String.Join(", ", l)
    End Function

    ''' <summary>«Slots declared by the models…»: lee los DOS modelos con el lector del juego y ofrece,
    ''' uno por uno, los biped slots que declaran y el BOD2 todavía no tiene. NUNCA destilda.</summary>
    ''' <param name="sender">El botón.</param>
    ''' <param name="e">Sin uso.</param>
    Private Sub OnSlotsFromModel(sender As Object, e As EventArgs)
        Const TITULO As String = "Slots declared by the models"
        ' ⛔ CONVERSION EXPLICITA: SessionGame() devuelve WbGame y LecturaDelJuego toma
        ' Config_App.Game_Enum. Son DOS enums distintos y con Option Strict apagado la conversion seria
        ' muda: hoy anda solo porque los dos numeran igual, y el dia que a WbGame se le agregue un
        ' miembro adelante el boton leeria con el lector del otro juego sin un solo error.
        Dim esSkyrim As Boolean = (Canon.CanonBridge.SessionGame() = Canon.WbGame.Skyrim)
        Dim juego As Config_App.Game_Enum = If(esSkyrim, Config_App.Game_Enum.Skyrim, Config_App.Game_Enum.Fallout4)
        Dim modo = SlotsDeLaMalla.LecturaDelJuego(juego)

        ' El tag del Pip-Boy sale de la RAZA. Sin raza resuelta no se propone: fail-closed.
        Dim bitPipboy As UInteger = 0UI
        If juego <> Config_App.Game_Enum.Skyrim Then
            Dim resuelta As Boolean = False
            bitPipboy = BitPipboyDeLasRazas(resuelta)
            If Not resuelta Then
                MessageBox.Show(Me,
                    "The Pip-Boy biped object comes from the RACE, and this addon's race could not be " &
                    "resolved, so the proposal cannot tell a real slot from the Pip-Boy tag. Set the race first.",
                    TITULO, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
        End If

        ' ⛔ Los paths salen del PANEL y no del borrador: el usuario puede acabar de tipear uno, y el
        ' volcado al borrador va por el debounce. MOD4/MOD5 quedan afuera A PROPÓSITO: son los modelos de
        ' 1ª persona, que un NPC no viste.
        Dim male = SlotsDelModelo.Leer(TextBoxMod2.Text, modo)
        Dim female = SlotsDelModelo.Leer(TextBoxMod3.Text, modo)
        Dim p = SlotsDelModelo.Proponer(male, female, ReadSlotChecks(), bitPipboy)

        If Not p.HuboLectura Then
            Dim ningunModelo = male.Estado = SlotsDelModelo.EstadoDeLectura.SinPath AndAlso
                               female.Estado = SlotsDelModelo.EstadoDeLectura.SinPath
            MessageBox.Show(Me,
                SlotsDelModeloDialog.TextoDeLecturas(p) & Environment.NewLine & Environment.NewLine &
                If(ningunModelo,
                   "This addon has no models yet: set the male mesh (MOD2) and/or the female mesh (MOD3) " &
                   "first, and then this button can tell you which biped slots they declare.",
                   "No model could be read. Check the mesh paths above - the file must exist as a loose file " &
                   "or inside a game archive."),
                TITULO, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        If p.TodoFueraDeBanda Then
            ' ⛔ La consecuencia de caer fuera de [30,61] es OPUESTA en los dos motores, y no se re-enuncia
            ' acá: se le pregunta a la sede de cada juego.
            Dim ocultan = SlotsDelModelo.FueraDeBandaSeOculta(modo)
            MessageBox.Show(Me,
                SlotsDelModeloDialog.TextoDeLecturas(p) & Environment.NewLine & Environment.NewLine &
                If(ocultan,
                   "The models that could be read DO declare partitions, but every one of them folds outside " &
                   "the biped range " &
                   "30-61 (for example BodyPart = 0). This game hides those, and no BOD2 slot changes it - " &
                   "the mesh has to be re-partitioned.",
                   "The models that could be read DO declare segment tags, but none of them is a biped object " &
                   "in the range " &
                   "30-61. This game leaves those segments visible, so they are not the reason anything is " &
                   "hidden, and no BOD2 slot relates to them."),
                TITULO, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        If p.Declarado = 0UI Then
            MessageBox.Show(Me,
                SlotsDelModeloDialog.TextoDeLecturas(p) & Environment.NewLine & Environment.NewLine &
                If(modo = SlotsDeLaMalla.ModoDeLectura.ParticionesDismember,
                   "The models that could be read declare no biped slot (no BSDismemberSkinInstance partitions). " &
                   "Skyrim only " &
                   "hides geometry that HAS partitions, so this mesh is not being hidden for lack of a slot.",
                   "The models that could be read declare no biped object in their segments. That is the usual " &
                   "case in " &
                   "Fallout 4, where segment tags are mostly material and damage sub-indices; and the " &
                   "Pip-Boy tag, when present, has its own rule and is not a slot declaration."),
                TITULO, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        If p.Faltan = 0UI Then
            MessageBox.Show(Me,
                SlotsDelModeloDialog.TextoDeLecturas(p) & Environment.NewLine & Environment.NewLine &
                "Everything the models that could be read declare is already checked: " & TextoDeSlots(p.Declarado) & "." &
                If(p.Sobran <> 0UI,
                   Environment.NewLine & Environment.NewLine &
                   "Slots you declare without geometry (they evict, they do not draw): " & TextoDeSlots(p.Sobran) &
                   ". This button never removes those.",
                   If(male.Estado <> SlotsDelModelo.EstadoDeLectura.Ilegible AndAlso
                      female.Estado <> SlotsDelModelo.EstadoDeLectura.Ilegible, "",
                      Environment.NewLine & Environment.NewLine &
                      "Slots declared without geometry are NOT listed: one of the models could not be read, " &
                      "so which slots have geometry is unknown.")),
                TITULO, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Using dlg As New SlotsDelModeloDialog(p, AvisoDeHerencia())
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim elegidos = dlg.SlotsElegidos
            If elegidos = 0UI Then Return
            SetSlotChecks(SlotsDelModelo.MascaraAplicada(ReadSlotChecks(), elegidos))
            OnFieldEdited(Me, EventArgs.Empty)
        End Using
    End Sub

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

    ''' <summary>La vista previa arma el OTFT de contexto real (no un envoltorio) cuando el alcance es
    ''' Full Outfit y hay un atuendo de contexto enhebrado.</summary>
    Private Function VistaPreviaConOtftDeContexto() As Boolean
        Return RadioFullOutfit.Checked AndAlso _outfitContextFormID <> 0UI
    End Function

    ''' <summary>La vista previa monta el ARMO PADRE real (no un envoltorio) cuando el alcance es Full
    ''' armor y hay un ARMO padre enhebrado.</summary>
    Private Function VistaPreviaConArmoPadre() As Boolean
        Return RadioFullArmor.Checked AndAlso _parentArmoFormID <> 0UI
    End Function

    ''' <summary>⛔ Sede ÚNICA de «¿esta vista previa pasa por el ARMO descartable?». Importa porque ese
    ''' envoltorio nace SIN slots, así que un addon con BOD2 vacío no hereda nada y la fase 1 le esconde
    ''' la geometría — y el aviso del botón de slots tiene que decir eso SÓLO cuando es cierto. En los
    ''' otros dos alcances se monta el record real y la herencia sí aplica: afirmarlo siempre sería
    ''' decirle al usuario que no le crea a una vista previa que en ese momento está bien.</summary>
    Private Function VistaPreviaUsaElEnvoltorio() As Boolean
        Return Not VistaPreviaConOtftDeContexto() AndAlso Not VistaPreviaConArmoPadre()
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
        ' ⛔ Si el volcado ya falló, no se rearma: este método hace Stop+Start y lo llama CADA edición
        ' de campo, así que el Stop() del manejador de error duraba hasta la próxima tecla — y como el
        ' aviso sale una sola vez, a partir de ahí el commit fallaba en silencio en cada tecla.
        If _previewCommitFallado Then Return
        If _host Is Nothing OrElse _previewDebounce Is Nothing Then Return
        _previewDebounce.Stop()
        _previewDebounce.Start()
    End Sub

    ''' <summary>Push the preview-mode controls (scope radios / Include Body / Show other gender) onto the
    ''' render host BEFORE each render. Called at the top of <see cref="RenderPreviewAsync"/> so the knobs
    ''' and the preview cache key are always in sync with the UI.
    '''
    ''' Scope → <c>OnlyOutfitCollect</c>: Only Model / Full armor collect just the edited piece; Full Outfit
    ''' collects the full actor. Include Body forces the body into collection (OnlyOutfitCollect skips Skin),
    ''' so <c>OnlyOutfitCollect = pieceScope AndAlso NOT IncludeBody</c>. RenderBody visibility mirrors
    ''' Include Body. Show other gender maps to a target-gender DEFAULT actor via <c>PreviewGenderOverride</c>
    ''' (opposite of the preview NPC's own gender).</summary>
    Private Sub ApplyPreviewControlsToHost()
        If _host Is Nothing Then Return
        ' "Full armor" renders the PARENT ARMO (its whole set) when one is threaded in (_parentArmoFormID<>0,
        ' from the ARMO editor's Addons tab); with no parent in context (standalone open) it falls back to
        ' "Only Model" (the ARMA alone). Either way it is PIECE-SCOPE: OnlyOutfitCollect collects just that
        ' ARMO/ARMA, not the whole outfit. The parent/outfit substitution happens in RenderPreviewAsync (which
        ' picks the OTFT); here we only set the collection scope. Both piece-scope radios ⇒ collect the piece.
        Dim pieceScope As Boolean = RadioOnlyModel.Checked OrElse RadioFullArmor.Checked
        Dim includeBody As Boolean = CheckIncludeBody.Checked
        _host.OnlyOutfitCollect = pieceScope AndAlso Not includeBody
        _host.Toggles.RenderBody = includeBody
        _host.PreviewGenderOverride = If(CheckShowOtherGender.Checked, CType(Not _isFemale, Boolean?), Nothing)
        ' "Only Model" = show me THIS mesh: collect the edited ARMA even when its race doesn't cover the preview
        ' actor's (otherwise an ARMA authored for another race renders nothing at all and there is no way to see
        ' it). The engine-faithful scopes never bypass — and they're disabled outright when the race doesn't
        ' match (UpdatePreviewScopeGating), so this is the only scope reachable in that state.
        _host.RaceFilterBypassArmaFormID = If(RadioOnlyModel.Checked, _draft.FormID, 0UI)
    End Sub

    ' Scope radios + "Show other gender" change COLLECTION / gender resolution ⇒ full (debounced) re-render.
    Private Sub OnPreviewModeChanged(sender As Object, e As EventArgs) _
        Handles RadioOnlyModel.CheckedChanged, RadioFullArmor.CheckedChanged, RadioFullOutfit.CheckedChanged,
                CheckShowOtherGender.CheckedChanged
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
    ' WYSIWYG preview — wrap the ARMA draft in a throwaway ARMO → throwaway OTFT
    ' =====================================================================

    ''' <summary>Cache key: the effective preview OTFT + its single throwaway item + the threaded scope
    ''' context (parent ARMO / outfit context), so a scope-mode switch that changes WHAT gets rendered
    ''' re-renders. <paramref name="throwawayItemFid"/> is 0 in the Full-Outfit-with-context path.</summary>
    Private Function BuildPreviewKey(previewOtftFid As UInteger, throwawayItemFid As UInteger) As String
        Dim rec = _draft.Record
        Dim fo4 = TryCast(rec, Canon.ArmaFO4)
        Dim maleSwap = If(fo4 IsNot Nothing, fo4.MaleMaterialSwap, 0UI)
        Dim femaleSwap = If(fo4 IsNot Nothing, fo4.FemaleMaterialSwap, 0UI)
        Dim maleFlags = If(fo4 IsNot Nothing, fo4.MaleFlags, CByte(0))
        Dim femaleFlags = If(fo4 IsNot Nothing, fo4.FemaleFlags, CByte(0))
        Dim noUnderarmorScaling = (fo4 IsNot Nothing AndAlso fo4.NoUnderarmorScaling)
        Return String.Join(":", {
            previewOtftFid.ToString("X8"), throwawayItemFid.ToString("X8"),
            _parentArmoFormID.ToString("X8"), _outfitContextFormID.ToString("X8"),
            _draft.FormID.ToString("X8"), rec.SlotMaskDe().ToString("X8"),
            rec.MaleModelFilename, rec.FemaleModelFilename,
            rec.Race.ToString("X8"),
            maleSwap.ToString("X8"), femaleSwap.ToString("X8"),
            _mainForm.GetMswpDraftSignature(maleSwap), _mainForm.GetMswpDraftSignature(femaleSwap),
            rec.MaleSkinTexture.ToString("X8"), rec.FemaleSkinTexture.ToString("X8"),
            rec.MaleSkinTextureSwapList.ToString("X8"),
            rec.FemaleSkinTextureSwapList.ToString("X8"),
            rec.DataMalePriority.ToString(CultureInfo.InvariantCulture),
            rec.DataFemalePriority.ToString(CultureInfo.InvariantCulture),
            maleFlags.ToString(CultureInfo.InvariantCulture),
            femaleFlags.ToString(CultureInfo.InvariantCulture),
            If(noUnderarmorScaling, "1", "0"),
            If(_host IsNot Nothing AndAlso _host.OnlyOutfitCollect, "oc1", "oc0"),
            If(_host IsNot Nothing, "rb" & _host.RaceFilterBypassArmaFormID.ToString("X8"), "rb-"),
            If(_host IsNot Nothing AndAlso _host.PreviewGenderOverride.HasValue, "g" & _host.PreviewGenderOverride.Value.ToString(), "g-")})
    End Function

    ''' <summary>Vuelca los paneles al borrador PARA EL PREVIEW, sin que un fallo se lleve el proceso.
    ''' <para>⛔ El volcado termina en <c>ContentEquals</c> → <c>WbWriter.EmitBody</c>, que TIRA en dos
    ''' casos legítimos: un árbol cuya Form Version no coincide con la del contexto, y un subrecord que
    ''' el esquema no supo ubicar. Este camino lo dispara el temporizador del preview en CADA redibujo,
    ''' desde un <c>Async Sub</c> sin <c>Try</c> y con <c>UnhandledExceptionMode.ThrowException</c>: sin
    ''' esta guarda, un árbol inemitible CIERRA la aplicación en mitad de un dibujo, sin diálogo y sin
    ''' nada accionable.</para>
    ''' <para>Y tampoco se puede callar —hay un <c>Catch</c> mudo más abajo para el render, que es otra
    ''' cosa—: un preview que no volcó está mostrando algo distinto de lo que el usuario está editando.
    ''' Se avisa una vez y se para el temporizador; lo que el usuario tenga sin guardar no se toca.</para>
    ''' <para>⛔ Y REVIERTE. El volcado escribe ~25 campos en el árbol antes de llegar al punto que
    ''' puede tirar, y el árbol del borrador es el MISMO objeto que ya está registrado en MainForm: sin
    ''' revertir, un fallo a mitad de camino deja un borrador medio escrito, marcado como sucio, y el
    ''' guardado lo emite así. Reportar un estado que ya quedó pegado no es reportar.</para>
    ''' <para>Lo usa también OnOk (con validate:=True): el diálogo del fallo invita a apretar Aceptar, y
    ''' si Aceptar corriera el mismo volcado sin protección el proceso moriría ahí — que es justo lo que
    ''' esta guarda vino a evitar.</para>
    ''' <para>Devuelve False si no se pudo volcar: el llamador no sigue con datos a medias.</para></summary>
    Private Function CommitProtegido(validate As Boolean) As Boolean
        ' ⛔ DENTRO del Try. `Clone()` ganó una precondición que puede tirar, y acá arriba no la
        ' atrapa nadie: el Catch que revierte el borrador empieza una línea más abajo, y la app corre
        ' con `UnhandledExceptionMode.ThrowException`, o sea que tirar en esta línea CIERRA la app y
        ' deja el borrador registrado a medias — lo contrario de lo que este Try existe para hacer.
        Dim antes As ArmaDraft = Nothing
        Try
            antes = _draft?.Clone()
            Return CommitPanelsToDraft(validate)
        Catch ex As Exception
            ' Primero deshacer, después avisar: si el aviso saliera antes, cualquier cosa que el usuario
            ' haga desde el diálogo ya vería el árbol a medio escribir.
            If antes IsNot Nothing AndAlso _draft IsNot Nothing Then
                _draft.Record = antes.Record
                _draft.IsModified = antes.IsModified
                _mainForm.RegisterArmaDraft(_draft)
            End If
            Logger.Log("ArmaEditor.CommitProtegido: " & ex.ToString())
            If _previewDebounce IsNot Nothing Then _previewDebounce.Stop()
            If Not _previewCommitFallado Then
                _previewCommitFallado = True
                MessageBox.Show(Me,
                    "Could not build this ARMA:" & vbCrLf & vbCrLf &
                    ex.Message & vbCrLf & vbCrLf &
                    "The preview is stopped and the last change was rolled back. The details went to " &
                    "the log. You can keep editing, but this change cannot be saved.",
                    "Preview", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End If
            Return False
        End Try
    End Function

    Private Async Function RenderPreviewAsync() As Task
        If _host Is Nothing OrElse _preview Is Nothing OrElse _preview.IsDisposed Then Return
        If _previewNpcFormID = 0UI Then Return

        ' Flush the current panel state into _draft AND register it (RegisterArmaDraft) BEFORE the wrapper +
        ' preview key are built. The very first render (from _Shown / template-load) reaches here with
        ' _pendingApply=False, so without this the draft would never be registered and the draft-aware
        ' resolver (TryGetArmaDraft) returns Nothing → naked. validate:=False never early-returns, so the
        ' draft is always registered; the key is computed from _draft AFTER this so it stays correct.
        If Not CommitProtegido(validate:=False) Then Return

        ' Push the preview-mode controls onto the host BEFORE the key is built so a scope / Include Body /
        ' gender change is reflected in both the render knobs and the cache key (else the key match early-returns).
        ApplyPreviewControlsToHost()

        ' Resolve which OTFT the preview renders as the actor's outfit, from the scope mode + threaded context:
        '  • Full Outfit + outfit context ⇒ render the whole outfit being assembled in the OutfitPicker; the
        '    edited ARMA rides its parent ARMO (a piece of that outfit) and the draft-aware resolver swaps in the
        '    edited version. No throwaway needed.
        '  • Full armor + parent ARMO ⇒ throwaway single-item OTFT holding the PARENT ARMO (renders the whole
        '    ARMO with this ARMA applied via the draft-aware resolver).
        '  • Only Model, or Full armor / Full Outfit with NO context ⇒ throwaway single-item OTFT holding the
        '    synthetic ARMA wrapper (this ARMA alone) — the existing fallback.
        Dim fullOutfitWithContext As Boolean = VistaPreviaConOtftDeContexto()
        Dim fullArmorWithParent As Boolean = VistaPreviaConArmoPadre()
        Dim throwawayItemFid As UInteger = 0UI
        Dim previewOtftFid As UInteger
        If fullOutfitWithContext Then
            previewOtftFid = _outfitContextFormID
        Else
            throwawayItemFid = If(fullArmorWithParent, _parentArmoFormID, EnsureArmaPreviewWrapper(_draft.FormID))
            previewOtftFid = OutfitDraft.PreviewDraftFormID
        End If

        Dim key As String = BuildPreviewKey(previewOtftFid, throwawayItemFid)
        If key = _lastPreviewKey Then Return
        If _previewInProgress Then Return
        _previewInProgress = True
        Try
            If Not fullOutfitWithContext Then
                Dim otft = OutfitDraft.Nuevo(OutfitDraft.PreviewDraftFormID,
                                             Canon.CanonBridge.SessionGame())
                otft.Record.EditorID = OutfitDraft.EditorIdPrefix & "(armapreview)"
                otft.ReemplazarPrendas({throwawayItemFid})
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

    ''' <summary>(Re)register a throwaway ARMO draft (single addon INDX 0) that references the ARMA draft, so
    ''' the OTFT preview can render it. Returns the wrapper ARMO FormID.</summary>
    Private Function EnsureArmaPreviewWrapper(armaFid As UInteger) As UInteger
        Dim wrapper = ArmoDraft.Nuevo(PreviewArmoWrapperFormID, Canon.CanonBridge.SessionGame())
        wrapper.Record.EditorID = ArmoDraft.EditorIdPrefix & "(armapreview)"
        wrapper.Record.Race = _draft.Record.Race
        ' El addon único, INDX 0 → esta ARMA. FO4 declara índice + referencia por separado; Skyrim,
        ' un array de referencias sin índice explícito. No hay un campo compartido para
        ' "agregar un addon".
        Dim fo4W = TryCast(wrapper.Record, Canon.ArmoFO4)
        Dim sseW = TryCast(wrapper.Record, Canon.ArmoSSE)
        If fo4W IsNot Nothing Then
            Dim m = fo4W.AgregarModels()
            If m IsNot Nothing Then
                m.ModelAddonIndex = 0US
                m.ModelArmorAddon = armaFid
            End If
        ElseIf sseW IsNot Nothing Then
            Dim m = sseW.AgregarArmature()
            If m IsNot Nothing Then m.ModelFilename = armaFid
        End If
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
            ' Camera GPU/CPU toggle debe re-aplicar el tint de ESTE preview (no sólo la geometría). Ver MainForm.
            _mainForm?.HookSkinningToggleRefresh(_preview, _host)
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
