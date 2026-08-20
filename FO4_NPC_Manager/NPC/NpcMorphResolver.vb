Imports System.IO
Imports OpenTK.Mathematics
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

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
    Private ReadOnly _morphValueDefs As IReadOnlyList(Of Canon.RaceFO4_MorphValues)   ' MSID -> MSM0/MSM1 del RACE
    Private ReadOnly _morphPresetDefs As List(Of RACE_MorphPresetDef)   ' MPPI -> MPPM from RACE Morph Groups
    ''' <summary>SSE-only gates (los canales que en Skyrim viven DENTRO del plan de cara y en FO4 son
    ''' pipelines aparte): sculpt per-vértice de RaceMenu = checkbox "Sculpt", y el SkinnyMorph de la
    ''' cabeza/pelo = checkbox "Body weight". El bake los deja en True (default del ctor) — es un toggle
    ''' de PREVIEW, no cambia lo que se hornea. Inertes en el camino FO4 (que no emite esos canales).</summary>
    Private ReadOnly _applySculpt As Boolean = True
    Private ReadOnly _applyBodyWeight As Boolean = True
    ''' <summary>SSE-only: emite los canales de FORMA de cara (race base + NAM9 + NAMA + custom + sculpt).
    ''' False = solo queda el SkinnyMorph del weight (sección 2d). Lo apagan (a) el checkbox "Vertex
    ''' morphs (TRI)" cuando "Body weight" sigue ON — antes eso mataba el resolver ENTERO y con él el
    ''' weight de la cabeza mientras el cuerpo _0/_1 seguía lerpeando ⇒ costura de cuello — y (b) el
    ''' preview "Show other gender" (los morphs del NPC no aplican cross-gender, el weight sí). El bake
    ''' lo deja en True (default del ctor). Inerte en FO4 (su plan no emite SkinnyMorph).</summary>
    Private ReadOnly _applyChargenMorphs As Boolean = True
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

    ' High Poly Head (KouLeifoh) redirect — see Config_App.Setting_SseResolveHighPolyHeadTri and
    ' ResolveHphHeadPartTriPath. Measured vertex counts from HPH v1.4 (SE): a shape with one of these counts IS the
    ' corresponding HPH head part, and its race/chargen/mesh morph tris live under HphDir (loose) or the equivalent
    ' internal path in High Poly Head.bsa — NEVER at the vanilla `Actors\Character\Character Assets\` path.
    '   FemaleHead 'FemaleHead_KLH' = 3832 ; MaleHead 'MaleHeadIMF_KLH' = 3598 ; FemaleBrows = 371 ; MaleBrows = 318.
    ' The per-part triplet (races / chargen / mesh) is the MEASURED HPH filename set (irregular: female brows race
    ' = "femaleheadbrowsrace.tri" SINGULAR but male = "maleheadbrowsraces.tri" PLURAL; HPH ships NO male head chargen
    ' — malehead uses maleheadcustomizations, so Male/Chargen is empty and degrades without harm).
    Private Const HphFemaleHeadVerts As Integer = 3832
    Private Const HphMaleHeadVerts As Integer = 3598
    Private Const HphFemaleBrowVerts As Integer = 371
    Private Const HphMaleBrowVerts As Integer = 318
    Private Const HphDir As String = "meshes\KL\High Poly Head\"
    ' Dirs probed for the basename-reuse fallback (an existing broken record path redirected to the same filename
    ' under HPH): head tris live in the root, brows/masks/scars under faceparts\, facial hair under beards\.
    Private Shared ReadOnly HphBasenameDirs As String() = {HphDir, HphDir & "faceparts\", HphDir & "beards\"}

    ''' <summary>Which head-part morph slot a tri fills, for the HPH redirect (HDPT NAM0 codes: 0=Race, 2=Chargen,
    ''' 1=Mesh/SkinnyMorph).</summary>
    Public Enum HphTriSlot
        Race
        Chargen
        Mesh
    End Enum

    ''' <summary>Drop the per-process TRI parse caches. Call on load-order change (FilesDictionary rebuilt):
    ''' a path could resolve to different bytes after a reload, so the cached parse would be stale, and the
    ''' parsed-geometry entries (potentially MBs each) are freed. Within a FIXED load order this is never
    ''' called, so a browsed .tri is parsed at most once — no re-parse churn during a session.</summary>
    Public Shared Sub ClearCaches()
        _triCache.Clear()
        _triHeadCache.Clear()
    End Sub

    ' MRSV — Body Morph Region Values. El subrecord es un struct fijo de 5 floats con estas
    ' etiquetas de región en este orden. Mismo layout en FO76 y en Starfield.
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
                   Optional morphValueDefs As IReadOnlyList(Of Canon.RaceFO4_MorphValues) = Nothing,
                   Optional morphPresetDefs As List(Of RACE_MorphPresetDef) = Nothing,
                   Optional meshDictKeys As Dictionary(Of IRenderableShape, String) = Nothing,
                   Optional shapeChargenTriPaths As Dictionary(Of IRenderableShape, String) = Nothing,
                   Optional shapeRaceMorphTriPaths As Dictionary(Of IRenderableShape, String) = Nothing,
                   Optional raceEditorId As String = "",
                   Optional shapeMeshMorphTriPaths As Dictionary(Of IRenderableShape, String) = Nothing,
                   Optional raceKeywordEditorIds As List(Of String) = Nothing,
                   Optional applySculpt As Boolean = True,
                   Optional applyBodyWeight As Boolean = True,
                   Optional applyChargenMorphs As Boolean = True)
        _npcData = npcData
        _applySculpt = applySculpt
        _applyBodyWeight = applyBodyWeight
        _applyChargenMorphs = applyChargenMorphs
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

        ' Semantica de aplicacion: final[i] = NIF.rest[i] + suma de morph.delta[i]. Los deltas del TriHead son
        ' 1:1 con los INDICES de vertice del NIF y el guard de rango de MorphEngine descarta los fuera de rango,
        ' asi que los tres casos de conteo quedan cubiertos sin corrupcion: si coinciden se aplican todos; si el
        ' TRI tiene MENOS (el NIF trae extras de rigging sin morph target) se aplican los primeros N y los extras
        ' quedan en reposo; si tiene MAS (NIF achicado) se pierden deltas pero nada se corrompe.
        ' El alineamiento por INDICE es la verdad del runtime, no el alineamiento por posicion.

        ' MWGT (Thin/Muscular/Fat) y MRSV (Body Morph Region Values) NO se aplican aca: viajan por el pipeline de
        ' escala osea (BuildBodyWeightPose), MWGT como capa 1 (RACE.BSMS WeightScale, interpolando por hueso) y
        ' MRSV como capa 3 (BSMS RangeModifier Min/Max Y,Z, mapeando cada hueso a una de 5 regiones).
        ' Antes corria ademas AddBodyWeightMorphs en paralelo, buscando los morphs WeightThin/Muscular/Fat en el
        ' .tri PIRT del cuerpo: eso aplicaba MWGT DOS VECES cuando el body mod del usuario traia esos morphs.

        ' 2) Face morph presets - GAME-AWARE:
        '  • FO4  = MSDK/MSDV sliders via RACE MSID→MSM0/MSM1 + MPPI presets (RACE-defined name map).
        '  • SSE  = NAM9 (18 signed sliders) + NAMA (Nose/Eyes/Mouth type) via a FIXED engine name
        '           table (no RACE defs) — byte-verified from SkyrimSE.exe @0x1ff92a0. See
        '           22-morphs-sse-nam9-map. Same chargen .tri, same TriHead.GetMorph mechanism.
        If triHead IsNot Nothing Then
            ' Single per-game builder — the SAME one the offline bake calls, so render and bake never diverge.
            ' Pass this shape's chargen tri (NAM0=2) so RaceMenu per-shape sculpt routes to it by Host.
            Dim shapeChargen As String = Nothing
            If _shapeChargenTriPaths IsNot Nothing Then _shapeChargenTriPaths.TryGetValue(shape, shapeChargen)
            plan.Channels.AddRange(BuildFaceMorphPlan(_npcData, triHead, _raceEditorId, _morphValueDefs, _morphPresetDefs, _raceKeywordEditorIds, shapeChargen,
                                                     applySculpt:=_applySculpt, applyBodyWeight:=_applyBodyWeight,
                                                     applyChargenMorphs:=_applyChargenMorphs).Channels)
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

    ''' <summary>â›” SYNC: RENDER == BAKE. ESTE es el unico builder del plan de morphs de cara por juego, y el
    ''' punto donde el contrato se cumple POR CONSTRUCCION: lo llaman los DOS caminos, el render vivo
    ''' (<see cref="ResolveMorphPlan"/>) y el bake offline (<c>FaceGenBuildPipeline.ApplyChargenMorphsInPlace</c>).
    ''' Si algun dia se agrega un segundo builder, preview y NIF horneado divergen sin que nada falle: todo canal
    ''' de morph nuevo entra ACA, no en un caller.
    ''' <para>Despacha por juego sobre el MISMO TriHead (raza + chargen mergeados): FO4 usa sliders MSDK/MSDV y
    ''' presets MPPI; Skyrim usa base de raza + sliders NAM9 + presets NAMA + morphs custom y sculpt por vertice
    ''' de RaceMenu. En los dos casos LooksMenu/RaceMenu ya vienen plegados en npcData por NpcRecordOverlay.</para>
    ''' <para>El morph de PESO del actor en SSE (canal SkinnyMorph, leido del .tri de cada mesh a frac =
    ''' 1 - NAM7/100) va dentro de este mismo plan: una sola fuente para render y bake, por parte y por raza, sin
    ''' resolver aparte ni tabla de deltas.</para>
    ''' <para><paramref name="applySculpt"/> y <paramref name="applyBodyWeight"/> son los toggles del PREVIEW de
    ''' SSE para los dos canales que viven en este plan; van en True por defecto para que el bake offline, que no
    ''' puede depender del estado de la UI, quede intacto.</para></summary>
    Public Shared Function BuildFaceMorphPlan(npcData As NPC_Data, triHead As TriHeadFile,
                                              raceEditorId As String,
                                              morphValueDefs As IReadOnlyList(Of Canon.RaceFO4_MorphValues),
                                              morphPresetDefs As List(Of RACE_MorphPresetDef),
                                              Optional raceKeywordEditorIds As List(Of String) = Nothing,
                                              Optional shapeChargenTriPath As String = "",
                                              Optional applySculpt As Boolean = True,
                                              Optional applyBodyWeight As Boolean = True,
                                              Optional applyChargenMorphs As Boolean = True) As MorphPlan
        Dim plan As New MorphPlan()
        If npcData Is Nothing OrElse triHead Is Nothing Then Return plan
        If npcData.Game = Config_App.Game_Enum.Skyrim Then
            plan.Channels.AddRange(BuildFaceMorphPlanFromNam9(npcData, triHead, raceEditorId, raceKeywordEditorIds, shapeChargenTriPath,
                                                              applySculpt, applyBodyWeight, applyChargenMorphs).Channels)
        ElseIf npcData.Record.MorfosDeCara().Count > 0 Then
            plan.Channels.AddRange(BuildFaceMorphPlanFromTriHead(npcData, morphValueDefs, morphPresetDefs, triHead).Channels)
        End If
        Return plan
    End Function

    ''' <summary>Merge the CHARGEN TriHead's morphs into the RACE TriHead in place — race morphs win on name
    ''' collision (race is loaded first; a chargen morph is added only when the race head lacks that name). THE
    ''' single merge used by both the render (<see cref="LoadTriForShape"/>) and the bake (FaceGenBuildPipeline)
    ''' so the head TriHead they feed <see cref="BuildFaceMorphPlan"/> is assembled identically. When
    ''' <paramref name="triHead"/> is Nothing it becomes the chargen head; when chargen is Nothing it is a no-op.</summary>
    ''' <remarks>NUNCA MUTA LA INSTANCIA QUE RECIBE. `triHead` y `chargenHead` vienen de
    ''' <c>TryLoadTriHead</c>, o sea de <c>_triHeadCache</c>, que es `Shared` y devuelve SIEMPRE LA MISMA
    ''' instancia para la misma ruta. Esta función hacía `triHead.Morphs.Add(...)` sobre ella y eso rompía
    ''' de dos maneras:
    ''' <para>1. CARRERA: `ResolveMorphPlan` corre bajo `Parallel.ForEach`. Un hilo mergeando (Add) contra
    ''' otro adentro de `GetMorph` (que hace `List.Find`, o sea ENUMERA) ⇒ `InvalidOperationException`
    ''' intermitente. Es el fallo que aparece una vez y a la siguiente corrida no.</para>
    ''' <para>2. CONTAMINACIÓN entre NPCs, que no tira y por eso es peor: el .tri de RAZA cacheado quedaba
    ''' con los morphs de chargen y con los EXTENDIDOS de RaceMenu del primer NPC que pasara, y todos los
    ''' demás NPC de esa raza los heredaban. Y el `triHead = chargenHead` de abajo ALIASEABA la entrada de
    ''' chargen del caché, así que el merge siguiente la contaminaba también.</para>
    ''' <para>Se clona antes de tocar nada (copia superficial: sólo se duplica la LISTA, que es lo único
    ''' que se muta). `ByRef` ya estaba, así que el llamador recibe la copia sin cambios de firma.</para></remarks>
    Public Shared Sub MergeChargenIntoRaceTriHead(ByRef triHead As TriHeadFile, chargenHead As TriHeadFile)
        If chargenHead Is Nothing Then Return
        If triHead Is Nothing Then triHead = chargenHead.ClonarParaMerge() : Return
        Dim copia = triHead.ClonarParaMerge()
        For Each morph In chargenHead.Morphs
            If copia.GetMorph(morph.Name) Is Nothing Then copia.Morphs.Add(morph)
        Next
        ' `NumMorphs` SE SINCRONIZA. Es el conteo del header del .tri y el merge lo dejaba desfasado de
        ' `Morphs.Count`. Hoy nadie lo lee (grep: declaracion, clon y escritura del parser), asi que es
        ' inerte — pero es un campo que MIENTE, y el dia que alguien escriba un .tri desde un TriHeadFile
        ' eso es corrupcion de bytes con la causa a mil lineas de distancia.
        copia.NumMorphs = CUInt(copia.Morphs.Count)
        triHead = copia
    End Sub

    Public Shared Function BuildFaceMorphPlanFromTriHead(npcData As NPC_Data,
                                                        morphValueDefs As IReadOnlyList(Of Canon.RaceFO4_MorphValues),
                                                        morphPresetDefs As List(Of RACE_MorphPresetDef),
                                                        triHead As TriHeadFile,
                                                        Optional logShapeName As String = "") As MorphPlan
        Dim plan As New MorphPlan()
        If npcData Is Nothing OrElse triHead Is Nothing Then Return plan
        ' Se arman una sola vez: cada llamada recorre las dos listas paralelas del record.
        Dim morfos = npcData.Record.MorfosDeCara()
        If morfos.Count = 0 Then Return plan

        ' 1) Morph Values (MSID → MSM0/MSM1 slider morphs)
        If morphValueDefs IsNot Nothing Then
            For Each mvDef In morphValueDefs
                Dim weight As Single = 0
                If Not morfos.TryGetValue(mvDef.ValueIndex, weight) Then Continue For
                If Math.Abs(weight) < 0.001F Then Continue For

                Dim usedMax As Boolean = (weight >= 0)
                Dim morphName = If(usedMax, mvDef.ValueMaxName, mvDef.ValueMinName)
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
                If Not morfos.TryGetValue(mpDef.Index, weight) Then Continue For
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

        ' 2b) Ley del motor: descartar los canales con peso fuera de [-1,1] ANTES del dedup. Los canales de
        ' este builder (MSDK sliders + MPPI presets) los aplica el applier nativo, así que van todos con
        ' EngineApplied=True (el default). Ver DropChannelsRejectedByEngine.
        DropChannelsRejectedByEngine(plan)

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
    ''' See 22-morphs-sse-nam9-map. The pos/neg split matters because the .tri stores SEPARATE
    ''' morphs per direction (independent deltas — NOT the negation of each other).</summary>
    Private Shared ReadOnly _sseNam9Morphs As (Pos As String, Neg As String)() = {
        ("NoseLong", "NoseShort"),       ' 0  Nose Long/Short
        ("NoseUp", "NoseDown"),          ' 1  Nose Up/Down
        ("JawDown", "JawUp"),            ' 2  Jaw Up/Down          (engine pos = JawDown)
        ("JawWide", "JawNarrow"),        ' 3  Jaw Narrow/Wide
        ("JawForward", "JawBack"),       ' 4  Jaw Forward/Back
        ("CheeksUp", "CheeksDown"),      ' 5  Cheeks Up/Down
        ("CheeksOut", "CheeksIn"),       ' 6  Cheeks In/Out (el rótulo "Fwd/Back" es incorrecto)
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
    ''' table (byte-verified, see <see cref="_sseNam9Morphs"/> / 22-morphs-sse-nam9-map), not
    ''' RACE-authored. Mechanism mirrors the engine (= RaceMenu TRIFile::Apply): head += triMorph.deltas
    ''' * abs(sliderValue). Channels are deduped-by-name with summed weights, same as the FO4 path.</summary>
    Public Shared Function BuildFaceMorphPlanFromNam9(npcData As NPC_Data, triHead As TriHeadFile,
                                                      Optional raceEditorId As String = "",
                                                      Optional raceKeywordEditorIds As List(Of String) = Nothing,
                                                      Optional shapeChargenTriPath As String = "",
                                                      Optional applySculpt As Boolean = True,
                                                      Optional applyBodyWeight As Boolean = True,
                                                      Optional applyChargenMorphs As Boolean = True) As MorphPlan
        ' applyChargenMorphs=False ⇒ se suprimen los canales de FORMA (race base, NAM9, NAMA, custom,
        ' sculpt) y solo puede emitirse el SkinnyMorph del weight (2d). Usos: checkbox "Vertex morphs
        ' (TRI)" OFF con "Body weight" ON, y el preview "Show other gender". El weight es per-actor e
        ' independiente del chargen (applier 0x1403B90D0), así que NO se apaga con los morphs de forma.
        Dim plan As New MorphPlan()
        If npcData Is Nothing OrElse triHead Is Nothing Then Return plan

        ' 0) RACE MORPH — the racial base face. HDPT.RaceMorphTri (NAM0=0, e.g. FemaleHeadRaces.tri) carries
        ' one morph per race NAMED BY THE RACE EDITORID ("ImperialRace", "NordRace", ...). CK applies it at
        ' weight 1 BEFORE the chargen sliders — it establishes the racial skull/face shape the NPC's NAM9
        ' then adjusts. Byte/geometry-validated: adding it drops the CK FaceGeom residual from 0.168→0.075
        ' RMS and makes every NAM9/NAMA channel's least-squares weight match its value (22-morphs-sse-nam9-map).
        ' The merged chargen TriHead already contains these race morphs (LoadTriForShape merges NAM0=0+NAM0=2).
        If applyChargenMorphs AndAlso Not String.IsNullOrEmpty(raceEditorId) Then
            AddNam9Channel(plan, triHead, raceEditorId, 1.0F)
        End If

        ' 1) NAM9 directional sliders (19 floats: 18 usable + [18] VampireMorph).
        Dim nam9 = npcData.Record.DeslizadoresDeCara()
        ' True cuando el slider [18] manejó el morph ⇒ el fallback por keyword de raza NO debe correr.
        Dim keywordMorphsApplied As Boolean = False
        If applyChargenMorphs AndAlso nam9 IsNot Nothing AndAlso nam9.Length >= SseNam9MorphMap.Nam9SliderCount Then
            For i = 0 To _sseNam9Morphs.Length - 1
                Dim v = nam9(i)
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
            Dim vamp = nam9(18)
            If Not Single.IsNaN(vamp) AndAlso Not Single.IsInfinity(vamp) AndAlso Math.Abs(vamp) >= 0.001F AndAlso Math.Abs(vamp) < 3.0E+38F Then
                AddNam9Channel(plan, triHead, "VampireMorph", Math.Abs(vamp))
                keywordMorphsApplied = True
            End If
        End If

        ' El fallback keyword->morph NO puede vivir dentro del guard de NAM9: un NPC SIN el subrecord es
        ' semanticamente equivalente a NAM9[18] = FLT_MAX ("no lo maneja un slider"), asi que el morph lo maneja
        ' la RACE igual. Estando adentro, el bloque entero se salteaba y el morph nunca se aplicaba.
        ' Medido sobre el corpus SSE completo (3104 NPCs con FaceGeom del CK): el separador es EXACTO, con 0
        ' falsos positivos y 0 negativos, y los 8 casos que fallaban son vampiros SIN NAM9. El residuo proyecta
        ' sobre "VampireMorph" con peso ~1,0 y al aplicarlo el maximo cae de 0,5297 a 0,00098; el conjunto de
        ' vertices movidos coincide con la prediccion, asi que la seleccion ya estaba bien y faltaba el CANAL.
        If applyChargenMorphs AndAlso Not keywordMorphsApplied AndAlso raceKeywordEditorIds IsNot Nothing Then
            For Each kw In raceKeywordEditorIds
                If Not String.IsNullOrEmpty(kw) Then AddNam9Channel(plan, triHead, kw & "Morph", 1.0F)
            Next
        End If

        ' 2) NAMA type presets. NAMA = {Nose, "Unknown", Eyes, Mouth} (u32×4). Byte-verified against
        ' SkyrimSE.exe: the 4 fields map 1:1 to the engine's face-part family table @0x1ff9470
        ' {NoseType, BrowType, EyesType, LipType} — i.e. the "Unknown" field IS the BROW type.
        ' The engine builds the morph name via sprintf("%s%d", family, N) (builder 0x1403B83F0) and
        ' looks it up by NAME (the ordinal/valid-bitmask path 0x3e1420 is chargen-UI navigation, not the
        ' bake): N==0 → "Default", N>0 → family&N, 0xFFFFFFFF → no preset. Applied at full weight.
        Dim nama = npcData.Record.PartesDeCara()
        If applyChargenMorphs AndAlso nama IsNot Nothing AndAlso nama.Length >= SseNam9MorphMap.NamaFamilyCount Then
            AddNamaTypePreset(plan, triHead, "NoseType", nama(0))
            AddNamaTypePreset(plan, triHead, "BrowType", nama(1))
            AddNamaTypePreset(plan, triHead, "EyesType", nama(2))
            AddNamaTypePreset(plan, triHead, "LipType", nama(3))
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
        If applyChargenMorphs AndAlso npcData.SseCustomMorphs IsNot Nothing Then
            For Each cm In npcData.SseCustomMorphs
                If cm Is Nothing OrElse String.IsNullOrEmpty(cm.Name) OrElse Math.Abs(cm.Value) < 0.0001F Then Continue For
                AddCustomMorphChannel(plan, triHead, raceEditorId, npcData.Record.ConfigurationFlagsFemale, cm.Name, cm.Value)
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
        If Not applySculpt OrElse Not applyChargenMorphs Then
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
            ' engineApplied:=False — el sculpt lo aplica skee64 (RaceMenu "FOD"), no el applier del motor.
            plan.Channels.Add(New MorphChannel("RaceMenuSculpt", 1.0F, ds, engineApplied:=False))
        End If

        ' 2d) HEAD WEIGHT morph (SSE), derivado del motor: SkyrimSE.exe aplica el peso del actor a la cabeza como
        ' un canal de morph ORDINARIO - lee el peso, computa frac = 1 - weight*0.01 y llama al applier estandar de
        ' deltas para el morph llamado "SkinnyMorph", que vive en el .tri del MESH de la cabeza y no en los tris
        ' de raza ni de chargen. frac = 1 con peso 0, 0 con peso 100. Byte-verificado: los deltas del SkinnyMorph
        ' reproducen indice por indice la tabla horneada que reemplazaron, lo que ademas prueba que el orden de
        ' vertices del tri es el de la shape. Leerlo del tri es AGNOSTICO (una cabeza modeada trae el suyo) y
        ' unifica render y bake en este plan: sin tabla y sin resolver aparte.
        ' â›” El canal SI aplica a barbas y pelo, no solo a la cara (humanbeardshort02.tri y hair09.tri traen
        ' SkinnyMorph), que es lo correcto porque el CK lo reparte a TODOS los hijos del BSFaceGenNiNode sin
        ' filtrar por tipo de head part. Verificado contra el CK sobre el pelo con residual max 0,00247.
        ' Con el checkbox "Body weight" apagado no se emite el SkinnyMorph: cabeza y cuerpo apagan juntos.
        ' Sin NAM7 el peso es 100: es el valor con el que el motor dibuja un actor que no lo declara.
        Dim weightVal As Single = If(npcData.Record.TienePesoDeSkyrim(), npcData.Record.PesoDeSkyrim(), 100.0F)
        Dim skinnyFrac As Single = 1.0F - Math.Max(0.0F, Math.Min(1.0F, weightVal / 100.0F))
        If applyBodyWeight AndAlso skinnyFrac > 0.0000001F Then AddNam9Channel(plan, triHead, "SkinnyMorph", skinnyFrac)

        ' 2e) Ley del motor: descartar los canales con peso fuera de [-1,1] ANTES del dedup. Acá conviven las
        ' dos clases: los del applier nativo (race base, NAM9, VampireMorph, keyword, NAMA, SkinnyMorph) van
        ' con EngineApplied=True y se descartan fuera de rango; los de RaceMenu (custom morphs + sculpt) van
        ' con EngineApplied=False y NUNCA se descartan — skee64 los aplica por su cuenta, sin validar.
        DropChannelsRejectedByEngine(plan)

        ' 3) Dedup channels by name SUMMING weights (same convention as the FO4 path — a slider and a
        ' type preset could both resolve to the same morph name; the engine applies the sum). The sculpt
        ' channel has a unique name so it survives dedup as a distinct additive layer.
        DedupSumChannelsByName(plan)
        Return plan
    End Function

    ''' <summary>The RaceMenu EXTENDED face-slider catalog (per-race .slider config), built once by the app from
    ''' the loaded plugins and read by the shared render/bake morph path to resolve a custom-morph SLIDER NAME to
    ''' its actual TRI morph(s). Nothing until the app populates it (e.g. FO4 sessions never set it) → the custom
    ''' morph loop falls back to applying the name directly.</summary>
    Public Shared Property SliderCatalog As FO4_Base_Library.RaceMenuSliderCatalog

    ''' <summary>Apply one RaceMenu extended custom morph (slider name → value) faithfully to the plan via the
    ''' catalog. See caller comment / skee64 ApplyMorphs:1229-1247.</summary>
    ''' <remarks>TODOS los canales que emite este método salen con <c>engineApplied:=False</c>: son de
    ''' RaceMenu (skee64), que los aplica con su propio TRIFile::Apply, NO con el applier del motor, y sin
    ''' validar el rango del peso. Por eso NO los toca <see cref="DropChannelsRejectedByEngine"/> — más aún,
    ''' skee64 descompone deliberadamente |v|&gt;1 para preservar la magnitud (FaceMorphInterface.cpp:1156-1163).</remarks>
    Private Shared Sub AddCustomMorphChannel(plan As MorphPlan, triHead As TriHeadFile, raceEditorId As String, isFemale As Boolean, sliderName As String, value As Single)
        Dim def As FO4_Base_Library.RaceMenuSliderCatalog.SliderDef = Nothing
        If SliderCatalog IsNot Nothing Then def = SliderCatalog.GetSlider(raceEditorId, isFemale, sliderName)
        If def Is Nothing Then
            AddNam9Channel(plan, triHead, sliderName, value, engineApplied:=False)   ' unknown slider / no catalog → best-effort direct
            Return
        End If
        Select Case def.Type
            Case FO4_Base_Library.RaceMenuSliderCatalog.SliderType.Preset
                Dim n = CInt(Math.Truncate(CDbl(value)))
                If n > 0 AndAlso Not String.IsNullOrEmpty(def.LowerBound) Then AddNam9Channel(plan, triHead, def.LowerBound & n.ToString(), 1.0F, engineApplied:=False)
            Case FO4_Base_Library.RaceMenuSliderCatalog.SliderType.Slider
                Dim morphName = If(value < 0, def.LowerBound, def.UpperBound)
                If Not String.IsNullOrEmpty(morphName) Then AddNam9Channel(plan, triHead, morphName, Math.Abs(value), engineApplied:=False)
            Case FO4_Base_Library.RaceMenuSliderCatalog.SliderType.HeadPart
                ' Head-part selection, not a morph — no plan channel.
        End Select
    End Sub

    ''' <param name="engineApplied">False SÓLO para canales de RaceMenu (skee64 los aplica con su propio
    ''' TRIFile::Apply, sin validar el rango). Ver <see cref="MorphChannel.EngineApplied"/>.</param>
    Private Shared Sub AddNam9Channel(plan As MorphPlan, triHead As TriHeadFile, morphName As String, weight As Single,
                                      Optional engineApplied As Boolean = True)
        If String.IsNullOrEmpty(morphName) Then Return
        Dim triMorph = triHead.GetMorph(morphName)
        If triMorph Is Nothing OrElse triMorph.Vertices Is Nothing OrElse triMorph.Vertices.Length = 0 Then Return
        Dim deltas = ConvertTriHeadMorphToMorphData(triMorph)
        If deltas.Count > 0 Then plan.Channels.Add(New MorphChannel(morphName, weight, deltas, engineApplied:=engineApplied))
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

    ''' <summary>DIAGNOSTICO de alcance (no altera el resultado): cuantos canales descarto esta ley y con que
    ''' pesos. Es lo que permite MEDIR la regla sin una corrida A/B: la rama solo se toma con peso fuera de
    ''' [-1,1], asi que si el contador da 0 sobre el corpus, la regla es demostrablemente inerte ahi.</summary>
    Public Shared DroppedOutOfRangeChannels As Long = 0
    Public Shared ReadOnly DroppedWeightSamples As New List(Of String)

    Private Shared Sub DropChannelsRejectedByEngine(plan As MorphPlan)
        If plan Is Nothing OrElse plan.Channels.Count = 0 Then Return
        Dim rejected = Function(ch As MorphChannel) ch IsNot Nothing AndAlso ch.EngineApplied AndAlso Not ch.IsZap AndAlso
                                             Not (ch.Weight >= -1.0F AndAlso ch.Weight <= 1.0F)
        For Each ch In plan.Channels
            If rejected(ch) Then
                Threading.Interlocked.Increment(DroppedOutOfRangeChannels)
                SyncLock DroppedWeightSamples
                    If DroppedWeightSamples.Count < 40 Then DroppedWeightSamples.Add($"{ch.Name}={ch.Weight:R}")
                End SyncLock
            End If
        Next
        plan.Channels.RemoveAll(rejected)
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
    ''' <remarks>Friend (no Private) para que SseMorphReverseEngineer construya columnas de base con
    ''' EXACTAMENTE el mismo filtro (|v|² &gt; 1e-6) que aplica el bake antes del gate. Reimplementarlo allá
    ''' haría que la selección de vértices de la base no coincidiera con la del pipeline real.</remarks>
    Friend Shared Function ConvertTriHeadMorphToMorphData(morph As TriHeadMorph) As List(Of MorphData)
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

    ''' <summary>Carga los datos de TRI de una shape, resolviendo hasta DOS archivos: el de raza/expresion
    ''' (morphs de animacion o morphs PIRT de cuerpo) y el de chargen (sculpting). Los dos se mergean en un solo
    ''' TriHead para que el loop de aplicacion vea todos los morphs.
    ''' <para>â›” SYNC: RENDER == BAKE, lado RENDER de la resolucion de <c>.tri</c>. Su gemelo es
    ''' <c>FaceGenBuildPipeline.LoadMergedHeadTri</c>: los dos resuelven RECORD-DRIVEN por HDPT NAM0/NAM1 (0 =
    ''' race morph, 1 = mesh/SkinnyMorph, 2 = chargen) y, solo para el slot de raza, por BODYTRI.</para>
    ''' <para>â›” NO agregar un fallback por convencion de nombre del mesh: el MOTOR no tiene ninguno, arma el path
    ''' en un solo sitio y lo unico que hace es NORMALIZAR el que el record ya declara. Adivinarlo hacia que el
    ''' render aplicara morphs que el CK nunca aplica. Ver 40-bake-reglas-comunes.</para></summary>
    Private Sub LoadTriForShape(shape As IRenderableShape, ByRef tri As TriFile, ByRef triHead As TriHeadFile)
        tri = Nothing
        triHead = Nothing

        ' This shape's own vertex count — the per-shape guard for the SSE High Poly Head .tri redirect
        ' (ResolveHphHeadPartTriPath accepts an HPH candidate only when its count == this). Covers head AND brows
        ' (and any HPH part). 0 for a shape with no geometry (redirect stays off).
        Dim shapeVerts As Integer = If(shape IsNot Nothing AndAlso shape.Geometry IsNot Nothing, shape.Geometry.VertexCount, 0)

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

        ' No step 3: a slot the record leaves empty STAYS empty (the engine has no mesh-name convention, and inventing
        ' one made the render apply morphs the CK/engine never apply). The opt-in SSE HPH redirect below only REDIRECTS
        ' a declared-but-broken path (e.g. head race/chargen pointing at the vanilla 996-vert tri); it does NOT fill an
        ' empty slot — an eyebrow HDPT ships only NAM0=1, and forcing HPH brow race/chargen onto it over-morphs the
        ' brow into a blob (the CK baked no such morph). The brows therefore render as the mod's record actually
        ' specifies; a record that omits their morph simply can't be fully reconstructed (that is the mod's limitation).

        ' Load race TRI: resolve HPH-aware (fills/redirects for a known HPH part; else returns the record path
        ' unchanged), then try PIRT first (BodySlide body format), then TriHead (Bethesda head format).
        Dim resolvedRace = ResolveHphHeadPartTriPath(raceMorphPath, shapeVerts, HphTriSlot.Race, AddressOf TriHeadVertsOf)
        If Not String.IsNullOrEmpty(resolvedRace) Then
            Dim normRace = MeshPathHelpers.NormalizeMeshKey(resolvedRace)
            tri = TryLoadPirt(normRace)
            If tri Is Nothing Then
                triHead = TryLoadTriHead(normRace)
            End If
        End If

        ' Load chargen TRI (always TriHead format), HPH-aware, and merge into triHead
        Dim resolvedChargen = ResolveHphHeadPartTriPath(chargenPath, shapeVerts, HphTriSlot.Chargen, AddressOf TriHeadVertsOf)
        If Not String.IsNullOrEmpty(resolvedChargen) Then
            Dim normChargen = MeshPathHelpers.NormalizeMeshKey(resolvedChargen)
            Dim chargenHead = TryLoadTriHead(normChargen)
            MergeChargenIntoRaceTriHead(triHead, chargenHead)

            ' RaceMenu EXTENDED morphs. A .slider bound — and therefore a .jslot custom morph — is a morph NAME
            ' whose geometry lives in a SEPARATE .tri, not in the chargen tri: morphs.ini maps this shape's chargen
            ' tri to a list of extended tris, and skee64 applies the named morph out of each one
            ' (MorphVisitor::Accept, SKEEHooks.cpp:687-696, via GetExtendedModelTri). Merging them here is the same
            ' composition, so every downstream consumer — NAM9/NAMA channels, custom-morph channels, render AND
            ' bake — resolves the name with no special case. Chargen/race morphs merged above win a name collision.
            ' Without this, every extended slider silently moved nothing.
            ' Keyed on the ORIGINAL record chargenPath (RaceMenu morphs.ini references the record's chargen tri, not
            ' an HPH-redirected one); empty when the slot was HPH-filled (brows) → no extended morphs, as expected.
            Dim catalog = SliderCatalog
            If catalog IsNot Nothing AndAlso Not String.IsNullOrEmpty(chargenPath) Then
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
            ' NAM0=1 mesh tri (SkinnyMorph source), HPH-aware. A hair shape's mesh tri has a non-head vertex count so
            ' the redirect skips it (candidate must count-match); only a known HPH head/brow shape can redirect here.
            Dim resolvedMesh = ResolveHphHeadPartTriPath(meshTriPath, shapeVerts, HphTriSlot.Mesh, AddressOf TriHeadVertsOf)
            If Not String.IsNullOrEmpty(resolvedMesh) Then
                Dim meshTriHead = TryLoadTriHead(MeshPathHelpers.NormalizeMeshKey(resolvedMesh))
                MergeChargenIntoRaceTriHead(triHead, meshTriHead)
            End If

            ' [SSE-TRI] Per-shape trace of the weight-morph inputs: which .tri each SSE head-part shape
            ' resolved (HDPT NAM0=0/1/2 — the record is the only source), whether the merged TriHead ended up
            ' carrying "SkinnyMorph", and the frac the plan will apply (1 - NAM7/100). A hair shape logging
            ' hasSkinnyMorph=False or skinnyFrac=0 renders un-weighted BY DESIGN when its HDPT declares no
            ' NAM0=1 — that is what the CK bakes too; the head-part occlusion is NOT involved in that.
            If Logger.Enabled Then
                Dim shName = If(shape.ShapeName, "?")
                Dim meshKeyD As String = Nothing
                If _meshDictKeys IsNot Nothing Then _meshDictKeys.TryGetValue(shape, meshKeyD)
                meshKeyD = If(meshKeyD, "")
                Dim raceD = If(raceMorphPath, "")
                Dim chargenD = If(chargenPath, "")
                Dim meshTriD = If(meshTriPath, "")
                Dim triHeadD = triHead
                Dim vertsD = If(triHeadD Is Nothing, 0UI, triHeadD.NumVertices)
                Dim morphsD = If(triHeadD Is Nothing, 0, triHeadD.Morphs.Count)
                Dim hasSkinnyD = triHeadD IsNot Nothing AndAlso triHeadD.GetMorph("SkinnyMorph") IsNot Nothing
                Dim weightD As Single = If(_npcData.Record.TienePesoDeSkyrim(), _npcData.Record.PesoDeSkyrim(), 100.0F)
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

    ''' <summary><c>Friend Shared</c> (no Private) para que el catálogo de tipos NAMA del editor
    ''' (<see cref="SseChargenTypeCatalog"/>) lea los mismos .tri por el MISMO caché, en vez de abrir un
    ''' segundo lector con su propia normalización y su propio parseo — que es como nació el
    ''' <c>_faceTriMorphNamesCache</c> de MainForm. No toca estado de instancia: <c>_triHeadCache</c>,
    ''' <c>TryGetFileBytes</c> y <c>ChargenMouthFix</c> ya son Shared.</summary>
    Friend Shared Function TryLoadTriHead(normalizedPath As String) As TriHeadFile
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

                ' SYNC: RENDER == BAKE — gemelo en FaceGenBuildPipeline (parse del tri del bake). El fix
                ' se aplica sobre el parse FRESCO y antes de cachear, para que los dos vean los mismos deltas.
                ChargenMouthFix.MaybeApplyInPlace(normalizedPath, head)
                Return head
            End Function)
    End Function

    ''' <summary>Is the SSE High Poly Head .tri redirect turned on? Gate = active game is Skyrim AND the opt-in
    ''' Config toggle. Default-off ⇒ zero effect unless enabled in CharGen Options → Fixes. (The per-shape exactness
    ''' is enforced in <see cref="ResolveHphHeadPartTriPath"/> by requiring the candidate's vertex count to equal the
    ''' shape's — a stronger, per-shape guard than a hardcoded count set.)</summary>
    Private Shared Function HphRedirectGateOn() As Boolean
        Dim c = Config_App.Current
        Return c IsNot Nothing AndAlso c.Game = Config_App.Game_Enum.Skyrim AndAlso c.Setting_SseResolveHighPolyHeadTri
    End Function

    ''' <summary>The MEASURED HPH tri path for a known head-part vertex count + slot, or Nothing if the count is not a
    ''' known HPH face part (or the slot doesn't exist, e.g. Male/Chargen). Used to FILL an empty record slot (brows
    ''' HDPTs ship only NAM0=1, no race/chargen) and to redirect a wrong-topology one, keyed by the shape's own
    ''' geometry — no basename guess. Counts/filenames measured from HPH v1.4 (SE).</summary>
    Private Shared Function HphTablePath(shapeVerts As Integer, slot As HphTriSlot) As String
        Select Case shapeVerts
            Case HphFemaleHeadVerts
                Return HphDir & (If(slot = HphTriSlot.Race, "femaleheadraces.tri", If(slot = HphTriSlot.Chargen, "femaleheadchargen.tri", "femalehead.tri")))
            Case HphMaleHeadVerts
                ' HPH ships no male head chargen (malehead uses maleheadcustomizations) → Chargen = Nothing.
                Return If(slot = HphTriSlot.Chargen, Nothing, HphDir & (If(slot = HphTriSlot.Race, "maleheadraces.tri", "malehead.tri")))
            Case HphFemaleBrowVerts
                Return HphDir & "faceparts\" & (If(slot = HphTriSlot.Race, "femaleheadbrowsrace.tri", If(slot = HphTriSlot.Chargen, "femaleheadbrowschargen.tri", "femaleheadbrows.tri")))
            Case HphMaleBrowVerts
                Return HphDir & "faceparts\" & (If(slot = HphTriSlot.Race, "maleheadbrowsraces.tri", If(slot = HphTriSlot.Chargen, "maleheadbrowschargen.tri", "maleheadbrows.tri")))
        End Select
        Return Nothing
    End Function

    ''' <summary>Resuelve el path de tri que se va a usar de verdad para el <paramref name="slot"/> de una shape
    ''' de head part, con soporte de High Poly Head. COMPARTIDA para que el render en vivo y el bake offline
    ''' resuelvan IDENTICO.
    ''' <para>Devuelve <paramref name="recordPath"/> sin tocar salvo que el toggle opt-in de SSE este prendido Y
    ''' el record DECLARE un path que falta o tiene topologia equivocada para una shape que matchea una head part
    ''' HPH conocida. Un slot VACIO se queda vacio: se redirige, nunca se rellena, porque llenar un slot que el CK
    ''' nunca horneo sobre-morphea.</para>
    ''' <para>Orden: (1) si el path del record carga con la misma cantidad de vertices que la shape, se conserva;
    ''' (2) la entrada MEDIDA de la tabla HPH para (verts, slot); (3) el mismo basename bajo los directorios de
    ''' HPH, que cubre partes HPH fuera de la tabla. El candidato se acepta SOLO si su cantidad de vertices iguala
    ''' la de la shape, que es el guard exacto por shape.</para></summary>
    Public Shared Function ResolveHphHeadPartTriPath(recordPath As String, shapeVerts As Integer, slot As HphTriSlot, vertsOf As Func(Of String, Integer)) As String
        If Not HphRedirectGateOn() OrElse shapeVerts <= 0 OrElse vertsOf Is Nothing Then Return recordPath
        ' A slot the record leaves EMPTY stays empty — we only REDIRECT a declared-but-broken path, never FILL one
        ' the record (and therefore the CK bake) never declared. Filling e.g. an eyebrow HDPT's absent race/chargen
        ' slot (brows ship only NAM0=1) applies a racial/chargen morph the CK never baked ⇒ the brow mesh over-morphs
        ' into a blob. Same lesson as the removed mesh-name guess: don't invent morphs the engine doesn't apply.
        If String.IsNullOrEmpty(recordPath) Then Return recordPath
        ' 1) record already compatible
        If vertsOf(recordPath) = shapeVerts Then Return recordPath
        ' 2) measured HPH table (also fills an empty record slot)
        Dim tablePath = HphTablePath(shapeVerts, slot)
        If Not String.IsNullOrEmpty(tablePath) AndAlso vertsOf(tablePath) = shapeVerts Then Return HphLog(recordPath, tablePath, shapeVerts, slot)
        ' 3) basename reuse for HPH parts not in the table
        If Not String.IsNullOrEmpty(recordPath) Then
            Dim bn = IO.Path.GetFileName(recordPath)
            If Not String.IsNullOrEmpty(bn) Then
                For Each d In HphBasenameDirs
                    Dim p = d & bn
                    If vertsOf(p) = shapeVerts Then Return HphLog(recordPath, p, shapeVerts, slot)
                Next
            End If
        End If
        Return recordPath
    End Function

    Private Shared Function HphLog(fromPath As String, toPath As String, shapeVerts As Integer, slot As HphTriSlot) As String
        If Logger.Enabled Then Logger.LogLazy(Function() $"[HPH-TRI] {slot} redirect '{If(fromPath, "<empty>")}' -> '{toPath}' (shape verts={shapeVerts})")
        Return toPath
    End Function

    ''' <summary>Render-side <c>vertsOf</c> for <see cref="ResolveHphHeadPartTriPath"/>: vertex count of the TriHead
    ''' at a raw path via the shared per-path cache, or -1 if it can't load / parse.</summary>
    Private Function TriHeadVertsOf(rawPath As String) As Integer
        If String.IsNullOrEmpty(rawPath) Then Return -1
        Dim h = TryLoadTriHead(MeshPathHelpers.NormalizeMeshKey(rawPath))
        Return If(h Is Nothing, -1, CInt(h.NumVertices))
    End Function

    ' Routed through MeshPathHelpers.TryLoadMeshBytes (minBytes:=8 preserves the TRI-magic guard)
    ' so the TryGetValue + GetBytes + size-check lives in one place (DUP-004).
    Private Shared Function TryGetFileBytes(normalizedPath As String) As Byte()
        Return MeshPathHelpers.TryLoadMeshBytes(normalizedPath, minBytes:=8)
    End Function

End Class
