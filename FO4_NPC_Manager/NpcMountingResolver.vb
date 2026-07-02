Imports System.Globalization
Imports System.IO
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FO4_Base_Library
Imports MaterialLib
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Mathematics

''' <summary>Phase 2 of the MainForm split (MeshCollection/Mounting — Increment 1): MOUNTING math.
''' Computes/applies mount-delta transforms for robot chunks + sockets onto a live SkeletonInstance,
''' resolves host-scoped attachment sockets, and the synthetic-skin Pipboy mount. Pure data + NiflySharp
''' parsing — NO WinForms controls, NO GL/host execution (it runs on the render Task.Run; the orchestrator
''' RenderCurrentStateAsync stays in MainForm and CALLS these). DI: NpcRenderContext (PluginManager +
''' parse caches) + NpcStateResolver (ResolveSkeletonKey). Shared nested types (MeshCandidate,
''' PreviewResolutionResult, NPCVisualState, PublisherSocketInfo, MountDesiredWorldEntry) stay nested in
''' MainForm and are referenced as MainForm.&lt;T&gt;. See project_mainform_split.</summary>
Friend NotInheritable Class NpcMountingResolver
    Private ReadOnly _ctx As NpcRenderContext
    Private ReadOnly _stateResolver As NpcStateResolver

    Public Sub New(ctx As NpcRenderContext, stateResolver As NpcStateResolver)
        _ctx = ctx
        _stateResolver = stateResolver
    End Sub

    ''' <summary>Hierarchy depth de un bone en actor.skel — cuenta cuántos parents tiene hasta
    ''' el root. Usado para sortear shape bones en orden top-down antes de aplicar overrides
    ''' (parent-first). Sin esto, si los shape bones del NIF están en orden no-hierarchical
    ''' (ej. LEFT arm Protectron: LUpperArmTwist=child primero, LClavicleTwist=root al final),
    ''' los overrides children fires antes que parent → cuando parent override fires → cascade
    ''' al children rompe su world (cascade drift). Procesar en depth-order garantiza que cada
    ''' child se overridea contra el parent.world ya finalizado.</summary>
    Private Function GetBoneHierarchyDepth(hb As HierarchiBone_class) As Integer
        If hb Is Nothing Then Return 0
        Dim depth As Integer = 0
        Dim current = hb
        Dim safety As Integer = 0
        While current.Parent IsNot Nothing AndAlso safety < 200
            depth += 1
            current = current.Parent
            safety += 1
        End While
        Return depth
    End Function

    ''' <summary>COLECTA el plan V2 SKEL-OVERRIDE para una shape con mount socket: computa cxNode,
    ''' G_CX, parentBoneWorld, M_mesh, y por cada bone (W_B = A × G_B, A = M_mesh × inv(G_CX)) agrega un
    ''' <see cref="MainForm.MountDesiredWorldEntry"/> al plan <see cref="MainForm.PreviewResolutionResult.MountDesiredWorlds"/>
    ''' (con <c>TargetSkel</c>) más actualiza <paramref name="chunkWBHistory"/> para la cascade
    ''' cross-shape. NO aplica MountDelta — eso lo hace <see cref="ApplyMountPlanForActor"/> en
    ''' orden topológico tras el shape loop (fuente única de verdad para initial render + pose-dirty).
    ''' Si cxNode no se encuentra en chunk NIF, emite DIAG-BIND-BAKE diagnostics.
    ''' Try/Catch envolvente — excepciones se loggean sin propagar al shape loop.</summary>
    Friend Sub CollectV2PlanForShape(shape As IRenderableShape,
                                       socket As BSConnectPointReader.ConnectPointInfo,
                                       targetSkel As SkeletonInstance,
                                       renderData As MainForm.PreviewResolutionResult,
                                       wbHistory As Dictionary(Of String, Transform_Class),
                                       isRobotMount As Boolean)
        If shape.ShapeBones Is Nothing OrElse shape.ShapeBoneTransforms Is Nothing Then Return
        Try
            ' Derive cxName from the actual mount socket (counterpart of socket.Name).
            ' El chunk's BSConnectPoint::Children PointName puede ser inconsistente con el
            ' socket donde OBTE lo monta: ej. HeadArmorProtectron.nif (clean) declara
            ' Children=["C-Head"] pero se monta en P-HeadArmorProtectron. Usar el cxName
            ' del chunk hace que V2 elija G_CX de un NiNode posicionado para OTRO frame
            ' de attachment (C-Head a altura de cabeza vs C-HeadArmorProtectron a altura
            ' del helmet socket) → A equivocado → casco rotado/caído. OBTE es autoritativo.
            ' Convención canónica (per BSConnectPointBoneInjector.TryGetSocketCounterpartName):
            ' "P-X" → "C-X", "P_X" → "C_X".
            Dim cxName As String = If(socket IsNot Nothing, BSConnectPointBoneInjector_Class.TryGetSocketCounterpartName(socket.Name), "")

            If Not String.IsNullOrEmpty(cxName) Then
                ' Find C-X NiNode (try exact, fallback suffix strip).
                Dim cxNode As NiflySharp.Blocks.NiNode = shape.NifContent.FindBlockByName(Of NiflySharp.Blocks.NiNode)(cxName)
                If cxNode Is Nothing Then
                    Dim cxNormSearch = NameUtils.StripInstanceSuffix(cxName)
                    For Each blk In shape.NifContent.Blocks
                        Dim cand = TryCast(blk, NiflySharp.Blocks.NiNode)
                        If cand Is Nothing Then Continue For
                        Dim candNm = If(cand.Name?.String, "")
                        If String.Equals(NameUtils.StripInstanceSuffix(candNm), cxNormSearch, StringComparison.OrdinalIgnoreCase) Then
                            cxNode = cand
                            Exit For
                        End If
                    Next
                End If

                If cxNode IsNot Nothing Then
                    Dim G_CX = Transform_Class.GetGlobalTransform(cxNode, shape.NifContent)

                    ' Compute P_world (M_mesh) desde el socket dict-existing.
                    '
                    ' UNIFICACIÓN: parentBoneWorld usa ResolveEffectiveWorld para respetar V2
                    ' de parent chunks. Si un chunk anterior corrió V2 sobre socket.ParentBoneName,
                    ' su W_B vive en chunkWBHistory[ParentBoneName] y representa la posición real
                    ' del bone post-V2. Sin esto, V2 sobre chunks que montan en V2-corregidos
                    ' usaría posiciones desactualizadas y la cascada se rompería.
                    Dim parentBoneWorld As Transform_Class = ResolveEffectiveWorld(wbHistory, targetSkel, socket.ParentBoneName)
                    If isRobotMount AndAlso Logger.Enabled Then
                        Dim hasOverride = wbHistory.ContainsKey(socket.ParentBoneName)
                        Dim shL = shape.ShapeName, pbnL = socket.ParentBoneName, hoL = hasOverride
                        Dim pwT = parentBoneWorld.Translation
                        Logger.LogLazy(Function() $"[V2-MMESH] shape='{shL}' parent_bone='{pbnL}' effective_world.T=({pwT.X:F3},{pwT.Y:F3},{pwT.Z:F3}) (chunkWBHistory-override={hoL})")
                    End If

                    ' socket.Translation YA viene del resolver en Parents space (BSConnectPoint::Parents
                    ' = chunk-source declaration).

                    ' [HOST-SCOPED PATH A] Si la pre-pass A_HOST ya computó cand.ChunkToActor
                    ' (Path A: M_mesh = host.ChunkToActor × HostSocketGlobalT en espacio del NIF
                    ' del host), V2 deriva M_mesh = A × G_CX para mantener consistencia con
                    ' downstream checks (ACTOR-RIG vs MODULE-RIG depende de M_mesh.T). Esto
                    ' reemplaza el cálculo legacy parentBoneWorld × socketLocal solo cuando
                    ' la pre-pass aplicó Path A — sino el path skeleton actual sigue.
                    Dim _candForShape As MainForm.MeshCandidate = Nothing
                    renderData.ShapeCandidate.TryGetValue(shape, _candForShape)

                    Dim M_mesh As Transform_Class
                    If _candForShape IsNot Nothing AndAlso _candForShape.ChunkToActor IsNot Nothing Then
                        ' Path A: A ya fue computado por la pre-pass usando coord system del
                        ' host NIF correctamente. Derivar M_mesh = A × G_CX para mantener
                        ' compatibilidad con downstream checks (ACTOR-RIG vs MODULE-RIG).
                        M_mesh = _candForShape.ChunkToActor.ComposeTransforms(G_CX)
                        Dim shL_pa = shape.ShapeName
                        Dim mmTl = M_mesh.Translation
                        Logger.LogLazy(Function() $"[V2-MMESH-PATH-A] shape='{shL_pa}' using pre-pass ChunkToActor, M_mesh.T=({mmTl.X:F3},{mmTl.Y:F3},{mmTl.Z:F3})")
                    Else
                        ' Path B (legacy parentBone × socket.local) ELIMINADO: confirmado INALCANZABLE
                        ' (barrido 4473 NPCs = 0 disparos, 2026-06-14). Fail-loud: si ChunkToActor no
                        ' resolvió (cadena de hosts rota/ciclo), gritarlo y saltar el shape — nunca
                        ' computar el mount por el camino no-canónico en silencio.
                        Dim pbReason As String = If(_candForShape Is Nothing, "candForShape=Nothing", "ChunkToActor=Nothing")
                        Dim pbShape As String = shape.ShapeName, pbSocket As String = If(socket.Name, "?")
                        Logger.LogLazy(Function() $"[PATH-B-IMPOSIBLE] shape='{pbShape}' socket='{pbSocket}' reason={pbReason} — ChunkToActor no resuelto, shape salteado")
                        System.Windows.Forms.MessageBox.Show("PATH B IMPOSIBLE — no debería pasar." & vbCrLf & vbCrLf &
                                        "shape  = " & pbShape & vbCrLf &
                                        "socket = " & pbSocket & vbCrLf &
                                        "razón  = " & pbReason & vbCrLf & vbCrLf &
                                        "La cadena de hosts no resolvió ChunkToActor. Shape salteado.",
                                        "Path B imposible", MessageBoxButtons.OK, MessageBoxIcon.Error)
                        Return
                    End If

                    ' [DIAG-CHUNKROOT] Hipótesis: GetGlobalTransform incluye chunkRoot.local
                    ' en su composición. Per BSConnectPointBoneInjector.vb:137-140 el
                    ' chunkRoot.local es "scene-viewer rotation del modelador, NO parte del
                    ' attachment". Si chunkRoot.local NO es identity, V2 (y SKIP) lo metería
                    ' espurio en G_CX / G_B / W_B → rotación/translation extra en render.
                    ' Loguear con-root vs stripped para confirmar la magnitud del impacto.
                    ' Corre PRE-skip así también vemos arms (que van a SKIP).
                    If isRobotMount AndAlso Logger.Enabled Then
                        Try
                            Dim chunkRootNode = shape.NifContent.GetRootNode()
                            Dim chunkRootLocal As Transform_Class
                            If chunkRootNode IsNot Nothing Then
                                chunkRootLocal = New Transform_Class(chunkRootNode)
                            Else
                                chunkRootLocal = New Transform_Class()
                            End If
                            Dim chunkRootIsIdent = chunkRootLocal.Equals(New Transform_Class())
                            Dim invChunkRoot = chunkRootLocal.Inverse()
                            Dim G_CX_stripped = G_CX.ComposeTransforms(invChunkRoot)
                            Dim invGCXStripped = G_CX_stripped.Inverse()
                            Dim A_with = M_mesh.ComposeTransforms(G_CX.Inverse())
                            Dim A_stripped = M_mesh.ComposeTransforms(invGCXStripped)
                            Dim shL_cr = shape.ShapeName, cxL_cr = cxName, isIdL = chunkRootIsIdent
                            Dim crT = chunkRootLocal.Translation, crR = chunkRootLocal.Rotation
                            Dim gcxT = G_CX.Translation, gcxR = G_CX.Rotation
                            Dim gcxStrT = G_CX_stripped.Translation, gcxStrR = G_CX_stripped.Rotation
                            Dim aT_cr = A_with.Translation, aR_cr = A_with.Rotation
                            Dim asT = A_stripped.Translation, asR = A_stripped.Rotation
                            Logger.LogLazy(Function() $"[DIAG-CHUNKROOT] shape='{shL_cr}' cx='{cxL_cr}' chunkRoot.local IDENTITY={isIdL} T=({crT.X:F3},{crT.Y:F3},{crT.Z:F3}) R=[{crR.M11:F3},{crR.M12:F3},{crR.M13:F3}|{crR.M21:F3},{crR.M22:F3},{crR.M23:F3}|{crR.M31:F3},{crR.M32:F3},{crR.M33:F3}]")
                            Logger.LogLazy(Function() $"[DIAG-CHUNKROOT]   G_CX(with-root).T=({gcxT.X:F3},{gcxT.Y:F3},{gcxT.Z:F3}) R=[{gcxR.M11:F3},{gcxR.M12:F3},{gcxR.M13:F3}|{gcxR.M21:F3},{gcxR.M22:F3},{gcxR.M23:F3}|{gcxR.M31:F3},{gcxR.M32:F3},{gcxR.M33:F3}]")
                            Logger.LogLazy(Function() $"[DIAG-CHUNKROOT]   G_CX(stripped).T=({gcxStrT.X:F3},{gcxStrT.Y:F3},{gcxStrT.Z:F3}) R=[{gcxStrR.M11:F3},{gcxStrR.M12:F3},{gcxStrR.M13:F3}|{gcxStrR.M21:F3},{gcxStrR.M22:F3},{gcxStrR.M23:F3}|{gcxStrR.M31:F3},{gcxStrR.M32:F3},{gcxStrR.M33:F3}]")
                            Logger.LogLazy(Function() $"[DIAG-CHUNKROOT]   A(with-root).T=({aT_cr.X:F3},{aT_cr.Y:F3},{aT_cr.Z:F3}) R=[{aR_cr.M11:F3},{aR_cr.M12:F3},{aR_cr.M13:F3}|{aR_cr.M21:F3},{aR_cr.M22:F3},{aR_cr.M23:F3}|{aR_cr.M31:F3},{aR_cr.M32:F3},{aR_cr.M33:F3}]")
                            Logger.LogLazy(Function() $"[DIAG-CHUNKROOT]   A(stripped).T=({asT.X:F3},{asT.Y:F3},{asT.Z:F3}) R=[{asR.M11:F3},{asR.M12:F3},{asR.M13:F3}|{asR.M21:F3},{asR.M22:F3},{asR.M23:F3}|{asR.M31:F3},{asR.M32:F3},{asR.M33:F3}]")
                            For sbiCR = 0 To Math.Min(shape.ShapeBones.Count, shape.ShapeBoneTransforms.Count) - 1
                                Dim niNCR = TryCast(shape.ShapeBones(sbiCR), NiflySharp.Blocks.NiNode)
                                If niNCR Is Nothing Then Continue For
                                Dim bnNmCR = If(niNCR.Name?.String, "")
                                If String.IsNullOrEmpty(bnNmCR) Then Continue For
                                Dim G_B_with = Transform_Class.GetGlobalTransform(niNCR, shape.NifContent)
                                Dim G_B_stripped = G_B_with.ComposeTransforms(invChunkRoot)
                                Dim WB_with = A_with.ComposeTransforms(G_B_with)
                                Dim WB_stripped = A_stripped.ComposeTransforms(G_B_stripped)
                                Dim wT_w = WB_with.Translation, wT_s = WB_stripped.Translation
                                Dim diff = Math.Sqrt((wT_w.X - wT_s.X) ^ 2 + (wT_w.Y - wT_s.Y) ^ 2 + (wT_w.Z - wT_s.Z) ^ 2)
                                Dim shLb = shape.ShapeName, bnLb = bnNmCR, dL = diff
                                Logger.LogLazy(Function() $"[DIAG-CHUNKROOT]     bone='{bnLb}' W_B(with).T=({wT_w.X:F3},{wT_w.Y:F3},{wT_w.Z:F3}) W_B(stripped).T=({wT_s.X:F3},{wT_s.Y:F3},{wT_s.Z:F3}) |diff|={dL:F3}")
                            Next
                        Catch exCR As Exception
                            Dim shL_cr = shape.ShapeName, msgL = exCR.Message
                            Logger.LogLazy(Function() $"[DIAG-CHUNKROOT] shape='{shL_cr}' EXCEPTION: {msgL}")
                        End Try
                    End If

                    ' (discriminador ACTOR-RIG/MODULE-RIG removido: ambas ramas computaban W_B = A × G_B idéntico; una sola rama abajo)

                    Dim invGCX = G_CX.Inverse()
                    ' A = inv(G_CX) × M_mesh in row-vec composition = M_mesh.Compose(invGCX)
                    Dim A = M_mesh.ComposeTransforms(invGCX)

                    Dim reskinCount As Integer = 0
                    Dim skipCount As Integer = 0
                    ' [DEPTH-ORDER] Sortear shape bones por hierarchy depth en actor.skel
                    ' (parent primero) antes de aplicar overrides. Sin esto, cascade drift
                    ' rompe arms con NIF order non-hierarchical.
                    Dim boneList_mod As New List(Of Tuple(Of Integer, NiflySharp.Blocks.NiNode, HierarchiBone_class, Integer))
                    Dim seenBones_mod As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                    For sbi_pre2 = 0 To Math.Min(shape.ShapeBones.Count, shape.ShapeBoneTransforms.Count) - 1
                        Dim niN_pre2 = TryCast(shape.ShapeBones(sbi_pre2), NiflySharp.Blocks.NiNode)
                        If niN_pre2 Is Nothing Then Continue For
                        Dim bnName_pre2 = If(niN_pre2.Name?.String, "")
                        If String.IsNullOrEmpty(bnName_pre2) Then Continue For
                        Dim hb_pre2 As HierarchiBone_class = Nothing
                        If Not targetSkel.SkeletonDictionary.TryGetValue(bnName_pre2, hb_pre2) Then
                            skipCount += 1
                            Continue For
                        End If
                        If seenBones_mod.Add(bnName_pre2) Then
                            Dim depth_pre2 = GetBoneHierarchyDepth(hb_pre2)
                            boneList_mod.Add(Tuple.Create(sbi_pre2, niN_pre2, hb_pre2, depth_pre2))
                        End If
                        ' [CHAIN-INTERMEDIATES] Walk parent chain de esta shape bone via chunk NIF
                        ' tree (GetParentNode). Para cada parent intermedio (hasta C-X), si su
                        ' nombre está en actor.SkeletonDictionary, agregarlo al boneList. Cubre
                        ' HeadAssaultron (HeadNod intermedio entre Neck y HeadTwist en chain[1]),
                        ' y cualquier otro chunk MODULE-RIG con bones intermedios no declarados
                        ' como shape bones. Depth-sort más abajo procesa todo top-down → sin
                        ' cascade drift. Idéntico al patrón ACTOR-RIG.
                        Dim parentNode_mod = TryCast(shape.NifContent.GetParentNode(niN_pre2), NiflySharp.Blocks.NiNode)
                        Dim safetyHops_mod As Integer = 0
                        While parentNode_mod IsNot Nothing AndAlso Not ReferenceEquals(parentNode_mod, cxNode) AndAlso safetyHops_mod < 20
                            Dim parentNm_pre2 = If(parentNode_mod.Name?.String, "")
                            If Not String.IsNullOrEmpty(parentNm_pre2) Then
                                Dim parentHb_pre2 As HierarchiBone_class = Nothing
                                If targetSkel.SkeletonDictionary.TryGetValue(parentNm_pre2, parentHb_pre2) AndAlso seenBones_mod.Add(parentNm_pre2) Then
                                    Dim depthP_pre2 = GetBoneHierarchyDepth(parentHb_pre2)
                                    boneList_mod.Add(Tuple.Create(-1, parentNode_mod, parentHb_pre2, depthP_pre2))
                                End If
                            End If
                            parentNode_mod = TryCast(shape.NifContent.GetParentNode(parentNode_mod), NiflySharp.Blocks.NiNode)
                            safetyHops_mod += 1
                        End While
                    Next
                    ' [CHUNK-TREE-FULL] El árbol del chunk COMPLETO define la distribución de Bethesda:
                    ' además de los skinned bones + sus cadenas, escribir TODO NiNode del chunk NIF que
                    ' exista en el actor (ramas hermanas y el propio C-X). Caso probado: TorsoAssaultron
                    ' trae LClavicle/RClavicle como nodos NO skinneados (ramas de Chest, fuera de las
                    ' cadenas de Spine) con el local DESPLEGADO (5.942,−4.773,2.658) == la constante que
                    ' juegan los clips; sin escribirlos, el despliegue entero del brazo caía como mount
                    ' sobre el primer skinned del chunk de brazo (LClavicleTwist +18.59) — distribución
                    ' que NO es la de Bethesda y dobla la cadena al animar. El C-X (W = A×G_CX = socket
                    ' publicado, ej. P-Head==(12.391,−3.921)==constante del clip) también se escribe:
                    ' el hueso socket VIVE donde su P-X lo publica.
                    For Each blk_mod In shape.NifContent.Blocks
                        Dim treeNode_mod = TryCast(blk_mod, NiflySharp.Blocks.NiNode)
                        If treeNode_mod Is Nothing OrElse treeNode_mod.Name Is Nothing Then Continue For
                        Dim treeNm_mod = If(treeNode_mod.Name.String, "")
                        If String.IsNullOrEmpty(treeNm_mod) Then Continue For
                        ' ⛔ Nodos con sufijo de instancia '|<dígitos>' EXCLUIDOS del tree-walk: los chunks
                        ' multi-instancia (ModTorsoHandyEye/ArmsTypeA1 ×3) comparten UN NIF cuyos nodos
                        ' se llaman '...|0' FIJO — escribirlos por nombre apila las 3 instancias en el
                        ' socket |0 (regresión: ojos mezclados, brazos corridos). Esos huesos los maneja
                        ' el path skinned+cadenas, que sí tiene el mapeo apIdx por instancia.
                        ' Usa el MISMO discriminador '|<dígitos>' que StripInstanceSuffix / apIdx-sub (antes
                        ' era IndexOf("|") crudo = cualquier pipe; verificado 2026-06-14: 0 nombres con
                        ' sufijo no-numérico en toda la data → el cambio es no-regresivo y consistente).
                        If NameUtils.StripInstanceSuffix(treeNm_mod) <> treeNm_mod Then Continue For
                        Dim treeHb_mod As HierarchiBone_class = Nothing
                        If targetSkel.SkeletonDictionary.TryGetValue(treeNm_mod, treeHb_mod) AndAlso seenBones_mod.Add(treeNm_mod) Then
                            boneList_mod.Add(Tuple.Create(-1, treeNode_mod, treeHb_mod, GetBoneHierarchyDepth(treeHb_mod)))
                        End If
                    Next
                    boneList_mod.Sort(Function(x_sort2, y_sort2) x_sort2.Item4.CompareTo(y_sort2.Item4))
                    For Each entry_mod In boneList_mod
                        Dim sbi = entry_mod.Item1
                        Dim niN = entry_mod.Item2
                        Dim actorBhb = entry_mod.Item3
                        Dim boneName = actorBhb.BoneName
                        Dim actor_B_world = actorBhb.OriginalGetGlobalTransform

                        ' [CHUNK-ACCUMULATION] Si bone fue reskin-eado por chunk previo, loggear
                        ' delta entre actor.world (que usamos para corregir) y prev_W_B (donde el
                        ' chunk previo realmente puso el geometry). Si delta ≠ 0, este sub-chunk
                        ' está usando referencia stale.
                        Dim prevWB As Transform_Class = Nothing
                        If isRobotMount AndAlso wbHistory.TryGetValue(boneName, prevWB) AndAlso prevWB IsNot Nothing Then
                            Dim aT0 = actor_B_world.Translation, pT0 = prevWB.Translation
                            Dim dX = pT0.X - aT0.X, dY = pT0.Y - aT0.Y, dZ = pT0.Z - aT0.Z
                            Dim bnL0 = boneName, shL0 = shape.ShapeName
                            Logger.LogLazy(Function() $"[CHUNK-ACCUMULATION] shape='{shL0}' bone='{bnL0}' actor.world=({aT0.X:F3},{aT0.Y:F3},{aT0.Z:F3}) prevChunkWB=({pT0.X:F3},{pT0.Y:F3},{pT0.Z:F3}) delta=({dX:F3},{dY:F3},{dZ:F3})")
                        End If

                        ' G_B desde el chunk NIF tree (no desde inv(bind)).
                        Dim G_B = Transform_Class.GetGlobalTransform(niN, shape.NifContent)
                        ' W_B = G_B × A (in row-vec composition).
                        Dim W_B = A.ComposeTransforms(G_B)

                        ' Acumular W_B en history para que sub-chunks posteriores puedan compararse
                        ' (cascade cross-shape en colección).
                        wbHistory(boneName) = W_B
                        ' [MOUNTDELTA-PLAN] V2 SOLO colecta el plan; el apply lo hace
                        ' ApplyMountPlanForActor en orden topológico tras el shape loop.
                        renderData.MountDesiredWorlds.Add(New MainForm.MountDesiredWorldEntry With {
                            .BoneName = actorBhb.BoneName,
                            .DesiredWorld = W_B,
                            .ContextLabel = "V2-MODULE-" & shape.ShapeName,
                            .TargetSkel = targetSkel
                        })
                        reskinCount += 1
                        If isRobotMount AndAlso Logger.Enabled Then
                            Dim sbiL = sbi, bnL = boneName, shL = shape.ShapeName
                            Dim wBT = W_B.Translation, gBT = G_B.Translation
                            Logger.LogLazy(Function() $"[CHUNK-RESKIN-V2] shape='{shL}' bone[{sbiL}] '{bnL}' G_B.T=({gBT.X:F3},{gBT.Y:F3},{gBT.Z:F3}) W_B.T=({wBT.X:F3},{wBT.Y:F3},{wBT.Z:F3}) → plan entry collected")
                        End If
                    Next
                    If isRobotMount AndAlso Logger.Enabled Then
                        Dim rcL = reskinCount, skL = skipCount, shL = shape.ShapeName, cxL = cxName
                        Dim AT = A.Translation, GCXT = G_CX.Translation
                        Logger.LogLazy(Function() $"[CHUNK-RESKIN-V2] shape='{shL}' cx='{cxL}' G_CX.T=({GCXT.X:F3},{GCXT.Y:F3},{GCXT.Z:F3}) A.T=({AT.X:F3},{AT.Y:F3},{AT.Z:F3}) summary: reskin={rcL} skip={skL}")
                    End If
                ElseIf isRobotMount AndAlso Logger.Enabled Then
                    Dim shL = shape.ShapeName, cxL = cxName
                    Logger.LogLazy(Function() $"[CHUNK-RESKIN-V2] shape='{shL}' cx='{cxL}' SKIP: C-X NiNode not found in chunk NIF tree")

                    ' [DIAG-BIND-BAKE] Para chunks SKIP (sin C-X), comparar inv(bind) vs actor.bone.world
                    ' vs actor.parent_bone.world × socket.local (M_mesh). Permite ver empíricamente si el
                    ' bind tiene baked SOLO bone (Assaultron, needs fix) o bone+socket (Protectron, OK as-is).
                    Try
                        Dim parentBoneHbDiag As HierarchiBone_class = Nothing
                        If targetSkel.SkeletonDictionary.TryGetValue(socket.ParentBoneName, parentBoneHbDiag) Then
                            Dim parentBoneWorldDiag = parentBoneHbDiag.OriginalGetGlobalTransform
                            Dim socketLocalDiag As New Transform_Class With {
                                .Translation = socket.Translation,
                                .Rotation = BSConnectPointReader.QuatToMatrix33(socket.Rotation),
                                .Scale = If(socket.Scale > 0.0F, socket.Scale, 1.0F)
                            }
                            Dim mMeshDiag = parentBoneWorldDiag.ComposeTransforms(socketLocalDiag)
                            Dim mmT_outer = mMeshDiag.Translation

                            For sbiD = 0 To Math.Min(shape.ShapeBones.Count, shape.ShapeBoneTransforms.Count) - 1
                                Dim niN = TryCast(shape.ShapeBones(sbiD), NiflySharp.Blocks.NiNode)
                                If niN Is Nothing Then Continue For
                                Dim boneName = If(niN.Name?.String, "")
                                If String.IsNullOrEmpty(boneName) Then Continue For
                                Dim bind = shape.ShapeBoneTransforms(sbiD)
                                If bind Is Nothing Then Continue For
                                Dim bindT As New Transform_Class With {
                                    .Translation = bind.Translation,
                                    .Rotation = bind.Rotation,
                                    .Scale = bind.Scale,
                                    .ScaleVector = bind.ScaleVector
                                }
                                Dim invBind = bindT.Inverse()
                                Dim invBindT = invBind.Translation
                                Dim actorBhbDiag As HierarchiBone_class = Nothing
                                Dim hasActor = targetSkel.SkeletonDictionary.TryGetValue(boneName, actorBhbDiag)
                                Dim aBT As System.Numerics.Vector3 = If(hasActor, actorBhbDiag.OriginalGetGlobalTransform.Translation, New System.Numerics.Vector3(0, 0, 0))
                                Dim dT_bone As Double = If(hasActor,
                                    Math.Sqrt((invBindT.X - aBT.X) ^ 2 + (invBindT.Y - aBT.Y) ^ 2 + (invBindT.Z - aBT.Z) ^ 2),
                                    Double.NaN)
                                Dim dT_mmesh As Double = Math.Sqrt((invBindT.X - mmT_outer.X) ^ 2 + (invBindT.Y - mmT_outer.Y) ^ 2 + (invBindT.Z - mmT_outer.Z) ^ 2)
                                Dim verdict As String
                                If Not hasActor Then
                                    verdict = "actor.bone NOT-IN-DICT"
                                ElseIf dT_bone < 1.0 AndAlso dT_mmesh > 1.0 Then
                                    verdict = "BIND≈ACTOR.BONE (sin socket; chunk-frame literal)"
                                ElseIf dT_mmesh < 1.0 AndAlso dT_bone > 1.0 Then
                                    verdict = "BIND≈M_MESH (socket baked; renders OK as-is)"
                                ElseIf dT_bone < dT_mmesh Then
                                    verdict = "CLOSER-TO-BONE"
                                Else
                                    verdict = "CLOSER-TO-M_MESH"
                                End If
                                Dim shLD = shape.ShapeName, bnLD = boneName, ibTL = invBindT, aBTL = aBT, mmTL = mmT_outer, dTbL = dT_bone, dTmL = dT_mmesh, vrL = verdict
                                Logger.LogLazy(Function() $"[DIAG-BIND-BAKE] shape='{shLD}' bone='{bnLD}' inv(bind).T=({ibTL.X:F3},{ibTL.Y:F3},{ibTL.Z:F3}) actor.bone.T=({aBTL.X:F3},{aBTL.Y:F3},{aBTL.Z:F3}) M_mesh.T=({mmTL.X:F3},{mmTL.Y:F3},{mmTL.Z:F3}) dT_bone={dTbL:F3} dT_mmesh={dTmL:F3} → {vrL}")
                            Next
                        Else
                            Dim shLD2 = shape.ShapeName, pbnL = socket.ParentBoneName
                            Logger.LogLazy(Function() $"[DIAG-BIND-BAKE] shape='{shLD2}' SKIP: parent bone '{pbnL}' NOT-IN-DICT")
                        End If
                    Catch exBb As Exception
                        Dim shLD3 = shape.ShapeName, msgL = exBb.Message
                        Logger.LogLazy(Function() $"[DIAG-BIND-BAKE] shape='{shLD3}' EXCEPTION: {msgL}")
                    End Try
                End If
            ElseIf isRobotMount AndAlso Logger.Enabled Then
                Dim shL = shape.ShapeName
                Logger.LogLazy(Function() $"[CHUNK-RESKIN-V2] shape='{shL}' SKIP: chunk has no BSConnectPoint::Children")
            End If
        Catch ex As Exception
            Dim shL = shape.ShapeName, exL = ex
            Logger.LogLazy(Function() $"[CHUNK-RESKIN-V2] shape='{shL}' EXCEPTION: {exL.GetType().Name}: {exL.Message}")
        End Try
    End Sub

    ''' <summary>Override actor bone world position to match <paramref name="desiredWorld"/>
    ''' (donde el chunk quiere el bone) escribiendo <c>MountDeltaTransform</c> sin mutar el
    ''' bind original. Children del bone cascadean automáticamente via parent chain.
    ''' Matemática: <c>newLocal = inv(parent.OriginalGetGlobalTransform) × desiredWorld</c>;
    ''' <c>MountDelta = inv(OrigL) × newLocal</c>. La ANIMACIÓN no pelea con esto:
    ''' <c>local = M × L_anim</c> con <c>M = (O×Mount) × inv(clipBase)</c>, y HkxPoseImportSession
    ''' mide del propio clip su frame de autoría (clips autoreados sobre el rig → M=Mount, el mount
    ''' persiste al animar; clips autoreados sobre el ensamblado → M=I, el clip ya trae el mount).</summary>
    Private Sub OverrideActorBoneWorld(hb As HierarchiBone_class,
                                        desiredWorld As Transform_Class,
                                        contextLabel As String)
        If hb Is Nothing OrElse desiredWorld Is Nothing Then Return
        Dim currentWorld = hb.OriginalGetGlobalTransform
        Dim cT = currentWorld.Translation, dT = desiredWorld.Translation
        Dim diff = Math.Sqrt((cT.X - dT.X) ^ 2 + (cT.Y - dT.Y) ^ 2 + (cT.Z - dT.Z) ^ 2)
        Dim parentWorld As Transform_Class
        If hb.Parent IsNot Nothing Then
            parentWorld = hb.Parent.OriginalGetGlobalTransform
        Else
            parentWorld = New Transform_Class()
        End If
        Dim newLocal = parentWorld.Inverse().ComposeTransforms(desiredWorld)
        Dim newMountDelta = hb.OriginalLocaLTransform.Inverse().ComposeTransforms(newLocal)
        ' El conflicto real "2 chunks → 1 hueso" se detecta fail-loud en ApplyMountPlanForActor (duplicado
        ' within-pass). Acá ya NO se loguea el caso cross-pase (hb.MountDeltaTransform de un render previo),
        ' que NO es conflicto sino re-aplicación normal por pose/re-render.
        hb.MountDeltaTransform = newMountDelta
        Dim bnL = hb.BoneName, ctxL = contextLabel, ctL = cT, dL = dT, diL = diff, mdT = newMountDelta.Translation
        Logger.LogLazy(Function() $"[MOUNTDELTA-WRITE] bone='{bnL}' ctx='{ctxL}' was.world.T=({ctL.X:F3},{ctL.Y:F3},{ctL.Z:F3}) → wants.world.T=({dL.X:F3},{dL.Y:F3},{dL.Z:F3}) diff={diL:F3} MountDelta.T=({mdT.X:F3},{mdT.Y:F3},{mdT.Z:F3})")
    End Sub

    ''' <summary>Nombres de los huesos que ALGUNA shape renderizada usa como skin-bone (geometría depende
    ''' de ellos). Se usa para distinguir un conflicto de mount que IMPORTA (skin-bone con malla) de uno
    ''' sobre un marker SIN malla (ej. ProjectileNode escrito por el tree-walk como bone[-1], que no
    ''' afecta el render aunque varios chunks lo quieran en lugares distintos).</summary>
    Private Shared Function BuildRenderedSkinBoneNames(renderData As MainForm.PreviewResolutionResult) As HashSet(Of String)
        Dim s As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If renderData Is Nothing OrElse renderData.Shapes Is Nothing Then Return s
        For Each sh In renderData.Shapes
            If sh Is Nothing OrElse sh.ShapeBones Is Nothing Then Continue For
            For Each b In sh.ShapeBones
                Dim niN = TryCast(b, NiflySharp.Blocks.NiNode)
                Dim nm = niN?.Name?.String
                If Not String.IsNullOrEmpty(nm) Then s.Add(nm)
            Next
        Next
        Return s
    End Function

    ''' <summary>Aplicador canónico ÚNICO del plan de mount. Recorre el plan
    ''' <c>renderData.MountDesiredWorlds</c> (orden topológico) y escribe <c>MountDeltaTransform</c>
    ''' vía <see cref="OverrideActorBoneWorld"/>. Patrón: <c>ApplyPose → ApplyMountPlanForActor</c>.
    ''' Per-instance scope vía TargetSkel.</summary>
    ''' <summary>[NO-ANIM-SYNC] Copia los flags NiAVObject No Anim Sync X/Y/Z/S (bits 16-19 de Flags_ui) de CADA
    ''' NiNode de los chunk NIFs (incluye connect-points) al hueso vivo homónimo (NoAnimSyncMask). Limpia primero
    ''' (stale de otro NPC en el mismo SkeletonInstance). Lo lee BuildPose para honrar la traslación estructural.</summary>
    Private Sub PlumbNoAnimSyncMasks(inst As SkeletonInstance, renderData As MainForm.PreviewResolutionResult)
        If inst Is Nothing OrElse inst.SkeletonDictionary Is Nothing Then Return
        For Each hb In inst.SkeletonDictionary.Values
            If hb IsNot Nothing Then hb.NoAnimSyncMask = 0
        Next
        If renderData Is Nothing OrElse renderData.Shapes Is Nothing Then Return
        For Each shape In renderData.Shapes
            Dim nif = TryCast(shape, NifRenderableShape)
            If nif Is Nothing OrElse nif.NifContent Is Nothing OrElse nif.NifContent.Blocks Is Nothing Then Continue For
            For Each blk In nif.NifContent.Blocks
                Dim ndn = TryCast(blk, NiflySharp.Blocks.NiNode)
                Dim ndName = ndn?.Name?.String
                If String.IsNullOrEmpty(ndName) Then Continue For
                Dim hbN As HierarchiBone_class = Nothing
                If Not inst.SkeletonDictionary.TryGetValue(ndName, hbN) OrElse hbN Is Nothing Then Continue For
                Dim f = ndn.Flags_ui
                Dim m As Byte = 0
                If (f And &H10000UI) <> 0 Then m = CByte(m Or 1)
                If (f And &H20000UI) <> 0 Then m = CByte(m Or 2)
                If (f And &H40000UI) <> 0 Then m = CByte(m Or 4)
                If (f And &H80000UI) <> 0 Then m = CByte(m Or 8)
                hbN.NoAnimSyncMask = m
            Next
        Next
    End Sub

    Friend Sub ApplyMountPlanForActor(inst As SkeletonInstance, renderData As MainForm.PreviewResolutionResult)
        If inst Is Nothing OrElse renderData Is Nothing Then Return
        ' [NO-ANIM-SYNC] Plumar SIEMPRE el mask del chunk NIF → hueso vivo (BuildPose lo honra). Independiente del mount.
        PlumbNoAnimSyncMasks(inst, renderData)
        If renderData.MountDesiredWorlds Is Nothing OrElse renderData.MountDesiredWorlds.Count = 0 Then Return

        Dim writtenCount As Integer = 0
        Dim skippedNoBone As Integer = 0
        Dim skippedScopeMismatch As Integer = 0
        ' [DEPTH-ORDER GLOBAL] Aplicar PADRE-PRIMERO sobre el plan entero: con entradas cross-shape
        ' (el árbol del torso trae LClavicle, el chunk de brazo trae sus huesos) el orden de colección
        ' no garantiza topología. OverrideActorBoneWorld computa el local contra el world ACTUAL del
        ' parent: si un hijo se aplica antes que su padre, el cascade posterior del padre lo corre.
        Dim applyList As New List(Of (Entry As MainForm.MountDesiredWorldEntry, Bone As HierarchiBone_class, Depth As Integer))
        For Each entry In renderData.MountDesiredWorlds
            If entry Is Nothing OrElse String.IsNullOrEmpty(entry.BoneName) Then Continue For
            If entry.TargetSkel IsNot Nothing AndAlso entry.TargetSkel IsNot inst Then
                skippedScopeMismatch += 1
                Continue For
            End If
            Dim hb As HierarchiBone_class = Nothing
            If Not inst.SkeletonDictionary.TryGetValue(entry.BoneName, hb) OrElse hb Is Nothing Then
                skippedNoBone += 1
                Continue For
            End If
            applyList.Add((entry, hb, GetBoneHierarchyDepth(hb)))
        Next
        ' Sort ESTABLE por depth: entre entradas del mismo bone (last-write-wins del plan) se
        ' conserva el orden de colección.
        Dim applyOrdered = applyList.OrderBy(Function(x) x.Depth).ToList()
        ' [MOUNTDELTA-CONFLICT-IMPOSIBLE] Fail-loud (como Path B). Conflicto que IMPORTA = 2+ entradas del
        ' plan quieren el mismo hueso en lugares DISTINTOS (diff>0.5) EN ESTE pase, Y el hueso tiene
        ' GEOMETRÍA (es skin-bone de alguna shape renderizada). Dos filtros contra falsos positivos:
        '   (1) diff>0.5  → descarta el caso benigno multi-part / multi-instancia (varias shapes del mismo
        '       chunk al mismo hueso con el MISMO W_B, ej. Mr Handy EyeArm1|N: brazo+iris+lente → idéntico).
        '   (2) skin-bone → descarta markers SIN malla escritos por el tree-walk como bone[-1] (ej.
        '       ProjectileNode = boca del arma: en un CreateABot 3 armas lo quieren en 3 lugares, pero NO
        '       hay geometría ahí → no afecta el render). Verificado 2026-06-14 con NPC 0x0100FF0A.
        ' (within-pass Dictionary fresco por llamada → NO confunde el re-render cross-pase. last-write-wins
        ' se sigue aplicando.)
        Dim appliedWorlds As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        Dim skinBoneNames As HashSet(Of String) = Nothing ' lazy: se construye solo en el 1er diff>0.5
        For Each item In applyOrdered
            Dim prevDesired As Transform_Class = Nothing
            If appliedWorlds.TryGetValue(item.Bone.BoneName, prevDesired) AndAlso prevDesired IsNot Nothing AndAlso item.Entry.DesiredWorld IsNot Nothing Then
                Dim pT = prevDesired.Translation, nT = item.Entry.DesiredWorld.Translation
                Dim dd As Double = Math.Sqrt((pT.X - nT.X) ^ 2 + (pT.Y - nT.Y) ^ 2 + (pT.Z - nT.Z) ^ 2)
                If dd > 0.5 Then
                    If skinBoneNames Is Nothing Then skinBoneNames = BuildRenderedSkinBoneNames(renderData)
                    If skinBoneNames.Contains(item.Bone.BoneName) Then
                        Dim bnDup As String = item.Bone.BoneName, ctxDup As String = If(item.Entry.ContextLabel, "?"), ddL As Double = dd
                        Logger.LogLazy(Function() $"[MOUNTDELTA-CONFLICT-IMPOSIBLE] bone='{bnDup}' ctx='{ctxDup}' diff={ddL:F3} — 2 chunks quieren el mismo SKIN-BONE en lugares DISTINTOS")
                        System.Windows.Forms.MessageBox.Show("MOUNTDELTA CONFLICT IMPOSIBLE — no debería pasar." & vbCrLf & vbCrLf &
                                        "bone = " & bnDup & vbCrLf &
                                        "ctx  = " & ctxDup & vbCrLf &
                                        "diff = " & dd.ToString("F2") & vbCrLf & vbCrLf &
                                        "2 chunks quieren el MISMO skin-bone (con geometría) en lugares DISTINTOS." & vbCrLf &
                                        "Aplica last-write-wins. La regla canónica sería 'gana el host que publica el hueso'.",
                                        "MountDelta conflict imposible — REVISAR", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Else
                        Dim bnMk As String = item.Bone.BoneName, ctxMk As String = If(item.Entry.ContextLabel, "?"), ddMk As Double = dd
                        Logger.LogLazy(Function() $"[MOUNTDELTA-MARKER-CONFLICT] bone='{bnMk}' ctx='{ctxMk}' diff={ddMk:F3} — sin geometría (no skin-bone), no afecta render, silenciado")
                    End If
                End If
            End If
            appliedWorlds(item.Bone.BoneName) = item.Entry.DesiredWorld
            OverrideActorBoneWorld(item.Bone, item.Entry.DesiredWorld, item.Entry.ContextLabel & "-APPLY")
            writtenCount += 1
        Next

        Dim instBonesL = inst.SkeletonDictionary.Count, cacheL = renderData.MountDesiredWorlds.Count
        Dim writtenL = writtenCount, skippedL = skippedNoBone, skippedScopeL = skippedScopeMismatch
        Logger.LogLazy(Function() $"[MOUNTDELTA-PREPASS] inst.bones={instBonesL} cache.entries={cacheL} written={writtenL} skipped(boneNotInDict)={skippedL} skipped(scopeMismatch)={skippedScopeL}")
    End Sub

    ''' <summary>Resuelve la "posición efectiva" de un bone en el actor world: si un parent
    ''' chunk corrió V2 sobre ese bone (su W_B vive en chunkWBHistory), devuelve W_B. Sino,
    ''' devuelve actor.bone.world del SkeletonDictionary. Identity si el bone no existe.
    '''
    ''' Esta es la pieza central de la unificación matemática V2 / PROPAGATE-V2 /
    ''' PROPAGATE-V2-ANCHOR. Las 3 fixes computan correction = inv(actor.B.world) × desired_W(B),
    ''' donde desired_W(B) = ResolveEffectiveWorld(B). La fórmula es idéntica; la diferencia
    ''' entre fixes es solo dónde se aplica el correction (bind del shape vs anchor.local del
    ''' chunk vs nuevo bind via V2 reskin).</summary>
    Private Function ResolveEffectiveWorld(chunkWBHistory As Dictionary(Of String, Transform_Class),
                                            inst As SkeletonInstance,
                                            boneName As String) As Transform_Class
        If chunkWBHistory IsNot Nothing AndAlso Not String.IsNullOrEmpty(boneName) Then
            Dim wb As Transform_Class = Nothing
            If chunkWBHistory.TryGetValue(boneName, wb) AndAlso wb IsNot Nothing Then
                Return wb
            End If
        End If
        If inst IsNot Nothing AndAlso Not String.IsNullOrEmpty(boneName) Then
            Dim hb As HierarchiBone_class = Nothing
            If inst.SkeletonDictionary.TryGetValue(boneName, hb) AndAlso hb IsNot Nothing Then
                Return hb.OriginalGetGlobalTransform
            End If
        End If
        Return New Transform_Class()
    End Function

    ''' <summary>Host-scoped resolution del MountSocket de un robot chunk. Walkea la cadena
    ''' de hosts: <c>host inmediato → host del host → ... → skeleton root</c>. En cada nivel
    ''' busca el socket en el namespace local del publisher (BSConnectPoint::Parents que ese
    ''' chunk publica). Si no aparece en ningún host de la cadena, cae al <c>skeletonSockets</c>
    ''' (SRC1+SRC2: RACE.ANAM + BPTD.MODL).
    '''
    ''' Reemplaza la resolución flat global que mezclaba STATIC skeleton + per-chunk publishers
    ''' en un único <c>SocketsDictionary</c> y forzaba políticas FIRST-WINS/CHUNK-WINS para
    ''' decidir conflicts artificiales que en realidad eran namespaces distintos. Caso vivo:
    ''' Assaultron Torso publica P-ArmRight con T=(8.666, ...) acomodado a sus hombros
    ''' estrechos; el skeleton vanilla publica P-ArmRight con T=(18.772, ...) genérico
    ''' humanoide. Con host-scoped, ArmRightAssaultron (host = TorsoAssaultron) resuelve
    ''' contra el T=(8.666) del torso publisher → brazo encastra. El skeleton sólo se
    ''' consulta si NINGÚN host de la cadena publicó P-ArmRight.</summary>
    Friend Function ResolveMountSocketHostScoped(apEditorId As String,
                                                  apIdx As Byte,
                                                  hostOrdinal As Integer,
                                                  publisherSockets As Dictionary(Of UInteger, Dictionary(Of String, MainForm.PublisherSocketInfo)),
                                                  hostChainMap As Dictionary(Of Integer, Integer),
                                                  resolution As ObjectTemplateResolver.CombinationResolution,
                                                  skeletonSockets As Dictionary(Of String, BSConnectPointReader.ConnectPointInfo),
                                                  ByRef resolvedInfo As MainForm.PublisherSocketInfo,
                                                  ByRef matchedHostOrdinal As Integer,
                                                  ByRef matchedHostFormID As UInteger,
                                                  ByRef matchedHostApIdx As Byte) _
                                                  As BSConnectPointReader.ConnectPointInfo
        resolvedInfo = Nothing
        matchedHostOrdinal = 0
        matchedHostFormID = 0UI
        matchedHostApIdx = 0
        If String.IsNullOrEmpty(apEditorId) Then
            Logger.LogLazy(Function() $"[MOUNT-LOOKUP-HS] apEditorId='' → NOT-FOUND (KYWD not resolvable)")
            Return Nothing
        End If
        Dim baseName = apEditorId
        Dim stripped As String = ""
        If baseName.StartsWith("ap_Bot_", StringComparison.OrdinalIgnoreCase) Then
            stripped = "ap_Bot_"
            baseName = baseName.Substring("ap_Bot_".Length)
        ElseIf baseName.StartsWith("ap_", StringComparison.OrdinalIgnoreCase) Then
            stripped = "ap_"
            baseName = baseName.Substring("ap_".Length)
        End If
        Dim indexed = $"P-{baseName}|{apIdx}"
        Dim plain = $"P-{baseName}"
        Dim apEditorIdLog = apEditorId, baseNameLog = baseName, strippedLog = stripped
        Dim indexedLog = indexed, plainLog = plain, apIdxLog = apIdx

        ' Walk host chain por ORDINAL runtime. Safety cap contra ciclo (no debería ocurrir
        ' — ordinals son monotónicos, no se pueden ciclar, pero defensivo).
        Dim currentOrd As Integer = hostOrdinal
        Dim hops As Integer = 0
        Const maxHops As Integer = 32
        Dim chainTrace As New System.Text.StringBuilder()
        While currentOrd <> 0 AndAlso hops < maxHops
            ' Lookup FormID del OMOD para este ordinal via resolution parallel arrays.
            ' Necesario porque publisherSockets sigue keyeado por FormID (asset-level —
            ' los sockets son propiedad del NIF, idénticos entre instancias del mismo asset).
            Dim currentFid As UInteger = 0UI
            For idx = 0 To resolution.IncludedOmodInstanceOrdinal.Count - 1
                If resolution.IncludedOmodInstanceOrdinal(idx) = currentOrd Then
                    Dim om = resolution.IncludedOmods(idx)
                    If om IsNot Nothing Then currentFid = om.FormID
                    Exit For
                End If
            Next
            chainTrace.Append($"→(ord={currentOrd},0x{currentFid:X8})")
            Dim hostMap As Dictionary(Of String, MainForm.PublisherSocketInfo) = Nothing
            If currentFid <> 0UI AndAlso publisherSockets.TryGetValue(currentFid, hostMap) Then
                Dim info As MainForm.PublisherSocketInfo = Nothing
                If hostMap.TryGetValue(indexed, info) Then
                    resolvedInfo = info
                    matchedHostOrdinal = currentOrd
                    matchedHostFormID = currentFid
                    ' Lookup apIdx via parallel arrays para logging legible.
                    For idx2 = 0 To resolution.IncludedOmodInstanceOrdinal.Count - 1
                        If resolution.IncludedOmodInstanceOrdinal(idx2) = currentOrd Then
                            matchedHostApIdx = resolution.IncludedOmodApIdx(idx2)
                            Exit For
                        End If
                    Next
                    Dim ordL = currentOrd, fidL = currentFid, traceL = chainTrace.ToString()
                    Logger.LogLazy(Function() $"[MOUNT-LOOKUP-HS] apEditorId='{apEditorIdLog}' apIdx={apIdxLog} base='{baseNameLog}' chain={traceL} → MATCH '{indexedLog}' at host=(ord={ordL},0x{fidL:X8}) parent='{info.Socket.ParentBoneName}' parentFoundInHost={info.ParentFoundInHostNif}")
                    Return info.Socket
                End If
                If hostMap.TryGetValue(plain, info) Then
                    resolvedInfo = info
                    matchedHostOrdinal = currentOrd
                    matchedHostFormID = currentFid
                    For idx2 = 0 To resolution.IncludedOmodInstanceOrdinal.Count - 1
                        If resolution.IncludedOmodInstanceOrdinal(idx2) = currentOrd Then
                            matchedHostApIdx = resolution.IncludedOmodApIdx(idx2)
                            Exit For
                        End If
                    Next
                    Dim ordL = currentOrd, fidL = currentFid, traceL = chainTrace.ToString()
                    Logger.LogLazy(Function() $"[MOUNT-LOOKUP-HS] apEditorId='{apEditorIdLog}' apIdx={apIdxLog} base='{baseNameLog}' chain={traceL} → MATCH '{plainLog}' at host=(ord={ordL},0x{fidL:X8}) parent='{info.Socket.ParentBoneName}' parentFoundInHost={info.ParentFoundInHostNif}")
                    Return info.Socket
                End If
            End If
            Dim parentOrd As Integer = 0
            hostChainMap.TryGetValue(currentOrd, parentOrd)
            currentOrd = parentOrd
            hops += 1
        End While

        ' Fallback: skeleton root (SRC1+SRC2). resolvedInfo queda Nothing → consumer cae
        ' al Path B fallback en V2 (actor.parentBone × socket.local con ResolveEffectiveWorld).
        Dim sk As BSConnectPointReader.ConnectPointInfo = Nothing
        If skeletonSockets.TryGetValue(indexed, sk) Then
            Dim traceL = chainTrace.ToString()
            Logger.LogLazy(Function() $"[MOUNT-LOOKUP-HS] apEditorId='{apEditorIdLog}' apIdx={apIdxLog} base='{baseNameLog}' chain={traceL}→skeleton → MATCH '{indexedLog}' parent='{sk.ParentBoneName}'")
            Return sk
        End If
        If skeletonSockets.TryGetValue(plain, sk) Then
            Dim traceL = chainTrace.ToString()
            Logger.LogLazy(Function() $"[MOUNT-LOOKUP-HS] apEditorId='{apEditorIdLog}' apIdx={apIdxLog} base='{baseNameLog}' chain={traceL}→skeleton → MATCH '{plainLog}' parent='{sk.ParentBoneName}'")
            Return sk
        End If
        Dim traceFinal = chainTrace.ToString()
        Logger.LogLazy(Function() $"[MOUNT-LOOKUP-HS] apEditorId='{apEditorIdLog}' apIdx={apIdxLog} base='{baseNameLog}' chain={traceFinal}→skeleton → NOT-FOUND (tried '{indexedLog}' and '{plainLog}' in every host + skeleton)")
        Return Nothing
    End Function

    ''' <summary>Compute (si no está cacheado) y devuelve cand.ChunkToActor para un robot
    ''' chunk. Recursivo — Path A consulta host.ChunkToActor, que si no está set se computa
    ''' lazy via EnsureChunkToActor(host). Esto desacopla el compute de ChunkToActor del
    ''' shape materialization: un host que publica sockets pero no emite shapes propias
    ''' (caso "host publisher sin shapes") nunca dispara JIT por shape loop, pero recibe
    ''' ChunkToActor cuando algún descendant con shapes lo requiere via recursión.
    '''
    ''' Cycle detection: <paramref name="visiting"/> set DFS coloring. Push del ordinal al
    ''' entrar (Try); pop al salir (Finally). Si recursión llega a ordinal ya en visiting
    ''' es ciclo real (loggeado, fallback Path B sin host). Cap defensivo 32 hops también.
    '''
    ''' Devuelve cand.ChunkToActor (o Nothing si compute falla — cand queda sin ChunkToActor).</summary>
    Friend Function EnsureChunkToActor(cand As MainForm.MeshCandidate,
                                         candByOrdinal As Dictionary(Of Integer, MainForm.MeshCandidate),
                                         renderData As MainForm.PreviewResolutionResult,
                                         targetSkel As SkeletonInstance,
                                         wbHistory As Dictionary(Of String, Transform_Class),
                                         visiting As HashSet(Of Integer)) As Transform_Class
        If cand Is Nothing Then Return Nothing
        If cand.ChunkToActor IsNot Nothing Then Return cand.ChunkToActor
        If cand.MountSocket Is Nothing Then Return Nothing

        Dim ordSelf = cand.ChunkInstanceOrdinal
        If ordSelf <> 0 AndAlso visiting.Contains(ordSelf) Then
            ' Ciclo detectado — el ordinal actual ya está siendo computado más arriba en
            ' la recursión. Log y NO recursar.
            Dim ordL = ordSelf, nmL = If(cand.MountSocket?.Name, "?")
            Logger.LogLazy(Function() $"[A_HOST-CYCLE] ord={ordL} socket='{nmL}' — ciclo detectado en host chain (DFS visiting set hit), no recursar; ChunkToActor queda Nothing")
            Return Nothing
        End If

        If ordSelf <> 0 Then visiting.Add(ordSelf)
        Try
            ' Resolver host's ChunkToActor recursivamente si Path A puede aplicar.
            Dim hostA As Transform_Class = Nothing
            Dim usedPathA As Boolean = False
            Dim hostCand As MainForm.MeshCandidate = Nothing
            If cand.ParentFoundInMatchedHostNif AndAlso cand.ResolvedHostSocketGlobalT IsNot Nothing AndAlso cand.MatchedHostInstanceOrdinal <> 0 Then
                candByOrdinal.TryGetValue(cand.MatchedHostInstanceOrdinal, hostCand)
                If hostCand IsNot Nothing Then
                    hostA = EnsureChunkToActor(hostCand, candByOrdinal, renderData, targetSkel, wbHistory, visiting)
                End If
            End If

            ' Resolver G_CX desde chunk NIF — necesario en ambos paths.
            Dim chunkNif As Nifcontent_Class_Manolo = Nothing
            If Not renderData.CandidateNif.TryGetValue(cand, chunkNif) OrElse chunkNif Is Nothing Then
                Dim ordL_dbg = cand.ChunkInstanceOrdinal, nmL_dbg = If(cand.MountSocket?.Name, "?"), fidL_dbg = cand.ChunkOmodFormID
                Logger.LogLazy(Function() $"[A_HOST-JIT-EARLY] ord={ordL_dbg} fid=0x{fidL_dbg:X8} socket='{nmL_dbg}' reason=CandidateNif-miss")
                Return Nothing
            End If
            Dim socketNm = If(cand.MountSocket.Name, "")
            If String.IsNullOrEmpty(socketNm) OrElse socketNm.Length <= 2 Then
                Dim ordL_dbg = cand.ChunkInstanceOrdinal, fidL_dbg = cand.ChunkOmodFormID
                Logger.LogLazy(Function() $"[A_HOST-JIT-EARLY] ord={ordL_dbg} fid=0x{fidL_dbg:X8} socket='{socketNm}' reason=socket-name-too-short")
                Return Nothing
            End If
            Dim cxNm As String = BSConnectPointBoneInjector_Class.TryGetSocketCounterpartName(socketNm)
            If String.IsNullOrEmpty(cxNm) Then
                Dim ordL_dbg = cand.ChunkInstanceOrdinal, fidL_dbg = cand.ChunkOmodFormID
                Logger.LogLazy(Function() $"[A_HOST-JIT-EARLY] ord={ordL_dbg} fid=0x{fidL_dbg:X8} socket='{socketNm}' reason=cxNm-empty (socket sin prefix P-/P_)")
                Return Nothing
            End If
            Dim cxNode = chunkNif.FindBlockByName(Of NiflySharp.Blocks.NiNode)(cxNm)
            If cxNode Is Nothing Then
                ' Strip-on-NIF-side fallback: chunks multi-instance comparten el MISMO NIF
                ' (mismo OMOD asset, distintos apIdx publisher-side). El NIF tiene UN único
                ' C-X NiNode (típicamente con sufijo apIdx fijo authoreado, p.ej. `C-X|0`).
                ' Cuando el resolver da socket `P-X|2` → cxNm=`C-X|2` exact no matchea el NIF
                ' que tiene `C-X|0`. Regla: cualquier NiNode cuyo base (pre-`|`) coincida con
                ' cxNm base es el mismo socket — el sufijo numérico es índice publisher, no
                ' parte del nombre semántico. Esto cubre Codsworth Bot_ModTorsoHandyEye1B
                ' apIdx=1/2 (NIF tiene C-ModSlotB|0, socket pide |1 o |2 → strip a C-ModSlotB
                ' en ambos lados → match). Paridad con la lógica StripSfx del V2 legacy
                ' (líneas ~2703-2712 inline en shape loop pre-refactor).
                Dim cxNormSearch = NameUtils.StripInstanceSuffix(cxNm)
                For Each blk In chunkNif.Blocks
                    Dim candBlk = TryCast(blk, NiflySharp.Blocks.NiNode)
                    If candBlk Is Nothing Then Continue For
                    Dim candNm = If(candBlk.Name?.String, "")
                    If String.Equals(NameUtils.StripInstanceSuffix(candNm), cxNormSearch, StringComparison.OrdinalIgnoreCase) Then
                        cxNode = candBlk
                        Exit For
                    End If
                Next
            End If
            If cxNode Is Nothing Then
                ' Chunk no tiene C-X NiNode interno — caso "attachment-style" (mesh skinned
                ' directamente a un bone parent del actor sin chunk-internal coord system).
                ' Path A no aplica; el chunk render va por el path INJECT/legacy con
                ' SkeletonFallbackSocket en el shape loop (SOCKET-EFFECTIVE-OVERRIDE).
                Dim ordL_dbg = cand.ChunkInstanceOrdinal, fidL_dbg = cand.ChunkOmodFormID, sNmL_dbg = socketNm, cxNmL_dbg = cxNm
                Logger.LogLazy(Function() $"[A_HOST-JIT-EARLY] ord={ordL_dbg} fid=0x{fidL_dbg:X8} socket='{sNmL_dbg}' cxNm='{cxNmL_dbg}' reason=cxNode-not-found-in-chunk-NIF (attachment-style chunk, render via legacy INJECT path)")
                Return Nothing
            End If
            Dim G_CX = Transform_Class.GetGlobalTransform(cxNode, chunkNif)

            ' Path A si host A está computado.
            Dim M_mesh As Transform_Class = Nothing
            Dim pathBSource As String = ""
            If hostA IsNot Nothing Then
                M_mesh = hostA.ComposeTransforms(cand.ResolvedHostSocketGlobalT)
                usedPathA = True
            Else
                ' [PATH B — SOCKET SOURCE SEPARATION] Per OpenAI Vuelta 17: el publisher chunk
                ' socket usa chunk-internal naming (parent='Arm1' sin suffix), pero Path B
                ' resuelve parent contra actor.skel que tiene indexed (Arm1|0/1/2). Eso rompe
                ' multi-instance attachments (Codsworth Mr Handy ModArmsHandyAR1A apIdx=0/1).
                ' Fix estructural: Path B usa SkeletonFallbackSocket (publisher SRC1/SRC2 con
                ' parent indexed correcto), NO el publisher chunk socket. Sólo cae al publisher
                ' socket como último recurso (loggeado) si skeleton no tiene este socket name.
                Dim socketForPathB As BSConnectPointReader.ConnectPointInfo = cand.SkeletonFallbackSocket
                If socketForPathB IsNot Nothing Then
                    pathBSource = "skel"
                Else
                    socketForPathB = cand.MountSocket
                    pathBSource = "publisher-fallback"
                    Dim ordL_pbf = ordSelf, nmL_pbf = socketNm
                    Logger.LogLazy(Function() $"[A_HOST-JIT-PATHB-FALLBACK] ord={ordL_pbf} socket='{nmL_pbf}' — SkeletonFallbackSocket is Nothing, usando publisher socket (último recurso; parent puede no estar en actor.skel)")
                End If
                ' [PATH B — APIDX SUBSTITUTION] Skeleton publica P-X con UN solo parent indexed
                ' (típicamente '|0'). Para consumers multi-instance con apIdx != 0, sustituir
                ' el suffix del parent para apuntar al bone indexed correcto del actor skel.
                ' Caso vivo Mr Handy: skeleton P-ModArmsSlotA parent='Arm1|0'. Consumer apIdx=1
                ' (Flamer arm mod) necesita parent='Arm1|1'. Engine convention empírica: el
                ' suffix '|N' del parent matchea el apIdx del consumer.
                Dim parentForPathB = If(socketForPathB.ParentBoneName, "")
                Dim parentForLookup = parentForPathB
                If cand.MountApIdx <> 0 AndAlso Not String.IsNullOrEmpty(parentForPathB) Then
                    Dim pipe = parentForPathB.LastIndexOf("|"c)
                    If pipe > 0 AndAlso pipe < parentForPathB.Length - 1 Then
                        Dim sfx = parentForPathB.Substring(pipe + 1)
                        Dim allDigits As Boolean = True
                        For Each c In sfx
                            If Not Char.IsDigit(c) Then allDigits = False : Exit For
                        Next
                        If allDigits Then
                            parentForLookup = String.Concat(parentForPathB.AsSpan(0, pipe + 1), cand.MountApIdx.ToString())
                            If Not String.Equals(parentForLookup, parentForPathB, StringComparison.Ordinal) Then
                                Dim ordL_sub = ordSelf, origL = parentForPathB, newL = parentForLookup, apL = cand.MountApIdx
                                Logger.LogLazy(Function() $"[A_HOST-JIT-PATHB-APIDX-SUB] ord={ordL_sub} parent '{origL}' → '{newL}' (consumer apIdx={apL})")
                            End If
                        End If
                    End If
                End If
                Dim parentBoneWorld = ResolveEffectiveWorld(wbHistory, targetSkel, parentForLookup)
                Dim socketLocal As New Transform_Class With {
                    .Translation = socketForPathB.Translation,
                    .Rotation = BSConnectPointReader.QuatToMatrix33(socketForPathB.Rotation),
                    .Scale = If(socketForPathB.Scale > 0.0F, socketForPathB.Scale, 1.0F)
                }
                M_mesh = parentBoneWorld.ComposeTransforms(socketLocal)
            End If
            cand.ChunkToActor = M_mesh.ComposeTransforms(G_CX.Inverse())

            Dim ordL2 = ordSelf, matchedOrdL = cand.MatchedHostInstanceOrdinal, sNmL = socketNm
            Dim pathL = If(usedPathA, "A(host.ChunkToActor × HostSocketGlobalT)", "B(" & pathBSource & " × socket.local)")
            Dim mmT = M_mesh.Translation, aT = cand.ChunkToActor.Translation
            Logger.LogLazy(Function() $"[A_HOST-JIT] ord={ordL2} socket='{sNmL}' matchedHost.ord={matchedOrdL} path={pathL} M_mesh.T=({mmT.X:F3},{mmT.Y:F3},{mmT.Z:F3}) A.T=({aT.X:F3},{aT.Y:F3},{aT.Z:F3})")
            Return cand.ChunkToActor
        Finally
            If ordSelf <> 0 Then visiting.Remove(ordSelf)
        End Try
    End Function

    ''' <summary>Resolve OMOD.AttachPointFormID (KYWD FormID) to the KYWD's EditorID. Returns ""
    ''' when the FormID is 0 or the record isn't loaded (which happened for every OMOD before the
    ''' KYWD loader fix on 2026-05-10).</summary>
    Friend Function ResolveAttachPointEditorId(kywdFormID As UInteger) As String
        If kywdFormID = 0UI Then
            Logger.LogLazy(Function() $"[AP-RESOLVE] kywdFid=0 → empty")
            Return ""
        End If
        Dim rec = _ctx.PluginManager.GetRecord(kywdFormID)
        Dim fidLog = kywdFormID
        If rec Is Nothing Then
            Logger.LogLazy(Function() $"[AP-RESOLVE] kywdFid=0x{fidLog:X8} → NOT FOUND in PluginManager")
            Return ""
        End If
        If rec.Header.Signature <> "KYWD" Then
            Dim sig = rec.Header.Signature
            Logger.LogLazy(Function() $"[AP-RESOLVE] kywdFid=0x{fidLog:X8} → wrong sig '{sig}' (expected KYWD)")
            Return ""
        End If
        Dim eid = If(rec.EditorID, "")
        If String.IsNullOrEmpty(eid) Then
            Logger.LogLazy(Function() $"[AP-RESOLVE] kywdFid=0x{fidLog:X8} → KYWD with empty EditorID")
        End If
        Return eid
    End Function

    ''' <summary>Load the actor's skeleton NIFs and index every BSConnectPoint::Parents socket by
    ''' Name (case-insens). Reads sockets from BOTH skeleton sources used by PrepareSkeleton:
    ''' (1) RACE.ANAM (resolved via ResolveSkeletonKey), and (2) BPTD.MODL (resolved via
    ''' RACE.GNAM → BPTD). For humanoides ambos coinciden y la 2da pasada es no-op por dedupe.
    ''' Para robots la 2da pasada aporta los sockets reales (P-ArmsTypeA1|0/1/2, P-BotCore,
    ''' P-BotLegs, P-ModSlotA/B, etc.) que viven en SkeletonRef.nif y no en el stub RACE.ANAM.
    ''' Last-wins on duplicate names (BPTD.MODL pisa al RACE.ANAM cuando hay colisión, igual
    ''' criterio que PrepareSkeleton tiene para bones via MergeAdditionalSkeleton).</summary>
    Friend Function LoadActorBSConnectPoints(state As MainForm.NPCVisualState, warnings As List(Of String)) As Dictionary(Of String, BSConnectPointReader.ConnectPointInfo)
        Dim dict As New Dictionary(Of String, BSConnectPointReader.ConnectPointInfo)(StringComparer.OrdinalIgnoreCase)

        ' Source 1: RACE.ANAM
        Dim skelKey = _stateResolver.ResolveSkeletonKey(state, warnings)
        Dim countAfterSrc1 As Integer = 0
        If Not String.IsNullOrEmpty(skelKey) Then
            IndexSocketsFromSkeletonKey(skelKey, dict)
            countAfterSrc1 = dict.Count
            Logger.LogLazy(Function() $"[SOCKETS-SRC1-RACE.ANAM] key='{skelKey}' addedTotal={countAfterSrc1}")
        Else
            Logger.LogLazy(Function() $"[SOCKETS-SRC1-RACE.ANAM] skelKey EMPTY → skipped")
        End If

        ' Source 2: BPTD.MODL (via RACE.GNAM) — aporta sockets cross-folder y los del SkeletonRef.
        Dim bptdBytes = BodyPartSkeletonResolver.TryLoadBptdSkeletonBytes(state.RaceFormID, _ctx.PluginManager)
        If bptdBytes IsNot Nothing AndAlso bptdBytes.Length > 0 Then
            IndexSocketsFromBytes(bptdBytes, "BPTD.MODL", dict)
            Dim countAfterSrc2 = dict.Count
            Dim diff = countAfterSrc2 - countAfterSrc1
            Logger.LogLazy(Function() $"[SOCKETS-SRC2-BPTD.MODL] bytes={bptdBytes.Length} totalAfter={countAfterSrc2} delta={diff} (delta cuenta nuevos+overwrites; overwrites no detectables sin tracking adicional)")
        Else
            Logger.LogLazy(Function() $"[SOCKETS-SRC2-BPTD.MODL] BPTD bytes EMPTY → skipped")
        End If

        ' [DIAG] Dump completo del dict — sockets disponibles para el resolver.
        Dim sorted = dict.OrderBy(Function(kv) kv.Key).ToList()
        Logger.LogLazy(Function() $"[SOCKETS-DICT] count={sorted.Count}")
        For Each kv In sorted
            Dim name = kv.Key
            Dim cp = kv.Value
            Dim t = cp.Translation
            Dim qx = cp.Rotation.X, qy = cp.Rotation.Y, qz = cp.Rotation.Z, qw = cp.Rotation.W
            Dim parentBone = cp.ParentBoneName
            Dim sc = cp.Scale
            Logger.LogLazy(Function() $"[SOCKETS-DICT]   '{name}' parent='{parentBone}' T=({t.X:F3},{t.Y:F3},{t.Z:F3}) QuatNiflyXYZW=({qx:F4},{qy:F4},{qz:F4},{qw:F4}) [disco(w,x,y,z)=({qx:F4},{qy:F4},{qz:F4},{qw:F4})] S={sc:F3}")
        Next

        Return dict
    End Function

    ''' <summary>Helper: load NIF bytes from FilesDictionary by key + index its BSConnectPoint
    ''' sockets into the target dict (last-wins on duplicate Name).</summary>
    Private Sub IndexSocketsFromSkeletonKey(skelKey As String, dict As Dictionary(Of String, BSConnectPointReader.ConnectPointInfo))
        Dim bytes = MeshPathHelpers.TryLoadMeshBytes(skelKey)
        If bytes Is Nothing Then Return
        Try
            IndexSocketsFromBytes(bytes, skelKey, dict)
        Catch ex As Exception
        End Try
    End Sub

    ''' <summary>Mount the standalone Pipboy ARMO mesh on the actor's pipboy bone via synthetic
    ''' skin. El ARMO Pipboy ships con NIF unskinned + sin BSConnectPoint::Children — engine
    ''' vanilla hardcoded mountea a un bone del actor cuyo nombre contiene "pipboy" (HumanRace
    ''' 369-bone expone "PipboyBone" + "PipboyBone_Offset"). Convención inalcanzable desde data
    ''' del record; la replicamos via synthetic skin + bone lookup dinámico.
    '''
    ''' Lookup target: case-insensitive contra el SkeletonDictionary del actor. Distintas razas
    ''' pueden traer otra convención de nombre (Ghoul, Child, Synth Race) o ninguna — NO
    ''' hardcodeamos "PipboyBone". Preferimos el match que NO termina en "_Offset" (es el bone
    ''' deformable, el _Offset es rest anchor; vanilla mountea al deformable).
    '''
    ''' Bind matrix: walking shape backing → parent → ... → root (exclusive). Misma fórmula que
    ''' FAKE-SKIN del Protectron HeadLight (MainForm.vb:2716-2748); root.local se excluye porque
    ''' en vanilla Bethesda authora ahí la transform de "scene viewer" del CK, no parte del attach.
    '''
    ''' Gate: SOLO standalone Pipboy ARMO (slot==BipedSlots.SlotBitPipboy exacto, sólo bit 30). Outfits que
    ''' declaran bit Pipboy junto con otros bits (ej. ClothesVaultTecScientist slot=0x40000008
    ''' BODY+Pipboy) NO entran — son outfits regulares con sus propios shapes skinneados, el bit
    ''' Pipboy es declarativo de slot reserve, no garantiza pipboy mesh built-in. Check IsSkinned
    ''' per-shape adicional como defense-in-depth.
    '''
    ''' Si el actor skeleton no expone ningún bone "*pipboy*" → log warning + skip; el Pipboy
    ''' renderiza al origin igual que sin fix (no es regresión, sólo no-op).</summary>
    Friend Sub ApplyPipboySyntheticSkin(result As MainForm.PreviewResolutionResult, inst As SkeletonInstance)
        If result Is Nothing OrElse inst Is Nothing Then Return

        Dim hasPipboyCandidate As Boolean = result.CandidateNif.Keys.Any(Function(c) c.SlotMask = BipedSlots.SlotBitPipboy)
        If Not hasPipboyCandidate Then Return

        ' Discover pipboy bone target del skeleton del actor (case-insensitive, sin hardcoding).
        Dim pipboyBoneName As String = Nothing
        Dim pipboyCandidates = inst.SkeletonDictionary.Keys.
            Where(Function(k) k.Contains("pipboy", StringComparison.OrdinalIgnoreCase)).
            ToList()
        If pipboyCandidates.Count > 0 Then
            Dim primary = pipboyCandidates.FirstOrDefault(Function(k) Not k.EndsWith("_Offset", StringComparison.OrdinalIgnoreCase))
            pipboyBoneName = If(primary, pipboyCandidates(0))
        End If
        If pipboyBoneName Is Nothing Then
            Logger.LogLazy(Function() "[PIPBOY-DIAG] FAKE-SKIN skip: no '*pipboy*' bone en actor skeleton — Pipboy renderiza al origin (raza sin chargen-bones?)")
            Return
        End If
        Dim boneNameL = pipboyBoneName
        Logger.LogLazy(Function() $"[PIPBOY-DIAG] FAKE-SKIN target bone resolved: '{boneNameL}'")

        For Each cand In result.CandidateNif.Keys
            If cand.SlotMask <> BipedSlots.SlotBitPipboy Then Continue For
            Dim pipboyNif As Nifcontent_Class_Manolo = Nothing
            If Not result.CandidateNif.TryGetValue(cand, pipboyNif) Then Continue For
            Dim rootNode = pipboyNif.GetRootNode()
            If rootNode Is Nothing Then Continue For

            ' Guard "no traen mounting": si el NIF declara BSConnectPoint::Children (mecanismo
            ' de socket-mounting via "C-X" → "P-X" match contra el actor skeleton), el modder
            ' quiso usar ese path — NO aplicar synthetic skin para no doblar el montaje.
            ' Vanilla Pipboy NIF no declara children (verificado en log: 0 children), así que
            ' este guard no dispara en data vanilla; es defensa contra mods custom.
            Try
                Dim childrenInfo = BSConnectPointReader.ReadChildren(pipboyNif)
                If childrenInfo.PointNames IsNot Nothing AndAlso childrenInfo.PointNames.Count > 0 Then
                    Dim candFidL = cand.SourceFormID
                    Dim ptsL = String.Join(",", childrenInfo.PointNames)
                    Logger.LogLazy(Function() $"[PIPBOY-DIAG] FAKE-SKIN skip cand=0x{candFidL:X8}: NIF declara BSConnectPoint::Children=[{ptsL}] — mod usa socket-mounting, no hardcoded bone attach")
                    Continue For
                End If
            Catch exChildren As Exception
                Dim msg = exChildren.Message
                Logger.LogLazy(Function() $"[PIPBOY-DIAG] FAKE-SKIN ReadChildren EXCEPTION: {msg} (proceediendo con synthetic skin)")
            End Try

            For Each shape In result.Shapes
                If shape.NifContent IsNot pipboyNif Then Continue For
                If shape.IsSkinned Then Continue For
                Dim asOverride = TryCast(shape, IRuntimeSkinOverride)
                If asOverride Is Nothing Then Continue For

                Try
                    Dim backing = shape.Geometry.BackingShape
                    Dim bindMatrix As New Transform_Class(backing)
                    Dim curNode As NiflySharp.Blocks.NiNode = TryCast(pipboyNif.GetParentNode(backing), NiflySharp.Blocks.NiNode)
                    While curNode IsNot Nothing AndAlso Not ReferenceEquals(curNode, rootNode)
                        bindMatrix = New Transform_Class(curNode).ComposeTransforms(bindMatrix)
                        curNode = TryCast(pipboyNif.GetParentNode(curNode), NiflySharp.Blocks.NiNode)
                    End While

                    Dim placeholder As New NiflySharp.Blocks.NiNode With {
                        .Name = New NiflySharp.NiStringRef(pipboyBoneName)
                    }
                    asOverride.ApplySyntheticAnchorSkin(placeholder, bindMatrix)

                    Dim shL = shape.ShapeName
                    Dim bT = bindMatrix.Translation
                    Logger.LogLazy(Function() $"[PIPBOY-DIAG] FAKE-SKIN shape='{shL}' anchor='{boneNameL}' bind.T=({bT.X:F3},{bT.Y:F3},{bT.Z:F3})")
                Catch ex As Exception
                    Dim shL = shape.ShapeName, exL = ex
                    Logger.LogLazy(Function() $"[PIPBOY-DIAG] FAKE-SKIN shape='{shL}' EXCEPTION: {exL.GetType().Name}: {exL.Message}")
                End Try
            Next
        Next
    End Sub

    ''' <summary>Mount-resolve pass for robot chunks. Delegates al
    ''' <see cref="ConnectPointMountResolver"/> de la lib (engine-canónica P-/C- match).
    '''
    ''' Filtrar candidates que vengan del robot path: Kind=Attachment es el discriminador
    ''' canónico (ChunkOmodFormID>0 y SlotMask=0 son condiciones implícitas del Kind, pero las
    ''' mantenemos como defensa explícita por si surge un Attachment con otra topología). Cargar
    ''' el "host NIF" (BPTD.MODL del race) una sola vez para esta corrida — los sockets viven
    ''' ahí (el RACE.ANAM stub solo trae 2 sockets; el real es BPTD.MODL).</summary>
    Friend Sub ResolveRobotChunkMounts(candidates As List(Of MainForm.MeshCandidate),
                                         loadedNifs As Dictionary(Of String, Nifcontent_Class_Manolo),
                                         state As MainForm.NPCVisualState,
                                         warnings As List(Of String))
        Dim robotChunks = candidates.Where(Function(c) c.SlotMask = 0UI AndAlso
                                                       c.Kind = MainForm.MeshCandidateKind.Attachment AndAlso
                                                       c.ChunkOmodFormID <> 0UI).ToList()
        If robotChunks.Count = 0 Then Return

        ' Cargar el host NIF (BPTD.MODL) — fuente canónica de sockets per race. Si el race
        ' no tiene BPTD (humanoides puros) los sockets vienen del RACE.ANAM, pero los chunks
        ' robot solo aparecen en races con BPTD/OBTE así que en la práctica esto siempre tira.
        Dim hostNif = LoadHostNifForMounting(state)

        ' Construir lista de addons. Key = candidate.DictKey (único por chunk en este flow).
        Dim addons = robotChunks.Select(Function(c)
                                            Dim nif As Nifcontent_Class_Manolo = Nothing
                                            loadedNifs.TryGetValue(c.DictKey, nif)
                                            Return New MountAddon With {
                                                .Key = c.DictKey,
                                                .Nif = nif,
                                                .Label = $"omod=0x{c.ChunkOmodFormID:X8} chunk='{c.DictKey}'"
                                            }
                                        End Function).Where(Function(a) a.Nif IsNot Nothing).ToList()

        Dim resolutions = ConnectPointMountResolver.Instance.ResolveMounts(hostNif, addons)

        ' Aplicar resultados al MountSocket de cada candidate. Si CollectRobotChunkCandidates
        ' ya lo resolvió vía AP+apIdx (camino preferido para chunks robot multi-instance), no
        ' pisar — la resolución por NIF children del legacy resolver mountea todo a |0 y rompería
        ' los multi-instance Mr Handy arms/eyes.
        Dim resolved As Integer = 0, noMatch As Integer = 0, noChildren As Integer = 0
        For Each cand In robotChunks
            If cand.MountSocket IsNot Nothing Then
                resolved += 1
                Continue For
            End If
            Dim r As MountResolution = Nothing
            If Not resolutions.TryGetValue(cand.DictKey, r) Then Continue For
            Select Case r.Status
                Case MountResolutionStatus.Resolved
                    cand.MountSocket = r.MatchedSocket
                    resolved += 1
                Case MountResolutionStatus.NoChildren
                    noChildren += 1
                Case MountResolutionStatus.NoMatch
                    noMatch += 1
            End Select
        Next

    End Sub

    ''' <summary>Carga el NIF "host" para mounting de chunks: el BPTD.MODL del race (fuente
    ''' canónica de sockets). Devuelve Nothing si la race no tiene BPTD o el NIF no se puede
    ''' leer — el resolver tolera host Nothing devolviendo NoMatch para todos los addons.</summary>
    Private Function LoadHostNifForMounting(state As MainForm.NPCVisualState) As Nifcontent_Class_Manolo
        Dim bytes = BodyPartSkeletonResolver.TryLoadBptdSkeletonBytes(state.RaceFormID, _ctx.PluginManager)
        If bytes Is Nothing OrElse bytes.Length = 0 Then Return Nothing
        Try
            Dim nif As New Nifcontent_Class_Manolo()
            nif.Load_Manolo(bytes)
            Return nif
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>Helper: parse NIF from bytes + index its BSConnectPoint sockets into the target
    ''' dict (last-wins on duplicate Name). Source label only for logging.</summary>
    Private Sub IndexSocketsFromBytes(bytes As Byte(), sourceLabel As String, dict As Dictionary(Of String, BSConnectPointReader.ConnectPointInfo))
        Try
            Dim nif As New Nifcontent_Class_Manolo()
            nif.Load_Manolo(bytes)
            Dim parents = BSConnectPointReader.ReadParents(nif)
            Dim added As Integer = 0
            For Each p In parents
                If String.IsNullOrEmpty(p.Name) Then Continue For
                dict(p.Name) = p
                added += 1
            Next
        Catch ex As Exception
        End Try
    End Sub
End Class
