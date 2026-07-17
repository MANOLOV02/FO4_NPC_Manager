Option Strict On
Imports System.IO
Imports FO4_Base_Library

''' <summary>
''' Dialog for "Export FOMOD" — packages an app-authored plugin (TES4.CNAM = NPC_Manager marker)
''' into a distributable FOMOD ZIP. The user edits the installer metadata (mod name, author,
''' version, description, website) and can add extra assets; the mandatory game-aware content
''' (plugin + BA2/BSA or loose FaceGen + .bssliders + apply-script .pex + BodyGen inis) is
''' auto-detected by <see cref="FomodExporter.BuildManifest"/> and shown read-only in the grid.
'''
''' The metadata persists in a <c>&lt;plugin&gt;.fomodmeta.json</c> sidecar next to the ESP
''' (<see cref="FomodMetaSidecar"/>) so re-exports reopen pre-filled. The export itself runs
''' inside a modal <see cref="BuildProgress_Form"/> (same pattern as SaveEsp_Form.OnOkClick);
''' DialogResult.OK is returned only when the ZIP was fully written.
''' </summary>
Public Class FomodExport_Form

    Private ReadOnly _espFullPath As String
    Private ReadOnly _pluginFileName As String
    Private ReadOnly _game As Config_App.Game_Enum
    Private ReadOnly _dataPath As String
    Private ReadOnly _pluginManager As PluginManager
    Private ReadOnly _npcFormIDs As List(Of UInteger)
    Private ReadOnly _hasUnsavedChanges As Boolean

    ''' <summary>Preview capture taken by MainForm right before opening this dialog (Nothing when
    ''' no GL frame was available). Owned by the CALLER — disposed after ShowDialog returns, never
    ''' here.</summary>
    Private ReadOnly _previewImage As Image

    ''' <summary>Author-added assets (Data-relative paths). Seeded from the sidecar; the manifest
    ''' rebuild consumes this list, so add/remove just mutates it and refreshes.</summary>
    Private ReadOnly _extraAssets As New List(Of String)

    Private _manifest As List(Of FomodExporter.ManifestItem) = New List(Of FomodExporter.ManifestItem)
    Private _loading As Boolean = False

    ''' <summary>Full path of the ZIP written by a successful export (Nothing otherwise).</summary>
    Public ReadOnly Property ExportedZipPath As String
        Get
            Return _exportedZipPath
        End Get
    End Property
    Private _exportedZipPath As String = Nothing

    Public Sub New(espFullPath As String, pluginFileName As String, game As Config_App.Game_Enum,
                   dataPath As String, pluginManager As PluginManager,
                   npcFormIDs As IEnumerable(Of UInteger), hasUnsavedChanges As Boolean,
                   previewImage As Image)
        InitializeComponent()
        _espFullPath = espFullPath
        _pluginFileName = pluginFileName
        _game = game
        _dataPath = dataPath
        _pluginManager = pluginManager
        _npcFormIDs = If(npcFormIDs Is Nothing, New List(Of UInteger), npcFormIDs.ToList())
        _hasUnsavedChanges = hasUnsavedChanges
        _previewImage = previewImage
    End Sub

    Private Sub FomodExport_Form_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Dim gameName = If(_game = Config_App.Game_Enum.Skyrim, "Skyrim SE", "Fallout 4")
        LabelHeader.Text = $"FOMOD package for {_pluginFileName} ({gameName})"
        If _hasUnsavedChanges Then
            LabelWarning.Text = "⚠ Unsaved NPC changes in this plugin — Save ESP first: the exported ZIP only contains what is on disk."
            LabelWarning.Visible = True
        End If

        ' Preview capture: shown as-is; without one the checkbox is off AND disabled so the
        ' export can never reference a screenshot that does not exist.
        If _previewImage IsNot Nothing Then
            PictureBoxScreenshot.Image = _previewImage
        Else
            CheckBoxIncludeScreenshot.Checked = False
            CheckBoxIncludeScreenshot.Enabled = False
            CheckBoxIncludeScreenshot.Text = "No preview available"
        End If

        BuildGridColumns()

        ' Seed the metadata from the sidecar when present, defaults otherwise.
        _loading = True
        Try
            Dim meta = FomodMetaSidecar.Read(FomodMetaSidecar.BuildPath(_espFullPath))
            If meta Is Nothing Then
                meta = New FomodMetaSidecar.MetaFile With {
                    .Plugin = _pluginFileName,
                    .ModName = Path.GetFileNameWithoutExtension(_pluginFileName)}
            End If
            TextBoxModName.Text = If(meta.ModName, "")
            TextBoxVersion.Text = If(String.IsNullOrWhiteSpace(meta.ModVersion), "1.0.0", meta.ModVersion)
            TextBoxAuthor.Text = If(meta.Author, "")
            TextBoxWebsite.Text = If(meta.Website, "")
            TextBoxDescription.Text = If(meta.Description, "")
            ' Persisted preference — only when a capture exists (otherwise forced off above).
            If _previewImage IsNot Nothing Then CheckBoxIncludeScreenshot.Checked = meta.IncludeScreenshot
            _extraAssets.Clear()
            If meta.ExtraAssets IsNot Nothing Then
                For Each asset In meta.ExtraAssets
                    If Not String.IsNullOrWhiteSpace(asset) AndAlso
                       Not _extraAssets.Contains(asset, StringComparer.OrdinalIgnoreCase) Then
                        _extraAssets.Add(asset)
                    End If
                Next
            End If
        Finally
            _loading = False
        End Try

        RefreshManifest()
    End Sub

    ' =====================================================================
    ' Manifest grid
    ' =====================================================================

    Private Sub BuildGridColumns()
        GridManifest.AutoGenerateColumns = False
        GridManifest.Columns.Clear()
        GridManifest.Columns.Add(NewReadOnlyCol("Type", 16))
        GridManifest.Columns.Add(NewReadOnlyCol("File (Data-relative)", 46))
        GridManifest.Columns.Add(NewReadOnlyCol("Status", 10))
        GridManifest.Columns.Add(NewReadOnlyCol("Size", 9))
        GridManifest.Columns.Add(NewReadOnlyCol("Note", 19))
    End Sub

    Private Shared Function NewReadOnlyCol(header As String, weight As Single) As DataGridViewTextBoxColumn
        Return New DataGridViewTextBoxColumn With {
            .HeaderText = header, .FillWeight = weight, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, .ReadOnly = True}
    End Function

    ''' <summary>Rebuild the manifest from current disk state + the extra-assets list, repopulate
    ''' the grid (Required-but-missing rows in red, absent optional rows grayed) and recompute the
    ''' Export gate + validation summary.</summary>
    Private Sub RefreshManifest()
        _manifest = FomodExporter.BuildManifest(_dataPath, _pluginFileName, _game,
                                                _npcFormIDs, _pluginManager, _extraAssets)
        GridManifest.Rows.Clear()
        For Each item In _manifest
            Dim status = If(item.Exists, "OK", If(item.Required, "MISSING", "—"))
            Dim idx = GridManifest.Rows.Add(KindLabel(item.Kind), item.DataRelativePath,
                                            status, FormatSize(item), item.Note)
            Dim row = GridManifest.Rows(idx)
            row.Tag = item
            If Not item.Exists Then
                row.DefaultCellStyle.ForeColor = If(item.Required, Drawing.Color.Firebrick, SystemColors.GrayText)
            End If
        Next
        UpdateExportEnabled()
    End Sub

    Private Shared Function KindLabel(kind As FomodExporter.ItemKind) As String
        Select Case kind
            Case FomodExporter.ItemKind.Plugin : Return "Plugin"
            Case FomodExporter.ItemKind.Archive : Return "Archive"
            Case FomodExporter.ItemKind.PresetSidecar : Return "Preset sidecar"
            Case FomodExporter.ItemKind.ApplyScript : Return "Apply script"
            Case FomodExporter.ItemKind.BodyGenIni : Return "BodyGen ini"
            Case FomodExporter.ItemKind.FaceGenLoose : Return "FaceGen (loose)"
            Case FomodExporter.ItemKind.ExtraAsset : Return "Extra asset"
            Case Else : Return kind.ToString()
        End Select
    End Function

    Private Shared Function FormatSize(item As FomodExporter.ManifestItem) As String
        If Not item.Exists Then Return ""
        Dim bytes = item.SizeBytes
        If bytes >= 1024L * 1024L Then Return $"{bytes / (1024.0 * 1024.0):0.0} MB"
        If bytes >= 1024L Then Return $"{bytes / 1024.0:0.0} KB"
        Return $"{bytes} B"
    End Function

    ''' <summary>Export gate: non-empty mod name + zero validation errors. The validation summary
    ''' goes to LabelValidation so the user always sees WHY the button is off.</summary>
    Private Sub UpdateExportEnabled()
        Dim errors = FomodExporter.Validate(_manifest)
        If String.IsNullOrWhiteSpace(TextBoxModName.Text) Then
            errors.Insert(0, "Mod name is required.")
        End If
        LabelValidation.Text = String.Join(Environment.NewLine, errors.Take(3))
        ButtonExport.Enabled = (errors.Count = 0)
    End Sub

    Private Sub TextBoxModName_TextChanged(sender As Object, e As EventArgs) Handles TextBoxModName.TextChanged
        If Not _loading Then UpdateExportEnabled()
    End Sub

    Private Sub GridManifest_SelectionChanged(sender As Object, e As EventArgs) Handles GridManifest.SelectionChanged
        ButtonRemoveAsset.Enabled = SelectedExtraAsset() IsNot Nothing
    End Sub

    Private Function SelectedExtraAsset() As FomodExporter.ManifestItem
        If GridManifest.SelectedRows.Count <> 1 Then Return Nothing
        Dim item = TryCast(GridManifest.SelectedRows(0).Tag, FomodExporter.ManifestItem)
        If item IsNot Nothing AndAlso item.Kind = FomodExporter.ItemKind.ExtraAsset Then Return item
        Return Nothing
    End Function

    ' =====================================================================
    ' Extra assets
    ' =====================================================================

    Private Sub ButtonAddAsset_Click(sender As Object, e As EventArgs) Handles ButtonAddAsset.Click
        Using ofd As New OpenFileDialog With {
            .Title = "Add asset (must live under the game's Data folder)",
            .InitialDirectory = _dataPath,
            .Filter = "All files (*.*)|*.*",
            .Multiselect = True}
            If ofd.ShowDialog(Me) <> DialogResult.OK Then Return

            Dim dataRoot = Path.GetFullPath(_dataPath).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
            Dim rejected As New List(Of String)
            For Each file In ofd.FileNames
                Dim full = Path.GetFullPath(file)
                If Not full.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase) Then
                    rejected.Add(file)
                    Continue For
                End If
                Dim rel = Path.GetRelativePath(_dataPath, full)
                If Not _extraAssets.Contains(rel, StringComparer.OrdinalIgnoreCase) Then
                    _extraAssets.Add(rel)
                End If
            Next
            If rejected.Count > 0 Then
                MessageBox.Show(Me,
                    "These files are outside the game's Data folder and were skipped (a FOMOD can only install under Data):" &
                    Environment.NewLine & String.Join(Environment.NewLine, rejected),
                    "Export FOMOD", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End If
        End Using
        RefreshManifest()
    End Sub

    Private Sub ButtonRemoveAsset_Click(sender As Object, e As EventArgs) Handles ButtonRemoveAsset.Click
        Dim item = SelectedExtraAsset()
        If item Is Nothing Then Return
        _extraAssets.RemoveAll(Function(a) String.Equals(a, item.DataRelativePath, StringComparison.OrdinalIgnoreCase))
        RefreshManifest()
    End Sub

    ' =====================================================================
    ' Export
    ' =====================================================================

    Private Function BuildMetaFromUi() As FomodMetaSidecar.MetaFile
        Return New FomodMetaSidecar.MetaFile With {
            .Plugin = _pluginFileName,
            .ModName = TextBoxModName.Text.Trim(),
            .ModVersion = If(String.IsNullOrWhiteSpace(TextBoxVersion.Text), "1.0.0", TextBoxVersion.Text.Trim()),
            .Author = TextBoxAuthor.Text.Trim(),
            .Website = TextBoxWebsite.Text.Trim(),
            .Description = TextBoxDescription.Text,
            .ExtraAssets = New List(Of String)(_extraAssets),
            .IncludeScreenshot = CheckBoxIncludeScreenshot.Checked}
    End Function

    Private Shared Function SanitizeFileName(name As String) As String
        Dim sb As New Text.StringBuilder(name.Length)
        For Each ch In name
            sb.Append(If(Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0, "_"c, ch))
        Next
        Return sb.ToString()
    End Function

    ''' <summary>Same shape as SaveEsp_Form.OnOkClick: validate → pick target → persist the
    ''' metadata sidecar FIRST (it must survive even a failed export) → run the ZIP write inside a
    ''' modal BuildProgress_Form (worker thread; Cancel polled between files) → close with OK only
    ''' on success, otherwise stay interactive for a retry.</summary>
    Private Sub ButtonExport_Click(sender As Object, e As EventArgs) Handles ButtonExport.Click
        ' Re-validate against CURRENT disk state (files may have changed since the dialog opened).
        RefreshManifest()
        If Not ButtonExport.Enabled Then Return

        Dim meta = BuildMetaFromUi()

        Dim zipPath As String = Nothing
        Using sfd As New SaveFileDialog With {
            .Title = "Export FOMOD package",
            .Filter = "ZIP archive (*.zip)|*.zip",
            .FileName = SanitizeFileName($"{meta.ModName}-{meta.ModVersion}") & ".zip",
            .OverwritePrompt = True}
            If sfd.ShowDialog(Me) <> DialogResult.OK Then Return
            zipPath = sfd.FileName
        End Using

        ' Persist the metadata sidecar before exporting — the user's editing work survives
        ' regardless of how the export itself ends.
        Try
            FomodMetaSidecar.Write(FomodMetaSidecar.BuildPath(_espFullPath), meta)
        Catch ex As Exception
            MessageBox.Show(Me, "Could not save the metadata sidecar (" & ex.Message & ")." &
                            Environment.NewLine & "The export will continue, but the metadata won't be remembered.",
                            "Export FOMOD", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try

        ' Screenshot: encode the capture to PNG on the UI thread (GDI+ bitmaps are not
        ' thread-affine friendly) and hand plain bytes to the worker. Only when the checkbox is
        ' on AND a capture exists — otherwise the config carries no image reference at all.
        Dim screenshotPng As Byte() = Nothing
        Dim wantShot = CheckBoxIncludeScreenshot.Checked AndAlso _previewImage IsNot Nothing
        If wantShot Then
            Try
                Using ms As New MemoryStream()
                    _previewImage.Save(ms, Imaging.ImageFormat.Png)
                    screenshotPng = ms.ToArray()
                End Using
            Catch ex As Exception
                MessageBox.Show(Me, "Could not encode the preview screenshot (" & ex.Message & ")." &
                                Environment.NewLine & "The FOMOD will be exported without it.",
                                "Export FOMOD", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                screenshotPng = Nothing
                wantShot = False
            End Try
        End If

        Dim infoXml = FomodExporter.BuildInfoXml(meta, _game)
        Dim cfgXml = FomodExporter.BuildModuleConfigXml(meta, _manifest, _game, _npcFormIDs.Count,
                                                        includeScreenshot:=wantShot)
        Dim manifest = _manifest
        Dim exportError As Exception = Nothing

        Using prog As New BuildProgress_Form()
            prog.Text = "Exporting FOMOD…"
            prog.WorkAsync =
                Async Function(dlg As BuildProgress_Form) As Task
                    ' Let the dialog paint before the blocking IO starts (same rationale as SaveEsp_Form).
                    Await Task.Delay(1)
                    ' Progress(Of T) captures THIS (UI) sync context; the worker reports through it.
                    Dim progressUi As IProgress(Of (Current As Integer, Max As Integer, Status As String)) =
                        New Progress(Of (Current As Integer, Max As Integer, Status As String))(
                            Sub(p) dlg.SetProgress(p.Current, p.Max, p.Status))
                    Try
                        Await Task.Run(
                            Sub()
                                FomodExporter.ExportToZip(zipPath, manifest, infoXml, cfgXml, screenshotPng,
                                    Sub(cur, max, status) progressUi.Report((cur, max, status)),
                                    Function() dlg.Cancelled)
                            End Sub)
                    Catch ex As Exception
                        exportError = ex
                    End Try
                End Function
            prog.ShowDialog(Me)
        End Using

        If exportError Is Nothing Then
            _exportedZipPath = zipPath
            DialogResult = DialogResult.OK
            Close()
        ElseIf TypeOf exportError Is OperationCanceledException Then
            MessageBox.Show(Me, "Export cancelled — no ZIP was written.",
                            "Export FOMOD", MessageBoxButtons.OK, MessageBoxIcon.Information)
        Else
            MessageBox.Show(Me, "Export failed: " & exportError.Message,
                            "Export FOMOD", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If
    End Sub

End Class
