Imports FO4_Base_Library
Imports NiflySharp
Imports OpenTK.Mathematics

''' <summary>
''' Export the currently-rendered NPC scene (the live <see cref="PreviewModel.RenderableMesh"/> list) to a NIF
''' on disk. This is pure build/bake/serialize domain logic extracted VERBATIM from MainForm's
''' <c>ButtonSaveSceneNif_Click</c> handler — the handler keeps only the SaveFileDialog, the
''' default-name plumbing and the result MessageBoxes; everything that constructs the destination
''' NIF, bakes world-pose vertices via the per-vertex skin matrices, computes the normal/tangent/
''' bitangent transforms, runs the hair-zap vertex compaction (oldToNew remap) and serializes the
''' shapes lives here.
'''
''' Faithful code-motion only: the statements below are the originals, with member access rebound
''' from <c>_previewControl.Model.meshes</c> to the injected <paramref name="meshes"/> parameter.
''' No math, ordering, type or zap/skin/serialize logic was changed.
''' </summary>
Public NotInheritable Class SceneNifExporter

    Private Sub New()
    End Sub

    ''' <summary>Cantidad máxima de vértices que un shape puede tener y seguir siendo indexable por
    ''' los triángulos del NIF. <c>NiflySharp.Structs.Triangle</c> guarda V1/V2/V3 como
    ''' <c>UShort</c> ⇒ índices válidos 0..65535 ⇒ 65536 vértices. Por encima de esto el cast a
    ''' UShort trunca en silencio (wraparound) y la malla sale destrozada; no hay camino de
    ''' índices de 32 bits en este formato, así que el shape se falla explícitamente.</summary>
    Private Const MaxUShortIndexableVerts As Integer = 65536

    ''' <summary>Outcome of an <see cref="Export"/> call. Carries the counts + per-shape failure
    ''' text the UI needs to build its summary MessageBox, plus a distinct save-error message for
    ''' the case where <c>Save_As_Manolo</c> threw (so the UI can show the error-icon box and
    ''' early-return exactly as the original handler did).</summary>
    Public Structure ExportResult
        ''' <summary>Count of shapes successfully written into the destination NIF.</summary>
        Public ShapesWritten As Integer
        ''' <summary>Count of shapes that failed (clone failure, missing data, inconsistent zap
        ''' compaction, or a per-shape exception).</summary>
        Public ShapesFailed As Integer
        ''' <summary>Per-shape failure details, one line per failure (same text the original
        ''' handler accumulated).</summary>
        Public FailureDetails As String
        ''' <summary>Non-Nothing only when the final <c>Save_As_Manolo</c> threw; carries the
        ''' exception message so the UI can show the "Failed to write {path}" error box.</summary>
        Public SaveError As String
        ''' <summary>Count of skin shapes whose shader got the NPC's skin tone written into it.</summary>
        Public SkinToneWritten As Integer
        ''' <summary>Count of skin shapes where the tone could NOT be written because the shape's shader
        ''' type does not carry the field. No es un fallo del export — el shape se escribió igual — pero
        ''' TAMPOCO es silencio: el usuario pidió que el modelo matcheara y en esos shapes no matchea.</summary>
        Public SkinToneSkipped As Integer
        ''' <summary>Una línea por shape salteado, para el cuadro de la UI.</summary>
        Public SkinToneSkippedDetails As String
        ''' <summary>Non-Nothing sólo si el usuario pidió el locator del menú de carga y NO se pudo
        ''' escribir. El NIF se guarda igual (la geometría está bien), pero callarlo le vendería un
        ''' "Export OK" a un archivo que no cumple lo que la opción prometía.</summary>
        Public LoadScreenNodeError As String
    End Structure

    ''' <summary>OpenTK.Mathematics.Vector3 → System.Numerics.Vector3 (mismo nombre, structs distintos).</summary>
    Private Shared Function ToNumerics(v As Vector3) As System.Numerics.Vector3
        Return New System.Numerics.Vector3(v.X, v.Y, v.Z)
    End Function

    ''' <summary>
    ''' Recorta la porción <c>BSTriShape</c> de un bloque dinámico, SIN transcribir la lista de campos:
    ''' se serializa el bloque y se lee de vuelta en un <c>BSTriShape</c>, que consume exactamente los
    ''' campos de SU nivel y descarta lo que agregó la subclase. El recorte lo define el <c>Sync</c>
    ''' GENERADO, así que sigue siendo correcto cuando NiflySharp agregue un campo — a diferencia de
    ''' una copia campo por campo, que se pudre en silencio (es la forma exacta de los defectos que
    ''' este export ya se comió: UVs y pesos destruidos por un resize que nadie repuso).
    ''' <para>El <c>Sync</c> generado invoca <c>BeforeSync</c>/<c>AfterSync</c> por su cuenta, o sea que
    ''' el round-trip pasa por los mismos hooks que un guardado real. Refs y strings viajan como
    ''' ÍNDICES del header y el bloque ya vive en este mismo NIF, así que siguen siendo válidos.</para>
    ''' <para>⛔ Lo único que el índice NO trae es el TEXTO del string: el guardado rearma la tabla del
    ''' header desde los textos resueltos, así que sin copiarlos los shapes salen SIN NOMBRE (medido:
    ''' <c>Header.strings</c> 12 → 7). Se copian por posición sobre <c>INiObject.StringRefs</c>, que
    ''' también enumera el generador.</para>
    ''' <para>VERIFICADO con Tools\NifFullDiff sobre el FaceGeom vanilla 0001325c: 91.700 hojas
    ''' comparadas y las únicas diferencias son el tipo del bloque, el campo <c>DynamicVertexSize</c>
    ''' del descriptor (que en un shape no dinámico DEBE ser 0) y el bookkeeping del header. El
    ''' archivo pasa de 152.287 a 108.000 bytes: la geometría dejó de estar duplicada.</para>
    ''' </summary>
    Private Shared Function RecortarABSTriShape(nif As Nifcontent_Class_Manolo,
                                                dyn As NiflySharp.Blocks.BSDynamicTriShape) As NiflySharp.Blocks.BSTriShape
        Using ms As New IO.MemoryStream()
            Dim w As New NiflySharp.Stream.NiStreamWriter(ms, nif)
            dyn.Sync(New NiflySharp.Stream.NiStreamReversible(w))
            ms.Position = 0
            Dim r As New NiflySharp.Stream.NiStreamReader(ms, nif)
            Dim plano As New NiflySharp.Blocks.BSTriShape()
            plano.Sync(New NiflySharp.Stream.NiStreamReversible(r))

            Dim srcRefs = dyn.StringRefs.ToList()
            Dim dstRefs = plano.StringRefs.ToList()
            If srcRefs.Count <> dstRefs.Count Then Return Nothing   ' antes que guardar algo mutilado
            For i = 0 To srcRefs.Count - 1
                dstRefs(i).String = srcRefs(i).String
            Next
            Return plano
        End Using
    End Function



    ''' <summary>
    ''' Máscara de vértices que NO viajan al NIF, con los DOS mecanismos con que el render oculta
    ''' geometría:
    ''' <para>(1) VERTEX-ZAP: <c>ApplyZaps=True</c> + <c>VertexMask(i)=-1</c> (pelo). Se cae el vértice y
    ''' todo triángulo que lo toque — el predicado exacto de Render.vb. AGNOSTICO a qué partición se
    ''' zapeó (Top / Long / Both): keyea puro en <c>VertexMask(i)=-1</c>.</para>
    ''' <para>(2) OCLUSION POR SEGMENTO/PARTICION: el cuerpo/armor tapado por otra prenda. NO toca
    ''' <c>VertexMask</c> — el render la aplica FILTRANDO EL INDEX BUFFER en <c>EnsureZapIndexBuffer</c>
    ''' (Render.vb:2689+). Un vértice se cae si ningún triángulo VIVO lo referencia.</para>
    ''' <para>⭐ El set NO se recalcula acá: se LEE el que el render usó para dibujar
    ''' (<c>mesh.HiddenTriangles</c>). Recalcularlo sería reproducir el criterio (toggle
    ''' DrawHiddenSegments + rama FO4-por-segmento / SSE-por-partición + máscaras), y dos copias del
    ''' criterio se desincronizan en cuanto alguien toque el render. Está indexado por índice de
    ''' triángulo del shape = el MISMO orden que <c>liveGeom.Indices</c>.</para>
    ''' <para>⛔ ESTÁ EXTRAÍDA A PROPÓSITO: la consumen el export (para compactar) Y
    ''' <see cref="MeasureBakedBounds"/> (para que el diálogo muestre el bbox que el export va a
    ''' producir). Con dos copias, el número que ve el usuario y el que se escribe divergen en cuanto
    ''' alguien toque una sola.</para>
    ''' </summary>
    ''' <param name="hasOcclusion">Sale True si algún triángulo está oculto por segmento/partición.</param>
    ''' <param name="allTrianglesHidden">Sale True si NO queda ningún triángulo vivo: el shape entero es
    ''' invisible y no se exporta (no es un fallo — en pantalla tampoco hay nada).</param>
    Private Shared Function ComputeVertexDropMask(mesh As PreviewModel.RenderableMesh,
                                                  srcRenderable As IRenderableShape,
                                                  liveGeom As SkinnedGeometry,
                                                  n As Integer,
                                                  ByRef hasOcclusion As Boolean,
                                                  ByRef allTrianglesHidden As Boolean) As Boolean()
        Dim vm = liveGeom.VertexMask
        Dim hasZap As Boolean = srcRenderable.ApplyZaps AndAlso vm IsNot Nothing AndAlso vm.Length = n
        Dim occl As Boolean() = mesh.HiddenTriangles
        hasOcclusion = occl IsNot Nothing AndAlso Array.IndexOf(occl, True) >= 0
        allTrianglesHidden = False

        ' El segundo término sólo corre con hasOcclusion, así que el camino sin oclusión queda
        ' idéntico byte a byte al de antes.
        Dim vertexDropped(n - 1) As Boolean
        For i = 0 To n - 1
            vertexDropped(i) = hasZap AndAlso vm(i) = -1.0F
        Next
        If Not hasOcclusion Then Return vertexDropped

        Dim idxAll = liveGeom.Indices
        Dim referenced(n - 1) As Boolean
        Dim liveTris As Integer = 0
        If idxAll IsNot Nothing Then
            Dim t2 = 0
            While t2 + 2 < idxAll.Length
                Dim ta = CInt(idxAll(t2)), tb = CInt(idxAll(t2 + 1)), tc = CInt(idxAll(t2 + 2))
                Dim triIdx = t2 \ 3
                Dim hidden = (triIdx < occl.Length AndAlso occl(triIdx))
                If Not hidden AndAlso ta >= 0 AndAlso ta < n AndAlso tb >= 0 AndAlso tb < n AndAlso tc >= 0 AndAlso tc < n Then
                    If Not vertexDropped(ta) AndAlso Not vertexDropped(tb) AndAlso Not vertexDropped(tc) Then
                        referenced(ta) = True : referenced(tb) = True : referenced(tc) = True
                        liveTris += 1
                    End If
                End If
                t2 += 3
            End While
        End If
        If liveTris = 0 Then
            allTrianglesHidden = True
            Return vertexDropped
        End If
        For i = 0 To n - 1
            If Not referenced(i) Then vertexDropped(i) = True
        Next
        Return vertexDropped
    End Function

    ''' <summary>Bounding box del modelo tal como lo dejaría un export UNSKINNED, en las coordenadas que
    ''' quedarían en el NIF destino, más el conteo de vértices que lo produjeron (0 = no hay nada que
    ''' medir).</summary>
    Public Structure BakedBounds
        Public Min As System.Numerics.Vector3
        Public Max As System.Numerics.Vector3
        Public VertexCount As Integer
        Public ReadOnly Property IsUsable As Boolean
            Get
                Return VertexCount > 0
            End Get
        End Property
        Public ReadOnly Property Height As Single
            Get
                Return If(VertexCount > 0, Max.Z - Min.Z, 0.0F)
            End Get
        End Property
        ''' <summary>Posición por defecto del locator del menú de carga: centrado en XY y a
        ''' <paramref name="heightFraction"/> del alto desde la base.</summary>
        Public Function ZoomTargetAt(heightFraction As Single) As System.Numerics.Vector3
            If VertexCount = 0 Then Return New System.Numerics.Vector3(0, 0, 0)
            Return New System.Numerics.Vector3((Min.X + Max.X) * 0.5F,
                                               (Min.Y + Max.Y) * 0.5F,
                                               Min.Z + (Max.Z - Min.Z) * heightFraction)
        End Function
    End Structure

    ''' <summary>
    ''' Mide el bbox que produciría un export unskinned de <paramref name="meshes"/>, SIN escribir nada.
    ''' Existe para que el diálogo pueda ofrecer un valor por defecto editable para el locator del menú
    ''' de carga y para que ese número sea EL MISMO que se escribiría.
    ''' <para>Aplica los mismos descartes que el export: shapes sin datos, <c>RenderHide</c>, oclusión sin
    ''' evaluar, shapes enteramente ocluidos, y por vértice la máscara de
    ''' <see cref="ComputeVertexDropMask"/>. Las posiciones salen del MISMO transform world-pose
    ''' (<c>PerVertexSkinMatrix</c>) que usa el bake.</para>
    ''' <para>⚠️ Un shape que el export falle DESPUÉS de esto (clone fallido, partición irremapeable) sí
    ''' cuenta acá y no allá. Es un residuo conocido y acotado: cuando pasa, el export ya le muestra al
    ''' usuario el cuadro de shapes fallidos.</para>
    ''' </summary>
    Public Shared Function MeasureBakedBounds(meshes As IEnumerable(Of PreviewModel.RenderableMesh)) As BakedBounds
        Dim result As New BakedBounds With {
            .Min = New System.Numerics.Vector3(Single.MaxValue, Single.MaxValue, Single.MaxValue),
            .Max = New System.Numerics.Vector3(Single.MinValue, Single.MinValue, Single.MinValue),
            .VertexCount = 0
        }
        If meshes Is Nothing Then Return result

        For Each mesh In meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Shape Is Nothing Then Continue For
            Dim srcRenderable = mesh.MeshData.Shape
            If srcRenderable.RenderHide Then Continue For
            If Not mesh.OcclusionEvaluated Then Continue For
            Try
                If mesh.MeshData.Meshgeometry.Vertices IsNot Nothing Then
                    SkinningHelper.GetWorldVertices(mesh.MeshData.Meshgeometry)
                End If
                Dim liveGeom = mesh.MeshData.Meshgeometry
                Dim localVerts = liveGeom.Vertices
                Dim perVtxMat = liveGeom.PerVertexSkinMatrix
                If localVerts Is Nothing OrElse perVtxMat Is Nothing OrElse localVerts.Length <> perVtxMat.Length Then Continue For
                Dim n = localVerts.Length

                Dim hasOccl As Boolean, allHidden As Boolean
                Dim dropped = ComputeVertexDropMask(mesh, srcRenderable, liveGeom, n, hasOccl, allHidden)
                If allHidden Then Continue For

                For i = 0 To n - 1
                    If dropped(i) Then Continue For
                    ' PerVertexSkinMatrix se guarda en Single; el exportador sigue haciendo su cuenta en Double.
                    Dim wv = Vector3d.TransformPosition(localVerts(i), SkinningHelper.AMatrix4d(perVtxMat(i)))
                    Dim p As New System.Numerics.Vector3(CSng(wv.X), CSng(wv.Y), CSng(wv.Z))
                    result.Min = System.Numerics.Vector3.Min(result.Min, p)
                    result.Max = System.Numerics.Vector3.Max(result.Max, p)
                    result.VertexCount += 1
                Next
            Catch
                ' Medir nunca puede voltear el diálogo: un shape degenerado simplemente no aporta.
            End Try
        Next
        Return result
    End Function

    ''' <summary>Nombre EXACTO del locator que el menú de carga de FO4 busca. Medido: aparece con esta
    ''' grafía en 63 de los 173 <c>Meshes\LoadScreenArt\*.nif</c> vanilla. No es configurable — lo lee el
    ''' motor por nombre.</summary>
    Private Const LoadScreenZoomTargetName As String = "LoadingMenuZoomTarget"

    ''' <summary>Altura del pivote como fracción del alto del modelo, medida desde su base.
    ''' <para>Vanilla NO usa ninguna fórmula: los 63 casos están puestos a mano y la fracción va de 0,218 a
    ''' 0,994 (mediana 0,769), con 6 archivos que directamente lo dejaron en (0,0,0). Pero el cluster de
    ''' bípedos erguidos —que es lo que este exporter produce— cae en 0,78..0,95, y 0,85 los reproduce
    ''' dentro de ±10 % (raider −3,5 %, super mutante −1,2 %, synths +1,9 %, protectron +2,6 %).</para>
    ''' <para>⚠️ En un NPC NO erguido (cuadrúpedo, mirelurk, stingwing) el punto más alto es el lomo o las
    ''' alas y no la cabeza, y esta fracción se va hasta 51 % de error. No hay regla geométrica que lo
    ''' resuelva: dónde está la cabeza no está en el bounding box. Ese caso se corrige a mano sobre el NIF.</para></summary>
    Private Const LoadScreenZoomHeightFraction As Single = 0.85F

    ''' <summary>¿El destino es FO4? El locator del menú de carga es exclusivo de ese motor (0 de 139
    ''' loadscreens de SSE lo traen), así que se pregunta por la versión que REALMENTE se va a escribir y
    ''' no por el toggle del diálogo.</summary>
    Private Shared Function IsFallout4Target(v As NiVersion) As Boolean
        Return v IsNot Nothing AndAlso v.UserVersion = 12 AndAlso v.StreamVersion = 130
    End Function

    ''' <summary>
    ''' Cuelga el locator <c>LoadingMenuZoomTarget</c> de la raíz del NIF destino, en el centro XY del
    ''' modelo y a <see cref="LoadScreenZoomHeightFraction"/> de su alto.
    ''' <para>⛔ TIENE que ir DESPUÉS del <c>RemoveUnreferencedBlocks</c> del exporter, igual que el
    ''' reparent de <c>FaceGenBuilder</c>: así el índice que devuelve <c>AddBlock</c> es el final.</para>
    ''' <para>⛔ Y TIENE que quedar referenciado desde <c>root.Children</c>, no basta con
    ''' <c>AddBlock</c>. <c>NifFile.Save</c> corre con los defaults de <c>NifFileSaveOptions</c>
    ''' (<c>RemoveUnreferencedBlocks=True</c>, <c>SortBlocks=True</c>): un bloque suelto lo borra el
    ''' primero, y para el segundo <c>GetParentNode</c> daría Nothing y lo trataría como un SEGUNDO nodo
    ''' raíz. Con el ref puesto, <c>NiNode.Sync</c> recalcula <c>NumChildren</c> desde la lista y
    ''' <c>UpdateHeaderStrings</c> mete el nombre en la tabla del header — nada más que tocar.</para>
    ''' <para>La rotación se escribe identidad EXPLÍCITA: <c>Matrix33</c> arranca en ceros, y una matriz
    ''' nula no es una rotación válida. <c>Scale</c> ya viene 1.0 por default pero se fija igual.</para>
    ''' </summary>
    Private Shared Function AddLoadScreenZoomTarget(nif As Nifcontent_Class_Manolo,
                                                    placement As LoadScreenNodePlacement) As Boolean
        Dim root = nif.GetRootNode()
        If root Is Nothing OrElse root.Children Is Nothing Then Return False

        ' ⛔ El cruce de ejes es REAL, no un descuido: Transform_Class.EulerXYZToMatrix33 toma
        ' (yaw=Z, pitch=Y, roll=X) en ese orden, mientras que el placement guarda grados POR EJE porque
        ' es como están etiquetados los campos del diálogo. Con rotación cero da la identidad exacta,
        ' que es lo que traen los 63 loadscreens vanilla.
        Dim rot = placement.RotationDegrees
        Dim m As NiflySharp.Structs.Matrix33 =
            If(rot.X = 0.0F AndAlso rot.Y = 0.0F AndAlso rot.Z = 0.0F,
               New NiflySharp.Structs.Matrix33 With {.M11 = 1.0F, .M22 = 1.0F, .M33 = 1.0F},
               Transform_Class.EulerXYZToMatrix33(rot.Z, rot.Y, rot.X))

        Dim node As New NiflySharp.Blocks.NiNode() With {
            .Name = New NiflySharp.NiStringRef(LoadScreenZoomTargetName),
            .Flags_ui = &HEUI,
            .Rotation = m,
            .Scale = placement.Scale,
            .Translation = placement.Position
        }
        root.Children.AddBlockRef(nif.AddBlock(node))
        Return True
    End Function

    ''' <summary>Versión del NIF destino según el juego activo, con el mismo criterio que el bake
    ''' de FaceGen (<c>FaceGenBuilder</c>: <c>Config_App.Current.Game = Skyrim</c> ⇒ SSE). Sin
    ''' <c>Config_App.Current</c> se mantiene el default histórico (FO4).</summary>
    Private Shared Function TargetVersionForCurrentGame() As NiVersion
        If Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            Return NiVersion.GetSSE()   ' 20.2.0.7, user 12, stream 100
        End If
        Return NiVersion.GetFO4()       ' 20.2.0.7, user 12, stream 130
    End Function

    ''' <summary>Dos NIF se serializan con las mismas leyes sólo si coinciden file/user/stream.
    ''' El stream es el que separa FO4 (130) de SSE (100) y de Oldrim (83) dentro del mismo
    ''' file version 20.2.0.7, así que comparar sólo el file version no alcanza.</summary>
    Private Shared Function SameNifVersion(a As NiVersion, b As NiVersion) As Boolean
        If a Is Nothing OrElse b Is Nothing Then Return True   ' sin dato, no bloquear el export
        Return a.FileVersion = b.FileVersion AndAlso
               a.UserVersion = b.UserVersion AndAlso
               a.StreamVersion = b.StreamVersion
    End Function

    ''' <summary>Etiqueta legible de una versión para los mensajes de fallo de la UI.</summary>
    Private Shared Function DescribeVersion(v As NiVersion) As String
        If v Is Nothing Then Return "unknown"
        Dim game As String
        If v.UserVersion = 12 AndAlso v.StreamVersion = 130 Then
            game = "Fallout 4"
        ElseIf v.UserVersion = 12 AndAlso v.StreamVersion = 100 Then
            game = "Skyrim SE"
        ElseIf v.UserVersion = 12 AndAlso v.StreamVersion = 83 Then
            game = "Skyrim LE"
        Else
            game = "unsupported"
        End If
        Return $"{game} (user {v.UserVersion}, stream {v.StreamVersion})"
    End Function

    ''' <summary>Build a game-matching NIF from the live rendered meshes and write it to
    ''' <paramref name="outPath"/>. Bakes each visible shape's world-pose vertices/normals/
    ''' tangents/bitangents through its per-vertex skin matrices, applies hair-zap vertex
    ''' compaction, strips skin on the clones, drops orphaned skin blocks and saves.
    ''' Never throws — failures surface via <see cref="ExportResult"/>.</summary>
    Public Shared Function Export(meshes As IEnumerable(Of PreviewModel.RenderableMesh), outPath As String,
                                  Optional options As SceneExportOptions = Nothing,
                                  Optional facePlan As FaceTexturePlan = Nothing) As ExportResult
        ' Sin opciones = el comportamiento histórico de esta función: unskinned, sin tocar texturas.
        Dim opts = If(options, New SceneExportOptions() With {.Skinned = False, .RepointFaceTextures = False})
        ' ⛔ La versión del NIF destino es GAME-AWARE. Estuvo clavada en NiVersion.GetFO4()
        ' (stream 130) y en modo Skyrim eso NO era sólo un header equivocado: los shapes SSE
        ' (stream 100) clonados a un destino stream 130 se serializan con las leyes de FO4 y el
        ' vertex data NO se emite. MEDIDO con Tools\SceneNifExportVersionProbe sobre los fixtures
        ' de nifly (TestNifFile_Skinned_SE / _FO4), clonando + strip skin + Save_As_Manolo:
        '   src SSE  → dest FO4 : 1.965 bytes, reload verts=0   tris=68   ⛔ malla vacía
        '   src SSE  → dest SSE : 9.515 bytes, reload verts=136 tris=68   ✅
        '   src FO4  → dest FO4 : 7.671 bytes, reload verts=136 tris=68   ✅ (control)
        '   src FO4  → dest SSE : 2.093 bytes, reload verts=0   tris=68   ⛔ el daño en espejo
        ' El daño es simétrico ⇒ la causa es el MISMATCH de versión, no el juego en sí. De ahí
        ' también la guarda por shape de más abajo.
        Dim destVersion As NiVersion = TargetVersionForCurrentGame()
        Dim destNif As New Nifcontent_Class_Manolo()
        destNif.Create(destVersion, withRootNode:=True)

        Dim shapesWritten As Integer = 0
        Dim shapesFailed As Integer = 0
        Dim failureDetails As New System.Text.StringBuilder()
        Dim skinToneWritten As Integer = 0
        Dim skinToneSkipped As Integer = 0
        Dim skinToneSkippedDetails As New System.Text.StringBuilder()
        Dim destIdx As Integer = 0

        For Each mesh In meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Shape Is Nothing Then Continue For
            Dim srcRenderable = mesh.MeshData.Shape
            If srcRenderable.RenderHide Then Continue For
            Dim srcINiShape = srcRenderable.NifShape
            Dim srcNif = srcRenderable.NifContent
            If srcINiShape Is Nothing OrElse srcNif Is Nothing Then Continue For

            Dim shapeName = If(srcINiShape.Name?.String, $"Shape_{destIdx}")

            ' Guarda de versión POR SHAPE. El destino ya se creó con la versión del juego activo,
            ' pero una escena puede traer un NIF de otra versión (típico en SSE: un mesh Oldrim
            ' stream 83 dentro de una instalación SE). Clonarlo igual produce el mismo vaciado de
            ' vertex data que la medición de arriba exhibe, y en SILENCIO: el NIF se escribe, el
            ' resumen dice "Wrote N shapes" y la malla sale sin vértices. Se falla explícito.
            If Not SameNifVersion(srcNif.Header.Version, destVersion) Then
                shapesFailed += 1
                failureDetails.AppendLine($"{shapeName}: source NIF is {DescribeVersion(srcNif.Header.Version)} but the export target is {DescribeVersion(destVersion)} — cross-version export would drop the vertex data, shape skipped")
                Continue For
            End If

            Try
                ' Guarantee PerVertexSkinMatrix is current before the bake reads it. After a GPU-mode
                ' animation play, RecomputeGPUBoneMatrices skips pass-2 (updatePerVertexSkin=False) and
                ' leaves PerVertexMatrixValid=False with PerVertexSkinMatrix STALE (SkinningHelper.vb
                ' ~889-908, 1099-1106). This Save dialog runs OFF the render loop with NO GL context, so
                ' nothing has recomputed it. GetWorldVertices is the SAME canonical CPU-only path the
                ' renderer's own world-cache readers use (Render.vb:1124/2562/3246, OcclusionRaytracer):
                ' it calls ComputeWorldSpaceCache → EnsurePerVertexSkinMatrix, which lazily re-blends
                ' PerVertexSkinMatrix from BoneMatsPose (pure Matrix4d math, GL-free) and sets
                ' PerVertexMatrixValid=True. Pass the LIVE field ByRef so the in-place element rewrite +
                ' the validity flag land on the live SkinnedGeometry — idempotent and identical to the
                ' renderer's next reader, so the on-screen model is not corrupted (it only fills the
                ' world cache + flips WorldCacheValid/PerVertexMatrixValid True; no dirty flag the
                ' renderer needs is cleared). When already valid this is a cheap no-op.
                ' Guard on Vertices IsNot Nothing exactly as the renderer's readers do
                ' (OcclusionRaytracer.vb:88) — a degenerate geom falls through to the existing
                ' missing-data check below, which fails the shape gracefully.
                If mesh.MeshData.Meshgeometry.Vertices IsNot Nothing Then
                    SkinningHelper.GetWorldVertices(mesh.MeshData.Meshgeometry)
                End If

                ' Copy AFTER ensuring freshness. SkinnedGeometry is a STRUCT (Render.vb:1656), so this is
                ' a by-value snapshot — but PerVertexSkinMatrix is an array reference shared with the live
                ' field, and EnsurePerVertexSkinMatrix rewrote that array's elements in place above, so the
                ' bake below reads the now-fresh matrices through this copy.
                Dim liveGeom = mesh.MeshData.Meshgeometry
                Dim localVerts = liveGeom.Vertices  ' post-morph, pre-skin (shape-local).
                Dim perVtxMat = liveGeom.PerVertexSkinMatrix
                If localVerts Is Nothing OrElse perVtxMat Is Nothing OrElse localVerts.Length <> perVtxMat.Length Then
                    shapesFailed += 1
                    failureDetails.AppendLine($"{shapeName}: missing skin matrix / vertex data")
                    Continue For
                End If
                Dim n = localVerts.Length

                ' ── Compaction map: los DOS mecanismos con que el render oculta geometría ──
                ' (1) VERTEX-ZAP: ApplyZaps=True + VertexMask(i)=-1 (pelo). Se cae el vértice y todo
                '     triángulo que lo toque — el predicado exacto de Render.vb. AGNOSTICO a qué
                '     partición se zapeó (Top / Long / Both): keyea puro en VertexMask(i)=-1.
                ' (2) OCLUSION POR SEGMENTO/PARTICION: el cuerpo/armor tapado por otra prenda. NO toca
                '     VertexMask — el render la aplica FILTRANDO EL INDEX BUFFER en
                '     EnsureZapIndexBuffer (Render.vb:2689+). Este export miraba SOLO (1), así que los
                '     triángulos ocluidos —invisibles en pantalla— salían igual en el NIF.
                ' ⭐ El set NO se recalcula acá: se LEE el que el render usó para dibujar
                ' (mesh.HiddenTriangles). Recalcularlo sería reproducir el criterio (toggle
                ' DrawHiddenSegments + rama FO4-por-segmento / SSE-por-partición + máscaras), y dos
                ' copias del criterio se desincronizan en cuanto alguien toque el render. Está
                ' indexado por índice de triángulo del shape = el MISMO orden que liveGeom.Indices,
                ' la alineación en la que ya se apoya el provenance de más abajo.
                If Not mesh.OcclusionEvaluated Then
                    ' El cómputo vive en Render(); si este shape visible nunca se dibujó, no sabemos
                    ' qué está ocluido. Se falla explícito antes que exportar geometría de más en
                    ' silencio (que es exactamente el bug que este bloque corrige).
                    shapesFailed += 1
                    failureDetails.AppendLine($"{shapeName}: occlusion state unknown (shape never went through a render pass) — skipped rather than exporting hidden geometry")
                    Continue For
                End If

                Dim hasOccl As Boolean
                Dim allHidden As Boolean
                Dim vertexDropped = ComputeVertexDropMask(mesh, srcRenderable, liveGeom, n, hasOccl, allHidden)
                Dim occl As Boolean() = mesh.HiddenTriangles
                ' Shape enteramente oculto (el caso típico del body tapado en SSE, donde la
                ' oclusión por partición es whole-mesh): no se exporta, igual que un RenderHide.
                ' No cuenta como fallo — en pantalla tampoco hay nada.
                If allHidden Then
                    Logger.LogLazy(Function() $"[SCENE-EXPORT] '{shapeName}' SKIPPED: todos los triángulos ocluidos por segmento/partición")
                    Continue For
                End If

                ' oldToNew(i) mapea un vértice fuente sobreviviente a su índice compactado; -1 = removido.
                Dim oldToNew(n - 1) As Integer
                Dim nSurv As Integer = 0
                For i = 0 To n - 1
                    If vertexDropped(i) Then
                        oldToNew(i) = -1
                    Else
                        oldToNew(i) = nSurv
                        nSurv += 1
                    End If
                Next
                Dim zappedCount As Integer = n - nSurv
                ' Hay que reescribir la topología si se cayó algún vértice o si algún triángulo está
                ' oculto (puede haber oclusión sin pérdida de vértices si los comparte con un
                ' triángulo visible).
                Dim needsCompaction As Boolean = (zappedCount > 0) OrElse hasOccl

                ' Compute world-pose attributes per SURVIVING vertex (packed in oldToNew order). Position
                ' via TransformPosition; normals/tangents/bitangents via per-vertex normal matrix
                ' (transpose of inverse of upper-left 3x3 of the skin matrix). Same formula the renderer uses.
                Dim worldPos As New List(Of System.Numerics.Vector3)(nSurv)
                Dim hasN = liveGeom.Normals IsNot Nothing AndAlso liveGeom.Normals.Length = n
                Dim hasT = liveGeom.Tangents IsNot Nothing AndAlso liveGeom.Tangents.Length = n
                Dim hasB = liveGeom.Bitangents IsNot Nothing AndAlso liveGeom.Bitangents.Length = n
                Dim worldN As List(Of System.Numerics.Vector3) = If(hasN, New List(Of System.Numerics.Vector3)(nSurv), Nothing)
                Dim worldT As List(Of System.Numerics.Vector3) = If(hasT, New List(Of System.Numerics.Vector3)(nSurv), Nothing)
                Dim worldB As List(Of System.Numerics.Vector3) = If(hasB, New List(Of System.Numerics.Vector3)(nSurv), Nothing)

                For i = 0 To n - 1
                    If oldToNew(i) < 0 Then Continue For  ' zapped crown vertex — drop from export

                    ' SKINNED: el vértice va en su espacio de BIND (post-morph, pre-skin), tal como
                    ' viene del NIF fuente. Es lo que hace el bake de FaceGen, cuyos archivos andan:
                    ' cada shape conserva el espacio en que fue autorado y su propio skinToBone.
                    If opts.Skinned Then
                        Dim lv = localVerts(i)
                        worldPos.Add(New System.Numerics.Vector3(CSng(lv.X), CSng(lv.Y), CSng(lv.Z)))
                        If hasN Then worldN.Add(ToNumerics(liveGeom.Normals(i)))
                        If hasT Then worldT.Add(ToNumerics(liveGeom.Tangents(i)))
                        If hasB Then worldB.Add(ToNumerics(liveGeom.Bitangents(i)))
                        Continue For
                    End If

                    ' PerVertexSkinMatrix se guarda en Single; aca se ensancha y la cuenta sigue en Double.
                    Dim m4 = SkinningHelper.AMatrix4d(perVtxMat(i))
                    Dim wv = Vector3d.TransformPosition(localVerts(i), m4)
                    worldPos.Add(New System.Numerics.Vector3(CSng(wv.X), CSng(wv.Y), CSng(wv.Z)))

                    If hasN OrElse hasT OrElse hasB Then
                        Dim nm As Matrix3d = SkinningHelper.NormalMatrixOrIdentity(m4)
                        Dim nm4 As Matrix4d = Matrix4d.Identity
                        nm4.M11 = nm.M11 : nm4.M12 = nm.M12 : nm4.M13 = nm.M13
                        nm4.M21 = nm.M21 : nm4.M22 = nm.M22 : nm4.M23 = nm.M23
                        nm4.M31 = nm.M31 : nm4.M32 = nm.M32 : nm4.M33 = nm.M33
                        ' liveGeom.Normals/Tangents/Bitangents estan en Single; el transform sigue en
                        ' Double (ADbl es exacta) y recien se redondea al armar el Vector3 de salida.
                        If hasN Then
                            Dim nrm = Vector3d.Normalize(Vector3d.TransformNormal(RecalcTBN.ADbl(liveGeom.Normals(i)), nm4))
                            worldN.Add(New System.Numerics.Vector3(CSng(nrm.X), CSng(nrm.Y), CSng(nrm.Z)))
                        End If
                        If hasT Then
                            Dim tan = Vector3d.Normalize(Vector3d.TransformNormal(RecalcTBN.ADbl(liveGeom.Tangents(i)), nm4))
                            worldT.Add(New System.Numerics.Vector3(CSng(tan.X), CSng(tan.Y), CSng(tan.Z)))
                        End If
                        If hasB Then
                            Dim bit = Vector3d.Normalize(Vector3d.TransformNormal(RecalcTBN.ADbl(liveGeom.Bitangents(i)), nm4))
                            worldB.Add(New System.Numerics.Vector3(CSng(bit.X), CSng(bit.Y), CSng(bit.Z)))
                        End If
                    End If
                Next

                Dim clonedINiShape = destNif.CloneShape_Original(srcINiShape, shapeName, srcNif)
                If clonedINiShape Is Nothing Then
                    shapesFailed += 1
                    failureDetails.AppendLine($"Clone failed: {shapeName}")
                    Continue For
                End If

                ' Reset clone's local T/R/S to identity. CloneShape_Original's unskinned branch
                ' (NifContent_Class.vb:407+) bakes srcShape's parent_chain (without root) into
                ' destShape.T/R/S so unskinned clones display at the right NIF-world position.
                ' Our verts are ALREADY in world coords (via PerVertexSkinMatrix, which absorbs
                ' parent_chain for unskinned and bone palette for skinned). Leaving the baked
                ' T/R/S in place would double-transform the verts.
                ' Sólo en unskinned, donde los vértices SÍ quedan en world y un T/R/S residual se
                ' sumaría encima. En skinned el vértice está en su espacio de bind y el T/R/S del
                ' shape es parte de cómo el NIF fuente lo coloca: tocarlo lo desplaza.
                If Not opts.Skinned Then
                    clonedINiShape.Translation = New System.Numerics.Vector3(0, 0, 0)
                    clonedINiShape.Rotation = New NiflySharp.Structs.Matrix33()
                    clonedINiShape.Scale = 1.0F
                End If

                ' Write world-pose attributes into the clone via its polymorphic adapter.
                Dim cloneRenderable As New NifRenderableShape(destNif, clonedINiShape, destIdx)
                Dim cloneAdapter = cloneRenderable.Geometry

                ' ⛔ ResizeVertices NO conserva nada: reemplaza la lista empaquetada por BSVertexData
                ' NUEVOS en cero (BSTriShapeGeometry.vb:330-338), y este export sólo reescribe
                ' posiciones/normales/tangentes/bitangentes. MEDIDO con Tools\SceneNifExportVersionProbe
                ' sobre los fixtures de nifly, resize a n-1 + SetVertexPositions, en los DOS juegos:
                '   uv[0] (1,0000, 0,5000) → (0,0000, 0,0000) · "UVs todos en cero = True"
                '   skin weights suma 136,00 → 0,00
                ' Es decir: todo shape compactado salía con las UVs destruidas y la textura sin mapear.
                ' Por eso los atributos por-vértice que el resize se lleva puesto se snapshotean ANTES
                ' y se reescriben compactados por el mismo oldToNew. (Los pesos de skin no se reponen
                ' a propósito: este export es unskinned y más abajo tira el skin entero.)
                ' Snapshot de TODOS los atributos por vértice, por el camino de la librería.
                ' ⛔ ANTES de cualquier escritura: ResizeVertices (que ApplyShapeGeometry llama
                ' adentro) reemplaza el vertex data por structs en CERO, así que todo campo que no
                ' se reescriba queda destruido — medido: UVs (1,0000, 0,5000) → (0,0000, 0,0000) y
                ' pesos de suma 136,00 → 0,00.
                Dim arrays = SkinningHelper.SnapshotSeparateArrays(cloneAdapter)

                ' ⛔ LA ESCRITURA NO SE HACE ACÁ. Este export tenía su propio camino a mano (setters
                ' sueltos + SetTriangles) en vez del canónico de la librería, y esa copia paralela ya
                ' se comió tres defectos: UVs destruidas, pesos destruidos y la partición de skin
                ' desincronizada. Ahora se arma el paquete completo y se publica de una sola vez con
                ' SkinningHelper.ApplyShapeGeometry — el MISMO punto que usan WM (split/merge) e
                ' InjectToTrishape, que sabe que ResizeVertices deja todo en cero y reescribe TODOS
                ' los campos por vértice + los triángulos con su provenance.
                Dim newTris As List(Of NiflySharp.Structs.Triangle) = Nothing
                Dim provenance As List(Of Integer) = Nothing

                ' Remap triangles after vertex compaction. Drop any triangle que toque un vértice
                ' caído (zap) O que esté oculto por segmento/partición; reindex the survivors through
                ' oldToNew; track per-new-triangle provenance (source triangle index) so
                ' SetTriangles(provenance) redistributes the BSSubIndexTriShape Segments/SubSegmentDatas
                ' consistently (the same contract WM's RemoveZaps uses → MorphingHelper.vb:226).
                ' liveGeom.Indices is in source-triangle order (SkinningHelper.vb:412 flattens
                ' GetTriangles()), so tr = oldTriIdx — la misma indexación que usa occl.
                Dim triCheckOk As Boolean = True
                If needsCompaction Then
                    Dim idxArr = liveGeom.Indices
                    If idxArr Is Nothing Then
                        triCheckOk = False
                    ElseIf nSurv > MaxUShortIndexableVerts Then
                        ' ⛔ NiflySharp.Structs.Triangle almacena V1/V2/V3 como UShort, así que sólo puede
                        ' direccionar índices 0..65535 (⇒ como mucho 65536 vértices). Con nSurv por encima
                        ' de eso, el CUShort(na/nb/nc) de abajo TRUNCA en silencio (wraparound) y produce
                        ' una malla destrozada. No hay camino de índices de 32 bits en este formato/adapter,
                        ' así que el shape se falla explícitamente en vez de escribir un NIF corrupto.
                        ' Se chequea ANTES del loop: hacerlo después es inútil, porque los valores ya
                        ' truncados vuelven a caer dentro del rango válido (ver nota en el bloque de
                        ' verificación de abajo).
                        triCheckOk = False
                        failureDetails.AppendLine($"{shapeName}: zap export vertex count {nSurv} exceeds the {MaxUShortIndexableVerts}-vertex limit of 16-bit triangle indices — shape skipped (would corrupt the mesh)")
                        ' Copias locales antes del lambda (mismo patrón que el log de abajo): la lambda
                        ' captura por referencia y estas son variables del loop de shapes.
                        Dim ovfShapeName = shapeName
                        Dim ovfNSurv = nSurv
                        Logger.LogLazy(Function() $"[ZAP-EXPORT] '{ovfShapeName}' SKIPPED: nSurv={ovfNSurv} > {MaxUShortIndexableVerts} (16-bit triangle index overflow)")
                    Else
                        newTris = New List(Of NiflySharp.Structs.Triangle)(idxArr.Length \ 3)
                        provenance = New List(Of Integer)(idxArr.Length \ 3)
                        ' Máximo de los índices remapeados PRE-CAST. El chequeo de consistencia de abajo lee
                        ' los triángulos ya escritos (post-CUShort) y por eso no puede ver un overflow; éste sí.
                        Dim maxNewIdxPreCast As Integer = -1
                        For tr = 0 To idxArr.Length - 3 Step 3
                            ' Oculto por segmento/partición: el mismo descarte que hace el index filter
                            ' del render (Render.vb:2775), con la misma indexación tr\3.
                            If occl IsNot Nothing AndAlso (tr \ 3) < occl.Length AndAlso occl(tr \ 3) Then Continue For
                            Dim a = CInt(idxArr(tr)), b = CInt(idxArr(tr + 1)), c = CInt(idxArr(tr + 2))
                            If a < 0 OrElse a >= n OrElse b < 0 OrElse b >= n OrElse c < 0 OrElse c >= n Then Continue For
                            Dim na = oldToNew(a), nb = oldToNew(b), nc = oldToNew(c)
                            If na < 0 OrElse nb < 0 OrElse nc < 0 Then Continue For  ' triangle touched a dropped vertex
                            maxNewIdxPreCast = Math.Max(maxNewIdxPreCast, Math.Max(na, Math.Max(nb, nc)))
                            newTris.Add(New NiflySharp.Structs.Triangle(CUShort(na), CUShort(nb), CUShort(nc)))
                            provenance.Add(tr \ 3)
                        Next
                        ' Red de seguridad sobre los valores PRE-CAST (la guarda de nSurv de arriba ya debería
                        ' haber cubierto esto; si dispara, oldToNew produjo un índice fuera del rango de nSurv).
                        If maxNewIdxPreCast >= nSurv Then
                            triCheckOk = False
                            failureDetails.AppendLine($"{shapeName}: zap export remapped triangle index out of range before cast (maxIdx {maxNewIdxPreCast} >= {nSurv})")
                        End If
                    End If
                End If

                If needsCompaction AndAlso Not triCheckOk Then
                    ' Compaction produced an inconsistent shape — skip it rather than write a corrupt NIF.
                    destNif.RemoveShape_Manolo(clonedINiShape)
                    shapesFailed += 1
                    Continue For
                End If

                ' ── (1) LA PARTICIÓN SE REMAPEA ANTES DE ESCRIBIR LA GEOMETRÍA ──
                ' Éste es el orden de WM y no es indistinto: MorphingHelper.RemoveZaps TERMINA con
                ' RemapSkinPartitionTriangles (MorphingHelper.vb:521) y recién después el build llama
                ' a InjectToTrishape para publicar el shape. El remap reconstruye los TrianglesCopy de
                ' cada partición a partir de la topología VIEJA (PrepareTrueTriangles lee
                ' part.Triangles + VertexMap); si se hace después de haber reescrito el shape, ya no
                ' está leyendo el estado que esos datos describen.
                If opts.Skinned AndAlso needsCompaction AndAlso clonedINiShape.IsSkinned Then
                    Dim remapDictPre As New Dictionary(Of Integer, Integer)(nSurv)
                    For i = 0 To n - 1
                        If oldToNew(i) >= 0 Then remapDictPre(i) = oldToNew(i)
                    Next
                    Try
                        destNif.RemapSkinPartitionTriangles(clonedINiShape, remapDictPre)
                    Catch ex As Exception
                        shapesFailed += 1
                        failureDetails.AppendLine($"{shapeName}: could not remap the skin partition ({ex.Message})")
                        destNif.RemoveShape_Manolo(clonedINiShape)
                        Continue For
                    End Try
                End If

                ' ── (2) UNA SOLA PUBLICACIÓN, POR EL CAMINO CANÓNICO ──
                ' SkinningHelper.ApplyShapeGeometry hace ResizeVertices y escribe TODOS los campos por
                ' vértice + los triángulos con provenance. Es el punto que ya usan SplitShapeHelper y
                ' MergeShapesHelper, y el gemelo de InjectToTrishape. Escribir esto a mano acá fue el
                ' origen de los tres defectos que aparecieron en este export (UVs en cero, pesos en
                ' cero, partición desincronizada): cada campo olvidado es un campo destruido, porque
                ' ResizeVertices deja el vertex data íntegramente a cero.
                If newTris Is Nothing Then
                    ' Sin compactación la topología no cambia: se republican los triángulos del clon.
                    newTris = cloneAdapter.GetTriangles()
                    provenance = Nothing
                End If
                ' Compactación con el MISMO helper que usa SplitShapeHelper: filtra posiciones,
                ' normales, tangentes, bitangentes, UVs, colores, eye data Y pesos de skin con un
                ' solo mapa, así que no hay forma de olvidarse un campo.
                If needsCompaction Then
                    Dim sobrevivientes As New HashSet(Of Integer)()
                    For i = 0 To n - 1
                        If oldToNew(i) >= 0 Then sobrevivientes.Add(i)
                    Next
                    arrays = arrays.FilterByIndices(sobrevivientes)
                End If
                ' Posiciones y TBN los calcula este export (world-pose o bind) y pisan el snapshot.
                arrays.Positions = worldPos
                If hasN AndAlso cloneAdapter.HasNormals Then arrays.Normals = worldN
                If hasT AndAlso cloneAdapter.HasTangents Then arrays.Tangents = worldT
                If hasB AndAlso cloneAdapter.HasTangents Then arrays.Bitangents = worldB
                If Not (opts.Skinned AndAlso cloneAdapter.IsSkinned) Then arrays.Skinning = Nothing
                SkinningHelper.ApplyShapeGeometry(cloneAdapter, newTris, arrays,
                                                  If(provenance Is Nothing, Nothing, TriangleRemap.SameShape(provenance)))

                ' Bounds. El clon traía los del NIF fuente y ninguno de los dos caminos los deja
                ' válidos: unskinned reescribe los vértices en WORLD y cualquier compactación cambia
                ' la extensión. Un bounding mal puesto se paga con culling raro en el juego.
                cloneAdapter.UpdateBounds()

                If Logger.Enabled Then
                    Dim shapeNameLog = shapeName, nLog = n, nSurvLog = nSurv, dropLog = zappedCount
                    Dim triLog = If(newTris Is Nothing, -1, newTris.Count)
                    Logger.LogLazy(Function() $"[SCENE-EXPORT] '{shapeNameLog}' verts {nLog}→{nSurvLog} (dropped {dropLog}); tris escritos={triLog}")
                End If

                If opts.Skinned Then
                    ' El skin se CONSERVA. CloneShape ya trajo el árbol de huesos del NIF fuente al
                    ' destino deduplicando por nombre (FindBlockByName), así que varios shapes de NIF
                    ' distintos convergen a un solo esqueleto, y re-referenció la bone list del skin.
                    ' Lo único que queda es la partición: los setters del adapter NO la regeneran (es
                    ' el contrato explícito de IShapeGeometry), y tras compactar quedó describiendo
                    ' vértices que ya no existen.
                    ' Sólo hace falta regenerar la partición cuando cambió la TOPOLOGÍA. Reescribir
                    ' posiciones no la invalida: en SSE `NiSkinPartition.VertexData` es la MISMA lista
                    ' que la del BSTriShape (UpdateSkinPartitions termina con
                    ' `skinPart.SetVertexData(bsTriShape.VertexDataSSE)`), así que las posiciones
                    ' nuevas ya se ven desde la partición — medido en el export: delta 0,000 entre
                    ' ambas copias sin haberla regenerado.
                    ' ⛔ ACÁ NO SE TOCA EL SKIN. Un FaceGeom bakeado por esta misma app —que funciona
                    ' en el juego— tiene shapes con vértices en WORLD y skinToBone = inv(boneWorld)
                    ' (cabeza, pelo) CONVIVIENDO con shapes en espacio LOCAL y skinToBone identidad
                    ' (pestañas, boca, ojos, cejas). Las dos formas son válidas y dependen de cómo
                    ' viene cada NIF fuente. Uniformarlas —forzar todo a world y reescribir todos los
                    ' skinToBone— fue un error: rompía justamente los que ya estaban bien.

                    ' ── (3) Y RECIÉN AHORA SE REGENERA LA PARTICIÓN ──
                    ' Mismo cierre que el build de WM (BuildingForm.vb:299): UpdateSkinPartitions
                    ' DESPUÉS de publicar la geometría. Los setters del adapter no la regeneran — es
                    ' el contrato explícito de IShapeGeometry — y NiSkinData ya quedó coherente porque
                    ' ApplyShapeGeometry pasó por SetSkinning, que llama a RebuildNiSkinData.
                    If needsCompaction AndAlso clonedINiShape.IsSkinned Then
                        Try
                            destNif.UpdateSkinPartitions(clonedINiShape)
                        Catch ex As Exception
                            shapesFailed += 1
                            failureDetails.AppendLine($"{shapeName}: could not rebuild the skin partition ({ex.Message})")
                            destNif.RemoveShape_Manolo(clonedINiShape)
                            Continue For
                        End Try
                    End If
                Else
                    ' Strip skin on the clone. For BSTriShape this clears the VertexAttribute.Skinned
                    ' flag (FinalizeData → CalcDataSizes excludes the bone weight/index bytes from the
                    ' per-vertex stream on save). For NiTriShape the setter is a no-op; the
                    ' SkinInstanceRef.Clear() below is what disables skinning in that family.
                    clonedINiShape.IsSkinned = False
                    clonedINiShape.SkinInstanceRef?.Clear()

                    ' ⛔ UN BSDynamicTriShape SIN SKIN NO EXISTE EN VANILLA, y en esa combinación las
                    ' posiciones no quedan alcanzables por ningún lector estándar:
                    '   · el atributo Vertex viene APAGADO — igual que en el FaceGeom del CK, porque el
                    '     motor lee la posición del array dinámico y tenerla además en el estático
                    '     congela la cara (sin lip-sync; medido in-game 2026-07-11, ver
                    '     FaceGenBuildPipeline.EmitMorphedPositions). Por eso NiflySharp lo protege en
                    '     el setter de HasVertices, y hace bien.
                    '   · el vertexData estático sólo se repuebla al CARGAR, y ese camino está gateado
                    '     por tener skin: NifFile.PrepareData sale en `if (skinInst == null) continue`
                    '     antes de copiar Vertices→vertData. Sin skin, nadie lo llena.
                    ' Resultado medido sobre el export: vertexData 0/982 no-cero en cabeza, boca, ojos y
                    ' cejas. nifly C++ BSTriShape::UpdateRawVertices (Geometry.cpp:633) lee ESE array sin
                    ' ninguna rama para shapes dinámicos, y es de donde Outfit Studio saca la malla ⇒ no
                    ' aparece. NifSkope no lo exhibe porque para un dinámico toma la posición del array
                    ' dinámico (bsshape.cpp:104), así que ahí el defecto es invisible.
                    ' En un export unskinned no hay morph facial que preservar —la pose ya está horneada
                    ' en los vértices— así que el shape deja de ser dinámico: se declara el atributo y
                    ' se CONVIERTE el bloque a BSTriShape plano. Así el tipo, el descriptor y el lugar
                    ' donde está el dato dicen todos lo mismo, y el array dinámico duplicado desaparece.
                    ' Los tamaños y offsets no se tocan a mano: FinalizeData llama a CalcDataSizes por
                    ' shape antes de escribir (NifFile.cs:586) y los deriva de los flags.
                    Dim dynClone = TryCast(clonedINiShape, NiflySharp.Blocks.BSDynamicTriShape)
                    If dynClone IsNot Nothing AndAlso dynClone.VertexDesc IsNot Nothing Then
                        dynClone.VertexDesc.VertexAttributes =
                            dynClone.VertexDesc.VertexAttributes Or NiflySharp.Enums.VertexAttribute.Vertex
                        Dim plainClone = RecortarABSTriShape(destNif, dynClone)
                        If plainClone Is Nothing OrElse Not destNif.ReplaceBlock(dynClone, plainClone) Then
                            Throw New Exception("no se pudo convertir el BSDynamicTriShape a BSTriShape")
                        End If
                        clonedINiShape = plainClone
                    End If
                End If

                ' ── EL FLAG 'Skinned' DEL SHADER TIENE QUE COINCIDIR CON EL SKIN REAL DEL SHAPE ──
                ' ⛔ El shader lleva su PROPIO bit Skinned (bit 1 de ShaderPropertyFlags1), INDEPENDIENTE
                ' del atributo de vértice (IsSkinned) y del SkinInstanceRef. La rama unskinned de arriba
                ' limpia esos dos y NO tocaba éste, así que el shape salía sin datos de skin pero con el
                ' shader diciéndole al motor que los bindee. FO4 lo carga y CRASHEA AL ESCRITORIO.
                ' MEDIDO: los 173 loadscreens vanilla tienen el bit en 0 y sus flags son idénticos a los
                ' que emitíamos salvo por ese +2 exacto (vanilla 2151678465 / nuestro 2151678467,
                ' vanilla 2151678593 / nuestro 2151678595, vanilla 2151679489 / nuestro 2151679491).
                ' Un export SKINNED del mismo NPC no crasheaba justamente porque ahí el bit sí decía
                ' la verdad.
                ' El predicado es el estado REAL del clon, no la opción del diálogo: con Skinned=True un
                ' shape que igual no tiene skin (un prop) debe quedar con el bit en 0. Va acá, después de
                ' la rama de skin Y de la conversión BSDynamicTriShape→BSTriShape, para leer el estado
                ' final. SetFlagSF1 despacha por juego según la versión del NIF (SK / FO4), y el valor
                ' del flag se toma del enum de CADA juego en vez de asumir que comparten el bit.
                Dim clonedShader = destNif.GetShader(clonedINiShape)
                If clonedShader IsNot Nothing Then
                    Dim skinnedFlag As UInteger =
                        If(IsFallout4Target(destVersion),
                           CUInt(NiflySharp.Enums.Fallout4ShaderPropertyFlags1.Skinned),
                           CUInt(NiflySharp.Enums.SkyrimShaderPropertyFlags1.Skinned))
                    NiflySharp.Helpers.ShaderHelper.SetFlagSF1(clonedShader, skinnedFlag, clonedINiShape.IsSkinned)
                End If

                ' Repunte de la cara. Va DESPUÉS de toda la escritura de geometría porque sólo toca el
                ' shader + el BSShaderTextureSet; el gate por shader-type vive adentro (no todo head part
                ' califica).
                ' ⭐ Se le pasa el material que el RENDER ya resolvió para este shape (cadena TXST/FTST +
                ' MNAM-BGSM + tints + palette). Sin él, el repunte de abajo es INERTE en FO4: el shape sigue
                ' nombrando su .bgsm y al aplicarlo el motor reemplaza el texture set entero. No hay que
                ' re-resolver nada — el preview ya lo hizo para dibujar este mismo shape.
                If opts.RepointFaceTextures AndAlso facePlan IsNot Nothing Then
                    FaceTextureRepointer.Repoint(destNif, clonedINiShape, facePlan, opts.FoldFaceOverlays,
                                                 srcRenderable.ShapeMaterial?.material)
                End If

                ' Material resuelto al shader. Va DESPUÉS del repunte porque en FO4 los dos escriben el
                ' MISMO shader inline y el repunte ya transcribe el de la cara; el writer descarta la cara
                ' por su cuenta (gate Facegen), así que ningún shape pasa dos veces por la transcripción.
                ' ⭐ En FO4 alcanza a TODA shape, no sólo a la piel: sin cortar el link al .bgsm el motor
                ' reemplaza el material entero y el color del NPC no llega al juego (era el caso del PELO).
                ' En SSE sigue siendo sólo el color de las shapes de piel. Ver ShapeMaterialTranscriber.
                ' ⭐ Se lee la MaterialData de la mesh, no el ShapeMaterial pelado: es la que tiene el
                ' "ya está" del tono horneado (SkinToneBaked), o sea exactamente lo que el render miró para
                ' decidir si tintaba este shape.
                ' ⚠️ Las capas de OVERLAY (tatuajes) NO pasan por acá y NO se exportan, en ninguno de los dos
                ' juegos: no son geometría sino un pase de material extra sobre el MISMO VAO
                ' (Render.RenderOverlayLayer), con una MaterialData transitoria que no vive en model.meshes.
                ' Lo que se exporta es el shape BASE, y el tono que se le escribe es el de su piel — correcto.
                If opts.WriteSkinTone Then
                    Dim matData = mesh.MeshData.Material
                    Select Case ShapeMaterialTranscriber.Apply(destNif, clonedINiShape,
                                                           matData?.MaterialBase,
                                                           matData IsNot Nothing AndAlso matData.SkinToneBaked)
                        Case ShapeMaterialTranscriber.Outcome.Written
                            skinToneWritten += 1
                        Case ShapeMaterialTranscriber.Outcome.SkippedShaderType
                            skinToneSkipped += 1
                            skinToneSkippedDetails.AppendLine($"{shapeName}: skin material but the shape's shader type is not Skin Tint — the NIF has no field to carry the tone there, shader left as-is")
                    End Select
                End If

                shapesWritten += 1
                destIdx += 1
            Catch ex As Exception
                shapesFailed += 1
                failureDetails.AppendLine($"{shapeName}: {ex.Message}")
            End Try
        Next

        If shapesWritten = 0 Then
            Return New ExportResult With {
                .ShapesWritten = 0,
                .ShapesFailed = shapesFailed,
                .FailureDetails = failureDetails.ToString(),
                .SaveError = Nothing,
                .SkinToneWritten = skinToneWritten,
                .SkinToneSkipped = skinToneSkipped,
                .SkinToneSkippedDetails = skinToneSkippedDetails.ToString()
            }
        End If


        ' Drop unreferenced BSSkin_Instance / BSSkin_BoneData / NiSkinInstance / NiSkinData /
        ' NiSkinPartition blocks orphaned by the cleared SkinInstanceRefs.
        Try
            destNif.RemoveUnreferencedBlocks()
        Catch
        End Try

        ' Locator del menú de carga. VA ACÁ: después del RemoveUnreferencedBlocks (índices finales) y
        ' antes del Save. La precondición se revalida en vez de confiar en el diálogo —el exporter
        ' también se llama sin él— y se pregunta por la versión que se va a ESCRIBIR, no por el toggle
        ' de juego. El skin NO entra en la precondición: el nodo es un locator inerte y el destino
        ' habitual (STAT) es sólo el caso vanilla, no una restricción del formato.
        Dim loadScreenNodeError As String = Nothing
        If opts.AddLoadScreenNode Then
            If Not IsFallout4Target(destVersion) Then
                loadScreenNodeError = $"'{LoadScreenZoomTargetName}' not written: the node only exists in Fallout 4, and the export target is {DescribeVersion(destVersion)}."
            Else
                ' El diálogo manda lo que el usuario tuvo a la vista. Sin eso (caller headless) se arma
                ' el default con la MISMA función con la que el diálogo arma el suyo.
                ' ⚠️ En un export SKINNED ese bbox sigue siendo el del modelo POSADO (es lo que mide
                ' MeasureBakedBounds), mientras que los vértices que se escriben quedan en su espacio de
                ' bind. La posición default es entonces aproximada; el usuario la corrige en el diálogo.
                Dim placement = opts.LoadScreenNodePlacement
                If placement Is Nothing Then
                    Dim measured = MeasureBakedBounds(meshes)
                    If Not measured.IsUsable Then
                        loadScreenNodeError = $"'{LoadScreenZoomTargetName}' not written: no exported vertex to measure the model from."
                    Else
                        placement = New LoadScreenNodePlacement With {
                            .Position = measured.ZoomTargetAt(LoadScreenZoomHeightFraction)
                        }
                    End If
                End If

                If loadScreenNodeError Is Nothing Then
                    Try
                        If AddLoadScreenZoomTarget(destNif, placement) Then
                            Dim p = placement
                            Logger.LogLazy(Function() $"[SCENE-EXPORT] {LoadScreenZoomTargetName} en ({p.Position.X:F2}, {p.Position.Y:F2}, {p.Position.Z:F2}) rot=({p.RotationDegrees.X:F1}, {p.RotationDegrees.Y:F1}, {p.RotationDegrees.Z:F1}) scale={p.Scale:F3}")
                        Else
                            loadScreenNodeError = $"'{LoadScreenZoomTargetName}' not written: the destination NIF has no root node to attach it to."
                        End If
                    Catch ex As Exception
                        loadScreenNodeError = $"'{LoadScreenZoomTargetName}' not written: {ex.Message}"
                    End Try
                End If
            End If
        End If

        Try
            destNif.Save_As_Manolo(outPath, Overwrite:=True)
        Catch ex As Exception
            Return New ExportResult With {
                .ShapesWritten = shapesWritten,
                .ShapesFailed = shapesFailed,
                .FailureDetails = failureDetails.ToString(),
                .SaveError = ex.Message,
                .SkinToneWritten = skinToneWritten,
                .SkinToneSkipped = skinToneSkipped,
                .SkinToneSkippedDetails = skinToneSkippedDetails.ToString()
            }
        End Try

        Return New ExportResult With {
            .ShapesWritten = shapesWritten,
            .ShapesFailed = shapesFailed,
            .FailureDetails = failureDetails.ToString(),
            .SaveError = Nothing,
            .SkinToneWritten = skinToneWritten,
            .SkinToneSkipped = skinToneSkipped,
            .SkinToneSkippedDetails = skinToneSkippedDetails.ToString(),
            .LoadScreenNodeError = loadScreenNodeError
        }
    End Function

End Class
