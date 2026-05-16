Imports FO4_Base_Library

''' <summary>Modal dialog that lets the user pick one HDPT for a given (RACE, gender, partType)
''' triple. Filters the master plugin HDPT enumeration by:
'''   - HDPT.PartType matches the requested type (Face / Hair / Eyes / etc).
'''   - HDPT.Flags &amp; IsExtra == 0 (extras are HNAM addons, regenerated automatically by the engine
'''     from the parent main HDPT — never selected directly).
'''   - HDPT.Flags gender-bit matches the NPC's gender. The bit layout of HDPT.DATA in this
'''     codebase is by-position (bit 1 = Male, bit 2 = Female) per the comment block at
'''     MainForm.vb:78-87. An HDPT with NEITHER bit set is treated as gender-neutral (uncommon
'''     but exists in mods); the user sees it regardless.
'''   - HDPT.RNAM (ValidRacesFormID) either 0 (no restriction) OR points to a FLST that contains
'''     the NPC's race FormID. Vanilla HDPTs typically restrict to HumanRace / GhoulRace via FLST.
'''
''' Returns the chosen FormID via <see cref="SelectedFormID"/> when DialogResult is OK.
''' </summary>
Public Class HeadPartPicker_Form

    ' HDPT.DATA flag bits — match the constants documented at MainForm.vb:78-87 (by-position
    ' interpretation of the wbFlags array, validated empirically against Cait's HDPT byte 0x35).
    Private Const FlagBitMale As Byte = &H2
    Private Const FlagBitFemale As Byte = &H4
    Private Const FlagBitIsExtra As Byte = &H8

    Private ReadOnly _pluginManager As PluginManager
    Private ReadOnly _candidates As New List(Of Candidate)
    Private _filtered As List(Of Candidate)

    Public Property SelectedFormID As UInteger

    ''' <summary>GLControl that previews the selected HDPT's NIF. Created in code-behind because
    ''' GLControl needs an OpenGL context that the Visual Studio Designer can't provide. The
    ''' preview is intentionally minimal: just the NIF shapes, no skinning resolver, no morphs,
    ''' no tints. The user only needs to see what they're picking.</summary>
    Private _preview As PreviewControl
    Private _previewLoadInProgress As Boolean
    Private _lastPreviewFormID As UInteger

    Private Class Candidate
        Public FormID As UInteger
        Public EditorID As String
        Public FullName As String
        Public Plugin As String
    End Class

    ''' <summary>Build the picker for a (race, gender, partType) tuple.</summary>
    ''' <param name="pluginManager">Master plugin manager — we walk all loaded HDPTs from it.</param>
    ''' <param name="raceFormID">FormID of the NPC's race; used both for the header label and
    ''' for filtering by HDPT.RNAM FLST.</param>
    ''' <param name="raceEditorID">Display name of the race for the header.</param>
    ''' <param name="isFemale">Gender filter applied to HDPT.DATA flags.</param>
    ''' <param name="partType">PNAM type to filter by (1=Face, 2=Eyes, 3=Hair, 4=Facial Hair,
    ''' 5=Scar, 6=Eyebrows, 7=Meatcaps, 8=Teeth, 9=Head Rear). Per wbDefinitionsFO4.pas:7373.</param>
    ''' <param name="partTypeLabel">Human-readable label of the part type for the header.</param>
    Public Sub New(pluginManager As PluginManager,
                   raceFormID As UInteger,
                   raceEditorID As String,
                   isFemale As Boolean,
                   partType As Integer,
                   partTypeLabel As String,
                   Optional raceDefaultHeadPartFormIDs As IEnumerable(Of UInteger) = Nothing)
        InitializeComponent()
        _pluginManager = pluginManager
        Text = $"Add {partTypeLabel}"
        LabelHeader.Text = $"{partTypeLabel} for race '{raceEditorID}' ({If(isFemale, "Female", "Male")}). Choose one:"

        Dim raceDefaultsSet As New HashSet(Of UInteger)
        If raceDefaultHeadPartFormIDs IsNot Nothing Then
            For Each fid In raceDefaultHeadPartFormIDs
                raceDefaultsSet.Add(fid)
            Next
        End If

        BuildCandidates(raceFormID, isFemale, partType, raceDefaultsSet)
        _filtered = New List(Of Candidate)(_candidates)
        RefreshList()

        AddHandler TextBoxFilter.TextChanged, AddressOf OnFilterChanged
        AddHandler ListViewParts.DoubleClick, AddressOf OnListDoubleClick
        AddHandler ListViewParts.SelectedIndexChanged, AddressOf OnListSelectionChanged
        AddHandler ButtonOk.Click, AddressOf OnOk
        SortableListView.Attach(ListViewParts)
    End Sub

    Private Sub BuildCandidates(raceFormID As UInteger, isFemale As Boolean, partType As Integer, raceDefaults As HashSet(Of UInteger))
        If _pluginManager Is Nothing Then Return
        Dim hdptRecords = _pluginManager.GetRecordsOfType("HDPT")
        If hdptRecords Is Nothing Then Return

        ' Cache resolved FLSTs as we encounter them — vanilla has 3-4 distinct race FLSTs and
        ' parsing the same FLST 396 times is wasteful.
        Dim flstCache As New Dictionary(Of UInteger, FLST_Data)

        Dim totalScanned As Integer = 0
        Dim filteredPartType As Integer = 0
        Dim filteredIsExtra As Integer = 0
        Dim filteredGender As Integer = 0
        Dim filteredRace As Integer = 0
        Dim accepted As Integer = 0

        For Each rec In hdptRecords
            totalScanned += 1
            Dim hdpt = RecordParsers.ParseHDPT(rec, _pluginManager)
            If hdpt Is Nothing Then Continue For

            ' Filter 1: PartType match.
            If hdpt.PartType <> partType Then
                filteredPartType += 1
                Continue For
            End If

            ' Filter 2: not an HNAM extra. For non-Misc parts (Hair / Eyes / etc.) the user
            ' picks the parent HDPT and the engine pulls its HNAM extras automatically — the
            ' user should not pick an extra directly. For the Misc bucket itself, however, the
            ' filter is the wrong question: Misc is exactly where addon-style HDPTs live in
            ' vanilla (lashes, AO, wet, hairlines, mouth shadow), and the user explicitly asked
            ' for that list when they clicked +Misc. So skip the IsExtra filter for partType=0.
            If partType <> 0 AndAlso (hdpt.Flags And FlagBitIsExtra) <> 0 Then
                filteredIsExtra += 1
                Continue For
            End If

            ' Filter 3: gender. Bits at position 1 (Male) and 2 (Female). HDPTs that declare
            ' NEITHER bit are treated as universal (some mods do this) and surfaced regardless.
            Dim hasMale = (hdpt.Flags And FlagBitMale) <> 0
            Dim hasFemale = (hdpt.Flags And FlagBitFemale) <> 0
            If hasMale OrElse hasFemale Then
                If isFemale AndAlso Not hasFemale Then
                    filteredGender += 1
                    Continue For
                End If
                If Not isFemale AndAlso Not hasMale Then
                    filteredGender += 1
                    Continue For
                End If
            End If

            ' Filter 4: RACE membership — delegated to HeadPartResolver.IsHdptValidForRace
            ' so the picker, the LooksmenuLoad_Form race filter and any other caller stay in
            ' lockstep with the same three pass conditions (HDPT.RNAM=0, FLST membership,
            ' RACE-default gender list). flstCache is shared across the loop so each FLST
            ' is parsed once even though we re-enter the helper per HDPT.
            If Not HeadPartResolver.IsHdptValidForRace(hdpt.FormID, raceFormID, isFemale, _pluginManager, flstCache, raceDefaults) Then
                filteredRace += 1
                Continue For
            End If

            accepted += 1
            _candidates.Add(New Candidate With {
                .FormID = hdpt.FormID,
                .EditorID = If(hdpt.EditorID, ""),
                .FullName = If(hdpt.FullName, ""),
                .Plugin = ResolvePluginName(rec)
            })
        Next


        _candidates.Sort(Function(a, b) String.Compare(a.EditorID, b.EditorID, StringComparison.OrdinalIgnoreCase))
    End Sub

    Private Function ResolvePluginName(rec As PluginRecord) As String
        ' PluginRecord.SourcePluginName is set by PluginReader.vb:119 at parse time. Empty when
        ' the parser couldn't attribute the record (defensive fallback only).
        If rec Is Nothing OrElse String.IsNullOrEmpty(rec.SourcePluginName) Then Return "?"
        Return rec.SourcePluginName
    End Function

    Private Sub RefreshList()
        ListViewParts.BeginUpdate()
        Try
            ListViewParts.Items.Clear()
            For Each c In _filtered
                Dim row As New ListViewItem(c.EditorID)
                row.SubItems.Add(c.FullName)
                row.SubItems.Add(c.Plugin)
                row.SubItems.Add($"{c.FormID:X8}")
                row.Tag = c
                ListViewParts.Items.Add(row)
            Next
        Finally
            ListViewParts.EndUpdate()
        End Try
    End Sub

    Private Sub OnFilterChanged(sender As Object, e As EventArgs)
        Dim text = TextBoxFilter.Text.Trim()
        If text.Length = 0 Then
            _filtered = New List(Of Candidate)(_candidates)
        Else
            _filtered = _candidates.Where(Function(c) _
                c.EditorID.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                c.FullName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList()
        End If
        RefreshList()
    End Sub

    Private Sub OnListDoubleClick(sender As Object, e As EventArgs)
        If ListViewParts.SelectedItems.Count = 0 Then Return
        OnOk(sender, e)
    End Sub

    ''' <summary>Re-load the selected HDPT's NIF into the right-pane preview. Cheapest pipeline:
    ''' read bytes from FilesDictionary, parse via NifContent_Class, hand the shapes to
    ''' PreviewControl.RenderShapes(shapes) which sets up Intent and runs the pipeline. No
    ''' skinning resolver / morphs / tints — the user only needs to see the geometry.</summary>
    Private Sub OnListSelectionChanged(sender As Object, e As EventArgs)
        If _preview Is Nothing OrElse _preview.IsDisposed Then Return
        If _previewLoadInProgress Then Return
        If ListViewParts.SelectedItems.Count = 0 Then
            ClearPreview("(no head part selected)")
            Return
        End If
        Dim c = TryCast(ListViewParts.SelectedItems(0).Tag, Candidate)
        If c Is Nothing Then Return
        ' Avoid re-loading the same NIF when the user clicks the already-selected row.
        If c.FormID = _lastPreviewFormID Then Return

        _previewLoadInProgress = True
        Try
            ' Walk the parent HDPT + every HDPT.ExtraPartFormIDs (HNAM) recursively. The engine
            ' pulls these "misc" sub-parts automatically — eyelashes/AO/wet for eyes, hairlines
            ' for hair, MouthShadowFemale/teeth for face, etc. Without expanding them the picker
            ' renders only the parent geometry and the user sees an incomplete head part.
            ' Shared HNAM-chain enumerator (also used by FaceGenBuilder).
            Dim allShapes As New List(Of IRenderableShape)
            Dim chainCount As Integer = 0
            For Each hdpt In HeadPartResolver.EnumerateHdptChain({c.FormID}, _pluginManager)
                chainCount += 1
                If String.IsNullOrEmpty(hdpt.MeshPath) Then Continue For

                ' Resolve the NIF bytes via FilesDictionary (same path MainForm.vb:7305 uses).
                Dim dictKey = NormalizeMeshKey(hdpt.MeshPath)
                Dim loc As FilesDictionary_class.File_Location = Nothing
                If Not FilesDictionary_class.Dictionary.TryGetValue(dictKey, loc) Then Continue For
                Dim bytes As Byte() = Nothing
                Try
                    bytes = loc.GetBytes()
                Catch
                End Try
                If bytes Is Nothing OrElse bytes.Length = 0 Then Continue For

                Dim nif As New Nifcontent_Class_Manolo()
                Try
                    nif.Load_Manolo(bytes)
                Catch
                    Continue For
                End Try

                Dim shapes = NifRenderableShape.FromNif(nif)
                If shapes Is Nothing OrElse Not shapes.Any() Then Continue For

                ' Apply this HDPT's TextureSet override (TNAM) to its own shapes only — extras
                ' carry their own TNAM and apply it on their own iteration. Vanilla eye HDPTs
                ' share femaleeyes.nif but each FemaleEyesHumanBlue/Brown/etc. has its own TXST,
                ' so without this pass every eye colour renders the default brown.
                If hdpt.TextureSetFormID <> 0UI Then
                    Dim txstRec = _pluginManager.GetRecord(hdpt.TextureSetFormID)
                    If txstRec IsNot Nothing AndAlso txstRec.Header.Signature = "TXST" Then
                        Dim txst = RecordParsers.ParseTXST(txstRec, _pluginManager)
                        If txst IsNot Nothing Then
                            For Each shape In shapes
                                MainForm.EnsureShapeMaterialResolved(shape)
                                Dim relatedMaterial = shape.ShapeMaterial
                                If relatedMaterial Is Nothing Then Continue For
                                MainForm.ApplyTextureSetOverrides(txst, relatedMaterial, hdpt.UsesBodyTexture, shape.NifShape, shape.NifContent)
                            Next
                        End If
                    End If
                End If

                allShapes.AddRange(shapes)
            Next

            If chainCount = 0 Then
                ClearPreview("(record not found)")
                Return
            End If
            If allShapes.Count = 0 Then
                ClearPreview("(no renderable shapes resolved for this HDPT chain)")
                Return
            End If

            ' Synchronous, single-call entry point: PreviewControl applies sane defaults
            ' (no skinning resolver, no morph resolver, identity pose) and runs the pipeline.
            _preview.RenderShapes(allShapes)
            _lastPreviewFormID = c.FormID
        Finally
            _previewLoadInProgress = False
        End Try
    End Sub

    Private Sub ClearPreview(statusMessage As String)
        _lastPreviewFormID = 0UI
        ' Render an empty shape list — the pipeline detects empty shapes and clears the model
        ' (Render.vb:502-512), so the preview goes blank without leaking the previous mesh.
        Try
            _preview?.RenderShapes(New List(Of IRenderableShape))
        Catch
        End Try
    End Sub

    ''' <summary>FilesDictionary keys are lowercase paths starting with "meshes\". HDPT.MeshPath
    ''' from the record may or may not include the "meshes\" prefix — normalize both shapes.</summary>
    Private Shared Function NormalizeMeshKey(rawPath As String) As String
        If String.IsNullOrEmpty(rawPath) Then Return ""
        Dim p = rawPath.Replace("/"c, "\"c).Trim().ToLowerInvariant()
        If Not p.StartsWith("meshes\") Then p = "meshes\" & p
        Return p
    End Function

    Private Sub OnOk(sender As Object, e As EventArgs)
        If ListViewParts.SelectedItems.Count = 0 Then
            DialogResult = DialogResult.None
            Return
        End If
        Dim c = TryCast(ListViewParts.SelectedItems(0).Tag, Candidate)
        If c Is Nothing Then Return
        SelectedFormID = c.FormID
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Sub HeadPartPicker_Form_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        If _preview Is Nothing OrElse _preview.IsDisposed Then
            _preview = New PreviewControl() With {.Dock = DockStyle.Fill}
            PreviewControlPanel.Controls.Add(_preview)
            _preview.BringToFront()
            _preview.ApplyResize(True)
        End If

        If ListViewParts.SelectedItems.Count > 0 Then
            OnListSelectionChanged(Me, EventArgs.Empty)
        Else
            ClearPreview("(no head part selected)")
        End If
    End Sub

    Private Sub HeadPartPicker_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        If _preview IsNot Nothing AndAlso Not _preview.IsDisposed Then
            _preview.Clean()
            _preview.Dispose()
        End If
    End Sub
End Class
