Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Un renglón del panel de detalle, ANTES de ser un <see cref="TreeNode"/>.
'''
''' <para>El árbol se arma como datos y recién <see cref="RecordDetailsTree.Volcar"/> lo convierte en
''' controles. Eso es lo que permite que el gate lo construya sin abrir el formulario: el panel de
''' detalle es la única vista que enumera el record ENTERO, y sin poder mirarlo desde afuera la única
''' forma de saber si falta un campo era mirarlo a ojo.</para></summary>
Friend NotInheritable Class DetalleNodo
    Public Property Texto As String

    ''' <summary>FormID del record que ESTE renglón nombra, o 0 si no nombra ninguno.
    ''' <para>Lo consume el doble click: el que resuelve a NPC_ o a LVLN se puede enfocar en el árbol
    ''' de la izquierda. Se anota en TODO renglón que nombre un record, no sólo en los de plantilla —
    ''' quién es navegable lo decide el TIPO del record al que apunta, y eso se sabe recién al
    ''' resolverlo. Anotar sólo los de plantilla obligaría a repetir acá esa decisión.</para></summary>
    Public Property Ref As UInteger

    Public ReadOnly Hijos As New List(Of DetalleNodo)

    Public Sub New(texto As String, Optional ref As UInteger = 0UI)
        Me.Texto = texto
        Me.Ref = ref
    End Sub
End Class

''' <summary>Arma el árbol del panel «Record Details»: todo lo que el NPC_ declara, con la herencia de
''' plantilla resuelta categoría por categoría.
'''
''' <para><b>Por qué es una clase y no un método de MainForm.</b> Es el único lugar de la app que
''' enumera el record COMPLETO, así que es el único que puede quedarse corto sin que se note. Acá
''' afuera lo puede construir un arnés contra el corpus real y comparar lo que sale contra las firmas
''' de subrecord que los archivos TRAEN — ver <see cref="Cobertura"/> y Tools/DetalleDelRecordGate.</para>
'''
''' <para><b>La fuente de cada sección.</b> Las categorías que el motor hereda por TPLT/TPTA se leen
''' del actor TERMINAL de la cadena (<see cref="ResolverFuenteDeSeccion"/>) y el encabezado dice de
''' quién salieron. Las que no son categoría de plantilla —bounds, destruible, object templates,
''' sonidos, actor values, las listas de override— se leen del record PROPIO, porque es de ahí de
''' donde las lee el motor.</para>
'''
''' <para>⛔ UN SOLO HILO. <see cref="Construir"/> escribe estado de instancia (las raíces en curso y la
''' caché de LVLN), así que no es reentrante. Es el mismo contrato que tenía cuando esto vivía en
''' MainForm: <c>PopulateRecordDetails</c> hace <c>Invoke</c> al hilo de UI antes de llamar, y los otros
''' dos consumidores (el sembrado de altura del editor de cuerpo y el <c>DescribeFormID</c> de
''' EditFace_Form) son manejadores de la UI. El día que alguien lo llame desde <c>Task.Run</c>, hay que
''' darle una instancia propia.</para></summary>
Friend NotInheritable Class RecordDetailsTree

    Private ReadOnly _pluginManager As PluginManager
    Private ReadOnly _ctx As NpcRenderContext

    ''' <summary>Raíces del árbol que se está construyendo. Vive por llamada a <see cref="Construir"/>.</summary>
    Private _raices As List(Of DetalleNodo)

    ''' <summary>LVLN parseadas mientras se arma UN árbol. El panel resuelve trece categorías de
    ''' plantilla por separado y un salto de cadena por la misma leveled list la re-parsearía una vez
    ''' por categoría. Se vacía al entrar a <see cref="Construir"/>, así que no puede quedar vieja
    ''' contra una recarga de plugins.</summary>
    Private ReadOnly _detailsLvlnCache As New Dictionary(Of UInteger, Canon.ILvln)

    Public Sub New(pluginManager As PluginManager, ctx As NpcRenderContext)
        _pluginManager = pluginManager
        _ctx = ctx
    End Sub

    '==============================================================================================
    ' Volcado a controles
    '==============================================================================================

    ''' <summary>Pasa el árbol de datos al TreeView y lo deja ABIERTO ENTERO.
    '''
    ''' <para>⛔ La expansión es del volcado y no de cada sección: antes cada sección decidía por su
    ''' cuenta si abrirse (<c>headerNode.Expand()</c> y seis más), así que el panel abría seis ramas y
    ''' dejaba el resto cerrado sin ninguna regla que dijera por qué esas seis. Lo que el usuario
    ''' quiere ver es el record, no seis ramas de él.</para>
    '''
    ''' <para>⛔ Y SE EXPANDE DESPEGADO DEL CONTROL, que no es un detalle: <c>TreeView.ExpandAll</c>
    ''' recorre los nodos YA COLGADOS y le manda un <c>TVM_EXPAND</c> al control nativo por cada uno.
    ''' MEDIDO sobre el corpus del usuario (Tools\DetalleDelRecordGate, 400 NPC por juego): el peor
    ''' árbol de Skyrim tiene 86.795 nodos —un atuendo por niveles que se abre en cientos de ARMO con
    ''' sus ARMA—, y expandirlo así costaba <b>23,7 s</b> por selección de NPC. <c>TreeNode.Expand</c>
    ''' sobre un nodo que TODAVÍA no cuelga de ningún control no manda mensaje: sólo prende su bandera,
    ''' y el control la realiza de una sola vez al colgarlo. Por eso se arma el subárbol entero
    ''' expandido y recién al final se cuelgan las raíces.</para></summary>
    Public Shared Sub Volcar(destino As TreeView, nodos As IReadOnlyList(Of DetalleNodo))
        If destino Is Nothing Then Return
        ' Se materializa ANTES del BeginUpdate: acá no se toca el control todavía.
        Dim raices As New List(Of TreeNode)
        If nodos IsNot Nothing Then
            For Each n In nodos
                raices.Add(Materializar(n))
            Next
        End If
        destino.BeginUpdate()
        Try
            destino.Nodes.Clear()
            destino.Nodes.AddRange(raices.ToArray())
        Finally
            destino.EndUpdate()
        End Try
        ' ⛔ DESPUES del EndUpdate y con handle: abrir todo deja la vista en la ULTIMA rama abierta, o
        ' sea que el panel arrancaba mostrando el final del record en vez del encabezado. TopNode sin
        ' handle no tiene dónde escribir.
        If destino.IsHandleCreated AndAlso destino.Nodes.Count > 0 Then destino.TopNode = destino.Nodes(0)
    End Sub

    Private Shared Function Materializar(n As DetalleNodo) As TreeNode
        Dim tn As New TreeNode(n.Texto)
        If n.Ref <> 0UI Then tn.Tag = n.Ref
        For Each h In n.Hijos
            tn.Nodes.Add(Materializar(h))
        Next
        ' Ver el comentario de Volcar: acá `tn` todavía no cuelga de ningún TreeView, así que esto es
        ' prender una bandera y no un mensaje al control nativo.
        If tn.Nodes.Count > 0 Then tn.Expand()
        Return tn
    End Function

    '==============================================================================================
    ' Construcción
    '==============================================================================================

    Private Function Agregar(padre As DetalleNodo, texto As String, Optional ref As UInteger = 0UI) As DetalleNodo
        Dim n As New DetalleNodo(texto, ref)
        If padre Is Nothing Then
            _raices.Add(n)
        Else
            padre.Hijos.Add(n)
        End If
        Return n
    End Function

    ''' <summary>El árbol completo del NPC. Nunca Nothing; con <paramref name="npc"/> Nothing devuelve
    ''' la lista vacía.</summary>
    Public Function Construir(npc As NPC_Data) As List(Of DetalleNodo)
        _raices = New List(Of DetalleNodo)()
        _detailsLvlnCache.Clear()
        If npc Is Nothing Then Return _raices

        ' El NPC_ no es UN esquema para los dos juegos. FO4 y Skyrim difieren en el layout de ACBS, en
        ' el tamaño del cuerpo (MWGT delgado/musculoso/gordo contra un solo flotante NAM7), en el bloque
        ' de stats (DNAM = 8 bytes de stats calculadas contra 52 de habilidades de jugador), en los datos
        ' de cara (MSDK/TETI/FMRI contra NAM9/NAMA/TINI) y en la resolución de plantilla (FO4 cachea el
        ' actor resuelto por categoría en TPTA; Skyrim no tiene TPTA y camina la cadena TPLT). Se decide
        ' por el juego del PROPIO record, no por el de la sesión, así cada record se dibuja con el
        ' esquema con el que se parseó.
        Dim isSse As Boolean = (npc.Game = Config_App.Game_Enum.Skyrim)

        SeccionEncabezado(npc)
        SeccionObjectBounds(npc)
        SeccionPlantilla(npc, isSse)
        SeccionConfiguracion(npc)
        SeccionTraits(npc, isSse)
        SeccionStats(npc, isSse)
        SeccionActorValues(npc)
        SeccionFacciones(npc)
        SeccionAIData(npc)
        SeccionPaquetes(npc)
        SeccionListasDeOverride(npc)
        SeccionEfectos(npc)
        SeccionKeywords(npc)
        SeccionPerks(npc)
        SeccionInventario(npc)
        SeccionApariencia(npc, isSse)
        SeccionObjectTemplates(npc)
        SeccionAtaques(npc, isSse)
        SeccionDestruible(npc, isSse)
        SeccionSonidos(npc, isSse)
        SeccionScripts(npc)
        SeccionOtros(npc, isSse)

        Return _raices
    End Function

    '----------------------------------------------------------------------------------------------
    ' EDID / FULL / SHRT
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionEncabezado(npc As NPC_Data)
        Dim headerNode = Agregar(Nothing, $"NPC_ {npc.EditorID}  [{npc.FormID:X8}]  {npc.PluginName}", npc.FormID)
        Agregar(headerNode, $"Full Name: {If(npc.Record.Name <> "", npc.Record.Name, "(none)")}")
        ' ⛔ «PRESENTE Y VACÍO» NO ES «AUSENTE». Antes el renglón pedía además `ShortName <> ""`, así que
        ' un record que TRAE el SHRT vacío se veía igual que uno que no lo trae. MEDIDO: en el corpus de
        ' Fallout 4 del usuario los 24 records testigo de SHRT lo traen vacío — o sea que el subrecord
        ' existía y el panel no lo mostraba NUNCA, y por eso el gate no podía verlo.
        If npc.Record.ShortNamePresente Then Agregar(headerNode, $"Short Name: {If(npc.Record.ShortName <> "", npc.Record.ShortName, "(empty)")}")
        Agregar(headerNode, $"Editor ID: {npc.EditorID}")
        Agregar(headerNode, $"Form ID: {npc.FormID:X8}")
        Agregar(headerNode, $"Plugin: {npc.PluginName}")
        Agregar(headerNode, $"Gender: {If(npc.Record.ConfigurationFlagsFemale, "Female", "Male")}")
    End Sub

    '----------------------------------------------------------------------------------------------
    ' OBND
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionObjectBounds(npc As NPC_Data)
        If Not npc.Record.MinXPresente AndAlso Not npc.Record.MaxXPresente Then Return
        Dim n = Agregar(Nothing, "Object Bounds (OBND)")
        Agregar(n, $"Min: X={npc.Record.MinX}  Y={npc.Record.MinY}  Z={npc.Record.MinZ}")
        Agregar(n, $"Max: X={npc.Record.MaxX}  Y={npc.Record.MaxY}  Z={npc.Record.MaxZ}")
    End Sub

    '----------------------------------------------------------------------------------------------
    ' TPLT / TPTA / LTPT / LTPC + los bits de Template Flags
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionPlantilla(npc As NPC_Data, isSse As Boolean)
        If npc.Record.Plantilla() = 0UI AndAlso npc.Record.ActoresDePlantilla().Count = 0 Then Return

        Dim tplNode = Agregar(Nothing, $"Template Configuration  (flags: {npc.Record.ConfigurationTemplateFlags:X4})")
        If npc.Record.Plantilla() <> 0UI Then
            Agregar(tplNode, $"Base Template (TPLT): {DescribirFormID(npc.Record.Plantilla())}", npc.Record.Plantilla())
        End If
        If Not isSse Then
            ' TPTA y el par de plantilla legendaria son subrecords sólo de Fallout.
            For Each cat As NPC_TemplateCategory In Canon.CanonInterpretacion.CategoriasDePlantilla
                Dim actor = npc.Record.ActorDePlantilla(cat)
                If actor = 0UI Then Continue For
                Agregar(tplNode, $"TPTA[{cat}] ({NpcManagerFormat.GetTemplateCategoryLabel(cat)}): {DescribirFormID(actor)}", actor)
            Next
            Dim npcFo4 = TryCast(npc.Record, Canon.NpcFO4)
            If npcFo4 IsNot Nothing Then
                If npcFo4.LegendaryTemplatePresente Then Agregar(tplNode, $"Legendary Template (LTPT): {DescribirFormID(npcFo4.LegendaryTemplate)}", npcFo4.LegendaryTemplate)
                If npcFo4.LegendaryChancePresente Then Agregar(tplNode, $"Legendary Chance (LTPC): {DescribirFormID(npcFo4.LegendaryChance)}", npcFo4.LegendaryChance)
            End If
        End If
        ' Los 13 bits de bandera de plantilla son idénticos en los dos motores.
        Dim flagList As New List(Of String)
        For Each cat As NPC_TemplateCategory In Canon.CanonInterpretacion.CategoriasDePlantilla
            If NpcTemplateHelpers.HasTemplateFlag(npc.Record.ConfigurationTemplateFlags, cat) Then flagList.Add(NpcManagerFormat.GetTemplateCategoryLabel(cat))
        Next
        If flagList.Count > 0 Then Agregar(tplNode, $"Active flags: {String.Join(", ", flagList)}")
    End Sub

    '----------------------------------------------------------------------------------------------
    ' ACBS
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionConfiguracion(npc As NPC_Data)
        ' Los bytes de ACBS viven SIEMPRE en ESTE record (subrecord requerido) — sólo los VALORES de
        ' las categorías de abajo se resuelven por la cadena de plantilla, así que esta sección nunca
        ' es "heredada".
        Dim cfgNode = Agregar(Nothing, "Configuration (ACBS)")
        Agregar(cfgNode, $"Flags: {NpcManagerFormat.DescribirBanderas(npc.Record.Node, "ACBS", "Flags")}")
        Agregar(cfgNode, NpcManagerFormat.FormatAcbsLevel(npc.Record))
        Dim cfgSse = TryCast(npc.Record, Canon.NpcSSE)
        Dim cfgFo4 = TryCast(npc.Record, Canon.NpcFO4)
        If cfgSse IsNot Nothing Then
            Agregar(cfgNode, $"Offsets: Magicka={cfgSse.ConfigurationMagickaOffset}  Stamina={cfgSse.ConfigurationStaminaOffset}  Health={cfgSse.ConfigurationHealthOffset}")
            Agregar(cfgNode, $"Speed Multiplier: {cfgSse.ConfigurationSpeedMultiplier}%")
            ' Skyrim declara el campo pero lo llama «Disposition Base (unused)»: se muestra con el
            ' nombre que le pone el formato, no con el de Fallout.
            If cfgSse.ConfigurationDispositionBaseUnusedPresente Then Agregar(cfgNode, $"Disposition Base (unused): {cfgSse.ConfigurationDispositionBaseUnused}")
        ElseIf cfgFo4 IsNot Nothing Then
            Agregar(cfgNode, $"XP Value Offset: {cfgFo4.ConfigurationXPValueOffset}")
            Agregar(cfgNode, $"Disposition Base: {npc.Record.BaseDeDisposicion()}")
        End If
        Agregar(cfgNode, $"Bleedout Override: {npc.Record.ConfigurationBleedoutOverride}")
    End Sub

    '----------------------------------------------------------------------------------------------
    ' RNAM / WNAM / VTCK / NAM6 / NAM4 / NAM7 / MWGT / MRSV
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionTraits(npc As NPC_Data, isSse As Boolean)
        Dim traitsNpc = ResolverFuenteDeSeccion(npc, NPC_TemplateCategory.Traits)
        Dim traitsNode = Agregar(Nothing, EtiquetaDeSeccion(npc, traitsNpc, "Traits"), FormIDDeFuente(npc, traitsNpc))
        Agregar(traitsNode, $"Race: {DescribirFormID(traitsNpc.Record.Race)}", traitsNpc.Record.Race)
        ExpandirRaza(traitsNode, traitsNpc.Record.Race, traitsNpc.Record.ConfigurationFlagsFemale)
        If traitsNpc.Record.Skin <> 0UI Then Agregar(traitsNode, $"Skin Armor: {DescribirFormID(traitsNpc.Record.Skin)}", traitsNpc.Record.Skin)
        If traitsNpc.Record.VoicePresente Then Agregar(traitsNode, $"Voice: {DescribirFormID(traitsNpc.Record.Voice)}", traitsNpc.Record.Voice)
        If isSse Then
            ' El tamaño del cuerpo en Skyrim = NAM6 Height + NAM7 Weight, dos flotantes sueltos. No
            ' tiene ni el triple MWGT delgado/musculoso/gordo ni las regiones de morph MRSV. NAM7 se
            ' parsea como payload opaco porque en FO4 la misma firma es un campo sin uso — acá lleva
            ' el peso.
            If traitsNpc.Record.TieneAltura() Then Agregar(traitsNode, $"Height: {traitsNpc.Record.Altura():F2}")
            If traitsNpc.Record.TienePesoDeSkyrim() Then Agregar(traitsNode, $"Weight: {traitsNpc.Record.PesoDeSkyrim():F2}")
        Else
            If traitsNpc.Record.TieneAltura() OrElse traitsNpc.Record.TieneAlturaMaxima() Then
                ' Cada mitad se reporta sólo si su subrecord está de verdad: HeightMax vale 0.0 por
                ' defecto, así que imprimirlo siempre mostraba "max=0.00" para un record que
                ' simplemente no trae NAM4 — indistinguible de uno que guarda cero.
                Dim hMin = If(traitsNpc.Record.TieneAltura(), $"{traitsNpc.Record.Altura():F2}", "(absent)")
                Dim hMax = If(traitsNpc.Record.TieneAlturaMaxima(), $"{traitsNpc.Record.AlturaMaxima():F2}", "(absent)")
                Agregar(traitsNode, $"Height: min={hMin}  max={hMax}")
            End If
            Dim fmtMwgt = Function(v As Single?) If(v.HasValue, v.Value.ToString("F2"), "Default")
            Agregar(traitsNode, $"Weight: Thin={fmtMwgt(traitsNpc.Record.PesoDelCuerpo(0))}  Muscular={fmtMwgt(traitsNpc.Record.PesoDelCuerpo(1))}  Fat={fmtMwgt(traitsNpc.Record.PesoDelCuerpo(2))}")
            Dim regiones = traitsNpc.Record.ValoresDeRegionCorporal()
            If regiones.Count > 0 Then
                ' Los nombres de las cinco regiones salen del ESQUEMA (MRSV\Body Morph Region Values),
                ' que es donde el formato los declara; el índice suelto no decía cuál era cuál.
                Dim morphNode = Agregar(traitsNode, $"Body Morph Regions ({regiones.Count} values)")
                VolcarHojas(morphNode, BloqueDeSubrecord(traitsNpc.Record.Node, "MRSV"))
            End If
        End If
    End Sub

    '----------------------------------------------------------------------------------------------
    ' CNAM / DNAM
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionStats(npc As NPC_Data, isSse As Boolean)
        Dim statsNpc = ResolverFuenteDeSeccion(npc, NPC_TemplateCategory.Stats)
        Dim statsNode = Agregar(Nothing, EtiquetaDeSeccion(npc, statsNpc, "Stats"), FormIDDeFuente(npc, statsNpc))
        If statsNpc.Record.ClassPresente Then Agregar(statsNode, $"Class: {DescribirFormID(statsNpc.Record.[Class])}", statsNpc.Record.[Class])
        If isSse Then
            ' DNAM = 52 bytes de Player Skills. Nada cuando el payload era demasiado corto para modelarlo.
            Dim skillsSse = TryCast(statsNpc.Record, Canon.NpcSSE)
            If skillsSse IsNot Nothing AndAlso skillsSse.PlayerSkillsHealthPresente Then
                Agregar(statsNode, $"Health {skillsSse.PlayerSkillsHealth}   Magicka {skillsSse.PlayerSkillsMagicka}   Stamina {skillsSse.PlayerSkillsStamina}")
                If skillsSse.PlayerSkillsFarAwayModelDistancePresente Then Agregar(statsNode, $"Far Away Model Distance: {skillsSse.PlayerSkillsFarAwayModelDistance:F2}")
                If skillsSse.PlayerSkillsGearedUpWeaponsPresente Then Agregar(statsNode, $"Geared Up Weapons: {skillsSse.PlayerSkillsGearedUpWeapons}")
                Dim skillsNode = Agregar(statsNode, $"Player Skills ({skillsSse.SkillValues.Count})")
                For i = 0 To skillsSse.SkillValues.Count - 1
                    Dim off = If(i < skillsSse.SkillOffsets.Count, skillsSse.SkillOffsets(i).Skill, CByte(0))
                    ' El nombre de la skill sale del esquema, que es donde vive el orden del arreglo.
                    Dim nombre = skillsSse.SkillValues(i).Node?.Name
                    Agregar(skillsNode, $"{If(String.IsNullOrEmpty(nombre), $"[{i}]", nombre)}: {skillsSse.SkillValues(i).Skill}  (offset +{off})")
                Next
            End If
        Else
            ' DNAM = 8 bytes de Calculated Stats. Ojo: acá la distancia del modelo lejano es u16, en SSE
            ' es flotante.
            Dim calcFo4 = TryCast(statsNpc.Record, Canon.NpcFO4)
            If calcFo4 IsNot Nothing AndAlso calcFo4.CalculatedHealthPresente Then
                Agregar(statsNode, $"Calculated Health: {calcFo4.CalculatedHealth}")
                Agregar(statsNode, $"Calculated Action Points: {calcFo4.CalculatedActionPoints}")
                If calcFo4.FarAwayModelDistancePresente Then Agregar(statsNode, $"Far Away Model Distance: {calcFo4.FarAwayModelDistance}")
                If calcFo4.GearedUpWeaponsPresente Then Agregar(statsNode, $"Geared Up Weapons: {calcFo4.GearedUpWeapons}")
            End If
        End If
    End Sub

    '----------------------------------------------------------------------------------------------
    ' PRPS — los actor values que el record fija de arranque (sólo Fallout 4)
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionActorValues(npc As NPC_Data)
        Dim fo4 = TryCast(npc.Record, Canon.NpcFO4)
        If fo4 Is Nothing OrElse fo4.Properties2.Count = 0 Then Return
        Dim n = Agregar(Nothing, $"Actor Values (PRPS, {fo4.Properties2.Count})")
        For Each p In fo4.Properties2
            Agregar(n, $"{DescribirFormID(p.PropertyActorValue)} = {p.PropertyValue:F4}", p.PropertyActorValue)
        Next
    End Sub

    '----------------------------------------------------------------------------------------------
    ' SNAM
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionFacciones(npc As NPC_Data)
        Dim facNpc = ResolverFuenteDeSeccion(npc, NPC_TemplateCategory.Factions)
        Dim facciones = facNpc.Record.Factions
        If facciones.Count = 0 Then Return
        Dim facNode = Agregar(Nothing, EtiquetaDeSeccion(npc, facNpc, $"Factions ({facciones.Count})"), FormIDDeFuente(npc, facNpc))
        For Each fac In facciones
            Agregar(facNode, $"{DescribirFormID(fac.Faction)}  rank {fac.FactionRank}", fac.Faction)
        Next
    End Sub

    '----------------------------------------------------------------------------------------------
    ' AIDT
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionAIData(npc As NPC_Data)
        Dim aiNpc = ResolverFuenteDeSeccion(npc, NPC_TemplateCategory.AIData)
        If Not aiNpc.Record.AIDataAggressionPresente Then Return
        Dim aiNode = Agregar(Nothing, EtiquetaDeSeccion(npc, aiNpc, "AI Data"), FormIDDeFuente(npc, aiNpc))
        Agregar(aiNode, $"Aggression: {aiNpc.Record.AIDataAggressionNombre}")
        Agregar(aiNode, $"Confidence: {aiNpc.Record.AIDataConfidenceNombre}")
        Agregar(aiNode, $"Morality: {aiNpc.Record.AIDataMoralityNombre}")
        Agregar(aiNode, $"Mood: {aiNpc.Record.AIDataMoodNombre}")
        Agregar(aiNode, $"Assistance: {aiNpc.Record.AIDataAssistanceNombre}")
        Agregar(aiNode, $"Energy Level: {aiNpc.Record.AIDataEnergyLevel}")
        Agregar(aiNode, $"Aggro Radius Behavior: {If(aiNpc.Record.AIDataAggroRadiusBehavior, "Yes", "No")}")
        Agregar(aiNode, $"Radius: warn={aiNpc.Record.AggroWarn}  warn/attack={aiNpc.Record.AggroWarnAttack}  attack={aiNpc.Record.AggroAttack}")
        Dim aiFo4 = TryCast(aiNpc.Record, Canon.NpcFO4)
        If aiFo4 IsNot Nothing Then
            If aiFo4.AIDataNoSlowApproachPresente Then Agregar(aiNode, $"No Slow Approach: {If(aiFo4.AIDataNoSlowApproach, "Yes", "No")}")
            If aiFo4.AggroUnknownPresente Then Agregar(aiNode, $"Aggro (unknown byte): {aiFo4.AggroUnknown}")
            If aiFo4.AIDataUnknownPresente Then Agregar(aiNode, $"AIDT trailing bytes: {EnHex(aiFo4.AIDataUnknown)}")
        End If
    End Sub

    '----------------------------------------------------------------------------------------------
    ' PKID + DPLT
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionPaquetes(npc As NPC_Data)
        Dim pkgNpc = ResolverFuenteDeSeccion(npc, NPC_TemplateCategory.AIPackages)
        Dim dpltNpc = ResolverFuenteDeSeccion(npc, NPC_TemplateCategory.DefaultPackageList)
        If pkgNpc.Record.PaquetesDeIA().Count = 0 AndAlso Not dpltNpc.Record.DefaultPackageListPresente Then Return
        Dim pkgNode = Agregar(Nothing, EtiquetaDeSeccion(npc, pkgNpc, $"AI Packages ({pkgNpc.Record.PaquetesDeIA().Count})"), FormIDDeFuente(npc, pkgNpc))
        For Each pkgID In pkgNpc.Record.PaquetesDeIA()
            Agregar(pkgNode, DescribirFormID(pkgID), pkgID)
        Next
        If dpltNpc.Record.DefaultPackageListPresente Then Agregar(pkgNode, $"Default Package List (DPLT): {DescribirFormID(dpltNpc.Record.DefaultPackageList)}", dpltNpc.Record.DefaultPackageList)
    End Sub

    '----------------------------------------------------------------------------------------------
    ' SPOR / OCOR / GWOR / ECOR / FCPL / RCLR
    '----------------------------------------------------------------------------------------------
    ''' <summary>Las listas de override de paquete. ⛔ NO son categoría de plantilla: ninguna de las
    ''' trece las cubre, así que el motor las lee del record PROPIO aunque el NPC herede AI Packages.
    ''' Por eso van en su propia sección y no colgadas de «AI Packages», que sí puede venir de otro
    ''' actor y diría una fuente que no es la de estos campos.</summary>
    Private Sub SeccionListasDeOverride(npc As NPC_Data)
        Dim filas As New List(Of (Etiqueta As String, Fid As UInteger))
        If npc.Record.SpectatorOverridePackageListPresente Then filas.Add(("Spectator (SPOR)", npc.Record.SpectatorOverridePackageList))
        If npc.Record.ObserveDeadBodyOverridePackageListPresente Then filas.Add(("Observe Dead Body (OCOR)", npc.Record.ObserveDeadBodyOverridePackageList))
        If npc.Record.GuardWarnOverridePackageListPresente Then filas.Add(("Guard Warn (GWOR)", npc.Record.GuardWarnOverridePackageList))
        If npc.Record.CombatOverridePackageListPresente Then filas.Add(("Combat (ECOR)", npc.Record.CombatOverridePackageList))
        Dim fo4 = TryCast(npc.Record, Canon.NpcFO4)
        If fo4 IsNot Nothing Then
            If fo4.FollowerCommandPackageListPresente Then filas.Add(("Follower Command (FCPL)", fo4.FollowerCommandPackageList))
            If fo4.FollowerElevatorPackageListPresente Then filas.Add(("Follower Elevator (RCLR)", fo4.FollowerElevatorPackageList))
        End If
        If filas.Count = 0 Then Return
        Dim n = Agregar(Nothing, $"Override Package Lists ({filas.Count})")
        For Each f In filas
            Agregar(n, $"{f.Etiqueta}: {DescribirFormID(f.Fid)}", f.Fid)
        Next
    End Sub

    '----------------------------------------------------------------------------------------------
    ' SPLO
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionEfectos(npc As NPC_Data)
        Dim spellNpc = ResolverFuenteDeSeccion(npc, NPC_TemplateCategory.SpellList)
        If spellNpc.Record.EfectosDeActor().Count = 0 Then Return
        Dim spellNode = Agregar(Nothing, EtiquetaDeSeccion(npc, spellNpc, $"Actor Effects ({spellNpc.Record.EfectosDeActor().Count})"), FormIDDeFuente(npc, spellNpc))
        For Each spellID In spellNpc.Record.EfectosDeActor()
            Agregar(spellNode, DescribirFormID(spellID), spellID)
        Next
    End Sub

    '----------------------------------------------------------------------------------------------
    ' KWDA
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionKeywords(npc As NPC_Data)
        Dim kwNpc = ResolverFuenteDeSeccion(npc, NPC_TemplateCategory.Keywords)
        If kwNpc.Record.PalabrasClave().Count = 0 Then Return
        Dim kwNode = Agregar(Nothing, EtiquetaDeSeccion(npc, kwNpc, $"Keywords ({kwNpc.Record.PalabrasClave().Count})"), FormIDDeFuente(npc, kwNpc))
        For Each kwID In kwNpc.Record.PalabrasClave()
            Agregar(kwNode, DescribirFormID(kwID), kwID)
        Next
    End Sub

    '----------------------------------------------------------------------------------------------
    ' PRKR — sin categoría de plantilla: siempre del record propio
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionPerks(npc As NPC_Data)
        Dim ventajas = npc.Record.Perks
        If ventajas.Count = 0 Then Return
        Dim perkNode = Agregar(Nothing, $"Perks ({ventajas.Count})")
        For Each perk In ventajas
            Agregar(perkNode, $"{DescribirFormID(perk.Perk)}  rank {perk.PerkRank}", perk.Perk)
        Next
    End Sub

    '----------------------------------------------------------------------------------------------
    ' DOFT / SOFT / CNTO / COED
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionInventario(npc As NPC_Data)
        Dim invNpc = ResolverFuenteDeSeccion(npc, NPC_TemplateCategory.Inventory)
        Dim invNode = Agregar(Nothing, EtiquetaDeSeccion(npc, invNpc, "Inventory"), FormIDDeFuente(npc, invNpc))
        If invNpc.Record.DefaultOutfit <> 0UI Then
            Dim outfitNode = Agregar(invNode, $"Default Outfit: {DescribirFormID(invNpc.Record.DefaultOutfit)}", invNpc.Record.DefaultOutfit)
            ExpandirAtuendo(outfitNode, invNpc.Record.DefaultOutfit)
        Else
            Agregar(invNode, "Default Outfit: (none)")
        End If
        If invNpc.Record.SleepingOutfit <> 0UI Then
            Dim sleepNode = Agregar(invNode, $"Sleep Outfit: {DescribirFormID(invNpc.Record.SleepingOutfit)}", invNpc.Record.SleepingOutfit)
            ExpandirAtuendo(sleepNode, invNpc.Record.SleepingOutfit)
        End If
        ' Los CNTO se listan por nombre nada más — a propósito NO se expanden a su grafo ARMO/ARMA como
        ' el atuendo, así un mercader de 40 ítems no lo paga en cada selección.
        Dim inventario = invNpc.Record.Items
        If inventario.Count > 0 Then
            Dim itemsNode = Agregar(invNode, $"Items ({inventario.Count})")
            For Each item In inventario
                Dim fila = Agregar(itemsNode, $"{DescribirFormID(item.Item)}  x{item.ItemCount}", item.Item)
                ' COED: los extras del ítem —dueño y condición— que hasta ahora no se veían.
                If item.ExtraDataOwnerPresente AndAlso item.ExtraDataOwner <> 0UI Then Agregar(fila, $"Owner: {DescribirFormID(item.ExtraDataOwner)}", item.ExtraDataOwner)
                If item.GlobalVariableRequiredRankGlobalVariablePresente AndAlso item.GlobalVariableRequiredRankGlobalVariable <> 0UI Then
                    Agregar(fila, $"Required rank global: {DescribirFormID(item.GlobalVariableRequiredRankGlobalVariable)}", item.GlobalVariableRequiredRankGlobalVariable)
                ElseIf item.GlobalVariableRequiredRankRequiredRankPresente Then
                    Agregar(fila, $"Required rank: {item.GlobalVariableRequiredRankRequiredRank}")
                End If
                If item.ExtraDataItemConditionPresente Then Agregar(fila, $"Condition: {item.ExtraDataItemCondition:F2}")
            Next
        End If
    End Sub

    '----------------------------------------------------------------------------------------------
    ' FTST / HCLF / BCLF / QNAM / PNAM / MSDK+MSDV / FMRI+FMRS / FMIN / TETI+TEND / NAM9 / NAMA / TIN*
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionApariencia(npc As NPC_Data, isSse As Boolean)
        Dim modelNpc = ResolverFuenteDeSeccion(npc, NPC_TemplateCategory.ModelAnimation)
        Dim modelNode = Agregar(Nothing, EtiquetaDeSeccion(npc, modelNpc, "Appearance"), FormIDDeFuente(npc, modelNpc))
        If modelNpc.Record.HeadTexture <> 0UI Then Agregar(modelNode, $"Head Texture: {DescribirFormID(modelNpc.Record.HeadTexture)}", modelNpc.Record.HeadTexture)
        If modelNpc.Record.HairColor <> 0UI Then Agregar(modelNode, $"Hair Color: {DescribirFormID(modelNpc.Record.HairColor)}", modelNpc.Record.HairColor)
        ' BCLF (color de barba) es un subrecord sólo de Fallout — Skyrim tiñe la barba desde HCLF.
        If Not isSse AndAlso modelNpc.Record.ColorDeBarba() <> 0UI Then Agregar(modelNode, $"Facial Hair Color: {DescribirFormID(modelNpc.Record.ColorDeBarba())}", modelNpc.Record.ColorDeBarba())
        ' QNAM existe en los dos motores (RGB(A) flotante).
        If modelNpc.Record.TextureLightingRedPresente Then
            Dim qnam = $"Texture Lighting: R={modelNpc.Record.ColorDeIluminacionDeTextura().R} G={modelNpc.Record.ColorDeIluminacionDeTextura().G} B={modelNpc.Record.ColorDeIluminacionDeTextura().B}"
            ' El cuarto flotante sólo lo declara Fallout 4.
            Dim modelFo4Qnam = TryCast(modelNpc.Record, Canon.NpcFO4)
            If modelFo4Qnam IsNot Nothing AndAlso modelFo4Qnam.TextureLightingAlphaPresente Then qnam &= $" A={modelFo4Qnam.TextureLightingAlpha:F4}"
            Agregar(modelNode, qnam)
        End If

        ' Head Parts (PNAM — los dos motores)
        If modelNpc.Record.PartesDeCabeza().Count > 0 Then
            Dim hpNode = Agregar(modelNode, $"Head Parts ({modelNpc.Record.PartesDeCabeza().Count})")
            For Each hpFormID In modelNpc.Record.PartesDeCabeza()
                Dim hpRec = _pluginManager.GetRecord(hpFormID)
                If hpRec IsNot Nothing Then
                    Dim hdpt = _ctx.ParseHdptCached(hpRec)
                    Dim typeName = NpcManagerFormat.GetHeadPartTypeName(hdpt.TipoDeParte())
                    Dim hpChildNode = Agregar(hpNode, $"[{typeName}] {hdpt.EditorID}  [{hpFormID:X8}]", hpFormID)
                    If hdpt.ModelFileName <> "" Then Agregar(hpChildNode, $"Mesh: {hdpt.ModelFileName}")
                    If hdpt.TextureSet <> 0UI Then Agregar(hpChildNode, $"TextureSet: {DescribirFormID(hdpt.TextureSet)}", hdpt.TextureSet)
                    If hdpt.Color <> 0UI Then Agregar(hpChildNode, $"Color: {DescribirFormID(hdpt.Color)}", hdpt.Color)
                    If hdpt.PartesExtra().Count > 0 Then
                        For Each epId In hdpt.PartesExtra()
                            Agregar(hpChildNode, $"Extra Part: {DescribirFormID(epId)}", epId)
                        Next
                    End If
                Else
                    Agregar(hpNode, $"HDPT [{hpFormID:X8}] (record not found)")
                End If
            Next
        End If

        If isSse Then
            AgregarMorfosSse(modelNode, modelNpc.Record)
            AgregarPartesDeCaraSse(modelNode, modelNpc.Record)
            AgregarCapasDeTinteSse(modelNode, TryCast(modelNpc.Record, Canon.NpcSSE))
        Else
            ' Face Morph Presets (MSDK/MSDV)
            If modelNpc.Record.MorfosDeCara().Count > 0 Then
                Dim morphNode = Agregar(modelNode, $"Face Morph Presets ({modelNpc.Record.MorfosDeCara().Count})")
                For Each kvp In modelNpc.Record.MorfosDeCara()
                    Agregar(morphNode, $"Key {kvp.Key:X8} = {kvp.Value:F4}")
                Next
            End If

            ' Face Morph Sculpting (FMRI/FMRS)
            Dim modelFo4 = TryCast(modelNpc.Record, Canon.NpcFO4)
            If modelFo4 IsNot Nothing AndAlso modelFo4.FaceMorphs.Count > 0 Then
                Dim fmNode = Agregar(modelNode, $"Face Morph Sculpting ({modelFo4.FaceMorphs.Count} morphs)")
                For Each fm In modelFo4.FaceMorphs
                    ' ⛔ UN RENGLÓN POR MORPH, no un sub-árbol. El renglón viejo decía «posicion, rotacion
                    ' y escala» sin UN número, así que se abrió el bloque FMRS campo por campo — y con eso
                    ' cada morph pasaba a costar ~10 nodos con nombres («Position - X: …») que no le dicen
                    ' nada a nadie. Decisión del usuario: los siete flotantes van inline, así el árbol
                    ' vuelve a costar UN nodo por morph y los números igual se ven.
                    Agregar(fmNode, $"Morph {fm.FaceMorphIndex:X8}  " &
                                    $"pos({fm.ValuesPositionX:F3}, {fm.ValuesPositionY:F3}, {fm.ValuesPositionZ:F3})  " &
                                    $"rot({fm.ValuesRotationX:F3}, {fm.ValuesRotationY:F3}, {fm.ValuesRotationZ:F3})  " &
                                    $"scale {fm.ValuesScale:F3}")
                Next
            End If
            If modelNpc.Record.TieneIntensidadDeMorfoFacial() Then Agregar(modelNode, $"Facial Morph Intensity (FMIN): {modelNpc.Record.IntensidadDeMorfoFacial():F2}")

            ' Face Tint Layers (TETI/TEND)
            Dim capasDeTinte = FaceTintInputBuilder.CapasAutoradasDelRecord(modelNpc.Record)
            If capasDeTinte.Count > 0 Then
                Dim tintNode = Agregar(modelNode, $"Face Tint Layers ({capasDeTinte.Count})")
                For Each tl In capasDeTinte
                    Dim colorStr = If(tl.Color <> Color.Empty, $" Color:({tl.Color.R},{tl.Color.G},{tl.Color.B},{tl.Color.A})", "")
                    Agregar(tintNode, $"Discr:{tl.Discriminator} Index:{tl.Index} Value:{tl.Value}{colorStr}")
                Next
            End If
        End If
    End Sub

    '----------------------------------------------------------------------------------------------
    ' OBTE / OBTF / OBTS + APPR — sólo Fallout 4, y sin categoría de plantilla
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionObjectTemplates(npc As NPC_Data)
        Dim ranuras = npc.Record.RanurasDeEnganche()
        If ranuras.Count > 0 Then
            Dim apprNode = Agregar(Nothing, $"Attach Parent Slots (APPR, {ranuras.Count})")
            For Each kw In ranuras
                Agregar(apprNode, DescribirFormID(kw), kw)
            Next
        End If

        Dim combos = npc.Record.CombinacionesDelNpc()
        If combos.Count = 0 Then Return
        Dim raiz = Agregar(Nothing, $"Object Templates (OBTE, {combos.Count} combination(s))")
        Dim i = 0
        For Each c In combos
            Dim nombre = If(String.IsNullOrEmpty(c.CombinationName), $"(unnamed #{i})", c.CombinationName)
            Dim cn = Agregar(raiz, $"[{i}] {nombre}")
            If c.CombinationEditorOnly Then Agregar(cn, "Editor Only")
            Agregar(cn, $"Level: min={c.ObjectModTemplateItemLevelMin}  max={c.ObjectModTemplateItemLevelMax}  " &
                        $"minForRanks={c.ObjectModTemplateItemMinLevelForRanks}  altPerTier={c.ObjectModTemplateItemAltLevelsPerTier}")
            Agregar(cn, $"Parent Combination Index: {c.ObjectModTemplateItemParentCombinationIndex}   Default: {c.ObjectModTemplateItemDefault}")
            If c.Keywords.Count > 0 Then
                Dim kn = Agregar(cn, $"Match Keywords ({c.Keywords.Count})")
                For Each k In c.Keywords
                    Agregar(kn, DescribirFormID(k.Keyword), k.Keyword)
                Next
            End If
            If c.Includes.Count > 0 Then
                Dim inc = Agregar(cn, $"Includes ({c.Includes.Count})")
                For Each x In c.Includes
                    Agregar(inc, $"OMOD {DescribirFormID(x.IncludeMod)}  attachPoint={x.IncludeAttachPointIndex}" &
                                 $"  optional={x.IncludeOptional}  dontUseAll={x.IncludeDonTUseAll}", x.IncludeMod)
                Next
            End If
            If c.Properties.Count > 0 Then
                Dim pr = Agregar(cn, $"Properties ({c.Properties.Count})")
                For Each p In c.Properties
                    Agregar(pr, $"{p.PropertyValueTypeNombre} / {p.PropertyFunctionTypeNombre} " &
                                $"prop={p.[Property]}  step={p.PropertyStep:F4}  {ValorDeOmod(p)}")
                Next
            End If
            i += 1
        Next
    End Sub

    ''' <summary>El «Value 1 / Value 2» de una propiedad de OBTS es una UNIÓN: la rama la eligió el
    ''' parseo. Se muestra la que el árbol TIENE, no una que se adivine por el tipo — el tipo y la rama
    ''' pueden no coincidir en un record mal formado y ahí lo que manda son los bytes.</summary>
    Private Function ValorDeOmod(p As Canon.IBloque_Properties4) As String
        If p.PropertyValue1FormIDPresente Then Return $"v1={DescribirFormID(p.PropertyValue1FormID)}"
        If p.PropertyValue1FloatPresente Then Return $"v1={p.PropertyValue1Float:F4}"
        If p.PropertyValue1IntPresente Then Return $"v1={p.PropertyValue1Int}"
        If p.PropertyValue1BoolPresente Then Return $"v1={p.PropertyValue1Bool}"
        If p.PropertyValue1EnumPresente Then Return $"v1={p.PropertyValue1Enum}"
        If p.Value1SoundLevelPresente Then Return $"v1={p.Value1SoundLevelNombre}"
        If p.Value1StaggerValuePresente Then Return $"v1={p.Value1StaggerValueNombre}"
        If p.Value1HitBehaviourPresente Then Return $"v1={p.Value1HitBehaviourNombre}"
        If p.PropertyValue1UnknownPresente Then Return $"v1={EnHex(p.PropertyValue1Unknown)}"
        Return "v1=(absent)"
    End Function

    '----------------------------------------------------------------------------------------------
    ' ATKR / ATKD / ATKE / ATKW / ATKS / ATKT
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionAtaques(npc As NPC_Data, isSse As Boolean)
        Dim atkNpc = ResolverFuenteDeSeccion(npc, NPC_TemplateCategory.AttackData)
        Dim ataques = atkNpc.Record.Attacks
        If ataques.Count = 0 AndAlso Not atkNpc.Record.AttackRacePresente Then Return
        Dim n = Agregar(Nothing, EtiquetaDeSeccion(npc, atkNpc, $"Attacks ({ataques.Count})"), FormIDDeFuente(npc, atkNpc))
        If atkNpc.Record.AttackRacePresente Then Agregar(n, $"Attack Race (ATKR): {DescribirFormID(atkNpc.Record.AttackRace)}", atkNpc.Record.AttackRace)
        Dim i = 0
        For Each a In ataques
            Dim etiqueta = If(String.IsNullOrEmpty(a.AttackEvent), $"[{i}]", $"[{i}] {a.AttackEvent}")
            Dim an = Agregar(n, etiqueta)
            Agregar(an, $"Flags: {NpcManagerFormat.DescribirBanderas(a.Node, "ATKD", "Attack Flags")}")
            Agregar(an, $"Damage Mult: {a.AttackDataDamageMult:F3}   Attack Chance: {a.AttackDataAttackChance:F3}")
            If a.AttackDataAttackSpell <> 0UI Then Agregar(an, $"Attack Spell: {DescribirFormID(a.AttackDataAttackSpell)}", a.AttackDataAttackSpell)
            Agregar(an, $"Angles: attack={a.AttackDataAttackAngle:F2}  strike={a.AttackDataStrikeAngle:F2}")
            Agregar(an, $"Stagger: {a.AttackDataStagger:F3}   Knockdown: {a.AttackDataKnockdown:F3}   Recovery: {a.AttackDataRecoveryTime:F3}")
            Dim aFo4 = TryCast(a, Canon.NpcFO4_Attacks)
            If aFo4 IsNot Nothing Then
                Agregar(an, $"Action Points Mult: {aFo4.AttackDataActionPointsMult:F3}   Stagger Offset: {aFo4.AttackDataStaggerOffset}")
                If aFo4.AttackWeaponSlotPresente Then Agregar(an, $"Weapon Slot (ATKW): {DescribirFormID(aFo4.AttackWeaponSlot)}", aFo4.AttackWeaponSlot)
                If aFo4.AttackRequiredSlotPresente Then Agregar(an, $"Required Slot (ATKS): {DescribirFormID(aFo4.AttackRequiredSlot)}", aFo4.AttackRequiredSlot)
                If aFo4.AttackDescriptionPresente Then Agregar(an, $"Description (ATKT): {aFo4.AttackDescription}")
            End If
            Dim aSse = TryCast(a, Canon.NpcSSE_Attacks)
            If aSse IsNot Nothing Then
                Agregar(an, $"Attack Type: {DescribirFormID(aSse.AttackDataAttackType)}   Stamina Mult: {aSse.AttackDataStaminaMult:F3}")
            End If
            i += 1
        Next
    End Sub

    '----------------------------------------------------------------------------------------------
    ' DEST / DAMC / DSTD / DSTA / DMDL / DMDC / DMDS / DSTF
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionDestruible(npc As NPC_Data, isSse As Boolean)
        If Not npc.Record.HeaderHealthPresente AndAlso npc.Record.Stages.Count = 0 Then Return
        Dim n = Agregar(Nothing, $"Destructible (DEST, {npc.Record.Stages.Count} stage(s))")
        If npc.Record.HeaderHealthPresente Then
            Agregar(n, $"Health: {npc.Record.HeaderHealth}   DEST Count: {npc.Record.HeaderDESTCount}")
            If isSse Then
                Dim dSse = TryCast(npc.Record, Canon.NpcSSE)
                If dSse IsNot Nothing Then Agregar(n, $"VATS Targetable: {If(dSse.HeaderVATSTargetable, "Yes", "No")}")
            Else
                Dim dFo4 = TryCast(npc.Record, Canon.NpcFO4)
                If dFo4 IsNot Nothing Then Agregar(n, $"Flags: {NpcManagerFormat.DescribirBanderas(npc.Record.Node, "DEST", "Flags")}")
            End If
            If npc.Record.HeaderUnknownPresente Then Agregar(n, $"DEST trailing bytes: {EnHex(npc.Record.HeaderUnknown)}")
        End If
        ' DAMC: las resistencias del destruible, sólo Fallout 4.
        Dim fo4 = TryCast(npc.Record, Canon.NpcFO4)
        If fo4 IsNot Nothing AndAlso fo4.Resistances.Count > 0 Then
            Dim rn = Agregar(n, $"Damage Resistances (DAMC, {fo4.Resistances.Count})")
            For Each r In fo4.Resistances
                Agregar(rn, $"{DescribirFormID(r.ResistanceDamageType)} = {r.ResistanceValue}", r.ResistanceDamageType)
            Next
        End If
        Dim i = 0
        For Each s In npc.Record.Stages
            Dim sn = Agregar(n, $"Stage [{i}] index={s.DestructionStageDataIndex}  health%={s.DestructionStageDataHealth}")
            Agregar(sn, $"Model Damage Stage: {s.DestructionStageDataModelDamageStage}   Flags: {NpcManagerFormat.DescribirBanderas(s.Node, "DSTD", "Flags")}")
            Agregar(sn, $"Self Damage/s: {s.DestructionStageDataSelfDamagePerSecond}   Debris Count: {s.DestructionStageDataDebrisCount}")
            If s.DestructionStageDataExplosion <> 0UI Then Agregar(sn, $"Explosion: {DescribirFormID(s.DestructionStageDataExplosion)}", s.DestructionStageDataExplosion)
            If s.DestructionStageDataDebris <> 0UI Then Agregar(sn, $"Debris: {DescribirFormID(s.DestructionStageDataDebris)}", s.DestructionStageDataDebris)
            If s.StageModelFileNamePresente AndAlso s.StageModelFileName <> "" Then Agregar(sn, $"Model (DMDL): {s.StageModelFileName}")
            ' DMDT: la información de modelo del bloque. No se abre campo por campo —es el mismo blob que
            ' el resto de la app no interpreta—, pero se DICE que está y cuánto trae, que es la diferencia
            ' entre «no lo muestro» y «no está».
            If s.ModelInformationERRORPresente Then Agregar(sn, $"Model info (DMDT): {EnHex(s.ModelInformationERROR)}")
            Dim sFo4 = TryCast(s, Canon.NpcFO4_Stages)
            If sFo4 IsNot Nothing Then
                If sFo4.StageSequenceNamePresente AndAlso sFo4.StageSequenceName <> "" Then Agregar(sn, $"Sequence Name (DSTA): {sFo4.StageSequenceName}")
                If sFo4.ModelColorRemappingIndexPresente Then Agregar(sn, $"Color Remapping Index (DMDC): {sFo4.ModelColorRemappingIndex:F4}")
                If sFo4.ModelMaterialSwapPresente Then Agregar(sn, $"Material Swap (DMDS): {DescribirFormID(sFo4.ModelMaterialSwap)}", sFo4.ModelMaterialSwap)
                If sFo4.Textures.Count > 0 Then Agregar(sn, $"Model textures: {sFo4.Textures.Count}")
                If sFo4.AddonNodes.Count > 0 Then Agregar(sn, $"Model addon nodes: {sFo4.AddonNodes.Count}")
                If sFo4.Materials.Count > 0 Then Agregar(sn, $"Model materials: {sFo4.Materials.Count}")
            End If
            Dim sSse = TryCast(s, Canon.NpcSSE_Stages)
            If sSse IsNot Nothing Then
                ' En Skyrim DMDS no es un MSWP sino el arreglo de «Alternate Textures» del modelo: pares
                ' (nombre de nodo 3D → TXST) que le cambian el set de texturas a un nodo de esa malla.
                ' ⛔ SOLO SE CUENTA, no se abre campo por campo. MEDIDO: en este árbol NADIE las consume —
                ' la única mención en la librería es un comentario que avisa que en Skyrim son un arreglo
                ' y no un FormID—, y encima acá cuelgan del modelo de una ETAPA DE DESTRUCCIÓN, que la app
                ' tampoco dibuja. Un renglón dice que están y cuántas son; volcarlas entera era pagar por
                ' un dato que no se usa.
                If sSse.AlternateTextures.Count > 0 Then Agregar(sn, $"Alternate Textures (DMDS): {sSse.AlternateTextures.Count}")
                If sSse.Textures.Count > 0 Then Agregar(sn, $"Model textures: {sSse.Textures.Count}")
                If sSse.AddonNodes.Count > 0 Then Agregar(sn, $"Model addon nodes: {sSse.AddonNodes.Count}")
            End If
            i += 1
        Next
    End Sub

    '----------------------------------------------------------------------------------------------
    ' CS2H/CS2K/CS2D/CS2F (FO4) · CSDT/CSDI/CSDC (SSE) · CSCR
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionSonidos(npc As NPC_Data, isSse As Boolean)
        Dim fo4 = TryCast(npc.Record, Canon.NpcFO4)
        Dim sse = TryCast(npc.Record, Canon.NpcSSE)
        Dim hayFo4 = fo4 IsNot Nothing AndAlso fo4.Sounds.Count > 0
        Dim haySse = sse IsNot Nothing AndAlso sse.SoundTypes.Count > 0
        If Not hayFo4 AndAlso Not haySse AndAlso Not npc.Record.InheritsSoundsFromPresente Then Return

        Dim n = Agregar(Nothing, "Actor Sounds")
        If npc.Record.InheritsSoundsFromPresente Then
            ' ⛔ CSCR apunta a un NPC_: es navegable igual que una plantilla.
            Agregar(n, $"Inherits Sounds From (CSCR): {DescribirFormID(npc.Record.InheritsSoundsFrom)}", npc.Record.InheritsSoundsFrom)
        End If
        If hayFo4 Then
            For Each s In fo4.Sounds
                Agregar(n, $"{DescribirFormID(s.SoundKeyword)} → {DescribirFormID(s.Sound)}", s.Sound)
            Next
            If fo4.ActorSoundsFinalizePresente Then Agregar(n, $"Finalize (CS2F): {EnHex(fo4.ActorSoundsFinalize)}")
        End If
        If haySse Then
            For Each st In sse.SoundTypes
                Dim tn = Agregar(n, $"{st.SoundTypeTypeNombre} ({st.Sounds.Count})")
                For Each s In st.Sounds
                    Agregar(tn, $"{DescribirFormID(s.Sound)}  chance={s.SoundChance}", s.Sound)
                Next
            Next
        End If
    End Sub

    '----------------------------------------------------------------------------------------------
    ' VMAD
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionScripts(npc As NPC_Data)
        Dim scNpc = ResolverFuenteDeSeccion(npc, NPC_TemplateCategory.Script)
        Dim scripts = scNpc.Record.Scripts
        If scripts.Count = 0 Then Return
        Dim n = Agregar(Nothing, EtiquetaDeSeccion(npc, scNpc, $"Scripts (VMAD, {scripts.Count})"), FormIDDeFuente(npc, scNpc))
        Agregar(n, $"Version: {scNpc.Record.VirtualMachineAdapterVersion}   Object Format: {scNpc.Record.VirtualMachineAdapterObjectFormat}")
        For Each s In scripts
            Dim sn = Agregar(n, $"{s.ScriptName}  [{s.ScriptFlagsNombre}]")
            AgregarPropiedadesDeScript(sn, s)
        Next
    End Sub

    ''' <summary>Las propiedades de un script. La lista vive en la clase del juego, así que hay que
    ''' bajar a ella; los dos juegos declaran los mismos campos de propiedad salvo los arreglos
    ''' anidados, que no se abren acá.</summary>
    Private Sub AgregarPropiedadesDeScript(destino As DetalleNodo, s As Canon.INpc_Scripts)
        Dim sFo4 = TryCast(s, Canon.NpcFO4_Scripts)
        If sFo4 IsNot Nothing Then
            For Each p In sFo4.Properties
                Agregar(destino, $"{p.PropertyName} [{p.PropertyFlagsNombre}] = {ValorDePropiedad(p.Node)}")
            Next
            Return
        End If
        Dim sSse = TryCast(s, Canon.NpcSSE_Scripts)
        If sSse Is Nothing Then Return
        For Each p In sSse.Properties
            Agregar(destino, $"{p.PropertyName} [{p.PropertyFlagsNombre}] = {ValorDePropiedad(p.Node)}")
        Next
    End Sub

    ''' <summary>El valor de una propiedad de VMAD es una UNIÓN de once ramas. Se muestra la que el
    ''' parseo ELIGIÓ —el hijo «Value» que el árbol tiene—, no una deducida del byte de tipo: si el
    ''' record miente sobre su tipo, lo que vale son los bytes que se leyeron.</summary>
    Private Function ValorDePropiedad(nodoPropiedad As WbNode) As String
        If nodoPropiedad Is Nothing Then Return "(absent)"
        For Each h In nodoPropiedad.Children
            If h.ChildCount > 0 Then Return $"({h.Name}: {h.ChildCount} element(s))"
            If h.Value Is Nothing Then Continue For
            If String.Equals(h.Name, "Name", StringComparison.Ordinal) Then Continue For
            If String.Equals(h.Name, "Type", StringComparison.Ordinal) Then Continue For
            If String.Equals(h.Name, "Flags", StringComparison.Ordinal) Then Continue For
            Return TextoDeHoja(h)
        Next
        Return "(absent)"
    End Function

    '----------------------------------------------------------------------------------------------
    ' Lo que queda: referencias sueltas del record propio
    '----------------------------------------------------------------------------------------------
    Private Sub SeccionOtros(npc As NPC_Data, isSse As Boolean)
        Dim otherNode = Agregar(Nothing, "Other")
        If npc.Record.DeathItemPresente Then Agregar(otherNode, $"Death Item (INAM): {DescribirFormID(npc.Record.DeathItem)}", npc.Record.DeathItem)
        If npc.Record.CombatStylePresente Then Agregar(otherNode, $"Combat Style (ZNAM): {DescribirFormID(npc.Record.CombatStyle)}", npc.Record.CombatStyle)
        If npc.Record.CrimeFactionPresente Then Agregar(otherNode, $"Crime Faction (CRIF): {DescribirFormID(npc.Record.CrimeFaction)}", npc.Record.CrimeFaction)
        If npc.Record.GiftFilterPresente Then Agregar(otherNode, $"Gift Filter (GNAM): {DescribirFormID(npc.Record.GiftFilter)}", npc.Record.GiftFilter)
        If npc.Record.FarAwayModelPresente Then Agregar(otherNode, $"Far Away Model (ANAM): {DescribirFormID(npc.Record.FarAwayModel)}", npc.Record.FarAwayModel)
        ' El nombre del nivel de sonido sale del ESQUEMA (NAM8 es un enumerado declarado), no de una
        ' tabla repetida acá: Fallout agrega un quinto valor que Skyrim no tiene y la tabla a mano lo
        ' tenía que saber.
        If npc.Record.SoundLevelPresente Then Agregar(otherNode, $"Sound Level (NAM8): {npc.Record.SoundLevelNombre}")
        If npc.Record.UnknownPresente Then Agregar(otherNode, $"Unknown (NAM5): {EnHex(npc.Record.Unknown)}")
        If Not isSse Then
            ' PFRN (soporte de servoarmadura), NTRM (terminal nativa), FTYP, PTRN, STCP y ATTX no tienen
            ' equivalente en Skyrim.
            Dim otherFo4 = TryCast(npc.Record, Canon.NpcFO4)
            If otherFo4 IsNot Nothing Then
                If otherFo4.PowerArmorStandPresente Then Agregar(otherNode, $"Power Armor Stand (PFRN): {DescribirFormID(otherFo4.PowerArmorStand)}", otherFo4.PowerArmorStand)
                If otherFo4.NativeTerminalPresente Then Agregar(otherNode, $"Native Terminal (NTRM): {DescribirFormID(otherFo4.NativeTerminal)}", otherFo4.NativeTerminal)
                If otherFo4.ForcedLocRefTypePresente Then Agregar(otherNode, $"Forced Loc Ref Type (FTYP): {DescribirFormID(otherFo4.ForcedLocRefType)}", otherFo4.ForcedLocRefType)
                If otherFo4.PreviewTransformPresente Then Agregar(otherNode, $"Preview Transform (PTRN): {DescribirFormID(otherFo4.PreviewTransform)}", otherFo4.PreviewTransform)
                If otherFo4.AnimationSoundPresente Then Agregar(otherNode, $"Animation Sound (STCP): {DescribirFormID(otherFo4.AnimationSound)}", otherFo4.AnimationSound)
                If otherFo4.ActivateTextOverridePresente Then Agregar(otherNode, $"Activate Text Override (ATTX): {otherFo4.ActivateTextOverride}")
            End If
        End If
        If otherNode.Hijos.Count = 0 Then _raices.Remove(otherNode)
    End Sub

    '==============================================================================================
    ' Herencia
    '==============================================================================================

    ''' <summary>El NPC que de verdad provee una categoría de plantilla — el terminal de la cadena, o el
    ''' record mismo cuando la categoría no se hereda. Nunca Nothing (cae en <paramref name="npc"/>).</summary>
    Public Function ResolverFuenteDeSeccion(npc As NPC_Data, category As NPC_TemplateCategory) As NPC_Data
        Return If(ResolverNpcHeredado(npc, category), npc)
    End Function

    ''' <summary>Texto del encabezado de sección, con «(own)» o «(inherited from X)».</summary>
    Private Shared Function EtiquetaDeSeccion(npc As NPC_Data, source As NPC_Data, title As String) As String
        If source IsNot Nothing AndAlso source.FormID <> npc.FormID Then
            Return $"{title}  (inherited from {NpcManagerFormat.DescribeNpc(source)} [{source.FormID:X8}])"
        End If
        Return $"{title}  (own)"
    End Function

    ''' <summary>El FormID que el encabezado de sección NOMBRA: el del actor del que se heredó, o 0 si la
    ''' sección es propia. Doble click sobre «Traits (inherited from X)» va a X.</summary>
    Private Shared Function FormIDDeFuente(npc As NPC_Data, source As NPC_Data) As UInteger
        If source IsNot Nothing AndAlso source.FormID <> npc.FormID Then Return source.FormID
        Return 0UI
    End Function

    ''' <summary>Sigue la cadena de plantilla de una categoría y devuelve el NPC terminal que provee el valor.</summary>
    Private Function ResolverNpcHeredado(npc As NPC_Data, category As NPC_TemplateCategory) As NPC_Data
        If npc Is Nothing OrElse Not NpcTemplateHelpers.HasTemplateFlag(npc.Record.ConfigurationTemplateFlags, category) Then Return npc

        Dim visited As New HashSet(Of UInteger)
        Dim current = npc

        While current IsNot Nothing
            If visited.Contains(current.FormID) Then Exit While
            visited.Add(current.FormID)

            If Not NpcTemplateHelpers.HasTemplateFlag(current.Record.ConfigurationTemplateFlags, category) Then Return current

            Dim sourceFormID = NpcTemplateHelpers.ResolveTemplateSourceFormID(current, category)
            If sourceFormID = 0UI Then Return current

            ' Si la fuente es una leveled NPC, se toma la primera entrada.
            Dim sourceRec = _pluginManager.GetRecord(sourceFormID)
            If sourceRec Is Nothing Then Return current

            If sourceRec.Header.Signature = "NPC_" Then
                current = _ctx.GetParsedNpc(sourceFormID)
                If current Is Nothing Then Return npc
            ElseIf sourceRec.Header.Signature = "LVLN" Then
                Dim lvln As Canon.ILvln = Nothing
                If Not _detailsLvlnCache.TryGetValue(sourceFormID, lvln) Then
                    lvln = NpcTemplateHelpers.TryAbrirLvlnTolerante(sourceRec, _pluginManager)
                    ' El fallo también se cachea: `_detailsLvlnCache` se vacía en CADA construcción del
                    ' árbol de detalle, o sea en cada selección de NPC, así que no puede quedar pegado.
                    _detailsLvlnCache(sourceFormID) = lvln
                End If
                ' TryAbrirLvlnTolerante devuelve Nothing en el LVLN malformado que existe para tolerar.
                ' Sin este guard el .LeveledListEntries de abajo tira NRE adentro del handler que
                ' repuebla el panel. Mismo patrón que los otros call sites en NpcTemplateHelpers /
                ' NpcStateResolver.
                If lvln Is Nothing Then Return current
                Dim firstNpcId = lvln.LeveledListEntries.Select(Function(e) e.LeveledListEntryNPC).
                    FirstOrDefault(Function(fid)
                                       Dim r = _pluginManager.GetRecord(fid)
                                       Return r IsNot Nothing AndAlso r.Header.Signature = "NPC_"
                                   End Function)
                If firstNpcId = 0UI Then Return current
                current = _ctx.GetParsedNpc(firstNpcId)
                If current Is Nothing Then Return npc
            Else
                Return current
            End If
        End While

        Return npc
    End Function

    '==============================================================================================
    ' Expansiones que salen del record: RACE y atuendo
    '==============================================================================================

    Private Sub ExpandirRaza(padre As DetalleNodo, raceFormID As UInteger, isFemale As Boolean)
        If raceFormID = 0UI Then Return
        Dim raceRec = _pluginManager.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return

        Dim race = _ctx.ParseRaceCanonCached(raceRec)
        Dim raceNodeName = If(race.NamePresente, race.Name, "")
        Dim raceNode = Agregar(padre, $"Race: {raceNodeName} [{race.EditorID}]", raceFormID)
        ' Default Face Texture: DFTM/DFTF, declarado por juego con su propia colección — TryCast al que
        ' corresponda.
        Dim raceFo4 = TryCast(race, Canon.RaceFO4)
        Dim raceSse = TryCast(race, Canon.RaceSSE)
        If isFemale Then
            If race.FemaleSkeletalModelPresente Then Agregar(raceNode, $"Skeleton: {race.FemaleSkeletalModel}")
            ' El filtro por ".nif" es del consumidor viejo (sólo mallas): se replica acá para no listar,
            ' p.ej., un ".egt" de morph de cuerpo como si fuera malla del cuerpo.
            Dim femaleMeshes = race.Parts2.Select(Function(p) p.PartModelFileName).
                Where(Function(m) m.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)).ToList()
            For Each mesh In femaleMeshes
                Agregar(raceNode, $"Body Mesh: {mesh}")
            Next
            Dim femaleFaceTex As UInteger = If(raceFo4 IsNot Nothing, raceFo4.FemaleDefaultFaceTexture,
                                               If(raceSse IsNot Nothing, raceSse.FemaleHeadDataDefaultFaceTextureFemale, 0UI))
            If femaleFaceTex <> 0UI Then Agregar(raceNode, $"Default Face Texture: {DescribirFormID(femaleFaceTex)}", femaleFaceTex)
        Else
            If race.MaleSkeletalModelPresente Then Agregar(raceNode, $"Skeleton: {race.MaleSkeletalModel}")
            Dim maleMeshes = race.Parts.Select(Function(p) p.PartModelFileName).
                Where(Function(m) m.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)).ToList()
            For Each mesh In maleMeshes
                Agregar(raceNode, $"Body Mesh: {mesh}")
            Next
            Dim maleFaceTex As UInteger = If(raceFo4 IsNot Nothing, raceFo4.MaleDefaultFaceTexture,
                                             If(raceSse IsNot Nothing, raceSse.MaleHeadDataDefaultFaceTextureMale, 0UI))
            If maleFaceTex <> 0UI Then Agregar(raceNode, $"Default Face Texture: {DescribirFormID(maleFaceTex)}", maleFaceTex)
        End If
        Dim pielDeLaRaza = Canon.CanonInterpretacion.SkinDe(race)
        If pielDeLaRaza <> 0UI Then Agregar(raceNode, $"Race Skin: {DescribirFormID(pielDeLaRaza)}", pielDeLaRaza)
    End Sub

    Private Sub ExpandirAtuendo(padre As DetalleNodo, outfitFormID As UInteger)
        If outfitFormID = 0UI Then Return
        Dim outfitRec = _pluginManager.GetRecord(outfitFormID)
        If outfitRec Is Nothing Then Return

        If outfitRec.Header.Signature = "OTFT" Then
            Dim otft = Canon.CanonRecords.Otft(outfitRec, _pluginManager)
            For Each itemFormID In otft.Prendas()
                ExpandirPrenda(padre, itemFormID)
            Next
        End If
    End Sub

    ''' <summary>Una prenda del atuendo. El ARMO se abre entero —sus armatures, sus mallas, sus texturas
    ''' y sus material swaps—; la lista por nivel NO.
    '''
    ''' <para>⛔ POR QUE LA LVLI NO SE ABRE. Abrirla era recorrer, recursivamente, todos los ARMO que esa
    ''' lista PODRÍA sortear, y las listas de Bethesda se anidan: MEDIDO sobre el corpus del usuario
    ''' (Tools\DetalleDelRecordGate, 400 NPC por juego), el peor árbol de Fallout 4 tenía 15.598 nodos y
    ''' <b>15.055 de ellos —el 96,5 %— eran esta expansión</b>, que costaba ~3.065 ms de volcado EN EL
    ''' HILO DE UI por cada selección de NPC; el peor de Skyrim llegaba a 86.795 nodos. Antes no se
    ''' notaba porque el nodo quedaba cerrado; desde que el panel se abre entero, se paga entero.</para>
    '''
    ''' <para>Y lo que se perdía no era «lo que el NPC lleva» sino «lo que la lista podría sortear»: el
    ''' sorteo real ya lo resuelve el preview. El renglón dice el nombre de la lista y CUÁNTAS entradas
    ''' tiene, así que sigue estando la puerta para ir a mirarla.</para>
    '''
    ''' <para>De paso desaparece la recursión, y con ella el ciclo: una LVLI que se contiene a sí misma
    ''' —que abriendo todo se llevaba la app puesta— ya no tiene por dónde repetirse.</para></summary>
    Private Sub ExpandirPrenda(padre As DetalleNodo, itemFormID As UInteger)
        If itemFormID = 0UI Then Return
        Dim itemRec = _pluginManager.GetRecord(itemFormID)
        If itemRec Is Nothing Then
            Agregar(padre, $"[{itemFormID:X8}] (missing record)")
            Return
        End If

        Select Case itemRec.Header.Signature
            Case "ARMO"
                ' CRUDA: el arbol muestra QUE DICE EL ARCHIVO. ⛔ Con `TNAM ≠ 0` el nodo lo MARCA:
                ' los armatures que se listan aca no son los que el motor va a usar.
                Dim armo = _ctx.GetParsedArmoCrudo(itemFormID)
                Dim slotStr = NpcManagerFormat.FormatSlotMask(armo.SlotMaskDe())
                Dim heredaDe = If(armo.TemplateArmor <> 0UI, "  [INHERITS]", "")
                Dim armoNode = Agregar(padre, $"ARMO {armo.EditorID}  ""{armo.Name}""  [{armo.FormID:X8}]  Slots:{slotStr}{heredaDe}", itemFormID)

                ' Se sigue la armadura plantilla.
                If armo.TemplateArmor <> 0UI Then
                    Agregar(armoNode, $"Template Armor: {DescribirFormID(armo.TemplateArmor)}" &
                                      "   <- the engine takes the armatures, slots, keywords and mesh FROM HERE", armo.TemplateArmor)
                    ' ⛔ Este arbol es un inspector del ARCHIVO, asi que lista lo que el archivo declara.
                    ' Pero si el motor va a usar OTRA cosa, se dice y se muestra: deducirlo de un renglon
                    ' de mas arriba no es verlo.
                    Dim efectivaArbol = _ctx.GetParsedArmoEfectivo(itemFormID)
                    If efectivaArbol IsNot Nothing Then
                        Dim propios = Canon.CanonInterpretacion.ComplementosDe(armo)
                        Dim usados = Canon.CanonInterpretacion.ComplementosDe(efectivaArbol)
                        If Not New HashSet(Of UInteger)(propios).SetEquals(usados) Then
                            Dim nEfe = Agregar(armoNode,
                                $"⚠ What the engine will DRAW: {usados.Count} armature(s) from the terminal " &
                                $"(this record declares {propios.Count})")
                            For Each af In usados
                                Agregar(nEfe, $"ARMA {DescribirFormID(af)}", af)
                            Next
                        End If
                    End If
                End If

                ' Armor Addons
                For Each addon In Canon.CanonInterpretacion.LeerComplementos(armo)
                    Dim aaFormID = addon.ArmaFormID
                    Dim aaRec = _pluginManager.GetRecord(aaFormID)
                    If aaRec Is Nothing OrElse aaRec.Header.Signature <> "ARMA" Then
                        Agregar(armoNode, $"ARMA [{aaFormID:X8}] (missing)")
                        Continue For
                    End If
                    Dim arma = _ctx.GetParsedArma(aaFormID)
                    Dim armaFo4 = TryCast(arma, Canon.ArmaFO4)
                    Dim aaNode = Agregar(armoNode, $"ARMA {arma.EditorID}  [{arma.FormID:X8}]  Slots:{NpcManagerFormat.FormatSlotMask(arma.SlotMaskDe())}", aaFormID)
                    If arma.MaleModelFilename <> "" Then Agregar(aaNode, $"Male Mesh: {arma.MaleModelFilename}")
                    If arma.FemaleModelFilename <> "" Then Agregar(aaNode, $"Female Mesh: {arma.FemaleModelFilename}")
                    If arma.MaleModelFilename2 <> "" Then Agregar(aaNode, $"Male 1P Mesh: {arma.MaleModelFilename2}")
                    If arma.FemaleModelFilename2 <> "" Then Agregar(aaNode, $"Female 1P Mesh: {arma.FemaleModelFilename2}")
                    If arma.MaleSkinTexture <> 0UI Then Agregar(aaNode, $"Male Skin Texture: {DescribirFormID(arma.MaleSkinTexture)}", arma.MaleSkinTexture)
                    If arma.FemaleSkinTexture <> 0UI Then Agregar(aaNode, $"Female Skin Texture: {DescribirFormID(arma.FemaleSkinTexture)}", arma.FemaleSkinTexture)
                    ' MO2S/MO3S (material swap) sólo existen en Fallout 4.
                    If armaFo4 IsNot Nothing AndAlso armaFo4.MaleMaterialSwap <> 0UI Then
                        Agregar(aaNode, $"Male Material Swap: {DescribirFormID(armaFo4.MaleMaterialSwap)}", armaFo4.MaleMaterialSwap)
                    End If
                    If armaFo4 IsNot Nothing AndAlso armaFo4.FemaleMaterialSwap <> 0UI Then
                        Agregar(aaNode, $"Female Material Swap: {DescribirFormID(armaFo4.FemaleMaterialSwap)}", armaFo4.FemaleMaterialSwap)
                    End If
                    If arma.AdditionalRaces.Count > 0 Then
                        For Each raceId In arma.AdditionalRaces
                            Agregar(aaNode, $"Additional Race: {DescribirFormID(raceId.Race)}", raceId.Race)
                        Next
                    End If
                Next

            Case "LVLI"
                ' Ver el comentario de este método: se nombra y se cuenta, no se abre.
                Dim lvli = Canon.CanonRecords.Lvli(itemRec, _pluginManager)
                If lvli Is Nothing Then
                    ' ⛔ EN INGLÉS como todo el panel. Este renglón venía en castellano de cuando el
                    ' árbol vivía en MainForm: era el único texto del panel que no estaba en el idioma
                    ' de la UI.
                    Agregar(padre, $"LVLI [{itemFormID:X8}] (does not parse)")
                    Return
                End If
                Agregar(padre, $"LVLI {lvli.EditorID}  [{lvli.FormID:X8}]  ({lvli.LeveledListEntries.Count} entries, not expanded)", itemFormID)

            Case Else
                Agregar(padre, $"{itemRec.Header.Signature} {itemRec.EditorID}  [{itemFormID:X8}]", itemFormID)
        End Select
    End Sub

    '==============================================================================================
    ' Bloques de cara de Skyrim
    '==============================================================================================

    ''' <summary>NAM9 — los deslizadores de chargen. Los NOMBRES y CUÁNTOS son salen del ESQUEMA, no de
    ''' una tabla repetida acá: la tabla a mano tenía 19 nombres fijos y un record más corto se leía
    ''' igual, mostrando nombres que sus bytes no traen.</summary>
    Private Sub AgregarMorfosSse(padre As DetalleNodo, rec As Canon.INpc)
        Dim n = BloqueDeSubrecord(rec.Node, "NAM9")
        If n Is Nothing Then Return
        VolcarHojas(Agregar(padre, $"Face Morph (NAM9, {n.ChildCount} sliders)"), n)
    End Sub

    ''' <summary>NAMA — las cuatro partes de cara (Nose / Unknown / Eyes / Mouth). Mismo criterio que
    ''' NAM9: los nombres los declara el formato.</summary>
    Private Sub AgregarPartesDeCaraSse(padre As DetalleNodo, rec As Canon.INpc)
        Dim n = BloqueDeSubrecord(rec.Node, "NAMA")
        If n Is Nothing Then Return
        VolcarHojas(Agregar(padre, "Face Parts (NAMA)"), n)
    End Sub

    ''' <summary>El bloque de campos de un subrecord, buscado por FIRMA.
    '''
    ''' <para>⛔ POR FIRMA Y NO POR RUTA. Las rutas del esquema se comparan con
    ''' <c>StringComparison.Ordinal</c>, así que el bloque de NAM9 se llama <c>«Face morph»</c> con eme
    ''' minúscula y el de NAMA <c>«Face parts»</c>: escribirlos con mayúscula —que es lo natural— NO
    ''' resuelve, devuelve Nothing y la sección desaparece EN SILENCIO. La firma son cuatro caracteres
    ''' que el archivo trae literalmente y no hay dónde equivocarse.</para>
    '''
    ''' <para>Si el subrecord envuelve sus campos en UNA estructura, se devuelve la estructura, así el
    ''' volcado no agrega un nivel de anidado que no le dice nada a nadie.</para></summary>
    Private Shared Function BloqueDeSubrecord(raiz As WbNode, firma As String) As WbNode
        Dim sr = CanonBridge.Find(raiz, firma)
        If sr Is Nothing Then Return Nothing
        Dim bloque = If(sr.ChildCount = 1 AndAlso sr.Children(0).ChildCount > 0, sr.Children(0), sr)
        If bloque.ChildCount = 0 Then Return Nothing
        Return bloque
    End Function

    ''' <summary>TINI/TINC/TINV/TIAS: las capas de tinte de cara, el equivalente de Skyrim a los pares
    ''' TETI/TEND de Fallout. Cada campo se muestra sólo si el record lo declara.</summary>
    Private Sub AgregarCapasDeTinteSse(padre As DetalleNodo, npcSse As Canon.NpcSSE)
        If npcSse Is Nothing OrElse npcSse.TintLayers.Count = 0 Then Return
        Dim tintNode = Agregar(padre, "Face Tint Layers")
        Dim layerCount = 0
        For Each tl In npcSse.TintLayers
            If Not tl.LayerTintIndexPresente Then Continue For
            layerCount += 1
            Dim layerNode = Agregar(tintNode, $"Layer index {tl.LayerTintIndex}")
            If tl.TintColorAlphaPresente Then
                Agregar(layerNode, $"Color: R={tl.TintColorRed} G={tl.TintColorGreen} B={tl.TintColorBlue} A={tl.TintColorAlpha}")
            End If
            If tl.LayerInterpolationValuePresente Then
                Agregar(layerNode, $"Interpolation: {tl.LayerInterpolationValue / 100.0F:F2}")
            End If
            If tl.LayerPresetPresente Then
                Agregar(layerNode, If(tl.LayerPreset < 0, "Preset: custom (-1)", $"Preset: {tl.LayerPreset}"))
            End If
        Next
        tintNode.Texto = $"Face Tint Layers ({layerCount})"
    End Sub

    '==============================================================================================
    ' Volcado genérico de un bloque del esquema
    '==============================================================================================

    ''' <summary>Cuelga de <paramref name="destino"/> un renglón por cada HOJA de
    ''' <paramref name="nodo"/>, con el nombre que le pone el ESQUEMA. Es lo que se usa donde el bloque
    ''' es una tira de campos sin ley propia (NAM9, NAMA, MRSV, FMRS): así el nombre y la cantidad los
    ''' pone el formato y no una lista escrita a mano que se queda vieja.</summary>
    Private Sub VolcarHojas(destino As DetalleNodo, nodo As WbNode)
        If destino Is Nothing OrElse nodo Is Nothing Then Return
        For Each h In nodo.Children
            If h.ChildCount > 0 Then
                VolcarHojas(Agregar(destino, h.Name), h)
            Else
                Agregar(destino, $"{h.Name}: {TextoDeHoja(h)}", RefDeHoja(h))
            End If
        Next
    End Sub

    ''' <summary>Cómo se escribe el valor de una hoja. Las referencias se resuelven a nombre, los
    ''' enteros con banderas se abren a los nombres de sus bits y los enumerados al nombre del valor —
    ''' todo desde el esquema.</summary>
    Private Function TextoDeHoja(h As WbNode) As String
        If h Is Nothing OrElse h.Value Is Nothing Then Return "(absent)"
        If TypeOf h.Def Is WbFormIdDef Then Return DescribirFormID(CUInt(CanonBridge.AEntero(h.Value)))
        Dim bytes = TryCast(h.Value, Byte())
        If bytes IsNot Nothing Then Return EnHex(bytes)
        If TypeOf h.Value Is Single Then Return CSng(h.Value).ToString("F4")
        Dim ent = TryCast(h.Def, WbIntegerDef)
        If ent IsNot Nothing Then
            If ent.FlagNames IsNot Nothing Then Return NpcManagerFormat.NombresDeBits(CanonBridge.AEntero(h.Value), ent.FlagNames)
            If ent.EnumValues IsNot Nothing Then
                Dim nombre As String = Nothing
                If ent.EnumValues.TryGetValue(CanonBridge.AEntero(h.Value), nombre) AndAlso Not String.IsNullOrEmpty(nombre) Then
                    Return $"{nombre} ({h.Value})"
                End If
            End If
        End If
        Return Convert.ToString(h.Value)
    End Function

    Private Shared Function RefDeHoja(h As WbNode) As UInteger
        If h Is Nothing OrElse h.Value Is Nothing OrElse Not (TypeOf h.Def Is WbFormIdDef) Then Return 0UI
        Return CUInt(CanonBridge.AEntero(h.Value))
    End Function

    Private Shared Function EnHex(b As Byte()) As String
        If b Is Nothing OrElse b.Length = 0 Then Return "(empty)"
        Return String.Join(" ", b.Select(Function(x) x.ToString("X2")))
    End Function

    '==============================================================================================
    ' Nombres de record
    '==============================================================================================

    Public Function DescribirFormID(formID As UInteger) As String
        If formID = 0UI Then Return "(none)"
        Dim rec = _pluginManager.GetRecord(formID)
        If rec Is Nothing Then Return $"[{formID:X8}]"
        Dim edid = If(rec.EditorID <> "", rec.EditorID, rec.Header.Signature)
        Dim pluginSuffix = If(String.IsNullOrWhiteSpace(rec.SourcePluginName), "", $" @{rec.SourcePluginName}")
        Return $"{edid}  [{formID:X8}]{pluginSuffix}"
    End Function

    '==============================================================================================
    ' COBERTURA — qué sección se hace cargo de cada subrecord del NPC_
    '==============================================================================================

    ''' <summary>Qué firma de subrecord cae en qué sección del panel.
    '''
    ''' <para><b>Para qué está.</b> Este panel es el único lugar de la app que pretende mostrar el
    ''' record ENTERO, así que es el único que puede quedarse corto sin que nada se rompa: falta un
    ''' campo y el panel sigue andando. La lista de campos del NPC_ es CERRADA —la declara el esquema—,
    ''' así que se puede afirmar la cobertura en vez de suponerla: <c>Tools/DetalleDelRecordGate</c>
    ''' junta las firmas que los plugins del usuario TRAEN y exige que cada una esté acá. Una firma que
    ''' aparezca en el corpus y no esté declarada pone el gate en ROJO.</para>
    '''
    ''' <para><b>Los tres valores especiales.</b> Hay subrecords que no son un dato del NPC sino
    ''' andamiaje del formato, y decir que se «muestran» sería mentir:</para>
    ''' <list type="bullet">
    ''' <item><c>(contador)</c> — repite el largo de un arreglo que SÍ se muestra (SPCT, COCT, PRKZ,
    ''' KSIZ, CS2H). Mostrarlo aparte invita a leerlo como un dato independiente, y hay records del
    ''' master de Fallout 4 cuyo contador ya viene mal en el archivo.</item>
    ''' <item><c>(marcador)</c> — subrecord VACÍO que sólo marca el fin de un bloque (DATA, STOP,
    ''' CS2E, DSTF). No lleva payload.</item>
    ''' <item><c>(en su bloque)</c> — no tiene renglón propio porque se muestra DENTRO del renglón de
    ''' su elemento (COED con su ítem, TINC/TINV/TIAS con su capa).</item>
    ''' <item><c>(fuera del árbol)</c> — SÍ se muestra, pero el valor no sale del árbol del record.
    ''' Hoy sólo EDID: el panel escribe <c>NPC_Data.EditorID</c>, que la app cachea al parsear y que un
    ''' NPC materializado desde una plantilla conserva aunque su árbol venga de otro. Se marca aparte
    ''' porque la prueba por mutación del gate —sacar el subrecord y ver si el árbol cambia— no puede
    ''' verlo, y taparlo con un «se muestra» sería el mismo pase en vacío que la marca evita.</item>
    ''' <item><c>(relleno)</c> — el formato lo declara sin dato: el NAM7 de Fallout 4 es literalmente
    ''' «Unused» (en Skyrim la misma firma lleva el peso del cuerpo y ahí sí se muestra).</item>
    ''' </list>
    '''
    ''' <para>⛔ EL VALOR ES EL TÍTULO HASTA EL PRIMER « (»: las secciones se titulan «Traits  (own)»,
    ''' «Factions (3)  (own)», «Destructible (DEST, 2 stage(s))», y lo que se declara acá es la parte
    ''' estable de adelante. El gate compara así, y por eso el título de una sección no puede empezar
    ''' con un paréntesis que no esté acá.</para></summary>
    Public Shared ReadOnly Property Cobertura(juego As Config_App.Game_Enum) As IReadOnlyDictionary(Of String, String)
        Get
            Return If(juego = Config_App.Game_Enum.Skyrim, CoberturaSse, CoberturaFo4)
        End Get
    End Property

    Private Shared ReadOnly CoberturaComun As New Dictionary(Of String, String) From {
        {"EDID", "(fuera del árbol)"}, {"FULL", "NPC_"}, {"SHRT", "NPC_"},
        {"OBND", "Object Bounds"},
        {"TPLT", "Template Configuration"},
        {"ACBS", "Configuration"},
        {"RNAM", "Traits"}, {"WNAM", "Traits"}, {"VTCK", "Traits"}, {"NAM6", "Traits"}, {"NAM7", "Traits"},
        {"CNAM", "Stats"}, {"DNAM", "Stats"},
        {"SNAM", "Factions"},
        {"AIDT", "AI Data"},
        {"PKID", "AI Packages"}, {"DPLT", "AI Packages"},
        {"SPOR", "Override Package Lists"}, {"OCOR", "Override Package Lists"},
        {"GWOR", "Override Package Lists"}, {"ECOR", "Override Package Lists"},
        {"SPLO", "Actor Effects"},
        {"KWDA", "Keywords"},
        {"PRKR", "Perks"},
        {"DOFT", "Inventory"}, {"SOFT", "Inventory"}, {"CNTO", "Inventory"}, {"COED", "(en su bloque)"},
        {"FTST", "Appearance"}, {"HCLF", "Appearance"}, {"QNAM", "Appearance"}, {"PNAM", "Appearance"},
        {"ATKR", "Attacks"}, {"ATKD", "Attacks"}, {"ATKE", "Attacks"},
        {"DEST", "Destructible"}, {"DSTD", "Destructible"}, {"DMDL", "Destructible"},
        {"DMDS", "Destructible"}, {"DMDT", "Destructible"}, {"DSTF", "(marcador)"},
        {"CSCR", "Actor Sounds"},
        {"VMAD", "Scripts"},
        {"INAM", "Other"}, {"ZNAM", "Other"}, {"CRIF", "Other"}, {"GNAM", "Other"},
        {"ANAM", "Other"}, {"NAM8", "Other"}, {"NAM5", "Other"},
        {"SPCT", "(contador)"}, {"COCT", "(contador)"}, {"PRKZ", "(contador)"}, {"KSIZ", "(contador)"},
        {"DATA", "(marcador)"}
    }

    Private Shared ReadOnly CoberturaFo4 As IReadOnlyDictionary(Of String, String) = ConstruirCobertura(New Dictionary(Of String, String) From {
        {"PTRN", "Other"}, {"STCP", "Other"}, {"NTRM", "Other"}, {"FTYP", "Other"}, {"ATTX", "Other"},
        {"PFRN", "Other"},
        {"LTPT", "Template Configuration"}, {"LTPC", "Template Configuration"}, {"TPTA", "Template Configuration"},
        {"MWGT", "Traits"}, {"NAM4", "Traits"}, {"MRSV", "Traits"}, {"NAM7", "(relleno)"},
        {"PRPS", "Actor Values"},
        {"FCPL", "Override Package Lists"}, {"RCLR", "Override Package Lists"},
        {"BCLF", "Appearance"}, {"MSDK", "Appearance"}, {"MSDV", "Appearance"},
        {"TETI", "Appearance"}, {"TEND", "Appearance"}, {"FMRI", "Appearance"}, {"FMRS", "Appearance"},
        {"FMIN", "Appearance"},
        {"APPR", "Attach Parent Slots"},
        {"OBTE", "(contador)"}, {"OBTF", "Object Templates"}, {"OBTS", "Object Templates"},
        {"STOP", "(marcador)"},
        {"ATKW", "Attacks"}, {"ATKS", "Attacks"}, {"ATKT", "Attacks"},
        {"DAMC", "Destructible"}, {"DSTA", "Destructible"}, {"DMDC", "Destructible"},
        {"CS2H", "(contador)"}, {"CS2K", "Actor Sounds"}, {"CS2D", "Actor Sounds"},
        {"CS2E", "(marcador)"}, {"CS2F", "Actor Sounds"}
    })

    Private Shared ReadOnly CoberturaSse As IReadOnlyDictionary(Of String, String) = ConstruirCobertura(New Dictionary(Of String, String) From {
        {"NAM9", "Appearance"}, {"NAMA", "Appearance"},
        {"TINI", "Appearance"}, {"TINC", "(en su bloque)"}, {"TINV", "(en su bloque)"}, {"TIAS", "(en su bloque)"},
        {"CSDT", "Actor Sounds"}, {"CSDI", "Actor Sounds"}, {"CSDC", "(en su bloque)"}
    })

    Private Shared Function ConstruirCobertura(propias As Dictionary(Of String, String)) As IReadOnlyDictionary(Of String, String)
        Dim d As New Dictionary(Of String, String)(CoberturaComun, StringComparer.Ordinal)
        For Each kv In propias
            d(kv.Key) = kv.Value
        Next
        Return d
    End Function

    ''' <summary>Los valores de <see cref="Cobertura"/> que NO son un título de sección.</summary>
    Public Shared ReadOnly SinRenglonPropio As String() = {"(contador)", "(marcador)", "(en su bloque)", "(fuera del árbol)", "(relleno)"}

End Class
