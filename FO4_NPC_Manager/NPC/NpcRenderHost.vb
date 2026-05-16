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

        Dim hidden As Integer = 0
        Dim shown As Integer = 0
        For Each mesh In PreviewCtl.Model.meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Shape Is Nothing Then Continue For
            Dim shape = mesh.MeshData.Shape
            Dim cat As MainForm.ShapeRenderCategory = MainForm.ShapeRenderCategory.Other
            LastRenderData.ShapeCategory.TryGetValue(shape, cat)
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
            If covered AndAlso renderUnderarmor AndAlso (cat = MainForm.ShapeRenderCategory.BodySkin OrElse cat = MainForm.ShapeRenderCategory.NakedHands) Then hide = True
            ' HeadPart ocluido por headwear + Render headwear ON → hide (replica occlusion matrix
            ' vanilla pelo-bajo-casco, etc). Render headwear=OFF destapa el head part para mostrar
            ' lo que estaba debajo del casco/glasses/etc.
            If occludedByHeadwear AndAlso renderHeadwear AndAlso cat = MainForm.ShapeRenderCategory.HeadPart Then hide = True

            shape.RenderHide = hide
            If hide Then hidden += 1 Else shown += 1
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
