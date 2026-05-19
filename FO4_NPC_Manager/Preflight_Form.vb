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
    ''' Order: active load order first (engine order), then inactives sorted alphabetically.</summary>
    Private Sub RefreshPluginList()
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
                AddRow(pluginName, isActive:=True)
                rendered.Add(pluginName)
            End If
        Next

        Dim inactives = allPluginFiles.
            Where(Function(p) Not rendered.Contains(p)).
            OrderBy(Function(p) p, StringComparer.OrdinalIgnoreCase)
        For Each pluginName In inactives
            AddRow(pluginName, isActive:=False)
        Next

        LabelStatus.Text = $"{ListViewPlugins.Items.Count} plugins found ({_activeOrder.Count} active, {ListViewPlugins.Items.Count - _activeOrder.Count} inactive)."
        ButtonOk.Enabled = True
    End Sub

    Private Sub AddRow(pluginName As String, isActive As Boolean)
        Dim it As New ListViewItem(pluginName)
        it.SubItems.Add(If(isActive, "Active", "Inactive"))
        it.Checked = isActive
        If Not isActive Then it.ForeColor = SystemColors.GrayText
        ListViewPlugins.Items.Add(it)
    End Sub

    Private Async Sub ButtonOk_Click(sender As Object, e As EventArgs) Handles ButtonOk.Click
        If Not Config_App.Check_FOFolder() Then
            MessageBox.Show("Pick a valid Fallout4.exe before continuing.", "Setup",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        SelectedPlugins.Clear()
        For Each it As ListViewItem In ListViewPlugins.Items
            If it.Checked Then SelectedPlugins.Add(it.Text)
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
        ButtonOk.Enabled = Not loading
        ProgressBarLoad.Visible = loading
        LabelProgress.Visible = loading
        LabelStatus.Visible = Not loading
        Cursor = If(loading, Cursors.WaitCursor, Cursors.Default)
    End Sub
End Class
