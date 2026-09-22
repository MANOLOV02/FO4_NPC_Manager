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
//   -player, IsChildPlayer keyword, preset, and a face EDITED AND STORED in the save- set
//   `dil = 0` and bail out at 0x1406DF42C, BEFORE (a) and (c). The plugin-based ones leave
//   through a different path. Both return 0 to the caller, so from a wrapper they are
//   INDISTINGUISHABLE, and an earlier version "rescued" the categorical ones too: the trace
//   caught it with the player (FormID 00000007) reaching the file check. The serious case was
//   not that one but the NPC whose face had been edited in the save, which got the stale bake
//   forced on top of it. Redirecting the `call` sites means the categorical exits never reach
//   us.
//
//   STOP - AND THE EXISTENCE CHECK IS NOT A LUXURY: it is what prevents reproducing X-Cell's
//   missing-head bug. That mod REPLACES the predicate, so every NPC without a FaceGeom loses
//   its head (there are dozens of "Missing Heads with X-Cell Facegen Fix" mods on Nexus).
//   Here, with no file, the behaviour is EXACTLY vanilla.
//
// LOG: errors only, in Documents\My Games\Fallout4\F4SE\. If all is well the file is never
// created. No external dependencies: the only things needed from F4SE are the version block
// structure and the runtime constants, declared below, copied from its header.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <share.h>   // _SH_DENYWR: we write, but it can still be READ live
#include <string>
#include <unordered_map>

#pragma comment(lib, "shell32.lib")

// ============================================================================================
// F4SE version block
// Copied from f4se-0.7.9/f4se/PluginAPI.h (shipped in 'Fallout 4\src\f4se-0.7.9.tar.gz').
// ============================================================================================
struct F4SEPluginVersionData
{
    enum { kVersion = 1 };
    UINT32 dataVersion;
    UINT32 pluginVersion;
    char   name[256];
    char   author[256];
    UINT32 addressIndependence;
    UINT32 structureIndependence;
    UINT32 compatibleVersions[16];
    UINT32 seVersionRequired;
    UINT32 reservedNonBreaking;
    UINT32 reservedBreaking;
    UINT8  reserved[512];
};

#define MAKE_EXE_VERSION(major, minor, build) \
    ((((major) & 0xFF) << 24) | (((minor) & 0xFF) << 16) | (((build) & 0xFFF) << 4))

#define RUNTIME_VERSION_1_10_163  MAKE_EXE_VERSION(1, 10, 163)
#define RUNTIME_VERSION_1_10_984  MAKE_EXE_VERSION(1, 10, 984)
#define RUNTIME_VERSION_1_11_240  MAKE_EXE_VERSION(1, 11, 240)

// STOP - addressIndependence = 0 ON PURPOSE. F4SE SKIPS the compatibleVersions walk entirely
// if you declare independence (verified in its code, 0x18005B794 of f4se_1_11_240.dll): pinned
// and dynamic are MUTUALLY EXCLUSIVE. Pinned was chosen, like the three plugins that already
// load on this rig (f4ee, mcm, HHS): on an unknown executable F4SE does not load us and says
// so itself, rather than injecting us into an engine nobody has verified.
extern "C" __declspec(dllexport) F4SEPluginVersionData F4SEPlugin_Version =
{
    F4SEPluginVersionData::kVersion,
    1,
    "NPC Manager FO4 - Load Bake",
    "Manolo",
    0, 0,
    { RUNTIME_VERSION_1_11_240,   // VERIFIED (MD5 c5791a0ce539465701c6e38fa465960c)
      RUNTIME_VERSION_1_10_984,   // declared; resolved by signature, UNTESTED
      RUNTIME_VERSION_1_10_163,   // same
      0 },
    0, 0, 0, { 0 }
};

// ============================================================================================
// Log - ERRORS ONLY. The file is created only when there is something to say.
// ============================================================================================
static FILE*       g_log = nullptr;
static std::string g_logPath;
// TWO THREADS can write the log at once: the predicate hangs off `QueuedHead`, i.e. off queued
// tasks. Without this, two simultaneous `fopen_s` calls clobber the handle and the lines come
// out interleaved exactly when the log is the only thing we have to understand what happened.
static SRWLOCK     g_logLock = SRWLOCK_INIT;

static void ResolveLogPath()
{
    PWSTR docs = nullptr;
    if (FAILED(SHGetKnownFolderPath(FOLDERID_Documents, 0, nullptr, &docs))) return;
    char base[MAX_PATH]{};
    WideCharToMultiByte(CP_ACP, 0, docs, -1, base, MAX_PATH, nullptr, nullptr);
    CoTaskMemFree(docs);
    g_logPath = std::string(base) + "\\My Games\\Fallout4\\F4SE\\NPC_Manager_FO4_LoadBake.log";
}

static void Err(const char* fmt, ...)
{
    AcquireSRWLockExclusive(&g_logLock);
    if (!g_log)
    {
        if (g_logPath.empty() || (g_log = _fsopen(g_logPath.c_str(), "w", _SH_DENYWR)) == nullptr)
        { ReleaseSRWLockExclusive(&g_logLock); return; }
    }
    va_list a;
    va_start(a, fmt);
    vfprintf(g_log, fmt, a);
    va_end(a);
    fputc(0x0A, g_log);
    fflush(g_log);
    ReleaseSRWLockExclusive(&g_logLock);
}

// ============================================================================================
// Signatures
// ============================================================================================
// Both are MEASURED against Fallout4.exe 1.11.240 and each appears EXACTLY ONCE in the 36 MB
// of .text. The wildcards fall on call/jcc displacements, which is precisely what changes
// between builds.
struct Signature { const unsigned char* bytes; const bool* fixed; size_t n; };

// (1) The heart of the predicate. Used to locate the function (its start is 0x9E earlier).
//   84 C0 74 26                test al,al / je FALSE        <- result of (a)
//   48 8B CB E8 ?? ?? ?? ??    mov rcx,rbx / call (b)
//   84 C0 74 0D                test/je -> TRUE
//   48 8D 4B 70 E8 ?? ?? ?? ?? lea rcx,[rbx+0x70] / call (c)   <- the plugin NAME
//   84 C0 74 0D B0 01          test/je -> FALSE ; mov al,1 -> TRUE
static const unsigned char kSigPredB[] = {
    0x84,0xC0, 0x74,0x26, 0x48,0x8B,0xCB, 0xE8,0,0,0,0,
    0x84,0xC0, 0x74,0x0D, 0x48,0x8D,0x4B,0x70, 0xE8,0,0,0,0,
    0x84,0xC0, 0x74,0x0D, 0xB0,0x01 };
static const bool kSigPredF[] = {
    1,1, 1,1, 1,1,1, 1,0,0,0,0,
    1,1, 1,1, 1,1,1,1, 1,0,0,0,0,
    1,1, 1,1, 1,1 };
static const ptrdiff_t kPredFromSig = -0x9E;         // start of the function
// The three `call` sites we redirect, as offsets from the start of the signature.
// Measured on 1.11.240: sig 0x1406DF43E - GetFile 0x1406DF424 - (a) 0x1406DF439 - (c) 0x1406DF452
static const ptrdiff_t kOffGetFile = -0x1A;
static const ptrdiff_t kOffCallA   = -5;
static const ptrdiff_t kOffCallC   = 0x14;
static const unsigned char kPredProlog[] = { 0x48,0x89,0x5C,0x24,0x08 };   // mov [rsp+8], rbx

// (2) The fragment of the FaceGeom LOADER where it builds the path and asks whether it exists.
// The two functions we need are read from here, without hardcoding their addresses:
//   48 8D 55 F0                lea rdx,[rbp-0x10]     ; output buffer
//   49 8B F8                   mov rdi,r8
//   E8 ?? ?? ?? ??             call BuildPath(npc, buf)
//   84 C0 0F 84 ?? ?? ?? ??    test al,al / je
//   48 8D 4D F0                lea rcx,[rbp-0x10]
//   E8 ?? ?? ?? ??             call Exists(path)
static const unsigned char kSigLoadB[] = {
    0x48,0x8D,0x55,0xF0, 0x49,0x8B,0xF8, 0xE8,0,0,0,0,
    0x84,0xC0, 0x0F,0x84,0,0,0,0,
    0x48,0x8D,0x4D,0xF0, 0xE8,0,0,0,0 };
static const bool kSigLoadF[] = {
    1,1,1,1, 1,1,1, 1,0,0,0,0,
    1,1, 1,1,0,0,0,0,
    1,1,1,1, 1,0,0,0,0 };
static const ptrdiff_t kOffCallBuildPath = 7;        // from the start of the signature
static const ptrdiff_t kOffCallExists    = 24;

struct Range { unsigned char* start; size_t len; };

static bool GetTextRange(Range& out)
{
    auto base = (unsigned char*)GetModuleHandleW(nullptr);
    if (!base) return false;
    auto dos = (IMAGE_DOS_HEADER*)base;
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
    auto nt = (IMAGE_NT_HEADERS64*)(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return false;
    auto sec = IMAGE_FIRST_SECTION(nt);
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++sec)
        if (memcmp(sec->Name, ".text", 5) == 0)
        {
            out.start = base + sec->VirtualAddress;
            out.len   = sec->Misc.VirtualSize;
            return true;
        }
    return false;
}

// STOP - IT DEMANDS EXACTLY ONE MATCH. With two we do not know which one it is, and guessing
// here means writing over engine code at random.
static unsigned char* FindUnique(const Range& r, const Signature& f, const char* who)
{
    unsigned char* found = nullptr;
    int n = 0;
    for (size_t i = 0; i + f.n <= r.len; ++i)
    {
        bool ok = true;
        for (size_t j = 0; j < f.n; ++j)
            if (f.fixed[j] && r.start[i + j] != f.bytes[j]) { ok = false; break; }
        if (!ok) continue;
        if (++n > 1) { Err("[sig:%s] %d matches; expected 1. Not hooking.", who, n); return nullptr; }
        found = r.start + i;
    }
    if (!found) Err("[sig:%s] not present in .text. Unsupported game version. Not hooking.", who);
    return found;
}

// Reads the target of a `call rel32`. nullptr if the byte is not E8.
static void* CallTarget(unsigned char* site, const char* who)
{
    if (*site != 0xE8) { Err("[call:%s] 0x%p is not E8 (byte %02X). Not hooking.", who, site, *site); return nullptr; }
    INT32 d = 0;
    memcpy(&d, site + 1, 4);
    return site + 5 + d;
}

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

// Same file and SAME LOCK as the errors.
static void Trace(const char* fmt, ...)
{
    AcquireSRWLockExclusive(&g_logLock);
    if (!g_log)
    {
        if (g_logPath.empty() || (g_log = _fsopen(g_logPath.c_str(), "w", _SH_DENYWR)) == nullptr)
        { ReleaseSRWLockExclusive(&g_logLock); return; }
    }
    va_list a;
    va_start(a, fmt);
    vfprintf(g_log, fmt, a);
    va_end(a);
    fputc(0x0A, g_log);
    fflush(g_log);
    ReleaseSRWLockExclusive(&g_logLock);
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
// Installing the redirection
// ============================================================================================
// Our DLL lives more than 2 GB away from the exe, so a 5-byte `jmp rel32` cannot reach it.
// A page is reserved NEAR the target holding 16-byte stubs that jump absolute.
static unsigned char* AllocNear(unsigned char* nearAddr)
{
    SYSTEM_INFO si{};
    GetSystemInfo(&si);
    const size_t gran = si.dwAllocationGranularity;
    auto base = (uintptr_t)nearAddr & ~(uintptr_t)(gran - 1);
    for (size_t step = gran; step < 0x60000000ull; step += gran)
        for (int dir = 0; dir < 2; ++dir)
        {
            uintptr_t dst = dir ? base + step : base - step;
            if (dst < 0x10000) continue;
            if (auto p = (unsigned char*)VirtualAlloc((void*)dst, gran,
                    MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE))
                return p;
        }
    return nullptr;
}

// Hands out 16-byte stubs from ONE SINGLE page.
//
// STOP - It used to reserve a whole page (64 KB) PER HOOK: 192 KB of address space for 42
// useful bytes. The three sites we hook are less than 0x40 bytes apart, so one page near the
// first is in range of all three. `RedirectCall` verifies reach anyway before writing, so if
// they ever stopped being adjacent the one out of range is reported and not hooked, instead of
// jumping somewhere arbitrary.
static unsigned char* g_page     = nullptr;
static size_t         g_pageUsed = 0;
static size_t         g_pageSize = 0;

static unsigned char* StubNear(unsigned char* nearAddr)
{
    if (!g_page)
    {
        SYSTEM_INFO si{};
        GetSystemInfo(&si);
        g_pageSize = si.dwAllocationGranularity;
        g_page     = AllocNear(nearAddr);
        g_pageUsed = 0;
    }
    if (!g_page) return nullptr;
    if (g_pageUsed + 16 > g_pageSize) return nullptr;   // cannot happen with 3 hooks
    unsigned char* s = g_page + g_pageUsed;
    g_pageUsed += 16;
    return s;
}

// Redirects ONE `call rel32` to a function of ours and returns its original target.
//
// No function prologue is touched and no trampoline is needed: the disp32 of the `E8` is
// rewritten at the CALL SITE, so the other callers of those functions -and there are some,
// they are generic engine utilities- notice nothing.
//
// The `E8` is a 32-bit relative and our DLL lives more than 2 GB from the exe, so it cannot
// reach. Hence the call points at a 14-byte stub reserved NEARBY, and the stub jumps absolute.
static void* RedirectCall(unsigned char* site, void* ours, const char* who)
{
    if (*site != 0xE8)
    { Err("[hook:%s] 0x%p is not an E8 call (byte %02X). Not hooking.", who, site, *site); return nullptr; }

    INT32 disp = 0;
    memcpy(&disp, site + 1, 4);
    void* original = site + 5 + disp;

    unsigned char* stub = StubNear(site);
    if (!stub)
    { Err("[hook:%s] could not get memory within +-2GB of 0x%p. Not hooking.", who, site); return nullptr; }

    stub[0] = 0xFF; stub[1] = 0x25;                     // jmp qword ptr [rip+0]
    memset(stub + 2, 0, 4);
    memcpy(stub + 6, &ours, 8);

    INT64 rel = (INT64)stub - (INT64)(site + 5);
    if (rel > INT32_MAX || rel < INT32_MIN)
    { Err("[hook:%s] the stub ended up out of reach of 0x%p. Not hooking.", who, site); return nullptr; }

    DWORD old = 0;
    if (!VirtualProtect(site + 1, 4, PAGE_EXECUTE_READWRITE, &old))
    { Err("[hook:%s] VirtualProtect failed at 0x%p (err %lu). Not hooking.", who, site, GetLastError()); return nullptr; }
    INT32 d32 = (INT32)rel;
    memcpy(site + 1, &d32, 4);
    VirtualProtect(site + 1, 4, old, &old);
    FlushInstructionCache(GetCurrentProcess(), site, 5);
    return original;
}

// ============================================================================================
static void Install()
{
    Range text{};
    if (!GetTextRange(text)) { Err("[init] could not locate .text of the executable. Not hooking."); return; }

    // (2) first: if we have no way to ask about the file, hooking makes no sense.
    Signature sigLoad{ kSigLoadB, kSigLoadF, sizeof kSigLoadB };
    unsigned char* sl = FindUnique(text, sigLoad, "loader");
    if (!sl) return;
    g_buildPath = (FnBuildPath)CallTarget(sl + kOffCallBuildPath, "build-path");
    g_exists    = (FnExists)   CallTarget(sl + kOffCallExists,    "exists");
    if (!g_buildPath || !g_exists) return;

    // (1) the predicate
    Signature sigPred{ kSigPredB, kSigPredF, sizeof kSigPredB };
    unsigned char* sp = FindUnique(text, sigPred, "predicate");
    if (!sp) return;

    unsigned char* fn = sp + kPredFromSig;
    if (fn < text.start || fn + sizeof kPredProlog > text.start + text.len ||
        memcmp(fn, kPredProlog, sizeof kPredProlog) != 0)
    {
        Err("[guard] the prologue at 0x%p is not the expected one. The signature matched but the function did not. Not hooking.", fn);
        return;
    }

    // The three sites, at fixed offsets from the signature. Verified against the 1.11.240
    // disassembly: signature at 0x1406DF43E, GetFile at 0x1406DF424 (-0x1A), (a) at
    // 0x1406DF439 (-5) and (c) at 0x1406DF452 (+0x14).
    // STOP - The ORDER matters: GetFile first. If that one hooks and one of the others fails,
    // the plugin changes no decision (recording the NPC does nothing on its own); the other
    // way round, (a) would consult a t_rootNpc nobody fills.
    void* gf = RedirectCall(sp + kOffGetFile, (void*)&HookGetFile, "getfile");
    if (!gf) return;
    g_origGetFile = (FnGetFile)gf;

    void* a = RedirectCall(sp + kOffCallA, (void*)&HookA, "master-check");
    if (!a) return;
    g_origA = (FnIsMaster)a;

    void* c = RedirectCall(sp + kOffCallC, (void*)&HookC, "name-list");
    if (!c) { Err("[hook] (a) got hooked and (c) did not: the gate is left half done."); return; }
    g_origC = (FnNameInList)c;
}

extern "C" __declspec(dllexport) bool F4SEPlugin_Load(const void* /*f4se*/)
{
    ResolveLogPath();
    Install();
#if TRACE
    if (g_origC)
        Trace("[trace] hooked. MEASUREMENT build: always writes the log (TRACE=1).");
#endif
    return true;        // we never return false: we do not block the game over this
}

BOOL APIENTRY DllMain(HMODULE, DWORD, LPVOID) { return TRUE; }
