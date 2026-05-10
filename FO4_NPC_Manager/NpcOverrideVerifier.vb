Imports System.IO
Imports System.Text
Imports FO4_Base_Library

' ============================================================================
' NpcOverrideVerifier — read-back verifier for the Save ESP/ESM round-trip.
' App-specific debug helper for NPC_Manager. NOT in the shared library because
' it consumes app-level state (npcSpec produced by the Save handler) and writes
' app-level diagnostics (NpcPreviewLog).
'
' After SaveNpcEspWriter writes a plugin, the verifier loads it back from disk,
' parses the NPC override with ParseNPC, and compares the result field-by-field
' against the in-memory npcSpec that was supposed to be written. Any divergence
' is collected into a list of human-readable diff lines.
'
' Designed for the diagnostic log path — caller should gate it on a log flag
' so production runs don't pay the read-back cost (loading a small auto-gen
' plugin + parsing one record is cheap, but not free).
'
' Usage:
'   Dim res = NpcOverrideVerifier.VerifyWrittenOverride(targetPath, expected,
'                                                       sourcePluginName,
'                                                       basePluginManager)
'   If res.Match Then ... else log res.Differences
' ============================================================================

Public Module NpcOverrideVerifier

    Public Class VerifyResult
        Public Match As Boolean
        Public Differences As New List(Of String)
        ''' <summary>Set when the verifier could not even load+parse the written record.
        ''' Match is False, Differences is empty, FatalError carries the reason.</summary>
        Public FatalError As String = ""
    End Class

    ''' <summary>Read back the just-written plugin, locate the NPC override matching the
    ''' expected FormID, and compare its fields against <paramref name="expected"/>.
    '''
    ''' FormID resolution: the written plugin has its own MAST list with re-mapped indices,
    ''' independent from <paramref name="basePluginManager"/>. To compare apples to apples,
    ''' we build a temporary PluginManager containing only the written plugin (so its
    ''' MAST entries resolve to load-order positions inside this temp manager) and then
    ''' translate FormIDs on both sides to "source-plugin-relative" form: master plugin
    ''' name + local ObjectID. That lets us compare without depending on either side's
    ''' load-order indexing.</summary>
    Public Function VerifyWrittenOverride(writtenPath As String,
                                          expected As NPC_Data,
                                          expectedSourcePluginName As String,
                                          basePluginManager As PluginManager) As VerifyResult
        Dim res As New VerifyResult

        If Not File.Exists(writtenPath) Then
            res.FatalError = $"Verifier: written plugin not found at {writtenPath}"
            Return res
        End If

        Dim reader As New PluginReader()
        Try
            reader.Load(writtenPath)
        Catch ex As Exception
            res.FatalError = $"Verifier: PluginReader.Load failed: {ex.Message}"
            Return res
        End Try

        ' Find the NPC record. The written plugin's record header carries a FormID whose
        ' high byte indexes into THIS plugin's MAST list, NOT the basePluginManager's load
        ' order. SaveNpcEspWriter remapped global → local at write time
        ' ([SaveNpcEspWriter.vb:458] via the FormIdRemapper closure built around the new
        ' MAST cleanup). The verifier has to do the same remap on the expected FormID before
        ' looking it up, otherwise a write that succeeded looks like "record not found".
        '
        ' Lookup: find expected's source-master name in basePluginManager, locate that name
        ' in reader.Masters → that's the new high byte. Local ObjectID is invariant.
        Dim expectedHighByte As Integer = CInt((expected.FormID >> 24) And &HFFUI)
        Dim expectedSourceMasterName As String = ""
        If expectedHighByte >= 0 AndAlso expectedHighByte < basePluginManager.Plugins.Count Then
            Dim sourcePlugin = basePluginManager.Plugins(expectedHighByte)
            If sourcePlugin IsNot Nothing Then expectedSourceMasterName = sourcePlugin.FileName
        End If

        Dim expectedLocalFormID As UInteger = expected.FormID
        If Not String.IsNullOrEmpty(expectedSourceMasterName) Then
            Dim newHigh As Integer = -1
            For i = 0 To reader.Masters.Count - 1
                If String.Equals(reader.Masters(i), expectedSourceMasterName, StringComparison.OrdinalIgnoreCase) Then
                    newHigh = i
                    Exit For
                End If
            Next
            If newHigh < 0 Then
                ' Not in MAST list → record was written as "self" (high byte = Masters.Count).
                ' Rare for an override but possible if cleanup determined the source plugin
                ' was the writer itself.
                newHigh = reader.Masters.Count
            End If
            expectedLocalFormID = (CUInt(newHigh) << 24) Or (expected.FormID And &HFFFFFFUI)
        End If

        Dim writtenRec As PluginRecord = Nothing
        For Each kvp In reader.Records
            If kvp.Value.Header.FormID = expectedLocalFormID Then
                writtenRec = kvp.Value
                Exit For
            End If
        Next
        If writtenRec Is Nothing Then
            res.FatalError = $"Verifier: NPC FormID 0x{expected.FormID:X8} (local 0x{expectedLocalFormID:X8} after MAST remap; source master '{expectedSourceMasterName}') not found in written plugin (records loaded: {reader.Records.Count}, masters: {String.Join(", ", reader.Masters)})"
            Return res
        End If

        ' Build a temp PluginManager that contains the same masters as basePluginManager
        ' PLUS the written plugin loaded after them, so the high-byte → plugin name
        ' mapping inside the written plugin's records resolves correctly to plugins the
        ' base manager knows about.
        '
        ' Instead of a full PluginManager rebuild (expensive), we do a manual resolve:
        ' walk the written reader's MAST list, map each name to its base-manager load
        ' order index, and translate every FormID we encounter.
        Dim parsed As NPC_Data = Nothing
        Try
            parsed = ParseAndResolveAgainstBase(writtenRec, reader, basePluginManager)
        Catch ex As Exception
            res.FatalError = $"Verifier: ParseNPC failed on read-back: {ex.Message}"
            Return res
        End Try

        ' Now compare. Both sides are NPC_Data; we walk fields and report mismatches.
        CompareNpcData(expected, parsed, res.Differences)
        res.Match = res.Differences.Count = 0
        Return res
    End Function

    ''' <summary>Parse the NPC record from the written plugin and translate its FormID
    ''' references through the written plugin's MAST list into base-manager load-order
    ''' indices. The result has FormIDs in the same global form as the expected NPC_Data,
    ''' allowing direct field-by-field comparison.
    '''
    ''' Approach: build a translation table writtenMastIdx → baseLoadOrderIdx by matching
    ''' master names. Walk every PluginRecord subrecord, find FormID-shaped 4-byte payloads,
    ''' rewrite the high byte through the translation. Then parse the rewritten record with
    ''' the basePluginManager so all the renderer-style FormIDs end up in base global form.
    '''
    ''' Why not use ResolveReferencedFormID directly: PluginManager._pluginIndex is private
    ''' and only populated by LoadAllPlugins. Just doing Plugins.Add(writtenReader) doesn't
    ''' make ResolveReferencedFormID(writtenReader.FileName, ...) work — the lookup misses.
    ''' Translating the bytes upfront sidesteps that limitation.</summary>
    Private Function ParseAndResolveAgainstBase(writtenRec As PluginRecord,
                                                writtenReader As PluginReader,
                                                basePluginManager As PluginManager) As NPC_Data
        ' Build the translation: written-plugin MAST index → base load-order index. Both
        ' fit in the high byte. "Self" entries (the written plugin's own records) — high
        ' byte = writtenReader.Masters.Count — translate to itself; expected side has them
        ' as the source plugin's own FormID anyway.
        Dim translation(255) As Integer
        For i = 0 To translation.Length - 1
            translation(i) = -1  ' sentinel: "no translation, keep as-is"
        Next
        For i = 0 To writtenReader.Masters.Count - 1
            Dim masterName = writtenReader.Masters(i)
            For j = 0 To basePluginManager.Plugins.Count - 1
                If String.Equals(basePluginManager.Plugins(j).FileName, masterName, StringComparison.OrdinalIgnoreCase) Then
                    translation(i) = j
                    Exit For
                End If
            Next
        Next
        ' "Self" master index for the written plugin (where its own records live in its
        ' own MAST scheme) = writtenReader.Masters.Count. Translate to whatever the source
        ' plugin's load-order index is in the base manager. The expected NPC_Data carries
        ' the source plugin's load-order index in high bytes for self-references.
        Dim selfMastIdx = writtenReader.Masters.Count
        If selfMastIdx <= 255 Then
            ' Find the source plugin (= writtenReader.FileName) in base manager.
            Dim baseSelfIdx As Integer = -1
            For j = 0 To basePluginManager.Plugins.Count - 1
                If String.Equals(basePluginManager.Plugins(j).FileName, writtenReader.FileName, StringComparison.OrdinalIgnoreCase) Then
                    baseSelfIdx = j
                    Exit For
                End If
            Next
            ' If the written plugin isn't loaded in base (typical: just-saved plugin), keep
            ' the raw self index. That maps to one of the source masters in expected, since
            ' overrides preserve their original FormID's high byte (= source plugin's MAST
            ' for its own records via ResolveFormID's selfIdx fallback).
            If baseSelfIdx >= 0 Then translation(selfMastIdx) = baseSelfIdx
        End If

        ' Build a translated copy of the record so ParseNPC sees base-relative FormIDs.
        Dim translatedRec As New PluginRecord With {
            .Header = writtenRec.Header,
            .SourcePluginName = writtenRec.SourcePluginName,
            .SourcePluginIsLocalized = writtenRec.SourcePluginIsLocalized
        }
        ' Translate the record's own FormID high byte too (for the FormID property check).
        translatedRec.Header.FormID = TranslateFormId(writtenRec.Header.FormID, translation)
        For Each subrec In writtenRec.Subrecords
            Dim newData As Byte() = subrec.Data
            If subrec.Data IsNot Nothing AndAlso subrec.Data.Length > 0 Then
                newData = TranslateAllFormIdsInSubrecord(subrec.Signature, subrec.Data, translation)
            End If
            translatedRec.Subrecords.Add(New SubrecordData With {
                .Signature = subrec.Signature,
                .Data = newData
            })
        Next

        ' Parse against base manager. We pass a sourcePluginName that exists in base so
        ' ResolveReferencedFormID's _pluginIndex lookup succeeds. The translated bytes
        ' already have base-relative high bytes, so ResolveFormID is essentially a no-op
        ' (maps base-idx-N → base-idx-N).
        Dim sourcePlugin = writtenRec.SourcePluginName
        If String.IsNullOrEmpty(sourcePlugin) OrElse Not basePluginManager.Plugins.Any(Function(p) String.Equals(p.FileName, sourcePlugin, StringComparison.OrdinalIgnoreCase)) Then
            ' Pick any base plugin name as the source — it just needs to be a known key.
            ' Since we already translated the bytes, ResolveFormID's master-list walk is
            ' irrelevant; only the _pluginIndex hit on the source name matters.
            If basePluginManager.Plugins.Count > 0 Then sourcePlugin = basePluginManager.Plugins(0).FileName
        End If
        Return RecordParsers.ParseNPC(translatedRec, sourcePlugin, basePluginManager)
    End Function

    ''' <summary>Translate a single u32 FormID's high byte through the translation table.
    ''' Returns the FormID with its master index rewritten to base-global. Sentinel -1 in
    ''' the table means "leave as-is".</summary>
    Private Function TranslateFormId(fid As UInteger, translation As Integer()) As UInteger
        If fid = 0UI Then Return 0UI
        Dim hi As Integer = CInt((fid >> 24) And &HFFUI)
        Dim mapped As Integer = translation(hi)
        If mapped < 0 Then Return fid
        Return (CUInt(mapped) << 24) Or (fid And &HFFFFFFUI)
    End Function

    ''' <summary>Walk a subrecord's payload, identify FormID positions per signature, and
    ''' rewrite each FormID's high byte through the translation. Returns a new byte array.
    '''
    ''' For NPC_ subrecords the FormID layout is well-known from xEdit + the parser: most
    ''' single-FormID subrecords (RNAM, WNAM, DOFT, SOFT, HCLF, etc.) have the FormID at
    ''' offset 0. PNAM/SPLO/PKID are similarly single-FormID per subrecord. Multi-FormID
    ''' subrecords (PRPS, KWDA, APPR, TPTA, SNAM, PRKR, CNTO, COED, ATKD, TETI/TEND not
    ''' applicable, OBTS, AIDT not applicable) need stride/offset tables.
    '''
    ''' Strategy: for known FormID-only signatures (4 bytes = pure FormID) translate the
    ''' whole 4 bytes. For arrays with strided FormIDs, translate at known offsets. For
    ''' anything else (including VMAD which has FormIDs at runtime-discovered positions),
    ''' leave the bytes as-is — the verifier will compare raw bytes for those (e.g.
    ''' VMAD.RawBytes) which means a binary-equivalent translation gap will show up as a
    ''' diff. That's a known limitation; for now FormID-only subrecords cover the common
    ''' renderer-edited fields (DOFT, SOFT, RNAM, WNAM, HCLF, BCLF, PNAM, etc.).</summary>
    Private Function TranslateAllFormIdsInSubrecord(signature As String, data As Byte(), translation As Integer()) As Byte()
        Dim result(data.Length - 1) As Byte
        Buffer.BlockCopy(data, 0, result, 0, data.Length)

        Select Case signature
            ' Single 4-byte FormID subrecords.
            Case "RNAM", "WNAM", "ANAM", "ATKR", "TPLT", "LTPT", "LTPC", "INAM", "VTCK",
                 "DOFT", "SOFT", "DPLT", "CRIF", "FTST", "FTSF", "HCLF", "BCLF", "ZNAM",
                 "GNAM", "CSCR", "PFRN", "CNAM", "PTRN", "STCP", "FTYP", "NTRM",
                 "SPOR", "OCOR", "GWOR", "ECOR", "FCPL", "RCLR", "PNAM", "SPLO", "PKID",
                 "DMDS", "ATKW", "ATKS"
                If data.Length >= 4 Then RewriteFormIdAt(result, 0, translation)

            ' KWDA + APPR: array of u32 FormIDs, stride 4.
            Case "KWDA", "APPR"
                Dim n = data.Length \ 4
                For i = 0 To n - 1
                    RewriteFormIdAt(result, i * 4, translation)
                Next

            ' TPTA: 13 fixed slots × 4 bytes = 52 bytes total.
            Case "TPTA"
                Dim n = Math.Min(data.Length \ 4, 13)
                For i = 0 To n - 1
                    RewriteFormIdAt(result, i * 4, translation)
                Next

            ' SNAM (Faction): 8 bytes per entry, FormID @ +0.
            Case "SNAM"
                If data.Length >= 4 Then RewriteFormIdAt(result, 0, translation)

            ' PRKR: 8 bytes per entry, FormID @ +0.
            Case "PRKR"
                If data.Length >= 4 Then RewriteFormIdAt(result, 0, translation)

            ' PRPS: stride 8: FormID + float.
            Case "PRPS"
                Dim n = data.Length \ 8
                For i = 0 To n - 1
                    RewriteFormIdAt(result, i * 8, translation)
                Next

            ' CNTO: 8 bytes: FormID + s32 count.
            Case "CNTO"
                If data.Length >= 4 Then RewriteFormIdAt(result, 0, translation)

            ' COED: 12 bytes: Owner FormID + union (4) + float.
            Case "COED"
                If data.Length >= 4 Then RewriteFormIdAt(result, 0, translation)
                ' Union slot — when Owner is FACT it's s32 rank (no FormID), when NPC_ it's
                ' a GLOB FormID. Without parsing the Owner first we don't know which. The
                ' verifier doesn't deeply compare COED today, so leave +4 alone.

            ' ATKD: 44 bytes, FormID at +8 (Attack Spell SPEL).
            Case "ATKD"
                If data.Length >= 12 Then RewriteFormIdAt(result, 8, translation)

            ' DSTD: 20 bytes, FormID at +8 (Explosion EXPL) and +12 (Debris DEBR).
            Case "DSTD"
                If data.Length >= 12 Then RewriteFormIdAt(result, 8, translation)
                If data.Length >= 16 Then RewriteFormIdAt(result, 12, translation)

            ' DAMC: stride 8: FormID + u32 value.
            Case "DAMC"
                Dim n = data.Length \ 8
                For i = 0 To n - 1
                    RewriteFormIdAt(result, i * 8, translation)
                Next

            ' CS2K (keyword), CS2D (sound) — single FormID @ 0.
            Case "CS2K", "CS2D"
                If data.Length >= 4 Then RewriteFormIdAt(result, 0, translation)

            ' OBTS, VMAD: complex layouts with FormIDs at runtime-discovered positions.
            ' Left as-is; verifier compares raw bytes for these so a translation gap shows
            ' up as a diff (acceptable known limitation for the diagnostic verifier).

            Case Else
                ' Unknown subrecord: leave bytes alone.
        End Select

        Return result
    End Function

    Private Sub RewriteFormIdAt(buf As Byte(), offset As Integer, translation As Integer())
        If offset + 4 > buf.Length Then Return
        Dim fid = BitConverter.ToUInt32(buf, offset)
        Dim newFid = TranslateFormId(fid, translation)
        If newFid = fid Then Return
        buf(offset + 0) = CByte(newFid And &HFFUI)
        buf(offset + 1) = CByte((newFid >> 8) And &HFFUI)
        buf(offset + 2) = CByte((newFid >> 16) And &HFFUI)
        buf(offset + 3) = CByte((newFid >> 24) And &HFFUI)
    End Sub

    ''' <summary>Field-by-field compare. Reports human-readable diff lines into
    ''' <paramref name="diffs"/>. Empty list = perfect match.</summary>
    Private Sub CompareNpcData(expected As NPC_Data, actual As NPC_Data, diffs As List(Of String))
        ' === Identity ===
        If expected.FormID <> actual.FormID Then
            diffs.Add($"FormID: expected 0x{expected.FormID:X8}, got 0x{actual.FormID:X8}")
        End If
        CompareString("EditorID", expected.EditorID, actual.EditorID, diffs)
        CompareString("FullName", expected.FullName, actual.FullName, diffs)

        ' === Optional FormID fields with Has-flag pairs ===
        CompareFormIdWithFlag("PreviewTransform", expected.HasPreviewTransform, expected.PreviewTransformFormID,
                              actual.HasPreviewTransform, actual.PreviewTransformFormID, diffs)
        CompareFormIdWithFlag("AnimationSound", expected.HasAnimationSound, expected.AnimationSoundFormID,
                              actual.HasAnimationSound, actual.AnimationSoundFormID, diffs)
        CompareFormIdWithFlag("DeathItem", expected.HasDeathItem, expected.DeathItemFormID,
                              actual.HasDeathItem, actual.DeathItemFormID, diffs)
        CompareFormIdWithFlag("Voice", expected.HasVoice, expected.VoiceFormID,
                              actual.HasVoice, actual.VoiceFormID, diffs)
        CompareFormIdWithFlag("Template", expected.HasTemplate, expected.TemplateFormID,
                              actual.HasTemplate, actual.TemplateFormID, diffs)
        CompareFormIdWithFlag("LegendaryTemplate", expected.HasLegendaryTemplate, expected.LegendaryTemplateFormID,
                              actual.HasLegendaryTemplate, actual.LegendaryTemplateFormID, diffs)
        CompareFormIdWithFlag("LegendaryChance", expected.HasLegendaryChance, expected.LegendaryChanceFormID,
                              actual.HasLegendaryChance, actual.LegendaryChanceFormID, diffs)
        CompareFormIdWithFlag("Race", expected.HasRace, expected.RaceFormID,
                              actual.HasRace, actual.RaceFormID, diffs)
        CompareFormIdWithFlag("Skin", expected.HasSkin, expected.SkinFormID,
                              actual.HasSkin, actual.SkinFormID, diffs)
        CompareFormIdWithFlag("FarAwayModel", expected.HasFarAwayModel, expected.FarAwayModelFormID,
                              actual.HasFarAwayModel, actual.FarAwayModelFormID, diffs)
        CompareFormIdWithFlag("AttackRace", expected.HasAttackRace, expected.AttackRaceFormID,
                              actual.HasAttackRace, actual.AttackRaceFormID, diffs)
        CompareFormIdWithFlag("SpectatorOverride", expected.HasSpectatorOverride, expected.SpectatorOverrideFormID,
                              actual.HasSpectatorOverride, actual.SpectatorOverrideFormID, diffs)
        CompareFormIdWithFlag("ObserveDeadBodyOverride", expected.HasObserveDeadBodyOverride, expected.ObserveDeadBodyOverrideFormID,
                              actual.HasObserveDeadBodyOverride, actual.ObserveDeadBodyOverrideFormID, diffs)
        CompareFormIdWithFlag("GuardWarnOverride", expected.HasGuardWarnOverride, expected.GuardWarnOverrideFormID,
                              actual.HasGuardWarnOverride, actual.GuardWarnOverrideFormID, diffs)
        CompareFormIdWithFlag("CombatOverride", expected.HasCombatOverride, expected.CombatOverrideFormID,
                              actual.HasCombatOverride, actual.CombatOverrideFormID, diffs)
        CompareFormIdWithFlag("FollowerCommand", expected.HasFollowerCommand, expected.FollowerCommandFormID,
                              actual.HasFollowerCommand, actual.FollowerCommandFormID, diffs)
        CompareFormIdWithFlag("FollowerElevator", expected.HasFollowerElevator, expected.FollowerElevatorFormID,
                              actual.HasFollowerElevator, actual.FollowerElevatorFormID, diffs)
        CompareFormIdWithFlag("ForcedLocRefType", expected.HasForcedLocRefType, expected.ForcedLocRefTypeFormID,
                              actual.HasForcedLocRefType, actual.ForcedLocRefTypeFormID, diffs)
        CompareFormIdWithFlag("NativeTerminal", expected.HasNativeTerminal, expected.NativeTerminalFormID,
                              actual.HasNativeTerminal, actual.NativeTerminalFormID, diffs)
        CompareFormIdWithFlag("Class", expected.HasClass, expected.ClassFormID,
                              actual.HasClass, actual.ClassFormID, diffs)
        CompareFormIdWithFlag("CombatStyle", expected.HasCombatStyle, expected.CombatStyleFormID,
                              actual.HasCombatStyle, actual.CombatStyleFormID, diffs)
        CompareFormIdWithFlag("GiftFilter", expected.HasGiftFilter, expected.GiftFilterFormID,
                              actual.HasGiftFilter, actual.GiftFilterFormID, diffs)
        CompareFormIdWithFlag("InheritsSoundsFrom", expected.HasInheritsSoundsFrom, expected.InheritsSoundsFromFormID,
                              actual.HasInheritsSoundsFrom, actual.InheritsSoundsFromFormID, diffs)
        CompareFormIdWithFlag("PowerArmorStand", expected.HasPowerArmorStand, expected.PowerArmorStandFormID,
                              actual.HasPowerArmorStand, actual.PowerArmorStandFormID, diffs)
        CompareFormIdWithFlag("DefaultOutfit", expected.HasDefaultOutfit, expected.DefaultOutfitFormID,
                              actual.HasDefaultOutfit, actual.DefaultOutfitFormID, diffs)
        CompareFormIdWithFlag("SleepOutfit", expected.HasSleepOutfit, expected.SleepOutfitFormID,
                              actual.HasSleepOutfit, actual.SleepOutfitFormID, diffs)
        CompareFormIdWithFlag("DefaultPackageList", expected.HasDefaultPackageList, expected.DefaultPackageListFormID,
                              actual.HasDefaultPackageList, actual.DefaultPackageListFormID, diffs)
        CompareFormIdWithFlag("CrimeFaction", expected.HasCrimeFaction, expected.CrimeFactionFormID,
                              actual.HasCrimeFaction, actual.CrimeFactionFormID, diffs)
        CompareFormIdWithFlag("HeadTexture", expected.HasHeadTexture, expected.HeadTextureFormID,
                              actual.HasHeadTexture, actual.HeadTextureFormID, diffs)
        CompareFormIdWithFlag("HairColor", expected.HasHairColor, expected.HairColorFormID,
                              actual.HasHairColor, actual.HairColorFormID, diffs)
        CompareFormIdWithFlag("FacialHairColor", expected.HasFacialHairColor, expected.FacialHairColorFormID,
                              actual.HasFacialHairColor, actual.FacialHairColorFormID, diffs)

        ' === Counters / flags ===
        CompareFlag("HasFull", expected.HasFull, actual.HasFull, diffs)
        CompareFlag("HasSpctCounter", expected.HasSpctCounter, actual.HasSpctCounter, diffs)
        CompareFlag("HasPrkzCounter", expected.HasPrkzCounter, actual.HasPrkzCounter, diffs)
        CompareFlag("HasCoctCounter", expected.HasCoctCounter, actual.HasCoctCounter, diffs)
        CompareFlag("HasKsizCounter", expected.HasKsizCounter, actual.HasKsizCounter, diffs)
        CompareFlag("HasCs2hCounter", expected.HasCs2hCounter, actual.HasCs2hCounter, diffs)
        CompareFlag("HasCs2eMarker", expected.HasCs2eMarker, actual.HasCs2eMarker, diffs)
        CompareFlag("HasObjectTemplate", expected.HasObjectTemplate, actual.HasObjectTemplate, diffs)
        CompareFlag("HasDataMarker", expected.HasDataMarker, actual.HasDataMarker, diffs)
        CompareFlag("HasMwgt", expected.HasMwgt, actual.HasMwgt, diffs)
        CompareFlag("HasFmin", expected.HasFmin, actual.HasFmin, diffs)
        CompareFlag("HasShortName", expected.HasShortName, actual.HasShortName, diffs)
        CompareFlag("HasActivateTextOverride", expected.HasActivateTextOverride, actual.HasActivateTextOverride, diffs)
        CompareFlag("HasSoundLevel", expected.HasSoundLevel, actual.HasSoundLevel, diffs)
        CompareFlag("HasHeightMin", expected.HasHeightMin, actual.HasHeightMin, diffs)
        CompareFlag("HasHeightMax", expected.HasHeightMax, actual.HasHeightMax, diffs)
        CompareFlag("HasTextureLighting", expected.HasTextureLighting, actual.HasTextureLighting, diffs)
        If expected.HasSoundLevel AndAlso actual.HasSoundLevel Then
            If expected.SoundLevel <> actual.SoundLevel Then
                diffs.Add($"SoundLevel: expected {expected.SoundLevel}, got {actual.SoundLevel}")
            End If
        End If

        ' === Strings ===
        CompareString("ShortName", expected.ShortName, actual.ShortName, diffs)
        CompareString("ActivateTextOverride", expected.ActivateTextOverride, actual.ActivateTextOverride, diffs)
        ' PluginName intentionally NOT compared: it's metadata the parser stamps based on
        ' which plugin file the record was read from. Expected has the source plugin
        ' (e.g. Fallout4.esm), actual has the just-written plugin (NPC_Manager.esp). That
        ' divergence is correct, not a bug in the writer.

        ' === ACBS ===
        CompareAcbs(expected.Acbs, actual.Acbs, diffs)

        ' === MWGT ===
        CompareNullableSingle("WeightThin", expected.WeightThin, actual.WeightThin, diffs)
        CompareNullableSingle("WeightMuscular", expected.WeightMuscular, actual.WeightMuscular, diffs)
        CompareNullableSingle("WeightFat", expected.WeightFat, actual.WeightFat, diffs)
        CompareBytes("MwgtRaw", expected.MwgtRaw, actual.MwgtRaw, diffs)

        ' === Heights ===
        If expected.HasHeightMin AndAlso actual.HasHeightMin Then
            CompareSingle("HeightMin", expected.HeightMin, actual.HeightMin, diffs)
        End If
        If expected.HasHeightMax AndAlso actual.HasHeightMax Then
            CompareSingle("HeightMax", expected.HeightMax, actual.HeightMax, diffs)
        End If

        ' === Required-but-opaque bytearrays ===
        CompareBytes("ObjectBoundsRaw", expected.ObjectBoundsRaw, actual.ObjectBoundsRaw, diffs)
        CompareBytes("Nam5Raw", expected.Nam5Raw, actual.Nam5Raw, diffs)
        CompareBytes("Nam7Raw", expected.Nam7Raw, actual.Nam7Raw, diffs)

        ' === DNAM ===
        CompareDnam(expected.CalculatedStats, actual.CalculatedStats, diffs)

        ' === QNAM ===
        CompareQnam(expected.TextureLightingFloats, actual.TextureLightingFloats, diffs)

        ' === Collections ===
        CompareFormIdList("HeadPartFormIDs", expected.HeadPartFormIDs, actual.HeadPartFormIDs, diffs)
        CompareFormIdList("ActorEffectFormIDs", expected.ActorEffectFormIDs, actual.ActorEffectFormIDs, diffs)
        CompareFormIdList("AiPackageFormIDs", expected.AiPackageFormIDs, actual.AiPackageFormIDs, diffs)
        CompareFormIdList("KeywordFormIDs", expected.KeywordFormIDs, actual.KeywordFormIDs, diffs)
        CompareFormIdList("AttachParentSlotFormIDs", expected.AttachParentSlotFormIDs, actual.AttachParentSlotFormIDs, diffs)
        CompareFormIdList("ObjectTemplateOMODFormIDs", expected.ObjectTemplateOMODFormIDs, actual.ObjectTemplateOMODFormIDs, diffs)

        ' === Template Actors (TPTA, 13 fixed slots) ===
        CompareTemplateActors(expected.TemplateActorFormIDs, actual.TemplateActorFormIDs, diffs)

        ' === MorphValues / MorphKeysOrdered ===
        CompareDictUInt32Single("MorphValues", expected.MorphValues, actual.MorphValues, diffs)
        CompareUInt32List("MorphKeysOrdered", expected.MorphKeysOrdered, actual.MorphKeysOrdered, diffs)

        ' === BodyMorphRegionValues (MRSV) ===
        CompareSingleList("BodyMorphRegionValues", expected.BodyMorphRegionValues, actual.BodyMorphRegionValues, diffs)

        ' === FacialMorphIntensity ===
        CompareSingle("FacialMorphIntensity", expected.FacialMorphIntensity, actual.FacialMorphIntensity, diffs)

        ' === FaceTintLayers ===
        CompareFaceTintLayers(expected.FaceTintLayers, actual.FaceTintLayers, diffs)
        CompareTintLayerStructs(expected.TintLayerStructs, actual.TintLayerStructs, diffs)

        ' === FaceMorphs ===
        CompareFaceMorphs(expected.FaceMorphs, actual.FaceMorphs, diffs)

        ' === Factions ===
        CompareFactions(expected.Factions, actual.Factions, diffs)

        ' === Perks ===
        ComparePerks(expected.Perks, actual.Perks, diffs)

        ' === Properties ===
        CompareProperties(expected.Properties, actual.Properties, diffs)

        ' === Inventory ===
        CompareInventory(expected.Inventory, actual.Inventory, diffs)

        ' === Attacks ===
        CompareAttacks(expected.Attacks, actual.Attacks, diffs)

        ' === ActorSounds ===
        CompareActorSounds(expected.ActorSounds, actual.ActorSounds, diffs)

        ' === AIDT ===
        CompareAiData(expected.AiData, actual.AiData, diffs)

        ' === Destruction ===
        CompareDestruction(expected.Destruction, actual.Destruction, diffs)

        ' === Object Template Combinations ===
        CompareObjectTemplateCombinations(expected.ObjectTemplateCombinations, actual.ObjectTemplateCombinations, diffs)

        ' === VMAD ===
        CompareVmad(expected.Vmad, actual.Vmad, diffs)
    End Sub

    ' ========================================================================
    ' Per-type comparison helpers
    ' ========================================================================

    Private Sub CompareString(name As String, a As String, b As String, diffs As List(Of String))
        If Not String.Equals(If(a, ""), If(b, ""), StringComparison.Ordinal) Then
            diffs.Add($"{name}: expected '{If(a, "")}', got '{If(b, "")}'")
        End If
    End Sub

    Private Sub CompareFlag(name As String, a As Boolean, b As Boolean, diffs As List(Of String))
        If a <> b Then diffs.Add($"{name}: expected {a}, got {b}")
    End Sub

    Private Sub CompareSingle(name As String, a As Single, b As Single, diffs As List(Of String))
        If Math.Abs(a - b) > 0.0001F Then
            diffs.Add($"{name}: expected {a}, got {b}")
        End If
    End Sub

    Private Sub CompareNullableSingle(name As String, a As Single?, b As Single?, diffs As List(Of String))
        If a.HasValue <> b.HasValue Then
            diffs.Add($"{name}: HasValue mismatch (expected {a.HasValue}, got {b.HasValue})")
            Return
        End If
        If Not a.HasValue Then Return
        If Math.Abs(a.Value - b.Value) > 0.0001F Then
            diffs.Add($"{name}: expected {a.Value}, got {b.Value}")
        End If
    End Sub

    Private Sub CompareFormIdWithFlag(name As String,
                                      hasA As Boolean, fidA As UInteger,
                                      hasB As Boolean, fidB As UInteger,
                                      diffs As List(Of String))
        If hasA <> hasB Then
            diffs.Add($"{name}: presence mismatch (expected has={hasA} fid=0x{fidA:X8}, got has={hasB} fid=0x{fidB:X8})")
            Return
        End If
        If Not hasA Then Return
        If fidA <> fidB Then
            diffs.Add($"{name}: FormID mismatch (expected 0x{fidA:X8}, got 0x{fidB:X8})")
        End If
    End Sub

    Private Sub CompareBytes(name As String, a As Byte(), b As Byte(), diffs As List(Of String))
        Dim aLen = If(a Is Nothing, 0, a.Length)
        Dim bLen = If(b Is Nothing, 0, b.Length)
        If aLen <> bLen Then
            diffs.Add($"{name}: length mismatch (expected {aLen}, got {bLen})")
            Return
        End If
        For i = 0 To aLen - 1
            If a(i) <> b(i) Then
                diffs.Add($"{name}: byte {i} differs (expected 0x{a(i):X2}, got 0x{b(i):X2})")
                Return
            End If
        Next
    End Sub

    Private Sub CompareFormIdList(name As String, a As List(Of UInteger), b As List(Of UInteger), diffs As List(Of String))
        Dim aCount = If(a Is Nothing, 0, a.Count)
        Dim bCount = If(b Is Nothing, 0, b.Count)
        If aCount <> bCount Then
            diffs.Add($"{name}: count mismatch (expected {aCount}, got {bCount})")
            Return
        End If
        For i = 0 To aCount - 1
            If a(i) <> b(i) Then
                diffs.Add($"{name}[{i}]: expected 0x{a(i):X8}, got 0x{b(i):X8}")
                Return
            End If
        Next
    End Sub

    Private Sub CompareUInt32List(name As String, a As List(Of UInteger), b As List(Of UInteger), diffs As List(Of String))
        CompareFormIdList(name, a, b, diffs)
    End Sub

    Private Sub CompareSingleList(name As String, a As List(Of Single), b As List(Of Single), diffs As List(Of String))
        Dim aCount = If(a Is Nothing, 0, a.Count)
        Dim bCount = If(b Is Nothing, 0, b.Count)
        If aCount <> bCount Then
            diffs.Add($"{name}: count mismatch (expected {aCount}, got {bCount})")
            Return
        End If
        For i = 0 To aCount - 1
            If Math.Abs(a(i) - b(i)) > 0.0001F Then
                diffs.Add($"{name}[{i}]: expected {a(i)}, got {b(i)}")
                Return
            End If
        Next
    End Sub

    Private Sub CompareDictUInt32Single(name As String,
                                        a As Dictionary(Of UInteger, Single),
                                        b As Dictionary(Of UInteger, Single),
                                        diffs As List(Of String))
        Dim aCount = If(a Is Nothing, 0, a.Count)
        Dim bCount = If(b Is Nothing, 0, b.Count)
        If aCount <> bCount Then
            diffs.Add($"{name}: count mismatch (expected {aCount}, got {bCount})")
            Return
        End If
        If a Is Nothing OrElse b Is Nothing Then Return
        For Each kv In a
            Dim bVal As Single = 0.0F
            If Not b.TryGetValue(kv.Key, bVal) Then
                diffs.Add($"{name}: key 0x{kv.Key:X8} missing in actual")
                Return
            End If
            If Math.Abs(kv.Value - bVal) > 0.0001F Then
                diffs.Add($"{name}[0x{kv.Key:X8}]: expected {kv.Value}, got {bVal}")
                Return
            End If
        Next
    End Sub

    Private Sub CompareTemplateActors(a As Dictionary(Of NPC_TemplateCategory, UInteger),
                                      b As Dictionary(Of NPC_TemplateCategory, UInteger),
                                      diffs As List(Of String))
        Dim aCount = If(a Is Nothing, 0, a.Count)
        Dim bCount = If(b Is Nothing, 0, b.Count)
        If aCount <> bCount Then
            diffs.Add($"TemplateActorFormIDs: count mismatch (expected {aCount}, got {bCount})")
            Return
        End If
        If a Is Nothing OrElse b Is Nothing Then Return
        For Each kv In a
            Dim bVal As UInteger = 0UI
            If Not b.TryGetValue(kv.Key, bVal) Then
                diffs.Add($"TemplateActorFormIDs[{kv.Key}]: missing in actual")
                Return
            End If
            If kv.Value <> bVal Then
                diffs.Add($"TemplateActorFormIDs[{kv.Key}]: expected 0x{kv.Value:X8}, got 0x{bVal:X8}")
                Return
            End If
        Next
    End Sub

    Private Sub CompareAcbs(a As NPC_AcbsData, b As NPC_AcbsData, diffs As List(Of String))
        If a Is Nothing AndAlso b Is Nothing Then Return
        If a Is Nothing OrElse b Is Nothing Then
            diffs.Add($"ACBS: presence mismatch (expected={a IsNot Nothing}, got={b IsNot Nothing})")
            Return
        End If
        If a.Flags <> b.Flags Then diffs.Add($"ACBS.Flags: expected 0x{a.Flags:X8}, got 0x{b.Flags:X8}")
        If a.XpValueOffset <> b.XpValueOffset Then diffs.Add($"ACBS.XpValueOffset: expected {a.XpValueOffset}, got {b.XpValueOffset}")
        If a.LevelOrLevelMult <> b.LevelOrLevelMult Then diffs.Add($"ACBS.LevelOrLevelMult: expected {a.LevelOrLevelMult}, got {b.LevelOrLevelMult}")
        If a.CalcMinLevel <> b.CalcMinLevel Then diffs.Add($"ACBS.CalcMinLevel: expected {a.CalcMinLevel}, got {b.CalcMinLevel}")
        If a.CalcMaxLevel <> b.CalcMaxLevel Then diffs.Add($"ACBS.CalcMaxLevel: expected {a.CalcMaxLevel}, got {b.CalcMaxLevel}")
        If a.DispositionBase <> b.DispositionBase Then diffs.Add($"ACBS.DispositionBase: expected {a.DispositionBase}, got {b.DispositionBase}")
        If a.TemplateFlags <> b.TemplateFlags Then diffs.Add($"ACBS.TemplateFlags: expected 0x{a.TemplateFlags:X4}, got 0x{b.TemplateFlags:X4}")
        If a.BleedoutOverride <> b.BleedoutOverride Then diffs.Add($"ACBS.BleedoutOverride: expected {a.BleedoutOverride}, got {b.BleedoutOverride}")
        CompareBytes("ACBS.Unknown18", a.Unknown18, b.Unknown18, diffs)
        CompareBytes("ACBS.TrailingBytes", a.TrailingBytes, b.TrailingBytes, diffs)
    End Sub

    Private Sub CompareDnam(a As NPC_CalculatedStats, b As NPC_CalculatedStats, diffs As List(Of String))
        If a Is Nothing AndAlso b Is Nothing Then Return
        If a Is Nothing OrElse b Is Nothing Then
            diffs.Add($"DNAM: presence mismatch (expected={a IsNot Nothing}, got={b IsNot Nothing})")
            Return
        End If
        If a.CalculatedHealth <> b.CalculatedHealth Then diffs.Add($"DNAM.CalculatedHealth: expected {a.CalculatedHealth}, got {b.CalculatedHealth}")
        If a.CalculatedActionPoints <> b.CalculatedActionPoints Then diffs.Add($"DNAM.CalculatedActionPoints: expected {a.CalculatedActionPoints}, got {b.CalculatedActionPoints}")
        If a.FarAwayModelDistance <> b.FarAwayModelDistance Then diffs.Add($"DNAM.FarAwayModelDistance: expected {a.FarAwayModelDistance}, got {b.FarAwayModelDistance}")
        If a.GearedUpWeapons <> b.GearedUpWeapons Then diffs.Add($"DNAM.GearedUpWeapons: expected {a.GearedUpWeapons}, got {b.GearedUpWeapons}")
        If a.Unused7 <> b.Unused7 Then diffs.Add($"DNAM.Unused7: expected 0x{a.Unused7:X2}, got 0x{b.Unused7:X2}")
    End Sub

    Private Sub CompareQnam(a As NPC_TextureLightingFloats, b As NPC_TextureLightingFloats, diffs As List(Of String))
        If a Is Nothing AndAlso b Is Nothing Then Return
        If a Is Nothing OrElse b Is Nothing Then
            diffs.Add($"QNAM: presence mismatch (expected={a IsNot Nothing}, got={b IsNot Nothing})")
            Return
        End If
        CompareSingle("QNAM.R", a.R, b.R, diffs)
        CompareSingle("QNAM.G", a.G, b.G, diffs)
        CompareSingle("QNAM.B", a.B, b.B, diffs)
        CompareSingle("QNAM.A", a.A, b.A, diffs)
    End Sub

    Private Sub CompareFaceTintLayers(a As List(Of NPC_FaceTintLayerData), b As List(Of NPC_FaceTintLayerData), diffs As List(Of String))
        Dim aCount = If(a Is Nothing, 0, a.Count)
        Dim bCount = If(b Is Nothing, 0, b.Count)
        If aCount <> bCount Then
            diffs.Add($"FaceTintLayers: count mismatch (expected {aCount}, got {bCount})")
            Return
        End If
        For i = 0 To aCount - 1
            If a(i).Discriminator <> b(i).Discriminator Then
                diffs.Add($"FaceTintLayers[{i}].Discriminator: expected {a(i).Discriminator}, got {b(i).Discriminator}")
                Return
            End If
            If a(i).Index <> b(i).Index Then
                diffs.Add($"FaceTintLayers[{i}].Index: expected {a(i).Index}, got {b(i).Index}")
                Return
            End If
            If a(i).Value <> b(i).Value Then
                diffs.Add($"FaceTintLayers[{i}].Value: expected {a(i).Value}, got {b(i).Value}")
                Return
            End If
            If a(i).TemplateColorIndex <> b(i).TemplateColorIndex Then
                diffs.Add($"FaceTintLayers[{i}].TemplateColorIndex: expected {a(i).TemplateColorIndex}, got {b(i).TemplateColorIndex}")
                Return
            End If
            ' Compare RGB only — TEND has no alpha channel ([RecordParsers.vb:2302] forces
            ' A=255 on parse). Expected side may carry A=0 or other values from upstream
            ' sources (LM preset JSON, overlay merge). The binary in the ESP only stores
            ' R/G/B/Pad, so alpha divergence is structural noise, not a round-trip bug.
            If (a(i).Color.R <> b(i).Color.R) OrElse
               (a(i).Color.G <> b(i).Color.G) OrElse
               (a(i).Color.B <> b(i).Color.B) Then
                diffs.Add($"FaceTintLayers[{i}].Color: expected {a(i).Color}, got {b(i).Color}")
                Return
            End If
        Next
    End Sub

    Private Sub CompareTintLayerStructs(a As List(Of (Teti As NPC_TetiStruct, Tend As NPC_TendStruct)),
                                        b As List(Of (Teti As NPC_TetiStruct, Tend As NPC_TendStruct)),
                                        diffs As List(Of String))
        Dim aCount = If(a Is Nothing, 0, a.Count)
        Dim bCount = If(b Is Nothing, 0, b.Count)
        If aCount <> bCount Then
            diffs.Add($"TintLayerStructs: count mismatch (expected {aCount}, got {bCount})")
            Return
        End If
        For i = 0 To aCount - 1
            If a(i).Teti.DataType <> b(i).Teti.DataType OrElse a(i).Teti.Index <> b(i).Teti.Index Then
                diffs.Add($"TintLayerStructs[{i}].Teti differs")
                Return
            End If
            If a(i).Tend.RawValue <> b(i).Tend.RawValue OrElse
               a(i).Tend.HasColor <> b(i).Tend.HasColor OrElse
               a(i).Tend.HasTemplateColorIndex <> b(i).Tend.HasTemplateColorIndex Then
                diffs.Add($"TintLayerStructs[{i}].Tend layout differs (expected V={a(i).Tend.RawValue} HC={a(i).Tend.HasColor} HTCI={a(i).Tend.HasTemplateColorIndex}, got V={b(i).Tend.RawValue} HC={b(i).Tend.HasColor} HTCI={b(i).Tend.HasTemplateColorIndex})")
                Return
            End If
            If a(i).Tend.HasColor AndAlso (
               a(i).Tend.ColorR <> b(i).Tend.ColorR OrElse
               a(i).Tend.ColorG <> b(i).Tend.ColorG OrElse
               a(i).Tend.ColorB <> b(i).Tend.ColorB OrElse
               a(i).Tend.ColorPad <> b(i).Tend.ColorPad) Then
                diffs.Add($"TintLayerStructs[{i}].Tend Color differs (expected R={a(i).Tend.ColorR} G={a(i).Tend.ColorG} B={a(i).Tend.ColorB} Pad={a(i).Tend.ColorPad}, got R={b(i).Tend.ColorR} G={b(i).Tend.ColorG} B={b(i).Tend.ColorB} Pad={b(i).Tend.ColorPad})")
                Return
            End If
            If a(i).Tend.HasTemplateColorIndex AndAlso a(i).Tend.TemplateColorIndex <> b(i).Tend.TemplateColorIndex Then
                diffs.Add($"TintLayerStructs[{i}].Tend.TemplateColorIndex: expected {a(i).Tend.TemplateColorIndex}, got {b(i).Tend.TemplateColorIndex}")
                Return
            End If
        Next
    End Sub

    Private Sub CompareFaceMorphs(a As List(Of NPC_FaceMorphData), b As List(Of NPC_FaceMorphData), diffs As List(Of String))
        Dim aCount = If(a Is Nothing, 0, a.Count)
        Dim bCount = If(b Is Nothing, 0, b.Count)
        If aCount <> bCount Then
            diffs.Add($"FaceMorphs: count mismatch (expected {aCount}, got {bCount})")
            Return
        End If
        For i = 0 To aCount - 1
            If a(i).Index <> b(i).Index Then
                diffs.Add($"FaceMorphs[{i}].Index: expected 0x{a(i).Index:X8}, got 0x{b(i).Index:X8}")
                Return
            End If

            ' FMRS spec ([wbDefinitionsFO4.pas:10805-10814]): exactly 7 named floats
            ' (Pos X/Y/Z, Rot X/Y/Z, Scale) + a wbByteArray('Unknown') trailing block.
            ' LooksMenu in-memory carries 8 floats per region ([CharGenInterface.cpp:147]),
            ' and our LM JSON saver pads to 8 to mimic LM's output. So `a` (built from a
            ' loaded JSON via the overlay) may have 8, while `b` (re-parsed from the ESP we
            ' just wrote) always has 7.
            '
            ' The 8th slot has only ever been observed as 0.0 in real LM JSONs (full dump
            ' verified against multiple regions on this NPC: every `value[7]` is 0). The
            ' writer truncates to 7 on emit, which is engine-correct. Treat the 8th slot
            ' as comparator-irrelevant when it's zero on the expected side.
            Dim aVals = a(i).Values
            Dim bVals = b(i).Values
            Dim aCnt = aVals.Count
            Dim bCnt = bVals.Count
            Dim cmpCount As Integer = Math.Min(aCnt, bCnt)
            ' Tolerate the LM-padding asymmetry: if expected has 8 and got has 7, ignore the
            ' 8th IFF it's zero. Anything non-zero in slot 7 IS reported (we'd be silently
            ' losing data and the user should know).
            If aCnt <> bCnt Then
                Dim larger = If(aCnt > bCnt, aVals, bVals)
                Dim allTrailingZero As Boolean = True
                For k = cmpCount To larger.Count - 1
                    If Math.Abs(larger(k)) > 0.0001F Then
                        allTrailingZero = False
                        Exit For
                    End If
                Next
                If Not allTrailingZero Then
                    diffs.Add($"FaceMorphs[{i}].Values count: expected {aCnt}, got {bCnt} (trailing values are non-zero — possible data loss)")
                    Return
                End If
            End If

            For j = 0 To cmpCount - 1
                If Math.Abs(aVals(j) - bVals(j)) > 0.0001F Then
                    diffs.Add($"FaceMorphs[{i}].Values[{j}]: expected {aVals(j)}, got {bVals(j)}")
                    Return
                End If
            Next
        Next
    End Sub

    Private Sub CompareFactions(a As List(Of NPC_FactionEntry), b As List(Of NPC_FactionEntry), diffs As List(Of String))
        Dim aCount = If(a Is Nothing, 0, a.Count)
        Dim bCount = If(b Is Nothing, 0, b.Count)
        If aCount <> bCount Then
            diffs.Add($"Factions: count mismatch (expected {aCount}, got {bCount})")
            Return
        End If
        For i = 0 To aCount - 1
            If a(i).FactionFormID <> b(i).FactionFormID Then
                diffs.Add($"Factions[{i}].FactionFormID: expected 0x{a(i).FactionFormID:X8}, got 0x{b(i).FactionFormID:X8}")
                Return
            End If
            If a(i).Rank <> b(i).Rank Then
                diffs.Add($"Factions[{i}].Rank: expected {a(i).Rank}, got {b(i).Rank}")
                Return
            End If
        Next
    End Sub

    Private Sub ComparePerks(a As List(Of NPC_PerkEntry), b As List(Of NPC_PerkEntry), diffs As List(Of String))
        Dim aCount = If(a Is Nothing, 0, a.Count)
        Dim bCount = If(b Is Nothing, 0, b.Count)
        If aCount <> bCount Then
            diffs.Add($"Perks: count mismatch (expected {aCount}, got {bCount})")
            Return
        End If
        For i = 0 To aCount - 1
            If a(i).PerkFormID <> b(i).PerkFormID OrElse a(i).Rank <> b(i).Rank Then
                diffs.Add($"Perks[{i}]: expected (0x{a(i).PerkFormID:X8}, rank {a(i).Rank}), got (0x{b(i).PerkFormID:X8}, rank {b(i).Rank})")
                Return
            End If
        Next
    End Sub

    Private Sub CompareProperties(a As List(Of NPC_PropertyEntry), b As List(Of NPC_PropertyEntry), diffs As List(Of String))
        Dim aCount = If(a Is Nothing, 0, a.Count)
        Dim bCount = If(b Is Nothing, 0, b.Count)
        If aCount <> bCount Then
            diffs.Add($"Properties: count mismatch (expected {aCount}, got {bCount})")
            Return
        End If
        For i = 0 To aCount - 1
            If a(i).ActorValueFormID <> b(i).ActorValueFormID Then
                diffs.Add($"Properties[{i}].ActorValueFormID: expected 0x{a(i).ActorValueFormID:X8}, got 0x{b(i).ActorValueFormID:X8}")
                Return
            End If
            If Math.Abs(a(i).Value - b(i).Value) > 0.0001F Then
                diffs.Add($"Properties[{i}].Value: expected {a(i).Value}, got {b(i).Value}")
                Return
            End If
        Next
    End Sub

    Private Sub CompareInventory(a As List(Of NPC_InventoryItem), b As List(Of NPC_InventoryItem), diffs As List(Of String))
        Dim aCount = If(a Is Nothing, 0, a.Count)
        Dim bCount = If(b Is Nothing, 0, b.Count)
        If aCount <> bCount Then
            diffs.Add($"Inventory: count mismatch (expected {aCount}, got {bCount})")
            Return
        End If
        For i = 0 To aCount - 1
            If a(i).ItemFormID <> b(i).ItemFormID OrElse a(i).Count <> b(i).Count Then
                diffs.Add($"Inventory[{i}]: expected (0x{a(i).ItemFormID:X8}, n={a(i).Count}), got (0x{b(i).ItemFormID:X8}, n={b(i).Count})")
                Return
            End If
            If a(i).HasCoed <> b(i).HasCoed Then
                diffs.Add($"Inventory[{i}].HasCoed: expected {a(i).HasCoed}, got {b(i).HasCoed}")
                Return
            End If
        Next
    End Sub

    Private Sub CompareAttacks(a As List(Of NPC_AttackData), b As List(Of NPC_AttackData), diffs As List(Of String))
        Dim aCount = If(a Is Nothing, 0, a.Count)
        Dim bCount = If(b Is Nothing, 0, b.Count)
        If aCount <> bCount Then
            diffs.Add($"Attacks: count mismatch (expected {aCount}, got {bCount})")
            Return
        End If
        For i = 0 To aCount - 1
            If Not String.Equals(If(a(i).AttackEvent, ""), If(b(i).AttackEvent, ""), StringComparison.Ordinal) Then
                diffs.Add($"Attacks[{i}].AttackEvent: expected '{a(i).AttackEvent}', got '{b(i).AttackEvent}'")
                Return
            End If
            If a(i).AttackSpellFormID <> b(i).AttackSpellFormID Then
                diffs.Add($"Attacks[{i}].AttackSpellFormID: expected 0x{a(i).AttackSpellFormID:X8}, got 0x{b(i).AttackSpellFormID:X8}")
                Return
            End If
        Next
    End Sub

    Private Sub CompareActorSounds(a As List(Of NPC_ActorSound), b As List(Of NPC_ActorSound), diffs As List(Of String))
        Dim aCount = If(a Is Nothing, 0, a.Count)
        Dim bCount = If(b Is Nothing, 0, b.Count)
        If aCount <> bCount Then
            diffs.Add($"ActorSounds: count mismatch (expected {aCount}, got {bCount})")
            Return
        End If
        For i = 0 To aCount - 1
            If a(i).KeywordFormID <> b(i).KeywordFormID OrElse a(i).SoundFormID <> b(i).SoundFormID Then
                diffs.Add($"ActorSounds[{i}]: expected (kw=0x{a(i).KeywordFormID:X8}, snd=0x{a(i).SoundFormID:X8}), got (kw=0x{b(i).KeywordFormID:X8}, snd=0x{b(i).SoundFormID:X8})")
                Return
            End If
        Next
    End Sub

    Private Sub CompareAiData(a As NPC_AiData, b As NPC_AiData, diffs As List(Of String))
        If a Is Nothing AndAlso b Is Nothing Then Return
        If a Is Nothing OrElse b Is Nothing Then
            diffs.Add($"AIDT: presence mismatch (expected={a IsNot Nothing}, got={b IsNot Nothing})")
            Return
        End If
        If a.Aggression <> b.Aggression Then diffs.Add($"AIDT.Aggression: expected {a.Aggression}, got {b.Aggression}")
        If a.Confidence <> b.Confidence Then diffs.Add($"AIDT.Confidence: expected {a.Confidence}, got {b.Confidence}")
        If a.EnergyLevel <> b.EnergyLevel Then diffs.Add($"AIDT.EnergyLevel: expected {a.EnergyLevel}, got {b.EnergyLevel}")
        If a.Morality <> b.Morality Then diffs.Add($"AIDT.Morality: expected {a.Morality}, got {b.Morality}")
        If a.Mood <> b.Mood Then diffs.Add($"AIDT.Mood: expected {a.Mood}, got {b.Mood}")
        If a.Assistance <> b.Assistance Then diffs.Add($"AIDT.Assistance: expected {a.Assistance}, got {b.Assistance}")
        If a.HasV29Fields <> b.HasV29Fields Then diffs.Add($"AIDT.HasV29Fields: expected {a.HasV29Fields}, got {b.HasV29Fields}")
    End Sub

    Private Sub CompareDestruction(a As NPC_DestructionData, b As NPC_DestructionData, diffs As List(Of String))
        If a Is Nothing AndAlso b Is Nothing Then Return
        If a Is Nothing OrElse b Is Nothing Then
            diffs.Add($"DEST: presence mismatch (expected={a IsNot Nothing}, got={b IsNot Nothing})")
            Return
        End If
        If a.Health <> b.Health Then diffs.Add($"DEST.Health: expected {a.Health}, got {b.Health}")
        If a.Resistances.Count <> b.Resistances.Count Then
            diffs.Add($"DEST.Resistances: count mismatch (expected {a.Resistances.Count}, got {b.Resistances.Count})")
        End If
        If a.Stages.Count <> b.Stages.Count Then
            diffs.Add($"DEST.Stages: count mismatch (expected {a.Stages.Count}, got {b.Stages.Count})")
        End If
    End Sub

    Private Sub CompareObjectTemplateCombinations(a As List(Of NPC_ObjectTemplateCombination),
                                                  b As List(Of NPC_ObjectTemplateCombination),
                                                  diffs As List(Of String))
        Dim aCount = If(a Is Nothing, 0, a.Count)
        Dim bCount = If(b Is Nothing, 0, b.Count)
        If aCount <> bCount Then
            diffs.Add($"ObjectTemplateCombinations: count mismatch (expected {aCount}, got {bCount})")
            Return
        End If
        For i = 0 To aCount - 1
            If a(i).IsEditorOnly <> b(i).IsEditorOnly Then
                diffs.Add($"ObjectTemplateCombinations[{i}].IsEditorOnly: expected {a(i).IsEditorOnly}, got {b(i).IsEditorOnly}")
                Return
            End If
            CompareBytes($"ObjectTemplateCombinations[{i}].RawObtsBytes", a(i).RawObtsBytes, b(i).RawObtsBytes, diffs)
        Next
    End Sub

    Private Sub CompareVmad(a As NPC_VmadData, b As NPC_VmadData, diffs As List(Of String))
        If a Is Nothing AndAlso b Is Nothing Then Return
        If a Is Nothing OrElse b Is Nothing Then
            diffs.Add($"VMAD: presence mismatch (expected={a IsNot Nothing}, got={b IsNot Nothing})")
            Return
        End If
        CompareBytes("VMAD.RawBytes", a.RawBytes, b.RawBytes, diffs)
        Dim aPos = If(a.FormIdPositions Is Nothing, 0, a.FormIdPositions.Count)
        Dim bPos = If(b.FormIdPositions Is Nothing, 0, b.FormIdPositions.Count)
        If aPos <> bPos Then
            diffs.Add($"VMAD.FormIdPositions: count mismatch (expected {aPos}, got {bPos})")
        End If
    End Sub

End Module
