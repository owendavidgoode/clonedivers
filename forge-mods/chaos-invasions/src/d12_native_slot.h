/* Genuine 12th Difficulty Slot ("LEVEL 12: CLONE WARS") Installer for Build 25480438 (HellForge 0.4.0 / Engine 0.1.47 / SDK 16).
 *
 * Extends HellForge's moved 15-row difficulty table (g_difficultyTable) in place with a true 12th carousel slot
 * right after Difficulty 11 ("HELL FORGE"), leaving Difficulty 1..11 completely intact:
 *   1. Registers unlock item 0xCD12C10E (copyOf Super Helldive 0xA0AE653A, level 12) in <logs_folder>\registered-items.json
 *      before client login so RegisteredItems.cs grants Difficulty 12 automatically and raises online cap 48726272 -> 12.
 *   2. Allocates near-game RW/RX pages for Difficulty 12's ExtraWidget bar slots (index 11), 12-entry band sprite &
 *      structured-map distance tables, extended 10-element array clamps (0x5E4FAF, 0x5E503F, 0x17C6AA3), and collision-free
 *      Galactic War mission-board slot routing (slots 90..98 on <=6-region planets; level-2 city/megafactory slots on 7..8-region worlds).
 *   3. Populates row 12 (row11 + 0x330) with tech level 11 (compatible with all 6 enemy rosters & 319 compositions) and
 *      Clone Wars theater parameters, widens objective/operation/mission max-difficulty ranges 11 -> 12, flips HellForge's
 *      `== 11` map/icon/countdown/selection/radar/grant stub checks to `>= 11`, and widens all 14 kDifficultyBounds 11 -> 12.
 */
#ifndef CHAOS_D12_NATIVE_SLOT_H
#define CHAOS_D12_NATIVE_SLOT_H

#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <windows.h>
#include "hellforge_mod.h"
#include "hellforge_content.h"
#include "ultrakill_d12.h"

#define D12_UNLOCK_ITEM_ID      0xCD12C10Eu /* 3440558350 */
#define D12_SUPER_HELLDIVE_ITEM 0xA0AE653Au /* 2695783738 */
#define D12_NAME_TEXT_ID        0xCD120001u
#define D12_NUM_TEXT_ID         0xCD120002u
#define D12_BOARD_SKIP          0xFFFFFFFFu

#define D12_ROW_BYTES           0x330u
#define D12_WIDGET_BYTES        0x910u
#define D12_BAR_SLOTS           32u

#define D12_BAND_SITE_RVA       0x19A297Cu
#define D12_BAR_SITE0_RVA       0x19D004Au
#define D12_BOARD_REG_MAX_RVA   0x11E46F1u
#define D12_BOARD_REG_SITE_RVA  0x11E4744u
#define D12_BOARD_REG_BACK_RVA  0x11E474Au
#define D12_BOARD_REG_SKIP_RVA  0x11E4821u
#define D12_BOARD_PLN_SITE_RVA  0x11E3E67u
#define D12_BOARD_PLN_BACK_RVA  0x11E3E6Eu
#define D12_BOARD_PLN_SKIP_RVA  0x11E4002u
#define D12_BOARD_DEF_SITE_RVA  0x11E4287u
#define D12_BOARD_DEF_BACK_RVA  0x11E428Eu
#define D12_BOARD_DEF_SKIP_RVA  0x11E4429u

#define D12_OBJECTIVES_RVA      0x32EF870u
#define D12_OBJECTIVE_ROWS      153u
#define D12_OBJECTIVE_STRIDE    0xA0u
#define D12_OPERATIONS_RVA      0x32FEF10u
#define D12_OPERATION_ROWS      25u
#define D12_OPERATION_STRIDE    0x490u
#define D12_MISSIONS_RVA        0x3773420u
#define D12_MISSION_ROWS        162u
#define D12_MISSION_STRIDE      0x380u

#define D12_CONFIG_RVA          0x347CDF8u
#define D12_UI_RVA              0x347CE28u
#define D12_SELECTION_END_RVA   0x175784Fu
#define D12_GRANT_SITE_RVA      0x6AE35Du
#define D12_RADAR_STATE_RVA     0x5DF35Bu
#define D12_SHELL_WEIGHT_RVA    0x9F342Fu

#if defined(__GNUC__) || defined(__clang__)
#define D12_MS_ABI __attribute__((ms_abi))
#else
#define D12_MS_ABI
#endif

typedef struct D12BoundSite {
    uint32_t rva;
    uint8_t imm_at;
    uint8_t width;
    uint8_t prefix[2];
} D12BoundSite;

static const D12BoundSite kD12DifficultyBounds[14] = {
    {0x11EBB4Au, 1u, 4u, {0xBBu, 0x00u}},
    {0x11EBC0Du, 2u, 1u, {0x83u, 0xF8u}},
    {0x11EBC7Cu, 2u, 1u, {0x83u, 0xFBu}},
    {0x11EBCCDu, 2u, 1u, {0x83u, 0xF8u}},
    {0x11EBEFDu, 2u, 1u, {0x83u, 0xFBu}},
    {0x11EBF33u, 2u, 4u, {0x41u, 0xB9u}},
    {0x1372B13u, 2u, 4u, {0x41u, 0xB9u}},
    {0x136E9CDu, 2u, 1u, {0x83u, 0xFAu}},
    {0x136F37Fu, 2u, 1u, {0x83u, 0xFBu}},
    {0x1799940u, 2u, 1u, {0x83u, 0xF8u}},
    {0x179BB9Eu, 2u, 1u, {0x83u, 0xFAu}},
    {0x179C9C3u, 2u, 1u, {0x83u, 0xF8u}},
    {0x179CA4Du, 2u, 1u, {0x83u, 0xFFu}},
    {0x179D710u, 2u, 1u, {0x83u, 0xFAu}},
};

typedef struct D12DistanceSite {
    uint32_t rva;
    uint8_t disp_at;
    uint8_t prefix[6];
} D12DistanceSite;

static const D12DistanceSite kD12DistanceSites[7] = {
    {0xF417C8u, 5u, {0xF3u, 0x0Fu, 0x10u, 0x84u, 0x8Bu, 0x00u}},
    {0x7BDEE8u, 4u, {0x41u, 0x8Bu, 0x84u, 0x89u, 0x00u, 0x00u}},
    {0xF62EDBu, 4u, {0x41u, 0x8Bu, 0x84u, 0x89u, 0x00u, 0x00u}},
    {0xF7D9B6u, 6u, {0xF3u, 0x41u, 0x0Fu, 0x10u, 0x84u, 0x89u}},
    {0xF7E506u, 6u, {0xF3u, 0x41u, 0x0Fu, 0x10u, 0x84u, 0x89u}},
    {0xF82CF6u, 6u, {0xF3u, 0x41u, 0x0Fu, 0x10u, 0x84u, 0x89u}},
    {0xF84BA6u, 6u, {0xF3u, 0x41u, 0x0Fu, 0x10u, 0x84u, 0x89u}},
};

static const uint8_t kD12ExtraWidgetTemplate[63] = {
    0x52, 0x41, 0x50,                               /*  0 push rdx; push r8 */
    0x4C, 0x8D, 0x05, 0, 0, 0, 0,                   /*  3 lea r8, [rip + bars] (rel32 at 6) */
    0x31, 0xC0,                                     /* 10 xor eax, eax */
    0x49, 0x8B, 0x14, 0xC0,                         /* 12 mov rdx, [r8 + rax*8] */
    0x48, 0x39, 0xCA,                               /* 16 cmp rdx, rcx */
    0x74, 0x15,                                     /* 19 je found (42) */
    0x48, 0x85, 0xD2,                               /* 21 test rdx, rdx */
    0x74, 0x0C,                                     /* 24 je claim (38) */
    0xFF, 0xC0,                                     /* 26 inc eax */
    0x83, 0xF8, D12_BAR_SLOTS,                      /* 28 cmp eax, 32 */
    0x72, 0xEB,                                     /* 31 jb 12 */
    0xB8, D12_BAR_SLOTS - 1u, 0x00, 0x00, 0x00,     /* 33 mov eax, 31 */
    0x49, 0x89, 0x0C, 0xC0,                         /* 38 claim: mov [r8 + rax*8], rcx */
    0x48, 0x69, 0xC0, 0x10, 0x09, 0x00, 0x00,       /* 42 found: imul rax, rax, 0x910 */
    0x4C, 0x8D, 0x05, 0, 0, 0, 0,                   /* 49 lea r8, [rip + widgets] (rel32 at 52) */
    0x4C, 0x01, 0xC0,                               /* 56 add rax, r8 */
    0x41, 0x58, 0x5A, 0xC3                          /* 59 pop r8; pop rdx; ret */
};

static const uint8_t kD12HfInPlace16Pin[16] = {
    0x0F, 0xB6, 0x80, 0xF9, 0x00, 0x00, 0x00,
    0x3C, 0x0B, 0x83, 0xD0, 0xFE, 0x41, 0x0F, 0x48, 0xC3
};

static const uint8_t kD12HfInPlace12Pin[12] = {
    0x8B, 0x40, 0x3C, 0x3C, 0x0B, 0x83, 0xD0, 0xFE, 0x41, 0x0F, 0x48, 0xC7
};

typedef struct D12NativeSlotState {
    int state;             /* 0 = waiting for D11, 1 = installed, -1 = refused */
    int item_registered;   /* 1 = registered-items.json verified containing mixId 3440558350 */
    int online_cap_raised; /* 1 = online override 48726272 raised to 12 */
    DWORD reg_known_bytes; /* verified byte length of registered-items.json */
    FILETIME reg_known_mtime; /* verified LastWriteTime of registered-items.json */
    DWORD reg_known_vol;      /* verified volume serial of registered-items.json */
    DWORD reg_known_idx_hi;   /* verified file index high of registered-items.json */
    DWORD reg_known_idx_lo;   /* verified file index low of registered-items.json */
    uint8_t* rw_mem;
    uint8_t* rx_mem;
    HfDifficultySettings* row12;
    uint8_t row12_baseline[D12_ROW_BYTES];
} D12NativeSlotState;

static inline int D12InRel32(int64_t delta)
{
    return delta >= -2147483647LL - 1LL && delta <= 2147483647LL;
}

static inline int D12SpanInRel32(uintptr_t block, size_t block_bytes, uintptr_t game, size_t game_bytes)
{
    int64_t d0 = (int64_t)block - (int64_t)game;
    int64_t d1 = (int64_t)(block + block_bytes) - (int64_t)game;
    int64_t d2 = (int64_t)block - (int64_t)(game + game_bytes);
    int64_t d3 = (int64_t)(block + block_bytes) - (int64_t)(game + game_bytes);
    return D12InRel32(d0) && D12InRel32(d1) && D12InRel32(d2) && D12InRel32(d3);
}

static inline uint8_t* D12AllocNearGame(uintptr_t game, size_t game_size, size_t bytes, uintptr_t peer_anchor)
{
    if (!game || !bytes) return NULL;
    if (game_size < 0x4000000u) game_size = 0x4800000u;

    /* Search above game.dll first */
    uintptr_t hi_limit = game + 0x7FFF0000ull;
    for (uintptr_t at = (game + game_size + 0xFFFFu) & ~(uintptr_t)0xFFFFu;
         at + bytes < hi_limit;
         at += 0x10000u) {
        MEMORY_BASIC_INFORMATION mbi;
        if (!VirtualQuery((const void*)at, &mbi, sizeof mbi)) break;
        if (mbi.State != MEM_FREE) {
            uintptr_t next = (((uintptr_t)mbi.BaseAddress + mbi.RegionSize + 0xFFFFu) & ~(uintptr_t)0xFFFFu);
            if (next > at) at = next - 0x10000u;
            continue;
        }
        if (!D12SpanInRel32(at, bytes, game, game_size)) continue;
        if (peer_anchor && !D12InRel32((int64_t)at - (int64_t)peer_anchor)) continue;
        void* p = VirtualAlloc((void*)at, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (p) return (uint8_t*)p;
    }

    /* Search below game.dll */
    uintptr_t lo_limit = (game + game_size > 0x7FFF0000ull + 0x10000ull)
                         ? (game + game_size - 0x7FFF0000ull)
                         : 0x10000ull;
    for (uintptr_t at = (game - bytes) & ~(uintptr_t)0xFFFFu;
         at >= lo_limit && at < game;) {
        MEMORY_BASIC_INFORMATION mbi;
        if (!VirtualQuery((const void*)at, &mbi, sizeof mbi)) break;
        if (mbi.State == MEM_FREE) {
            if (D12SpanInRel32(at, bytes, game, game_size) &&
                (!peer_anchor || D12InRel32((int64_t)at - (int64_t)peer_anchor))) {
                void* p = VirtualAlloc((void*)at, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (p) return (uint8_t*)p;
            }
            if (at < lo_limit + 0x10000u) break;
            at -= 0x10000u;
            continue;
        }
        uintptr_t base = mbi.AllocationBase ? (uintptr_t)mbi.AllocationBase : (uintptr_t)mbi.BaseAddress;
        if (base < lo_limit + bytes) break;
        at = (base - bytes) & ~(uintptr_t)0xFFFFu;
    }
    return NULL;
}

#ifdef CHAOS_UNIT_TEST
typedef void (*D12TestRegistryHookFn)(const char* logs_folder_utf8, int stage);
static D12TestRegistryHookFn g_d12_test_registry_hook = NULL;
static int g_d12_test_force_posix_unsupported = 0;
#endif

static inline int D12QueryPathHandleInfo(const wchar_t* wpath, BY_HANDLE_FILE_INFORMATION* out_info)
{
    if (!wpath || !out_info) return 0;
    HANDLE h = CreateFileW(wpath, FILE_READ_ATTRIBUTES,
                           FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                           NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, NULL);
    if (h == INVALID_HANDLE_VALUE) return 0;
    BOOL ok = GetFileInformationByHandle(h, out_info);
    CloseHandle(h);
    if (!ok ||
        (out_info->dwFileAttributes & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY)) != 0 ||
        out_info->nFileSizeHigh != 0) {
        return 0;
    }
    return 1;
}

static inline int D12SameFileInfo(const BY_HANDLE_FILE_INFORMATION* a, const BY_HANDLE_FILE_INFORMATION* b)
{
    return a->dwVolumeSerialNumber == b->dwVolumeSerialNumber &&
           a->nFileIndexHigh == b->nFileIndexHigh &&
           a->nFileIndexLow == b->nFileIndexLow &&
           a->nFileSizeHigh == 0u && b->nFileSizeHigh == 0u &&
           a->nFileSizeLow == b->nFileSizeLow &&
           CompareFileTime(&a->ftLastWriteTime, &b->ftLastWriteTime) == 0;
}

static inline BOOL D12ReplaceFilePosixAtomic(const wchar_t* wtmp, const wchar_t* wpath)
{
    typedef struct D12RenameInfoEx {
        DWORD Flags;
        HANDLE RootDirectory;
        DWORD FileNameLength;
        WCHAR FileName[MAX_PATH];
    } D12RenameInfoEx;

#ifdef CHAOS_UNIT_TEST
    if (g_d12_test_force_posix_unsupported) {
        SetLastError(ERROR_NOT_SUPPORTED);
        return FALSE;
    }
#endif

    HANDLE hRen = CreateFileW(wtmp, DELETE | SYNCHRONIZE,
                              FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                              NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hRen == INVALID_HANDLE_VALUE) return FALSE;

    D12RenameInfoEx ri;
    memset(&ri, 0, sizeof ri);
    ri.Flags = 0x00000001u | 0x00000002u; /* FILE_RENAME_REPLACE_IF_EXISTS | FILE_RENAME_POSIX_SEMANTICS */
    ri.RootDirectory = NULL;
    size_t wlen = wcslen(wpath);
    if (wlen == 0u || wlen >= MAX_PATH) {
        CloseHandle(hRen);
        SetLastError(ERROR_FILENAME_EXCED_RANGE);
        return FALSE;
    }
    ri.FileNameLength = (DWORD)(wlen * sizeof(WCHAR));
    memcpy(ri.FileName, wpath, (wlen + 1u) * sizeof(WCHAR));
    /* FileRenameInfoEx == 22: unlinks wpath immediately on NTFS while hIn stays open!
     * Fail closed if POSIX semantics are unavailable so hIn is NEVER closed prior to replacement. */
    BOOL ok = SetFileInformationByHandle(hRen, (FILE_INFO_BY_HANDLE_CLASS)22, &ri, sizeof(ri));
    DWORD rerr = ok ? ERROR_SUCCESS : GetLastError();
    CloseHandle(hRen);
    if (!ok) SetLastError(rerr);
    return ok;
}

static inline int D12IsTransientOpenError(DWORD err)
{
    return err == ERROR_SHARING_VIOLATION ||
           err == ERROR_LOCK_VIOLATION ||
           err == ERROR_ACCESS_DENIED;
}

typedef struct D12RegistryLock {
    HANDLE hFile;
} D12RegistryLock;

static inline int D12AcquireRegistryLock(const wchar_t* wdir, D12RegistryLock* out_lock)
{
    if (!wdir || !out_lock) return 0;
    out_lock->hFile = INVALID_HANDLE_VALUE;
    wchar_t wlock[MAX_PATH];
    int n = swprintf(wlock, MAX_PATH, L"%ls\\registered-items.json.lock", wdir);
    if (n <= 0 || n >= MAX_PATH) return 0;

    HANDLE hf = CreateFileW(wlock, GENERIC_READ | GENERIC_WRITE,
                            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                            NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, NULL);
    if (hf == INVALID_HANDLE_VALUE) return 0;

    BY_HANDLE_FILE_INFORMATION linfo;
    if (!GetFileInformationByHandle(hf, &linfo) ||
        (linfo.dwFileAttributes & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY)) != 0) {
        CloseHandle(hf);
        return 0;
    }

    /* Non-recursive OS byte-range lock on registered-items.json.lock: serializes the complete
     * read -> merge -> temp-stage -> POSIX-rename critical section across all processes, threads,
     * and re-entrant handles without ever locking registered-items.json itself. */
    for (int attempt = 0; attempt < 2500; ++attempt) {
        OVERLAPPED ov;
        memset(&ov, 0, sizeof ov);
        if (LockFileEx(hf, LOCKFILE_EXCLUSIVE_LOCK | LOCKFILE_FAIL_IMMEDIATELY, 0u, 1u, 0u, &ov)) {
            out_lock->hFile = hf;
            return 1;
        }
        Sleep(2u);
    }
    CloseHandle(hf);
    return 0;
}

static inline void D12ReleaseRegistryLock(D12RegistryLock* lk)
{
    if (!lk || lk->hFile == INVALID_HANDLE_VALUE) return;
    OVERLAPPED ov;
    memset(&ov, 0, sizeof ov);
    UnlockFileEx(lk->hFile, 0u, 1u, 0u, &ov);
    CloseHandle(lk->hFile);
    lk->hFile = INVALID_HANDLE_VALUE;
}

static inline char* D12BuildSplicedRegistryJsonEntry(
    const char* existing,
    DWORD existing_len,
    const char* entry_json,
    DWORD* out_len_ptr)
{
    if (out_len_ptr) *out_len_ptr = 0u;
    if (!entry_json || !entry_json[0]) return NULL;
    size_t entry_len = strlen(entry_json);
    size_t out_cap = (size_t)existing_len + entry_len + 64u;
    char* out_buf = (char*)malloc(out_cap);
    if (!out_buf) return NULL;

    int out_len = 0;
    if (!existing) {
        out_len = snprintf(out_buf, out_cap, "{\"format\":1,\"items\":[%s]}\n", entry_json);
    } else {
        int close_br = -1;
        for (int i = (int)existing_len - 1; i >= 0; --i) {
            if (existing[i] == ']') { close_br = i; break; }
        }
        int open_br = -1;
        for (int i = 0; i < close_br; ++i) {
            if (existing[i] == '[') { open_br = i; break; }
        }
        if (open_br < 0 || close_br < 0) {
            free(out_buf);
            return NULL;
        }
        int has_items = 0;
        for (int i = open_br + 1; i < close_br; ++i) {
            char ch = existing[i];
            if (ch != ' ' && ch != '\t' && ch != '\r' && ch != '\n') { has_items = 1; break; }
        }
        memcpy(out_buf, existing, (size_t)close_br);
        out_len = close_br + snprintf(out_buf + close_br, out_cap - (size_t)close_br,
                                      "%s%s%s", has_items ? "," : "", entry_json, existing + close_br);
    }
    if (out_len <= 0 || (size_t)out_len >= out_cap) {
        free(out_buf);
        return NULL;
    }
    if (out_len_ptr) *out_len_ptr = (DWORD)out_len;
    return out_buf;
}

static inline char* D12BuildSplicedRegistryJson(const char* existing, DWORD existing_len, DWORD* out_len_ptr)
{
    static const char kD12EntryJson[] =
        "{\"kind\":\"difficulty\",\"mixId\":3440558350,\"copyOf\":2695783738,"
        "\"level\":12,\"mod\":\"clonedivers.chaos-invasions\",\"name\":\"clonedivers.chaos-invasions:clone_wars\"}";
    return D12BuildSplicedRegistryJsonEntry(existing, existing_len, kD12EntryJson, out_len_ptr);
}

static inline int D12ReadEntireHandle(HANDLE h, char** out_buf, DWORD* out_len, BY_HANDLE_FILE_INFORMATION* out_info)
{
    BY_HANDLE_FILE_INFORMATION info_a, info_b;
    if (!out_buf || !out_len || !GetFileInformationByHandle(h, &info_a)) return 0;
    if ((info_a.dwFileAttributes & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY)) != 0 ||
        info_a.nFileSizeHigh != 0u || info_a.nFileSizeLow > (4u << 20))
        return 0;
    if (SetFilePointer(h, 0, NULL, FILE_BEGIN) == INVALID_SET_FILE_POINTER) return 0;
    DWORD len = info_a.nFileSizeLow;
    char* buf = (char*)malloc((size_t)len + 1u);
    if (!buf) return 0;
    DWORD got = 0u;
    if (len > 0u && (!ReadFile(h, buf, len, &got, NULL) || got != len)) {
        free(buf);
        return 0;
    }
    buf[len] = '\0';
    if (!GetFileInformationByHandle(h, &info_b) || !D12SameFileInfo(&info_a, &info_b)) {
        free(buf);
        return 0;
    }
    *out_buf = buf;
    *out_len = len;
    if (out_info) *out_info = info_a;
    return 1;
}

/* Splices a registered item entry into <logs_folder>\registered-items.json atomically so
 * HellForge.LocalBackend's RegisteredItems.Load() grants level 12 to every Super Helldive player
 * and raises online max_difficulty override 48726272 -> 12.
 *
 * Writer architecture & concurrency guarantees (HellForge 0.4.0):
 *   1. All atomic-replacement registry writers serialize their COMPLETE read -> merge -> temp-stage ->
 *      `D12ReplaceFilePosixAtomic` -> post-verify transaction via a non-recursive OS byte-range lock
 *      (`LockFileEx`) on `<logs_folder>\registered-items.json.lock` (`D12AcquireRegistryLock`), so two
 *      atomic replacers can never interleave or clobber one another at any stage.
 *   2. HellForge's own native writer (`WriteRegisteredItems()` in `native/mod-loader/registry.c:326,365`)
 *      never renames over `registered-items.json`: it exclusively opens `registered-items.json` in-place via
 *      `CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS, ...)`. Keeping `hIn` open with
 *      `GENERIC_READ` and `FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE` never blocks
 *      `WriteRegisteredItems()`, while `FileRenameInfoEx` POSIX semantics unlinks the old inode without closing
 *      `hIn` so post-replace `D12ReadEntireHandle(hIn)` detects and re-merges any concurrent in-place write
 *      that occurred even one instruction prior to replacement.
 *   3. Distinguishes `ERROR_FILE_NOT_FOUND` (created via atomic `CREATE_NEW`) from transient share/open errors
 *      (failing closed without overwriting). */
static inline int D12RegisterRegistryItemLocked(
    const char* logs_folder_utf8,
    const wchar_t* wdir,
    const wchar_t* wpath,
    const char* mix_id_needle,
    const char* entry_json,
    int invoke_test_hooks,
    BY_HANDLE_FILE_INFORMATION* out_verified_info)
{
    wchar_t wtmp[MAX_PATH];
    swprintf(wtmp, MAX_PATH, L"%ls\\registered-items.json.d12.%lu.%lu.tmp",
             wdir, (unsigned long)GetCurrentProcessId(), (unsigned long)GetCurrentThreadId());

    int did_splice = 0;
    char* carry_buf = NULL;
    DWORD carry_len = 0u;
    BY_HANDLE_FILE_INFORMATION carry_target_info;
    memset(&carry_target_info, 0, sizeof carry_target_info);
    int have_carry = 0;

    for (int attempt = 0; attempt < 10; ++attempt) {
        HANDLE hIn = CreateFileW(wpath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                                 NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, NULL);
        if (hIn == INVALID_HANDLE_VALUE) {
            DWORD err = GetLastError();
            if (err != ERROR_FILE_NOT_FOUND) {
                free(carry_buf); carry_buf = NULL; have_carry = 0;
                if (D12IsTransientOpenError(err)) {
                    Sleep(2u);
                    continue;
                }
                return 0;
            }
            /* File is genuinely absent: create exclusively with CREATE_NEW so a racing creator is never overwritten. */
            DWORD init_len = 0u;
            char* init_buf = have_carry
                             ? D12BuildSplicedRegistryJsonEntry(carry_buf, carry_len, entry_json, &init_len)
                             : D12BuildSplicedRegistryJsonEntry(NULL, 0u, entry_json, &init_len);
            if (!init_buf) {
                free(carry_buf);
                return 0;
            }
            HANDLE hNew = CreateFileW(wpath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ,
                                      NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
            if (hNew == INVALID_HANDLE_VALUE) {
                DWORD new_err = GetLastError();
                free(init_buf);
                if (new_err == ERROR_FILE_EXISTS || new_err == ERROR_ALREADY_EXISTS || D12IsTransientOpenError(new_err)) {
                    Sleep(1u);
                    continue;
                }
                free(carry_buf);
                return 0;
            }
            DWORD written = 0u;
            BOOL wr_ok = WriteFile(hNew, init_buf, init_len, &written, NULL) && written == init_len;
            free(init_buf);
            CloseHandle(hNew);
            if (!wr_ok) {
                free(carry_buf);
                return 0;
            }
            free(carry_buf); carry_buf = NULL; have_carry = 0;
            did_splice = 1;
            continue;
        }

        char* disk_buf = NULL;
        DWORD disk_len = 0u;
        BY_HANDLE_FILE_INFORMATION bhfi_in;
        memset(&bhfi_in, 0, sizeof bhfi_in);
        if (!D12ReadEntireHandle(hIn, &disk_buf, &disk_len, &bhfi_in)) {
            CloseHandle(hIn);
            Sleep(2u);
            continue;
        }

        /* If a previous POSIX rename raced with an in-place WriteRegisteredItems() on the unlinked inode,
         * use that captured snapshot ONLY if wpath still points to the temp file we just moved into place. */
        const char* src_buf = disk_buf;
        DWORD src_len = disk_len;
        if (have_carry) {
            if (bhfi_in.dwVolumeSerialNumber == carry_target_info.dwVolumeSerialNumber &&
                bhfi_in.nFileIndexHigh == carry_target_info.nFileIndexHigh &&
                bhfi_in.nFileIndexLow == carry_target_info.nFileIndexLow &&
                bhfi_in.nFileSizeLow == carry_target_info.nFileSizeLow) {
                src_buf = carry_buf;
                src_len = carry_len;
            } else {
                free(carry_buf);
                carry_buf = NULL;
                have_carry = 0;
            }
        }

        if (!have_carry && strstr(src_buf, mix_id_needle) != NULL) {
            BY_HANDLE_FILE_INFORMATION bhfi_cur;
            int still_same = D12QueryPathHandleInfo(wpath, &bhfi_cur) && D12SameFileInfo(&bhfi_in, &bhfi_cur);
            free(disk_buf);
            CloseHandle(hIn);
            if (!still_same) continue;
            if (out_verified_info) *out_verified_info = bhfi_in;
            return did_splice ? 2 : 1;
        }

        /* An existing 0-byte or half-written file means WriteRegisteredItems() just truncated via CREATE_ALWAYS
         * and has not completed its WriteFile yet — wait and retry instead of clobbering! */
        DWORD out_len = 0u;
        char* out_buf = (src_len > 0u) ? D12BuildSplicedRegistryJsonEntry(src_buf, src_len, entry_json, &out_len) : NULL;
        if (!out_buf) {
            free(disk_buf);
            CloseHandle(hIn);
            Sleep(2u);
            continue;
        }

        HANDLE hOut = CreateFileW(wtmp, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        if (hOut == INVALID_HANDLE_VALUE) {
            free(out_buf);
            free(disk_buf);
            free(carry_buf);
            CloseHandle(hIn);
            return 0;
        }
        DWORD written = 0u;
        BOOL ok = WriteFile(hOut, out_buf, out_len, &written, NULL) && written == out_len;
        BY_HANDLE_FILE_INFORMATION bhfi_tmp;
        memset(&bhfi_tmp, 0, sizeof bhfi_tmp);
        BOOL have_tmp_info = ok && GetFileInformationByHandle(hOut, &bhfi_tmp);
        CloseHandle(hOut);
        free(out_buf);
        if (!ok || !have_tmp_info) {
            DeleteFileW(wtmp);
            free(disk_buf);
            free(carry_buf);
            CloseHandle(hIn);
            return 0;
        }

#ifdef CHAOS_UNIT_TEST
        if (invoke_test_hooks && g_d12_test_registry_hook) g_d12_test_registry_hook(logs_folder_utf8, 0);
        if (invoke_test_hooks && g_d12_test_registry_hook) g_d12_test_registry_hook(logs_folder_utf8, 1);
#else
        (void)logs_folder_utf8;
        (void)invoke_test_hooks;
#endif

        if (!D12ReplaceFilePosixAtomic(wtmp, wpath)) {
            DWORD merr = GetLastError();
            DeleteFileW(wtmp);
            free(disk_buf);
            CloseHandle(hIn);
            if (D12IsTransientOpenError(merr)) {
                Sleep(2u);
                continue;
            }
            free(carry_buf);
            return 0;
        }
        did_splice = 1;
        free(carry_buf);
        carry_buf = NULL;
        have_carry = 0;

        /* Post-replace check on still-open hIn (POSIX rename on NTFS): if WriteRegisteredItems() truncated &
         * rewrote hIn's inode in-place immediately before rename unlinked it, hIn STILL holds all of those new items! */
        char* post_buf = NULL;
        DWORD post_len = 0u;
        BY_HANDLE_FILE_INFORMATION bhfi_post;
        int post_ok = D12ReadEntireHandle(hIn, &post_buf, &post_len, &bhfi_post);
        CloseHandle(hIn);

        if (!post_ok || !D12SameFileInfo(&bhfi_in, &bhfi_post) ||
            post_len != disk_len || memcmp(post_buf, disk_buf, disk_len) != 0) {
            free(disk_buf);
            if (post_ok && post_len > 0u && strstr(post_buf, mix_id_needle) == NULL) {
                carry_buf = post_buf;
                carry_len = post_len;
                carry_target_info = bhfi_tmp;
                have_carry = 1;
            } else {
                free(post_buf);
            }
            continue;
        }
        free(post_buf);
        free(disk_buf);
        /* Loop back immediately to verify wpath in-handle and capture its exact handle info! */
    }
    free(carry_buf);
    return 0;
}

static inline int D12RegisterRegistryItemEx(
    const char* logs_folder_utf8,
    const char* mix_id_needle,
    const char* entry_json,
    int invoke_test_hooks,
    BY_HANDLE_FILE_INFORMATION* out_verified_info)
{
    if (!logs_folder_utf8 || !logs_folder_utf8[0] || !mix_id_needle || !entry_json) return 0;

    wchar_t wdir[MAX_PATH], wpath[MAX_PATH];
    int wn = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, logs_folder_utf8, -1, wdir, MAX_PATH - 64);
    if (wn <= 0 || wn >= MAX_PATH - 64) return 0;
    swprintf(wpath, MAX_PATH, L"%ls\\registered-items.json", wdir);

    DWORD attr = GetFileAttributesW(wdir);
    if (attr == INVALID_FILE_ATTRIBUTES ||
        (attr & FILE_ATTRIBUTE_DIRECTORY) == 0 ||
        (attr & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
        return 0;

    D12RegistryLock lk;
    if (!D12AcquireRegistryLock(wdir, &lk)) return 0;
    int rc = D12RegisterRegistryItemLocked(logs_folder_utf8, wdir, wpath, mix_id_needle, entry_json,
                                           invoke_test_hooks, out_verified_info);
    D12ReleaseRegistryLock(&lk);
    return rc;
}

static inline int D12RegisterUnlockItemEx(const char* logs_folder_utf8, BY_HANDLE_FILE_INFORMATION* out_verified_info)
{
    static const char kD12EntryJson[] =
        "{\"kind\":\"difficulty\",\"mixId\":3440558350,\"copyOf\":2695783738,"
        "\"level\":12,\"mod\":\"clonedivers.chaos-invasions\",\"name\":\"clonedivers.chaos-invasions:clone_wars\"}";
    return D12RegisterRegistryItemEx(logs_folder_utf8, "\"mixId\":3440558350", kD12EntryJson, 1, out_verified_info);
}

static inline int D12RegisterUnlockItemInFolder(const char* logs_folder_utf8)
{
    return D12RegisterUnlockItemEx(logs_folder_utf8, NULL);
}

/* Keeps <logs_folder>\registered-items.json synced across startup and any later HellForge
 * WriteRegisteredItems() rewrite (e.g. StratagemsInstalled after WatchStratagems completes).
 * Returns: 2 = newly spliced/restored, 1 = already present & verified, 0 = failed/unavailable. */
static inline int D12SyncUnlockItemInFolder(D12NativeSlotState* st, const char* logs_folder_utf8, int force_read)
{
    if (!st || !logs_folder_utf8 || !logs_folder_utf8[0]) return 0;

    wchar_t wdir[MAX_PATH], wpath[MAX_PATH];
    int wn = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, logs_folder_utf8, -1, wdir, MAX_PATH - 48);
    if (wn <= 0 || wn >= MAX_PATH - 48) return 0;
    swprintf(wpath, MAX_PATH, L"%ls\\registered-items.json", wdir);

    BY_HANDLE_FILE_INFORMATION cur_info;
    if (!force_read && st->item_registered &&
        D12QueryPathHandleInfo(wpath, &cur_info) &&
        cur_info.dwVolumeSerialNumber == st->reg_known_vol &&
        cur_info.nFileIndexHigh == st->reg_known_idx_hi &&
        cur_info.nFileIndexLow == st->reg_known_idx_lo &&
        cur_info.nFileSizeLow == st->reg_known_bytes &&
        CompareFileTime(&cur_info.ftLastWriteTime, &st->reg_known_mtime) == 0) {
        return 1;
    }

    BY_HANDLE_FILE_INFORMATION verified_info;
    memset(&verified_info, 0, sizeof verified_info);
    int rc = D12RegisterUnlockItemEx(logs_folder_utf8, &verified_info);
    if (rc <= 0) {
        st->item_registered = 0;
        return 0;
    }

    st->reg_known_bytes  = verified_info.nFileSizeLow;
    st->reg_known_mtime  = verified_info.ftLastWriteTime;
    st->reg_known_vol    = verified_info.dwVolumeSerialNumber;
    st->reg_known_idx_hi = verified_info.nFileIndexHigh;
    st->reg_known_idx_lo = verified_info.nFileIndexLow;
    st->item_registered  = 1;
    return rc;
}

/* Pure C mission-board slot router for Galactic War region (kind=0), planet campaign (kind=1),
 * and planet defence (kind=2) mission generators across all Helldivers 2 planet topologies. */
static inline uint32_t D12ComputeBoardSlot(
    uint32_t kind,
    uint32_t level,
    int32_t index,
    const uint8_t* war_regions,
    uint32_t region_count,
    uint32_t planet,
    uint32_t default_slot)
{
    if (level < 11u) return default_slot;

    int has_reg6 = 0;
    int has_reg7 = 0;
    if (war_regions && region_count > 0u) {
        uint32_t limit = region_count > 4096u ? 4096u : region_count;
        for (uint32_t i = 0; i < limit; ++i) {
            uint32_t p = 0u, reg = 0u;
            memcpy(&p, war_regions + (size_t)i * 0x18u + 0u, 4u);
            memcpy(&reg, war_regions + (size_t)i * 0x18u + 4u, 4u);
            if (p == planet) {
                if (reg >= 6u) has_reg6 = 1;
                if (reg >= 7u) { has_reg7 = 1; break; }
            }
        }
    }

    if (level == 11u) {
        /* Exact HellForge Difficulty 11 slot mapping (preserved 1:1) */
        if (kind == 0u) {
            if (index < 0 || index > 7) return D12_BOARD_SKIP;
            if (has_reg7) return 30u + 10u * (uint32_t)index;
            if (index > 6) return D12_BOARD_SKIP;
            return 103u + (uint32_t)index;
        }
        if (has_reg7 || index < 0 || index > 2) return D12_BOARD_SKIP;
        return 100u + (uint32_t)index;
    }

    if (level == 12u) {
        if (!has_reg6) {
            /* Standard <=6-region planets (regions 0..5): use unused Region 6 block (slots 90..98) */
            if (kind == 0u) {
                if (index < 0 || index > 5) return D12_BOARD_SKIP;
                return 93u + (uint32_t)index;
            }
            if (index < 0 || index > 2) return D12_BOARD_SKIP;
            return 90u + (uint32_t)index;
        }
        /* 7-region (Super Earth Mega Cities) or 8-region (Cyberstan Megafactories):
         * city/megafactory region r uses its level-2 slot (30 + 10*r + 1), keeping D11 at +0 and D3..D10 intact! */
        if (kind == 0u) {
            int max_r = has_reg7 ? 7 : 6;
            if (index < 0 || index > max_r) return D12_BOARD_SKIP;
            return 30u + 10u * (uint32_t)index + 1u;
        }
        return D12_BOARD_SKIP;
    }

    return D12_BOARD_SKIP;
}

static uint32_t D12_MS_ABI D12BoardSlotFromSite(
    uint32_t kind,
    uint32_t level,
    int32_t index,
    uintptr_t war_ptr,
    uint32_t planet,
    uint32_t default_slot)
{
    if (level < 11u) return default_slot;
    if (war_ptr < 0x10000u || war_ptr >= 0x7FFFFFFFFFFFull) {
        return D12ComputeBoardSlot(kind, level, index, NULL, 0u, planet, default_slot);
    }

    uintptr_t regions_addr = (kind == 0u) ? (war_ptr + 0x23018u) : (war_ptr - 0x23008u);
    uintptr_t count_addr   = (kind == 0u) ? (war_ptr + 0x26018u) : (war_ptr - 0x20008u);
    if (regions_addr < 0x10000u || count_addr < 0x10000u) {
        return D12ComputeBoardSlot(kind, level, index, NULL, 0u, planet, default_slot);
    }

    uint32_t count = *(const uint32_t*)count_addr;
    if (count > 4096u) count = 0u;
    const uint8_t* regions = count ? (const uint8_t*)regions_addr : NULL;
    return D12ComputeBoardSlot(kind, level, index, regions, count, planet, default_slot);
}

/* Emits 36-byte extended clamp stub for kLevelInPlace[1..2] (0x5E4FAF / 0x5E503F) or kLevelInPlace[3] (0x17C6AA3),
 * mapping level 0 -> zero_reg, 1..10 -> 0..9, and >=11 (both 11 and 12!) -> 9. */
static inline size_t D12EmitLevelInPlaceStub(uint8_t* out, int is_hud_target, uintptr_t stub_va, uintptr_t back_va)
{
    int32_t rel;
    if (!is_hud_target) {
        static const uint8_t kStub[36] = {
            0x0F, 0xB6, 0x80, 0xF9, 0x00, 0x00, 0x00, /*  0 movzx eax, byte [rax+0xF9] */
            0x85, 0xC0,                               /*  7 test eax, eax */
            0x74, 0x11,                               /*  9 jz +17 (28) */
            0x83, 0xF8, 0x0A,                         /* 11 cmp eax, 10 */
            0x76, 0x05,                               /* 14 jbe +5 (21) */
            0xB8, 0x0A, 0x00, 0x00, 0x00,             /* 16 mov eax, 10 */
            0xFF, 0xC8,                               /* 21 dec eax */
            0xE9, 0, 0, 0, 0,                         /* 23 jmp back (rel32 at 24) */
            0x41, 0x8B, 0xC3,                         /* 28 mov eax, r11d */
            0xE9, 0, 0, 0, 0                          /* 31 jmp back (rel32 at 32) */
        };
        memcpy(out, kStub, sizeof kStub);
        rel = (int32_t)((int64_t)back_va - (int64_t)(stub_va + 28u));
        memcpy(out + 24u, &rel, 4u);
        rel = (int32_t)((int64_t)back_va - (int64_t)(stub_va + 36u));
        memcpy(out + 32u, &rel, 4u);
        return sizeof kStub;
    } else {
        static const uint8_t kHudStub[36] = {
            0x8B, 0x40, 0x3C,                         /*  0 mov eax, [rax+0x3C] */
            0x85, 0xC0,                               /*  3 test eax, eax */
            0x74, 0x13,                               /*  5 jz +19 (26) */
            0x83, 0xF8, 0x0A,                         /*  7 cmp eax, 10 */
            0x76, 0x05,                               /* 10 jbe +5 (17) */
            0xB8, 0x0A, 0x00, 0x00, 0x00,             /* 12 mov eax, 10 */
            0xFF, 0xC8,                               /* 17 dec eax */
            0x89, 0xC1,                               /* 19 mov ecx, eax */
            0xE9, 0, 0, 0, 0,                         /* 21 jmp back (rel32 at 22) */
            0x41, 0x8B, 0xC7,                         /* 26 mov eax, r15d */
            0x89, 0xC1,                               /* 29 mov ecx, eax */
            0xE9, 0, 0, 0, 0                          /* 31 jmp back (rel32 at 32) */
        };
        memcpy(out, kHudStub, sizeof kHudStub);
        rel = (int32_t)((int64_t)back_va - (int64_t)(stub_va + 26u));
        memcpy(out + 22u, &rel, 4u);
        rel = (int32_t)((int64_t)back_va - (int64_t)(stub_va + 36u));
        memcpy(out + 32u, &rel, 4u);
        return sizeof kHudStub;
    }
}

/* Emits 16-byte-stack-aligned MS x64 ABI trampoline calling D12BoardSlotFromSite for board site `kind` (0..2). */
static inline size_t D12EmitBoardStub(
    uint8_t* out,
    uint32_t kind,
    uintptr_t stub_va,
    uintptr_t fn_addr,
    uintptr_t back_va,
    uintptr_t skip_va)
{
    size_t n = 0;
    /* Prologue: push rbp; mov rbp, rsp; push rcx, rdx, r8, r9, r10, r11; and rsp, -16; sub rsp, 0x90 */
    static const uint8_t kPrologue[] = {
        0x55,                                           /* push rbp */
        0x48, 0x89, 0xE5,                               /* mov rbp, rsp */
        0x51,                                           /* push rcx  [rbp-0x08] */
        0x52,                                           /* push rdx  [rbp-0x10] */
        0x41, 0x50,                                     /* push r8   [rbp-0x18] */
        0x41, 0x51,                                     /* push r9   [rbp-0x20] */
        0x41, 0x52,                                     /* push r10  [rbp-0x28] */
        0x41, 0x53,                                     /* push r11  [rbp-0x30] */
        0x48, 0x83, 0xE4, 0xF0,                         /* and rsp, -16 */
        0x48, 0x81, 0xEC, 0x90, 0x00, 0x00, 0x00,       /* sub rsp, 0x90 */
        0x0F, 0x11, 0x44, 0x24, 0x30,                   /* movups [rsp+0x30], xmm0 */
        0x0F, 0x11, 0x4C, 0x24, 0x40,                   /* movups [rsp+0x40], xmm1 */
        0x0F, 0x11, 0x54, 0x24, 0x50,                   /* movups [rsp+0x50], xmm2 */
        0x0F, 0x11, 0x5C, 0x24, 0x60,                   /* movups [rsp+0x60], xmm3 */
        0x0F, 0x11, 0x64, 0x24, 0x70,                   /* movups [rsp+0x70], xmm4 */
        0x0F, 0x11, 0xAC, 0x24, 0x80, 0x00, 0x00, 0x00  /* movups [rsp+0x80], xmm5 */
    };
    _Static_assert(sizeof kPrologue == 58u, "D12EmitBoardStub kPrologue size");
    memcpy(out + n, kPrologue, sizeof kPrologue);
    n += sizeof kPrologue;

    /* mov ecx, kind; mov r8d, esi */
    out[n++] = 0xB9;
    memcpy(out + n, &kind, 4u);
    n += 4u;
    out[n++] = 0x41; out[n++] = 0x89; out[n++] = 0xF0;

    if (kind == 0u) {
        /* edx = ebx; r9 = [rbp+0x40]; [rsp+0x20] = dword [rbp+0x48]; [rsp+0x28] = dword [rbp-0x10] */
        static const uint8_t kArgs0[20] = {
            0x89, 0xDA,                         /* mov edx, ebx */
            0x4C, 0x8B, 0x4D, 0x40,             /* mov r9, [rbp+0x40] */
            0x8B, 0x45, 0x48,                   /* mov eax, [rbp+0x48] */
            0x89, 0x44, 0x24, 0x20,             /* mov [rsp+0x20], eax */
            0x8B, 0x45, 0xF0,                   /* mov eax, [rbp-0x10] */
            0x89, 0x44, 0x24, 0x28              /* mov [rsp+0x28], eax */
        };
        memcpy(out + n, kArgs0, sizeof kArgs0);
        n += sizeof kArgs0;
    } else if (kind == 1u) {
        /* edx = r14d; r9 = [rbp+0x40]; [rsp+0x20] = r12d; [rsp+0x28] = dword [rbp-0x30] */
        static const uint8_t kArgs1[19] = {
            0x44, 0x89, 0xF2,                   /* mov edx, r14d */
            0x4C, 0x8B, 0x4D, 0x40,             /* mov r9, [rbp+0x40] */
            0x44, 0x89, 0x64, 0x24, 0x20,       /* mov [rsp+0x20], r12d */
            0x8B, 0x45, 0xD0,                   /* mov eax, [rbp-0x30] */
            0x89, 0x44, 0x24, 0x28              /* mov [rsp+0x28], eax */
        };
        memcpy(out + n, kArgs1, sizeof kArgs1);
        n += sizeof kArgs1;
    } else {
        /* edx = r14d; r9 = r12; [rsp+0x20] = dword [rbp+0x40]; [rsp+0x28] = dword [rbp-0x30] */
        static const uint8_t kArgs2[20] = {
            0x44, 0x89, 0xF2,                   /* mov edx, r14d */
            0x4D, 0x89, 0xE1,                   /* mov r9, r12 */
            0x8B, 0x45, 0x40,                   /* mov eax, [rbp+0x40] */
            0x89, 0x44, 0x24, 0x20,             /* mov [rsp+0x20], eax */
            0x8B, 0x45, 0xD0,                   /* mov eax, [rbp-0x30] */
            0x89, 0x44, 0x24, 0x28              /* mov [rsp+0x28], eax */
        };
        memcpy(out + n, kArgs2, sizeof kArgs2);
        n += sizeof kArgs2;
    }

    /* mov rax, fn_addr; call rax */
    out[n++] = 0x48; out[n++] = 0xB8;
    uint64_t fn64 = (uint64_t)fn_addr;
    memcpy(out + n, &fn64, 8u);
    n += 8u;
    out[n++] = 0xFF; out[n++] = 0xD0;

    /* Epilogue: restore xmm0..xmm5, lea rsp, [rbp-0x30], pop r11..rcx, pop rbp */
    static const uint8_t kEpilogue[] = {
        0x0F, 0x10, 0x44, 0x24, 0x30,                   /* movups xmm0, [rsp+0x30] */
        0x0F, 0x10, 0x4C, 0x24, 0x40,                   /* movups xmm1, [rsp+0x40] */
        0x0F, 0x10, 0x54, 0x24, 0x50,                   /* movups xmm2, [rsp+0x50] */
        0x0F, 0x10, 0x5C, 0x24, 0x60,                   /* movups xmm3, [rsp+0x60] */
        0x0F, 0x10, 0x64, 0x24, 0x70,                   /* movups xmm4, [rsp+0x70] */
        0x0F, 0x10, 0xAC, 0x24, 0x80, 0x00, 0x00, 0x00, /* movups xmm5, [rsp+0x80] */
        0x48, 0x8D, 0x65, 0xD0,                         /* lea rsp, [rbp-0x30] */
        0x41, 0x5B,                                     /* pop r11 */
        0x41, 0x5A,                                     /* pop r10 */
        0x41, 0x59,                                     /* pop r9 */
        0x41, 0x58,                                     /* pop r8 */
        0x5A,                                           /* pop rdx */
        0x59,                                           /* pop rcx */
        0x5D                                            /* pop rbp */
    };
    _Static_assert(sizeof kEpilogue == 48u, "D12EmitBoardStub kEpilogue size");
    memcpy(out + n, kEpilogue, sizeof kEpilogue);
    n += sizeof kEpilogue;

    /* cmp eax, -1 */
    out[n++] = 0x83; out[n++] = 0xF8; out[n++] = 0xFF;

    int32_t rel;
    if (kind == 0u) {
        out[n++] = 0x74; out[n++] = 0x0D;                     /* je .skip (+13) */
        out[n++] = 0x89; out[n++] = 0xC0;                     /* mov eax, eax (zero-extend) */
        out[n++] = 0x89; out[n++] = 0xC2;                     /* mov edx, eax */
        out[n++] = 0x48; out[n++] = 0x6B; out[n++] = 0xF8; out[n++] = 0x5C; /* imul rdi, rax, 0x5C */
        out[n++] = 0xE9;
        rel = (int32_t)((int64_t)back_va - (int64_t)(stub_va + n + 4u));
        memcpy(out + n, &rel, 4u);
        n += 4u;
        /* .skip: mov rax, [rsp+0x20]; jmp skip_va */
        out[n++] = 0x48; out[n++] = 0x8B; out[n++] = 0x44; out[n++] = 0x24; out[n++] = 0x20;
        out[n++] = 0xE9;
        rel = (int32_t)((int64_t)skip_va - (int64_t)(stub_va + n + 4u));
        memcpy(out + n, &rel, 4u);
        n += 4u;
    } else {
        out[n++] = 0x74; out[n++] = 0x0E;                     /* je .skip (+14) */
        out[n++] = 0x89; out[n++] = 0xC0;                     /* mov eax, eax (zero-extend) */
        out[n++] = 0x41; out[n++] = 0x89; out[n++] = 0xC3;    /* mov r11d, eax */
        out[n++] = 0x48; out[n++] = 0x6B; out[n++] = 0xF8; out[n++] = 0x5C; /* imul rdi, rax, 0x5C */
        out[n++] = 0xE9;
        rel = (int32_t)((int64_t)back_va - (int64_t)(stub_va + n + 4u));
        memcpy(out + n, &rel, 4u);
        n += 4u;
        /* .skip: jmp skip_va */
        out[n++] = 0xE9;
        rel = (int32_t)((int64_t)skip_va - (int64_t)(stub_va + n + 4u));
        memcpy(out + n, &rel, 4u);
        n += 4u;
    }
    return n;
}

typedef struct D12PatchEntry {
    uintptr_t addr;
    uint8_t len;
    uint8_t is_code;
    uint8_t prev[16];
} D12PatchEntry;

static inline int D12ApplyCheckedPatch(
    const HfApi* api,
    D12PatchEntry* patches,
    size_t* patch_count,
    size_t patch_cap,
    uintptr_t addr,
    const void* data,
    size_t len,
    int is_code)
{
    if (!api || !api->write || !patches || !patch_count || !data || len == 0u || len > 16u || *patch_count >= patch_cap)
        return 0;
    int (*rd)(uintptr_t, void*, size_t) = is_code ? api->read_code : api->read;
    if (!rd) return 0;

    uint8_t prev[16] = {0};
    uint8_t verify[16] = {0};
    if (rd(addr, prev, len) != 0) return 0;

    /* Record original bytes BEFORE attempting mutation so any partial write or failed
     * readback is guaranteed to be restored & verified by D12RollbackPatches. */
    D12PatchEntry* e = &patches[(*patch_count)++];
    e->addr = addr;
    e->len = (uint8_t)len;
    e->is_code = is_code ? 1u : 0u;
    memcpy(e->prev, prev, len);

    if (api->write(addr, data, len) != 0) return 0;
    if (rd(addr, verify, len) != 0 || memcmp(verify, data, len) != 0) return 0;
    return 1;
}

static inline int D12RollbackPatches(
    const HfApi* api,
    const D12PatchEntry* patches,
    size_t patch_count,
    uintptr_t row12_va,
    const uint8_t* row12_orig)
{
    int all_ok = 1;
    if (!api || !api->write) return 0;
    for (size_t i = patch_count; i-- > 0u;) {
        int (*rd)(uintptr_t, void*, size_t) = patches[i].is_code ? api->read_code : api->read;
        uint8_t verify[16] = {0};
        if (!rd ||
            api->write(patches[i].addr, patches[i].prev, patches[i].len) != 0 ||
            rd(patches[i].addr, verify, patches[i].len) != 0 ||
            memcmp(verify, patches[i].prev, patches[i].len) != 0) {
            all_ok = 0;
        }
    }
    if (row12_va && row12_orig) {
        uint8_t verify_row12[D12_ROW_BYTES];
        if (!api->read ||
            api->write(row12_va, row12_orig, D12_ROW_BYTES) != 0 ||
            api->read(row12_va, verify_row12, D12_ROW_BYTES) != 0 ||
            memcmp(verify_row12, row12_orig, D12_ROW_BYTES) != 0) {
            all_ok = 0;
        }
    }
    return all_ok;
}

static inline int D12BuildE9JumpPad(uintptr_t site, uintptr_t target, size_t pad_len, uint8_t out_buf[16])
{
    if (!out_buf || pad_len < 5u || pad_len > 16u) return 0;
    int64_t delta = (int64_t)target - (int64_t)(site + 5u);
    if (!D12InRel32(delta)) return 0;
    int32_t rel = (int32_t)delta;
    memset(out_buf, 0x90, pad_len);
    out_buf[0] = 0xE9u;
    memcpy(out_buf + 1u, &rel, 4u);
    return 1;
}

static inline int D12WriteE9JumpPad(const HfApi* api, uintptr_t site, uintptr_t target, size_t pad_len)
{
    uint8_t buf[16];
    if (!api || !api->write || !D12BuildE9JumpPad(site, target, pad_len, buf)) return 0;
    return api->write(site, buf, pad_len) == 0;
}

static inline uintptr_t D12FollowE9Target(const HfApi* api, uintptr_t site)
{
    uint8_t b[5];
    if (!api || !api->read_code || api->read_code(site, b, sizeof b) != 0 || b[0] != 0xE9u) return 0u;
    int32_t rel = 0;
    memcpy(&rel, b + 1u, 4u);
    return (uintptr_t)((int64_t)(site + 5u) + (int64_t)rel);
}

static inline int D12IsUnemittedStubBytes(const uint8_t* buf, size_t len)
{
    int all_00 = 1, all_cc = 1, all_90 = 1;
    for (size_t i = 0; i < len; ++i) {
        if (buf[i] != 0x00u) all_00 = 0;
        if (buf[i] != 0xCCu) all_cc = 0;
        if (buf[i] != 0x90u) all_90 = 0;
    }
    return all_00 || all_cc || all_90;
}

typedef struct D12StubFlipTarget {
    uintptr_t patch_addr;
    uint8_t new_jcc;
    uint8_t active;
} D12StubFlipTarget;

static inline int D12ValidateCodeSlotFlip(
    const HfApi* api,
    uintptr_t stub,
    size_t off,
    const uint8_t* expected,
    size_t exp_len,
    uint8_t new_jcc,
    D12StubFlipTarget* out_target)
{
    uint8_t buf[16];
    if (!api || !api->read_code || !stub || !expected || !out_target || exp_len == 0u || exp_len > sizeof buf)
        return 0;
    out_target->patch_addr = 0u;
    out_target->new_jcc = new_jcc;
    out_target->active = 0u;
    if (api->read_code(stub + off, buf, exp_len) != 0) return 0;
    if (D12IsUnemittedStubBytes(buf, exp_len)) return 1; /* Optional D11 feature inactive */
    if (memcmp(buf, expected, exp_len) != 0) return 0;
    out_target->patch_addr = stub + off + exp_len - 1u;
    out_target->active = 1u;
    return 1;
}

static inline int D12ValidateHookSiteFlip(
    const HfApi* api,
    uintptr_t site_va,
    size_t off,
    const uint8_t* expected,
    size_t exp_len,
    uint8_t new_jcc,
    D12StubFlipTarget* out_target)
{
    uint8_t head[5], buf[16];
    if (!api || !api->read_code || !site_va || !expected || !out_target || exp_len == 0u || exp_len > sizeof buf)
        return 0;
    out_target->patch_addr = 0u;
    out_target->new_jcc = new_jcc;
    out_target->active = 0u;
    if (api->read_code(site_va, head, sizeof head) != 0) return 0;
    if (head[0] != 0xE9u) return 1; /* Optional D11 feature not hooked */
    int32_t rel = 0;
    memcpy(&rel, head + 1u, 4u);
    uintptr_t stub = (uintptr_t)((int64_t)(site_va + 5u) + (int64_t)rel);
    if (stub <= 0x10000u || api->read_code(stub + off, buf, exp_len) != 0) return 0;
    if (memcmp(buf, expected, exp_len) != 0) return 0;
    out_target->patch_addr = stub + off + exp_len - 1u;
    out_target->active = 1u;
    return 1;
}

/* Raises online override 48726272 ({0xAF218CAC, 0xFAABBEA9}) in [game + 0x347CDF8] from 10/11 -> 12
 * once the config table is populated, while no UI menu is open. */
static inline uintptr_t D12FindConfigOverrideEntry(const HfApi* api, uintptr_t table, uint32_t key)
{
    uintptr_t entries = 0u;
    uint32_t capacity = 0u, empty = 0u, mult = 0u;
    if (api->read(table + 0x00u, &entries, 8u) != 0 ||
        api->read(table + 0x08u, &capacity, 4u) != 0 ||
        api->read(table + 0x0Cu, &empty, 4u) != 0 ||
        api->read(table + 0x10u, &mult, 4u) != 0)
        return 0u;
    if (!entries || !capacity || capacity > (1u << 20) || (capacity & (capacity - 1u)) != 0u) return 0u;
    for (uint32_t i = 0; i < capacity && i < 4096u; ++i) {
        uintptr_t entry = entries + 48u * (uintptr_t)((capacity - 1u) & (i + key * mult));
        uint32_t found = 0u;
        if (api->read(entry, &found, 4u) != 0 || found == empty) return 0u;
        if (found == key) return entry;
    }
    return 0u;
}

static inline int D12TrySyncOnlineCap(D12NativeSlotState* st, const HfApi* api, uintptr_t game)
{
    if (!st || st->state != 1 || st->online_cap_raised || !api || !api->read || !api->write || !game) return 0;

    uintptr_t ui = 0u;
    uint32_t menu_open = 0u;
    if (api->read(game + D12_UI_RVA, &ui, 8u) == 0 && ui != 0u) {
        if (api->read(ui + 0x4294u, &menu_open, 4u) == 0 && menu_open != 0u) return 0;
    }

    uintptr_t cfg = 0u;
    if (api->read(game + D12_CONFIG_RVA, &cfg, 8u) != 0 || !cfg) return 0;

    const uint32_t m = 0x5BD1E995u;
    uint32_t k = 0xFAABBEA9u * m;
    uint32_t key = ((k ^ (k >> 24)) * m) ^ (0xAF218CACu * m);

    uintptr_t entry = D12FindConfigOverrideEntry(api, cfg + 0x12078u, key);
    if (!entry) entry = D12FindConfigOverrideEntry(api, cfg + 0xC050u, key);
    if (!entry) return 0;

    uint32_t type = 0u, cur_val = 0u;
    if (api->read(entry + 0x14u, &type, 4u) != 0 || type != 6u ||
        api->read(entry + 0x18u, &cur_val, 4u) != 0)
        return 0;

    if (cur_val >= 12u) {
        st->online_cap_raised = 1;
        return 1;
    }
    if (cur_val == 10u || cur_val == 11u) {
        uint32_t twelve = 12u;
        if (api->write(entry + 0x18u, &twelve, 4u) == 0) {
            st->online_cap_raised = 1;
            return 1;
        }
    }
    return 0;
}

static inline void D12PopulateRow12(D12NativeSlotState* st, const CdHostOptions* ho)
{
    if (!st || !st->row12) return;
    uint8_t* raw = (uint8_t*)st->row12;
    memcpy(raw, st->row12_baseline, D12_ROW_BYTES);

    /* Always configure Slot 12 as Full Clone Wars (or ULTRAKILL if explicitly selected) */
    CdHostOptions eff = ho ? *ho : cd_host_options_defaults();
    if (!cd_host_options_is_ultrakill(&eff)) {
        eff.theater_preset = CD_THEATER_CLONE_WARS;
        eff.chaos_intensity = CD_CHAOS_CLONE_WARS;
    }
    UltrakillApplyDifficultyRow12(st->row12, st->row12_baseline, &eff);

    uint32_t item = D12_UNLOCK_ITEM_ID;
    uint32_t level = 12u;
    uint32_t name_txt = D12_NAME_TEXT_ID;
    uint32_t num_txt = D12_NUM_TEXT_ID;
    memcpy(raw + 0x00u, &item, 4u);
    memcpy(raw + 0x04u, &level, 4u);
    memcpy(raw + 0x08u, &name_txt, 4u);
    memcpy(raw + 0x0Cu, &num_txt, 4u);
    raw[0xECu] = 11u; /* Keep tech level 11 so all 6 enemy rosters & 319 compositions stay in-bounds */
}

/* Installs the genuine 12th difficulty slot once HellForge has finished installing Difficulty 11.
 * Returns: 1 = newly installed (or already installed), 0 = still waiting for D11 / menu close, -1 = refused. */
static inline int D12TryInstallNativeSlot(
    D12NativeSlotState* st,
    const HfApi* api,
    HfMod* mod,
    uintptr_t game,
    size_t game_size,
    const CdHostOptions* ho)
{
    if (!st || !api || !mod) return -1;
    if (st->state != 0) return st->state;
    if (!game || !api->read || !api->read_code || !api->write ||
        api->size < offsetof(HfApi, difficulty_row) + sizeof(api->difficulty_row) ||
        !api->difficulty_row)
        return 0;

    uintptr_t row11_va = api->difficulty_row(11u);
    if (!row11_va) return 0; /* HellForge's InstallDifficulty has not completed yet */

    uint32_t row11_lvl = 0u;
    if (api->read(row11_va + 4u, &row11_lvl, 4u) != 0 || row11_lvl != 11u) return 0;

    uintptr_t row12_va = row11_va + D12_ROW_BYTES;
    uint8_t row11_snapshot[D12_ROW_BYTES];
    uint8_t row12_orig[D12_ROW_BYTES];
    if (api->read(row11_va, row11_snapshot, D12_ROW_BYTES) != 0 ||
        api->read(row12_va, row12_orig, D12_ROW_BYTES) != 0) {
        st->state = -1;
        api->log(mod, "CHAOS_D12_SLOT: refused reason=row12_table_unreadable");
        return -1;
    }

    /* Never widen carousel bounds while a UI screen/war-table bar is open */
    uintptr_t ui = 0u;
    uint32_t menu_open = 0u;
    if (api->read(game + D12_UI_RVA, &ui, 8u) == 0 && ui != 0u) {
        if (api->read(ui + 0x4294u, &menu_open, 4u) == 0 && menu_open != 0u) return 0;
    }

    /* 1. Locate HellForge's d->code stub page via kBarSites[0] (game + 0x19D004A -> d->code + 0x100) */
    uintptr_t bar_stub0 = D12FollowE9Target(api, game + D12_BAR_SITE0_RVA);
    if (bar_stub0 <= 0x10000u) {
        st->state = -1;
        api->log(mod, "CHAOS_D12_SLOT: refused reason=bar_stub0_missing");
        return -1;
    }
    uintptr_t d_code = bar_stub0 - 0x100u;
    uint8_t orig_extra[63];
    if (api->read_code(d_code, orig_extra, sizeof orig_extra) != 0 ||
        orig_extra[0] != 0x52u || orig_extra[1] != 0x41u || orig_extra[2] != 0x50u ||
        memcmp(orig_extra + 42u, kD12ExtraWidgetTemplate + 42u, 7u) != 0) {
        st->state = -1;
        api->log(mod, "CHAOS_D12_SLOT: refused reason=extra_widget_pin_mismatch");
        return -1;
    }

    /* 2. Pre-verify kLevelInPlace[1..3], mission board E9 hooks, and D12_BOARD_REG_MAX_RVA */
    uint8_t ip1[16], ip2[16], ip3[12], reg_max_insn[5];
    uint32_t eleven = 11u;
    if (api->read_code(game + 0x5E4FAFu, ip1, 16u) != 0 || memcmp(ip1, kD12HfInPlace16Pin, 16u) != 0 ||
        api->read_code(game + 0x5E503Fu, ip2, 16u) != 0 || memcmp(ip2, kD12HfInPlace16Pin, 16u) != 0 ||
        api->read_code(game + 0x17C6AA3u, ip3, 12u) != 0 || memcmp(ip3, kD12HfInPlace12Pin, 12u) != 0 ||
        !D12FollowE9Target(api, game + D12_BOARD_REG_SITE_RVA) ||
        !D12FollowE9Target(api, game + D12_BOARD_PLN_SITE_RVA) ||
        !D12FollowE9Target(api, game + D12_BOARD_DEF_SITE_RVA) ||
        api->read_code(game + D12_BOARD_REG_MAX_RVA, reg_max_insn, 5u) != 0 ||
        reg_max_insn[0] != 0xBDu || memcmp(reg_max_insn + 1u, &eleven, 4u) != 0) {
        st->state = -1;
        api->log(mod, "CHAOS_D12_SLOT: refused reason=inplace_or_board_pin_mismatch");
        return -1;
    }

    /* 3. Pre-verify moved band site, ALL 7 distance sites (full instruction prefix + displacement), ALL 14 companion stub sites, and ALL 14 kD12DifficultyBounds */
    uint8_t band_insn[8];
    int32_t cur_band_disp = 0, cur_dist_disp = 0;
    if (api->read_code(game + D12_BAND_SITE_RVA, band_insn, 8u) != 0 ||
        band_insn[0] != 0x49u || band_insn[1] != 0x8Bu || band_insn[2] != 0x94u || band_insn[3] != 0xC6u) {
        st->state = -1;
        api->log(mod, "CHAOS_D12_SLOT: refused reason=band_or_distance_disp_unreadable");
        return -1;
    }
    memcpy(&cur_band_disp, band_insn + 4u, 4u);
    for (size_t i = 0u; i < 7u; ++i) {
        uint8_t d_insn[10] = {0};
        size_t p_len = kD12DistanceSites[i].disp_at;
        int32_t site_disp = 0;
        if (p_len == 0u || p_len > 6u ||
            api->read_code(game + kD12DistanceSites[i].rva, d_insn, p_len + 4u) != 0 ||
            memcmp(d_insn, kD12DistanceSites[i].prefix, p_len) != 0) {
            st->state = -1;
            api->log(mod, "CHAOS_D12_SLOT: refused reason=distance_site_prefix_mismatch index=%u", (unsigned)i);
            return -1;
        }
        memcpy(&site_disp, d_insn + p_len, 4u);
        if (i == 0u) {
            cur_dist_disp = site_disp;
        } else if (site_disp != cur_dist_disp) {
            st->state = -1;
            api->log(mod, "CHAOS_D12_SLOT: refused reason=distance_site_pin_mismatch index=%u", (unsigned)i);
            return -1;
        }
    }
    uintptr_t old_bands = (uintptr_t)((int64_t)game + (int64_t)cur_band_disp);
    uintptr_t old_dist  = (uintptr_t)((int64_t)game + (int64_t)cur_dist_disp);

    static const uint8_t kCmpEsi11Jne[5]      = {0x83, 0xFE, 0x0B, 0x0F, 0x85};
    static const uint8_t kCmpRdi4_11Jne[6]    = {0x83, 0x7F, 0x04, 0x0B, 0x0F, 0x85};
    static const uint8_t kCmpRbxRsi4_11Jne[7] = {0x83, 0x7C, 0x33, 0x04, 0x0B, 0x0F, 0x85};
    static const uint8_t kCmpRbxRbp4_11Jne[7] = {0x83, 0x7C, 0x2B, 0x04, 0x0B, 0x0F, 0x85};
    static const uint8_t kCmpRaxF9_11Jne[8]   = {0x80, 0xB8, 0xF9, 0x00, 0x00, 0x00, 0x0B, 0x75};
    static const uint8_t kCmpRsi9_11Jne[5]    = {0x80, 0x7E, 0x09, 0x0B, 0x75};
    static const uint8_t kCmpR13F9_11Jne[9]   = {0x41, 0x80, 0xBD, 0xF9, 0x00, 0x00, 0x00, 0x0B, 0x75};
    static const uint8_t kCmpRcx10_11Jne[5]   = {0x83, 0x79, 0x10, 0x0B, 0x75};

    D12StubFlipTarget flips[14];
    if (!D12ValidateCodeSlotFlip(api, d_code + 0x040u, 0u,  kCmpEsi11Jne,      sizeof kCmpEsi11Jne,      0x82u, &flips[0]) ||
        !D12ValidateCodeSlotFlip(api, d_code + 0x940u, 0u,  kCmpEsi11Jne,      sizeof kCmpEsi11Jne,      0x82u, &flips[1]) ||
        !D12ValidateCodeSlotFlip(api, d_code + 0x9D0u, 0u,  kCmpRdi4_11Jne,    sizeof kCmpRdi4_11Jne,    0x82u, &flips[2]) ||
        !D12ValidateCodeSlotFlip(api, d_code + 0x9E0u, 0u,  kCmpRbxRsi4_11Jne, sizeof kCmpRbxRsi4_11Jne, 0x82u, &flips[3]) ||
        !D12ValidateCodeSlotFlip(api, d_code + 0x9F0u, 0u,  kCmpRbxRsi4_11Jne, sizeof kCmpRbxRsi4_11Jne, 0x82u, &flips[4]) ||
        !D12ValidateCodeSlotFlip(api, d_code + 0xA00u, 0u,  kCmpRbxRbp4_11Jne, sizeof kCmpRbxRbp4_11Jne, 0x82u, &flips[5]) ||
        !D12ValidateCodeSlotFlip(api, d_code + 0x790u, 7u,  kCmpRaxF9_11Jne, sizeof kCmpRaxF9_11Jne, 0x72u, &flips[6]) ||
        !D12ValidateCodeSlotFlip(api, d_code + 0x7E0u, 0u,  kCmpRsi9_11Jne,  sizeof kCmpRsi9_11Jne,  0x72u, &flips[7]) ||
        !D12ValidateCodeSlotFlip(api, d_code + 0x810u, 31u, kCmpRaxF9_11Jne, sizeof kCmpRaxF9_11Jne, 0x72u, &flips[8]) ||
        !D12ValidateCodeSlotFlip(api, d_code + 0xC40u, 14u, kCmpR13F9_11Jne, sizeof kCmpR13F9_11Jne, 0x72u, &flips[9]) ||
        !D12ValidateHookSiteFlip(api, game + D12_SELECTION_END_RVA, 19u, kCmpRcx10_11Jne, sizeof kCmpRcx10_11Jne, 0x72u, &flips[10]) ||
        !D12ValidateHookSiteFlip(api, game + D12_GRANT_SITE_RVA,    30u, kCmpRaxF9_11Jne, sizeof kCmpRaxF9_11Jne, 0x72u, &flips[11]) ||
        !D12ValidateHookSiteFlip(api, game + D12_RADAR_STATE_RVA,   44u, kCmpRaxF9_11Jne, sizeof kCmpRaxF9_11Jne, 0x72u, &flips[12]) ||
        !D12ValidateHookSiteFlip(api, game + D12_SHELL_WEIGHT_RVA,  38u, kCmpRaxF9_11Jne, sizeof kCmpRaxF9_11Jne, 0x72u, &flips[13])) {
        st->state = -1;
        api->log(mod, "CHAOS_D12_SLOT: refused reason=companion_stub_pin_mismatch");
        return -1;
    }

    for (size_t i = 0u; i < 14u; ++i) {
        uint8_t b_insn[8] = {0};
        size_t p_len = kD12DifficultyBounds[i].imm_at;
        size_t w_len = kD12DifficultyBounds[i].width;
        uint32_t cur_bound = 0u;
        if (p_len == 0u || p_len > 2u || (w_len != 1u && w_len != 4u)) {
            st->state = -1;
            return -1;
        }
        if (api->read_code(game + kD12DifficultyBounds[i].rva, b_insn, p_len + w_len) != 0 ||
            memcmp(b_insn, kD12DifficultyBounds[i].prefix, p_len) != 0) {
            st->state = -1;
            api->log(mod, "CHAOS_D12_SLOT: refused reason=difficulty_bound_prefix_mismatch index=%u", (unsigned)i);
            return -1;
        }
        memcpy(&cur_bound, b_insn + p_len, w_len);
        if (cur_bound != 11u) {
            st->state = -1;
            api->log(mod, "CHAOS_D12_SLOT: refused reason=difficulty_bound_pin_mismatch index=%u", (unsigned)i);
            return -1;
        }
    }

    /* 4. Allocate near-game RW (widgets + bands + distance) and RX (stubs) blocks + rollback log */
    uint8_t* rw_mem = D12AllocNearGame(game, game_size, 0x14000u, d_code);
    uint8_t* rx_mem = rw_mem ? D12AllocNearGame(game, game_size, 0x1000u, d_code) : NULL;
    const size_t patch_cap = 416u;
    D12PatchEntry* patches = (rw_mem && rx_mem) ? (D12PatchEntry*)calloc(patch_cap, sizeof(D12PatchEntry)) : NULL;
    if (!rw_mem || !rx_mem || !patches ||
        !D12InRel32((int64_t)((uintptr_t)rw_mem + 0x13000u) - (int64_t)rx_mem) ||
        !D12InRel32((int64_t)rx_mem - (int64_t)rw_mem)) {
        free(patches);
        if (rw_mem) VirtualFree(rw_mem, 0, MEM_RELEASE);
        if (rx_mem) VirtualFree(rx_mem, 0, MEM_RELEASE);
        st->state = -1;
        api->log(mod, "CHAOS_D12_SLOT: refused reason=near_game_alloc_failed");
        return -1;
    }
    memset(rw_mem, 0, 0x14000u);
    memset(rx_mem, 0xCC, 0x1000u);

    uint8_t* d12_widgets  = rw_mem + 0x00000u; /* 32 * 8 + 32 * 0x910 = 0x12300 bytes */
    uint8_t* d12_bands    = rw_mem + 0x12400u; /* 16 * 8 bytes */
    uint8_t* d12_distance = rw_mem + 0x12500u; /* 16 * 12 bytes */

    if (api->read(old_bands, d12_bands, 11u * 8u) != 0 ||
        api->read(old_dist, d12_distance, 11u * 12u) != 0) {
        free(patches);
        VirtualFree(rw_mem, 0, MEM_RELEASE);
        VirtualFree(rx_mem, 0, MEM_RELEASE);
        st->state = -1;
        api->log(mod, "CHAOS_D12_SLOT: refused reason=band_or_distance_copy_failed");
        return -1;
    }
    memcpy(d12_bands + 11u * 8u, d12_bands + 10u * 8u, 8u);
    memcpy(d12_distance + 11u * 12u, d12_distance + 10u * 12u, 12u);

    /* 5. Prepare RX stubs:
     *    +0x000: relocated orig_extra (for index 10 == Difficulty 11)
     *    +0x080: d12_extra (cmp eax, 10; je orig_extra; else index 11 -> d12_widgets)
     *    +0x100: kLevelInPlace[1] (0x5E4FAF)
     *    +0x140: kLevelInPlace[2] (0x5E503F)
     *    +0x180: kLevelInPlace[3] (0x17C6AA3)
     *    +0x200: Mission board region stub (0x11E4744)
     *    +0x2C0: Mission board planet campaign stub (0x11E3E67)
     *    +0x380: Mission board planet defence stub (0x11E4287) */
    int32_t orig_bars_rel = 0, orig_w_rel = 0;
    memcpy(&orig_bars_rel, orig_extra + 6u, 4u);
    memcpy(&orig_w_rel, orig_extra + 52u, 4u);
    uintptr_t orig_bars_va = (uintptr_t)((int64_t)(d_code + 10u) + (int64_t)orig_bars_rel);
    uintptr_t orig_w_va    = (uintptr_t)((int64_t)(d_code + 56u) + (int64_t)orig_w_rel);
    int64_t new_orig_bars_rel = (int64_t)orig_bars_va - (int64_t)((uintptr_t)rx_mem + 0x000u + 10u);
    int64_t new_orig_w_rel    = (int64_t)orig_w_va    - (int64_t)((uintptr_t)rx_mem + 0x000u + 56u);
    if (!D12InRel32(new_orig_bars_rel) || !D12InRel32(new_orig_w_rel)) {
        free(patches);
        VirtualFree(rw_mem, 0, MEM_RELEASE);
        VirtualFree(rx_mem, 0, MEM_RELEASE);
        st->state = -1;
        api->log(mod, "CHAOS_D12_SLOT: refused reason=orig_widgets_out_of_rel32");
        return -1;
    }

    memcpy(rx_mem + 0x000u, orig_extra, sizeof orig_extra);
    int32_t rel32 = (int32_t)new_orig_bars_rel;
    memcpy(rx_mem + 0x000u + 6u, &rel32, 4u);
    rel32 = (int32_t)new_orig_w_rel;
    memcpy(rx_mem + 0x000u + 52u, &rel32, 4u);

    uint8_t* d12_extra = rx_mem + 0x080u;
    d12_extra[0] = 0x83u; d12_extra[1] = 0xF8u; d12_extra[2] = 0x0Au; /* cmp eax, 10 */
    d12_extra[3] = 0x0Fu; d12_extra[4] = 0x84u;                       /* je rx_mem + 0x000 */
    rel32 = (int32_t)((int64_t)(uintptr_t)(rx_mem + 0x000u) - (int64_t)(uintptr_t)(d12_extra + 9u));
    memcpy(d12_extra + 5u, &rel32, 4u);
    memcpy(d12_extra + 9u, kD12ExtraWidgetTemplate, sizeof kD12ExtraWidgetTemplate);
    rel32 = (int32_t)((int64_t)(uintptr_t)d12_widgets - (int64_t)(uintptr_t)(d12_extra + 9u + 10u));
    memcpy(d12_extra + 9u + 6u, &rel32, 4u);
    rel32 = (int32_t)((int64_t)(uintptr_t)(d12_widgets + D12_BAR_SLOTS * 8u) - (int64_t)(uintptr_t)(d12_extra + 9u + 56u));
    memcpy(d12_extra + 9u + 52u, &rel32, 4u);

    D12EmitLevelInPlaceStub(rx_mem + 0x100u, 0, (uintptr_t)(rx_mem + 0x100u), game + 0x5E4FBFu);
    D12EmitLevelInPlaceStub(rx_mem + 0x140u, 0, (uintptr_t)(rx_mem + 0x140u), game + 0x5E504Fu);
    D12EmitLevelInPlaceStub(rx_mem + 0x180u, 1, (uintptr_t)(rx_mem + 0x180u), game + 0x17C6AAFu);

    uintptr_t board_fn = (uintptr_t)(void*)&D12BoardSlotFromSite;
    D12EmitBoardStub(rx_mem + 0x200u, 0u, (uintptr_t)(rx_mem + 0x200u), board_fn, game + D12_BOARD_REG_BACK_RVA, game + D12_BOARD_REG_SKIP_RVA);
    D12EmitBoardStub(rx_mem + 0x2C0u, 1u, (uintptr_t)(rx_mem + 0x2C0u), board_fn, game + D12_BOARD_PLN_BACK_RVA, game + D12_BOARD_PLN_SKIP_RVA);
    D12EmitBoardStub(rx_mem + 0x380u, 2u, (uintptr_t)(rx_mem + 0x380u), board_fn, game + D12_BOARD_DEF_BACK_RVA, game + D12_BOARD_DEF_SKIP_RVA);

    uint8_t jmp_d_extra[16], jmp_ip1[16], jmp_ip2[16], jmp_ip3[16], jmp_b0[16], jmp_b1[16], jmp_b2[16];
    if (!D12BuildE9JumpPad(d_code, (uintptr_t)d12_extra, 5u, jmp_d_extra) ||
        !D12BuildE9JumpPad(game + 0x5E4FAFu, (uintptr_t)(rx_mem + 0x100u), 16u, jmp_ip1) ||
        !D12BuildE9JumpPad(game + 0x5E503Fu, (uintptr_t)(rx_mem + 0x140u), 16u, jmp_ip2) ||
        !D12BuildE9JumpPad(game + 0x17C6AA3u, (uintptr_t)(rx_mem + 0x180u), 12u, jmp_ip3) ||
        !D12BuildE9JumpPad(game + D12_BOARD_REG_SITE_RVA, (uintptr_t)(rx_mem + 0x200u), 6u, jmp_b0) ||
        !D12BuildE9JumpPad(game + D12_BOARD_PLN_SITE_RVA, (uintptr_t)(rx_mem + 0x2C0u), 7u, jmp_b1) ||
        !D12BuildE9JumpPad(game + D12_BOARD_DEF_SITE_RVA, (uintptr_t)(rx_mem + 0x380u), 7u, jmp_b2)) {
        free(patches);
        VirtualFree(rw_mem, 0, MEM_RELEASE);
        VirtualFree(rx_mem, 0, MEM_RELEASE);
        st->state = -1;
        api->log(mod, "CHAOS_D12_SLOT: refused reason=jmp_rel32_out_of_range");
        return -1;
    }

    DWORD old_prot = 0;
    if (!VirtualProtect(rx_mem, 0x1000u, PAGE_EXECUTE_READ, &old_prot)) {
        free(patches);
        VirtualFree(rw_mem, 0, MEM_RELEASE);
        VirtualFree(rx_mem, 0, MEM_RELEASE);
        st->state = -1;
        api->log(mod, "CHAOS_D12_SLOT: refused reason=rx_protect_failed");
        return -1;
    }
    FlushInstructionCache(GetCurrentProcess(), rx_mem, 0x1000u);

    /* 6. Populate Difficulty 12 row (row11 + 0x330) via checked write BEFORE touching code or bounds */
    st->rw_mem = rw_mem;
    st->rx_mem = rx_mem;
    st->row12 = (HfDifficultySettings*)row12_va;
    memcpy(st->row12_baseline, row11_snapshot, D12_ROW_BYTES);
    uint8_t prepared_row12[D12_ROW_BYTES];
    {
        HfDifficultySettings* saved_ptr = st->row12;
        st->row12 = (HfDifficultySettings*)prepared_row12;
        D12PopulateRow12(st, ho);
        st->row12 = saved_ptr;
    }
    uint8_t verify_row12[D12_ROW_BYTES];
    if (api->write(row12_va, prepared_row12, D12_ROW_BYTES) != 0 ||
        api->read(row12_va, verify_row12, D12_ROW_BYTES) != 0 ||
        memcmp(verify_row12, prepared_row12, D12_ROW_BYTES) != 0) {
        int clean_row12 = D12RollbackPatches(api, NULL, 0u, row12_va, row12_orig);
        st->row12 = NULL;
        free(patches);
        if (clean_row12) {
            st->rw_mem = NULL;
            st->rx_mem = NULL;
            VirtualFree(rw_mem, 0, MEM_RELEASE);
            VirtualFree(rx_mem, 0, MEM_RELEASE);
        }
        st->state = -1;
        api->log(mod, "CHAOS_D12_SLOT: refused reason=row12_write_failed rolled_back=%d", clean_row12);
        return -1;
    }

    /* 7..11. Apply all prerequisite patches transactionally; widen kD12DifficultyBounds ONLY last! */
    size_t patch_count = 0u;
    const char* fail_reason = NULL;
    int32_t new_band_disp = (int32_t)((int64_t)(uintptr_t)d12_bands - (int64_t)game);
    int32_t new_dist_disp = (int32_t)((int64_t)(uintptr_t)d12_distance - (int64_t)game);
    uint32_t twelve = 12u;
    uint32_t widened_objs = 0u, widened_ops = 0u, widened_missions = 0u;

    if (!D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, game + D12_BAND_SITE_RVA + 4u, &new_band_disp, 4u, 1)) {
        fail_reason = "band_write_failed";
    }
    for (size_t i = 0u; !fail_reason && i < 7u; ++i) {
        uintptr_t at = game + kD12DistanceSites[i].rva + kD12DistanceSites[i].disp_at;
        if (!D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, at, &new_dist_disp, 4u, 1))
            fail_reason = "distance_write_failed";
    }
    if (!fail_reason && (
        !D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, d_code, jmp_d_extra, 5u, 1) ||
        !D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, game + 0x5E4FAFu, jmp_ip1, 16u, 1) ||
        !D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, game + 0x5E503Fu, jmp_ip2, 16u, 1) ||
        !D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, game + 0x17C6AA3u, jmp_ip3, 12u, 1) ||
        !D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, game + D12_BOARD_REG_SITE_RVA, jmp_b0, 6u, 1) ||
        !D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, game + D12_BOARD_PLN_SITE_RVA, jmp_b1, 7u, 1) ||
        !D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, game + D12_BOARD_DEF_SITE_RVA, jmp_b2, 7u, 1) ||
        !D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, game + D12_BOARD_REG_MAX_RVA + 1u, &twelve, 4u, 1))) {
        fail_reason = "stub_redirect_failed";
    }
    for (size_t i = 0u; !fail_reason && i < 14u; ++i) {
        if (!flips[i].active) continue;
        if (!D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, flips[i].patch_addr, &flips[i].new_jcc, 1u, 1))
            fail_reason = "companion_flip_write_failed";
    }
    for (uint32_t i = 0u; !fail_reason && i < D12_OBJECTIVE_ROWS; ++i) {
        uintptr_t at = game + D12_OBJECTIVES_RVA + (uintptr_t)i * D12_OBJECTIVE_STRIDE + 0x28u;
        uint32_t hi = 0u;
        if (api->read(at, &hi, 4u) != 0) { fail_reason = "objective_table_read_failed"; break; }
        if (hi == 11u) {
            if (!D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, at, &twelve, 4u, 0)) {
                fail_reason = "objective_table_write_failed";
                break;
            }
            ++widened_objs;
        }
    }
    for (uint32_t i = 0u; !fail_reason && i < D12_OPERATION_ROWS; ++i) {
        uintptr_t at = game + D12_OPERATIONS_RVA + (uintptr_t)i * D12_OPERATION_STRIDE + 0x10u;
        uint32_t hi = 0u;
        if (api->read(at, &hi, 4u) != 0) { fail_reason = "operation_table_read_failed"; break; }
        if (hi == 11u) {
            if (!D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, at, &twelve, 4u, 0)) {
                fail_reason = "operation_table_write_failed";
                break;
            }
            ++widened_ops;
        }
    }
    for (uint32_t i = 0u; !fail_reason && i < D12_MISSION_ROWS; ++i) {
        uintptr_t at = game + D12_MISSIONS_RVA + (uintptr_t)i * D12_MISSION_STRIDE + 0x10u;
        uint32_t hi = 0u;
        if (api->read(at, &hi, 4u) != 0) { fail_reason = "mission_table_read_failed"; break; }
        if (hi == 11u) {
            if (!D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, at, &twelve, 4u, 0)) {
                fail_reason = "mission_table_write_failed";
                break;
            }
            ++widened_missions;
        }
    }
    /* Publish new bounds ONLY after every prerequisite above succeeded (carousel count 0x11EBB4A last!) */
    for (size_t i = 14u; !fail_reason && i-- > 0u;) {
        uintptr_t at = game + kD12DifficultyBounds[i].rva + kD12DifficultyBounds[i].imm_at;
        if (!D12ApplyCheckedPatch(api, patches, &patch_count, patch_cap, at, &twelve, kD12DifficultyBounds[i].width, 1))
            fail_reason = "difficulty_bound_write_failed";
    }

    if (fail_reason) {
        int clean_rb = D12RollbackPatches(api, patches, patch_count, row12_va, row12_orig);
        free(patches);
        st->row12 = NULL;
        if (clean_rb) {
            st->rw_mem = NULL;
            st->rx_mem = NULL;
            VirtualFree(rw_mem, 0, MEM_RELEASE);
            VirtualFree(rx_mem, 0, MEM_RELEASE);
        }
        st->state = -1;
        api->log(mod, "CHAOS_D12_SLOT: refused reason=%s rolled_back=%d patches=%u",
                 fail_reason, clean_rb, (unsigned)patch_count);
        return -1;
    }

    free(patches);
    st->state = 1;
    (void)D12TrySyncOnlineCap(st, api, game);

    api->log(mod,
             "CHAOS_D12_SLOT: installed=1 row11=0x%llx (HELL FORGE intact) row12=0x%llx item=0x%08X objs=%u ops=%u missions=%u online_cap=%d",
             (unsigned long long)row11_va,
             (unsigned long long)(uintptr_t)st->row12,
             (unsigned)D12_UNLOCK_ITEM_ID,
             widened_objs, widened_ops, widened_missions,
             st->online_cap_raised);
    return 1;
}

#endif /* CHAOS_D12_NATIVE_SLOT_H */
