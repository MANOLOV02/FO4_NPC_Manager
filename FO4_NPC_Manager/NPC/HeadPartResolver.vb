Imports System.Text
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>
''' Head-part resolution helpers for NPC_Manager. App-specific (not promoted to
''' FO4_Base_Library) — only NPC_Manager merges NPC.PNAM with RACE.HeadParts;
''' Wardrobe_Manager has no NPC concept.
'''
''' Public Shared so multiple call sites inside NPC_Manager can share the same
''' implementation without duplication. Today's callers: MainForm render path +
''' FaceGenBuilder.
''' </summary>
Public Module HeadPartResolver

    ''' <summary>Reconstruction of RaceCompatibility's runtime FormList injection (see
    ''' <see cref="RaceCompatibilityCatalog"/>), consulted by <see cref="IsHdptValidForRace"/> so the head-part
    ''' CATALOGS (picker, LooksMenu preset gate) offer what the game's chargen menu would offer for a custom race.
    ''' Built once per load order in MainForm alongside the other RaceMenu catalogs; Nothing / empty ⇒ no-op.
    ''' NOT used by the render or the bake: the engine does not filter worn head parts by RNAM at all.</summary>
    Public Property RaceCompatCatalog As RaceCompatibilityCatalog

    ''' <summary>El mensaje de las guardas de <c>res</c>. Una sola redacción para las cinco, porque el
    ''' motivo es el mismo y estaba por escribirse cinco veces.</summary>
    Private Const SinSede As String =
        "Sin ResolucionDeHeadParts no hay resolución: este módulo dejó de aceptar un " &
        "'pluginManager + Optional parseHdpt' justamente porque el delegado opcional resolvía sin " &
        "borradores en silencio. Para un camino sin borradores, la puerta es " &
        "ResolucionDeHeadParts.SinBorradores(plugins), que lo DECLARA." 

    ''' <summary>Merge NPC.PNAM head parts with RACE.HeadParts defaults per vanilla CK semantics.
    ''' Main types (1=Face, 2=Eyes, 3=Hair, 4=FacialHair, 5=Scar, 6=Eyebrows, 7=Meatcaps, 8=Teeth, 9=HeadRear):
    ''' NPC override wins; fall back to RACE default per type (gender-specific).
    ''' Type 0 Misc: should only appear as extras inside each main HDPT's HNAM; freestanding top-level
    ''' type=0 entries (rare/undocumented in vanilla) are preserved as additive to avoid data loss.
    ''' RACE.HeadParts per gender: Head Part\HEAD (con su propio INDX), declarado en la interfaz de cada
    ''' juego con su propia colección — RaceFO4.MaleHeadParts/FemaleHeadParts, RaceSSE.HeadParts/HeadParts2.</summary>
    ''' <param name="res">LA SEDE de resolución de head parts (<see cref="ResolucionDeHeadParts"/>), que
    ''' trae el orden de carga Y los borradores. ⛔ Es OBLIGATORIA y reemplazó al par
    ''' <c>pluginManager + Optional parseHdpt</c>: con el delegado opcional, un llamador que se olvidaba
    ''' de pasarlo resolvía sin borradores y el head part del usuario desaparecía sin un error. Además
    ''' el par permitía pasar un <c>pluginManager</c> y un parser cerrado sobre OTRO.</param>
    ''' <param name="parseRace">Parser de RACE cacheado, opcional. La RACE no es un head part: su caché
    ''' vive en <c>NpcRenderContext</c>, no en la sede de head parts.</param>
    Public Function MergeHeadPartsWithRaceDefaults(raceFormID As UInteger,
                                                   isFemale As Boolean,
                                                   npcHeadPartFormIDs As IReadOnlyList(Of UInteger),
                                                   res As ResolucionDeHeadParts,
                                                   Optional parseRace As Func(Of PluginRecord, Canon.IRace) = Nothing) As List(Of UInteger)
        If res Is Nothing Then Throw New ArgumentNullException(NameOf(res), SinSede)
        Dim safeNpcParts As IReadOnlyList(Of UInteger) = If(npcHeadPartFormIDs, CType(New List(Of UInteger)(), IReadOnlyList(Of UInteger)))
        If raceFormID = 0UI Then Return safeNpcParts.ToList()
        Dim raceRec = res.Plugins.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then
            Return safeNpcParts.ToList()
        End If
        Dim race = If(parseRace IsNot Nothing, parseRace(raceRec), Canon.CanonRecords.Race(raceRec, res.Plugins))
        ' La ley de "que head parts trae la raza para este genero" vive en UN solo lugar
        ' (CanonInterpretacion.HeadPartsDe): contempla los dos juegos y sus nombres propios. Aca estaba
        ' copiada a mano, asi que el dia que se corrija alla esta copia se queda vieja en silencio.
        Dim raceDefaults = Canon.CanonInterpretacion.HeadPartsDe(race, isFemale)

            Dim fuentes As New List(Of Canon.FuenteDePartes) From {
                New Canon.FuenteDePartes("raza", raceDefaults, False),
                New Canon.FuenteDePartes("npc", safeNpcParts, False)
            }
            ' La resolución va por la SEDE: así el render, el bake, el guardado y el picker ven lo mismo,
            ' incluidos los borradores. Antes esto era un lambda local que iba derecho al PluginManager.
            Return Canon.ResolverPartesDeCabeza(fuentes, AddressOf res.Hdpt)
    End Function

    ''' <summary>Si <paramref name="hdptFormID"/> es valido para un NPC de <paramref name="raceFormID"/>. Pasa
    ''' cuando: (a) HDPT.RNAM = 0 y la RACE destino declara head parts, o sea que es humanoide; (b) el RNAM
    ''' apunta a una FLST que contiene la raza; o (c) la RACE nombra al HDPT como default de genero.
    ''' <para>El camino (a) exige que la RACE tenga head parts porque RNAM=0 no es un pase universal: hay NPCs no
    ''' humanoides (perros) cuyo PNAM lista head parts humanas con RNAM=0 que el motor no dibuja, justamente
    ''' porque su raza declara cero head parts.</para>
    ''' <para><paramref name="raceHasAnyHeadParts"/> lo provee el caller.</para>
    ''' <para>⛔ Acá vivía un parámetro <c>flstCache As Dictionary(...)</c> que el caller tenía que
    ''' compartir entre llamadas, y <b>memoizaba el FRACASO</b>: <c>flstCache(hdpt.ValidRaces) = flst</c>
    ''' se ejecutaba también cuando la FLST no resolvía. Con un <c>RNAM</c> apuntando a una FLST
    ''' BORRADOR eso guardaba <c>Nothing</c>, el head part se filtraba, y <b>seguía filtrándose aunque
    ''' el usuario guardara la FLST</b>, porque esa caché no se invalidaba nunca. La caché se mudó
    ''' adentro de <see cref="ResolucionDeHeadParts"/>, donde el borrador se consulta ANTES y donde hay
    ''' una invalidación con dueño.</para></summary>
    Public Function IsHdptValidForRace(hdptFormID As UInteger,
                                       raceFormID As UInteger,
                                       isFemale As Boolean,
                                       res As ResolucionDeHeadParts,
                                       Optional raceDefaults As HashSet(Of UInteger) = Nothing,
                                       Optional raceHasAnyHeadParts As Boolean = True) As Boolean
        If res Is Nothing Then Throw New ArgumentNullException(NameOf(res), SinSede)
        If hdptFormID = 0UI Then Return False
        Dim hdpt = res.Hdpt(hdptFormID)
        If hdpt Is Nothing Then Return False
        Return IsHdptValidForRace(hdpt, hdptFormID, raceFormID, isFemale, res, raceDefaults, raceHasAnyHeadParts)
    End Function

    ''' <summary>La MISMA ley, para quien YA TIENE la vista del record en la mano.
    ''' <para>⛔ Existe porque preguntar por FormID obliga a que el record esté RESUELTO, y el editor de
    ''' head parts tiene casos donde no lo está: un «New (blank)» no se registra hasta que el usuario
    ''' escribe algo —a propósito, para no dejar borradores basura—, así que la sede devolvía Nothing y el
    ''' panel de validez informaba «RNAM includes race ....... NO» sobre un record cuyo RNAM es 0, que
    ''' PASA. Con la vista en la mano no hay resolución que pueda fallar.</para>
    ''' <para>El FormID sigue haciendo falta: el camino (c) pregunta si la RACE lo declara como default
    ''' de género, y eso se contesta por identidad, no por contenido.</para></summary>
    Public Function IsHdptValidForRace(hdpt As Canon.IHdpt,
                                       hdptFormID As UInteger,
                                       raceFormID As UInteger,
                                       isFemale As Boolean,
                                       res As ResolucionDeHeadParts,
                                       Optional raceDefaults As HashSet(Of UInteger) = Nothing,
                                       Optional raceHasAnyHeadParts As Boolean = True) As Boolean
        If res Is Nothing Then Throw New ArgumentNullException(NameOf(res), SinSede)
        If hdpt Is Nothing Then Return False

        ' Path (a): no race restriction declared. Pass only if the RACE itself uses head parts
        ' (humanoid). Non-humanoid races (dog/robot/creature) drop RNAM=0 HDPTs even though
        ' a buggy NPC.PNAM might list one — engine-faithful behavior.
        If hdpt.ValidRaces = 0UI Then Return raceHasAnyHeadParts

        ' Path (b): RNAM points to a FLST and the FLST contains the target race. La FLST la resuelve la
        ' SEDE (borrador primero, caché adentro): ver el ⛔ del sumario sobre el fracaso memoizado.
        Dim flst As Canon.IFlst = res.Flst(hdpt.ValidRaces)
        If flst IsNot Nothing AndAlso flst.Miembros().Contains(raceFormID) Then Return True

        ' Path (b'): the FormList as the GAME would have it. RaceCompatibility's proxyRaces script INSERTS a mod's
        ' custom races into the vanilla head-part FormLists at runtime (once, on OnInit) — nothing of that is ever
        ' written to a plugin, so the record says "not a member" while the game's own chargen menu says it is. The
        ' catalog reconstructs that insertion from the QUST's VMAD + the mod's compiled script. Without it every
        ' custom-race NPC (COtR & co) would be offered ONLY its own mod's head parts and not a single vanilla hair.
        ' Empty catalog (no such mod installed, or FO4) ⇒ this is a no-op and the filter behaves exactly as before.
        If RaceCompatCatalog IsNot Nothing AndAlso RaceCompatCatalog.ContainsRace(hdpt.ValidRaces, raceFormID) Then Return True

        ' Path (c): the NPC's RACE record declares this HDPT as a gender-default.
        If raceDefaults IsNot Nothing AndAlso raceDefaults.Contains(hdptFormID) Then Return True

        Return False
    End Function

    ''' <summary>Si un preset de LooksMenu es compatible con la raza del NPC destino. La compatibilidad la
    ''' deciden SOLO las head parts: todas tienen que pasar <see cref="IsHdptValidForRace"/> (conjunto vacio =
    ''' compatible).
    ''' <para>Las capas de FaceTint NO gatean: un tint cuyo Index no resuelve contra esta raza no es motivo para
    ''' ocultar el preset entero. Se conserva verbatim en el NPC (round-trippea al guardar) pero queda inerte, el
    ''' compositor lo saltea y el editor esconde su fila. Las head parts si gatean porque un HDPT de otra raza
    ''' cambiaria una malla entera (pelo, ojos), que es el apply parcial que el usuario si quiere ocultar.</para>
    ''' <para><paramref name="ignoreFaceBaseHeadPart"/> (camino SSE): saltea la head part base de cara. Los
    ''' presets .jslot de RaceMenu traen la cabeza base especifica de la raza del autor, que legitimamente falla
    ''' contra otra raza, pero skee aplica el sculpt sobre la cabeza base de la raza propia: si eso descartara el
    ''' preset, casi todos los cross-race desaparecerian del browser. Pelo, ojos y cejas siguen gateando; FO4 lo
    ''' deja en False.</para></summary>
    Public Function IsPresetCompatibleWithRace(preset As LooksmenuLoader.LooksmenuPreset,
                                               raceFormID As UInteger,
                                               isFemale As Boolean,
                                               res As ResolucionDeHeadParts,
                                               race As Canon.IRace,
                                               raceDefaults As HashSet(Of UInteger),
                                               Optional ignoreFaceBaseHeadPart As Boolean = False) As Boolean
        If res Is Nothing Then Throw New ArgumentNullException(NameOf(res), SinSede)
        If preset Is Nothing Then Return False

        ' Diagnostic: when the logger is on, record the concrete reason a preset is judged
        ' race-incompatible (which HDPT). Gated + lazy so it's a no-op with logging off.
        ' presetName is captured once for the lambdas below.
        Dim presetName As String = IO.Path.GetFileName(preset.SourcePath)

        ' HeadPart compatibility — every declared HDPT must be valid for the target race.
        ' (Tints are deliberately NOT checked here — see the summary above.)
        ' "Declared" = lo que el ARCHIVO trae: las aplicadas (HeadPartFormIDs) MÁS las que el mapper SSE resolvió
        ' pero el motor no aplicaría a este actor (SseHeadPartsFiltradasPorMotor, skee64 PresetInterface.cpp:164-175).
        ' Sin la segunda lista, un .jslot de otra raza pasaría este gate justo porque el motor le descarta el pelo:
        ' el browser lo listaría como compatible y el usuario lo cargaría para verlo aplicar nada.
        Dim declaradas As IEnumerable(Of UInteger) = If(preset.HeadPartFormIDs, Enumerable.Empty(Of UInteger)())
        If preset.SseHeadPartsFiltradasPorMotor IsNot Nothing AndAlso preset.SseHeadPartsFiltradasPorMotor.Count > 0 Then
            declaradas = declaradas.Concat(preset.SseHeadPartsFiltradasPorMotor)
        End If
        If declaradas IsNot Nothing Then
            For Each fid In declaradas
                If fid = 0UI Then Continue For
                ' SSE: don't let the race-specific base HEAD (Face, PartType=1) gate the preset.
                If ignoreFaceBaseHeadPart Then
                    Dim hd = res.Hdpt(fid)
                    If hd IsNot Nothing Then
                        If hd.TipoDeParte() = 1 Then
                            Dim fidFace = fid
                            Logger.LogLazy(Function() $"[LMLoad] '{presetName}': skipping base-head (Face) HDPT 0x{fidFace:X8} from race-compat gate (SSE — skee applies the preset sculpt over the NPC's own base head).")
                            Continue For
                        End If
                    End If
                End If
                If Not IsHdptValidForRace(fid, raceFormID, isFemale, res, raceDefaults) Then
                    Dim fidLocal = fid
                    Logger.LogLazy(Function() $"[LMLoad] DROP '{presetName}' as race-incompatible: HDPT 0x{fidLocal:X8} not valid for race 0x{raceFormID:X8} (gender={If(isFemale, "F", "M")}). HDPT's RACE/FLST does not list this race and it is not a race gender-default.")
                    Return False
                End If
            Next
        End If

        Logger.LogLazy(Function() $"[LMLoad] KEEP '{presetName}' as race-compatible for race 0x{raceFormID:X8} (gender={If(isFemale, "F", "M")}). (HeadParts OK; unresolved tint layers, if any, are preserved verbatim but not applied/editable.)")
        Return True
    End Function

    ''' <summary>Precompute the Misc(0) -> parent-effective-type promotion over a set of root
    ''' HDPTs: if a root HDPT is declared as a Misc(0) HNAM extra of another root whose type is
    ''' non-zero, it inherits that parent's type even when visited at top level. Order-independent.
    ''' Single source of truth shared by the render candidate walk (MainForm.CollectHeadPartCandidates)
    ''' and <see cref="EnumerateHdptChain"/>.</summary>
    Public Function BuildMiscToParentEffective(rootFormIDs As IEnumerable(Of UInteger),
                                               res As ResolucionDeHeadParts) As Dictionary(Of UInteger, Integer)
        Dim result As New Dictionary(Of UInteger, Integer)
        If res Is Nothing Then Throw New ArgumentNullException(NameOf(res), SinSede)
        If rootFormIDs Is Nothing Then Return result
        Dim parsed As New Dictionary(Of UInteger, Canon.IHdpt)
        For Each fid In rootFormIDs
            If fid = 0UI OrElse parsed.ContainsKey(fid) Then Continue For
            Dim hd = res.Hdpt(fid)
            If hd IsNot Nothing Then parsed(fid) = hd
        Next
        For Each parentKv In parsed
            Dim parentEff = parentKv.Value.TipoDeParte()
            If parentEff = 0 Then Continue For
            If parentKv.Value.PartesExtra() Is Nothing Then Continue For
            For Each extraFid In parentKv.Value.PartesExtra()
                Dim extraData As Canon.IHdpt = Nothing
                If Not parsed.TryGetValue(extraFid, extraData) Then Continue For
                If extraData.TipoDeParte() <> 0 Then Continue For
                If Not result.ContainsKey(extraFid) Then result(extraFid) = parentEff
            Next
        Next
        Return result
    End Function

    ''' <summary>Per-node effective-type rule: the HDPT's own PartType, unless it is Misc(0) — then
    ''' inherit the parent's effective type (HNAM cascade, <paramref name="parentPartType"/> &gt;= 0),
    ''' or the precomputed top-level promotion (<paramref name="parentPartType"/> &lt; 0). Single
    ''' source of truth shared by the render walk and <see cref="EnumerateHdptChain"/>.</summary>
    Public Function ResolveEffectivePartType(ownPartType As Integer,
                                             parentPartType As Integer,
                                             hdptFormID As UInteger,
                                             miscToParentEffective As Dictionary(Of UInteger, Integer)) As Integer
        If ownPartType <> 0 Then Return ownPartType
        If parentPartType >= 0 Then Return parentPartType
        If miscToParentEffective IsNot Nothing Then
            Dim promoted As Integer = 0
            If miscToParentEffective.TryGetValue(hdptFormID, promoted) Then Return promoted
        End If
        Return ownPartType
    End Function

    ''' <summary>Borra en cascada los hijos Misc(0) standalone que quedaron HUERFANOS al remover o reemplazar una
    ''' head part. Un Misc que vivia en el HNAM del padre removido queda huerfano: su tipo efectivo colapsa a
    ''' Misc(0), no le aplica paleta de pelo ni de barba, y se dibuja con el color por defecto del BGSM. Se
    ''' descartan, EXCEPTO los que todavia reclame otro padre presente (incluido un padre de reemplazo que
    ''' comparta el extra, asi que un hairline declarado por el pelo viejo y el nuevo sobrevive). Los extras que
    ''' no son Misc, y los Misc que nunca estuvieron en el HNAM del padre removido (addons independientes como la
    ''' sombra de boca o el AO/wet), no se tocan.
    ''' <para>Fuente unica de dos callers, para que un cambio de pelo por preset descarte el hairline viejo
    ''' EXACTAMENTE igual que el editor manual. El caller del saver DEBE gatear en "el preset realmente reemplazo
    ''' al padre de ese PartType": pasar un padre sin cambios no haria nada, pero el gate evita siquiera
    ''' considerar un extra cuyo padre quedo intacto.</para>
    ''' <para><paramref name="resolveHdpt"/> mapea FormID a su <see cref="Canon.IHdpt"/> parseado; cada caller pasa
    ''' su propia cache.</para></summary>
    Public Sub CascadeRemoveOrphanedHnamMisc(headParts As List(Of UInteger),
                                             removedParentFid As UInteger,
                                             resolveHdpt As Func(Of UInteger, Canon.IHdpt))
        If headParts Is Nothing OrElse resolveHdpt Is Nothing Then Return
        Dim removedHdpt = resolveHdpt(removedParentFid)
        If removedHdpt Is Nothing Then Return
        If removedHdpt.TipoDeParte() = 0 Then Return   ' a Misc has no HNAM children to orphan
        If removedHdpt.PartesExtra() Is Nothing OrElse removedHdpt.PartesExtra().Count = 0 Then Return

        Dim extras As New HashSet(Of UInteger)(removedHdpt.PartesExtra())
        ' If another head part still in the list declares one of these extras in its HNAM, it's a live
        ' HNAM child of that parent — keep it (covers a hairline shared by the old and new hair).
        Dim claimedByOtherParent As New HashSet(Of UInteger)
        For Each otherFid In headParts
            If otherFid = removedParentFid Then Continue For
            Dim otherHdpt = resolveHdpt(otherFid)
            If otherHdpt Is Nothing OrElse otherHdpt.PartesExtra() Is Nothing Then Continue For
            For Each ex In otherHdpt.PartesExtra()
                If extras.Contains(ex) Then claimedByOtherParent.Add(ex)
            Next
        Next
        For i = headParts.Count - 1 To 0 Step -1
            Dim fid = headParts(i)
            If Not extras.Contains(fid) Then Continue For
            If claimedByOtherParent.Contains(fid) Then Continue For
            Dim extraHdpt = resolveHdpt(fid)
            If extraHdpt Is Nothing OrElse extraHdpt.TipoDeParte() <> 0 Then Continue For
            headParts.RemoveAt(i)
        Next
    End Sub

    ''' <summary>Dadas las head parts crudas del NPC.PNAM y las de un preset, devuelve los FormID de Misc
    ''' standalone que quedan HUERFANOS porque el preset reemplazo a su padre: es el conjunto que un apply (Load
    ''' LooksMenu/RaceMenu, Copy/Paste) tiene que registrar en
    ''' <see cref="LooksmenuLoader.LooksmenuPreset.SuppressedRawHeadPartFormIDs"/> para que la union cruda del
    ''' guardado los descarte, igual que hace Edit Face en un cambio manual de pelo.
    ''' <para>Mergea crudo y preset igual que persiste el saver (un HDPT por tipo principal, gana el preset; los
    ''' Misc se acumulan) y, por cada padre de tipo principal que el preset REEMPLAZO por otro HDPT, junta los
    ''' hijos Misc huerfanos de ese padre crudo via <see cref="CascadeRemoveOrphanedHnamMisc"/>. Asi el guardado
    ''' coincide con el render, que ya rearma las head parts como defaults de raza + preset.</para>
    ''' <para>No es la regresion de las pestanas: aquella venia de filtrar los extras crudos SIN CONDICION,
    ''' mientras que esto solo se dispara ante un reemplazo real y la cascada conserva todo extra que siga
    ''' reclamando un padre vivo.</para></summary>
    Public Function ComputeReplacedParentOrphanMisc(rawParts As IEnumerable(Of UInteger),
                                                    presetParts As IEnumerable(Of UInteger),
                                                    resolveHdpt As Func(Of UInteger, Canon.IHdpt)) As HashSet(Of UInteger)
        Dim result As New HashSet(Of UInteger)
        If rawParts Is Nothing OrElse presetParts Is Nothing OrElse resolveHdpt Is Nothing Then Return result

            Dim fuentes As New List(Of Canon.FuenteDePartes) From {
                New Canon.FuenteDePartes("crudo", rawParts.ToList(), False),
                New Canon.FuenteDePartes("preset", presetParts.ToList(), False)
            }
            Dim finalFlat = Canon.ResolverPartesDeCabeza(fuentes, resolveHdpt)
            Dim sobrevive As New HashSet(Of UInteger)(finalFlat)

            For Each fid In rawParts
                If fid = 0UI Then Continue For
                Dim hdRaw = resolveHdpt(fid)
                If hdRaw Is Nothing Then Continue For
                If hdRaw.ClasificarHeadPart(False).Clase <> Canon.ClaseDeHeadPart.Slot Then Continue For
                If sobrevive.Contains(fid) Then Continue For
                Dim before As New HashSet(Of UInteger)(finalFlat)
                CascadeRemoveOrphanedHnamMisc(finalFlat, fid, resolveHdpt)
                before.ExceptWith(finalFlat)
                result.UnionWith(before)
            Next
            Return result
    End Function

    ''' <summary>LOS CUATRO FILTROS DEL SELECTOR, en un solo lugar y devueltos por separado.
    '''
    ''' <para>⛔⛔ Estaban escritos TRES veces: la vuelta del orden de carga y la de los borradores
    ''' en <c>HeadPartPicker_Form.BuildCandidates</c>, y una tercera en el panel de validez del
    ''' editor de head parts. Y las tres NO decían lo mismo: el panel no contaba el filtro del TIPO
    ''' y aplicaba el de «es un extra» SIN la excepción de Misc, así que podía prometer que un
    ''' record iba a aparecer en un selector que lo filtraba, y al revés. El usuario no tiene forma
    ''' de saber cuál de las tres le está hablando.</para>
    '''
    ''' <para>⛔ DEVUELVE LOS CUATRO, no un «sirve / no sirve». El panel existe para decir CUÁL falla
    ''' — pinta una línea por filtro —, así que colapsarlos en un booleano obligaría al panel a
    ''' recalcular por su cuenta y volvería la divergencia por la puerta de al lado. El selector, que
    ''' sólo necesita el «Y», usa <see cref="FiltrosDePicker.Pasa"/>.</para>
    '''
    ''' <para>⛔ EL FILTRO DE «ES UN EXTRA» NO APLICA A MISC, y eso no es una excepción cómoda: Misc
    ''' es exactamente donde viven los HDPT de tipo agregado en vanilla (pestañas, AO, wet, hairlines,
    ''' sombra de boca), y el usuario que abrió «+Misc» pidió esa lista. Para los tipos con slot, el
    ''' usuario elige el PADRE y el motor arrastra sus <c>HNAM</c>.</para></summary>
    Public Structure FiltrosDePicker
        ''' <summary><c>HDPT.PartType</c> coincide con el tipo pedido.</summary>
        Public TipoOk As Boolean
        ''' <summary>No es un extra <c>HNAM</c>. Siempre True para el tipo 0 (Misc).</summary>
        Public NoEsExtraOk As Boolean
        ''' <summary>Declara el bit del género pedido, o no declara ninguno (universal).</summary>
        Public GeneroOk As Boolean
        ''' <summary>La raza le entra por <c>RNAM</c> o por los defaults de la RACE.</summary>
        Public RazaOk As Boolean
        ''' <summary>Los cuatro. Es lo que usa el selector.</summary>
        Public ReadOnly Property Pasa As Boolean
            Get
                Return TipoOk AndAlso NoEsExtraOk AndAlso GeneroOk AndAlso RazaOk
            End Get
        End Property
    End Structure

    ' ⛔⛔ ACÁ HABÍA TRES CONSTANTES CON LOS BITS DE `DATA` — `BitMacho`, `BitHembra`,
    ' `BitEsExtra` — y el comentario decía «escritos una vez». Era falso y lo escribí yo: saqué la
    ' copia que vivía en el formulario del selector y CREÉ OTRA acá, mientras una tercera seguía
    ' viva en `MainForm`. Tres deletreos del mismo bit.
    '   Y sobraban las tres: **el esquema los declara con nombre** — `FlagsMale`, `FlagsFemale`,
    ' `FlagsIsExtraPart` en la interfaz generada, con su doc «Bit 3 de DATA\Flags: Is Extra Part» —
    ' y todo el resto del árbol los usa así. Una tabla a mano que repite lo que el formato declara
    ' es duplicación CON EL ESQUEMA, que es la peor de las tres: el día que el esquema cambie, la
    ' copia sigue contestando el valor viejo y nadie se entera.

    ''' <summary>Los cuatro filtros para un head part concreto. <paramref name="tipoPedido"/> es el
    ''' slot que el usuario abrió (0 = el balde Misc).</summary>
    ''' <param name="hdptFormID">La identidad con la que preguntar por la raza. Va aparte de
    ''' <paramref name="hdpt"/> porque un borrador nuevo tiene el encabezado del record en CERO y la
    ''' sede lo indexa por el FormID del borrador.</param>
    Public Function FiltrarParaPicker(hdpt As Canon.IHdpt, hdptFormID As UInteger,
                                      tipoPedido As Integer, raceFormID As UInteger,
                                      isFemale As Boolean, res As ResolucionDeHeadParts,
                                      Optional raceDefaults As HashSet(Of UInteger) = Nothing) As FiltrosDePicker
        Dim f As New FiltrosDePicker()
        If hdpt Is Nothing Then Return f
        f.TipoOk = (hdpt.TipoDeParte() = tipoPedido)
        f.NoEsExtraOk = (tipoPedido = 0) OrElse Not hdpt.FlagsIsExtraPart
        Dim hayMacho = hdpt.FlagsMale
        Dim hayHembra = hdpt.FlagsFemale
        ' Sin ningún bit se trata como universal: hay mods que los dejan los dos en cero.
        f.GeneroOk = (Not hayMacho AndAlso Not hayHembra) OrElse If(isFemale, hayHembra, hayMacho)
        f.RazaOk = IsHdptValidForRace(hdpt, hdptFormID, raceFormID, isFemale, res, raceDefaults)
        Return f
    End Function

    ''' <summary>One yielded entry of <see cref="EnumerateHdptChain"/>: the parsed HDPT plus the
    ''' EFFECTIVE part type. Effective type = the HDPT's own PartType, except a Misc(0) sub-part
    ''' reached through a parent's HNAM inherits the parent's type (a hair Hairline, HDPT
    ''' PartType=Misc, becomes effective type Hair=3). This is the single source of truth for the
    ''' rule the render applies inline in <c>MainForm.CollectHeadPartCandidate</c>; callers that
    ''' need to color/treat a sub-part like its parent (e.g. hair palette on a hairline) must use
    ''' <see cref="EffectivePartType"/>, not <c>Hdpt.PartType</c>.</summary>
    Public Class HdptChainEntry
        Public Property Hdpt As Canon.IHdpt
        Public Property EffectivePartType As Integer
        ''' <summary>El FormID que el recorrido CAMINO para llegar a este nodo.
        ''' <para>⛔⛔ NO usar <c>Hdpt.FormID</c> para identificar la entrada: el FormID del ENCABEZADO de
        ''' un borrador NUEVO es <b>CERO</b>. Es el contrato compartido por las SIETE clases de borrador
        ''' —<c>Draft.FormID</c> es la identidad y el writer estampa el real al emitir—, no una
        ''' particularidad de HDPT.</para>
        ''' <para>⛔ EL DOC DECÍA «hoy ningún consumidor de la app lo lee» Y ERA FALSO. El censo: el
        ''' horneado indexa por <c>EditorID</c> y el de morphs por clave de malla, pero
        ''' <c>FO4_FaceTint_CLI</c> identificaba cada entrada por el FormID del encabezado, y tres
        ''' arneses también (<c>HairlineDupProbe</c> ×2, su <c>SelfTest</c>, <c>HairTintGateProbe</c>).
        ''' Los cinco pasaron a <c>Fid</c>. La divergencia era LATENTE — ninguno de esos caminos ve
        ''' borradores de sesión —, pero «nadie lo lee» era una afirmación sin censo. El primer intento de <c>Tools\HdptBorradorRenderGate</c> se la comió y
        ''' reportó «la cadena no recorre el borrador» cuando sí lo recorría, con FormID 0.</para></summary>
        Public Property Fid As UInteger
    End Class

    ''' <summary>Expansion BFS de una cadena de HDPT por <c>ExtraPartFormIDs</c> (extras HNAM). Devuelve cada
    ''' HDPT alcanzable (con su tipo efectivo) desde <paramref name="rootFormIDs"/>, incluidos los propios roots.
    ''' Los ciclos se cortan con un visited-set y los records que no son HDPT o no parsean se saltean.
    ''' <para>Los HDPT vanilla usan HNAM para colgar sub-partes tecnicas (pestanas, AO/wet, hairlines, sombra de
    ''' boca, dientes), asi que cualquier cosa que quiera "dibujar el mismo conjunto de shapes que el motor"
    ''' necesita esta expansion: la malla del padre sola esta incompleta.</para>
    ''' <para>Regla de tipo efectivo (espeja el recorrido del render): un sub-part Misc(0) hereda el tipo efectivo
    ''' del padre que lo alcanzo por HNAM, y un Misc de primer nivel que ADEMAS es extra HNAM de otro root se
    ''' promueve al tipo de ese padre, asi que el resultado no depende del orden.</para></summary>
    Public Iterator Function EnumerateHdptChain(rootFormIDs As IEnumerable(Of UInteger),
                                                res As ResolucionDeHeadParts) As IEnumerable(Of HdptChainEntry)
        If res Is Nothing Then Throw New ArgumentNullException(NameOf(res), SinSede)
        If rootFormIDs Is Nothing Then Return
        Dim roots = rootFormIDs.Where(Function(f) f <> 0UI).ToList()

        ' Shared precompute (also used by the render walk) so the effective-type rule lives once.
        Dim miscToParentEffective = BuildMiscToParentEffective(roots, res)

        Dim visited As New HashSet(Of UInteger)
        ' Queue of (FormID, parent effective type). Roots carry parentEff = -1.
        Dim queue As New Queue(Of (Fid As UInteger, ParentEff As Integer))
        For Each fid In roots
            queue.Enqueue((fid, -1))
        Next
        While queue.Count > 0
            Dim item = queue.Dequeue()
            Dim fid = item.Fid
            If Not visited.Add(fid) Then Continue While
            Dim hdpt = res.Hdpt(fid)
            If hdpt Is Nothing Then Continue While

            ' Effective type via the shared rule (same one the render walk uses).
            Dim effectiveType = ResolveEffectivePartType(hdpt.TipoDeParte(), item.ParentEff, fid, miscToParentEffective)

            Yield New HdptChainEntry With {.Hdpt = hdpt, .EffectivePartType = effectiveType, .Fid = fid}

            ' Children inherit this node's effective type (so a hairline under hair stays Hair).
            Dim childParentEff = If(effectiveType <> 0, effectiveType, item.ParentEff)
            If hdpt.PartesExtra() IsNot Nothing Then
                For Each extraFid In hdpt.PartesExtra()
                    If extraFid <> 0UI Then queue.Enqueue((extraFid, childParentEff))
                Next
            End If
        End While
    End Function
End Module
