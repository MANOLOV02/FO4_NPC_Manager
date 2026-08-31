Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Aplica sobre la sombra de guardado el <see cref="NpcRecordOverride"/> que el usuario authored en
''' el NPC Editor. Vivía dentro de <c>MainForm</c> como <c>Private Sub ApplyNpcRecordOverrideToSpec</c>; se
''' mudó acá SIN cambiarle el cuerpo, por dos razones:
'''
''' <list type="number">
''' <item><b>Media ley del guardado era inalcanzable para un arnés.</b> El bit ACBS que decide cosas del
''' diálogo (0x04, "Is CharGen Face Preset") lo pisa la palabra entera que escribe este código, así que
''' cualquier gate que quisiera medir la PRECEDENCIA overlay-vs-override tenía que replicarlo — y una réplica
''' mide otra cosa. <c>HeadPartSaveGate</c> deja el delegado en Nothing por eso mismo.</item>
''' <item><b>El lado LECTOR no puede tirar.</b> <see cref="NpcTemplateMaterializer.MakeCategoryOwn"/> ya
''' devuelve <c>Unresolvable</c>; el que lanzaba era el envoltorio. Ahora esa rama se apaga por
''' <paramref name="strict"/> en vez de reescribir la función: con <c>strict:=False</c> el fallo VUELVE en
''' <c>fallo</c> y el llamador decide, que es lo que necesita el diálogo para no matar el proceso al abrirse.</item>
''' </list>
'''
''' <para>⛔ El wrapper de <c>MainForm</c> es fino a propósito: sólo enhebra los cuatro estados de la app
''' (<c>_npcRecordOverrides</c>, <c>_ctx.GetParsedNpc</c>, <c>_appliedPresets</c>, <c>ResolveLvlnPick_Friend</c>).
''' Mismo patrón que <c>MainForm.ApplyPresetOverlayToNpcData</c> sobre <see cref="NpcRecordOverlay"/>.</para></summary>
Public Module NpcRecordOverrideApplier

    ''' <summary>Aplica el override authored sobre <paramref name="npcSpec"/> (la copia de round-trip). Para
    ''' cada categoría editada que el NPC todavía HEREDA se materializa la plantilla y recién ahí se baja el
    ''' bit Use-X, para que el <c>CopyFromTemplate</c> del motor no pise la edición. No-op cuando el NPC no
    ''' tiene override.</summary>
    ''' <param name="strict">True (el camino de ESCRITURA): una categoría que no se puede materializar LANZA y
    ''' aborta el guardado — comportamiento idéntico al de antes de la mudanza. False (el camino de LECTURA):
    ''' no lanza, DEVUELVE el motivo y sigue; la sombra queda INCOMPLETA y el llamador
    ''' tiene que tratar su resultado como no resuelto, nunca como un valor.</param>
    ''' <returns>Nothing cuando todo resolvio. Con <c>strict:=False</c>, el motivo de la PRIMERA categoria que
    ''' no se pudo materializar. Se DEVUELVE en vez de tomarse <c>ByRef</c> porque esta es la forma exacta del
    ''' delegado <c>SaveContext.ApplyNpcRecordOverride</c>, y un ByRef no entra en una lambda.</returns>
    Public Function Aplicar(npcSpec As NPC_Data,
                            npcFormID As UInteger,
                            overridesPorNpc As Dictionary(Of UInteger, NpcRecordOverride),
                            getParsedNpc As Func(Of UInteger, NPC_Data),
                            hasOverlayFor As Func(Of UInteger, Boolean),
                            resolveLvlnPick As Func(Of UInteger, UInteger),
                            strict As Boolean) As String
        Dim fallo As String = Nothing
        Dim ov As NpcRecordOverride = Nothing
        If Not overridesPorNpc.TryGetValue(npcFormID, ov) OrElse ov Is Nothing Then Return Nothing
        Dim resolver As Func(Of UInteger, NPC_Data) = getParsedNpc

        ' --- Template-flag hook (materialize → clear Use-X) for each edited category the NPC still inherits. ---
        ' Every supported category is fully materialized before its Use-X bit is cleared.
        If ov.BaseDataChanged Then MakeCategoryOwnForSave(npcSpec, NPC_TemplateCategory.BaseData, resolver, resolveLvlnPick, strict, fallo)
        If ov.StatsChanged Then MakeCategoryOwnForSave(npcSpec, NPC_TemplateCategory.Stats, resolver, resolveLvlnPick, strict, fallo)
        If ov.Keywords IsNot Nothing Then MakeCategoryOwnForSave(npcSpec, NPC_TemplateCategory.Keywords, resolver, resolveLvlnPick, strict, fallo)
        If ov.Factions IsNot Nothing Then MakeCategoryOwnForSave(npcSpec, NPC_TemplateCategory.Factions, resolver, resolveLvlnPick, strict, fallo)
        If ov.Inventory IsNot Nothing Then MakeCategoryOwnForSave(npcSpec, NPC_TemplateCategory.Inventory, resolver, resolveLvlnPick, strict, fallo)
        ' Actor Effects (SPLO) belong to the SpellList category; Perks (PRKR) and Properties (PRPS) have no
        ' template category (engine copies them under other buckets), so they are just replaced below.
        If ov.ActorEffects IsNot Nothing Then MakeCategoryOwnForSave(npcSpec, NPC_TemplateCategory.SpellList, resolver, resolveLvlnPick, strict, fallo)
        ' Traits (Race/Voice/OBTS): materialize the template Traits set + clear Use-Traits. When a LooksMenu
        ' overlay is applied for this NPC, skip the overlay-OWNED appearance fields (skin/hair/headparts/morphs/
        ' tints/weight) so the overlay's already-applied values win — the template still fills the non-overlaid,
        ' non-edited Traits fields (Race/DeathItem/FarAwayModel/Height/OBTS), so nothing falls back to the record's
        ' empty own-value. The user's Race/Voice/OBTS edits are written below, on top of the materialized set.
        If ov.TraitsChanged AndAlso NpcTemplateHelpers.HasTemplateFlag(npcSpec.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.Traits) Then
            Dim hasOverlay = hasOverlayFor IsNot Nothing AndAlso hasOverlayFor(npcFormID)
            ' resolveLvlnPick: sin esto la cadena que termina en un LVLN era irresoluble y el bit se bajaba
            ' igual, dejando al NPC sin cara. Ver NpcTemplateMaterializer.ResolveCategorySource.
            MakeCategoryOwnForSave(npcSpec, NPC_TemplateCategory.Traits, resolver, resolveLvlnPick, strict, fallo, skipOverlayOwned:=hasOverlay)
        End If

        ' --- Scalars. ---
        ' Escribir un campo CREA su subrecord; una referencia en cero significa "ninguna" y por eso se
        ' saca en vez de escribirse.
        If ov.FullName IsNot Nothing Then
            If ov.FullName.Length > 0 OrElse npcSpec.Record.NamePresente Then
                npcSpec.Record.Name = ov.FullName
            Else
                npcSpec.Record.QuitarSubrecord("FULL")
            End If
        End If
        If ov.ShortName IsNot Nothing Then
            If ov.ShortName.Length > 0 OrElse npcSpec.Record.ShortNamePresente Then
                npcSpec.Record.ShortName = ov.ShortName
            Else
                npcSpec.Record.QuitarSubrecord("SHRT")
            End If
        End If
        ' ⛔ RNAM NO pasa por la ley del «sin valor SACA el campo»: xEdit lo declara
        ' `wbFormIDCk(RNAM, 'Race', [RACE]).SetRequired` dentro de `wbRecord(NPC_)` en LOS DOS juegos
        ' (wbDefinitionsFO4.pas:10370, dentro del record que abre en :10286; wbDefinitionsTES5.pas:8355,
        ' record en :8290). Un NPC_ sin RNAM es ilegal, así que una caja de raza vacía no es «borrar la
        ' raza» sino entrada INVÁLIDA — la misma excepción declarada que ya tienen ARMA y ARMO. La ley
        ' lo dice en su propio docstring: «NO va para campos REQUERIDOS de hecho, como el RNAM».
        ' ⚠️ CAMBIO DE SEMANTICA declarado: antes un 0 acá SACABA el RNAM; ahora es un no-op y el
        ' NPC conserva su raza. Es el sentido seguro —RNAM es requerido— y además ALINEA el guardado
        ' con el render, que ya trataba el 0 como «sin override». Hoy no llega un 0: el picker de raza
        ' abre con `allowNull:=False` y el override se escribe desde esa misma caja.
        If ov.RaceFormID.HasValue Then
            Canon.CanonInterpretacion.PonerReferenciaRequerida(ov.RaceFormID.Value, Sub(x) npcSpec.Record.Race = x)
        End If
        If ov.VoiceFormID.HasValue Then Canon.CanonInterpretacion.PonerReferenciaOSacarSubrecord(npcSpec.Record, ov.VoiceFormID.Value, "VTCK", Sub(v) npcSpec.Record.Voice = v)
        If ov.ClassFormID.HasValue Then Canon.CanonInterpretacion.PonerReferenciaOSacarSubrecord(npcSpec.Record, ov.ClassFormID.Value, "CNAM", Sub(v) npcSpec.Record.[Class] = v)
        If ov.CombatStyleFormID.HasValue Then Canon.CanonInterpretacion.PonerReferenciaOSacarSubrecord(npcSpec.Record, ov.CombatStyleFormID.Value, "ZNAM", Sub(v) npcSpec.Record.CombatStyle = v)
        ' NAM6 / NAM4 (Height). Written AFTER the Traits materialization above on purpose: height is a
        ' Traits-category field (MaterializeTraits copies it unconditionally), so on a Traits-inheriting NPC
        ' the materializer first fills the template's height and this then overwrites it with the user's.
        ' The editor latches TraitsChanged when it sets these, which is what clears the Use-Traits flag —
        ' without that the engine's CopyFromTemplate would overwrite the edit at runtime.
        ' Has* is forced True because a value here means the user authored one; NAM4 is FO4-only and the
        ' SSE editor path never sets it, so Skyrim records keep emitting no NAM4.
        If ov.HeightMin.HasValue Then npcSpec.Record.PonerAltura(ov.HeightMin.Value)
        If ov.HeightMax.HasValue Then npcSpec.Record.PonerAlturaMaxima(ov.HeightMax.Value)

        ' --- ACBS (banderas, nivel, rango de calculo, disposicion y los desplazamientos de Skyrim). ---
        ' ⛔ El bit 0x04 ("Is CharGen Face Preset") NO viaja en esta palabra, y por eso se preserva de la
        ' sombra en vez de dejarse pisar. El NPC Editor no expone ese bit a proposito y lo arrastra desde su
        ' snapshot, que sale del record CRUDO — o sea SIN lo que el overlay de Edit Face acaba de poner. Como
        ' aca se escribia la palabra ENTERA, tocar cualquier casilla del editor BORRABA el tilde del usuario y
        ' el ESP salia sin la bandera, en silencio. Ahora el overlay es el UNICO dueno del 0x04 y el editor
        ' manda en todos los demas bits.
        ' Sin preset el resultado no cambia: la sombra trae el bit del crudo, que es el mismo que ov.AcbsFlags.
        If ov.AcbsFlags.HasValue Then
            Dim chargenDelOverlay = npcSpec.Record.ConfigurationFlagsIsCharGenFacePreset
            npcSpec.Record.ConfigurationFlags = ov.AcbsFlags.Value
            npcSpec.Record.ConfigurationFlagsIsCharGenFacePreset = chargenDelOverlay
        End If
        If ov.Level.HasValue Then npcSpec.Record.PonerNivelDeConfiguracion(ov.Level.Value)
        If ov.CalcMinLevel.HasValue Then npcSpec.Record.ConfigurationCalcMinLevel = ov.CalcMinLevel.Value
        If ov.CalcMaxLevel.HasValue Then npcSpec.Record.ConfigurationCalcMaxLevel = ov.CalcMaxLevel.Value
        If ov.DispositionBase.HasValue Then npcSpec.Record.PonerBaseDeDisposicion(ov.DispositionBase.Value)
        If ov.TemplateFlags.HasValue Then npcSpec.Record.ConfigurationTemplateFlags = ov.TemplateFlags.Value
        Dim ovFo4 = TryCast(npcSpec.Record, Canon.NpcFO4)
        If ovFo4 IsNot Nothing AndAlso ov.XpValueOffset.HasValue Then ovFo4.ConfigurationXPValueOffset = ov.XpValueOffset.Value
        Dim ovSse = TryCast(npcSpec.Record, Canon.NpcSSE)
        If ovSse IsNot Nothing Then
            If ov.MagickaOffset.HasValue Then ovSse.ConfigurationMagickaOffset = ov.MagickaOffset.Value
            If ov.StaminaOffset.HasValue Then ovSse.ConfigurationStaminaOffset = ov.StaminaOffset.Value
            If ov.SpeedMultiplier.HasValue Then ovSse.ConfigurationSpeedMultiplier = ov.SpeedMultiplier.Value
            If ov.HealthOffset.HasValue Then ovSse.ConfigurationHealthOffset = ov.HealthOffset.Value
        End If

        ' --- DNAM de Skyrim. El override lleva el record cuyo DNAM es la edicion; se copia el subrecord
        ' entero, asi que los campos que nadie toco -relleno sin usar incluido- llegan tal cual. ---
        If ov.SsePlayerSkills IsNot Nothing Then npcSpec.Record.CopiarSubrecord(ov.SsePlayerSkills, "DNAM")

        ' --- Listas. ---
        If ov.Keywords IsNot Nothing Then npcSpec.Record.PonerPalabrasClave(ov.Keywords)
        If ov.AttachParentSlots IsNot Nothing Then npcSpec.Record.PonerRanurasDeEnganche(ov.AttachParentSlots)
        If ov.Factions IsNot Nothing Then npcSpec.Record.PonerFacciones(ov.Factions)
        If ov.Inventory IsNot Nothing Then npcSpec.Record.PonerInventario(ov.Inventory)
        If ov.Perks IsNot Nothing Then npcSpec.Record.PonerVentajas(ov.Perks)
        If ov.ActorEffects IsNot Nothing Then npcSpec.Record.PonerEfectosDeActor(ov.ActorEffects)
        If ov.Properties IsNot Nothing Then npcSpec.Record.PonerPropiedades(ov.Properties)
        If ov.ObjectTemplateCombinations IsNot Nothing Then npcSpec.Record.ReemplazarCombinations(ov.ObjectTemplateCombinations)
        Return fallo
    End Function

    ''' <summary>Materializa la categoría y baja su bit Use-X. Una categoría irresoluble es un ABORTO en el
    ''' camino de escritura (si se bajara el bit igual, la plantilla dejaría de llenar el campo y el NPC se
    ''' quedaría con el valor propio vacío); en el de lectura sólo se anota, porque ahí nadie escribe nada.
    ''' <para>El primer fallo gana: <paramref name="fallo"/> no se pisa, así el motivo que ve el usuario es el
    ''' de la categoría que se rompió primero y no el de la última que se probó.</para></summary>
    Private Sub MakeCategoryOwnForSave(npcSpec As NPC_Data,
                                       category As NPC_TemplateCategory,
                                       resolver As Func(Of UInteger, NPC_Data),
                                       resolveLvlnPick As Func(Of UInteger, UInteger),
                                       strict As Boolean,
                                       ByRef fallo As String,
                                       Optional skipOverlayOwned As Boolean = False)
        Dim outcome = NpcTemplateMaterializer.MakeCategoryOwn(npcSpec, category, resolver,
                                                               skipOverlayOwned:=skipOverlayOwned,
                                                               resolveLvlnPick:=resolveLvlnPick)
        If outcome <> NpcTemplateMaterializer.MaterializeOutcome.Unresolvable AndAlso
           outcome <> NpcTemplateMaterializer.MaterializeOutcome.UnsupportedCategory Then Return

        Dim motivo = $"NPC 0x{npcSpec.FormID:X8}: cannot materialize {NpcManagerFormat.GetTemplateCategoryLabel(category)}"
        If strict Then
            Throw New InvalidOperationException(motivo & "; save aborted so the template cannot overwrite the edit.")
        End If
        If fallo Is Nothing Then fallo = motivo
    End Sub

End Module
