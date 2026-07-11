Imports System.IO
Imports System.Xml.Linq
Imports FO4_Base_Library

''' <summary>
''' skee64 TintData XML loader — <c>Data\SKSE\Plugins\NiOverride\TintData\*.xml</c>, la definición explícita del
''' compositor GPU de skee (armaduras teñibles + cabezas de razas custom con capas de máscara fijas). Parseo
''' FIEL al schema de <c>TintMaskInterface::ParseTintData</c> (skee64/TintMaskInterface.cpp), verificado línea a línea:
''' <code>
''' &lt;tintmasks&gt;
'''   &lt;object path="mesh\path.nif" override="bool" remappable="bool"&gt;
'''     &lt;geometry name="trishape" texture|diffuse="basetex" override="bool"&gt;
'''       &lt;mask path="mask.dds" color="skin|hair|RRGGBB" alpha="0.0" blend="overlay" type="mask|normal|solid|color|N" index="I" slot="S"/&gt;
'''     &lt;/geometry&gt;
'''   &lt;/object&gt;
''' &lt;/tintmasks&gt;
''' </code>
''' Reglas EXACTAS (del source): color "skin"→−2 / "hair"→−1 / hex(%x) / default 0xFFFFFF ; alpha double ;
''' blend default **"overlay"** (⚠ distinto del MASKT crudo del NIF, que es "normal") ; type default "mask"
''' ("mask"→1 "normal"→0 "solid"/"color"→2 / numérico) ; index explícito o auto-incremental por mask.
''' El motor de compose es el compartido (<see cref="SseOverlayCompositor.BuildSkeeMaskLayer"/> + ApplyOverlays).
''' </summary>
Public Module SseTintDataXml

    Public Structure TintDataLayer
        Public Index As Integer
        Public TexturePath As String
        Public ColorArgb As UInteger      ' incl. sentinels SkeePresetSkin(-2)/SkeePresetHair(-1)
        Public Alpha As Double
        Public Blend As SseOverlayCompositor.SseBlendMode
        Public LayerType As Integer       ' 0 Normal, 1 Mask, 2 Color
    End Structure

    ' meshPath(lower) → capas ordenables por Index. Cargado una vez por sesión.
    Private _cache As Dictionary(Of String, List(Of TintDataLayer)) = Nothing

    Public Sub Invalidate()
        _cache = Nothing
    End Sub

    ''' <summary>Parsea todos los <c>TintData\*.xml</c> bajo <paramref name="dataPath"/> a un mapa
    ''' meshPath(lower)→capas. Cacheado. Vacío si la carpeta no existe. No lanza (loguea y sigue por archivo).</summary>
    Public Function LoadAll(dataPath As String) As Dictionary(Of String, List(Of TintDataLayer))
        If _cache IsNot Nothing Then Return _cache
        Dim map As New Dictionary(Of String, List(Of TintDataLayer))(StringComparer.OrdinalIgnoreCase)
        Try
            Dim dir = Path.Combine(dataPath, "SKSE", "Plugins", "NiOverride", "TintData")
            If Directory.Exists(dir) Then
                For Each xmlFile In Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories)
                    Try
                        ParseInto(File.ReadAllText(xmlFile), map)
                    Catch ex As Exception
                        Logger.LogLazy(Function() $"[TINTDATA] parse failed '{xmlFile}': {ex.Message}")
                    End Try
                Next
            End If
        Catch ex As Exception
            Logger.LogLazy(Function() $"[TINTDATA] LoadAll failed: {ex.Message}")
        End Try
        _cache = map
        Return map
    End Function

    ''' <summary>Parsea UN documento XML (string) al mapa. Público para test unitario con XML sintético.</summary>
    Public Sub ParseInto(xml As String, map As Dictionary(Of String, List(Of TintDataLayer)))
        Dim doc = XDocument.Parse(xml)
        Dim root = doc.Root
        If root Is Nothing OrElse Not String.Equals(root.Name.LocalName, "tintmasks", StringComparison.OrdinalIgnoreCase) Then Return
        For Each obj In root.Elements()
            If Not String.Equals(obj.Name.LocalName, "object", StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim objPath = CStr(obj.Attribute("path"))
            If String.IsNullOrEmpty(objPath) Then Continue For
            Dim remappable = BoolAttr(obj, "remappable")
            Dim layers As List(Of TintDataLayer) = Nothing
            Dim key = objPath.Replace("/"c, "\"c).ToLowerInvariant()
            If Not map.TryGetValue(key, layers) Then layers = New List(Of TintDataLayer) : map(key) = layers

            Dim autoIndex As Integer = 0
            For Each geom In obj.Elements()
                If Not String.Equals(geom.Name.LocalName, "geometry", StringComparison.OrdinalIgnoreCase) Then Continue For
                If remappable Then autoIndex = 0   ' skee: remappable resetea el índice por geometry
                For Each maskEl In geom.Elements()
                    If Not String.Equals(maskEl.Name.LocalName, "mask", StringComparison.OrdinalIgnoreCase) Then Continue For
                    Dim ly As New TintDataLayer With {
                        .TexturePath = If(CStr(maskEl.Attribute("path")), ""),
                        .ColorArgb = ParseColor(CStr(maskEl.Attribute("color"))),
                        .Alpha = DoubleAttr(maskEl, "alpha"),
                        .Blend = SseOverlayCompositor.BlendModeFromName(If(CStr(maskEl.Attribute("blend")), "overlay")),
                        .LayerType = ParseType(CStr(maskEl.Attribute("type")))}
                    Dim explicitIdx As Integer
                    ly.Index = If(Integer.TryParse(CStr(maskEl.Attribute("index")), explicitIdx), explicitIdx, autoIndex)
                    layers.Add(ly)
                    autoIndex += 1
                Next
            Next
        Next
    End Sub

    ''' <summary>color: "skin"→SkeePresetSkin(-2), "hair"→SkeePresetHair(-1), hex RRGGBB (%x), o 0xFFFFFF default.</summary>
    Private Function ParseColor(color As String) As UInteger
        If String.IsNullOrEmpty(color) Then Return &HFFFFFFUI
        If color.StartsWith("skin", StringComparison.OrdinalIgnoreCase) Then Return SseOverlayCompositor.SkeePresetSkin
        If color.StartsWith("hair", StringComparison.OrdinalIgnoreCase) Then Return SseOverlayCompositor.SkeePresetHair
        Dim v As UInteger
        If UInteger.TryParse(color.TrimStart("#"c, "0"c, "x"c, "X"c), Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, v) Then Return v
        Return &HFFFFFFUI
    End Function

    ''' <summary>type: "mask"→1 "normal"→0 "solid"/"color"→2 / numérico. Default "mask"(1).</summary>
    Private Function ParseType(type As String) As Integer
        If String.IsNullOrEmpty(type) Then Return 1
        If type.StartsWith("mask", StringComparison.OrdinalIgnoreCase) Then Return 1
        If type.StartsWith("normal", StringComparison.OrdinalIgnoreCase) Then Return 0
        If type.StartsWith("solid", StringComparison.OrdinalIgnoreCase) OrElse type.StartsWith("color", StringComparison.OrdinalIgnoreCase) Then Return 2
        Dim n As Integer
        Return If(Integer.TryParse(type, n), n, 1)
    End Function

    Private Function BoolAttr(el As XElement, name As String) As Boolean
        Dim v = CStr(el.Attribute(name))
        Return Not String.IsNullOrEmpty(v) AndAlso (v = "1" OrElse String.Equals(v, "true", StringComparison.OrdinalIgnoreCase))
    End Function

    Private Function DoubleAttr(el As XElement, name As String) As Double
        Dim v As Double
        Return If(Double.TryParse(CStr(el.Attribute(name)), Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, v), v, 0.0)
    End Function

End Module
