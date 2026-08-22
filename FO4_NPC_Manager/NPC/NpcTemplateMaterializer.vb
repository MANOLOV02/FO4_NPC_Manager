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
    ''' <param name="skipOverlayOwned">Traits only: when True, the appearance fields the LooksMenu overlay owns
    ''' (skin WNAM, hair/facial-hair color + their emit gates, FTST face TextureSet, QNAM texture lighting,
    ''' head parts, body weight, face morphs/tints/regions, body-morph regions, facial-morph intensity) are NOT
    ''' materialized — the overlay has already populated them on the save shadow and must win. The template
    ''' still fills the NON-overlaid Traits fields (Race, Death Item, Far-Away Model, Height, APPR, Object
    ''' Template), so nothing falls back to the record's empty own-value.
    ''' <para>The gate is exact, not approximate: FTST/QNAM/HasHairColor belong in here precisely because
    ''' <c>NpcRecordOverlay.ApplyPresetOverlayToNpcData</c> DERIVES all three (FTST at :205/:212 as LM template
    ''' &gt; preset .jslot &gt; raw; QNAM at :467-479 from the slot-12 SkinTone tint; HasHairColor at :333).
    ''' This helper runs LAST on the save shadow, so materializing them unconditionally would overwrite that
    ''' derivation and undo the very fixes those lines exist for.</para></param>
    ''' <param name="resolveLvlnPick">Optional: LVLN FormID → the leaf NPC_ FormID to PIN this actor to.
    ''' Only consulted for Traits, and only when the chain hits a record <paramref name="getParsedNpc"/>
    ''' can't return — i.e. a leveled list. Pinning one leaf is the WHOLE POINT of the editor: a generic
    ''' actor is being turned into a concrete one, and the engine's per-spawn re-roll is exactly what the
    ''' user is choosing to replace. The caller decides WHICH leaf (MainForm hands back the one currently
    ''' being previewed, so the NPC the user edits is the NPC they were looking at). Nothing (CLI / probes)
    ''' leaves the chain unresolvable.</param>
    ''' <returns>What actually happened — see <see cref="MaterializeOutcome"/>.</returns>
    Friend Shared Function MakeCategoryOwn(npc As NPC_Data,
                                           category As NPC_TemplateCategory,
                                           getParsedNpc As Func(Of UInteger, NPC_Data),
                                           Optional skipOverlayOwned As Boolean = False,
                                           Optional resolveLvlnPick As Func(Of UInteger, UInteger) = Nothing) As MaterializeOutcome
        Dim resolution = ProbeCategoryOwn(npc, category, getParsedNpc, resolveLvlnPick)

        Select Case resolution.Outcome
            Case MaterializeOutcome.Materialized, MaterializeOutcome.MaterializedFromLeveledPick
                Select Case category
                    Case NPC_TemplateCategory.Traits
                        MaterializeTraits(npc, resolution.Source, skipOverlayOwned)
                    Case NPC_TemplateCategory.BaseData
                        MaterializeBaseData(npc, resolution.Source)
                    Case NPC_TemplateCategory.Stats
                        MaterializeStats(npc, resolution.Source)
                    Case NPC_TemplateCategory.Keywords
                        npc.Record.PonerPalabrasClave(resolution.Source.Record.PalabrasClave())
                    Case NPC_TemplateCategory.Factions
                        npc.Record.PonerFacciones(resolution.Source.Record.Factions)
                    Case NPC_TemplateCategory.Inventory
                        npc.Record.PonerInventario(resolution.Source.Record.Items)
                    Case NPC_TemplateCategory.SpellList
                        npc.Record.PonerEfectosDeActor(resolution.Source.Record.EfectosDeActor())
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
                 NPC_TemplateCategory.SpellList
                Return ResolveCategorySource(npc, category, getParsedNpc, resolveLvlnPick)
            Case Else
                Return New TraitsResolution With {.Outcome = MaterializeOutcome.UnsupportedCategory,
                                                  .LogFormID = npc.FormID, .LogEditorId = npc.EditorID,
                                                  .LogReason = "category has no complete materializer"}
        End Select
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
    Private Shared Sub MaterializeTraits(npc As NPC_Data, src As NPC_Data, Optional skipOverlayOwned As Boolean = False)
        Dim d = npc.Record, s = src.Record
        ' Scalar identity FormIDs the overlay never owns — always materialize.
        CopiarReferencia(d, s.RacePresente, s.Race, "RNAM", Sub(v) d.Race = v)
        CopiarReferencia(d, s.VoicePresente, s.Voice, "VTCK", Sub(v) d.Voice = v)
        CopiarReferencia(d, s.DeathItemPresente, s.DeathItem, "INAM", Sub(v) d.DeathItem = v)
        CopiarReferencia(d, s.FarAwayModelPresente, s.FarAwayModel, "ANAM", Sub(v) d.FarAwayModel = v)
        d.ConfigurationFlags = MergeMaskedFlags(d.ConfigurationFlags, s.ConfigurationFlags,
                                                NpcTemplateHelpers.TraitsAcbsFlagsMask)
        d.PonerBaseDeDisposicion(s.BaseDeDisposicion())
        CopiarReferencia(d, s.ClassPresente, s.[Class], "CNAM", Sub(v) d.[Class] = v)
        CopiarReferencia(d, s.CombatStylePresente, s.CombatStyle, "ZNAM", Sub(v) d.CombatStyle = v)

        ' Height — not overlay-owned.
        If s.TieneAltura() Then d.PonerAltura(s.Altura()) Else d.QuitarSubrecord("NAM6")
        If s.TieneAlturaMaxima() Then d.PonerAlturaMaxima(s.AlturaMaxima()) Else d.QuitarSubrecord("NAM4")

        ' Overlay-OWNED appearance/body fields: skip when a LooksMenu overlay already populated them on the shadow
        ' (skipOverlayOwned) so the overlay wins; otherwise materialize from the template as before.
        If Not skipOverlayOwned Then
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
            Dim regiones = s.ValoresDeRegionCorporal()
            If regiones.Count > 0 Then d.PonerValoresDeRegionCorporal(regiones) Else d.QuitarSubrecord("MRSV")
            If s.TieneIntensidadDeMorfoFacial() Then
                d.PonerIntensidadDeMorfoFacial(s.IntensidadDeMorfoFacial())
            Else
                d.QuitarSubrecord("FMIN")
            End If
        End If

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
    Private Shared Sub CopiarCapasDeTinte(destino As Canon.INpc, origen As Canon.INpc)
        Dim d = TryCast(destino, Canon.NpcFO4)
        Dim s = TryCast(origen, Canon.NpcFO4)
        If d Is Nothing OrElse s Is Nothing Then Return
        While d.FaceTintingLayers.Count > 0
            If Not d.QuitarFaceTintingLayers(0) Then Exit While
        End While
        For Each c In s.FaceTintingLayers
            Dim e = d.AgregarFaceTintingLayers()
            If e Is Nothing Then Return
            If c.IndexDataTypePresente Then e.IndexDataType = c.IndexDataType
            If c.LayerIndexPresente Then e.LayerIndex = c.LayerIndex
            If c.DataValuePresente Then e.DataValue = c.DataValue
            If c.ColorRedPresente Then e.ColorRed = c.ColorRed
            If c.ColorGreenPresente Then e.ColorGreen = c.ColorGreen
            If c.ColorBluePresente Then e.ColorBlue = c.ColorBlue
            If c.DataTemplateColorIndexPresente Then e.DataTemplateColorIndex = c.DataTemplateColorIndex
        Next
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
        Next
    End Sub

    Private Shared Sub MaterializeBaseData(npc As NPC_Data, src As NPC_Data)
        Dim d = npc.Record, s = src.Record
        If s.NamePresente Then d.Name = s.Name Else d.QuitarSubrecord("FULL")
        If s.ShortNamePresente Then d.ShortName = s.ShortName Else d.QuitarSubrecord("SHRT")
        d.ConfigurationFlags = MergeMaskedFlags(d.ConfigurationFlags, s.ConfigurationFlags,
                                                NpcTemplateHelpers.BaseDataAcbsFlagsMask)
    End Sub

    Private Shared Sub MaterializeStats(npc As NPC_Data, src As NPC_Data)
        Dim d = npc.Record, s = src.Record
        ' DNAM: en Fallout 4 son las estadisticas calculadas y en Skyrim el bloque de habilidades. Se copia
        ' el subrecord entero, que es lo que el motor copia por esta categoria.
        CopiarDnam(d, s)
        d.ConfigurationFlags = MergeMaskedFlags(d.ConfigurationFlags, s.ConfigurationFlags,
                                                NpcTemplateHelpers.StatsAcbsFlagsMask)
        d.PonerNivelDeConfiguracion(s.NivelDeConfiguracion())
        d.ConfigurationCalcMinLevel = s.ConfigurationCalcMinLevel
        d.ConfigurationCalcMaxLevel = s.ConfigurationCalcMaxLevel
        Dim sf4 = TryCast(s, Canon.NpcFO4), df4 = TryCast(d, Canon.NpcFO4)
        If sf4 IsNot Nothing AndAlso df4 IsNot Nothing Then
            df4.ConfigurationXPValueOffset = sf4.ConfigurationXPValueOffset
        End If
        Dim ss = TryCast(s, Canon.NpcSSE), ds = TryCast(d, Canon.NpcSSE)
        If ss IsNot Nothing AndAlso ds IsNot Nothing Then
            ds.ConfigurationMagickaOffset = ss.ConfigurationMagickaOffset
            ds.ConfigurationStaminaOffset = ss.ConfigurationStaminaOffset
            ds.ConfigurationSpeedMultiplier = ss.ConfigurationSpeedMultiplier
            ds.ConfigurationHealthOffset = ss.ConfigurationHealthOffset
        End If
    End Sub

    ''' <summary>DNAM completo. Los dos juegos guardan cosas distintas ahi: Fallout 4 salud y puntos de
    ''' accion calculados, Skyrim las habilidades del jugador con sus valores y desplazamientos.</summary>
    Private Shared Sub CopiarDnam(destino As Canon.INpc, origen As Canon.INpc)
        Dim sf4 = TryCast(origen, Canon.NpcFO4), df4 = TryCast(destino, Canon.NpcFO4)
        If sf4 IsNot Nothing AndAlso df4 IsNot Nothing Then
            If Not sf4.CalculatedHealthPresente Then
                destino.QuitarSubrecord("DNAM")
                Return
            End If
            df4.CalculatedHealth = sf4.CalculatedHealth
            df4.CalculatedActionPoints = sf4.CalculatedActionPoints
            df4.FarAwayModelDistance = sf4.FarAwayModelDistance
            df4.GearedUpWeapons = sf4.GearedUpWeapons
            Return
        End If
        Dim ss = TryCast(origen, Canon.NpcSSE), ds = TryCast(destino, Canon.NpcSSE)
        If ss Is Nothing OrElse ds Is Nothing Then Return
        If Not ss.PlayerSkillsHealthPresente Then
            destino.QuitarSubrecord("DNAM")
            Return
        End If
        ds.PlayerSkillsHealth = ss.PlayerSkillsHealth
        ds.PlayerSkillsMagicka = ss.PlayerSkillsMagicka
        ds.PlayerSkillsStamina = ss.PlayerSkillsStamina
        ds.PlayerSkillsFarAwayModelDistance = ss.PlayerSkillsFarAwayModelDistance
        ds.PlayerSkillsGearedUpWeapons = ss.PlayerSkillsGearedUpWeapons
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

    Private Shared Function MergeMaskedFlags(current As UInteger, source As UInteger, mask As UInteger) As UInteger
        Return (current And Not mask) Or (source And mask)
    End Function

    ''' <summary>Baja el bit de <paramref name="category"/> en las banderas de plantilla, para que el emisor
    ''' escriba el valor bajado y el motor saltee la copia de esa categoria.</summary>
    Private Shared Sub ClearFlagBit(npc As NPC_Data, category As NPC_TemplateCategory)
        Dim mask As UShort = CUShort(1 << CInt(category))
        npc.Record.ConfigurationTemplateFlags = CUShort(npc.Record.ConfigurationTemplateFlags And Not mask)
    End Sub

End Class
