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

    Private _activeOrder As List(Of String) = New List(Of String)()

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
        TextBoxExePath.Text = Config_App.Current.FO4ExePath
        RefreshPluginList()
    End Sub

    Private Sub ButtonBrowse_Click(sender As Object, e As EventArgs) Handles ButtonBrowse.Click
        Dim chosen = BrowseForExe(IO.Path.GetDirectoryName(TextBoxExePath.Text))
        If String.IsNullOrEmpty(chosen) Then Return
        Config_App.Current.FO4ExePath = chosen
        TextBoxExePath.Text = chosen
        RefreshPluginList()
    End Sub

    Private Shared Function BrowseForExe(initialDir As String) As String
        Using dlg As New OpenFileDialog()
            dlg.Title = "Select Fallout4.exe"
            dlg.Filter = "Fallout4.exe|Fallout4.exe|EXE files (*.exe)|*.exe"
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
        ListViewPlugins.Items.Clear()
        ButtonOk.Enabled = False
        LabelStatus.Text = ""

        If Not Config_App.Check_FOFolder() Then
            LabelStatus.Text = "Pick Fallout4.exe to enumerate plugins."
            Return
        End If

        Dim dataPath = Config_App.Current.FO4EDataPath
        _activeOrder = PluginManager.ReadActiveLoadOrder()

        Dim allPluginFiles =
            FilesDictionary_class.EnumerateFilesWithSymlinkSupport(dataPath, "*.esp;*.esm;*.esl", False).
                Select(Function(p) IO.Path.GetFileName(p)).
                ToList()

        Dim allPluginsSet = New HashSet(Of String)(allPluginFiles, StringComparer.OrdinalIgnoreCase)
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

        ApplyFilter()
        ButtonOk.Enabled = True
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
                it.Checked = _checkedPlugins.Contains(row.Name)
                If Not row.IsActive Then it.ForeColor = SystemColors.GrayText
                ListViewPlugins.Items.Add(it)
            Next
        Finally
            ListViewPlugins.EndUpdate()
            _suspendItemChecked = False
        End Try

        Dim activeCount As Integer = _allRows.Where(Function(r) r.IsActive).Count()
        Dim totalShown As Integer = ListViewPlugins.Items.Count
        If filter.Length > 0 Then
            LabelStatus.Text = $"{totalShown} shown / {_allRows.Count} total ({activeCount} active) — {_checkedPlugins.Count} checked."
        Else
            LabelStatus.Text = $"{_allRows.Count} plugins found ({activeCount} active, {_allRows.Count - activeCount} inactive) — {_checkedPlugins.Count} checked."
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
        ' Refresh the status counter without rebuilding the list. Cheap: just reads counts.
        Dim activeCount As Integer = _allRows.Where(Function(r) r.IsActive).Count()
        Dim filter As String = If(TextBoxFilter.Text, "").Trim()
        If filter.Length > 0 Then
            LabelStatus.Text = $"{ListViewPlugins.Items.Count} shown / {_allRows.Count} total ({activeCount} active) — {_checkedPlugins.Count} checked."
        Else
            LabelStatus.Text = $"{_allRows.Count} plugins found ({activeCount} active, {_allRows.Count - activeCount} inactive) — {_checkedPlugins.Count} checked."
        End If
    End Sub

    Private Sub ButtonMarkAll_Click(sender As Object, e As EventArgs) Handles ButtonMarkAll.Click
        SetCheckStateOnVisible(True)
    End Sub

    Private Sub ButtonUnmarkAll_Click(sender As Object, e As EventArgs) Handles ButtonUnmarkAll.Click
        SetCheckStateOnVisible(False)
    End Sub

    ''' <summary>Reset selection to the default state: every active plugin checked, everything
    ''' else unchecked. GLOBAL operation (touches _allRows, not just the filtered subset) — same
    ''' end state as the initial post-RefreshPluginList state. ApplyFilter then re-renders the
    ''' currently-visible subset with the new check map.</summary>
    Private Sub ButtonSelectActives_Click(sender As Object, e As EventArgs) Handles ButtonSelectActives.Click
        _checkedPlugins.Clear()
        For Each row In _allRows
            If row.IsActive Then _checkedPlugins.Add(row.Name)
        Next
        ApplyFilter()
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

        Dim activeCount As Integer = _allRows.Where(Function(r) r.IsActive).Count()
        Dim filter As String = If(TextBoxFilter.Text, "").Trim()
        If filter.Length > 0 Then
            LabelStatus.Text = $"{ListViewPlugins.Items.Count} shown / {_allRows.Count} total ({activeCount} active) — {_checkedPlugins.Count} checked."
        Else
            LabelStatus.Text = $"{_allRows.Count} plugins found ({activeCount} active, {_allRows.Count - activeCount} inactive) — {_checkedPlugins.Count} checked."
        End If
    End Sub

    Private Async Sub ButtonOk_Click(sender As Object, e As EventArgs) Handles ButtonOk.Click
        If Not Config_App.Check_FOFolder() Then
            MessageBox.Show("Pick a valid Fallout4.exe before continuing.", "Setup",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' Iterate _allRows (master, load-order-preserving) instead of ListViewPlugins.Items —
        ' the latter only contains rows matching the current filter, which would drop checked
        ' plugins hidden by an active filter at OK time.
        SelectedPlugins.Clear()
        For Each row In _allRows
            If _checkedPlugins.Contains(row.Name) Then SelectedPlugins.Add(row.Name)
        Next

        Config_App.SaveConfig()

        SetLoadingMode(True)

        Try
            LoadedDataPath = Config_App.Current.FO4EDataPath

            ' --- Plugin load: report per-plugin progress against the picked count. The lib's
            ' LoadAllPlugins reports "Loading X (i/N)" per plugin; we surface that as label text
            ' and tick the bar. Total = SelectedPlugins.Count (some may not exist in Data\, the
            ' lib silently skips those — close enough for progress reporting).
            ProgressBarLoad.Style = ProgressBarStyle.Continuous
            ProgressBarLoad.Maximum = Math.Max(1, SelectedPlugins.Count)
            ProgressBarLoad.Value = 0
            LabelProgress.Text = "Loading plugins..."

            Dim pm As New PluginManager()
            Dim pluginIdx As Integer = 0
            Dim pluginProgress As New Progress(Of String)(
                Sub(msg)
                    pluginIdx = Math.Min(pluginIdx + 1, ProgressBarLoad.Maximum)
                    ProgressBarLoad.Value = pluginIdx
                    LabelProgress.Text = msg
                End Sub)

            Await Task.Run(Sub() pm.LoadAllPlugins(LoadedDataPath, SelectedPlugins, pluginProgress))

            LoadedPluginManager = pm

            ' --- Archive load: Fill_DictionaryAsync reports (stage, value, max) and discovers the
            ' archive count itself from the Data folder. Switch to indeterminate marquee until the
            ' first progress tick gives us a max.
            Dim cacheDir = IO.Path.Combine(Application.StartupPath, "Caches")
            IO.Directory.CreateDirectory(cacheDir)
            FilesDictionary_class.CacheDirectory = cacheDir
            FilesDictionary_class.RegisterExtensions(".ssf", ".sclp")

            ProgressBarLoad.Style = ProgressBarStyle.Marquee
            ProgressBarLoad.MarqueeAnimationSpeed = 30
            LabelProgress.Text = "Mounting archives..."

            Dim archiveProgress As New Progress(Of (Stepn As String, Value As Integer, Max As Integer))(
                Sub(info)
                    If info.Max > 0 Then
                        If ProgressBarLoad.Style <> ProgressBarStyle.Continuous Then
                            ProgressBarLoad.Style = ProgressBarStyle.Continuous
                        End If
                        ProgressBarLoad.Maximum = info.Max
                        ProgressBarLoad.Value = Math.Max(0, Math.Min(info.Value, info.Max))
                    End If
                    If Not String.IsNullOrEmpty(info.Stepn) Then LabelProgress.Text = info.Stepn
                End Sub)

            Await FilesDictionary_class.Fill_DictionaryAsync(LoadedDataPath, archiveProgress)

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
            LabelProgress.Text = ""
            ProgressBarLoad.Visible = False
            LabelProgress.Visible = False
            MessageBox.Show(ex.ToString(), "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>Iterate the user-selected plugins, check for a sibling <c>.bssliders</c> file,
    ''' and parse the ones that exist. Plugins without a sidecar are simply absent from the
    ''' result dict. Best-effort: a malformed sidecar is skipped silently (its NPCs render with
    ''' no overlay, same as if the user had never edited them). Keyed by plugin filename,
    ''' case-insensitive.</summary>
    Private Shared Function ScanSidecarsForPlugins(dataPath As String,
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
        ButtonOk.Enabled = Not loading
        ProgressBarLoad.Visible = loading
        LabelProgress.Visible = loading
        LabelStatus.Visible = Not loading
        Cursor = If(loading, Cursors.WaitCursor, Cursors.Default)
    End Sub
End Class
