Imports System.IO
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FO4_Base_Library
Imports MaterialLib
Imports NiflySharp.Blocks

Public Class MainForm

    ''' <summary>Two-step skin tint experiment. When True:
    '''   - Face: slot 12 SkinTone is composited as a normal Palette layer (with its authored
    '''     SoftLight blendOp from TTEC) instead of being skipped. The render shader still
    '''     multiplies by material.SkinTintColor afterwards (slot 12 RGB), so the final pixel
    '''     becomes softlight(base, slot12) * slot12.
    '''   - Body: a one-shot SoftLight pre-pass paints QNAM RGB onto the body diffuse before
    '''     render. Render still multiplies by material.SkinTintColor (QNAM RGB), so the
    '''     final pixel becomes softlight(base, qnam) * qnam — symmetric with face.
    ''' When False: legacy behaviour. Slot 12 is skipped on the face, body gets only the
    ''' render-shader uniform multiply. Use this flag to A/B compare without recompiling
    ''' material code.</summary>
    Private Const ENABLE_TWO_STEP_SKIN_TINT As Boolean = True

    Private ReadOnly _pluginManager As New PluginManager()
    Private _allNPCs As New List(Of NPC_Data)
    Private _previewControl As PreviewControl
    Private _dataPath As String = ""
    Private _assetDictionaryLoadTask As Task = Nothing
    Private ReadOnly _assetDictionaryLock As New Object()
    Private _previewRequestVersion As Integer = 0
    Private Shared ReadOnly _rng As New Random()
    Private ReadOnly _variantCache As New Dictionary(Of UInteger, List(Of PreviewVariantDefinition))
    Private _npcByIdCache As New Dictionary(Of UInteger, NPC_Data)()
    Private _templateDependencyMapCache As New Dictionary(Of UInteger, List(Of TemplateDependencyEdge))()
    Private _templateRootSourceIdsCache As New List(Of UInteger)()
    ''' <summary>NPCs directly placed in the world via ACHR records (unique characters).</summary>
    Private _directlyPlacedNPCFormIDs As New HashSet(Of UInteger)()
    ''' <summary>NPCs that appear in the game world: placed in CELLs (ACHR) or in LVLN encounter lists.</summary>
    Private _npcsInGameWorld As New HashSet(Of UInteger)()
    ''' <summary>NPCs that are referenced as template source (TPLT/TPTA) by other NPCs.</summary>
    Private _npcsUsedAsTemplates As New HashSet(Of UInteger)()
    ''' <summary>Final LVLNs: leveled NPC lists that are NOT nested inside another LVLN.</summary>
    Private _finalLVLNFormIDs As New List(Of UInteger)()
    ''' <summary>Parsed LVLN data cache keyed by FormID.</summary>
    Private _lvlnDataCache As New Dictionary(Of UInteger, LVLN_Data)()
    Private _pendingTreeFilter As String = ""
    Private WithEvents _searchDebounceTimer As New System.Windows.Forms.Timer()

    ' Deferred face tint application — the texture cache is async (Render.vb queues uploads
    ' and processes them per-frame), so when ApplyFaceTintOverlay runs right after RenderShapes
    ' the face diffuse texture may not be in Textures_Dictionary yet. We poll on this timer
    ' until it appears, then bake the tints once and stop the timer.
    Private WithEvents _pendingTintTimer As New System.Windows.Forms.Timer With {.Interval = 120}
    Private _pendingTintState As NPCVisualState = Nothing
    Private _pendingTintAttempts As Integer = 0
    Private Const PendingTintMaxAttempts As Integer = 60   ' 60 × 120ms = ~7.2s upper bound

    Private Const HeadPartFlagUseSolidTint As Byte = &H20
    Private Const HeadPartTypeFace As Integer = 1
    Private Const HeadPartTypeEyes As Integer = 2
    Private Const HeadPartTypeHair As Integer = 3
    Private Const HeadPartTypeFacialHair As Integer = 4

    Private Enum PreviewMode
        FullCharacter = 0
        OnlyFace = 1
    End Enum

    Private Enum GenderFilterMode
        Random = 0
        Male = 1
        Female = 2
    End Enum

    Private ReadOnly Property CurrentGenderFilter As GenderFilterMode
        Get
            If ComboBoxGender.InvokeRequired Then
                Return CType(ComboBoxGender.Invoke(Function() ComboBoxGender.SelectedIndex), GenderFilterMode)
            End If
            Return CType(Math.Max(0, ComboBoxGender.SelectedIndex), GenderFilterMode)
        End Get
    End Property

    Private Enum MeshCandidateKind
        Skin = 0
        Outfit = 1
        HeadPart = 2
    End Enum

    Private Class MeshCandidate
        Public DictKey As String = ""
        Public SlotMask As UInteger
        Public Priority As Integer
        Public Kind As MeshCandidateKind
        Public SourceFormID As UInteger
        Public ArmorAddonFormID As UInteger
        Public MaterialSwapFormID As UInteger
        Public ColorRemapIndex As Nullable(Of Single)
        Public HeadPartType As Integer = -1
        Public HeadPartColorFormID As UInteger
        Public TextureSetFormID As UInteger
        Public UseSolidTint As Boolean
        Public UsesBodyTexture As Boolean
        Public FaceGenTexturePrefix As String = ""
        Public Order As Integer
        ' From HDPT NAM0/NAM1 pairs (face head parts only, normally empty otherwise)
        Public RaceMorphTriPath As String = ""
        Public ChargenMorphTriPath As String = ""
    End Class

    Private Class ResolvedBranch(Of T)
        Public SourceNpcFormID As UInteger
        Public Value As T
    End Class

    Private Class PreviewVariantDefinition
        Public RootNpcFormID As UInteger
        Public VariantId As Integer
        Public DisplayName As String = ""
        Public State As NPCVisualState
        Public UseFaceGen As Boolean
        Public ReadOnly Warnings As New List(Of String)
    End Class

    Private Class TemplateDependencyEdge
        Public SourceFormID As UInteger
        Public DependentNpc As NPC_Data
        Public Categories As New List(Of String)
    End Class
    Private Class PreviewResolutionResult
        Public ReadOnly Shapes As New List(Of IRenderableShape)
        Public SkeletonKey As String = ""
        Public ReadOnly Warnings As New List(Of String)
        ''' <summary>Shape reference -> mesh dictionary key path (for TRI file lookup).</summary>
        Public ReadOnly MeshDictKeys As New Dictionary(Of IRenderableShape, String)
        ''' <summary>Shape reference -> chargen morph TRI path (from HDPT NAM0=2/NAM1).</summary>
        Public ReadOnly ShapeChargenTriPaths As New Dictionary(Of IRenderableShape, String)
        ''' <summary>Shape reference -> race morph TRI path (from HDPT NAM0=0/NAM1, expression file).</summary>
        Public ReadOnly ShapeRaceMorphTriPaths As New Dictionary(Of IRenderableShape, String)
    End Class
    Private Class TraitsState
        Public IsFemale As Boolean
        Public RaceFormID As UInteger
        Public SkinFormID As UInteger
        Public WeightThin As Single
        Public WeightMuscular As Single
        Public WeightFat As Single
    End Class

    Private Class InventoryState
        Public DefaultOutfitFormID As UInteger
        Public SleepOutfitFormID As UInteger
    End Class

    Private Class ModelAnimationState
        Public HeadTextureFormID As UInteger
        Public HairColorFormID As UInteger
        Public FacialHairColorFormID As UInteger
        Public HasTextureLighting As Boolean
        Public TextureLightingColor As Color = Color.Empty
        Public HeadPartFormIDs As New List(Of UInteger)
    End Class

    Private Class NPCVisualState
        Public FormID As UInteger
        Public RootNpcFormID As UInteger
        Public TraitsSourceFormID As UInteger
        Public InventorySourceFormID As UInteger
        Public ModelSourceFormID As UInteger
        Public VariantLabel As String = ""
        Public IsFemale As Boolean
        Public RaceFormID As UInteger
        Public SkinFormID As UInteger
        Public DefaultOutfitFormID As UInteger
        Public SleepOutfitFormID As UInteger
        Public HeadTextureFormID As UInteger
        Public HairColorFormID As UInteger
        Public FacialHairColorFormID As UInteger
        Public HasTextureLighting As Boolean
        Public TextureLightingColor As Color = Color.Empty
        Public HeadPartFormIDs As New List(Of UInteger)
        Public LoadoutArmorFormIDs As New List(Of UInteger)
        Public WeightThin As Single
        Public WeightMuscular As Single
        Public WeightFat As Single
    End Class

    Private ReadOnly Property CurrentPreviewMode As PreviewMode
        Get
            If ComboBoxPreviewMode.InvokeRequired Then
                Return CType(ComboBoxPreviewMode.Invoke(Function() ComboBoxPreviewMode.SelectedIndex), PreviewMode)
            End If
            Return CType(Math.Max(0, ComboBoxPreviewMode.SelectedIndex), PreviewMode)
        End Get
    End Property

    Private Sub ComboBoxPreviewMode_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBoxPreviewMode.SelectedIndexChanged
        ' Re-render the currently selected node with new preview mode
        If _currentBaseState Is Nothing Then Return
        Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
        RenderOnDemandAsync(requestVersion)
    End Sub

    ''' <summary>Toggle the FMRS face bone pose in-place without re-extracting geometry.
    ''' WM-style granular pipeline: mutate the existing RenderIntent, flip Intent.Pose, mark
    ''' RenderDirtyFlags.Pose, InvalidateRender. The pipeline's needsPoseUpdate path re-runs
    ''' the skeleton step and recomputes bone matrices — it does NOT reload shapes.
    '''
    ''' When toggling OFF we pass an EMPTY Poses_class (not Nothing) so that
    ''' FaceBoneSkeletonResolver actually calls AppplyPoseToSkeleton — which runs Reset()
    ''' first (clearing all DeltaTransforms) and then iterates an empty dict. Passing
    ''' Nothing would make the resolver's "If pose IsNot Nothing" guard skip the reset
    ''' and leave the previous deltas pegged on the SkeletonDictionary.</summary>
    Private Sub CheckBoxApplyBoneMorphs_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxApplyBoneMorphs.CheckedChanged
        If _lastRenderedState Is Nothing OrElse _lastRenderData Is Nothing Then Return
        Dim newPose As Poses_class
        If CheckBoxApplyBoneMorphs.Checked Then
            newPose = BuildFaceBoneTransforms(_lastRenderedState)
            If newPose Is Nothing Then newPose = BuildEmptyFacePose()
        Else
            newPose = BuildEmptyFacePose()
        End If
        Dim intent = _previewControl.Intent
        intent.Pose = newPose
        intent.MarkDirty(RenderDirtyFlags.Pose)
        _previewControl.InvalidateRender()
    End Sub

    ''' <summary>Build an empty (no transforms) Poses_class that, when passed through the
    ''' face bone skeleton resolver, triggers AppplyPoseToSkeleton's internal Reset() without
    ''' applying any deltas. Used to clear the previous FMRS state when toggling the bone
    ''' morphs checkbox OFF.</summary>
    Private Shared Function BuildEmptyFacePose() As Poses_class
        Return New Poses_class With {
            .Name = "__reset_face_pose__",
            .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
            .Transforms = New Dictionary(Of String, PoseTransformData)
        }
    End Function

    ''' <summary>Toggle the chargen vertex morph resolver in-place without re-extracting
    ''' geometry. WM-style granular: mutate Intent.MorphResolver, mark RenderDirtyFlags.Morphs,
    ''' InvalidateRender. The pipeline's needsMorphUpdate path re-runs PipelineStep_Morphs
    ''' which restarts from NifLocalVertices (raw pre-skinning) and applies the plan fresh.
    '''
    ''' When toggling OFF we cannot just set MorphResolver=Nothing, because PipelineStep_Morphs
    ''' early-returns in that case and the previously-applied deltas stay pegged on geom.Vertices.
    ''' Instead we swap in a ResetMorphResolver that returns a no-op plan (one empty channel)
    ''' — that forces ApplyMorphPlan to execute, which unconditionally writes
    ''' geom.Vertices = NifLocalVertices.ToArray() + (no deltas) = raw.</summary>
    Private Sub CheckBoxApplyVertexMorphs_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxApplyVertexMorphs.CheckedChanged
        If _lastRenderedState Is Nothing OrElse _lastRenderData Is Nothing Then Return
        Dim newResolver As IMorphResolver
        If CheckBoxApplyVertexMorphs.Checked Then
            newResolver = BuildFaceMorphResolver(_lastRenderedState, _lastRenderData)
        Else
            newResolver = New ResetMorphResolver()
        End If
        Dim intent = _previewControl.Intent
        intent.MorphResolver = newResolver
        intent.MarkDirty(RenderDirtyFlags.Morphs)
        _previewControl.InvalidateRender()
    End Sub

    ''' <summary>No-op IMorphResolver used to reset geom.Vertices back to raw NifLocalVertices
    ''' when toggling vertex morphs OFF. See CheckBoxApplyVertexMorphs_CheckedChanged for the
    ''' reasoning (PipelineStep_Morphs early-returns on Nothing resolver, but a plan with one
    ''' empty channel runs through ApplyMorphPlan and writes geom.Vertices = raw).</summary>
    Private Class ResetMorphResolver
        Implements IMorphResolver
        Public Function ResolveMorphPlan(shape As IRenderableShape, geom As SkinnedGeometry) As MorphPlan Implements IMorphResolver.ResolveMorphPlan
            Dim plan As New MorphPlan
            plan.Channels.Add(New MorphChannel("__reset__", 0.0F, New List(Of MorphData)()))
            Return plan
        End Function
    End Class

    Private Sub MainForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        _searchDebounceTimer.Interval = 250
        Config_App.Current.Game = Config_App.Game_Enum.Fallout4
        NpcPreviewLog.Initialize()
        InitializePreview()
        LoadDataAsync()
    End Sub

    Private Sub InitializePreview()
        _previewControl = New PreviewControl()
        _previewControl.Dock = DockStyle.Fill
        ' Remove only the LabelStatus placeholder, keep PanelPreviewToolbar
        If LabelStatus IsNot Nothing AndAlso PanelRight.Controls.Contains(LabelStatus) Then
            PanelRight.Controls.Remove(LabelStatus)
        End If
        PanelRight.Controls.Add(_previewControl)
        ' Ensure toolbar stays on top (Dock.Top renders above Dock.Fill)
        PanelPreviewToolbar.BringToFront()
    End Sub

    Private Async Sub LoadDataAsync()
        Try
            SetStatus("Locating Fallout 4 Data folder...")
            ToolStripProgressBar1.Visible = False

            _dataPath = FindFO4DataPath()
            If String.IsNullOrEmpty(_dataPath) Then
                SetStatus("Fallout 4 Data folder not found. Configure in settings.")
                Return
            End If

            SetStatus("Loading plugins...")
            Dim espProg = New Progress(Of String)(Sub(msg) SetStatus(msg))
            Await Task.Run(Sub() _pluginManager.LoadAllPlugins(_dataPath, espProg))

            SetStatus("Parsing NPC records...")
            Await Task.Run(Sub() ParseAllNPCs())

            PopulateNPCTree()

            ToolStripProgressBar1.Visible = False
            SetStatus($"Loaded {_directlyPlacedNPCFormIDs.Count} placed NPCs + {_finalLVLNFormIDs.Count} leveled lists from {_pluginManager.Plugins.Count} plugins")

        Catch ex As Exception
            SetStatus($"Error: {ex.Message}")
            MessageBox.Show(ex.ToString(), "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub ParseAllNPCs()
        _allNPCs.Clear()
        SyncLock _variantCache
            _variantCache.Clear()
        End SyncLock

        Dim npcRecords = _pluginManager.GetNPCs()
        For Each rec In npcRecords
            Try
                Dim pluginName = If(rec.SourcePluginName <> "", rec.SourcePluginName, "Unknown")
                Dim npc = RecordParsers.ParseNPC(rec, pluginName, _pluginManager)
                _allNPCs.Add(npc)
            Catch
            End Try
        Next
        ' Resolve inherited FullName for NPCs that inherit BaseData from a template
        ResolveInheritedFullNames()
        _allNPCs.Sort(Function(a, b) String.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase))
        RebuildTreeModelCache()
    End Sub

    ''' <summary>For NPCs with no FullName that inherit BaseData from a template, resolve the name from the chain.</summary>
    Private Sub ResolveInheritedFullNames()
        For Each npc In _allNPCs
            If npc.FullName <> "" Then Continue For
            If Not HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.BaseData) Then Continue For

            Dim sourceFormID = ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.BaseData)
            Dim resolved = ResolveInheritedFullName(sourceFormID, New HashSet(Of UInteger)())
            If resolved <> "" Then npc.FullName = resolved
        Next
    End Sub

    Private Function ResolveInheritedFullName(formID As UInteger, visited As HashSet(Of UInteger)) As String
        If formID = 0UI OrElse visited.Contains(formID) Then Return ""
        visited.Add(formID)

        Dim rec = _pluginManager.GetRecord(formID)
        If rec Is Nothing Then Return ""

        Select Case rec.Header.Signature
            Case "NPC_"
                Dim npc = RecordParsers.ParseNPC(rec, "", _pluginManager)
                If npc.FullName <> "" Then Return npc.FullName
                ' Follow BaseData chain if this NPC also inherits
                If HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.BaseData) Then
                    Return ResolveInheritedFullName(ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.BaseData), visited)
                End If
            Case "LVLN"
                ' Pick first NPC entry from the LVLN to get a representative name
                Dim lvln = RecordParsers.ParseLVLN(rec, _pluginManager)
                For Each entry In lvln.Entries
                    If entry.FormID = 0UI Then Continue For
                    Dim resolved = ResolveInheritedFullName(entry.FormID, visited)
                    If resolved <> "" Then Return resolved
                Next
        End Select

        Return ""
    End Function

    Private Sub RebuildTreeModelCache()
        _npcByIdCache = _allNPCs.GroupBy(Function(n) n.FormID).Select(Function(g) g.First()).ToDictionary(Function(n) n.FormID)
        _templateDependencyMapCache = BuildTemplateDependencyMap(_npcByIdCache)
        _templateRootSourceIdsCache = BuildTemplateTreeRootSourceIds(_npcByIdCache, _templateDependencyMapCache)
        BuildNPCClassification()
    End Sub

    ''' <summary>Classify NPCs:
    ''' - _directlyPlacedNPCFormIDs: NPCs placed in CELLs via ACHR records (unique characters).
    ''' - _npcsInGameWorld: All NPCs in game (placed + LVLN encounters).
    ''' - _npcsUsedAsTemplates: NPCs referenced as TPLT/TPTA source by other NPCs.
    ''' - _finalLVLNFormIDs: Top-level LVLNs not nested inside another LVLN.
    ''' A "template only" NPC is one used as template but never placed or in any LVLN.</summary>
    Private Sub BuildNPCClassification()
        _directlyPlacedNPCFormIDs.Clear()
        _npcsInGameWorld.Clear()
        _npcsUsedAsTemplates.Clear()
        _finalLVLNFormIDs.Clear()
        _lvlnDataCache.Clear()

        ' Collect NPCs placed in the world (ACHR records from CELL/WRLD groups)
        Dim placedNPCs = _pluginManager.GetPlacedNPCFormIDs()
        _directlyPlacedNPCFormIDs.UnionWith(placedNPCs)
        _npcsInGameWorld.UnionWith(placedNPCs)

        ' Parse and cache all LVLN records; track which LVLNs are nested inside others
        Dim nestedLVLNFormIDs As New HashSet(Of UInteger)()
        Dim allLVLNRecords = _pluginManager.GetRecordsOfType("LVLN")

        For Each rec In allLVLNRecords
            Dim lvln = RecordParsers.ParseLVLN(rec, _pluginManager)
            _lvlnDataCache(lvln.FormID) = lvln

            For Each entry In lvln.Entries
                If entry.FormID = 0UI Then Continue For
                Dim entryRec = _pluginManager.GetRecord(entry.FormID)
                If entryRec IsNot Nothing AndAlso entryRec.Header.Signature = "LVLN" Then
                    nestedLVLNFormIDs.Add(entry.FormID)
                End If
            Next
        Next

        ' Final LVLNs = all LVLNs that are NOT referenced as entries inside another LVLN
        For Each lvlnFormID In _lvlnDataCache.Keys
            If Not nestedLVLNFormIDs.Contains(lvlnFormID) Then
                _finalLVLNFormIDs.Add(lvlnFormID)
            End If
        Next
        _finalLVLNFormIDs.Sort(Function(a, b)
                                   Dim lvlnA = _lvlnDataCache(a)
                                   Dim lvlnB = _lvlnDataCache(b)
                                   Return String.Compare(lvlnA.EditorID, lvlnB.EditorID, StringComparison.OrdinalIgnoreCase)
                               End Function)

        ' Collect NPCs in leveled lists (encounter spawns)
        For Each rec In allLVLNRecords
            CollectNPCsFromLVLNRecursive(rec.Header.FormID, _npcsInGameWorld, New HashSet(Of UInteger)())
        Next

        ' Scan all NPCs to find which are used as template sources
        For Each npc In _allNPCs
            If npc.TemplateFormID <> 0UI Then
                Dim rec = _pluginManager.GetRecord(npc.TemplateFormID)
                If rec IsNot Nothing AndAlso rec.Header.Signature = "NPC_" Then
                    _npcsUsedAsTemplates.Add(npc.TemplateFormID)
                End If
            End If
            For Each kvp In npc.TemplateActorFormIDs
                If kvp.Value = 0UI Then Continue For
                Dim rec = _pluginManager.GetRecord(kvp.Value)
                If rec IsNot Nothing AndAlso rec.Header.Signature = "NPC_" Then
                    _npcsUsedAsTemplates.Add(kvp.Value)
                End If
            Next
        Next

        NpcPreviewLog.Log($"[CLASSIFICATION] {placedNPCs.Count} placed (ACHR), {_npcsInGameWorld.Count} total in-game, {_npcsUsedAsTemplates.Count} used as templates, {_finalLVLNFormIDs.Count} final LVLNs")
    End Sub

    Private Sub CollectNPCsFromLVLNRecursive(lvlnFormID As UInteger, result As HashSet(Of UInteger), visited As HashSet(Of UInteger))
        If lvlnFormID = 0UI OrElse visited.Contains(lvlnFormID) Then Return
        Dim rec = _pluginManager.GetRecord(lvlnFormID)
        If rec Is Nothing OrElse rec.Header.Signature <> "LVLN" Then Return

        visited.Add(lvlnFormID)
        Dim lvln = RecordParsers.ParseLVLN(rec, _pluginManager)

        For Each entry In lvln.Entries
            If entry.FormID = 0UI Then Continue For
            Dim entryRec = _pluginManager.GetRecord(entry.FormID)
            If entryRec Is Nothing Then Continue For

            Select Case entryRec.Header.Signature
                Case "NPC_"
                    result.Add(entry.FormID)
                Case "LVLN"
                    CollectNPCsFromLVLNRecursive(entry.FormID, result, visited)
            End Select
        Next
    End Sub

    ''' <summary>True if this NPC is only used as a template source and never placed in the world or in any LVLN.</summary>
    Private Function IsTemplateOnly(npc As NPC_Data) As Boolean
        If npc Is Nothing Then Return False
        Return _npcsUsedAsTemplates.Contains(npc.FormID) AndAlso Not _npcsInGameWorld.Contains(npc.FormID)
    End Function

    ''' <summary>True if this NPC inherits its visual appearance (Traits or ModelAnimation) from any template.
    ''' Such NPCs are generic — their look is defined by the template chain, not by themselves.</summary>
    Private Shared Function NpcInheritsVisualAppearance(npc As NPC_Data) As Boolean
        If npc Is Nothing OrElse npc.TemplateFlags = 0US Then Return False
        Return HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.Traits) OrElse
               HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.ModelAnimation)
    End Function

    ''' <summary>Check if this NPC has any LVLN in its direct TPLT or TPTA references.
    ''' These NPCs produce different results each time they're resolved (different face, gender, etc).</summary>
    Private Function NpcHasLeveledTemplates(npc As NPC_Data) As Boolean
        If npc Is Nothing OrElse npc.TemplateFlags = 0US Then Return False

        ' Check TPLT
        If npc.TemplateFormID <> 0UI Then
            Dim rec = _pluginManager.GetRecord(npc.TemplateFormID)
            If rec IsNot Nothing AndAlso rec.Header.Signature = "LVLN" Then Return True
        End If

        ' Check TPTA entries
        For Each kvp In npc.TemplateActorFormIDs
            If kvp.Value = 0UI Then Continue For
            Dim rec = _pluginManager.GetRecord(kvp.Value)
            If rec IsNot Nothing AndAlso rec.Header.Signature = "LVLN" Then Return True
        Next

        Return False
    End Function

    Private Sub PopulateNPCTree(Optional filter As String = "")
        If InvokeRequired Then
            Invoke(Sub() PopulateNPCTree(filter))
            Return
        End If

        If (_npcByIdCache Is Nothing OrElse _npcByIdCache.Count = 0) AndAlso _allNPCs.Count > 0 Then
            RebuildTreeModelCache()
        End If

        Dim normalizedFilter = If(filter, "").Trim()

        TreeViewNPCs.SuspendLayout()
        TreeViewNPCs.BeginUpdate()
        TreeViewNPCs.Nodes.Clear()

        Try
            ' === Section 1: Placed NPCs (unique characters with ACHR in the world) ===
            ' Only show NPCs that are directly placed AND define their own visual appearance.
            ' NPCs that inherit Traits or ModelAnimation from ANY template are generic
            ' (raiders, gunners, etc.) — their appearance comes from the template chain,
            ' so they are represented by the LVLN entries in section 2.
            Dim placedNpcs = _allNPCs.
                Where(Function(n) _directlyPlacedNPCFormIDs.Contains(n.FormID) AndAlso Not NpcInheritsVisualAppearance(n)).
                GroupBy(Function(n) If(n.PluginName, "Unknown")).
                OrderBy(Function(g) g.Key, StringComparer.OrdinalIgnoreCase)

            For Each pluginGroup In placedNpcs
                Dim pluginNode As TreeNode = Nothing
                Dim matchCount = 0

                For Each npc In pluginGroup.OrderBy(Function(n) n.ToString(), StringComparer.OrdinalIgnoreCase)
                    If normalizedFilter.Length > 0 AndAlso Not MatchesNpcFilter(npc, Nothing, normalizedFilter) Then Continue For

                    If pluginNode Is Nothing Then
                        pluginNode = New TreeNode(pluginGroup.Key) With {
                            .Name = $"PLUGIN_{pluginGroup.Key}",
                            .Tag = Nothing
                        }
                    End If

                    Dim displayText = If(npc.FullName <> "" AndAlso npc.EditorID <> "",
                                         $"{npc.FullName} [{npc.EditorID}]",
                                         If(npc.FullName <> "", npc.FullName,
                                         If(npc.EditorID <> "", npc.EditorID, npc.FormID.ToString("X8"))))

                    Dim templateInfo = GetNpcTemplateSummary(npc)
                    If templateInfo <> "" Then displayText &= $" ({templateInfo})"

                    Dim npcNode = New TreeNode(displayText) With {
                        .Name = $"NPC_{npc.FormID:X8}",
                        .Tag = npc
                    }
                    pluginNode.Nodes.Add(npcNode)
                    matchCount += 1
                Next

                If pluginNode IsNot Nothing Then
                    pluginNode.Text = $"{pluginGroup.Key} ({matchCount})"
                    TreeViewNPCs.Nodes.Add(pluginNode)
                    If normalizedFilter.Length > 0 Then pluginNode.Expand()
                End If
            Next

            ' === Section 2: Final Leveled NPC Lists (encounter spawns) ===
            If _finalLVLNFormIDs.Count > 0 Then
                ' Group final LVLNs by source plugin
                Dim lvlnsByPlugin = _finalLVLNFormIDs.
                    Select(Function(fid)
                               Dim rec = _pluginManager.GetRecord(fid)
                               Dim lvln = If(_lvlnDataCache.ContainsKey(fid), _lvlnDataCache(fid), Nothing)
                               Return (FormID:=fid, Record:=rec, Data:=lvln)
                           End Function).
                    Where(Function(x) x.Record IsNot Nothing AndAlso x.Data IsNot Nothing).
                    GroupBy(Function(x) If(x.Record.SourcePluginName, "Unknown")).
                    OrderBy(Function(g) g.Key, StringComparer.OrdinalIgnoreCase)

                For Each pluginGroup In lvlnsByPlugin
                    Dim pluginNode As TreeNode = Nothing
                    Dim matchCount = 0

                    For Each item In pluginGroup.OrderBy(Function(x) x.Data.EditorID, StringComparer.OrdinalIgnoreCase)
                        If normalizedFilter.Length > 0 AndAlso Not MatchesRecordFilter(item.Record, normalizedFilter) Then Continue For

                        If pluginNode Is Nothing Then
                            pluginNode = New TreeNode($"[LVLN] {pluginGroup.Key}") With {
                                .Name = $"LVLN_PLUGIN_{pluginGroup.Key}",
                                .Tag = Nothing
                            }
                        End If

                        Dim leafCount = CountLVLNLeafNPCs(item.FormID, New HashSet(Of UInteger)())
                        Dim label = If(item.Data.EditorID <> "", item.Data.EditorID, item.FormID.ToString("X8"))
                        Dim displayText = $"{label} ({leafCount} NPCs)"

                        Dim lvlnNode = New TreeNode(displayText) With {
                            .Name = $"LVLN_{item.FormID:X8}",
                            .Tag = item.Data
                        }
                        pluginNode.Nodes.Add(lvlnNode)
                        matchCount += 1
                    Next

                    If pluginNode IsNot Nothing Then
                        pluginNode.Text = $"[LVLN] {pluginGroup.Key} ({matchCount})"
                        TreeViewNPCs.Nodes.Add(pluginNode)
                        If normalizedFilter.Length > 0 Then pluginNode.Expand()
                    End If
                Next
            End If
        Finally
            TreeViewNPCs.EndUpdate()
            TreeViewNPCs.ResumeLayout()
        End Try
    End Sub

    ''' <summary>Count the total number of leaf NPC_ entries reachable from a LVLN (recursing into nested LVLNs).</summary>
    Private Function CountLVLNLeafNPCs(lvlnFormID As UInteger, visited As HashSet(Of UInteger)) As Integer
        If lvlnFormID = 0UI OrElse visited.Contains(lvlnFormID) Then Return 0
        visited.Add(lvlnFormID)

        Dim lvln As LVLN_Data = Nothing
        If Not _lvlnDataCache.TryGetValue(lvlnFormID, lvln) Then Return 0

        Dim count = 0
        For Each entry In lvln.Entries
            If entry.FormID = 0UI Then Continue For
            Dim entryRec = _pluginManager.GetRecord(entry.FormID)
            If entryRec Is Nothing Then Continue For

            Select Case entryRec.Header.Signature
                Case "NPC_"
                    count += 1
                Case "LVLN"
                    count += CountLVLNLeafNPCs(entry.FormID, visited)
            End Select
        Next
        Return count
    End Function

    Private Function GetNpcTemplateSummary(npc As NPC_Data) As String
        If npc Is Nothing OrElse npc.TemplateFlags = 0US Then Return ""
        Dim parts As New List(Of String)
        For Each boxedCategory In [Enum].GetValues(GetType(NPC_TemplateCategory))
            Dim category = CType(boxedCategory, NPC_TemplateCategory)
            If HasTemplateFlag(npc.TemplateFlags, category) Then
                parts.Add(GetTemplateCategoryLabel(category))
            End If
        Next
        If parts.Count = 0 Then Return ""
        Return "tmpl: " & String.Join(", ", parts)
    End Function

    Private Function BuildTemplateDependencyMap(npcById As IReadOnlyDictionary(Of UInteger, NPC_Data)) As Dictionary(Of UInteger, List(Of TemplateDependencyEdge))
        Dim dependencyMap As New Dictionary(Of UInteger, List(Of TemplateDependencyEdge))

        For Each npc In npcById.Values
            Dim groupedEdges As New Dictionary(Of UInteger, TemplateDependencyEdge)

            For Each dependency In GetTemplateDependencies(npc)
                If Not IsSupportedTemplateSource(dependency.Key, npcById) Then Continue For

                Dim edge As TemplateDependencyEdge = Nothing
                If Not groupedEdges.TryGetValue(dependency.Key, edge) Then
                    edge = New TemplateDependencyEdge With {
                        .SourceFormID = dependency.Key,
                        .DependentNpc = npc
                    }
                    groupedEdges(dependency.Key) = edge
                End If

                If Not edge.Categories.Contains(dependency.Value) Then
                    edge.Categories.Add(dependency.Value)
                End If
            Next

            For Each edge In groupedEdges.Values
                Dim edges As List(Of TemplateDependencyEdge) = Nothing
                If Not dependencyMap.TryGetValue(edge.SourceFormID, edges) Then
                    edges = New List(Of TemplateDependencyEdge)()
                    dependencyMap(edge.SourceFormID) = edges
                End If
                edges.Add(edge)
            Next
        Next

        For Each edges In dependencyMap.Values
            edges.Sort(Function(left, right)
                           Dim compare = StringComparer.OrdinalIgnoreCase.Compare(GetNpcNodeDisplayText(left.DependentNpc, left), GetNpcNodeDisplayText(right.DependentNpc, right))
                           If compare <> 0 Then Return compare
                           Return left.DependentNpc.FormID.CompareTo(right.DependentNpc.FormID)
                       End Function)
        Next

        Return dependencyMap
    End Function

    Private Function BuildTemplateTreeRootSourceIds(npcById As IReadOnlyDictionary(Of UInteger, NPC_Data), dependencyMap As Dictionary(Of UInteger, List(Of TemplateDependencyEdge))) As List(Of UInteger)
        Dim rootIds As New HashSet(Of UInteger)()
        Dim dependentNpcIds As New HashSet(Of UInteger)()

        For Each edges In dependencyMap.Values
            For Each edge In edges
                dependentNpcIds.Add(edge.DependentNpc.FormID)
            Next
        Next

        For Each sourceId In dependencyMap.Keys
            If Not dependentNpcIds.Contains(sourceId) AndAlso IsSupportedTemplateSource(sourceId, npcById) Then
                rootIds.Add(sourceId)
            End If
        Next

        For Each npc In npcById.Values
            If Not GetTemplateDependencies(npc).Any(Function(dep) IsSupportedTemplateSource(dep.Key, npcById)) Then
                rootIds.Add(npc.FormID)
            End If
        Next

        Dim reachableNpcIds = CollectReachableNpcIds(rootIds, dependencyMap, npcById)
        For Each npcId In npcById.Keys
            If Not reachableNpcIds.Contains(npcId) Then
                rootIds.Add(npcId)
            End If
        Next

        Return rootIds.ToList()
    End Function

    Private Function CollectReachableNpcIds(rootSourceIds As IEnumerable(Of UInteger), dependencyMap As Dictionary(Of UInteger, List(Of TemplateDependencyEdge)), npcById As IReadOnlyDictionary(Of UInteger, NPC_Data)) As HashSet(Of UInteger)
        Dim reachableNpcIds As New HashSet(Of UInteger)()
        Dim visitedSources As New HashSet(Of UInteger)()

        For Each sourceId In rootSourceIds
            CollectReachableNpcIds(sourceId, dependencyMap, npcById, reachableNpcIds, visitedSources)
        Next

        Return reachableNpcIds
    End Function

    Private Sub CollectReachableNpcIds(sourceId As UInteger,
                                       dependencyMap As Dictionary(Of UInteger, List(Of TemplateDependencyEdge)),
                                       npcById As IReadOnlyDictionary(Of UInteger, NPC_Data),
                                       reachableNpcIds As HashSet(Of UInteger),
                                       visitedSources As HashSet(Of UInteger))
        If visitedSources.Contains(sourceId) Then Return
        visitedSources.Add(sourceId)

        If npcById.ContainsKey(sourceId) Then
            reachableNpcIds.Add(sourceId)
        End If

        Dim edges As List(Of TemplateDependencyEdge) = Nothing
        If Not dependencyMap.TryGetValue(sourceId, edges) Then Return

        For Each edge In edges
            reachableNpcIds.Add(edge.DependentNpc.FormID)
            CollectReachableNpcIds(edge.DependentNpc.FormID, dependencyMap, npcById, reachableNpcIds, visitedSources)
        Next
    End Sub

    Private Function GetTemplateDependencies(npc As NPC_Data) As List(Of KeyValuePair(Of UInteger, String))
        Dim dependencies As New List(Of KeyValuePair(Of UInteger, String))
        If npc Is Nothing Then Return dependencies

        For Each boxedCategory In [Enum].GetValues(GetType(NPC_TemplateCategory))
            Dim category = CType(boxedCategory, NPC_TemplateCategory)
            If Not HasTemplateFlag(npc.TemplateFlags, category) Then Continue For

            Dim sourceFormID = ResolveTemplateSourceFormID(npc, category)
            If sourceFormID = 0UI Then Continue For

            dependencies.Add(New KeyValuePair(Of UInteger, String)(sourceFormID, GetTemplateCategoryLabel(category)))
        Next

        Return dependencies
    End Function

    Private Shared Function GetTemplateCategoryLabel(category As NPC_TemplateCategory) As String
        Select Case category
            Case NPC_TemplateCategory.AIData
                Return "AI Data"
            Case NPC_TemplateCategory.AIPackages
                Return "AI Packages"
            Case NPC_TemplateCategory.ModelAnimation
                Return "Model/Animation"
            Case NPC_TemplateCategory.BaseData
                Return "Base Data"
            Case NPC_TemplateCategory.DefaultPackageList
                Return "Default Package List"
            Case Else
                Return category.ToString()
        End Select
    End Function

    Private Function BuildTemplateTreeNode(sourceId As UInteger,
                                           dependencyEdge As TemplateDependencyEdge,
                                           dependencyMap As Dictionary(Of UInteger, List(Of TemplateDependencyEdge)),
                                           npcById As IReadOnlyDictionary(Of UInteger, NPC_Data),
                                           filter As String,
                                           path As HashSet(Of UInteger)) As TreeNode
        Dim node As TreeNode = Nothing
        Dim selfMatches = False

        If npcById.ContainsKey(sourceId) Then
            Dim npc = npcById(sourceId)
            selfMatches = MatchesNpcFilter(npc, dependencyEdge, filter)
            node = New TreeNode(GetNpcNodeDisplayText(npc, dependencyEdge)) With {
                .Name = $"NPC_{npc.FormID:X8}",
                .Tag = npc
            }
        Else
            Dim sourceRec = _pluginManager.GetRecord(sourceId)
            If sourceRec Is Nothing OrElse sourceRec.Header.Signature <> "LVLN" Then Return Nothing

            selfMatches = MatchesRecordFilter(sourceRec, filter)
            node = New TreeNode(GetTemplateSourceDisplayText(sourceRec)) With {
                .Name = $"LVLN_{sourceId:X8}",
                .Tag = sourceRec
            }
        End If

        Dim childNodes As New List(Of TreeNode)()
        If path.Contains(sourceId) Then
            childNodes.Add(New TreeNode("<cycle detected>") With {.Tag = Nothing})
        Else
            path.Add(sourceId)
            Dim edges As List(Of TemplateDependencyEdge) = Nothing
            If dependencyMap.TryGetValue(sourceId, edges) Then
                For Each childEdge In edges
                    Dim childNode = BuildTemplateTreeNode(childEdge.DependentNpc.FormID, childEdge, dependencyMap, npcById, filter, path)
                    If childNode IsNot Nothing Then childNodes.Add(childNode)
                Next
            End If
            path.Remove(sourceId)
        End If

        If filter.Length > 0 AndAlso Not selfMatches AndAlso childNodes.Count = 0 Then
            Return Nothing
        End If

        For Each childNode In childNodes
            node.Nodes.Add(childNode)
        Next

        Return node
    End Function

    Private Shared Function GetNpcNodeDisplayText(npc As NPC_Data, dependencyEdge As TemplateDependencyEdge) As String
        Dim baseText = If(npc Is Nothing, "<unknown NPC>", npc.ToString())
        If dependencyEdge Is Nothing OrElse dependencyEdge.Categories.Count = 0 Then Return baseText
        Return $"{baseText} (template: {String.Join(", ", dependencyEdge.Categories)})"
    End Function

    Private Function IsSupportedTemplateSource(sourceFormID As UInteger, npcById As IReadOnlyDictionary(Of UInteger, NPC_Data)) As Boolean
        If sourceFormID = 0UI Then Return False
        If npcById.ContainsKey(sourceFormID) Then Return True

        Dim sourceRec = _pluginManager.GetRecord(sourceFormID)
        Return sourceRec IsNot Nothing AndAlso sourceRec.Header.Signature = "LVLN"
    End Function

    Private Function MatchesNpcFilter(npc As NPC_Data, dependencyEdge As TemplateDependencyEdge, filter As String) As Boolean
        If String.IsNullOrWhiteSpace(filter) Then Return True
        If npc Is Nothing Then Return False

        Dim comparisons As String() = {
            npc.ToString(),
            npc.EditorID,
            npc.FullName,
            npc.PluginName,
            npc.FormID.ToString("X8"),
            If(dependencyEdge Is Nothing, "", String.Join(" ", dependencyEdge.Categories))
        }

        Return comparisons.Any(Function(value) Not String.IsNullOrEmpty(value) AndAlso value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
    End Function

    Private Shared Function MatchesRecordFilter(rec As PluginRecord, filter As String) As Boolean
        If String.IsNullOrWhiteSpace(filter) Then Return True
        If rec Is Nothing Then Return False

        Dim comparisons As String() = {
            rec.EditorID,
            rec.Header.FormID.ToString("X8"),
            rec.SourcePluginName,
            rec.Header.Signature
        }

        Return comparisons.Any(Function(value) Not String.IsNullOrEmpty(value) AndAlso value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
    End Function

    Private Function GetTemplateSourceSortKey(sourceId As UInteger, npcById As IReadOnlyDictionary(Of UInteger, NPC_Data)) As String
        If npcById.ContainsKey(sourceId) Then Return GetNpcNodeDisplayText(npcById(sourceId), Nothing)

        Dim sourceRec = _pluginManager.GetRecord(sourceId)
        If sourceRec Is Nothing Then Return sourceId.ToString("X8")
        Return GetTemplateSourceDisplayText(sourceRec)
    End Function

    Private Function GetTemplateSourceDisplayText(sourceRec As PluginRecord) As String
        If sourceRec Is Nothing Then Return "<missing template source>"
        If sourceRec.Header.Signature = "LVLN" Then
            Dim label = If(sourceRec.EditorID <> "", sourceRec.EditorID, sourceRec.Header.FormID.ToString("X8"))
            Dim pluginSuffix = If(String.IsNullOrWhiteSpace(sourceRec.SourcePluginName), "", $" [{sourceRec.SourcePluginName}]")
            Return $"LVLN {label}{pluginSuffix}"
        End If

        Return DescribeRecord(sourceRec)
    End Function

    ''' <summary>Current NPC visual state being previewed (without outfit — outfit applied on-demand from combo).</summary>
    Private _currentBaseState As NPCVisualState = Nothing
    ''' <summary>State of the most recently rendered NPC variant. Used by the bone/vertex morph
    ''' checkbox handlers to rebuild a single pipeline stage without re-running the full preview
    ''' resolution.</summary>
    Private _lastRenderedState As NPCVisualState = Nothing
    Private _lastRenderData As PreviewResolutionResult = Nothing
    ''' <summary>Outfit options for current NPC: list of (display name, list of ARMO FormIDs).</summary>
    Private _currentOutfitOptions As New List(Of (Name As String, ArmorFormIDs As List(Of UInteger)))
    Private _suppressOutfitComboEvent As Boolean = False

    Private Sub TreeViewNPCs_DrawNode(sender As Object, e As DrawTreeNodeEventArgs) Handles TreeViewNPCs.DrawNode
        Dim npc = TryCast(e.Node.Tag, NPC_Data)
        Dim lvln = TryCast(e.Node.Tag, LVLN_Data)
        Dim textColor = Color.Black
        If npc IsNot Nothing AndAlso IsTemplateOnly(npc) Then
            textColor = Color.Gray
        ElseIf lvln IsNot Nothing Then
            textColor = Color.DarkBlue
        End If

        ' Selection highlight
        If (e.State And TreeNodeStates.Selected) <> 0 Then
            e.Graphics.FillRectangle(SystemBrushes.Highlight, e.Bounds)
            TextRenderer.DrawText(e.Graphics, e.Node.Text, TreeViewNPCs.Font, e.Bounds, SystemColors.HighlightText, TextFormatFlags.GlyphOverhangPadding)
        Else
            e.Graphics.FillRectangle(SystemBrushes.Window, e.Bounds)
            TextRenderer.DrawText(e.Graphics, e.Node.Text, TreeViewNPCs.Font, e.Bounds, textColor, TextFormatFlags.GlyphOverhangPadding)
        End If
    End Sub

    Private Sub TreeViewNPCs_AfterSelect(sender As Object, e As TreeViewEventArgs) Handles TreeViewNPCs.AfterSelect
        Dim selectedNode = e.Node
        If selectedNode Is Nothing Then
            PopulateRecordDetails(Nothing)
            Return
        End If

        ' Check if the selected node is a LVLN
        Dim lvlnData = TryCast(selectedNode.Tag, LVLN_Data)
        If lvlnData IsNot Nothing Then
            PopulateRecordDetails(Nothing)
            Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
            LoadLVLNOnDemandAsync(lvlnData, requestVersion)
            Return
        End If

        ' Otherwise expect NPC_Data
        Dim npc = TryCast(selectedNode.Tag, NPC_Data)
        If npc Is Nothing Then
            PopulateRecordDetails(Nothing)
            Return
        End If

        PopulateRecordDetails(npc)

        Dim reqVersion = Interlocked.Increment(_previewRequestVersion)
        LoadNPCOnDemandAsync(npc, reqVersion)
    End Sub

    Private Async Sub LoadNPCOnDemandAsync(npc As NPC_Data, requestVersion As Integer)
        Try
            SetStatus($"Loading assets for {npc}...")
            Await EnsureAssetDictionaryAsync()
            If requestVersion <> _previewRequestVersion Then Return

            SetStatus($"Resolving {npc}...")
            Dim baseState As NPCVisualState = Nothing
            Dim outfitOptions As List(Of (Name As String, ArmorFormIDs As List(Of UInteger))) = Nothing
            Await Task.Run(Sub()
                               baseState = ResolveNPCBaseState(npc)
                               outfitOptions = ResolveOutfitOptions(baseState)
                           End Sub)
            If requestVersion <> _previewRequestVersion Then Return

            _currentBaseState = baseState
            _currentOutfitOptions = If(outfitOptions, New List(Of (Name As String, ArmorFormIDs As List(Of UInteger))))

            ' Enable/disable NPC randomization controls based on whether NPC has LVLN in template chain
            Dim hasLeveledTemplates = NpcHasLeveledTemplates(npc)
            If InvokeRequired Then
                Invoke(Sub()
                           ButtonRandomNPC.Enabled = hasLeveledTemplates
                           ComboBoxGender.Enabled = hasLeveledTemplates
                       End Sub)
            Else
                ButtonRandomNPC.Enabled = hasLeveledTemplates
                ComboBoxGender.Enabled = hasLeveledTemplates
            End If

            ' Populate outfit combo
            PopulateOutfitCombo()

            ' Render with first outfit option
            Await RenderCurrentStateAsync(requestVersion)

        Catch ex As Exception
            SetStatus($"Error: {ex.Message}")
        End Try
    End Sub

    ''' <summary>When a LVLN node is selected, pick a random NPC from the list and render it.</summary>
    Private Async Sub LoadLVLNOnDemandAsync(lvlnData As LVLN_Data, requestVersion As Integer)
        Try
            SetStatus($"Picking random NPC from {lvlnData.EditorID}...")
            Await EnsureAssetDictionaryAsync()
            If requestVersion <> _previewRequestVersion Then Return

            ' Pick a random leaf NPC from the LVLN (weighted by Count, recursing into nested LVLNs)
            Dim pickedFormID = PickWeightedRandomFromLVLN(lvlnData.FormID, New HashSet(Of UInteger)())
            If pickedFormID = 0UI Then
                SetStatus($"No NPCs found in {lvlnData.EditorID}")
                Return
            End If

            Dim npc As NPC_Data = Nothing
            _npcByIdCache.TryGetValue(pickedFormID, npc)
            If npc Is Nothing Then
                ' NPC not in cache — parse it on-the-fly
                Dim npcRec = _pluginManager.GetRecord(pickedFormID)
                If npcRec Is Nothing OrElse npcRec.Header.Signature <> "NPC_" Then
                    SetStatus($"Picked FormID {pickedFormID:X8} is not a valid NPC")
                    Return
                End If
                npc = RecordParsers.ParseNPC(npcRec, If(npcRec.SourcePluginName, ""), _pluginManager)
            End If

            NpcPreviewLog.Log($"  [LVLN-SELECT] {lvlnData.EditorID} [{lvlnData.FormID:X8}] ? picked {npc.EditorID} [{npc.FormID:X8}]")
            PopulateRecordDetails(npc)

            SetStatus($"Resolving {npc} (from {lvlnData.EditorID})...")
            Dim baseState As NPCVisualState = Nothing
            Dim outfitOptions As List(Of (Name As String, ArmorFormIDs As List(Of UInteger))) = Nothing
            Await Task.Run(Sub()
                               baseState = ResolveNPCBaseState(npc)
                               outfitOptions = ResolveOutfitOptions(baseState)
                           End Sub)
            If requestVersion <> _previewRequestVersion Then Return

            _currentBaseState = baseState
            _currentOutfitOptions = If(outfitOptions, New List(Of (Name As String, ArmorFormIDs As List(Of UInteger))))

            ' LVLN selections always allow re-randomization
            If InvokeRequired Then
                Invoke(Sub()
                           ButtonRandomNPC.Enabled = True
                           ComboBoxGender.Enabled = True
                       End Sub)
            Else
                ButtonRandomNPC.Enabled = True
                ComboBoxGender.Enabled = True
            End If

            PopulateOutfitCombo()
            Await RenderCurrentStateAsync(requestVersion)

        Catch ex As Exception
            SetStatus($"Error: {ex.Message}")
        End Try
    End Sub

    Private Sub ComboBoxOutfit_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBoxOutfit.SelectedIndexChanged
        If _suppressOutfitComboEvent Then Return
        If _currentBaseState Is Nothing Then Return
        Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
        RenderOnDemandAsync(requestVersion)
    End Sub

    Private Sub ButtonReroll_Click(sender As Object, e As EventArgs) Handles ButtonReroll.Click
        ' Reroll variable outfit pieces only: re-resolve the CURRENT outfit option with fresh random
        If _currentBaseState Is Nothing Then Return
        Dim idx = ComboBoxOutfit.SelectedIndex
        If idx < 0 OrElse idx >= _currentOutfitOptions.Count Then Return

        NpcPreviewLog.Log($"  [REROLL-OUTFIT] idx={idx}")

        ' Re-collect armor pieces for the selected outfit variant with new random picks
        Dim opt = _currentOutfitOptions(idx)
        Dim rerolled = RerollCurrentOutfitOption()
        If rerolled IsNot Nothing Then
            _currentOutfitOptions(idx) = (opt.Name, rerolled)
        End If

        Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
        RenderOnDemandAsync(requestVersion)
    End Sub

    ''' <summary>Re-resolve armor pieces for the currently selected outfit option with fresh random.</summary>
    Private Function RerollCurrentOutfitOption() As List(Of UInteger)
        If _currentBaseState Is Nothing OrElse _currentBaseState.DefaultOutfitFormID = 0UI Then Return Nothing

        Dim outfitRec = _pluginManager.GetRecord(_currentBaseState.DefaultOutfitFormID)
        If outfitRec Is Nothing OrElse outfitRec.Header.Signature <> "OTFT" Then Return Nothing
        Dim otft = RecordParsers.ParseOTFT(outfitRec, _pluginManager)

        Dim idx = If(ComboBoxOutfit.InvokeRequired,
                     CInt(ComboBoxOutfit.Invoke(Function() ComboBoxOutfit.SelectedIndex)),
                     ComboBoxOutfit.SelectedIndex)

        ' Find the LVLI variant entry for this combo index
        Dim fixedPieces As New List(Of UInteger)
        Dim entryIndex = 0

        For Each itemFormID In otft.ItemFormIDs
            Dim itemRec = _pluginManager.GetRecord(itemFormID)
            If itemRec Is Nothing Then Continue For

            Select Case itemRec.Header.Signature
                Case "ARMO"
                    Dim terminalID = ResolveTerminalArmorFormID(itemFormID, New HashSet(Of UInteger)(), New List(Of String))
                    If terminalID <> 0UI Then fixedPieces.Add(terminalID)
                Case "LVLI"
                    Dim lvliRec = _pluginManager.GetRecord(itemFormID)
                    If lvliRec Is Nothing OrElse lvliRec.Header.Signature <> "LVLI" Then Continue For
                    Dim lvli = RecordParsers.ParseLVLI(lvliRec, _pluginManager)

                    For Each entry In lvli.Entries
                        If entry.FormID = 0UI Then Continue For
                        If entryIndex = idx Then
                            Dim result As New List(Of UInteger)(fixedPieces)
                            CollectAllArmorFromEntry(entry.FormID, New HashSet(Of UInteger)(), result)
                            Return result.Distinct().ToList()
                        End If
                        entryIndex += 1
                    Next
            End Select
        Next

        Return Nothing
    End Function

    Private Sub ButtonRandomNPC_Click(sender As Object, e As EventArgs) Handles ButtonRandomNPC.Click
        Dim selectedNode = TreeViewNPCs.SelectedNode
        If selectedNode Is Nothing Then Return

        ' If selected node is a LVLN, re-pick a random NPC from it
        Dim lvlnData = TryCast(selectedNode.Tag, LVLN_Data)
        If lvlnData IsNot Nothing Then
            NpcPreviewLog.Log($"  [REROLL-LVLN] {lvlnData.EditorID} gender={ComboBoxGender.Text}")
            Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
            LoadLVLNOnDemandAsync(lvlnData, requestVersion)
            Return
        End If

        ' Re-resolve the SAME NPC — the LVLN in its template chain will produce
        ' different random picks (different face/gender) each time.
        Dim npc = TryCast(selectedNode.Tag, NPC_Data)
        If npc Is Nothing Then Return

        NpcPreviewLog.Log($"  [REROLL-NPC] {npc.EditorID} gender={ComboBoxGender.Text}")
        Dim requestVersion2 = Interlocked.Increment(_previewRequestVersion)
        LoadNPCOnDemandAsync(npc, requestVersion2)
    End Sub

    Private Async Sub RenderOnDemandAsync(requestVersion As Integer)
        Try
            Await RenderCurrentStateAsync(requestVersion)
        Catch ex As Exception
            SetStatus($"Error: {ex.Message}")
        End Try
    End Sub

    Private Sub PopulateOutfitCombo()
        If InvokeRequired Then
            Invoke(Sub() PopulateOutfitCombo())
            Return
        End If

        _suppressOutfitComboEvent = True
        ComboBoxOutfit.Items.Clear()
        If _currentOutfitOptions.Count = 0 Then
            ComboBoxOutfit.Items.Add("(base body)")
            ComboBoxOutfit.SelectedIndex = 0
        Else
            For Each opt In _currentOutfitOptions
                ComboBoxOutfit.Items.Add(opt.Name)
            Next
            ComboBoxOutfit.SelectedIndex = 0
        End If
        _suppressOutfitComboEvent = False
    End Sub

    Private Function GetSelectedOutfitArmorIDs() As List(Of UInteger)
        Dim idx = If(ComboBoxOutfit.InvokeRequired,
                     CInt(ComboBoxOutfit.Invoke(Function() ComboBoxOutfit.SelectedIndex)),
                     ComboBoxOutfit.SelectedIndex)
        If idx < 0 OrElse idx >= _currentOutfitOptions.Count Then Return New List(Of UInteger)
        Return _currentOutfitOptions(idx).ArmorFormIDs
    End Function

    Private Async Function RenderCurrentStateAsync(requestVersion As Integer) As Task
        If _currentBaseState Is Nothing Then Return

        ' Build final state with selected outfit
        Dim state = CloneVisualState(_currentBaseState)
        state.LoadoutArmorFormIDs.AddRange(GetSelectedOutfitArmorIDs())

        Dim useFaceGen = HasFaceGenAssets(state)

        ' All diagnostic dumps removed — findings saved in memory project_morph_system.md
        ' Next: parse HumanRaceFacialBoneRegions<Gender>.txt JSON and apply FMRS via DeltaTransform.

        ' One-time probe: check whether _faceBones.nif variants exist for vanilla face parts
        Dim _probedFaceBones As Boolean = False
        If Not _probedFaceBones Then
            _probedFaceBones = True
            Dim probePaths = {
                "meshes\actors\character\characterassets\basefemalehead_facebones.nif",
                "meshes\actors\character\characterassets\basemalehead_facebones.nif",
                "meshes\actors\character\characterassets\faceparts\femalemouth_facebones.nif",
                "meshes\actors\character\characterassets\faceparts\femaleeyes_facebones.nif",
                "meshes\actors\character\characterassets\faceparts\femalelashes_facebones.nif",
                "meshes\actors\character\characterassets\faceparts\femaleheadrear_facebones.nif"
            }
            For Each p In probePaths
                Dim exists = FilesDictionary_class.Dictionary.ContainsKey(p)
                NpcPreviewLog.Log($"  [FACEBONES-PROBE] '{p}' exists={exists}")
            Next
        End If

        Dim previewVariant As New PreviewVariantDefinition With {
            .RootNpcFormID = state.FormID,
            .VariantId = 1,
            .DisplayName = $"{DescribeNpc(GetParsedNpc(state.FormID))} | {ComboBoxOutfit.Text}",
            .State = state,
            .UseFaceGen = useFaceGen
        }

        SetStatus($"Rendering {previewVariant.DisplayName}...")
        Dim renderData As PreviewResolutionResult = Nothing
        Await Task.Run(Sub() renderData = ResolvePreviewVariant(previewVariant))
        If requestVersion <> _previewRequestVersion Then Return

        If renderData Is Nothing OrElse renderData.Shapes.Count = 0 Then
            SetStatus($"No meshes found{BuildWarningSuffix(renderData?.Warnings)}")
            Return
        End If

        ' Two independent checkboxes control bone pose (FMRS) and vertex morphs (chargen TRI).
        ' Both are honored during the initial full render; individual toggles after that are
        ' handled by the CheckedChanged handlers below using the granular Intent.MarkDirty flow
        ' (WM pattern from WM_RenderExtensions.vb), NOT a full reload via RenderShapes(request).
        Dim vertexMorphsEnabled = CheckBoxApplyVertexMorphs.Checked
        Dim boneMorphsEnabled = CheckBoxApplyBoneMorphs.Checked
        Dim morphResolver = If(vertexMorphsEnabled, BuildFaceMorphResolver(state, renderData), Nothing)

        ' Build a pose carrying the FMRI/FMRS face bone deltas (each region's bones become
        ' PoseTransformData entries). This pose is applied via AppplyPoseToSkeleton which sets
        ' DeltaTransform on each bone — the same mechanism body poses use. The checkbox toggle
        ' lets the user compare "raw face" (no pose, no morphs) vs "with FMRS applied" live.
        Dim baseSkelResolver = New DefaultSkeletonResolver(renderData.SkeletonKey)
        Dim faceBonePose As Poses_class = Nothing
        If boneMorphsEnabled Then
            faceBonePose = BuildFaceBoneTransforms(state)
        End If
        Dim faceSkelBytes = TryLoadFaceSkeletonBytes(state)
        Dim skelResolver As ISkeletonResolver
        If faceSkelBytes IsNot Nothing Then
            ' Wrap the base resolver so the face skeleton is merged BEFORE the pose is applied.
            skelResolver = New FaceBoneSkeletonResolver(baseSkelResolver, faceSkelBytes)
        Else
            skelResolver = baseSkelResolver
        End If

        Dim request As New RenderRequest With {
            .Shapes = renderData.Shapes,
            .Pose = faceBonePose,
            .SkeletonResolver = skelResolver,
            .MorphResolver = morphResolver,
            .RecalculateNormals = True,
            .ResetCamera = True
        }
        _previewControl.RenderShapes(request)

        ' Cache the resolved state + render data so the morph/pose checkbox handlers can
        ' rebuild the resolver / face bone pose on demand without re-running the full
        ' preview resolution pipeline. See CheckBoxApplyBoneMorphs_CheckedChanged /
        ' CheckBoxApplyVertexMorphs_CheckedChanged below — they follow the WM granular
        ' Intent.MarkDirty(Pose)/MarkDirty(Morphs) pattern, not a full reload.
        _lastRenderedState = state
        _lastRenderData = renderData

        ' After the shapes become RenderableMesh instances, compose the NPC's face tint layers
        ' into an RGBA overlay texture via FBO and assign it to the face mesh's MaterialData.
        ' This is done post-render because MaterialData only exists on a RenderableMesh.
        ApplyFaceTintOverlay(state, renderData)

        SetStatus($"Rendered {previewVariant.DisplayName} ({renderData.Shapes.Count} shapes)")
    End Function

    ''' <summary>Entry point invoked right after RenderShapes. Tries to bake tints immediately;
    ''' if the face diffuse texture isn't in the cache yet (async upload pending), schedules a
    ''' polling timer that retries until the texture appears.</summary>
    Private Sub ApplyFaceTintOverlay(state As NPCVisualState, renderData As PreviewResolutionResult)
        If state Is Nothing Then Return

        ' Cancel any in-flight pending tint for a previous NPC.
        _pendingTintTimer.Stop()
        _pendingTintState = Nothing
        _pendingTintAttempts = 0

        Dim applied = TryApplyFaceTints(state)
        If ENABLE_TWO_STEP_SKIN_TINT Then
            TryApplyFaceSkinSoftLight(state)
            TryApplyBodySkinSoftLight(state)
        End If
        If Not applied Then
            ' Defer: store state, kick the timer.
            _pendingTintState = state
            _pendingTintAttempts = 0
            _pendingTintTimer.Start()
            NpcPreviewLog.Log($"  [FACETINT] face diffuse not ready, deferred (timer started)")
        End If
    End Sub

    Private Sub _pendingTintTimer_Tick(sender As Object, e As EventArgs) Handles _pendingTintTimer.Tick
        If _pendingTintState Is Nothing Then
            _pendingTintTimer.Stop()
            Return
        End If

        _pendingTintAttempts += 1
        If _pendingTintAttempts > PendingTintMaxAttempts Then
            NpcPreviewLog.Log($"  [FACETINT] giving up after {_pendingTintAttempts} attempts (~{_pendingTintAttempts * _pendingTintTimer.Interval}ms)")
            _pendingTintTimer.Stop()
            _pendingTintState = Nothing
            Return
        End If

        Dim model = _previewControl.Model
        If model Is Nothing OrElse Not model.TexturesReady Then Return

        Dim applied = TryApplyFaceTints(_pendingTintState)
        If applied Then
            If ENABLE_TWO_STEP_SKIN_TINT Then
                TryApplyFaceSkinSoftLight(_pendingTintState)
                TryApplyBodySkinSoftLight(_pendingTintState)
            End If
            NpcPreviewLog.Log($"  [FACETINT] applied on attempt #{_pendingTintAttempts}")
            _pendingTintTimer.Stop()
            _pendingTintState = Nothing
            _previewControl.Invalidate()
        End If
    End Sub

    ''' <summary>Build the list of region-mask TXST swaps for an NPC. For each Morph Group
    ''' of the NPC's race, look up whether any preset in that group is currently active in
    ''' the NPC's MorphValues AND the preset has an MPPT TXST. If so, resolve:
    '''   - mask DDS: from the Morph Group's MPPK enum -> TintSlot 0..6 -> TintOption -> TTET[0]
    '''   - swap DDS bytes: from the preset's MPPT TXST.TX00 / TX01 / TX07
    ''' Returns one FaceRegionSwapInput per active preset (typically 0..3 for non-aged NPCs,
    ''' 3 for Murphy who has Arrugado in Forehead/Cheeks/Neck).</summary>
    Private Function BuildFaceRegionSwaps(npcData As NPC_Data, race As RACE_Data, isFemale As Boolean) As List(Of FaceRegionSwapInput)
        Dim swaps As New List(Of FaceRegionSwapInput)
        If npcData Is Nothing OrElse race Is Nothing Then Return swaps
        If npcData.MorphValues Is Nothing OrElse npcData.MorphValues.Count = 0 Then Return swaps

        Dim morphGroups = If(isFemale, race.FemaleMorphGroups, race.MaleMorphGroups)
        If morphGroups Is Nothing OrElse morphGroups.Count = 0 Then Return swaps

        For Each g In morphGroups
            ' Resolve the region mask path via MPPK -> Slot -> TintOption.
            Dim slot As TintSlot
            If Not g.TryGetMaskSlot(slot) Then Continue For
            Dim slotOpts = race.FindTintOptionsBySlot(slot, isFemale)
            If slotOpts.Count = 0 Then Continue For
            ' wbDefinitionsFO4.pas + empirical log: exactly one option per region mask slot.
            Dim maskOpt = slotOpts(0)
            If maskOpt.Textures Is Nothing OrElse maskOpt.Textures.Count = 0 Then Continue For
            Dim maskBytes = LoadTintLayerBytes(maskOpt.Textures(0))
            If maskBytes Is Nothing Then Continue For

            ' For each preset in this group: is the NPC currently selecting it?
            ' Two conditions: the NPC has an MSDV entry whose key == preset.Index, and the
            ' preset has an MPPT TXST to swap to. If MPPT is 0 the preset is morph-only
            ' (vertex deformation) and there's no texture work to do.
            For Each p In g.Presets
                If p.TextureFormID = 0UI Then Continue For
                Dim msdvVal As Single = 0F
                If Not npcData.MorphValues.TryGetValue(p.Index, msdvVal) Then Continue For
                If msdvVal <= 0.001F Then Continue For

                ' Resolve the MPPT FormID to a TXST and grab its TX00/TX01/TX07 bytes.
                Dim txstRec = _pluginManager.GetRecord(p.TextureFormID)
                If txstRec Is Nothing OrElse txstRec.Header.Signature <> "TXST" Then Continue For
                Dim txst = RecordParsers.ParseTXST(txstRec, _pluginManager)
                If txst Is Nothing Then Continue For

                Dim diffBytes = LoadTintLayerBytes(txst.DiffuseTexture)
                Dim normBytes = LoadTintLayerBytes(txst.NormalTexture)
                Dim specBytes = LoadTintLayerBytes(txst.SmoothSpecTexture)

                ' If none of the three swap channels has bytes, the swap is a no-op — skip it.
                If diffBytes Is Nothing AndAlso normBytes Is Nothing AndAlso specBytes Is Nothing Then
                    NpcPreviewLog.Log($"  [REGION-SWAP] '{g.Name}/{p.PresetName}' MPPT={txstRec.EditorID}: no D/N/S bytes, skip")
                    Continue For
                End If

                Dim sw As New FaceRegionSwapInput With {
                    .RegionMaskDdsBytes = maskBytes,
                    .SwapDiffuseDdsBytes = diffBytes,
                    .SwapNormalDdsBytes = normBytes,
                    .SwapSpecularDdsBytes = specBytes,
                    .DebugName = $"{g.Name}/{p.PresetName}"
                }
                swaps.Add(sw)
                Dim chans = If(diffBytes IsNot Nothing, "D", "-") & If(normBytes IsNot Nothing, "+N", "") & If(specBytes IsNot Nothing, "+S", "")
                NpcPreviewLog.Log($"  [REGION-SWAP] ADDED '{g.Name}/{p.PresetName}' slot={CInt(slot)} mask='{maskOpt.Textures(0)}' MPPT={txstRec.EditorID} channels={chans}")
            Next
        Next
        Return swaps
    End Function

    ''' <summary>Build the layer list, find the face mesh diffuse cache entry, run the compositor
    ''' and mutate the cache entry. Returns True if at least one face mesh was successfully tinted,
    ''' False if the texture wasn't ready (caller should defer and retry).</summary>
    Private Function TryApplyFaceTints(state As NPCVisualState) As Boolean
        If state Is Nothing Then Return False

        Dim modelFormID = If(state.ModelSourceFormID <> 0UI, state.ModelSourceFormID, state.FormID)
        Dim npcData = GetParsedNpc(modelFormID)
        If npcData Is Nothing OrElse npcData.FaceTintLayers.Count = 0 Then Return True   ' nothing to do

        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return True
        Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)

        ' Build per-region MPPT TXST swaps from the active Morph Group presets. Empty for NPCs
        ' whose chosen presets are vertex-only (no MPPT) — the typical case for non-aged NPCs.
        Dim regionSwaps = BuildFaceRegionSwaps(npcData, race, state.IsFemale)
        NpcPreviewLog.Log($"  [REGION-SWAP] built {regionSwaps.Count} region swaps for {npcData.EditorID}")

        ' Build full layer list (Palette + TextureSet, all blend ops). The compositor takes the
        ' face diffuse texture as the starting point and ping-pongs each layer onto a copy.
        '
        ' Classification by NPC TETI[0] discriminator (verified empirically):
        '   1 = Palette     ? mask in TTET[0], colour from TEND bytes 1..3, blendOp from TTEC[0]
        '   2 = TextureSet  ? pre-coloured diffuse in TTET[0], no uniform colour, blendOp from TTEB
        Dim layerInputs As New List(Of FaceTintLayerInput)

        ' Diagnostic counters (remove for production along with the rest of NpcPreviewLog usage).
        Dim stat_added_palette As Integer = 0
        Dim stat_added_textureSet As Integer = 0
        Dim stat_added_takesSkinTone As Integer = 0
        Dim stat_skip_skinToneSlot As Integer = 0
        Dim stat_skip_zeroOpacity As Integer = 0
        Dim stat_skip_zeroOpacity_takesSkinTone As Integer = 0
        Dim stat_skip_missingOption As Integer = 0
        Dim stat_skip_missingMask As Integer = 0
        Dim stat_skip_unknownDiscriminator As Integer = 0
        Dim stat_byFlags_added As New Dictionary(Of UShort, Integer)
        Dim stat_byFlags_skipped As New Dictionary(Of UShort, Integer)

        NpcPreviewLog.Log($"  [FACETINT] processing {npcData.FaceTintLayers.Count} tint layers for {npcData.EditorID}")
        NpcPreviewLog.Log($"  [FACETINT] === Pipeline coverage ===")
        NpcPreviewLog.Log($"    TTEF bits: 0x0001=OnOffOnly, 0x0002=ChargenDetail, 0x0004=TakesSkinTone (metadata only, not used by render branch)")
        NpcPreviewLog.Log($"    opacity = tl.Value/100 <= 0.001 -> SKIPPED entirely")
        NpcPreviewLog.Log($"    Slot 12 SkinTone -> SKIPPED from compositor; applied by render shader's tintColor uniform (albedo *= tintColor) on face AND body meshes")
        NpcPreviewLog.Log($"    Discr=1 Palette (non-SkinTone) -> D: srcColor=uColor from TEND RGB, coverage=layerSample.r*uOpacity*TTEC.Alpha, blendOp from TTEC")
        NpcPreviewLog.Log($"    Discr=2 TextureSet             -> D: srcColor=layerSample.rgb, coverage=layerSample.a*uOpacity, blendOp from TTEB")
        NpcPreviewLog.Log($"    Discr=2 TextureSet on N/S      -> hard-replace gated by TTET[0].alpha*uOpacity (absolute full-face detail)")
        NpcPreviewLog.Log($"    NifSkope reference (sk_msn.frag): overlay(albedo, tintMask) then *= tintColor -- we approximate overlay via alpha-over in the existing preview shader and keep SkinTint uniform active.")
        NpcPreviewLog.Log($"  [FACETINT] === Layers ===")

        For Each tl In npcData.FaceTintLayers
            Dim opt = race.FindTintOption(tl.Index, state.IsFemale)

            ' [tl raw] — log EVERY NPC tint layer before any filter so we can audit drops.
            ' This is the source-of-truth dump for "are all facetints applied?" — every layer
            ' that the parser produced must show up here, then ADDED or one of the skip lines.
            Dim rawOptName = If(opt IsNot Nothing, opt.Name, "<no-option>")
            Dim rawOptSlot = If(opt IsNot Nothing, opt.Slot.ToString(), "?")
            Dim rawOptFlagsU = If(opt IsNot Nothing, opt.Flags, CUShort(0))
            Dim rawOptFlagsHex = If(opt IsNot Nothing, $"0x{opt.Flags:X4}", "?")
            Dim rawOptFlagsName = If(opt IsNot Nothing, FormatTintFlagsName(opt.Flags), "?")
            Dim ttetSlots As String
            If opt IsNot Nothing AndAlso opt.Textures IsNot Nothing Then
                Dim s0 = If(opt.Textures.Count >= 1 AndAlso Not String.IsNullOrEmpty(opt.Textures(0)), "Y", "-")
                Dim s1 = If(opt.Textures.Count >= 2 AndAlso Not String.IsNullOrEmpty(opt.Textures(1)), "Y", "-")
                Dim s2 = If(opt.Textures.Count >= 3 AndAlso Not String.IsNullOrEmpty(opt.Textures(2)), "Y", "-")
                ttetSlots = $"[D={s0} N={s1} S={s2}]"
            Else
                ttetSlots = "[?]"
            End If
            NpcPreviewLog.Log($"    [tl raw] idx={tl.Index} discr={tl.Discriminator} value={tl.Value} slot={rawOptSlot} flags={rawOptFlagsHex}({rawOptFlagsName}) ttet={ttetSlots} name='{rawOptName}'")

            If opt Is Nothing OrElse opt.Textures Is Nothing OrElse opt.Textures.Count = 0 Then
                NpcPreviewLog.Log($"      -> SKIP option/textures missing")
                stat_skip_missingOption += 1
                If Not stat_byFlags_skipped.ContainsKey(rawOptFlagsU) Then stat_byFlags_skipped(rawOptFlagsU) = 0
                stat_byFlags_skipped(rawOptFlagsU) += 1
                Continue For
            End If

            Dim takesSkinTone As Boolean = (opt.Flags And &H4US) <> 0US

            ' Slot 12 SkinTone handling depends on ENABLE_TWO_STEP_SKIN_TINT.
            ' Legacy mode (False): skipped here -- the render shader's `albedo *= tintColor`
            '   uniform handles skin tone on both face and body, with face using slot 12 RGB
            '   and body using QNAM. No compositor pass touches slot 12.
            ' Two-step mode (True): slot 12 enters the compositor as a normal Palette layer.
            '   ResolvePaletteLayerEffective resolves its colour and blendOp from TTEC; in
            '   HumanRace the authored blendOp for the SkinTone palette is SoftLight, so the
            '   compositor produces softlight(base, slot12) which the render shader then
            '   multiplies by slot12 again. The body path applies an analogous SoftLight with
            '   QNAM in TryApplyBodySkinSoftLight so the two meshes stay symmetric.
            If opt.Slot = CUShort(TintSlot.SkinTone) AndAlso Not ENABLE_TWO_STEP_SKIN_TINT Then
                stat_skip_skinToneSlot += 1
                NpcPreviewLog.Log($"      -> SKIP SkinTone slot (legacy mode: render shader handles it via tintColor uniform on both face and body) value={tl.Value}")
                If Not stat_byFlags_skipped.ContainsKey(rawOptFlagsU) Then stat_byFlags_skipped(rawOptFlagsU) = 0
                stat_byFlags_skipped(rawOptFlagsU) += 1
                Continue For
            End If

            Dim opacity As Single = CSng(tl.Value) / 100.0F
            If opacity <= 0.001F Then
                stat_skip_zeroOpacity += 1
                If takesSkinTone Then stat_skip_zeroOpacity_takesSkinTone += 1
                ' WARNING: if takesSkinTone, this gate also kills N/S. Pending review:
                ' skin-tone N/S relief is baked, may need to bypass this gate.
                Dim warn = If(takesSkinTone, " <<< takesSkinTone -- N/S also lost here", "")
                NpcPreviewLog.Log($"      -> SKIP value=0/low (opacity={opacity:F3}){warn}")
                If Not stat_byFlags_skipped.ContainsKey(rawOptFlagsU) Then stat_byFlags_skipped(rawOptFlagsU) = 0
                stat_byFlags_skipped(rawOptFlagsU) += 1
                Continue For
            End If

            ' Resolve TTET[0] (mask / diffuse). Always required.
            Dim diffuseBytes = LoadTintLayerBytes(opt.Textures(0))
            If diffuseBytes Is Nothing Then
                NpcPreviewLog.Log($"      -> SKIP TTET[0] not found: '{opt.Textures(0)}'")
                stat_skip_missingMask += 1
                If Not stat_byFlags_skipped.ContainsKey(rawOptFlagsU) Then stat_byFlags_skipped(rawOptFlagsU) = 0
                stat_byFlags_skipped(rawOptFlagsU) += 1
                Continue For
            End If

            ' For TextureSet entries, also try TTET[1] (normal) and TTET[2] (specular).
            ' These are optional — many entries have empty strings, in which case the layer
            ' contributes only to the diffuse channel.
            Dim normalBytes As Byte() = Nothing
            Dim specularBytes As Byte() = Nothing
            If tl.Discriminator = 2 Then
                If opt.Textures.Count >= 2 Then normalBytes = LoadTintLayerBytes(opt.Textures(1))
                If opt.Textures.Count >= 3 Then specularBytes = LoadTintLayerBytes(opt.Textures(2))
            End If

            ' TTEF flags (verified from wbDefinitionsFO4.pas:3496):
            '   0x0001 = On/Off only
            '   0x0002 = Chargen Detail
            '   0x0004 = Takes Skin Tone — scar/wrinkle detail layers that carry their own
            '                              pre-baked normal + specular. The compositor now
            '                              applies these via the mask-gated hard replace branch
            '                              (Scar_d.alpha as spatial mask on Scar_n / Scar_s).
            Dim layerInput As New FaceTintLayerInput With {
                .LayerDdsBytes = diffuseBytes,
                .NormalDdsBytes = normalBytes,
                .SpecularDdsBytes = specularBytes,
                .Opacity = opacity,
                .TakesSkinTone = takesSkinTone,
                .DebugName = opt.Name
            }

            If tl.Discriminator = 1 Then
                ' Palette: greyscale mask in .r. The effective colour, blendOp and per-preset
                ' alpha multiplier are resolved from the NPC's TEND TemplateColorIndex indexed
                ' positionally into the RACE Option's TTEC TemplateColors array (CLFM lookup
                ' + authored BlendOperation + authored Alpha). When TemplateColorIndex = -1,
                ' falls back to the direct TEND RGB and alpha = 1.0.
                layerInput.Kind = FaceTintLayerKind.PaletteMask
                Dim resolved = ResolvePaletteLayerEffective(tl, opt)
                layerInput.R = resolved.Color.R
                layerInput.G = resolved.Color.G
                layerInput.B = resolved.Color.B
                layerInput.BlendOp = CInt(resolved.BlendOp)
                ' Per-preset alpha multiplies the slider value. Slider at 100% with alpha=0.5
                ' ends up as 0.5 effective opacity so the author can keep the user-facing slider
                ' range full while still authoring a softer preset intensity.
                Dim sliderOpacity As Single = opacity   ' already tl.Value / 100
                Dim effectiveOpacity As Single = sliderOpacity * resolved.Alpha
                layerInput.Opacity = Math.Max(0.0F, Math.Min(1.0F, effectiveOpacity))
                opacity = layerInput.Opacity   ' keep the downstream ADDED log line consistent
                NpcPreviewLog.Log($"      -> Palette resolve: TemplateColorIndex={tl.TemplateColorIndex} tendRGB=({tl.Color.R},{tl.Color.G},{tl.Color.B}) -> effectiveRGB=({resolved.Color.R},{resolved.Color.G},{resolved.Color.B}) blendOp={resolved.BlendOp}({BlendOpName(resolved.BlendOp)}) slider={sliderOpacity:F2} * tcAlpha={resolved.Alpha:F2} = effectiveOpacity={effectiveOpacity:F2}")
            ElseIf tl.Discriminator = 2 Then
                ' TextureSet: pre-coloured RGBA. Blend op from TTEB raw bytes (parser reads as U32).
                layerInput.Kind = FaceTintLayerKind.TextureSetDiffuse
                layerInput.BlendOp = CInt(opt.BlendOperation)
            Else
                NpcPreviewLog.Log($"      -> SKIP unknown discriminator={tl.Discriminator}")
                stat_skip_unknownDiscriminator += 1
                If Not stat_byFlags_skipped.ContainsKey(rawOptFlagsU) Then stat_byFlags_skipped(rawOptFlagsU) = 0
                stat_byFlags_skipped(rawOptFlagsU) += 1
                Continue For
            End If

            Dim slotName = TintSlotName(opt.Slot)
            Dim opName = BlendOpName(CUInt(layerInput.BlendOp))
            Dim chans = "D"
            If normalBytes IsNot Nothing Then chans &= "+N"
            If specularBytes IsNot Nothing Then chans &= "+S"
            ' Note: DDS format per channel is reported by the compositor in the DRAWN logger line.
            NpcPreviewLog.Log($"      -> ADDED slot={opt.Slot}({slotName}) kind={layerInput.Kind} blendOp={layerInput.BlendOp}({opName}) value={tl.Value} opacity={opacity:F2} flags={rawOptFlagsHex}({rawOptFlagsName}) takesSkinTone={takesSkinTone} channels={chans}")
            layerInputs.Add(layerInput)

            ' Stats tracking.
            If layerInput.Kind = FaceTintLayerKind.PaletteMask Then
                stat_added_palette += 1
            Else
                stat_added_textureSet += 1
            End If
            If takesSkinTone Then stat_added_takesSkinTone += 1
            If Not stat_byFlags_added.ContainsKey(rawOptFlagsU) Then stat_byFlags_added(rawOptFlagsU) = 0
            stat_byFlags_added(rawOptFlagsU) += 1
        Next

        ' === Summary ===
        NpcPreviewLog.Log($"  [FACETINT] === Summary for {npcData.EditorID} ===")
        NpcPreviewLog.Log($"    Total NPC layers: {npcData.FaceTintLayers.Count}")
        NpcPreviewLog.Log($"    ADDED: {layerInputs.Count} ({stat_added_palette} Palette + {stat_added_textureSet} TextureSet, {stat_added_takesSkinTone} takesSkinTone)")
        NpcPreviewLog.Log($"    SKIPPED: skinToneSlot={stat_skip_skinToneSlot} zeroOpacity={stat_skip_zeroOpacity} (of which takesSkinTone={stat_skip_zeroOpacity_takesSkinTone}) missingOption={stat_skip_missingOption} missingMask={stat_skip_missingMask} unknownDiscr={stat_skip_unknownDiscriminator}")
        Dim allFlagKeys As New SortedSet(Of UShort)
        For Each k In stat_byFlags_added.Keys : allFlagKeys.Add(k) : Next
        For Each k In stat_byFlags_skipped.Keys : allFlagKeys.Add(k) : Next
        For Each fk In allFlagKeys
            Dim a As Integer = 0 : stat_byFlags_added.TryGetValue(fk, a)
            Dim s As Integer = 0 : stat_byFlags_skipped.TryGetValue(fk, s)
            NpcPreviewLog.Log($"    flags 0x{fk:X4} ({FormatTintFlagsName(fk)}): ADDED={a} SKIPPED={s}")
        Next

        If layerInputs.Count = 0 Then
            NpcPreviewLog.Log($"  [FACETINT] no valid layers for {npcData.EditorID}")
            Return True   ' no work to do; don't keep retrying
        End If

        ' Find the face mesh in the model, get its diffuse texture cache entry, and call the
        ' compositor on a copy. Then mutate the cache entry's GL Texture_ID so the existing
        ' render path picks up the modified diffuse without any library changes.
        Dim model = _previewControl.Model
        If model Is Nothing OrElse model.meshes Is Nothing Then
            Return True   ' no model — nothing we can do, don't retry forever
        End If

        Dim composedAny As Boolean = False
        Dim faceMeshFoundButTextureNotReady As Boolean = False
        Dim seenFaceMeshes As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each mesh In model.meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Material Is Nothing Then Continue For
            Dim shape = mesh.MeshData.Shape
            If shape Is Nothing Then Continue For

            Dim materialBase = mesh.MeshData.Material.MaterialBase
            If materialBase Is Nothing Then Continue For

            ' The actual face shape uses the FaceTint shader (BSLightingShaderType). Other "head"
            ' shapes (BaseFemaleHeadRear with body texture, mouth, lashes, eyes) use SkinTint or
            ' EnvMap. Filtering by shader type avoids touching the headrear / mouth diffuses.
            If materialBase.NifShaderType <> NiflySharp.Enums.BSLightingShaderType.FaceTint Then Continue For

            Dim diffusePath = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.Diffuse_or_Base_Texture)
            If String.IsNullOrEmpty(diffusePath) Then Continue For
            If seenFaceMeshes.Contains(diffusePath) Then Continue For
            seenFaceMeshes.Add(diffusePath)

            ' Diffuse must be ready before we attempt anything — it's the channel every layer
            ' contributes to and it's the one whose dimensions drive the FBO size. If diffuse
            ' isn't loaded, signal "retry later".
            Dim diffuseEntry As PreviewModel.Texture_Loaded_Class = Nothing
            If Not model.Textures_Dictionary.TryGetValue(diffusePath, diffuseEntry) _
               OrElse diffuseEntry Is Nothing OrElse Not diffuseEntry.Loaded OrElse diffuseEntry.Texture_ID = 0 Then
                NpcPreviewLog.Log($"  [FACETINT] face diffuse '{diffusePath}' not in cache yet")
                faceMeshFoundButTextureNotReady = True
                Continue For
            End If

            Dim w = diffuseEntry.Size.Width
            Dim h = diffuseEntry.Size.Height
            If w <= 0 OrElse h <= 0 Then
                NpcPreviewLog.Log($"  [FACETINT] face diffuse '{diffusePath}' has invalid size {w}x{h}, skip")
                Continue For
            End If

            ' Compose all three channels. Diffuse is always attempted; normal and specular only
            ' run if their respective face textures exist in the cache (they may be missing for
            ' some materials — that's OK, just skip those channels).
            Dim normalPath = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.NormalTexture)
            Dim specPath = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.SmoothSpecTexture)

            ' --- Region-swap pre-pass ---
            ' Apply Morph Group MPPT TXST swaps (e.g. Murphy's "Arrugado" presets) onto the
            ' base D/N/S textures BEFORE running the tint compositor. This swaps the per-region
            ' face textures (e.g. SkinHeadFemaleOld -> OldHumanFemaleHead_d/n/s.dds) inside the
            ' MPPK region mask so the tint layers below blend on top of the wrinkled base.
            ' The pre-pass is a no-op for NPCs whose presets don't have MPPT (all young NPCs).
            If regionSwaps.Count > 0 Then
                ApplyRegionSwapChannelOnto(model, diffusePath, diffuseEntry, w, h, regionSwaps, FaceTintChannel.Diffuse)
                ApplyRegionSwapChannelOnto(model, normalPath, Nothing, w, h, regionSwaps, FaceTintChannel.Normal)
                ApplyRegionSwapChannelOnto(model, specPath, Nothing, w, h, regionSwaps, FaceTintChannel.Specular)

                ' Re-fetch diffuseEntry — ApplyRegionSwapChannelOnto may have replaced its
                ' Texture_ID in place. The compositor below reads from this same entry.
                model.Textures_Dictionary.TryGetValue(diffusePath, diffuseEntry)
            End If

            ComposeChannelOnto(model, diffusePath, diffuseEntry, w, h, layerInputs, FaceTintChannel.Diffuse, composedAny)
            ComposeChannelOnto(model, normalPath, Nothing, w, h, layerInputs, FaceTintChannel.Normal, composedAny)
            ComposeChannelOnto(model, specPath, Nothing, w, h, layerInputs, FaceTintChannel.Specular, composedAny)

            ' materialBase.SkinTint stays ENABLED. The render shader's `albedo *= tintColor`
            ' uniform handles slot 12 SkinTone uniformly on both face and body meshes, using
            ' the same resolved colour. Skin tone is not a FaceTint layer in our pipeline.
        Next

        ' If we found a face mesh but its texture wasn't ready, signal "retry later".
        ' If we composed at least one, success. Otherwise nothing matched — give up (no retry).
        If composedAny Then Return True
        If faceMeshFoundButTextureNotReady Then Return False
        NpcPreviewLog.Log($"  [FACETINT] no face mesh (NifShaderType=FaceTint) found in model")
        Return True
    End Function

    ''' <summary>Returns True iff the NPC has a face tint layer in slot 12 (SkinTone) with
    ''' non-trivial slider value (>0.001). Used to decide whether the face compositor will
    ''' apply SoftLight via the slot-12 Palette path on its own, or whether we need to run
    ''' the standalone face SoftLight fallback against QNAM.</summary>
    Private Function NpcHasSkinToneLayer(npcData As NPC_Data, race As RACE_Data, isFemale As Boolean) As Boolean
        If npcData Is Nothing OrElse race Is Nothing Then Return False
        If npcData.FaceTintLayers Is Nothing OrElse npcData.FaceTintLayers.Count = 0 Then Return False
        For Each tl In npcData.FaceTintLayers
            Dim opt = race.FindTintOption(tl.Index, isFemale)
            If opt Is Nothing Then Continue For
            If opt.Slot <> CUShort(TintSlot.SkinTone) Then Continue For
            If tl.Value <= 0 Then Continue For
            Return True
        Next
        Return False
    End Function

    ''' <summary>Two-step skin tint — face FALLBACK side. Some NPCs (e.g. MayorMcDonough) do
    ''' NOT declare a face tint layer in slot 12 SkinTone. For them the face compositor never
    ''' runs the slot-12 Palette path, so the face misses the SoftLight that the body gets
    ''' from TryApplyBodySkinSoftLight against QNAM. The result: face = base * QNAM but body
    ''' = softlight(base, qnam) * qnam — visibly mismatched (the alcalde shows this).
    '''
    ''' This function applies the same one-shot SoftLight pre-pass to the face mesh diffuse
    ''' (using QNAM as the colour, same as body) so both meshes end up doing
    '''   final = softlight(base, qnam) * qnam
    ''' even when slot 12 is absent. Symmetric with the body path.
    '''
    ''' MUST be called AFTER TryApplyFaceTints so the SoftLight is the LAST thing baked into
    ''' the face diffuse (matches the order in which the body pre-pass runs against the body
    ''' mesh: a single uniform blend on top of whatever is already there). Skipped silently
    ''' when the NPC already has a slot 12 layer, when QNAM is absent, or when the face
    ''' diffuse isn't in cache yet.</summary>
    Private Sub TryApplyFaceSkinSoftLight(state As NPCVisualState)
        If state Is Nothing Then Return
        If Not state.HasTextureLighting Then
            NpcPreviewLog.Log($"  [FACESKIN] no QNAM (HasTextureLighting=False), skip")
            Return
        End If

        Dim modelFormID = If(state.ModelSourceFormID <> 0UI, state.ModelSourceFormID, state.FormID)
        Dim npcData = GetParsedNpc(modelFormID)
        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        Dim race As RACE_Data = Nothing
        If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
            race = RecordParsers.ParseRACE(raceRec, _pluginManager)
        End If

        ' If the NPC has a slot 12 layer, the face compositor already applied SoftLight via
        ' the Palette path (when ENABLE_TWO_STEP_SKIN_TINT is True). Don't double-apply.
        If NpcHasSkinToneLayer(npcData, race, state.IsFemale) Then
            NpcPreviewLog.Log($"  [FACESKIN] NPC has slot 12 layer, compositor handled it; skip face fallback")
            Return
        End If

        Dim model = _previewControl.Model
        If model Is Nothing OrElse model.meshes Is Nothing Then Return

        Dim qnam = state.TextureLightingColor
        Dim qR As Single = CSng(qnam.R) / 255.0F
        Dim qG As Single = CSng(qnam.G) / 255.0F
        Dim qB As Single = CSng(qnam.B) / 255.0F

        Const SoftLightOp As Integer = 3

        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim affected As Integer = 0
        For Each mesh In model.meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Material Is Nothing Then Continue For
            Dim materialBase = mesh.MeshData.Material.MaterialBase
            If materialBase Is Nothing Then Continue For

            ' Only the face mesh — the body mesh is handled by TryApplyBodySkinSoftLight.
            If materialBase.NifShaderType <> NiflySharp.Enums.BSLightingShaderType.FaceTint Then Continue For

            Dim diffusePath = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.Diffuse_or_Base_Texture)
            If String.IsNullOrEmpty(diffusePath) Then Continue For
            If seen.Contains(diffusePath) Then Continue For
            seen.Add(diffusePath)

            Dim entry As PreviewModel.Texture_Loaded_Class = Nothing
            If Not model.Textures_Dictionary.TryGetValue(diffusePath, entry) _
               OrElse entry Is Nothing OrElse Not entry.Loaded OrElse entry.Texture_ID = 0 Then
                NpcPreviewLog.Log($"  [FACESKIN] '{diffusePath}' not in cache, skip")
                Continue For
            End If

            Dim w = entry.Size.Width, h = entry.Size.Height
            If w <= 0 OrElse h <= 0 Then
                NpcPreviewLog.Log($"  [FACESKIN] '{diffusePath}' invalid size {w}x{h}, skip")
                Continue For
            End If

            NpcPreviewLog.Log($"  [FACESKIN] applying softlight(QNAM) onto '{diffusePath}' ({w}x{h}), originalTexID={entry.Texture_ID}, qnam=({qnam.R},{qnam.G},{qnam.B})")
            Dim faceLogger As Action(Of String) = Sub(msg) NpcPreviewLog.Log($"  [FACESKIN]{msg}")
            Dim newTexId = FaceTintCompositor.ApplyUniformBlendOntoFaceTexture(
                entry.Texture_ID, w, h, qR, qG, qB, SoftLightOp, logger:=faceLogger)
            If newTexId = 0 OrElse newTexId = entry.Texture_ID Then
                NpcPreviewLog.Log($"  [FACESKIN] returned 0 / no-op")
                Continue For
            End If

            Dim oldId = entry.Texture_ID
            entry.Texture_ID = newTexId
            Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(oldId) : Catch : End Try
            affected += 1
            NpcPreviewLog.Log($"  [FACESKIN] replaced cache entry: oldTexID={oldId} -> newTexID={newTexId}")
        Next

        NpcPreviewLog.Log($"  [FACESKIN] done — {affected} face diffuse(s) updated")
    End Sub

    ''' <summary>Two-step skin tint experiment — body side. Applies a one-shot SoftLight pass
    ''' against the NPC's QNAM TextureLightingColor onto every body diffuse texture (any mesh
    ''' with material.SkinTint = True that isn't the face mesh). The render shader still
    ''' multiplies by material.SkinTintColor afterwards, so the body ends up doing
    '''   final = softlight(base, qnam) * qnam
    ''' which is symmetric with the face's
    '''   final = softlight(base, slot12) * slot12
    ''' (when slot 12 is composited as a Palette layer with SoftLight blend op and the render
    ''' tint stays enabled). NO material modification — material.SkinTint and SkinTintColor
    ''' stay exactly as set by the existing override pipeline.
    '''
    ''' Skipped silently when ENABLE_TWO_STEP_SKIN_TINT is False, when state has no QNAM,
    ''' when the model has no body meshes, or when their diffuse textures aren't in cache yet
    ''' (no retry mechanism — body diffuse usually loads with the rest of the body pass).</summary>
    Private Sub TryApplyBodySkinSoftLight(state As NPCVisualState)
        If state Is Nothing Then Return
        If Not state.HasTextureLighting Then
            NpcPreviewLog.Log($"  [BODYSKIN] no QNAM (HasTextureLighting=False), skip")
            Return
        End If

        Dim model = _previewControl.Model
        If model Is Nothing OrElse model.meshes Is Nothing Then Return

        Dim qnam = state.TextureLightingColor
        Dim qR As Single = CSng(qnam.R) / 255.0F
        Dim qG As Single = CSng(qnam.G) / 255.0F
        Dim qB As Single = CSng(qnam.B) / 255.0F

        ' BlendOp matches the face slot 12 path: SoftLight (Photoshop / W3C SVG) so the
        ' two meshes use the same compositing operation against the same colour.
        Const SoftLightOp As Integer = 3

        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim affected As Integer = 0
        For Each mesh In model.meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Material Is Nothing Then Continue For
            Dim materialBase = mesh.MeshData.Material.MaterialBase
            If materialBase Is Nothing Then Continue For

            ' Body = SkinTint material that is NOT the face. Face has its own slot-12 path
            ' running through the FaceTint compositor; touching it again here would double
            ' the SoftLight on the diffuse.
            If Not materialBase.SkinTint Then Continue For
            If materialBase.NifShaderType = NiflySharp.Enums.BSLightingShaderType.FaceTint Then Continue For

            Dim diffusePath = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.Diffuse_or_Base_Texture)
            If String.IsNullOrEmpty(diffusePath) Then Continue For
            If seen.Contains(diffusePath) Then Continue For
            seen.Add(diffusePath)

            Dim entry As PreviewModel.Texture_Loaded_Class = Nothing
            If Not model.Textures_Dictionary.TryGetValue(diffusePath, entry) _
               OrElse entry Is Nothing OrElse Not entry.Loaded OrElse entry.Texture_ID = 0 Then
                NpcPreviewLog.Log($"  [BODYSKIN] '{diffusePath}' not in cache, skip")
                Continue For
            End If

            Dim w = entry.Size.Width, h = entry.Size.Height
            If w <= 0 OrElse h <= 0 Then
                NpcPreviewLog.Log($"  [BODYSKIN] '{diffusePath}' invalid size {w}x{h}, skip")
                Continue For
            End If

            NpcPreviewLog.Log($"  [BODYSKIN] applying softlight(QNAM) onto '{diffusePath}' ({w}x{h}), originalTexID={entry.Texture_ID}, qnam=({qnam.R},{qnam.G},{qnam.B})")
            Dim bodyLogger As Action(Of String) = Sub(msg) NpcPreviewLog.Log($"  [BODYSKIN]{msg}")
            Dim newTexId = FaceTintCompositor.ApplyUniformBlendOntoFaceTexture(
                entry.Texture_ID, w, h, qR, qG, qB, SoftLightOp, logger:=bodyLogger)
            If newTexId = 0 OrElse newTexId = entry.Texture_ID Then
                NpcPreviewLog.Log($"  [BODYSKIN] returned 0 / no-op")
                Continue For
            End If

            Dim oldId = entry.Texture_ID
            entry.Texture_ID = newTexId
            Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(oldId) : Catch : End Try
            affected += 1
            NpcPreviewLog.Log($"  [BODYSKIN] replaced cache entry: oldTexID={oldId} -> newTexID={newTexId}")
        Next

        NpcPreviewLog.Log($"  [BODYSKIN] done — {affected} body diffuse(s) updated")
    End Sub

    ''' <summary>Resolve a Palette face tint layer's effective colour, blend operation, and
    ''' per-preset alpha multiplier from the RACE TintOption and the NPC's TEND data.
    '''
    ''' Evidence-based rule (verified against Roxy's TEND where RGB=(140,147,157) but
    ''' TemplateColorIndex=0 points to CLFM "HumanSkinBase01 Pálido" which is near-white —
    ''' if the engine followed the CLFM, Roxy would render pale; she doesn't, so the engine
    ''' uses the TEND RGB at runtime and the CLFM is only a chargen UI aid for the preset
    ''' buttons):
    '''
    '''   - Colour  : ALWAYS tl.Color (direct RGB from TEND bytes 1..3). The TEND RGB is the
    '''               authoritative runtime value; the author (or chargen save) computed the
    '''               final colour, possibly derived from a CLFM preset, and cached it here.
    '''               The TemplateColorIndex is UI metadata identifying which preset button
    '''               was highlighted — NOT a runtime colour selector.
    '''
    '''   - BlendOp : TTEC[TemplateColorIndex].BlendOperation when the index is a valid
    '''               positional index into the TemplateColors array. Fallback to TTEC[0]'s
    '''               BlendOperation, or Default (0) if the Option has no TemplateColors.
    '''
    '''   - Alpha   : TTEC[TemplateColorIndex].Alpha as a per-preset intensity multiplier
    '''               applied on top of the slider value (tl.Value/100). Same fallback chain
    '''               as BlendOp. When the TTEC entry has Alpha=0, the layer effectively
    '''               switches off regardless of slider value — that's how Bethesda marks
    '''               "this preset is an empty placeholder".</summary>
    Private Function ResolvePaletteLayerEffective(tl As NPC_FaceTintLayerData, opt As RACE_TintTemplateOption) As (Color As Color, BlendOp As UInteger, Alpha As Single)
        Dim resolvedColor As Color = tl.Color
        Dim resolvedBlendOp As UInteger = 0UI
        Dim resolvedAlpha As Single = 1.0F

        If opt IsNot Nothing AndAlso opt.TemplateColors IsNot Nothing AndAlso opt.TemplateColors.Count > 0 Then
            If tl.TemplateColorIndex >= 0 AndAlso tl.TemplateColorIndex < opt.TemplateColors.Count Then
                Dim tc = opt.TemplateColors(tl.TemplateColorIndex)
                resolvedBlendOp = tc.BlendOperation
                resolvedAlpha = tc.Alpha
            Else
                ' TemplateColorIndex = -1 (no preset selected) or out-of-range: fall back to
                ' the first palette entry's blendOp as an authored default; alpha stays 1.0.
                resolvedBlendOp = opt.TemplateColors(0).BlendOperation
                resolvedAlpha = 1.0F
            End If
        End If

        Return (resolvedColor, resolvedBlendOp, resolvedAlpha)
    End Function

    ''' <summary>Run the compositor for one channel (Diffuse / Normal / Specular) onto the
    ''' face mesh's texture for that channel, then mutate the model's Textures_Dictionary entry
    ''' so the existing render path picks up the modified texture.</summary>
    Private Sub ComposeChannelOnto(model As PreviewModel,
                                   texPath As String,
                                   knownEntry As PreviewModel.Texture_Loaded_Class,
                                   width As Integer, height As Integer,
                                   layers As IList(Of FaceTintLayerInput),
                                   channel As FaceTintChannel,
                                   ByRef composedAny As Boolean)
        If model Is Nothing OrElse String.IsNullOrEmpty(texPath) Then Return

        Dim entry = knownEntry
        If entry Is Nothing Then
            If Not model.Textures_Dictionary.TryGetValue(texPath, entry) Then
                NpcPreviewLog.Log($"  [FACETINT/{channel}] '{texPath}' not in cache, skip")
                Return
            End If
        End If
        If entry Is Nothing OrElse Not entry.Loaded OrElse entry.Texture_ID = 0 Then
            NpcPreviewLog.Log($"  [FACETINT/{channel}] '{texPath}' not loaded, skip")
            Return
        End If

        ' Quick pre-check: do any layers actually contribute to this channel? If none, skip
        ' the GL work entirely. (Avoids re-uploading the diffuse / normal / spec for nothing.)
        Dim hasContribution As Boolean = False
        For Each layer In layers
            If layer Is Nothing Then Continue For
            Dim b = layer.GetChannelBytes(channel)
            If b IsNot Nothing AndAlso b.Length > 0 Then
                hasContribution = True
                Exit For
            End If
        Next
        If Not hasContribution Then
            NpcPreviewLog.Log($"  [FACETINT/{channel}] no layer contributes, skip")
            Return
        End If

        NpcPreviewLog.Log($"  [FACETINT/{channel}] composing onto '{texPath}' ({width}x{height}), originalTexID={entry.Texture_ID}")
        Dim channelLogger As Action(Of String) = Sub(msg) NpcPreviewLog.Log($"  [FACETINT/{channel}]{msg}")
        Dim newTexId As Integer = FaceTintCompositor.ComposeOntoFaceTexture(entry.Texture_ID, width, height, layers, channel, logger:=channelLogger)
        If newTexId = 0 OrElse newTexId = entry.Texture_ID Then
            NpcPreviewLog.Log($"  [FACETINT/{channel}] compose returned 0 / no-op")
            Return
        End If

        Dim oldId = entry.Texture_ID
        entry.Texture_ID = newTexId
        Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(oldId) : Catch : End Try
        composedAny = True
        NpcPreviewLog.Log($"  [FACETINT/{channel}] replaced cache entry: oldTexID={oldId} ? newTexID={newTexId}")
    End Sub

    ''' <summary>Run the region-swap pre-pass for one channel onto the face mesh's texture for
    ''' that channel, then mutate the model's Textures_Dictionary entry in place so the rest
    ''' of the pipeline (tint compositor + render) sees the modified base. No-op if the swap
    ''' list contributes nothing for this channel (e.g. no MPPT TXST has a TX01 normal).</summary>
    Private Sub ApplyRegionSwapChannelOnto(model As PreviewModel,
                                           texPath As String,
                                           knownEntry As PreviewModel.Texture_Loaded_Class,
                                           width As Integer, height As Integer,
                                           swaps As IList(Of FaceRegionSwapInput),
                                           channel As FaceTintChannel)
        If model Is Nothing OrElse String.IsNullOrEmpty(texPath) Then Return
        If swaps Is Nothing OrElse swaps.Count = 0 Then Return

        Dim entry = knownEntry
        If entry Is Nothing Then
            If Not model.Textures_Dictionary.TryGetValue(texPath, entry) Then
                NpcPreviewLog.Log($"  [REGION-SWAP/{channel}] '{texPath}' not in cache, skip")
                Return
            End If
        End If
        If entry Is Nothing OrElse Not entry.Loaded OrElse entry.Texture_ID = 0 Then
            NpcPreviewLog.Log($"  [REGION-SWAP/{channel}] '{texPath}' not loaded, skip")
            Return
        End If

        ' Quick pre-check: do any swaps actually contribute to this channel?
        Dim hasContribution As Boolean = False
        For Each sw In swaps
            If sw Is Nothing Then Continue For
            Dim b = sw.GetSwapBytes(channel)
            If b IsNot Nothing AndAlso b.Length > 0 Then
                hasContribution = True
                Exit For
            End If
        Next
        If Not hasContribution Then
            NpcPreviewLog.Log($"  [REGION-SWAP/{channel}] no swap contributes, skip")
            Return
        End If

        NpcPreviewLog.Log($"  [REGION-SWAP/{channel}] applying onto '{texPath}' ({width}x{height}), originalTexID={entry.Texture_ID}")
        Dim channelLogger As Action(Of String) = Sub(msg) NpcPreviewLog.Log($"  [REGION-SWAP/{channel}]{msg}")
        Dim newTexId As Integer = FaceTintCompositor.ApplyRegionSwapsOntoFaceTexture(entry.Texture_ID, width, height, swaps, channel, logger:=channelLogger)
        If newTexId = 0 OrElse newTexId = entry.Texture_ID Then
            NpcPreviewLog.Log($"  [REGION-SWAP/{channel}] returned 0 / no-op")
            Return
        End If

        Dim oldId = entry.Texture_ID
        entry.Texture_ID = newTexId
        Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(oldId) : Catch : End Try
        NpcPreviewLog.Log($"  [REGION-SWAP/{channel}] replaced cache entry: oldTexID={oldId} -> newTexID={newTexId}")
    End Sub

    ''' <summary>Resolve a tint layer texture path to its raw DDS bytes via FilesDictionary.
    ''' Returns Nothing on empty path, missing entry, or read failure.</summary>
    Private Function LoadTintLayerBytes(rawPath As String) As Byte()
        If String.IsNullOrEmpty(rawPath) Then Return Nothing
        Dim normalized = NormalizeDictionaryKeyWithTexturesPrefix(rawPath)
        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(normalized, loc) Then Return Nothing
        Try
            Dim bytes = loc.GetBytes()
            If bytes Is Nothing OrElse bytes.Length = 0 Then Return Nothing
            Return bytes
        Catch
            Return Nothing
        End Try
    End Function

    Private Shared Function TintSlotName(slot As UShort) As String
        Static names As String() = {
            "ForeheadMask", "EyesMask", "NoseMask", "EarsMask", "CheeksMask", "MouthMask", "NeckMask",
            "LipColor", "CheekColor", "Eyeliner", "EyeSocketUpper", "EyeSocketLower", "SkinTone",
            "Paint", "LaughLines", "CheekColorLower", "Nose", "Chin", "Neck", "Forehead", "Dirt",
            "Scars", "FaceDetail", "Brow", "Wrinkles", "Beard"
        }
        If slot >= names.Length Then Return "?"
        Return names(slot)
    End Function

    Private Shared Function BlendOpName(op As UInteger) As String
        Select Case op
            Case 0 : Return "Default"
            Case 1 : Return "Multiply"
            Case 2 : Return "Overlay"
            Case 3 : Return "SoftLight"
            Case 4 : Return "HardLight"
            Case Else : Return $"?{op}"
        End Select
    End Function

    ''' <summary>Decode TTEF flags U16 to a readable name. Diagnostic only — remove for production.</summary>
    Private Shared Function FormatTintFlagsName(flags As UShort) As String
        Dim parts As New List(Of String)
        If (flags And &H1US) <> 0US Then parts.Add("OnOffOnly")
        If (flags And &H2US) <> 0US Then parts.Add("ChargenDetail")
        If (flags And &H4US) <> 0US Then parts.Add("TakesSkinTone")
        Dim unknown As UShort = CUShort(flags And &HFFF8US)
        If unknown <> 0US Then parts.Add($"unknown=0x{unknown:X4}")
        If parts.Count = 0 Then Return "none"
        Return String.Join("+", parts)
    End Function


    ''' <summary>Normalize a texture path for FilesDictionary lookup (ensures "textures\" prefix).</summary>
    Private Shared Function NormalizeDictionaryKeyWithTexturesPrefix(rawPath As String) As String
        If String.IsNullOrWhiteSpace(rawPath) Then Return ""
        Dim normalized = rawPath.Replace("/"c, "\"c).Trim().ToLowerInvariant()
        If Not normalized.StartsWith("textures\") Then
            normalized = "textures\" & normalized
        End If
        Return normalized
    End Function

    ''' <summary>Per-resolve cache of LVLN picks. When the same LVLN is encountered multiple times
    ''' during a single NPC resolution (e.g. Traits and Model both use same LVLN), the same NPC
    ''' is returned. This is how FO4 works: one random pick per LVLN per spawn.</summary>
    <ThreadStatic> Private Shared _lvlnPickCache As Dictionary(Of UInteger, UInteger)

    ''' <summary>Resolve the NPC's base visual state (traits + model, without outfit expansion).</summary>
    Private Function ResolveNPCBaseState(npc As NPC_Data) As NPCVisualState
        ' Fresh LVLN pick cache for this resolution — ensures consistent picks across categories
        _lvlnPickCache = New Dictionary(Of UInteger, UInteger)()

        Dim warnings As New List(Of String)
        Dim traits = ResolveTraitsStateFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings)
        Dim inventory = ResolveInventoryStateFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings)
        Dim model = ResolveModelAnimationStateFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings)

        If traits Is Nothing Then traits = CreateOwnTraitsState(npc)
        If inventory Is Nothing Then inventory = CreateOwnInventoryState(npc)
        If model Is Nothing Then model = CreateOwnModelAnimationState(npc)

        Dim state As New NPCVisualState With {
            .FormID = npc.FormID,
            .RootNpcFormID = npc.FormID,
            .IsFemale = traits.IsFemale,
            .RaceFormID = traits.RaceFormID,
            .SkinFormID = traits.SkinFormID,
            .DefaultOutfitFormID = inventory.DefaultOutfitFormID,
            .SleepOutfitFormID = inventory.SleepOutfitFormID,
            .HeadTextureFormID = model.HeadTextureFormID,
            .HairColorFormID = model.HairColorFormID,
            .FacialHairColorFormID = model.FacialHairColorFormID,
            .HasTextureLighting = model.HasTextureLighting,
            .TextureLightingColor = model.TextureLightingColor,
            .WeightThin = traits.WeightThin,
            .WeightMuscular = traits.WeightMuscular,
            .WeightFat = traits.WeightFat
        }

        state.HeadPartFormIDs.AddRange(model.HeadPartFormIDs)
        ApplyRaceFallbacks(state)
        state.HeadPartFormIDs = state.HeadPartFormIDs.Where(Function(id) id <> 0UI).Distinct().ToList()

        Return state
    End Function

    ''' <summary>Resolve outfit options for the combo selector.
    ''' Each OTFT item that is ARMO goes into every outfit as a fixed piece.
    ''' Each OTFT item that is LVLI: its top-level entries are the outfit variants.
    ''' Each variant collects ALL armor pieces from that LVLI entry (resolving nested LVLI depth-first, first leaf).
    ''' Result: one combo entry per outfit variant, each containing all fixed + variant armor pieces.</summary>
    Private Function ResolveOutfitOptions(state As NPCVisualState) As List(Of (Name As String, ArmorFormIDs As List(Of UInteger)))
        Dim result As New List(Of (Name As String, ArmorFormIDs As List(Of UInteger)))
        If state Is Nothing OrElse state.DefaultOutfitFormID = 0UI Then Return result

        Dim outfitRec = _pluginManager.GetRecord(state.DefaultOutfitFormID)
        If outfitRec Is Nothing OrElse outfitRec.Header.Signature <> "OTFT" Then Return result

        Dim otft = RecordParsers.ParseOTFT(outfitRec, _pluginManager)
        NpcPreviewLog.Log($"  [OTFT-OPTIONS] {otft.EditorID} FID={state.DefaultOutfitFormID:X8} items={otft.ItemFormIDs.Count}")

        ' Separate fixed ARMO pieces from LVLI variant sources
        Dim fixedPieces As New List(Of UInteger)
        Dim variantSources As New List(Of UInteger) ' top-level LVLI FormIDs

        For Each itemFormID In otft.ItemFormIDs
            Dim itemRec = _pluginManager.GetRecord(itemFormID)
            If itemRec Is Nothing Then Continue For

            Select Case itemRec.Header.Signature
                Case "ARMO"
                    Dim terminalID = ResolveTerminalArmorFormID(itemFormID, New HashSet(Of UInteger)(), New List(Of String))
                    If terminalID <> 0UI Then fixedPieces.Add(terminalID)
                Case "LVLI"
                    variantSources.Add(itemFormID)
            End Select
        Next

        If variantSources.Count = 0 Then
            ' No leveled items: single outfit with all fixed pieces
            If fixedPieces.Count > 0 Then
                result.Add((DescribeOutfitArmorSet(fixedPieces), fixedPieces))
            End If
            NpcPreviewLog.Log($"  [OTFT-OPTIONS] 1 fixed outfit ({fixedPieces.Count} pieces)")
            Return result
        End If

        ' For each top-level LVLI: its entries are the outfit variants.
        ' Each variant = fixed pieces + all armor pieces collected from that LVLI entry.
        For Each lvliFormID In variantSources
            Dim lvliRec = _pluginManager.GetRecord(lvliFormID)
            If lvliRec Is Nothing OrElse lvliRec.Header.Signature <> "LVLI" Then Continue For

            Dim lvli = RecordParsers.ParseLVLI(lvliRec, _pluginManager)
            NpcPreviewLog.Log($"    [LVLI] {lvli.EditorID} FID={lvliFormID:X8} entries={lvli.Entries.Count}")

            For Each entry In lvli.Entries
                If entry.FormID = 0UI Then Continue For
                Dim entryRec = _pluginManager.GetRecord(entry.FormID)
                If entryRec Is Nothing Then Continue For

                ' Collect all armor pieces from this entry (one complete outfit variant)
                Dim variantPieces As New List(Of UInteger)(fixedPieces)
                CollectAllArmorFromEntry(entry.FormID, New HashSet(Of UInteger)(), variantPieces)

                If variantPieces.Count > 0 Then
                    Dim distinct = variantPieces.Distinct().ToList()
                    Dim entryLabel = If(entryRec.EditorID <> "", entryRec.EditorID, entry.FormID.ToString("X8"))
                    result.Add(($"{entryLabel} ({distinct.Count} pcs)", distinct))
                    NpcPreviewLog.Log($"      variant: {entryLabel} ? {distinct.Count} pieces")
                End If
            Next
        Next

        ' Deduplicate identical outfit sets
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim deduped As New List(Of (Name As String, ArmorFormIDs As List(Of UInteger)))
        For Each opt In result
            Dim key = String.Join("|", opt.ArmorFormIDs.OrderBy(Function(id) id).Select(Function(id) id.ToString("X8")))
            If seen.Add(key) Then deduped.Add(opt)
        Next

        NpcPreviewLog.Log($"  [OTFT-OPTIONS] {deduped.Count} outfit variants resolved")
        Return deduped
    End Function

    ''' <summary>Collect terminal ARMO FormIDs from an entry (ARMO or LVLI).
    ''' Respects UseAll flag: if set, collects ALL entries. If not, picks one random entry.
    ''' Recurses for nested LVLI.</summary>
    Private Sub CollectAllArmorFromEntry(formID As UInteger, visited As HashSet(Of UInteger), result As List(Of UInteger))
        If formID = 0UI OrElse visited.Contains(formID) Then Return
        Dim rec = _pluginManager.GetRecord(formID)
        If rec Is Nothing Then Return

        Select Case rec.Header.Signature
            Case "ARMO"
                Dim terminalID = ResolveTerminalArmorFormID(formID, New HashSet(Of UInteger)(), New List(Of String))
                If terminalID <> 0UI Then result.Add(terminalID)
            Case "LVLI"
                visited.Add(formID)
                Dim lvli = RecordParsers.ParseLVLI(rec, _pluginManager)
                Dim usableEntries = lvli.Entries.Where(Function(e) e.FormID <> 0UI).ToList()

                If lvli.UseAll Then
                    ' UseAll: include ALL entries (this is how outfit slot lists work)
                    For Each entry In usableEntries
                        CollectAllArmorFromEntry(entry.FormID, New HashSet(Of UInteger)(visited), result)
                    Next
                Else
                    ' Pick one random entry (this is how variant selection works)
                    If usableEntries.Count > 0 Then
                        Dim entry = usableEntries(_rng.Next(usableEntries.Count))
                        CollectAllArmorFromEntry(entry.FormID, New HashSet(Of UInteger)(visited), result)
                    End If
                End If
                visited.Remove(formID)
        End Select
    End Sub

    ''' <summary>Get a random terminal ARMO from a LVLI.</summary>
    Private Function GetRandomLVLILeafArmor(lvliFormID As UInteger, visited As HashSet(Of UInteger)) As UInteger
        If lvliFormID = 0UI OrElse visited.Contains(lvliFormID) Then Return 0UI
        Dim lvliRec = _pluginManager.GetRecord(lvliFormID)
        If lvliRec Is Nothing OrElse lvliRec.Header.Signature <> "LVLI" Then Return 0UI

        visited.Add(lvliFormID)
        Dim lvli = RecordParsers.ParseLVLI(lvliRec, _pluginManager)
        Dim usableEntries = lvli.Entries.Where(Function(e) e.FormID <> 0UI).ToList()
        If usableEntries.Count = 0 Then Return 0UI

        ' Pick random entry
        Dim entry = usableEntries(_rng.Next(usableEntries.Count))
        Dim entryRec = _pluginManager.GetRecord(entry.FormID)
        If entryRec Is Nothing Then Return 0UI

        Select Case entryRec.Header.Signature
            Case "ARMO"
                Return ResolveTerminalArmorFormID(entry.FormID, New HashSet(Of UInteger)(), New List(Of String))
            Case "LVLI"
                Return GetRandomLVLILeafArmor(entry.FormID, visited)
            Case Else
                Return 0UI
        End Select
    End Function


    ''' <summary>Recursively flatten a LVLI into a list of unique terminal ARMO FormIDs (leaf armors).</summary>
    Private Sub FlattenLVLIToArmorList(lvliFormID As UInteger, visited As HashSet(Of UInteger), result As List(Of UInteger))
        If lvliFormID = 0UI OrElse visited.Contains(lvliFormID) Then Return
        Dim lvliRec = _pluginManager.GetRecord(lvliFormID)
        If lvliRec Is Nothing OrElse lvliRec.Header.Signature <> "LVLI" Then Return

        visited.Add(lvliFormID)
        Dim lvli = RecordParsers.ParseLVLI(lvliRec, _pluginManager)

        For Each entry In lvli.Entries
            If entry.FormID = 0UI Then Continue For
            Dim entryRec = _pluginManager.GetRecord(entry.FormID)
            If entryRec Is Nothing Then Continue For

            Select Case entryRec.Header.Signature
                Case "ARMO"
                    Dim terminalID = ResolveTerminalArmorFormID(entry.FormID, New HashSet(Of UInteger)(), New List(Of String))
                    If terminalID <> 0UI AndAlso Not result.Contains(terminalID) Then
                        result.Add(terminalID)
                    End If
                Case "LVLI"
                    FlattenLVLIToArmorList(entry.FormID, visited, result)
            End Select
        Next

        visited.Remove(lvliFormID)
    End Sub

    Private Function DescribeOutfitArmorSet(armorIDs As List(Of UInteger)) As String
        If armorIDs.Count = 0 Then Return "(base body)"
        Dim names As New List(Of String)
        For Each fid In armorIDs
            Dim rec = _pluginManager.GetRecord(fid)
            names.Add(If(rec IsNot Nothing, rec.EditorID, fid.ToString("X8")))
        Next
        Return String.Join(" + ", names)
    End Function

    ' RenderVariantAsync and PopulateVariantNodes removed — replaced by on-demand RenderCurrentStateAsync + outfit combo.

    Private Async Function EnsureAssetDictionaryAsync() As Task
        Dim loadTask As Task = Nothing

        SyncLock _assetDictionaryLock
            If _assetDictionaryLoadTask Is Nothing Then
                ToolStripProgressBar1.Visible = True
                ToolStripProgressBar1.Minimum = 0
                ToolStripProgressBar1.Maximum = 1
                ToolStripProgressBar1.Value = 0

                Dim progress = New Progress(Of (Stepn As String, Value As Integer, Max As Integer))(
                    Sub(info) UpdateAssetLoadProgress(info)
                )

                _assetDictionaryLoadTask = FilesDictionary_class.Fill_DictionaryAsync(_dataPath, progress)
            End If

            loadTask = _assetDictionaryLoadTask
        End SyncLock

        Await loadTask

        If InvokeRequired Then
            BeginInvoke(Sub() ToolStripProgressBar1.Visible = False)
        Else
            ToolStripProgressBar1.Visible = False
        End If
    End Function

    ' ==========================================================================
    ' FACE TINT RENDERING — TODO (not implemented)
    '
    ' What we know:
    ' - RACE record has tint templates (parsed in RACE_TintTemplateGroup/Option):
    '   slots like SkinTone(12), LipColor(7), Eyeliner(9), Dirt(20), Scars(21), etc.
    '   Each option has: mask DDS texture path, template colors (CLFM FormIDs), blend op
    ' - NPC record has tint layers (TETI/TEND):
    '   Discriminator=1 (Palette) TEND is 7 bytes: Value(1) + RGB(3) + pad(3)
    '   Discriminator=2 (TextureSet) TEND is 1 byte: Value only
    '   Value byte / 100 = opacity (0..1). Verified empirically on vanilla NPCs.
    ' - Mask textures are greyscale DXT1 (white=apply, black=don't). Color comes from TEND/template.
    ' - FBO composition works: masks load, colors resolve, output texture is correct (verified via PNG dump)
    ' - Problem: could not get the composed texture to render on the face mesh.
    '   The shader received bComposedTint=true and the texture was bound, but no visible effect.
    '   Likely issue: the FO4UnifiedMaterial_Class instance where ComposedTintMask_ID was set
    '   may not be the same instance the render reads (material may get cloned/recreated during pipeline).
    '   Needs investigation of how RenderShapes/LoadShapeSafe handles material instances.
    ' ==========================================================================

    ''' <summary>Build a face morph resolver for the given NPC visual state.
    ''' Uses MSDK/MSDV morph presets from Chargen.tri (via RACE mapping) and
    ''' FMRI/FMRS face bone transforms (applied via skeleton DeltaTransform).
    ''' Body weight morphs are NOT applied (vanilla uses hardcoded bone scaling, not TRI).</summary>
    Private Function BuildFaceMorphResolver(state As NPCVisualState, renderData As PreviewResolutionResult) As IMorphResolver
        If state Is Nothing Then Return Nothing

        ' Get the full NPC_Data for the model source (the NPC whose face we're rendering)
        Dim modelNpcFormID = If(state.ModelSourceFormID <> 0UI, state.ModelSourceFormID, state.FormID)
        Dim npcData = GetParsedNpc(modelNpcFormID)
        If npcData Is Nothing Then Return Nothing

        ' No morph data at all? Skip
        If npcData.MorphValues.Count = 0 Then Return Nothing

        ' Get RACE morph definitions for mapping MSDK keys ? morph names
        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)

        Dim morphValueDefs = race.MorphValues
        Dim morphPresetDefs = If(state.IsFemale, race.FemaleMorphPresets, race.MaleMorphPresets)
        Dim morphGroups = If(state.IsFemale, race.FemaleMorphGroups, race.MaleMorphGroups)

        ' Build face morph name map from RACE (FMRI index ? morph name) for FMRI/FMRS
        Dim faceMorphDefs = If(state.IsFemale, race.FemaleFaceMorphs, race.MaleFaceMorphs)
        Dim faceMorphNameMap As New Dictionary(Of UInteger, String)
        For Each fmd In faceMorphDefs
            If fmd.Index <> 0UI AndAlso fmd.Name <> "" Then faceMorphNameMap(fmd.Index) = fmd.Name
        Next

        NpcPreviewLog.Log($"  [MORPH] NPC {npcData.EditorID} [{modelNpcFormID:X8}]: MorphValues={npcData.MorphValues.Count} FaceMorphs={npcData.FaceMorphs.Count} FMIN={npcData.FacialMorphIntensity:F3} RaceMorphValueDefs={morphValueDefs.Count} RacePresetDefs={morphPresetDefs.Count} RaceMorphGroups={morphGroups.Count} FaceMorphNameMap={faceMorphNameMap.Count}")

        ' === Raw MSDV dump ===
        ' For each NPC MorphValue key, annotate which RACE def (if any) it matches. Slider
        ' defs map via Index -> MinName/MaxName (signed weight selects one). Preset defs map
        ' via Index -> PresetName (localized display) + MorphName (Chargen.tri morph target).
        ' For presets we ALSO resolve the owning Morph Group (region name + MPPK mask enum)
        ' and the preset's MPPT -> TXST editor ID so we can see the per-region texture swap
        ' that Bethesda's engine applies.
        Dim sliderDefByIndex As New Dictionary(Of UInteger, RACE_MorphValueDef)
        For Each d In morphValueDefs
            sliderDefByIndex(d.Index) = d
        Next
        ' Map each preset to its (group, preset) pair so we can report the region context.
        Dim presetToGroupByIndex As New Dictionary(Of UInteger, List(Of (Grp As RACE_MorphGroup, Pst As RACE_MorphPresetDef)))
        For Each g In morphGroups
            For Each p In g.Presets
                Dim list As List(Of (Grp As RACE_MorphGroup, Pst As RACE_MorphPresetDef)) = Nothing
                If Not presetToGroupByIndex.TryGetValue(p.Index, list) Then
                    list = New List(Of (Grp As RACE_MorphGroup, Pst As RACE_MorphPresetDef))
                    presetToGroupByIndex(p.Index) = list
                End If
                list.Add((g, p))
            Next
        Next
        ' Pre-collect every region-mask TTET[0] path referenced by any morph group of THIS
        ' RACE whose MPPK resolves to a slot 0..6, then load them all in one batch via the
        ' background DDS loader. Each RACE has its own TintTemplateGroups so the mask paths
        ' are race-specific (HumanRace, GhoulRace, SynthRace differ). Caching, if added later,
        ' must be keyed by race+gender. This lets us report each mask's pixel format and
        ' dimensions in the dump below without doing one-shot GL uploads.
        Dim maskInfoByPath As New Dictionary(Of String, PreviewModel.Texture_Loaded_Class)(StringComparer.OrdinalIgnoreCase)
        Try
            Dim maskPathSet As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each g In morphGroups
                Dim s As TintSlot
                If Not g.TryGetMaskSlot(s) Then Continue For
                For Each opt In race.FindTintOptionsBySlot(s, state.IsFemale)
                    If opt.Textures Is Nothing Then Continue For
                    For Each tx In opt.Textures
                        If String.IsNullOrEmpty(tx) Then Continue For
                        Dim norm = NormalizeDictionaryKeyWithTexturesPrefix(tx)
                        If FilesDictionary_class.Dictionary.ContainsKey(norm) Then maskPathSet.Add(norm)
                    Next
                Next
            Next
            If maskPathSet.Count > 0 Then
                Dim batch = DirectXDDSLoader.Load_And_GenerateOpenGLTextures_FromDictionary(
                    maskPathSet.ToArray(), True, True)
                For Each kvp In batch
                    maskInfoByPath(kvp.Key) = kvp.Value
                Next
            End If
        Catch ex As Exception
            NpcPreviewLog.Log($"  [MORPH-RAW] mask preload error: {ex.Message}")
        End Try

        NpcPreviewLog.Log($"  [MORPH-RAW] === MSDV dump for {npcData.EditorID} ({npcData.MorphValues.Count} keys) ===")
        For Each kvp In npcData.MorphValues
            Dim key As UInteger = kvp.Key
            Dim val As Single = kvp.Value
            Dim sliderDef As RACE_MorphValueDef = Nothing
            Dim presetPairs As List(Of (Grp As RACE_MorphGroup, Pst As RACE_MorphPresetDef)) = Nothing
            Dim sliderMatched As Boolean = sliderDefByIndex.TryGetValue(key, sliderDef)
            Dim presetMatched As Boolean = presetToGroupByIndex.TryGetValue(key, presetPairs)

            Dim annotations As New List(Of String)
            If sliderMatched Then
                annotations.Add($"slider[minName='{sliderDef.MinName}', maxName='{sliderDef.MaxName}']")
            End If
            If presetMatched Then
                For Each pair In presetPairs
                    annotations.Add($"preset[group='{pair.Grp.Name}', maskEnum={pair.Grp.MaskEnum}, presetName='{pair.Pst.PresetName}', morphName='{pair.Pst.MorphName}']")
                Next
            End If
            If annotations.Count = 0 Then annotations.Add("UNMATCHED (no slider def, no preset def)")

            NpcPreviewLog.Log($"    [MORPH-RAW] key=0x{key:X8} value={val:F4} -> {String.Join(" | ", annotations)}")

            ' For each matched preset, emit sub-lines with the resolved TXST texture paths
            ' (TX00 diffuse, TX01 normal, TX02 wrinkles, TX07 smooth spec) and the region
            ' mask texture path from the tint option indexed by MaskEnum.
            If presetMatched Then
                For Each pair In presetPairs
                    Dim g = pair.Grp
                    Dim p = pair.Pst

                    Dim txstDesc As String = "none"
                    Dim txstDiffuse As String = ""
                    Dim txstNormal As String = ""
                    Dim txstWrinkles As String = ""
                    Dim txstSpec As String = ""
                    If p.TextureFormID <> 0UI Then
                        Dim txstRec = _pluginManager.GetRecord(p.TextureFormID)
                        If txstRec IsNot Nothing AndAlso txstRec.Header.Signature = "TXST" Then
                            Dim txst = RecordParsers.ParseTXST(txstRec, _pluginManager)
                            txstDesc = $"{txstRec.EditorID} [{p.TextureFormID:X8}]"
                            If txst IsNot Nothing Then
                                txstDiffuse = txst.DiffuseTexture
                                txstNormal = txst.NormalTexture
                                txstWrinkles = txst.WrinklesTexture
                                txstSpec = txst.SmoothSpecTexture
                            End If
                        Else
                            txstDesc = $"[{p.TextureFormID:X8}]"
                        End If
                    End If

                    ' Resolve MPPK enum -> TintSlot 0..6 -> all tint options of that slot.
                    ' MPPK 1221..1227 (female) and 1171..1177 (male) are SEMANTIC region IDs,
                    ' not array indices. Each maps by convention to TETI.Slot 0..6 (Forehead..Neck).
                    ' The actual mask DDS lives in each option's TTET[0].
                    Dim slot As TintSlot
                    Dim slotResolved = g.TryGetMaskSlot(slot)
                    NpcPreviewLog.Log($"       MPPT={txstDesc}")
                    If txstDiffuse <> "" Then NpcPreviewLog.Log($"         TX00 D='{txstDiffuse}'")
                    If txstNormal <> "" Then NpcPreviewLog.Log($"         TX01 N='{txstNormal}'")
                    If txstWrinkles <> "" Then NpcPreviewLog.Log($"         TX02 W='{txstWrinkles}'")
                    If txstSpec <> "" Then NpcPreviewLog.Log($"         TX07 S='{txstSpec}'")
                    If Not slotResolved Then
                        NpcPreviewLog.Log($"       mppk={g.MaskEnum} -> NO SLOT (out of male/female region range)")
                    Else
                        Dim slotOpts = race.FindTintOptionsBySlot(slot, state.IsFemale)
                        NpcPreviewLog.Log($"       mppk={g.MaskEnum} -> slot={CInt(slot)} ({slot}) optionsInSlot={slotOpts.Count}")
                        For optIdx = 0 To slotOpts.Count - 1
                            Dim opt = slotOpts(optIdx)
                            Dim ttetCount = If(opt.Textures Is Nothing, 0, opt.Textures.Count)
                            NpcPreviewLog.Log($"         opt[{optIdx}] index={opt.Index} entryType={opt.EntryType} name='{opt.Name}' TTET.Count={ttetCount}")
                            If opt.Textures IsNot Nothing Then
                                For texIdx = 0 To opt.Textures.Count - 1
                                    Dim texPath = opt.Textures(texIdx)
                                    Dim normalized = NormalizeDictionaryKeyWithTexturesPrefix(texPath)
                                    Dim exists = (texPath <> "") AndAlso FilesDictionary_class.Dictionary.ContainsKey(normalized)
                                    Dim fmtDesc As String = ""
                                    Dim loadedTex As PreviewModel.Texture_Loaded_Class = Nothing
                                    If exists AndAlso maskInfoByPath.TryGetValue(normalized, loadedTex) AndAlso loadedTex IsNot Nothing AndAlso loadedTex.Loaded Then
                                        fmtDesc = $" dxgiOrig={loadedTex.DGXFormat_Original} dxgiFinal={loadedTex.DGXFormat_Final} {loadedTex.Size.Width}x{loadedTex.Size.Height}"
                                    End If
                                    NpcPreviewLog.Log($"           TTET[{texIdx}]={If(exists, "OK ", "MISS")} '{texPath}'{fmtDesc}")
                                Next
                            End If
                        Next
                    End If
                Next
            End If
        Next
        NpcPreviewLog.Log($"  [MORPH-RAW] === end MSDV dump ===")

        Return New NpcMorphResolver(
            npcData,
            faceMorphNameMap:=faceMorphNameMap,
            morphValueDefs:=morphValueDefs,
            morphPresetDefs:=morphPresetDefs,
            meshDictKeys:=renderData.MeshDictKeys,
            shapeChargenTriPaths:=renderData.ShapeChargenTriPaths,
            shapeRaceMorphTriPaths:=renderData.ShapeRaceMorphTriPaths)
    End Function

    ''' <summary>Load the bytes of the race-specific face skeleton file.
    ''' Derives the path from RACE.ANAM (the body skeleton) by Bethesda naming convention:
    '''   &lt;body_basename&gt;_&lt;gender&gt;_faceBones.nif   (gender-specific, preferred)
    '''   &lt;body_basename&gt;_faceBones.nif             (generic fallback)
    ''' Returns Nothing if the race has no body skeleton declared, or if neither candidate exists.</summary>
    Private Function TryLoadFaceSkeletonBytes(state As NPCVisualState) As Byte()
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return Nothing
        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)

        Dim bodySkel = If(state.IsFemale, race.FemaleSkeletonPath, race.MaleSkeletonPath)
        If String.IsNullOrEmpty(bodySkel) Then bodySkel = If(state.IsFemale, race.MaleSkeletonPath, race.FemaleSkeletonPath)
        If String.IsNullOrEmpty(bodySkel) Then Return Nothing

        ' Strip .nif, build candidate face skel paths
        Dim basePath = bodySkel
        If basePath.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) Then
            basePath = basePath.Substring(0, basePath.Length - 4)
        End If

        Dim genderSuffix = If(state.IsFemale, "_female", "_male")
        Dim candidates = {
            basePath & genderSuffix & "_faceBones.nif",
            basePath & "_faceBones.nif"
        }

        For Each raw In candidates
            Dim normalized = NormalizeDictionaryKeyWithMeshesPrefix(raw)
            Dim loc As FilesDictionary_class.File_Location = Nothing
            If FilesDictionary_class.Dictionary.TryGetValue(normalized, loc) Then
                Try
                    Dim bytes = loc.GetBytes()
                    If bytes IsNot Nothing AndAlso bytes.Length > 0 Then
                        NpcPreviewLog.Log($"  [FACE-SKEL] loaded '{normalized}' ({bytes.Length} bytes) for race {race.EditorID}")
                        Return bytes
                    End If
                Catch ex As Exception
                    NpcPreviewLog.Log($"  [FACE-SKEL] error reading '{normalized}': {ex.Message}")
                End Try
            End If
        Next

        NpcPreviewLog.Log($"  [FACE-SKEL] no face skeleton found for race {race.EditorID} (body skel='{bodySkel}')")
        Return Nothing
    End Function

    ''' <summary>Cache of parsed FacialBoneRegions files per race/gender key (e.g. "HumanRace:female").</summary>
    Private Shared ReadOnly _facialBoneRegionsCache As New Dictionary(Of String, FacialBoneRegionsFile)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Load and parse the per-race HumanRaceFacialBoneRegions<Gender>.txt JSON file.
    ''' Returns Nothing if the file doesn't exist or can't be parsed.</summary>
    Private Function GetFacialBoneRegionsForRace(race As RACE_Data, isFemale As Boolean) As FacialBoneRegionsFile
        If race Is Nothing OrElse String.IsNullOrEmpty(race.EditorID) Then Return Nothing

        Dim genderKey = If(isFemale, "Female", "Male")
        Dim cacheKey = race.EditorID & ":" & genderKey

        Dim cached As FacialBoneRegionsFile = Nothing
        If _facialBoneRegionsCache.TryGetValue(cacheKey, cached) Then Return cached

        ' Build candidate paths. Use race.EditorID as the base name (HumanRace, GhoulRace, etc.)
        Dim dataPath = $"meshes\actors\character\characterassets\{race.EditorID}FacialBoneRegions{genderKey}.txt".ToLowerInvariant()
        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(dataPath, loc) Then
            NpcPreviewLog.Log($"  [FBR] no regions file for {race.EditorID}/{genderKey}: '{dataPath}'")
            _facialBoneRegionsCache(cacheKey) = Nothing
            Return Nothing
        End If

        Try
            Dim bytes = loc.GetBytes()
            Dim parsed = FacialBoneRegionsFile.ParseFromBytes(bytes)
            _facialBoneRegionsCache(cacheKey) = parsed
            If parsed Is Nothing Then
                NpcPreviewLog.Log($"  [FBR] parse failed for '{dataPath}'")
            End If
            Return parsed
        Catch ex As Exception
            NpcPreviewLog.Log($"  [FBR] error loading '{dataPath}': {ex.Message}")
            _facialBoneRegionsCache(cacheKey) = Nothing
            Return Nothing
        End Try
    End Function

    ''' <summary>Build a Poses_class that carries face bone deltas from FMRI/FMRS data.
    ''' Reads the race's FacialBoneRegions JSON file, then for each NPC FMRI entry:
    '''   - Looks up the region by ID
    '''   - For each bone in the region, computes a delta transform by lerping Min?Default?Max
    '''     using the FMRS slider values (per-axis independent, clamped to [-1,+1], scaled by FMIN)
    '''   - Converts the Transform_Class (Matrix33 rotation + Vec3 translation + scale) into a
    '''     PoseTransformData (axis-angle via Matrix33ToBSRotation for WardrobeManager source)
    '''   - Prepends "skin_" to the bone name to match what's in SkeletonDictionary
    ''' Returns Nothing if no regions file found or no non-zero FMRS values.</summary>
    Private Function BuildFaceBoneTransforms(state As NPCVisualState) As Poses_class
        If state Is Nothing Then Return Nothing

        Dim modelNpcFormID = If(state.ModelSourceFormID <> 0UI, state.ModelSourceFormID, state.FormID)
        Dim npcData = GetParsedNpc(modelNpcFormID)
        If npcData Is Nothing OrElse npcData.FaceMorphs.Count = 0 Then Return Nothing

        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)

        Dim regionsFile = GetFacialBoneRegionsForRace(race, state.IsFemale)
        If regionsFile Is Nothing Then Return Nothing

        Dim result As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        Dim matchedRegions As Integer = 0
        Dim unmatchedRegions As Integer = 0
        ' Default to 1.0 when the NPC record doesn't carry an explicit FMIN.
        Dim fmin = If(npcData.FacialMorphIntensity <= 0.0F, 1.0F, npcData.FacialMorphIntensity)
        Dim maxDeltaSeen As Single = 0

        For Each fm In npcData.FaceMorphs
            Dim region As FacialBoneRegion = Nothing
            If Not regionsFile.Regions.TryGetValue(fm.Index, region) Then
                unmatchedRegions += 1
                Continue For
            End If

            ' Per-axis sliders. LerpFmrs() clamps to [-1,+1] internally, so we just pass raw.
            Dim px = fm.PositionX
            Dim py = fm.PositionY
            Dim pz = fm.PositionZ
            Dim rx = fm.RotationX
            Dim ry = fm.RotationY
            Dim rz = fm.RotationZ
            Dim sc = fm.Scale

            NpcPreviewLog.Log($"    [FMRS] id={fm.Index} '{region.Name}' P=({px:F3},{py:F3},{pz:F3}) R=({rx:F3},{ry:F3},{rz:F3}) S={sc:F3}")

            ' Skip regions with all-zero FMRS (no deformation at all)
            If Math.Abs(px) < 0.0001F AndAlso Math.Abs(py) < 0.0001F AndAlso Math.Abs(pz) < 0.0001F AndAlso
               Math.Abs(rx) < 0.0001F AndAlso Math.Abs(ry) < 0.0001F AndAlso Math.Abs(rz) < 0.0001F AndAlso
               Math.Abs(sc) < 0.0001F Then Continue For

            matchedRegions += 1

            ' For each bone in the region, compute the per-axis deltas via LerpFmrs (which now
            ' returns the DELTA from default, not the lerped absolute) and post-scale by FMIN.
            For Each boneEntry In region.Bones
                Dim targetBoneName = "skin_" & boneEntry.Bone

                Dim deltaPos As New System.Numerics.Vector3(
                    LerpFmrs(px, region.DefaultPosition.X, boneEntry.MinimaPosition.X, boneEntry.MaximaPosition.X) * fmin,
                    LerpFmrs(py, region.DefaultPosition.Y, boneEntry.MinimaPosition.Y, boneEntry.MaximaPosition.Y) * fmin,
                    LerpFmrs(pz, region.DefaultPosition.Z, boneEntry.MinimaPosition.Z, boneEntry.MaximaPosition.Z) * fmin)

                Dim deltaRot As New System.Numerics.Vector3(
                    LerpFmrs(rx, region.DefaultRotation.X, boneEntry.MinimaRotation.X, boneEntry.MaximaRotation.X) * fmin,
                    LerpFmrs(ry, region.DefaultRotation.Y, boneEntry.MinimaRotation.Y, boneEntry.MaximaRotation.Y) * fmin,
                    LerpFmrs(rz, region.DefaultRotation.Z, boneEntry.MinimaRotation.Z, boneEntry.MaximaRotation.Z) * fmin)

                ' Single scale slider drives all 3 scale axes (the JSON Min/Max have a Vec3 scale).
                Dim deltaScale As New System.Numerics.Vector3(
                    LerpFmrs(sc, region.DefaultScale.X, boneEntry.MinimaScale.X, boneEntry.MaximaScale.X) * fmin,
                    LerpFmrs(sc, region.DefaultScale.Y, boneEntry.MinimaScale.Y, boneEntry.MaximaScale.Y) * fmin,
                    LerpFmrs(sc, region.DefaultScale.Z, boneEntry.MinimaScale.Z, boneEntry.MaximaScale.Z) * fmin)

                ' Build Transform_Class: rotation from euler degrees (JSON values are in degrees),
                ' translation from position delta, scale '0 = no change' convention ? 1 + delta.
                Dim rotation = Transform_Class.EulerXYZToMatrix33(deltaRot.X, deltaRot.Y, deltaRot.Z)
                Dim xform As New Transform_Class With {
                    .Rotation = rotation,
                    .Translation = deltaPos,
                    .Scale = 1.0F + deltaScale.X
                }

                ' Track max |delta| magnitude for diagnostic visibility
                Dim mag = deltaPos.Length()
                If mag > maxDeltaSeen Then maxDeltaSeen = mag

                ' Compose with any previous transform on the same bone (multiple regions may affect same bone)
                Dim existing As Transform_Class = Nothing
                If result.TryGetValue(targetBoneName, existing) AndAlso existing IsNot Nothing Then
                    result(targetBoneName) = existing.ComposeTransforms(xform)
                Else
                    result(targetBoneName) = xform
                End If
            Next
        Next

        If result.Count = 0 Then
            NpcPreviewLog.Log($"  [FACE-BONE] no transforms built (matched regions={matchedRegions}, unmatched={unmatchedRegions}, FMIN={fmin:F3})")
            Return Nothing
        End If

        NpcPreviewLog.Log($"  [FACE-BONE] {result.Count} bones deformed across {matchedRegions} regions (unmatched={unmatchedRegions}, FMIN={fmin:F3}, maxPosDelta={maxDeltaSeen:F3} NIF units)")

        ' Convert the Transform_Class deltas into a Poses_class with PoseTransformData entries.
        Dim pose As New Poses_class With {
            .Name = "FMRS Face Morph",
            .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
            .Transforms = New Dictionary(Of String, PoseTransformData)
        }
        For Each kv In result
            Dim xform = kv.Value
            Dim rotVec = Transform_Class.Matrix33ToBSRotation(xform.Rotation)
            pose.Transforms(kv.Key) = New PoseTransformData With {
                .X = xform.Translation.X,
                .Y = xform.Translation.Y,
                .Z = xform.Translation.Z,
                .Yaw = rotVec.X,
                .Pitch = rotVec.Y,
                .Roll = rotVec.Z,
                .Scale = xform.Scale
            }
        Next

        Return pose
    End Function

    ''' <summary>Compute the per-axis DELTA from a FMRS-driven slider.
    ''' fmrsVal is the NPC's slider value for this axis (clamped to [-1,+1] by the engine).
    '''   fmrsVal = 0  ? 0      (no morph applied)
    '''   fmrsVal = +1 ? maxVal - defaultVal
    '''   fmrsVal = -1 ? minVal - defaultVal
    ''' Negative values map toward minima, positive toward maxima. Returns the DELTA from the
    ''' rest pose (default), NOT the lerped absolute value, so applying the result is just an
    ''' add/multiply on the bone's existing local transform.
    ''' Source: 3-point lerp Min?Default?Max inferred from CommonLibF4 BGSCharacterMorph layout
    ''' (region-level Transform default + per-bone TransformMinMax).</summary>
    Private Shared Function LerpFmrs(fmrsVal As Single, defaultVal As Single, minVal As Single, maxVal As Single) As Single
        Dim s = Math.Max(-1.0F, Math.Min(1.0F, fmrsVal))
        If s >= 0 Then
            Return s * (maxVal - defaultVal)
        Else
            Return (-s) * (minVal - defaultVal)
        End If
    End Function

    ''' <summary>Skeleton resolver that merges the race's face skeleton NIF into the body skeleton, then
    ''' <summary>Skeleton resolver that merges the race's face skeleton (skeleton_&lt;gender&gt;_faceBones.nif)
    ''' into SkeletonDictionary BEFORE the body pose is applied, so that any face bone entries in the
    ''' passed pose get their DeltaTransform set alongside the body bones.
    '''
    ''' Order:
    '''   1. Call base resolver with Nothing pose ? loads body skeleton, HKX injection, but does NOT
    '''      apply pose yet. (If we passed the real pose here, face bone entries would be skipped
    '''      because the face bones aren't in the dict yet.)
    '''   2. Merge face skeleton ? face bones added with DeltaTransform=Nothing.
    '''   3. Call Skeleton_Class.AppplyPoseToSkeleton(realPose) ? applies pose to body + face bones.
    ''' </summary>
    Private Class FaceBoneSkeletonResolver
        Implements ISkeletonResolver

        Private ReadOnly _baseResolver As ISkeletonResolver
        Private ReadOnly _faceSkelBytes As Byte()

        Public Sub New(baseResolver As ISkeletonResolver, faceSkelBytes As Byte())
            _baseResolver = baseResolver
            _faceSkelBytes = faceSkelBytes
        End Sub

        Public Sub ResolveSkeleton(shapes As IEnumerable(Of IRenderableShape), pose As Poses_class) Implements ISkeletonResolver.ResolveSkeleton
            ' 1. Base resolver loads body skeleton + HKX physics injection, but NO pose yet
            '    (we'll apply the pose after merging face bones, so face entries don't get dropped).
            _baseResolver.ResolveSkeleton(shapes, Nothing)

            ' 2. Merge the race's face skeleton into SkeletonDictionary.
            If _faceSkelBytes IsNot Nothing Then
                Dim added = Skeleton_Class.MergeFaceSkeleton(_faceSkelBytes)
                NpcPreviewLog.Log($"  [FACE-SKEL-MERGE] added {added} face bones to SkeletonDictionary")
            End If

            ' 3. Apply the full pose (body + face bone entries) now that all target bones exist.
            If pose IsNot Nothing AndAlso pose.Source <> Poses_class.Pose_Source_Enum.None Then
                Skeleton_Class.AppplyPoseToSkeleton(pose)
            End If
        End Sub
    End Class

    Private Sub UpdateAssetLoadProgress(info As (Stepn As String, Value As Integer, Max As Integer))
        ToolStripProgressBar1.Visible = True
        ToolStripProgressBar1.Minimum = 0
        ToolStripProgressBar1.Maximum = Math.Max(1, info.Max)
        ToolStripProgressBar1.Value = Math.Max(0, Math.Min(info.Value, ToolStripProgressBar1.Maximum))
        SetStatus(info.Stepn)
    End Sub

    Private Function GetOrResolveVariants(npc As NPC_Data) As List(Of PreviewVariantDefinition)
        Dim cached As List(Of PreviewVariantDefinition) = Nothing
        SyncLock _variantCache
            If _variantCache.TryGetValue(npc.FormID, cached) Then
                Return cached
            End If
        End SyncLock

        Dim variants = ResolveNPCVariants(npc)

        SyncLock _variantCache
            _variantCache(npc.FormID) = variants
        End SyncLock

        Return variants
    End Function

    Private Function ResolveNPCVariants(npc As NPC_Data) As List(Of PreviewVariantDefinition)
        Dim warnings As New List(Of String)
        Dim visualStates = ResolveNPCVisualStates(npc, warnings)
        Dim variants As New List(Of PreviewVariantDefinition)
        Dim variantId As Integer = 1

        For Each state In visualStates
            For Each useFaceGen In DetermineFaceGenModes(state)
                Dim previewVariant As New PreviewVariantDefinition With {
                    .RootNpcFormID = npc.FormID,
                    .VariantId = variantId,
                    .DisplayName = BuildVariantDisplayName(variantId, state, useFaceGen),
                    .State = state,
                    .UseFaceGen = useFaceGen
                }
                previewVariant.Warnings.AddRange(warnings)
                variants.Add(previewVariant)
                variantId += 1
            Next
        Next

        Return variants
    End Function

    ''' <summary>Check if pre-baked FaceGen NIF exists for this NPC.
    ''' Vanilla path: meshes\actors\character\facegendata\facegeom\&lt;plugin&gt;\&lt;formid:X8&gt;.nif
    ''' For templated NPCs, uses the model source FormID (the NPC that owns the visual traits).</summary>
    Private Function HasFaceGenAssets(state As NPCVisualState) As Boolean
        If state Is Nothing Then Return False
        Dim modelFormID = If(state.ModelSourceFormID <> 0UI, state.ModelSourceFormID, state.FormID)
        Dim path = ResolveFaceGenNifPath(modelFormID)
        Dim found = path <> "" AndAlso FilesDictionary_class.Dictionary.ContainsKey(path)

        ' Log both ACBS flag candidates and the FaceGen existence for empirical correlation
        Dim npcData = GetParsedNpc(modelFormID)
        Dim acbsFlags As UInteger = If(npcData IsNot Nothing, npcData.AcbsFlags, 0UI)
        Dim tplFlags As UShort = If(npcData IsNot Nothing, npcData.TemplateFlags, CUShort(0))
        Dim isCharGenFacePreset = (acbsFlags And &H4UI) <> 0UI
        Dim usesTemplateTraits = (tplFlags And &H1US) <> 0US
        NpcPreviewLog.Log($"  [FACEGEN-PROBE] NPC={state.FormID:X8} model={modelFormID:X8} ACBS={acbsFlags:X8} tplFlags={tplFlags:X4} CharGenFacePreset={isCharGenFacePreset} UsesTraitsTemplate={usesTemplateTraits} facegenExists={found}")
        Return found
    End Function

    ''' <summary>Try to find a _faceBones.nif variant of the given mesh path.
    ''' Vanilla FO4 ships both <mesh>.nif (skinned to body bones only) and <mesh>_faceBones.nif
    ''' (skinned to face bones like Jaw/LipUpper/Cheek). The _faceBones variant is what the engine
    ''' uses at runtime for chargen/LooksMenu to enable FMRS bone deformation.
    ''' Returns the normalized _faceBones path if it exists in FilesDictionary, or empty string
    ''' if no variant exists. We do NOT filter by partType — empirically hair-region meshes
    ''' (e.g. femalehair28_hairline2.nif ? femalehair28_hairline2_facebones.nif) DO have variants,
    ''' so any partType filter would miss legitimate cases. Let the dictionary lookup decide.</summary>
    Private Function TryGetFaceBonesVariant(meshDictKey As String, partType As Integer) As String
        If String.IsNullOrEmpty(meshDictKey) Then Return ""
        If Not meshDictKey.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) Then Return ""
        Dim candidate = meshDictKey.Substring(0, meshDictKey.Length - 4) & "_facebones.nif"
        If FilesDictionary_class.Dictionary.ContainsKey(candidate) Then Return candidate
        Return ""
    End Function

    ''' <summary>Build the FaceGen NIF lookup path for a given NPC FormID.
    ''' Vanilla FO4 layout:
    '''   meshes\actors\character\facegendata\facegeom\&lt;plugin&gt;\&lt;formid:X8&gt;.nif   (the mesh)
    '''   textures\actors\character\facecustomization\&lt;plugin&gt;\&lt;formid:X8&gt;_d.dds (the texture)
    ''' Returns path normalized for FilesDictionary lookup, or empty if FormID can't be resolved.</summary>
    Private Function ResolveFaceGenNifPath(npcFormID As UInteger) As String
        If npcFormID = 0UI Then Return ""
        Dim pluginName = _pluginManager.GetOriginatingPluginName(npcFormID)
        If String.IsNullOrEmpty(pluginName) Then Return ""
        ' FaceGen file is named with the FULL 8-hex FormID; the master byte in the FormID is always the
        ' load-order byte, so we mask it out and zero-pad the local FormID to 8 hex chars.
        Dim localFormID = npcFormID And &HFFFFFFUI
        Return $"meshes\actors\character\facegendata\facegeom\{pluginName}\{localFormID:X8}.nif".ToLowerInvariant()
    End Function

    Private Iterator Function DetermineFaceGenModes(state As NPCVisualState) As IEnumerable(Of Boolean)
        Yield False
        If HasFaceGenAssets(state) Then Yield True
    End Function

    Private Function ResolvePreviewVariant(previewVariant As PreviewVariantDefinition) As PreviewResolutionResult
        Dim result As New PreviewResolutionResult()
        If previewVariant Is Nothing OrElse previewVariant.State Is Nothing Then Return result
        Dim state = previewVariant.State

        NpcPreviewLog.LogSeparator($"RESOLVE PREVIEW: {previewVariant.DisplayName}")
        NpcPreviewLog.Log($"  FormID={state.FormID:X8} Female={state.IsFemale} Race={state.RaceFormID:X8}")
        NpcPreviewLog.Log($"  SkinFormID={state.SkinFormID:X8} OutfitFormID={state.DefaultOutfitFormID:X8}")
        NpcPreviewLog.Log($"  HeadTexture={state.HeadTextureFormID:X8} HairColor={state.HairColorFormID:X8} FacialHairColor={state.FacialHairColorFormID:X8}")
        NpcPreviewLog.Log($"  HasTextureLighting={state.HasTextureLighting} TextureLightingColor={state.TextureLightingColor}")
        NpcPreviewLog.Log($"  HeadParts({state.HeadPartFormIDs.Count}): {String.Join(", ", state.HeadPartFormIDs.Select(Function(id) id.ToString("X8")))}")
        NpcPreviewLog.Log($"  LoadoutArmor({state.LoadoutArmorFormIDs.Count}): {String.Join(", ", state.LoadoutArmorFormIDs.Select(Function(id) id.ToString("X8")))}")
        NpcPreviewLog.Log($"  PreviewMode={CurrentPreviewMode}")

        result.Warnings.AddRange(previewVariant.Warnings)
        result.SkeletonKey = ResolveSkeletonKey(previewVariant.State, result.Warnings)
        NpcPreviewLog.Log($"  Skeleton={result.SkeletonKey}")

        Dim candidates = CollectMeshCandidates(previewVariant.State, result.Warnings, previewVariant.UseFaceGen)
        NpcPreviewLog.Log($"  Candidates collected: {candidates.Count}")
        For Each c In candidates
            NpcPreviewLog.Log($"    [{c.Kind}] type={c.HeadPartType} slot={c.SlotMask:X8} pri={c.Priority} txst={c.TextureSetFormID:X8} mswp={c.MaterialSwapFormID:X8} solidTint={c.UseSolidTint} bodyTex={c.UsesBodyTexture} colorFID={c.HeadPartColorFormID:X8} key={c.DictKey}")
        Next

        Dim selectedCandidates = SelectWinningCandidates(candidates)
        NpcPreviewLog.Log($"  Selected winners: {selectedCandidates.Count}")
        For Each c In selectedCandidates
            NpcPreviewLog.Log($"    WIN [{c.Kind}] type={c.HeadPartType} slot={c.SlotMask:X8} key={c.DictKey}")
        Next

        Dim loadedNifs As New Dictionary(Of String, Nifcontent_Class_Manolo)(StringComparer.OrdinalIgnoreCase)

        For Each candidate In selectedCandidates
            LoadNifShapes(candidate, previewVariant.State, loadedNifs, result)
        Next

        NpcPreviewLog.Log($"  Total shapes loaded: {result.Shapes.Count}")
        DeduplicateWarnings(result.Warnings)
        For Each w In result.Warnings
            NpcPreviewLog.Log($"  WARNING: {w}")
        Next
        Return result
    End Function

    Private Function ResolveNPCVisualStates(npc As NPC_Data, warnings As List(Of String)) As List(Of NPCVisualState)
        Dim results As New List(Of NPCVisualState)
        If npc Is Nothing Then Return results

        Dim traitBranches = ResolveTraitsBranchesFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings)
        Dim inventoryBranches = ResolveInventoryBranchesFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings)
        Dim modelBranches = ResolveModelAnimationBranchesFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings)

        If traitBranches.Count = 0 Then
            traitBranches.Add(New ResolvedBranch(Of TraitsState) With {.SourceNpcFormID = npc.FormID, .Value = CreateOwnTraitsState(npc)})
        End If
        If inventoryBranches.Count = 0 Then
            inventoryBranches.Add(New ResolvedBranch(Of InventoryState) With {.SourceNpcFormID = npc.FormID, .Value = CreateOwnInventoryState(npc)})
        End If
        If modelBranches.Count = 0 Then
            modelBranches.Add(New ResolvedBranch(Of ModelAnimationState) With {.SourceNpcFormID = npc.FormID, .Value = CreateOwnModelAnimationState(npc)})
        End If

        For Each traitsBranch In traitBranches
            For Each inventoryBranch In inventoryBranches
                For Each modelBranch In modelBranches
                    Dim baseState As New NPCVisualState With {
                        .FormID = npc.FormID,
                        .RootNpcFormID = npc.FormID,
                        .TraitsSourceFormID = traitsBranch.SourceNpcFormID,
                        .InventorySourceFormID = inventoryBranch.SourceNpcFormID,
                        .ModelSourceFormID = modelBranch.SourceNpcFormID,
                        .IsFemale = traitsBranch.Value.IsFemale,
                        .RaceFormID = traitsBranch.Value.RaceFormID,
                        .SkinFormID = traitsBranch.Value.SkinFormID,
                        .DefaultOutfitFormID = inventoryBranch.Value.DefaultOutfitFormID,
                        .SleepOutfitFormID = inventoryBranch.Value.SleepOutfitFormID,
                        .HeadTextureFormID = modelBranch.Value.HeadTextureFormID,
                        .HairColorFormID = modelBranch.Value.HairColorFormID,
                        .FacialHairColorFormID = modelBranch.Value.FacialHairColorFormID,
                        .HasTextureLighting = modelBranch.Value.HasTextureLighting,
                        .TextureLightingColor = modelBranch.Value.TextureLightingColor,
                        .WeightThin = traitsBranch.Value.WeightThin,
                        .WeightMuscular = traitsBranch.Value.WeightMuscular,
                        .WeightFat = traitsBranch.Value.WeightFat
                    }

                    baseState.HeadPartFormIDs.AddRange(modelBranch.Value.HeadPartFormIDs)
                    ApplyRaceFallbacks(baseState)
                    baseState.HeadPartFormIDs = baseState.HeadPartFormIDs.Where(Function(id) id <> 0UI).Distinct().ToList()

                    Dim loadoutVariants = ExpandLoadoutArmorSets(baseState.DefaultOutfitFormID, New HashSet(Of UInteger)(), warnings)
                    If loadoutVariants.Count = 0 Then loadoutVariants.Add(New List(Of UInteger))

                    For Each loadoutArmorIds In loadoutVariants
                        Dim state = CloneVisualState(baseState)
                        state.LoadoutArmorFormIDs.AddRange(loadoutArmorIds.Where(Function(id) id <> 0UI))
                        results.Add(state)
                    Next
                Next
            Next
        Next

        Return DeduplicateVisualStates(results)
    End Function

    Private Function CloneVisualState(state As NPCVisualState) As NPCVisualState
        Dim clone As New NPCVisualState With {
            .FormID = state.FormID,
            .RootNpcFormID = state.RootNpcFormID,
            .TraitsSourceFormID = state.TraitsSourceFormID,
            .InventorySourceFormID = state.InventorySourceFormID,
            .ModelSourceFormID = state.ModelSourceFormID,
            .VariantLabel = state.VariantLabel,
            .IsFemale = state.IsFemale,
            .RaceFormID = state.RaceFormID,
            .SkinFormID = state.SkinFormID,
            .DefaultOutfitFormID = state.DefaultOutfitFormID,
            .SleepOutfitFormID = state.SleepOutfitFormID,
            .HeadTextureFormID = state.HeadTextureFormID,
            .HairColorFormID = state.HairColorFormID,
            .FacialHairColorFormID = state.FacialHairColorFormID,
            .HasTextureLighting = state.HasTextureLighting,
            .TextureLightingColor = state.TextureLightingColor,
            .WeightThin = state.WeightThin,
            .WeightMuscular = state.WeightMuscular,
            .WeightFat = state.WeightFat
        }
        clone.HeadPartFormIDs.AddRange(state.HeadPartFormIDs)
        clone.LoadoutArmorFormIDs.AddRange(state.LoadoutArmorFormIDs)
        Return clone
    End Function

    Private Function BuildVariantDisplayName(variantId As Integer, state As NPCVisualState, useFaceGen As Boolean) As String
        Dim traitsLabel = DescribeNpc(GetParsedNpc(state.TraitsSourceFormID))
        Dim inventoryLabel = DescribeNpc(GetParsedNpc(state.InventorySourceFormID))
        Dim modelLabel = DescribeNpc(GetParsedNpc(state.ModelSourceFormID))
        Dim outfitLabel = If(state.LoadoutArmorFormIDs.Count = 0, "base body", $"{state.LoadoutArmorFormIDs.Count} outfit item(s)")
        Dim faceLabel = If(useFaceGen, "facegen", "records")
        Return $"Variant {variantId:00} | T:{traitsLabel} | I:{inventoryLabel} | M:{modelLabel} | {outfitLabel} | {faceLabel}"
    End Function
    Private Function ResolveNPCPreview(npc As NPC_Data) As PreviewResolutionResult
        Dim result As New PreviewResolutionResult()
        Dim state = ResolveNPCVisualState(npc, result.Warnings)
        If state Is Nothing Then Return result

        result.SkeletonKey = ResolveSkeletonKey(state, result.Warnings)

        Dim candidates = CollectMeshCandidates(state, result.Warnings)
        Dim selectedCandidates = SelectWinningCandidates(candidates)
        Dim loadedNifs As New Dictionary(Of String, Nifcontent_Class_Manolo)(StringComparer.OrdinalIgnoreCase)

        For Each candidate In selectedCandidates
            LoadNifShapes(candidate, state, loadedNifs, result)
        Next

        DeduplicateWarnings(result.Warnings)
        Return result
    End Function

    Private Function ResolveNPCVisualState(npc As NPC_Data, warnings As List(Of String)) As NPCVisualState
        Dim traits = ResolveTraitsStateFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings)
        Dim inventory = ResolveInventoryStateFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings)
        Dim model = ResolveModelAnimationStateFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings)

        If traits Is Nothing Then traits = CreateOwnTraitsState(npc)
        If inventory Is Nothing Then inventory = CreateOwnInventoryState(npc)
        If model Is Nothing Then model = CreateOwnModelAnimationState(npc)

        Dim state As New NPCVisualState With {
            .FormID = npc.FormID,
            .IsFemale = traits.IsFemale,
            .RaceFormID = traits.RaceFormID,
            .SkinFormID = traits.SkinFormID,
            .DefaultOutfitFormID = inventory.DefaultOutfitFormID,
            .SleepOutfitFormID = inventory.SleepOutfitFormID,
            .HeadTextureFormID = model.HeadTextureFormID,
            .HairColorFormID = model.HairColorFormID,
            .FacialHairColorFormID = model.FacialHairColorFormID,
            .HasTextureLighting = model.HasTextureLighting,
            .TextureLightingColor = model.TextureLightingColor,
            .WeightThin = traits.WeightThin,
            .WeightMuscular = traits.WeightMuscular,
            .WeightFat = traits.WeightFat
        }

        state.HeadPartFormIDs.AddRange(model.HeadPartFormIDs)
        ApplyRaceFallbacks(state)
        state.HeadPartFormIDs = state.HeadPartFormIDs.Where(Function(id) id <> 0UI).Distinct().ToList()

        Return state
    End Function

    Private Sub ApplyRaceFallbacks(state As NPCVisualState)
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return

        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return

        Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)
        If state.SkinFormID = 0UI Then
            state.SkinFormID = race.SkinFormID
        End If

        If state.HeadPartFormIDs.Count = 0 Then
            If state.IsFemale Then
                state.HeadPartFormIDs.AddRange(race.FemaleHeadPartFormIDs)
                If state.HeadTextureFormID = 0UI Then state.HeadTextureFormID = If(race.FemaleDefaultFaceTextureFormID <> 0UI, race.FemaleDefaultFaceTextureFormID, race.MaleDefaultFaceTextureFormID)
            Else
                state.HeadPartFormIDs.AddRange(race.MaleHeadPartFormIDs)
                If state.HeadTextureFormID = 0UI Then state.HeadTextureFormID = If(race.MaleDefaultFaceTextureFormID <> 0UI, race.MaleDefaultFaceTextureFormID, race.FemaleDefaultFaceTextureFormID)
            End If
        ElseIf state.HeadTextureFormID = 0UI Then
            If state.IsFemale Then
                state.HeadTextureFormID = If(race.FemaleDefaultFaceTextureFormID <> 0UI, race.FemaleDefaultFaceTextureFormID, race.MaleDefaultFaceTextureFormID)
            Else
                state.HeadTextureFormID = If(race.MaleDefaultFaceTextureFormID <> 0UI, race.MaleDefaultFaceTextureFormID, race.FemaleDefaultFaceTextureFormID)
            End If
        End If
    End Sub

    Private Function ResolveSkeletonKey(state As NPCVisualState, warnings As List(Of String)) As String
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return ""

        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return ""

        Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)
        Dim skeletonPath = If(state.IsFemale, race.FemaleSkeletonPath, race.MaleSkeletonPath)
        If String.IsNullOrWhiteSpace(skeletonPath) Then
            skeletonPath = If(race.MaleSkeletonPath <> "", race.MaleSkeletonPath, race.FemaleSkeletonPath)
        End If

        Dim dictionaryKey = NormalizeDictionaryKeyWithMeshesPrefix(skeletonPath)
        If dictionaryKey = "" Then warnings.Add($"No skeleton path resolved for race {state.RaceFormID:X8}")
        Return dictionaryKey
    End Function

    Private Function ResolveTraitsBranchesFromNPC(formID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As List(Of ResolvedBranch(Of TraitsState))
        Dim npc = GetParsedNpc(formID)
        If npc Is Nothing Then Return New List(Of ResolvedBranch(Of TraitsState))

        Dim ownBranch = New ResolvedBranch(Of TraitsState) With {
            .SourceNpcFormID = formID,
            .Value = CreateOwnTraitsState(npc)
        }

        If visited.Contains(formID) OrElse Not HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.Traits) Then
            Return New List(Of ResolvedBranch(Of TraitsState)) From {ownBranch}
        End If

        visited.Add(formID)
        Dim sourceFormID = ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.Traits)
        Dim branches = ResolveTraitsBranchesFromTemplateSource(sourceFormID, visited, warnings)
        visited.Remove(formID)

        If branches.Count = 0 Then
            warnings.Add($"Traits template unresolved for {DescribeNpc(npc)}")
            Return New List(Of ResolvedBranch(Of TraitsState)) From {ownBranch}
        End If

        Return DistinctBranches(branches)
    End Function

    Private Function ResolveInventoryBranchesFromNPC(formID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As List(Of ResolvedBranch(Of InventoryState))
        Dim npc = GetParsedNpc(formID)
        If npc Is Nothing Then Return New List(Of ResolvedBranch(Of InventoryState))

        Dim ownBranch = New ResolvedBranch(Of InventoryState) With {
            .SourceNpcFormID = formID,
            .Value = CreateOwnInventoryState(npc)
        }

        If visited.Contains(formID) OrElse Not HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.Inventory) Then
            Return New List(Of ResolvedBranch(Of InventoryState)) From {ownBranch}
        End If

        visited.Add(formID)
        Dim sourceFormID = ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.Inventory)
        Dim branches = ResolveInventoryBranchesFromTemplateSource(sourceFormID, visited, warnings)
        visited.Remove(formID)

        If branches.Count = 0 Then
            warnings.Add($"Inventory template unresolved for {DescribeNpc(npc)}")
            Return New List(Of ResolvedBranch(Of InventoryState)) From {ownBranch}
        End If

        Return DistinctBranches(branches)
    End Function

    Private Function ResolveModelAnimationBranchesFromNPC(formID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As List(Of ResolvedBranch(Of ModelAnimationState))
        Dim npc = GetParsedNpc(formID)
        If npc Is Nothing Then Return New List(Of ResolvedBranch(Of ModelAnimationState))

        Dim ownBranch = New ResolvedBranch(Of ModelAnimationState) With {
            .SourceNpcFormID = formID,
            .Value = CreateOwnModelAnimationState(npc)
        }

        If visited.Contains(formID) OrElse Not HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.ModelAnimation) Then
            Return New List(Of ResolvedBranch(Of ModelAnimationState)) From {ownBranch}
        End If

        visited.Add(formID)
        Dim sourceFormID = ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.ModelAnimation)
        Dim branches = ResolveModelAnimationBranchesFromTemplateSource(sourceFormID, visited, warnings)
        visited.Remove(formID)

        If branches.Count = 0 Then
            warnings.Add($"Model/Animation template unresolved for {DescribeNpc(npc)}")
            Return New List(Of ResolvedBranch(Of ModelAnimationState)) From {ownBranch}
        End If

        Return DistinctBranches(branches)
    End Function

    Private Function ResolveTraitsBranchesFromTemplateSource(sourceFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As List(Of ResolvedBranch(Of TraitsState))
        Dim branches As New List(Of ResolvedBranch(Of TraitsState))
        For Each leafNpcFormID In ResolveTemplateNpcLeaves(sourceFormID, visited, warnings)
            branches.AddRange(ResolveTraitsBranchesFromNPC(leafNpcFormID, visited, warnings))
        Next
        Return DistinctBranches(branches)
    End Function

    Private Function ResolveInventoryBranchesFromTemplateSource(sourceFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As List(Of ResolvedBranch(Of InventoryState))
        Dim branches As New List(Of ResolvedBranch(Of InventoryState))
        For Each leafNpcFormID In ResolveTemplateNpcLeaves(sourceFormID, visited, warnings)
            branches.AddRange(ResolveInventoryBranchesFromNPC(leafNpcFormID, visited, warnings))
        Next
        Return DistinctBranches(branches)
    End Function

    Private Function ResolveModelAnimationBranchesFromTemplateSource(sourceFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As List(Of ResolvedBranch(Of ModelAnimationState))
        Dim branches As New List(Of ResolvedBranch(Of ModelAnimationState))
        For Each leafNpcFormID In ResolveTemplateNpcLeaves(sourceFormID, visited, warnings)
            branches.AddRange(ResolveModelAnimationBranchesFromNPC(leafNpcFormID, visited, warnings))
        Next
        Return DistinctBranches(branches)
    End Function

    Private Function ResolveTemplateNpcLeaves(sourceFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As List(Of UInteger)
        Dim result As New List(Of UInteger)
        If sourceFormID = 0UI Then Return result

        Dim sourceRecord = _pluginManager.GetRecord(sourceFormID)
        If sourceRecord Is Nothing Then
            warnings.Add($"Missing template source {sourceFormID:X8}")
            Return result
        End If

        Select Case sourceRecord.Header.Signature
            Case "NPC_"
                result.Add(sourceRecord.Header.FormID)
            Case "LVLN"
                result.AddRange(ExpandLeveledNpcLeaves(sourceRecord.Header.FormID, visited, warnings))
            Case Else
                warnings.Add($"Unsupported template source {sourceRecord.Header.Signature} [{sourceFormID:X8}]")
        End Select

        Return result.Distinct().ToList()
    End Function

    Private Function ExpandLeveledNpcLeaves(lvlnFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As List(Of UInteger)
        Dim result As New List(Of UInteger)
        If lvlnFormID = 0UI Then Return result
        If visited.Contains(lvlnFormID) Then Return result

        Dim lvlnRec = _pluginManager.GetRecord(lvlnFormID)
        If lvlnRec Is Nothing OrElse lvlnRec.Header.Signature <> "LVLN" Then Return result

        visited.Add(lvlnFormID)
        Dim lvln = RecordParsers.ParseLVLN(lvlnRec, _pluginManager)

        For Each entry In lvln.Entries
            If entry.FormID = 0UI Then Continue For
            Dim entryRec = _pluginManager.GetRecord(entry.FormID)
            If entryRec Is Nothing Then Continue For

            Select Case entryRec.Header.Signature
                Case "NPC_"
                    result.Add(entryRec.Header.FormID)
                Case "LVLN"
                    result.AddRange(ExpandLeveledNpcLeaves(entryRec.Header.FormID, visited, warnings))
            End Select
        Next

        visited.Remove(lvlnFormID)
        Return result.Distinct().ToList()
    End Function

    Private Function ExpandLoadoutArmorSets(outfitFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As List(Of List(Of UInteger))
        If outfitFormID = 0UI Then
            Return New List(Of List(Of UInteger)) From {New List(Of UInteger)}
        End If

        Dim outfitRec = _pluginManager.GetRecord(outfitFormID)
        If outfitRec Is Nothing OrElse outfitRec.Header.Signature <> "OTFT" Then
            warnings.Add($"Default outfit {outfitFormID:X8} is missing or not OTFT")
            Return New List(Of List(Of UInteger)) From {New List(Of UInteger)}
        End If

        Dim otft = RecordParsers.ParseOTFT(outfitRec, _pluginManager)
        NpcPreviewLog.Log($"  [OTFT] {otft.EditorID} FID={outfitFormID:X8} items({otft.ItemFormIDs.Count}): {String.Join(", ", otft.ItemFormIDs.Select(Function(id) id.ToString("X8")))}")
        For Each itemFID In otft.ItemFormIDs
            Dim itemRec = _pluginManager.GetRecord(itemFID)
            If itemRec IsNot Nothing Then
                NpcPreviewLog.Log($"    OTFT item {itemFID:X8} sig={itemRec.Header.Signature} edid={itemRec.EditorID}")
            End If
        Next
        Dim results As New List(Of List(Of UInteger)) From {New List(Of UInteger)}

        For Each itemFormID In otft.ItemFormIDs
            Dim branches = ExpandOutfitItemToArmorSets(itemFormID, New HashSet(Of UInteger)(visited), warnings)
            results = CrossJoinArmorSets(results, branches)
        Next

        Return DeduplicateArmorSets(results)
    End Function

    Private Function ExpandOutfitItemToArmorSets(itemFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As List(Of List(Of UInteger))
        If itemFormID = 0UI Then
            Return New List(Of List(Of UInteger)) From {New List(Of UInteger)}
        End If
        If visited.Contains(itemFormID) Then
            Return New List(Of List(Of UInteger)) From {New List(Of UInteger)}
        End If

        Dim rec = _pluginManager.GetRecord(itemFormID)
        If rec Is Nothing Then
            warnings.Add($"Missing outfit item {itemFormID:X8}")
            Return New List(Of List(Of UInteger)) From {New List(Of UInteger)}
        End If

        Select Case rec.Header.Signature
            Case "ARMO"
                visited.Add(itemFormID)
                Dim terminalArmorFormID = ResolveTerminalArmorFormID(itemFormID, New HashSet(Of UInteger)(), warnings)
                If terminalArmorFormID = 0UI Then
                    Return New List(Of List(Of UInteger)) From {New List(Of UInteger)}
                End If
                Return New List(Of List(Of UInteger)) From {New List(Of UInteger) From {terminalArmorFormID}}
            Case "LVLI"
                ' Pass a COPY of visited so the OTFT-level item doesn't block its own LVLI expansion,
                ' but cycles within LVLI?LVLI chains are still caught by ExpandLeveledItemToArmorSets.
                Return ExpandLeveledItemToArmorSets(itemFormID, New HashSet(Of UInteger)(visited), warnings)
            Case Else
                warnings.Add($"Unsupported outfit item {rec.Header.Signature} [{itemFormID:X8}]")
                Return New List(Of List(Of UInteger)) From {New List(Of UInteger)}
        End Select
    End Function

    Private Function ResolveTerminalArmorFormID(armoFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As UInteger
        If armoFormID = 0UI Then Return 0UI
        If visited.Contains(armoFormID) Then Return armoFormID

        Dim armoRec = _pluginManager.GetRecord(armoFormID)
        If armoRec Is Nothing OrElse armoRec.Header.Signature <> "ARMO" Then Return 0UI

        visited.Add(armoFormID)
        Dim armo = RecordParsers.ParseARMO(armoRec, _pluginManager)
        If armo.TemplateArmorFormID <> 0UI Then
            Dim resolved = ResolveTerminalArmorFormID(armo.TemplateArmorFormID, visited, warnings)
            If resolved <> 0UI Then Return resolved
        End If

        Return armoFormID
    End Function

    Private Function ExpandLeveledItemToArmorSets(lvliFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As List(Of List(Of UInteger))
        Dim results As New List(Of List(Of UInteger))
        If lvliFormID = 0UI Then Return results
        If visited.Contains(lvliFormID) Then Return results

        Dim lvliRec = _pluginManager.GetRecord(lvliFormID)
        If lvliRec Is Nothing OrElse lvliRec.Header.Signature <> "LVLI" Then Return results

        visited.Add(lvliFormID)
        Dim lvli = RecordParsers.ParseLVLI(lvliRec, _pluginManager)
        Dim perEntryBranches As New List(Of List(Of List(Of UInteger)))

        For Each entry In lvli.Entries
            Dim options = ExpandOutfitItemToArmorSets(entry.FormID, New HashSet(Of UInteger)(visited), warnings)
            If entry.Count > 1US AndAlso lvli.CalculateEachItemInCount Then
                options = ExpandRepeatedArmorSets(options, CInt(entry.Count))
            End If
            If entry.ChanceNone > 0 Then
                options.Add(New List(Of UInteger))
            End If
            perEntryBranches.Add(DeduplicateArmorSets(options))
        Next

        If lvli.UseAll Then
            results.Add(New List(Of UInteger))
            For Each branchSet In perEntryBranches
                results = CrossJoinArmorSets(results, branchSet)
            Next
        Else
            For Each branchSet In perEntryBranches
                results.AddRange(branchSet)
            Next
        End If

        If lvli.ChanceNone > 0 OrElse results.Count = 0 Then
            results.Add(New List(Of UInteger))
        End If

        visited.Remove(lvliFormID)
        Return DeduplicateArmorSets(results)
    End Function

    Private Function ExpandRepeatedArmorSets(options As List(Of List(Of UInteger)), repeatCount As Integer) As List(Of List(Of UInteger))
        If repeatCount <= 1 Then Return DeduplicateArmorSets(options)

        Dim results As New List(Of List(Of UInteger)) From {New List(Of UInteger)}
        For i = 1 To repeatCount
            results = CrossJoinArmorSets(results, options)
        Next
        Return DeduplicateArmorSets(results)
    End Function

    Private Function CrossJoinArmorSets(leftSets As List(Of List(Of UInteger)), rightSets As List(Of List(Of UInteger))) As List(Of List(Of UInteger))
        Dim results As New List(Of List(Of UInteger))
        If leftSets Is Nothing OrElse leftSets.Count = 0 Then leftSets = New List(Of List(Of UInteger)) From {New List(Of UInteger)}
        If rightSets Is Nothing OrElse rightSets.Count = 0 Then rightSets = New List(Of List(Of UInteger)) From {New List(Of UInteger)}

        For Each leftSet In leftSets
            For Each rightSet In rightSets
                Dim combined As New List(Of UInteger)
                combined.AddRange(leftSet)
                combined.AddRange(rightSet)
                results.Add(combined.Where(Function(id) id <> 0UI).Distinct().ToList())
            Next
        Next

        Return DeduplicateArmorSets(results)
    End Function

    Private Function DeduplicateArmorSets(sets As IEnumerable(Of List(Of UInteger))) As List(Of List(Of UInteger))
        Dim results As New List(Of List(Of UInteger))
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        For Each current In sets
            Dim normalized = If(current, New List(Of UInteger)).Where(Function(id) id <> 0UI).Distinct().OrderBy(Function(id) id).ToList()
            Dim key = String.Join("|", normalized.Select(Function(id) id.ToString("X8")))
            If seen.Add(key) Then results.Add(normalized)
        Next

        Return results
    End Function

    Private Function DeduplicateVisualStates(states As IEnumerable(Of NPCVisualState)) As List(Of NPCVisualState)
        Dim results As New List(Of NPCVisualState)
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        For Each state In states
            Dim key = String.Join("|", {
                state.RootNpcFormID.ToString("X8"),
                state.TraitsSourceFormID.ToString("X8"),
                state.InventorySourceFormID.ToString("X8"),
                state.ModelSourceFormID.ToString("X8"),
                If(state.IsFemale, "F", "M"),
                state.RaceFormID.ToString("X8"),
                state.SkinFormID.ToString("X8"),
                String.Join(",", state.HeadPartFormIDs.OrderBy(Function(id) id).Select(Function(id) id.ToString("X8"))),
                String.Join(",", state.LoadoutArmorFormIDs.OrderBy(Function(id) id).Select(Function(id) id.ToString("X8")))
            })
            If seen.Add(key) Then results.Add(state)
        Next

        Return results
    End Function

    Private Function DistinctBranches(Of T)(branches As IEnumerable(Of ResolvedBranch(Of T))) As List(Of ResolvedBranch(Of T))
        Dim results As New List(Of ResolvedBranch(Of T))
        Dim seen As New HashSet(Of UInteger)

        For Each branch In branches
            If branch Is Nothing Then Continue For
            If seen.Add(branch.SourceNpcFormID) Then results.Add(branch)
        Next

        Return results
    End Function
    Private Function ResolveTraitsStateFromNPC(formID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As TraitsState
        Dim npc = GetParsedNpc(formID)
        If npc Is Nothing Then Return Nothing

        Dim own = CreateOwnTraitsState(npc)
        If visited.Contains(formID) Then Return own

        NpcPreviewLog.Log($"  [TRAITS-CHAIN] {npc.EditorID} [{formID:X8}] flags={npc.TemplateFlags:X4} hasTraitsFlag={HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.Traits)} Female={npc.IsFemale}")

        If Not HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.Traits) Then
            NpcPreviewLog.Log($"  [TRAITS-CHAIN] ? OWN traits (Female={npc.IsFemale})")
            Return own
        End If

        visited.Add(formID)
        Dim sourceFormID = ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.Traits)
        Dim sourceRec = _pluginManager.GetRecord(sourceFormID)
        NpcPreviewLog.Log($"  [TRAITS-CHAIN] ? source {sourceFormID:X8} sig={sourceRec?.Header.Signature} edid={sourceRec?.EditorID}")

        Dim resolved = ResolveTraitsStateFromTemplateSource(sourceFormID, visited, warnings)
        visited.Remove(formID)

        If resolved IsNot Nothing Then Return resolved

        warnings.Add($"Traits template unresolved for {DescribeNpc(npc)}")
        Return own
    End Function

    Private Function ResolveInventoryStateFromNPC(formID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As InventoryState
        Dim npc = GetParsedNpc(formID)
        If npc Is Nothing Then Return Nothing

        Dim own = CreateOwnInventoryState(npc)
        If visited.Contains(formID) Then Return own
        If Not HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.Inventory) Then Return own

        visited.Add(formID)
        Dim sourceFormID = ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.Inventory)
        Dim resolved = ResolveInventoryStateFromTemplateSource(sourceFormID, visited, warnings)
        visited.Remove(formID)

        If resolved IsNot Nothing Then Return resolved

        warnings.Add($"Inventory template unresolved for {DescribeNpc(npc)}")
        Return own
    End Function

    Private Function ResolveModelAnimationStateFromNPC(formID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As ModelAnimationState
        Dim npc = GetParsedNpc(formID)
        If npc Is Nothing Then Return Nothing

        Dim own = CreateOwnModelAnimationState(npc)
        If visited.Contains(formID) Then Return own
        If Not HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.ModelAnimation) Then Return own

        visited.Add(formID)
        Dim sourceFormID = ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.ModelAnimation)
        Dim resolved = ResolveModelAnimationStateFromTemplateSource(sourceFormID, visited, warnings)
        visited.Remove(formID)

        If resolved IsNot Nothing Then Return resolved

        warnings.Add($"Model/Animation template unresolved for {DescribeNpc(npc)}")
        Return own
    End Function

    Private Function ResolveTraitsStateFromTemplateSource(sourceFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As TraitsState
        Dim sourceRecord = ResolveTemplateSourceRecord(sourceFormID, "Traits", visited, warnings)
        If sourceRecord Is Nothing Then Return Nothing
        Return ResolveTraitsStateFromNPC(sourceRecord.Header.FormID, visited, warnings)
    End Function

    Private Function ResolveInventoryStateFromTemplateSource(sourceFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As InventoryState
        Dim sourceRecord = ResolveTemplateSourceRecord(sourceFormID, "Inventory", visited, warnings)
        If sourceRecord Is Nothing Then Return Nothing
        Return ResolveInventoryStateFromNPC(sourceRecord.Header.FormID, visited, warnings)
    End Function

    Private Function ResolveModelAnimationStateFromTemplateSource(sourceFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As ModelAnimationState
        Dim sourceRecord = ResolveTemplateSourceRecord(sourceFormID, "Model/Animation", visited, warnings)
        If sourceRecord Is Nothing Then Return Nothing
        Return ResolveModelAnimationStateFromNPC(sourceRecord.Header.FormID, visited, warnings)
    End Function

    Private Function ResolveTemplateSourceRecord(sourceFormID As UInteger, categoryName As String, visited As HashSet(Of UInteger), warnings As List(Of String)) As PluginRecord
        If sourceFormID = 0UI Then Return Nothing

        Dim sourceRecord = _pluginManager.GetRecord(sourceFormID)
        If sourceRecord Is Nothing Then
            warnings.Add($"Missing {categoryName} template source {sourceFormID:X8}")
            Return Nothing
        End If

        Select Case sourceRecord.Header.Signature
            Case "NPC_"
                Return sourceRecord
            Case "LVLN"
                Dim resolvedFormID = ResolveSingleLeveledTemplate(sourceRecord, warnings)
                If resolvedFormID = 0UI Then Return Nothing
                If visited.Contains(resolvedFormID) Then Return Nothing
                Return ResolveTemplateSourceRecord(resolvedFormID, categoryName, visited, warnings)
            Case Else
                warnings.Add($"Unsupported {categoryName} template source {sourceRecord.Header.Signature} [{sourceFormID:X8}]")
                Return Nothing
        End Select
    End Function

    ''' <summary>Pick a random leaf NPC from a LVLN, using Count as weight, recursing into nested LVLNs.
    ''' Ignores Level requirements and ChanceNone for NPC leveled lists.</summary>
    Private Function PickWeightedRandomFromLVLN(lvlnFormID As UInteger, visited As HashSet(Of UInteger)) As UInteger
        If lvlnFormID = 0UI OrElse visited.Contains(lvlnFormID) Then Return 0UI
        visited.Add(lvlnFormID)

        Dim lvln As LVLN_Data = Nothing
        If Not _lvlnDataCache.TryGetValue(lvlnFormID, lvln) Then
            Dim lvlnRec = _pluginManager.GetRecord(lvlnFormID)
            If lvlnRec Is Nothing OrElse lvlnRec.Header.Signature <> "LVLN" Then Return 0UI
            lvln = RecordParsers.ParseLVLN(lvlnRec, _pluginManager)
        End If

        ' Build weighted list of leaf NPC FormIDs: each entry contributes Count copies
        Dim weightedLeaves As New List(Of UInteger)()

        For Each entry In lvln.Entries
            If entry.FormID = 0UI Then Continue For
            Dim entryRec = _pluginManager.GetRecord(entry.FormID)
            If entryRec Is Nothing Then Continue For

            Dim weight = Math.Max(CInt(entry.Count), 1)

            Select Case entryRec.Header.Signature
                Case "NPC_"
                    For i = 0 To weight - 1
                        weightedLeaves.Add(entry.FormID)
                    Next
                Case "LVLN"
                    ' Recurse into nested LVLN: pick from sub-list, weighted by this entry's Count
                    For i = 0 To weight - 1
                        Dim subPick = PickWeightedRandomFromLVLN(entry.FormID, New HashSet(Of UInteger)(visited))
                        If subPick <> 0UI Then weightedLeaves.Add(subPick)
                    Next
            End Select
        Next

        If weightedLeaves.Count = 0 Then Return 0UI

        ' Apply gender filter if set
        Dim genderFilter = CurrentGenderFilter
        If genderFilter <> GenderFilterMode.Random Then
            Dim filtered = weightedLeaves.Where(Function(fid)
                                                    Dim npc As NPC_Data = Nothing
                                                    If _npcByIdCache.TryGetValue(fid, npc) Then
                                                        Return If(genderFilter = GenderFilterMode.Female, npc.IsFemale, Not npc.IsFemale)
                                                    End If
                                                    Dim npcRec = _pluginManager.GetRecord(fid)
                                                    If npcRec Is Nothing OrElse npcRec.Header.Signature <> "NPC_" Then Return True
                                                    Dim parsed = RecordParsers.ParseNPC(npcRec, "", _pluginManager)
                                                    Return If(genderFilter = GenderFilterMode.Female, parsed.IsFemale, Not parsed.IsFemale)
                                                End Function).ToList()
            If filtered.Count > 0 Then weightedLeaves = filtered
        End If

        Dim picked = weightedLeaves(_rng.Next(weightedLeaves.Count))
        NpcPreviewLog.Log($"  [LVLN] {lvln.EditorID} [{lvlnFormID:X8}] picked {picked:X8} from {weightedLeaves.Count} weighted entries (gender={genderFilter})")
        Return picked
    End Function

    ''' <summary>Pick a single NPC from a LVLN for template resolution. Uses Count as weight.
    ''' Results are cached per NPC resolution to ensure consistent picks across categories.</summary>
    Private Function ResolveSingleLeveledTemplate(lvlnRec As PluginRecord, warnings As List(Of String)) As UInteger
        Dim lvlnFormID = lvlnRec.Header.FormID

        ' Check cache first — same LVLN must return same pick within one NPC resolution
        If _lvlnPickCache IsNot Nothing Then
            Dim cached As UInteger = 0UI
            If _lvlnPickCache.TryGetValue(lvlnFormID, cached) Then
                NpcPreviewLog.Log($"  [LVLN] {lvlnRec.EditorID} [{lvlnFormID:X8}] ? cached pick {cached:X8}")
                Return cached
            End If
        End If

        Dim picked = PickWeightedRandomFromLVLN(lvlnFormID, New HashSet(Of UInteger)())

        If picked = 0UI Then
            warnings.Add($"Leveled template {DescribeRecord(lvlnRec)} has no usable entries")
            Return 0UI
        End If

        If _lvlnPickCache IsNot Nothing Then _lvlnPickCache(lvlnFormID) = picked
        NpcPreviewLog.Log($"  [LVLN-TMPL] {lvlnRec.EditorID} [{lvlnFormID:X8}] resolved to {picked:X8}")
        Return picked
    End Function

    Private Function CollectMeshCandidates(state As NPCVisualState, warnings As List(Of String), Optional useFaceGen As Boolean = False) As List(Of MeshCandidate)
        Dim candidates As New List(Of MeshCandidate)
        Dim order As Integer = 0
        Dim mode = CurrentPreviewMode

        ' In OnlyFace mode, skip body skin and outfit meshes entirely
        If mode = PreviewMode.FullCharacter Then
            If state.SkinFormID <> 0UI Then
                CollectArmoCandidates(state.SkinFormID, state, MeshCandidateKind.Skin, candidates, order, warnings)
            End If

            ' Use pre-resolved LoadoutArmorFormIDs (already expanded from LVLI).
            ' These are the final ARMO FormIDs for this specific variant.
            If state.LoadoutArmorFormIDs.Count > 0 Then
                NpcPreviewLog.Log($"  [OUTFIT] Using resolved LoadoutArmorFormIDs({state.LoadoutArmorFormIDs.Count}): {String.Join(", ", state.LoadoutArmorFormIDs.Select(Function(id) id.ToString("X8")))}")
                For Each armoFormID In state.LoadoutArmorFormIDs
                    CollectArmoCandidates(armoFormID, state, MeshCandidateKind.Outfit, candidates, order, warnings)
                Next
            ElseIf state.DefaultOutfitFormID <> 0UI Then
                ' Fallback: read OTFT directly (for NPCs without leveled expansion)
                Dim outfitRec = _pluginManager.GetRecord(state.DefaultOutfitFormID)
                If outfitRec Is Nothing OrElse outfitRec.Header.Signature <> "OTFT" Then
                    warnings.Add($"Default outfit {state.DefaultOutfitFormID:X8} is missing or not OTFT")
                Else
                    Dim outfit = RecordParsers.ParseOTFT(outfitRec, _pluginManager)
                    NpcPreviewLog.Log($"  [OUTFIT] Fallback OTFT read: {outfit.EditorID} items({outfit.ItemFormIDs.Count})")
                    For Each itemFormID In outfit.ItemFormIDs
                        CollectArmoCandidates(itemFormID, state, MeshCandidateKind.Outfit, candidates, order, warnings)
                    Next
                End If
            End If
        End If

        CollectHeadPartCandidates(state.HeadPartFormIDs, New HashSet(Of UInteger)(), candidates, order, warnings)

        Return candidates
    End Function

    Private Sub CollectArmoCandidates(armoFormID As UInteger,
                                      state As NPCVisualState,
                                      kind As MeshCandidateKind,
                                      candidates As List(Of MeshCandidate),
                                      ByRef order As Integer,
                                      warnings As List(Of String))
        Dim armoRec = _pluginManager.GetRecord(armoFormID)
        If armoRec Is Nothing OrElse armoRec.Header.Signature <> "ARMO" Then Return

        Dim armo = RecordParsers.ParseARMO(armoRec, _pluginManager)
        NpcPreviewLog.Log($"  [ARMO] {armo.EditorID} FID={armoFormID:X8} kind={kind} race={armo.RaceFormID:X8} slot={armo.SlotMask:X8} addons={armo.ArmorAddonFormIDs.Count}")
        If Not MatchesRace(armo.RaceFormID, state.RaceFormID) Then
            NpcPreviewLog.Log($"    SKIPPED: race mismatch (npc={state.RaceFormID:X8})")
            Return
        End If

        For Each armaFormID In armo.ArmorAddonFormIDs
            Dim armaRec = _pluginManager.GetRecord(armaFormID)
            If armaRec Is Nothing OrElse armaRec.Header.Signature <> "ARMA" Then Continue For

            Dim arma = RecordParsers.ParseARMA(armaRec, _pluginManager)
            If Not ArmorAddonMatchesRace(arma, state.RaceFormID) Then
                NpcPreviewLog.Log($"    [ARMA] {arma.EditorID} FID={armaFormID:X8} SKIPPED: race mismatch")
                Continue For
            End If
            NpcPreviewLog.Log($"    [ARMA] {arma.EditorID} FID={armaFormID:X8} slot={arma.SlotMask:X8} maleMesh={arma.MaleMeshPath} femaleMesh={arma.FemaleMeshPath} maleTxst={arma.MaleSkinTextureFormID:X8} femaleTxst={arma.FemaleSkinTextureFormID:X8} maleMswp={arma.MaleMaterialSwapFormID:X8} femaleMswp={arma.FemaleMaterialSwapFormID:X8}")

            Dim meshPath = If(state.IsFemale, arma.FemaleMeshPath, arma.MaleMeshPath)
            If meshPath = "" Then meshPath = If(arma.MaleMeshPath <> "", arma.MaleMeshPath, arma.FemaleMeshPath)
            If meshPath = "" Then Continue For

            candidates.Add(New MeshCandidate With {
                .DictKey = NormalizeDictionaryKeyWithMeshesPrefix(meshPath),
                .SlotMask = If(arma.SlotMask <> 0UI, arma.SlotMask, armo.SlotMask),
                .Priority = If(state.IsFemale, arma.FemalePriority, arma.MalePriority),
                .Kind = kind,
                .SourceFormID = armoFormID,
                .ArmorAddonFormID = armaFormID,
                .TextureSetFormID = If(state.IsFemale,
                                       If(arma.FemaleSkinTextureFormID <> 0UI, arma.FemaleSkinTextureFormID, arma.MaleSkinTextureFormID),
                                       If(arma.MaleSkinTextureFormID <> 0UI, arma.MaleSkinTextureFormID, arma.FemaleSkinTextureFormID)),
                .MaterialSwapFormID = If(state.IsFemale,
                                          If(arma.FemaleMaterialSwapFormID <> 0UI, arma.FemaleMaterialSwapFormID, arma.MaleMaterialSwapFormID),
                                          If(arma.MaleMaterialSwapFormID <> 0UI, arma.MaleMaterialSwapFormID, arma.FemaleMaterialSwapFormID)),
                .Order = order
            })

            order += 1
        Next
    End Sub

    Private Sub CollectHeadPartCandidates(headPartFormIDs As IEnumerable(Of UInteger),
                                          visited As HashSet(Of UInteger),
                                          candidates As List(Of MeshCandidate),
                                          ByRef order As Integer,
                                          warnings As List(Of String))
        For Each hdptFormID In headPartFormIDs.Where(Function(id) id <> 0UI)
            CollectHeadPartCandidate(hdptFormID, visited, candidates, order, warnings, -1)
        Next
    End Sub

    ''' <param name="parentPartType">The parent HDPT's PartType, or -1 if this is a top-level head part.
    ''' Extra parts inherit the parent's type for color/tint purposes (e.g. hair extras get hair color).</param>
    Private Sub CollectHeadPartCandidate(hdptFormID As UInteger,
                                         visited As HashSet(Of UInteger),
                                         candidates As List(Of MeshCandidate),
                                         ByRef order As Integer,
                                         warnings As List(Of String),
                                         parentPartType As Integer)
        If hdptFormID = 0UI Then Return
        If visited.Contains(hdptFormID) Then Return
        visited.Add(hdptFormID)

        Dim hdptRec = _pluginManager.GetRecord(hdptFormID)
        If hdptRec Is Nothing OrElse hdptRec.Header.Signature <> "HDPT" Then Return

        Dim hdpt = RecordParsers.ParseHDPT(hdptRec, _pluginManager)

        ' Extra parts (type=0/Misc) inherit the parent's type for color treatment.
        ' E.g. a hair extra part mesh needs the same hair palette remap as the main hair.
        Dim effectivePartType = hdpt.PartType
        If parentPartType >= 0 AndAlso hdpt.PartType = 0 Then
            effectivePartType = parentPartType
        End If

        NpcPreviewLog.Log($"  [HDPT] {hdpt.EditorID} FID={hdptFormID:X8} type={hdpt.PartType} effectiveType={effectivePartType} mesh={hdpt.MeshPath} txst={hdpt.TextureSetFormID:X8} color={hdpt.ColorFormID:X8} flags={hdpt.Flags:X2} bodyTex={hdpt.UsesBodyTexture} extras={hdpt.ExtraPartFormIDs.Count} parent={parentPartType} raceTri={hdpt.RaceMorphTriPath} chargenTri={hdpt.ChargenMorphTriPath}")
        If hdpt.MeshPath <> "" Then
            ' Redirect face-region meshes to their _faceBones.nif variant when available.
            ' The _faceBones variants are rigged to actual face bones (Jaw, LipUpper_L, Cheek_R, etc)
            ' instead of only body bones, enabling FMRS bone transforms to deform the mesh dynamically.
            Dim dictKey = NormalizeDictionaryKeyWithMeshesPrefix(hdpt.MeshPath)
            Dim faceBonesKey = TryGetFaceBonesVariant(dictKey, effectivePartType)
            If faceBonesKey <> "" Then
                NpcPreviewLog.Log($"  [FACEBONES-REDIRECT] {dictKey} ? {faceBonesKey}")
                dictKey = faceBonesKey
            End If

            candidates.Add(New MeshCandidate With {
                .DictKey = dictKey,
                .SlotMask = 0UI,
                .Priority = 0,
                .Kind = MeshCandidateKind.HeadPart,
                .HeadPartType = effectivePartType,
                .HeadPartColorFormID = hdpt.ColorFormID,
                .TextureSetFormID = hdpt.TextureSetFormID,
                .UseSolidTint = (hdpt.Flags And HeadPartFlagUseSolidTint) <> 0,
                .UsesBodyTexture = hdpt.UsesBodyTexture,
                .Order = order,
                .RaceMorphTriPath = hdpt.RaceMorphTriPath,
                .ChargenMorphTriPath = hdpt.ChargenMorphTriPath
            })
            order += 1
        End If

        ' Pass the effective type down so nested extras also inherit
        Dim childParentType = If(effectivePartType <> 0, effectivePartType, parentPartType)
        For Each extraPartFormID In hdpt.ExtraPartFormIDs
            CollectHeadPartCandidate(extraPartFormID, visited, candidates, order, warnings, childParentType)
        Next
    End Sub

    ' Biped object slot bits (verified from wbDefinitionsFO4.pas:3745 wbBipedObjectFlags).
    ' Slot index = bit position + 30, so bit 0 = slot 30, bit 2 = slot 32, bit 16 = slot 46.
    ' Only the bits we actually use for head part occlusion are defined; body / hand slots
    ' (33/34/35) are handled implicitly by the "outfit wins over skin on same slot" loop in
    ' SelectWinningCandidates, no constants needed there.
    Private Const SlotBitHairTop As UInteger = &H1UI         ' Slot 30 - Hair Top      (sombreros, gorros, cualquier headwear)
    Private Const SlotBitHairLong As UInteger = &H2UI        ' Slot 31 - Hair Long     (cascos que cubren el largo del pelo)
    Private Const SlotBitFaceGenHead As UInteger = &H4UI     ' Slot 32 - FaceGen Head  (casco integral / vault helmet — cubre LA CARA entera)
    Private Const SlotBitBeard As UInteger = &H40000UI       ' Slot 48 - Beard         (algo equipable que pisa la zona barba)
    Private Const SlotBitMouth As UInteger = &H80000UI       ' Slot 49 - Mouth         (bandana, máscara quirúrgica, gas mask boca)

    Private Function SelectWinningCandidates(candidates As List(Of MeshCandidate)) As List(Of MeshCandidate)
        Dim selected As New List(Of MeshCandidate)

        ' First pass: resolve slotted candidates (outfit wins over skin on same slot)
        Dim occupiedSlots As UInteger = 0UI
        Dim slottedCandidates = candidates.Where(Function(c) c.SlotMask <> 0UI).
            OrderByDescending(Function(c) CandidateKindRank(c.Kind)).
            ThenByDescending(Function(c) c.Priority).
            ThenBy(Function(c) c.Order)

        For Each candidate In slottedCandidates
            If (candidate.SlotMask And occupiedSlots) <> 0UI Then Continue For
            occupiedSlots = occupiedSlots Or candidate.SlotMask
            selected.Add(candidate)
        Next

        ' Third pass: add slotless (head parts), hiding based on occupied biped slots.
        '
        ' Slot semantics (verified wbDefinitionsFO4.pas:3745):
        '   30 HairTop      — every hat or helmet (the slot every headwear takes)
        '   31 HairLong     — some helmets that wrap longer hair
        '   32 FaceGenHead  — full helmet that covers the entire face (vault helmet, gas mask)
        '   46 Headband     — bandana / hairband on the forehead, does NOT cover face
        '   48 Beard        — equipable beard slot (rare; covers the beard area when used)
        '   49 Mouth        — bandana / surgical mask / mostacho replacement that covers mouth+beard
        '
        ' Occlusion matrix (head parts of type 0/1 and "extra parts" never occlude — only the
        ' four slotless visual layers below apply):
        '   3 Hair          : hidden by HairTop or HairLong or FaceGenHead   (any headwear)
        '   4 FacialHair    : hidden by FaceGenHead or Beard or Mouth        (covers the beard region)
        '   6 Eyebrows      : hidden by FaceGenHead only                     (full helmet covers brows)
        '   9 HeadRear      : hidden by HairTop AND (HairLong or FaceGenHead) (full hood wrap)
        Dim hasHairTop As Boolean = (occupiedSlots And SlotBitHairTop) <> 0UI
        Dim hasHairLong As Boolean = (occupiedSlots And SlotBitHairLong) <> 0UI
        Dim hasFaceGenHead As Boolean = (occupiedSlots And SlotBitFaceGenHead) <> 0UI
        Dim hasBeard As Boolean = (occupiedSlots And SlotBitBeard) <> 0UI
        Dim hasMouth As Boolean = (occupiedSlots And SlotBitMouth) <> 0UI

        For Each slotlessCandidate In candidates.Where(Function(c) c.SlotMask = 0UI).OrderBy(Function(c) c.Order)
            If slotlessCandidate.Kind = MeshCandidateKind.HeadPart Then
                Select Case slotlessCandidate.HeadPartType
                    Case HeadPartTypeHair
                        If hasHairTop OrElse hasHairLong OrElse hasFaceGenHead Then Continue For
                    Case HeadPartTypeFacialHair
                        If hasFaceGenHead OrElse hasBeard OrElse hasMouth Then Continue For
                    Case 6 ' Eyebrows
                        If hasFaceGenHead Then Continue For
                    Case 9 ' Head Rear
                        If hasHairTop AndAlso (hasHairLong OrElse hasFaceGenHead) Then Continue For
                End Select
            End If
            selected.Add(slotlessCandidate)
        Next

        Return selected.OrderBy(Function(c) c.Order).ToList()
    End Function

    Private Shared Function CandidateKindRank(kind As MeshCandidateKind) As Integer
        Select Case kind
            Case MeshCandidateKind.Outfit
                Return 2
            Case MeshCandidateKind.Skin
                Return 1
            Case Else
                Return 0
        End Select
    End Function

    Private Shared Function MatchesRace(recordRaceFormID As UInteger, npcRaceFormID As UInteger) As Boolean
        If recordRaceFormID = 0UI OrElse npcRaceFormID = 0UI Then Return True
        Return recordRaceFormID = npcRaceFormID
    End Function

    Private Shared Function ArmorAddonMatchesRace(arma As ARMA_Data, npcRaceFormID As UInteger) As Boolean
        If npcRaceFormID = 0UI Then Return True
        If arma.RaceFormID = 0UI Then Return True
        If arma.RaceFormID = npcRaceFormID Then Return True
        Return arma.AdditionalRaces.Contains(npcRaceFormID)
    End Function

    Private Sub LoadNifShapes(candidate As MeshCandidate, state As NPCVisualState, loadedNifs As Dictionary(Of String, Nifcontent_Class_Manolo), result As PreviewResolutionResult)
        Dim dictKey = NormalizeDictionaryKeyWithMeshesPrefix(candidate.DictKey)
        If dictKey = "" Then Return
        If loadedNifs.ContainsKey(dictKey) Then Return

        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(dictKey, loc) Then Return

        Try
            Dim bytes = loc.GetBytes()
            If bytes Is Nothing OrElse bytes.Length = 0 Then Return

            Dim nif As New Nifcontent_Class_Manolo()
            nif.Load_Manolo(bytes)
            loadedNifs(dictKey) = nif

            Dim shapes = NifRenderableShape.FromNif(nif)
            ApplyShapeMaterialOverrides(candidate, state, shapes)

            ' Track shape -> dict key for TRI lookup, plus explicit HDPT TRI paths if present.
            For Each shape In shapes
                result.MeshDictKeys(shape) = dictKey
                If candidate.Kind = MeshCandidateKind.HeadPart Then
                    If Not String.IsNullOrEmpty(candidate.ChargenMorphTriPath) Then
                        result.ShapeChargenTriPaths(shape) = candidate.ChargenMorphTriPath
                    End If
                    If Not String.IsNullOrEmpty(candidate.RaceMorphTriPath) Then
                        result.ShapeRaceMorphTriPaths(shape) = candidate.RaceMorphTriPath
                    End If
                End If
            Next

            result.Shapes.AddRange(shapes)
        Catch ex As Exception
            NpcPreviewLog.Log($"[NIF] FAILED to load {dictKey}: {ex.Message}")
        End Try
    End Sub

    Private Sub ApplyShapeMaterialOverrides(candidate As MeshCandidate, state As NPCVisualState, shapes As IEnumerable(Of IRenderableShape))
        If shapes Is Nothing Then Return

        NpcPreviewLog.Log($"  [MAT-OVERRIDE] kind={candidate?.Kind} headPartType={candidate?.HeadPartType} key={candidate?.DictKey}")

        ' Apply Material Swap (MSWP) first - replaces entire materials before other overrides
        If candidate IsNot Nothing AndAlso candidate.MaterialSwapFormID <> 0UI Then
            NpcPreviewLog.Log($"    MSWP={candidate.MaterialSwapFormID:X8}")
            ApplyMaterialSwap(candidate.MaterialSwapFormID, shapes)
        End If

        Dim solidTintColor = ResolveHeadPartSolidTintColor(candidate)
        Dim hairTintColor = ResolveHairTintColor(candidate, state, solidTintColor)
        Dim skinTintColor = ResolveSkinTintColor(candidate, state, solidTintColor)
        Dim textureSet = ResolveTextureSet(candidate, state)
        Dim hairPaletteTexture As String = ""
        Dim hairPaletteScale As Single = 0.0F
        Dim hasHairPaletteRemap = TryResolveHairPaletteRemap(candidate, state, hairPaletteTexture, hairPaletteScale)

        NpcPreviewLog.Log($"    solidTint={solidTintColor} hairTint={hairTintColor} skinTint={skinTintColor}")
        NpcPreviewLog.Log($"    textureSet={If(textureSet IsNot Nothing, textureSet.EditorID, "none")} txstFID={If(textureSet IsNot Nothing, textureSet.FormID.ToString("X8"), "0")}")
        NpcPreviewLog.Log($"    hairPaletteRemap={hasHairPaletteRemap} paletteTexture={hairPaletteTexture} paletteScale={hairPaletteScale}")

        For Each shape In shapes
            EnsureShapeMaterialResolved(shape)

            Dim relatedMaterial = shape.ShapeMaterial
            If relatedMaterial Is Nothing Then
                NpcPreviewLog.Log($"    shape={shape.ShapeName}: NO material")
                Continue For
            End If

            ApplyTextureSetOverrides(textureSet, relatedMaterial)

            Dim material = relatedMaterial.material
            If material Is Nothing Then
                NpcPreviewLog.Log($"    shape={shape.ShapeName}: material.material is Nothing")
                Continue For
            End If

            Dim appliedAction = "none"
            NpcPreviewLog.Log($"      [BEFORE-OVERRIDE] g2p={material.GrayscaleToPaletteColor} g2pScale={material.GrayscaleToPaletteScale} greyscaleTex={material.GreyscaleTexture} hair={material.Hair} specEnabled={material.SpecularEnabled} smoothSpecTex={material.SmoothSpecTexture}")
            If hasHairPaletteRemap AndAlso IsHairHeadPart(candidate) AndAlso material.GrayscaleToPaletteColor Then
                ' Material already uses grayscale-to-palette: override scale with CLFM RemappingIndex
                material.GrayscaleToPaletteScale = hairPaletteScale
                If hairPaletteTexture <> "" Then
                    material.GreyscaleTexture = hairPaletteTexture
                End If
                appliedAction = $"hairPaletteRemap(scale={hairPaletteScale})"
            ElseIf material.Hair OrElse material.GrayscaleToPaletteColor Then
                ' Material is hair or uses grayscale-to-palette: try palette remap first, tint as fallback.
                ' This handles typed hair parts, hairlines, and extra parts with Hair material.
                Dim didPalette = False
                If state IsNot Nothing AndAlso state.HairColorFormID <> 0UI Then
                    Dim clfm = ResolveColorFormData(state.HairColorFormID)
                    If clfm IsNot Nothing AndAlso clfm.HasRemappingIndex Then
                        Dim palTex = ResolveRaceHairLookupTexture(state)
                        If palTex <> "" Then
                            material.GrayscaleToPaletteColor = True
                            material.GrayscaleToPaletteScale = clfm.RemappingIndex
                            material.GreyscaleTexture = palTex
                            appliedAction = $"hairPaletteRemap(scale={clfm.RemappingIndex})"
                            didPalette = True
                        End If
                    End If
                End If
                ' Fallback: direct hair tint color
                If Not didPalette Then
                    Dim effectiveHairColor = hairTintColor
                    If Not effectiveHairColor.HasValue AndAlso state IsNot Nothing Then
                        effectiveHairColor = ResolveColorFormColor(state.HairColorFormID)
                    End If
                    If effectiveHairColor.HasValue Then
                        material.HairTintColor = effectiveHairColor.Value
                        appliedAction = $"hairTint({effectiveHairColor.Value})"
                    End If
                End If
            End If

            Dim forceSkinTint = skinTintColor.HasValue AndAlso ShouldForceSkinTint(candidate, material)
            If forceSkinTint Then
                material.SkinTint = True
                appliedAction &= $"+forceSkinTint"
            End If

            If material.SkinTint AndAlso skinTintColor.HasValue Then
                material.SkinTintColor = skinTintColor.Value
                appliedAction &= $"+skinTintColor({skinTintColor.Value})"
            End If

            If solidTintColor.HasValue AndAlso Not material.Hair AndAlso Not material.SkinTint Then
                shape.TintColor = solidTintColor.Value
                appliedAction &= $"+solidTint({solidTintColor.Value})"
            End If

            NpcPreviewLog.Log($"    shape={shape.ShapeName}: shaderType={material.NifShaderType} matHair={material.Hair} matSkinTint={material.SkinTint} g2p={material.GrayscaleToPaletteColor} g2pScale={material.GrayscaleToPaletteScale} greyscaleTex={material.GreyscaleTexture} specEnabled={material.SpecularEnabled} specMult={material.SpecularMult} smoothness={material.Smoothness} smoothSpecTex={material.SmoothSpecTexture} fresnelPower={material.FresnelPower} envMap={material.EnvironmentMapping} envScale={material.EnvironmentMappingMaskScale} rimLight={material.RimLighting} rimPower={material.RimPower} backLight={material.BackLighting} diffuse={material.Diffuse_or_Base_Texture} normalTex={material.NormalTexture} isBGSM={material.IsBGSM} ? {appliedAction}")
        Next
    End Sub

    Private Shared Function ShouldForceSkinTint(candidate As MeshCandidate, material As FO4UnifiedMaterial_Class) As Boolean
        If candidate Is Nothing OrElse material Is Nothing Then Return False
        If Not material.IsBGSM() Then Return False

        Select Case material.NifShaderType
            Case NiflySharp.Enums.BSLightingShaderType.SkinTint
                Return True

            Case NiflySharp.Enums.BSLightingShaderType.FaceTint
                Return candidate.Kind = MeshCandidateKind.HeadPart OrElse candidate.UsesBodyTexture OrElse candidate.Kind = MeshCandidateKind.Skin
        End Select

        If candidate.Kind = MeshCandidateKind.Skin Then Return True
        If candidate.Kind = MeshCandidateKind.HeadPart AndAlso candidate.HeadPartType = HeadPartTypeFace Then Return True
        If candidate.UsesBodyTexture Then Return True

        Return False
    End Function

    Private Function ResolveHeadPartSolidTintColor(candidate As MeshCandidate) As Nullable(Of Color)
        If candidate Is Nothing OrElse Not candidate.UseSolidTint Then Return Nothing
        Return ResolveColorFormColor(candidate.HeadPartColorFormID)
    End Function

    Private Function ResolveTextureSet(candidate As MeshCandidate, state As NPCVisualState) As TXST_Data
        Dim textureSetFormID As UInteger = 0UI

        If candidate IsNot Nothing Then
            textureSetFormID = candidate.TextureSetFormID
            If textureSetFormID = 0UI AndAlso candidate.Kind = MeshCandidateKind.HeadPart AndAlso candidate.HeadPartType = HeadPartTypeFace AndAlso state IsNot Nothing Then
                textureSetFormID = state.HeadTextureFormID
            End If
        End If

        If textureSetFormID = 0UI Then Return Nothing

        Dim rec = _pluginManager.GetRecord(textureSetFormID)
        If rec Is Nothing OrElse rec.Header.Signature <> "TXST" Then Return Nothing

        Return RecordParsers.ParseTXST(rec, _pluginManager)
    End Function

    Private Sub ApplyTextureSetOverrides(textureSet As TXST_Data, relatedMaterial As Nifcontent_Class_Manolo.RelatedMaterial_Class)
        If textureSet Is Nothing OrElse relatedMaterial Is Nothing Then Return

        Dim material = relatedMaterial.material
        If material Is Nothing Then Return

        If textureSet.MaterialPath <> "" Then
            Dim overrideMaterial = TryLoadMaterialFromDictionary(textureSet.MaterialPath, material)
            If overrideMaterial IsNot Nothing Then
                relatedMaterial.material = overrideMaterial
                relatedMaterial.path = FO4UnifiedMaterial_Class.CorrectMaterialPath(textureSet.MaterialPath)
                material = overrideMaterial
            End If
        End If

        ApplyTextureSetToMaterial(material, textureSet)
    End Sub

    Private Function TryLoadMaterialFromDictionary(materialPath As String, fallbackMaterial As FO4UnifiedMaterial_Class) As FO4UnifiedMaterial_Class
        Dim correctedPath = FO4UnifiedMaterial_Class.CorrectMaterialPath(materialPath)
        If correctedPath = "" Then Return Nothing
        If Not FilesDictionary_class.Dictionary.ContainsKey(correctedPath) Then Return Nothing

        Dim materialType = GetMaterialTypeFromPath(correctedPath, fallbackMaterial)
        If materialType Is Nothing Then Return Nothing

        Try
            Dim material As New FO4UnifiedMaterial_Class()
            material.Deserialize(correctedPath, materialType)
            Return material
        Catch ex As Exception
            NpcPreviewLog.Log($"[MAT] FAILED to load {correctedPath}: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Private Shared Function GetMaterialTypeFromPath(materialPath As String, fallbackMaterial As FO4UnifiedMaterial_Class) As Type
        Select Case Path.GetExtension(materialPath).ToLowerInvariant()
            Case ".bgsm"
                Return GetType(BGSM)
            Case ".bgem"
                Return GetType(BGEM)
        End Select

        If fallbackMaterial IsNot Nothing AndAlso fallbackMaterial.Underlying_Material IsNot Nothing Then
            Return fallbackMaterial.Underlying_Material.GetType()
        End If

        Return Nothing
    End Function

    Private Shared Sub ApplyTextureSetToMaterial(material As FO4UnifiedMaterial_Class, textureSet As TXST_Data)
        If material Is Nothing OrElse textureSet Is Nothing Then Return

        If textureSet.DiffuseTexture <> "" Then material.Diffuse_or_Base_Texture = textureSet.DiffuseTexture
        If textureSet.NormalTexture <> "" Then material.NormalTexture = textureSet.NormalTexture
        If textureSet.WrinklesTexture <> "" Then material.WrinklesTexture = textureSet.WrinklesTexture
        If textureSet.GlowTexture <> "" Then material.GlowTexture = textureSet.GlowTexture
        If textureSet.HeightTexture <> "" Then material.DisplacementTexture = textureSet.HeightTexture
        If textureSet.EnvironmentTexture <> "" Then material.EnvmapTexture = textureSet.EnvironmentTexture
        If textureSet.MultilayerTexture <> "" Then material.InnerLayerTexture = textureSet.MultilayerTexture
        If textureSet.SmoothSpecTexture <> "" Then material.SmoothSpecTexture = textureSet.SmoothSpecTexture
    End Sub

    ''' <summary>
    ''' Apply a Material Swap (MSWP) to shapes - replaces materials matching OriginalMaterial
    ''' with ReplacementMaterial from the swap record. This is how NPCs get unique skin textures.
    ''' </summary>
    Private Sub ApplyMaterialSwap(mswpFormID As UInteger, shapes As IEnumerable(Of IRenderableShape))
        If mswpFormID = 0UI Then Return

        Dim mswpRec = _pluginManager.GetRecord(mswpFormID)
        If mswpRec Is Nothing OrElse mswpRec.Header.Signature <> "MSWP" Then Return

        Dim mswp = RecordParsers.ParseMSWP(mswpRec, _pluginManager)
        If mswp.Substitutions.Count = 0 Then Return

        For Each shape In shapes
            EnsureShapeMaterialResolved(shape)

            Dim relatedMaterial = shape.ShapeMaterial
            If relatedMaterial Is Nothing OrElse relatedMaterial.material Is Nothing Then Continue For

            Dim currentPath = If(relatedMaterial.path, "").Trim()
            If currentPath = "" Then Continue For

            Dim correctedCurrentPath = FO4UnifiedMaterial_Class.CorrectMaterialPath(currentPath)

            For Each sub_ In mswp.Substitutions
                Dim origPath = FO4UnifiedMaterial_Class.CorrectMaterialPath(If(sub_.OriginalMaterial, ""))
                If origPath = "" Then Continue For

                If String.Equals(correctedCurrentPath, origPath, StringComparison.OrdinalIgnoreCase) Then
                    Dim replacementPath = If(sub_.ReplacementMaterial, "")
                    If replacementPath = "" Then Exit For

                    Dim newMaterial = TryLoadMaterialFromDictionary(replacementPath, relatedMaterial.material)
                    If newMaterial IsNot Nothing Then
                        relatedMaterial.material = newMaterial
                        relatedMaterial.path = FO4UnifiedMaterial_Class.CorrectMaterialPath(replacementPath)
                    End If
                    Exit For
                End If
            Next
        Next
    End Sub

    Private Function ResolveHairTintColor(candidate As MeshCandidate, state As NPCVisualState, headPartColor As Nullable(Of Color)) As Nullable(Of Color)
        Select Case candidate.HeadPartType
            Case HeadPartTypeHair, 6 ' Hair and Hairline/Brow use hair color
                Dim hairColor = ResolveColorFormColor(state.HairColorFormID)
                If hairColor.HasValue Then Return hairColor
            Case HeadPartTypeFacialHair
                Dim facialHairColor = ResolveColorFormColor(state.FacialHairColorFormID)
                If facialHairColor.HasValue Then Return facialHairColor
                Dim hairColor = ResolveColorFormColor(state.HairColorFormID)
                If hairColor.HasValue Then Return hairColor
        End Select

        If headPartColor.HasValue Then Return headPartColor
        Return Nothing
    End Function


    Private Function TryResolveHairPaletteRemap(candidate As MeshCandidate, state As NPCVisualState, ByRef paletteTexture As String, ByRef paletteScale As Single) As Boolean
        paletteTexture = ""
        paletteScale = 0.0F

        If candidate Is Nothing OrElse state Is Nothing OrElse Not IsHairHeadPart(candidate) Then Return False

        Dim colorFormID As UInteger = 0UI
        Select Case candidate.HeadPartType
            Case HeadPartTypeHair, 6 ' Hair and Hairline/Brow use hair color
                colorFormID = state.HairColorFormID
            Case HeadPartTypeFacialHair
                colorFormID = If(state.FacialHairColorFormID <> 0UI, state.FacialHairColorFormID, state.HairColorFormID)
        End Select

        Dim clfm = ResolveColorFormData(colorFormID)
        If clfm Is Nothing OrElse Not clfm.HasRemappingIndex Then Return False

        paletteTexture = ResolveRaceHairLookupTexture(state)
        If paletteTexture = "" Then Return False

        paletteScale = clfm.RemappingIndex
        Return True
    End Function

    Private Function ResolveRaceHairLookupTexture(state As NPCVisualState) As String
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return ""

        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return ""

        Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)
        If race Is Nothing Then Return ""

        Dim lookupCandidates = New String() {race.HairColorLookupTexture, race.HairColorExtendedLookupTexture}
        For Each lookupTexture In lookupCandidates
            Dim correctedPath = FO4UnifiedMaterial_Class.CorrectTexturePath(lookupTexture)
            If correctedPath <> "" AndAlso FilesDictionary_class.Dictionary.ContainsKey(correctedPath) Then
                Return lookupTexture
            End If
        Next

        For Each lookupTexture In lookupCandidates
            If Not String.IsNullOrWhiteSpace(lookupTexture) Then Return lookupTexture
        Next

        Return ""
    End Function

    Private Shared Function IsHairHeadPart(candidate As MeshCandidate) As Boolean
        If candidate Is Nothing OrElse candidate.Kind <> MeshCandidateKind.HeadPart Then Return False
        ' Hair (3), Facial Hair (4), Hairline/Brow (6) all use hair color
        Return candidate.HeadPartType = HeadPartTypeHair OrElse
               candidate.HeadPartType = HeadPartTypeFacialHair OrElse
               candidate.HeadPartType = 6
    End Function
    Private Function ResolveSkinTintColor(candidate As MeshCandidate, state As NPCVisualState, headPartColor As Nullable(Of Color)) As Nullable(Of Color)
        ' PRIORITY 1: the NPC's SkinTone tint layer (TETI slot 12).
        ' This is the authoritative source for a character's skin color in FO4 — it's what the engine
        ' uses when applying skin tint. Both my face tint overlay (which skips SkinTone) and the legacy
        ' SkinTintColor multiplier need this value to produce the correct final color.
        If state IsNot Nothing AndAlso candidate IsNot Nothing AndAlso candidate.HeadPartType = HeadPartTypeFace Then
            Dim skinToneColor = ResolveNpcSkinToneColor(state)
            If skinToneColor.HasValue Then Return skinToneColor
        End If

        If state IsNot Nothing AndAlso state.HasTextureLighting Then
            Return state.TextureLightingColor
        End If

        If candidate.HeadPartType = HeadPartTypeFace AndAlso headPartColor.HasValue Then
            Return headPartColor
        End If

        Return Nothing
    End Function

    ''' <summary>Look up the NPC's SkinTone tint layer (TETI slot 12) and resolve its effective
    ''' colour using the same rule the face compositor applies: if the NPC TEND has a non-negative
    ''' TemplateColorIndex, the colour comes from the CLFM referenced by TTEC[TemplateColorIndex];
    ''' otherwise the colour is taken directly from TEND RGB bytes 1..3. Returns Nothing when the
    ''' NPC has no SkinTone layer or the RACE template / CLFM lookup fails.</summary>
    Private Function ResolveNpcSkinToneColor(state As NPCVisualState) As Nullable(Of Color)
        If state Is Nothing Then Return Nothing
        Dim modelNpcFormID = If(state.ModelSourceFormID <> 0UI, state.ModelSourceFormID, state.FormID)
        Dim npcData = GetParsedNpc(modelNpcFormID)
        If npcData Is Nothing OrElse npcData.FaceTintLayers.Count = 0 Then Return Nothing

        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)

        For Each tl In npcData.FaceTintLayers
            Dim opt = race.FindTintOption(tl.Index, state.IsFemale)
            If opt Is Nothing Then Continue For
            If opt.Slot <> CUShort(TintSlot.SkinTone) Then Continue For
            If tl.Discriminator <> 1 Then Continue For

            ' Use the same resolver the compositor uses so body uniform and face compositor
            ' agree on the colour. The blendOp returned here is irrelevant for the body path.
            Dim resolved = ResolvePaletteLayerEffective(tl, opt)
            If resolved.Color <> Color.Empty Then Return resolved.Color
        Next

        Return Nothing
    End Function

    Private Function ResolveColorFormColor(formID As UInteger) As Nullable(Of Color)
        Dim clfm = ResolveColorFormData(formID)
        If clfm Is Nothing OrElse Not clfm.HasColor Then Return Nothing
        Return clfm.Color
    End Function

    Private Function ResolveColorFormData(formID As UInteger) As CLFM_Data
        If formID = 0UI Then Return Nothing

        Dim rec = _pluginManager.GetRecord(formID)
        If rec Is Nothing OrElse rec.Header.Signature <> "CLFM" Then Return Nothing

        Return RecordParsers.ParseCLFM(rec, _pluginManager)
    End Function

    Private Sub EnsureShapeMaterialResolved(shape As IRenderableShape)
        If shape Is Nothing Then Return

        Dim relatedMaterial = shape.ShapeMaterial
        If relatedMaterial Is Nothing Then Return

        Dim materialPath = FO4UnifiedMaterial_Class.CorrectMaterialPath(relatedMaterial.path)
        Dim hasResolvableMaterial = relatedMaterial.path <> "" AndAlso FilesDictionary_class.Dictionary.ContainsKey(materialPath)
        If relatedMaterial.material IsNot Nothing AndAlso (relatedMaterial.path = "" OrElse hasResolvableMaterial) Then
            NpcPreviewLog.Log($"      [EnsureMat] {shape.ShapeName}: OK (path={relatedMaterial.path} resolved={hasResolvableMaterial})")
            Return
        End If
        If shape.NifContent Is Nothing OrElse shape.NifShape Is Nothing OrElse shape.NifShader Is Nothing Then Return

        NpcPreviewLog.Log($"      [EnsureMat] {shape.ShapeName}: REBUILDING from NIF shader! matWasNull={relatedMaterial.material Is Nothing} path={relatedMaterial.path} correctedPath={materialPath} inDict={hasResolvableMaterial}")

        Dim rebuiltMaterial As New FO4UnifiedMaterial_Class()

        Select Case shape.NifShader.GetType()
            Case GetType(BSLightingShaderProperty)
                rebuiltMaterial.Create_From_Shader(shape.NifContent, shape.NifShape, CType(shape.NifShader, BSLightingShaderProperty))
            Case GetType(BSEffectShaderProperty)
                rebuiltMaterial.Create_From_Shader(shape.NifContent, shape.NifShape, CType(shape.NifShader, BSEffectShaderProperty))
            Case Else
                Return
        End Select

        relatedMaterial.material = rebuiltMaterial
        relatedMaterial.path = ""
    End Sub
    Private Shared Function NormalizeDictionaryKeyWithMeshesPrefix(path As String) As String
        If String.IsNullOrWhiteSpace(path) Then Return ""

        Dim normalized = path.Replace("/", "\").Trim()
        If Not normalized.StartsWith("Meshes\", StringComparison.OrdinalIgnoreCase) Then
            normalized = "Meshes\" & normalized
        End If

        Return normalized.ToLowerInvariant()
    End Function

    Private Shared Function HasTemplateFlag(flags As UShort, category As NPC_TemplateCategory) As Boolean
        Dim mask = CUShort(1 << CInt(category))
        Return (flags And mask) <> 0US
    End Function

    Private Shared Function ResolveTemplateSourceFormID(npc As NPC_Data, category As NPC_TemplateCategory) As UInteger
        Dim specificFormID As UInteger = 0UI
        If npc.TemplateActorFormIDs.TryGetValue(category, specificFormID) AndAlso specificFormID <> 0UI Then
            Return specificFormID
        End If

        Return npc.TemplateFormID
    End Function

    Private Function GetParsedNpc(formID As UInteger) As NPC_Data
        Dim rec = _pluginManager.GetRecord(formID)
        If rec Is Nothing OrElse rec.Header.Signature <> "NPC_" Then Return Nothing
        Dim pluginName = If(rec.SourcePluginName <> "", rec.SourcePluginName, "Unknown")
        Return RecordParsers.ParseNPC(rec, pluginName, _pluginManager)
    End Function

    Private Shared Function CreateOwnTraitsState(npc As NPC_Data) As TraitsState
        Return New TraitsState With {
            .IsFemale = npc.IsFemale,
            .RaceFormID = npc.RaceFormID,
            .SkinFormID = npc.SkinFormID,
            .WeightThin = npc.WeightThin,
            .WeightMuscular = npc.WeightMuscular,
            .WeightFat = npc.WeightFat
        }
    End Function

    Private Shared Function CreateOwnInventoryState(npc As NPC_Data) As InventoryState
        Return New InventoryState With {
            .DefaultOutfitFormID = npc.DefaultOutfitFormID,
            .SleepOutfitFormID = npc.SleepOutfitFormID
        }
    End Function

    Private Shared Function CreateOwnModelAnimationState(npc As NPC_Data) As ModelAnimationState
        Dim state As New ModelAnimationState With {
            .HeadTextureFormID = npc.HeadTextureFormID,
            .HairColorFormID = npc.HairColorFormID,
            .FacialHairColorFormID = npc.FacialHairColorFormID,
            .HasTextureLighting = npc.HasTextureLighting,
            .TextureLightingColor = npc.TextureLightingColor
        }
        state.HeadPartFormIDs.AddRange(npc.HeadPartFormIDs)
        Return state
    End Function

    Private Shared Function DescribeNpc(npc As NPC_Data) As String
        If npc Is Nothing Then Return "<unknown NPC>"
        If npc.EditorID <> "" Then Return npc.EditorID
        If npc.FullName <> "" Then Return npc.FullName
        Return npc.FormID.ToString("X8")
    End Function

    Private Shared Function DescribeRecord(rec As PluginRecord) As String
        If rec Is Nothing Then Return "<unknown record>"
        If rec.EditorID <> "" Then Return rec.EditorID
        Return $"{rec.Header.Signature} {rec.Header.FormID:X8}"
    End Function

    Private Shared Sub DeduplicateWarnings(warnings As List(Of String))
        If warnings Is Nothing OrElse warnings.Count <= 1 Then Return
        Dim unique = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        warnings.Clear()
        warnings.AddRange(unique)
    End Sub

    Private Shared Function BuildWarningSuffix(warnings As IList(Of String)) As String
        If warnings Is Nothing OrElse warnings.Count = 0 Then Return ""
        Return $" ({warnings(0)})"
    End Function

    Private Sub TextBoxSearch_TextChanged(sender As Object, e As EventArgs) Handles TextBoxSearch.TextChanged
        _pendingTreeFilter = TextBoxSearch.Text
        _searchDebounceTimer.Stop()
        _searchDebounceTimer.Start()
    End Sub

    Private Sub SearchDebounceTimer_Tick(sender As Object, e As EventArgs) Handles _searchDebounceTimer.Tick
        _searchDebounceTimer.Stop()
        PopulateNPCTree(_pendingTreeFilter)
    End Sub

    Private Sub SetStatus(text As String)
        If InvokeRequired Then
            BeginInvoke(Sub() SetStatus(text))
            Return
        End If
        ToolStripStatusLabel1.Text = text
    End Sub

    Private Shared Function FindFO4DataPath() As String
        If Config_App.Current.DataPath <> "" AndAlso Directory.Exists(Config_App.Current.DataPath) Then
            Return Config_App.Current.DataPath
        End If

        Dim steamPaths = {
            "C:\Program Files (x86)\Steam\steamapps\common\Fallout 4\Data",
            "C:\Program Files\Steam\steamapps\common\Fallout 4\Data",
            "D:\SteamLibrary\steamapps\common\Fallout 4\Data",
            "E:\SteamLibrary\steamapps\common\Fallout 4\Data"
        }
        For Each p In steamPaths
            If Directory.Exists(p) Then Return p
        Next

        Try
            Dim key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout4")
            If key IsNot Nothing Then
                Dim installPath = TryCast(key.GetValue("Installed Path"), String)
                If installPath IsNot Nothing Then
                    Dim dataPath = Path.Combine(installPath, "Data")
                    If Directory.Exists(dataPath) Then Return dataPath
                End If
            End If
        Catch
        End Try

        Return ""
    End Function

#Region "Record Details Panel"

    ''' <summary>
    ''' Populates the record details TreeView for a selected NPC, showing all fields
    ''' with full inheritance resolution (which template provides each category).
    ''' </summary>
    Private Sub PopulateRecordDetails(npc As NPC_Data)
        If InvokeRequired Then
            Invoke(Sub() PopulateRecordDetails(npc))
            Return
        End If

        TreeViewRecordDetails.SuspendLayout()
        TreeViewRecordDetails.BeginUpdate()
        TreeViewRecordDetails.Nodes.Clear()

        If npc Is Nothing Then
            TreeViewRecordDetails.EndUpdate()
            TreeViewRecordDetails.ResumeLayout()
            Return
        End If

        Try
            LabelRecordTitle.Text = $"  {npc} [{npc.PluginName}] FormID:{npc.FormID:X8}"

            ' --- Header ---
            Dim headerNode = AddNode(Nothing, $"NPC_ {npc.EditorID}  [{npc.FormID:X8}]  {npc.PluginName}")
            AddNode(headerNode, $"Full Name: {If(npc.FullName <> "", npc.FullName, "(none)")}")
            AddNode(headerNode, $"Editor ID: {npc.EditorID}")
            AddNode(headerNode, $"Form ID: {npc.FormID:X8}")
            AddNode(headerNode, $"Plugin: {npc.PluginName}")
            AddNode(headerNode, $"Gender: {If(npc.IsFemale, "Female", "Male")}")
            headerNode.Expand()

            ' --- Template Info ---
            If npc.TemplateFormID <> 0UI OrElse npc.TemplateActorFormIDs.Count > 0 Then
                Dim tplNode = AddNode(Nothing, $"Template Configuration  (flags: {npc.TemplateFlags:X4})")
                If npc.TemplateFormID <> 0UI Then
                    AddNode(tplNode, $"Base Template (TPLT): {DescribeFormID(npc.TemplateFormID)}")
                End If
                For Each kvp In npc.TemplateActorFormIDs
                    AddNode(tplNode, $"TPTA[{kvp.Key}] ({GetTemplateCategoryLabel(kvp.Key)}): {DescribeFormID(kvp.Value)}")
                Next
                Dim flagList As New List(Of String)
                For Each boxedCat In [Enum].GetValues(GetType(NPC_TemplateCategory))
                    Dim cat = CType(boxedCat, NPC_TemplateCategory)
                    If HasTemplateFlag(npc.TemplateFlags, cat) Then flagList.Add(GetTemplateCategoryLabel(cat))
                Next
                If flagList.Count > 0 Then AddNode(tplNode, $"Active flags: {String.Join(", ", flagList)}")
                tplNode.Expand()
            End If

            ' --- Traits (with inheritance) ---
            Dim traitsSource = ResolveInheritedSourceNpc(npc, NPC_TemplateCategory.Traits)
            Dim traitsNpc = If(traitsSource, npc)
            Dim traitsLabel = If(traitsSource IsNot Nothing AndAlso traitsSource.FormID <> npc.FormID,
                                 $"Traits  (inherited from {DescribeNpc(traitsSource)} [{traitsSource.FormID:X8}])",
                                 "Traits  (own)")
            Dim traitsNode = AddNode(Nothing, traitsLabel)
            AddNode(traitsNode, $"Race: {DescribeFormID(traitsNpc.RaceFormID)}")
            ExpandRaceDetails(traitsNode, traitsNpc.RaceFormID, traitsNpc.IsFemale)
            If traitsNpc.SkinFormID <> 0UI Then AddNode(traitsNode, $"Skin Armor: {DescribeFormID(traitsNpc.SkinFormID)}")
            AddNode(traitsNode, $"Weight: Thin={traitsNpc.WeightThin:F2}  Muscular={traitsNpc.WeightMuscular:F2}  Fat={traitsNpc.WeightFat:F2}")
            If traitsNpc.BodyMorphRegionValues.Count > 0 Then
                Dim morphNode = AddNode(traitsNode, $"Body Morph Regions ({traitsNpc.BodyMorphRegionValues.Count} values)")
                For i = 0 To traitsNpc.BodyMorphRegionValues.Count - 1
                    AddNode(morphNode, $"[{i}] = {traitsNpc.BodyMorphRegionValues(i):F4}")
                Next
            End If
            traitsNode.Expand()

            ' --- Inventory (with inheritance) ---
            Dim invSource = ResolveInheritedSourceNpc(npc, NPC_TemplateCategory.Inventory)
            Dim invNpc = If(invSource, npc)
            Dim invLabel = If(invSource IsNot Nothing AndAlso invSource.FormID <> npc.FormID,
                              $"Inventory  (inherited from {DescribeNpc(invSource)} [{invSource.FormID:X8}])",
                              "Inventory  (own)")
            Dim invNode = AddNode(Nothing, invLabel)
            If invNpc.DefaultOutfitFormID <> 0UI Then
                Dim outfitNode = AddNode(invNode, $"Default Outfit: {DescribeFormID(invNpc.DefaultOutfitFormID)}")
                ExpandOutfitDetails(outfitNode, invNpc.DefaultOutfitFormID)
            Else
                AddNode(invNode, "Default Outfit: (none)")
            End If
            If invNpc.SleepOutfitFormID <> 0UI Then
                Dim sleepNode = AddNode(invNode, $"Sleep Outfit: {DescribeFormID(invNpc.SleepOutfitFormID)}")
                ExpandOutfitDetails(sleepNode, invNpc.SleepOutfitFormID)
            End If
            invNode.Expand()

            ' --- Model / Appearance (with inheritance) ---
            Dim modelSource = ResolveInheritedSourceNpc(npc, NPC_TemplateCategory.ModelAnimation)
            Dim modelNpc = If(modelSource, npc)
            Dim modelLabel = If(modelSource IsNot Nothing AndAlso modelSource.FormID <> npc.FormID,
                                $"Appearance  (inherited from {DescribeNpc(modelSource)} [{modelSource.FormID:X8}])",
                                "Appearance  (own)")
            Dim modelNode = AddNode(Nothing, modelLabel)
            If modelNpc.HeadTextureFormID <> 0UI Then AddNode(modelNode, $"Head Texture: {DescribeFormID(modelNpc.HeadTextureFormID)}")
            If modelNpc.HairColorFormID <> 0UI Then AddNode(modelNode, $"Hair Color: {DescribeFormID(modelNpc.HairColorFormID)}")
            If modelNpc.FacialHairColorFormID <> 0UI Then AddNode(modelNode, $"Facial Hair Color: {DescribeFormID(modelNpc.FacialHairColorFormID)}")
            If modelNpc.HasTextureLighting Then AddNode(modelNode, $"Texture Lighting: R={modelNpc.TextureLightingColor.R} G={modelNpc.TextureLightingColor.G} B={modelNpc.TextureLightingColor.B}")

            ' Head Parts
            If modelNpc.HeadPartFormIDs.Count > 0 Then
                Dim hpNode = AddNode(modelNode, $"Head Parts ({modelNpc.HeadPartFormIDs.Count})")
                For Each hpFormID In modelNpc.HeadPartFormIDs
                    Dim hpRec = _pluginManager.GetRecord(hpFormID)
                    If hpRec IsNot Nothing Then
                        Dim hdpt = RecordParsers.ParseHDPT(hpRec, _pluginManager)
                        Dim typeName = GetHeadPartTypeName(hdpt.PartType)
                        Dim hpChildNode = AddNode(hpNode, $"[{typeName}] {hdpt.EditorID}  [{hpFormID:X8}]")
                        If hdpt.MeshPath <> "" Then AddNode(hpChildNode, $"Mesh: {hdpt.MeshPath}")
                        If hdpt.TextureSetFormID <> 0UI Then AddNode(hpChildNode, $"TextureSet: {DescribeFormID(hdpt.TextureSetFormID)}")
                        If hdpt.ColorFormID <> 0UI Then AddNode(hpChildNode, $"Color: {DescribeFormID(hdpt.ColorFormID)}")
                        If hdpt.ExtraPartFormIDs.Count > 0 Then
                            For Each epId In hdpt.ExtraPartFormIDs
                                AddNode(hpChildNode, $"Extra Part: {DescribeFormID(epId)}")
                            Next
                        End If
                    Else
                        AddNode(hpNode, $"HDPT [{hpFormID:X8}] (record not found)")
                    End If
                Next
                hpNode.Expand()
            End If

            ' Face Morphs
            If modelNpc.MorphValues.Count > 0 Then
                Dim morphNode = AddNode(modelNode, $"Face Morph Presets ({modelNpc.MorphValues.Count})")
                For Each kvp In modelNpc.MorphValues
                    AddNode(morphNode, $"Key {kvp.Key:X8} = {kvp.Value:F4}")
                Next
            End If

            ' Face Morph Sculpting
            If modelNpc.FaceMorphs.Count > 0 Then
                Dim fmNode = AddNode(modelNode, $"Face Morph Sculpting ({modelNpc.FaceMorphs.Count} morphs)")
                For Each fm In modelNpc.FaceMorphs
                    AddNode(fmNode, $"Morph {fm.Index:X8}: {fm.Values.Count} values")
                Next
            End If

            ' Tint Layers
            If modelNpc.FaceTintLayers.Count > 0 Then
                Dim tintNode = AddNode(modelNode, $"Face Tint Layers ({modelNpc.FaceTintLayers.Count})")
                For Each tl In modelNpc.FaceTintLayers
                    Dim colorStr = If(tl.Color <> Color.Empty, $" Color:({tl.Color.R},{tl.Color.G},{tl.Color.B},{tl.Color.A})", "")
                    AddNode(tintNode, $"Discr:{tl.Discriminator} Index:{tl.Index} Value:{tl.Value}{colorStr}")
                Next
            End If
            modelNode.Expand()

        Finally
            TreeViewRecordDetails.EndUpdate()
            TreeViewRecordDetails.ResumeLayout()
        End Try
    End Sub

    Private Sub PopulateRecordDetailsForVariant(variante As PreviewVariantDefinition)
        If variante Is Nothing OrElse variante.State Is Nothing Then
            PopulateRecordDetails(Nothing)
            Return
        End If

        ' Get root NPC for base info
        Dim npc As NPC_Data = Nothing
        _npcByIdCache.TryGetValue(variante.RootNpcFormID, npc)
        If npc IsNot Nothing Then
            PopulateRecordDetails(npc)
        End If
    End Sub

    ''' <summary>Follow template chain for a category and return the terminal NPC that provides the value.</summary>
    Private Function ResolveInheritedSourceNpc(npc As NPC_Data, category As NPC_TemplateCategory) As NPC_Data
        If npc Is Nothing OrElse Not HasTemplateFlag(npc.TemplateFlags, category) Then Return npc

        Dim visited As New HashSet(Of UInteger)
        Dim current = npc

        While current IsNot Nothing
            If visited.Contains(current.FormID) Then Exit While
            visited.Add(current.FormID)

            If Not HasTemplateFlag(current.TemplateFlags, category) Then Return current

            Dim sourceFormID = ResolveTemplateSourceFormID(current, category)
            If sourceFormID = 0UI Then Return current

            ' If source is a leveled NPC, try to get the first entry
            Dim sourceRec = _pluginManager.GetRecord(sourceFormID)
            If sourceRec Is Nothing Then Return current

            If sourceRec.Header.Signature = "NPC_" Then
                current = GetParsedNpc(sourceFormID)
                If current Is Nothing Then Return npc
            ElseIf sourceRec.Header.Signature = "LVLN" Then
                ' Leveled NPC - get first NPC_ entry
                Dim lvln = RecordParsers.ParseLVLN(sourceRec, _pluginManager)
                Dim firstNpcId = lvln.Entries.Select(Function(e) e.FormID).
                    FirstOrDefault(Function(fid)
                                       Dim r = _pluginManager.GetRecord(fid)
                                       Return r IsNot Nothing AndAlso r.Header.Signature = "NPC_"
                                   End Function)
                If firstNpcId = 0UI Then Return current
                current = GetParsedNpc(firstNpcId)
                If current Is Nothing Then Return npc
            Else
                Return current
            End If
        End While

        Return npc
    End Function

    Private Sub ExpandRaceDetails(parentNode As TreeNode, raceFormID As UInteger, isFemale As Boolean)
        If raceFormID = 0UI Then Return
        Dim raceRec = _pluginManager.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return

        Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)
        Dim raceNode = AddNode(parentNode, $"Race: {race.FullName} [{race.EditorID}]")
        If isFemale Then
            If race.FemaleSkeletonPath <> "" Then AddNode(raceNode, $"Skeleton: {race.FemaleSkeletonPath}")
            If race.FemaleBodyMeshes.Count > 0 Then
                For Each mesh In race.FemaleBodyMeshes
                    AddNode(raceNode, $"Body Mesh: {mesh}")
                Next
            End If
            If race.FemaleDefaultFaceTextureFormID <> 0UI Then AddNode(raceNode, $"Default Face Texture: {DescribeFormID(race.FemaleDefaultFaceTextureFormID)}")
        Else
            If race.MaleSkeletonPath <> "" Then AddNode(raceNode, $"Skeleton: {race.MaleSkeletonPath}")
            If race.MaleBodyMeshes.Count > 0 Then
                For Each mesh In race.MaleBodyMeshes
                    AddNode(raceNode, $"Body Mesh: {mesh}")
                Next
            End If
            If race.MaleDefaultFaceTextureFormID <> 0UI Then AddNode(raceNode, $"Default Face Texture: {DescribeFormID(race.MaleDefaultFaceTextureFormID)}")
        End If
        If race.SkinFormID <> 0UI Then AddNode(raceNode, $"Race Skin: {DescribeFormID(race.SkinFormID)}")
    End Sub

    Private Sub ExpandOutfitDetails(parentNode As TreeNode, outfitFormID As UInteger)
        If outfitFormID = 0UI Then Return
        Dim outfitRec = _pluginManager.GetRecord(outfitFormID)
        If outfitRec Is Nothing Then Return

        If outfitRec.Header.Signature = "OTFT" Then
            Dim otft = RecordParsers.ParseOTFT(outfitRec, _pluginManager)
            For Each itemFormID In otft.ItemFormIDs
                ExpandOutfitItem(parentNode, itemFormID)
            Next
        End If
    End Sub

    Private Sub ExpandOutfitItem(parentNode As TreeNode, itemFormID As UInteger)
        If itemFormID = 0UI Then Return
        Dim itemRec = _pluginManager.GetRecord(itemFormID)
        If itemRec Is Nothing Then
            AddNode(parentNode, $"[{itemFormID:X8}] (missing record)")
            Return
        End If

        Select Case itemRec.Header.Signature
            Case "ARMO"
                Dim armo = RecordParsers.ParseARMO(itemRec, _pluginManager)
                Dim slotStr = FormatSlotMask(armo.SlotMask)
                Dim armoNode = AddNode(parentNode, $"ARMO {armo.EditorID}  ""{armo.FullName}""  [{armo.FormID:X8}]  Slots:{slotStr}")

                ' Follow template armor
                If armo.TemplateArmorFormID <> 0UI Then
                    AddNode(armoNode, $"Template Armor: {DescribeFormID(armo.TemplateArmorFormID)}")
                End If

                ' Armor Addons
                For Each aaFormID In armo.ArmorAddonFormIDs
                    Dim aaRec = _pluginManager.GetRecord(aaFormID)
                    If aaRec Is Nothing OrElse aaRec.Header.Signature <> "ARMA" Then
                        AddNode(armoNode, $"ARMA [{aaFormID:X8}] (missing)")
                        Continue For
                    End If
                    Dim arma = RecordParsers.ParseARMA(aaRec, _pluginManager)
                    Dim aaNode = AddNode(armoNode, $"ARMA {arma.EditorID}  [{arma.FormID:X8}]  Slots:{FormatSlotMask(arma.SlotMask)}")
                    If arma.MaleMeshPath <> "" Then AddNode(aaNode, $"Male Mesh: {arma.MaleMeshPath}")
                    If arma.FemaleMeshPath <> "" Then AddNode(aaNode, $"Female Mesh: {arma.FemaleMeshPath}")
                    If arma.MaleFPMeshPath <> "" Then AddNode(aaNode, $"Male 1P Mesh: {arma.MaleFPMeshPath}")
                    If arma.FemaleFPMeshPath <> "" Then AddNode(aaNode, $"Female 1P Mesh: {arma.FemaleFPMeshPath}")
                    If arma.MaleSkinTextureFormID <> 0UI Then AddNode(aaNode, $"Male Skin Texture: {DescribeFormID(arma.MaleSkinTextureFormID)}")
                    If arma.FemaleSkinTextureFormID <> 0UI Then AddNode(aaNode, $"Female Skin Texture: {DescribeFormID(arma.FemaleSkinTextureFormID)}")
                    If arma.MaleMaterialSwapFormID <> 0UI Then AddNode(aaNode, $"Male Material Swap: {DescribeFormID(arma.MaleMaterialSwapFormID)}")
                    If arma.FemaleMaterialSwapFormID <> 0UI Then AddNode(aaNode, $"Female Material Swap: {DescribeFormID(arma.FemaleMaterialSwapFormID)}")
                    If arma.AdditionalRaces.Count > 0 Then
                        For Each raceId In arma.AdditionalRaces
                            AddNode(aaNode, $"Additional Race: {DescribeFormID(raceId)}")
                        Next
                    End If
                Next

            Case "LVLI"
                Dim lvli = RecordParsers.ParseLVLI(itemRec, _pluginManager)
                Dim lvliNode = AddNode(parentNode, $"LVLI {lvli.EditorID}  [{lvli.FormID:X8}]  ({lvli.Entries.Count} entries)")
                For Each entry In lvli.Entries
                    ExpandOutfitItem(lvliNode, entry.FormID)
                Next

            Case Else
                AddNode(parentNode, $"{itemRec.Header.Signature} {itemRec.EditorID}  [{itemFormID:X8}]")
        End Select
    End Sub

    Private Function AddNode(parent As TreeNode, text As String) As TreeNode
        Dim node As New TreeNode(text)
        If parent Is Nothing Then
            TreeViewRecordDetails.Nodes.Add(node)
        Else
            parent.Nodes.Add(node)
        End If
        Return node
    End Function

    Private Function DescribeFormID(formID As UInteger) As String
        If formID = 0UI Then Return "(none)"
        Dim rec = _pluginManager.GetRecord(formID)
        If rec Is Nothing Then Return $"[{formID:X8}]"
        Dim edid = If(rec.EditorID <> "", rec.EditorID, rec.Header.Signature)
        Dim pluginSuffix = If(String.IsNullOrWhiteSpace(rec.SourcePluginName), "", $" @{rec.SourcePluginName}")
        Return $"{edid}  [{formID:X8}]{pluginSuffix}"
    End Function

    ''' <summary>HDPT PNAM Type enum names (verified wbDefinitionsFO4.pas:7373).</summary>
    Private Shared Function GetHeadPartTypeName(partType As Integer) As String
        Select Case partType
            Case 0 : Return "Misc"
            Case 1 : Return "Face"
            Case 2 : Return "Eyes"
            Case 3 : Return "Hair"
            Case 4 : Return "Facial Hair"
            Case 5 : Return "Scar"
            Case 6 : Return "Eyebrows"
            Case 7 : Return "Meatcaps"
            Case 8 : Return "Teeth"
            Case 9 : Return "Head Rear"
            Case Else : Return $"Type{partType}"
        End Select
    End Function

    Private Shared Function FormatSlotMask(mask As UInteger) As String
        If mask = 0UI Then Return "(none)"
        Dim slots As New List(Of String)
        Dim bitMask As UInteger = 1UI
        For bit = 0 To 31
            If (mask And bitMask) <> 0UI Then
                slots.Add((30 + bit).ToString())
            End If
            bitMask <<= 1
        Next
        Return String.Join(",", slots)
    End Function

#End Region

    Private Sub MainForm_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        If _previewControl IsNot Nothing Then
            _previewControl.Dispose()
        End If
    End Sub
End Class




























