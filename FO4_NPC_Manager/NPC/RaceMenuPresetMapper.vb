Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Single source of truth for the FULL RaceMenu <c>.jslot</c> ↔ <see cref="LooksmenuLoader.LooksmenuPreset"/>
''' mapping (SSE only). Unifies the two per-editor mappings that today live split across the Edit Face and Edit Body
''' forms so Task-A's game-aware Load/Save/Copy/Paste can round-trip a whole preset in one call instead of the editor-
''' scoped halves.
'''
''' The transforms below are copied faithfully from those editors (cited per block) — no scaling is invented:
'''   FACE  (EditFace_Form.OnSaveJslot :602-621 / OnLoadJslot :566-584 / ApplySseTintOverlay :824-833 /
'''          ParseSseTintLayers :704-714): sliders↔SseNam9 (+VampireMorph sentinel), sculpt↔SseSculptHead (×/÷ divisor),
'''          custom↔SseCustomMorphs, tintInfo↔SseTintLayers.
'''   BODY  (EditBody_Form.OnSaveJslot :1085-1090 / OnLoadJslot :1046-1053 / BuildJslotBodyMorphs :1125-1144 /
'''          JslotBodyMorphsToKeyed :1102-1120): actor.weight↔SseWeight, bodyMorphs↔BodyMorphsKeyed (flat fallback),
'''          overrides↔SseBodyOverlays.
'''
''' NOTE (future cleanup): the editors still run their own inline copies of these transforms; they are intentionally
''' left untouched in this task. A later refactor should route EditFace/EditBody Load/Save .jslot through here.</summary>
Public Module RaceMenuPresetMapper

    ''' <summary>Traducir un <c>headParts[].formId</c> de un <c>.jslot</c> SIN <c>formIdentifier</c> (formato
    ''' viejo de RaceMenu) al FormID global de ESTA sesión, usando la tabla <c>mods</c> del propio archivo.
    ''' Devuelve 0 cuando no se puede — sin tabla, sin entrada para ese índice, o el plugin no está cargado.
    '''
    ''' <para>Réplica exacta del fallback del motor, <c>skee PresetInterface.cpp:988-1002</c>:
    ''' <c>modIndex = formId &gt;&gt; 24</c>; la clave es <c>modIndex &lt;&gt; 0xFE ? modIndex : (formId &gt;&gt; 12)</c>
    ''' (= <c>ModInfo::GetPartialIndex</c>, f4se GameData.h:87-90, que es como skee ESCRIBIÓ la tabla);
    ''' después <c>LookupModByName</c> y <c>modInfo-&gt;GetFormID(formId)</c>, que enmascara los bits bajos al
    ''' ancho del plugin ACTUAL (24 full / 12 light). De este lado esas dos piezas ya existen:
    ''' <see cref="PluginManager.PartialIndexOfFormID"/> y
    ''' <see cref="PluginManager.GlobalFormIDFromIdentifierLocal"/>.</para>
    '''
    ''' <para>Sólo para el formato viejo. Un <c>.jslot</c> moderno trae <c>formIdentifier</c> y ése gana
    ''' siempre, porque es portable por construcción y no depende de ninguna tabla.</para></summary>
    Friend Function ResolveLegacyHeadPartFormId(rawFormId As UInteger,
                                                modIndexToName As Dictionary(Of UInteger, String),
                                                pluginManager As PluginManager) As UInteger
        If rawFormId = 0UI OrElse pluginManager Is Nothing Then Return 0UI
        If modIndexToName Is Nothing OrElse modIndexToName.Count = 0 Then Return 0UI
        Dim modName As String = Nothing
        If Not modIndexToName.TryGetValue(PluginManager.PartialIndexOfFormID(rawFormId), modName) Then Return 0UI
        If String.IsNullOrEmpty(modName) Then Return 0UI
        ' Devuelve 0 si el plugin no está cargado, que es justo el "no se pudo" del contrato.
        Return pluginManager.GlobalFormIDFromIdentifierLocal(modName, rawFormId)
    End Function

    ''' <summary>Si skee64 deja PUESTA en el actor una head part que el <c>.jslot</c> trae y que ya resolvió.
    ''' Dos filtros del motor, en su orden:
    ''' <list type="number">
    ''' <item>Carga (LoadJsonPreset :982 / :1000): <c>DYNAMIC_CAST(form, TESForm, BGSHeadPart)</c> — un form que no
    ''' es HDPT no entra en <c>presetData->headParts</c>.</item>
    ''' <item>Aplicación (ApplyPresetData :164-175), por actor: (a) el flag de sexo del HDPT tiene que coincidir con
    ''' el del NPC — <c>gender==0 &amp;&amp; partFlags &amp; kFlagMale</c> o <c>gender==1 &amp;&amp; partFlags &amp;
    ''' kFlagFemale</c> (:165-166; kFlagMale = 1&lt;&lt;1 = DATA bit 1, kFlagFemale = 1&lt;&lt;2 = bit 2, skse64
    ''' GameForms.h:790-791); (b) <c>part->validRaces</c> no nulo (:169) — RNAM = 0 ⇒ la parte se SALTEA, sin pase
    ''' por «humanoide» ni por «default de raza»; (c) <c>validRaces->Visit(ValidRaceFinder(race))</c> (:170-171):
    ''' la FLST contiene la raza del NPC por igualdad de puntero (:30-42), recorriendo sus entradas directas y las
    ''' agregadas por script (<c>BGSListForm::Visit</c>, skse64 GameForms.cpp:85-110; de este lado
    ''' <see cref="HeadPartResolver.RaceCompatCatalog"/> reconstruye las agregadas). NO recurre en FLSTs anidadas.</item>
    ''' </list>
    ''' Distinto a propósito de <see cref="HeadPartResolver.IsHdptValidForRace"/>: ése es el criterio del PICKER
    ''' y del browser, con dos pases propios de la app (RNAM=0 en raza humanoide; default de género de la RACE) que
    ''' el motor no tiene, y sin el flag de sexo. Sin raza conocida (<paramref name="raceFid"/> = 0) el filtro (2)
    ''' no se puede evaluar y se devuelve True: no se inventa un actor.
    ''' Hueco: RNAM apuntando a un form que NO es FLST — no sé qué carga el juego en <c>validRaces</c>; acá se
    ''' trata como nulo (no aplica).</summary>
    Public Function MotorAplicaHeadPart(fid As UInteger, raceFid As UInteger, isFemale As Boolean,
                                        pm As PluginManager, flstCache As Dictionary(Of UInteger, Canon.IFlst),
                                        ByRef motivo As String) As Boolean
        motivo = ""
        Dim rec = pm.GetRecord(fid)
        If rec Is Nothing Then
            motivo = "no record with this FormID in the load order"
            Return False
        End If
        If rec.Header.Signature <> "HDPT" Then
            motivo = $"the record is a {rec.Header.Signature}, not a HDPT"
            Return False
        End If
        If raceFid = 0UI Then Return True
        Dim hd = Canon.CanonRecords.Hdpt(rec, pm)
        ' Sin parser del HDPT no hay flags ni RNAM que leer: no se puede evaluar ⇒ se aplica lo resuelto (el gate
        ' del browser, que sí lo rechaza, lo sigue viendo en HeadPartFormIDs).
        If hd Is Nothing Then Return True
        If Not If(isFemale, hd.FlagsFemale, hd.FlagsMale) Then
            motivo = $"its DATA flags don't include {If(isFemale, "Female", "Male")}"
            Return False
        End If
        If hd.ValidRaces = 0UI Then
            motivo = "it declares no Valid Races (RNAM=0): the engine never applies it from a preset"
            Return False
        End If
        Dim flst As Canon.IFlst = Nothing
        If Not flstCache.TryGetValue(hd.ValidRaces, flst) Then
            Dim flstRec = pm.GetRecord(hd.ValidRaces)
            If flstRec IsNot Nothing AndAlso flstRec.Header.Signature = "FLST" Then
                flst = Canon.CanonRecords.Flst(flstRec, pm)
            End If
            flstCache(hd.ValidRaces) = flst
        End If
        If flst Is Nothing Then
            motivo = $"its Valid Races FLST 0x{hd.ValidRaces:X8} doesn't exist in this load order"
            Return False
        End If
        If flst.Miembros().Contains(raceFid) Then Return True
        If HeadPartResolver.RaceCompatCatalog IsNot Nothing AndAlso
           HeadPartResolver.RaceCompatCatalog.ContainsRace(hd.ValidRaces, raceFid) Then Return True
        motivo = $"its Valid Races FLST 0x{hd.ValidRaces:X8} doesn't list race 0x{raceFid:X8}"
        Return False
    End Function

    ''' <summary>Full preset → <c>.jslot</c>. Combines the FACE mapping (EditFace_Form.OnSaveJslot) and the BODY
    ''' mapping (EditBody_Form.OnSaveJslot + BuildJslotBodyMorphs). Never returns Nothing; a Nothing/empty preset
    ''' yields an all-default jslot.</summary>
    Public Function ToJslot(preset As LooksmenuLoader.LooksmenuPreset,
                            Optional pluginManager As PluginManager = Nothing,
                            Optional raceFid As UInteger = 0UI, Optional isFemale As Boolean = False) As RaceMenuJslot
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
                ' HadFormIdentifier = lo que Save va a emitir (la key si tiene contenido o si el archivo la traía, RaceMenuJslot.Save).
                j.HeadParts.Add(New RaceMenuJslot.JslotHeadPart With {
                    .FormId = fid, .FormIdentifier = ident, .Type = ptype, .HadFormIdentifier = Not String.IsNullOrEmpty(ident)})
            Next
        End If
        ' Re-emit the head parts that ApplyJslotToPreset couldn't resolve (owning mod absent from THIS load
        ' order) exactly as they came in, so a preset whose mod is missing does not LOSE that hair/eyes entry.
        ' Skee behaves the same: it skips the head part it can't apply but leaves the stored entry intact.
        ' HOY ESTA RAMA NO SE ALCANZA — misma situación que la de UnresolvedHairColor
        ' (LooksmenuLoader.vb:997). El único caller de ToJslot es MainForm ("Save RaceMenu Preset"), que arma
        ' el preset con BuildPresetFromState — desde el ESTADO del NPC, nunca desde un .jslot leído de disco —
        ' y ese constructor NO copia SseUnresolvedHeadParts. O sea que la app NO hace load→save de un archivo
        ' de preset, en ningún juego. Se deja como red LATENTE: el día que exista ese camino, sin ella el dato
        ' se pierde en silencio.
        ' MATIZ QUE FALTABA, Y QUE HACE ALCANZABLES DEFECTOS REALES: es cierto que la app no hace
        ' load→save del ARCHIVO, pero los DATOS sí hacen el viaje completo. ApplyJslotToPreset deja en
        ' `_appliedPresets` los SseBodyOverlays (con su RawValues), SseNodeTransforms (con su Raw),
        ' SseSkinOverrides, SseSculptParts, SseCustomMorphs y demás, y BuildPresetFromState los copia tal
        ' cual (MainForm.vb:10015-10041) antes de que ToJslot los re-serialice. O sea: cargar el .jslot del
        ' usuario y guardar SÍ puede corromperlo. Leer esto como "no hay round-trip, no hay riesgo" es el
        ' error opuesto al que el comentario venía a evitar — y los dos ya costaron un diagnóstico malo.
        ' Lo único que efectivamente NO viaja es SseUnresolvedHeadParts: ese campo no se copia.
        If preset.SseUnresolvedHeadParts IsNot Nothing Then
            For Each h In preset.SseUnresolvedHeadParts
                If h Is Nothing Then Continue For
                j.HeadParts.Add(New RaceMenuJslot.JslotHeadPart With {
                    .FormId = h.FormId, .FormIdentifier = h.FormIdentifier, .Type = h.Type, .HadFormIdentifier = h.HadFormIdentifier})
            Next
        End If
        ' Sólo el override CON VALOR se emite. Los otros dos estados no tienen representación en el formato:
        ' `Nothing` (sin override) es la ausencia de la key, y el CLEAR EXPLÍCITO (Some(0)) es INEXPRESABLE —
        ' skee64 sólo aplica headTexture dentro de `if (presetData->headTexture)` (PresetInterface.cpp:147), así
        ' que nada que escribamos acá haría que RaceMenu limpie el FTST. El clear degrada a "preservar" en el
        ' round-trip por .jslot; está documentado en el campo. NO inventar `"headTexture": ""` para significar
        ' clear: sería inerte in-game y además key churn contra el archivo del usuario (ver j.Save).
        If preset.SseHeadTextureFormIDOverride.HasValue AndAlso preset.SseHeadTextureFormIDOverride.Value <> 0UI _
           AndAlso pluginManager IsNot Nothing Then
            j.HeadTexture = LooksmenuLoader.FormatFormIdentifier(preset.SseHeadTextureFormIDOverride.Value, pluginManager)
        End If
        ' Hair colour (actor.hairColor). j.Save emits the key SÓLO con HadHairColor (antes lo emitía SIEMPRE, y
        ' dejarlo sin setear escribía hairColor:0
        ' — which RaceMenu applies as literal BLACK hair, PresetInterface.cpp:112-116 runs unconditionally over
        ' every HairTint material; y al RECARGAR el preset nuestro propio decode veía la key y forzaba el negro).
        ' Este bloque sigue siendo necesario igual: es lo que hace que un preset guardado DESDE UN NPC lleve su
        ' color real en vez de nada. skee's own exporter never emits 0 for a coloured NPC: it packs the RGB of the
        ' actor's CLFM (`headData->hairColor->color`, PresetInterface.cpp:675-677). So:
        '   1) the preset's absolute RGB when it has one (round-trip of a loaded .jslot / a user edit), else
        '   2) the RGB of the effective CLFM — BuildPresetFromState seeds preset.HairColorFormID from
        '      state.HairColorFormID, i.e. post-ApplyRaceFallbacks, so it IS the colour the NPC renders with.
        ' Only a CLFM with a real RGB qualifies: a FO4 hair CLFM carries a RemappingIndex instead (HasColor=False),
        ' and there is no meaningful RGB to export — that path correctly leaves the key at its 0 default, exactly
        ' as before this change (.jslot is an SSE format; the FO4 save path writes f4ee JSON, not this).
        If preset.SseHairColorRgb.HasValue Then
            j.HairColor = preset.SseHairColorRgb.Value
            j.HadHairColor = True
        ElseIf preset.HairColorFormID <> 0UI AndAlso pluginManager IsNot Nothing Then
            Dim clfmRec = pluginManager.GetRecord(preset.HairColorFormID)
            If clfmRec IsNot Nothing AndAlso clfmRec.Header.Signature = "CLFM" Then
                Dim clfm = Canon.CanonRecords.Clfm(clfmRec, pluginManager)
                If clfm IsNot Nothing AndAlso clfm.TieneColor() Then
                    j.HairColor = (CInt(clfm.ColorDe().R) << 16) Or (CInt(clfm.ColorDe().G) << 8) Or CInt(clfm.ColorDe().B)
                    j.HadHairColor = True
                End If
            End If
        End If

        ' ---- FACE: sliders (NAM9) → morphs.default.morphs + [18] VampireMorph sentinel (EditFace_Form.vb:602-603).
        Dim nam9 = preset.SseNam9
        For i = 0 To SseNam9MorphMap.Nam9SliderCount - 1
            Dim v As Single = 0.0F
            If nam9 IsNot Nothing AndAlso i < nam9.Length Then v = nam9(i)
            j.SliderMorphs.Add(v)
        Next
        ' Slot 18 = VampireMorph. Se emitía SIEMPRE la constante centinela, así que un load→save pisaba el
        ' valor real del NPC (1 de los 48 presets medidos trae 0 acá, y el render propio lee ese slot —
        ' NpcMorphResolver.vb:427-431). Ahora sale el que traiga el preset; el centinela queda como default
        ' para cuando no se conoce, que es lo que significa "no es vampiro" (EditFace_Form.vb:603).
        j.SliderMorphs.Add(If(preset.SseVampireMorph.HasValue, preset.SseVampireMorph.Value, 3.402823466E+38F))

        ' ---- FACE: NAMA face-part presets → morphs.default.presets (symmetric with ApplyJslotToPreset). Emitted
        ' only when the preset carries NAMA; preserves the 0xFFFFFFFF unset sentinel per family.
        If preset.SseNama IsNot Nothing Then
            For i = 0 To SseNam9MorphMap.NamaFamilyCount - 1
                j.NamaPresets.Add(If(i < preset.SseNama.Length, preset.SseNama(i), &HFFFFFFFFUI))
            Next
        End If

        ' ---- FACE: sculpt, world delta × SculptDivisor. Emit ALL per-shape blocks (head + brows + eyes + mouth)
        ' with their Host chargen tri, so a load→save round-trip preserves the full preset (not just the head).
        ' Falls back to the head-only SseSculptHead when SseSculptParts is absent (older editor-authored overlays).
        Dim sculptSource As List(Of NPC_SculptPart) = preset.SseSculptParts
        If (sculptSource Is Nothing OrElse sculptSource.Count = 0) AndAlso preset.SseSculptHead IsNot Nothing AndAlso preset.SseSculptHead.Count > 0 Then
            sculptSource = New List(Of NPC_SculptPart) From {New NPC_SculptPart With {.Host = "", .Verts = preset.SseSculptHead}}
        End If
        If sculptSource IsNot Nothing Then
            For Each blk In sculptSource
                If blk Is Nothing OrElse blk.Verts Is Nothing OrElse blk.Verts.Count = 0 Then Continue For
                Dim part As New RaceMenuJslot.JslotSculptPart With {.Host = If(blk.Host, ""), .Vertices = 0}
                For Each sv In blk.Verts
                    part.Indices.Add(sv.Index)
                    part.Dx.Add(CInt(Math.Round(sv.Dx * j.SculptDivisor)))
                    part.Dy.Add(CInt(Math.Round(sv.Dy * j.SculptDivisor)))
                    part.Dz.Add(CInt(Math.Round(sv.Dz * j.SculptDivisor)))
                Next
                j.Sculpt.Add(part)
            Next
        End If

        ' ---- FACE: NiOverride custom morphs (EditFace_Form.vb:615-617).
        If preset.SseCustomMorphs IsNot Nothing Then
            For Each cm In preset.SseCustomMorphs
                j.CustomMorphs.Add(New RaceMenuJslot.JslotCustomMorph With {.Name = cm.Name, .Value = cm.Value})
            Next
        End If

        ' ---- FACE: tints. La fuente es preset.SseTintLayers (las capas autoradas). Empaquetadas a ARGB
        ' igual que las serializa RaceMenu (PresetInterface.cpp:388): alpha = cobertura(TINV)*255, y despues
        ' (A<<24)|(R<<16)|(G<<8)|B. La mascara custom por capa (preset.SseTintTexOverride, solo de RaceMenu)
        ' viaja en tint.texture (vacio = la mascara propia de la capa de la RACE).
        If preset.SseTintLayers IsNot Nothing Then
            For Each t In preset.SseTintLayers
                If t Is Nothing OrElse Not t.Indice.HasValue Then Continue For
                Dim cobertura As Double = If(t.Cobertura.HasValue, t.Cobertura.Value / 100.0, 1.0)
                Dim aCov As UInteger = CUInt(Math.Max(0, Math.Min(255, Math.Round(cobertura * 255.0))))
                Dim col As UInteger = (aCov << 24) Or (CUInt(If(t.Rojo, CByte(0))) << 16) Or
                                      (CUInt(If(t.Verde, CByte(0))) << 8) Or CUInt(If(t.Azul, CByte(0)))
                Dim tini As Integer = CInt(t.Indice.Value)
                Dim texPath As String = ""
                If preset.SseTintTexOverride IsNot Nothing Then preset.SseTintTexOverride.TryGetValue(tini, texPath)
                ' El indice de la capa es el TINI del record; el .jslot quiere el POSICIONAL (la inversa de la
                ' traduccion del lado de la carga), asi que un guardar-cargar vuelve a la misma capa en
                ' cualquier raza.
                Dim jslotIndex As Integer = TiniToJslotIndex(pluginManager, raceFid, isFemale, tini)
                ' ⛔ NUNCA UNA CADENA VACÍA EN UNA CAPA OPACA. RaceMenu pisa la textura de la capa con lo
                ' que traiga el archivo cuando `alpha > 0` (PresetInterface.cpp:202-203), así que un ""
                ' le BORRA al jugador la máscara que declaraba la raza. El canónico escribe siempre la
                ' textura EFECTIVA (:426), no una vacía. Sin override propio se emite la de la RACE, que
                ' es exactamente eso. Ver `TexturaDeLaCapaDeRaza`.
                Dim textura As String = If(texPath, "")
                If textura = "" AndAlso aCov > 0UI Then
                    textura = TexturaDeLaCapaDeRaza(pluginManager, raceFid, isFemale, tini)
                End If
                j.TintInfo.Add(New RaceMenuJslot.JslotTint With {.Color = col, .Index = jslotIndex, .Texture = textura})
            Next
        End If

        ' ---- BODY: actor.weight ← SseWeight (EditBody_Form.vb:1085).
        ' El fallback es 100.0F, NO 0.0F. `Save` emite `actor.weight` SIEMPRE (el motor la lee sin gate:
        ' PresetInterface.cpp:1019 + :174), así que un carrier sin peso ya no significa "no escribas la key" —
        ' significa "escribí un 0", y eso deja al actor en peso 0 in-game. 100 es el mismo default que usa
        ' BuildPresetFromState cuando el record no trae NAM7.
        ' (El flag `HadWeight` que gateaba el sentido jslot→preset ya no existe: ApplyJslotToPreset asigna
        ' `SseWeight = j.Weight` incondicional, como el motor — ver el bloque BODY de ApplyJslotToPreset.)
        j.Weight = CDbl(If(preset.SseWeight.HasValue, preset.SseWeight.Value, 100.0F))

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
        If preset.SseFirstPersonTransformsRaw IsNot Nothing Then
            For Each fpJson In preset.SseFirstPersonTransformsRaw
                j.AddFirstPersonTransformJson(fpJson)
            Next
        End If

        ' ---- SKIN: skinOverrides ← RaceMenu NiOverride skin texture-tint (body-paint per slot).
        If preset.SseSkinOverrides IsNot Nothing Then
            j.SkinOverrides.AddRange(LooksmenuLoader.CloneSseSkinOverrides(preset.SseSkinOverrides))
        End If

        Return j
    End Function

    ''' <summary>Bloque de cabeza para <c>SseSculptHead</c>: el host del chargen de la cabeza base
    ''' ("...HeadCharGen" / "...HeadCustomizations") pero NO "...Brows...". Si ninguno matchea, cae al
    ''' bloque 0 (replica el comportamiento previo de Sculpt(0)). Nothing si no hay bloques.
    ''' <para>Public y extraída de <see cref="ApplyJslotToPreset"/> para que el reconstructor
    ''' (SseMorphReverseEngineer), que escribe los campos del preset directamente sin pasar por un jslot,
    ''' use ESTA MISMA regla en vez de una copia que se desincronizaría.</para></summary>
    Public Function SelectHeadSculptBlock(parts As List(Of NPC_SculptPart)) As List(Of NPC_SculptVert)
        If parts Is Nothing OrElse parts.Count = 0 Then Return Nothing
        Dim headBlk = parts.FirstOrDefault(Function(p) p.Host IsNot Nothing AndAlso
                                               p.Host.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
                                               p.Host.IndexOf("Brows", StringComparison.OrdinalIgnoreCase) < 0)
        If headBlk Is Nothing Then headBlk = parts(0)
        Return headBlk.Verts
    End Function

    ''' <param name="capasBase">Las capas de tinte que el NPC YA TENÍA — normalmente las del record
    ''' (<c>LooksmenuLoader.CapasDeTinteSseDelRecord</c>). Con esto la aplicación de tintes FUSIONA por TINI
    ''' como el motor (PresetInterface.cpp:197, en el lugar y por posición) en vez de reemplazar la lista.
    ''' <b>Se omite a propósito en el mapeo "qué trae el ARCHIVO"</b> (MainForm, el segundo mapeo sobre un
    ''' preset vacío): ahí fusionar le atribuiría al archivo tintes del NPC y falsearía el conteo por
    ''' categoría y el reporte de compatibilidad. Si el preset que entra YA trae tintes autorados, esos
    ''' mandan como base y este parámetro no se usa.</param>
    Public Sub ApplyJslotToPreset(j As RaceMenuJslot, preset As LooksmenuLoader.LooksmenuPreset,
                                  Optional pluginManager As PluginManager = Nothing,
                                  Optional raceFid As UInteger = 0UI, Optional isFemale As Boolean = False,
                                  Optional capasBase As List(Of LooksmenuLoader.CapaDeTinteSsePreset) = Nothing)
        If j Is Nothing OrElse preset Is Nothing Then Return

        ' ---- FACE IDENTITY: headParts (hair/eyes/brows/…) → preset.HeadPartFormIDs = lo que skee64 deja PUESTO
        ' en el NPC después de LoadPreset. Dos etapas del motor, replicadas las dos:
        '   (1) LoadJsonPreset :976-1015 RESUELVE cada entrada: `isMember("formIdentifier")` ⇒ GetFormFromIdentifier
        '       (:979-986; y SÓLO si la clave no está prueba `formId` + la tabla `mods` :988-1013). Lo que no resuelve
        '       (form nulo, mod fuera de la tabla o inactivo, o un form que NO es BGSHeadPart :982/:1000) no entra
        '       en `presetData->headParts` y el resto se aplica igual.
        '   (2) ApplyPresetData :164-175 FILTRA por actor antes de ChangeHeadPart: flag de sexo del HDPT + `validRaces`
        '       existente y que contenga la raza (ver MotorAplicaHeadPart). Lo filtrado NO queda en el NPC.
        ' El portable id es "formIdentifier" ("Plugin|FormID"); se resuelve contra el load order actual
        ' (LooksmenuLoader.ResolveFormIdentifier). Requiere el PluginManager; sin él, identidad intacta.
        '
        ' Con identificador presente e irresoluble (fid=0: el mod dueño no está en este load order) NO se cae a
        ' h.FormId: ese FormId es el absoluto del load order del AUTOR, cuyo byte alto acá no nombra nada — metería
        ' un FormID basura que el gate de raza no resuelve y que (MEDIDO) hacía que HeadPartResolver descartara el
        ' preset ENTERO. Se SALTEA y se PRESERVA verbatim (SseUnresolvedHeadParts ⇒ ToJslot lo re-emite al
        ' guardar; divergencia decidida por el usuario el 24-ago, ver CopyUnresolvedHeadPartsToSnapshot). Sólo
        ' cuando el archivo NO trae la clave (`HadFormIdentifier=False`, .jslot viejo) se usa formId + `mods`.
        If pluginManager IsNot Nothing AndAlso j.HeadParts IsNot Nothing AndAlso j.HeadParts.Count > 0 Then
            ' ⛔ IDEMPOTENCIA: las TRES listas se vacian ACA, adentro del mismo `If` que gobierna la lista
            ' resuelta. `preset.HeadPartFormIDs` ya se REEMPLAZA mas abajo (Clear + AddRange), pero las de
            ' NO resueltas solo se AGREGABAN, asi que aplicar un segundo .jslot sobre el mismo objeto
            ' —cargar A y despues B— dejaba las de A pegadas y `ToJslot` las re-emitia al guardar B.
            ' MEDIDO: 49 entradas de 14 mods, en 33 de 48 archivos del corpus.
            ' El `Clear` va adentro del `If` y no arriba a proposito: un .jslot SIN bloque `headParts` no
            ' declara nada sobre head parts y no tiene por que borrar lo que el preset ya traia.
            ' Con esto, el dedup por string que habia en las dos ramas de abajo sobra: era la compensacion
            ' de este mismo defecto, y un dedup no puede distinguir "repetida de esta carga" de
            ' "sobrante de la carga anterior".
            preset.UnresolvedHeadParts.Clear()
            preset.SseUnresolvedHeadParts.Clear()
            preset.SseHeadPartsFiltradasPorMotor.Clear()
            Dim hp As New List(Of UInteger)
            ' Una FLST de RNAM se parsea una sola vez por carga (varias partes comparten la misma lista).
            Dim flstCache As New Dictionary(Of UInteger, Canon.IFlst)
            For Each h In j.HeadParts
                If h Is Nothing Then Continue For
                Dim fid As UInteger
                If h.HadFormIdentifier Then
                    ' :979: la clave está ⇒ esta rama, aunque el valor sea "" o null (asString ⇒ "" ⇒
                    ' GetFormFromIdentifier ⇒ nullptr ⇒ no resuelve). Nunca cae a formId.
                    fid = LooksmenuLoader.ResolveFormIdentifier(If(h.FormIdentifier, ""), pluginManager)
                    If fid = 0UI Then
                        ' Unresolved: skip + preserve verbatim (both the diagnostic string and the full entry).
                        ' Sin dedup: las listas se vaciaron al entrar al bloque, asi que lo unico que puede
                        ' repetirse es una entrada repetida DENTRO de este mismo .jslot — y esa la queremos
                        ' re-emitir tal cual vino, que es lo que significa "preservar verbatim".
                        preset.UnresolvedHeadParts.Add(If(h.FormIdentifier, ""))
                        preset.SseUnresolvedHeadParts.Add(h)
                        If Logger.Enabled Then
                            Dim srcName As String = System.IO.Path.GetFileName(If(preset.SourcePath, ""))
                            Dim ident As String = If(h.FormIdentifier, "")
                            Logger.LogLazy(Function() $"[LMLoad] '{srcName}': head part '{ident}' unresolved (plugin not in load order) -> skipped, preserved verbatim.")
                        End If
                        Continue For
                    End If
                Else
                    ' No portable id (.jslot viejo). NO se usa h.FormId crudo: su byte alto es un slot del
                    ' load order del AUTOR y en el nuestro —que además está COMPACTADO por el Preflight—
                    ' nombra otro plugin. Eso es exactamente lo que el comentario de arriba describe como
                    ' MEDIDO ("makes HeadPartResolver discard the WHOLE preset"), así que hacerlo acá era
                    ' cometer el daño que el bloque documenta.
                    ' La tabla de traducción está EN EL ARCHIVO: `mods` = [{index,name}] con el partial index
                    ' del autor. Es lo mismo que hace skee (PresetInterface.cpp:992-997): buscar el nombre por
                    ' índice y re-encodear con el índice ACTUAL vía ModInfo::GetFormID — que de este lado es
                    ' GlobalFormIDFromIdentifierLocal (enmascara al ancho del dueño, 12 bits si light y 24 si
                    ' full, igual que GetFormID).
                    fid = ResolveLegacyHeadPartFormId(h.FormId, j.ModIndexToName, pluginManager)
                    If fid = 0UI Then
                        ' El mod no está en este load order (o el .jslot no trae tabla): mismo trato que un
                        ' identifier que no resuelve — se SALTEA y se PRESERVA verbatim, para no borrarle al
                        ' usuario el pelo/ojos al guardar en una máquina que no tiene ese mod.
                        Dim legacyKey As String = "#" & h.FormId.ToString("X8", Globalization.CultureInfo.InvariantCulture)
                        preset.UnresolvedHeadParts.Add(legacyKey)
                        preset.SseUnresolvedHeadParts.Add(h)
                        If Logger.Enabled Then
                            Dim srcName2 As String = System.IO.Path.GetFileName(If(preset.SourcePath, ""))
                            Dim rawFid As UInteger = h.FormId
                            Logger.LogLazy(Function() $"[LMLoad] '{srcName2}': legacy head part 0x{rawFid:X8} " &
                                                      "unresolved (no `mods` entry, or its plugin is not in the " &
                                                      "load order) -> skipped, preserved verbatim.")
                        End If
                        Continue For
                    End If
                End If
                If fid = 0UI Then Continue For
                ' Etapa (2): el filtro por actor de ApplyPresetData :164-175 (y el DYNAMIC_CAST a BGSHeadPart de
                ' :982/:1000, que ya en la carga descarta un form que no es HDPT). Lo que el motor no aplica NO va
                ' a HeadPartFormIDs — va a SseHeadPartsFiltradasPorMotor para que el gate de raza del browser y el
                ' reporte sigan viendo lo que el archivo declara. Sin raza conocida (raceFid=0: los probes de
                ' Tools llaman sin ella) el filtro de :164-175 no se puede evaluar y se aplica lo resuelto.
                Dim motivo As String = ""
                If Not MotorAplicaHeadPart(fid, raceFid, isFemale, pluginManager, flstCache, motivo) Then
                    If Not preset.SseHeadPartsFiltradasPorMotor.Contains(fid) Then preset.SseHeadPartsFiltradasPorMotor.Add(fid)
                    If Logger.Enabled Then
                        Dim srcName3 As String = System.IO.Path.GetFileName(If(preset.SourcePath, ""))
                        Dim fidF As UInteger = fid
                        Dim motivoF As String = motivo
                        Logger.LogLazy(Function() $"[LMLoad] '{srcName3}': head part 0x{fidF:X8} resolved but the engine " &
                                                  $"would not apply it to this actor ({motivoF}) -> not applied (skee64 PresetInterface.cpp:164-175).")
                    End If
                    Continue For
                End If
                If Not hp.Contains(fid) Then hp.Add(fid)
            Next
            If hp.Count > 0 Then
                preset.HeadPartFormIDs.Clear()
                preset.HeadPartFormIDs.AddRange(hp)
                preset.HasHeadPartFormIDs = True
            End If
        End If
        ' ---- FACE IDENTITY: hair color. actor.hairColor is a packed RGB (PresetInterface.cpp:677
        ' color.red<<16|green<<8|blue), NOT a CLFM FormID. skee applies it straight onto the hair shape's
        ' BSLightingShaderMaterialHairTint.tintColor (PresetInterface.cpp:112-116), taking precedence over the NPC's
        ' CLFM. We carry the packed RGB on the preset (→ state.SseHairColorRgb → ResolveHairTintColor); Nothing when
        ' the preset had no hairColor so the render falls back to the CLFM.
        preset.SseHairColorRgb = If(j.HadHairColor, CType(j.HairColor, Integer?), Nothing)
        ' ---- FACE IDENTITY: headTexture (face FTST FormID) — see SseHeadTextureFormIDOverride below (render override).
        ' Se asigna SÓLO si el identificador RESOLVIÓ. `ResolveFormIdentifier` devuelve 0 cuando el plugin dueño
        ' del TXST no está en el load order, y con el carrier tri-estado ese 0 significaría CLEAR EXPLÍCITO: un
        ' preset que referencia un TXST de un mod ausente le BORRARÍA el FTST al NPC en vez de preservarlo. Además
        ' contradiría al motor, que ante un headTexture irresoluble simplemente no aplica nada
        ' (skee64 PresetInterface.cpp:147 + GetFormFromIdentifier fallido → nullptr). No-resuelto ⇒ Nothing.
        If pluginManager IsNot Nothing AndAlso Not String.IsNullOrEmpty(j.HeadTexture) Then
            Dim ftstFid = LooksmenuLoader.ResolveFormIdentifier(j.HeadTexture, pluginManager)
            If ftstFid <> 0UI Then preset.SseHeadTextureFormIDOverride = ftstFid
        End If

        ' ---- FACE: morphs.default.morphs → NAM9 y morphs.default.presets → NAMA. El motor los escribe POSICIONALMENTE
        ' sobre el faceMorph del NPC (ApplyPresetData :182-192: `option[i] = value` / `presets[i] = value` por cada
        ' entrada del archivo, en orden) — o sea que un archivo con 5 entradas pisa los índices 0..4 y deja 5..17
        ' como estaban en el NPC. UNA ley para eso: `preset.SseNam9`/`SseNama` llevan EXACTAMENTE los índices que el
        ' estado cubre, y NpcRecordOverlay escribe sólo `Length` entradas sobre el record del NPC destino (los no
        ' cubiertos quedan como el RECORD, no ceros). Con overlay previo (clon = estado en memoria) el array nuevo
        ' mide Max(previo, cubiertos) sembrado del previo; sin overlay previo mide `cubiertos` y nada más: no se
        ' siembra ni ceros ni centinelas, porque eso sería inventar un valor donde el motor deja el del NPC.
        ' Tope 18: el slot 18 (VampireMorph) viaja aparte en SseVampireMorph. Más de 19 entradas: el motor escribe
        ' fuera del array (hueco: comportamiento indefinido); acá se descartan.
        ' NAMA tope 4 (`presets[4]`): más de 4 ⇒ mismo hueco, se descartan.
        Dim cubiertos9 As Integer = Math.Min(j.SliderMorphs.Count, SseNam9MorphMap.Nam9SliderCount)
        Dim nam9(Math.Max(cubiertos9, If(preset.SseNam9 Is Nothing, 0, preset.SseNam9.Length)) - 1) As Single
        If preset.SseNam9 IsNot Nothing Then
            For i = 0 To Math.Min(preset.SseNam9.Length, nam9.Length) - 1 : nam9(i) = preset.SseNam9(i) : Next
        End If
        For i = 0 To cubiertos9 - 1
            nam9(i) = j.SliderMorphs(i)
        Next
        preset.SseNam9 = nam9
        ' Slot 18 (VampireMorph): `option[18]` lo escribe el mismo bucle :188-192 cuando el archivo trae ≥ 19
        ' entradas. Va aparte porque no entra en los 18 sliders editables; NpcRecordOverlay lo escribe al record y
        ' ToJslot lo devuelve en vez de la constante centinela. Ver LooksmenuPreset.SseVampireMorph.
        If j.SliderMorphs.Count > SseNam9MorphMap.Nam9SliderCount Then
            preset.SseVampireMorph = j.SliderMorphs(SseNam9MorphMap.Nam9SliderCount)
        End If
        Dim cubiertosA As Integer = Math.Min(j.NamaPresets.Count, SseNam9MorphMap.NamaFamilyCount)
        Dim nama(Math.Max(cubiertosA, If(preset.SseNama Is Nothing, 0, preset.SseNama.Length)) - 1) As UInteger
        If preset.SseNama IsNot Nothing Then
            For i = 0 To Math.Min(preset.SseNama.Length, nama.Length) - 1 : nama(i) = preset.SseNama(i) : Next
        End If
        For i = 0 To cubiertosA - 1
            nama(i) = j.NamaPresets(i)
        Next
        preset.SseNama = nama
        ' :179-192 corre siempre (aloca faceMorph si falta y escribe lo que haya): el canal siempre se declara.
        preset.HasSseMorphs = True

        ' ---- FACE: sculpt ÷ divisor → world deltas. A RaceMenu preset sculpts head + brows + eyes + mouth as
        ' SEPARATE blocks, each tagged with its Host chargen tri (HDPT NAM0=2). Parse ALL blocks into
        ' SseSculptParts so render/bake route each to its shape by Host (brows/eyes/mouth were previously dropped
        ' — only Sculpt(0)=head survived, so those parts ignored the preset). SseSculptHead stays = the head block
        ' (Host base-head chargen, no "Brows") for the editor/save back-compat that reads the head-only field.
        ' INCONDICIONAL: skee64 ApplyPresetData (PresetInterface.cpp:221-226) hace `EraseSculptData(npc)` SIEMPRE y
        ' recién después `SetSculptTarget` si `sculptData.size() > 0`. Un .jslot sin sculpt DEJA al NPC sin sculpt: acá
        ' eso es una lista vacía (SseSculptHead = Nothing), que el overlay copia tal cual (NpcRecordOverlay :419-420).
        ' Dx/Dy/Dz del modelo son ENTEROS escalados por SculptDivisor (RaceMenuJslot.Load normaliza la forma float a
        ' ese entero); acá se divide para volver al delta de mundo que aplica el motor (:1095-1097 `dx / multiplier`).
        ' SculptDivisor nunca es <= 0: RaceMenuJslot.Load sólo lo asigna cuando `multiplier > 0` (:1031-1032) y
        ' arranca en 10000 (:183) — el mismo corte que el motor usa para elegir forma entera o float (:1094).
        Dim div = j.SculptDivisor
        Dim parts As New List(Of NPC_SculptPart)(j.Sculpt.Count)
        For Each blk In j.Sculpt
            Dim verts As New List(Of NPC_SculptVert)(blk.Indices.Count)
            For k = 0 To blk.Indices.Count - 1
                verts.Add(New NPC_SculptVert With {.Index = blk.Indices(k), .Dx = blk.Dx(k) / div, .Dy = blk.Dy(k) / div, .Dz = blk.Dz(k) / div})
            Next
            parts.Add(New NPC_SculptPart With {.Host = If(blk.Host, ""), .Verts = verts})
        Next
        preset.SseSculptParts = parts
        preset.SseSculptHead = SelectHeadSculptBlock(parts)

        ' ---- FACE: custom morphs → preset (EditFace_Form.vb:580-584).
        ' INCONDICIONAL: PresetInterface.cpp:228-230 hace `EraseMorphData(npc)` SIEMPRE y después `SetMorphValue` por
        ' cada entrada. Sin entradas = NPC sin custom morphs (lista vacía, que el overlay copia; NpcRecordOverlay :421).
        Dim cms As New List(Of NPC_CustomMorph)(j.CustomMorphs.Count)
        For Each cm In j.CustomMorphs : cms.Add(New NPC_CustomMorph With {.Name = cm.Name, .Value = CSng(cm.Value)}) : Next
        preset.SseCustomMorphs = cms

        ' ---- FACE: tintInfo → SseTintLayers (+ HasSseTints) and the per-layer custom mask texture map.
        ' Inverse of the pack above and of RaceMenu's apply (PresetInterface.cpp:194-205): the jslot colour's ALPHA
        ' byte IS the coverage (tintMask.alpha) → TINV; RGB → TINC (its own alpha byte is unused by the SSE face
        ' composite → 255). TIAS (preset index) is not stored in a .jslot: RaceMenu writes a FREE RGB colour (its own
        ' colour picker), which is exactly the vanilla "custom" case → TIAS = -1 (NOT 0; 0 is a valid preset TIRS the
        ' CK would resolve to a race default, re-introducing the wrong-colour bug). Verified vanilla: custom colours
        ' carry TIAS = -1. tint.texture, when non-empty, is a RaceMenu custom mask path (tintMask->texture->str =
        ' tint.name) → SseTintTexOverride[index], composited by SseFaceTintComposer instead of the RACE layer's mask.
        '
        ' LEY DEL MOTOR (PresetInterface.cpp:194-218) — leída línea por línea:
        '   :197 `if (player == actor && player->tintMasks.GetNthItem(tint.index, tintMask))` ⇒ el motor sólo toca las
        '        máscaras del JUGADOR, POR POSICIÓN y EN EL LUGAR (color+alpha; textura sólo si alpha > 0, :202-204).
        '        Las posiciones que el archivo NO trae quedan como estaban; sin entradas no se toca nada.
        '   :206-217 para CUALQUIER actor: sólo `tint.index == 0 && setSkinColor` ⇒ SetSkinFromTint (tono de piel).
        '   SavePreset (:380-392) escribe TODAS las máscaras del jugador que tengan textura ⇒ un .jslot producido por
        '   RaceMenu cubre todas las posiciones de la raza y «en el lugar por posición» ≡ reemplazo de la lista.
        ' DECISIÓN DE PRODUCTO (D-Tints SSE, no del motor), y es SOLO UNA: el motor toca las máscaras del JUGADOR
        ' (`player == actor`, :197) y acá el NPC recibe ese mismo tratamiento — aplicar tintes a un NPC es el punto
        ' de la app, RaceMenu no lo hace porque es para el jugador. `HasSseTints` sólo con entradas: el motor con 0
        ' entradas no escribe el canal, así que un .jslot sin tintInfo NO borra los tints del record.
        ' ⛔ LO QUE YA NO ES CIERTO Y ESTABA ESCRITO ACÁ: que «la lista se reemplaza entera». Se corrigió el
        ' 2026-09-04 — ahora se FUSIONA por TINI (ver el bloque de abajo), que es la ley de :197. El HUECO que este
        ' comentario declaraba (un archivo que cubre MENOS posiciones que la raza destino borraba las no cubiertas)
        ' está CERRADO, con dos casos de gate: uno sintético y otro sobre una raza divergente real del load order.
        If j.TintInfo.Count > 0 Then
            ' FUSIÓN POR TINI, no reemplazo — es la ley del motor. `player->tintMasks.GetNthItem(tint.index, ...)`
            ' (:197) escribe EN EL LUGAR sobre la máscara de esa posición: las posiciones que el archivo NO trae
            ' quedan como estaban. Antes acá se reemplazaba la lista entera, así que un archivo que cubre menos
            ' capas que la raza destino (editado a mano, o de una raza con menos capas) borraba las no cubiertas
            ' — y en el editor esas caían al DEFAULT de la RACE, no a lo que el NPC tenía.
            ' LA BASE, en orden: lo que el preset ya trae autorado (un .jslot previo, un Paste, un Edit Face
            ' confirmado) y si no, `capasBase`, que el llamador saca del record. SIN base ⇒ reemplazo, que es
            ' justo lo que necesita el mapeo "qué trae el ARCHIVO".
            Dim porTini As New Dictionary(Of Integer, LooksmenuLoader.CapaDeTinteSsePreset)
            Dim ordenTini As New List(Of Integer)
            Dim capasPrevias As List(Of LooksmenuLoader.CapaDeTinteSsePreset) = Nothing
            If preset.HasSseTints AndAlso preset.SseTintLayers IsNot Nothing Then
                capasPrevias = preset.SseTintLayers
            ElseIf capasBase IsNot Nothing Then
                capasPrevias = capasBase
            End If
            If capasPrevias IsNot Nothing Then
                For Each c In capasPrevias
                    If c Is Nothing OrElse Not c.Indice.HasValue Then Continue For
                    Dim k = CInt(c.Indice.Value)
                    If Not porTini.ContainsKey(k) Then ordenTini.Add(k)
                    porTini(k) = c
                Next
            End If
            ' El mapa de texturas se hereda igual: una máscara custom de una capa que este archivo no toca no
            ' tiene por qué perderse.
            Dim texMap As Dictionary(Of Integer, String) = Nothing
            If preset.SseTintTexOverride IsNot Nothing AndAlso preset.SseTintTexOverride.Count > 0 Then
                texMap = New Dictionary(Of Integer, String)(preset.SseTintTexOverride)
            End If
            For Each ti In j.TintInfo
                Dim a As Byte = CByte((ti.Color >> 24) And &HFFUI)   ' coverage (0..255)
                Dim r As Byte = CByte((ti.Color >> 16) And &HFFUI)
                Dim g As Byte = CByte((ti.Color >> 8) And &HFFUI)
                Dim b As Byte = CByte(ti.Color And &HFFUI)
                Dim tinv As UInteger = CUInt(Math.Round(a / 255.0 * 100.0))
                ' ti.Index is the .jslot POSITIONAL index → the record needs the RACE layer's TINI value. For most
                ' vanilla base races TINI == position so this is a no-op, but for the 71 divergent races it is the
                ' difference between binding to the right layer (incl. the position-0 skin tone that drives QNAM)
                ' and dropping the tint onto a TINI the race doesn't have.
                Dim tini As Integer = JslotIndexToTini(pluginManager, raceFid, isFemale, ti.Index)
                ' Preseleccion -1 = color propio, elegido a mano (el selector de RaceMenu).
                If Not porTini.ContainsKey(tini) Then ordenTini.Add(tini)
                porTini(tini) = New LooksmenuLoader.CapaDeTinteSsePreset With {
                    .Indice = CUShort(tini), .Rojo = r, .Verde = g, .Azul = b, .Alfa = CByte(255),
                    .Cobertura = tinv, .Preseleccion = CShort(-1)}
                If Not String.IsNullOrEmpty(ti.Texture) Then
                    If texMap Is Nothing Then texMap = New Dictionary(Of Integer, String)
                    texMap(tini) = ti.Texture
                End If
            Next
            preset.SseTintLayers = ordenTini.Select(Function(k) porTini(k)).ToList()
            preset.HasSseTints = True
            preset.SseTintTexOverride = texMap
        End If

        ' ---- BODY: actor.weight → SseWeight (NAM7).
        ' INCONDICIONAL y SIN CLAMP: PresetInterface.cpp:177 `npc->weight = presetData->weight` corre siempre;
        ' `PresetData()` arranca en `weight = 0` (:893) y el bloque `actor` (:1017-1024) sólo lo pisa si el objeto está
        ' (`weight` ausente ⇒ asFloat(null) = 0). Un .jslot sin `actor.weight` deja al NPC en peso 0 — es lo que hace el
        ' motor. El clamp 0..100 que había acá era del editor (EditBody_Form), no del motor: fuera (D-Weight SSE).
        preset.SseWeight = CSng(j.Weight)

        ' ---- BODY: bodyMorphs → flat render dict + keyed sidecar (EditBody_Form.vb:1049-1050).
        ' :281 `g_bodyMorphInterface.ClearMorphs(actor)` corre SIEMPRE — está FUERA del `if (applyType & kPresetApplyBodyMorphs)`
        ' de :283-291, que sólo condiciona los SetMorph — ⇒ el canal se declara aunque el archivo no traiga bodyMorphs:
        ' el NPC queda sin morfos de cuerpo, no con los que tenía.
        preset.BodyMorphSliders = j.BodyMorphsToFlatSliderDict()
        preset.BodyMorphsKeyed = JslotBodyMorphsToKeyed(j)
        preset.HasBodyMorphSliders = True

        ' ---- BODY: overrides → SSE body overlays (EditBody_Form.vb:1053).
        preset.SseBodyOverlays = LooksmenuLoader.CloneSseBodyOverlays(j.Overlays)

        ' ---- BODY: transforms → SSE node transforms (body-scale).
        preset.SseNodeTransforms = LooksmenuLoader.CloneSseNodeTransforms(j.NodeTransforms)
        ' Los de primera persona no se modelan pero viajan crudos, para no perder dato ajeno al re-exportar.
        preset.SseFirstPersonTransformsRaw = New List(Of String)(j.FirstPersonTransformsJson)

        ' ---- SKIN: skinOverrides → SSE skin overrides (body-paint per slot).
        preset.SseSkinOverrides = LooksmenuLoader.CloneSseSkinOverrides(j.SkinOverrides)
    End Sub

    ' =====================================================================
    ' Replicated private helpers (copied faithfully from the editors so this module is self-contained; the
    ' originals stay Private to EditFace/EditBody and are unchanged).
    ' =====================================================================

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
            ' Las contribuciones ajenas pasan al MODELO tal cual (sirven para saber quién aportó qué) y el archivo
            ' las emite tal cual: el ESCRITOR (RaceMenuJslot.Save) conserva el desglose por contribuyente.
            ' ACÁ HABÍA SEIS LÍNEAS DESCRIBIENDO UN COLAPSO EN NUESTRA KEY QUE **YA NO EXISTE** — lo probé, lo
            ' revertí en Save, y me olvidé de este comentario. No es ruido: un revisor lo leyó y reportó como defecto
            ' de producto que la app borra la autoría de los body morphs de otros mods, que es exactamente lo
            ' contrario de lo que el código hace. Un comentario que miente cuesta lo mismo que un bug.
            ' El motivo por el que el archivo NO colapsa: el motor SUMA las keys de un morph
            ' (Impl_GetBodyMorphs, BodyMorphInterface.cpp:220-240, con el default iBodyMorphMode=0), así que el
            ' desglose y el total rinden lo MISMO — y el desglose además deja los sliders de BodySlide/RaceMenu
            ' funcionando. El total bajo un nombre barrible lo necesita el ESP, no el archivo.
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
                ' ERA EL LITERAL "NPCManager", una CUARTA grafía de la misma cosa. Ahora la key sale de la
                ' constante — y el colapso de las contribuciones lo hace el escritor, no acá (ver Save).
                ' LA JUSTIFICACIÓN QUE HABÍA ESCRITO ERA FALSA: decía "con dos nombres SUMABA, el morph quedaba al
                ' doble", y no puede pasar en ese orden porque el cargador de skee hace ClearMorphs (poda total del
                ' actor, PresetInterface.cpp:281) ANTES de replayear, igual que nuestro script. La razón verdadera es
                ' la PROPIEDAD: bajo nuestra key el morph es nuestro — lo barre RemovePrevious y lo reemplaza un
                ' re-apply (skee guarda por (morph, key), BodyMorphInterface.cpp:150-154). Bajo el nombre de otro mod,
                ' no. Leer sigue andando igual: BodyMorphsToFlatSliderDict suma todas las keys sin mirar el nombre.
                entry.Keys.Add(New RaceMenuJslot.JslotBodyMorphKey With {.Key = RaceMenuJslot.AppOverrideKey, .Value = kv.Value})
                j.BodyMorphs.Add(entry)
            Next
        End If
    End Sub

    ' =====================================================================
    ' RaceMenu tint INDEX semantics — positional (.jslot) ↔ TINI value (record).
    '
    ' A .jslot tintInfo[].index is POSITIONAL: RaceMenu exports it as the loop counter over the actor's ordered
    ' tint-mask array (PresetInterface.cpp:383) and applies it with tintMasks.GetNthItem(index) (:197). The NPC_
    ' record instead references a RACE tint mask by its TINI *value*, which is NOT the position — measured across
    ' Skyrim.esm, 71 races have TINI != position (e.g. WoodElfRace female position 0 → TINI 24), and the skin-tone
    ' layer (TINP mask type 6) is always at position 0 but with a race-specific TINI. Treating the jslot index as a
    ' TINI value therefore bound the color/texture (and the index-0 skin tone that must match the body QNAM) to the
    ' wrong layer on those races. These two helpers translate at the .jslot boundary, using the SAME ordered race
    ' layer list the game builds (SseFaceTintComposer.GetRaceLayersOrdered = RACE subrecord order, gender-filtered).
    ' =====================================================================

    ''' <summary>.jslot positional index → RACE tint-mask TINI value for this race+gender. Returns the index
    ''' unchanged when the race is unknown or the position is out of range (identity fallback = old behaviour).</summary>
    Private Function JslotIndexToTini(pm As PluginManager, raceFid As UInteger, isFemale As Boolean, jslotIndex As Integer) As Integer
        If pm Is Nothing OrElse raceFid = 0UI OrElse jslotIndex < 0 Then Return jslotIndex
        Dim layers = SseFaceTintComposer.GetRaceLayersOrdered(pm, raceFid, isFemale)
        If layers Is Nothing OrElse jslotIndex >= layers.Count Then Return jslotIndex
        Return layers(jslotIndex).Index
    End Function

    ''' <summary>RACE tint-mask TINI value → .jslot positional index for this race+gender (inverse of
    ''' <see cref="JslotIndexToTini"/>). Returns the TINI unchanged when the race is unknown or no layer carries
    ''' that TINI (identity fallback).</summary>
    Private Function TiniToJslotIndex(pm As PluginManager, raceFid As UInteger, isFemale As Boolean, tini As Integer) As Integer
        If pm Is Nothing OrElse raceFid = 0UI Then Return tini
        Dim layers = SseFaceTintComposer.GetRaceLayersOrdered(pm, raceFid, isFemale)
        If layers Is Nothing Then Return tini
        For pos = 0 To layers.Count - 1
            If layers(pos).Index = tini Then Return pos
        Next
        Return tini
    End Function

    ''' <summary>La máscara (TINT) que la RACE declara para esa capa. "" si no se puede resolver.
    ''' <para>⛔ Hace falta porque RaceMenu, al aplicar un preset, PISA la textura de la capa con lo que
    ''' venga en el archivo — pero sólo cuando la capa es opaca:
    ''' <code>if (tintMask->alpha > 0) tintMask->texture->str = tint.name;</code>
    ''' (<c>PresetInterface.cpp:202-203</c>). Así que emitir una cadena VACÍA en una capa con alpha &gt; 0
    ''' le BORRA la máscara al jugador; con alpha 0 es inerte porque la asignación no corre.</para>
    ''' <para>El canónico escribe siempre la textura EFECTIVA de la capa
    ''' (<c>tint["texture"] = tmIt-&gt;second.second</c>, <c>:426</c>), no una vacía, así que un
    ''' guardar-cargar suyo no pierde nada. MEDIDO sobre los 48 .jslot instalados: 48 entradas con
    ''' textura vacía, TODAS en los 4 archivos que escribió esta app, y <b>20 con alpha &gt; 0</b> — que
    ''' son las que hacen daño.</para></summary>
    Private Function TexturaDeLaCapaDeRaza(pm As PluginManager, raceFid As UInteger, isFemale As Boolean, tini As Integer) As String
        If pm Is Nothing OrElse raceFid = 0UI Then Return ""
        Dim layers = SseFaceTintComposer.GetRaceLayersOrdered(pm, raceFid, isFemale)
        If layers Is Nothing Then Return ""
        For pos = 0 To layers.Count - 1
            If layers(pos).Index = tini Then Return If(layers(pos).Path, "")
        Next
        Return ""
    End Function

End Module
