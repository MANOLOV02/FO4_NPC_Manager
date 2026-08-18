Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports FO4_Base_Library

''' <summary>Orquestador del flujo de Save NPC override: arma las entries, escribe el plugin por
''' <see cref="SaveNpcEspWriter"/> y, opcionalmente, hornea el NIF de CharGen con sus texturas y las empaqueta
''' al BA2.
''' <para>Es batch: <see cref="ExecuteAsync"/> toma una LISTA de <see cref="NpcSaveInput"/>, todos los NPCs van
''' a UN plugin en una sola escritura, y el bake corre una vez por NPC despues de escribir.</para>
''' <para>Reporta por <see cref="IProgress(Of SaveProgress)"/> para que el dialogo muestre su panel sin un form
''' aparte. Las tareas de limpieza que dependen de internals del MainForm (cache del plugin autogenerado,
''' refresh del arbol, re-lectura post-save, MessageBox) NO se hacen aca: el orquestador devuelve los datos y
''' el MainForm las hace al cerrar el dialogo.</para></summary>
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
        ''' <summary>Avisos del payload del apply-script acumulados durante el guardado (recortes por el tope de
        ''' 128 elementos, VMAD cerca del techo de 64 KB). Se vuelcan al resumen post-guardado: un recorte que
        ''' sólo va al log se ve EXACTAMENTE igual que un payload completo.</summary>
        Public PayloadWarnings As New List(Of String)
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
        ''' <summary>Set by <see cref="BuildOverrideEntry"/> when at least one NPC got our Papyrus apply-script
        ''' attached to its VMAD. The compiled .pex is then installed ONCE into <c>Data\Scripts\</c> — no point
        ''' copying it when no record references it.</summary>
        Public WroteApplyScript As Boolean

        ''' <summary>Generacion del payload del apply-script para ESTE guardado, y nombre del plugin de destino.
        ''' Se resuelven UNA vez (del sidecar, que es la fuente de verdad) la primera vez que un NPC lo necesita, y
        ''' los usan TODOS los NPC y la instalacion del .pex — si no fueran el mismo numero, el VMAD y el .pex
        ''' quedarian en generaciones distintas y el script leeria None.</summary>
        Public ApplyScriptGeneration As Integer = 0
        ''' <summary>SAL del sufijo de generacion, sorteada UNA vez por guardado. Va al VMAD, al .pex
        ''' parcheado Y al sidecar: los tres tienen que llevar la misma o el script leeria None en todo.</summary>
        Public ApplyScriptSalt As String = ""
        Public ApplyScriptPluginFile As String = Nothing
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
        Public RunChargenBake As Func(Of UInteger, String, String, IProgress(Of SaveProgress), Task(Of (Success As Boolean, Skipped As Boolean, Bundle As NpcFaceGenPacker.BakedNpcBundle, FailureMessage As String, TexWarning As String)))

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

            ' Fase 4: bake de CharGen + pack BA2, en dos sub-fases. 4a) bake GL por NPC en el hilo de UI, que
            ' escribe los sueltos y junta un BakedNpcBundle por bake exitoso; 4b) UNA sola llamada PackBatch en
            ' un worker con todos los bundles. ArchivePackager sigue haciendo diff CRC32 por entrada, asi que la
            ' semantica de override se preserva; la ganancia es pasar de O(N^2) reescrituras del BA2 a O(K). En
            ' modo loose-only el delegate de pack no hace nada y los sueltos quedan en disco.
            ' Mark-to-delete: paths canonicos de FaceGen de cada NPC removido, para SACARLOS del set de BA2 (solo
            ' se tocan los archives propios; un path ausente es no-op). Se arma siempre, aun sin GenerateChargen,
            ' para que un save que solo borra igual limpie el BA2.
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
                ' NPCs whose NIF baked OK but at least one face texture (D/N/S) failed to encode/write. These
                ' STILL count as bakedOk (the NIF is valid) but surface a warning + the first cause, because
                ' they are exactly the NPCs the BA2 pack will later report as "files unaccounted for".
                Dim bakedTexWarn = 0
                Dim firstTexWarn As String = ""
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
                        If Not String.IsNullOrEmpty(bakeRes.TexWarning) Then
                            bakedTexWarn += 1
                            If firstTexWarn = "" Then firstTexWarn = bakeRes.TexWarning
                        End If
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

                ' Clean, sectioned block (one bullet per line) instead of a run-on sentence. Reads well in the
                ' plain MessageBox the caller shows and keeps bake / textures / pack visually separated.
                Dim sb As New System.Text.StringBuilder()
                sb.Append(vbCrLf & vbCrLf & "FaceGen")
                Dim okText = If(totalBakes = 1, $"{bakedOk} OK", $"{bakedOk}/{totalBakes} OK")
                sb.Append(vbCrLf & "  • Bake: " & okText &
                          If(bakedSkip > 0, $", {bakedSkip} skipped", "") &
                          If(bakedFail > 0, $", {bakedFail} failed", "") &
                          If(result.BakeCancelled, " (cancelled — remaining NPCs not baked)", ""))
                If bakedTexWarn > 0 Then
                    sb.Append(vbCrLf & $"  • ⚠ Textures: {bakedTexWarn} NPC{If(bakedTexWarn = 1, "", "s")} with missing face textures")
                    sb.Append(vbCrLf & "      " & firstTexWarn)
                End If
                If packSummary <> "" Then sb.Append(vbCrLf & "  • " & packSummary)
                result.ChargenSummary = sb.ToString()

                ' A texture failure (or a pack miss) leaves the NIF pointing at DDS that don't exist → broken
                ' face in-game. It doesn't fail the ESP write (already on disk), but it IS a warning, so flip
                ' the icon. bakedTexWarn is the root-cause signal; the pack "unaccounted" line is its echo.
                result.ChargenSuccess = (bakedFail = 0) AndAlso packSuccess AndAlso (bakedTexWarn = 0)
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
        If npc.HasFull Then checks.Add(("FULL (name)" & labelSuffix, npc.FullName))
        If npc.HasShortName Then checks.Add(("SHRT (short name)" & labelSuffix, npc.ShortName))
        If npc.HasActivateTextOverride Then checks.Add(("ATTX (activate text)" & labelSuffix, npc.ActivateTextOverride))

        For Each check In checks
            If Not String.IsNullOrEmpty(check.Value) AndAlso Not PluginEncodingSettings.CanEncodeTranslatableStrict(check.Value) Then
                Return $"Field {check.Field} contains characters that don't fit in the selected encoding." & vbCrLf & vbCrLf &
                       $"Value: ""{check.Value}""" & vbCrLf & vbCrLf &
                       "Those characters would be lost (replaced with '?'). Choose UTF-8 (recommended) " &
                       "or an encoding that covers the name's alphabet, and save again."
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
        ' CLFM colour records: preserved ones from a prior save (Phase 2a) + the ones materialized for the SSE
        ' RaceMenu hair tint (Phase 2h). SKYRIM-ONLY by construction — see MaterializeSseHairColors.
        Dim clfmEntries As New List(Of SaveNpcEspWriter.ClfmRecordEntry)
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
            Dim skipMasterIndex = PluginManager.BuildMasterIndex(reader.Masters)
            For Each npcInput In inputs
                Dim localFid As UInteger = 0UI
                Dim mapRes = ctx.PluginManager.TryMapGlobalToFileLocal(
                    npcInput.NpcFormID, skipMasterIndex, reader.Masters.Count, reader.FileName, localFid)
                If mapRes = PluginManager.FileLocalMapResult.Ok Then
                    skipLocalFormIDs.Add(localFid)
                    Continue For
                End If
                ' ⛔ NO se anota nada. El master de origen de este NPC todavia no esta en la MAST del
                ' destino (se suma recien en ESTE guardado, Paso 2b del writer), asi que el archivo NO
                ' PUEDE contener una copia suya y no hay nada que descartar.
                ' El codigo viejo caia a SELF (reader.Masters.Count) para este caso, que es la respuesta
                ' correcta SOLO cuando el dueno ES el archivo destino. Con un master ausente producia un
                ' FormID local que colisiona con los records PROPIOS del destino: los drafts de la app
                ' arrancan en 0x800 (PluginWriter.NEXT_OBJECT_ID_DEFAULT) y los records nuevos de
                ' cualquier mod tambien, por la convencion del CK (xEdit TwbFile.NewFormID,
                ' wbImplementation.pas:5092). El record colisionado se saltaba en el barrido de
                ' preservacion de abajo y DESAPARECIA del plugin, sin aviso. Medido: un outfit creado en
                ' una sesion previa se perdia al guardar por primera vez un NPC de un mod nuevo.
                Logger.LogLazy(Function() $"[SAVE] NPC {npcInput.NpcFormID:X8}: su master de origen no esta " &
                                          $"en la MAST de '{reader.FileName}' todavia, asi que el archivo no " &
                                          "puede tener una copia previa; no se descarta ningun record.")
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
                ' CLFM authored by a PRIOR save (an SSE hair colour materialized from a RaceMenu preset) —
                ' preserved as an OVERRIDE entry, same as OTFT/ARMO/MSWP. Two reasons this is mandatory:
                ' (1) without it the record would fall through to SerializeExistingRecord, which only handles
                '     NPC_ and throws; (2) dropping it would leave every NPC_.HCLF that points at it DANGLING.
                ' It also feeds the reuse index below, so a re-save re-points at the SAME FormID instead of
                ' minting a new colour record on every save.
                If rec.Header.Signature = "CLFM" Then
                    ' ⭐ CNAM/FNAM se copian de los BYTES del record fuente, sin pasar por ParseCLFM. Un camino de
                    ' PRESERVACIÓN no puede depender de cómo se interprete el CNAM (en FO4 es una unión decidida
                    ' por FNAM; en Skyrim siempre RGBA): si la interpretación fallara, la re-escritura le cambiaría
                    ' el color al record en vez de preservarlo. Copiando los 4 bytes el round-trip es exacto por
                    ' construcción, sea cual sea el juego y venga de donde venga el record.
                    ' ⭐ El FULL (nombre visible) va por el MISMO criterio: los BYTES del payload tal cual,
                    ' sin decodificar-y-re-encodear. Es un lstring cpTranslate, así que el round-trip por
                    ' string lo re-escribiría con el Translatable global actual — lossy si el record se
                    ' autoró bajo otro codepage, y con ExceptionFallback podría TIRAR a mitad del write sin
                    ' que el pre-chequeo de Phase 2b lo cubra (ése sólo camina FULL/SHRT/ATTX de NPC_).
                    Dim clfmEdid As String = rec.EditorID
                    Dim clfmFullRaw As Byte() = Nothing
                    Dim clfmRgb As Integer = 0
                    Dim clfmAlpha As Byte = 0
                    Dim clfmFlags As UInteger = 0UI
                    For Each sr In rec.Subrecords
                        If sr.Signature = "CNAM" AndAlso sr.Data IsNot Nothing AndAlso sr.Data.Length >= 4 Then
                            clfmRgb = (CInt(sr.Data(0)) << 16) Or (CInt(sr.Data(1)) << 8) Or CInt(sr.Data(2))
                            clfmAlpha = sr.Data(3)
                        ElseIf sr.Signature = "FNAM" AndAlso sr.Data IsNot Nothing AndAlso sr.Data.Length >= 4 Then
                            clfmFlags = BitConverter.ToUInt32(sr.Data, 0)
                        ElseIf sr.Signature = "FULL" AndAlso sr.Data IsNot Nothing AndAlso clfmFullRaw Is Nothing Then
                            clfmFullRaw = CType(sr.Data.Clone(), Byte())
                        End If
                    Next
                    clfmEntries.Add(New SaveNpcEspWriter.ClfmRecordEntry With {
                        .FormID = ctx.PluginManager.ResolveReferencedFormID(rec.SourcePluginName, rec.Header.FormID),
                        .EditorID = clfmEdid,
                        .FullNameRaw = clfmFullRaw,
                        .ColorRgb = clfmRgb,
                        .ColorAlpha = clfmAlpha,
                        .Flags = clfmFlags,
                        .IsOverride = True,
                        .OriginalVcs1 = rec.Header.VCS1,
                        .OriginalVcs2 = rec.Header.VCS2})
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
            Dim existingConflict = FindEncodingConflict(parsedExisting, $" of NPC [{label}]")
            If existingConflict <> "" Then Throw New InvalidDataException(existingConflict)
        Next

        ' Phase 2b': refrescar el VMAD de los NPC del plugin que NO entran en este guardado, para que su
        ' payload quede en la MISMA generación que el .pex que se instala. Va DESPUÉS del chequeo de encoding
        ' de arriba a propósito: así los records que se mueven a `entries` ya pasaron por él.
        Dim refreshedVmadFormIDs = RefreshPreservedApplyScripts(existingRecords, entries, ctx, target)

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

        ' Fases 2e/2f/2g: drafts de ARMA/ARMO/MSWP que necesita este guardado. Se emite la CLAUSURA TRANSITIVA
        ' del grafo de dependencias de armadura para que un outfit o una piel guardados sean autocontenidos y no
        ' queden referencias provisionales colgadas. El recorrido espeja al de la fase 2d (Queue + HashSet,
        ' a prueba de ciclos) pero sobre tres tipos de record:
        '   ARMO --ArmorAddons[].ArmaFormID--> ARMA --{Male,Female}MaterialSwapFormID--> MSWP
        '     \--{Male,Female}MaterialSwapFormID--------------------------------------------/
        ' Las RAICES son los ARMO draft referenciados por un OTFT emitido, por una entrada de leveled list o por
        ' el WNAM de un NPC guardado, mas los ARMO draft sucios que ademas esten referenciados: un draft sucio y
        ' huerfano NO se arrastra, para que no infle el plugin. El writer pre-asigna a cada draft su FormID
        ' self-index real, asi que las referencias cruzadas resuelven sin importar el orden de emision.
        ' Unicidad de EDID: cada tipo de record tiene su propio set (en xEdit son namespaces distintos por
        ' signature), sembrado con los OVERRIDE preservados de ese tipo para que un draft NUEVO no colisione con
        ' un record preservado. Los drafts override conservan su EDID verbatim.
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

        ' Phase 2i: materialize the SSE RaceMenu hair tint into HCLF. SKYRIM-ONLY (see the method).
        ' Runs LAST of the entry-building phases and BEFORE the writer, because it mutates
        ' entry.Npc.HairColorFormID — which the writer's discovery pass sees when it walks the emitters,
        ' pulling the CLFM's defining plugin into the MAST list. It has to be set before that walk runs.
        MaterializeSseHairColors(game, entries, clfmEntries, ctx, espNameNoExt, target)

        Dim writeRes = SaveNpcEspWriter.SaveOverridePlugin(
            target.TargetPath, game, target.MarkAsMaster, target.LightMaster,
            entries, existingRecords, existingMasters, ctx.PluginManager, outfitEntries, leveledEntries,
            existingNextObjectId,
            armoEntries:=armoEntries, armaEntries:=armaEntries, mswpEntries:=mswpEntries,
            clfmEntries:=clfmEntries)

        result.WriterResult = writeRes
        result.DraftFormIdMap = writeRes.DraftFormIdMap
        For Each existingRec In existingRecords
            result.SavedFormIDs.Add(existingRec.Header.FormID)
        Next
        ' Los preservados a los que se les refrescó el VMAD salieron de existingRecords y ahora viajan en
        ' `entries` — se siguen contando como guardados igual (Phase 2b').
        For Each refreshedFid In refreshedVmadFormIDs
            result.SavedFormIDs.Add(refreshedFid)
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
                ' La generacion usada en ESTE guardado queda en el sidecar: es de donde sale la proxima.
                If ctx.ApplyScriptGeneration > 0 Then
                    mergedSidecar.PayloadGeneration = ctx.ApplyScriptGeneration
                    mergedSidecar.PayloadSalt = ctx.ApplyScriptSalt
                End If
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
            ' â›” EL .ini NO SE TOCA NUNCA POR LA RUTA DE ENTREGA. El script GANA por construccion, corra antes o
            ' despues que BodyGen, en los DOS juegos: si el script va primero, el actor queda con morphs y
            ' BodyGen se saltea por su gate de "no tiene morphs"; si va primero BodyGen, el .psc barre su key
            ' antes de aplicar la nuestra. No hace falta borrar ni excluir nada del .ini para que el resultado
            ' sea determinista.
            ' â›” Y NO SE DEBE: el .ini es por PLUGIN y lista TODOS sus NPC. Un NPC que el usuario no re-grabo
            ' conserva su VMAD viejo, el script le llega INERTE (sus properties no existen en el .pex nuevo) y el
            ' .ini es su UNICA via; una version anterior borraba el par completo y le cortaba la entrega a todos
            ' ellos. Ademas funciona como red: sin SKSE/F4SE o con VMAD viejo, BodyGen sigue entregando.
            If target.EmitBodyGen OrElse (removedFromSidecar AndAlso iniExists) Then
                ReportPhase(progress, "Writing BodyGen .ini…", IO.Path.GetFileName(target.TargetPath))
                EmitBodyGenFromSidecar(target, mergedSidecar, ctx)
            End If
        End If

        ' Install the compiled apply-script, but ONLY if a record actually references it (ctx.WroteApplyScript
        ' is set per-NPC in BuildOverrideEntry). Game-aware: NPCM_Manolov_ApplySSE.pex for Skyrim,
        ' NPCM_Manolov_ApplyFO4.pex for FO4 — both into Data\Scripts\. NEVER the native stubs
        ' (NiOverride/Overlays/BodyGen): a loose .pex shadows the BSA/BA2, so shipping our transcribed stub
        ' would replace RaceMenu's/LooksMenu's real implementation. See Papyrus\README.md.
        If ctx.WroteApplyScript Then
            ReportPhase(progress, "Writing helper script…", IO.Path.GetFileName(target.TargetPath))
            Dim installed = NpcApplyScriptEmitter.InstallPex(ctx.DataPath, Config_App.Current.Game,
                                                             ctx.ApplyScriptPluginFile, ctx.ApplyScriptGeneration, ctx.ApplyScriptSalt)
            If installed Is Nothing Then
                ' The VMAD references a script whose .pex we could not ship — the engine would log a missing
                ' script and apply nothing. Loud, because the plugin is otherwise silently half-broken.
                Throw New IO.FileNotFoundException(
                    "The NPC records reference our Papyrus helper script, but its compiled .pex is not embedded " &
                    "in this build. Re-run the Papyrus compile step so the .pex exists before building (see " &
                    "Papyrus\README.md), or untick ""Attach the helper script"" in the Save dialog.")
            End If
        End If

        result.VerifierIcon = MessageBoxIcon.Information
        result.ChargenSuccess = True

        ' ⛔ LOS RECORTES DEL PAYLOAD SE MUESTRAN, no se entierran en fo4lib.log. Un payload recortado se ve
        ' EXACTAMENTE igual que uno completo desde afuera: sin este aviso, "se aplicó todo" sería mentira y
        ' nadie se enteraría. Se listan hasta 8 y se cuenta el resto, para que el MessageBox siga siendo legible.
        If ctx.PayloadWarnings.Count > 0 Then
            Dim shown = ctx.PayloadWarnings.Take(8).ToList()
            Dim extra = ctx.PayloadWarnings.Count - shown.Count
            result.VerifierSummary &= vbCrLf & vbCrLf &
                "⚠ Apply-script payload:" & vbCrLf & "  • " & String.Join(vbCrLf & "  • ", shown) &
                If(extra > 0, vbCrLf & $"  • (+{extra} more — see fo4lib.log)", "")
            result.VerifierIcon = MessageBoxIcon.Warning
        End If
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

        ' Phase 1a'': attach (or strip) our Papyrus apply-script on the NPC_'s VMAD, so the engine applies on
        ' FIRST SPAWN the RaceMenu/LooksMenu options with no other delivery route — overlays, skin overrides,
        ' and (SSE only) node transforms. Runs AFTER the round-trip copy, which is what put the source record's
        ' VMAD on the shadow: NpcVmadBuilder.UpsertScript preserves every vanilla / other-mod script byte-for-byte
        ' and rewrites only ours, so this is idempotent across repeated saves. Unchecking the option strips a
        ' previously-emitted script instead of leaving it stale. True no-op for an NPC with nothing to apply.
        Dim lmPreset As LooksmenuLoader.LooksmenuPreset = Nothing
        ctx.AppliedPresets?.TryGetValue(npcFormID, lmPreset)
        EnsureApplyScriptGeneration(ctx, target)
        ' ownBodyMorphs: el script es dueño de los body morphs SÓLO en la ruta ApplyScript. Viaja al .psc como
        ' MorphsOwned, y no es un simple "no emitir": en FO4 nuestro barrido usa el keyword None, que es EL
        ' MISMO SLOT que escribe BodyGen, así que con la ruta .ini el script tiene que quedarse quieto.
        Dim applyWarnings As New List(Of String)
        If NpcApplyScriptEmitter.ApplyToNpc(npcSpec, lmPreset, Config_App.Current.Game,
                                            target.EmitApplyScript,
                                            ctx.ApplyScriptPluginFile, ctx.ApplyScriptGeneration, ctx.ApplyScriptSalt,
                                            target.ScriptOwnsBodyMorphs, applyWarnings) Then
            ctx.WroteApplyScript = True   ' at least one NPC carries it → the .pex must be installed
        End If
        Dim applyLabel = NpcLabel(npcSpec, npcFormID)
        CollectPayloadWarnings(ctx, applyLabel, applyWarnings)
        CheckVmadSize(npcSpec, applyLabel, ctx)

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
    ''' Byte-array fields (Unknown18/TrailingBytes) are emit-only, so sharing the references is safe.
    ''' EVERY field must be copied, including the game's OTHER layout: ACBS is game-aware (FO4 20B has
    ''' XpValueOffset; SSE 24B has Magicka/Stamina/Health offsets + SpeedMultiplier), and a field left out
    ''' here is written to the ESP as 0 — e.g. a Skyrim NPC would lose its Speed Multiplier (usually 100).
    ''' Same bug class as the DnamRawSse shadow-drop (see MainForm.CopyRoundTripOnlyFieldsFromRaw).</summary>
    Private Function CloneAcbsWithFlags(src As NPC_AcbsData, flags As UInteger) As NPC_AcbsData
        Return New NPC_AcbsData With {
            .Flags = flags,
            .XpValueOffset = src.XpValueOffset,
            .MagickaOffset = src.MagickaOffset,
            .StaminaOffset = src.StaminaOffset,
            .LevelOrLevelMult = src.LevelOrLevelMult,
            .CalcMinLevel = src.CalcMinLevel,
            .CalcMaxLevel = src.CalcMaxLevel,
            .SpeedMultiplier = src.SpeedMultiplier,
            .DispositionBase = src.DispositionBase,
            .TemplateFlags = src.TemplateFlags,
            .HealthOffset = src.HealthOffset,
            .BleedoutOverride = src.BleedoutOverride,
            .Unknown18 = src.Unknown18,
            .TrailingBytes = src.TrailingBytes
        }
    End Function

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

        ' Fold any row this NPC still has under an older identifier form onto the canonical key, so the
        ' write below replaces it instead of adding a second row. The rule lives next to BuildIdentifier
        ' because it IS the same law (what the key means), and keeping it there is what makes it testable.
        BssliderSidecar.FoldLegacyKeys(merged.Npcs, identifier, masterName, npcFormID)

        ' Overlay → entry via the ONE preset↔entry mirror (BssliderSidecar.EntryFromPreset). The field
        ' list used to be duplicated here and in HydratePresets/StripEspFieldsFromOverlay, and the copies
        ' drifted (2026-07-16 audit: the residual was missing 5 fields, so a second Save wiped them).
        Dim overlay As LooksmenuLoader.LooksmenuPreset = Nothing
        ctx.AppliedPresets.TryGetValue(npcFormID, overlay)
        Dim entry = BssliderSidecar.EntryFromPreset(overlay,
                                                    If(npcSpec.EditorID, ""),
                                                    If(npcSpec.IsFemale, "female", "male"))

        ' Always overwrite the NPC's slot — even if entry ends up empty. Write() drops empty entries
        ' so a clear-then-save round trip removes the row instead of leaving stale data on disk.
        merged.Npcs(identifier) = entry
    End Sub

    ''' <summary>SKYRIM-ONLY. Materializa el tinte de pelo absoluto de RaceMenu (el RGB empaquetado que el
    ''' overlay lleva en <c>SseHairColorRgb</c>) en un record <b>CLFM</b> real y apunta el <b>HCLF</b> del NPC a
    ''' el. Corre despues de todas las fases de armado y antes del writer.
    ''' <para><b>Por que un record y no el apply-script.</b> skee solo escribe el RGB del preset sobre el
    ''' material de pelo VIVO, nunca al record; el JUEGO, en cambio, empuja el color del CLFM sobre TODO material
    ''' HairTint del 3D del actor en cada update, y skee hookea justamente esa funcion. O sea que un color
    ''' horneado en el FaceGeom o puesto por node override pelea contra el motor, mientras que el CLFM es el
    ''' valor que el motor lee. RaceMenu opina igual: su API de aplicar preset toma un BGSColorForm y llama
    ''' SetHairColor.</para>
    ''' <para><b>Por que solo Skyrim.</b> Un CLFM de Skyrim lleva un RGB real, asi que un color arbitrario mapea
    ''' 1:1 a un record nativo. Un CLFM de pelo de FO4 lleva un RemappingIndex (una fila de LUT): un RGB
    ''' empaquetado no significa nada ahi.</para>
    ''' <para><b>Politica de reuso (sin masters nuevos).</b> Referenciar un record de otro plugin lo convierte en
    ''' MASTER del ESP de salida, asi que reusar un CLFM de un mod cualquiera agregaria una dependencia dura solo
    ''' para escribir un color. El reuso se limita a fuentes que no agregan dependencia: los CLFM ya emitidos en
    ''' ESTE plugin (mantiene estable el FormID entre re-saves) y el master del juego, que ya es master de
    ''' cualquier override de NPC y cubre el caso comun de los 15 colores vanilla. Cualquier otro caso emite un
    ''' CLFM propio.</para></summary>
    Private Sub MaterializeSseHairColors(game As Config_App.Game_Enum,
                                         entries As List(Of SaveNpcEspWriter.NpcOverrideEntry),
                                         clfmEntries As List(Of SaveNpcEspWriter.ClfmRecordEntry),
                                         ctx As SaveContext,
                                         espNameNoExt As String,
                                         target As SaveEsp_Form.SaveTarget)
        If game <> Config_App.Game_Enum.Skyrim Then Return
        If entries Is Nothing OrElse entries.Count = 0 Then Return
        ' Nothing to do unless some NPC in this save actually carries a preset hair colour.
        If Not entries.Any(Function(e) e.Npc IsNot Nothing AndAlso e.Npc.SseHairColorRgb.HasValue) Then Return

        ' --- Reuse index, built in priority order (later Add is a no-op for an already-known colour). ---
        Dim byRgb As New Dictionary(Of Integer, UInteger)
        ' (1) CLFMs already bound to THIS plugin (preserved from a prior save). Their FormID is the value the
        '     NPCs pointed at last time, so re-using it makes a re-save a no-op instead of a new record.
        For Each ce In clfmEntries
            If Not byRgb.ContainsKey(ce.ColorRgb) Then byRgb(ce.ColorRgb) = ce.FormID
        Next
        ' (2) The game master. Adds no dependency (an NPC override always masters Skyrim.esm) and covers the
        '     15 vanilla hair colours, which is what a preset exported from a vanilla-coloured NPC carries.
        Dim gameMasterName = SaveNpcEspWriter.MasterFileNamePublic(game)
        Dim allClfm = ctx.PluginManager.GetRecordsOfType("CLFM")
        If allClfm IsNot Nothing Then
            For Each rec In allClfm
                If Not String.Equals(ctx.PluginManager.GetOriginatingPluginName(
                        ctx.PluginManager.ResolveReferencedFormID(rec.SourcePluginName, rec.Header.FormID)),
                        gameMasterName, StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim parsed = RecordParsers.ParseCLFM(rec, ctx.PluginManager)
                If parsed Is Nothing OrElse Not parsed.HasColor Then Continue For
                Dim rgb = (CInt(parsed.Color.R) << 16) Or (CInt(parsed.Color.G) << 8) Or CInt(parsed.Color.B)
                If Not byRgb.ContainsKey(rgb) Then
                    byRgb(rgb) = ctx.PluginManager.ResolveReferencedFormID(rec.SourcePluginName, rec.Header.FormID)
                End If
            Next
        End If

        ' --- Provisional FormID allocator for the drafts we mint (shared counter, no cross-draft collision). ---
        Dim fallbackCtr As UInteger = &HFF0C0000UI
        Dim allocProvisional As Func(Of UInteger) =
            Function() As UInteger
                If ctx.AllocateDraftFormID IsNot Nothing Then Return ctx.AllocateDraftFormID()
                fallbackCtr += 1UI
                Return fallbackCtr
            End Function
        Dim usedEdids As New HashSet(Of String)(clfmEntries.Select(Function(x) If(x.EditorID, "")), StringComparer.OrdinalIgnoreCase)

        For Each entry In entries
            Dim npc = entry.Npc
            If npc Is Nothing OrElse Not npc.SseHairColorRgb.HasValue Then Continue For
            Dim rgb = npc.SseHairColorRgb.Value And &HFFFFFF

            Dim fid As UInteger = 0UI
            If Not byRgb.TryGetValue(rgb, fid) Then
                ' No dependency-free match — mint our own, one per distinct colour in this save. EDID is
                ' namespaced by ESP like every other record we author, so two plugins can't collide.
                Dim finalEdid = MakeUniqueEditorId(ApplyEspNamespaceToEditorId($"npcm_HairColor_{rgb:X6}", espNameNoExt), usedEdids)
                ' FULL: nombre legible para que el record se lea como algo y no como un EditorID crudo — en el
                ' combo de Edit Face (que prefiere FullName), en xEdit y en el CK. ⛔ ASCII PURO y SIN el nombre
                ' del ESP: el FULL se encodea con EncodeTranslatable (ExceptionFallback) y este record NO entra
                ' en el chequeo de conflicto de encoding de Phase 2b, así que un carácter que el codepage
                ' elegido no represente tiraría a mitad del guardado. El namespacing por ESP ya lo lleva el EDID.
                Dim finalFull = $"NPC Manager custom hair color #{rgb:X6}"
                fid = allocProvisional()
                clfmEntries.Add(New SaveNpcEspWriter.ClfmRecordEntry With {
                    .FormID = fid,
                    .EditorID = finalEdid,
                    .FullName = finalFull,
                    .ColorRgb = rgb,
                    .ColorAlpha = 0,          ' measured: 178/178 CLFM in Skyrim.esm carry alpha 0
                    .Flags = 1UI,             ' measured: the 15 vanilla hair colours carry FNAM=1 (Playable)
                    .IsOverride = False})
                byRgb(rgb) = fid
                Logger.LogLazy(Function() $"[SAVE] SSE hair colour 0x{rgb:X6} → NEW CLFM '{finalEdid}' (provisional 0x{fid:X8}).")
            Else
                Dim fidL = fid
                Logger.LogLazy(Function() $"[SAVE] SSE hair colour 0x{rgb:X6} → reusing CLFM 0x{fidL:X8}.")
            End If

            ' HCLF. HasHairColor must be DERIVED from the value we just resolved, never copied from the raw
            ' record: an NPC whose base record had no HCLF would otherwise keep the flag False and the writer
            ' would silently drop the subrecord (NpcSubrecordWriter.vb:80). Same rule the FTST/DOFT/SOFT
            ' fields already follow.
            npc.HairColorFormID = fid
            npc.HasHairColor = True
        Next
    End Sub

    ''' <summary>Etiqueta legible de un NPC para los avisos: EditorID si lo hay, si no el FormID.</summary>
    Private Function NpcLabel(npcSpec As NPC_Data, formID As UInteger) As String
        If npcSpec IsNot Nothing AndAlso Not String.IsNullOrEmpty(npcSpec.EditorID) Then Return npcSpec.EditorID
        Return $"FormID {formID:X8}"
    End Function

    ''' <summary>Vuelca los avisos que el emisor dejó para UN NPC al acumulador del guardado, prefijados con su
    ''' nombre. El emisor no conoce al NPC a propósito: emite el hecho ("12 node transform(s) DESCARTADO(S)…")
    ''' y acá se le pone el apellido.</summary>
    Private Sub CollectPayloadWarnings(ctx As SaveContext, label As String, warnings As List(Of String))
        If warnings Is Nothing OrElse warnings.Count = 0 Then Return
        For Each w In warnings
            ctx.PayloadWarnings.Add($"{label}: {w}")
            ' ⛔ Y AL LOG, porque el MessageBox lista sólo los primeros 8 y remite el resto a "see fo4lib.log" —
            ' donde NO estaban: ningún uso de PayloadWarnings escribía una sola línea. Mientras los avisos eran
            ' raros (recortes por el tope de 128 elementos del VMAD) el faltante no se notaba; con el descarte de
            ' magic overlays fuera de rango, un batch de presets importados llena los 8 cupos y manda el resto a
            ' un archivo vacío. Un mensaje que promete un lugar tiene que dejar algo ahí.
            Dim line = w
            Dim who = label
            Logger.LogLazy(Function() $"[PAYLOAD] {who}: {line}")
        Next
    End Sub

    ''' <summary>⛔ TECHO DURO DEL VMAD. El campo de longitud de un subrecord es u16 y la lib no implementa la
    ''' extensión XXXX, así que <c>PluginWriter.WriteSubrecordHeader</c> tira si se pasa — pero su mensaje NO
    ''' dice de qué NPC se trata, y con cientos de records eso es indiagnosticable. Acá se chequea POR NPC,
    ''' apenas se le arma el VMAD, para poder nombrarlo.
    '''
    ''' <para>Referencia medida (2026-07-28): un NPC con 2 overlays + 1 skin + 1 node + 22 morphs pesa 1622 B,
    ''' o sea 2,5 % del techo. Lo que empuja el tamaño son los PATHS DE TEXTURA de overlays y skin, no los
    ''' morphs (~22 B por morph).</para></summary>
    Private Sub CheckVmadSize(npcSpec As NPC_Data, label As String, ctx As SaveContext)
        If npcSpec Is Nothing OrElse npcSpec.Vmad Is Nothing OrElse npcSpec.Vmad.RawBytes Is Nothing Then Return
        Dim n = npcSpec.Vmad.RawBytes.Length
        If n > NpcApplyScriptEmitter.VmadHardLimitBytes Then
            Throw New IO.InvalidDataException(
                $"The VMAD of NPC [{label}] is {n} bytes, over the {NpcApplyScriptEmitter.VmadHardLimitBytes}-byte " &
                "subrecord limit. Remove some of its overlays / skin overrides / node transforms " &
                "(texture paths are what weighs most) and save again.")
        End If
        ' Warn at 90%: leaves room to react before a save actually fails.
        If n > (NpcApplyScriptEmitter.VmadHardLimitBytes * 9) \ 10 Then
            ctx.PayloadWarnings.Add($"{label}: VMAD is {n} bytes, close to the {NpcApplyScriptEmitter.VmadHardLimitBytes}-byte limit")
        End If
    End Sub

    ''' <summary>Resuelve la generación del payload del apply-script para ESTE guardado, UNA sola vez. Sale del
    ''' sidecar (que es su fuente de verdad) o del override manual del diálogo. Idempotente: la primera llamada
    ''' la fija y el resto son no-op, así los NPC del guardado y los PRESERVADOS caen todos en el MISMO número
    ''' — que es justamente lo que hace que un solo <c>.pex</c> los pueda servir a todos.</summary>
    Private Sub EnsureApplyScriptGeneration(ctx As SaveContext, target As SaveEsp_Form.SaveTarget)
        If ctx.ApplyScriptGeneration > 0 Then Return
        Dim pluginFile = IO.Path.GetFileName(target.TargetPath)
        Dim prevSidecar = BssliderSidecar.Read(BssliderSidecar.BuildPath(target.TargetPath))
        Dim sidecarGen = If(prevSidecar Is Nothing, 0, prevSidecar.PayloadGeneration)

        ' â›”â›” EL CONTADOR NUNCA PUEDE RETROCEDER, Y EL SIDECAR SOLO NO ALCANZA PARA GARANTIZARLO.
        ' Reusar una generacion ya publicada es la PEOR falla de este esquema: el savegame del jugador ya tiene
        ' variables con esos nombres, asi que las restaura RANCIAS y le ganan al VMAD. El actor aplica fielmente
        ' el payload VIEJO, sin un solo error en ningun log.
        ' Medido, y provocado restaurando un backup del .bssliders sin caer en que el sidecar es TAMBIEN el hogar
        ' del contador: volvio atras, el guardado siguiente reemitio la misma generacion y el NPC quedo con el
        ' payload de la corrida anterior mientras el script se veia perfecto. Al usuario le pasa igual con un
        ' backup, con un mod manager, o borrando el sidecar.
        ' Por eso el piso sale del MAXIMO entre el sidecar y la generacion que declara el .pex YA INSTALADO: ese
        ' .pex es testigo confiable porque se instala en el MISMO Save ESP que emitio el VMAD.
        Dim installedGen = ReadInstalledPexGeneration(ctx.DataPath, Config_App.Current.Game, pluginFile)
        Dim floorGen = Math.Max(sidecarGen, installedGen)

        If target.ScriptVersionOverride > 0 Then
            ' Forzada a mano desde el dialogo: gana, porque para eso existe.
            '
            ' ⭐ AVISO CORREGIDO 2026-07-28. Antes decía "los actores conservarán el payload VIEJO", y eso era
            ' cierto SÓLO antes de que existiera la sal. Ahora el sufijo es <contador><sal>, así que reusar el
            ' número NO reusa el nombre: la sal se sortea igual y las properties siguen siendo inéditas para el
            ' savegame. MEDIDO: forzar la versión 5 sobre una 5 ya publicada aplicó perfecto, porque el sufijo
            ' pasó de `_G000005` a `_G000005C6BC`.
            ' Se sigue avisando porque hay algo real que se pierde —el número deja de indicar el orden de
            ' publicación— pero el texto ya no puede afirmar una pérdida de datos que no ocurre.
            ctx.ApplyScriptGeneration = target.ScriptVersionOverride
            If target.ScriptVersionOverride <= floorGen Then
                ctx.PayloadWarnings.Add(
                    $"Forced script version {target.ScriptVersionOverride} does not advance past the last " &
                    $"published generation ({floorGen}). The payload still reaches actors — the random salt " &
                    "keeps the property names unique — but the version number no longer reflects publish order.")
            End If
        Else
            ctx.ApplyScriptGeneration = PexPatcher.NextGeneration(floorGen)
            If Logger.Enabled AndAlso installedGen > sidecarGen Then
                Logger.LogLazy(Function() $"[NPCM-APPLY] generation floor came from the installed .pex ({installedGen}) — the sidecar said {sidecarGen}; a rollback was prevented")
            End If
        End If
        ' ⭐ SEGUNDA LINEA DE DEFENSA. El piso de arriba depende de que sobreviva el sidecar o el .pex; la sal
        ' no depende de NADA: 4 hex sorteados por guardado hacen que el nombre sea distinto aunque el numero se
        ' repita, y un nombre nuevo el savegame NO lo tiene ⇒ llega fresco del VMAD.
        ctx.ApplyScriptSalt = PexPatcher.NewSalt()
        ctx.ApplyScriptPluginFile = pluginFile
    End Sub

    ''' <summary>Generación que declara el <c>.pex</c> ya instalado de ESTE plugin, o 0 si no hay ninguno /
    ''' no se puede leer. Es el testigo que impide que el contador retroceda cuando el sidecar se pierde,
    ''' se restaura de un backup o lo administra un mod manager. Nunca tira: un <c>.pex</c> ilegible sólo
    ''' significa "sin piso extra", que es el comportamiento de antes.</summary>
    Private Function ReadInstalledPexGeneration(dataPath As String, game As Config_App.Game_Enum, pluginFileName As String) As Integer
        If String.IsNullOrEmpty(dataPath) Then Return 0
        Try
            Dim path = IO.Path.Combine(dataPath, "Scripts",
                                       NpcApplyScriptEmitter.ScriptNameFor(game, pluginFileName) & ".pex")
            If Not IO.File.Exists(path) Then Return 0
            Dim g = PexPatcher.ReadGeneration(IO.File.ReadAllBytes(path))
            Return If(g < 0, 0, g)
        Catch
            Return 0
        End Try
    End Function

    ''' <summary>â›” RE-EMITE EL VMAD DE LOS NPC DEL PLUGIN QUE **NO** ENTRAN EN ESTE GUARDADO.
    ''' <para><b>El problema, medido.</b> El <c>.pex</c> es UNO por plugin y declara UNA sola generacion de
    ''' properties (<c>_G&lt;n&gt;</c>), que sube en cada Save ESP. Pero solo se reescribia el VMAD de los NPC
    ''' incluidos en el guardado: los demas quedaban con su VMAD viejo, nombrando properties que el .pex nuevo YA
    ''' NO DECLARA. Resultado: no les llega ninguna property, sus arrays vienen en longitud 0, corta el guard de
    ''' instancia huerfana y el actor queda INERTE - sin overlays, sin skin, sin transforms y sin body morphs. O
    ''' sea, cada guardado dejaba atras a todos los NPC que no tocaste.</para>
    ''' <para><b>Como.</b> Los NPC_ preservados no se copian byte a byte: el writer hace ParseNPC +
    ''' NpcSubrecordWriter completo. Aca se hace exactamente lo mismo un paso antes -parsear, refrescar el VMAD y
    ''' mandarlo por <c>entries</c>- y termina en el MISMO serializador. El payload se reconstruye desde el
    ''' sidecar via <see cref="BssliderSidecar.HydratePresets"/>, el unico espejo entry-preset del proyecto.</para>
    ''' <para>De paso migra el nombre LEGADO al nombre por plugin, porque <c>ApplyToNpc</c> ya hace el upsert en
    ''' dos pasadas.</para>
    ''' <para>â›” SIN SIDECAR NO SE TOCA EL RECORD: si un NPC lleva nuestro script pero no tiene entrada, no hay
    ''' con que reconstruir su payload, y ApplyToNpc con preset Nothing emitiria el spec de LIMPIEZA, que le
    ''' BORRARIA sus overlays. Se lo deja inerte (que es lo que ya era) y se LOGUEA: perder la entrega es malo,
    ''' borrarle datos al usuario es peor.</para></summary>
    ''' <returns>FormIDs LOCALES de los records movidos a <paramref name="entries"/>, para que el caller los siga
    ''' contando como guardados.</returns>
    Private Function RefreshPreservedApplyScripts(existingRecords As List(Of PluginRecord),
                                                  entries As List(Of SaveNpcEspWriter.NpcOverrideEntry),
                                                  ctx As SaveContext,
                                                  target As SaveEsp_Form.SaveTarget) As List(Of UInteger)
        Dim moved As New List(Of UInteger)
        If Not target.EmitApplyScript OrElse existingRecords.Count = 0 Then Return moved

        ' Payload de cada NPC del plugin, reconstruido desde SU sidecar. Un solo espejo (HydratePresets).
        Dim sidecars As New Dictionary(Of String, BssliderSidecar.SidecarFile)(StringComparer.OrdinalIgnoreCase)
        Dim sc = BssliderSidecar.Read(BssliderSidecar.BuildPath(target.TargetPath))
        If sc IsNot Nothing Then sidecars(target.TargetPath) = sc
        Dim presetsByFid As New Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)
        BssliderSidecar.HydratePresets(sidecars, ctx.PluginManager, presetsByFid)

        Dim skipped As New List(Of String)
        ' Hacia atrás: se sacan elementos de existingRecords mientras se recorre.
        For i = existingRecords.Count - 1 To 0 Step -1
            Dim rec = existingRecords(i)
            If rec.Header.Signature <> "NPC_" Then Continue For

            Dim parsed = RecordParsers.ParseNPC(rec, rec.SourcePluginName, ctx.PluginManager)
            ' Sólo los que YA llevan script nuestro. A un NPC preservado sin script no se le agrega uno: no
            ' estaba en este guardado, así que el usuario no pidió nada sobre él.
            If Not NpcVmadBuilder.HasAppScript(parsed.Vmad) Then Continue For

            ' Mismo resolve que hace SerializeExistingRecord: el FormID del header viene LOCAL del reader y el
            ' remapper de abajo espera GLOBAL.
            Dim globalFid = ctx.PluginManager.ResolveReferencedFormID(rec.SourcePluginName, rec.Header.FormID)
            parsed.FormID = globalFid

            Dim preset As LooksmenuLoader.LooksmenuPreset = Nothing
            If Not presetsByFid.TryGetValue(globalFid, preset) OrElse preset Is Nothing Then
                skipped.Add($"{If(parsed.EditorID, "")} ({globalFid:X8})")
                Continue For          ' ⛔ sin datos NO se re-emite: ver el remark de arriba
            End If

            ' PEREZOSA a propósito: recién acá sabemos que hay algo que refrescar. Resolverla antes haría
            ' avanzar la generación (y crear un sidecar para guardarla) en un plugin sin scripts nuestros.
            ' Es idempotente, así que llamarla en cada vuelta no cambia el número.
            EnsureApplyScriptGeneration(ctx, target)

            Dim refreshWarnings As New List(Of String)
            If NpcApplyScriptEmitter.ApplyToNpc(parsed, preset, Config_App.Current.Game,
                                                target.EmitApplyScript,
                                                ctx.ApplyScriptPluginFile, ctx.ApplyScriptGeneration, ctx.ApplyScriptSalt,
                                                target.ScriptOwnsBodyMorphs, refreshWarnings) Then
                ctx.WroteApplyScript = True
            End If
            Dim refreshLabel = NpcLabel(parsed, globalFid)
            CollectPayloadWarnings(ctx, refreshLabel, refreshWarnings)
            CheckVmadSize(parsed, refreshLabel, ctx)

            ' ⚠️ SE APENDEA AL FINAL, NUNCA SE INSERTA. La fase 3b aparea entries(i) con writeInputs(i) POR
            ' ÍNDICE, así que los primeros writeInputs.Count elementos tienen que seguir siendo los del guardado.
            entries.Add(New SaveNpcEspWriter.NpcOverrideEntry With {
                .Npc = parsed,
                .SourcePluginName = rec.SourcePluginName,
                .OriginalHeader = rec.Header})
            existingRecords.RemoveAt(i)
            moved.Add(rec.Header.FormID)
        Next

        If Logger.Enabled Then
            Dim movedCount = moved.Count
            Logger.LogLazy(Function() $"[NPCM-APPLY] refreshed VMAD on {movedCount} preserved NPC(s) → generation {ctx.ApplyScriptGeneration}")
            If skipped.Count > 0 Then
                Dim list = String.Join(", ", skipped)
                Logger.LogLazy(Function() $"[NPCM-APPLY] WARNING: {skipped.Count} NPC(s) carry our script but have NO sidecar entry — left with their old VMAD (inert) rather than wiping their data → {list}")
            End If
        End If
        Return moved
    End Function

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
