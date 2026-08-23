Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Mathematics

''' <summary>Orquestador del bake offline de FaceGen. Compone los MISMOS bloques que usa el render en runtime
''' (NpcRecordOverlay, FaceSkeletonResolver, FaceBonePoseBuilder, MorphEngine, SkinBakeMath, las regiones
''' faciales de la raza) sin necesitar contexto GL ni NpcRenderHost. Su salida son los "world vertices"
''' (v_world) por shape: las posiciones que un vertice ocuparia si el render lo hubiera producido desde el mesh
''' <c>_facebones</c>.
''' <para>Matematica del bake por shape: cargar el mesh <c>_facebones</c>; aplicar los morphs de vertice del TRI
''' de chargen en espacio NIF-local; cargar skeletons frescos de cuerpo y cara y armar la pose FMRS desde los
''' FaceMorphs del NPC mas el JSON de regiones de la raza; skinnear cada vertice con los huesos ya posados.</para>
''' <para>Con el v_world en mano, el caller (FaceGenBuilder) camina la jerarquia de huesos de cara para
''' redistribuir pesos a los ancestros de la paleta ORIG y calcula <c>v_baked = inv(Mtot_orig) x v_world</c> para
''' escribirlo al .nif con particion de skin solo de cuerpo.</para></summary>
Public Module FaceGenBuildPipeline

    ''' <summary>Per-shape result of <see cref="ComputeWorldVerticesForShape"/>.</summary>
    Public Class WorldVertResult
        ''' <summary>v_world per vertex in render-pipeline coords (post-FMRS, post-chargen-morph).</summary>
        Public Property WorldVertices As Vector3d()
        ''' <summary>Skin instance with FMRS pose applied — the same one used to compute v_world.
        ''' Caller uses this for parent-walk + ancestor lookup when redistributing weights to ORIG palette.</summary>
        Public Property FaceSkel As SkeletonInstance
        ''' <summary>Body skel (no pose) — fallback for bones not in face skel (HEAD, Neck_skin, ...).</summary>
        Public Property BodySkel As SkeletonInstance
        ''' <summary>Body skel con SÓLO la pose de body-weight (sin FMRS), o Nothing si esta shape no
        ''' lleva body-weight. Es el bind del lado DESTINO: el CK aplica el mismo array de escalas en
        ''' las dos pasadas, así que el inverso tiene que usar la paleta escalada.</summary>
        Public Property BwBindSkel As SkeletonInstance
    End Class

    ''' <summary>Compute v_world for a shape from its `_facebones` source NIF, with the
    ''' overlay-applied NPC's FMRS pose + chargen morphs applied. Mutates
    ''' <paramref name="facebonesShape"/>'s vertex array in place when chargen morphs apply
    ''' (this is fine because the shape is a freshly-loaded clone from disk, owned by this
    ''' pipeline run). Returns Nothing if any required input is missing.</summary>
    Public Function ComputeWorldVerticesForShape(state As BakeState,
                                                  facebonesNif As Nifcontent_Class_Manolo,
                                                  facebonesShape As INiShape,
                                                  chargenTriPath As String,
                                                  Optional raceMorphTriPath As String = Nothing,
                                                  Optional srcNif As Nifcontent_Class_Manolo = Nothing) As WorldVertResult
        If state Is Nothing OrElse facebonesShape Is Nothing OrElse facebonesNif Is Nothing Then Return Nothing

        ' 1) Apply chargen TRI morphs to the facebones shape's vertex array (in place; the
        ' caller passes a freshly-loaded NIF that nobody else reads).
        ApplyChargenMorphsInPlace(facebonesNif, facebonesShape, chargenTriPath, raceMorphTriPath, state)

        ' 2) Build face + body skel. FMRS va a la capa MorphDeltaTransform (igual que el render en
        ' vivo); el bake lee GetGlobalTransform, que compone todas las capas.
        ' Al bodySkel se le aplica la MISMA pose que al faceSkel: ApplyBoneMorphPose sólo escribe
        ' entradas cuya key existe en el diccionario destino, así que en bodySkel esto fija el morph
        ' del hueso literal "Neck" (las entradas "skin_*" son no-op) — igual que el render.
        Dim faceSkel = LoadFaceSkeleton(state)
        Dim bodySkel = LoadBodySkeleton(state)

        ' 2b) Body-weight (MWGT + MRSV) — el CK SÍ lo hornea, en las shapes que NO traen
        ' CustomizationRemapNewBonesData. Ver 40-bake-leyes-fo4:
        ' el exportador vuelve a llamar al constructor del array per-hueso del skin instance
        ' (CreationKit.exe 0x140A8CFD0) con los mapas en NULL para las shapes que sí la traen, y
        ' eso deja sus escalas en identidad. Medido sobre 16.438 shapes del corpus: los que pasan
        ' de 0,05 contra la referencia del CK bajan de 324 a 3.
        Dim bwPose As Poses_class = Nothing
        If bodySkel IsNot Nothing AndAlso bodySkel.HasSkeleton AndAlso
           Not ShapeHasCustomizationNewBones(facebonesNif, facebonesShape) Then
            bwPose = BuildBakeBodyWeightPose(state, bodySkel)
        End If

        ' El source lleva FMRS + body-weight mergeados: ApplyBoneMorphPose REEMPLAZA toda la capa
        ' morph, así que aplicarlas por separado perdería la primera.
        Dim srcPose As Poses_class = state.FmrsPose
        If bwPose IsNot Nothing Then
            srcPose = If(srcPose Is Nothing, bwPose, PoseMath.MergePoses(srcPose, bwPose))
        End If
        If srcPose IsNot Nothing Then
            If faceSkel IsNot Nothing AndAlso faceSkel.HasSkeleton Then faceSkel.ApplyBoneMorphPose(srcPose)
            If bodySkel IsNot Nothing AndAlso bodySkel.HasSkeleton Then bodySkel.ApplyBoneMorphPose(srcPose)
        End If

        ' 2c) Huesos de cloth (pelo con BSClothExtraData) — inyectados ANTES de skinear, no después.
        ' El inverso ya los inyectaba (ver BakeShape), pero el forward corría PRIMERO y resolvía esos
        ' huesos por el fallback crudo del NIF (BuildPoseResolver nivel 3) ⇒ las dos mitades del bake
        ' usaban un bind DISTINTO para el mismo hueso. MEDIDO con Tools/BakeAsymmetryProbe sobre la Data
        ' de FO4: de 271 NIFs `_faceBones` de head parts, 14 traen cloth y 3 referencian huesos ausentes
        ' de skeleton.nif ∪ skeleton_faceBones.nif — FemaleHair05/FemaleHair30 (Ponytail_C_Cloth01..04)
        ' y FemaleHair32 (SideTail_BN_A/B_001..004).
        ' El bind sale de srcNif (el NIF PLANO), la MISMA fuente que usa el inverso, así que el lado
        ' destino queda bit-idéntico y sólo cambia el forward. La lista de huesos a inyectar la manda la
        ' shape que se está skineando acá (la `_faceBones`), no la plana.
        If srcNif IsNot Nothing AndAlso bodySkel IsNot Nothing AndAlso bodySkel.HasSkeleton Then
            Try
                Dim clothSkel = SkeletonClothOverlayHelper_Class.ParseClothSkeleton(srcNif)
                If clothSkel IsNot Nothing Then
                    Dim fbnsWrap As New NifRenderableShape(facebonesNif, facebonesShape, 0)
                    SkeletonClothOverlayHelper_Class.InjectMissingBonesIntoLiveSkeleton(fbnsWrap, bodySkel, clothSkel)
                End If
            Catch ex As Exception
            End Try
        End If

        ' 3) Esqueleto para el lado DESTINO (el inverso del bake). Lleva SÓLO el body-weight, sin
        ' FMRS: el CK aplica el mismo array de escalas en las dos pasadas (PASS 1 = rig _faceBones,
        ' PASS 2 = rig plano), así que la escala tiene que estar en las DOS paletas.
        ' Medido: meterla en un solo lado es PEOR que no meterla (Wfat max 0,5352 → 0,8153 sólo
        ' en source, contra 0,0039 en ambas). BuildBindResolver usa OriginalGetGlobalTransform, que
        ' por diseño excluye la capa morph, de ahí este segundo esqueleto.
        Dim bwBindSkel As SkeletonInstance = Nothing
        If bwPose IsNot Nothing Then
            bwBindSkel = LoadBodySkeleton(state)
            If bwBindSkel IsNot Nothing AndAlso bwBindSkel.HasSkeleton Then
                bwBindSkel.ApplyBoneMorphPose(bwPose)
            Else
                bwBindSkel = Nothing
            End If
        End If

        ' ⛔ EL BAKE NO LLEVA FÍSICA. `GetGlobalTransform` compone también la capa
        ' `PhysicsDeltaTransform`, así que un esqueleto que ya hubiera simulado en el previewer
        ' metería el último frame de tela en un bake que TIENE que ser determinista y byte-idéntico
        ' (regla RENDER == BAKE: el render puede tener física, el bake no puede depender del reloj).
        ' Limpiar acá es barato y hace que la garantía no dependa de en qué orden usó la app el
        ' esqueleto. Si algún día el bake DEBE llevar física, es una decisión del usuario, no un
        ' efecto colateral de haber dejado la capa puesta.
        faceSkel?.ResetPhysics()
        bodySkel?.ResetPhysics()
        bwBindSkel?.ResetPhysics()

        ' 4) Skin shape with poseT resolved from faceSkel ∪ bodySkel ∪ shape-internal fallback.
        Dim resolver = BuildPoseResolver(faceSkel, bodySkel, facebonesNif)
        Dim vWorld = SkinBakeMath.SkinShapeWorldVerticesWithPose(facebonesShape, facebonesNif, resolver)

        Return New WorldVertResult With {
            .WorldVertices = vWorld,
            .FaceSkel = faceSkel,
            .BodySkel = bodySkel,
            .BwBindSkel = bwBindSkel
        }
    End Function

    ''' <summary>True si el shape del `_faceBones.nif` trae `CustomizationRemapNewBonesData`. Esas
    ''' shapes (cara, neck gore, barbas, MaleHeadRear…) necesitan que el CK les inyecte un hueso que
    ''' su rig no declara — típicamente "Neck" — y esa inyección reconstruye el array de escalas
    ''' per-hueso del skin instance desde cero, con lo que el body-weight se pierde. NO es un filtro
    ''' deliberado: es efecto colateral de la inyección. Medido: separación 13/13 en los shapes con
    ''' señal, incluida la anomalía Male/FemaleHeadRear (mismo tipo de HDPT, comportamiento opuesto).</summary>
    Private Function ShapeHasCustomizationNewBones(nif As Nifcontent_Class_Manolo, shape As INiShape) As Boolean
        If nif Is Nothing OrElse shape Is Nothing Then Return False
        Dim av = TryCast(shape, NiAVObject)
        If av Is Nothing OrElse av.ExtraDataList Is Nothing Then Return False
        For Each ref As NiRef In av.ExtraDataList.References
            Dim ed = TryCast(nif.Blocks(ref.Index), NiBinaryExtraData)
            If ed Is Nothing Then Continue For
            If String.Equals(ed.Name?.String, "CustomizationRemapNewBonesData", StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

    ''' <summary>Pose de escalas por hueso del body-weight para el bake (MWGT capa 1 + MRSV capa 3). Reusa
    ''' <see cref="PoseMath.BuildBodyWeightPose"/>, la misma implementacion que el render, con la formula del
    ''' motor. Dos diferencias deliberadas contra el render, las dos MEDIDAS:
    ''' <list type="bullet">
    ''' <item>Sin ARMA sculpt: el FaceGeom se hornea una vez por NPC y no puede depender del outfit. De 1.093
    ''' ARMA vanilla solo 5 tienen delta en un hueso con peso real en el pelo.</item>
    ''' <item>Un slot MWGT centinela ("Default") implica SIN body-weight, en vez de la sustitucion que hace el
    ''' render. Medido sobre los 12 NPCs vanilla con slots centinela: sustituir da 24 regresiones, tratarlos como
    ''' 0 da 2, e ignorarlos deja 1 de +0,0001.</item>
    ''' </list>
    ''' NNAM sigue fuera del bake.</summary>
    Private Function BuildBakeBodyWeightPose(state As BakeState, skeleton As SkeletonInstance) As Poses_class
        If state?.NpcData Is Nothing OrElse state.Race Is Nothing Then Return Nothing

        ' Slot centinela ⇒ el CK no aplica body-weight a esta cabeza.
        Dim wt = state.NpcData.Record.PesoDelCuerpo(0)
        Dim wm = state.NpcData.Record.PesoDelCuerpo(1)
        Dim wf = state.NpcData.Record.PesoDelCuerpo(2)
        If Not wt.HasValue OrElse Not wm.HasValue OrElse Not wf.HasValue Then Return Nothing

        ' Mismo gate que el render (NpcMorphPoseResolver.ResolveBodyWeightData): sin MWGT efectivo
        ' no se emite ninguna escala. Sin él la fórmula daría escala NEGATIVA en (0,0,0).
        If (wt.Value + wm.Value + wf.Value) < 0.001F Then Return Nothing

        Dim targetGender As UInteger = If(state.IsFemale, 1UI, 0UI)
        Dim genderBlock As Canon.RaceFO4_BoneScaleData = Nothing
        ' Bone Data es exclusivo de Fallout 4 — Skyrim no lo declara en RACE. Sin raza de Fallout 4
        ' no hay escala por peso que emitir, que es lo mismo que decia el lector viejo devolviendo
        ' una lista vacia.
        Dim razaFo4 = TryCast(state.Race, Canon.RaceFO4)
        If razaFo4 Is Nothing Then Return Nothing
        For Each bd In razaFo4.BoneScaleData
            If bd.BoneWeightScaleDataWeightScaleTargetGender = targetGender Then genderBlock = bd : Exit For
        Next
        If genderBlock Is Nothing Then Return Nothing

        Return PoseMath.BuildBodyWeightPose(wt.Value, wm.Value, wf.Value,
                                            genderBlock, state.NpcData.Record.ValoresDeRegionCorporal(),
                                            Nothing, skeleton, True)
    End Function

    ''' <summary>Per-NPC bake context. Built once at the start of a BuildCharGen run and
    ''' reused for every shape in the HDPT chain.</summary>
    Public Class BakeState
        Public Property NpcFormID As UInteger
        Public Property RaceFormID As UInteger
        Public Property IsFemale As Boolean
        ''' <summary>NPC_Data with overlay applied (LooksMenu preset folded in).</summary>
        Public Property NpcData As NPC_Data
        Public Property Race As Canon.IRace
        Public Property RaceMorphValueDefs As IReadOnlyList(Of Canon.RaceFO4_MorphValues)
        Public Property RaceMorphPresetDefs As List(Of RACE_MorphPresetDef)
        Public Property FmrsPose As Poses_class
        Public Property PluginManager As PluginManager
        ''' <summary>Cached chargen TRI parses, keyed by normalized mesh path. The same .tri
        ''' is referenced by multiple HDPTs in some cases (rare for face).</summary>
        Public Property TriHeadCache As New Dictionary(Of String, TriHeadFile)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>Bytes del esqueleto de CARA, resueltos una vez por NPC (ver <c>LoadFaceSkeleton</c>).</summary>
        Public Property FaceSkeletonBytes As Byte() = Nothing
        ''' <summary>Key normalizada del esqueleto de CUERPO, resuelta una vez por NPC (ver <c>LoadBodySkeleton</c>).
        ''' "" = todavía sin resolver; "-" = la raza no declara ninguno (negativo cacheado).</summary>
        Public Property BodySkeletonKey As String = ""
    End Class

    ''' <summary>Build a BakeState for one NPC. Loads NPC, applies LooksMenu overlay, parses
    ''' RACE, resolves face regions JSON, builds FMRS pose. Returns Nothing if NPC or RACE
    ''' resolution fails.</summary>
    Public Function BuildBakeState(npcFormID As UInteger,
                                    pluginManager As PluginManager,
                                    appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                    facialBoneRegions As FacialBoneRegionsFile) As BakeState
        Dim npcData = NpcRecordOverlay.ResolveOverlaidNpcData(npcFormID, pluginManager, appliedPresets)
        If npcData Is Nothing Then Return Nothing

        Dim raceRec = pluginManager.GetRecord(npcData.Record.Race)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = Canon.CanonRecords.Race(raceRec, pluginManager)
        ' MorphValues/MorphPresets son exclusivos de Fallout 4 — Skyrim no los declara en RACE. Con
        ' otra raza la lista queda VACIA, no nula: el modelo anterior la declaraba ya construida, asi
        ' que un record sin esos subrecords daba una lista sin elementos.
        Dim raceFo4 = TryCast(race, Canon.RaceFO4)
        Dim valoresDeMorfo As IReadOnlyList(Of Canon.RaceFO4_MorphValues) =
            If(raceFo4 Is Nothing, New List(Of Canon.RaceFO4_MorphValues)(), raceFo4.MorphValues)

        Dim fmrsPose As Poses_class = Nothing
        If facialBoneRegions IsNot Nothing Then
            ' NNAM ("Neck Fat Adjustments Scale") is deliberately NOT included in the bake. It is a
            ' CK RUNTIME scale of the shared "Neck" bone (head+body) applied to the live skeleton —
            ' empirically validated 2026-06-17 that CK does NOT bake it into FaceGeom. The render
            ' path applies it via BuildBodyWeightPose (Layer 2); the bake must stay NNAM-free.
            fmrsPose = FaceBonePoseBuilder.BuildFaceBoneTransforms(npcData, facialBoneRegions)
        End If

        Return New BakeState With {
            .NpcFormID = npcFormID,
            .RaceFormID = npcData.Record.Race,
            .IsFemale = npcData.Record.ConfigurationFlagsFemale,
            .NpcData = npcData,
            .Race = race,
            .RaceMorphValueDefs = valoresDeMorfo,
            .RaceMorphPresetDefs = raceFo4.ReadMorphPresetsFlat(npcData.Record.ConfigurationFlagsFemale),
            .FmrsPose = fmrsPose,
            .PluginManager = pluginManager
        }
    End Function

    ''' <summary>Build a bind-only resolver (NO FMRS) over body skel ∪ face skel ∪ shape NIF
    ''' fallback. This is what CK / the runtime uses when it skins a baked face NIF: every bone
    ''' resolves to its canonical bind transform. Reused by <see cref="BakeShape"/> for the
    ''' inverse step and by the post-write render-vs-baked comparison harness.</summary>
    Public Function BuildBindResolver(faceSkel As SkeletonInstance,
                                       bodySkel As SkeletonInstance,
                                       shapeNif As Nifcontent_Class_Manolo,
                                       Optional bwBindSkel As SkeletonInstance = Nothing) As Func(Of NiNode, Transform_Class)
        Return Function(boneNode As NiNode) As Transform_Class
                   If boneNode Is Nothing Then Return Nothing
                   Dim bn = If(boneNode.Name?.String, "")
                   If bn = "" Then Return Nothing
                   ' Bind + body-weight: se consulta PRIMERO y con GetGlobalTransform (no Original),
                   ' porque este esqueleto lleva la escala en la capa morph y Original la excluye.
                   ' Sólo tiene la pose de body-weight, así que GetGlobalTransform = bind ∘ escala.
                   If bwBindSkel IsNot Nothing AndAlso bwBindSkel.HasSkeleton Then
                       Dim hbw As HierarchiBone_class = Nothing
                       If bwBindSkel.SkeletonDictionary.TryGetValue(bn, hbw) AndAlso hbw IsNot Nothing Then
                           Return hbw.GetGlobalTransform
                       End If
                   End If
                   If bodySkel IsNot Nothing AndAlso bodySkel.HasSkeleton Then
                       Dim hb As HierarchiBone_class = Nothing
                       If bodySkel.SkeletonDictionary.TryGetValue(bn, hb) AndAlso hb IsNot Nothing Then
                           Return hb.OriginalGetGlobalTransform
                       End If
                   End If
                   If faceSkel IsNot Nothing AndAlso faceSkel.HasSkeleton Then
                       Dim hb As HierarchiBone_class = Nothing
                       If faceSkel.SkeletonDictionary.TryGetValue(bn, hb) AndAlso hb IsNot Nothing Then
                           Return hb.OriginalGetGlobalTransform
                       End If
                   End If
                   Return Transform_Class.GetGlobalTransform(boneNode, shapeNif)
               End Function
    End Function

    ''' <summary>Full per-shape bake: compute v_world from the FBNS source (FMRS-applied) and
    ''' write v_baked = inv(Mtot_orig) × v_world into the cloned ORIG shape so that, when later
    ''' skinned by the runtime/CK with body-only bones, each vertex lands at the SAME world
    ''' position the renderer's FBNS skin path would produce. This is the core of iter-3.
    '''
    ''' Preconditions: the cloned ORIG shape lives in <paramref name="destNif"/>; the FBNS NIF
    ''' is loaded fresh (caller-owned) and its shape has the same VertexCount and ordering as
    ''' the cloned ORIG (verified empirically by the THREEWAY harness for face HDPTs).
    '''
    Public Function BakeShape(state As BakeState,
                               destNif As Nifcontent_Class_Manolo,
                               clonedOrigShape As INiShape,
                               facebonesNif As Nifcontent_Class_Manolo,
                               facebonesShape As INiShape,
                               chargenTriPath As String,
                               Optional srcNif As Nifcontent_Class_Manolo = Nothing,
                               Optional srcShape As INiShape = Nothing,
                               Optional raceMorphTriPath As String = Nothing) As Boolean
        Dim baked = ComputeBakedVertices(state, destNif, clonedOrigShape, facebonesNif, facebonesShape,
                                          chargenTriPath, srcNif, srcShape, raceMorphTriPath)
        If baked Is Nothing Then Return False

        Dim geomOut = ShapeGeometryFactory.[For](clonedOrigShape, destNif)
        geomOut.SetVertexPositions(baked)
        Try : geomOut.UpdateBounds() : Catch : End Try
        Return True
    End Function

    ''' <summary>Devuelve las posiciones horneadas SIN escribirlas en ninguna shape. Es el cuerpo real
    ''' del bake; <see cref="BakeShape"/> es esto + <c>SetVertexPositions</c>.
    ''' <para>Existe separado porque el PREVIEW también las necesita: <see cref="HeadBakeService"/> las
    ''' entrega como geometría base del shape plano (<c>IBaseGeometryProvider</c>) en vez de escribirlas
    ''' al NIF. <b>UNA sola implementación</b> para bake y render — si divergen, el preview deja de ser
    ''' WYSIWYG, que es justo el bug que este servicio existe para cerrar.</para>
    ''' <para>Devuelve <c>Nothing</c> si falta cualquier insumo o el VertexCount no aparea.</para></summary>
    Public Function ComputeBakedVertices(state As BakeState,
                                          destNif As Nifcontent_Class_Manolo,
                                          clonedOrigShape As INiShape,
                                          facebonesNif As Nifcontent_Class_Manolo,
                                          facebonesShape As INiShape,
                                          chargenTriPath As String,
                                          Optional srcNif As Nifcontent_Class_Manolo = Nothing,
                                          Optional srcShape As INiShape = Nothing,
                                          Optional raceMorphTriPath As String = Nothing) As List(Of System.Numerics.Vector3)
        If state Is Nothing OrElse destNif Is Nothing OrElse clonedOrigShape Is Nothing Then Return Nothing
        If facebonesNif Is Nothing OrElse facebonesShape Is Nothing Then Return Nothing

        ' 1) v_world via FBNS skin with FMRS pose applied + chargen morphs.
        Dim wr = ComputeWorldVerticesForShape(state, facebonesNif, facebonesShape, chargenTriPath, raceMorphTriPath,
                                              srcNif:=srcNif)
        If wr Is Nothing OrElse wr.WorldVertices Is Nothing Then
            Return Nothing
        End If
        Dim vWorld = wr.WorldVertices

        ' 2a) Inject cloth-physics bones from the source NIF's BSClothExtraData into bodySkel.
        ' Hair shapes (Hair28.nif et al.) carry an HKX skeleton inside BSClothExtraData with the
        ' bind reference pose for cloth bones (Hair_C_Cloth00..02 etc.). The render injects these
        ' at PrepareForShapes time so the live skin uses the HKX bind reference. Without this,
        ' the bake's bind resolver falls back to Transform_Class.GetGlobalTransform(boneNode,
        ' destNif) which reads whatever the cloned NIF carries for that bone — leading to
        ' mismatched Mtot_orig vs CK and ~2 unit vertex RMS on hair shapes.
        If srcNif IsNot Nothing AndAlso srcShape IsNot Nothing AndAlso wr.BodySkel IsNot Nothing AndAlso wr.BodySkel.HasSkeleton Then
            Try
                Dim clothSkel = SkeletonClothOverlayHelper_Class.ParseClothSkeleton(srcNif)
                If clothSkel IsNot Nothing Then
                    Dim clothWrap As New NifRenderableShape(srcNif, srcShape, 0)
                    SkeletonClothOverlayHelper_Class.InjectMissingBonesIntoLiveSkeleton(clothWrap, wr.BodySkel, clothSkel)
                End If
            Catch ex As Exception
            End Try
        End If

        ' 2) Per-vertex Mtot_orig from ORIG bones at canonical bind (body skel ∪ face skel ∪
        ' shape-internal fallback). NO FMRS applied to ORIG bones — the ORIG palette is body
        ' bones (HEAD, Neck_skin, ...) + a few face hooks; CK at bake time keeps them at bind.
        ' With cloth-bone injection above, the resolver also resolves Hair_C_Cloth* etc. via the
        ' HKX reference pose instead of falling through to the NIF-crude transform.
        Dim origResolver = BuildBindResolver(wr.FaceSkel, wr.BodySkel, destNif, wr.BwBindSkel)

        ' Walk the cloned ORIG to compute its per-vertex Mtot at bind.
        Dim wrap As New NifRenderableShape(destNif, clonedOrigShape, 0)
        Dim shapeBones = wrap.ShapeBones.ToArray()
        Dim shapeLocalTs = wrap.ShapeBoneTransforms.ToArray()
        If shapeBones.Length <> shapeLocalTs.Length OrElse shapeBones.Length = 0 Then
            Return Nothing
        End If
        Dim nBones = shapeBones.Length
        Dim shapeNode = TryCast(destNif.GetParentNode(clonedOrigShape), NiflySharp.Blocks.NiNode)
        If shapeNode Is Nothing Then shapeNode = destNif.GetRootNode()
        Dim shapeGlobal As Matrix4d = If(shapeNode IsNot Nothing,
                                          Transform_Class.GetGlobalTransform(shapeNode, destNif).ToMatrix4d(),
                                          Matrix4d.Identity)
        ' Paleta del INVERSO: el motor usa la del `_faceBones` en las DOS pasadas cuando hay
        ' CustomizationRemapData (501/501 en vanilla). Ver BuildEngineInverseBinds.
        Dim invBinds = BuildEngineInverseBinds(shapeBones, shapeLocalTs, facebonesNif, facebonesShape)
        Dim precomputedOrig(nBones - 1) As Matrix4d
        For k = 0 To nBones - 1
            Dim bindT As Transform_Class = Nothing
            If origResolver IsNot Nothing Then bindT = origResolver(shapeBones(k))
            If bindT Is Nothing Then bindT = Transform_Class.GetGlobalTransform(shapeBones(k), destNif)
            If bindT Is Nothing Then bindT = New Transform_Class()
            precomputedOrig(k) = shapeGlobal * bindT.ComposeTransforms(invBinds(k)).ToMatrix4d()
        Next

        Dim geom = ShapeGeometryFactory.[For](clonedOrigShape, destNif)
        Dim skin = geom.GetSkinning()
        Dim wpv = If(skin.WeightsPerVertex > 0, skin.WeightsPerVertex, 4)
        Dim flatIdx = skin.BoneIndices
        Dim flatWgt = skin.BoneWeights
        Dim positions = geom.GetVertexPositions()
        Dim vCount = positions.Count

        If vCount <> vWorld.Length Then
            Return Nothing
        End If

        ' Diagnóstico de fidelidad del preview (--headfidelity). OFF por defecto ⇒ el bake no cambia.
        If HeadFidelityEnabled Then
            Try
                ' invBinds (no shapeLocalTs): las DOS paletas del diagnóstico tienen que diferir SÓLO
                ' en el body-weight, o el autochequeo del grupo de control deja de dar 0 exacto.
                CollectHeadFidelity(state, destNif, wr, vWorld, shapeBones, invBinds, shapeGlobal,
                                    skin, wpv, precomputedOrig,
                                    ShapeHasCustomizationNewBones(facebonesNif, facebonesShape),
                                    If(clonedOrigShape.Name?.String, ""))
            Catch ex As Exception
            End Try
        End If


        Dim baked As New List(Of System.Numerics.Vector3)(vCount)
        Dim singularCount As Integer = 0
        ' Buffer reusable para la normalizacion de pesos del MOTOR (ver EngineSkinWeightNormalization). Lado INVERSO
        ' (mundo → local del mesh destino) = el segundo SkinBlend del CK (invert=1, 142B6F91E), con
        ' la paleta del destino. El drift ε = s_src/s_dst − 1 sólo aparece si AMBOS lados (el forward
        ' de SkinBakeMath y este inverso) corren la misma ley. Gate apagado ⇒ bit-idéntico.
        Dim ckW(EngineSkinWeightNormalization.Slots - 1) As Single
        ' Paleta plana para el blend vectorial: UNA vez por shape (20-60 matrices), no por vértice.
        Dim flatPal = FastGeom.BuildFlatPaletteS(precomputedOrig)

        For i = 0 To vCount - 1
            Dim Mtot As Matrix4d = BlendMtot(precomputedOrig, skin, i, wpv, nBones, ckW, flatPal)

            Dim vBaked As Vector3d
            Try
                ' ReanchorAffine ANTES de invertir, y FUERA del If/Else (las tres ramas lo necesitan).
                ' `Mtot += mat * peso` escala los 16 elementos, asi que M44 queda en Σpesos. El forward
                ' no lo nota (TransformPosition ignora w) pero Invert SI hace algebra homogenea y mete
                ' un 1/Σw que cancela justo el ε de la ley del motor. El CK mezcla un 3x4 y su fila 3
                ' queda [0,0,0,1] (SkinBlend 0x142B73230). Ver EngineSkinWeightNormalization.ReanchorAffine.
                Dim invMtot = Matrix4d.Invert(EngineSkinWeightNormalization.ReanchorAffine(Mtot))
                vBaked = Vector3d.TransformPosition(vWorld(i), invMtot)
            Catch
                ' Singular Mtot — keep ORIG vertex as fallback. Should be extremely rare.
                singularCount += 1
                vBaked = New Vector3d(positions(i).X, positions(i).Y, positions(i).Z)
            End Try
            baked.Add(New System.Numerics.Vector3(CSng(vBaked.X), CSng(vBaked.Y), CSng(vBaked.Z)))
        Next

        Return baked
    End Function

    ''' <summary>Una fila del diagnóstico de fidelidad del preview (ver <see cref="CollectHeadFidelity"/>).</summary>
    Public Class HeadFidelityRow
        Public Property NpcFormID As UInteger
        Public Property ShapeName As String = ""
        ''' <summary>El shape trae <c>CustomizationRemapNewBonesData</c> ⇒ el CK NO le hornea el
        ''' body-weight en ninguna de las dos pasadas ⇒ es donde puede haber divergencia.</summary>
        Public Property HasRemapFlag As Boolean
        Public Property VertexCount As Integer
        Public Property MaxD As Double
        Public Property Rms As Double
        ''' <summary>Vértices con peso en UN solo hueso del rig plano. Si dominan, la corrección
        ''' <c>B_k·S_k·inv(B_k)</c> es POR HUESO y no hace falta ningún canal per-vértice.</summary>
        Public Property SingleBoneVerts As Integer
        Public Property MultiBoneVerts As Integer
    End Class

    ''' <summary>OFF por defecto. Lo enciende el CLI (<c>--headfidelity</c>). NO altera nada de lo que
    ''' el bake escribe — sólo mide.</summary>
    Public HeadFidelityEnabled As Boolean = False
    Private ReadOnly _headFidelityRows As New List(Of HeadFidelityRow)
    Private ReadOnly _headFidelityLock As New Object()

    ''' <summary>Devuelve una copia de las filas acumuladas.</summary>
    Public Function GetHeadFidelityRows() As List(Of HeadFidelityRow)
        SyncLock _headFidelityLock
            Return _headFidelityRows.ToList()
        End SyncLock
    End Function

    ''' <summary>ETAPA 1 del diagnostico de fidelidad del preview: mide por shape cuanto se aparta lo que MUESTRA
    ''' EL PREVIEW de lo que MUESTRA EL JUEGO, sin abrir la app.
    ''' <code>
    '''   preview = v_world                                                      (el forward del bake)
    '''   juego   = Mtot_plano(bind.bw_vivo) * inv(Mtot_plano(bind.bw_ck)) * v_world
    ''' </code>
    ''' El render produce por construccion el mismo v_world que el forward del bake, asi que la diferencia se
    ''' calcula entera offline. Las dos paletas salen de <see cref="BuildBindResolver"/>: bw_ck es lo que el bake
    ''' efectivamente invirtio y bw_vivo el mismo body-weight sin ese gate, que es lo que el motor aplica al
    ''' esqueleto tenga o no la flag (la flag solo decide si el CK lo HORNEA).
    ''' <para>AUTOCHEQUEO: en las shapes sin esa flag las dos paletas son identicas, asi que tiene que dar 0
    ''' EXACTO; si da distinto, lo que esta mal es la medicion, no el bake.</para></summary>
    Private Sub CollectHeadFidelity(state As BakeState, destNif As Nifcontent_Class_Manolo,
                                     wr As WorldVertResult, vWorld As Vector3d(),
                                     shapeBones As NiNode(), shapeLocalTs As Transform_Class(),
                                     shapeGlobal As Matrix4d, skin As ShapeSkinningData, wpv As Integer,
                                     precomputedBakeBind As Matrix4d(),
                                     hasRemapFlag As Boolean, shapeName As String)
        Dim nBones = shapeBones.Length
        If nBones = 0 OrElse vWorld Is Nothing OrElse vWorld.Length = 0 Then Return

        ' Paleta VIVA = la del bake pero con el body-weight SIN el gate de la flag.
        Dim bwLiveSkel As SkeletonInstance = Nothing
        Dim bwLivePose = BuildBakeBodyWeightPose(state, wr.BodySkel)
        If bwLivePose IsNot Nothing Then
            bwLiveSkel = LoadBodySkeleton(state)
            If bwLiveSkel IsNot Nothing AndAlso bwLiveSkel.HasSkeleton Then
                bwLiveSkel.ApplyBoneMorphPose(bwLivePose)
            Else
                bwLiveSkel = Nothing
            End If
        End If

        Dim liveResolver = BuildBindResolver(wr.FaceSkel, wr.BodySkel, destNif, bwLiveSkel)
        Dim precomputedLive(nBones - 1) As Matrix4d
        For k = 0 To nBones - 1
            Dim t As Transform_Class = Nothing
            If liveResolver IsNot Nothing Then t = liveResolver(shapeBones(k))
            If t Is Nothing Then t = Transform_Class.GetGlobalTransform(shapeBones(k), destNif)
            If t Is Nothing Then t = New Transform_Class()
            precomputedLive(k) = shapeGlobal * t.ComposeTransforms(shapeLocalTs(k)).ToMatrix4d()
        Next

        Dim m = MeasureOldVsNewWorld(vWorld, shapeBones, skin, wpv, precomputedBakeBind, precomputedLive)

        Dim row As New HeadFidelityRow With {
            .NpcFormID = state.NpcFormID,
            .ShapeName = shapeName,
            .HasRemapFlag = hasRemapFlag,
            .VertexCount = vWorld.Length,
            .MaxD = m.MaxD,
            .Rms = m.Rms,
            .SingleBoneVerts = m.NSingle,
            .MultiBoneVerts = m.NMulti
        }
        SyncLock _headFidelityLock
            _headFidelityRows.Add(row)
        End SyncLock
    End Sub

    ''' <summary>Divergencia de POSICIÓN EN PANTALLA (world-space) entre el camino VIEJO y el NUEVO, por
    ''' vértice. <c>vWorld</c> = lo que dibuja el camino viejo (el <c>_faceBones</c> skineado en vivo);
    ''' <c>vGame = M_live · inv(M_bind_ck) · vWorld</c> = lo que dibuja el nuevo (la malla plana horneada,
    ''' re-skineada con la paleta viva). <c>d = |vGame − vWorld|</c> en unidades de juego.
    ''' <para>Es la MISMA cuenta que valida el modo <c>--headfidelity</c> (control 8,5e-14). Extraída para
    ''' que el batch offline (<see cref="CollectHeadFidelity"/>) y el log LIVE del toggle usen una sola
    ''' implementación.</para></summary>
    Private Function MeasureOldVsNewWorld(vWorld As Vector3d(), shapeBones As NiNode(),
                                           skin As ShapeSkinningData, wpv As Integer,
                                           precomputedBakeBind As Matrix4d(), precomputedLive As Matrix4d()) _
                                           As (MaxD As Double, Rms As Double, WorstIdx As Integer,
                                               WorstOld As Vector3d, WorstNew As Vector3d,
                                               NSingle As Integer, NMulti As Integer, NUsed As Integer)
        Dim nBones = shapeBones.Length
        Dim ckW(EngineSkinWeightNormalization.Slots - 1) As Single
        ' Una paleta plana por cada una de las dos paletas, armadas una sola vez.
        Dim flatPalBind = FastGeom.BuildFlatPaletteS(precomputedBakeBind)
        Dim flatPalLive = FastGeom.BuildFlatPaletteS(precomputedLive)
        Dim ssq As Double = 0, mx As Double = 0, worstIdx As Integer = -1
        Dim worstOld As Vector3d = Nothing, worstNew As Vector3d = Nothing
        Dim nSingle As Integer = 0, nMulti As Integer = 0, nUsed As Integer = 0
        For i = 0 To vWorld.Length - 1
            ' Cuántos huesos del rig PLANO pesan en este vértice (decide si la corrección es por-hueso).
            Dim nb = 0
            If skin.BoneWeights IsNot Nothing AndAlso i < skin.VertexCount Then
                For j = 0 To wpv - 1
                    If CSng(skin.BoneWeights(i * wpv + j)) > 0.0F Then nb += 1
                Next
            End If
            If nb <= 1 Then nSingle += 1 Else nMulti += 1

            Dim mBind = BlendMtot(precomputedBakeBind, skin, i, wpv, nBones, ckW, flatPalBind)
            Dim mLive = BlendMtot(precomputedLive, skin, i, wpv, nBones, ckW, flatPalLive)
            Try
                Dim inv = Matrix4d.Invert(EngineSkinWeightNormalization.ReanchorAffine(mBind))
                Dim vLocal = Vector3d.TransformPosition(vWorld(i), inv)
                Dim vGame = Vector3d.TransformPosition(vLocal, EngineSkinWeightNormalization.ReanchorAffine(mLive))
                Dim d = (vGame - vWorld(i)).Length
                ssq += d * d
                nUsed += 1
                If d > mx Then
                    mx = d : worstIdx = i : worstOld = vWorld(i) : worstNew = vGame
                End If
            Catch
                ' Mtot singular — el bake ya lo contabiliza aparte; acá simplemente no aporta a la métrica.
            End Try
        Next
        Return (mx, If(nUsed > 0, Math.Sqrt(ssq / nUsed), 0.0), worstIdx, worstOld, worstNew, nSingle, nMulti, nUsed)
    End Function

    ''' <summary>invBind por hueso para el lado <b>DESTINO</b> del bake (el inverso), con la paleta que usa el
    ''' <b>MOTOR</b>: la del <c>_faceBones</c> para los huesos que ese rig declara y la del NIF plano para los que
    ''' no.
    ''' <para>El inverso de la app estaba MAL: reconstruia desde el skin del propio shape destino con la paleta
    ''' del destino, que es literalmente la rama de FALLBACK de <c>ApplyCustomizationRemap</c>. Y 501 de 501
    ''' shapes <c>_faceBones</c> vanilla traen CustomizationRemapData (0 de 501 planos la traen), asi que el motor
    ''' NUNCA toma esa rama para una head part del juego.</para>
    ''' <para>Alcanza con sustituir el invBind, sin parsear el remap: los pesos del remap son bit-identicos a los
    ''' del shape plano (8660/8660 vertices en 6 pares vanilla) y lo unico que cambia son los indices, que apuntan
    ''' a la paleta del source. Toda la diferencia colapsa al invBind por hueso, y el apareo del motor es por
    ''' nombre igual que aca. El RE completo esta en 40-bake-leyes-fo4.</para>
    ''' <para>Magnitud, que no es lo mismo que correctitud: mueve la salida max 2,97e-4 / rms 2,20e-5 sobre
    ''' 607.376 vertices, y solo es discriminable donde los binds difieren (FemaleHeadHuman mejora un 24 % contra
    ''' el CK). ⛔ Que la correccion sea chica no la hace opcional.</para>
    ''' <para>⛔ Corolario para el RENDER: al DIBUJAR hay que seguir usando el invBind del NIF PLANO. Medido sobre
    ''' 301 FaceGeom del BA2, el BoneData que el CK escribe a disco es el del plano en 8.186 entradas contra 0 del
    ''' <c>_faceBones</c>. Esta sustitucion es SOLO del inverso del bake.</para>
    ''' <para>Degrada, no rompe: sin NIF <c>_faceBones</c>, sin shape, o si un hueso del rig plano no aparece en
    ''' el <c>_faceBones</c>, esa entrada se queda con el invBind del plano, que es exactamente lo que el motor
    ''' anexa.</para></summary>
    Private Function BuildEngineInverseBinds(shapeBones As NiNode(),
                                              flatLocalTs As Transform_Class(),
                                              facebonesNif As Nifcontent_Class_Manolo,
                                              facebonesShape As INiShape) As Transform_Class()
        Dim n = shapeBones.Length
        Dim outT(n - 1) As Transform_Class
        Array.Copy(flatLocalTs, outT, n)
        If facebonesNif Is Nothing OrElse facebonesShape Is Nothing Then Return outT
        Try
            Dim fw As New NifRenderableShape(facebonesNif, facebonesShape, 0)
            Dim fb = fw.ShapeBones
            Dim ft = fw.ShapeBoneTransforms
            If fb Is Nothing OrElse ft Is Nothing OrElse fb.Count <> ft.Count Then Return outT
            Dim byName As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
            For j = 0 To fb.Count - 1
                Dim nm = If(fb(j)?.Name?.String, "")
                If nm <> "" Then byName(nm) = ft(j)
            Next
            For k = 0 To n - 1
                Dim nm = If(shapeBones(k)?.Name?.String, "")
                If nm = "" Then Continue For
                Dim t As Transform_Class = Nothing
                If byName.TryGetValue(nm, t) AndAlso t IsNot Nothing Then outT(k) = t
            Next
        Catch ex As Exception
            ' Cualquier fallo ⇒ se queda con el invBind del plano (comportamiento previo).
        End Try
        Return outT
    End Function

    ''' <summary>Mezcla per-vértice de la paleta del DESTINO: Σ w·M con la ley de pesos del MOTOR
    ''' (<see cref="EngineSkinWeightNormalization"/>) y los mismos fallbacks (fila sin skin, Σw=0) que
    ''' venía haciendo el inverso del bake inline. UNA sola implementación: la consumen el inverso de
    ''' <see cref="BakeShape"/> y el diagnóstico <see cref="CollectHeadFidelity"/>, así no pueden
    ''' divergir. <paramref name="ckW"/> es un buffer reusable del caller (evita alocar por vértice).</summary>
    ''' <param name="flatPal">Paleta plana de <paramref name="precomputed"/>, armada UNA vez por shape
    ''' con <c>FastGeom.BuildFlatPalette</c>. Es lo que habilita el camino vectorial; con
    ''' <c>Nothing</c> el blend cae al escalar y da exactamente lo mismo (lo garantiza el gate
    ''' <c>skin-blend</c>, que compara los dos caminos BIT A BIT sobre esta misma función).</param>
    Private Function BlendMtot(precomputed As Matrix4d(), skin As ShapeSkinningData,
                                i As Integer, wpv As Integer, nBones As Integer,
                                ckW As Single(), Optional flatPal As Single() = Nothing) As Matrix4d
        ' EL CUERPO SE FUE A SkinningHelper.BlendBoneMatrices. Acá había una TERCERA copia escrita
        ' a mano de la misma ley (la 4ta estaba en SkinBakeMath), y el precio no era sólo la
        ' duplicación: el gate `skin-blend` de FaceGenBuilder afirma que "el bake usa esa misma ley,
        ' asi que una divergencia ahi saldria a los vertices horneados" — y era FALSO, porque el bake
        ' no llamaba a la función que el gate prueba. Compartían la LEY, no el CÓDIGO, que es
        ' exactamente lo que esta misma release corrigió en BuildPosePalette y en
        ' FillPerVertexSkinMatrix (y ahí, factorizar destapó una SEGUNDA divergencia que no se veía
        ' mirando los dos cuerpos por separado).
        ' De paso el bake pasa a usar el blend VECTORIAL: el SIMD de FastGeom entró por dentro de
        ' BlendBoneMatrices y por eso nunca lo había tocado.
        '
        ' `ckW` ya no se usa: la normalización del motor vive adentro de BlendBoneMatrices, con su
        ' propio scratch por hilo. Se conserva en la firma para no tocar a los llamadores.
        '
        ' Las TRES diferencias textuales que tenía esta copia, revisadas una por una antes de borrarla:
        '   1. no exigía `available >= Slots` antes de TryComputeWeights ⇒ INERTE: TryComputeWeights ya
        '      rechaza solo con `wpv <> Slots` y con `baseSlot + Slots > flatWgt.Length`.
        '   2. recorría `wpv` sin acotar contra el largo del array; BlendBoneMatrices acota con
        '      `available` ⇒ sólo se separan con un array corto, donde ésta reventaba.
        '   3. con `nBones = 0` dejaba `Matrix4d.Zero` (manda TODO vértice al origen);
        '      BlendBoneMatrices devuelve Identity, que es lo defendible.
        ' Ninguna de las tres ocurre con entrada sana ⇒ se predijo CERO bytes, y se midió.
        Dim flatIdx = skin.BoneIndices
        Dim flatWgt = skin.BoneWeights
        ' El guard por FILA (`i < skin.VertexCount`) es de acá: BlendBoneMatrices no conoce el índice
        ' de vértice. Sin fila de skin se pasa Nothing, que es su camino de "sin skin" y devuelve
        ' precomputed(0) — el mismo resultado que daba el fallback de Σw=0 de esta copia.
        If flatIdx Is Nothing OrElse flatWgt Is Nothing OrElse i >= skin.VertexCount Then
            Return SkinningHelper.BlendBoneMatrices(Nothing, Nothing, 0, wpv, precomputed, flatPal)
        End If
        Return SkinningHelper.BlendBoneMatrices(flatWgt, flatIdx, i * wpv, wpv, precomputed, flatPal)
    End Function

    ''' <summary>Walk the face skeleton hierarchy from <paramref name="boneName"/> upward
    ''' until we hit a bone whose name is in <paramref name="palette"/>. Returns the matching
    ''' bone name, or empty string if the walk reaches the root without finding one.</summary>
    Public Function WalkParentToPaletteAncestor(boneName As String,
                                                 faceSkel As SkeletonInstance,
                                                 palette As HashSet(Of String)) As String
        If faceSkel Is Nothing OrElse Not faceSkel.HasSkeleton Then Return ""
        If palette Is Nothing OrElse palette.Count = 0 Then Return ""
        If String.IsNullOrEmpty(boneName) Then Return ""
        ' Direct hit: the bone itself is already in palette (typical for body bones in the FBNS shape).
        If palette.Contains(boneName) Then Return boneName

        Dim current = boneName
        Dim guard = 0
        While guard < 32  ' Sanity bound — vanilla face skeleton has depth <10.
            guard += 1
            Dim parent = faceSkel.GetParentNodeNameSkeleton(current)
            If String.IsNullOrEmpty(parent) Then Return ""
            If palette.Contains(parent) Then Return parent
            current = parent
        End While
        Return ""
    End Function

    ''' <summary>SYNC: RENDER == BAKE — lado BAKE. Aplica los morphs de vértice del `.tri` (chargen, y en
    ''' SSE también el de raza) al array de vértices del shape IN PLACE, usando el MISMO plan que arma el
    ''' render: <c>NpcMorphResolver.BuildFaceMorphPlan</c>. Ése es el único builder y no debe duplicarse acá;
    ''' un segundo camino de morph rompe WYSIWYG en silencio. Ver 00-reglas-dos-juegos-y-render-bake.md §1.
    ''' <para>Es <c>Friend</c> para que el bake de SSE —que no tiene rig <c>_faceBones</c> y por eso nunca
    ''' entra a <see cref="BakeShape"/>— pueda morphear el shape clonado directo. FO4 llega por
    ''' <see cref="ComputeWorldVerticesForShape"/>. No-op si el HDPT no declara tri de chargen o si el plan
    ''' no tiene canales que apliquen.</para></summary>
    Friend Sub ApplyChargenMorphsInPlace(nif As Nifcontent_Class_Manolo,
                                           shape As INiShape,
                                           chargenTriPath As String,
                                           raceMorphTriPath As String,
                                           state As BakeState,
                                           Optional headMeshTriPath As String = Nothing)
        Dim isSse = state.NpcData IsNot Nothing AndAlso state.NpcData.Game = Config_App.Game_Enum.Skyrim

        ' ⛔ SYNC: RENDER == BAKE. El resolver de runtime mergea el tri de morphs de raza (HDPT NAM0=0) CON el de
        ' chargen (NAM0=2) en UN solo TriHead, y el bake TIENE que hacer lo mismo en SSE o pierde en silencio el
        ' morph facial de la raza: el plan lo aplica por EditorID de RACE a peso 1, pero ese morph vive SOLO en el
        ' tri de raza, asi que con un TriHead de solo chargen el canal queda en no-op. Por eso el render en vivo
        ' se veia bien y el NIF horneado no.
        ' FO4 se queda solo con chargen (validado byte-exacto contra el CK): su plan pide nombres de sculpt que
        ' viven todos en el tri de chargen, y el merge del render solo agrega morphs de expresion sin usar.
        ' La geometria se toma al principio para que su cantidad de vertices maneje el redirect del .tri de High
        ' Poly Head y se reuse al escribir el morph.
        Dim geom = ShapeGeometryFactory.[For](shape, nif)
        Dim shapeVerts = geom.VertexCount

        Dim triHead As TriHeadFile
        If isSse Then
            ' SSE merges race (NAM0=0) + chargen (NAM0=2) + the head MESH tri (SkinnyMorph weight morph). The
            ' mesh tri is per-part and RACE-AWARE by construction: headMeshTriPath is ChangeExtension of THIS
            ' head-part's own mesh (femalehead / ...argonian / ...khajiit / hairNN — each ships its own SkinnyMorph
            ' at its own vertex count), so no race table or vertex-count gate is needed.
            '
            ' HPH redirect — the SAME resolver the live render calls (NpcMorphResolver.ResolveHphHeadPartTriPath), so
            ' render == bake by construction (regla de oro). Opt-in + SSE-gated; returns each record path unchanged
            ' unless it's missing/wrong-topology/empty for a known HPH part (e.g. brows, whose HDPT ships only NAM0=1).
            Dim vertsOf As Func(Of String, Integer) = Function(p)
                                                          Dim h = LoadHeadTriCached(p, state)
                                                          Return If(h Is Nothing, -1, CInt(h.NumVertices))
                                                      End Function
            Dim rRace = NpcMorphResolver.ResolveHphHeadPartTriPath(raceMorphTriPath, shapeVerts, NpcMorphResolver.HphTriSlot.Race, vertsOf)
            Dim rChargen = NpcMorphResolver.ResolveHphHeadPartTriPath(chargenTriPath, shapeVerts, NpcMorphResolver.HphTriSlot.Chargen, vertsOf)
            Dim rMesh = NpcMorphResolver.ResolveHphHeadPartTriPath(headMeshTriPath, shapeVerts, NpcMorphResolver.HphTriSlot.Mesh, vertsOf)
            triHead = LoadMergedHeadTri(rRace, rChargen, state, rMesh)
        Else
            triHead = LoadHeadTriCached(chargenTriPath, state)
        End If
        If triHead Is Nothing Then
            Return
        End If

        ' Single per-game morph plan — the SAME builder the live render calls (NpcMorphResolver.
        ' BuildFaceMorphPlan): FO4 = MSDK/MSDV+MPPI via RACE defs; SSE = race base + NAM9 + NAMA + RaceMenu.
        ' One path per game, no divergence between render and bake.
        ' Race base morph is looked up by the MORPH-race EditorID (RACE.NAM8 redirect: e.g. Dremora→DarkElf,
        ' every *Vampire→base race), not the actor's raw race — see RecordParsers.ResolveMorphRaceEditorId.
        Dim plan = NpcMorphResolver.BuildFaceMorphPlan(state.NpcData, triHead,
                                                       RecordParsers.ResolveMorphRaceEditorId(state.Race, state.PluginManager),
                                                       state.RaceMorphValueDefs, state.RaceMorphPresetDefs,
                                                       raceKeywordEditorIds:=RecordParsers.GetRaceKeywordEditorIds(state.Race, state.PluginManager),
                                                       shapeChargenTriPath:=chargenTriPath)
        If plan Is Nothing OrElse Not plan.HasMorphs Then Return

        Dim positionsFloat = geom.GetVertexPositions()
        Dim count = positionsFloat.Count
        If count = 0 Then Return
        Dim positionsDouble(count - 1) As Vector3d
        For i = 0 To count - 1
            positionsDouble(i) = New Vector3d(positionsFloat(i).X, positionsFloat(i).Y, positionsFloat(i).Z)
        Next
        ' The SSE actor-weight head/hair morph is now a normal channel INSIDE the plan (SkinnyMorph, added by
        ' BuildFaceMorphPlanFromNam9 from the merged head MESH tri) — no separate table pass. Render and bake
        ' therefore weight-morph through this single MorphEngine call, per-part and race-aware.
        Dim morphed = MorphEngine.ApplyChannelsToVertexArray(positionsDouble, plan)
        Dim outFloat As New List(Of System.Numerics.Vector3)(count)
        For i = 0 To count - 1
            outFloat.Add(New System.Numerics.Vector3(CSng(morphed(i).X), CSng(morphed(i).Y), CSng(morphed(i).Z)))
        Next
        ' VertexDesc (SSE): OJO al histórico. SetVertexPositions hacía HasVertices=True también en
        ' BSDynamicTriShape, encendiendo el atributo "Vertex" y DUPLICANDO las posiciones al buffer estático
        ' (VertSize 5→9 en el head). Una nota anterior lo dio por "diff cosmético" y era FALSO: con la posición
        ' en el estático el motor deja de leerla del array dinámico, que es donde escribe la animación facial
        ' ⇒ cara correcta pero CONGELADA, sin lip-sync ni expresiones (confirmado in-game 2026-07-11).
        ' Arreglado en la raíz (NiflySharp BSTriShape.SetVertexPositions: guard `is not BSDynamicTriShape`),
        ' así que aquí ya no hace falta nada. El estático SÍ debe seguir llevando el morph: FinalizeData llama
        ' a CalcDynamicData, que regenera el array dinámico A PARTIR de él — por eso escribir dynShape.Vertices
        ' directo no funciona (se machaca).
        geom.SetVertexPositions(outFloat)
    End Sub

    ''' <summary>Load+parse a head TriHead by mesh path, cached per-BakeState (Nothing is cached too so a
    ''' missing/broken path is not re-attempted for every shape). Returns Nothing on miss/parse-fail.</summary>
    Private Function LoadHeadTriCached(triPath As String, state As BakeState) As TriHeadFile
        If String.IsNullOrEmpty(triPath) Then Return Nothing
        Dim key = MeshPathHelpers.NormalizeMeshKey(triPath)
        Dim head As TriHeadFile = Nothing
        If state.TriHeadCache.TryGetValue(key, head) Then Return head
        head = ParseHeadTri(key)
        state.TriHeadCache(key) = head
        Return head
    End Function

    ''' <summary>Parse a TriHead from a normalized FilesDictionary key. Nothing on missing bytes / parse error.</summary>
    Private Function ParseHeadTri(normalizedKey As String) As TriHeadFile
        Dim bytes = FilesDictionary_class.GetBytes(normalizedKey)
        If bytes Is Nothing OrElse bytes.Length = 0 Then Return Nothing
        Try
            Dim head = TriHeadParser.ParseTriHeadFromBytes(bytes)
            ' SYNC: RENDER == BAKE — gemelo en NpcMorphResolver (parse del tri del render). El fix se
            ' aplica sobre el parse FRESCO y antes de cachear, para que los dos caminos vean los mismos deltas.
            ChargenMouthFix.MaybeApplyInPlace(normalizedKey, head)
            Return head
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>⛔ SYNC: RENDER == BAKE, lado BAKE de la resolucion de <c>.tri</c>. Su gemelo es
    ''' <c>NpcMorphResolver.LoadTriForShape</c> y tiene que resolver IGUAL: primero el tri de raza (HDPT NAM0=0) y
    ''' despues el de chargen (NAM0=2), agregando solo los nombres que la raza no traiga (la raza gana las
    ''' colisiones).
    ''' <para>El merge se cachea por BakeState bajo una clave compuesta. El lado de raza se parsea FRESCO y no
    ''' desde la cache por path: mutarlo con los morphs de chargen corromperia un TriHead que se reusa en otro
    ''' lado. Si falta uno de los dos lados, cae al que este presente.</para></summary>
    ''' <remarks>Friend y no Private para que SseMorphReverseEngineer construya la MISMA base mergeada que usa el
    ''' bake: duplicarlo alla crearia una segunda fuente de verdad que se desincroniza en silencio.</remarks>
    Friend Function LoadMergedHeadTri(raceMorphTriPath As String, chargenTriPath As String, state As BakeState,
                                       Optional headMeshTriPath As String = Nothing) As TriHeadFile
        Dim raceKey = If(String.IsNullOrEmpty(raceMorphTriPath), "", MeshPathHelpers.NormalizeMeshKey(raceMorphTriPath))
        Dim chargenKey = If(String.IsNullOrEmpty(chargenTriPath), "", MeshPathHelpers.NormalizeMeshKey(chargenTriPath))
        Dim meshKey = If(String.IsNullOrEmpty(headMeshTriPath), "", MeshPathHelpers.NormalizeMeshKey(headMeshTriPath))
        Dim comboKey = "merged:" & raceKey & "|" & chargenKey & "|" & meshKey
        Dim merged As TriHeadFile = Nothing
        If state.TriHeadCache.TryGetValue(comboKey, merged) Then Return merged

        ' Parse ALL THREE sides FRESH (owned copies) so the merge base is never a shared per-path cache object —
        ' MergeChargenIntoRaceTriHead mutates its first arg, and folding into a shared TriHead would corrupt it for
        ' other consumers. Precedence race > chargen > mesh (add-if-absent). Merging the head MESH tri LAST is what
        ' brings "SkinnyMorph" (the actor-weight head/hair morph) into the plan's TriHead — the SAME source the live
        ' render picks up in LoadTriForShape. CRUCIAL for HAIR/hairline/beard parts: they have NO race/chargen tri
        ' at all, only their own mesh tri (hairNN.tri = a single SkinnyMorph), so the base ends up BEING that fresh
        ' mesh TriHead and the weight morph still applies. The comboKey includes meshKey so distinct meshes don't alias.
        ' Los tres lados se parsean FRESCOS. El motivo escrito era que `MergeChargenIntoRaceTriHead`
        ' mutaba su primer argumento — ya NO lo hace (pasó a copy-on-write con `ClonarParaMerge`), asi
        ' que este re-parseo por cada miss de `comboKey` es coste sin causa. Se CONSERVA igual: cambiarlo
        ' a un parse cacheado es una optimizacion que toca el camino del bake y hay que MEDIRLA, no
        ' deducirla. Lo que se arregla ahora es el comentario, que justificaba algo que ya no pasa.
        Dim raceHead As TriHeadFile = If(raceKey = "", Nothing, ParseHeadTri(raceKey))
        Dim chargenHead As TriHeadFile = If(chargenKey = "", Nothing, ParseHeadTri(chargenKey))
        Dim meshHead As TriHeadFile = If(meshKey = "", Nothing, ParseHeadTri(meshKey))
        Dim baseTri As TriHeadFile = raceHead
        NpcMorphResolver.MergeChargenIntoRaceTriHead(baseTri, chargenHead)
        NpcMorphResolver.MergeChargenIntoRaceTriHead(baseTri, meshHead)

        ' RaceMenu extended morphs — the SAME merge the live render does (NpcMorphResolver.LoadTriForShape).
        ' A .jslot custom morph names a morph that lives in an extended .tri mapped from the chargen tri by
        ' morphs.ini. Without merging them the bake would silently drop every extended slider while the render
        ' showed it, breaking render == bake.
        Dim catalog = NpcMorphResolver.SliderCatalog
        If catalog IsNot Nothing AndAlso Not String.IsNullOrEmpty(chargenTriPath) Then
            For Each extTriPath In catalog.GetExtendedMorphTris(chargenTriPath)
                Dim extHead = ParseHeadTri(MeshPathHelpers.NormalizeMeshKey(extTriPath))
                If extHead IsNot Nothing Then NpcMorphResolver.MergeChargenIntoRaceTriHead(baseTri, extHead)
            Next
        End If
        merged = baseTri
        state.TriHeadCache(comboKey) = merged
        Return merged
    End Function

    ''' <summary>Returns the union of bone names from the actor's face + body skeletons.
    ''' Used by the bake to drop source shapes that skin to bones outside this set
    ''' (e.g. MaleEyesGhoul.nif's GhoulTearDuct sub-shape, which skins to a custom
    ''' 'GhoulTearDuct' bone that no actor skeleton exposes — CK drops it for that reason).
    ''' Returns an empty (case-insensitive) HashSet if either skeleton load fails.</summary>
    Public Function GetActorBoneNames(state As BakeState) As HashSet(Of String)
        Dim names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If state Is Nothing Then Return names
        Try
            Dim faceSkel = LoadFaceSkeleton(state)
            If faceSkel IsNot Nothing AndAlso faceSkel.HasSkeleton Then
                For Each k In faceSkel.SkeletonDictionary.Keys
                    names.Add(k)
                Next
            End If
        Catch ex As Exception
        End Try
        Try
            Dim bodySkel = LoadBodySkeleton(state)
            If bodySkel IsNot Nothing AndAlso bodySkel.HasSkeleton Then
                For Each k In bodySkel.SkeletonDictionary.Keys
                    names.Add(k)
                Next
            End If
        Catch ex As Exception
        End Try
        Return names
    End Function

    ''' <summary>Los bytes del esqueleto se resuelven UNA vez por <see cref="BakeState"/> (= por NPC) y se
    ''' reusan para todas sus shapes. Antes cada shape volvía a pedirlos: con el bake de disco eso era
    ''' 2 lecturas × N shapes por NPC, y con el PREVIEW pasó a ser 2×N por cada tick de slider FMRS
    ''' (el provider re-hornea dentro de PipelineStep_Morphs). Sigue habiendo un <c>SkeletonInstance</c>
    ''' NUEVO por shape a propósito: el bake les aplica poses distintas (el body-weight se saltea en las
    ''' shapes con <c>CustomizationRemapNewBonesData</c>) e inyecta huesos de cloth por shape.</summary>
    Private Function LoadFaceSkeleton(state As BakeState) As SkeletonInstance
        Dim bytes = state.FaceSkeletonBytes
        If bytes Is Nothing Then
            bytes = FaceSkeletonResolver.TryLoadFaceSkeletonBytes(state.RaceFormID, state.IsFemale, state.PluginManager)
            If bytes Is Nothing Then Return Nothing
            state.FaceSkeletonBytes = bytes
        End If
        Dim skel As New SkeletonInstance()
        If Not skel.LoadFromBytes(bytes) Then Return Nothing
        Return skel
    End Function

    Private Function LoadBodySkeleton(state As BakeState) As SkeletonInstance
        ' Body skel path comes from RACE.ANAM (FemaleSkeletonPath / MaleSkeletonPath).
        ' Misma razón que en LoadFaceSkeleton: la key se resuelve una vez por NPC.
        Dim key = state.BodySkeletonKey
        If key = "-" Then Return Nothing
        If key = "" Then
            Dim path = If(state.IsFemale, state.Race.FemaleSkeletalModel, state.Race.MaleSkeletalModel)
            If String.IsNullOrEmpty(path) Then path = If(state.IsFemale, state.Race.MaleSkeletalModel, state.Race.FemaleSkeletalModel)
            If String.IsNullOrEmpty(path) Then
                state.BodySkeletonKey = "-"
                Return Nothing
            End If
            key = MeshPathHelpers.NormalizeMeshKey(path)
            state.BodySkeletonKey = key
        End If
        Dim skel As New SkeletonInstance()
        If Not skel.LoadFromKey(key) Then Return Nothing
        Return skel
    End Function

    Private Function BuildPoseResolver(faceSkel As SkeletonInstance,
                                        bodySkel As SkeletonInstance,
                                        shapeNif As Nifcontent_Class_Manolo) As Func(Of NiNode, Transform_Class)
        Return Function(boneNode As NiNode) As Transform_Class
                   If boneNode Is Nothing Then Return Nothing
                   Dim boneName = If(boneNode.Name?.String, "")
                   If boneName = "" Then Return Nothing

                   ' 1) Face skel wins (FMRS bone-morph folded in via ApplyBoneMorphPose →
                   ' GetGlobalTransform includes the MorphDeltaTransform layer).
                   If faceSkel IsNot Nothing AndAlso faceSkel.HasSkeleton Then
                       Dim hb As HierarchiBone_class = Nothing
                       If faceSkel.SkeletonDictionary.TryGetValue(boneName, hb) AndAlso hb IsNot Nothing Then
                           Return hb.GetGlobalTransform
                       End If
                   End If

                   ' 2) Body skel fallback. GetGlobalTransform (NOT OriginalGetGlobalTransform) so the
                   ' MorphDeltaTransform layer is included: the only morph applied to bodySkel here is
                   ' the CK NNAM neck-fat scale on the literal "Neck" bone (see ComputeWorldVerticesForShape).
                   ' Every other body bone has MorphDeltaTransform=Nothing, so GetGlobalTransform == bind
                   ' for them — identical to the previous OriginalGetGlobalTransform for all non-Neck bones.
                   If bodySkel IsNot Nothing AndAlso bodySkel.HasSkeleton Then
                       Dim hb As HierarchiBone_class = Nothing
                       If bodySkel.SkeletonDictionary.TryGetValue(boneName, hb) AndAlso hb IsNot Nothing Then
                           Return hb.GetGlobalTransform
                       End If
                   End If

                   ' 3) Last fallback: walk the bone's parent chain in the shape's own NIF.
                   Return Transform_Class.GetGlobalTransform(boneNode, shapeNif)
               End Function
    End Function

End Module
