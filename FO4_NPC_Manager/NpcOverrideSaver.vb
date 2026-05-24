Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports FO4_Base_Library

''' <summary>
''' Orchestrator for the Save NPC override flow. Owns the multi-phase work that used to live
''' inline in <see cref="MainForm.ButtonSavePlugin_Click"/>: build the override entry, write the
''' plugin via <see cref="SaveNpcEspWriter"/>, optionally bake the CharGen NIF + textures and
''' pack them into BA2.
'''
''' Reports progress through <see cref="IProgress(Of SaveProgress)"/> so the caller (the Save
''' dialog) can render an embedded progress panel without a separate form. Cleanup tasks that
''' depend on MainForm internals (auto-gen plugin cache, NPC tree refresh, success MessageBox)
''' are NOT performed here — the orchestrator returns the data those steps need and MainForm
''' performs them after the dialog closes.
''' </summary>
Public Module NpcOverrideSaver

    Public Class SaveProgress
        Public Phase As String = ""
        Public Detail As String = ""
        ''' <summary>True = use Max/Current for a determinate bar. False = marquee.</summary>
        Public Determinate As Boolean
        Public Max As Integer
        Public Current As Integer
    End Class

    ''' <summary>Outcome of <see cref="ExecuteAsync"/>. Populated even on failure so the caller
    ''' can show a meaningful error.</summary>
    Public Class SaveExecutionResult
        Public Success As Boolean
        Public WriterResult As SaveNpcEspWriter.SaveResult
        ''' <summary>Final list of NPC FormIDs in the saved plugin (preserved existing + the new
        ''' override). Used by MainForm to update the auto-gen plugin cache.</summary>
        Public SavedFormIDs As New List(Of UInteger)
        Public VerifierSummary As String = ""
        Public VerifierIcon As MessageBoxIcon = MessageBoxIcon.Information
        Public ChargenSummary As String = ""
        Public ChargenSuccess As Boolean = True
        ''' <summary>Empty when <see cref="Success"/> is True; the user-facing exception message
        ''' otherwise. Caller is expected to surface it via MessageBox.</summary>
        Public ErrorMessage As String = ""
    End Class

    ''' <summary>Bundles the dependencies the orchestrator needs to call back into the host app.
    ''' Constructed once by MainForm and passed through. All fields are required.</summary>
    Public Class SaveContext
        Public PluginManager As PluginManager
        Public AppliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)
        Public RenderHost As Object  ' NpcRenderHost — typed loosely to avoid an extra import.
        Public DataPath As String
        ''' <summary>MainForm helper: returns the post-overlay shadow NPC_Data, or the raw
        ''' instance unchanged when no overlay is applied.</summary>
        Public ApplyPresetOverlayToNpcData As Func(Of NPC_Data, UInteger, NPC_Data)
        ''' <summary>MainForm helper: copy round-trip-only fields (Vmad, Acbs trailing, OBND,
        ''' Object Template raw, factions, AI data) from raw onto shadow.</summary>
        Public CopyRoundTripOnlyFieldsFromRaw As Action(Of NPC_Data, NPC_Data)
        ''' <summary>MainForm helper: rebuild the parser's parallel collections (TintLayerStructs,
        ''' FaceMorphTrailingBytes, MorphKeysOrdered) on the shadow after overlay copy.</summary>
        Public SyncParallelCollectionsAfterOverlay As Action(Of NPC_Data)
        ''' <summary>FaceGen bake delegate: invoked when the user opted into CharGen bake +
        ''' BA2 pack. Kept as a callback because the bake pipeline lives in MainForm/FaceGenBuilder
        ''' and pulls render state from <see cref="RenderHost"/>.</summary>
        Public RunChargenBakeAndPack As Func(Of UInteger, String, String, IProgress(Of SaveProgress), (Summary As String, Success As Boolean))
        ''' <summary>All outfit drafts authored in the Edit Outfit "Create" tab (MainForm's
        ''' <c>_outfitDrafts</c>, minus the throwaway preview sentinel). When the save target's
        ''' <c>SaveNewOutfits</c> is True, ExecuteWritePhases emits as OTFT records every draft that is
        ''' dirty OR referenced by this NPC's DOFT (so the plugin is self-contained); clean unreferenced
        ''' drafts are skipped (the "don't re-save what's already saved" rule). Nothing = none.</summary>
        Public OutfitDrafts As List(Of OutfitDraft) = Nothing
    End Class

    ''' <summary>Execute the save end-to-end. Runs the synchronous CPU/IO work on a background
    ''' Task so the UI thread can paint progress reports without blocking. <paramref name="progress"/>
    ''' callbacks are marshaled back to the UI thread by .NET's <see cref="IProgress(Of T)"/>
    ''' contract.
    '''
    ''' Phases (each one emits a progress report at start):
    '''   1. Build override entry (apply overlay, sync MWGT, rebuild HeadParts).
    '''   2. Load existing records from the target plugin (Update existing path).
    '''   3. Write plugin via <see cref="SaveNpcEspWriter.SaveOverridePlugin"/>.
    '''   4. Optional CharGen bake + BA2 pack (per <see cref="SaveEsp_Form.SaveTarget.GenerateChargen"/>).</summary>
    ''' <summary>Run the save end-to-end. Hybrid threading model — pure-IO phases (write ESP,
    ''' verifier, BA2 pack) run on a worker Task so the UI message pump stays alive and the
    ''' progress panel repaints; the CharGen bake runs on the UI thread because it touches the
    ''' OpenGL render host, which is single-thread-bound to the GL context owner. Awaits
    ''' alternate the two so the orchestrator stays a single linear flow.</summary>
    Public Async Function ExecuteAsync(target As SaveEsp_Form.SaveTarget,
                                       npcFormID As UInteger,
                                       npc As NPC_Data,
                                       rawRecord As PluginRecord,
                                       rawNpcSpec As NPC_Data,
                                       sourcePluginName As String,
                                       baseStateWeightThin As Single,
                                       baseStateWeightMuscular As Single,
                                       baseStateWeightFat As Single,
                                       baseStateValid As Boolean,
                                       ctx As SaveContext,
                                       progress As IProgress(Of SaveProgress)) As Task(Of SaveExecutionResult)

        Dim result As New SaveExecutionResult

        Try
            ' Phases 1-3 (record build → existing-plugin load → write → verifier) are pure CPU/IO;
            ' run on a worker so the UI thread keeps pumping messages and the progress panel
            ' repaints. Returns the post-overlay NPC_Data via WritePhaseResult; we don't need it
            ' here yet, but the bake phase may consume it later for write/bake parity checks.
            Await Task.Run(Sub()
                               ExecuteWritePhases(target, npcFormID, npc, rawRecord, rawNpcSpec,
                                                  sourcePluginName,
                                                  baseStateWeightThin, baseStateWeightMuscular, baseStateWeightFat,
                                                  baseStateValid, ctx, progress, result)
                           End Sub)

            ' Phase 4: CharGen bake. Must run on the UI thread because FaceGenBuilder.BuildCharGen
            ' reads textures from the live GL context. Yield first so the panel paints "Baking…"
            ' before the synchronous bake call blocks the message pump again.
            If target.GenerateChargen Then
                Await Task.Yield()
                ReportPhase(progress, "Baking CharGen NIF + textures…", "")
                Dim chargenRes = ctx.RunChargenBakeAndPack(npcFormID, target.TargetPath, sourcePluginName, progress)
                result.ChargenSummary = chargenRes.Summary
                result.ChargenSuccess = chargenRes.Success
                If Not chargenRes.Success Then result.VerifierIcon = MessageBoxIcon.Warning
            End If

            result.Success = True
        Catch ex As Exception
            result.Success = False
            result.ErrorMessage = ex.Message
        End Try

        Return result
    End Function

    ''' <summary>Bag returned by <see cref="ExecuteWritePhases"/> so the caller can hand off
    ''' state from the worker-thread phases to subsequent UI-thread phases.</summary>
    Private Class WritePhaseResult
        Public NpcSpec As NPC_Data
    End Class

    ''' <summary>
    ''' Check the translatable string fields (FULL/SHRT/ATTX — the ones NpcSubrecordWriter.EmitLString
    ''' emits) of one NPC against the currently-selected Translatable encoding. Returns "" if all
    ''' fit, or a user-facing message naming the offending field + value. labelSuffix distinguishes
    ''' pre-existing NPCs from the one being edited.
    ''' </summary>
    Private Function FindEncodingConflict(npc As NPC_Data, labelSuffix As String) As String
        If npc Is Nothing Then Return ""

        Dim checks As New List(Of (Field As String, Value As String))
        If npc.HasFull Then checks.Add(("FULL (nombre)" & labelSuffix, npc.FullName))
        If npc.HasShortName Then checks.Add(("SHRT (nombre corto)" & labelSuffix, npc.ShortName))
        If npc.HasActivateTextOverride Then checks.Add(("ATTX (texto de activación)" & labelSuffix, npc.ActivateTextOverride))

        For Each check In checks
            If Not String.IsNullOrEmpty(check.Value) AndAlso Not PluginEncodingSettings.CanEncodeTranslatableStrict(check.Value) Then
                Return $"El campo {check.Field} contiene caracteres que no caben en el encoding seleccionado." & vbCrLf & vbCrLf &
                       $"Valor: ""{check.Value}""" & vbCrLf & vbCrLf &
                       "Esos caracteres se perderían (reemplazados por '?'). Elegí UTF-8 (recomendado) " &
                       "o un encoding que cubra el alfabeto del nombre, y volvé a guardar."
            End If
        Next

        Return ""
    End Function

    ''' <summary>Phases 1-3 of the save: build the override entry (overlay + MWGT + HeadParts
    ''' merge), load existing records, write the plugin, and run the optional verifier. All
    ''' pure CPU/IO — safe to run on a worker Task. Mutates <paramref name="result"/> in place
    ''' (writer result, saved FormIDs, verifier summary/icon) and returns the post-overlay
    ''' <see cref="NPC_Data"/> for downstream phases.</summary>
    Private Function ExecuteWritePhases(target As SaveEsp_Form.SaveTarget,
                                        npcFormID As UInteger,
                                        npc As NPC_Data,
                                        rawRecord As PluginRecord,
                                        rawNpcSpec As NPC_Data,
                                        sourcePluginName As String,
                                        baseStateWeightThin As Single,
                                        baseStateWeightMuscular As Single,
                                        baseStateWeightFat As Single,
                                        baseStateValid As Boolean,
                                        ctx As SaveContext,
                                        progress As IProgress(Of SaveProgress),
                                        result As SaveExecutionResult) As WritePhaseResult

        ReportPhase(progress, "Preparing NPC record…", "")

        ' Phase 1a: apply overlay + copy round-trip-only fields.
        Dim npcSpec = ctx.ApplyPresetOverlayToNpcData(rawNpcSpec, npcFormID)
        If Not ReferenceEquals(npcSpec, rawNpcSpec) Then
            ctx.CopyRoundTripOnlyFieldsFromRaw(rawNpcSpec, npcSpec)
            ctx.SyncParallelCollectionsAfterOverlay(npcSpec)
        End If

        ' Phase 1b: detect MWGT user edits via baseState vs raw and copy live values onto shadow.
        Dim mwgtUserEdited As Boolean = False
        If baseStateValid AndAlso
           rawNpcSpec.WeightThin.HasValue AndAlso rawNpcSpec.WeightMuscular.HasValue AndAlso rawNpcSpec.WeightFat.HasValue Then
            Const eps As Single = 0.0001F
            mwgtUserEdited = (Math.Abs(baseStateWeightThin - rawNpcSpec.WeightThin.Value) > eps) OrElse
                             (Math.Abs(baseStateWeightMuscular - rawNpcSpec.WeightMuscular.Value) > eps) OrElse
                             (Math.Abs(baseStateWeightFat - rawNpcSpec.WeightFat.Value) > eps)
        End If
        If mwgtUserEdited Then
            npcSpec.WeightThin = baseStateWeightThin
            npcSpec.WeightMuscular = baseStateWeightMuscular
            npcSpec.WeightFat = baseStateWeightFat
            Using ms As New MemoryStream()
                Using bw As New BinaryWriter(ms)
                    bw.Write(baseStateWeightThin)
                    bw.Write(baseStateWeightMuscular)
                    bw.Write(baseStateWeightFat)
                End Using
                npcSpec.MwgtRaw = ms.ToArray()
            End Using
            npcSpec.HasMwgt = True
        End If

        ' Phase 1c: rebuild HeadPartFormIDs from raw NPC PNAM ∪ preset, dedup by PartType.
        ' (Replicates the merge that ButtonSavePlugin_Click used to do inline.)
        npcSpec.HeadPartFormIDs.Clear()
        Dim overlay As LooksmenuLoader.LooksmenuPreset = Nothing
        ctx.AppliedPresets.TryGetValue(npcFormID, overlay)
        Dim presetHasHeadParts = (overlay IsNot Nothing AndAlso overlay.HasHeadPartFormIDs)
        If presetHasHeadParts Then
            Dim presetParts = overlay.HeadPartFormIDs
            Dim mergedByType As New Dictionary(Of Integer, UInteger)
            Dim freestandingMisc As New List(Of UInteger)
            For Each fid In rawNpcSpec.HeadPartFormIDs
                If fid = 0UI Then Continue For
                Dim hpRec = ctx.PluginManager.GetRecord(fid)
                If hpRec Is Nothing OrElse hpRec.Header.Signature <> "HDPT" Then Continue For
                Dim hd = RecordParsers.ParseHDPT(hpRec, ctx.PluginManager)
                If hd.PartType = 0 Then
                    freestandingMisc.Add(fid)
                ElseIf hd.PartType >= 1 AndAlso hd.PartType <= 9 Then
                    mergedByType(hd.PartType) = fid
                End If
            Next
            For Each fid In presetParts
                If fid = 0UI Then Continue For
                Dim hpRec = ctx.PluginManager.GetRecord(fid)
                If hpRec Is Nothing OrElse hpRec.Header.Signature <> "HDPT" Then Continue For
                Dim hd = RecordParsers.ParseHDPT(hpRec, ctx.PluginManager)
                ' IsExtraPart flag = 0x08; same value used by MainForm.HeadPartFlagIsExtra.
                If (hd.Flags And 8US) <> 0 Then Continue For
                If hd.PartType = 0 Then
                    freestandingMisc.Add(fid)
                ElseIf hd.PartType >= 1 AndAlso hd.PartType <= 9 Then
                    mergedByType(hd.PartType) = fid
                End If
            Next
            For Each t In mergedByType.Keys.OrderBy(Function(k) k)
                npcSpec.HeadPartFormIDs.Add(mergedByType(t))
            Next
            npcSpec.HeadPartFormIDs.AddRange(freestandingMisc)
        Else
            npcSpec.HeadPartFormIDs.AddRange(rawNpcSpec.HeadPartFormIDs)
        End If

        ' Phase 1d: outfit (DOFT) + new-outfit (OTFT) handling for the Edit Outfit "Create" tab.
        '   • SaveNewOutfits ON  → emit as OTFT every draft that is dirty OR referenced by this NPC's DOFT
        '     (so the output plugin is self-contained). If the NPC's DOFT points at a NEW draft (provisional
        '     0xFF FormID) the writer remaps it to the real self FormID. Clean unreferenced drafts are
        '     skipped — the "don't re-save what's already saved unless modified" rule.
        '   • SaveNewOutfits OFF → write NO outfits; if the NPC's DOFT points at a NEW draft, revert it to
        '     the NPC's ORIGINAL record outfit (the user's rule: saving the NPC without the checkbox keeps
        '     its original outfit, not the unsaved draft). A DOFT pointing at a REAL OTFT (existing record,
        '     picked in Browse) is kept either way — the checkbox only governs NEW drafts.
        Dim outfitEntries As New List(Of SaveNpcEspWriter.OtftRecordEntry)
        If target.SaveNewOutfits Then
            If ctx.OutfitDrafts IsNot Nothing Then
                For Each d In ctx.OutfitDrafts
                    If d Is Nothing OrElse d.FormID = OutfitDraft.PreviewDraftFormID Then Continue For
                    If Not (d.IsDirty OrElse d.FormID = npcSpec.DefaultOutfitFormID) Then Continue For
                    Dim oe As New SaveNpcEspWriter.OtftRecordEntry With {
                        .FormID = d.FormID,
                        .EditorID = d.EditorID,
                        .IsOverride = d.IsOverride
                    }
                    oe.ItemArmoFormIDs.AddRange(d.ItemArmoFormIDs)
                    outfitEntries.Add(oe)
                Next
            End If
        ElseIf OutfitDraft.IsDraftFormID(npcSpec.DefaultOutfitFormID) Then
            npcSpec.DefaultOutfitFormID = rawNpcSpec.DefaultOutfitFormID
            npcSpec.HasDefaultOutfit = rawNpcSpec.HasDefaultOutfit
        End If

        Dim entry As New SaveNpcEspWriter.NpcOverrideEntry With {
            .Npc = npcSpec,
            .SourcePluginName = sourcePluginName,
            .OriginalHeader = rawRecord.Header
        }

        ' Phase 2: load existing records from the target plugin if updating.
        ReportPhase(progress, "Loading existing plugin…", IO.Path.GetFileName(target.TargetPath))
        Dim existingRecords As New List(Of PluginRecord)
        Dim existingMasters As New List(Of String)
        If Not target.IsNewPlugin AndAlso File.Exists(target.TargetPath) Then
            Dim reader As New PluginReader()
            reader.Load(target.TargetPath)
            existingMasters.AddRange(reader.Masters)

            ' Translate the global FormID to the local FormID the reader sees (high byte = MAST idx of
            ' the source master in the existing plugin) so we can identify and skip the record we're
            ' about to replace. The source plugin comes from GetOriginatingPluginName (engine FileID
            ' scheme, full + 0xFE light); the object width is 12-bit for an ESL source, 24-bit for full.
            Dim npcSourceMasterName As String = ctx.PluginManager.GetOriginatingPluginName(npcFormID)
            Dim npcIsLight As Boolean = ((npcFormID >> 24) And &HFFUI) = &HFEUI
            Dim npcObject As UInteger = If(npcIsLight, npcFormID And &HFFFUI, npcFormID And &HFFFFFFUI)
            Dim npcLocalFormID As UInteger = npcFormID
            If Not String.IsNullOrEmpty(npcSourceMasterName) Then
                Dim newHigh As Integer = -1
                For i = 0 To reader.Masters.Count - 1
                    If String.Equals(reader.Masters(i), npcSourceMasterName, StringComparison.OrdinalIgnoreCase) Then
                        newHigh = i
                        Exit For
                    End If
                Next
                If newHigh < 0 Then newHigh = reader.Masters.Count  ' self
                npcLocalFormID = (CUInt(newHigh) << 24) Or npcObject
            End If

            For Each kv In reader.Records
                Dim rec = kv.Value
                If rec.Header.FormID = npcLocalFormID Then Continue For
                ' OTFT outfits from a prior save belong to the OTFT path, not existingRecords (which
                ' SerializeExistingRecord only handles for NPC_). Re-emit them as OVERRIDE entries so
                ' the writer preserves them (other NPCs in the plugin may reference them) with proper
                ' FormID + INAM remapping. Resolve to global FormIDs first. Runs regardless of
                ' SaveNewOutfits — preservation of existing records is not gated by the new-draft toggle.
                If rec.Header.Signature = "OTFT" Then
                    Dim parsedOtft = RecordParsers.ParseOTFT(rec, ctx.PluginManager)
                    Dim oe As New SaveNpcEspWriter.OtftRecordEntry With {
                        .FormID = ctx.PluginManager.ResolveReferencedFormID(rec.SourcePluginName, rec.Header.FormID),
                        .EditorID = parsedOtft.EditorID,
                        .IsOverride = True
                    }
                    oe.ItemArmoFormIDs.AddRange(parsedOtft.ItemFormIDs)
                    outfitEntries.Add(oe)
                    Continue For
                End If
                existingRecords.Add(rec)
            Next
        End If

        ' Phase 2b: encoding-conflict check. The writer re-emits the edited NPC AND every
        ' pre-existing NPC using the currently-selected Translatable encoding. If any FULL/SHRT/
        ' ATTX contains characters that don't fit, .NET would silently replace them with '?'
        ' (xEdit-faithful but corrupting). Detect it here — reusing the existingRecords already
        ' loaded above (no second PluginReader.Load) — and abort with a descriptive message.
        Dim editedConflict = FindEncodingConflict(entry.Npc, "")
        If editedConflict <> "" Then Throw New InvalidDataException(editedConflict)
        For Each existing In existingRecords
            If existing.Header.Signature <> "NPC_" Then Continue For
            Dim parsedExisting = RecordParsers.ParseNPC(existing, existing.SourcePluginName, ctx.PluginManager)
            Dim label = If(parsedExisting.HasFull AndAlso parsedExisting.FullName <> "",
                           parsedExisting.FullName, $"FormID {existing.Header.FormID:X8}")
            Dim existingConflict = FindEncodingConflict(parsedExisting, $" del NPC [{label}]")
            If existingConflict <> "" Then Throw New InvalidDataException(existingConflict)
        Next

        ' Phase 3: write plugin.
        ReportPhase(progress, "Writing NPC override to plugin…", IO.Path.GetFileName(target.TargetPath))
        Dim entries As New List(Of SaveNpcEspWriter.NpcOverrideEntry) From {entry}
        Dim game = Config_App.Current.Game
        Dim writeRes = SaveNpcEspWriter.SaveOverridePlugin(
            target.TargetPath, game, target.MarkAsMaster, target.LightMaster,
            entries, existingRecords, existingMasters, ctx.PluginManager, outfitEntries)

        result.WriterResult = writeRes

        For Each existingRec In existingRecords
            result.SavedFormIDs.Add(existingRec.Header.FormID)
        Next
        result.SavedFormIDs.Add(npcFormID)

        ' Phase 3b: refresh the BodyMorphs/Skin sidecar (default ON). Always builds the merged
        ' SidecarFile when WriteBssliders OR EmitBodyGen is set — the BodyGen emitter consumes
        ' the post-merge dict so its .ini reflects both the current NPC and any pre-existing
        ' entries from prior saves of other NPCs to the same plugin.
        Dim mergedSidecar As BssliderSidecar.SidecarFile = Nothing
        If target.WriteBssliders OrElse target.EmitBodyGen Then
            mergedSidecar = MergeSidecarForCurrentNpc(target, npcFormID, npcSpec, ctx)
            If target.WriteBssliders Then
                ReportPhase(progress, "Writing .bssliders sidecar…", IO.Path.GetFileName(target.TargetPath))
                Dim sidecarPath = BssliderSidecar.BuildPath(target.TargetPath)
                BssliderSidecar.Write(sidecarPath, mergedSidecar)
            End If
        End If

        ' Phase 3c: BodyGen .ini pair (opt-in). Iterates the post-merge sidecar so all NPCs of
        ' the plugin contribute their templates, not just the one currently being saved.
        If target.EmitBodyGen AndAlso mergedSidecar IsNot Nothing Then
            ReportPhase(progress, "Writing BodyGen .ini…", IO.Path.GetFileName(target.TargetPath))
            EmitBodyGenFromSidecar(target, mergedSidecar, ctx)
        End If

        result.VerifierIcon = MessageBoxIcon.Information

        ' Default ChargenSuccess to True so the caller can OR it with the bake outcome later.
        result.ChargenSuccess = True

        ' Hand the post-overlay record back so ExecuteAsync can pass it to the bake phase.
        Return New WritePhaseResult With {.NpcSpec = npcSpec}
    End Function

    ''' <summary>Push a marquee-style phase update through IProgress. The runtime marshals the
    ''' callback to the UI thread (the IProgress was constructed there), so the panel repaints
    ''' on the next message-pump tick.</summary>
    Private Sub ReportPhase(progress As IProgress(Of SaveProgress), phase As String, detail As String)
        If progress Is Nothing Then Return
        progress.Report(New SaveProgress With {.Phase = phase, .Detail = detail, .Determinate = False})
    End Sub

    ''' <summary>Read the existing <c>&lt;plugin&gt;.bssliders</c> sidecar (if any), overwrite
    ''' the entry for the NPC being saved with whatever its overlay currently holds, and
    ''' return the merged in-memory SidecarFile. The caller writes it (when the user has
    ''' WriteBssliders ON) and/or feeds it to the BodyGen emitter. Entries for other NPCs of
    ''' the plugin are preserved as-is so a single-NPC save never wipes the rest.</summary>
    Private Function MergeSidecarForCurrentNpc(target As SaveEsp_Form.SaveTarget,
                                               npcFormID As UInteger,
                                               npcSpec As NPC_Data,
                                               ctx As SaveContext) As BssliderSidecar.SidecarFile
        Dim sidecarPath = BssliderSidecar.BuildPath(target.TargetPath)
        Dim merged = BssliderSidecar.Read(sidecarPath)
        If merged Is Nothing Then merged = New BssliderSidecar.SidecarFile()
        merged.Plugin = IO.Path.GetFileName(target.TargetPath)

        ' BodyGen matches morphs.ini rows by the NPC's ORIGINATING master (the plugin that
        ' originally defines the NPC), not by the override plugin we're writing to. Use the
        ' source plugin lookup so the identifier is stable for re-emits even when the load
        ' order changes.
        Dim masterName = ctx.PluginManager.GetOriginatingPluginName(npcFormID)
        If String.IsNullOrEmpty(masterName) Then masterName = "Unknown.esp"
        Dim identifier = BssliderSidecar.BuildIdentifier(masterName, npcFormID)

        Dim entry As New BssliderSidecar.NpcEntry With {
            .EditorId = If(npcSpec.EditorID, ""),
            .Gender = If(npcSpec.IsFemale, "female", "male")
        }

        Dim overlay As LooksmenuLoader.LooksmenuPreset = Nothing
        ctx.AppliedPresets.TryGetValue(npcFormID, overlay)
        If overlay IsNot Nothing Then
            If overlay.BodyMorphSliders IsNot Nothing Then
                For Each kv In overlay.BodyMorphSliders
                    entry.BodyMorphs(kv.Key) = kv.Value
                Next
            End If
            entry.SkinTemplateId = If(overlay.SkinTemplateId, "")
        End If

        ' Always overwrite the current NPC's slot — even if entry ends up empty. Write() drops
        ' empty entries so a clear-then-save round trip removes the row instead of leaving stale
        ' data on disk.
        merged.Npcs(identifier) = entry
        Return merged
    End Function

    ''' <summary>Translate the merged sidecar into BodyGenIniWriter entries and emit the .ini
    ''' pair. Sidecar rows without BodyMorphs (SkinTemplate-only entries) are skipped — the
    ''' Skin override is an F4SE feature unrelated to BodyGen. Malformed identifiers are also
    ''' skipped silently; the sidecar Read() already filters them out, this Catch is belt-and-
    ''' suspenders.</summary>
    Private Sub EmitBodyGenFromSidecar(target As SaveEsp_Form.SaveTarget,
                                       sidecar As BssliderSidecar.SidecarFile,
                                       ctx As SaveContext)
        Dim entries As New List(Of BodyGenIniWriter.NpcEntry)
        For Each kv In sidecar.Npcs
            Dim e = kv.Value
            If e Is Nothing OrElse e.BodyMorphs Is Nothing OrElse e.BodyMorphs.Count = 0 Then Continue For

            Dim masterName As String = ""
            Dim localFid As UInteger = 0UI
            If Not BssliderSidecar.TryParseIdentifier(kv.Key, masterName, localFid) Then Continue For

            Dim editorId = If(e.EditorId, "")
            Dim templateName = "NPCM_" & BodyGenIniWriter.SanitizeTemplateName(editorId)
            entries.Add(New BodyGenIniWriter.NpcEntry With {
                .TemplateName = templateName,
                .MasterPluginFileName = masterName,
                .LocalFormIDHex = localFid.ToString("X6"),
                .Gender = If(e.Gender, ""),
                .BodyMorphs = New Dictionary(Of String, Single)(e.BodyMorphs, StringComparer.OrdinalIgnoreCase)
            })
        Next

        Dim baseName = IO.Path.GetFileNameWithoutExtension(target.TargetPath)
        BodyGenIniWriter.Emit(ctx.DataPath, baseName, entries)
    End Sub

End Module
