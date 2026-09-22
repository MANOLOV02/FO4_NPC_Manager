# NPC_Manager_FO4_LoadBake

An F4SE plugin that does **one thing**: let Fallout 4 use the baked head (FaceGeom) of NPCs
that are overridden by a plain `.esp`.

**No configuration.** There is no `.ini` and no plugin list — the criterion is the *file*. If
the baked head exists, it is used; if it does not, the game behaves exactly as it always did.

## The problem

The engine has a predicate — `0x1406DF3A0` in 1.11.240 — that decides whether it may use the
preprocessed head. It first rejects for reasons about the NPC, and only then looks at the
**plugin**:

```
0x1406DF3C3  if formID == 7                 -> dil = 0    CATEGORICAL (the player)
0x1406DF3C9  if npc == [0x1431F1278]        -> dil = 0    CATEGORICAL (player base, F)
0x1406DF3D2  if npc == [0x1431F1240]        -> dil = 0    CATEGORICAL (player base, M)
0x1406DF3EF  if HasKeyword(IsChildPlayer)   -> dil = 0    CATEGORICAL
0x1406DF406  if GetChange(npc, 0x800)       -> dil = 0    CATEGORICAL (face edited in the SAVE)
0x1406DF40F  dil = 1
0x1406DF424  file = GetFile(rootNpc, -1)                  ; the LAST file (the one overriding)
0x1406DF42C  test dil,dil / je              -> the categorical ones BAIL OUT HERE
0x1406DF439  call (a) -> [file+0x334] & 1                 ; .esm/.esl extension or TES4 ESM flag
0x1406DF445  call (b) -> [file+0x370]                     ; load index; ==0 -> TRUE outright
0x1406DF452  call (c) -> name list                        ; the 8 DLC + the lines of Fallout4.ccc
0x1406DF45B  mov al,1                                     ; TRUE
```

A plain `.esp` fails (a) and (c). The result: the baked FaceGeom is ignored and the head is
rebuilt at runtime from head parts. Measured in game: NOP out those two `je` and the baked head
shows up.

## What this plugin does

It redirects **three** `call` instructions at their call site. It does not replace the
predicate and does not touch any function prologue, so other callers of those engine utilities
are unaffected.

| site | what we do |
|---|---|
| `GetFile` | record the root NPC being evaluated (it is the only one of the three that receives it) |
| `(a)` | if the engine says no and the baked head **exists**, answer yes |
| `(c)` | same |

If the engine already said yes, nothing is touched. **It never returns NO where the engine
returned YES.**

### Why redirect the calls instead of wrapping the function

Because its exits are not all alike. The **categorical** ones set `dil = 0` and bail out at
`0x1406DF42C`, before (a) and (c). The plugin-based ones leave through a different path. But
**both return 0 to the caller**, so from a wrapper they are indistinguishable.

An earlier version wrapped the predicate and therefore "rescued" the categorical rejections
too. The trace caught it with the player (FormID `00000007`) reaching the file check. That case
was harmless — no baked head existed for the player — but the serious one was an NPC whose face
had been **edited and stored in the save**: the engine deliberately refuses the bake there, and
the wrapper forced the stale one on top of it.

Redirecting the calls means the categorical exits never reach us. The engine still decides
everything that is not about the plugin.

### Why the existence check is not optional

It is what prevents reproducing the headless-NPC bug of X-Cell. That mod **replaces** the
predicate, so every NPC without a FaceGeom loses its head — hence the dozens of
"Missing Heads with X-Cell Facegen Fix" mods on Nexus. Here, with no file, the behaviour is
exactly vanilla.

The lookup uses **the engine's own functions** — the same two its FaceGeom loader calls two
instructions after accepting the head:

- `0x140658E60` builds `Meshes\Actors\Character\FaceGenData\FaceGeom\<plugin>\<FormID>.NIF`,
  walking the template chain up to the root NPC, taking that NPC's **origin** plugin (index 0,
  not −1) and masking the FormID to 24 bits — or to **12 if the plugin is light**.
- `0x1402FC0C0` asks the resource manager. It is **archive aware**: it sees both loose files and
  anything inside a `.ba2`. A plain `GetFileAttributes` would report "missing" for a packed
  bake.

The result is cached per FormID. There is no invalidation and no TTL, and none is needed:
baking requires writing the `.ba2` files, and the game holds them open. The entry is valid for
the whole session by construction.

## How it finds the addresses

Two byte signatures, each with **exactly one match** in the 36 MB of `.text` (measured): one
locates the predicate, the other a fragment of the loader from which the two functions above
are read. Nothing is hardcoded, and Address Library does not need to be installed.

Before writing anything it verifies the function prologue. If anything does not line up —
signature missing, two matches, different prologue — **it does not hook, it says so in the log,
and the game is left untouched.**

## Installation

```
Data\F4SE\Plugins\NPC_Manager_FO4_LoadBake.dll
```

That is all. Requires F4SE.

## Log

`Documents\My Games\Fallout4\F4SE\NPC_Manager_FO4_LoadBake.log`, and **only if something
fails**. If everything works, the file is never created.

There is also a measurement instrument gated by `#define TRACE` in the source. With `TRACE 0` —
what ships — not a single instruction remains: verified, the strings `[call]`, `[npc]`,
`[summary]`, `[rescue]` and `[trace]` are absent from the binary. With `TRACE 1` it always
writes the log and does an `fflush` per call: that is a measurement build, not for normal use.

## Game versions

Pinned through `compatibleVersions`. On an unlisted runtime F4SE refuses to load it and says so
itself (`disabled, incompatible with current version of the game`).

| runtime | status |
|---|---|
| 1.11.240 | **verified** (MD5 `c5791a0ce539465701c6e38fa465960c`) |
| 1.10.984 | declared; resolved by signature, **untested** |
| 1.10.163 | same |

`addressIndependence = 0` is deliberate: F4SE **skips** the `compatibleVersions` walk entirely
if you declare independence (verified in its code, `0x18005B794` of `f4se_1_11_240.dll`), so
pinned and dynamic are mutually exclusive. Pinned was chosen, like the plugins that already
work (`f4ee`, `mcm`, `HHS`): on an unknown executable we do not load at all, rather than
injecting into an engine nobody has verified.

Source is public, as the F4SE readme requires.
