Imports System.Globalization
Imports System.Linq
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

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
    Private ReadOnly _actorEffects As New List(Of UInteger)        ' SPLO → SPEL
    ''' <summary>Las combinaciones del Object Template, en el orden de las filas. Cada elemento es una vista
    ''' sobre <see cref="_comboHost"/>.</summary>
    Private ReadOnly _combos As New List(Of Canon.IBloque_Combinations)

    ''' <summary>El NPC_ sobre el que viven las combinaciones que el usuario está armando: una COPIA del que
    ''' las trajo. Una vista no existe sin un nodo, y el nodo tiene que colgar de algún árbol; al ser copia
    ''' del original hereda su contexto, y cancelar no le deja nada escrito al record de verdad.</summary>
    Private _comboHost As Canon.INpc = Nothing

    ''' <summary>El borrador: un CLON del record del NPC. Las listas que se editan por fila —SNAM, CNTO/COED,
    ''' PRKR, PRPS— y el DNAM de Skyrim viven acá, en el árbol, no en una copia con otros nombres. Cancelar es
    ''' tirar el clon; aceptar es volcarlo sobre el record vivo. Las filas que el editor no toca conservan cada
    ''' byte, incluidos los campos que ningún control muestra.</summary>
    Private ReadOnly _borrador As Canon.INpc
    ''' <summary>Otro clon, tomado al terminar de cargar los paneles: la línea base contra la que se decide
    ''' qué cambió de verdad.</summary>
    Private _snapRecord As Canon.INpc

    ' ACBS flag bit map (checkbox → bit) built from the Canon.INpc.ConfigurationFlags* bit definitions. The
    ' union is used to preserve bits NOT surfaced as a checkbox when re-composing the flags word.
    Private ReadOnly _flagChecks As New List(Of (Chk As CheckBox, Mask As UInteger))
    Private _managedFlagMask As UInteger
    Private _loadedFlagsWord As UInteger
    ''' <summary>True when editing a Skyrim NPC — gates the Stats tab (DNAM Player Skills) and the SSE-only
    ''' ACBS offsets, both of which have no slot in the Fallout 4 record.</summary>
    Private ReadOnly _isSkyrim As Boolean = (Config_App.Current IsNot Nothing AndAlso
                                             Config_App.Current.Game = Config_App.Game_Enum.Skyrim)

    ' Los 18 pares de spinners del Designer, indexados por el indice de skill del DNAM.
    ' Arrays of already-built controls, not UI construction — the tab itself lives in the Designer.
    Private _skillVals As NumericUpDown()
    Private _skillOffs As NumericUpDown()
    Private _skillLabels As Label()

    ' El DNAM de apertura (SSE) vive en _snapRecord, igual que el resto del record. Acá queda sólo el
    ' float de far-away-model como el DECIMAL con el que se sembró el spinner, para que un valor sin tocar
    ' re-emita su float original en vez de la ida y vuelta por el NumericUpDown.
    Private _snapFarModelDec As Decimal
    Private _snapMagickaOff As Short, _snapStaminaOff As Short, _snapHealthOff As Short
    Private _snapSpeedMult As UShort

    ''' <summary>True while NumLevel is in "Level Mult" mode (PC Level Mult flag 0x80 set) — the u16 is a mult
    ''' shown as raw/1000 with 3 decimals. False = fixed integer Level. See Canon.INpc.ConfigurationLevelMult,
    ''' gated by ConfigurationFlagsPCLevelMult.</summary>
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
    ' Outfits (DOFT/SOFT) — raw record value (for the "reverted to record default → clear override" decision)
    ' and the effective (overlay-aware) value shown at open (the snapshot baseline for change detection).
    Private _rawDefaultOutfit As UInteger, _rawSleepOutfit As UInteger
    Private _snapDefaultOutfit As UInteger, _snapSleepOutfit As UInteger
    Private _snapActorEffects As New List(Of UInteger)
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
        _borrador = npc.Record.Copia()

        BuildSkillControlMap()
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

    ''' <summary>Show only the subrecords the CURRENT game's record actually has a slot for, so the user can
    ''' never author a field the writer for that game will not emit (a control that writes nowhere is worse than
    ''' no control). Gating runs BOTH ways because ACBS/DNAM have a different layout per game:
    '''
    ''' Hidden on SKYRIM (Fallout 4 only): the OBTS/OBTE and PRPS tabs, the APPR attach-parent-slot section, the
    ''' ACBS 0x800000 flag (see BuildFlagChecks), and XP Value Offset (ACBS +4 in the 20-byte FO4 struct — in the
    ''' 24-byte Skyrim struct those bytes are the Magicka Offset instead).
    '''
    ''' Hidden on FALLOUT 4 (Skyrim only): the Stats tab (DNAM = 52-byte Player Skills in Skyrim; in FO4 DNAM is
    ''' an unrelated 8-byte Calculated-Stats block the engine recomputes, so there is nothing to edit) and the
    ''' four ACBS offsets the 24-byte Skyrim struct adds (Magicka/Stamina/Health Offset + Speed Multiplier).
    '''
    ''' Removing a tab / hiding a row is loss-free: a category the editor never touches round-trips verbatim from
    ''' the source record, and the hidden rows sit in AutoSize TableLayoutPanel rows that collapse to zero height.</summary>
    Private Sub ApplyGameGating()
        If _isSkyrim Then
            If Tabs.TabPages.Contains(TabObts) Then Tabs.TabPages.Remove(TabObts)
            If Tabs.TabPages.Contains(TabProps) Then Tabs.TabPages.Remove(TabProps)
            For Each c As Control In New Control() {LabelAppr, ListAppr, ButtonAddAppr, ButtonRemoveAppr,
                                                    LabelXp, NumXp}
                If c IsNot Nothing Then c.Visible = False
            Next
            If ChkNoActHellos IsNot Nothing Then ChkNoActHellos.Visible = False
        Else
            If Tabs.TabPages.Contains(TabStats) Then Tabs.TabPages.Remove(TabStats)
            For Each c As Control In New Control() {LabelMagickaOff, NumMagickaOff, LabelStaminaOff, NumStaminaOff,
                                                    LabelHealthOff, NumHealthOff, LabelSpeedMult, NumSpeedMult}
                If c IsNot Nothing Then c.Visible = False
            Next
        End If
    End Sub

    ''' <summary>Indexa por índice de skill del DNAM los 18 pares de spinners del Designer. El índice ES el
    ''' orden de bytes: el i-ésimo es el valor en +i y el desplazamiento en +18+i. Las etiquetas las pone
    ''' <see cref="LoadPlayerSkills"/> con el nombre que el esquema le da a cada elemento del arreglo, así
    ''' que una fila no puede quedar desfasada del byte que escribe.</summary>
    Private Sub BuildSkillControlMap()
        _skillVals = {NumSkillVal0, NumSkillVal1, NumSkillVal2, NumSkillVal3, NumSkillVal4, NumSkillVal5,
                      NumSkillVal6, NumSkillVal7, NumSkillVal8, NumSkillVal9, NumSkillVal10, NumSkillVal11,
                      NumSkillVal12, NumSkillVal13, NumSkillVal14, NumSkillVal15, NumSkillVal16, NumSkillVal17}
        _skillOffs = {NumSkillOff0, NumSkillOff1, NumSkillOff2, NumSkillOff3, NumSkillOff4, NumSkillOff5,
                      NumSkillOff6, NumSkillOff7, NumSkillOff8, NumSkillOff9, NumSkillOff10, NumSkillOff11,
                      NumSkillOff12, NumSkillOff13, NumSkillOff14, NumSkillOff15, NumSkillOff16, NumSkillOff17}
        _skillLabels = {LabelSkill0, LabelSkill1, LabelSkill2, LabelSkill3, LabelSkill4, LabelSkill5,
                        LabelSkill6, LabelSkill7, LabelSkill8, LabelSkill9, LabelSkill10, LabelSkill11,
                        LabelSkill12, LabelSkill13, LabelSkill14, LabelSkill15, LabelSkill16, LabelSkill17}
    End Sub

    ' =====================================================================
    ' One-time UI construction (Designer rule: variable/typed columns in code)
    ' =====================================================================

    Private Sub BuildFlagChecks()
        ' ACBS Flags bit values. VERIFIED identical between FO4 and Skyrim for EVERY surfaced
        ' flag EXCEPT bit 0x800000: FO4 = "No Activation / Hellos", Skyrim = "Unknown 23"
        ' (unused). So ChkNoActHellos is added ONLY on FO4 — on Skyrim its bit stays OUT of
        ' _managedFlagMask and is preserved verbatim by ComposeFlags (the checkbox is hidden in
        ' ApplyGameGating). Note: ACBS bit 0x04 (Is CharGen Face Preset) is intentionally NOT
        ' surfaced here (both games) — owned by the Face editor, preserved verbatim.
        Dim isSkyrim = _isSkyrim
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
            Dim lvlnPick As Func(Of UInteger, UInteger) = AddressOf _mainForm.ResolveLvlnPick_Friend
            Dim baseNpc = NpcTemplateMaterializer.ResolveEffectiveSourceForEditor(_npc, NPC_TemplateCategory.BaseData, _getParsedNpc, lvlnPick)
            Dim traitsNpc = NpcTemplateMaterializer.ResolveEffectiveSourceForEditor(_npc, NPC_TemplateCategory.Traits, _getParsedNpc, lvlnPick)
            Dim statsNpc = NpcTemplateMaterializer.ResolveEffectiveSourceForEditor(_npc, NPC_TemplateCategory.Stats, _getParsedNpc, lvlnPick)
            Dim keywordsNpc = NpcTemplateMaterializer.ResolveEffectiveSourceForEditor(_npc, NPC_TemplateCategory.Keywords, _getParsedNpc, lvlnPick)
            Dim factionsNpc = NpcTemplateMaterializer.ResolveEffectiveSourceForEditor(_npc, NPC_TemplateCategory.Factions, _getParsedNpc, lvlnPick)
            Dim inventoryNpc = NpcTemplateMaterializer.ResolveEffectiveSourceForEditor(_npc, NPC_TemplateCategory.Inventory, _getParsedNpc, lvlnPick)
            Dim spellsNpc = NpcTemplateMaterializer.ResolveEffectiveSourceForEditor(_npc, NPC_TemplateCategory.SpellList, _getParsedNpc, lvlnPick)
            ' General.
            TextBoxFull.Text = If(baseNpc.Record.Name, "")
            TextBoxShort.Text = If(baseNpc.Record.ShortName, "")
            Dim raceFid = If(traitsNpc.Record.Race <> 0UI, traitsNpc.Record.Race, fallbackRaceFormID)
            SetFidText(TextBoxRace, raceFid)
            SetFidText(TextBoxVoice, traitsNpc.Record.Voice)
            SetFidText(TextBoxClass, traitsNpc.Record.[Class])
            SetFidText(TextBoxZnam, traitsNpc.Record.CombatStyle)

            Dim traitsFlags As UInteger = traitsNpc.Record.ConfigurationFlags
            Dim statsFlags As UInteger = statsNpc.Record.ConfigurationFlags
            Dim baseFlags As UInteger = baseNpc.Record.ConfigurationFlags
            Dim ownFlags As UInteger = _npc.Record.ConfigurationFlags
            Dim governedMask As UInteger = NpcTemplateHelpers.ClassifiedAcbsFlagsMask
            Dim flagsWord As UInteger = (ownFlags And Not governedMask) Or
                                        (traitsFlags And NpcTemplateHelpers.TraitsAcbsFlagsMask) Or
                                        (baseFlags And NpcTemplateHelpers.BaseDataAcbsFlagsMask) Or
                                        (statsFlags And NpcTemplateHelpers.StatsAcbsFlagsMask)
            _loadedFlagsWord = flagsWord
            SetFlagChecks(flagsWord)   ' fires ChkPCLevelMult.CheckedChanged, guarded by _loading (no-op here)
            ' Level union — mode from the PC Level Mult flag (0x80), value from the raw u16.
            ConfigureLevelControl((flagsWord And &H80UI) <> 0UI, statsNpc.Record.NivelDeConfiguracion())
            Dim statsFo4 = TryCast(statsNpc.Record, Canon.NpcFO4)
            NumXp.Value = ClampDec(CDec(If(statsFo4 Is Nothing, 0S, statsFo4.ConfigurationXPValueOffset)), NumXp)
            NumCalcMin.Value = ClampDec(CDec(statsNpc.Record.ConfigurationCalcMinLevel), NumCalcMin)
            NumCalcMax.Value = ClampDec(CDec(statsNpc.Record.ConfigurationCalcMaxLevel), NumCalcMax)
            NumDisp.Value = ClampDec(CDec(traitsNpc.Record.BaseDeDisposicion()), NumDisp)
            ' SSE-only ACBS offsets (hidden on FO4, whose 20-byte struct has no slot for them).
            Dim statsSse = TryCast(statsNpc.Record, Canon.NpcSSE)
            NumMagickaOff.Value = ClampDec(CDec(If(statsSse Is Nothing, 0S, statsSse.ConfigurationMagickaOffset)), NumMagickaOff)
            NumStaminaOff.Value = ClampDec(CDec(If(statsSse Is Nothing, 0S, statsSse.ConfigurationStaminaOffset)), NumStaminaOff)
            NumHealthOff.Value = ClampDec(CDec(If(statsSse Is Nothing, 0S, statsSse.ConfigurationHealthOffset)), NumHealthOff)
            NumSpeedMult.Value = ClampDec(CDec(If(statsSse Is Nothing, 0US, statsSse.ConfigurationSpeedMultiplier)), NumSpeedMult)

            ' Stats — DNAM Player Skills (SSE). The tab is removed on FO4, so this is a no-op there.
            LoadPlayerSkills(statsNpc)

            ' Object Template — se edita sobre una COPIA del record que trae las combinaciones, así el
            ' sub-editor y el reordenamiento no tocan el original.
            _comboHost = traitsNpc.Record.Copia()
            _combos.Clear()
            If _comboHost IsNot Nothing Then _combos.AddRange(_comboHost.CombinacionesDelNpc())
            RefreshCombosGrid()

            ' Keywords.
            _keywords.Clear()
            _keywords.AddRange(keywordsNpc.Record.PalabrasClave())
            RefreshKeywordsList()

            ' Attach Parent Slots (APPR).
            _appr.Clear()
            _appr.AddRange(traitsNpc.Record.RanurasDeEnganche())
            RefreshApprList()

            ' Factions — la fuente resuelta se vuelca en el borrador (las entradas son nodos del árbol).
            _borrador.PonerFacciones(factionsNpc.Record.Factions)
            RefreshFactionsGrid()

            ' Inventory — con el bloque COED de cada entrada.
            _borrador.PonerInventario(inventoryNpc.Record.Items)
            RefreshInventoryGrid()

            ' Outfits (DOFT/SOFT). Seed with the EFFECTIVE (overlay-aware) values: a prior Edit Outfit pick
            ' lives in the LooksMenu overlay, not on _npc, so show the overlaid value. Keep the raw record value
            ' too so OnOk can map "reverted to the record's own outfit" back to a cleared override.
            _rawDefaultOutfit = _npc.Record.DefaultOutfit
            _rawSleepOutfit = _npc.Record.SleepingOutfit
            Dim effDefaultOutfit = _rawDefaultOutfit, effSleepOutfit = _rawSleepOutfit
            _mainForm.GetEffectiveNpcOutfitsForEditor(_npcFormID, _rawDefaultOutfit, _rawSleepOutfit, effDefaultOutfit, effSleepOutfit)
            SetFidText(TextBoxDefaultOutfit, effDefaultOutfit)
            SetFidText(TextBoxSleepOutfit, effSleepOutfit)

            ' Perks — sin categoría de plantilla: siempre las propias del record.
            RefreshPerksGrid()

            ' Actor Effects (SPLO).
            _actorEffects.Clear()
            _actorEffects.AddRange(spellsNpc.Record.EfectosDeActor())
            RefreshSpellList()

            ' Properties (PRPS) — sin categoría de plantilla, igual que las ventajas.
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
    ''' 3 decimals, value = raw/1000. Fixed (OFF): integer "Level".</summary>
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

    ' =====================================================================
    ' Stats tab — DNAM Player Skills (SSE)
    ' =====================================================================

    ''' <summary>El DNAM de Skyrim del borrador, o Nothing cuando el NPC no es de Skyrim.</summary>
    Private Function BorradorSse() As Canon.NpcSSE
        If Not _isSkyrim Then Return Nothing
        Return TryCast(_borrador, Canon.NpcSSE)
    End Function

    ''' <summary>El DNAM de la línea base de apertura, o Nothing cuando el NPC no es de Skyrim.</summary>
    Private Function LineaBaseSse() As Canon.NpcSSE
        If Not _isSkyrim OrElse _snapRecord Is Nothing Then Return Nothing
        Return TryCast(_snapRecord, Canon.NpcSSE)
    End Function

    ''' <summary>Copia el DNAM de la fuente resuelta al borrador y siembra con él los spinners de Stats. El
    ''' bloque viaja como subrecord entero: los campos que ningún control muestra —los dos tramos de relleno
    ''' sin usar incluidos— quedan tal cual y el record re-emite byte a byte. Un NPC de Skyrim con un DNAM
    ''' demasiado corto para modelar arranca en cero y no se escribe nada salvo que el usuario edite.</summary>
    Private Sub LoadPlayerSkills(sourceNpc As NPC_Data)
        Dim ns = BorradorSse()
        If ns Is Nothing Then Return
        If Not ReferenceEquals(sourceNpc.Record, _npc.Record) Then _borrador.CopiarSubrecord(sourceNpc.Record, "DNAM")

        For i = 0 To Math.Min(ns.SkillValues.Count, ns.SkillOffsets.Count) - 1
            If i >= _skillVals.Length Then Exit For
            _skillVals(i).Value = ClampDec(CDec(ns.SkillValues(i).Skill), _skillVals(i))
            _skillOffs(i).Value = ClampDec(CDec(ns.SkillOffsets(i).Skill), _skillOffs(i))
            ' El nombre sale del esquema, que es donde vive el orden real del arreglo. Sin DNAM no hay
            ' elementos de los que sacarlo y la fila conserva el texto del Designer.
            Dim nombre = ns.SkillValues(i).Node?.Name
            If Not String.IsNullOrEmpty(nombre) Then _skillLabels(i).Text = nombre & ":"
        Next
        NumHealth.Value = ClampDec(CDec(ns.PlayerSkillsHealth), NumHealth)
        NumMagicka.Value = ClampDec(CDec(ns.PlayerSkillsMagicka), NumMagicka)
        NumStamina.Value = ClampDec(CDec(ns.PlayerSkillsStamina), NumStamina)
        NumGeared.Value = ClampDec(CDec(ns.PlayerSkillsGearedUpWeapons), NumGeared)
        NumFarModel.Value = ClampDec(CDec(ns.PlayerSkillsFarAwayModelDistance), NumFarModel)
        _snapFarModelDec = NumFarModel.Value
    End Sub

    ''' <summary>Vuelca el panel de Stats en el DNAM del borrador. Lo que ningún control muestra sigue donde
    ''' estaba. El float de far-away-model se pisa sólo si su spinner se movió de verdad: un NumericUpDown no
    ''' representa todos los floats y releerlo sin haberlo tocado correría los bytes.</summary>
    Private Sub ComposePlayerSkills()
        Dim ns = BorradorSse()
        If ns Is Nothing Then Return
        For i = 0 To Math.Min(ns.SkillValues.Count, ns.SkillOffsets.Count) - 1
            If i >= _skillVals.Length Then Exit For
            ns.SkillValues(i).Skill = CByte(_skillVals(i).Value)
            ns.SkillOffsets(i).Skill = CByte(_skillOffs(i).Value)
        Next
        ns.PlayerSkillsHealth = CUShort(NumHealth.Value)
        ns.PlayerSkillsMagicka = CUShort(NumMagicka.Value)
        ns.PlayerSkillsStamina = CUShort(NumStamina.Value)
        ns.PlayerSkillsGearedUpWeapons = CByte(NumGeared.Value)
        If NumFarModel.Value <> _snapFarModelDec Then ns.PlayerSkillsFarAwayModelDistance = CSng(NumFarModel.Value)
    End Sub

    ''' <summary>True when any Stats-tab value differs from the open-time snapshot.</summary>
    Private Function PlayerSkillsChanged() As Boolean
        Dim ns = LineaBaseSse()
        If ns Is Nothing Then Return False
        For i = 0 To Math.Min(ns.SkillValues.Count, ns.SkillOffsets.Count) - 1
            If i >= _skillVals.Length Then Exit For
            If CByte(_skillVals(i).Value) <> ns.SkillValues(i).Skill Then Return True
            If CByte(_skillOffs(i).Value) <> ns.SkillOffsets(i).Skill Then Return True
        Next
        Return CUShort(NumHealth.Value) <> ns.PlayerSkillsHealth OrElse
               CUShort(NumMagicka.Value) <> ns.PlayerSkillsMagicka OrElse
               CUShort(NumStamina.Value) <> ns.PlayerSkillsStamina OrElse
               CByte(NumGeared.Value) <> ns.PlayerSkillsGearedUpWeapons OrElse
               NumFarModel.Value <> _snapFarModelDec
    End Function

    ''' <summary>True when any SSE-only ACBS offset differs from the open-time snapshot (always False on FO4,
    ''' where those spinners are hidden and never seeded with anything but the struct's zeroed fields).</summary>
    Private Function SseAcbsOffsetsChanged() As Boolean
        If Not _isSkyrim Then Return False
        Return CShort(NumMagickaOff.Value) <> _snapMagickaOff OrElse
               CShort(NumStaminaOff.Value) <> _snapStaminaOff OrElse
               CShort(NumHealthOff.Value) <> _snapHealthOff OrElse
               CUShort(NumSpeedMult.Value) <> _snapSpeedMult
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
        _snapMagickaOff = CShort(NumMagickaOff.Value)
        _snapStaminaOff = CShort(NumStaminaOff.Value)
        _snapHealthOff = CShort(NumHealthOff.Value)
        _snapSpeedMult = CUShort(NumSpeedMult.Value)
        ' _snapFarModelDec sale de LoadPlayerSkills; el DNAM y las listas por fila salen del clon de abajo.
        _snapRecord = _borrador.Copia()
        _snapKeywords = New List(Of UInteger)(_keywords)
        _snapAppr = New List(Of UInteger)(_appr)
        _snapDefaultOutfit = GetFid(TextBoxDefaultOutfit)
        _snapSleepOutfit = GetFid(TextBoxSleepOutfit)
        _snapActorEffects = New List(Of UInteger)(_actorEffects)
        _snapCombosSig = CombosSignature(_combos)
    End Sub

    ''' <summary>The flags word as it stands in the checkboxes right after load — the snapshot baseline. Read
    ''' from the raw NPC word (not <see cref="ComposeFlags"/>, which itself depends on _snapFlags).</summary>
    Private Function ReadCurrentFlagsForSnapshot() As UInteger
        Return _loadedFlagsWord
    End Function

    ' =====================================================================
    ' Object Template (OBTS) combinations
    ' =====================================================================

    Private Sub RefreshCombosGrid()
        Dim selIdx = If(GridCombos.CurrentRow IsNot Nothing, GridCombos.CurrentRow.Index, -1)
        GridCombos.Rows.Clear()
        For i = 0 To _combos.Count - 1
            Dim c = _combos(i)
            GridCombos.Rows.Add((i + 1).ToString(CultureInfo.InvariantCulture),
                                If(Not String.IsNullOrEmpty(c.CombinationName), c.CombinationName, "(unnamed)"),
                                If(c.ObjectModTemplateItemDefault, "Yes", ""),
                                If(c.CombinationEditorOnly, "Yes", ""),
                                c.Includes.Count.ToString(CultureInfo.InvariantCulture),
                                c.Properties.Count.ToString(CultureInfo.InvariantCulture),
                                c.Keywords.Count.ToString(CultureInfo.InvariantCulture),
                                "")
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
        Dim nueva = _comboHost.AgregarCombinacion(Nothing)
        If nueva Is Nothing Then Return
        Using dlg As New ObtsCombinationEditor_Form(_mainForm, nueva)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                _combos.Add(nueva)
                RefreshCombosGrid()
            End If
        End Using
    End Sub

    Private Sub OnDuplicateCombo(sender As Object, e As EventArgs)
        Dim i = SelectedComboIndex()
        If i < 0 Then Return
        Dim copia = _comboHost.AgregarCombinacion(_combos(i))
        If copia Is Nothing Then Return
        _combos.Insert(i + 1, copia)
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

    ''' <summary>Edita una combinacion en el sub-editor compartido, el mismo que usa el editor de ARMO. El
    ''' sub-editor trabaja sobre una copia aparte, así cancelar deja la fila como estaba.</summary>
    Private Sub EditComboAt(i As Integer)
        If i < 0 OrElse i >= _combos.Count Then Return
        Dim trabajo = _comboHost.AgregarCombinacion(_combos(i))
        If trabajo Is Nothing Then Return
        Using dlg As New ObtsCombinationEditor_Form(_mainForm, trabajo)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                _combos(i) = trabajo
                RefreshCombosGrid()
                SelectGridRow(GridCombos, i)
            End If
        End Using
    End Sub

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
        For Each f In _borrador.Factions
            GridFactions.Rows.Add($"{DisplayFor(f.Faction)} [0x{f.Faction:X8}]",
                                  f.FactionRank.ToString(CultureInfo.InvariantCulture))
        Next
        SelectGridRow(GridFactions, selIdx)
    End Sub

    Private Function SelectedFactionIndex() As Integer
        If GridFactions.CurrentRow Is Nothing Then Return -1
        Dim i = GridFactions.CurrentRow.Index
        If i < 0 OrElse i >= _borrador.Factions.Count Then Return -1
        Return i
    End Function

    ''' <summary>Agrega una faccion. La entrada se crea en el borrador ANTES de abrir el sub-editor -es un
    ''' nodo del arbol, no un objeto suelto- y se saca de vuelta si el usuario cancela. La baja va por
    ''' referencia: sacar "la ultima" daba lo mismo hoy, pero apoyado en que nadie mas toque la lista
    ''' mientras el dialogo esta abierto, y eso no es una garantia de nada.</summary>
    Private Sub OnAddFaction(sender As Object, e As EventArgs)
        Dim entrada = _borrador.AgregarFactions()
        If entrada Is Nothing Then Return
        Using dlg As New NpcFactionEntryEditor_Form(_mainForm, 0UI, 0)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                entrada.Faction = dlg.ResultFormID
                entrada.FactionRank = dlg.ResultRank
                RefreshFactionsGrid()
            Else
                _borrador.QuitarFactions(entrada)
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
        If i < 0 OrElse i >= _borrador.Factions.Count Then Return
        Dim entrada = _borrador.Factions(i)
        Using dlg As New NpcFactionEntryEditor_Form(_mainForm, entrada.Faction, entrada.FactionRank)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                entrada.Faction = dlg.ResultFormID
                entrada.FactionRank = dlg.ResultRank
                RefreshFactionsGrid()
                SelectGridRow(GridFactions, i)
            End If
        End Using
    End Sub

    Private Sub OnRemoveFaction(sender As Object, e As EventArgs)
        Dim i = SelectedFactionIndex()
        If i < 0 Then Return
        _borrador.QuitarFactions(i)
        RefreshFactionsGrid()
    End Sub

    ' =====================================================================
    ' Inventory
    ' =====================================================================

    Private Sub RefreshInventoryGrid()
        Dim selIdx = If(GridInventory.CurrentRow IsNot Nothing, GridInventory.CurrentRow.Index, -1)
        GridInventory.Rows.Clear()
        For Each it In _borrador.Items
            GridInventory.Rows.Add($"{DisplayFor(it.Item)} [0x{it.Item:X8}]",
                                   it.ItemCount.ToString(CultureInfo.InvariantCulture))
        Next
        SelectGridRow(GridInventory, selIdx)
    End Sub

    Private Function SelectedItemIndex() As Integer
        If GridInventory.CurrentRow Is Nothing Then Return -1
        Dim i = GridInventory.CurrentRow.Index
        If i < 0 OrElse i >= _borrador.Items.Count Then Return -1
        Return i
    End Function

    Private Sub OnAddItem(sender As Object, e As EventArgs)
        Dim entrada = _borrador.AgregarItems()
        If entrada Is Nothing Then Return
        Using dlg As New NpcInventoryEntryEditor_Form(_mainForm, 0UI, 1)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                entrada.Item = dlg.ResultFormID
                entrada.ItemCount = dlg.ResultCount
                RefreshInventoryGrid()
            Else
                _borrador.QuitarItems(entrada)
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

    ''' <summary>Edita item y cantidad de una entrada. El bloque COED de propiedad, que el sub-editor no
    ''' muestra, queda intacto: se escribe sobre la MISMA entrada del borrador, no sobre una copia.</summary>
    Private Sub EditItemAt(i As Integer)
        If i < 0 OrElse i >= _borrador.Items.Count Then Return
        Dim entrada = _borrador.Items(i)
        Using dlg As New NpcInventoryEntryEditor_Form(_mainForm, entrada.Item, entrada.ItemCount)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                entrada.Item = dlg.ResultFormID
                entrada.ItemCount = dlg.ResultCount
                RefreshInventoryGrid()
                SelectGridRow(GridInventory, i)
            End If
        End Using
    End Sub

    Private Sub OnRemoveItem(sender As Object, e As EventArgs)
        Dim i = SelectedItemIndex()
        If i < 0 Then Return
        _borrador.QuitarItems(i)
        RefreshInventoryGrid()
    End Sub

    ' =====================================================================
    ' Perks (PRKR — PERK FormID + u8 Rank)
    ' =====================================================================

    Private Sub RefreshPerksGrid()
        Dim selIdx = If(GridPerks.CurrentRow IsNot Nothing, GridPerks.CurrentRow.Index, -1)
        GridPerks.Rows.Clear()
        For Each p In _borrador.Perks
            GridPerks.Rows.Add($"{DisplayFor(p.Perk)} [0x{p.Perk:X8}]",
                               p.PerkRank.ToString(CultureInfo.InvariantCulture))
        Next
        SelectGridRow(GridPerks, selIdx)
    End Sub

    Private Function SelectedPerkIndex() As Integer
        If GridPerks.CurrentRow Is Nothing Then Return -1
        Dim i = GridPerks.CurrentRow.Index
        If i < 0 OrElse i >= _borrador.Perks.Count Then Return -1
        Return i
    End Function

    Private Sub OnAddPerk(sender As Object, e As EventArgs)
        Dim entrada = _borrador.AgregarPerks()
        If entrada Is Nothing Then Return
        Using dlg As New NpcPerkEntryEditor_Form(_mainForm, 0UI, 0)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                entrada.Perk = dlg.ResultFormID
                entrada.PerkRank = dlg.ResultRank
                RefreshPerksGrid()
            Else
                _borrador.QuitarPerks(entrada)
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
        If i < 0 OrElse i >= _borrador.Perks.Count Then Return
        Dim entrada = _borrador.Perks(i)
        Using dlg As New NpcPerkEntryEditor_Form(_mainForm, entrada.Perk, entrada.PerkRank)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                entrada.Perk = dlg.ResultFormID
                entrada.PerkRank = dlg.ResultRank
                RefreshPerksGrid()
                SelectGridRow(GridPerks, i)
            End If
        End Using
    End Sub

    Private Sub OnRemovePerk(sender As Object, e As EventArgs)
        Dim i = SelectedPerkIndex()
        If i < 0 Then Return
        _borrador.QuitarPerks(i)
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

    ''' <summary>El borrador visto como record de Fallout 4, o Nothing en Skyrim: PRPS es de Fallout 4 y
    ''' la pestaña que lo muestra no existe en el otro juego.</summary>
    Private Function BorradorFo4() As Canon.NpcFO4
        Return TryCast(_borrador, Canon.NpcFO4)
    End Function

    Private Function PropiedadesDelBorrador() As IReadOnlyList(Of Canon.NpcFO4_Properties2)
        Dim nf = BorradorFo4()
        If nf Is Nothing Then Return Array.Empty(Of Canon.NpcFO4_Properties2)()
        Return nf.Properties2
    End Function

    Private Sub RefreshPropsGrid()
        Dim selIdx = If(GridProps.CurrentRow IsNot Nothing, GridProps.CurrentRow.Index, -1)
        GridProps.Rows.Clear()
        For Each p In PropiedadesDelBorrador()
            GridProps.Rows.Add($"{DisplayFor(p.PropertyActorValue)} [0x{p.PropertyActorValue:X8}]",
                               p.PropertyValue.ToString(CultureInfo.InvariantCulture))
        Next
        SelectGridRow(GridProps, selIdx)
    End Sub

    Private Function SelectedPropIndex() As Integer
        If GridProps.CurrentRow Is Nothing Then Return -1
        Dim i = GridProps.CurrentRow.Index
        If i < 0 OrElse i >= PropiedadesDelBorrador().Count Then Return -1
        Return i
    End Function

    Private Sub OnAddProp(sender As Object, e As EventArgs)
        Dim nf = BorradorFo4()
        If nf Is Nothing Then Return
        Dim entrada = nf.AgregarProperties2()
        If entrada Is Nothing Then Return
        Using dlg As New NpcPropertyEntryEditor_Form(_mainForm, 0UI, 0.0F)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                entrada.PropertyActorValue = dlg.ResultFormID
                entrada.PropertyValue = dlg.ResultValue
                RefreshPropsGrid()
            Else
                nf.QuitarProperties2(entrada)
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
        Dim lista = PropiedadesDelBorrador()
        If i < 0 OrElse i >= lista.Count Then Return
        Dim entrada = lista(i)
        Using dlg As New NpcPropertyEntryEditor_Form(_mainForm, entrada.PropertyActorValue, entrada.PropertyValue)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                entrada.PropertyActorValue = dlg.ResultFormID
                entrada.PropertyValue = dlg.ResultValue
                RefreshPropsGrid()
                SelectGridRow(GridProps, i)
            End If
        End Using
    End Sub

    Private Sub OnRemoveProp(sender As Object, e As EventArgs)
        Dim nf = BorradorFo4()
        If nf Is Nothing Then Return
        Dim i = SelectedPropIndex()
        If i < 0 Then Return
        nf.QuitarProperties2(i)
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
        Dim changedFlagBits = newFlags Xor _snapFlags
        Dim flagsChanged = changedFlagBits <> 0UI
        Dim unsupportedChangedFlagBits = changedFlagBits And
                                         (_managedFlagMask And Not NpcTemplateHelpers.ClassifiedAcbsFlagsMask)
        If unsupportedChangedFlagBits <> 0UI AndAlso _npc.Record.ConfigurationTemplateFlags <> 0US Then
            Dim flagNames = String.Join(", ", _flagChecks.
                                        Where(Function(fc) (unsupportedChangedFlagBits And fc.Mask) <> 0UI).
                                        Select(Function(fc) fc.Chk.Text))
            MessageBox.Show(Me,
                            $"Cannot safely change {flagNames} (0x{unsupportedChangedFlagBits:X8}) while this NPC inherits template categories." &
                            Environment.NewLine & "Revert those checkboxes to continue. Their engine template category has not been measured, so saving them could silently discard the edit.",
                            "Unsupported templated flag edit", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        Dim baseDataChanged = (changedFlagBits And NpcTemplateHelpers.BaseDataAcbsFlagsMask) <> 0UI OrElse
                              Not String.Equals(TextBoxFull.Text.Trim(), _snapFull, StringComparison.Ordinal) OrElse
                              Not String.Equals(TextBoxShort.Text.Trim(), _snapShort, StringComparison.Ordinal)
        Dim combosChanged = Not String.Equals(CombosSignature(_combos), _snapCombosSig, StringComparison.Ordinal)
        Dim apprChanged = Not SequenceEqualU(_appr, _snapAppr)
        Dim traitsChanged = (changedFlagBits And NpcTemplateHelpers.TraitsAcbsFlagsMask) <> 0UI OrElse
                            (GetFid(TextBoxRace) <> _snapRace) OrElse (GetFid(TextBoxVoice) <> _snapVoice) OrElse
                            (GetFid(TextBoxClass) <> _snapClass) OrElse (GetFid(TextBoxZnam) <> _snapZnam) OrElse
                            CShort(NumDisp.Value) <> _snapDisp OrElse combosChanged OrElse apprChanged
        ' DNAM, level and the SSE-only ACBS offsets ride Use-Stats. Class and Combat Style are
        ' Traits under the historical FNV actor-template field categorization (see
        ' NpcTemplateHelpers); do not move them here without runtime evidence.
        Dim skillsChanged = PlayerSkillsChanged()
        ' El DNAM del borrador se compone ACA, antes de registrar el override y antes de aplicar: los dos
        ' salen del mismo bloque.
        If skillsChanged Then ComposePlayerSkills()
        Dim statsChanged = (changedFlagBits And NpcTemplateHelpers.StatsAcbsFlagsMask) <> 0UI OrElse CurrentLevelRaw() <> _snapLevel OrElse
                           CShort(NumXp.Value) <> _snapXp OrElse CUShort(NumCalcMin.Value) <> _snapCalcMin OrElse
                           CUShort(NumCalcMax.Value) <> _snapCalcMax OrElse skillsChanged OrElse SseAcbsOffsetsChanged()
        Dim keywordsChanged = Not SequenceEqualU(_keywords, _snapKeywords)
        Dim factionsChanged = Not FactionsEqual(_borrador.Factions, _snapRecord.Factions)
        Dim inventoryChanged = Not InventoryEqual(_borrador.Items, _snapRecord.Items)
        Dim perksChanged = Not PerksEqual(_borrador.Perks, _snapRecord.Perks)
        Dim actorEffectsChanged = Not SequenceEqualU(_actorEffects, _snapActorEffects)
        Dim propertiesChanged = Not PropertiesEqual(BorradorFo4(), TryCast(_snapRecord, Canon.NpcFO4))
        Dim defaultOutfitChanged = GetFid(TextBoxDefaultOutfit) <> _snapDefaultOutfit
        Dim sleepOutfitChanged = GetFid(TextBoxSleepOutfit) <> _snapSleepOutfit

        Dim lvlnPick As Func(Of UInteger, UInteger) = AddressOf _mainForm.ResolveLvlnPick_Friend
        Dim categoriesToOwn As New List(Of NPC_TemplateCategory)
        If traitsChanged Then categoriesToOwn.Add(NPC_TemplateCategory.Traits)
        If baseDataChanged Then categoriesToOwn.Add(NPC_TemplateCategory.BaseData)
        If statsChanged Then categoriesToOwn.Add(NPC_TemplateCategory.Stats)
        If keywordsChanged Then categoriesToOwn.Add(NPC_TemplateCategory.Keywords)
        If factionsChanged Then categoriesToOwn.Add(NPC_TemplateCategory.Factions)
        If inventoryChanged Then categoriesToOwn.Add(NPC_TemplateCategory.Inventory)
        If actorEffectsChanged Then categoriesToOwn.Add(NPC_TemplateCategory.SpellList)

        ' Validate every changed bucket before mutating any of them or registering an override. A broken chain
        ' must fail closed; otherwise Use-X remains active and the engine silently overwrites the edit.
        For Each category In categoriesToOwn
            Dim probe = NpcTemplateMaterializer.ProbeCategoryOwn(_npc, category, _getParsedNpc, lvlnPick)
            If probe.Outcome = NpcTemplateMaterializer.MaterializeOutcome.Unresolvable OrElse
               probe.Outcome = NpcTemplateMaterializer.MaterializeOutcome.UnsupportedCategory Then
                MessageBox.Show(Me,
                                $"Cannot make {NpcManagerFormat.GetTemplateCategoryLabel(category)} editable because its template chain could not be resolved." &
                                Environment.NewLine & $"Nothing was changed. Details: {probe.LogReason}",
                                "Template chain cannot be materialized", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
        Next

        ' Mutate the LIVE in-memory NPC_Data (render cache) so the preview reflects the edit immediately.
        ' Materialize → clear Use-X flag for each edited category BEFORE applying (engine CopyFromTemplate rule).
        ' Traits uses the SAME skip-overlay-owned rule as the save apply so the preview matches the written record
        ' (overlay-owned appearance fields come from the LM overlay at render, not the template).
        ' Traits: el resolver de LVLN es lo que evita que una cadena que termina en una lista nivelada se
        ' quede sin materializar y ADEMÁS pierda el bit (= NPC sin cara). Con varias hojas se FIJA una —
        ' la que se está previsualizando— y el NPC deja de re-sortear plantilla: es exactamente para lo que
        ' está el editor (volver concreto a un NPC genérico), así que no se bloquea ni se pregunta nada.
        If traitsChanged Then
            NpcTemplateMaterializer.MakeCategoryOwn(_npc, NPC_TemplateCategory.Traits, _getParsedNpc,
                                                    skipOverlayOwned:=_mainForm.NpcHasOverlay(_npcFormID),
                                                    resolveLvlnPick:=AddressOf _mainForm.ResolveLvlnPick_Friend)
        End If
        If baseDataChanged Then NpcTemplateMaterializer.MakeCategoryOwn(_npc, NPC_TemplateCategory.BaseData, _getParsedNpc, resolveLvlnPick:=lvlnPick)
        If statsChanged Then NpcTemplateMaterializer.MakeCategoryOwn(_npc, NPC_TemplateCategory.Stats, _getParsedNpc, resolveLvlnPick:=lvlnPick)
        If keywordsChanged Then NpcTemplateMaterializer.MakeCategoryOwn(_npc, NPC_TemplateCategory.Keywords, _getParsedNpc, resolveLvlnPick:=lvlnPick)
        If factionsChanged Then NpcTemplateMaterializer.MakeCategoryOwn(_npc, NPC_TemplateCategory.Factions, _getParsedNpc, resolveLvlnPick:=lvlnPick)
        If inventoryChanged Then NpcTemplateMaterializer.MakeCategoryOwn(_npc, NPC_TemplateCategory.Inventory, _getParsedNpc, resolveLvlnPick:=lvlnPick)
        If actorEffectsChanged Then NpcTemplateMaterializer.MakeCategoryOwn(_npc, NPC_TemplateCategory.SpellList, _getParsedNpc, resolveLvlnPick:=lvlnPick)

        ' Persist only after every preflight passed and each required bucket was made own.
        RegisterRecordOverride(newFlags, flagsChanged, baseDataChanged, traitsChanged, statsChanged,
                               keywordsChanged, apprChanged, factionsChanged, inventoryChanged, combosChanged,
                               perksChanged, actorEffectsChanged, propertiesChanged)

        ApplyToNpc(newFlags, changedFlagBits, baseDataChanged, traitsChanged, statsChanged, skillsChanged,
                   keywordsChanged, factionsChanged, inventoryChanged, perksChanged,
                   actorEffectsChanged, propertiesChanged)

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

        _hasChanges = flagsChanged OrElse baseDataChanged OrElse traitsChanged OrElse statsChanged OrElse
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
    Private Sub RegisterRecordOverride(newFlags As UInteger, flagsChanged As Boolean, baseDataChanged As Boolean, traitsChanged As Boolean,
                                       statsChanged As Boolean, keywordsChanged As Boolean, apprChanged As Boolean,
                                       factionsChanged As Boolean, inventoryChanged As Boolean, combosChanged As Boolean,
                                       perksChanged As Boolean, actorEffectsChanged As Boolean, propertiesChanged As Boolean)
        If Not (flagsChanged OrElse baseDataChanged OrElse traitsChanged OrElse statsChanged OrElse keywordsChanged OrElse
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
        ' SSE-only ACBS offsets + DNAM Player Skills (both no-ops on FO4: the controls are hidden and never move).
        If CShort(NumMagickaOff.Value) <> _snapMagickaOff Then ov.MagickaOffset = CShort(NumMagickaOff.Value)
        If CShort(NumStaminaOff.Value) <> _snapStaminaOff Then ov.StaminaOffset = CShort(NumStaminaOff.Value)
        If CShort(NumHealthOff.Value) <> _snapHealthOff Then ov.HealthOffset = CShort(NumHealthOff.Value)
        If CUShort(NumSpeedMult.Value) <> _snapSpeedMult Then ov.SpeedMultiplier = CUShort(NumSpeedMult.Value)
        ' El DNAM ya está compuesto en el borrador (OnOk lo hace antes de llegar acá); el override se lleva
        ' un clon para no compartir nodos con lo que el editor deja vivo.
        If PlayerSkillsChanged() Then ov.SsePlayerSkills = TryCast(_borrador.Copia(), Canon.NpcSSE)
        If GetFid(TextBoxRace) <> _snapRace Then ov.RaceFormID = GetFid(TextBoxRace)
        If GetFid(TextBoxVoice) <> _snapVoice Then ov.VoiceFormID = GetFid(TextBoxVoice)
        If GetFid(TextBoxClass) <> _snapClass Then ov.ClassFormID = GetFid(TextBoxClass)
        If GetFid(TextBoxZnam) <> _snapZnam Then ov.CombatStyleFormID = GetFid(TextBoxZnam)
        If keywordsChanged Then ov.Keywords = New List(Of UInteger)(_keywords)
        If apprChanged Then ov.AttachParentSlots = New List(Of UInteger)(_appr)
        ' Las listas por fila salen de UN clon del borrador: el override se queda con nodos propios, que no
        ' comparte con el editor ni con el record vivo.
        If factionsChanged OrElse inventoryChanged OrElse perksChanged OrElse propertiesChanged Then
            Dim guardado = _borrador.Copia()
            If factionsChanged Then ov.Factions = guardado.Factions.ToList()
            If inventoryChanged Then ov.Inventory = guardado.Items.ToList()
            If perksChanged Then ov.Perks = guardado.Perks.ToList()
            If propertiesChanged Then
                Dim gf = TryCast(guardado, Canon.NpcFO4)
                If gf IsNot Nothing Then ov.Properties = gf.Properties2.ToList()
            End If
        End If
        If actorEffectsChanged Then ov.ActorEffects = New List(Of UInteger)(_actorEffects)
        If combosChanged Then ov.ObjectTemplateCombinations = CombosParaElOverride()
        ov.TraitsChanged = ov.TraitsChanged OrElse traitsChanged
        ov.BaseDataChanged = ov.BaseDataChanged OrElse baseDataChanged
        ov.StatsChanged = ov.StatsChanged OrElse statsChanged

        _mainForm.SetNpcRecordOverride(_npcFormID, ov)
    End Sub

    ''' <summary>Escribe un texto en el record, o SACA el subrecord cuando el texto queda vacio y el
    ''' record no lo traia. Escribirlo vacio no es lo mismo: crea un subrecord con la cadena vacia.</summary>
    Private Shared Sub EscribirTextoOSacar(destino As Canon.INpc, texto As String, yaLoTraia As Boolean,
                                           firma As String, escribir As Action(Of String))
        If texto.Length > 0 OrElse yaLoTraia Then
            escribir(texto)
        Else
            destino.QuitarSubrecord(firma)
        End If
    End Sub

    ''' <summary>Escribe una referencia en el record, o SACA el subrecord cuando vale cero: el picker
    ''' vacio significa "ninguno", no "una referencia a cero".</summary>
    Private Shared Sub EscribirReferenciaOSacar(destino As Canon.INpc, fid As UInteger,
                                                firma As String, escribir As Action(Of UInteger))
        If fid <> 0UI Then
            escribir(fid)
        Else
            destino.QuitarSubrecord(firma)
        End If
    End Sub

    ''' <summary>Write the panel state into <see cref="_npc"/> (in place — the caller's live cache instance).</summary>
    Private Sub ApplyToNpc(newFlags As UInteger, changedFlagBits As UInteger,
                           baseDataChanged As Boolean, traitsChanged As Boolean, statsChanged As Boolean, skillsChanged As Boolean,
                           keywordsChanged As Boolean, factionsChanged As Boolean, inventoryChanged As Boolean,
                           perksChanged As Boolean, actorEffectsChanged As Boolean, propertiesChanged As Boolean)
        ' General identity.
        If baseDataChanged Then
            ' Escribir el campo CREA el subrecord; con el texto vacio y el record sin traerlo, se saca,
            ' que es distinto de emitir un FULL vacio.
            EscribirTextoOSacar(_npc.Record, TextBoxFull.Text.Trim(), _npc.Record.NamePresente,
                                "FULL", Sub(v) _npc.Record.Name = v)
            EscribirTextoOSacar(_npc.Record, TextBoxShort.Text.Trim(), _npc.Record.ShortNamePresente,
                                "SHRT", Sub(v) _npc.Record.ShortName = v)
        End If

        If traitsChanged Then
            EscribirReferenciaOSacar(_npc.Record, GetFid(TextBoxRace), "RNAM", Sub(v) _npc.Record.Race = v)
            EscribirReferenciaOSacar(_npc.Record, GetFid(TextBoxVoice), "VTCK", Sub(v) _npc.Record.Voice = v)
            EscribirReferenciaOSacar(_npc.Record, GetFid(TextBoxClass), "CNAM", Sub(v) _npc.Record.[Class] = v)
            EscribirReferenciaOSacar(_npc.Record, GetFid(TextBoxZnam), "ZNAM", Sub(v) _npc.Record.CombatStyle = v)
        End If

        If traitsChanged Then _npc.Record.PonerBaseDeDisposicion(CShort(NumDisp.Value))
        If changedFlagBits <> 0UI Then
            _npc.Record.ConfigurationFlags = (_npc.Record.ConfigurationFlags And Not changedFlagBits) Or
                                             (newFlags And changedFlagBits)
        End If
        If statsChanged Then
            _npc.Record.PonerNivelDeConfiguracion(CurrentLevelRaw())
            _npc.Record.ConfigurationCalcMinLevel = CUShort(NumCalcMin.Value)
            _npc.Record.ConfigurationCalcMaxLevel = CUShort(NumCalcMax.Value)
            Dim nf4 = TryCast(_npc.Record, Canon.NpcFO4)
            If nf4 IsNot Nothing Then nf4.ConfigurationXPValueOffset = CShort(NumXp.Value)
            ' Los desplazamientos de ACBS son de Skyrim. En Fallout 4 los controles estan ocultos y nunca se
            ' sembraron del record, asi que escribirlos pondria ceros donde el record tiene otra cosa.
            Dim ns4 = TryCast(_npc.Record, Canon.NpcSSE)
            If _isSkyrim AndAlso ns4 IsNot Nothing Then
                ns4.ConfigurationMagickaOffset = CShort(NumMagickaOff.Value)
                ns4.ConfigurationStaminaOffset = CShort(NumStaminaOff.Value)
                ns4.ConfigurationHealthOffset = CShort(NumHealthOff.Value)
                ns4.ConfigurationSpeedMultiplier = CUShort(NumSpeedMult.Value)
                ' DNAM: solo con una edicion real, para que un NPC cuyo DNAM no se pudo modelar conserve el
                ' bloque tal cual en vez de que lo reemplace uno bien formado lleno de ceros. Viaja como
                ' subrecord entero, asi que lo que ningun control muestra pasa sin tocarse.
                If skillsChanged Then _npc.Record.CopiarSubrecord(_borrador, "DNAM")
            End If
        End If

        If keywordsChanged Then _npc.Record.PonerPalabrasClave(_keywords)
        If traitsChanged Then _npc.Record.PonerRanurasDeEnganche(_appr)
        If factionsChanged Then _npc.Record.PonerFacciones(_borrador.Factions)
        If inventoryChanged Then _npc.Record.PonerInventario(_borrador.Items)
        If perksChanged Then _npc.Record.PonerVentajas(_borrador.Perks)
        If actorEffectsChanged Then _npc.Record.PonerEfectosDeActor(_actorEffects)
        If propertiesChanged Then _npc.Record.PonerPropiedades(PropiedadesDelBorrador())
        If traitsChanged Then _npc.Record.ReemplazarCombinations(_combos)
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

    ''' <summary>Una copia independiente de la lista, para que el override guardado no comparta nodos con
    ''' lo que el editor siga tocando. Las combinaciones se cuelgan de un NPC_ aparte -otra copia del que las
    ''' trajo- que sólo existe para sostenerlas.</summary>
    Private Function CombosParaElOverride() As List(Of Canon.IBloque_Combinations)
        Dim host = _comboHost.Copia()
        If host Is Nothing Then Return New List(Of Canon.IBloque_Combinations)
        host.ReemplazarCombinations(_combos)
        Return New List(Of Canon.IBloque_Combinations)(host.CombinacionesDelNpc())
    End Function

    ''' <summary>Firma de contenido de la lista de combinaciones, para detectar cambios: nombre, banderas,
    ''' includes, propiedades y palabras clave. Sensible al orden.</summary>
    Private Shared Function CombosSignature(list As List(Of Canon.IBloque_Combinations)) As String
        Dim parts As New List(Of String)
        For Each c In list
            If c Is Nothing Then Continue For
            Dim incl = String.Join("|", c.Includes.Select(Function(i) i.IncludeMod.ToString("X8") & ":" & i.IncludeAttachPointIndex.ToString(CultureInfo.InvariantCulture) & ":" & If(i.IncludeOptional, "1", "0") & If(i.IncludeDonTUseAll, "1", "0")))
            Dim props = String.Join("|", c.Properties.Select(Function(v) DescribirPropiedad(v)))
            Dim kwds = String.Join(",", c.Keywords.Select(Function(k) k.Keyword.ToString("X8")))
            parts.Add($"{c.CombinationName}#{If(c.CombinationEditorOnly, 1, 0)}#{If(c.ObjectModTemplateItemDefault, 1, 0)}#{incl}#{props}#{kwds}")
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

    Private Shared Function SequenceEqualU(a As List(Of UInteger), b As List(Of UInteger)) As Boolean
        If a.Count <> b.Count Then Return False
        For i = 0 To a.Count - 1
            If a(i) <> b(i) Then Return False
        Next
        Return True
    End Function

    Private Shared Function FactionsEqual(a As IReadOnlyList(Of Canon.INpc_Factions),
                                          b As IReadOnlyList(Of Canon.INpc_Factions)) As Boolean
        If a.Count <> b.Count Then Return False
        For i = 0 To a.Count - 1
            If a(i).Faction <> b(i).Faction OrElse a(i).FactionRank <> b(i).FactionRank Then Return False
        Next
        Return True
    End Function

    Private Shared Function InventoryEqual(a As IReadOnlyList(Of Canon.INpc_Items),
                                           b As IReadOnlyList(Of Canon.INpc_Items)) As Boolean
        If a.Count <> b.Count Then Return False
        For i = 0 To a.Count - 1
            If a(i).Item <> b(i).Item OrElse a(i).ItemCount <> b(i).ItemCount Then Return False
        Next
        Return True
    End Function

    Private Shared Function PerksEqual(a As IReadOnlyList(Of Canon.INpc_Perks),
                                       b As IReadOnlyList(Of Canon.INpc_Perks)) As Boolean
        If a.Count <> b.Count Then Return False
        For i = 0 To a.Count - 1
            If a(i).Perk <> b(i).Perk OrElse a(i).PerkRank <> b(i).PerkRank Then Return False
        Next
        Return True
    End Function

    ''' <summary>Compara los PRPS de dos records de Fallout 4. En Skyrim no hay subrecord y los dos llegan
    ''' Nothing: no hay nada que haya podido cambiar.</summary>
    Private Shared Function PropertiesEqual(a As Canon.NpcFO4, b As Canon.NpcFO4) As Boolean
        If a Is Nothing OrElse b Is Nothing Then Return a Is Nothing AndAlso b Is Nothing
        If a.Properties2.Count <> b.Properties2.Count Then Return False
        For i = 0 To a.Properties2.Count - 1
            If a.Properties2(i).PropertyActorValue <> b.Properties2(i).PropertyActorValue Then Return False
            If a.Properties2(i).PropertyValue <> b.Properties2(i).PropertyValue Then Return False
        Next
        Return True
    End Function

End Class
