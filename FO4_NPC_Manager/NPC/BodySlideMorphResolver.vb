Imports FO4_Base_Library

' ==========================================================================
' BodySlide vertex morph resolver for FO4_NPC_Manager.
'
' Consumes a unified slider dict {sliderName -> weight} (e.g. from a LooksMenu
' preset's BodyMorphs field) and applies it to ALL shapes that carry a PIRT .tri
' defining the matching morph name. A single slider can affect multiple shapes
' (typical: a CBBE BigBelly slider hits the body shape, the genitals shape, etc.)
' or none (a slider that the loaded body doesn't define is silently skipped on
' that shape).
'
' Mirrors Wardrobe_Manager's SliderMorphResolver pattern (MorphingHelper.vb:345)
' adapted to NPC_Manager's per-shape .tri resolution: WM keeps morph deltas in
' Shape_class.MorphDiffs (built from OSD blocks of an .osp project); we read them
' fresh from the PIRT .tri because NPC_Manager has no .osp.
'
' Format invariant: PIRT only. Never touches FRTRI003. See BodySlideTriResolver.
' ==========================================================================

Public Class BodySlideMorphResolver
    Implements IMorphResolver

    Private ReadOnly _sliders As Dictionary(Of String, Single)
    Private ReadOnly _meshDictKeys As Dictionary(Of IRenderableShape, String)

    ''' <summary>Create a resolver over a COPY of the caller's slider dict. The copy is deliberate:
    ''' callers hand us the live LooksmenuPreset.BodyMorphSliders, which the UI thread mutates while
    ''' the user drags a slider in Edit Body or applies an LM preset — and ResolveMorphPlan iterates
    ''' it from the render pipeline's Parallel.ForEach. Holding it by reference let a concurrent
    ''' mutation throw InvalidOperationException mid-iteration (swallowed upstream → shape rendered
    ''' unmorphed) or observe a half-updated dict. The resolver is rebuilt on every refresh
    ''' (BuildCompositeMorphResolver), so the snapshot is always the current state.</summary>
    Public Sub New(sliders As Dictionary(Of String, Single),
                   meshDictKeys As Dictionary(Of IRenderableShape, String))
        _sliders = If(sliders Is Nothing,
                      New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase),
                      New Dictionary(Of String, Single)(sliders, StringComparer.OrdinalIgnoreCase))
        _meshDictKeys = meshDictKeys
    End Sub

    Public Function ResolveMorphPlan(shape As IRenderableShape, geom As SkinnedGeometry) As MorphPlan _
            Implements IMorphResolver.ResolveMorphPlan
        Dim plan As New MorphPlan()
        If _sliders.Count = 0 OrElse shape Is Nothing Then Return plan

        Dim shapeName = shape.ShapeName
        If String.IsNullOrEmpty(shapeName) Then Return plan

        Dim meshKey As String = Nothing
        If _meshDictKeys IsNot Nothing Then _meshDictKeys.TryGetValue(shape, meshKey)

        Dim pirt = BodySlideTriResolver.ResolveAndLoad(shape, meshKey)
        If pirt Is Nothing Then
            If Logger.Enabled Then Logger.LogLazy(Function() $"[BODYSLIDE] shape='{shapeName}' → NO PIRT (BODYTRI unresolved / missing / non-PIRT). sliders={_sliders.Count}")
            Return plan
        End If

        ' Resolve the shape name match using LooksMenu's binding semantics
        ' (BodyMorphInterface.cpp:1366-1384 BodyMorphProcessor::Process):
        '
        '   At NIF load LM iterates the SHAPE NAMES the .tri declares (trishapeMap.first),
        '   and for each one calls nif.GetObjectByName(<.tri shape name>). If the NIF has
        '   a child with that exact name, LM stamps it with MORPH_FILE+MORPH_SHAPE extras
        '   pointing back at the .tri shape name. Render-time apply then uses MORPH_SHAPE
        '   (the .tri's name), NOT the NIF child's display name.
        '
        ' The direction is .tri-authoritative: the .tri owns the set of shape names; the NIF
        ' responds by exposing children that match. This is what lets a vanilla Cait NIF +
        ' an arbitrary BodySlide .tri work as long as the .tri's "BaseFemaleBody" entry
        ' matches the NIF's "BaseFemaleBody" child.
        '
        ' Replication here: we get one shape at a time (the host loop iterates renderable
        ' shapes and calls us per shape). For each one we ask "is THIS NIF child's name a
        ' key in the .tri?". Same outcome as LM doing nif.GetObjectByName(<.tri key>) —
        ' just driven from the NIF side because that's the iteration order of the host.
        Dim resolvedTriShapeName = ResolveTriShapeKey(pirt, shapeName)

        If resolvedTriShapeName Is Nothing Then
            If Logger.Enabled Then
                Dim triKeys = String.Join(", ", pirt.ShapeMorphs.Keys)
                Logger.LogLazy(Function() $"[BODYSLIDE] shape='{shapeName}' PIRT loaded but shape-name NOT in .tri (case-sensitive). .tri keys=[{triKeys}]")
            End If
            Return plan
        End If

        For Each kv In _sliders
            Dim sliderName = kv.Key
            Dim weight = kv.Value
            If Math.Abs(weight) < 0.001F Then Continue For
            If BodySlideTriResolver.IsExcludedSliderName(sliderName) Then Continue For

            Dim morph = pirt.GetMorph(resolvedTriShapeName, sliderName)
            If morph Is Nothing OrElse morph.Offsets.Count = 0 Then
                Continue For
            End If

            Dim deltas As New List(Of MorphData)(morph.Offsets.Count)
            For Each off In morph.Offsets
                deltas.Add(New MorphData With {.index = off.Key, .PosDiff = off.Value})
            Next

            ' applyCkBlockGate:=False — estos deltas salen de un .tri PIRT de BodySlide (body morphs),
            ' no de un .tri de FaceGen. El gate por bloques de 4 indices es la ley del applier de CABEZA
            ' del CK; a los body morphs los aplican f4ee y skee64 recorriendo m_vertexDeltas y sumando
            ' cada entrada SIN gate alguno (BodyMorphInterface.cpp, TriShapePacked/FullVertexData::
            ' ApplyMorph). Con el gate prendido el preview ocultaba deltas que el juego si aplica.
            plan.Channels.Add(New MorphChannel(sliderName, weight, deltas,
                                               isZap:=False, engineApplied:=True, applyCkBlockGate:=False))
        Next

        If Logger.Enabled Then
            Dim chCount = plan.Channels.Count
            Logger.LogLazy(Function() $"[BODYSLIDE] shape='{shapeName}' matched .tri key='{resolvedTriShapeName}' → channels emitted={chCount} (of {_sliders.Count} sliders)")
        End If
        Return plan
    End Function

    ''' <summary>Find the .tri shape-name key that this NIF child should bind to. Mirrors
    ''' LooksMenu's BodyMorphProcessor::Process (BodyMorphInterface.cpp:1369-1372) exactly:
    ''' iterates the .tri's shape names and asks the NIF for a child with that EXACT name
    ''' (BSAutoFixedString is case-sensitive). We answer from the inverse direction (host
    ''' gives us the NIF child; we look it up in the .tri), so the lookup is a single
    ''' dict probe.
    '''
    ''' No case-insensitive fallback — LM doesn't have one and we mirror its behaviour to
    ''' the letter. If a body pack ships "BaseFemaleBody" in the NIF and "BaseFemalebody"
    ''' in the .tri it's broken in-game too, and pretending to fix it in NPC_Manager would
    ''' diverge the preview from what the user will see in Fallout 4.</summary>
    Private Shared Function ResolveTriShapeKey(pirt As TriFile, nifShapeName As String) As String
        If pirt Is Nothing OrElse String.IsNullOrEmpty(nifShapeName) Then Return Nothing
        If pirt.ShapeMorphs.ContainsKey(nifShapeName) Then Return nifShapeName
        Return Nothing
    End Function
End Class
