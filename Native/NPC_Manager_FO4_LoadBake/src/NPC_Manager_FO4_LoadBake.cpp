// NPC_Manager_FO4_LoadBake - an F4SE plugin.
//
// Lets Fallout 4 use the baked head (FaceGeom) of NPCs that are overridden by a plain .esp.
// No configuration: the criterion is the FILE. If the bake exists, it is used.
//
//   The engine decides whether it may use the preprocessed head in a predicate -0x1406DF3A0
//   in 1.11.240-. Three of its exits look at THE PLUGIN, not at the NPC:
//
//     0x1406DF424  file = GetFile(form, -1)      ; the LAST file (the overriding one)
//     0x1406DF439  call (a) -> [file+0x334] & 1  ; .esm/.esl extension or TES4 ESM flag
//     0x1406DF445  call (b) -> [file+0x370]      ; load index; ==0 -> TRUE outright
//     0x1406DF452  call (c) -> name list         ; the 8 DLC + the lines of Fallout4.ccc
//     0x1406DF45B  mov al,1                      ; TRUE
//
//   A plain .esp fails (a) and (c) => the FaceGeom is ignored and the head is rebuilt at
//   runtime out of head parts.
//
// WHAT THIS PLUGIN DOES: it does NOT wrap the predicate -that has a problem, see below-. It
// redirects THREE `call` instructions at their call site:
//
//     GetFile  -> record the root NPC being evaluated (the other two do not receive it)
//     (a)      -> if the engine says no and the bake EXISTS, answer yes
//     (c)      -> same
//
// If the engine already said yes, nothing is touched. It never returns NO where the engine
// returned YES.
//
//   STOP - WHY THE PREDICATE IS NOT WRAPPED. Its exits are not all alike: the CATEGORICAL ones
//   -the player, the two player-base globals, the IsChildPlayer keyword, and a face EDITED AND
//   STORED in the save- set `dil = 0` and bail out at 0x1406DF42C, BEFORE (a) and (c). The
//   plugin-based ones leave through a different path. Both return 0 to the caller, so from a
//   wrapper they are INDISTINGUISHABLE, and an earlier version "rescued" the categorical ones
//   too: the trace caught it with the player (FormID 00000007) reaching the file check. The
//   serious case was not that one but the NPC whose face had been edited in the save, which got
//   the stale bake forced on top of it. Redirecting the `call` sites means the categorical
//   exits never reach us.
//
//   STOP - AND THE EXISTENCE CHECK IS NOT A LUXURY: it is what prevents reproducing X-Cell's
//   missing-head bug. That mod REPLACES the predicate, so every NPC without a FaceGeom loses
//   its head (there are dozens of "Missing Heads with X-Cell Facegen Fix" mods on Nexus).
//   Here, with no file, the behaviour is EXACTLY vanilla.
//
//   NOTE - THE ACBS "Is CharGen Face Preset" FLAG (bit 2 of [npc+0x70]; F4SE 0.7.9
//   GameFormComponents.h kFlagIsPreset = 0x04) IS NOT READ BY THIS PREDICATE. Measured over the
//   whole function 0x1406DF3A0-0x1406DF483: the only NPC fields it touches are +0x270 (template
//   chain), +0x14 (FormID) and +0x160 (keyword form). The [rbx+0x70] at 0x1406DF44E is the FILE
//   name, not the NPC - rbx was overwritten by GetFile at 0x1406DF429. A sibling gate at
//   0x14065DC60 DOES test that bit (0x14065DD53, preset -> reject) and reads the same global
//   0x142F25EC0 as the caller; this plugin does not touch that function, and rewriting a call
//   SITE leaves the shared helpers untouched for every other caller.
//
// LOG: errors only, in Documents\My Games\Fallout4\F4SE\. If all is well the file is never
// created. No external dependencies: the only things needed from F4SE are the version block
// structure and the runtime constants, copied from its header into F4sePluginKit.h.

#include "F4sePluginKit.h"   // FO4_Base_Library\Native: version block, log, scanner, call redirection
#include "LoadBakeSites.h"   // WHERE it hooks (pure resolution: the probe runs the same code)
#include <unordered_map>

using f4kit::Err;

// ============================================================================================
// F4SE version block (structure and constants: F4sePluginKit.h, copied from F4SE's PluginAPI.h)
// ============================================================================================
// STOP - addressIndependence = 0 ON PURPOSE. F4SE SKIPS the compatibleVersions walk entirely
// if you declare independence (verified in its code, 0x18005B794 of f4se_1_11_240.dll): pinned
// and dynamic are MUTUALLY EXCLUSIVE. Pinned was chosen, like the three plugins that already
// load on this rig (f4ee, mcm, HHS): on an unknown executable F4SE does not load us and says
// so itself, rather than injecting us into an engine nobody has verified.
extern "C" __declspec(dllexport) f4kit::F4SEPluginVersionData F4SEPlugin_Version =
{
    f4kit::F4SEPluginVersionData::kVersion,
    1,
    "NPC Manager FO4 - Load Bake",
    "Manolo",
    0, 0,
    { f4kit::kRuntime_1_11_240,   // VERIFIED (MD5 c5791a0ce539465701c6e38fa465960c)
      f4kit::kRuntime_1_10_984,   // declared; resolved by signature, UNTESTED
      f4kit::kRuntime_1_10_163,   // same
      0 },
    0, 0, 0, { 0 }
};

// ============================================================================================
// Engine functions we reuse
// ============================================================================================
// Both come out of the FaceGeom loader itself (signature 2), which means we do EXACTLY what
// the engine does two instructions after accepting the head. We do not replicate its logic.
//
//   BuildPath  0x140658E60 in 1.11.240
//     Walks the template chain (+0x270) up to the ROOT NPC, asks for its ORIGIN file
//     (GetFile(form, 0) -> index 0, not -1), and formats
//         "Meshes\Actors\Character\FaceGenData\FaceGeom\%s\%08X.NIF"
//     with the plugin name ([file+0x70]) and the FormID ([npc+0x14]) masked to 24 bits, or to
//     12 if the file is light. Buffer of 0x104. Returns false if the NPC has no file.
//
//   Exists  0x1402FC0C0 in 1.11.240
//     Normalizes against the "meshes\" prefix and queries the global resource manager. It is
//     ARCHIVE AWARE: it sees both loose files and anything inside a .ba2. That is why
//     GetFileAttributes is not used - it would report "missing" for a packed bake.
typedef bool (*FnBuildPath)(void* npc, char* outBuf);
typedef bool (*FnExists)(const char* relativePath);

static FnBuildPath g_buildPath = nullptr;
static FnExists    g_exists    = nullptr;

// ============================================================================================
// TRACE - measurement instrument, GATED
// ============================================================================================
// With TRACE at 0 NOTHING remains: not the counters, not the function, not a single call. It
// all lives inside `#if TRACE`, so the compiler emits no instruction. That is the only honest
// way to leave an instrument in place at no cost: switching it off has to mean it does not
// exist, not that "you barely notice it".
//
// STOP - And with TRACE at 1 the plugin ALWAYS WRITES THE LOG, against its own contract, and
// does one `fflush` per call. That is a MEASUREMENT build, not for normal use.
#define TRACE 0

#if TRACE
static volatile LONG g_nCalls   = 0;   // entries into the hook (via GetFile)
static volatile LONG g_nInA     = 0;   // times check (a) was reached
static volatile LONG g_nInC     = 0;   // times check (c) was reached
static volatile LONG g_nLookups = 0;   // distinct NPCs we looked up on disk / in archives
static volatile LONG g_nRescued = 0;   // ANSWERS WE CHANGED (not "files that exist")

// Same file and SAME LOCK as the errors (F4sePluginKit.h).
static void Trace(const char* fmt, ...)
{
    va_list a;
    va_start(a, fmt);
    f4kit::LogLineV(fmt, a);
    va_end(a);
}
#endif

// ============================================================================================
// Existence cache
// ============================================================================================
// The predicate runs on every NPC load. One query to the resource system per call is
// unacceptable, and the answer CANNOT change while the game runs: baking requires writing the
// .ba2 files, and the game holds them open. So a cached entry is valid for the whole session
// BY CONSTRUCTION, not out of optimism. Hence no invalidation and no TTL.

static std::unordered_map<UINT32, bool> g_cache;
static SRWLOCK                          g_cacheLock = SRWLOCK_INIT;

// TESForm FormID: +0x14. Measured in the predicate itself (`cmp dword [rbx+0x14], 7`, the
// player exit) and in the path builder (`mov ebx, [rbx+0x14]`).
static UINT32 FormIDOf(void* npc) { return *(UINT32*)((unsigned char*)npc + 0x14); }

// STOP - REENTRANCY GUARD. Below we call engine functions (build the path, ask the resource
// manager). If any of those ever triggered a head load, the predicate would reenter ON THIS
// SAME THREAD and clobber `t_rootNpc`: the outer evaluation would carry on with the inner
// NPC. It is unlikely -an existence query should not load anything- but it is not proven, and
// covering it costs one bool per thread.
static thread_local bool t_inside = false;

static bool HasBake(void* npc)
{
    if (!npc || !g_buildPath || !g_exists) return false;
    if (t_inside) return false;               // reentrancy: we answer no and move on

    const UINT32 id = FormIDOf(npc);
    AcquireSRWLockShared(&g_cacheLock);
    auto it = g_cache.find(id);
    const bool hit = (it != g_cache.end());
    const bool val = hit ? it->second : false;
    ReleaseSRWLockShared(&g_cacheLock);
    if (hit) return val;

    t_inside = true;
    char path[0x104]{};                       // 0x104: the size the engine itself passes in
    bool gotPath = g_buildPath(npc, path);
    bool r = gotPath && g_exists(path);
    t_inside = false;
#if TRACE
    // STOP - THIS LINE DOES NOT SAY "rescued": it says whether the FILE is there. They are
    // different things, and confusing them already made me misread the trace once (I counted
    // 23 rescues where there were 2). A real rescue -having changed the engine's answer- is
    // recorded by [rescue].
    InterlockedIncrement(&g_nLookups);
    Trace("[npc] %08X  path=%s  exists=%s", id,
          gotPath ? path : "(could not be built)", r ? "YES" : "NO");
#endif

    AcquireSRWLockExclusive(&g_cacheLock);
    g_cache[id] = r;
    ReleaseSRWLockExclusive(&g_cacheLock);
    return r;
}

// ============================================================================================
// The three hooks
// ============================================================================================
// STOP - the whole predicate is NOT wrapped, and the reason is in its own disassembly. Its
// exits are NOT all alike:
//
//   0x1406DF3C3  if formID == 7                 -> dil = 0      CATEGORICAL (the player)
//   0x1406DF3C9  if npc == [0x1431F1278]        -> dil = 0      CATEGORICAL (player base, F)
//   0x1406DF3D2  if npc == [0x1431F1240]        -> dil = 0      CATEGORICAL (player base, M)
//   0x1406DF3EF  if HasKeyword(IsChildPlayer)   -> dil = 0      CATEGORICAL
//   0x1406DF406  if GetChange(npc, 0x800)       -> dil = 0      CATEGORICAL (face in the SAVE)
//   0x1406DF40F  dil = 1
//   0x1406DF424  rbx = GetFile(rootNpc, -1)
//   0x1406DF42C  test dil,dil / je 0x1406DF475  <- the categorical ones BAIL OUT HERE
//   0x1406DF439  (a) ...                        <- only reached with dil == 1
//   0x1406DF452  (c) ...
//
// Both classes return 0 to the caller, so FROM A WRAPPER THEY ARE INDISTINGUISHABLE: an
// earlier version of this plugin wrapped the predicate and therefore "rescued" the categorical
// rejections too. The trace exposed it, with the player (FormID 00000007) reaching the file
// check; it got away with it only because no bake existed for the player.
//
// The case that really mattered: an NPC with a face EDITED AND STORED in the save. The engine
// deliberately refuses the bake there, and the wrapper forced the stale one on top of it.
//
// By redirecting the CALL sites instead of wrapping the function, the categorical exits never
// reach us: they bail out first. The engine still decides everything that is not the plugin.

// The ROOT NPC (already resolved through the template chain) the predicate is evaluating.
// Captured at GetFile and consumed by (a) and (c), which receive the file but not the NPC.
// It is thread_local because the predicate hangs off `QueuedHead`, i.e. off queued tasks.
static thread_local void* t_rootNpc = nullptr;

typedef void* (*FnGetFile)(void* form, int idx);
typedef bool  (*FnIsMaster)(void* file);
typedef bool  (*FnNameInList)(const char* name);

static FnGetFile    g_origGetFile = nullptr;
static FnIsMaster   g_origA       = nullptr;
static FnNameInList g_origC       = nullptr;

// 0x1406DF424 - GetFile(rootNpc, -1). All we do is record the NPC.
static void* HookGetFile(void* form, int idx)
{
#if TRACE
    const LONG n = InterlockedIncrement(&g_nCalls);
    // The FIRST 120 calls, one by one, with the FormID: it is the only thing that answers
    // "how many times does an NPC repeat". The [npc] line alone does not say it, because it is
    // written on a cache miss and repeats leave no trace. MEASURED: 2 to 14 times per NPC.
    if (n <= 120)
        Trace("[call] #%-4ld npc=%08X", n, form ? FormIDOf(form) : 0);
    // The SPACING between summaries answers "per frame or per load": measured, 160 calls in
    // about 3 minutes, i.e. per load.
    if (n % 20 == 0)
        Trace("[summary] calls=%ld  inA=%ld  inC=%ld  npcsLookedUp=%ld  rescued=%ld",
              n, g_nInA, g_nInC, g_nLookups, g_nRescued);
#endif
    t_rootNpc = form;
    return g_origGetFile ? g_origGetFile(form, idx) : nullptr;
}

// 0x1406DF439 - (a) is the file a "master"? (.esm/.esl extension or TES4 ESM flag)
static bool HookA(void* file)
{
#if TRACE
    InterlockedIncrement(&g_nInA);
#endif
    // STOP - THE ENGINE FIRST. An earlier version asked about the file BEFORE consulting the
    // engine: it returned the same answer, but made a resource-manager query that was not
    // needed and -worse- counted as a "rescue" cases the engine already accepted. MEASURED on
    // the rig's log: of 23 NPCs, 21 are won by Fallout4.esm and the engine accepted them on
    // its own; the real rescues were 2. Asking first saves the query in 21 of 23 and leaves
    // the counter telling the truth.
    if (g_origA && g_origA(file)) return true;

    // The engine says no. Was it only because of the plugin? If the bake is there, accept it.
    // The NPC of THIS evaluation is taken and PUT BACK afterwards: even if the inner query
    // were to clobber it, (c) -which runs later, in the same invocation- finds it intact.
    void* npc = t_rootNpc;
    bool has = HasBake(npc);
    t_rootNpc = npc;
#if TRACE
    if (has) { InterlockedIncrement(&g_nRescued);
               Trace("[rescue] (a) npc=%08X", npc ? FormIDOf(npc) : 0); }
#endif
    return has;
}

// 0x1406DF452 - (c) is the name in the list of DLC + Fallout4.ccc?
static bool HookC(const char* name)
{
#if TRACE
    InterlockedIncrement(&g_nInC);
#endif
    // Same criterion as in (a): the engine first, the file afterwards.
    if (g_origC && g_origC(name)) return true;

    void* npc = t_rootNpc;
    bool has = HasBake(npc);
    t_rootNpc = npc;
#if TRACE
    if (has) { InterlockedIncrement(&g_nRescued);
               Trace("[rescue] (c) npc=%08X", npc ? FormIDOf(npc) : 0); }
#endif
    return has;
}

// ============================================================================================
// Installing the redirection (WHERE: LoadBakeSites.h; the call redirection itself: F4sePluginKit.h)
// ============================================================================================
static void Install()
{
    loadbake::Sites sites;
    if (!loadbake::Resolve((unsigned char*)GetModuleHandleW(nullptr), sites)) return;
    g_buildPath = (FnBuildPath)sites.buildPath;
    g_exists    = (FnExists)sites.exists;

    // STOP - The ORDER matters: GetFile first. If that one hooks and one of the others fails,
    // the plugin changes no decision (recording the NPC does nothing on its own); the other
    // way round, (a) would consult a t_rootNpc nobody fills.
    void* gf = f4kit::RedirectCall(sites.siteGetFile, (void*)&HookGetFile, "getfile");
    if (!gf) return;
    g_origGetFile = (FnGetFile)gf;

    void* a = f4kit::RedirectCall(sites.siteA, (void*)&HookA, "master-check");
    if (!a) return;
    g_origA = (FnIsMaster)a;

    void* c = f4kit::RedirectCall(sites.siteC, (void*)&HookC, "name-list");
    if (!c) { Err("[hook] (a) got hooked and (c) did not: the gate is left half done."); return; }
    g_origC = (FnNameInList)c;
}

extern "C" __declspec(dllexport) bool F4SEPlugin_Load(const void* /*f4se*/)
{
    f4kit::InitLog("NPC_Manager_FO4_LoadBake.log");
    Install();
#if TRACE
    if (g_origC)
        Trace("[trace] hooked. MEASUREMENT build: always writes the log (TRACE=1).");
#endif
    return true;        // we never return false: we do not block the game over this
}

BOOL APIENTRY DllMain(HMODULE, DWORD, LPVOID) { return TRUE; }
