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
        ''' <summary>FaceGen bake delegate: invoked once per NPC when the user opted into CharGen
        ''' bake + BA2 pack. Kept as a callback because the bake pipeline lives in MainForm/
        ''' FaceGenBuilder and pulls GL resources from <see cref="RenderHost"/>.</summary>
        Public RunChargenBakeAndPack As Func(Of UInteger, String, String, IProgress(Of SaveProgress), Task(Of (Summary As String, Success As Boolean)))
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

            ' Phase 4: CharGen bake, once per NPC. RunChargenBakeAndPack runs the GL-bound texture
            ' bake synchronously on the UI thread (the GL context lives there) and then awaits the
            ' BA2 pack on a worker thread, so the UI only blocks for the (short) GL bake, not the
            ' (long) compression. Yield first so the "NPC k/N" header paints before the GL bake.
            If target.GenerateChargen Then
                Dim totalBakes = inputs.Count
                Dim bakedOk = 0
                Dim bakedFail = 0
                For i = 0 To inputs.Count - 1
                    If bakeCancel.IsCancellationRequested Then
                        result.BakeCancelled = True
                        Exit For
                    End If
                    Dim npcInput = inputs(i)
                    Await Task.Yield()
                    Dim label = If(npcInput.Npc IsNot Nothing AndAlso Not String.IsNullOrEmpty(npcInput.Npc.EditorID),
                                   npcInput.Npc.EditorID, npcInput.NpcFormID.ToString("X8"))
                    progress?.Report(New SaveProgress With {
                        .Phase = "Baking CharGen NIF + textures…",
                        .Detail = $"NPC {i + 1}/{totalBakes}: {label}",
                        .Determinate = True,
                        .Max = totalBakes,
                        .Current = i + 1
                    })
                    Dim chargenRes = Await ctx.RunChargenBakeAndPack(npcInput.NpcFormID, target.TargetPath, npcInput.SourcePluginName, progress)
                    If chargenRes.Success Then bakedOk += 1 Else bakedFail += 1
                Next
                If totalBakes = 1 Then
                    ' Single-NPC: terse summary. Only mention failures when there ARE any (no "0 failed" noise).
                    result.ChargenSummary = $"{vbCrLf}{vbCrLf}CharGen bake: {bakedOk} OK" &
                        If(bakedFail > 0, $", {bakedFail} failed", "") & "."
                Else
                    result.ChargenSummary = $"{vbCrLf}{vbCrLf}CharGen bake: {bakedOk}/{totalBakes} OK" &
                        If(bakedFail > 0, $", {bakedFail} failed", "") &
                        If(result.BakeCancelled, " (cancelled — remaining NPCs not baked)", "") & "."
                End If
                result.ChargenSuccess = (bakedFail = 0)
                If bakedFail > 0 Then result.VerifierIcon = MessageBoxIcon.Warning
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

        ' Phase 1: build one override entry per NPC. outfitEntries is shared and deduped at the end.
        Dim entries As New List(Of SaveNpcEspWriter.NpcOverrideEntry)
        For Each npcInput In inputs
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
        If Not target.IsNewPlugin AndAlso File.Exists(target.TargetPath) Then
            Dim reader As New PluginReader()
            reader.Load(target.TargetPath)
            existingMasters.AddRange(reader.Masters)

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
                If rec.Header.Signature = "LVLI" Then
                    Dim parsedLvli = RecordParsers.ParseLVLI(rec, ctx.PluginManager)
                    Dim le As New SaveNpcEspWriter.LvliRecordEntry With {
                        .FormID = ctx.PluginManager.ResolveReferencedFormID(rec.SourcePluginName, rec.Header.FormID),
                        .EditorID = parsedLvli.EditorID,
                        .ChanceNone = parsedLvli.ChanceNone,
                        .MaxCount = parsedLvli.MaxCount,
                        .Flags = parsedLvli.Flags,
                        .IsOverride = True
                    }
                    For Each ent In parsedLvli.Entries
                        le.Entries.Add(New SaveNpcEspWriter.LvliEntryData With {
                            .Level = ent.Level, .RefFormID = ent.FormID, .Count = ent.Count, .ChanceNone = ent.ChanceNone})
                    Next
                    leveledEntries.Add(le)
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
            For Each d In ctx.OutfitDrafts
                If d Is Nothing OrElse d.FormID = OutfitDraft.PreviewDraftFormID Then Continue For
                If Not (d.IsDirty OrElse referencedDoft.Contains(d.FormID)) Then Continue For
                If Not alreadyEmitted.Add(d.FormID) Then Continue For
                Dim oe As New SaveNpcEspWriter.OtftRecordEntry With {
                    .FormID = d.FormID,
                    .EditorID = d.EditorID,
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
            For Each fid In needed
                If Not alreadyLeveled.Add(fid) Then Continue For
                Dim d = draftByFid(fid)
                Dim le As New SaveNpcEspWriter.LvliRecordEntry With {
                    .FormID = d.FormID,
                    .EditorID = d.EditorID,
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

        ' Phase 3: write the plugin (all entries in one pass).
        ReportPhase(progress, "Writing NPC override to plugin…", IO.Path.GetFileName(target.TargetPath))
        Dim game = Config_App.Current.Game
        Dim writeRes = SaveNpcEspWriter.SaveOverridePlugin(
            target.TargetPath, game, target.MarkAsMaster, target.LightMaster,
            entries, existingRecords, existingMasters, ctx.PluginManager, outfitEntries, leveledEntries)

        result.WriterResult = writeRes
        result.DraftFormIdMap = writeRes.DraftFormIdMap
        For Each existingRec In existingRecords
            result.SavedFormIDs.Add(existingRec.Header.FormID)
        Next
        For Each npcInput In inputs
            result.SavedFormIDs.Add(npcInput.NpcFormID)
            result.WrittenNpcFormIDs.Add(npcInput.NpcFormID)
        Next

        ' Phase 3b: refresh the BodyMorphs/Skin sidecar (default ON). Read once, merge every NPC in
        ' the batch, write once. The BodyGen emitter consumes the post-merge dict so its .ini
        ' reflects all NPCs of the plugin (this batch + any pre-existing entries from prior saves).
        If target.WriteBssliders OrElse target.EmitBodyGen Then
            Dim sidecarPath = BssliderSidecar.BuildPath(target.TargetPath)
            Dim mergedSidecar = BssliderSidecar.Read(sidecarPath)
            If mergedSidecar Is Nothing Then mergedSidecar = New BssliderSidecar.SidecarFile()
            mergedSidecar.Plugin = IO.Path.GetFileName(target.TargetPath)
            ' entries are built in input order in Phase 1, so entries(i) ↔ inputs(i).
            For i = 0 To inputs.Count - 1
                MergeOneNpcIntoSidecar(mergedSidecar, inputs(i).NpcFormID, entries(i).Npc, ctx)
            Next
            If target.WriteBssliders Then
                ReportPhase(progress, "Writing .bssliders sidecar…", IO.Path.GetFileName(target.TargetPath))
                BssliderSidecar.Write(sidecarPath, mergedSidecar)
            End If
            If target.EmitBodyGen Then
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

        ' Reconcile the IsCharGenFacePreset overlay edit into the ACBS struct the writer emits.
        ' ApplyPresetOverlayToNpcData sets only the AcbsFlags mirror; EmitAcbs writes Acbs.Flags, and
        ' CopyRoundTripOnlyFieldsFromRaw copies Acbs from the raw parse BY REFERENCE — so without this
        ' the edited bit never reaches the ESP. Clone before mutating so the shared raw parse isn't
        ' corrupted. (No-op when the two already agree, i.e. no IsCharGenFacePreset override.)
        If npcSpec.Acbs IsNot Nothing AndAlso npcSpec.Acbs.Flags <> npcSpec.AcbsFlags Then
            npcSpec.Acbs = CloneAcbsWithFlags(npcSpec.Acbs, npcSpec.AcbsFlags)
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

        ' Phase 1c: rebuild HeadPartFormIDs from raw NPC PNAM ∪ preset, dedup by PartType.
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
            For Each fid In rawHeadParts
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
            entry.SkinTemplateId = If(overlay.SkinTemplateId, "")
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
