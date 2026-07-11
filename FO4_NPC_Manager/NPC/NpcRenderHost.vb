Imports System.Windows.Forms
Imports FO4_Base_Library

''' <summary>Per-PreviewControl bag of render-pipeline state for an NPC preview. Holds the
''' "Last*" snapshots produced by a render (skeleton instance, ARMA-cloned skeletons, sculpt
''' deltas, the shape→skeleton resolver map, the resolver result and the visual state itself),
''' the deferred face-tint polling timer + its state/attempts counter, and the GPU/CPU caches
''' used by the live tint compositor (FaceTintTextureCache and the pristine-diffuse pixel
''' dictionary).
'''
''' MainForm owns one instance of this for its main preview. EditFace_Form / EditBody_Form
''' will own their own instance for the embedded preview inside the editor (a future phase
''' wires that up). The split lets each preview keep its own per-render snapshots and tint
''' state so a render in the editor cannot stomp the main form's "Last*" fields and vice
''' versa.
''' </summary>
'''
''' <remarks>
''' This class deliberately does NOT know about <c>Form</c> or <c>MainForm</c>. It receives a
''' <see cref="PreviewControl"/> via the constructor and exposes plain properties. That makes
''' it testable in isolation (instantiate with a stub PreviewControl, verify dirty-flag and
''' tint-deferral semantics without spinning up a Form).
'''
''' Thread-safety: every consumer is expected to run on the UI thread. Properties are not
''' synchronized. The pending-tint timer is a <c>Windows.Forms.Timer</c> which always
''' marshals its <c>Tick</c> back to the UI thread by definition.
'''
''' Disposal: <see cref="Dispose"/> stops and disposes the timer, releases reference fields
''' so any large skeleton / resolver graphs become eligible for GC, and clears the GPU /
''' pristine caches. The hosted <see cref="PreviewControl"/> itself is NOT disposed here —
''' its lifetime is owned by the host Form (MainForm or editor).
''' </remarks>
Friend Class NpcRenderHost
    Implements IDisposable

    ''' <summary>The preview control this host drives. Set once in the constructor; never
    ''' reassigned. Disposed by the owning Form, not by this class.</summary>
    Public ReadOnly Property PreviewCtl As PreviewControl

    ''' <summary>State of the most recently rendered NPC variant. Used by the bone/vertex
    ''' morph checkbox handlers and editor live-edit refreshes to rebuild a single pipeline
    ''' stage without re-running the full preview resolution.</summary>
    Public Property LastRenderedState As MainForm.NPCVisualState = Nothing

    ''' <summary>Resolver result of the most recently rendered NPC: shapes list, skeleton
    ''' key, mesh dictionary keys, chargen / race-morph TRI paths, ARMA sculpt data, etc.
    ''' Reused by partial-refresh paths that don't need to re-resolve from records.</summary>
    Public Property LastRenderData As MainForm.PreviewResolutionResult = Nothing

    ''' <summary>SkeletonInstance built per NPC during the last render. Reused by the
    ''' pose-toggle handlers and the diagnostic harness so they read from the same skeleton
    ''' the render is using (no singleton dependency).</summary>
    Public Property LastSkeletonInstance As SkeletonInstance = Nothing

    ''' <summary>Head skeleton built per NPC during the last render: SAME morph/FMRS pose as
    ''' <see cref="LastSkeletonInstance"/> but WITHOUT the body-weight bone scaling (MWGT/MRSV/NNAM
    ''' neck-fat). Head-part shapes (FaceGen head, hair, eyes, brows) are routed to this so the
    ''' scaled "Neck" bone does not propagate down to Head/FaceBones and deform the head — matching
    ''' the FaceGen bake, which excludes body weight from the head. Animation frames are applied to
    ''' it too (same as the per-ARMA clones) so the head still follows played clips.</summary>
    Public Property LastHeadSkeletonInstance As SkeletonInstance = Nothing

    ''' <summary>Per-ARMA skeleton clones built during the last render. Indexed by
    ''' ArmorAddonFormID. Persisted so the dropdown handler (RebuildAndApplyMergedPose) can
    ''' reconstruct each clone's pose when the user changes armaModel without forcing a full
    ''' re-render.</summary>
    Public Property LastSkelByArma As New Dictionary(Of UInteger, SkeletonInstance)

    ''' <summary>Per-ARMA sculpt deltas used when building each skeleton clone in
    ''' <see cref="LastSkelByArma"/>. Indexed by ArmorAddonFormID. Used to re-derive the
    ''' pose for each clone when armaModel changes.</summary>
    Public Property LastSculptByArma As New Dictionary(Of UInteger, Dictionary(Of String, System.Numerics.Vector3))

    ''' <summary>Shape→SkeletonInstance map handed to MultiInstanceSkeletonResolver. The
    ''' resolver holds this by reference (IReadOnlyDictionary), so mutating entries here is
    ''' observed by the render pipeline on the next Pose dirty pass — without rebuilding the
    ''' resolver. Used by RebuildAndApplyMergedPose to lazy-build per-ARMA skels when Sclpt
    ''' is toggled ON post-render.</summary>
    Public Property LastShapeToSkel As Dictionary(Of IRenderableShape, SkeletonInstance) = Nothing

    ''' <summary>Morph names available in the chargen TRI loaded for the face shape on the last
    ''' render. Used by EditFace_Form to filter slider/preset UI to only entries the engine
    ''' can actually apply: vanilla data is inconsistent (e.g. HumanChildRace.MorphValues
    ''' declares Brow/Chin sliders but the HDPT points at BaseFemaleHeadChargen.tri which only
    ''' has EyesFeature/LipFeature names). Hiding sliders whose MSM0/MSM1 isn't in this set
    ''' replicates engine in-game behavior — silently no-op on missing morphs.
    ''' Empty = no TRI was loaded for this NPC's face (editor falls back to "show all" so the
    ''' user isn't blocked when LastRenderData hasn't been published yet).</summary>
    Public Property LastFaceTriMorphNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Pristine *decoded* pixel bytes (BGRA8) of every face/body diffuse the
    ''' compositor is going to mutate. See <see cref="PristinePixels"/> for the layout
    ''' contract. Captured the first time the compositor / SoftLight pass runs against a
    ''' path. Cleared on root-NPC change (different race / skin TXST = different paths) and
    ''' on FilesDictionary rebuild (BA2 mount/unmount).</summary>
    Public Property PristineDiffusePixels As New Dictionary(Of String, PristinePixels)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Process-lifetime cache of decoded DDS → GL textures keyed by the same
    ''' normalized "textures\..." path used by the MainForm's tint-bytes cache. Lives here
    ''' (per-host) because the entries hold GL texture IDs that the compositor would
    ''' otherwise allocate-and-delete every call.</summary>
    Public Property TintGpuCache As New FaceTintTextureCache()

    ''' <summary>Per-host FaceTintCompositor state (shader programs, fullscreen quad VAO/VBO,
    ''' uniform locations). GL handles are per-context; with multiple PreviewControls (MainForm
    ''' + each editor) each owns its own context, so each owns its own compositor state.
    ''' Disposed (program/VAO/VBO deleted) in <see cref="Dispose"/>.</summary>
    Public Property CompositorState As New FaceTintCompositorState()

    ''' <summary>When True, <c>CollectMeshCandidates</c> skips Skin and Outfit collection
    ''' entirely — only HeadParts enter the pipeline. Same mechanism MainForm's "Only Face"
    ''' PreviewMode uses. EditFace_Form sets this True on its host so the embedded preview
    ''' shows just the head (no body, no outfit, no headwear). MainForm leaves it False and
    ''' selects the mode via its ComboBox; the consumer side merges both.</summary>
    Public Property OnlyFaceCollect As Boolean = False

    ''' <summary>When True, <c>CollectMeshCandidates</c> collects ONLY the outfit (skips Skin, HeadParts
    ''' and robot chunks) while still building the posed/weighted skeleton. The Edit Outfit picker sets
    ''' this for its "selected piece only" preview: the throwaway draft holds the single piece, so the
    ''' render shows just that piece skinned to the real body skeleton — no wasted skinning of body/head
    ''' that the old RenderBody=False only hid post-collection.</summary>
    Public Property OnlyOutfitCollect As Boolean = False

    ''' <summary>Preview-only gender override for the ARMA/ARMO editors' "Show other gender" toggle.
    ''' Nothing = render the NPC's own gender (default, main render path). True/False = force the preview
    ''' to a DEFAULT actor of that gender for the NPC's race (True=female, False=male) — NOT the source NPC
    ''' flipped: <see cref="NpcStateResolver.ResolveNPCBaseState"/> wipes the source NPC's gender-specific
    ''' identity (head parts, face texture, hair color, skin, body weights) so <c>ApplyRaceFallbacks</c>
    ''' repopulates them from the RACE defaults for the target gender, and gender-specific face morphs
    ''' (chargen MSDK/MSDV + FMRS face bones) + the NPC FaceGen head are suppressed downstream. Body mesh
    ''' (MOD2/MOD3), skin TXST (NAM0/NAM1), material swaps (MO2S/MO3S), skeleton, height and body-weight
    ''' bone-scaling all follow the flipped <c>state.IsFemale</c>. Host-scoped so the main render (Nothing)
    ''' is completely inert.</summary>
    Public Property PreviewGenderOverride As Boolean?

    ''' <summary>Deferred face-tint polling timer. The texture cache is async (Render.vb
    ''' <summary>Current NPC visual state being previewed (without outfit — outfit applied
    ''' on-demand from combo). The Save / Copy snapshot reads from this; the render reads
    ''' from <see cref="LastRenderedState"/>. Sync between them is the responsibility of
    ''' code paths that mutate state outside the overlay preset (e.g. MWGT live edit).</summary>
    Public Property CurrentBaseState As MainForm.NPCVisualState = Nothing

    ''' <summary>Render-pipeline boolean knobs. Replaces direct <c>CheckBox*.Checked</c> reads
    ''' inside the pipeline so a render can be requested with a specific configuration regardless
    ''' of which UI surface drives it. The owning Form is responsible for refreshing this snapshot
    ''' (e.g. <c>RenderToggles.FromMainCheckBoxes(Me)</c>) before requesting a render or in each
    ''' CheckedChanged handler. Default ctor produces all-True except RenderGore=False, which is
    ''' a safe baseline for tests / pre-UI-load renders — see RenderToggles.vb for the field
    ''' defaults.</summary>
    Public Property Toggles As RenderToggles = New RenderToggles()

    ''' <summary>Per-NPC LooksMenu preset overlay dict. Stored as a REFERENCE handed in by the
    ''' owning Form (MainForm owns the canonical instance); editor hosts share the same reference
    ''' so live edits inside a modal write through to the same dict the MainForm will re-resolve
    ''' from after OK. Never assigned here — the owner sets it after constructing the host.</summary>
    Public Property AppliedPresets As Dictionary(Of UInteger, FO4_NPC_Manager.LooksmenuLoader.LooksmenuPreset) = Nothing

    ''' <summary>Outfit combo entries sampled for THIS host's last render (Default + optional Sleep,
    ''' each carrying one LVLI realization). Non-main hosts (editor / outfit picker) render the Default
    ''' entry from here, so an editor/picker render never reads or mutates the MainForm-global
    ''' <c>_currentOutfitEntries</c> + outfit combo. Nothing for the main host, which keeps using
    ''' <c>_currentOutfitEntries</c> + <c>ComboBoxOutfit</c>.</summary>
    Public Property OutfitEntries As List(Of MainForm.OutfitComboEntry) = Nothing

    ''' <summary>Out-of-band outfit override for a WYSIWYG preview (Edit Outfit picker), applied in
    ''' <c>ResolveNPCBaseState</c> on top of the overlay-derived outfit. Host-scoped: it NEVER writes
    ''' the shared <see cref="AppliedPresets"/> / MainForm <c>_appliedPresets</c>, so browsing outfits
    ''' in the picker leaves the main render's committed state untouched. Honoured only when
    ''' <see cref="OutfitPreviewActive"/> is True (the main host leaves it False, so the main render path
    ''' is inert). Value: Nothing → raw record DOFT · Some(0) → naked · Some(fid) → OTFT / draft.</summary>
    Public Property OutfitPreviewActive As Boolean = False
    Public Property OutfitPreviewOverride As UInteger?

    ''' <summary>One-shot construction. The PreviewControl must already be created and
    ''' attached to its parent panel; this class does not own its lifetime.</summary>
    Public Sub New(previewCtl As PreviewControl)
        _PreviewCtl = previewCtl
    End Sub

    ''' <summary>Aplica RenderHide a cada mesh según su categoría y el estado de los toggles
    ''' independientes (CheckBoxRenderArmor, CheckBoxRenderUnderarmor). NO re-resuelve candidates
    ''' ni recarga NIFs — sólo flip del flag y refresh GL.</summary>
    Public Sub ApplyRenderToggleVisibility()
        If PreviewCtl Is Nothing OrElse PreviewCtl.Model Is Nothing OrElse LastRenderData Is Nothing Then Return
        Dim renderArmor = Toggles.RenderArmor
        Dim renderUnderarmor = Toggles.RenderUnderarmor
        Dim renderBody = Toggles.RenderBody
        Dim renderHeadwear = Toggles.RenderHeadwear
        Dim renderGore = Toggles.RenderGore
        ' SSE occludes skin PER-PARTITION (BSDismemberSkinInstance), FO4 whole-shape (System-A displacement).
        ' Byte-level RE both engines: reference_sse_engine_occlusion_re. Gates the skin-occlusion branches below.
        Dim isSse As Boolean = (Config_App.Current.Game = Config_App.Game_Enum.Skyrim)

        ' --- Per-segment worn-slot occlusion, recomputed from the items CURRENTLY rendered ---
        ' Engine-faithful ORDER / other-items rule (resolver 0x14035E090, owner-slot branch 0x14035E22B),
        ' made toggle-aware: an item hidden by a render toggle (e.g. the Pipboy when "Render armor" is OFF)
        ' contributes NO slots, so the segments it was covering re-appear (a rolled-up sleeve drops back
        ' down). The occluder set is NOT the static load-time union — it is rebuilt here every apply.
        ' groupSlots[gid] = the OWN-slot mask of each rendered worn-item group (one group per candidate; its
        ' shapes share the mask, so an item never occludes its own segments). A worn item contributes only
        ' while its render category is shown (same condition that drives RenderHide for that category).
        ' SLOT_PIPBOY (bit 30 = biped slot 60, Pipboy) now lives in the shared BipedSlots module.
        Dim groupSlots As New Dictionary(Of Integer, UInteger)
        ' SSE: máscaras crudas por-armature de los ítems renderizados (input de HeadPartHideMask). Vacía en FO4.
        Dim sseWornOwnMasks As New List(Of UInteger)
        ' SSE per-partición por-dueño (fase 1 de 0x140218200): el engine mantiene, por biped-slot, el ARMA que
        ' GANÓ ese slot (owner en `entry+0x18` = puntero al ARMATURE). Aquí lo replicamos como un mapa slot→
        ' (model, priority) construido SÓLO desde los worn items ACTUALMENTE renderizados (toggle-aware). El
        ' `model` es el path del NIF del ítem (DictKey); en el attach el engine lo lee de vf+0x20 del bipedModel.
        ' En empate de slot gana el de mayor DNAM priority; a igualdad, el de después en el orden de iteración
        ' (engine tie=newer). Vacío/ignorado en FO4. Índice b (0..31) = biped slot 30+b (plegado 130..161→30..).
        Dim slotOwnerKey(31) As String       ' Nothing = slot libre; si no, DictKey (model path) del dueño
        Dim slotOwnerPrio(31) As Integer
        For Each m In PreviewCtl.Model.meshes
            If m Is Nothing OrElse m.MeshData Is Nothing OrElse m.MeshData.Shape Is Nothing Then Continue For
            Dim sh = m.MeshData.Shape
            Dim own As UInteger = 0UI
            If Not LastRenderData.ShapeOwnSlots.TryGetValue(sh, own) OrElse own = 0UI Then Continue For
            Dim oc As MainForm.ShapeRenderCategory = MainForm.ShapeRenderCategory.Other
            LastRenderData.ShapeCategory.TryGetValue(sh, oc)
            Dim rendered As Boolean
            Select Case oc
                Case MainForm.ShapeRenderCategory.ArmorOver : rendered = renderArmor
                Case MainForm.ShapeRenderCategory.Underarmor, MainForm.ShapeRenderCategory.GloveOutfit : rendered = renderUnderarmor
                Case MainForm.ShapeRenderCategory.Headwear : rendered = renderHeadwear
                Case Else : rendered = True
            End Select
            If Not rendered Then Continue For
            Dim gid As Integer = 0
            LastRenderData.ShapeSlotGroup.TryGetValue(sh, gid)
            ' Slot 60 (Pipboy) is COEXIST-BY-DESIGN: ~every body outfit declares it in BOD2 for the forearm
            ' 60/160 accommodation swap (RE + ESM data 06-22), but only an ACTUAL Pipboy DEVICE (an item
            ' whose ONLY worn slot is 60) really occupies it. The 60/160 swap must trigger on "a Pipboy
            ' device is present", NOT on "some worn piece declared slot 60". So strip bit 60 from non-device
            ' groups: a uniform's incidental slot-60 must NOT occlude another piece's forearm-60 segment
            ' (Captain Cade: the gloves' forearm-60 was wrongly hidden by the uniform's BOD2 slot-60, with no
            ' pipboy). A real Pipboy device keeps its 60 → it alone drives the swap (60 hidden, 160 shown).
            ' Slot-60 strip is FO4-ONLY (Pipboy coexist-by-design). In Skyrim slot 60 is a generic MOD slot
            ' and the engine's worn-mask builder (0x140225CB0) strips NO bit — so pass the raw own-mask.
            If isSse Then
                groupSlots(gid) = own
                ' Mecanismo (b) de HeadPartHideMask: BOD2 del ARMATURE, no la SlotMask (que trae además los
                ' bits headwear del ARMO). La fase 2 de 0x140218200 agrupa por el puntero de ARMA guardado en
                ' `entry+0x18`, y ese writer sólo recorre los bits del ARMA.
                Dim armaOwn As UInteger = 0UI
                If LastRenderData.ShapeArmaOwnSlots.TryGetValue(sh, armaOwn) AndAlso armaOwn <> 0UI Then
                    sseWornOwnMasks.Add(armaOwn)
                    ' Fase 1 de 0x140218200: registrar el dueño de cada slot que este worn item declara en su
                    ' ARMA (skin NO posee: su ShapeArmaOwnSlots es 0 y no llega acá). Empate → mayor priority;
                    ' a igualdad (>=) gana el de después en la iteración = engine tie=newer.
                    Dim ownerKey As String = Nothing
                    LastRenderData.MeshDictKeys.TryGetValue(sh, ownerKey)
                    Dim ownerPrio As Integer = 0
                    LastRenderData.ShapePriority.TryGetValue(sh, ownerPrio)
                    For b As Integer = 0 To 31
                        If (armaOwn And (1UI << b)) = 0UI Then Continue For
                        If slotOwnerKey(b) Is Nothing OrElse ownerPrio >= slotOwnerPrio(b) Then
                            slotOwnerKey(b) = ownerKey
                            slotOwnerPrio(b) = ownerPrio
                        End If
                    Next
                End If
            Else
                Dim isPipboyDevice As Boolean = (own And BipedSlots.SLOT_PIPBOY) <> 0UI AndAlso (own And (Not BipedSlots.SLOT_PIPBOY)) = 0UI
                groupSlots(gid) = If(isPipboyDevice, own, own And (Not BipedSlots.SLOT_PIPBOY))
            End If
        Next
        Dim occupiedVisible As UInteger = 0UI
        For Each kv In groupSlots : occupiedVisible = occupiedVisible Or kv.Value : Next

        ' SSE head-part occlusion (engine attach 0x140218200 fase 2): la máscara per-partición de head-parts
        ' NO es occupiedVisible ∩ HeadOcclusionMask sino el BOD2 COMPLETO del ítem que ocupa el slot de pelo
        ' (30+B). Se recomputa desde los grupos ACTUALMENTE renderizados (respeta los toggles) con la MISMA
        ' regla que SelectWinningCandidates → NpcMeshCollector.HeadPartHideMask (un único sitio con la regla).
        ' Vacío en FO4 (esa rama sigue usando occupiedVisible ∩ HeadOcclusionMask, byte-idéntica).
        Dim sseHeadMask As UInteger = If(isSse, NpcMeshCollector.HeadPartHideMask(LastRenderData.HeadHairSlotMask, occupiedVisible, sseWornOwnMasks), 0UI)

        ' SSE oclusión per-partición por-dueño (fase 1 de 0x140218200): la máscara de slots CUBIERTOS de una
        ' malla = todos los slots cuyo dueño es un ítem con OTRO NIF. Una partición se oculta ⟺ su slot lo
        ' posee un ítem cuyo model path (DictKey) difiere del de esta malla. El engine compara con `_stricmp`
        ' (case-insensitive, import de api-ms-win-crt-string) el model del owner(slot) contra el model M de la
        ' malla. Self-exclude natural: los slots que la propia malla posee tienen su MISMO DictKey ⇒ no entran.
        ' NOTA (deliberadamente NO implementado — traceado hasta el origen): el engine tiene un gate extra
        ' que oculta la partición si su slot NO está en el BOD2 propio de la RACE (`race+0x60`, IsSlotOccupied
        ' @0x140218305). Pero el bool que lo activa (attach 0x2166C0 @0x14021674A-0x140216770) NACE EN 0: sólo
        ' se enciende si el objeto adjuntado es idéntico a uno de dos SINGLETONS GLOBALES (`cmp rbx,[rip+..]` +
        ' predicado de identidad 0x140736130). Para la piel/ropa/cabeza de un NPC normal rbx nunca es esos
        ' singletons ⇒ bool=0 ⇒ el gate NUNCA corre. Replicarlo sería una rama muerta en nuestros casos.
        Dim CoveredForShape As Func(Of String, UInteger) =
            Function(shapeKey As String)
                Dim m As UInteger = 0UI
                For b As Integer = 0 To 31
                    Dim ok As String = slotOwnerKey(b)
                    If ok IsNot Nothing AndAlso Not String.Equals(ok, shapeKey, StringComparison.OrdinalIgnoreCase) Then m = m Or (1UI << b)
                Next
                Return m
            End Function

        ' [OCCL-DEBUG] gated by Logger.Enabled (zero cost when off; app-only diagnostic, B.1-compliant).
        ' Dumps the per-actor occlusion inputs so a visual occlusion bug can be read from the log:
        ' the rendered worn-slot union + each worn-item group's own-slot mask.
        If Logger.Enabled Then
            Dim grpSb As New System.Text.StringBuilder()
            For Each kv In groupSlots : grpSb.Append($"g{kv.Key}=0x{kv.Value:X} ") : Next
            Dim ovDbg = occupiedVisible
            Dim grpDbg = grpSb.ToString().TrimEnd()
            Dim hmDbg = sseHeadMask
            Dim rhDbg = renderHeadwear : Dim raDbg = renderArmor : Dim ruDbg = renderUnderarmor : Dim rbDbg = renderBody : Dim rgDbg = renderGore
            ' SSE: dump del mapa de dueños por-slot (fase 1 de 0x140218200). owner(slot).model = DictKey del ARMA
            ' que ganó ese slot; sólo el basename para compactar. Vacío en FO4 (arrays sin poblar).
            Dim ownSb As New System.Text.StringBuilder()
            If isSse Then
                For b As Integer = 0 To 31
                    If slotOwnerKey(b) IsNot Nothing Then ownSb.Append($"{30 + b}→{System.IO.Path.GetFileName(slotOwnerKey(b))} ")
                Next
            End If
            Dim ownDbg = ownSb.ToString().TrimEnd()
            Logger.LogLazy(Function() $"[OCCL] apply: headwear={rhDbg} armor={raDbg} underarmor={ruDbg} body={rbDbg} gore={rgDbg} occupiedVisible=0x{ovDbg:X} headMask=0x{hmDbg:X} groups=[{grpDbg}] owners=[{ownDbg}]")
        End If

        Dim hidden As Integer = 0
        Dim shown As Integer = 0
        For Each mesh In PreviewCtl.Model.meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Shape Is Nothing Then Continue For
            Dim shape = mesh.MeshData.Shape
            Dim cat As MainForm.ShapeRenderCategory = MainForm.ShapeRenderCategory.Other
            LastRenderData.ShapeCategory.TryGetValue(shape, cat)
            ' Push the per-segment occlusion mask the render index filter (EnsureZapIndexBuffer) consumes.
            ' Head parts, GATED by Render headwear ("Render headwear" OFF ⇒ mask 0 ⇒ head parts revealed whole):
            '   • SSE: engine attach 0x140218200 fase 2 — el BOD2 COMPLETO del ítem que ocupa el slot de pelo
            '     (30+B), recomputado por render desde los grupos renderizados (sseHeadMask =
            '     NpcMeshCollector.HeadPartHideMask). NO occupiedVisible ∩ HeadOcclusionMask (eso daba sólo el
            '     bit del slot de pelo, dejando visibles las demás particiones del casco).
            '   • FO4 (byte-idéntico): la rebanada head-region del worn set renderizado, occupiedVisible ∩
            '     LastRenderData.HeadOcclusionMask (RaceUtil.RaceHeadOcclusionMask, per-NPC RACE-driven).
            ' Worn items: OR of the OTHER rendered groups' slots (own group excluded ⇒ shared-slot safe — the
            ' Pipboy's slot 60 still hides a Pipboy-aware outfit's biped-60 forearm). Everything else: none.
            shape.OwnSlotsMask = 0UI   ' default; set to the item's BOD2 only on the FO4 worn branch below
            If cat = MainForm.ShapeRenderCategory.HeadPart Then
                If isSse Then
                    shape.CoveredSlotsMask = If(renderHeadwear, sseHeadMask, 0UI)
                Else
                    shape.CoveredSlotsMask = If(renderHeadwear, occupiedVisible And LastRenderData.HeadOcclusionMask, 0UI)
                End If
            ElseIf isSse Then
                ' SSE oclusión per-partición por-dueño (fase 1 de 0x140218200). TODA malla no-headpart (skin
                ' desnudo Y outfits) recibe la máscara de slots cuyo dueño es un ítem con OTRO NIF. Cada
                ' partición BSDismember se oculta ⟺ su slot plegado (130..161→30.., 230..261→130..) lo posee
                ' un model distinto. Regla neta del engine: una partición se oculta ⟺ owner(slot).model ≠ M
                ' (comparado con `_stricmp`; owner(slot) = el ARMA guardado en `entry+0x18`). Self-exclude
                ' natural — los slots que la propia malla posee mapean a su MISMO DictKey ⇒ CoveredForShape los
                ' excluye. Reemplaza el par de ramas viejo (skin=occupiedVisible, outfit=0): el skin ya no se
                ' sobre-ocultaba por un slot BOD2 incidental (p.ej. calves 38 compartido con botas) y los
                ' outfits ahora SÍ ocultan las particiones que otro ítem les cubre.
                Dim shapeKey As String = Nothing
                LastRenderData.MeshDictKeys.TryGetValue(shape, shapeKey)
                shape.CoveredSlotsMask = CoveredForShape(shapeKey)
            Else
                Dim ownSlots As UInteger = 0UI
                If LastRenderData.ShapeOwnSlots.TryGetValue(shape, ownSlots) AndAlso ownSlots <> 0UI Then
                    Dim gid As Integer = 0
                    LastRenderData.ShapeSlotGroup.TryGetValue(shape, gid)
                    Dim others As UInteger = 0UI
                    For Each kv In groupSlots
                        If kv.Key <> gid Then others = others Or kv.Value
                    Next
                    shape.CoveredSlotsMask = others
                    ' Own BOD2 footprint of this worn item, so ComputeHiddenTriangles can tell a SELF-tagged
                    ' segment (slot the item occupies) from a FOREIGN one (engine coverage-key companion).
                    shape.OwnSlotsMask = ownSlots
                Else
                    shape.CoveredSlotsMask = 0UI
                End If
            End If
            Dim covered As Boolean = False
            LastRenderData.ShapeCoveredByOutfit.TryGetValue(shape, covered)
            Dim occludedByHeadwear As Boolean = False
            LastRenderData.ShapeOccludedByHeadwear.TryGetValue(shape, occludedByHeadwear)
            Dim meatcapCls As MainForm.MeatcapClassification = MainForm.MeatcapClassification.Normal
            LastRenderData.ShapeMeatcap.TryGetValue(shape, meatcapCls)

            Dim hide As Boolean = False
            ' Render gore OFF → ocultar meatcap shapes (BSSubIndexTriShape sub-segments con
            ' userSlotID en SECTIONCAP/TORSOCAP del enum NIF o en el rango Gore 100/102/103 del
            ' .xrc de BS-OS). Geometría interna del corte que sólo se ve post-dismemberment. ON
            ' las muestra para inspección.
            If Not renderGore AndAlso meatcapCls <> MainForm.MeatcapClassification.Normal Then hide = True
            ' Render armor OFF → hide piezas [A] over-armor.
            If Not renderArmor AndAlso cat = MainForm.ShapeRenderCategory.ArmorOver Then hide = True
            ' Render underarmor OFF → hide ropa que cubre body/hands desnudos: Underarmor (Outfit
            ' con BODY/[U]) + GloveOutfit (Outfit con hand bits). Apagar estos destapa el Skin
            ' subyacente, replicando el efecto in-game `unequipall`.
            If Not renderUnderarmor AndAlso (cat = MainForm.ShapeRenderCategory.Underarmor OrElse cat = MainForm.ShapeRenderCategory.GloveOutfit) Then hide = True
            ' Render body OFF → hide cuerpo desnudo del NPC: body skin + naked hands + head parts.
            ' Aplica independientemente de si el Skin está cubierto o no (cat captura BodySkin sin
            ' necesidad de mirar `covered`).
            If Not renderBody AndAlso (cat = MainForm.ShapeRenderCategory.BodySkin OrElse cat = MainForm.ShapeRenderCategory.NakedHands OrElse cat = MainForm.ShapeRenderCategory.HeadPart) Then hide = True
            ' Render headwear OFF → hide cualquier headwear (Outfit con bits cabeza/cara puros).
            If Not renderHeadwear AndAlso cat = MainForm.ShapeRenderCategory.Headwear Then hide = True
            ' Skin cubierto por outfit + Render underarmor ON → hide (el outfit lo tapa visualmente,
            ' evita z-fighting). Cuando Render underarmor=OFF el outfit se oculta arriba y el Skin
            ' subyacente queda visible (no se aplica este hide). Solo afecta a Skin candidates
            ' (BodySkin/NakedHands); las otras categorías no setean ShapeCoveredByOutfit.
            ' FO4: skin covered by outfit → whole-shape hide (its dismember tags 1-5 aren't biped-slot
            ' tagged). SSE: skin is occluded PER-PARTITION via CoveredSlotsMask above (keyed on the real
            ' mesh SBP slot), so do NOT whole-hide here — a body whose BOD2 incidentally lists calves(38)
            ' no longer vanishes under boots; only the partitions whose SBP slot is actually covered go.
            If Not isSse AndAlso covered AndAlso renderUnderarmor AndAlso (cat = MainForm.ShapeRenderCategory.BodySkin OrElse cat = MainForm.ShapeRenderCategory.NakedHands) Then hide = True
            ' HeadPart ocluido por headwear + Render headwear ON → hide (replica occlusion matrix
            ' vanilla pelo-bajo-casco, etc). Render headwear=OFF destapa el head part para mostrar
            ' lo que estaba debajo del casco/glasses/etc.
            If occludedByHeadwear AndAlso renderHeadwear AndAlso cat = MainForm.ShapeRenderCategory.HeadPart Then hide = True

            shape.RenderHide = hide
            If hide Then hidden += 1 Else shown += 1

            ' [OCCL-DEBUG] per-shape occlusion decision (gated; computes the predicted hidden-triangle
            ' count via the SAME ComputeHiddenTriangles the renderer applies, so the log mirrors what the
            ' viewport will do). Lets a visual occlusion error be read from fo4lib.log shape-by-shape.
            If Logger.Enabled Then
                Dim cmDbg = shape.CoveredSlotsMask
                Dim ownDbg As UInteger = 0UI : LastRenderData.ShapeOwnSlots.TryGetValue(shape, ownDbg)
                Dim siDbg = TryCast(shape.NifShape, NiflySharp.Blocks.BSSubIndexTriShape)
                Dim bipedDbg As String = "-"
                Dim hidDbg As Integer = 0, totDbg As Integer = 0
                If siDbg IsNot Nothing Then
                    ' FO4: segmented BSSubIndexTriShape.
                    bipedDbg = String.Join(",", BSTriShapeGeometry.GetBipedObjects(siDbg))
                    Dim occlDbg = BSTriShapeGeometry.ComputeHiddenTriangles(siDbg, cmDbg)
                    totDbg = occlDbg.Length
                    For Each h In occlDbg
                        If h Then hidDbg += 1
                    Next
                ElseIf isSse AndAlso shape.NifContent IsNot Nothing AndAlso shape.NifShape IsNot Nothing Then
                    ' SSE: mirror the REAL render path (BSDismember partitions) so the log tells the truth.
                    ' The old code only cast to BSSubIndexTriShape (an FO4 type) → SSE shapes always logged
                    ' biped={-} hiddenTris=0/0 even when occlusion ran. This computes the actual SSE result.
                    Dim bps = shape.NifContent.GetTriangleBodyParts(shape.NifShape)
                    If bps IsNot Nothing AndAlso bps.Count > 0 Then
                        bipedDbg = String.Join(",", bps.Where(Function(v) v >= 0).Distinct().OrderBy(Function(v) v))
                        Dim occlDbg = shape.NifContent.ComputeHiddenTrianglesDismember(shape.NifShape, cmDbg)
                        If occlDbg IsNot Nothing Then
                            totDbg = occlDbg.Length
                            For Each h In occlDbg
                                If h Then hidDbg += 1
                            Next
                        End If
                    Else
                        bipedDbg = "no-dismember-parts"
                    End If
                End If
                Dim nmDbg = If(shape.ShapeName, "?"), catDbg = cat, hideDbg = hide
                Logger.LogLazy(Function() $"[OCCL]   shape='{nmDbg}' cat={catDbg} own=0x{ownDbg:X} coveredMask=0x{cmDbg:X} hiddenTris={hidDbg}/{totDbg} biped={{{bipedDbg}}} renderHide={hideDbg}")
            End If
        Next
        ' RefreshRender fuerza repaint inmediato del control GL (Invalidate). InvalidateRender
        ' va por el pipeline que requiere DirtyFlags y aquí no hay nada dirty — sólo flip de
        ' RenderHide en shapes existentes que el shader respeta en cada frame.
        PreviewCtl.RefreshRender()
    End Sub

    ''' <summary>Pristine *decoded* pixel layout used by <see cref="NpcRenderHost.PristineDiffusePixels"/>.
    ''' Pixels are stored as the level-0 mip in BGRA8 order (B, G, R, A in memory) because
    ''' the source is <c>DirectXTexWrapperCLI.Loader.ConvertForBitmap</c>, which produces
    ''' the GDI <c>Format32bppArgb</c> layout. Upload with <c>PixelFormat.Bgra</c> (NOT
    ''' Rgba) so the GL driver swizzles correctly.</summary>
    Public NotInheritable Class PristinePixels
        Public Pixels As Byte()
        Public Width As Integer
        Public Height As Integer
        Public DGXFormat_Original As Integer
        Public DGXFormat_Final As Integer
        ''' <summary>sRGB-ness of the ORIGINAL load (captured from the dict entry). The rollback must
        ''' re-upload in the same colour-space (sRGB SRV vs raw Rgba8) and restore entry.IsSRGB to this,
        ''' otherwise baseDiffuseIsLinearOnGpu desyncs on the next composite → tone/gamma shift on edit.</summary>
        Public IsSRGB As Boolean
    End Class

#Region "IDisposable"

    Private _disposed As Boolean = False

    Public ReadOnly Property IsDisposed As Boolean
        Get
            Return _disposed
        End Get
    End Property

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        _disposed = True

        ' Drop refs so large graphs become GC-eligible. The PreviewControl itself is owned
        ' by the host Form and is NOT disposed here.
        LastRenderedState = Nothing
        LastRenderData = Nothing
        LastSkeletonInstance = Nothing
        LastShapeToSkel = Nothing
        CurrentBaseState = Nothing

        If LastSkelByArma IsNot Nothing Then LastSkelByArma.Clear()
        If LastSculptByArma IsNot Nothing Then LastSculptByArma.Clear()
        If PristineDiffusePixels IsNot Nothing Then PristineDiffusePixels.Clear()

        Try
            If TintGpuCache IsNot Nothing Then TintGpuCache.Clear()
        Catch
            ' Defensive
        End Try

        ' Release compositor GL handles (program/VAO/VBO). Caller must already have the
        ' owning GL context current — this is the standard precondition the host's lifecycle
        ' contract gives Dispose: invoked from FormClosing on the UI thread, before the
        ' PreviewControl is disposed and its context torn down.
        Try
            If CompositorState IsNot Nothing Then CompositorState.Dispose()
        Catch
            ' Defensive
        End Try

        GC.SuppressFinalize(Me)
    End Sub

#End Region
End Class
