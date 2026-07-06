Imports FO4_Base_Library

''' <summary>Makes a template-inheriting NPC OWN a template-flag category, so an edit to a field in
''' that category survives in-game instead of being overwritten by the engine's template resolution.
'''
''' WHY (RE, engine-verified — see memory project_re_template_flag_inheritance): at NPC resolution
''' (load/init) the engine runs <c>CopyFromTemplate</c> (Fallout4.exe 0x140657D80): for each template
''' data-category whose Use-X component is present (= the Use-X flag is set) it copies the TEMPLATE's
''' fields OVER the NPC's own — verdict (c), template wins, per-category, no additive merge. Proof for
''' Skin: <c>[dest+0x2A0] = [template+0x2A0]</c> at 0x140658073→0x14065807A; for the effective/skeleton
''' race <c>[dest+0x1B8] = [template+0x1B8]</c> at 0x140657FD5→0x140657FDC. If the Use-X flag is CLEAR
''' the copy is skipped and the NPC's own fields are used. So to make a category editable/authoritative:
''' clear its Use-X bit AND write the resolved (template-chain) values into the NPC's own record — else
''' clearing the flag would leave those fields empty/default and break the look.
'''
''' USAGE (the caller — e.g. the Object Template editor — orders it as: MATERIALIZE → CLEAR FLAG → APPLY
''' EDIT): call <see cref="MakeCategoryOwn"/> to snapshot the resolved category into the NPC and drop the
''' flag, THEN apply the user's edit on top. This helper is OPT-IN and does NOT touch the normal save
''' path; NPCs that don't inherit the category (flag already clear) are returned untouched.
'''
''' COVERAGE: <see cref="NPC_TemplateCategory.Traits"/> materializes the appearance/identity set the CK
''' groups under "Use Traits" AND the engine copies under idx 0 (Race, Skin, HairColor, FacialHairColor,
''' Head Parts, Death Item, Far-Away-Model, Height, Weight, face morphs/tints/body-morphs, and the OBTS
''' object-template combinations). Fields the app does not yet model (Voice VTCK, Disposition, Alignment,
''' Weapon List, Attach-Parent-Slots) are NOT materialized — flagged in <see cref="UnmodeledTraitsFields"/>
''' so a future pass can close them; they are preserved verbatim from the NPC (round-trip), which is
''' correct when they were already the NPC's own and a conservative best-effort otherwise. Other categories
''' (Stats/Factions/Inventory/Keywords/…) currently only clear the bit (their field materialization is a
''' follow-up); the engine's per-category rule is identical, so the framework generalizes.</summary>
Friend NotInheritable Class NpcTemplateMaterializer

    Private Sub New()
    End Sub

    ''' <summary>Traits sub-fields the app does not model yet, so they are not materialized here. Kept as a
    ''' visible TODO surface (and for a caller that wants to warn the user).</summary>
    Friend Shared ReadOnly UnmodeledTraitsFields As String() =
        {"Voice (VTCK)", "Disposition (ACBS)", "Alignment", "Weapon List", "Attach-Parent-Slots"}

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
    ''' (skin WNAM, hair/facial-hair color, head parts, body weight, face morphs/tints/regions, body-morph
    ''' regions, facial-morph intensity) are NOT materialized — the overlay has already populated them on the
    ''' save shadow and must win. The template still fills the NON-overlaid Traits fields (Race, Death Item,
    ''' Far-Away Model, Height, Object Template), so nothing falls back to the record's empty own-value.</param>
    ''' <returns>True when the flag was cleared (the NPC changed); False when it was already own.</returns>
    Friend Shared Function MakeCategoryOwn(npc As NPC_Data,
                                           category As NPC_TemplateCategory,
                                           getParsedNpc As Func(Of UInteger, NPC_Data),
                                           Optional skipOverlayOwned As Boolean = False) As Boolean
        If npc Is Nothing Then Return False
        If Not NpcTemplateHelpers.HasTemplateFlag(npc.TemplateFlags, category) Then Return False

        ' Only the appearance set (Traits) is field-materialized today; other categories clear-only.
        If category = NPC_TemplateCategory.Traits Then
            Dim src = ResolveCategorySource(npc, category, getParsedNpc)
            If src IsNot Nothing Then MaterializeTraits(npc, src, skipOverlayOwned)
        End If

        ClearFlagBit(npc, category)
        Return True
    End Function

    ''' <summary>Walk the template chain for <paramref name="category"/> to the terminal NPC that actually
    ''' provides the category's values (the deepest source that itself does not inherit the category), so
    ''' the materialized values equal what the engine's <c>CopyFromTemplate</c> chain would land on. Returns
    ''' Nothing if there is no template source.</summary>
    Private Shared Function ResolveCategorySource(npc As NPC_Data,
                                                  category As NPC_TemplateCategory,
                                                  getParsedNpc As Func(Of UInteger, NPC_Data)) As NPC_Data
        Dim current = npc
        Dim seen As New HashSet(Of UInteger)
        For depth = 0 To MaxChainDepth - 1
            If Not NpcTemplateHelpers.HasTemplateFlag(current.TemplateFlags, category) Then
                ' current owns the category → it is the source (unless it IS the original npc, which by
                ' contract inherits, so we only get here after ≥1 hop).
                Return If(ReferenceEquals(current, npc), Nothing, current)
            End If
            Dim srcFid = NpcTemplateHelpers.ResolveTemplateSourceFormID(current, category)
            If srcFid = 0UI OrElse Not seen.Add(srcFid) Then Return Nothing   ' no source, or cycle
            Dim next_ = getParsedNpc(srcFid)
            If next_ Is Nothing Then Return Nothing
            current = next_
        Next
        Return Nothing
    End Function

    ''' <summary>Copy the Traits appearance/identity set from the resolved source into <paramref name="npc"/>.
    ''' Unconditional per-field overwrite (verdict c): the source is exactly what the engine would have
    ''' copied, so the NPC ends up identical to its in-template look. The caller re-applies the user's edit
    ''' AFTER this, so the edited field is not lost.</summary>
    Private Shared Sub MaterializeTraits(npc As NPC_Data, src As NPC_Data, Optional skipOverlayOwned As Boolean = False)
        ' Scalar identity FormIDs the overlay never owns — always materialize (with Has* emit-gate).
        npc.RaceFormID = src.RaceFormID
        npc.DeathItemFormID = src.DeathItemFormID
        npc.HasDeathItem = src.HasDeathItem
        npc.FarAwayModelFormID = src.FarAwayModelFormID
        npc.HasFarAwayModel = src.HasFarAwayModel
        npc.IsFemale = src.IsFemale

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
    End Sub

    ''' <summary>Drop <paramref name="category"/>'s bit from both TemplateFlags mirrors (NPC_Data + ACBS
    ''' struct) so the writer emits the cleared value and the engine skips the category's template copy.</summary>
    Private Shared Sub ClearFlagBit(npc As NPC_Data, category As NPC_TemplateCategory)
        Dim mask As UShort = CUShort(1 << CInt(category))
        npc.TemplateFlags = CUShort(npc.TemplateFlags And Not mask)
        If npc.Acbs IsNot Nothing Then npc.Acbs.TemplateFlags = npc.TemplateFlags
    End Sub

    ' ---- deep-copy helpers (avoid aliasing the source NPC's parsed sub-objects) ----

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
