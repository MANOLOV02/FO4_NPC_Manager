Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>
''' Reusable NPC record-parsing and LooksMenu preset overlay helpers. App-specific
''' (NPC_Manager only — Wardrobe_Manager has no NPC concept), so they live in this app's
''' NPC/ folder, not in FO4_Base_Library.
'''
''' Single-source-of-truth for the orchestration "fetch NPC record → parse → apply overlay".
''' Both the render path (NpcRenderContext.GetParsedNpc / MainForm.ApplyPresetOverlayToNpcData) and
''' the offline bake (FaceGenBuilder) consume this module so the two views never drift.
''' </summary>
Public Module NpcRecordOverlay

    ''' <summary>Parse the NPC record at the given FormID into NPC_Data. Returns Nothing if
    ''' the record is missing or has the wrong signature. The pluginManager is the single
    ''' source of records — no static state.</summary>
    Public Function GetParsedNpc(formID As UInteger, pluginManager As PluginManager) As NPC_Data
        Dim rec = pluginManager.GetRecord(formID)
        If rec Is Nothing OrElse rec.Header.Signature <> "NPC_" Then Return Nothing
        Dim pluginName = If(rec.SourcePluginName <> "", rec.SourcePluginName, "Unknown")
        Return RecordParsers.ParseNPC(rec, pluginManager)
    End Function

    ''' <summary>Hook opcional: fid → raza EFECTIVA (el <c>NpcRecordOverride.RaceFormID</c> del editor), 0 = sin
    ''' override. Lo setea MainForm al iniciar (mirror de <c>NpcMorphResolver.SliderCatalog</c>); la CLI y los
    ''' probes nunca lo setean → no-op. Consumido por <see cref="ResolveOverlaidNpcData"/> para que el BAKE vea la
    ''' misma raza que el render (state.RaceFormID) — sin esto, un cambio de raza en el editor bakeaba FaceTint/
    ''' FaceGen con el catálogo de la raza VIEJA (npcData crudo) mientras el render usaba la nueva (render==bake).</summary>
    Public Property EffectiveRaceResolver As Func(Of UInteger, UInteger)

    ''' <summary>Convenience composition of <see cref="GetParsedNpc"/> + <see cref="ApplyPresetOverlayToNpcData"/>:
    ''' fetch+parse the NPC record and apply the LooksMenu preset overlay in one call. Returns Nothing if the
    ''' NPC record doesn't resolve. Single source of truth for the FaceGen bake paths (FaceGenBuilder.BuildCharGen /
    ''' .BakeFaceTextures, FaceGenBuildPipeline.BuildBakeState) which all needed the same two-step sequence.</summary>
    Public Function ResolveOverlaidNpcData(npcFormID As UInteger,
                                           pluginManager As PluginManager,
                                           appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                           Optional lmSkinTemplateResolver As ResolveLmSkinTemplateDelegate = Nothing) As NPC_Data
        Dim raw = GetParsedNpc(npcFormID, pluginManager)
        If raw Is Nothing Then Return Nothing
        Dim result = ApplyPresetOverlayToNpcData(raw, npcFormID, appliedPresets, pluginManager, lmSkinTemplateResolver)
        ' Raza efectiva del editor (ver EffectiveRaceResolver). Mutar acá es seguro: `raw` es un parse FRESCO
        ' (GetParsedNpc no cachea) y el shadow del overlay también — nunca es la instancia cacheada del ctx.
        Dim effResolver = EffectiveRaceResolver
        If result IsNot Nothing AndAlso effResolver IsNot Nothing Then
            Dim eff = effResolver(npcFormID)
            If eff <> 0UI AndAlso eff <> result.Record.Race Then
                result.Record.Race = eff
            End If
        End If
        Return result
    End Function

    ''' <summary>Dos listas de identificadores con el mismo contenido y en el mismo orden.</summary>
    Private Function MismaLista(a As List(Of UInteger), b As List(Of UInteger)) As Boolean
        If a Is Nothing OrElse b Is Nothing Then Return a Is b
        If a.Count <> b.Count Then Return False
        For i = 0 To a.Count - 1
            If a(i) <> b(i) Then Return False
        Next
        Return True
    End Function

    ''' <summary>Resolve an LM SkinTemplate id to its full bundle. Returns Nothing if the id
    ''' isn't loaded. Optional injection so the offline bake path (FaceGenBuilder) can opt out —
    ''' F4SE skin overrides are runtime only and don't apply to baked CharGen output.</summary>
    Public Delegate Function ResolveLmSkinTemplateDelegate(templateId As String) As LmSkinTemplate

    ''' <summary>HDPT.PartType enum values, as defined by the record's own schema. These
    ''' are the values the parser surfaces in Canon.IHdpt.PartType and that the renderer reads via
    ''' state.HeadPartFormIDs lookups — NOT the F4SE runtime BGSHeadPart::Type enum (which uses
    ''' different numbering). Used by ApplyLmHdptReplacement and by MainForm's overlay merge.</summary>
    Public Const HdptPartType_Misc As Byte = 0
    Public Const HdptPartType_Face As Byte = 1
    Public Const HdptPartType_Eyes As Byte = 2
    Public Const HdptPartType_Hair As Byte = 3
    Public Const HdptPartType_FacialHair As Byte = 4
    Public Const HdptPartType_Scar As Byte = 5
    Public Const HdptPartType_Eyebrows As Byte = 6
    Public Const HdptPartType_Meatcaps As Byte = 7
    Public Const HdptPartType_Teeth As Byte = 8
    Public Const HdptPartType_HeadRear As Byte = 9

    ''' <summary>Public wrapper over ApplyLmHdptReplacement so MainForm's overlay merge can call
    ''' the same helper the shadow uses, ensuring identical replacement semantics across both
    ''' code paths. PartType is read from the new HDPT itself (engine-faithful per
    ''' SkinInterface.cpp:292), so callers don't pass a target — the helper figures it out.</summary>
    Public Sub ApplyLmHdptReplacementPublic(headParts As List(Of UInteger), newHdptFormID As UInteger,
                                              pluginManager As PluginManager)
        ApplyLmHdptReplacement(headParts, newHdptFormID, pluginManager)
    End Sub

    ''' <summary>Si hay un preset de LooksMenu aplicado a <paramref name="selectedNpcFormID"/>, devuelve una
    ''' copia de <paramref name="raw"/> con los campos de morph y face-tint del preset pisados. Sin preset,
    ''' devuelve <paramref name="raw"/> tal cual.
    ''' <para>El overlay se indexa por el NPC que el usuario SELECCIONÓ, no por el origen de su plantilla:
    ''' así un preset aplicado a un NPC no se filtra a otros que compartan su cadena de templates.</para>
    ''' <para>La sombra arranca como una COPIA DEL RECORD: todo lo que el preset no pisa queda tal cual
    ''' estaba, incluidos los campos que ningún editor toca. Antes se armaba campo por campo, y el que
    ''' alguien se olvidaba de copiar se perdía al guardar — media docena de bugs medidos salieron de
    ''' ahí, y cada uno se tapó agregando una línea más a la lista.</para>
    ''' <para>LA LEY (f4ee <c>CharGenInterface.cpp</c> LoadPreset :269-645, un try/catch por canal): la sombra
    ''' es el estado del motor DESPUÉS de LoadPreset. Cada <c>Has*</c> del preset dice «el motor escribiría este
    ''' canal»; con <c>Has*</c> el canal se REEMPLAZA con lo que el preset trae (aunque sea vacío: el motor hace
    ''' <c>Clear()</c> y recién después <c>Add</c>), y sin <c>Has*</c> (el motor lanzó y el catch lo dejó como
    ''' estaba) se PRESERVA el record. Nada se siembra del record dentro de un canal que el preset reemplaza.
    ''' Los <c>Has*</c> los decide <c>LooksmenuLoader.ParseFile</c> con la tabla de jsoncpp, no esta función.</para>
    ''' <para>Tints: además del <c>Has</c>, el motor valida cada capa contra la RAZA (:512-527) — índice sin
    ''' plantilla ⇒ capa salteada; ColorID sin color en la plantilla ⇒ el primero de la lista; y una conversión
    ''' que lanza en una capa validada vacía el canal entero (:563). Eso lo hace <c>ValidarCapasDeTinteContraLaRaza</c>
    ''' UNA vez al cargar el .json (MainForm.PreviewLooksmenuOverlay), no acá: esta función también recibe capas
    ''' sembradas del record, que en el motor no pasan por LoadPreset.</para>
    ''' <para>FMIN: <c>Morphs.Intensity</c> ausente ⇒ 1.0 es MOTOR (:466-467, para todo actor que no sea el jugador
    ''' :462); la decisión de PRODUCTO es no crear el subrecord cuando vale 1.0 y el record no lo tenía.</para></summary>
    ''' <param name="parseRace">Optional cached RACE parser (NpcRenderContext.ParseRaceCanonCached). When Nothing,
    ''' falls back to a direct <c>Canon.CanonRecords.Race</c> — keeps the offline bake path pure.</param>
    Public Function ApplyPresetOverlayToNpcData(raw As NPC_Data,
                                                selectedNpcFormID As UInteger,
                                                appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                                pluginManager As PluginManager,
                                                Optional lmSkinTemplateResolver As ResolveLmSkinTemplateDelegate = Nothing,
                                                Optional parseRace As Func(Of PluginRecord, Canon.IRace) = Nothing,
                                                Optional estricto As Boolean = False) As NPC_Data
        If raw Is Nothing OrElse raw.Record Is Nothing Then Return raw
        If appliedPresets Is Nothing Then Return raw
        Dim preset As LooksmenuLoader.LooksmenuPreset = Nothing
        If Not appliedPresets.TryGetValue(selectedNpcFormID, preset) Then Return raw

        ' Raza EFECTIVA (record override del editor, ver EffectiveRaceResolver): la sombra la lleva desde el
        ' arranque, y TODO lo que esta función deriva de la raza (seed de head-parts, QNAM del skin-tone,
        ' catálogo de tints) usa la efectiva — sin esto, un NPC con cambio de raza + preset LM sembraba
        ' head-parts/QNAM del catálogo de la raza VIEJA. El camino sin preset devuelve `raw` intacto (arriba):
        ' esa instancia puede ser la cacheada del ctx y NO se muta — los callers que necesitan la raza
        ' efectiva en ese caso la stampan sobre su copia fresca (ResolveOverlaidNpcData, FaceTintLayerBuilder).
        Dim effRaceFid As UInteger = raw.Record.Race
        Dim effResolver = EffectiveRaceResolver
        If effResolver IsNot Nothing Then
            Dim eff = effResolver(selectedNpcFormID)
            If eff <> 0UI Then effRaceFid = eff
        End If

        Dim shadow As New NPC_Data With {
            .Record = raw.Record.Copia(),
            .FormID = raw.FormID,
            .EditorID = raw.EditorID,
            .Game = raw.Game,
            .PluginName = raw.PluginName
        }
        Dim sr = shadow.Record
        If effRaceFid <> 0UI Then sr.Race = effRaceFid

        ' La RACE se parsea UNA vez y acá arriba: la necesitan el WNAM (abajo), el seed de head-parts y
        ' la derivación del QNAM. Antes se parseaba recién en la mitad de la función, y por eso el WNAM
        ' no podía resolver nada y se conformaba con dejar un 0.
        Dim raceRec = If(effRaceFid <> 0UI, pluginManager.GetRecord(effRaceFid), Nothing)
        Dim raceIsValid As Boolean = raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE"
        Dim race As Canon.IRace = Nothing
        If raceIsValid Then
            race = If(parseRace IsNot Nothing, parseRace(raceRec), Canon.CanonRecords.Race(raceRec, pluginManager))
        End If

        ' NPC.WNAM (piel → ARMO). Tres estados del override, y NINGUNO escribe un 0 al archivo:
        '   Nothing    → no se toca el WNAM del record
        '   valor <> 0 → se escribe ese ARMO
        '   valor = 0  → el combo dice «(use RACE default)» ⇒ se resuelve contra la RAZA (abajo).
        '
        ' LA RAZA TIENE TRES ESTADOS, y los tres producen algo DISTINTO y VISIBLE:
        '   declara piel     → se escribe ESA piel, explícita en el archivo.
        '   NO declara piel  → no hay nada que nombrar ⇒ el subrecord NO VA. Es el dato correcto:
        '                      la raza no tiene piel, así que el NPC tampoco.
        '   NO SE PUDO resolver → SE RECHAZA EL GUARDADO. No es un estado del dato: es que falta
        '                      información. Sacar el subrecord «porque el motor igual cae a la raza»
        '                      parece inocuo y NO lo es: dejaría que el MISMO clic produzca dos
        '                      productos según si el mod de la raza estaba cargado al guardar —uno
        '                      CLAVADO a la piel de hoy y otro que sigue a la raza para siempre—, y
        '                      eso lo elegiría el orden de carga, en silencio. Los bytes los decide
        '                      el usuario, y para decidir tiene que enterarse.
        '
        ' ⛔ Antes esto grababa `sr.Skin = 0`, o sea un WNAM presente apuntando a nada. Eso no es un
        ' tercer estado del formato: xEdit declara NPC_\WNAM como [ARMO] SIN NULL en los dos juegos, así
        ' que su validador lo marca «Found a NULL reference, expected: ARMO» y ni siquiera deja
        ' escribirlo a mano — a diferencia de RACE\WNAM, que sí es [ARMO, NULL]. Y no existe en el
        ' ecosistema: medido sobre 175 plugins y 3.482.830 records, CERO referencias nulas en
        ' NPC_/ARMA/ARMO/RACE/HDPT.
        ' La ley de cuál es la piel de una raza es la MISMA que usa el render (CanonInterpretacion.SkinDe),
        ' no una copia.
        If preset.SkinFormIDOverride.HasValue Then
            Dim pielPedida = preset.SkinFormIDOverride.Value
            If pielPedida = 0UI Then
                ' Falta de información, no un estado del dato. Y el rechazo va SÓLO en el camino de
                ' GUARDADO: esta función la comparten el render y el horneado, y ahí tirar cerraba la
                ' app al seleccionar un NPC — `MainForm.LoadNPCOnDemandAsyncFromExisting` la llama en el
                ' hilo de UI, sin Try, y la app corre con UnhandledExceptionMode.ThrowException. En
                ' render/bake se deja el WNAM como está y el resolvedor cae al fallback de siempre: el
                ' preview muestra lo que el record dice HOY, y el guardado se niega a adivinar. No hay
                ' divergencia de producto, porque en ese estado el guardado no produce nada.
                If race Is Nothing Then
                    If estricto Then
                        Throw New InvalidOperationException(
                            $"NPC {raw.FormID:X8} ({raw.EditorID}): se pidió usar la piel de la raza, pero " &
                            $"la raza {effRaceFid:X8} no se pudo resolver — falta su plugin en el orden de " &
                            "carga, o el record al que apunta ya no existe. No se graba: elegir por el " &
                            "usuario cuál de las dos representaciones posibles queda en el archivo sería " &
                            "decidir sus bytes.")
                    End If
                    GoTo finDelSkin
                End If
                pielPedida = Canon.CanonInterpretacion.SkinDe(race)
            End If
            If pielPedida <> 0UI Then
                sr.Skin = pielPedida
            Else
                sr.QuitarSubrecord("WNAM")
            End If
finDelSkin:
        End If

        ' LM SkinTemplate (F4SE bundle) wins over NPC.WNAM at preview time, mirroring
        ' SkinInterface.cpp:250-332 in Script extenders, Racemenu y Looksmenu/F4SEPlugins/f4ee — ApplyOverride applies the
        ' template's `skin` ARMO + face[gender] TXST + head[gender] HDPT + rear[gender] HDPT.
        ' Skin and face TXST are applied here; head / headRear HDPT replacement is applied below
        ' after the preset HeadParts merge so the bundle sits on top of preset overrides.
        Dim lmTemplate As LmSkinTemplate = Nothing
        If Not String.IsNullOrEmpty(preset.SkinTemplateId) AndAlso lmSkinTemplateResolver IsNot Nothing Then
            lmTemplate = lmSkinTemplateResolver(preset.SkinTemplateId)
            If lmTemplate IsNot Nothing AndAlso lmTemplate.SkinArmoFormID <> 0UI Then
                sr.Skin = lmTemplate.SkinArmoFormID
            End If
        End If

        Dim isFemale As Boolean = raw.Record.ConfigurationFlagsFemale

        ' NPC.DOFT (atuendo por defecto → OTFT). Tres estados, igual que WNAM: sin override se preserva;
        ' con valor se escribe; con cero se SACA el subrecord, que es lo que significa "sin atuendo" —
        ' escribir DOFT=0 no es lo mismo y el motor lo leería como una referencia rota.
        If preset.DefaultOutfitFormIDOverride.HasValue Then
            If preset.DefaultOutfitFormIDOverride.Value <> 0UI Then
                sr.DefaultOutfit = preset.DefaultOutfitFormIDOverride.Value
            Else
                sr.QuitarSubrecord("DOFT")
            End If
        End If
        ' NPC.SOFT (atuendo de dormir → OTFT). Mismos tres estados que DOFT.
        If preset.SleepOutfitFormIDOverride.HasValue Then
            If preset.SleepOutfitFormIDOverride.Value <> 0UI Then
                sr.SleepingOutfit = preset.SleepOutfitFormIDOverride.Value
            Else
                sr.QuitarSubrecord("SOFT")
            End If
        End If

        ' FTST (face TXST). PRECEDENCIA, de mayor a menor: plantilla LM > headTexture del preset > el
        ' del record.
        '   1) LM SkinTemplate face[gender] (bundle de F4SE, SkinInterface.cpp:307-313 — ApplyOverride
        '      setea npc->headData->faceTextures). Gana porque es la misma ley que YA aplican sus campos
        '      hermanos del mismo bundle acá: WNAM (pisa el override del preset) y head/headRear HDPT
        '      (más abajo, "post-merge override so the bundle sits on top of preset overrides"). Y sobre
        '      todo: es la del camino de RENDER en NpcStateResolver.vb:160-172, donde el template se
        '      aplica DESPUÉS del preset y gana. Alinear el guardado con el render es lo que mantiene
        '      el WYSIWYG.
        '   2) SseHeadTextureFormIDOverride del preset — TRES estados: Nothing = el .jslot no trae
        '      `headTexture` y el editor no lo tocó → no participa; <> 0 = override explícito (RaceMenu
        '      skee64 PresetInterface.cpp:158-160, o el picker de Edit Face); = 0 = BORRADO EXPLÍCITO →
        '      no se emite FTST y la cara cae al DefaultFaceTexture de la RAZA / HDPT.TNAM.
        ' El borrado tiene que poder GANARLE al record: por eso el carrier es `UInteger?` y no un
        '      `UInteger` donde 0 significaba a la vez "sin override" y "ninguno" — con el tipo plano,
        '      elegir "(none)" en el picker era indistinguible de no tocar nada.
        '   3) El FTST del record.
        ' UNA SOLA resolución de FTST en toda esta función. Antes había DOS asignaciones y la segunda,
        ' con el override vacío (el caso normal), REVERTÍA el TXST de la plantilla LM.
        Dim headTxstOverride As UInteger? = Nothing
        If lmTemplate IsNot Nothing Then
            Dim genderIdx As Integer = If(isFemale, 1, 0)
            If lmTemplate.FaceTxstFormID(genderIdx) <> 0UI Then headTxstOverride = lmTemplate.FaceTxstFormID(genderIdx)
        End If
        If Not headTxstOverride.HasValue AndAlso preset.SseHeadTextureFormIDOverride.HasValue Then
            headTxstOverride = preset.SseHeadTextureFormIDOverride
        End If
        If headTxstOverride.HasValue Then
            If headTxstOverride.Value <> 0UI Then
                sr.HeadTexture = headTxstOverride.Value
            Else
                sr.QuitarSubrecord("FTST")
            End If
        End If

        ' "Is CharGen Face Preset". El editor lo prende o lo apaga para que quede en el plugin; el
        ' render no lo mira, así que no tiene efecto visual.
        ' Por la propiedad del canon y NO por el bit a mano: el número y su máscara estaban copiados en
        ' SEIS lugares de la app, y el que se equivoque de bit no lo dice nadie.
        If preset.IsCharGenFacePreset.HasValue Then
            sr.ConfigurationFlagsIsCharGenFacePreset = preset.IsCharGenFacePreset.Value
        End If

        ' HeadParts: replicate engine wipe + race defaults + preset overrides — PERO SÓLO SI EL PRESET LOS TRAE.
        ' ⚠ DIVERGENCIA CONOCIDA, PENDIENTE DE DECISIÓN DEL USUARIO (D-HeadParts): los dos motores limpian SIEMPRE
        ' (f4ee CharGenInterface.cpp:318-331 `headParts.Clear()` + defaults de raza; skee64 PresetInterface.cpp:131-175
        ' incondicional) y el preset REEMPLAZA por tipo; acá «Has con lista vacía ⇒ record» y «clave ausente ⇒ record».
        ' Hasta esa decisión NO se toca: es la única rama de este Sub que no sigue la ley «Has* = el motor escribe».
        ' Parse RACE ONCE here (cached via parseRace when supplied by the render path; direct parse
        ' on the offline bake path) and reuse for both the HeadParts seed and the QNAM derivation below.
        ' RAZA EFECTIVA (effRaceFid, no la del record): el seed de head-parts y el QNAM salen del catálogo
        ' de la raza que realmente se muestra/hornea.
        ' La RACE ya se parseó arriba (hace falta para el WNAM). Acá sólo se reusa.

        ' EL WIPE SE GATEA POR PRESENCIA (HasHeadPartFormIDs), igual que morphs, tints y weight, Y NO SE
        ' PREPENDE NADA. Las dos mitades importan y las dos vienen de bugs medidos, en FO4 y en SSE:
        '   · Sin el gate: al abrir la app se siembra un preset SINTETICO y VACIO por cada NPC con entrada en el
        '     sidecar, y el wipe corria igual, dejando la sombra con los DEFAULTS DE LA RAZA. Abrir y hornear sin
        '     tocar nada horneaba la cara con el pelo, cejas y ojos de la RAZA en vez de los del NPC.
        '   · Sin sacar el prepend: sembrar los defaults de raza DELANTE de los del preset invierte la
        '     precedencia aguas abajo, porque MergeHeadPartsWithRaceDefaults trata su entrada como el PNAM del
        '     NPC y aplica PRIMERO-GANA por PartType. El prepend no era redundante, era ACTIVAMENTE dañino: el
        '     NIF horneado contradecia al PNAM del ESP y al render, y eso da CARA MARRON en juego.
        ' REGLA UNICA (la del render): la posesion exige CONTENIDO y el preset REEMPLAZA. El relleno por PartType
        ' lo hace el UNICO merge de aguas abajo, que ya siembra la raza en su primer paso, y los dos consumidores
        ' de la sombra pasan por ese merge, asi que nadie pierde los defaults por sacar el prepend.
        Dim headParts As List(Of UInteger)
        If preset.HasHeadPartFormIDs AndAlso preset.HeadPartFormIDs.Count > 0 Then
            headParts = New List(Of UInteger)(preset.HeadPartFormIDs)
        Else
            ' El preset NO toma posesión — o la reclama con la lista VACÍA, que antes caía en la rama de arriba y
            ' dejaba la sombra con SÓLO defaults de raza ⇒ se preservan los head parts del NPC tal cual (lo que ve
            ' el editor y lo que el render debe dibujar). MergeHeadPartsWithRaceDefaults, aguas abajo, ya rellena
            ' los huecos por PartType con los defaults de la raza — los faltantes siguen cubiertos, sin pisar los
            ' propios. (Una lista vacía CON bandera nunca significó "cabeza pelada": daba defaults de raza.)
            Dim propias = raw.Record.PartesDeCabeza()
            If preset.HasHeadPartFormIDs Then
                ' Estado ANÓMALO: alguien reclamó posesión sin contenido. Ya no rompe (cae acá y manda el record),
                ' pero es un fallo silencioso de algún productor de la bandera — Edit Face la enciende
                ' incondicionalmente (EditFace_Form:2119) aunque su seed no haya poblado nada, y hay 4 productores
                ' más (Load LM, Paste, RaceMenuPresetMapper, PresetCategoryFilter). Se loguea para poder ubicarlo
                ' si aparece, en vez de que vuelva a manifestarse como una cara marrón in-game.
                Dim cuantas = propias.Count
                Logger.LogLazy(Function() $"[HEADPARTS] npc=0x{selectedNpcFormID:X8} HasHeadPartFormIDs=True con lista " &
                                          $"VACIA => se preservan los {cuantas} del record (fallback)")
            End If
            headParts = propias
        End If

        ' LM SkinTemplate head / headRear: replace the per-PartType HDPT entry. Mirrors
        ' SkinInterface.cpp:289-303 — npc->ChangeHeadPart(template.head/rear, false, false)
        ' which swaps the existing Face / HeadRear part for the template's. We do it here as a
        ' post-merge override so that the resulting list is "race defaults + preset overrides
        ' + LM bundle". HDPT.Type: 0=Misc, 1=Face, 2=Eyes, 3=Hair, 4=FacialHair, 5=Scar,
        ' 6=Eyebrows, 7=Meatcaps, 8=Teeth, 9=HeadRear.
        ' (Ojo: el enum de C++ de F4SE usa otra numeración — kTypeFace=0, kTypeHeadRear=2 — porque es el
        ' BGSHeadPart::Type de runtime, NO el PartType del record. El parser expone el del record en
        ' Canon.IHdpt.PartType, que es el que se usa acá.)
        If lmTemplate IsNot Nothing Then
            Dim genderIdx As Integer = If(isFemale, 1, 0)
            ' The helper reads each HDPT's own PartType to decide which slot to replace,
            ' so a JSON template that puts (e.g.) a Hair HDPT in "maleHead" replaces the
            ' Hair slot, not Face. Engine-faithful per SkinInterface.cpp:292.
            ApplyLmHdptReplacement(headParts, lmTemplate.HeadHdptFormID(genderIdx), pluginManager)
            ApplyLmHdptReplacement(headParts, lmTemplate.HeadRearHdptFormID(genderIdx), pluginManager)
        End If
        ' Solo si la lista cambio: reescribirla igual daria los mismos bytes, pero un PNAM en cero -que la
        ' lectura filtra- se perderia al pasar por aca sin que nadie lo haya pedido.
        If Not MismaLista(headParts, raw.Record.PartesDeCabeza()) Then sr.PonerPartesDeCabeza(headParts)

        ' HairColor: el 0 del preset significa "no viene en el JSON, preservar" (es lo que hace el motor:
        ' un form nulo saltea la asignación). Escribirlo CREA el HCLF si el record no lo traía, que es
        ' justo lo que hace falta: sin eso, elegirle un color a un NPC sin HCLF propio se veía en el
        ' preview y desaparecía al guardar.
        If preset.HairColorFormID <> 0UI Then sr.HairColor = preset.HairColorFormID
        ' SSE RaceMenu absolute hair tint (.jslot actor.hairColor). Sidecar-only data — the record has no
        ' field for it — so viaja en la sombra para que el bake y el render resuelvan el MISMO color. Save ESP
        ' lo materializa en un CLFM real (NpcOverrideSaver.MaterializeSseHairColors). Nothing en FO4.
        shadow.SseHairColorRgb = preset.SseHairColorRgb

        ' Weight (CharGenInterface.cpp:476-484): `root["Weight"][i].asFloat()` slot por slot, en orden. Un slot
        ' `Nothing` es un slot en el que el motor LANZÓ (y con él los siguientes: el catch corta el bloque) ⇒ se
        ' preserva el record. El «ausente ⇒ 0,0,0» ya viene resuelto de ParseFile (null ⇒ asFloat 0).
        If preset.WeightThin.HasValue Then sr.PonerPesoDelCuerpo(0, preset.WeightThin)
        If preset.WeightMuscular.HasValue Then sr.PonerPesoDelCuerpo(1, preset.WeightMuscular)
        If preset.WeightFat.HasValue Then sr.PonerPesoDelCuerpo(2, preset.WeightFat)

        ' Morphs.Presets (MSDK/MSDV chargen vertex morphs) — CharGenInterface.cpp:433-460: `morphSetData->Clear()`
        ' (:443) y después `Add` por clave ⇒ REEMPLAZO, nunca fusión con el record. Ésta era la causa de la
        ' «boca anormal» reportada en Nexus: la app sembraba los morfos del record y pisaba encima, y un preset
        ' que no traía la clave del record la dejaba viva. Con `Presets` vacío o null el motor deja el set vacío
        ' (o no lo crea si nunca existió, :437 — mismo resultado: sin MSDK).
        If preset.HasChargenFaceMorphs Then sr.PonerMorfosDeCara(preset.ChargenFaceMorphs)

        ' SSE (Skyrim) head morphs (NAM9 19 floats / NAMA 4 type uints). skee64 ApplyPresetData :179-192 escribe
        ' POSICIONALMENTE `option[i]` / `presets[i]` por cada entrada del archivo: los índices que el preset no cubre
        ' quedan como estaban en el record. `preset.SseNam9`/`SseNama` miden EXACTAMENTE lo cubierto (el mapper y
        ' BuildPresetFromState los dimensionan así) y acá se escriben sólo `Length` entradas.
        ' Record sin NAM9: el motor aloca `faceMorph` con Heap_Allocate (:179-180) sin inicializar — HUECO: la app
        ' escribe los 19 en 0, la misma ley que ya tenía este bloque.
        If preset.HasSseMorphs AndAlso preset.SseNam9 IsNot Nothing Then
            Dim n9 = raw.Record.DeslizadoresDeCara()
            If n9 Is Nothing Then n9 = New Single(SseNam9MorphMap.Nam9SliderCount) {}
            For i = 0 To Math.Min(preset.SseNam9.Length, Math.Min(n9.Length, SseNam9MorphMap.Nam9SliderCount)) - 1
                n9(i) = preset.SseNam9(i)
            Next
            ' Slot 18 (VampireMorph): `option[18]` lo escribe el mismo bucle :188-192 cuando el archivo trae 19
            ' entradas; el mapper lo deja en SseVampireMorph (HasValue = el archivo lo trajo, o es el del propio NPC).
            If preset.SseVampireMorph.HasValue AndAlso n9.Length > SseNam9MorphMap.Nam9SliderCount Then
                n9(SseNam9MorphMap.Nam9SliderCount) = preset.SseVampireMorph.Value
            End If
            sr.PonerDeslizadoresDeCara(n9)
        End If
        If preset.HasSseMorphs AndAlso preset.SseNama IsNot Nothing Then
            ' El relleno es el CENTINELA, no cero: son dos valores distintos y los dos son legitimos.
            ' Con el centinela el motor no aplica nada; con cero aplica la variante "Default" con peso
            ' entero, asi que rellenar con ceros le CAMBIA LA CARA a un NPC que no traia el subrecord.
            ' El vector sale del helper compartido: era la cuarta rama que lo armaba por su cuenta.
            Dim na = raw.Record.PartesDeCara()
            If na Is Nothing Then na = SseNam9MorphMap.DefaultNamaVector()
            For f = 0 To Math.Min(preset.SseNama.Length, Math.Min(na.Length, SseNam9MorphMap.NamaFamilyCount)) - 1
                na(f) = preset.SseNama(f)
            Next
            sr.PonerPartesDeCara(na)
        End If
        ' SSE tint RArrayS (TINI/TINC/TINV/TIAS) — la edición de Face Tints reemplaza la lista entera;
        ' sin edición queda la del record.
        If preset.HasSseTints AndAlso preset.SseTintLayers IsNot Nothing Then
            LooksmenuLoader.EscribirCapasDeTinteSse(sr, preset.SseTintLayers)
        End If
        ' RaceMenu-only per-layer custom tint mask texture (index → path). No ESP home (TINI/TINC/TINV/TIAS carry
        ' no path) → carried on the shadow so the composer (render + bake) composites the custom mask instead of
        ' the RACE layer's default for that index. Absent = the RACE layer's own mask.
        If preset.SseTintTexOverride IsNot Nothing AndAlso preset.SseTintTexOverride.Count > 0 Then
            shadow.SseTintTexOverride = preset.SseTintTexOverride
        End If
        ' SSE (Skyrim) vanilla body weight (NPC.NAM7). Sólo se pisa con el valor editado en Edit Body; en
        ' Fallout 4 el preset nunca lo trae y el subrecord queda como estaba.
        If preset.SseWeight.HasValue Then sr.PonerPesoDeSkyrim(preset.SseWeight.Value)

        ' RaceMenu .jslot sculpt + custom morphs — sidecar-only (el record no tiene dónde). Vienen del preset
        ' para que el resolver de morfos de SSE (BuildFaceMorphPlanFromNam9) los aplique en render y bake.
        shadow.SseSculptHead = preset.SseSculptHead
        shadow.SseSculptParts = preset.SseSculptParts
        shadow.SseCustomMorphs = preset.SseCustomMorphs

        ' Morphs.Values (MRSV) — CharGenInterface.cpp:373-397: el array se CREA sólo si el JSON trae algún valor
        ' (:380-381) y, si existe, se limpia y se reasigna con `Allocate(5)` (:383-384) poniendo los que vinieron
        ' (:387-388). Con lista vacía sobre un record sin MRSV no se crea nada; sobre uno con MRSV queda el
        ' subrecord con los 5 slots «asignados por Allocate» — HUECO: `Heap_Allocate` no inicializa
        ' (GameTypes.h:275-287); la app escribe 0, que es la ley que ya tenía `PonerValoresDeRegionCorporal`.
        If preset.HasBodyMorphValues AndAlso (raw.Record.ValoresDeRegionCorporal().Count > 0 OrElse preset.BodyMorphValues.Count > 0) Then
            sr.PonerValoresDeRegionCorporal(preset.BodyMorphValues)
        End If

        ' Morphs.Regions (FMRI/FMRS) — :399-429: mismo patrón que MSDK, `Clear()` (:409) y `Add` ⇒ REEMPLAZO.
        If preset.HasFaceBoneRegions Then EscribirMorfosDeRegion(sr, preset.FaceBoneRegions)

        ' FMIN — :462-474: `SetFacialBoneMorphIntensity(root["Morphs"]["Intensity"].asFloat())` SIEMPRE que el
        ' bloque no lance (`HasFacialMorphIntensity`). El valor lo decidió ParseFile como el motor (ausente ⇒ 1.0f
        ' :466-467; null ⇒ 0.0; número ⇒ tal cual). DECISIÓN DE PRODUCTO (la única de este canal): el record de un
        ' NPC sin FMIN no lo gana por escribirle 1.0 — el motor no persiste nada y 1.0 es el neutro del campo — se
        ' crea el subrecord sólo cuando el record ya lo tenía o cuando el valor no es el neutro.
        If preset.HasFacialMorphIntensity AndAlso (sr.TieneIntensidadDeMorfoFacial() OrElse preset.FacialMorphIntensity <> 1.0F) Then
            sr.PonerIntensidadDeMorfoFacial(preset.FacialMorphIntensity)
        End If

        ' Tints — :486-566. `HasFaceTintLayers` = el bloque no lanzó antes de tocar `npc->tints`. La lista ya es
        ' el estado del motor: la validación por RAZA (:512-527, ValidarCapasDeTinteContraLaRaza) se aplicó UNA
        ' vez, al cargar el .json (MainForm.PreviewLooksmenuOverlay), sobre las capas que vienen del archivo.
        ' Acá NO se valida: las capas que llegan sembradas del record (Edit Face, Paste Look, categoría no
        ' tickeada) no pasan por LoadPreset en el motor y validarlas las salteaba o les cambiaba el ColorID.
        If preset.HasFaceTintLayers Then EscribirCapasDeTinte(sr, preset.FaceTintLayers)

        ' QNAM derivation (post-Tints): si la sombra ahora lleva una capa de slot-12 (SkinTone), se re-deriva
        ' el QNAM de ahí para que el plugin guardado tenga el mismo tono de piel que compone el preview.
        ' LooksMenu no serializa QNAM (CharGenInterface.cpp no emite "TextureLighting") — el motor lo lee en
        ' runtime del array de tints del actor. Acá se hace lo mismo a la hora de escribir, así que el record
        ' persistido lleva el color efectivo y no el original, al que las ediciones del usuario nunca llegaron.
        ' Sin capa de SkinTone, el QNAM del record queda sin tocar.
        If raceIsValid Then
            ' El ajuste manual del tono del CUERPO entra ACA (y no dentro de la derivacion compartida): este
            ' es el punto donde el QNAM se materializa como campo del record, que es tono de CUERPO por
            ' definicion. El save y el BAKE salen los dos de esta sombra, asi que con una sola linea quedan
            ' alineados con el render. La cara no pasa por aca (compone desde las capas de tinte).
            Dim derivedSkinTone = DeriveSkinToneQnam(shadow, race, isFemale, pluginManager,
                                                     offset:=If(preset Is Nothing, Nothing, preset.SkinToneOffset))
            Dim tintCountLog = FaceTintInputBuilder.CapasAutoradasDelRecord(sr).Count
            If derivedSkinTone.HasValue Then
                sr.PonerIluminacionDeTextura(derivedSkinTone.Value)
                Dim dR = derivedSkinTone.Value.R
                Dim dG = derivedSkinTone.Value.G
                Dim dB = derivedSkinTone.Value.B
                Dim dA = derivedSkinTone.Value.A
                Dim fidLog = raw.FormID
                Logger.LogLazy(Function() $"[QNAM-OVERLAY] fid=0x{fidLog:X8} derived from SkinTone tint: RGBA=({dR},{dG},{dB},{dA}) tintCount={tintCountLog}")
            Else
                Dim crudo = raw.Record.ColorDeIluminacionDeTextura()
                Dim rawLog As String = If(crudo = Color.Empty, "Nothing", $"({crudo.R},{crudo.G},{crudo.B},{crudo.A})")
                Dim fidLog = raw.FormID
                Logger.LogLazy(Function() $"[QNAM-OVERLAY] fid=0x{fidLog:X8} NO derivation — preserving raw QNAM={rawLog} tintCount={tintCountLog}")
            End If
        End If

        Return shadow
    End Function

    ''' <summary>Vuelca al record los morfos por region de cara que trae el preset, reemplazando los que
    ''' tenia. Los valores que el preset no llene quedan en cero, que es el neutro del campo.
    ''' <para>Solo Fallout 4: Skyrim no declara estos subrecords y el volcado no hace nada.</para></summary>
    Private Sub EscribirMorfosDeRegion(npc As Canon.INpc, regiones As Dictionary(Of UInteger, Single()))
        Dim nf = TryCast(npc, Canon.NpcFO4)
        If nf Is Nothing Then Return
        While nf.FaceMorphs.Count > 0
            If Not nf.QuitarFaceMorphs(0) Then Exit While
        End While
        If regiones Is Nothing Then Return
        For Each kv In regiones
            Dim e = nf.AgregarFaceMorphs()
            If e Is Nothing Then Return
            Dim v = kv.Value
            e.FaceMorphIndex = kv.Key
            e.ValuesPositionX = ValorEn(v, 0)
            e.ValuesPositionY = ValorEn(v, 1)
            e.ValuesPositionZ = ValorEn(v, 2)
            e.ValuesRotationX = ValorEn(v, 3)
            e.ValuesRotationY = ValorEn(v, 4)
            e.ValuesRotationZ = ValorEn(v, 5)
            e.ValuesScale = ValorEn(v, 6)
            e.ValuesUnknown = New Byte(7) {}
        Next
    End Sub

    Private Function ValorEn(valores As Single(), i As Integer) As Single
        If valores Is Nothing OrElse i >= valores.Length Then Return 0.0F
        Return valores(i)
    End Function

    ''' <summary>La puerta ÚNICA por la que las capas de tinte de un .json recién parseado pasan a ser estado del
    ''' motor: reemplaza <c>preset.FaceTintLayers</c> por la lista validada contra la raza
    ''' (<see cref="ValidarCapasDeTinteContraLaRaza"/>). No hace nada si el bloque Tints lanzó
    ''' (<c>HasFaceTintLayers = False</c>: el motor no tocó las capas). La llaman MainForm.PreviewLooksmenuOverlay
    ''' al cargar y el gate PresetCargaEstadoMotorGate por la misma puerta.</summary>
    Public Sub ValidarTintsDelArchivo(preset As LooksmenuLoader.LooksmenuPreset,
                                      race As Canon.IRace, isFemale As Boolean,
                                      pluginManager As PluginManager, npcFormID As UInteger)
        If preset Is Nothing OrElse Not preset.HasFaceTintLayers OrElse preset.FaceTintLayers Is Nothing Then Return
        preset.FaceTintLayers = ValidarCapasDeTinteContraLaRaza(preset.FaceTintLayers, race, isFemale, pluginManager, npcFormID)
    End Sub

    ''' <summary>La validación por RAZA que LoadPreset hace capa por capa (f4ee CharGenInterface.cpp:512-527),
    ''' sobre las capas que ParseFile ya ordenó (TintOrder :537-556):
    ''' <list type="bullet">
    ''' <item>:512-513 <c>chargenData->GetTemplateByIndex(keyValue)</c> nulo ⇒ la capa NO se crea (salteada).
    ''' El catálogo es el del compositor (<c>LmCustomTintLoader.Fusionar</c>: RACE + tints custom de LooksMenu,
    ''' que LM registra en el mismo <c>chargenData</c>) y se busca por <c>Option.Index</c>
    ''' (<c>TintesEfectivos.BuscarOpcion</c>).</item>
    ''' <item>:519-529 recién con plantilla válida el motor lee Color/ColorID/Percent; si alguno lanza, el catch
    ''' de :563 corta el bloque con <c>npc->tints</c> YA limpiado (:498) y el <c>tintMap</c> local perdido ⇒ el
    ''' actor queda SIN NINGUNA capa (<see cref="LooksmenuLoader.CapaDeTintePreset.ConversionFallida"/>).</item>
    ''' <item>:520-527 sólo paleta: <c>GetColorDataByID(SInt16 → UInt16)</c> compara contra
    ''' <c>ColorData.colorID</c> (GameCustomization.h:124 = TTEC TemplateIndex); sin match ⇒ <c>colors[0].colorID</c>;
    ''' sin colores ⇒ 0. El Color del JSON se conserva tal cual (:519 no depende del ColorID).</item>
    ''' </list>
    ''' Sin raza resuelta (plugin ausente) o fuera de FO4 el motor no llega acá; la app no puede validar y
    ''' deja las capas como vinieron — es el mismo criterio de «lo que no se puede resolver, se preserva» y
    ''' el comportamiento que ya tenía. Se loguea.
    ''' <para>Se llama UNA vez por carga de .json, sólo sobre las capas que vienen del ARCHIVO
    ''' (MainForm.PreviewLooksmenuOverlay, con la categoría Tints tickeada). No se llama desde el overlay por
    ''' sombra: ahí llegan también capas sembradas del record (Edit Face, Paste Look, categoría no tickeada), que
    ''' en el motor no pasan por LoadPreset. Es idempotente: una lista ya validada sale igual.</para></summary>
    Public Function ValidarCapasDeTinteContraLaRaza(capas As IEnumerable(Of LooksmenuLoader.CapaDeTintePreset),
                                                      race As Canon.IRace, isFemale As Boolean,
                                                      pluginManager As PluginManager,
                                                      npcFormID As UInteger) As List(Of LooksmenuLoader.CapaDeTintePreset)
        Dim salida As New List(Of LooksmenuLoader.CapaDeTintePreset)
        If capas Is Nothing Then Return salida
        If TryCast(race, Canon.RaceFO4) Is Nothing Then
            For Each c In capas
                If c IsNot Nothing Then salida.Add(c)
            Next
            Dim cuantas = salida.Count
            Logger.LogLazy(Function() $"[TINTS-OVERLAY] npc=0x{npcFormID:X8} raza no resuelta o no-FO4: {cuantas} capas sin validar")
            Return salida
        End If

        Dim grupos = LmCustomTintLoader.Fusionar(race, isFemale, pluginManager)
        Dim salteadas = 0
        For Each c In capas
            If c Is Nothing Then Continue For
            Dim opcion = grupos.BuscarOpcion(c.Index)
            If opcion Is Nothing Then
                salteadas += 1
                Continue For
            End If
            If c.ConversionFallida Then
                Dim idx = c.Index
                Logger.LogLazy(Function() $"[TINTS-OVERLAY] npc=0x{npcFormID:X8} capa 0x{idx:X} con Color/ColorID/Percent no convertible => el motor lanza: canal de tints VACIO")
                salida.Clear()
                Return salida
            End If
            Dim capa = LooksmenuLoader.CloneFaceTintLayer(c)
            If capa.Discriminator = 1US Then
                Dim colorId16 As UShort = CUShort(capa.TemplateColorIndex And &HFFFF)
                Dim colores = If(opcion.TemplateColors, New List(Of ColorDeTinteEfectivo))
                If Not colores.Any(Function(t) t.TemplateIndex = colorId16) Then
                    Dim primero As Integer = If(colores.Count > 0, CInt(colores(0).TemplateIndex), 0)
                    ' `palette->colorID` es SInt16 (GameCustomization.h:221): el UInt16 del catálogo se guarda con
                    ' truncación con signo.
                    capa.TemplateColorIndex = If(primero >= &H8000, primero - &H10000, primero)
                End If
            End If
            salida.Add(capa)
        Next
        If salteadas > 0 Then
            Dim n = salteadas
            Logger.LogLazy(Function() $"[TINTS-OVERLAY] npc=0x{npcFormID:X8} {n} capas con indice sin plantilla en la raza: salteadas")
        End If
        Return salida
    End Function

    ''' <summary>Vuelca al record las capas de tinte de cara que trae el preset, en ese orden y
    ''' reemplazando las que tenia. El color y la posicion en la paleta solo se escriben en las capas de
    ''' PALETA: en las de conjunto de texturas esos bytes son otra cosa.
    ''' <para>Solo Fallout 4.</para></summary>
    Private Sub EscribirCapasDeTinte(npc As Canon.INpc, capas As IEnumerable(Of LooksmenuLoader.CapaDeTintePreset))
        Dim nf = TryCast(npc, Canon.NpcFO4)
        If nf Is Nothing Then Return
        While nf.FaceTintingLayers.Count > 0
            If Not nf.QuitarFaceTintingLayers(0) Then Exit While
        End While
        If capas Is Nothing Then Return
        For Each c In capas
            If c Is Nothing Then Continue For
            Dim e = nf.AgregarFaceTintingLayers()
            If e Is Nothing Then Return
            e.IndexDataType = c.Discriminator
            e.LayerIndex = c.Index
            e.DataValue = CByte(Math.Max(0, c.Value))
            If c.Discriminator <> 1 Then Continue For
            If c.Color <> Color.Empty Then
                e.ColorRed = c.Color.R
                e.ColorGreen = c.Color.G
                e.ColorBlue = c.Color.B
            End If
            If c.TemplateColorIndex >= 0 Then e.DataTemplateColorIndex = CShort(c.TemplateColorIndex)
        Next
    End Sub

    ''' <summary>Derive the effective QNAM (TextureLightingColor) from the NPC's slot-12 SkinTone
    ''' tint layer. Returns Nothing when no such layer exists or its palette doesn't resolve. The
    ''' returned Color packs RGB from the palette CLFM and A from tl.Value (the layer's percent,
    ''' scaled to 0..255) — same shape MainForm.ResolveNpcSkinToneColor consumes. Single source of
    ''' truth shared by render (preview) and save (NpcRecordOverlay) so the two never drift.
    ''' <para>The Slot enum value is a schema-defined field name,
    ''' NOT a hardcoded magic number — this is the canonical lookup for "skin tint layer".</para></summary>
    Public Function DeriveSkinToneQnam(npc As NPC_Data, race As Canon.IRace, isFemale As Boolean, pluginManager As PluginManager,
                                       Optional raceFormIDOverride As UInteger = 0UI,
                                       Optional offset As SkinToneQnamOffset = Nothing) As Nullable(Of Color)
        ' SSE (Skyrim): no slot-12; the skin tone is the RACE tint layer whose TINP mask type == 6, with the
        ' intensity FOLDED into the QNAM colour (SSE QNAM has no alpha). Game-gated so FO4 stays byte-identical.
        ' This single source of truth feeds both the save-overlay QNAM (above) and the render body (via
        ' ResolveNpcSkinToneColor), so face and body match on SSE.
        If Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            If npc Is Nothing Then Return Nothing
            ' Raza EFECTIVA cuando el caller la tiene (state.RaceFormID); npc.RaceFormID puede ser el raw
            ' cacheado con la raza vieja tras un cambio de raza sin preset aplicado.
            Dim raceFid = If(raceFormIDOverride <> 0UI, raceFormIDOverride, npc.Record.Race)
            Return SseFaceTintComposer.ResolveSkinToneQnam(pluginManager, npc, race, raceFid, isFemale, offset)
        End If

        If npc Is Nothing OrElse race Is Nothing Then Return Nothing

        ' Iterar las capas MERGED (autoradas + defaults HEREDADOS de RACE), NO solo npc.FaceTintLayers.
        ' Asi el skin-tone HEREDADO (slot-12 que el NPC no autora) tambien resuelve -> el render (uniform
        ' albedo*=tintColor) y el save lo toman SIN tener que materializarlo en Face Edit. El heredado se
        ' comporta identico a uno autorado: MergeTintLayersWithRaceDefaults ya pone Color=CLFM y
        ' Value=Alpha*100 del TemplateColor por el indice TTED. Ver [[50-facetint-leyes-y-compositor]].
        Dim tintGroups = LmCustomTintLoader.Fusionar(race, isFemale, pluginManager)
        Dim merged = FaceTintInputBuilder.MergeTintLayersWithRaceDefaults(npc.Record, tintGroups, pluginManager)

        For Each tl In merged
            Dim opt = tintGroups.BuscarOpcion(tl.Index)
            If opt Is Nothing Then Continue For
            If opt.Slot <> CUShort(TintSlot.SkinTone) Then Continue For
            If tl.Discriminator <> 1 Then Continue For   ' Palette only — color source for skin tone

            If tl.Color <> Color.Empty Then
                ' Ajuste manual del tono del CUERPO: se suma sobre el color de la paleta y sobre la opacidad de
                ' la capa, los dos normalizados a [0..1] = el dominio del dato (el QNAM en disco son FLOATS; el
                ' byte es la cuantizacion de ESTA funcion, no la unidad del campo). En FO4 la intensidad ES el
                ' alpha del QNAM -la opacidad del soft-light del cuerpo- asi que va derecho ahi; en SSE no hay
                ' alpha y la rama de arriba la pliega en el color con el seed y la convencion de la config.
                ' Con offset Nothing/cero la aritmetica es la de antes byte a byte.
                Dim r01 As Double = CDbl(tl.Color.R) / 255.0R
                Dim g01 As Double = CDbl(tl.Color.G) / 255.0R
                Dim b01 As Double = CDbl(tl.Color.B) / 255.0R
                Dim a01 As Double = SkinToneQnamOffset.Clamp01(CDbl(tl.Value) / 100.0R)
                If offset IsNot Nothing AndAlso Not offset.IsZero Then
                    offset.ApplyToRgb01(r01, g01, b01)
                    a01 = offset.ApplyToIntensity01(a01)
                End If
                Return Color.FromArgb(CInt(Math.Round(a01 * 255.0R)),
                                      CInt(Math.Round(r01 * 255.0R)),
                                      CInt(Math.Round(g01 * 255.0R)),
                                      CInt(Math.Round(b01 * 255.0R)))
            End If
        Next

        Return Nothing
    End Function

    ''' <summary>Replace the entry in <paramref name="headParts"/> whose PartType matches the
    ''' new HDPT's PartType, with <paramref name="newHdptFormID"/>. PartType is READ from the new
    ''' HDPT itself — we do NOT assume "head=Face" or "headRear=HeadRear". This matches engine
    ''' behaviour: F4SE's <c>SkinInterface.cpp:292</c> calls <c>npc->ChangeHeadPart(headPart, ...)</c>
    ''' which internally uses <c>headPart->type</c> as the target slot, not a hardcoded category.
    ''' So a JSON template that puts a Hair HDPT in "maleHead" replaces the Hair slot, not Face.
    ''' If no entry of that PartType exists in <paramref name="headParts"/>, the new HDPT is
    ''' appended (mirrors engine post-AddHeadPart fallthrough).</summary>
    Private Sub ApplyLmHdptReplacement(headParts As List(Of UInteger), newHdptFormID As UInteger,
                                        pluginManager As PluginManager)
        If newHdptFormID = 0UI Then Return
        Dim newRec = pluginManager.GetRecord(newHdptFormID)
        If newRec Is Nothing OrElse newRec.Header.Signature <> "HDPT" Then Return

        ' Read the target PartType from the NEW HDPT — engine-faithful (engine reads
        ' headPart->type for the slot lookup, doesn't accept it as an argument).
        Dim targetPartType As Integer
        Try
            Dim newHdpt = Canon.CanonRecords.Hdpt(newRec, pluginManager)
            targetPartType = newHdpt.TipoDeParte()
        Catch ex As Exception
            Logger.LogLazy(Function() $"[LM-HDPT-REPLACE] HDPT 0x{newHdptFormID:X8} parse failed; replacement skipped: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try
        ' PartType=0 (Misc) is freestanding (extras like eyelashes, AO meshes) — those don't
        ' replace anything, they just accumulate. Add as freestanding.
        If targetPartType = 0 Then
            If Not headParts.Contains(newHdptFormID) Then headParts.Add(newHdptFormID)
            Return
        End If

        ' Find the index of the existing HDPT of the same PartType. Walk from the front; if
        ' multiple exist (shouldn't happen for vanilla NPCs but mods may inject) we replace the
        ' first and remove the rest to mirror engine post-Add (one slot per PartType).
        Dim replaceIdx As Integer = -1
        Dim removalIndices As New List(Of Integer)
        For i = 0 To headParts.Count - 1
            Dim r = pluginManager.GetRecord(headParts(i))
            If r Is Nothing OrElse r.Header.Signature <> "HDPT" Then Continue For
            Try
                Dim hd = Canon.CanonRecords.Hdpt(r, pluginManager)
                If hd.TipoDeParte() = targetPartType Then
                    If replaceIdx < 0 Then
                        replaceIdx = i
                    Else
                        removalIndices.Add(i)
                    End If
                End If
            Catch
            End Try
        Next

            If replaceIdx >= 0 Then
                headParts(replaceIdx) = newHdptFormID
                ' Remove back-to-front so indices stay valid.
                For j = removalIndices.Count - 1 To 0 Step -1
                    Dim idx = removalIndices(j)
                    ' El duplicado EXACTO del que acabamos de poner se saca SIEMPRE (decision 2:
                    ' colapsar repetidos, como el CK). Si no, saltear el borrado para un tipo que
                    ' acumula dejaria el MISMO FormID dos veces, y es PUNTO FIJO: una segunda pasada
                    ' tampoco lo colapsa, y MismaLista da False => se escribe el PNAM duplicado.
                    If headParts(idx) = newHdptFormID OrElse Not Canon.SlotAcumulaVarios(targetPartType) Then
                        headParts.RemoveAt(idx)
                    End If
                Next
            Else
                headParts.Add(newHdptFormID)
            End If
    End Sub

    ''' <summary>Single source of truth for "the preset must reflect the LM template's bundle,
    ''' not just the id". Materializes <paramref name="preset.SkinTemplateId"/>'s head + headRear
    ''' HDPT swaps into <paramref name="preset.HeadPartFormIDs"/> and marks
    ''' <c>HasHeadPartFormIDs=True</c>, so any downstream consumer (Save ESP writer, Edit Face
    ''' seed, Copy Look snapshot) sees the same picture the live render already shows via
    ''' <see cref="ApplyPresetOverlayToNpcData"/>.
    '''
    ''' Idempotent: HDPTs already present in the list are NOT duplicated. Safe to call multiple
    ''' times on the same preset.
    '''
    ''' Called by every path that touches a preset whose <c>SkinTemplateId</c> is set:
    ''' • Load LooksMenu (after parsing the JSON).
    ''' • Copy Look (BuildPresetFromState, after copying SkinTemplateId from overlay).
    ''' • Edit Face seed (so the user sees the HDPTs the LM template injected).
    ''' • EditBody combo handler (when the user picks a template from the dropdown).
    ''' No-op when SkinTemplateId is empty or the resolver doesn't find the template.</summary>
    Public Sub MaterializeLmTemplateBundleToPreset(preset As LooksmenuLoader.LooksmenuPreset,
                                                    isFemale As Boolean,
                                                    resolver As ResolveLmSkinTemplateDelegate)
        If preset Is Nothing Then Return
        If String.IsNullOrEmpty(preset.SkinTemplateId) Then Return
        If resolver Is Nothing Then Return
        Dim tpl = resolver(preset.SkinTemplateId)
        If tpl Is Nothing Then Return

        Dim genderIdx As Integer = If(isFemale, 1, 0)
        Dim head As UInteger = tpl.HeadHdptFormID(genderIdx)
        Dim rear As UInteger = tpl.HeadRearHdptFormID(genderIdx)
        If head = 0UI AndAlso rear = 0UI Then Return

        ' Track each HDPT we inject so Retract can identify and remove ONLY the template's
        ' contribution later. AddHdptIfMissingPreset is idempotent vs the list, but the set
        ' should get the FormID even if it was already present in the list (which may have
        ' come from raw NPC PNAM and now coincides with the template — Retract still needs to
        ' know "the template asserted this one too").
        If head <> 0UI Then
            AddHdptIfMissingPreset(preset.HeadPartFormIDs, head)
            preset.LmTemplateInjectedHdptFormIDs.Add(head)
        End If
        If rear <> 0UI Then
            AddHdptIfMissingPreset(preset.HeadPartFormIDs, rear)
            preset.LmTemplateInjectedHdptFormIDs.Add(rear)
        End If
        ' Only flip Has* if it wasn't already True. If something else (Edit Face / Paste)
        ' set it before us, preserve that authority — we record our own flag separately.
        If Not preset.HasHeadPartFormIDs Then
            preset.HasHeadPartFormIDs = True
            preset.HasHeadPartFormIDsSetByTemplate = True
        End If
    End Sub

    ''' <summary>Inverse of <see cref="MaterializeLmTemplateBundleToPreset"/>: removes from
    ''' <paramref name="preset.HeadPartFormIDs"/> exactly the HDPTs a previous Materialize call
    ''' injected (tracked in <see cref="LooksmenuLoader.LooksmenuPreset.LmTemplateInjectedHdptFormIDs"/>),
    ''' and resets <c>HasHeadPartFormIDs=False</c> only if Materialize was the one that flipped it
    ''' (tracked via <c>HasHeadPartFormIDsSetByTemplate</c>). Edits made by Edit Face / Paste / Load
    ''' LM HeadParts arrays are preserved verbatim — Retract NEVER touches them.
    '''
    ''' Used by EditBody's LM template combo handler to do a clean revert before applying a new
    ''' template (or when the user goes back to "(none)").</summary>
    Public Sub RetractLmTemplateBundleFromPreset(preset As LooksmenuLoader.LooksmenuPreset)
        If preset Is Nothing Then Return
        If preset.LmTemplateInjectedHdptFormIDs.Count = 0 AndAlso
           Not preset.HasHeadPartFormIDsSetByTemplate Then Return

        For Each fid In preset.LmTemplateInjectedHdptFormIDs
            preset.HeadPartFormIDs.Remove(fid)
        Next
        preset.LmTemplateInjectedHdptFormIDs.Clear()
        If preset.HasHeadPartFormIDsSetByTemplate Then
            preset.HasHeadPartFormIDs = False
            preset.HasHeadPartFormIDsSetByTemplate = False
        End If
    End Sub

    Private Sub AddHdptIfMissingPreset(list As List(Of UInteger), hdptFormID As UInteger)
        If hdptFormID = 0UI Then Return
        If list.Contains(hdptFormID) Then Return
        list.Add(hdptFormID)
    End Sub

End Module
