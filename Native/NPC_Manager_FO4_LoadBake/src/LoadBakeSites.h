// LoadBakeSites.h - WHERE NPC_Manager_FO4_LoadBake hooks: the resolution of its two signatures into the
// engine functions it reuses and the three call sites it redirects. PURE: it reads the image and writes
// nothing, so the plugin (in the game, base = the executable) and a probe (base = Fallout4.exe mapped from
// disk) run THIS SAME code and must resolve the same addresses.
//
// Split out of NPC_Manager_FO4_LoadBake.cpp when the scanner moved to FO4_Base_Library\Native\
// F4sePluginKit.h (SafeScrap phase D, 26-sep). The signatures and offsets are LoadBake's, unchanged.
#pragma once

#include "F4sePluginKit.h"

namespace loadbake
{

// Both are MEASURED against Fallout4.exe 1.11.240 and each appears EXACTLY ONCE in the 36 MB of .text.
// The wildcards fall on call/jcc displacements, which is precisely what changes between builds.

// (1) The heart of the predicate. Used to locate the function (its start is 0x9E earlier).
//   84 C0 74 26                test al,al / je FALSE        <- result of (a)
//   48 8B CB E8 ?? ?? ?? ??    mov rcx,rbx / call (b)
//   84 C0 74 0D                test/je -> TRUE
//   48 8D 4B 70 E8 ?? ?? ?? ?? lea rcx,[rbx+0x70] / call (c)   <- the plugin NAME
//   84 C0 74 0D B0 01          test/je -> FALSE ; mov al,1 -> TRUE
inline const unsigned char kSigPredB[] = {
    0x84,0xC0, 0x74,0x26, 0x48,0x8B,0xCB, 0xE8,0,0,0,0,
    0x84,0xC0, 0x74,0x0D, 0x48,0x8D,0x4B,0x70, 0xE8,0,0,0,0,
    0x84,0xC0, 0x74,0x0D, 0xB0,0x01 };
inline const bool kSigPredF[] = {
    1,1, 1,1, 1,1,1, 1,0,0,0,0,
    1,1, 1,1, 1,1,1,1, 1,0,0,0,0,
    1,1, 1,1, 1,1 };
constexpr ptrdiff_t kPredFromSig = -0x9E;         // start of the function
// The three `call` sites we redirect, as offsets from the start of the signature.
// Measured on 1.11.240: sig 0x1406DF43E - GetFile 0x1406DF424 - (a) 0x1406DF439 - (c) 0x1406DF452
constexpr ptrdiff_t kOffGetFile = -0x1A;
constexpr ptrdiff_t kOffCallA   = -5;
constexpr ptrdiff_t kOffCallC   = 0x14;
inline const unsigned char kPredProlog[] = { 0x48,0x89,0x5C,0x24,0x08 };   // mov [rsp+8], rbx

// (2) The fragment of the FaceGeom LOADER where it builds the path and asks whether it exists.
// The two functions we need are read from here, without hardcoding their addresses:
//   48 8D 55 F0                lea rdx,[rbp-0x10]     ; output buffer
//   49 8B F8                   mov rdi,r8
//   E8 ?? ?? ?? ??             call BuildPath(npc, buf)
//   84 C0 0F 84 ?? ?? ?? ??    test al,al / je
//   48 8D 4D F0                lea rcx,[rbp-0x10]
//   E8 ?? ?? ?? ??             call Exists(path)
inline const unsigned char kSigLoadB[] = {
    0x48,0x8D,0x55,0xF0, 0x49,0x8B,0xF8, 0xE8,0,0,0,0,
    0x84,0xC0, 0x0F,0x84,0,0,0,0,
    0x48,0x8D,0x4D,0xF0, 0xE8,0,0,0,0 };
inline const bool kSigLoadF[] = {
    1,1,1,1, 1,1,1, 1,0,0,0,0,
    1,1, 1,1,0,0,0,0,
    1,1,1,1, 1,0,0,0,0 };
constexpr ptrdiff_t kOffCallBuildPath = 7;        // from the start of the signature
constexpr ptrdiff_t kOffCallExists    = 24;

// What the resolution yields. Nothing here is written by the resolution.
struct Sites
{
    void*          buildPath = nullptr;   // 0x140658E60 in 1.11.240
    void*          exists    = nullptr;   // 0x1402FC0C0 in 1.11.240
    unsigned char* predicate = nullptr;   // 0x1406DF3A0 in 1.11.240 (verified by its prologue)
    unsigned char* siteGetFile = nullptr; // 0x1406DF424
    unsigned char* siteA       = nullptr; // 0x1406DF439
    unsigned char* siteC       = nullptr; // 0x1406DF452
};

// Resolves every address from the image at `base`. False (and an error in the log) if anything does not
// line up: a signature missing or duplicated, or a prologue that is not the expected one.
inline bool Resolve(unsigned char* base, Sites& out)
{
    f4kit::Range text{};
    if (!f4kit::GetTextRange(base, text)) { f4kit::Err("[init] could not locate .text of the executable. Not hooking."); return false; }

    // (2) first: if we have no way to ask about the file, hooking makes no sense.
    f4kit::Signature sigLoad{ kSigLoadB, kSigLoadF, sizeof kSigLoadB };
    unsigned char* sl = f4kit::FindUnique(text, sigLoad, "loader");
    if (!sl) return false;
    out.buildPath = f4kit::CallTarget(sl + kOffCallBuildPath, "build-path");
    out.exists    = f4kit::CallTarget(sl + kOffCallExists,    "exists");
    if (!out.buildPath || !out.exists) return false;

    // (1) the predicate
    f4kit::Signature sigPred{ kSigPredB, kSigPredF, sizeof kSigPredB };
    unsigned char* sp = f4kit::FindUnique(text, sigPred, "predicate");
    if (!sp) return false;

    unsigned char* fn = sp + kPredFromSig;
    if (fn < text.start || fn + sizeof kPredProlog > text.start + text.len ||
        memcmp(fn, kPredProlog, sizeof kPredProlog) != 0)
    {
        f4kit::Err("[guard] the prologue at 0x%p is not the expected one. The signature matched but the function did not. Not hooking.", fn);
        return false;
    }
    out.predicate = fn;

    // The three sites, at fixed offsets from the signature. Verified against the 1.11.240 disassembly:
    // signature at 0x1406DF43E, GetFile at 0x1406DF424 (-0x1A), (a) at 0x1406DF439 (-5) and (c) at
    // 0x1406DF452 (+0x14).
    out.siteGetFile = sp + kOffGetFile;
    out.siteA       = sp + kOffCallA;
    out.siteC       = sp + kOffCallC;
    return true;
}

} // namespace loadbake
