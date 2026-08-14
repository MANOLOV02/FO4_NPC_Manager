Imports System.IO
Imports System.Threading.Tasks
Imports FO4_Base_Library

''' <summary>Preflight dialog: el usuario apunta a Fallout4.exe y elige qué plugins cargar
''' (activos pre-tickeados; inactivos también tickeables). Implicit masters (Fallout4.esm + DLCs)
''' salen pre-tickeados pero NO bloqueados — si el usuario los destilda problema suyo.
'''
''' On OK: persiste FO4ExePath, dispara la carga de plugins + BA2/BSA con progress bar visible
''' (UI no se congela: Task.Run + IProgress(Of T) marshaling). Cuando termina, expone
''' <see cref="LoadedPluginManager"/> + <see cref="LoadedDataPath"/> para que MainForm los consuma
''' sin re-cargar.</summary>
Public Class Preflight_Form

    ' Fixed resolution for the byte-progress (Detail) bar in BOTH phases (plugin bytes, then mounted-
    ' archive bytes). BytesDone/BytesTotal are Longs whose total can exceed Int32 (Fallout4.esm alone
    ' ~300MB, full set > 2GB), so we map the ratio onto this fixed Integer scale instead of feeding raw
    ' byte counts into ProgressBar.Value.
    Private Const DetailBarScale As Integer = 1000

    Private _activeOrder As New List(Of String)()

    ' Master list of plugins found in Data\, in stable display order (actives first per load
    ' order, then inactives alphabetical). Survives filter changes so OK can rebuild
    ' SelectedPlugins in load order even when the ListView only contains filtered rows.
    Private ReadOnly _allRows As New List(Of PluginRow)

    ' Set of currently-checked plugin filenames (case-insensitive). Kept in sync with the
    ' ListView checks but lives separately so filtering can hide/show rows without losing
    ' check state — ListView re-populate during filter would otherwise wipe Checked flags.
    Private ReadOnly _checkedPlugins As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ' Re-entrancy guard around programmatic ItemChecked: set during ApplyFilter's bulk repopulate
    ' and during Mark/Unmark loops so the user-driven OnItemChecked handler doesn't double-track
    ' or fight the source-of-truth update.
    Private _suspendItemChecked As Boolean = False

    ' Direct master list per plugin filename, read ONCE via a cheap TES4-header-only load on a
    ' background sweep after the list is built. Every subsequent validation is pure in-memory
    ' lookup against this — no disk I/O when the user marks plugins (single toggle or bulk button).
    Private ReadOnly _mastersByName As New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)

    ' All plugin filenames physically present in Data\ (case-insensitive). Lets validation tell
    ' "master missing on disk" apart from "master present on disk but not checked".
    Private ReadOnly _presentFiles As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ' Checked plugins that have at least one direct master not satisfied (missing on disk OR not
    ' checked). Drives the red row color and the OK gate. Recomputed in-memory on every check change.
    Private ReadOnly _brokenPlugins As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ' True once the background masters sweep has populated _mastersByName. OK stays disabled until
    ' then so we never enable a selection we haven't validated.
    Private _mastersReady As Boolean = False

    ' Monotonic token so a stale background sweep (user re-browsed mid-sweep) discards its result.
    Private _mastersSweepToken As Integer = 0

    Private Structure PluginRow
        Public Name As String
        Public IsActive As Boolean
    End Structure

    ''' <summary>Plugins picked by the user, in the order they appear in the ListView.</summary>
    Public ReadOnly Property SelectedPlugins As New List(Of String)

    ''' <summary>PluginManager fully loaded with the user's selection. Null until OK + load completes.</summary>
    Public Property LoadedPluginManager As PluginManager

    ''' <summary>Data path resolved from FO4ExePath. Empty until OK + load completes.</summary>
    Public Property LoadedDataPath As String = ""

    ''' <summary>NPC_Manager auto-generated plugins detected on disk during preflight. MainForm
    ''' uses this as the seed for its Save-ESP cache so the first Save dialog opens without a
    ''' visible disk scan. Null until OK + load completes.</summary>
    Public Property LoadedAutoGenPlugins As List(Of SaveEsp_Form.ExistingPlugin)

    ''' <summary>Per-plugin <c>.bssliders</c> sidecars discovered next to the user-selected
    ''' plugins. Key = plugin filename (e.g. <c>"NPC_Manager.esp"</c>, case-insensitive); value
    ''' = parsed sidecar. Plugins without a sidecar on disk are absent from the dict. MainForm
    ''' uses this to hydrate <c>_appliedPresets</c> with BodyMorphs + SkinTemplateId when the
    ''' user opens an NPC whose record originates from one of these plugins.</summary>
    Public Property LoadedSidecars As Dictionary(Of String, BssliderSidecar.SidecarFile)

    Private Sub Preflight_Form_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        ' Seed the game selector from the persisted config, then let an already-configured exe filename
        ' auto-correct it (Fallout4.exe → FO4, SkyrimSE.exe → Skyrim). This keeps returning FO4 users on
        ' FO4 even though the shared library's Game default is Skyrim. SelectedIndexChanged persists the
        ' choice back into Config_App.Current.Game.
        ComboBoxGame.SelectedIndex = CInt(Config_App.Current.Game)
        AutoDetectGameFromExe(Config_App.Current.FO4ExePath)
        TextBoxExePath.Text = Config_App.Current.FO4ExePath
        RefreshPathRows()
        RefreshPluginList()
    End Sub

    Private Sub ButtonBrowse_Click(sender As Object, e As EventArgs) Handles ButtonBrowse.Click
        Dim chosen = BrowseForExe(IO.Path.GetDirectoryName(TextBoxExePath.Text), Config_App.Current.Game)
        If String.IsNullOrEmpty(chosen) Then Return
        Config_App.Current.FO4ExePath = chosen
        TextBoxExePath.Text = chosen
        ' Sync the game combo to the exe the user just picked (a Skyrim exe flips the selector to SSE).
        AutoDetectGameFromExe(chosen)
        ' El exe es de donde sale la variante (plana / VR) y por lo tanto los candidatos de carpeta, así que
        ' las dos filas de abajo se recalculan. Un override que el usuario haya fijado NO se toca: lo fijó
        ' porque el automático no le servía, y borrárselo al re-elegir el exe sería pisarle la config.
        RefreshPathRows()
        RefreshPluginList()
    End Sub

    ' ==============================================================================================
    ' Plugins.txt / carpeta de INIs — automático con opción de fijarlo a mano, persistido POR JUEGO
    ' ==============================================================================================

    ''' <summary>Vuelca en las dos filas lo que resolvió <see cref="GamePathsResolver"/> y deja dicho de
    ''' dónde salió cada valor. Es barato: la resolución está memoizada por (exe, juego, overrides), así que
    ''' llamar a esto en cada cambio de la UI no vuelve a tocar el disco.</summary>
    Private Sub RefreshPathRows()
        Dim r = GamePathsResolver.Resolve()

        FillPathRow(TextBoxPluginsTxt, ButtonAutoPluginsTxt, r.PluginsTxtPath, r.PluginsTxtOrigin)
        FillPathRow(TextBoxIniDir, ButtonAutoIniDir, r.IniDir, r.IniDirOrigin)

        LabelPathsStatus.Text = r.StatusLine
        LabelPathsStatus.ForeColor = If(r.Problem <> "", Color.Firebrick, SystemColors.GrayText)
    End Sub

    ''' <summary>Una fila. El COLOR es el que dice si el valor es del usuario o derivado: gris = lo dedujo la
    ''' app, negro = lo fijaste vos. "Auto" sólo se habilita cuando hay algo que devolver a automático, así
    ''' que un usuario sin override no puede tocar un botón que no haría nada.</summary>
    Private Shared Sub FillPathRow(box As TextBox, autoButton As Button, value As String,
                                   origin As GamePathsResolver.PathOrigin)
        Select Case origin
            Case GamePathsResolver.PathOrigin.UserOverride
                box.Text = value
                box.ForeColor = SystemColors.WindowText
                autoButton.Enabled = True
            Case GamePathsResolver.PathOrigin.AutoTable
                box.Text = value
                box.ForeColor = SystemColors.GrayText
                autoButton.Enabled = False
            Case Else
                box.Text = ""
                box.PlaceholderText = "Not found — click Browse..."
                box.ForeColor = SystemColors.GrayText
                autoButton.Enabled = False
        End Select
    End Sub

    Private Sub ButtonBrowsePluginsTxt_Click(sender As Object, e As EventArgs) Handles ButtonBrowsePluginsTxt.Click
        Using dlg As New OpenFileDialog()
            dlg.Title = "Select the game's Plugins.txt"
            dlg.Filter = "Plugins.txt|Plugins.txt|Text files (*.txt)|*.txt|All files (*.*)|*.*"
            dlg.CheckFileExists = True
            dlg.CheckPathExists = True
            dlg.Multiselect = False
            Dim start = InitialDirFor(TextBoxPluginsTxt.Text)
            If start <> "" Then dlg.InitialDirectory = start
            If dlg.ShowDialog() <> DialogResult.OK Then Return
            Config_App.Current.SetActivePluginsTxtOverride(dlg.FileName)
        End Using
        RefreshPathRows()
        ' El load order acaba de cambiar ⇒ la clasificación activo/inactivo de la lista ya no vale.
        RefreshPluginList()
    End Sub

    Private Sub ButtonAutoPluginsTxt_Click(sender As Object, e As EventArgs) Handles ButtonAutoPluginsTxt.Click
        Config_App.Current.SetActivePluginsTxtOverride("")
        RefreshPathRows()
        RefreshPluginList()
    End Sub

    Private Sub ButtonBrowseIniDir_Click(sender As Object, e As EventArgs) Handles ButtonBrowseIniDir.Click
        Using dlg As New FolderBrowserDialog()
            dlg.Description = $"Select the folder holding {GamePathsResolver.IniBaseName(Config_App.Current.Game)}.ini"
            dlg.UseDescriptionForTitle = True
            Dim start = If(TextBoxIniDir.Text <> "" AndAlso Directory.Exists(TextBoxIniDir.Text), TextBoxIniDir.Text, "")
            If start <> "" Then dlg.SelectedPath = start
            If dlg.ShowDialog() <> DialogResult.OK Then Return
            Config_App.Current.SetActiveGameIniDirOverride(dlg.SelectedPath)
        End Using
        RefreshPathRows()
    End Sub

    Private Sub ButtonAutoIniDir_Click(sender As Object, e As EventArgs) Handles ButtonAutoIniDir.Click
        Config_App.Current.SetActiveGameIniDirOverride("")
        RefreshPathRows()
    End Sub

    ''' <summary>Carpeta desde la que abrir el diálogo, o "" si no hay ninguna usable. El valor de la caja
    ''' puede ser una ruta que NO existe (el automático la compone igual para poder mostrarla), así que se
    ''' comprueba antes de pasársela al diálogo.</summary>
    Private Shared Function InitialDirFor(currentPath As String) As String
        If String.IsNullOrEmpty(currentPath) Then Return ""
        Try
            Dim dir = IO.Path.GetDirectoryName(currentPath)
            If Not String.IsNullOrEmpty(dir) AndAlso Directory.Exists(dir) Then Return dir
        Catch
        End Try
        Return ""
    End Function

    ''' <summary>Game selector changed: persist the choice into Config_App.Current.Game. The plugin list
    ''' depends on the Data folder (derived from the exe path), not the game, so it is NOT re-enumerated
    ''' here — a game switch on the same exe keeps the list; picking the other game's exe re-lists via
    ''' ButtonBrowse. The final encoding init for the chosen game happens in ButtonOk before loading.</summary>
    Private Sub ComboBoxGame_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBoxGame.SelectedIndexChanged
        If ComboBoxGame.SelectedIndex < 0 Then Return
        Config_App.Current.Game = CType(ComboBoxGame.SelectedIndex, Config_App.Game_Enum)
        ' Plugins.txt y los .ini SÍ dependen del juego, y los overrides son por juego: cambiar de juego
        ' cambia de slot, así que las dos filas se repintan con lo que corresponda al juego nuevo.
        RefreshPathRows()
    End Sub

    ''' <summary>Set the game combo from an exe filename: contains "fallout4" → FO4, "skyrim"/"sse" →
    ''' Skyrim. No-op when the name matches neither, so a non-standard exe name leaves the current
    ''' selection intact. Mirror of Wardrobe_Manager Config_Form's exe→game auto-detect.</summary>
    Private Sub AutoDetectGameFromExe(exePath As String)
        If String.IsNullOrEmpty(exePath) Then Return
        Dim name = IO.Path.GetFileName(exePath).ToLowerInvariant()
        If name.Contains("fallout4") Then
            ComboBoxGame.SelectedIndex = CInt(Config_App.Game_Enum.Fallout4)
        ElseIf name.Contains("skyrim") OrElse name.Contains("sse") Then
            ComboBoxGame.SelectedIndex = CInt(Config_App.Game_Enum.Skyrim)
        End If
    End Sub

    Private Shared Function BrowseForExe(initialDir As String, game As Config_App.Game_Enum) As String
        Dim exeName = If(game = Config_App.Game_Enum.Skyrim, "SkyrimSE.exe", "Fallout4.exe")
        Using dlg As New OpenFileDialog()
            dlg.Title = $"Select {exeName}"
            dlg.Filter = $"{exeName}|{exeName}|EXE files (*.exe)|*.exe"
            dlg.CheckFileExists = True
            dlg.CheckPathExists = True
            dlg.Multiselect = False
            If Not String.IsNullOrEmpty(initialDir) AndAlso Directory.Exists(initialDir) Then
                dlg.InitialDirectory = initialDir
            End If
            If dlg.ShowDialog() = DialogResult.OK Then Return dlg.FileName
        End Using
        Return ""
    End Function

    ''' <summary>Re-scan Data\ for plugins, classify each (active / inactive), pre-check the active
    ''' ones (which already include Fallout4.esm + DLCs courtesy of <see cref="PluginManager.ReadActiveLoadOrder"/>).
    ''' Order: active load order first (engine order), then inactives sorted alphabetically.
    ''' Result is cached in _allRows + _checkedPlugins; ApplyFilter renders the visible subset.</summary>
    Private Sub RefreshPluginList()
        _allRows.Clear()
        _checkedPlugins.Clear()
        _mastersByName.Clear()
        _presentFiles.Clear()
        _brokenPlugins.Clear()
        _mastersReady = False
        ListViewPlugins.Items.Clear()
        ButtonOk.Enabled = False
        LabelStatus.Text = ""

        If Not Config_App.Check_FOFolder() Then
            LabelStatus.Text = "Pick the game .exe to enumerate plugins."
            Return
        End If

        Dim dataPath = Config_App.Current.FO4EDataPath
        _activeOrder = PluginManager.ReadActiveLoadOrder()

        Dim allPluginFiles =
            FilesDictionary_class.EnumerateFilesWithSymlinkSupport(dataPath, "*.esp;*.esm;*.esl", False).
                Select(Function(p) IO.Path.GetFileName(p)).
                ToList()

        Dim allPluginsSet = New HashSet(Of String)(allPluginFiles, StringComparer.OrdinalIgnoreCase)
        For Each f In allPluginsSet : _presentFiles.Add(f) : Next
        Dim rendered = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        For Each pluginName In _activeOrder
            If allPluginsSet.Contains(pluginName) AndAlso Not rendered.Contains(pluginName) Then
                _allRows.Add(New PluginRow With {.Name = pluginName, .IsActive = True})
                _checkedPlugins.Add(pluginName)
                rendered.Add(pluginName)
            End If
        Next

        Dim inactives = allPluginFiles.
            Where(Function(p) Not rendered.Contains(p)).
            OrderBy(Function(p) p, StringComparer.OrdinalIgnoreCase)
        For Each pluginName In inactives
            _allRows.Add(New PluginRow With {.Name = pluginName, .IsActive = False})
        Next

        ' Last selection the user confirmed with OK for THIS game wins over the actives default (see
        ' NPC_Config.PreflightSelection_FO4/_SSE). Filtered against what's actually in Data\ — a plugin
        ' saved last run and since uninstalled just drops out. If nothing survives (never saved, opted
        ' out, or the whole selection belongs to another install) we keep the actives already ticked
        ' above, so the first run and a moved install both still open on the engine load order.
        '
        ' The checkbox MIRRORS what we found: ticked = "there was a stored selection and you're looking
        ' at it" (so pressing OK keeps it up to date), unticked = "nothing stored, this is the actives
        ' default". Unticking it and pressing OK is the opt-out — see ButtonOk_Click.
        Dim savedSelection = NPC_Config.GetPreflightSelection(Config_App.Current.Game).
            Where(Function(n) allPluginsSet.Contains(n)).
            ToList()
        If savedSelection.Count > 0 Then
            _checkedPlugins.Clear()
            For Each pluginName In savedSelection : _checkedPlugins.Add(pluginName) : Next
        End If
        CheckBoxPersistSelection.Checked = (savedSelection.Count > 0)

        ApplyFilter()

        ' Masters are needed before we can validate dependencies and enable OK. Read them off the
        ' UI thread (one cheap TES4-header read per plugin) so the dialog stays responsive; OK is
        ' gated by RecomputeValidation until the sweep finishes and on every check change after.
        BeginMastersSweep(dataPath)
    End Sub

    ''' <summary>Background pass that reads each plugin's direct master list (TES4 header only) and,
    ''' on completion, runs the first validation. Tokenized so a re-browse mid-sweep discards the
    ''' stale result. After this, all marking is validated in-memory with no further disk access.</summary>
    Private Async Sub BeginMastersSweep(dataPath As String)
        _mastersSweepToken += 1
        Dim token = _mastersSweepToken
        Dim names = _allRows.Select(Function(r) r.Name).ToList()
        If names.Count = 0 Then
            _mastersReady = True
            RecomputeValidation()
            Return
        End If

        LabelStatus.Text = $"Reading plugin masters (0/{names.Count})..."
        Dim prog As New Progress(Of Integer)(
            Sub(done) LabelStatus.Text = $"Reading plugin masters ({done}/{names.Count})...")

        Try
            Dim result = Await Task.Run(Function() ReadAllMasters(dataPath, names, prog))

            ' Discard if a newer sweep started (user re-browsed) or the form is gone.
            If token <> _mastersSweepToken OrElse IsDisposed Then Return

            _mastersByName.Clear()
            For Each kvp In result : _mastersByName(kvp.Key) = kvp.Value : Next
            _mastersReady = True
            RecomputeValidation()
        Catch ex As Exception
            If token <> _mastersSweepToken OrElse IsDisposed Then Return
            Logger.LogLazy(Function() $"[PREFLIGHT] Masters sweep failed: {ex.Message}")
            ' Without master data we can't validate; leave validation off (no false reds) and let
            ' OK enable so the user isn't hard-blocked by a sweep failure.
            _mastersReady = True
            RecomputeValidation()
        End Try
    End Sub

    ''' <summary>Read the direct master list of each named plugin via a TES4-header-only load.
    ''' Runs on a worker thread. A plugin whose header can't be read maps to an empty list (it
    ''' won't be flagged for missing masters — a corrupt plugin is a separate concern).</summary>
    Private Shared Function ReadAllMasters(dataPath As String,
                                           names As List(Of String),
                                           progress As IProgress(Of Integer)) As Dictionary(Of String, List(Of String))
        Dim map As New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)
        Dim i As Integer = 0
        For Each pluginName In names
            i += 1
            Dim masters As New List(Of String)
            Try
                Dim reader As New PluginReader()
                reader.LoadHeaderOnly(IO.Path.Combine(dataPath, pluginName))
                masters = reader.Masters
            Catch
                ' Unreadable/malformed header — treat as no declared masters.
            End Try
            map(pluginName) = masters
            If (i And &H3F) = 0 Then progress?.Report(i)
        Next
        progress?.Report(names.Count)
        Return map
    End Function

    ''' <summary>Recompute which CHECKED plugins have an unsatisfied direct master — i.e. a master
    ''' missing on disk OR present but not checked — then repaint row colors and gate OK. Pure
    ''' in-memory set lookups over the cached master lists, so it's cheap to call on every single
    ''' check toggle and once after each bulk operation. No-op (OK stays disabled) until the masters
    ''' sweep has populated <see cref="_mastersByName"/>.</summary>
    Private Sub RecomputeValidation()
        If Not _mastersReady Then
            ButtonOk.Enabled = False
            ButtonCheckMasters.Visible = False
            Return
        End If

        _brokenPlugins.Clear()
        For Each pluginName In _checkedPlugins
            Dim masters As List(Of String) = Nothing
            If Not _mastersByName.TryGetValue(pluginName, masters) OrElse masters Is Nothing Then Continue For
            For Each m In masters
                If (Not _presentFiles.Contains(m)) OrElse (Not _checkedPlugins.Contains(m)) Then
                    _brokenPlugins.Add(pluginName)
                    Exit For
                End If
            Next
        Next

        For Each it As ListViewItem In ListViewPlugins.Items
            ApplyRowColor(it)
        Next

        ' OK requires (a) at least one plugin checked (empty selection has nothing to load) AND
        ' (b) no checked plugin with unsatisfied masters.
        ButtonOk.Enabled = (_checkedPlugins.Count > 0 AndAlso _brokenPlugins.Count = 0)
        ' "Check Masters" affordance: shown whenever a checked plugin is broken. Clicking ticks the
        ' fixable masters (present on disk) transitively and reports any that are missing on disk.
        ButtonCheckMasters.Visible = (_brokenPlugins.Count > 0)
        UpdateStatusLabel()
    End Sub

    ''' <summary>Walk the master dependency graph of every CHECKED plugin (following only masters
    ''' present on disk, so the walk stops at missing ones) and split the requirements into:
    ''' <paramref name="toCheck"/> = present-on-disk masters not yet checked (the transitive set the
    ''' "Check Masters" button will tick), and <paramref name="missing"/> = masters not present on
    ''' disk, mapped to the plugins that require them. Pure in-memory over the cached master lists.</summary>
    Private Sub CollectMasterClosure(ByRef toCheck As List(Of String),
                                     ByRef missing As Dictionary(Of String, List(Of String)))
        toCheck = New List(Of String)
        missing = New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)
        Dim toCheckSet As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim queue As New Queue(Of String)
        For Each pluginName In _checkedPlugins : queue.Enqueue(pluginName) : Next

        While queue.Count > 0
            Dim p = queue.Dequeue()
            If Not seen.Add(p) Then Continue While
            Dim masters As List(Of String) = Nothing
            If Not _mastersByName.TryGetValue(p, masters) OrElse masters Is Nothing Then Continue While
            For Each m In masters
                If _presentFiles.Contains(m) Then
                    ' Present on disk → fixable. Tick if not already checked; follow its own chain.
                    If Not _checkedPlugins.Contains(m) AndAlso toCheckSet.Add(m) Then toCheck.Add(m)
                    queue.Enqueue(m)
                Else
                    ' Missing on disk → can't be ticked; record which plugin needs it.
                    Dim reqs As List(Of String) = Nothing
                    If Not missing.TryGetValue(m, reqs) Then
                        reqs = New List(Of String)
                        missing(m) = reqs
                    End If
                    If Not reqs.Contains(p) Then reqs.Add(p)
                End If
            Next
        End While
    End Sub

    ''' <summary>Resolve the masters of the checked selection: tick every fixable (present-on-disk)
    ''' master transitively so the broken plugins go green, then — if any required master is missing
    ''' from Data\ — inform the user which files are missing and which plugins need them.</summary>
    Private Sub ButtonCheckMasters_Click(sender As Object, e As EventArgs) Handles ButtonCheckMasters.Click
        Dim toCheck As List(Of String) = Nothing
        Dim missing As Dictionary(Of String, List(Of String)) = Nothing
        CollectMasterClosure(toCheck, missing)

        If toCheck IsNot Nothing AndAlso toCheck.Count > 0 Then
            For Each m In toCheck : _checkedPlugins.Add(m) : Next
            ApplyFilter()          ' reflect the newly-ticked masters in the ListView
            RecomputeValidation()  ' recolor, re-gate OK, refresh button visibility
        End If

        If missing IsNot Nothing AndAlso missing.Count > 0 Then
            Dim sb As New System.Text.StringBuilder()
            sb.AppendLine("These master files are missing from your Data folder and can't be selected:")
            sb.AppendLine()
            For Each kvp In missing.OrderBy(Function(k) k.Key, StringComparer.OrdinalIgnoreCase)
                sb.AppendLine($"  • {kvp.Key}   (required by: {String.Join(", ", kvp.Value)})")
            Next
            sb.AppendLine()
            sb.AppendLine("Install/enable these plugins, or untick the plugins that depend on them.")
            MessageBox.Show(sb.ToString(), "Missing masters", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

    ''' <summary>Color a row from its current state: red if it's a checked plugin with an
    ''' unsatisfied master, gray if inactive, default otherwise. Row.IsActive is carried in
    ''' it.Tag by ApplyFilter so this stays a pure lookup.</summary>
    Private Sub ApplyRowColor(it As ListViewItem)
        If _brokenPlugins.Contains(it.Text) Then
            it.ForeColor = Color.Red
        ElseIf TypeOf it.Tag Is Boolean AndAlso Not CBool(it.Tag) Then
            it.ForeColor = SystemColors.GrayText
        Else
            it.ForeColor = SystemColors.WindowText
        End If
    End Sub

    ''' <summary>Repopulate the ListView from _allRows filtered by TextBoxFilter.Text (case-
    ''' insensitive substring on filename). Restores check state from _checkedPlugins so a
    ''' filter pass doesn't wipe what the user has ticked. Suspends ItemChecked event handling
    ''' during the bulk set so the user-driven handler doesn't fire on every programmatic check.</summary>
    Private Sub ApplyFilter()
        Dim filter As String = If(TextBoxFilter.Text, "").Trim()
        _suspendItemChecked = True
        ListViewPlugins.BeginUpdate()
        Try
            ListViewPlugins.Items.Clear()
            For Each row In _allRows
                If filter.Length > 0 AndAlso row.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
                Dim it As New ListViewItem(row.Name)
                it.SubItems.Add(If(row.IsActive, "Active", "Inactive"))
                it.Tag = row.IsActive
                it.Checked = _checkedPlugins.Contains(row.Name)
                ApplyRowColor(it)
                ListViewPlugins.Items.Add(it)
            Next
        Finally
            ListViewPlugins.EndUpdate()
            _suspendItemChecked = False
        End Try

        UpdateStatusLabel()
    End Sub

    ''' <summary>Refresh the status line: counts of shown/total/active/checked plus a warning
    ''' suffix when some checked plugins have missing masters. Single source for the label so the
    ''' filter, check toggles and bulk buttons all read consistently.</summary>
    Private Sub UpdateStatusLabel()
        Dim activeCount As Integer = _allRows.Where(Function(r) r.IsActive).Count()
        Dim filter As String = If(TextBoxFilter.Text, "").Trim()
        Dim brokenSuffix As String = If(_brokenPlugins.Count > 0,
                                        $" — ⚠ {_brokenPlugins.Count} with missing master(s)", "")
        If filter.Length > 0 Then
            LabelStatus.Text = $"{ListViewPlugins.Items.Count} shown / {_allRows.Count} total ({activeCount} active) — {_checkedPlugins.Count} checked{brokenSuffix}."
        Else
            LabelStatus.Text = $"{_allRows.Count} plugins found ({activeCount} active, {_allRows.Count - activeCount} inactive) — {_checkedPlugins.Count} checked{brokenSuffix}."
        End If
    End Sub

    Private Sub TextBoxFilter_TextChanged(sender As Object, e As EventArgs) Handles TextBoxFilter.TextChanged
        ApplyFilter()
    End Sub

    Private Sub ListViewPlugins_ItemChecked(sender As Object, e As ItemCheckedEventArgs) Handles ListViewPlugins.ItemChecked
        If _suspendItemChecked Then Return
        If e.Item.Checked Then
            _checkedPlugins.Add(e.Item.Text)
        Else
            _checkedPlugins.Remove(e.Item.Text)
        End If
        ' One in-memory validation pass: repaints affected rows, gates OK, refreshes status counts.
        RecomputeValidation()
    End Sub

    Private Sub ButtonMarkAll_Click(sender As Object, e As EventArgs) Handles ButtonMarkAll.Click
        SetCheckStateOnVisible(True)
    End Sub

    Private Sub ButtonUnmarkAll_Click(sender As Object, e As EventArgs) Handles ButtonUnmarkAll.Click
        SetCheckStateOnVisible(False)
    End Sub

    ''' <summary>Reset selection to the engine default: every active plugin checked, everything else
    ''' unchecked. GLOBAL operation (touches _allRows, not just the filtered subset). This is the way
    ''' BACK from a restored selection — the dialog now opens pre-ticked with whatever the user last
    ''' confirmed for this game (NPC_Config.PreflightSelection_*), so "actives" is no longer necessarily
    ''' the initial state. ApplyFilter then re-renders the currently-visible subset with the new check
    ''' map. The reset is only persisted if the user then presses OK.</summary>
    Private Sub ButtonSelectActives_Click(sender As Object, e As EventArgs) Handles ButtonSelectActives.Click
        _checkedPlugins.Clear()
        For Each row In _allRows
            If row.IsActive Then _checkedPlugins.Add(row.Name)
        Next
        ApplyFilter()
        ' Single validation pass after the bulk selection change (not per-row).
        RecomputeValidation()
    End Sub

    ''' <summary>Apply <paramref name="checkedState"/> to every row currently visible in the
    ''' ListView (i.e. the filter result). _checkedPlugins is updated alongside so hidden rows
    ''' keep their state and a subsequent filter clear / change reflects the bulk operation.</summary>
    Private Sub SetCheckStateOnVisible(checkedState As Boolean)
        _suspendItemChecked = True
        ListViewPlugins.BeginUpdate()
        Try
            For Each it As ListViewItem In ListViewPlugins.Items
                it.Checked = checkedState
                If checkedState Then
                    _checkedPlugins.Add(it.Text)
                Else
                    _checkedPlugins.Remove(it.Text)
                End If
            Next
        Finally
            ListViewPlugins.EndUpdate()
            _suspendItemChecked = False
        End Try

        ' Single validation pass after the bulk check change (the per-item handler was suspended).
        RecomputeValidation()
    End Sub

    Private Async Sub ButtonOk_Click(sender As Object, e As EventArgs) Handles ButtonOk.Click
        If Not Config_App.Check_FOFolder() Then
            MessageBox.Show("Pick a valid game .exe before continuing.", "Setup",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' ⛔ Sin Plugins.txt no se sigue. Este es el modo de falla que motivó todo el rediseño de rutas y es
        ' MUDO: ReadActiveLoadOrder devuelve los masters implícitos y nada más, o sea una lista válida que
        ' describe un juego sin un solo mod. Además arrastra el mount de archives (FilesDictionary usa el
        ' mismo load order para la prioridad de BA2/BSA), así que cada mod se vería como vanilla. Cargar así
        ' produce un resultado convincente y equivocado; cortar acá y pedir la ruta es lo honesto.
        Dim loadOrderProblem = PluginManager.LoadOrderSourceProblem()
        If loadOrderProblem <> "" Then
            MessageBox.Show(
                loadOrderProblem & vbCrLf & vbCrLf &
                "Without it the app cannot tell which plugins the game loads, nor which mod wins a file " &
                "conflict — every modded mesh and texture would silently fall back to vanilla." & vbCrLf & vbCrLf &
                "Set ""Plugins.txt"" above (Browse...) and try again. The choice is remembered for this game.",
                "Load order not found", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' Guard the silent-corruption trap: the selected game must match the exe/Data we're about to
        ' parse. NPC_/RACE byte layouts (ACBS offsets, tint, sounds) differ between FO4 and SSE, so
        ' loading a Skyrim Data folder while "Fallout 4" is selected (or vice-versa) mis-reads records.
        ' Warn on an obvious filename mismatch but let the user proceed (non-standard exe names exist).
        Dim exeLower = IO.Path.GetFileName(Config_App.Current.FO4ExePath).ToLowerInvariant()
        Dim wantSkyrim = (Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
        Dim looksSkyrim = exeLower.Contains("skyrim") OrElse exeLower.Contains("sse")
        Dim looksFo4 = exeLower.Contains("fallout4")
        If (wantSkyrim AndAlso looksFo4) OrElse (Not wantSkyrim AndAlso looksSkyrim) Then
            Dim gameName = If(wantSkyrim, "Skyrim SE", "Fallout 4")
            If MessageBox.Show(
                $"You selected {gameName} but the executable is '{IO.Path.GetFileName(Config_App.Current.FO4ExePath)}'." & vbCrLf & vbCrLf &
                "This usually means the game selector and the Data folder don't match, which corrupts record parsing. Continue anyway?",
                "Game / executable mismatch", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
                Return
            End If
        End If

        ' El juego queda FIJADO acá ⇒ re-aplicar el gate por juego de la ley opt-in del CK (blend de skin
        ' NO normalizado). Sin esto, arrancar en FO4 con el toggle en True y cambiar a Skyrim en el
        ' selector dejaría la ley encendida en un motor donde NO está verificada por RE.
        NPC_Config.ApplyEngineSkinWeightNormalizationGate(Config_App.Current.Game)
        NPC_Config.ApplyGlDecodeSetting()
        NPC_Config.ApplyDownsizeFromMip0Setting()

        ' Iterate _allRows (master, load-order-preserving) instead of ListViewPlugins.Items —
        ' the latter only contains rows matching the current filter, which would drop checked
        ' plugins hidden by an active filter at OK time.
        SelectedPlugins.Clear()
        For Each row In _allRows
            If _checkedPlugins.Contains(row.Name) Then SelectedPlugins.Add(row.Name)
        Next

        Config_App.SaveConfig()

        ' Remember this selection for the next preflight of THIS game (npc_config.json, per-game slot),
        ' or CLEAR that slot when the user opted out — an unticked box means "stop remembering", so the
        ' stored list has to go, otherwise the next open would restore it and silently re-tick the box.
        ' Only this game's slot is touched; the other game's stored selection isn't on screen and isn't
        ' the user's to discard here.
        ' Written BEFORE the load: if the load blows up on one of these plugins, reopening the dialog
        ' still shows the same tick set so the user can untick the culprit instead of re-curating from
        ' the actives default.
        NPC_Config.SetPreflightSelection(Config_App.Current.Game,
                                         If(CheckBoxPersistSelection.Checked, SelectedPlugins, Nothing))
        NPC_Config.SaveConfig()

        ' Finalize plugin text encoding for the game the user settled on. Program.Main ran this once at
        ' startup against the persisted default; the user may have switched games in this dialog, so redo
        ' it here — BEFORE LoadAllPlugins below — mirroring xEdit's "configure encoding → load → edit" order.
        PluginEncodingSettings.InitializeForGame(Config_App.Current.Game)
        PluginEncodingSettings.SetLanguage(PluginEncodingSettings.ReadLanguageFromIni())
        PluginEncodingSettings.ApplyOverrideIni(AppDomain.CurrentDomain.BaseDirectory)

        SetLoadingMode(True)

        Try
            LoadedDataPath = Config_App.Current.FO4EDataPath

            ' --- Plugin load: the lib's LoadAllPlugins reports a rich PluginLoadProgress (file
            ' count + byte-weighted progress + current plugin name) from its parallel parse, marshaled
            ' to the UI thread via the Progress(Of T) we build here. Two bars:
            '   Overall = per-file (FilesDone / FilesTotal).
            '   Detail  = bytes within the whole plugin set (BytesDone / BytesTotal), mapped onto a
            '             fixed 0..1000 scale because BytesTotal is a Long that can exceed Int32 and
            '             ProgressBar.Value is an Integer.
            ProgressBarOverall.Style = ProgressBarStyle.Continuous
            ProgressBarOverall.Maximum = Math.Max(1, SelectedPlugins.Count)
            ProgressBarOverall.Value = 0
            ProgressBarDetail.Style = ProgressBarStyle.Continuous
            ProgressBarDetail.Maximum = DetailBarScale   ' fixed byte-progress scale (see DetailBarScale)
            ProgressBarDetail.Value = 0
            LabelProgress.Text = "Loading plugins..."

            Dim pm As New PluginManager()
            Dim pluginProgress As New Progress(Of PluginLoadProgress)(
                Sub(p)
                    ' Overall = per-file count. FilesTotal arrives with the first report; trust it
                    ' over the pre-set SelectedPlugins.Count if they differ (lib skips missing files).
                    If p.FilesTotal > 0 AndAlso ProgressBarOverall.Maximum <> p.FilesTotal Then
                        ProgressBarOverall.Maximum = p.FilesTotal
                    End If
                    ProgressBarOverall.Value = Math.Max(0, Math.Min(p.FilesDone, ProgressBarOverall.Maximum))

                    ' Detail = bytes on the fixed 0..DetailBarScale scale. Long math so the
                    ' multiply can't overflow Int32; clamp into range.
                    Dim detail As Integer = 0
                    If p.BytesTotal > 0 Then
                        detail = CInt(Math.Min(CLng(DetailBarScale), p.BytesDone * CLng(DetailBarScale) \ p.BytesTotal))
                    End If
                    ProgressBarDetail.Value = Math.Max(0, Math.Min(detail, ProgressBarDetail.Maximum))

                    LabelProgress.Text = If(String.IsNullOrEmpty(p.CurrentName),
                                            $"Parsing plugins — ({p.FilesDone}/{p.FilesTotal})",
                                            $"Parsing plugins — {p.CurrentName} ({p.FilesDone}/{p.FilesTotal})")
                End Sub)

            Await Task.Run(Sub() pm.LoadAllPlugins(LoadedDataPath, SelectedPlugins, pluginProgress))

            LoadedPluginManager = pm

            ' --- Archive load: Fill_DictionaryAsync reports (stage, value, max) and discovers the
            ' archive+loose count itself from the Data folder, emitting it on its first tick (which
            ' arrives almost immediately). Two bars:
            '   Overall = per-file count (archives + loose), a real value (NO marquee).
            '   Detail  = mounted-archive bytes (BytesDone / BytesTotal) of the BA2/BSA set, mapped
            '             onto the fixed 0..DetailBarScale scale (same Long math as the plugin phase,
            '             since BytesTotal is a Long that can exceed Int32). Loose files aren't byte-
            '             counted, so Detail reaches 100% when the archives finish (loose are fast).
            Dim cacheDir = IO.Path.Combine(Application.StartupPath, "Caches")
            IO.Directory.CreateDirectory(cacheDir)
            FilesDictionary_class.CacheDirectory = cacheDir
            ProgressBarDetail.Visible = True
            ProgressBarDetail.Style = ProgressBarStyle.Continuous
            ProgressBarDetail.Maximum = DetailBarScale   ' fixed byte-progress scale (see DetailBarScale)
            ProgressBarDetail.Value = 0
            ProgressBarOverall.Style = ProgressBarStyle.Continuous
            ProgressBarOverall.Maximum = 1
            ProgressBarOverall.Value = 0
            LabelProgress.Text = "Mounting archives..."

            Dim archiveProgress As New Progress(Of (Stepn As String, Value As Integer, Max As Integer))(
                Sub(info)
                    If info.Max > 0 Then
                        ProgressBarOverall.Maximum = info.Max
                        ProgressBarOverall.Value = Math.Max(0, Math.Min(info.Value, info.Max))
                    End If
                    If Not String.IsNullOrEmpty(info.Stepn) Then LabelProgress.Text = info.Stepn
                End Sub)

            ' Detail = mounted-archive bytes on the fixed 0..DetailBarScale scale. Long math so the
            ' multiply can't overflow Int32; clamp into range. b.Total=0 (all loose) leaves it at 0.
            Dim archiveByteProg As New Progress(Of (Done As Long, Total As Long))(
                Sub(b)
                    Dim detail As Integer = 0
                    If b.Total > 0 Then detail = CInt(Math.Min(CLng(DetailBarScale), b.Done * CLng(DetailBarScale) \ b.Total))
                    ProgressBarDetail.Value = Math.Max(0, Math.Min(detail, ProgressBarDetail.Maximum))
                End Sub)

            ' Task.Run is NOT redundant despite Fill_DictionaryAsync being Async: its first Await sits far
            ' down the body (after the .ba2/.bsa scan, the recursive loose-file walk of the WHOLE Data tree —
            ' once per supported extension — and BuildArchivePriority). An Async method runs SYNCHRONOUSLY
            ' until its first await, so awaiting it directly from here executed that entire walk ON THE UI
            ' THREAD: no message pump, so the "Mounting archives..." label set above never repainted and the
            ' window stayed frozen on the last painted frame ("Parsing plugins — <esm> (7/7)", bar full) while
            ' Windows marked it "Not responding". On a big modded Data folder that is minutes of dead UI.
            ' Offloading the whole call keeps the pump alive; the progress handlers still marshal back here
            ' because Progress(Of T) captured this SynchronizationContext at construction.
            ' loadedPlugins:=SelectedPlugins — ONE notion of "what is loaded" for this session, shared by records
            ' and assets. The list is pre-checked with the active load order, so the default run is engine-faithful;
            ' if the user unticks a plugin, its records AND its archives both drop out. Previously the archives were
            ' keyed off Plugins.txt no matter what the user ticked, so an unticked plugin still had its assets
            ' indexed while anything keyed off the ticked set (e.g. the RaceMenu slider config) skipped it — two
            ' different answers to the same question, and a silently half-loaded mod.
            Await Task.Run(Function() FilesDictionary_class.Fill_DictionaryAsync(LoadedDataPath, archiveProgress,
                                                                                archiveByteProgress:=archiveByteProg,
                                                                                loadedPlugins:=SelectedPlugins))

            ' No-op unless the app was launched with --diagnoseLoad (see WritePreflightDiagnostics): a normal
            ' run writes nothing at all. ⛔ NOT wired to Logger.Enabled — that flag also drives
            ' FaceGenBuilder.DebugMode, so using it as a profiling switch would silently change FaceGen bakes.
            WritePreflightDiagnostics(FilesDictionary_class.LastScanDiagnostics)

            ' Archive phase done — fill both bars to their max for the final scans. Detail stays visible.
            ProgressBarOverall.Value = ProgressBarOverall.Maximum
            ProgressBarDetail.Value = ProgressBarDetail.Maximum

            ' Final preflight step: scan Data\ for existing NPC_Manager auto-generated plugins.
            ' Cheap (only opens TES4 of each plugin to read CNAM), fully loads only the few that
            ' match. Pre-populating this here means the user's first Save ESP opens instantly
            ' instead of stalling on disk I/O. See MainForm._autoGenPluginsCache.
            Dim scanProgress As New Progress(Of String)(
                Sub(msg)
                    LabelProgress.Text = msg
                End Sub)
            LoadedAutoGenPlugins = Await Task.Run(
                Function() SaveEsp_Form.ScanAutoGeneratedPlugins(LoadedDataPath, scanProgress))

            ' Scan for <plugin>.bssliders sidecars next to the user-selected plugins. Cheap
            ' (a File.Exists per plugin + a small JSON parse for the ones that exist). Done on
            ' the worker so the splash doesn't stutter when there are many plugins.
            LabelProgress.Text = "Scanning .bssliders sidecars..."
            LoadedSidecars = Await Task.Run(
                Function() ScanSidecarsForPlugins(LoadedDataPath, SelectedPlugins))

            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            SetLoadingMode(False)
            ' SetLoadingMode re-enabled OK unconditionally; re-gate it against the master validation.
            RecomputeValidation()
            LabelProgress.Text = ""
            ProgressBarOverall.Visible = False
            ProgressBarDetail.Visible = False
            LabelProgress.Visible = False
            MessageBox.Show(ex.ToString(), "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>Write the last dictionary scan's phase breakdown to <c>preflight.log</c> next to the exe —
    ''' ⛔ ONLY when the app was launched with <c>--diagnoseLoad</c>. A normal run writes NOTHING and does
    ''' not touch the disk: shipping software does not leave logs behind, and the fix for a slow preflight
    ''' is a fast preflight, not a log we ask the user to mail us. This exists purely as an opt-in switch we
    ''' can drive OURSELVES when profiling a rig.
    '''
    ''' <para>⛔ OVERWRITE, not append: the file is FRESHLY created on every diagnosed run and holds exactly
    ''' the last one's line — no history to grow unbounded and no stale line to be mistaken for the current
    ''' run's. (A load reloads the dictionary at most once, so there is only ever one line to write.)</para></summary>
    Private Shared Sub WritePreflightDiagnostics(summary As String)
        If String.IsNullOrEmpty(summary) Then Return
        If Not Environment.GetCommandLineArgs().
                Any(Function(a) String.Equals(a, "--diagnoseLoad", StringComparison.OrdinalIgnoreCase)) Then Return

        Try
            ' Fecha en cultura INVARIANTE, por el mismo motivo que en CrashReport: con un calendario no
            ' gregoriano (ar-SA / th-TH) la marca de tiempo de un log de diagnostico sale con un anio que
            ' no cruza con ninguna otra evidencia.
            IO.File.WriteAllText(IO.Path.Combine(Application.StartupPath, "preflight.log"),
                                 Date.Now.ToString("yyyy-MM-dd HH:mm:ss", Globalization.CultureInfo.InvariantCulture) &
                                 $" [{Config_App.Current.Game}] {summary}" & Environment.NewLine)
        Catch
            ' Read-only install dir, locked file, whatever — diagnostics never break the load they diagnose.
        End Try
    End Sub

    ''' <summary>Iterate the user-selected plugins, check for a sibling <c>.bssliders</c> file,
    ''' and parse the ones that exist. Plugins without a sidecar are simply absent from the
    ''' result dict. Best-effort: a malformed sidecar is skipped silently (its NPCs render with
    ''' no overlay, same as if the user had never edited them). Keyed by plugin filename,
    ''' case-insensitive.</summary>
    ''' <remarks>Friend (not Private) so the headless bake (Program.HeadlessBakeAll) scans sidecars
    ''' through the very same function the preflight uses.</remarks>
    Friend Shared Function ScanSidecarsForPlugins(dataPath As String,
                                                  pluginNames As List(Of String)) As Dictionary(Of String, BssliderSidecar.SidecarFile)
        Dim result As New Dictionary(Of String, BssliderSidecar.SidecarFile)(StringComparer.OrdinalIgnoreCase)
        If String.IsNullOrEmpty(dataPath) OrElse pluginNames Is Nothing Then Return result
        For Each pluginName In pluginNames
            Dim espPath = IO.Path.Combine(dataPath, pluginName)
            Dim sidecarPath = BssliderSidecar.BuildPath(espPath)
            If String.IsNullOrEmpty(sidecarPath) OrElse Not IO.File.Exists(sidecarPath) Then Continue For
            Dim parsed = BssliderSidecar.Read(sidecarPath)
            If parsed IsNot Nothing Then result(pluginName) = parsed
        Next
        Return result
    End Function

    ''' <summary>Lock the form into "loading" mode while the async load runs: disable the controls
    ''' that would mutate state, show the progress bar + label. Cancel stays enabled so the user
    ''' can still bail. Closing while loading just lets the dialog close — the Task itself can't
    ''' be cleanly cancelled mid-archive, so we let it finish and discard the result.</summary>
    Private Sub SetLoadingMode(loading As Boolean)
        TextBoxExePath.Enabled = Not loading
        ButtonBrowse.Enabled = Not loading
        ListViewPlugins.Enabled = Not loading
        TextBoxFilter.Enabled = Not loading
        ButtonSelectActives.Enabled = Not loading
        ButtonMarkAll.Enabled = Not loading
        ButtonUnmarkAll.Enabled = Not loading
        ButtonCheckMasters.Enabled = Not loading
        CheckBoxPersistSelection.Enabled = Not loading
        ButtonOk.Enabled = Not loading
        ProgressBarOverall.Visible = loading
        ProgressBarDetail.Visible = loading
        LabelProgress.Visible = loading
        LabelStatus.Visible = Not loading
        Cursor = If(loading, Cursors.WaitCursor, Cursors.Default)
    End Sub
End Class
