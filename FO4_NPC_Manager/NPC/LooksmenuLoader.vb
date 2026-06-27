Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports FO4_Base_Library

''' <summary>Parser of the LooksMenu CharGen preset JSON format. Schema verified against
''' F4SEPlugins-master/f4ee/CharGenInterface.cpp:60-256 (SavePreset) and 259-620 (LoadPreset).
'''
''' Maps every JSON field that has a vanilla NPC_ subrecord equivalent. The F4SE-only Overlays
''' field (body tattoos) is now fully parsed + round-tripped (see <see cref="LooksmenuPreset.Overlays"/>);
''' it is also still surfaced as a raw count via <see cref="LooksmenuPreset.UnsupportedCounts"/> for the
''' existing Load warning UI. BodyMorphs (BodySlide sliders) and Skin (LM skin template) are likewise
''' parsed. See memory/project_npc_looksmenu_pending.md for the deferral rationale on render wiring.</summary>
Public Module LooksmenuLoader

    ''' <summary>Output of <see cref="ParseFile"/>. All vanilla-mappable fields are pre-resolved to
    ''' global FormIDs (via <see cref="PluginManager.ResolveReferencedFormID"/>) so the caller can
    ''' just assign them onto an NPC_Data without any string-parsing logic.</summary>
    Public Class LooksmenuPreset
        Public SourcePath As String = ""
        Public Gender As Byte = 0   ' 0=Male, 1=Female
        Public HeadPartFormIDs As New List(Of UInteger)
        ''' <summary>HeadPart entries from the JSON that ResolveFormIdentifier couldn't resolve to a
        ''' loaded plugin (returned 0). Kept as raw "Plugin.esp|HEX" strings so the caller can log
        ''' which plugins the preset depends on but the user doesn't have active. Almost always
        ''' the cause when a preset's pelo/ojos visually don't apply: the HDPT lives in a
        ''' presets-mod ESP that isn't in Plugins.txt.</summary>
        Public UnresolvedHeadParts As New List(Of String)
        Public HairColorFormID As UInteger
        Public WeightThin As Single?
        Public WeightMuscular As Single?
        Public WeightFat As Single?
        ''' <summary>Chargen face vertex morphs — Morphs.Presets in JSON, MSDK/MSDV in NPC_.
        ''' Key = MSDK hash (the JSON serializes it as hex string, parsed to UInt32 here).</summary>
        Public ChargenFaceMorphs As New Dictionary(Of UInteger, Single)
        ''' <summary>Body region morph values — Morphs.Values[] in JSON (positional array),
        ''' MRSV in NPC_. Index = position in RACE.MorphValues definitions.</summary>
        Public BodyMorphValues As New List(Of Single)
        ''' <summary>Face bone morph regions — Morphs.Regions in JSON, FMRI/FMRS in NPC_.
        ''' Key = FMRI region index, Value = 8 floats (the FMRS values for that region).</summary>
        Public FaceBoneRegions As New Dictionary(Of UInteger, Single())
        ''' <summary>Always present after parse. CharGenInterface.cpp asymmetrically skips the field
        ''' on Save when intensity == 1.0 (line 161 `if(intensity != 1.0f)`), but on Load (lines 456-458)
        ''' it interprets "absent" as "use 1.0" and ALWAYS calls SetFacialBoneMorphIntensity. So
        ''' missing-from-JSON is semantically equivalent to "1.0 explicit", not "preserve previous".
        ''' We replicate that: default 1.0F at parse time, override only when the JSON has the field.</summary>
        Public FacialMorphIntensity As Single = 1.0F
        ''' <summary>Tint layers reordered by TintOrder[] if the JSON provided one. Each entry
        ''' is a parsed NPC_FaceTintLayerData with Discriminator/Index/Value/Color/TemplateColorIndex
        ''' filled. RawTetiBytes/RawTendBytes are NOT populated (the JSON doesn't carry them) —
        ''' callers that need byte-perfect round-trip must re-emit them from the parsed fields.</summary>
        Public FaceTintLayers As New List(Of NPC_FaceTintLayerData)

        ''' <summary>Presence flags for the four overlay-replaceable list fields. True = "this
        ''' preset declares that field is being overridden, the list (even empty) is authoritative".
        ''' False = "field absent from this preset, ApplyPresetOverlayToNpcData preserves raw NPC".
        '''
        ''' Without this distinction Count=0 would be ambiguous: it could mean either "user wiped
        ''' all entries and wants override-as-empty" (apply wipe) or "preset never carried this
        ''' field" (preserve raw). Both are valid; the editor + Save flow needs the wipe semantics
        ''' while Load LooksMenu absent-key needs preserve semantics. Has flags resolve it.
        '''
        ''' Note on engine semantics: vanilla LooksMenu (CharGenInterface.cpp:387-413, 421-450,
        ''' 477-524) does NOT distinguish "key absent" from "key present empty {}/[]" — both yield
        ''' members.size()==0 and both trigger Clear() of the corresponding NPC field. So the LM
        ''' engine's actual behaviour is "any LoadPreset call wipes existing tints/morphs/regions
        ''' even if that section is missing from the JSON". We deliberately diverge from that:
        ''' loading a partial preset (e.g. only HeadParts) shouldn't nuke tints. Has flags make
        ''' our overlay treat absent JSON keys as "preserve raw" — better UX than LM's wipe-all,
        ''' and harmless because the wipe semantics are still available via explicit edits.
        '''
        ''' Setters: ParseFile sets True when JSON has the key (even if value is empty {}/[]).
        ''' BuildPresetFromState sets all four True (snapshot is complete by definition).
        ''' Edit forms set True at seed time (the editor opening "claims" these fields).
        ''' Paste handler sets True for fields the options dialog ticked.
        '''
        ''' Reader: ApplyPresetOverlayToNpcData reads Has* (not Count). Count=0+Has=True ⇒ wipe.
        ''' Count=0+Has=False ⇒ preserve raw (current behaviour for absent fields).</summary>
        Public HasFaceTintLayers As Boolean = False
        Public HasChargenFaceMorphs As Boolean = False
        Public HasBodyMorphValues As Boolean = False
        Public HasFaceBoneRegions As Boolean = False
        ''' <summary>HeadParts presence — same semantics as the four list flags above.
        ''' Without this, an empty HeadPartFormIDs.Count couldn't distinguish "not in this preset"
        ''' from "user wiped all head parts". Save ESP needs the latter to emit zero PNAM
        ''' subrecords (engine then falls back to RACE.HEAD only).</summary>
        Public HasHeadPartFormIDs As Boolean = False

        ''' <summary>BodySlide vertex morph sliders ("BodyMorphs" in JSON). Dict keyed by slider
        ''' name (e.g. "BigBelly", "ChubbyButt"); the resolver looks each name up in the PIRT .tri
        ''' of every shape and applies wherever defined. Empty = no overlay; the NPC's body renders
        ''' with no BodySlide morphs. Schema: F4SEPlugins-master/f4ee/CharGenInterface.cpp:204-215
        ''' (Save) and 560-570 (Load). NOT a vanilla record — lives only in the JSON.</summary>
        Public BodyMorphSliders As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>Body overlays (LooksMenu "tattoos") — the per-NPC list of applied overlay entries.
        ''' Render-only F4SE field, same shape as <see cref="BodyMorphSliders"/> (lives only in the JSON,
        ''' no vanilla NPC_ subrecord equivalent). Each entry references an <see cref="OverlayTemplate"/>
        ''' by id plus per-instance priority and optional tint/UV transform. Schema:
        ''' F4SEPlugins-master/f4ee/CharGenInterface.cpp:217-244 (Save) and 578-619 (Load).</summary>
        Public Overlays As New List(Of OverlayEntry)

        ''' <summary>Overlays presence — SAME semantics as the other Has* flags above. True = "this
        ''' preset declares the Overlays field, the list (even empty) is authoritative ⇒ overlay
        ''' treats it as a wipe". False = "field absent from this preset, preserve raw NPC".
        ''' Set True by ParseFile when the "Overlays" key is present (regardless of array length).</summary>
        Public HasOverlays As Boolean = False

        ''' <summary>Counts of F4SE-only fields the preset contains. Non-zero = the preset has
        ''' content the editor will not apply (Overlays/BodyMorphs sliders/Skin override).</summary>
        Public UnsupportedCounts As New UnsupportedFieldCounts

        ''' <summary>Editor-only override of NPC.ACBS bit 2 ("Is CharGen Face Preset"). Lives in
        ''' the in-memory overlay so the user can flip the flag in Edit Face and have it persisted
        ''' to ESP later (Save ESP/ESM is the consumer; out of scope for the LM JSON, which doesn't
        ''' carry this field). Nothing = preserve raw NPC.AcbsFlags; True/False = override the bit.
        ''' NOT serialized to LooksMenu JSON — see project_facegen_ischargenpreset_flag.md memory.</summary>
        Public IsCharGenFacePreset As Boolean?

        ''' <summary>Editor-only override of NPC.WNAM (vanilla Skin → ARMO FormID). Distinct from
        ''' <see cref="SkinTemplateId"/> which is the F4SE LM template (different feature). NPC.WNAM
        ''' lives on the record and persists to ESP; SkinTemplateId lives only in the LM JSON.
        ''' Nothing = preserve raw NPC.SkinFormID; 0 = clear (engine falls back to RACE.SkinFormID);
        ''' other = ARMO FormID. NOT serialized to LM JSON.</summary>
        Public SkinFormIDOverride As UInteger?

        ''' <summary>Editor-only override of NPC.DOFT (Default Outfit → OTFT FormID). Same shape as
        ''' <see cref="SkinFormIDOverride"/>: a record-level field that lives in the in-memory overlay,
        ''' round-trips through the <c>_npcm_DefaultOutfit</c> JSON extension and Copy/Paste, and will
        ''' persist to ESP via Save ESP (NPC_.DOFT). Set by the Edit Outfit picker.
        ''' Nothing = preserve raw NPC.DOFT; 0 = no outfit (naked); other = OTFT FormID.
        ''' NOT a vanilla LooksMenu field — LM in-game ignores the <c>_npcm_</c> key.</summary>
        Public DefaultOutfitFormIDOverride As UInteger?

        ''' <summary>F4SE LM Skin override — the string id of a SkinTemplate registered via
        ''' <c>F4SEPlugins-master/f4ee/SkinInterface.cpp</c>. The template bundles ARMO + face TXST +
        ''' head/headRear HDPT (see <see cref="LmSkinTemplate"/> for the full layout) and is applied
        ''' at runtime by LooksMenu's <c>ApplyOverride</c> on top of whatever NPC.WNAM/RACE.WNAM
        ''' resolved to. Nothing / empty = no LM override; non-empty = the id to apply. Serialized
        ''' to LM JSON as the canonical "Skin" key (CharGenInterface.cpp emits/reads this string).
        ''' Distinct from the vanilla <see cref="SkinFormIDOverride"/> — both can coexist; the LM
        ''' template wins at preview time when both are set (matches in-game order).</summary>
        Public SkinTemplateId As String = ""

        ''' <summary>Set of HDPT FormIDs that were materialized into <see cref="HeadPartFormIDs"/>
        ''' specifically by an LM SkinTemplate bundle (via
        ''' <c>NpcRecordOverlay.MaterializeLmTemplateBundleToPreset</c>). Lets us distinguish
        ''' "template-injected" entries from entries the user added manually via Edit Face, so
        ''' switching/clearing the template can retract ONLY its own contribution without
        ''' clobbering the user's edits.
        ''' NOT serialized to LM JSON — it's overlay-only metadata. Cleared on Retract.</summary>
        Public LmTemplateInjectedHdptFormIDs As New HashSet(Of UInteger)

        ''' <summary>True when <see cref="HasHeadPartFormIDs"/> was flipped to True specifically by
        ''' an LM template materialization (not by Edit Face / Paste / Load LM HeadParts array).
        ''' Lets the Retract path safely flip Has* back to False when the template was the sole
        ''' reason it was True. If Edit Face / Paste / etc. set Has* before or after the template
        ''' was applied, this stays False and Retract preserves Has*=True.</summary>
        Public HasHeadPartFormIDsSetByTemplate As Boolean = False
    End Class

    Public Class UnsupportedFieldCounts
        Public Overlays As Integer
        Public BodyMorphSliders As Integer
        Public HasSkinOverride As Boolean
    End Class

    ''' <summary>One applied body overlay (a LooksMenu "tattoo") on an NPC. References an
    ''' <see cref="OverlayTemplate"/> by id; carries per-instance priority and optional tint/UV
    ''' transform. Schema verified against F4SEPlugins-master/f4ee/CharGenInterface.cpp:217-244
    ''' (Save) and :578-619 (Load). The float arrays are kept at the JSON's native width (tint=4,
    ''' UV=2) so a round-trip is byte-faithful; Nothing means the JSON key was absent (the engine
    ''' load supplies a default — tint 0,0,0,0 / offsetUV 0,0 / scaleUV 1,1).</summary>
    Public Class OverlayEntry
        Public TemplateId As String = ""      ' JSON "template" — the OverlayTemplate id (CharGenInterface.cpp:586)
        Public Priority As Integer = 0        ' JSON "priority" (SInt32, multimap render order; :585)
        Public Tint As Single()               ' JSON "tint" [r,g,b,a] 0..1; Nothing = no tint (kHasTintColor absent, :592-597)
        Public OffsetUV As Single()           ' JSON "offsetUV" [x,y]; Nothing = default (0,0) (:601-604)
        Public ScaleUV As Single()            ' JSON "scaleUV" [x,y]; Nothing = default (1,1) (:608-611)
    End Class

    ''' <summary>Parse a LooksMenu preset JSON file. Returns Nothing if the file is unreadable
    ''' or not valid JSON. Form-identifier strings ("Plugin.esp|XXXXXX") are resolved against
    ''' <paramref name="pluginManager"/> at parse time — entries from plugins not in the active
    ''' load order resolve to 0 and the caller will see HeadParts entries missing.</summary>
    Public Function ParseFile(filePath As String, pluginManager As PluginManager) As LooksmenuPreset
        If String.IsNullOrEmpty(filePath) OrElse Not File.Exists(filePath) Then Return Nothing

        Dim raw As String
        Try
            raw = File.ReadAllText(filePath)
        Catch
            Return Nothing
        End Try

        Dim doc As JsonDocument
        Try
            doc = JsonDocument.Parse(raw, New JsonDocumentOptions With {.CommentHandling = JsonCommentHandling.Skip, .AllowTrailingCommas = True})
        Catch
            Return Nothing
        End Try

        Using doc
            Dim root = doc.RootElement
            If root.ValueKind <> JsonValueKind.Object Then Return Nothing

            Dim preset As New LooksmenuPreset With {.SourcePath = filePath}

            ' Gender
            Dim genderEl As JsonElement
            If root.TryGetProperty("Gender", genderEl) AndAlso genderEl.ValueKind = JsonValueKind.Number Then
                preset.Gender = CByte(Math.Min(255, Math.Max(0, genderEl.GetInt32())))
            End If

            ' HeadParts: array of "Plugin.esp|FormIDhex" strings
            Dim hpEl As JsonElement
            If root.TryGetProperty("HeadParts", hpEl) AndAlso hpEl.ValueKind = JsonValueKind.Array Then
                preset.HasHeadPartFormIDs = True
                For Each entry In hpEl.EnumerateArray()
                    If entry.ValueKind = JsonValueKind.String Then
                        Dim hpStr = entry.GetString()
                        Dim resolved = ResolveFormIdentifier(hpStr, pluginManager)
                        If resolved <> 0UI Then
                            preset.HeadPartFormIDs.Add(resolved)
                        Else
                            preset.UnresolvedHeadParts.Add(hpStr)
                        End If
                    End If
                Next
            End If

            ' HairColor
            Dim hcEl As JsonElement
            If root.TryGetProperty("HairColor", hcEl) AndAlso hcEl.ValueKind = JsonValueKind.String Then
                preset.HairColorFormID = ResolveFormIdentifier(hcEl.GetString(), pluginManager)
            End If

            ' Weight: 3 floats [thin, muscular, large]
            Dim wEl As JsonElement
            If root.TryGetProperty("Weight", wEl) AndAlso wEl.ValueKind = JsonValueKind.Array Then
                Dim arr = wEl.EnumerateArray().ToArray()
                If arr.Length >= 1 AndAlso arr(0).ValueKind = JsonValueKind.Number Then preset.WeightThin = arr(0).GetSingle()
                If arr.Length >= 2 AndAlso arr(1).ValueKind = JsonValueKind.Number Then preset.WeightMuscular = arr(1).GetSingle()
                If arr.Length >= 3 AndAlso arr(2).ValueKind = JsonValueKind.Number Then preset.WeightFat = arr(2).GetSingle()
            End If

            ' Morphs.{Values, Presets, Regions, Intensity}
            ' Has* flags: set True when the JSON contains the key (regardless of whether it's
            ' empty or has entries). The presence of the key means "this preset declares this
            ' field, even if empty" — the overlay will treat empty-with-Has=True as "wipe".
            Dim morphsEl As JsonElement
            If root.TryGetProperty("Morphs", morphsEl) AndAlso morphsEl.ValueKind = JsonValueKind.Object Then
                Dim valuesEl As JsonElement
                If morphsEl.TryGetProperty("Values", valuesEl) AndAlso valuesEl.ValueKind = JsonValueKind.Array Then
                    preset.HasBodyMorphValues = True
                    For Each v In valuesEl.EnumerateArray()
                        If v.ValueKind = JsonValueKind.Number Then preset.BodyMorphValues.Add(v.GetSingle())
                    Next
                End If

                Dim presetsEl As JsonElement
                If morphsEl.TryGetProperty("Presets", presetsEl) AndAlso presetsEl.ValueKind = JsonValueKind.Object Then
                    preset.HasChargenFaceMorphs = True
                    For Each prop In presetsEl.EnumerateObject()
                        Dim hash As UInteger
                        If UInteger.TryParse(prop.Name, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, hash) AndAlso prop.Value.ValueKind = JsonValueKind.Number Then
                            preset.ChargenFaceMorphs(hash) = prop.Value.GetSingle()
                        End If
                    Next
                End If

                Dim regionsEl As JsonElement
                If morphsEl.TryGetProperty("Regions", regionsEl) AndAlso regionsEl.ValueKind = JsonValueKind.Object Then
                    preset.HasFaceBoneRegions = True
                    For Each prop In regionsEl.EnumerateObject()
                        Dim idx As UInteger
                        If UInteger.TryParse(prop.Name, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, idx) AndAlso prop.Value.ValueKind = JsonValueKind.Array Then
                            Dim vals As New List(Of Single)
                            For Each v In prop.Value.EnumerateArray()
                                If v.ValueKind = JsonValueKind.Number Then vals.Add(v.GetSingle())
                            Next
                            preset.FaceBoneRegions(idx) = vals.ToArray()
                        End If
                    Next
                End If

                Dim intEl As JsonElement
                If morphsEl.TryGetProperty("Intensity", intEl) AndAlso intEl.ValueKind = JsonValueKind.Number Then
                    preset.FacialMorphIntensity = intEl.GetSingle()
                End If
            End If

            ' Tints + TintOrder. CharGenInterface.cpp:165-201 saves Tints as a dict keyed by tint
            ' index (hex), each entry having Type/Percent/Color/ColorID. TintOrder is a parallel
            ' array dictating render order. We build the layers in TintOrder order if provided;
            ' fallback = enumeration order of the Tints object.
            Dim tintsEl As JsonElement
            Dim tintOrderEl As JsonElement
            Dim hasTints = root.TryGetProperty("Tints", tintsEl) AndAlso tintsEl.ValueKind = JsonValueKind.Object
            Dim hasOrder = root.TryGetProperty("TintOrder", tintOrderEl) AndAlso tintOrderEl.ValueKind = JsonValueKind.Array

            If hasTints Then
                preset.HasFaceTintLayers = True
                Dim orderedKeys As New List(Of String)
                If hasOrder Then
                    For Each k In tintOrderEl.EnumerateArray()
                        If k.ValueKind = JsonValueKind.String Then orderedKeys.Add(k.GetString())
                    Next
                Else
                    For Each prop In tintsEl.EnumerateObject()
                        orderedKeys.Add(prop.Name)
                    Next
                End If

                For Each keyName In orderedKeys
                    Dim entryEl As JsonElement
                    If Not tintsEl.TryGetProperty(keyName, entryEl) Then Continue For
                    If entryEl.ValueKind <> JsonValueKind.Object Then Continue For

                    Dim layer As New NPC_FaceTintLayerData()
                    Dim idxParsed As UInteger
                    If UInteger.TryParse(keyName, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, idxParsed) Then
                        layer.Index = CUShort(idxParsed And &HFFFFUI)
                    End If

                    Dim typeEl As JsonElement
                    If entryEl.TryGetProperty("Type", typeEl) AndAlso typeEl.ValueKind = JsonValueKind.Number Then
                        layer.Discriminator = CUShort(typeEl.GetInt32())
                    End If

                    Dim pctEl As JsonElement
                    If entryEl.TryGetProperty("Percent", pctEl) AndAlso pctEl.ValueKind = JsonValueKind.Number Then
                        ' "Percent" is 0..100 per LooksMenu schema (the field name says it explicitly,
                        ' and CharGenInterface.cpp:180-181 only emits entries with Value>0 — it never
                        ' generates >100). NPC_FaceTintLayerData.Value is Integer (RecordParsers.vb:27).
                        ' The previous clamp to 255 + cast to Byte was wrong on both axes: it allowed
                        ' an out-of-spec range (101..255) to slip through, and the Byte round-trip
                        ' silently corrupted any future expansion of the field. Clamp to the documented
                        ' range and store as Integer.
                        layer.Value = Math.Min(100, Math.Max(0, pctEl.GetInt32()))
                    End If

                    ' Palette-only: Type=1 (BGSCharacterTint::Entry::kTypePalette in f4se).
                    ' Color is stored as bgra UInt32. CharGenInterface.cpp:193 writes
                    '   tintData[k]["Color"] = (Json::Int)palette->color.bgra
                    ' which is signed-int-with-bit-pattern. We read via GetInt32 + bit cast.
                    If layer.Discriminator = 1US Then
                        Dim colorEl As JsonElement
                        If entryEl.TryGetProperty("Color", colorEl) AndAlso colorEl.ValueKind = JsonValueKind.Number Then
                            Dim bgra = CUInt(colorEl.GetInt64() And &HFFFFFFFFL)
                            ' Despite the field name "bgra", LooksMenu stores the UInt32 with bytes
                            ' in memory order [R, G, B, A] (verified empirically: a TEND with
                            ' R=0xE9 G=0xDA B=0xD8 round-trips through LooksMenu in-game as
                            ' Color=0x00D8DAE9, which packs as B<<16 | G<<8 | R, NOT the field-
                            ' name-suggested R<<16 | G<<8 | B). So byte 0 (LSB) is R, byte 2 is B.
                            Dim r = CInt((bgra >> 0) And &HFFUI)
                            Dim g = CInt((bgra >> 8) And &HFFUI)
                            Dim b = CInt((bgra >> 16) And &HFFUI)
                            Dim a = CInt((bgra >> 24) And &HFFUI)
                            layer.Color = Drawing.Color.FromArgb(a, r, g, b)
                        End If
                        Dim cidEl As JsonElement
                        If entryEl.TryGetProperty("ColorID", cidEl) AndAlso cidEl.ValueKind = JsonValueKind.Number Then
                            layer.TemplateColorIndex = cidEl.GetInt32()
                        End If
                    End If

                    preset.FaceTintLayers.Add(layer)
                Next
            End If

            ' Overlays (body tattoos). Fully parsed into preset.Overlays. Engine semantics mirror
            ' CharGenInterface.cpp:578-619 LoadPreset: presence of the key wipes existing overlays
            ' (RemoveAll at :577) then applies each member. We set HasOverlays True on presence so the
            ' overlay-apply path treats it as authoritative (Has* semantics, like the other fields).
            ' UnsupportedCounts.Overlays is still populated (the Load warning UI reads it).
            ' Per-entry: template(string, required — skip if missing/empty, :586), priority(int default 0,
            ' :585), optional tint[4]/offsetUV[2]/scaleUV[2] (:592-611). Absent UV/tint left Nothing;
            ' the engine load substitutes its defaults (tint 0,0,0,0 / offset 0,0 / scale 1,1).
            Dim ovEl As JsonElement
            If root.TryGetProperty("Overlays", ovEl) AndAlso ovEl.ValueKind = JsonValueKind.Array Then
                preset.HasOverlays = True
                preset.UnsupportedCounts.Overlays = ovEl.GetArrayLength()
                For Each ov In ovEl.EnumerateArray()
                    If ov.ValueKind <> JsonValueKind.Object Then Continue For

                    ' template — required. CharGenInterface.cpp:586 reads it unconditionally; an entry
                    ' without a template id can't reference a template, so we skip it.
                    Dim tplEl As JsonElement
                    If Not ov.TryGetProperty("template", tplEl) OrElse tplEl.ValueKind <> JsonValueKind.String Then Continue For
                    Dim tplId = tplEl.GetString()
                    If String.IsNullOrEmpty(tplId) Then Continue For

                    Dim entry As New OverlayEntry With {.TemplateId = tplId}

                    ' priority — default 0 (CharGenInterface.cpp:585 asInt with no default; absent key
                    ' in jsoncpp yields 0, so default 0 matches).
                    Dim prEl As JsonElement
                    If ov.TryGetProperty("priority", prEl) AndAlso prEl.ValueKind = JsonValueKind.Number Then
                        entry.Priority = prEl.GetInt32()
                    End If

                    ' tint [r,g,b,a] — optional (CharGenInterface.cpp:592-597). Only set when present.
                    Dim tintEl As JsonElement
                    If ov.TryGetProperty("tint", tintEl) AndAlso tintEl.ValueKind = JsonValueKind.Array Then
                        entry.Tint = ReadFloatArray(tintEl, 4)
                    End If

                    ' offsetUV [x,y] — optional (CharGenInterface.cpp:601-604).
                    Dim offEl As JsonElement
                    If ov.TryGetProperty("offsetUV", offEl) AndAlso offEl.ValueKind = JsonValueKind.Array Then
                        entry.OffsetUV = ReadFloatArray(offEl, 2)
                    End If

                    ' scaleUV [x,y] — optional (CharGenInterface.cpp:608-611). The engine SAVE has a bug
                    ' (:238-239 appends offsetUV.x/y into the scaleUV array), but the engine LOAD reads
                    ' scaleUV faithfully, so reading it straight is correct.
                    Dim sclEl As JsonElement
                    If ov.TryGetProperty("scaleUV", sclEl) AndAlso sclEl.ValueKind = JsonValueKind.Array Then
                        entry.ScaleUV = ReadFloatArray(sclEl, 2)
                    End If

                    preset.Overlays.Add(entry)
                Next
            End If
            Dim skEl As JsonElement
            If root.TryGetProperty("Skin", skEl) AndAlso skEl.ValueKind = JsonValueKind.String Then
                Dim skId = skEl.GetString()
                preset.SkinTemplateId = If(skId, "")
                preset.UnsupportedCounts.HasSkinOverride = Not String.IsNullOrEmpty(skId)
            End If

            ' BodyMorphs: BodySlide vertex sliders. Canonical LooksMenu field — see
            ' CharGenInterface.cpp:560-570 for the engine semantics:
            '   • Key present (even empty {}) → wipe existing morphs, then apply each member.
            '   • Key absent                  → preserve current actor state.
            ' We replicate that:
            '   • Key present → fill BodyMorphSliders (caller's overlay-apply will replace any
            '                  prior sliders on the NPC).
            '   • Key absent  → leave BodyMorphSliders empty; ApplyPresetOverlayToNpcData treats
            '                  that as "no BodyMorphs override" and the NPC keeps whatever sliders
            '                  it had before this preset was applied.
            Dim bmEl As JsonElement
            If root.TryGetProperty("BodyMorphs", bmEl) AndAlso bmEl.ValueKind = JsonValueKind.Object Then
                For Each prop In bmEl.EnumerateObject()
                    If prop.Value.ValueKind = JsonValueKind.Number Then
                        preset.BodyMorphSliders(prop.Name) = prop.Value.GetSingle()
                    End If
                Next
                preset.UnsupportedCounts.BodyMorphSliders = preset.BodyMorphSliders.Count
            End If

            ' Note on MRSV: the canonical LooksMenu field is Morphs.Values (a 5-element float array
            ' per CharGenInterface.cpp LoadPreset.Allocate(5)). That field is already parsed into
            ' BodyMorphValues above in the Morphs section. We do NOT introduce a separate "MRSV"
            ' top-level key — it would duplicate the canonical channel and break round-trip
            ' compatibility with LooksMenu in-game.

            ' === NPC_Manager extensions (paired con SerializePreset) ===
            ' Keys "_npcm_*" emitidas por SerializePreset; LM in-game las ignora. Si la JSON no
            ' las trae (preset autoreado por LM o por NPC_Manager pre-extensión), los campos
            ' quedan Nothing y el overlay merge cae al preserve-raw semantic.
            Dim skinFidEl As JsonElement
            If root.TryGetProperty("_npcm_SkinFormID", skinFidEl) AndAlso skinFidEl.ValueKind = JsonValueKind.String Then
                Dim sfStr = skinFidEl.GetString()
                If String.IsNullOrEmpty(sfStr) Then
                    ' Empty string = clear (engine fallback to RACE.WNAM). Equivale a Some(0).
                    preset.SkinFormIDOverride = 0UI
                Else
                    Dim resolved = ResolveFormIdentifier(sfStr, pluginManager)
                    ' Si plugin no está cargado, ResolveFormIdentifier devuelve 0 → cae como Some(0).
                    ' Aceptable: si el JSON referenciaba un ARMO custom que el user no tiene activo,
                    ' el render cae a RACE.WNAM en lugar de crashear.
                    preset.SkinFormIDOverride = resolved
                End If
            End If
            Dim outfitFidEl As JsonElement
            If root.TryGetProperty("_npcm_DefaultOutfit", outfitFidEl) AndAlso outfitFidEl.ValueKind = JsonValueKind.String Then
                Dim ofStr = outfitFidEl.GetString()
                If String.IsNullOrEmpty(ofStr) Then
                    ' Empty string = "no outfit". Equivale a Some(0).
                    preset.DefaultOutfitFormIDOverride = 0UI
                Else
                    ' Si el plugin del OTFT no está cargado, ResolveFormIdentifier devuelve 0 → cae como
                    ' Some(0) = "no outfit" en lugar de crashear (mismo criterio que _npcm_SkinFormID).
                    preset.DefaultOutfitFormIDOverride = ResolveFormIdentifier(ofStr, pluginManager)
                End If
            End If
            Dim cgpEl As JsonElement
            If root.TryGetProperty("_npcm_IsCharGenPreset", cgpEl) AndAlso
               (cgpEl.ValueKind = JsonValueKind.True OrElse cgpEl.ValueKind = JsonValueKind.False) Then
                preset.IsCharGenFacePreset = cgpEl.GetBoolean()
            End If

            Return preset
        End Using
    End Function

    ''' <summary>Read a fixed-width float array from a JSON array element. Reads exactly
    ''' <paramref name="count"/> slots; a short JSON array pads the tail with 0.0F (jsoncpp's
    ''' <c>arr[i].asFloat()</c> on an out-of-range index returns 0, so the engine load — which
    ''' indexes [0..3]/[0..1] unconditionally — sees the same value). Non-number slots also yield 0.</summary>
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

    ''' <summary>Resolve a "Plugin.esp|FormIDhex" identifier (LooksMenu's serialization format —
    ''' Utilities.cpp:108-130 GetFormIdentifier emits "%s|%06X" with the LOCAL FormID, no master
    ''' index in the high bits) to a global FormID. Returns 0 when the named plugin isn't in the
    ''' active load order (caller falls back to "skip this entry") or when the string is malformed.
    '''
    ''' We can't delegate to <see cref="PluginManager.ResolveReferencedFormID"/> directly: that
    ''' helper returns the input localFormID unchanged when the plugin isn't loaded, which would
    ''' look like a successful resolution (and then GetRecord fails downstream with "not found").
    ''' Doing the lookup ourselves lets us cleanly distinguish "plugin not loaded" from "resolved
    ''' to a global ID that happens to have low bytes".</summary>
    Friend Function ResolveFormIdentifier(identifier As String, pluginManager As PluginManager) As UInteger
        If String.IsNullOrEmpty(identifier) Then Return 0UI
        Dim pipeIdx = identifier.IndexOf("|"c)
        If pipeIdx <= 0 OrElse pipeIdx >= identifier.Length - 1 Then Return 0UI

        Dim pluginName = identifier.Substring(0, pipeIdx).Trim()
        Dim hex = identifier.Substring(pipeIdx + 1).Trim()
        Dim localFormID As UInteger
        If Not UInteger.TryParse(hex, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, localFormID) Then
            Return 0UI
        End If

        ' Find the named plugin in the active load order. If not loaded, signal "unresolved" with
        ' 0 — caller will route the raw identifier into UnresolvedHeadParts for diagnostics.
        Dim loadOrderIdx As Integer = -1
        For i = 0 To pluginManager.Plugins.Count - 1
            If String.Equals(pluginManager.Plugins(i).FileName, pluginName, StringComparison.OrdinalIgnoreCase) Then
                loadOrderIdx = i
                Exit For
            End If
        Next
        If loadOrderIdx < 0 Then Return 0UI

        ' LooksMenu serializes the runtime FormID masked to 24 bits (Utilities.cpp:112
        ' `modForm = formID & 0xFFFFFF`). Combine with the plugin's engine FileID slot (full or 0xFE
        ' light) — PluginManager owns that scheme so ESL plugins resolve correctly.
        Return pluginManager.GlobalFormIDFromIdentifierLocal(pluginName, localFormID)
    End Function

    ''' <summary>Deep-clone a LooksmenuPreset. Single source of truth for preset cloning across
    ''' the codebase — EditFace_Form, EditBody_Form and MainForm.BuildPresetFromState used to
    ''' have their own near-identical copies that drifted (e.g. one missed copying Has* flags).
    ''' Centralizing here guarantees any new field added to LooksmenuPreset propagates through
    ''' every snapshot/copy path automatically.</summary>
    Public Function ClonePreset(p As LooksmenuPreset) As LooksmenuPreset
        If p Is Nothing Then Return Nothing
        Dim c As New LooksmenuPreset With {
            .SourcePath = p.SourcePath,
            .Gender = p.Gender
        }
        c.HeadPartFormIDs.AddRange(p.HeadPartFormIDs)
        c.UnresolvedHeadParts.AddRange(p.UnresolvedHeadParts)
        c.HairColorFormID = p.HairColorFormID
        c.WeightThin = p.WeightThin
        c.WeightMuscular = p.WeightMuscular
        c.WeightFat = p.WeightFat

        For Each kv In p.ChargenFaceMorphs : c.ChargenFaceMorphs(kv.Key) = kv.Value : Next
        c.BodyMorphValues.AddRange(p.BodyMorphValues)
        For Each kv In p.FaceBoneRegions
            c.FaceBoneRegions(kv.Key) = If(kv.Value Is Nothing, Nothing, CType(kv.Value.Clone(), Single()))
        Next
        c.FacialMorphIntensity = p.FacialMorphIntensity
        For Each tl In p.FaceTintLayers
            c.FaceTintLayers.Add(CloneFaceTintLayer(tl))
        Next

        ' Has flags must be carried with the lists they describe — without these the wipe vs
        ' preserve semantics differ between original and clone.
        c.HasFaceTintLayers = p.HasFaceTintLayers
        c.HasChargenFaceMorphs = p.HasChargenFaceMorphs
        c.HasBodyMorphValues = p.HasBodyMorphValues
        c.HasFaceBoneRegions = p.HasFaceBoneRegions
        c.HasHeadPartFormIDs = p.HasHeadPartFormIDs

        For Each kv In p.BodyMorphSliders : c.BodyMorphSliders(kv.Key) = kv.Value : Next

        ' Overlays — deep-copy each entry (cloning the float arrays so the clone is independent).
        ' HasOverlays travels with the list, same as the other Has* flags above.
        For Each ov In p.Overlays
            c.Overlays.Add(New OverlayEntry With {
                .TemplateId = ov.TemplateId,
                .Priority = ov.Priority,
                .Tint = If(ov.Tint Is Nothing, Nothing, CType(ov.Tint.Clone(), Single())),
                .OffsetUV = If(ov.OffsetUV Is Nothing, Nothing, CType(ov.OffsetUV.Clone(), Single())),
                .ScaleUV = If(ov.ScaleUV Is Nothing, Nothing, CType(ov.ScaleUV.Clone(), Single()))
            })
        Next
        c.HasOverlays = p.HasOverlays

        c.UnsupportedCounts.Overlays = p.UnsupportedCounts.Overlays
        c.UnsupportedCounts.BodyMorphSliders = p.UnsupportedCounts.BodyMorphSliders
        c.UnsupportedCounts.HasSkinOverride = p.UnsupportedCounts.HasSkinOverride

        ' Editor-only overrides (not part of the LM JSON schema, but live in the in-memory overlay).
        c.IsCharGenFacePreset = p.IsCharGenFacePreset
        c.SkinFormIDOverride = p.SkinFormIDOverride
        c.DefaultOutfitFormIDOverride = p.DefaultOutfitFormIDOverride
        c.SkinTemplateId = p.SkinTemplateId
        For Each fid In p.LmTemplateInjectedHdptFormIDs : c.LmTemplateInjectedHdptFormIDs.Add(fid) : Next
        c.HasHeadPartFormIDsSetByTemplate = p.HasHeadPartFormIDsSetByTemplate
        Return c
    End Function

    ''' <summary>Deep-clone a single tint layer. Used by ClonePreset and by call sites that
    ''' need to copy individual layers without cloning the full preset.</summary>
    Public Function CloneFaceTintLayer(tl As NPC_FaceTintLayerData) As NPC_FaceTintLayerData
        If tl Is Nothing Then Return Nothing
        Return New NPC_FaceTintLayerData With {
            .Discriminator = tl.Discriminator,
            .Index = tl.Index,
            .Value = tl.Value,
            .Color = tl.Color,
            .TemplateColorIndex = tl.TemplateColorIndex,
            .RawTetiBytes = If(tl.RawTetiBytes Is Nothing, Nothing, CType(tl.RawTetiBytes.Clone(), Byte())),
            .RawTendBytes = If(tl.RawTendBytes Is Nothing, Nothing, CType(tl.RawTendBytes.Clone(), Byte()))
        }
    End Function

    ''' <summary>Serialize a preset to a LooksMenu-canonical JSON string. Schema replicates
    ''' CharGenInterface.cpp SavePreset (lines 49-256) field-by-field. BodyMorphs, Overlays and Skin
    ''' (the three F4SE-only fields) ARE emitted so the preset round-trips with LooksMenu in-game.
    ''' See memory/project_npc_looksmenu_pending.md for the render-wiring deferral rationale.
    '''
    ''' Per-field semantics (matches CharGenInterface.cpp behaviour):
    '''   • Gender (line 90): always emitted as UInt.
    '''   • HeadParts (line 92-103): array of "Plugin|HEX" strings. IsExtraPart filter is the
    '''     caller's responsibility (BuildPresetFromState filters before reaching here).
    '''   • HairColor (line 105-111): only when non-zero.
    '''   • Weight (line 113-115): array of 3 floats, always emitted.
    '''   • Morphs.Values (line 117-126): emitted ONLY when present. LoadPreset.Allocate(5) means
    '''     the engine works with exactly 5 slots — we pad/truncate to 5 to match.
    '''   • Morphs.Presets (line 128-139): dict hex→float, only when non-empty.
    '''   • Morphs.Regions (line 142-158): dict hex→[8 floats], only when non-empty.
    '''   • Morphs.Intensity (line 160-163): only emitted when != 1.0F.
    '''   • Tints + TintOrder (line 165-202): emitted only when there's at least one layer with
    '''     Value &gt; 0 (LooksMenu skips Value=0 entries at line 180-181 and only writes the
    '''     Tints object when it built at least one entry).
    '''   • Hex format throughout: "%X" uppercase, no zero-padding. Verified against actual
    '''     LooksMenu-saved JSON files (e.g. "4D7", "72A", "525").
    ''' </summary>
    Public Function SerializePreset(preset As LooksmenuPreset, pluginManager As PluginManager) As String
        If preset Is Nothing Then Return ""

        Using ms As New MemoryStream()
            ' StyledWriter in jsoncpp uses 3-space indentation. .NET's JsonWriter doesn't expose
            ' a knob for indent size; we let it write with default (2) and post-process below to
            ' match LooksMenu byte-for-byte readability. Using a raw MemoryStream + JsonWriter
            ' (rather than serializing to object then JsonSerializer) so we can preserve the
            ' field order LooksMenu emits — the engine doesn't depend on order but human diffs
            ' between presets do.
            ' UnsafeRelaxedJsonEscaping: emit UTF-8 raw (eñes, tildes, etc.) instead of \u escapes,
            ' so a preset with a non-ASCII EditorID or plugin name diffs cleanly against one
            ' written by jsoncpp's StyledWriter. The "Unsafe" name is misleading — it's safe for
            ' file output, just not for embedding inside HTML/JS where < > & need escaping.
            Dim writerOpts As New JsonWriterOptions() With {
                .Indented = True,
                .SkipValidation = False,
                .Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }
            Using w As New Utf8JsonWriter(ms, writerOpts)
                w.WriteStartObject()

                ' Field order: alphabetical to match jsoncpp's StyledWriter, which sorts keys
                ' alphabetically when serializing a Json::Value object. Verified empirically by
                ' diffing a JSON saved by NPC_Manager against one re-written by LooksMenu in-game.
                ' Canonical order: BodyMorphs → Gender → HairColor → HeadParts → Morphs → Overlays
                ' → Skin → Tints → TintOrder → Weight. (Overlays sorts between Morphs and Skin: M<O<S.)

                ' BodyMorphs — canonical LooksMenu BodySlide slider dict. Engine convention
                ' (CharGenInterface.cpp:204-215): the key is emitted iff `morphMap` exists for the
                ' actor; if the actor has no BodyMorphs registered the key is OMITTED entirely.
                ' On Load (CharGenInterface.cpp:560-570) presence of the key — even empty — wipes
                ' the actor's morphs (RemoveMorphsByKeyword) before applying members. Absence
                ' preserves the in-game actor state.
                ' We match the same convention: emit only when non-empty. Saving a NPC with an
                ' empty slider dict therefore behaves like "no BodyMorphs declared" rather than
                ' "wipe", which is the safer round-trip semantics for an editor.
                If preset.BodyMorphSliders IsNot Nothing AndAlso preset.BodyMorphSliders.Count > 0 Then
                    w.WriteStartObject("BodyMorphs")
                    Dim bmKeys = preset.BodyMorphSliders.Keys.OrderBy(Function(k) k, StringComparer.Ordinal).ToList()
                    For Each k In bmKeys
                        w.WriteNumber(k, preset.BodyMorphSliders(k))
                    Next
                    w.WriteEndObject()
                End If

                ' Gender (always)
                w.WriteNumber("Gender", CUInt(preset.Gender))

                ' HairColor — only when non-zero (CharGenInterface.cpp:106-110)
                If preset.HairColorFormID <> 0UI Then
                    Dim hc = FormatFormIdentifier(preset.HairColorFormID, pluginManager)
                    If Not String.IsNullOrEmpty(hc) Then w.WriteString("HairColor", hc)
                End If

                ' HeadParts (always, even if empty array)
                w.WriteStartArray("HeadParts")
                For Each fid In preset.HeadPartFormIDs
                    Dim ident = FormatFormIdentifier(fid, pluginManager)
                    If Not String.IsNullOrEmpty(ident) Then w.WriteStringValue(ident)
                Next
                w.WriteEndArray()

                ' MRSV travels through the canonical Morphs.Values channel (positional 5-float
                ' array per CharGenInterface.cpp LoadPreset.Allocate(5)). No separate top-level key.

                ' Morphs container — only emit when at least one sub-field has data.
                ' Sub-key order also alphabetical: Intensity → Presets → Regions → Values.
                Dim hasValues = preset.BodyMorphValues.Count > 0
                Dim hasPresets = preset.ChargenFaceMorphs.Count > 0
                Dim hasRegions = preset.FaceBoneRegions.Count > 0
                Dim hasIntensity = (preset.FacialMorphIntensity <> 1.0F)
                If hasValues OrElse hasPresets OrElse hasRegions OrElse hasIntensity Then
                    w.WriteStartObject("Morphs")

                    If hasIntensity Then
                        w.WriteNumber("Intensity", preset.FacialMorphIntensity)
                    End If

                    If hasPresets Then
                        ' Hex keys sorted alphabetically (case-insensitive). LooksMenu's jsoncpp
                        ' sorts member names lexicographically, which for uppercase hex is the
                        ' same as numeric sort — but we sort the strings explicitly to match.
                        w.WriteStartObject("Presets")
                        Dim presetKeys = preset.ChargenFaceMorphs.Keys.
                            Select(Function(k) k.ToString("X", Globalization.CultureInfo.InvariantCulture)).
                            OrderBy(Function(s) s, StringComparer.Ordinal).
                            ToList()
                        For Each keyStr In presetKeys
                            Dim k As UInteger = UInteger.Parse(keyStr, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture)
                            w.WriteNumber(keyStr, preset.ChargenFaceMorphs(k))
                        Next
                        w.WriteEndObject()
                    End If

                    If hasRegions Then
                        w.WriteStartObject("Regions")
                        Dim regionKeys = preset.FaceBoneRegions.Keys.
                            Select(Function(k) k.ToString("X", Globalization.CultureInfo.InvariantCulture)).
                            OrderBy(Function(s) s, StringComparer.Ordinal).
                            ToList()
                        For Each keyStr In regionKeys
                            Dim k As UInteger = UInteger.Parse(keyStr, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture)
                            Dim values = preset.FaceBoneRegions(k)
                            w.WriteStartArray(keyStr)
                            ' LooksMenu serializes exactly 8 floats per region (CharGenInterface.cpp:147
                            ' `for(UInt32 f = 0; f < 8; f++)`). Pad with 0 if we have less, truncate
                            ' the trailing scale-or-padding slot if we somehow have more (the ESP
                            ' parser may keep an extra "Unknown" trailing byte per RecordParsers.vb:48
                            ' "7 floats + trailing Unknown byte array").
                            For i = 0 To 7
                                Dim v As Single = If(i < values.Length, values(i), 0.0F)
                                w.WriteNumberValue(v)
                            Next
                            w.WriteEndArray()
                        Next
                        w.WriteEndObject()
                    End If

                    If hasValues Then
                        ' LoadPreset.Allocate(5) hardcodes the array size — pad/truncate to match.
                        w.WriteStartArray("Values")
                        For i = 0 To 4
                            Dim v As Single = If(i < preset.BodyMorphValues.Count, preset.BodyMorphValues(i), 0.0F)
                            w.WriteNumberValue(v)
                        Next
                        w.WriteEndArray()
                    End If

                    w.WriteEndObject()
                End If

                ' Overlays (body tattoos) — emitted when non-empty. Mirrors CharGenInterface.cpp
                ' SavePreset:217-244: an array of objects each with template + priority, plus optional
                ' tint[r,g,b,a] / offsetUV[x,y] / scaleUV[x,y] (only written when the corresponding
                ' kHas* flag was set in-game — i.e. when our parsed field is non-Nothing). Sorts
                ' alphabetically between Morphs and Skin. We keep insertion order within the array
                ' (it's a JSON array, not an object — jsoncpp does NOT reorder array elements, and the
                ' engine's load preserves order too; priority drives render order independently).
                If preset.Overlays IsNot Nothing AndAlso preset.Overlays.Count > 0 Then
                    w.WriteStartArray("Overlays")
                    For Each ov In preset.Overlays
                        w.WriteStartObject()
                        ' Per-object sub-keys also alphabetical (jsoncpp sorts object members):
                        ' offsetUV → priority → scaleUV → template → tint.
                        If ov.OffsetUV IsNot Nothing Then
                            w.WriteStartArray("offsetUV")
                            For Each f In ov.OffsetUV : w.WriteNumberValue(f) : Next
                            w.WriteEndArray()
                        End If
                        w.WriteNumber("priority", ov.Priority)
                        ' scaleUV — written CORRECTLY here. The engine SAVE has a bug
                        ' (CharGenInterface.cpp:238-239 appends offsetUV.x/y into the scaleUV array
                        ' instead of scaleUV.x/y), which corrupts scale on re-save. We deliberately
                        ' DO NOT replicate that bug: the engine LOAD (:608-610) reads scaleUV
                        ' faithfully, so emitting the real scale preserves round-trip AND avoids
                        ' corrupting the value. Conscious divergence from the engine save.
                        If ov.ScaleUV IsNot Nothing Then
                            w.WriteStartArray("scaleUV")
                            For Each f In ov.ScaleUV : w.WriteNumberValue(f) : Next
                            w.WriteEndArray()
                        End If
                        w.WriteString("template", ov.TemplateId)
                        If ov.Tint IsNot Nothing Then
                            w.WriteStartArray("tint")
                            For Each f In ov.Tint : w.WriteNumberValue(f) : Next
                            w.WriteEndArray()
                        End If
                        w.WriteEndObject()
                    Next
                    w.WriteEndArray()
                End If

                ' Skin — F4SE LM SkinTemplate id. Emitted only when non-empty so unset presets
                ' don't claim an override they don't have. CharGenInterface.cpp serializes this
                ' as a plain string key (the template id; LM resolves it against in-memory
                ' SkinTemplate registry on Load via SkinInterface::AddSkinOverride).
                If Not String.IsNullOrEmpty(preset.SkinTemplateId) Then
                    w.WriteString("Skin", preset.SkinTemplateId)
                End If

                ' Tints + TintOrder. Skip Value=0 entries (CharGenInterface.cpp:180-181). Both keys
                ' only emitted when at least one layer survives the filter. The tint dict keys are
                ' sorted alphabetically (lexicographic on uppercase hex) to match jsoncpp's output;
                ' TintOrder preserves the original render-order independently.
                Dim emittedTints = preset.FaceTintLayers.Where(Function(tl) tl.Value > 0).ToList()
                If emittedTints.Count > 0 Then
                    w.WriteStartObject("Tints")
                    Dim sortedTints = emittedTints.
                        OrderBy(Function(tl) (CUInt(tl.Index) And &HFFFFUI).ToString("X", Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal).
                        ToList()
                    For Each tl In sortedTints
                        Dim keyName = (CUInt(tl.Index) And &HFFFFUI).ToString("X", Globalization.CultureInfo.InvariantCulture)
                        w.WriteStartObject(keyName)
                        ' Sub-key order alphabetical: Color → ColorID → Percent → Type. Matches
                        ' a canonical Marcy preset diff: jsoncpp orders these the same way.
                        ' Palette-only color fields (CharGenInterface.cpp:191-195). For
                        ' Discriminator=2 (TextureSet) the engine writes neither Color nor ColorID.
                        If tl.Discriminator = 1US Then
                            ' LooksMenu's `palette->color.bgra` UInt32 has bytes in memory order
                            ' [R, G, B, A] despite the field name (verified empirically: TEND
                            ' raw R=0xE9 G=0xDA B=0xD8 → LM emits Color=0x00D8DAE9, which packs
                            ' as B<<16 | G<<8 | R, NOT R<<16 | G<<8 | B). My previous shift had
                            ' R and B swapped, producing colors with R/B mirrored vs the in-game
                            ' palette — the rendered tint visibly differed from the original NPC.
                            ' A is forced to 0: ESP parser sets A=255 (RecordParsers.vb:940) but
                            ' a Color with bit 31 set serializes as negative Int32 in jsoncpp,
                            ' and LooksMenu's asUInt() then asserts → entire Tints block is
                            ' silently dropped via try/catch.
                            Dim bgra As UInteger =
                                (CUInt(tl.Color.B) << 16) Or
                                (CUInt(tl.Color.G) << 8) Or
                                CUInt(tl.Color.R)
                            ' Use the unsigned overload so System.Text.Json emits the value as
                            ' a positive number (negative Int32 trips LooksMenu's asUInt assert).
                            w.WriteNumber("Color", bgra)
                            w.WriteNumber("ColorID", tl.TemplateColorIndex)
                        End If
                        w.WriteNumber("Percent", CInt(tl.Value))
                        w.WriteNumber("Type", CInt(tl.Discriminator))
                        w.WriteEndObject()
                    Next
                    w.WriteEndObject()

                    ' TintOrder preserves the render-order, NOT the alphabetical sort.
                    w.WriteStartArray("TintOrder")
                    For Each tl In emittedTints
                        w.WriteStringValue((CUInt(tl.Index) And &HFFFFUI).ToString("X", Globalization.CultureInfo.InvariantCulture))
                    Next
                    w.WriteEndArray()
                End If

                ' Weight — always 3 floats (CharGenInterface.cpp:113-115). Missing slot = 0.
                ' Emitted last to preserve alphabetical key order (T < W).
                w.WriteStartArray("Weight")
                w.WriteNumberValue(preset.WeightThin.GetValueOrDefault(0.0F))
                w.WriteNumberValue(preset.WeightMuscular.GetValueOrDefault(0.0F))
                w.WriteNumberValue(preset.WeightFat.GetValueOrDefault(0.0F))
                w.WriteEndArray()

                ' === NPC_Manager extensions (NOT part of vanilla LM schema) ===
                ' Prefix "_npcm_" marca extensions específicas de NPC_Manager fuera del namespace
                ' LM. CharGenInterface.cpp LoadPreset accede a keys conocidas por nombre via
                ' root["Key"]; no itera el objeto root → unknown keys son ignoradas silenciosamente
                ' por LM in-game. Verificado contra F4SEPlugins-master/f4ee/CharGenInterface.cpp.
                ' Independientes entre sí; la precedencia en aplicación la resuelve
                ' NpcRecordOverlay (orden: NPC.WNAM primero, luego LM SkinTemplate pisa si está
                ' set), mismo orden que el overlay aplica a render.
                If preset.SkinFormIDOverride.HasValue Then
                    Dim sfId = FormatFormIdentifier(preset.SkinFormIDOverride.Value, pluginManager)
                    ' Empty string = clear semantic (engine cae a RACE.WNAM). Emitimos string siempre
                    ' (incluso vacío) cuando HasValue=True para distinguir de "key absent" = preserve.
                    w.WriteString("_npcm_SkinFormID", sfId)
                End If
                If preset.DefaultOutfitFormIDOverride.HasValue Then
                    Dim ofId = FormatFormIdentifier(preset.DefaultOutfitFormIDOverride.Value, pluginManager)
                    ' Empty string = "no outfit" (Some(0)). String siempre presente cuando HasValue=True
                    ' para distinguir de "key absent" = preserve raw NPC.DOFT.
                    w.WriteString("_npcm_DefaultOutfit", ofId)
                End If
                If preset.IsCharGenFacePreset.HasValue Then
                    w.WriteBoolean("_npcm_IsCharGenPreset", preset.IsCharGenFacePreset.Value)
                End If

                w.WriteEndObject()
                w.Flush()
            End Using

            Dim json = Encoding.UTF8.GetString(ms.ToArray())
            ' Re-indent from 2 spaces (.NET default) to 3 (LooksMenu StyledWriter) so the file
            ' diffs cleanly against ones written in-game. Cheap line-by-line conversion.
            Return ConvertIndentationFromTwoToThree(json)
        End Using
    End Function

    Private Function ConvertIndentationFromTwoToThree(json As String) As String
        Dim sb As New System.Text.StringBuilder(json.Length + json.Length \ 8)
        Dim lines = json.Split({vbCrLf, vbLf}, StringSplitOptions.None)
        For i = 0 To lines.Length - 1
            Dim line = lines(i)
            Dim leading = 0
            While leading < line.Length AndAlso line(leading) = " "c
                leading += 1
            End While
            ' Each 2-space indent becomes 3 spaces. Odd remainders pass through (shouldn't happen).
            Dim depth = leading \ 2
            Dim extra = leading Mod 2
            sb.Append(New String(" "c, depth * 3 + extra))
            ' StyledWriter (jsoncpp) puts a space BEFORE the colon as well as after: `"key" : value`.
            ' Utf8JsonWriter omits the leading space. Patch the rest of the line — only the FIRST
            ' `":` per line needs fixing (subsequent ones are inside string literals if any). We
            ' rely on the fact that key strings written by Utf8JsonWriter never contain a literal
            ' `":` because the writer escapes embedded quotes.
            Dim rest = line.Substring(leading)
            Dim colonIdx = rest.IndexOf(""":", StringComparison.Ordinal)
            If colonIdx >= 0 Then
                ' colonIdx points at the closing quote of the key; the colon is at colonIdx+1.
                ' Insert a space between them.
                sb.Append(rest, 0, colonIdx + 1)
                sb.Append(" "c)
                sb.Append(rest, colonIdx + 1, rest.Length - colonIdx - 1)
            Else
                sb.Append(rest)
            End If
            If i < lines.Length - 1 Then sb.Append(vbLf)
        Next
        Return sb.ToString()
    End Function

    ''' <summary>Inverse of ResolveFormIdentifier: take a global FormID, find its owning plugin
    ''' in the load order, and emit "Plugin.esp|HEX" with the local 24-bit FormID.</summary>
    Private Function FormatFormIdentifier(globalFormID As UInteger, pluginManager As PluginManager) As String
        If globalFormID = 0UI Then Return ""
        ' GetOriginatingPluginName handles both full (high byte = full slot) and ESL (0xFE light) globals.
        Dim pluginName = pluginManager.GetOriginatingPluginName(globalFormID)
        If String.IsNullOrEmpty(pluginName) Then Return ""
        ' LooksMenu's local = runtime FormID & 0xFFFFFF (for ESL this carries the light-slot bits, which
        ' is exactly what GlobalFormIDFromIdentifierLocal expects on the way back).
        Dim localFormID = globalFormID And &HFFFFFFUI
        ' LooksMenu uses %06X (6-digit zero-padded hex) per Utilities.cpp:127.
        Return pluginName & "|" & localFormID.ToString("X6", Globalization.CultureInfo.InvariantCulture)
    End Function

    ''' <summary>List preset JSON files. LooksMenu's actual convention (verified empirically and
    ''' against CharGenInterface.cpp:259-620 LoadPreset) is a FLAT directory: all presets live
    ''' directly in Data\F4SE\Plugins\F4EE\Presets\, no per-race subfolder. The UI compiled into
    ''' the LooksMenu .swf builds the path; the C++ side (ScaleformNatives.cpp:85-90) just receives
    ''' the file path string. The JSON itself does not store a race — only Gender — and LoadPreset
    ''' applies the preset to the current actor's race regardless of where the preset originated.
    '''
    ''' Returns absolute paths in alphabetical order, recursing one level so user-organized
    ''' subfolders (some users group presets by race or author) are still found.</summary>
    Public Function EnumeratePresetFiles(dataPath As String) As List(Of String)
        Dim result As New List(Of String)
        If String.IsNullOrEmpty(dataPath) Then Return result
        Dim dir = Path.Combine(dataPath, "F4SE", "Plugins", "F4EE", "Presets")
        If Not Directory.Exists(dir) Then Return result
        result.AddRange(Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
        Return result
    End Function
End Module
