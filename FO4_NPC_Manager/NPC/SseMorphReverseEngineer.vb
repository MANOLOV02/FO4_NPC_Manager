Imports FO4_Base_Library
Imports NiflySharp
Imports OpenTK.Mathematics

''' <summary>DEBUG-ONLY. Reconstruye un preset de RaceMenu (NAM9 + sculpt per-shape) a partir de un FaceGen YA
''' HORNEADO, para mods que shipean el .nif de facegeom pero no el .jslot.
''' <para>POR QUE ES INVERTIBLE (solo SSE; en FO4 el bake va por FMRS, que es un rig de huesos y no un morph por
''' vertice, y LooksMenu no tiene canal de sculpt donde escribir el residual): el bake de SSE es un morph por
''' vertice PURO sobre la malla del HDPT, sin skinning ni remap, con correspondencia indice a indice entre base y
''' horneado. Todos los canales del plan son ADITIVOS y todos son CONOCIDOS salvo los NAM9, asi que
''' <c>V_horneado = Conocidos(base) + suma(w_j x Columna_j(NAM9)) + Sculpt</c>, con las columnas ya gateadas y los
''' pesos como unicas incognitas.</para>
''' <para>El gate del CK (bloques de 4, umbral 0,01) se evalua sobre el delta CRUDO y NO escalado por el peso, asi
''' que la mascara de cada columna es FIJA, se precomputa una vez y la aplicacion queda LINEAL en el peso.
''' Verificado por RE de los dos binarios de SSE: el gate no es una comparacion en tiempo de aplicacion sino un
''' mapa RLE precomputado en los datos del morph, consumido ANTES de que el peso entre.</para>
''' <para>âš ï¸ QUE ELIGE ESTE CODIGO: la descomposicion NO es unica -el sculpt es un campo libre por vertice y puede
''' absorber cualquier cosa, asi que infinitos pares (NAM9, sculpt) dan el MISMO horneado-. Se elige el que
''' MINIMIZA LA ENERGIA DEL SCULPT, que es lo que da minimos cuadrados. Los NAM9 devueltos NO son "los
''' originales" (eso no lo puede recuperar nadie): son unos que reproducen la geometria. La geometria cierra al
''' vertice; la atribucion es una eleccion.</para>
''' <para>RESTRICCION DE CAJA [0,1], NO CLAMP: el motor valida el peso contra [-1,1] ANTES de aplicar y, fuera de
''' rango, la llamada entera es NO-OP. O sea que un peso 1,3 no equivale a 1,0 sino a 0, y por eso la cota va
''' DENTRO del solver y no se clampea despues.</para>
''' <para>FUERA DE ALCANCE, porque no esta horneado y no lo recupera ninguna via: texturas, warpaints custom,
''' overlays y node transforms. El pelo no tiene tri de chargen (211/211 records vanilla sin NAM0=2), asi que no
''' admite bloque de sculpt - no es limitacion de esto sino del formato, y no importa: su unica deformacion es
''' SkinnyMorph, que es un canal CONOCIDO, asi que su residual es ~0.</para></summary>
Public Module SseMorphReverseEngineer

    ''' <summary>Umbral por defecto (unidades de modelo) bajo el cual un delta residual se considera
    ''' ruido y NO se emite como sculpt. El suelo de ruido medido del pipeline es RMS 1,5e-3 y el divisor
    ''' del jslot da resolución 1e-4, así que 2e-3 deja el ruido fuera sin comerse señal real.</summary>
    Public Const DefaultSculptThreshold As Single = 0.002F

    Public Class ShapeReport
        Public Property ShapeName As String
        Public Property Host As String
        Public Property VertexCount As Integer
        Public Property SculptedVerts As Integer
        Public Property DroppedVerts As Integer
        Public Property MaxResidual As Double
        Public Property RmsResidual As Double
        Public Property Note As String = ""
        ''' <summary>El shape no pudo procesarse (sin malla base, ausente del horneado, o conteo de
        ''' vértices distinto). Relevante para el veredicto: con shapes omitidos NO se puede afirmar
        ''' "sin cambios", porque la diferencia podría vivir justo en el shape que no se miró.</summary>
        Public Property Skipped As Boolean
    End Class

    Public Class Result
        Public Property Ok As Boolean
        Public Property Message As String = ""
        ''' <summary>Bloques de sculpt reconstruidos, uno por shape con chargen tri (Host). Deltas ya en
        ''' espacio de mundo — no hay divisor que aplicar: no pasamos por un .jslot.</summary>
        Public Property SculptParts As List(Of NPC_SculptPart)
        Public Property Shapes As New List(Of ShapeReport)
        ''' <summary>18 sliders con signo, tal como irían al NAM9 (índice 18/VampireMorph no se ajusta:
        ''' es un canal conocido, no una incógnita).</summary>
        Public Property Nam9 As Single()
        ''' <summary>Valores EFECTIVOS actuales (overlay si lo hay, si no el record crudo) — el "desde"
        ''' del informe. Misma fuente que LoadSseMorphValues, para que lo que muestra el diálogo coincida
        ''' con lo que el usuario ve en el tab de morphs.</summary>
        Public Property Nam9Before As Single()
        ''' <summary>NAMA reconstruida = la del RECORD CRUDO. Es un canal CONOCIDO (no se ajusta), pero
        ''' ApplyJslotToPreset la escribe SIEMPRE, así que si el overlay actual tiene otra, aplicar el
        ''' preset la revierte. Por eso cuenta como cambio y hay que mostrarla.</summary>
        Public Property Nama As UInteger()
        Public Property NamaBefore As UInteger()
        Public Property TotalSculptedVerts As Integer
        Public Property WorstResidual As Double
        Public Property SkippedShapes As Integer
        Public Property ChangedSliders As Integer
        Public Property ChangedNama As Integer
        ''' <summary>True SÓLO si nada cambia: ningún slider se mueve, no se emite un solo vértice de
        ''' sculpt Y todos los shapes emparejaron. Es decir: el record del NPC ya reproduce el FaceGen
        ''' horneado, no hay nada que recuperar. El "todos emparejaron" se exige a propósito — declarar
        ''' "sin cambios" habiendo omitido un shape sería afirmar algo que no se midió: la diferencia
        ''' podría vivir justo en el shape que no se pudo mirar.</summary>
        Public ReadOnly Property IsNoOp As Boolean
            Get
                Return ChangedSliders = 0 AndAlso ChangedNama = 0 AndAlso
                       TotalSculptedVerts = 0 AndAlso SkippedShapes = 0
            End Get
        End Property
        Public Property Report As String = ""
    End Class

    ' Par (Pos, Neg) por índice de slider — MISMA tabla que el motor. No se duplica acá: se consulta la
    ' del engine-verified map para que un cambio allá no pueda desincronizar esto.
    Private ReadOnly Property SliderPairs As SseNam9MorphMap.Slider()
        Get
            Return SseNam9MorphMap.Sliders
        End Get
    End Property

    ''' <summary>
    ''' Construye el jslot sintético. NO escribe nada a disco y NO muta el estado del NPC: devuelve el
    ''' objeto para que el llamante lo pase por el mismo camino que un preset cargado de fichero.
    ''' </summary>
    ''' <param name="appliedPresets">Overlay ACTUAL, sólo para leer y preservar campos que el jslot
    ''' sintético no reconstruye (hair color). La predicción se hace SIEMPRE contra el record crudo:
    ''' si se hiciera contra el overlay, un sculpt de una corrida previa se hornearía dentro de la
    ''' predicción y el residual saldría ~0 (auto-confirmación circular).</param>
    Public Function Build(npcFormID As UInteger,
                          pluginManager As PluginManager,
                          appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                          Optional sculptThreshold As Single = DefaultSculptThreshold) As Result
        Dim res As New Result()
        Dim sb As New System.Text.StringBuilder()

        If pluginManager Is Nothing Then
            res.Message = "No PluginManager." : Return res
        End If

        ' ---- 1) NIF horneado ----------------------------------------------------------------
        Dim bakedRel = FaceGenBuilder.ResolveFaceGenPath(npcFormID, pluginManager)
        If String.IsNullOrEmpty(bakedRel) Then
            res.Message = "Could not resolve the FaceGen path (originating plugin unknown)." : Return res
        End If
        Dim bakedBytes As Byte() = Nothing
        Try
            bakedBytes = FilesDictionary_class.GetBytes(bakedRel.ToLowerInvariant())
        Catch ex As Exception
        End Try
        If bakedBytes Is Nothing OrElse bakedBytes.Length = 0 Then
            res.Message = $"No baked FaceGen for this NPC:{vbCrLf}{bakedRel}{vbCrLf}{vbCrLf}" &
                          "Without the baked .nif there is nothing to reverse." : Return res
        End If
        Dim bakedNif As New Nifcontent_Class_Manolo()
        Try
            bakedNif.Load_Manolo(bakedBytes)
        Catch ex As Exception
            res.Message = "The baked FaceGen could not be parsed: " & ex.Message : Return res
        End Try

        ' ---- 2) Estado con NAM9 = 0 y sin sculpt --------------------------------------------
        ' Se consigue vía el MISMO mecanismo de overlay que usa la app (un preset sintético), no
        ' mutando el NPC_Data cacheado: NpcRecordOverlay pisa Nam9Raw con preset.SseNam9 y copia
        ' SseSculptParts/SseCustomMorphs del preset (Nothing ⇒ sin sculpt, sin custom morphs).
        ' NAMA / NAM7 / keywords se dejan sin tocar ⇒ vienen del record = canales CONOCIDOS.
        Dim zeroPreset As New LooksmenuLoader.LooksmenuPreset()
        zeroPreset.HasSseMorphs = True
        zeroPreset.SseNam9 = New Single(SseNam9MorphMap.Nam9SliderCount - 1) {}   ' todo 0
        zeroPreset.SseNama = Nothing                                             ' ⇒ NAMA del record
        zeroPreset.SseSculptParts = Nothing
        zeroPreset.SseSculptHead = Nothing
        zeroPreset.SseCustomMorphs = Nothing
        Dim zeroMap As New Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset) From {{npcFormID, zeroPreset}}

        Dim state = FaceGenBuildPipeline.BuildBakeState(npcFormID, pluginManager, zeroMap, Nothing)
        If state Is Nothing OrElse state.NpcData Is Nothing Then
            res.Message = "Could not build the BakeState (NPC or RACE did not resolve)." : Return res
        End If
        If state.NpcData.Game <> Config_App.Game_Enum.Skyrim Then
            res.Message = "Skyrim SE only. FO4 bakes through the FMRS bone rig, not a per-vertex morph, " &
                          "and LooksMenu has no sculpt channel to write the residual into." : Return res
        End If

        Dim morphRaceEd = RecordParsers.ResolveMorphRaceEditorId(state.Race, pluginManager)
        Dim raceKeywords = RecordParsers.GetRaceKeywordEditorIds(state.Race, pluginManager)

        ' ---- 3) Recorrer los head parts igual que el bake ------------------------------------
        Dim mergedRoots = HeadPartResolver.MergeHeadPartsWithRaceDefaults(
            state.NpcData.RaceFormID, state.NpcData.IsFemale, state.NpcData.HeadPartFormIDs, pluginManager)

        ' Caché local de conteos de vértices por .tri para el redirect High Poly Head (mismo resolver
        ' que usan render y bake — no se replica la regla, se llama).
        Dim triVertCache As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
        Dim vertsOf As Func(Of String, Integer) =
            Function(p)
                If String.IsNullOrEmpty(p) Then Return -1
                Dim k = MeshPathHelpers.NormalizeMeshKey(p)
                Dim n As Integer
                If triVertCache.TryGetValue(k, n) Then Return n
                n = -1
                Try
                    Dim b = FilesDictionary_class.GetBytes(k)
                    If b IsNot Nothing AndAlso b.Length > 0 Then
                        Dim h = TriHeadParser.ParseTriHeadFromBytes(b)
                        If h IsNot Nothing Then n = CInt(h.NumVertices)
                    End If
                Catch ex As Exception
                End Try
                triVertCache(k) = n
                Return n
            End Function

        ' Un "trabajo" por shape emparejado: base neutral + horneado + columnas gateadas.
        Dim jobs As New List(Of ShapeJob)()
        Dim usedBaked As New HashSet(Of INiShape)()

        For Each entry In HeadPartResolver.EnumerateHdptChain(mergedRoots, pluginManager)
            Dim hdpt = entry.Hdpt
            If hdpt Is Nothing OrElse String.IsNullOrEmpty(hdpt.MeshPath) Then Continue For

            Dim baseKey = MeshPathHelpers.NormalizeMeshKey(hdpt.MeshPath)
            Dim baseBytes As Byte() = Nothing
            Try
                baseBytes = FilesDictionary_class.GetBytes(baseKey)
            Catch ex As Exception
            End Try
            If baseBytes Is Nothing OrElse baseBytes.Length = 0 Then
                res.Shapes.Add(New ShapeReport With {.ShapeName = If(hdpt.EditorID, baseKey),
                                                     .Note = "base mesh not found - skipped", .Skipped = True})
                Continue For
            End If
            Dim baseNif As New Nifcontent_Class_Manolo()
            Try
                baseNif.Load_Manolo(baseBytes)
            Catch ex As Exception
                res.Shapes.Add(New ShapeReport With {.ShapeName = If(hdpt.EditorID, baseKey),
                                                     .Note = "base mesh failed to parse - skipped", .Skipped = True})
                Continue For
            End Try

            ' Redirect HPH sobre los tres slots — el MISMO resolver que el bake (regla de oro render==bake).
            For Each srcShape In baseNif.NifShapes
                Dim srcGeom = ShapeGeometryFactory.[For](srcShape, baseNif)
                Dim nv = srcGeom.VertexCount
                If nv <= 0 Then Continue For

                Dim bakedShape = MatchBakedShape(bakedNif, srcShape, nv, usedBaked)
                If bakedShape Is Nothing Then
                    res.Shapes.Add(New ShapeReport With {
                        .ShapeName = ShapeNameOf(srcShape), .VertexCount = nv,
                        .Note = "not present in the baked FaceGen - skipped", .Skipped = True})
                    Continue For
                End If
                usedBaked.Add(bakedShape)

                Dim bakedGeom = ShapeGeometryFactory.[For](bakedShape, bakedNif)
                If bakedGeom.VertexCount <> nv Then
                    ' ⛔ Sin correspondencia índice-a-índice no hay resta posible. Puede ser una malla
                    ' custom, otra raza, o un NIF pasado por NIF Optimizer (suelda/reordena vértices).
                    res.Shapes.Add(New ShapeReport With {
                        .ShapeName = ShapeNameOf(srcShape), .VertexCount = nv,
                        .Note = $"VERTEX COUNT MISMATCH (base {nv} vs baked {bakedGeom.VertexCount}) - SKIPPED, no index correspondence", .Skipped = True})
                    Continue For
                End If

                Dim rRace = NpcMorphResolver.ResolveHphHeadPartTriPath(hdpt.RaceMorphTriPath, nv, NpcMorphResolver.HphTriSlot.Race, vertsOf)
                Dim rChargen = NpcMorphResolver.ResolveHphHeadPartTriPath(hdpt.ChargenMorphTriPath, nv, NpcMorphResolver.HphTriSlot.Chargen, vertsOf)
                Dim rMesh = NpcMorphResolver.ResolveHphHeadPartTriPath(hdpt.TriPath, nv, NpcMorphResolver.HphTriSlot.Mesh, vertsOf)
                Dim triHead = FaceGenBuildPipeline.LoadMergedHeadTri(rRace, rChargen, state, rMesh)

                Dim job As New ShapeJob With {
                    .Hdpt = hdpt,
                    .ChargenTriPath = rChargen,
                    .ShapeName = ShapeNameOf(srcShape),
                    .N = nv,
                    .TriHead = triHead
                }

                ' Posiciones neutras y horneadas (System.Numerics → Vector3d, igual que el bake).
                Dim bp = srcGeom.GetVertexPositions()
                Dim kp = bakedGeom.GetVertexPositions()
                ReDim job.BaseP(nv - 1)
                ReDim job.BakedP(nv - 1)
                For i = 0 To nv - 1
                    job.BaseP(i) = New Vector3d(bp(i).X, bp(i).Y, bp(i).Z)
                    job.BakedP(i) = New Vector3d(kp(i).X, kp(i).Y, kp(i).Z)
                Next

                ' La predicción de canales CONOCIDOS se calcula más abajo (RecomputeKnown): depende de la
                ' NAMA candidata, que ahora se AJUSTA en vez de asumirse.

                ' Columnas gateadas de los 36 morphs direccionales, sobre base CERO ⇒ el resultado ES
                ' el delta ya gateado. El gate sale del MOTOR (MorphEngine), no de una copia del umbral.
                If triHead IsNot Nothing Then
                    Dim zero(nv - 1) As Vector3d
                    ReDim job.Cols(SliderPairs.Length * 2 - 1)
                    For s = 0 To SliderPairs.Length - 1
                        job.Cols(s * 2) = GatedColumn(triHead, SliderPairs(s).Pos, zero)
                        job.Cols(s * 2 + 1) = GatedColumn(triHead, SliderPairs(s).Neg, zero)
                    Next
                End If

                jobs.Add(job)
            Next
        Next

        If jobs.Count = 0 Then
            res.Message = "No shape of the baked FaceGen could be matched to its base mesh." : Return res
        End If

        ' ---- 4) Ajuste conjunto sobre TODOS los shapes ---------------------------------------
        ' Conjunto y no sólo la cabeza: los NAM9 deforman también cejas/ojos/boca (mismos nombres de
        ' morph en sus propios chargen tris), así que ajustar sólo la cabeza minimizaría el sculpt de
        ' la cabeza dejando el de las otras partes al azar. Se minimiza el TOTAL.
        '
        ' G = AᵀA se calcula UNA sola vez: las columnas A son los morphs direccionales, que NO dependen
        ' de la NAMA. Sólo c = Aᵀb cambia con cada NAMA candidata (b = horneado − conocidos). Eso es lo
        ' que hace barato probar decenas de combinaciones de tipos.
        Dim nCols = SliderPairs.Length * 2
        Dim g(nCols - 1, nCols - 1) As Double
        For Each job In jobs
            If job.Cols Is Nothing Then Continue For
            For i = 0 To job.N - 1
                For a = 0 To nCols - 1
                    Dim ca = job.Cols(a)
                    If ca Is Nothing Then Continue For
                    Dim ax = ca(i).X, ay = ca(i).Y, az = ca(i).Z
                    If ax = 0 AndAlso ay = 0 AndAlso az = 0 Then Continue For
                    For b2 = a To nCols - 1
                        Dim cb = job.Cols(b2)
                        If cb Is Nothing Then Continue For
                        g(a, b2) += ax * cb(i).X + ay * cb(i).Y + az * cb(i).Z
                    Next
                Next
            Next
        Next
        For a = 0 To nCols - 1
            For b2 = a + 1 To nCols - 1
                g(b2, a) = g(a, b2)
            Next
        Next

        ' ---- 4b) NAMA: selección DISCRETA por descenso alternado ------------------------------
        ' NAMA no es continua — es una elección de tipo aplicada a peso fijo 1.0 (0xFFFFFFFF = ninguno,
        ' 0 = "Default", N = "<familia><N>"). Así que no se ajusta por mínimos cuadrados: se PRUEBAN los
        ' candidatos que el .tri realmente contiene y se elige el que minimiza el residual.
        '
        ' ⭐ Por qué se ajusta en vez de leerla del record: si el mod borró/cambió la NAMA (igual que hizo
        ' con los NAM9), asumirla del record haría que la diferencia entre el tipo real y el del record
        ' cayera ENTERA en el sculpt — un preset de tipo disfrazado de escultura, y la NAMA sin recuperar.
        ' Ajustándola deja de importar de dónde leerla: no se asume, se mide.
        '
        ' ⛔ Se evalúa construyendo el plan COMPLETO por el motor con cada NAMA candidata, no sumando
        ' columnas por familia. Motivo: AddNamaTypePreset mapea el valor 0 de CUALQUIER familia al MISMO
        ' morph "Default", y el dedup-suma del plan colapsa esos canales sumando pesos (dos familias en 0
        ' ⇒ "Default" a peso 2,0). Columnas independientes por familia no reproducirían eso.
        Dim namaVec(SseNam9MorphMap.NamaFamilyCount - 1) As UInteger
        For f = 0 To namaVec.Length - 1
            namaVec(f) = If(state.NpcData.NamaRaw IsNot Nothing AndAlso state.NpcData.NamaRaw.Length >= 16,
                            BitConverter.ToUInt32(state.NpcData.NamaRaw, f * 4), SseNam9MorphMap.NamaUnset)
        Next

        Dim bestW = EvaluateNama(jobs, state, namaVec, morphRaceEd, raceKeywords, g, nCols)
        Dim bestRes = bestW.ResidualSq
        ' El catálogo depende SÓLO de los .tri de `jobs`, que no cambian durante el descenso: se arma UNA
        ' vez. Antes se reconstruía adentro de NamaCandidates, o sea hasta 3 rondas × 4 familias = 12 veces,
        ' y cada una recorría todos los morphs de todos los jobs para las 4 familias y devolvía una sola.
        Dim triNames As New List(Of String)
        For Each job In jobs
            If job.TriHead Is Nothing OrElse job.TriHead.Morphs Is Nothing Then Continue For
            For Each m In job.TriHead.Morphs
                If Not String.IsNullOrEmpty(m.Name) Then triNames.Add(m.Name)
            Next
        Next
        Dim typeCatalog = SseNam9MorphMap.BuildTypeCatalog(triNames)
        For round = 1 To 3
            Dim improvedAny = False
            For f = 0 To SseNam9MorphMap.NamaFamilyCount - 1
                Dim keep = namaVec(f)
                For Each cand In NamaCandidates(typeCatalog, f, keep)
                    If cand = keep Then Continue For
                    namaVec(f) = cand
                    Dim trial = EvaluateNama(jobs, state, namaVec, morphRaceEd, raceKeywords, g, nCols)
                    ' Margen relativo para NO cambiar un tipo por una mejora despreciable: sin esto el
                    ' ajuste oscilaría entre tipos casi equivalentes y reportaría cambios cosméticos.
                    If trial.ResidualSq < bestRes * 0.999 Then
                        bestRes = trial.ResidualSq
                        bestW = trial
                        keep = cand
                        improvedAny = True
                    End If
                Next
                namaVec(f) = keep
            Next
            If Not improvedAny Then Exit For
        Next

        ' Estado final: dejar el plan/predicción coherentes con la NAMA ganadora.
        Dim finalEval = EvaluateNama(jobs, state, namaVec, morphRaceEd, raceKeywords, g, nCols)
        Dim w = finalEval.W

        ' → NAM9 con signo.
        Dim nam9(SseNam9MorphMap.Nam9SliderCount - 1) As Single
        For s = 0 To Math.Min(SliderPairs.Length, nam9.Length) - 1
            Dim vp = w(s * 2), vn = w(s * 2 + 1)
            If vp >= vn Then
                nam9(s) = CSng(vp)
            Else
                nam9(s) = -CSng(vn)
            End If
            If Math.Abs(nam9(s)) < 0.001F Then nam9(s) = 0.0F   ' zona muerta del motor
        Next
        res.Nam9 = nam9

        ' "Desde": el estado EFECTIVO actual — overlay si tomó posesión, si no el record crudo. Misma
        ' regla que LoadSseMorphValues (EditFace_Form.vb:1267) para que el diálogo y el tab coincidan.
        Dim before(SseNam9MorphMap.Nam9SliderCount - 1) As Single
        Dim curEffective = NpcRecordOverlay.ResolveOverlaidNpcData(npcFormID, pluginManager, appliedPresets)
        Dim beforeRaw = If(curEffective IsNot Nothing, curEffective.Nam9Raw, Nothing)
        If beforeRaw IsNot Nothing AndAlso beforeRaw.Length >= 76 Then
            For s = 0 To before.Length - 1
                Dim v = BitConverter.ToSingle(beforeRaw, s * 4)
                If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then v = 0
                before(s) = v
            Next
        End If
        res.Nam9Before = before

        ' NAMA "desde": misma fuente efectiva. Se compara aunque NAMA no se ajuste, porque
        ' ApplyJslotToPreset la escribe siempre ⇒ aplicar revierte cualquier tipo editado en el overlay.
        Dim namaBefore(SseNam9MorphMap.NamaFamilyCount - 1) As UInteger
        For f = 0 To namaBefore.Length - 1 : namaBefore(f) = SseNam9MorphMap.NamaUnset : Next
        Dim namaBeforeRaw = If(curEffective IsNot Nothing, curEffective.NamaRaw, Nothing)
        If namaBeforeRaw IsNot Nothing AndAlso namaBeforeRaw.Length >= 16 Then
            For f = 0 To namaBefore.Length - 1
                namaBefore(f) = BitConverter.ToUInt32(namaBeforeRaw, f * 4)
            Next
        End If
        res.NamaBefore = namaBefore

        ' ---- 5) Sculpt = residual tras aplicar los NAM9 ajustados ----------------------------
        ' Superposición aditiva exacta: como el gate no depende del peso, aplicar dos canales del mismo
        ' morph o uno con la suma de pesos da idéntico resultado (misma máscara, mismos deltas) — que es
        ' justo lo que hace el dedup-suma de NpcMorphResolver.vb:559.
        ' ⛔ NO se construye un .jslot ni se pasa por ApplyJslotToPreset. Ese camino escribe SEIS campos
        ' INCONDICIONALMENTE (RaceMenuPresetMapper.vb:315-328: SseWeight, BodyMorphSliders,
        ' BodyMorphsKeyed, SseBodyOverlays, SseNodeTransforms, SseSkinOverrides), así que un jslot que no
        ' los trajera ponía el peso a 0 (síntoma: cuello y cuerpo cambian) y borraba body morphs,
        ' tatuajes, node transforms y skin overrides.
        ' Rellenarlos con un round-trip ToJslot lo tapaba, pero dejaba la corrección dependiendo de que
        ' ToJslot∘ApplyJslotToPreset fuera la identidad — sin verificar, y con pérdida silenciosa en cada
        ' aplicación si no lo fuera. Aquí se escriben DIRECTAMENTE los cuatro campos que esta feature
        ' reconstruye (ver ApplyTo) y no se toca ningún otro: nada que preservar es nada que romper.

        res.Nama = DirectCast(namaVec.Clone(), UInteger())
        Dim sculptParts As New List(Of NPC_SculptPart)()

        Dim worst As Double = 0
        For Each job In jobs
            Dim rep As New ShapeReport With {
                .ShapeName = job.ShapeName, .Host = If(job.ChargenTriPath, ""), .VertexCount = job.N}

            ' Predicción final = conocidos + Σ w·columna.
            Dim pred = job.KnownP.ToArray()
            If job.Cols IsNot Nothing Then
                For a = 0 To nCols - 1
                    Dim ca = job.Cols(a)
                    If ca Is Nothing OrElse w(a) <= 0 Then Continue For
                    Dim wa = w(a)
                    For i = 0 To job.N - 1
                        pred(i) = New Vector3d(pred(i).X + ca(i).X * wa,
                                               pred(i).Y + ca(i).Y * wa,
                                               pred(i).Z + ca(i).Z * wa)
                    Next
                Next
            End If

            Dim part As New NPC_SculptPart With {
                .Host = If(job.ChargenTriPath, ""), .Verts = New List(Of NPC_SculptVert)()}
            Dim sumSq As Double = 0, maxAbs As Double = 0, dropped As Integer = 0
            For i = 0 To job.N - 1
                Dim dx = job.BakedP(i).X - pred(i).X
                Dim dy = job.BakedP(i).Y - pred(i).Y
                Dim dz = job.BakedP(i).Z - pred(i).Z
                Dim m = Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz)))
                sumSq += dx * dx + dy * dy + dz * dz
                If m > maxAbs Then maxAbs = m
                If m < sculptThreshold Then
                    If m > 0 Then dropped += 1
                    Continue For
                End If
                ' Se cuantiza a la MISMA rejilla que el .jslot (1/10000) aunque acá no pasemos por uno:
                ' así "lo que ves ahora" == "lo que queda tras guardar el preset y recargarlo". La pérdida
                ' (1e-4) es más de un orden menor que el umbral de sculpt, así que no cambia decisiones.
                part.Verts.Add(New NPC_SculptVert With {
                    .Index = i,
                    .Dx = CSng(Math.Round(dx * JslotSculptDivisor) / JslotSculptDivisor),
                    .Dy = CSng(Math.Round(dy * JslotSculptDivisor) / JslotSculptDivisor),
                    .Dz = CSng(Math.Round(dz * JslotSculptDivisor) / JslotSculptDivisor)})
            Next
            rep.RmsResidual = Math.Sqrt(sumSq / Math.Max(1, job.N))
            rep.MaxResidual = maxAbs
            rep.SculptedVerts = part.Verts.Count
            rep.DroppedVerts = dropped
            If maxAbs > worst Then worst = maxAbs

            If String.IsNullOrEmpty(part.Host) Then
                ' Sin chargen tri no hay Host ⇒ el sculpt es INEXPRESABLE para este shape (pelo, hairlines).
                ' No es un fallo: su única deformación es SkinnyMorph, que es conocido ⇒ residual ~0.
                If rep.SculptedVerts > 0 Then
                    rep.Note = $"no chargen tri (cannot carry sculpt) yet {rep.SculptedVerts} vertices still deviate - investigate"
                Else
                    rep.Note = "no chargen tri - no residual either, nothing to express"
                End If
            ElseIf part.Verts.Count > 0 Then
                sculptParts.Add(part)
            End If

            ' Las barbas arrastran un residual irreducible vs el CK (max ~0,067 / rms ~0,0096, probado NO
            ' expresable como combinación de sus morphs ni como transform afín, e independiente del NPC —
            ' ver FaceGenBuilder.vb:884-887). Ese residual va a caer aquí como si fuera escultura del autor.
            If IsBeardLike(job) Then
                rep.Note = (rep.Note & " | beard: this residual includes the known CK artefact, not only author data").Trim()
            End If

            res.Shapes.Add(rep)
            res.TotalSculptedVerts += rep.SculptedVerts
        Next

        res.WorstResidual = worst
        res.SculptParts = sculptParts
        res.Ok = True

        ' ---- 6) Informe ---------------------------------------------------------------------
        ' Veredicto ANTES del informe, para poder abrir con el titular.
        res.SkippedShapes = res.Shapes.Where(Function(r) r.Skipped).Count()
        Dim changedCount As Integer = 0
        For s = 0 To Math.Min(SliderPairs.Length, nam9.Length) - 1
            Dim f0 As Single = If(before IsNot Nothing AndAlso s < before.Length, before(s), 0.0F)
            If Math.Abs(nam9(s) - f0) >= 0.001F Then changedCount += 1
        Next
        res.ChangedSliders = changedCount
        Dim changedNamaCount As Integer = 0
        For f = 0 To SseNam9MorphMap.NamaFamilyCount - 1
            If res.Nama IsNot Nothing AndAlso res.NamaBefore IsNot Nothing AndAlso
               res.Nama(f) <> res.NamaBefore(f) Then changedNamaCount += 1
        Next
        res.ChangedNama = changedNamaCount

        sb.AppendLine("=======================================================================")
        If res.IsNoOp Then
            sb.AppendLine("  RESULT: NO CHANGE - nothing to recover.")
            sb.AppendLine("=======================================================================")
            sb.AppendLine("  The NPC record ALREADY reproduces the baked FaceGen exactly: no slider")
            sb.AppendLine("  moves, not a single vertex needs sculpting, and every shape matched.")
            sb.AppendLine("  Applying this would be a no-op. Nothing was lost for this NPC.")
        ElseIf res.SkippedShapes > 0 AndAlso res.ChangedSliders = 0 AndAlso res.TotalSculptedVerts = 0 Then
            sb.AppendLine("  RESULT: no change found, but NOT conclusive.")
            sb.AppendLine("=======================================================================")
            sb.AppendLine($"  Nothing to change in what could be measured - but {res.SkippedShapes} shape(s) were")
            sb.AppendLine("  SKIPPED (see the per-shape table). A difference could live in exactly")
            sb.AppendLine("  the shape that could not be inspected, so this is not a clean bill.")
        Else
            sb.AppendLine($"  RESULT: {res.ChangedSliders} slider(s) and {res.ChangedNama} face-part type(s) change, " &
                          $"{res.TotalSculptedVerts} vertices sculpted.")
            sb.AppendLine("=======================================================================")
            If res.SkippedShapes > 0 Then
                sb.AppendLine($"  WARNING: {res.SkippedShapes} shape(s) skipped - see the per-shape table.")
            End If
        End If
        sb.AppendLine()
        sb.AppendLine($"FaceGen : {bakedRel}")
        sb.AppendLine($"Morph race : {morphRaceEd}    Weight (NAM7) : {WeightOf(state.NpcData):0.##}")
        sb.AppendLine($"Shapes matched : {jobs.Count}")
        ' El estado del redirect HPH se imprime a propósito: el sculpt son deltas ABSOLUTOS calculados
        ' contra la predicción de canales conocidos, y esa predicción depende de QUÉ .tri se resolvió.
        ' Cambiar el toggle después de generar el preset hace derivar la cara.
        sb.AppendLine($"High Poly Head tri redirect : {If(Config_App.Current.Setting_SseResolveHighPolyHeadTri, "ON", "OFF")}" &
                      "   (the sculpt is tied to this setting - flipping it later will drift the face)")
        sb.AppendLine()
        sb.AppendLine("NAM9 SLIDERS  —  current value  ->  reconstructed value")
        sb.AppendLine("These are the values that MINIMISE the sculpt, not necessarily the author's originals:")
        sb.AppendLine("the decomposition is not unique, so many (NAM9, sculpt) pairs give the same baked head.")
        sb.AppendLine()
        sb.AppendLine("  idx  slider              from        to        change   chargen morph")
        sb.AppendLine("  ---  ------------------  --------  --------  --------  -------------------")
        Dim changed As Integer = 0
        For s = 0 To Math.Min(SliderPairs.Length, nam9.Length) - 1
            Dim f As Single = If(before IsNot Nothing AndAlso s < before.Length, before(s), 0.0F)
            Dim t As Single = nam9(s)
            Dim d As Single = t - f
            Dim morphNm As String = ""
            If Math.Abs(t) >= 0.001F Then morphNm = If(t >= 0, SliderPairs(s).Pos, SliderPairs(s).Neg)
            Dim mark As String = "   "
            If Math.Abs(d) >= 0.001F Then
                mark = " * "
                changed += 1
            End If
            sb.AppendLine($" {mark}{s,2}  {SliderPairs(s).Label,-18}  {f,8:0.0000}  {t,8:0.0000}  {d,8:+0.0000;-0.0000; 0.0000}  {morphNm}")
        Next
        sb.AppendLine()
        sb.AppendLine($"  {changed} of {SliderPairs.Length} sliders change ( * marks a change ).")
        If nam9.All(Function(v) Math.Abs(v) < 0.001F) Then
            sb.AppendLine("  No slider was recovered — the whole face shape ended up in the sculpt.")
        End If
        sb.AppendLine()
        sb.AppendLine("NAMA FACE-PART TYPES  —  current  ->  reconstructed")
        sb.AppendLine("These are RECOVERED from the baked geometry, not read from the record: the type that")
        sb.AppendLine("best explains the baked head is picked out of the types the .tri actually contains.")
        sb.AppendLine("So a record whose types were wiped or altered gets them back, instead of the difference")
        sb.AppendLine("silently ending up as sculpt. A tie keeps the current value - no cosmetic churn.")
        sb.AppendLine()
        sb.AppendLine("  family              from        to")
        sb.AppendLine("  ------------------  ----------  ----------")
        For f = 0 To SseNam9MorphMap.NamaFamilyCount - 1
            Dim fromV = If(res.NamaBefore IsNot Nothing, res.NamaBefore(f), SseNam9MorphMap.NamaUnset)
            Dim toV = If(res.Nama IsNot Nothing, res.Nama(f), SseNam9MorphMap.NamaUnset)
            Dim mk = If(fromV <> toV, " * ", "   ")
            sb.AppendLine($" {mk}{SseNam9MorphMap.Families(f).Label,-18}  {NamaText(fromV),-10}  {NamaText(toV),-10}")
        Next
        sb.AppendLine()
        sb.AppendLine("PER-SHAPE RESIDUAL  —  what the sliders could NOT explain, which becomes sculpt")
        sb.AppendLine()
        sb.AppendLine("  shape                          verts   sculpt      max       rms")
        sb.AppendLine("  -----------------------------  ------  ------  --------  --------")
        For Each r In res.Shapes
            sb.AppendLine($"  {Trunc(r.ShapeName, 29),-29}  {r.VertexCount,6}  {r.SculptedVerts,6}  " &
                          $"{r.MaxResidual,8:0.0000}  {r.RmsResidual,8:0.0000}")
            If Not String.IsNullOrEmpty(r.Note) Then sb.AppendLine($"        -> {r.Note}")
        Next
        sb.AppendLine()
        sb.AppendLine($"  Sculpted vertices total : {res.TotalSculptedVerts}     Worst residual : {worst:0.0000}")
        sb.AppendLine()
        sb.AppendLine("HOW TO READ THIS")
        sb.AppendLine("  · The baked geometry is reproduced EXACTLY by construction — the sculpt absorbs")
        sb.AppendLine("    whatever the sliders don't explain. These residuals are NOT a fidelity check.")
        sb.AppendLine("  · Fewer sculpted vertices = the sliders explained more = a cleaner preset.")
        sb.AppendLine("  · A shape listed as skipped for a vertex-count mismatch means the base mesh does")
        sb.AppendLine("    not correspond (custom head, wrong race, or a NIF run through NIF Optimizer).")
        sb.AppendLine()
        sb.AppendLine("NOT RECOVERABLE (not baked into the NIF — left empty in the preset):")
        sb.AppendLine("  textures, custom warpaints, body overlays, node transforms.")
        res.Report = sb.ToString()
        Return res
    End Function

    ' =====================================================================================
    ' Internos
    ' =====================================================================================

    Private Class ShapeJob
        Public Property Hdpt As HDPT_Data
        Public Property ChargenTriPath As String
        Public Property ShapeName As String
        Public Property N As Integer
        Public Property TriHead As TriHeadFile
        Public BaseP As Vector3d()
        Public BakedP As Vector3d()
        Public KnownP As Vector3d()
        ''' <summary>36 columnas (18 pares Pos/Neg), YA gateadas. Nothing donde el morph no existe.</summary>
        Public Cols As Vector3d()()
    End Class

    ''' <summary>Rejilla del sculpt del .jslot (1/10000). Ver el comentario del cuantizado.</summary>
    Private Const JslotSculptDivisor As Double = 10000.0R

    ''' <summary>
    ''' ÚNICO punto donde esta feature escribe en el overlay. Toca EXACTAMENTE cinco campos, todos los
    ''' que reconstruye, y ningún otro:
    '''     SseNam9 · SseNama · HasSseMorphs · SseSculptParts · SseSculptHead
    '''
    ''' ⛔ Deliberadamente NO se pasa por RaceMenuPresetMapper.ApplyJslotToPreset. Ese camino existe para
    ''' cargar un .jslot COMPLETO de disco y por eso escribe seis campos más de forma incondicional
    ''' (SseWeight, BodyMorphSliders, BodyMorphsKeyed, SseBodyOverlays, SseNodeTransforms,
    ''' SseSkinOverrides). Alimentarlo con un jslot parcial ponía el peso a 0 y borraba el resto; y
    ''' rellenarlos con un round-trip ToJslot sólo tapaba el síntoma, dejando la corrección colgando de
    ''' que ese round-trip fuese la identidad. Escribiendo sólo lo reconstruido no hay nada que preservar.
    '''
    ''' La regla del bloque de cabeza NO se replica: se llama a la de RaceMenuPresetMapper.
    ''' </summary>
    Public Sub ApplyTo(res As Result, preset As LooksmenuLoader.LooksmenuPreset)
        If res Is Nothing OrElse Not res.Ok OrElse preset Is Nothing Then Return
        If res.Nam9 IsNot Nothing Then preset.SseNam9 = DirectCast(res.Nam9.Clone(), Single())
        If res.Nama IsNot Nothing Then preset.SseNama = DirectCast(res.Nama.Clone(), UInteger())
        preset.HasSseMorphs = True
        If res.SculptParts IsNot Nothing AndAlso res.SculptParts.Count > 0 Then
            preset.SseSculptParts = res.SculptParts
            preset.SseSculptHead = RaceMenuPresetMapper.SelectHeadSculptBlock(res.SculptParts)
        Else
            ' Reconstrucción sin sculpt = la cara se explica entera con NAM9/NAMA. Hay que LIMPIAR el
            ' sculpt previo, no dejarlo: si no, deltas de otra reconstrucción quedarían encima de una
            ' cara que ya no los necesita.
            preset.SseSculptParts = Nothing
            preset.SseSculptHead = Nothing
        End If
    End Sub

    ''' <summary>
    ''' Evalúa una NAMA candidata: recalcula la predicción de canales conocidos de cada shape con esa
    ''' NAMA, ajusta los NAM9 por BVLS y devuelve los pesos + el residual cuadrático total.
    ''' Deja <c>job.KnownP</c> coherente con la NAMA evaluada, así que la última llamada define el estado.
    '''
    ''' Residual = Σ‖b‖² − 2·wᵀc + wᵀGw, con G precalculada (no depende de la NAMA).
    ''' </summary>
    Private Function EvaluateNama(jobs As List(Of ShapeJob),
                                  state As FaceGenBuildPipeline.BakeState,
                                  namaVec As UInteger(),
                                  morphRaceEd As String,
                                  raceKeywords As List(Of String),
                                  g As Double(,), nCols As Integer) As (W As Double(), ResidualSq As Double)
        ' ⛔ Se ASIGNA un array nuevo a NamaRaw, nunca se muta el existente: el shadow que devuelve
        ' ResolveOverlaidNpcData comparte la referencia del array del NPC_Data CRUDO cacheado
        ' (NpcRecordOverlay.vb:317 `shadow.NamaRaw = raw.NamaRaw`), así que escribir dentro del array
        ' corrompería el record en caché para toda la app.
        Dim raw(15) As Byte
        For f = 0 To Math.Min(namaVec.Length, SseNam9MorphMap.NamaFamilyCount) - 1
            BitConverter.GetBytes(namaVec(f)).CopyTo(raw, f * 4)
        Next
        state.NpcData.NamaRaw = raw

        Dim c(nCols - 1) As Double
        Dim bNormSq As Double = 0
        For Each job In jobs
            If job.TriHead IsNot Nothing Then
                Dim plan = NpcMorphResolver.BuildFaceMorphPlanFromNam9(
                    state.NpcData, job.TriHead, morphRaceEd, raceKeywords,
                    shapeChargenTriPath:=job.ChargenTriPath)
                job.KnownP = MorphEngine.ApplyChannelsToVertexArray(job.BaseP, plan)
            Else
                job.KnownP = job.BaseP.ToArray()
            End If
            For i = 0 To job.N - 1
                Dim bx = job.BakedP(i).X - job.KnownP(i).X
                Dim by = job.BakedP(i).Y - job.KnownP(i).Y
                Dim bz = job.BakedP(i).Z - job.KnownP(i).Z
                bNormSq += bx * bx + by * by + bz * bz
                If job.Cols Is Nothing Then Continue For
                For a = 0 To nCols - 1
                    Dim ca = job.Cols(a)
                    If ca Is Nothing Then Continue For
                    c(a) += ca(i).X * bx + ca(i).Y * by + ca(i).Z * bz
                Next
            Next
        Next

        Dim w = SolveBoxedLeastSquares(g, c, nCols)
        ' Exclusividad de par: un slider no puede ser a la vez Pos y Neg (el motor aplica UNO, elegido por
        ' el signo del valor). Si el ajuste activa los dos, se fija a cero el menor y se re-resuelve.
        Dim blocked(nCols - 1) As Boolean
        For guard = 0 To SliderPairs.Length
            Dim conflict = False
            For s = 0 To SliderPairs.Length - 1
                Dim idxPos = s * 2, idxNeg = s * 2 + 1
                If w(idxPos) > 0.0001 AndAlso w(idxNeg) > 0.0001 Then
                    If w(idxPos) >= w(idxNeg) Then blocked(idxNeg) = True Else blocked(idxPos) = True
                    conflict = True
                End If
            Next
            If Not conflict Then Exit For
            w = SolveBoxedLeastSquares(g, c, nCols, blocked)
        Next

        Dim quad As Double = 0, lin As Double = 0
        For a = 0 To nCols - 1
            lin += w(a) * c(a)
            For b2 = 0 To nCols - 1
                quad += w(a) * g(a, b2) * w(b2)
            Next
        Next
        Return (w, Math.Max(0, bNormSq - 2 * lin + quad))
    End Function

    ''' <summary>Candidatos de tipo para una familia NAMA: los que el .tri REALMENTE contiene
    ''' ("&lt;Prefix&gt;N"), más "Default" (valor 0) si existe, más "unset" (0xFFFFFFFF = sin canal), más el
    ''' valor actual. Enumerar sólo morphs existentes no pierde nada: el motor resuelve por NOMBRE y un
    ''' tipo cuyo morph no existe es indistinguible de "unset" (AddNam9Channel no-opea en el miss).</summary>
    Private Function NamaCandidates(catalog As SseNam9MorphMap.NamaTypeCatalog, familyIndex As Integer, current As UInteger) As List(Of UInteger)
        ' ⭐ La ley "¿este nombre es el tipo N de esta familia?" vive en UN solo lugar
        ' (SseNam9MorphMap.BuildTypeCatalog → TryParseFamilyMember), compartida con el combo del editor.
        ' Antes esto parseaba la cola con UInteger.TryParse por su cuenta y aceptaba de más: "NoseType03"
        ' entraba como 3 (pero el motor pide "NoseType3", que es otro morph o ninguno) y "NoseType0" entraba
        ' como candidato 0 DUPLICANDO el "Default" que se agrega aparte. El round-trip contra MorphForType
        ' descarta los dos casos por construcción.
        ' ⚠️ CAMBIO DE COMPORTAMIENTO declarado: los candidatos ahora salen ORDENADOS ascendente (el
        ' catálogo los ordena) y antes venían en orden de aparición en los .tri. Importa porque el descenso
        ' se queda con el PRIMERO que mejora y exige otro 0,1% a los siguientes: entre dos tipos casi
        ' equivalentes, el orden decide cuál gana, y eso se escribe en la NAMA. El orden nuevo es
        ' DETERMINISTA (no depende del layout del archivo ni del orden de las shapes al unir head parts),
        ' que es lo que se quiere; el efecto sobre el resultado NO está medido.
        Dim outList As New List(Of UInteger) From {SseNam9MorphMap.NamaUnset}
        If Not outList.Contains(current) Then outList.Add(current)
        Dim seen As New HashSet(Of UInteger)(outList)
        For Each n In catalog.Available(familyIndex)
            If seen.Add(n) Then outList.Add(n)
        Next
        If catalog.HasDefault(familyIndex) AndAlso seen.Add(0UI) Then outList.Add(0UI)
        Return outList
    End Function

    ''' <summary>Columna gateada de un morph: se aplica el canal a peso 1.0 sobre una base CERO, así que
    ''' el resultado ES el delta con el gate del motor ya aplicado. No se reimplementa el umbral.</summary>
    Private Function GatedColumn(triHead As TriHeadFile, morphName As String, zero As Vector3d()) As Vector3d()
        If triHead Is Nothing OrElse String.IsNullOrEmpty(morphName) Then Return Nothing
        Dim m = triHead.GetMorph(morphName)
        If m Is Nothing OrElse m.Vertices Is Nothing OrElse m.Vertices.Length = 0 Then Return Nothing
        Dim deltas = NpcMorphResolver.ConvertTriHeadMorphToMorphData(m)
        If deltas.Count = 0 Then Return Nothing
        Dim p As New MorphPlan()
        p.Channels.Add(New MorphChannel(morphName, 1.0F, deltas))
        Return MorphEngine.ApplyChannelsToVertexArray(zero, p)
    End Function

    ''' <summary>
    ''' Mínimos cuadrados con caja [0,1] por descenso coordinado proyectado sobre las ecuaciones normales.
    ''' G es PSD (G = AᵀA) ⇒ el descenso coordinado con proyección converge monótonamente. Se prefiere a un
    ''' active-set con eliminación gaussiana porque no necesita pivoteo ni maneja singularidades: una columna
    ''' nula (morph ausente) deja G(j,j)=0 y simplemente se salta.
    ''' La cota va DENTRO del solver: el motor NO clampea pesos fuera de [-1,1], los descarta por completo.
    ''' </summary>
    Private Function SolveBoxedLeastSquares(g As Double(,), c As Double(), n As Integer,
                                            Optional blocked As Boolean() = Nothing) As Double()
        Dim w(n - 1) As Double
        For it = 1 To 200
            Dim maxDelta As Double = 0
            For j = 0 To n - 1
                If blocked IsNot Nothing AndAlso blocked(j) Then
                    w(j) = 0 : Continue For
                End If
                Dim gjj = g(j, j)
                If gjj <= 0.0000000001 Then
                    w(j) = 0 : Continue For
                End If
                Dim num = c(j)
                For k = 0 To n - 1
                    If k <> j Then num -= g(j, k) * w(k)
                Next
                Dim nw = num / gjj
                If nw < 0 Then nw = 0
                If nw > 1 Then nw = 1
                Dim d = Math.Abs(nw - w(j))
                If d > maxDelta Then maxDelta = d
                w(j) = nw
            Next
            If maxDelta < 0.000001 Then Exit For
        Next
        Return w
    End Function

    Private Function ShapeNameOf(s As INiShape) As String
        Try
            Return If(s?.Name?.String, "(no name)")
        Catch ex As Exception
            Return "(no name)"
        End Try
    End Function

    ''' <summary>Empareja un shape base con el del FaceGen horneado. Por NOMBRE primero; si falla, por
    ''' conteo de vértices SÓLO si es inequívoco. ⛔ Nunca cae a FirstOrDefault: un emparejamiento
    ''' equivocado produciría un sculpt silenciosamente basura en vez de un error visible.</summary>
    Private Function MatchBakedShape(bakedNif As Nifcontent_Class_Manolo, srcShape As INiShape,
                                     nv As Integer, used As HashSet(Of INiShape)) As INiShape
        Dim want = ShapeNameOf(srcShape)
        For Each s In bakedNif.NifShapes
            If used.Contains(s) Then Continue For
            If String.Equals(ShapeNameOf(s), want, StringComparison.OrdinalIgnoreCase) Then Return s
        Next
        Dim cands As New List(Of INiShape)()
        For Each s In bakedNif.NifShapes
            If used.Contains(s) Then Continue For
            Try
                If ShapeGeometryFactory.[For](s, bakedNif).VertexCount = nv Then cands.Add(s)
            Catch ex As Exception
            End Try
        Next
        If cands.Count = 1 Then Return cands(0)
        Return Nothing
    End Function

    Private Function IsBeardLike(job As ShapeJob) As Boolean
        If job.Hdpt Is Nothing Then Return False
        Return job.Hdpt.PartType = 4
    End Function

    ''' <summary>0xFFFFFFFF es el centinela "unset" del CK (no un tipo 0 real) — se muestra como tal para
    ''' que no se confunda con "Default", que sí es un morph que el motor aplica.</summary>
    Private Function NamaText(v As UInteger) As String
        If v = SseNam9MorphMap.NamaUnset Then Return "(unset)"
        If v = 0UI Then Return "0 Default"
        Return v.ToString()
    End Function

    Private Function Trunc(s As String, n As Integer) As String
        If String.IsNullOrEmpty(s) Then Return ""
        If s.Length <= n Then Return s
        Return s.Substring(0, Math.Max(1, n - 1)) & "…"
    End Function

    Private Function WeightOf(npc As NPC_Data) As Single
        Dim n7 = npc.Nam7Raw
        If n7 IsNot Nothing AndAlso n7.Length >= 4 Then Return BitConverter.ToSingle(n7, 0)
        Return 100.0F
    End Function

End Module
