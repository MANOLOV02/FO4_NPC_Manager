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

    ''' <summary>Create a resolver fed by an immutable slider snapshot. The dict is held
    ''' by reference — caller is responsible for not mutating it during render. Pass a
    ''' fresh dict for each new state.</summary>
    Public Sub New(sliders As Dictionary(Of String, Single),
                   meshDictKeys As Dictionary(Of IRenderableShape, String))
        _sliders = If(sliders, New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase))
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
            NpcPreviewLog.LogLazy(Function() $"  [BSMR] shape='{shapeName}' meshKey='{meshKey}' → no PIRT .tri found")
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
        Dim nifVerts = geom.NifLocalVertices.Length
        Dim shapeKeys = pirt.ShapeMorphs.Keys.ToList()
        NpcPreviewLog.LogLazy(Function() $"  [BSMR-PROBE] nifShape='{shapeName}' nifVerts={nifVerts} pirt_shapes=[{String.Join(",", shapeKeys)}] resolvedTriShape={If(resolvedTriShapeName, "<no match>")}")

        If resolvedTriShapeName Is Nothing Then
            For Each kv In _sliders
                If Math.Abs(kv.Value) < 0.001F Then Continue For
                If BodySlideTriResolver.IsExcludedSliderName(kv.Key) Then Continue For
                Dim sliderLocal = kv.Key
                NpcPreviewLog.LogLazy(Function() $"    [BSMR-DROP] slider='{sliderLocal}' — NIF shape '{shapeName}' not in .tri (LM would also skip)")
            Next
            Return plan
        End If

        For Each kv In _sliders
            Dim sliderName = kv.Key
            Dim weight = kv.Value
            If Math.Abs(weight) < 0.001F Then Continue For
            If BodySlideTriResolver.IsExcludedSliderName(sliderName) Then Continue For

            Dim morph = pirt.GetMorph(resolvedTriShapeName, sliderName)
            If morph Is Nothing OrElse morph.Offsets.Count = 0 Then
                Dim sLocal = sliderName
                Dim triKeyMiss = resolvedTriShapeName
                NpcPreviewLog.LogLazy(Function() $"    [BSMR-MISS] slider='{sLocal}' has no morph entry for tri shape '{triKeyMiss}'")
                Continue For
            End If

            ' Index-range probe vs NIF vertex count. MorphEngine silently drops indices
            ' >= nifVerts; high oob count means the .tri targets a body with more verts than
            ' our NIF (CBBE Curvy build vs vanilla, etc.) and the slider will look broken.
            Dim maxIdx As Integer = -1
            Dim oobCount As Integer = 0
            For Each off In morph.Offsets
                If off.Key > maxIdx Then maxIdx = off.Key
                If off.Key >= nifVerts Then oobCount += 1
            Next

            Dim deltas As New List(Of MorphData)(morph.Offsets.Count)
            For Each off In morph.Offsets
                deltas.Add(New MorphData With {.index = off.Key, .PosDiff = off.Value})
            Next

            Dim deltaTotal = morph.Offsets.Count
            Dim sName = sliderName
            Dim mIdx = maxIdx
            Dim oobC = oobCount
            Dim w = weight
            Dim triKey = resolvedTriShapeName
            NpcPreviewLog.LogLazy(Function() $"    [BSMR-APPLY] slider='{sName}' triShape='{triKey}' w={w:F3} morphVerts={deltaTotal} maxIdx={mIdx} oobVsNif={oobC}{If(oobC > 0, " (out-of-range deltas dropped)", "")}")

            plan.Channels.Add(New MorphChannel(sliderName, weight, deltas))
        Next

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
