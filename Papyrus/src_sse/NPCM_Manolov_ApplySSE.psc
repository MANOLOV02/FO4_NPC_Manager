Scriptname NPCM_Manolov_ApplySSE extends Actor
{
  NPC Manager (Manolov) — applies the RaceMenu/NiOverride options that CANNOT live in an ESP record
  and CANNOT be baked into a mesh or texture, to an NPC, on its first spawn.

  Attached to the NPC_ base record via VMAD (see NpcVmadBuilder.vb). A script on an ActorBase is
  inherited by every reference of it, and because the Papyrus type `Actor` extends `ObjectReference`
  it receives per-instance events — that is why OnLoad() fires per spawned actor.

  WHAT THIS SCRIPT DOES *NOT* DO — on purpose, to avoid applying anything twice:
    * Body morphs        — BodyGen morphs.ini/templates.ini (SseBodyGenIniWriter).
    * ANYTHING on the FACE — face overlays, morphs, sculpt, tints: all BAKED into the FaceGen NIF and
                           textures. The emitter never sends a Face node. No exceptions.
}

;-- ⛔⛔ LAS DOS REGLAS DE PAPYRUS QUE ME COSTARON UNA TARDE ENTERA -----------------------------
;
; (1) NUNCA pasar un array como ARGUMENTO a una función propia.
;     Papyrus tira "Cannot cast from None to X[]" AL BINDEAR el argumento, así que un `if a == None`
;     dentro del helper NUNCA llega a correr. Lo aprendí con Count/At/AtBool, lo documenté, y lo
;     repetí igual con RotAt. En este archivo no hay helpers que reciban arrays. Que siga así.
;
; (2) NUNCA comparar un array contra None: `if arr == None` / `if arr != None`.
;     Es la trampa peor, porque PARECE el guard correcto y compila sin chistar. Papyrus emite un CAST
;     de None al tipo del array, y ese cast REVIENTA — tenga o no valor el array. MEDIDO: los guards
;     eran el 100% de los errores del Papyrus.0.log. Vanilla nunca lo hace: chequea `.Length`
;     (p.ej. `if (WaterStages.Length == 0)`).
;
; ⇒ Acá los arrays se chequean SIEMPRE por `.Length`, jamás contra None. Eso sólo es seguro porque el
;   emisor GARANTIZA que toda array-property existe y trae al menos 1 elemento: cuando no hay datos
;   manda un CENTINELA ("" o 0) que los loops de abajo saltean solos. Esa garantía vive en
;   NpcApplyScriptEmitter.AddArray y este script DEPENDE de ella.
;   (Un array vacío es ilegal en Papyrus, y una propiedad ausente queda en None: el centinela es la
;   única salida que no cae en ninguna de las dos.)

;-- ⭐⭐⭐ EL SUFIJO `_G<n>` Y EL NOMBRE POR PLUGIN — LO PONE LA APP, NO SE TOCA A MANO ------------
;
; LEY MEDIDA (Skyrim SE, 2026-07-28, sobre instancias demostrablemente congeladas):
;
;   Al cargar la partida, el motor reconcilia cada instancia de script guardada contra el .pex y el
;   VMAD actuales:
;     * la variable que el savegame TIENE     -> se RESTAURA del savegame  => valor RANCIO para siempre
;     * la variable que el savegame NO tiene  -> se INICIALIZA del VMAD    => valor FRESCO del plugin
;   Vale para escalares y para arrays: llegan con su longitud, sin None y sin reventar.
;
; Una property `Auto` se compila a una variable de script (`::X_var`), y las variables se serializan.
; Por eso el payload de una version quedaba pegado para siempre en toda referencia que ya existia en
; la partida del jugador: re-publicar el mod no la alcanzaba.
;
; ⇒ Cada version publicada emite el payload con NOMBRES NUEVOS. El sufijo es el numero de generacion.
;
; ⚠️ ESTE ARCHIVO ES UNA PLANTILLA. Se compila con `_G000001` y con el nombre `NPCM_Manolov_ApplySSE`,
;   y NINGUNO de los dos es lo que llega al juego: al guardar el ESP, la app reescribe DENTRO del .pex
;   (PexPatcher.vb, a nivel bytes) tanto el nombre del script como la generacion:
;
;       plantilla:  NPCM_Manolov_ApplySSE                 _G000001
;       instalado:  NPCM_Manolov_<Plugin_esp>_ApplySSE    _G000007
;
;   ⇒ NO subir el sufijo a mano y NO renombrar el Scriptname. Si se cambia alguno hay que actualizar
;     `BaselineScriptSse` / `BaselineGeneration` en NpcApplyScriptEmitter.vb, o el parcheo falla.
;
; DE DONDE SALE EL NUMERO: del sidecar `<plugin>.bssliders`, campo `payloadGeneration`. Sube +1 en cada
; Save ESP y vuelve a 0 despues de 999999 (ancho fijo de 6 digitos para que el reemplazo en el .pex sea
; byte a byte). El wrap es seguro: el motor DESCARTA la variable que el script ya no declara — medido,
; "Variable ::OvlNode_var ... not found within the actual object. This variable will be skipped".
; Se puede forzar a mano con "Override version" en el dialogo de Save ESP.
;
; POR QUE EL NOMBRE LLEVA EL PLUGIN: los .pex sueltos de Data\Scripts no se fusionan, gana uno. Con un
; nombre unico, dos mods hechos con la app conviven. El stem va ANTES de "ApplySSE" para que el nombre
; legado no sea prefijo de ninguno nuevo, y asi el borrado por prefijo del VMAD no toque lo de otro autor.
;
; ⚠️ `appliedVersion` NO lleva sufijo A PROPOSITO: es el lado que TIENE que persistir. Asi
;   `appliedVersion` (viejo, del save) != `SchemaVersion_G<n>` (fresco, del plugin) ⇒ el actor aplica UNA
;   vez y despues saltea. Si el payload de ese NPC no cambio entre releases su hash es el mismo, asi que
;   ni siquiera re-aplica.
;
; Los `KEY_*`/`IDX_*` tampoco lo llevan: son `AutoReadOnly`, o sea SIN variable de respaldo (verificado
; en el binario del .pex), asi que su valor sale del .pex y ya es fresco por definicion.

;-- NiOverride override keys (skee64 OverrideVariant.h:31-59) -----------------------------------
int Property KEY_TINT    = 7 AutoReadOnly   ; kParam_ShaderTintColor — packed 0xAARRGGBB
int Property KEY_ALPHA   = 8 AutoReadOnly   ; kParam_ShaderAlpha
int Property KEY_TEXTURE = 9 AutoReadOnly   ; kParam_ShaderTexture — index 0 = diffuse, 1 = normal
int Property IDX_DIFFUSE = 0 AutoReadOnly
int Property IDX_NORMAL  = 1 AutoReadOnly

bool Property IsFemale_G000001 = false Auto
{Género para el que se autoraron los overrides. NiOverride guarda los sets male/female por separado.}

int Property SchemaVersion_G000001 = 1 Auto
{Hash del payload de ESTE NPC. Cambia sólo si cambian sus valores ⇒ sólo ESE actor re-aplica.}

;-- overlays: Body/Hands/Feet. JAMÁS Face (la cara es del bake) ---------------------------------
string[] Property OvlNode_G000001 Auto
string[] Property OvlDiffuse_G000001 Auto
string[] Property OvlNormal_G000001 Auto
bool[]   Property OvlHasTint_G000001 Auto
int[]    Property OvlTint_G000001 Auto
bool[]   Property OvlHasAlpha_G000001 Auto
float[]  Property OvlAlpha_G000001 Auto

;-- skin overrides (por slot biped) -------------------------------------------------------------
int[]    Property SkinSlot_G000001 Auto
string[] Property SkinDiffuse_G000001 Auto
string[] Property SkinNormal_G000001 Auto
bool[]   Property SkinHasTint_G000001 Auto
int[]    Property SkinTint_G000001 Auto

;-- node transforms -----------------------------------------------------------------------------
string[] Property NodeName_G000001 Auto
bool[]   Property NodeHasScale_G000001 Auto
float[]  Property NodeScale_G000001 Auto
bool[]   Property NodeHasPos_G000001 Auto
float[]  Property NodePosX_G000001 Auto
float[]  Property NodePosY_G000001 Auto
float[]  Property NodePosZ_G000001 Auto
bool[]   Property NodeHasRot_G000001 Auto
float[]  Property NodeRotM0_G000001 Auto
float[]  Property NodeRotM1_G000001 Auto
float[]  Property NodeRotM2_G000001 Auto
float[]  Property NodeRotM3_G000001 Auto
float[]  Property NodeRotM4_G000001 Auto
float[]  Property NodeRotM5_G000001 Auto
float[]  Property NodeRotM6_G000001 Auto
float[]  Property NodeRotM7_G000001 Auto
float[]  Property NodeRotM8_G000001 Auto
{La matriz 3x3 row-major, repartida en NUEVE arrays — NodeRotM<k>[i] = elemento k del nodo i. Uno por
 elemento, NO un array plano de 9xN: los arrays de Papyrus topan en 128 ELEMENTOS y un plano se
 pasaría a los 15 nodos. Así el techo son 128 NODOS, igual que el resto.

 NO es euler. AddNodeTransformRotation acepta 3 (euler en grados) O 9 (matriz cruda), y con 9 los
 copia directo a NiMatrix33::arr[i] (PapyrusNiOverride.cpp:1190-1193) — el mismo arr[i] que skee
 empaqueta después bajo la key 32 índice i, que es exactamente lo que guarda un .jslot. O sea que le
 devolvemos SU PROPIA secuencia de floats y no hay ninguna convención euler de por medio.}
int[]    Property NodeScaleMode_G000001 Auto
{-1 = no tocar. 0 mult / 1 avg / 2 add / 3 max (NiTransformInterface.cpp:682-707).}

;-- estado por instancia (persiste en el savegame, como el TeleportActorScript vanilla) ----------
int appliedVersion = -1

Event OnLoad()
    ; TRAZA: el script no logueaba NADA, y sin eso no se puede distinguir tres cosas que dan el MISMO
    ; sintoma ("el NPC no cambia"): (a) OnLoad no se dispara en esa referencia, (b) se dispara y se saltea
    ; por el sello, (c) se dispara pero con las propiedades CONGELADAS del savegame (o sea el payload viejo).
    ; MEDIDO: una copia por placeatme toma los cambios y la referencia que ya existia no cambia nada -- las
    ; tres explicaciones encajan con eso, asi que hay que verlas. Si aparece esta linea, (a) queda descartada;
    ; el valor de SchemaVersion_G000001 dice si el record fresco llego o no.
    Debug.Trace("[NPCM] OnLoad ref=" + self.GetFormID() + " appliedVersion=" + appliedVersion + " SchemaVersion_G000001=" + SchemaVersion_G000001)

    ; TRAZA DEL PAYLOAD. Es la prueba end-to-end de la ley del sufijo: si estas lineas salen sobre una
    ; referencia que YA existia en la partida, el dato nuevo llego. Tocar los arrays aca es seguro
    ; porque el emisor GARANTIZA que toda array-property existe y trae >= 1 elemento (centinela), y esa
    ; garantia vale TAMBIEN para el spec de limpieza, que se arma con el builder normal y allowEmpty.
    ; Una lectura por traza: indexar el mismo array dos veces en una expresion imprime N veces el
    ; ULTIMO elemento (quirk del codegen de Papyrus).
    Debug.Trace("[NPCM] payload ovl=" + OvlNode_G000001.Length + " skin=" + SkinSlot_G000001.Length + " nodes=" + NodeName_G000001.Length)

    ; ⛔ INSTANCIA HUERFANA: no soy la version activa de este actor, no toco NADA.
    ;
    ; El nombre del script lleva el nombre del ESP. Si el autor RENOMBRA su plugin publicado, el savegame
    ; del jugador se queda con la instancia del nombre anterior pegada al actor. Esa instancia ya no
    ; aparece en el VMAD, asi que no recibe ninguna property y sus arrays llegan con LONGITUD 0 (medido
    ; 2026-07-28: "payload ovl=0 skin=0 nodes=0"). Sin este guard correria igual, su sello no coincidiria,
    ; haria DONE y su RemovePrevious() barreria TODO -- borrandole al script activo lo que acaba de
    ; aplicar. Se vieron los dos OnLoad sobre la MISMA referencia en el mismo instante.
    ;
    ; Length == 0 es una senal SIN AMBIGUEDAD: el emisor garantiza que toda array-property llega con al
    ; menos 1 elemento (el centinela), tambien en el spec de LIMPIEZA. Un 0 solo puede venir de que el
    ; VMAD no nombra a este script.
    ;
    ; ⛔ Y NO SE BORRA EL .pex HUERFANO PARA "ARREGLARLO". Medido 2026-07-28: al borrarlo, el tipo deja de
    ; resolver y —como el script extends Actor— ese actor queda SIN TABLA DE METODOS PARA TODOS LOS DEMAS
    ; SCRIPTS. RaceMenuHHScaleEffect fallo 17 veces sobre FF000911 con "Method GetLeveledActorBase not
    ; found on NPCM_Manolov_ApplySSE" y "Cannot call GetSex() on a None object". El .pex viejo se queda
    ; donde esta; este guard lo vuelve inofensivo.
    if OvlNode_G000001.Length == 0
        Debug.Trace("[NPCM] INERTE: sin payload del VMAD (instancia de un nombre de script viejo)")
        return
    endif
    if OvlNode_G000001.Length > 0
        Debug.Trace("[NPCM] payload OvlNode_G000001[0]=" + OvlNode_G000001[0])
    endif
    if OvlDiffuse_G000001.Length > 0
        Debug.Trace("[NPCM] payload OvlDiffuse_G000001[0]=" + OvlDiffuse_G000001[0])
    endif

    if appliedVersion == SchemaVersion_G000001
        Debug.Trace("[NPCM] SKIP: sello igual, no se limpia ni se aplica nada")
        return                    ; ya aplicado a ESTE actor, y nada cambió desde entonces
    endif
    appliedVersion = SchemaVersion_G000001

    ; ⭐⭐ EL REGISTRO EN SKEE VA PRIMERO, ANTES DE BORRAR. Con el default [Overlays] bPlayerOnly=1
    ; (verificado en skee64.ini) skee sólo construye nodos de overlay para un actor que HasOverlays(), y
    ; AddOverlays() es lo que mete al actor en ese set. Estaba DENTRO de ApplyOverlays(), o sea DESPUÉS de
    ; RemovePrevious() ⇒ los Remove* salían contra un actor todavía sin registrar y se perdían sin error:
    ; los overlays nuevos aparecían y los viejos no se iban nunca. Peor: si no quedaba nada que aplicar,
    ; ApplyOverlays() retorna en su primera línea (n == 0) y el registro no ocurría NUNCA, así que "borré
    ; todos los overlays" no borraba nada.
    ; Es idempotente y barato, así que va incondicional: el barrido tiene que poder correr aunque el payload
    ; nuevo esté vacío, que es justamente el caso de una limpieza.
    NiOverride.AddOverlays(self)

    RemovePrevious()
    ApplyOverlays()
    ApplySkin()
    ApplyNodeTransforms()
    Debug.Trace("[NPCM] DONE ref=" + self.GetFormID())
EndEvent

; La "name" del transform es la KEY del override: namespacea NUESTRA capa, así RaceMenu, XPMSE y
; nosotros podemos tener un valor sobre el MISMO hueso sin pisarnos — y RemoveNodeTransform* saca
; SÓLO la nuestra. (No puede ser una variable llamada `key`: `Key` es un tipo real de Skyrim — `Key
; extends MiscObject` — y Papyrus rechaza una variable con nombre de tipo conocido.)
string Function XformKey()
    return "NPCM_Manolov"
EndFunction

; ============================================================================================
; DESHACER LO QUE APLICAMOS LA VEZ ANTERIOR.
;
; Todo entra con persist=true ⇒ va al store de skee, de ahí al co-save, y el motor lo re-aplica en
; cada carga. Si el usuario BORRA un overlay / skin / transform en la app y re-guarda, el set nuevo se
; aplica, pero el override VIEJO sigue ahí y nadie lo saca: queda pegado al actor para siempre.
;
; Se borra POR KEY EXACTA, nunca con RemoveAll* (que se llevarían puestas las capas de XPMSE o de
; cualquier otro mod). Overlays y skin se barren por ENUMERACIÓN — los nodos de overlay son un
; conjunto conocido y contable ("Body [Ovl{n}]"…, skee OverlayInterface.h:33,38,43) y los slots de
; skin son 32 — así que no hace falta recordar nada.
;
; Los node transforms se barren POR ENUMERACION con GetNodeTransformNames — ver el bloque de abajo.
; (Antes se barrian solo los nodos del payload actual, y un nodo sacado del preset quedaba pegado.)
; ============================================================================================
Function RemovePrevious()
    ; --- overlays: los nodos son enumerables.
    ClearOverlayGroup("Body [Ovl", NiOverride.GetNumBodyOverlays())
    ClearOverlayGroup("Hands [Ovl", NiOverride.GetNumHandOverlays())
    ClearOverlayGroup("Feet [Ovl", NiOverride.GetNumFeetOverlays())
    ; FACE: decia "NUNCA Face: la cara es del bake" y era cierto mientras el emisor jamas mandaba un
    ; nodo Face. Ya no: el bake de overlays de cara esta gateado por Setting_BakeSseRaceMenuOverlays y,
    ; con ese toggle APAGADO, el emisor SI manda los nodos Face (si no, no los aplicaba nadie y el
    ; overlay desaparecia). Sin este barrido, cambiar el toggle de OFF a ON dejaba el override viejo
    ; PEGADO al actor -- todo entra con persist=true, o sea al co-save -- y el overlay quedaba aplicado
    ; DOS VECES: el que sigue vivo en el co-save mas el que ahora esta horneado en la textura.
    ; Barrer es incondicional a proposito: es idempotente (RemoveNodeOverride de una key que no existe
    ; es no-op) y NO puede depender del toggle, porque el toggle de HOY no dice que se aplico AYER.
    ; Las dos familias de skee: FACE_NODE "Face [Ovl{}]" y FACE_NODE_SPELL "Face [SOvl{}]"
    ; (OverlayInterface.h:23-24), ambas contadas por GetNumFaceOverlays (main.cpp: g_numFaceOverlays).
    int nFace = NiOverride.GetNumFaceOverlays()
    ClearOverlayGroup("Face [Ovl", nFace)
    ClearOverlayGroup("Face [SOvl", nFace)
    NiOverride.ApplyNodeOverrides(self)

    ; --- skin: los 32 slots biped. mask arranca en 1 y se duplica; en el bit 31 "desborda" a
    ; negativo, que es EXACTAMENTE el patrón de bits del último slot (Papyrus no chequea overflow:
    ; envuelve, a diferencia de VB).
    int mask = 1
    int b = 0
    while b < 32
        NiOverride.RemoveSkinOverride(self, IsFemale_G000001, false, mask, KEY_TEXTURE, IDX_DIFFUSE)
        NiOverride.RemoveSkinOverride(self, IsFemale_G000001, false, mask, KEY_TEXTURE, IDX_NORMAL)
        NiOverride.RemoveSkinOverride(self, IsFemale_G000001, false, mask, KEY_TINT, -1)
        mask = mask * 2
        b += 1
    endwhile
    NiOverride.ApplySkinOverrides(self)

    ; --- node transforms: POR ENUMERACION, no por el payload actual.
    ;
    ; ⭐ ESTO ARREGLA UN RESIDUAL VIEJO, y de paso RETRACTA lo que decia el comentario anterior. Decia
    ; que GetNodeTransformNames "devuelve None cuando el actor no tiene ninguno" y que por eso habia que
    ; barrer solo los nodos del payload — dejando pegado el transform de un nodo que se sacaba del
    ; preset. LEIDO EN LA FUENTE (PapyrusNiOverride.cpp:1381-1394): la native construye un
    ; VMResultArray LOCAL y lo devuelve; NO hay ningun camino que devuelva null. Devuelve VACIO.
    ; El "Cannot cast from None to String[]" que se le atribuyo venia de una PROPERTY que llegaba None
    ; (regla 2 de la cabecera), no del retorno de esta native.
    ;
    ; ⚠️ SIN COLATERAL, a diferencia del barrido de overlays: se sacan SOLO las keys NUESTRAS de cada
    ; nodo, asi que XPMSE, RaceMenu o cualquier otro conservan las suyas. Quitar una key que no existe
    ; es no-op, asi que enumerar todos los nodos es seguro.
    ;
    ; ⭐ Y ACA SI HAY "DESHACER" DE VERDAD: Impl_UpdateNodeTransforms (NiTransformInterface.cpp:454-476)
    ; RECOMPONE el transform desde cero — arranca de la transform base del nodo e itera las keys que
    ; QUEDAN. Si no queda ninguna, el nodo vuelve a su base. Es lo contrario de los overlays, donde
    ; ApplyNodeOverrides solo empuja lo que quedo y nunca resetea.
    string ovrKey = XformKey()
    Debug.Trace("[NPCM] antes de GetNodeTransformNames")
    string[] xnodes = NiOverride.GetNodeTransformNames(self, false, IsFemale_G000001)
    Debug.Trace("[NPCM] xforms previos=" + xnodes.Length)
    int i = 0
    while i < xnodes.Length
        string node = xnodes[i]
        if node != ""
            NiOverride.RemoveNodeTransformScale(self, false, IsFemale_G000001, node, ovrKey)
            NiOverride.RemoveNodeTransformPosition(self, false, IsFemale_G000001, node, ovrKey)
            NiOverride.RemoveNodeTransformRotation(self, false, IsFemale_G000001, node, ovrKey)
            NiOverride.RemoveNodeTransformScaleMode(self, false, IsFemale_G000001, node, ovrKey)
            NiOverride.UpdateNodeTransform(self, false, IsFemale_G000001, node)
        endif
        i += 1
    endwhile
EndFunction

; ⛔⛔ BORRAR = APAGAR EL NODO. skee NO tiene "deshacer", y esto costó una tarde de "aplica lo nuevo
; pero no borra lo viejo". Los tres caminos, leídos en la fuente de skee64:
;
;   AddNodeOverride*     -> guarda en el store (si persist) Y **pinta el nodo en el acto**
;                           (Impl_SetNodeProperty, PapyrusNiOverride.cpp:273-306)
;   RemoveNodeOverride   -> **sólo borra la entrada del map. No toca el nodo.**
;                           (OverrideInterface.cpp:333-351)
;   ApplyNodeOverrides   -> sólo empuja lo que QUEDÓ en el store; nunca resetea un nodo cuyo override
;                           se borró (Impl_SetNodeProperties, OverrideInterface.cpp:777)
;
; Y las DOS puertas que parecían la salida están cerradas, verificado:
;   * RevertOverlays / RevertOverlay llegan a Papyrus con **resetDiffuse = FALSE**
;     (PapyrusNiOverride.cpp:103-115) — restauran el normal y DEJAN el tatuaje. RaceMenu los llama
;     internamente con `true`; a los scripts les expusieron la variante capada.
;   * OverlayInterface::GetDefaultTexture() existe en el C++ (OverlayInterface.h:212) pero **NO está
;     registrada como native**, así que no se le puede preguntar cuál es su textura por defecto.
;
; ⇒ Se apaga el nodo con KEY_ALPHA = 0. ES EL ÚNICO MECANISMO QUE NO DEPENDE DE CONFIGURACIÓN QUE NO
;   PODEMOS VER. Repintar el diffuse con el default de skee sería "más limpio" en teoría, pero ese path
;   sale de `skee64.ini [Overlays/Data] sDefaultTexture` (main.cpp:789-793) — el del JUGADOR, no el del
;   autor: si lo cambió, el borrado no limpiaría nada y no habría forma de saberlo. El alpha funciona en
;   toda instalación. Y no queda estado sucio: el store se limpia igual con los Remove* de abajo, así que
;   el próximo attach del 3D reconstruye el nodo desde cero.
;
; persist = FALSE: pinta el nodo pero NO deja entrada en el store (PapyrusNiOverride.cpp:503-514). Los
; Remove* siguen siendo necesarios: sacan lo que YA estaba guardado, que es lo que skee re-aplicaría en
; cada carga (todo entró con persist=true).
;
; ⚠️ ESTO OBLIGA a que ApplyOverlays aplique el alpha SIEMPRE — ver el comentario allá. No es opcional.
;
; ⚠️ RESIDUAL: el normal (IDX_NORMAL) no se toca. skee lo restaura copiándolo de la geometría de la piel
; y eso no se puede hacer desde Papyrus. Un overlay viejo con normal propio lo deja hasta el próximo
; rebuild del 3D — pero con alpha 0 el nodo no se ve, así que es inerte.
;
; ⚠️ BARRE TODOS LOS NODOS DE OVERLAY, sean nuestros o de otro mod. DECISIÓN DE PRODUCTO tomada a
; propósito: el NPC muestra exactamente lo que muestra la app. skee no guarda dueño (su store es
; actor+nodo+key+index), así que distinguir no es posible sin un ledger.
;
; Recibe un String y un Int — NO un array (regla 1 de la cabecera).
Function ClearOverlayGroup(string prefix, int n)
    int i = 0
    while i < n
        string node = prefix + i + "]"
        ; 1) apagar el nodo (visual, sin persistir)
        NiOverride.AddNodeOverrideFloat(self, IsFemale_G000001, node, KEY_ALPHA, -1, 0.0, false)
        NiOverride.AddNodeOverrideInt(self, IsFemale_G000001, node, KEY_TINT, -1, 0, false)
        ; 2) sacar del store lo que hubiera guardado
        NiOverride.RemoveNodeOverride(self, IsFemale_G000001, node, KEY_TEXTURE, IDX_DIFFUSE)
        NiOverride.RemoveNodeOverride(self, IsFemale_G000001, node, KEY_TEXTURE, IDX_NORMAL)
        NiOverride.RemoveNodeOverride(self, IsFemale_G000001, node, KEY_TINT, -1)
        NiOverride.RemoveNodeOverride(self, IsFemale_G000001, node, KEY_ALPHA, -1)
        i += 1
    endwhile
EndFunction


Function ApplyOverlays()
    int n = OvlNode_G000001.Length
    if n == 0
        return
    endif

    ; El registro en skee (NiOverride.AddOverlays) ya NO va acá: se MOVIÓ al principio de OnLoad().
    ; Motivo: es igual de obligatorio para BORRAR que para aplicar, y acá corría DESPUÉS de
    ; RemovePrevious() — con el default [Overlays] bPlayerOnly=true, OverlayInterface::OnAttach sólo
    ; construye los nodos de overlay para un actor que HasOverlays() (OverlayInterface.cpp:854 mete el
    ; formID; el gate de :1011 pasa), así que los Remove* caían sobre nodos que nunca se crearon: sin
    ; error, invisible. Y con n == 0 esta función retorna arriba, así que el registro no ocurría NUNCA
    ; justo en el caso de limpieza. Un solo dueño, y es OnLoad.

    int i = 0
    while i < n
        string node = OvlNode_G000001[i]
        if node != ""
            ; persist = TRUE siempre. Con persist=false skee lo aplica visualmente pero NO lo mete en
            ; el store ⇒ no se serializa al co-save y desaparece en la próxima carga
            ; (PapyrusNiOverride.cpp:503-514).
            if i < OvlDiffuse_G000001.Length
                if OvlDiffuse_G000001[i] != ""
                    NiOverride.AddNodeOverrideString(self, IsFemale_G000001, node, KEY_TEXTURE, IDX_DIFFUSE, OvlDiffuse_G000001[i], true)
                endif
            endif
            if i < OvlNormal_G000001.Length
                if OvlNormal_G000001[i] != ""
                    NiOverride.AddNodeOverrideString(self, IsFemale_G000001, node, KEY_TEXTURE, IDX_NORMAL, OvlNormal_G000001[i], true)
                endif
            endif
            if i < OvlHasTint_G000001.Length
                if OvlHasTint_G000001[i]
                    if i < OvlTint_G000001.Length
                        NiOverride.AddNodeOverrideInt(self, IsFemale_G000001, node, KEY_TINT, -1, OvlTint_G000001[i], true)
                    endif
                endif
            endif
            ; ⚠️ EL ALPHA SE APLICA SIEMPRE, no sólo cuando el overlay trae uno propio. NO es opcional: es el
            ; complemento obligatorio del barrido, que deja TODOS los nodos en alpha 0. Si acá se gateara por
            ; OvlHasAlpha_G000001, un nodo que recibe textura nueva sin alpha explícito se quedaría con ese 0 e
            ; INVISIBLE. El emisor ya manda 1.0 cuando el overlay no define alpha, así que el valor es válido
            ; siempre. (OvlHasAlpha_G000001 sigue declarada y emitida —la garantía 1:1 con el .psc— sin decidir nada.)
            if i < OvlAlpha_G000001.Length
                NiOverride.AddNodeOverrideFloat(self, IsFemale_G000001, node, KEY_ALPHA, -1, OvlAlpha_G000001[i], true)
            endif
        endif
        i += 1
    endwhile

    NiOverride.ApplyNodeOverrides(self)
EndFunction

Function ApplySkin()
    int n = SkinSlot_G000001.Length
    if n == 0
        return
    endif

    int i = 0
    while i < n
        int slot = SkinSlot_G000001[i]
        if slot != 0
            ; firstPerson = false: los NPC no tienen esqueleto de primera persona.
            if i < SkinDiffuse_G000001.Length
                if SkinDiffuse_G000001[i] != ""
                    NiOverride.AddSkinOverrideString(self, IsFemale_G000001, false, slot, KEY_TEXTURE, IDX_DIFFUSE, SkinDiffuse_G000001[i], true)
                endif
            endif
            if i < SkinNormal_G000001.Length
                if SkinNormal_G000001[i] != ""
                    NiOverride.AddSkinOverrideString(self, IsFemale_G000001, false, slot, KEY_TEXTURE, IDX_NORMAL, SkinNormal_G000001[i], true)
                endif
            endif
            if i < SkinHasTint_G000001.Length
                if SkinHasTint_G000001[i]
                    if i < SkinTint_G000001.Length
                        NiOverride.AddSkinOverrideInt(self, IsFemale_G000001, false, slot, KEY_TINT, -1, SkinTint_G000001[i], true)
                    endif
                endif
            endif
        endif
        i += 1
    endwhile

    NiOverride.ApplySkinOverrides(self)
EndFunction

Function ApplyNodeTransforms()
    int n = NodeName_G000001.Length
    if n == 0
        return
    endif

    string ovrKey = XformKey()
    int i = 0
    while i < n
        string node = NodeName_G000001[i]
        if node != ""

            ; --- escala
            if i < NodeHasScale_G000001.Length
                if NodeHasScale_G000001[i]
                    if i < NodeScale_G000001.Length
                        NiOverride.AddNodeTransformScale(self, false, IsFemale_G000001, node, ovrKey, NodeScale_G000001[i])
                    endif
                endif
            endif

            ; --- posición
            if i < NodeHasPos_G000001.Length
                if NodeHasPos_G000001[i]
                    if i < NodePosX_G000001.Length
                        float[] pos = new float[3]
                        pos[0] = NodePosX_G000001[i]
                        pos[1] = NodePosY_G000001[i]
                        pos[2] = NodePosZ_G000001[i]
                        NiOverride.AddNodeTransformPosition(self, false, IsFemale_G000001, node, ovrKey, pos)
                    endif
                endif
            endif

            ; --- rotación: 9 floats de matriz CRUDA, no euler (ver el doc de NodeRotM0_G000001..8)
            if i < NodeHasRot_G000001.Length
                if NodeHasRot_G000001[i]
                    if i < NodeRotM0_G000001.Length
                        float[] rot = new float[9]
                        rot[0] = NodeRotM0_G000001[i]
                        rot[1] = NodeRotM1_G000001[i]
                        rot[2] = NodeRotM2_G000001[i]
                        rot[3] = NodeRotM3_G000001[i]
                        rot[4] = NodeRotM4_G000001[i]
                        rot[5] = NodeRotM5_G000001[i]
                        rot[6] = NodeRotM6_G000001[i]
                        rot[7] = NodeRotM7_G000001[i]
                        rot[8] = NodeRotM8_G000001[i]
                        NiOverride.AddNodeTransformRotation(self, false, IsFemale_G000001, node, ovrKey, rot)
                    endif
                endif
            endif

            ; --- scale mode
            if i < NodeScaleMode_G000001.Length
                if NodeScaleMode_G000001[i] >= 0
                    NiOverride.AddNodeTransformScaleMode(self, false, IsFemale_G000001, node, ovrKey, NodeScaleMode_G000001[i])
                endif
            endif

            NiOverride.UpdateNodeTransform(self, false, IsFemale_G000001, node)
        endif
        i += 1
    endwhile
EndFunction
