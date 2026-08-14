Imports FO4_Base_Library

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
''' <para>â›” LA INVARIANTE QUE MANTIENE ESTO HONESTO: todo campo que lleve <see cref="MainForm.TraitsState"/>
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
                        npc.KeywordFormIDs = New List(Of UInteger)(resolution.Source.KeywordFormIDs)
                        npc.HasKsizCounter = resolution.Source.HasKsizCounter
                    Case NPC_TemplateCategory.Factions
                        npc.Factions = CloneFactions(resolution.Source.Factions)
                    Case NPC_TemplateCategory.Inventory
                        npc.Inventory = CloneInventory(resolution.Source.Inventory)
                        npc.HasCoctCounter = resolution.Source.HasCoctCounter
                    Case NPC_TemplateCategory.SpellList
                        npc.ActorEffectFormIDs = New List(Of UInteger)(resolution.Source.ActorEffectFormIDs)
                        npc.HasSpctCounter = resolution.Source.HasSpctCounter
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
                ' ⛔ THE FLAG STAYS SET. This is NOT the leveled-list case (that one gets pinned above) — it is
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
        If npc Is Nothing OrElse Not NpcTemplateHelpers.HasTemplateFlag(npc.TemplateFlags, category) Then
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
            If Not NpcTemplateHelpers.HasTemplateFlag(current.TemplateFlags, category) Then
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
        If npc Is Nothing OrElse Not NpcTemplateHelpers.HasTemplateFlag(npc.TemplateFlags, category) Then Return npc
        Dim resolution = ResolveCategorySource(npc, category, getParsedNpc, resolveLvlnPick)
        If resolution.Outcome = MaterializeOutcome.Materialized OrElse
           resolution.Outcome = MaterializeOutcome.MaterializedFromLeveledPick Then Return resolution.Source
        Return npc
    End Function

    ''' <summary>Copy the Traits appearance/identity set from the resolved source into <paramref name="npc"/>.
    ''' Unconditional per-field overwrite (verdict c): the source is exactly what the engine would have
    ''' copied, so the NPC ends up identical to its in-template look. The caller re-applies the user's edit
    ''' AFTER this, so the edited field is not lost.</summary>
    Private Shared Sub MaterializeTraits(npc As NPC_Data, src As NPC_Data, Optional skipOverlayOwned As Boolean = False)
        ' Scalar identity FormIDs the overlay never owns — always materialize (with Has* emit-gate).
        npc.RaceFormID = src.RaceFormID
        npc.HasRace = src.HasRace
        npc.VoiceFormID = src.VoiceFormID
        npc.HasVoice = src.HasVoice
        npc.DeathItemFormID = src.DeathItemFormID
        npc.HasDeathItem = src.HasDeathItem
        npc.FarAwayModelFormID = src.FarAwayModelFormID
        npc.HasFarAwayModel = src.HasFarAwayModel
        npc.IsFemale = src.IsFemale
        EnsureAcbs(npc)
        npc.Acbs.Flags = MergeMaskedFlags(CurrentAcbsFlags(npc), CurrentAcbsFlags(src), NpcTemplateHelpers.TraitsAcbsFlagsMask)
        npc.AcbsFlags = npc.Acbs.Flags
        If src.Acbs IsNot Nothing Then
            npc.Acbs.DispositionBase = src.Acbs.DispositionBase
        End If
        npc.ClassFormID = src.ClassFormID
        npc.HasClass = src.HasClass
        npc.CombatStyleFormID = src.CombatStyleFormID
        npc.HasCombatStyle = src.HasCombatStyle

        ' Height — not overlay-owned.
        npc.HeightMin = src.HeightMin
        npc.HasHeightMin = src.HasHeightMin
        npc.HeightMax = src.HeightMax
        npc.HasHeightMax = src.HasHeightMax

        ' Overlay-OWNED appearance/body fields: skip when a LooksMenu overlay already populated them on the shadow
        ' (skipOverlayOwned) so the overlay wins; otherwise materialize from the template as before.
        If Not skipOverlayOwned Then
            npc.SkinFormID = src.SkinFormID
            npc.HasSkin = src.HasSkin
            npc.HairColorFormID = src.HairColorFormID
            npc.FacialHairColorFormID = src.FacialHairColorFormID
            ' HCLF/BCLF EMIT GATES. The values above travelled since day one but their Has* flags did not,
            ' so a materialized NPC whose OWN record carried no HCLF got the template's colour in memory and
            ' NO subrecord in the ESP (NpcSubrecordWriter.vb:80/81 gate on the flag alone) — the exact shape
            ' of the HasHairColor bug already fixed on the overlay side. MEASURED (TemplateTraitsProbe, real
            ' load orders): SSE 2 NPCs, FO4 0.
            npc.HasHairColor = src.HasHairColor
            npc.HasFacialHairColor = src.HasFacialHairColor
            ' FTST (face TextureSet) — Traits bucket per TraitsState, resolved through the chain by the RENDER
            ' (NpcStateResolver.ResolveTraitsStateFromNPC → state.HeadTextureFormID). It was never materialized,
            ' so clearing Use-Traits dropped the inherited face TXST and the head fell back to the race DFTM in
            ' ApplyRaceFallbacks. The pair travels together — HasHeadTexture is the writer's FTST emit gate
            ' (NpcSubrecordWriter.vb:113) and a value without its flag is written as nothing at all.
            ' MEASURED: SSE 40 NPCs lose a real FTST (e.g. TreasCorpseVampire* ← EncVampire01*F = 02006F9C);
            ' FO4 0 (its templated NPCs duplicate the value on their own record — latent there, not silent).
            npc.HeadTextureFormID = src.HeadTextureFormID
            npc.HasHeadTexture = src.HasHeadTexture
            ' QNAM (texture lighting / body skin tone). THREE fields, not two: the RENDER reads the
            ' Has+Color pair (state.TextureLightingColor → TryApplyBodySkinSoftLight) but the WRITER emits the
            ' subrecord ONLY from TextureLightingFloats (NpcSubrecordWriter.vb:114 EmitQnam, Nothing ⇒ no QNAM).
            ' Copying just the pair would fix the preview and leave the ESP silently unchanged — the same
            ' split that makes this bug class invisible. MEASURED: SSE 40 NPCs, FO4 0.
            npc.HasTextureLighting = src.HasTextureLighting
            npc.TextureLightingColor = src.TextureLightingColor
            npc.TextureLightingFloats = src.TextureLightingFloats
            npc.WeightThin = src.WeightThin
            npc.WeightMuscular = src.WeightMuscular
            npc.WeightFat = src.WeightFat
            npc.HeadPartFormIDs = New List(Of UInteger)(src.HeadPartFormIDs)
            npc.MorphValues = New Dictionary(Of UInteger, Single)(src.MorphValues)
            npc.FaceTintLayers = CloneTintLayers(src.FaceTintLayers)
            npc.FaceMorphs = CloneFaceMorphs(src.FaceMorphs)
            npc.BodyMorphRegionValues = New List(Of Single)(src.BodyMorphRegionValues)
            npc.FacialMorphIntensity = src.FacialMorphIntensity
        End If

        ' Object Template (OBTS) — the robot/OMOD combinations. Deep-copied so the NPC owns them outright.
        ' NOT overlay-owned (the overlay never touches OBTS). The Object Template editor overwrites/edits these
        ' AFTER MakeCategoryOwn returns via the record override.
        npc.ObjectTemplateCombinations = CloneObjectTemplateCombinations(src.ObjectTemplateCombinations)
        npc.HasObjectTemplate = src.HasObjectTemplate
        npc.ObjectTemplateOMODFormIDs = New List(Of UInteger)(src.ObjectTemplateOMODFormIDs)
        ' APPR (Attach Parent Slots) — rides the Traits chain alongside OBTS (TraitsState.AttachParentSlotFormIDs,
        ' seeded by CreateOwnTraitsState) and seeds the AP-pool filter in ObjectTemplateResolver, so dropping it
        ' while KEEPING the OBTS combinations would leave the robot path with combinations it can no longer
        ' filter. Not overlay-owned (the overlay never touches APPR — CopyRoundTripOnlyFieldsFromRaw carries it
        ' verbatim), hence outside the skip block, exactly like OBTS above.
        ' MEASURED: 0 NPCs lose a value in either vanilla load order (the templated ones duplicate APPR on their
        ' own record); materialized for the same reason OBTS is — the chain is what the app reads it through.
        npc.AttachParentSlotFormIDs = New List(Of UInteger)(If(src.AttachParentSlotFormIDs, New List(Of UInteger)))
    End Sub

    Private Shared Sub MaterializeBaseData(npc As NPC_Data, src As NPC_Data)
        npc.FullName = src.FullName
        npc.HasFull = src.HasFull
        npc.ShortName = src.ShortName
        npc.HasShortName = src.HasShortName
        EnsureAcbs(npc)
        npc.Acbs.Flags = MergeMaskedFlags(CurrentAcbsFlags(npc), CurrentAcbsFlags(src), NpcTemplateHelpers.BaseDataAcbsFlagsMask)
        npc.AcbsFlags = npc.Acbs.Flags
    End Sub

    Private Shared Sub MaterializeStats(npc As NPC_Data, src As NPC_Data)
        npc.CalculatedStats = CloneCalculatedStats(src.CalculatedStats)
        npc.SsePlayerSkills = ClonePlayerSkills(src.SsePlayerSkills)
        EnsureAcbs(npc)
        npc.Acbs.Flags = MergeMaskedFlags(CurrentAcbsFlags(npc), CurrentAcbsFlags(src), NpcTemplateHelpers.StatsAcbsFlagsMask)
        npc.AcbsFlags = npc.Acbs.Flags
        If src.Acbs Is Nothing Then Return
        npc.Acbs.XpValueOffset = src.Acbs.XpValueOffset
        npc.Acbs.LevelOrLevelMult = src.Acbs.LevelOrLevelMult
        npc.Acbs.CalcMinLevel = src.Acbs.CalcMinLevel
        npc.Acbs.CalcMaxLevel = src.Acbs.CalcMaxLevel
        npc.Acbs.MagickaOffset = src.Acbs.MagickaOffset
        npc.Acbs.StaminaOffset = src.Acbs.StaminaOffset
        npc.Acbs.SpeedMultiplier = src.Acbs.SpeedMultiplier
        npc.Acbs.HealthOffset = src.Acbs.HealthOffset
    End Sub

    Private Shared Sub EnsureAcbs(npc As NPC_Data)
        If npc.Acbs Is Nothing Then npc.Acbs = New NPC_AcbsData()
    End Sub

    Private Shared Function MergeMaskedFlags(current As UInteger, source As UInteger, mask As UInteger) As UInteger
        Return (current And Not mask) Or (source And mask)
    End Function

    Private Shared Function CurrentAcbsFlags(npc As NPC_Data) As UInteger
        If npc Is Nothing Then Return 0UI
        Return If(npc.Acbs IsNot Nothing, npc.Acbs.Flags, npc.AcbsFlags)
    End Function

    ''' <summary>Drop <paramref name="category"/>'s bit from both TemplateFlags mirrors (NPC_Data + ACBS
    ''' struct) so the writer emits the cleared value and the engine skips the category's template copy.</summary>
    Private Shared Sub ClearFlagBit(npc As NPC_Data, category As NPC_TemplateCategory)
        Dim mask As UShort = CUShort(1 << CInt(category))
        npc.TemplateFlags = CUShort(npc.TemplateFlags And Not mask)
        If npc.Acbs IsNot Nothing Then npc.Acbs.TemplateFlags = npc.TemplateFlags
    End Sub

    ' ---- deep-copy helpers (avoid aliasing the source NPC's parsed sub-objects) ----

    Private Shared Function CloneFactions(src As List(Of NPC_FactionEntry)) As List(Of NPC_FactionEntry)
        Dim dst As New List(Of NPC_FactionEntry)
        If src Is Nothing Then Return dst
        For Each f In src
            dst.Add(New NPC_FactionEntry With {.FactionFormID = f.FactionFormID, .Rank = f.Rank,
                    .SseUnused = If(f.SseUnused Is Nothing, Nothing, CType(f.SseUnused.Clone(), Byte()))})
        Next
        Return dst
    End Function

    Private Shared Function CloneInventory(src As List(Of NPC_InventoryItem)) As List(Of NPC_InventoryItem)
        Dim dst As New List(Of NPC_InventoryItem)
        If src Is Nothing Then Return dst
        For Each i In src
            dst.Add(New NPC_InventoryItem With {.ItemFormID = i.ItemFormID, .Count = i.Count, .HasCoed = i.HasCoed,
                    .CoedOwnerFormID = i.CoedOwnerFormID, .CoedOwnerExtra = i.CoedOwnerExtra,
                    .CoedExtraIsFormID = i.CoedExtraIsFormID, .CoedItemCondition = i.CoedItemCondition})
        Next
        Return dst
    End Function

    Private Shared Function CloneCalculatedStats(src As NPC_CalculatedStats) As NPC_CalculatedStats
        If src Is Nothing Then Return Nothing
        Return New NPC_CalculatedStats With {.CalculatedHealth = src.CalculatedHealth,
                .CalculatedActionPoints = src.CalculatedActionPoints, .FarAwayModelDistance = src.FarAwayModelDistance,
                .GearedUpWeapons = src.GearedUpWeapons, .Unused7 = src.Unused7}
    End Function

    Private Shared Function ClonePlayerSkills(src As NPC_SsePlayerSkills) As NPC_SsePlayerSkills
        If src Is Nothing Then Return Nothing
        Return MainForm.ClonePlayerSkills(src)
    End Function

    Private Shared Function CloneTintLayers(src As List(Of NPC_FaceTintLayerData)) As List(Of NPC_FaceTintLayerData)
        Dim dst As New List(Of NPC_FaceTintLayerData)
        If src Is Nothing Then Return dst
        For Each l In src
            dst.Add(New NPC_FaceTintLayerData With {
                .Discriminator = l.Discriminator, .Index = l.Index, .Value = l.Value, .Color = l.Color,
                .TemplateColorIndex = l.TemplateColorIndex,
                .RawTetiBytes = If(l.RawTetiBytes Is Nothing, Nothing, CType(l.RawTetiBytes.Clone(), Byte())),
                .RawTendBytes = If(l.RawTendBytes Is Nothing, Nothing, CType(l.RawTendBytes.Clone(), Byte()))})
        Next
        Return dst
    End Function

    Private Shared Function CloneFaceMorphs(src As List(Of NPC_FaceMorphData)) As List(Of NPC_FaceMorphData)
        Dim dst As New List(Of NPC_FaceMorphData)
        If src Is Nothing Then Return dst
        For Each m In src
            dst.Add(New NPC_FaceMorphData With {
                .Index = m.Index,
                .Values = New List(Of Single)(m.Values),
                .SourcePlugin = m.SourcePlugin,
                .RawFmriBytes = If(m.RawFmriBytes Is Nothing, Nothing, CType(m.RawFmriBytes.Clone(), Byte())),
                .RawFmrsBytes = If(m.RawFmrsBytes Is Nothing, Nothing, CType(m.RawFmrsBytes.Clone(), Byte()))})
        Next
        Return dst
    End Function

    Private Shared Function CloneObjectTemplateCombinations(src As List(Of NPC_ObjectTemplateCombination)) As List(Of NPC_ObjectTemplateCombination)
        Dim dst As New List(Of NPC_ObjectTemplateCombination)
        If src Is Nothing Then Return dst
        For Each c In src
            dst.Add(New NPC_ObjectTemplateCombination With {
                .IsEditorOnly = c.IsEditorOnly,
                .DisplayName = c.DisplayName,
                .Combination = c.Combination,
                .RawObtsBytes = If(c.RawObtsBytes Is Nothing, Nothing, CType(c.RawObtsBytes.Clone(), Byte()))})
        Next
        Return dst
    End Function

End Class
