Imports FO4_Base_Library

''' <summary>Single source of truth for the FULL RaceMenu <c>.jslot</c> ↔ <see cref="LooksmenuLoader.LooksmenuPreset"/>
''' mapping (SSE only). Unifies the two per-editor mappings that today live split across the Edit Face and Edit Body
''' forms so Task-A's game-aware Load/Save/Copy/Paste can round-trip a whole preset in one call instead of the editor-
''' scoped halves.
'''
''' The transforms below are copied faithfully from those editors (cited per block) — no scaling is invented:
'''   FACE  (EditFace_Form.OnSaveJslot :602-621 / OnLoadJslot :566-584 / ApplySseTintOverlay :824-833 /
'''          ParseSseTintLayers :704-714): sliders↔SseNam9 (+VampireMorph sentinel), sculpt↔SseSculptHead (×/÷ divisor),
'''          custom↔SseCustomMorphs, tintInfo↔SseTintRawOverride.
'''   BODY  (EditBody_Form.OnSaveJslot :1085-1090 / OnLoadJslot :1046-1053 / BuildJslotBodyMorphs :1125-1144 /
'''          JslotBodyMorphsToKeyed :1102-1120): actor.weight↔SseWeight, bodyMorphs↔BodyMorphsKeyed (flat fallback),
'''          overrides↔SseBodyOverlays.
'''
''' NOTE (future cleanup): the editors still run their own inline copies of these transforms; they are intentionally
''' left untouched in this task. A later refactor should route EditFace/EditBody Load/Save .jslot through here.</summary>
Public Module RaceMenuPresetMapper

    ''' <summary>Full preset → <c>.jslot</c>. Combines the FACE mapping (EditFace_Form.OnSaveJslot) and the BODY
    ''' mapping (EditBody_Form.OnSaveJslot + BuildJslotBodyMorphs). Never returns Nothing; a Nothing/empty preset
    ''' yields an all-default jslot.</summary>
    Public Function ToJslot(preset As LooksmenuLoader.LooksmenuPreset,
                            Optional pluginManager As PluginManager = Nothing) As RaceMenuJslot
        Dim j As New RaceMenuJslot()
        If preset Is Nothing Then Return j

        ' ---- FACE IDENTITY: headParts + headTexture (inverse of ApplyJslotToPreset's headParts/headTexture apply).
        ' Emit the portable formIdentifier ("Plugin|FormID") when a PluginManager is available — that is what RaceMenu
        ' keys head parts by and makes the preset load-order-independent; without a pm we still emit formId (absolute,
        ' round-trips within the same load order). type = the HDPT PNAM enum (informational for our loader). Without
        ' this a preset saved from the app would drop the actual hair/eyes/brows selection.
        If preset.HasHeadPartFormIDs AndAlso preset.HeadPartFormIDs IsNot Nothing Then
            For Each fid In preset.HeadPartFormIDs
                If fid = 0UI Then Continue For
                Dim ident As String = ""
                Dim ptype As Integer = 0
                If pluginManager IsNot Nothing Then
                    ident = LooksmenuLoader.FormatFormIdentifier(fid, pluginManager)
                    ptype = ResolveHdptType(fid, pluginManager)
                End If
                j.HeadParts.Add(New RaceMenuJslot.JslotHeadPart With {.FormId = fid, .FormIdentifier = ident, .Type = ptype})
            Next
        End If
        If preset.SseHeadTextureFormID <> 0UI AndAlso pluginManager IsNot Nothing Then
            j.HeadTexture = LooksmenuLoader.FormatFormIdentifier(preset.SseHeadTextureFormID, pluginManager)
        End If

        ' ---- FACE: sliders (NAM9) → morphs.default.morphs + [18] VampireMorph sentinel (EditFace_Form.vb:602-603).
        Dim nam9 = preset.SseNam9
        For i = 0 To SseNam9MorphMap.Nam9SliderCount - 1
            Dim v As Single = 0.0F
            If nam9 IsNot Nothing AndAlso i < nam9.Length Then v = nam9(i)
            j.SliderMorphs.Add(v)
        Next
        j.SliderMorphs.Add(3.402823466E+38F)   ' VampireMorph sentinel (= not a vampire), EditFace_Form.vb:603.

        ' ---- FACE: sculpt (head part 0), world delta × SculptDivisor (EditFace_Form.vb:605-614).
        If preset.SseSculptHead IsNot Nothing AndAlso preset.SseSculptHead.Count > 0 Then
            Dim part As New RaceMenuJslot.JslotSculptPart
            For Each sv In preset.SseSculptHead
                part.Indices.Add(sv.Index)
                part.Dx.Add(CInt(Math.Round(sv.Dx * j.SculptDivisor)))
                part.Dy.Add(CInt(Math.Round(sv.Dy * j.SculptDivisor)))
                part.Dz.Add(CInt(Math.Round(sv.Dz * j.SculptDivisor)))
            Next
            j.Sculpt.Add(part)
        End If

        ' ---- FACE: NiOverride custom morphs (EditFace_Form.vb:615-617).
        If preset.SseCustomMorphs IsNot Nothing Then
            For Each cm In preset.SseCustomMorphs
                j.CustomMorphs.Add(New RaceMenuJslot.JslotCustomMorph With {.Name = cm.Name, .Value = cm.Value})
            Next
        End If

        ' ---- FACE: tints. Source of truth is preset.SseTintRawOverride (authored TINI/TINC/TINV/TIAS), parsed the
        ' same way ParseSseTintLayers reads raw.SseTintRaw. Packed to ARGB exactly like RaceMenu serializes
        ' (PresetInterface.cpp:388): alpha = coverage(TINV)*255, then (A<<24)|(R<<16)|(G<<8)|B. The per-layer custom
        ' mask texture (preset.SseTintTexOverride, RaceMenu-only) rides in tint.texture (empty = RACE default mask).
        For Each t In ParseSseTintRawToLayers(preset.SseTintRawOverride)
            Dim aCov As UInteger = CUInt(Math.Max(0, Math.Min(255, Math.Round(t.V * 255.0))))
            Dim col As UInteger = (aCov << 24) Or (CUInt(t.R) << 16) Or (CUInt(t.G) << 8) Or CUInt(t.B)
            Dim texPath As String = ""
            If preset.SseTintTexOverride IsNot Nothing Then preset.SseTintTexOverride.TryGetValue(t.Index, texPath)
            j.TintInfo.Add(New RaceMenuJslot.JslotTint With {.Color = col, .Index = t.Index, .Texture = If(texPath, "")})
        Next

        ' ---- BODY: actor.weight ← SseWeight (EditBody_Form.vb:1085).
        j.Weight = CDbl(If(preset.SseWeight.HasValue, preset.SseWeight.Value, 0.0F))

        ' ---- BODY: bodyMorphs ← keyed (or flat fallback under a synthetic key), replicated from
        ' EditBody_Form.BuildJslotBodyMorphs (:1125-1144).
        BuildJslotBodyMorphs(j, preset)

        ' ---- BODY: overrides ← RaceMenu body overlays (EditBody_Form.vb:1088-1090).
        If preset.SseBodyOverlays IsNot Nothing Then
            j.Overlays.AddRange(LooksmenuLoader.CloneSseBodyOverlays(preset.SseBodyOverlays))
        End If

        ' ---- BODY: transforms ← RaceMenu NiOverride node scales (body-scale sliders).
        If preset.SseNodeTransforms IsNot Nothing Then
            j.NodeTransforms.AddRange(LooksmenuLoader.CloneSseNodeTransforms(preset.SseNodeTransforms))
        End If

        ' ---- SKIN: skinOverrides ← RaceMenu NiOverride skin texture-tint (body-paint per slot).
        If preset.SseSkinOverrides IsNot Nothing Then
            j.SkinOverrides.AddRange(LooksmenuLoader.CloneSseSkinOverrides(preset.SseSkinOverrides))
        End If

        Return j
    End Function

    ''' <summary>Inverse of <see cref="ToJslot"/>: apply a <c>.jslot</c> onto an existing preset in place. Combines
    ''' EditFace_Form.OnLoadJslot (FACE) and EditBody_Form.OnLoadJslot (BODY). Sets the relevant Has* authority flags
    ''' so the resulting preset applies as an overlay.</summary>
    Public Sub ApplyJslotToPreset(j As RaceMenuJslot, preset As LooksmenuLoader.LooksmenuPreset,
                                  Optional pluginManager As PluginManager = Nothing)
        If j Is Nothing OrElse preset Is Nothing Then Return

        ' ---- FACE IDENTITY: headParts (hair/eyes/brows/…) → preset.HeadPartFormIDs. skee64 ApplyPreset applies
        ' the preset's head parts (ChangeHeadPart, PresetInterface.cpp:1580); without this a loaded .jslot changed
        ' the morphs/tints but NOT the actual hair/eyes. The portable id is the "formIdentifier" ("Plugin|FormID");
        ' resolve it against the current load order (LooksmenuLoader.ResolveFormIdentifier). Falls back to the raw
        ' FormId only when there's no identifier. Requires the PluginManager; skipped (identity untouched) without it.
        If pluginManager IsNot Nothing AndAlso j.HeadParts IsNot Nothing AndAlso j.HeadParts.Count > 0 Then
            Dim hp As New List(Of UInteger)
            For Each h In j.HeadParts
                If h Is Nothing Then Continue For
                Dim fid As UInteger = 0UI
                If Not String.IsNullOrEmpty(h.FormIdentifier) Then fid = LooksmenuLoader.ResolveFormIdentifier(h.FormIdentifier, pluginManager)
                If fid = 0UI Then fid = h.FormId
                If fid <> 0UI AndAlso Not hp.Contains(fid) Then hp.Add(fid)
            Next
            If hp.Count > 0 Then
                preset.HeadPartFormIDs.Clear()
                preset.HeadPartFormIDs.AddRange(hp)
                preset.HasHeadPartFormIDs = True
            End If
        End If
        ' ---- FACE IDENTITY: hair color. The .jslot stores actor.hairColor as a packed RGB int (PresetInterface.cpp:677
        ' color.red<<16|green<<8|blue), NOT a CLFM FormID — so it can't be mapped to preset.HairColorFormID (a CLFM
        ' ref) without a matching Color record. Left for a dedicated RGB-hair-tint path (documented, not guessed).
        ' ---- FACE IDENTITY: headTexture (face FTST FormID) — see SseHeadTextureFormID handling below (render override).
        If pluginManager IsNot Nothing AndAlso Not String.IsNullOrEmpty(j.HeadTexture) Then
            preset.SseHeadTextureFormID = LooksmenuLoader.ResolveFormIdentifier(j.HeadTexture, pluginManager)
        End If

        ' ---- FACE: sliders → NAM9, capped at Nam9SliderCount (the [18] VampireMorph sentinel is ignored), then
        ' set HasSseMorphs (EditFace_Form.vb:566-569 + ApplySseMorphOverlay :683-685). Seed from the preset's
        ' existing SseNam9 so untouched slots survive; NAMA is not carried in the jslot slider array (preserve /
        ' default to the unset-zero array).
        Dim nam9(SseNam9MorphMap.Nam9SliderCount - 1) As Single
        If preset.SseNam9 IsNot Nothing Then
            For i = 0 To Math.Min(preset.SseNam9.Length, nam9.Length) - 1 : nam9(i) = preset.SseNam9(i) : Next
        End If
        For i = 0 To Math.Min(j.SliderMorphs.Count, SseNam9MorphMap.Nam9SliderCount) - 1
            nam9(i) = j.SliderMorphs(i)
        Next
        preset.SseNam9 = nam9
        If preset.SseNama Is Nothing Then preset.SseNama = New UInteger(SseNam9MorphMap.NamaFamilyCount - 1) {}
        preset.HasSseMorphs = True

        ' ---- FACE: sculpt(0) ÷ divisor → world deltas (EditFace_Form.vb:571-578).
        If j.Sculpt.Count > 0 Then
            Dim head = j.Sculpt(0), div = Math.Max(1, j.SculptDivisor)
            Dim sc As New List(Of NPC_SculptVert)(head.Indices.Count)
            For k = 0 To head.Indices.Count - 1
                sc.Add(New NPC_SculptVert With {.Index = head.Indices(k), .Dx = head.Dx(k) / div, .Dy = head.Dy(k) / div, .Dz = head.Dz(k) / div})
            Next
            preset.SseSculptHead = sc
        End If

        ' ---- FACE: custom morphs → preset (EditFace_Form.vb:580-584).
        If j.CustomMorphs.Count > 0 Then
            Dim cms As New List(Of NPC_CustomMorph)
            For Each cm In j.CustomMorphs : cms.Add(New NPC_CustomMorph With {.Name = cm.Name, .Value = CSng(cm.Value)}) : Next
            preset.SseCustomMorphs = cms
        End If

        ' ---- FACE: tintInfo → SseTintRawOverride (+ HasSseTints) and the per-layer custom mask texture map.
        ' Inverse of the pack above and of RaceMenu's apply (PresetInterface.cpp:194-205): the jslot colour's ALPHA
        ' byte IS the coverage (tintMask.alpha) → TINV; RGB → TINC (its own alpha byte is unused by the SSE face
        ' composite → 255). TIAS (preset index) is not stored in a .jslot → 0. tint.texture, when non-empty, is a
        ' RaceMenu custom mask path (tintMask->texture->str = tint.name) → SseTintTexOverride[index], composited by
        ' SseFaceTintComposer instead of the RACE layer's own mask.
        If j.TintInfo.Count > 0 Then
            Dim outList As New List(Of NPC_RawSubrecord)
            Dim texMap As Dictionary(Of Integer, String) = Nothing
            For Each ti In j.TintInfo
                Dim a As Byte = CByte((ti.Color >> 24) And &HFFUI)   ' coverage (0..255)
                Dim r As Byte = CByte((ti.Color >> 16) And &HFFUI)
                Dim g As Byte = CByte((ti.Color >> 8) And &HFFUI)
                Dim b As Byte = CByte(ti.Color And &HFFUI)
                Dim tinv As UInteger = CUInt(Math.Round(a / 255.0 * 100.0))
                outList.Add(New NPC_RawSubrecord With {.Sig = "TINI", .Data = BitConverter.GetBytes(CUShort(ti.Index))})
                outList.Add(New NPC_RawSubrecord With {.Sig = "TINC", .Data = New Byte() {r, g, b, 255}})
                outList.Add(New NPC_RawSubrecord With {.Sig = "TINV", .Data = BitConverter.GetBytes(tinv)})
                outList.Add(New NPC_RawSubrecord With {.Sig = "TIAS", .Data = BitConverter.GetBytes(CShort(0))})
                If Not String.IsNullOrEmpty(ti.Texture) Then
                    If texMap Is Nothing Then texMap = New Dictionary(Of Integer, String)
                    texMap(ti.Index) = ti.Texture
                End If
            Next
            preset.SseTintRawOverride = outList
            preset.HasSseTints = True
            preset.SseTintTexOverride = texMap
        End If

        ' ---- BODY: actor.weight → SseWeight (clamp 0..100) (EditBody_Form.vb:1046-1047).
        preset.SseWeight = CSng(Math.Max(0.0, Math.Min(100.0, j.Weight)))

        ' ---- BODY: bodyMorphs → flat render dict + keyed sidecar (EditBody_Form.vb:1049-1050).
        preset.BodyMorphSliders = j.BodyMorphsToFlatSliderDict()
        preset.BodyMorphsKeyed = JslotBodyMorphsToKeyed(j)

        ' ---- BODY: overrides → SSE body overlays (EditBody_Form.vb:1053).
        preset.SseBodyOverlays = LooksmenuLoader.CloneSseBodyOverlays(j.Overlays)

        ' ---- BODY: transforms → SSE node transforms (body-scale).
        preset.SseNodeTransforms = LooksmenuLoader.CloneSseNodeTransforms(j.NodeTransforms)

        ' ---- SKIN: skinOverrides → SSE skin overrides (body-paint per slot).
        preset.SseSkinOverrides = LooksmenuLoader.CloneSseSkinOverrides(j.SkinOverrides)
    End Sub

    ' =====================================================================
    ' Replicated private helpers (copied faithfully from the editors so this module is self-contained; the
    ' originals stay Private to EditFace/EditBody and are unchanged).
    ' =====================================================================

    ''' <summary>One parsed authored SSE tint layer. RaceMenu packs the jslot tint colour as ARGB where the
    ''' ALPHA byte is the layer's COVERAGE (PresetInterface.cpp:195 alpha=(color&gt;&gt;24)/255 → tintMask.alpha),
    ''' i.e. the vanilla TINV — NOT the TINC alpha byte. So we carry V (TINV coverage 0..1) and pack it into the
    ''' jslot alpha; RGB come from TINC.</summary>
    Private Structure SseTintParsed
        Public Index As Integer
        Public R As Byte
        Public G As Byte
        Public B As Byte
        Public V As Double   ' TINV coverage 0..1 → packed into the jslot colour's alpha byte
    End Structure

    ''' <summary>Resolve an HDPT record's PNAM type enum (0=Misc,1=Face,2=Eyes,3=Hair,4=FacialHair,5=Scar,6=Eyebrows)
    ''' for the .jslot headPart "type" field. Returns 0 (Misc) when the record/subrecord is missing.</summary>
    Private Function ResolveHdptType(fid As UInteger, pm As PluginManager) As Integer
        If fid = 0UI OrElse pm Is Nothing Then Return 0
        Dim rec = pm.GetRecord(fid)
        If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Return 0
        For Each sr In rec.Subrecords
            If sr.Signature = "PNAM" AndAlso sr.Data IsNot Nothing AndAlso sr.Data.Length >= 4 Then
                Return CInt(BitConverter.ToUInt32(sr.Data, 0))
            End If
        Next
        Return 0
    End Function

    ''' <summary>Parse a flat TINI/TINC/TINV/TIAS raw subrecord list into per-layer index+RGB+coverage. Replicates
    ''' the authored-layer walk in EditFace_Form.ParseSseTintLayers: TINI opens an entry, TINC sets RGB, TINV sets
    ''' coverage, TIAS closes it. (SseTintRawOverride only ever contains authored layers, so no RACE-default merge.)</summary>
    Private Function ParseSseTintRawToLayers(raw As List(Of NPC_RawSubrecord)) As List(Of SseTintParsed)
        Dim outList As New List(Of SseTintParsed)
        If raw Is Nothing Then Return outList
        Dim cur As New SseTintParsed With {.Index = -1, .V = 1.0}
        Dim have As Boolean = False
        For Each sr In raw
            Select Case sr.Sig
                Case "TINI"
                    cur = New SseTintParsed With {.Index = BitConverter.ToUInt16(sr.Data, 0), .V = 1.0} : have = True
                Case "TINC"
                    If sr.Data.Length >= 3 Then cur.R = sr.Data(0) : cur.G = sr.Data(1) : cur.B = sr.Data(2)
                Case "TINV"
                    If sr.Data.Length >= 4 Then cur.V = BitConverter.ToUInt32(sr.Data, 0) / 100.0
                Case "TIAS"
                    If have Then outList.Add(cur) : have = False
            End Select
        Next
        Return outList
    End Function

    ''' <summary>Decode a .jslot's bodyMorphs into the keyed sidecar shape (name → {key → value}). Nothing when the
    ''' preset carries no body morphs. Copied verbatim from EditBody_Form.JslotBodyMorphsToKeyed (:1102-1120).</summary>
    Private Function JslotBodyMorphsToKeyed(j As RaceMenuJslot) As Dictionary(Of String, Dictionary(Of String, Single))
        If j Is Nothing OrElse j.BodyMorphs Is Nothing OrElse j.BodyMorphs.Count = 0 Then Return Nothing
        Dim d As New Dictionary(Of String, Dictionary(Of String, Single))(StringComparer.OrdinalIgnoreCase)
        For Each bm In j.BodyMorphs
            If bm Is Nothing OrElse String.IsNullOrEmpty(bm.Name) Then Continue For
            Dim inner As Dictionary(Of String, Single) = Nothing
            If Not d.TryGetValue(bm.Name, inner) Then
                inner = New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
                d(bm.Name) = inner
            End If
            If bm.Keys IsNot Nothing Then
                For Each k In bm.Keys
                    If String.IsNullOrEmpty(k.Key) Then Continue For
                    inner(k.Key) = k.Value
                Next
            End If
        Next
        Return d
    End Function

    ''' <summary>Populate a .jslot's bodyMorphs from the preset: keyed data when present, otherwise each flat slider
    ''' under one synthetic key. Copied verbatim from EditBody_Form.BuildJslotBodyMorphs (:1125-1144).</summary>
    Private Sub BuildJslotBodyMorphs(j As RaceMenuJslot, p As LooksmenuLoader.LooksmenuPreset)
        If p.BodyMorphsKeyed IsNot Nothing AndAlso p.BodyMorphsKeyed.Count > 0 Then
            For Each kv In p.BodyMorphsKeyed
                Dim entry As New RaceMenuJslot.JslotBodyMorph With {.Name = kv.Key}
                If kv.Value IsNot Nothing Then
                    For Each ik In kv.Value
                        entry.Keys.Add(New RaceMenuJslot.JslotBodyMorphKey With {.Key = ik.Key, .Value = ik.Value})
                    Next
                End If
                j.BodyMorphs.Add(entry)
            Next
        ElseIf p.BodyMorphSliders IsNot Nothing Then
            For Each kv In p.BodyMorphSliders
                If Math.Abs(kv.Value) < 0.0001F Then Continue For
                Dim entry As New RaceMenuJslot.JslotBodyMorph With {.Name = kv.Key}
                entry.Keys.Add(New RaceMenuJslot.JslotBodyMorphKey With {.Key = "NPCManager", .Value = kv.Value})
                j.BodyMorphs.Add(entry)
            Next
        End If
    End Sub

End Module
