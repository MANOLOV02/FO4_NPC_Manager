Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

' ==========================================================================
' SSE — qué tipos NAMA (Nose/Brow/Eyes/Lip) puede ofrecer el editor para un NPC.
'
' DOS LEYES DISTINTAS, las dos data-driven (medido 2026-08-17, ver 22-morphs-race-mpav-lo-que-el-ck-ofrece):
'
'   • lo que el MOTOR ACEPTA  = los morphs "<Prefix><N>" que existen en los chargen .tri de las head
'     parts del NPC. ESTA es la lista: el aplicador resuelve POR NOMBRE (AddNamaTypePreset) y es CIEGO
'     a cualquier bitmask (skee64 ApplyChargenMorph_Hooked, SKEEHooks.cpp:730-749).
'   • lo que el CREATION KIT OFRECE = el bitmask RACE.MPAV por raza+género. Es MÁS CHICO, y sirve SÓLO
' para ANOTAR. Usarlo de filtro es un BUG: de 90 valores NAMA que su raza no declara, 75 EXISTEN
'     en las head parts de ese NPC y el juego se los aplica (p.ej. HighElfFemalePreset01 con NoseType7).
'
' Por qué la unión sobre TODAS las head parts y no "la cabeza": NAMA se resuelve por shape
' (NpcMorphResolver.ResolveMorphPlan corre por shape, y el motor instancia un BSFaceGenModel por parte),
' así que un LipType7 que viva sólo en el .tri de la BOCA es un tipo perfectamente vigente.
' ==========================================================================

''' <summary>Los tipos NAMA alcanzables por un NPC, más la anotación de cuáles ofrece el CK.</summary>
Public NotInheritable Class SseChargenTypeCatalog

    ''' <summary>False = no se pudo leer ningún .tri todavía. NO es lo mismo que "no hay tipos": sin esta
    ''' distinción la UI tendría que elegir entre mentir (mostrar una lista inventada) y bloquear.</summary>
    Public ReadOnly Property IsKnown As Boolean

    Private ReadOnly _catalog As SseNam9MorphMap.NamaTypeCatalog
    Private ReadOnly _offered As RACE_AvailableMorphs

    Private Sub New(catalog As SseNam9MorphMap.NamaTypeCatalog, offered As RACE_AvailableMorphs)
        _catalog = catalog
        _offered = offered
        IsKnown = catalog IsNot Nothing AndAlso catalog.IsKnown
    End Sub

    Public Shared Function Unknown() As SseChargenTypeCatalog
        Return New SseChargenTypeCatalog(SseNam9MorphMap.NamaTypeCatalog.Unknown(), Nothing)
    End Function

    ''' <summary>Los N que el motor puede aplicar para esta familia, ordenados y sin repetir.</summary>
    Public Function AvailableTypes(familyIndex As Integer) As IReadOnlyList(Of UInteger)
        If _catalog Is Nothing OrElse familyIndex < 0 OrElse familyIndex >= _catalog.Available.Length Then
            Return New List(Of UInteger)()
        End If
        Return _catalog.Available(familyIndex)
    End Function

    ''' <summary>¿Existe el morph "Default" (el que selecciona el valor 0) para esta familia?</summary>
    Public Function HasDefault(familyIndex As Integer) As Boolean
        Return _catalog IsNot Nothing AndAlso familyIndex >= 0 AndAlso
               familyIndex < _catalog.HasDefault.Length AndAlso _catalog.HasDefault(familyIndex)
    End Function

    ''' <summary>¿Se leyó el MPAV de ESTA FAMILIA para la raza+género del NPC?
    ''' <para>Es POR FAMILIA, no por raza: los bloques MPAI/MPAV son independientes y un RACE puede traer
    ''' Nose/Brow/Eyes y no Lip. Con un flag por raza, esa familia se rotularía entera "el CK no lo ofrece"
    ''' — afirmando sobre un dato que nunca se leyó, que es justo lo que <c>RACE_AvailableMorphs.Present</c>
    ''' existe para evitar.</para></summary>
    Public Function OfferedIsKnown(familyIndex As Integer) As Boolean
        Return _offered IsNot Nothing AndAlso familyIndex >= 0 AndAlso
               familyIndex < RACE_AvailableMorphs.FamilyCount AndAlso _offered.Present(familyIndex)
    End Function

    ''' <summary>¿El CK ofrece este tipo para la raza+género del NPC? Sólo tiene sentido consultarlo con
    ''' <see cref="OfferedIsKnown"/> en True. NUNCA se usa para filtrar la lista.</summary>
    Public Function IsOfferedByCk(familyIndex As Integer, value As UInteger) As Boolean
        Return _offered IsNot Nothing AndAlso _offered.Offers(familyIndex, value)
    End Function

    ''' <summary>Arma el catálogo para un NPC.</summary>
    ''' <param name="chargenTriPaths">Las rutas de chargen .tri (HDPT NAM0=2) de TODAS sus head parts, con
    ''' el vertex count de la shape correspondiente para el redirect HPH. Fuente:
    ''' <c>renderData.ShapeChargenTriPaths</c>, que NpcMeshCollector puebla para toda HeadPart y que NO
    ''' depende de los toggles de morphs del preview.</param>
    ''' <param name="effectiveRace">RACE ya parseado de la raza EFECTIVA del NPC (no la cruda del record —
    ''' ver 20-app-raza-efectiva). Nothing ⇒ sin anotación.</param>
    Public Shared Function Build(chargenTriPaths As IEnumerable(Of (Path As String, ShapeVerts As Integer)),
                                 effectiveRace As Canon.IRace,
                                 isFemale As Boolean) As SseChargenTypeCatalog
        If chargenTriPaths Is Nothing Then Return Unknown()

        Dim names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim anyLoaded = False
        Dim declaredPaths = 0
        For Each entry In chargenTriPaths
            If String.IsNullOrEmpty(entry.Path) Then Continue For
            declaredPaths += 1

            ' MISMA resolución que el render (NpcMorphResolver.LoadTriForShape): HPH-aware sobre el path del
            ' record, y los extended de RaceMenu llaveados sobre el chargen ORIGINAL — no el redirigido.
            Dim resolved = NpcMorphResolver.ResolveHphHeadPartTriPath(
                entry.Path, entry.ShapeVerts, NpcMorphResolver.HphTriSlot.Chargen, AddressOf TriHeadVertsOf)
            If AddNames(resolved, names) Then anyLoaded = True

            Dim catalog = NpcMorphResolver.SliderCatalog
            If catalog IsNot Nothing Then
                For Each extTriPath In catalog.GetExtendedMorphTris(entry.Path)
                    If AddNames(extTriPath, names) Then anyLoaded = True
                Next
            End If
        Next

        Dim offered As RACE_AvailableMorphs = Nothing
        If effectiveRace IsNot Nothing Then
            ' MPAI/MPAV son SKYRIM-only — Fallout 4 no los declara en RACE.
            offered = TryCast(effectiveRace, Canon.RaceSSE).ReadAvailableMorphs(isFemale)
        End If

        ' TRES estados, no dos. "Ninguna head part declara chargen .tri" es CONOCIMIENTO —ahí el motor
        ' tampoco aplicaría NAMA— y se devuelve como Known(vacío). Sólo "había rutas y ninguna cargó" es
        ' IGNORANCIA. Colapsarlos haría que el combo dijera "todavía no cargó" cuando la respuesta real
        ' es "esta cabeza no tiene tipos".
        If declaredPaths = 0 Then Return New SseChargenTypeCatalog(SseNam9MorphMap.KnownEmptyTypeCatalog(), offered)
        If Not anyLoaded Then Return Unknown()

        Return New SseChargenTypeCatalog(SseNam9MorphMap.BuildTypeCatalog(names), offered)
    End Function

    ''' <summary>Suma los nombres de un .tri al set. Devuelve True si el archivo se pudo leer — lo que
    ''' distingue "leí y no tenía tipos" de "no pude leer nada".</summary>
    Private Shared Function AddNames(rawPath As String, sink As HashSet(Of String)) As Boolean
        If String.IsNullOrEmpty(rawPath) Then Return False
        Dim head = NpcMorphResolver.TryLoadTriHead(MeshPathHelpers.NormalizeMeshKey(rawPath))
        If head Is Nothing OrElse head.Morphs Is Nothing Then Return False
        ' COPIA de los nombres, no se retiene la lista: la instancia viene del caché COMPARTIDO
        ' (PathLoadCache, Shared) y otros hilos pueden estar adentro de GetMorph → List.Find, que enumera.
        For Each m In head.Morphs
            If Not String.IsNullOrEmpty(m.Name) Then sink.Add(m.Name)
        Next
        Return True
    End Function

    ''' <summary>vertsOf para el redirect HPH, por el mismo caché que usa el render.</summary>
    Private Shared Function TriHeadVertsOf(rawPath As String) As Integer
        If String.IsNullOrEmpty(rawPath) Then Return -1
        Dim h = NpcMorphResolver.TryLoadTriHead(MeshPathHelpers.NormalizeMeshKey(rawPath))
        Return If(h Is Nothing, -1, CInt(h.NumVertices))
    End Function
End Class
