Imports System.Drawing
Imports System.Linq
Imports FO4_Base_Library

''' <summary>Standalone editor to CREATE / EDIT / OVERRIDE armor records (ARMO + its ARMA addons + MSWP
''' material swaps). ARMO-centric: an ARMO is the equippable unit; ARMAs are its addons; an MSWP is a
''' referenced sub-resource. The form does NOT write the ESP — it only mutates the MainForm draft lists
''' (<c>_armoDrafts</c>/<c>_armaDrafts</c>/<c>_mswpDrafts</c> via the Register* accessors). The existing
''' "Save ESP" flow persists the transitive closure of dirty drafts. Once a draft exists the draft-aware
''' render resolver (NpcRenderContext.GetParsedArmo/Arma → MainForm.BuildArmo/ArmaDataFromDraft) shows it.
'''
''' Layout mirrors <see cref="OutfitPicker_Form"/>: a SplitContainer with the editor on the left and a
''' dedicated preview panel hosting an <see cref="NpcRenderHost"/> on the right. The WYSIWYG preview wraps
''' the selected ARMO draft in a THROWAWAY OTFT (reusing <see cref="OutfitDraft.PreviewDraftFormID"/>,
''' register/unregister exactly like OutfitPicker's Create tab) and renders it equipped on the currently-
''' selected NPC via <see cref="MainForm.PreviewOutfitInHostAsync"/>. A standalone ARMA draft is auto-
''' wrapped in a throwaway ARMO draft (single addon INDX 0) before that.</summary>
Public Class ArmorEditor_Form

    Private Enum DraftKind
        None
        Armo
        Arma
        Mswp
    End Enum

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _previewNpcFormID As UInteger
    Private ReadOnly _raceFormID As UInteger
    Private ReadOnly _isFemale As Boolean

    ''' <summary>The biped slots shown as granular per-bit checkboxes (FO4 BOD2 bit = slot-30). Per the spec:
    ''' the commonly-used slots only (30-45, 48-51) — kept usable rather than all 32. Bit index = slot-30.</summary>
    Private Shared ReadOnly EditableSlots As Integer() = {30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 48, 49, 50, 51}

    ' Slot checkboxes added in code-behind (per Designer rule: variable/many repeated controls go in the
    ' container after InitializeComponent). Keyed by slot number.
    Private ReadOnly _armoSlotChecks As New Dictionary(Of Integer, CheckBox)
    Private ReadOnly _armaSlotChecks As New Dictionary(Of Integer, CheckBox)

    ' === current selection / panel state ===
    Private _currentKind As DraftKind = DraftKind.None
    Private _currentArmo As ArmoDraft
    Private _currentArma As ArmaDraft
    Private _currentMswp As MswpDraft
    ''' <summary>Suppresses preview re-render + dirty-marking while a panel is being LOADED from a draft (so
    ''' programmatic SetText/Checked don't count as user edits).</summary>
    Private _loading As Boolean

    ' === preview (mirror OutfitPicker) ===
    Private _preview As PreviewControl
    Private _host As NpcRenderHost
    Private _previewDraftRegistered As Boolean
    Private _previewArmaWrapperRegistered As Boolean
    Private _lastPreviewKey As String = Nothing
    Private _previewInProgress As Boolean
    ''' <summary>Debounce timer: field edits restart it; on tick we re-render (so we re-preview on a short
    ''' delay, not on every keystroke). Created in code-behind.</summary>
    Private WithEvents _previewDebounce As Timer

    ''' <summary>Throwaway ARMO wrapper FormID for previewing a STANDALONE ARMA draft — a draft sentinel just
    ''' below the OTFT preview sentinel so the resolver picks it up but it's never persisted.</summary>
    Private Const PreviewArmoWrapperFormID As UInteger = &HFF0007FEUI

    ''' <summary>Small combo item: a (FormID, DisplayName) pair whose ToString is the display name. FormID 0
    ''' = the "(none)" entry.</summary>
    Private Class ComboItem
        Public FormID As UInteger
        Public Display As String
        Public Overrides Function ToString() As String
            Return Display
        End Function
    End Class

    ''' <param name="mainForm">Owner — supplies draft registrars, parsed-record access and the WYSIWYG preview.</param>
    ''' <param name="previewNpcFormID">The currently-selected NPC for preview context. 0 = no preview (hint shown).</param>
    ''' <param name="raceFormID">The preview NPC's race (used to pre-fill a new ARMO/ARMA's Race).</param>
    ''' <param name="isFemale">The preview NPC's gender (drives which mesh/priority is previewed).</param>
    Public Sub New(mainForm As MainForm, previewNpcFormID As UInteger, raceFormID As UInteger, isFemale As Boolean)
        InitializeComponent()
        _mainForm = mainForm
        _previewNpcFormID = previewNpcFormID
        _raceFormID = raceFormID
        _isFemale = isFemale

        BuildSlotCheckBoxes()
        BuildMswpGridColumns()
        PopulateCombos()

        _previewDebounce = New Timer() With {.Interval = 450}

        ' Draft list + draft-management buttons.
        AddHandler ListViewDrafts.SelectedIndexChanged, AddressOf OnDraftSelectionChanged
        AddHandler ButtonNewArmo.Click, AddressOf OnNewArmo
        AddHandler ButtonNewArma.Click, AddressOf OnNewArma
        AddHandler ButtonNewMswp.Click, AddressOf OnNewMswp
        AddHandler ButtonOverrideExisting.Click, AddressOf OnOverrideExisting
        AddHandler ButtonDeleteDraft.Click, AddressOf OnDeleteDraft
        AddHandler ButtonApply.Click, AddressOf OnApply

        ' ARMO panel events.
        AddHandler ButtonArmoAddKeyword.Click, AddressOf OnArmoAddKeyword
        AddHandler ButtonArmoRemoveKeyword.Click, AddressOf OnArmoRemoveKeyword
        AddHandler ButtonArmoAddAddonExisting.Click, AddressOf OnArmoAddAddonExisting
        AddHandler ButtonArmoAddAddonDraft.Click, AddressOf OnArmoAddAddonDraft
        AddHandler ButtonArmoRemoveAddon.Click, AddressOf OnArmoRemoveAddon
        AddHandler ButtonArmoAddonUp.Click, AddressOf OnArmoAddonUp
        AddHandler ButtonArmoAddonDown.Click, AddressOf OnArmoAddonDown
        AddHandler ButtonArmoNewMswp.Click, AddressOf OnArmoNewMswp

        ' ARMA panel events.
        AddHandler ButtonArmaAddRace.Click, AddressOf OnArmaAddRace
        AddHandler ButtonArmaRemoveRace.Click, AddressOf OnArmaRemoveRace
        AddHandler ButtonArmaBrowseMeshMale.Click, AddressOf OnArmaBrowseMeshMale
        AddHandler ButtonArmaBrowseMeshFemale.Click, AddressOf OnArmaBrowseMeshFemale
        AddHandler ButtonArmaBrowseMeshMaleFp.Click, AddressOf OnArmaBrowseMeshMaleFp
        AddHandler ButtonArmaBrowseMeshFemaleFp.Click, AddressOf OnArmaBrowseMeshFemaleFp
        AddHandler ButtonArmaNewMswp.Click, AddressOf OnArmaNewMswp

        ' MSWP panel events.
        AddHandler ButtonMswpAddRow.Click, AddressOf OnMswpAddRow
        AddHandler ButtonMswpRemoveRow.Click, AddressOf OnMswpRemoveRow
        AddHandler ButtonMswpBrowseOriginal.Click, AddressOf OnMswpBrowseOriginal
        AddHandler ButtonMswpBrowseReplacement.Click, AddressOf OnMswpBrowseReplacement

        ' Re-preview (debounced) on edits to the fields that change the render. ARMO: name/race/world-mswp.
        AddHandler TextBoxArmoFull.TextChanged, AddressOf OnFieldEdited
        AddHandler ComboArmoRace.SelectedIndexChanged, AddressOf OnFieldEdited
        AddHandler ComboArmoMswp.SelectedIndexChanged, AddressOf OnFieldEdited
        ' ARMA: mesh paths + race + material swaps drive the render — debounce-preview on these too.
        AddHandler TextBoxArmaMeshMale.TextChanged, AddressOf OnFieldEdited
        AddHandler TextBoxArmaMeshFemale.TextChanged, AddressOf OnFieldEdited
        AddHandler ComboArmaRace.SelectedIndexChanged, AddressOf OnFieldEdited
        AddHandler ComboArmaMswpMale.SelectedIndexChanged, AddressOf OnFieldEdited
        AddHandler ComboArmaMswpFemale.SelectedIndexChanged, AddressOf OnFieldEdited
        AddHandler ComboArmaTxstMale.SelectedIndexChanged, AddressOf OnFieldEdited
        AddHandler ComboArmaTxstFemale.SelectedIndexChanged, AddressOf OnFieldEdited

        RefreshDraftList()
        ShowPanelFor(DraftKind.None)

        If _previewNpcFormID = 0UI Then
            LabelPreviewHint.Text = "Select an NPC to preview."
        Else
            LabelPreviewHint.Text = "Preview: the selected ARMO/ARMA equipped on the current NPC."
        End If
    End Sub

    ' =====================================================================
    ' One-time UI construction done in code-behind (Designer rule: variable/
    ' many repeated controls + the grid columns are added to their containers
    ' here, after InitializeComponent).
    ' =====================================================================

    Private Sub BuildSlotCheckBoxes()
        For Each slot In EditableSlots
            Dim cbArmo As New CheckBox With {.AutoSize = True, .Text = SlotName(slot), .Margin = New Padding(2),
                                             .Tag = slot, .UseVisualStyleBackColor = True}
            AddHandler cbArmo.CheckedChanged, AddressOf OnFieldEdited
            FlowArmoSlots.Controls.Add(cbArmo)
            _armoSlotChecks(slot) = cbArmo

            Dim cbArma As New CheckBox With {.AutoSize = True, .Text = SlotName(slot), .Margin = New Padding(2),
                                             .Tag = slot, .UseVisualStyleBackColor = True}
            AddHandler cbArma.CheckedChanged, AddressOf OnFieldEdited
            FlowArmaSlots.Controls.Add(cbArma)
            _armaSlotChecks(slot) = cbArma
        Next
    End Sub

    Private Sub BuildMswpGridColumns()
        GridMswp.Columns.Clear()
        Dim colOrig As New DataGridViewTextBoxColumn With {.HeaderText = "Original Material (BNAM)", .FillWeight = 40, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill}
        Dim colRepl As New DataGridViewTextBoxColumn With {.HeaderText = "Replacement Material (SNAM)", .FillWeight = 40, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill}
        Dim colRemap As New DataGridViewTextBoxColumn With {.HeaderText = "Color Remap", .FillWeight = 20, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill}
        GridMswp.Columns.Add(colOrig)
        GridMswp.Columns.Add(colRepl)
        GridMswp.Columns.Add(colRemap)
    End Sub

    ''' <summary>Fill the race / keyword / TXST / MSWP / template combos from the load order (+ drafts where
    ''' relevant). The MSWP combos additionally carry the current MSWP drafts so a just-created swap is pickable.</summary>
    Private Sub PopulateCombos()
        Dim races = _mainForm.GetRaceCandidatesForEditor()
        FillCombo(ComboArmoRace, races, includeNone:=True)
        FillCombo(ComboArmaRace, races, includeNone:=True)
        FillCombo(ComboArmaAddRace, races, includeNone:=False)

        FillCombo(ComboArmoKeyword, _mainForm.GetKeywordCandidatesForEditor(), includeNone:=False)

        Dim txst = _mainForm.GetTxstCandidatesForEditor()
        FillCombo(ComboArmaTxstMale, txst, includeNone:=True)
        FillCombo(ComboArmaTxstFemale, txst, includeNone:=True)

        RefreshMswpCombos()

        ' Template ARMO picker (optional). Reuse the ARMO record list.
        Dim armoRecs = _mainForm.GetArmoRecordsForEditor().
            Select(Function(a) (a.FormID, a.DisplayName)).ToList()
        FillCombo(ComboArmoTnam, armoRecs, includeNone:=True)
    End Sub

    ''' <summary>(Re)fill the three MSWP combos = real records + current MSWP drafts, each with a "(none)" head.</summary>
    Private Sub RefreshMswpCombos()
        Dim mswp = _mainForm.GetMswpCandidatesForEditor()
        For Each d In _mainForm.MswpDrafts()
            mswp.Add((d.FormID, d.EditorID & "  (new)"))
        Next
        FillCombo(ComboArmoMswp, mswp, includeNone:=True)
        FillCombo(ComboArmaMswpMale, mswp, includeNone:=True)
        FillCombo(ComboArmaMswpFemale, mswp, includeNone:=True)
    End Sub

    Private Shared Sub FillCombo(combo As ComboBox, items As List(Of (FormID As UInteger, DisplayName As String)), includeNone As Boolean)
        combo.BeginUpdate()
        Try
            combo.Items.Clear()
            If includeNone Then combo.Items.Add(New ComboItem With {.FormID = 0UI, .Display = "(none)"})
            For Each it In items
                combo.Items.Add(New ComboItem With {.FormID = it.FormID, .Display = it.DisplayName})
            Next
            If combo.Items.Count > 0 Then combo.SelectedIndex = 0
        Finally
            combo.EndUpdate()
        End Try
    End Sub

    ''' <summary>Select the combo entry whose FormID matches <paramref name="fid"/> (0 = the "(none)" head).
    ''' If the FormID isn't present (e.g. a fresh draft), it's appended so the value isn't lost.</summary>
    Private Sub SelectComboFormID(combo As ComboBox, fid As UInteger)
        For i = 0 To combo.Items.Count - 1
            Dim ci = TryCast(combo.Items(i), ComboItem)
            If ci IsNot Nothing AndAlso ci.FormID = fid Then
                combo.SelectedIndex = i
                Return
            End If
        Next
        If fid <> 0UI Then
            Dim ci As New ComboItem With {.FormID = fid, .Display = _mainForm.GetRecordDisplayNameForEditor(fid)}
            combo.Items.Add(ci)
            combo.SelectedItem = ci
        ElseIf combo.Items.Count > 0 Then
            combo.SelectedIndex = 0
        End If
    End Sub

    Private Shared Function SelectedComboFormID(combo As ComboBox) As UInteger
        Dim ci = TryCast(combo.SelectedItem, ComboItem)
        Return If(ci IsNot Nothing, ci.FormID, 0UI)
    End Function

    ' =====================================================================
    ' Draft list
    ' =====================================================================

    Private Sub RefreshDraftList()
        Dim keepFid As UInteger = SelectedDraftFormID()
        ListViewDrafts.BeginUpdate()
        Try
            ListViewDrafts.Items.Clear()
            For Each d In _mainForm.ArmoDrafts()
                AddDraftRow(d.FormID, If(Not String.IsNullOrEmpty(d.FullName), d.FullName, d.EditorID), "ARMO", d.IsOverride, d.IsModified, keepFid)
            Next
            For Each d In _mainForm.ArmaDrafts()
                AddDraftRow(d.FormID, d.EditorID, "ARMA", d.IsOverride, d.IsModified, keepFid)
            Next
            For Each d In _mainForm.MswpDrafts()
                AddDraftRow(d.FormID, d.EditorID, "MSWP", d.IsOverride, d.IsModified, keepFid)
            Next
        Finally
            ListViewDrafts.EndUpdate()
        End Try
    End Sub

    Private Sub AddDraftRow(fid As UInteger, name As String, type As String, isOverride As Boolean, isModified As Boolean, keepFid As UInteger)
        ' Skip the throwaway preview sentinels — they're never user drafts.
        If fid = OutfitDraft.PreviewDraftFormID OrElse fid = PreviewArmoWrapperFormID Then Return
        Dim status As String
        If isOverride Then
            status = "(override)"
        ElseIf isModified Then
            status = "(modified)"
        Else
            status = "(new)"
        End If
        Dim row As New ListViewItem(If(String.IsNullOrEmpty(name), fid.ToString("X8"), name))
        row.SubItems.Add(type)
        row.SubItems.Add(status)
        row.Tag = fid
        If keepFid <> 0UI AndAlso fid = keepFid Then row.Selected = True
        ListViewDrafts.Items.Add(row)
    End Sub

    Private Function SelectedDraftFormID() As UInteger
        If ListViewDrafts.SelectedItems.Count = 0 Then Return 0UI
        Return CUInt(ListViewDrafts.SelectedItems(0).Tag)
    End Function

    Private Sub OnDraftSelectionChanged(sender As Object, e As EventArgs)
        Dim fid = SelectedDraftFormID()
        If fid = 0UI Then
            ShowPanelFor(DraftKind.None)
            Return
        End If
        Dim md = _mainForm.TryGetMswpDraft(fid)
        If md IsNot Nothing Then
            LoadMswp(md)
            Return
        End If
        Dim ad = _mainForm.TryGetArmoDraft(fid)
        If ad IsNot Nothing Then
            LoadArmo(ad)
            Return
        End If
        Dim aad = _mainForm.TryGetArmaDraft(fid)
        If aad IsNot Nothing Then
            LoadArma(aad)
            Return
        End If
        ShowPanelFor(DraftKind.None)
    End Sub

    Private Sub SelectDraftInList(fid As UInteger)
        For Each row As ListViewItem In ListViewDrafts.Items
            If CUInt(row.Tag) = fid Then
                ListViewDrafts.SelectedItems.Clear()
                row.Selected = True
                row.EnsureVisible()
                Return
            End If
        Next
    End Sub

    ' =====================================================================
    ' Panel switching
    ' =====================================================================

    Private Sub ShowPanelFor(kind As DraftKind)
        _currentKind = kind
        GroupBoxArmo.Visible = (kind = DraftKind.Armo)
        GroupBoxArma.Visible = (kind = DraftKind.Arma)
        GroupBoxMswp.Visible = (kind = DraftKind.Mswp)
        LabelNoSelection.Visible = (kind = DraftKind.None)
        If kind <> DraftKind.None Then
            ' Bring the active group to front so it covers the host panel area.
            Select Case kind
                Case DraftKind.Armo : GroupBoxArmo.BringToFront()
                Case DraftKind.Arma : GroupBoxArma.BringToFront()
                Case DraftKind.Mswp : GroupBoxMswp.BringToFront()
            End Select
        End If
    End Sub

    ' =====================================================================
    ' New drafts
    ' =====================================================================

    Private Sub OnNewArmo(sender As Object, e As EventArgs)
        Dim name = PromptName("New ARMO", ArmoDraft.EditorIdPrefix)
        If name Is Nothing Then Return
        Dim d As New ArmoDraft With {.FormID = _mainForm.AllocateDraftFormID(),
                                     .EditorID = ArmoDraft.EditorIdPrefix & name,
                                     .RaceFormID = _raceFormID, .IsNew = True}
        _mainForm.RegisterArmoDraft(d)
        RefreshDraftList()
        SelectDraftInList(d.FormID)
    End Sub

    Private Sub OnNewArma(sender As Object, e As EventArgs)
        Dim name = PromptName("New ARMA", ArmaDraft.EditorIdPrefix)
        If name Is Nothing Then Return
        Dim d = CreateArmaDraft(name)
        _mainForm.RegisterArmaDraft(d)
        RefreshDraftList()
        SelectDraftInList(d.FormID)
    End Sub

    Private Function CreateArmaDraft(name As String) As ArmaDraft
        Return New ArmaDraft With {.FormID = _mainForm.AllocateDraftFormID(),
                                   .EditorID = ArmaDraft.EditorIdPrefix & name,
                                   .RaceFormID = _raceFormID, .IsNew = True}
    End Function

    Private Sub OnNewMswp(sender As Object, e As EventArgs)
        Dim name = PromptName("New MSWP", MswpDraft.EditorIdPrefix)
        If name Is Nothing Then Return
        Dim d = CreateMswpDraft(name)
        _mainForm.RegisterMswpDraft(d)
        RefreshMswpCombos()
        RefreshDraftList()
        SelectDraftInList(d.FormID)
    End Sub

    Private Function CreateMswpDraft(name As String) As MswpDraft
        Return New MswpDraft With {.FormID = _mainForm.AllocateDraftFormID(),
                                   .EditorID = MswpDraft.EditorIdPrefix & name,
                                   .IsNew = True}
    End Function

    ''' <summary>Prompt for a name suffix, validating EditorID uniqueness against the prefix. Nothing = cancel.</summary>
    Private Function PromptName(title As String, prefix As String) As String
        Dim suffix = InputBox($"{title}{vbCrLf}{vbCrLf}EditorID = {prefix}<name>", title, "")
        If suffix Is Nothing Then Return Nothing
        suffix = suffix.Trim()
        If suffix.Length = 0 Then Return Nothing
        Dim full = prefix & suffix
        If Not _mainForm.IsRecordEditorIdAvailable(full) Then
            MessageBox.Show(Me, $"EditorID '{full}' is already in use. Choose another name.",
                            title, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return Nothing
        End If
        Return suffix
    End Function

    ' =====================================================================
    ' Override existing — load a real ARMO/ARMA record into a draft (IsOverride=True).
    ' =====================================================================

    Private Sub OnOverrideExisting(sender As Object, e As EventArgs)
        Using dlg As New OverridePicker_Form(_mainForm)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            If dlg.SelectedFormID = 0UI Then Return
            If dlg.SelectedIsArma Then
                Dim d = BuildArmaDraftFromExisting(dlg.SelectedFormID)
                If d Is Nothing Then
                    MessageBox.Show(Me, "Could not parse that ARMA record.", "Override", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return
                End If
                _mainForm.RegisterArmaDraft(d)
                RefreshDraftList()
                SelectDraftInList(d.FormID)
            Else
                Dim d = BuildArmoDraftFromExisting(dlg.SelectedFormID)
                If d Is Nothing Then
                    MessageBox.Show(Me, "Could not parse that ARMO record.", "Override", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return
                End If
                _mainForm.RegisterArmoDraft(d)
                RefreshDraftList()
                SelectDraftInList(d.FormID)
            End If
        End Using
    End Sub

    ''' <summary>Override-load converter (reverse of MainForm.BuildArmoDataFromDraft): parse the real ARMO via
    ''' the draft-aware context and copy every editor-relevant field into a NEW ArmoDraft keeping the real
    ''' GLOBAL FormID + IsOverride=True. Subrecords the editor doesn't own (OBTS combinations, APPR, etc.) are
    ''' NOT mirrored into the draft — the saver copies them verbatim from the SourceRecord on override emit.</summary>
    Private Function BuildArmoDraftFromExisting(fid As UInteger) As ArmoDraft
        Dim a = _mainForm.GetParsedArmoForEditor(fid)
        If a Is Nothing Then Return Nothing
        Dim d As New ArmoDraft With {
            .FormID = fid,
            .EditorID = If(Not String.IsNullOrEmpty(a.EditorID), a.EditorID, ArmoDraft.EditorIdPrefix & fid.ToString("X8")),
            .FullName = a.FullName,
            .SlotMask = a.SlotMask,
            .RaceFormID = a.RaceFormID,
            .TemplateArmorFormID = a.TemplateArmorFormID,
            .MaleWorldModelPath = a.MaleWorldModelPath,
            .FemaleWorldModelPath = a.FemaleWorldModelPath,
            .MaleMaterialSwapFormID = a.MaleMaterialSwapFormID,
            .FemaleMaterialSwapFormID = a.FemaleMaterialSwapFormID,
            .Value = a.Value,
            .Weight = a.Weight,
            .Health = a.Health,
            .ArmorRating = a.ArmorRating,
            .BaseAddonIndex = CUShort(If(a.BaseAddonIndex < 0, 0, a.BaseAddonIndex)),
            .StaggerRating = a.StaggerRating,
            .IsOverride = True, .IsNew = False, .IsModified = False
        }
        For Each addon In a.ArmorAddons
            d.ArmorAddons.Add(New ARMO_AddonEntry With {.AddonIndex = addon.AddonIndex, .ArmaFormID = addon.ArmaFormID})
        Next
        d.KeywordFormIDs.AddRange(a.KeywordFormIDs)
        d.AttachParentSlotFormIDs.AddRange(a.AttachParentSlotFormIDs)
        Return d
    End Function

    ''' <summary>Override-load converter (reverse of MainForm.BuildArmaDataFromDraft): parse the real ARMA and
    ''' copy every editor-relevant field into a NEW ArmaDraft keeping the real GLOBAL FormID + IsOverride=True.
    ''' Bone-scale (BSMP/BSMB/BSMS) IS copied so an override preserves it even though the editor can't edit it
    ''' yet (see the bone-scale TODO). Priorities widen Integer→Byte (clamped).</summary>
    Private Function BuildArmaDraftFromExisting(fid As UInteger) As ArmaDraft
        Dim a = _mainForm.GetParsedArmaForEditor(fid)
        If a Is Nothing Then Return Nothing
        Dim d As New ArmaDraft With {
            .FormID = fid,
            .EditorID = If(Not String.IsNullOrEmpty(a.EditorID), a.EditorID, ArmaDraft.EditorIdPrefix & fid.ToString("X8")),
            .SlotMask = a.SlotMask,
            .RaceFormID = a.RaceFormID,
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
            .NoUnderarmorScaling = a.NoUnderarmorScaling,
            .HasSculptData = a.HasSculptData,
            .HiRes1stPersonOnly = a.HiRes1stPersonOnly,
            .IsOverride = True, .IsNew = False, .IsModified = False
        }
        d.AdditionalRaces.AddRange(a.AdditionalRaces)
        ' Preserve bone-scale verbatim (editing TODO).
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
    ' Delete draft
    ' =====================================================================

    Private Sub OnDeleteDraft(sender As Object, e As EventArgs)
        Dim fid = SelectedDraftFormID()
        If fid = 0UI Then Return
        _mainForm.UnregisterArmoDraft(fid)
        _mainForm.UnregisterArmaDraft(fid)
        _mainForm.UnregisterMswpDraft(fid)
        RefreshMswpCombos()
        RefreshDraftList()
        ShowPanelFor(DraftKind.None)
        ClearPreview()
    End Sub

    ' =====================================================================
    ' Apply (commit the panel edits back into the selected draft)
    ' =====================================================================

    Private Sub OnApply(sender As Object, e As EventArgs)
        Select Case _currentKind
            Case DraftKind.Armo : ApplyArmo()
            Case DraftKind.Arma : ApplyArma()
            Case DraftKind.Mswp : ApplyMswp()
        End Select
        RefreshMswpCombos()
        RefreshDraftList()
        ' Re-register so the draft-aware resolver / save flow see the edits, then re-preview.
        RequestPreview()
    End Sub

    ' =====================================================================
    ' ARMO panel load / apply
    ' =====================================================================

    Private Sub LoadArmo(d As ArmoDraft)
        _loading = True
        Try
            _currentArmo = d
            _currentArma = Nothing
            _currentMswp = Nothing
            ShowPanelFor(DraftKind.Armo)
            TextBoxArmoEdid.Text = d.EditorID
            TextBoxArmoFull.Text = d.FullName
            SelectComboFormID(ComboArmoRace, d.RaceFormID)
            SetSlotChecks(_armoSlotChecks, d.SlotMask)
            NumArmoValue.Value = ClampDec(d.Value, NumArmoValue)
            NumArmoWeight.Value = ClampDec(CDec(d.Weight), NumArmoWeight)
            NumArmoHealth.Value = ClampDec(CDec(d.Health), NumArmoHealth)
            NumArmoRating.Value = ClampDec(CDec(d.ArmorRating), NumArmoRating)
            NumArmoBaseAddon.Value = ClampDec(CDec(d.BaseAddonIndex), NumArmoBaseAddon)
            NumArmoStagger.Value = ClampDec(CDec(d.StaggerRating), NumArmoStagger)
            SelectComboFormID(ComboArmoMswp, d.MaleMaterialSwapFormID)
            SelectComboFormID(ComboArmoTnam, d.TemplateArmorFormID)
            RefreshArmoKeywordList()
            RefreshArmoAddonList()
        Finally
            _loading = False
        End Try
        RequestPreview()
    End Sub

    Private Sub ApplyArmo()
        If _currentArmo Is Nothing Then Return
        Dim d = _currentArmo
        d.FullName = TextBoxArmoFull.Text.Trim()
        d.RaceFormID = SelectedComboFormID(ComboArmoRace)
        d.SlotMask = ReadSlotChecks(_armoSlotChecks)
        d.Value = CInt(NumArmoValue.Value)
        d.Weight = CSng(NumArmoWeight.Value)
        d.Health = CUInt(NumArmoHealth.Value)
        d.ArmorRating = CUShort(NumArmoRating.Value)
        d.BaseAddonIndex = CUShort(NumArmoBaseAddon.Value)
        d.StaggerRating = CByte(NumArmoStagger.Value)
        ' World-model material swap applies to both genders for simplicity (MO2S + MO4S).
        Dim mswp = SelectedComboFormID(ComboArmoMswp)
        d.MaleMaterialSwapFormID = mswp
        d.FemaleMaterialSwapFormID = mswp
        d.TemplateArmorFormID = SelectedComboFormID(ComboArmoTnam)
        MarkDirty(d)
        _mainForm.RegisterArmoDraft(d)
    End Sub

    Private Sub RefreshArmoKeywordList()
        ListViewArmoKeywords.BeginUpdate()
        Try
            ListViewArmoKeywords.Items.Clear()
            If _currentArmo Is Nothing Then Return
            For Each kw In _currentArmo.KeywordFormIDs
                Dim row As New ListViewItem(_mainForm.GetRecordDisplayNameForEditor(kw))
                row.Tag = kw
                ListViewArmoKeywords.Items.Add(row)
            Next
        Finally
            ListViewArmoKeywords.EndUpdate()
        End Try
    End Sub

    Private Sub OnArmoAddKeyword(sender As Object, e As EventArgs)
        If _currentArmo Is Nothing Then Return
        Dim fid = SelectedComboFormID(ComboArmoKeyword)
        If fid = 0UI Then Return
        If Not _currentArmo.KeywordFormIDs.Contains(fid) Then
            _currentArmo.KeywordFormIDs.Add(fid)
            MarkDirty(_currentArmo)
            RefreshArmoKeywordList()
        End If
    End Sub

    Private Sub OnArmoRemoveKeyword(sender As Object, e As EventArgs)
        If _currentArmo Is Nothing OrElse ListViewArmoKeywords.SelectedItems.Count = 0 Then Return
        Dim fid = CUInt(ListViewArmoKeywords.SelectedItems(0).Tag)
        _currentArmo.KeywordFormIDs.Remove(fid)
        MarkDirty(_currentArmo)
        RefreshArmoKeywordList()
    End Sub

    Private Sub RefreshArmoAddonList()
        ListViewArmoAddons.BeginUpdate()
        Try
            ListViewArmoAddons.Items.Clear()
            If _currentArmo Is Nothing Then Return
            For Each addon In _currentArmo.ArmorAddons
                Dim row As New ListViewItem(addon.AddonIndex.ToString())
                row.SubItems.Add(_mainForm.GetRecordDisplayNameForEditor(addon.ArmaFormID))
                row.Tag = addon
                ListViewArmoAddons.Items.Add(row)
            Next
        Finally
            ListViewArmoAddons.EndUpdate()
        End Try
    End Sub

    Private Sub OnArmoAddAddonExisting(sender As Object, e As EventArgs)
        If _currentArmo Is Nothing Then Return
        Using dlg As New OverridePicker_Form(_mainForm, armaOnly:=True)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            AddArmoAddon(dlg.SelectedFormID)
        End Using
    End Sub

    Private Sub OnArmoAddAddonDraft(sender As Object, e As EventArgs)
        If _currentArmo Is Nothing Then Return
        ' Pick an existing ARMA draft, or create one on the fly.
        Dim armaDrafts = _mainForm.ArmaDrafts()
        If armaDrafts.Count = 0 Then
            Dim name = PromptName("New ARMA addon", ArmaDraft.EditorIdPrefix)
            If name Is Nothing Then Return
            Dim nd = CreateArmaDraft(name)
            _mainForm.RegisterArmaDraft(nd)
            AddArmoAddon(nd.FormID)
            RefreshDraftList()
            Return
        End If
        Using dlg As New DraftPicker_Form("Pick ARMA draft", armaDrafts.Select(Function(a) (a.FormID, a.EditorID)).ToList())
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            AddArmoAddon(dlg.SelectedFormID)
        End Using
    End Sub

    Private Sub AddArmoAddon(armaFid As UInteger)
        If _currentArmo Is Nothing OrElse armaFid = 0UI Then Return
        Dim indx = CUShort(NumArmoAddonIndx.Value)
        _currentArmo.ArmorAddons.Add(New ARMO_AddonEntry With {.AddonIndex = indx, .ArmaFormID = armaFid})
        MarkDirty(_currentArmo)
        RefreshArmoAddonList()
        RequestPreview()
    End Sub

    Private Sub OnArmoRemoveAddon(sender As Object, e As EventArgs)
        If _currentArmo Is Nothing OrElse ListViewArmoAddons.SelectedItems.Count = 0 Then Return
        Dim entry = TryCast(ListViewArmoAddons.SelectedItems(0).Tag, ARMO_AddonEntry)
        If entry Is Nothing Then Return
        _currentArmo.ArmorAddons.Remove(entry)
        MarkDirty(_currentArmo)
        RefreshArmoAddonList()
        RequestPreview()
    End Sub

    Private Sub OnArmoAddonUp(sender As Object, e As EventArgs)
        MoveSelectedAddon(-1)
    End Sub

    Private Sub OnArmoAddonDown(sender As Object, e As EventArgs)
        MoveSelectedAddon(1)
    End Sub

    Private Sub MoveSelectedAddon(delta As Integer)
        If _currentArmo Is Nothing OrElse ListViewArmoAddons.SelectedItems.Count = 0 Then Return
        Dim entry = TryCast(ListViewArmoAddons.SelectedItems(0).Tag, ARMO_AddonEntry)
        If entry Is Nothing Then Return
        Dim idx = _currentArmo.ArmorAddons.IndexOf(entry)
        Dim newIdx = idx + delta
        If newIdx < 0 OrElse newIdx >= _currentArmo.ArmorAddons.Count Then Return
        _currentArmo.ArmorAddons.RemoveAt(idx)
        _currentArmo.ArmorAddons.Insert(newIdx, entry)
        MarkDirty(_currentArmo)
        RefreshArmoAddonList()
        If newIdx < ListViewArmoAddons.Items.Count Then ListViewArmoAddons.Items(newIdx).Selected = True
    End Sub

    Private Sub OnArmoNewMswp(sender As Object, e As EventArgs)
        Dim newFid = CreateMswpInteractive()
        If newFid = 0UI Then Return
        SelectComboFormID(ComboArmoMswp, newFid)
        If _currentArmo IsNot Nothing Then
            _currentArmo.MaleMaterialSwapFormID = newFid
            _currentArmo.FemaleMaterialSwapFormID = newFid
            MarkDirty(_currentArmo)
        End If
    End Sub

    ' =====================================================================
    ' ARMA panel load / apply
    ' =====================================================================

    Private Sub LoadArma(d As ArmaDraft)
        _loading = True
        Try
            _currentArma = d
            _currentArmo = Nothing
            _currentMswp = Nothing
            ShowPanelFor(DraftKind.Arma)
            TextBoxArmaEdid.Text = d.EditorID
            SelectComboFormID(ComboArmaRace, d.RaceFormID)
            SetSlotChecks(_armaSlotChecks, d.SlotMask)
            TextBoxArmaMeshMale.Text = d.MaleMeshPath
            TextBoxArmaMeshFemale.Text = d.FemaleMeshPath
            TextBoxArmaMeshMaleFp.Text = d.MaleFPMeshPath
            TextBoxArmaMeshFemaleFp.Text = d.FemaleFPMeshPath
            CheckArmaMaleFaceBones.Checked = (d.MaleModelFlags And &H1) <> 0
            CheckArmaMale1stPerson.Checked = (d.MaleModelFlags And &H2) <> 0
            CheckArmaFemaleFaceBones.Checked = (d.FemaleModelFlags And &H1) <> 0
            CheckArmaFemale1stPerson.Checked = (d.FemaleModelFlags And &H2) <> 0
            SelectComboFormID(ComboArmaTxstMale, d.MaleSkinTextureFormID)
            SelectComboFormID(ComboArmaTxstFemale, d.FemaleSkinTextureFormID)
            SelectComboFormID(ComboArmaMswpMale, d.MaleMaterialSwapFormID)
            SelectComboFormID(ComboArmaMswpFemale, d.FemaleMaterialSwapFormID)
            NumArmaMalePrio.Value = ClampDec(CDec(d.MalePriority), NumArmaMalePrio)
            NumArmaFemalePrio.Value = ClampDec(CDec(d.FemalePriority), NumArmaFemalePrio)
            CheckArmaMaleWeightEnabled.Checked = (d.MaleWeightSliderFlags And &H2) <> 0
            CheckArmaFemaleWeightEnabled.Checked = (d.FemaleWeightSliderFlags And &H2) <> 0
            NumArmaDetSound.Value = ClampDec(CDec(d.DetectionSoundValue), NumArmaDetSound)
            NumArmaWeaponAdjust.Value = ClampDec(CDec(d.WeaponAdjust), NumArmaWeaponAdjust)
            RefreshArmaRaceList()
        Finally
            _loading = False
        End Try
        RequestPreview()
    End Sub

    Private Sub ApplyArma()
        If _currentArma Is Nothing Then Return
        Dim d = _currentArma
        d.RaceFormID = SelectedComboFormID(ComboArmaRace)
        d.SlotMask = ReadSlotChecks(_armaSlotChecks)
        d.MaleMeshPath = TextBoxArmaMeshMale.Text.Trim()
        d.FemaleMeshPath = TextBoxArmaMeshFemale.Text.Trim()
        d.MaleFPMeshPath = TextBoxArmaMeshMaleFp.Text.Trim()
        d.FemaleFPMeshPath = TextBoxArmaMeshFemaleFp.Text.Trim()
        d.MaleModelFlags = BuildModelFlags(CheckArmaMaleFaceBones.Checked, CheckArmaMale1stPerson.Checked)
        d.FemaleModelFlags = BuildModelFlags(CheckArmaFemaleFaceBones.Checked, CheckArmaFemale1stPerson.Checked)
        d.MaleSkinTextureFormID = SelectedComboFormID(ComboArmaTxstMale)
        d.FemaleSkinTextureFormID = SelectedComboFormID(ComboArmaTxstFemale)
        d.MaleMaterialSwapFormID = SelectedComboFormID(ComboArmaMswpMale)
        d.FemaleMaterialSwapFormID = SelectedComboFormID(ComboArmaMswpFemale)
        d.MalePriority = CByte(NumArmaMalePrio.Value)
        d.FemalePriority = CByte(NumArmaFemalePrio.Value)
        d.MaleWeightSliderFlags = If(CheckArmaMaleWeightEnabled.Checked, CByte(&H2), CByte(0))
        d.FemaleWeightSliderFlags = If(CheckArmaFemaleWeightEnabled.Checked, CByte(&H2), CByte(0))
        d.DetectionSoundValue = CByte(NumArmaDetSound.Value)
        d.WeaponAdjust = CSng(NumArmaWeaponAdjust.Value)
        ' Bone-scale (BSMP/BSMB/BSMS): editing TODO — d.BoneScaleData is left untouched so an override
        ' preserves whatever was loaded from the source record. New drafts simply have none.
        MarkDirty(d)
        _mainForm.RegisterArmaDraft(d)
    End Sub

    Private Shared Function BuildModelFlags(faceBones As Boolean, firstPerson As Boolean) As Byte
        Dim f As Byte = 0
        If faceBones Then f = CByte(f Or &H1)
        If firstPerson Then f = CByte(f Or &H2)
        Return f
    End Function

    Private Sub RefreshArmaRaceList()
        ListViewArmaAddRaces.BeginUpdate()
        Try
            ListViewArmaAddRaces.Items.Clear()
            If _currentArma Is Nothing Then Return
            For Each r In _currentArma.AdditionalRaces
                Dim row As New ListViewItem(_mainForm.GetRecordDisplayNameForEditor(r))
                row.Tag = r
                ListViewArmaAddRaces.Items.Add(row)
            Next
        Finally
            ListViewArmaAddRaces.EndUpdate()
        End Try
    End Sub

    Private Sub OnArmaAddRace(sender As Object, e As EventArgs)
        If _currentArma Is Nothing Then Return
        Dim fid = SelectedComboFormID(ComboArmaAddRace)
        If fid = 0UI Then Return
        If Not _currentArma.AdditionalRaces.Contains(fid) Then
            _currentArma.AdditionalRaces.Add(fid)
            MarkDirty(_currentArma)
            RefreshArmaRaceList()
        End If
    End Sub

    Private Sub OnArmaRemoveRace(sender As Object, e As EventArgs)
        If _currentArma Is Nothing OrElse ListViewArmaAddRaces.SelectedItems.Count = 0 Then Return
        Dim fid = CUInt(ListViewArmaAddRaces.SelectedItems(0).Tag)
        _currentArma.AdditionalRaces.Remove(fid)
        MarkDirty(_currentArma)
        RefreshArmaRaceList()
    End Sub

    Private Sub OnArmaBrowseMeshMale(sender As Object, e As EventArgs)
        BrowseMeshInto(TextBoxArmaMeshMale)
    End Sub

    Private Sub OnArmaBrowseMeshFemale(sender As Object, e As EventArgs)
        BrowseMeshInto(TextBoxArmaMeshFemale)
    End Sub

    Private Sub OnArmaBrowseMeshMaleFp(sender As Object, e As EventArgs)
        BrowseMeshInto(TextBoxArmaMeshMaleFp)
    End Sub

    Private Sub OnArmaBrowseMeshFemaleFp(sender As Object, e As EventArgs)
        BrowseMeshInto(TextBoxArmaMeshFemaleFp)
    End Sub

    Private Sub BrowseMeshInto(target As TextBox)
        Using dlg As New AssetBrowser_Form("Pick mesh", "Meshes\", New String() {".nif"}, target.Text.Trim())
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso Not String.IsNullOrEmpty(dlg.SelectedPath) Then
                target.Text = dlg.SelectedPath
            End If
        End Using
    End Sub

    Private Sub OnArmaNewMswp(sender As Object, e As EventArgs)
        Dim newFid = CreateMswpInteractive()
        If newFid = 0UI Then Return
        SelectComboFormID(ComboArmaMswpMale, newFid)
        SelectComboFormID(ComboArmaMswpFemale, newFid)
    End Sub

    ' =====================================================================
    ' MSWP panel load / apply + the shared "New MSWP…" creator
    ' =====================================================================

    Private Sub LoadMswp(d As MswpDraft)
        _loading = True
        Try
            _currentMswp = d
            _currentArmo = Nothing
            _currentArma = Nothing
            ShowPanelFor(DraftKind.Mswp)
            TextBoxMswpEdid.Text = d.EditorID
            TextBoxMswpTreeFolder.Text = d.TreeFolder
            GridMswp.Rows.Clear()
            For Each s In d.Substitutions
                Dim remap = If(s.HasColorRemapIndex, s.ColorRemapIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), "")
                GridMswp.Rows.Add(s.OriginalMaterial, s.ReplacementMaterial, remap)
            Next
        Finally
            _loading = False
        End Try
    End Sub

    Private Sub ApplyMswp()
        If _currentMswp Is Nothing Then Return
        Dim d = _currentMswp
        d.TreeFolder = TextBoxMswpTreeFolder.Text.Trim()
        d.Substitutions.Clear()
        For Each row As DataGridViewRow In GridMswp.Rows
            If row.IsNewRow Then Continue For
            Dim orig = CStr(If(row.Cells(0).Value, "")).Trim()
            Dim repl = CStr(If(row.Cells(1).Value, "")).Trim()
            If orig.Length = 0 AndAlso repl.Length = 0 Then Continue For
            Dim sub_ As New MSWP_Substitution With {.OriginalMaterial = orig, .ReplacementMaterial = repl}
            Dim remapText = CStr(If(row.Cells(2).Value, "")).Trim()
            Dim remapVal As Single
            If remapText.Length > 0 AndAlso Single.TryParse(remapText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, remapVal) Then
                sub_.HasColorRemapIndex = True
                sub_.ColorRemapIndex = remapVal
            End If
            d.Substitutions.Add(sub_)
        Next
        MarkDirty(d)
        _mainForm.RegisterMswpDraft(d)
    End Sub

    Private Sub OnMswpAddRow(sender As Object, e As EventArgs)
        GridMswp.Rows.Add("", "", "")
    End Sub

    Private Sub OnMswpRemoveRow(sender As Object, e As EventArgs)
        If GridMswp.SelectedRows.Count = 0 Then Return
        For Each row As DataGridViewRow In GridMswp.SelectedRows
            If Not row.IsNewRow Then GridMswp.Rows.Remove(row)
        Next
    End Sub

    Private Sub OnMswpBrowseOriginal(sender As Object, e As EventArgs)
        BrowseMaterialIntoCell(0)
    End Sub

    Private Sub OnMswpBrowseReplacement(sender As Object, e As EventArgs)
        BrowseMaterialIntoCell(1)
    End Sub

    Private Sub BrowseMaterialIntoCell(colIndex As Integer)
        If GridMswp.CurrentRow Is Nothing OrElse GridMswp.CurrentRow.IsNewRow Then
            MessageBox.Show(Me, "Select a row first (Add row if empty).", "Browse material", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim current = CStr(If(GridMswp.CurrentRow.Cells(colIndex).Value, "")).Trim()
        Using dlg As New AssetBrowser_Form("Pick material", "Materials\", New String() {".bgsm", ".bgem"}, current)
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso Not String.IsNullOrEmpty(dlg.SelectedPath) Then
                GridMswp.CurrentRow.Cells(colIndex).Value = dlg.SelectedPath
            End If
        End Using
    End Sub

    ''' <summary>"New MSWP…": create a fresh MswpDraft, register it, open its panel for editing, and return its
    ''' FormID so the caller can select it in the relevant material-swap combo. 0 = cancelled.</summary>
    Private Function CreateMswpInteractive() As UInteger
        Dim name = PromptName("New MSWP", MswpDraft.EditorIdPrefix)
        If name Is Nothing Then Return 0UI
        Dim d = CreateMswpDraft(name)
        _mainForm.RegisterMswpDraft(d)
        RefreshMswpCombos()
        RefreshDraftList()
        Return d.FormID
    End Function

    ' =====================================================================
    ' Slot checkbox helpers
    ' =====================================================================

    Private Shared Sub SetSlotChecks(checks As Dictionary(Of Integer, CheckBox), mask As UInteger)
        For Each kv In checks
            Dim bit = kv.Key - 30
            kv.Value.Checked = (bit >= 0 AndAlso bit < 32 AndAlso (mask And (1UI << bit)) <> 0UI)
        Next
    End Sub

    Private Shared Function ReadSlotChecks(checks As Dictionary(Of Integer, CheckBox)) As UInteger
        Dim mask As UInteger = 0UI
        For Each kv In checks
            If kv.Value.Checked Then
                Dim bit = kv.Key - 30
                If bit >= 0 AndAlso bit < 32 Then mask = mask Or (1UI << bit)
            End If
        Next
        Return mask
    End Function

    ' =====================================================================
    ' Dirty marking
    ' =====================================================================

    Private Shared Sub MarkDirty(d As ArmoDraft)
        If Not d.IsNew Then d.IsModified = True
    End Sub

    Private Shared Sub MarkDirty(d As ArmaDraft)
        If Not d.IsNew Then d.IsModified = True
    End Sub

    Private Shared Sub MarkDirty(d As MswpDraft)
        If Not d.IsNew Then d.IsModified = True
    End Sub

    ' =====================================================================
    ' Field-edit → debounced preview
    ' =====================================================================

    Private Sub OnFieldEdited(sender As Object, e As EventArgs)
        If _loading Then Return
        ' A real user edit: the debounce tick must COMMIT the panel into the draft before rendering.
        _pendingApply = True
        RequestPreview()
    End Sub

    ''' <summary>Restart the debounce timer; on tick the current panel is (optionally) committed to its draft
    ''' and re-rendered (so the preview follows live edits without re-rendering on every keystroke). The commit
    ''' only happens when <see cref="_pendingApply"/> was set by an actual user edit — a pure load/selection
    ''' render-only request does NOT mark the draft modified.</summary>
    Private Sub RequestPreview()
        If _host Is Nothing Then Return
        _previewDebounce.Stop()
        _previewDebounce.Start()
    End Sub

    ''' <summary>Set when a real user edit is pending commit; consumed by the debounce tick. Keeps a pure
    ''' load/selection render from spuriously marking a clean draft IsModified.</summary>
    Private _pendingApply As Boolean

    Private Async Sub PreviewDebounce_Tick(sender As Object, e As EventArgs) Handles _previewDebounce.Tick
        _previewDebounce.Stop()
        ' Commit current panel edits into the draft (only after a real edit) so the throwaway preview reads
        ' them, then render.
        If _pendingApply Then
            _pendingApply = False
            Select Case _currentKind
                Case DraftKind.Armo : ApplyArmo()
                Case DraftKind.Arma : ApplyArma()
                Case DraftKind.Mswp : ApplyMswp()
            End Select
        End If
        Await RenderPreviewAsync()
    End Sub

    ' =====================================================================
    ' WYSIWYG preview — wrap the draft in a throwaway OTFT (reuse OutfitPicker mechanism)
    ' =====================================================================

    ''' <summary>Render the currently-selected ARMO (or standalone ARMA wrapped in a throwaway ARMO) equipped
    ''' on the preview NPC. Mirrors OutfitPicker: register a throwaway OutfitDraft at
    ''' <see cref="OutfitDraft.PreviewDraftFormID"/> whose items = the ARMO draft FormID, then call
    ''' <see cref="MainForm.PreviewOutfitInHostAsync"/>. The draft-aware resolver makes the ARMO + its ARMA
    ''' addons render. Skipped when there's no preview NPC or nothing armor-shaped is selected.</summary>
    Private Async Function RenderPreviewAsync() As Task
        If _host Is Nothing OrElse _preview Is Nothing OrElse _preview.IsDisposed Then Return
        If _previewNpcFormID = 0UI Then Return

        Dim armoFid As UInteger = 0UI
        If _currentKind = DraftKind.Armo AndAlso _currentArmo IsNot Nothing Then
            armoFid = _currentArmo.FormID
        ElseIf _currentKind = DraftKind.Arma AndAlso _currentArma IsNot Nothing Then
            armoFid = EnsureArmaPreviewWrapper(_currentArma.FormID)
        Else
            ' MSWP / nothing armor-shaped → clear preview.
            ClearPreview()
            Return
        End If
        If armoFid = 0UI Then Return

        Dim key As String = _currentKind.ToString() & ":" & armoFid.ToString("X8")
        If key = _lastPreviewKey Then Return
        If _previewInProgress Then Return
        _previewInProgress = True
        Try
            ' Throwaway OTFT containing just this ARMO (reuse OutfitPicker's preview-draft mechanism).
            Dim otft As New OutfitDraft With {.FormID = OutfitDraft.PreviewDraftFormID,
                                              .EditorID = OutfitDraft.EditorIdPrefix & "(armorpreview)"}
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

    ''' <summary>For a standalone ARMA draft: (re)register a throwaway ARMO draft (single addon INDX 0) that
    ''' references the ARMA, so the OTFT preview can render it. Returns the wrapper ARMO FormID.</summary>
    Private Function EnsureArmaPreviewWrapper(armaFid As UInteger) As UInteger
        Dim wrapper As New ArmoDraft With {.FormID = PreviewArmoWrapperFormID,
                                           .EditorID = ArmoDraft.EditorIdPrefix & "(armapreview)",
                                           .RaceFormID = _raceFormID}
        wrapper.ArmorAddons.Add(New ARMO_AddonEntry With {.AddonIndex = 0US, .ArmaFormID = armaFid})
        _mainForm.RegisterArmoDraft(wrapper)
        _previewArmaWrapperRegistered = True
        Return PreviewArmoWrapperFormID
    End Function

    Private Sub ClearPreview()
        _lastPreviewKey = Nothing
        Try
            _preview?.RenderShapes(New List(Of IRenderableShape))
        Catch
        End Try
    End Sub

    ' =====================================================================
    ' Form lifecycle (preview host setup/teardown — mirror OutfitPicker)
    ' =====================================================================

    Private Sub ArmorEditor_Form_Shown(sender As Object, e As EventArgs) Handles Me.Shown
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

    Private Sub ArmorEditor_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
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

    ' =====================================================================
    ' Helpers
    ' =====================================================================

    ''' <summary>Clamp a value to a NumericUpDown's [Minimum, Maximum] so loading an out-of-range draft value
    ''' (e.g. a huge Health) never throws. Decimal in / Decimal out.</summary>
    Private Shared Function ClampDec(v As Decimal, num As NumericUpDown) As Decimal
        If v < num.Minimum Then Return num.Minimum
        If v > num.Maximum Then Return num.Maximum
        Return v
    End Function

    ''' <summary>Human-readable biped slot name (FO4 slots 30-61). Mirrors OutfitPicker_Form.SlotName.</summary>
    Private Shared Function SlotName(slot As Integer) As String
        Select Case slot
            Case 30 : Return "30 HairTop"
            Case 31 : Return "31 HairLong"
            Case 32 : Return "32 FaceHead"
            Case 33 : Return "33 BODY"
            Case 34 : Return "34 LHand"
            Case 35 : Return "35 RHand"
            Case 36 : Return "36 [U]Torso"
            Case 37 : Return "37 [U]LArm"
            Case 38 : Return "38 [U]RArm"
            Case 39 : Return "39 [U]LLeg"
            Case 40 : Return "40 [U]RLeg"
            Case 41 : Return "41 [A]Torso"
            Case 42 : Return "42 [A]LArm"
            Case 43 : Return "43 [A]RArm"
            Case 44 : Return "44 [A]LLeg"
            Case 45 : Return "45 [A]RLeg"
            Case 46 : Return "46 Headband"
            Case 47 : Return "47 Eyes"
            Case 48 : Return "48 Beard"
            Case 49 : Return "49 Mouth"
            Case 50 : Return "50 Neck"
            Case 51 : Return "51 Ring"
            Case Else : Return "s" & slot.ToString()
        End Select
    End Function

End Class
