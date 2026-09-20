Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion
Imports MaterialLib

''' <summary>LAS FORMAS DIBUJABLES de un head part, con su cadena de extras y su conjunto de texturas
''' ya aplicado. Una sola casa para los DOS que lo necesitan: el selector
''' (<see cref="HeadPartPicker_Form"/>) y el editor (<see cref="HeadPartEditor_Form"/>).
'''
''' <para>⛔ Vive acá y no adentro del selector porque el editor necesita exactamente lo mismo, y dos
''' copias de esto divergen en lo que menos se nota: el <c>TNAM</c> de un HDPT de ojos ES la diffuse del
''' ojo, así que una copia que se olvide de aplicarlo muestra todos los ojos marrones. El selector ya
''' tenía el paso y el comentario que lo explica; duplicarlo era pedirle al editor que lo redescubriera.</para>
'''
''' <para>⛔ Y resuelve POR LA SEDE (<see cref="ResolucionDeHeadParts"/>), así que un head part
''' BORRADOR se previsualiza igual que uno real —incluidos su <c>TNAM</c> borrador y sus extras
''' borradores—. Ésa es la propiedad que hace del preview el TESTIGO del cableado: si el borrador no se
''' ve acá, la sede no está bien conectada.</para></summary>
Friend Module PreviewDeHeadPart

    ''' <summary>Recorre el HDPT y todos sus extras (<c>HNAM</c>, recursivo, con ciclos cerrados adentro
    ''' del enumerador compartido) y devuelve las formas de cada malla, con el <c>TNAM</c> de CADA nodo
    ''' aplicado a SUS propias formas — los extras traen el suyo y lo aplican en su vuelta.
    ''' <para>Devuelve también cuántos nodos tenía la cadena, para que el llamador pueda distinguir
    ''' «no resolvió el record» de «resolvió y ninguna malla se pudo cargar».</para></summary>
    Friend Function FormasDeLaCadena(hdptFormID As UInteger,
                                     res As ResolucionDeHeadParts) _
                                     As (Formas As List(Of IRenderableShape), NodosDeLaCadena As Integer)
        Dim allShapes As New List(Of IRenderableShape)
        Dim chainCount As Integer = 0
        If res Is Nothing OrElse hdptFormID = 0UI Then Return (allShapes, 0)

        For Each chainEntry In HeadPartResolver.EnumerateHdptChain({hdptFormID}, res)
            Dim hdpt = chainEntry.Hdpt
            chainCount += 1
            If String.IsNullOrEmpty(hdpt.ModelFileName) Then Continue For

            ' Los bytes del NIF por el FilesDictionary — el mismo camino que usa el render.
            Dim dictKey = NormalizarClaveDeMalla(hdpt.ModelFileName)
            Dim loc As FilesDictionary_class.File_Location = Nothing
            If Not FilesDictionary_class.Dictionary.TryGetValue(dictKey, loc) Then Continue For
            Dim bytes As Byte() = Nothing
            Try
                bytes = loc.GetBytes()
            Catch
            End Try
            If bytes Is Nothing OrElse bytes.Length = 0 Then Continue For

            Dim nif As New Nifcontent_Class_Manolo()
            Try
                nif.Load_Manolo(bytes)
            Catch
                Continue For
            End Try

            Dim shapes = NifRenderableShape.FromNif(nif)
            If shapes Is Nothing OrElse Not shapes.Any() Then Continue For

            ' El TNAM de ESTE nodo sobre SUS formas. Los HDPT de ojos vanilla comparten
            ' femaleeyes.nif y cada color tiene su propio TXST: sin este paso todos salen marrones.
            If hdpt.TextureSet <> 0UI Then
                Dim txst = res.Txst(hdpt.TextureSet)
                If txst IsNot Nothing Then
                    For Each shape In shapes
                        MaterialResolver.EnsureShapeMaterialResolved(shape)
                        Dim relatedMaterial = shape.ShapeMaterial
                        If relatedMaterial Is Nothing Then Continue For
                        ' alphaTestWriteDecision se OMITE: esto es PREVIEW, camino de render puro que no
                        ' escribe NIF.
                        NpcMaterialResolver.ApplyTextureSetOverrides(txst, relatedMaterial,
                                                                    hdpt.UsaTexturaDelCuerpo(),
                                                                    shape.NifShape, shape.NifContent,
                                                                    isHeadPartTextureSet:=True)
                    Next
                End If
            End If

            allShapes.AddRange(shapes)
        Next
        Return (allShapes, chainCount)
    End Function

    ''' <summary>Las claves del FilesDictionary son rutas en minúsculas que empiezan con "meshes\". El
    ''' <c>MODL</c> del record puede traer el prefijo o no: se normalizan las dos formas.</summary>
    Friend Function NormalizarClaveDeMalla(rawPath As String) As String
        If String.IsNullOrEmpty(rawPath) Then Return ""
        Dim p = rawPath.Replace("/"c, "\"c).Trim().ToLowerInvariant()
        If Not p.StartsWith("meshes\") Then p = "meshes\" & p
        Return p
    End Function

End Module
