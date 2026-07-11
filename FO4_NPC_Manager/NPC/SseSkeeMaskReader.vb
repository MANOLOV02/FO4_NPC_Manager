Imports System.Linq
Imports FO4_Base_Library

''' <summary>
''' SSE skee64 (RaceMenu/NiOverride) GPU TEXTURE COMPOSITOR — the producer side. Reads the per-index mask
''' layers skee's <c>TintMaskInterface</c> composites at runtime (and never bakes) from a shape's NIF extra
''' data, and composites them into a target texture via the shared <see cref="SseOverlayCompositor.ApplyOverlays"/>
''' engine (the same 16-blend / 3-type / straight-alpha-over math decoded from the RaceMenu <c>.fx</c>).
''' We bake them so the per-NPC diffuse is WYSIWYG, exactly like the RaceMenu face overlays.
'''
''' NIF layer source (TintMaskInterface.cpp:176-229 ApplyMasks):
'''   MASKT = <c>NiStringsExtraData</c> → per-index mask texture paths
'''   MASKC = <c>NiIntegersExtraData</c> → per-index colours (ARGB; skee presets −2 skin / −1 hair)
'''   MASKA = <c>NiFloatsExtraData</c> → per-index opacity
''' Compose order = ascending layer index (skee's std::map). Raw MASKT layers default to type Mask(1) +
''' technique "normal" (CDXNifTextureRenderer.h:24-30); a TintData XML can override type/blend/colour per index.
'''
''' TintData XML (<c>Data\SKSE\Plugins\NiOverride\TintData\*.xml</c>) is the dyeable-armor / explicit path — NOT
''' yet loaded here (rare on face NPCs; the MASKT extra-data path is the common one). See the TODO below.
''' </summary>
Public Module SseSkeeMaskReader

    ''' <summary>Read a shape's MASKT/MASKC/MASKA extra data into ordered skee mask layers and composite them
    ''' into <paramref name="acc"/> (linear RGBA, in place), via <see cref="SseOverlayCompositor.ApplyOverlays"/>.
    ''' Returns True iff at least one layer contributed. No-op (False) when the shape carries no MASKT — i.e.
    ''' vanilla / non-dyeable heads are byte-unchanged. Textures are decoded through <paramref name="decode"/>
    ''' (path → linear RGBA at w×h) so this stays render-agnostic.</summary>
    ''' <summary>Cheap gate: True iff the shape carries a non-empty MASKT (skee mask layers) — WITHOUT decoding
    ''' any texture. Lets the bake decide whether to do the expensive complexion decode+compose at all.</summary>
    Public Function HasMaskLayers(nif As Nifcontent_Class_Manolo, shape As NiflySharp.INiShape) As Boolean
        If nif Is Nothing OrElse nif.Blocks Is Nothing OrElse shape Is Nothing OrElse shape.ExtraDataList Is Nothing Then Return False
        Dim blocks = nif.Blocks
        For Each edRef In shape.ExtraDataList.References
            If edRef.Index < 0 OrElse edRef.Index >= blocks.Count Then Continue For
            Dim se = TryCast(blocks(edRef.Index), NiflySharp.Blocks.NiStringsExtraData)
            If se IsNot Nothing AndAlso se.Name?.String = "MASKT" AndAlso se.Data IsNot Nothing AndAlso se.Data.Count > 0 Then Return True
        Next
        Return False
    End Function

    Public Function ComposeNifMaskLayersIntoDiffuse(nif As Nifcontent_Class_Manolo, shape As NiflySharp.INiShape, w As Integer, h As Integer,
                                                    decode As Func(Of String, Integer, Integer, Double()),
                                                    skinRgb As Double(), hairRgb As Double(),
                                                    acc As Double()) As Boolean
        If nif Is Nothing OrElse nif.Blocks Is Nothing OrElse shape Is Nothing OrElse shape.ExtraDataList Is Nothing Then Return False
        Dim blocks = nif.Blocks

        Dim maskt As NiflySharp.Blocks.NiStringsExtraData = Nothing
        Dim maskc As NiflySharp.Blocks.NiIntegersExtraData = Nothing
        Dim maska As NiflySharp.Blocks.NiFloatsExtraData = Nothing
        For Each edRef In shape.ExtraDataList.References
            If edRef.Index < 0 OrElse edRef.Index >= blocks.Count Then Continue For
            Dim blk = blocks(edRef.Index)
            Dim nm = TryCast(blk, NiflySharp.Blocks.NiExtraData)?.Name?.String
            Select Case nm
                Case "MASKT" : maskt = TryCast(blk, NiflySharp.Blocks.NiStringsExtraData)
                Case "MASKC" : maskc = TryCast(blk, NiflySharp.Blocks.NiIntegersExtraData)
                Case "MASKA" : maska = TryCast(blk, NiflySharp.Blocks.NiFloatsExtraData)
            End Select
        Next
        If maskt Is Nothing OrElse maskt.Data Is Nothing OrElse maskt.Data.Count = 0 Then Return False

        ' Build one SseOverlay per index (ascending = skee compose order). Raw MASKT → type Mask(1), blend normal.
        Dim layers As New List(Of SseOverlayCompositor.SseOverlay)
        For i = 0 To maskt.Data.Count - 1
            Dim texPath = maskt.Data(i)?.Content
            Dim colorRaw As UInteger = If(maskc IsNot Nothing AndAlso maskc.Data IsNot Nothing AndAlso i < maskc.Data.Count, maskc.Data(i), &HFFFFFFFFUI)
            Dim opacity As Double = If(maska IsNot Nothing AndAlso maska.Data IsNot Nothing AndAlso i < maska.Data.Count, maska.Data(i), 0.0)
            If opacity <= 0.0 Then Continue For                                   ' skee skips alpha==0 layers
            Dim texRgba As Double() = Nothing
            If Not String.IsNullOrEmpty(texPath) AndAlso decode IsNot Nothing Then texRgba = decode(texPath, w, h)
            ' MASKC=hair-preset (−1) collides with opaque white; skee treats the raw int as the sentinel first.
            layers.Add(SseOverlayCompositor.BuildSkeeMaskLayer(colorRaw, opacity, texRgba,
                                                               layerType:=1, blend:=SseOverlayCompositor.SseBlendMode.Normal,
                                                               skinRgb:=skinRgb, hairRgb:=hairRgb))
        Next
        If layers.Count = 0 Then Return False
        SseOverlayCompositor.ApplyOverlays(acc, layers, w, h)
        Return True
    End Function

    ''' <summary>Compose skee TintData XML layers (Data\SKSE\Plugins\NiOverride\TintData\*.xml) whose
    ''' <c>&lt;object path&gt;</c> matches <paramref name="meshPath"/> INTO <paramref name="acc"/> (linear RGBA,
    ''' in place), via the shared <see cref="SseOverlayCompositor.ApplyOverlays"/>. Order = layer Index ascending
    ''' (= skee's std::map). Layer default blend = "overlay", type = "mask" (per TintMaskInterface::ParseTintData).
    ''' Returns True iff at least one layer contributed. No-op when no XML targets this mesh (the common case).</summary>
    Public Function ComposeTintDataLayersIntoDiffuse(meshPath As String, dataPath As String, w As Integer, h As Integer,
                                                     decode As Func(Of String, Integer, Integer, Double()),
                                                     skinRgb As Double(), hairRgb As Double(),
                                                     acc As Double()) As Boolean
        If String.IsNullOrEmpty(meshPath) OrElse acc Is Nothing OrElse decode Is Nothing Then Return False
        Dim map = SseTintDataXml.LoadAll(dataPath)
        If map Is Nothing OrElse map.Count = 0 Then Return False
        Dim key = meshPath.Replace("/"c, "\"c).ToLowerInvariant()
        Dim tdLayers As List(Of SseTintDataXml.TintDataLayer) = Nothing
        If Not map.TryGetValue(key, tdLayers) OrElse tdLayers Is Nothing OrElse tdLayers.Count = 0 Then Return False

        Dim ordered = tdLayers.OrderBy(Function(l) l.Index).ToList()
        Dim built As New List(Of SseOverlayCompositor.SseOverlay)
        For Each l In ordered
            If l.Alpha <= 0.0 Then Continue For
            Dim texRgba As Double() = Nothing
            If Not String.IsNullOrEmpty(l.TexturePath) AndAlso l.LayerType <> 2 Then texRgba = decode(l.TexturePath, w, h)  ' type 2 = sólido
            built.Add(SseOverlayCompositor.BuildSkeeMaskLayer(l.ColorArgb, l.Alpha, texRgba, l.LayerType, l.Blend, skinRgb, hairRgb))
        Next
        If built.Count = 0 Then Return False
        SseOverlayCompositor.ApplyOverlays(acc, built, w, h)
        Return True
    End Function

End Module
