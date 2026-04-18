Imports System.IO
Imports OpenTK.Mathematics
Imports FO4_Base_Library

' ==========================================================================
' Face morph resolver for FO4 NPCs.
'   MSDK/MSDV morph presets → Chargen.tri vertex morphs (via RACE MorphValueDefs/MorphPresetDefs).
'   FMRI/FMRS face bone sculpting is applied separately in MainForm.BuildFaceBoneTransforms
'   via skeleton DeltaTransform — not through this resolver.
' ==========================================================================
''' <summary>
''' IMorphResolver implementation that applies FO4 NPC face morph data to shapes.
''' </summary>
Public Class NpcMorphResolver
    Implements IMorphResolver

    Private ReadOnly _npcData As NPC_Data
    Private ReadOnly _meshDictKeys As Dictionary(Of IRenderableShape, String)
    Private ReadOnly _shapeChargenTriPaths As Dictionary(Of IRenderableShape, String)
    Private ReadOnly _shapeRaceMorphTriPaths As Dictionary(Of IRenderableShape, String)
    Private ReadOnly _morphValueDefs As List(Of RACE_MorphValueDef)      ' MSID -> MSM0/MSM1 from RACE
    Private ReadOnly _morphPresetDefs As List(Of RACE_MorphPresetDef)   ' MPPI -> MPPM from RACE Morph Groups
    Private ReadOnly _triCache As New Dictionary(Of String, TriFile)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _triHeadCache As New Dictionary(Of String, TriHeadFile)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _triLoadAttempted As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ' Standard body weight morph names used by CBBE, vanilla, and most body mods
    Private Shared ReadOnly BodyWeightMorphNames As String() = {"WeightThin", "WeightMuscular", "WeightFat"}

    ' Body morph region names (from BodySlide/CBBE TRI files, in order matching MRSV array)
    Private Shared ReadOnly BodyRegionMorphNames As String() = {
        "MorphRegion0", "MorphRegion1", "MorphRegion2", "MorphRegion3",
        "MorphRegion4", "MorphRegion5", "MorphRegion6", "MorphRegion7",
        "MorphRegion8", "MorphRegion9", "MorphRegion10", "MorphRegion11",
        "MorphRegion12", "MorphRegion13", "MorphRegion14", "MorphRegion15"
    }

    ''' <summary>
    ''' Create a morph resolver for an NPC.
    ''' </summary>
    ''' <param name="npcData">NPC morph data (weights, face morphs, etc.)</param>
    ''' <param name="meshDictKeys">Optional mapping of shape reference -> mesh dictionary key path (for TRI fallback lookup).</param>
    Public Sub New(npcData As NPC_Data,
                   Optional morphValueDefs As List(Of RACE_MorphValueDef) = Nothing,
                   Optional morphPresetDefs As List(Of RACE_MorphPresetDef) = Nothing,
                   Optional meshDictKeys As Dictionary(Of IRenderableShape, String) = Nothing,
                   Optional shapeChargenTriPaths As Dictionary(Of IRenderableShape, String) = Nothing,
                   Optional shapeRaceMorphTriPaths As Dictionary(Of IRenderableShape, String) = Nothing)
        _npcData = npcData
        _morphValueDefs = morphValueDefs
        _morphPresetDefs = morphPresetDefs
        _meshDictKeys = meshDictKeys
        _shapeChargenTriPaths = shapeChargenTriPaths
        _shapeRaceMorphTriPaths = shapeRaceMorphTriPaths
    End Sub

    Public Function ResolveMorphPlan(shape As IRenderableShape, geom As SkinnedGeometry) As MorphPlan Implements IMorphResolver.ResolveMorphPlan
        Dim plan As New MorphPlan()
        If _npcData Is Nothing OrElse shape Is Nothing Then Return plan

        Dim shapeName = If(shape.ShapeName, "")
        If shapeName = "" Then Return plan

        ' Load TRI file(s) - both PIRT (BodySlide) and TriHead (Bethesda) formats + *Chargen.tri
        Dim tri As TriFile = Nothing
        Dim triHead As TriHeadFile = Nothing
        LoadTriForShape(shape, tri, triHead)

        ' Vertex count sanity check: TriHead deltas are 1:1 with NIF vertex indices.
        ' If counts don't match, the NIF was replaced (e.g. HiPoly Faces) and applying
        ' vanilla morph indices to a different mesh corrupts geometry. Skip in that case.
        Dim shapeVertCount = geom.NifLocalVertices.Length
        If triHead IsNot Nothing AndAlso shapeVertCount > 0 Then
            If CInt(triHead.NumVertices) <> shapeVertCount Then
                NpcPreviewLog.Log($"  [MORPH-MISMATCH] Shape '{shapeName}' has {shapeVertCount} verts but TriHead has {triHead.NumVertices} — skipping chargen morphs (NIF likely replaced by mod)")
                triHead = Nothing
            End If
        End If

        ' 1) Body weight morphs (MWGT) - from PIRT TRI (BodySlide/CBBE)
        If tri IsNot Nothing Then
            AddBodyWeightMorphs(tri, shapeName, plan)
            AddBodyRegionMorphs(tri, shapeName, plan)
        End If

        ' 2) Face morph presets (MSDK/MSDV) - via TriHead chargen .tri + RACE MSID→MSM0/MSM1
        If _npcData.MorphValues.Count > 0 Then
            NpcPreviewLog.Log($"  [MORPH] Shape '{shapeName}' verts={shapeVertCount}: TRI={tri IsNot Nothing} TriHead={triHead IsNot Nothing}({If(triHead IsNot Nothing, CInt(triHead.NumVertices), 0)} verts, {If(triHead IsNot Nothing, triHead.Morphs.Count, 0)} morphs) MorphValues={_npcData.MorphValues.Count}")
            If triHead IsNot Nothing Then AddFaceMorphPresetsFromTriHead(triHead, plan)
        End If

        ' Deduplicate channels: collapse multiple channels with the same morph name into one.
        ' Vanilla RACE records sometimes have several MPPI keys mapping to the same MPPM name
        ' (e.g. "DefaultFaceType0" used as the no-op preset across multiple morph groups).
        ' Without dedup, we'd apply the same morph deltas N times, amplifying them.
        ' We KEEP THE MAX absolute weight, which matches "select one preset per morph name".
        If plan.Channels.Count > 1 Then
            Dim dedupedByName As New Dictionary(Of String, MorphChannel)(StringComparer.OrdinalIgnoreCase)
            For Each ch In plan.Channels
                Dim existing As MorphChannel = Nothing
                If dedupedByName.TryGetValue(ch.Name, existing) Then
                    If Math.Abs(ch.Weight) > Math.Abs(existing.Weight) Then
                        dedupedByName(ch.Name) = ch
                    End If
                Else
                    dedupedByName(ch.Name) = ch
                End If
            Next
            If dedupedByName.Count <> plan.Channels.Count Then
                NpcPreviewLog.Log($"  [MORPH-DEDUP] '{shapeName}': {plan.Channels.Count} → {dedupedByName.Count} channels (collapsed duplicates)")
                plan.Channels.Clear()
                plan.Channels.AddRange(dedupedByName.Values)
            End If
        End If

        ' Diagnostic: log max delta magnitude per applied channel (helps spot scaling/space bugs)
        For Each ch In plan.Channels
            If ch.Deltas Is Nothing OrElse ch.Deltas.Count = 0 Then Continue For
            Dim maxMag As Single = 0
            For Each d In ch.Deltas
                Dim m = d.PosDiff.LengthSquared
                If m > maxMag Then maxMag = m
            Next
            maxMag = CSng(Math.Sqrt(maxMag))
            NpcPreviewLog.Log($"  [MORPH-CH] '{shapeName}' '{ch.Name}' w={ch.Weight:F3} verts={ch.Deltas.Count} maxDelta={maxMag:F3} effective={maxMag * Math.Abs(ch.Weight):F3}")
        Next

        ' 3) Face sculpting (FMRI/FMRS) — DISABLED: these are bone transforms
        '    (position/rotation/scale), not vertex morph weights.
        '    They should be applied via skeleton DeltaTransform, not via TRI vertex deltas.

        ' 4) Apply Facial Morph Intensity (FMIN) as global multiplier
        Dim fmin = _npcData.FacialMorphIntensity
        If Math.Abs(fmin - 1.0F) > 0.001F AndAlso plan.Channels.Count > 0 Then
            For Each ch In plan.Channels
                ch.Weight *= fmin
            Next
            NpcPreviewLog.Log($"  [MORPH] Applied FMIN={fmin:F3} to {plan.Channels.Count} channels")
        End If

        Return plan
    End Function

    ''' <summary>Add body weight morphs from MWGT data.</summary>
    Private Sub AddBodyWeightMorphs(tri As TriFile, shapeName As String, plan As MorphPlan)
        Dim weights = {_npcData.WeightThin, _npcData.WeightMuscular, _npcData.WeightFat}

        For i = 0 To Math.Min(BodyWeightMorphNames.Length, weights.Length) - 1
            If Math.Abs(weights(i)) < 0.001F Then Continue For

            Dim morphEntry = tri.GetMorph(shapeName, BodyWeightMorphNames(i))
            If morphEntry Is Nothing OrElse morphEntry.Offsets.Count = 0 Then Continue For

            Dim deltas = ConvertTriOffsetsToMorphData(morphEntry)
            If deltas.Count > 0 Then
                plan.Channels.Add(New MorphChannel(BodyWeightMorphNames(i), weights(i), deltas))
            End If
        Next
    End Sub

    ''' <summary>Add body morph region values from MRSV data.</summary>
    Private Sub AddBodyRegionMorphs(tri As TriFile, shapeName As String, plan As MorphPlan)
        If _npcData.BodyMorphRegionValues.Count = 0 Then Return

        For i = 0 To Math.Min(BodyRegionMorphNames.Length, _npcData.BodyMorphRegionValues.Count) - 1
            Dim weight = _npcData.BodyMorphRegionValues(i)
            If Math.Abs(weight) < 0.001F Then Continue For

            Dim morphEntry = tri.GetMorph(shapeName, BodyRegionMorphNames(i))
            If morphEntry Is Nothing OrElse morphEntry.Offsets.Count = 0 Then Continue For

            Dim deltas = ConvertTriOffsetsToMorphData(morphEntry)
            If deltas.Count > 0 Then
                plan.Channels.Add(New MorphChannel(BodyRegionMorphNames(i), weight, deltas))
            End If
        Next
    End Sub

    ''' <summary>
    ''' Add face morph presets from MSDK/MSDV using TriHead (Bethesda format) + RACE Morph Values.
    ''' RACE defines MSID -> MSM0 (min morph name) / MSM1 (max morph name).
    ''' NPC has MSDK key (= MSID) -> weight. If weight > 0, use MSM1; if weight &lt; 0, use MSM0 with abs(weight).
    ''' </summary>
    Private Sub AddFaceMorphPresetsFromTriHead(triHead As TriHeadFile, plan As MorphPlan)
        Dim shapeName = If(triHead.Morphs.Count > 0, "head", "?")

        ' 1) Morph Values (MSID → MSM0/MSM1 slider morphs)
        If _morphValueDefs IsNot Nothing Then
            For Each mvDef In _morphValueDefs
                Dim weight As Single = 0
                If Not _npcData.MorphValues.TryGetValue(mvDef.Index, weight) Then Continue For
                If Math.Abs(weight) < 0.001F Then Continue For

                Dim morphName = If(weight >= 0, mvDef.MaxName, mvDef.MinName)
                Dim morphWeight = Math.Abs(weight)
                If String.IsNullOrEmpty(morphName) Then Continue For

                Dim triMorph = triHead.GetMorph(morphName)
                If triMorph Is Nothing OrElse triMorph.Vertices Is Nothing OrElse triMorph.Vertices.Length = 0 Then
                    NpcPreviewLog.Log($"  [MORPH-SLIDER] '{morphName}' w={morphWeight:F3} → NOT FOUND in TRI")
                    Continue For
                End If

                Dim deltas = ConvertTriHeadMorphToMorphData(triMorph)
                If deltas.Count > 0 Then
                    plan.Channels.Add(New MorphChannel(morphName, morphWeight, deltas))
                    NpcPreviewLog.Log($"  [MORPH-SLIDER] '{morphName}' w={morphWeight:F3} → {deltas.Count} verts")
                End If
            Next
        End If

        ' 2) Morph Group Presets (MPPI → MPPM morph name)
        If _morphPresetDefs IsNot Nothing Then
            For Each mpDef In _morphPresetDefs
                Dim weight As Single = 0
                If Not _npcData.MorphValues.TryGetValue(mpDef.Index, weight) Then Continue For
                If Math.Abs(weight) < 0.001F Then Continue For

                Dim morphName = mpDef.MorphName
                If String.IsNullOrEmpty(morphName) Then Continue For

                Dim triMorph = triHead.GetMorph(morphName)
                If triMorph Is Nothing OrElse triMorph.Vertices Is Nothing OrElse triMorph.Vertices.Length = 0 Then
                    NpcPreviewLog.Log($"  [MORPH-PRESET] '{morphName}' w={weight:F3} → NOT FOUND in TRI")
                    Continue For
                End If

                Dim deltas = ConvertTriHeadMorphToMorphData(triMorph)
                If deltas.Count > 0 Then
                    plan.Channels.Add(New MorphChannel(morphName, weight, deltas))
                    NpcPreviewLog.Log($"  [MORPH-PRESET] '{morphName}' w={weight:F3} → {deltas.Count} verts")
                End If
            Next
        End If

        NpcPreviewLog.Log($"  [MORPH-PLAN] {plan.Channels.Count} total channels for shape")
    End Sub

    ''' <summary>Convert PIRT TRI morph offsets (sparse) to MorphData list.</summary>
    Private Shared Function ConvertTriOffsetsToMorphData(entry As TriMorphEntry) As List(Of MorphData)
        Dim result As New List(Of MorphData)(entry.Offsets.Count)
        For Each kvp In entry.Offsets
            result.Add(New MorphData With {
                .index = kvp.Key,
                .PosDiff = kvp.Value
            })
        Next
        Return result
    End Function

    ''' <summary>Convert TriHead morph vertices (dense, all vertices) to MorphData list (only non-zero).</summary>
    Private Shared Function ConvertTriHeadMorphToMorphData(morph As TriHeadMorph) As List(Of MorphData)
        Dim result As New List(Of MorphData)
        If morph.Vertices Is Nothing Then Return result
        For i = 0 To morph.Vertices.Length - 1
            Dim v = morph.Vertices(i)
            If v.X * v.X + v.Y * v.Y + v.Z * v.Z > 0.000001F Then
                result.Add(New MorphData With {
                    .index = CUInt(i),
                    .PosDiff = v
                })
            End If
        Next
        Return result
    End Function

    ''' <summary>
    ''' Get the TRI file associated with a shape's NIF.
    ''' Looks for BODYTRI extra data in the NIF (standard FO4 mechanism).
    ''' Also tries the mesh dict key path with .tri extension as fallback.
    ''' </summary>
    ''' <summary>
    ''' Load TRI data for a shape.
    ''' Resolves up to TWO TRI files per shape:
    '''   - Race/Expression TRI (animation morphs OR sparse PIRT body morphs)
    '''   - Chargen Morph TRI (sculpting morphs — LipFeature*, NoseFeature*, etc.)
    ''' Both are merged into a single TriHead so the morph application loop sees all morphs.
    '''
    ''' Path resolution priority for each of the two paths:
    '''   1. Explicit HDPT NAM0/NAM1 entry (NAM0=0 Race Morph, NAM0=2 Chargen Morph)
    '''   2. BODYTRI NiStringExtraData (BodySlide/CBBE — only fills the race/expression slot)
    '''   3. Mesh-name convention (vanilla Bethesda):
    '''         &lt;mesh&gt;.tri          → race/expression
    '''         &lt;mesh&gt;chargen.tri  → chargen sculpting
    ''' Vanilla HDPT records only declare NAM0/NAM1 for ~half the head parts (188/396);
    ''' the rest rely on the convention. The mouth is one such case.
    ''' </summary>
    Private Sub LoadTriForShape(shape As IRenderableShape, ByRef tri As TriFile, ByRef triHead As TriHeadFile)
        tri = Nothing
        triHead = Nothing

        ' Step 1: pull explicit paths from HDPT (may be empty)
        Dim raceMorphPath As String = Nothing
        Dim chargenPath As String = Nothing
        If _shapeRaceMorphTriPaths IsNot Nothing Then _shapeRaceMorphTriPaths.TryGetValue(shape, raceMorphPath)
        If _shapeChargenTriPaths IsNot Nothing Then _shapeChargenTriPaths.TryGetValue(shape, chargenPath)

        ' Step 2: fall back to BODYTRI extra data for the race/expression slot if HDPT didn't provide it
        If String.IsNullOrEmpty(raceMorphPath) Then
            Dim bodyTriPath = GetBodyTriPath(shape)
            If bodyTriPath <> "" Then raceMorphPath = bodyTriPath
        End If

        ' Step 3: fall back to mesh-name convention for any slot still empty
        Dim meshKey As String = Nothing
        If _meshDictKeys IsNot Nothing Then _meshDictKeys.TryGetValue(shape, meshKey)
        If Not String.IsNullOrEmpty(meshKey) Then
            If String.IsNullOrEmpty(raceMorphPath) Then
                raceMorphPath = Path.ChangeExtension(meshKey, ".tri")
            End If
            If String.IsNullOrEmpty(chargenPath) Then
                chargenPath = Path.ChangeExtension(meshKey, Nothing) & "chargen.tri"
            End If
        End If

        ' Load race/expression TRI: try PIRT first (BodySlide format), then TriHead (Bethesda format)
        If Not String.IsNullOrEmpty(raceMorphPath) Then
            Dim normRace = NormalizeDictPath(raceMorphPath)
            tri = TryLoadPirt(normRace)
            If tri Is Nothing Then
                triHead = TryLoadTriHead(normRace)
                If triHead IsNot Nothing Then
                    Dim regBase = triHead.Morphs.Where(Function(m) Not m.IsModMorph).Select(Function(m) m.Name).ToList()
                    Dim modBase = triHead.Morphs.Where(Function(m) m.IsModMorph).Select(Function(m) m.Name).ToList()
                    NpcPreviewLog.Log($"[TRI] Base TRI '{normRace}': {regBase.Count} regular + {modBase.Count} mod-morphs")
                    If regBase.Count > 0 Then NpcPreviewLog.Log($"[TRI]   regular: {String.Join(", ", regBase)}")
                    If modBase.Count > 0 Then NpcPreviewLog.Log($"[TRI]   mod: {String.Join(", ", modBase)}")
                End If
            End If
        End If

        ' Load chargen TRI (always TriHead format) and merge into triHead
        If Not String.IsNullOrEmpty(chargenPath) Then
            Dim normChargen = NormalizeDictPath(chargenPath)
            Dim chargenHead = TryLoadTriHead(normChargen)
            If chargenHead IsNot Nothing Then
                Dim regularMorphs = chargenHead.Morphs.Where(Function(m) Not m.IsModMorph).Select(Function(m) m.Name).ToList()
                Dim modMorphs = chargenHead.Morphs.Where(Function(m) m.IsModMorph).Select(Function(m) m.Name).ToList()
                NpcPreviewLog.Log($"[TRI] Chargen TRI '{normChargen}': {regularMorphs.Count} regular + {modMorphs.Count} mod-morphs")
                If regularMorphs.Count > 0 Then NpcPreviewLog.Log($"[TRI]   regular: {String.Join(", ", regularMorphs)}")
                If modMorphs.Count > 0 Then NpcPreviewLog.Log($"[TRI]   mod: {String.Join(", ", modMorphs)}")
                If triHead Is Nothing Then
                    triHead = chargenHead
                Else
                    For Each morph In chargenHead.Morphs
                        If triHead.GetMorph(morph.Name) Is Nothing Then
                            triHead.Morphs.Add(morph)
                        End If
                    Next
                End If
            End If
        End If
    End Sub

    Private Function TryLoadPirt(normalizedPath As String) As TriFile
        SyncLock _triCache
            Dim cached As TriFile = Nothing
            If _triCache.TryGetValue(normalizedPath, cached) Then Return cached
        End SyncLock

        Dim bytes = TryGetFileBytes(normalizedPath)
        If bytes Is Nothing Then Return Nothing

        Try
            Dim pirt = TriFileParser.ParseTriFromBytes(bytes)
            SyncLock _triCache
                _triCache(normalizedPath) = pirt
            End SyncLock
            NpcPreviewLog.Log($"[TRI] PIRT loaded: '{normalizedPath}' ({pirt.ShapeMorphs.Count} shapes)")
            Return pirt
        Catch
            Return Nothing
        End Try
    End Function

    Private Function TryLoadTriHead(normalizedPath As String) As TriHeadFile
        SyncLock _triHeadCache
            Dim cached As TriHeadFile = Nothing
            If _triHeadCache.TryGetValue(normalizedPath, cached) Then Return cached
        End SyncLock

        SyncLock _triLoadAttempted
            If _triLoadAttempted.Contains(normalizedPath) Then Return Nothing
            _triLoadAttempted.Add(normalizedPath)
        End SyncLock

        Dim bytes = TryGetFileBytes(normalizedPath)
        If bytes Is Nothing Then Return Nothing

        Try
            Dim head = TriHeadParser.ParseTriHeadFromBytes(bytes)
            If head IsNot Nothing Then
                SyncLock _triHeadCache
                    _triHeadCache(normalizedPath) = head
                End SyncLock
                NpcPreviewLog.Log($"[TRI] TriHead loaded: '{normalizedPath}' ({head.Morphs.Count} morphs, {head.NumVertices} verts)")
            End If
            Return head
        Catch ex As Exception
            NpcPreviewLog.Log($"[TRI] Failed: '{normalizedPath}': {ex.Message}")
            Return Nothing
        End Try
    End Function

    Private Shared Function TryGetFileBytes(normalizedPath As String) As Byte()
        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(normalizedPath, loc) Then Return Nothing
        Dim bytes = loc.GetBytes()
        If bytes Is Nothing OrElse bytes.Length < 8 Then Return Nothing
        Return bytes
    End Function

    ''' <summary>Extract BODYTRI extra data path from a NIF shape or its root.</summary>
    Private Shared Function GetBodyTriPath(shape As IRenderableShape) As String
        If shape.NifContent Is Nothing OrElse shape.NifContent.Blocks Is Nothing Then Return ""

        ' Check shape-level extra data
        If shape.NifShape IsNot Nothing AndAlso shape.NifShape.ExtraDataList IsNot Nothing Then
            For Each edRef In shape.NifShape.ExtraDataList.References
                If edRef.Index < 0 OrElse edRef.Index >= shape.NifContent.Blocks.Count Then Continue For
                Dim ed = TryCast(shape.NifContent.Blocks(edRef.Index), NiflySharp.Blocks.NiStringExtraData)
                If ed IsNot Nothing AndAlso ed.Name IsNot Nothing AndAlso ed.Name.String = "BODYTRI" Then
                    Return If(ed.StringData?.String, "")
                End If
            Next
        End If

        ' Check root node extra data
        Dim rootNode = shape.NifContent.Blocks.OfType(Of NiflySharp.Blocks.NiNode)().FirstOrDefault()
        If rootNode IsNot Nothing AndAlso rootNode.ExtraDataList IsNot Nothing Then
            For Each edRef In rootNode.ExtraDataList.References
                If edRef.Index < 0 OrElse edRef.Index >= shape.NifContent.Blocks.Count Then Continue For
                Dim ed = TryCast(shape.NifContent.Blocks(edRef.Index), NiflySharp.Blocks.NiStringExtraData)
                If ed IsNot Nothing AndAlso ed.Name IsNot Nothing AndAlso ed.Name.String = "BODYTRI" Then
                    Return If(ed.StringData?.String, "")
                End If
            Next
        End If

        Return ""
    End Function

    ''' <summary>Normalize a file path for FilesDictionary lookup.</summary>
    Private Shared Function NormalizeDictPath(path As String) As String
        If String.IsNullOrWhiteSpace(path) Then Return ""
        Dim normalized = path.Replace("/", "\").Trim().ToLowerInvariant()
        If Not normalized.StartsWith("meshes\") Then
            normalized = "meshes\" & normalized
        End If
        Return normalized
    End Function

End Class
