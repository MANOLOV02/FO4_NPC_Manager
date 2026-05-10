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
    '''   4. Optional verifier (only when <see cref="NpcPreviewLog.Enabled"/>).
    '''   5. Optional CharGen bake + BA2 pack (per <see cref="SaveEsp_Form.SaveTarget.GenerateChargen"/>).</summary>
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
            NpcPreviewLog.LogLazy(Function() $"  [SAVE-ESP] EXCEPTION {ex.GetType().Name}: {ex.Message}")
        End Try

        Return result
    End Function

    ''' <summary>Bag returned by <see cref="ExecuteWritePhases"/> so the caller can hand off
    ''' state from the worker-thread phases to subsequent UI-thread phases.</summary>
    Private Class WritePhaseResult
        Public NpcSpec As NPC_Data
    End Class

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

            ' Translate the global FormID (high byte = host load-order) to the local FormID
            ' the reader sees (high byte = MAST idx of the source master in the existing
            ' plugin) so we can identify and skip the record we're about to replace.
            Dim npcSourceMasterName As String = ""
            Dim npcGlobalHigh As Integer = CInt((npcFormID >> 24) And &HFFUI)
            If npcGlobalHigh >= 0 AndAlso npcGlobalHigh < ctx.PluginManager.Plugins.Count Then
                Dim sp = ctx.PluginManager.Plugins(npcGlobalHigh)
                If sp IsNot Nothing Then npcSourceMasterName = sp.FileName
            End If
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
                npcLocalFormID = (CUInt(newHigh) << 24) Or (npcFormID And &HFFFFFFUI)
            End If

            For Each kv In reader.Records
                Dim rec = kv.Value
                If rec.Header.FormID = npcLocalFormID Then Continue For
                existingRecords.Add(rec)
            Next
        End If

        ' Phase 3: write plugin.
        ReportPhase(progress, "Writing NPC override to plugin…", IO.Path.GetFileName(target.TargetPath))
        Dim entries As New List(Of SaveNpcEspWriter.NpcOverrideEntry) From {entry}
        Dim game = Config_App.Current.Game
        Dim writeRes = SaveNpcEspWriter.SaveOverridePlugin(
            target.TargetPath, game, target.MarkAsMaster, target.LightMaster,
            entries, existingRecords, existingMasters, ctx.PluginManager)

        result.WriterResult = writeRes

        For Each existingRec In existingRecords
            result.SavedFormIDs.Add(existingRec.Header.FormID)
        Next
        result.SavedFormIDs.Add(npcFormID)

        NpcPreviewLog.LogSeparator($"SAVE ESP from {npc.EditorID} [0x{npc.FormID:X8}]")
        NpcPreviewLog.LogLazy(Function() $"  written to: {writeRes.OutputPath}")
        NpcPreviewLog.LogLazy(Function() $"  NPC count: {writeRes.NpcCount}, MAST list: {String.Join(", ", writeRes.MasterList)}")
        If writeRes.RemovedMasters.Count > 0 Then
            NpcPreviewLog.LogLazy(Function() $"  Removed masters: {String.Join(", ", writeRes.RemovedMasters)}")
        End If
        If writeRes.AddedMasters.Count > 0 Then
            NpcPreviewLog.LogLazy(Function() $"  Added masters: {String.Join(", ", writeRes.AddedMasters)}")
        End If
        For Each kvp In writeRes.MasterAudit
            Dim auditMaster = kvp.Key
            Dim auditFids = kvp.Value
            NpcPreviewLog.LogLazy(Function() $"  [MAST] {auditMaster} ({auditFids.Count} ref{If(auditFids.Count = 1, "", "s")}): {String.Join(", ", auditFids.Take(8).Select(Function(f) $"0x{f:X8}"))}{If(auditFids.Count > 8, " ...", "")}")
        Next

        ' Phase 4: optional round-trip verifier (debug only — gated by NpcPreviewLog.Enabled).
        result.VerifierIcon = MessageBoxIcon.Information
        If NpcPreviewLog.Enabled Then
            ReportPhase(progress, "Verifying written record…", "")
            Dim verifyRes = NpcOverrideVerifier.VerifyWrittenOverride(writeRes.OutputPath, npcSpec, sourcePluginName, ctx.PluginManager)
            If Not String.IsNullOrEmpty(verifyRes.FatalError) Then
                NpcPreviewLog.LogLazy(Function() $"  [VERIFY] FATAL: {verifyRes.FatalError}")
                result.VerifierSummary = vbCrLf & vbCrLf & "Verifier could not run: " & verifyRes.FatalError
                result.VerifierIcon = MessageBoxIcon.Warning
            ElseIf verifyRes.Match Then
                NpcPreviewLog.Log("  [VERIFY] todo igual")
                result.VerifierSummary = vbCrLf & vbCrLf & "Verifier: todo igual"
            Else
                NpcPreviewLog.LogLazy(Function() $"  [VERIFY] {verifyRes.Differences.Count} differences:")
                For Each line In verifyRes.Differences
                    Dim local = line
                    NpcPreviewLog.LogLazy(Function() "    " & local)
                Next
                Dim shownCount = Math.Min(verifyRes.Differences.Count, 10)
                Dim sb As New System.Text.StringBuilder()
                sb.AppendLine()
                sb.AppendLine()
                sb.AppendLine($"Verifier: {verifyRes.Differences.Count} difference(s) — see log for full list.")
                For i = 0 To shownCount - 1
                    sb.AppendLine($"  • {verifyRes.Differences(i)}")
                Next
                If verifyRes.Differences.Count > shownCount Then
                    sb.AppendLine($"  … {verifyRes.Differences.Count - shownCount} more")
                End If
                result.VerifierSummary = sb.ToString()
                result.VerifierIcon = MessageBoxIcon.Warning
            End If
        End If

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

End Module
