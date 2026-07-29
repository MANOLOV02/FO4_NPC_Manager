Scriptname NPCM_Manolov_ApplyFO4 extends Actor
{
  NPC Manager (Manolov) — applies the LooksMenu/f4ee options that CANNOT live in an ESP record and
  CANNOT be baked, to an NPC, on its first spawn. FO4 counterpart of NPCM_Manolov_ApplySSE.

  Attached to the NPC_ base record via VMAD. A script on an ActorBase is inherited by every
  reference of it, and Papyrus `Actor` extends `ObjectReference`, so OnLoad() fires per spawned
  actor. Verified against vanilla: 382 of 3015 Fallout4.esm NPC_ ship base-attached scripts that
  work exactly this way (TeleportActorScript, WorkshopNPCScript, ...).

  ⚠ THIS IS NOT A PORT OF THE SSE SCRIPT. LooksMenu exposes only TWO Papyrus classes (`Overlays`
  and `BodyGen` — f4ee main.cpp:465-469 registers nothing else), so the surface is much smaller:

    * Overlays       — FULL support, and richer than SSE: template + tint + UV offset/scale +
                       priority, all carried in the `Overlays:Entry` struct.
    * Skin override  — by TEMPLATE ID ONLY (BodyGen.SetSkinOverride takes a string id naming a skin
                       template defined in f4ee's JSON). There is NO per-slot diffuse/normal/tint
                       from Papyrus: the fine-grained C++ path exists but is Scaleform-only
                       (ScaleformNatives.cpp F4EEScaleform_SetSkinOverride), unreachable from script.
    * Node transforms— DO NOT EXIST IN FALLOUT 4. Not "not exposed": f4ee's TransformInterface is
                       behind #ifdef _TRANSFORMS, is never registered to Papyrus, and is not even
                       serialized to the co-save. There is nothing to call. (Also moot: node
                       transforms are an SSE-only feature in the app.)

    * Body morphs    — DELIVERED HERE NOW (BodyGen.SetMorph), and the BodyGen .ini is then NOT emitted
                       for the same plugin. They cannot coexist: f4ee combines the per-keyword values of
                       a morph with MAX (UserValues::GetEffectiveValue, BodyMorphInterface.cpp:1001-1009),
                       and BodyGen writes to the SAME keyword slot we do (nullptr / None — see
                       BodyGenInterface.cpp:517). See ApplyBodyMorphs() below.

  NOT DONE HERE, on purpose (already delivered another way, would otherwise apply twice):
    * Anything face — baked into the FaceGen NIF/textures.
}

bool Property IsFemale_G0000010000 = false Auto

bool Property Verbose_G0000010000 = false Auto
{DIAGNOSTICO. false (default, y lo que se publica) = el script NO traza NADA. La app lo pone en true
 cuando ella misma esta diagnosticando (Logger.Enabled, que es Debug-only). Ver el docstring gemelo en
 NPCM_Manolov_ApplySSE.psc para el razonamiento completo.

 ⭐ Lo que gatea NO es solo el Debug.Trace: es la CONCATENACION (bytecode, se ejecuta siempre) y sobre
 todo LAS NATIVAS DE SONDA (BodyGen.GetMorphs / GetMorph / GetKeywords), que existen unicamente para
 trazar. Y en FO4 eso pesa MAS que en SSE: f4ee NO le pone kFunctionFlag_NoWait a la clase BodyGen
 (solo se lo pone a Overlays), asi que cada nativa hace ceder la VM — medido: 512 SetMorph = ~9 s.

 ⛔ JAMAS envolver una llamada FUNCIONAL (RemoveAllMorphs, SetMorph, UpdateMorphs, Overlays.*):
 si el flag se apagara, el script dejaria de aplicar. Solo se envuelve lo que existe para mirar.}

int Property SchemaVersion_G0000010000 = 1 Auto
{Bumped by the app when the authored values change, so an updated plugin re-applies to actors that
 already spawned in an existing save.}

;-- ⭐⭐⭐ EL SUFIJO `_G<n>` Y EL NOMBRE POR PLUGIN — LO PONE LA APP, NO SE TOCA A MANO ------------
;
; Mismo esquema que SSE, por la misma ley MEDIDA (Skyrim SE 2026-07-28): al cargar la partida el motor
; restaura del savegame la variable que ya tenia (=> RANCIA para siempre) e inicializa desde el VMAD la
; que no tenia (=> FRESCA). Una property `Auto` se compila a variable de script, y las variables se
; serializan: por eso el payload de una version quedaba pegado en toda referencia ya existente.
;
; ⚠️ ESTE ARCHIVO ES UNA PLANTILLA. Se compila con `_G0000010000` y con el nombre `NPCM_Manolov_ApplyFO4`, y
;   ninguno de los dos llega al juego: al guardar el ESP la app reescribe DENTRO del .pex (PexPatcher.vb)
;   el nombre del script y la generacion. NO subir el sufijo ni renombrar el Scriptname a mano.
;
; ⚠️ EN FO4 ESTO NO ESTA MEDIDO TODAVIA. Se porta con la misma forma que SSE y con trazas para
;   confirmarlo en la primera corrida — los dos motores YA difirieron en el manejo de arrays.
;
; `appliedVersion` NO lleva sufijo a proposito: es el lado que tiene que persistir.

;-- overlays (parallel arrays, one entry per overlay) -------------------------------------------
string[] Property OvlTemplate_G0000010000 Auto
{f4ee overlay template id (the `template` member of Overlays:Entry) — from the installed
 overlays.json catalog, NOT a loose texture path.}
int[]   Property OvlPriority_G0000010000 Auto
float[] Property OvlRed_G0000010000 Auto
float[] Property OvlGreen_G0000010000 Auto
float[] Property OvlBlue_G0000010000 Auto
float[] Property OvlAlpha_G0000010000 Auto
float[] Property OvlOffsetU_G0000010000 Auto
float[] Property OvlOffsetV_G0000010000 Auto
float[] Property OvlScaleU_G0000010000 Auto
float[] Property OvlScaleV_G0000010000 Auto

;-- skin override (single template id; "" = none) -----------------------------------------------
string Property SkinTemplate_G0000010000 = "" Auto

;-- body morphs (BodySlide) ---------------------------------------------------------------------
bool Property MorphsOwned_G0000010000 = false Auto
{⛔ QUIEN ES EL DUEÑO DE LOS BODY MORPHS DE ESTE NPC. false = los entrega el par BodyGen .ini y este
 script NO TOCA NADA de morphs (ni siquiera barre); true = los entrega este script.

 NO es un lujo, es obligatorio: nuestro barrido usa el keyword None, que es EL MISMO SLOT que escribe
 BodyGen (BodyGenInterface.cpp:517). Sin este flag, con el modo .ini activo el barrido borraria lo que
 BodyGen acaba de aplicar — o no, segun quien corra primero, porque el orden entre el evento de f4ee y
 el OnLoad de Papyrus NO esta garantizado. Un flag lo vuelve determinista.

 ⚠️ Volver del modo script al modo .ini deja los morphs que este script ya aplico pegados al actor (el
 mapa del actor no queda vacio, asi que BodyGen tampoco lo re-evalua). No es una perdida: los dos modos
 sacan los valores del MISMO sidecar.}
string[] Property MorphName_G0000010000 Auto
{Nombre del morph de BodySlide (la key del .tri PIRT). Array paralelo con MorphValue.}
float[]  Property MorphValue_G0000010000 Auto
{Valor del morph. Entra bajo el keyword None, que es el MISMO slot que usa BodyGen
 (SetMorph(actor, isFemale, name, nullptr, value) — BodyGenInterface.cpp:517) y el unico que se puede
 usar sin agregarle un record KYWD al ESP ni un master de LooksMenu.

 ⚠️ NO se emiten valores 0: UserValues::SetValue BORRA la entrada cuando el valor es exactamente
 cero (BodyMorphInterface.cpp:983-987), asi que un 0 no seria "morph en cero" sino "morph ausente".
 El emisor ya los filtra; esto queda escrito para que nadie lo "arregle" mandandolos.}

;-- per-instance state (persists in the savegame, like vanilla TeleportActorScript) -------------
int appliedVersion = -1

Event OnLoad()
    ; TRAZA: misma instrumentacion que el script de SSE, a proposito -- sin ella no se puede distinguir
    ; tres causas que dan el MISMO sintoma ("el NPC no cambia"): (a) OnLoad no se dispara en esa
    ; referencia, (b) se dispara y se saltea por el sello, (c) se dispara pero con las propiedades
    ; CONGELADAS del savegame, o sea leyendo el payload viejo.
    ; MEDIDO en SSE (2026-07-26, log de Papyrus + VMAD del ESP): es (c). Dos referencias reportaban un
    ; SchemaVersion_G0000010000 que YA NO EXISTE en el plugin, asi que solo podia venir del savegame. La via es la
    ; misma en los dos juegos (propiedades del VMAD), asi que aca se espera lo mismo -- pero se instrumenta
    ; igual en vez de asumirlo, que es como se destapo en SSE.
    ; No se tocan arrays en la traza: el spec de LIMPIEZA no emitia array alguna y un .Length sobre None
    ; tiraria justo en el caso que interesa observar.
    if Verbose_G0000010000
        Debug.Trace("[NPCM] OnLoad ref=" + self.GetFormID() + " appliedVersion=" + appliedVersion + " SchemaVersion_G0000010000=" + SchemaVersion_G0000010000)
    endif

    ; ⚠️ INSTRUMENTADO A PROPOSITO. En SSE esta medido que una array-property que el VMAD NO trae llega
    ; con LONGITUD 0 (no None), y eso es lo que hace de senal para el guard de instancia huerfana. En FO4
    ; NO esta medido, y los dos motores YA difirieron en el manejo de arrays (FO4 tolera arrays vacios,
    ; Skyrim no). Si la linea "antes de tocar" sale y la siguiente NO, llego None y el .Length tiro: ahi
    ; el guard tiene que pasar a un escalar.
    if Verbose_G0000010000
        Debug.Trace("[NPCM] payload ovl=" + OvlTemplate_G0000010000.Length + " skin='" + SkinTemplate_G0000010000 + "'")
        Debug.Trace("[NPCM] BM payload morphs=" + MorphName_G0000010000.Length)
        ; Identidad del primer overlay, gemela de la de SSE: sin esto el log dice CUANTOS llegaron pero no
        ; CUALES, y un template id equivocado se ve igual que uno correcto. Una lectura por traza (quirk del
        ; codegen: indexar el mismo array dos veces en una expresion imprime N veces el ULTIMO elemento).
        if OvlTemplate_G0000010000.Length > 0
            string t0 = OvlTemplate_G0000010000[0]
            Debug.Trace("[NPCM] payload OvlTemplate_G0000010000[0]=" + t0)
        endif
    endif

    ; ⛔ INSTANCIA HUERFANA: no soy la version activa de este actor, no toco NADA.
    ; El nombre del script lleva el del ESP; si el autor renombra su plugin, el savegame se queda con la
    ; instancia del nombre anterior pegada al actor. Esa instancia ya no aparece en el VMAD, no recibe
    ; ninguna property, y sin este guard correria igual, su sello no coincidiria, y su barrido se llevaria
    ; puesto lo que el script activo acaba de aplicar. MEDIDO en SSE 2026-07-28 (dos OnLoad sobre la misma
    ; referencia en el mismo instante).
    ; ⛔ Y NO se borra el .pex huerfano para "arreglarlo": el script extends Actor, asi que si el tipo no
    ; resuelve ese actor queda SIN TABLA DE METODOS PARA TODOS LOS DEMAS SCRIPTS (medido en SSE: RaceMenu
    ; fallando 17 veces sobre un NPC nuestro). El .pex viejo se queda; este guard lo vuelve inofensivo.
    if OvlTemplate_G0000010000.Length == 0
        if Verbose_G0000010000
            Debug.Trace("[NPCM] INERTE: sin payload del VMAD (instancia de un nombre de script viejo)")
        endif
        return
    endif

    if appliedVersion == SchemaVersion_G0000010000
        if Verbose_G0000010000
            Debug.Trace("[NPCM] SKIP: sello igual, no se limpia ni se aplica nada")
        endif
        return                    ; already applied to THIS actor, and nothing changed since
    endif
    appliedVersion = SchemaVersion_G0000010000

    ApplyOverlays()
    ApplySkin()
    ; ⭐ VA DESPUES DE ApplyOverlays A PROPOSITO, y el orden importa. Overlays.Update solo destruye y
    ; reconstruye el SUBARBOL de overlays (F4EEUpdateOverlays::Run, OverlayInterface.cpp:857-910): no
    ; toca los morphs. BodyGen.UpdateMorphs, en cambio, DETACHA los slots morphables y hace
    ; Update3DModel (BodyMorphInterface.cpp:517-556) — un rebuild del modelo que vuelve a disparar el
    ; hook de attach y re-aplica los overlays desde el mapa, que a esta altura ya es el correcto.
    ; Al reves (morphs primero) el rebuild de los morphs seria pisado por el de los overlays.
    ApplyBodyMorphs()
    if Verbose_G0000010000
        Debug.Trace("[NPCM] DONE ref=" + self.GetFormID())
    endif
EndEvent

Function ApplyOverlays()
    Actor a = self as Actor

    ; ⭐⭐ BORRAR EN FO4 ES DISTINTO QUE EN SSE, Y ES MAS SIMPLE. LEIDO EN LA FUENTE DE f4ee:
    ;
    ;   F4EEUpdateOverlays::Run()  (OverlayInterface.cpp:856-914)
    ;       // Delete all overlays
    ;       parent->RemoveChild(overlayRoot);        <- DESTRUYE el subarbol de overlays entero
    ;       // Rebuild overlays
    ;       for (cada slot biped) UpdateOverlays(...) <- lo reconstruye desde el mapa
    ;
    ; O sea: Overlays.Update() DESTRUYE Y RECONSTRUYE. Entonces RemoveAll + Update es un borrado REAL
    ; y completo. skee no tiene nada de esto (su ApplyNodeOverrides solo empuja lo que quedo en el
    ; store y nunca resetea un nodo), y por eso el script de SSE tiene que apagar los nodos a mano
    ; con KEY_ALPHA=0. Aca no hace falta ningun truco.
    ;
    ; ⭐ Y POR ESO YA NO HAY LEDGER DE UIDS. Antes se recordaban los uids minteados por instancia,
    ; porque Overlays.Add() mintea uno nuevo en cada llamada y re-aplicar habria apilado duplicados.
    ; Con RemoveAll el mapa de ese actor queda VACIO y Update lo reconstruye: no queda nada que
    ; apilar. Ademas el ledger se habria perdido igual, porque el nombre del script ahora lleva el
    ; del plugin y una version nueva estrena instancia.
    ;
    ; ⚠️ RemoveAll se lleva TAMBIEN los overlays que otro mod le haya puesto a este actor. Es la
    ; MISMA decision de producto que en SSE, tomada a proposito: el NPC muestra exactamente lo que
    ; muestra la app. f4ee no guarda dueño (su mapa es actor+prioridad+uid).
    Overlays.RemoveAll(a, IsFemale_G0000010000)

    int n = OvlTemplate_G0000010000.Length
    if n == 0
        Overlays.Update(a)
        return
    endif

    int i = 0
    while i < n
        if OvlTemplate_G0000010000[i] != ""
            Overlays:Entry e = new Overlays:Entry
            e.template = OvlTemplate_G0000010000[i]
            ; ⛔ INLINE guards, never a helper taking an array parameter. Papyrus throws
            ; "Cannot cast from None to Float[]" AT THE CALL when the argument is None, so a
            ; `if a == None` inside the helper never runs. That bug took down the SSE script
            ; (20 cast errors, node transforms silently dropped) — same shape here, so the same
            ; fix. See the note at the top of NPCM_Manolov_ApplySSE.psc.
            ; DEFAULTS FIRST, then override from the arrays — so no combination of missing/short arrays
            ; can leave a field at an accidental value.
            ;
            ; ⛔ THE NEUTRAL TINT IS (0,0,0,0), NOT WHITE. This is counter-intuitive and I got it wrong
            ; once: f4ee treats the tint as ABSENT only when it is exactly zero.
            ;   - OverlayData ctor: tintColor = (0,0,0,0)              (OverlayInterface.h:76-79)
            ;   - Preset loader, no "tint" member: color = (0,0,0,0)   (CharGenInterface.cpp:587-597)
            ;   - UpdateFlags(): sets kHasTintColor iff tint != (0,0,0,0)  (OverlayInterface.h:97-100)
            ; So white (1,1,1,1) does NOT mean "no tint" — it TURNS THE FLAG ON. With the flag on, the
            ; engine tints a SkinTint overlay with our colour instead of the NPC's skinColor (a washed-out
            ; patch instead of a tattoo) AND overwrites the material's fLookupScale
            ; (OverlayInterface.cpp:204-218). Zero is the only value that means "leave it alone".
            ;
            ; scale_u/scale_v are the opposite: their neutral IS 1.0. Zero would set kHasScaleUV and
            ; collapse the UVs (OverlayInterface.h:82-83 + .cpp:193-194).
            e.priority = 0
            e.red      = 0.0
            e.green    = 0.0
            e.blue     = 0.0
            e.alpha    = 0.0
            e.offset_u = 0.0
            e.offset_v = 0.0
            e.scale_u  = 1.0
            e.scale_v  = 1.0

            if i < OvlPriority_G0000010000.Length
                    e.priority = OvlPriority_G0000010000[i]
            endif
            if i < OvlRed_G0000010000.Length
                    e.red = OvlRed_G0000010000[i]
            endif
            if i < OvlGreen_G0000010000.Length
                    e.green = OvlGreen_G0000010000[i]
            endif
            if i < OvlBlue_G0000010000.Length
                    e.blue = OvlBlue_G0000010000[i]
            endif
            if i < OvlAlpha_G0000010000.Length
                    e.alpha = OvlAlpha_G0000010000[i]
            endif
            if i < OvlOffsetU_G0000010000.Length
                    e.offset_u = OvlOffsetU_G0000010000[i]
            endif
            if i < OvlOffsetV_G0000010000.Length
                    e.offset_v = OvlOffsetV_G0000010000[i]
            endif
            if i < OvlScaleU_G0000010000.Length
                    e.scale_u = OvlScaleU_G0000010000[i]
            endif
            if i < OvlScaleV_G0000010000.Length
                    e.scale_v = OvlScaleV_G0000010000[i]
            endif

            Overlays.Add(a, IsFemale_G0000010000, e)
        endif
        i += 1
    endwhile

    Overlays.Update(a)
EndFunction

Function ApplySkin()
    ; ⛔ The empty case must REMOVE, not just return. g_skinInterface persists the skin override in the
    ; co-save, so if the user clears the skin template in the app and re-saves, an early return would leave
    ; the OLD override applied to the actor forever — the same "never deletes" hazard the uid ledger fixes
    ; for overlays. BodyGen.RemoveSkinOverride exists precisely for this (PapyrusBodyGen.cpp:114-130).
    if SkinTemplate_G0000010000 == ""
        BodyGen.RemoveSkinOverride(self as Actor)
        return
    endif
    ; SetSkinOverride calls UpdateSkinOverride internally (PapyrusBodyGen.cpp:109) — no refresh needed.
    BodyGen.SetSkinOverride(self as Actor, SkinTemplate_G0000010000)
EndFunction

; ============================================================================================
; BODY MORPHS DE BODYSLIDE (los que antes entregaba el par BodyGen morphs.ini/templates.ini).
;
; POR QUE SE MUDARON ACA: BodyGen se evalua UNA sola vez y con el gate `!morphMap`
; (f4ee/ActorUpdateManager.cpp:49-54, :95-100, :121-126). Una referencia que YA existe en la partida
; del jugador no lo recibe NUNCA. El apply-script con el sufijo _G<n> si le llega, porque una property
; con nombre nuevo se inicializa del VMAD en vez de restaurarse rancia del savegame.
;
; ⛔ EL .ini NO SE EMITE MAS PARA ESTE PLUGIN, Y SI HABIA UNO SE BORRA. f4ee combina las keywords de un
; mismo morph con MAX (UserValues::GetEffectiveValue, BodyMorphInterface.cpp:1001-1009), asi que con un
; row de BodyGen vivo ganaria el valor mas grande de los dos en vez del que autoro el usuario.
; (En SSE el mismo choque SUMA en vez de maxear — motores distintos, misma conclusion.)
;
; ⭐ EL KEYWORD ES `None`, Y ESO ES CORRECTO, NO UN ATAJO. UserValues indexa por
; `keyword ? keyword->formID : 0` (BodyMorphInterface.cpp:970, :980, :995), y BodyGen mismo escribe con
; nullptr. O sea que None ES el slot canonico. Emitir un KYWD propio habria sido la alternativa para
; poder distinguir "lo nuestro" de "lo de BodyGen", pero exige un record nuevo en el ESP (el ESP no
; lleva master de LooksMenu) y no compra nada: si el .ini no se emite, ese slot es SOLO nuestro.
;
; ⛔ LO QUE NO SE USA, Y POR QUE:
;   ClearAll             -> es Revert(): borra los morphs de TODOS LOS ACTORES DEL JUEGO
;                           (PapyrusBodyGen.cpp:67-70). Nombre peligrosamente parecido a RemoveAllMorphs.
;   RegenerateMorphs     -> vuelve a correr BodyGen DESDE LOS .ini (PapyrusBodyGen.cpp:84-85). Es
;                           exactamente lo contrario de lo que queremos.
;
; ⚠️ CON LA PODA TOTAL EL ACTOR QUEDA SIN ENTRADA en el mapa, asi que `GetMorphMap` da null y BodyGen SI
; podria volver a evaluarlo en la carga siguiente. En la practica no pasa: en el mismo OnLoad volvemos a
; escribir nuestros morphs, con lo cual el mapa deja de estar vacio antes de que haya otra carga. El unico
; caso donde queda vacio de verdad es el de LIMPIEZA (payload sin morphs) — y ahi que BodyGen aplique lo que
; diga el .ini es exactamente lo correcto, porque el .ini se re-emite desde el mismo sidecar.
; ============================================================================================
Function ApplyBodyMorphs()
    ; ⛔ NO SOMOS EL DUEÑO: los entrega el .ini. Salir ANTES del barrido, no despues. Ver MorphsOwned.
    if !MorphsOwned_G0000010000
        if Verbose_G0000010000
            Debug.Trace("[NPCM] BM SKIP barrido: los morphs los entrega el BodyGen .ini (MorphsOwned=false)")
        endif
        return
    endif

    Actor a = self as Actor

    ; ⭐⭐ SONDA DE ORDEN. f4ee corre BodyGen en TESObjectLoadedEvent / TESInitScriptEvent y nosotros en
    ; OnLoad; el orden entre los dos NO esta garantizado. Estas lineas lo MIDEN:
    ;   * morphs previos = 0     -> corrimos primero (o nadie aplico nada)
    ;   * previos > 0 y el valor del slot None != 0 -> alguien escribio ANTES en NUESTRO slot: o quedo un
    ;     .ini instalado de una version anterior, o BodyGen corrio primero
    ;   * kw > 1                 -> otro mod tiene morphs keyword-scoped en este actor (los respetamos)
    ; GetMorphs/GetKeywords NUNCA devuelven None: arman un VMArray sobre un vector LOCAL
    ; (PapyrusBodyGen.cpp:53-65).
    ; ⭐ BLOQUE DE SONDA COMPLETO bajo Verbose: GetMorphs / GetMorph / GetKeywords existen SOLO para
    ; trazar, y en FO4 cada nativa hace ceder la VM (BodyGen no tiene NoWait). Es el ahorro que importa.
    if Verbose_G0000010000
        string[] pre = BodyGen.GetMorphs(a, IsFemale_G0000010000)
        Debug.Trace("[NPCM] BM morphs previos=" + pre.Length)
        if pre.Length > 0
            string pname = pre[0]
            Debug.Trace("[NPCM] BM morph previo[0]=" + pname)
            float pval = BodyGen.GetMorph(a, IsFemale_G0000010000, pname, None)
            Debug.Trace("[NPCM] BM morph previo[0] slot None = " + pval)
            Keyword[] pkw = BodyGen.GetKeywords(a, IsFemale_G0000010000, pname)
            Debug.Trace("[NPCM] BM morph previo[0] keywords=" + pkw.Length)
        endif
    endif

    ; ⭐⭐⭐ PODA TOTAL DEL ACTOR (para ESTE genero) — RemoveAllMorphs -> BodyMorphInterface::ClearMorphs
    ; (BodyMorphInterface.cpp:927-937) hace
    ;     m_morphMap[isFemale ? 1 : 0].erase(actor->formID)
    ;
    ; ⚠️⚠️ NO ES LO MISMO QUE EN SSE, Y HAY QUE SABERLO: aca el mapa es POR GENERO, asi que el clear solo
    ; alcanza al genero que le pasamos. En SSE el store no tiene esa dimension (la clave es solo el formID)
    ; y el clear se lleva todo. Consecuencia practica: si IsFemale_G<n> (bit 0 de ACBS) no coincidiera con
    ; el GetSex() que usa f4ee para guardar, los morphs quedarian en el OTRO mapa, invisibles para nosotros
    ; y sin barrer. Es el mismo IsFemale que ya usan los overlays, asi que la exposicion no es nueva.
    ;
    ; POR QUE PODA TOTAL Y NO POR KEYWORD. Antes se barria RemoveMorphsByKeyword(None). Funcionaba, pero
    ; ese camino borra la KEYWORD y DEJA el NOMBRE del morph con el mapa vacio (MorphValueMap::RemoveMorphs-
    ; ByKeyword, :1066-1073, no poda), asi que los nombres se acumulan en el co-save para siempre cada vez
    ; que el usuario cambia a un preset con otros sliders.
    ;
    ; ⚠️ SE LLEVA LOS MORPHS QUE OTRO MOD LE HAYA PUESTO A ESTE ACTOR, bajo cualquier keyword. Misma
    ; decision de producto que Overlays.RemoveAll: el NPC muestra EXACTAMENTE lo que muestra la app.
    ;
    ; ⛔⛔ NO CONFUNDIR CON BodyGen.ClearAll(): ese es Revert() y borra los morphs de TODOS LOS ACTORES DEL
    ; JUEGO (PapyrusBodyGen.cpp:67-70). El nombre es peligrosamente parecido. Aca va RemoveAllMorphs, que
    ; es por-actor.
    ;
    ; Incondicional: con payload vacio tambien, que es el caso de limpieza (cuerpo base).
    BodyGen.RemoveAllMorphs(a, IsFemale_G0000010000)

    ; ⭐ SONDA DE CONTROL POST-PODA, gemela de la de SSE. Gateada por Verbose porque GetMorphs es una
    ; nativa que existe SOLO para mirar (y en FO4 cada nativa hace ceder la VM).
    ; TIENE QUE DAR 0: ClearMorphs borra la ENTRADA DEL ACTOR entera (BodyMorphInterface.cpp:927-937), no
    ; como el viejo RemoveMorphsByKeyword, que borraba la keyword y dejaba el NOMBRE con el mapa vacio.
    ; Y GetMorphs NO filtra nombres vacios (:885-899), asi que si quedara alguno lo veriamos.
    ; ⚠️ ATENCION AL LEERLO: el mapa de FO4 es POR GENERO, asi que este 0 sólo dice que quedo limpio el
    ; genero que le pasamos (IsFemale). En SSE el 0 es absoluto porque alla el store no tiene esa dimension.
    if Verbose_G0000010000
        string[] post = BodyGen.GetMorphs(a, IsFemale_G0000010000)
        Debug.Trace("[NPCM] BM RemoveAllMorphs (poda total del actor) hecho")
        Debug.Trace("[NPCM] BM morphs tras barrido=" + post.Length)
        ; Segundo nivel, gemelo del de SSE: SOLO dispara si la poda fallo, y entonces dice QUE sobrevivio.
        ; Con la poda funcionando este bloque no corre nunca, asi que no cuesta nada.
        if post.Length > 0
            string qname = post[0]
            Keyword[] qkw = BodyGen.GetKeywords(a, IsFemale_G0000010000, qname)
            Debug.Trace("[NPCM] BM tras barrido " + qname + " keywords=" + qkw.Length)
        endif
    endif

    int n = MorphName_G0000010000.Length
    int applied = 0
    int i = 0
    while i < n
        string mname = MorphName_G0000010000[i]
        if mname != ""
            ; Guarda INLINE por .Length, jamas contra None y jamas pasando el array a un helper.
            if i < MorphValue_G0000010000.Length
                float mval = MorphValue_G0000010000[i]
                BodyGen.SetMorph(a, IsFemale_G0000010000, mname, None, mval)
                applied += 1
            endif
        endif
        i += 1
    endwhile

    if Verbose_G0000010000
        Debug.Trace("[NPCM] BM aplicados=" + applied + " de " + n)
        if n > 0
            string m0 = MorphName_G0000010000[0]
            if m0 != ""
                ; Read-back: si no devuelve lo que acabamos de escribir, la nativa no tomo el valor (o f4ee
                ; no esta cargado) y el problema esta ahi, no en el emisor.
                float back = BodyGen.GetMorph(a, IsFemale_G0000010000, m0, None)
                Debug.Trace("[NPCM] BM readback " + m0 + " = " + back)
            endif
        endif
    endif

    ; Repintado. INCONDICIONAL, tambien con payload vacio: UpdateMorphs detacha los slots morphables y
    ; hace Update3DModel, o sea que RECOMPONE desde el mapa. Con el mapa ya barrido, eso es exactamente
    ; "volver al cuerpo base" — que es lo unico que hay que hacer en el caso de limpieza.
    BodyGen.UpdateMorphs(a)
EndFunction
