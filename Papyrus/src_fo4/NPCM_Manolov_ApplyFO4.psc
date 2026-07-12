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

bool Property IsFemale = false Auto

int Property SchemaVersion = 1 Auto
{Bumped by the app when the authored values change, so an updated plugin re-applies to actors that
 already spawned in an existing save.}

;-- overlays (parallel arrays, one entry per overlay) -------------------------------------------
string[] Property OvlTemplate Auto
{f4ee overlay template id (the `template` member of Overlays:Entry) — from the installed
 overlays.json catalog, NOT a loose texture path.}
int[]   Property OvlPriority Auto
float[] Property OvlRed Auto
float[] Property OvlGreen Auto
float[] Property OvlBlue Auto
float[] Property OvlAlpha Auto
float[] Property OvlOffsetU Auto
float[] Property OvlOffsetV Auto
float[] Property OvlScaleU Auto
float[] Property OvlScaleV Auto

;-- skin override (single template id; "" = none) -----------------------------------------------
string Property SkinTemplate = "" Auto

;-- per-instance state (persists in the savegame, like vanilla TeleportActorScript) -------------
int appliedVersion = -1

; The uids of the overlays WE added to this actor. Script variables persist per instance in the
; savegame, so on a re-apply we can remove exactly our own overlays and nobody else's — see
; ApplyOverlays. (A plain variable, not a Property: Papyrus rejects doc strings on variables, hence
; this comment rather than a {...} block.)
int[] myUids

Event OnLoad()
    if appliedVersion == SchemaVersion
        return                    ; already applied to THIS actor, and nothing changed since
    endif
    appliedVersion = SchemaVersion

    ApplyOverlays()
    ApplySkin()
EndEvent

Function ApplyOverlays()
    Actor a = self as Actor

    ; ⚠ FO4-ONLY HAZARD, and the reason we track uids.
    ; Overlays.Add() mints a NEW uid on every call and f4ee persists overlays in the co-save. So a
    ; re-apply (SchemaVersion bumped because the user edited this NPC and re-saved) would STACK a second
    ; copy of every tattoo — forever. SSE has no such problem: NiOverride node overrides are keyed by
    ; node+key+index and simply overwrite.
    ;
    ; The fix is NOT Overlays.RemoveAll(a, ...) — that would also delete overlays some OTHER mod put on
    ; this actor. Instead we remove precisely the uids we minted last time, which we remembered in the
    ; savegame. Anything anyone else added is untouched.
    if myUids != None
        int u = 0
        while u < myUids.Length
            Overlays.Remove(a, IsFemale, myUids[u])
            u += 1
        endwhile
    endif

    ; Start a fresh uid ledger. FO4 Papyrus arrays are resizable (Add/Clear), unlike Skyrim's — so we
    ; grow it as we mint uids instead of pre-sizing. (Utility.CreateIntArray is a Skyrim-only function
    ; and does not exist here.)
    myUids = new int[1]
    myUids.Clear()

    int n = 0
    if OvlTemplate != None
        n = OvlTemplate.Length
    endif
    if n == 0
        Overlays.Update(a)
        return
    endif

    int i = 0
    while i < n
        if OvlTemplate[i] != ""
            Overlays:Entry e = new Overlays:Entry
            e.template = OvlTemplate[i]
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

            if OvlPriority != None
                if i < OvlPriority.Length
                    e.priority = OvlPriority[i]
                endif
            endif
            if OvlRed != None
                if i < OvlRed.Length
                    e.red = OvlRed[i]
                endif
            endif
            if OvlGreen != None
                if i < OvlGreen.Length
                    e.green = OvlGreen[i]
                endif
            endif
            if OvlBlue != None
                if i < OvlBlue.Length
                    e.blue = OvlBlue[i]
                endif
            endif
            if OvlAlpha != None
                if i < OvlAlpha.Length
                    e.alpha = OvlAlpha[i]
                endif
            endif
            if OvlOffsetU != None
                if i < OvlOffsetU.Length
                    e.offset_u = OvlOffsetU[i]
                endif
            endif
            if OvlOffsetV != None
                if i < OvlOffsetV.Length
                    e.offset_v = OvlOffsetV[i]
                endif
            endif
            if OvlScaleU != None
                if i < OvlScaleU.Length
                    e.scale_u = OvlScaleU[i]
                endif
            endif
            if OvlScaleV != None
                if i < OvlScaleV.Length
                    e.scale_v = OvlScaleV[i]
                endif
            endif

            myUids.Add(Overlays.Add(a, IsFemale, e))
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
    if SkinTemplate == ""
        BodyGen.RemoveSkinOverride(self as Actor)
        return
    endif
    ; SetSkinOverride calls UpdateSkinOverride internally (PapyrusBodyGen.cpp:109) — no refresh needed.
    BodyGen.SetSkinOverride(self as Actor, SkinTemplate)
EndFunction
