Imports System.Globalization
Imports System.Linq
Imports FO4_Base_Library

''' <summary>Multi-tab editor for a single NPC_ record — Name/flags/level + identity FormIDs (General),
''' the object-template combinations (Object Template), keywords, factions and inventory. Companion to
''' <see cref="ArmoEditor_Form"/>: same TabControl shape, read-only grids edited via modal sub-dialogs
''' (project rule), working buffers deep-copied at the borders so Cancel never mutates the caller.
'''
''' PERSISTENCE: on OK the editor does TWO things. (1) It records the edit as an <see cref="NpcRecordOverride"/>
''' (only the fields the user changed) via MainForm.SetNpcRecordOverride — a bag mirrored on _appliedPresets.
''' At Save the MainForm.ApplyNpcRecordOverride delegate (wired onto NpcOverrideSaver.SaveContext) applies it
''' onto the save shadow JUST AFTER CopyRoundTripOnlyFieldsFromRaw, so the edit WINS over the fresh re-parse
''' without the round-trip copy being altered. (2) It also mutates the LIVE in-memory NPC_Data (render cache)
''' so the preview reflects the edit immediately. The override MERGES across sessions so successive edits
''' accumulate (TraitsChanged latches). Fields the user didn't touch round-trip verbatim from the source.
'''
''' Template-flag hook: before applying an edit whose data-category is template-inherited, the editor calls
''' <see cref="NpcTemplateMaterializer.MakeCategoryOwn"/> (materialize → clear Use-X flag) so the edit is not
''' silently overwritten by the engine's CopyFromTemplate. Only categories whose fields actually changed are
''' materialized (no-op when the NPC already owns the category).</summary>
Public Class NpcEditor_Form

    Private ReadOnly _mainForm As MainForm
    ''' <summary>The LIVE NPC_Data (render-cache instance). Read at open; mutated only on OK (Cancel = untouched).</summary>
    Private ReadOnly _npc As NPC_Data
    Private ReadOnly _npcFormID As UInteger
    Private ReadOnly _getParsedNpc As Func(Of UInteger, NPC_Data)
    ''' <summary>Suppresses side effects while panels are LOADED programmatically.</summary>
    Private _loading As Boolean

    ' Working buffers (edited by the Add/Remove/modal handlers; flushed into _npc only on OK).
    Private ReadOnly _keywords As New List(Of UInteger)
    Private ReadOnly _appr As New List(Of UInteger)          ' APPR — attach-parent-slot KYWDs
    Private ReadOnly _factions As New List(Of NPC_FactionEntry)
    Private ReadOnly _inventory As New List(Of NPC_InventoryItem)
    Private ReadOnly _perks As New List(Of NPC_PerkEntry)          ' PRKR
    Private ReadOnly _actorEffects As New List(Of UInteger)        ' SPLO → SPEL
    Private ReadOnly _properties As New List(Of NPC_PropertyEntry) ' PRPS
    Private ReadOnly _combos As New List(Of NPC_ObjectTemplateCombination)

    ' ACBS flag bit map (checkbox → bit) built from the NPC_AcbsData bit definitions. The union is used to
    ' preserve bits NOT surfaced as a checkbox when re-composing the flags word.
    Private ReadOnly _flagChecks As New List(Of (Chk As CheckBox, Mask As UInteger))
    Private _managedFlagMask As UInteger

    ''' <summary>True while NumLevel is in "Level Mult" mode (PC Level Mult flag 0x80 set) — the u16 is a mult
    ''' shown as raw/1000 with 3 decimals. False = fixed integer Level. See NPC_AcbsData.LevelOrLevelMult (+6).</summary>
    Private _levelIsMult As Boolean

    ' Open-time snapshots for per-category change detection (drives which template categories are materialized).
    Private _snapFlags As UInteger
    Private _snapFull As String = ""
    Private _snapShort As String = ""
    Private _snapRace As UInteger, _snapVoice As UInteger, _snapClass As UInteger, _snapZnam As UInteger
    Private _snapLevel As UShort, _snapCalcMin As UShort, _snapCalcMax As UShort
    Private _snapDisp As Short
    Private _snapXp As Short
    Private _snapKeywords As New List(Of UInteger)
    Private _snapAppr As New List(Of UInteger)
    Private _snapFactions As New List(Of NPC_FactionEntry)
    Private _snapInventory As New List(Of NPC_InventoryItem)
    ' Outfits (DOFT/SOFT) — raw record value (for the "reverted to record default → clear override" decision)
    ' and the effective (overlay-aware) value shown at open (the snapshot baseline for change detection).
    Private _rawDefaultOutfit As UInteger, _rawSleepOutfit As UInteger
    Private _snapDefaultOutfit As UInteger, _snapSleepOutfit As UInteger
    Private _snapPerks As New List(Of NPC_PerkEntry)
    Private _snapActorEffects As New List(Of UInteger)
    Private _snapProperties As New List(Of NPC_PropertyEntry)
    Private _snapCombosSig As String = ""

    ''' <summary>True after an OK that actually changed something — the launcher marks the NPC dirty only then.</summary>
    Public ReadOnly Property HasChanges As Boolean
        Get
            Return _hasChanges
        End Get
    End Property
    Private _hasChanges As Boolean

    ''' <param name="mainForm">Owner — supplies the PluginManager for the FormID pickers + display names.</param>
    ''' <param name="npc">The live NPC_Data (render cache). Read at open; mutated only on OK.</param>
    ''' <param name="npcFormID">The NPC's global FormID.</param>
    ''' <param name="raceFormID">The NPC's current race (pre-fills the Race row when the NPC has none).</param>
    ''' <param name="isFemale">The NPC's current gender (informational).</param>
    ''' <param name="getParsedNpc">Resolver FormID → parsed NPC_Data, for the template-materializer chain walk.</param>
    Public Sub New(mainForm As MainForm, npc As NPC_Data, npcFormID As UInteger, raceFormID As UInteger,
                   isFemale As Boolean, getParsedNpc As Func(Of UInteger, NPC_Data))
        InitializeComponent()
        _mainForm = mainForm
        _npc = npc
        _npcFormID = npcFormID
        _getParsedNpc = getParsedNpc

        BuildFlagChecks()
        BuildCombosGridColumns()
        BuildFactionsGridColumns()
        BuildInventoryGridColumns()
        BuildPerksGridColumns()
        BuildPropertiesGridColumns()

        ' General tab — identity FormID pickers.
        AddHandler ButtonPickRace.Click, Sub() PickFidInto(TextBoxRace, {"RACE"}, "Select Race (RNAM)", allowNull:=False)
        AddHandler ButtonPickVoice.Click, Sub() PickFidInto(TextBoxVoice, {"VTYP"}, "Select Voice (VTCK)", allowNull:=True)
        AddHandler ButtonPickClass.Click, Sub() PickFidInto(TextBoxClass, {"CLAS"}, "Select Class (CNAM)", allowNull:=True)
        AddHandler ButtonPickZnam.Click, Sub() PickFidInto(TextBoxZnam, {"CSTY"}, "Select Combat Style (ZNAM)", allowNull:=True)
        ' Level is a UNION gated by the PC Level Mult flag (ACBS +6): ON = Level Mult (raw/1000), OFF = fixed
        ' Level. Toggling the flag live re-interprets the field while preserving the underlying raw u16.
        AddHandler ChkPCLevelMult.CheckedChanged, AddressOf OnPcLevelMultToggled

        ' Object Template tab — read-only grid; every mutation goes through a button / the double-click modal.
        AddHandler ButtonAddCombo.Click, AddressOf OnAddCombo
        AddHandler ButtonDupCombo.Click, AddressOf OnDuplicateCombo
        AddHandler ButtonRemoveCombo.Click, AddressOf OnRemoveCombo
        AddHandler ButtonComboUp.Click, Sub() MoveCombo(-1)
        AddHandler ButtonComboDown.Click, Sub() MoveCombo(1)
        AddHandler ButtonEditCombo.Click, AddressOf OnEditCombo
        AddHandler GridCombos.CellDoubleClick, AddressOf OnComboDoubleClick

        ' Keywords tab.
        AddHandler ButtonAddKeyword.Click, AddressOf OnAddKeyword
        AddHandler ButtonRemoveKeyword.Click, AddressOf OnRemoveKeyword

        ' Attach Parent Slots (APPR) tab.
        AddHandler ButtonAddAppr.Click, AddressOf OnAddAppr
        AddHandler ButtonRemoveAppr.Click, AddressOf OnRemoveAppr

        ' Factions tab.
        AddHandler ButtonAddFaction.Click, AddressOf OnAddFaction
        AddHandler ButtonEditFaction.Click, AddressOf OnEditFaction
        AddHandler ButtonRemoveFaction.Click, AddressOf OnRemoveFaction
        AddHandler GridFactions.CellDoubleClick, AddressOf OnFactionDoubleClick

        ' Inventory tab.
        AddHandler ButtonAddItem.Click, AddressOf OnAddItem
        AddHandler ButtonEditItem.Click, AddressOf OnEditItem
        AddHandler ButtonRemoveItem.Click, AddressOf OnRemoveItem
        AddHandler GridInventory.CellDoubleClick, AddressOf OnItemDoubleClick
        ' Default (DOFT) + Sleep (SOFT) outfit pickers. allowNull → "(none)" = 0 = no outfit.
        AddHandler ButtonPickDefaultOutfit.Click, Sub() PickFidInto(TextBoxDefaultOutfit, {"OTFT"}, "Select Default Outfit (DOFT)", allowNull:=True)
        AddHandler ButtonPickSleepOutfit.Click, Sub() PickFidInto(TextBoxSleepOutfit, {"OTFT"}, "Select Sleep Outfit (SOFT)", allowNull:=True)

        ' Perks tab.
        AddHandler ButtonAddPerk.Click, AddressOf OnAddPerk
        AddHandler ButtonEditPerk.Click, AddressOf OnEditPerk
        AddHandler ButtonRemovePerk.Click, AddressOf OnRemovePerk
        AddHandler GridPerks.CellDoubleClick, AddressOf OnPerkDoubleClick

        ' Actor Effects (SPLO) tab.
        AddHandler ButtonAddSpell.Click, AddressOf OnAddSpell
        AddHandler ButtonRemoveSpell.Click, AddressOf OnRemoveSpell

        ' Properties (PRPS) tab.
        AddHandler ButtonAddProp.Click, AddressOf OnAddProp
        AddHandler ButtonEditProp.Click, AddressOf OnEditProp
        AddHandler ButtonRemoveProp.Click, AddressOf OnRemoveProp
        AddHandler GridProps.CellDoubleClick, AddressOf OnPropDoubleClick

        ' Bottom.
        AddHandler ButtonOk.Click, AddressOf OnOk
        AddHandler ButtonCancel.Click, AddressOf OnCancel

        ApplyGameGating()
        LoadNpcIntoPanels(raceFormID)
        SnapshotOpenState()
    End Sub

    ''' <summary>Hide the NPC_ subrecords that exist only in Fallout 4 when editing a Skyrim NPC, so the user
    ''' can't author subrecords the Skyrim engine/record has no slot for (which the FO4-shaped writer would emit
    ''' into a Skyrim NPC): OBTS/OBTE object-template combinations, PRPS properties, and APPR attach-parent slots.
    ''' Skyrim NPCs never carry these, so removing the tabs/section is loss-free (untouched categories round-trip
    ''' verbatim regardless). FO4 keeps everything.</summary>
    Private Sub ApplyGameGating()
        If Config_App.Current.Game <> Config_App.Game_Enum.Skyrim Then Return
        If Tabs.TabPages.Contains(TabObts) Then Tabs.TabPages.Remove(TabObts)
        If Tabs.TabPages.Contains(TabProps) Then Tabs.TabPages.Remove(TabProps)
        For Each c As Control In New Control() {LabelAppr, ListAppr, ButtonAddAppr, ButtonRemoveAppr}
            If c IsNot Nothing Then c.Visible = False
        Next
        ' 0x800000 = "No Activation / Hellos" in FO4 but an unused/unknown bit in Skyrim (see BuildFlagChecks):
        ' hide the checkbox; its bit is already excluded from _managedFlagMask on Skyrim so it round-trips verbatim.
        If ChkNoActHellos IsNot Nothing Then ChkNoActHellos.Visible = False
    End Sub

    ' =====================================================================
    ' One-time UI construction (Designer rule: variable/typed columns in code)
    ' =====================================================================

    Private Sub BuildFlagChecks()
        ' ACBS Flags bit values. VERIFIED identical between FO4 (wbDefinitionsFO4) and Skyrim (wbDefinitionsTES5,
        ' the ACBS 'Flags' wbFlags block) for EVERY surfaced flag EXCEPT bit 0x800000: FO4 = "No Activation /
        ' Hellos", Skyrim = "Unknown 23" (unused). So ChkNoActHellos is added ONLY on FO4 — on Skyrim its bit stays
        ' OUT of _managedFlagMask and is preserved verbatim by ComposeFlags (the checkbox is hidden in
        ' ApplyGameGating). Note: ACBS bit 0x04 (Is CharGen Face Preset) is intentionally NOT surfaced here (both
        ' games) — owned by the Face editor, preserved verbatim.
        Dim isSkyrim = (Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
        _flagChecks.Add((ChkFemale, &H1UI))
        _flagChecks.Add((ChkEssential, &H2UI))
        _flagChecks.Add((ChkRespawn, &H8UI))
        _flagChecks.Add((ChkAutoCalc, &H10UI))
        _flagChecks.Add((ChkUnique, &H20UI))
        _flagChecks.Add((ChkNoStealth, &H40UI))
        _flagChecks.Add((ChkPCLevelMult, &H80UI))
        _flagChecks.Add((ChkProtected, &H800UI))
        _flagChecks.Add((ChkSummonable, &H4000UI))
        _flagChecks.Add((ChkDoesntBleed, &H10000UI))
        _flagChecks.Add((ChkOppositeGender, &H80000UI))
        _flagChecks.Add((ChkSimpleActor, &H100000UI))
        If Not isSkyrim Then _flagChecks.Add((ChkNoActHellos, &H800000UI))
        _flagChecks.Add((ChkGhost, &H20000000UI))
        _flagChecks.Add((ChkInvulnerable, &H80000000UI))
        _managedFlagMask = 0UI
        For Each fc In _flagChecks
            _managedFlagMask = _managedFlagMask Or fc.Mask
        Next
    End Sub

    Private Sub BuildCombosGridColumns()
        GridCombos.AutoGenerateColumns = False
        GridCombos.Columns.Clear()
        GridCombos.Columns.Add(NewReadOnlyCol("#", 6))
        GridCombos.Columns.Add(NewReadOnlyCol("Name", 34))
        GridCombos.Columns.Add(NewReadOnlyCol("Default", 12))
        GridCombos.Columns.Add(NewReadOnlyCol("EditorOnly", 12))
        GridCombos.Columns.Add(NewReadOnlyCol("#Incl", 9))
        GridCombos.Columns.Add(NewReadOnlyCol("#Props", 9))
        GridCombos.Columns.Add(NewReadOnlyCol("#Kwds", 9))
        GridCombos.Columns.Add(NewReadOnlyCol("Raw", 9))
    End Sub

    Private Sub BuildFactionsGridColumns()
        GridFactions.AutoGenerateColumns = False
        GridFactions.Columns.Clear()
        GridFactions.Columns.Add(NewReadOnlyCol("Faction [SNAM]", 78))
        GridFactions.Columns.Add(NewReadOnlyCol("Rank", 22))
    End Sub

    Private Sub BuildInventoryGridColumns()
        GridInventory.AutoGenerateColumns = False
        GridInventory.Columns.Clear()
        GridInventory.Columns.Add(NewReadOnlyCol("Item [CNTO]", 78))
        GridInventory.Columns.Add(NewReadOnlyCol("Count", 22))
    End Sub

    Private Sub BuildPerksGridColumns()
        GridPerks.AutoGenerateColumns = False
        GridPerks.Columns.Clear()
        GridPerks.Columns.Add(NewReadOnlyCol("Perk [PRKR]", 78))
        GridPerks.Columns.Add(NewReadOnlyCol("Rank", 22))
    End Sub

    Private Sub BuildPropertiesGridColumns()
        GridProps.AutoGenerateColumns = False
        GridProps.Columns.Clear()
        GridProps.Columns.Add(NewReadOnlyCol("Actor Value [PRPS]", 78))
        GridProps.Columns.Add(NewReadOnlyCol("Value", 22))
    End Sub

    Private Shared Function NewReadOnlyCol(header As String, weight As Single) As DataGridViewTextBoxColumn
        Return New DataGridViewTextBoxColumn With {
            .HeaderText = header, .FillWeight = weight, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, .ReadOnly = True}
    End Function

    ' =====================================================================
    ' NPC → panels
    ' =====================================================================

    Private Sub LoadNpcIntoPanels(fallbackRaceFormID As UInteger)
        _loading = True
        Try
            ' General.
            TextBoxFull.Text = If(_npc.FullName, "")
            TextBoxShort.Text = If(_npc.ShortName, "")
            Dim raceFid = If(_npc.RaceFormID <> 0UI, _npc.RaceFormID, fallbackRaceFormID)
            SetFidText(TextBoxRace, raceFid)
            SetFidText(TextBoxVoice, _npc.VoiceFormID)
            SetFidText(TextBoxClass, _npc.ClassFormID)
            SetFidText(TextBoxZnam, _npc.CombatStyleFormID)

            Dim acbs = _npc.Acbs
            Dim flagsWord As UInteger = If(acbs IsNot Nothing, acbs.Flags, _npc.AcbsFlags)
            SetFlagChecks(flagsWord)   ' fires ChkPCLevelMult.CheckedChanged, guarded by _loading (no-op here)
            ' Level union — mode from the PC Level Mult flag (0x80), value from the raw u16.
            ConfigureLevelControl((flagsWord And &H80UI) <> 0UI, If(acbs IsNot Nothing, acbs.LevelOrLevelMult, 0US))
            NumXp.Value = ClampDec(CDec(If(acbs IsNot Nothing, acbs.XpValueOffset, 0S)), NumXp)
            NumCalcMin.Value = ClampDec(CDec(If(acbs IsNot Nothing, acbs.CalcMinLevel, 0US)), NumCalcMin)
            NumCalcMax.Value = ClampDec(CDec(If(acbs IsNot Nothing, acbs.CalcMaxLevel, 0US)), NumCalcMax)
            NumDisp.Value = ClampDec(CDec(If(acbs IsNot Nothing, acbs.DispositionBase, 0S)), NumDisp)

            ' Object Template — deep-copy the wrapper list into the working buffer (never alias the parse).
            _combos.Clear()
            _combos.AddRange(CloneCombos(_npc.ObjectTemplateCombinations))
            RefreshCombosGrid()

            ' Keywords.
            _keywords.Clear()
            _keywords.AddRange(_npc.KeywordFormIDs)
            RefreshKeywordsList()

            ' Attach Parent Slots (APPR).
            _appr.Clear()
            _appr.AddRange(_npc.AttachParentSlotFormIDs)
            RefreshApprList()

            ' Factions (deep-copy).
            _factions.Clear()
            For Each f In _npc.Factions
                _factions.Add(New NPC_FactionEntry With {.FactionFormID = f.FactionFormID, .Rank = f.Rank})
            Next
            RefreshFactionsGrid()

            ' Inventory (deep-copy — carries the COED block).
            _inventory.Clear()
            For Each it In _npc.Inventory
                _inventory.Add(CloneInventoryItem(it))
            Next
            RefreshInventoryGrid()

            ' Outfits (DOFT/SOFT). Seed with the EFFECTIVE (overlay-aware) values: a prior Edit Outfit pick
            ' lives in the LooksMenu overlay, not on _npc, so show the overlaid value. Keep the raw record value
            ' too so OnOk can map "reverted to the record's own outfit" back to a cleared override.
            _rawDefaultOutfit = _npc.DefaultOutfitFormID
            _rawSleepOutfit = _npc.SleepOutfitFormID
            Dim effDefaultOutfit = _rawDefaultOutfit, effSleepOutfit = _rawSleepOutfit
            _mainForm.GetEffectiveNpcOutfitsForEditor(_npcFormID, _rawDefaultOutfit, _rawSleepOutfit, effDefaultOutfit, effSleepOutfit)
            SetFidText(TextBoxDefaultOutfit, effDefaultOutfit)
            SetFidText(TextBoxSleepOutfit, effSleepOutfit)

            ' Perks (deep-copy).
            _perks.Clear()
            For Each p In _npc.Perks
                _perks.Add(New NPC_PerkEntry With {.PerkFormID = p.PerkFormID, .Rank = p.Rank})
            Next
            RefreshPerksGrid()

            ' Actor Effects (SPLO).
            _actorEffects.Clear()
            _actorEffects.AddRange(_npc.ActorEffectFormIDs)
            RefreshSpellList()

            ' Properties (PRPS) (deep-copy).
            _properties.Clear()
            For Each p In _npc.Properties
                _properties.Add(New NPC_PropertyEntry With {.ActorValueFormID = p.ActorValueFormID, .Value = p.Value})
            Next
            RefreshPropsGrid()
        Finally
            _loading = False
        End Try
    End Sub

    Private Sub SetFlagChecks(word As UInteger)
        For Each fc In _flagChecks
            fc.Chk.Checked = (word And fc.Mask) <> 0UI
        Next
    End Sub

    ' =====================================================================
    ' Level / Level Mult union (ACBS +6, gated by PC Level Mult flag 0x80)
    ' =====================================================================

    ''' <summary>PC Level Mult flag toggled by the user → switch the Level field's mode live, preserving the
    ''' underlying raw u16 (so re-reading it in the new mode is byte-consistent). Suppressed during load.</summary>
    Private Sub OnPcLevelMultToggled(sender As Object, e As EventArgs)
        If _loading Then Return
        ConfigureLevelControl(ChkPCLevelMult.Checked, CurrentLevelRaw())
    End Sub

    ''' <summary>Configure NumLevel for the given mode from a raw u16. Mult (PC Level Mult ON): "Level Mult",
    ''' 3 decimals, value = raw/1000 (xEdit shows the mult as value/1000). Fixed (OFF): integer "Level".</summary>
    Private Sub ConfigureLevelControl(mult As Boolean, raw As UShort)
        _levelIsMult = mult
        If mult Then
            LabelLevel.Text = "Level Mult (ACBS):"
            NumLevel.DecimalPlaces = 3
            NumLevel.Increment = 0.01D
            NumLevel.Minimum = 0D
            NumLevel.Maximum = 65.535D
            NumLevel.Value = ClampDec(CDec(raw) / 1000D, NumLevel)
        Else
            LabelLevel.Text = "Level (ACBS):"
            NumLevel.DecimalPlaces = 0
            NumLevel.Increment = 1D
            NumLevel.Minimum = 0D
            NumLevel.Maximum = 65535D
            NumLevel.Value = ClampDec(CDec(raw), NumLevel)
        End If
    End Sub

    ''' <summary>The raw u16 the Level field currently represents (mult mode → round(value*1000); fixed → value).
    ''' This is what gets stored/compared/written — the union bytes, not the display value.</summary>
    Private Function CurrentLevelRaw() As UShort
        If _levelIsMult Then
            Return CUShort(Math.Round(NumLevel.Value * 1000D))
        Else
            Return CUShort(NumLevel.Value)
        End If
    End Function

    ''' <summary>Compose the ACBS flags word from the checkboxes, preserving any bit that is NOT surfaced as a
    ''' checkbox (start from the open-time word, clear the managed bits, then OR in the checked ones).</summary>
    Private Function ComposeFlags() As UInteger
        Dim word As UInteger = _snapFlags And Not _managedFlagMask
        For Each fc In _flagChecks
            If fc.Chk.Checked Then word = word Or fc.Mask
        Next
        Return word
    End Function

    Private Sub SnapshotOpenState()
        _snapFlags = ReadCurrentFlagsForSnapshot()
        _snapFull = TextBoxFull.Text.Trim()
        _snapShort = TextBoxShort.Text.Trim()
        _snapRace = GetFid(TextBoxRace)
        _snapVoice = GetFid(TextBoxVoice)
        _snapClass = GetFid(TextBoxClass)
        _snapZnam = GetFid(TextBoxZnam)
        _snapLevel = CurrentLevelRaw()
        _snapXp = CShort(NumXp.Value)
        _snapCalcMin = CUShort(NumCalcMin.Value)
        _snapCalcMax = CUShort(NumCalcMax.Value)
        _snapDisp = CShort(NumDisp.Value)
        _snapKeywords = New List(Of UInteger)(_keywords)
        _snapAppr = New List(Of UInteger)(_appr)
        _snapFactions = _factions.Select(Function(f) New NPC_FactionEntry With {.FactionFormID = f.FactionFormID, .Rank = f.Rank}).ToList()
        _snapInventory = _inventory.Select(AddressOf CloneInventoryItem).ToList()
        _snapDefaultOutfit = GetFid(TextBoxDefaultOutfit)
        _snapSleepOutfit = GetFid(TextBoxSleepOutfit)
        _snapPerks = _perks.Select(Function(p) New NPC_PerkEntry With {.PerkFormID = p.PerkFormID, .Rank = p.Rank}).ToList()
        _snapActorEffects = New List(Of UInteger)(_actorEffects)
        _snapProperties = _properties.Select(Function(p) New NPC_PropertyEntry With {.ActorValueFormID = p.ActorValueFormID, .Value = p.Value}).ToList()
        _snapCombosSig = CombosSignature(_combos)
    End Sub

    ''' <summary>The flags word as it stands in the checkboxes right after load — the snapshot baseline. Read
    ''' from the raw NPC word (not <see cref="ComposeFlags"/>, which itself depends on _snapFlags).</summary>
    Private Function ReadCurrentFlagsForSnapshot() As UInteger
        Dim word As UInteger = If(_npc.Acbs IsNot Nothing, _npc.Acbs.Flags, _npc.AcbsFlags)
        Return word
    End Function

    ' =====================================================================
    ' Object Template (OBTS) combinations
    ' =====================================================================

    Private Sub RefreshCombosGrid()
        Dim selIdx = If(GridCombos.CurrentRow IsNot Nothing, GridCombos.CurrentRow.Index, -1)
        GridCombos.Rows.Clear()
        For i = 0 To _combos.Count - 1
            Dim w = _combos(i)
            Dim c = w.Combination
            Dim name = If(Not String.IsNullOrEmpty(w.DisplayName), w.DisplayName,
                          If(c IsNot Nothing AndAlso Not String.IsNullOrEmpty(c.DisplayName), c.DisplayName, "(unnamed)"))
            GridCombos.Rows.Add((i + 1).ToString(CultureInfo.InvariantCulture),
                                name,
                                If(c IsNot Nothing AndAlso c.IsDefault, "Yes", ""),
                                If(w.IsEditorOnly, "Yes", ""),
                                If(c IsNot Nothing, c.Includes.Count, 0).ToString(CultureInfo.InvariantCulture),
                                If(c IsNot Nothing, c.Properties.Count, 0).ToString(CultureInfo.InvariantCulture),
                                If(c IsNot Nothing, c.Keywords.Count, 0).ToString(CultureInfo.InvariantCulture),
                                If(c Is Nothing, "raw", ""))
        Next
        SelectGridRow(GridCombos, selIdx)
    End Sub

    Private Function SelectedComboIndex() As Integer
        If GridCombos.CurrentRow Is Nothing Then Return -1
        Dim i = GridCombos.CurrentRow.Index
        If i < 0 OrElse i >= _combos.Count Then Return -1
        Return i
    End Function

    Private Sub OnAddCombo(sender As Object, e As EventArgs)
        Using dlg As New ObtsCombinationEditor_Form(_mainForm, New ARMO_Combination())
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultCombination IsNot Nothing Then
                _combos.Add(WrapCombination(dlg.ResultCombination))
                RefreshCombosGrid()
            End If
        End Using
    End Sub

    Private Sub OnDuplicateCombo(sender As Object, e As EventArgs)
        Dim i = SelectedComboIndex()
        If i < 0 Then Return
        _combos.Insert(i + 1, CloneCombo(_combos(i)))
        RefreshCombosGrid()
    End Sub

    Private Sub OnRemoveCombo(sender As Object, e As EventArgs)
        Dim i = SelectedComboIndex()
        If i < 0 Then Return
        _combos.RemoveAt(i)
        RefreshCombosGrid()
    End Sub

    Private Sub MoveCombo(delta As Integer)
        Dim i = SelectedComboIndex()
        If i < 0 Then Return
        Dim j = i + delta
        If j < 0 OrElse j >= _combos.Count Then Return
        Dim tmp = _combos(i)
        _combos(i) = _combos(j)
        _combos(j) = tmp
        RefreshCombosGrid()
        SelectGridRow(GridCombos, j)
    End Sub

    Private Sub OnEditCombo(sender As Object, e As EventArgs)
        EditComboAt(SelectedComboIndex())
    End Sub

    Private Sub OnComboDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        EditComboAt(e.RowIndex)
    End Sub

    ''' <summary>Edit the wrapper's inner <see cref="ARMO_Combination"/> in the shared modal sub-editor. The
    ''' wrapper-level DisplayName / IsEditorOnly are synced from the sub-editor's result, and the preserved raw
    ''' OBTS bytes are dropped (the combination was structurally edited, so the bytes no longer match).</summary>
    Private Sub EditComboAt(i As Integer)
        If i < 0 OrElse i >= _combos.Count Then Return
        Dim w = _combos(i)
        Dim seed = If(w.Combination, New ARMO_Combination())
        If String.IsNullOrEmpty(seed.DisplayName) Then seed.DisplayName = w.DisplayName
        If w.IsEditorOnly Then seed.IsEditorOnly = True
        Using dlg As New ObtsCombinationEditor_Form(_mainForm, seed)
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultCombination IsNot Nothing Then
                _combos(i) = WrapCombination(dlg.ResultCombination)
                RefreshCombosGrid()
                SelectGridRow(GridCombos, i)
            End If
        End Using
    End Sub

    ''' <summary>Wrap a freshly-edited ARMO_Combination into an NPC OBTS wrapper (mirrors the CK's OBTF/FULL/OBTS
    ''' sequence). RawObtsBytes = Nothing: the wrapper is now authored from the structured combination.</summary>
    Private Shared Function WrapCombination(c As ARMO_Combination) As NPC_ObjectTemplateCombination
        Return New NPC_ObjectTemplateCombination With {
            .Combination = c, .DisplayName = If(c IsNot Nothing, c.DisplayName, ""),
            .IsEditorOnly = c IsNot Nothing AndAlso c.IsEditorOnly, .RawObtsBytes = Nothing}
    End Function

    ' =====================================================================
    ' Keywords
    ' =====================================================================

    ''' <summary>Add a general keyword. KWDA EXCLUDES attach-point keywords (those belong in APPR) — the reverse
    ''' of the APPR tab — by KYWD.TNAM type, not a name heuristic; "Show all" escapes the filter.</summary>
    Private Sub OnAddKeyword(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"KYWD"},
                                           "Add keyword (KWDA)", 0UI, allowNull:=False,
                                           formIdFilter:=Function(fid) Not _mainForm.IsAttachPointKeyword(fid))
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            If Not _keywords.Contains(dlg.SelectedFormID) Then _keywords.Add(dlg.SelectedFormID)
            RefreshKeywordsList()
        End Using
    End Sub

    Private Sub OnRemoveKeyword(sender As Object, e As EventArgs)
        If ListKeywords.SelectedItems.Count = 0 Then Return
        Dim fid = CUInt(ListKeywords.SelectedItems(0).Tag)
        _keywords.Remove(fid)
        RefreshKeywordsList()
    End Sub

    Private Sub RefreshKeywordsList()
        RefreshFidList(ListKeywords, _keywords)
    End Sub

    ' =====================================================================
    ' Attach Parent Slots (APPR — attach-point KYWDs, the reverse filter of Keywords)
    ' =====================================================================

    ''' <summary>Add an attach-parent-slot keyword. The picker shows ONLY attach-point KYWDs (authoritative
    ''' KYWD.TNAM Type == 'Attach Point', via <see cref="MainForm.IsAttachPointKeyword"/>) — the reverse of the
    ''' Keywords tab, which EXCLUDES them. "Show all" escapes the filter.</summary>
    Private Sub OnAddAppr(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"KYWD"},
                                           "Add attach-parent-slot (APPR)", 0UI, allowNull:=False,
                                           formIdFilter:=AddressOf _mainForm.IsAttachPointKeyword)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            If Not _appr.Contains(dlg.SelectedFormID) Then _appr.Add(dlg.SelectedFormID)
            RefreshApprList()
        End Using
    End Sub

    Private Sub OnRemoveAppr(sender As Object, e As EventArgs)
        If ListAppr.SelectedItems.Count = 0 Then Return
        Dim fid = CUInt(ListAppr.SelectedItems(0).Tag)
        _appr.Remove(fid)
        RefreshApprList()
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
    ' Factions
    ' =====================================================================

    Private Sub RefreshFactionsGrid()
        Dim selIdx = If(GridFactions.CurrentRow IsNot Nothing, GridFactions.CurrentRow.Index, -1)
        GridFactions.Rows.Clear()
        For Each f In _factions
            GridFactions.Rows.Add($"{DisplayFor(f.FactionFormID)} [0x{f.FactionFormID:X8}]",
                                  f.Rank.ToString(CultureInfo.InvariantCulture))
        Next
        SelectGridRow(GridFactions, selIdx)
    End Sub

    Private Function SelectedFactionIndex() As Integer
        If GridFactions.CurrentRow Is Nothing Then Return -1
        Dim i = GridFactions.CurrentRow.Index
        If i < 0 OrElse i >= _factions.Count Then Return -1
        Return i
    End Function

    Private Sub OnAddFaction(sender As Object, e As EventArgs)
        Using dlg As New NpcFactionEntryEditor_Form(_mainForm, New NPC_FactionEntry())
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultEntry IsNot Nothing Then
                _factions.Add(dlg.ResultEntry)
                RefreshFactionsGrid()
            End If
        End Using
    End Sub

    Private Sub OnEditFaction(sender As Object, e As EventArgs)
        EditFactionAt(SelectedFactionIndex())
    End Sub

    Private Sub OnFactionDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        EditFactionAt(e.RowIndex)
    End Sub

    Private Sub EditFactionAt(i As Integer)
        If i < 0 OrElse i >= _factions.Count Then Return
        Using dlg As New NpcFactionEntryEditor_Form(_mainForm, _factions(i))
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultEntry IsNot Nothing Then
                _factions(i) = dlg.ResultEntry
                RefreshFactionsGrid()
                SelectGridRow(GridFactions, i)
            End If
        End Using
    End Sub

    Private Sub OnRemoveFaction(sender As Object, e As EventArgs)
        Dim i = SelectedFactionIndex()
        If i < 0 Then Return
        _factions.RemoveAt(i)
        RefreshFactionsGrid()
    End Sub

    ' =====================================================================
    ' Inventory
    ' =====================================================================

    Private Sub RefreshInventoryGrid()
        Dim selIdx = If(GridInventory.CurrentRow IsNot Nothing, GridInventory.CurrentRow.Index, -1)
        GridInventory.Rows.Clear()
        For Each it In _inventory
            GridInventory.Rows.Add($"{DisplayFor(it.ItemFormID)} [0x{it.ItemFormID:X8}]",
                                   it.Count.ToString(CultureInfo.InvariantCulture))
        Next
        SelectGridRow(GridInventory, selIdx)
    End Sub

    Private Function SelectedItemIndex() As Integer
        If GridInventory.CurrentRow Is Nothing Then Return -1
        Dim i = GridInventory.CurrentRow.Index
        If i < 0 OrElse i >= _inventory.Count Then Return -1
        Return i
    End Function

    Private Sub OnAddItem(sender As Object, e As EventArgs)
        Using dlg As New NpcInventoryEntryEditor_Form(_mainForm, New NPC_InventoryItem())
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultEntry IsNot Nothing Then
                _inventory.Add(dlg.ResultEntry)
                RefreshInventoryGrid()
            End If
        End Using
    End Sub

    Private Sub OnEditItem(sender As Object, e As EventArgs)
        EditItemAt(SelectedItemIndex())
    End Sub

    Private Sub OnItemDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        EditItemAt(e.RowIndex)
    End Sub

    Private Sub EditItemAt(i As Integer)
        If i < 0 OrElse i >= _inventory.Count Then Return
        Using dlg As New NpcInventoryEntryEditor_Form(_mainForm, _inventory(i))
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultEntry IsNot Nothing Then
                _inventory(i) = dlg.ResultEntry
                RefreshInventoryGrid()
                SelectGridRow(GridInventory, i)
            End If
        End Using
    End Sub

    Private Sub OnRemoveItem(sender As Object, e As EventArgs)
        Dim i = SelectedItemIndex()
        If i < 0 Then Return
        _inventory.RemoveAt(i)
        RefreshInventoryGrid()
    End Sub

    ' =====================================================================
    ' Perks (PRKR — PERK FormID + u8 Rank)
    ' =====================================================================

    Private Sub RefreshPerksGrid()
        Dim selIdx = If(GridPerks.CurrentRow IsNot Nothing, GridPerks.CurrentRow.Index, -1)
        GridPerks.Rows.Clear()
        For Each p In _perks
            GridPerks.Rows.Add($"{DisplayFor(p.PerkFormID)} [0x{p.PerkFormID:X8}]",
                               p.Rank.ToString(CultureInfo.InvariantCulture))
        Next
        SelectGridRow(GridPerks, selIdx)
    End Sub

    Private Function SelectedPerkIndex() As Integer
        If GridPerks.CurrentRow Is Nothing Then Return -1
        Dim i = GridPerks.CurrentRow.Index
        If i < 0 OrElse i >= _perks.Count Then Return -1
        Return i
    End Function

    Private Sub OnAddPerk(sender As Object, e As EventArgs)
        Using dlg As New NpcPerkEntryEditor_Form(_mainForm, New NPC_PerkEntry())
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultEntry IsNot Nothing Then
                _perks.Add(dlg.ResultEntry)
                RefreshPerksGrid()
            End If
        End Using
    End Sub

    Private Sub OnEditPerk(sender As Object, e As EventArgs)
        EditPerkAt(SelectedPerkIndex())
    End Sub

    Private Sub OnPerkDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        EditPerkAt(e.RowIndex)
    End Sub

    Private Sub EditPerkAt(i As Integer)
        If i < 0 OrElse i >= _perks.Count Then Return
        Using dlg As New NpcPerkEntryEditor_Form(_mainForm, _perks(i))
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultEntry IsNot Nothing Then
                _perks(i) = dlg.ResultEntry
                RefreshPerksGrid()
                SelectGridRow(GridPerks, i)
            End If
        End Using
    End Sub

    Private Sub OnRemovePerk(sender As Object, e As EventArgs)
        Dim i = SelectedPerkIndex()
        If i < 0 Then Return
        _perks.RemoveAt(i)
        RefreshPerksGrid()
    End Sub

    ' =====================================================================
    ' Actor Effects (SPLO → SPEL) — flat FormID list, mirror of Keywords
    ' =====================================================================

    Private Sub OnAddSpell(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"SPEL"},
                                           "Add actor effect (SPLO)", 0UI, allowNull:=False)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            If Not _actorEffects.Contains(dlg.SelectedFormID) Then _actorEffects.Add(dlg.SelectedFormID)
            RefreshSpellList()
        End Using
    End Sub

    Private Sub OnRemoveSpell(sender As Object, e As EventArgs)
        If ListSpells.SelectedItems.Count = 0 Then Return
        Dim fid = CUInt(ListSpells.SelectedItems(0).Tag)
        _actorEffects.Remove(fid)
        RefreshSpellList()
    End Sub

    Private Sub RefreshSpellList()
        RefreshFidList(ListSpells, _actorEffects)
    End Sub

    ' =====================================================================
    ' Properties (PRPS — AVIF FormID + f32 Value)
    ' =====================================================================

    Private Sub RefreshPropsGrid()
        Dim selIdx = If(GridProps.CurrentRow IsNot Nothing, GridProps.CurrentRow.Index, -1)
        GridProps.Rows.Clear()
        For Each p In _properties
            GridProps.Rows.Add($"{DisplayFor(p.ActorValueFormID)} [0x{p.ActorValueFormID:X8}]",
                               p.Value.ToString(CultureInfo.InvariantCulture))
        Next
        SelectGridRow(GridProps, selIdx)
    End Sub

    Private Function SelectedPropIndex() As Integer
        If GridProps.CurrentRow Is Nothing Then Return -1
        Dim i = GridProps.CurrentRow.Index
        If i < 0 OrElse i >= _properties.Count Then Return -1
        Return i
    End Function

    Private Sub OnAddProp(sender As Object, e As EventArgs)
        Using dlg As New NpcPropertyEntryEditor_Form(_mainForm, New NPC_PropertyEntry())
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultEntry IsNot Nothing Then
                _properties.Add(dlg.ResultEntry)
                RefreshPropsGrid()
            End If
        End Using
    End Sub

    Private Sub OnEditProp(sender As Object, e As EventArgs)
        EditPropAt(SelectedPropIndex())
    End Sub

    Private Sub OnPropDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        EditPropAt(e.RowIndex)
    End Sub

    Private Sub EditPropAt(i As Integer)
        If i < 0 OrElse i >= _properties.Count Then Return
        Using dlg As New NpcPropertyEntryEditor_Form(_mainForm, _properties(i))
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultEntry IsNot Nothing Then
                _properties(i) = dlg.ResultEntry
                RefreshPropsGrid()
                SelectGridRow(GridProps, i)
            End If
        End Using
    End Sub

    Private Sub OnRemoveProp(sender As Object, e As EventArgs)
        Dim i = SelectedPropIndex()
        If i < 0 Then Return
        _properties.RemoveAt(i)
        RefreshPropsGrid()
    End Sub

    ' =====================================================================
    ' OK / Cancel
    ' =====================================================================

    Private Sub OnCancel(sender As Object, e As EventArgs)
        DialogResult = DialogResult.Cancel
        Close()
    End Sub

    ''' <summary>Commit the panels into the LIVE NPC_Data. For each data-category whose fields actually changed,
    ''' MATERIALIZE the template-inherited values and clear the Use-X flag FIRST (so the edit is authoritative),
    ''' then apply the edit on top. See the class summary for why this reaches only the in-memory NPC + preview.</summary>
    Private Sub OnOk(sender As Object, e As EventArgs)
        ' Detect which template categories were edited (drives the materialize → clear-flag → apply order).
        Dim newFlags = ComposeFlags()
        Dim baseDataChanged = (newFlags <> _snapFlags) OrElse
                              Not String.Equals(TextBoxFull.Text.Trim(), _snapFull, StringComparison.Ordinal) OrElse
                              Not String.Equals(TextBoxShort.Text.Trim(), _snapShort, StringComparison.Ordinal) OrElse
                              CurrentLevelRaw() <> _snapLevel OrElse CShort(NumXp.Value) <> _snapXp OrElse
                              CUShort(NumCalcMin.Value) <> _snapCalcMin OrElse
                              CUShort(NumCalcMax.Value) <> _snapCalcMax OrElse CShort(NumDisp.Value) <> _snapDisp
        Dim combosChanged = Not String.Equals(CombosSignature(_combos), _snapCombosSig, StringComparison.Ordinal)
        Dim traitsChanged = (GetFid(TextBoxRace) <> _snapRace) OrElse (GetFid(TextBoxVoice) <> _snapVoice) OrElse combosChanged
        Dim statsChanged = (GetFid(TextBoxClass) <> _snapClass) OrElse (GetFid(TextBoxZnam) <> _snapZnam)
        Dim keywordsChanged = Not SequenceEqualU(_keywords, _snapKeywords)
        Dim apprChanged = Not SequenceEqualU(_appr, _snapAppr)
        Dim factionsChanged = Not FactionsEqual(_factions, _snapFactions)
        Dim inventoryChanged = Not InventoryEqual(_inventory, _snapInventory)
        Dim perksChanged = Not PerksEqual(_perks, _snapPerks)
        Dim actorEffectsChanged = Not SequenceEqualU(_actorEffects, _snapActorEffects)
        Dim propertiesChanged = Not PropertiesEqual(_properties, _snapProperties)
        Dim defaultOutfitChanged = GetFid(TextBoxDefaultOutfit) <> _snapDefaultOutfit
        Dim sleepOutfitChanged = GetFid(TextBoxSleepOutfit) <> _snapSleepOutfit

        ' Persistence: record the edit as a MERGED NpcRecordOverride. MainForm's ApplyNpcRecordOverride delegate
        ' applies it at Save time AFTER CopyRoundTripOnlyFieldsFromRaw, so the edit wins over the fresh re-parse.
        RegisterRecordOverride(newFlags, baseDataChanged, traitsChanged, statsChanged,
                               keywordsChanged, apprChanged, factionsChanged, inventoryChanged, combosChanged,
                               perksChanged, actorEffectsChanged, propertiesChanged)

        ' Also mutate the LIVE in-memory NPC_Data (render cache) so the preview reflects the edit immediately.
        ' Materialize → clear Use-X flag for each edited category BEFORE applying (engine CopyFromTemplate rule).
        ' Traits uses the SAME skip-overlay-owned rule as the save apply so the preview matches the written record
        ' (overlay-owned appearance fields come from the LM overlay at render, not the template).
        If traitsChanged Then NpcTemplateMaterializer.MakeCategoryOwn(_npc, NPC_TemplateCategory.Traits, _getParsedNpc, skipOverlayOwned:=_mainForm.NpcHasOverlay(_npcFormID))
        If baseDataChanged Then NpcTemplateMaterializer.MakeCategoryOwn(_npc, NPC_TemplateCategory.BaseData, _getParsedNpc)
        If statsChanged Then NpcTemplateMaterializer.MakeCategoryOwn(_npc, NPC_TemplateCategory.Stats, _getParsedNpc)
        If keywordsChanged Then NpcTemplateMaterializer.MakeCategoryOwn(_npc, NPC_TemplateCategory.Keywords, _getParsedNpc)
        If factionsChanged Then NpcTemplateMaterializer.MakeCategoryOwn(_npc, NPC_TemplateCategory.Factions, _getParsedNpc)
        If inventoryChanged Then NpcTemplateMaterializer.MakeCategoryOwn(_npc, NPC_TemplateCategory.Inventory, _getParsedNpc)
        If actorEffectsChanged Then NpcTemplateMaterializer.MakeCategoryOwn(_npc, NPC_TemplateCategory.SpellList, _getParsedNpc)

        ApplyToNpc(newFlags)

        ' Outfits (DOFT/SOFT) are committed to the LooksMenu overlay — the SAME path the Edit Outfit picker uses —
        ' so the preview, the outfit combo (rebuilt by the caller's re-render) and Save all resolve them once.
        ' Only touch a field the user actually changed, so an untouched outfit never clobbers a prior pick. A value
        ' equal to the raw record outfit clears the override (preserve raw); any other value stores the override
        ' (0 = no outfit). ApplyToNpc does NOT write DOFT/SOFT onto _npc — the overlay owns them at render/save.
        If defaultOutfitChanged Then
            Dim v = GetFid(TextBoxDefaultOutfit)
            _mainForm.SetNpcDefaultOutfitOverrideFromEditor(_npcFormID, If(v = _rawDefaultOutfit, CType(Nothing, UInteger?), v))
        End If
        If sleepOutfitChanged Then
            Dim v = GetFid(TextBoxSleepOutfit)
            _mainForm.SetNpcSleepOutfitOverrideFromEditor(_npcFormID, If(v = _rawSleepOutfit, CType(Nothing, UInteger?), v))
        End If

        _hasChanges = baseDataChanged OrElse traitsChanged OrElse statsChanged OrElse
                      keywordsChanged OrElse apprChanged OrElse factionsChanged OrElse inventoryChanged OrElse
                      perksChanged OrElse actorEffectsChanged OrElse propertiesChanged OrElse
                      defaultOutfitChanged OrElse sleepOutfitChanged
        DialogResult = DialogResult.OK
        Close()
    End Sub

    ''' <summary>Build (or MERGE into) the NPC's <see cref="NpcRecordOverride"/> from the panel state, setting
    ''' only the fields that actually changed vs the open-time snapshot. Merging into any existing override lets
    ''' successive edit sessions accumulate; <see cref="NpcRecordOverride.TraitsChanged"/> latches once set so a
    ''' later session (whose snapshot already reflects the earlier edit) doesn't drop the template-flag hook.</summary>
    Private Sub RegisterRecordOverride(newFlags As UInteger, baseDataChanged As Boolean, traitsChanged As Boolean,
                                       statsChanged As Boolean, keywordsChanged As Boolean, apprChanged As Boolean,
                                       factionsChanged As Boolean, inventoryChanged As Boolean, combosChanged As Boolean,
                                       perksChanged As Boolean, actorEffectsChanged As Boolean, propertiesChanged As Boolean)
        If Not (baseDataChanged OrElse traitsChanged OrElse statsChanged OrElse keywordsChanged OrElse
                apprChanged OrElse factionsChanged OrElse inventoryChanged OrElse perksChanged OrElse
                actorEffectsChanged OrElse propertiesChanged) Then Return
        Dim ov = _mainForm.TryGetNpcRecordOverride(_npcFormID)
        If ov Is Nothing Then ov = New NpcRecordOverride()

        Dim full = TextBoxFull.Text.Trim()
        Dim shortN = TextBoxShort.Text.Trim()
        If Not String.Equals(full, _snapFull, StringComparison.Ordinal) Then ov.FullName = full
        If Not String.Equals(shortN, _snapShort, StringComparison.Ordinal) Then ov.ShortName = shortN
        If newFlags <> _snapFlags Then ov.AcbsFlags = newFlags
        If CurrentLevelRaw() <> _snapLevel Then ov.Level = CurrentLevelRaw()
        If CShort(NumXp.Value) <> _snapXp Then ov.XpValueOffset = CShort(NumXp.Value)
        If CUShort(NumCalcMin.Value) <> _snapCalcMin Then ov.CalcMinLevel = CUShort(NumCalcMin.Value)
        If CUShort(NumCalcMax.Value) <> _snapCalcMax Then ov.CalcMaxLevel = CUShort(NumCalcMax.Value)
        If CShort(NumDisp.Value) <> _snapDisp Then ov.DispositionBase = CShort(NumDisp.Value)
        If GetFid(TextBoxRace) <> _snapRace Then ov.RaceFormID = GetFid(TextBoxRace)
        If GetFid(TextBoxVoice) <> _snapVoice Then ov.VoiceFormID = GetFid(TextBoxVoice)
        If GetFid(TextBoxClass) <> _snapClass Then ov.ClassFormID = GetFid(TextBoxClass)
        If GetFid(TextBoxZnam) <> _snapZnam Then ov.CombatStyleFormID = GetFid(TextBoxZnam)
        If keywordsChanged Then ov.Keywords = New List(Of UInteger)(_keywords)
        If apprChanged Then ov.AttachParentSlots = New List(Of UInteger)(_appr)
        If factionsChanged Then ov.Factions = _factions.Select(Function(f) New NPC_FactionEntry With {.FactionFormID = f.FactionFormID, .Rank = f.Rank}).ToList()
        If inventoryChanged Then ov.Inventory = _inventory.Select(AddressOf CloneInventoryItem).ToList()
        If perksChanged Then ov.Perks = _perks.Select(Function(p) New NPC_PerkEntry With {.PerkFormID = p.PerkFormID, .Rank = p.Rank}).ToList()
        If actorEffectsChanged Then ov.ActorEffects = New List(Of UInteger)(_actorEffects)
        If propertiesChanged Then ov.Properties = _properties.Select(Function(p) New NPC_PropertyEntry With {.ActorValueFormID = p.ActorValueFormID, .Value = p.Value}).ToList()
        If combosChanged Then ov.ObjectTemplateCombinations = CloneCombos(_combos)
        ov.TraitsChanged = ov.TraitsChanged OrElse traitsChanged

        _mainForm.SetNpcRecordOverride(_npcFormID, ov)
    End Sub

    ''' <summary>Write the panel state into <see cref="_npc"/> (in place — the caller's live cache instance).</summary>
    Private Sub ApplyToNpc(newFlags As UInteger)
        ' General identity.
        Dim full = TextBoxFull.Text.Trim()
        _npc.FullName = full
        _npc.HasFull = _npc.HasFull OrElse full.Length > 0
        Dim shortN = TextBoxShort.Text.Trim()
        _npc.ShortName = shortN
        _npc.HasShortName = _npc.HasShortName OrElse shortN.Length > 0

        Dim raceFid = GetFid(TextBoxRace)
        _npc.RaceFormID = raceFid
        _npc.HasRace = raceFid <> 0UI
        Dim voiceFid = GetFid(TextBoxVoice)
        _npc.VoiceFormID = voiceFid
        _npc.HasVoice = voiceFid <> 0UI
        Dim classFid = GetFid(TextBoxClass)
        _npc.ClassFormID = classFid
        _npc.HasClass = classFid <> 0UI
        Dim znamFid = GetFid(TextBoxZnam)
        _npc.CombatStyleFormID = znamFid
        _npc.HasCombatStyle = znamFid <> 0UI

        ' ACBS struct (create if missing — required subrecord).
        If _npc.Acbs Is Nothing Then _npc.Acbs = New NPC_AcbsData()
        _npc.Acbs.Flags = newFlags
        _npc.AcbsFlags = newFlags
        _npc.IsFemale = (newFlags And &H1UI) <> 0UI
        _npc.Acbs.LevelOrLevelMult = CurrentLevelRaw()
        _npc.Acbs.XpValueOffset = CShort(NumXp.Value)
        _npc.Acbs.CalcMinLevel = CUShort(NumCalcMin.Value)
        _npc.Acbs.CalcMaxLevel = CUShort(NumCalcMax.Value)
        _npc.Acbs.DispositionBase = CShort(NumDisp.Value)

        ' Keywords.
        _npc.KeywordFormIDs = New List(Of UInteger)(_keywords)
        _npc.HasKsizCounter = _npc.HasKsizCounter OrElse _keywords.Count > 0

        ' Attach Parent Slots (APPR).
        _npc.AttachParentSlotFormIDs = New List(Of UInteger)(_appr)

        ' Factions (deep-copy out).
        _npc.Factions = _factions.Select(Function(f) New NPC_FactionEntry With {.FactionFormID = f.FactionFormID, .Rank = f.Rank}).ToList()

        ' Inventory (deep-copy out — carries COED).
        _npc.Inventory = _inventory.Select(AddressOf CloneInventoryItem).ToList()
        _npc.HasCoctCounter = _npc.HasCoctCounter OrElse _inventory.Count > 0

        ' Perks (deep-copy out).
        _npc.Perks = _perks.Select(Function(p) New NPC_PerkEntry With {.PerkFormID = p.PerkFormID, .Rank = p.Rank}).ToList()
        _npc.HasPrkzCounter = _npc.HasPrkzCounter OrElse _perks.Count > 0

        ' Actor Effects (SPLO).
        _npc.ActorEffectFormIDs = New List(Of UInteger)(_actorEffects)
        _npc.HasSpctCounter = _npc.HasSpctCounter OrElse _actorEffects.Count > 0

        ' Properties (PRPS) (deep-copy out).
        _npc.Properties = _properties.Select(Function(p) New NPC_PropertyEntry With {.ActorValueFormID = p.ActorValueFormID, .Value = p.Value}).ToList()

        ' Object Template (deep-copy out).
        _npc.ObjectTemplateCombinations = CloneCombos(_combos)
        _npc.HasObjectTemplate = _npc.HasObjectTemplate OrElse _combos.Count > 0
    End Sub

    ' =====================================================================
    ' FormID picker plumbing + helpers (mirror ArmoEditor)
    ' =====================================================================

    Private Sub PickFidInto(target As TextBox, sigs As String(), title As String, allowNull As Boolean)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, sigs, title, GetFid(target), allowNull)
            If dlg.ShowDialog(Me) = DialogResult.OK Then SetFidText(target, dlg.SelectedFormID)
        End Using
    End Sub

    Private Sub SetFidText(tb As TextBox, fid As UInteger)
        tb.Tag = fid
        tb.Text = If(fid = 0UI, "(none)", $"{DisplayFor(fid)} [0x{fid:X8}]")
    End Sub

    Private Shared Function GetFid(tb As TextBox) As UInteger
        If tb.Tag Is Nothing Then Return 0UI
        Return CUInt(tb.Tag)
    End Function

    Private Function DisplayFor(fid As UInteger) As String
        If fid = 0UI Then Return "(none)"
        Return _mainForm.GetRecordDisplayNameForEditor(fid)
    End Function

    Private Shared Sub SelectGridRow(grid As DataGridView, idx As Integer)
        If idx < 0 OrElse idx >= grid.Rows.Count Then Return
        grid.Rows(idx).Selected = True
        grid.CurrentCell = grid.Rows(idx).Cells(0)
    End Sub

    Private Shared Function ClampDec(v As Decimal, num As NumericUpDown) As Decimal
        If v < num.Minimum Then Return num.Minimum
        If v > num.Maximum Then Return num.Maximum
        Return v
    End Function

    ' =====================================================================
    ' Deep-copy + equality helpers
    ' =====================================================================

    Private Shared Function CloneInventoryItem(it As NPC_InventoryItem) As NPC_InventoryItem
        Return New NPC_InventoryItem With {
            .ItemFormID = it.ItemFormID, .Count = it.Count, .HasCoed = it.HasCoed,
            .CoedOwnerFormID = it.CoedOwnerFormID, .CoedOwnerExtra = it.CoedOwnerExtra,
            .CoedExtraIsFormID = it.CoedExtraIsFormID, .CoedItemCondition = it.CoedItemCondition}
    End Function

    Private Shared Function CloneCombo(w As NPC_ObjectTemplateCombination) As NPC_ObjectTemplateCombination
        Dim innerCopy As ARMO_Combination = Nothing
        If w.Combination IsNot Nothing Then
            innerCopy = ArmoDraft.CloneCombinations(New List(Of ARMO_Combination) From {w.Combination})(0)
        End If
        Return New NPC_ObjectTemplateCombination With {
            .IsEditorOnly = w.IsEditorOnly, .DisplayName = w.DisplayName, .Combination = innerCopy,
            .RawObtsBytes = If(w.RawObtsBytes Is Nothing, Nothing, CType(w.RawObtsBytes.Clone(), Byte()))}
    End Function

    Private Shared Function CloneCombos(src As List(Of NPC_ObjectTemplateCombination)) As List(Of NPC_ObjectTemplateCombination)
        Dim dst As New List(Of NPC_ObjectTemplateCombination)
        If src Is Nothing Then Return dst
        For Each w In src
            If w Is Nothing Then Continue For
            dst.Add(CloneCombo(w))
        Next
        Return dst
    End Function

    ''' <summary>Content signature of the OBTS wrapper list for change detection (name + flags + inner combination
    ''' includes/props/keywords + preserved-raw marker). Order-sensitive.</summary>
    Private Shared Function CombosSignature(list As List(Of NPC_ObjectTemplateCombination)) As String
        Dim parts As New List(Of String)
        For Each w In list
            Dim c = w.Combination
            Dim incl = "", props = "", kwds = ""
            If c IsNot Nothing Then
                incl = String.Join("|", c.Includes.Select(Function(i) i.ModFormID.ToString("X8") & ":" & i.AttachPointIndex.ToString(CultureInfo.InvariantCulture) & ":" & If(i.IsOptional, "1", "0") & If(i.DontUseAll, "1", "0")))
                props = String.Join("|", c.Properties.Select(Function(p) $"{CInt(p.ValueType)}/{p.FunctionType}/{p.PropertyIndex}/{p.Value1FormID:X8}/{BitConverter.ToInt32(BitConverter.GetBytes(p.Value1), 0)}/{BitConverter.ToInt32(BitConverter.GetBytes(p.Value2), 0)}"))
                kwds = String.Join(",", c.Keywords.Select(Function(k) k.ToString("X8")))
            End If
            Dim rawLen = If(w.RawObtsBytes IsNot Nothing, w.RawObtsBytes.Length, -1)
            Dim isDefault = If(c IsNot Nothing AndAlso c.IsDefault, "1", "0")
            parts.Add($"{w.DisplayName}#{If(w.IsEditorOnly, 1, 0)}#{isDefault}#{incl}#{props}#{kwds}#{rawLen}")
        Next
        Return String.Join("~", parts)
    End Function

    Private Shared Function SequenceEqualU(a As List(Of UInteger), b As List(Of UInteger)) As Boolean
        If a.Count <> b.Count Then Return False
        For i = 0 To a.Count - 1
            If a(i) <> b(i) Then Return False
        Next
        Return True
    End Function

    Private Shared Function FactionsEqual(a As List(Of NPC_FactionEntry), b As List(Of NPC_FactionEntry)) As Boolean
        If a.Count <> b.Count Then Return False
        For i = 0 To a.Count - 1
            If a(i).FactionFormID <> b(i).FactionFormID OrElse a(i).Rank <> b(i).Rank Then Return False
        Next
        Return True
    End Function

    Private Shared Function InventoryEqual(a As List(Of NPC_InventoryItem), b As List(Of NPC_InventoryItem)) As Boolean
        If a.Count <> b.Count Then Return False
        For i = 0 To a.Count - 1
            If a(i).ItemFormID <> b(i).ItemFormID OrElse a(i).Count <> b(i).Count Then Return False
        Next
        Return True
    End Function

    Private Shared Function PerksEqual(a As List(Of NPC_PerkEntry), b As List(Of NPC_PerkEntry)) As Boolean
        If a.Count <> b.Count Then Return False
        For i = 0 To a.Count - 1
            If a(i).PerkFormID <> b(i).PerkFormID OrElse a(i).Rank <> b(i).Rank Then Return False
        Next
        Return True
    End Function

    Private Shared Function PropertiesEqual(a As List(Of NPC_PropertyEntry), b As List(Of NPC_PropertyEntry)) As Boolean
        If a.Count <> b.Count Then Return False
        For i = 0 To a.Count - 1
            If a(i).ActorValueFormID <> b(i).ActorValueFormID OrElse a(i).Value <> b(i).Value Then Return False
        Next
        Return True
    End Function

End Class
