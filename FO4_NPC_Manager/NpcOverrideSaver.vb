Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports FO4_Base_Library

''' <summary>
''' Orchestrator for the Save NPC override flow. Owns the multi-phase work that used to live
''' inline in <see cref="MainForm.ButtonSavePlugin_Click"/>: build the override entries, write the
''' plugin via <see cref="SaveNpcEspWriter"/>, optionally bake the CharGen NIF + textures and
''' pack them into BA2.
'''
''' Batch-capable: <see cref="ExecuteAsync"/> takes a LIST of <see cref="NpcSaveInput"/> so the Save
''' dialog can persist either the single selected NPC or every NPC the user changed this session.
''' All NPCs in the batch are written into ONE target plugin in a single write; the CharGen bake
''' runs once per NPC after the write.
'''
''' Reports progress through <see cref="IProgress(Of SaveProgress)"/> so the caller (the Save
''' dialog) can render an embedded progress panel without a separate form. Cleanup tasks that
''' depend on MainForm internals (auto-gen plugin cache, NPC tree refresh, post-save re-read,
''' success MessageBox) are NOT performed here — the orchestrator returns the data those steps
''' need and MainForm performs them after the dialog closes.
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

    ''' <summary>Per-NPC input for a save run. MainForm builds one of these for every NPC being
    ''' saved (the selected one, or every dirty NPC) from its cached record + a fresh raw parse.</summary>
    Public Class NpcSaveInput
        Public NpcFormID As UInteger
        ''' <summary>Cached NPC_Data (for EditorID in progress/messages). Not used for serialization.</summary>
        Public Npc As NPC_Data
        Public RawRecord As PluginRecord
        ''' <summary>Fresh type-safe parse of <see cref="RawRecord"/> — the base the overlay is applied on.</summary>
        Public RawNpcSpec As NPC_Data
        Public SourcePluginName As String
        ''' <summary>ACBS "Is CharGen Face Preset" bit. Drives whether the CharGen bake is optional
        ''' for this NPC; the dialog forces the bake on when ANY batch NPC lacks the flag.</summary>
        Public IsCharGenFacePreset As Boolean
    End Class

    ''' <summary>Outcome of <see cref="ExecuteAsync"/>. Populated even on failure so the caller
    ''' can show a meaningful error.</summary>
    Public Class SaveExecutionResult
        Public Success As Boolean
        ''' <summary>True when the bake loop was stopped early by the user. The plugin write already
        ''' succeeded (Success stays True); some NPCs' FaceGen BA2 may be unbaked.</summary>
        Public BakeCancelled As Boolean
        Public WriterResult As SaveNpcEspWriter.SaveResult
        ''' <summary>Final list of NPC FormIDs in the saved plugin (preserved existing + every new
        ''' override). Used by MainForm to update the auto-gen plugin cache.</summary>
        Public SavedFormIDs As New List(Of UInteger)
        ''' <summary>The FormIDs this run actually wrote as overrides (the batch). MainForm uses this
        ''' for the post-save re-read + overlay cleanup (distinct from SavedFormIDs, which also
        ''' includes pre-existing records preserved from the target plugin).</summary>
        Public WrittenNpcFormIDs As New List(Of UInteger)
        ''' <summary>Provisional draft FormID → file-local real FormID for every OTFT/LVLI draft emitted this
        ''' save (copied from <see cref="SaveNpcEspWriter.SaveResult.DraftFormIdMap"/>). MainForm uses it to
        ''' promote drafts to real records post-readback. Empty when no new outfits/leveled lists were written.</summary>
        Public DraftFormIdMap As Dictionary(Of UInteger, UInteger) = Nothing
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
        ''' <summary>MainForm helper (optional): apply the NPC-record scalar/list override authored in the NPC
        ''' Editor onto the post-round-trip shadow. Args = (shadow NPC_Data, NPC global FormID). Invoked in
        ''' <see cref="BuildOverrideEntry"/> JUST AFTER the round-trip copy so the user's Name/flags/keywords/
        ''' factions/inventory/OBTS edits win over the source record. Nothing = no NPC-record overrides authored
        ''' (existing callers / no-op).</summary>
        Public ApplyNpcRecordOverride As Action(Of NPC_Data, UInteger) = Nothing
        ''' <summary>FaceGen bake delegate: invoked once per NPC during Phase 4a. Writes the 4 loose
        ''' files (NIF + 3 DDS) on the UI thread (GL-bound), returns a <see cref="NpcFaceGenPacker.BakedNpcBundle"/>
        ''' identifying that NPC's bake outputs so the orchestrator can batch them into one pack call.
        ''' Bundle is Nothing when the bake was skipped (no FaceGen head parts) or failed.</summary>
        Public RunChargenBake As Func(Of UInteger, String, String, IProgress(Of SaveProgress), Task(Of (Success As Boolean, Skipped As Boolean, Bundle As NpcFaceGenPacker.BakedNpcBundle, FailureMessage As String)))

        ''' <summary>BA2 pack delegate: invoked in Phase 4b with the bundles collected from all successful Phase 4a
        ''' bakes PLUS the canonical FaceGen entry paths to REMOVE from the target archive set (mark-to-delete).
        ''' Honors the loose-only sentinel (NPC_Config.Ba2Version_FO4 = 0) by skipping the pack. Returns a single
        ''' summary the orchestrator appends to the user-facing message. The excludeEntries arg is the 3rd param.</summary>
        Public RunChargenPackBatch As Func(Of String, IReadOnlyList(Of NpcFaceGenPacker.BakedNpcBundle), IReadOnlyList(Of String), IProgress(Of SaveProgress), Task(Of (Summary As String, Success As Boolean)))
        ''' <summary>All outfit drafts authored in the Edit Outfit "Create" tab (MainForm's
        ''' <c>_outfitDrafts</c>, minus the throwaway preview sentinel). When the save target's
        ''' <c>SaveNewOutfits</c> is True, the orchestrator emits as OTFT records every draft that is
        ''' dirty OR referenced by any saved NPC's DOFT (so the plugin is self-contained); clean
        ''' unreferenced drafts are skipped (the "don't re-save what's already saved" rule). Nothing = none.</summary>
        Public OutfitDrafts As List(Of OutfitDraft) = Nothing
        ''' <summary>All author-built leveled lists (LVLI drafts) from MainForm's <c>_leveledListDrafts</c>.
        ''' When <c>SaveNewOutfits</c> is True the orchestrator emits as LVLI records the TRANSITIVE CLOSURE
        ''' of drafts reachable from the emitted OTFTs' items (so an outfit that references a draft LVLI — and
        ''' a draft LVLI that nests other draft LVLIs — never writes a dangling 0xFF reference), plus any dirty
        ''' draft the user built standalone. Nothing = none.</summary>
        Public LeveledListDrafts As List(Of LeveledListDraft) = Nothing
        ''' <summary>All Armor (ARMO) drafts from MainForm's <c>_armoDrafts</c>. When <c>SaveNewOutfits</c> is
        ''' True the orchestrator emits as ARMO records the TRANSITIVE CLOSURE of drafts reachable from the
        ''' emitted OTFTs' items + leveled-list entries + WNAM skin overrides (so a saved outfit/skin that
        ''' references a draft ARMO is self-contained), plus any dirty referenced standalone draft. Each needed
        ''' ARMO draft pulls in its ARMA draft refs (ArmorAddons) and MSWP draft refs (material swaps). Nothing = none.</summary>
        Public ArmoDrafts As List(Of ArmoDraft) = Nothing
        ''' <summary>All Armor Addon (ARMA) drafts from MainForm's <c>_armaDrafts</c>. Emitted (Phase 2f) for the
        ''' subset reachable from the needed ARMO drafts' ArmorAddons; each needed ARMA pulls in its material-swap
        ''' MSWP draft refs. Nothing = none.</summary>
        Public ArmaDrafts As List(Of ArmaDraft) = Nothing
        ''' <summary>All Material Swap (MSWP) drafts from MainForm's <c>_mswpDrafts</c>. Emitted (Phase 2g) for the
        ''' subset reachable from the needed ARMO/ARMA drafts' material-swap FormIDs. Nothing = none.</summary>
        Public MswpDrafts As List(Of MswpDraft) = Nothing
        ''' <summary>GLOBAL FormIDs the user marked for REMOVAL from their plugin (Delete of a saved NEW record /
        ''' Revert of a saved OVERRIDE). Records with these FormIDs are NOT preserved in Phase 2a, so on re-save a
        ''' new record vanishes and an override is dropped (the base/original record wins again). Nothing = none.</summary>
        Public RecordsToRemove As HashSet(Of UInteger) = Nothing
        ''' <summary>MainForm helper: allocate the next provisional draft FormID (0xFF high byte) from the
        ''' SAME counter as OTFT/LVLI drafts, so a Leveled-NPC list (LVLN) built at save time
        ''' (<c>SaveTarget.AddToLvlList</c>) gets a sentinel that can't collide with any other draft. The
        ''' writer rewrites it to a real self-index FormID via draftRemap. Nothing → the saver uses a safe
        ''' local fallback counter (the LVLN are terminal, nothing references them by provisional).</summary>
        Public AllocateDraftFormID As Func(Of UInteger) = Nothing
    End Class

    ''' <summary>Run the save end-to-end for one or more NPCs into a single target plugin. Hybrid
    ''' threading model — pure-IO phases (build entries, write ESP, sidecar) run on a worker Task
    ''' so the UI message pump stays alive and the progress panel repaints; the CharGen bake runs
    ''' on the UI thread because it touches the OpenGL render host, which is single-thread-bound to
    ''' the GL context owner. Awaits alternate the two so the orchestrator stays a single linear flow.
    '''
    ''' <paramref name="bakeCancel"/> is checked only between NPCs in the bake loop (the long part).
    ''' The plugin write itself is atomic and not cancellable; cancelling stops baking the remaining
    ''' NPCs' FaceGen, leaving the already-written ESP intact.</summary>
    Public Async Function ExecuteAsync(target As SaveEsp_Form.SaveTarget,
                                       inputs As List(Of NpcSaveInput),
                                       ctx As SaveContext,
                                       progress As IProgress(Of SaveProgress),
                                       bakeCancel As CancellationToken) As Task(Of SaveExecutionResult)

        Dim result As New SaveExecutionResult

        Try
            ' Phases 1-3 (build entries → existing-plugin load → write → sidecar) are pure CPU/IO;
            ' run on a worker so the UI thread keeps pumping messages and the progress panel repaints.
            Await Task.Run(Sub()
                               ExecuteWritePhases(target, inputs, ctx, progress, result)
                           End Sub)

            ' Phase 4: CharGen bake + BA2 pack, split into two sub-phases:
            '   4a) Per-NPC GL bake (UI thread) — writes the 4 loose files. Collects a BakedNpcBundle
            '       for each successful bake into 'bundles' (the deferred pack list).
            '   4b) Single PackBatch call (worker thread) with all collected bundles. ArchivePackager
            '       still does CRC32 diff per entry so override semantics are preserved; the win is
            '       O(N²)→O(K) BA2 rewrites where K = ceil(totalCompressedBytes / MEMORY_CAP_BYTES)
            '       (typically K=1 for normal save sizes). When NPC_Config.Ba2Version_FO4=0
            '       (loose-only sentinel), the PackBatch delegate skips the pack entirely and the
            '       loose stay on disk.
            ' Mark-to-delete: canonical FaceGen entry paths of every removed NPC, to STRIP from the target BA2
            ' set (only the app's own archives are touched; a path not present is a no-op). Built regardless of
            ' GenerateChargen so a delete-only save still cleans the BA2 (see the removal-only pack below).
            Dim faceGenExcludeEntries As New List(Of String)
            If ctx.RecordsToRemove IsNot Nothing Then
                For Each remFid In ctx.RecordsToRemove
                    faceGenExcludeEntries.AddRange(NpcFaceGenPacker.CanonicalFaceGenEntryPathsForNpc(remFid, ctx.PluginManager, Config_App.Current.Game))
                Next
            End If

            If target.GenerateChargen Then
                Dim totalBakes = inputs.Count
                Dim bakedOk = 0
                Dim bakedFail = 0
                Dim bakedSkip = 0
                Dim bundles As New List(Of NpcFaceGenPacker.BakedNpcBundle)
                For i = 0 To inputs.Count - 1
                    If bakeCancel.IsCancellationRequested Then
                        result.BakeCancelled = True
                        Exit For
                    End If
                    Dim npcInput = inputs(i)
                    ' MARK-TO-DELETE: never bake a CharGen NIF/textures for an NPC that is being removed this
                    ' save (it isn't written as an override, and its bakes are deleted in post-save cleanup).
                    If ctx.RecordsToRemove IsNot Nothing AndAlso ctx.RecordsToRemove.Contains(npcInput.NpcFormID) Then
                        bakedSkip += 1
                        Continue For
                    End If
                    Dim label = If(npcInput.Npc IsNot Nothing AndAlso Not String.IsNullOrEmpty(npcInput.Npc.EditorID),
                                   npcInput.Npc.EditorID, npcInput.NpcFormID.ToString("X8"))
                    progress?.Report(New SaveProgress With {
                        .Phase = "Baking CharGen NIF + textures…",
                        .Detail = $"NPC {i + 1}/{totalBakes}: {label}",
                        .Determinate = True,
                        .Max = totalBakes,
                        .Current = i + 1
                    })
                    ' Yield to the WinForms message pump BEFORE the synchronous GL-bound bake
                    ' grabs the UI thread for the next NPC. Without this, a Stop click between
                    ' NPCs has no chance to register — the synchronous bake call blocks the pump
                    ' until completion. Task.Delay(1) (≈ one timer tick) is stronger than
                    ' Task.Yield(): Yield just posts the continuation back to the SyncContext,
                    ' Delay actually drains the queued WM_PAINT + WM_LBUTTONDOWN events.
                    ' Re-check cancel immediately after the yield in case Stop fired during it.
                    Await Task.Delay(1)
                    If bakeCancel.IsCancellationRequested Then
                        result.BakeCancelled = True
                        Exit For
                    End If
                    Dim bakeRes = Await ctx.RunChargenBake(npcInput.NpcFormID, target.TargetPath, npcInput.SourcePluginName, progress)
                    ' Skipped (no FaceGen head parts — non-human race, etc.) is counted separately from
                    ' OK/failed; reported as a SKIP in the summary.
                    If bakeRes.Skipped Then
                        bakedSkip += 1
                    ElseIf bakeRes.Success AndAlso bakeRes.Bundle IsNot Nothing Then
                        bakedOk += 1
                        bundles.Add(bakeRes.Bundle)
                    Else
                        bakedFail += 1
                    End If
                Next

                ' Phase 4b: single PackBatch call with all successful bundles PLUS the mark-to-delete exclusions.
                ' Runs when there are new bakes OR entries to strip (delete-only save with existing target bakes).
                Dim packSummary As String = ""
                Dim packSuccess As Boolean = True
                If bundles.Count > 0 OrElse faceGenExcludeEntries.Count > 0 Then
                    Dim packRes = Await ctx.RunChargenPackBatch(target.TargetPath, bundles, faceGenExcludeEntries, progress)
                    packSummary = If(packRes.Summary, "")
                    packSuccess = packRes.Success
                End If

                If totalBakes = 1 Then
                    ' Single-NPC: terse summary. Only mention skip/failure when there is one (no noise).
                    result.ChargenSummary = $"{vbCrLf}{vbCrLf}CharGen bake: {bakedOk} OK" &
                        If(bakedSkip > 0, $", {bakedSkip} skipped", "") &
                        If(bakedFail > 0, $", {bakedFail} failed", "") & "."
                Else
                    result.ChargenSummary = $"{vbCrLf}{vbCrLf}CharGen bake: {bakedOk}/{totalBakes} OK" &
                        If(bakedSkip > 0, $", {bakedSkip} skipped", "") &
                        If(bakedFail > 0, $", {bakedFail} failed", "") &
                        If(result.BakeCancelled, " (cancelled — remaining NPCs not baked)", "") & "."
                End If
                If packSummary <> "" Then result.ChargenSummary &= vbCrLf & packSummary
                result.ChargenSuccess = (bakedFail = 0) AndAlso packSuccess
                If Not result.ChargenSuccess Then result.VerifierIcon = MessageBoxIcon.Warning
            ElseIf faceGenExcludeEntries.Count > 0 Then
                ' Delete-only save with CharGen bake OFF: still strip the removed NPCs' stale bakes from the
                ' target BA2 (removal-only Pack; a no-op when the entries aren't present / loose-only mode).
                Dim packRes = Await ctx.RunChargenPackBatch(target.TargetPath, New List(Of NpcFaceGenPacker.BakedNpcBundle)(), faceGenExcludeEntries, progress)
                If Not String.IsNullOrEmpty(packRes.Summary) Then result.ChargenSummary &= vbCrLf & packRes.Summary
                If Not packRes.Success Then result.VerifierIcon = MessageBoxIcon.Warning
            End If

            result.Success = True
        Catch ex As Exception
            result.Success = False
            result.ErrorMessage = ex.Message
        End Try

        Return result
    End Function

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

    ''' <summary>Phases 1-3 of the save: build one override entry per input NPC (overlay + MWGT +
    ''' HeadParts merge), load existing records (skipping every NPC being written), write the plugin
    ''' in a single pass, and refresh the BodyMorphs/Skin sidecar. All pure CPU/IO — safe on a worker
    ''' Task. Mutates <paramref name="result"/> in place.</summary>
    Private Sub ExecuteWritePhases(target As SaveEsp_Form.SaveTarget,
                                   inputs As List(Of NpcSaveInput),
                                   ctx As SaveContext,
                                   progress As IProgress(Of SaveProgress),
                                   result As SaveExecutionResult)

        ReportPhase(progress, "Preparing NPC records…", "")

        ' EditorID namespace segment for author-built records (OTFT/LVLI/LVLN), known only here (at save):
        ' the destination plugin name. Injected into every NEW author record → npcm_<ESPNAME>_<TYPE>_<name>.
        Dim espNameNoExt = IO.Path.GetFileNameWithoutExtension(target.TargetPath)

        ' MARK-TO-DELETE wins over dirty: an NPC marked for removal is NEVER (re)written as an override — only
        ' the Phase 2a drop applies. This single choke point covers every scope (selected / all-dirty): the
        ' removal set is subtracted from the inputs the writer actually serializes, the sidecar merges, and the
        ' WrittenNpcFormIDs post-save readback re-reads. The FULL `inputs` list is still used for the Phase 2a
        ' skipLocalFormIDs sweep (so a marked NPC's existing copy is dropped there too, belt-and-suspenders).
        Dim removeSet As HashSet(Of UInteger) = If(ctx.RecordsToRemove, New HashSet(Of UInteger)())
        Dim writeInputs = inputs.Where(Function(ni) Not removeSet.Contains(ni.NpcFormID)).ToList()

        ' Phase 1: build one override entry per NON-removed NPC. outfitEntries is shared and deduped at the end.
        Dim entries As New List(Of SaveNpcEspWriter.NpcOverrideEntry)
        For Each npcInput In writeInputs
            Dim entry = BuildOverrideEntry(npcInput, ctx, target)
            ' Encoding-conflict check for the edited NPC (per-NPC FULL/SHRT/ATTX vs encoding).
            Dim editedConflict = FindEncodingConflict(entry.Npc, "")
            If editedConflict <> "" Then Throw New InvalidDataException(editedConflict)
            entries.Add(entry)
        Next

        ' Phase 2: load existing records from the target plugin when updating, skipping EVERY NPC
        ' we are about to (re)write. Outfit records (OTFT) authored in prior saves are re-emitted as
        ' OVERRIDE entries so they survive (other NPCs may reference them).
        ReportPhase(progress, "Loading existing plugin…", IO.Path.GetFileName(target.TargetPath))
        Dim existingRecords As New List(Of PluginRecord)
        Dim existingMasters As New List(Of String)
        Dim outfitEntries As New List(Of SaveNpcEspWriter.OtftRecordEntry)
        ' Existing LVLI records (authored in prior saves) are preserved as OVERRIDE entries too, here, so a
        ' re-save of the same plugin doesn't choke (the writer only copy-through-preserves NPC_ records).
        ' Draft LVLIs (Phase 2d) append to this same list.
        Dim leveledEntries As New List(Of SaveNpcEspWriter.LvliRecordEntry)
        ' Author-built ARMA/ARMO/MSWP drafts (Phases 2e/2f/2g) append to these. The existing-plugin sweep
        ' below ALSO re-emits preserved ARMO/ARMA/MSWP records of the target plugin as OVERRIDE entries here
        ' (same as OTFT/LVLI) — without that they hit SerializeExistingRecord, which only handles NPC_ and
        ' throws. The drafts themselves may be OVERRIDE flavour (edit of a load-order record), which the writer
        ' emits via SourceRecord merge; Phases 2e/2f/2g dedup drafts against the preserved entries by FormID/EDID.
        Dim armoEntries As New List(Of SaveNpcEspWriter.ArmoRecordEntry)
        Dim armaEntries As New List(Of SaveNpcEspWriter.ArmaRecordEntry)
        Dim mswpEntries As New List(Of SaveNpcEspWriter.MswpRecordEntry)
        ' HEDR.NextObjectID of the on-disk plugin (0 when creating fresh). Forwarded to the writer
        ' so re-save doesn't roll back the dispense counter and accidentally re-issue an ID that
        ' CK already consumed between saves (mirror of TwbFile.NewFormID at wbImplementation.pas:5083).
        Dim existingNextObjectId As UInteger = 0UI
        If Not target.IsNewPlugin AndAlso File.Exists(target.TargetPath) Then
            Dim reader As New PluginReader()
            reader.Load(target.TargetPath)
            existingMasters.AddRange(reader.Masters)
            existingNextObjectId = reader.NextObjectId

            ' Build the set of LOCAL FormIDs (as the target plugin's MAST list sees them) for every
            ' NPC being written, so we drop the records we're about to replace. Mirror of the engine
            ' FileID scheme: 12-bit object for an ESL source, 24-bit for a full source.
            Dim skipLocalFormIDs As New HashSet(Of UInteger)
            For Each npcInput In inputs
                skipLocalFormIDs.Add(MapGlobalToLocalInPlugin(npcInput.NpcFormID, reader, ctx.PluginManager))
            Next

            For Each kv In reader.Records
                Dim rec = kv.Value
                If skipLocalFormIDs.Contains(rec.Header.FormID) Then Continue For
                ' Records the user marked for REMOVAL (Delete a saved NEW record / Revert a saved OVERRIDE) —
                ' don't preserve them: a new record vanishes; an override is dropped so the original (base plugin)
                ' wins again. Compare on the GLOBAL FormID (the removal set is keyed the way the UI sees records).
                If ctx.RecordsToRemove IsNot Nothing AndAlso ctx.RecordsToRemove.Count > 0 Then
                    Dim globalFid = ctx.PluginManager.ResolveReferencedFormID(rec.SourcePluginName, rec.Header.FormID)
                    If ctx.RecordsToRemove.Contains(globalFid) Then Continue For
                End If
                If rec.Header.Signature = "OTFT" Then
                    Dim parsedOtft = RecordParsers.ParseOTFT(rec, ctx.PluginManager)
                    Dim oe As New SaveNpcEspWriter.OtftRecordEntry With {
                        .FormID = ctx.PluginManager.ResolveReferencedFormID(rec.SourcePluginName, rec.Header.FormID),
                        .EditorID = parsedOtft.EditorID,
                        .IsOverride = True,
                        .OriginalVcs1 = rec.Header.VCS1,
                        .OriginalVcs2 = rec.Header.VCS2
                    }
                    oe.ItemArmoFormIDs.AddRange(parsedOtft.ItemFormIDs)
                    outfitEntries.Add(oe)
                    Continue For
                End If
                If rec.Header.Signature = "LVLI" Then
                    Dim parsedLvli = RecordParsers.ParseLVLI(rec, ctx.PluginManager)
                    Dim le As New SaveNpcEspWriter.LvliRecordEntry With {
                        .FormID = ctx.PluginManager.ResolveReferencedFormID(rec.SourcePluginName, rec.Header.FormID),
                        .EditorID = parsedLvli.EditorID,
                        .ObjectBoundsRaw = parsedLvli.ObjectBoundsRaw,
                        .ChanceNone = parsedLvli.ChanceNone,
                        .MaxCount = parsedLvli.MaxCount,
                        .Flags = parsedLvli.Flags,
                        .IsOverride = True,
                        .HasUseGlobal = parsedLvli.HasUseGlobal,
                        .UseGlobalFormID = parsedLvli.UseGlobalFormID,
                        .HasEpicLootChance = parsedLvli.HasEpicLootChance,
                        .EpicLootChanceFormID = parsedLvli.EpicLootChanceFormID,
                        .HasOverrideName = parsedLvli.HasOverrideName,
                        .OverrideName = parsedLvli.OverrideName,
                        .OriginalVcs1 = rec.Header.VCS1,
                        .OriginalVcs2 = rec.Header.VCS2
                    }
                    For Each ent In parsedLvli.Entries
                        le.Entries.Add(New SaveNpcEspWriter.LvliEntryData With {
                            .Level = ent.Level,
                            .RefFormID = ent.FormID,
                            .Count = ent.Count,
                            .ChanceNone = ent.ChanceNone,
                            .HasCoed = ent.HasCoed,
                            .CoedOwnerFormID = ent.CoedOwnerFormID,
                            .CoedOwnerExtra = ent.CoedOwnerExtra,
                            .CoedExtraIsFormID = ent.CoedExtraIsFormID,
                            .CoedItemCondition = ent.CoedItemCondition})
                    Next
                    For Each fk In parsedLvli.FilterKeywords
                        le.FilterKeywords.Add(New SaveNpcEspWriter.LvliFilterKeywordData With {
                            .KeywordFormID = fk.KeywordFormID,
                            .Chance = fk.Chance})
                    Next
                    leveledEntries.Add(le)
                    Continue For
                End If
                If rec.Header.Signature = "LVLN" Then
                    ' Leveled NPC lists authored externally (or in a prior save) are preserved as OVERRIDE
                    ' entries on the shared leveled path (IsNpcList=True → emitted as LVLN). Without this they
                    ' fell through to existingRecords → SerializeExistingRecord, which only handles NPC_ and
                    ' threw "currently only supports NPC_ records. Encountered 'LVLN'". Full round-trip parity
                    ' with LVLI (OBND/LVLM/LVLG/COED/LLKC + LVLN-only generic model).
                    Dim parsedLvln = RecordParsers.ParseLVLN(rec, ctx.PluginManager)
                    Dim le As New SaveNpcEspWriter.LvliRecordEntry With {
                        .FormID = ctx.PluginManager.ResolveReferencedFormID(rec.SourcePluginName, rec.Header.FormID),
                        .EditorID = parsedLvln.EditorID,
                        .ObjectBoundsRaw = parsedLvln.ObjectBoundsRaw,
                        .ChanceNone = parsedLvln.ChanceNone,
                        .MaxCount = parsedLvln.MaxCount,
                        .Flags = parsedLvln.Flags,
                        .IsOverride = True,
                        .IsNpcList = True,
                        .HasUseGlobal = parsedLvln.HasUseGlobal,
                        .UseGlobalFormID = parsedLvln.UseGlobalFormID,
                        .OriginalVcs1 = rec.Header.VCS1,
                        .OriginalVcs2 = rec.Header.VCS2
                    }
                    For Each ent In parsedLvln.Entries
                        le.Entries.Add(New SaveNpcEspWriter.LvliEntryData With {
                            .Level = ent.Level,
                            .RefFormID = ent.FormID,
                            .Count = ent.Count,
                            .ChanceNone = ent.ChanceNone,
                            .HasCoed = ent.HasCoed,
                            .CoedOwnerFormID = ent.CoedOwnerFormID,
                            .CoedOwnerExtra = ent.CoedOwnerExtra,
                            .CoedExtraIsFormID = ent.CoedExtraIsFormID,
                            .CoedItemCondition = ent.CoedItemCondition})
                    Next
                    For Each fk In parsedLvln.FilterKeywords
                        le.FilterKeywords.Add(New SaveNpcEspWriter.LvliFilterKeywordData With {
                            .KeywordFormID = fk.KeywordFormID,
                            .Chance = fk.Chance})
                    Next
                    For Each m In parsedLvln.ModelSubrecords
                        le.ModelSubrecords.Add((m.Signature, m.Data))
                    Next
                    leveledEntries.Add(le)
                    Continue For
                End If
                ' ARMO/ARMA/MSWP authored in a prior save are preserved as OVERRIDE entries on their
                ' respective draft paths (the writer's SerializeArmo/Arma/MswpRecordOverride path: owned
                ' fields from the entry, all other subrecords copied verbatim from SourceRecord with FormIDs
                ' remapped). Without this they fell through to existingRecords → SerializeExistingRecord, which
                ' only handles NPC_ and threw "currently only supports NPC_ records. Encountered 'ARMO'…". The
                ' draft phases (2e/2f/2g) APPEND to these same lists and dedup against the preserved entries.
                If rec.Header.Signature = "ARMO" Then
                    armoEntries.Add(BuildArmoEntryFromParsed(RecordParsers.ParseARMO(rec, ctx.PluginManager), rec, ctx))
                    Continue For
                End If
                If rec.Header.Signature = "ARMA" Then
                    armaEntries.Add(BuildArmaEntryFromParsed(RecordParsers.ParseARMA(rec, ctx.PluginManager), rec, ctx))
                    Continue For
                End If
                If rec.Header.Signature = "MSWP" Then
                    mswpEntries.Add(BuildMswpEntryFromParsed(RecordParsers.ParseMSWP(rec, ctx.PluginManager), rec, ctx))
                    Continue For
                End If
                existingRecords.Add(rec)
            Next
        End If

        ' Phase 2b: encoding-conflict check for every pre-existing NPC re-emitted by the writer.
        For Each existing In existingRecords
            If existing.Header.Signature <> "NPC_" Then Continue For
            Dim parsedExisting = RecordParsers.ParseNPC(existing, existing.SourcePluginName, ctx.PluginManager)
            Dim label = If(parsedExisting.HasFull AndAlso parsedExisting.FullName <> "",
                           parsedExisting.FullName, $"FormID {existing.Header.FormID:X8}")
            Dim existingConflict = FindEncodingConflict(parsedExisting, $" del NPC [{label}]")
            If existingConflict <> "" Then Throw New InvalidDataException(existingConflict)
        Next

        ' Phase 2c: new-outfit (OTFT) drafts authored in the Edit Outfit "Create" tab. Emitted ONCE
        ' for the whole batch: every dirty draft, plus any draft referenced by a saved NPC's DOFT
        ' (so the plugin is self-contained). Deduped against the existing-OTFT entries by FormID.
        If target.SaveNewOutfits AndAlso ctx.OutfitDrafts IsNot Nothing Then
            Dim referencedDoft As New HashSet(Of UInteger)
            For Each entry In entries
                If entry.Npc IsNot Nothing Then referencedDoft.Add(entry.Npc.DefaultOutfitFormID)
            Next
            Dim alreadyEmitted As New HashSet(Of UInteger)(outfitEntries.Select(Function(o) o.FormID))
            ' EDID uniqueness guard: dedup each NEW draft's final namespaced EditorID against the OTFTs already
            ' bound for this plugin (preserved existing + earlier drafts), auto-suffixing _2/_3 on collision.
            ' Overrides keep their EditorID verbatim (they target an existing record by FormID) but still seed
            ' the set so a later new draft doesn't collide with them.
            Dim usedOtftEdids As New HashSet(Of String)(outfitEntries.Select(Function(o) o.EditorID), StringComparer.OrdinalIgnoreCase)
            For Each d In ctx.OutfitDrafts
                If d Is Nothing OrElse d.FormID = OutfitDraft.PreviewDraftFormID Then Continue For
                If Not (d.IsDirty OrElse referencedDoft.Contains(d.FormID)) Then Continue For
                ' An OVERRIDE draft (user edited an existing OTFT in the Create tab) targets a real OTFT
                ' FormID. When that OTFT was preserved from the target plugin in Phase 2a it is already in
                ' outfitEntries with its OLD items, so the dedup below would skip the draft and silently drop
                ' the user's edits (the stale preserved version wins). Replace the preserved entry's items
                ' with the draft's edited items so the override is actually persisted. (A cross-plugin
                ' override target is not in outfitEntries → falls through and is emitted as a new override.)
                If d.IsOverride Then
                    ' Skip an OVERRIDE whose item set is IDENTICAL to the outfit it overrides — writing it would
                    ' just duplicate the original. The OutfitDraft marks every override IsModified on Create, so
                    ' the dirty flag can't tell us "actually changed"; compare the items against the record the
                    ' draft overrides (the current winning record = what it was loaded from) here at save time.
                    ' Safe because the NPC's DOFT points at this same FormID, which resolves to the original (or
                    ' this plugin's own copy preserved in Phase 2a). Mirror of the ArmA/ArmO "unchanged override
                    ' → don't emit" rule.
                    Dim overridden = ctx.PluginManager.GetRecord(d.FormID)
                    If overridden IsNot Nothing AndAlso overridden.Header.Signature = "OTFT" Then
                        Dim origItems = RecordParsers.ParseOTFT(overridden, ctx.PluginManager).ItemFormIDs
                        If OutfitItemsEqual(d.ItemFormIDs, origItems) Then Continue For
                    End If
                    Dim preserved = outfitEntries.FirstOrDefault(Function(o) o.FormID = d.FormID)
                    If preserved IsNot Nothing Then
                        preserved.ItemArmoFormIDs.Clear()
                        preserved.ItemArmoFormIDs.AddRange(d.ItemFormIDs)
                        Continue For
                    End If
                End If
                If Not alreadyEmitted.Add(d.FormID) Then Continue For
                Dim oeEdid As String
                If d.IsOverride Then
                    oeEdid = d.EditorID
                    usedOtftEdids.Add(oeEdid)
                Else
                    Dim desiredEdid = ApplyEspNamespaceToEditorId(d.EditorID, espNameNoExt)
                    oeEdid = MakeUniqueEditorId(desiredEdid, usedOtftEdids)
                    If Not String.Equals(oeEdid, desiredEdid, StringComparison.Ordinal) Then
                        Logger.LogLazy(Function() $"[SAVE] Outfit EditorID '{desiredEdid}' already used in {IO.Path.GetFileName(target.TargetPath)} → renamed to '{oeEdid}' (FormID unchanged).")
                    End If
                End If
                Dim oe As New SaveNpcEspWriter.OtftRecordEntry With {
                    .FormID = d.FormID,
                    .EditorID = oeEdid,
                    .IsOverride = d.IsOverride
                }
                ' INAM = the draft's items as authored — ARMO or LVLI FormIDs (the writer's INAM is
                ' FormID-agnostic, so an LVLI ref persists as a leveled entry; the engine rolls at runtime).
                oe.ItemArmoFormIDs.AddRange(d.ItemFormIDs)
                outfitEntries.Add(oe)
            Next
        End If

        ' Phase 2d: author-built leveled lists (LVLI drafts) needed by this save. Emit the TRANSITIVE
        ' CLOSURE of draft LVLIs reachable from the emitted OTFTs' INAM items (so a saved outfit that
        ' references a draft LVLI — and a draft LVLI that nests other draft LVLIs — is self-contained and
        ' never writes a dangling 0xFF reference), plus any dirty draft the user built standalone. The
        ' writer pre-assigns every draft (OTFT + LVLI) its real self-index FormID, so the cross-references
        ' resolve regardless of emit order. Appends to leveledEntries (which already holds the preserved
        ' existing LVLI overrides); drafts are deduped by FormID against those.
        If target.SaveNewOutfits AndAlso ctx.LeveledListDrafts IsNot Nothing AndAlso ctx.LeveledListDrafts.Count > 0 Then
            Dim alreadyLeveled As New HashSet(Of UInteger)(leveledEntries.Select(Function(l) l.FormID))
            Dim draftByFid As New Dictionary(Of UInteger, LeveledListDraft)
            For Each d In ctx.LeveledListDrafts
                If d IsNot Nothing Then draftByFid(d.FormID) = d
            Next
            Dim needed As New HashSet(Of UInteger)
            Dim toVisit As New Queue(Of UInteger)
            ' Seed: every draft LVLI referenced by an emitted OTFT's items.
            For Each oe In outfitEntries
                For Each fid In oe.ItemArmoFormIDs
                    If draftByFid.ContainsKey(fid) AndAlso needed.Add(fid) Then toVisit.Enqueue(fid)
                Next
            Next
            ' Seed: dirty standalone drafts (user built them this session; persist even if unreferenced).
            For Each d In ctx.LeveledListDrafts
                If d IsNot Nothing AndAlso d.IsDirty AndAlso needed.Add(d.FormID) Then toVisit.Enqueue(d.FormID)
            Next
            ' Walk nested draft LVLI → draft LVLI references (cycle-safe via the visited set).
            While toVisit.Count > 0
                Dim fid = toVisit.Dequeue()
                Dim d = draftByFid(fid)
                For Each e In d.Entries
                    If draftByFid.ContainsKey(e.RefFormID) AndAlso needed.Add(e.RefFormID) Then toVisit.Enqueue(e.RefFormID)
                Next
            End While
            ' Build the writer entries (skip any FormID already present as a preserved existing override).
            ' EDID uniqueness guard: dedup each draft's final namespaced EditorID against the LVLI/LVLN already
            ' bound for this plugin (preserved existing + earlier drafts), auto-suffixing _2/_3 on collision.
            Dim usedLeveledEdids As New HashSet(Of String)(leveledEntries.Select(Function(l) l.EditorID), StringComparer.OrdinalIgnoreCase)
            For Each fid In needed
                Dim d = draftByFid(fid)
                ' OVERRIDE draft (re-edit of an existing LVLI): keep its real FormID + EDID verbatim. An UNCHANGED
                ' override (pulled in by a reference but not itself edited) is skipped — its FormID resolves to the
                ' record it overrides (the master original, or this plugin's copy preserved in Phase 2a), so no
                ' reference dangles. A dirty override REPLACES the preserved Phase 2a copy in place (keeping the
                ' source's OBND/LLKC/LVLG/LVSG/ONAM/VCS, only the edited LVLD/LVLM/LVLF + LVLO entries change); when
                ' the target is a vanilla/master LVLI not yet in this plugin (no preserved copy), a full override
                ' entry is built from the source record so those non-owned subrecords still survive. Mirror of the
                ' OTFT override handling (Phase 2c) + the ARMO "unchanged override → don't emit" gate (Phase 2e).
                If d.IsOverride Then
                    If Not d.IsDirty Then Continue For
                    If Not alreadyLeveled.Add(fid) Then
                        Dim preserved = leveledEntries.FirstOrDefault(Function(x) x.FormID = fid)
                        If preserved IsNot Nothing Then
                            preserved.ChanceNone = d.ChanceNone
                            preserved.MaxCount = d.MaxCount
                            preserved.Flags = d.FlagsByte()
                            preserved.IsOverride = True
                            preserved.Entries.Clear()
                            For Each e In d.Entries
                                If e.RefFormID <> 0UI Then preserved.Entries.Add(New SaveNpcEspWriter.LvliEntryData With {
                                    .Level = e.Level, .RefFormID = e.RefFormID, .Count = e.Count, .ChanceNone = e.ChanceNone})
                            Next
                        End If
                    Else
                        leveledEntries.Add(BuildLvliOverrideEntryFromSource(d, ctx))
                    End If
                    usedLeveledEdids.Add(d.EditorID)
                    Continue For
                End If
                If Not alreadyLeveled.Add(fid) Then Continue For
                Dim desiredLvliEdid = ApplyEspNamespaceToEditorId(d.EditorID, espNameNoExt)
                Dim finalLvliEdid = MakeUniqueEditorId(desiredLvliEdid, usedLeveledEdids)
                If Not String.Equals(finalLvliEdid, desiredLvliEdid, StringComparison.Ordinal) Then
                    Logger.LogLazy(Function() $"[SAVE] LVLI EditorID '{desiredLvliEdid}' already used in {IO.Path.GetFileName(target.TargetPath)} → renamed to '{finalLvliEdid}' (FormID unchanged).")
                End If
                Dim le As New SaveNpcEspWriter.LvliRecordEntry With {
                    .FormID = d.FormID,
                    .EditorID = finalLvliEdid,
                    .ChanceNone = d.ChanceNone,
                    .MaxCount = d.MaxCount,
                    .Flags = d.FlagsByte()
                }
                For Each e In d.Entries
                    If e.RefFormID = 0UI Then Continue For
                    le.Entries.Add(New SaveNpcEspWriter.LvliEntryData With {
                        .Level = e.Level, .RefFormID = e.RefFormID, .Count = e.Count, .ChanceNone = e.ChanceNone
                    })
                Next
                leveledEntries.Add(le)
            Next
        End If

        ' Phases 2e/2f/2g: author-built ARMA/ARMO/MSWP drafts needed by this save. Emit the TRANSITIVE
        ' CLOSURE of the armor dependency graph so a saved outfit/skin is self-contained and never writes a
        ' dangling 0xFF provisional reference. The walk MIRRORS Phase 2d (Queue+HashSet, cycle-safe) but over
        ' a three-record-type graph instead of LVLI→LVLI:
        '
        '   ARMO  --ArmorAddons[].ArmaFormID-->  ARMA  --{Male,Female}MaterialSwapFormID-->  MSWP
        '     \--{Male,Female}MaterialSwapFormID--------------------------------------------/
        '
        ' Seed for neededArmo (the ROOTS) = every ARMO **draft** FormID referenced by an emitted OTFT's items
        ' OR by an emitted leveled-list entry OR by a saved NPC's WNAM skin override (entry.Npc.SkinFormID,
        ' which already carries the overlay's SkinFormIDOverride), PLUS every dirty standalone ARMO draft that
        ' is ALSO referenced (mirror of Phase 2c's "skip clean AND not referenced" rule — a dirty-but-unreferenced
        ' ARMO draft is NOT pulled in, matching the OTFT rule which only persists drafts that are dirty OR
        ' referenced; here we additionally require referenced so an orphan ARMO never bloats the plugin). The
        ' writer pre-assigns every draft (OTFT/LVLI/ARMO/ARMA/MSWP) its real self-index FormID, so cross-refs
        ' resolve regardless of emit order.
        '
        ' EDID uniqueness: every emitted record kind (OTFT/LVLI here too) shares one used-EDID set per kind, but
        ' ARMO/ARMA/MSWP are distinct namespaces in xEdit (keyed by signature), so each gets its own set. Each set
        ' is SEEDED from the preserved-existing OVERRIDE entries of that kind (Phase 2a re-emits them) so a NEW
        ' draft's namespaced EDID can't collide with a preserved record. Override drafts keep their EDID verbatim.
        If target.SaveNewOutfits Then
            ' Index every draft kind by FormID for O(1) closure lookups.
            Dim armoByFid As New Dictionary(Of UInteger, ArmoDraft)
            If ctx.ArmoDrafts IsNot Nothing Then
                For Each d In ctx.ArmoDrafts
                    If d IsNot Nothing Then armoByFid(d.FormID) = d
                Next
            End If
            Dim armaByFid As New Dictionary(Of UInteger, ArmaDraft)
            If ctx.ArmaDrafts IsNot Nothing Then
                For Each d In ctx.ArmaDrafts
                    If d IsNot Nothing Then armaByFid(d.FormID) = d
                Next
            End If
            Dim mswpByFid As New Dictionary(Of UInteger, MswpDraft)
            If ctx.MswpDrafts IsNot Nothing Then
                For Each d In ctx.MswpDrafts
                    If d IsNot Nothing Then mswpByFid(d.FormID) = d
                Next
            End If

            ' Only do the walk when at least one of the three kinds has drafts.
            If armoByFid.Count > 0 OrElse armaByFid.Count > 0 OrElse mswpByFid.Count > 0 Then
                Dim neededArmo As New HashSet(Of UInteger)
                Dim neededArma As New HashSet(Of UInteger)
                Dim neededMswp As New HashSet(Of UInteger)
                Dim armoToVisit As New Queue(Of UInteger)

                ' Phase-2a preserved FormIDs (existing target-plugin ARMO/ARMA/MSWP already in the entry lists) — a
                ' draft sharing one of these FormIDs REPLACES the preserved copy at emit time (dedup, lines ~717+).
                Dim armoAlreadyEmitted As New HashSet(Of UInteger)(armoEntries.Select(Function(x) x.FormID))
                Dim armaAlreadyEmitted As New HashSet(Of UInteger)(armaEntries.Select(Function(x) x.FormID))
                Dim mswpAlreadyEmitted As New HashSet(Of UInteger)(mswpEntries.Select(Function(x) x.FormID))
                ' Emit EVERY authored draft (new + override, referenced or not). Since the user now has Delete/Revert
                ' to remove records they don't want, an unreferenced NEW record is KEPT rather than dropped — a
                ' record can legitimately exist in the plugin without anything pointing at it yet (the user's rule
                ' "if they're saved they may just not be referenced"). Gated only on IsDirty (all NEW drafts are
                ' dirty; a modified OVERRIDE is dirty; an untouched override never gets registered). The reference
                ' seeds below (OTFT/leveled/skin) + the walk stay — they're now redundant but harmless (Add no-ops).
                For Each d In armoByFid.Values
                    If d.IsDirty AndAlso neededArmo.Add(d.FormID) Then armoToVisit.Enqueue(d.FormID)
                Next
                For Each d In armaByFid.Values
                    If d.IsDirty Then neededArma.Add(d.FormID)
                Next
                For Each d In mswpByFid.Values
                    If d.IsDirty Then neededMswp.Add(d.FormID)
                Next

                ' --- Seed neededArmo: ARMO drafts referenced by an emitted OTFT's items. ---
                For Each oe In outfitEntries
                    For Each fid In oe.ItemArmoFormIDs
                        If armoByFid.ContainsKey(fid) AndAlso neededArmo.Add(fid) Then armoToVisit.Enqueue(fid)
                    Next
                Next
                ' --- Seed neededArmo: ARMO drafts referenced by an emitted leveled-list entry. ---
                For Each le In leveledEntries
                    For Each e In le.Entries
                        If armoByFid.ContainsKey(e.RefFormID) AndAlso neededArmo.Add(e.RefFormID) Then armoToVisit.Enqueue(e.RefFormID)
                    Next
                Next
                ' --- Seed neededArmo: ARMO drafts referenced by a saved NPC's WNAM skin override. The post-overlay
                ' entry.Npc.SkinFormID already reflects the preset's SkinFormIDOverride, so a draft skin assigned to
                ' an NPC in this batch is pulled in (self-contained skin). ---
                For Each entry In entries
                    If entry.Npc Is Nothing Then Continue For
                    Dim skinFid = entry.Npc.SkinFormID
                    If armoByFid.ContainsKey(skinFid) AndAlso neededArmo.Add(skinFid) Then armoToVisit.Enqueue(skinFid)
                Next
                ' --- NEW standalone ARMO drafts: only emitted if REFERENCED (an emitted outfit/leveled/skin points
                ' at them — the three seeds above test exactly that). An unreferenced NEW draft is dropped so a
                ' brand-new orphan record doesn't bloat the plugin. (OVERRIDE drafts are already handled above by
                ' the `Not d.IsNew` seed and are always emitted.) So nothing extra to add here. ---

                ' --- Walk: each needed ARMO contributes its ARMA draft refs (ArmorAddons) to neededArma and its
                ' MSWP draft refs (ARMO-level material swaps) to neededMswp. Cycle-safe via the visited sets. ---
                While armoToVisit.Count > 0
                    Dim fid = armoToVisit.Dequeue()
                    Dim d = armoByFid(fid)
                    For Each addon In d.ArmorAddons
                        If armaByFid.ContainsKey(addon.ArmaFormID) Then neededArma.Add(addon.ArmaFormID)
                    Next
                    If mswpByFid.ContainsKey(d.MaleMaterialSwapFormID) Then neededMswp.Add(d.MaleMaterialSwapFormID)
                    If mswpByFid.ContainsKey(d.FemaleMaterialSwapFormID) Then neededMswp.Add(d.FemaleMaterialSwapFormID)
                    ' ARMO drafts only reference ARMA/MSWP (terminal record kinds for this graph) — no ARMO→ARMO
                    ' edge exists, so the queue drains without re-enqueuing ARMOs.
                End While

                ' --- From each needed ARMA, collect its MSWP draft refs into neededMswp (ARMA is the only kind that
                ' can pull additional MSWPs beyond what the ARMOs already pulled). ARMA has no draft→draft edge. ---
                For Each fid In neededArma
                    Dim d = armaByFid(fid)
                    If mswpByFid.ContainsKey(d.MaleMaterialSwapFormID) Then neededMswp.Add(d.MaleMaterialSwapFormID)
                    If mswpByFid.ContainsKey(d.FemaleMaterialSwapFormID) Then neededMswp.Add(d.FemaleMaterialSwapFormID)
                Next

                ' EDID/FormID dedup against the preserved-existing OVERRIDE entries already in each list
                ' (Phase 2a may have re-emitted ARMO/ARMA/MSWP records of the target plugin). Mirror of the
                ' OTFT dedup (Phase 2c):
                '   • Seed usedEdids from the EDITORIDs already bound, so a NEW draft's namespaced EDID doesn't
                '     collide with a preserved record's EDID (auto-suffix _2/_3 on collision).
                '   • A DRAFT whose FormID already exists in the list is an OVERRIDE re-edit of a record that was
                '     ALSO preserved — the DRAFT wins (newer user edit): remove the pre-existing entry first.
                '     Provisional NEW-draft FormIDs (0xFF…) never collide with the real ones in 'alreadyEmitted'.

                ' An UNCHANGED OVERRIDE draft (referenced by a dirty parent via the walk above, but not itself
                ' edited) must NOT be emitted: its FormID is real and resolves to the record it overrides (the
                ' master original, or this plugin's own copy preserved in Phase 2a), so a reference never dangles.
                ' Emitting it would just re-write an identical override. NEW drafts are always dirty (IsNew) so they
                ' are never skipped. Mirror of the ArmA/ArmO/MSWP editor "dirty only on real change" gate.
                ' --- Phase 2e: build ArmoRecordEntry for each needed ARMO draft. ---
                Dim usedArmoEdids As New HashSet(Of String)(armoEntries.Select(Function(x) x.EditorID), StringComparer.OrdinalIgnoreCase)
                For Each fid In neededArmo
                    Dim d = armoByFid(fid)
                    If d.IsOverride AndAlso Not d.IsDirty Then Continue For
                    If armoAlreadyEmitted.Contains(d.FormID) Then armoEntries.RemoveAll(Function(x) x.FormID = d.FormID)
                    armoEntries.Add(BuildArmoEntry(d, ctx, espNameNoExt, usedArmoEdids, target))
                Next
                ' --- Phase 2f: build ArmaRecordEntry for each needed ARMA draft. ---
                Dim usedArmaEdids As New HashSet(Of String)(armaEntries.Select(Function(x) x.EditorID), StringComparer.OrdinalIgnoreCase)
                For Each fid In neededArma
                    Dim d = armaByFid(fid)
                    If d.IsOverride AndAlso Not d.IsDirty Then Continue For
                    If armaAlreadyEmitted.Contains(d.FormID) Then armaEntries.RemoveAll(Function(x) x.FormID = d.FormID)
                    armaEntries.Add(BuildArmaEntry(d, ctx, espNameNoExt, usedArmaEdids, target))
                Next
                ' --- Phase 2g: build MswpRecordEntry for each needed MSWP draft. ---
                Dim usedMswpEdids As New HashSet(Of String)(mswpEntries.Select(Function(x) x.EditorID), StringComparer.OrdinalIgnoreCase)
                For Each fid In neededMswp
                    Dim d = mswpByFid(fid)
                    If d.IsOverride AndAlso Not d.IsDirty Then Continue For
                    If mswpAlreadyEmitted.Contains(d.FormID) Then mswpEntries.RemoveAll(Function(x) x.FormID = d.FormID)
                    mswpEntries.Add(BuildMswpEntry(d, ctx, espNameNoExt, usedMswpEdids, target))
                Next
            End If
        End If

        ' Phase 2h: add the saved NPCs to a Leveled NPC list (LVLN) when requested. Each saved NPC's
        ' GLOBAL FormID (inputs(i).NpcFormID is global — GetRecord-keyed) becomes one LVLO entry. We pass
        ' GLOBAL FormIDs and 0xFF provisional sentinels ONLY — the writer's remapper/draftRemap does ALL
        ' master/high-byte/ESL mapping; nothing here computes a high byte (same contract as the LVLI path).
        ' Standard "pick one" semantics (LVLF=0). On overflow (>255, the LLCT u8 cap) the list is split into
        ' FLAT siblings: the first keeps the base name, the rest get _1, _2, … (no parent — chosen topology).
        If target.AddToLvlList Then
            BuildLeveledNpcListEntries(target, writeInputs, ctx, leveledEntries)
        End If

        ' Phase 3: write the plugin (all entries in one pass).
        ReportPhase(progress, "Writing NPC override to plugin…", IO.Path.GetFileName(target.TargetPath))
        Dim game = Config_App.Current.Game
        Dim writeRes = SaveNpcEspWriter.SaveOverridePlugin(
            target.TargetPath, game, target.MarkAsMaster, target.LightMaster,
            entries, existingRecords, existingMasters, ctx.PluginManager, outfitEntries, leveledEntries,
            existingNextObjectId,
            armoEntries:=armoEntries, armaEntries:=armaEntries, mswpEntries:=mswpEntries)

        result.WriterResult = writeRes
        result.DraftFormIdMap = writeRes.DraftFormIdMap
        For Each existingRec In existingRecords
            result.SavedFormIDs.Add(existingRec.Header.FormID)
        Next
        For Each npcInput In writeInputs
            result.SavedFormIDs.Add(npcInput.NpcFormID)
            result.WrittenNpcFormIDs.Add(npcInput.NpcFormID)
        Next

        ' Phase 3b: refresh + PRUNE the BodyMorphs/Skin sidecar (default ON). Read once (preserving every other
        ' NPC), merge the saved NPCs, drop the mark-to-delete NPCs, write once. The BodyGen emitter consumes the
        ' post-merge dict so its .ini reflects all NPCs of the plugin. Runs for body-write saves AND for a
        ' delete-only save that must strip a removed NPC already present in the sidecar/.ini — WITHOUT creating a
        ' sidecar or .ini when nothing existed.
        Dim wantBodyWork = target.WriteBssliders OrElse target.EmitBodyGen
        Dim haveRemovals = ctx.RecordsToRemove IsNot Nothing AndAlso ctx.RecordsToRemove.Count > 0
        If wantBodyWork OrElse haveRemovals Then
            Dim sidecarPath = BssliderSidecar.BuildPath(target.TargetPath)
            Dim existingSidecar = BssliderSidecar.Read(sidecarPath)
            Dim sidecarExisted = existingSidecar IsNot Nothing
            Dim mergedSidecar = If(existingSidecar, New BssliderSidecar.SidecarFile())
            mergedSidecar.Plugin = IO.Path.GetFileName(target.TargetPath)

            ' Merge the saved NPCs only when the user asked to persist body data (writeInputs already excludes the
            ' removed ones). entries are built in writeInputs order in Phase 1, so entries(i) ↔ writeInputs(i).
            If wantBodyWork Then
                For i = 0 To writeInputs.Count - 1
                    MergeOneNpcIntoSidecar(mergedSidecar, writeInputs(i).NpcFormID, entries(i).Npc, ctx)
                Next
            End If

            ' Mark-to-delete: drop each removed NPC from the sidecar (and thus the BodyGen .ini). Identifier =
            ' BuildIdentifier(origin master, FormID) — the exact key MergeOneNpcIntoSidecar used. Preserves all
            ' other NPCs. GetOriginatingPluginName resolves here (in-save, before MainForm's post-save revert).
            Dim removedFromSidecar As Boolean = False
            If haveRemovals Then
                For Each remFid In ctx.RecordsToRemove
                    Dim ident = BssliderSidecar.BuildIdentifier(ctx.PluginManager.GetOriginatingPluginName(remFid), remFid)
                    If mergedSidecar.Npcs.Remove(ident) Then removedFromSidecar = True
                Next
            End If

            ' Write the sidecar when the user asked OR a removal changed a sidecar that ALREADY existed (never
            ' create a fresh sidecar just to prune an entry that wasn't there).
            If target.WriteBssliders OrElse (removedFromSidecar AndAlso sidecarExisted) Then
                ReportPhase(progress, "Writing .bssliders sidecar…", IO.Path.GetFileName(target.TargetPath))
                BssliderSidecar.Write(sidecarPath, mergedSidecar)
            End If

            ' Re-emit BodyGen when the user asked OR a removal must drop the NPC from an EXISTING .ini (Emit
            ' rewrites without the removed NPC, or wipes the .ini when it was the last one). Never creates one.
            ' BodyGen folder = the plugin's modInfo->name, which INCLUDES the extension: both f4ee
            ' (BodyGenInterface.cpp:534, GetLoadedModIndex("Name.esp")) and skee64 (BodyMorphInterface.cpp:132)
            ' look up BodyGen\<Name.esp>\. GetFileName keeps the extension; GetFileNameWithoutExtension (old)
            ' wrote to a folder the engine never scans, so BodyGen never applied in-game (both games).
            Dim iniBaseName = IO.Path.GetFileName(target.TargetPath)
            Dim isSseSave As Boolean = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
            Dim iniExists As Boolean = If(isSseSave,
                                          SseBodyGenIniWriter.IniExists(ctx.DataPath, iniBaseName),
                                          BodyGenIniWriter.IniExists(ctx.DataPath, iniBaseName))
            If target.EmitBodyGen OrElse (removedFromSidecar AndAlso iniExists) Then
                ReportPhase(progress, "Writing BodyGen .ini…", IO.Path.GetFileName(target.TargetPath))
                EmitBodyGenFromSidecar(target, mergedSidecar, ctx)
            End If
        End If

        result.VerifierIcon = MessageBoxIcon.Information
        result.ChargenSuccess = True
    End Sub

    ''' <summary>Build a single NPC override entry: apply the overlay onto the raw parse, copy
    ''' round-trip-only fields, detect a user MWGT edit from the overlay, rebuild HeadParts (raw
    ''' PNAM ∪ preset, deduped by PartType), and resolve the outfit (DOFT) draft fallback. Pure —
    ''' no IO. Shared by every NPC in a batch.</summary>
    Private Function BuildOverrideEntry(npcInput As NpcSaveInput,
                                        ctx As SaveContext,
                                        target As SaveEsp_Form.SaveTarget) As SaveNpcEspWriter.NpcOverrideEntry
        Dim npcFormID = npcInput.NpcFormID
        Dim rawNpcSpec = npcInput.RawNpcSpec

        ' Phase 1a: apply overlay + copy round-trip-only fields.
        Dim npcSpec = ctx.ApplyPresetOverlayToNpcData(rawNpcSpec, npcFormID)
        If Not ReferenceEquals(npcSpec, rawNpcSpec) Then
            ctx.CopyRoundTripOnlyFieldsFromRaw(rawNpcSpec, npcSpec)
            ctx.SyncParallelCollectionsAfterOverlay(npcSpec)
        End If

        ' Phase 1a': apply the NPC-record scalar/list override (NPC Editor) ON TOP of the round-trip copy, so
        ' the user's Name/ACBS/identity/keyword/faction/inventory/OBTS edits win over the source record without
        ' the round-trip copy above being altered. No-op when no override was authored for this NPC.
        ctx.ApplyNpcRecordOverride?.Invoke(npcSpec, npcFormID)

        ' Reconcile the IsCharGenFacePreset overlay edit into the ACBS struct the writer emits.
        ' ApplyPresetOverlayToNpcData sets only the AcbsFlags mirror; EmitAcbs writes Acbs.Flags, and
        ' CopyRoundTripOnlyFieldsFromRaw copies Acbs from the raw parse BY REFERENCE — so without this
        ' the edited bit never reaches the ESP. Clone before mutating so the shared raw parse isn't
        ' corrupted. (No-op when the two already agree, i.e. no IsCharGenFacePreset override.)
        If npcSpec.Acbs IsNot Nothing AndAlso npcSpec.Acbs.Flags <> npcSpec.AcbsFlags Then
            npcSpec.Acbs = CloneAcbsWithFlags(npcSpec.Acbs, npcSpec.AcbsFlags)
        End If

        ' When this save bakes CharGen AND the user asked to drop the CharGen flag, clear ACBS bit 0x04
        ' on the written override so the engine loads the baked FaceGen instead of reconstructing the
        ' face at runtime (CK skips FaceGen export for CharGen-preset NPCs). No-op for NPCs that don't
        ' carry the bit. Clone the Acbs before mutating so the shared raw parse isn't corrupted.
        If target.GenerateChargen AndAlso target.RemoveCharGenFlag Then
            Dim strippedFlags As UInteger = npcSpec.AcbsFlags And Not AcbsBitIsCharGenFacePreset
            If strippedFlags <> npcSpec.AcbsFlags Then
                npcSpec.AcbsFlags = strippedFlags
                If npcSpec.Acbs IsNot Nothing Then
                    npcSpec.Acbs = CloneAcbsWithFlags(npcSpec.Acbs, strippedFlags)
                End If
            End If
        End If

        Dim overlay As LooksmenuLoader.LooksmenuPreset = Nothing
        ctx.AppliedPresets.TryGetValue(npcFormID, overlay)

        ' Phase 1b: detect a user MWGT edit from the OVERLAY (not the live render dual-cache, so the
        ' check works for non-loaded NPCs too). EditBody's ApplyMwgt writes overlay.WeightX on every
        ' weight drag, so a HasValue weight that differs from the raw record by >eps is a real edit.
        ' Guard on raw HasValue mirrors the single-NPC path: an NPC whose raw weight is the
        ' Single.MaxValue sentinel ("inherit from race") is left as-is rather than baked to a literal.
        Dim mwgtUserEdited As Boolean = False
        If overlay IsNot Nothing AndAlso
           overlay.WeightThin.HasValue AndAlso overlay.WeightMuscular.HasValue AndAlso overlay.WeightFat.HasValue AndAlso
           rawNpcSpec.WeightThin.HasValue AndAlso rawNpcSpec.WeightMuscular.HasValue AndAlso rawNpcSpec.WeightFat.HasValue Then
            Const eps As Single = 0.0001F
            mwgtUserEdited = (Math.Abs(overlay.WeightThin.Value - rawNpcSpec.WeightThin.Value) > eps) OrElse
                             (Math.Abs(overlay.WeightMuscular.Value - rawNpcSpec.WeightMuscular.Value) > eps) OrElse
                             (Math.Abs(overlay.WeightFat.Value - rawNpcSpec.WeightFat.Value) > eps)
        End If
        If mwgtUserEdited Then
            npcSpec.WeightThin = overlay.WeightThin.Value
            npcSpec.WeightMuscular = overlay.WeightMuscular.Value
            npcSpec.WeightFat = overlay.WeightFat.Value
            Using ms As New MemoryStream()
                Using bw As New BinaryWriter(ms)
                    bw.Write(overlay.WeightThin.Value)
                    bw.Write(overlay.WeightMuscular.Value)
                    bw.Write(overlay.WeightFat.Value)
                End Using
                npcSpec.MwgtRaw = ms.ToArray()
            End Using
            npcSpec.HasMwgt = True
        End If

        ' Phase 1c: rebuild HeadPartFormIDs, dedup main types (1-9) by PartType. For a FILTERED preset
        ' the source is raw NPC PNAM ∪ preset (union restores IsExtraPart addons the preset dropped);
        ' for a COMPLETE superset preset (Edit Face) the preset alone is authoritative (see below).
        ' Snapshot the raw head parts FIRST. When no overlay is applied, ApplyPresetOverlayToNpcData returns
        ' the SAME instance (npcSpec IS rawNpcSpec), so clearing npcSpec.HeadPartFormIDs would also empty
        ' rawNpcSpec.HeadPartFormIDs — and the rebuild below would then read an empty list, WIPING every head
        ' part on a no-op re-save (the "save again with no changes → parts lost" bug). The snapshot is a
        ' separate list, so the clear can't cannibalize the source.
        Dim rawHeadParts As New List(Of UInteger)(rawNpcSpec.HeadPartFormIDs)
        npcSpec.HeadPartFormIDs.Clear()
        Dim presetHasHeadParts = (overlay IsNot Nothing AndAlso overlay.HasHeadPartFormIDs)
        If presetHasHeadParts Then
            Dim presetParts = overlay.HeadPartFormIDs
            Dim mergedByType As New Dictionary(Of Integer, UInteger)
            Dim freestandingMisc As New List(Of UInteger)
            ' Per-FormID classification → PartType slot (1-9) or freestanding misc (0).
            ' skipExtra drops IsExtraPart HDPTs (DATA flag 0x08, xEdit wbDefinitionsFO4.pas:7369 — lashes,
            ' hairlines, AO/wet meshes that hang off another head part via HNAM). Applied to the PRESET
            ' loop ONLY (its long-standing behaviour). The RAW loop keeps extras verbatim:
            ' an empirical scan of the live load order (Tools/ExtraPartFilterProbe, 4473 NPCs) found ZERO
            ' extra-parts with a 1-9 PartType — so a raw extra can never displace a main slot, the only
            ' case a raw guard would help — while filtering the raw loop stripped FemaleEyesHumanLashes &
            ' co. from 2147 NPCs, 43 of them (incl. CompanionCait) with NO retained HNAM parent to
            ' regenerate the part → lost eyelashes on save. So we preserve the raw record's head parts.
            Dim classifyHeadPart =
                Sub(fid As UInteger, skipExtra As Boolean)
                    If fid = 0UI Then Return
                    Dim hpRec = ctx.PluginManager.GetRecord(fid)
                    If hpRec Is Nothing OrElse hpRec.Header.Signature <> "HDPT" Then Return
                    Dim hd = RecordParsers.ParseHDPT(hpRec, ctx.PluginManager)
                    ' IsExtraPart flag = 0x08; same value used by MainForm.HeadPartFlagIsExtra.
                    If skipExtra AndAlso (hd.Flags And 8US) <> 0 Then Return
                    If hd.PartType = 0 Then
                        freestandingMisc.Add(fid)
                    ElseIf hd.PartType >= 1 AndAlso hd.PartType <= 9 Then
                        mergedByType(hd.PartType) = fid
                    End If
                End Sub
            ' A COMPLETE superset preset (Edit Face — seeded from the raw record's PNAM including its
            ' IsExtraPart addons, then edited) is AUTHORITATIVE: it already carries every raw extra it
            ' means to keep, so we must NOT union the raw record back in. Doing so would (a) resurrect
            ' freestanding Misc parts the user explicitly deleted — the orphan-hairline bug — because a
            ' raw Misc has no PartType slot to be overridden and always re-accumulates into
            ' freestandingMisc, and (b) duplicate any Misc present in both lists (freestandingMisc is
            ' not deduped). Filtered presets (LooksMenu JSON / SavePreset / Paste) DROP IsExtraPart
            ' addons, so they still need the raw union to restore lashes/AO/wet the preset omitted.
            Dim presetIsCompleteSuperset As Boolean = overlay.HeadPartFormIDsIncludeRawExtras
            If Not presetIsCompleteSuperset Then
                ' Skip raw parts the APPLY step flagged as orphaned by a parent replacement (an old
                ' hairline / eye-lash left over after a preset/paste swapped that parent). Decided at Load/Paste
                ' (HeadPartResolver.ComputeReplacedParentOrphanMisc → overlay.SuppressedRawHeadPartFormIDs);
                ' the saver just obeys it. Empty set for any apply that didn't replace a parent.
                Dim suppressedRaw = overlay.SuppressedRawHeadPartFormIDs
                For Each fid In rawHeadParts
                    If suppressedRaw IsNot Nothing AndAlso suppressedRaw.Contains(fid) Then Continue For
                    classifyHeadPart(fid, False)   ' raw: keep extras (round-trip faithful)
                Next
            End If
            For Each fid In presetParts
                ' Complete superset already holds raw extras verbatim → keep them (skipExtra:=False).
                ' Filtered preset keeps its long-standing extra filter (the raw union above restores them).
                classifyHeadPart(fid, Not presetIsCompleteSuperset)
            Next
            For Each t In mergedByType.Keys.OrderBy(Function(k) k)
                npcSpec.HeadPartFormIDs.Add(mergedByType(t))
            Next
            npcSpec.HeadPartFormIDs.AddRange(freestandingMisc)
        Else
            npcSpec.HeadPartFormIDs.AddRange(rawHeadParts)
        End If

        ' Phase 1d: outfit (DOFT) draft fallback. When the user is NOT saving new outfits and this
        ' NPC's DOFT points at an unsaved draft (provisional FormID), revert it to the original
        ' record outfit (the user's rule). Draft EMISSION (the ON case) is handled once per batch in
        ' ExecuteWritePhases Phase 2c. A DOFT pointing at a real OTFT is kept either way.
        If Not target.SaveNewOutfits AndAlso OutfitDraft.IsDraftFormID(npcSpec.DefaultOutfitFormID) Then
            npcSpec.DefaultOutfitFormID = rawNpcSpec.DefaultOutfitFormID
            npcSpec.HasDefaultOutfit = rawNpcSpec.HasDefaultOutfit
        End If

        ' Phase 1e: skin (WNAM) draft fallback — the exact mirror of 1d for the NPC's skin ARMO. When the user
        ' is NOT saving new records and this NPC's skin points at an unsaved draft ARMO (provisional 0xFF FormID),
        ' the draft is never emitted (Phase 2e skin closure is SaveNewOutfits-gated), so revert WNAM to the
        ' original record's skin. Without this, NPC_.WNAM would be written as a DANGLING 0xFF… reference and the
        ' custom skin armor would be absent from the plugin. Draft EMISSION (the ON case) is Phase 2e.
        If Not target.SaveNewOutfits AndAlso OutfitDraft.IsDraftFormID(npcSpec.SkinFormID) Then
            npcSpec.SkinFormID = rawNpcSpec.SkinFormID
            npcSpec.HasSkin = rawNpcSpec.HasSkin
        End If

        Return New SaveNpcEspWriter.NpcOverrideEntry With {
            .Npc = npcSpec,
            .SourcePluginName = npcInput.SourcePluginName,
            .OriginalHeader = npcInput.RawRecord.Header
        }
    End Function

    ''' <summary>Shallow copy of an <see cref="NPC_AcbsData"/> with an overridden Flags value. Used to
    ''' apply the IsCharGenFacePreset overlay edit without mutating the raw parse's shared Acbs instance.
    ''' Byte-array fields (Unknown18/TrailingBytes) are emit-only, so sharing the references is safe.</summary>
    Private Function CloneAcbsWithFlags(src As NPC_AcbsData, flags As UInteger) As NPC_AcbsData
        Return New NPC_AcbsData With {
            .Flags = flags,
            .XpValueOffset = src.XpValueOffset,
            .LevelOrLevelMult = src.LevelOrLevelMult,
            .CalcMinLevel = src.CalcMinLevel,
            .CalcMaxLevel = src.CalcMaxLevel,
            .DispositionBase = src.DispositionBase,
            .TemplateFlags = src.TemplateFlags,
            .BleedoutOverride = src.BleedoutOverride,
            .Unknown18 = src.Unknown18,
            .TrailingBytes = src.TrailingBytes
        }
    End Function

    ''' <summary>Translate a global FormID to the local FormID the target plugin's MAST list sees,
    ''' so existing-record load can identify the records being replaced. Mirror of the engine FileID
    ''' scheme (12-bit object for an ESL source, 24-bit for a full source).</summary>
    ''' <summary>Working EditorID prefix (type segment) for Leveled-NPC lists authored by the Save dialog's
    ''' "Add to LVL list" feature: <c>npcm_LVLN_&lt;name&gt;</c>. At save the destination plugin name is
    ''' injected via <see cref="ApplyEspNamespaceToEditorId"/> → final <c>npcm_&lt;ESPNAME&gt;_LVLN_&lt;name&gt;</c>.
    ''' Mirror of <see cref="OutfitDraft.EditorIdPrefix"/> / <see cref="LeveledListDraft.EditorIdPrefix"/>.</summary>
    Public Const LeveledNpcListEditorIdPrefix As String = "npcm_LVLN_"

    ''' <summary>ACBS Flags bit 0x04 = "Is CharGen Face Preset" (NPC_AcbsData.IsCharGenFacePreset,
    ''' RecordParsers.vb:134). Cleared from saved overrides when the user bakes CharGen and ticks
    ''' "Remove CharGen flag", so the engine loads the baked FaceGen instead of reconstructing the
    ''' face at runtime.</summary>
    Private Const AcbsBitIsCharGenFacePreset As UInteger = &H4UI

    ''' <summary>LLCT is itU8 (wbDefinitionsFO4.pas:3674) → a leveled list holds at most 255 entries.</summary>
    Private Const LeveledListEntryCap As Integer = 255

    ''' <summary>Sanitize a string into a clean EditorID segment: ASCII letters/digits/underscore only
    ''' (every other run collapses to a single '_'; leading/trailing '_' trimmed). Used for the
    ''' &lt;ESPNAME&gt; segment so a plugin filename with spaces/dashes still yields a valid EditorID.</summary>
    Public Function SanitizeEditorIdSegment(s As String) As String
        If String.IsNullOrEmpty(s) Then Return "esp"
        Dim sb As New System.Text.StringBuilder(s.Length)
        Dim lastUnderscore As Boolean = False
        For Each c In s
            If (c >= "a"c AndAlso c <= "z"c) OrElse (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "0"c AndAlso c <= "9"c) Then
                sb.Append(c)
                lastUnderscore = False
            ElseIf Not lastUnderscore Then
                sb.Append("_"c)
                lastUnderscore = True
            End If
        Next
        Dim r = sb.ToString().Trim("_"c)
        Return If(r.Length = 0, "esp", r)
    End Function

    ''' <summary>Namespace an author-built record's EditorID with the destination plugin name, the standardized
    ''' convention for ALL author records (OTFT/LVLI/LVLN): <c>npcm_&lt;TYPE&gt;_&lt;name&gt;</c> →
    ''' <c>npcm_&lt;ESPNAME&gt;_&lt;TYPE&gt;_&lt;name&gt;</c>. The plugin name is known only at save, so this runs
    ''' there. Only EditorIDs that start with the author prefix <c>npcm_</c> are rewritten — overrides of
    ''' pre-existing records (whatever their EditorID) and already-saved records pass through unchanged, which
    ''' keeps re-saves idempotent. Caller must NOT apply this to override entries.</summary>
    Public Function ApplyEspNamespaceToEditorId(edid As String, espNameNoExt As String) As String
        If String.IsNullOrEmpty(edid) OrElse Not edid.StartsWith("npcm_", StringComparison.Ordinal) Then Return edid
        Return "npcm_" & SanitizeEditorIdSegment(espNameNoExt) & "_" & edid.Substring("npcm_".Length)
    End Function

    ''' <summary>Return an EditorID unique within <paramref name="used"/>: the desired value if free, else
    ''' <c>desired_2</c>, <c>desired_3</c>, … Adds the chosen value to the set. Guards against a DUPLICATE
    ''' EditorID landing in one plugin — the realistic case is a cross-session re-creation of the same
    ''' outfit/list name into the same target esp, where the final namespaced EditorID would otherwise equal
    ''' a previously-saved record's. The numeric suffix mirrors the plugin auto-suffix (_2/_3) and the LVLN
    ''' overflow convention. The rename is COSMETIC: FO4 keys records by FormID, so identity and all
    ''' references are unaffected — the suffix only keeps xEdit's EDID namespace clean.</summary>
    Private Function MakeUniqueEditorId(desired As String, used As HashSet(Of String)) As String
        If used.Add(desired) Then Return desired
        Dim n As Integer = 2
        Dim candidate As String
        Do
            candidate = $"{desired}_{n}"
            n += 1
        Loop While Not used.Add(candidate)
        Return candidate
    End Function

    ''' <summary>Resolve a draft's final EditorID + (for OVERRIDE) its parsed SourceRecord/VCS. Shared by the
    ''' three armor draft builders so the NEW-vs-OVERRIDE contract is identical to the OTFT draft path (Phase 2c):
    '''   • NEW  → namespaced EDID (ApplyEspNamespaceToEditorId) made unique within <paramref name="usedEdids"/>.
    '''   • OVERRIDE → EDID kept verbatim (targets a real record by FormID), but still seeded into the set so a
    '''     later NEW draft of the same kind can't collide with it. SourceRecord = GetRecord(d.FormID) (the draft's
    '''     FormID IS the real GLOBAL FormID for overrides — same key/value the OTFT override path resolves), and
    '''     OriginalVcs1/2 come from its header. Throws if the source record can't be found / is the wrong type
    '''     (a stale override target must fail loud, never silently emit a NEW record with a 0xFF FormID).</summary>
    Private Sub ResolveArmorDraftHeader(formID As UInteger, isOverride As Boolean, draftEdid As String, signature As String,
                                        espNameNoExt As String, usedEdids As HashSet(Of String), target As SaveEsp_Form.SaveTarget,
                                        ctx As SaveContext,
                                        ByRef finalEdid As String, ByRef src As PluginRecord,
                                        ByRef vcs1 As UInteger, ByRef vcs2 As UShort)
        src = Nothing
        vcs1 = 0UI
        vcs2 = 0US
        If isOverride Then
            finalEdid = draftEdid
            usedEdids.Add(finalEdid)
            src = ctx.PluginManager.GetRecord(formID)
            If src Is Nothing OrElse src.Header.Signature <> signature Then
                Throw New InvalidDataException(
                    $"{signature} override draft targets FormID {formID:X8}, which is not a loaded {signature} record. " &
                    "The source record must exist to emit an override (re-load the plugin or recreate the draft as new).")
            End If
            vcs1 = src.Header.VCS1
            vcs2 = src.Header.VCS2
        Else
            Dim desired = ApplyEspNamespaceToEditorId(draftEdid, espNameNoExt)
            Dim chosen = MakeUniqueEditorId(desired, usedEdids)
            finalEdid = chosen
            If Not String.Equals(chosen, desired, StringComparison.Ordinal) Then
                ' Copy into a local: a ByRef parameter (finalEdid) can't be captured in the LogLazy lambda.
                Logger.LogLazy(Function() $"[SAVE] {signature} EditorID '{desired}' already used in {IO.Path.GetFileName(target.TargetPath)} → renamed to '{chosen}' (FormID unchanged).")
            End If
        End If
    End Sub

    ''' <summary>Build an <see cref="SaveNpcEspWriter.ArmoRecordEntry"/> from an <see cref="ArmoDraft"/> (Phase 2e).
    ''' Mirrors the OTFT draft→entry construction in Phase 2c: NEW carries the provisional FormID + IsOverride=False;
    ''' OVERRIDE carries the real GLOBAL FormID + IsOverride=True + SourceRecord/VCS (the writer copies the
    ''' non-owned subrecords verbatim from SourceRecord and remaps their FormIDs).</summary>
    Private Function BuildArmoEntry(d As ArmoDraft, ctx As SaveContext, espNameNoExt As String,
                                    usedEdids As HashSet(Of String), target As SaveEsp_Form.SaveTarget) As SaveNpcEspWriter.ArmoRecordEntry
        Dim finalEdid As String = Nothing, src As PluginRecord = Nothing
        Dim vcs1 As UInteger, vcs2 As UShort
        ResolveArmorDraftHeader(d.FormID, d.IsOverride, d.EditorID, "ARMO", espNameNoExt, usedEdids, target, ctx, finalEdid, src, vcs1, vcs2)
        Dim e As New SaveNpcEspWriter.ArmoRecordEntry With {
            .FormID = d.FormID,
            .EditorID = finalEdid,
            .FullName = d.FullName,
            .SlotMask = d.SlotMask,
            .RaceFormID = d.RaceFormID,
            .InstanceNamingFormID = d.InstanceNamingFormID,
            .EnchantmentFormID = d.EnchantmentFormID,
            .PatternFormID = d.PatternFormID,
            .EquipTypeFormID = d.EquipTypeFormID,
            .PickupSoundFormID = d.PickupSoundFormID,
            .DropSoundFormID = d.DropSoundFormID,
            .AlternateBlockMaterialFormID = d.AlternateBlockMaterialFormID,
            .Description = d.Description,
            .NonPlayable = d.NonPlayable,
            .ObndX1 = d.ObndX1, .ObndY1 = d.ObndY1, .ObndZ1 = d.ObndZ1,
            .ObndX2 = d.ObndX2, .ObndY2 = d.ObndY2, .ObndZ2 = d.ObndZ2,
            .TemplateArmorFormID = d.TemplateArmorFormID,
            .MaleWorldModelPath = d.MaleWorldModelPath,
            .FemaleWorldModelPath = d.FemaleWorldModelPath,
            .MaleMaterialSwapFormID = d.MaleMaterialSwapFormID,
            .FemaleMaterialSwapFormID = d.FemaleMaterialSwapFormID,
            .Value = d.Value,
            .Weight = d.Weight,
            .Health = d.Health,
            .ArmorRating = d.ArmorRating,
            .SkyrimArmorRating = d.SkyrimArmorRating,
            .BaseAddonIndex = d.BaseAddonIndex,
            .StaggerRating = d.StaggerRating,
            .IsOverride = d.IsOverride,
            .OriginalVcs1 = vcs1,
            .OriginalVcs2 = vcs2,
            .SourceRecord = src
        }
        For Each a In d.ArmorAddons
            e.ArmorAddons.Add(New ARMO_AddonEntry With {.AddonIndex = a.AddonIndex, .ArmaFormID = a.ArmaFormID})
        Next
        e.KeywordFormIDs.AddRange(d.KeywordFormIDs)
        e.AttachParentSlotFormIDs.AddRange(d.AttachParentSlotFormIDs)
        For Each dr In d.DamageResistances
            e.DamageResistances.Add(New ARMO_DamageResist With {.DamageTypeFormID = dr.DamageTypeFormID, .Value = dr.Value})
        Next
        ' OBTS combinations:
        '   • NEW → always emit from the model (SerializeArmoRecord builds the OBTE/OBTS block from
        '     entry.Combinations). So a "New from template" ARMO keeps its object template on save.
        '   • OVERRIDE + edited → author from the model: populate Combinations (deep-copy) and flag
        '     CombinationsAuthored so SerializeArmoRecordOverride re-emits the block instead of preserving
        '     the source OBTS verbatim (Phase 4). Without this the user's OBTS edits would be lost.
        '   • OVERRIDE + NOT edited → leave the list empty and the flag False: the override path preserves
        '     the source Object Template block verbatim, keeping the record byte-exact.
        If Not d.IsOverride Then
            e.Combinations.AddRange(ArmoDraft.CloneCombinations(d.Combinations))
        ElseIf d.CombinationsEdited Then
            e.Combinations.AddRange(ArmoDraft.CloneCombinations(d.Combinations))
            e.CombinationsAuthored = True
        End If
        Return e
    End Function

    ''' <summary>Build an <see cref="SaveNpcEspWriter.ArmaRecordEntry"/> from an <see cref="ArmaDraft"/> (Phase 2f).
    ''' Same NEW/OVERRIDE contract as <see cref="BuildArmoEntry"/>.</summary>
    Private Function BuildArmaEntry(d As ArmaDraft, ctx As SaveContext, espNameNoExt As String,
                                    usedEdids As HashSet(Of String), target As SaveEsp_Form.SaveTarget) As SaveNpcEspWriter.ArmaRecordEntry
        Dim finalEdid As String = Nothing, src As PluginRecord = Nothing
        Dim vcs1 As UInteger, vcs2 As UShort
        ResolveArmorDraftHeader(d.FormID, d.IsOverride, d.EditorID, "ARMA", espNameNoExt, usedEdids, target, ctx, finalEdid, src, vcs1, vcs2)
        Dim e As New SaveNpcEspWriter.ArmaRecordEntry With {
            .FormID = d.FormID,
            .EditorID = finalEdid,
            .SlotMask = d.SlotMask,
            .RaceFormID = d.RaceFormID,
            .FootstepSetFormID = d.FootstepSetFormID,
            .ArtObjectFormID = d.ArtObjectFormID,
            .MaleFPMaterialSwapFormID = d.MaleFPMaterialSwapFormID,
            .FemaleFPMaterialSwapFormID = d.FemaleFPMaterialSwapFormID,
            .MalePriority = d.MalePriority,
            .FemalePriority = d.FemalePriority,
            .MaleWeightSliderFlags = d.MaleWeightSliderFlags,
            .FemaleWeightSliderFlags = d.FemaleWeightSliderFlags,
            .DetectionSoundValue = d.DetectionSoundValue,
            .WeaponAdjust = d.WeaponAdjust,
            .MaleMeshPath = d.MaleMeshPath,
            .FemaleMeshPath = d.FemaleMeshPath,
            .MaleFPMeshPath = d.MaleFPMeshPath,
            .FemaleFPMeshPath = d.FemaleFPMeshPath,
            .MaleModelFlags = d.MaleModelFlags,
            .FemaleModelFlags = d.FemaleModelFlags,
            .MaleFPModelFlags = d.MaleFPModelFlags,
            .FemaleFPModelFlags = d.FemaleFPModelFlags,
            .MaleColorRemapIndex = d.MaleColorRemapIndex,
            .FemaleColorRemapIndex = d.FemaleColorRemapIndex,
            .MaleSkinTextureFormID = d.MaleSkinTextureFormID,
            .FemaleSkinTextureFormID = d.FemaleSkinTextureFormID,
            .MaleSkinTextureSwapListFormID = d.MaleSkinTextureSwapListFormID,
            .FemaleSkinTextureSwapListFormID = d.FemaleSkinTextureSwapListFormID,
            .MaleMaterialSwapFormID = d.MaleMaterialSwapFormID,
            .FemaleMaterialSwapFormID = d.FemaleMaterialSwapFormID,
            .NoUnderarmorScaling = d.NoUnderarmorScaling,
            .HasSculptData = d.HasSculptData,
            .HiRes1stPersonOnly = d.HiRes1stPersonOnly,
            .IsOverride = d.IsOverride,
            .OriginalVcs1 = vcs1,
            .OriginalVcs2 = vcs2,
            .SourceRecord = src
        }
        e.AdditionalRaces.AddRange(d.AdditionalRaces)
        For Each g In d.BoneScaleData
            Dim cg As New ARMA_BoneScaleGender With {.Gender = g.Gender}
            For Each bd In g.Bones
                cg.Bones.Add(New ARMA_BoneScaleDelta With {
                    .BoneName = bd.BoneName, .DeltaX = bd.DeltaX, .DeltaY = bd.DeltaY, .DeltaZ = bd.DeltaZ})
            Next
            e.BoneScaleData.Add(cg)
        Next
        Return e
    End Function

    ''' <summary>Build an <see cref="SaveNpcEspWriter.MswpRecordEntry"/> from an <see cref="MswpDraft"/> (Phase 2g).
    ''' Same NEW/OVERRIDE contract as <see cref="BuildArmoEntry"/>.</summary>
    Private Function BuildMswpEntry(d As MswpDraft, ctx As SaveContext, espNameNoExt As String,
                                    usedEdids As HashSet(Of String), target As SaveEsp_Form.SaveTarget) As SaveNpcEspWriter.MswpRecordEntry
        Dim finalEdid As String = Nothing, src As PluginRecord = Nothing
        Dim vcs1 As UInteger, vcs2 As UShort
        ResolveArmorDraftHeader(d.FormID, d.IsOverride, d.EditorID, "MSWP", espNameNoExt, usedEdids, target, ctx, finalEdid, src, vcs1, vcs2)
        Dim e As New SaveNpcEspWriter.MswpRecordEntry With {
            .FormID = d.FormID,
            .EditorID = finalEdid,
            .TreeFolder = d.TreeFolder,
            .IsOverride = d.IsOverride,
            .OriginalVcs1 = vcs1,
            .OriginalVcs2 = vcs2,
            .SourceRecord = src
        }
        For Each s In d.Substitutions
            e.Substitutions.Add(New MSWP_Substitution With {
                .OriginalMaterial = s.OriginalMaterial,
                .ReplacementMaterial = s.ReplacementMaterial,
                .TreeFolder = s.TreeFolder,
                .HasColorRemapIndex = s.HasColorRemapIndex,
                .ColorRemapIndex = s.ColorRemapIndex})
        Next
        Return e
    End Function

    ''' <summary>Order-sensitive equality of two OTFT item (INAM) FormID lists. The engine equips items in
    ''' INAM order, and the writer emits them in list order, so a reorder IS a content change.</summary>
    Private Function OutfitItemsEqual(a As List(Of UInteger), b As List(Of UInteger)) As Boolean
        If a Is Nothing OrElse b Is Nothing Then Return a Is b
        If a.Count <> b.Count Then Return False
        For i = 0 To a.Count - 1
            If a(i) <> b(i) Then Return False
        Next
        Return True
    End Function

    ''' <summary>Build an <see cref="SaveNpcEspWriter.ArmoRecordEntry"/> OVERRIDE entry from a record PRESERVED
    ''' out of the target plugin (Phase 2a). Mirrors <see cref="BuildArmoEntry"/>'s field map exactly, but sources
    ''' every owned field from the parsed <see cref="ARMO_Data"/> instead of an <see cref="ArmoDraft"/>, and resolves
    ''' the header from <paramref name="rec"/> directly (no <see cref="ResolveArmorDraftHeader"/> — the source record
    ''' is in hand, the EditorID is kept verbatim, and the FormID is the record's resolved GLOBAL value). The parser
    ''' already resolves all referenced FormIDs to GLOBAL — exactly what the writer's override remapper expects.</summary>
    ''' <summary>Build a full OVERRIDE <see cref="SaveNpcEspWriter.LvliRecordEntry"/> for an LVLI draft whose target
    ''' record is NOT preserved in this plugin's Phase 2a sweep (a vanilla/master LVLI overridden for the first time).
    ''' The edited fields (LVLD/LVLM/LVLF + LVLO entries) come from the draft; the non-owned subrecords
    ''' (OBND/LLKC/LVLG/LVSG/ONAM + VCS) are copied from the SOURCE record so the override stays byte-faithful for
    ''' everything the user didn't touch. Mirror of the ARMO/ARMA override "owned from draft, rest from source" rule.
    ''' The entry FormID is the draft's real GLOBAL FormID (the writer master-remaps it on emit).</summary>
    Private Function BuildLvliOverrideEntryFromSource(d As LeveledListDraft, ctx As SaveContext) As SaveNpcEspWriter.LvliRecordEntry
        Dim le As New SaveNpcEspWriter.LvliRecordEntry With {
            .FormID = d.FormID, .EditorID = d.EditorID, .IsOverride = True,
            .ChanceNone = d.ChanceNone, .MaxCount = d.MaxCount, .Flags = d.FlagsByte()}
        Dim src = ctx.PluginManager.GetRecord(d.FormID)
        If src IsNot Nothing AndAlso src.Header.Signature = "LVLI" Then
            Dim p = RecordParsers.ParseLVLI(src, ctx.PluginManager)
            If p IsNot Nothing Then
                le.ObjectBoundsRaw = p.ObjectBoundsRaw
                le.HasUseGlobal = p.HasUseGlobal
                le.UseGlobalFormID = p.UseGlobalFormID
                le.HasEpicLootChance = p.HasEpicLootChance
                le.EpicLootChanceFormID = p.EpicLootChanceFormID
                le.HasOverrideName = p.HasOverrideName
                le.OverrideName = p.OverrideName
                le.OriginalVcs1 = src.Header.VCS1
                le.OriginalVcs2 = src.Header.VCS2
                For Each fk In p.FilterKeywords
                    le.FilterKeywords.Add(New SaveNpcEspWriter.LvliFilterKeywordData With {
                        .KeywordFormID = fk.KeywordFormID, .Chance = fk.Chance})
                Next
            End If
        End If
        For Each e In d.Entries
            If e.RefFormID = 0UI Then Continue For
            le.Entries.Add(New SaveNpcEspWriter.LvliEntryData With {
                .Level = e.Level, .RefFormID = e.RefFormID, .Count = e.Count, .ChanceNone = e.ChanceNone})
        Next
        Return le
    End Function

    Private Function BuildArmoEntryFromParsed(parsed As ARMO_Data, rec As PluginRecord, ctx As SaveContext) As SaveNpcEspWriter.ArmoRecordEntry
        Dim e As New SaveNpcEspWriter.ArmoRecordEntry With {
            .FormID = ctx.PluginManager.ResolveReferencedFormID(rec.SourcePluginName, rec.Header.FormID),
            .EditorID = parsed.EditorID,
            .FullName = parsed.FullName,
            .SlotMask = parsed.SlotMask,
            .RaceFormID = parsed.RaceFormID,
            .InstanceNamingFormID = parsed.InstanceNamingFormID,
            .EnchantmentFormID = parsed.EnchantmentFormID,
            .PatternFormID = parsed.PatternFormID,
            .EquipTypeFormID = parsed.EquipTypeFormID,
            .PickupSoundFormID = parsed.PickupSoundFormID,
            .DropSoundFormID = parsed.DropSoundFormID,
            .AlternateBlockMaterialFormID = parsed.AlternateBlockMaterialFormID,
            .Description = parsed.Description,
            .NonPlayable = parsed.NonPlayable,
            .ObndX1 = parsed.ObndX1, .ObndY1 = parsed.ObndY1, .ObndZ1 = parsed.ObndZ1,
            .ObndX2 = parsed.ObndX2, .ObndY2 = parsed.ObndY2, .ObndZ2 = parsed.ObndZ2,
            .TemplateArmorFormID = parsed.TemplateArmorFormID,
            .MaleWorldModelPath = parsed.MaleWorldModelPath,
            .FemaleWorldModelPath = parsed.FemaleWorldModelPath,
            .MaleMaterialSwapFormID = parsed.MaleMaterialSwapFormID,
            .FemaleMaterialSwapFormID = parsed.FemaleMaterialSwapFormID,
            .Value = parsed.Value,
            .Weight = parsed.Weight,
            .Health = parsed.Health,
            .ArmorRating = parsed.ArmorRating,
            .SkyrimArmorRating = parsed.SkyrimArmorRating,
            .BaseAddonIndex = If(parsed.BaseAddonIndex >= 0, CUShort(parsed.BaseAddonIndex), CUShort(0)),
            .StaggerRating = parsed.StaggerRating,
            .IsOverride = True,
            .OriginalVcs1 = rec.Header.VCS1,
            .OriginalVcs2 = rec.Header.VCS2,
            .SourceRecord = rec
        }
        For Each a In parsed.ArmorAddons
            e.ArmorAddons.Add(New ARMO_AddonEntry With {.AddonIndex = a.AddonIndex, .ArmaFormID = a.ArmaFormID})
        Next
        e.KeywordFormIDs.AddRange(parsed.KeywordFormIDs)
        e.AttachParentSlotFormIDs.AddRange(parsed.AttachParentSlotFormIDs)
        For Each dr In parsed.DamageResistances
            e.DamageResistances.Add(New ARMO_DamageResist With {.DamageTypeFormID = dr.DamageTypeFormID, .Value = dr.Value})
        Next
        Return e
    End Function

    ''' <summary>Build an <see cref="SaveNpcEspWriter.ArmaRecordEntry"/> OVERRIDE entry from a PRESERVED record
    ''' (Phase 2a). Mirrors <see cref="BuildArmaEntry"/>'s field map, sourcing from the parsed <see cref="ARMA_Data"/>.
    ''' See <see cref="BuildArmoEntryFromParsed"/> for the header/FormID-resolution rationale.</summary>
    Private Function BuildArmaEntryFromParsed(parsed As ARMA_Data, rec As PluginRecord, ctx As SaveContext) As SaveNpcEspWriter.ArmaRecordEntry
        Dim e As New SaveNpcEspWriter.ArmaRecordEntry With {
            .FormID = ctx.PluginManager.ResolveReferencedFormID(rec.SourcePluginName, rec.Header.FormID),
            .EditorID = parsed.EditorID,
            .SlotMask = parsed.SlotMask,
            .RaceFormID = parsed.RaceFormID,
            .FootstepSetFormID = parsed.FootstepSetFormID,
            .ArtObjectFormID = parsed.ArtObjectFormID,
            .MaleFPMaterialSwapFormID = parsed.MaleFPMaterialSwapFormID,
            .FemaleFPMaterialSwapFormID = parsed.FemaleFPMaterialSwapFormID,
            .MalePriority = parsed.MalePriority,
            .FemalePriority = parsed.FemalePriority,
            .MaleWeightSliderFlags = parsed.MaleWeightSliderFlags,
            .FemaleWeightSliderFlags = parsed.FemaleWeightSliderFlags,
            .DetectionSoundValue = parsed.DetectionSoundValue,
            .WeaponAdjust = parsed.WeaponAdjust,
            .MaleMeshPath = parsed.MaleMeshPath,
            .FemaleMeshPath = parsed.FemaleMeshPath,
            .MaleFPMeshPath = parsed.MaleFPMeshPath,
            .FemaleFPMeshPath = parsed.FemaleFPMeshPath,
            .MaleModelFlags = parsed.MaleModelFlags,
            .FemaleModelFlags = parsed.FemaleModelFlags,
            .MaleFPModelFlags = parsed.MaleFPModelFlags,
            .FemaleFPModelFlags = parsed.FemaleFPModelFlags,
            .MaleColorRemapIndex = parsed.MaleColorRemapIndex,
            .FemaleColorRemapIndex = parsed.FemaleColorRemapIndex,
            .MaleSkinTextureFormID = parsed.MaleSkinTextureFormID,
            .FemaleSkinTextureFormID = parsed.FemaleSkinTextureFormID,
            .MaleSkinTextureSwapListFormID = parsed.MaleSkinTextureSwapListFormID,
            .FemaleSkinTextureSwapListFormID = parsed.FemaleSkinTextureSwapListFormID,
            .MaleMaterialSwapFormID = parsed.MaleMaterialSwapFormID,
            .FemaleMaterialSwapFormID = parsed.FemaleMaterialSwapFormID,
            .NoUnderarmorScaling = parsed.NoUnderarmorScaling,
            .HasSculptData = parsed.HasSculptData,
            .HiRes1stPersonOnly = parsed.HiRes1stPersonOnly,
            .IsOverride = True,
            .OriginalVcs1 = rec.Header.VCS1,
            .OriginalVcs2 = rec.Header.VCS2,
            .SourceRecord = rec
        }
        e.AdditionalRaces.AddRange(parsed.AdditionalRaces)
        For Each g In parsed.BoneScaleData
            Dim cg As New ARMA_BoneScaleGender With {.Gender = g.Gender}
            For Each bd In g.Bones
                cg.Bones.Add(New ARMA_BoneScaleDelta With {
                    .BoneName = bd.BoneName, .DeltaX = bd.DeltaX, .DeltaY = bd.DeltaY, .DeltaZ = bd.DeltaZ})
            Next
            e.BoneScaleData.Add(cg)
        Next
        Return e
    End Function

    ''' <summary>Build an <see cref="SaveNpcEspWriter.MswpRecordEntry"/> OVERRIDE entry from a PRESERVED record
    ''' (Phase 2a). Mirrors <see cref="BuildMswpEntry"/>'s field map, sourcing from the parsed <see cref="MSWP_Data"/>.
    ''' See <see cref="BuildArmoEntryFromParsed"/> for the header/FormID-resolution rationale.</summary>
    Private Function BuildMswpEntryFromParsed(parsed As MSWP_Data, rec As PluginRecord, ctx As SaveContext) As SaveNpcEspWriter.MswpRecordEntry
        Dim e As New SaveNpcEspWriter.MswpRecordEntry With {
            .FormID = ctx.PluginManager.ResolveReferencedFormID(rec.SourcePluginName, rec.Header.FormID),
            .EditorID = parsed.EditorID,
            .TreeFolder = parsed.TreeFolder,
            .IsOverride = True,
            .OriginalVcs1 = rec.Header.VCS1,
            .OriginalVcs2 = rec.Header.VCS2,
            .SourceRecord = rec
        }
        For Each s In parsed.Substitutions
            e.Substitutions.Add(New MSWP_Substitution With {
                .OriginalMaterial = s.OriginalMaterial,
                .ReplacementMaterial = s.ReplacementMaterial,
                .TreeFolder = s.TreeFolder,
                .HasColorRemapIndex = s.HasColorRemapIndex,
                .ColorRemapIndex = s.ColorRemapIndex})
        Next
        Return e
    End Function

    ''' <summary>Build (or extend) the Leveled NPC list(s) for a save where <c>AddToLvlList</c> is set, and
    ''' append the resulting <see cref="SaveNpcEspWriter.LvliRecordEntry"/> (IsNpcList) to
    ''' <paramref name="leveledEntries"/>. CRITICAL (FormID resolution): every LVLO reference is the saved
    ''' NPC's GLOBAL FormID and every NEW list record carries a 0xFF provisional sentinel — the writer's
    ''' remapper/draftRemap performs ALL high-byte/master/ESL mapping. This method never builds a high byte.</summary>
    Private Sub BuildLeveledNpcListEntries(target As SaveEsp_Form.SaveTarget,
                                           inputs As List(Of NpcSaveInput),
                                           ctx As SaveContext,
                                           leveledEntries As List(Of SaveNpcEspWriter.LvliRecordEntry))
        ' Saved NPC FormIDs in scope: GLOBAL, de-duplicated, order preserved.
        Dim npcFids As New List(Of UInteger)
        Dim npcSeen As New HashSet(Of UInteger)
        For Each ni In inputs
            If ni IsNot Nothing AndAlso ni.NpcFormID <> 0UI AndAlso npcSeen.Add(ni.NpcFormID) Then npcFids.Add(ni.NpcFormID)
        Next
        If npcFids.Count = 0 Then Return

        ' "Evitar duplicados": drop NPCs already a member of ANY Leveled NPC list IN THIS PLUGIN. Scope =
        ' the LVLN bound for this save (preserved existing overrides in leveledEntries + the list being
        ' appended to). Cheap (no load-order scan) and predictable: vanilla/other-mod lists are not consulted.
        ' OFF by default → an NPC can intentionally live in several lists. Note: the existing-list append path
        ' ALSO dedups against the target list's own entries regardless of this flag (never double-add to one
        ' list); this flag widens that to all of the plugin's leveled-NPC lists.
        If target.LvlListNoDuplicate Then
            Dim alreadyLeveled As New HashSet(Of UInteger)
            For Each le In leveledEntries
                If Not le.IsNpcList Then Continue For
                For Each e In le.Entries
                    If e.RefFormID <> 0UI Then alreadyLeveled.Add(e.RefFormID)
                Next
            Next
            If alreadyLeveled.Count > 0 Then
                Dim before = npcFids.Count
                npcFids = npcFids.Where(Function(f) Not alreadyLeveled.Contains(f)).ToList()
                Dim skipped = before - npcFids.Count
                If skipped > 0 Then Logger.LogLazy(Function() $"[SAVE] Add-to-LVL: skipped {skipped} NPC(s) already in a leveled list of {IO.Path.GetFileName(target.TargetPath)} (no-dup).")
            End If
            If npcFids.Count = 0 Then Return  ' every selected NPC was already leveled — nothing to add
        End If

        ' Provisional FormID allocator: prefer MainForm's shared draft counter (no collision with OTFT/LVLI
        ' drafts). Fallback is a high local 0xFF counter — these LVLN are terminal (nothing references them by
        ' provisional), so the only requirement is in-save uniqueness.
        Dim fallbackCtr As UInteger = &HFF0F0000UI
        Dim allocProvisional As Func(Of UInteger) =
            Function() As UInteger
                If ctx.AllocateDraftFormID IsNot Nothing Then Return ctx.AllocateDraftFormID()
                fallbackCtr += 1UI
                Return fallbackCtr
            End Function

        ' Make one LVLO entry per NPC: Level 1 / Count 1 / ChanceNone 0. RefFormID is GLOBAL (remapped on emit).
        Dim makeEntry As Func(Of UInteger, SaveNpcEspWriter.LvliEntryData) =
            Function(fid) New SaveNpcEspWriter.LvliEntryData With {.Level = 1US, .RefFormID = fid, .Count = 1US, .ChanceNone = 0}

        ' Make a NEW LVLN sibling with the given EditorID + entries (provisional FormID, standard flags).
        Dim makeList As Func(Of String, IEnumerable(Of SaveNpcEspWriter.LvliEntryData), SaveNpcEspWriter.LvliRecordEntry) =
            Function(edid, ents)
                Dim le As New SaveNpcEspWriter.LvliRecordEntry With {
                    .FormID = allocProvisional(),
                    .EditorID = edid,
                    .ChanceNone = 0,
                    .MaxCount = 0,
                    .Flags = 0,
                    .IsOverride = False,
                    .IsNpcList = True
                }
                le.Entries.AddRange(ents)
                Return le
            End Function

        ' EditorIDs already used by LVLN in this save (preserved overrides + drafts) — so overflow siblings
        ' never collide with an existing list name.
        Dim usedEdids As New HashSet(Of String)(
            leveledEntries.Where(Function(l) l.IsNpcList).Select(Function(l) l.EditorID),
            StringComparer.OrdinalIgnoreCase)

        If target.LvlListIsNew Then
            ' NEW list(s): final EditorID = npcm_<ESPNAME>_LVLN_<name>; first chunk keeps the base, overflow
            ' chunks get _1, _2, … The esp segment is injected here (same convention as OTFT/LVLI drafts).
            Dim espNameNoExt = IO.Path.GetFileNameWithoutExtension(target.TargetPath)
            Dim editorBase = ApplyEspNamespaceToEditorId(LeveledNpcListEditorIdPrefix & If(target.LvlListNewName, "").Trim(), espNameNoExt)
            Dim chunks = ChunkList(npcFids, LeveledListEntryCap)
            For ci = 0 To chunks.Count - 1
                Dim edid = NextFreeListEditorId(editorBase, ci, usedEdids)
                leveledEntries.Add(makeList(edid, chunks(ci).Select(makeEntry)))
            Next
        Else
            ' EXISTING list: the preserve-existing pass already added the chosen LVLN to leveledEntries as an
            ' override (matched by EditorID). Append the new NPCs, deduped against its current entries; spill
            ' any overflow past 255 into NEW sibling lists EditorID_1, _2, …
            Dim targetEdid = If(target.LvlListExistingEditorID, "")
            Dim host = leveledEntries.FirstOrDefault(
                Function(l) l.IsNpcList AndAlso String.Equals(l.EditorID, targetEdid, StringComparison.OrdinalIgnoreCase))
            If host Is Nothing Then Return  ' chosen list not present (shouldn't happen — dialog only offers in-plugin LVLN)

            Dim present As New HashSet(Of UInteger)(host.Entries.Select(Function(e) e.RefFormID))
            For Each fid In npcFids
                If present.Add(fid) Then host.Entries.Add(makeEntry(fid))
            Next

            If host.Entries.Count > LeveledListEntryCap Then
                Dim overflow = host.Entries.Skip(LeveledListEntryCap).ToList()
                host.Entries.RemoveRange(LeveledListEntryCap, host.Entries.Count - LeveledListEntryCap)
                Dim siblingIdx As Integer = 1
                For Each chunk In ChunkList(overflow, LeveledListEntryCap)
                    Dim edid = NextFreeListEditorId(host.EditorID, siblingIdx, usedEdids)
                    leveledEntries.Add(makeList(edid, chunk))
                    siblingIdx += 1
                Next
            End If
        End If
    End Sub

    ''' <summary>Resolve a list EditorID for chunk index <paramref name="idx"/>: idx 0 → the base name as-is;
    ''' idx ≥ 1 → base_idx, advancing past any name already taken in <paramref name="used"/> (so overflow
    ''' siblings never collide). Adds the chosen name to <paramref name="used"/>.</summary>
    Private Function NextFreeListEditorId(baseEdid As String, idx As Integer, used As HashSet(Of String)) As String
        Dim edid As String = baseEdid
        Dim n As Integer = If(idx <= 0, 0, idx)
        If n = 0 Then
            ' First chunk keeps the base name; if (rarely) taken, fall through to suffixing from _1.
            If Not used.Contains(edid) Then
                used.Add(edid)
                Return edid
            End If
            n = 1
        End If
        Do
            edid = $"{baseEdid}_{n}"
            n += 1
        Loop While used.Contains(edid)
        used.Add(edid)
        Return edid
    End Function

    ''' <summary>Split a list into consecutive chunks of at most <paramref name="size"/> items, order preserved.</summary>
    Private Function ChunkList(Of T)(items As IList(Of T), size As Integer) As List(Of List(Of T))
        Dim result As New List(Of List(Of T))
        Dim i As Integer = 0
        While i < items.Count
            Dim take = Math.Min(size, items.Count - i)
            Dim chunk As New List(Of T)(take)
            For j = 0 To take - 1
                chunk.Add(items(i + j))
            Next
            result.Add(chunk)
            i += take
        End While
        Return result
    End Function

    Private Function MapGlobalToLocalInPlugin(npcFormID As UInteger, reader As PluginReader, pm As PluginManager) As UInteger
        Dim npcSourceMasterName As String = pm.GetOriginatingPluginName(npcFormID)
        Dim npcIsLight As Boolean = ((npcFormID >> 24) And &HFFUI) = &HFEUI
        Dim npcObject As UInteger = If(npcIsLight, npcFormID And &HFFFUI, npcFormID And &HFFFFFFUI)
        If String.IsNullOrEmpty(npcSourceMasterName) Then Return npcFormID
        Dim newHigh As Integer = -1
        For i = 0 To reader.Masters.Count - 1
            If String.Equals(reader.Masters(i), npcSourceMasterName, StringComparison.OrdinalIgnoreCase) Then
                newHigh = i
                Exit For
            End If
        Next
        If newHigh < 0 Then newHigh = reader.Masters.Count  ' self
        Return (CUInt(newHigh) << 24) Or npcObject
    End Function

    ''' <summary>Push a marquee-style phase update through IProgress. The runtime marshals the
    ''' callback to the UI thread (the IProgress was constructed there), so the panel repaints
    ''' on the next message-pump tick.</summary>
    Private Sub ReportPhase(progress As IProgress(Of SaveProgress), phase As String, detail As String)
        If progress Is Nothing Then Return
        progress.Report(New SaveProgress With {.Phase = phase, .Detail = detail, .Determinate = False})
    End Sub

    ''' <summary>Overwrite the entry for one NPC in an in-memory sidecar with whatever its overlay
    ''' currently holds (BodyMorphs + SkinTemplate). Entries for other NPCs are preserved. The
    ''' caller reads the sidecar once and calls this per NPC, then writes once.</summary>
    Private Sub MergeOneNpcIntoSidecar(merged As BssliderSidecar.SidecarFile,
                                       npcFormID As UInteger,
                                       npcSpec As NPC_Data,
                                       ctx As SaveContext)
        ' BodyGen matches morphs.ini rows by the NPC's ORIGINATING master (the plugin that
        ' originally defines the NPC), not by the override plugin we're writing to.
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
            ' SSE keyed body morphs — deep-copy the nested dict so the sidecar copy is independent of the
            ' live overlay (mirrors the flat BodyMorphSliders copy above and LooksmenuLoader's preset
            ' deep-copy). FO4 presets leave BodyMorphsKeyed = Nothing, so this block no-ops on FO4 and
            ' the sidecar entry keeps BodyMorphsKeyed = Nothing (FO4 behavior identical).
            If overlay.BodyMorphsKeyed IsNot Nothing Then
                Dim keyedCopy As New Dictionary(Of String, Dictionary(Of String, Single))(StringComparer.OrdinalIgnoreCase)
                For Each kv In overlay.BodyMorphsKeyed
                    Dim inner As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
                    If kv.Value IsNot Nothing Then
                        For Each ikv In kv.Value
                            inner(ikv.Key) = ikv.Value
                        Next
                    End If
                    keyedCopy(kv.Key) = inner
                Next
                entry.BodyMorphsKeyed = keyedCopy
            End If
            entry.SkinTemplateId = If(overlay.SkinTemplateId, "")
            ' Overlays (LM body tattoos) — deep-copy each entry, cloning the float arrays so the
            ' sidecar copy is independent of the live overlay. Mirrors the BodyMorphSliders copy
            ' above and LooksmenuLoader's preset deep-copy. NOT routed to BodyGen (see
            ' EmitBodyGenFromSidecar): overlays have no in-game file mechanism.
            If overlay.Overlays IsNot Nothing Then
                For Each ov In overlay.Overlays
                    entry.Overlays.Add(New LooksmenuLoader.OverlayEntry With {
                        .TemplateId = ov.TemplateId,
                        .Priority = ov.Priority,
                        .Tint = If(ov.Tint Is Nothing, Nothing, CType(ov.Tint.Clone(), Single())),
                        .OffsetUV = If(ov.OffsetUV Is Nothing, Nothing, CType(ov.OffsetUV.Clone(), Single())),
                        .ScaleUV = If(ov.ScaleUV Is Nothing, Nothing, CType(ov.ScaleUV.Clone(), Single()))
                    })
                Next
            End If
            ' SSE body overlays (path-based RaceMenu tattoos) — deep-copy onto the sidecar entry (SSE-only,
            ' nullable). FO4 presets leave SseBodyOverlays = Nothing so this no-ops on FO4.
            If overlay.SseBodyOverlays IsNot Nothing AndAlso overlay.SseBodyOverlays.Count > 0 Then
                entry.SseBodyOverlays = LooksmenuLoader.CloneSseBodyOverlays(overlay.SseBodyOverlays)
            End If
            ' SSE node transforms (body-scale/position/rotation) — deep-copy the full per-node TRS onto the sidecar
            ' entry so an edited position/rotation survives a reload, not just the scale (SSE-only, nullable).
            If overlay.SseNodeTransforms IsNot Nothing AndAlso overlay.SseNodeTransforms.Count > 0 Then
                Dim list As New List(Of RaceMenuJslot.JslotNodeTransform)(overlay.SseNodeTransforms.Count)
                For Each nt In overlay.SseNodeTransforms
                    If nt IsNot Nothing AndAlso Not String.IsNullOrEmpty(nt.NodeName) AndAlso Not nt.IsIdentity Then list.Add(nt.Clone())
                Next
                If list.Count > 0 Then entry.SseNodeTransforms = list
            End If
            ' SSE RaceMenu absolute hair tint (packed RGB) — co-save data, persist so it survives a reload.
            If overlay.SseHairColorRgb.HasValue Then entry.SseHairColorRgb = overlay.SseHairColorRgb
            ' SSE skin overrides (body-paint per slot) — deep-copy onto the sidecar entry (SSE-only, nullable).
            If overlay.SseSkinOverrides IsNot Nothing AndAlso overlay.SseSkinOverrides.Count > 0 Then
                entry.SseSkinOverrides = LooksmenuLoader.CloneSseSkinOverrides(overlay.SseSkinOverrides)
            End If
            ' SSE custom face morphs (RaceMenu NiOverride named morphs) — co-save data, persist so they survive reload.
            If overlay.SseCustomMorphs IsNot Nothing AndAlso overlay.SseCustomMorphs.Count > 0 Then
                Dim cms As New List(Of NPC_CustomMorph)(overlay.SseCustomMorphs.Count)
                For Each cm In overlay.SseCustomMorphs : cms.Add(New NPC_CustomMorph With {.Name = cm.Name, .Value = cm.Value}) : Next
                entry.SseCustomMorphs = cms
            End If
            ' SSE per-vertex head sculpt — co-save data, persist so it survives reload.
            If overlay.SseSculptHead IsNot Nothing AndAlso overlay.SseSculptHead.Count > 0 Then
                Dim sc As New List(Of NPC_SculptVert)(overlay.SseSculptHead.Count)
                For Each sv In overlay.SseSculptHead : sc.Add(New NPC_SculptVert With {.Index = sv.Index, .Dx = sv.Dx, .Dy = sv.Dy, .Dz = sv.Dz}) : Next
                entry.SseSculptHead = sc
            End If
            ' SSE per-SHAPE sculpt (head+brows+eyes+mouth) — full-fidelity co-save superset; persist so all four parts survive reload.
            If overlay.SseSculptParts IsNot Nothing AndAlso overlay.SseSculptParts.Count > 0 Then
                entry.SseSculptParts = LooksmenuLoader.CloneSseSculptParts(overlay.SseSculptParts)
            End If
            ' SSE per-layer custom tint mask textures (RaceMenu co-save) — no ESP home, persist so they survive reload.
            If overlay.SseTintTexOverride IsNot Nothing AndAlso overlay.SseTintTexOverride.Count > 0 Then
                entry.SseTintTexOverride = New Dictionary(Of Integer, String)(overlay.SseTintTexOverride)
            End If
        End If

        ' Always overwrite the NPC's slot — even if entry ends up empty. Write() drops empty entries
        ' so a clear-then-save round trip removes the row instead of leaving stale data on disk.
        merged.Npcs(identifier) = entry
    End Sub

    ''' <summary>Translate the merged sidecar into BodyGenIniWriter entries and emit the .ini
    ''' pair. Sidecar rows without BodyMorphs (SkinTemplate-only entries) are skipped — the
    ''' Skin override is an F4SE feature unrelated to BodyGen. Malformed identifiers are also
    ''' skipped silently; the sidecar Read() already filters them out.</summary>
    Private Sub EmitBodyGenFromSidecar(target As SaveEsp_Form.SaveTarget,
                                       sidecar As BssliderSidecar.SidecarFile,
                                       ctx As SaveContext)
        ' Folder name = the plugin's modInfo->name (WITH extension) — the engine looks up
        ' BodyGen\<Name.esp>\ (f4ee BodyGenInterface.cpp:534 / skee64 BodyMorphInterface.cpp:132). Must match
        ' the IniExists() name above (also GetFileName) so delete/update finds the same folder.
        Dim baseName = IO.Path.GetFileName(target.TargetPath)

        ' Branch the BodyGen writer by game: SSE writes the skee64 pair under
        ' Meshes\actors\character\BodyGenData\<plugin>\ and sources values from the keyed sidecar
        ' morphs (flattened by summing); FO4 keeps the existing F4SE\...\BodyGen\ path + flat morphs.
        If Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            EmitSseBodyGenFromSidecar(baseName, sidecar, ctx)
            Return
        End If

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

        BodyGenIniWriter.Emit(ctx.DataPath, baseName, entries)
    End Sub

    ''' <summary>SSE branch of <see cref="EmitBodyGenFromSidecar"/>: translate the merged sidecar into
    ''' <see cref="SseBodyGenIniWriter"/> entries and emit the skee64 .ini pair. Morph values come from
    ''' the entry's SSE-only <c>BodyMorphsKeyed</c>, flattened by summing each morph's keyed
    ''' contributions (the engine nets keyed values — RaceMenuJslot.BodyMorphsToFlatSliderDict does the
    ''' same). Falls back to the flat <c>BodyMorphs</c> dict when a row has no keyed data. SkinTemplate-only
    ''' rows (no morphs) are skipped, as in the FO4 path.</summary>
    Private Sub EmitSseBodyGenFromSidecar(baseName As String,
                                          sidecar As BssliderSidecar.SidecarFile,
                                          ctx As SaveContext)
        Dim entries As New List(Of SseBodyGenIniWriter.NpcEntry)
        For Each kv In sidecar.Npcs
            Dim e = kv.Value
            If e Is Nothing Then Continue For

            ' Flat (summed) render values: prefer the keyed dict, fall back to the flat BodyMorphs.
            Dim flat As Dictionary(Of String, Single) = Nothing
            If e.BodyMorphsKeyed IsNot Nothing AndAlso e.BodyMorphsKeyed.Count > 0 Then
                flat = New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
                For Each mk In e.BodyMorphsKeyed
                    If String.IsNullOrEmpty(mk.Key) Then Continue For
                    Dim sum As Single = 0.0F
                    If mk.Value IsNot Nothing Then
                        For Each ikv In mk.Value : sum += ikv.Value : Next
                    End If
                    Dim existing As Single
                    If flat.TryGetValue(mk.Key, existing) Then flat(mk.Key) = existing + sum Else flat(mk.Key) = sum
                Next
            ElseIf e.BodyMorphs IsNot Nothing AndAlso e.BodyMorphs.Count > 0 Then
                flat = New Dictionary(Of String, Single)(e.BodyMorphs, StringComparer.OrdinalIgnoreCase)
            End If
            If flat Is Nothing OrElse flat.Count = 0 Then Continue For  ' SkinTemplate-only / no morphs → skip

            Dim masterName As String = ""
            Dim localFid As UInteger = 0UI
            If Not BssliderSidecar.TryParseIdentifier(kv.Key, masterName, localFid) Then Continue For

            Dim editorId = If(e.EditorId, "")
            Dim templateName = "NPCM_" & SseBodyGenIniWriter.SanitizeTemplateName(editorId)
            entries.Add(New SseBodyGenIniWriter.NpcEntry With {
                .TemplateName = templateName,
                .MasterPluginFileName = masterName,
                .LocalFormIDHex = localFid.ToString("X6"),
                .Gender = If(e.Gender, ""),
                .BodyMorphs = flat
            })
        Next

        SseBodyGenIniWriter.Emit(ctx.DataPath, baseName, entries)
    End Sub

End Module
