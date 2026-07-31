Imports System.IO
Imports System.Text.Json
Imports FO4_Base_Library

''' <summary>Per-plugin JSON sidecar storing F4SE-only fields that have no ESP record
''' equivalent — currently BodySlide morph sliders, the LM SkinTemplate id, and the LM body
''' overlays (tattoos). One file per
''' plugin: <c>&lt;plugin&gt;.bssliders</c> next to <c>&lt;plugin&gt;.esp</c>. Read at preflight
''' when the plugin is selected; merged + re-written by NpcOverrideSaver on Save ESP.
'''
''' Schema (version 1):
''' <code>
''' {
'''   "version": 1,
'''   "plugin": "NPC_Manager.esp",
'''   "npcs": {
'''     "ABCDEF": { "editorId": "Cait",
'''                 "bodyMorphs": { "BigBelly": 0.45 },
'''                 "skinTemplateId": "Vanilla CBBE",
'''                 "overlays": [ { "template": "Tattoo01", "priority": 0,
'''                                 "tint": [1,0,0,1], "offsetUV": [0,0], "scaleUV": [1,1] } ] }
'''   }
''' }
''' </code>
''' Key of <c>npcs</c> = LooksMenu-style form identifier <c>"Master.esp|HEX6"</c> (master
''' plugin name + local 24-bit FormID in uppercase 6-digit hex). Same convention LM uses for
''' its preset JSONs, so <see cref="LooksmenuLoader.ResolveFormIdentifier"/> resolves it to a
''' global FormID when the master is loaded. This makes the sidecar robust to overrides of
''' NPCs from multiple masters in the same override plugin (e.g. an ESP overriding both
''' Fallout4.esm and DLCRobot.esm NPCs would otherwise collide on bare 6-digit hex).
'''
''' Only NPCs with a non-empty bodyMorphs dict OR a non-empty skinTemplateId OR a non-empty
''' overlays list are persisted; everything else is dropped to keep the file small and avoid
''' leaving zero-NPC sidecars on disk.</summary>
Public Module BssliderSidecar

    Public Const Extension As String = ".bssliders"
    ''' <summary>v12 agrego <c>payloadSalt</c>: la SAL del sufijo de generacion. Se persiste porque el
    ''' FomodExporter re-parchea el .pex desde el recurso EMBEBIDO usando la generacion del sidecar; sin la sal
    ''' sortearia otra y el .pex del paquete declararia nombres distintos a los del VMAD del ESP.</summary>
    Public Const SchemaVersion As Integer = 12

    Public Class SidecarFile
        Public Version As Integer = SchemaVersion
        Public Plugin As String = ""
        ''' <summary>Generacion del payload del apply-script (el sufijo _G###### de los nombres de property
        ''' en el VMAD). Vive ACA y no en el ESP porque el ESP solo lo lleva si algun NPC tiene el script: un
        ''' guardado sin overlays lo perderia. Sube en CADA Save ESP que emita el script, y por eso una
        ''' property con nombre nuevo le llega FRESCA del plugin a una instancia ya guardada en la partida
        ''' del jugador (ver Papyrus\GENERACION_DEL_PAYLOAD.md). 0 = todavia ninguna.</summary>
        Public PayloadGeneration As Integer = 0
        ''' <summary>Sal del sufijo <c>_G&lt;n&gt;&lt;sal&gt;</c> usada en el ultimo guardado. Vacia = formato viejo
        ''' (sin sal), que se sigue leyendo. Ver PexPatcher.NewSalt.</summary>
        Public PayloadSalt As String = ""
        Public Npcs As New Dictionary(Of String, NpcEntry)(StringComparer.OrdinalIgnoreCase)
    End Class

    Public Class NpcEntry
        Public EditorId As String = ""
        Public BodyMorphs As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>SSE-ONLY keyed body morphs: morph name → (BodySlide key → value). RaceMenu body
        ''' sliders carry one keyed contribution per BodySlide source; <see cref="BodyMorphs"/> is the
        ''' summed (netted) render input, this is the save source that round-trips to <c>.jslot</c>/BodyGen
        ''' without collapsing the keys. Nullable — Nothing on FO4 and on SSE entries without body morphs.</summary>
        Public BodyMorphsKeyed As Dictionary(Of String, Dictionary(Of String, Single)) = Nothing
        Public SkinTemplateId As String = ""
        ''' <summary>LM body overlays (tattoos) — NPC_Manager-internal persistence only (there is
        ''' no BodyGen/in-game file mechanism for overlays). Reuses the public
        ''' <see cref="LooksmenuLoader.OverlayEntry"/> so the in-memory overlay and the sidecar
        ''' share one type. Same on-disk shape as the LM preset overlay format (template +
        ''' priority always, optional tint[r,g,b,a]/offsetUV[x,y]/scaleUV[x,y]).</summary>
        Public Overlays As New List(Of LooksmenuLoader.OverlayEntry)
        ''' <summary>SSE-ONLY RaceMenu body overlays (path-based tattoos): node + diffuse/normal path + tint.
        ''' Distinct from the FO4 template-based <see cref="Overlays"/> (see
        ''' <see cref="LooksmenuLoader.LooksmenuPreset.SseBodyOverlays"/>). Nullable — Nothing on FO4 and on
        ''' SSE entries without overlays; serialized under the <c>sseBodyOverlays</c> key (schema v3).</summary>
        Public SseBodyOverlays As List(Of RaceMenuJslot.JslotOverlayNode) = Nothing
        ''' <summary>SSE-ONLY RaceMenu NiOverride node transforms (body-scale/position/rotation sliders): the full
        ''' per-node TRS (scale key 30, position key 31, rotation key 32 as axis-angle, scaleMode key 33). Nullable —
        ''' Nothing on FO4 / SSE entries without transforms; serialized under <c>sseNodeTransforms</c> (schema v10).
        ''' Superseded the scale-only <c>sseNodeScales</c> map (schema v4) so an edited position/rotation survives a
        ''' reload, not just the scale; a legacy <c>sseNodeScales</c> object is still read and migrated on load.</summary>
        Public SseNodeTransforms As List(Of RaceMenuJslot.JslotNodeTransform) = Nothing
        ''' <summary>SSE-ONLY RaceMenu absolute hair tint (packed 0xRRGGBB) from a loaded .jslot's actor.hairColor.
        ''' RaceMenu co-save data (not the NPC record) → persisted so the hair colour survives a reload. Nullable —
        ''' Nothing on FO4 / presets without hairColor; serialized under <c>sseHairColor</c> (schema v11).</summary>
        Public SseHairColorRgb As Integer? = Nothing
        ''' <summary>SSE-ONLY RaceMenu NiOverride SKIN overrides (body-paint per biped slot): slotMask +
        ''' diffuse/normal path + tint. Nullable — Nothing on FO4 / SSE entries without skin overrides;
        ''' serialized under <c>sseSkinOverrides</c> (schema v5). See
        ''' <see cref="LooksmenuLoader.LooksmenuPreset.SseSkinOverrides"/>.</summary>
        Public SseSkinOverrides As List(Of RaceMenuJslot.JslotSkinOverride) = Nothing
        ''' <summary>SSE-ONLY RaceMenu NiOverride CUSTOM face morphs (named chargen-TRI morphs from mods): name →
        ''' value. Not in the NPC record (RaceMenu co-save data) → persisted here so they auto-resolve after a
        ''' reload instead of needing a fresh .jslot load. Serialized under <c>sseCustomMorphs</c> (schema v6).</summary>
        Public SseCustomMorphs As List(Of NPC_CustomMorph) = Nothing
        ''' <summary>SSE-ONLY RaceMenu per-vertex head SCULPT deltas (index + dx/dy/dz, object space). Not in the
        ''' NPC record (RaceMenu co-save) → persisted here so the sculpt survives a reload. Serialized under
        ''' <c>sseSculpt</c> (schema v6).</summary>
        Public SseSculptHead As List(Of NPC_SculptVert) = Nothing
        ''' <summary>SSE-ONLY RaceMenu per-SHAPE sculpt blocks (head + brows + eyes + mouth), each tagged with its
        ''' Host chargen tri (HDPT NAM0=2). The full-fidelity superset of <see cref="SseSculptHead"/> (head-only):
        ''' render/bake route each block to its shape by Host so all four parts get their sculpt. Serialized under
        ''' <c>sseSculptParts</c> (schema v8). Absent = fall back to the head-only sseSculpt.</summary>
        Public SseSculptParts As List(Of NPC_SculptPart) = Nothing
        ''' <summary>SSE-ONLY RaceMenu per-layer CUSTOM tint mask texture override (tint layer index → texture path).
        ''' RaceMenu co-save data with no vanilla NPC record home (TINI/TINC/TINV/TIAS carry no path) → persisted here
        ''' so a custom warpaint/tattoo mask survives a reload. Serialized under <c>sseTintTextures</c> (schema v7).</summary>
        Public SseTintTexOverride As Dictionary(Of Integer, String) = Nothing
        ''' <summary>Optional gender hint: <c>"male"</c>, <c>"female"</c>, or empty (unknown).
        ''' Persisted alongside the sliders because the BodyGen emitter needs the gender to
        ''' filter <c>morphs.ini</c> rows, and at re-emit time the NPC's master plugin may not
        ''' be in the current load order (so we cannot re-derive it from the record). Empty =
        ''' BodyGen row written without a gender filter (engine applies to both).</summary>
        Public Gender As String = ""

        ''' <summary>True when this entry carries at least one slider or a non-empty template id.
        ''' Write() drops entries that don't satisfy this so the on-disk file never contains
        ''' rows that would be no-ops if re-applied.</summary>
        Public ReadOnly Property HasAnything As Boolean
            Get
                If BodyMorphs IsNot Nothing AndAlso BodyMorphs.Count > 0 Then Return True
                If BodyMorphsKeyed IsNot Nothing AndAlso BodyMorphsKeyed.Count > 0 Then Return True
                If Not String.IsNullOrEmpty(SkinTemplateId) Then Return True
                If Overlays IsNot Nothing AndAlso Overlays.Count > 0 Then Return True
                If SseBodyOverlays IsNot Nothing AndAlso SseBodyOverlays.Count > 0 Then Return True
                If SseNodeTransforms IsNot Nothing AndAlso SseNodeTransforms.Count > 0 Then Return True
                If SseHairColorRgb.HasValue Then Return True
                If SseSkinOverrides IsNot Nothing AndAlso SseSkinOverrides.Count > 0 Then Return True
                If SseCustomMorphs IsNot Nothing AndAlso SseCustomMorphs.Count > 0 Then Return True
                If SseSculptHead IsNot Nothing AndAlso SseSculptHead.Count > 0 Then Return True
                If SseSculptParts IsNot Nothing AndAlso SseSculptParts.Count > 0 Then Return True
                If SseTintTexOverride IsNot Nothing AndAlso SseTintTexOverride.Count > 0 Then Return True
                Return False
            End Get
        End Property
    End Class

    ''' <summary>Build the sidecar path for an ESP/ESM/ESL path: same directory, same basename,
    ''' <see cref="Extension"/> in place of the plugin extension.</summary>
    Public Function BuildPath(espPath As String) As String
        If String.IsNullOrEmpty(espPath) Then Return ""
        Return Path.ChangeExtension(espPath, Extension)
    End Function

    ''' <summary>Translate each sidecar entry's "Master.esp|HEX6" key into a global FormID via
    ''' the active load order and seed <paramref name="appliedPresets"/> with a minimal
    ''' <see cref="LooksmenuLoader.LooksmenuPreset"/> carrying just the BodyMorphs + SkinTemplate
    ''' (+ the SSE-only carriers).
    '''
    ''' <para>Entries whose master isn't in the load order resolve to FormID 0 and are skipped —
    ''' same semantics as the LM JSON loader's UnresolvedHeadParts handling. Last-loaded-wins
    ''' across sidecars (iteration order = dict order = insertion order from the preflight scan).
    ''' The Has* flags on the synthesized presets stay False so vanilla fields (HeadParts, tints,
    ''' weights, MRSV, FMRI/FMRS, MSDK/MSDV) are preserved from the raw record.</para>
    '''
    ''' <para>Shared by MainForm (GUI startup) and the headless bake (Program.HeadlessBakeAll) so
    ''' both start from the identical overlay state.</para></summary>
    Public Sub HydratePresets(sidecars As Dictionary(Of String, SidecarFile),
                              pluginManager As PluginManager,
                              appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset))
        If sidecars Is Nothing OrElse sidecars.Count = 0 Then Return
        If pluginManager Is Nothing OrElse appliedPresets Is Nothing Then Return
        For Each pluginKv In sidecars
            Dim sidecar = pluginKv.Value
            If sidecar Is Nothing OrElse sidecar.Npcs Is Nothing Then Continue For
            For Each entryKv In sidecar.Npcs
                Dim entry = entryKv.Value
                If entry Is Nothing OrElse Not entry.HasAnything Then Continue For

                Dim globalFid = LooksmenuLoader.ResolveFormIdentifier(entryKv.Key, pluginManager)
                If globalFid = 0UI Then Continue For  ' Master not in load order; nothing to apply.

                ' If a later sidecar (or some other code path) already hydrated this FormID,
                ' merge in the slider dict + SkinTemplate without clobbering whatever may
                ' already be on the existing overlay (e.g. Has* flags from a prior hydration).
                Dim existing As LooksmenuLoader.LooksmenuPreset = Nothing
                If Not appliedPresets.TryGetValue(globalFid, existing) OrElse existing Is Nothing Then
                    existing = New LooksmenuLoader.LooksmenuPreset()
                    appliedPresets(globalFid) = existing
                End If
                ApplyEntryToPreset(entry, existing)
            Next
        Next
    End Sub

    ''' <summary>Rebuild every F4SE-only field of <paramref name="entry"/> onto <paramref name="preset"/>
    ''' (deep copies; merge-not-clobber for the dict/list fields so multiple sidecars can layer).
    ''' This is THE single entry→preset mirror: <see cref="HydratePresets"/> (startup / headless bake)
    ''' and MainForm.StripEspFieldsFromOverlay (post-save residual) both route through it, so the
    ''' post-save residual is structurally identical to a fresh hydration BY CONSTRUCTION. When the
    ''' sidecar gains a field, add it here + <see cref="EntryFromPreset"/> + <see cref="NpcEntry.HasAnything"/>
    ''' + Read/Write — nowhere else.</summary>
    Public Sub ApplyEntryToPreset(entry As NpcEntry, preset As LooksmenuLoader.LooksmenuPreset)
        If entry Is Nothing OrElse preset Is Nothing Then Return
        If entry.BodyMorphs IsNot Nothing Then
            For Each bm In entry.BodyMorphs
                preset.BodyMorphSliders(bm.Key) = bm.Value
            Next
        End If
        ' SSE keyed body morphs — deep-copy the nested dict. Without this the keyed structure died on
        ' every reload/readback: the flat dict above kept rendering correctly, but the NEXT save rebuilt
        ' the sidecar from a keyed-less overlay, silently collapsing the .jslot round-trip to flat sums
        ' (2026-07-16 audit finding).
        If entry.BodyMorphsKeyed IsNot Nothing AndAlso entry.BodyMorphsKeyed.Count > 0 Then
            Dim keyedCopy As New Dictionary(Of String, Dictionary(Of String, Single))(StringComparer.OrdinalIgnoreCase)
            For Each kv In entry.BodyMorphsKeyed
                Dim inner As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
                If kv.Value IsNot Nothing Then
                    For Each ikv In kv.Value : inner(ikv.Key) = ikv.Value : Next
                End If
                keyedCopy(kv.Key) = inner
            Next
            preset.BodyMorphsKeyed = keyedCopy
        End If
        If Not String.IsNullOrEmpty(entry.SkinTemplateId) Then
            preset.SkinTemplateId = entry.SkinTemplateId
        End If
        ' Overlays (LM body tattoos) — append a deep copy (cloning the float arrays) onto
        ' whatever the overlay already carries, same merge-not-clobber style as BodyMorphs
        ' above. HasOverlays makes the render + editor treat the list as applied.
        If entry.Overlays IsNot Nothing AndAlso entry.Overlays.Count > 0 Then
            For Each ov In entry.Overlays
                preset.Overlays.Add(New LooksmenuLoader.OverlayEntry With {
                    .TemplateId = ov.TemplateId,
                    .Priority = ov.Priority,
                    .Tint = If(ov.Tint Is Nothing, Nothing, CType(ov.Tint.Clone(), Single())),
                    .OffsetUV = If(ov.OffsetUV Is Nothing, Nothing, CType(ov.OffsetUV.Clone(), Single())),
                    .ScaleUV = If(ov.ScaleUV Is Nothing, Nothing, CType(ov.ScaleUV.Clone(), Single()))
                })
            Next
            preset.HasOverlays = True
        End If
        ' SSE body overlays (path-based RaceMenu tattoos) — deep-copy onto the overlay. SSE-only:
        ' FO4 sidecars leave sseBodyOverlays = Nothing, so this no-ops on FO4.
        If entry.SseBodyOverlays IsNot Nothing AndAlso entry.SseBodyOverlays.Count > 0 Then
            preset.SseBodyOverlays = LooksmenuLoader.CloneSseBodyOverlays(entry.SseBodyOverlays)
        End If
        ' SSE node transforms (body-scale/position/rotation) — deep-copy the full per-node TRS onto the
        ' carrier (Raw stays Nothing → a later .jslot export rebuilds each element from the modeled fields).
        If entry.SseNodeTransforms IsNot Nothing AndAlso entry.SseNodeTransforms.Count > 0 Then
            Dim nts As New List(Of RaceMenuJslot.JslotNodeTransform)(entry.SseNodeTransforms.Count)
            For Each nt In entry.SseNodeTransforms
                If nt IsNot Nothing Then nts.Add(nt.Clone())
            Next
            preset.SseNodeTransforms = nts
        End If
        ' SSE RaceMenu absolute hair tint (packed RGB) — rebuild onto the carrier from the sidecar.
        If entry.SseHairColorRgb.HasValue Then preset.SseHairColorRgb = entry.SseHairColorRgb
        ' SSE skin overrides (body-paint per slot) — deep-copy onto the overlay. SSE-only.
        If entry.SseSkinOverrides IsNot Nothing AndAlso entry.SseSkinOverrides.Count > 0 Then
            preset.SseSkinOverrides = LooksmenuLoader.CloneSseSkinOverrides(entry.SseSkinOverrides)
        End If
        ' SSE custom face morphs — rebuild onto the overlay so they auto-resolve after reload (SSE-only).
        If entry.SseCustomMorphs IsNot Nothing AndAlso entry.SseCustomMorphs.Count > 0 Then
            Dim cms As New List(Of NPC_CustomMorph)(entry.SseCustomMorphs.Count)
            For Each cm In entry.SseCustomMorphs : cms.Add(New NPC_CustomMorph With {.Name = cm.Name, .Value = cm.Value}) : Next
            preset.SseCustomMorphs = cms
        End If
        ' SSE per-vertex head sculpt — rebuild onto the overlay so it survives reload (SSE-only).
        If entry.SseSculptHead IsNot Nothing AndAlso entry.SseSculptHead.Count > 0 Then
            Dim sc As New List(Of NPC_SculptVert)(entry.SseSculptHead.Count)
            For Each sv In entry.SseSculptHead : sc.Add(New NPC_SculptVert With {.Index = sv.Index, .Dx = sv.Dx, .Dy = sv.Dy, .Dz = sv.Dz}) : Next
            preset.SseSculptHead = sc
        End If
        ' SSE per-SHAPE sculpt (head+brows+eyes+mouth) — full-fidelity superset; rebuild onto the overlay too.
        If entry.SseSculptParts IsNot Nothing AndAlso entry.SseSculptParts.Count > 0 Then
            preset.SseSculptParts = LooksmenuLoader.CloneSseSculptParts(entry.SseSculptParts)
        End If
        ' SSE per-layer custom tint mask textures — rebuild onto the overlay so they survive reload (SSE-only).
        If entry.SseTintTexOverride IsNot Nothing AndAlso entry.SseTintTexOverride.Count > 0 Then
            preset.SseTintTexOverride = New Dictionary(Of Integer, String)(entry.SseTintTexOverride)
        End If
    End Sub

    ''' <summary>Build a sidecar <see cref="NpcEntry"/> from an in-memory overlay preset (deep copies —
    ''' the entry must stay independent of the live overlay). Inverse of <see cref="ApplyEntryToPreset"/>;
    ''' NpcOverrideSaver.MergeOneNpcIntoSidecar (Save ESP) and MainForm.StripEspFieldsFromOverlay
    ''' (post-save residual) both route through it, so the F4SE-only field list lives in this file only.
    ''' <paramref name="overlay"/> = Nothing yields an empty entry (Write() drops it → a clear-then-save
    ''' round trip removes the NPC's row from disk).</summary>
    Public Function EntryFromPreset(overlay As LooksmenuLoader.LooksmenuPreset,
                                    editorId As String,
                                    gender As String) As NpcEntry
        Dim entry As New NpcEntry With {
            .EditorId = If(editorId, ""),
            .Gender = If(gender, "")
        }
        If overlay Is Nothing Then Return entry

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
        ' sidecar copy is independent of the live overlay. NOT routed to BodyGen (see
        ' NpcOverrideSaver.EmitBodyGenFromSidecar): overlays have no in-game file mechanism.
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
        Return entry
    End Function

    ''' <summary>Read sidecar JSON from disk. Returns Nothing when the file is missing,
    ''' unreadable, or not valid JSON. Logs nothing — caller decides whether/how to surface.
    ''' Schema-mismatch fields are silently ignored (forward-compat).</summary>
    Public Function Read(path As String) As SidecarFile
        If String.IsNullOrEmpty(path) OrElse Not File.Exists(path) Then Return Nothing
        Dim raw As String
        Try
            raw = File.ReadAllText(path)
        Catch
            Return Nothing
        End Try
        Dim doc As JsonDocument
        Try
            doc = JsonDocument.Parse(raw)
        Catch
            Return Nothing
        End Try
        Using doc
            Dim root = doc.RootElement
            If root.ValueKind <> JsonValueKind.Object Then Return Nothing
            Dim result As New SidecarFile

            Dim el As JsonElement
            If root.TryGetProperty("version", el) AndAlso el.ValueKind = JsonValueKind.Number Then
                result.Version = el.GetInt32()
            End If
            If root.TryGetProperty("plugin", el) AndAlso el.ValueKind = JsonValueKind.String Then
                result.Plugin = el.GetString()
            End If
            If root.TryGetProperty("payloadSalt", el) AndAlso el.ValueKind = JsonValueKind.String Then
                result.PayloadSalt = If(el.GetString(), "")
            End If
            If root.TryGetProperty("payloadGeneration", el) AndAlso el.ValueKind = JsonValueKind.Number Then
                result.PayloadGeneration = el.GetInt32()
            End If
            If root.TryGetProperty("npcs", el) AndAlso el.ValueKind = JsonValueKind.Object Then
                For Each prop In el.EnumerateObject()
                    Dim entry = ParseNpcEntry(prop.Value)
                    If entry IsNot Nothing Then result.Npcs(prop.Name) = entry
                Next
            End If
            Return result
        End Using
    End Function

    Private Function ParseNpcEntry(el As JsonElement) As NpcEntry
        If el.ValueKind <> JsonValueKind.Object Then Return Nothing
        Dim entry As New NpcEntry
        Dim child As JsonElement
        If el.TryGetProperty("editorId", child) AndAlso child.ValueKind = JsonValueKind.String Then
            entry.EditorId = child.GetString()
        End If
        If el.TryGetProperty("bodyMorphs", child) AndAlso child.ValueKind = JsonValueKind.Object Then
            For Each prop In child.EnumerateObject()
                If prop.Value.ValueKind = JsonValueKind.Number Then
                    entry.BodyMorphs(prop.Name) = prop.Value.GetSingle()
                End If
            Next
        End If
        ' bodyMorphsKeyed — SSE-only, optional. Object of { morphName : { key : value } }. Tolerant of
        ' absence (v1 files, FO4 entries) — left Nothing when the field is missing.
        If el.TryGetProperty("bodyMorphsKeyed", child) AndAlso child.ValueKind = JsonValueKind.Object Then
            Dim keyed As New Dictionary(Of String, Dictionary(Of String, Single))(StringComparer.OrdinalIgnoreCase)
            For Each morphProp In child.EnumerateObject()
                If morphProp.Value.ValueKind <> JsonValueKind.Object Then Continue For
                Dim inner As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
                For Each keyProp In morphProp.Value.EnumerateObject()
                    If keyProp.Value.ValueKind = JsonValueKind.Number Then
                        inner(keyProp.Name) = keyProp.Value.GetSingle()
                    End If
                Next
                keyed(morphProp.Name) = inner
            Next
            If keyed.Count > 0 Then entry.BodyMorphsKeyed = keyed
        End If
        If el.TryGetProperty("skinTemplateId", child) AndAlso child.ValueKind = JsonValueKind.String Then
            entry.SkinTemplateId = child.GetString()
        End If
        If el.TryGetProperty("gender", child) AndAlso child.ValueKind = JsonValueKind.String Then
            entry.Gender = child.GetString()
        End If
        ' overlays — optional array of LM body overlays. Same element shape as the LM preset
        ' overlay format (see LooksmenuLoader.ParseFile's Overlays block): template required,
        ' priority default 0, optional tint[r,g,b,a]/offsetUV[x,y]/scaleUV[x,y] left Nothing when
        ' absent. An element without a template id can't reference a template, so it's skipped.
        If el.TryGetProperty("overlays", child) AndAlso child.ValueKind = JsonValueKind.Array Then
            For Each ov In child.EnumerateArray()
                If ov.ValueKind <> JsonValueKind.Object Then Continue For
                Dim tplEl As JsonElement
                If Not ov.TryGetProperty("template", tplEl) OrElse tplEl.ValueKind <> JsonValueKind.String Then Continue For
                Dim tplId = tplEl.GetString()
                If String.IsNullOrEmpty(tplId) Then Continue For

                Dim ovEntry As New LooksmenuLoader.OverlayEntry With {.TemplateId = tplId}

                Dim prEl As JsonElement
                If ov.TryGetProperty("priority", prEl) AndAlso prEl.ValueKind = JsonValueKind.Number Then
                    ovEntry.Priority = prEl.GetInt32()
                End If

                Dim tintEl As JsonElement
                If ov.TryGetProperty("tint", tintEl) AndAlso tintEl.ValueKind = JsonValueKind.Array Then
                    ovEntry.Tint = ReadFloatArray(tintEl, 4)
                End If

                Dim offEl As JsonElement
                If ov.TryGetProperty("offsetUV", offEl) AndAlso offEl.ValueKind = JsonValueKind.Array Then
                    ovEntry.OffsetUV = ReadFloatArray(offEl, 2)
                End If

                Dim sclEl As JsonElement
                If ov.TryGetProperty("scaleUV", sclEl) AndAlso sclEl.ValueKind = JsonValueKind.Array Then
                    ovEntry.ScaleUV = ReadFloatArray(sclEl, 2)
                End If

                entry.Overlays.Add(ovEntry)
            Next
        End If
        ' sseBodyOverlays — SSE-only, optional (schema v3). Array of path-based RaceMenu overlays:
        ' { node, diffuse, normal?, tint?[r,g,b,a] }. Tolerant of absence (FO4 / v1-v2 files) — left Nothing.
        If el.TryGetProperty("sseBodyOverlays", child) AndAlso child.ValueKind = JsonValueKind.Array Then
            Dim list As New List(Of RaceMenuJslot.JslotOverlayNode)
            For Each ov In child.EnumerateArray()
                If ov.ValueKind <> JsonValueKind.Object Then Continue For
                Dim nodeEl As JsonElement
                If Not ov.TryGetProperty("node", nodeEl) OrElse nodeEl.ValueKind <> JsonValueKind.String Then Continue For
                Dim node As New RaceMenuJslot.JslotOverlayNode With {.NodeName = nodeEl.GetString(), .DiffusePath = "", .NormalPath = ""}
                Dim s As JsonElement
                If ov.TryGetProperty("diffuse", s) AndAlso s.ValueKind = JsonValueKind.String Then node.DiffusePath = s.GetString()
                If ov.TryGetProperty("normal", s) AndAlso s.ValueKind = JsonValueKind.String Then node.NormalPath = s.GetString()
                Dim tintEl As JsonElement
                If ov.TryGetProperty("tint", tintEl) AndAlso tintEl.ValueKind = JsonValueKind.Array Then
                    Dim t = ReadFloatArray(tintEl, 4)
                    node.TintR = t(0) : node.TintG = t(1) : node.TintB = t(2) : node.TintA = t(3)
                    node.HasTint = True
                End If
                ' alpha — schema v9. skee64's kParam_ShaderAlpha (key 8) = the overlay's OPACITY, a separate
                ' override from the tint colour. Absent in v1-v8 files, which then reload fully opaque — exactly
                ' how they already rendered before the key was modelled, so no silent change of appearance.
                Dim alphaEl As JsonElement
                If ov.TryGetProperty("alpha", alphaEl) AndAlso alphaEl.ValueKind = JsonValueKind.Number Then
                    node.Alpha = alphaEl.GetSingle() : node.HasAlpha = True
                End If
                list.Add(node)
            Next
            If list.Count > 0 Then entry.SseBodyOverlays = list
        End If
        ' sseNodeTransforms — SSE-only, optional (schema v10). Array of { node, s?, sm?, p:[x,y,z]?, r:[ax,ay,az]? }
        ' — the full per-node TRS (rotation as axis-angle radians, the model's canonical form). Raw stays Nothing so a
        ' later .jslot export rebuilds the element from these fields.
        If el.TryGetProperty("sseNodeTransforms", child) AndAlso child.ValueKind = JsonValueKind.Array Then
            Dim list As New List(Of RaceMenuJslot.JslotNodeTransform)
            For Each te In child.EnumerateArray()
                If te.ValueKind <> JsonValueKind.Object Then Continue For
                Dim nameEl As JsonElement
                If Not te.TryGetProperty("node", nameEl) OrElse nameEl.ValueKind <> JsonValueKind.String Then Continue For
                Dim nt As New RaceMenuJslot.JslotNodeTransform With {.NodeName = nameEl.GetString()}
                Dim f As JsonElement
                If te.TryGetProperty("s", f) AndAlso f.ValueKind = JsonValueKind.Number Then nt.Scale = f.GetSingle() : nt.HasScale = True
                If te.TryGetProperty("sm", f) AndAlso f.ValueKind = JsonValueKind.Number Then nt.ScaleMode = f.GetInt32() : nt.HasScaleMode = True
                If te.TryGetProperty("p", f) AndAlso f.ValueKind = JsonValueKind.Array AndAlso f.GetArrayLength() = 3 Then
                    nt.PosX = f(0).GetSingle() : nt.PosY = f(1).GetSingle() : nt.PosZ = f(2).GetSingle() : nt.HasPosition = True
                End If
                If te.TryGetProperty("r", f) AndAlso f.ValueKind = JsonValueKind.Array AndAlso f.GetArrayLength() = 3 Then
                    nt.RotX = f(0).GetSingle() : nt.RotY = f(1).GetSingle() : nt.RotZ = f(2).GetSingle() : nt.HasRotation = True
                End If
                list.Add(nt)
            Next
            If list.Count > 0 Then entry.SseNodeTransforms = list
        ElseIf el.TryGetProperty("sseNodeScales", child) AndAlso child.ValueKind = JsonValueKind.Object Then
            ' Legacy scale-only map (schema v4-v9). Object { nodeName: scale } → migrate to scale-only transforms.
            Dim list As New List(Of RaceMenuJslot.JslotNodeTransform)
            For Each prop In child.EnumerateObject()
                If prop.Value.ValueKind = JsonValueKind.Number Then
                    list.Add(New RaceMenuJslot.JslotNodeTransform With {.NodeName = prop.Name, .Scale = prop.Value.GetSingle(), .HasScale = True})
                End If
            Next
            If list.Count > 0 Then entry.SseNodeTransforms = list
        End If
        ' sseHairColor — SSE-only, optional (schema v11). Packed 0xRRGGBB int (RaceMenu absolute hair tint).
        Dim hairEl As JsonElement
        If el.TryGetProperty("sseHairColor", hairEl) AndAlso hairEl.ValueKind = JsonValueKind.Number Then
            entry.SseHairColorRgb = hairEl.GetInt32()
        End If
        ' sseSkinOverrides — SSE-only, optional (schema v5). Array of { slotMask, diffuse?, normal?, tint?[r,g,b,a] }.
        ' Tolerant of absence (FO4 / v1-v4 files) — left Nothing.
        If el.TryGetProperty("sseSkinOverrides", child) AndAlso child.ValueKind = JsonValueKind.Array Then
            Dim list As New List(Of RaceMenuJslot.JslotSkinOverride)
            For Each so In child.EnumerateArray()
                If so.ValueKind <> JsonValueKind.Object Then Continue For
                Dim sk As New RaceMenuJslot.JslotSkinOverride With {.DiffusePath = "", .NormalPath = ""}
                Dim m As JsonElement
                If so.TryGetProperty("slotMask", m) AndAlso m.ValueKind = JsonValueKind.Number Then sk.SlotMask = CUInt(m.GetInt64() And &HFFFFFFFFL)
                Dim s As JsonElement
                If so.TryGetProperty("diffuse", s) AndAlso s.ValueKind = JsonValueKind.String Then sk.DiffusePath = s.GetString()
                If so.TryGetProperty("normal", s) AndAlso s.ValueKind = JsonValueKind.String Then sk.NormalPath = s.GetString()
                Dim tintEl As JsonElement
                If so.TryGetProperty("tint", tintEl) AndAlso tintEl.ValueKind = JsonValueKind.Array Then
                    Dim t = ReadFloatArray(tintEl, 4)
                    sk.TintR = t(0) : sk.TintG = t(1) : sk.TintB = t(2) : sk.TintA = t(3)
                    sk.HasTint = True
                End If
                list.Add(sk)
            Next
            If list.Count > 0 Then entry.SseSkinOverrides = list
        End If
        ' sseCustomMorphs — SSE-only, optional (schema v6). Array of { name, value }. Tolerant of absence.
        If el.TryGetProperty("sseCustomMorphs", child) AndAlso child.ValueKind = JsonValueKind.Array Then
            Dim list As New List(Of NPC_CustomMorph)
            For Each cm In child.EnumerateArray()
                If cm.ValueKind <> JsonValueKind.Object Then Continue For
                Dim nm As JsonElement, vv As JsonElement
                If Not cm.TryGetProperty("name", nm) OrElse nm.ValueKind <> JsonValueKind.String Then Continue For
                Dim val As Single = 0
                If cm.TryGetProperty("value", vv) AndAlso vv.ValueKind = JsonValueKind.Number Then val = vv.GetSingle()
                list.Add(New NPC_CustomMorph With {.Name = nm.GetString(), .Value = val})
            Next
            If list.Count > 0 Then entry.SseCustomMorphs = list
        End If
        ' sseSculpt — SSE-only, optional (schema v6). Array of { index, dx, dy, dz } (object-space deltas). Tolerant of absence.
        If el.TryGetProperty("sseSculpt", child) AndAlso child.ValueKind = JsonValueKind.Array Then
            Dim list As New List(Of NPC_SculptVert)
            For Each sv In child.EnumerateArray()
                If sv.ValueKind <> JsonValueKind.Object Then Continue For
                Dim ix As JsonElement
                If Not sv.TryGetProperty("index", ix) OrElse ix.ValueKind <> JsonValueKind.Number Then Continue For
                Dim dx As Single = 0, dy As Single = 0, dz As Single = 0, tmp As JsonElement
                If sv.TryGetProperty("dx", tmp) AndAlso tmp.ValueKind = JsonValueKind.Number Then dx = tmp.GetSingle()
                If sv.TryGetProperty("dy", tmp) AndAlso tmp.ValueKind = JsonValueKind.Number Then dy = tmp.GetSingle()
                If sv.TryGetProperty("dz", tmp) AndAlso tmp.ValueKind = JsonValueKind.Number Then dz = tmp.GetSingle()
                list.Add(New NPC_SculptVert With {.Index = CUInt(ix.GetInt64() And &HFFFFFFFFL), .Dx = dx, .Dy = dy, .Dz = dz})
            Next
            If list.Count > 0 Then entry.SseSculptHead = list
        End If
        ' sseSculptParts — SSE-only, optional (schema v8). Per-shape sculpt: array of { host, verts:[{index,dx,dy,dz}] }.
        ' Full-fidelity superset of sseSculpt (head-only). Tolerant of absence (older sidecars only have sseSculpt).
        If el.TryGetProperty("sseSculptParts", child) AndAlso child.ValueKind = JsonValueKind.Array Then
            Dim parts As New List(Of NPC_SculptPart)
            For Each pe In child.EnumerateArray()
                If pe.ValueKind <> JsonValueKind.Object Then Continue For
                Dim host As String = ""
                Dim hostEl As JsonElement
                If pe.TryGetProperty("host", hostEl) AndAlso hostEl.ValueKind = JsonValueKind.String Then host = hostEl.GetString()
                Dim verts As New List(Of NPC_SculptVert)
                Dim vertsEl As JsonElement
                If pe.TryGetProperty("verts", vertsEl) AndAlso vertsEl.ValueKind = JsonValueKind.Array Then
                    For Each sv In vertsEl.EnumerateArray()
                        If sv.ValueKind <> JsonValueKind.Object Then Continue For
                        Dim ix As JsonElement
                        If Not sv.TryGetProperty("index", ix) OrElse ix.ValueKind <> JsonValueKind.Number Then Continue For
                        Dim dx As Single = 0, dy As Single = 0, dz As Single = 0, tmp As JsonElement
                        If sv.TryGetProperty("dx", tmp) AndAlso tmp.ValueKind = JsonValueKind.Number Then dx = tmp.GetSingle()
                        If sv.TryGetProperty("dy", tmp) AndAlso tmp.ValueKind = JsonValueKind.Number Then dy = tmp.GetSingle()
                        If sv.TryGetProperty("dz", tmp) AndAlso tmp.ValueKind = JsonValueKind.Number Then dz = tmp.GetSingle()
                        verts.Add(New NPC_SculptVert With {.Index = CUInt(ix.GetInt64() And &HFFFFFFFFL), .Dx = dx, .Dy = dy, .Dz = dz})
                    Next
                End If
                If verts.Count > 0 Then parts.Add(New NPC_SculptPart With {.Host = host, .Verts = verts})
            Next
            If parts.Count > 0 Then entry.SseSculptParts = parts
        End If
        ' sseTintTextures — SSE-only, optional (schema v7). Array of { index, texture } (custom tint mask paths).
        If el.TryGetProperty("sseTintTextures", child) AndAlso child.ValueKind = JsonValueKind.Array Then
            Dim map As New Dictionary(Of Integer, String)
            For Each tt In child.EnumerateArray()
                If tt.ValueKind <> JsonValueKind.Object Then Continue For
                Dim ix As JsonElement, tx As JsonElement
                If Not tt.TryGetProperty("index", ix) OrElse ix.ValueKind <> JsonValueKind.Number Then Continue For
                If Not tt.TryGetProperty("texture", tx) OrElse tx.ValueKind <> JsonValueKind.String Then Continue For
                Dim path = tx.GetString()
                If Not String.IsNullOrEmpty(path) Then map(ix.GetInt32()) = path
            Next
            If map.Count > 0 Then entry.SseTintTexOverride = map
        End If
        Return entry
    End Function

    ''' <summary>Read up to <paramref name="count"/> floats from a JSON array element into a fixed
    ''' Single() (missing/non-number slots stay 0). Mirror of LooksmenuLoader.ReadFloatArray —
    ''' duplicated here because that helper is Private to the loader.</summary>
    Private Function ReadFloatArray(arrEl As JsonElement, count As Integer) As Single()
        Dim result(count - 1) As Single
        Dim i As Integer = 0
        For Each v In arrEl.EnumerateArray()
            If i >= count Then Exit For
            If v.ValueKind = JsonValueKind.Number Then result(i) = v.GetSingle()
            i += 1
        Next
        Return result
    End Function

    ''' <summary>Write the sidecar JSON to disk atomically (.tmp + rename). Filters out NPC
    ''' entries that have neither sliders nor a skin template id. If nothing remains after
    ''' filtering, the existing sidecar (if any) is deleted instead of writing an empty file.
    ''' Indented output, npcs keys sorted ascending so diffs across saves stay readable.</summary>
    Public Sub Write(path As String, sidecar As SidecarFile)
        If String.IsNullOrEmpty(path) OrElse sidecar Is Nothing Then Return

        Dim kept = sidecar.Npcs.
            Where(Function(kv) kv.Value IsNot Nothing AndAlso kv.Value.HasAnything).
            OrderBy(Function(kv) kv.Key, StringComparer.OrdinalIgnoreCase).
            ToList()

        ' ⛔ NO borrar el archivo si guarda una GENERACION: el contador tiene que sobrevivir a un
        ' guardado sin overlays. Si se borrara, el proximo Save ESP volveria a la generacion 1 y todo
        ' jugador que ya tuviera esa generacion recibiria el payload rancio, en silencio.
        If kept.Count = 0 AndAlso sidecar.PayloadGeneration <= 0 Then
            Try
                If File.Exists(path) Then File.Delete(path)
            Catch
                ' Best-effort cleanup; a leftover empty sidecar is harmless on next read.
            End Try
            Return
        End If

        Dim opts As New JsonWriterOptions With {.Indented = True}
        Dim bytes() As Byte
        Using ms As New MemoryStream()
            Using w As New Utf8JsonWriter(ms, opts)
                w.WriteStartObject()
                w.WriteNumber("version", SchemaVersion)
                w.WriteString("plugin", If(sidecar.Plugin, ""))
                If sidecar.PayloadGeneration > 0 Then w.WriteNumber("payloadGeneration", sidecar.PayloadGeneration)
                If Not String.IsNullOrEmpty(sidecar.PayloadSalt) Then w.WriteString("payloadSalt", sidecar.PayloadSalt)
                w.WriteStartObject("npcs")
                For Each kv In kept
                    w.WriteStartObject(kv.Key)
                    w.WriteString("editorId", If(kv.Value.EditorId, ""))
                    If kv.Value.BodyMorphs IsNot Nothing AndAlso kv.Value.BodyMorphs.Count > 0 Then
                        w.WriteStartObject("bodyMorphs")
                        For Each bm In kv.Value.BodyMorphs.OrderBy(Function(p) p.Key, StringComparer.Ordinal)
                            w.WriteNumber(bm.Key, bm.Value)
                        Next
                        w.WriteEndObject()
                    End If
                    ' bodyMorphsKeyed — SSE-only, emitted when non-empty. Nested object mirroring the
                    ' flat bodyMorphs block above; morph names and keys sorted for stable diffs.
                    If kv.Value.BodyMorphsKeyed IsNot Nothing AndAlso kv.Value.BodyMorphsKeyed.Count > 0 Then
                        w.WriteStartObject("bodyMorphsKeyed")
                        For Each morph In kv.Value.BodyMorphsKeyed.OrderBy(Function(p) p.Key, StringComparer.Ordinal)
                            w.WriteStartObject(morph.Key)
                            If morph.Value IsNot Nothing Then
                                For Each mk In morph.Value.OrderBy(Function(p) p.Key, StringComparer.Ordinal)
                                    w.WriteNumber(mk.Key, mk.Value)
                                Next
                            End If
                            w.WriteEndObject()
                        Next
                        w.WriteEndObject()
                    End If
                    If Not String.IsNullOrEmpty(kv.Value.SkinTemplateId) Then
                        w.WriteString("skinTemplateId", kv.Value.SkinTemplateId)
                    End If
                    If Not String.IsNullOrEmpty(kv.Value.Gender) Then
                        w.WriteString("gender", kv.Value.Gender)
                    End If
                    ' overlays — emitted when non-empty. template + priority always; tint/offsetUV/
                    ' scaleUV only when non-Nothing. Mirrors the LM serializer's float-array idiom
                    ' (LooksmenuLoader.SerializePreset Overlays block) but with the sidecar's
                    ' insertion order preserved (priority drives render order independently).
                    If kv.Value.Overlays IsNot Nothing AndAlso kv.Value.Overlays.Count > 0 Then
                        w.WriteStartArray("overlays")
                        For Each ov In kv.Value.Overlays
                            w.WriteStartObject()
                            w.WriteString("template", ov.TemplateId)
                            w.WriteNumber("priority", ov.Priority)
                            If ov.Tint IsNot Nothing Then
                                w.WriteStartArray("tint")
                                For Each f In ov.Tint : w.WriteNumberValue(f) : Next
                                w.WriteEndArray()
                            End If
                            If ov.OffsetUV IsNot Nothing Then
                                w.WriteStartArray("offsetUV")
                                For Each f In ov.OffsetUV : w.WriteNumberValue(f) : Next
                                w.WriteEndArray()
                            End If
                            If ov.ScaleUV IsNot Nothing Then
                                w.WriteStartArray("scaleUV")
                                For Each f In ov.ScaleUV : w.WriteNumberValue(f) : Next
                                w.WriteEndArray()
                            End If
                            w.WriteEndObject()
                        Next
                        w.WriteEndArray()
                    End If
                    ' sseBodyOverlays — SSE-only, emitted when non-empty. node + diffuse always; normal/tint
                    ' only when present. Insertion order preserved (skee applies Ovl0..N in node order).
                    If kv.Value.SseBodyOverlays IsNot Nothing AndAlso kv.Value.SseBodyOverlays.Count > 0 Then
                        w.WriteStartArray("sseBodyOverlays")
                        For Each ov In kv.Value.SseBodyOverlays
                            If ov Is Nothing Then Continue For
                            w.WriteStartObject()
                            w.WriteString("node", If(ov.NodeName, ""))
                            w.WriteString("diffuse", If(ov.DiffusePath, ""))
                            If Not String.IsNullOrEmpty(ov.NormalPath) Then w.WriteString("normal", ov.NormalPath)
                            If ov.HasTint Then
                                w.WriteStartArray("tint")
                                w.WriteNumberValue(ov.TintR) : w.WriteNumberValue(ov.TintG)
                                w.WriteNumberValue(ov.TintB) : w.WriteNumberValue(ov.TintA)
                                w.WriteEndArray()
                            End If
                            If ov.HasAlpha Then w.WriteNumber("alpha", ov.Alpha)   ' opacity (skee64 key 8), schema v9
                            w.WriteEndObject()
                        Next
                        w.WriteEndArray()
                    End If
                    ' sseNodeTransforms — SSE-only, emitted when non-empty. Array of { node, s?, sm?, p:[x,y,z]?,
                    ' r:[ax,ay,az]? } — the full per-node TRS (rotation as axis-angle radians). Only the present
                    ' components are written, so a scale-only override stays compact.
                    If kv.Value.SseNodeTransforms IsNot Nothing AndAlso kv.Value.SseNodeTransforms.Count > 0 Then
                        w.WriteStartArray("sseNodeTransforms")
                        For Each nt In kv.Value.SseNodeTransforms
                            If nt Is Nothing OrElse String.IsNullOrEmpty(nt.NodeName) Then Continue For
                            w.WriteStartObject()
                            w.WriteString("node", nt.NodeName)
                            If nt.HasScale Then w.WriteNumber("s", nt.Scale)
                            If nt.HasScaleMode Then w.WriteNumber("sm", nt.ScaleMode)
                            If nt.HasPosition Then
                                w.WriteStartArray("p")
                                w.WriteNumberValue(nt.PosX) : w.WriteNumberValue(nt.PosY) : w.WriteNumberValue(nt.PosZ)
                                w.WriteEndArray()
                            End If
                            If nt.HasRotation Then
                                w.WriteStartArray("r")
                                w.WriteNumberValue(nt.RotX) : w.WriteNumberValue(nt.RotY) : w.WriteNumberValue(nt.RotZ)
                                w.WriteEndArray()
                            End If
                            w.WriteEndObject()
                        Next
                        w.WriteEndArray()
                    End If
                    ' sseHairColor — SSE-only, emitted when present. Packed 0xRRGGBB int (RaceMenu absolute hair tint).
                    If kv.Value.SseHairColorRgb.HasValue Then w.WriteNumber("sseHairColor", kv.Value.SseHairColorRgb.Value)
                    ' sseSkinOverrides — SSE-only, emitted when non-empty. Array of { slotMask, diffuse, normal?, tint? }.
                    If kv.Value.SseSkinOverrides IsNot Nothing AndAlso kv.Value.SseSkinOverrides.Count > 0 Then
                        w.WriteStartArray("sseSkinOverrides")
                        For Each sk In kv.Value.SseSkinOverrides
                            If sk Is Nothing Then Continue For
                            w.WriteStartObject()
                            w.WriteNumber("slotMask", CLng(sk.SlotMask))
                            w.WriteString("diffuse", If(sk.DiffusePath, ""))
                            If Not String.IsNullOrEmpty(sk.NormalPath) Then w.WriteString("normal", sk.NormalPath)
                            If sk.HasTint Then
                                w.WriteStartArray("tint")
                                w.WriteNumberValue(sk.TintR) : w.WriteNumberValue(sk.TintG)
                                w.WriteNumberValue(sk.TintB) : w.WriteNumberValue(sk.TintA)
                                w.WriteEndArray()
                            End If
                            w.WriteEndObject()
                        Next
                        w.WriteEndArray()
                    End If
                    ' sseCustomMorphs — SSE-only, emitted when non-empty. Array of { name, value }.
                    If kv.Value.SseCustomMorphs IsNot Nothing AndAlso kv.Value.SseCustomMorphs.Count > 0 Then
                        w.WriteStartArray("sseCustomMorphs")
                        For Each cm In kv.Value.SseCustomMorphs
                            If cm Is Nothing OrElse String.IsNullOrEmpty(cm.Name) Then Continue For
                            w.WriteStartObject()
                            w.WriteString("name", cm.Name)
                            w.WriteNumber("value", cm.Value)
                            w.WriteEndObject()
                        Next
                        w.WriteEndArray()
                    End If
                    ' sseSculpt — SSE-only, emitted when non-empty. Array of { index, dx, dy, dz }.
                    If kv.Value.SseSculptHead IsNot Nothing AndAlso kv.Value.SseSculptHead.Count > 0 Then
                        w.WriteStartArray("sseSculpt")
                        For Each sv In kv.Value.SseSculptHead
                            If sv Is Nothing Then Continue For
                            w.WriteStartObject()
                            w.WriteNumber("index", CLng(sv.Index))
                            w.WriteNumber("dx", sv.Dx) : w.WriteNumber("dy", sv.Dy) : w.WriteNumber("dz", sv.Dz)
                            w.WriteEndObject()
                        Next
                        w.WriteEndArray()
                    End If
                    ' sseSculptParts — SSE-only, emitted when non-empty (schema v8). Per-shape: { host, verts:[{index,dx,dy,dz}] }.
                    If kv.Value.SseSculptParts IsNot Nothing AndAlso kv.Value.SseSculptParts.Count > 0 Then
                        w.WriteStartArray("sseSculptParts")
                        For Each pt In kv.Value.SseSculptParts
                            If pt Is Nothing OrElse pt.Verts Is Nothing OrElse pt.Verts.Count = 0 Then Continue For
                            w.WriteStartObject()
                            w.WriteString("host", If(pt.Host, ""))
                            w.WriteStartArray("verts")
                            For Each sv In pt.Verts
                                If sv Is Nothing Then Continue For
                                w.WriteStartObject()
                                w.WriteNumber("index", CLng(sv.Index))
                                w.WriteNumber("dx", sv.Dx) : w.WriteNumber("dy", sv.Dy) : w.WriteNumber("dz", sv.Dz)
                                w.WriteEndObject()
                            Next
                            w.WriteEndArray()
                            w.WriteEndObject()
                        Next
                        w.WriteEndArray()
                    End If
                    ' sseTintTextures — SSE-only, emitted when non-empty. Array of { index, texture } (custom tint masks).
                    If kv.Value.SseTintTexOverride IsNot Nothing AndAlso kv.Value.SseTintTexOverride.Count > 0 Then
                        w.WriteStartArray("sseTintTextures")
                        For Each tt In kv.Value.SseTintTexOverride
                            If String.IsNullOrEmpty(tt.Value) Then Continue For
                            w.WriteStartObject()
                            w.WriteNumber("index", tt.Key)
                            w.WriteString("texture", tt.Value)
                            w.WriteEndObject()
                        Next
                        w.WriteEndArray()
                    End If
                    w.WriteEndObject()
                Next
                w.WriteEndObject()
                w.WriteEndObject()
                w.Flush()
            End Using
            bytes = ms.ToArray()
        End Using

        Dim tmp = path & ".tmp"
        File.WriteAllBytes(tmp, bytes)
        If File.Exists(path) Then File.Delete(path)
        File.Move(tmp, path)
    End Sub

    ''' <summary>Build a LM-style form identifier <c>"Master.esp|HEX6"</c> from a master plugin
    ''' filename and a (global) FormID. Only the low 24 bits of the FormID are used — the high
    ''' byte (load-order index) is intentionally dropped so the identifier stays stable across
    ''' different load orders.</summary>
    Public Function BuildIdentifier(masterPluginName As String, globalFormID As UInteger) As String
        Return $"{If(masterPluginName, "")}|{(globalFormID And &HFFFFFFUI):X6}"
    End Function

    ''' <summary>Reverse of <see cref="BuildIdentifier"/>: split <c>"Master.esp|HEX6"</c> into
    ''' the master filename and the local 24-bit FormID. Returns Nothing if the identifier is
    ''' malformed (no pipe, hex unparseable, empty master). Caller resolves the master to a
    ''' load-order index via <see cref="LooksmenuLoader.ResolveFormIdentifier"/> to compose the
    ''' global FormID.</summary>
    Public Function TryParseIdentifier(identifier As String,
                                       ByRef masterPluginName As String,
                                       ByRef localFormID As UInteger) As Boolean
        masterPluginName = ""
        localFormID = 0UI
        If String.IsNullOrEmpty(identifier) Then Return False
        Dim pipeIdx = identifier.IndexOf("|"c)
        If pipeIdx <= 0 OrElse pipeIdx >= identifier.Length - 1 Then Return False
        Dim master = identifier.Substring(0, pipeIdx).Trim()
        If String.IsNullOrEmpty(master) Then Return False
        Dim hex = identifier.Substring(pipeIdx + 1).Trim()
        Dim parsed As UInteger
        If Not UInteger.TryParse(hex, Globalization.NumberStyles.HexNumber,
                                 Globalization.CultureInfo.InvariantCulture, parsed) Then Return False
        masterPluginName = master
        localFormID = parsed And &HFFFFFFUI
        Return True
    End Function

End Module
