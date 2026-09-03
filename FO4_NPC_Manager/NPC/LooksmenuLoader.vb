Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports FO4_Base_Library

''' <summary>Parser of the LooksMenu CharGen preset JSON format. Schema verified against
''' Script extenders, Racemenu y Looksmenu/F4SEPlugins/f4ee/CharGenInterface.cpp:49-256 (SavePreset) and 259-620 (LoadPreset).
'''
''' Maps every JSON field that has a vanilla NPC_ subrecord equivalent. The F4SE-only Overlays
''' field (body tattoos) is now fully parsed + round-tripped (see <see cref="LooksmenuPreset.Overlays"/>);
''' it is also still surfaced as a raw count via <see cref="LooksmenuPreset.UnsupportedCounts"/> for the
''' existing Load warning UI. BodyMorphs (BodySlide sliders) and Skin (LM skin template) are likewise
''' parsed. See memory/project_npc_looksmenu_pending.md for the deferral rationale on render wiring.</summary>
Public Module LooksmenuLoader

    ''' <summary>Una capa de tinte de cara tal como la trae un PRESET de autoria: el .json de LooksMenu,
    ''' el .jslot de RaceMenu, o lo que el editor de cara tiene en mano mientras se edita.
    ''' <para>No es un record ni un espejo de uno. Mientras el preset esta abierto todavia no hay donde
    ''' escribir la capa; recien al aplicarlo el overlay la vuelca al TETI/TEND del record -esa es la
    ''' operacion- y de ahi en mas el dueno del dato es el record.</para></summary>
    Public Class CapaDeTintePreset
        ''' <summary>0 = mascara, 1 = paleta, 2 = conjunto de texturas (f4se GameCustomization.h:172-174). Desde
        ''' el .json de LooksMenu llega el "Type" tal cual lo convierte jsoncpp (`asInt`, CharGenInterface.cpp:506),
        ''' truncado a 16 bits; un Type fuera de {0,1,2} es un HUECO: `CreateCharacterTintEntry` es una funcion del
        ''' juego que no leimos.</summary>
        Public Discriminator As UShort
        ''' <summary>Indice de la opcion de tinte de la RACE que esta capa realiza. Desde el .json es la clave del
        ''' miembro de "Tints" convertida con `sscanf_s("%X")` y truncada a UInt16, que es lo que ve
        ''' `GetTemplateByIndex(UInt16)` (CharGenInterface.cpp:503-512).</summary>
        Public Index As UShort
        ''' <summary>Intensidad de la capa. En el motor es un UInt8 (`Entry.percent`, GameCustomization.h:179)
        ''' que LoadPreset llena con `Percent.asInt()` sin clamp (CharGenInterface.cpp:529): desde el .json queda
        ''' 0..255 (byte bajo). LooksMenu guarda 0..100.</summary>
        Public Value As Integer
        ''' <summary>Color final aplicado. Solo las capas de paleta lo llevan.</summary>
        Public Color As System.Drawing.Color = System.Drawing.Color.Empty
        ''' <summary>Posicion en la paleta de colores de la opcion, o -1 cuando el color es propio y no
        ''' sale de la paleta.</summary>
        Public TemplateColorIndex As Integer = -1
        ''' <summary>True cuando "Color", "ColorID" o "Percent" del .json NO son convertibles por jsoncpp (string,
        ''' array, objeto; "Color" ademas negativo). El motor recien los lee DESPUES de encontrar la plantilla de la
        ''' RACE (CharGenInterface.cpp:512-529), asi que la falla solo cuenta si la RACE tiene la opcion: en ese
        ''' caso el `catch` de :563 corta el canal entero y el NPC queda SIN tints (el contenedor ya se limpio en
        ''' :495-500). Lo evalua <see cref="NpcRecordOverlay"/>, que es quien conoce la raza; el parser no.</summary>
        Public ConversionFallida As Boolean = False
    End Class

    ''' <summary>Una capa de tinte de cara de Skyrim traida por un preset de autoria (.jslot de RaceMenu
    ''' o la edicion viva del editor de cara). Misma condicion que <see cref="CapaDeTintePreset"/>: no es
    ''' un record, y el overlay la vuelca al del NPC al aplicar el preset.
    ''' <para>Los cinco datos van por separado y pueden faltar porque en el archivo son cuatro campos
    ''' independientes: una capa puede declarar indice y no color, o color y no cobertura. Colapsarlos a
    ''' valores con default haria que un preset le AGREGUE al record campos que su fuente no traia.</para></summary>
    Public Class CapaDeTinteSsePreset
        ''' <summary>Indice de la capa de tinte de la RACE.</summary>
        Public Indice As UShort?
        Public Rojo As Byte?
        Public Verde As Byte?
        Public Azul As Byte?
        Public Alfa As Byte?
        ''' <summary>Cobertura tal como la guarda el archivo: 0..100.</summary>
        Public Cobertura As UInteger?
        ''' <summary>Preseleccion de la paleta de la RACE. -1 = color propio, elegido a mano.</summary>
        Public Preseleccion As Short?
    End Class

    ''' <summary>Output of <see cref="ParseFile"/>. All vanilla-mappable fields are pre-resolved to
    ''' global FormIDs (via <see cref="PluginManager.ResolveReferencedFormID"/>) so the caller can
    ''' just assign them onto an NPC_Data without any string-parsing logic.</summary>
    Public Class LooksmenuPreset
        Public SourcePath As String = ""
        Public Gender As Byte = 0   ' 0=Male, 1=Female
        ''' <summary>El motor pudo leer "Gender" (CharGenInterface.cpp:301-309: `root["Gender"].asUInt()` a un
        ''' UInt8; ausente o null vale 0). False = jsoncpp lanzo (negativo, string, array, objeto): el motor toma
        ''' el genero del NPC y el preset PASA el filtro de genero. Desde <see cref="ParseFile"/>; los demas
        ''' productores lo dejan en su valor.</summary>
        Public HasGender As Boolean = False
        Public HeadPartFormIDs As New List(Of UInteger)
        ''' <summary>HeadPart entries from the JSON that ResolveFormIdentifier couldn't resolve to a
        ''' loaded plugin (returned 0). Kept as raw "Plugin.esp|HEX" strings so the caller can log
        ''' which plugins the preset depends on but the user doesn't have active. Almost always
        ''' the cause when a preset's pelo/ojos visually don't apply: the HDPT lives in a
        ''' presets-mod ESP that isn't in Plugins.txt.</summary>
        Public UnresolvedHeadParts As New List(Of String)
        ''' <summary>SSE-ONLY companion to <see cref="UnresolvedHeadParts"/>: the same unresolved
        ''' <c>headParts</c> entries from a <c>.jslot</c> (whose <c>formIdentifier</c> didn't resolve
        ''' against the current load order), kept VERBATIM as the parsed
        ''' <see cref="RaceMenuJslot.JslotHeadPart"/> (FormId + FormIdentifier + Type) rather than just
        ''' the diagnostic string. This lets <see cref="RaceMenuPresetMapper.ToJslot"/> re-emit them
        ''' without loss when a preset loaded while the owning mod was absent is saved back — otherwise a
        ''' load→save round-trip would silently drop those head parts from the <c>.jslot</c>.
        ''' <see cref="UnresolvedHeadParts"/> (List(Of String)) stays the UI/log list, shared with the
        ''' FO4 path. Nothing populates this on FO4 (FO4 loads from f4ee JSON, not .jslot).</summary>
        Public SseUnresolvedHeadParts As New List(Of RaceMenuJslot.JslotHeadPart)
        ''' <summary>SSE-ONLY: head parts del <c>.jslot</c> que SÍ resolvieron contra el load order pero que el
        ''' motor NO aplica a ESTE actor: skee64 <c>ApplyPresetData</c> (PresetInterface.cpp:164-175) sólo llama
        ''' <c>ChangeHeadPart</c> si el flag de sexo del HDPT (DATA bit 1 Male / bit 2 Female) coincide con el
        ''' del NPC Y el HDPT tiene <c>validRaces</c> (RNAM ≠ 0, la FLST existe) Y esa FLST lista la raza del
        ''' NPC (<c>BGSListForm::Visit</c>: entradas directas + las agregadas por script). Una parte que falla
        ''' NO forma parte del estado producido (<see cref="HeadPartFormIDs"/> = lo que el motor deja puesto)
        ''' y <c>SavePreset</c> (:342-364 arma <c>partList</c> con <c>npc->headparts</c>, :409-417 lo emite) tampoco la re-exportaría. Se guardan acá
        ''' para que el gate de raza del browser (<see cref="HeadPartResolver.IsPresetCompatibleWithRace"/>) y
        ''' el reporte de compatibilidad sigan viendo lo que el ARCHIVO declara. Sólo la puebla
        ''' <see cref="RaceMenuPresetMapper.ApplyJslotToPreset"/> con raza conocida; FO4 no la usa.</summary>
        Public SseHeadPartsFiltradasPorMotor As New List(Of UInteger)
        Public HairColorFormID As UInteger
        ''' <summary>El identificador crudo ("Plugin.esp|FORMID") del "HairColor" del preset cuando NO resolvió
        ''' contra el load order — o sea, cuando el mod que trae el color no está instalado. Análogo a
        ''' <see cref="UnresolvedHeadParts"/>. Sin esto, <see cref="HairColorFormID"/> queda en 0 y el caso se
        ''' vuelve indistinguible de "el preset no trae color de pelo": el auditor de compatibilidad lo
        ''' saltaba en silencio. "" = resolvió, o el preset no declara HairColor.</summary>
        Public UnresolvedHairColor As String = ""
        ''' <summary>Los identificadores crudos ("Plugin.esp|FORMID") de los TRES overrides <c>_npcm_</c> del
        ''' preset —skin (WNAM), outfit por defecto (DOFT) y outfit de dormir (SOFT)— cuando NO resolvieron
        ''' contra el orden de carga. "" = resolvió, o el preset no declara ese campo.
        ''' <para>⛔ <b>SIN ESTO EL CASO ERA MUDO EN RELEASE, y es el modo de fallar más común de un preset
        ''' ajeno.</b> El campo que no resuelve queda en <c>Nothing</c> —«preservar», que es lo correcto: f4ee
        ''' saltea el form que no resuelve—, pero entonces el auditor de compatibilidad no lo ve, porque sólo
        ''' mira los que TIENEN valor (<c>HasValue AndAlso &lt;&gt; 0</c>). El único aviso era un
        ''' <c>Logger</c>, y el logger está apagado por construcción en Release: el usuario cargaba un preset,
        ''' el NPC salía con SU piel y SU ropa en vez de las del preset, y nada se lo decía.</para>
        ''' <para>Mismo patrón que <see cref="UnresolvedHeadParts"/> y <see cref="UnresolvedHairColor"/>: se
        ''' PRESERVA el identificador crudo acá y el informe lo nombra
        ''' (<c>PresetCompatibilityReport.AuditFormIdFields</c>), que es el canal que el usuario sí ve.</para></summary>
        Public UnresolvedSkin As String = ""
        Public UnresolvedDefaultOutfit As String = ""
        Public UnresolvedSleepOutfit As String = ""
        ''' <summary>SSE-ONLY face texture set (NPC_.FTST) override. THREE states, same shape as
        ''' <see cref="SkinFormIDOverride"/> / <see cref="DefaultOutfitFormIDOverride"/> / <see cref="SleepOutfitFormIDOverride"/>:
        ''' <list type="bullet">
        ''' <item>Nothing    → no override: preserve the raw NPC.FTST verbatim (Edit Face's "Use record default").</item>
        ''' <item>value &lt;&gt; 0 → explicit face TXST: a loaded .jslot's actor.headTexture (skee64 sets
        '''       npc-&gt;headData-&gt;headTexture, PresetInterface.cpp:158-160) or Edit Face's "Change…" picker.</item>
        ''' <item>value = 0  → EXPLICIT CLEAR: emit NO FTST subrecord, so the head falls back to the RACE
        '''       DefaultFaceTexture[gender] and, failing that, to the head part's own HDPT.TNAM.</item>
        ''' </list>
        ''' The render consumes it as state.ExplicitHeadTextureFormID (NpcMaterialResolver.ResolveTextureSet).
        ''' SSE-only; Nothing on FO4, where the face override travels through the LooksMenu skin template instead.
        ''' <para>El estado "clear explícito" NO sobrevive un round-trip por `.jslot`: el formato de RaceMenu no
        ''' distingue "sin clave" de "clave nula" (RaceMenuJslot colapsa null/"" en ""), y su motor NUNCA limpia el
        ''' FTST — skee64 PresetInterface.cpp:147 sólo asigna dentro de `if (presetData-&gt;headTexture)`. Guardar
        ''' como preset RaceMenu y recargarlo degrada `0` → `Nothing` (= preservar). Es inherente al formato ajeno y
        ''' NO se workaroundea: la casa del clear es el ESP, no el .jslot.</para></summary>
        Public SseHeadTextureFormIDOverride As UInteger?
        ''' <summary>SSE-ONLY RaceMenu absolute hair tint from a loaded .jslot's actor.hairColor (packed 0xRRGGBB).
        ''' skee writes it straight onto the hair shape's BSLightingShaderMaterialHairTint.tintColor (unpacked /255,
        ''' ×2, PresetInterface.cpp:112-116), taking precedence over the NPC's CLFM/HCLF colour. Nothing = the preset
        ''' carried no hairColor → render falls back to the CLFM. SSE-only; Nothing on FO4.</summary>
        Public SseHairColorRgb As Integer?
        ''' <summary>Ajuste manual del SKIN TONE del cuerpo (QNAM), autorado en Edit Body -> "Skin Tint Adjustment".
        ''' Nothing = sin ajuste. Es dato INTERNO del app (ni LooksMenu ni RaceMenu tienen un campo asi): viaja por
        ''' el overlay y se persiste en el sidecar .bssliders, no en el .jslot. Lo consumen el tono del CUERPO en el
        ''' render y el QNAM que se escribe en el ESP / se hornea, NUNCA la resolucion que lee la CARA.</summary>
        Public SkinToneOffset As SkinToneQnamOffset = Nothing
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
        ''' Key = FMRI region index, Value = 8 floats (the FMRS values for that region). Desde el .json
        ''' <see cref="ParseFile"/> deja SIEMPRE 8 floats por region: el motor lee `regions[key][i]` para i = 0..7
        ''' (CharGenInterface.cpp:418-421) y jsoncpp devuelve null ⇒ 0.0 para los indices que faltan.</summary>
        Public FaceBoneRegions As New Dictionary(Of UInteger, Single())
        ''' <summary>FMIN. En el motor (CharGenInterface.cpp:462-474) `Morphs.Intensity` ausente vale 1.0 y se
        ''' escribe SIEMPRE (non-player), asi que "no esta en el .json" equivale a "1.0 explicito", no a
        ''' "preservar". <see cref="HasFacialMorphIntensity"/> dice si el motor lo escribiria.</summary>
        Public FacialMorphIntensity As Single = 1.0F
        ''' <summary>El motor escribiria FMIN (CharGenInterface.cpp:462-474). Desde <see cref="ParseFile"/>: True
        ''' salvo que `Morphs` no sea objeto (:373 lanza y :466 `isMember` tambien) o que `Morphs.Intensity`
        ''' sea string/array/objeto (`asFloat` lanza ⇒ se preserva). Ausente ⇒ True con 1.0; null ⇒ True con
        ''' 0.0; bool ⇒ 1/0. Los productores que representan el estado completo (snapshot, editor) lo ponen en
        ''' True. False = el overlay deja el FMIN del record.</summary>
        Public HasFacialMorphIntensity As Boolean = False
        ''' <summary>Tint layers reordered by TintOrder[] if the JSON provided one. Each entry is a
        ''' <see cref="CapaDeTintePreset"/> with Discriminator/Index/Value/Color/TemplateColorIndex filled
        ''' desde el JSON.</summary>
        Public FaceTintLayers As New List(Of CapaDeTintePreset)

        ''' <summary>Presence flags de los canales que el overlay REEMPLAZA. True = "el motor escribiria este
        ''' canal con esta lista (aunque este vacia)". False = "el motor lo dejaria como esta":
        ''' ApplyPresetOverlayToNpcData preserva el record y PresetCategoryFilter preserva el baseline.
        '''
        ''' Sin la distincion Count=0 seria ambiguo: "el usuario borro todo y quiere override vacio" (wipe) o
        ''' "el preset nunca trajo el canal" (preservar). El editor y el Save necesitan el wipe; el Load
        ''' necesita lo que haria LoadPreset.
        '''
        ''' LA LEY (f4ee CharGenInterface.cpp LoadPreset :269-645, cada bloque con su propio try/catch): el
        ''' loader produce, por canal, el estado del motor DESPUES de LoadPreset. Has* = True cuando el motor
        ''' escribe el canal; False solo cuando jsoncpp LANZA dentro del bloque (clave con tipo no convertible)
        ''' y el motor deja el valor anterior. En particular ausente ⇒ jsoncpp devuelve null ⇒ el motor SI
        ''' escribe (vacio): Tints (:489-500), Morphs.Presets (:433-460), Morphs.Regions (:399-429),
        ''' Morphs.Values (:373-397), Overlays (:587 RemoveAll incondicional). Excepciones con cita:
        ''' BodyMorphs (:568-577) limpia solo con miembros o con null explicito, `{}`/ausente preserva; y
        ''' HeadParts (:318-331 limpia SIEMPRE) es la decision D-HeadParts pendiente del usuario, por eso
        ''' <see cref="HasHeadPartFormIDs"/> sigue siendo True solo cuando la clave esta.
        '''
        ''' Setters: ParseFile segun la ley. BuildPresetFromState pone todo True (el snapshot es completo).
        ''' Edit forms ponen True al sembrar (el editor "reclama" el canal). Paste toma lo tickeado.
        '''
        ''' Reader: ApplyPresetOverlayToNpcData lee Has* (no Count). Count=0+Has=True ⇒ wipe.
        ''' Count=0+Has=False ⇒ preserva el record.</summary>
        Public HasFaceTintLayers As Boolean = False
        Public HasChargenFaceMorphs As Boolean = False
        ' SSE (Skyrim) head morphs: NAM9 (hasta 18 floats editables) + NAMA (hasta 4 type uints). Con HasSseMorphs
        ' el overlay los escribe POSICIONALMENTE sobre el record de la sombra —índice a índice, sólo `Length`
        ' entradas— porque eso hace skee64 (ApplyPresetData :182-192 `presets[i] = value` / `option[i] = value`
        ' por cada entrada del archivo): un .jslot con 5 morphs pisa [0..4] y deja [5..17] como estaban. Los
        ' productores dimensionan los arrays con EXACTAMENTE lo cubierto (mapper) o los 18/4 completos (snapshot,
        ' editor, revert). El render + bake leen el record resultante. FO4 los deja sin usar.
        Public SseNam9 As Single() = Nothing
        Public SseNama As UInteger() = Nothing
        Public HasSseMorphs As Boolean = False
        ''' <summary>El slot 18 del NAM9 (VampireMorph), que NO entra en <see cref="SseNam9"/>: el NAM9 son 19
        ''' floats (76 bytes) y el modelo editable dimensiona 18 sliders. Nothing = no se conoce.
        ''' <para>Existe porque <c>ToJslot</c> lo escribía con una CONSTANTE (FLT_MAX = "no es vampiro"), así
        ''' que un load→save le pisaba el valor real. Medido: 1 de los 48 presets del usuario trae 0 ahí, y el
        ''' render propio SÍ lee ese slot (<see cref="NpcMorphResolver.BuildFaceMorphPlanFromNam9"/>, slot 18).
        ''' Sin conocerlo se sigue emitiendo el
        ''' centinela, que es el default correcto.</para></summary>
        Public SseVampireMorph As Single? = Nothing
        ' SSE (Skyrim) face tints: las capas editadas. Con HasSseTints el overlay las vuelca al record de
        ' la sombra, asi el compositor (render + horneado) usa la edicion y el Save ESP la emite. En Fallout 4
        ' queda sin usar (sus tints viven en FaceTintLayers).
        Public SseTintLayers As List(Of CapaDeTinteSsePreset) = Nothing
        Public HasSseTints As Boolean = False
        ' RaceMenu-only per-layer CUSTOM tint mask texture (index → texture path). PresetInterface.cpp:203 does
        ' tintMask->texture->str = tint.name — i.e. a .jslot tint can OVERRIDE the RACE layer's mask texture by
        ' index to an arbitrary path (shared warpaint/tattoo presets). No vanilla NPC record home (TINI/TINC/TINV/
        ' TIAS carry no path) → carried here, composited by SseFaceTintComposer (render + bake), persisted in the
        ' .bssliders sidecar. Empty/absent index = use the RACE layer's own TINT path. FO4 leaves unset.
        Public SseTintTexOverride As Dictionary(Of Integer, String) = Nothing
        ' RaceMenu .jslot sidecar (per-vertex sculpt + NiOverride custom morphs). Applied on top of NAM9/NAMA in
        ' render + bake; saved to the .jslot alongside the ESP. FO4 leaves unset.
        Public SseSculptHead As List(Of NPC_SculptVert) = Nothing
        ' All per-shape sculpt blocks (head + brows + eyes + mouth), each tagged with its Host chargen tri.
        ' Render/bake route by Host so brows/eyes/mouth get their sculpt too (SseSculptHead is head-only).
        Public SseSculptParts As List(Of NPC_SculptPart) = Nothing
        Public SseCustomMorphs As List(Of NPC_CustomMorph) = Nothing
        ' SSE (Skyrim) vanilla body weight (NPC.NAM7). Nothing = preserve raw NPC.NAM7; a value overrides
        ' it (0..100). The overlay writes BitConverter.GetBytes(SseWeight) into shadow.Nam7Raw so the SSE
        ' body-weight (_0/_1) LERP resolver reads the edited weight live and Save ESP persists it. Editor-only
        ' (Edit Body SSE weight slider) — not serialized to the FO4 f4ee JSON; the .jslot carries it as
        ' actor.weight. FO4 leaves this unset (NAM7 is Unused there).
        Public SseWeight As Single? = Nothing
        Public HasBodyMorphValues As Boolean = False
        Public HasFaceBoneRegions As Boolean = False
        ''' <summary>HeadParts presence — same semantics as the four list flags above.
        ''' Without this, an empty HeadPartFormIDs.Count couldn't distinguish "not in this preset"
        ''' from "user wiped all head parts". Save ESP needs the latter to emit zero PNAM
        ''' subrecords (engine then falls back to RACE.HEAD only).</summary>
        Public HasHeadPartFormIDs As Boolean = False

        ''' <summary>True when <see cref="HeadPartFormIDs"/> is a COMPLETE superset seeded from the
        ''' raw NPC.PNAM including its IsExtraPart addons (lashes/AO/wet/hairlines) — i.e. the list is
        ''' faithful and authoritative, and any raw part it OMITS was deleted on purpose.
        '''
        ''' Only Edit Face produces such a list (EditFace_Form.SeedFromOverlayOrRaw seeds from the raw
        ''' PNAM without the IsExtraPart filter). Filtered sources leave this False: LooksMenu JSON,
        ''' SavePreset/BuildPresetFromState and Paste all DROP IsExtraPart addons, so their list is a
        ''' subset that still needs the raw record unioned back in at save to restore those extras.
        '''
        ''' Reader: NpcOverrideSaver Phase 1c. True ⇒ do NOT union the raw record's head parts (the
        ''' preset already carries the extras it keeps, and a raw union would resurrect user-deleted
        ''' freestanding Misc parts — the orphan-hairline bug — since a raw Misc has no PartType slot
        ''' to be overridden and always re-accumulates). False ⇒ raw ∪ preset (long-standing behaviour).</summary>
        Public HeadPartFormIDsIncludeRawExtras As Boolean = False

        ''' <summary>Raw NPC.PNAM head parts to SUPPRESS from the save-time raw union: the orphaned
        ''' standalone Misc (hairline, orphaned eye lashes, …) left behind when this preset/paste REPLACED
        ''' a main-type parent. Computed at APPLY time — Load LooksMenu/RaceMenu + Copy/Paste — via
        ''' <see cref="HeadPartResolver.ComputeReplacedParentOrphanMisc"/>, so the decision lives where
        ''' the hair swap happens; NpcOverrideSaver Phase 1c merely skips these FormIDs when unioning the
        ''' raw record. Gated on an actual REPLACEMENT (a parent the preset left untouched suppresses
        ''' nothing) and the cascade keeps extras a surviving parent still claims — so no Cait-class lash loss.
        ''' Empty for EditFace (a complete superset — <see cref="HeadPartFormIDsIncludeRawExtras"/> — that
        ''' already reflects deletions) and for any apply that didn't replace a parent. Does NOT affect
        ''' render: the render reads <see cref="HeadPartFormIDs"/> directly, not this set.</summary>
        Public SuppressedRawHeadPartFormIDs As New HashSet(Of UInteger)

        ''' <summary>BodySlide vertex morph sliders ("BodyMorphs" in JSON). Dict keyed by slider
        ''' name (e.g. "BigBelly", "ChubbyButt"); the resolver looks each name up in the PIRT .tri
        ''' of every shape and applies wherever defined. Empty = no overlay; the NPC's body renders
        ''' with no BodySlide morphs. Schema: Script extenders, Racemenu y Looksmenu/F4SEPlugins/f4ee/CharGenInterface.cpp:204-215
        ''' (Save) and 560-570 (Load). NOT a vanilla record — lives only in the JSON.</summary>
        Public BodyMorphSliders As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>El motor escribiria los sliders (CharGenInterface.cpp:568-577): `RemoveMorphsByKeyword` (:573) solo con
        ''' `members.size() > 0 || (isMember("BodyMorphs") && isNull())`. Asi que `{}` o ausente ⇒ False
        ''' (preserva); null explicito o con miembros ⇒ True; "BodyMorphs" no-objeto ⇒ `getMemberNames`
        ''' lanza ⇒ False. Los productores que representan el estado completo (snapshot, .jslot con
        ''' `ClearMorphs` incondicional en skee64 PresetInterface.cpp:281) lo ponen en True. Lo lee
        ''' <see cref="PresetCategoryFilter"/>: False ⇒ la categoria se preserva del baseline aunque este tickeada.</summary>
        Public HasBodyMorphSliders As Boolean = False

        ''' <summary>SSE-ONLY keyed body morphs: morph name → (BodySlide key → value). RaceMenu body
        ''' sliders accumulate one keyed contribution per BodySlide source; the engine nets (sums) the
        ''' per-key values, so the flat render dict is derived by summing. Kept keyed here (nullable —
        ''' Nothing on FO4 and on SSE presets without body morphs) so a .jslot/BodyGen save round-trips
        ''' faithfully instead of collapsing to the summed <see cref="BodyMorphSliders"/>. Not serialized
        ''' to the FO4 f4ee preset JSON — persistence is the SSE .bssliders sidecar (BodyMorphsKeyed).</summary>
        Public BodyMorphsKeyed As Dictionary(Of String, Dictionary(Of String, Single)) = Nothing

        ''' <summary>Body overlays (LooksMenu "tattoos") — the per-NPC list of applied overlay entries.
        ''' Render-only F4SE field, same shape as <see cref="BodyMorphSliders"/> (lives only in the JSON,
        ''' no vanilla NPC_ subrecord equivalent). Each entry references an <see cref="OverlayTemplate"/>
        ''' by id plus per-instance priority and optional tint/UV transform. Schema:
        ''' Script extenders, Racemenu y Looksmenu/F4SEPlugins/f4ee/CharGenInterface.cpp:217-244 (Save) and 578-619 (Load).</summary>
        Public Overlays As New List(Of OverlayEntry)

        ''' <summary>Overlays presence — SAME semantics as the other Has* flags above. True = "this
        ''' preset declares the Overlays field, the list (even empty) is authoritative ⇒ overlay
        ''' treats it as a wipe". False = "preserve".
        ''' <see cref="ParseFile"/> lo pone SIEMPRE en True: el motor hace `RemoveAll` incondicional antes de
        ''' leer la clave (CharGenInterface.cpp:587), asi que un .json sin "Overlays" deja al NPC sin overlays.</summary>
        Public HasOverlays As Boolean = False

        ''' <summary>SSE-ONLY RaceMenu body overlays (tattoos) — PATH-based (node + diffuse/normal path +
        ''' tint), decoded from a <c>.jslot</c>'s <c>overrides</c> array (<see cref="RaceMenuJslot.Overlays"/>).
        ''' Distinct carrier from the FO4 template-based <see cref="Overlays"/>: RaceMenu overlays have no
        ''' f4ee catalog, so they can't be an <see cref="OverlayEntry"/> (TemplateId). Nullable — Nothing on
        ''' FO4 and on SSE presets without overlays; persistence is the <c>.jslot</c> + the <c>.bssliders</c>
        ''' sidecar (<c>sseBodyOverlays</c>). The render sources it under the game gate; FO4 never reads it.</summary>
        Public SseBodyOverlays As List(Of RaceMenuJslot.JslotOverlayNode) = Nothing

        ''' <summary>SSE RaceMenu NiOverride node transforms (body-scale sliders — e.g. scale of "NPC L Breast").
        ''' Nothing on FO4 / SSE presets without transforms. Persistence: <c>.jslot</c> (transforms) + the
        ''' <c>.bssliders</c> sidecar; also flows through Copy/Paste. The render applies the per-node scale to
        ''' the skeleton under the game gate; FO4 never reads it.</summary>
        Public SseNodeTransforms As List(Of RaceMenuJslot.JslotNodeTransform) = Nothing

        ''' <summary>Los elementos <c>transforms</c> con <c>firstPerson = true</c> del preset, como JSON crudo.
        ''' <para>NO se modelan (son el 3D de primera persona: un NPC no lo tiene, y modelarlos daba DOS entradas del
        ''' mismo nodo) pero tienen que VIAJAR: el "Save RaceMenu preset" construye un <c>RaceMenuJslot</c> nuevo desde
        ''' el carrier, así que sin este campo se perderían al re-exportar. Misma razón que la key 40: no modelar algo
        ''' no da derecho a borrarlo.</para></summary>
        Public SseFirstPersonTransformsRaw As List(Of String) = Nothing
        ''' <summary>SSE RaceMenu NiOverride SKIN overrides (body-paint / skin texture-tint per biped slot).
        ''' Nothing on FO4 / SSE presets without skin overrides. Persistence: <c>.jslot</c> (skinOverrides) + the
        ''' <c>.bssliders</c> sidecar; also flows through Copy/Paste. FO4 never reads it.</summary>
        Public SseSkinOverrides As List(Of RaceMenuJslot.JslotSkinOverride) = Nothing

        ''' <summary>Counts of F4SE-only fields the preset contains. Non-zero = the preset has
        ''' content the editor will not apply (Overlays/BodyMorphs sliders/Skin override).</summary>
        Public UnsupportedCounts As New UnsupportedFieldCounts

        ''' <summary>Editor-only override of NPC.ACBS bit 2 ("Is CharGen Face Preset"). Lives in
        ''' the in-memory overlay so the user can flip the flag in Edit Face and have it persisted
        ''' to ESP later (Save ESP/ESM is the consumer; out of scope for the LM JSON, which doesn't
        ''' carry this field). Nothing = preserve raw NPC.AcbsFlags; True/False = override the bit.
        ''' NOT serialized to LooksMenu JSON — see 40-bake-reglas-comunes.md memory.</summary>
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

        ''' <summary>Editor-only override of NPC.SOFT (Sleep Outfit → OTFT FormID). Same three-state shape as
        ''' <see cref="DefaultOutfitFormIDOverride"/>: lives in the in-memory overlay, round-trips through the
        ''' <c>_npcm_SleepOutfit</c> JSON extension and Copy/Paste, and persists to ESP via Save ESP (NPC_.SOFT).
        ''' Set by the NPC Editor's Inventory tab.
        ''' Nothing = preserve raw NPC.SOFT; 0 = no sleep outfit; other = OTFT FormID.
        ''' NOT a vanilla LooksMenu field — LM in-game ignores the <c>_npcm_</c> key.</summary>
        Public SleepOutfitFormIDOverride As UInteger?

        ''' <summary>F4SE LM Skin override — the string id of a SkinTemplate registered via
        ''' <c>Script extenders, Racemenu y Looksmenu/F4SEPlugins/f4ee/SkinInterface.cpp</c>. The template bundles ARMO + face TXST +
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
    ''' transform. Schema verified against Script extenders, Racemenu y Looksmenu/F4SEPlugins/f4ee/CharGenInterface.cpp:217-244
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

    ''' <summary>EL ORDEN DE DIBUJO de los overlays de LooksMenu: de ABAJO hacia ARRIBA, o sea prioridad
    ''' DESCENDENTE — la prioridad MÁS BAJA se dibuja última y queda ARRIBA. Es el análogo FO4 de
    ''' <c>SseOverlayCompositor.OrderForDraw</c> y, como aquélla, es LA ÚNICA implementación: la consume el
    ''' resolver del render y la fija el gate.
    ''' <para><b>DE DÓNDE SALE.</b> La CITA (f4ee OverlayInterface.cpp:124-152) da el orden de ENGANCHE:
    ''' <c>ForEachOverlayBySlot</c> recorre el <c>PriorityMap</c> —un <c>std::multimap&lt;SInt32,…&gt;</c>, o sea
    ''' prioridad ASCENDENTE (OverlayInterface.h:123)— y por cada entrada clona la shape de piel y hace
    ''' <c>AttachChild</c>. La MEDICIÓN es un reporte in-game (2026-08-28) que dice que se ve encima el PRIMER
    ''' nodo enganchado ⇒ el motor dibuja los hermanos coplanares al revés del enganche. Enganchar NO es dibujar:
    ''' ese paso lo decide el acumulador del motor, que f4ee no toca (no hay un solo <c>reverse</c>/<c>rbegin</c>
    ''' en el plugin), y NO está leído del binario. Si algún día se lee y dice otra cosa, cambia ESTA función.</para>
    ''' <para><c>OrderByDescending</c> es estable ⇒ entradas con la misma prioridad conservan el orden del preset,
    ''' que es lo que hace el multimap dentro de un bucket. Tolera <c>Nothing</c> en la lista (el resolver filtra
    ''' recién adentro del loop); un nulo ordena al fondo.</para></summary>
    Public Function OrderOverlaysForDraw(entries As IEnumerable(Of OverlayEntry)) As List(Of OverlayEntry)
        If entries Is Nothing Then Return New List(Of OverlayEntry)()
        Return entries.OrderByDescending(Function(e) If(e IsNot Nothing, e.Priority, Integer.MaxValue)).ToList()
    End Function

    ''' <summary>Parse a LooksMenu preset JSON file. Returns Nothing if the file is unreadable
    ''' or not valid JSON. Form-identifier strings ("Plugin.esp|XXXXXX") are resolved against
    ''' <paramref name="pluginManager"/> at parse time — entries from plugins not in the active
    ''' load order resolve to 0 and the caller will see HeadParts entries missing.
    ''' <para>LA LEY: el resultado es, canal por canal, el estado del motor DESPUES de
    ''' <c>CharGenInterface::LoadPreset</c> (f4ee CharGenInterface.cpp:269-645), leido con la tabla de
    ''' conversiones de jsoncpp (f4ee/jsoncpp/json_value.cpp): <c>asFloat</c> :758-778, <c>asInt</c> :631-651,
    ''' <c>asUInt</c> :653-673, <c>asString</c> :606-623, <c>asCString</c> :600-604, <c>operator[]</c> :918-994,
    ''' <c>isMember</c> :1090-1093, <c>getMemberNames</c> :1105-1127, range-for :1284-1402 y orden de claves
    ''' <c>strcmp</c> :200-204. Cada bloque de LoadPreset tiene su propio try/catch: cuando jsoncpp lanza el
    ''' motor deja el canal como estaba, y eso es lo unico que pone un Has* en False. Los <c>Has*</c> se
    ''' documentan en <see cref="LooksmenuPreset"/>. Aca no hay decisiones de producto: Intensity ausente ⇒ 1.0
    ''' es motor (:466-467); la unica de producto (no crear FMIN cuando vale 1.0) vive en
    ''' <see cref="NpcRecordOverlay"/>.</para></summary>
    Public Function ParseFile(filePath As String, pluginManager As PluginManager) As LooksmenuPreset
        If String.IsNullOrEmpty(filePath) OrElse Not File.Exists(filePath) Then Return Nothing

        Dim bytes As Byte()
        Try
            bytes = File.ReadAllBytes(filePath)
        Catch
            Return Nothing
        End Try

        ' El motor lee el archivo como bytes y se los da a `Json::Reader::parse` (CharGenInterface.cpp:274-282),
        ' que NO saltea ningun BOM (json_reader.cpp:83-143): un archivo con BOM es ERROR_INVALID_TOKEN y no se
        ' aplica nada. Se parsea desde los bytes por lo mismo: la misma entrada que ve el motor.
        If bytes.Length >= 3 AndAlso bytes(0) = &HEF AndAlso bytes(1) = &HBB AndAlso bytes(2) = &HBF Then
            Logger.LogLazy(Function() $"[LM-PRESET] '{filePath}': BOM al inicio; LooksMenu lo rechaza (json_reader.cpp:83-143). Se saltea.")
            Return Nothing
        End If

        Dim doc As JsonDocument
        Try
            ' Comentarios permitidos: `Json::Features` por defecto trae allowComments_ = true (json_reader.cpp:28-29).
            ' Coma final PROHIBIDA: el reader la toma como error de sintaxis en objetos (:413-425) y arrays
            ' (:468-474) ⇒ el motor no aplica nada. Con AllowTrailingCommas el app cargaba lo que el juego rechaza.
            doc = JsonDocument.Parse(New ReadOnlyMemory(Of Byte)(bytes),
                                     New JsonDocumentOptions With {.CommentHandling = JsonCommentHandling.Skip, .AllowTrailingCommas = False})
        Catch ex As Exception
            Logger.LogLazy(Function() $"[LM-PRESET] '{filePath}': JSON invalido para LooksMenu ({ex.Message}). Se saltea.")
            Return Nothing
        End Try

        Using doc
            Dim root = doc.RootElement
            ' Raiz no-objeto: HUECO del motor. `root["Gender"]` lanza dentro del try (:301-309), la limpieza de
            ' head parts :318-331 pasa igual, y `root.isMember("Overlays")` :588 lanza SIN catch. No se replica:
            ' el archivo se saltea.
            If root.ValueKind <> JsonValueKind.Object Then Return Nothing

            Dim preset As New LooksmenuPreset With {.SourcePath = filePath}
            Dim ok As Boolean

            ' Gender (:301-309): `UInt8 loadedGender = root["Gender"].asUInt()`. Ausente/null ⇒ 0; bool ⇒ 1/0;
            ' real ⇒ truncado; el UInt32 se trunca a UInt8. Si asUInt lanza (negativo, fuera de rango, string,
            ' array, objeto) el catch deja loadedGender = gender ⇒ el preset pasa el filtro: HasGender = False.
            Dim genderEl As JsonElement
            root.TryGetProperty("Gender", genderEl)
            Dim genderU = Jsoncpp.AsUInt(genderEl, ok)
            preset.HasGender = ok
            If ok Then preset.Gender = CByte(genderU And &HFFUI)

            ' HeadParts (:333-352): range-for sobre `root["HeadParts"]` (array ⇒ elementos; objeto ⇒ valores;
            ' otro ⇒ nada), `part.asString()` (null ⇒ ""; numero/bool ⇒ texto; array/objeto ⇒ lanza y corta el
            ' resto, lo ya aplicado queda), `GetFormFromIdentifier` (Utilities.cpp:133-155), `if(!form) continue`
            ' (:339-340), `DYNAMIC_CAST(..., BGSHeadPart)` y `if(!headPart) continue` (:342-344).
            ' D-HeadParts (PENDIENTE del usuario): el motor limpia las partes SIEMPRE (:318-331, antes de mirar la
            ' clave); el app solo lo hace cuando la clave esta (array u objeto). Ausente/escalar ⇒ preserva.
            ' Un identificador cuyo plugin no esta cargado va a UnresolvedHeadParts (decision del usuario
            ' 2026-08-24: PRESERVAR, no inventar); el motor en ese caso hace `LookupFormByID` con el FormID
            ' local sin indice de mod (Utilities.cpp:141-153), que puede pegarle a un form ajeno: NO se replica.
            Dim hpEl As JsonElement
            If root.TryGetProperty("HeadParts", hpEl) AndAlso (hpEl.ValueKind = JsonValueKind.Array OrElse hpEl.ValueKind = JsonValueKind.Object) Then
                preset.HasHeadPartFormIDs = True
                For Each entry In Jsoncpp.Valores(hpEl)
                    Dim hpStr = Jsoncpp.AsString(entry, ok)
                    If Not ok Then Exit For
                    If String.IsNullOrEmpty(hpStr) Then Continue For
                    Dim resolved = ResolveFormIdentifier(hpStr, pluginManager)
                    If resolved = 0UI Then
                        preset.UnresolvedHeadParts.Add(hpStr)
                        Continue For
                    End If
                    Dim rec = pluginManager.GetRecord(resolved)
                    If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then
                        Logger.LogLazy(Function() $"[LM-PRESET] '{filePath}': HeadParts '{hpStr}' → {resolved:X8} no es un HDPT cargado; LooksMenu lo saltea (CharGenInterface.cpp:339-344).")
                        Continue For
                    End If
                    preset.HeadPartFormIDs.Add(resolved)
                Next
            End If

            ' HairColor (:354-363): `root["HairColor"].asString()` (ausente/null ⇒ ""; array/objeto ⇒ lanza ⇒
            ' preserva), `GetFormFromIdentifier`, `DYNAMIC_CAST(..., BGSColorForm)`; sin form o no-CLFM ⇒ no se
            ' toca el color. 0 = preservar.
            Dim hcEl As JsonElement
            root.TryGetProperty("HairColor", hcEl)
            Dim hcRaw = Jsoncpp.AsString(hcEl, ok)
            If ok AndAlso Not String.IsNullOrEmpty(hcRaw) Then
                Dim hcFid = ResolveFormIdentifier(hcRaw, pluginManager)
                If hcFid = 0UI Then
                    ' Guardar el crudo si no resolvió: el 0 solo no distingue "no hay color" de "el mod no está".
                    If Not String.IsNullOrWhiteSpace(hcRaw) Then preset.UnresolvedHairColor = hcRaw
                Else
                    Dim rec = pluginManager.GetRecord(hcFid)
                    If rec IsNot Nothing AndAlso rec.Header.Signature = "CLFM" Then
                        preset.HairColorFormID = hcFid
                    Else
                        Logger.LogLazy(Function() $"[LM-PRESET] '{filePath}': HairColor '{hcRaw}' → {hcFid:X8} no es un CLFM cargado; LooksMenu lo ignora (CharGenInterface.cpp:358-359).")
                    End If
                End If
            End If

            ' Weight (:476-484): `root["Weight"][i].asFloat()` para i = 0..2, en secuencia. Ausente/null ⇒ el
            ' `[i]` crea nulls ⇒ 0,0,0 (el motor ESCRIBE ceros: un .json sin "Weight" deja al NPC en 0/0/0).
            ' Array corto ⇒ los que faltan son 0. Objeto/escalar/string ⇒ `[0]` lanza ⇒ los tres preservados
            ' (Nothing). Un elemento string/array/objeto ⇒ lanza ahi ⇒ ese y los siguientes preservados.
            Dim wEl As JsonElement
            root.TryGetProperty("Weight", wEl)
            Dim pesos(2) As Single?
            If wEl.ValueKind = JsonValueKind.Undefined OrElse wEl.ValueKind = JsonValueKind.Null OrElse wEl.ValueKind = JsonValueKind.Array Then
                Dim arr = If(wEl.ValueKind = JsonValueKind.Array, wEl.EnumerateArray().ToArray(), New JsonElement() {})
                For i = 0 To 2
                    Dim v = Jsoncpp.AsFloat(If(i < arr.Length, arr(i), New JsonElement()), ok)
                    If Not ok Then Exit For
                    pesos(i) = v
                Next
            End If
            preset.WeightThin = pesos(0)
            preset.WeightMuscular = pesos(1)
            preset.WeightFat = pesos(2)

            ' Morphs.{Values, Presets, Regions, Intensity}. `root["Morphs"]` (operator[] no-const, json_value.cpp
            ' :970-994): ausente/null ⇒ se vuelve `{}` y los cuatro canales se leen como de un objeto vacio
            ' (los tres contenedores se ESCRIBEN vacios y FMIN = 1.0). "Morphs" string/numero/array/bool ⇒
            ' lanza en :373, :399, :433 y en el `isMember` de :466 ⇒ los cuatro se preservan.
            Dim morphsEl As JsonElement
            root.TryGetProperty("Morphs", morphsEl)
            Dim morphsEsObjeto = (morphsEl.ValueKind = JsonValueKind.Object OrElse morphsEl.ValueKind = JsonValueKind.Null OrElse morphsEl.ValueKind = JsonValueKind.Undefined)
            If morphsEsObjeto Then
                ' Ausente/null ⇒ `{}` para el motor; para System.Text.Json un JsonElement Undefined/Null LANZA en
                ' TryGetProperty, asi que las cuatro lecturas se gatean por ValueKind = Object y caen al default.
                Dim morphsTieneClaves = (morphsEl.ValueKind = JsonValueKind.Object)
                ' Values (:373-397): range-for sobre `Morphs["Values"]` (array ⇒ elementos; objeto ⇒ valores;
                ' ausente/null/escalar ⇒ nada) y `asFloat` de cada uno ANTES de tocar al NPC: un elemento
                ' string/array/objeto lanza ⇒ se preserva. Con todos convertibles: `Clear(); Allocate(5)` y se
                ' escriben los primeros 5 (:385-389). El pad a 5 lo hace PonerValoresDeRegionCorporal en el overlay.
                Dim valuesEl As JsonElement
                If morphsTieneClaves Then morphsEl.TryGetProperty("Values", valuesEl)
                Dim valores As New List(Of Single)
                ok = True
                For Each v In Jsoncpp.Valores(valuesEl)
                    Dim f = Jsoncpp.AsFloat(v, ok)
                    If Not ok Then Exit For
                    valores.Add(f)
                Next
                If ok Then
                    preset.HasBodyMorphValues = True
                    preset.BodyMorphValues.AddRange(valores)
                End If

                ' Presets (:433-460): `getMemberNames` (null ⇒ vacio; array/escalar/string ⇒ lanza ⇒ preserva),
                ' `Clear()` del contenedor, y por cada clave en orden strcmp: `sscanf_s("%X")` (sin chequear),
                ' `asFloat` (lanza ⇒ corta: lo anterior queda escrito) y `tHashSet::Add` (f4se GameTypes.h:1159 ⇒ `Insert` :988-1016:
                ' clave repetida ⇒ gana la PRIMERA).
                Dim presetsEl As JsonElement
                If morphsTieneClaves Then morphsEl.TryGetProperty("Presets", presetsEl)
                Dim miembros = Jsoncpp.Miembros(presetsEl, ok)
                If ok Then
                    preset.HasChargenFaceMorphs = True
                    For Each kv In miembros
                        Dim f = Jsoncpp.AsFloat(kv.Value, ok)
                        If Not ok Then Exit For
                        Dim hash = Jsoncpp.ClaveSscanfX(kv.Key)
                        If Not preset.ChargenFaceMorphs.ContainsKey(hash) Then preset.ChargenFaceMorphs(hash) = f
                    Next
                End If

                ' Regions (:399-429): mismo patron que Presets. El valor se lee como `regions[key][i]` para
                ' i = 0..7 (:418-421): null ⇒ se vuelve array y da 8 ceros; array corto ⇒ 0 en lo que falta;
                ' objeto/escalar/string ⇒ `[i]` lanza ⇒ corta; un elemento string/array/objeto ⇒ `asFloat` lanza
                ' ⇒ corta. Siempre 8 floats por region. Clave repetida ⇒ gana la primera (tHashSet::Add).
                Dim regionsEl As JsonElement
                If morphsTieneClaves Then morphsEl.TryGetProperty("Regions", regionsEl)
                miembros = Jsoncpp.Miembros(regionsEl, ok)
                If ok Then
                    preset.HasFaceBoneRegions = True
                    For Each kv In miembros
                        Dim vals(7) As Single
                        Dim regionOk = (kv.Value.ValueKind = JsonValueKind.Null OrElse kv.Value.ValueKind = JsonValueKind.Array)
                        If regionOk Then
                            Dim arr = If(kv.Value.ValueKind = JsonValueKind.Array, kv.Value.EnumerateArray().ToArray(), New JsonElement() {})
                            For i = 0 To 7
                                vals(i) = Jsoncpp.AsFloat(If(i < arr.Length, arr(i), New JsonElement()), regionOk)
                                If Not regionOk Then Exit For
                            Next
                        End If
                        If Not regionOk Then Exit For
                        Dim idx = Jsoncpp.ClaveSscanfX(kv.Key)
                        If Not preset.FaceBoneRegions.ContainsKey(idx) Then preset.FaceBoneRegions(idx) = vals
                    Next
                End If

                ' Intensity (:462-474): `isMember("Intensity")` ⇒ presente (aunque null) ⇒ `asFloat` (null ⇒ 0.0,
                ' bool ⇒ 1/0; string/array/objeto lanza ⇒ preserva); ausente ⇒ 1.0f (:467, MOTOR). Se escribe siempre
                ' que no lance (non-player :462). Que el overlay cree el subrecord o no es la decision de producto de FMIN.
                Dim intEl As JsonElement
                If morphsTieneClaves AndAlso morphsEl.TryGetProperty("Intensity", intEl) Then
                    Dim f = Jsoncpp.AsFloat(intEl, ok)
                    preset.HasFacialMorphIntensity = ok
                    If ok Then preset.FacialMorphIntensity = f
                Else
                    preset.HasFacialMorphIntensity = True
                    preset.FacialMorphIntensity = 1.0F
                End If
            End If

            ' Tints + TintOrder (:486-566). `Json::Value tints = root["Tints"]` y `getMemberNames` (null ⇒ vacio;
            ' array/escalar/string ⇒ lanza ⇒ se preserva TODO el canal). Con miembros o contenedor existente:
            ' `ClearCharacterTints` (:495-500) y por cada clave en orden strcmp:
            '   • `sscanf_s(key, "%X", &keyValue)` con keyValue = 0 si no hay digitos (:504);
            '   • `tints[key]["Type"].asInt()` (:506): miembro null ⇒ se vuelve `{}` ⇒ Type 0; miembro
            '     escalar/array/string ⇒ `["Type"]` lanza ⇒ ABORTA el canal (el contenedor ya esta limpio ⇒ el
            '     NPC queda SIN tints); Type string/array/objeto/fuera de Int32 ⇒ idem;
            '   • `GetTemplateByIndex((UInt16)keyValue)` (:512, f4se GameCustomization.cpp:121-137): sin
            '     plantilla en la RACE ⇒ `continue` SIN leer Color/ColorID/Percent — por eso esas tres
            '     conversiones se posponen a ConversionFallida y las evalua el overlay, que conoce la raza;
            '   • `CreateCharacterTintEntry((keyValue << 16) | type)` (:514): el indice de la entrada es
            '     keyValue & 0xFFFF (GameCustomization.h:454-455);
            '   • paleta (:517-527): `Color.asUInt()` (negativo/string/array/objeto lanza ⇒ aborta),
            '     `SInt16 colorID = ColorID.asInt()` (truncado a 16 bits), `GetColorDataByID((UInt16)colorID)`
            '     y si no existe `colors[0].colorID` (lo resuelve el overlay con la RACE);
            '   • `percent = Percent.asInt()` a UInt8 (GameCustomization.h:179, sin clamp: byte bajo);
            '   • `tintMap.emplace(keyValue, ...)` (:531): clave UInt32 repetida ⇒ gana la primera.
            ' TintOrder (:537-552): `isMember` ⇒ presente (aunque null) ⇒ range-for (array ⇒ elementos; objeto ⇒
            ' valores; otro ⇒ nada), `asCString` (no-string ⇒ lanza ⇒ corta: lo YA empujado queda, lo que sigue
            ' y el resto del mapa NO se empujan), `sscanf_s("%X")`, se saca del mapa y se empuja; clave que no
            ' esta se ignora. Lo que queda en el mapa se empuja ascendente por clave UInt32 (:555-556).
            Dim tintsEl As JsonElement
            root.TryGetProperty("Tints", tintsEl)
            Dim tintMiembros = Jsoncpp.Miembros(tintsEl, ok)
            If ok Then
                preset.HasFaceTintLayers = True
                Dim mapa As New SortedDictionary(Of UInteger, CapaDeTintePreset)
                Dim aborta = False
                For Each kv In tintMiembros
                    Dim keyValue = Jsoncpp.ClaveSscanfX(kv.Key)
                    Dim entryEl = kv.Value
                    If entryEl.ValueKind <> JsonValueKind.Object AndAlso entryEl.ValueKind <> JsonValueKind.Null Then
                        aborta = True : Exit For
                    End If
                    ' `= Nothing` en cada vuelta: VB NO reinicializa un `Dim` de bloque por iteracion, y para una
                    ' entrada null (que el motor trata como `{}`) no se llama TryGetProperty ⇒ sin esto la capa
                    ' heredaba Type/Color/ColorID/Percent de la capa ANTERIOR.
                    Dim typeEl As JsonElement = Nothing
                    If entryEl.ValueKind = JsonValueKind.Object Then entryEl.TryGetProperty("Type", typeEl)
                    Dim tipo = Jsoncpp.AsInt(typeEl, ok)
                    If Not ok Then aborta = True : Exit For

                    Dim layer As New CapaDeTintePreset()
                    layer.Index = CUShort(keyValue And &HFFFFUI)
                    layer.Discriminator = CUShort(tipo And &HFFFF)

                    Dim colorEl As JsonElement = Nothing, cidEl As JsonElement = Nothing, pctEl As JsonElement = Nothing
                    If entryEl.ValueKind = JsonValueKind.Object Then
                        entryEl.TryGetProperty("Color", colorEl)
                        entryEl.TryGetProperty("ColorID", cidEl)
                        entryEl.TryGetProperty("Percent", pctEl)
                    End If
                    ' Palette-only: Type=1 (BGSCharacterTint::Entry::kTypePalette, GameCustomization.h:173).
                    ' Color is stored as bgra UInt32. CharGenInterface.cpp:193 writes
                    '   tintData[k]["Color"] = (Json::Int)palette->color.bgra
                    ' which is signed-int-with-bit-pattern, and :519 reads it back with asUInt (negative ⇒ throw).
                    If layer.Discriminator = 1US Then
                        Dim bgra = Jsoncpp.AsUInt(colorEl, ok)
                        If ok Then
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
                        Else
                            layer.ConversionFallida = True
                        End If
                        Dim cid = Jsoncpp.AsInt(cidEl, ok)
                        If ok Then
                            ' `SInt16 colorID = ...asInt()` (:520): se queda con los 16 bits bajos, con signo.
                            Dim lo16 = cid And &HFFFF
                            If lo16 >= &H8000 Then lo16 -= &H10000
                            layer.TemplateColorIndex = lo16
                        Else
                            layer.ConversionFallida = True
                        End If
                    End If
                    Dim pct = Jsoncpp.AsInt(pctEl, ok)
                    If ok Then
                        layer.Value = pct And &HFF
                    Else
                        layer.ConversionFallida = True
                    End If

                    If Not mapa.ContainsKey(keyValue) Then mapa(keyValue) = layer
                Next

                If aborta Then
                    mapa.Clear()
                End If
                Dim tintOrderEl As JsonElement
                If Not aborta AndAlso root.TryGetProperty("TintOrder", tintOrderEl) Then
                    For Each k In Jsoncpp.Valores(tintOrderEl)
                        Dim texto = Jsoncpp.AsCString(k, ok)
                        If Not ok Then
                            ' Lanza a mitad del bucle: lo empujado queda, el resto del mapa no se empuja nunca.
                            mapa.Clear()
                            Exit For
                        End If
                        Dim keyValue = Jsoncpp.ClaveSscanfX(texto)
                        Dim capa As CapaDeTintePreset = Nothing
                        If mapa.TryGetValue(keyValue, capa) Then
                            preset.FaceTintLayers.Add(capa)
                            mapa.Remove(keyValue)
                        End If
                    Next
                End If
                For Each kv In mapa
                    preset.FaceTintLayers.Add(kv.Value)
                Next
            End If

            ' Overlays (body tattoos) — CharGenInterface.cpp:587-630. `RemoveAll` es INCONDICIONAL (:587, antes
            ' de mirar la clave) ⇒ HasOverlays = True siempre: un .json sin "Overlays" deja al NPC sin overlays.
            ' `isMember("Overlays")` ⇒ range-for (array ⇒ elementos; objeto ⇒ valores; otro ⇒ nada). Cada entrada
            ' tiene su propio try/catch (:594-628): `priority.asInt()` (entrada null ⇒ se vuelve `{}` ⇒ 0;
            ' escalar/array/string ⇒ lanza ⇒ se saltea), `template.asCString()` (ausente/null/no-string ⇒ lanza
            ' ⇒ se saltea; "" se acepta pero `AddOverlay` con plantilla no registrada devuelve 0,
            ' OverlayInterface.cpp:237-241 ⇒ mismo resultado que saltearla), y tint/offsetUV/scaleUV con
            ' `isMember` ⇒ presente (aunque null) ⇒ `[i].asFloat()` (null/corto ⇒ 0; objeto/escalar/string ⇒
            ' `[0]` lanza ⇒ se saltea la entrada; elemento no convertible ⇒ idem). Ausente ⇒ Nothing y el motor
            ' pone sus defaults (tint 0,0,0,0 / offset 0,0 / scale 1,1). UnsupportedCounts.Overlays sigue
            ' poblado (la UI del Load lo lee).
            preset.HasOverlays = True
            Dim ovEl As JsonElement
            root.TryGetProperty("Overlays", ovEl)
            Dim ovEntradas = Jsoncpp.Valores(ovEl)
            preset.UnsupportedCounts.Overlays = ovEntradas.Count
            For Each ov In ovEntradas
                If ov.ValueKind <> JsonValueKind.Object Then Continue For

                Dim prEl, tplEl As JsonElement
                ov.TryGetProperty("priority", prEl)
                Dim prioridad = Jsoncpp.AsInt(prEl, ok)
                If Not ok Then Continue For
                ov.TryGetProperty("template", tplEl)
                Dim tplId = Jsoncpp.AsCString(tplEl, ok)
                If Not ok OrElse String.IsNullOrEmpty(tplId) Then Continue For

                Dim entry As New OverlayEntry With {.TemplateId = tplId, .Priority = prioridad}

                Dim tintEl As JsonElement
                If ov.TryGetProperty("tint", tintEl) Then
                    entry.Tint = Jsoncpp.Floats(tintEl, 4, ok)
                    If Not ok Then Continue For
                End If
                Dim offEl As JsonElement
                If ov.TryGetProperty("offsetUV", offEl) Then
                    entry.OffsetUV = Jsoncpp.Floats(offEl, 2, ok)
                    If Not ok Then Continue For
                End If
                ' scaleUV: the engine SAVE has a bug (:248-249 appends offsetUV.x/y into the scaleUV array),
                ' but the engine LOAD reads scaleUV faithfully, so reading it straight is correct.
                Dim sclEl As JsonElement
                If ov.TryGetProperty("scaleUV", sclEl) Then
                    entry.ScaleUV = Jsoncpp.Floats(sclEl, 2, ok)
                    If Not ok Then Continue For
                End If

                preset.Overlays.Add(entry)
            Next

            ' Skin (:632-638): `RevertOverride` + `RemoveSkinOverride` INCONDICIONALES ⇒ ausente = "" (sin
            ' plantilla). `isMember("Skin")` ⇒ `asString` (null ⇒ ""; numero/bool ⇒ texto; array/objeto ⇒
            ' lanza FUERA de todo try — HUECO del motor — con el override ya quitado ⇒ ""). Un id que no esta
            ' registrado no hace nada (SkinInterface.cpp:84-85).
            Dim skEl As JsonElement
            root.TryGetProperty("Skin", skEl)
            Dim skId = Jsoncpp.AsString(skEl, ok)
            preset.SkinTemplateId = If(ok, If(skId, ""), "")
            preset.UnsupportedCounts.HasSkinOverride = Not String.IsNullOrEmpty(preset.SkinTemplateId)

            ' BodyMorphs (:568-585): `morphData = root["BodyMorphs"]`, `getMemberNames` (null ⇒ vacio;
            ' array/escalar/string ⇒ lanza ⇒ se preserva). `RemoveMorphsByKeyword` (:573) SOLO si `members.size() > 0 ||
            ' (isMember("BodyMorphs") && isNull())` (:572-577): `{}` y ausente PRESERVAN los sliders del actor;
            ' null explicito los borra. Despues, por clave en orden strcmp, `asFloat` (lanza ⇒ corta: lo
            ' anterior queda, con el wipe ya hecho).
            Dim bmEl As JsonElement
            Dim bmPresente = root.TryGetProperty("BodyMorphs", bmEl)
            Dim bmMiembros = Jsoncpp.Miembros(bmEl, ok)
            If ok Then
                preset.HasBodyMorphSliders = (bmMiembros.Count > 0 OrElse (bmPresente AndAlso bmEl.ValueKind = JsonValueKind.Null))
                For Each kv In bmMiembros
                    Dim f = Jsoncpp.AsFloat(kv.Value, ok)
                    If Not ok Then Exit For
                    preset.BodyMorphSliders(kv.Key) = f
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
                    ' "NO PUDE RESOLVERLO" ≠ "EL USUARIO LO BORRÓ". Este campo tiene TRES estados y dos de
                    ' ellos colapsaban: Nothing = preservar, Some(0) = CLEAR explícito (el "" de arriba), y
                    ' un valor = override. Asignar el 0 que devuelve ResolveFormIdentifier cuando el plugin
                    ' no está cargado lo metía en el estado CLEAR, y ese valor NO se queda en el render:
                    ' NpcRecordOverlay:131 hace `.SkinFormID = If(preset.SkinFormIDOverride, raw.SkinFormID)`
                    ' ⇒ pisa el skin real del NPC con 0 ⇒ el NPC_ sale al ESP con la piel BORRADA. O sea que
                    ' un preset cuyo ARMO de piel es de un mod no activo BORRABA la piel del NPC de forma
                    ' permanente, en vez de dejarla como estaba.
                    ' CANÓNICO: f4ee saltea el form que no resuelve y no toca al actor —
                    ' `TESForm * form = GetFormFromIdentifier(...); if(!form) continue;`
                    ' (Script extenders, Racemenu y Looksmenu/F4SEPlugins/f4ee/CharGenInterface.cpp:328-330).
                    If resolved <> 0UI Then
                        preset.SkinFormIDOverride = resolved
                    Else
                        ' ⛔ SE PRESERVA EL CRUDO, Y FUERA DEL `Logger`. El informe de compatibilidad sólo mira
                        ' los overrides que TIENEN valor (`HasValue AndAlso <> 0`,
                        ' `PresetCompatibilityReport.AuditFormIdFields`), así que sin guardar el identificador
                        ' el caso no aparece en ningún lado — y el log está apagado en Release. Con los head
                        ' parts el usuario sí se entera (`UnresolvedHeadParts` → el mismo informe), y no hay
                        ' razón para que estos tres sean la excepción. Ahora no lo son.
                        preset.UnresolvedSkin = sfStr
                        Dim sfMissing = sfStr
                        Logger.LogLazy(Function() $"[LMLoad] _npcm_SkinFormID '{sfMissing}' no resuelve " &
                                                  "(su plugin no está en el load order) -> se preserva el skin del NPC.")
                    End If
                End If
            End If
            Dim outfitFidEl As JsonElement
            If root.TryGetProperty("_npcm_DefaultOutfit", outfitFidEl) AndAlso outfitFidEl.ValueKind = JsonValueKind.String Then
                Dim ofStr = outfitFidEl.GetString()
                If String.IsNullOrEmpty(ofStr) Then
                    ' Empty string = "no outfit". Equivale a Some(0).
                    preset.DefaultOutfitFormIDOverride = 0UI
                Else
                    ' Mismo criterio que _npcm_SkinFormID: si el plugin del OTFT no está cargado, NO se
                    ' asigna ⇒ queda Nothing = "preservar el DOFT del NPC". Asignar el 0 lo metía en el
                    ' estado CLEAR ("sin outfit") y eso viaja al ESP, o sea que un preset cuyo outfit es de
                    ' un mod ausente DEJABA AL NPC DESNUDO de forma permanente. f4ee saltea el form que no
                    ' resuelve (CharGenInterface.cpp:328-330), no lo limpia.
                    Dim ofResolved = ResolveFormIdentifier(ofStr, pluginManager)
                    If ofResolved <> 0UI Then
                        preset.DefaultOutfitFormIDOverride = ofResolved
                    Else
                        preset.UnresolvedDefaultOutfit = ofStr   ' ver la nota de `_npcm_SkinFormID`
                        Dim ofMissing = ofStr
                        Logger.LogLazy(Function() $"[LMLoad] _npcm_DefaultOutfit '{ofMissing}' no resuelve " &
                                                  "(su plugin no está en el load order) -> se preserva el DOFT del NPC.")
                    End If
                End If
            End If
            Dim sleepFidEl As JsonElement
            If root.TryGetProperty("_npcm_SleepOutfit", sleepFidEl) AndAlso sleepFidEl.ValueKind = JsonValueKind.String Then
                Dim sofStr = sleepFidEl.GetString()
                If String.IsNullOrEmpty(sofStr) Then
                    ' Empty string = "no sleep outfit". Equivale a Some(0). (mismo criterio que _npcm_DefaultOutfit)
                    preset.SleepOutfitFormIDOverride = 0UI
                Else
                    ' Mismo criterio que los dos de arriba: sin resolver ⇒ Nothing = preservar, nunca CLEAR.
                    Dim sofResolved = ResolveFormIdentifier(sofStr, pluginManager)
                    If sofResolved <> 0UI Then
                        preset.SleepOutfitFormIDOverride = sofResolved
                    Else
                        preset.UnresolvedSleepOutfit = sofStr   ' ver la nota de `_npcm_SkinFormID`
                        Dim sofMissing = sofStr
                        Logger.LogLazy(Function() $"[LMLoad] _npcm_SleepOutfit '{sofMissing}' no resuelve " &
                                                  "(su plugin no está en el load order) -> se preserva el SOFT del NPC.")
                    End If
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
        ' El motor lee el hex con `sscanf_s(modForm, "%X")` (f4ee Utilities.cpp:140; skee64 FileUtils.cpp:212):
        ' acepta prefijo 0x, signo y corta en el primer caracter no-hex. Sin digitos ⇒ 0.
        Dim localFormID As UInteger = Jsoncpp.ClaveSscanfX(hex)

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

    ''' <summary>⭐ ÚNICA LEY de "lo que la app no pudo resolver, se preserva", del overlay al snapshot
    ''' que arma <c>MainForm.BuildPresetFromState</c>. Se llama FUERA del gate de juego: el agujero era
    ''' de los DOS lados y por eso vive en una sola función.
    '''
    ''' <para><b>Qué preserva y por qué.</b> Un preset nombra head parts y color de pelo por
    ''' <c>Plugin.esp|FORMID</c>. Si ese mod no está instalado la app no puede resolverlo, y la ley —que
    ''' el resto del código ya sigue— es guardarlo VERBATIM para que un cargar→guardar no lo destruya.
    ''' <c>BuildPresetFromState</c> no poblaba ninguno de los tres campos, así que el escritor los
    ''' recibía vacíos.</para>
    '''
    ''' <para><b>MEDIDO sobre el disco del usuario:</b> FO4 pierde <b>236 identificadores de head part en
    ''' 201 de 368 presets</b>, y <b>70 de 368</b> traen un HairColor de un mod ausente. El agujero del
    ''' color de pelo deja además <c>HairColorFormID = 0</c> en <b>322 de 4.474 NPC de FO4</b> (7,2 %) y
    ''' <b>2.248 de 7.206 de SSE</b>, donde "no resolvió" se vuelve indistinguible de "no trae color".</para>
    '''
    ''' <para>⛔ <b>POR QUÉ NO ES `ClonePreset` + pisar.</b> La refactorización obvia está MEDIDA y
    ''' RECHAZADA: <c>BuildPresetFromState</c> llena sus colecciones con <c>.Add</c>/<c>.AddRange</c>, así
    ''' que clonar primero duplicaría head parts, el MRSV (5→10 floats), cada capa de tinte y cada
    ''' tatuaje; y arrastraría <c>HeadPartFormIDsIncludeRawExtras = True</c>, que hace que el saver deje
    ''' de unir el PNAM crudo (40/40 NPC de FO4 perdían lashes/AO/wet/hairlines en su día).</para>
    '''
    ''' <para>⚠️ <b>Límite conocido, declarado y NO cerrado por esta función.</b>
    ''' <c>MainForm.StripEspFieldsFromOverlay</c> reconstruye el overlay DESDE EL SIDECAR después de un
    ''' Save ESP. Los tres campos viajan en el sidecar (ver <c>BssliderSidecar</c> esquema 15), así que
    ''' la preservación sobrevive a ese paso; lo que no sobrevive es un overlay que nunca pasó por el
    ''' sidecar.</para>
    '''
    ''' <para><b>DIVERGENCIA DELIBERADA del canónico, decidida por el usuario (24-ago-2026):</b> ni
    ''' RaceMenu ni LooksMenu preservan esto — <c>PresetInterface.cpp:355-365</c> arma el array desde
    ''' <c>npc-&gt;headparts</c> (lo que el ACTOR tiene) y <c>:978-987</c> nunca mete la que no resuelve.
    ''' Acá se preserva porque esto es un EDITOR, con el mismo criterio que la app ya aplicaba al color de
    ''' pelo ("PRESERVACIÓN, no invención").</para></summary>
    Public Sub CopyUnresolvedHeadPartsToSnapshot(overlay As LooksmenuPreset, snapshot As LooksmenuPreset)
        If overlay Is Nothing OrElse snapshot Is Nothing Then Return
        ' Idempotente: el snapshot es recién construido, pero si algún día se llamara dos veces sobre el
        ' mismo objeto no puede duplicar. Misma razón que el Clear de RaceMenuPresetMapper.
        snapshot.UnresolvedHeadParts.Clear()
        snapshot.SseUnresolvedHeadParts.Clear()
        If overlay.UnresolvedHeadParts IsNot Nothing Then
            snapshot.UnresolvedHeadParts.AddRange(overlay.UnresolvedHeadParts)
        End If
        If overlay.SseUnresolvedHeadParts IsNot Nothing Then
            snapshot.SseUnresolvedHeadParts.AddRange(overlay.SseUnresolvedHeadParts)
        End If
        ' El color de pelo NO se pisa si el snapshot ya resolvió uno: el control negativo está medido —
        ' con `HairColorFormID <> 0` el `ElseIf` de SerializePreset NO emite el identificador crudo, así
        ' que copiarlo igual sería cargar un campo que nadie va a leer.
        If snapshot.HairColorFormID = 0UI AndAlso Not String.IsNullOrEmpty(overlay.UnresolvedHairColor) Then
            snapshot.UnresolvedHairColor = overlay.UnresolvedHairColor
        End If
    End Sub

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
        ' SSE-only verbatim unresolved head parts must travel with the clone too — otherwise a
        ' load→(copy/snapshot)→save would drop the preserved parts that ToJslot re-emits.
        c.SseUnresolvedHeadParts.AddRange(p.SseUnresolvedHeadParts)
        c.SseHeadPartsFiltradasPorMotor.AddRange(p.SseHeadPartsFiltradasPorMotor)
        c.HairColorFormID = p.HairColorFormID
        c.UnresolvedHairColor = p.UnresolvedHairColor
        ' Los tres crudos viajan con el clon por lo mismo que los de arriba: el informe de compatibilidad se
        ' arma sobre el preset que tiene en la mano, y si el clon los pierde el aviso desaparece.
        c.UnresolvedSkin = p.UnresolvedSkin
        c.UnresolvedDefaultOutfit = p.UnresolvedDefaultOutfit
        c.UnresolvedSleepOutfit = p.UnresolvedSleepOutfit
        c.SseHeadTextureFormIDOverride = p.SseHeadTextureFormIDOverride
        c.SseHairColorRgb = p.SseHairColorRgb
        c.SkinToneOffset = SkinToneQnamOffset.CloneOrNothing(p.SkinToneOffset)
        c.WeightThin = p.WeightThin
        c.WeightMuscular = p.WeightMuscular
        c.WeightFat = p.WeightFat

        For Each kv In p.ChargenFaceMorphs : c.ChargenFaceMorphs(kv.Key) = kv.Value : Next
        c.BodyMorphValues.AddRange(p.BodyMorphValues)
        For Each kv In p.FaceBoneRegions
            c.FaceBoneRegions(kv.Key) = CType(kv.Value?.Clone(), Single())
        Next
        c.FacialMorphIntensity = p.FacialMorphIntensity
        For Each tl In p.FaceTintLayers
            c.FaceTintLayers.Add(CloneFaceTintLayer(tl))
        Next

        ' Has flags must be carried with the lists they describe — without these the wipe vs
        ' preserve semantics differ between original and clone.
        c.HasGender = p.HasGender
        c.HasFaceTintLayers = p.HasFaceTintLayers
        c.HasChargenFaceMorphs = p.HasChargenFaceMorphs
        c.HasBodyMorphValues = p.HasBodyMorphValues
        c.HasFaceBoneRegions = p.HasFaceBoneRegions
        c.HasFacialMorphIntensity = p.HasFacialMorphIntensity
        c.HasHeadPartFormIDs = p.HasHeadPartFormIDs
        c.HasBodyMorphSliders = p.HasBodyMorphSliders
        c.HeadPartFormIDsIncludeRawExtras = p.HeadPartFormIDsIncludeRawExtras
        c.SuppressedRawHeadPartFormIDs = New HashSet(Of UInteger)(p.SuppressedRawHeadPartFormIDs)

        For Each kv In p.BodyMorphSliders : c.BodyMorphSliders(kv.Key) = kv.Value : Next

        ' BodyMorphsKeyed (SSE-only, nullable) — deep-copy the nested dict so the clone is independent.
        If p.BodyMorphsKeyed IsNot Nothing Then
            c.BodyMorphsKeyed = New Dictionary(Of String, Dictionary(Of String, Single))(StringComparer.OrdinalIgnoreCase)
            For Each kv In p.BodyMorphsKeyed
                Dim inner As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
                If kv.Value IsNot Nothing Then
                    For Each ik In kv.Value : inner(ik.Key) = ik.Value : Next
                End If
                c.BodyMorphsKeyed(kv.Key) = inner
            Next
        End If

        ' Overlays — deep-copy each entry (cloning the float arrays so the clone is independent).
        ' HasOverlays travels with the list, same as the other Has* flags above.
        For Each ov In p.Overlays
            c.Overlays.Add(New OverlayEntry With {
                .TemplateId = ov.TemplateId,
                .Priority = ov.Priority,
                .Tint = CType(ov.Tint?.Clone(), Single()),
                .OffsetUV = CType(ov.OffsetUV?.Clone(), Single()),
                .ScaleUV = CType(ov.ScaleUV?.Clone(), Single())
            })
        Next
        c.HasOverlays = p.HasOverlays

        ' SSE body overlays (path-based RaceMenu tattoos) — deep-copy (SSE-only, nullable). FO4 leaves
        ' SseBodyOverlays = Nothing so this no-ops on FO4.
        c.SseBodyOverlays = CloneSseBodyOverlays(p.SseBodyOverlays)
        ' SSE RaceMenu node transforms (body-scale) — deep-copy (SSE-only, nullable).
        c.SseNodeTransforms = CloneSseNodeTransforms(p.SseNodeTransforms)
        ' Lista de strings: se copia la LISTA (los strings son inmutables, no hace falta clonar cada uno).
        c.SseFirstPersonTransformsRaw = If(p.SseFirstPersonTransformsRaw Is Nothing, Nothing,
                                           New List(Of String)(p.SseFirstPersonTransformsRaw))
        ' SSE RaceMenu skin overrides (body-paint) — deep-copy (SSE-only, nullable).
        c.SseSkinOverrides = CloneSseSkinOverrides(p.SseSkinOverrides)

        c.UnsupportedCounts.Overlays = p.UnsupportedCounts.Overlays
        c.UnsupportedCounts.BodyMorphSliders = p.UnsupportedCounts.BodyMorphSliders
        c.UnsupportedCounts.HasSkinOverride = p.UnsupportedCounts.HasSkinOverride

        ' Editor-only overrides (not part of the LM JSON schema, but live in the in-memory overlay).
        c.IsCharGenFacePreset = p.IsCharGenFacePreset
        c.SkinFormIDOverride = p.SkinFormIDOverride
        c.DefaultOutfitFormIDOverride = p.DefaultOutfitFormIDOverride
        c.SleepOutfitFormIDOverride = p.SleepOutfitFormIDOverride
        c.SkinTemplateId = p.SkinTemplateId
        c.SseWeight = p.SseWeight

        ' SSE head morphs (NAM9 18 floats + NAMA 4 type uints) — deep-copy the arrays so the clone is
        ' independent; carry HasSseMorphs with them. FO4 leaves these Nothing/False so this no-ops there.
        c.SseNam9 = CType(p.SseNam9?.Clone(), Single())
        c.SseNama = CType(p.SseNama?.Clone(), UInteger())
        c.SseVampireMorph = p.SseVampireMorph
        c.HasSseMorphs = p.HasSseMorphs

        ' SSE face tints — copia independiente de cada capa.
        c.SseTintLayers = PresetCategoryFilter.CloneSseTintLayers(p.SseTintLayers)
        c.HasSseTints = p.HasSseTints
        ' Per-layer custom tint mask texture override (index → path) — copy the map.
        If p.SseTintTexOverride IsNot Nothing Then
            c.SseTintTexOverride = New Dictionary(Of Integer, String)(p.SseTintTexOverride)
        End If

        ' SSE RaceMenu sidecar (per-vertex sculpt + NiOverride custom morphs) — deep-copy (SSE-only, nullable).
        If p.SseSculptHead IsNot Nothing Then
            Dim sc As New List(Of NPC_SculptVert)(p.SseSculptHead.Count)
            For Each sv In p.SseSculptHead
                If sv Is Nothing Then Continue For
                sc.Add(New NPC_SculptVert With {.Index = sv.Index, .Dx = sv.Dx, .Dy = sv.Dy, .Dz = sv.Dz})
            Next
            c.SseSculptHead = sc
        End If
        c.SseSculptParts = CloneSseSculptParts(p.SseSculptParts)
        If p.SseCustomMorphs IsNot Nothing Then
            Dim cms As New List(Of NPC_CustomMorph)(p.SseCustomMorphs.Count)
            For Each cm In p.SseCustomMorphs
                If cm Is Nothing Then Continue For
                cms.Add(New NPC_CustomMorph With {.Name = cm.Name, .Value = cm.Value})
            Next
            c.SseCustomMorphs = cms
        End If

        For Each fid In p.LmTemplateInjectedHdptFormIDs : c.LmTemplateInjectedHdptFormIDs.Add(fid) : Next
        c.HasHeadPartFormIDsSetByTemplate = p.HasHeadPartFormIDsSetByTemplate
        Return c
    End Function

    ''' <summary>Deep-clone a list of SSE RaceMenu body-overlay nodes (path-based tattoos). Nothing in →
    ''' Nothing out (preserves the "no overlays" nullable state). Single source of truth for copying this
    ''' SSE-only carrier across ClonePreset / sidecar hydrate / save-residual / editor snapshot.</summary>
    Public Function CloneSseBodyOverlays(src As List(Of RaceMenuJslot.JslotOverlayNode)) As List(Of RaceMenuJslot.JslotOverlayNode)
        If src Is Nothing Then Return Nothing
        Dim copy As New List(Of RaceMenuJslot.JslotOverlayNode)(src.Count)
        For Each ov In src
            If ov Is Nothing Then Continue For
            ' Clone() (not a field-by-field copy) so the unmodeled-key preservation (RawValues) rides along.
            copy.Add(ov.Clone())
        Next
        Return copy
    End Function

    ''' <summary>Deep-clone the SSE node-transform carrier (RaceMenu body-scale). Nothing → Nothing.
    ''' Single source of truth for copying across ClonePreset / sidecar / Copy-Paste / editor snapshot.</summary>
    Public Function CloneSseNodeTransforms(src As List(Of RaceMenuJslot.JslotNodeTransform)) As List(Of RaceMenuJslot.JslotNodeTransform)
        If src Is Nothing Then Return Nothing
        Dim copy As New List(Of RaceMenuJslot.JslotNodeTransform)(src.Count)
        For Each nt In src
            If nt Is Nothing Then Continue For
            copy.Add(nt.Clone())
        Next
        Return copy
    End Function

    ''' <summary>Deep-clone the SSE skin-override carrier (RaceMenu body-paint). Nothing → Nothing.
    ''' Single source of truth for copying across ClonePreset / sidecar / Copy-Paste / editor snapshot.</summary>
    Public Function CloneSseSkinOverrides(src As List(Of RaceMenuJslot.JslotSkinOverride)) As List(Of RaceMenuJslot.JslotSkinOverride)
        If src Is Nothing Then Return Nothing
        Dim copy As New List(Of RaceMenuJslot.JslotSkinOverride)(src.Count)
        For Each sk In src
            If sk Is Nothing Then Continue For
            copy.Add(sk.Clone())
        Next
        Return copy
    End Function

    ''' <summary>Deep-clone the SSE per-SHAPE sculpt blocks (host + per-vertex deltas). Nothing in, Nothing out.</summary>
    Public Function CloneSseSculptParts(src As List(Of NPC_SculptPart)) As List(Of NPC_SculptPart)
        If src Is Nothing Then Return Nothing
        Dim copy As New List(Of NPC_SculptPart)(src.Count)
        For Each p In src
            If p Is Nothing Then Continue For
            Dim verts As New List(Of NPC_SculptVert)(If(p.Verts IsNot Nothing, p.Verts.Count, 0))
            If p.Verts IsNot Nothing Then
                For Each sv In p.Verts
                    If sv Is Nothing Then Continue For
                    verts.Add(New NPC_SculptVert With {.Index = sv.Index, .Dx = sv.Dx, .Dy = sv.Dy, .Dz = sv.Dz})
                Next
            End If
            copy.Add(New NPC_SculptPart With {.Host = If(p.Host, ""), .Verts = verts})
        Next
        Return copy
    End Function

    ''' <summary>Deep-clone a single tint layer. Used by ClonePreset and by call sites that
    ''' need to copy individual layers without cloning the full preset.</summary>
    Public Function CloneFaceTintLayer(tl As CapaDeTintePreset) As CapaDeTintePreset
        If tl Is Nothing Then Return Nothing
        Return New CapaDeTintePreset With {
            .Discriminator = tl.Discriminator,
            .Index = tl.Index,
            .Value = tl.Value,
            .Color = tl.Color,
            .TemplateColorIndex = tl.TemplateColorIndex,
            .ConversionFallida = tl.ConversionFallida
        }
    End Function

    ''' <summary>Las capas de tinte que el NPC AUTORA en su record, pasadas a capas de preset. Es la
    ''' operacion de "arranca el preset con lo que el NPC ya tiene", no un espejo permanente: la copia
    ''' se desprende del record en el momento en que se hace.</summary>
    Public Function CapasDeTinteDelRecord(npc As Canon.INpc) As List(Of CapaDeTintePreset)
        Dim salida As New List(Of CapaDeTintePreset)
        For Each m In FaceTintInputBuilder.CapasAutoradasDelRecord(npc)
            salida.Add(New CapaDeTintePreset With {
                .Discriminator = m.Discriminator, .Index = m.Index, .Value = m.Value,
                .Color = m.Color, .TemplateColorIndex = m.TemplateColorIndex})
        Next
        Return salida
    End Function

    ''' <summary>Las capas de tinte de Skyrim que el NPC trae en su record, pasadas a capas de preset.
    ''' Cada campo viaja con su presencia: lo que el record no declara, el preset tampoco.</summary>
    Public Function CapasDeTinteSseDelRecord(npc As Canon.INpc) As List(Of CapaDeTinteSsePreset)
        Dim salida As New List(Of CapaDeTinteSsePreset)
        Dim ns = TryCast(npc, Canon.NpcSSE)
        If ns Is Nothing Then Return salida
        For Each tl In ns.TintLayers
            Dim c As New CapaDeTinteSsePreset
            If tl.LayerTintIndexPresente Then c.Indice = tl.LayerTintIndex
            If tl.TintColorRedPresente Then c.Rojo = tl.TintColorRed
            If tl.TintColorGreenPresente Then c.Verde = tl.TintColorGreen
            If tl.TintColorBluePresente Then c.Azul = tl.TintColorBlue
            If tl.TintColorAlphaPresente Then c.Alfa = tl.TintColorAlpha
            If tl.LayerInterpolationValuePresente Then c.Cobertura = tl.LayerInterpolationValue
            If tl.LayerPresetPresente Then c.Preseleccion = tl.LayerPreset
            salida.Add(c)
        Next
        Return salida
    End Function

    ''' <summary>Vuelca las capas de tinte del preset al record, reemplazando las que tenia. Lo que la
    ''' capa no declara no se escribe: el record queda sin ese campo, igual que la fuente.</summary>
    Public Sub EscribirCapasDeTinteSse(npc As Canon.INpc, capas As IEnumerable(Of CapaDeTinteSsePreset))
        Dim ns = TryCast(npc, Canon.NpcSSE)
        If ns Is Nothing Then Return
        While ns.TintLayers.Count > 0
            If Not ns.QuitarTintLayers(0) Then Exit While
        End While
        If capas Is Nothing Then Return
        For Each c In capas
            If c Is Nothing Then Continue For
            Dim tl = ns.AgregarTintLayers()
            If tl Is Nothing Then Return
            If c.Indice.HasValue Then tl.LayerTintIndex = c.Indice.Value
            If c.Rojo.HasValue Then tl.TintColorRed = c.Rojo.Value
            If c.Verde.HasValue Then tl.TintColorGreen = c.Verde.Value
            If c.Azul.HasValue Then tl.TintColorBlue = c.Azul.Value
            If c.Alfa.HasValue Then tl.TintColorAlpha = c.Alfa.Value
            If c.Cobertura.HasValue Then tl.LayerInterpolationValue = c.Cobertura.Value
            If c.Preseleccion.HasValue Then tl.LayerPreset = c.Preseleccion.Value
        Next
    End Sub

    ''' <summary>Serializa el preset al JSON de LooksMenu. Overload de conveniencia que descarta la lista de
    ''' campos omitidos; usar el de tres argumentos cuando haya que avisarle al usuario.</summary>
    Public Function SerializePreset(preset As LooksmenuPreset, pluginManager As PluginManager) As String
        Dim ignored As List(Of String) = Nothing
        Return SerializePreset(preset, pluginManager, ignored)
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
    ''' <param name="omittedFields">Campos <c>_npcm_*</c> que NO se pudieron nombrar y por lo tanto quedaron
    ''' FUERA del preset (uno por línea, ya redactado para mostrarle al usuario). Vacío = salió completo.
    ''' Existe porque omitir la key es la respuesta correcta pero MUDA: el usuario guardaría un preset
    ''' creyendo que lleva su outfit/piel y no lo lleva. El caso típico es un draft sin guardar todavía.</param>
    Public Function SerializePreset(preset As LooksmenuPreset, pluginManager As PluginManager,
                                    ByRef omittedFields As List(Of String)) As String
        omittedFields = New List(Of String)
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
                ' (CharGenInterface.cpp:214-225): `root["BodyMorphs"] = morphData` iff `morphMap` exists for
                ' the actor; with un morphMap SIN entradas `morphData` queda como Json::Value nulo ⇒ el archivo
                ' trae `"BodyMorphs": null`. Sin morphMap la clave se OMITE.
                ' On Load (CharGenInterface.cpp:568-577) the actor's morphs are wiped iff the key has members
                ' OR it is present and null; an ABSENT key or an EMPTY object `{}` PRESERVES the actor state.
                ' Es el UNICO canal donde ausente y null cargan distinto, asi que aca se emite lo que el
                ' estado del preset dice: `HasBodyMorphSliders` = «el motor escribiria este canal» (el snapshot
                ' de BuildPresetFromState lo pone en True) ⇒ con sliders el objeto, sin sliders `null` (= wipe
                ' al recargar, que es el estado que se guardo: un NPC sin sliders). Sin la bandera y sin
                ' sliders se omite (= preservar). Antes se omitia siempre que estuviera vacio, y un preset
                ' guardado desde un NPC sin sliders dejaba al destino con los suyos.
                If preset.BodyMorphSliders IsNot Nothing AndAlso preset.BodyMorphSliders.Count > 0 Then
                    w.WriteStartObject("BodyMorphs")
                    Dim bmKeys = preset.BodyMorphSliders.Keys.OrderBy(Function(k) k, StringComparer.Ordinal).ToList()
                    For Each k In bmKeys
                        w.WriteNumber(k, preset.BodyMorphSliders(k))
                    Next
                    w.WriteEndObject()
                ElseIf preset.HasBodyMorphSliders Then
                    w.WriteNull("BodyMorphs")
                End If

                ' Gender (always)
                w.WriteNumber("Gender", CUInt(preset.Gender))

                ' HairColor — only when non-zero (CharGenInterface.cpp:106-110)
                If preset.HairColorFormID <> 0UI Then
                    Dim hc = FormatFormIdentifier(preset.HairColorFormID, pluginManager)
                    If Not String.IsNullOrEmpty(hc) Then w.WriteString("HairColor", hc)
                ElseIf Not String.IsNullOrWhiteSpace(preset.UnresolvedHairColor) Then
                    ' PRESERVACIÓN, no invención: se re-emite el identificador CRUDO, tal cual vino, para
                    ' que un preset cuyo mod de color no está instalado no PIERDA el color al guardarse.
                    ' Mismo criterio que SseUnresolvedHeadParts (:698-699).
                    ' HOY ESTA RAMA NO SE ALCANZA: el único caller de SerializePreset es "Save Looksmenu"
                    ' (MainForm), que arma el preset con BuildPresetFromState — o sea desde el ESTADO del NPC,
                    ' nunca desde un preset leído de disco, así que UnresolvedHairColor viene vacío. Es una
                    ' red para el día que alguien serialice un preset cargado o clonado (ClonePreset SÍ
                    ' propaga el campo). No la saco: sin ella ese día se pierde el dato en silencio.
                    ' Los caminos que reemplazan el color a propósito lo limpian antes
                    ' (PresetCategoryFilter, EditFace_Form.OnHairColorChanged), así que no puede re-emitir un
                    ' color descartado. Y no duplica la clave: es un ElseIf de la rama que resuelve.
                    w.WriteString("HairColor", preset.UnresolvedHairColor)
                End If

                ' HeadParts (always, even if empty array)
                w.WriteStartArray("HeadParts")
                For Each fid In preset.HeadPartFormIDs
                    Dim ident = FormatFormIdentifier(fid, pluginManager)
                    If Not String.IsNullOrEmpty(ident) Then w.WriteStringValue(ident)
                Next
                ' ⛔ DIVERGENCIA DELIBERADA CON EL CANÓNICO, decidida por el usuario (24-ago-2026).
                ' El motor NO conserva esto: `CharGenInterface.cpp:92-103` arma el array recorriendo las
                ' partes que el ACTOR TIENE, y `:324-337` saltea con `continue` la que no resuelve al
                ' aplicar — así que un ciclo cargar→aplicar→guardar dentro del propio LooksMenu pierde la
                ' entrada igual. No es paridad lo que se busca acá: es que un EDITOR no tire en silencio
                ' un dato que entró por la puerta. Es el MISMO criterio que esta clase ya aplica al color
                ' de pelo unas líneas más arriba ("PRESERVACIÓN, no invención").
                ' MEDIDO sobre los 368 presets del usuario: 236 identificadores de 12 mods no instalados,
                ' en 201 de 368 archivos (MiscHairstyle.esp 86, Lots More Facial Hair.esp 46, Lots More
                ' Male Hairstyles.esp 40, …). Sin esto, abrir uno de esos presets y guardarlo lo deja
                ' CALVO para siempre: reinstalar el mod ya no lo recupera.
                ' ⚠️ Van al FINAL del array, no en su posición original: el orden del canónico es el de las
                ' partes del actor y una entrada que no resuelve no está en el actor, así que no hay
                ' posición que preservar. El motor las vuelve a saltear al cargar, así que el orden es
                ' inocuo para él.
                ' ⚠️ SIN DEDUP, A PROPÓSITO: `UnresolvedHeadParts.Add` (:412) no comprueba repetidos —la
                ' rama SSE sí lo hace— y `ClonePreset:803` acumula con `AddRange`. Si eso puede repetir un
                ' identificador hay que medirlo antes de decidir qué hacer, no taparlo acá.
                If preset.UnresolvedHeadParts IsNot Nothing Then
                    For Each ident In preset.UnresolvedHeadParts
                        If Not String.IsNullOrEmpty(ident) Then w.WriteStringValue(ident)
                    Next
                End If
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
                            ' the trailing scale-or-padding slot if we somehow have more (the FMRS
                            ' schema itself is 7 floats + a trailing Wb.Bytes("Unknown", -1) — see the
                            ' "Face Morph" struct in WbSchemaGen_FO4.vb).
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
                        ' (CharGenInterface.cpp:248-249 appends offsetUV.x/y into the scaleUV array
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
                            ' as B<<16 | G<<8 | R, NOT R<<16 | G<<8 | B).
                            ' A is forced to 0: the app always treats a TEND Color as opaque
                            ' (Color.FromArgb(255, R, G, B) — the record's TEND Color has no real
                            ' alpha bit, its 4th byte is Unused in the schema), but a Color with
                            ' bit 31 set serializes as negative Int32 in jsoncpp, and LooksMenu's
                            ' asUInt() then asserts → entire Tints block is silently dropped via
                            ' try/catch.
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
                ' por LM in-game. Verificado contra Script extenders, Racemenu y Looksmenu/F4SEPlugins/f4ee/CharGenInterface.cpp.
                ' Independientes entre sí; la precedencia en aplicación la resuelve
                ' NpcRecordOverlay (orden: NPC.WNAM primero, luego LM SkinTemplate pisa si está
                ' set), mismo orden que el overlay aplica a render.
                ' LOS TRES ESTADOS, DEL LADO DEL ESCRITOR. El campo vale `Nothing`=preservar (key AUSENTE),
                ' `Some(0)`=CLEAR explícito (string VACÍO) o un valor=override (el identificador).
                ' `FormatFormIdentifier` devuelve "" en DOS casos que no son lo mismo: el valor 0, y "no pude
                ' nombrar el plugin dueño". Emitir "" en el segundo caso le dice al lector CLEAR — y el lector
                ' (:610-681) lo obedece: NpcRecordOverlay:131/157 apaga la piel o el outfit del NPC.
                ' Camino de rutina: un outfit recién creado en Edit Outfit tiene FormID provisional 0xFF00xxxx
                ' (Borradores.FormIdAltoDeBorrador), y 0xFF NUNCA es un slot (MAX_FULL_SLOT = 0xFD), así que
                ' GetOriginatingPluginName devuelve "" ⇒ el preset salía diciendo "sin outfit" ⇒ NPC DESNUDO.
                ' Canónico: f4ee saltea el form que no resuelve y no toca al actor — `if(!form) continue`
                ' (CharGenInterface.cpp:328-330). Omitir la key es exactamente eso, y es la simetría del
                ' `ElseIf Logger.Enabled` que el LECTOR ya tiene en :630-638.
                EmitNpcmFormIdentifier(w, "_npcm_SkinFormID", "skin", preset.SkinFormIDOverride, pluginManager, omittedFields)
                EmitNpcmFormIdentifier(w, "_npcm_DefaultOutfit", "default outfit", preset.DefaultOutfitFormIDOverride, pluginManager, omittedFields)
                EmitNpcmFormIdentifier(w, "_npcm_SleepOutfit", "sleep outfit", preset.SleepOutfitFormIDOverride, pluginManager, omittedFields)
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

    ''' <summary>Emite una key <c>_npcm_*</c> respetando sus TRES estados, o la OMITE cuando el FormID no se
    ''' puede nombrar. Ver el bloque de comentarios del llamador para el porqué.
    ''' <list type="bullet">
    ''' <item><c>Nothing</c> ⇒ key ausente = "preservar lo que tenga el NPC".</item>
    ''' <item><c>0</c> ⇒ string vacío = CLEAR explícito (el único caso en que "" es correcto).</item>
    ''' <item>valor con identificador ⇒ el identificador.</item>
    ''' <item>valor SIN identificador ⇒ <b>key omitida</b> (= preservar) + log + se anota en
    ''' <paramref name="omittedFields"/> para que la UI lo pueda decir.</item>
    ''' </list></summary>
    Private Sub EmitNpcmFormIdentifier(w As Utf8JsonWriter, keyName As String, label As String,
                                       value As UInteger?, pluginManager As PluginManager,
                                       omittedFields As List(Of String))
        If Not value.HasValue Then Return
        If value.Value = 0UI Then
            w.WriteString(keyName, "")   ' CLEAR explícito
            Return
        End If
        Dim ident = FormatFormIdentifier(value.Value, pluginManager)
        If ident <> "" Then
            w.WriteString(keyName, ident)
            Return
        End If
        Dim fid = value.Value
        Logger.LogLazy(Function() $"[LMSave] {keyName} {fid:X8}: no pertenece a ningún plugin cargado " &
                                  "(típicamente un record todavía sin guardar al ESP), así que la key se OMITE " &
                                  "— el preset preserva lo que tenga el NPC en vez de borrárselo.")
        omittedFields.Add($"{label} (FormID {fid:X8})")
    End Sub

    ''' <summary>Inverse of ResolveFormIdentifier: take a global FormID, find its owning plugin
    ''' in the load order, and emit "Plugin.esp|HEX" with the local 24-bit FormID.</summary>
    Friend Function FormatFormIdentifier(globalFormID As UInteger, pluginManager As PluginManager) As String
        If globalFormID = 0UI Then Return ""
        ' GetOriginatingPluginName handles both full (high byte = full slot) and ESL (0xFE light) globals.
        Dim pluginName = pluginManager.GetOriginatingPluginName(globalFormID)
        If String.IsNullOrEmpty(pluginName) Then Return ""
        ' The local is the owner's OBJECT ID with NO load-order information in it: 12 bits for a light
        ' plugin, 24 for a full one. That is precisely Bethesda's own ModInfo::GetFormID
        ' (f4se GameData.h:93-96):
        '     !IsLight() ? modIndex << 24 | (formLower & 0xFFFFFF)
        '                : 0xFE000000 | (lightIndex << 12) | (formLower & 0xFFF)
        ' so ToFaceGenLocalFormID is exactly its inverse.
        '
        ' This used to write `globalFormID And 0xFFFFFF`, which for a LIGHT owner keeps the light slot
        ' in bits 12..23 — the slot of whichever session wrote the file. Verified against BOTH engines:
        '   * SSE/skee64 reads via modInfo->GetFormID (FileUtils.cpp:219), which MASKS to 0xFFF, so a
        '     stale slot is discarded — tolerant.
        '   * FO4/f4ee hand-rolls the reconstruction instead and ORs 24 raw bits
        '     (Utilities.cpp:147-151, BodyGenInterface.cpp:319-321), so a stale slot MERGES with the
        '     current one into a third, bogus slot — the morphs then land on the wrong NPC or nowhere.
        ' Writing the bare object id is correct under BOTH: the masking engine no-ops it, the OR-ing
        ' engine gets clean bits. It is the only form that is right either way.
        Dim localFormID = PluginManager.ToFaceGenLocalFormID(globalFormID)
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
    ''' Returns absolute paths in alphabetical order, recursing into all subfolders so user-organized
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
