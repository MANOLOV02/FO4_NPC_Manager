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

    ''' <summary>Una capa skee CRUDA (sin texturas decodificadas): lo que se lee del NIF y se propaga al render.
    ''' ⭐ Existe porque CPU y GPU necesitan la máscara en formatos DISTINTOS: el CPU quiere los pixels decodificados
    ''' (<see cref="SseOverlayCompositor.SseOverlay"/>.Texture) y el GPU quiere los BYTES del DDS para subirlos como
    ''' textura (FaceTintLayerInput.LayerDdsBytes). Propagando la capa cruda, cada camino la adapta y NINGUNO queda
    ''' forzado — que es lo que exige la regla "el flag de la cámara es el único que decide CPU vs GPU".</summary>
    Public Structure SkeeMaskLayerRaw
        Public TexturePath As String
        Public ColorArgb As UInteger       ' MASKC crudo (puede ser un sentinel skin/hair; lo resuelve BuildSkeeMaskLayer)
        Public Opacity As Double           ' MASKA
        Public LayerType As Integer        ' MASKT del NIF = 1 (Mask)
        Public Blend As SseOverlayCompositor.SseBlendMode
    End Structure

    ''' <summary>Lee las capas skee del shape SIN decodificar ninguna textura (barato). Orden = índice ascendente
    ''' (= el orden de composición de skee). Las capas con alpha<=0 se saltean (skee hace lo mismo). Vacío si el
    ''' shape no tiene MASKT. ⭐ FUENTE ÚNICA: la usan <see cref="ComposeNifMaskLayersIntoDiffuse"/> (CPU) y el
    ''' collector (que las propaga al render para el path GPU) ⇒ no hay dos parseos que se puedan desincronizar.</summary>
    Public Function ReadNifMaskLayersRaw(nif As Nifcontent_Class_Manolo, shape As NiflySharp.INiShape) As List(Of SkeeMaskLayerRaw)
        Dim outLayers As New List(Of SkeeMaskLayerRaw)
        If nif Is Nothing OrElse nif.Blocks Is Nothing OrElse shape Is Nothing OrElse shape.ExtraDataList Is Nothing Then Return outLayers
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
        If maskt Is Nothing OrElse maskt.Data Is Nothing OrElse maskt.Data.Count = 0 Then Return outLayers

        For i = 0 To maskt.Data.Count - 1
            Dim opacity As Double = If(maska IsNot Nothing AndAlso maska.Data IsNot Nothing AndAlso i < maska.Data.Count, maska.Data(i), 0.0)
            If opacity <= 0.0 Then Continue For                                   ' skee skips alpha==0 layers
            ' MASKC=hair-preset (-1) collides with opaque white; skee treats the raw int as the sentinel first.
            outLayers.Add(New SkeeMaskLayerRaw With {
                .TexturePath = maskt.Data(i)?.Content,
                .ColorArgb = If(maskc IsNot Nothing AndAlso maskc.Data IsNot Nothing AndAlso i < maskc.Data.Count, maskc.Data(i), &HFFFFFFFFUI),
                .Opacity = opacity,
                .LayerType = 1,                                                  ' MASKT del NIF = type Mask
                .Blend = SseOverlayCompositor.SseBlendMode.Normal})              ' MASKT del NIF = blend normal
        Next
        Return outLayers
    End Function

    ''' <summary>Resuelve capas crudas → <see cref="SseOverlayCompositor.SseOverlay"/> (decodifica las texturas y
    ''' sustituye los sentinels skin/hair). Es el adaptador del path CPU; el GPU usa su propio adaptador (sube los
    ''' bytes como textura) a partir de las MISMAS capas crudas.</summary>
    Public Function ResolveLayersForCpu(raw As IList(Of SkeeMaskLayerRaw), w As Integer, h As Integer,
                                        decode As Func(Of String, Integer, Integer, Single()),
                                        skinRgb As Double(), hairRgb As Double()) As List(Of SseOverlayCompositor.SseOverlay)
        Dim built As New List(Of SseOverlayCompositor.SseOverlay)
        If raw Is Nothing Then Return built
        For Each l In raw
            Dim texRgba As Single() = Nothing
            If Not String.IsNullOrEmpty(l.TexturePath) AndAlso l.LayerType <> 2 AndAlso decode IsNot Nothing Then
                texRgba = decode(l.TexturePath, w, h)
            End If
            built.Add(SseOverlayCompositor.BuildSkeeMaskLayer(l.ColorArgb, l.Opacity, texRgba, l.LayerType, l.Blend, skinRgb, hairRgb))
        Next
        Return built
    End Function

    Public Function ComposeNifMaskLayersIntoDiffuse(nif As Nifcontent_Class_Manolo, shape As NiflySharp.INiShape, w As Integer, h As Integer,
                                                    decode As Func(Of String, Integer, Integer, Single()),
                                                    skinRgb As Double(), hairRgb As Double(),
                                                    acc As Single()) As Boolean
        Dim raw = ReadNifMaskLayersRaw(nif, shape)
        If raw.Count = 0 Then Return False
        Dim layers = ResolveLayersForCpu(raw, w, h, decode, skinRgb, hairRgb)
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
                                                     decode As Func(Of String, Integer, Integer, Single()),
                                                     skinRgb As Double(), hairRgb As Double(),
                                                     acc As Single()) As Boolean
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
            Dim texRgba As Single() = Nothing
            If Not String.IsNullOrEmpty(l.TexturePath) AndAlso l.LayerType <> 2 Then texRgba = decode(l.TexturePath, w, h)  ' type 2 = sólido
            built.Add(SseOverlayCompositor.BuildSkeeMaskLayer(l.ColorArgb, l.Alpha, texRgba, l.LayerType, l.Blend, skinRgb, hairRgb))
        Next
        If built.Count = 0 Then Return False
        SseOverlayCompositor.ApplyOverlays(acc, built, w, h)
        Return True
    End Function

End Module
