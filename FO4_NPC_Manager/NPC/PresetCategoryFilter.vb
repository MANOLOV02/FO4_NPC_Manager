Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>UNIFIED per-category merge, shared by Paste Look and by the LooksMenu/RaceMenu preset loader,
''' for BOTH games. Replaces the old MainForm.BuildFilteredPaste / BuildFilteredPasteSse pair, whose
''' "shared" record-field block was physically duplicated under a SYNC OBLIGATION comment — the two
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
''' when the two builders were separate.</para>
'''
''' <para><b>Engine gate (FO4).</b> LooksMenu's <c>LoadPreset</c> (f4ee CharGenInterface.cpp:269-645) reads
''' every channel inside its own try/catch: when the channel's read throws, the actor keeps whatever it had
''' and the rest of the file is still applied. The parser records that per channel as <c>Has* = False</c>
''' (= "the engine would not write this channel"). A ticked category whose channel the engine would not
''' write is therefore treated exactly like an unticked one (<see cref="MotorEscribe"/>): the target's
''' current value = baseline overlay if it declares the field, else the record — because that is the state
''' the engine leaves. Channels finer than a category (the three MWGT slots, FMRS regions vs FMIN
''' intensity) are completed slot by slot in <see cref="CompletarCanalesQueElMotorNoEscribe"/>.</para></summary>
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
    ''' <summary>⛔ CONTADOR DE LLAMADAS — superficie de arnés, hermana de
    ''' <c>MainForm.AppliedPresetsForEditor</c>.
    ''' <para>Existe porque `BuildFiltered` es CARO — clon de ~41 campos, un `Revert` por cada una de
    ''' las 16 categorías (uno recorre `FaceMorphs` entero) y resolución de HDPTs— y la DECISIÓN 36
    ''' dice que para un heredero se calcula **UNA vez por render** y se cachea en el state. Sin
    ''' contarlo, ese caché se cae en cualquier refactor y **nadie se entera hasta que el preview se
    ''' pone lento**: es una decisión sin gate, que es como no haberla tomado.</para>
    ''' <para>En producción no cuesta nada: un incremento de entero. No se resetea sola — la pone en
    ''' cero quien mide.</para></summary>
    Private _llamadasABuildFiltered As Long = 0
    Public Property LlamadasABuildFiltered As Long
        Get
            Return Threading.Interlocked.Read(_llamadasABuildFiltered)
        End Get
        Set(value As Long)
            Threading.Interlocked.Exchange(_llamadasABuildFiltered, value)
        End Set
    End Property

    Public Function BuildFiltered(source As LooksmenuLoader.LooksmenuPreset,
                                  targetRaw As NPC_Data,
                                  baseline As LooksmenuLoader.LooksmenuPreset,
                                  options As PresetCategoryOptions,
                                  isSse As Boolean,
                                  Optional resolveHdpt As Func(Of UInteger, Canon.IHdpt) = Nothing,
                                  Optional resolveLmTemplate As NpcRecordOverlay.ResolveLmSkinTemplateDelegate = Nothing) As LooksmenuLoader.LooksmenuPreset
        If source Is Nothing Then Return Nothing
        ' ⛔ cg-04: `Interlocked`, no `+= 1`. `PresetCategoryFilter` es un Module => estado ESTATICO
        ' de proceso. Hoy los dos sitios de produccion que llaman aca son de UI y no hay carrera, pero
        ' el bake corre con `Parallel.ForEach` por NPC y el dia que toque este camino un `+= 1L` no
        ' atomico pierde cuentas en silencio. Cuesta lo mismo.
        Threading.Interlocked.Increment(_llamadasABuildFiltered)
        Dim p = LooksmenuLoader.ClonePreset(source)

        ' ⛔⛔ RONDA 16 (rev-41): ¿LA LISTA DE HEAD PARTS DEL RESULTADO VA A VENIR DEL ORIGEN? Es la MISMA condicion
        ' que decide el `Revert` de abajo, sacada afuera porque el rastreo de la inyeccion LM la necesita: el rastreo
        ' DESCRIBE FILAS de `HeadPartFormIDs` y no puede venir de un dueño distinto que la lista.
        Dim facePartsDelOrigen As Boolean =
            options.Value(PresetCategory.FaceParts) AndAlso
            AppliesToGame(PresetCategory.FaceParts, isSse) AndAlso
            MotorEscribe(source, PresetCategory.FaceParts, isSse)

        ' ⛔⛔ RONDA 17 (rev-46): ¿Y LA PLANTILLA? El rastreo describe filas de `HeadPartFormIDs` PERO BAJO LA
        ' AUTORIDAD DE `SkinTemplateId` -- los dos consumidores (`NpcRecordOverlay.RetirarInyeccionLmDeOtroSexo` y
        ' `RetractLmTemplateBundleFromPreset`) RESUELVEN `preset.SkinTemplateId` y BORRAN de la lista lo que el
        ' rastreo les diga. Y esos dos campos son de CATEGORIAS DISTINTAS (`FaceParts` y `LmSkinTemplate`), que el
        ' usuario tilda POR SEPARADO: la misma condicion, calculada igual, para la otra mitad de la pregunta.
        Dim lmTemplateDelOrigen As Boolean =
            options.Value(PresetCategory.LmSkinTemplate) AndAlso
            AppliesToGame(PresetCategory.LmSkinTemplate, isSse) AndAlso
            MotorEscribe(source, PresetCategory.LmSkinTemplate, isSse)

        For Each cat In AllCategories
            ' A category the running game doesn't have behaves like an unticked one: its carrier gets the
            ' target's value, so a cross-game source can never leak MRSV/FMRS into SSE (or sculpt into FO4).
            ' And so does a ticked one whose channel the ENGINE would not write (the channel's read threw
            ' inside LoadPreset's per-channel try/catch ⇒ Has* = False): the engine leaves the actor as it
            ' was, which is the baseline overlay's value when it declares the field, else the record's.
            If options.Value(cat) AndAlso AppliesToGame(cat, isSse) AndAlso MotorEscribe(source, cat, isSse) Then Continue For
            Revert(p, cat, targetRaw, baseline, isSse)
        Next
        CompletarCanalesQueElMotorNoEscribe(p, source, targetRaw, baseline, options, isSse)

        ' ⛔⛔ LO QUE EL USUARIO NO TILDO Y EL BASELINE NO DECLARA, QUEDA SIN DECLARAR.
        ' `Revert` deja la categoria con `Has* = True` y el valor del record, y desde que el record que se
        ' le pasa es el EFECTIVO (X con el bucket de su plantilla) eso convertia una COPIA de la plantilla
        ' en "decision del usuario". Dos consecuencias, las dos malas:
        '   - el NPC dejaba de SEGUIR a su plantilla: cambiabas la cara de la plantilla y el seguia
        '     mostrando la vieja, porque la suya ya contaba como authoreada;
        '   - y al guardar sin desprender, el ESP se llevaba PNAM/MSDK/TETI de la plantilla con el bit 0
        '     ARRIBA -- bytes que el usuario no pidio y que el motor pisa al cargar.
        ' Sin declarar, la BASE los provee, que es de donde tienen que salir.
        ' ⛔ Se limpia con la tabla de canales, no con una lista a mano: la sede de "que campos tiene esta
        ' categoria" es `PorCanal` y no puede haber una segunda.
        Dim enBlanco As New LooksmenuLoader.LooksmenuPreset()
        ' ⛔⛔ LA PREGUNTA ES "EL PREVIO DECLARA ESTA CATEGORIA", y la sede de esa pregunta es
        ' `Describe` -- la MISMA que arma las tildes que el usuario ve. Aca decia `MotorEscribe`, que responde
        ' OTRA pregunta (que canales escribe el cargador del motor al aplicar un preset) y que en Skyrim
        ' devuelve True SIEMPRE por una linea al tope: con cualquier overlay previo la limpieza no corria
        ' NUNCA en ese juego, o sea que el defecto que este bloque cierra seguia vivo desde el segundo
        ' preset. En Fallout pasaba lo mismo por el `Case Else`. Dos preguntas con una sola funcion.
        Dim declaradasPorElPrevio = If(baseline Is Nothing,
                                       New Dictionary(Of PresetCategory, PresetCategories.CategoryInfo)(),
                                       PresetCategories.Describe(baseline, isSse))
        For Each cat In AllCategories
            If Not PresetCategories.HeredaPorTraits(cat, isSse) Then Continue For
            If options.Value(cat) Then Continue For
            ' ⛔⛔ `.Available`, NO la presencia de la clave: `Describe` PRE-CARGA las 16 categorias
            ' (`PresetCategories.vb:273-276`) y les pone `Available=False` a las que el preset no declara.
            ' Preguntar por la clave daba SIEMPRE verdadero con cualquier previo, asi que este bloque era
            ' INERTE salvo en la primerisima carga de la sesion -- y el defecto que cierra seguia vivo
            ' desde el segundo preset.
            Dim infoPrevia As PresetCategories.CategoryInfo = Nothing
            If declaradasPorElPrevio.TryGetValue(cat, infoPrevia) AndAlso infoPrevia.Available Then Continue For
            PorCanal(cat, isSse, Sub(nombre, leerCanal, escribirCanal)
                                     escribirCanal(p, leerCanal(enBlanco))
                                 End Sub)
        Next

        ' Replacing a main-type parent (e.g. a hair swap) orphans the target's raw Misc parts (hairlines):
        ' record them HERE, at the apply point, so Save drops them the same way Edit Face does. Empty when
        ' the parts were preserved or nothing was replaced, so lashes/AO/wet on untouched parents are safe.
        If resolveHdpt IsNot Nothing AndAlso targetRaw IsNot Nothing Then
            p.SuppressedRawHeadPartFormIDs = HeadPartResolver.ComputeReplacedParentOrphanMisc(
                targetRaw.Record.PartesDeCabeza(), p.HeadPartFormIDs, resolveHdpt)
        End If

        ' If the result carries an LM skin template, populate the origin tracker so a later Retract (user
        ' switches template in EditBody) can identify exactly which HDPTs came from it. Without this the
        ' HDPTs would be stuck and a later combo change would duplicate them by PartType.
        '
        ' ⛔⛔⛔ LA SEDE UNICA DEL RASTREO (RONDA 16 rev-41 + RONDA 17 rev-46). El rastreo
        ' (`LmTemplateInjectedHdptFormIDs` + `HasHeadPartFormIDsSetByTemplate`) es una AFIRMACION SOBRE UN PAR:
        ' «estas filas de `HeadPartFormIDs` las puso `SkinTemplateId`». Lista y plantilla son de categorias
        ' DISTINTAS (`FaceParts` y `LmSkinTemplate`) que el usuario tilda por separado, asi que el par puede
        ' quedar con DUEÑOS distintos y entonces la afirmacion no la sostiene nadie. Las CUATRO combinaciones,
        ' una por una, y ninguna se contesta en otro lado -- este bloque corre DESPUES del `Revert` y de la
        ' limpieza de categorias sin declarar, que son los unicos que pueden mover la lista, y PISA lo que
        ' `Revert(LmSkinTemplate)` haya dejado, porque `Revert` corre antes y no puede ver de donde termino
        ' saliendo la lista:
        '
        '   (1) lista del ORIGEN + plantilla del ORIGEN ⇒ el rastreo del ORIGEN, ampliado con el bundle que esa
        '       plantilla tiene EN esa lista. El `Gender` con el que se resuelve el bundle es el del DUEÑO DE LA
        '       LISTA -- aca `p.Gender`, que `ClonePreset` trae del origen.
        '   (2) lista del ORIGEN + plantilla del DESTINO ⇒ VACIO. Ninguna fila de la lista del origen la puso la
        '       plantilla del destino.
        '       ⛔ RONDA 17 (rev-46) — ERA EL ESPEJO DEL DEFECTO DE rev-41 Y SEGUIA VIVO: con «LM skin template»
        '       DESTILDADO y «Face parts» TILDADO quedaba el rastreo del DESTINO (el que copia `Revert` en el Case
        '       LmSkinTemplate) sobre la lista del ORIGEN, y encima este bloque le sumaba el bundle de la plantilla
        '       del DESTINO resuelto con el `Gender` del ORIGEN. `RetirarInyeccionLmDeOtroSexo`, que corre en el
        '       Paste con el sexo EFECTIVO del destino, le BORRABA entonces a la lista recien pegada toda pieza
        '       rastreada que no fuera del bundle de ese sexo.
        '   (3) lista del DESTINO + plantilla del DESTINO ⇒ el rastreo del BASELINE, y SOLO si el baseline es de
        '       donde salio la lista.
        '       ⛔ RONDA 17 (rev-46): `Revert(FaceParts)` (Case FaceParts, mas abajo) copia la lista del baseline
        '       UNICAMENTE cuando `baseline.HasHeadPartFormIDs`; si no, la lista sale del RECORD del target. Sin esa
        '       misma guarda aca, el rastreo del baseline describia filas que NO estaban en la lista resultante --
        '       y si alguna coincidia con una head part propia del record, `RetirarInyeccionLmDeOtroSexo` se la
        '       borraba al destino. Es el MISMO defecto de rev-41 por otra puerta: la condicion tiene que ser la
        '       misma que la del `Revert` que repone la lista.
        '   (4) lista del DESTINO + plantilla del ORIGEN ⇒ VACIO, por lo mismo que (2) al reves: el rastreo del
        '       baseline describe filas del baseline, pero bajo la plantilla del baseline, no bajo la del origen.
        '       ⛔ RONDA 18 (rev-49): esta rama YA TIENE TESTIGO PROPIO — EstadoAbGate --g18 R18-rev49 C, con el
        '       rastreo del baseline POBLADO (hasta la ronda 17 la recorria el caso de rev-41 con el rastreo vacio,
        '       o sea sin discriminar) y su control por la rama (3). Mutante: `If facePartsDelOrigen AndAlso Not
        '       lmTemplateDelOrigen Then` ⇒ (4) cae en el baseline y el Paste le borra al destino su head part.
        '
        ' ⛔ En SKYRIM `lmTemplateDelOrigen` es SIEMPRE False (`AppliesToGame(LmSkinTemplate, True)` = False), asi
        ' que con «Face parts» tildado se cae en (2) y el rastreo queda vacio. DECLARADO: en SSE el rastreo no
        ' tiene quien lo siembre (el bundle de piel LM es de F4EE y `SkinTemplateId` no se puebla en ese juego),
        ' asi que el valor que este bloque escribe y el que escribia antes coinciden en la poblacion que un arnes
        ' puede alcanzar; lo que NO esta medido es un overlay de sesion de SSE con el rastreo poblado.
        p.LmTemplateInjectedHdptFormIDs.Clear()
        If facePartsDelOrigen <> lmTemplateDelOrigen Then
            ' (2) y (4): el par tiene dueños distintos. El rastreo no puede afirmar nada y `HasHeadPartFormIDsSetByTemplate`
            ' tampoco -- esa bandera dice «la plantilla fue la que prendio `HasHeadPartFormIDs`», y la plantilla que viaja
            ' no es la que declaro esta lista. Queda en False, que es el lado conservador: `RetractLmTemplateBundleFromPreset`
            ' no le apaga la declaracion a una lista que no puso la plantilla.
            p.HasHeadPartFormIDsSetByTemplate = False
        ElseIf facePartsDelOrigen Then
            ' (1) las dos del ORIGEN.
            p.HasHeadPartFormIDsSetByTemplate = source.HasHeadPartFormIDsSetByTemplate
            For Each fid In source.LmTemplateInjectedHdptFormIDs
                p.LmTemplateInjectedHdptFormIDs.Add(fid)
            Next
            If Not isSse AndAlso resolveLmTemplate IsNot Nothing AndAlso Not String.IsNullOrEmpty(p.SkinTemplateId) Then
                Dim tpl = resolveLmTemplate(p.SkinTemplateId)
                If tpl IsNot Nothing Then
                    ' ⛔ `p.Gender` = el del dueño de la LISTA (el origen). Resolver el bundle con OTRO sexo marca como
                    ' «inyectada» una fila que esa plantilla nunca puso en ESTA lista, y el que retira por rastreo la borra.
                    Dim genderIdx As Integer = If(p.Gender = 1, 1, 0)
                    Dim head As UInteger = tpl.HeadHdptFormID(genderIdx)
                    Dim rear As UInteger = tpl.HeadRearHdptFormID(genderIdx)
                    If head <> 0UI AndAlso p.HeadPartFormIDs.Contains(head) Then p.LmTemplateInjectedHdptFormIDs.Add(head)
                    If rear <> 0UI AndAlso p.HeadPartFormIDs.Contains(rear) Then p.LmTemplateInjectedHdptFormIDs.Add(rear)
                End If
            End If
        ElseIf baseline IsNot Nothing AndAlso baseline.HasHeadPartFormIDs Then
            ' (3) las dos del DESTINO, y el baseline es de donde salio la lista.
            p.HasHeadPartFormIDsSetByTemplate = baseline.HasHeadPartFormIDsSetByTemplate
            For Each fid In baseline.LmTemplateInjectedHdptFormIDs
                p.LmTemplateInjectedHdptFormIDs.Add(fid)
            Next
        Else
            ' (3) con la lista sacada del RECORD (o sin baseline): el rastreo del baseline no describe ninguna de esas filas.
            p.HasHeadPartFormIDsSetByTemplate = False
        End If

        Return p
    End Function

    ''' <summary>"The engine would write this category from <paramref name="source"/>". FO4 only: LooksMenu's
    ''' LoadPreset (f4ee CharGenInterface.cpp:269-645) wraps each channel in its own try/catch and the
    ''' parser reports a throw as <c>Has* = False</c>. Under Skyrim the answer is always True: RaceMenu's
    ''' LoadJsonPreset has no per-channel catch — a file that does not convert is rejected WHOLE
    ''' (<c>RaceMenuJslot.Load</c> returns Nothing and nothing reaches this filter).
    ''' <para>Categories with no engine channel behind them (the <c>_npcm_</c> carriers: skin override,
    ''' outfit, chargen flag) are always "written": their carrier already distinguishes absent
    ''' (Nothing = preserve) from present.</para></summary>
    Public Function MotorEscribe(source As LooksmenuLoader.LooksmenuPreset, cat As PresetCategory, isSse As Boolean) As Boolean
        If source Is Nothing Then Return False
        If isSse Then Return True
        Select Case cat
            Case PresetCategory.BodyWeight
                ' Per slot (:476-484 reads [0],[1],[2] in sequence; a throw leaves that slot and the following
                ' ones untouched). A slot the engine did not write is `Nothing`; the whole category counts as
                ' written when at least one slot is, and CompletarCanalesQueElMotorNoEscribe fills the rest.
                Return source.WeightThin.HasValue OrElse source.WeightMuscular.HasValue OrElse source.WeightFat.HasValue
            Case PresetCategory.BodyRegions
                Return source.HasBodyMorphValues                    ' Values :373-397
            Case PresetCategory.BodySliders
                Return source.HasBodyMorphSliders                   ' BodyMorphs :568-585 (wipe iff members>0 or null)
            Case PresetCategory.Overlays
                Return source.HasOverlays                           ' Overlays :587-630 (RemoveAll unconditional)
            Case PresetCategory.FaceParts
                Return source.HasHeadPartFormIDs                    ' HeadParts :318-352
            Case PresetCategory.HairColor
                ' HairColor :354-363: written only when the identifier resolves to a loaded CLFM; an absent
                ' or unresolved colour leaves the actor's. The raw identifier still travels (see Completar…).
                Return source.HairColorFormID <> 0UI
            Case PresetCategory.FaceTints
                Return source.HasFaceTintLayers                     ' Tints :486-566
            Case PresetCategory.FaceVertexMorphs
                Return source.HasChargenFaceMorphs                  ' Presets :433-460
            Case PresetCategory.FaceBoneRegions
                ' Regions :399-429 and Intensity :462-474 are two channels of one category: the category is
                ' written when either is; the missing half is completed slot by slot below.
                Return source.HasFaceBoneRegions OrElse source.HasFacialMorphIntensity
            Case Else
                Return True
        End Select
    End Function

    ''' <summary>Second pass for the channels finer than a category. For a TICKED FO4 category whose engine
    ''' write is only partial, the parts the engine did not write take the target's value (baseline if it
    ''' declares them, else the record), exactly as <see cref="Revert"/> does for a whole category:
    ''' <list type="bullet">
    ''' <item>MWGT: <c>Weight[i].asFloat()</c> :476-484 — a slot left <c>Nothing</c> by the parser (the read
    ''' threw there) is filled from the target; the slots before it keep the file's value.</item>
    ''' <item>FMRS regions vs FMIN intensity: Regions :399-429 and Intensity :462-474 are separate
    ''' try/catch blocks, so either half can be the one the engine skipped.</item>
    ''' </list>
    ''' Two app-only carriers ride along with a category the engine did not write and are restored from
    ''' the source because they are not engine channels: the unresolved hair-colour identifier
    ''' (PRESERVACIÓN, no invención — user decision 2026-08-24) and the body skin-tone QNAM adjustment
    ''' (<c>_npcm_</c>, filtered with the tints by user decision, not by LooksMenu).</summary>
    Private Sub CompletarCanalesQueElMotorNoEscribe(p As LooksmenuLoader.LooksmenuPreset,
                                                    source As LooksmenuLoader.LooksmenuPreset,
                                                    raw As NPC_Data,
                                                    baseline As LooksmenuLoader.LooksmenuPreset,
                                                    options As PresetCategoryOptions,
                                                    isSse As Boolean)
        If isSse OrElse source Is Nothing Then Return

        If options.Value(PresetCategory.BodyWeight) Then
            If Not p.WeightThin.HasValue Then p.WeightThin = PickSingle(baseline?.WeightThin, raw?.Record.PesoDelCuerpo(0))
            If Not p.WeightMuscular.HasValue Then p.WeightMuscular = PickSingle(baseline?.WeightMuscular, raw?.Record.PesoDelCuerpo(1))
            If Not p.WeightFat.HasValue Then p.WeightFat = PickSingle(baseline?.WeightFat, raw?.Record.PesoDelCuerpo(2))
        End If

        If options.Value(PresetCategory.FaceBoneRegions) Then
            If Not source.HasFaceBoneRegions Then RevertirRegionesDeCara(p, raw, baseline)
            If Not source.HasFacialMorphIntensity Then RevertirIntensidadDeMorfoFacial(p, raw, baseline)
        End If

        If options.Value(PresetCategory.HairColor) AndAlso source.HairColorFormID = 0UI Then
            p.UnresolvedHairColor = If(source.UnresolvedHairColor, "")
        End If

        If options.Value(PresetCategory.FaceTints) AndAlso Not source.HasFaceTintLayers Then
            p.SkinToneOffset = SkinToneQnamOffset.CloneOrNothing(source.SkinToneOffset)
        End If
    End Sub

    ''' <summary>FMRS regions from the target: baseline's when it declares them, else the record's. The
    ''' result authoritatively defines the field (Has = True), as every Revert branch does.</summary>
    Private Sub RevertirRegionesDeCara(p As LooksmenuLoader.LooksmenuPreset, raw As NPC_Data, baseline As LooksmenuLoader.LooksmenuPreset)
        p.FaceBoneRegions.Clear()
        If baseline IsNot Nothing AndAlso baseline.HasFaceBoneRegions Then
            For Each kv In baseline.FaceBoneRegions
                p.FaceBoneRegions(kv.Key) = CType(kv.Value?.Clone(), Single())
            Next
        ElseIf raw IsNot Nothing Then
            Dim rawFo4 = TryCast(raw.Record, Canon.NpcFO4)
            If rawFo4 IsNot Nothing Then
                For Each fm In rawFo4.FaceMorphs
                    p.FaceBoneRegions(fm.FaceMorphIndex) = New Single() {
                        fm.ValuesPositionX, fm.ValuesPositionY, fm.ValuesPositionZ,
                        fm.ValuesRotationX, fm.ValuesRotationY, fm.ValuesRotationZ, fm.ValuesScale}
                Next
            End If
        End If
        p.HasFaceBoneRegions = True
    End Sub

    ''' <summary>FMIN intensity from the target: baseline's when it declares it, else the record's
    ''' (<c>IntensidadDeMorfoFacial</c> = 1.0 when the record has no FMIN — the app's product decision for
    ''' "absent", same value the parser gives a file without <c>Morphs.Intensity</c>). Has = True: the
    ''' overlay writes it only when the record already carries FMIN or the value is not 1.0.</summary>
    Private Sub RevertirIntensidadDeMorfoFacial(p As LooksmenuLoader.LooksmenuPreset, raw As NPC_Data, baseline As LooksmenuLoader.LooksmenuPreset)
        If baseline IsNot Nothing AndAlso baseline.HasFacialMorphIntensity Then
            p.FacialMorphIntensity = baseline.FacialMorphIntensity
        ElseIf raw IsNot Nothing Then
            p.FacialMorphIntensity = raw.Record.IntensidadDeMorfoFacial()
        Else
            p.FacialMorphIntensity = 1.0F
        End If
        p.HasFacialMorphIntensity = True
    End Sub

    ''' <summary>Overwrite ONE category of <paramref name="p"/> with the target's current value: the
    ''' baseline overlay's when it declares that field, else the raw record's. Record-less carriers
    ''' (F4SE/RaceMenu-only) end up empty when there is no baseline.</summary>
    ''' <summary>⛔ Friend y no Private: es LA primitiva de combinación, y ahora tiene DOS políticas encima.
    ''' <c>BuildFiltered</c> la usa con <c>baseline := el overlay del propio NPC</c> para «preservá lo que este
    ''' NPC muestra»; <c>OverlayDeDibujo</c> la usa con <c>baseline := el overlay del TERMINAL</c> y
    ''' <c>raw := el record del TERMINAL</c> para «heredá lo que muestra tu plantilla». Es la misma frase — «lo
    ''' del baseline si lo declara, si no lo del record» — y por eso no nace un segundo combinador.</summary>
    Friend Sub Revert(p As LooksmenuLoader.LooksmenuPreset,
                       cat As PresetCategory,
                       raw As NPC_Data,
                       baseline As LooksmenuLoader.LooksmenuPreset,
                       isSse As Boolean)
        Select Case cat

            Case PresetCategory.BodyWeight
                If isSse Then
                    If baseline IsNot Nothing AndAlso baseline.SseWeight.HasValue Then
                        p.SseWeight = baseline.SseWeight.Value
                    ElseIf raw IsNot Nothing AndAlso raw.Record.TienePesoDeSkyrim() Then
                        p.SseWeight = raw.Record.PesoDeSkyrim()
                    Else
                        p.SseWeight = 100.0F
                    End If
                Else
                    ' `PesoDelCuerpo` is `Single?` (Nothing = no MWGT or the "use the race's" sentinel). It used
                    ' to be narrowed to `Single` on the way into PickSingle (Option Strict Off): on a record
                    ' without MWGT that narrowing THROWS (InvalidOperationException) instead of preserving.
                    ' Nothing now travels through: the overlay skips a `Nothing` slot (`.HasValue`), which is
                    ' "preserve" — the target keeps having no MWGT.
                    p.WeightThin = PickSingle(baseline?.WeightThin, raw?.Record.PesoDelCuerpo(0))
                    p.WeightMuscular = PickSingle(baseline?.WeightMuscular, raw?.Record.PesoDelCuerpo(1))
                    p.WeightFat = PickSingle(baseline?.WeightFat, raw?.Record.PesoDelCuerpo(2))
                End If

            Case PresetCategory.BodyRegions
                ' MRSV per-region weights (FO4). Either source leaves the list populated, so the result
                ' authoritatively defines the field — Has* True in both branches, as Paste always did.
                p.BodyMorphValues.Clear()
                If baseline IsNot Nothing AndAlso baseline.HasBodyMorphValues Then
                    p.BodyMorphValues.AddRange(baseline.BodyMorphValues)
                ElseIf raw IsNot Nothing Then
                    p.BodyMorphValues.AddRange(raw.Record.ValoresDeRegionCorporal())
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
                ' The result authoritatively defines the field, like every other Revert branch.
                p.HasBodyMorphSliders = True

            Case PresetCategory.BodyScale
                ' RaceMenu NiOverride node transforms (SSE-only) — overlay-only carrier.
                p.SseNodeTransforms = If(baseline Is Nothing, Nothing, LooksmenuLoader.CloneSseNodeTransforms(baseline.SseNodeTransforms))
                ' FALTABA ESTO. Los elementos de primera persona son la OTRA MITAD del mismo array `transforms` del
                ' .jslot, así que destildar esta categoría tiene que devolverlos al baseline igual que los otros. Sin la
                ' línea, los del preset RECHAZADO se quedaban en el carrier, iban al sidecar y se re-emitían en el
                ' próximo "Save RaceMenu preset" — y como todas las demás asignaciones están gateadas por Count > 0,
                ' ningún camino los volvía a poner en Nothing: una vez que entraban, no salían nunca.
                p.SseFirstPersonTransformsRaw = If(baseline Is Nothing OrElse baseline.SseFirstPersonTransformsRaw Is Nothing,
                                                  Nothing, New List(Of String)(baseline.SseFirstPersonTransformsRaw))

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
                                .Tint = CType(ov.Tint?.Clone(), Single()),
                                .OffsetUV = CType(ov.OffsetUV?.Clone(), Single()),
                                .ScaleUV = CType(ov.ScaleUV?.Clone(), Single())
                            })
                        Next
                        p.HasOverlays = baseline.HasOverlays
                    End If
                End If

            Case PresetCategory.SkinOverride
                ' NPC.WNAM. Nothing = "no override", which the overlay merge resolves to the raw record's
                ' own skin — so preserving means carrying the baseline's override (if any) and nothing else.
                p.SkinFormIDOverride = baseline?.SkinFormIDOverride

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
                p.DefaultOutfitFormIDOverride = baseline?.DefaultOutfitFormIDOverride
                p.SleepOutfitFormIDOverride = baseline?.SleepOutfitFormIDOverride

            Case PresetCategory.FaceParts
                ' The overlay merge does wipe + race defaults + preset entries, so preserving means copying
                ' the target's own head parts in: the merge's "preset wins per type" rule then re-establishes
                ' exactly what the NPC had. The unresolved-part lists and the SSE head FTST travel with the
                ' parts (they describe the same selection).
                p.HeadPartFormIDs.Clear()
                p.UnresolvedHeadParts.Clear()
                p.SseUnresolvedHeadParts.Clear()
                p.SseHeadPartsFiltradasPorMotor.Clear()
                p.HeadPartFormIDsIncludeRawExtras = False
                ' `Nothing` (= "sin override, preservar el FTST del target"), NUNCA `0UI`: con el carrier
                ' tri-estado, 0 significa CLEAR EXPLÍCITO. Poner 0 acá —el camino de "categoría NO tickeada",
                ' o sea preservar— le BORRARÍA el FTST al target en el Paste más común del diálogo, y en AMBOS
                ' juegos: este Case no está gateado por juego y NpcRecordOverlay tampoco. Es la única línea de
                ' todo el tri-estado con potencial de cambiar bytes en FO4.
                p.SseHeadTextureFormIDOverride = Nothing
                If baseline IsNot Nothing AndAlso baseline.HasHeadPartFormIDs Then
                    p.HeadPartFormIDs.AddRange(baseline.HeadPartFormIDs)
                    p.UnresolvedHeadParts.AddRange(baseline.UnresolvedHeadParts)
                    p.SseUnresolvedHeadParts.AddRange(baseline.SseUnresolvedHeadParts)
                    p.SseHeadPartsFiltradasPorMotor.AddRange(baseline.SseHeadPartsFiltradasPorMotor)
                    p.HeadPartFormIDsIncludeRawExtras = baseline.HeadPartFormIDsIncludeRawExtras
                ElseIf raw IsNot Nothing Then
                    p.HeadPartFormIDs.AddRange(raw.Record.PartesDeCabeza())
                End If
                If baseline IsNot Nothing Then p.SseHeadTextureFormIDOverride = baseline.SseHeadTextureFormIDOverride
                p.HasHeadPartFormIDs = True

            Case PresetCategory.HairColor
                ' HCLF FormID (both games) + the RaceMenu custom RGB (SSE), which is the same decision.
                If baseline IsNot Nothing AndAlso baseline.HairColorFormID <> 0UI Then
                    p.HairColorFormID = baseline.HairColorFormID
                Else
                    p.HairColorFormID = If(raw Is Nothing, 0UI, raw.Record.HairColor)
                End If
                p.SseHairColorRgb = baseline?.SseHairColorRgb
                ' El identificador sin resolver viaja con el color: acá el color se REEMPLAZA por el del
                ' baseline/raw, así que el crudo del archivo dejaría de describirlo. Sin limpiarlo, un
                ' HairColorFormID=0 legítimo (el baseline no tenía color) más un UnresolvedHairColor viejo
                ' hacen que el auditor reporte "el mod no está instalado" para un color que se descartó a
                ' propósito.
                p.UnresolvedHairColor = ""

            Case PresetCategory.FaceTints
                If isSse Then
                    p.SseTintLayers = Nothing
                    p.SseTintTexOverride = Nothing
                    p.HasSseTints = False
                    If baseline IsNot Nothing AndAlso baseline.HasSseTints AndAlso baseline.SseTintLayers IsNot Nothing Then
                        p.SseTintLayers = CloneSseTintLayers(baseline.SseTintLayers)
                        p.HasSseTints = True
                        If baseline.SseTintTexOverride IsNot Nothing Then p.SseTintTexOverride = New Dictionary(Of Integer, String)(baseline.SseTintTexOverride)
                    ElseIf raw IsNot Nothing Then
                        Dim delRecord = LooksmenuLoader.CapasDeTinteSseDelRecord(raw.Record)
                        If delRecord.Count > 0 Then
                            p.SseTintLayers = delRecord
                            p.HasSseTints = True
                        End If
                    End If
                Else
                    p.FaceTintLayers.Clear()
                    ' If/Else y no un ternario: las dos ramas traen listas de tipos distintos y el ternario
                    ' compila igual, pero revienta al evaluarlo.
                    Dim src As List(Of LooksmenuLoader.CapaDeTintePreset) = Nothing
                    If baseline IsNot Nothing AndAlso baseline.HasFaceTintLayers Then
                        src = baseline.FaceTintLayers
                    ElseIf raw IsNot Nothing Then
                        src = LooksmenuLoader.CapasDeTinteDelRecord(raw.Record)
                    End If
                    If src IsNot Nothing Then
                        For Each tl In src
                            p.FaceTintLayers.Add(LooksmenuLoader.CloneFaceTintLayer(tl))
                        Next
                    End If
                    p.HasFaceTintLayers = True
                End If

                ' El ajuste manual del tono del cuerpo (QNAM) se filtra CON los tints, no aparte: es un ajuste
                ' de tinte de piel y sigue la misma decision del usuario. Game-agnostico, por eso va fuera del
                ' If de arriba. Sin baseline queda Nothing = "sin ajuste", que es el estado neutro correcto.
                p.SkinToneOffset = SkinToneQnamOffset.CloneOrNothing(If(baseline Is Nothing, Nothing, baseline.SkinToneOffset))

            Case PresetCategory.FaceVertexMorphs
                If isSse Then
                    ' NAM9 (18 floats) + NAMA (4 type uints) + the RaceMenu custom morphs (no record source).
                    If baseline IsNot Nothing AndAlso baseline.HasSseMorphs AndAlso baseline.SseNam9 IsNot Nothing Then
                        p.SseNam9 = DirectCast(baseline.SseNam9.Clone(), Single())
                        p.SseNama = If(baseline.SseNama Is Nothing, SseNam9MorphMap.DefaultNamaVector(), DirectCast(baseline.SseNama.Clone(), UInteger()))
                        p.HasSseMorphs = True
                    Else
                        Dim rawNam9 = raw?.Record.DeslizadoresDeCara()
                        Dim rawNama = raw?.Record.PartesDeCara()
                        Dim nam9(SseNam9MorphMap.Nam9SliderCount - 1) As Single
                        For i = 0 To SseNam9MorphMap.Nam9SliderCount - 1
                            If rawNam9 IsNot Nothing AndAlso i < rawNam9.Length Then
                                Dim v = rawNam9(i)
                                If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then v = 0.0F
                                nam9(i) = v
                            End If
                        Next
                        Dim nama(SseNam9MorphMap.NamaFamilyCount - 1) As UInteger
                        For f = 0 To SseNam9MorphMap.NamaFamilyCount - 1
                            ' El centinela "sin tipo asignado" viaja INTACTO — misma ley y mismo motivo que en
                            ' MainForm.BuildPresetFromState; colapsarlo a 0 convierte "sin tipo" en "tipo 0" y le
                            ' cambia la cara al NPC. Los dos sitios tienen que decir lo mismo.
                            nama(f) = If(rawNama IsNot Nothing AndAlso f < rawNama.Length,
                                         rawNama(f), SseNam9MorphMap.NamaUnset)
                        Next
                        p.SseNam9 = nam9
                        p.SseNama = nama
                        p.HasSseMorphs = (rawNam9 IsNot Nothing OrElse rawNama IsNot Nothing)
                    End If
                    ' El slot 18 (VampireMorph) es parte de ESTA categoría y también tiene que revertirse.
                    ' `BuildFiltered` arranca clonando el preset ORIGEN y `ClonePreset` copia SseVampireMorph, así
                    ' que sin esta línea un Load/Paste con "Face vertex morphs" DESTILDADO dejaba el VampireMorph
                    ' del origen sobre un target al que se le preservó todo el resto de la cara — y de ahí viajaba
                    ' al .jslot exportado del target Y al ESP: NpcRecordOverlay escribe NAM9[18] cuando
                    ' `SseVampireMorph.HasValue` (skee64 ApplyPresetData :188-192 pisa `option[i]` posicional, y
                    ' el 18 es el slot del VampireMorph que RaceMenu guarda en `morphs[18]`).
                    p.SseVampireMorph = If(baseline IsNot Nothing AndAlso baseline.SseVampireMorph.HasValue,
                                           baseline.SseVampireMorph,
                                           SseNam9MorphMap.VampireMorphDe(If(raw Is Nothing, Nothing, raw.Record.DeslizadoresDeCara())))
                Else
                    p.ChargenFaceMorphs.Clear()
                    Dim src = If(baseline IsNot Nothing AndAlso baseline.HasChargenFaceMorphs, baseline.ChargenFaceMorphs,
                                 If(raw Is Nothing, Nothing, raw.Record.MorfosDeCara()))
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
                ' FMRS regions and FMIN intensity are one category (the engine writes the intensity with the
                ' regions: Intensity :462-474 right after Regions :399-429), so preserving the regions
                ' preserves the intensity too. Two helpers because the ENGINE can skip either half on its own
                ' (separate try/catch blocks) and CompletarCanalesQueElMotorNoEscribe reverts just that half.
                RevertirRegionesDeCara(p, raw, baseline)
                RevertirIntensidadDeMorfoFacial(p, raw, baseline)

            Case PresetCategory.Sculpt
                ' Per-vertex head/shape sculpt (SSE) — RaceMenu-only carrier, no record source.
                p.SseSculptHead = If(baseline Is Nothing, Nothing, CloneSseSculptHead(baseline.SseSculptHead))
                p.SseSculptParts = If(baseline Is Nothing, Nothing, LooksmenuLoader.CloneSseSculptParts(baseline.SseSculptParts))

            Case PresetCategory.IsCharGenPreset
                If baseline IsNot Nothing AndAlso baseline.IsCharGenFacePreset.HasValue Then
                    p.IsCharGenFacePreset = baseline.IsCharGenFacePreset.Value
                ElseIf raw IsNot Nothing Then
                    p.IsCharGenFacePreset = raw.Record.ConfigurationFlagsIsCharGenFacePreset
                Else
                    p.IsCharGenFacePreset = Nothing
                End If

        End Select
    End Sub

    ''' <summary>Qué hacer con UN canal de una categoría. <paramref name="leer"/> y <paramref name="escribir"/>
    ''' salen de la reflexión sobre el campo, no de una lambda escrita a mano por canal.</summary>
    Friend Delegate Sub AccionDeCanal(nombre As String,
                                      leer As Func(Of LooksmenuLoader.LooksmenuPreset, Object),
                                      escribir As Action(Of LooksmenuLoader.LooksmenuPreset, Object))

    ''' <summary>LA TABLA: qué campos del preset son los canales de cada categoría. Sale de recorrer las
    ''' ramas de <see cref="Revert"/> y anotar cada <c>p.&lt;campo&gt;</c> que toca cada una — no de lo que uno
    ''' se acuerde.
    ''' <para>⛔⛔ CINCO canales son SUB-CARRIERS que no tienen categoría propia y que un comparador escrito a
    ''' mano se come, siempre: <c>SkinToneOffset</c> viaja dentro de <c>FaceTints</c> aunque su panel viva en Edit
    ''' Body; <c>SseHeadTextureFormIDOverride</c> y <c>HeadPartFormIDsIncludeRawExtras</c> dentro de
    ''' <c>FaceParts</c>; <c>SseHairColorRgb</c> dentro de <c>HairColor</c>; y <c>SseVampireMorph</c> dentro de
    ''' <c>FaceVertexMorphs</c> — éste último con su propio comentario en Revert diciendo que olvidarlo ya
    ''' causó una fuga al ESP.</para>
    ''' <para>⛔ Cinco categorías son GAME-AWARE porque su CARRIER cambia de juego, no porque la ley cambie:
    ''' el peso, los tintes, los morfos de cara, los deslizadores y los overlays.</para></summary>
    Private Function CanalesDe(cat As PresetCategory, isSse As Boolean) As String()
        Select Case cat
            Case PresetCategory.BodyWeight
                ' NAM7 en Skyrim (un escalar) contra MWGT en Fallout (tres). Carrier distinto, misma ley.
                Return If(isSse, New String() {"SseWeight"},
                                 New String() {"WeightThin", "WeightMuscular", "WeightFat"})

            Case PresetCategory.BodyRegions
                Return New String() {"BodyMorphValues", "HasBodyMorphValues"}

            Case PresetCategory.BodySliders
                ' `BodyMorphsKeyed` se limpia en los DOS juegos y sólo se rellena en SSE, así que es canal de
                ' los dos: si estuviera sólo en SSE, un FO4 con el campo sucio no se detectaría como cambio.
                Return New String() {"BodyMorphSliders", "BodyMorphsKeyed", "HasBodyMorphSliders"}

            Case PresetCategory.BodyScale
                Return New String() {"SseNodeTransforms", "SseFirstPersonTransformsRaw"}

            Case PresetCategory.Overlays
                Return If(isSse, New String() {"SseBodyOverlays", "SseSkinOverrides", "Overlays", "HasOverlays"},
                                 New String() {"Overlays", "HasOverlays", "SseBodyOverlays", "SseSkinOverrides"})

            Case PresetCategory.SkinOverride
                Return New String() {"SkinFormIDOverride"}

            Case PresetCategory.LmSkinTemplate
                Return New String() {"SkinTemplateId", "LmTemplateInjectedHdptFormIDs",
                                     "HasHeadPartFormIDsSetByTemplate"}

            Case PresetCategory.Outfit
                Return New String() {"DefaultOutfitFormIDOverride", "SleepOutfitFormIDOverride"}

            Case PresetCategory.FaceParts
                Return New String() {"HeadPartFormIDs", "UnresolvedHeadParts", "SseUnresolvedHeadParts",
                                     "SseHeadPartsFiltradasPorMotor", "HeadPartFormIDsIncludeRawExtras",
                                     "SseHeadTextureFormIDOverride", "HasHeadPartFormIDs"}

            Case PresetCategory.HairColor
                Return New String() {"HairColorFormID", "SseHairColorRgb", "UnresolvedHairColor"}

            Case PresetCategory.FaceTints
                ' El ajuste manual del tono (QNAM) NO es game-aware: viaja con los tintes en los dos juegos,
                ' fuera del If de Revert. Por eso va en las dos ramas.
                Return If(isSse,
                          New String() {"SseTintLayers", "SseTintTexOverride", "HasSseTints", "SkinToneOffset"},
                          New String() {"FaceTintLayers", "HasFaceTintLayers", "SkinToneOffset"})

            Case PresetCategory.FaceVertexMorphs
                Return If(isSse,
                          New String() {"SseNam9", "SseNama", "HasSseMorphs", "SseVampireMorph"},
                          New String() {"ChargenFaceMorphs", "HasChargenFaceMorphs"})

            Case PresetCategory.CustomMorphs
                Return New String() {"SseCustomMorphs"}

            Case PresetCategory.FaceBoneRegions
                ' Las regiones (FMRS) y la intensidad (FMIN) son UNA categoría: el motor escribe la intensidad
                ' con las regiones, y por eso Revert las revierte juntas con dos helpers.
                Return New String() {"FaceBoneRegions", "HasFaceBoneRegions",
                                     "FacialMorphIntensity", "HasFacialMorphIntensity"}

            Case PresetCategory.Sculpt
                Return New String() {"SseSculptHead", "SseSculptParts"}

            Case PresetCategory.IsCharGenPreset
                ' No es un canal de apariencia: es el bit 2 de ACBS, y el RE mostró que GATEA los tintes.
                Return New String() {"IsCharGenFacePreset"}

        End Select
        Throw New ArgumentOutOfRangeException(NameOf(cat), cat,
            "No hay tabla de canales para esta categoria. Una categoria nueva SIN canales es una que " &
            "el predicado del COMMIT nunca ve cambiar: el desprendimiento no se latchea y el motor le " &
            "pisa la edicion al usuario.")
    End Function

    ''' <summary>Los campos de <c>LooksmenuPreset</c>, por nombre. Cacheado: la tabla se recorre por cada
    ''' categoría de cada comparación.</summary>
    Private ReadOnly CamposDelPreset As Dictionary(Of String, Reflection.FieldInfo) =
        GetType(LooksmenuLoader.LooksmenuPreset).
            GetFields(Reflection.BindingFlags.Public Or Reflection.BindingFlags.Instance).
            ToDictionary(Function(f) f.Name, StringComparer.Ordinal)

    Private Function CampoDelPreset(nombre As String) As Reflection.FieldInfo
        Dim f As Reflection.FieldInfo = Nothing
        If Not CamposDelPreset.TryGetValue(nombre, f) Then
            ' ⛔ Revienta con nombre en vez de devolver Nothing: un canal mal escrito que se saltea en silencio
            ' es un canal que el predicado del COMMIT no mira, y el sintoma aparece recien en el ESP.
            Throw New InvalidOperationException(
                "La tabla de canales nombra un campo que LooksmenuPreset no tiene: " & nombre)
        End If
        Return f
    End Function

    ''' <summary>Recorre los canales de UNA categoría.
    ''' <para>⛔ <see cref="Revert"/> NO se reescribió encima de esta tabla, y va declarado por qué: sus ramas no
    ''' son "copiar un campo" sino "el baseline si lo declara, si no EXTRAERLO DEL RECORD", con reglas propias
    ''' por canal que están documentadas una por una — el tri-estado del FTST, el centinela de NAMA, el
    ''' <c>PickSingle</c> del MWGT. Reescribirlas para que pasen por acá es justo la clase de cambio que en esta
    ''' misma ola perdió un campo cuatro veces. ⇒ la tabla es UNA, y que <c>Revert</c> escriba exactamente estos
    ''' canales y ningún otro se afirma POR MEDICIÓN: clonar un preset, correr <c>Revert</c> de la categoría y
    ''' diferenciar por reflexión contra el clon. Eso caza las dos direcciones — canal en Revert que falta en la
    ''' tabla, y canal en la tabla que Revert no toca — y una lista compartida por construcción sólo cazaba la
    ''' primera.</para></summary>
    Friend Sub PorCanal(cat As PresetCategory, isSse As Boolean, accion As AccionDeCanal)
        If accion Is Nothing Then Return
        For Each nombre In CanalesDe(cat, isSse)
            Dim f = CampoDelPreset(nombre)
            accion(nombre, Function(p) f.GetValue(p), Sub(p, v) f.SetValue(p, v))
        Next
    End Sub

    ''' <summary>⛔⛔ ¿ESTO ES AUTORIA DE LA CATEGORIA **Y** DISTINTA DE LO QUE SE VE? Las DOS preguntas
    ''' de la puerta, en su orden, en UNA sede.
    ''' <para>(1) ¿el overlay DECLARA la categoria? Un preset que no la trae (un color de pelo, un atuendo)
    ''' no authorea nada de ella. (2) ¿difiere de la BASE, o sea de lo que el usuario esta viendo?</para>
    ''' <para>⛔ Existe porque se contestaron por separado y se contradijeron: el predicado del tono se
    ''' quedo con la (2) sola y re-derivaba con CUALQUIER overlay -- `Revert` trae las capas de la plantilla
    ''' con su marca puesta, asi que siempre "difiere". Medido: 27 herederos re-derivaban mientras heredaban
    ''' (preview distinto del ESP) y 53 dejaban de seguir el ajuste de tono de su plantilla.</para>
    ''' <para>⛔ La (1) cuenta la MARCA de un canal heredable como declaracion. "Vaciar a proposito" es la
    ''' marca puesta con la lista vacia, y por VALOR eso es igual a un preset en blanco: sin esta regla,
    ''' borrar todas las partes de cabeza de un heredero no desprendia, no avisaba, y al guardar la edicion
    ''' se perdia en silencio.</para></summary>
    ''' <returns>El nombre del primer canal que difiere, o Nothing si no hay autoria distinta.</returns>
    Friend Function AutoreaDistintoDeLaBase(autoria As LooksmenuLoader.LooksmenuPreset,
                                            cat As PresetCategory,
                                            baseRecord As NPC_Data,
                                            isSse As Boolean) As String
        If autoria Is Nothing OrElse baseRecord Is Nothing Then Return Nothing
        If Not DeclaraLaCategoria(autoria, cat, isSse) Then Return Nothing
        Dim deLaBase As New LooksmenuLoader.LooksmenuPreset()
        Revert(deLaBase, cat, baseRecord, Nothing, isSse)
        Return PrimerCanalDistinto(deLaBase, autoria, cat, isSse, soloHeredables:=True)
    End Function

    ' ⛔⛔ ACA VIVIA `AutoriaTocaElTono`, con su lista de exclusion y el control de esa lista.
    ' Se fueron las tres el 09-sep: por decision del usuario el tono del cuerpo se DERIVA SIEMPRE --en el
    ' render y al grabar el ESP-- del record que corresponde, herede o no, asi que ya no hay nada que
    ' preguntar. La unica condicion que queda es del MOTOR y vive en `NpcRecordOverlay`: mientras el NPC
    ' hereda, el bit 0 le copia el QNAM de su plantilla y escribirle uno derivado seria escribir un valor
    ' que el juego pisa al cargar.
    ' ⛔ Se BORRARON en vez de dejarlas sin llamador: una funcion viva sin consumidor es la costura que
    ' el proximo lector toma por ley vigente. Con ellas se fue la ley 12 de G17, que solo las vigilaba.

    ''' <summary>⛔ La PRIMERA de las dos preguntas: ¿el overlay declara algo de esta categoria?
    ''' <para>Se distingue de un preset en blanco en algun canal heredable -- de VALOR o de MARCA. La marca
    ''' cuenta: es la unica forma de expresar "lo vacie a proposito", y por valor una lista vacia declarada
    ''' es indistinguible de una no declarada.</para></summary>
    ''' <param name="soloHeredables">True --lo de la PUERTA-- mira solo los canales cuya edicion DESPRENDE
    ''' (<see cref="PresetCategories.DesprendeElCanal"/>: los que el bit 0 copia Y los que solo llegan por el
    ''' FaceGen horneado): los otros no pueden desprender nada porque el motor ni los pisa ni deja de leerlos.
    ''' False mira TODOS.</param>
    ''' <param name="canalesQueNoCuentan">Canales que el llamador declara IRRELEVANTES para SU pregunta,
    ''' con su cita. No es un filtro de conveniencia: la pregunta del tono la contesta lo que ALIMENTA la
    ''' derivacion, y declarar algo que la derivacion no lee no es authorear el tono.</param>
    Friend Function DeclaraLaCategoria(autoria As LooksmenuLoader.LooksmenuPreset,
                                       cat As PresetCategory, isSse As Boolean,
                                       Optional soloHeredables As Boolean = True,
                                       Optional canalesQueNoCuentan As String() = Nothing) As Boolean
        If autoria Is Nothing Then Return False
        Dim fuera As New HashSet(Of String)(
            If(canalesQueNoCuentan, New String() {}), StringComparer.Ordinal)
        Dim enBlanco As New LooksmenuLoader.LooksmenuPreset()
        ' ⛔⛔ DELEGA, no reimplementa. Escribi este bucle a mano por un rato y se comia el filtro
        ' `EsCanalDeValor` que `PrimerCanalDistinto` aplica con `soloHeredables` -- o sea que la PUERTA
        ' habria pasado a contar como declaracion las banderas de contabilidad de la app. Una copia del
        ' bucle es una segunda ley que empieza igual y termina distinta.
        If PrimerCanalDistinto(enBlanco, autoria, cat, isSse, soloHeredables,
                               canalesQueNoCuentan) IsNot Nothing Then Return True
        Dim marcado As Boolean = False
        PorCanal(cat, isSse,
                 Sub(nombre, leerCanal, escribirCanal)
                     If marcado OrElse fuera.Contains(nombre) Then Return
                     If Not nombre.StartsWith("Has", StringComparison.Ordinal) Then Return
                     ' ⛔ La MISMA pregunta que `PrimerCanalDistinto`: la de la puerta (punto 1).
                     If soloHeredables AndAlso
                        Not PresetCategories.DesprendeElCanal(cat, nombre, isSse) Then Return
                     Dim v = leerCanal(autoria)
                     If TypeOf v Is Boolean AndAlso CBool(v) Then marcado = True
                 End Sub)
        Return marcado
    End Function

    ''' <summary>El NOMBRE del primer canal que difiere, o Nothing. Es la forma que necesita un gate para poder
    ''' decir QUÉ se rompió; un booleano pelado obliga a re-recorrer a mano, y esa segunda pasada es una
    ''' segunda ley.</summary>
    ''' <param name="soloHeredables">True = mirar SOLO los canales cuya edicion DESPRENDE. Lo usa la puerta
    ''' del desprendimiento: authorear un canal que el motor ni pisa ni deja de leer no puede desprender a
    ''' nadie. Ver <see cref="PresetCategories.DesprendeElCanal"/>.</param>
    Friend Function PrimerCanalDistinto(a As LooksmenuLoader.LooksmenuPreset,
                                        b As LooksmenuLoader.LooksmenuPreset,
                                        cat As PresetCategory, isSse As Boolean,
                                        Optional soloHeredables As Boolean = False,
                                        Optional canalesQueNoCuentan As String() = Nothing) As String
        If a Is Nothing OrElse b Is Nothing Then Return If(a Is b, Nothing, "(uno de los dos presets es Nothing)")
        ' ⛔ Canales que el llamador declara irrelevantes PARA SU PREGUNTA, con su cita. Viaja por aca
        ' y no por una copia del bucle: esta funcion tiene ademas el filtro `EsCanalDeValor`, y una copia
        ' se lo comeria en silencio.
        Dim fuera As New HashSet(Of String)(If(canalesQueNoCuentan, New String() {}), StringComparer.Ordinal)
        Dim distinto As String = Nothing
        PorCanal(cat, isSse, Sub(nombre, leerCanal, escribirCanal)
                                 If distinto IsNot Nothing OrElse fuera.Contains(nombre) Then Return
                                 If soloHeredables AndAlso
                                    Not PresetCategories.DesprendeElCanal(cat, nombre, isSse) Then Return
                                 ' ⛔ Y ademas tiene que ser un canal de VALOR: las banderas de
                                 ' contabilidad de la app no son campos del motor, asi que el bit 0 no
                                 ' puede pisarlas. Ver `EsCanalDeValor`.
                                 If soloHeredables AndAlso
                                    Not PresetCategories.EsCanalDeValor(nombre) Then Return
                                 If Not ComparacionPorValor.IgualPorValor(leerCanal(a), leerCanal(b)) Then
                                     distinto = nombre
                                 End If
                             End Sub)
        Return distinto
    End Function

    ''' <summary>Baseline value when the overlay declares one, else the record's (Nothing when the record
    ''' has none either = preserve).</summary>
    Private Function PickSingle(overlayValue As Single?, recordValue As Single?) As Single?
        Return If(overlayValue.HasValue, overlayValue, recordValue)
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

    ''' <summary>Copia independiente de las capas de tinte de Skyrim de un preset. Nothing entra,
    ''' Nothing sale.</summary>
    Public Function CloneSseTintLayers(src As List(Of LooksmenuLoader.CapaDeTinteSsePreset)) As List(Of LooksmenuLoader.CapaDeTinteSsePreset)
        If src Is Nothing Then Return Nothing
        Dim c As New List(Of LooksmenuLoader.CapaDeTinteSsePreset)(src.Count)
        For Each sr In src
            If sr Is Nothing Then Continue For
            c.Add(New LooksmenuLoader.CapaDeTinteSsePreset With {
                .Indice = sr.Indice, .Rojo = sr.Rojo, .Verde = sr.Verde, .Azul = sr.Azul,
                .Alfa = sr.Alfa, .Cobertura = sr.Cobertura, .Preseleccion = sr.Preseleccion})
        Next
        Return c
    End Function

End Module
