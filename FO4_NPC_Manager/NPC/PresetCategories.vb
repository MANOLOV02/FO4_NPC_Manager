Imports FO4_Base_Library

''' <summary>UNIFIED preset-category model, shared by Paste Look and by the LooksMenu/RaceMenu preset
''' loader. One enum, one options struct, one "what does this preset actually carry" describer — so the
''' two features can never drift apart in which categories exist, what they mean, or which game shows them.
'''
''' <para>A category = ONE independent appearance pipeline (00-reglas-ui-y-vb "toggles granulares
''' por pipeline"). Checked = take this category from the SOURCE preset; unchecked = keep whatever the
''' TARGET NPC currently shows. The actual merge lives in <see cref="PresetCategoryFilter"/>.</para>
'''
''' <para>Game-awareness: some categories only exist in one engine (MRSV body regions, FMRS face bone
''' regions and the F4SE LM skin template are FO4-only; per-vertex sculpt and RaceMenu body scale are
''' SSE-only). <see cref="AppliesToGame"/> is the single source of truth for that, consumed by the UI to
''' hide rows and by the filter to skip carriers.</para></summary>
Public Module PresetCategories

    ''' <summary>Every independently-toggleable appearance category. Values are stable identifiers used by
    ''' the UI (one checkbox each) and by the filter (one revert branch each).</summary>
    Public Enum PresetCategory
        BodyWeight
        BodyRegions
        BodySliders
        BodyScale
        Overlays
        SkinOverride
        LmSkinTemplate
        Outfit
        FaceParts
        HairColor
        FaceTints
        FaceVertexMorphs
        CustomMorphs
        FaceBoneRegions
        Sculpt
        IsCharGenPreset
    End Enum

    ''' <summary>All categories in display order. Used by the panel to iterate rows and by the filter to
    ''' iterate revert branches — adding a category here (plus its Designer row) is the only place a new
    ''' pipeline has to be registered.</summary>
    Public ReadOnly AllCategories As PresetCategory() = {
        PresetCategory.BodyWeight, PresetCategory.BodyRegions, PresetCategory.BodySliders,
        PresetCategory.BodyScale, PresetCategory.Overlays, PresetCategory.SkinOverride,
        PresetCategory.LmSkinTemplate, PresetCategory.Outfit,
        PresetCategory.FaceParts, PresetCategory.HairColor, PresetCategory.FaceTints,
        PresetCategory.FaceVertexMorphs, PresetCategory.CustomMorphs, PresetCategory.FaceBoneRegions, PresetCategory.Sculpt,
        PresetCategory.IsCharGenPreset
    }

    ''' <summary>True if the category exists in the given game. FO4-only: MRSV body regions, FMRS face
    ''' bone regions, F4SE LM skin template. SSE-only: per-vertex sculpt, RaceMenu node-transform body
    ''' scale. Everything else is shared (record fields or an equivalent carrier in both engines).</summary>
    Public Function AppliesToGame(cat As PresetCategory, isSse As Boolean) As Boolean
        Select Case cat
            Case PresetCategory.BodyRegions, PresetCategory.FaceBoneRegions, PresetCategory.LmSkinTemplate
                Return Not isSse
            Case PresetCategory.Sculpt, PresetCategory.BodyScale, PresetCategory.CustomMorphs
                Return isSse
            Case Else
                Return True
        End Select
    End Function

    ''' <summary>Per-category boolean flags. True = take the field from the SOURCE preset; False = keep the
    ''' TARGET NPC's current value. Categories that don't apply to the running game are ignored by the
    ''' filter regardless of their flag.</summary>
    Public Structure PresetCategoryOptions
        Public BodyWeight As Boolean         ' FO4: WeightThin/Muscular/Fat (MWGT) — SSE: SseWeight (NAM7)
        Public BodyRegions As Boolean        ' BodyMorphValues (MRSV) — FO4-only
        Public BodySliders As Boolean        ' BodySlide vertex morphs (BodyMorphSliders / BodyMorphsKeyed)
        Public BodyScale As Boolean          ' SseNodeTransforms (RaceMenu NiOverride) — TRS COMPLETO por hueso
        '                                     (escala + posición + rotación), no sólo escala. SSE-only.
        Public Overlays As Boolean           ' Body tattoos/paint (FO4 Overlays — SSE SseBodyOverlays + SseSkinOverrides)
        Public SkinOverride As Boolean       ' SkinFormIDOverride (NPC.WNAM record skin)
        Public LmSkinTemplate As Boolean     ' SkinTemplateId (F4SE LM SkinInterface) — FO4-only
        Public Outfit As Boolean             ' DefaultOutfitFormIDOverride + SleepOutfitFormIDOverride (DOFT/SOFT)
        Public FaceParts As Boolean          ' HeadPartFormIDs (+ SSE head FTST override)
        Public HairColor As Boolean          ' HairColorFormID (+ SSE RaceMenu custom RGB)
        Public FaceTints As Boolean          ' FO4 FaceTintLayers — SSE SseTintLayers + mask textures
        Public FaceVertexMorphs As Boolean   ' FO4 ChargenFaceMorphs (MSDV) — SSE NAM9/NAMA (record-backed)
        Public CustomMorphs As Boolean       ' SseCustomMorphs (RaceMenu NiOverride, no record source) — SSE-only
        Public FaceBoneRegions As Boolean    ' FaceBoneRegions (FMRS) + FacialMorphIntensity — FO4-only
        Public Sculpt As Boolean             ' SseSculptHead/SseSculptParts (per-vertex) — SSE-only
        Public IsCharGenPreset As Boolean    ' ACBS bit 0x04

        ''' <summary>Read one flag by category.</summary>
        Public Function Value(cat As PresetCategory) As Boolean
            Select Case cat
                Case PresetCategory.BodyWeight : Return BodyWeight
                Case PresetCategory.BodyRegions : Return BodyRegions
                Case PresetCategory.BodySliders : Return BodySliders
                Case PresetCategory.BodyScale : Return BodyScale
                Case PresetCategory.Overlays : Return Overlays
                Case PresetCategory.SkinOverride : Return SkinOverride
                Case PresetCategory.LmSkinTemplate : Return LmSkinTemplate
                Case PresetCategory.Outfit : Return Outfit
                Case PresetCategory.FaceParts : Return FaceParts
                Case PresetCategory.HairColor : Return HairColor
                Case PresetCategory.FaceTints : Return FaceTints
                Case PresetCategory.FaceVertexMorphs : Return FaceVertexMorphs
                Case PresetCategory.CustomMorphs : Return CustomMorphs
                Case PresetCategory.FaceBoneRegions : Return FaceBoneRegions
                Case PresetCategory.Sculpt : Return Sculpt
                Case PresetCategory.IsCharGenPreset : Return IsCharGenPreset
                Case Else : Return False
            End Select
        End Function

        ''' <summary>Write one flag by category.</summary>
        Public Sub SetValue(cat As PresetCategory, v As Boolean)
            Select Case cat
                Case PresetCategory.BodyWeight : BodyWeight = v
                Case PresetCategory.BodyRegions : BodyRegions = v
                Case PresetCategory.BodySliders : BodySliders = v
                Case PresetCategory.BodyScale : BodyScale = v
                Case PresetCategory.Overlays : Overlays = v
                Case PresetCategory.SkinOverride : SkinOverride = v
                Case PresetCategory.LmSkinTemplate : LmSkinTemplate = v
                Case PresetCategory.Outfit : Outfit = v
                Case PresetCategory.FaceParts : FaceParts = v
                Case PresetCategory.HairColor : HairColor = v
                Case PresetCategory.FaceTints : FaceTints = v
                Case PresetCategory.FaceVertexMorphs : FaceVertexMorphs = v
                Case PresetCategory.CustomMorphs : CustomMorphs = v
                Case PresetCategory.FaceBoneRegions : FaceBoneRegions = v
                Case PresetCategory.Sculpt : Sculpt = v
                Case PresetCategory.IsCharGenPreset : IsCharGenPreset = v
            End Select
        End Sub

        ''' <summary>All categories on — the legacy "paste everything" / "load everything" behaviour.</summary>
        Public Shared Function All() As PresetCategoryOptions
            Dim o As New PresetCategoryOptions
            For Each c In AllCategories
                o.SetValue(c, True)
            Next
            Return o
        End Function
    End Structure

    ''' <summary>What a given preset actually carries for one category: whether it DECLARES the category at
    ''' all, and how much of it there is. <see cref="Available"/> False means the preset has nothing to give
    ''' (the UI greys the row out) — it is NOT the same as a declared-but-empty category, which is an
    ''' authoritative wipe and stays selectable.</summary>
    Public Class CategoryInfo
        ''' <summary>The preset declares this category (there is something to take).</summary>
        Public Available As Boolean
        ''' <summary>Short right-aligned amount shown next to the checkbox ("12", "yes", "—").</summary>
        Public Text As String = "—"
        ''' <summary>Longer breakdown for the row tooltip. Empty when the short text says it all.</summary>
        Public Detail As String = ""
    End Class

    ''' <summary>Describe every category of <paramref name="p"/> for the running game: what it carries and
    ''' how much. Single source of truth for the counts shown by BOTH the loader panel and the paste panel.
    ''' Nothing in → every category unavailable.</summary>
    Public Function Describe(p As LooksmenuLoader.LooksmenuPreset, isSse As Boolean) As Dictionary(Of PresetCategory, CategoryInfo)
        Dim d As New Dictionary(Of PresetCategory, CategoryInfo)
        For Each c In AllCategories
            d(c) = New CategoryInfo()
        Next
        If p Is Nothing Then Return d

        ' --- Body weight ---
        If isSse Then
            If p.SseWeight.HasValue Then Set0(d, PresetCategory.BodyWeight, $"{p.SseWeight.Value:0}", "NAM7 weight")
        Else
            Dim wn = If(p.WeightThin.HasValue, 1, 0) + If(p.WeightMuscular.HasValue, 1, 0) + If(p.WeightFat.HasValue, 1, 0)
            If wn > 0 Then Set0(d, PresetCategory.BodyWeight, "yes",
                                $"Thin {Fmt(p.WeightThin)} / Muscular {Fmt(p.WeightMuscular)} / Fat {Fmt(p.WeightFat)}")
        End If

        ' --- Body regions (MRSV, FO4-only) ---
        If Not isSse AndAlso (p.HasBodyMorphValues OrElse p.BodyMorphValues.Count > 0) Then
            Set0(d, PresetCategory.BodyRegions, p.BodyMorphValues.Count.ToString(), "MRSV per-region weights")
        End If

        ' --- Body sliders (BodySlide morphs) ---
        Dim sliderCount As Integer = p.BodyMorphSliders.Count
        Dim keyedDetail As String = ""
        If isSse AndAlso p.BodyMorphsKeyed IsNot Nothing Then
            Dim keys As Integer = 0
            For Each kv In p.BodyMorphsKeyed
                If kv.Value IsNot Nothing Then keys += kv.Value.Count
            Next
            sliderCount = p.BodyMorphsKeyed.Count
            keyedDetail = $"{p.BodyMorphsKeyed.Count} morphs across {keys} BodySlide keys"
        End If
        If sliderCount > 0 Then Set0(d, PresetCategory.BodySliders, sliderCount.ToString(), keyedDetail)

        ' --- Body scale (RaceMenu node transforms, SSE-only) ---
        If isSse AndAlso p.SseNodeTransforms IsNot Nothing AndAlso p.SseNodeTransforms.Count > 0 Then
            ' DECÍA "NiOverride node transforms": el tooltip re-metía la jerga que se le sacó al rótulo de la tilde.
            Set0(d, PresetCategory.BodyScale, p.SseNodeTransforms.Count.ToString(),
                 $"{p.SseNodeTransforms.Count} bone(s) moved, rotated or resized")
        End If

        ' --- Overlays (+ SSE skin overrides, which ride along as the other body texture layer) ---
        If isSse Then
            Dim ov = If(p.SseBodyOverlays Is Nothing, 0, p.SseBodyOverlays.Count)
            Dim sk = If(p.SseSkinOverrides Is Nothing, 0, p.SseSkinOverrides.Count)
            If ov + sk > 0 Then Set0(d, PresetCategory.Overlays, (ov + sk).ToString(), $"{ov} overlay nodes + {sk} skin overrides")
        ElseIf p.HasOverlays OrElse p.Overlays.Count > 0 Then
            Set0(d, PresetCategory.Overlays, p.Overlays.Count.ToString(), "F4SE overlay templates")
        End If

        ' --- Skin override (NPC.WNAM) ---
        If p.SkinFormIDOverride.HasValue Then
            Set0(d, PresetCategory.SkinOverride, If(p.SkinFormIDOverride.Value = 0UI, "none", "yes"),
                 $"WNAM 0x{p.SkinFormIDOverride.Value:X8}")
        End If

        ' --- LM skin template (FO4-only) ---
        If Not isSse AndAlso Not String.IsNullOrEmpty(p.SkinTemplateId) Then
            Set0(d, PresetCategory.LmSkinTemplate, "yes", p.SkinTemplateId)
        End If

        ' --- Outfit (DOFT + SOFT) ---
        If p.DefaultOutfitFormIDOverride.HasValue OrElse p.SleepOutfitFormIDOverride.HasValue Then
            Dim n = If(p.DefaultOutfitFormIDOverride.HasValue, 1, 0) + If(p.SleepOutfitFormIDOverride.HasValue, 1, 0)
            Set0(d, PresetCategory.Outfit, n.ToString(),
                 $"DOFT {FmtFid(p.DefaultOutfitFormIDOverride)} / SOFT {FmtFid(p.SleepOutfitFormIDOverride)}")
        End If

        ' --- Face parts (head parts + el head TXST de SSE; los irresolubles van al tooltip) ---
        ' El gate incluye el head TXST y NO sólo los head parts: el override viaja en la MISMA categoría
        ' (PresetCategoryFilter, Case FaceParts), así que un preset que trae headTexture pero ningún head part
        ' —.jslot sin array `headParts`, o con todos irresolubles— no emitía fila, la categoría no aparecía en
        ' el diálogo, el usuario no podía tildarla y el Revert descartaba el headTexture sin decir nada.
        Dim hasFtstOv As Boolean = isSse AndAlso p.SseHeadTextureFormIDOverride.HasValue
        If p.HasHeadPartFormIDs OrElse p.HeadPartFormIDs.Count > 0 OrElse hasFtstOv Then
            Dim det = ""
            If p.UnresolvedHeadParts.Count > 0 Then det = $"{p.UnresolvedHeadParts.Count} unresolved (owning plugin not loaded)"
            ' El marcador del FTST va al TEXTO CORTO, no sólo al tooltip: el clear es destructivo sobre el target
            ' y el conteo de head parts NO cambia al agregarlo ⇒ sin hover era invisible.
            ' Tiene que ser CORTO: la celda del contador es una columna ABSOLUTA de 74px con un Label de ~68px
            ' sin AutoSize ni AutoEllipsis (PresetCategoryPanel.Designer :350), o sea ~9 caracteres. Un texto tipo
            ' "12 + FTST cleared" se recorta y el fix no sirve de nada. El detalle largo va al tooltip.
            Dim txt As String = p.HeadPartFormIDs.Count.ToString()
            If hasFtstOv Then
                If p.SseHeadTextureFormIDOverride.Value = 0UI Then
                    txt &= " ✕FTST"
                    det = If(det.Length > 0, det & "  •  ", "") & "head FTST: cleared (no FTST subrecord emitted)"
                Else
                    txt &= " +FTST"
                    det = If(det.Length > 0, det & "  •  ", "") & $"head FTST 0x{p.SseHeadTextureFormIDOverride.Value:X8}"
                End If
            End If
            Set0(d, PresetCategory.FaceParts, txt, det)
        End If

        ' --- Hair color ---
        If p.HairColorFormID <> 0UI Then
            Set0(d, PresetCategory.HairColor, "yes", $"HCLF 0x{p.HairColorFormID:X8}")
        ElseIf isSse AndAlso p.SseHairColorRgb.HasValue Then
            Set0(d, PresetCategory.HairColor, "yes", $"RaceMenu RGB 0x{p.SseHairColorRgb.Value:X6}")
        End If

        ' --- Face tints ---
        If isSse Then
            If p.HasSseTints AndAlso p.SseTintLayers IsNot Nothing Then
                Dim layers As Integer = 0
                For Each sr In p.SseTintLayers
                    If sr IsNot Nothing AndAlso sr.Indice.HasValue Then layers += 1
                Next
                Dim tex = If(p.SseTintTexOverride Is Nothing, 0, p.SseTintTexOverride.Count)
                Set0(d, PresetCategory.FaceTints, layers.ToString(),
                     If(tex > 0, $"{tex} custom mask textures", ""))
            End If
        ElseIf p.HasFaceTintLayers OrElse p.FaceTintLayers.Count > 0 Then
            Set0(d, PresetCategory.FaceTints, p.FaceTintLayers.Count.ToString(), "")
        End If
        ' El ajuste del tono del CUERPO viaja dentro de esta categoria (no es una categoria nueva: es un
        ' ajuste de tinte). Si el preset lo trae hay que DECIRLO, o el usuario acepta "Face tints" sin
        ' enterarse de que tambien le cambia el tono del cuerpo. Marca la categoria como disponible aunque el
        ' preset no tenga NINGUNA capa de tint: el ajuste solo ya es algo que tomar.
        If p.SkinToneOffset IsNot Nothing AndAlso Not p.SkinToneOffset.IsZero Then
            Dim info = d(PresetCategory.FaceTints)
            Dim extra = $"body skin tint adjustment ({p.SkinToneOffset})"
            info.Detail = If(String.IsNullOrEmpty(info.Detail), extra, info.Detail & " · " & extra)
            If Not info.Available Then
                info.Available = True
                info.Text = "yes"
            End If
        End If

        ' --- Face morphs ---
        If isSse Then
            Dim nam9Set As Integer = 0
            If p.SseNam9 IsNot Nothing Then
                For Each v In p.SseNam9
                    If v <> 0.0F Then nam9Set += 1
                Next
            End If
            If p.HasSseMorphs Then
                Set0(d, PresetCategory.FaceVertexMorphs, nam9Set.ToString(), "NAM9 sliders with a non-zero value")
            End If
            ' RaceMenu NiOverride custom morphs are a SEPARATE pipeline from the record's NAM9/NAMA: no record
            ' source, they live in the .jslot / sidecar. Own category so a preset's vanilla sliders can be taken
            ' without dragging in morphs that depend on mods the user may not have.
            Dim custom = If(p.SseCustomMorphs Is Nothing, 0, p.SseCustomMorphs.Count)
            If custom > 0 Then Set0(d, PresetCategory.CustomMorphs, custom.ToString(), "RaceMenu NiOverride morphs")
        ElseIf p.HasChargenFaceMorphs OrElse p.ChargenFaceMorphs.Count > 0 Then
            Set0(d, PresetCategory.FaceVertexMorphs, p.ChargenFaceMorphs.Count.ToString(), "chargen MSDV sliders")
        End If

        ' --- Face bone regions (FMRS, FO4-only) ---
        If Not isSse AndAlso (p.HasFaceBoneRegions OrElse p.FaceBoneRegions.Count > 0) Then
            Set0(d, PresetCategory.FaceBoneRegions, p.FaceBoneRegions.Count.ToString(),
                 $"morph intensity {p.FacialMorphIntensity:0.###}")
        End If

        ' --- Sculpt (SSE-only): head verts + per-shape parts ---
        If isSse Then
            Dim headVerts = If(p.SseSculptHead Is Nothing, 0, p.SseSculptHead.Count)
            Dim partVerts As Integer = 0
            Dim parts As Integer = 0
            If p.SseSculptParts IsNot Nothing Then
                parts = p.SseSculptParts.Count
                For Each sp In p.SseSculptParts
                    If sp IsNot Nothing AndAlso sp.Verts IsNot Nothing Then partVerts += sp.Verts.Count
                Next
            End If
            If headVerts + partVerts > 0 Then
                Set0(d, PresetCategory.Sculpt, (headVerts + partVerts).ToString(),
                     $"{headVerts} head verts + {partVerts} verts across {parts} shapes")
            End If
        End If

        ' --- CharGen face preset flag ---
        If p.IsCharGenFacePreset.HasValue Then
            Set0(d, PresetCategory.IsCharGenPreset, If(p.IsCharGenFacePreset.Value, "on", "off"), "ACBS bit 0x04")
        End If

        Return d
    End Function

    Private Sub Set0(d As Dictionary(Of PresetCategory, CategoryInfo), cat As PresetCategory, text As String, detail As String)
        d(cat).Available = True
        d(cat).Text = text
        d(cat).Detail = If(detail, "")
    End Sub

    Private Function Fmt(v As Single?) As String
        Return If(v.HasValue, v.Value.ToString("0.###"), "—")
    End Function

    Private Function FmtFid(v As UInteger?) As String
        Return If(v.HasValue, $"0x{v.Value:X8}", "—")
    End Function

End Module
