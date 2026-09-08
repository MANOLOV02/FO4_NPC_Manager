Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Hace que un NPC que hereda por plantilla PASE A SER DUENIO de una categoria de template-flag, para
''' que una edicion en esa categoria sobreviva in-game en vez de que la pise la resolucion de plantillas.
''' <para>POR QUE (RE verificado sobre el motor, ver 40-bake-reglas-comunes): al resolver el NPC, el motor corre
''' <c>CopyFromTemplate</c> y, por cada categoria cuyo flag Use-X esta puesto, copia los campos de la PLANTILLA
''' POR ENCIMA de los propios del NPC: gana la plantilla, por categoria, sin merge aditivo. Con el flag en claro
''' la copia se saltea y se usan los campos propios. Asi que para volver una categoria editable hay que hacer las
''' DOS cosas: bajar su bit Use-X Y escribir los valores resueltos de la cadena en el record del NPC - si no,
''' bajar el flag dejaria esos campos vacios y rompeia el aspecto.</para>
''' <para>USO: el caller ordena MATERIALIZAR, BAJAR EL FLAG y despues APLICAR LA EDICION. Es OPT-IN y no toca el
''' camino normal de guardado; un NPC que no hereda la categoria vuelve intacto.</para>
''' <para>⛔ LA INVARIANTE QUE MANTIENE ESTO HONESTO: todo campo que lleve <see cref="MainForm.TraitsState"/>
''' tiene que materializarse aca. Ese state ES el modelo de la app de "lo que aporta la cadena de Traits", asi que
''' un campo presente alla y ausente aca se pierde EN SILENCIO apenas se baja el bit, y el sintoma aparece recien
''' al renderizar, hornear o guardar. FTST, QNAM y APPR fueron exactamente ese agujero (medido: 52 NPCs de SSE
''' perdian un valor real, 0 en FO4 - latente, no ausente). Al agregar un campo a TraitsState, agregarlo aca en el
''' MISMO commit.</para>
''' <para>Alignment y Weapon List siguen fuera del modelo. Las categorias soportadas se materializan completas;
''' una categoria no soportada falla cerrada y conserva su flag de herencia.</para></summary>
Friend NotInheritable Class NpcTemplateMaterializer

    Private Sub New()
    End Sub

    ''' <summary>Traits sub-fields the app does not model yet, so they are not materialized here. Kept as a
    ''' visible TODO surface (and for a caller that wants to warn the user).</summary>
    Friend Shared ReadOnly UnmodeledTraitsFields As String() = {"Alignment", "Weapon List"}

    ''' <summary>Guard against a cyclic template chain (a resolves to b resolves to a). The engine's walk
    ''' has a cycle check; ours bounds the depth.</summary>
    Private Const MaxChainDepth As Integer = 32
    ''' <summary>Make <paramref name="npc"/> own <paramref name="category"/>: materialize the resolved
    ''' template-chain values for that category into the NPC's own fields, then clear the Use-X flag bit.
    ''' No-op (returns False) when the NPC does not inherit the category (flag already clear) — its own
    ''' data already wins. Mutates <paramref name="npc"/> in place.</summary>
    ''' <param name="getParsedNpc">Resolver: FormID → parsed <see cref="NPC_Data"/> (Nothing if absent).
    ''' Decoupled from PluginManager so this stays unit-testable.</param>
    ''' <param name="resolveLvlnPick">Optional: LVLN FormID → the leaf NPC_ FormID to PIN this actor to.
    ''' Only consulted for Traits, and only when the chain hits a record <paramref name="getParsedNpc"/>
    ''' can't return — i.e. a leveled list. Pinning one leaf is the WHOLE POINT of the editor: a generic
    ''' actor is being turned into a concrete one, and the engine's per-spawn re-roll is exactly what the
    ''' user is choosing to replace. The caller decides WHICH leaf (MainForm hands back the one currently
    ''' being previewed, so the NPC the user edits is the NPC they were looking at). Nothing (CLI / probes)
    ''' leaves the chain unresolvable.</param>
    ''' <returns>What actually happened — see <see cref="MaterializeOutcome"/>.</returns>
    ''' <summary>Hace propia una categoria a partir de una resolucion YA HECHA.
    ''' <para>⛔ La cadena NO se resuelve aca: se resuelve UNA vez, en la sede del desprendimiento, y
    ''' viaja congelada. Volver a resolverla al guardar daria otra respuesta si el arbol cambio entre
    ''' medio, y esa segunda respuesta no la eligio nadie.</para>
    ''' <para>⛔ SE CONSERVA la guarda de bandera: es la que devuelve `NotInheriting` y la que hace que
    ''' los llamadores no necesiten guarda propia para un NPC que no hereda.</para>
    ''' <para>⛔ `skipOverlayOwned` ya no existe. Antes el overlay se estampaba ANTES de materializar,
    ''' asi que habia que saltear lo que el overlay habia puesto — con un booleano GRUESO por NPC: un
    ''' overlay de solo CUERPO hacia saltear TODA la cara. Ahora se materializa primero y el overlay
    ''' se estampa despues, asi que gana por llegar ultimo, campo por campo.</para></summary>
    Friend Shared Function MakeCategoryOwn(npc As NPC_Data,
                                           category As NPC_TemplateCategory,
                                           resolution As TraitsResolution) As MaterializeOutcome
        If npc Is Nothing OrElse Not NpcTemplateHelpers.HasTemplateFlag(
               npc.Record.ConfigurationTemplateFlags, category) Then Return MaterializeOutcome.NotInheriting

        Select Case resolution.Outcome
            Case MaterializeOutcome.Materialized, MaterializeOutcome.MaterializedFromLeveledPick
                Select Case category
                    Case NPC_TemplateCategory.Traits
                        MaterializeTraits(npc, resolution.Source)
                    Case NPC_TemplateCategory.BaseData
                        MaterializeBaseData(npc, resolution.Source)
                    Case NPC_TemplateCategory.Stats
                        MaterializeStats(npc, resolution.Source)
                    Case NPC_TemplateCategory.AIData
                        MaterializeAIData(npc, resolution.Source)
                    Case NPC_TemplateCategory.DefaultPackageList
                        MaterializeDefPackList(npc, resolution.Source)
                    Case NPC_TemplateCategory.AttackData
                        MaterializeAttackData(npc, resolution.Source)
                    Case NPC_TemplateCategory.Keywords
                        npc.Record.PonerPalabrasClave(resolution.Source.Record.PalabrasClave())
                    Case NPC_TemplateCategory.Factions
                        npc.Record.PonerFacciones(resolution.Source.Record.Factions)
                        ' ⛔ CRIF viaja con el array de facciones bajo el bit 2, en los dos motores:
                        ' SSE 0x1403C2478/0x1403C247F (+0x230), FO4 0x1406586E9/0x1406586F0 (+0x2C8).
                        ' El handler que lo nombra es SSE 0x1403BC9D1 / FO4 0x14064FA28.
                        CopiarReferencia(npc.Record, resolution.Source.Record.CrimeFactionPresente,
                                         resolution.Source.Record.CrimeFaction, "CRIF",
                                         Sub(v) npc.Record.CrimeFaction = v)
                    Case NPC_TemplateCategory.Inventory
                        npc.Record.PonerInventario(resolution.Source.Record.Items)
                        ' ⛔ Este bucket no es solo `CNTO`: el motor le copia tambien los dos atuendos,
                        ' el byte de *geared up weapons* del DNAM y, en FO4, el soporte de servoarmadura.
                        ' Citas: SSE 0x1403C2022 (+0x218 DOFT), 0x1403C2030 (+0x220 SOFT),
                        ' 0x1403C203E (+0x241); FO4 0x14065818D (+0x2A8 PFRN), 0x14065819B (+0x2B0 DOFT),
                        ' 0x1406581A9 (+0x2B8 SOFT), 0x1406581B7 (+0x23E).
                        CopiarReferencia(npc.Record, resolution.Source.Record.DefaultOutfitPresente,
                                         resolution.Source.Record.DefaultOutfit, "DOFT",
                                         Sub(v) npc.Record.DefaultOutfit = v)
                        CopiarReferencia(npc.Record, resolution.Source.Record.SleepingOutfitPresente,
                                         resolution.Source.Record.SleepingOutfit, "SOFT",
                                         Sub(v) npc.Record.SleepingOutfit = v)
                        CopiarArmasListas(npc.Record, resolution.Source.Record)
                        Dim invF4 = TryCast(npc.Record, Canon.NpcFO4)
                        Dim invSrcF4 = TryCast(resolution.Source.Record, Canon.NpcFO4)
                        If invF4 IsNot Nothing AndAlso invSrcF4 IsNot Nothing Then
                            CopiarReferencia(invF4, invSrcF4.PowerArmorStandPresente, invSrcF4.PowerArmorStand,
                                             "PFRN", Sub(v) invF4.PowerArmorStand = v)
                        End If
                    Case NPC_TemplateCategory.SpellList
                        npc.Record.PonerEfectosDeActor(resolution.Source.Record.EfectosDeActor())
                        ' ⛔ Los PERKS viajan por ESTE bucket, no por uno propio. SSE 0x1403C24C3 ->
                        ' sub_1401DB120 sobre TESNPC+0x138; FO4 0x14065875A -> sub_1403075B0 sobre
                        ' TESNPC+0x188 — el mismo campo que escribe el handler de PRKZ.
                        ' ⭐ `PRKR` no tiene handler propio en ninguno de los dos: lo consume entero el de
                        ' `PRKZ`, igual que `KWDA` lo consume `KSIZ`. Por eso `PRKR` da cero como literal.
                        ' ⚠️ Sin esto, materializar Spell List bajaba el bit y el heredero se quedaba con
                        ' SUS perks donde el motor le habria puesto los de la plantilla.
                        npc.Record.PonerVentajas(resolution.Source.Record.Perks)
                End Select
                ClearFlagBit(npc, category)
                If resolution.Outcome = MaterializeOutcome.MaterializedFromLeveledPick Then
                    ' Worth a line in the log: the actor's look is now PINNED to one leaf of a list the game
                    ' used to re-roll, so "why does this NPC always look like X now" has a traceable answer.
                    Logger.LogLazy(Function() $"[TPLT-MATERIALIZE] NPC 0x{resolution.LogFormID:X8} '{resolution.LogEditorId}': " &
                                              $"Use-{category} came from a leveled list — pinned to leaf {resolution.LogReason}. " &
                                              "The actor no longer re-rolls its template at spawn.")
                End If

            Case MaterializeOutcome.NoSourceToLose
                ' Flag set but the chain has no template at all (TPLT and TPTA both 0). The engine's
                ' CopyFromTemplate has nothing to copy either, so the NPC's own data already wins in game and
                ' clearing the bit is a semantic no-op — safe, and it makes the record self-consistent.
                ClearFlagBit(npc, category)

            Case Else   ' Unresolvable
                ' THE FLAG STAYS SET. This is NOT the leveled-list case (that one gets pinned above) — it is
                ' the genuinely empty one: an unreadable/foreign source record, a cycle, or a list with no NPC_
                ' leaves at all. There is nothing to copy, so clearing the bit would drop the NPC to its own
                ' (usually EMPTY) Traits and the face would collapse to the race default. MEASURED own-record
                ' head-part count of 0 for FO4 904 / SSE 1294 of the affected population, which is what that
                ' collapse looks like. Keeping the bit preserves current in-game behaviour exactly.
                ' MEASURED occurrences of THIS branch in both vanilla load orders: 0.
                Logger.LogLazy(Function() $"[TPLT-MATERIALIZE] NPC 0x{resolution.LogFormID:X8} '{resolution.LogEditorId}': " &
                                          $"Use-{category} could not be resolved ({resolution.LogReason}) => flag LEFT SET; " &
                                          "inheritance preserved.")
        End Select

        Return resolution.Outcome
    End Function

    ''' <summary>Resolve and validate a category without copying fields or clearing its template bit.</summary>
    Friend Shared Function ProbeCategoryOwn(npc As NPC_Data,
                                            category As NPC_TemplateCategory,
                                            getParsedNpc As Func(Of UInteger, NPC_Data),
                                            Optional resolveLvlnPick As Func(Of UInteger, UInteger) = Nothing) As TraitsResolution
        If npc Is Nothing OrElse Not NpcTemplateHelpers.HasTemplateFlag(npc.Record.ConfigurationTemplateFlags, category) Then
            Return New TraitsResolution With {.Outcome = MaterializeOutcome.NotInheriting}
        End If

        Select Case category
            Case NPC_TemplateCategory.Traits, NPC_TemplateCategory.BaseData, NPC_TemplateCategory.Stats,
                 NPC_TemplateCategory.Keywords, NPC_TemplateCategory.Factions, NPC_TemplateCategory.Inventory,
                 NPC_TemplateCategory.SpellList, NPC_TemplateCategory.AIData,
                 NPC_TemplateCategory.DefaultPackageList, NPC_TemplateCategory.AttackData
                Return ResolveCategorySource(npc, category, getParsedNpc, resolveLvlnPick)
            Case Else
                Return New TraitsResolution With {.Outcome = MaterializeOutcome.UnsupportedCategory,
                                                  .LogFormID = npc.FormID, .LogEditorId = npc.EditorID,
                                                  .LogReason = "category has no complete materializer"}
        End Select
    End Function

    ''' <summary>La misma resolucion, con la fuente CLONADA, lista para viajar congelada en el override.
    ''' <para>⛔ Sin esto el snapshot guarda la instancia CACHEADA del parse -- la misma que el NPC Editor muta
    ''' en vivo para que el preview refleje la edicion. Un snapshot por referencia SEGUIRIA al terminal, y al
    ''' guardar se materializaria algo distinto de lo que el usuario aprobo en pantalla.</para>
    ''' <para>Los desenlaces sin fuente (`NotInheriting`, `NoSourceToLose`, `Unresolvable`) pasan tal cual: no
    ''' hay nada que clonar y siguen valiendo como respuesta.</para></summary>
    Friend Shared Function CongelarResolucion(resol As TraitsResolution) As TraitsResolution
        If resol.Source Is Nothing Then Return resol
        Dim congelada = resol
        congelada.Source = resol.Source.Copia()
        Return congelada
    End Function

    ''' <summary>What <see cref="MakeCategoryOwn"/> did. Not a success/failure flag — most are normal.</summary>
    Friend Enum MaterializeOutcome
        ''' <summary>The flag was already clear: the NPC's own data already wins. Nothing to do.</summary>
        NotInheriting = 0
        ''' <summary>Chain resolved to a single NPC_ template; fields copied in and the Use-X bit cleared.</summary>
        Materialized
        ''' <summary>Same, but the chain ran through a leveled list and one leaf was PINNED. The actor stops
        ''' re-rolling its template at spawn — intended: that is what "make this generic NPC editable" means.</summary>
        MaterializedFromLeveledPick
        ''' <summary>Flag set but there is no template source at all, so nothing could be inherited and
        ''' nothing was lost by clearing the bit.</summary>
        NoSourceToLose
        ''' <summary>Nothing to copy at all (unreadable source, cycle, or a list with no NPC_ leaves). The bit
        ''' was LEFT SET — the NPC keeps inheriting and its appearance is preserved.</summary>
        Unresolvable
        ''' <summary>The category has no complete materializer. The record and its flag are untouched.</summary>
        UnsupportedCategory
    End Enum

    Friend Structure TraitsResolution
        Public Outcome As MaterializeOutcome
        Public Source As NPC_Data
        Public LogFormID As UInteger
        Public LogEditorId As String
        Public LogReason As String
    End Structure

    ''' <summary>Walk the Traits template chain to the terminal NPC that actually provides the values (the
    ''' deepest source that itself does not inherit Traits), so the materialized values equal what the
    ''' engine's <c>CopyFromTemplate</c> chain would land on.
    '''
    ''' <para><b>LVLN — the case that used to be silently fatal.</b> The old walk called
    ''' <c>getParsedNpc(srcFid)</c> and treated its Nothing as "give up", then the caller cleared the flag
    ''' anyway. An LVLN template source always lands there (the resolver only returns NPC_ records), so the
    ''' single most common template shape in both games destroyed the NPC's appearance without a word.
    ''' MEASURED over the real load orders: FO4 1306 / 2126 Use-Traits NPCs reach an LVLN, SSE 1294 / 2461.</para>
    '''
    ''' <para><b>A leveled list gets PINNED, not refused.</b> The chain is followed through the LVLN by asking
    ''' <paramref name="resolveLvlnPick"/> for one leaf and continuing from it. Yes, that replaces a per-spawn
    ''' re-roll with a fixed actor — and that is the point: the user opened the editor to turn a generic NPC
    ''' into a concrete one, so pinning is the feature, not a side effect. MEASURED reach of this path:
    ''' FO4 1306 of 2126 Use-Traits NPCs (112 lists collapse to a single leaf anyway, 1194 are multi-leaf),
    ''' SSE 1294 of 2461 (6 single, 1288 multi).</para>
    '''
    ''' <para>Refusal is reserved for the case where there is nothing to pick FROM (unreadable record, cycle,
    ''' list with no NPC_ leaves) — measured 0 times in either vanilla load order.</para></summary>
    Private Shared Function ResolveCategorySource(npc As NPC_Data,
                                                  category As NPC_TemplateCategory,
                                                  getParsedNpc As Func(Of UInteger, NPC_Data),
                                                  resolveLvlnPick As Func(Of UInteger, UInteger)) As TraitsResolution
        Dim res As New TraitsResolution With {.LogFormID = npc.FormID, .LogEditorId = npc.EditorID}
        Dim current = npc
        Dim seen As New HashSet(Of UInteger)
        Dim wentThroughLeveledList = False

        For depth = 0 To MaxChainDepth - 1
            If Not NpcTemplateHelpers.HasTemplateFlag(current.Record.ConfigurationTemplateFlags, category) Then
                ' current owns the category → it is the source (unless it IS the original npc, which by
                ' contract inherits, so we only get here after ≥1 hop).
                If ReferenceEquals(current, npc) Then
                    res.Outcome = MaterializeOutcome.Unresolvable
                    res.LogReason = "the walk ended on the NPC itself"
                    Return res
                End If
                res.Outcome = If(wentThroughLeveledList, MaterializeOutcome.MaterializedFromLeveledPick, MaterializeOutcome.Materialized)
                res.Source = current
                If wentThroughLeveledList Then res.LogReason = $"0x{current.FormID:X8} '{current.EditorID}'"
                Return res
            End If

            Dim srcFid = NpcTemplateHelpers.ResolveTemplateSourceFormID(current, category)
            If srcFid = 0UI Then
                ' Flag set with no TPLT/TPTA behind it: the engine has nothing to copy either.
                res.Outcome = MaterializeOutcome.NoSourceToLose
                res.LogReason = "no TPLT/TPTA"
                Return res
            End If
            If Not seen.Add(srcFid) Then
                res.Outcome = MaterializeOutcome.Unresolvable
                res.LogReason = $"cycle in the chain (0x{srcFid:X8} seen twice)"
                Return res
            End If

            Dim next_ = getParsedNpc(srcFid)
            If next_ Is Nothing Then
                ' Not an NPC_ we can parse — a leveled list (the common case) or a missing/foreign record.
                ' PIN one leaf and keep walking from it: the leaf may itself inherit Traits, in which case the
                ' real source is further down the chain.
                Dim pick As UInteger = 0UI
                If resolveLvlnPick IsNot Nothing Then pick = resolveLvlnPick(srcFid)
                If pick = 0UI Then
                    res.Outcome = MaterializeOutcome.Unresolvable
                    res.LogReason = $"source 0x{srcFid:X8} is unreadable or has no NPC_ leaves"
                    Return res
                End If
                If Not seen.Add(pick) Then
                    res.Outcome = MaterializeOutcome.Unresolvable
                    res.LogReason = $"cycle through leveled list 0x{srcFid:X8}"
                    Return res
                End If
                Dim picked = getParsedNpc(pick)
                If picked Is Nothing Then
                    res.Outcome = MaterializeOutcome.Unresolvable
                    res.LogReason = $"leveled-list pick 0x{pick:X8} could not be parsed"
                    Return res
                End If
                wentThroughLeveledList = True
                next_ = picked
            End If

            current = next_
        Next

        res.Outcome = MaterializeOutcome.Unresolvable
        res.LogReason = $"chain deeper than {MaxChainDepth}"
        Return res
    End Function

    ''' <summary>Returns the terminal effective owner used to seed an editor panel. If the chain cannot be
    ''' resolved, returns the original NPC so the UI never fabricates data or diverges from MakeCategoryOwn.</summary>
    Friend Shared Function ResolveEffectiveSourceForEditor(npc As NPC_Data,
                                                            category As NPC_TemplateCategory,
                                                            getParsedNpc As Func(Of UInteger, NPC_Data),
                                                            Optional resolveLvlnPick As Func(Of UInteger, UInteger) = Nothing) As NPC_Data
        If npc Is Nothing OrElse Not NpcTemplateHelpers.HasTemplateFlag(npc.Record.ConfigurationTemplateFlags, category) Then Return npc
        Dim resolution = ResolveCategorySource(npc, category, getParsedNpc, resolveLvlnPick)
        If resolution.Outcome = MaterializeOutcome.Materialized OrElse
           resolution.Outcome = MaterializeOutcome.MaterializedFromLeveledPick Then Return resolution.Source
        Return npc
    End Function

    ''' <summary>Copy the Traits appearance/identity set from the resolved source into <paramref name="npc"/>.
    ''' Unconditional per-field overwrite (verdict c): the source is exactly what the engine would have
    ''' copied, so the NPC ends up identical to its in-template look. The caller re-applies the user's edit
    ''' AFTER this, so the edited field is not lost.
    ''' <para>Un campo que la fuente NO declara se SACA del destino: copiar el valor y dejar el subrecord
    ''' puesto lo convertiria en un valor propio que la plantilla nunca tuvo.</para></summary>
    Private Shared Sub MaterializeTraits(npc As NPC_Data, src As NPC_Data)
        Dim d = npc.Record, s = src.Record
        ' Scalar identity FormIDs the overlay never owns — always materialize.
        CopiarReferencia(d, s.RacePresente, s.Race, "RNAM", Sub(v) d.Race = v)
        CopiarReferencia(d, s.VoicePresente, s.Voice, "VTCK", Sub(v) d.Voice = v)
        CopiarReferencia(d, s.DeathItemPresente, s.DeathItem, "INAM", Sub(v) d.DeathItem = v)
        CopiarReferencia(d, s.FarAwayModelPresente, s.FarAwayModel, "ANAM", Sub(v) d.FarAwayModel = v)
        ' Su DISTANCIA es un campo del DNAM, y viaja por ESTE bucket — no por Stats, donde estaba.
        CopiarDistanciaDeModeloLejano(d, s)

        ' ⛔ Cuatro canales que el motor copia bajo el bit 0 y la app no copiaba. Los tres primeros
        ' son BLOQUES — el motor los mueve enteros— asi que va `CopiarSubrecord`, que copia el NODO y
        ' se lleva tambien lo que ninguna propiedad modela; enumerar campos perderia justo eso.
        '   OBND  0x1403C225D sub_140276CC0, 12 bytes (TESForm+0x20). FO4 0x14065846E sub_1404579C0.
        '   NAM9  sub_1403BDFC0 :0x1403BE165, bloque de 0x5C en +0x258 (0x4C de floats)
        '   NAMA  el MISMO bloque, +0x4C (0x10 de uints)
        CopiarSonidos(d, s)
        d.CopiarSubrecord(s, "OBND")
        d.CopiarSubrecord(s, "NAM9")
        d.CopiarSubrecord(s, "NAMA")
        ' ⛔⛔ NAM8 (nivel de sonido) y el grupo Destructible: los copia el bit 0 en los DOS juegos y la
        ' app no los copiaba. Salieron del CENSO de todo par origen->destino del bloque del bit 0, no de
        ' una lista de memoria -- eran el par 9 de 9 en los dos, o sea el ultimo renglon de la tabla.
        '   NAM8  SSE 0x1403C224A movzx eax,[rsi+0x245] / 0x1403C2251 mov [rdi+0x215],al
        '         FO4 0x140658443 / 0x14065844A  (+0x2E9 -> +0x281)
        '   DEST  SSE 0x1403C2194 call sub_1401D2AF0 (componente en TESNPC+0xF0)
        '         FO4 0x1406583D4 call sub_1402FD6C0 (TESNPC+0x138)
        ' ⛔ `NAM8` es `.AsRequired()` en los dos esquemas, asi que "el origen no lo trae" es esquina de
        ' plugin malformado; `CopiarSubrecord` BORRA el del destino en ese caso, que es lo mas cerca del
        ' motor que se puede estar sin inventarle un valor.
        d.CopiarSubrecord(s, "NAM8")
        d.CopiarGrupo(s, "Destructible")
        ' NAM7 (peso). Solo Skyrim: en FO4 el peso viaja por MWGT, que ya se copia mas abajo.
        ' ⛔⛔ EL `Else` NO ES DEFENSIVO: el motor copia este campo INCONDICIONALMENTE. Medido en
        ' `SkyrimSE.exe`, cuatro copias seguidas sin un solo test ni salto:
        '     0x1403C2146: mov eax,[rsi+0x1F8] / 0x1403C214C: mov [rdi+0x1C8],eax   <- NAM6
        '     0x1403C2152: mov eax,[rsi+0x1FC] / 0x1403C2158: mov [rdi+0x1CC],eax   <- NAM7
        '     0x1403C215E: mov rax,[rsi+0x108] / 0x1403C2165: mov [rdi+0xD8],rax    <- WNAM
        '     0x1403C216C: mov rax,[rsi+0x210] / 0x1403C2173: mov [rdi+0x1E0],rax   <- ANAM
        ' Lo mismo en FO4 para MWGT (0x140658359..0x140658379). ⇒ si el terminal NO trae peso, el
        ' heredero tampoco puede quedarse con el suyo: el motor le copia el patron de bits del
        ' terminal, sea cual sea. Sin este `Else` el heredero conservaba SU peso mientras en pantalla
        ' se veia el del terminal — el residuo real de la DECISION 17b, y el unico de sus cuatro
        ' canales que segula abierto: NAM9 y NAMA ya se copian con `CopiarSubrecord` (que BORRA el del
        ' destino cuando el origen no lo trae) y TINI esta fuera de alcance porque el bit 0 de SSE no
        ' lo copia.
        Dim sPeso = TryCast(s, Canon.NpcSSE), dPeso = TryCast(d, Canon.NpcSSE)
        If sPeso IsNot Nothing AndAlso dPeso IsNot Nothing Then
            If sPeso.WeightPresente Then
                dPeso.Weight = sPeso.Weight   ' 0x1403C2152 -> +0x1FC
            Else
                d.QuitarSubrecord("NAM7")
            End If
        End If
        d.ConfigurationFlags = MergeMaskedFlags(d.ConfigurationFlags, s.ConfigurationFlags,
                                                NpcTemplateHelpers.TraitsAcbsFlagsMask)
        ' ⛔ ACA SE COPIABA LA DISPOSICION BASE, y el motor NO la copia en ninguno de los dos: llama al
        ' setter con el valor DEL PROPIO DESTINO. SSE `0x1403C2188 movzx edx, word [rdi+0x18]` (rdi =
        ' destino+0x30) -> `0x1403C218F sub_1401DD490` = `mov [rcx+0x18],dx` con rcx=rdi: lee y escribe
        ' LA MISMA direccion. FO4 `0x140658387 movzx edx, word [r14+0x14]` (r14 = destino+0x68) ->
        ' `sub_14030A8D0`. El origen (rsi / [rbp+0x30]) no participa: es auto-escritura mas notificacion.
        ' ⚠️ Y esto NO alcanza para que la app deje de copiarla: el editor la vuelve a traer del
        ' TERMINAL (`NpcEditor_Form` :399 la muestra, :616 la congela, :1331 la escribe). Ese arreglo va
        ' aparte y esta declarado.
        ' ⛔ CNAM se fue a `MaterializeStats`: el motor lo copia bajo el bit 1 (Use Stats), no bajo el
        ' 0. Medido por los limites de bucket — la copia (SSE 0x1403C233E, FO4 0x1406585B7) cae entre el
        ' gate del bucket 1 (SSE 0x1403C232A / FO4 0x140658596) y el del 2.
        ' ⛔ ZNAM se fue a `MaterializeAIData`: su bucket es el 4, no el 0. La copia (SSE
        ' 0x1403C2502/0x1403C250E por el par virtual +0x2A0/+0x2A8, FO4 0x1406587B1/0x1406587BD por
        ' +0x340/+0x348) cae entre el gate del bucket 4 y el del 5.

        ' Height — not overlay-owned.
        If s.TieneAltura() Then d.PonerAltura(s.Altura()) Else d.QuitarSubrecord("NAM6")
        If s.TieneAlturaMaxima() Then d.PonerAlturaMaxima(s.AlturaMaxima()) Else d.QuitarSubrecord("NAM4")

        ' ⛔ Aca habia un `If Not skipOverlayOwned` que salteaba estos campos cuando el NPC tenia
        ' overlay. Se fue con el REORDEN: ahora se materializa PRIMERO y el overlay se estampa
        ' DESPUES, asi que gana por llegar ultimo y campo por campo. El salteo era un booleano
        ' GRUESO por NPC — un overlay de solo CUERPO hacia saltear TODA la cara.
        CopiarReferencia(d, s.SkinPresente, s.Skin, "WNAM", Sub(v) d.Skin = v)
        ' HCLF/BCLF. Antes viajaba el valor y no la presencia, asi que un NPC materializado cuyo record
        ' propio no traia HCLF se quedaba con el color de la plantilla en memoria y SIN el subrecord en el
        ' plugin. MEDIDO (TemplateTraitsProbe, load orders reales): SSE 2 NPC, FO4 0.
        CopiarReferencia(d, s.HairColorPresente, s.HairColor, "HCLF", Sub(v) d.HairColor = v)
        If s.TieneColorDeBarba() Then d.PonerColorDeBarba(s.ColorDeBarba()) Else d.QuitarSubrecord("BCLF")
        ' FTST (face TextureSet) — Traits bucket per TraitsState, resolved through the chain by the RENDER
        ' (NpcStateResolver.ResolveTraitsStateFromNPC → state.HeadTextureFormID). It was never materialized,
        ' so clearing Use-Traits dropped the inherited face TXST and the head fell back to the race DFTM in
        ' ApplyRaceFallbacks. MEASURED: SSE 40 NPCs lose a real FTST (e.g. TreasCorpseVampire* ←
        ' EncVampire01*F = 02006F9C); FO4 0 (its templated NPCs duplicate the value on their own record).
        CopiarReferencia(d, s.HeadTexturePresente, s.HeadTexture, "FTST", Sub(v) d.HeadTexture = v)
        ' QNAM (texture lighting / body skin tone). Lo lee el render como color y el plugin lo guarda como
        ' cuatro floats: es UN campo, no dos, asi que copiarlo entero es copiar el subrecord.
        ' MEDIDO: SSE 40 NPC, FO4 0.
        If s.TextureLightingRedPresente Then
            d.TextureLightingRed = s.TextureLightingRed
            d.TextureLightingGreen = s.TextureLightingGreen
            d.TextureLightingBlue = s.TextureLightingBlue
            Dim sf4 = TryCast(s, Canon.NpcFO4)
            Dim df4 = TryCast(d, Canon.NpcFO4)
            If sf4 IsNot Nothing AndAlso df4 IsNot Nothing AndAlso sf4.TextureLightingAlphaPresente Then
                df4.TextureLightingAlpha = sf4.TextureLightingAlpha
            End If
        Else
            d.QuitarSubrecord("QNAM")
        End If
        If s.PesoDelCuerpo(0).HasValue OrElse s.PesoDelCuerpo(1).HasValue OrElse s.PesoDelCuerpo(2).HasValue Then
            d.PonerPesoDelCuerpo(0, s.PesoDelCuerpo(0))
            d.PonerPesoDelCuerpo(1, s.PesoDelCuerpo(1))
            d.PonerPesoDelCuerpo(2, s.PesoDelCuerpo(2))
        Else
            d.QuitarSubrecord("MWGT")
        End If
        d.PonerPartesDeCabeza(s.PartesDeCabeza())
        d.PonerMorfosDeCara(s.MorfosDeCara())
        CopiarCapasDeTinte(d, s)
        CopiarMorfosDeRegion(d, s)
        ' ⛔ MRSV NO se copia. Medido: vive en `TESNPC+0x2D8` (load 0x14065ECA7) y **ninguna
        ' escritura de la funcion de copia toca ese offset**, en ningun bucket. La negativa se
        ' apoya en un censo POR CAMPO que cubre las tres formas de despacho mas los callees, con
        ' 0 de 64 sin delimitar — no en un barrido de instrucciones, que es lo que ya mintio tres
        ' veces en esta ola. El heredero se queda con las suyas, que es lo que hace el motor.
        ' ⚠️ FMIN se resolvio DESPUES, y por otro camino — ver el comentario de mas abajo. Lo que
        ' el censo por campo dio primero fue SIN_RESOLVER, porque su valor no va a ningun
        ' `TESNPC+K`; recien al abrir el setter aparecio la tabla lateral.
        ' ⛔ FMIN tampoco se copia, y costo fundarlo porque NO vive en el `TESNPC`.
        ' Su handler (0x1406503D4) lee el float y llama a `sub_140654DC0(this, xmm1)`, que NO
        ' escribe ningun `TESNPC+K`: mete el par (NPC, valor) en una TABLA LATERAL GLOBAL
        ' —`0x142F092B8` para insertar (`sub_1406637B0`), `0x142F092B0` para sacar
        ' (`sub_140667430`)— y el default 1.0 significa *no esta en la tabla*. Es la misma forma
        ' que el `BGSSoundTagComponent` de FO4.
        ' ⇒ la pregunta correcta no era "que escritura toca su offset" sino "alguien de la copia
        ' toca ESA TABLA". Medido sobre la funcion entera y sus **47 callees directos** (las 16
        ' hojas sin `.pdata` recorridas hasta su `ret`): **0 accesos**. No lo copia ningun bucket.

        ' Object Template (OBTS) — las combinaciones de mods de los robots. NO la posee el overlay (nunca las
        ' toca). El editor de Object Template las reescribe DESPUES de que MakeCategoryOwn vuelve.
        d.ReemplazarCombinations(s.CombinacionesDelNpc())
        ' APPR (Attach Parent Slots) — viaja por la cadena de Traits junto con OBTS y alimenta el filtro del
        ' pool de enganches en ObjectTemplateResolver, asi que soltarlo dejaria al robot con combinaciones que
        ' ya no puede filtrar. MEDIDO: 0 NPC pierden un valor en los dos load orders de vanilla (los
        ' templateados duplican APPR en su propio record); se materializa por el mismo motivo que OBTS.
        d.PonerRanurasDeEnganche(s.RanurasDeEnganche())
    End Sub

    ''' <summary>Copia un campo de referencia entero: si la fuente lo declara se escribe, y si no se SACA del
    ''' destino. Escribir el valor sin mirar la presencia le inventaria al destino un subrecord que la fuente
    ''' no tiene.</summary>
    Private Shared Sub CopiarReferencia(destino As Canon.INpc, presente As Boolean, valor As UInteger,
                                        firma As String, escribir As Action(Of UInteger))
        If presente Then
            escribir(valor)
        Else
            destino.QuitarSubrecord(firma)
        End If
    End Sub

    ''' <summary>Copia las capas de tinte de cara de un record al otro, reemplazando las que el destino
    ''' tenia. El destino queda con las mismas capas, en el mismo orden y con los mismos campos
    ''' declarados: lo que la fuente no trae, el destino tampoco.
    ''' <para>Solo Fallout 4: Skyrim no declara TETI/TEND y la copia no hace nada.</para></summary>
    ''' <summary>Las capas de tinte de la cara (FO4). Se copia el GRUPO entero, no campo por campo.
    ''' <para>⛔ Aca se enumeraban los siete campos con guarda de presencia, y el `TEND` quedaba
    ''' distinto del origen — el gate lo reportaba como "no copiado". La causa no era la que se
    ''' supuso: el `TEND` declara `Value`, un struct `Color` con Red/Green/Blue **mas un byte
    ''' `Unused`**, y un `Template Color Index`. Ese byte no tiene propiedad generada, asi que
    ''' enumerar campos lo perdia siempre. Es literalmente lo que advierte el docstring de
    ''' `CopiarSubrecord`: *copiar el NODO se lleva tambien lo que ninguna propiedad muestra —los
    ''' tramos de relleno sin usar— y el record re-emite byte a byte; enumerar los campos a mano
    ''' pierde justo los que no se modelan*.</para>
    ''' <para>Y encaja con el motor, que mueve el contenedor entero: `sub_1403FE7C0` = Clear del
    ''' destino (`0x1404030E0`) + copiar, llamado en `0x1406516D9`-`0x140651752`. Si la plantilla no
    ''' trae, `0x140651768`/`0x140651796` lo destruyen — y `CopiarGrupo` hace lo mismo: si el origen
    ''' no lo trae, saca el del destino.</para></summary>
    Private Shared Sub CopiarCapasDeTinte(destino As Canon.INpc, origen As Canon.INpc)
        If TryCast(destino, Canon.NpcFO4) Is Nothing OrElse TryCast(origen, Canon.NpcFO4) Is Nothing Then Return
        destino.CopiarGrupo(origen, "Face Tinting Layers")
    End Sub

    ''' <summary>Copia los morfos por region de cara de un record al otro. Mismo criterio que
    ''' <see cref="CopiarCapasDeTinte"/>.</summary>
    Private Shared Sub CopiarMorfosDeRegion(destino As Canon.INpc, origen As Canon.INpc)
        Dim d = TryCast(destino, Canon.NpcFO4)
        Dim s = TryCast(origen, Canon.NpcFO4)
        If d Is Nothing OrElse s Is Nothing Then Return
        While d.FaceMorphs.Count > 0
            If Not d.QuitarFaceMorphs(0) Then Exit While
        End While
        For Each m In s.FaceMorphs
            Dim e = d.AgregarFaceMorphs()
            If e Is Nothing Then Return
            If m.FaceMorphIndexPresente Then e.FaceMorphIndex = m.FaceMorphIndex
            If m.ValuesPositionXPresente Then e.ValuesPositionX = m.ValuesPositionX
            If m.ValuesPositionYPresente Then e.ValuesPositionY = m.ValuesPositionY
            If m.ValuesPositionZPresente Then e.ValuesPositionZ = m.ValuesPositionZ
            If m.ValuesRotationXPresente Then e.ValuesRotationX = m.ValuesRotationX
            If m.ValuesRotationYPresente Then e.ValuesRotationY = m.ValuesRotationY
            If m.ValuesRotationZPresente Then e.ValuesRotationZ = m.ValuesRotationZ
            If m.ValuesScalePresente Then e.ValuesScale = m.ValuesScale
            If m.ValuesUnknownPresente Then e.ValuesUnknown = CType(m.ValuesUnknown?.Clone(), Byte())
        Next
    End Sub

    ''' <summary>Los campos del <c>AIDT</c> que el motor copia, POR JUEGO.
    ''' <para>⛔⛔ ACA HABIA UNA LEY FALSA, y su premisa era que en memoria el <c>AIDT</c> tiene el
    ''' layout del archivo (8 u8 + 3 u32) y que el motor copia un PREFIJO —SSE hasta <c>Warn</c>, FO4 hasta
    ''' <c>Warn/Attack</c>—. Los dos loaders REEMPAQUETAN, y esta medido:</para>
    ''' <code>
    ''' setter FO4 sub_14030C5A0
    '''   0x14030C6D1  lea  rsi,[rdi+0xc]        ; destino de los radios
    '''   0x14030C6D7  mov  ebp,3                ; son TRES
    '''   0x14030C6F0  mov  eax,[r14]            ; bucle: u32 del archivo
    '''   0x14030C6F3  mov  ecx,0xffff
    '''   0x14030C6F8  cmp  eax,ecx
    '''   0x14030C6FA  cmovb cx,ax               ; SATURA
    '''   0x14030C6FE  mov  [rsi],cx             ; y guarda U16
    '''   0x14030C6DC  cmp  byte [r14+0x14],cl   ; No Slow Approach
    '''   0x14030C6E8  mov  [rdi+0x14],ecx       ;   = bit 0 de +0x14
    ''' copia SSE sub_1401DE8F0 : add rcx,8 / add rdx,8 / movsd + mov eax,[rdx+8]  ==> +0x08..+0x13
    ''' copia FO4 sub_14030C2E0 : add rcx,8 / add rdx,8 / movups                   ==> +0x08..+0x17
    ''' </code>
    ''' <para>Con ese layout —bitfield en +0x08..+0x0B, los tres radios como u16 en +0x0C/+0x0E/+0x10—
    ''' las dos copias significan otra cosa: <b>los DOS juegos copian los TRES radios</b>, y FO4 ademas
    ''' <c>No Slow Approach</c>. A la app le faltaban <c>Attack</c> en los dos y <c>Warn/Attack</c> en SSE.</para>
    ''' <para>⛔ Y el byte +0x07 (<c>Unused</c> en Skyrim, <c>Unknown</c> en Fallout 4) SALE de las listas:
    ''' el setter no lo guarda en ningun lado, asi que copiarlo era copiar de MAS -- bytes al ESP que el
    ''' motor nunca habria movido.</para>
    ''' <para>Los 3 bytes de relleno del final de FO4 tambien se llaman <c>Unknown</c>; al no nombrar ese
    ''' campo, ninguno de los dos entra.</para></summary>
    Private Shared ReadOnly CamposAidtSse As String() = {
        "Aggression", "Confidence", "Energy Level", "Morality", "Mood", "Assistance",
        "Aggro Radius Behavior", "Warn", "Warn/Attack", "Attack"}
    Private Shared ReadOnly CamposAidtFo4 As String() = {
        "Aggression", "Confidence", "Energy Level", "Morality", "Mood", "Assistance",
        "Aggro Radius Behavior", "Warn", "Warn/Attack", "Attack", "No Slow Approach"}

    ''' <summary>El bucket 4 (Use AI Data). Tres canales medidos: <c>AIDT</c> (parcial), <c>ZNAM</c> y
    ''' <c>GNAM</c>.
    ''' <para>⛔ Antes esta categoria no estaba soportada, y eso tenia una consecuencia peor que una
    ''' copia mal puesta: como <c>ZNAM</c> se editaba bajo Traits, <b>ninguna secuencia de la UI podia
    ''' bajar el bit 4</b>, asi que el motor le pisaba el Combat Style al cargar y la edicion del
    ''' usuario desaparecia en el juego, sin error y sin log.</para>
    ''' <para>Citas: <c>AIDT</c> SSE <c>0x1403C24F4</c> / FO4 <c>0x1406587A1</c>; <c>ZNAM</c> SSE
    ''' <c>0x1403C2502</c>+<c>0x1403C250E</c> / FO4 <c>0x1406587B1</c>+<c>0x1406587BD</c>; <c>GNAM</c>
    ''' SSE <c>0x1403C251B</c> (+0x1D0) / FO4 <c>0x1406587CE</c> (+0x250).</para></summary>
    ''' <summary>⛔ El canal de SONIDOS del actor, con su bit derivado. Era el ultimo hueco declarado
    ''' del bucket 0 y el unico rojo que quedaba en los dos juegos.
    ''' <para>El motor tiene tres casos (SSE <c>0x1403C21A7</c>-<c>0x1403C2243</c>, FO4
    ''' <c>0x1406583D9</c>-<c>0x140658436</c>): si la plantilla es DUEÑA de su contenedor, el heredero
    ''' recibe uno PROPIO y se le copia el de la plantilla; si la plantilla NO es dueña porque hereda
    ''' de mas arriba, el heredero libera el suyo y queda con el puntero COMPARTIDO.</para>
    ''' <para>A nivel de record eso es exactamente: o copio el grupo y declaro que son propios, o tomo
    ''' su <c>CSCR</c> —de quien los hereda— y dejo de declarar grupo. El bit <c>0x100</c> sale de cual
    ''' de las dos paso, nunca del origen: es estado DERIVADO.</para>
    ''' <para>⚠️ El caso 2 (la plantilla es dueña pero su contenedor esta vacio) es la unica asimetria
    ''' que queda entre los dos motores — SSE deja intacto el del heredero y FO4 lo destruye— y NO se
    ''' replica: no es observable en el record, porque un grupo vacio y un grupo ausente se emiten
    ''' igual. Queda dicho para que no se lo busque de nuevo.</para></summary>
    Private Shared Sub CopiarSonidos(d As Canon.INpc, s As Canon.INpc)
        Dim grupo = If(EsSse(d), "Sound Types", "Actor Sounds")
        If d.CopiarGrupo(s, grupo) Then
            ' La plantilla es dueña: el heredero pasa a serlo tambien, y deja de heredarlos de nadie.
            d.QuitarSubrecord("CSCR")
            d.ConfigurationFlags = d.ConfigurationFlags Or NpcTemplateHelpers.AcbsBitSonidosPropios
        Else
            ' La plantilla los hereda: el heredero hereda de la MISMA fuente y no declara propios.
            d.CopiarSubrecord(s, "CSCR")
            d.ConfigurationFlags = d.ConfigurationFlags And Not NpcTemplateHelpers.AcbsBitSonidosPropios
        End If
    End Sub

    ''' <summary>⚠⚠ SIN LLAMADOR EN PRODUCCION, y va dicho aca para que nadie lo tome por vivo.
    ''' <para>Censado: `NpcEditor_Form` tiene <b>cero</b> referencias a listas de paquetes y a la raza
    ''' de ataque, y `NpcRecordOverride` no tiene campo para ninguna de las dos. O sea que **ninguna
    ''' edicion del usuario puede pedir estas dos categorias**: los unicos que las ejercitan son
    ''' `BucketTraitsGate` y `ProbeCategoryOwn`.</para>
    ''' <para>No es codigo muerto para borrar — es la ley del motor transcrita con cita y medida por el
    ''' gate, lista para cuando el editor exponga esos campos. Pero mientras no los exponga, **el gate
    ''' mide un sujeto que el producto no corre**, y eso hay que saberlo al leer su verde.</para></summary>
    ''' <summary>El bucket 10 (Use Def Pack List): el paquete por defecto mas las listas de override.
    ''' <para>SSE: <c>DPLT</c> en <c>0x1403C2556</c>/<c>0x1403C255D</c> (+0x228) y las CUATRO listas
    ''' por <c>0x1403C2572</c> → <c>sub_1401DAB00</c>, que hace cuatro stores CONSECUTIVOS
    ''' (+0x168/+0x170/+0x178/+0x180) <b>sin ninguna rama por lista</b>: no pueden diferir entre si.
    ''' FO4: <c>DPLT</c> en <c>0x14065885D</c> (+0x2C0) y SEIS por <c>0x140658864</c> →
    ''' <c>sub_140306E40</c> (+0x1C8..+0x1F0) — las mismas cuatro mas <c>FCPL</c> y <c>RCLR</c>, que
    ''' no existen en Skyrim.</para></summary>
    ''' <summary>⛔ Mismo caso que <see cref="MaterializeAttackData"/>: sin llamador de producción porque el
    ''' editor no expone el bucket 10, pero con su ley medida y ejercitada por el gate. No es código muerto.</summary>
    Private Shared Sub MaterializeDefPackList(npc As NPC_Data, src As NPC_Data)
        Dim d = npc.Record, s = src.Record
        CopiarReferencia(d, s.DefaultPackageListPresente, s.DefaultPackageList, "DPLT",
                         Sub(v) d.DefaultPackageList = v)
        CopiarReferencia(d, s.SpectatorOverridePackageListPresente, s.SpectatorOverridePackageList, "SPOR",
                         Sub(v) d.SpectatorOverridePackageList = v)
        CopiarReferencia(d, s.ObserveDeadBodyOverridePackageListPresente, s.ObserveDeadBodyOverridePackageList, "OCOR",
                         Sub(v) d.ObserveDeadBodyOverridePackageList = v)
        CopiarReferencia(d, s.GuardWarnOverridePackageListPresente, s.GuardWarnOverridePackageList, "GWOR",
                         Sub(v) d.GuardWarnOverridePackageList = v)
        CopiarReferencia(d, s.CombatOverridePackageListPresente, s.CombatOverridePackageList, "ECOR",
                         Sub(v) d.CombatOverridePackageList = v)
        ' Las dos que solo tiene Fallout 4.
        Dim sf4 = TryCast(s, Canon.NpcFO4), df4 = TryCast(d, Canon.NpcFO4)
        If sf4 IsNot Nothing AndAlso df4 IsNot Nothing Then
            CopiarReferencia(df4, sf4.FollowerCommandPackageListPresente, sf4.FollowerCommandPackageList, "FCPL",
                             Sub(v) df4.FollowerCommandPackageList = v)
            CopiarReferencia(df4, sf4.FollowerElevatorPackageListPresente, sf4.FollowerElevatorPackageList, "RCLR",
                             Sub(v) df4.FollowerElevatorPackageList = v)
        End If
    End Sub

    ''' <summary>El bucket 11 (Use Attack Data): la raza de ataque y el array de ataques.
    ''' <para>En memoria el motor deja un <b>puntero COMPARTIDO</b> al mapa del terminal
    ''' (<c>0x1403C25AD</c> → <c>sub_1401D1AC0</c>, con <c>lock inc</c> del refcount; FO4
    ''' <c>0x1406588BA</c> → <c>sub_1402FC670</c>), y tiene una asimetria medida: si el origen usa el
    ''' default de SU raza, SSE <b>conserva</b> el mapa propio del heredero (<c>0x1403C259D je</c>) y
    ''' FO4 igual lo asigna.</para>
    ''' <para>⛔ Nada de eso es expresable en un ESP — no hay "puntero compartido" en un record— asi
    ''' que lo canonico es lo unico OBSERVABLE: el heredero termina con los ataques y la raza de
    ''' ataque del terminal. La asimetria queda dicha para que no se la busque de nuevo.</para>
    ''' <para>Y el respaldo de <c>0x1403C25B2</c>/<c>0x1406588C9</c> (si quedo sin mapa, se le pone el
    ''' de SU PROPIA raza) tampoco entra: es reconstruccion en runtime desde la raza, que el motor
    ''' rehace en cada carga.</para></summary>
    ''' <summary>⛔ SIN llamador de PRODUCCIÓN, y NO es código muerto — el comentario está para que el
    ''' próximo que pase no lo borre. El editor no expone campos de los buckets 10 (Default Package List) ni
    ''' 11 (Attack Data): eso es una FEATURE que no existe, no una ley que falte. La ley sí existe, está
    ''' medida contra el binario, y está EJERCITADA: `BucketTraitsGate` materializa estos dos buckets en sus
    ''' corridas 10 y 11, y `G14` les mide además el viaje al ESP escrito.
    ''' <para>El día que el editor exponga esos campos, lo único que hace falta es el latch en el override —
    ''' la materialización ya está acá y ya está probada.</para></summary>
    Private Shared Sub MaterializeAttackData(npc As NPC_Data, src As NPC_Data)
        Dim d = npc.Record, s = src.Record
        CopiarReferencia(d, s.AttackRacePresente, s.AttackRace, "ATKR", Sub(v) d.AttackRace = v)
        d.CopiarGrupo(s, "Attacks")
        ' ⛔ `ATKT` viaja con el grupo, y eso NO es copia de mas. El motor no hace NADA con `ATKT` en el
        ' camino de `NPC_`: llega al despacho (`0x1406508C2`), el handler cae al `jne 0x1406FF867` de
        ' `0x1406FF69C` = ret, y se descarta **sin consumir payload**. Su otra aparicion, `0x140688F65`,
        ' es el `LoadForm` de RACE, no el de `NPC_`.
        ' ⇒ Sobre un campo que el motor NUNCA CARGA no existe la ley "no lo copies": el bucket 11 mueve un
        ' PUNTERO al mapa de attack data, y el mapa no tiene ranura para `ATKT`. Las dos formas de escribir
        ' el ESP — con el campo y sin el — cargan al MISMO estado de runtime, y eso esta MEDIDO, no
        ' argumentado. `BucketTraitsGate` lo declara en `DESCARTADOS_POR_BUCKET`, lo IMPRIME con su cita, y
        ' tiene un control (C2b) que se pone rojo si la exclusion deja de usarse — una excusa que no tapa
        ' nada es candidata a estar tapando otra cosa.
        ' ⛔ Y se copia el GRUPO, no campo por campo: copiar por nodo perderia los tramos que ninguna
        ' propiedad modela, que es el mismo defecto que hubo que arreglar en `TEND`.
        ' ⚠ Queda una pregunta abierta, que no cambia nada de lo anterior: por que el esquema declara
        ' `ATKT` en `NPC_` si el loader lo tira — campo que solo vive en RACE, o hueco del motor.
    End Sub

    Private Shared Sub MaterializeAIData(npc As NPC_Data, src As NPC_Data)
        Dim d = npc.Record, s = src.Record
        ' ⛔ El retorno NO se descarta: si el destino no trae `AIDT`, `FindField` falla para todos los
        ' nombres, se copia CERO y el bit 4 se bajaria igual — heredero sin AI Data y sin que nadie se
        ' entere. Si el origen lo trae y no se copio nada, es cableado roto.
        Dim copiados = d.CopiarCamposDeSubrecord(s, "AIDT", If(EsSse(d), CamposAidtSse, CamposAidtFo4))
        If copiados = 0 AndAlso Canon.WbEdit.FindSubrecord(TryCast(s, Canon.CanonView)?.Node, "AIDT") IsNot Nothing Then
            Throw New InvalidOperationException(
                "MaterializeAIData: el origen trae AIDT y no se copio ningun campo (nombres del esquema cambiados?)")
        End If
        CopiarReferencia(d, s.CombatStylePresente, s.CombatStyle, "ZNAM", Sub(v) d.CombatStyle = v)
        CopiarReferencia(d, s.GiftFilterPresente, s.GiftFilter, "GNAM", Sub(v) d.GiftFilter = v)
    End Sub

    Private Shared Sub MaterializeBaseData(npc As NPC_Data, src As NPC_Data)
        Dim d = npc.Record, s = src.Record
        If s.NamePresente Then d.Name = s.Name Else d.QuitarSubrecord("FULL")
        If s.ShortNamePresente Then d.ShortName = s.ShortName Else d.QuitarSubrecord("SHRT")
        ' La mascara de Base Data es un dato POR JUEGO, y el juego sale del RECORD, no de un global:
        ' asi el materializador sigue siendo probable sin levantar `Config_App`.
        d.ConfigurationFlags = MergeMaskedFlags(d.ConfigurationFlags, s.ConfigurationFlags,
                                                NpcTemplateHelpers.BaseDataAcbsFlagsMask(EsSse(d)))
        ' ⛔ NTRM (terminal nativa), SOLO FO4 y bajo ESTE bucket: 0x140658260 `call sub_140256B40` sobre
        ' `TESNPC+0x208` (BGSNativeTerminalForm), dentro del bloque del bit 7. Faltaba.
        Dim sf As Canon.NpcFO4 = TryCast(s, Canon.NpcFO4), df As Canon.NpcFO4 = TryCast(d, Canon.NpcFO4)
        If sf IsNot Nothing AndAlso df IsNot Nothing Then
            CopiarReferencia(d, sf.NativeTerminalPresente, sf.NativeTerminal, "NTRM",
                             Sub(v) df.NativeTerminal = v)
        End If
    End Sub

    Private Shared Sub MaterializeStats(npc As NPC_Data, src As NPC_Data)
        Dim d = npc.Record, s = src.Record
        ' DNAM: en Fallout 4 son las estadisticas calculadas y en Skyrim el bloque de habilidades. Se copia
        ' el subrecord entero, que es lo que el motor copia por esta categoria.
        ' ⛔ De todo el `DNAM`, este bucket posee SOLO las habilidades — y en FO4, NADA.
        CopiarHabilidadesDelDnam(d, s)
        ' CNAM (Class). Su bucket es ESTE, no Traits: SSE 0x1403C2337/0x1403C233E (+0x1C0), FO4
        ' 0x1406585AC/0x1406585B7 (+0x240), los dos dentro del bloque del bit 1.
        CopiarReferencia(d, s.ClassPresente, s.[Class], "CNAM", Sub(v) d.[Class] = v)
        ' Los dos bits que se copian tal cual, y despues la REGLA del 0x10, que no es una mascara.
        d.ConfigurationFlags = MergeMaskedFlags(d.ConfigurationFlags, s.ConfigurationFlags,
                                                NpcTemplateHelpers.StatsAcbsFlagsMaskPlana)
        d.ConfigurationFlags = NpcTemplateHelpers.AplicarReglaAutoCalc(d.ConfigurationFlags, s.ConfigurationFlags)
        d.PonerNivelDeConfiguracion(s.NivelDeConfiguracion())
        d.ConfigurationCalcMinLevel = s.ConfigurationCalcMinLevel
        d.ConfigurationCalcMaxLevel = s.ConfigurationCalcMaxLevel
        ' ⛔ Bleedout Override: lo copia el bit 1 en los DOS juegos y faltaba en los dos.
        '   SSE 0x1403C244C movzx eax,word [rsi+0x4e] / 0x1403C2450 mov [rdi+0x1e],ax
        '   FO4 0x140658695 movzx ecx,word [rax+0x80] / 0x1406586A0 mov [r14+0x18],cx
        ' Es un campo del ACBS, no un subrecord propio, asi que va por la propiedad y no por CopiarSubrecord.
        d.ConfigurationBleedoutOverride = s.ConfigurationBleedoutOverride
        d.ConfigurationBleedoutOverridePresente = s.ConfigurationBleedoutOverridePresente

        Dim sf4 = TryCast(s, Canon.NpcFO4), df4 = TryCast(d, Canon.NpcFO4)
        If sf4 IsNot Nothing AndAlso df4 IsNot Nothing Then
            df4.ConfigurationXPValueOffset = sf4.ConfigurationXPValueOffset
            ' ⛔ PRPS bajo el bit 1, SOLO FO4: 0x140658644 `call sub_140256EF0` sobre `TESNPC+0x1A0`
            ' (BGSPropertySheet por RTTI). Faltaba, y ademas el editor lo escribe sin materializar
            ' -- ver `NpcEditor_Form`.
            d.CopiarSubrecord(s, "PRPS")
        End If
        Dim ss = TryCast(s, Canon.NpcSSE), ds = TryCast(d, Canon.NpcSSE)
        If ss IsNot Nothing AndAlso ds IsNot Nothing Then
            ds.ConfigurationMagickaOffset = ss.ConfigurationMagickaOffset
            ds.ConfigurationStaminaOffset = ss.ConfigurationStaminaOffset
            ds.ConfigurationSpeedMultiplier = ss.ConfigurationSpeedMultiplier
            ds.ConfigurationHealthOffset = ss.ConfigurationHealthOffset
        End If
    End Sub

    ''' <summary>⛔⛔ EL `DNAM` NO TIENE UN DUEÑO: TIENE CUATRO. Aca habia un `CopiarDnam` que lo
    ''' copiaba entero desde `MaterializeStats`, con el comentario *«es lo que el motor copia por esta
    ''' categoria»*. Medido en los dos binarios, eso es falso — y en FO4 lo es para el subrecord
    ''' ENTERO: ni un solo campo del `DNAM` viaja bajo Use Stats.</summary>
    ''' <remarks>
    ''' <code>
    ''' tramo                                    SSE           FO4          bucket
    ''' 18 Skill Values + 18 Skill Offsets       +0x190..1B3   (no existe)  1 Stats
    ''' Health/Magicka/Stamina | Calc.Health/AP  +0x1B4/6/8    +0x238/23A   NINGUNO — se DERIVAN
    ''' Far Away Model Distance                  +0x1BA        +0x23C       0 Traits
    ''' Geared Up Weapons                        +0x241        +0x23E       8 Inventory
    ''' </code>
    ''' <para>⛔ Y `QuitarSubrecord("DNAM")` desde Stats se llevaba puestos los tramos de Traits y de
    ''' Inventory del DESTINO, que ese bucket no gobierna. Por eso ninguna de las tres partes lo
    ''' hace: sacar el subrecord entero es una operacion que no le pertenece a ningun bucket.</para>
    ''' </remarks>
    Private Shared Sub CopiarHabilidadesDelDnam(destino As Canon.INpc, origen As Canon.INpc)
        ' Solo SSE: FO4 no tiene habilidades en el DNAM (sus skills viajan como actor values del
        ' PropertySheet). Citas de la copia: 0x1403C23CA (16 B) / 0x1403C23D8 / 0x1403C23E6 (16 B) /
        ' 0x1403C23F4, todas dentro del bloque del bit 1.
        Dim ss = TryCast(origen, Canon.NpcSSE), ds = TryCast(destino, Canon.NpcSSE)
        If ss Is Nothing OrElse ds Is Nothing Then Return
        While ds.SkillValues.Count > 0
            If Not ds.QuitarSkillValues(0) Then Exit While
        End While
        For Each v In ss.SkillValues
            Dim e = ds.AgregarSkillValues()
            If e IsNot Nothing Then e.Skill = v.Skill
        Next
        While ds.SkillOffsets.Count > 0
            If Not ds.QuitarSkillOffsets(0) Then Exit While
        End While
        For Each v In ss.SkillOffsets
            Dim e = ds.AgregarSkillOffsets()
            If e IsNot Nothing Then e.Skill = v.Skill
        Next
    End Sub

    ''' <summary>La distancia del modelo lejano viaja en TRAITS, no en Stats. SSE `0x1403C217A`
    ''' (+0x1BA), FO4 `0x1406583A1` (+0x23C).
    ''' <para>⚠️ No es el mismo tipo en los dos: en SSE el archivo trae un `Single` y el parser lo
    ''' convierte a u16 con saturacion (`sub_1403BB6E0`, `minss` 65535 + `cvttss2si`); en FO4 ya es
    ''' u16. Por eso hay una rama por juego y no un camino unico.</para></summary>
    Private Shared Sub CopiarDistanciaDeModeloLejano(destino As Canon.INpc, origen As Canon.INpc)
        Dim sf4 = TryCast(origen, Canon.NpcFO4), df4 = TryCast(destino, Canon.NpcFO4)
        If sf4 IsNot Nothing AndAlso df4 IsNot Nothing Then
            If sf4.FarAwayModelDistancePresente Then df4.FarAwayModelDistance = sf4.FarAwayModelDistance
            Return
        End If
        Dim ss = TryCast(origen, Canon.NpcSSE), ds = TryCast(destino, Canon.NpcSSE)
        If ss Is Nothing OrElse ds Is Nothing Then Return
        If ss.PlayerSkillsFarAwayModelDistancePresente Then
            ds.PlayerSkillsFarAwayModelDistance = ss.PlayerSkillsFarAwayModelDistance
        End If
    End Sub

    ''' <summary>El byte de *geared up weapons* viaja en INVENTORY. SSE `0x1403C2045` (+0x241),
    ''' FO4 `0x1406581BE` (+0x23E). Es un campo del `DNAM` que no viaja con el resto del `DNAM`:
    ''' el mismo error de forma que tenia `CNAM`.</summary>
    Private Shared Sub CopiarArmasListas(destino As Canon.INpc, origen As Canon.INpc)
        Dim sf4 = TryCast(origen, Canon.NpcFO4), df4 = TryCast(destino, Canon.NpcFO4)
        If sf4 IsNot Nothing AndAlso df4 IsNot Nothing Then
            If sf4.GearedUpWeaponsPresente Then df4.GearedUpWeapons = sf4.GearedUpWeapons
            Return
        End If
        Dim ss = TryCast(origen, Canon.NpcSSE), ds = TryCast(destino, Canon.NpcSSE)
        If ss Is Nothing OrElse ds Is Nothing Then Return
        If ss.PlayerSkillsGearedUpWeaponsPresente Then
            ds.PlayerSkillsGearedUpWeapons = ss.PlayerSkillsGearedUpWeapons
        End If
    End Sub

    Private Shared Function MergeMaskedFlags(current As UInteger, source As UInteger, mask As UInteger) As UInteger
        Return (current And Not mask) Or (source And mask)
    End Function

    ''' <summary>El juego, deducido del RECORD y no de un global: `NpcSSE` y `NpcFO4` son las dos
    ''' clases concretas, y el materializador ya baja a ellas con `TryCast` en otros cuatro sitios.</summary>
    Private Shared Function EsSse(r As Canon.INpc) As Boolean
        Return TypeOf r Is Canon.NpcSSE
    End Function

    ''' <summary>Baja el bit de <paramref name="category"/> en las banderas de plantilla, para que el emisor
    ''' escriba el valor bajado y el motor saltee la copia de esa categoria.</summary>
    Private Shared Sub ClearFlagBit(npc As NPC_Data, category As NPC_TemplateCategory)
        Dim mask As UShort = CUShort(1 << CInt(category))
        npc.Record.ConfigurationTemplateFlags = CUShort(npc.Record.ConfigurationTemplateFlags And Not mask)
    End Sub

End Class
