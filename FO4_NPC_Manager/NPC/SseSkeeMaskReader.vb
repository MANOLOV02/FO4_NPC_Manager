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
    ''' <summary>Cheap gate: True iff this shape has skee mask layers que el compose VA A APLICAR — sin decodificar
    ''' ninguna textura.
    '''
    ''' <para>⭐⭐ ES <see cref="ReadNifMaskLayersRaw"/>, NO una condición paralela. Antes miraba SÓLO la presencia
    ''' de MASKT, mientras el lector descarta las capas con <c>opacity &lt;= 0</c> y el default de un MASKA AUSENTE
    ''' es <c>0.0</c> ⇒ un shape con MASKT y sin MASKA pasaba el gate y perdía TODAS sus capas. Eso producía dos
    ''' fallos silenciosos a la vez:
    '''   1. el bake entraba al camino PLEGADO, no componía nada, y salía por el return de
    '''      <c>WriteSseFaceDiffuseWithOverlays</c> SIN borrar los artefactos del fold anterior (stale al BSA);
    '''   2. el RENDER usaba ya <c>ReadNifMaskLayersRaw(...).Count &gt; 0</c> como gate ⇒ para ESE mismo NPC el
    '''      render NO plegaba y el bake SÍ. Violación directa de RENDER == BAKE.
    ''' Con el gate derivado del lector, los dos caminos comparten literalmente la misma condición y no se pueden
    ''' desincronizar. El costo sigue siendo el de leer tres bloques de extra data: cero decodes.</para>
    '''
    ''' <para>⚠️ RESIDUO CONOCIDO Y ACOTADO: el compose además saltea las capas cuya TEXTURA no se puede leer
    ''' (<see cref="ResolveLayersForCpu"/>, en paridad con el GPU). Eso NO se puede saber sin tocar el disco, así que
    ''' el gate puede dar True y el compose devolver False en ese caso. Es un ERROR real (la máscara existe y no se
    ''' pudo cargar), y por eso el bake lo REPORTA en vez de tragárselo — ver FaceGenBuilder.RecordTextureFailure.</para></summary>
    Public Function HasMaskLayers(nif As Nifcontent_Class_Manolo, shape As NiflySharp.INiShape) As Boolean
        Return ReadNifMaskLayersRaw(nif, shape).Count > 0
    End Function

    ''' <summary>Una capa skee CRUDA (sin texturas decodificadas): lo que se lee del NIF y se propaga al render.
    ''' ⭐ Existe porque CPU y GPU necesitan la máscara en formatos DISTINTOS: el CPU quiere los pixels decodificados
    ''' (<see cref="SseOverlayCompositor.SseOverlay"/>.Texture) y el GPU quiere los BYTES del DDS para subirlos como
    ''' textura (FaceTintLayerInput.LayerDdsBytes). Propagando la capa cruda, cada camino la adapta y NINGUNO queda
    ''' forzado — que es lo que exige la regla "el flag de la cámara es el único que decide CPU vs GPU".</summary>
    Public Structure SkeeMaskLayerRaw
        Public TexturePath As String
        Public ColorArgb As UInteger       ' MASKC crudo (puede ser un sentinel skin/hair; lo resuelve BuildSkeeMaskLayer)
        ''' <summary>⭐ True sólo si la capa DECLARA un MASKC. False = "esta capa no trae color".
        ''' Existe porque el valor de <see cref="ColorArgb"/> NO puede distinguir las dos cosas: el default que se
        ''' usaba para un MASKC ausente era <c>0xFFFFFFFF</c>, que ES el sentinel <c>SkeePresetHair</c> de skee
        ''' (SseOverlayCompositor.SkeePresetHair) ⇒ una capa sin color se interpretaba como "preset de pelo", y como
        ''' los dos callers pasan <c>hairRgb = Nothing</c>, terminaba cayendo al decode literal de 0xFFFFFFFF =
        ''' BLANCO OPACO pintado con la cobertura de la máscara. Un fallo de "no hay dato" disfrazado de dato.</summary>
        Public HasColor As Boolean
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
            ' ⭐ MASKC AUSENTE ≠ 0xFFFFFFFF. El raw int SÍ se trata como sentinel primero (skee hace eso: -1 =
            ' preset de pelo colisiona con blanco opaco) — pero ESO SÓLO VALE CUANDO LA CAPA DECLARA UN COLOR.
            ' Si no hay MASKC no hay color, y eso se propaga como HasColor=False en vez de inventar un valor que
            ' además es exactamente el sentinel. BuildSkeeMaskLayer resuelve "sin color" → blanco (mismo valor
            ' que antes, sin fuente para otro) pero por la rama correcta y sin pasar por la resolución de presets.
            Dim hasC As Boolean = (maskc IsNot Nothing AndAlso maskc.Data IsNot Nothing AndAlso i < maskc.Data.Count)
            outLayers.Add(New SkeeMaskLayerRaw With {
                .TexturePath = maskt.Data(i)?.Content,
                .ColorArgb = If(hasC, maskc.Data(i), &HFFFFFFFFUI),
                .HasColor = hasC,
                .Opacity = opacity,
                .LayerType = 1,                                                  ' MASKT del NIF = type Mask
                .Blend = SseOverlayCompositor.SseBlendMode.Normal})              ' MASKT del NIF = blend normal
        Next
        Return outLayers
    End Function

    ''' <summary>Resuelve capas crudas → <see cref="SseOverlayCompositor.SseOverlay"/> (decodifica las texturas y
    ''' sustituye los sentinels skin/hair). Es el adaptador del path CPU; el GPU usa su propio adaptador (sube los
    ''' bytes como textura) a partir de las MISMAS capas crudas.
    '''
    ''' <para>⭐⭐ UNA CAPA CUYA TEXTURA NO SE PUEDE LEER SE DESCARTA — igual que el GPU
    ''' (<c>SseFoldLayerStack.BuildSkeeGpuLayers</c>: <c>If texBytes Is Nothing Then Continue For</c>).
    ''' ⛔ Antes se agregaba igual con <c>Texture = Nothing</c>, y eso NO era inerte: en
    ''' <see cref="SseOverlayCompositor.ApplyOverlays"/> el sample de una capa sin textura vale <c>1.0</c> en los
    ''' cuatro canales, así que un type-1 (Mask) daba <c>la = 1.0 × color.a</c> ⇒ COBERTURA TOTAL: el color plano
    ''' de la capa pintaba LA CARA ENTERA. Y como el GPU sí la descartaba, el mismo NPC salía distinto según el
    ''' flag de la cámara — o sea que además rompía la paridad CPU==GPU.</para></summary>
    Public Function ResolveLayersForCpu(raw As IList(Of SkeeMaskLayerRaw), w As Integer, h As Integer,
                                        decode As Func(Of String, Integer, Integer, Single()),
                                        skinRgb As Double(), hairRgb As Double()) As List(Of SseOverlayCompositor.SseOverlay)
        Dim built As New List(Of SseOverlayCompositor.SseOverlay)
        If raw Is Nothing Then Return built
        For Each l In raw
            Dim texRgba As Single() = Nothing
            If l.LayerType <> 2 Then
                ' Type != 2 (Solid) EXIGE textura: sin ella no hay cobertura, sólo un color plano a pantalla completa.
                If String.IsNullOrEmpty(l.TexturePath) OrElse decode Is Nothing Then
                    Dim lpEmpty = l.TexturePath
                    Logger.LogLazy(Function() $"[SSE-SKEE] capa DESCARTADA (type={l.LayerType}): sin ruta de máscara ('{lpEmpty}') — sin textura la cobertura sería TOTAL")
                    Continue For
                End If
                texRgba = decode(l.TexturePath, w, h)
                If texRgba Is Nothing Then
                    Dim lpFail = l.TexturePath
                    Logger.LogLazy(Function() $"[SSE-SKEE] capa DESCARTADA: la máscara '{lpFail}' no se pudo leer/decodificar (= lo que hace el camino GPU)")
                    Continue For
                End If
            End If
            built.Add(SseOverlayCompositor.BuildSkeeMaskLayer(l.ColorArgb, l.Opacity, texRgba, l.LayerType, l.Blend, skinRgb, hairRgb, l.HasColor))
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
            If l.LayerType <> 2 Then                                    ' type 2 = sólido (no lleva textura)
                ' MISMA regla que ResolveLayersForCpu: sin textura la cobertura sería TOTAL. Ver la nota ahí.
                If String.IsNullOrEmpty(l.TexturePath) Then Continue For
                texRgba = decode(l.TexturePath, w, h)
                If texRgba Is Nothing Then
                    Dim lpTd = l.TexturePath
                    Logger.LogLazy(Function() $"[SSE-SKEE] TintData: capa DESCARTADA, máscara '{lpTd}' no decodifica")
                    Continue For
                End If
            End If
            ' hasColor:=True — el TintData XML declara el color explícitamente (a diferencia de un MASKC ausente).
            built.Add(SseOverlayCompositor.BuildSkeeMaskLayer(l.ColorArgb, l.Alpha, texRgba, l.LayerType, l.Blend, skinRgb, hairRgb, hasColor:=True))
        Next
        If built.Count = 0 Then Return False
        SseOverlayCompositor.ApplyOverlays(acc, built, w, h)
        Return True
    End Function

End Module
