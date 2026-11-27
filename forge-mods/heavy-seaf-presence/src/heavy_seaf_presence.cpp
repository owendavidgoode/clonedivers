/* Heavy SEAF Presence Everywhere, for HellForge build 25480438.
 *
 * The game spawns SEAF squads on a mission when that planet's record carries modifier 1401
 * (Heavy SEAF Presence). This mod writes 1401 into the first free modifier slot of every planet
 * record, and writes it again when the game rebuilds the table. Modifiers a planet already has
 * stay in place. A full record loses one low-priority marker id (1311, then 1308, 1318, 1309)
 * to make a slot, and only then.
 *
 * The planet table on this build is the one Second Galactic War's probe described: records of
 * 0x130 bytes, eight modifier ids at +0x00, the used-slot count at +0x80, a few copies, each
 * copy's first record at one of 0xFB20 / 0xDF40 / 0x0F58 inside a 64 KB heap block. A candidate
 * is used only when it contains a known modifier pair in slots 0 and 1 (so a shifted look-alike
 * is never written).
 *
 * EXTRA_SQUADS raises the live-SEAF cap by that many squads of 6. 0 leaves the game's numbers
 * (12 and 18 per drop, admission at 10 and 18) untouched. 5 is the highest step (about 48 SEAF).
 * The four numbers are changed only when exactly one SEAF template is found and those numbers
 * are still the defaults.
 */
#include "hellforge_mod.h"
#include "hellforge_content.h"
#include "options_menu.h"
#include "heavy_gate_and_pacing.h"
#include "tm_tick.h"

#include <math.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <windows.h>

#define MODIFIER 1401u
#define MARKER 1400u /* the galactic-map tag "Heavy SEAF Presence"; 1401 is the mission modifier */
#define EXTRA_SQUADS 0
/* Constant drops stop adding waves while this many SEAF are alive. The game's own
 * drops keep arriving, so the field can pass it; the timer waits for casualties. */
#define WAVE_CEILING 24

/* The war-status parser, the galactic-effect catalog parser, and the game's allocator.
 * Confirmed on build 25480438. The catalog parser is the one HellForge's backend names for
 * /WarSeason/GalacticWarEffects. That reply is an empty list, so a mission never learns that
 * modifier 1401 means SEAF Pelicans. */
#define STATUS_RVA 0x1071a10u
#define EFFECTS_RVA 0x1081ba0u
#define ALLOC_RVA 0x173b8d0u
#define ALLOCATOR_RVA 0x33262f0u
static const unsigned char STATUS_PROLOGUE[32] = {
    0x48, 0x89, 0x5c, 0x24, 0x18, 0x48, 0x89, 0x74, 0x24, 0x20, 0x48, 0x89, 0x4c, 0x24, 0x08, 0x55,
    0x57, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57, 0x48, 0x8b, 0xec, 0x48, 0x83, 0xec, 0x50, 0x41, 0x8b
};
typedef uintptr_t (*ParserFn)(uintptr_t, uintptr_t, uintptr_t, uintptr_t, uintptr_t, uintptr_t, uintptr_t, uintptr_t);
typedef void* (*AllocFn)(uintptr_t object, size_t size, size_t align);
static ParserFn g_status;
static ParserFn g_effects;
static char* g_fx_json;
static uint64_t g_fx_len;

#define REC 0x130u
#define REC_WORDS 76u
#define MAX_RANGES 8
#define MAX_RECORDS 1500u
#define MIN_RECORDS 100u

static const HfApi* g_api;
static HfMod* g_mod;
static CRITICAL_SECTION g_lock;
static int g_on_game;
static uint32_t g_rng = 0x9e3779b9u;
#define CUSTOM_SQUAD_BASE 0xF0000000u
#define CUSTOM_SQUAD_WAIT 0xFFFFFFFEu
static const char* const SUPPORT_MOD_ID = "local.mod-9fd7880666f6";
static const char* const SUPPORT_NAMES[4] = {
    "local/seaf_squads/patrol", "local/seaf_squads/stealth",
    "local/seaf_squads/riot", "local/seaf_squads/tank"
};
static uint32_t g_support_count;
static uint32_t g_support_types[4];
static int g_support_scanned;
extern "C" int heavy_seaf_squads_init(const HfApi* hf, HfMod* owner);
extern "C" int heavy_squad_pool_init(const HfApi* hf, HfMod* owner);
extern "C" int heavy_seaf_support_loaded(void);
extern "C" int heavy_seaf_squads_ready(void);
extern "C" int heavy_seaf_squads_can_spawn(void);
extern "C" int heavy_seaf_spawn_squad(unsigned squad, const float* position);
extern "C" int heavy_seaf_support_call(unsigned squad, unsigned type, const float* dispatched);
/* squad_pool.cpp: living SEAF troopers the spawn hook has seen, 0 outside a mission. */
extern "C" int heavy_seaf_live_troops(void);
static int g_custom_cap_waiting;
static volatile LONG g_worker_stop;
static int g_squads_tick_joined;

/* ---- Shared 1-unit/tick equip pacing & lock-free QPC state breadcrumbs (zero FlushFileBuffers) ---- */
static unsigned g_equip_budget_remaining = HEAVY_MAX_EQUIPS_PER_TICK;
static HeavyBreadcrumb g_breadcrumbs[64];
static unsigned g_breadcrumb_head;
static unsigned g_breadcrumb_count;
static uint64_t g_qpc_freq;

extern "C" void HeavyEquipBeginFrame(void)
{
    g_equip_budget_remaining = HEAVY_MAX_EQUIPS_PER_TICK;
}

extern "C" int HeavyEquipConsumeOne(void)
{
    if (g_equip_budget_remaining == 0u) return 0;
    --g_equip_budget_remaining;
    return 1;
}

static uint64_t HeavyNowQpc(void)
{
    LARGE_INTEGER li;
    if (g_qpc_freq == 0ULL) {
        LARGE_INTEGER f;
        if (QueryPerformanceFrequency(&f) && f.QuadPart > 0) g_qpc_freq = (uint64_t)f.QuadPart;
        else g_qpc_freq = 10000000ULL;
    }
    if (!QueryPerformanceCounter(&li)) return 0ULL;
    return (uint64_t)li.QuadPart;
}

extern "C" unsigned HeavyBreadcrumbEnter(uint32_t kind, uint32_t shuttle, uint32_t soldier, uint32_t slot)
{
    unsigned idx = g_breadcrumb_head & 63u;
    g_breadcrumb_head = (g_breadcrumb_head + 1u) & 63u;
    if (g_breadcrumb_count < 64u) ++g_breadcrumb_count;
    HeavyBreadcrumb* b = &g_breadcrumbs[idx];
    b->kind = kind;
    b->shuttle = shuttle;
    b->soldier = soldier;
    b->slot = slot;
    b->enter_qpc = HeavyNowQpc();
    b->leave_qpc = 0ULL;
    b->status = 0;
    return idx;
}

extern "C" double HeavyBreadcrumbLeave(unsigned idx, int32_t status)
{
    HeavyBreadcrumb* b = &g_breadcrumbs[idx & 63u];
    b->leave_qpc = HeavyNowQpc();
    b->status = status;
    if (b->leave_qpc <= b->enter_qpc || g_qpc_freq == 0ULL) return 0.0;
    return (double)(b->leave_qpc - b->enter_qpc) * 1000.0 / (double)g_qpc_freq;
}

extern "C" const HeavyBreadcrumb* HeavyBreadcrumbRing(unsigned* count_out, unsigned* head_out)
{
    if (count_out) *count_out = g_breadcrumb_count;
    if (head_out) *head_out = g_breadcrumb_head;
    return g_breadcrumbs;
}

#ifdef HEAVY_SEAF_UNIT_TEST
typedef unsigned char (*HeavyTestSearchFn)(void*, uint32_t, const void*, float*, int);
static HeavyTestSearchFn g_test_search_override;
static HfFn_ActivateStratagemBeacon g_test_activate_override;
static ULONGLONG g_test_now_ms_override;
#endif

static ULONGLONG HeavyNowMs(void)
{
#ifdef HEAVY_SEAF_UNIT_TEST
    if (g_test_now_ms_override) return g_test_now_ms_override;
#endif
    return GetTickCount64();
}

/* 0 keeps the game's cap. 1..5 adds that many squads of 6 (up to 25 in Clone Wars mode -> 162-168 SEAF). */
static int SquadStep(void)
{
    int step = OptionsExtraSquads();
    int max_step = OptionsCloneWars() ? 25 : 5;
    if (step < EXTRA_SQUADS) step = EXTRA_SQUADS;
    if (step < 0) return 0;
    if (step > max_step) return max_step;
    return step;
}

static int SquadStepForDifficulty(int live_diff)
{
    int step = OptionsEffectiveExtraSquads(live_diff);
    int max_step = OptionsEffectiveCloneWars(live_diff) ? 25 : 5;
    if (step < EXTRA_SQUADS) step = EXTRA_SQUADS;
    if (step < 0) return 0;
    if (step > max_step) return max_step;
    return step;
}

static int HeavyIsHostOrSolo(void);

static int HeavyIsMissionFlow(void)
{
    if (g_api == NULL || g_mod == NULL || g_api->game_state == NULL) return 0;
    uint32_t st = g_api->game_state(g_mod);
    return st == HF_STATE_MISSION || st == HF_STATE_PREPARE_MISSION;
}

static void Log(const char* fmt, ...)
{
    char buf[480];
    va_list ap;
    if (g_api == NULL || g_mod == NULL) return;
    va_start(ap, fmt);
    vsnprintf(buf, sizeof buf, fmt, ap);
    va_end(ap);
    g_api->log(g_mod, "%s", buf);
}

static int Rd(uintptr_t address, void* buffer, size_t size)
{
    SIZE_T got = 0;
    if (address < 0x10000) return 0;
    if (!ReadProcessMemory(GetCurrentProcess(), (void*)address, buffer, size, &got)) return 0;
    return got == size;
}

static int ApiRd(uintptr_t address, void* buffer, size_t size)
{
    if (g_api == NULL || address < 0x10000) return 0;
    return g_api->read(address, buffer, size) == 0;
}

static int ApiWr(uintptr_t address, const void* data, size_t size)
{
    if (g_api == NULL || address < 0x10000) return 0;
    return g_api->write(address, data, size) == 0;
}

/* Slots 0 and 1 of a sub-faction planet: main modifier, then its marker. */
static const uint32_t PAIRS[][2] = {
    {1248u, 1249u}, {1377u, 1378u}, {1303u, 1310u}, {1244u, 1386u}, {1243u, 1245u}, {1306u, 1309u},
    {1307u, 1308u}, {1202u, 1203u}, {1360u, 1361u}, {1402u, 1403u}, {1401u, 1400u},
};

static const uint32_t DROP_IDS[] = {1311u, 1308u, 1318u, 1309u};

static int IsPair(uint32_t a, uint32_t b)
{
    size_t i;
    for (i = 0; i < sizeof PAIRS / sizeof PAIRS[0]; i++) {
        if (a == PAIRS[i][0] && b == PAIRS[i][1]) return 1;
    }
    return 0;
}

/* tail: some word after the count must be nonzero (all-zero memory is not a record).
 * wide: the id array may run past 8 entries; the count at +0x80 still names how many are used. */
static int RecOk(const uint32_t* w, int tail, int wide)
{
    uint32_t limit = wide ? 32u : 8u;
    uint32_t used = 0;
    uint32_t k;
    int any = 0;
    if (w[32] > limit) return 0;
    for (k = 0; k < limit; k++) {
        if (w[k] > 3000u) return 0;
        if (w[k] != 0) used++;
    }
    if (w[32] != used) return 0;
    if (!tail) return 1;
    for (k = 33; k < REC_WORDS; k++) {
        if (w[k] != 0) { any = 1; break; }
    }
    return any;
}

static int RecOkAt(uintptr_t address, int tail, int wide)
{
    uint32_t w[REC_WORDS];
    if (!Rd(address, w, REC)) return 0;
    return RecOk(w, tail, wide);
}

typedef struct Range {
    uintptr_t start;
    uint32_t count;
} Range;

static Range g_ranges[MAX_RANGES];
static int g_range_count;
static int g_tail;
static int g_rescan;
static uintptr_t g_tpl;
static int g_squad; /* 0 searching, 1 address ready, 2 applied, 3 gave up */
static int g_squad_tries;
static int g_removed;
static ULONGLONG g_next_squad;

static uint8_t g_verify[400u * REC];

static int VerifyStart(uintptr_t start, uintptr_t region_end, int tail)
{
    uint32_t n;
    uint32_t i;
    uint32_t valid = 0;
    int anchor = 0;
    if (region_end <= start) return 0;
    n = (uint32_t)((region_end - start) / REC);
    if (n > 400u) n = 400u;
    if (n < MIN_RECORDS) return 0;
    if (!Rd(start, g_verify, (size_t)n * REC)) return 0;
    for (i = 0; i < n; i++) {
        const uint32_t* w = (const uint32_t*)(g_verify + (size_t)i * REC);
        if (!RecOk(w, tail, 0)) continue;
        valid++;
        if (!anchor && w[0] != 0 && w[32] >= 2u && IsPair(w[0], w[1])) anchor = 1;
    }
    return anchor && valid >= MIN_RECORDS;
}

static uintptr_t Extend(uintptr_t address, int dir, int tail)
{
    uintptr_t best = address;
    int bad = 0;
    int step;
    for (step = 0; step < 1200; step++) {
        if (dir < 0 && address < REC) break;
        address = (uintptr_t)((intptr_t)address + dir * (intptr_t)REC);
        if (RecOkAt(address, tail, 0)) {
            best = address;
            bad = 0;
        } else if (++bad > 6) {
            break;
        }
    }
    return best;
}

static const uint32_t HINTS[] = {0xFB20u, 0xDF40u, 0x0F58u};

static int SearchTables(Range* out, int cap, int* tail_used)
{
    MEMORY_BASIC_INFORMATION mbi;
    uintptr_t addr = 0x10000;
    int found = 0;
    int pass;
    *tail_used = 1;
    for (pass = 0; pass < 2 && found == 0; pass++) {
        int tail = pass == 0;
        addr = 0x10000;
        while (addr < 0x7ffffffe0000ULL && VirtualQuery((void*)addr, &mbi, sizeof mbi) == sizeof mbi) {
            uintptr_t base = (uintptr_t)mbi.BaseAddress;
            uintptr_t end = base + mbi.RegionSize;
            if (mbi.RegionSize == 0 || end <= addr) {
                addr += 0x10000;
                continue;
            }
            if (mbi.State == MEM_COMMIT && mbi.Type == MEM_PRIVATE && mbi.Protect == PAGE_READWRITE && mbi.RegionSize >= 0x14000) {
                uintptr_t blk;
                for (blk = base & ~(uintptr_t)0xFFFF; blk < end && found < cap; blk += 0x10000) {
                    size_t h;
                    for (h = 0; h < sizeof HINTS / sizeof HINTS[0] && found < cap; h++) {
                        uintptr_t c = blk + HINTS[h];
                        uintptr_t s;
                        uintptr_t e;
                        uint32_t n;
                        int dup;
                        int r;
                        if (c < base || c + (uintptr_t)MIN_RECORDS * REC > end) continue;
                        if (!RecOkAt(c, tail, 0) || !RecOkAt(c + REC, tail, 0)) continue;
                        if (c >= base + REC && RecOkAt(c - REC, tail, 0)) continue;
                        if (!VerifyStart(c, end, tail)) continue;
                        s = Extend(c, -1, tail);
                        e = Extend(c, 1, tail);
                        if (e < s) continue;
                        n = (uint32_t)((e - s) / REC) + 1u;
                        if (n < MIN_RECORDS || n > MAX_RECORDS) continue;
                        dup = 0;
                        for (r = 0; r < found; r++) {
                            if (s >= out[r].start && s < out[r].start + (uintptr_t)out[r].count * REC) dup = 1;
                        }
                        if (dup) continue;
                        out[found].start = s;
                        out[found].count = n;
                        found++;
                        *tail_used = tail;
                    }
                }
            }
            addr = end;
        }
    }
    return found;
}

static int HasId(const uint32_t* w, uint32_t id, uint32_t limit)
{
    uint32_t k;
    if (limit > 32u) limit = 32u;
    for (k = 0; k < limit; k++) {
        if (w[k] == id) return 1;
    }
    return 0;
}

static int Contiguous(const uint32_t* w, uint32_t count)
{
    uint32_t k;
    if (count > 32u) return 0;
    for (k = 0; k < count; k++) {
        if (w[k] == 0) return 0;
    }
    for (k = count; k < 32u; k++) {
        if (w[k] != 0) return 0;
    }
    return 1;
}

static void Publish(const Range* ranges, int count, int tail)
{
    EnterCriticalSection(&g_lock);
    if (count > MAX_RANGES) count = MAX_RANGES;
    memcpy(g_ranges, ranges, (size_t)count * sizeof(Range));
    g_range_count = count;
    g_tail = tail;
    if (count > 0) g_rescan = 0;
    LeaveCriticalSection(&g_lock);
}

static void RequestRescan(void)
{
    EnterCriticalSection(&g_lock);
    g_range_count = 0;
    g_rescan = 1;
    LeaveCriticalSection(&g_lock);
}

static int CopyRanges(Range* out, int* tail)
{
    int n;
    EnterCriticalSection(&g_lock);
    n = g_range_count;
    if (n > MAX_RANGES) n = MAX_RANGES;
    memcpy(out, g_ranges, (size_t)n * sizeof(Range));
    *tail = g_tail;
    LeaveCriticalSection(&g_lock);
    return n;
}

/* Puts 1401 and the map tag 1400 into the record. Drops one low-priority marker per missing slot when the record is full. */
static int WriteModifier(uintptr_t at, const uint32_t* w, uint32_t cap)
{
    uint32_t count = w[32];
    uint32_t want[2];
    uint32_t nwant = 0;
    uint32_t ids[32];
    uint32_t n;
    uint32_t span;
    uint32_t out[32];
    if (count > 32u || cap > 32u || cap == 0) return 0;
    if (!HasId(w, MODIFIER, count)) want[nwant++] = MODIFIER;
    if (!HasId(w, MARKER, count)) want[nwant++] = MARKER;
    if (nwant == 0) return 0;
    if (count > 0 && !Contiguous(w, count)) return 0;
    n = count;
    memcpy(ids, w, (size_t)count * 4u);
    while (n + nwant > cap) {
        int slot = -1;
        size_t d;
        uint32_t k;
        uint32_t drop = 0;
        for (d = 0; d < sizeof DROP_IDS / sizeof DROP_IDS[0] && slot < 0; d++) {
            for (k = 0; k < n; k++) {
                if (ids[k] == DROP_IDS[d]) { slot = (int)k; drop = DROP_IDS[d]; break; }
            }
        }
        if (slot < 0) return 0;
        memmove(ids + slot, ids + slot + 1, (size_t)(n - (uint32_t)slot - 1u) * 4u);
        n--;
        g_removed++;
        if (g_removed <= 8) Log("planet at %llx: removed %u to make room for the SEAF tag", (unsigned long long)at, drop);
    }
    memcpy(ids + n, want, (size_t)nwant * 4u);
    n += nwant;
    memset(out, 0, sizeof out);
    memcpy(out, ids, (size_t)n * 4u);
    span = n > count ? n : count;
    if (!ApiWr(at, out, (size_t)span * 4u)) return 0;
    if (!ApiWr(at + 0x80, &n, 4u)) return 0;
    return 1;
}

static void ApplyRanges(void)
{
    Range ranges[MAX_RANGES];
    int tail = 1;
    int n;
    int ri;
    int wrote = 0;
    int alive = 0;
    int beyond = 0;
    static ULONGLONG last_wrote_log;
    static ULONGLONG last_status;
    ULONGLONG now;
    n = CopyRanges(ranges, &tail);
    if (n <= 0) return;

    /* Eight modifier slots is the layout this build's probe confirmed. A few planets already
     * carry a 9th id; that space is not written. Room is made by dropping a marker id instead. */
    for (uint32_t i = 0; i < ranges[0].count; i++) {
        uint32_t w[REC_WORDS];
        if (!ApiRd(ranges[0].start + (uintptr_t)i * REC, w, REC)) continue;
        if (RecOk(w, tail, 1) && w[32] > 8u) beyond++;
    }
    {
        static int said_beyond;
        if (beyond > 0 && !said_beyond) {
            said_beyond = 1;
            Log("%d planet record(s) already have more than 8 modifiers; those are left as they are", beyond);
        }
    }

    for (ri = 0; ri < n; ri++) {
        uint32_t i;
        uint32_t valid = 0;
        uint32_t have = 0;
        int anchor = 0;
        const int wide = 0;
        for (i = 0; i < ranges[ri].count; i++) {
            uint32_t w[REC_WORDS];
            uintptr_t at = ranges[ri].start + (uintptr_t)i * REC;
            if (!ApiRd(at, w, REC) || !RecOk(w, tail, wide)) continue;
            valid++;
            if (HasId(w, MODIFIER, w[32]) && HasId(w, MARKER, w[32])) have++;
            if (!anchor && w[0] != 0 && w[32] >= 2u && IsPair(w[0], w[1])) anchor = 1;
        }
        if (!anchor || valid < MIN_RECORDS) {
            Log("planet table at %llx no longer checks out (%u of %u records, anchor %s): searching again",
                (unsigned long long)ranges[ri].start, valid, ranges[ri].count, anchor ? "present" : "missing");
            RequestRescan();
            return;
        }
        alive++;
        if (have >= valid) continue;
        for (i = 0; i < ranges[ri].count; i++) {
            uint32_t w[REC_WORDS];
            uintptr_t at = ranges[ri].start + (uintptr_t)i * REC;
            if (!ApiRd(at, w, REC) || !RecOk(w, tail, wide)) continue;
            if (w[32] > 8u) continue;
            if (HasId(w, MODIFIER, w[32]) && HasId(w, MARKER, w[32])) continue;
            wrote += WriteModifier(at, w, 8u);
        }
    }

    now = GetTickCount64();
    if (wrote > 0 && (last_wrote_log == 0 || now - last_wrote_log > 10000)) {
        last_wrote_log = now;
        Log("modifier %u and map tag %u written into %d planet record(s) across %d table copy(ies)", MODIFIER, MARKER, wrote, alive);
    }
    if (alive > 0 && (last_status == 0 || now - last_status > 120000)) {
        last_status = now;
        Log("status: %d planet table copy(ies), 8 modifier slots, %d id(s) removed to make room", alive, g_removed);
    }
}

/* SEAF template: 6,6,6, then 12,18, then difficulty factors 0.25,0.25,0.5,1,1, admission 10,18,
 * and 1.6 four times at +80. The four written numbers are the 12, the 18, the 10 and the 18. */
static uint8_t g_head[40];
static uint8_t g_tailb[16];
static int g_sig;

static void InitSig(void)
{
    uint32_t u[5] = {6u, 6u, 6u, 12u, 18u};
    float f[5] = {0.25f, 0.25f, 0.5f, 1.0f, 1.0f};
    float b[4] = {1.6f, 1.6f, 1.6f, 1.6f};
    memcpy(g_head, u, 20);
    memcpy(g_head + 20, f, 20);
    memcpy(g_tailb, b, 16);
    g_sig = 1;
}

static int TemplateAt(const uint8_t* p)
{
    uint32_t a;
    uint32_t b;
    if (!g_sig) InitSig();
    if (memcmp(p, g_head, 40) != 0) return 0;
    if (memcmp(p + 80, g_tailb, 16) != 0) return 0;
    memcpy(&a, p + 40, 4);
    memcpy(&b, p + 44, 4);
    return a == 10u && b == 18u;
}

static uint8_t g_chunk[0x40000 + 96];

static int ScanProtect(DWORD protect, uintptr_t* hits, int max_hits)
{
    MEMORY_BASIC_INFORMATION mbi;
    uintptr_t addr = 0x10000;
    int n = 0;
    while (n < max_hits && addr < 0x7ffffffe0000ULL && VirtualQuery((void*)addr, &mbi, sizeof mbi) == sizeof mbi) {
        uintptr_t base = (uintptr_t)mbi.BaseAddress;
        uintptr_t end = base + mbi.RegionSize;
        DWORD p = mbi.Protect;
        if (mbi.RegionSize == 0 || end <= addr) {
            addr += 0x10000;
            continue;
        }
            if (mbi.State == MEM_COMMIT && mbi.Type == MEM_PRIVATE && !(p & PAGE_GUARD) && (p & 0xFFu) == protect &&
            mbi.RegionSize >= 96 && mbi.RegionSize <= 0x8000000ULL) {
            uintptr_t pos = base;
            while (pos < end && n < max_hits) {
                size_t room = (size_t)(end - pos);
                size_t take = room > 0x40000 ? (size_t)0x40000 : room;
                size_t extra = 0;
                size_t gotn;
                size_t i;
                if (room > take) {
                    extra = room - take;
                    if (extra > 96) extra = 96;
                }
                gotn = take + extra;
                if (gotn < 96) break;
                if (!Rd(pos, g_chunk, gotn)) break;
                for (i = 0; i + 96 <= gotn && i < take && n < max_hits; i += 4) {
                    if (TemplateAt(g_chunk + i)) hits[n++] = pos + i;
                }
                if (take >= room) break;
                pos += take;
            }
        }
        addr = end;
    }
    return n;
}

static uintptr_t FindTemplate(int allow_write)
{
    uintptr_t hits[2];
    int n = ScanProtect(PAGE_READONLY, hits, 2);
    if (n == 1) return hits[0];
    if (n > 1) return 1; /* more than one: caller treats any value below 0x10000 except 0 as "refuse" */
    if (!allow_write) return 0;
    n = ScanProtect(PAGE_READWRITE, hits, 2);
    if (n == 1) return hits[0];
    if (n > 1) return 1;
    return 0;
}

static int g_applied_step = 0;

static int TemplateFramingMatches(const uint8_t* p)
{
    if (!g_sig) InitSig();
    if (memcmp(p, g_head, 12) != 0) return 0;
    if (memcmp(p + 20, g_head + 20, 20) != 0) return 0;
    if (memcmp(p + 80, g_tailb, 16) != 0) return 0;
    return 1;
}

static void SyncTemplateForStep(uintptr_t at, int want_step)
{
    uint8_t raw[96];
    uint32_t want_add;
    uint32_t prev_add;
    static const uint32_t expect[4] = {12u, 18u, 10u, 18u};
    uint32_t gotv[4];
    static const uintptr_t OFFS[4] = {12u, 16u, 40u, 44u};
    int i;
    int matches_prev = 1;
    int matches_want = 1;

    if (want_step < 0) want_step = 0;
    if (want_step > 25) want_step = 25;

    EnterCriticalSection(&g_lock);
    int cur_applied = g_applied_step;
    int cur_squad = g_squad;
    LeaveCriticalSection(&g_lock);

    if (want_step == cur_applied && ((want_step == 0 && cur_squad == 1) || (want_step > 0 && cur_squad == 2)))
        return;

    if (!ApiRd(at, raw, 96) || !TemplateFramingMatches(raw)) {
        Log("SEAF cap not changed: the template at %llx does not match the game's framing bytes", (unsigned long long)at);
        EnterCriticalSection(&g_lock);
        g_squad = 3;
        g_tpl = 0;
        g_applied_step = 0;
        LeaveCriticalSection(&g_lock);
        return;
    }

    want_add = (uint32_t)want_step * 6u;
    prev_add = (uint32_t)cur_applied * 6u;
    for (i = 0; i < 4; i++) {
        memcpy(&gotv[i], raw + OFFS[i], 4);
        if (gotv[i] != expect[i] + prev_add) matches_prev = 0;
        if (gotv[i] != expect[i] + want_add) matches_want = 0;
    }

    if (matches_want) {
        EnterCriticalSection(&g_lock);
        g_applied_step = want_step;
        g_squad = (want_step > 0) ? 2 : 1;
        LeaveCriticalSection(&g_lock);
        return;
    }

    if (!matches_prev) {
        Log("SEAF cap left alone: template %llx holds %u,%u / %u,%u, not expected %u,%u / %u,%u",
            (unsigned long long)at, gotv[0], gotv[1], gotv[2], gotv[3],
            expect[0] + prev_add, expect[1] + prev_add, expect[2] + prev_add, expect[3] + prev_add);
        EnterCriticalSection(&g_lock);
        g_squad = 3;
        LeaveCriticalSection(&g_lock);
        return;
    }

    for (i = 0; i < 4; i++) {
        if (g_api->write_u32_if == NULL ||
            g_api->write_u32_if(g_mod, at + OFFS[i], gotv[i], expect[i] + want_add) != 0) {
            int j;
            for (j = 0; j < i && g_api->write_u32_if != NULL; j++) {
                (void)g_api->write_u32_if(g_mod, at + OFFS[j], expect[j] + want_add, gotv[j]);
            }
            Log("SEAF cap: write %u at template+%u did not apply", expect[i] + want_add, (unsigned)OFFS[i]);
            EnterCriticalSection(&g_lock);
            g_squad = 3;
            LeaveCriticalSection(&g_lock);
            return;
        }
    }

    EnterCriticalSection(&g_lock);
    g_applied_step = want_step;
    g_squad = (want_step > 0) ? 2 : 1;
    LeaveCriticalSection(&g_lock);

    if (want_step == 0) {
        Log("SEAF cap restored to native defaults 12/18, admission 10/18 (template %llx)", (unsigned long long)at);
    } else {
        Log("SEAF cap raised by %d squad(s): per drop %u/%u, admission %u/%u (template %llx)",
            want_step, expect[0] + want_add, expect[1] + want_add, expect[2] + want_add, expect[3] + want_add, (unsigned long long)at);
    }
}

static int WorkerStopped(void)
{
    return InterlockedCompareExchange(&g_worker_stop, 0, 0) != 0;
}

static int SleepWorkerMs(DWORD total_ms)
{
    DWORD waited = 0;
    while (waited < total_ms) {
        if (WorkerStopped()) return 1;
        DWORD slice = (total_ms - waited > 100u) ? 100u : (total_ms - waited);
        Sleep(slice);
        waited += slice;
    }
    return WorkerStopped();
}

static DWORD WINAPI Worker(LPVOID unused)
{
    int misses = 0;
    (void)unused;
    if (SleepWorkerMs(5000)) return 0;
    while (!WorkerStopped()) {
        int need;
        int squad;
        int tries;
        uintptr_t tpl;
        if (HeavyIsMissionFlow()) {
            /* Pause VirtualQuery/ReadProcessMemory heap scans while preparing or fighting a mission. */
            if (SleepWorkerMs(2000)) return 0;
            continue;
        }
        EnterCriticalSection(&g_lock);
        need = g_rescan || g_range_count == 0;
        squad = g_squad;
        tries = g_squad_tries;
        tpl = g_tpl;
        LeaveCriticalSection(&g_lock);

        if (need) {
            Range found[MAX_RANGES];
            int tail = 1;
            int n = SearchTables(found, MAX_RANGES, &tail);
            if (n > 0) {
                int i;
                misses = 0;
                Publish(found, n, tail);
                Log("planet table: %d copy(ies), record check %s", n, tail ? "strict" : "without the trailing-data requirement");
                for (i = 0; i < n; i++) {
                    Log("  copy %d at %llx, %u records", i + 1, (unsigned long long)found[i].start, found[i].count);
                }
            } else {
                misses++;
                if (misses == 1 || (misses % 4) == 0) {
                    Log("planet table not found yet (try %d). Nothing is written until a table checks out.", misses);
                }
                if (SleepWorkerMs(13000)) return 0;
            }
        }

        if (!g_on_game) ApplyRanges();

        if (SquadStep() > 0 && squad == 0 && tpl == 0 && GetTickCount64() >= g_next_squad) {
            int late = tries >= 4;
            uintptr_t at = FindTemplate(late);
            g_next_squad = GetTickCount64() + 15000;
            EnterCriticalSection(&g_lock);
            if (g_squad == 0 && g_tpl == 0) {
                if (at > 0x10000) {
                    g_tpl = at;
                    g_squad = 1;
                    Log("SEAF template found at %llx", (unsigned long long)at);
                } else if (at == 1) {
                    g_squad = 3;
                    Log("SEAF cap not changed: more than one SEAF template was found");
                } else {
                    g_squad_tries++;
                    if (g_squad_tries == 1 || g_squad_tries == 4) {
                        Log("SEAF template not found yet (try %d)", g_squad_tries);
                    }
                    if (g_squad_tries >= 12) {
                        g_squad = 3;
                        Log("SEAF cap not changed: the SEAF template was not found");
                    }
                }
            }
            LeaveCriticalSection(&g_lock);
        } else if (SquadStep() > 0 && (squad == 1 || squad == 2) && tpl > 0x10000 && g_on_game == 0) {
            SyncTemplateForStep(tpl, HeavyIsHostOrSolo() ? SquadStepForDifficulty(0) : 0);
        }

        if (SleepWorkerMs(2000)) return 0;
    }
    return 0;
}

/* The mission director on this build (game.dll+0x3326d10). Its tag list is what starts the SEAF
 * logic; the roster at +0x4fd78 is the squad that logic spawns. HellForge never fills either,
 * because it has no effect definition at mission setup. The squad definition and role table are
 * the game's own, checked before anything is written. */
#define DIRECTOR_RVA 0x3326d10u
#define SEAF_TAG 0xba586f51u
#define SEAF_DEF_RVA 0x32d4d10u
#define SEAF_ROLE_RVA 0x32d4e40u

static int SquadEntry(uintptr_t game, unsigned char* entry)
{
    unsigned char def[0x118];
    uint32_t rows = 0;
    uint32_t mark = 0;
    uintptr_t at = game + SEAF_DEF_RVA;
    float one = 1.0f;
    int i;
    if (!ApiRd(at, def, sizeof def)) return 0;
    memcpy(&mark, def + 0x60, 4);
    if (mark != SEAF_TAG) return 0;
    for (i = 0; i < 8; i++) {
        uint32_t role = 0;
        uint32_t troopers = 0;
        int found = 0;
        int r;
        memcpy(&role, def + i * 12, 4);
        memcpy(&troopers, def + i * 12 + 4, 4);
        if (role == 0) continue;
        if (troopers == 0 || troopers > 12) return 0;
        for (r = 0; r < 24 && !found; r++) {
            uint32_t rrole = 0;
            uint32_t rtag = 0;
            if (!ApiRd(game + SEAF_ROLE_RVA + (uintptr_t)r * 128u, &rrole, 4)) continue;
            if (!ApiRd(game + SEAF_ROLE_RVA + (uintptr_t)r * 128u + 0x58u, &rtag, 4)) continue;
            if (rrole == role && rtag == SEAF_TAG) found = 1;
        }
        if (!found) return 0;
        rows++;
    }
    if (rows == 0) return 0;
    memset(entry, 0, 0xd8);
    memcpy(entry, def, 0x60);
    memcpy(entry + 0x60, &rows, 4);
    memcpy(entry + 0xa8, &at, 8);
    memcpy(entry + 0xb0, def + 0x100, 8);
    memcpy(entry + 0xb8, def + 0xfc, 4);
    memcpy(entry + 0xc0, &one, 4);
    memcpy(entry + 0xc4, def + 0x108, 12);
    return 1;
}

static int ArmMission(void)
{
    uintptr_t game;
    uintptr_t ai = 0;
    uint32_t count = 0;
    uint32_t tcount = 0;
    uint32_t tags[16];
    uint32_t i;
    int have = 0;
    unsigned char entry[0xd8];
    game = g_api->module_base("game.dll", NULL);
    if (game == 0 || !ApiRd(game + DIRECTOR_RVA, &ai, 8) || ai < 0x10000) {
        Log("mission: the director is not ready yet");
        return 0;
    }
    if (!ApiRd(ai + 0x518acu, &count, 4) || count > 32u || !ApiRd(ai + 0x51914u, &tcount, 4) || tcount > 16u || !ApiRd(ai + 0x518d4u, tags, sizeof tags)) {
        Log("mission: the director at %llx does not have the expected roster", (unsigned long long)ai);
        return 1;
    }
    for (i = 0; i < tcount; i++) if (tags[i] == SEAF_TAG) have = 1;
    Log("mission: director %llx, %u tag(s), SEAF tag %s, squad roster %u", (unsigned long long)ai, tcount, have ? "present" : "absent", count);
    if (count == 0) {
        if (!SquadEntry(game, entry)) {
            Log("mission: the game's SEAF squad definition did not check out; the roster was left empty");
            return 1;
        }
        count = 1;
        if (!ApiWr(ai + 0x4fd78u, entry, sizeof entry) || !ApiWr(ai + 0x518acu, &count, 4)) {
            Log("mission: could not fill the SEAF roster");
            return 1;
        }
        Log("mission: SEAF squad roster filled from the game's own definition");
    }
    if (!have) {
        uint32_t tag = SEAF_TAG;
        uint32_t n = tcount + 1u;
        if (tcount >= 16u) { Log("mission: the tag list is full; the SEAF tag was not added"); return 1; }
        if (!ApiWr(ai + 0x518d4u + tcount * 4u, &tag, 4) || !ApiWr(ai + 0x51914u, &n, 4)) {
            Log("mission: could not add the SEAF tag");
            return 1;
        }
        Log("mission: SEAF tag added to the director");
    }
    return 1;
}

/* One SEAF shuttle beside the local player, through the game's own SEAF delivery (stratagem 116).
 * The route is the one checked on this build: site search game.dll+0x6a8f00, dispatcher game.dll+0x6abdb0.
 * Both are checked by their first bytes, and the call is refused when the squad roster is empty. */
static int CodeIs(uintptr_t at, const unsigned char* expect, size_t n)
{
    unsigned char b[64];
    return n <= sizeof b && ApiRd(at, b, n) && memcmp(b, expect, n) == 0;
}

static int MapFind(uintptr_t table, uint32_t buckets, uint32_t empty, uint32_t mult, uint32_t key, uint32_t* index)
{
    uint32_t n;
    if (table == 0 || buckets == 0 || buckets > 65536u || (buckets & (buckets - 1u)) != 0) return 0;
    for (n = 0; n < buckets; n++) {
        uint32_t pair[2];
        if (!ApiRd(table + (uintptr_t)((key * mult + n) & (buckets - 1u)) * 8u, pair, 8)) return 0;
        if (pair[0] == key) { *index = pair[1]; return pair[1] != 0xffffffffu; }
        if (pair[0] == empty) return 0;
    }
    return 0;
}

static int Finite3(const float* p)
{
    int i;
    for (i = 0; i < 3; i++) if (p[i] != p[i] || p[i] > 99900.0f || p[i] < -99900.0f) return 0;
    return 1;
}

/* The source mod registers the four squad call-ins but cannot deliver them on this build (its
 * game-thread subscription is refused), so its stratagem types are captured here and the
 * delivery is done by this mod. It stays an explicit prerequisite for the rosters. */
static void FindSupportSquads(HfMod* mod)
{
    uint32_t i;
    if (g_support_scanned || g_api->version < 7 || g_api->content == NULL) return;
    g_support_scanned = 1;
    for (i = 0; i < 4096; i++) {
        HfContent item;
        uint32_t n;
        memset(&item, 0, sizeof item);
        item.size = (uint32_t)sizeof item;
        if (!g_api->content(mod, i, &item)) break;
        if (item.kind != HF_KIND_STRATAGEM || item.mod == NULL || item.name == NULL || strcmp(item.mod, SUPPORT_MOD_ID) != 0) continue;
        for (n = 0; n < 4; n++) {
            if (strcmp(item.name, SUPPORT_NAMES[n]) != 0) continue;
            if (g_support_types[n] == 0) {
                if (item.id == 0 || item.id > 0xffffffffull) break;
                g_support_types[n] = (uint32_t)item.id;
                g_support_count++;
            }
            break;
        }
    }
    g_rng ^= (uint32_t)GetTickCount64();
    if (g_rng == 0) g_rng = 0x6d2b79f5u;
    Log("SEAF Squad Stratagems dependency: %s (%u of 4 squad call-ins detected)", g_support_count == 4 ? "loaded" : "missing or incomplete", g_support_count);
}

/* 1 while the original SEAF Squad Stratagems mod is installed and enabled: the
 * squad pool adds its compositions on the condition that it is present. */
extern "C" int heavy_seaf_support_loaded(void)
{
    return g_support_scanned && g_support_count == 4;
}

static uint32_t WaveCallTypeFor(int support_squads)
{
    if (!support_squads) return 116u;
    if (g_support_count != 4 || !heavy_seaf_squads_ready()) return 116u;
    if (!heavy_seaf_squads_can_spawn()) {
        if (!g_custom_cap_waiting) { g_custom_cap_waiting = 1; Log("custom SEAF limit reached (18 living troops); wave will resume after casualties"); }
        return CUSTOM_SQUAD_WAIT;
    }
    g_custom_cap_waiting = 0;
    g_rng ^= g_rng << 13;
    g_rng ^= g_rng >> 17;
    g_rng ^= g_rng << 5;
    return CUSTOM_SQUAD_BASE + (g_rng % 4u);
}

static float g_wave_site[6][3];
static int g_wave_sites;

static int FarFromWave(const float* g)
{
    int i;
    for (i = 0; i < g_wave_sites; i++) {
        float dx = g[0] - g_wave_site[i][0];
        float dy = g[1] - g_wave_site[i][1];
        if (dx * dx + dy * dy < 400.0f) return 0;
    }
    return 1;
}

static int HeavyHasEntityHeadroom(uintptr_t game, uint32_t min_free)
{
    static uint8_t s_em_desc_buf[2048u * 24u];
    uintptr_t factory = 0;
    uint32_t invalid = 0xffffffffu;
    uint32_t free_slots = 0u;
    if (game == 0 || !ApiRd(game + 0x346bf98u, &factory, 8) || factory < 0x10000u) return 0;
    if (!ApiRd(game + 0x348456cu, &invalid, 4)) return 0;
    if (!ApiRd(factory + 0xf32f18u, s_em_desc_buf, sizeof s_em_desc_buf)) return 0;
    for (uint32_t i = 0; i < 2048u; i++) {
        uint64_t res = 0;
        uint32_t eid = 0;
        const uint8_t* slot = s_em_desc_buf + (size_t)i * 24u;
        memcpy(&res, slot + 0u, 8);
        memcpy(&eid, slot + 8u, 4);
        if (res == 0ULL || eid == 0u || eid == 0xffffffffu || eid == invalid) {
            ++free_slots;
        }
    }
    return free_slots >= min_free;
}

static int ResolveLivingHostMissionGround(uintptr_t game, uint32_t* out_owner, uint32_t* out_character, float out_origin[3])
{
    uintptr_t context = 0, settings = 0, section = 0, players = 0, player = 0, factory = 0, table = 0, source = 0, rows = 0;
    uint32_t mode = 0, invalid = 0, pcount = 0, local = 0, s = 0, map[3], at = 0;
    uint64_t local_peer = 0, user_ptr = 0;
    uint32_t owner = 0, character = 0;
    float origin[3];
    int picked = 0;

    if (game == 0) return 0;
    if (!ApiRd(game + 0x3326340u, &context, 8) || context == 0 || !ApiRd(context + 0xae288u, &settings, 8) || settings == 0 ||
        !ApiRd(settings + 0x1e4u, &mode, 4) || mode != 3u || !ApiRd(settings + 0x2c8u, &section, 8) || section == 0)
        return 0;
    if (!ApiRd(game + 0x3483c34u, &invalid, 4)) return 0;
    if (!ApiRd(game + 0x3326468u, &players, 8) || players == 0 || !ApiRd(players + 0x84u, &pcount, 4) ||
        pcount == 0 || pcount > 4u || !ApiRd(players + 0x88u, &local, 4) || local == 0 || local > 4u ||
        !ApiRd(game + 0x346bf98u, &factory, 8) || factory == 0 ||
        !ApiRd(factory + 0xf22ec8u, &table, 8) || !ApiRd(factory + 0xf22ed0u, map, 12))
        return 0;
    if (ApiRd(game + 0x347cef0u, &user_ptr, 8) && user_ptr >= 0x10000u)
        ApiRd((uintptr_t)user_ptr + 0xb398u, &local_peer, 8);
    for (s = 0; s < pcount && !picked; s++) {
        uint64_t speer = 0;
        uint32_t slife = 0, snet = 0, sowner = 0, sidx = 0, ptable_idx = 0;
        uintptr_t ptable = 0;
        uint32_t pmap[3];
        uint32_t char_id = 0, chk = 0;
        if (!ApiRd(players + 0x2c8u + (uintptr_t)s * 0x38u, &speer, 8)) continue;
        if (local_peer != 0 && speer != local_peer) continue;
        if (!ApiRd(players + 0x2e0u + (uintptr_t)s * 0x38u, &slife, 4) || slife != 3u) continue;
        if (!ApiRd(players + 0x3a8u + (uintptr_t)s * 0x20u, &snet, 4) || snet == 0x7fffu) continue;
        if (!ApiRd(players + 0xe8u + (uintptr_t)s * 8u, &player, 8) || player == 0 || !ApiRd(player + 8u, &sowner, 4)) continue;
        if (!ApiRd(players + 0xd0u, &ptable, 8) || !ApiRd(players + 0xd8u, pmap, 12) ||
            !MapFind(ptable, pmap[0], pmap[1], pmap[2], sowner, &ptable_idx) || ptable_idx >= 4u) continue;
        if (!MapFind(table, map[0], map[1], map[2], snet, &sidx) || sidx >= 2048u) continue;
        if (!ApiRd(factory + 0xf32f18u + (uintptr_t)sidx * 24u + 8u, &char_id, 4) ||
            !ApiRd(factory + 0xf32f18u + (uintptr_t)sidx * 24u + 16u, &chk, 4) ||
            chk != snet || char_id == 0xffffffffu || char_id == invalid) continue;
        owner = sowner;
        character = char_id;
        picked = 1;
    }
    if (!picked) return 0;
    if (!ApiRd(game + 0x3326558u, &source, 8) || source == 0 || !ApiRd(source + 0x48a0u, &table, 8) || !ApiRd(source + 0x48a8u, map, 12) ||
        !MapFind(table, map[0], map[1], map[2], character, &at) || at >= 65536u || !ApiRd(source + 0x48d0u, &rows, 8) || rows == 0 ||
        !ApiRd(rows + (uintptr_t)at * 0xa4u, origin, 12) || !Finite3(origin))
        return 0;

    /* If HealthManager is populated for this character entity, confirm hp > 0 and life < 2 */
    {
        uintptr_t hm = 0, htable = 0, hrows = 0;
        uint32_t hmap[3], hidx = 0, hcap = 0;
        if (ApiRd(game + 0x3326688u, &hm, 8) && hm >= 0x10000u &&
            ApiRd(hm + 0x1020u, &hcap, 4) && hcap > 0 && hcap <= 65536u &&
            ApiRd(hm + 0x1030u, &htable, 8) && ApiRd(hm + 0x1038u, hmap, 12) &&
            MapFind(htable, hmap[0], hmap[1], hmap[2], character, &hidx) && hidx < hcap &&
            ApiRd(hm + 0x1058u, &hrows, 8) && hrows >= 0x10000u) {
            int32_t hp = 0;
            uint32_t hlife = 0;
            if (ApiRd(hrows + (uintptr_t)hidx * 0x1b8u + 0x14u, &hp, 4) &&
                ApiRd(hrows + (uintptr_t)hidx * 0x1b8u + 0x19cu, &hlife, 4)) {
                if (hp <= 0 || hlife >= 2u) return 0;
            }
        }
    }

    if (out_owner) *out_owner = owner;
    if (out_character) *out_character = character;
    if (out_origin) memcpy(out_origin, origin, 12);
    return 1;
}

/* 0 sent, 1 the player is not on the ground yet, 2 this build's call-in code does not match, 3 no landing site. */
static int CallSeaf(uintptr_t game, int squad, uint32_t call_type, const float* beacon)
{
    static const unsigned char SEARCH[] = { 0x40,0x55,0x53,0x56,0x57,0x41,0x54,0x41,0x55,0x41,0x56,0x48,0x8d,0x6c,0x24,0x80,0x48,0x81,0xec,0x80,0x01,0x00,0x00,0x48,0x8b,0x05,0xf2,0x30,0xf9,0x01 };
    static const unsigned char QUERY[] = { 0x48,0x8b,0xc4,0x48,0x89,0x58,0x20,0x55,0x56,0x57,0x41,0x54,0x41,0x55,0x41,0x56,0x41,0x57,0x48,0x8d,0xa8,0x48,0xfe,0xff,0xff };
    static const unsigned char DISPATCH[] = { 0x48,0x89,0x5c,0x24,0x18,0x55,0x56,0x57,0x41,0x54,0x41,0x55,0x41,0x56,0x41,0x57,0x48,0x8d,0xac,0x24,0xb0,0xfc,0xff,0xff,0x48,0x81,0xec,0x50,0x04,00,0x00,0x48,0x8b,0x05,0x3a,0x02,0xf9,0x01 };
    static const char DISPATCH_PAT[] = "48 89 5C 24 18 55 56 57 41 54 41 55 41 56 41 57 48 8D AC 24 B0 FC FF FF 48 81 EC 50 04 00 00 48 8B 05 3A 02 F9 01";
    static unsigned char controller[0x8e8];
    static unsigned char front[0x8a0];
    static unsigned char mgr_raw[144];
    unsigned char* mgr_copy;
    unsigned char src[60];
    unsigned char def[0x130];
    unsigned char d[38];
    uintptr_t record = 0, manager = 0, world = 0, ctl, ai = 0;
    uint32_t owner = 0, character = 0, invalid = 0, type = 116, u, roster = 0;
    float origin[3], approach[3];
    int ok = 0;
    int ring, ang, hz;
    if (call_type == CUSTOM_SQUAD_WAIT) return 1;
    mgr_copy = (unsigned char*)(((uintptr_t)mgr_raw + 15u) & ~(uintptr_t)15u);
    if (!CodeIs(game + 0x6a8f00u, SEARCH, sizeof SEARCH) || !CodeIs(game + 0x13caa80u, QUERY, sizeof QUERY)) return 2;
    {
        int dispatch_ok = (g_api->size >= offsetof(HfApi, code_matches) + sizeof g_api->code_matches &&
                           g_api->code_matches != NULL && g_api->code_matches(game + 0x6abdb0u, DISPATCH_PAT));
        if (!dispatch_ok) {
            if (!ApiRd(game + 0x6abdb0u, d, sizeof d) ||
                (memcmp(d, DISPATCH, sizeof d) != 0 && !(d[0] == 0xE9 && memcmp(d + 5, DISPATCH + 5, sizeof d - 5) == 0)))
                return 2;
        }
    }
    if (!ApiRd(game + 0x37cb600u + 116u * 8u, &record, 8) || record == 0 || !ApiRd(record, def, sizeof def)) return 2;
    memcpy(&u, def + 0, 4); if (u != 116u) return 2;
    memcpy(&u, def + 0x3c, 4); if (u != 6u) return 2;
    memcpy(&u, def + 0x78, 4); if (u != 0xc0u) return 2;
    memcpy(&u, def + 0x12c, 4); if (u != 0) return 2;
    if (call_type == 116u && (!ApiRd(game + DIRECTOR_RVA, &ai, 8) || ai < 0x10000 || !ApiRd(ai + 0x518acu, &roster, 4) || roster == 0)) return 1;
    if (!ApiRd(game + 0x3483c34u, &invalid, 4)) return 2;
    if (!ResolveLivingHostMissionGround(game, &owner, &character, origin)) return 1;
    if (beacon != NULL && Finite3(beacon)) memcpy(origin, beacon, 12);
    if (squad > 0) {
        int live_diff_cw = (g_api != NULL) ? cd_read_live_mission_difficulty(g_api->read, game) : 0;
        int cw = (beacon == NULL && OptionsEffectiveCloneWars(live_diff_cw));
        float spread = cw ? 58.0f : 30.0f;
        float step_ang = cw ? 1.5707963f : 2.0944f;
        origin[0] += spread * cosf((float)squad * step_ang);
        origin[1] += spread * sinf((float)squad * step_ang);
    }
    if (!ApiRd(game + 0x33266b0u, &manager, 8) || manager == 0 || !ApiRd(manager, mgr_copy, 128)) return 2;
    memcpy(&world, mgr_copy + 8, 8);
    if (world == 0) return 2;
    {
        static const float dz[5] = { 0.0f, 3.0f, -3.0f, 6.0f, -6.0f };
        static const float ringd_std[3] = { 0.0f, 15.0f, 30.0f };
        static const float ringd_cw[5] = { 0.0f, 20.0f, 45.0f, 80.0f, 120.0f };
        int live_diff_rings = (g_api != NULL) ? cd_read_live_mission_difficulty(g_api->read, game) : 0;
        int cw_rings = (beacon == NULL && OptionsEffectiveCloneWars(live_diff_rings));
        const float* ringd = cw_rings ? ringd_cw : ringd_std;
        int n_rings = cw_rings ? 5 : 3;
        typedef unsigned char (*SearchFn)(void*, uint32_t, const void*, float*, int);
        SearchFn search_fn = (SearchFn)(game + 0x6a8f00u);
#ifdef HEAVY_SEAF_UNIT_TEST
        if (g_test_search_override) search_fn = g_test_search_override;
#endif
        for (ring = 0; ring < n_rings && !ok; ring++)
        for (ang = 0; ang < (ring ? 8 : 1) && !ok; ang++)
        for (hz = 0; hz < 5 && !ok; hz++) {
            float o[3];
            o[0] = origin[0] + ringd[ring] * cosf((float)ang * 0.7853982f);
            o[1] = origin[1] + ringd[ring] * sinf((float)ang * 0.7853982f);
            o[2] = origin[2] + dz[hz];
            memset(controller, 0, sizeof controller);
            ApiRd(manager, mgr_copy, 128);
            ctl = (uintptr_t)controller;
            memcpy(mgr_copy + 0x68, &ctl, 8);
            memcpy(controller + 0x30, o, 12);
            memset(src, 0, sizeof src);
            memcpy(src, &type, 4);
            memcpy(src + 8, &invalid, 4);
            memcpy(src + 12, &character, 4);
            memcpy(src + 16, o, 12);
            memcpy(src + 28, &invalid, 4);
            memcpy(approach, o, 12);
            if (search_fn(mgr_copy, 0, src, approach, 1) && Finite3(approach) && Finite3((const float*)(controller + 0x650)) && FarFromWave((const float*)(controller + 0x650))) ok = 1;
        }
    }
    if (!ok) return 3;
    if (g_wave_sites < 6) memcpy(g_wave_site[g_wave_sites++], controller + 0x650, 12);
    memcpy(front, controller + 0x40, sizeof front);
    memcpy(front + 0x3a4, &character, 4);
    memcpy(front + 0x3b4, &invalid, 4);
    memcpy(front + 0x3f8, &invalid, 4);
    if (call_type >= CUSTOM_SQUAD_BASE) {
        if (!heavy_seaf_spawn_squad(call_type - CUSTOM_SQUAD_BASE, (const float*)(controller + 0x650))) return 1;
    } else {
        /* Disassembled game.dll+0x6ABDB0 (1,564 instructions) + SDK 16 ActivateDetour:
         * Must pass full 10-argument HfFn_ActivateStratagemBeacon signature so [rsp+0x38..0x50]
         * (s7..s10) are deterministically zeroed across all hooked detours. */
        HfFn_ActivateStratagemBeacon dispatch_fn =
            (g_api->size >= offsetof(HfApi, hook_function) + sizeof(void*) && g_api->function != NULL)
                ? hf_fn_ActivateStratagemBeacon(g_mod)
                : NULL;
        if (dispatch_fn == NULL) dispatch_fn = (HfFn_ActivateStratagemBeacon)(game + 0x6abdb0u);
#ifdef HEAVY_SEAF_UNIT_TEST
        if (g_test_activate_override) dispatch_fn = g_test_activate_override;
#endif
        unsigned bc = HeavyBreadcrumbEnter(HEAVY_BC_CALL_SEAF, 0u, character, call_type);
        (void)dispatch_fn((void*)manager, -1, owner, approach, front, call_type, 0ULL, 0ULL, 0ULL, 0ULL);
        HeavyBreadcrumbLeave(bc, 1);
    }
    return 0;
}

/* Logs a call-in that held the game thread long enough to be felt as a hitch. */
static int TimedCallSeaf(uintptr_t game, int squad, uint32_t call_type, const float* beacon)
{
    ULONGLONG at = GetTickCount64();
    int rc = CallSeaf(game, squad, call_type, beacon);
    ULONGLONG took = GetTickCount64() - at;
    if (took >= 200)
        Log("SEAF call-in: squad %d took %llu ms (%s)", squad + 1, (unsigned long long)took,
            rc == 0 ? "sent" : rc == 1 ? "not on the ground yet" : rc == 2 ? "call-in code mismatch" : "no landing site");
    return rc;
}

static uint32_t g_call_type;
static int g_call_ready;

static void RegisterCallIn(void)
{
    HfStratagem call;
    memset(&call, 0, sizeof call);
    call.size = (uint32_t)sizeof call;
    call.base = HF_STRATAGEM_AMG43_MACHINE_GUN_SENTRY;
    call.name = "local/heavy_seaf_presence/reinforcement";
    call.title = "SEAF Reinforcement";
    call.description = "A Heavy SEAF Presence wave at the beacon: three squads. Turn off Constant drops on the MODS page when you want SEAF only on call.";
    call.code = "LRLRUD";
    call.cooldown = 90.0f;
    call.uses = -1;
    call.category = HF_CATEGORY_SUPPLY;
    call.beam = HF_BEAM_BLUE;
    g_call_type = g_api->add_stratagem(g_mod, &call);
    if (g_call_type == 0) Log("SEAF Reinforcement stratagem was refused");
    else Log("SEAF Reinforcement stratagem registered, code LRLRUD, type %u", g_call_type);
}

/* A private copy of the SEAF squad's payload, so this row does not share the vanilla stratagem's pointer. */
static uint64_t g_seaf_payload[8];

/* The stratagem is a normal supply stratagem. A few delivery fields are pointed at the game's SEAF drop,
 * and the icon is the SOS beacon's. The rest of the row, including HellForge's own bookkeeping, stays put. */
static void PrepareCallIn(void)
{
    uintptr_t game;
    uintptr_t from = 0;
    uintptr_t to;
    uintptr_t sos = 0;
    unsigned char donor[400];
    uint64_t payload = 0;
    uint64_t package = 0;
    uint64_t icon = 0;
    uint64_t ours = 0;
    uint32_t delivery = 0;
    uint32_t count = 0;
    uint32_t waves = 0;
    uint32_t mark = 0;
    uint32_t type = 0;
    uint8_t stick = 0;
    if (g_call_type == 0) return;
    game = g_api->module_base("game.dll", NULL);
    if (game == 0) return;
    to = g_api->stratagem_row(g_call_type);
    if (to == 0 || !ApiRd(to + 0x3c, &delivery, 4) || !ApiRd(to + 0x98, &ours, 8)) return;
    if (delivery == 6u && ours == (uint64_t)(uintptr_t)g_seaf_payload) {
        if (!g_call_ready) {
            g_call_ready = 1;
            Log("SEAF Reinforcement starts a wave of 3. Constant drops are %s.", OptionsConstant() ? "on" : "off");
        }
        return;
    }
    if (!ApiRd(game + 0x37cb600u + 116u * 8u, &from, 8) || from == 0 || !ApiRd(from, donor, sizeof donor)) return;
    memcpy(&type, donor + 0, 4);
    memcpy(&delivery, donor + 0x3c, 4);
    memcpy(&waves, donor + 0x12c, 4);
    memcpy(&payload, donor + 0x98, 8);
    memcpy(&count, donor + 0xA0, 4);
    memcpy(&package, donor + 0xA8, 8);
    memcpy(&mark, donor + 0x78, 4);
    if (type != 116u || delivery != 6u || waves != 0 || mark != 0xc0u || payload == 0 || count < 1u || count > 8u) {
        static int said;
        if (!said) { said = 1; Log("SEAF Reinforcement: this build's SEAF stratagem does not match, so the call-in was left unchanged"); }
        return;
    }
    if (!ApiRd((uintptr_t)payload, g_seaf_payload, (size_t)count * 8u)) return;
    payload = (uint64_t)(uintptr_t)g_seaf_payload;
    delivery = 6u;
    waves = 0;
    stick = donor[0x170];
    if (!ApiWr(to + 0x3c, &delivery, 4) || !ApiWr(to + 0x54, donor + 0x54, 16) || !ApiWr(to + 0x70, donor + 0x70, 12) ||
        !ApiWr(to + 0x98, &payload, 8) || !ApiWr(to + 0xA0, &count, 4) || !ApiWr(to + 0xA8, &package, 8) ||
        !ApiWr(to + 0x12c, &waves, 4) || !ApiWr(to + 0x170, &stick, 1)) {
        static int said;
        if (!said) { said = 1; Log("SEAF Reinforcement: could not turn the stratagem into a SEAF drop"); }
        return;
    }
    if (ApiRd(game + 0x37cb600u + (uintptr_t)HF_STRATAGEM_SOSBEACON * 8u, &sos, 8) && sos != 0 && ApiRd(sos + 0xb0, &icon, 8) && icon != 0)
        ApiWr(to + 0xb0, &icon, 8);
}

/* The throw lands squad 1 at the beacon. These two finish the Heavy SEAF Presence wave of 3. */
static int g_call_left;
static int g_call_k;
static float g_beacon[3];
static int g_have_beacon;
static uint64_t g_support_update;
static uint32_t g_support_last;
static uint32_t g_support_thrower;

static int HeavyIsHostOrSolo(void)
{
    HfSession s;
    if (!g_api || !g_mod || !g_api->session) return 0;
    memset(&s, 0, sizeof s);
    s.role = 0xFFFFFFFFu;
    g_api->session(g_mod, &s);
    return s.role == HF_SESSION_SOLO || s.role == HF_SESSION_HOST;
}

static void OnCallIn(HfMod* mod, const HfEventInfo* event, void* user)
{
    const HfStratagemEvent* thrown;
    uint32_t n;
    (void)mod;
    (void)user;
    if (!HeavyIsHostOrSolo()) return;
    if (event == NULL || event->size < sizeof(HfStratagemEvent) || event->kind != HF_ON_STRATAGEM) return;
    thrown = (const HfStratagemEvent*)event;
    if (!Finite3(thrown->position)) return;
    for (n = 0; n < 4; n++) {
        if (g_support_types[n] == 0 || thrown->type != g_support_types[n]) continue;
        if (thrown->info.update == g_support_update && thrown->type == g_support_last && thrown->thrower == g_support_thrower) return;
        g_support_update = thrown->info.update;
        g_support_last = thrown->type;
        g_support_thrower = thrown->thrower;
        heavy_seaf_support_call(n, thrown->type, thrown->position);
        return;
    }
    if (thrown->type != g_call_type) return;
    memcpy(g_beacon, thrown->position, sizeof g_beacon);
    g_have_beacon = 1;
    g_call_k = 1;
    g_call_left = 2;
    if (g_wave_sites < 3) memcpy(g_wave_site[g_wave_sites++], g_beacon, sizeof g_beacon);
    Log("SEAF Reinforcement: wave of 3 at the beacon");
}

static HeavyLandingGate g_landing_gate;

static void Tick(HfMod* mod, float dt, void* user)
{
    static float acc;
    static ULONGLONG mission_at;
    static ULONGLONG next_wave;
    static int armed;
    static int tries;
    static int wave_left;
    static int squad_k;
    static int sent;
    static int drop_stop;
    static int said_site;
    static int wave_held;
    static int waves;
    static int was_host;
    uintptr_t tpl = 0;
    uintptr_t game;
    int squad = 0;
    uint32_t state;
    ULONGLONG now;
    (void)user;
    if (!g_squads_tick_joined) HeavyEquipBeginFrame();
    OptionsMenuPoll(dt);
    acc += dt;
    if (acc < 2.0f) return;
    acc = 0.0f;
    PrepareCallIn();
    state = g_api->game_state(mod);
    now = HeavyNowMs();
    if (state == HF_STATE_MISSION) {
        int is_host = HeavyIsHostOrSolo();
            if (!is_host) {
            HeavyLandingGateReset(&g_landing_gate);
            if (was_host) {
                armed = 0; tries = 0; wave_left = 0; wave_held = 0; g_call_left = 0; g_have_beacon = 0;
                was_host = 0;
                EnterCriticalSection(&g_lock);
                squad = g_squad;
                tpl = g_tpl;
                LeaveCriticalSection(&g_lock);
                if ((squad == 1 || squad == 2) && tpl > 0x10000) SyncTemplateForStep(tpl, 0);
                Log("mission: host role lost; SEAF director mutations and wave spawns paused");
            }
            return;
        }
        if (mission_at == 0 || !was_host) {
            mission_at = now;
            armed = 0;
            tries = 0;
            wave_left = 0;
            squad_k = 0;
            sent = 0;
            drop_stop = 0;
            said_site = 0;
            wave_held = 0;
            waves = 0;
            g_wave_sites = 0;
            next_wave = 0;
            HeavyLandingGateReset(&g_landing_gate);
            was_host = 1;
        }
        if (!armed && tries < 8 && now >= mission_at && now - mission_at > 2000) {
            tries++;
            if (ArmMission()) armed = 1;
        }
        game = g_api->module_base("game.dll", NULL);
        {
            int host_on_ground = (game != 0) ? ResolveLivingHostMissionGround(game, NULL, NULL, NULL) : 0;
            int gate = HeavyLandingGateStep(&g_landing_gate, state, is_host, host_on_ground, now);
            if (gate == 2) {
                Log("AUTO_WAVE_GROUND_GATE: host entity verified on mission ground; arming %llu ms post-landing grace",
                    (unsigned long long)HEAVY_LANDING_GRACE_MS);
            }
            int live_diff = (game != 0 && g_api != NULL) ? cd_read_live_mission_difficulty(g_api->read, game) : 0;
            if (live_diff >= 1) {
                EnterCriticalSection(&g_lock);
                squad = g_squad;
                tpl = g_tpl;
                LeaveCriticalSection(&g_lock);
                if ((squad == 1 || squad == 2) && tpl > 0x10000) {
                    SyncTemplateForStep(tpl, SquadStepForDifficulty(live_diff));
                }
            }
            int eff_clone_wars = 0, eff_constant = 0, eff_squads = 1, eff_interval_ms = 120000, eff_ceiling = 24;
            int eff_support_squads = OptionsEffectiveSupportSquads(live_diff);
            OptionsResolveForDifficulty(live_diff, &eff_clone_wars, &eff_constant, &eff_squads, &eff_interval_ms, &eff_ceiling);
            if (armed && !drop_stop && game != 0 && eff_constant && gate == 1) {
                int rc;
                if (wave_left == 0 && now >= next_wave) {
                    int living = heavy_seaf_live_troops();
                    int ceiling = eff_ceiling;
                    int em_ok = !eff_clone_wars || HeavyHasEntityHeadroom(game, 1180u);
                    if (living >= ceiling || !em_ok) {
                        if (!wave_held) {
                            wave_held = 1;
                            Log("SEAF field holds %d/%d living troops (em_headroom=%d); the next wave waits for casualties",
                                living, ceiling, em_ok);
                        }
                    } else {
                        if (wave_held) {
                            wave_held = 0;
                            Log("SEAF field down to %d/%d living troops; waves resume", living, ceiling);
                        }
                        wave_left = eff_squads;
                        waves++;
                        squad_k = 0;
                        g_wave_sites = 0;
                    }
                }
                if (wave_left > 0) {
                    uint32_t call_type = eff_clone_wars ? 116u : WaveCallTypeFor(eff_support_squads);
                    rc = TimedCallSeaf(game, squad_k, call_type, NULL);
                    if (rc == 0) {
                        sent++;
                        squad_k++;
                        wave_left--;
                        Log("%s %d called in%s", call_type == 116u ? "SEAF shuttle" : "SEAF support squad", sent, waves == 1 ? " after ground grace" : "");
                        if (wave_left == 0) next_wave = now + (ULONGLONG)eff_interval_ms;
                    } else if (rc == 2) {
                        drop_stop = 1;
                        Log("SEAF shuttles stopped: this build's call-in code does not match");
                    } else if (rc == 3) {
                        squad_k++;
                        wave_left--;
                        if (!said_site) { said_site = 1; Log("SEAF shuttle: no landing site near the player this time"); }
                        if (wave_left == 0) next_wave = now + (ULONGLONG)eff_interval_ms;
                    }
                }
            }
        }
        if (armed && !drop_stop && game != 0 && g_call_left > 0 && g_have_beacon) {
            int beacon_diff = (g_api != NULL) ? cd_read_live_mission_difficulty(g_api->read, game) : 0;
            uint32_t call_type = WaveCallTypeFor(OptionsEffectiveSupportSquads(beacon_diff));
            int rc = TimedCallSeaf(game, g_call_k, call_type, g_beacon);
            if (rc == 0) {
                g_call_k++;
                g_call_left--;
                Log("SEAF Reinforcement wave squad %d of 3 (%s)", g_call_k, call_type == 116u ? "regular SEAF" : "SEAF support squad");
            } else if (rc == 2) {
                g_call_left = 0;
                Log("SEAF Reinforcement wave stopped: this build's call-in code does not match");
            } else if (rc == 3) {
                g_call_k++;
                g_call_left--;
                Log("SEAF Reinforcement: no landing site for squad %d", g_call_k);
            }
        }
        return;
    } else {
        HeavyLandingGateReset(&g_landing_gate);
        if (mission_at != 0 || was_host) {
            mission_at = 0;
            was_host = 0;
            armed = 0;
            tries = 0;
            wave_left = 0;
            wave_held = 0;
            g_call_left = 0;
            g_have_beacon = 0;
        }
    }
    if (state == HF_STATE_PREPARE_MISSION) return;
    ApplyRanges();
    EnterCriticalSection(&g_lock);
    squad = g_squad;
    tpl = g_tpl;
    LeaveCriticalSection(&g_lock);
    if ((squad == 1 || squad == 2) && tpl > 0x10000) {
        SyncTemplateForStep(tpl, HeavyIsHostOrSolo() ? SquadStepForDifficulty(0) : 0);
    }
}

/* The galaxy map reads planetActiveEffects from the parsed war status, not the planet table.
 * Official Heavy SEAF Presence planets carry both 1400 (the tag) and 1401 (the mission modifier). */
static int EffectHas(const uint32_t* fx, uint32_t n, uint32_t planet, uint32_t id)
{
    uint32_t i;
    for (i = 0; i < n; i++) {
        if (fx[i * 2u] == planet && fx[i * 2u + 1u] == id) return 1;
    }
    return 0;
}

static void StampEffects(uintptr_t out)
{
    uintptr_t game = g_api->module_base("game.dll", NULL);
    unsigned char hdr[0x78];
    uintptr_t planets = 0;
    uintptr_t effects = 0;
    uint32_t pc = 0;
    uint32_t fc = 0;
    uint32_t* idx;
    uint32_t* fx;
    uint32_t* neu;
    uint32_t i;
    uint32_t added = 0;
    uint32_t planets_hit = 0;
    unsigned char* mem;
    if (game == 0 || !ApiRd(out, hdr, sizeof hdr)) return;
    memcpy(&planets, hdr + 0x18, 8);
    memcpy(&pc, hdr + 0x20, 4);
    memcpy(&effects, hdr + 0x68, 8);
    memcpy(&fc, hdr + 0x70, 4);
    if (planets == 0 || pc == 0 || pc > 512u || fc > 8192u || (fc != 0 && effects == 0)) {
        Log("war status was not a planet list (planets %u, effects %u): map tag not added", pc, fc);
        return;
    }
    idx = (uint32_t*)malloc((size_t)pc * 4u);
    fx = (uint32_t*)malloc((size_t)(fc + pc * 2u) * 8u);
    if (idx == NULL || fx == NULL) { free(idx); free(fx); return; }
    for (i = 0; i < pc; i++) {
        if (!ApiRd(planets + (uintptr_t)i * 32u, &idx[i], 4u)) { free(idx); free(fx); return; }
    }
    if (fc != 0 && !ApiRd(effects, fx, (size_t)fc * 8u)) { free(idx); free(fx); return; }
    neu = fx + (size_t)fc * 2u;
    for (i = 0; i < pc; i++) {
        int hit = 0;
        if (idx[i] > 512u) continue;
        if (!EffectHas(fx, fc, idx[i], MARKER)) { neu[added * 2u] = idx[i]; neu[added * 2u + 1u] = MARKER; added++; hit = 1; }
        if (!EffectHas(fx, fc, idx[i], MODIFIER)) { neu[added * 2u] = idx[i]; neu[added * 2u + 1u] = MODIFIER; added++; hit = 1; }
        if (hit) planets_hit++;
    }
    if (added == 0) {
        static int said;
        if (!said) { said = 1; Log("galactic map: every planet already has Heavy SEAF Presence"); }
        free(idx);
        free(fx);
        return;
    }
    mem = (unsigned char*)((AllocFn)(game + ALLOC_RVA))(game + ALLOCATOR_RVA, (size_t)(fc + added) * 8u, 8u);
    if (mem == NULL) {
        Log("galactic map: the game allocator returned nothing; the tag was not added");
        free(idx);
        free(fx);
        return;
    }
    if (fc != 0) memcpy(mem, fx, (size_t)fc * 8u);
    memcpy(mem + (size_t)fc * 8u, neu, (size_t)added * 8u);
    {
        uintptr_t ptr = (uintptr_t)mem;
        uint32_t total = fc + added;
        if (!ApiWr(out + 0x68, &ptr, 8u) || !ApiWr(out + 0x70, &total, 4u)) {
            Log("galactic map: could not publish the effect list");
            free(idx);
            free(fx);
            return;
        }
    }
    Log("galactic map: Heavy SEAF Presence added to %u planet(s) (tag %u, modifier %u, %u new effect row(s))", planets_hit, MARKER, MODIFIER, added);
    free(idx);
    free(fx);
}

static uintptr_t StatusDetour(uintptr_t a, uintptr_t b, uintptr_t c, uintptr_t d, uintptr_t e, uintptr_t f, uintptr_t g, uintptr_t h)
{
    uintptr_t result = g_status(a, b, c, d, e, f, g, h);
    if (result != 0 && b != 0) StampEffects(b);
    return result;
}

/* The live game's /WarSeason/GalacticWarEffects body. HellForge serves [] for that path. */
static int LoadEffectCatalog(void)
{
    const char* folder;
    char path[520];
    FILE* f;
    long n;
    char* buf;
    if (g_fx_json != NULL) return 1;
    folder = g_api->mod_folder(g_mod);
    if (folder == NULL) return 0;
    snprintf(path, sizeof path, "%s\\galactic-war-effects.json", folder);
    f = fopen(path, "rb");
    if (f == NULL) { Log("effect catalog missing at %s", path); return 0; }
    if (fseek(f, 0, SEEK_END) != 0) { fclose(f); return 0; }
    n = ftell(f);
    if (n < 32 || n > 2 * 1024 * 1024) { fclose(f); Log("effect catalog size %ld is not usable", n); return 0; }
    if (fseek(f, 0, SEEK_SET) != 0) { fclose(f); return 0; }
    buf = (char*)malloc((size_t)n + 1u);
    if (buf == NULL || fread(buf, 1, (size_t)n, f) != (size_t)n) { free(buf); fclose(f); return 0; }
    fclose(f);
    buf[n] = 0;
    if (buf[0] != '[') { free(buf); Log("effect catalog is not a JSON array"); return 0; }
    g_fx_json = buf;
    g_fx_len = (uint64_t)n;
    return 1;
}

static int EmptyArray(uintptr_t start, uint64_t len)
{
    unsigned char b[64];
    uint64_t i;
    int open = 0;
    int close = 0;
    if (len == 0 || len > sizeof b) return 0;
    if (!ApiRd(start, b, (size_t)len)) return 0;
    for (i = 0; i < len; i++) {
        if (b[i] == ' ' || b[i] == '\t' || b[i] == '\r' || b[i] == '\n') continue;
        if (b[i] == '[') { open++; continue; }
        if (b[i] == ']') { close++; continue; }
        return 0;
    }
    return open == 1 && close == 1;
}

/* Status replies on this build keep the body pointer at +0x10 and the length at +0x20.
 * The effect catalog uses the same request object. Only an empty array is replaced. */
static int ReplaceEmptyCatalog(uintptr_t obj)
{
    uintptr_t start = 0;
    uint64_t wide = 0;
    uint64_t again = 0;
    uint64_t len;
    int use32 = 0;
    if (g_fx_json == NULL) return 0;
    if (!ApiRd(obj + 0x10, &start, 8) || !ApiRd(obj + 0x20, &wide, 8)) return 0;
    if (wide <= 64) len = wide;
    else {
        use32 = 1;
        len = (uint32_t)wide;
    }
    if (start < 0x10000 || len == 0 || len > 64 || !EmptyArray(start, len)) return 0;
    start = (uintptr_t)g_fx_json;
    if (!ApiWr(obj + 0x10, &start, 8)) return 0;
    if (use32) {
        uint32_t n = (uint32_t)g_fx_len;
        if (!ApiWr(obj + 0x20, &n, 4)) return 0;
    } else if (!ApiWr(obj + 0x20, &g_fx_len, 8)) return 0;
    if (ApiRd(obj + 0x40, &again, 8) && (again == len || again == (uint32_t)len)) {
        if (use32) {
            uint32_t n = (uint32_t)g_fx_len;
            ApiWr(obj + 0x40, &n, 4);
        } else {
            ApiWr(obj + 0x40, &g_fx_len, 8);
        }
    }
    return 1;
}

static uintptr_t EffectsDetour(uintptr_t a, uintptr_t b, uintptr_t c, uintptr_t d, uintptr_t e, uintptr_t f, uintptr_t g, uintptr_t h)
{
    int replaced = a != 0 && ReplaceEmptyCatalog(a);
    uintptr_t result = g_effects(a, b, c, d, e, f, g, h);
    if (replaced) Log("effect catalog: gave the game the Heavy SEAF Presence definition (modifier %u) along with the other galactic effects", MODIFIER);
    else Log("effect catalog: not replaced (reply is not an empty list in the expected layout); existing backend definitions are preserved. Verify modifier 1401 in the local backend catalog.");
    return result;
}

static void OnGameLoaded(HfMod* mod, HfEvent event, void* user)
{
    uintptr_t game;
    uintptr_t target;
    unsigned char now[sizeof STATUS_PROLOGUE];
    int rc;
    (void)event;
    (void)user;
    FindSupportSquads(mod);
    game = g_api->module_base("game.dll", NULL);
    if (game == 0) { Log("game.dll is not loaded; the map tag cannot be added"); return; }
    if (heavy_squad_pool_init(g_api, mod) != 0) Log("the SEAF squad pool did not initialize after game load; custom roster is unavailable");
    target = game + STATUS_RVA;
    if (g_api->read(target, now, sizeof now) != 0 || memcmp(now, STATUS_PROLOGUE, sizeof now) != 0) {
        Log("war-status parser at game.dll+0x%x does not match this build; the map tag is not hooked", STATUS_RVA);
        return;
    }
    rc = g_api->hook(mod, target, (void*)StatusDetour, (void**)&g_status);
    Log("war-status parser hook: %s", rc == 0 ? "installed" : "failed");
    if (!LoadEffectCatalog()) return;
    target = game + EFFECTS_RVA;
    if (g_api->read(target, now, 1) != 0 || (now[0] != 0x48 && now[0] != 0x40 && now[0] != 0x4c && now[0] != 0x55)) {
        Log("effect-catalog parser at game.dll+0x%x does not look like code; missions still have no SEAF definition", EFFECTS_RVA);
        return;
    }
    rc = g_api->hook(mod, target, (void*)EffectsDetour, (void**)&g_effects);
    Log("effect-catalog parser hook: %s", rc == 0 ? "installed" : "failed");
}

static void OnSessionEnd(HfMod* mod, HfEvent event, void* user)
{
    (void)mod;
    (void)event;
    (void)user;
    InterlockedExchange(&g_worker_stop, 1);
}

HF_MOD_EXPORT int hellforge_mod_init(const HfApi* hf, HfMod* mod)
{
    HANDLE thread;
    const char* build;
    if (hf == NULL || mod == NULL || hf->version < 16 || hf->session == NULL || hf->game_state == NULL) return 1;
    g_api = hf;
    g_mod = mod;
    g_rescan = 1;
    InterlockedExchange(&g_worker_stop, 0);
    HeavyLandingGateReset(&g_landing_gate);
    InitSig();
    InitializeCriticalSection(&g_lock);
    build = hf->build_id();
    OptionsMenuStart(hf, mod);
    Log("Heavy SEAF Presence Everywhere 1.2.0-clonedivers.6: modifier %u and map tag %u on every planet, extra squads %d, constant=%d, ceiling=%d (build %s)",
        MODIFIER, MARKER, SquadStep(), OptionsConstant(), OptionsWaveCeiling(), build != NULL ? build : "?");
    g_squads_tick_joined = (heavy_seaf_squads_init(hf, mod) == 0) ? 1 : 0;
    if (!g_squads_tick_joined) Log("custom squad delivery did not initialize; waves will use regular SEAF");
    /* Clonedivers local repair: the pool needs decoded game.dll; initialize in OnGameLoaded. */
    RegisterCallIn();
    if (!hf->subscribe(mod, HF_ON_STRATAGEM, OnCallIn, NULL, 0)) Log("could not listen for the SEAF Reinforcement throw");
    if (hf->on(mod, HF_EVENT_GAME_LOADED, OnGameLoaded, NULL) != 0) Log("could not listen for game load");
    (void)hf->on(mod, HF_EVENT_SESSION_END, OnSessionEnd, NULL);
    g_on_game = tm_tick_join(hf, mod, Tick, NULL, "Heavy SEAF Presence") == 1;
    if (!g_on_game) Log("game-thread update was not hooked; planet writes run from the search thread");
#ifndef HEAVY_SEAF_UNIT_TEST
    thread = CreateThread(NULL, 0, Worker, NULL, 0, NULL);
    if (thread != NULL) CloseHandle(thread);
    else Log("search thread did not start");
#else
    (void)thread;
#endif
    return 0;
}
