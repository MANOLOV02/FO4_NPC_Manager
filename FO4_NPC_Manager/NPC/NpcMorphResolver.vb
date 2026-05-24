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
    ' Per-process (Shared) TRI caches: a given chargen/race .tri is parsed at most once for the
    ' lifetime of the process and shared across every NpcMorphResolver instance — the resolver is
    ' rebuilt on each render/toggle (MainForm.BuildCompositeMorphResolver), so a per-instance cache
    ' re-parsed the FRTRI003 TriHead every frame. Mirrors the existing Shared path-keyed
    ' FilesDictionary caches (BodySlideTriResolver._pirtCache, MainForm._facialBoneRegionsCache):
    ' FilesDictionary content is treated as process-stable, so no per-render invalidation.
    Private Shared ReadOnly _triCache As New Dictionary(Of String, TriFile)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly _triHeadCache As New Dictionary(Of String, TriHeadFile)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly _triLoadAttempted As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ' MRSV — Body Morph Region Values. Per TES5Edit/Core/wbDefinitionsFO4.pas:10793-10799,
    ' the subrecord is a fixed struct of 5 floats with these region labels in this order.
    ' Same layout in FO76 (wbDefinitionsFO76.pas:13350) and SF1 (wbDefinitionsSF1.pas:13376).
    Public Shared ReadOnly BodyRegionLabels As String() = {
        "Head",
        "Upper Torso",
        "Arms",
        "Lower Torso",
        "Legs"
    }

    ''' <summary>Create a face morph resolver for an NPC. Applies MSDK/MSDV (sliders+presets)
    ''' against the chargen FRTRI003 TriHead. MWGT/MRSV are NOT applied here — they go through
    ''' MainForm.BuildBodyWeightPose (bone-scale layers).</summary>
    ''' <param name="npcData">NPC morph data (face morph values, FMIN, etc.)</param>
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

        ' TRI-BASE-ALIGN-PROBE 2026-04-19: compare TRI.BaseVertices[i] vs NIF.NifLocalVertices[i]
        ' over the overlapping range [0, min(TRI, NIF)). Runs ALWAYS (match or mismatch) so we have
        ' female-matching reference (expected max diff ≈ 0 over full range) vs male-mismatched
        ' (diff ≈ 0 over first 1690 if extras are appended at end, or nonzero if interleaved).
        If triHead IsNot Nothing AndAlso shapeVertCount > 0 AndAlso triHead.BaseVertices IsNot Nothing AndAlso triHead.BaseVertices.Length > 0 Then
            Dim overlap = Math.Min(triHead.BaseVertices.Length, shapeVertCount)
            Dim maxMag As Double = 0
            Dim maxIdx As Integer = -1
            Dim nearZero As Integer = 0
            For i = 0 To overlap - 1
                Dim t = triHead.BaseVertices(i)
                Dim n = geom.NifLocalVertices(i)
                Dim dx = CDbl(t.X) - n.X
                Dim dy = CDbl(t.Y) - n.Y
                Dim dz = CDbl(t.Z) - n.Z
                Dim mag = Math.Sqrt(dx * dx + dy * dy + dz * dz)
                If mag < 0.0001 Then nearZero += 1
                If mag > maxMag Then
                    maxMag = mag
                    maxIdx = i
                End If
            Next
            Dim pct = (100.0 * nearZero) / overlap
            Dim kind = If(CInt(triHead.NumVertices) = shapeVertCount, "MATCH", "MISMATCH")
            For i = 0 To Math.Min(4, overlap - 1)
                Dim t = triHead.BaseVertices(i)
                Dim n = geom.NifLocalVertices(i)
                Dim dx = CDbl(t.X) - n.X
                Dim dy = CDbl(t.Y) - n.Y
                Dim dz = CDbl(t.Z) - n.Z
                Dim mag = Math.Sqrt(dx * dx + dy * dy + dz * dz)
                Dim iLocal = i
            Next
            If maxIdx >= 0 AndAlso maxMag > 0.0001 Then
                Dim t = triHead.BaseVertices(maxIdx)
                Dim n = geom.NifLocalVertices(maxIdx)
            End If
            If shapeVertCount > overlap Then
                For i = overlap To Math.Min(shapeVertCount - 1, overlap + 9)
                    Dim n = geom.NifLocalVertices(i)
                    Dim iLocal = i
                Next
            End If
        End If

        ' Vertex count sanity check. Apply semantics: final[i] = NIF.rest[i] + Σ morph.delta[i]
        ' (verified MorphEngine.vb:86 starts from NifLocalVertices and line 137 adds deltas by index).
        ' MorphEngine also has a safety guard (line 134: If i >= 0 AndAlso i < count) that drops
        ' out-of-range indices. Three cases:
        '   A) TriHead.NumVertices == NIF verts: exact match, apply all deltas.
        '   B) TriHead.NumVertices <  NIF verts: NIF has appended extras (vanilla male _faceBones
        '      = 1696 verts with TRI chargen = 1690; extras are inner-mouth/jaw rigging geometry
        '      with no morph target in any vanilla TRI). Apply the TRI's first N deltas to
        '      indices [0, TRI.NumVertices); extras [TRI.NumVertices, NIF) stay at NIF rest.
        '      Empirically confirmed via TRI-BASE-ALIGN-PROBE 2026-04-19 that female (count MATCH)
        '      has maxDiff 0.72 between TRI.BaseVertices and NIF rest at some indices yet morphs
        '      at noise floor — proves by-index alignment is the runtime truth, not by-position.
        '   C) TriHead.NumVertices >  NIF verts: NIF was DOWNSIZED (mod replaced with fewer verts).
        '      Log warning; MorphEngine's bounds check will drop indices ≥ NIF count. Some morph
        '      deltas are lost but nothing corrupts.
        If triHead IsNot Nothing AndAlso shapeVertCount > 0 Then
            If CInt(triHead.NumVertices) = shapeVertCount Then
                ' Case A — nothing to log
            ElseIf CInt(triHead.NumVertices) < shapeVertCount Then
            Else
            End If
        End If

        ' MWGT (NPC.MWGT Thin/Muscular/Fat) and MRSV (Body Morph Region Values) are NOT applied
        ' here. They travel through the bone-scale pose pipeline in MainForm.BuildBodyWeightPose:
        '   - MWGT  → Layer 1 (RACE.BSMS WeightScale, per-bone interpolation Thin·t + Musc·m + Fat·f).
        '   - MRSV  → Layer 3 (RACE.BSMS RangeModifier Min/Max Y,Z), via ResolveMrsvRegion mapping
        '             a bone to one of 5 regions (Head/UpperTorso/Arms/LowerTorso/Legs).
        ' xEdit defs: wbDefinitionsFO4.pas:10793 (MRSV struct), wbDefinitionsFO4.pas:5929 (BSMS
        ' RangeModifier), parser at RecordParsers.vb (RACE.BoneData) reads BSMS WeightScale 9 floats.
        '
        ' Previously AddBodyWeightMorphs ran here in parallel, looking up "WeightThin/Muscular/Fat"
        ' morphs in the body's PIRT .tri (BodySlide/CBBE convention). That caused DOUBLE application
        ' of MWGT whenever the user's body mod shipped those morphs — once via bones (always on with
        ' weightLayersEnabled), once via vertex morph. Removed 2026-05-02 to keep MWGT consistent
        ' with the canonical engine path (bone scaling).

        ' 2) Face morph presets (MSDK/MSDV) - via TriHead chargen .tri + RACE MSID→MSM0/MSM1
        If _npcData.MorphValues.Count > 0 Then
            ' Raw dump of parsed MSDK→MSDV pairs for cross-reference with xEdit.
            Dim dumpIdx As Integer = 0
            For Each kvp In _npcData.MorphValues
                dumpIdx += 1
            Next
            If triHead IsNot Nothing Then AddFaceMorphPresetsFromTriHead(triHead, plan)
        End If

        ' Channel dedup-by-name with SUMMED weights now lives inside
        ' BuildFaceMorphPlanFromTriHead (called via AddFaceMorphPresetsFromTriHead above).
        ' Empirical rationale (Alijo + Cait, 2026-04-18 against CK FaceGen): vanilla RACE has
        ' multiple MPPI keys pointing to the same morph name (e.g. "DefaultFaceType0" across
        ' Nose+Cheek+Neck+Mouth groups); CK applies the SUM, not max-abs.

        ' Diagnostic: log max delta magnitude per applied channel (helps spot scaling/space bugs)
        For Each ch In plan.Channels
            If ch.Deltas Is Nothing OrElse ch.Deltas.Count = 0 Then Continue For
            Dim maxMag As Single = 0
            For Each d In ch.Deltas
                Dim m = d.PosDiff.LengthSquared
                If m > maxMag Then maxMag = m
            Next
            maxMag = CSng(Math.Sqrt(maxMag))
        Next

        ' 3) Face sculpting (FMRI/FMRS) — DISABLED: these are bone transforms
        '    (position/rotation/scale), not vertex morph weights.
        '    They should be applied via skeleton DeltaTransform, not via TRI vertex deltas.

        ' 4) FMIN (FacialMorphIntensity) is NOT applied to vertex morphs.
        ' Empirical validation 2026-04-19 fixture 0x0015E922 (FMIN=2):
        '   Harness obs direction is bake − our (NOT our − bake; see harness dx=vRaw-ourWorld).
        '   V-only verts: obs ≈ −our_delta_Σ → bake_delta = our + obs ≈ 0. Bake has ZERO vertex
        '   morph at these neck/collarbone-edge verts of the head mesh.
        '   When we DO apply FMIN×weight: our_delta doubles → obs doubles → V-only RMS 0.012 → 0.026,
        '   V+FMRS max 0.08 → 1.03. Empirically worse.
        ' The residual 0.012 at V-only is NOT FMIN: it's our resolver applying morph deltas at
        ' edge verts that CK's bake doesn't have. Separate bug, out of scope for FMIN semantics.
        ' No-op for the scaling; log FMIN for visibility.

        Return plan
    End Function

    ' AddBodyWeightMorphs and AddBodyRegionMorphs removed 2026-05-02. MWGT and MRSV both
    ' travel through MainForm.BuildBodyWeightPose (bone-scale layers), not via PIRT vertex
    ' morphs. Keeping a parallel .tri-based path here caused double application whenever
    ' the user's installed body mod (CBBE/FG/etc) defined "WeightThin/Muscular/Fat" or
    ' "MorphRegion<i>" morphs.

    ''' <summary>
    ''' Build the face MorphPlan for an NPC against a chargen TriHead, applying:
    '''   1) MSID slider morphs (RACE.MorphValues): MSDK/MSDV weight ≥0 picks MSM1/MaxName,
    '''      &lt;0 picks MSM0/MinName with abs(weight).
    '''   2) MPPI preset morphs (RACE.MorphPresets gendered): direct mapping to MPPM morph name.
    '''   3) Channel dedup-by-name with SUMMED weights — same data the runtime resolver applies
    '''      (vanilla RACE has multiple MPPI keys pointing to the same morph name, e.g.
    '''      "DefaultFaceType0" across Nose+Cheek+Neck+Mouth groups; CK applies the SUM).
    '''
    ''' Public Shared so offline bakes (FaceGenBuilder) can build the same plan the runtime
    ''' uses without spinning up an IMorphResolver / SkinnedGeometry. The instance method
    ''' <see cref="ResolveMorphPlan"/> delegates here for the runtime path so the two never
    ''' drift.
    ''' </summary>
    ''' <param name="npcData">NPC face morph data (MorphValues dict, FMIN, etc.).</param>
    ''' <param name="morphValueDefs">RACE.MorphValues (MSID → MSM0/MSM1).</param>
    ''' <param name="morphPresetDefs">RACE.MorphPresets gendered (MPPI → MPPM).</param>
    ''' <param name="triHead">Chargen FRTRI003 file already parsed for the target shape.</param>
    ''' <param name="logShapeName">Optional shape name for log lines; empty disables logging.</param>
    Public Shared Function BuildFaceMorphPlanFromTriHead(npcData As NPC_Data,
                                                        morphValueDefs As List(Of RACE_MorphValueDef),
                                                        morphPresetDefs As List(Of RACE_MorphPresetDef),
                                                        triHead As TriHeadFile,
                                                        Optional logShapeName As String = "") As MorphPlan
        Dim plan As New MorphPlan()
        If npcData Is Nothing OrElse triHead Is Nothing Then Return plan
        If npcData.MorphValues Is Nothing OrElse npcData.MorphValues.Count = 0 Then Return plan

        ' 1) Morph Values (MSID → MSM0/MSM1 slider morphs)
        If morphValueDefs IsNot Nothing Then
            For Each mvDef In morphValueDefs
                Dim weight As Single = 0
                If Not npcData.MorphValues.TryGetValue(mvDef.Index, weight) Then Continue For
                If Math.Abs(weight) < 0.001F Then Continue For

                Dim usedMax As Boolean = (weight >= 0)
                Dim morphName = If(usedMax, mvDef.MaxName, mvDef.MinName)
                Dim nameSrc As String = If(usedMax, "MSM1/MaxName", "MSM0/MinName")
                Dim morphWeight = Math.Abs(weight)
                If String.IsNullOrEmpty(morphName) Then Continue For

                Dim triMorph = triHead.GetMorph(morphName)
                If triMorph Is Nothing OrElse triMorph.Vertices Is Nothing OrElse triMorph.Vertices.Length = 0 Then
                    Continue For
                End If

                Dim deltas = ConvertTriHeadMorphToMorphData(triMorph)
                If deltas.Count > 0 Then
                    plan.Channels.Add(New MorphChannel(morphName, morphWeight, deltas))
                    If logShapeName <> "" Then
                        Dim maxDeltaStr = DescribeMaxSignedDelta(triMorph, triHead)
                    End If
                End If
            Next
        End If

        ' 2) Morph Group Presets (MPPI → MPPM morph name)
        If morphPresetDefs IsNot Nothing Then
            For Each mpDef In morphPresetDefs
                Dim weight As Single = 0
                If Not npcData.MorphValues.TryGetValue(mpDef.Index, weight) Then Continue For
                If Math.Abs(weight) < 0.001F Then Continue For

                Dim morphName = mpDef.MorphName
                If String.IsNullOrEmpty(morphName) Then Continue For

                Dim triMorph = triHead.GetMorph(morphName)
                If triMorph Is Nothing OrElse triMorph.Vertices Is Nothing OrElse triMorph.Vertices.Length = 0 Then
                    Continue For
                End If

                Dim deltas = ConvertTriHeadMorphToMorphData(triMorph)
                If deltas.Count > 0 Then
                    plan.Channels.Add(New MorphChannel(morphName, weight, deltas))
                    If logShapeName <> "" Then
                        Dim topDeltas = DescribeTopSignedDeltas(triMorph, triHead, 5)
                    End If
                End If
            Next
        End If

        ' 3) Dedup channels by name SUMMING their weights — vanilla RACE uses several MPPI
        ' keys pointing to the same morph name; CK applies the sum. Empirically validated
        ' against CK FaceGen bake (Alijo + Cait 2026-04-18).
        If plan.Channels.Count > 1 Then
            Dim summedByName As New Dictionary(Of String, MorphChannel)(StringComparer.OrdinalIgnoreCase)
            For Each ch In plan.Channels
                Dim existing As MorphChannel = Nothing
                If summedByName.TryGetValue(ch.Name, existing) Then
                    existing.Weight += ch.Weight
                Else
                    summedByName(ch.Name) = ch
                End If
            Next
            If summedByName.Count <> plan.Channels.Count Then
                plan.Channels.Clear()
                plan.Channels.AddRange(summedByName.Values)
            End If
        End If

        Return plan
    End Function

    ''' <summary>
    ''' Add face morph presets from MSDK/MSDV using TriHead (Bethesda format) + RACE Morph Values.
    ''' Instance-side wrapper: delegates to <see cref="BuildFaceMorphPlanFromTriHead"/> using this
    ''' resolver's stored npcData / RACE defs. Kept for readability of ResolveMorphPlan.
    ''' </summary>
    Private Sub AddFaceMorphPresetsFromTriHead(triHead As TriHeadFile, plan As MorphPlan)
        Dim built = BuildFaceMorphPlanFromTriHead(_npcData, _morphValueDefs, _morphPresetDefs, triHead, logShapeName:="head")
        plan.Channels.AddRange(built.Channels)
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

    ''' <summary>Return a string listing the top-N non-zero deltas of a TriHead morph (sorted
    ''' by magnitude descending), each with its base position and signed delta. Useful to see
    ''' the spatial distribution of a morph: does it push a single feature or sweep the whole
    ''' face? And in which direction.</summary>
    Private Shared Function DescribeTopSignedDeltas(morph As TriHeadMorph, triHead As TriHeadFile, topN As Integer) As String
        If morph Is Nothing OrElse morph.Vertices Is Nothing OrElse morph.Vertices.Length = 0 Then Return "(none)"
        Dim entries As New List(Of (Idx As Integer, V As Vector3, MagSq As Single))
        For i = 0 To morph.Vertices.Length - 1
            Dim v = morph.Vertices(i)
            Dim m = v.X * v.X + v.Y * v.Y + v.Z * v.Z
            If m > 0.000001F Then entries.Add((i, v, m))
        Next
        If entries.Count = 0 Then Return "(none)"
        entries.Sort(Function(a, b) b.MagSq.CompareTo(a.MagSq))
        Dim count = Math.Min(topN, entries.Count)
        Dim sb As New System.Text.StringBuilder()
        For k = 0 To count - 1
            Dim e = entries(k)
            Dim baseStr As String = "base=?"
            If triHead IsNot Nothing AndAlso triHead.BaseVertices IsNot Nothing _
               AndAlso e.Idx < triHead.BaseVertices.Length Then
                Dim b = triHead.BaseVertices(e.Idx)
                baseStr = $"base=({b.X:F2},{b.Y:F2},{b.Z:F2})"
            End If
            If k > 0 Then sb.Append(" | ")
            sb.Append($"idx={e.Idx} {baseStr} delta=({e.V.X:+0.000;-0.000;0.000},{e.V.Y:+0.000;-0.000;0.000},{e.V.Z:+0.000;-0.000;0.000}) mag={Math.Sqrt(e.MagSq):F3}")
        Next
        Return sb.ToString()
    End Function

    ''' <summary>Single-vertex wrapper for backward compatibility with the SLIDER log (shows only the peak).</summary>
    Private Shared Function DescribeMaxSignedDelta(morph As TriHeadMorph, triHead As TriHeadFile) As String
        Return DescribeTopSignedDeltas(morph, triHead, 1)
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
            Dim bodyTriPath = MeshPathHelpers.ReadBodyTriPath(shape, includeShapeLevel:=True)
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
            Dim normRace = MeshPathHelpers.NormalizeMeshKey(raceMorphPath)
            tri = TryLoadPirt(normRace)
            If tri Is Nothing Then
                triHead = TryLoadTriHead(normRace)
            End If
        End If

        ' Load chargen TRI (always TriHead format) and merge into triHead
        If Not String.IsNullOrEmpty(chargenPath) Then
            Dim normChargen = MeshPathHelpers.NormalizeMeshKey(chargenPath)
            Dim chargenHead = TryLoadTriHead(normChargen)
            If chargenHead IsNot Nothing Then
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
        If bytes Is Nothing Then
            Return Nothing
        End If

        Try
            Dim pirt = TriFileParser.ParseTriFromBytes(bytes)
            SyncLock _triCache
                _triCache(normalizedPath) = pirt
            End SyncLock
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
        If bytes Is Nothing Then
            Return Nothing
        End If

        Try
            Dim head = TriHeadParser.ParseTriHeadFromBytes(bytes)
            If head IsNot Nothing Then
                SyncLock _triHeadCache
                    _triHeadCache(normalizedPath) = head
                End SyncLock
            End If
            Return head
        Catch ex As Exception
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

End Class
