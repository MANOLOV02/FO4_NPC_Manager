Imports FO4_Base_Library

''' <summary>UNIFIED per-category merge, shared by Paste Look and by the LooksMenu/RaceMenu preset loader,
''' for BOTH games. Replaces the old MainForm.BuildFilteredPaste / BuildFilteredPasteSse pair, whose
''' "shared" record-field block was physically duplicated under a ⚠ SYNC OBLIGATION comment — the two
''' copies are now one code path with per-game branches only where the CARRIER genuinely differs.
'''
''' <para><b>Shape.</b> Start from a full clone of the SOURCE preset, then for every category the user did
''' NOT tick, overwrite that category with the target's current value. So a category is expressed ONCE
''' (its revert branch) instead of twice (a take-branch and a preserve-branch per game), and any preset
''' field that belongs to no category (SourcePath, unresolved head parts, unsupported-field counts…)
''' rides along with the source instead of being silently dropped.</para>
'''
''' <para><b>Preserve source of truth</b> — <c>baseline overlay if it declares the field, else the raw
''' record</c>. The baseline is the NPC's live overlay for Paste, and the PRE-DIALOG overlay for the
''' loader (whose live preview keeps rewriting <c>_appliedPresets</c> as the user clicks around, so
''' reading the current overlay there would preserve the previously PREVIEWED preset, not the NPC).
''' F4SE/RaceMenu-only carriers (BodySlide sliders, overlays, sculpt, body scale) have no record source,
''' so they preserve from the baseline or end up empty.</para>
'''
''' <para><b>Game gate.</b> A category that doesn't exist in the running game
''' (<see cref="PresetCategories.AppliesToGame"/>) is treated as unticked, so its carrier always ends up
''' holding the target's value and never the source's — that is what kept FO4 fields out of SSE pastes
''' when the two builders were separate.</para></summary>
Public Module PresetCategoryFilter

    ''' <summary>Build the preset to stamp as the NPC's overlay: source values for the ticked categories,
    ''' target values for the rest.</summary>
    ''' <param name="source">Preset being applied (clipboard preset for Paste, loaded LM/RaceMenu preset for Load).</param>
    ''' <param name="targetRaw">The target NPC's parsed record — the fallback preserve source.</param>
    ''' <param name="baseline">The target's overlay to preserve from (Nothing = no overlay, preserve from the record).</param>
    ''' <param name="options">Per-category user selection.</param>
    ''' <param name="isSse">True under Skyrim: SSE carriers are used and the FO4-only categories are inert.</param>
    ''' <param name="resolveHdpt">FormID → HDPT resolver for the orphan-Misc cascade. Nothing skips that step.</param>
    ''' <param name="resolveLmTemplate">LM skin-template resolver (FO4). Nothing skips the injected-HDPT tracker.</param>
    Public Function BuildFiltered(source As LooksmenuLoader.LooksmenuPreset,
                                  targetRaw As NPC_Data,
                                  baseline As LooksmenuLoader.LooksmenuPreset,
                                  options As PresetCategoryOptions,
                                  isSse As Boolean,
                                  Optional resolveHdpt As Func(Of UInteger, HDPT_Data) = Nothing,
                                  Optional resolveLmTemplate As NpcRecordOverlay.ResolveLmSkinTemplateDelegate = Nothing) As LooksmenuLoader.LooksmenuPreset
        If source Is Nothing Then Return Nothing
        Dim p = LooksmenuLoader.ClonePreset(source)

        For Each cat In AllCategories
            ' A category the running game doesn't have behaves like an unticked one: its carrier gets the
            ' target's value, so a cross-game source can never leak MRSV/FMRS into SSE (or sculpt into FO4).
            If options.Value(cat) AndAlso AppliesToGame(cat, isSse) Then Continue For
            Revert(p, cat, targetRaw, baseline, isSse)
        Next

        ' Replacing a main-type parent (e.g. a hair swap) orphans the target's raw Misc parts (hairlines):
        ' record them HERE, at the apply point, so Save drops them the same way Edit Face does. Empty when
        ' the parts were preserved or nothing was replaced, so lashes/AO/wet on untouched parents are safe.
        If resolveHdpt IsNot Nothing AndAlso targetRaw IsNot Nothing Then
            p.SuppressedRawHeadPartFormIDs = HeadPartResolver.ComputeReplacedParentOrphanMisc(
                targetRaw.HeadPartFormIDs, p.HeadPartFormIDs, resolveHdpt)
        End If

        ' If the result carries an LM skin template, populate the origin tracker so a later Retract (user
        ' switches template in EditBody) can identify exactly which HDPTs came from it. Without this the
        ' HDPTs would be stuck and a later combo change would duplicate them by PartType.
        If Not isSse AndAlso resolveLmTemplate IsNot Nothing AndAlso Not String.IsNullOrEmpty(p.SkinTemplateId) Then
            Dim tpl = resolveLmTemplate(p.SkinTemplateId)
            If tpl IsNot Nothing Then
                Dim genderIdx As Integer = If(p.Gender = 1, 1, 0)
                Dim head As UInteger = tpl.HeadHdptFormID(genderIdx)
                Dim rear As UInteger = tpl.HeadRearHdptFormID(genderIdx)
                If head <> 0UI AndAlso p.HeadPartFormIDs.Contains(head) Then p.LmTemplateInjectedHdptFormIDs.Add(head)
                If rear <> 0UI AndAlso p.HeadPartFormIDs.Contains(rear) Then p.LmTemplateInjectedHdptFormIDs.Add(rear)
                ' HasHeadPartFormIDsSetByTemplate stays as-is: the Has* assertion below is made on
                ' snapshot grounds, independent of the template, so Retract must not flip it off.
            End If
        End If

        Return p
    End Function

    ''' <summary>Overwrite ONE category of <paramref name="p"/> with the target's current value: the
    ''' baseline overlay's when it declares that field, else the raw record's. Record-less carriers
    ''' (F4SE/RaceMenu-only) end up empty when there is no baseline.</summary>
    Private Sub Revert(p As LooksmenuLoader.LooksmenuPreset,
                       cat As PresetCategory,
                       raw As NPC_Data,
                       baseline As LooksmenuLoader.LooksmenuPreset,
                       isSse As Boolean)
        Select Case cat

            Case PresetCategory.BodyWeight
                If isSse Then
                    If baseline IsNot Nothing AndAlso baseline.SseWeight.HasValue Then
                        p.SseWeight = baseline.SseWeight.Value
                    ElseIf raw IsNot Nothing AndAlso raw.Nam7Raw IsNot Nothing AndAlso raw.Nam7Raw.Length >= 4 Then
                        p.SseWeight = BitConverter.ToSingle(raw.Nam7Raw, 0)
                    Else
                        p.SseWeight = 100.0F
                    End If
                Else
                    p.WeightThin = PickSingle(If(baseline Is Nothing, Nothing, baseline.WeightThin), If(raw Is Nothing, 0.0F, raw.WeightThin))
                    p.WeightMuscular = PickSingle(If(baseline Is Nothing, Nothing, baseline.WeightMuscular), If(raw Is Nothing, 0.0F, raw.WeightMuscular))
                    p.WeightFat = PickSingle(If(baseline Is Nothing, Nothing, baseline.WeightFat), If(raw Is Nothing, 0.0F, raw.WeightFat))
                End If

            Case PresetCategory.BodyRegions
                ' MRSV per-region weights (FO4). Either source leaves the list populated, so the result
                ' authoritatively defines the field — Has* True in both branches, as Paste always did.
                p.BodyMorphValues.Clear()
                If baseline IsNot Nothing AndAlso baseline.HasBodyMorphValues Then
                    p.BodyMorphValues.AddRange(baseline.BodyMorphValues)
                ElseIf raw IsNot Nothing Then
                    p.BodyMorphValues.AddRange(raw.BodyMorphRegionValues)
                End If
                p.HasBodyMorphValues = True

            Case PresetCategory.BodySliders
                ' BodySlide vertex morphs — F4SE/RaceMenu-only, no record source. Empty = "vanilla NPC,
                ' no overlay sliders", which is what the resolver reads when nothing was ever applied.
                p.BodyMorphSliders.Clear()
                p.BodyMorphsKeyed = Nothing
                If baseline IsNot Nothing Then
                    For Each kv In baseline.BodyMorphSliders
                        p.BodyMorphSliders(kv.Key) = kv.Value
                    Next
                    If isSse Then p.BodyMorphsKeyed = CloneBodyMorphsKeyed(baseline.BodyMorphsKeyed)
                End If

            Case PresetCategory.BodyScale
                ' RaceMenu NiOverride node transforms (SSE-only) — overlay-only carrier.
                p.SseNodeTransforms = If(baseline Is Nothing, Nothing, LooksmenuLoader.CloneSseNodeTransforms(baseline.SseNodeTransforms))

            Case PresetCategory.Overlays
                ' Body tattoos / paint. FO4: template-based Overlays list. SSE: path-based RaceMenu overlay
                ' nodes PLUS the per-slot skin overrides (the other body-texture layer, same category).
                p.Overlays.Clear()
                p.HasOverlays = False
                p.SseBodyOverlays = Nothing
                p.SseSkinOverrides = Nothing
                If baseline IsNot Nothing Then
                    If isSse Then
                        p.SseBodyOverlays = LooksmenuLoader.CloneSseBodyOverlays(baseline.SseBodyOverlays)
                        p.SseSkinOverrides = LooksmenuLoader.CloneSseSkinOverrides(baseline.SseSkinOverrides)
                    Else
                        For Each ov In baseline.Overlays
                            p.Overlays.Add(New LooksmenuLoader.OverlayEntry With {
                                .TemplateId = ov.TemplateId,
                                .Priority = ov.Priority,
                                .Tint = If(ov.Tint Is Nothing, Nothing, CType(ov.Tint.Clone(), Single())),
                                .OffsetUV = If(ov.OffsetUV Is Nothing, Nothing, CType(ov.OffsetUV.Clone(), Single())),
                                .ScaleUV = If(ov.ScaleUV Is Nothing, Nothing, CType(ov.ScaleUV.Clone(), Single()))
                            })
                        Next
                        p.HasOverlays = baseline.HasOverlays
                    End If
                End If

            Case PresetCategory.SkinOverride
                ' NPC.WNAM. Nothing = "no override", which the overlay merge resolves to the raw record's
                ' own skin — so preserving means carrying the baseline's override (if any) and nothing else.
                p.SkinFormIDOverride = If(baseline Is Nothing, Nothing, baseline.SkinFormIDOverride)

            Case PresetCategory.LmSkinTemplate
                ' F4SE LM SkinInterface template (FO4-only), separate from the WNAM record skin.
                p.SkinTemplateId = If(baseline Is Nothing, "", If(baseline.SkinTemplateId, ""))
                p.LmTemplateInjectedHdptFormIDs.Clear()
                p.HasHeadPartFormIDsSetByTemplate = False
                If baseline IsNot Nothing Then
                    For Each fid In baseline.LmTemplateInjectedHdptFormIDs
                        p.LmTemplateInjectedHdptFormIDs.Add(fid)
                    Next
                    p.HasHeadPartFormIDsSetByTemplate = baseline.HasHeadPartFormIDsSetByTemplate
                End If

            Case PresetCategory.Outfit
                ' NPC.DOFT + NPC.SOFT. Same Nothing-means-fall-back-to-record rule as the skin override.
                p.DefaultOutfitFormIDOverride = If(baseline Is Nothing, Nothing, baseline.DefaultOutfitFormIDOverride)
                p.SleepOutfitFormIDOverride = If(baseline Is Nothing, Nothing, baseline.SleepOutfitFormIDOverride)

            Case PresetCategory.FaceParts
                ' The overlay merge does wipe + race defaults + preset entries, so preserving means copying
                ' the target's own head parts in: the merge's "preset wins per type" rule then re-establishes
                ' exactly what the NPC had. The unresolved-part lists and the SSE head FTST travel with the
                ' parts (they describe the same selection).
                p.HeadPartFormIDs.Clear()
                p.UnresolvedHeadParts.Clear()
                p.SseUnresolvedHeadParts.Clear()
                p.HeadPartFormIDsIncludeRawExtras = False
                p.SseHeadTextureFormID = 0UI
                If baseline IsNot Nothing AndAlso baseline.HasHeadPartFormIDs Then
                    p.HeadPartFormIDs.AddRange(baseline.HeadPartFormIDs)
                    p.UnresolvedHeadParts.AddRange(baseline.UnresolvedHeadParts)
                    p.SseUnresolvedHeadParts.AddRange(baseline.SseUnresolvedHeadParts)
                    p.HeadPartFormIDsIncludeRawExtras = baseline.HeadPartFormIDsIncludeRawExtras
                ElseIf raw IsNot Nothing Then
                    p.HeadPartFormIDs.AddRange(raw.HeadPartFormIDs)
                End If
                If baseline IsNot Nothing Then p.SseHeadTextureFormID = baseline.SseHeadTextureFormID
                p.HasHeadPartFormIDs = True

            Case PresetCategory.HairColor
                ' HCLF FormID (both games) + the RaceMenu custom RGB (SSE), which is the same decision.
                If baseline IsNot Nothing AndAlso baseline.HairColorFormID <> 0UI Then
                    p.HairColorFormID = baseline.HairColorFormID
                Else
                    p.HairColorFormID = If(raw Is Nothing, 0UI, raw.HairColorFormID)
                End If
                p.SseHairColorRgb = If(baseline Is Nothing, Nothing, baseline.SseHairColorRgb)
                ' El identificador sin resolver viaja con el color: acá el color se REEMPLAZA por el del
                ' baseline/raw, así que el crudo del archivo dejaría de describirlo. Sin limpiarlo, un
                ' HairColorFormID=0 legítimo (el baseline no tenía color) más un UnresolvedHairColor viejo
                ' hacen que el auditor reporte "el mod no está instalado" para un color que se descartó a
                ' propósito.
                p.UnresolvedHairColor = ""

            Case PresetCategory.FaceTints
                If isSse Then
                    p.SseTintRawOverride = Nothing
                    p.SseTintTexOverride = Nothing
                    p.HasSseTints = False
                    If baseline IsNot Nothing AndAlso baseline.HasSseTints AndAlso baseline.SseTintRawOverride IsNot Nothing Then
                        p.SseTintRawOverride = CloneSseTintRaw(baseline.SseTintRawOverride)
                        p.HasSseTints = True
                        If baseline.SseTintTexOverride IsNot Nothing Then p.SseTintTexOverride = New Dictionary(Of Integer, String)(baseline.SseTintTexOverride)
                    ElseIf raw IsNot Nothing AndAlso raw.SseTintRaw IsNot Nothing AndAlso raw.SseTintRaw.Count > 0 Then
                        p.SseTintRawOverride = CloneSseTintRaw(raw.SseTintRaw)
                        p.HasSseTints = True
                    End If
                Else
                    p.FaceTintLayers.Clear()
                    Dim src = If(baseline IsNot Nothing AndAlso baseline.HasFaceTintLayers, baseline.FaceTintLayers,
                                 If(raw Is Nothing, Nothing, raw.FaceTintLayers))
                    If src IsNot Nothing Then
                        For Each tl In src
                            p.FaceTintLayers.Add(LooksmenuLoader.CloneFaceTintLayer(tl))
                        Next
                    End If
                    p.HasFaceTintLayers = True
                End If

            Case PresetCategory.FaceVertexMorphs
                If isSse Then
                    ' NAM9 (18 floats) + NAMA (4 type uints) + the RaceMenu custom morphs (no record source).
                    If baseline IsNot Nothing AndAlso baseline.HasSseMorphs AndAlso baseline.SseNam9 IsNot Nothing Then
                        p.SseNam9 = DirectCast(baseline.SseNam9.Clone(), Single())
                        p.SseNama = If(baseline.SseNama Is Nothing, New UInteger(SseNam9MorphMap.NamaFamilyCount - 1) {}, DirectCast(baseline.SseNama.Clone(), UInteger()))
                        p.HasSseMorphs = True
                    Else
                        Dim nam9(SseNam9MorphMap.Nam9SliderCount - 1) As Single
                        For i = 0 To SseNam9MorphMap.Nam9SliderCount - 1
                            If raw IsNot Nothing AndAlso raw.Nam9Raw IsNot Nothing AndAlso raw.Nam9Raw.Length >= (i + 1) * 4 Then
                                Dim v = BitConverter.ToSingle(raw.Nam9Raw, i * 4)
                                If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then v = 0.0F
                                nam9(i) = v
                            End If
                        Next
                        Dim nama(SseNam9MorphMap.NamaFamilyCount - 1) As UInteger
                        For f = 0 To SseNam9MorphMap.NamaFamilyCount - 1
                            If raw IsNot Nothing AndAlso raw.NamaRaw IsNot Nothing AndAlso raw.NamaRaw.Length >= (f + 1) * 4 Then
                                Dim tv = BitConverter.ToUInt32(raw.NamaRaw, f * 4)
                                If tv = SseNam9MorphMap.NamaUnset Then tv = 0UI
                                nama(f) = tv
                            End If
                        Next
                        p.SseNam9 = nam9
                        p.SseNama = nama
                        p.HasSseMorphs = (raw IsNot Nothing AndAlso (raw.Nam9Raw IsNot Nothing OrElse raw.NamaRaw IsNot Nothing))
                    End If
                Else
                    p.ChargenFaceMorphs.Clear()
                    Dim src = If(baseline IsNot Nothing AndAlso baseline.HasChargenFaceMorphs, baseline.ChargenFaceMorphs,
                                 If(raw Is Nothing, Nothing, raw.MorphValues))
                    If src IsNot Nothing Then
                        For Each kv In src
                            p.ChargenFaceMorphs(kv.Key) = kv.Value
                        Next
                    End If
                    p.HasChargenFaceMorphs = True
                End If

            Case PresetCategory.CustomMorphs
                ' RaceMenu NiOverride custom morphs (SSE-only). Separate pipeline from the record's
                ' NAM9/NAMA above: no record source, so preserving means the baseline's or nothing.
                p.SseCustomMorphs = If(baseline Is Nothing, Nothing, CloneSseCustomMorphs(baseline.SseCustomMorphs))

            Case PresetCategory.FaceBoneRegions
                ' FMRS regions and FMIN intensity are paired: the engine always overwrites the intensity,
                ' so preserving the regions has to preserve the intensity too.
                p.FaceBoneRegions.Clear()
                If baseline IsNot Nothing AndAlso baseline.HasFaceBoneRegions Then
                    For Each kv In baseline.FaceBoneRegions
                        p.FaceBoneRegions(kv.Key) = If(kv.Value Is Nothing, Nothing, CType(kv.Value.Clone(), Single()))
                    Next
                    p.FacialMorphIntensity = baseline.FacialMorphIntensity
                ElseIf raw IsNot Nothing Then
                    For Each fm In raw.FaceMorphs
                        p.FaceBoneRegions(fm.Index) = fm.Values.ToArray()
                    Next
                    p.FacialMorphIntensity = raw.FacialMorphIntensity
                End If
                p.HasFaceBoneRegions = True

            Case PresetCategory.Sculpt
                ' Per-vertex head/shape sculpt (SSE) — RaceMenu-only carrier, no record source.
                p.SseSculptHead = If(baseline Is Nothing, Nothing, CloneSseSculptHead(baseline.SseSculptHead))
                p.SseSculptParts = If(baseline Is Nothing, Nothing, LooksmenuLoader.CloneSseSculptParts(baseline.SseSculptParts))

            Case PresetCategory.IsCharGenPreset
                Const AcbsBitIsCharGenFacePreset As UInteger = &H4UI
                If baseline IsNot Nothing AndAlso baseline.IsCharGenFacePreset.HasValue Then
                    p.IsCharGenFacePreset = baseline.IsCharGenFacePreset.Value
                ElseIf raw IsNot Nothing Then
                    p.IsCharGenFacePreset = ((raw.AcbsFlags And AcbsBitIsCharGenFacePreset) <> 0UI)
                Else
                    p.IsCharGenFacePreset = Nothing
                End If

        End Select
    End Sub

    ''' <summary>Baseline value when the overlay declares one, else the record's.</summary>
    Private Function PickSingle(overlayValue As Single?, recordValue As Single) As Single
        Return If(overlayValue.HasValue, overlayValue.Value, recordValue)
    End Function

    ''' <summary>Deep-copy an SSE keyed body-morph dict (morph name → BodySlide key → value). Nothing in,
    ''' Nothing out; the copy is independent so later edits never mutate the source/baseline overlay.</summary>
    Public Function CloneBodyMorphsKeyed(src As Dictionary(Of String, Dictionary(Of String, Single))) As Dictionary(Of String, Dictionary(Of String, Single))
        If src Is Nothing Then Return Nothing
        Dim c As New Dictionary(Of String, Dictionary(Of String, Single))(StringComparer.OrdinalIgnoreCase)
        For Each kv In src
            Dim inner As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
            If kv.Value IsNot Nothing Then
                For Each ik In kv.Value : inner(ik.Key) = ik.Value : Next
            End If
            c(kv.Key) = inner
        Next
        Return c
    End Function

    ''' <summary>Deep-copy the SSE per-vertex head sculpt list. Nothing in, Nothing out.</summary>
    Public Function CloneSseSculptHead(src As List(Of NPC_SculptVert)) As List(Of NPC_SculptVert)
        If src Is Nothing Then Return Nothing
        Dim c As New List(Of NPC_SculptVert)(src.Count)
        For Each sv In src
            If sv Is Nothing Then Continue For
            c.Add(New NPC_SculptVert With {.Index = sv.Index, .Dx = sv.Dx, .Dy = sv.Dy, .Dz = sv.Dz})
        Next
        Return c
    End Function

    ''' <summary>Deep-copy the SSE NiOverride custom-morph list. Nothing in, Nothing out.</summary>
    Public Function CloneSseCustomMorphs(src As List(Of NPC_CustomMorph)) As List(Of NPC_CustomMorph)
        If src Is Nothing Then Return Nothing
        Dim c As New List(Of NPC_CustomMorph)(src.Count)
        For Each cm In src
            If cm Is Nothing Then Continue For
            c.Add(New NPC_CustomMorph With {.Name = cm.Name, .Value = cm.Value})
        Next
        Return c
    End Function

    ''' <summary>Deep-copy the SSE flat tint subrecord list (TINI/TINC/TINV/TIAS), cloning each byte array.</summary>
    Public Function CloneSseTintRaw(src As List(Of NPC_RawSubrecord)) As List(Of NPC_RawSubrecord)
        If src Is Nothing Then Return Nothing
        Dim c As New List(Of NPC_RawSubrecord)(src.Count)
        For Each sr In src
            If sr Is Nothing Then Continue For
            c.Add(New NPC_RawSubrecord With {
                .Sig = sr.Sig,
                .Data = If(sr.Data Is Nothing, Nothing, CType(sr.Data.Clone(), Byte())),
                .IsFormId = sr.IsFormId
            })
        Next
        Return c
    End Function

End Module
