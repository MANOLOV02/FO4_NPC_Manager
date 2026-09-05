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

    ''' <summary>¿Este preview dibuja los overlays del pool MAGIC (<c>… [SOvl{n}]</c>)?
    ''' <para>Default <b>False</b>, y ese default es una decisión de producto sobre un hecho MEDIDO: la plantilla del
    ''' pool magic (<c>*_magicoverlay.nif</c>) trae un <c>BSEffectShaderPropertyFloatController</c> con
    ''' <c>typeOfControlledVariable=5</c> (=<b>Alpha</b>), apuntando al <c>BSLightingShaderProperty</c>, con flags
    ''' <c>0x4A</c> = ACTIVE + CYCLE_REVERSE, frequency 8 y keys <c>(t=0,v=0)→(t=10,v=1)</c> lineales ⇒ <b>la opacidad
    ''' la anima el motor, pulsando 0↔1</b>. O sea que NO EXISTE un cuadro que sea "cómo se ve": el preview principal
    ''' es el retrato del NPC, y un efecto en curso no es parte de su identidad.
    ''' <para>La versión anterior de esta nota decía "arranca apagado", que era una INFERENCIA sobre las keys
    ''' presentada como medición — y encima nuestro propio apply-script escribe <c>KEY_ALPHA</c> con persist=true, así
    ''' que "apagado" no era ni siquiera lo que dejamos escrito. El dato real es que ese valor lo pisa el controller
    ''' mientras corre.</para></para>
    ''' <para>Los hosts de los EDITORES la ponen en True: ahí el trabajo es autorar esa capa, así que hay que verla —
    ''' en el PICO de su ciclo, que es el cuadro útil para el autor. El checkbox del editor conmuta esto y
    ''' re-renderiza.</para>
    ''' <para>Vive en el HOST y no en el Config global: dos previews del MISMO NPC tienen que poder discrepar (el
    ''' principal no, el del editor sí) y eso es exactamente lo que un flag global no puede representar. Por eso el
    ''' resolver recibe el host que está renderizando, no <c>_hostProvider()</c>.</para>
    ''' <para>El bake NO consulta esto: no hay bake de spell overlays (nunca se pliegan; viajan por el
    ''' apply-script), así que ningún host de bake necesita el flag.</para></summary>

    ''' <summary>State of the most recently rendered NPC variant. Used by the bone/vertex
    ''' morph checkbox handlers and editor live-edit refreshes to rebuild a single pipeline
    ''' stage without re-running the full preview resolution.</summary>
    Public Property LastRenderedState As MainForm.NPCVisualState = Nothing

    ''' <summary>Resolver result of the most recently rendered NPC: shapes list, skeleton
    ''' key, mesh dictionary keys, chargen / race-morph TRI paths, ARMA sculpt data, etc.
    ''' Reused by partial-refresh paths that don't need to re-resolve from records.</summary>
    Public Property LastRenderData As MainForm.PreviewResolutionResult = Nothing
    ''' <summary>Servicio de head-bake del último render (Nothing con el gate OFF). Vive acá y no en una
    ''' local de <c>BuildRenderPlan</c> porque lo necesitan los SEIS sitios que reconstruyen el composite de
    ''' morphs — es lo que les permite filtrar los canales de posición de las shapes gateadas sin volver a
    ''' armar el servicio.</summary>
    Public Property LastHeadBakeService As HeadBakeService = Nothing

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

    ''' <summary>ESPEJO CPU EXACTO de <see cref="TintGpuCache"/>: decodes de DDS (source D/N/S de la cara +
    ''' cada capa de tint + cada mascara de region-swap) reusados entre composes, con la MISMA vida per-NPC
    ''' que el cache GL (lo limpia <c>ClearFaceTintCaches</c> al cambiar de NPC raiz y este Dispose).
    ''' <para><b>Por que existe.</b> Con el flag de camara en CPU el render compone la cara por
    ''' <c>FaceTintCpuCompositor.ComposeCpuPipeline</c>, y ese camino armaba un diccionario de decode NUEVO en
    ''' CADA llamada. O sea: cada refresh de edicion viva (cada slider, cada color del editor de cara) volvia a
    ''' decodificar por DirectXTex el juego COMPLETO de DDS, mientras el camino GPU las tenia residentes en
    ''' <c>TintGpuCache</c> desde el primer compose. Era la asimetria que hacia que el modo CPU se sintiera mas
    ''' lento que el bake — el bake batch SI amortiza sus decodes (<c>BeginBatchDecodeCache</c>).</para>
    ''' <para>No puede cambiar un byte de la salida: lo que guarda es funcion PURA de (bytes del DDS, tamaño
    ''' destino) — es exactamente lo que devuelve <c>DecodeDds</c>. Sin cache se recalcula el MISMO valor.</para>
    ''' ConcurrentDictionary por el mismo motivo que <c>BatchDecodeCache</c>: durante un bake batch la bomba
    ''' de mensajes sigue viva y un WM_PAINT puede entrar al render (hilo UI) mientras el bake corre en el
    ''' ThreadPool. Son caches distintos, pero el patron de acceso concurrente es el mismo.</summary>
    Public Property TintCpuDecodeCache As New System.Collections.Concurrent.ConcurrentDictionary(Of String, FaceTintCpuCompositor.DecodedTex)(StringComparer.OrdinalIgnoreCase)

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

    ''' <summary>PREVIEW-ONLY escape hatch for the ARMA editor's "Only Model" scope: the ARMA with this FormID
    ''' is collected even when its RNAM / AdditionalRaces don't cover the preview actor's race (the engine rule
    ''' that <c>EquipResolver.ArmaMatchesRace</c> enforces). Without it, editing an ARMA authored for another
    ''' race renders NOTHING — the collector drops it before any mesh is loaded, so the user can't see the model
    ''' they're editing. "Only Model" already renders a synthetic ARMO/OTFT that no engine ever sees, so the
    ''' bypass changes nothing an actor would actually wear; the engine-faithful scopes ("Full armor" / "Full
    ''' Outfit") never set this and stay strictly filtered. 0 (default, every non-editor host) = no bypass.
    ''' Same rule in both games — the race match is not game-gated.</summary>
    Public Property RaceFilterBypassArmaFormID As UInteger = 0UI

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
        ' El guard dice lo que el método NECESITA. Ya no toca el control ni el modelo: sólo lee
        ' LastRenderData y escribe las máscaras de sus shapes. Exigir un Model cargado hacía que la ley
        ' no corriera —en silencio— hasta que hubiera upload.
        If LastRenderData Is Nothing OrElse LastRenderData.Shapes Is Nothing Then Return
        Dim renderArmor = Toggles.RenderArmor
        Dim renderUnderarmor = Toggles.RenderUnderarmor
        Dim renderBody = Toggles.RenderBody
        Dim renderHeadwear = Toggles.RenderHeadwear
        Dim renderGore = Toggles.RenderGore
        ' SSE occludes skin PER-PARTITION (BSDismemberSkinInstance), FO4 whole-shape (System-A displacement).
        ' Byte-level RE both engines: 23-armor-oclusion-sse-re. Gates the skin-occlusion branches below.
        Dim isSse As Boolean = (Config_App.Current.Game = Config_App.Game_Enum.Skyrim)

        ' --- Per-segment worn-slot occlusion, recomputed from the items CURRENTLY rendered ---
        ' Engine-faithful ORDER / other-items rule (resolver 0x14035E090, owner-slot branch 0x14035E22B),
        ' made toggle-aware: an item hidden by a render toggle (e.g. the Pipboy when "Render armor" is OFF)
        ' contributes NO slots, so the segments it was covering re-appear (a rolled-up sleeve drops back
        ' down). The occluder set is NOT the static load-time union — it is rebuilt here every apply.
        ' groupSlots[gid] = the OWN-slot mask of each rendered worn-item group (one group per candidate; its
        ' shapes comparten la máscara; el self-exclude, en cambio, sale de la tabla de dueños). Un worn item aporta sólo
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
        ' Adjuntos al biped EN ORDEN DE ATTACH (piel primero, worn items después). La tabla de dueños y su
        ' regla de desempate NO se escriben acá: viven en NpcMeshCollector.TablaDeDuenosPorSlot, que es la
        ' sede única y la que ejerce el gate. Acá sólo se junta la entrada.
        Dim adjuntos As New List(Of NpcMeshCollector.AdjuntoDeBiped)
        ' ⛔ UN adjunto por ARMA, no por SHAPE. El writer corre una vez por ARMA (el driver le pasa el
        ' ARMO y él recorre sus armatures), así que una ARMA con N shapes tiene que producir UNA pasada.
        ' Con N pasadas el one-shot del ARMO y el desalojo se ejecutan N veces.
        Dim armasAdjuntadas As New HashSet(Of UInteger)
        Dim Adjuntar As Action(Of IRenderableShape, UInteger) =
            Sub(shp As IRenderableShape, armaOwn As UInteger)
                Dim ownerKey As String = Nothing
                LastRenderData.MeshDictKeys.TryGetValue(shp, ownerKey)
                Dim ownerPrio As Integer = 0
                LastRenderData.ShapePriority.TryGetValue(shp, ownerPrio)
                Dim ownerArmo As UInteger = 0UI
                LastRenderData.ShapeArmoOwnSlots.TryGetValue(shp, ownerArmo)
                Dim ownerSwap As Boolean = False
                LastRenderData.ShapeArmaSwapDePiel.TryGetValue(shp, ownerSwap)
                Dim ownerArmaId As UInteger = 0UI
                LastRenderData.ShapeArmaAddonFormID.TryGetValue(shp, ownerArmaId)
                ' Sin ARMA resuelta el ítem NO pasa por el writer (que es por ARMA): no puede ser dueño
                ' de ningún slot. Es el fallback de EquipResolver al world model del ARMO.
                If ownerArmaId = 0UI Then Exit Sub
                If Not armasAdjuntadas.Add(ownerArmaId) Then Exit Sub
                Dim ownerArmoId As UInteger = 0UI
                LastRenderData.ShapeArmoFormID.TryGetValue(shp, ownerArmoId)
                adjuntos.Add(New NpcMeshCollector.AdjuntoDeBiped With {
                    .Key = ownerKey, .ArmaId = ownerArmaId, .ArmoId = ownerArmoId, .ArmaOwnSlots = armaOwn,
                    .ArmoOwnSlots = ownerArmo, .TieneSwapDePiel = ownerSwap, .Priority = ownerPrio})
            End Sub

        ' ⭐ PASE 0 — LA PIEL TAMBIÉN ES DUEÑA DE SLOTS.
        ' En el motor el ARMA de la piel se adjunta como cualquier otro y el writer del attach
        ' (0x140218AE0) le llena `entry+0x20` a cada slot que gana, así que la fase 1 (0x14021DAE0) ve un
        ' dueño ahí. Sin este pase, un slot que SÓLO posee la piel queda sin dueño y `CoveredForShape` no
        ' lo marca: la app dibuja particiones que el motor oculta. Medido: NakedTorso (00000D67) declara
        ' BOD2 0x174 = {32,34,35,36,38}; FineBoots01AA_UBE declara sólo el 37 y su NIF trae particiones
        ' {37,38} ⇒ el motor oculta la 38 (dueño MaleBody_1.NIF, otro model path) y sin esto se dibujaba
        ' encima de la pantorrilla del cuerpo.
        ' VA PRIMERO A PROPÓSITO: el desempate del attach (0x140218D5F, `ja` ⇒ gana el ocupante existente
        ' sólo si tiene MÁS prioridad) hace que a igual prioridad gane el que se adjunta DESPUÉS, y la
        ' prenda se adjunta después de la piel. Registrar la piel primero reproduce ese orden.
        ' ⛔ La piel NO entra en `groupSlots`/`occupiedVisible`: ése es el análogo de GetWornMask
        ' (0x14022B5A0), que recorre el INVENTARIO equipado, donde la piel no está. Tampoco en
        ' `sseWornOwnMasks` (mecanismo (b) de head-parts), que es la fase 2 sobre worn items.
        ' ⭐ Corre en LOS DOS JUEGOS. En Fallout 4 el writer 0x1403597E0 hace exactamente lo mismo: recorre
        ' los slots que el ARMA declara (0x1403599EA call [ARMA+0x30 vt+0x38]) y escribe el TESModel en
        ' table[slot]+0x30 (0x140359B23 mov [r12+8],rax, con r12 = &table[i]+0x28 por 0x140359898 +
        ' 0x140359B34), que es el puntero que el resolver compara con _stricmp en 0x14035E572/0x14035E58C.
        ' La piel no es un caso aparte para el writer, así que tampoco puede serlo acá.
        ' ⛔ SE ITERA LA LISTA RESUELTA, no `PreviewCtl.Model.meshes`. Los tres bucles de este método usaban
        ' la lista de la GPU y sólo para sacar `MeshData.Shape` —son los MISMOS objetos que
        ' `LastRenderData.Shapes`—, o sea que una ley que no depende del cuadro estaba acoplada al upload.
        ' Costaba tres cosas: (1) no se podía ejercer sin contexto GL, así que el gate canónico no la cubre;
        ' (2) fallaba EN SILENCIO — si el upload no ocurrió, o la lista todavía tenía las mallas del NPC
        ' anterior, cada shape se quedaba con su máscara vieja: medido, 35.047 líneas de un barrido salieron
        ' en vacío por exactamente esto—; (3) obligaba a subir geometría y texturas de cada NPC para leer
        ' números que no dependen de la GPU. Tampoco implementaba la lectura caritativa ("recorrer lo que se
        ' dibujó"): una shape ausente de `meshes` tampoco recibía máscara, se quedaba con la anterior.
        For Each shSkin In LastRenderData.Shapes
            If shSkin Is Nothing Then Continue For
            ' ⛔ POR KIND, no por la categoría de display. Con el filtro por categoría, una piel que no
            ' declare slot de cuerpo ni de manos cae en `Other` y no se registra nunca; y con la ley de
            ' "slot huérfano = CUBIERTO" eso deja a la criatura ENTERA cubierta. Medido con el barrido:
            ' `WinterholdJailFrostAtronach` salía con covered=0xFFFFFFFF y 1944/1944 triángulos ocultos.
            ' `SkinAtronachFrost` (0005B2E7) declara BOD2 = 0x1 = sólo el slot 30. Alcance del corpus:
            ' 22 de 224 razas de Skyrim y 5 de 109 de Fallout tienen la piel así.
            Dim kindSkin As MainForm.MeshCandidateKind = MainForm.MeshCandidateKind.Outfit
            LastRenderData.ShapeKind.TryGetValue(shSkin, kindSkin)
            If kindSkin <> MainForm.MeshCandidateKind.Skin Then Continue For
            If Not renderBody Then Continue For
            Dim armaSkin As UInteger = 0UI
            If Not LastRenderData.ShapeArmaOwnSlots.TryGetValue(shSkin, armaSkin) OrElse armaSkin = 0UI Then Continue For
            Adjuntar(shSkin, armaSkin)
        Next

        For Each sh In LastRenderData.Shapes
            If sh Is Nothing Then Continue For
            Dim own As UInteger = 0UI
            If Not LastRenderData.ShapeOwnSlots.TryGetValue(sh, own) OrElse own = 0UI Then Continue For
            Dim oc As MainForm.ShapeRenderCategory = MainForm.ShapeRenderCategory.Other
            LastRenderData.ShapeCategory.TryGetValue(sh, oc)
            Dim rendered As Boolean
            Select Case oc
                Case MainForm.ShapeRenderCategory.ArmorOver : rendered = renderArmor
                Case MainForm.ShapeRenderCategory.Underarmor, MainForm.ShapeRenderCategory.GloveOutfit : rendered = renderUnderarmor
                Case MainForm.ShapeRenderCategory.Headwear : rendered = renderHeadwear
                ' Categorias de CUERPO: solo aportan mientras se dibujan, igual que los worn items (un
                ' toggle que esconde algo lo saca del set que ocluye). Son Kind=Skin / Kind=HeadPart, que
                ' NUNCA tienen ShapeOwnSlots -eso es exclusivo de Kind=Outfit-, asi que agregarlas aca no
                ' puede cambiar `groupSlots` ni `occupiedVisible`: solo gobierna el mapa de duenos de SSE.
                Case MainForm.ShapeRenderCategory.BodySkin, MainForm.ShapeRenderCategory.NakedHands,
                     MainForm.ShapeRenderCategory.HeadPart : rendered = renderBody
                Case Else : rendered = True
            End Select
            If Not rendered Then Continue For
            Dim gid As Integer = 0
            LastRenderData.ShapeSlotGroup.TryGetValue(sh, gid)
            ' ⛔ ACÁ ESTABA EL STRIP DEL BIT DEL PIPBOY, y se fue con su heurística. El armador de la
            ' máscara worn del motor NO saca ningún bit: Fallout 4 0x14051F530 (0x14051F5A0 call [vt+0x238]
            ' / 0x14051F5A6 or [rbp],eax, salteando los formType 0x22/0x2B/0x2C) y Skyrim 0x14022B5A0.
            ' Strippear era inventar. El caso que el strip tapaba —el uniforme que declara el 60 de
            ' incidente ocultándole el antebrazo-60 a otra pieza— lo resuelve ahora la ley estructural del
            ' occluder (ver `occluderConDispositivo` más abajo), que es la del motor.
            ' ⭐ Y la máscara es el BOD2 del ARMO, CRUDO. El motor lee exactamente eso: FO4
            ' 0x14051F5A0 call [vt+0x238] = 0x140313B80, que hace 0x140313B89 call 0x1402FCB60
            ' (AsBipedObjectForm) + 0x140313B93 mov eax,[rax+8]; SSE el functor 0x1402422F0 hace
            ' 0x14024231D call 0x1401D21C0 + 0x140242327 mov ebx,[rax+8]. Los dos iteran el INVENTARIO
            ' equipado, no el 3D.
            ' ⛔ ACÁ ESTABA `own` = ShapeOwnSlots = ARMA ∪ (ARMO ∩ HEADWEAR_MASK), que es OTRA cantidad:
            ' sumaba bits que sólo declara la ARMA y tiraba bits del ARMO fuera de la región headwear.
            ' Medido: FO4 35 pares (ARMO, ARMA) con un bit de canal de cabeza SÓLO en la ARMA —los
            ' Clothes_RaiderMod_Hood, Armor_Power_Raider_Helm, los Hazmat— y 13 ARMO con un bit de
            ' cabeza que ninguna ARMA declara; SSE 132 y 210 (ArmorDragonPriestMask*: ARMA {30,31},
            ' ARMO {30,42}). Con esto el collect y el render usan LA MISMA cantidad, y la única
            ' diferencia que queda entre los dos sitios es el SET —ganadores del torneo contra ítems
            ' efectivamente dibujados—, que es la decisión de producto de los toggles.
            Dim armoOwn As UInteger = 0UI
            LastRenderData.ShapeArmoOwnSlots.TryGetValue(sh, armoOwn)
            groupSlots(gid) = armoOwn
            ' El BOD2 del ARMATURE, no la SlotMask (que trae además los bits headwear del ARMO).
            Dim armaOwn As UInteger = 0UI
            If LastRenderData.ShapeArmaOwnSlots.TryGetValue(sh, armaOwn) AndAlso armaOwn <> 0UI Then
                ' Mecanismo (b) de HeadPartHideMask, SSE-only: la fase 2 de 0x140218200 agrupa por el
                ' puntero de ARMA guardado en `entry+0x18`, y ese writer sólo recorre los bits del ARMA.
                If isSse Then sseWornOwnMasks.Add(armaOwn)
                ' Registrar el dueño de cada slot que este worn item declara en su ARMA. Corre en los DOS
                ' juegos: SSE 0x140218AE0 y FO4 0x1403597E0 escriben los dos el modelo por slot ganado.
                ' La piel ya se adjuntó en el pase 0; los worn items van después y por eso ganan el empate.
                Adjuntar(sh, armaOwn)
            End If
        Next
        Dim occupiedVisible As UInteger = 0UI
        For Each kv In groupSlots : occupiedVisible = occupiedVisible Or kv.Value : Next

        ' SSE head-part occlusion (engine attach 0x140218200 fase 2): la máscara per-partición de head-parts
        ' NO es occupiedVisible ∩ HeadOcclusionMask sino el BOD2 COMPLETO del ítem que ocupa el slot de pelo
        ' (30+B). Se recomputa desde los grupos ACTUALMENTE renderizados (respeta los toggles) con la MISMA
        ' regla que SelectWinningCandidates → NpcMeshCollector.HeadPartHideMask (un único sitio con la regla).
        ' Vacío en FO4, pero ⛔ NO porque Fallout 4 carezca de fase 2 —la tiene, y es la misma ley— sino
        ' porque su driver de head parts es OTRA cosa y va por canal (HeadPartChannelMask: aplica A, B y C
        ' a tipos de head-part distintos), mientras que la fase 2 aplica UNA máscara a todo el subárbol.
        ' La fase 2 de FO4 vive en el post-loop del resolver de CUERPO 0x14035E3B0: entra sólo si el slot de
        ' attach es el canal B o C de la raza (0x14035E6EF / 0x14035E6F8), recorre los 32 slots
        ' (0x14035E710 … 0x14035E7DE inc r12d / 0x14035E7E5 cmp r12d,0x20 / jl), agrupa por el ARMA guardado
        ' en element+0x28 (0x14035E725, escrito por 0x140359B17) y por cada OTRO slot que ese mismo ARMA
        ' ganó llama al walker 0x140360540 sobre el nodo de cara (Actor::vf+0x408 = 0x140C8D520), que oculta
        ' —sólo HIDE, nunca SHOW— el sub-segmento tagueado 30+i.
        ' ⛔ ESTA NOTA DECÍA QUE ERA INERTE Y ERA FALSO. Decía "el pelo trae {30,31}, así que lo único que
        ' puede ocultar es el 31", suponiendo que el walker corre sobre la pieza de pelo. Corre sobre el
        ' NODO DE CARA ENTERO (0x14035E762 call [vt+0x408] = 0x140C8D520, y 0x140360540 recorre el subárbol),
        ' así que alcanza a ojos, boca, nuca, cara y barba. Medido sobre el corpus: 2135 mallas de head part
        ' traen tag 32 —2043 son ojos de tipo 2—, 21 traen el 48 y 178/169 traen 30/31; y 33 ARMO vanilla
        ' cuyo ARMA gana el canal de pelo declaran el slot 32 que su ARMO NO declara (22 el slot 50), que es
        ' exactamente lo único que la fase 2 puede aportar por encima del worn mask.
        ' ⇒ NO es inerte, y está cableada abajo (fase2FO4). La ley es: para el slot que ES el canal de
        ' pelo (0x14035E6EF cmp [raza+0x1b4],r13d) o el de barba (0x14035E6F8 cmp [raza+0x1b8],r13d), se
        ' toma el ARMA que ganó ese slot (0x14035E720 mov rax,[rcx+rdi+0x28]), se juntan los OTROS slots
        ' que ESA MISMA ARMA ganó (0x14035E725 cmp [rbp],rax, loop de 32 en 0x14035E7DE/0x14035E7E5) y se
        ' zapea el sub-segmento tagueado 30+i (0x1403605A0 lea r8d,[rbp+0x1e] → 0x1416C3860 → 0x1416C34B0).
        ' ⛔ El destino es el NODO DE CARA ENTERO —0x14035E762 call [vt+0x408] = 0x140C8D520, que resuelve
        ' "BSFaceGenNiNodeSkinned", y 0x140360540 recorre el subárbol por [vt+0x20]/[+0x128]— así que NO va
        ' por tipo de head part: alcanza a ojos, boca, cara, nuca y barba igual que al pelo. Y son DOS
        ' disparadores, no uno: B y C.
        ' ⛔ Sólo HIDE: 0x1416C34B0 hace add byte [rec+0x10],1 y con 0xFF sigue al próximo segmento; no hay
        ' fallback a SetAppCulled, así que la fase 2 nunca oculta un nodo entero.

        ' SSE oclusión per-partición por-dueño (fase 1 de 0x140218200): la máscara de slots CUBIERTOS de una
        ' malla = todos los slots cuyo dueño es un ítem con OTRO NIF. Una partición se oculta ⟺ su slot lo
        ' posee un ítem cuyo model path (DictKey) difiere del de esta malla. El engine compara con `_stricmp`
        ' (case-insensitive, import de api-ms-win-crt-string) el model del owner(slot) contra el model M de la
        ' malla. Self-exclude natural: los slots que la propia malla posee tienen su MISMO DictKey ⇒ no entran.
        ' NOTA (deliberadamente NO implementado): la fase 1 tiene un gate extra que oculta la partición si su
        ' slot NO está en el BOD2 propio de la RACE (`race+0x60`, IsSlotOccupied 0x1401D2170, llamado desde
        ' 0x14021DBE5 detrás de un bool que llega como 6º argumento del attach).
        ' ⛔ LA RAZÓN DE NO IMPLEMENTARLO NO ES EL ORIGEN DEL BOOL — la versión anterior de esta nota lo
        ' justificaba con un predicado de identidad contra dos singletons, y eso se trazó sobre SkyrimSE
        ' 1.6.1170, un build que ya no está instalado: hoy no se puede re-verificar. La razón que SÍ se puede
        ' volver a medir en cualquier build es el CORPUS: las 99 RACE de Skyrim.esm traen BOD2 (ninguna trae
        ' BODT) y `NordRace` declara 0x8000025C = slots {32,33,34,36,39,61}, mientras que `femalehead.nif`
        ' tiene particiones {30,43}. Si el gate corriera, la cabeza entera desaparecería en todo NPC humano.
        ' En FO4 es igual: `HumanRace` declara 0xE0203038 = {33,34,35,42,43,51,59,60,61}, sin 30/47/48, así
        ' que el casco synth, la gas mask y la bandana nunca se dibujarían. ⇒ no corre, y no implementarlo
        ' es lo fiel.
        Dim tablaDeSlots = NpcMeshCollector.TablaDeDuenosPorSlot(adjuntos, Config_App.Current.Game)
        Dim CoveredForShape As Func(Of String, UInteger) =
            Function(shapeKey As String) NpcMeshCollector.SlotsCubiertosPorOtroModelo(tablaDeSlots, shapeKey)
        ' Estado del slot occluder para ESTE actor: uno solo, derivado de la tabla de dueños y del BOD2 del
        ' ARMO de cada adjunto. Ver NpcMeshCollector.OccluderConDispositivo — reemplaza la identidad por
        ' default object (ShapeIsPipboyDevice) Y el strip del bit, que no son la ley del motor.
        Dim occluderConDispositivo As Boolean =
            NpcMeshCollector.OccluderConDispositivo(tablaDeSlots, LastRenderData.PipboySlotMask)

        ' Fase 2 del attach. VA ACÁ y no antes porque necesita la TABLA DE DUEÑOS: el motor pregunta por
        ' el ARMA que GANÓ el slot de pelo y por los otros slots que ESA ganó (0x14021DD4D/0x14021DD52),
        ' no por los BOD2 declarados de las ARMA que lo intersectan. El canal de pelo de SSE es un solo
        ' bit, así que HeadHairSlotMask sirve tal cual (allá RaceHairSecondBit devuelve 0).
        Dim sseHeadMask As UInteger = If(isSse,
            NpcMeshCollector.HeadPartHideMask(LastRenderData.HeadHairSlotMask, occupiedVisible, tablaDeSlots), 0UI)

        ' ⛔ POR QUÉ "Render headwear" OFF NO le saca la gorra al escudero, y por qué NO se arregla acá.
        ' `ClothesSquire` (BOD2 {30,33,34,35}) cubre cabeza y cuerpo en UNA sola malla, así que no cae en
        ' la categoría `Headwear` y ocultarlo entero escondería el traje. La idea era zapear por segmento
        ' su rebanada de cabeza — y NO SE PUEDE: medido, `Clothes\Squire\Squire.nif` no trae NINGÚN tag
        ' de segmento (sus dos shapes dan el set vacío), o sea que gorra y cuerpo son la misma geometría
        ' indiferenciada y no hay nada que seleccionar.
        ' Y el zap tampoco servía para el resto: de los 46 ARMO de Fallout que cubren cabeza Y cuerpo,
        ' sólo 4 mallas traen un tag de la región de cabeza (MHat, MHelmet, MHelmetDamaged,
        ' GrandmaMonkHat) y esas cuatro son mallas SEPARADAS cuyo candidato toma el slot del ARMA, así que
        ' ya salen como `Headwear` y el toggle ya las alcanza. Cero casos ganados ⇒ era código muerto.
        ' Alcance de lo que queda sin resolver: 8 NPC en Fallout (escuderos) y 3 en Skyrim, dos de los
        ' cuales son un fantasma y el jinete sin cabeza.
        Dim fase2FO4 As UInteger = 0UI
        If Not isSse Then
            fase2FO4 = NpcMeshCollector.SlotsDeFase2(tablaDeSlots, LastRenderData.HeadHairFirstBit) Or
                       NpcMeshCollector.SlotsDeFase2(tablaDeSlots, LastRenderData.HeadFacialHairMask)
        End If

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
                    If tablaDeSlots.Dueno(b) IsNot Nothing Then ownSb.Append($"{30 + b}→{System.IO.Path.GetFileName(tablaDeSlots.Dueno(b))} ")
                Next
            End If
            Dim ownDbg = ownSb.ToString().TrimEnd()
            Logger.LogLazy(Function() $"[OCCL] apply: headwear={rhDbg} armor={raDbg} underarmor={ruDbg} body={rbDbg} gore={rgDbg} occupiedVisible=0x{ovDbg:X} headMask=0x{hmDbg:X} groups=[{grpDbg}] owners=[{ownDbg}]")
        End If

        Dim hidden As Integer = 0
        Dim shown As Integer = 0
        For Each shape In LastRenderData.Shapes
            If shape Is Nothing Then Continue For
            Dim cat As MainForm.ShapeRenderCategory = MainForm.ShapeRenderCategory.Other
            LastRenderData.ShapeCategory.TryGetValue(shape, cat)
            ' Push the per-segment occlusion mask the render index filter (EnsureZapIndexBuffer) consumes.
            ' Head parts, GATED by Render headwear ("Render headwear" OFF ⇒ mask 0 ⇒ head parts revealed whole):
            '   • SSE: engine attach 0x140218200 fase 2 — el BOD2 COMPLETO del ítem que ocupa el slot de pelo
            '     (30+B), recomputado por render desde los grupos renderizados (sseHeadMask =
            '     NpcMeshCollector.HeadPartHideMask). NO occupiedVisible ∩ HeadOcclusionMask (eso daba sólo el
            '     bit del slot de pelo, dejando visibles las demás particiones del casco).
            '   • FO4: la rebanada del worn set renderizado que corresponde AL CANAL DE ESTE HEAD-PART
            '     (NpcMeshCollector.HeadPartChannelMask), no la unión plana de A|B|B+1|C. El driver
            '     0x140506460 recorre los canales por separado y se los aplica a TIPOS distintos: B/B+1 al
            '     head-part tipo 3 y C al tipo 4. Un tipo sin canal (ojos, cejas, nuca) recibe 0 — a esos
            '     sólo los alcanza la cascada del face-cull A, que es whole-node y la resuelve
            '     SelectWinningCandidates, no esta máscara per-segmento.
            ' Worn items y piel: la máscara de slots que NO ocupa el propio modelo, de la tabla de dueños
            ' (CoveredForShape). ⛔ Ya NO es "el OR de los otros grupos": esa rama era identidad de GRUPO y
            ' no sabía de slots vacíos. El slot 60 compartido lo resuelve ahora `occluderConDispositivo`.
            ' Estado del slot occluder: uno solo por actor, así que va igual en toda shape. Los head parts
            ' no tienen rama occluder (OccluderSlotMask = 0 abajo), así que ahí el valor no se lee.
            shape.OccluderConDispositivo = occluderConDispositivo
            ' Camino del motor de ESTA shape (fija el default de una partición fuera de banda) y slot
            ' occluder de la raza. Head parts: camino de head-part y SIN occluder — el driver de head-parts
            ' (0x140506460 / 0x1403C2940) no tiene rama de swap N+100 ni de occluder-order.
            shape.OcclusionAsWornItem = (cat <> MainForm.ShapeRenderCategory.HeadPart)
            shape.OccluderSlotMask = If(cat = MainForm.ShapeRenderCategory.HeadPart, 0UI, LastRenderData.PipboySlotMask)
            If cat = MainForm.ShapeRenderCategory.HeadPart Then
                If isSse Then
                    ' SSE fase 2: NO es por tipo. El walker 0x14021DF20 recorre TODO el subárbol de la
                    ' cabeza aplicando la misma máscara, así que acá va la del ítem del slot de pelo.
                    shape.CoveredSlotsMask = If(renderHeadwear, sseHeadMask, 0UI)
                Else
                    Dim hpType As Integer = -1
                    LastRenderData.ShapeHeadPartType.TryGetValue(shape, hpType)
                    Dim canal As UInteger = NpcMeshCollector.HeadPartChannelMask(
                        hpType, LastRenderData.HeadHairSlotMask, LastRenderData.HeadFacialHairMask)
                    ' (worn ∩ canal DEL TIPO) ∪ fase 2. El primer término es por tipo porque el driver
                    ' recorre los canales por separado y se los aplica a tipos distintos (B/B+1 al tipo 3
                    ' en 0x1405066B5, C al tipo 4); el segundo NO, porque el walker de la fase 2 corre
                    ' sobre el nodo de cara entero y no mira el tipo de nada.
                    shape.CoveredSlotsMask = If(renderHeadwear, (occupiedVisible And canal) Or fase2FO4, 0UI)
                End If
            Else
                ' Oclusión per-slot por DUEÑO, en LOS DOS JUEGOS. Toda malla no-headpart (piel desnuda Y
                ' outfits) recibe la máscara de slots que NO ocupa su propio modelo. La ley es la misma en
                ' los dos motores: visible ⟺ el slot lo ocupa un ítem con MI mismo model path
                ' (FO4 0x14035E595 _stricmp + 0x14035E5A6 cmove edi,eax; SSE 0x1417C91E8 + 0x14021DC50), y
                ' se oculta tanto si lo ocupa otro como si NO LO OCUPA NADIE (FO4 0x14035E563 cmp
                ' [rcx+r8+0x30],r12 + je; SSE 0x14021DC09). El self-exclude sale gratis: los slots que la
                ' propia malla ganó tienen su MISMO DictKey.
                ' ⛔ ACÁ ESTABA la rama de FO4 con `others` (unión de los OTROS grupos de groupSlots). Era
                ' identidad de GRUPO en vez de identidad de MODEL PATH, y además no sabía de slots vacíos —
                ' o sea la segunda causa de HIDE del motor no existía. Con esto hay UNA sola sede
                ' (TablaDeDuenosPorSlot + SlotsCubiertosPorOtroModelo) para los dos juegos.
                ' ⛔ Los ATTACHMENTS no entran acá. Montan por SOCKET, no por biped slot: no están en la
                ' tabla de bipeds, y el resolver per-slot del motor recorre LA TABLA (FO4 0x14035E3B0 /
                ' SSE 0x14021DAE0), no el árbol 3D. Sin este corte, `CoveredForShape` les devuelve
                ' 0xFFFFFFFF —no son dueños de nada— y con "huérfano = CUBIERTO" se les zapea cada
                ' segmento tagueado. Medido con el barrido: `Percy` (HandyRace) perdía
                ' `LegsHandyThruster1AR1A` entero (396/396), `HandyRearArmor` 300/452 y `TorsoHandy` 334/4698.
                Dim kindSh As MainForm.MeshCandidateKind = MainForm.MeshCandidateKind.Outfit
                LastRenderData.ShapeKind.TryGetValue(shape, kindSh)
                If Not NpcMeshCollector.EsAdjuntoDeBiped(kindSh) Then
                    shape.CoveredSlotsMask = 0UI
                Else
                    Dim shapeKey As String = Nothing
                    LastRenderData.MeshDictKeys.TryGetValue(shape, shapeKey)
                    shape.CoveredSlotsMask = CoveredForShape(shapeKey)
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
                    Dim occlDbg = BSTriShapeGeometry.ComputeHiddenTriangles(siDbg, cmDbg, shape.OccluderSlotMask, shape.OccluderConDispositivo)
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
                        Dim occlDbg = shape.NifContent.ComputeHiddenTrianglesDismember(shape.NifShape, cmDbg, shape.OcclusionAsWornItem)
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
        ' ⛔ El ÚNICO uso del control que queda, y es un EFECTO, no la ley: pedir el redibujado. Va
        ' condicionado porque el guard de arriba ya no exige control —el método calcula máscaras sobre
        ' `LastRenderData` y eso no necesita nada del render—. Sin este `IsNot Nothing` quedaba un
        ' NullReferenceException latente para todo llamador sin PreviewCtl.
        If PreviewCtl IsNot Nothing Then PreviewCtl.RefreshRender()
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

        ' HACER CURRENT EL CONTEXTO PROPIO ANTES DE BORRAR UN SOLO HANDLE GL.
        '
        ' Abajo decía que "el llamador ya tiene current el contexto dueño" y lo trataba como precondición
        ' del ciclo de vida. NINGÚN llamador lo garantiza: `EditFace_Form` invoca `_editorHost.Dispose()`
        ' justo después de `BeginTeardown()`, que no toca el contexto.
        '
        ' Los nombres GL son POR CONTEXTO, y `GenTexture` devuelve el menor libre. Con el editor de cara
        ' abierto sobre el MainForm, el latido de seguridad del MainForm hace current SU contexto cada ~1 s.
        ' Si el usuario cierra el editor justo después, estos `DeleteTexture`/`DeleteProgram` se disparan
        ' contra el contexto del MainForm y borran ids que allá pertenecen a texturas VIVAS: el preview
        ' principal pierde texturas sin ningún error, y el editor filtra las suyas de verdad.
        ' El comentario de EditFace_Form habla de "the shared GL context", pero `PreviewControl.New`
        ' construye el `GLControlSettings` SIN `SharedContext`: los contextos son independientes. La
        ' premisa de la que colgaba la precondición es falsa.
        '
        ' `EnsureContextCurrent` no hace `MakeCurrent` si ya lo está, así que en el camino sano no cuesta.
        Dim contextoListo As Boolean = False
        Try
            ' SE USA EL RETORNO, no se asume. Antes esto ponia `contextoListo = True` por el solo hecho
            ' de llamar: si `MakeCurrent` fallaba (control dispuesto entre el chequeo y la llamada, driver
            ' caido) se borraban handles creyendo que el contexto era el propio.
            If PreviewCtl IsNot Nothing Then contextoListo = PreviewCtl.EnsureContextCurrent()
        Catch ex As Exception
            ' Si el contexto ya murió (control disposed, driver caído) NO se borra nada: los handles se
            ' van con el contexto igual. Borrar a ciegas es lo único que sí puede dañar a otro contexto.
            Dim m = ex.Message
            Logger.LogLazy(Function() $"[AUDIT-TEARDOWN] no se pudo hacer current el contexto propio: {m} => NO se borra ningun handle")
        End Try
        ' [AUDIT-TEARDOWN] valida el arreglo del borrado en el contexto equivocado.
        If Logger.Enabled Then
            Dim aCtx = contextoListo
            Dim aTint = If(TintGpuCache Is Nothing, -1, TintGpuCache.Count)
            Dim aComp = (CompositorState IsNot Nothing)
            Logger.LogLazy(Function() $"[AUDIT-TEARDOWN] contextoPropioCurrent={aCtx} texturasEnTintGpuCache={aTint} compositorVivo={aComp}")
        End If

        Try
            If TintGpuCache IsNot Nothing Then
                If contextoListo Then
                    TintGpuCache.Clear()
                Else
                    ' Sin contexto no se BORRAN handles (los nombres de GL son por contexto), pero las
                    ' entradas se SUELTAN igual: cada una retiene un Texture_Loaded_Class, y dejarlas
                    ' vivas ata el diccionario entero a un host que se está muriendo.
                    TintGpuCache.OlvidarSinBorrar()
                End If
            End If
        Catch
            ' Defensive
        End Try

        ' Espejo CPU del cache de arriba: son arrays managed (sin handles GL), pero pueden ser cientos de MB
        ' a resoluciones altas, asi que se sueltan en el mismo punto del ciclo de vida.
        If TintCpuDecodeCache IsNot Nothing Then TintCpuDecodeCache.Clear()

        ' Libera los handles GL del compositor (program/VAO/VBO/FBO/texturas). El contexto propio ya se
        ' hizo current arriba; si no se pudo, no se borra nada (ver el bloque de arriba).
        ' Y SE SUELTA LA REFERENCIA PASE LO QUE PASE. Gatear TODO por `contextoListo` dejaba el
        ' `CompositorState` entero colgando del host cuando el contexto ya no estaba: sus handles GL se van
        ' con el contexto igual, pero lo ADMINISTRADO que tenga adentro no lo suelta nadie.
        Try
            If CompositorState IsNot Nothing Then
                If contextoListo Then CompositorState.Dispose()
                CompositorState = Nothing
            End If
        Catch
            ' Defensive
        End Try

        GC.SuppressFinalize(Me)
    End Sub

#End Region
End Class
