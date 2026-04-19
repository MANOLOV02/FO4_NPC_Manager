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
        ''' <summary>Per-bone scale deltas from the ARMA's BSMP/BSMB/BSMS block (matching this
        ''' NPC's gender). Engine-side these are added on top of RACE.BSMS to shape the outfit
        ''' (cinched waist, wider hips, etc.). Nothing when the ARMA has no BSMS or gender mismatch.</summary>
        Public ArmaBoneScaleDeltas As List(Of ARMA_BoneScaleDelta) = Nothing
        ''' <summary>True = collect into candidates for logging/inspection but exclude from render.
        ''' Set for HDPT type=7 Meatcaps (inner-mouth geometry occluded by teeth; vanilla CK declares
        ''' them but normally not visible in static pose). Filtered out in SelectWinningCandidates.</summary>
        Public Hide As Boolean = False
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
        ''' <summary>Aggregated per-bone scale deltas from ALL winning ARMAs (outfit + armor + skin).
        ''' Each ARMA's gender-matching BSMS entries are summed here. Engine-side these add on top
        ''' of RACE.BSMS to shape the outfit around the body; NPC_Manager consumes them in
        ''' BuildBodyWeightPose so the rendered geometry matches in-game proportions.</summary>
        Public ReadOnly ArmaBoneScaleDeltas As New Dictionary(Of String, System.Numerics.Vector3)(StringComparer.OrdinalIgnoreCase)
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
        ''' <summary>OMOD FormIDs from NPC_.ObjectTemplate combination #0 (robot body parts).</summary>
        Public ObjectTemplateOMODFormIDs As New List(Of UInteger)
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
        ''' <summary>OMOD FormIDs from NPC_.ObjectTemplate combination #0 (robot body parts).
        ''' Empty for humanoids; populated for Assaultron/MrHandy/etc.</summary>
        Public ObjectTemplateOMODFormIDs As New List(Of UInteger)
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
        NpcPreviewLog.Log($"  [FMRS-TOGGLE] fired checked={CheckBoxApplyBoneMorphs.Checked}")
        RebuildAndApplyMergedPose()
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
    ''' Toggling OFF sets MorphResolver=Nothing: per the PipelineStep_Morphs / ApplyMorphPlan
    ''' contract, a null resolver resets geom.Vertices to NifLocalVertices (no stale deltas).</summary>
    Private Sub CheckBoxApplyVertexMorphs_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxApplyVertexMorphs.CheckedChanged
        NpcPreviewLog.Log($"  [VERTEX-MORPH-TOGGLE] fired checked={CheckBoxApplyVertexMorphs.Checked}")
        If _lastRenderedState Is Nothing OrElse _lastRenderData Is Nothing Then
            NpcPreviewLog.Log($"  [VERTEX-MORPH-TOGGLE] ABORT — _lastRenderedState or _lastRenderData is Nothing")
            Return
        End If
        Dim newResolver As IMorphResolver = Nothing
        If CheckBoxApplyVertexMorphs.Checked Then
            newResolver = BuildFaceMorphResolver(_lastRenderedState, _lastRenderData)
        End If
        NpcPreviewLog.Log($"  [VERTEX-MORPH-TOGGLE] new resolver = {If(newResolver IsNot Nothing, "SET", "Nothing")}")
        Dim intent = _previewControl.Intent
        intent.MorphResolver = newResolver
        intent.MarkDirty(RenderDirtyFlags.Morphs)
        _previewControl.InvalidateRender()
    End Sub

    ''' <summary>Toggle body-weight pose (MWGT × BSMS + MRSV + ARMA). Symmetric with FMRS handler:
    ''' both call RebuildAndApplyMergedPose which rebuilds the full merged pose (race + BW + FMRS)
    ''' from current checkbox state, swaps intent.Pose, MarkDirty(Pose). Granular — no reload.</summary>
    Private Sub CheckBoxApplyBodyWeight_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxApplyBodyWeight.CheckedChanged
        NpcPreviewLog.Log($"  [BODY-WEIGHT-TOGGLE] fired checked={CheckBoxApplyBodyWeight.Checked}")
        RebuildAndApplyMergedPose()
    End Sub

    ''' <summary>Shared path for FMRS / body-weight toggles: rebuild the merged NPC pose from
    ''' current checkbox state and apply via intent.Pose + MarkDirty(Pose). SkeletonDictionary
    ''' is already populated from the initial render, so BuildMergedNpcPose can parent-walk.</summary>
    Private Sub RebuildAndApplyMergedPose()
        If _lastRenderedState Is Nothing OrElse _lastRenderData Is Nothing Then
            NpcPreviewLog.Log($"  [POSE-TOGGLE] ABORT — no initial render cached")
            Return
        End If
        Dim fmrsEnabled = CheckBoxApplyBoneMorphs.Checked
        Dim bwEnabled = CheckBoxApplyBodyWeight.Checked
        Dim mergedPose = BuildMergedNpcPose(_lastRenderedState, _lastRenderData, fmrsEnabled, bwEnabled)
        NpcPreviewLog.Log($"  [POSE-TOGGLE] rebuilt merged pose (fmrs={fmrsEnabled} bw={bwEnabled}) → {mergedPose.Transforms.Count} bones")
        Dim intent = _previewControl.Intent
        intent.Pose = mergedPose
        intent.MarkDirty(RenderDirtyFlags.Pose)
        _previewControl.InvalidateRender()
    End Sub


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

        DumpBPTDForRace(state)

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
        Dim faceSkelBytes = TryLoadFaceSkeletonBytes(state)
        Dim bodyWeightEnabled = CheckBoxApplyBodyWeight.Checked
        NpcPreviewLog.Log($"  [BODY-WEIGHT-TOGGLE] {If(bodyWeightEnabled, "ON — body-weight pose applied", "OFF — body-weight pose skipped (MWGT/BSMS/NNAM not applied)")}")

        ' Prime skeleton: BuildMergedNpcPose → BuildBodyWeightPose walks the skeleton hierarchy
        ' via ResolveMrsvRegion (needs SkeletonDictionary populated with parent links). The pipeline
        ' will re-run PipelineStep_Skeleton later through the resolver — idempotent.
        baseSkelResolver.ResolveSkeleton(renderData.Shapes, Nothing)
        ' Robot extended-skeleton merge (TO-REVIEW 2026-04-19): per user empirical call —
        ' robot race base `skeleton.nif` (e.g. Actors\Robot\CharacterAssets\skeleton.nif) is
        ' empty/minimal; actual bones live in `SkeletonRef.nif` + other sibling skeleton files in
        ' the same folder. Detect "robot-mode" by presence of `<bodySkelDir>/SkeletonRef.nif` and
        ' merge all `skeleton*.nif` from that folder. Humanoid races have no SkeletonRef sibling
        ' → no-op. No guarantees that merging everything is the canonical solution — see
        ' project_robot_rendering_combinations.md for the pending investigation.
        MergeRobotExtendedSkeletonsIfRobot(state)
        If faceSkelBytes IsNot Nothing Then
            Skeleton_Class.MergeFaceSkeleton(faceSkelBytes)
        End If

        ' Build the merged pose: race-height + body-weight + FMRS → single Poses_class.
        ' Goes into intent.Pose. The 3 checkbox handlers below call BuildMergedNpcPose with the
        ' current checkbox state to rebuild on toggle (granular MarkDirty(Pose), no full reload).
        Dim mergedPose = BuildMergedNpcPose(state, renderData, boneMorphsEnabled, bodyWeightEnabled)
        Dim skelResolver As ISkeletonResolver = New FaceBoneSkeletonResolver(baseSkelResolver, faceSkelBytes)

        Dim request As New RenderRequest With {
            .Shapes = renderData.Shapes,
            .Pose = mergedPose,
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

        ' DIAGNOSTIC: for a small set of ground-truth NPCs (Alijo 0018A6D1, Cait 00079249),
        ' compare our post-morph vertices against CK's FaceGen bake on disk to find which verts
        ' differ and by how much. Only runs for those two FormIDs — silent for everyone else.
        Try
            CompareAgainstFaceGenIfWhitelisted(state, morphResolver)
        Catch ex As Exception
            NpcPreviewLog.Log($"  [FACEGEN-DIAG] exception: {ex.Message}")
        End Try

        SetStatus($"Rendered {previewVariant.DisplayName} ({renderData.Shapes.Count} shapes)")
    End Function

    ''' <summary>Whitelisted FormIDs for FaceGen ground-truth diff. Diagnostic only — silent
    ''' for any other NPC so the log doesn't get flooded.</summary>
    Private Shared ReadOnly _faceGenDiagWhitelist As HashSet(Of UInteger) = New HashSet(Of UInteger) From {
        &H18A6D1UI,  ' REChokepointCT02_Merchant (Alijo) — vanilla Fallout4.esm (musc-dominant, low fat)
        &H79249UI,  ' CompanionCait — modded test NPC (musc-dominant, zero fat)
        &H19EE79UI,  ' Cientifica — fat-heavy fixture (discriminator for fat channel in body-weight model)
        &H15E922UI,  ' FMIN=2 fixture — FMIN semantics discriminator (scale vs scale+translation vs +rotation)
        &H19FD9UI   ' FMIN=4 fixture — stronger discriminator for FMIN-on-vertex hypothesis (1/FMIN vs 1/FMIN² vs zero)
    }

    ''' <summary>BPND.PartType enum (0-25) → name mapping per wbDefinitionsFO4.pas:8079-8107.</summary>
    Private Shared ReadOnly _bptdPartTypeNames As String() = {
        "Torso", "Head1", "Eye", "LookAt", "FlyGrab", "Head2",
        "LeftArm1", "LeftArm2", "RightArm1", "RightArm2",
        "LeftLeg1", "LeftLeg2", "LeftLeg3",
        "RightLeg1", "RightLeg2", "RightLeg3",
        "Brain", "Weapon", "Root", "COM", "Pelvis",
        "Camera", "OffsetRoot", "LeftFoot", "RightFoot", "FaceTargetSource"
    }

    ''' <summary>Tracks races already dumped to avoid flooding the log on subsequent renders.</summary>
    Private _bptdDumpedRaces As New HashSet(Of UInteger)

    ''' <summary>Diagnostic: dump BPTD (Body Part Data) record referenced by the NPC's RACE via GNAM.
    ''' Each Body Part has NodeName (bone), PartName, VATS target, and a PartType enum — Bethesda's
    ''' authoritative mapping of bones to body part categories. Logged once per race to investigate
    ''' whether MRSV region resolution (5 groups) can be derived from BPTD part types (24 groups).</summary>
    Private Sub DumpBPTDForRace(state As NPCVisualState)
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return
        If _bptdDumpedRaces.Contains(state.RaceFormID) Then Return
        _bptdDumpedRaces.Add(state.RaceFormID)

        Try
            Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
            If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return
            Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)

            If race.BodyPartDataFormID = 0UI Then
                NpcPreviewLog.Log($"  [BPTD] RACE {race.EditorID} (0x{race.FormID:X8}) has no GNAM → no BodyPartData")
                Return
            End If

            Dim bptdRec = _pluginManager.GetRecord(race.BodyPartDataFormID)
            If bptdRec Is Nothing Then
                NpcPreviewLog.Log($"  [BPTD] RACE {race.EditorID} → BPTD 0x{race.BodyPartDataFormID:X8} NOT FOUND")
                Return
            End If
            If bptdRec.Header.Signature <> "BPTD" Then
                NpcPreviewLog.Log($"  [BPTD] FormID 0x{race.BodyPartDataFormID:X8} is not BPTD (sig={bptdRec.Header.Signature})")
                Return
            End If

            Dim bptd = ActorRecordParsers.ParseBPTD(bptdRec, _pluginManager)
            NpcPreviewLog.Log($"  [BPTD] RACE {race.EditorID} → BPTD {bptd.EditorID} (0x{bptd.FormID:X8}) parts={bptd.Parts.Count}")
            For Each part In bptd.Parts
                Dim ptName = If(part.PartType < _bptdPartTypeNames.Length, _bptdPartTypeNames(part.PartType), $"Unknown({part.PartType})")
                NpcPreviewLog.Log($"    [BPTD] part='{part.PartName}' node='{part.NodeName}' VATS='{part.VATSTarget}' type={part.PartType}/{ptName} flags=0x{part.Flags:X2} health={part.HealthPercent} toHit={part.ToHitChance} geoSegIdx={part.GeometrySegmentIndex} nonLethalDismem={part.NonLethalDismembermentChance}")
            Next
        Catch ex As Exception
            NpcPreviewLog.Log($"  [BPTD] exception: {ex.Message}")
        End Try
    End Sub


    Private Sub CompareAgainstFaceGenIfWhitelisted(state As NPCVisualState, morphResolver As IMorphResolver)
        If state Is Nothing Then Return
        Dim modelNpcFormID = If(state.ModelSourceFormID <> 0UI, state.ModelSourceFormID, state.FormID)
        If Not _faceGenDiagWhitelist.Contains(modelNpcFormID) Then Return

        ' Build the FaceGen NIF path per Bethesda convention:
        '   Meshes\actors\character\FaceGenData\FaceGeom\<plugin>\<FormID 8-hex>.nif
        ' For FormIDs under 0x02000000 in vanilla, plugin = Fallout4.esm.
        Dim pluginName As String = "Fallout4.esm"
        Dim faceGenPath = $"meshes\actors\character\facegendata\facegeom\{pluginName}\{modelNpcFormID:X8}.nif"

        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(faceGenPath.ToLowerInvariant(), loc) Then
            NpcPreviewLog.Log($"  [FACEGEN-DIAG] FaceGen NIF not found via FilesDictionary: '{faceGenPath}'")
            Return
        End If
        Dim faceGenBytes = loc.GetBytes()
        If faceGenBytes Is Nothing OrElse faceGenBytes.Length = 0 Then
            NpcPreviewLog.Log($"  [FACEGEN-DIAG] FaceGen NIF empty: '{faceGenPath}'")
            Return
        End If

        Dim baked As New Nifcontent_Class_Manolo()
        Try
            baked.Load_Manolo(faceGenBytes)
        Catch ex As Exception
            NpcPreviewLog.Log($"  [FACEGEN-DIAG] failed to parse FaceGen NIF: {ex.Message}")
            Return
        End Try

        ' Find the head shape by name (not by max verts — hair shapes have more vertices).
        ' Priority: explicit BaseFemaleHead/BaseMaleHead, then anything with "Head" in the name,
        ' excluding "HeadRear", "Hair" and other non-skull parts.
        Dim bakedShapes = baked.GetShapes().OfType(Of NiflySharp.Blocks.BSTriShape)().ToList()
        If bakedShapes.Count = 0 Then
            NpcPreviewLog.Log($"  [FACEGEN-DIAG] FaceGen NIF has no BSTriShape to compare.")
            Return
        End If
        ' Log every shape available so we can pick manually if heuristic fails.
        For Each sh In bakedShapes
            Dim shName = If(sh.Name Is Nothing, "(unnamed)", sh.Name.String)
            Dim vc = If(sh.VertexPositions IsNot Nothing, sh.VertexPositions.Count, 0)
            NpcPreviewLog.Log($"  [FACEGEN-DIAG]   available shape='{shName}' verts={vc}")
        Next
        Dim bakedHead As NiflySharp.Blocks.BSTriShape = bakedShapes.FirstOrDefault(Function(s)
                                                                                       Dim n = If(s.Name Is Nothing, "", s.Name.String)
                                                                                       If String.IsNullOrEmpty(n) Then Return False
                                                                                       If n.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0 Then Return False
                                                                                       If n.IndexOf("Rear", StringComparison.OrdinalIgnoreCase) >= 0 Then Return False
                                                                                       If n.IndexOf("Lashes", StringComparison.OrdinalIgnoreCase) >= 0 Then Return False
                                                                                       If n.IndexOf("Eyes", StringComparison.OrdinalIgnoreCase) >= 0 Then Return False
                                                                                       If n.IndexOf("Mouth", StringComparison.OrdinalIgnoreCase) >= 0 Then Return False
                                                                                       Return n.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0
                                                                                   End Function)
        If bakedHead Is Nothing Then
            NpcPreviewLog.Log($"  [FACEGEN-DIAG] no head shape found in FaceGen NIF (tried 'Head' name filter excluding Hair/Rear/Lashes/Eyes/Mouth).")
            Return
        End If
        Dim bakedVertCount = If(bakedHead.VertexPositions IsNot Nothing, bakedHead.VertexPositions.Count, 0)
        Dim bakedHeadName = If(bakedHead.Name Is Nothing, "(unnamed)", bakedHead.Name.String)
        NpcPreviewLog.Log($"  [FACEGEN-DIAG] baked head shape='{bakedHeadName}' verts={bakedVertCount}")
        If bakedVertCount = 0 Then Return

        ' Log the FaceGen NIF's accumulated root→shape transform.
        Try
            Dim bakedShapeNode = TryCast(baked.GetParentNode(bakedHead), NiflySharp.Blocks.NiNode)
            If bakedShapeNode Is Nothing Then bakedShapeNode = baked.GetRootNode()
            If bakedShapeNode IsNot Nothing Then
                Dim gt = Transform_Class.GetGlobalTransform(bakedShapeNode, baked)
                NpcPreviewLog.Log($"  [FACEGEN-DIAG] baked head NIF global transform: translation=({gt.Translation.X:F4},{gt.Translation.Y:F4},{gt.Translation.Z:F4}) scale={gt.Scale:F4} rotation R11={gt.Rotation.M11:F4} R22={gt.Rotation.M22:F4} R33={gt.Rotation.M33:F4}")
            End If
        Catch ex As Exception
            NpcPreviewLog.Log($"  [FACEGEN-DIAG] could not read baked NIF root transform: {ex.Message}")
        End Try


        If _previewControl Is Nothing OrElse _previewControl.Model Is Nothing Then
            NpcPreviewLog.Log($"  [FACEGEN-DIAG] no model loaded yet to compare against.")
            Return
        End If

        ' Find our equivalent head mesh (post-morph). Match by name containing 'Head' + same vertex count.
        Dim ourMesh As PreviewModel.RenderableMesh = Nothing
        For Each m In _previewControl.Model.meshes
            If m Is Nothing OrElse m.MeshData Is Nothing OrElse m.MeshData.Shape Is Nothing Then Continue For
            Dim shapeName = m.MeshData.Shape.ShapeName
            If String.IsNullOrEmpty(shapeName) Then Continue For
            If shapeName.IndexOf("Head", StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            Dim geomVerts = m.MeshData.Meshgeometry.Vertices
            If geomVerts Is Nothing Then Continue For
            If geomVerts.Length = bakedVertCount Then
                ourMesh = m
                Exit For
            End If
        Next
        If ourMesh Is Nothing Then
            NpcPreviewLog.Log($"  [FACEGEN-DIAG] no matching head mesh (verts={bakedVertCount}) in our model.")
            Return
        End If

        Dim ourVerts = ourMesh.MeshData.Meshgeometry.Vertices
        Dim count = Math.Min(ourVerts.Length, bakedVertCount)

        ' First pass: compute mean diff (baked - ours) to detect a uniform offset.
        Dim meanDx As Double = 0, meanDy As Double = 0, meanDz As Double = 0
        For i = 0 To count - 1
            Dim bv = bakedHead.VertexPositions(i)
            Dim ov = ourVerts(i)
            meanDx += CDbl(bv.X) - CDbl(ov.X)
            meanDy += CDbl(bv.Y) - CDbl(ov.Y)
            meanDz += CDbl(bv.Z) - CDbl(ov.Z)
        Next
        meanDx /= count : meanDy /= count : meanDz /= count
        NpcPreviewLog.Log($"  [FACEGEN-DIAG] mean offset (baked-ours) = ({meanDx:F4},{meanDy:F4},{meanDz:F4})")

        ' Per-axis linear fit: find (a, b) such that baked_axis ≈ a + b * ours_axis.
        ' Signature of a head-bone bind-pose transform (scale+translation per axis).
        ' Least-squares fit: b = (sum(xy) - N*mean_x*mean_y) / (sum(x^2) - N*mean_x^2)
        '                   a = mean_y - b*mean_x
        Dim meanOX As Double = 0, meanOY As Double = 0, meanOZ As Double = 0
        Dim meanBX As Double = 0, meanBY As Double = 0, meanBZ As Double = 0
        For i = 0 To count - 1
            Dim bv = bakedHead.VertexPositions(i)
            Dim ov = ourVerts(i)
            meanOX += ov.X : meanOY += ov.Y : meanOZ += ov.Z
            meanBX += bv.X : meanBY += bv.Y : meanBZ += bv.Z
        Next
        meanOX /= count : meanOY /= count : meanOZ /= count
        meanBX /= count : meanBY /= count : meanBZ /= count

        Dim sumXOXB As Double = 0, sumYOYB As Double = 0, sumZOZB As Double = 0
        Dim sumXOXO As Double = 0, sumYOYO As Double = 0, sumZOZO As Double = 0
        For i = 0 To count - 1
            Dim bv = bakedHead.VertexPositions(i)
            Dim ov = ourVerts(i)
            Dim cox = ov.X - meanOX : Dim coy = ov.Y - meanOY : Dim coz = ov.Z - meanOZ
            Dim cbx = bv.X - meanBX : Dim cby = bv.Y - meanBY : Dim cbz = bv.Z - meanBZ
            sumXOXB += cox * cbx : sumYOYB += coy * cby : sumZOZB += coz * cbz
            sumXOXO += cox * cox : sumYOYO += coy * coy : sumZOZO += coz * coz
        Next
        Dim slopeX As Double = If(sumXOXO > 0.00001, sumXOXB / sumXOXO, 1.0)
        Dim slopeY As Double = If(sumYOYO > 0.00001, sumYOYB / sumYOYO, 1.0)
        Dim slopeZ As Double = If(sumZOZO > 0.00001, sumZOZB / sumZOZO, 1.0)
        Dim interceptX As Double = meanBX - slopeX * meanOX
        Dim interceptY As Double = meanBY - slopeY * meanOY
        Dim interceptZ As Double = meanBZ - slopeZ * meanOZ
        NpcPreviewLog.Log($"  [FACEGEN-DIAG] per-axis fit: X: baked = {interceptX:F4} + {slopeX:F4}*ours  Y: baked = {interceptY:F4} + {slopeY:F4}*ours  Z: baked = {interceptZ:F4} + {slopeZ:F4}*ours")


        Dim diffs As New List(Of (Idx As Integer, Bx As Single, By As Single, Bz As Single, Ox As Single, Oy As Single, Oz As Single, Mag As Single, ResidualMag As Single))(count)
        Dim sumSq As Double = 0
        Dim sumSqResidual As Double = 0
        Dim sumSqResidualPerAxisFit As Double = 0
        Dim maxMag As Single = 0
        For i = 0 To count - 1
            Dim bv = bakedHead.VertexPositions(i)
            Dim bx = CSng(bv.X) : Dim byv = CSng(bv.Y) : Dim bz = CSng(bv.Z)
            Dim ov = ourVerts(i)
            Dim ox = CSng(ov.X) : Dim oy = CSng(ov.Y) : Dim oz = CSng(ov.Z)
            Dim dx = bx - ox : Dim dy = byv - oy : Dim dz = bz - oz
            Dim rdx = dx - CSng(meanDx) : Dim rdy = dy - CSng(meanDy) : Dim rdz = dz - CSng(meanDz)
            Dim residualMag = CSng(Math.Sqrt(rdx * rdx + rdy * rdy + rdz * rdz))
            sumSqResidual += CDbl(residualMag) * CDbl(residualMag)
            ' Residual tras aplicar per-axis linear fit baked ≈ a + b*ours:
            ' expected baked = a + b*ours; residual = actual baked - expected baked.
            Dim expectedBX = interceptX + slopeX * ox
            Dim expectedBY = interceptY + slopeY * oy
            Dim expectedBZ = interceptZ + slopeZ * oz
            Dim fitRdx = bx - expectedBX
            Dim fitRdy = byv - expectedBY
            Dim fitRdz = bz - expectedBZ
            Dim fitMag = Math.Sqrt(fitRdx * fitRdx + fitRdy * fitRdy + fitRdz * fitRdz)
            sumSqResidualPerAxisFit += fitMag * fitMag
            Dim mag = CSng(Math.Sqrt(dx * dx + dy * dy + dz * dz))
            sumSq += CDbl(mag) * CDbl(mag)
            If mag > maxMag Then maxMag = mag
            diffs.Add((i, bx, byv, bz, ox, oy, oz, mag, residualMag))
        Next
        diffs.Sort(Function(a, b) b.Mag.CompareTo(a.Mag))
        Dim rms As Double = Math.Sqrt(sumSq / Math.Max(1, count))
        Dim rmsResidual As Double = Math.Sqrt(sumSqResidual / Math.Max(1, count))
        Dim rmsResidualPerAxisFit As Double = Math.Sqrt(sumSqResidualPerAxisFit / Math.Max(1, count))

        NpcPreviewLog.Log($"  [FACEGEN-DIAG] NPC=0x{modelNpcFormID.ToString("X8")} compared {count} verts: RMS={rms.ToString("F4")} maxDiff={maxMag.ToString("F4")} RMSresidualAfterMeanSub={rmsResidual.ToString("F4")} RMSresidualAfterPerAxisFit={rmsResidualPerAxisFit.ToString("F4")}")

        ' Re-resolve the morph plan early — needed both for per-vertex morph contribution logging
        ' below AND for building a bind-pose comparison geometry in the world-space section.
        Dim headPlan As MorphPlan = Nothing
        If morphResolver IsNot Nothing Then
            Try
                headPlan = morphResolver.ResolveMorphPlan(ourMesh.MeshData.Shape, ourMesh.MeshData.Meshgeometry)
            Catch ex As Exception
                NpcPreviewLog.Log($"  [FACEGEN-DIAG] could not re-resolve head morph plan: {ex.Message}")
            End Try
        End If

        ' Count morphs per vertex (used by both the bucket analysis below and the per-vertex top diff log).
        Dim morphCountPerVert(count - 1) As Integer
        If headPlan IsNot Nothing AndAlso headPlan.Channels IsNot Nothing Then
            For Each ch In headPlan.Channels
                If ch.Deltas Is Nothing Then Continue For
                For Each d In ch.Deltas
                    If d.index < CUInt(count) Then morphCountPerVert(CInt(d.index)) += 1
                Next
            Next
        End If

        ' World-space compare:
        '   OUR  = the render's actual SkinnedGeometry → already has (vertex morphs) + (FMRS pose
        '          via PerVertexSkinMatrix). GetWorldVertices produces world verts with FMRS baked in.
        '   FG   = FaceGen NIF extracted at bind pose (ApplyPose:=False). The FaceGen already has
        '          FMRS baked into its vertex positions by CK, so applying bind pose (no FMRS) just
        '          places each vertex at its world coordinate.
        ' Risks:
        '   1. Vertex order mismatch between FaceGen NIF and our BaseFemaleHead.nif → loggeo
        '      un sample de verts base side-by-side para verificar correspondencia.
        '   2. Bind matrix mismatch: FaceGen uses its own BSSkinBoneData bind transforms;
        '      our render uses skeleton_female_faceBones.nif bind. Si difieren, diff world
        '      refleja mostly esa diff, no un morph bug.
        Try
            Dim fgShape As IRenderableShape = New NifRenderableShape(baked, bakedHead, 0)

            ' Isolated bake-vs-app harness with morph attribution. Uses fresh skeleton NIFs (body + face),
            ' injects raceHeight as Root Scale to match the app render, skins the bake manually, then
            ' compares vertex-by-vertex against ourWorld. Per-vertex morph attribution (vertex morphs from
            ' MorphPlan, bone morphs from SkeletonDictionary DeltaTransforms) is built alongside so we can
            ' identify WHICH morph (not just where) explains each top-diff vertex. Zero library changes.
            Try
                DumpIsolatedBakeHarnessCSV(state, baked, bakedHead, fgShape,
                                           ourMesh.MeshData.Meshgeometry,
                                           ourMesh.MeshData.Shape, morphResolver)
            Catch exH As Exception
                NpcPreviewLog.Log($"  [HARNESS-RAW] top-level exception: {exH.Message}")
            End Try

            ' Diagnostic: verificar que los bones de AMBOS lados estén en SkeletonDictionary
            ' (merge face skel ya corrió antes del render). Si el bake referencia un bone
            ' que no está en el dict, ExtractSkinnedGeometry cae al fallback del NiNode
            ' del propio bake y su bind pose puede divergir del nuestro → ensucia la diff.
            Dim bakeBones = fgShape.ShapeBones.Select(Function(n) If(n?.Name?.String, "")).Where(Function(s) s <> "").ToList()
            Dim ourBones = ourMesh.MeshData.Shape.ShapeBones.Select(Function(n) If(n?.Name?.String, "")).Where(Function(s) s <> "").ToList()
            Dim bakeSet = New HashSet(Of String)(bakeBones, StringComparer.OrdinalIgnoreCase)
            Dim ourSet = New HashSet(Of String)(ourBones, StringComparer.OrdinalIgnoreCase)
            Dim onlyBake = bakeSet.Except(ourSet, StringComparer.OrdinalIgnoreCase).OrderBy(Function(s) s).ToList()
            Dim onlyOurs = ourSet.Except(bakeSet, StringComparer.OrdinalIgnoreCase).OrderBy(Function(s) s).ToList()
            Dim bakeMissingInDict = bakeBones.Where(Function(b) Not Skeleton_Class.SkeletonDictionary.ContainsKey(b)).OrderBy(Function(s) s).ToList()
            Dim oursMissingInDict = ourBones.Where(Function(b) Not Skeleton_Class.SkeletonDictionary.ContainsKey(b)).OrderBy(Function(s) s).ToList()
            NpcPreviewLog.Log($"  [FG-BONES] bake={bakeBones.Count} ours={ourBones.Count} onlyInBake={onlyBake.Count} onlyInOurs={onlyOurs.Count} bakeMissingInDict={bakeMissingInDict.Count} oursMissingInDict={oursMissingInDict.Count}")
            If onlyBake.Count > 0 Then NpcPreviewLog.Log($"    [FG-BONES] only-in-BAKE: {String.Join(", ", onlyBake)}")
            If onlyOurs.Count > 0 Then NpcPreviewLog.Log($"    [FG-BONES] only-in-OURS: {String.Join(", ", onlyOurs)}")
            If bakeMissingInDict.Count > 0 Then NpcPreviewLog.Log($"    [FG-BONES] bake-bones NOT-in-SkeletonDict (fallback active): {String.Join(", ", bakeMissingInDict)}")
            If oursMissingInDict.Count > 0 Then NpcPreviewLog.Log($"    [FG-BONES] our-bones NOT-in-SkeletonDict: {String.Join(", ", oursMissingInDict)}")

            ' Log raw skin bind transforms from bake NIF's BSSkinInstance.BoneData.
            ' Verifies what localT actually contains for bake bones.
            Try
                Dim bakeBoneNodes = fgShape.ShapeBones.ToArray()
                Dim bakeBoneTxs = fgShape.ShapeBoneTransforms.ToArray()
                For k = 0 To bakeBoneNodes.Length - 1
                    Dim nm = If(bakeBoneNodes(k)?.Name?.String, "")
                    If nm = "HEAD" OrElse nm = "Neck_skin" OrElse nm = "Chest_skin" Then
                        Dim lt = bakeBoneTxs(k)
                        NpcPreviewLog.Log($"    [BAKE-LOCALT] bone='{nm}' Trans=({lt.Translation.X:F4},{lt.Translation.Y:F4},{lt.Translation.Z:F4}) Scale={lt.Scale:F4}")
                        NpcPreviewLog.Log($"                  Rotation=[{lt.Rotation.M11:F4} {lt.Rotation.M12:F4} {lt.Rotation.M13:F4} | {lt.Rotation.M21:F4} {lt.Rotation.M22:F4} {lt.Rotation.M23:F4} | {lt.Rotation.M31:F4} {lt.Rotation.M32:F4} {lt.Rotation.M33:F4}]")
                    End If
                Next
            Catch : End Try

            ' Extract bake geometry using the current SkeletonDictionary (FMRS pose + race Root scale
            ' already applied by the normal render pass). No per-bone compensation: CK does NOT bake
            ' race-height (race is a runtime Delta.Scale on Root; see [RACE-HEIGHT-POSE] and the
            ' harness result where injecting Root Scale=raceHeight dropped RMS from 2.41 → 0.09).
            Dim fgGeom As SkinnedGeometry = SkinningHelper.ExtractSkinnedGeometry(
                fgShape, ApplyPose:=True, singleboneskinning:=False, RecalculateNormals:=False)
            Dim fgWorld = SkinningHelper.GetWorldVertices(fgGeom)

            Dim ourGeo = ourMesh.MeshData.Meshgeometry
            ' Diagnostic sanity check: log first vert's PerVertexSkinMatrix Z-translation component.
            ' If scale was applied to skeleton AFTER skinning ran, this won't reflect it.
            Try
                If ourGeo.PerVertexSkinMatrix IsNot Nothing AndAlso ourGeo.PerVertexSkinMatrix.Length > 0 Then
                    Dim m0 = ourGeo.PerVertexSkinMatrix(0)
                    NpcPreviewLog.Log($"  [SKIN-CACHE-SANITY] ourGeo verts={ourGeo.Vertices.Length} PVSM[0].M={m0.M11:F4},{m0.M12:F4},{m0.M13:F4},{m0.M14:F4} | T=({m0.M41:F4},{m0.M42:F4},{m0.M43:F4})")
                End If
            Catch : End Try
            Dim ourWorld = SkinningHelper.GetWorldVertices(ourGeo)

            Dim wCount = Math.Min(fgWorld.Length, ourWorld.Length)
            Dim sumSqW As Double = 0
            Dim maxW As Single = 0
            Dim nExactW As Integer = 0, nTinyW As Integer = 0, nSmallW As Integer = 0, nLargeW As Integer = 0, nHugeW As Integer = 0
            For i = 0 To wCount - 1
                Dim dx = CSng(fgWorld(i).X - ourWorld(i).X)
                Dim dy = CSng(fgWorld(i).Y - ourWorld(i).Y)
                Dim dz = CSng(fgWorld(i).Z - ourWorld(i).Z)
                Dim mag = CSng(Math.Sqrt(dx * dx + dy * dy + dz * dz))
                sumSqW += CDbl(mag) * CDbl(mag)
                If mag > maxW Then maxW = mag
                If mag < 0.001F Then nExactW += 1
                If mag >= 0.001F AndAlso mag < 0.01F Then nTinyW += 1
                If mag >= 0.01F AndAlso mag < 0.1F Then nSmallW += 1
                If mag >= 0.1F AndAlso mag < 0.5F Then nLargeW += 1
                If mag >= 0.5F Then nHugeW += 1
            Next
            Dim rmsW As Double = Math.Sqrt(sumSqW / Math.Max(1, wCount))
            NpcPreviewLog.Log($"  [FACEGEN-DIAG-WORLD] world compare (ours+FMRS vs FG bind): {wCount} verts RMS={rmsW.ToString("F4")} maxDiff={maxW.ToString("F4")} exact(<0.001)={nExactW} tiny(<0.01)={nTinyW} small(<0.1)={nSmallW} large(<0.5)={nLargeW} huge(>=0.5)={nHugeW}")

            ' Bucket by vertex morph count — the user's decomposition strategy:
            '   0 morphs: diff here reveals BONE MORPH (FMRS) pipeline bugs (since vertex morphs
            '             don't touch these verts, only skinning/bone transforms do).
            '   1 morph:  diff beyond the 0-morph baseline → that single morph applied wrong.
            '   N morphs: cumulative error as morphs stack.
            Dim morphCountArr = morphCountPerVert
            Dim bucketN As New Dictionary(Of Integer, Integer)
            Dim bucketSumSq As New Dictionary(Of Integer, Double)
            Dim bucketMax As New Dictionary(Of Integer, Single)
            For i = 0 To wCount - 1
                Dim mc As Integer = If(i < morphCountArr.Length, morphCountArr(i), 0)
                Dim dx = CSng(fgWorld(i).X - ourWorld(i).X)
                Dim dy = CSng(fgWorld(i).Y - ourWorld(i).Y)
                Dim dz = CSng(fgWorld(i).Z - ourWorld(i).Z)
                Dim mag = CSng(Math.Sqrt(dx * dx + dy * dy + dz * dz))
                If Not bucketN.ContainsKey(mc) Then
                    bucketN(mc) = 0
                    bucketSumSq(mc) = 0
                    bucketMax(mc) = 0
                End If
                bucketN(mc) += 1
                bucketSumSq(mc) += CDbl(mag) * CDbl(mag)
                If mag > bucketMax(mc) Then bucketMax(mc) = mag
            Next
            Dim bucketKeys = bucketN.Keys.OrderBy(Function(k) k).ToList()
            NpcPreviewLog.Log($"  [FACEGEN-DIAG-WORLD] world diff BUCKETED by vertex-morph count:")
            For Each k In bucketKeys
                Dim nInBucket = bucketN(k)
                Dim rmsBucket As Double = Math.Sqrt(bucketSumSq(k) / Math.Max(1, nInBucket))
                Dim maxBucket As Single = bucketMax(k)
                Dim interpretation As String = ""
                If k = 0 Then
                    interpretation = If(rmsBucket < 0.0005, " [FMRS OK]", " [FMRS BUG]")
                End If
                NpcPreviewLog.Log($"    bucket morphCount={k}: N={nInBucket} RMS={rmsBucket.ToString("F4")} maxDiff={maxBucket.ToString("F4")}{interpretation}")
            Next

            ' Group by PRIMARY bone (the bone with the largest weight per vertex).
            ' This lets us see:
            '   - Body bones (Neck/Chest/Collarbone/Spine): known bug with body morph propagation.
            '   - HEAD bone: should have ~0 diff (no FMRS on it).
            '   - Face bones with FMRS: diff reflects FMRS application accuracy.
            Try
                Dim ourGeoWeights = ourMesh.MeshData.Meshgeometry
                Dim gpuIdx = ourGeoWeights.GPUBoneIndices
                Dim gpuWgt = ourGeoWeights.GPUBoneWeights
                Dim shapeBones = ourMesh.MeshData.Shape.ShapeBones
                Dim boneNames As New List(Of String)
                If shapeBones IsNot Nothing Then
                    For Each bn In shapeBones
                        boneNames.Add(If(bn?.Name Is Nothing, "?", bn.Name.String))
                    Next
                End If
                Dim byPrimaryBone As New Dictionary(Of String, (N As Integer, SumSq As Double, MaxMag As Single))
                For i = 0 To wCount - 1
                    Dim primaryName As String = "?"
                    Dim primaryWgt As Single = 0
                    If gpuIdx IsNot Nothing AndAlso gpuWgt IsNot Nothing Then
                        Dim baseIdx = i * 4
                        For w = 0 To 3
                            If baseIdx + w < gpuWgt.Length Then
                                Dim ww = gpuWgt(baseIdx + w)
                                If ww > primaryWgt Then
                                    primaryWgt = ww
                                    Dim bi = CInt(gpuIdx(baseIdx + w))
                                    primaryName = If(bi < boneNames.Count, boneNames(bi), "?")
                                End If
                            End If
                        Next
                    End If
                    Dim dx = CSng(fgWorld(i).X - ourWorld(i).X)
                    Dim dy = CSng(fgWorld(i).Y - ourWorld(i).Y)
                    Dim dz = CSng(fgWorld(i).Z - ourWorld(i).Z)
                    Dim mag = CSng(Math.Sqrt(dx * dx + dy * dy + dz * dz))
                    Dim cur As (N As Integer, SumSq As Double, MaxMag As Single)
                    If byPrimaryBone.TryGetValue(primaryName, cur) Then
                        cur.N += 1
                        cur.SumSq += CDbl(mag) * CDbl(mag)
                        If mag > cur.MaxMag Then cur.MaxMag = mag
                        byPrimaryBone(primaryName) = cur
                    Else
                        byPrimaryBone(primaryName) = (1, CDbl(mag) * CDbl(mag), mag)
                    End If
                Next
                NpcPreviewLog.Log($"  --- DIFF by PRIMARY BONE (bone with largest weight per vertex) ---")
                For Each kvp In byPrimaryBone.OrderByDescending(Function(x) Math.Sqrt(x.Value.SumSq / Math.Max(1, x.Value.N)))
                    Dim rmsPB As Double = Math.Sqrt(kvp.Value.SumSq / Math.Max(1, kvp.Value.N))
                    NpcPreviewLog.Log($"    primaryBone='{kvp.Key}' N={kvp.Value.N} RMS={rmsPB.ToString("F4")} maxDiff={kvp.Value.MaxMag.ToString("F4")}")
                Next

                ' Per-vertex detail for focus bones: neck/jaw/chin FMRS bones where the residual
                ' diff is concentrated. Dump ours/bake/diff per-axis + all bone weights so we can
                ' see if the diff is a sign flip, wrong scale, or bad bind pose.
                ' Per-bone REST + WORLD comparison: dump for each bone referenced by the bake
                ' its bind pose in dict Original, dict GetGlobal (post-pose), and bake NIF's own bind.
                NpcPreviewLog.Log($"  --- BONE COMPARISON (rest=Original, world=post-pose, bake-NIF=bake's own bind) ---")
                For Each boneName In bakeBones.OrderBy(Function(s) s)
                    Dim dictBone As Skeleton_Class.HierarchiBone_class = Nothing
                    Dim restTxt As String = "not-in-dict"
                    Dim worldTxt As String = "-"
                    If Skeleton_Class.SkeletonDictionary.TryGetValue(boneName, dictBone) Then
                        Dim rest = dictBone.OriginalGetGlobalTransform
                        restTxt = $"T=({rest.Translation.X:F4},{rest.Translation.Y:F4},{rest.Translation.Z:F4}) S={rest.Scale:F4}"
                        Dim world = dictBone.GetGlobalTransform
                        worldTxt = $"T=({world.Translation.X:F4},{world.Translation.Y:F4},{world.Translation.Z:F4}) S={world.Scale:F4}"
                    End If
                    Dim bakeTxt As String = "not-in-bake"
                    For Each bn In fgShape.ShapeBones
                        Dim bnName = If(bn?.Name?.String, "")
                        If String.Equals(bnName, boneName, StringComparison.OrdinalIgnoreCase) Then
                            Dim bakeGT = Transform_Class.GetGlobalTransform(bn, baked)
                            bakeTxt = $"T=({bakeGT.Translation.X:F4},{bakeGT.Translation.Y:F4},{bakeGT.Translation.Z:F4}) S={bakeGT.Scale:F4}"
                            Exit For
                        End If
                    Next
                    NpcPreviewLog.Log($"    [BONE-COMP] '{boneName}' rest={restTxt} | world={worldTxt} | bakeNIF={bakeTxt}")
                Next

                ' Per-vertex diff — ALL verts (sorted by mag desc), no focus filter.
                NpcPreviewLog.Log($"  --- ALL VERTEX DIFFS (sorted by magnitude desc) ---")
                Dim vertDiffs As New List(Of (Idx As Integer, Primary As String, Mag As Single, Dx As Single, Dy As Single, Dz As Single, Ox As Single, Oy As Single, Oz As Single, Bx As Single, ByV As Single, Bz As Single, Weights As String))
                For i = 0 To wCount - 1
                    Dim primaryName As String = "?"
                    Dim primaryWgt As Single = 0
                    Dim weightsDesc As New System.Text.StringBuilder()
                    If gpuIdx IsNot Nothing AndAlso gpuWgt IsNot Nothing Then
                        Dim baseIdx = i * 4
                        For w = 0 To 3
                            If baseIdx + w < gpuWgt.Length Then
                                Dim ww = gpuWgt(baseIdx + w)
                                Dim bi = CInt(gpuIdx(baseIdx + w))
                                Dim bnam = If(bi < boneNames.Count, boneNames(bi), "?")
                                If ww > primaryWgt Then
                                    primaryWgt = ww
                                    primaryName = bnam
                                End If
                                If ww > 0.001F Then
                                    If weightsDesc.Length > 0 Then weightsDesc.Append(", ")
                                    weightsDesc.Append($"{bnam}:{ww:F3}")
                                End If
                            End If
                        Next
                    End If
                    Dim ox = CSng(ourWorld(i).X), oy = CSng(ourWorld(i).Y), oz = CSng(ourWorld(i).Z)
                    Dim bx = CSng(fgWorld(i).X), byv = CSng(fgWorld(i).Y), bz = CSng(fgWorld(i).Z)
                    Dim dx = bx - ox, dy = byv - oy, dz = bz - oz
                    Dim mag = CSng(Math.Sqrt(dx * dx + dy * dy + dz * dz))
                    vertDiffs.Add((i, primaryName, mag, dx, dy, dz, ox, oy, oz, bx, byv, bz, weightsDesc.ToString()))
                Next
                vertDiffs.Sort(Function(a, b) b.Mag.CompareTo(a.Mag))
                For Each v In vertDiffs
                    NpcPreviewLog.Log($"    [FG-VERT] idx={v.Idx} primary='{v.Primary}' mag={v.Mag:F4} ours=({v.Ox:F4},{v.Oy:F4},{v.Oz:F4}) bake=({v.Bx:F4},{v.ByV:F4},{v.Bz:F4}) diff=({v.Dx:+0.000;-0.000;0.000},{v.Dy:+0.000;-0.000;0.000},{v.Dz:+0.000;-0.000;0.000}) weights=[{v.Weights}]")
                Next
            Catch ex As Exception
                NpcPreviewLog.Log($"  [FACEGEN-DIAG-WORLD] primary-bone log failed: {ex.Message}")
            End Try

            ' Bind pose dump for neck-chain bones: verify if our SkeletonDict's OriginalGetGlobalTransform
            ' matches the bake NIF's GetGlobalTransform for the same named bone. If binds differ,
            ' the verts weighted to that bone can't match even with identical skinning formula.
            Try
                Dim chainToCheck = {"Neck_skin", "Neck1_skin", "Neck_Low_skin", "HEAD", "Head_skin", "Chest_skin",
                                    "skin_bone_L_Ear", "skin_bone_R_Ear", "LArm_Collarbone_skin", "RArm_Collarbone_skin",
                                    "skin_bone_L_Eye", "skin_bone_R_Eye",
                                    "Neck", "Chest", "SPINE2", "SPINE1", "COM", "Root"}
                NpcPreviewLog.Log($"  --- BIND POSE DIFF (ours dict vs bake NIF per bone) ---")
                Dim bakeBoneByName As New Dictionary(Of String, NiflySharp.Blocks.NiNode)(StringComparer.OrdinalIgnoreCase)
                For Each bn In fgShape.ShapeBones
                    Dim nm = If(bn?.Name?.String, "")
                    If nm <> "" AndAlso Not bakeBoneByName.ContainsKey(nm) Then bakeBoneByName(nm) = bn
                Next
                ' Walk our dict's skeleton chain from HEAD up to the root (Parent=Nothing).
                ' Shows which bone is the root (COM? BSFadeNode? Root?) so we know where to apply
                ' race Height. Reveals also the full hierarchy length.
                Try
                    For Each startBoneName In {"HEAD", "Neck_skin", "Chest_skin"}
                        Dim curr As Skeleton_Class.HierarchiBone_class = Nothing
                        If Skeleton_Class.SkeletonDictionary.TryGetValue(startBoneName, curr) Then
                            Dim chainDepth As Integer = 0
                            While curr IsNot Nothing AndAlso chainDepth < 30
                                Dim lt = curr.OriginalLocaLTransform
                                Dim ltDesc = If(lt Is Nothing, "null",
                                    $"LT=({lt.Translation.X:F3},{lt.Translation.Y:F3},{lt.Translation.Z:F3}) scale={lt.Scale:F4}")
                                NpcPreviewLog.Log($"    [DICT-CHAIN-{startBoneName}] depth={chainDepth} bone='{curr.BoneName}' {ltDesc}")
                                curr = curr.Parent
                                chainDepth += 1
                            End While
                        End If
                    Next
                Catch ex As Exception
                    NpcPreviewLog.Log($"    [DICT-CHAIN] walk failed: {ex.Message}")
                End Try

                ' Walk parent chain of the bakedHead NIF shape up to root: dump each NiNode's scale + translation.
                ' If any level has scale ≠ 1.0 or a translation matching our offsets, we've found the 0.98.
                Try
                    Dim currNode As NiflySharp.Blocks.NiNode = baked.GetParentNode(bakedHead)
                    Dim depth As Integer = 0
                    While currNode IsNot Nothing AndAlso depth < 10
                        Dim nm = If(currNode.Name?.String, "(no-name)")
                        NpcPreviewLog.Log($"    [BAKE-CHAIN] depth={depth} node='{nm}' T=({currNode.Translation.X:F4},{currNode.Translation.Y:F4},{currNode.Translation.Z:F4}) scale={currNode.Scale:F6}")
                        Dim nextParent As NiflySharp.Blocks.NiNode = Nothing
                        Try : nextParent = baked.GetParentNode(currNode) : Catch : End Try
                        If nextParent Is Nothing Then Exit While
                        currNode = nextParent
                        depth += 1
                    End While
                Catch ex As Exception
                    NpcPreviewLog.Log($"    [BAKE-CHAIN] walk failed: {ex.Message}")
                End Try

                ' Also log the NiNode.Scale of each bone in the bake (if per-node scale != 1 we've found it).
                For Each kv In bakeBoneByName
                    Dim bn = kv.Value
                    NpcPreviewLog.Log($"    [BAKE-BONE-SCALE] '{kv.Key}' nodeScale={bn.Scale:F6} nodeT=({bn.Translation.X:F4},{bn.Translation.Y:F4},{bn.Translation.Z:F4})")
                Next

                For Each boneName In chainToCheck
                    Dim dictBone As Skeleton_Class.HierarchiBone_class = Nothing
                    Dim hasDict = Skeleton_Class.SkeletonDictionary.TryGetValue(boneName, dictBone)
                    Dim ourGlobalDesc As String = "not-in-dict"
                    Dim ourLocalDesc As String = "-"
                    Dim ourParentDesc As String = "-"
                    If hasDict Then
                        Dim gt = dictBone.OriginalGetGlobalTransform
                        Dim lt = dictBone.OriginalLocaLTransform
                        ourGlobalDesc = $"T=({gt.Translation.X:F4},{gt.Translation.Y:F4},{gt.Translation.Z:F4})"
                        If lt IsNot Nothing Then ourLocalDesc = $"LT=({lt.Translation.X:F4},{lt.Translation.Y:F4},{lt.Translation.Z:F4})"
                        ourParentDesc = $"parent='{If(dictBone.Parent?.BoneName, "(root)")}'"
                    End If
                    Dim bakeBoneNode As NiflySharp.Blocks.NiNode = Nothing
                    Dim hasBake = bakeBoneByName.TryGetValue(boneName, bakeBoneNode)
                    Dim bakeGlobalDesc As String = "not-in-bake"
                    Dim bakeLocalDesc As String = "-"
                    Dim bakeParentDesc As String = "-"
                    If hasBake Then
                        Dim gt = Transform_Class.GetGlobalTransform(bakeBoneNode, baked)
                        bakeGlobalDesc = $"T=({gt.Translation.X:F4},{gt.Translation.Y:F4},{gt.Translation.Z:F4})"
                        bakeLocalDesc = $"LT=({bakeBoneNode.Translation.X:F4},{bakeBoneNode.Translation.Y:F4},{bakeBoneNode.Translation.Z:F4})"
                        Try
                            Dim par = baked.GetParentNode(bakeBoneNode)
                            bakeParentDesc = $"parent='{If(par?.Name?.String, "(root)")}'"
                        Catch
                            bakeParentDesc = "parent=err"
                        End Try
                    End If
                    NpcPreviewLog.Log($"    [BIND-CHECK] bone='{boneName}' ours: {ourGlobalDesc} {ourLocalDesc} {ourParentDesc} | bake: {bakeGlobalDesc} {bakeLocalDesc} {bakeParentDesc}")
                Next
            Catch ex As Exception
                NpcPreviewLog.Log($"  [BIND-CHECK] failed: {ex.Message}")
            End Try

            ' TriHead vs Plan coverage check: list morphs present in the .tri but NOT in headPlan.
            ' For each missing morph, report how many verts have a non-zero delta (threshold 0.001)
            ' and the top-3 verts with largest delta. Cross-reference with bucket 0 (no vertex morphs)
            ' to see if applying the missing morph would fix the bucket-0 residual.
            Try
                Dim triHeadPath As String = If(state.IsFemale,
                    "meshes\actors\character\characterassets\basefemaleheadchargen.tri",
                    "meshes\actors\character\characterassets\basemaleheadchargen.tri")
                Dim triLoc As FilesDictionary_class.File_Location = Nothing
                If FilesDictionary_class.Dictionary.TryGetValue(triHeadPath, triLoc) Then
                    Dim triBytes = triLoc.GetBytes()
                    If triBytes IsNot Nothing AndAlso triBytes.Length > 0 Then
                        Dim triH = TriHeadParser.ParseTriHeadFromBytes(triBytes)
                        If triH IsNot Nothing Then
                            Dim planNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                            If headPlan IsNot Nothing AndAlso headPlan.Channels IsNot Nothing Then
                                For Each ch In headPlan.Channels
                                    If Not String.IsNullOrEmpty(ch.Name) Then planNames.Add(ch.Name)
                                Next
                            End If

                            Dim bucket0 As New HashSet(Of Integer)
                            For i = 0 To Math.Min(morphCountPerVert.Length, wCount) - 1
                                If morphCountPerVert(i) = 0 Then bucket0.Add(i)
                            Next

                            Dim inPlanCount As Integer = 0
                            For Each m In triH.Morphs
                                If planNames.Contains(m.Name) Then inPlanCount += 1
                            Next
                            NpcPreviewLog.Log($"  [TRI-VS-PLAN] tri='{triHeadPath}' morphs={triH.Morphs.Count} inPlan={inPlanCount} missing={triH.Morphs.Count - inPlanCount}")
                            NpcPreviewLog.Log($"  [TRI-VS-PLAN] bucket0 verts={bucket0.Count} (verts WITHOUT any morph in our plan)")

                            For Each m In triH.Morphs
                                If planNames.Contains(m.Name) Then Continue For
                                If m.Vertices Is Nothing OrElse m.Vertices.Length = 0 Then Continue For
                                Dim touched As Integer = 0
                                Dim touchedInBucket0 As Integer = 0
                                Dim top3 As New List(Of (Idx As Integer, Mag As Single))
                                Dim morphMaxMag As Single = 0
                                For i = 0 To m.Vertices.Length - 1
                                    Dim d = m.Vertices(i)
                                    Dim mag = CSng(Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z))
                                    If mag > 0.001F Then
                                        touched += 1
                                        If bucket0.Contains(i) Then touchedInBucket0 += 1
                                        If mag > morphMaxMag Then morphMaxMag = mag
                                        If top3.Count < 3 Then
                                            top3.Add((i, mag))
                                            top3.Sort(Function(a, b) b.Mag.CompareTo(a.Mag))
                                        ElseIf mag > top3(2).Mag Then
                                            top3(2) = (i, mag)
                                            top3.Sort(Function(a, b) b.Mag.CompareTo(a.Mag))
                                        End If
                                    End If
                                Next
                                If touched > 0 Then
                                    Dim top3Str = String.Join(" | ", top3.Select(Function(t) $"idx={t.Idx} mag={t.Mag:F3}"))
                                    NpcPreviewLog.Log($"    [TRI-MISSING] morph='{m.Name}' touchesVerts={touched} ofWhichBucket0={touchedInBucket0} maxMag={morphMaxMag:F4} top3=[{top3Str}]")
                                End If
                            Next
                        End If
                    End If
                Else
                    NpcPreviewLog.Log($"  [TRI-VS-PLAN] could not find '{triHeadPath}' in FilesDictionary")
                End If
            Catch ex As Exception
                NpcPreviewLog.Log($"  [TRI-VS-PLAN] failed: {ex.Message}")
            End Try
        Catch ex As Exception
            NpcPreviewLog.Log($"  [FACEGEN-DIAG-WORLD] world compare failed: {ex.Message}")
        End Try

        ' Histogram of diff magnitudes — tells us if diff is concentrated in few verts (localized
        ' bug) or spread across many verts (global transform bug).
        Dim nExact As Integer = 0     ' diff < 0.001
        Dim nTiny As Integer = 0      ' 0.001..0.01
        Dim nSmall As Integer = 0     ' 0.01..0.05
        Dim nModerate As Integer = 0  ' 0.05..0.1
        Dim nLarge As Integer = 0     ' 0.1..0.3
        Dim nXLarge As Integer = 0    ' 0.3..0.5
        Dim nHuge As Integer = 0      ' >= 0.5
        For Each d In diffs
            Dim m = d.Mag
            If m < 0.001F Then nExact += 1
            If m >= 0.001F AndAlso m < 0.01F Then nTiny += 1
            If m >= 0.01F AndAlso m < 0.05F Then nSmall += 1
            If m >= 0.05F AndAlso m < 0.1F Then nModerate += 1
            If m >= 0.1F AndAlso m < 0.3F Then nLarge += 1
            If m >= 0.3F AndAlso m < 0.5F Then nXLarge += 1
            If m >= 0.5F Then nHuge += 1
        Next
        NpcPreviewLog.Log($"  [FACEGEN-DIAG] diff histogram: exact(<0.001)={nExact} tiny(<0.01)={nTiny} small(<0.05)={nSmall} moderate(<0.1)={nModerate} large(<0.3)={nLarge} xlarge(<0.5)={nXLarge} huge(>=0.5)={nHuge}")

        ' Access NifLocalVertices (base pre-morph) for delta logging.
        Dim baseVerts = ourMesh.MeshData.Meshgeometry.NifLocalVertices

        ' Prioritize simple verts (morphCount <= 1) with largest diffs.
        Dim simpleVertDiffs As New List(Of (Idx As Integer, Mag As Single, MCount As Integer))
        For Each d In diffs
            If d.Idx < count AndAlso morphCountPerVert(d.Idx) <= 1 Then
                simpleVertDiffs.Add((d.Idx, d.Mag, morphCountPerVert(d.Idx)))
            End If
        Next
        simpleVertDiffs.Sort(Function(a, b) b.Mag.CompareTo(a.Mag))
        Dim simpleTopN = Math.Min(10, simpleVertDiffs.Count)
        NpcPreviewLog.Log($"  [FACEGEN-DIAG] SIMPLE-VERT TOP (morphCount<=1, likely pure base/single-morph bug):")
        For k = 0 To simpleTopN - 1
            Dim sv = simpleVertDiffs(k)
            Dim bvBase = If(baseVerts IsNot Nothing AndAlso sv.Idx < baseVerts.Length, baseVerts(sv.Idx), New OpenTK.Mathematics.Vector3d(0, 0, 0))
            Dim d = diffs.FirstOrDefault(Function(x) x.Idx = sv.Idx)
            Dim contrib As String = "(no morph)"
            If sv.MCount = 1 AndAlso headPlan IsNot Nothing AndAlso headPlan.Channels IsNot Nothing Then
                For Each ch In headPlan.Channels
                    If ch.Deltas Is Nothing Then Continue For
                    For Each dx In ch.Deltas
                        If dx.index = CUInt(sv.Idx) Then
                            Dim appliedX = dx.PosDiff.X * ch.Weight
                            Dim appliedY = dx.PosDiff.Y * ch.Weight
                            Dim appliedZ = dx.PosDiff.Z * ch.Weight
                            contrib = $"{ch.Name}(w={ch.Weight.ToString("F3")},rawDelta=({dx.PosDiff.X.ToString("+0.000;-0.000;0.000")},{dx.PosDiff.Y.ToString("+0.000;-0.000;0.000")},{dx.PosDiff.Z.ToString("+0.000;-0.000;0.000")}),applied=({appliedX.ToString("+0.000;-0.000;0.000")},{appliedY.ToString("+0.000;-0.000;0.000")},{appliedZ.ToString("+0.000;-0.000;0.000")}))"
                            Exit For
                        End If
                    Next
                Next
            End If
            Dim ourDx = d.Ox - CSng(bvBase.X)
            Dim ourDy = d.Oy - CSng(bvBase.Y)
            Dim ourDz = d.Oz - CSng(bvBase.Z)
            Dim bakedDx = d.Bx - CSng(bvBase.X)
            Dim bakedDy = d.By - CSng(bvBase.Y)
            Dim bakedDz = d.Bz - CSng(bvBase.Z)
            NpcPreviewLog.Log($"    [FACEGEN-DIAG] simple-top{k + 1} idx={sv.Idx} morphs={sv.MCount} diff={sv.Mag:F4} base=({bvBase.X:F3},{bvBase.Y:F3},{bvBase.Z:F3}) ourDelta=({ourDx.ToString("+0.000;-0.000;0.000")},{ourDy.ToString("+0.000;-0.000;0.000")},{ourDz.ToString("+0.000;-0.000;0.000")}) bakedDelta=({bakedDx.ToString("+0.000;-0.000;0.000")},{bakedDy.ToString("+0.000;-0.000;0.000")},{bakedDz.ToString("+0.000;-0.000;0.000")}) morph={contrib}")
        Next

        Dim topN = Math.Min(20, diffs.Count)
        For k = 0 To topN - 1
            Dim e = diffs(k)
            Dim morphList As String = ""
            Dim deltaInfo As String = ""
            If baseVerts IsNot Nothing AndAlso e.Idx < baseVerts.Length Then
                Dim bvBase = baseVerts(e.Idx)
                Dim oursDx = e.Ox - CSng(bvBase.X)
                Dim oursDy = e.Oy - CSng(bvBase.Y)
                Dim oursDz = e.Oz - CSng(bvBase.Z)
                Dim bakedDx = e.Bx - CSng(bvBase.X)
                Dim bakedDy = e.By - CSng(bvBase.Y)
                Dim bakedDz = e.Bz - CSng(bvBase.Z)
                deltaInfo = $" base=({bvBase.X:F2},{bvBase.Y:F2},{bvBase.Z:F2}) ourDelta=({oursDx:+0.000;-0.000;0.000},{oursDy:+0.000;-0.000;0.000},{oursDz:+0.000;-0.000;0.000}) bakedDelta=({bakedDx:+0.000;-0.000;0.000},{bakedDy:+0.000;-0.000;0.000},{bakedDz:+0.000;-0.000;0.000})"
            End If
            If headPlan IsNot Nothing AndAlso headPlan.Channels IsNot Nothing Then
                Dim contributions As New List(Of String)
                For Each ch In headPlan.Channels
                    If ch.Deltas Is Nothing Then Continue For
                    Dim foundMatch As Boolean = False
                    Dim deltaX As Single = 0, deltaY As Single = 0, deltaZ As Single = 0
                    For Each d In ch.Deltas
                        If d.index = CUInt(e.Idx) Then
                            foundMatch = True
                            deltaX = d.PosDiff.X : deltaY = d.PosDiff.Y : deltaZ = d.PosDiff.Z
                            Exit For
                        End If
                    Next
                    If Not foundMatch Then Continue For
                    Dim appliedX As Single = deltaX * ch.Weight
                    Dim appliedY As Single = deltaY * ch.Weight
                    Dim appliedZ As Single = deltaZ * ch.Weight
                    contributions.Add($"{ch.Name}(w={ch.Weight.ToString("F3")},applied=({appliedX.ToString("+0.000;-0.000;0.000")},{appliedY.ToString("+0.000;-0.000;0.000")},{appliedZ.ToString("+0.000;-0.000;0.000")}))")
                Next
                If contributions.Count > 0 Then morphList = " morphs=[" & String.Join(" | ", contributions) & "]"
            End If
            NpcPreviewLog.Log($"    [FACEGEN-DIAG] top{k + 1} idx={e.Idx} diff={e.Mag:F4}{deltaInfo}{morphList}")
        Next
    End Sub

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

        For Each tl In npcData.FaceTintLayers
            Dim opt = race.FindTintOption(tl.Index, state.IsFemale)
            Dim rawOptFlagsU = If(opt IsNot Nothing, opt.Flags, CUShort(0))
            Dim rawOptFlagsHex = If(opt IsNot Nothing, $"0x{opt.Flags:X4}", "?")
            Dim rawOptFlagsName = If(opt IsNot Nothing, FormatTintFlagsName(opt.Flags), "?")

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

            ' DIAGNOSTIC: dump raw TEND bytes so we can verify the real layout vs xEdit/CK.
            If tl.RawTendBytes IsNot Nothing AndAlso tl.RawTendBytes.Length > 0 Then
                Dim hex As New System.Text.StringBuilder()
                For i As Integer = 0 To tl.RawTendBytes.Length - 1
                    If i > 0 Then hex.Append(",")
                    hex.Append($"0x{tl.RawTendBytes(i):X2}")
                Next
                Dim unusedByte As String = "N/A"
                Dim tplLo As String = "N/A"
                Dim tplHi As String = "N/A"
                Dim unusedFlag As String = ""
                If tl.RawTendBytes.Length >= 5 Then
                    unusedByte = $"0x{tl.RawTendBytes(4):X2}"
                    If tl.RawTendBytes(4) <> 0 Then unusedFlag = " *** UNUSED-BYTE NON-ZERO ***"
                End If
                If tl.RawTendBytes.Length >= 7 Then
                    tplLo = $"0x{tl.RawTendBytes(5):X2}"
                    tplHi = $"0x{tl.RawTendBytes(6):X2}"
                End If
                NpcPreviewLog.Log($"      [TEND-RAW] disc={tl.Discriminator} optName={opt.Name} TETI.Index={tl.Index} len={tl.RawTendBytes.Length} bytes=[{hex.ToString()}] | Value=0x{tl.RawTendBytes(0):X2}({tl.Value}) R=0x{If(tl.RawTendBytes.Length >= 2, tl.RawTendBytes(1), CByte(0)):X2} G=0x{If(tl.RawTendBytes.Length >= 3, tl.RawTendBytes(2), CByte(0)):X2} B=0x{If(tl.RawTendBytes.Length >= 4, tl.RawTendBytes(3), CByte(0)):X2} Unused(b4)={unusedByte} TplLo(b5)={tplLo} TplHi(b6)={tplHi} TplIdx={tl.TemplateColorIndex}{unusedFlag}")
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
                ' Palette: greyscale mask in .r. The effective colour, blendOp and opacity scale
                ' are resolved by ResolvePaletteLayerEffective. Lookup is by VALUE of TTEC entry's
                ' TemplateIndex field matching TEND.TemplateColorIndex (not by array position).
                ' On match: CLFM colour + preset BlendOp + preset Alpha (opacity multiplier).
                ' On no match (CUSTOM): tendRGB + ResolveFallbackBlendOp(opt) + opacityScale 1.0.
                layerInput.Kind = FaceTintLayerKind.PaletteMask
                Dim resolved = ResolvePaletteLayerEffective(tl, opt)
                layerInput.R = resolved.Color.R
                layerInput.G = resolved.Color.G
                layerInput.B = resolved.Color.B
                layerInput.BlendOp = CInt(resolved.BlendOp)
                ' Runtime opacity: NPC.Value (slider) × TTEC.Alpha when preset matched; just NPC.Value on CUSTOM.
                Dim effectiveOpacity As Single = opacity * resolved.OpacityScale
                layerInput.Opacity = effectiveOpacity
                Dim resolveMode As String = If(resolved.Matched, "PRESET (match TTEC.TemplateIndex)", "CUSTOM (no match — tendRGB + TTEC(1).BlendOp)")
                NpcPreviewLog.Log($"      -> Palette resolve: mode={resolveMode} TemplateColorIndex={tl.TemplateColorIndex} tendRGB=({tl.Color.R},{tl.Color.G},{tl.Color.B}) effectiveRGB=({resolved.Color.R},{resolved.Color.G},{resolved.Color.B}) blendOp={resolved.BlendOp}({BlendOpName(resolved.BlendOp)}) opt.TTEB={opt.BlendOperation}({BlendOpName(opt.BlendOperation)}) NPC.Value={opacity:F2} tplAlpha={resolved.OpacityScale:F2} effOpacity={effectiveOpacity:F2}")
                If opt IsNot Nothing AndAlso opt.TemplateColors IsNot Nothing AndAlso opt.TemplateColors.Count > 0 Then
                    Dim sb As New System.Text.StringBuilder()
                    For i = 0 To opt.TemplateColors.Count - 1
                        Dim tc = opt.TemplateColors(i)
                        Dim rgbStr As String = "(?)"
                        If tc.ColorFormID <> 0UI AndAlso _pluginManager IsNot Nothing Then
                            Dim cr = _pluginManager.GetRecord(tc.ColorFormID)
                            If cr IsNot Nothing AndAlso cr.Header.Signature = "CLFM" Then
                                Dim cc = RecordParsers.ParseCLFM(cr, _pluginManager)
                                If cc IsNot Nothing AndAlso cc.HasColor Then
                                    rgbStr = $"({cc.Color.R},{cc.Color.G},{cc.Color.B})"
                                End If
                            End If
                        End If
                        If i > 0 Then sb.Append(" | ")
                        sb.Append($"[pos={i} TemplateIndex={tc.TemplateIndex} CLFM={tc.ColorFormID:X8} rgb={rgbStr} blendOp={tc.BlendOperation}]")
                    Next
                    NpcPreviewLog.Log($"      -> TTEC list ({opt.TemplateColors.Count} entries): {sb}")
                End If
            ElseIf tl.Discriminator = 2 Then
                ' TextureSet: pre-coloured RGBA. TTEB (opt.BlendOperation) is almost always empty
                ' in vanilla data, so we apply the same fallback used for disc=1 CUSTOM:
                ' prefer TemplateColors(1).BlendOperation (first real preset — skips pos=0 "None/Nada"
                ' placeholder); fall back to TTEC(0), and only then to opt.BlendOperation.
                layerInput.Kind = FaceTintLayerKind.TextureSetDiffuse
                layerInput.BlendOp = CInt(ResolveFallbackBlendOp(opt))
                NpcPreviewLog.Log($"      -> TextureSet resolve: blendOp={layerInput.BlendOp}({BlendOpName(CUInt(layerInput.BlendOp))}) opt.TTEB={opt.BlendOperation}({BlendOpName(opt.BlendOperation)}) TTEC.Count={If(opt.TemplateColors IsNot Nothing, opt.TemplateColors.Count, 0)} opacity={opacity:F2}")
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

    ''' <summary>Resolve a Palette face tint layer's effective colour and blend operation.
    '''
    ''' Rule verified against vanilla FO4.esm vs modded Cait (xEdit diff screenshot):
    '''   - Vanilla Lápiz-de-labios: TemplateColorIndex=1824, RGB=(0,0,0) → preset.
    '''   - Mod Lápiz-de-labios:     TemplateColorIndex=0,    RGB=(103,5,5) → custom.
    '''
    ''' The TemplateColorIndex in TEND is NOT a positional array index. Each TTEC entry has its
    ''' own 'Template Index' (U16, spec wbDefinitionsFO4.pas:3507). TEND.TemplateColorIndex is
    ''' matched by VALUE against TTEC[i].TemplateIndex.
    '''
    '''   - Match found:  color = CLFM color (via ColorFormID); blendOp = TTEC[i].BlendOperation.
    '''   - No match:     custom color — color = tl.Color (tendRGB); blendOp = opt.BlendOperation (TTEB).
    '''   - TplIdx = -1:  no preset (discriminator=2 / disc without color); custom path.
    '''
    ''' TTEC.Alpha is NOT used as a runtime opacity multiplier. The NPC's TEND.Value is the sole
    ''' authoritative opacity source — the slider IS the opacity.</summary>
    ''' <summary>Fallback BlendOp used whenever no preset match is available (disc=1 CUSTOM,
    ''' or disc=2 TextureSet). Rule: TTEC pos=0 is the "None/Nada" placeholder (Default blend);
    ''' the first real preset at pos=1 carries the authored BlendOp (usually SoftLight). The
    ''' option-level TTEB (opt.BlendOperation) is almost always empty in vanilla data, so it's
    ''' a last-resort fallback, not a primary source.</summary>
    Private Shared Function ResolveFallbackBlendOp(opt As RACE_TintTemplateOption) As UInteger
        If opt Is Nothing Then Return 0UI
        If opt.TemplateColors IsNot Nothing AndAlso opt.TemplateColors.Count >= 2 Then
            Return opt.TemplateColors(1).BlendOperation
        ElseIf opt.TemplateColors IsNot Nothing AndAlso opt.TemplateColors.Count = 1 Then
            Return opt.TemplateColors(0).BlendOperation
        Else
            Return opt.BlendOperation
        End If
    End Function

    Private Function ResolvePaletteLayerEffective(tl As NPC_FaceTintLayerData, opt As RACE_TintTemplateOption) As (Color As Color, BlendOp As UInteger, Matched As Boolean, OpacityScale As Single)
        Dim resolvedColor As Color = tl.Color
        Dim resolvedBlendOp As UInteger = ResolveFallbackBlendOp(opt)
        Dim matched As Boolean = False
        Dim opacityScale As Single = 1.0F

        If opt IsNot Nothing Then

            If opt.TemplateColors IsNot Nothing AndAlso opt.TemplateColors.Count > 0 _
               AndAlso tl.TemplateColorIndex >= 0 Then
                Dim needle As UShort = CUShort(tl.TemplateColorIndex)
                Dim tplCol As RACE_TintTemplateColor = opt.TemplateColors.FirstOrDefault(
                    Function(t) t.TemplateIndex = needle)
                If tplCol IsNot Nothing Then
                    matched = True
                    resolvedBlendOp = tplCol.BlendOperation
                    opacityScale = tplCol.Alpha
                    If tplCol.ColorFormID <> 0UI AndAlso _pluginManager IsNot Nothing Then
                        Dim clfmRec = _pluginManager.GetRecord(tplCol.ColorFormID)
                        If clfmRec IsNot Nothing AndAlso clfmRec.Header.Signature = "CLFM" Then
                            Dim clfm = RecordParsers.ParseCLFM(clfmRec, _pluginManager)
                            If clfm IsNot Nothing AndAlso clfm.HasColor Then
                                resolvedColor = clfm.Color
                            End If
                        End If
                    End If
                End If
            End If
        End If

        Return (resolvedColor, resolvedBlendOp, matched, opacityScale)
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
        state.ObjectTemplateOMODFormIDs.AddRange(model.ObjectTemplateOMODFormIDs)
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

                FilesDictionary_class.CacheDirectory = Application.StartupPath
                _assetDictionaryLoadTask = FilesDictionary_class.Fill_DictionaryAsync(_dataPath, progress)
            End If

            loadTask = _assetDictionaryLoadTask
        End SyncLock

        Await loadTask

        Dim scanReport = FilesDictionary_class.DrainScanReport()
        If scanReport.Count > 0 Then
            Dim hits As Integer = 0
            For Each r In scanReport
                If r.CacheHit Then hits += 1
            Next
            Dim misses As Integer = scanReport.Count - hits
            NpcPreviewLog.LogSeparator($"FilesDictionary scan: {hits} cache hits, {misses} fresh reads")
            For Each r In scanReport
                NpcPreviewLog.Log($"  {If(r.CacheHit, "CACHE", "READ ")}  {r.ArchiveName}")
            Next
        End If

        ' TRI-PROBE 2026-04-19: enumerate vanilla head TRIs to resolve the male _faceBones 1696
        ' vs chargen 1690 mismatch puzzle. Loads all known head TRI variants and logs vert count
        ' + morph names so we can decide which TRI is the correct morph source for _faceBones.
        NpcPreviewLog.LogSeparator("TRI-PROBE: vanilla head TRI vertex counts and morphs")
        Dim triProbePaths = {
            "meshes\actors\character\characterassets\basemalehead.tri",
            "meshes\actors\character\characterassets\basemaleheadchargen.tri",
            "meshes\actors\character\characterassets\basemaleheadold.tri",
            "meshes\actors\character\characterassets\basefemalehead.tri",
            "meshes\actors\character\characterassets\basefemaleheadchargen.tri",
            "meshes\actors\character\characterassets\humanoidhead.tri"
        }
        For Each probePath In triProbePaths
            Dim loc As FilesDictionary_class.File_Location = Nothing
            If Not FilesDictionary_class.Dictionary.TryGetValue(probePath, loc) Then
                NpcPreviewLog.Log($"  [TRI-PROBE] '{probePath}': NOT in FilesDictionary")
                Continue For
            End If
            Try
                Dim bytes = loc.GetBytes()
                If bytes Is Nothing OrElse bytes.Length < 64 Then
                    NpcPreviewLog.Log($"  [TRI-PROBE] '{probePath}': empty/short bytes ({If(bytes Is Nothing, 0, bytes.Length)} B)")
                    Continue For
                End If
                ' Verify FRTRI003 magic
                Dim magic = System.Text.Encoding.ASCII.GetString(bytes, 0, 8)
                If Not magic.StartsWith("FRTRI") Then
                    NpcPreviewLog.Log($"  [TRI-PROBE] '{probePath}': NOT FRTRI (magic='{magic}', first bytes: {BitConverter.ToString(bytes, 0, 16)})")
                    Continue For
                End If
                ' Read 14 uint32 header fields at offset 8 to expose EVERYTHING (incl. numModifiers, numModVertices, unknowns)
                Dim h(13) As UInteger
                For k = 0 To 13
                    h(k) = BitConverter.ToUInt32(bytes, 8 + k * 4)
                Next
                Dim numVertices = h(0), numTriangles = h(1), numQuads = h(2), unk2 = h(3), unk3 = h(4)
                Dim numUV = h(5), flags = h(6), numMorphs = h(7), numModifiers = h(8), numModVertices = h(9)
                Dim unk7 = h(10), unk8 = h(11), unk9 = h(12), unk10 = h(13)
                NpcPreviewLog.Log($"  [TRI-PROBE] '{probePath}' magic='{magic}' bytes={bytes.Length}")
                NpcPreviewLog.Log($"    header: numVerts={numVertices} numTri={numTriangles} numQuads={numQuads} numUV={numUV} flags=0x{flags:X8} numMorphs={numMorphs} numModifiers={numModifiers} numModVerts={numModVertices}")
                NpcPreviewLog.Log($"    unknowns: u2={unk2} u3={unk3} u7={unk7} u8={unk8} u9={unk9} u10={unk10}")
                ' Now parse via library for morph names (splits regular vs mod)
                Dim head = TriHeadParser.ParseTriHeadFromBytes(bytes)
                If head IsNot Nothing Then
                    Dim regularNames = head.Morphs.Where(Function(m) Not m.IsModMorph).Select(Function(m) m.Name).ToList()
                    Dim modNames = head.Morphs.Where(Function(m) m.IsModMorph).Select(Function(m) m.Name).ToList()
                    NpcPreviewLog.Log($"    regular morphs ({regularNames.Count}): [{String.Join(", ", regularNames)}]")
                    If modNames.Count > 0 Then
                        NpcPreviewLog.Log($"    mod-morphs ({modNames.Count}): [{String.Join(", ", modNames)}]")
                    End If
                End If
            Catch ex As Exception
                NpcPreviewLog.Log($"  [TRI-PROBE] '{probePath}': error — {ex.Message}")
            End Try
        Next

        If InvokeRequired Then
            BeginInvoke(Sub() ToolStripProgressBar1.Visible = False)
        Else
            ToolStripProgressBar1.Visible = False
        End If
    End Function

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

        NpcPreviewLog.Log($"  [MORPH] NPC {npcData.EditorID} [{modelNpcFormID:X8}]: MorphValues={npcData.MorphValues.Count} FaceMorphs={npcData.FaceMorphs.Count} FMIN={npcData.FacialMorphIntensity:F3} Template=0x{npcData.TemplateFormID:X8} TemplateFlags=0x{npcData.TemplateFlags:X4}")

        ' Dump raw MSDK/MSDV table from this NPC (to see what keys+weights the record really has).
        ' Cross-reference each key against RACE.MSID (sliders) / MPPI (presets) / MPGS (group sliders)
        ' to show where each morph came from and why it's in the NPC.
        Dim sliderIndexSet As New HashSet(Of UInteger)
        If morphValueDefs IsNot Nothing Then
            For Each mv In morphValueDefs : sliderIndexSet.Add(mv.Index) : Next
        End If
        Dim presetIndexMap As New Dictionary(Of UInteger, String)
        If morphPresetDefs IsNot Nothing Then
            For Each mp In morphPresetDefs
                If Not presetIndexMap.ContainsKey(mp.Index) Then presetIndexMap(mp.Index) = mp.MorphName
            Next
        End If
        NpcPreviewLog.Log($"  [MORPH-RAW] NPC MSDK/MSDV table ({npcData.MorphValues.Count} entries):")
        For Each kvp In npcData.MorphValues
            Dim key = kvp.Key
            Dim value = kvp.Value
            Dim classification As String
            If sliderIndexSet.Contains(key) Then
                Dim mvDef = morphValueDefs.FirstOrDefault(Function(m) m.Index = key)
                classification = $"SLIDER (RACE.MSID) MSM0='{mvDef.MinName}' MSM1='{mvDef.MaxName}'"
            ElseIf presetIndexMap.ContainsKey(key) Then
                classification = $"PRESET (RACE.MPPI) morphName='{presetIndexMap(key)}'"
            Else
                classification = "??? (not found in RACE MSID/MPPI for this gender)"
            End If
            NpcPreviewLog.Log($"    key=0x{key:X8} weight={value:+0.0000;-0.0000;0.0000} → {classification}")
        Next

        ' Dump RACE morph structure for this gender: how many groups, and within each group how
        ' many presets and what morph name they point to. Shows whether the 4x DefaultFaceType0
        ' belongs to 4 distinct groups (as hypothesized) or something else.
        NpcPreviewLog.Log($"  [MORPH-RAW] RACE MorphGroups for {(If(state.IsFemale, "Female", "Male"))} ({If(morphGroups IsNot Nothing, morphGroups.Count, 0)} groups):")
        If morphGroups IsNot Nothing Then
            For Each g In morphGroups
                Dim presetSummary As New System.Text.StringBuilder()
                For k = 0 To g.Presets.Count - 1
                    If k > 0 Then presetSummary.Append(" | ")
                    Dim p = g.Presets(k)
                    presetSummary.Append($"MPPI=0x{p.Index:X8}[MPPN='{p.PresetName}']→MPPM='{p.MorphName}'")
                Next
                Dim slidersSummary As String = ""
                If g.SliderIndices IsNot Nothing AndAlso g.SliderIndices.Count > 0 Then
                    Dim sliderKeys = String.Join(",", g.SliderIndices.Select(Function(k) $"0x{k:X8}"))
                    slidersSummary = $" MPGS=[{sliderKeys}]"
                End If
                NpcPreviewLog.Log($"    group='{g.Name}' mask=0x{g.MaskEnum:X4} presets={g.Presets.Count}: [{presetSummary}]{slidersSummary}")
            Next
        End If

        Return New NpcMorphResolver(
            npcData,
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

    ''' <summary>TO-REVIEW 2026-04-19 — tentative fix per user empirical call.
    ''' For robot races (Assaultron, Mr Handy, etc.), RACE.ANAM declares a base `skeleton.nif` that
    ''' is empty/minimal. Actual bones needed by OMOD part meshes live in `SkeletonRef.nif` + other
    ''' sibling skeleton files in the same folder. Detection: if `<bodySkelDir>/SkeletonRef.nif`
    ''' exists, assume robot-mode and merge every `skeleton*.nif` file from that folder into
    ''' SkeletonDictionary. Humanoid races have no `SkeletonRef.nif` sibling → early return no-op.
    ''' Merge semantics: MergeFaceSkeleton uses matching bone names as anchors, adds new bones as
    ''' children. Safe to call multiple times (idempotent over already-merged names).
    ''' Follow-up: verify with a bone-diff probe whether the merge covers every bone that OMOD
    ''' meshes actually reference. See project_robot_rendering_combinations.md.</summary>
    Private Sub MergeRobotExtendedSkeletonsIfRobot(state As NPCVisualState)
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return
        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return
        Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)

        Dim bodySkel = If(state.IsFemale, race.FemaleSkeletonPath, race.MaleSkeletonPath)
        If String.IsNullOrEmpty(bodySkel) Then bodySkel = If(state.IsFemale, race.MaleSkeletonPath, race.FemaleSkeletonPath)
        If String.IsNullOrEmpty(bodySkel) Then Return

        Dim lastSep = Math.Max(bodySkel.LastIndexOf("\"c), bodySkel.LastIndexOf("/"c))
        Dim folder = If(lastSep >= 0, bodySkel.Substring(0, lastSep + 1), "")
        If folder = "" Then Return

        Dim skeletonRefKey = NormalizeDictionaryKeyWithMeshesPrefix(folder & "SkeletonRef.nif")
        If Not FilesDictionary_class.Dictionary.ContainsKey(skeletonRefKey) Then
            ' No SkeletonRef sibling → not a robot race (humanoid) → nothing to merge.
            Return
        End If

        ' Enumerate all `skeleton*.nif` files in the same folder (case-insensitive).
        Dim normalizedFolder = NormalizeDictionaryKeyWithMeshesPrefix(folder).ToLowerInvariant()
        Dim matches As New List(Of String)
        For Each key In FilesDictionary_class.Dictionary.Keys
            Dim k = key.ToLowerInvariant()
            If Not k.StartsWith(normalizedFolder) Then Continue For
            ' Match only immediate children of the folder (no deeper subfolders).
            Dim rest = k.Substring(normalizedFolder.Length)
            If rest.Contains("\"c) OrElse rest.Contains("/"c) Then Continue For
            If Not rest.EndsWith(".nif") Then Continue For
            ' Filename must start with "skeleton" (covers skeleton.nif, SkeletonRef.nif, skeletonSentryBodyPart.nif, etc.)
            If Not rest.StartsWith("skeleton") Then Continue For
            matches.Add(key)
        Next

        NpcPreviewLog.Log($"  [ROBOT-SKEL] race {race.EditorID} robot-mode (SkeletonRef found). Merging {matches.Count} sibling skeleton files from '{folder}':")
        For Each key In matches
            Dim loc As FilesDictionary_class.File_Location = Nothing
            If Not FilesDictionary_class.Dictionary.TryGetValue(key, loc) Then Continue For
            Try
                Dim bytes = loc.GetBytes()
                If bytes Is Nothing OrElse bytes.Length = 0 Then
                    NpcPreviewLog.Log($"    [ROBOT-SKEL] '{key}' empty/failed to read")
                    Continue For
                End If
                Dim added = Skeleton_Class.MergeFaceSkeleton(bytes)
                NpcPreviewLog.Log($"    [ROBOT-SKEL] '{key}' ({bytes.Length} B) → +{added} bones")
            Catch ex As Exception
                NpcPreviewLog.Log($"    [ROBOT-SKEL] '{key}' error: {ex.Message}")
            End Try
        Next
    End Sub


    ''' <summary>Isolated bake-vs-app harness (CSV-only; zero library changes; zero global state mutation).
    '''
    ''' Loads fresh copies of the body + face skeleton NIFs from disk (same sources the app normally uses:
    ''' Config_App.Current.SkeletonFilePath for body, TryLoadFaceSkeletonBytes for face), builds a local
    ''' per-bone bindT lookup by walking those fresh NIFs' NiNode hierarchies, then manually skins the
    ''' bake NIF's vertices with matsPose = matsBind (no Deltas, no Pose B, no MWGT, no FMRS). Compares
    ''' against the app's world-space render output and dumps a CSV alongside npc_preview.log.
    '''
    ''' Purpose: distinguish whether the ~0.16 RMS residual (Cait/Alijo vs CK FaceGen bake) comes from
    ''' Pose B / pipeline machinery or from elsewhere. Interpretation:
    '''   - RMS(V_raw, V_app) ≈ 0  → app matches bake via bindpose; Pose B is inocent (and unnecessary).
    '''   - RMS(V_raw, V_app) ≈ 0.16 → Pose B is not the culprit; residual is upstream in the app pipeline.
    '''   - Intermediate → datapoint; analyze bucketed by primary bone as the existing [FACEGEN-DIAG-WORLD].
    '''
    ''' Known contaminant: Alijo has MWGT.Fat > 0 and our app doesn't implement NNAM (neck-fat adjust).
    ''' Part of Alijo's Neck_skin residual is expected until NNAM is implemented. See memory P2.</summary>
    Private Sub DumpIsolatedBakeHarnessCSV(state As NPCVisualState,
                                            baked As Nifcontent_Class_Manolo,
                                            bakedHead As NiflySharp.Blocks.BSTriShape,
                                            fgShape As IRenderableShape,
                                            ByRef ourGeo As SkinnedGeometry,
                                            ourShape As IRenderableShape,
                                            morphResolver As IMorphResolver)
        Try
            NpcPreviewLog.Log($"  [HARNESS-RAW] start NPC=0x{state.FormID:X8} MWGT(thin={state.WeightThin:F3} musc={state.WeightMuscular:F3} fat={state.WeightFat:F3}) IsFemale={state.IsFemale}")

            ' 0) Race height. At render the app applies it as DeltaTransform.Scale on the Root bone
            ' (MainForm.vb near [RACE-HEIGHT-POSE]); CK does NOT bake it into FaceGen. Without
            ' reproducing it here, vRaw lives at canonical 100% while ourWorld is at raceHeight,
            ' and the diff measures race-scale instead of the morph pipeline residual.
            ' Implementation below: after loading each fresh skel NIF, multiply its root NiNode's
            ' Scale by raceHeight. Transform_Class.GetGlobalTransform walks parent→…→root composing
            ' each node's local transform, so the root scale propagates into every bone's bindT
            ' automatically (NifRenderTransformation.vb:7-20). No per-bone loop changes needed.
            Dim raceHeight As Single = 1.0F
            If state.RaceFormID <> 0UI Then
                Dim rrH = _pluginManager.GetRecord(state.RaceFormID)
                If rrH IsNot Nothing AndAlso rrH.Header.Signature = "RACE" Then
                    Dim rH = RecordParsers.ParseRACE(rrH, _pluginManager)
                    raceHeight = If(state.IsFemale, rH.FemaleHeight, rH.MaleHeight)
                    If raceHeight <= 0 Then raceHeight = 1.0F
                End If
            End If
            NpcPreviewLog.Log($"  [HARNESS-RAW] raceHeight={raceHeight:F4}")

            ' 1) Fresh body skeleton from disk — resolve path from RACE.ANAM (same source the app's normal
            ' path uses; see ResolveSkeletonDictionaryKey around MainForm.vb:4488 for the canonical pattern).
            ' Not Config_App.Current.SkeletonFilePath — that's WM-centric and empty in NPC_Manager.
            Dim skelBody As Nifcontent_Class_Manolo = Nothing
            Dim bodyDictKey As String = ""
            If state.RaceFormID <> 0UI Then
                Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
                If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
                    Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)
                    Dim rawPath = If(state.IsFemale, race.FemaleSkeletonPath, race.MaleSkeletonPath)
                    If String.IsNullOrWhiteSpace(rawPath) Then rawPath = If(race.MaleSkeletonPath <> "", race.MaleSkeletonPath, race.FemaleSkeletonPath)
                    bodyDictKey = NormalizeDictionaryKeyWithMeshesPrefix(rawPath)
                End If
            End If
            If Not String.IsNullOrEmpty(bodyDictKey) Then
                Dim loc As FilesDictionary_class.File_Location = Nothing
                If FilesDictionary_class.Dictionary.TryGetValue(bodyDictKey, loc) Then
                    Try
                        Dim bytes = loc.GetBytes()
                        If bytes IsNot Nothing AndAlso bytes.Length > 0 Then
                            skelBody = New Nifcontent_Class_Manolo()
                            skelBody.Load_Manolo(bytes)
                            NpcPreviewLog.Log($"  [HARNESS-RAW] body skel fresh via FilesDictionary '{bodyDictKey}' ({bytes.Length} B)")
                            If Math.Abs(raceHeight - 1.0F) > 0.0001F Then
                                Dim rootB = skelBody.GetRootNode()
                                If rootB IsNot Nothing Then
                                    Dim before = rootB.Scale
                                    rootB.Scale = rootB.Scale * raceHeight
                                    NpcPreviewLog.Log($"  [HARNESS-RAW] body skel root '{If(rootB.Name?.String, "")}' Scale {before:F4} → {rootB.Scale:F4}")
                                End If
                            End If
                        End If
                    Catch exB As Exception
                        NpcPreviewLog.Log($"  [HARNESS-RAW] body skel load failed for '{bodyDictKey}': {exB.Message}")
                    End Try
                Else
                    NpcPreviewLog.Log($"  [HARNESS-RAW] body skel dictKey '{bodyDictKey}' not in FilesDictionary")
                End If
            End If
            If skelBody Is Nothing Then
                NpcPreviewLog.Log($"  [HARNESS-RAW] abort — could not load body skeleton (race-derived key='{bodyDictKey}')")
                Return
            End If

            ' 2) Fresh face skeleton (reuse existing TryLoadFaceSkeletonBytes helper — same source app uses)
            Dim skelFace As Nifcontent_Class_Manolo = Nothing
            Try
                Dim faceBytes = TryLoadFaceSkeletonBytes(state)
                If faceBytes IsNot Nothing AndAlso faceBytes.Length > 0 Then
                    skelFace = New Nifcontent_Class_Manolo()
                    skelFace.Load_Manolo(faceBytes)
                    NpcPreviewLog.Log($"  [HARNESS-RAW] face skel fresh ({faceBytes.Length} B)")
                    If Math.Abs(raceHeight - 1.0F) > 0.0001F Then
                        Dim rootF = skelFace.GetRootNode()
                        If rootF IsNot Nothing Then
                            Dim beforeF = rootF.Scale
                            rootF.Scale = rootF.Scale * raceHeight
                            NpcPreviewLog.Log($"  [HARNESS-RAW] face skel root '{If(rootF.Name?.String, "")}' Scale {beforeF:F4} → {rootF.Scale:F4}")
                        End If
                    End If
                Else
                    NpcPreviewLog.Log($"  [HARNESS-RAW] face skel not available — continuing with body-only lookup")
                End If
            Catch exF As Exception
                NpcPreviewLog.Log($"  [HARNESS-RAW] face skel load failed: {exF.Message}")
            End Try

            ' 3) Build per-bone matsBind using fresh bindT (face wins over body; bake hierarchy is final fallback)
            Dim bakeBones = fgShape.ShapeBones.ToArray()
            Dim bakeLocalTs = fgShape.ShapeBoneTransforms.ToArray()
            If bakeBones.Length <> bakeLocalTs.Length Then
                NpcPreviewLog.Log($"  [HARNESS-RAW] abort — bake bones/transforms length mismatch {bakeBones.Length} vs {bakeLocalTs.Length}")
                Return
            End If
            Dim nBones = bakeBones.Length
            Dim matsBind(nBones - 1) As OpenTK.Mathematics.Matrix4d
            Dim srcFace As Integer = 0, srcBody As Integer = 0, srcBake As Integer = 0, srcMissing As Integer = 0

            For k = 0 To nBones - 1
                Dim boneName = If(bakeBones(k)?.Name?.String, "")
                Dim bindT As Transform_Class = Nothing

                If skelFace IsNot Nothing AndAlso boneName <> "" Then
                    Dim faceNode = skelFace.Blocks.OfType(Of NiflySharp.Blocks.NiNode)().FirstOrDefault(
                        Function(n) String.Equals(If(n?.Name?.String, ""), boneName, StringComparison.OrdinalIgnoreCase))
                    If faceNode IsNot Nothing Then
                        bindT = Transform_Class.GetGlobalTransform(faceNode, skelFace)
                        If bindT IsNot Nothing Then srcFace += 1
                    End If
                End If
                If bindT Is Nothing AndAlso boneName <> "" Then
                    Dim bodyNode = skelBody.Blocks.OfType(Of NiflySharp.Blocks.NiNode)().FirstOrDefault(
                        Function(n) String.Equals(If(n?.Name?.String, ""), boneName, StringComparison.OrdinalIgnoreCase))
                    If bodyNode IsNot Nothing Then
                        bindT = Transform_Class.GetGlobalTransform(bodyNode, skelBody)
                        If bindT IsNot Nothing Then srcBody += 1
                    End If
                End If
                If bindT Is Nothing Then
                    bindT = Transform_Class.GetGlobalTransform(bakeBones(k), baked)
                    If bindT IsNot Nothing Then
                        srcBake += 1
                    Else
                        bindT = New Transform_Class()
                        srcMissing += 1
                    End If
                End If

                matsBind(k) = bindT.ComposeTransforms(bakeLocalTs(k)).ToMatrix4d()
            Next
            NpcPreviewLog.Log($"  [HARNESS-RAW] bone resolution: face={srcFace} body={srcBody} bake-fallback={srcBake} missing={srcMissing} total={nBones}")

            ' 4) Shape global transform (typically Identity for FaceGen bakes; log for verification)
            Dim shapeNode = TryCast(baked.GetParentNode(bakedHead), NiflySharp.Blocks.NiNode)
            If shapeNode Is Nothing Then shapeNode = baked.GetRootNode()
            Dim shapeGlobal As OpenTK.Mathematics.Matrix4d = If(shapeNode IsNot Nothing,
                                                                 Transform_Class.GetGlobalTransform(shapeNode, baked).ToMatrix4d(),
                                                                 OpenTK.Mathematics.Matrix4d.Identity)

            ' 5) Precompute per-bone GlobalTransform × matsBind (same as SkinningHelper line 255)
            Dim precomputed(nBones - 1) As OpenTK.Mathematics.Matrix4d
            For k = 0 To nBones - 1
                precomputed(k) = shapeGlobal * matsBind(k)
            Next

            ' 6) Per-vertex skinning: blend precomputed by bone weights (mirrors BlendBoneMatrices semantics)
            Dim nifVer = baked.Header.Version
            Dim vdSSE = If(bakedHead.VertexDataSSE IsNot Nothing, bakedHead.VertexDataSSE.ToArray(), Nothing)
            Dim vdFO4 = If(bakedHead.VertexData IsNot Nothing, bakedHead.VertexData.ToArray(), Nothing)
            Dim verts = bakedHead.VertexPositions
            Dim vCount = verts.Count
            Dim vRaw(vCount - 1) As OpenTK.Mathematics.Vector3d

            For i = 0 To vCount - 1
                Dim wts As System.Half() = Nothing
                Dim bIdx As Byte() = Nothing
                If nifVer.IsSSE AndAlso vdSSE IsNot Nothing AndAlso i < vdSSE.Length Then
                    wts = vdSSE(i).BoneWeights
                    bIdx = vdSSE(i).BoneIndices
                ElseIf vdFO4 IsNot Nothing AndAlso i < vdFO4.Length Then
                    wts = vdFO4(i).BoneWeights
                    bIdx = vdFO4(i).BoneIndices
                End If

                Dim Mtot As OpenTK.Mathematics.Matrix4d = OpenTK.Mathematics.Matrix4d.Zero
                Dim sumW As Double = 0
                If wts IsNot Nothing AndAlso bIdx IsNot Nothing Then
                    Dim cnt = Math.Min(wts.Length, bIdx.Length) - 1
                    For j = 0 To cnt
                        Dim w = CDbl(CSng(wts(j)))
                        sumW += w
                        Dim idx = CInt(bIdx(j))
                        If idx >= 0 AndAlso idx < nBones Then Mtot += precomputed(idx) * w
                    Next
                End If
                If sumW = 0 Then
                    If nBones > 0 Then
                        Dim idx0 = If(bIdx IsNot Nothing AndAlso bIdx.Length > 0, CInt(bIdx(0)), 0)
                        Mtot = precomputed(Math.Max(0, Math.Min(idx0, nBones - 1)))
                    End If
                Else
                    Mtot = Mtot * (1.0 / sumW)
                End If

                Dim vLocal As New OpenTK.Mathematics.Vector3d(verts(i).X, verts(i).Y, verts(i).Z)
                vRaw(i) = OpenTK.Mathematics.Vector3d.TransformPosition(vLocal, Mtot)
            Next

            ' 7) App world vertices from the normal render path
            Dim ourWorld = SkinningHelper.GetWorldVertices(ourGeo)
            Dim compareCount = Math.Min(vCount, ourWorld.Length)

            ' 7.1) TRI-PROBE 2026-04-19: explicit vert-count comparison bake NIF vs render NIF.
            ' If the two counts differ, vRaw[i] ≠ ourWorld[i] by definition (index misalignment)
            ' and the RMS is meaningless. Male asymmetry puzzle: Base.nif=1690 vs _faceBones.nif=1696.
            Dim bakeShapeName = If(bakedHead?.Name?.String, "(null)")
            Dim ourShapeName = If(ourShape?.NifShape?.Name?.String, "(null)")
            Dim ourLocalCount = If(ourGeo.NifLocalVertices IsNot Nothing, ourGeo.NifLocalVertices.Length, -1)
            NpcPreviewLog.Log($"  [HARNESS-VCOUNT] bake shape='{bakeShapeName}' verts={vCount} | render shape='{ourShapeName}' NifLocal={ourLocalCount} world={ourWorld.Length} | compare={compareCount} | match={(vCount = ourWorld.Length)}")

            ' 8) Sanity: log first 5 raw/app pairs BEFORE subtraction (catches unit/axis mismatch)
            For i = 0 To Math.Min(4, compareCount - 1)
                NpcPreviewLog.Log($"    [HARNESS-RAW] vert[{i}] raw=({vRaw(i).X:F4},{vRaw(i).Y:F4},{vRaw(i).Z:F4}) app=({ourWorld(i).X:F4},{ourWorld(i).Y:F4},{ourWorld(i).Z:F4})")
            Next

            ' 9) Diffs, RMS, max, top-10
            Dim sumSq As Double = 0
            Dim maxMag As Double = 0
            Dim diffs As New List(Of (Idx As Integer, Mag As Double))(compareCount)
            For i = 0 To compareCount - 1
                Dim dx = vRaw(i).X - ourWorld(i).X
                Dim dy = vRaw(i).Y - ourWorld(i).Y
                Dim dz = vRaw(i).Z - ourWorld(i).Z
                Dim mag = Math.Sqrt(dx * dx + dy * dy + dz * dz)
                sumSq += mag * mag
                If mag > maxMag Then maxMag = mag
                diffs.Add((i, mag))
            Next
            Dim rms = Math.Sqrt(sumSq / Math.Max(1, compareCount))
            NpcPreviewLog.Log($"  [HARNESS-RAW] {compareCount} verts RMS={rms:F4} max={maxMag:F4}")

            diffs.Sort(Function(a, b) b.Mag.CompareTo(a.Mag))
            For i = 0 To Math.Min(9, diffs.Count - 1)
                NpcPreviewLog.Log($"    [HARNESS-RAW] top[{i}] idx={diffs(i).Idx} mag={diffs(i).Mag:F4}")
            Next

            ' 9.5) Morph attribution per vertex — morph-driven scope, not vertex-driven lookup.
            '   Vertex morphs: MorphPlan.Channels[k].Deltas[].Index is the exact list of verts touched.
            '   Bone morphs:   SkeletonDictionary[bone].DeltaTransform ≠ identity means the bone
            '                  carries a morph (FMRS if face-region name, body-weight if body scale);
            '                  GPUBoneIndices/Weights give which verts weight to it.
            '   Empirical test: vertices with neither vertex-morph nor bone-morph MUST have diff ≈ 0.
            '                   Non-zero RMS in "N-none" bucket = conceptual error (missing morph source
            '                   or a pipeline step that perturbs untouched verts).

            Dim vertMorphNames(compareCount - 1) As List(Of String)
            For i = 0 To compareCount - 1 : vertMorphNames(i) = New List(Of String)() : Next
            Dim morphPlan As MorphPlan = Nothing
            If morphResolver IsNot Nothing AndAlso ourShape IsNot Nothing Then
                Try
                    Dim plan = morphResolver.ResolveMorphPlan(ourShape, ourGeo)
                    morphPlan = plan
                    If plan IsNot Nothing AndAlso plan.Channels IsNot Nothing Then
                        NpcPreviewLog.Log($"  [HARNESS-ATTR] vertex morph channels: {plan.Channels.Count}")
                        For Each ch In plan.Channels
                            Dim n = If(ch.Deltas IsNot Nothing, ch.Deltas.Count, 0)
                            NpcPreviewLog.Log($"    [HARNESS-ATTR] channel '{ch.Name}' weight={ch.Weight:F3} touches={n} verts")
                            If ch.Deltas Is Nothing Then Continue For
                            For Each d In ch.Deltas
                                Dim vi As Integer = CInt(d.index)
                                If vi >= 0 AndAlso vi < compareCount Then vertMorphNames(vi).Add(ch.Name)
                            Next
                        Next
                    Else
                        NpcPreviewLog.Log($"  [HARNESS-ATTR] morphResolver returned null/empty plan")
                    End If
                Catch exP As Exception
                    NpcPreviewLog.Log($"  [HARNESS-ATTR] ResolveMorphPlan exception: {exP.Message}")
                End Try
            Else
                NpcPreviewLog.Log($"  [HARNESS-ATTR] morphResolver=Nothing (vertex morphs checkbox OFF)")
            End If

            ' Non-identity bone deltas → morph tag per bone.
            ' Root carries the race Delta.Scale (affects every descendant via composition); tag it
            ' RACE-ROOT and EXCLUDE from per-vert attribution — otherwise every vertex would trivially
            ' be "bone-morph-touched" and the N-none bucket would be empty.
            '
            ' Non-uniform scale detection: the body-weight pipeline encodes ScaleX/Y/Z into the
            ' Rotation matrix columns instead of the uniform Scale field (NifRenderTransformation.vb:62-68:
            ' r.M11 *= ScaleX, r.M21 *= ScaleX, r.M31 *= ScaleX, etc). A pure rotation keeps each
            ' column with length 1 (orthonormal). If any column length ≠ 1, ScaleX/Y/Z ≠ 1 is hiding
            ' there — that's the BODY-WEIGHT signature. Without this check, body-weight bones land in
            ' OTHER-DELTA (empirical: 46 "OTHER" bones were really body-weight).
            Dim boneMorphs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each kv In Skeleton_Class.SkeletonDictionary
                Dim dt = kv.Value.DeltaTransform
                If dt Is Nothing Then Continue For
                Dim tNonZero = Math.Abs(dt.Translation.X) + Math.Abs(dt.Translation.Y) + Math.Abs(dt.Translation.Z) > 0.00001F
                Dim sNonOne = Math.Abs(dt.Scale - 1.0F) > 0.00001F
                Dim r = dt.Rotation
                Dim rNonIdent = Math.Abs(r.M11 - 1) + Math.Abs(r.M22 - 1) + Math.Abs(r.M33 - 1) +
                                Math.Abs(r.M12) + Math.Abs(r.M13) +
                                Math.Abs(r.M21) + Math.Abs(r.M23) +
                                Math.Abs(r.M31) + Math.Abs(r.M32) > 0.00001F
                If Not (tNonZero OrElse sNonOne OrElse rNonIdent) Then Continue For
                Dim col0Len = Math.Sqrt(r.M11 * r.M11 + r.M21 * r.M21 + r.M31 * r.M31)
                Dim col1Len = Math.Sqrt(r.M12 * r.M12 + r.M22 * r.M22 + r.M32 * r.M32)
                Dim col2Len = Math.Sqrt(r.M13 * r.M13 + r.M23 * r.M23 + r.M33 * r.M33)
                Dim nonUniformScale = Math.Abs(col0Len - 1) > 0.0001 OrElse
                                      Math.Abs(col1Len - 1) > 0.0001 OrElse
                                      Math.Abs(col2Len - 1) > 0.0001
                Dim name = kv.Key
                Dim tag As String
                If name.StartsWith("skin_bone_", StringComparison.OrdinalIgnoreCase) Then
                    tag = "FMRS"
                ElseIf name.Equals("Root", StringComparison.OrdinalIgnoreCase) Then
                    tag = "RACE-ROOT"
                ElseIf nonUniformScale OrElse sNonOne Then
                    tag = "BODY-WEIGHT"
                Else
                    tag = "OTHER-DELTA"
                End If
                boneMorphs(name) = tag
            Next
            Dim cntFMRS = 0, cntBody = 0, cntRoot = 0, cntOther = 0
            For Each t In boneMorphs.Values
                Select Case t
                    Case "FMRS" : cntFMRS += 1
                    Case "BODY-WEIGHT" : cntBody += 1
                    Case "RACE-ROOT" : cntRoot += 1
                    Case Else : cntOther += 1
                End Select
            Next
            NpcPreviewLog.Log($"  [HARNESS-ATTR] bones with non-identity DeltaTransform: {boneMorphs.Count} (FMRS={cntFMRS} BODY-WEIGHT={cntBody} RACE-ROOT={cntRoot} OTHER={cntOther})")

            ' Per-vertex bone attribution via GPUBoneIndices/Weights (flat arrays, 4 bones per vert).
            ' Also track primary_bone = the bone with the maximum skin weight for this vertex;
            ' used by the neck-cluster spatial-linearity filter below and emitted to CSV so
            ' multi-NPC regressions can filter/pivot by primary_bone.
            Dim ourBoneNames As String() = ourShape.ShapeBones.Select(Function(nn) If(nn?.Name?.String, "")).ToArray()
            Dim gpuIdx = ourGeo.GPUBoneIndices
            Dim gpuWgt = ourGeo.GPUBoneWeights
            Dim vertBoneMorphTags(compareCount - 1) As List(Of String)
            Dim primaryBoneName(compareCount - 1) As String
            Dim primaryBoneWeight(compareCount - 1) As Single
            Dim hasFMRSByVert(compareCount - 1) As Boolean
            Dim hasBWByVert(compareCount - 1) As Boolean
            Dim hasOtherByVert(compareCount - 1) As Boolean
            For i = 0 To compareCount - 1
                vertBoneMorphTags(i) = New List(Of String)()
                primaryBoneName(i) = ""
                primaryBoneWeight(i) = 0
            Next
            If gpuIdx IsNot Nothing AndAlso gpuWgt IsNot Nothing Then
                For i = 0 To compareCount - 1
                    Dim baseI = i * 4
                    If baseI + 3 >= gpuIdx.Length Then Exit For
                    Dim bestW As Single = -1
                    Dim bestName As String = ""
                    For j = 0 To 3
                        Dim w = gpuWgt(baseI + j)
                        If w <= 0.00001F Then Continue For
                        Dim bi = CInt(gpuIdx(baseI + j))
                        If bi < 0 OrElse bi >= ourBoneNames.Length Then Continue For
                        Dim bn = ourBoneNames(bi)
                        If w > bestW Then
                            bestW = w
                            bestName = bn
                        End If
                        Dim btag As String = Nothing
                        If boneMorphs.TryGetValue(bn, btag) AndAlso btag <> "RACE-ROOT" Then
                            vertBoneMorphTags(i).Add($"{bn}({btag},w={w:F2})")
                            Select Case btag
                                Case "FMRS" : hasFMRSByVert(i) = True
                                Case "BODY-WEIGHT" : hasBWByVert(i) = True
                                Case "OTHER-DELTA" : hasOtherByVert(i) = True
                            End Select
                        End If
                    Next
                    primaryBoneName(i) = bestName
                    primaryBoneWeight(i) = If(bestW < 0, 0, bestW)
                Next
            End If

            ' Empirical self-consistency buckets by morph type. FMRS and BODY-WEIGHT are kept as
            ' separate bone-source axes so we can see if the face bake-match residual comes from
            ' (a) pure body-weight bones (the strong candidate per log evidence), (b) pure FMRS,
            ' (c) vertex morphs, or combinations. N-none MUST be ≈0 or there's an unaccounted source.
            Dim bucketKeys As String() = {"N-none", "V-only", "FMRS-only", "BW-only",
                                          "V+FMRS", "V+BW", "FMRS+BW", "V+FMRS+BW"}
            Dim bucketN As New Dictionary(Of String, Integer)()
            Dim bucketSumSq As New Dictionary(Of String, Double)()
            Dim bucketMax As New Dictionary(Of String, Double)()
            For Each k In bucketKeys
                bucketN(k) = 0 : bucketSumSq(k) = 0 : bucketMax(k) = 0
            Next
            Dim bucketTag(compareCount - 1) As String
            For i = 0 To compareCount - 1
                Dim hasV = vertMorphNames(i).Count > 0
                Dim hasF = hasFMRSByVert(i)
                Dim hasW = hasBWByVert(i)
                Dim bk As String
                If Not hasV AndAlso Not hasF AndAlso Not hasW Then
                    bk = "N-none"
                ElseIf hasV AndAlso Not hasF AndAlso Not hasW Then
                    bk = "V-only"
                ElseIf Not hasV AndAlso hasF AndAlso Not hasW Then
                    bk = "FMRS-only"
                ElseIf Not hasV AndAlso Not hasF AndAlso hasW Then
                    bk = "BW-only"
                ElseIf hasV AndAlso hasF AndAlso Not hasW Then
                    bk = "V+FMRS"
                ElseIf hasV AndAlso Not hasF AndAlso hasW Then
                    bk = "V+BW"
                ElseIf Not hasV AndAlso hasF AndAlso hasW Then
                    bk = "FMRS+BW"
                Else
                    bk = "V+FMRS+BW"
                End If
                bucketTag(i) = bk
                Dim dx = vRaw(i).X - ourWorld(i).X
                Dim dy = vRaw(i).Y - ourWorld(i).Y
                Dim dz = vRaw(i).Z - ourWorld(i).Z
                Dim mg = Math.Sqrt(dx * dx + dy * dy + dz * dz)
                bucketN(bk) += 1
                bucketSumSq(bk) += mg * mg
                If mg > bucketMax(bk) Then bucketMax(bk) = mg
            Next
            NpcPreviewLog.Log($"  [HARNESS-ATTR] bucket RMS by morph type (N-none MUST be ≈0 or unaccounted source):")
            For Each k In bucketKeys
                Dim n = bucketN(k)
                If n = 0 Then
                    NpcPreviewLog.Log($"    [{k}] N=0")
                Else
                    Dim bRms = Math.Sqrt(bucketSumSq(k) / n)
                    NpcPreviewLog.Log($"    [{k}] N={n} RMS={bRms:F4} max={bucketMax(k):F4}")
                End If
            Next
            ' Log any OTHER-DELTA verts separately if they exist (classifier leftover = regression canary).
            Dim nOther = 0
            For i = 0 To compareCount - 1 : If hasOtherByVert(i) Then nOther += 1
            Next
            If nOther > 0 Then NpcPreviewLog.Log($"  [HARNESS-ATTR] WARN: {nOther} verts touch bones tagged OTHER-DELTA — classifier did not cover some delta type")

            NpcPreviewLog.Log($"  [HARNESS-ATTR] top-10 with morph attribution:")
            For i = 0 To Math.Min(9, diffs.Count - 1)
                Dim vi = diffs(i).Idx
                Dim vM = If(vertMorphNames(vi).Count > 0, String.Join("|", vertMorphNames(vi)), "(none)")
                Dim bM = If(vertBoneMorphTags(vi).Count > 0, String.Join("|", vertBoneMorphTags(vi)), "(none)")
                NpcPreviewLog.Log($"    top[{i}] idx={vi} mag={diffs(i).Mag:F4} bucket={bucketTag(vi)} V=[{vM}] B=[{bM}]")
            Next

            ' V-only delta breakdown: for each V-only vert, dump the per-channel delta contribution
            ' (weight × PosDiff summed to produce the total applied morph vector). Compares magnitude
            ' of |Σ deltas| vs observed diff to decide if FMIN interacts with vertex morphs or if
            ' the residual is just the base vertex-morph delta (α_bake=0 hypothesis: bake applies
            ' delta × 1, same as our render, so diff should be ~0 — any residual is NPC-specific).
            If morphPlan IsNot Nothing AndAlso morphPlan.Channels IsNot Nothing Then
                Dim vOnlyCount = 0
                For i = 0 To compareCount - 1 : If bucketTag(i) = "V-only" Then vOnlyCount += 1
                Next
                If vOnlyCount > 0 Then
                    NpcPreviewLog.Log($"  [V-ONLY-DELTA-DIAG] {vOnlyCount} V-only verts — per-channel delta breakdown:")
                    For i = 0 To compareCount - 1
                        If bucketTag(i) <> "V-only" Then Continue For
                        Dim dx = vRaw(i).X - ourWorld(i).X
                        Dim dy = vRaw(i).Y - ourWorld(i).Y
                        Dim dz = vRaw(i).Z - ourWorld(i).Z
                        Dim obsMag = Math.Sqrt(dx * dx + dy * dy + dz * dz)
                        Dim sumX As Single = 0, sumY As Single = 0, sumZ As Single = 0
                        Dim contribs As New List(Of String)
                        For Each ch In morphPlan.Channels
                            If ch.Deltas Is Nothing Then Continue For
                            For Each d In ch.Deltas
                                If CInt(d.index) <> i Then Continue For
                                Dim wx = ch.Weight * d.PosDiff.X
                                Dim wy = ch.Weight * d.PosDiff.Y
                                Dim wz = ch.Weight * d.PosDiff.Z
                                sumX += wx : sumY += wy : sumZ += wz
                                Dim wmag = Math.Sqrt(wx * wx + wy * wy + wz * wz)
                                contribs.Add($"{ch.Name}(w={ch.Weight:F3},wd=({wx:+0.0000;-0.0000;0.0000},{wy:+0.0000;-0.0000;0.0000},{wz:+0.0000;-0.0000;0.0000}),|wd|={wmag:F4})")
                                Exit For
                            Next
                        Next
                        Dim sumMag = Math.Sqrt(sumX * sumX + sumY * sumY + sumZ * sumZ)
                        NpcPreviewLog.Log($"    [V-ONLY-DELTA-DIAG] idx={i} obs_diff=({dx:+0.0000;-0.0000;0.0000},{dy:+0.0000;-0.0000;0.0000},{dz:+0.0000;-0.0000;0.0000}) |obs|={obsMag:F4} | Σweighted_delta=({sumX:+0.0000;-0.0000;0.0000},{sumY:+0.0000;-0.0000;0.0000},{sumZ:+0.0000;-0.0000;0.0000}) |Σ|={sumMag:F4}")
                        For Each c In contribs
                            NpcPreviewLog.Log($"      [V-ONLY-DELTA-DIAG] {c}")
                        Next
                    Next
                End If
            End If

            ' Neck-cluster spatial-linearity filter.
            ' Verts whose primary bone is in {Neck_skin, Neck_Low_skin, Neck1_skin, Chest_skin,
            ' LArm_Collarbone_skin, RArm_Collarbone_skin} — the body-weight bones that dominate
            ' top-diffs. MWGT is inline so the log row is self-describing; cross-NPC regression
            ' (test #3 of the theory evaluation: per-vert A_musc_i, A_thin_i, A_fat_i) uses CSV.
            Dim neckClusterBones As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                "Neck_skin", "Neck_Low_skin", "Neck1_skin", "Chest_skin",
                "LArm_Collarbone_skin", "RArm_Collarbone_skin"
            }
            Dim clusterDiffs As New List(Of (Idx As Integer, Mag As Double, Dx As Double, Dy As Double, Dz As Double, Bone As String, W As Single))()
            For i = 0 To compareCount - 1
                If Not neckClusterBones.Contains(primaryBoneName(i)) Then Continue For
                Dim dx = vRaw(i).X - ourWorld(i).X
                Dim dy = vRaw(i).Y - ourWorld(i).Y
                Dim dz = vRaw(i).Z - ourWorld(i).Z
                Dim mg = Math.Sqrt(dx * dx + dy * dy + dz * dz)
                clusterDiffs.Add((i, mg, dx, dy, dz, primaryBoneName(i), primaryBoneWeight(i)))
            Next
            clusterDiffs.Sort(Function(a, b) b.Mag.CompareTo(a.Mag))
            NpcPreviewLog.Log($"  [NECK-CLUSTER] mwgt(thin={state.WeightThin:F3} musc={state.WeightMuscular:F3} fat={state.WeightFat:F3}) raceHeight={raceHeight:F4} verts_in_cluster={clusterDiffs.Count}")
            If clusterDiffs.Count > 0 Then
                Dim clusterSumSq As Double = 0
                For Each cd In clusterDiffs : clusterSumSq += cd.Mag * cd.Mag : Next
                Dim clusterRms = Math.Sqrt(clusterSumSq / clusterDiffs.Count)
                NpcPreviewLog.Log($"  [NECK-CLUSTER] cluster RMS={clusterRms:F4} max={clusterDiffs(0).Mag:F4}. Top-20 diffs:")
                For i = 0 To Math.Min(19, clusterDiffs.Count - 1)
                    Dim cd = clusterDiffs(i)
                    NpcPreviewLog.Log($"    idx={cd.Idx} primary={cd.Bone}(w={cd.W:F2}) mag={cd.Mag:F4} diff=({cd.Dx:+0.0000;-0.0000;0.0000},{cd.Dy:+0.0000;-0.0000;0.0000},{cd.Dz:+0.0000;-0.0000;0.0000})")
                Next
            End If

            ' 10) CSV dump alongside npc_preview.log (includes morph attribution columns)
            Try
                Dim logDir = AppDomain.CurrentDomain.BaseDirectory
                Dim csvPath = IO.Path.Combine(logDir, $"harness_raw_{state.FormID:X8}.csv")
                Using w As New IO.StreamWriter(csvPath, False)
                    w.WriteLine("vertex_index,x_app,y_app,z_app,x_raw,y_raw,z_raw,dx,dy,dz,mag,bucket,vertex_morphs,bone_morphs,primary_bone,primary_bone_weight,mwgt_thin,mwgt_musc,mwgt_fat,race_height")
                    For i = 0 To compareCount - 1
                        Dim dx = vRaw(i).X - ourWorld(i).X
                        Dim dy = vRaw(i).Y - ourWorld(i).Y
                        Dim dz = vRaw(i).Z - ourWorld(i).Z
                        Dim mag = Math.Sqrt(dx * dx + dy * dy + dz * dz)
                        Dim vM = String.Join("|", vertMorphNames(i))
                        Dim bM = String.Join("|", vertBoneMorphTags(i))
                        w.WriteLine($"{i},{ourWorld(i).X:R},{ourWorld(i).Y:R},{ourWorld(i).Z:R},{vRaw(i).X:R},{vRaw(i).Y:R},{vRaw(i).Z:R},{dx:R},{dy:R},{dz:R},{mag:R},{bucketTag(i)},{vM},{bM},{primaryBoneName(i)},{primaryBoneWeight(i):R},{state.WeightThin:R},{state.WeightMuscular:R},{state.WeightFat:R},{raceHeight:R}")
                    Next
                End Using
                NpcPreviewLog.Log($"  [HARNESS-RAW] CSV dumped to '{csvPath}'")
            Catch exC As Exception
                NpcPreviewLog.Log($"  [HARNESS-RAW] CSV dump failed: {exC.Message}")
            End Try

        Catch ex As Exception
            NpcPreviewLog.Log($"  [HARNESS-RAW] exception: {ex.Message}")
        End Try
    End Sub

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

    ''' <summary>Build a pose of face bone deltas from the NPC's FMRI/FMRS subrecords.
    ''' For each FMRI region, look up the region in the race's FacialBoneRegions JSON, then
    ''' for each bone in the region compute a per-axis delta by signed-lerping FMRS sliders
    ''' (clamped to [-1,+1]) across Minima/Default/Maxima, scaled by FMIN. Bone names are
    ''' prefixed with "skin_" to match SkeletonDictionary. Returns Nothing if no regions
    ''' file is found or no non-zero FMRS values contribute.</summary>
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
        Dim boneScales As New Dictionary(Of String, System.Numerics.Vector3)(StringComparer.OrdinalIgnoreCase)
        Dim fmin = If(npcData.FacialMorphIntensity <= 0.0F, 1.0F, npcData.FacialMorphIntensity)

        ' Log RACE region count vs NPC FaceMorphs count, and which indices the NPC references
        ' vs which ones the JSON declares. Helps spot missing regions (CK shows all RACE regions
        ' as sliders; NPC only stores the ones with non-default values).
        Dim raceRegionIndices = regionsFile.Regions.Keys.OrderBy(Function(i) i).ToList()
        Dim npcIndices = npcData.FaceMorphs.Select(Function(f) f.Index).OrderBy(Function(i) i).ToList()
        Dim missingInNpc = raceRegionIndices.Except(npcIndices).ToList()
        Dim extraInNpc = npcIndices.Except(raceRegionIndices).ToList()
        NpcPreviewLog.Log($"  [FMRS-RAW] RACE regions={raceRegionIndices.Count} NPC FaceMorphs={npcIndices.Count} missing-in-NPC={missingInNpc.Count} extra-in-NPC={extraInNpc.Count} fmin={fmin:F3}")
        If missingInNpc.Count > 0 Then
            Dim missingDetail = String.Join(", ", missingInNpc.Take(10).Select(Function(i)
                                                                                   Dim r As FacialBoneRegion = Nothing
                                                                                   regionsFile.Regions.TryGetValue(i, r)
                                                                                   Return $"{i}('{If(r IsNot Nothing, r.Name, "?")}')"
                                                                               End Function))
            NpcPreviewLog.Log($"  [FMRS-RAW] regions-in-RACE-not-in-NPC (first 10): {missingDetail}")
        End If
        If extraInNpc.Count > 0 Then
            NpcPreviewLog.Log($"  [FMRS-RAW] indices-in-NPC-not-in-RACE (first 10): {String.Join(", ", extraInNpc.Take(10))}")
        End If

        For Each fm In npcData.FaceMorphs
            Dim region As FacialBoneRegion = Nothing
            If Not regionsFile.Regions.TryGetValue(fm.Index, region) Then
                NpcPreviewLog.Log($"  [FMRS-RAW] FMRI={fm.Index} → NOT FOUND in RACE regions JSON")
                Continue For
            End If

            Dim px = fm.PositionX
            Dim py = fm.PositionY
            Dim pz = fm.PositionZ
            Dim rx = fm.RotationX
            Dim ry = fm.RotationY
            Dim rz = fm.RotationZ
            Dim sc = fm.Scale

            Dim isZero As Boolean = (Math.Abs(px) < 0.0001F AndAlso Math.Abs(py) < 0.0001F AndAlso Math.Abs(pz) < 0.0001F AndAlso
                                     Math.Abs(rx) < 0.0001F AndAlso Math.Abs(ry) < 0.0001F AndAlso Math.Abs(rz) < 0.0001F AndAlso
                                     Math.Abs(sc) < 0.0001F)
            Dim nonZeroMark As String = If(isZero, " (all-zero, will skip)", "")
            NpcPreviewLog.Log($"  [FMRS-RAW] FMRI={fm.Index} region='{region.Name}' bones={region.Bones.Count} sliders: pos=({px:+0.000;-0.000;0.000},{py:+0.000;-0.000;0.000},{pz:+0.000;-0.000;0.000}) rot=({rx:+0.000;-0.000;0.000},{ry:+0.000;-0.000;0.000},{rz:+0.000;-0.000;0.000}) scale={sc:+0.000;-0.000;0.000}{nonZeroMark}")

            ' Skip regions with all-zero FMRS (no deformation at all)
            If Math.Abs(px) < 0.0001F AndAlso Math.Abs(py) < 0.0001F AndAlso Math.Abs(pz) < 0.0001F AndAlso
               Math.Abs(rx) < 0.0001F AndAlso Math.Abs(ry) < 0.0001F AndAlso Math.Abs(rz) < 0.0001F AndAlso
               Math.Abs(sc) < 0.0001F Then Continue For

            For Each boneEntry In region.Bones
                Dim targetBoneName = "skin_" & boneEntry.Bone

                ' FMIN as linear multiplier AFTER LerpFmrs, symmetric across pos/rot/scale.
                ' Empirical validation 2026-04-19:
                '   Cient (FMIN=2) all-linear render → FMRS-only RMS=0.012 (ruido) confirms bake =
                '     s × range × fmin without clamping s × fmin for pos/rot.
                '   Tested "clamp s × fmin inside LerpFmrs" for pos/rot → broke Cient (0.012 → 0.127).
                '     So bake doesn't clamp s × fmin; fmin stays OUTSIDE.
                '   Scale kept fmin INSIDE (legacy) clamped at |s×fmin|≤1. With fmin=2 (Cient) the
                '     clamp rarely fired (|sc|×2 < 1 for most sliders) so noise stayed. With fmin=4
                '     (Preston) the clamp fired for |sc|>0.25 (many sliders) saturating scale while
                '     pos/rot went unclamped, producing FMRS-only RMS=0.079 residual. Aligning scale
                '     with pos/rot (outside-linear, no clamp) restores symmetry.
                Dim deltaPos As New System.Numerics.Vector3(
                    LerpFmrs(px, region.DefaultPosition.X, boneEntry.MinimaPosition.X, boneEntry.MaximaPosition.X) * fmin,
                    LerpFmrs(py, region.DefaultPosition.Y, boneEntry.MinimaPosition.Y, boneEntry.MaximaPosition.Y) * fmin,
                    LerpFmrs(pz, region.DefaultPosition.Z, boneEntry.MinimaPosition.Z, boneEntry.MaximaPosition.Z) * fmin)

                Dim deltaRot As New System.Numerics.Vector3(
                    LerpFmrs(rx, region.DefaultRotation.X, boneEntry.MinimaRotation.X, boneEntry.MaximaRotation.X) * fmin,
                    LerpFmrs(ry, region.DefaultRotation.Y, boneEntry.MinimaRotation.Y, boneEntry.MaximaRotation.Y) * fmin,
                    LerpFmrs(rz, region.DefaultRotation.Z, boneEntry.MinimaRotation.Z, boneEntry.MaximaRotation.Z) * fmin)

                Dim deltaScale As New System.Numerics.Vector3(
                    LerpFmrs(sc, region.DefaultScale.X, boneEntry.MinimaScale.X, boneEntry.MaximaScale.X) * fmin,
                    LerpFmrs(sc, region.DefaultScale.Y, boneEntry.MinimaScale.Y, boneEntry.MaximaScale.Y) * fmin,
                    LerpFmrs(sc, region.DefaultScale.Z, boneEntry.MinimaScale.Z, boneEntry.MaximaScale.Z) * fmin)

                ' EulerXYZToMatrix33 applies a J·R·J permutation (with J anti-diagonal, swapping X
                ' and Z axes) whose net effect inverts the sign of all three rotation angles:
                '   J·Rx(θ)·J = Rz(-θ), J·Ry(θ)·J = Ry(-θ), J·Rz(θ)·J = Rx(-θ)
                ' The pose-system callsite (NifRenderTransformation.vb:55) works because BodySlide/SAM
                ' pose JSON already uses angles pre-inverted relative to standard math — the function
                ' compensates for them. FMRS JSON uses standard convention (positive = right-hand rule
                ' around the named axis), so we must negate all three to undo the function's flip.
                ' Confirmado 2026-04-18 matemática + empírica en los 3 ejes:
                '   X: Alijo Nose-Bridge rot X=+24° → tabique proyectado (matchea CK).
                '   Y: Cait Ears-Full rot Y=±12.9° → orejas afuera (matchea CK).
                '   Z: Cait Mouth-Corners rot Z=±14° → comisuras en la dirección correcta (matchea CK).
                Dim rotation = Transform_Class.EulerXYZToMatrix33(-deltaRot.X, -deltaRot.Y, -deltaRot.Z)
                Dim xform As New Transform_Class With {
                    .Rotation = rotation,
                    .Translation = deltaPos,
                    .Scale = 1.0F
                }
                Dim boneScaleVec = New System.Numerics.Vector3(
                    1.0F + deltaScale.X, 1.0F + deltaScale.Y, 1.0F + deltaScale.Z)

                ' Accumulate non-uniform scale per bone (multiply across regions)
                Dim existingScale As System.Numerics.Vector3
                If boneScales.TryGetValue(targetBoneName, existingScale) Then
                    boneScales(targetBoneName) = existingScale * boneScaleVec
                Else
                    boneScales(targetBoneName) = boneScaleVec
                End If

                ' Compose rotation+translation across regions
                Dim existing As Transform_Class = Nothing
                If result.TryGetValue(targetBoneName, existing) AndAlso existing IsNot Nothing Then
                    result(targetBoneName) = existing.ComposeTransforms(xform)
                Else
                    result(targetBoneName) = xform
                End If

                Dim isAnyNonZero As Boolean = (Math.Abs(deltaPos.X) > 0.0001F OrElse Math.Abs(deltaPos.Y) > 0.0001F OrElse Math.Abs(deltaPos.Z) > 0.0001F _
                                            OrElse Math.Abs(deltaRot.X) > 0.0001F OrElse Math.Abs(deltaRot.Y) > 0.0001F OrElse Math.Abs(deltaRot.Z) > 0.0001F _
                                            OrElse Math.Abs(deltaScale.X) > 0.0001F OrElse Math.Abs(deltaScale.Y) > 0.0001F OrElse Math.Abs(deltaScale.Z) > 0.0001F)
                If isAnyNonZero Then
                    NpcPreviewLog.Log($"    [FMRS-BONE] region='{region.Name}' bone='{targetBoneName}' deltaPos=({deltaPos.X:+0.000;-0.000;0.000},{deltaPos.Y:+0.000;-0.000;0.000},{deltaPos.Z:+0.000;-0.000;0.000}) deltaRot=({deltaRot.X:+0.000;-0.000;0.000},{deltaRot.Y:+0.000;-0.000;0.000},{deltaRot.Z:+0.000;-0.000;0.000}) deltaScale=({deltaScale.X:+0.000;-0.000;0.000},{deltaScale.Y:+0.000;-0.000;0.000},{deltaScale.Z:+0.000;-0.000;0.000})")
                End If
            Next
        Next

        If result.Count = 0 Then Return Nothing

        ' Convert the Transform_Class deltas into a Poses_class with PoseTransformData entries.
        Dim pose As New Poses_class With {
            .Name = "FMRS Face Morph",
            .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
            .Transforms = New Dictionary(Of String, PoseTransformData)
        }
        For Each kv In result
            Dim xform = kv.Value
            Dim rotVec = Transform_Class.Matrix33ToBSRotation(xform.Rotation)
            Dim sc As System.Numerics.Vector3
            If Not boneScales.TryGetValue(kv.Key, sc) Then
                sc = New System.Numerics.Vector3(1.0F, 1.0F, 1.0F)
            End If
            pose.Transforms(kv.Key) = New PoseTransformData With {
                .X = xform.Translation.X,
                .Y = xform.Translation.Y,
                .Z = xform.Translation.Z,
                .Yaw = rotVec.X,
                .Pitch = rotVec.Y,
                .Roll = rotVec.Z,
                .Scale = 1.0F,
                .ScaleX = sc.X,
                .ScaleY = sc.Y,
                .ScaleZ = sc.Z
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

    ''' <summary>Resolve the NPC's MWGT weights and the RACE's per-bone weight scale data for
    ''' use by the skeleton resolver. Returns Nothing if the NPC has no MWGT or the RACE has
    ''' no bone data for the NPC's gender.</summary>
    Private Function ResolveBodyWeightData(state As NPCVisualState, renderData As PreviewResolutionResult) As (Wt As Single, Wm As Single, Wf As Single, GenderBlock As RACE_BoneDataGender, MrsvValues As List(Of Single), ArmaDeltas As Dictionary(Of String, System.Numerics.Vector3), NnamX As Single, NnamY As Single)
        If state Is Nothing Then Return Nothing

        Dim modelNpcFormID = If(state.ModelSourceFormID <> 0UI, state.ModelSourceFormID, state.FormID)
        Dim npcData = GetParsedNpc(modelNpcFormID)
        If npcData Is Nothing Then Return Nothing

        Dim wt As Single = npcData.WeightThin
        Dim wm As Single = npcData.WeightMuscular
        Dim wf As Single = npcData.WeightFat
        Dim armaDeltas = If(renderData IsNot Nothing, renderData.ArmaBoneScaleDeltas, Nothing)
        Dim hasMwgt = (wt + wm + wf) >= 0.001F
        Dim hasArmaDeltas = (armaDeltas IsNot Nothing AndAlso armaDeltas.Count > 0)
        If Not hasMwgt AndAlso Not hasArmaDeltas Then Return Nothing

        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)

        ' Log the FaceGen clamps for reference. TBD whether they apply to body BSMS output
        ' or only to face slider*FMIN. Not applying any clamp formula without spec.
        NpcPreviewLog.Log($"  [RACE-CLAMPS] {race.EditorID} PNAM(Main)={race.FaceGenMainClamp:F3} UNAM(Face)={race.FaceGenFaceClamp:F3}")

        ' Log NNAM raw (both genders) — "Neck Fat Adjustments Scale" per xEdit, 4 unknown bytes + X + Y.
        ' Hypothesis: the 4 bytes may be (thin, musc, fat, pad) weights. Interpretation pending.
        Dim fmtNNAM = Function(raw As Byte(), xv As Single, yv As Single) As String
                          If raw Is Nothing Then Return "none"
                          Return $"bytes=[{raw(0):X2} {raw(1):X2} {raw(2):X2} {raw(3):X2}] (dec={raw(0)},{raw(1)},{raw(2)},{raw(3)}) X={xv:F4} Y={yv:F4}"
                      End Function
        NpcPreviewLog.Log($"  [RACE-NNAM] {race.EditorID} Male:   {fmtNNAM(race.MaleNeckNNAMRaw, race.MaleNeckNNAMX, race.MaleNeckNNAMY)}")
        NpcPreviewLog.Log($"  [RACE-NNAM] {race.EditorID} Female: {fmtNNAM(race.FemaleNeckNNAMRaw, race.FemaleNeckNNAMX, race.FemaleNeckNNAMY)}")
        NpcPreviewLog.Log($"  [RACE-HEIGHT] {race.EditorID} MaleHeight={race.MaleHeight:F4} FemaleHeight={race.FemaleHeight:F4}")

        ' Gender-resolved NNAM ("Neck Fat Adjustments Scale" — xEdit wbDefinitionsFO4.pas:11639/11657).
        ' Consumed by BuildBodyWeightPose as HIPÓTESIS H1 (multiplicative neck-fat modifier).
        ' The 4-byte Unknown prefix is NOT read — HumanRace vanilla has it zero and no spec exists
        ' to decode it. If a race ships it non-zero we flag and proceed with H1 anyway (unchanged).
        Dim nnamX As Single = If(state.IsFemale, race.FemaleNeckNNAMX, race.MaleNeckNNAMX)
        Dim nnamY As Single = If(state.IsFemale, race.FemaleNeckNNAMY, race.MaleNeckNNAMY)
        Dim nnamRaw = If(state.IsFemale, race.FemaleNeckNNAMRaw, race.MaleNeckNNAMRaw)
        NpcPreviewLog.Log($"  [NNAM-RESOLVED] race={race.EditorID} gender={If(state.IsFemale, "F", "M")} X={nnamX:F4} Y={nnamY:F4} fat={wf:F3}")
        If nnamRaw IsNot Nothing AndAlso (nnamRaw(0) <> 0 OrElse nnamRaw(1) <> 0 OrElse nnamRaw(2) <> 0 OrElse nnamRaw(3) <> 0) Then
            NpcPreviewLog.Log($"  [NNAM-WARN] Unknown prefix bytes are non-zero on {race.EditorID} gender={If(state.IsFemale, "F", "M")} bytes=[{nnamRaw(0):X2} {nnamRaw(1):X2} {nnamRaw(2):X2} {nnamRaw(3):X2}] — semantics unresolved, H1 ignores them")
        End If

        Dim targetGender As UInteger = If(state.IsFemale, 1UI, 0UI)
        For Each bd In race.BoneData
            If bd.Gender = targetGender Then
                If bd.Bones.Count > 0 Then Return (wt, wm, wf, bd, npcData.BodyMorphRegionValues, armaDeltas, nnamX, nnamY)
                Exit For
            End If
        Next
        If hasArmaDeltas Then
            Return (wt, wm, wf, New RACE_BoneDataGender With {.Gender = targetGender}, npcData.BodyMorphRegionValues, armaDeltas, nnamX, nnamY)
        End If
        Return Nothing
    End Function

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
    ''' <summary>Walk the skeleton hierarchy from a bone upward to determine which MRSV body
    ''' morph region (0..4) the bone belongs to. Returns -1 if no known region ancestor found.
    ''' The mapping is based on matching ancestor bone names to the major skeleton "trunk" bones:
    '''   HEAD → 0 (Head)
    '''   Chest, SPINE2, Neck → 1 (Upper Torso)
    '''   Arm (anywhere in ancestor chain) → 2 (Arms)
    '''   SPINE1, Pelvis, Butt (anywhere in ancestor chain) → 3 (Lower Torso)
    '''   Leg (anywhere in ancestor chain) → 4 (Legs)
    ''' This is inferred from the skeleton hierarchy, not from any Bethesda data file.</summary>
    Private Shared Function ResolveMrsvRegion(bone As Skeleton_Class.HierarchiBone_class) As Integer
        Dim cur = bone
        Dim depth = 0
        While cur IsNot Nothing AndAlso depth < 20
            Dim n = cur.BoneName
            If n IsNot Nothing Then
                Dim upper = n.ToUpperInvariant()
                If upper.Contains("LEG") OrElse upper.Contains("LLEG") OrElse upper.Contains("RLEG") Then Return 4
                If upper.Contains("ARM") OrElse upper.Contains("LARM") OrElse upper.Contains("RARM") Then Return 2
                If upper.Contains("BUTT") Then Return 3
                If upper = "HEAD" OrElse upper.StartsWith("HEAD") Then Return 0
                If upper = "SPINE1" OrElse upper = "SPINE1_OFFSET" Then Return 3
                If upper = "PELVIS" OrElse upper = "PELVIS_OFFSET" Then Return 3
                If upper = "SPINE2" OrElse upper = "SPINE2_OFFSET" Then Return 1
                If upper = "CHEST" OrElse upper = "CHEST_OFFSET" Then Return 1
                If upper = "NECK" OrElse upper = "NECK_OFFSET" Then Return 1
            End If
            cur = cur.Parent
            depth += 1
        End While
        Return -1
    End Function

    ''' <summary>Skeleton resolver that merges the race's face skeleton into SkeletonDictionary
    ''' and applies whatever pose is passed. ALL pose building (FMRS + body-weight + race-height)
    ''' happens at the caller level (via BuildMergedNpcPose) and gets merged into intent.Pose.
    ''' The resolver is intentionally "dumb" — just a skel-loader + pose applier.</summary>
    Private Class FaceBoneSkeletonResolver
        Implements ISkeletonResolver

        Private ReadOnly _baseResolver As ISkeletonResolver
        Private ReadOnly _faceSkelBytes As Byte()

        Public Sub New(baseResolver As ISkeletonResolver, faceSkelBytes As Byte())
            _baseResolver = baseResolver
            _faceSkelBytes = faceSkelBytes
        End Sub

        Public Sub ResolveSkeleton(shapes As IEnumerable(Of IRenderableShape), pose As Poses_class) Implements ISkeletonResolver.ResolveSkeleton
            ' 1. Load body skeleton (pose=Nothing — pose application is step 3).
            _baseResolver.ResolveSkeleton(shapes, Nothing)
            ' 2. Merge face skeleton (adds face bones to dict with DeltaTransform=Nothing).
            If _faceSkelBytes IsNot Nothing Then
                Skeleton_Class.MergeFaceSkeleton(_faceSkelBytes)
            End If
            ' 3. Apply merged pose. AppplyPoseToSkeleton resets all DeltaTransforms to identity
            '    first, then applies pose.Transforms. Pass even an empty Poses_class so the
            '    reset runs and clears stale deltas from a previous render.
            If pose IsNot Nothing Then
                Skeleton_Class.AppplyPoseToSkeleton(pose)
            End If
        End Sub
    End Class

    ''' <summary>Produce a pose with a single Root.Scale delta carrying the race height factor.
    ''' Empty / identity if raceHeight ≈ 1. The Scale propagates to every descendant bone via
    ''' Transform_Class.ComposeTransforms (NIF convention T·R·S with scale inheritance).</summary>
    Private Shared Function BuildRaceHeightPose(raceHeight As Single) As Poses_class
        Dim pose As New Poses_class With {
            .Name = "Race Height",
            .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
            .Transforms = New Dictionary(Of String, PoseTransformData)(StringComparer.OrdinalIgnoreCase)
        }
        If Math.Abs(raceHeight - 1.0F) > 0.0001F Then
            pose.Transforms("Root") = New PoseTransformData With {.Scale = raceHeight}
        End If
        Return pose
    End Function

    ''' <summary>Read race height (Male/Female Height from RACE.DATA) for the NPC's race. 1.0 if unknown.</summary>
    Private Function GetRaceHeight(state As NPCVisualState) As Single
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return 1.0F
        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return 1.0F
        Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)
        Dim h = If(state.IsFemale, race.FemaleHeight, race.MaleHeight)
        If h <= 0 Then Return 1.0F
        Return h
    End Function

    ''' <summary>Build the merged NPC pose: race-height + body-weight (MWGT×BSMS+MRSV+ARMA) + FMRS.
    ''' Order: race → body-weight → FMRS (top-down by skeleton hierarchy). The sources write to
    ''' disjoint field sets of PoseTransformData (race→Scale, BW→ScaleX/Y/Z, FMRS→T/R), so field-
    ''' level merging preserves each source's contribution even if the same bone appears in two.
    ''' See MergePoses for overlap detection (logs a [POSE-MERGE-OVERLAP] warning if sources collide
    ''' on the same field — should never fire with current sources).
    '''
    ''' Caller contract: Skeleton_Class.SkeletonDictionary must be populated BEFORE this is called,
    ''' because BuildBodyWeightPose walks the skeleton hierarchy via ResolveMrsvRegion to map bones
    ''' to MRSV regions. RenderCurrentStateAsync primes it by calling baseSkelResolver.ResolveSkeleton
    ''' eagerly before invoking this helper.</summary>
    Private Function BuildMergedNpcPose(state As NPCVisualState, renderData As PreviewResolutionResult,
                                        faceMorphsEnabled As Boolean,
                                        bodyWeightEnabled As Boolean) As Poses_class
        Dim racePose = BuildRaceHeightPose(GetRaceHeight(state))

        Dim bwPose As Poses_class = Nothing
        If bodyWeightEnabled Then
            Dim bwData = ResolveBodyWeightData(state, renderData)
            If bwData.GenderBlock IsNot Nothing Then
                bwPose = BuildBodyWeightPose(bwData.Wt, bwData.Wm, bwData.Wf,
                                             bwData.GenderBlock, bwData.MrsvValues, bwData.ArmaDeltas,
                                             bwData.NnamX, bwData.NnamY)
            End If
        End If

        Dim fmrsPose As Poses_class = Nothing
        If faceMorphsEnabled Then
            fmrsPose = BuildFaceBoneTransforms(state)
        End If

        Return MergePoses(racePose, bwPose, fmrsPose)
    End Function

    ''' <summary>Field-level merge of multiple Poses_class into one. For each PoseTransformData field
    ''' (X/Y/Z/Pitch/Roll/Yaw/Scale/ScaleX/ScaleY/ScaleZ), non-identity values from later sources
    ''' overwrite earlier ones. If two sources both have non-identity on the same field → log a
    ''' [POSE-MERGE-OVERLAP] warning and use last-wins. The 3 pose sources (race/BW/FMRS) write to
    ''' disjoint field sets by design, so overlap should never fire — it's a canary for future
    ''' architectural regressions.</summary>
    Private Shared Function MergePoses(ParamArray sources As Poses_class()) As Poses_class
        Dim merged As New Poses_class With {
            .Name = "NPC Bone Transforms",
            .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
            .Transforms = New Dictionary(Of String, PoseTransformData)(StringComparer.OrdinalIgnoreCase)
        }
        For Each src In sources
            If src Is Nothing Then Continue For
            For Each kv In src.Transforms
                Dim bone = kv.Key
                Dim newPose = kv.Value
                Dim existing As PoseTransformData = Nothing
                If merged.Transforms.TryGetValue(bone, existing) Then
                    Dim conflicts As New List(Of String)
                    If newPose.X <> 0 Then
                        If existing.X <> 0 Then conflicts.Add("X")
                        existing.X = newPose.X
                    End If
                    If newPose.Y <> 0 Then
                        If existing.Y <> 0 Then conflicts.Add("Y")
                        existing.Y = newPose.Y
                    End If
                    If newPose.Z <> 0 Then
                        If existing.Z <> 0 Then conflicts.Add("Z")
                        existing.Z = newPose.Z
                    End If
                    If newPose.Pitch <> 0 Then
                        If existing.Pitch <> 0 Then conflicts.Add("Pitch")
                        existing.Pitch = newPose.Pitch
                    End If
                    If newPose.Roll <> 0 Then
                        If existing.Roll <> 0 Then conflicts.Add("Roll")
                        existing.Roll = newPose.Roll
                    End If
                    If newPose.Yaw <> 0 Then
                        If existing.Yaw <> 0 Then conflicts.Add("Yaw")
                        existing.Yaw = newPose.Yaw
                    End If
                    If newPose.Scale <> 1 Then
                        If existing.Scale <> 1 Then conflicts.Add("Scale")
                        existing.Scale = newPose.Scale
                    End If
                    If newPose.ScaleX <> 1 Then
                        If existing.ScaleX <> 1 Then conflicts.Add("ScaleX")
                        existing.ScaleX = newPose.ScaleX
                    End If
                    If newPose.ScaleY <> 1 Then
                        If existing.ScaleY <> 1 Then conflicts.Add("ScaleY")
                        existing.ScaleY = newPose.ScaleY
                    End If
                    If newPose.ScaleZ <> 1 Then
                        If existing.ScaleZ <> 1 Then conflicts.Add("ScaleZ")
                        existing.ScaleZ = newPose.ScaleZ
                    End If
                    If conflicts.Count > 0 Then
                        NpcPreviewLog.Log($"  [POSE-MERGE-OVERLAP] bone='{bone}' fields={String.Join(",", conflicts)} (last-wins — race/BW/FMRS should be disjoint)")
                    End If
                    merged.Transforms(bone) = existing
                Else
                    merged.Transforms(bone) = newPose
                End If
            Next
        Next
        Return merged
    End Function

    ''' <summary>Build a pose of non-uniform bone-scale deltas from NPC MWGT + RACE BSMS
    ''' (weight scale layer) and NPC MRSV + RACE BSMS "Range" (region modifier layer).
    ''' Requires SkeletonDictionary populated (ResolveMrsvRegion walks bone.Parent chain).</summary>
    Private Shared Function BuildBodyWeightPose(wt As Single, wm As Single, wf As Single,
                                                 genderBlock As RACE_BoneDataGender,
                                                 mrsvValues As List(Of Single),
                                                 armaDeltas As Dictionary(Of String, System.Numerics.Vector3),
                                                 nnamX As Single, nnamY As Single) As Poses_class
        Const Eps As Single = 0.001F
        Dim pose As New Poses_class With {
                .Name = "MWGT Body Weight",
                .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
                .Transforms = New Dictionary(Of String, PoseTransformData)
            }
        Dim affected As Integer = 0
        Dim skippedNoSkel As Integer = 0
        Dim skippedNegligibleScale As Integer = 0
        Dim unmatched As New List(Of String)

        ' Diagnostic buffer: per-bone rows for a compact summary at the end.
        Dim diag As New List(Of (Name As String, Sx As Single, Sy As Single, Sz As Single, RestY As Single, RestZ As Single, Region As Integer, Slider As Single, ArmaDX As Single, ArmaDY As Single, ArmaDZ As Single))

        ' Build the bone set as union(RACE.BoneData, ARMA.BoneScaleDeltas). ARMA may cover
        ' bones that RACE doesn't list for this gender (outfit-specific bones) — we still
        ' apply their delta on top of the identity RACE scale.
        Dim boneLookup As New Dictionary(Of String, RACE_BoneData)(StringComparer.OrdinalIgnoreCase)
        For Each b In genderBlock.Bones
            boneLookup(b.BoneName) = b
        Next
        Dim allBoneNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each b In genderBlock.Bones
            allBoneNames.Add(b.BoneName)
        Next
        If armaDeltas IsNot Nothing Then
            For Each kv In armaDeltas
                allBoneNames.Add(kv.Key)
            Next
        End If

        For Each boneName In allBoneNames
            Dim skelBone As Skeleton_Class.HierarchiBone_class = Nothing
            Dim restY As Single = 0.0F, restZ As Single = 0.0F
            If Skeleton_Class.SkeletonDictionary.TryGetValue(boneName, skelBone) Then
                If skelBone.OriginalLocaLTransform IsNot Nothing Then
                    restY = skelBone.OriginalLocaLTransform.Translation.Y
                    restZ = skelBone.OriginalLocaLTransform.Translation.Z
                End If
            Else
                skippedNoSkel += 1
                If unmatched.Count < 20 Then unmatched.Add(boneName)
                Continue For
            End If

            ' RACE.BSMS WeightScale = 9 floats = 3 × Vec3 (Thin, Musc, Fat) × (X, Y, Z).
            ' Parser reads all 9 (RecordParsers.vb:1216-1226). Previously only Y/Z were consumed
            ' here; X was silently discarded. Fixed 2026-04-19 per audit — ignored X caused the
            ' systematic X-dominant residual vs CK FaceGen bake at shared neck bones.
            Dim sx As Single = 1.0F, sy As Single = 1.0F, sz As Single = 1.0F
            Dim bone As RACE_BoneData = Nothing
            boneLookup.TryGetValue(boneName, bone)

            If bone IsNot Nothing AndAlso bone.HasWeightScale Then
                sx = bone.ThinX * wt + bone.MuscularX * wm + bone.FatX * wf
                sy = bone.ThinY * wt + bone.MuscularY * wm + bone.FatY * wf
                sz = bone.ThinZ * wt + bone.MuscularZ * wm + bone.FatZ * wf
            End If

            ' NNAM ("Neck Fat Adjustments Scale" — RACE.NNAM inside the head block, xEdit spec
            ' wbDefinitionsFO4.pas:11639/11657). HIPÓTESIS H1 2026-04-19: multiplicative neck-fat
            ' modifier on RACE-declared weight-scale bones whose name contains "Neck"; driven
            ' only by MWGT.Fat (matches the record's literal name "Neck Fat"). "Budget realloc"
            ' model falsified 2026-04-19 (see npc_manager_closure_plan P2). The 4-byte Unknown
            ' prefix is ignored: HumanRace vanilla has it zero, semantics unresolved. Reaches the
            ' head-mesh neck verts via bones shared between head and body skin (Neck_skin,
            ' Neck_Low_skin, Neck1_skin). Validate with harness [BW-only] RMS Científica < 0.10
            ' before promoting out of hypothesis.
            If bone IsNot Nothing AndAlso bone.HasWeightScale _
               AndAlso boneName.IndexOf("Neck", StringComparison.OrdinalIgnoreCase) >= 0 _
               AndAlso (Math.Abs(nnamX) > Single.Epsilon OrElse Math.Abs(nnamY) > Single.Epsilon) Then
                Dim sxBefore As Single = sx
                Dim syBefore As Single = sy
                sx *= (1.0F + nnamX * wf)
                sy *= (1.0F + nnamY * wf)
                NpcPreviewLog.Log($"    [NNAM-APPLY] bone='{boneName}' fat={wf:F3} nnam=({nnamX:F4},{nnamY:F4}) sx {sxBefore:F4}→{sx:F4} sy {syBefore:F4}→{sy:F4}")
            End If

            ' BSMS RangeModifier spec has only Min/Max Y and Z (no X) — per
            ' wbDefinitionsFO4.pas:5929. MRSV does NOT contribute to X.
            Dim region As Integer = -1
            Dim slider As Single = 0.0F
            If bone IsNot Nothing AndAlso bone.HasRangeModifier AndAlso mrsvValues IsNot Nothing AndAlso mrsvValues.Count >= 5 Then
                region = ResolveMrsvRegion(skelBone)
                If region >= 0 AndAlso region < mrsvValues.Count Then
                    slider = mrsvValues(region)
                    If slider >= 0 Then
                        sy += slider * bone.MaxY
                        sz += slider * bone.MaxZ
                    Else
                        sy += (-slider) * bone.MinY
                        sz += (-slider) * bone.MinZ
                    End If
                End If
            End If

            ' TEST: ARMA as AMPLIFIER of RACE delta from identity. User observation: arms
            ' thinner in CK (RACE shrinks arms), hips wider in CK (RACE expands butt); all
            ' ARMA values positive. A single-sign addition/subtraction cannot explain both
            ' directions. AMPLIFYING race's own delta does:
            '   sy = 1 + (race_sy - 1) * (1 + arma.Y)
            ' Properties:
            '  - arma.Y = 0  → sy = race_sy (identity multiplier).
            '  - race_sy = 1 → sy = 1 (no amplification when neutral).
            '  - race shrinks + positive arma → stronger shrink (arms thinner).
            '  - race expands + positive arma → stronger expand (butt wider).
            ' X axis added 2026-04-19 for symmetry with RACE.BSMS's 3-axis WeightScale.
            Dim armaDX As Single = 0.0F, armaDY As Single = 0.0F, armaDZ As Single = 0.0F
            If armaDeltas IsNot Nothing Then
                Dim d As System.Numerics.Vector3
                If armaDeltas.TryGetValue(boneName, d) Then
                    armaDX = d.X
                    armaDY = d.Y
                    armaDZ = d.Z
                    sx = 1.0F + (sx - 1.0F) * (1.0F + armaDX)
                    sy = 1.0F + (sy - 1.0F) * (1.0F + armaDY)
                    sz = 1.0F + (sz - 1.0F) * (1.0F + armaDZ)
                End If
            End If

            If Math.Abs(sx - 1.0F) < Eps AndAlso Math.Abs(sy - 1.0F) < Eps AndAlso Math.Abs(sz - 1.0F) < Eps Then
                skippedNegligibleScale += 1
                Continue For
            End If

            diag.Add((boneName, sx, sy, sz, restY, restZ, region, slider, armaDX, armaDY, armaDZ))

            pose.Transforms(boneName) = New PoseTransformData With {
                    .ScaleX = sx,
                    .ScaleY = sy,
                    .ScaleZ = sz
                }
            affected += 1
        Next

        Dim mrsvStr = If(mrsvValues Is Nothing OrElse mrsvValues.Count = 0,
                             "null/empty",
                             String.Join(",", mrsvValues.Select(Function(v) v.ToString("F3"))))
        Dim armaCount = If(armaDeltas Is Nothing, 0, armaDeltas.Count)
        NpcPreviewLog.Log($"  [BODY-WEIGHT] MWGT=({wt:F3},{wm:F3},{wf:F3}) NNAM=({nnamX:F4},{nnamY:F4}) MRSV=[{mrsvStr}] ARMA-deltas={armaCount} bones: union={allBoneNames.Count} affected={affected} skipped=[noSkel={skippedNoSkel} negScale={skippedNegligibleScale}]")
        If unmatched.Count > 0 Then
            NpcPreviewLog.Log($"    [BW-UNMATCHED-BONES] {String.Join(", ", unmatched)}")
        End If

        If diag.Count > 0 Then
            For Each r In diag.OrderBy(Function(x) x.Name)
                NpcPreviewLog.Log($"    [BW-BONE] {r.Name} sx={r.Sx:F4} sy={r.Sy:F4} sz={r.Sz:F4} restY={r.RestY:F3} restZ={r.RestZ:F3} region={r.Region} slider={r.Slider:F3} armaDX={r.ArmaDX:F4} armaDY={r.ArmaDY:F4} armaDZ={r.ArmaDZ:F4}")
            Next
        End If

        If affected = 0 Then Return Nothing
        Return pose
    End Function

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
        Return path <> "" AndAlso FilesDictionary_class.Dictionary.ContainsKey(path)
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

        ' Aggregate per-bone ARMA BSMS deltas across only the winning candidates so covered/hidden
        ' ARMAs don't contribute. This is what BuildBodyWeightPose reads to shape the outfit.
        For Each c In selectedCandidates
            If c.ArmaBoneScaleDeltas Is Nothing Then Continue For
            For Each bd In c.ArmaBoneScaleDeltas
                Dim delta = New System.Numerics.Vector3(bd.DeltaX, bd.DeltaY, bd.DeltaZ)
                Dim existing As System.Numerics.Vector3
                If result.ArmaBoneScaleDeltas.TryGetValue(bd.BoneName, existing) Then
                    result.ArmaBoneScaleDeltas(bd.BoneName) = existing + delta
                Else
                    result.ArmaBoneScaleDeltas(bd.BoneName) = delta
                End If
            Next
        Next
        If result.ArmaBoneScaleDeltas.Count > 0 Then
            NpcPreviewLog.Log($"  [ARMA-BSMS-AGG] {result.ArmaBoneScaleDeltas.Count} unique bones with aggregated delta (sum across winning ARMAs)")
        End If

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
        clone.ObjectTemplateOMODFormIDs.AddRange(state.ObjectTemplateOMODFormIDs)
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
        state.ObjectTemplateOMODFormIDs.AddRange(model.ObjectTemplateOMODFormIDs)
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

        Dim mergedHeadParts = MergeHeadPartsWithRaceDefaults(state)
        CollectHeadPartCandidates(mergedHeadParts, New HashSet(Of UInteger)(), candidates, order, warnings, useFaceGen)

        ' Robot body parts via NPC_.ObjectTemplate OBTS Includes → OMOD.ModelPath.
        ' For vanilla robots (Assaultron, Mr Handy, etc.) the per-part meshes live in the OMOD records
        ' referenced from the first combination of NPC_.ObjectTemplate. Each OMOD has a MODL mesh path
        ' (captured at CraftingRecords.OMOD_Data.ModelPath). Emit one Skin-kind candidate per OMOD that
        ' has a non-empty ModelPath. Spec: project_robot_rendering_combinations.md (memory).
        If state.ObjectTemplateOMODFormIDs IsNot Nothing AndAlso state.ObjectTemplateOMODFormIDs.Count > 0 Then
            NpcPreviewLog.Log($"  [ROBOT-PARTS] NPC has {state.ObjectTemplateOMODFormIDs.Count} OMOD includes in ObjectTemplate combination #0")
            For Each omodFID In state.ObjectTemplateOMODFormIDs
                If omodFID = 0UI Then Continue For
                Dim omodRec = _pluginManager.GetRecord(omodFID)
                If omodRec Is Nothing OrElse omodRec.Header.Signature <> "OMOD" Then
                    NpcPreviewLog.Log($"    [ROBOT-PARTS] {omodFID:X8} → OMOD NOT FOUND")
                    Continue For
                End If
                Dim omod = CraftingRecordParsers.ParseOMOD(omodRec, _pluginManager)
                If String.IsNullOrEmpty(omod.ModelPath) Then
                    NpcPreviewLog.Log($"    [ROBOT-PARTS] {omodFID:X8} '{omod.EditorID}' → empty ModelPath, skipped")
                    Continue For
                End If
                Dim dictKey = NormalizeDictionaryKeyWithMeshesPrefix(omod.ModelPath)
                NpcPreviewLog.Log($"    [ROBOT-PARTS] {omodFID:X8} '{omod.EditorID}' → mesh='{dictKey}'")
                candidates.Add(New MeshCandidate With {
                    .DictKey = dictKey,
                    .SlotMask = 0UI,
                    .Priority = 0,
                    .Kind = MeshCandidateKind.Skin,
                    .SourceFormID = omodFID,
                    .Order = order
                })
                order += 1
            Next
        End If

        Return candidates
    End Function

    ''' <summary>Merge NPC.PNAM head parts with RACE.HeadParts defaults per vanilla CK semantics.
    ''' Main types (1=Face, 2=Eyes, 3=Hair, 4=FacialHair, 5=Scar, 6=Eyebrows, 7=Meatcaps, 8=Teeth, 9=HeadRear):
    ''' NPC override wins; fall back to RACE default per type (gender-specific).
    ''' Type 0 Misc: should only appear as extras inside each main HDPT's HNAM; freestanding top-level
    ''' type=0 entries (rare/undocumented in vanilla) are preserved as additive to avoid data loss.
    ''' HDPT spec: [wbDefinitionsFO4.pas:7373-7384](../../FO4_Base_Library/../TES5Edit/Core/wbDefinitionsFO4.pas#L7373-L7384).
    ''' RACE.HeadParts per gender: parsed into RACE_Data.MaleHeadPartFormIDs / FemaleHeadPartFormIDs
    ''' at [RecordParsers.vb:920-927](../../../../FO4_Base_Library/ESP/Records/RecordParsers.vb#L920-L927).
    ''' Logs one [HEADPARTS-MERGE] summary line + per-type decision for traceability.</summary>
    Private Function MergeHeadPartsWithRaceDefaults(state As NPCVisualState) As List(Of UInteger)
        If state Is Nothing OrElse state.RaceFormID = 0UI Then
            Return state.HeadPartFormIDs.ToList()
        End If
        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then
            Return state.HeadPartFormIDs.ToList()
        End If
        Dim race = RecordParsers.ParseRACE(raceRec, _pluginManager)
        Dim raceDefaults = If(state.IsFemale, race.FemaleHeadPartFormIDs, race.MaleHeadPartFormIDs)

        ' Build merged dict by PartType for main types (1..9). Track provenance for logging.
        Dim mergedByType As New Dictionary(Of Integer, UInteger)
        Dim provenanceByType As New Dictionary(Of Integer, String) ' value format "RACE:{edid}" or "NPC:{edid}"
        Dim freestandingMisc As New List(Of UInteger)
        Dim miscProvenance As New List(Of String)

        ' Step 1: seed with RACE defaults
        For Each defFID In raceDefaults
            Dim defRec = _pluginManager.GetRecord(defFID)
            If defRec Is Nothing OrElse defRec.Header.Signature <> "HDPT" Then Continue For
            Dim hdpt = RecordParsers.ParseHDPT(defRec, _pluginManager)
            If hdpt.PartType = 0 Then
                freestandingMisc.Add(defFID)
                miscProvenance.Add($"RACE:{hdpt.EditorID}")
            ElseIf hdpt.PartType >= 1 AndAlso hdpt.PartType <= 9 Then
                mergedByType(hdpt.PartType) = defFID
                provenanceByType(hdpt.PartType) = $"RACE:{hdpt.EditorID}"
            End If
        Next

        ' Step 2: override with NPC.PNAM (NPC wins per main type, or accumulates for misc)
        For Each npcFID In state.HeadPartFormIDs
            Dim npcRec = _pluginManager.GetRecord(npcFID)
            If npcRec Is Nothing OrElse npcRec.Header.Signature <> "HDPT" Then Continue For
            Dim hdpt = RecordParsers.ParseHDPT(npcRec, _pluginManager)
            If hdpt.PartType = 0 Then
                freestandingMisc.Add(npcFID)
                miscProvenance.Add($"NPC:{hdpt.EditorID}")
            ElseIf hdpt.PartType >= 1 AndAlso hdpt.PartType <= 9 Then
                mergedByType(hdpt.PartType) = npcFID
                provenanceByType(hdpt.PartType) = $"NPC:{hdpt.EditorID}"
            End If
        Next

        ' Step 3: build final list (main types sorted by type number + freestanding misc after)
        Dim finalList As New List(Of UInteger)
        For Each t In mergedByType.Keys.OrderBy(Function(k) k)
            finalList.Add(mergedByType(t))
        Next
        finalList.AddRange(freestandingMisc)

        ' Step 4: summary log — one line with per-type decision for traceability per NPC.
        Dim typeNames = New String() {"Misc", "Face", "Eyes", "Hair", "FacialHair", "Scar", "Eyebrows", "Meatcaps", "Teeth", "HeadRear"}
        Dim summary As New System.Text.StringBuilder
        summary.Append($"  [HEADPARTS-MERGE] RACE '{race.EditorID}' {If(state.IsFemale, "F", "M")} | NPC.PNAM={state.HeadPartFormIDs.Count} race.defaults={raceDefaults.Count} → merged={finalList.Count}")
        NpcPreviewLog.Log(summary.ToString())
        For t = 1 To 9
            Dim prov As String = Nothing
            If provenanceByType.TryGetValue(t, prov) Then
                Dim from = If(prov.StartsWith("NPC:"), "NPC", "RACE-DEFAULT")
                NpcPreviewLog.Log($"    [HEADPARTS-MERGE] type={t}/{typeNames(t)}: from={from} {prov.Substring(prov.IndexOf(":"c) + 1)}")
            End If
        Next
        If freestandingMisc.Count > 0 Then
            NpcPreviewLog.Log($"    [HEADPARTS-MERGE] freestanding-misc (type=0): {freestandingMisc.Count} entries [{String.Join(", ", miscProvenance)}]")
        End If
        Dim missedTypes = New List(Of String)
        For t = 1 To 9
            If Not provenanceByType.ContainsKey(t) Then missedTypes.Add(typeNames(t))
        Next
        If missedTypes.Count > 0 Then
            NpcPreviewLog.Log($"    [HEADPARTS-MERGE] no-data-for-types: {String.Join(", ", missedTypes)} (neither RACE nor NPC declared; slot left empty)")
        End If

        Return finalList
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
        NpcPreviewLog.Log($"  [ARMO] {armo.EditorID} FID={armoFormID:X8} kind={kind} race={armo.RaceFormID:X8} slot={armo.SlotMask:X8} addons={armo.ArmorAddonFormIDs.Count} tnam={armo.TemplateArmorFormID:X8}")
        If armo.MaleWorldModelPath <> "" OrElse armo.FemaleWorldModelPath <> "" Then
            NpcPreviewLog.Log($"    [ARMO-WORLDMODEL] male='{armo.MaleWorldModelPath}' female='{armo.FemaleWorldModelPath}'")
        End If
        ' NO early-out on ARMO.RaceFormID: vanilla convention is each ARMA declares its own
        ' race compatibility via RNAM + AdditionalRaces (MODL entries). An ARMO with
        ' RNAM=HumanRace is commonly worn by Ghouls/Synths if the sub-ARMAs list those as
        ' AdditionalRaces. The per-ARMA check (ArmorAddonMatchesRace) handles this correctly.
        ' Log the ARMO race only for visibility; don't reject based on it.
        If armo.RaceFormID <> 0UI AndAlso armo.RaceFormID <> state.RaceFormID Then
            NpcPreviewLog.Log($"    [ARMO-RACE-INFO] primary race={armo.RaceFormID:X8} ≠ npc race={state.RaceFormID:X8} — continuing; per-ARMA match will decide")
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
            NpcPreviewLog.Log($"      [ARMA-FLAGS] {arma.EditorID} NoUnderarmorScaling={arma.NoUnderarmorScaling} HasSculptData={arma.HasSculptData} HiRes1stPerson={arma.HiRes1stPersonOnly} MaleWSFlags=0x{arma.MaleWeightSliderFlags:X2}(enabled={(arma.MaleWeightSliderFlags And 2) <> 0}) FemaleWSFlags=0x{arma.FemaleWeightSliderFlags:X2}(enabled={(arma.FemaleWeightSliderFlags And 2) <> 0})")

            ' Pick the gender-matching bone scale block (if any) and log + stash it on the
            ' candidate. Engine-side these per-bone Vec3 deltas are added on top of RACE.BSMS
            ' to shape the outfit around the body (cinched waist, wider hips, vest volume).
            Dim targetGender As UInteger = If(state.IsFemale, 1UI, 0UI)
            Dim genderBoneScale As List(Of ARMA_BoneScaleDelta) = Nothing
            For Each bsg In arma.BoneScaleData
                If bsg.Gender <> targetGender Then Continue For
                If bsg.Bones.Count = 0 Then Continue For
                genderBoneScale = bsg.Bones
                NpcPreviewLog.Log($"      [ARMA-BSMS] {arma.EditorID} gender={bsg.Gender} {bsg.Bones.Count} bone-deltas:")
                For Each bd In bsg.Bones
                    Dim mag = Math.Sqrt(bd.DeltaX * bd.DeltaX + bd.DeltaY * bd.DeltaY + bd.DeltaZ * bd.DeltaZ)
                    NpcPreviewLog.Log($"        {bd.BoneName} = ({bd.DeltaX:F4}, {bd.DeltaY:F4}, {bd.DeltaZ:F4}) |mag|={mag:F4}")
                Next
                Exit For
            Next

            ' Resolve mesh path with ARMA-first / ARMO-WorldModel-fallback semantics.
            ' ARMO.MOD2 (male) / MOD4 (female) per wbDefinitionsFO4.pas:6164-6175 populate when the
            ' mesh is authored at ARMO level (robots: Assaultron skin has ARMO.MOD2=Assaultron.nif
            ' with empty ARMA.MOD2/MOD3). Humanoid armors inverse: ARMA has the mesh, ARMO.MOD2/MOD4
            ' usually empty. Gender mirror inside each source: try same-gender first, then opposite.
            Dim meshPath = If(state.IsFemale, arma.FemaleMeshPath, arma.MaleMeshPath)
            If meshPath = "" Then meshPath = If(arma.MaleMeshPath <> "", arma.MaleMeshPath, arma.FemaleMeshPath)
            If meshPath = "" Then
                meshPath = If(state.IsFemale, armo.FemaleWorldModelPath, armo.MaleWorldModelPath)
                If meshPath = "" Then meshPath = If(armo.MaleWorldModelPath <> "", armo.MaleWorldModelPath, armo.FemaleWorldModelPath)
                If meshPath <> "" Then
                    NpcPreviewLog.Log($"      [ARMA→ARMO-FALLBACK] {arma.EditorID}: ARMA meshes empty, using ARMO.WorldModel '{meshPath}'")
                End If
            End If
            If meshPath = "" Then
                NpcPreviewLog.Log($"      [ARMA-NO-MESH] {arma.EditorID}: neither ARMA.MOD2/3 nor ARMO.MOD2/4 had a path — likely robot using NPC_.ObjectTemplate/OMOD pipeline (see project_robot_rendering_combinations.md)")
                Continue For
            End If

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
                .Order = order,
                .ArmaBoneScaleDeltas = genderBoneScale
            })

            order += 1
        Next
    End Sub

    Private Sub CollectHeadPartCandidates(headPartFormIDs As IEnumerable(Of UInteger),
                                          visited As HashSet(Of UInteger),
                                          candidates As List(Of MeshCandidate),
                                          ByRef order As Integer,
                                          warnings As List(Of String),
                                          Optional useFaceGen As Boolean = False)
        For Each hdptFormID In headPartFormIDs.Where(Function(id) id <> 0UI)
            CollectHeadPartCandidate(hdptFormID, visited, candidates, order, warnings, -1, useFaceGen)
        Next
    End Sub

    Private Sub CollectHeadPartCandidate(hdptFormID As UInteger,
                                         visited As HashSet(Of UInteger),
                                         candidates As List(Of MeshCandidate),
                                         ByRef order As Integer,
                                         warnings As List(Of String),
                                         parentPartType As Integer,
                                         Optional useFaceGen As Boolean = False)
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
            ' Redirect face-region meshes to their _faceBones.nif variant only for NPCs with
            ' a custom CharGen face (useFaceGen=True). The _faceBones variants are rigged to face
            ' bones (Jaw, LipUpper_L, Cheek_R, etc) enabling FMRS bone transforms to deform the
            ' mesh. NPCs without FaceGen use default race face — no _faceBones redirect needed.
            Dim dictKey = NormalizeDictionaryKeyWithMeshesPrefix(hdpt.MeshPath)
            If useFaceGen Then
                Dim faceBonesKey = TryGetFaceBonesVariant(dictKey, effectivePartType)
                If faceBonesKey <> "" Then
                    NpcPreviewLog.Log($"  [FACEBONES-REDIRECT] {dictKey} ? {faceBonesKey}")
                    dictKey = faceBonesKey
                End If
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
                .ChargenMorphTriPath = hdpt.ChargenMorphTriPath,
                .Hide = (effectivePartType = 7)
            })
            order += 1
        End If

        ' Pass the effective type down so nested extras also inherit
        Dim childParentType = If(effectivePartType <> 0, effectivePartType, parentPartType)
        For Each extraPartFormID In hdpt.ExtraPartFormIDs
            CollectHeadPartCandidate(extraPartFormID, visited, candidates, order, warnings, childParentType, useFaceGen)
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

        ' Filter out Hide=true candidates (e.g. HDPT type=7 Meatcaps: occluded by teeth in static pose).
        ' They stay in the `candidates` list for logging/inspection but never reach the render dispatch.
        Dim hiddenCandidates = candidates.Where(Function(c) c.Hide).ToList()
        If hiddenCandidates.Count > 0 Then
            NpcPreviewLog.Log($"  [CANDIDATE-HIDDEN] {hiddenCandidates.Count} candidates excluded from render (Hide=true): {String.Join(", ", hiddenCandidates.Select(Function(c) $"type={c.HeadPartType} key={c.DictKey}"))}")
        End If
        Dim visibleCandidates = candidates.Where(Function(c) Not c.Hide).ToList()

        ' First pass: resolve slotted candidates (outfit wins over skin on same slot)
        Dim occupiedSlots As UInteger = 0UI
        Dim slottedCandidates = visibleCandidates.Where(Function(c) c.SlotMask <> 0UI).
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

        For Each slotlessCandidate In visibleCandidates.Where(Function(c) c.SlotMask = 0UI).OrderBy(Function(c) c.Order)
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
            If relatedMaterial Is Nothing Then Continue For

            ApplyTextureSetOverrides(textureSet, relatedMaterial)

            Dim material = relatedMaterial.material
            If material Is Nothing Then Continue For

            If hasHairPaletteRemap AndAlso IsHairHeadPart(candidate) AndAlso material.GrayscaleToPaletteColor Then
                ' Material already uses grayscale-to-palette: override scale with CLFM RemappingIndex
                material.GrayscaleToPaletteScale = hairPaletteScale
                If hairPaletteTexture <> "" Then
                    material.GreyscaleTexture = hairPaletteTexture
                End If
            ElseIf material.Hair OrElse material.GrayscaleToPaletteColor Then
                ' Material is hair or uses grayscale-to-palette: try palette remap first, tint as fallback.
                Dim didPalette = False
                If state IsNot Nothing AndAlso state.HairColorFormID <> 0UI Then
                    Dim clfm = ResolveColorFormData(state.HairColorFormID)
                    If clfm IsNot Nothing AndAlso clfm.HasRemappingIndex Then
                        Dim palTex = ResolveRaceHairLookupTexture(state)
                        If palTex <> "" Then
                            material.GrayscaleToPaletteColor = True
                            material.GrayscaleToPaletteScale = clfm.RemappingIndex
                            material.GreyscaleTexture = palTex
                            didPalette = True
                        End If
                    End If
                End If
                If Not didPalette Then
                    Dim effectiveHairColor = hairTintColor
                    If Not effectiveHairColor.HasValue AndAlso state IsNot Nothing Then
                        effectiveHairColor = ResolveColorFormColor(state.HairColorFormID)
                    End If
                    If effectiveHairColor.HasValue Then material.HairTintColor = effectiveHairColor.Value
                End If
            End If

            If skinTintColor.HasValue AndAlso ShouldForceSkinTint(candidate, material) Then
                material.SkinTint = True
            End If

            If material.SkinTint AndAlso skinTintColor.HasValue Then
                material.SkinTintColor = skinTintColor.Value
            End If

            If solidTintColor.HasValue AndAlso Not material.Hair AndAlso Not material.SkinTint Then
                shape.TintColor = solidTintColor.Value
            End If
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
        If relatedMaterial.material IsNot Nothing AndAlso (relatedMaterial.path = "" OrElse hasResolvableMaterial) Then Return
        If shape.NifContent Is Nothing OrElse shape.NifShape Is Nothing OrElse shape.NifShader Is Nothing Then Return

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
        state.ObjectTemplateOMODFormIDs.AddRange(npc.ObjectTemplateOMODFormIDs)
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




























