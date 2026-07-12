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

;-- NiOverride override keys (skee64 OverrideVariant.h:31-59) -----------------------------------
int Property KEY_TINT    = 7 AutoReadOnly   ; kParam_ShaderTintColor — packed 0xAARRGGBB
int Property KEY_ALPHA   = 8 AutoReadOnly   ; kParam_ShaderAlpha
int Property KEY_TEXTURE = 9 AutoReadOnly   ; kParam_ShaderTexture — index 0 = diffuse, 1 = normal
int Property IDX_DIFFUSE = 0 AutoReadOnly
int Property IDX_NORMAL  = 1 AutoReadOnly

bool Property IsFemale = false Auto
{Género para el que se autoraron los overrides. NiOverride guarda los sets male/female por separado.}

int Property SchemaVersion = 1 Auto
{Hash del payload de ESTE NPC. Cambia sólo si cambian sus valores ⇒ sólo ESE actor re-aplica.}

;-- overlays: Body/Hands/Feet. JAMÁS Face (la cara es del bake) ---------------------------------
string[] Property OvlNode Auto
string[] Property OvlDiffuse Auto
string[] Property OvlNormal Auto
bool[]   Property OvlHasTint Auto
int[]    Property OvlTint Auto
bool[]   Property OvlHasAlpha Auto
float[]  Property OvlAlpha Auto

;-- skin overrides (por slot biped) -------------------------------------------------------------
int[]    Property SkinSlot Auto
string[] Property SkinDiffuse Auto
string[] Property SkinNormal Auto
bool[]   Property SkinHasTint Auto
int[]    Property SkinTint Auto

;-- node transforms -----------------------------------------------------------------------------
string[] Property NodeName Auto
bool[]   Property NodeHasScale Auto
float[]  Property NodeScale Auto
bool[]   Property NodeHasPos Auto
float[]  Property NodePosX Auto
float[]  Property NodePosY Auto
float[]  Property NodePosZ Auto
bool[]   Property NodeHasRot Auto
float[]  Property NodeRotM0 Auto
float[]  Property NodeRotM1 Auto
float[]  Property NodeRotM2 Auto
float[]  Property NodeRotM3 Auto
float[]  Property NodeRotM4 Auto
float[]  Property NodeRotM5 Auto
float[]  Property NodeRotM6 Auto
float[]  Property NodeRotM7 Auto
float[]  Property NodeRotM8 Auto
{La matriz 3x3 row-major, repartida en NUEVE arrays — NodeRotM<k>[i] = elemento k del nodo i. Uno por
 elemento, NO un array plano de 9xN: los arrays de Papyrus topan en 128 ELEMENTOS y un plano se
 pasaría a los 15 nodos. Así el techo son 128 NODOS, igual que el resto.

 NO es euler. AddNodeTransformRotation acepta 3 (euler en grados) O 9 (matriz cruda), y con 9 los
 copia directo a NiMatrix33::arr[i] (PapyrusNiOverride.cpp:1190-1193) — el mismo arr[i] que skee
 empaqueta después bajo la key 32 índice i, que es exactamente lo que guarda un .jslot. O sea que le
 devolvemos SU PROPIA secuencia de floats y no hay ninguna convención euler de por medio.}
int[]    Property NodeScaleMode Auto
{-1 = no tocar. 0 mult / 1 avg / 2 add / 3 max (NiTransformInterface.cpp:682-707).}

;-- estado por instancia (persiste en el savegame, como el TeleportActorScript vanilla) ----------
int appliedVersion = -1

Event OnLoad()
    if appliedVersion == SchemaVersion
        return                    ; ya aplicado a ESTE actor, y nada cambió desde entonces
    endif
    appliedVersion = SchemaVersion

    RemovePrevious()
    ApplyOverlays()
    ApplySkin()
    ApplyNodeTransforms()
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
; ⚠️ RESIDUAL: los node transforms se limpian sólo sobre los nodos del payload ACTUAL. Si un nodo se
; SACA por completo del preset, su transform queda hasta que el actor se resetee, porque el script ya
; no lo nombra. Lo correcto sería enumerarlos con NiOverride.GetNodeTransformNames(), PERO devuelve
; None cuando el actor no tiene ninguno y asignar None a un String[] TIRA (regla 2 de arriba) — fue
; el primer error del log. La salida limpia es que el emisor mande también los nodos del payload
; ANTERIOR (los tiene: lee el VMAD viejo antes de reescribirlo). PENDIENTE.
; ============================================================================================
Function RemovePrevious()
    ; --- overlays: los nodos son enumerables. NUNCA "Face": la cara es del bake.
    ClearOverlayGroup("Body [Ovl", NiOverride.GetNumBodyOverlays())
    ClearOverlayGroup("Hands [Ovl", NiOverride.GetNumHandOverlays())
    ClearOverlayGroup("Feet [Ovl", NiOverride.GetNumFeetOverlays())
    NiOverride.ApplyNodeOverrides(self)

    ; --- skin: los 32 slots biped. mask arranca en 1 y se duplica; en el bit 31 "desborda" a
    ; negativo, que es EXACTAMENTE el patrón de bits del último slot (Papyrus no chequea overflow:
    ; envuelve, a diferencia de VB).
    int mask = 1
    int b = 0
    while b < 32
        NiOverride.RemoveSkinOverride(self, IsFemale, false, mask, KEY_TEXTURE, IDX_DIFFUSE)
        NiOverride.RemoveSkinOverride(self, IsFemale, false, mask, KEY_TEXTURE, IDX_NORMAL)
        NiOverride.RemoveSkinOverride(self, IsFemale, false, mask, KEY_TINT, -1)
        mask = mask * 2
        b += 1
    endwhile
    NiOverride.ApplySkinOverrides(self)

    ; --- node transforms de los nodos que vamos a (re)escribir: sacamos SÓLO nuestra key
    string ovrKey = XformKey()
    int i = 0
    while i < NodeName.Length
        string node = NodeName[i]
        if node != ""
            NiOverride.RemoveNodeTransformScale(self, false, IsFemale, node, ovrKey)
            NiOverride.RemoveNodeTransformPosition(self, false, IsFemale, node, ovrKey)
            NiOverride.RemoveNodeTransformRotation(self, false, IsFemale, node, ovrKey)
            NiOverride.RemoveNodeTransformScaleMode(self, false, IsFemale, node, ovrKey)
        endif
        i += 1
    endwhile
EndFunction

; Borra NUESTRAS keys de los n nodos de un grupo de overlay. Recibe un String y un Int — NO un array.
Function ClearOverlayGroup(string prefix, int n)
    int i = 0
    while i < n
        string node = prefix + i + "]"
        NiOverride.RemoveNodeOverride(self, IsFemale, node, KEY_TEXTURE, IDX_DIFFUSE)
        NiOverride.RemoveNodeOverride(self, IsFemale, node, KEY_TEXTURE, IDX_NORMAL)
        NiOverride.RemoveNodeOverride(self, IsFemale, node, KEY_TINT, -1)
        NiOverride.RemoveNodeOverride(self, IsFemale, node, KEY_ALPHA, -1)
        i += 1
    endwhile
EndFunction

Function ApplyOverlays()
    int n = OvlNode.Length
    if n == 0
        return
    endif

    ; OBLIGATORIO Y PRIMERO. Con el default [Overlays] bPlayerOnly=true, OverlayInterface::OnAttach
    ; sólo construye los nodos de overlay para un actor que HasOverlays() — y AddOverlays() es lo que
    ; registra al actor en ese set (OverlayInterface.cpp:854 mete el formID; el gate de :1011 pasa).
    ; Sin esto los overrides caen sobre nodos que nunca se crearon: sin error, invisible.
    NiOverride.AddOverlays(self)

    int i = 0
    while i < n
        string node = OvlNode[i]
        if node != ""
            ; persist = TRUE siempre. Con persist=false skee lo aplica visualmente pero NO lo mete en
            ; el store ⇒ no se serializa al co-save y desaparece en la próxima carga
            ; (PapyrusNiOverride.cpp:503-514).
            if i < OvlDiffuse.Length
                if OvlDiffuse[i] != ""
                    NiOverride.AddNodeOverrideString(self, IsFemale, node, KEY_TEXTURE, IDX_DIFFUSE, OvlDiffuse[i], true)
                endif
            endif
            if i < OvlNormal.Length
                if OvlNormal[i] != ""
                    NiOverride.AddNodeOverrideString(self, IsFemale, node, KEY_TEXTURE, IDX_NORMAL, OvlNormal[i], true)
                endif
            endif
            if i < OvlHasTint.Length
                if OvlHasTint[i]
                    if i < OvlTint.Length
                        NiOverride.AddNodeOverrideInt(self, IsFemale, node, KEY_TINT, -1, OvlTint[i], true)
                    endif
                endif
            endif
            if i < OvlHasAlpha.Length
                if OvlHasAlpha[i]
                    if i < OvlAlpha.Length
                        NiOverride.AddNodeOverrideFloat(self, IsFemale, node, KEY_ALPHA, -1, OvlAlpha[i], true)
                    endif
                endif
            endif
        endif
        i += 1
    endwhile

    NiOverride.ApplyNodeOverrides(self)
EndFunction

Function ApplySkin()
    int n = SkinSlot.Length
    if n == 0
        return
    endif

    int i = 0
    while i < n
        int slot = SkinSlot[i]
        if slot != 0
            ; firstPerson = false: los NPC no tienen esqueleto de primera persona.
            if i < SkinDiffuse.Length
                if SkinDiffuse[i] != ""
                    NiOverride.AddSkinOverrideString(self, IsFemale, false, slot, KEY_TEXTURE, IDX_DIFFUSE, SkinDiffuse[i], true)
                endif
            endif
            if i < SkinNormal.Length
                if SkinNormal[i] != ""
                    NiOverride.AddSkinOverrideString(self, IsFemale, false, slot, KEY_TEXTURE, IDX_NORMAL, SkinNormal[i], true)
                endif
            endif
            if i < SkinHasTint.Length
                if SkinHasTint[i]
                    if i < SkinTint.Length
                        NiOverride.AddSkinOverrideInt(self, IsFemale, false, slot, KEY_TINT, -1, SkinTint[i], true)
                    endif
                endif
            endif
        endif
        i += 1
    endwhile

    NiOverride.ApplySkinOverrides(self)
EndFunction

Function ApplyNodeTransforms()
    int n = NodeName.Length
    if n == 0
        return
    endif

    string ovrKey = XformKey()
    int i = 0
    while i < n
        string node = NodeName[i]
        if node != ""

            ; --- escala
            if i < NodeHasScale.Length
                if NodeHasScale[i]
                    if i < NodeScale.Length
                        NiOverride.AddNodeTransformScale(self, false, IsFemale, node, ovrKey, NodeScale[i])
                    endif
                endif
            endif

            ; --- posición
            if i < NodeHasPos.Length
                if NodeHasPos[i]
                    if i < NodePosX.Length
                        float[] pos = new float[3]
                        pos[0] = NodePosX[i]
                        pos[1] = NodePosY[i]
                        pos[2] = NodePosZ[i]
                        NiOverride.AddNodeTransformPosition(self, false, IsFemale, node, ovrKey, pos)
                    endif
                endif
            endif

            ; --- rotación: 9 floats de matriz CRUDA, no euler (ver el doc de NodeRotM0..8)
            if i < NodeHasRot.Length
                if NodeHasRot[i]
                    if i < NodeRotM0.Length
                        float[] rot = new float[9]
                        rot[0] = NodeRotM0[i]
                        rot[1] = NodeRotM1[i]
                        rot[2] = NodeRotM2[i]
                        rot[3] = NodeRotM3[i]
                        rot[4] = NodeRotM4[i]
                        rot[5] = NodeRotM5[i]
                        rot[6] = NodeRotM6[i]
                        rot[7] = NodeRotM7[i]
                        rot[8] = NodeRotM8[i]
                        NiOverride.AddNodeTransformRotation(self, false, IsFemale, node, ovrKey, rot)
                    endif
                endif
            endif

            ; --- scale mode
            if i < NodeScaleMode.Length
                if NodeScaleMode[i] >= 0
                    NiOverride.AddNodeTransformScaleMode(self, false, IsFemale, node, ovrKey, NodeScaleMode[i])
                endif
            endif

            NiOverride.UpdateNodeTransform(self, false, IsFemale, node)
        endif
        i += 1
    endwhile
EndFunction
