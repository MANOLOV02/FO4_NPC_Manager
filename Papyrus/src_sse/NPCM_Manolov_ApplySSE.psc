Scriptname NPCM_Manolov_ApplySSE extends Actor
{
  NPC Manager (Manolov) — applies the RaceMenu/NiOverride options that CANNOT live in an ESP record
  and CANNOT be baked into a mesh or texture, to an NPC, on its first spawn.

  Attached to the NPC_ base record via VMAD (see NpcVmadBuilder.vb). A script on an ActorBase is
  inherited by every reference of it, and because the Papyrus type `Actor` extends `ObjectReference`
  it receives per-instance events — that is why OnLoad() fires per spawned actor. Verified against
  vanilla: 805 of 5118 Skyrim.esm NPC_ ship base-attached scripts that work exactly this way
  (defaultGhostScript, WIDeadBodyCleanupScript, ...).

  WHAT THIS SCRIPT DOES *NOT* DO — on purpose, to avoid applying anything twice:
    * Body morphs (BodySlide sliders) — already delivered by the BodyGen morphs.ini/templates.ini
      pair that the app writes (SseBodyGenIniWriter). RaceMenu evaluates those itself on actor load.
    * FACE overlays / face paint — already BAKED into the per-NPC head diffuse by FaceGenBuilder
      (WriteSseFaceDiffuseWithOverlays). Re-applying them here would composite them a second time,
      as a live decal on top of a texture that already contains them.
      => The emitter must pass ONLY Body/Hands/Feet overlay nodes here. Face nodes are baked.
    * Face morphs / sculpt / tints — baked into the FaceGen NIF + textures.

  So this script covers exactly the three subsystems with no other delivery route:
    1. Body/Hands/Feet overlays (tattoos)   2. Skin overrides (per-slot body paint)   3. Node transforms

  All arrays are PARALLEL: index i of every Ovl* array describes overlay i, etc. The emitter is
  responsible for keeping their lengths equal; the script defends against ragged input anyway.
}

;-- NiOverride override keys (skee64 OverrideVariant.h:31-59) -----------------------------------
int Property KEY_TINT    = 7 AutoReadOnly   ; kParam_ShaderTintColor  — packed 0xAARRGGBB
int Property KEY_ALPHA   = 8 AutoReadOnly   ; kParam_ShaderAlpha
int Property KEY_TEXTURE = 9 AutoReadOnly   ; kParam_ShaderTexture — index 0 = diffuse, 1 = normal
int Property IDX_DIFFUSE = 0 AutoReadOnly
int Property IDX_NORMAL  = 1 AutoReadOnly

;-- identity ------------------------------------------------------------------------------------
bool Property IsFemale = false Auto
{Gender the overrides were authored for. NiOverride stores male/female sets separately.}

int Property SchemaVersion = 1 Auto
{Bumped by the app whenever the authored values change. The applied version is remembered per
 instance, so an updated plugin re-applies to actors that already spawned in an existing save.}

;-- 1. overlays: Body/Hands/Feet ONLY (never Face — see header) ---------------------------------
string[] Property OvlNode Auto
{Overlay node names, e.g. "Body [Ovl0]". NOT "Face [Ovl*]" — those are baked.}
string[] Property OvlDiffuse Auto
string[] Property OvlNormal Auto
bool[]   Property OvlHasTint Auto
int[]    Property OvlTint Auto
{Packed 0xAARRGGBB, only read where OvlHasTint[i].}
bool[]   Property OvlHasAlpha Auto
float[]  Property OvlAlpha Auto

;-- 2. skin overrides (per biped slot) ----------------------------------------------------------
int[]    Property SkinSlot Auto
{Slot MASK (not slot index) — e.g. 1 << (32 - 30) for slot 32/body.}
string[] Property SkinDiffuse Auto
string[] Property SkinNormal Auto
bool[]   Property SkinHasTint Auto
int[]    Property SkinTint Auto

;-- 3. node transforms --------------------------------------------------------------------------
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
{The 3x3 rotation matrix, row-major, split across NINE arrays — NodeRotM<k>[i] is element k of node i's
 matrix. One array per element, NOT one flat array of 9xN.

 ⛔ Why split: Papyrus arrays in Skyrim are capped at 128 ELEMENTS. A flat 9xN array would blow that at
 15 node transforms, and the editor happily lets the user pick far more bones than that. Splitting keeps
 every array exactly N long, so the real ceiling is 128 nodes — the same as every other array here.

 NOT euler. NiOverride.AddNodeTransformRotation accepts EITHER 3 euler angles in degrees OR 9 raw matrix
 floats, and with 9 it copies them straight into NiMatrix33::arr[i] (PapyrusNiOverride.cpp:1190-1193) —
 the same arr[i] skee later packs out under key 32 index i, which is exactly what a .jslot stores. So
 passing the 9 floats hands skee back its own values and needs NO euler convention. The 3-float path
 would depend on NiMatrix33::SetEulerAngles' heading/attitude/bank order matching ours; that assumption
 is unnecessary, so it is not made. The app fills these from RaceMenuJslot.RotationRowMajor — the same
 function that writes key 32 to the .jslot.}
int[]    Property NodeScaleMode Auto
{-1 = leave alone. 0 mult / 1 avg / 2 add / 3 max (NiTransformInterface.cpp:682-707).}

;-- per-instance state (persists in the savegame, like vanilla TeleportActorScript) -------------
int appliedVersion = -1

; LEDGER of what WE applied last time, so a re-apply can UNDO it first.
;
; ⛔ Without this, removing something in the app never removes it in the game. Every Add*Override goes in
; with persist=true, which puts it in skee's store and serializes it to the co-save; skee then re-applies it
; on every load, forever. If the user deletes an overlay / skin slot / node transform and re-saves, the hash
; changes and OnLoad re-applies — but the OLD override is still sitting in the store and nobody ever deletes
; it. The tattoo stays on the actor permanently. (FO4 has the same hazard and solves it with myUids.)
;
; We remove by EXACT key, never RemoveAllNodeNameOverrides / RemoveAllReferenceOverrides / RemoveAllTransforms
; — those would also wipe overrides XPMSE or another mod put on the same node. Node transforms are removed by
; our own key name (ovrKey), which namespaces our layer, so other mods' layers on the same bone survive.
string[] appliedOvlNodes
int[]    appliedSkinSlots
string[] appliedXformNodes

Event OnLoad()
    if appliedVersion == SchemaVersion
        return                    ; already applied to THIS actor, and nothing changed since
    endif
    appliedVersion = SchemaVersion
    ApplyAll()
EndEvent

Function ApplyAll()
    RemovePrevious()
    ApplyOverlays()
    ApplySkin()
    ApplyNodeTransforms()
EndFunction

;-- undo whatever WE applied last time (see the ledger note above) -------------------------------
Function RemovePrevious()
    string ovrKey = XformKey()

    if appliedOvlNodes != None
        int i = 0
        while i < appliedOvlNodes.Length
            string node = appliedOvlNodes[i]
            if node != ""
                NiOverride.RemoveNodeOverride(self, IsFemale, node, KEY_TEXTURE, IDX_DIFFUSE)
                NiOverride.RemoveNodeOverride(self, IsFemale, node, KEY_TEXTURE, IDX_NORMAL)
                NiOverride.RemoveNodeOverride(self, IsFemale, node, KEY_TINT, -1)
                NiOverride.RemoveNodeOverride(self, IsFemale, node, KEY_ALPHA, -1)
            endif
            i += 1
        endwhile
        NiOverride.ApplyNodeOverrides(self)
    endif

    if appliedSkinSlots != None
        int i = 0
        while i < appliedSkinSlots.Length
            int slot = appliedSkinSlots[i]
            if slot != 0
                NiOverride.RemoveSkinOverride(self, IsFemale, false, slot, KEY_TEXTURE, IDX_DIFFUSE)
                NiOverride.RemoveSkinOverride(self, IsFemale, false, slot, KEY_TEXTURE, IDX_NORMAL)
                NiOverride.RemoveSkinOverride(self, IsFemale, false, slot, KEY_TINT, -1)
            endif
            i += 1
        endwhile
        NiOverride.ApplySkinOverrides(self)
    endif

    if appliedXformNodes != None
        int i = 0
        while i < appliedXformNodes.Length
            string node = appliedXformNodes[i]
            if node != ""
                NiOverride.RemoveNodeTransformScale(self, false, IsFemale, node, ovrKey)
                NiOverride.RemoveNodeTransformPosition(self, false, IsFemale, node, ovrKey)
                NiOverride.RemoveNodeTransformRotation(self, false, IsFemale, node, ovrKey)
                NiOverride.RemoveNodeTransformScaleMode(self, false, IsFemale, node, ovrKey)
                NiOverride.UpdateNodeTransform(self, false, IsFemale, node)
            endif
            i += 1
        endwhile
    endif
EndFunction

; The transform "name" is the override KEY — it namespaces our layer so RaceMenu, XPMSE and we can each hold
; a value on the same node without overwriting one another, and so RemoveNodeTransform* only removes OURS.
; (Cannot be a variable called `key`: `Key` is a real form type in Skyrim — `Key extends MiscObject` — and
; Papyrus refuses a variable named after a known script/type.)
string Function XformKey()
    return "NPCM_Manolov"
EndFunction

; ⛔⛔ NEVER PASS AN ARRAY PROPERTY TO A HELPER FUNCTION. ⛔⛔
;
; The original version of this script had "ragged-array guard" helpers — Count(string[] a),
; At(string[] a, int i), AtBool(bool[] a, int i) … each starting with `if a == None`. They are
; WORTHLESS: Papyrus throws `Cannot cast from None to String[]` AT THE CALL, while binding the
; argument, so the `== None` check inside the body never runs. Measured in Papyrus.0.log: 20 cast
; errors, one per helper call, which aborted ApplyNodeTransforms and silently dropped the node
; rotations.
;
; And every array property goes None as soon as ONE property fails to initialize — which is what an
; EMPTY array property does, because Skyrim Papyrus has no zero-length arrays. (Fallout 4's arrays
; are resizable and tolerate empty, which is exactly why the FO4 script worked and this one did not.)
;
; So: the emitter now omits zero-length array properties entirely (NpcApplyScriptEmitter), and every
; guard here is INLINE — `X != None && i < X.Length` — never a call. Both halves are needed: the
; first removes the cause, the second means a None property degrades to "skip that field" instead of
; taking the whole apply down with it.

Function ApplyOverlays()
    appliedOvlNodes = None          ; ledger reset — repopulated below with exactly what we apply
    if OvlNode == None
        return
    endif
    int n = OvlNode.Length
    if n == 0
        return
    endif
    ; Papyrus arrays must be created with a LITERAL size (Utility.CreateStringArray is an SKSE addition and
    ; the vanilla Utility.psc we compile against does not have it), and 128 is Papyrus's hard array ceiling —
    ; so a 128-slot ledger can hold anything the payload could legally carry. Unused slots stay "" and
    ; RemovePrevious skips them.
    appliedOvlNodes = new string[128]

    ; MANDATORY and FIRST. With the default [Overlays] bPlayerOnly=true, OverlayInterface::OnAttach
    ; only builds the overlay nodes for an actor that HasOverlays() — and AddOverlays() is what
    ; registers the actor in that set (OverlayInterface.cpp:854 inserts the formID; the gate at
    ; :1011 then passes). Skip this and the overrides below land on nodes that were never created:
    ; no error, just silently invisible.
    NiOverride.AddOverlays(self)

    int i = 0
    while i < n
        string node = OvlNode[i]
        if node != ""
            if i < 128
                appliedOvlNodes[i] = node   ; remember it so the next re-apply can remove it first
            endif
            ; persist = TRUE on every call. With persist=false skee applies the value visually but
            ; never puts it in the store, so it is never serialized to the co-save and vanishes on
            ; the next load (PapyrusNiOverride.cpp:503-514).
            if OvlDiffuse != None
                if i < OvlDiffuse.Length
                    if OvlDiffuse[i] != ""
                        NiOverride.AddNodeOverrideString(self, IsFemale, node, KEY_TEXTURE, IDX_DIFFUSE, OvlDiffuse[i], true)
                    endif
                endif
            endif
            if OvlNormal != None
                if i < OvlNormal.Length
                    if OvlNormal[i] != ""
                        NiOverride.AddNodeOverrideString(self, IsFemale, node, KEY_TEXTURE, IDX_NORMAL, OvlNormal[i], true)
                    endif
                endif
            endif
            bool doTint = false
            if OvlHasTint != None
                if i < OvlHasTint.Length
                    doTint = OvlHasTint[i]
                endif
            endif
            if doTint
                if OvlTint != None
                    if i < OvlTint.Length
                        NiOverride.AddNodeOverrideInt(self, IsFemale, node, KEY_TINT, -1, OvlTint[i], true)
                    endif
                endif
            endif
            bool doAlpha = false
            if OvlHasAlpha != None
                if i < OvlHasAlpha.Length
                    doAlpha = OvlHasAlpha[i]
                endif
            endif
            if doAlpha
                if OvlAlpha != None
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
    appliedSkinSlots = None         ; ledger reset
    if SkinSlot == None
        return
    endif
    int n = SkinSlot.Length
    if n == 0
        return
    endif
    appliedSkinSlots = new int[128]      ; literal size — see the note in ApplyOverlays

    int i = 0
    while i < n
        int slot = SkinSlot[i]
        if slot != 0
            if i < 128
                appliedSkinSlots[i] = slot
            endif
            ; firstPerson = false: NPCs have no first-person skeleton.
            if SkinDiffuse != None
                if i < SkinDiffuse.Length
                    if SkinDiffuse[i] != ""
                        NiOverride.AddSkinOverrideString(self, IsFemale, false, slot, KEY_TEXTURE, IDX_DIFFUSE, SkinDiffuse[i], true)
                    endif
                endif
            endif
            if SkinNormal != None
                if i < SkinNormal.Length
                    if SkinNormal[i] != ""
                        NiOverride.AddSkinOverrideString(self, IsFemale, false, slot, KEY_TEXTURE, IDX_NORMAL, SkinNormal[i], true)
                    endif
                endif
            endif
            bool doTint = false
            if SkinHasTint != None
                if i < SkinHasTint.Length
                    doTint = SkinHasTint[i]
                endif
            endif
            if doTint
                if SkinTint != None
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
    ; NESTED ifs, never a compound condition. This does NOT rely on Papyrus short-circuiting `&&`/`||`
    ; — a dependency I did not verify and refuse to bet on: if it did not short-circuit, `X != None &&
    ; X.Length > i` would still evaluate X.Length on a None array and throw. Nested ifs are correct
    ; under either semantics.
    appliedXformNodes = None        ; ledger reset
    if NodeName == None
        return
    endif
    int n = NodeName.Length
    if n == 0
        return
    endif
    appliedXformNodes = new string[128]   ; literal size — see the note in ApplyOverlays

    string ovrKey = XformKey()

    int i = 0
    while i < n
        string node = NodeName[i]
        if node != ""
            if i < 128
                appliedXformNodes[i] = node
            endif

            ; --- scale
            bool doScale = false
            if NodeHasScale != None
                if i < NodeHasScale.Length
                    doScale = NodeHasScale[i]
                endif
            endif
            if doScale
                if NodeScale != None
                    if i < NodeScale.Length
                        NiOverride.AddNodeTransformScale(self, false, IsFemale, node, ovrKey, NodeScale[i])
                    endif
                endif
            endif

            ; --- position
            bool doPos = false
            if NodeHasPos != None
                if i < NodeHasPos.Length
                    doPos = NodeHasPos[i]
                endif
            endif
            if doPos
                if NodePosX != None
                    if i < NodePosX.Length
                        float[] pos = new float[3]
                        pos[0] = NodePosX[i]
                        pos[1] = NodePosY[i]
                        pos[2] = NodePosZ[i]
                        NiOverride.AddNodeTransformPosition(self, false, IsFemale, node, ovrKey, pos)
                    endif
                endif
            endif

            ; --- rotation (9 raw matrix floats, NOT euler — see the NodeRotMatrix property doc)
            bool doRot = false
            if NodeHasRot != None
                if i < NodeHasRot.Length
                    doRot = NodeHasRot[i]
                endif
            endif
            if doRot
                if NodeRotM0 != None
                    if i < NodeRotM0.Length
                        float[] rot = new float[9]
                        rot[0] = NodeRotM0[i]
                        rot[1] = RotAt(NodeRotM1, i)
                        rot[2] = RotAt(NodeRotM2, i)
                        rot[3] = RotAt(NodeRotM3, i)
                        rot[4] = RotAt(NodeRotM4, i)
                        rot[5] = RotAt(NodeRotM5, i)
                        rot[6] = RotAt(NodeRotM6, i)
                        rot[7] = RotAt(NodeRotM7, i)
                        rot[8] = RotAt(NodeRotM8, i)
                        NiOverride.AddNodeTransformRotation(self, false, IsFemale, node, ovrKey, rot)
                    endif
                endif
            endif

            ; --- scale mode
            if NodeScaleMode != None
                if i < NodeScaleMode.Length
                    if NodeScaleMode[i] >= 0
                        NiOverride.AddNodeTransformScaleMode(self, false, IsFemale, node, ovrKey, NodeScaleMode[i])
                    endif
                endif
            endif

            NiOverride.UpdateNodeTransform(self, false, IsFemale, node)
        endif
        i += 1
    endwhile
EndFunction

; Element k of node i's rotation matrix. Safe to call with a None array (returns 0.0) — the ONE case where
; a helper may take an array: every call site passes NodeRotM1..8, which the emitter always writes together
; with NodeRotM0, and the caller has already established NodeRotM0 is non-None and long enough. If a future
; payload ever omits one of them, this degrades to a 0 in that matrix cell rather than throwing.
; ⛔ Papyrus throws "Cannot cast from None to Float[]" while BINDING the argument, so the `== None` check
; below only protects the SHORT case, never the None case. Do not rely on it for arrays that can be absent.
float Function RotAt(float[] a, int i)
    if a == None
        return 0.0
    endif
    if i >= a.Length
        return 0.0
    endif
    return a[i]
EndFunction
