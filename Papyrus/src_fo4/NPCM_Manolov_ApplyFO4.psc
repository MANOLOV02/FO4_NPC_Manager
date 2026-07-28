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

  NOT DONE HERE, on purpose (already delivered another way, would otherwise apply twice):
    * Body morphs — the BodyGen morphs.ini/templates.ini pair (BodyGenIniWriter) already does it.
    * Anything face — baked into the FaceGen NIF/textures.
}

bool Property IsFemale_G000001 = false Auto

int Property SchemaVersion_G000001 = 1 Auto
{Bumped by the app when the authored values change, so an updated plugin re-applies to actors that
 already spawned in an existing save.}

;-- ⭐⭐⭐ EL SUFIJO `_G<n>` Y EL NOMBRE POR PLUGIN — LO PONE LA APP, NO SE TOCA A MANO ------------
;
; Mismo esquema que SSE, por la misma ley MEDIDA (Skyrim SE 2026-07-28): al cargar la partida el motor
; restaura del savegame la variable que ya tenia (=> RANCIA para siempre) e inicializa desde el VMAD la
; que no tenia (=> FRESCA). Una property `Auto` se compila a variable de script, y las variables se
; serializan: por eso el payload de una version quedaba pegado en toda referencia ya existente.
;
; ⚠️ ESTE ARCHIVO ES UNA PLANTILLA. Se compila con `_G000001` y con el nombre `NPCM_Manolov_ApplyFO4`, y
;   ninguno de los dos llega al juego: al guardar el ESP la app reescribe DENTRO del .pex (PexPatcher.vb)
;   el nombre del script y la generacion. NO subir el sufijo ni renombrar el Scriptname a mano.
;
; ⚠️ EN FO4 ESTO NO ESTA MEDIDO TODAVIA. Se porta con la misma forma que SSE y con trazas para
;   confirmarlo en la primera corrida — los dos motores YA difirieron en el manejo de arrays.
;
; `appliedVersion` NO lleva sufijo a proposito: es el lado que tiene que persistir.

;-- overlays (parallel arrays, one entry per overlay) -------------------------------------------
string[] Property OvlTemplate_G000001 Auto
{f4ee overlay template id (the `template` member of Overlays:Entry) — from the installed
 overlays.json catalog, NOT a loose texture path.}
int[]   Property OvlPriority_G000001 Auto
float[] Property OvlRed_G000001 Auto
float[] Property OvlGreen_G000001 Auto
float[] Property OvlBlue_G000001 Auto
float[] Property OvlAlpha_G000001 Auto
float[] Property OvlOffsetU_G000001 Auto
float[] Property OvlOffsetV_G000001 Auto
float[] Property OvlScaleU_G000001 Auto
float[] Property OvlScaleV_G000001 Auto

;-- skin override (single template id; "" = none) -----------------------------------------------
string Property SkinTemplate_G000001 = "" Auto

;-- per-instance state (persists in the savegame, like vanilla TeleportActorScript) -------------
int appliedVersion = -1

Event OnLoad()
    ; TRAZA: misma instrumentacion que el script de SSE, a proposito -- sin ella no se puede distinguir
    ; tres causas que dan el MISMO sintoma ("el NPC no cambia"): (a) OnLoad no se dispara en esa
    ; referencia, (b) se dispara y se saltea por el sello, (c) se dispara pero con las propiedades
    ; CONGELADAS del savegame, o sea leyendo el payload viejo.
    ; MEDIDO en SSE (2026-07-26, log de Papyrus + VMAD del ESP): es (c). Dos referencias reportaban un
    ; SchemaVersion_G000001 que YA NO EXISTE en el plugin, asi que solo podia venir del savegame. La via es la
    ; misma en los dos juegos (propiedades del VMAD), asi que aca se espera lo mismo -- pero se instrumenta
    ; igual en vez de asumirlo, que es como se destapo en SSE.
    ; No se tocan arrays en la traza: el spec de LIMPIEZA no emitia array alguna y un .Length sobre None
    ; tiraria justo en el caso que interesa observar.
    Debug.Trace("[NPCM] OnLoad ref=" + self.GetFormID() + " appliedVersion=" + appliedVersion + " SchemaVersion_G000001=" + SchemaVersion_G000001)

    ; ⚠️ INSTRUMENTADO A PROPOSITO. En SSE esta medido que una array-property que el VMAD NO trae llega
    ; con LONGITUD 0 (no None), y eso es lo que hace de senal para el guard de instancia huerfana. En FO4
    ; NO esta medido, y los dos motores YA difirieron en el manejo de arrays (FO4 tolera arrays vacios,
    ; Skyrim no). Si la linea "antes de tocar" sale y la siguiente NO, llego None y el .Length tiro: ahi
    ; el guard tiene que pasar a un escalar.
    Debug.Trace("[NPCM] antes de tocar OvlTemplate_G000001")
    Debug.Trace("[NPCM] payload ovl=" + OvlTemplate_G000001.Length + " skin='" + SkinTemplate_G000001 + "'")

    ; ⛔ INSTANCIA HUERFANA: no soy la version activa de este actor, no toco NADA.
    ; El nombre del script lleva el del ESP; si el autor renombra su plugin, el savegame se queda con la
    ; instancia del nombre anterior pegada al actor. Esa instancia ya no aparece en el VMAD, no recibe
    ; ninguna property, y sin este guard correria igual, su sello no coincidiria, y su barrido se llevaria
    ; puesto lo que el script activo acaba de aplicar. MEDIDO en SSE 2026-07-28 (dos OnLoad sobre la misma
    ; referencia en el mismo instante).
    ; ⛔ Y NO se borra el .pex huerfano para "arreglarlo": el script extends Actor, asi que si el tipo no
    ; resuelve ese actor queda SIN TABLA DE METODOS PARA TODOS LOS DEMAS SCRIPTS (medido en SSE: RaceMenu
    ; fallando 17 veces sobre un NPC nuestro). El .pex viejo se queda; este guard lo vuelve inofensivo.
    if OvlTemplate_G000001.Length == 0
        Debug.Trace("[NPCM] INERTE: sin payload del VMAD (instancia de un nombre de script viejo)")
        return
    endif

    if appliedVersion == SchemaVersion_G000001
        Debug.Trace("[NPCM] SKIP: sello igual, no se limpia ni se aplica nada")
        return                    ; already applied to THIS actor, and nothing changed since
    endif
    appliedVersion = SchemaVersion_G000001

    ApplyOverlays()
    ApplySkin()
    Debug.Trace("[NPCM] DONE ref=" + self.GetFormID())
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
    Overlays.RemoveAll(a, IsFemale_G000001)

    int n = OvlTemplate_G000001.Length
    if n == 0
        Overlays.Update(a)
        return
    endif

    int i = 0
    while i < n
        if OvlTemplate_G000001[i] != ""
            Overlays:Entry e = new Overlays:Entry
            e.template = OvlTemplate_G000001[i]
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

            if i < OvlPriority_G000001.Length
                    e.priority = OvlPriority_G000001[i]
            endif
            if i < OvlRed_G000001.Length
                    e.red = OvlRed_G000001[i]
            endif
            if i < OvlGreen_G000001.Length
                    e.green = OvlGreen_G000001[i]
            endif
            if i < OvlBlue_G000001.Length
                    e.blue = OvlBlue_G000001[i]
            endif
            if i < OvlAlpha_G000001.Length
                    e.alpha = OvlAlpha_G000001[i]
            endif
            if i < OvlOffsetU_G000001.Length
                    e.offset_u = OvlOffsetU_G000001[i]
            endif
            if i < OvlOffsetV_G000001.Length
                    e.offset_v = OvlOffsetV_G000001[i]
            endif
            if i < OvlScaleU_G000001.Length
                    e.scale_u = OvlScaleU_G000001[i]
            endif
            if i < OvlScaleV_G000001.Length
                    e.scale_v = OvlScaleV_G000001[i]
            endif

            Overlays.Add(a, IsFemale_G000001, e)
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
    if SkinTemplate_G000001 == ""
        BodyGen.RemoveSkinOverride(self as Actor)
        return
    endif
    ; SetSkinOverride calls UpdateSkinOverride internally (PapyrusBodyGen.cpp:109) — no refresh needed.
    BodyGen.SetSkinOverride(self as Actor, SkinTemplate_G000001)
EndFunction
