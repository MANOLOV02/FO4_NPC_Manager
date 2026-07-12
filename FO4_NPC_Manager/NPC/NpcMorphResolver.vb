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
    ''' <summary>SSE only: the NPC's RACE EditorID (e.g. "ImperialRace"). Names the race-morph in the merged
    ''' head TriHead (FemaleHeadRaces.tri) — the racial base face applied before NAM9/NAMA. Empty = skip.</summary>
    Private ReadOnly _raceEditorId As String
    Private ReadOnly _meshDictKeys As Dictionary(Of IRenderableShape, String)
    Private ReadOnly _shapeChargenTriPaths As Dictionary(Of IRenderableShape, String)
    Private ReadOnly _shapeRaceMorphTriPaths As Dictionary(Of IRenderableShape, String)
    Private ReadOnly _shapeMeshMorphTriPaths As Dictionary(Of IRenderableShape, String)  ' HDPT NAM0=1 (SkinnyMorph source)
    Private ReadOnly _raceKeywordEditorIds As List(Of String)   ' race KWDA EditorIDs → "<kw>Morph"@1.0 (race-agnostic)
    Private ReadOnly _morphValueDefs As List(Of RACE_MorphValueDef)      ' MSID -> MSM0/MSM1 from RACE
    Private ReadOnly _morphPresetDefs As List(Of RACE_MorphPresetDef)   ' MPPI -> MPPM from RACE Morph Groups
    ''' <summary>SSE-only gates (los canales que en Skyrim viven DENTRO del plan de cara y en FO4 son
    ''' pipelines aparte): sculpt per-vértice de RaceMenu = checkbox "Sculpt", y el SkinnyMorph de la
    ''' cabeza/pelo = checkbox "Body weight". El bake los deja en True (default del ctor) — es un toggle
    ''' de PREVIEW, no cambia lo que se hornea. Inertes en el camino FO4 (que no emite esos canales).</summary>
    Private ReadOnly _applySculpt As Boolean = True
    Private ReadOnly _applyBodyWeight As Boolean = True
    ' Per-process (Shared) TRI caches: a given chargen/race .tri is parsed at most once for the
    ' lifetime of the process and shared across every NpcMorphResolver instance — the resolver is
    ' rebuilt on each render/toggle (MainForm.BuildCompositeMorphResolver), so a per-instance cache
    ' re-parsed the FRTRI003 TriHead every frame. Mirrors the existing Shared path-keyed
    ' FilesDictionary caches (BodySlideTriResolver._pirtCache, MainForm._facialBoneRegionsCache):
    ' FilesDictionary content is treated as process-stable, so no per-render invalidation.
    ' PathLoadCache: load-once per path, failed loads remembered as Nothing, and concurrent callers for the
    ' same path wait for the in-flight load instead of short-circuiting (ResolveMorphPlan runs under
    ' Parallel.ForEach — the old attempted-HashSet was marked BEFORE the load, so a second thread could see
    ' "attempted" with nothing cached yet and return Nothing). See PathLoadCache.vb.
    Private Shared ReadOnly _triCache As New PathLoadCache(Of TriFile)()
    Private Shared ReadOnly _triHeadCache As New PathLoadCache(Of TriHeadFile)()

    ''' <summary>Drop the per-process TRI parse caches. Call on load-order change (FilesDictionary rebuilt):
    ''' a path could resolve to different bytes after a reload, so the cached parse would be stale, and the
    ''' parsed-geometry entries (potentially MBs each) are freed. Within a FIXED load order this is never
    ''' called, so a browsed .tri is parsed at most once — no re-parse churn during a session.</summary>
    Public Shared Sub ClearCaches()
        _triCache.Clear()
        _triHeadCache.Clear()
    End Sub

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
                   Optional shapeRaceMorphTriPaths As Dictionary(Of IRenderableShape, String) = Nothing,
                   Optional raceEditorId As String = "",
                   Optional shapeMeshMorphTriPaths As Dictionary(Of IRenderableShape, String) = Nothing,
                   Optional raceKeywordEditorIds As List(Of String) = Nothing,
                   Optional applySculpt As Boolean = True,
                   Optional applyBodyWeight As Boolean = True)
        _npcData = npcData
        _applySculpt = applySculpt
        _applyBodyWeight = applyBodyWeight
        _morphValueDefs = morphValueDefs
        _morphPresetDefs = morphPresetDefs
        _meshDictKeys = meshDictKeys
        _shapeChargenTriPaths = shapeChargenTriPaths
        _shapeRaceMorphTriPaths = shapeRaceMorphTriPaths
        _shapeMeshMorphTriPaths = shapeMeshMorphTriPaths
        _raceEditorId = raceEditorId
        _raceKeywordEditorIds = raceKeywordEditorIds
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

        ' Apply semantics: final[i] = NIF.rest[i] + Σ morph.delta[i] — TriHead deltas are 1:1 with NIF
        ' vertex indices (verified MorphEngine.vb:86 starts from NifLocalVertices, line 137 adds deltas
        ' by index). No resolver-side vertex-count handling is needed: MorphEngine's bounds guard
        ' (line 134: If i >= 0 AndAlso i < count) drops out-of-range indices, covering all three count
        ' cases without corruption:
        '   A) TriHead.NumVertices == NIF verts: exact match, all deltas apply.
        '   B) TriHead.NumVertices <  NIF verts: NIF has appended extras (vanilla male _faceBones
        '      = 1696 verts vs TRI chargen 1690; extras are inner-mouth/jaw rigging with no morph
        '      target). First N deltas apply to [0, TRI.NumVertices); extras stay at NIF rest.
        '      By-index alignment is the runtime truth, not by-position (confirmed 2026-04-19: female
        '      count-MATCH has maxDiff 0.72 between TRI base and NIF rest yet morphs at noise floor).
        '   C) TriHead.NumVertices >  NIF verts: NIF was DOWNSIZED; the bounds guard drops indices
        '      ≥ NIF count — some deltas lost but nothing corrupts.

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

        ' 2) Face morph presets - GAME-AWARE:
        '  • FO4  = MSDK/MSDV sliders via RACE MSID→MSM0/MSM1 + MPPI presets (RACE-defined name map).
        '  • SSE  = NAM9 (18 signed sliders) + NAMA (Nose/Eyes/Mouth type) via a FIXED engine name
        '           table (no RACE defs) — byte-verified from SkyrimSE.exe @0x1ff92a0. See
        '           project_sse_nam9_morph_map. Same chargen .tri, same TriHead.GetMorph mechanism.
        If triHead IsNot Nothing Then
            ' Single per-game builder — the SAME one the offline bake calls, so render and bake never diverge.
            ' Pass this shape's chargen tri (NAM0=2) so RaceMenu per-shape sculpt routes to it by Host.
            Dim shapeChargen As String = Nothing
            If _shapeChargenTriPaths IsNot Nothing Then _shapeChargenTriPaths.TryGetValue(shape, shapeChargen)
            plan.Channels.AddRange(BuildFaceMorphPlan(_npcData, triHead, _raceEditorId, _morphValueDefs, _morphPresetDefs, _raceKeywordEditorIds, shapeChargen,
                                                     applySculpt:=_applySculpt, applyBodyWeight:=_applyBodyWeight).Channels)
        End If

        ' Channel dedup-by-name with SUMMED weights now lives inside
        ' BuildFaceMorphPlanFromTriHead (called via AddFaceMorphPresetsFromTriHead above).
        ' Empirical rationale (Alijo + Cait, 2026-04-18 against CK FaceGen): vanilla RACE has
        ' multiple MPPI keys pointing to the same morph name (e.g. "DefaultFaceType0" across
        ' Nose+Cheek+Neck+Mouth groups); CK applies the SUM, not max-abs.

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
    ''' <summary>THE single per-GAME face morph plan builder. The live render (<see cref="ResolveMorphPlan"/>)
    ''' and the offline FaceGen bake (FaceGenBuildPipeline.ApplyChargenMorphsInPlace) BOTH call this, so there
    ''' is exactly ONE morph path per game with no divergence. Dispatches on <paramref name="npcData"/>.Game
    ''' over the SAME merged race+chargen TriHead and assembles ALL face morphs:
    '''   • FO4    → MSDK/MSDV sliders + MPPI presets (RACE defs). LooksMenu face edits are already folded into
    '''              npcData.MorphValues by NpcRecordOverlay before this runs.
    '''   • Skyrim → race base + NAM9 sliders + NAMA type presets + RaceMenu custom morphs + RaceMenu per-vertex
    '''              sculpt (all inside <see cref="BuildFaceMorphPlanFromNam9"/>; the RaceMenu data is folded
    '''              into npcData by NpcRecordOverlay before this runs).
    ''' The SSE head/hair actor-WEIGHT morph is folded into this same plan (the "SkinnyMorph" channel in
    ''' BuildFaceMorphPlanFromNam9, read from each shape's own mesh .tri at frac = 1 - NAM7/100) — one source,
    ''' render + bake, per-part and race-aware; there is no separate weight resolver or delta table.
    ''' <paramref name="applySculpt"/> / <paramref name="applyBodyWeight"/> are the SSE PREVIEW toggles for the
    ''' two channels that live inside this plan (RaceMenu sculpt / SkinnyMorph). They default True so the offline
    ''' bake — which must not depend on UI state — is unaffected.</summary>
    Public Shared Function BuildFaceMorphPlan(npcData As NPC_Data, triHead As TriHeadFile,
                                              raceEditorId As String,
                                              morphValueDefs As List(Of RACE_MorphValueDef),
                                              morphPresetDefs As List(Of RACE_MorphPresetDef),
                                              Optional raceKeywordEditorIds As List(Of String) = Nothing,
                                              Optional shapeChargenTriPath As String = "",
                                              Optional applySculpt As Boolean = True,
                                              Optional applyBodyWeight As Boolean = True) As MorphPlan
        Dim plan As New MorphPlan()
        If npcData Is Nothing OrElse triHead Is Nothing Then Return plan
        If npcData.Game = Config_App.Game_Enum.Skyrim Then
            plan.Channels.AddRange(BuildFaceMorphPlanFromNam9(npcData, triHead, raceEditorId, raceKeywordEditorIds, shapeChargenTriPath,
                                                              applySculpt, applyBodyWeight).Channels)
        ElseIf npcData.MorphValues IsNot Nothing AndAlso npcData.MorphValues.Count > 0 Then
            plan.Channels.AddRange(BuildFaceMorphPlanFromTriHead(npcData, morphValueDefs, morphPresetDefs, triHead).Channels)
        End If
        Return plan
    End Function

    ''' <summary>Merge the CHARGEN TriHead's morphs into the RACE TriHead in place — race morphs win on name
    ''' collision (race is loaded first; a chargen morph is added only when the race head lacks that name). THE
    ''' single merge used by both the render (<see cref="LoadTriForShape"/>) and the bake (FaceGenBuildPipeline)
    ''' so the head TriHead they feed <see cref="BuildFaceMorphPlan"/> is assembled identically. When
    ''' <paramref name="triHead"/> is Nothing it becomes the chargen head; when chargen is Nothing it is a no-op.</summary>
    Public Shared Sub MergeChargenIntoRaceTriHead(ByRef triHead As TriHeadFile, chargenHead As TriHeadFile)
        If chargenHead Is Nothing Then Return
        If triHead Is Nothing Then triHead = chargenHead : Return
        For Each morph In chargenHead.Morphs
            If triHead.GetMorph(morph.Name) Is Nothing Then triHead.Morphs.Add(morph)
        Next
    End Sub

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

    ''' <summary>SSE NAM9→chargen morph-name table, byte-verified from SkyrimSE.exe engine table
    ''' @RVA 0x1ff92a0 (stride 0x18, record {flag,negName,posName}; accessor 0x1403B8360). Index =
    ''' NAM9 slider index (0..17); PosName applied when the signed slider value ≥ 0, NegName when &lt; 0,
    ''' both with weight = abs(value). Index 18 (VampireMorph) is unidirectional and handled separately.
    ''' See project_sse_nam9_morph_map. The pos/neg split matters because the .tri stores SEPARATE
    ''' morphs per direction (independent deltas — NOT the negation of each other).</summary>
    Private Shared ReadOnly _sseNam9Morphs As (Pos As String, Neg As String)() = {
        ("NoseLong", "NoseShort"),       ' 0  Nose Long/Short
        ("NoseUp", "NoseDown"),          ' 1  Nose Up/Down
        ("JawDown", "JawUp"),            ' 2  Jaw Up/Down          (engine pos = JawDown)
        ("JawWide", "JawNarrow"),        ' 3  Jaw Narrow/Wide
        ("JawForward", "JawBack"),       ' 4  Jaw Forward/Back
        ("CheeksUp", "CheeksDown"),      ' 5  Cheeks Up/Down
        ("CheeksOut", "CheeksIn"),       ' 6  Cheeks In/Out        (xEdit label "Fwd/Back" is wrong)
        ("EyesMoveUp", "EyesMoveDown"),  ' 7  Eyes Up/Down
        ("EyesMoveOut", "EyesMoveIn"),   ' 8  Eyes In/Out
        ("BrowUp", "BrowDown"),          ' 9  Brows Up/Down
        ("BrowOut", "BrowIn"),           ' 10 Brows In/Out
        ("BrowForward", "BrowBack"),     ' 11 Brows Forward/Back
        ("LipMoveUp", "LipMoveDown"),    ' 12 Lips Up/Down
        ("LipMoveOut", "LipMoveIn"),     ' 13 Lips In/Out
        ("ChinWide", "ChinThin"),        ' 14 Chin Narrow/Wide
        ("ChinMoveDown", "ChinMoveUp"),  ' 15 Chin Up/Down         (engine pos = ChinMoveDown)
        ("Underbite", "Overbite"),       ' 16 Chin Underbite/Overbite
        ("EyesForward", "EyesBack")      ' 17 Eyes Forward/Back
    }

    ''' <summary>Build the face MorphPlan for a SKYRIM NPC from its NAM9 (18 signed directional
    ''' sliders + VampireMorph) and NAMA (Nose/Eyes/Mouth type presets), applied against the chargen
    ''' TriHead by morph NAME. This is the SSE analogue of <see cref="BuildFaceMorphPlanFromTriHead"/>
    ''' — no RACE MorphValues/Presets are consulted because Skyrim's slider→morph map is a FIXED engine
    ''' table (byte-verified, see <see cref="_sseNam9Morphs"/> / project_sse_nam9_morph_map), not
    ''' RACE-authored. Mechanism mirrors the engine (= RaceMenu TRIFile::Apply): head += triMorph.deltas
    ''' * abs(sliderValue). Channels are deduped-by-name with summed weights, same as the FO4 path.</summary>
    Public Shared Function BuildFaceMorphPlanFromNam9(npcData As NPC_Data, triHead As TriHeadFile,
                                                      Optional raceEditorId As String = "",
                                                      Optional raceKeywordEditorIds As List(Of String) = Nothing,
                                                      Optional shapeChargenTriPath As String = "",
                                                      Optional applySculpt As Boolean = True,
                                                      Optional applyBodyWeight As Boolean = True) As MorphPlan
        Dim plan As New MorphPlan()
        If npcData Is Nothing OrElse triHead Is Nothing Then Return plan

        ' 0) RACE MORPH — the racial base face. HDPT.RaceMorphTri (NAM0=0, e.g. FemaleHeadRaces.tri) carries
        ' one morph per race NAMED BY THE RACE EDITORID ("ImperialRace", "NordRace", ...). CK applies it at
        ' weight 1 BEFORE the chargen sliders — it establishes the racial skull/face shape the NPC's NAM9
        ' then adjusts. Byte/geometry-validated: adding it drops the CK FaceGeom residual from 0.168→0.075
        ' RMS and makes every NAM9/NAMA channel's least-squares weight match its value (project_sse_nam9_morph_map).
        ' The merged chargen TriHead already contains these race morphs (LoadTriForShape merges NAM0=0+NAM0=2).
        If Not String.IsNullOrEmpty(raceEditorId) Then
            AddNam9Channel(plan, triHead, raceEditorId, 1.0F)
        End If

        ' 1) NAM9 directional sliders (19 floats: 18 usable + [18] VampireMorph).
        Dim nam9 = npcData.Nam9Raw
        If nam9 IsNot Nothing AndAlso nam9.Length >= 76 Then
            For i = 0 To _sseNam9Morphs.Length - 1
                Dim v = BitConverter.ToSingle(nam9, i * 4)
                If Single.IsNaN(v) OrElse Single.IsInfinity(v) OrElse Math.Abs(v) < 0.001F Then Continue For
                Dim morphName = If(v >= 0, _sseNam9Morphs(i).Pos, _sseNam9Morphs(i).Neg)
                AddNam9Channel(plan, triHead, morphName, Math.Abs(v))
            Next
            ' [18] is the per-actor CHARGEN slider for the "VampireMorph" name in the fixed NAM9 engine table
            ' (byte-verified index→name). It drives that morph when finite (the player's progressive vampirism);
            ' vanilla pre-placed NPCs carry FLT_MAX = "not slider-driven". In that case the morph is driven by the
            ' RACE instead, RACE-AGNOSTICALLY: for each of the race's KWDA keywords, apply the chargen morph named
            ' "<keyword>Morph" at full weight. A race with the "Vampire" keyword thus gets "VampireMorph"; a race
            ' whose keywords name no morph gets nothing (AddNam9Channel no-ops on GetMorph miss — every vanilla
            ' non-vampire race). No vampire special-casing here: the rule is keyword→morph, driven purely by data.
            ' Measured: reproduces the CK FaceGeom for pre-placed *Vampire NPCs across races; zero effect elsewhere.
            Dim vamp = BitConverter.ToSingle(nam9, 18 * 4)
            If Not Single.IsNaN(vamp) AndAlso Not Single.IsInfinity(vamp) AndAlso Math.Abs(vamp) >= 0.001F AndAlso Math.Abs(vamp) < 3.0E+38F Then
                AddNam9Channel(plan, triHead, "VampireMorph", Math.Abs(vamp))
            ElseIf raceKeywordEditorIds IsNot Nothing Then
                For Each kw In raceKeywordEditorIds
                    If Not String.IsNullOrEmpty(kw) Then AddNam9Channel(plan, triHead, kw & "Morph", 1.0F)
                Next
            End If
        End If

        ' 2) NAMA type presets. NAMA = {Nose, "Unknown", Eyes, Mouth} (u32×4). Byte-verified against
        ' SkyrimSE.exe: the 4 fields map 1:1 to the engine's face-part family table @0x1ff9470
        ' {NoseType, BrowType, EyesType, LipType} — i.e. the xEdit "Unknown" field IS the BROW type.
        ' The engine builds the morph name via sprintf("%s%d", family, N) (builder 0x1403B83F0) and
        ' looks it up by NAME (the ordinal/valid-bitmask path 0x3e1420 is chargen-UI navigation, not the
        ' bake): N==0 → "Default", N>0 → family&N, 0xFFFFFFFF → no preset. Applied at full weight.
        Dim nama = npcData.NamaRaw
        If nama IsNot Nothing AndAlso nama.Length >= 16 Then
            AddNamaTypePreset(plan, triHead, "NoseType", BitConverter.ToUInt32(nama, 0))
            AddNamaTypePreset(plan, triHead, "BrowType", BitConverter.ToUInt32(nama, 4))
            AddNamaTypePreset(plan, triHead, "EyesType", BitConverter.ToUInt32(nama, 8))
            AddNamaTypePreset(plan, triHead, "LipType", BitConverter.ToUInt32(nama, 12))
        End If

        ' 2b) RaceMenu EXTENDED custom morphs (NiOverride ValueSet) — layered on top of the vanilla NAM9/NAMA.
        ' The ValueSet is keyed by the SLIDER NAME (skee64 FaceMorphInterface LoadSliders:1315), NOT the TRI morph
        ' name. Applied faithfully per ApplyMorphs (:1229-1247): resolve the slider in the per-race catalog, then
        '   • Slider   : value V<0 ⇒ lowerBound morph at |V|; V>0 ⇒ upperBound morph at V (TRIFile::Apply is linear
        '                in the weight, so |V|>1 collapses to a single channel at weight |V|).
        '   • Preset   : morph (lowerBound & int(V)) at 1.0.
        '   • HeadPart : a head-part selection, not a morph — skipped here.
        ' No catalog / unknown slider ⇒ apply the name directly (covers simple name==morph sliders + keeps working
        ' when the RaceMenu slider config isn't installed).
        If npcData.SseCustomMorphs IsNot Nothing Then
            For Each cm In npcData.SseCustomMorphs
                If cm Is Nothing OrElse String.IsNullOrEmpty(cm.Name) OrElse Math.Abs(cm.Value) < 0.0001F Then Continue For
                AddCustomMorphChannel(plan, triHead, raceEditorId, npcData.IsFemale, cm.Name, cm.Value)
            Next
        End If

        ' 2c) RaceMenu (.jslot) per-vertex SCULPT — a direct delta channel (weight 1), applied AFTER the slider/
        ' type morphs (RaceMenu sculpt is the final free-form adjustment). Deltas are already world-space (the
        ' loader divided by sculptDivisor). A preset sculpts head + brows + eyes + mouth as SEPARATE blocks; route
        ' the block whose Host == THIS shape's chargen tri (NAM0=2) to this shape — that's the geometry identity
        ' skee serializes, so brows/eyes/mouth get their own sculpt instead of only the head (the old code applied
        ' Sculpt(0) to every shape → brows/eyes/mouth ignored the preset AND the head block bled onto them by index).
        ' Fall back to the head-only SseSculptHead when the overlay predates per-shape parsing (editor-authored).
        ' applySculpt=False (checkbox "Sculpt" OFF en el preview) ⇒ no se emite el canal: la cara queda con los
        ' NAM9/NAMA vanilla, sin los deltas libres del .jslot. Es el análogo SSE del toggle ARMA SCLP de FO4.
        Dim sculptVerts As List(Of NPC_SculptVert) = Nothing
        If Not applySculpt Then
            sculptVerts = Nothing
        ElseIf npcData.SseSculptParts IsNot Nothing AndAlso npcData.SseSculptParts.Count > 0 Then
            If Not String.IsNullOrEmpty(shapeChargenTriPath) Then
                Dim wantKey = MeshPathHelpers.NormalizeMeshKey(shapeChargenTriPath)
                For Each p In npcData.SseSculptParts
                    If p IsNot Nothing AndAlso Not String.IsNullOrEmpty(p.Host) AndAlso
                       String.Equals(MeshPathHelpers.NormalizeMeshKey(p.Host), wantKey, StringComparison.OrdinalIgnoreCase) Then
                        sculptVerts = p.Verts : Exit For
                    End If
                Next
            End If
        ElseIf npcData.SseSculptHead IsNot Nothing AndAlso npcData.SseSculptHead.Count > 0 Then
            sculptVerts = npcData.SseSculptHead
        End If
        If sculptVerts IsNot Nothing AndAlso sculptVerts.Count > 0 Then
            Dim ds As New List(Of MorphData)(sculptVerts.Count)
            For Each sv In sculptVerts
                ds.Add(New MorphData With {.index = CUInt(sv.Index), .PosDiff = New Vector3(sv.Dx, sv.Dy, sv.Dz)})
            Next
            plan.Channels.Add(New MorphChannel("RaceMenuSculpt", 1.0F, ds))
        End If

        ' 2d) HEAD WEIGHT morph (SSE) — engine-derived, replaces the former hardcoded SseHeadWeightDelta table.
        ' SkyrimSE.exe applies the actor weight to the head as an ORDINARY morph channel: applier 0x1403B90D0
        ' reads the weight at actor+0x1FC, computes frac = 1 - weight*0.01, then calls the standard int16-RLE
        ' delta morph applier (0x140430FE0 → 0x140430190) for the morph named "SkinnyMorph", which lives in the
        ' head MESH .tri (femalehead.tri / malehead.tri) — NOT in the HDPT race-morph (NAM0=0) or chargen (NAM0=2)
        ' tris. frac = 1 at weight 0 (full thin/skinny) → 0 at weight 100 (neutral/full). Byte-verified: the
        ' SkinnyMorph deltas reproduce the deleted baked table index-for-index (RMS 1.5e-3, 0 missing verts), which
        ' also proves the tri's vertex order equals the head shape's. Reading it from the tri is AGNOSTIC (a modded
        ' head ships its own SkinnyMorph) and unifies render+bake on this one plan — no table, no separate resolver.
        ' The mesh .tri is merged into triHead by the callers (render: LoadTriForShape; bake: LoadMergedHeadTri),
        ' so on a non-head shape GetMorph("SkinnyMorph") is Nothing and AddNam9Channel no-ops (natural gating).
        ' applyBodyWeight=False (checkbox "Body weight" OFF en el preview) ⇒ peso neutro: no se emite el
        ' SkinnyMorph, igual que el resolver del cuerpo (_0/_1) no se engancha. Cabeza y cuerpo apagan juntos.
        Dim nam7 = npcData.Nam7Raw
        Dim weightVal As Single = If(nam7 IsNot Nothing AndAlso nam7.Length >= 4, BitConverter.ToSingle(nam7, 0), 100.0F)
        Dim skinnyFrac As Single = 1.0F - Math.Max(0.0F, Math.Min(1.0F, weightVal / 100.0F))
        If applyBodyWeight AndAlso skinnyFrac > 0.0000001F Then AddNam9Channel(plan, triHead, "SkinnyMorph", skinnyFrac)

        ' 3) Dedup channels by name SUMMING weights (same convention as the FO4 path — a slider and a
        ' type preset could both resolve to the same morph name; the engine applies the sum). The sculpt
        ' channel has a unique name so it survives dedup as a distinct additive layer.
        DedupSumChannelsByName(plan)
        Return plan
    End Function

    ''' <summary>Look up <paramref name="morphName"/> in the chargen TriHead and, if present with
    ''' non-empty deltas, add a MorphChannel at <paramref name="weight"/>. No-op for missing morphs.</summary>
    ''' <summary>The RaceMenu EXTENDED face-slider catalog (per-race .slider config), built once by the app from
    ''' the loaded plugins and read by the shared render/bake morph path to resolve a custom-morph SLIDER NAME to
    ''' its actual TRI morph(s). Nothing until the app populates it (e.g. FO4 sessions never set it) → the custom
    ''' morph loop falls back to applying the name directly.</summary>
    Public Shared Property SliderCatalog As FO4_Base_Library.RaceMenuSliderCatalog

    ''' <summary>Apply one RaceMenu extended custom morph (slider name → value) faithfully to the plan via the
    ''' catalog. See caller comment / skee64 ApplyMorphs:1229-1247.</summary>
    Private Shared Sub AddCustomMorphChannel(plan As MorphPlan, triHead As TriHeadFile, raceEditorId As String, isFemale As Boolean, sliderName As String, value As Single)
        Dim def As FO4_Base_Library.RaceMenuSliderCatalog.SliderDef = Nothing
        If SliderCatalog IsNot Nothing Then def = SliderCatalog.GetSlider(raceEditorId, isFemale, sliderName)
        If def Is Nothing Then
            AddNam9Channel(plan, triHead, sliderName, value)   ' unknown slider / no catalog → best-effort direct
            Return
        End If
        Select Case def.Type
            Case FO4_Base_Library.RaceMenuSliderCatalog.SliderType.Preset
                Dim n = CInt(Math.Truncate(CDbl(value)))
                If n > 0 AndAlso Not String.IsNullOrEmpty(def.LowerBound) Then AddNam9Channel(plan, triHead, def.LowerBound & n.ToString(), 1.0F)
            Case FO4_Base_Library.RaceMenuSliderCatalog.SliderType.Slider
                Dim morphName = If(value < 0, def.LowerBound, def.UpperBound)
                If Not String.IsNullOrEmpty(morphName) Then AddNam9Channel(plan, triHead, morphName, Math.Abs(value))
            Case FO4_Base_Library.RaceMenuSliderCatalog.SliderType.HeadPart
                ' Head-part selection, not a morph — no plan channel.
        End Select
    End Sub

    Private Shared Sub AddNam9Channel(plan As MorphPlan, triHead As TriHeadFile, morphName As String, weight As Single)
        If String.IsNullOrEmpty(morphName) Then Return
        Dim triMorph = triHead.GetMorph(morphName)
        If triMorph Is Nothing OrElse triMorph.Vertices Is Nothing OrElse triMorph.Vertices.Length = 0 Then Return
        Dim deltas = ConvertTriHeadMorphToMorphData(triMorph)
        If deltas.Count > 0 Then plan.Channels.Add(New MorphChannel(morphName, weight, deltas))
    End Sub

    ''' <summary>Apply a NAMA face-part type preset, engine-faithful (SkyrimSE.exe name builder
    ''' 0x1403B83F0): 0xFFFFFFFF → no preset; 0 → the "Default" morph; N&gt;0 → "&lt;family&gt;&lt;N&gt;"
    ''' (e.g. NoseType3, EyesType18, LipType11). Matched by name in the chargen .tri, at full weight.
    ''' A missing morph (e.g. the vanilla femaleheadchargen quirk with no "NoseType10") is silently
    ''' skipped — the engine's by-name lookup does the same.</summary>
    Private Shared Sub AddNamaTypePreset(plan As MorphPlan, triHead As TriHeadFile, family As String, typeIndex As UInteger)
        If typeIndex = UInteger.MaxValue Then Return          ' 0xFFFFFFFF = no preset
        Dim morphName = If(typeIndex = 0UI, "Default", family & typeIndex.ToString())
        AddNam9Channel(plan, triHead, morphName, 1.0F)
    End Sub

    ''' <summary>Dedup a plan's channels by morph name, summing weights (shared FO4/SSE convention).</summary>
    Private Shared Sub DedupSumChannelsByName(plan As MorphPlan)
        If plan.Channels.Count <= 1 Then Return
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
    End Sub

    ''' <summary>
    ''' Add face morph presets from MSDK/MSDV using TriHead (Bethesda format) + RACE Morph Values.
    ''' Instance-side wrapper: delegates to <see cref="BuildFaceMorphPlanFromTriHead"/> using this
    ''' resolver's stored npcData / RACE defs. Kept for readability of ResolveMorphPlan.
    ''' </summary>
    Private Sub AddFaceMorphPresetsFromTriHead(triHead As TriHeadFile, plan As MorphPlan)
        Dim built = BuildFaceMorphPlanFromTriHead(_npcData, _morphValueDefs, _morphPresetDefs, triHead, logShapeName:="head")
        plan.Channels.AddRange(built.Channels)
    End Sub

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
            MergeChargenIntoRaceTriHead(triHead, chargenHead)

            ' RaceMenu EXTENDED morphs. A .slider bound — and therefore a .jslot custom morph — is a morph NAME
            ' whose geometry lives in a SEPARATE .tri, not in the chargen tri: morphs.ini maps this shape's chargen
            ' tri to a list of extended tris, and skee64 applies the named morph out of each one
            ' (MorphVisitor::Accept, SKEEHooks.cpp:687-696, via GetExtendedModelTri). Merging them here is the same
            ' composition, so every downstream consumer — NAM9/NAMA channels, custom-morph channels, render AND
            ' bake — resolves the name with no special case. Chargen/race morphs merged above win a name collision.
            ' Without this, every extended slider silently moved nothing.
            Dim catalog = SliderCatalog
            If catalog IsNot Nothing Then
                For Each extTriPath In catalog.GetExtendedMorphTris(chargenPath)
                    Dim extHead = TryLoadTriHead(MeshPathHelpers.NormalizeMeshKey(extTriPath))
                    If extHead IsNot Nothing Then MergeChargenIntoRaceTriHead(triHead, extHead)
                Next
            End If
        End If

        ' SSE HEAD WEIGHT source: the "SkinnyMorph" channel (the actor-weight head morph, applied by
        ' BuildFaceMorphPlanFromNam9 as frac=1-NAM7/100) lives in the head MESH .tri — femalehead.tri /
        ' malehead.tri = ChangeExtension(meshKey,".tri") — which is NEITHER the HDPT race-morph tri (NAM0=0)
        ' NOR the chargen tri (NAM0=2). Merge it so the single face MorphPlan can apply the weight morph
        ' engine-faithfully instead of a hardcoded table. SSE-only; race/chargen names already loaded win the
        ' collision (merge adds only absent names → only SkinnyMorph + unused expression morphs join). Per-shape
        ' and AGNOSTIC: each shape merges its OWN mesh tri, so a modded head/hair with its own SkinnyMorph works.
        If _npcData IsNot Nothing AndAlso _npcData.Game = Config_App.Game_Enum.Skyrim Then
            ' AUTHORITATIVE and ONLY mesh-tri path = HDPT NAM0=1 (ShapeMeshMorphTriPaths). The engine/CK apply the
            ' mesh weight morph IFF the record declares it here. The NIF and its weight tri do NOT always share a
            ' basename (Hair08.nif → Elf\Male\ElfHair08.tri), so the old ChangeExtension(meshKey) guess both MISSED
            ' the real tri (elf/nord hair rendered un-weighted) AND wrongly applied a same-named tri the CK ignores
            ' (HairMaleDarkElf02 has no NAM0=1 yet MaleDarkElfHair02.tri exists → over-morphed). Using only NAM0=1
            ' makes the render match the CK bake for both cases (render == bake). See FaceGenBuilder for the twin.
            Dim meshTriPath As String = Nothing
            If _shapeMeshMorphTriPaths IsNot Nothing Then _shapeMeshMorphTriPaths.TryGetValue(shape, meshTriPath)
            If Not String.IsNullOrEmpty(meshTriPath) Then
                Dim meshTriHead = TryLoadTriHead(MeshPathHelpers.NormalizeMeshKey(meshTriPath))
                MergeChargenIntoRaceTriHead(triHead, meshTriHead)
            End If

            ' [SSE-TRI] Per-shape trace of the weight-morph inputs: which .tri each SSE head-part shape
            ' resolved (HDPT NAM0=0/1/2 vs the mesh-name fallback), whether the merged TriHead ended up
            ' carrying "SkinnyMorph", and the frac the plan will apply (1 - NAM7/100). A hair shape logging
            ' hasSkinnyMorph=False or skinnyFrac=0 means it renders un-weighted while the head is weighted —
            ' the head-part occlusion is NOT involved in that.
            If Logger.Enabled Then
                Dim shName = If(shape.ShapeName, "?")
                Dim meshKeyD = If(meshKey, "")
                Dim raceD = If(raceMorphPath, "")
                Dim chargenD = If(chargenPath, "")
                Dim meshTriD = If(meshTriPath, "")
                Dim triHeadD = triHead
                Dim vertsD = If(triHeadD Is Nothing, 0UI, triHeadD.NumVertices)
                Dim morphsD = If(triHeadD Is Nothing, 0, triHeadD.Morphs.Count)
                Dim hasSkinnyD = triHeadD IsNot Nothing AndAlso triHeadD.GetMorph("SkinnyMorph") IsNot Nothing
                Dim nam7D = _npcData.Nam7Raw
                Dim weightD As Single = If(nam7D IsNot Nothing AndAlso nam7D.Length >= 4, BitConverter.ToSingle(nam7D, 0), 100.0F)
                Dim fracD As Single = 1.0F - Math.Max(0.0F, Math.Min(1.0F, weightD / 100.0F))
                Logger.LogLazy(Function() $"[SSE-TRI] shape='{shName}' mesh='{meshKeyD}' raceTri='{raceD}' chargenTri='{chargenD}' meshTri(NAM0=1)='{meshTriD}' triHead={(triHeadD IsNot Nothing)} triVerts={vertsD} morphs={morphsD} hasSkinnyMorph={hasSkinnyD} nam7Weight={weightD} skinnyFrac={fracD}")
            End If
        End If
    End Sub

    Private Function TryLoadPirt(normalizedPath As String) As TriFile
        If String.IsNullOrEmpty(normalizedPath) Then Return Nothing

        Return _triCache.GetOrLoad(normalizedPath,
            Function() As TriFile
                Dim bytes = TryGetFileBytes(normalizedPath)
                If bytes Is Nothing Then Return Nothing
                Return TriFileParser.ParseTriFromBytes(bytes)
            End Function)
    End Function

    Private Function TryLoadTriHead(normalizedPath As String) As TriHeadFile
        If String.IsNullOrEmpty(normalizedPath) Then Return Nothing

        ' Key the cache on the mouth-fix state so the vanilla and the fixed BaseFemaleHeadChargen.tri head
        ' live under distinct keys — toggling Setting_ApplyMouthVanillaFix then re-reads the right one
        ' instead of serving a stale (fixed/vanilla) cached head. Suffix is "" for every other file.
        Dim cacheKey = normalizedPath & ChargenMouthFix.CacheKeySuffix(normalizedPath)

        Return _triHeadCache.GetOrLoad(cacheKey,
            Function() As TriHeadFile
                Dim bytes = TryGetFileBytes(normalizedPath)
                If bytes Is Nothing Then Return Nothing

                Dim head = TriHeadParser.ParseTriHeadFromBytes(bytes)
                If head Is Nothing Then Return Nothing

                ' Zero the 22 vanilla mouth deltas iff the toggle is on and this is the female chargen tri
                ' (no-op otherwise). Done on the fresh parse before caching, so the merge downstream sees it.
                ChargenMouthFix.MaybeApplyInPlace(normalizedPath, head)
                Return head
            End Function)
    End Function

    ' Routed through MeshPathHelpers.TryLoadMeshBytes (minBytes:=8 preserves the TRI-magic guard)
    ' so the TryGetValue + GetBytes + size-check lives in one place (DUP-004).
    Private Shared Function TryGetFileBytes(normalizedPath As String) As Byte()
        Return MeshPathHelpers.TryLoadMeshBytes(normalizedPath, minBytes:=8)
    End Function

End Class
