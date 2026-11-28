#include "hellforge_mod.h"
#include <windows.h>
#include <psapi.h>
#include "policy.h"
#include "ultrakill_d12.h"
#include "d12_native_slot.h"

/* ---- Build 25480438 Native Offsets & Constants ----------------------------------------------------------------- */

#define CHAOS_DIRECTOR_RVA      0x3326D10u
#define CHAOS_ENCOUNTER_RVA     0x3326618u
#define CHAOS_FX_QUEUE_RVA      0x346D558u
#define CHAOS_INVALID_SPAWN_RVA 0x3483C4Cu
#define CHAOS_PKG_SYSTEM_RVA    0x347CEB0u
#define CHAOS_PLAYERS_RVA       0x3326468u
#define CHAOS_LOCAL_USER_RVA    0x347CEF0u
#define CHAOS_ENTITIES_RVA      0x346BF98u
#define CHAOS_HEALTH_RVA        0x3326688u
#define CHAOS_TRANSFORM_RVA     0x3326508u
#define CHAOS_PERCEPTION_RVA    0x3326548u
#define CHAOS_FACTIONS_RVA      0x3326CB8u
#define CHAOS_PASSENGER_RVA     0x3326CC0u

#define CHAOS_ROSTER_BOTS_RVA   0x21CBCB0u
#define CHAOS_ROSTER_SQUIDS_RVA 0x21CE910u
#define CHAOS_ROSTER_BUGS_RVA   0x21CBD60u
#define CHAOS_ROSTER_ROW_STRIDE 0x80u

#define CHAOS_EXE_ENGINE_RVA    0x1A10208u
#define CHAOS_EXE_ENTITIES_RVA  0x1B135B8u
#define CHAOS_EXE_PKG_HAS_LOADED_RVA 0x321330u
#define CHAOS_EXE_PKG_FIND_RVA       0x60DA80u

#define CHAOS_ENTITY_DROPSHIP   0xDB90077E76FAA025ull
#define CHAOS_ENTITY_WARPSHIP   0x74E2285C01DA4F71ull
#define CHAOS_ENTITY_AVATAR     0x4D1C334D294DFA97ull
#define CHAOS_PKG_G3_SMOKE      0xBD90B4FCCB70622Cull
#define CHAOS_EXPLOSION_G3_SMOKE 28u
#define CHAOS_BREACH_FX_ID      0x7Bu

#define CHAOS_CURATED_PER_FACTION 6
#define CHAOS_MAX_PRELOAD_PKGS    40
#define CHAOS_PERCEPTION_STRIDE   0x13F8u
#define CHAOS_CARGO_STRIDE        0x54u
#define CHAOS_SEATS_STRIDE        0x3D8u
#define CHAOS_TRANSPORT_TIMEOUT_S 22.0
#define CHAOS_BREACH_TIMEOUT_S    8.0
#define CHAOS_BREACH_STAGGER_S    3.5f

#define CHAOS_PRELOAD_SETTLE_PREPARE_S 1.5f
#define CHAOS_PRELOAD_SETTLE_S         0.5f
#define CHAOS_PRELOAD_RATE_PREPARE_S   4.0f
#define CHAOS_PRELOAD_RATE_PER_S       12.0f
#define CHAOS_PRELOAD_FACTION_WAIT_S   0.5f

#define CHAOS_PKG_EAGLE_BASE       0x2C26BC4C6592FA14ull
#define CHAOS_PKG_COMBAT_WALKER    0x22749A294788AF66ull
#define CHAOS_PKG_EXO49_ATTE       0xE72D3E9B05C3DB0Bull
#define CHAOS_ENTITY_EXO49_ATTE    0xC2D449ECF7FACAB1ull
#define CHAOS_STRAT_TABLE_RVA      0x37CB600u
#define CHAOS_MAX_OWNED_PKGS       384u
#define CHAOS_MAX_AUX_BUNDLE_PKGS  6
#define CHAOS_STRAGGLER_MAX_R_M    260.0f

enum { PHASE_INACTIVE = 0, PHASE_ACTIVE = 1, PHASE_ENDED = 2 };

typedef struct ChaosRosterUnit {
    uint32_t role_id;
    uint64_t entity_hash;
    uint8_t row_bytes[CHAOS_ROSTER_ROW_STRIDE];
    uintptr_t row_addr;
    int valid;
} ChaosRosterUnit;

typedef struct ChaosPendingWave {
    int active;
    int is_transport;
    int used_fallback;
    int faction;
    int difficulty;
    int unloaded_count;
    int ship_seen;
    int cargo_patched;
    int squad_size;
    int unmapped_logged;
    uint32_t ship_entity;
    double elapsed;
    double timeout;
    float anchor[3];
    float target[3];
} ChaosPendingWave;

static const uint32_t kCuratedRows[CHAOS_FACTIONS][CHAOS_CURATED_PER_FACTION] = {
    {0u, 3u, 8u, 14u, 19u, 20u},  /* Bots: Trooper, Brawler, Rocket Raider, Commissar, Berserker, Devastator */
    {4u, 5u, 6u, 3u, 0u, 15u},    /* Squids: Voteless V1/V2/V3, Overseer, Elevated Overseer, Crescent Overseer */
    {0u, 2u, 3u, 4u, 10u, 6u},    /* Bugs: Scavenger, Bile Spitter, Pouncer, Warrior, Hunter, Hive Guard */
};

static const uint64_t kBaseFactionPackages[CHAOS_FACTIONS][4] = {
    {0xF6B59D283DF68AD0ull, 0xFDF011DAECF24312ull, 0x9E430D763AF434C1ull, 0xC82197FE7045AD5Eull},
    {0x6FBBB2AFFAB096D9ull, 0x046D4441A6DAE0A9ull, 0x00C928EC7E330F9Eull, 0x8801ED6613674D22ull},
    {0x501B113721D6FAF0ull, 0x89DE9C3D26D2ADC1ull, 0x6FA8FB795E82246Dull, 0x7BB9ECF8AC258C77ull},
};

static const uint8_t kExeHasLoadedPin[32] = {
    0x48, 0x83, 0xec, 0x28, 0x48, 0x8b, 0x05, 0xcd, 0xee, 0x6e, 0x01, 0x48, 0x8b, 0xd1, 0x48, 0x8b,
    0x88, 0x00, 0x04, 0x00, 0x00, 0x48, 0x8b, 0x89, 0x08, 0x02, 0x00, 0x00, 0xe8, 0x2f, 0xc7, 0x2e
};
static const uint8_t kExeFindPin[32] = {
    0x48, 0x89, 0x5c, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x10, 0x48, 0x89, 0x7c, 0x24, 0x18, 0x8b,
    0xb9, 0x80, 0x00, 0x00, 0x00, 0x45, 0x33, 0xd2, 0x85, 0xff, 0x74, 0x40, 0x48, 0x8b, 0xb1, 0x88
};

/* ---- Runtime State --------------------------------------------------------------------------------------------- */

static const HfApi* hf;
static HfMod* self;
static volatile LONG g_phase = PHASE_INACTIVE;
static ChaosConfig config;
static ChaosConfig g_ini_config;
static ChaosConfig g_session_config;
static int g_has_session_config = 0;
static int g_d12_config_active = 0;
static char g_host_options_path[CD_HOST_OPTIONS_MAX_PATH];
static uint64_t g_host_options_mtime = 0ULL;
static char g_hellforge_mods_path[CD_HOST_OPTIONS_MAX_PATH];
static uint64_t g_hellforge_mods_mtime = 0ULL;
static CdActiveMode g_detected_mods_mode = CD_MODE_AUTO;
static volatile LONG g_empire_mode_active = 0;
static uint64_t g_published_bridge_mtime = 0ULL;
static char g_published_bridge_text[320];
static uint64_t g_last_bridge_seen_mtime = 0ULL;
static char g_last_bridge_seen_text[512];
static uint64_t g_bridge_user_edit_mtime = 0ULL;
static CdHostOptions g_host_opts;
static ChaosClock clock_state;
static ChaosTracker g_tracker;
static ChaosPendingWave g_pending;
static ChaosEmSnapshot g_em_snap;
static ChaosSpawnQueue g_spawn_queue;
static ChaosUltrakillState g_ultrakill;
static D12NativeSlotState g_d12_slot;
static int g_last_mission_live_diff = 0;
static float g_prune_accum_s = 0.0f;

static unsigned state, landed, stopped, previous_role;
static float options_poll, perception_poll, preload_log_poll;
static char bridge[2048], options[2048], stop_file[2048], status_text[192];
static unsigned long token;
static uint32_t g_prng_state = 0x9E3779B9u;

static int g_pins_checked = 0, g_pins_ok = 0, g_exe_pins_ok = 0;
static int g_native_faction = -1;
static int g_rosters_loaded[CHAOS_FACTIONS];
static ChaosRosterUnit g_rosters[CHAOS_FACTIONS][CHAOS_CURATED_PER_FACTION];
static uint64_t g_preload_pkgs[CHAOS_FACTIONS][CHAOS_MAX_PRELOAD_PKGS];
static int g_preload_count[CHAOS_FACTIONS];
static int g_preload_requested[CHAOS_FACTIONS][CHAOS_MAX_PRELOAD_PKGS];
static int g_preload_built[CHAOS_FACTIONS];
static int g_faction_resident[CHAOS_FACTIONS];
static int g_preload_ready_logged[CHAOS_FACTIONS];
static int g_preload_refmap_warned[CHAOS_FACTIONS];
static int g_bot_smoke_requested = 0;
static int g_bot_smoke_resident = 0;
static float g_preload_mission_time = 0.0f;
static float g_preload_tokens = 0.0f;
static float g_preload_faction_wait[CHAOS_FACTIONS];
static int g_preload_deferred_logged = 0;

static uint32_t g_last_faction_masks[CHAOS_FACTIONS];
static uint32_t g_last_player_mask = 0;
static unsigned g_perception_narrowed_total = 0;

typedef struct ChaosPeerSync {
    uint64_t peer;
    uint8_t resident_mask;
    uint8_t hello_seen;
    uint8_t in_mission;
    uint8_t active;
    double joined_age;
    double mission_entered_age;
    double last_seen_age;
} ChaosPeerSync;

static ChaosPeerSync g_peer_sync[CHAOS_MAX_PEERS];
static uint32_t g_mission_epoch = 1u;
static uint32_t g_wave_seq = 0u;
static uint8_t g_last_sent_resident_mask = 0xFFu;
static uint8_t g_last_sent_in_mission = 0xFFu;
static double g_last_client_status_sent = -10.0;
static float g_share_poll = 0.0f;
static int g_shared_dirty = 0;
static int g_shared_applied = 0;
static double g_last_foreign_spawn_age = -1000.0;
static int g_last_foreign_faction = -1;
static float g_preload_watchdog_s = 0.0f;
static float g_straggler_recycle_s = 0.0f;
static float g_last_wave_anchor[3] = {0.0f, 0.0f, 0.0f};
static int g_has_last_wave_anchor = 0;
static uint64_t g_chaos_owned_pkgs[CHAOS_MAX_OWNED_PKGS];
static uint8_t g_chaos_owned_pkg_evicted[CHAOS_MAX_OWNED_PKGS];
static uint32_t g_chaos_owned_pkg_count = 0u;
static uint8_t s_refmap_scan_buf[4096u * 16u];

typedef struct ChaosAirRunState {
    float friendly_cd_s;
    float enemy_cd_s;
    int eagle_pkg_requested;
    int eagle_pkg_resident;
    int strafe_burst_rem;
    int strafe_is_friendly;
    float strafe_step_cd_s;
    float strafe_start[3];
    float strafe_dir[2];
    int strafe_step_idx;
} ChaosAirRunState;

typedef struct ChaosAtteWalkerState {
    int pkg_requested;
    int pkg_resident;
    int active;
    uint32_t entity;
    float pos[3];
    float heading_rad;
    float fire_cd_s;
    float redeploy_cd_s;
    float spawn_retry_s;
} ChaosAtteWalkerState;

static ChaosAirRunState g_air_runs;
static ChaosAtteWalkerState g_atte;
#if defined(__GNUC__) || defined(__clang__)
static uint8_t s_atte_spawn_ctx[0x8A0] __attribute__((aligned(16)));
static uint8_t s_atte_spawn_creation[0x58] __attribute__((aligned(8)));
#else
static __declspec(align(16)) uint8_t s_atte_spawn_ctx[0x8A0];
static __declspec(align(8)) uint8_t s_atte_spawn_creation[0x58];
#endif

static int ChaosOwnsPkg(uint64_t pkg_id)
{
    if (!pkg_id) return 0;
    for (uint32_t i = 0; i < g_chaos_owned_pkg_count; i++) {
        if (g_chaos_owned_pkgs[i] == pkg_id) return 1;
    }
    return 0;
}

static void ChaosRecordOwnedPkg(uint64_t pkg_id)
{
    if (!pkg_id || ChaosOwnsPkg(pkg_id)) return;
    if (g_chaos_owned_pkg_count < CHAOS_MAX_OWNED_PKGS) {
        g_chaos_owned_pkg_evicted[g_chaos_owned_pkg_count] = 0u;
        g_chaos_owned_pkgs[g_chaos_owned_pkg_count++] = pkg_id;
    }
}

static int ChaosOwnedPkgEvicted(uint64_t pkg_id)
{
    if (!pkg_id) return 0;
    for (uint32_t i = 0; i < g_chaos_owned_pkg_count; i++) {
        if (g_chaos_owned_pkgs[i] == pkg_id) return g_chaos_owned_pkg_evicted[i] != 0u;
    }
    return 0;
}

static void ChaosSetOwnedPkgEvicted(uint64_t pkg_id, uint8_t evicted)
{
    if (!pkg_id) return;
    for (uint32_t i = 0; i < g_chaos_owned_pkg_count; i++) {
        if (g_chaos_owned_pkgs[i] == pkg_id) {
            g_chaos_owned_pkg_evicted[i] = evicted;
            return;
        }
    }
}

/* Optional unit-test overrides cleanly compiled out for shipping DLL */
#ifdef CHAOS_UNIT_TEST
typedef struct ChaosNativeMock {
    uintptr_t game_base;
    size_t game_size;
    uintptr_t exe_base;
    size_t exe_size;
    void* (*spawn_roster_row)(void* dir, const void* row, const float pos[3], uint32_t kind, uint32_t flags,
                              const float target_pos[3], const float* quat4, const void* extra, float delay, uint32_t z);
    uint8_t (*has_headroom)(void* dir, uint32_t kind, uint8_t bypass);
    void (*spawn_transport)(void* em, uint32_t eid, const uint32_t* roles, uint32_t count, const float pos[3],
                            float spacing, const float drop_pos[3], const float target_pos[3], uint64_t ship);
    float* (*project_anchor)(void* em, float out[3], const float in[3], int radius);
    void (*particle_fx)(void* q, uint64_t res, const float pos[3], uint32_t fx, uint32_t se, uint32_t oe, uint64_t cr, uint32_t te);
    void (*breach_burst)(void* q, const float pos[3], uint32_t fx, uint32_t se, uint32_t oe, uint64_t z, uint32_t te);
    const uint8_t* (*lookup_entity_pkgs)(uint64_t entity_hash);
    void (*pkg_request)(void* sys, const uint64_t* ids, uint32_t count);
    void (*pkg_release)(void* sys, const uint64_t* ids, uint32_t count);
    uint8_t (*pkgs_idle)(void* unused);
    int (*alive_override)(uint32_t entity);
    void (*remove_entity)(void* em_root, uint32_t entity);
    uint32_t* (*request_entity_spawn)(uint32_t* out_entity, uint64_t resource, void* creation);
    int (*activate_beacon_override)(uint32_t strat_type, const float pos[3]);
} ChaosNativeMock;
static ChaosNativeMock g_mock;
#endif

/* ---- Safe Memory & Utility Helpers ----------------------------------------------------------------------------- */

static int PtrValid(uintptr_t at)
{
    return at >= 0x10000u && at < 0x800000000000ull;
}

static int ReadMem(uintptr_t at, void* out, size_t size)
{
    if (!hf || !hf->read || !PtrValid(at) || size > 0x800000000000ull - at) return 0;
    return hf->read(at, out, size) == 0;
}

static int ReadPtr(uintptr_t at, uintptr_t* out)
{
    uint64_t v = 0;
    if (!ReadMem(at, &v, sizeof v) || v < 0x10000ull || (v >> 32) > 0x7FFFull) return 0;
    *out = (uintptr_t)v;
    return 1;
}

static uint32_t ReadU32(uintptr_t at, uint32_t fallback)
{
    uint32_t v = fallback;
    ReadMem(at, &v, sizeof v);
    return v;
}

static int WriteU32(uintptr_t at, uint32_t val)
{
    if (!hf || !hf->write || !PtrValid(at)) return 0;
    return hf->write(at, &val, sizeof val) == 0;
}

static int ChaosWriteDataU32(uintptr_t at, uint32_t val)
{
#ifdef CHAOS_UNIT_TEST
    return WriteU32(at, val);
#else
    if (!PtrValid(at)) return 0;
    static uintptr_t s_rw_base = 0, s_rw_end = 0;
    if (at >= s_rw_base && at + sizeof(uint32_t) <= s_rw_end) {
        *(volatile uint32_t*)at = val;
        return 1;
    }
    MEMORY_BASIC_INFORMATION mbi;
    if (VirtualQuery((const void*)at, &mbi, sizeof mbi) == sizeof mbi &&
        mbi.State == MEM_COMMIT && mbi.Type == MEM_PRIVATE && mbi.Protect == PAGE_READWRITE) {
        uintptr_t base = (uintptr_t)mbi.BaseAddress;
        uintptr_t end = base + (uintptr_t)mbi.RegionSize;
        if (at >= base && at + sizeof(uint32_t) <= end) {
            s_rw_base = base;
            s_rw_end = end;
            *(volatile uint32_t*)at = val;
            return 1;
        }
    }
    return WriteU32(at, val);
#endif
}

static int WriteU64(uintptr_t at, uint64_t val)
{
    if (!hf || !hf->write || !PtrValid(at)) return 0;
    return hf->write(at, &val, sizeof val) == 0;
}

static void SetStatusOnce(const char* text)
{
    if (!hf || !hf->set_status || !text) return;
    if (strcmp(status_text, text) != 0) {
        snprintf(status_text, sizeof status_text, "%s", text);
        hf->set_status(self, status_text);
    }
}

static uint32_t NextRand(void)
{
    uint32_t x = g_prng_state;
    x ^= x << 13;
    x ^= x >> 17;
    x ^= x << 5;
    g_prng_state = x ? x : 0x6D2B79F5u;
    return g_prng_state;
}

static uintptr_t GameBase(size_t* size_out)
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.game_base) {
        if (size_out) *size_out = g_mock.game_size;
        return g_mock.game_base;
    }
#endif
    return (hf && hf->module_base) ? hf->module_base("game.dll", size_out) : 0;
}

static uintptr_t ExeBase(size_t* size_out)
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.exe_base) {
        if (size_out) *size_out = g_mock.exe_size;
        return g_mock.exe_base;
    }
#endif
    return (hf && hf->module_base) ? hf->module_base("helldivers2.exe", size_out) : 0;
}

static int HashFindU32(uintptr_t header, uint32_t key, uint32_t* val_out)
{
    uint8_t h[20];
    uint64_t buckets = 0;
    uint32_t cap = 0, empty = 0, mult = 0;
    if (!key || !ReadMem(header, h, sizeof h)) return 0;
    memcpy(&buckets, h, 8);
    memcpy(&cap, h + 8, 4);
    memcpy(&empty, h + 12, 4);
    memcpy(&mult, h + 16, 4);
    if (buckets < 0x10000ull || cap == 0 || cap > 1048576u) return 0;
    uint32_t start = key * mult;
    uint32_t limit = cap < 4096u ? cap : 4096u;
    for (uint32_t i = 0; i < limit; i++) {
        uint32_t pair[2];
        uintptr_t slot_addr = (uintptr_t)(buckets + (uint64_t)((start + i) % cap) * 8u);
        if (!ReadMem(slot_addr, pair, sizeof pair)) return 0;
        if (pair[0] == key) {
            if (pair[1] == 0xFFFFFFFFu) return 0;
            *val_out = pair[1];
            return 1;
        }
        if (pair[0] == empty) return 0;
    }
    return 0;
}

/* ---- Fail-Closed Pin Verification ------------------------------------------------------------------------------ */

static int ProveChaosRuntime(void)
{
    if (g_pins_checked) return g_pins_ok;
    size_t gsize = 0;
    uintptr_t gbase = GameBase(&gsize);
    if (!gbase || gsize < 0x3485000u || !hf || hf->size < offsetof(HfApi, read_code) + sizeof hf->read_code || !hf->read_code)
        return 0;

    uint32_t bad_rva = 0;
    const char* bad_name = NULL;
    g_pins_ok = ChaosVerifyCodePins(hf->read_code, gbase, gsize, &bad_rva, &bad_name);
    g_pins_checked = 1;
    if (!g_pins_ok) {
        hf->log(self, "CHAOS_PINS: ok=0 failed_rva=0x%X name=%s; native calls disabled",
                bad_rva, bad_name ? bad_name : "unknown");
        SetStatusOnce("Chaos offline: native code pin mismatch");
        return 0;
    }

    size_t esize = 0;
    uintptr_t ebase = ExeBase(&esize);
    if (ebase && esize >= CHAOS_EXE_ENTITIES_RVA + 0x100u) {
        uint8_t b1[32], b2[32];
        if (hf->read_code(ebase + CHAOS_EXE_PKG_HAS_LOADED_RVA, b1, sizeof b1) == 0 &&
            memcmp(b1, kExeHasLoadedPin, sizeof b1) == 0 &&
            hf->read_code(ebase + CHAOS_EXE_PKG_FIND_RVA, b2, sizeof b2) == 0 &&
            memcmp(b2, kExeFindPin, sizeof b2) == 0) {
            g_exe_pins_ok = 1;
        }
    }

    hf->log(self, "CHAOS_PINS: ok=1 verified=%u exe_pkg_pins=%d", (unsigned)CPIN_COUNT, g_exe_pins_ok);
    return g_pins_ok;
}

/* ---- Native Function Bridges ----------------------------------------------------------------------------------- */

static const uint8_t* CallLookupEntityPkgs(uintptr_t gbase, uint64_t entity_hash)
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.lookup_entity_pkgs) return g_mock.lookup_entity_pkgs(entity_hash);
#endif
    typedef const uint8_t* (*Fn)(uint64_t);
    return ((Fn)(gbase + kChaosCodePins[CPIN_LOOKUP_ENTITY_PKGS].rva))(entity_hash);
}

static void CallPkgRequest(uintptr_t gbase, void* sys, const uint64_t* ids, uint32_t count)
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.pkg_request) { g_mock.pkg_request(sys, ids, count); return; }
#endif
    typedef void (*Fn)(void*, const uint64_t*, uint32_t);
    ((Fn)(gbase + kChaosCodePins[CPIN_PKG_REQUEST].rva))(sys, ids, count);
}

static void CallPkgRelease(uintptr_t gbase, void* sys, const uint64_t* ids, uint32_t count)
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.pkg_release) { g_mock.pkg_release(sys, ids, count); return; }
#endif
    typedef void (*Fn)(void*, const uint64_t*, uint32_t);
    ((Fn)(gbase + kChaosCodePins[CPIN_PKG_RELEASE].rva))(sys, ids, count);
}

static void CallRemoveEntity(uintptr_t gbase, void* em_root, uint32_t entity)
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.remove_entity) { g_mock.remove_entity(em_root, entity); return; }
#endif
    typedef void (*Fn)(void*, uint32_t);
    ((Fn)(gbase + kChaosCodePins[CPIN_REMOVE_ENTITY].rva))(em_root, entity);
}

static uint32_t* CallRequestEntitySpawn(uintptr_t gbase, uint32_t* out_entity, uint64_t resource, void* creation)
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.request_entity_spawn) return g_mock.request_entity_spawn(out_entity, resource, creation);
#endif
    typedef uint32_t* (*Fn)(uint32_t*, uint64_t, void*);
    return ((Fn)(gbase + kChaosCodePins[CPIN_REQUEST_ENTITY_SPAWN].rva))(out_entity, resource, creation);
}

static int CallPkgsIdle(uintptr_t gbase)
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.pkgs_idle) return (int)g_mock.pkgs_idle(NULL);
#endif
    typedef uint8_t (*Fn)(void*);
    return (int)(((Fn)(gbase + kChaosCodePins[CPIN_PKGS_IDLE].rva))(NULL));
}

static int CallHasHeadroom(uintptr_t gbase, void* director, uint32_t kind, uint8_t bypass)
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.has_headroom) return (int)g_mock.has_headroom(director, kind, bypass);
#endif
    typedef uint8_t (*Fn)(void*, uint32_t, uint8_t);
    return (int)(((Fn)(gbase + kChaosCodePins[CPIN_HAS_HEADROOM].rva))(director, kind, bypass));
}

static float* CallProjectAnchor(uintptr_t gbase, void* em, float out_pos[3], const float in_pos[3])
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.project_anchor) return g_mock.project_anchor(em, out_pos, in_pos, 5);
#endif
    typedef float* (*Fn)(void*, float*, const float*, int);
    return ((Fn)(gbase + kChaosCodePins[CPIN_PROJECT_ANCHOR].rva))(em, out_pos, in_pos, 5);
}

static void CallSpawnTransport(uintptr_t gbase, void* em, uint32_t eid, const uint32_t* roles,
                               uint32_t count, const float pos[3], float spacing,
                               const float drop_pos[3], const float target_pos[3], uint64_t ship)
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.spawn_transport) {
        g_mock.spawn_transport(em, eid, roles, count, pos, spacing, drop_pos, target_pos, ship);
        return;
    }
#endif
    typedef void (*Fn)(void*, uint32_t, const uint32_t*, uint32_t, const float*, float,
                       const float*, const float*, uint64_t);
    ((Fn)(gbase + kChaosCodePins[CPIN_SPAWN_TRANSPORT].rva))(em, eid, roles, count, pos, spacing,
                                                              drop_pos, target_pos, ship);
}

static void* CallSpawnRosterRow(uintptr_t gbase, void* director, const void* row, const float pos[3],
                                uint32_t kind, uint32_t flags, const float target_pos[3], float delay_s)
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.spawn_roster_row)
        return g_mock.spawn_roster_row(director, row, pos, kind, flags, target_pos, NULL, NULL, delay_s, 0u);
#endif
    typedef void* (*Fn)(void*, const void*, const float*, uint32_t, uint32_t, const float*,
                        const float*, const void*, float, uint32_t);
    return ((Fn)(gbase + kChaosCodePins[CPIN_SPAWN_ROSTER_ROW].rva))(director, row, pos, kind, flags,
                                                                     target_pos, NULL, NULL, delay_s, 0u);
}

static void CallTriggerParticleFx(uintptr_t gbase, void* exp_queue, const float pos[3],
                                  uint32_t fx_id, uint32_t invalid_ent)
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.particle_fx) {
        g_mock.particle_fx(exp_queue, 0ULL, pos, fx_id, invalid_ent, invalid_ent, 0ULL, invalid_ent);
        return;
    }
#endif
    typedef void (*Fn)(void*, uint64_t, const float*, uint32_t, uint32_t, uint32_t, uint64_t, uint32_t);
    ((Fn)(gbase + kChaosCodePins[CPIN_PARTICLE_FX].rva))(exp_queue, 0ULL, pos, fx_id,
                                                         invalid_ent, invalid_ent, 0ULL, invalid_ent);
}

static void CallTriggerBreachBurst(uintptr_t gbase, void* exp_queue, const float pos[3],
                                   uint32_t fx_id, uint32_t invalid_ent)
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.breach_burst) {
        g_mock.breach_burst(exp_queue, pos, fx_id, invalid_ent, invalid_ent, 0ULL, invalid_ent);
        return;
    }
#endif
    typedef void (*Fn)(void*, const float*, uint32_t, uint32_t, uint32_t, uint64_t, uint32_t);
    ((Fn)(gbase + kChaosCodePins[CPIN_BREACH_BURST].rva))(exp_queue, pos, fx_id,
                                                          invalid_ent, invalid_ent, 0ULL, invalid_ent);
}

/* ---- World State: Entity Liveness, Players, Director, Package Residency ---------------------------------------- */

typedef struct ChaosBulkAliveCtx {
    uintptr_t ebase;
    size_t esize;
    uintptr_t gbase;
    size_t gsize;
    uint32_t ent_size;
    int gens_ok;
    uintptr_t hm;
    uintptr_t hm_records;
    uint32_t hm_cap;
    int hm_ok;
    uint8_t gens[65536];
} ChaosBulkAliveCtx;

static ChaosBulkAliveCtx g_alive_ctx;

static void ChaosPrepareAliveBatch(uintptr_t ebase, size_t esize, uintptr_t gbase, size_t gsize)
{
    g_alive_ctx.ebase = ebase;
    g_alive_ctx.esize = esize;
    g_alive_ctx.gbase = gbase;
    g_alive_ctx.gsize = gsize;
    g_alive_ctx.ent_size = 0u;
    g_alive_ctx.gens_ok = 0;
    g_alive_ctx.hm_ok = 0;
#ifdef CHAOS_UNIT_TEST
    if (g_mock.alive_override) return;
#endif
    uintptr_t ent_mgr = 0, gens = 0;
    uint32_t sz = 0;
    if (ebase && esize >= CHAOS_EXE_ENTITIES_RVA + 8u &&
        ReadPtr(ebase + CHAOS_EXE_ENTITIES_RVA, &ent_mgr) &&
        ReadMem(ent_mgr + 0x80u, &sz, sizeof sz) && sz > 0u &&
        ReadPtr(ent_mgr + 0x88u, &gens)) {
        uint32_t take = sz < (uint32_t)sizeof(g_alive_ctx.gens) ? sz : (uint32_t)sizeof(g_alive_ctx.gens);
        if (ReadMem(gens, g_alive_ctx.gens, take)) {
            g_alive_ctx.ent_size = sz;
            g_alive_ctx.gens_ok = 1;
        }
    }
    if (gbase && gsize >= CHAOS_HEALTH_RVA + 8u) {
        uintptr_t hm = 0, recs = 0;
        uint32_t hcap = 0;
        if (ReadPtr(gbase + CHAOS_HEALTH_RVA, &hm) &&
            ReadMem(hm + 0x1020u, &hcap, 4) && hcap > 0u &&
            ReadPtr(hm + 0x1058u, &recs)) {
            g_alive_ctx.hm = hm;
            g_alive_ctx.hm_records = recs;
            g_alive_ctx.hm_cap = hcap;
            g_alive_ctx.hm_ok = 1;
        }
    }
}

static int EntityAlive(uint32_t entity, void* ctx)
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.alive_override) return g_mock.alive_override(entity);
#endif
    if (!entity || entity == 0xFFFFFFFFu) return 0;
    const ChaosBulkAliveCtx* b = (const ChaosBulkAliveCtx*)ctx;
    uint32_t index = entity & 0x3FFFFFu;
    uint8_t want_gen = (uint8_t)((entity >> 22) & 0xFFu);

    if (b && b->gens_ok && index < sizeof(b->gens)) {
        if (index >= b->ent_size || b->gens[index] != want_gen) return 0;
        if (b->hm_ok) {
            uint32_t hidx = 0;
            if (HashFindU32(b->hm + 0x1030u, entity, &hidx) && hidx < b->hm_cap) {
                int32_t hp = 0;
                uint32_t life = 2;
                if (ReadMem(b->hm_records + (uintptr_t)hidx * 0x1B8u + 0x14u, &hp, 4) &&
                    ReadMem(b->hm_records + (uintptr_t)hidx * 0x1B8u + 0x19Cu, &life, 4)) {
                    if (life >= 2u || hp <= 0) return 0;
                }
            }
        }
        return 1;
    }

    size_t esize = 0, gsize = 0;
    uintptr_t ebase = ExeBase(&esize);
    uintptr_t gbase = GameBase(&gsize);
    if (!ebase || esize < CHAOS_EXE_ENTITIES_RVA + 8u) return 0;

    uintptr_t ent_mgr = 0, gens = 0;
    uint32_t size = 0;
    uint8_t gen = 0;
    if (!ReadPtr(ebase + CHAOS_EXE_ENTITIES_RVA, &ent_mgr) ||
        !ReadMem(ent_mgr + 0x80u, &size, sizeof size) || index >= size ||
        !ReadPtr(ent_mgr + 0x88u, &gens) ||
        !ReadMem(gens + index, &gen, sizeof gen) ||
        gen != want_gen)
        return 0;

    /* If entity has a HealthManager record, verify life < 2 and health > 0 */
    if (gbase && gsize >= CHAOS_HEALTH_RVA + 8u) {
        uintptr_t hm = 0, records = 0;
        uint32_t hidx = 0;
        if (ReadPtr(gbase + CHAOS_HEALTH_RVA, &hm) && HashFindU32(hm + 0x1030u, entity, &hidx) &&
            hidx < ReadU32(hm + 0x1020u, 0u) && ReadPtr(hm + 0x1058u, &records)) {
            int32_t hp = 0;
            uint32_t life = 2;
            if (ReadMem(records + (uintptr_t)hidx * 0x1B8u + 0x14u, &hp, 4) &&
                ReadMem(records + (uintptr_t)hidx * 0x1B8u + 0x19Cu, &life, 4)) {
                if (life >= 2u || hp <= 0) return 0;
            }
        }
    }
    return 1;
}

static void ChaosRefreshEmSnapshot(uintptr_t gbase, double now_age)
{
    if (g_em_snap.valid && (now_age - g_em_snap.captured_age) >= 0.0 && (now_age - g_em_snap.captured_age) < 0.20) {
        return;
    }
    uintptr_t em = 0;
    if (!gbase || !ReadPtr(gbase + CHAOS_ENTITIES_RVA, &em)) {
        g_em_snap.valid = 0;
        return;
    }
    uint32_t inv = ReadU32(gbase + UK_INVALID_ENTITY_RVA, 0xFFFFFFFFu);
    CdEmLayout layout = cd_em_resolve_hf(gbase, hf ? hf->read : NULL);
    uintptr_t dbase = cd_em_records_base(em, layout.records_offset);
    size_t snap_bytes = (size_t)layout.bounded_capacity * UK_EM_DESCRIPTOR_STRIDE;
    if (!dbase || !ReadMem(dbase, g_em_snap.raw, snap_bytes)) {
        g_em_snap.valid = 0;
        return;
    }
    uint32_t live = 0u, walking_ai = 0u;
    for (uint32_t i = 0; i < layout.bounded_capacity; i++) {
        uint32_t eid = 0;
        uint64_t res = 0;
        const uint8_t* slot = g_em_snap.raw + (size_t)i * UK_EM_DESCRIPTOR_STRIDE;
        memcpy(&res, slot + 0u, 8);
        memcpy(&eid, slot + 8u, 4);
        if (res != 0ULL && eid != 0u && eid != 0xFFFFFFFFu && eid != inv) {
            ++live;
            if (!ChaosIsProvenNonWalkingResource(res)) {
                ++walking_ai;
            }
        }
    }
#ifdef CHAOS_UNIT_TEST
    /* In synthetic arenas where descriptors[] aren't populated per spawn, conservatively fold in tracked live count. */
    if ((uint32_t)g_tracker.total_live > live && (uint32_t)g_tracker.total_live <= layout.bounded_capacity) {
        live = (uint32_t)g_tracker.total_live;
    }
    if ((uint32_t)g_tracker.total_live > walking_ai && (uint32_t)g_tracker.total_live <= layout.bounded_capacity) {
        walking_ai = (uint32_t)g_tracker.total_live;
    }
#endif
    g_em_snap.em_root = em;
    g_em_snap.desc_base = dbase;
    g_em_snap.desc_end = dbase + snap_bytes;
    g_em_snap.em_cap = layout.bounded_capacity;
    g_em_snap.gwnav_reserve = cd_em_scale_reserve(layout.bounded_capacity, UK_EM_RESERVE_SLOTS);
    g_em_snap.invalid_entity = inv;
    g_em_snap.live_slots = live;
    g_em_snap.walking_ai_slots = walking_ai;
    g_em_snap.free_slots = (live < layout.bounded_capacity) ? (layout.bounded_capacity - live) : 0u;
    g_em_snap.captured_age = now_age;
    g_em_snap.valid = 1;
}

static int EntityPosition(uintptr_t gbase, uint32_t entity, float pos_out[3])
{
    uintptr_t tm = 0, data = 0;
    uint32_t idx = 0, cap = 0;
    if (!entity || entity == 0xFFFFFFFFu || !ReadPtr(gbase + CHAOS_TRANSFORM_RVA, &tm)) return 0;
    cap = ReadU32(tm + 0x48u, 0u);
    if (cap == 0 || cap > 1048576u) return 0;
    if (!HashFindU32(tm + 0x40u, entity, &idx) || idx >= cap || !ReadPtr(tm + 0x68u, &data)) return 0;
    if (!ReadMem(data + (uintptr_t)idx * 0x308u + 0x2E0u, pos_out, 12)) return 0;
    return isfinite(pos_out[0]) && isfinite(pos_out[1]) && isfinite(pos_out[2]);
}

static int CollectLivingPlayers(uintptr_t gbase, float positions_out[4][3], uint32_t avatars_out[4])
{
    uintptr_t pm = 0, em = 0;
    uint32_t count = 0;
    int living = 0;
    if (!ReadPtr(gbase + CHAOS_PLAYERS_RVA, &pm) || !ReadMem(pm + 0x84u, &count, 4) || count == 0 || count > 4u)
        return 0;
    ReadPtr(gbase + CHAOS_ENTITIES_RVA, &em);
    CdEmLayout layout = cd_em_resolve_hf(gbase, hf ? hf->read : NULL);
    for (uint32_t i = 0; i < count; i++) {
        uint32_t life = ReadU32(pm + 0x2E0u + 0x38u * i, 0u);
        uint32_t net = ReadU32(pm + 0x3A8u + 0x20u * i, 0x7FFFu);
        uint32_t slot = 0, avatar = 0;
        uintptr_t rec = 0;
        if (life != 3u || net == 0x7FFFu || !em) continue;
        if (!HashFindU32(em + 0xF22EC8u, net, &slot) || slot >= layout.capacity) continue;
        rec = cd_em_record_at(em, layout.records_offset, slot);
        if (!rec) continue;
        avatar = ReadU32(rec + 8u, 0u);
        if (!avatar || !EntityAlive(avatar, NULL)) continue;
        float pos[3];
        if (!EntityPosition(gbase, avatar, pos)) continue;
        if (avatars_out) avatars_out[living] = avatar;
        memcpy(positions_out[living], pos, sizeof pos);
        ++living;
    }
    return living;
}

/* Returns: 2 = all parts resident (state 4), 1 = present in PackageManager (streaming state 1..3 or corrupt >1024),
 *          0 = confirmed absent from PackageManager or confirmed unloaded (part state == 0),
 *         -1 = unknown / transient read failure (callers must NOT treat as unloaded). */
static int PackageStateExe(uintptr_t ebase, size_t esize, uint64_t pkg_id)
{
    uintptr_t engine = 0, manager = 0, resources = 0, list = 0;
    uint32_t count = 0;
    if (!ebase || esize < CHAOS_EXE_ENGINE_RVA + 8u || !g_exe_pins_ok || !pkg_id) return -1;
    if (!ReadPtr(ebase + CHAOS_EXE_ENGINE_RVA, &engine) ||
        !ReadPtr(engine + 0x400u, &manager) ||
        !ReadPtr(manager + 0x208u, &resources) ||
        !ReadMem(resources + 0x80u, &count, 4) || count > 8192u)
        return -1;
    if (count == 0u) return 0;
    if (!ReadPtr(resources + 0x88u, &list)) return -1;

    int had_read_error = 0;
    for (uint32_t i = 0; i < count; i++) {
        uintptr_t pkg = 0, parts_arr = 0;
        uint64_t id = 0;
        uint32_t parts = 0;
        if (!ReadPtr(list + (uintptr_t)i * 8u, &pkg) ||
            !ReadMem(pkg + 0x10u, &id, 8) ||
            !ReadMem(pkg + 0x18u, &parts, 4) || parts == 0) {
            had_read_error = 1;
            continue;
        }
        int matched = (id == pkg_id);
        if (parts > 1024u) {
            if (matched) return 1;
            had_read_error = 1;
            continue;
        }
        if (!ReadPtr(pkg + 0x20u, &parts_arr)) {
            if (matched) return -1;
            had_read_error = 1;
            continue;
        }

        if (!matched) {
            for (uint32_t p = 0; p < parts; p++) {
                uintptr_t part = 0;
                uint64_t part_id = 0;
                if (!ReadPtr(parts_arr + (uintptr_t)p * 8u, &part) ||
                    !ReadMem(part + 0x10u, &part_id, 8)) {
                    had_read_error = 1;
                    continue;
                }
                if (part_id == pkg_id) {
                    matched = 1;
                    break;
                }
            }
        }
        if (!matched) continue;

        int all_resident = 1;
        for (uint32_t p = 0; p < parts; p++) {
            uintptr_t part = 0;
            uint32_t pstate = 0;
            if (!ReadPtr(parts_arr + (uintptr_t)p * 8u, &part) ||
                !ReadMem(part + 0x1Cu, &pstate, 4))
                return -1;
            if (pstate == 0u) return 0;
            if (pstate != 4u) all_resident = 0;
        }
        return all_resident ? 2 : 1;
    }
    return had_read_error ? -1 : 0;
}

static int PackageResidentExe(uintptr_t ebase, size_t esize, uint64_t pkg_id)
{
    return PackageStateExe(ebase, esize, pkg_id) == 2;
}

static int ReadDirectorContext(uintptr_t gbase, uintptr_t* dir_out, int* faction_out, int* diff_out)
{
    uintptr_t dir = 0;
    uint32_t fmask = 0, tech = 0;
    if (!ReadPtr(gbase + CHAOS_DIRECTOR_RVA, &dir)) return 0;
    if (!ReadMem(dir + 0x518BCu, &fmask, 4) || !ReadMem(dir + 0x518C4u, &tech, 4)) return 0;
    int nf = ChaosFactionFromDirectorMask(fmask);
    if (nf < 0 || tech < 1u || tech > 12u) return 0;
    if (dir_out) *dir_out = dir;
    if (faction_out) *faction_out = nf;
    if (diff_out) *diff_out = (int)tech;
    return 1;
}

/* ---- Curated Roster Resolution & Foreign Package Preloader (All-Peer) ------------------------------------------ */

static uint32_t FactionRosterRva(int faction)
{
    if (faction == CHAOS_BOTS) return CHAOS_ROSTER_BOTS_RVA;
    if (faction == CHAOS_SQUIDS) return CHAOS_ROSTER_SQUIDS_RVA;
    if (faction == CHAOS_BUGS) return CHAOS_ROSTER_BUGS_RVA;
    return 0u;
}

static int ResolveCuratedRosterForFaction(uintptr_t gbase, int faction)
{
    if (faction < 0 || faction >= CHAOS_FACTIONS) return 0;
    if (g_rosters_loaded[faction]) return 1;
    uint32_t rva = FactionRosterRva(faction);
    if (!rva) return 0;

    uintptr_t rows_ptr = 0;
    uint32_t row_count = 0;
    if (!ReadPtr(gbase + rva, &rows_ptr) || !ReadMem(gbase + rva + 8u, &row_count, 4) || row_count == 0 || row_count > 512u)
        return 0;

    int loaded = 0;
    for (int i = 0; i < CHAOS_CURATED_PER_FACTION; i++) {
        uint32_t idx = kCuratedRows[faction][i];
        ChaosRosterUnit* u = &g_rosters[faction][i];
        memset(u, 0, sizeof *u);
        if (idx >= row_count) continue;
        uintptr_t raddr = rows_ptr + (uintptr_t)idx * CHAOS_ROSTER_ROW_STRIDE;
        if (!ReadMem(raddr, u->row_bytes, CHAOS_ROSTER_ROW_STRIDE)) continue;
        memcpy(&u->role_id, u->row_bytes + 0x00u, 4);
        memcpy(&u->entity_hash, u->row_bytes + 0x08u, 8);
        if (!u->role_id || !u->entity_hash) continue;
        u->row_addr = raddr;
        u->valid = 1;
        ++loaded;
    }
    if (loaded > 0) g_rosters_loaded[faction] = 1;
    return loaded > 0;
}

static void AddPreloadPkg(int faction, uint64_t pkg_id)
{
    if (!pkg_id || faction < 0 || faction >= CHAOS_FACTIONS) return;
    int count = g_preload_count[faction];
    for (int i = 0; i < count; i++) if (g_preload_pkgs[faction][i] == pkg_id) return;
    if (count < CHAOS_MAX_PRELOAD_PKGS) {
        g_preload_pkgs[faction][count] = pkg_id;
        g_preload_count[faction] = count + 1;
    }
}

static void BuildPreloadList(uintptr_t gbase, int faction)
{
    if (g_preload_built[faction] || !ResolveCuratedRosterForFaction(gbase, faction)) return;

    for (int i = 0; i < 4; i++) AddPreloadPkg(faction, kBaseFactionPackages[faction][i]);

    if (faction == CHAOS_BOTS || faction == CHAOS_SQUIDS) {
        uint64_t ship = (faction == CHAOS_BOTS) ? CHAOS_ENTITY_DROPSHIP : CHAOS_ENTITY_WARPSHIP;
        const uint8_t* rec = CallLookupEntityPkgs(gbase, ship);
        if (rec) {
            uint64_t p0 = 0, p1 = 0;
            ReadMem((uintptr_t)rec + 8u, &p0, 8);
            ReadMem((uintptr_t)rec + 16u, &p1, 8);
            AddPreloadPkg(faction, p0);
            AddPreloadPkg(faction, p1);
        }
    }

    for (int i = 0; i < CHAOS_CURATED_PER_FACTION; i++) {
        if (!g_rosters[faction][i].valid) continue;
        const uint8_t* rec = CallLookupEntityPkgs(gbase, g_rosters[faction][i].entity_hash);
        if (rec) {
            uint64_t p0 = 0, p1 = 0;
            ReadMem((uintptr_t)rec + 8u, &p0, 8);
            ReadMem((uintptr_t)rec + 16u, &p1, 8);
            AddPreloadPkg(faction, p0);
            AddPreloadPkg(faction, p1);
        }
    }

    g_preload_built[faction] = 1;
    hf->log(self, "CHAOS_PRELOAD_PLAN: faction=%s packages=%d",
            chaos_faction_names[faction], g_preload_count[faction]);
}

static int CheckFactionResidency(uintptr_t gbase, uintptr_t ebase, size_t esize, int faction, int* res_out, int* idle_out)
{
    int total = g_preload_count[faction], res = 0, idle = CallPkgsIdle(gbase);
    if (idle_out) *idle_out = idle;
    if (total <= 0) { if (res_out) *res_out = 0; return 0; }
    for (int i = 0; i < total; i++) {
        if (!g_preload_requested[faction][i]) { if (res_out) *res_out = res; return 0; }
        if (PackageResidentExe(ebase, esize, g_preload_pkgs[faction][i])) ++res;
    }
    if (res_out) *res_out = res;
    return idle != 0 && res == total;
}

static int Host(void)
{
    HfSession session = {0};
    if (!hf || !hf->session) return 0;
    hf->session(self, &session);
    return session.role == HF_SESSION_SOLO || session.role == HF_SESSION_HOST;
}

static int IsCoopHost(void)
{
    HfSession session = {0};
    if (!hf || !hf->session) return 0;
    hf->session(self, &session);
    return session.role == HF_SESSION_HOST;
}

static int HasAuthorityApi(void)
{
    return hf && hf->size >= offsetof(HfApi, is_authority) + sizeof hf->is_authority && hf->is_authority != NULL;
}

static int HasChannelSend(void)
{
    return hf && hf->size >= offsetof(HfApi, send) + sizeof hf->send && hf->send != NULL;
}

static int HasChannelShare(void)
{
    return hf && hf->size >= offsetof(HfApi, share_row) + sizeof hf->share_row &&
           hf->share_set != NULL && hf->share_get != NULL;
}

static int EntityAuthorityAllowed(uint32_t entity, uint32_t event_authority)
{
    if (!Host() || !event_authority) return 0;
    if (HasAuthorityApi() && entity != 0u && entity != 0xFFFFFFFFu) {
        int auth = hf->is_authority(self, entity);
        if (auth == 0) return 0;
    }
    return 1;
}

static uint8_t CurrentForeignResidentMask(void)
{
    uint8_t m = 0;
    for (int f = 0; f < CHAOS_FACTIONS; f++) {
        if (g_faction_resident[f]) m |= (uint8_t)(1u << f);
    }
    return m;
}

static int CollectRemotePeers(uintptr_t gbase, uint64_t remote_out[CHAOS_MAX_PEERS])
{
    uintptr_t pm = 0, user = 0;
    uint32_t count = 0;
    uint64_t local_peer = 0;
    int remote_count = 0;

    if (!gbase || !ReadPtr(gbase + CHAOS_PLAYERS_RVA, &pm) ||
        !ReadMem(pm + 0x84u, &count, 4) || count == 0u || count > 4u)
        return -1;

    if (ReadPtr(gbase + CHAOS_LOCAL_USER_RVA, &user)) {
        ReadMem(user + 0xB398u, &local_peer, 8);
    }

    for (uint32_t i = 0; i < count; i++) {
        uint64_t p = 0;
        if (!ReadMem(pm + 0x2C8u + (uintptr_t)i * 0x38u, &p, 8) || p == 0ULL) continue;
        if (local_peer && p == local_peer) continue;
        int dup = 0;
        for (int j = 0; j < remote_count; j++) {
            if (remote_out[j] == p) { dup = 1; break; }
        }
        if (!dup && remote_count < CHAOS_MAX_PEERS) remote_out[remote_count++] = p;
    }
    return remote_count;
}

static void PublishSharedState(int force)
{
    if (!Host() || !HasChannelShare()) return;
    if (!force && !g_shared_dirty) return;

    ChaosSharedState st;
    memset(&st, 0, sizeof st);
    st.magic = CHAOS_SYNC_MAGIC;
    st.version = CHAOS_SYNC_VERSION;
    st.native_faction = (g_native_faction >= 0 && g_native_faction < CHAOS_FACTIONS)
                        ? (uint8_t)g_native_faction : 0xFFu;
    st.landed = landed ? 1u : 0u;
    st.mission_spawned = (uint16_t)(g_tracker.mission_spawned > 0 ? g_tracker.mission_spawned : 0);
    st.attempts = (uint16_t)(clock_state.attempts > 0xFFFFu ? 0xFFFFu : clock_state.attempts);
    st.epoch = g_mission_epoch;
    double rem = (landed && clock_state.next > clock_state.age) ? (clock_state.next - clock_state.age) : 0.0;
    if (rem > 3600.0) rem = 3600.0;
    st.cooldown_rem_ms = (int32_t)(rem * 1000.0);
    st.last_wave_seq = g_wave_seq;
    st.active_wave = g_pending.active ? 1u : 0u;
    st.active_faction = g_pending.active ? (uint8_t)g_pending.faction : 0xFFu;

    hf->share_set(self, CHAOS_SHARE_KEY_STATE, &st, sizeof st);
    g_shared_dirty = 0;
}

static void ApplySharedState(const ChaosSharedState* st)
{
    if (Host() || !ChaosSharedStateValid(st)) return;

    if (st->epoch > g_mission_epoch) g_mission_epoch = st->epoch;
    if (st->last_wave_seq > g_wave_seq) g_wave_seq = st->last_wave_seq;
    if (g_native_faction < 0 && st->native_faction < CHAOS_FACTIONS) {
        g_native_faction = (int)st->native_faction;
    }
    if (st->landed && !landed) landed = 1;
    if ((int)st->mission_spawned > g_tracker.mission_spawned) {
        g_tracker.mission_spawned = (int)st->mission_spawned;
    }
    if ((unsigned)st->attempts > clock_state.attempts) {
        clock_state.attempts = (unsigned)st->attempts;
    }
    if (st->landed) {
        double rem_s = (st->cooldown_rem_ms > 0) ? ((double)st->cooldown_rem_ms / 1000.0) : 0.0;
        clock_state.next = clock_state.age + rem_s;
    }
    g_shared_applied = 1;
}

static void ReconcilePeers(uintptr_t gbase)
{
    if (!IsCoopHost()) return;
    uint64_t remote[CHAOS_MAX_PEERS] = {0};
    int remote_count = CollectRemotePeers(gbase, remote);
    if (remote_count < 0) return;

    /* Prune peers that left PlayerManager */
    for (int i = 0; i < CHAOS_MAX_PEERS; i++) {
        if (!g_peer_sync[i].active) continue;
        int still_here = 0;
        for (int j = 0; j < remote_count; j++) {
            if (remote[j] == g_peer_sync[i].peer) { still_here = 1; break; }
        }
        if (!still_here) {
            hf->log(self, "CHAOS_PEER_LEAVE: peer=%llu resident_mask=0x%x",
                    (unsigned long long)g_peer_sync[i].peer, g_peer_sync[i].resident_mask);
            memset(&g_peer_sync[i], 0, sizeof g_peer_sync[i]);
        }
    }

    /* Register newly connected peers (including late joiners) */
    for (int j = 0; j < remote_count; j++) {
        int found = 0, free_slot = -1;
        for (int i = 0; i < CHAOS_MAX_PEERS; i++) {
            if (g_peer_sync[i].active && g_peer_sync[i].peer == remote[j]) { found = 1; break; }
            if (!g_peer_sync[i].active && free_slot < 0) free_slot = i;
        }
        if (!found && free_slot >= 0) {
            g_peer_sync[free_slot].peer = remote[j];
            g_peer_sync[free_slot].resident_mask = 0u;
            g_peer_sync[free_slot].hello_seen = 0u;
            g_peer_sync[free_slot].in_mission = 0u;
            g_peer_sync[free_slot].active = 1u;
            g_peer_sync[free_slot].joined_age = clock_state.age;
            g_peer_sync[free_slot].mission_entered_age = clock_state.age;
            g_peer_sync[free_slot].last_seen_age = clock_state.age;
            g_shared_dirty = 1;
            hf->log(self, "CHAOS_PEER_JOIN: peer=%llu age=%.1fs",
                    (unsigned long long)remote[j], clock_state.age);
        }
    }
}

static int PeersReadyForFaction(uintptr_t gbase, int chosen)
{
    if (!IsCoopHost() || !HasChannelSend() || chosen < 0 || chosen >= CHAOS_FACTIONS) return 1;
    ReconcilePeers(gbase);

    for (int i = 0; i < CHAOS_MAX_PEERS; i++) {
        const ChaosPeerSync* p = &g_peer_sync[i];
        if (!p->active) continue;
        double since_join = clock_state.age - p->joined_age;
        if (since_join < 0.0) since_join = 0.0;
        if (!p->hello_seen) {
            if (since_join < CHAOS_LATE_JOIN_HELLO_S) return 0;
            continue;
        }
        if (!p->in_mission) {
            /* H4: Joiner is still in HF_STATE_PREPARE_MISSION (e.g. slow D11 Mega City level gen > 30s):
             * hold foreign spawns up to CHAOS_PEER_PREPARE_GRACE_S (120s safety cap). */
            if (since_join < CHAOS_PEER_PREPARE_GRACE_S) return 0;
            continue;
        }
        double since_mission = clock_state.age - p->mission_entered_age;
        if (since_mission < 0.0) since_mission = 0.0;
        if (((p->resident_mask & (uint8_t)(1u << chosen)) == 0u) &&
            since_mission < CHAOS_PEER_PRELOAD_GRACE_S) {
            return 0;
        }
    }
    return 1;
}

static void SendClientStatusIfNeeded(unsigned now, uint8_t required_mask)
{
    if (Host() || !HasChannelSend()) return;
    uint8_t mask = CurrentForeignResidentMask();
    uint8_t in_mission = (now == HF_STATE_MISSION) ? 1u : 0u;
    int need = (mask != g_last_sent_resident_mask) ||
               (in_mission != g_last_sent_in_mission) ||
               (required_mask && ((mask & required_mask) != required_mask) &&
                (clock_state.age - g_last_client_status_sent) >= 5.0);
    if (!need) return;

    ChaosPeerMsg msg;
    memset(&msg, 0, sizeof msg);
    msg.magic = CHAOS_SYNC_MAGIC;
    msg.version = CHAOS_SYNC_VERSION;
    msg.kind = CHAOS_MSG_PEER_STATUS;
    msg.resident_mask = mask;
    msg.in_mission = in_mission;
    msg.epoch = g_mission_epoch;
    if (hf->send(self, HF_TO_HOST, &msg, sizeof msg) == 0) {
        g_last_sent_resident_mask = mask;
        g_last_sent_in_mission = in_mission;
        g_last_client_status_sent = clock_state.age;
    }
}

static int ChaosIsUltrakillActiveInMission(uintptr_t gbase);
static int EnqueueTheaterPlatoon(int faction, int diff, int count, uint32_t flags,
                                 const float anchor[3], const float target[3]);

static uint8_t* ChaosReadRefMapBuffer(uintptr_t gbase, uintptr_t* out_pkg_sys, uintptr_t* out_entries_addr,
                                      uint32_t* out_cap, uint64_t* out_empty_key, int* out_heap_owned)
{
    if (out_pkg_sys) *out_pkg_sys = 0u;
    if (out_entries_addr) *out_entries_addr = 0u;
    if (out_cap) *out_cap = 0u;
    if (out_empty_key) *out_empty_key = 0ULL;
    if (out_heap_owned) *out_heap_owned = 0;

    uintptr_t pkg_sys = 0, map_entries_addr = 0;
    uint32_t map_cap = 0;
    uint64_t empty_key = 0;
    if (!gbase ||
        !ReadPtr(gbase + CHAOS_PKG_SYSTEM_RVA, &pkg_sys) ||
        !ReadPtr(pkg_sys + 0x00u, &map_entries_addr) ||
        !ReadMem(pkg_sys + 0x08u, &map_cap, 4) ||
        !ReadMem(pkg_sys + 0x10u, &empty_key, 8) ||
        map_cap < 16u || map_cap > 65536u || (map_cap & (map_cap - 1u)) != 0u) {
        return NULL;
    }

    size_t bytes = (size_t)map_cap * 16u;
    uint8_t* buf = NULL;
    int heap_owned = 0;
    if (bytes <= sizeof(s_refmap_scan_buf)) {
        buf = s_refmap_scan_buf;
    } else {
        buf = (uint8_t*)malloc(bytes);
        heap_owned = 1;
    }
    if (!buf) return NULL;
    if (!ReadMem(map_entries_addr, buf, bytes)) {
        if (heap_owned) free(buf);
        return NULL;
    }
    if (out_pkg_sys) *out_pkg_sys = pkg_sys;
    if (out_entries_addr) *out_entries_addr = map_entries_addr;
    if (out_cap) *out_cap = map_cap;
    if (out_empty_key) *out_empty_key = empty_key;
    if (out_heap_owned) *out_heap_owned = heap_owned;
    return buf;
}

static int ChaosEnsureOwnedAuxBundle(uintptr_t gbase, uintptr_t ebase, size_t esize,
                                     const uint64_t* pkgs, int pkg_count,
                                     int* io_requested, int* io_resident);

/* Releases every engine package ref_count acquired by Chaos during the ending mission (#43/#45/#46).
 * Inspects pkg_sys before each RefcountedPackageRelease (game.dll+0x12FE4E0) so only present entries
 * with refcnt >= 1 are decremented (1 -> 0 unloads; >=2 -> N-1 preserves external engine owners; 0 double-free).
 * Retains any unreleased entries in g_chaos_owned_pkgs if refmap reads fail initially or mid-loop. */
static void ReleaseChaosOwnedPackages(uintptr_t gbase)
{
    if (g_chaos_owned_pkg_count == 0u) return;
    if (!gbase || !g_pins_ok || !hf || !hf->read) {
        return;
    }

    uintptr_t pkg_sys = 0, map_entries_addr = 0;
    uint32_t map_cap = 0;
    uint64_t empty_key = 0;
    int heap_owned = 0;
    uint8_t* map_buf = ChaosReadRefMapBuffer(gbase, &pkg_sys, &map_entries_addr, &map_cap, &empty_key, &heap_owned);
    if (!map_buf || !pkg_sys) {
        if (heap_owned && map_buf) free(map_buf);
        return;
    }

    uint32_t owned_before = g_chaos_owned_pkg_count;
    uint32_t released = 0u, skipped = 0u;
    size_t map_bytes = (size_t)map_cap * 16u;

    for (uint32_t i = 0; i < g_chaos_owned_pkg_count; ++i) {
        uint64_t pkg_id = g_chaos_owned_pkgs[i];
        if (!pkg_id) continue;
        uint32_t used = 0u, slot_idx = 0xFFFFFFFFu;
        uint64_t refcnt = 0ULL;
        int present = 0;
        (void)ChaosRefMapInspect(map_buf, map_cap, empty_key, pkg_id, &used, &present, &slot_idx, &refcnt);
        if (present && refcnt >= 1ULL) {
            CallPkgRelease(gbase, (void*)pkg_sys, &pkg_id, 1u);
            g_chaos_owned_pkgs[i] = 0ULL;
            g_chaos_owned_pkg_evicted[i] = 0u;
            ++released;
            if ((i + 1u < g_chaos_owned_pkg_count) && !ReadMem(map_entries_addr, map_buf, map_bytes)) {
                break;
            }
        } else {
            g_chaos_owned_pkgs[i] = 0ULL;
            g_chaos_owned_pkg_evicted[i] = 0u;
            ++skipped;
        }
    }
    if (heap_owned && map_buf) free(map_buf);

    uint32_t remaining = 0u;
    for (uint32_t i = 0; i < g_chaos_owned_pkg_count; ++i) {
        if (g_chaos_owned_pkgs[i] != 0ULL) {
            g_chaos_owned_pkgs[remaining] = g_chaos_owned_pkgs[i];
            g_chaos_owned_pkg_evicted[remaining] = g_chaos_owned_pkg_evicted[i];
            ++remaining;
        }
    }
    for (uint32_t i = remaining; i < CHAOS_MAX_OWNED_PKGS; ++i) {
        g_chaos_owned_pkgs[i] = 0ULL;
        g_chaos_owned_pkg_evicted[i] = 0u;
    }
    g_chaos_owned_pkg_count = remaining;

    if (hf && self && hf->log && (released > 0u || skipped > 0u || remaining > 0u)) {
        hf->log(self, "CHAOS_PKG_RELEASE: owned=%u released=%u skipped=%u retained=%u",
                owned_before, released, skipped, remaining);
    }
}

static void PreloadForeignFactionPackagesPaced(
    uintptr_t gbase,
    uintptr_t ebase,
    size_t esize,
    float dt,
    unsigned cur_state,
    int pace_requests)
{
    /* Stream foreign faction packages during BOTH HF_STATE_PREPARE_MISSION (Hellpod drop, gently paced at 4 pkgs/s)
     * and HF_STATE_MISSION (12 pkgs/s) so off-planet Chaos swarms are resident right as landing grace expires. */
    if (cur_state != HF_STATE_MISSION && cur_state != HF_STATE_PREPARE_MISSION) return;

    /* On Solo / Host, skip foreign package preloading entirely while Chaos is disabled ("off" preset).
     * Joined clients still preload so they stay ready for the Host's active config. */
    if (pace_requests && Host() && !config.enabled) {
        preload_log_poll -= dt;
        if (preload_log_poll <= 0.0f) {
            preload_log_poll = 15.0f;
            SetStatusOnce("Chaos Invasions standby (disabled)");
        }
        return;
    }

    int native = -1, diff = 0;
    if (!ReadDirectorContext(gbase, NULL, &native, &diff)) return;
    if (g_native_faction != native) {
        g_native_faction = native;
        g_shared_dirty = 1;
        hf->log(self, "CHAOS_MISSION_CONTEXT: native_faction=%s difficulty=%d host=%d state=%u",
                chaos_faction_names[native], diff, Host(), cur_state);
    }

    if (ChaosIsUltrakillActiveInMission(gbase) && native >= 0 && native < CHAOS_FACTIONS) {
        if (ResolveCuratedRosterForFaction(gbase, native)) {
            g_faction_resident[native] = 1;
        }
    }

    /* Fast path + 5.0s residency watchdog: zero per-tick refmap reads once both foreign factions are resident,
     * while catching & re-streaming any package evicted mid-mission so foreign waves never stop dropping! */
    int all_foreign_resident = 1;
    for (int f = 0; f < CHAOS_FACTIONS; f++) {
        if (f != native && !g_faction_resident[f]) {
            all_foreign_resident = 0;
            break;
        }
    }
    if (all_foreign_resident && pace_requests) {
        g_preload_watchdog_s += dt;
        if (g_preload_watchdog_s < 5.0f) {
            SendClientStatusIfNeeded(cur_state, ChaosRequiredForeignMask(native));
            return;
        }
        g_preload_watchdog_s = 0.0f;
        for (int f = 0; f < CHAOS_FACTIONS; f++) {
            if (f == native || !g_preload_built[f]) continue;
            int evicted = 0;
            for (int i = 0; i < g_preload_count[f]; i++) {
                uint64_t wpkg = g_preload_pkgs[f][i];
                if (PackageStateExe(ebase, esize, wpkg) == 0) {
                    ChaosSetOwnedPkgEvicted(wpkg, 1u);
                    g_preload_requested[f][i] = 0;
                    evicted = 1;
                }
            }
            if (evicted) {
                g_faction_resident[f] = 0;
                all_foreign_resident = 0;
                hf->log(self, "CHAOS_PRELOAD_WATCHDOG: faction=%s package_evicted=1 re_streaming=1",
                        chaos_faction_names[f]);
            }
        }
        if (all_foreign_resident) {
            SendClientStatusIfNeeded(cur_state, ChaosRequiredForeignMask(native));
            return;
        }
    }

    int is_prepare = (cur_state == HF_STATE_PREPARE_MISSION);
    int req_budget = CHAOS_MAX_PRELOAD_PKGS * CHAOS_FACTIONS;
    if (pace_requests) {
        float settle_s = is_prepare ? CHAOS_PRELOAD_SETTLE_PREPARE_S : CHAOS_PRELOAD_SETTLE_S;
        float rate_s   = is_prepare ? CHAOS_PRELOAD_RATE_PREPARE_S   : CHAOS_PRELOAD_RATE_PER_S;
        g_preload_mission_time += dt;
        if (g_preload_mission_time < settle_s) {
            SendClientStatusIfNeeded(cur_state, ChaosRequiredForeignMask(native));
            return;
        }
        g_preload_tokens += dt * rate_s;
        float max_burst = is_prepare ? 2.0f : ((dt >= 0.25f) ? 6.0f : 3.0f);
        if (g_preload_tokens > max_burst) g_preload_tokens = max_burst;
        req_budget = (int)g_preload_tokens;
    }

    uintptr_t pkg_sys = 0, map_entries_addr = 0;
    uint32_t map_cap = 0;
    uint64_t empty_key = 0;
    int map_heap_owned = 0;
    uint8_t* map_buf = ChaosReadRefMapBuffer(gbase, &pkg_sys, &map_entries_addr, &map_cap, &empty_key, &map_heap_owned);

    int ready_count = 0;
    int block_next_faction = 0;
    for (int f = 0; f < CHAOS_FACTIONS; f++) {
        if (f == native) continue;

        if (!block_next_faction) {
            BuildPreloadList(gbase, f);

            /* Smoke flare package for Bot dropship arrivals (in HF_STATE_MISSION only; managed via Chaos refmap, never hf->load_package). */
            if (!is_prepare && f == CHAOS_BOTS && !g_bot_smoke_resident) {
                uint64_t smoke_pkg = CHAOS_PKG_G3_SMOKE;
                if (ChaosEnsureOwnedAuxBundle(gbase, ebase, esize, &smoke_pkg, 1,
                                              &g_bot_smoke_requested, &g_bot_smoke_resident)) {
                    /* Refresh local map_buf view if ChaosEnsureOwnedAuxBundle mutated pkg_sys */
                    if (map_buf && map_entries_addr) {
                        (void)ReadMem(map_entries_addr, map_buf, (size_t)map_cap * 16u);
                    }
                } else if (map_buf && map_entries_addr) {
                    (void)ReadMem(map_entries_addr, map_buf, (size_t)map_cap * 16u);
                }
            }

            if (map_buf && g_preload_built[f]) {
                for (int i = 0; i < g_preload_count[f]; i++) {
                    if (g_preload_requested[f][i]) continue;
                    uint64_t pkg_id = g_preload_pkgs[f][i];
                    int pkg_state = PackageStateExe(ebase, esize, pkg_id);
                    /* Already resident in Stingray PackageManager: no request or token needed. */
                    if (pkg_state == 2) {
                        ChaosSetOwnedPkgEvicted(pkg_id, 0u);
                        g_preload_requested[f][i] = 1;
                        continue;
                    }
                    /* Shared package already requested by an earlier Chaos faction in this mission and not evicted:
                     * mark requested without touching pkg_sys or falsely triggering REARM while still queued! */
                    if (ChaosOwnsPkg(pkg_id) && !ChaosOwnedPkgEvicted(pkg_id)) {
                        g_preload_requested[f][i] = 1;
                        continue;
                    }
                    if (pace_requests && req_budget <= 0) break;
                    uint32_t used = 0, slot_idx = 0xFFFFFFFFu;
                    uint64_t refcnt = 0ULL;
                    int already = 0;
                    if (!ChaosRefMapInspect(map_buf, map_cap, empty_key, pkg_id,
                                            &used, &already, &slot_idx, &refcnt)) {
                        if (!g_preload_refmap_warned[f]) {
                            g_preload_refmap_warned[f] = 1;
                            hf->log(self, "CHAOS_PRELOAD_REFUSED: faction=%s pkg=%016llx reason=refmap_75pct used=%u cap=%u",
                                    chaos_faction_names[f], (unsigned long long)pkg_id, used, map_cap);
                        }
                        break;
                    }
                    if (ChaosOwnsPkg(pkg_id)) {
                        /* Flagged evicted mid-mission by the 5.0s post-ready watchdog: */
                        ChaosSetOwnedPkgEvicted(pkg_id, 0u);
                        if (pkg_state == 0 && already && refcnt == 1ULL && slot_idx < map_cap) {
                            /* Confirmed unloaded (pkg_state == 0) while still held exclusively by Chaos: reset 1 -> 0
                             * in-place and re-request so mid-mission eviction recovers cleanly. */
                            if (!WriteU64(map_entries_addr + (uintptr_t)slot_idx * 16u + 8u, 0ULL)) {
                                break;
                            }
                            hf->log(self, "CHAOS_PRELOAD_REARM: faction=%s pkg=%016llx slot=%u stale_ref=%llu",
                                    chaos_faction_names[f], (unsigned long long)pkg_id, slot_idx,
                                    (unsigned long long)refcnt);
                            CallPkgRequest(gbase, (void*)pkg_sys, &pkg_id, 1u);
                            if (pace_requests) {
                                --req_budget;
                                g_preload_tokens -= 1.0f;
                                if (g_preload_tokens < 0.0f) g_preload_tokens = 0.0f;
                            }
                            if (!ReadMem(map_entries_addr, map_buf, (size_t)map_cap * 16u)) {
                                if (map_heap_owned) free(map_buf);
                                map_buf = NULL;
                                map_heap_owned = 0;
                                break;
                            }
                        } else if (pkg_state == 0 && (!already || refcnt == 0ULL)) {
                            CallPkgRequest(gbase, (void*)pkg_sys, &pkg_id, 1u);
                            if (pace_requests) {
                                --req_budget;
                                g_preload_tokens -= 1.0f;
                                if (g_preload_tokens < 0.0f) g_preload_tokens = 0.0f;
                            }
                            if (!ReadMem(map_entries_addr, map_buf, (size_t)map_cap * 16u)) {
                                if (map_heap_owned) free(map_buf);
                                map_buf = NULL;
                                map_heap_owned = 0;
                                break;
                            }
                        }
                        g_preload_requested[f][i] = 1;
                    } else {
                        /* Acquire Chaos's own +1 ref (whether refcnt == 0 or already >= 1 from an external holder)
                         * so mission-end ReleaseChaosOwnedPackages symmetrically decrements N+1 -> N and never deadlocks. */
                        ChaosRecordOwnedPkg(pkg_id);
                        CallPkgRequest(gbase, (void*)pkg_sys, &pkg_id, 1u);
                        if (pace_requests) {
                            --req_budget;
                            g_preload_tokens -= 1.0f;
                            if (g_preload_tokens < 0.0f) g_preload_tokens = 0.0f;
                        }
                        if (!ReadMem(map_entries_addr, map_buf, (size_t)map_cap * 16u)) {
                            if (map_heap_owned) free(map_buf);
                            map_buf = NULL;
                            map_heap_owned = 0;
                            break;
                        }
                        g_preload_requested[f][i] = 1;
                    }
                }
            }
        }

        if (g_preload_built[f] && !g_faction_resident[f]) {
            int res = 0, idle = 0;
            if (CheckFactionResidency(gbase, ebase, esize, f, &res, &idle)) {
                g_faction_resident[f] = 1;
                if (!g_preload_ready_logged[f]) {
                    g_preload_ready_logged[f] = 1;
                    hf->log(self, "CHAOS_PRELOAD_READY: faction=%s resident=%d/%d idle=%d host=%d state=%u",
                            chaos_faction_names[f], res, g_preload_count[f], idle, Host(), cur_state);
                }
            }
        }

        if (g_faction_resident[f]) {
            ++ready_count;
        } else if (pace_requests && !block_next_faction) {
            int all_req = g_preload_built[f] && g_preload_count[f] > 0;
            for (int i = 0; all_req && i < g_preload_count[f]; i++) {
                if (!g_preload_requested[f][i]) all_req = 0;
            }
            if (!all_req) {
                block_next_faction = 1;
            } else {
                g_preload_faction_wait[f] += dt;
                if (g_preload_faction_wait[f] < CHAOS_PRELOAD_FACTION_WAIT_S) {
                    block_next_faction = 1;
                }
            }
        }
    }

    if (map_heap_owned && map_buf) free(map_buf);

    SendClientStatusIfNeeded(cur_state, ChaosRequiredForeignMask(native));

    preload_log_poll -= dt;
    if (preload_log_poll <= 0.0f) {
        preload_log_poll = 15.0f;
        if (ready_count < 2) SetStatusOnce("Preloading foreign Chaos packages...");
        else if (Host()) SetStatusOnce("Chaos Invasions active (foreign factions ready)");
        else SetStatusOnce("Chaos client: foreign packages resident");
    }
}

#ifdef CHAOS_UNIT_TEST
static void PreloadForeignFactionPackages(uintptr_t gbase, uintptr_t ebase, size_t esize, float dt, unsigned cur_state)
{
    PreloadForeignFactionPackagesPaced(gbase, ebase, esize, dt, cur_state, 0);
}
#endif

/* ---- Ground Anchor Finder -------------------------------------------------------------------------------------- */

static int EntityHasHealthRecord(uintptr_t gbase, size_t gsize, uint32_t entity)
{
    if (!gbase || gsize < CHAOS_HEALTH_RVA + 8u || !entity || entity == 0xFFFFFFFFu) return 0;
    uintptr_t hm = 0, records = 0;
    uint32_t hidx = 0;
    return ReadPtr(gbase + CHAOS_HEALTH_RVA, &hm) &&
           HashFindU32(hm + 0x1030u, entity, &hidx) &&
           hidx < ReadU32(hm + 0x1020u, 0u) &&
           ReadPtr(hm + 0x1058u, &records);
}

static void PruneStaleTrackedUnits(uintptr_t gbase, size_t gsize,
                                   const float (*living_pos)[3], int living_players,
                                   double now_age)
{
    int changed = 0;
    int check_health = 1;
#ifdef CHAOS_UNIT_TEST
    if (g_mock.alive_override) check_health = 0;
#endif
    int is_uk = ChaosIsUltrakillActiveInMission(gbase);
    float stranded_r = is_uk ? 320.0f : 150.0f;
    double stranded_age = is_uk ? 45.0 : 180.0;
    int limit = (g_tracker.high_water > 0 && g_tracker.high_water <= CHAOS_MAX_TRACKED)
                ? g_tracker.high_water : CHAOS_MAX_TRACKED;
    for (int i = 0; i < limit; i++) {
        ChaosTrackedUnit* u = &g_tracker.units[i];
        if (!u->active) continue;
        double age = now_age - u->spawn_age;
        if (age < 0.0) age = 0.0;

        if (check_health && gbase && gsize >= CHAOS_HEALTH_RVA + 8u) {
            if (!u->saw_health) {
                if (EntityHasHealthRecord(gbase, gsize, u->entity)) {
                    u->saw_health = 1u;
                } else if (age >= 5.0) {
                    hf->log(self, "CHAOS_PRUNE_NO_HEALTH: faction=%s entity=%u age=%.1fs",
                            (u->faction < CHAOS_FACTIONS) ? chaos_faction_names[u->faction] : "unknown",
                            u->entity, age);
                    u->active = 0u;
                    changed = 1;
                    continue;
                }
            }
        }

        if (living_players > 0 && age >= stranded_age && gbase) {
            float pos[3] = {0.0f, 0.0f, 0.0f};
            if (EntityPosition(gbase, u->entity, pos)) {
                float min_d2 = 1e30f;
                for (int p = 0; p < living_players; p++) {
                    float dx = pos[0] - living_pos[p][0];
                    float dy = pos[1] - living_pos[p][1];
                    float d2 = dx * dx + dy * dy;
                    if (d2 < min_d2) min_d2 = d2;
                }
                if (min_d2 > stranded_r * stranded_r) {
                    hf->log(self, "CHAOS_PRUNE_STRANDED: faction=%s entity=%u age=%.1fs dist=%.1fm",
                            (u->faction < CHAOS_FACTIONS) ? chaos_faction_names[u->faction] : "unknown",
                            u->entity, age, sqrtf(min_d2));
                    u->active = 0u;
                    changed = 1;
                }
            }
        }
    }
    if (changed) ChaosTrackerRecount(&g_tracker);
}

static int ChaosEnemyFamilyEx(uint64_t resource);

/* Recycles enemies stranded > 260 m from ALL living players via RemoveEntity (game.dll+0xFDC310)
 * so both EntityManager and GwNav::Bot slots return to active combat around the squad. */
static void RecycleFarEnemyStragglers(uintptr_t gbase, const float (*living_pos)[3], int living_players,
                                      double now_age, float dt)
{
    if (!Host() || !landed || living_players <= 0 || !gbase || !g_pins_ok) return;
    g_straggler_recycle_s += dt;
    if (g_straggler_recycle_s < 1.0f) return;
    g_straggler_recycle_s = 0.0f;

    ChaosRefreshEmSnapshot(gbase, now_age);
    if (!g_em_snap.valid || !g_em_snap.em_root) return;

    const float max_d2 = CHAOS_STRAGGLER_MAX_R_M * CHAOS_STRAGGLER_MAX_R_M;
    int budget = 12;
    int tracker_changed = 0;

    int limit = (g_tracker.high_water > 0 && g_tracker.high_water <= CHAOS_MAX_TRACKED)
                ? g_tracker.high_water : CHAOS_MAX_TRACKED;
    for (int i = 0; i < limit && budget > 0; ++i) {
        ChaosTrackedUnit* u = &g_tracker.units[i];
        if (!u->active) continue;
        double age = now_age - u->spawn_age;
        if (age < 12.0) continue;
        float pos[3] = {0.0f, 0.0f, 0.0f};
        if (!EntityPosition(gbase, u->entity, pos)) continue;
        float min_d2 = 1e30f;
        for (int p = 0; p < living_players; ++p) {
            float dx = pos[0] - living_pos[p][0];
            float dy = pos[1] - living_pos[p][1];
            float d2 = dx * dx + dy * dy;
            if (d2 < min_d2) min_d2 = d2;
        }
        if (min_d2 > max_d2 && EntityAuthorityAllowed(u->entity, 1u)) {
            CallRemoveEntity(gbase, (void*)g_em_snap.em_root, u->entity);
            u->active = 0u;
            tracker_changed = 1;
            --budget;
            ++g_ultrakill.stragglers_recycled_total;
        }
    }

    if (ChaosIsUltrakillActiveInMission(gbase) && budget > 0 && g_em_snap.walking_ai_slots >= 260u) {
        static uint32_t s_scan_cursor = 0u;
        uint32_t cap = g_em_snap.em_cap;
        uint32_t steps = cap < 512u ? cap : 512u;
        for (uint32_t k = 0; k < steps && budget > 0; ++k) {
            uint32_t idx = (s_scan_cursor + k) % cap;
            const uint8_t* slot = g_em_snap.raw + (size_t)idx * UK_EM_DESCRIPTOR_STRIDE;
            uint64_t res = 0ULL;
            uint32_t eid = 0u;
            uint8_t flags = slot[0x14u];
            memcpy(&res, slot + 0u, 8);
            memcpy(&eid, slot + 8u, 4);
            if (!res || !eid || eid == 0xFFFFFFFFu || eid == g_em_snap.invalid_entity) continue;
            if ((flags & 1u) == 0u || (flags & 2u) != 0u) continue; /* locally owned live enemy only */
            if (ChaosIsProvenNonWalkingResource(res) || ChaosEnemyFamilyEx(res) < 0) continue;
            if (ChaosTrackerHasEntity(&g_tracker, eid)) continue;
            float pos[3] = {0.0f, 0.0f, 0.0f};
            if (!EntityPosition(gbase, eid, pos)) continue;
            float min_d2 = 1e30f;
            for (int p = 0; p < living_players; ++p) {
                float dx = pos[0] - living_pos[p][0];
                float dy = pos[1] - living_pos[p][1];
                float d2 = dx * dx + dy * dy;
                if (d2 < min_d2) min_d2 = d2;
            }
            if (min_d2 > max_d2 && EntityAuthorityAllowed(eid, 1u)) {
                CallRemoveEntity(gbase, (void*)g_em_snap.em_root, eid);
                --budget;
                ++g_ultrakill.stragglers_recycled_total;
            }
        }
        s_scan_cursor = (s_scan_cursor + steps) % (cap ? cap : 1u);
    }

    if (tracker_changed) ChaosTrackerRecount(&g_tracker);
}

static int FindSafeGroundAnchorEx(uintptr_t gbase, int squad_size, float out_anchor[3], float out_player[3])
{
    float players[4][3];
    int living = CollectLivingPlayers(gbase, players, NULL);
    uintptr_t em = 0;
    if (living <= 0 || !ReadPtr(gbase + CHAOS_ENCOUNTER_RVA, &em)) return 0;

    int outer_swarm = (squad_size >= 12) || ChaosIsUltrakillActiveInMission(gbase);
    float probe_min_r = outer_swarm ? 98.0f  : 40.0f;
    float probe_max_r = outer_swarm ? 182.0f : 60.0f;
    float valid_min_r = outer_swarm ? 95.0f  : 38.0f;
    float valid_max_r = outer_swarm ? 185.0f : 62.0f;
    float min_all_r   = outer_swarm ? 70.0f  : 28.0f;

    int start_p = (int)(NextRand() % (uint32_t)living);
    for (int attempt = 0; attempt < 16; attempt++) {
        int strict = (attempt < 10);
        float max_dz = outer_swarm ? (strict ? 18.0f : 32.0f) : (strict ? 6.0f : 12.0f);
        int pi = (start_p + attempt) % living;
        float angle = (float)(NextRand() % 3600u) * (3.14159265f / 1800.0f);
        float radius = probe_min_r + (float)(NextRand() % 1000u) * 0.001f * (probe_max_r - probe_min_r);
        float seed[3] = {
            players[pi][0] + radius * cosf(angle),
            players[pi][1] + radius * sinf(angle),
            players[pi][2]
        };
        float candidate[3] = {0.0f, 0.0f, 0.0f};
        CallProjectAnchor(gbase, (void*)em, candidate, seed);
        if (!ChaosAnchorValidRange(candidate, players[pi], players, living,
                                   valid_min_r, valid_max_r, min_all_r, max_dz))
            continue;
        if (strict && g_has_last_wave_anchor) {
            float ldx = candidate[0] - g_last_wave_anchor[0];
            float ldy = candidate[1] - g_last_wave_anchor[1];
            if (ldx * ldx + ldy * ldy < 20.0f * 20.0f) continue;
        }
        memcpy(out_anchor, candidate, 12);
        memcpy(out_player, players[pi], 12);
        return 1;
    }
    return 0;
}

/* ---- Wave Dispatch & Staggered Fallback ------------------------------------------------------------------------ */

static int ChaosEnemyFamilyEx(uint64_t resource)
{
    int fam = ChaosEnemyFamily(resource);
    if (fam >= 0) return fam;
    for (int f = 0; f < CHAOS_FACTIONS; f++) {
        if (!g_rosters_loaded[f]) continue;
        for (int i = 0; i < CHAOS_CURATED_PER_FACTION; i++) {
            if (g_rosters[f][i].valid && g_rosters[f][i].entity_hash == resource)
                return f;
        }
    }
    return -1;
}

static const char* PendingWaveMode(const ChaosPendingWave* w)
{
    if (w->used_fallback) return "fallback";
    if (w->is_transport) return "transport";
    return (w->faction == CHAOS_BUGS) ? "breach" : "roster";
}

static void PickRoleArrayAndRows(int faction, int diff, int count, uint32_t roles_out[6], int row_idx_out[6])
{
    int avail[CHAOS_CURATED_PER_FACTION], n_avail = 0;
    int weighted[CHAOS_CURATED_PER_FACTION], n_weighted = 0;
    int d = (diff < 1) ? 1 : (diff > 10 ? 10 : diff);
    for (int i = 0; i < CHAOS_CURATED_PER_FACTION; i++) {
        if (!g_rosters[faction][i].valid) continue;
        avail[n_avail++] = i;
        float w = 0.0f;
        memcpy(&w, g_rosters[faction][i].row_bytes + 0x30u + (size_t)(d - 1) * 4u, sizeof w);
        if (isfinite(w) && w > 0.0f) weighted[n_weighted++] = i;
    }
    const int* pool = n_weighted > 0 ? weighted : avail;
    int pool_n = n_weighted > 0 ? n_weighted : n_avail;
    for (int i = 0; i < count && i < 6; i++) {
        int slot = pool_n > 0 ? pool[(int)(NextRand() % (uint32_t)pool_n)] : 0;
        row_idx_out[i] = slot;
        roles_out[i] = pool_n > 0 ? g_rosters[faction][slot].role_id : 0u;
    }
}

static int ResolveTransportCargoSlot(uintptr_t pass_comp, uint32_t active, uintptr_t descs,
                                     uintptr_t cargo_base, uint32_t prev_active, uint32_t ship_hint,
                                     uint32_t* slot_out, uint32_t* ship_out)
{
    uint32_t slot = 0xFFFFFFFFu;
    if (ship_hint != 0u && HashFindU32(pass_comp + 0x20u, ship_hint, &slot) && slot < active) {
        *slot_out = slot;
        *ship_out = ship_hint;
        return 1;
    }
    if (active > prev_active && active <= 256u && descs) {
        for (uint32_t s = prev_active; s < active; s++) {
            uintptr_t edesc = 0;
            uint64_t eres = 0;
            uint32_t eeid = 0, hslot = 0xFFFFFFFFu;
            if (!ReadPtr(descs + (uintptr_t)s * 8u, &edesc) ||
                !ReadMem(edesc, &eres, sizeof eres) || !ChaosIsTransportResource(eres) ||
                !ReadMem(edesc + 8u, &eeid, sizeof eeid))
                continue;
            if (!HashFindU32(pass_comp + 0x20u, eeid, &hslot) || hslot != s) continue;
            if (ReadU32(cargo_base + (uintptr_t)s * CHAOS_CARGO_STRIDE, 99u) != 0u ||
                ReadU32(cargo_base + (uintptr_t)s * CHAOS_CARGO_STRIDE + 0x48u, 0u) != 3u)
                continue;
            *slot_out = s;
            *ship_out = eeid;
            return 1;
        }
    }
    return 0;
}

static int FixTransportCargoSlot(uintptr_t gbase, int faction, uint32_t cargo_faction,
                                 uint32_t prev_active, uint32_t ship_hint,
                                 const int idxs[6], int sq)
{
    uintptr_t pass_comp = 0, descs = 0, cargo_base = 0, seats_base = 0;
    uint32_t active = 0;
    if (!ReadPtr(gbase + CHAOS_PASSENGER_RVA, &pass_comp) ||
        !ReadMem(pass_comp + 0x10u, &active, sizeof active) || active == 0u || active > 256u ||
        !ReadPtr(pass_comp + 0x50u, &cargo_base))
        return 0;
    ReadPtr(pass_comp + 0x38u, &descs);
    ReadPtr(pass_comp + 0x48u, &seats_base);

    uint32_t slot = 0xFFFFFFFFu, ship_eid = ship_hint;
    if (!ResolveTransportCargoSlot(pass_comp, active, descs, cargo_base, prev_active, ship_hint,
                                   &slot, &ship_eid))
        return 0;

    uintptr_t slot_addr = cargo_base + (uintptr_t)slot * CHAOS_CARGO_STRIDE;
    uint32_t cur_fac = ReadU32(slot_addr, 0xFFFFFFFFu);
    uint32_t kind = ReadU32(slot_addr + 0x48u, 0u);
    if ((cur_fac != 0u && cur_fac != cargo_faction) || kind != 3u) return 0;

    int wrote_fac = 0;
    if (cur_fac == 0u) {
        if (!WriteU32(slot_addr, cargo_faction)) return 0;
        wrote_fac = 1;
    }
    if (!g_pending.ship_entity && ship_eid) g_pending.ship_entity = ship_eid;
    g_pending.cargo_patched = 1;

    uint32_t c_cnt = ReadU32(slot_addr + 0x04u, 0u);
    uint32_t repaired = 0u;
    if (idxs && sq > 0 && c_cnt > 0u && c_cnt <= 16u) {
        uintptr_t seats_addr = seats_base ? (seats_base + (uintptr_t)slot * CHAOS_SEATS_STRIDE) : 0u;
        uint32_t s_cnt = seats_addr ? ReadU32(seats_addr + 0x3CCu, 0u) : 0u;
        for (uint32_t k = 0; k < c_cnt; k++) {
            uint32_t ridx = ReadU32(slot_addr + 0x08u + (uintptr_t)k * 4u, 0u);
            if (ridx == 0xFFFFFFFFu) {
                uint32_t curated = kCuratedRows[faction][idxs[k % (uint32_t)sq]];
                if (WriteU32(slot_addr + 0x08u + (uintptr_t)k * 4u, curated)) {
                    ++repaired;
                    if (seats_addr && s_cnt == c_cnt &&
                        ReadU32(seats_addr + 0x80u + (uintptr_t)k * 4u, 0u) == 0xFFFFFFFFu) {
                        WriteU32(seats_addr + 0x80u + (uintptr_t)k * 4u, curated);
                    }
                }
            }
        }
    }
    if (wrote_fac || repaired > 0u) {
        hf->log(self, "CHAOS_PASSENGER_PATCH: faction=%s ship=%u slot=%u cargo_faction=%u count=%u repaired=%u",
                chaos_faction_names[faction], ship_eid, slot, cargo_faction, c_cnt, repaired);
    }
    return 1;
}

static void DispatchStaggeredRoster(uintptr_t gbase, uintptr_t director, int faction, int diff, int count,
                                    uint32_t flags, const float anchor[3], const float target[3],
                                    float total_span_s)
{
    uint32_t roles[6] = {0};
    int idxs[6] = {0};
    PickRoleArrayAndRows(faction, diff, count, roles, idxs);

    int dispatched = 0;
    for (int i = 0; i < count && i < 6; i++) {
        const ChaosRosterUnit* u = &g_rosters[faction][idxs[i]];
        if (!u->valid || !u->row_addr) continue;
        float angle = (float)i * (6.2831853f / (float)(count > 0 ? count : 1));
        float jit[3] = {
            anchor[0] + 1.5f * cosf(angle),
            anchor[1] + 1.5f * sinf(angle),
            anchor[2]
        };
        float delay = (count > 1 && total_span_s > 0.0f) ? (total_span_s * (float)i / (float)(count - 1)) : 0.0f;
        void* slot = CallSpawnRosterRow(gbase, (void*)director, (const void*)u->row_addr, jit,
                                        3u, flags, target, delay);
        uint32_t eid = 0;
        if (slot && ReadMem((uintptr_t)slot + 8u, &eid, 4) && eid != 0u && eid != 0xFFFFFFFFu &&
            ChaosTrackerRecordSpawnAt(&g_tracker, faction, eid, 1, clock_state.age)) {
            ++dispatched;
            if (g_pending.active && g_pending.faction == faction) ++g_pending.unloaded_count;
        }
    }
    hf->log(self, "CHAOS_ROSTER_DISPATCH: faction=%s flags=0x%x count=%d immediate=%d anchor=(%.1f,%.1f,%.1f)",
            chaos_faction_names[faction], flags, count, dispatched, anchor[0], anchor[1], anchor[2]);
}

static void DispatchChaosWave(uintptr_t gbase, uintptr_t director, int faction, int diff,
                              int squad_count, const float anchor[3], const float player_pos[3])
{
    int sq = squad_count > 0 ? squad_count : config.squad_size;
    if (sq < 1) sq = 1;
    if (sq > 32) sq = 32;
    memcpy(g_last_wave_anchor, anchor, 12);
    g_has_last_wave_anchor = 1;
    if (faction >= 0 && faction < CHAOS_FACTIONS && faction != g_native_faction) {
        g_last_foreign_faction = faction;
    }

    if (g_pending.active) ChaosTrackerReleaseUnusedReservations(&g_tracker, g_pending.faction);
    memset(&g_pending, 0, sizeof g_pending);
    g_pending.active = 1;
    g_pending.faction = faction;
    g_pending.difficulty = diff;
    g_pending.squad_size = sq;
    memcpy(g_pending.anchor, anchor, 12);
    memcpy(g_pending.target, player_pos, 12);

    /* Cohesive Outer SWARM Wave (18-32 units on 95-185 m perimeter ring marching inward onto squad):
     * dispatch a 6-unit vanguard immediately and stream the remaining cluster smoothly at <= 4/frame! */
    if (sq > 6) {
        int lead = 6;
        int rest = sq - lead;
        uint32_t swarm_flags = (faction == CHAOS_BUGS) ? 0x24u : 0x3Cu;
        ChaosTrackerReserveWave(&g_tracker, faction, lead);
        g_pending.squad_size = lead;
        g_pending.is_transport = 0;
        g_pending.used_fallback = 1;
        g_pending.timeout = CHAOS_BREACH_TIMEOUT_S;
        DispatchStaggeredRoster(gbase, director, faction, diff, lead, swarm_flags, anchor, player_pos, 1.2f);
        int queued = EnqueueTheaterPlatoon(faction, diff, rest, swarm_flags, anchor, player_pos);
        ++g_wave_seq;
        g_shared_dirty = 1;
        PublishSharedState(1);
        hf->log(self, "CHAOS_WAVE_DISPATCH: event=%s mode=outer_swarm count=%d vanguard=%d queued=%d anchor=(%.1f,%.1f,%.1f)",
                chaos_names[faction], sq, lead, queued, anchor[0], anchor[1], anchor[2]);
        return;
    }

    ChaosTrackerReserveWave(&g_tracker, faction, sq);

    if (faction == CHAOS_BOTS || faction == CHAOS_SQUIDS) {
        uintptr_t em = 0, orig_dir_roster = 0, pass_comp = 0;
        uint32_t q_transports = 999u, orig_dir_faction = 0, prev_pass_active = 0u;
        uintptr_t scoped_roster = gbase + FactionRosterRva(faction);
        uint32_t cargo_faction = ChaosCargoFactionFromFaction(faction);
        int has_remote_peers = 0;
        if (IsCoopHost()) {
            ReconcilePeers(gbase);
            for (int pi = 0; pi < CHAOS_MAX_PEERS; pi++) {
                if (g_peer_sync[pi].active) { has_remote_peers = 1; break; }
            }
            if (!has_remote_peers) {
                uint64_t remote_tmp[CHAOS_MAX_PEERS] = {0};
                if (CollectRemotePeers(gbase, remote_tmp) > 0) has_remote_peers = 1;
            }
        }
        /* In active co-op, foreign transport cargo_slot->cargo_faction is host-local and unreplicated while
         * passenger seat row indices replicate, causing joiner PassengerSetupSlot (+0x684a70) to index the
         * native roster and dereference NULL. DispatchStaggeredRoster replicates 64-bit entity hashes cleanly.
         * In ULTRAKILL theater mode, ground surges also prevent uncounted transport/breach bursts from exceeding
         * the governed EntityManager ceiling. */
        int is_ultrakill = ChaosIsUltrakillActiveInMission(gbase);
        int can_transport = !has_remote_peers &&
                            !is_ultrakill &&
                            cargo_faction != 0u &&
                            ReadPtr(gbase + CHAOS_ENCOUNTER_RVA, &em) &&
                            ReadMem(em + 0x918u, &q_transports, 4) && q_transports < 8u &&
                            ReadMem(director + 0x518BCu, &orig_dir_faction, 4) &&
                            ReadPtr(director + 0x660u, &orig_dir_roster);

        if (can_transport) {
            uint32_t roles[6] = {0};
            int idxs[6] = {0};
            float spacing = 4.0f;
            ReadMem(em + 0x91Cu, &spacing, 4);
            if (!isfinite(spacing) || spacing < 1.0f || spacing > 30.0f) spacing = 4.0f;
            PickRoleArrayAndRows(faction, diff, sq, roles, idxs);

            if (ReadPtr(gbase + CHAOS_PASSENGER_RVA, &pass_comp)) {
                prev_pass_active = ReadU32(pass_comp + 0x10u, 0u);
            }

            if (faction == CHAOS_BOTS && g_bot_smoke_resident && hf->spawn_explosion) {
                hf->spawn_explosion(self, CHAOS_EXPLOSION_G3_SMOKE, anchor);
            }

            uint64_t ship = (faction == CHAOS_BOTS) ? CHAOS_ENTITY_DROPSHIP : CHAOS_ENTITY_WARPSHIP;
            uint32_t scoped_mask = ChaosDirectorMaskFromFaction(faction);
            WriteU32(director + 0x518BCu, scoped_mask);
            WriteU64(director + 0x660u, (uint64_t)scoped_roster);
            CallSpawnTransport(gbase, (void*)em, 0u, roles, (uint32_t)sq, anchor, spacing,
                               anchor, player_pos, ship);
            WriteU64(director + 0x660u, (uint64_t)orig_dir_roster);
            WriteU32(director + 0x518BCu, orig_dir_faction);

            FixTransportCargoSlot(gbase, faction, cargo_faction, prev_pass_active,
                                  g_pending.ship_entity, idxs, sq);

            g_pending.is_transport = 1;
            g_pending.timeout = CHAOS_TRANSPORT_TIMEOUT_S;
            ++g_wave_seq;
            g_shared_dirty = 1;
            PublishSharedState(1);
            hf->log(self, "CHAOS_WAVE_DISPATCH: event=%s mode=transport ship=%016llx q=%u count=%d cargo_patched=%d anchor=(%.1f,%.1f,%.1f)",
                    chaos_names[faction], (unsigned long long)ship, q_transports, sq,
                    g_pending.cargo_patched, anchor[0], anchor[1], anchor[2]);
            return;
        }

        /* Transport slot busy or unreadable: fall back immediately to staggered SpawnRosterRow (0x3C) */
        g_pending.is_transport = 0;
        g_pending.used_fallback = 1;
        g_pending.timeout = CHAOS_BREACH_TIMEOUT_S;
        DispatchStaggeredRoster(gbase, director, faction, diff, sq, 0x3Cu, anchor, player_pos, 2.0f);
        ++g_wave_seq;
        g_shared_dirty = 1;
        PublishSharedState(1);
        hf->log(self, "CHAOS_WAVE_DISPATCH: event=%s mode=fallback_immediate count=%d anchor=(%.1f,%.1f,%.1f)",
                chaos_names[faction], sq, anchor[0], anchor[1], anchor[2]);
        return;
    }

    /* Terminids: staggered SpawnRosterRow (flags=0x24, kind=3). Skip native TriggerBreachBurst in ULTRAKILL
     * so native breach emitters cannot pour 30+ uncounted extra entities past the EntityManager governor. */
    if (!ChaosIsUltrakillActiveInMission(gbase)) {
        uintptr_t exp_q = 0;
        uint32_t invalid_ent = ReadU32(gbase + CHAOS_INVALID_SPAWN_RVA, 0xFFFFFFFFu);
        if (ReadPtr(gbase + CHAOS_FX_QUEUE_RVA, &exp_q)) {
            CallTriggerParticleFx(gbase, (void*)exp_q, anchor, CHAOS_BREACH_FX_ID, invalid_ent);
            CallTriggerBreachBurst(gbase, (void*)exp_q, anchor, CHAOS_BREACH_FX_ID, invalid_ent);
        }
    }
    g_pending.is_transport = 0;
    g_pending.timeout = CHAOS_BREACH_TIMEOUT_S;
    DispatchStaggeredRoster(gbase, director, CHAOS_BUGS, diff, sq, 0x24u, anchor, player_pos, CHAOS_BREACH_STAGGER_S);
    ++g_wave_seq;
    g_shared_dirty = 1;
    PublishSharedState(1);
    hf->log(self, "CHAOS_WAVE_DISPATCH: event=bug_breach mode=breach_fx count=%d anchor=(%.1f,%.1f,%.1f)",
            sq, anchor[0], anchor[1], anchor[2]);
}

static void ServicePendingWave(uintptr_t gbase, uintptr_t director, float dt)
{
    if (!g_pending.active) return;
    g_pending.elapsed += (double)dt;
    if (g_pending.unloaded_count >= g_pending.squad_size) {
        ChaosTrackerReleaseUnusedReservations(&g_tracker, g_pending.faction);
        hf->log(self,
                "CHAOS_WAVE_COMPLETE: faction=%s mode=%s unloaded=%d/%d ship_seen=%d elapsed=%.1fs live=%d mission_spawned=%d result=complete",
                chaos_faction_names[g_pending.faction], PendingWaveMode(&g_pending),
                g_pending.unloaded_count, g_pending.squad_size, g_pending.ship_seen,
                g_pending.elapsed, g_tracker.live[g_pending.faction], g_tracker.mission_spawned);
        g_pending.active = 0;
        return;
    }
    if (g_pending.elapsed < g_pending.timeout) return;

    if (g_pending.is_transport && g_pending.unloaded_count == 0) {
        hf->log(self, "CHAOS_TRANSPORT_FALLBACK: faction=%s ship_seen=%d unloaded=0 after=%.1fs; calling SpawnRosterRow(0x3C)",
                chaos_faction_names[g_pending.faction], g_pending.ship_seen, g_pending.elapsed);
        g_pending.used_fallback = 1;
        g_pending.is_transport = 0;
        g_pending.elapsed = 0.0;
        g_pending.timeout = CHAOS_BREACH_TIMEOUT_S;
        DispatchStaggeredRoster(gbase, director, g_pending.faction, g_pending.difficulty, g_pending.squad_size,
                                0x3Cu, g_pending.anchor, g_pending.target, 2.0f);
        return;
    }

    int released = g_tracker.reserved[g_pending.faction];
    ChaosTrackerReleaseUnusedReservations(&g_tracker, g_pending.faction);
    hf->log(self,
            "CHAOS_WAVE_COMPLETE: faction=%s mode=%s unloaded=%d/%d ship_seen=%d elapsed=%.1fs released=%d live=%d mission_spawned=%d result=timeout",
            chaos_faction_names[g_pending.faction], PendingWaveMode(&g_pending),
            g_pending.unloaded_count, g_pending.squad_size, g_pending.ship_seen,
            g_pending.elapsed, released, g_tracker.live[g_pending.faction], g_tracker.mission_spawned);
    g_pending.active = 0;
}

/* ---- 250ms Host/Solo Mutual Hostility Perception Pass (Bulk Read + Epoch Cache + Direct RW Store) ------------ */

#define CHAOS_PERC_HT_CAP  8192u
#define CHAOS_BULK_CHUNK   4096u

typedef struct ChaosFacMaskEntry {
    uint32_t epoch;
    uint32_t entity;
    uint32_t mask;
} ChaosFacMaskEntry;

typedef struct ChaosPercCacheEntry {
    uint32_t entity;
    uint32_t applied_ignore;
    uint32_t all_enemy_masks;
    uint32_t player_mask;
    uint32_t mission_epoch;
} ChaosPercCacheEntry;

static ChaosFacMaskEntry s_fac_mask_ht[CHAOS_PERC_HT_CAP];
static uint32_t s_fac_mask_epoch = 1u;
static ChaosPercCacheEntry s_perc_cache_ht[CHAOS_PERC_HT_CAP];
static uint32_t s_perc_pass_seq = 0u;
static uintptr_t s_bulk_desc_ptrs[CHAOS_BULK_CHUNK];
static uint32_t s_bulk_fac_masks[CHAOS_BULK_CHUNK];

static inline int ChaosReadDescFast(uintptr_t desc, uint64_t* res_out, uint32_t* eid_out, uint8_t* flags_out)
{
    if (!PtrValid(desc)) return 0;
    if (g_em_snap.valid && desc >= g_em_snap.desc_base && desc + UK_EM_DESCRIPTOR_STRIDE <= g_em_snap.desc_end) {
        const uint8_t* p = g_em_snap.raw + (size_t)(desc - g_em_snap.desc_base);
        uint64_t r = 0;
        memcpy(&r, p + 0u, 8);
        if (r != 0ULL) {
            *res_out = r;
            if (eid_out) memcpy(eid_out, p + 8u, 4);
            if (flags_out) *flags_out = p[0x14u];
            return 1;
        }
    }
    uint8_t raw[24];
    if (!ReadMem(desc, raw, sizeof raw)) return 0;
    memcpy(res_out, raw + 0u, 8);
    if (eid_out) memcpy(eid_out, raw + 8u, 4);
    if (flags_out) *flags_out = raw[0x14u];
    return 1;
}

static inline void ChaosFacHtInsert(uint32_t epoch, uint32_t entity, uint32_t mask)
{
    if (!entity || entity == 0xFFFFFFFFu || !mask) return;
    uint32_t m = CHAOS_PERC_HT_CAP - 1u;
    uint32_t h = (entity * 0x9E3779B1u);
    h ^= h >> 16;
    for (uint32_t p = 0; p < CHAOS_PERC_HT_CAP; p++) {
        ChaosFacMaskEntry* e = &s_fac_mask_ht[(h + p) & m];
        if (e->epoch != epoch || e->entity == entity) {
            e->epoch = epoch;
            e->entity = entity;
            e->mask = mask;
            return;
        }
    }
}

static inline uint32_t ChaosFacHtFind(uint32_t epoch, uint32_t entity)
{
    if (!entity || entity == 0xFFFFFFFFu) return 0u;
    uint32_t m = CHAOS_PERC_HT_CAP - 1u;
    uint32_t h = (entity * 0x9E3779B1u);
    h ^= h >> 16;
    for (uint32_t p = 0; p < CHAOS_PERC_HT_CAP; p++) {
        const ChaosFacMaskEntry* e = &s_fac_mask_ht[(h + p) & m];
        if (e->epoch != epoch) return 0u;
        if (e->entity == entity) return e->mask;
    }
    return 0u;
}

static void UpdatePerceptionHostility(uintptr_t gbase)
{
    uintptr_t fac_mgr = 0, perc_mgr = 0;
    uintptr_t fac_descs = 0, fac_masks = 0, perc_descs = 0, perc_recs = 0;
    uint32_t fac_count = 0, perc_count = 0;

    if (!ReadPtr(gbase + CHAOS_FACTIONS_RVA, &fac_mgr) ||
        !ReadPtr(gbase + CHAOS_PERCEPTION_RVA, &perc_mgr))
        return;

    if (!ReadMem(fac_mgr + 0x12040u, &fac_count, 4) || fac_count == 0 || fac_count > 65536u ||
        !ReadPtr(fac_mgr + 0x12068u, &fac_descs) || !ReadPtr(fac_mgr + 0x12078u, &fac_masks))
        return;

    if (!ReadMem(perc_mgr + 0x20u, &perc_count, 4) || perc_count == 0 || perc_count > 65536u ||
        !ReadPtr(perc_mgr + 0x48u, &perc_descs) || !ReadPtr(perc_mgr + 0x50u, &perc_recs))
        return;

    uint32_t epoch = ++s_fac_mask_epoch;
    if (epoch == 0u) {
        memset(s_fac_mask_ht, 0, sizeof s_fac_mask_ht);
        epoch = ++s_fac_mask_epoch;
    }

    /* Never write BHV_FACTIONS: bulk-scan read-only to discover live faction bitmasks & populate O(1) entity->mask map. */
    uint32_t player_mask = 0;
    uint32_t observed_masks[CHAOS_FACTIONS] = {0, 0, 0};

    for (uint32_t base = 0; base < fac_count; base += CHAOS_BULK_CHUNK) {
        uint32_t chunk = (fac_count - base < CHAOS_BULK_CHUNK) ? (fac_count - base) : CHAOS_BULK_CHUNK;
        if (!ReadMem(fac_descs + (uintptr_t)base * 8u, s_bulk_desc_ptrs, (size_t)chunk * sizeof(uintptr_t)) ||
            !ReadMem(fac_masks + (uintptr_t)base * 4u, s_bulk_fac_masks, (size_t)chunk * sizeof(uint32_t))) {
            return;
        }
        for (uint32_t k = 0; k < chunk; k++) {
            uint32_t mask = s_bulk_fac_masks[k];
            uintptr_t desc = s_bulk_desc_ptrs[k];
            if (!mask || !PtrValid(desc)) continue;
            uint64_t resource = 0;
            uint32_t entity = 0;
            if (!ChaosReadDescFast(desc, &resource, &entity, NULL)) continue;
            ChaosFacHtInsert(epoch, entity, mask);
            if (resource == CHAOS_ENTITY_AVATAR) {
                player_mask |= mask;
                continue;
            }
            int fam = ChaosEnemyFamilyEx(resource);
            if (fam >= 0 && !ChaosIsTransportResource(resource)) observed_masks[fam] |= mask;
        }
    }

    uint32_t all_enemy_masks = observed_masks[0] | observed_masks[1] | observed_masks[2];
    if (all_enemy_masks == 0) return;

    uint32_t pass_seq = ++s_perc_pass_seq;
    int force_recheck = ((pass_seq & 15u) == 1u);
#ifdef CHAOS_UNIT_TEST
    force_recheck = 1;
#endif

    unsigned narrowed_now = 0, enemy_perceivers = 0;
    for (uint32_t base = 0; base < perc_count; base += CHAOS_BULK_CHUNK) {
        uint32_t chunk = (perc_count - base < CHAOS_BULK_CHUNK) ? (perc_count - base) : CHAOS_BULK_CHUNK;
        if (!ReadMem(perc_descs + (uintptr_t)base * 8u, s_bulk_desc_ptrs, (size_t)chunk * sizeof(uintptr_t))) {
            break;
        }
        for (uint32_t k = 0; k < chunk; k++) {
            uintptr_t desc = s_bulk_desc_ptrs[k];
            if (!PtrValid(desc)) continue;
            uint64_t resource = 0;
            uint32_t entity = 0;
            uint8_t desc_flags = 0, active = 0;
            if (!ChaosReadDescFast(desc, &resource, &entity, &desc_flags) || (desc_flags & 2u))
                continue;

            int fam = ChaosEnemyFamilyEx(resource);
            if (fam < 0 || ChaosIsTransportResource(resource)) continue;

            uint32_t own_mask = ChaosFacHtFind(epoch, entity);
            if (own_mask == 0u) {
                uint32_t fidx = 0;
                if (!HashFindU32(fac_mgr + 0x12050u, entity, &fidx) || fidx >= fac_count ||
                    !ReadMem(fac_masks + (uintptr_t)fidx * 4u, &own_mask, 4))
                    continue;
            }
            if (own_mask == 0u || (own_mask & player_mask) != 0u) continue;

            uint32_t cslot = ((entity * 0x9E3779B1u) >> 16) & (CHAOS_PERC_HT_CAP - 1u);
            ChaosPercCacheEntry* ce = &s_perc_cache_ht[cslot];
            if (!force_recheck &&
                ce->entity == entity &&
                ce->mission_epoch == g_mission_epoch &&
                ce->all_enemy_masks == all_enemy_masks &&
                ce->player_mask == player_mask) {
                ++enemy_perceivers;
                continue;
            }

            uint32_t idx = base + k;
            uintptr_t rec_addr = perc_recs + (uintptr_t)idx * CHAOS_PERCEPTION_STRIDE;
            uint8_t tail_bytes[9]; /* +0x13E8..+0x13F0 (ignore_mask at +0, active at +8) */
            if (!ReadMem(rec_addr + 0x13E8u, tail_bytes, sizeof tail_bytes)) continue;
            active = tail_bytes[8];
            if (!active) continue;

            uint32_t cur_ignore = 0;
            memcpy(&cur_ignore, tail_bytes, 4);
            ++enemy_perceivers;

            uint32_t next_ignore = ChaosNarrowIgnoreMask(cur_ignore, own_mask, all_enemy_masks, player_mask);
            if (next_ignore != cur_ignore) {
                if (ChaosWriteDataU32(rec_addr + 0x13E8u, next_ignore)) {
                    ++narrowed_now;
                    cur_ignore = next_ignore;
                }
            }
            ce->entity = entity;
            ce->applied_ignore = cur_ignore;
            ce->all_enemy_masks = all_enemy_masks;
            ce->player_mask = player_mask;
            ce->mission_epoch = g_mission_epoch;
        }
    }

    if (memcmp(observed_masks, g_last_faction_masks, sizeof observed_masks) != 0 ||
        player_mask != g_last_player_mask || narrowed_now > 0) {
        memcpy(g_last_faction_masks, observed_masks, sizeof observed_masks);
        g_last_player_mask = player_mask;
        g_perception_narrowed_total += narrowed_now;
        hf->log(self, "CHAOS_PERCEPTION: player_mask=0x%x bots=0x%x squids=0x%x bugs=0x%x perceivers=%u narrowed_now=%u narrowed_total=%u",
                player_mask, observed_masks[CHAOS_BOTS], observed_masks[CHAOS_SQUIDS], observed_masks[CHAOS_BUGS],
                enemy_perceivers, narrowed_now, g_perception_narrowed_total);
    }
}

/* ---- Level 12 — ULTRAKILL & Full Clone Wars Theater Runtime ---------------------------------------------------- */

static int ChaosLiveMissionDifficulty(uintptr_t gbase)
{
    if (!hf || !hf->read || !gbase) return 0;
    return cd_read_live_mission_difficulty(hf->read, gbase);
}

static int ChaosIsUltrakillActiveInMission(uintptr_t gbase)
{
    int live_diff = ChaosLiveMissionDifficulty(gbase);
    return cd_host_options_active_for_difficulty(&g_host_opts, live_diff);
}

static const char* ChaosD12DisplayName(void)
{
    if (g_host_opts.chaos_intensity == CD_CHAOS_ULTRAKILL ||
        (g_host_opts.theater_preset == CD_THEATER_LEVEL12_ULTRAKILL &&
         g_host_opts.chaos_intensity != CD_CHAOS_CLONE_WARS)) {
        return "ULTRAKILL";
    }
    return (InterlockedCompareExchange(&g_empire_mode_active, 0, 0) != 0)
           ? "STORMTROOPER"
           : "CLONE WARS";
}

static const char* UltrakillTextDetour(uint32_t id, void* a, void* b, void* c)
{
    if (id == D12_NUM_TEXT_ID) {
        return "12";
    }
    if (id == D12_NAME_TEXT_ID) {
        return ChaosD12DisplayName();
    }
    if (g_d12_slot.state != 1 && g_ultrakill.d12_applied && id != 0u) {
        if (id == g_ultrakill.d11_num_text_id && Host()) {
            return "12";
        }
        if (id == g_ultrakill.d11_name_text_id && Host()) {
            return (cd_effective_chaos_intensity(&g_host_opts) == CD_CHAOS_CLONE_WARS)
                   ? ChaosD12DisplayName()
                   : "ULTRAKILL";
        }
    }
    return g_ultrakill.orig_text ? g_ultrakill.orig_text(id, a, b, c) : "";
}

static void UltrakillTryInstallTextHook(const char* where)
{
    if (!hf || !self || hf->size < offsetof(HfApi, hook_ex) + sizeof(hf->hook_ex) || !hf->hook_ex) return;
    if (InterlockedCompareExchange(&g_ultrakill.text_hook_state, 0, 0) != 0) return;

    size_t gsize = 0;
    uintptr_t gbase = GameBase(&gsize);
    if (!gbase || gsize <= UK_ENGINE_API_RVA + 8u) return;

    uintptr_t api_ptr = 0, table_ptr = 0, text_fn = 0;
    if (!ReadPtr(gbase + UK_ENGINE_API_RVA, &api_ptr) ||
        !ReadPtr(api_ptr + UK_ENGINE_API_TABLE_OFF, &table_ptr) ||
        !ReadPtr(table_ptr + UK_ENGINE_TEXT_SLOT_OFF, &text_fn))
        return;

    if (InterlockedCompareExchange(&g_ultrakill.text_hook_state, 1, 0) != 0) return;
    uint8_t probe[4];
    if (!ReadMem(text_fn, probe, sizeof probe)) {
        InterlockedExchange(&g_ultrakill.text_hook_state, 0);
        return;
    }
    if (hf->hook_ex(self, text_fn, (void*)UltrakillTextDetour, (void**)&g_ultrakill.orig_text, 20) != 0) {
        InterlockedExchange(&g_ultrakill.text_hook_state, -1);
        hf->log(self, "CHAOS_ULTRAKILL_TEXT: hook_ex failed at 0x%llx (%s)", (unsigned long long)text_fn, where);
        return;
    }
    InterlockedExchange(&g_ultrakill.text_hook_state, 2);
    hf->log(self, "CHAOS_ULTRAKILL_TEXT: hooked engine text vtable at 0x%llx (%s)", (unsigned long long)text_fn, where);
}

static float g_d12_sync_poll = 0.0f;

static void ChaosEnsureD12SlotAndItemEx(int force_reg_read)
{
    if (!hf || !self) return;
    if (hf->size >= offsetof(HfApi, logs_folder) + sizeof(hf->logs_folder) &&
        hf->logs_folder != NULL) {
        const char* ldir = hf->logs_folder(self);
        if (ldir && ldir[0]) {
            int rc = D12SyncUnlockItemInFolder(&g_d12_slot, ldir, force_reg_read);
            if (rc == 2) {
                hf->log(self, "CHAOS_D12_ITEM: synced mixId=%u copyOf=%u level=12 in %s\\registered-items.json",
                        (unsigned)D12_UNLOCK_ITEM_ID, (unsigned)D12_SUPER_HELLDIVE_ITEM, ldir);
            }
        }
    }
    size_t gsize = 0;
    uintptr_t gbase = GameBase(&gsize);
    if (!gbase) return;
    if (g_d12_slot.state == 0) {
        (void)D12TryInstallNativeSlot(&g_d12_slot, hf, self, gbase, gsize, &g_host_opts);
    } else if (g_d12_slot.state == 1 && !g_d12_slot.online_cap_raised) {
        if (D12TrySyncOnlineCap(&g_d12_slot, hf, gbase)) {
            hf->log(self, "CHAOS_D12_CAP: raised online max_difficulty override to 12");
        }
    }
}

static void ChaosEnsureD12SlotAndItem(void)
{
    ChaosEnsureD12SlotAndItemEx(0);
}

static void UltrakillSyncDifficulty12(void)
{
    if (!hf || hf->size < offsetof(HfApi, difficulty_row) + sizeof(hf->difficulty_row) || !hf->difficulty_row) return;
    HfDifficultySettings* row = (HfDifficultySettings*)hf->difficulty_row(11u);
    if (!row || row->level != 11u) return;

    if (!g_ultrakill.d11_snapshot_valid) {
        memcpy(g_ultrakill.d11_baseline, row, sizeof(HfDifficultySettings));
        g_ultrakill.d11_name_text_id = row->name_text;
        g_ultrakill.d11_num_text_id = row->description_text;
        g_ultrakill.d11_snapshot_valid = 1;
    }

    if (g_d12_slot.state == 1 && g_d12_slot.row12 != NULL) {
        if (g_ultrakill.d12_applied) {
            memcpy(row, g_ultrakill.d11_baseline, sizeof(HfDifficultySettings));
            g_ultrakill.d12_applied = 0;
        }
        D12PopulateRow12(&g_d12_slot, &g_host_opts);
        return;
    }

    int force_all_diffs = Host() &&
                          (g_host_opts.theater_preset == CD_THEATER_LEVEL12_ULTRAKILL ||
                           g_host_opts.chaos_intensity == CD_CHAOS_ULTRAKILL);
    if (force_all_diffs) {
        UltrakillApplyDifficultyRow12(row, g_ultrakill.d11_baseline, &g_host_opts);
        if (!g_ultrakill.d12_applied) {
            g_ultrakill.d12_applied = 1;
            hf->log(self,
                    "CHAOS_ULTRAKILL_D12: armed=1 preset=%s encounter=%.0f guard=%.0f spawner_cd=%.2fs patrol_cap=%u radius=%.3f (%.1fm)",
                    cd_theater_preset_name(g_host_opts.theater_preset),
                    (double)row->encounter_points, (double)row->guardforce_points,
                    (double)row->spawner_cooldown, row->patrol_cap[0],
                    (double)row->map_radius[0], (double)(row->map_radius[0] * 1024.0f));
        }
    } else if (g_ultrakill.d12_applied) {
        memcpy(row, g_ultrakill.d11_baseline, sizeof(HfDifficultySettings));
        g_ultrakill.d12_applied = 0;
        hf->log(self, "CHAOS_ULTRAKILL_D12: armed=0 restored baseline Difficulty 11 settings");
    }
}

#ifndef CHAOS_UNIT_TEST
static void UltrakillTryEnlargePhysicsPools(void)
{
    if (g_ultrakill.pools_status != 0 || !hf || !hf->read || !hf->write) return;
    size_t gsize = 0;
    uintptr_t gbase = GameBase(&gsize);
    if (!gbase) return;
    int rc = UltrakillPatchPoolsWithOps(gbase, gsize, hf->read, hf->write);
    if (rc == 1) {
        g_ultrakill.pools_status = 1;
        hf->log(self,
                "CHAOS_ULTRAKILL_POOLS: enlarged=1 ragdolls=2048 ragdoll_actors=65536 motions=131072 bodies=262144 dynamic=131072 keyframed=32768");
    } else if (rc == 0) {
        g_ultrakill.pools_status = -1;
        hf->log(self, "CHAOS_ULTRAKILL_POOLS: enlarged=0 reason=physics_setup_already_ran");
    } else {
        g_ultrakill.pools_status = -1;
        hf->log(self, "CHAOS_ULTRAKILL_POOLS: enlarged=0 reason=pin_mismatch_left_default");
    }
}

static void UltrakillRaiseWwiseVoices(void)
{
    if (g_ultrakill.voices_raised) return;
    typedef int (*SetMaxVoicesFn)(unsigned short voices);
    typedef bool (*IsInitFn)(void);
    HMODULE wwise = GetModuleHandleW(L"wwise_pluginw64_release.dll");
    SetMaxVoicesFn set_limit = wwise
        ? (SetMaxVoicesFn)(void*)GetProcAddress(wwise, "?SetMaxNumVoicesLimit@SoundEngine@AK@@YA?AW4AKRESULT@@G@Z")
        : NULL;
    IsInitFn is_init = wwise
        ? (IsInitFn)(void*)GetProcAddress(wwise, "?IsInitialized@SoundEngine@AK@@YA_NXZ")
        : NULL;
    if (!set_limit || !is_init || !is_init()) return;
    int ak_res = set_limit((unsigned short)UK_WWISE_VOICES);
    if (ak_res == 1) {
        g_ultrakill.voices_raised = 1;
        hf->log(self, "CHAOS_ULTRAKILL_AUDIO: wwise_max_voices=%u (default=140)", (unsigned)UK_WWISE_VOICES);
    }
}
#endif

static void OnGameLoaded(HfMod* mod, HfEvent ev, void* user)
{
    (void)mod; (void)ev; (void)user;
#ifndef CHAOS_UNIT_TEST
    UltrakillTryEnlargePhysicsPools();
#endif
    UltrakillTryInstallTextHook("HF_EVENT_GAME_LOADED");
    ChaosEnsureD12SlotAndItemEx(1);
}

static void OnEntitiesReady(HfMod* mod, HfEvent ev, void* user)
{
    (void)mod; (void)ev; (void)user;
#ifndef CHAOS_UNIT_TEST
    UltrakillRaiseWwiseVoices();
#endif
    UltrakillTryInstallTextHook("HF_EVENT_ENTITIES_READY");
    ChaosEnsureD12SlotAndItemEx(1);
    UltrakillSyncDifficulty12();
}

static int FindTheaterRingAnchor(uintptr_t gbase, const float (*players)[3], int living,
                                 float min_r, float max_r, float out_anchor[3], float out_player[3])
{
    uintptr_t em = 0;
    if (living <= 0 || !ReadPtr(gbase + CHAOS_ENCOUNTER_RVA, &em)) return 0;
    int start_p = (int)(NextRand() % (uint32_t)living);
    float span = (max_r > min_r) ? (max_r - min_r) : 20.0f;
    float min_all = (min_r >= 90.0f) ? 68.0f : 42.0f;
    for (int attempt = 0; attempt < 14; attempt++) {
        int pi = (start_p + attempt) % living;
        float angle = (float)(NextRand() % 3600u) * (3.14159265f / 1800.0f);
        float radius = min_r + (float)(NextRand() % 1000u) * 0.001f * span;
        float seed[3] = {
            players[pi][0] + radius * cosf(angle),
            players[pi][1] + radius * sinf(angle),
            players[pi][2]
        };
        float candidate[3] = {0.0f, 0.0f, 0.0f};
        CallProjectAnchor(gbase, (void*)em, candidate, seed);
        if (!ChaosAnchorValidRange(candidate, players[pi], players, living,
                                   min_r - 6.0f, max_r + 12.0f, min_all, 42.0f))
            continue;
        memcpy(out_anchor, candidate, 12);
        memcpy(out_player, players[pi], 12);
        return 1;
    }
    return 0;
}

static int EnqueueTheaterPlatoon(int faction, int diff, int count, uint32_t flags,
                                 const float anchor[3], const float target[3])
{
    if (faction < 0 || faction >= CHAOS_FACTIONS || !g_rosters_loaded[faction] || count <= 0) return 0;
    int enqueued = 0;
    for (int base = 0; base < count; base += 6) {
        int batch = (count - base < 6) ? (count - base) : 6;
        uint32_t roles[6] = {0};
        int idxs[6] = {0};
        PickRoleArrayAndRows(faction, diff, batch, roles, idxs);
        for (int i = 0; i < batch; i++) {
            const ChaosRosterUnit* u = &g_rosters[faction][idxs[i]];
            if (!u->valid || !u->row_addr) continue;
            float angle = (float)(base + i) * (6.2831853f / (float)count);
            float rad = 2.4f + 1.35f * (float)((base + i) % 6); /* Cohesive 2.4..9.2m swarm cluster */
            ChaosSpawnJob job;
            memset(&job, 0, sizeof job);
            job.faction = (uint8_t)faction;
            job.row_slot = (uint8_t)idxs[i];
            job.flags = flags;
            job.pos[0] = anchor[0] + rad * cosf(angle);
            job.pos[1] = anchor[1] + rad * sinf(angle);
            job.pos[2] = anchor[2];
            memcpy(job.target, target, 12);
            job.delay_s = 0.04f * (float)((base + i) & 7);
            if (!ChaosSpawnQueuePush(&g_spawn_queue, &job)) return enqueued;
            ++enqueued;
        }
    }
    return enqueued;
}

static void DrainTheaterSpawnQueue(uintptr_t gbase, uintptr_t director)
{
    if (!config.enabled) {
        ChaosSpawnQueueReset(&g_spawn_queue);
        return;
    }
    if (g_spawn_queue.count == 0u) return;
    ChaosRefreshEmSnapshot(gbase, clock_state.age);
    uint32_t reserve_floor = g_em_snap.gwnav_reserve ? g_em_snap.gwnav_reserve : UK_EM_RESERVE_SLOTS;
    if (!g_em_snap.valid ||
        g_em_snap.free_slots < reserve_floor ||
        g_em_snap.walking_ai_slots >= UK_MAX_WALKING_AI_CAP) {
        return; /* Hard <= 620 walking AI & EntityManager safety floor (< 1,024 GwNav::Bot)! */
    }
    if (!CallHasHeadroom(gbase, (void*)director, 3u, 1u)) {
        return;
    }

    uint32_t budget = UK_MAX_SPAWNS_PER_FRAME;
    uint32_t avail_em = g_em_snap.free_slots - reserve_floor;
    uint32_t avail_walk = UK_MAX_WALKING_AI_CAP - g_em_snap.walking_ai_slots;
    if (avail_em < budget) budget = avail_em;
    if (avail_walk < budget) budget = avail_walk;

    ChaosSpawnJob job;
    for (uint32_t n = 0; n < budget && ChaosSpawnQueuePop(&g_spawn_queue, &job); ++n) {
        if (job.faction >= CHAOS_FACTIONS || job.row_slot >= CHAOS_CURATED_PER_FACTION) continue;
        if (!((int)job.faction == g_native_faction ||
              (g_faction_resident[job.faction] && PeersReadyForFaction(gbase, (int)job.faction)))) {
            continue;
        }
        const ChaosRosterUnit* u = &g_rosters[job.faction][job.row_slot];
        if (!u->valid || !u->row_addr) continue;
        void* slot = CallSpawnRosterRow(gbase, (void*)director, (const void*)u->row_addr,
                                        job.pos, 3u, job.flags, job.target, job.delay_s);
        uint32_t eid = 0;
        if (slot && ReadMem((uintptr_t)slot + 8u, &eid, 4) && eid != 0u && eid != 0xFFFFFFFFu) {
            ChaosTrackerRecordSpawnAt(&g_tracker, (int)job.faction, eid, 0, clock_state.age);
        }
        ++g_ultrakill.multi_ring_spawns_total;
        if (g_em_snap.valid && g_em_snap.free_slots > 0u) {
            --g_em_snap.free_slots;
            ++g_em_snap.live_slots;
            ++g_em_snap.walking_ai_slots;
        }
    }
}

static void UltrakillSyncDirectorPatrolValve(int close_valve)
{
    HfDifficultySettings* active_row = NULL;
    const HfDifficultySettings* base_row = NULL;
    if (g_d12_slot.state == 1 && g_d12_slot.row12 != NULL) {
        active_row = g_d12_slot.row12;
        base_row = (const HfDifficultySettings*)g_d12_slot.row12_baseline;
    } else if (g_ultrakill.d12_applied && g_ultrakill.d11_snapshot_valid &&
               hf && hf->size >= offsetof(HfApi, difficulty_row) + sizeof(hf->difficulty_row) && hf->difficulty_row) {
        active_row = (HfDifficultySettings*)hf->difficulty_row(11u);
        base_row = (const HfDifficultySettings*)g_ultrakill.d11_baseline;
    }
    if (!active_row) return;

    (void)base_row;
    if (close_valve) {
        if (!g_ultrakill.director_valve_closed) {
            for (int f = 0; f < 3; ++f) {
                active_row->patrol_cap[f] = 0u;
                active_row->patrol_interval[f] = 9999.0f;
            }
            active_row->encounter_points = 0.0f;
            active_row->spawner_cooldown = 9999.0f;
            uint32_t zero_reinf = 0u;
            memcpy((uint8_t*)active_row + UK_DIFF_REINFORCE_CAP_OFF, &zero_reinf, sizeof zero_reinf);
            g_ultrakill.director_valve_closed = 1;
        }
    } else if (g_ultrakill.director_valve_closed) {
        for (int f = 0; f < 3; ++f) {
            active_row->patrol_cap[f] = 24u;
            active_row->patrol_interval[f] = 30.0f;
        }
        active_row->encounter_points = 1500.0f;
        active_row->spawner_cooldown = 10.0f;
        uint32_t reinf_cap = 96u;
        memcpy((uint8_t*)active_row + UK_DIFF_REINFORCE_CAP_OFF, &reinf_cap, sizeof reinf_cap);
        g_ultrakill.director_valve_closed = 0;
    }
}

static void UltrakillSyncMissionTheater(uintptr_t gbase, uintptr_t director, float dt)
{
    /* Sync playable radius factor on 2x generated missions (~701 m = 0.685f default). */
    uintptr_t ctx = 0, lg = 0;
    uint32_t mode = 0;
    if (ReadPtr(gbase + UK_CONTEXT_RVA, &ctx) && ReadPtr(ctx + UK_CONTEXT_LG_OFF, &lg) &&
        ReadMem(lg + UK_LG_MODE_OFF, &mode, 4) && mode == 3u) {
        float cur_r = 0.0f, border_r = 0.0f;
        if (ReadMem(lg + UK_LG_PLAYABLE_RADIUS_OFF, &cur_r, 4) &&
            ReadMem(lg + UK_LG_BORDER_RADIUS_OFF, &border_r, 4) &&
            isfinite(cur_r) && isfinite(border_r) && border_r >= 0.60f) {
            float want_r = UltrakillResolveRadiusFactor(&g_host_opts);
            if (want_r > border_r - 0.01f) want_r = border_r - 0.01f;
            if (fabsf(want_r - cur_r) > 0.005f) {
                uint32_t bits = 0;
                memcpy(&bits, &want_r, 4);
                if (ChaosWriteDataU32(lg + UK_LG_PLAYABLE_RADIUS_OFF, bits)) {
                    cur_r = want_r;
                    float rm = cur_r * 1024.0f;
                    float area = 3.14159265f * rm * rm * 1e-6f;
                    hf->log(self, "CHAOS_ULTRAKILL_AREA: radius_factor=%.3f radius_m=%.1f area_km2=%.2f border_m=%.1f",
                            (double)cur_r, (double)rm, (double)area, (double)(border_r * 1024.0f));
                }
            }
            g_ultrakill.live_radius_factor = cur_r;
        }
    }

    /* Tight Director patrol & reinforcement valve (<= 620 walking AI):
     * Close native patrol/traveller/straggler caps and zero reinforce_budget whenever walking AI >= 580
     * (or live EM >= 680) so D12 3x outposts + Pelicans NEVER approach the 1,024 GwNav::Bot ceiling. */
    if (g_em_snap.valid &&
        (g_em_snap.walking_ai_slots >= UK_DIRECTOR_VALVE_STOP_WALKING || g_em_snap.live_slots >= 680u)) {
        UltrakillSyncDirectorPatrolValve(1);
        if (director) {
            float cur_budget = 0.0f;
            if (ReadMem(director + UK_DIR_REINFORCE_BUDGET_OFF, &cur_budget, 4) &&
                isfinite(cur_budget) && cur_budget > 0.0f) {
                uint32_t zero_bits = 0u;
                ChaosWriteDataU32(director + UK_DIR_REINFORCE_BUDGET_OFF, zero_bits);
            }
        }
    } else if (g_em_snap.valid &&
               g_em_snap.walking_ai_slots < UK_DIRECTOR_VALVE_RESUME_WALKING &&
               g_em_snap.live_slots < 620u) {
        UltrakillSyncDirectorPatrolValve(0);
        g_ultrakill.budget_refresh_s -= dt;
        if (g_ultrakill.budget_refresh_s <= 0.0f) {
            g_ultrakill.budget_refresh_s = 4.0f;
            float cur_budget = 0.0f;
            if (director &&
                ReadMem(director + UK_DIR_REINFORCE_BUDGET_OFF, &cur_budget, 4) &&
                isfinite(cur_budget) && cur_budget < 500.0f) {
                float topup = 750.0f;
                uint32_t bits = 0;
                memcpy(&bits, &topup, 4);
                ChaosWriteDataU32(director + UK_DIR_REINFORCE_BUDGET_OFF, bits);
            }
        }
    }
}

static void ServiceVirtualBattalions(uintptr_t gbase, int native, int diff,
                                     const float (*players)[3], int living, float dt)
{
    if (living <= 0) return;
    int live_diff = ChaosLiveMissionDifficulty(gbase);
    int want_virtual = UltrakillTargetVirtualUnitsForDiff(&g_host_opts, live_diff);
    float eff_rf = (g_ultrakill.live_radius_factor > 0.40f)
                   ? g_ultrakill.live_radius_factor
                   : UltrakillResolveRadiusFactor(&g_host_opts);
    float max_map_r = eff_rf * 1024.0f - 35.0f;
    if (max_map_r < 320.0f) max_map_r = 320.0f;
    if (max_map_r > 680.0f) max_map_r = 680.0f;

    g_ultrakill.salvo_budget_tokens += dt * 3.5f;
    if (g_ultrakill.salvo_budget_tokens > 3.0f) g_ultrakill.salvo_budget_tokens = 3.0f;
    uint32_t reserve_floor = g_em_snap.gwnav_reserve ? g_em_snap.gwnav_reserve : UK_EM_RESERVE_SLOTS;
    int can_fire_proj = (hf && hf->size >= offsetof(HfApi, spawn_projectile) + sizeof(hf->spawn_projectile) &&
                         hf->spawn_projectile != NULL &&
                         g_em_snap.valid && g_em_snap.free_slots >= reserve_floor);

    uint32_t active_regs = 0u, active_units = 0u;
    int first_free = -1;
    int last_enemy_idx = -1, last_clone_idx = -1;

    for (uint32_t i = 0; i < UK_MAX_VIRTUAL_BATTALIONS; i++) {
        ChaosVirtualBattalion* b = &g_ultrakill.battalions[i];
        if (!b->active) {
            if (first_free < 0) first_free = (int)i;
            continue;
        }
        b->ttl -= dt;
        if (b->ttl <= 0.0f || b->unit_count == 0u) {
            b->active = 0u;
            if (first_free < 0) first_free = (int)i;
            continue;
        }

        b->pos[0] += b->vel[0] * dt;
        b->pos[1] += b->vel[1] * dt;

        /* Clamp inside walkable planet disk */
        float r2_origin = b->pos[0] * b->pos[0] + b->pos[1] * b->pos[1];
        if (r2_origin > max_map_r * max_map_r) {
            float inv = max_map_r / sqrtf(r2_origin);
            b->pos[0] *= inv;
            b->pos[1] *= inv;
            b->vel[0] = -b->vel[0];
            b->vel[1] = -b->vel[1];
        }

        float nearest_d2 = 1e30f;
        int nearest_p = 0;
        for (int p = 0; p < living; p++) {
            float dx = b->pos[0] - players[p][0];
            float dy = b->pos[1] - players[p][1];
            float d2 = dx * dx + dy * dy;
            if (d2 < nearest_d2) { nearest_d2 = d2; nearest_p = p; }
        }

        /* Promote outer enemy regiment into a cohesive 18-24 unit real swarm when players close within 135 m! */
        {
            int eff_native = (native >= 0) ? native : g_native_faction;
            int fac_ready = (b->faction < CHAOS_FACTIONS) &&
                            g_rosters_loaded[b->faction] &&
                            (((int)b->faction == eff_native) ||
                             (g_faction_resident[b->faction] && PeersReadyForFaction(gbase, (int)b->faction)));
            if (config.enabled && fac_ready && nearest_d2 < 135.0f * 135.0f &&
                g_spawn_queue.count + 24u < UK_SPAWN_QUEUE_CAP &&
                g_em_snap.valid &&
                g_em_snap.free_slots >= reserve_floor + 28u &&
                g_em_snap.walking_ai_slots + 24u <= UK_MAX_WALKING_AI_CAP) {
                uint32_t flags = (b->faction == CHAOS_BUGS) ? 0x24u : 0x3Cu;
                int swarm_n = (b->unit_count >= 18u) ? (int)b->unit_count : 20;
                int promoted = EnqueueTheaterPlatoon((int)b->faction, diff, swarm_n, flags,
                                                     b->pos, players[nearest_p]);
                if (promoted > 0) {
                    g_ultrakill.promotions_total += (uint32_t)promoted;
                    b->active = 0u;
                    continue;
                }
            }
        }

        /* Steer into the visible 150..365 m battle ring framing the squad */
        if (nearest_d2 > 1.0f) {
            float dist = sqrtf(nearest_d2);
            float dir_x = (players[nearest_p][0] - b->pos[0]) / dist;
            float dir_y = (players[nearest_p][1] - b->pos[1]) / dist;
            float tangent_x = -dir_y;
            float tangent_y = dir_x;
            float radial = (dist > 365.0f) ? 3.2f : (dist < 150.0f ? -2.6f : 0.4f);
            float swirl = (b->faction == UK_VIRTUAL_FACTION_CLONES) ? 2.2f : -2.2f;
            b->vel[0] = dir_x * radial + tangent_x * swirl;
            b->vel[1] = dir_y * radial + tangent_y * swirl;
        }

        b->fire_cooldown -= dt;
        if (b->faction == UK_VIRTUAL_FACTION_CLONES) last_clone_idx = (int)i;
        else last_enemy_idx = (int)i;

        /* Exchange visible non-explosive laser salvos strictly inside 140..380 m around the squad */
        if (can_fire_proj && b->fire_cooldown <= 0.0f && g_ultrakill.salvo_budget_tokens >= 1.0f &&
            nearest_d2 >= 140.0f * 140.0f && nearest_d2 <= 380.0f * 380.0f) {
            int opp_idx = (b->faction == UK_VIRTUAL_FACTION_CLONES) ? last_enemy_idx : last_clone_idx;
            if (opp_idx >= 0 && g_ultrakill.battalions[opp_idx].active) {
                const ChaosVirtualBattalion* opp = &g_ultrakill.battalions[opp_idx];
                float dx = opp->pos[0] - b->pos[0];
                float dy = opp->pos[1] - b->pos[1];
                float len2d = sqrtf(dx * dx + dy * dy);
                if (len2d >= 20.0f && len2d <= 280.0f) {
                    float base_z = players[nearest_p][2] + 12.0f;
                    if (base_z < 16.0f) base_z = 16.0f;
                    float muzzle[3] = {b->pos[0], b->pos[1], base_z};
                    float dir[3] = {dx / len2d, dy / len2d, -0.03f};
                    uint32_t proj = (b->faction == UK_VIRTUAL_FACTION_CLONES)
                                    ? HF_PROJECTILE_LAS12_SAI
                                    : HF_PROJECTILE_LAS58_TALON;
                    hf->spawn_projectile(self, proj, muzzle, dir);
                    g_ultrakill.salvo_budget_tokens -= 1.0f;
                    ++g_ultrakill.salvos_fired_total;
                    b->fire_cooldown = 1.05f + (float)(NextRand() % 800u) * 0.001f;
                }
            }
        }

        ++active_regs;
        active_units += b->unit_count;
    }

    /* Spawn up to 8 new outer-ring regiments per tick in the 145..375 m ring until virtual force meets target */
    int spawns_left = 8;
    while ((int)active_units < want_virtual && spawns_left-- > 0) {
        int slot = -1;
        for (uint32_t i = (first_free >= 0 ? (uint32_t)first_free : 0u); i < UK_MAX_VIRTUAL_BATTALIONS; i++) {
            if (!g_ultrakill.battalions[i].active) { slot = (int)i; first_free = (int)i + 1; break; }
        }
        if (slot < 0) break;

        int pi = (int)(NextRand() % (uint32_t)living);
        float angle = (float)(NextRand() % 3600u) * (3.14159265f / 1800.0f);
        float radius = 145.0f + (float)(NextRand() % 230u); /* 145..375 m visible horizon ring */
        float px = players[pi][0] + radius * cosf(angle);
        float py = players[pi][1] + radius * sinf(angle);
        float r2 = px * px + py * py;
        if (r2 > max_map_r * max_map_r) {
            float inv = (max_map_r - 20.0f) / sqrtf(r2);
            px *= inv;
            py *= inv;
        }

        ChaosVirtualBattalion* nb = &g_ultrakill.battalions[slot];
        memset(nb, 0, sizeof *nb);
        nb->active = 1u;
        if ((NextRand() % 100u) < 40u) {
            nb->faction = UK_VIRTUAL_FACTION_CLONES;
        } else {
            int ef = ChaosChooseForeignRotated(&config, native, diff, g_last_foreign_faction, NextRand());
            if (ef < 0 || (NextRand() % 100u) < 35u) ef = native;
            nb->faction = (uint8_t)((ef >= 0 && ef < CHAOS_FACTIONS) ? ef : 0);
        }
        nb->unit_count = (uint8_t)(10u + (NextRand() % 5u));
        nb->pos[0] = px;
        nb->pos[1] = py;
        nb->pos[2] = players[pi][2] + 2.0f;
        nb->fire_cooldown = 0.25f + (float)(NextRand() % 900u) * 0.001f;
        nb->ttl = 65.0f + (float)(NextRand() % 40u);
        ++active_regs;
        active_units += nb->unit_count;
    }

    g_ultrakill.virtual_regiments_active = active_regs;
    g_ultrakill.virtual_units_total = active_units;
}

/* ---- Deliverable 5A: Periodic Friendly Eagle Storm & Enemy Gunship Strafing Runs (D11 & D12) ------------------- */

/* Requests & verifies auxiliary mission package bundles strictly via refmap-checked CallPkgRequest + ChaosRecordOwnedPkg
 * (never hf->load_package, avoiding both 60 Hz non-catalogue log floods and permanent session package leaks). */
static int ChaosEnsureOwnedAuxBundle(uintptr_t gbase, uintptr_t ebase, size_t esize,
                                     const uint64_t* pkgs, int pkg_count,
                                     int* io_requested, int* io_resident)
{
    if (*io_resident) return 1;
    uint64_t unique_pkgs[CHAOS_MAX_AUX_BUNDLE_PKGS];
    int unique_cnt = 0;
    for (int i = 0; i < pkg_count; ++i) {
        uint64_t pid = pkgs[i];
        if (pid == 0ULL || pid == ~0ULL) continue;
        int dup = 0;
        for (int j = 0; j < unique_cnt; ++j) {
            if (unique_pkgs[j] == pid) { dup = 1; break; }
        }
        if (!dup && unique_cnt < CHAOS_MAX_AUX_BUNDLE_PKGS) {
            unique_pkgs[unique_cnt++] = pid;
        }
    }
    if (unique_cnt == 0) return 0;

    if (!*io_requested && gbase && g_pins_ok) {
        uintptr_t pkg_sys = 0, map_addr = 0;
        uint32_t map_cap = 0;
        uint64_t empty_key = 0;
        int heap_owned = 0;
        uint8_t* map_buf = ChaosReadRefMapBuffer(gbase, &pkg_sys, &map_addr, &map_cap, &empty_key, &heap_owned);
        if (map_buf && pkg_sys) {
            int all_handled = 1;
            for (int k = 0; k < unique_cnt; ++k) {
                uint64_t pid = unique_pkgs[k];
                if (ChaosOwnsPkg(pid)) continue;
                uint32_t used = 0, slot_idx = 0xFFFFFFFFu;
                uint64_t refcnt = 0ULL;
                int already = 0;
                if (!ChaosRefMapInspect(map_buf, map_cap, empty_key, pid, &used, &already, &slot_idx, &refcnt)) {
                    all_handled = 0;
                    break;
                }
                ChaosRecordOwnedPkg(pid);
                CallPkgRequest(gbase, (void*)pkg_sys, &pid, 1u);
                if (!ReadMem(map_addr, map_buf, (size_t)map_cap * 16u)) {
                    all_handled = 0;
                    break;
                }
            }
            if (all_handled) *io_requested = 1;
        }
        if (heap_owned && map_buf) free(map_buf);
    }

    if (*io_requested) {
        int ready = CallPkgsIdle(gbase) != 0;
        for (int k = 0; ready && k < unique_cnt; ++k) {
            if (PackageStateExe(ebase, esize, unique_pkgs[k]) != 2) ready = 0;
        }
        if (ready) {
            *io_resident = 1;
            return 1;
        }
    }
    return 0;
}

static int ChaosBuildEagleAirBundle(uintptr_t gbase, uint64_t out_pkgs[CHAOS_MAX_AUX_BUNDLE_PKGS])
{
    int n = 0;
    uintptr_t srow = 0;
    uint64_t row_pkg = 0ULL;
    if (gbase && ReadPtr(gbase + CHAOS_STRAT_TABLE_RVA + (uintptr_t)HF_STRATAGEM_EAGLE_STRAFING_RUN * 8u, &srow) &&
        srow != 0u && ReadMem(srow + 0xA8u, &row_pkg, 8) && row_pkg != 0ULL && row_pkg != ~0ULL) {
        out_pkgs[n++] = row_pkg;
    }
    out_pkgs[n++] = CHAOS_PKG_EAGLE_BASE;
    return n;
}

static int ChaosBuildAtteBundle(uintptr_t gbase, uint64_t out_pkgs[CHAOS_MAX_AUX_BUNDLE_PKGS])
{
    int n = 0;
    if (gbase && g_pins_ok) {
        const uint8_t* rec = CallLookupEntityPkgs(gbase, CHAOS_ENTITY_EXO49_ATTE);
        if (rec) {
            uint64_t p0 = 0ULL, p1 = 0ULL;
            if (ReadMem((uintptr_t)rec + 8u, &p0, 8) && p0 != 0ULL && p0 != ~0ULL) out_pkgs[n++] = p0;
            if (ReadMem((uintptr_t)rec + 16u, &p1, 8) && p1 != 0ULL && p1 != ~0ULL) out_pkgs[n++] = p1;
        }
    }
    out_pkgs[n++] = CHAOS_PKG_COMBAT_WALKER;
    out_pkgs[n++] = CHAOS_PKG_EXO49_ATTE;
    return n;
}

static int ChaosPointClearOfPlayers(const float pt[3], const float (*players)[3], int living, float min_r)
{
    float r2 = min_r * min_r;
    for (int i = 0; i < living; ++i) {
        float dx = pt[0] - players[i][0];
        float dy = pt[1] - players[i][1];
        if (dx * dx + dy * dy < r2) return 0;
    }
    return 1;
}

static int ChaosPickAirRunTarget(uintptr_t gbase, const float (*players)[3], int living,
                                 float min_r, float max_r, float min_clear_r,
                                 float out_pos[3], float out_dir[2])
{
    if (living <= 0) return 0;
    /* Prefer a live tracked enemy inside min_r..max_r relative to the NEAREST living co-op player */
    int limit = (g_tracker.high_water > 0 && g_tracker.high_water <= CHAOS_MAX_TRACKED)
                ? g_tracker.high_water : CHAOS_MAX_TRACKED;
    for (int i = 0; i < limit; ++i) {
        if (!g_tracker.units[i].active) continue;
        float epos[3];
        if (!EntityPosition(gbase, g_tracker.units[i].entity, epos)) continue;
        if (!ChaosPointClearOfPlayers(epos, players, living, min_clear_r)) continue;
        float best_d2 = 1e30f;
        int best_p = 0;
        for (int p = 0; p < living; ++p) {
            float pdx = epos[0] - players[p][0];
            float pdy = epos[1] - players[p][1];
            float pd2 = pdx * pdx + pdy * pdy;
            if (pd2 < best_d2) {
                best_d2 = pd2;
                best_p = p;
            }
        }
        if (best_d2 >= min_r * min_r && best_d2 <= max_r * max_r && best_d2 > 1.0f) {
            float dx = epos[0] - players[best_p][0];
            float dy = epos[1] - players[best_p][1];
            float d = sqrtf(best_d2);
            memcpy(out_pos, epos, 12);
            out_dir[0] = -dy / d;
            out_dir[1] = dx / d;
            return 1;
        }
    }

    uintptr_t em = 0;
    int has_em = ReadPtr(gbase + CHAOS_ENCOUNTER_RVA, &em);
    int pi = (int)(NextRand() % (uint32_t)living);
    for (int attempt = 0; attempt < 12; ++attempt) {
        float ang = (float)(NextRand() % 3600u) * (3.14159265f / 1800.0f);
        float rad = min_r + (float)(NextRand() % 1000u) * 0.001f * (max_r - min_r);
        float cand[3] = {
            players[pi][0] + rad * cosf(ang),
            players[pi][1] + rad * sinf(ang),
            players[pi][2]
        };
        if (has_em) {
            float proj[3] = {0.0f, 0.0f, 0.0f};
            CallProjectAnchor(gbase, (void*)em, proj, cand);
            if (isfinite(proj[0]) && isfinite(proj[1]) && isfinite(proj[2]) &&
                !(proj[0] == 0.0f && proj[1] == 0.0f && proj[2] == 0.0f)) {
                memcpy(cand, proj, 12);
            }
        }
        if (!ChaosPointClearOfPlayers(cand, players, living, min_clear_r)) continue;
        memcpy(out_pos, cand, 12);
        out_dir[0] = -sinf(ang);
        out_dir[1] = cosf(ang);
        return 1;
    }
    return 0;
}

static int ChaosTryDispatchNativeEagleBeacon(uintptr_t gbase, const float target_pos[3])
{
#ifdef CHAOS_UNIT_TEST
    if (g_mock.activate_beacon_override) {
        return g_mock.activate_beacon_override(HF_STRATAGEM_EAGLE_STRAFING_RUN, target_pos);
    }
    (void)gbase;
    return 0;
#else
    static const uint8_t kSearchPin[30] = {
        0x40, 0x55, 0x53, 0x56, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x48, 0x8d, 0x6c, 0x24, 0x80,
        0x48, 0x81, 0xec, 0x80, 0x01, 0x00, 0x00, 0x48, 0x8b, 0x05, 0xf2, 0x30, 0xf9, 0x01
    };
    static const uint8_t kQueryPin[25] = {
        0x48, 0x8b, 0xc4, 0x48, 0x89, 0x58, 0x20, 0x55, 0x56, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56,
        0x41, 0x57, 0x48, 0x8d, 0xa8, 0x48, 0xfe, 0xff, 0xff
    };
    static const char kDispatchPat[] =
        "48 89 5C 24 18 55 56 57 41 54 41 55 41 56 41 57 48 8D AC 24 B0 FC FF FF 48 81 EC 50 04 00 00 48 8B 05 3A 02 F9 01";
    static uint8_t s_ctl[0x8E8];
    static uint8_t s_front[0x8A0];
    static uint8_t s_mgr_raw[160];

    if (!hf || !hf->read_code) return 0;
    uint8_t sb[30], qb[25];
    if (hf->read_code(gbase + 0x6A8F00u, sb, sizeof sb) != 0 || memcmp(sb, kSearchPin, sizeof sb) != 0 ||
        hf->read_code(gbase + 0x13CAA80u, qb, sizeof qb) != 0 || memcmp(qb, kQueryPin, sizeof qb) != 0)
        return 0;

    int disp_ok = (hf->size >= offsetof(HfApi, code_matches) + sizeof(hf->code_matches) &&
                   hf->code_matches != NULL && hf->code_matches(gbase + 0x6ABDB0u, kDispatchPat));
    if (!disp_ok) return 0;

    uintptr_t rec = 0, pm = 0, em = 0, player_obj = 0, manager = 0, world = 0;
    uint32_t def_id = 0, pcount = 0, s = 0, owner = 0, character = 0, invalid = 0;
    if (!ReadPtr(gbase + 0x37CB600u + (uintptr_t)HF_STRATAGEM_EAGLE_STRAFING_RUN * 8u, &rec) ||
        !ReadMem(rec, &def_id, 4) || def_id != HF_STRATAGEM_EAGLE_STRAFING_RUN)
        return 0;
    if (!ReadMem(gbase + 0x3483C34u, &invalid, 4)) return 0;
    if (!ReadPtr(gbase + CHAOS_PLAYERS_RVA, &pm) || !ReadMem(pm + 0x84u, &pcount, 4) || pcount == 0 || pcount > 4u ||
        !ReadPtr(gbase + CHAOS_ENTITIES_RVA, &em))
        return 0;

    uint64_t local_peer = 0;
    uintptr_t user_ptr = 0;
    if (ReadPtr(gbase + CHAOS_LOCAL_USER_RVA, &user_ptr)) ReadMem(user_ptr + 0xB398u, &local_peer, 8);
    CdEmLayout em_layout = cd_em_resolve_hf(gbase, hf->read);
    int picked = 0;
    for (s = 0; s < pcount && !picked; ++s) {
        uint64_t speer = 0;
        uint32_t slife = 0, snet = 0, sowner = 0, sidx = 0, char_id = 0;
        if (!ReadMem(pm + 0x2C8u + (uintptr_t)s * 0x38u, &speer, 8)) continue;
        if (local_peer != 0ULL && speer != local_peer) continue;
        if (!ReadMem(pm + 0x2E0u + (uintptr_t)s * 0x38u, &slife, 4) || slife != 3u) continue;
        if (!ReadMem(pm + 0x3A8u + (uintptr_t)s * 0x20u, &snet, 4) || snet == 0x7FFFu) continue;
        if (!ReadPtr(pm + 0xE8u + (uintptr_t)s * 8u, &player_obj) || !ReadMem(player_obj + 8u, &sowner, 4)) continue;
        if (!HashFindU32(em + 0xF22EC8u, snet, &sidx) || sidx >= em_layout.capacity) continue;
        uintptr_t drec = cd_em_record_at(em, em_layout.records_offset, sidx);
        if (!drec || !ReadMem(drec + 8u, &char_id, 4) || char_id == 0u || char_id == 0xFFFFFFFFu || char_id == invalid)
            continue;
        owner = sowner;
        character = char_id;
        picked = 1;
    }
    if (!picked) return 0;

    uint8_t* mgr_copy = (uint8_t*)(((uintptr_t)s_mgr_raw + 15u) & ~(uintptr_t)15u);
    if (!ReadPtr(gbase + 0x33266B0u, &manager) || !ReadMem(manager, mgr_copy, 128)) return 0;
    memcpy(&world, mgr_copy + 8u, 8);
    if (!world) return 0;

    memset(s_ctl, 0, sizeof s_ctl);
    uintptr_t ctl_va = (uintptr_t)s_ctl;
    memcpy(mgr_copy + 0x68u, &ctl_va, 8);
    memcpy(s_ctl + 0x30u, target_pos, 12);

    uint8_t src[60];
    memset(src, 0, sizeof src);
    uint32_t stype = HF_STRATAGEM_EAGLE_STRAFING_RUN;
    memcpy(src + 0u, &stype, 4);
    memcpy(src + 8u, &invalid, 4);
    memcpy(src + 12u, &character, 4);
    memcpy(src + 16u, target_pos, 12);
    memcpy(src + 28u, &invalid, 4);
    float approach[3] = {target_pos[0], target_pos[1], target_pos[2]};

    typedef unsigned char (*SearchFn)(void*, uint32_t, const void*, float*, int);
    SearchFn search_fn = (SearchFn)(gbase + 0x6A8F00u);
    if (!search_fn(mgr_copy, 0u, src, approach, 1) ||
        !isfinite(approach[0]) || !isfinite(approach[1]) || !isfinite(approach[2]))
        return 0;

    memcpy(s_front, s_ctl + 0x40u, sizeof s_front);
    memcpy(s_front + 0x3A4u, &character, 4);
    memcpy(s_front + 0x3B4u, &invalid, 4);
    memcpy(s_front + 0x3F8u, &invalid, 4);

    HfFn_ActivateStratagemBeacon dispatch_fn =
        (hf->size >= offsetof(HfApi, hook_function) + sizeof(void*) && hf->function != NULL)
            ? hf_fn_ActivateStratagemBeacon(self)
            : NULL;
    if (!dispatch_fn) dispatch_fn = (HfFn_ActivateStratagemBeacon)(gbase + 0x6ABDB0u);
    (void)dispatch_fn((void*)manager, -1, owner, approach, s_front, stype, 0ULL, 0ULL, 0ULL, 0ULL);
    return 1;
#endif
}

static void ServiceTacticalAirRuns(uintptr_t gbase, uintptr_t ebase, size_t esize, int live_diff,
                                   const float (*living_pos)[3], int living_players, float dt)
{
    if (!Host() || !landed || living_players <= 0 || !config.air_runs_enabled) {
        g_air_runs.strafe_burst_rem = 0;
        return;
    }
    if (live_diff < 11 && !ChaosIsUltrakillActiveInMission(gbase)) return;

    ChaosRefreshEmSnapshot(gbase, clock_state.age);
    uint32_t reserve_floor = g_em_snap.gwnav_reserve ? g_em_snap.gwnav_reserve : UK_EM_RESERVE_SLOTS;
    int can_proj = (hf && hf->size >= offsetof(HfApi, spawn_projectile) + sizeof(hf->spawn_projectile) &&
                    hf->spawn_projectile != NULL &&
                    g_em_snap.valid && g_em_snap.free_slots >= reserve_floor + 8u);

    /* Drain active multi-bolt fighter / gunship strafing pass along ground track */
    if (g_air_runs.strafe_burst_rem > 0) {
        g_air_runs.strafe_step_cd_s -= dt;
        while (g_air_runs.strafe_burst_rem > 0 && g_air_runs.strafe_step_cd_s <= 0.0f) {
            g_air_runs.strafe_step_cd_s += 0.07f;
            float along = ((float)g_air_runs.strafe_step_idx - 3.5f) * 4.5f;
            float impact[3] = {
                g_air_runs.strafe_start[0] + g_air_runs.strafe_dir[0] * along,
                g_air_runs.strafe_start[1] + g_air_runs.strafe_dir[1] * along,
                g_air_runs.strafe_start[2]
            };
            if (can_proj && ChaosPointClearOfPlayers(impact, living_pos, living_players, 22.0f)) {
                float alt = g_air_runs.strafe_is_friendly ? 36.0f : 40.0f;
                float muzzle[3] = {
                    impact[0] - g_air_runs.strafe_dir[0] * 28.0f,
                    impact[1] - g_air_runs.strafe_dir[1] * 28.0f,
                    impact[2] + alt
                };
                float dx = impact[0] - muzzle[0];
                float dy = impact[1] - muzzle[1];
                float dz = (impact[2] + 0.8f) - muzzle[2];
                float len = sqrtf(dx * dx + dy * dy + dz * dz);
                if (len > 1.0f) {
                    float dir[3] = {dx / len, dy / len, dz / len};
                    uint32_t proj = g_air_runs.strafe_is_friendly
                                    ? ((g_air_runs.strafe_step_idx & 1)
                                       ? HF_PROJECTILE_MG206_HEAVY_MACHINE_GUN
                                       : HF_PROJECTILE_LAS12_SAI)
                                    : HF_PROJECTILE_LAS58_TALON;
                    hf->spawn_projectile(self, proj, muzzle, dir);
                }
            }
            ++g_air_runs.strafe_step_idx;
            --g_air_runs.strafe_burst_rem;
        }
    }

    {
        uint64_t eagle_pkgs[CHAOS_MAX_AUX_BUNDLE_PKGS];
        int eagle_cnt = ChaosBuildEagleAirBundle(gbase, eagle_pkgs);
        (void)ChaosEnsureOwnedAuxBundle(gbase, ebase, esize, eagle_pkgs, eagle_cnt,
                                        &g_air_runs.eagle_pkg_requested, &g_air_runs.eagle_pkg_resident);
    }

    if (g_air_runs.friendly_cd_s <= 0.0f) {
        g_air_runs.friendly_cd_s = 12.0f + (float)(NextRand() % 8u);
    } else {
        g_air_runs.friendly_cd_s -= dt;
        if (g_air_runs.friendly_cd_s <= 0.0f) {
            g_air_runs.friendly_cd_s = 32.0f + (float)(NextRand() % 14u);
            float tpos[3], tdir[2];
            if (g_em_snap.valid && g_em_snap.free_slots >= reserve_floor + 16u &&
                ChaosPickAirRunTarget(gbase, living_pos, living_players, 48.0f, 118.0f, 26.0f, tpos, tdir)) {
                int native_eagle = g_air_runs.eagle_pkg_resident
                                   ? ChaosTryDispatchNativeEagleBeacon(gbase, tpos)
                                   : 0;
                if (g_air_runs.strafe_burst_rem == 0) {
                    g_air_runs.strafe_is_friendly = 1;
                    g_air_runs.strafe_burst_rem = 8;
                    g_air_runs.strafe_step_idx = 0;
                    g_air_runs.strafe_step_cd_s = 0.0f;
                    memcpy(g_air_runs.strafe_start, tpos, 12);
                    memcpy(g_air_runs.strafe_dir, tdir, 8);
                }
                ++g_ultrakill.friendly_air_runs_total;
                hf->log(self, "CHAOS_AIR_RUN: side=friendly_eagle_storm beacon=%d pos=(%.1f,%.1f,%.1f) total=%u",
                        native_eagle, (double)tpos[0], (double)tpos[1], (double)tpos[2],
                        g_ultrakill.friendly_air_runs_total);
            }
        }
    }

    if (g_air_runs.enemy_cd_s <= 0.0f) {
        g_air_runs.enemy_cd_s = 18.0f + (float)(NextRand() % 10u);
    } else {
        g_air_runs.enemy_cd_s -= dt;
        if (g_air_runs.enemy_cd_s <= 0.0f) {
            g_air_runs.enemy_cd_s = 36.0f + (float)(NextRand() % 16u);
            float tpos[3], tdir[2];
            if (can_proj && g_air_runs.strafe_burst_rem == 0 &&
                ChaosPickAirRunTarget(gbase, living_pos, living_players, 38.0f, 105.0f, 24.0f, tpos, tdir)) {
                g_air_runs.strafe_is_friendly = 0;
                g_air_runs.strafe_burst_rem = 8;
                g_air_runs.strafe_step_idx = 0;
                g_air_runs.strafe_step_cd_s = 0.0f;
                memcpy(g_air_runs.strafe_start, tpos, 12);
                memcpy(g_air_runs.strafe_dir, tdir, 8);
                ++g_ultrakill.enemy_air_runs_total;
                hf->log(self, "CHAOS_AIR_RUN: side=enemy_gunship_strafe pos=(%.1f,%.1f,%.1f) total=%u",
                        (double)tpos[0], (double)tpos[1], (double)tpos[2],
                        g_ultrakill.enemy_air_runs_total);
            }
        }
    }
}

/* ---- Deliverable 5B: Autonomous Friendly Republic AT-TE Fire-Support Walker Prototype (D12 Clones) ------------ */

static void DespawnAtteWalkerIfActive(uintptr_t gbase)
{
    if (g_atte.entity != 0u && g_atte.entity != 0xFFFFFFFFu && gbase && g_pins_ok && hf && hf->read) {
        uintptr_t em = 0;
        if (ReadPtr(gbase + CHAOS_ENTITIES_RVA, &em)) {
            CallRemoveEntity(gbase, (void*)em, g_atte.entity);
        }
    }
    g_atte.active = 0;
    g_atte.entity = 0u;
}

static int SpawnAutonomousAtteHull(uintptr_t gbase, const float pos[3], float heading_rad, uint32_t* out_eid)
{
    if (out_eid) *out_eid = 0u;
    if (!gbase || !g_pins_ok || !g_em_snap.valid || !g_em_snap.em_root) return 0;

    uint16_t ctor_ready = 1u;
#ifndef CHAOS_UNIT_TEST
    if (!ReadMem(g_em_snap.em_root + 0xF3F828u, &ctor_ready, sizeof ctor_ready) || ctor_ready != 1u) {
        return 0;
    }
#else
    (void)ctor_ready;
#endif
    memset(s_atte_spawn_ctx, 0, sizeof s_atte_spawn_ctx);
    memset(s_atte_spawn_creation, 0, sizeof s_atte_spawn_creation);
    s_atte_spawn_creation[0] = 1u;
    uint32_t unit_sentinel = 0x7FFFu;
    memcpy(s_atte_spawn_creation + 4u, &unit_sentinel, 4u);

    float c = cosf(heading_rad);
    float s = sinf(heading_rad);
    float transform[16] = {
        c,    s,    0.0f, 0.0f,
       -s,    c,    0.0f, 0.0f,
        0.0f, 0.0f, 1.0f, 0.0f,
        pos[0], pos[1], pos[2], 1.0f
    };
    memcpy(s_atte_spawn_creation + 8u, transform, sizeof transform);
    void* ctx_ptr = s_atte_spawn_ctx;
    memcpy(s_atte_spawn_creation + 0x48u, &ctx_ptr, sizeof ctx_ptr);

    uint32_t spawned_eid = 0u;
    CallRequestEntitySpawn(gbase, &spawned_eid, CHAOS_ENTITY_EXO49_ATTE, s_atte_spawn_creation);
    if (spawned_eid == 0u || spawned_eid == 0xFFFFFFFFu) return 0;
    if (out_eid) *out_eid = spawned_eid;
    return 1;
}

static void ServiceAutonomousAtteWalker(uintptr_t gbase, uintptr_t ebase, size_t esize, int live_diff,
                                        const float (*living_pos)[3], int living_players, float dt)
{
    int want_atte = Host() &&
                    landed &&
                    living_players > 0 &&
                    live_diff >= 12 &&
                    config.atte_walker_support != 0 &&
                    InterlockedCompareExchange(&g_empire_mode_active, 0, 0) == 0;
    if (!want_atte) {
        if (g_atte.active || g_atte.entity != 0u) {
            DespawnAtteWalkerIfActive(gbase);
        }
        return;
    }

    ChaosRefreshEmSnapshot(gbase, clock_state.age);
    uint32_t reserve_floor = g_em_snap.gwnav_reserve ? g_em_snap.gwnav_reserve : UK_EM_RESERVE_SLOTS;
    {
        uint64_t atte_pkgs[CHAOS_MAX_AUX_BUNDLE_PKGS];
        int atte_cnt = ChaosBuildAtteBundle(gbase, atte_pkgs);
        if (!ChaosEnsureOwnedAuxBundle(gbase, ebase, esize, atte_pkgs, atte_cnt,
                                       &g_atte.pkg_requested, &g_atte.pkg_resident)) {
            return;
        }
    }

    if (g_atte.redeploy_cd_s > 0.0f) g_atte.redeploy_cd_s -= dt;
    if (g_atte.spawn_retry_s > 0.0f) g_atte.spawn_retry_s -= dt;

    /* Deploy or leapfrog-redeploy outer-perimeter Republic AT-TE fire-support walker (78..112 m from squad).
     * Never latches g_atte.active or fires salvos unless SpawnAutonomousAtteHull yields a valid live hull entity! */
    if (!g_atte.active && g_atte.spawn_retry_s <= 0.0f &&
        g_em_snap.valid && g_em_snap.free_slots >= reserve_floor + 16u) {
        g_atte.spawn_retry_s = 3.0f;
        float wpos[3], wdir[2];
        if (ChaosPickAirRunTarget(gbase, living_pos, living_players, 78.0f, 112.0f, 46.0f, wpos, wdir)) {
            int near_p = 0;
            float near_d2 = 1e30f;
            for (int p = 0; p < living_players; ++p) {
                float pdx = living_pos[p][0] - wpos[0];
                float pdy = living_pos[p][1] - wpos[1];
                float pd2 = pdx * pdx + pdy * pdy;
                if (pd2 < near_d2) { near_d2 = pd2; near_p = p; }
            }
            float hdg = atan2f(living_pos[near_p][1] - wpos[1], living_pos[near_p][0] - wpos[0]);
            uint32_t eid = 0u;
            if (SpawnAutonomousAtteHull(gbase, wpos, hdg, &eid) && eid != 0u && eid != 0xFFFFFFFFu) {
                g_atte.active = 1;
                g_atte.entity = eid;
                memcpy(g_atte.pos, wpos, 12);
                g_atte.heading_rad = hdg;
                g_atte.fire_cd_s = 2.0f;
                g_atte.redeploy_cd_s = 20.0f;
                hf->log(self, "CHAOS_ATTE_DEPLOY: entity=%u pos=(%.1f,%.1f,%.1f) mode=autonomous_perimeter_leapfrog",
                        eid, (double)wpos[0], (double)wpos[1], (double)wpos[2]);
            }
        }
    }
    if (!g_atte.active || g_atte.entity == 0u || g_atte.entity == 0xFFFFFFFFu) return;

    /* Require a live physical hull with a valid world transform every tick before firing any salvo */
    if (!EntityAlive(g_atte.entity, NULL)) {
        g_atte.active = 0;
        g_atte.entity = 0u;
        g_atte.spawn_retry_s = 18.0f;
        return;
    }
    {
        float live_hull[3];
        if (!EntityPosition(gbase, g_atte.entity, live_hull)) {
            DespawnAtteWalkerIfActive(gbase);
            g_atte.spawn_retry_s = 6.0f;
            return;
        }
        memcpy(g_atte.pos, live_hull, 12);
    }

    float min_player_d2 = 1e30f;
    for (int p = 0; p < living_players; ++p) {
        float dx = g_atte.pos[0] - living_pos[p][0];
        float dy = g_atte.pos[1] - living_pos[p][1];
        float d2 = dx * dx + dy * dy;
        if (d2 < min_player_d2) min_player_d2 = d2;
    }

    /* Yield immediately if any player approaches within 18m (inspecting / boarding safety) */
    if (min_player_d2 < 18.0f * 18.0f) return;

    /* When the squad advances > 165m away from the stationary fire-support hull, leapfrog AT-TE forward on perimeter */
    if (min_player_d2 > 165.0f * 165.0f && g_atte.redeploy_cd_s <= 0.0f) {
        DespawnAtteWalkerIfActive(gbase);
        g_atte.spawn_retry_s = 0.25f;
        return;
    }

    /* Fire heavy dorsal cannon + quad laser turret salvos from the live hull transform every 3.6..4.8s */
    g_atte.fire_cd_s -= dt;
    if (g_atte.fire_cd_s <= 0.0f &&
        hf && hf->size >= offsetof(HfApi, spawn_projectile) + sizeof(hf->spawn_projectile) &&
        hf->spawn_projectile != NULL &&
        g_em_snap.valid && g_em_snap.free_slots >= reserve_floor + 8u) {
        g_atte.fire_cd_s = 3.6f + (float)(NextRand() % 1200u) * 0.001f;

        float target[3] = {0.0f, 0.0f, 0.0f};
        int have_target = 0;
        int limit = (g_tracker.high_water > 0 && g_tracker.high_water <= CHAOS_MAX_TRACKED)
                    ? g_tracker.high_water : CHAOS_MAX_TRACKED;
        for (int i = 0; i < limit; ++i) {
            if (!g_tracker.units[i].active) continue;
            float epos[3];
            if (!EntityPosition(gbase, g_tracker.units[i].entity, epos)) continue;
            if (!ChaosPointClearOfPlayers(epos, living_pos, living_players, 22.0f)) continue;
            float dx = epos[0] - g_atte.pos[0];
            float dy = epos[1] - g_atte.pos[1];
            float d2 = dx * dx + dy * dy;
            if (d2 >= 28.0f * 28.0f && d2 <= 205.0f * 205.0f) {
                memcpy(target, epos, 12);
                have_target = 1;
                break;
            }
        }
        if (!have_target && g_has_last_wave_anchor &&
            ChaosPointClearOfPlayers(g_last_wave_anchor, living_pos, living_players, 24.0f)) {
            float dx = g_last_wave_anchor[0] - g_atte.pos[0];
            float dy = g_last_wave_anchor[1] - g_atte.pos[1];
            float d2 = dx * dx + dy * dy;
            if (d2 >= 25.0f * 25.0f && d2 <= 220.0f * 220.0f) {
                memcpy(target, g_last_wave_anchor, 12);
                have_target = 1;
            }
        }
        if (!have_target) {
            float tdir[2];
            if (ChaosPickAirRunTarget(gbase, living_pos, living_players, 95.0f, 165.0f, 32.0f, target, tdir)) {
                float dx = target[0] - g_atte.pos[0];
                float dy = target[1] - g_atte.pos[1];
                if (dx * dx + dy * dy >= 25.0f * 25.0f) have_target = 1;
            }
        }

        if (have_target) {
            float dorsal_muzzle[3] = {g_atte.pos[0], g_atte.pos[1], g_atte.pos[2] + 4.8f};
            float dx = target[0] - dorsal_muzzle[0];
            float dy = target[1] - dorsal_muzzle[1];
            float dz = (target[2] + 1.0f) - dorsal_muzzle[2];
            float len = sqrtf(dx * dx + dy * dy + dz * dz);
            if (len > 5.0f) {
                float dir[3] = {dx / len, dy / len, dz / len};
                float side_x = -dir[1], side_y = dir[0];
                /* 2 dorsal heavy mass-driver kinetic slugs + 4 forward anti-personnel Republic laser bolts */
                hf->spawn_projectile(self, HF_PROJECTILE_MG206_HEAVY_MACHINE_GUN, dorsal_muzzle, dir);
                dorsal_muzzle[2] += 0.25f;
                hf->spawn_projectile(self, HF_PROJECTILE_MG206_HEAVY_MACHINE_GUN, dorsal_muzzle, dir);
                for (int b = 0; b < 4; ++b) {
                    float off = ((b & 1) ? 1.35f : -1.35f);
                    float zoff = (b < 2) ? 2.9f : 3.3f;
                    float ball_muzzle[3] = {
                        g_atte.pos[0] + side_x * off,
                        g_atte.pos[1] + side_y * off,
                        g_atte.pos[2] + zoff
                    };
                    hf->spawn_projectile(self, HF_PROJECTILE_LAS12_SAI, ball_muzzle, dir);
                }
                ++g_ultrakill.atte_salvos_total;
            }
        }
    }
}

static void ServiceUltrakillTheater(uintptr_t gbase, uintptr_t director, int native, int diff,
                                    const float (*living_pos)[3], int living_players, float dt)
{
    if (!Host() || !landed || !ChaosIsUltrakillActiveInMission(gbase)) return;

    if (native >= 0 && native < CHAOS_FACTIONS && !g_rosters_loaded[native]) {
        if (ResolveCuratedRosterForFaction(gbase, native)) {
            g_faction_resident[native] = 1;
        }
    }

    ChaosRefreshEmSnapshot(gbase, clock_state.age);
    UltrakillSyncMissionTheater(gbase, director, dt);
    DrainTheaterSpawnQueue(gbase, director);

    int has_remote = 0;
    if (IsCoopHost()) {
        for (int i = 0; i < CHAOS_MAX_PEERS; i++) {
            if (g_peer_sync[i].active) { has_remote = 1; break; }
        }
    }

    int target_real = UltrakillTargetRealAiCap(&g_host_opts, has_remote);
    int cur_walking = g_em_snap.valid ? (int)g_em_snap.walking_ai_slots : g_tracker.total_live;

    g_ultrakill.surge_accum_s += dt;
    if (g_ultrakill.surge_accum_s >= 1.05f && living_players > 0) {
        g_ultrakill.surge_accum_s = 0.0f;
        uint32_t surge_reserve = g_em_snap.gwnav_reserve ? g_em_snap.gwnav_reserve : UK_EM_RESERVE_SLOTS;
        if (g_em_snap.valid &&
            cur_walking < target_real &&
            (uint32_t)cur_walking < UK_MAX_WALKING_AI_CAP &&
            g_spawn_queue.count < 128u &&
            g_em_snap.free_slots >= surge_reserve + 24u) {

            /* Reserve top 72 walking-AI slots exclusively for foreign Chaos swarms, and draw rotated foreign
             * factions 70% of the time overall so off-planet factions drop consistently all mission long! */
            int native_headroom_full = (cur_walking >= target_real - UK_FOREIGN_HEADROOM_SLOTS);
            int prefer_foreign = native_headroom_full || ((NextRand() % 100u) < 70u);
            int chosen = -1;
            if (prefer_foreign) {
                chosen = ChaosChooseForeignRotated(&config, native, diff, g_last_foreign_faction, NextRand());
                if (chosen >= 0 && chosen < CHAOS_FACTIONS &&
                    (!g_rosters_loaded[chosen] || !g_faction_resident[chosen] || !PeersReadyForFaction(gbase, chosen))) {
                    int alt = 3 - native - chosen;
                    if (alt >= 0 && alt < CHAOS_FACTIONS && alt != native &&
                        ChaosWeight(&config, alt, diff) > 0 &&
                        g_rosters_loaded[alt] && g_faction_resident[alt] && PeersReadyForFaction(gbase, alt)) {
                        chosen = alt;
                    } else if (!native_headroom_full && native >= 0 && native < CHAOS_FACTIONS &&
                               ChaosWeight(&config, native, diff) > 0 && g_rosters_loaded[native]) {
                        chosen = native;
                    } else {
                        chosen = -1;
                    }
                }
            } else {
                chosen = native;
                if (chosen < 0 || chosen >= CHAOS_FACTIONS || !g_rosters_loaded[chosen]) {
                    chosen = ChaosChooseForeignRotated(&config, native, diff, g_last_foreign_faction, NextRand());
                }
            }

            if (chosen >= 0 && chosen < CHAOS_FACTIONS &&
                g_rosters_loaded[chosen] && (chosen == native || g_faction_resident[chosen]) &&
                (chosen == native || PeersReadyForFaction(gbase, chosen))) {
                /* 65% outer cohesive SWARM ring (95..185 m marching inward past SEAF), 35% active combat flank (65..165 m) */
                int outer_swarm = ((NextRand() % 100u) < 65u);
                float min_r = outer_swarm ? 95.0f  : 65.0f;
                float max_r = outer_swarm ? 185.0f : 165.0f;
                if (g_host_opts.spawn_radius_min_m > 0) min_r = (float)g_host_opts.spawn_radius_min_m;
                if (g_host_opts.spawn_radius_max_m > 0 && (float)g_host_opts.spawn_radius_max_m > min_r)
                    max_r = (float)g_host_opts.spawn_radius_max_m;

                float anchor[3], target_p[3];
                if (FindTheaterRingAnchor(gbase, living_pos, living_players, min_r, max_r, anchor, target_p)) {
                    int swarm_count = 18 + (int)(NextRand() % 15u); /* 18..32 cohesive swarm cluster! */
                    int rem_walk = target_real - cur_walking;
                    if (swarm_count > rem_walk) swarm_count = rem_walk;
                    if (swarm_count >= 6) {
                        uint32_t flags = (chosen == CHAOS_BUGS) ? 0x24u : 0x3Cu;
                        if (EnqueueTheaterPlatoon(chosen, diff, swarm_count, flags, anchor, target_p) > 0 &&
                            chosen != native) {
                            g_last_foreign_faction = chosen;
                        }
                    }
                }
            }
        }
    }

    ServiceVirtualBattalions(gbase, native, diff, living_pos, living_players, dt);

    g_ultrakill.telemetry_accum_s += dt;
    if (g_ultrakill.telemetry_accum_s >= 5.0f) {
        g_ultrakill.telemetry_accum_s = 0.0f;
        uint32_t real_units = g_em_snap.valid ? g_em_snap.live_slots : (uint32_t)g_tracker.total_live;
        uint32_t walk_units = g_em_snap.valid ? g_em_snap.walking_ai_slots : (uint32_t)g_tracker.total_live;
        uint32_t total_theater = real_units + g_ultrakill.virtual_units_total;
        float r_m = (g_ultrakill.live_radius_factor > 0.1f
                     ? g_ultrakill.live_radius_factor
                     : UltrakillResolveRadiusFactor(&g_host_opts)) * 1024.0f;
        float area_km2 = 3.14159265f * r_m * r_m * 1e-6f;
        hf->log(self,
                "CHAOS_THEATER_TELEMETRY: preset=%s real_em_live=%u walking_ai=%u em_free=%u chaos_tracked=%d virtual_regiments=%u virtual_units=%u total_theater_units=%u queued=%u recycled=%u air_f=%u air_e=%u atte_salvos=%u radius_m=%.1f area_km2=%.2f salvos=%u promoted=%u",
                cd_theater_preset_name(g_host_opts.theater_preset),
                real_units,
                walk_units,
                g_em_snap.valid ? g_em_snap.free_slots : 0u,
                g_tracker.total_live,
                g_ultrakill.virtual_regiments_active,
                g_ultrakill.virtual_units_total,
                total_theater,
                g_spawn_queue.count,
                g_ultrakill.stragglers_recycled_total,
                g_ultrakill.friendly_air_runs_total,
                g_ultrakill.enemy_air_runs_total,
                g_ultrakill.atte_salvos_total,
                (double)r_m,
                (double)area_km2,
                g_ultrakill.salvos_fired_total,
                g_ultrakill.promotions_total);
    }
}

/* ---- Config Bridge, Kill Switch, Events & Game-Thread Loop ----------------------------------------------------- */

static int ClampInt_(int v, int lo, int hi)
{
    return v < lo ? lo : (v > hi ? hi : v);
}

static void FormatBridgeValues(char* out, size_t cap)
{
    snprintf(out, cap,
             "session=%lu\nenabled=%d\ncooldown_seconds=%d\nlanding_grace_seconds=%d\ntotal_cap=%d\n"
             "faction_cap=%d\nsquad_size=%d\nmission_budget=%d\ndroid_weight=%d\n"
             "squid_weight=%d\nbug_weight=%d\nair_runs_enabled=%d\natte_walker_support=%d\n",
             token,
             config.enabled ? 1 : 0,
             ClampInt_(config.cooldown, 10, 900),
             ClampInt_(config.grace, 6, 300),
             ClampInt_(config.total_cap, 1, 640),
             ClampInt_(config.faction_cap, 1, 400),
             ClampInt_(config.squad_size, 1, 32),
             ClampInt_(config.mission_budget, 1, 25000),
             ClampInt_(config.bots_weight, 0, 200),
             ClampInt_(config.squids_weight, 0, 200),
             ClampInt_(config.bugs_weight, 0, 200),
             config.air_runs_enabled ? 1 : 0,
             config.atte_walker_support ? 1 : 0);
}

static void Publish(void)
{
    FormatBridgeValues(g_published_bridge_text, sizeof g_published_bridge_text);
    FILE* f = bridge[0] ? fopen(bridge, "wb") : NULL;
    if (f) {
        fputs(g_published_bridge_text, f);
        fclose(f);
        cd_get_file_mtime_100ns(bridge, &g_published_bridge_mtime);
        g_last_bridge_seen_mtime = g_published_bridge_mtime;
        snprintf(g_last_bridge_seen_text, sizeof g_last_bridge_seen_text, "%s", g_published_bridge_text);
    }
}

static void LoadOptions(const char* path, int session_required)
{
    if (!path || !path[0]) return;
    uint64_t bm = 0ULL;
    if (session_required) {
#ifndef CHAOS_UNIT_TEST
        if (cd_get_file_mtime_100ns(path, &bm) == 1 && bm != 0ULL &&
            (bm == g_published_bridge_mtime || bm == g_last_bridge_seen_mtime)) {
            return;
        }
#else
        cd_get_file_mtime_100ns(path, &bm);
#endif
    }
    FILE* f = fopen(path, "rb");
    char raw_buf[512];
    char line[160];
    ChaosConfig next = config;
    int count = 0;
    unsigned long session = 0;
    if (!f) return;
    if (session_required) {
        size_t got = fread(raw_buf, 1, sizeof(raw_buf) - 1u, f);
        raw_buf[got] = '\0';
        if ((g_published_bridge_text[0] && strcmp(raw_buf, g_published_bridge_text) == 0) ||
            (g_last_bridge_seen_text[0] && strcmp(raw_buf, g_last_bridge_seen_text) == 0 &&
             bm != 0ULL && bm == g_last_bridge_seen_mtime)) {
            fclose(f);
            return;
        }
        fseek(f, 0, SEEK_SET);
    }
    if (session_required &&
        (!fgets(line, sizeof line, f) || sscanf(line, "session=%lu", &session) != 1 || session != token)) {
        fclose(f);
        return;
    }
    while (fgets(line, sizeof line, f)) {
        if (!strchr(line, '\n') && !feof(f)) { fclose(f); return; }
        if (line[0] == '#' || line[0] == '\r' || line[0] == '\n') continue;
        if (!ChaosParse(&next, line)) { fclose(f); return; }
        ++count;
    }
    int valid = !ferror(f) && (!session_required || count == 12);
    fclose(f);
    if (!valid) return;
    if (!session_required) {
        g_ini_config = next;
    } else {
        if (bm == 0ULL) cd_get_file_mtime_100ns(path, &bm);
        if (bm != 0ULL) {
            g_bridge_user_edit_mtime = bm;
            g_last_bridge_seen_mtime = bm;
        }
        snprintf(g_last_bridge_seen_text, sizeof g_last_bridge_seen_text, "%s", raw_buf);
        g_session_config = next;
        g_has_session_config = 1;
        g_d12_config_active = 0;
        if (g_host_opts.theater_preset == CD_THEATER_LEVEL12_ULTRAKILL)
            g_host_opts.theater_preset = CD_THEATER_CLONE_WARS;
        if (g_host_opts.chaos_intensity == CD_CHAOS_ULTRAKILL || g_host_opts.chaos_intensity == CD_CHAOS_CLONE_WARS)
            g_host_opts.chaos_intensity = CD_CHAOS_STANDARD;
        UltrakillSyncDifficulty12();
    }
    if (memcmp(&config, &next, sizeof next) != 0) {
        config = next;
        if (!config.enabled) {
            ChaosSpawnQueueReset(&g_spawn_queue);
            if (g_pending.active) {
                ChaosTrackerReleaseUnusedReservations(&g_tracker, g_pending.faction);
                memset(&g_pending, 0, sizeof g_pending);
            }
        }
        ChaosReset(&clock_state, &config);
        g_shared_dirty = 1;
        hf->log(self, "CHAOS_OPTIONS: enabled=%d cooldown=%d grace=%d cap=%d faction_cap=%d squad=%d budget=%d",
                config.enabled, config.cooldown, config.grace, config.total_cap, config.faction_cap,
                config.squad_size, config.mission_budget);
    }
}

static void ChaosApplyEffectiveHostConfig(int live_diff, int is_poll, const char* log_tag)
{
    CdChaosIntensity eff_mode = cd_effective_chaos_for_difficulty(&g_host_opts, live_diff);
    ChaosConfig next;
    ChaosApplyHostIntensity(&next, &g_ini_config, eff_mode);
    if (g_host_opts.target_active_units > 0 && cd_host_options_active_for_difficulty(&g_host_opts, live_diff)) {
        int governed = UltrakillTargetRealAiCap(&g_host_opts, 0);
        next.total_cap = governed;
        next.faction_cap = (governed * 3) / 4;
    }
    UltrakillSyncDifficulty12();
    if (memcmp(&config, &next, sizeof next) != 0) {
        config = next;
        if (!config.enabled) {
            ChaosSpawnQueueReset(&g_spawn_queue);
            if (g_pending.active) {
                ChaosTrackerReleaseUnusedReservations(&g_tracker, g_pending.faction);
                memset(&g_pending, 0, sizeof g_pending);
            }
        }
        ChaosReset(&clock_state, &config);
        g_shared_dirty = 1;
        if (is_poll && bridge[0]) Publish();
        hf->log(self, "%s: live_diff=%d intensity=%s preset=%s enabled=%d cooldown=%d grace=%d cap=%d faction_cap=%d squad=%d budget=%d",
                log_tag, live_diff, cd_chaos_intensity_name(eff_mode), cd_theater_preset_name(g_host_opts.theater_preset),
                config.enabled, config.cooldown, config.grace,
                config.total_cap, config.faction_cap, config.squad_size, config.mission_budget);
    }
}

static int ChaosHasActiveSessionOverride(void)
{
    if (!g_has_session_config) return 0;
    if (!g_host_opts.loaded_from_file) return 1;
    return cd_session_override_is_newer(g_host_options_mtime, g_bridge_user_edit_mtime);
}

static void ChaosRestoreNonD12Config(int live_diff, const char* log_tag)
{
    int session_active = ChaosHasActiveSessionOverride();
    g_d12_config_active = 0;
    if (!session_active && g_host_opts.loaded_from_file) {
        ChaosApplyEffectiveHostConfig(live_diff, 0, log_tag);
        return;
    }
    ChaosConfig target = session_active ? g_session_config : g_ini_config;
    UltrakillSyncDifficulty12();
    if (memcmp(&config, &target, sizeof target) != 0) {
        config = target;
        if (!config.enabled) {
            ChaosSpawnQueueReset(&g_spawn_queue);
            if (g_pending.active) {
                ChaosTrackerReleaseUnusedReservations(&g_tracker, g_pending.faction);
                memset(&g_pending, 0, sizeof g_pending);
            }
        }
        ChaosReset(&clock_state, &config);
        g_shared_dirty = 1;
        if (hf && self && hf->log) {
            hf->log(self, "%s: live_diff=%d source=%s enabled=%d cooldown=%d grace=%d cap=%d faction_cap=%d squad=%d budget=%d",
                    log_tag, live_diff, session_active ? "session" : "ini",
                    config.enabled, config.cooldown, config.grace,
                    config.total_cap, config.faction_cap, config.squad_size, config.mission_budget);
        }
    }
}

static void ChaosSyncLiveDifficultyConfig(uintptr_t gbase)
{
    int live_diff = ChaosLiveMissionDifficulty(gbase);
    if (live_diff == g_last_mission_live_diff) return;
    g_last_mission_live_diff = live_diff;
    if (live_diff >= 12) {
        g_d12_config_active = (g_host_opts.chaos_intensity != CD_CHAOS_OFF) ? 1 : 0;
        if (g_d12_config_active || g_host_opts.loaded_from_file) {
            ChaosApplyEffectiveHostConfig(live_diff, 0, "CHAOS_DIFF_GATE");
        }
        return;
    }
    if (ChaosHasActiveSessionOverride()) {
        if (g_d12_config_active) {
            ChaosRestoreNonD12Config(live_diff, "CHAOS_DIFF_GATE");
        }
        return;
    }
    if (g_host_opts.loaded_from_file) {
        g_d12_config_active = 0;
        ChaosApplyEffectiveHostConfig(live_diff, 0, "CHAOS_DIFF_GATE");
    } else if (g_d12_config_active) {
        ChaosRestoreNonD12Config(live_diff, "CHAOS_DIFF_GATE");
    }
}

static void RefreshActivePackMode(int is_poll)
{
    if (g_hellforge_mods_path[0]) {
        uint64_t mm = 0ULL;
        int mst = cd_get_file_mtime_100ns(g_hellforge_mods_path, &mm);
        if (mst == 0) {
            g_hellforge_mods_mtime = 0ULL;
            g_detected_mods_mode = CD_MODE_AUTO;
        } else if (mst == 1 && (!is_poll || mm != g_hellforge_mods_mtime)) {
            uint64_t read_mtime = 0ULL;
            CdActiveMode det = CD_MODE_AUTO;
            int drc = cd_detect_hellforge_mods_mode_file(g_hellforge_mods_path, &read_mtime, &det);
            if (drc >= 0) {
                g_hellforge_mods_mtime = read_mtime ? read_mtime : mm;
                g_detected_mods_mode = det;
            }
        }
    } else {
        g_hellforge_mods_mtime = 0ULL;
        g_detected_mods_mode = CD_MODE_AUTO;
    }

    CdActiveMode eff = (g_detected_mods_mode != CD_MODE_AUTO) ? g_detected_mods_mode : g_host_opts.active_mode;
    InterlockedExchange(&g_empire_mode_active, (eff == CD_MODE_EMPIRE) ? 1L : 0L);
}

static void LoadHostOptions(int is_poll)
{
    if (!g_host_options_path[0]) {
        RefreshActivePackMode(is_poll);
        return;
    }
    uint64_t hm = 0ULL;
    int st = cd_get_file_mtime_100ns(g_host_options_path, &hm);
    if (st != 1) {
        RefreshActivePackMode(is_poll);
        return;
    }
    if (is_poll && hm == g_host_options_mtime) {
        RefreshActivePackMode(is_poll);
        return;
    }
    g_host_options_mtime = hm;
    if (is_poll && cd_session_override_is_newer(hm, g_bridge_user_edit_mtime)) {
        RefreshActivePackMode(is_poll);
        return;
    }

    CdHostOptions ho;
    if (cd_load_host_options_file(g_host_options_path, &ho) != 1) {
        RefreshActivePackMode(is_poll);
        return;
    }
    g_host_opts = ho;
    RefreshActivePackMode(is_poll);
    g_has_session_config = 0;
    int live_diff = 0;
    if (hf && self && hf->game_state && hf->game_state(self) == HF_STATE_MISSION) {
        live_diff = ChaosLiveMissionDifficulty(GameBase(NULL));
    }
    g_last_mission_live_diff = live_diff;
    g_d12_config_active = (live_diff >= 12 && ho.chaos_intensity != CD_CHAOS_OFF) ? 1 : 0;
    ChaosApplyEffectiveHostConfig(live_diff, is_poll, "CHAOS_HOST_OPTIONS");
}

static void ResetMissionState(void);

static int Killed(void)
{
    if (!stopped && stop_file[0]) {
        DWORD a = GetFileAttributesA(stop_file);
        if (a != INVALID_FILE_ATTRIBUTES || GetLastError() != ERROR_FILE_NOT_FOUND) {
            stopped = 1;
            ResetMissionState();
            hf->log(self, "CHAOS_STOP: latched until session restart");
            SetStatusOnce("Chaos stopped until next session");
        }
    }
    return stopped;
}

static int ReadyPhase(void)
{
    return InterlockedCompareExchange(&g_phase, PHASE_ACTIVE, PHASE_ACTIVE) == PHASE_ACTIVE && !Killed();
}

static void ResetMissionState(void)
{
    uintptr_t cur_gbase = GameBase(NULL);
    UltrakillSyncDirectorPatrolValve(0);
    DespawnAtteWalkerIfActive(cur_gbase);
    ReleaseChaosOwnedPackages(cur_gbase);
    memset(&g_air_runs, 0, sizeof g_air_runs);
    memset(&g_atte, 0, sizeof g_atte);
    landed = 0;
    g_native_faction = -1;
    g_last_foreign_faction = -1;
    g_preload_watchdog_s = 0.0f;
    g_straggler_recycle_s = 0.0f;
    g_bot_smoke_requested = 0;
    g_bot_smoke_resident = 0;
    memset(&g_pending, 0, sizeof g_pending);
    memset(g_last_faction_masks, 0, sizeof g_last_faction_masks);
    memset(g_faction_resident, 0, sizeof g_faction_resident);
    memset(g_preload_ready_logged, 0, sizeof g_preload_ready_logged);
    memset(g_preload_refmap_warned, 0, sizeof g_preload_refmap_warned);
    memset(g_preload_requested, 0, sizeof g_preload_requested);
    memset(g_preload_built, 0, sizeof g_preload_built);
    memset(g_preload_count, 0, sizeof g_preload_count);
    memset(g_rosters_loaded, 0, sizeof g_rosters_loaded);
    memset(g_peer_sync, 0, sizeof g_peer_sync);
    memset(g_preload_faction_wait, 0, sizeof g_preload_faction_wait);
    g_preload_mission_time = 0.0f;
    g_preload_tokens = 0.0f;
    g_preload_deferred_logged = 0;
    g_last_player_mask = 0;
    g_last_sent_resident_mask = 0xFFu;
    g_last_sent_in_mission = 0xFFu;
    g_last_client_status_sent = -10.0;
    g_share_poll = 0.0f;
    g_shared_dirty = 0;
    g_shared_applied = 0;
    g_last_foreign_spawn_age = -1000.0;
    memset(g_last_wave_anchor, 0, sizeof g_last_wave_anchor);
    g_has_last_wave_anchor = 0;
    g_wave_seq = 0u;
    g_prune_accum_s = 0.0f;
    g_em_snap.valid = 0;
    ChaosSpawnQueueReset(&g_spawn_queue);
    memset(g_ultrakill.battalions, 0, sizeof g_ultrakill.battalions);
    g_ultrakill.surge_accum_s = 0.0f;
    g_ultrakill.telemetry_accum_s = 0.0f;
    g_ultrakill.budget_refresh_s = 0.0f;
    g_ultrakill.salvo_budget_tokens = 0.0f;
    g_ultrakill.virtual_units_total = 0u;
    g_ultrakill.virtual_regiments_active = 0u;
    g_ultrakill.live_radius_factor = 0.0f;
    g_last_mission_live_diff = 0;
    if (g_d12_config_active) {
        ChaosRestoreNonD12Config(0, "CHAOS_MISSION_RESET");
    }
    memset(s_perc_cache_ht, 0, sizeof s_perc_cache_ht);
    s_perc_pass_seq = 0u;
    ++g_mission_epoch;
    ChaosReset(&clock_state, &config);
    ChaosTrackerReset(&g_tracker);
}

static int ChaosIsInMissionFlow(unsigned s)
{
    return s == HF_STATE_MISSION || s == HF_STATE_PREPARE_MISSION;
}

static void HandleHostMigration(unsigned old_host, unsigned new_host)
{
    int released = 0;
    if (g_pending.active) {
        released = g_tracker.reserved[g_pending.faction];
        ChaosTrackerReleaseUnusedReservations(&g_tracker, g_pending.faction);
        memset(&g_pending, 0, sizeof g_pending);
    }

    ChaosTrackerPrune(&g_tracker, EntityAlive, NULL);
    memset(g_last_faction_masks, 0, sizeof g_last_faction_masks);
    g_last_player_mask = 0;
    perception_poll = 0.0f;
    g_share_poll = 0.0f;
    ++g_mission_epoch;

    if (!old_host && new_host) {
        g_shared_dirty = 1;
        PublishSharedState(1);
    } else if (old_host && !new_host) {
        g_last_sent_resident_mask = 0xFFu;
        g_last_sent_in_mission = 0xFFu;
        g_last_client_status_sent = -10.0;
        ChaosSpawnQueueReset(&g_spawn_queue);
        memset(g_ultrakill.battalions, 0, sizeof g_ultrakill.battalions);
        g_ultrakill.virtual_units_total = 0u;
        g_ultrakill.virtual_regiments_active = 0u;
        g_atte.active = 0;
        g_atte.entity = 0u;
        g_atte.fire_cd_s = 0.0f;
        g_atte.redeploy_cd_s = 0.0f;
        g_atte.spawn_retry_s = 0.0f;
    }
    UltrakillSyncDifficulty12();

    double rem = (landed && clock_state.next > clock_state.age) ? (clock_state.next - clock_state.age) : 0.0;
    hf->log(self,
            "CHAOS_HOST_MIGRATION: old_host=%u new_host=%u landed=%u live=%d mission_spawned=%d released=%d cooldown_rem=%.1fs",
            old_host, new_host, landed, g_tracker.total_live, g_tracker.mission_spawned, released, rem);
}

static void OnMessage(HfMod* mod, const HfEventInfo* info, void* user)
{
    (void)mod; (void)user;
    if (!ReadyPhase() || !info || info->size < sizeof(HfMessageEvent)) return;
    const HfMessageEvent* m = (const HfMessageEvent*)info;
    if (m->bytes < sizeof(ChaosPeerMsg) || !m->from || !Host()) return;

    ChaosPeerMsg pm;
    memcpy(&pm, m->data, sizeof pm);
    if (!ChaosPeerMsgValid(&pm)) return;

    int slot = -1, free_slot = -1;
    for (int i = 0; i < CHAOS_MAX_PEERS; i++) {
        if (g_peer_sync[i].active && g_peer_sync[i].peer == m->from) { slot = i; break; }
        if (!g_peer_sync[i].active && free_slot < 0) free_slot = i;
    }
    if (slot < 0) {
        if (free_slot < 0) return;
        slot = free_slot;
        g_peer_sync[slot].peer = m->from;
        g_peer_sync[slot].active = 1u;
        g_peer_sync[slot].joined_age = clock_state.age;
        g_peer_sync[slot].mission_entered_age = clock_state.age;
    }
    int first_hello = !g_peer_sync[slot].hello_seen;
    uint8_t prev_mask = g_peer_sync[slot].resident_mask;
    uint8_t prev_in_mission = g_peer_sync[slot].in_mission;
    g_peer_sync[slot].hello_seen = 1u;
    g_peer_sync[slot].resident_mask = pm.resident_mask;
    g_peer_sync[slot].in_mission = pm.in_mission;
    if (pm.in_mission && (first_hello || !prev_in_mission)) {
        /* H4: Joiner entered HF_STATE_MISSION (directly or after slow PrepareMission): grant full post-entry preload grace. */
        g_peer_sync[slot].mission_entered_age = clock_state.age;
    }
    g_peer_sync[slot].last_seen_age = clock_state.age;
    if (first_hello || prev_mask != pm.resident_mask || prev_in_mission != pm.in_mission) {
        hf->log(self, "CHAOS_PEER_READY: peer=%llu resident_mask=0x%x in_mission=%u",
                (unsigned long long)m->from, pm.resident_mask, pm.in_mission);
        PublishSharedState(1);
    }
}

static void OnShare(HfMod* mod, const HfEventInfo* info, void* user)
{
    (void)mod; (void)user;
    if (!ReadyPhase() || !info || info->size < sizeof(HfShareEvent)) return;
    const HfShareEvent* s = (const HfShareEvent*)info;
    if (s->key != CHAOS_SHARE_KEY_STATE || s->bytes < sizeof(ChaosSharedState) || Host()) return;

    ChaosSharedState st;
    memcpy(&st, s->value, sizeof st);
    ApplySharedState(&st);
}

static void OnSpawn(HfMod* mod, const HfEventInfo* info, void* user)
{
    (void)mod; (void)user;
    if (!ReadyPhase() || !info || info->size < sizeof(HfSpawnEvent)) return;
    if (hf->game_state(self) != HF_STATE_MISSION) return;
    const HfSpawnEvent* e = (const HfSpawnEvent*)info;
    int fam = ChaosEnemyFamilyEx(e->resource);

    if (!Host()) {
        /* Read-only client / late-joiner passive tracking for host-migration budget/population continuity. */
        if (fam < 0 || ChaosIsTransportResource(e->resource)) return;
        if (g_native_faction < 0) {
            uintptr_t gbase = GameBase(NULL);
            int native = -1, diff = 0;
            if (gbase && g_pins_ok && ReadDirectorContext(gbase, NULL, &native, &diff)) {
                g_native_faction = native;
            }
        }
        if (g_native_faction >= 0 && fam != g_native_faction) {
            if (ChaosTrackerRecordSpawnAt(&g_tracker, fam, e->entity, 0, clock_state.age)) {
                if (!g_shared_applied && (clock_state.age - g_last_foreign_spawn_age) >= 15.0) {
                    g_last_foreign_spawn_age = clock_state.age;
                    ++clock_state.attempts;
                    if (clock_state.next < clock_state.age + (double)config.cooldown) {
                        clock_state.next = clock_state.age + (double)config.cooldown;
                    }
                }
                hf->log(self, "CHAOS_PEER_OBSERVE: faction=%s entity=%u resource=%016llx auth=%u live=%d mission_spawned=%d",
                        chaos_faction_names[fam], e->entity, (unsigned long long)e->resource,
                        e->authority, g_tracker.live[fam], g_tracker.mission_spawned);
            }
        }
        return;
    }

    if (ChaosIsKnownCorpseWrapper(e->resource)) return;

    if (!EntityAuthorityAllowed(e->entity, e->authority)) {
        if (g_pending.active && fam != g_native_faction && e->resource != CHAOS_ENTITY_AVATAR &&
            g_pending.unmapped_logged < 2) {
            ++g_pending.unmapped_logged;
            hf->log(self, "CHAOS_SPAWN_UNMAPPED: faction=%s entity=%u resource=%016llx auth=0 pending_faction=%s elapsed=%.1fs",
                    (fam >= 0 && fam < CHAOS_FACTIONS) ? chaos_faction_names[fam] : "unknown",
                    e->entity, (unsigned long long)e->resource,
                    chaos_faction_names[g_pending.faction], g_pending.elapsed);
        }
        return;
    }

    if (fam < 0) {
        if (g_pending.active && e->resource != CHAOS_ENTITY_AVATAR && g_pending.unmapped_logged < 2) {
            ++g_pending.unmapped_logged;
            hf->log(self, "CHAOS_SPAWN_UNMAPPED: faction=unknown entity=%u resource=%016llx auth=1 pending_faction=%s elapsed=%.1fs",
                    e->entity, (unsigned long long)e->resource,
                    chaos_faction_names[g_pending.faction], g_pending.elapsed);
        }
        return;
    }

    if (ChaosIsTransportResource(e->resource)) {
        int in_wave = (g_pending.active && g_pending.faction == fam);
        if (in_wave) {
            ++g_pending.ship_seen;
            g_pending.ship_entity = e->entity;
            if (!g_pending.cargo_patched && g_pins_ok) {
                uintptr_t gbase = GameBase(NULL);
                uint32_t cf = ChaosCargoFactionFromFaction(fam);
                if (gbase && cf) {
                    FixTransportCargoSlot(gbase, fam, cf, UINT32_MAX, e->entity, NULL, 0);
                }
            }
        }
        if (in_wave || (g_native_faction >= 0 && fam != g_native_faction)) {
            hf->log(self, "CHAOS_TRANSPORT_SPAWN: faction=%s entity=%u resource=%016llx ship_seen=%d elapsed=%.1fs cargo_patched=%d",
                    chaos_faction_names[fam], e->entity, (unsigned long long)e->resource,
                    in_wave ? g_pending.ship_seen : 0, in_wave ? g_pending.elapsed : 0.0,
                    in_wave ? g_pending.cargo_patched : 0);
        }
        return;
    }

    if (g_pending.active && g_pending.faction == fam) {
        if (ChaosTrackerRecordSpawnAt(&g_tracker, fam, e->entity, 1, clock_state.age)) {
            ++g_pending.unloaded_count;
            g_shared_dirty = 1;
            hf->log(self,
                    "CHAOS_SPAWN: faction=%s entity=%u resource=%016llx mode=%s wave_unloaded=%d/%d elapsed=%.1fs live=%d mission_spawned=%d",
                    chaos_faction_names[fam], e->entity, (unsigned long long)e->resource,
                    PendingWaveMode(&g_pending),
                    g_pending.unloaded_count, g_pending.squad_size, g_pending.elapsed,
                    g_tracker.live[fam], g_tracker.mission_spawned);
        }
    } else if (g_native_faction >= 0 && fam != g_native_faction) {
        if (ChaosTrackerRecordSpawnAt(&g_tracker, fam, e->entity, 0, clock_state.age)) {
            g_shared_dirty = 1;
            hf->log(self,
                    "CHAOS_SPAWN: faction=%s entity=%u resource=%016llx mode=late wave_unloaded=0/0 elapsed=%.1fs live=%d mission_spawned=%d",
                    chaos_faction_names[fam], e->entity, (unsigned long long)e->resource,
                    g_pending.active ? g_pending.elapsed : 0.0,
                    g_tracker.live[fam], g_tracker.mission_spawned);
        }
    }
}

static void OnMissionEnd(HfMod* mod, const HfEventInfo* info, void* user)
{
    (void)mod; (void)info; (void)user;
    if (InterlockedCompareExchange(&g_phase, PHASE_ACTIVE, PHASE_ACTIVE) != PHASE_ACTIVE) return;
    ResetMissionState();
    if (hf && self && hf->game_state) {
        state = hf->game_state(self);
    }
}

static void OnSessionEnd(HfMod* mod, HfEvent ev, void* user)
{
    /* SDK v20 contract (hellforge_mod.h:60-65): HF_EVENT_SESSION_END runs on the loader watch thread
     * or under Windows process-exit loader lock — never call game.dll functions here.
     * In-session mission teardown is handled on the game thread via ResetMissionState(). */
    (void)mod; (void)ev; (void)user;
    InterlockedExchange(&g_phase, PHASE_ENDED);
}

static void Tick(HfMod* mod, float dt, void* user)
{
    (void)mod; (void)user;
    if (!ReadyPhase() || !isfinite(dt) || dt < 0.0f || dt > 5.0f) return;

    unsigned now = hf->game_state(self);
    unsigned host = (unsigned)Host();
    unsigned role_changed = (host != previous_role);
    int in_flow_prev = ChaosIsInMissionFlow(state);
    int in_flow_now = ChaosIsInMissionFlow(now);

    if (!in_flow_prev || !in_flow_now) {
        if (now != state) {
            state = now;
            previous_role = host;
            ResetMissionState();
        } else {
            previous_role = host;
        }
        if (role_changed) {
            UltrakillSyncDifficulty12();
        }
    } else if (now != state) {
        unsigned prev_state = state;
        state = now;
        previous_role = host;
        if (prev_state == HF_STATE_MISSION && now == HF_STATE_PREPARE_MISSION) {
            ResetMissionState();
        } else if (prev_state == HF_STATE_PREPARE_MISSION && now == HF_STATE_MISSION && !g_shared_applied) {
            landed = 0;
            memset(&g_pending, 0, sizeof g_pending);
            ChaosReset(&clock_state, &config);
            ChaosTrackerReset(&g_tracker);
        }
        if (role_changed) {
            HandleHostMigration(host ? 0u : 1u, host);
        } else {
            UltrakillSyncDifficulty12();
        }
    } else if (role_changed) {
        HandleHostMigration(previous_role, host);
        previous_role = host;
    }

    options_poll -= dt;
    if (options_poll <= 0.0f) {
        options_poll = 1.0f;
        LoadOptions(bridge, 1);
        LoadHostOptions(1);
    }

    if (!in_flow_now) {
        g_d12_sync_poll -= dt;
        if (g_d12_sync_poll <= 0.0f) {
            g_d12_sync_poll = 0.25f;
            ChaosEnsureD12SlotAndItem();
        }
        return;
    }

    /* Pre-stream foreign faction packages during HF_STATE_PREPARE_MISSION (Hellpod drop, gently paced at 4 pkgs/s)
     * so off-planet Chaos waves are resident and ready right as the 6-8s landing grace expires! */
    if (now == HF_STATE_PREPARE_MISSION) {
        if (ProveChaosRuntime()) {
            size_t pgsize = 0, pesize = 0;
            uintptr_t pgbase = GameBase(&pgsize), pebase = ExeBase(&pesize);
            if (pgbase) {
                PreloadForeignFactionPackagesPaced(pgbase, pebase, pesize, dt, now, 1);
            }
        }
        if (!host) {
            SendClientStatusIfNeeded(
                now,
                (g_native_faction >= 0 && g_native_faction < CHAOS_FACTIONS) ? ChaosRequiredForeignMask(g_native_faction) : 0u);
        }
        return;
    }
    if (now != HF_STATE_MISSION) return;

    if (!ProveChaosRuntime()) return;

    size_t gsize = 0, esize = 0;
    uintptr_t gbase = GameBase(&gsize), ebase = ExeBase(&esize);
    if (!gbase) return;

    /* All-peer curated foreign package preloading (smoothly paced, with 5.0s residency watchdog) */
    PreloadForeignFactionPackagesPaced(gbase, ebase, esize, dt, now, 1);

    g_prune_accum_s += dt;
    if (g_tracker.total_live <= 32 || g_prune_accum_s >= 0.25f) {
        g_prune_accum_s = 0.0f;
        ChaosPrepareAliveBatch(ebase, esize, gbase, gsize);
        ChaosTrackerPrune(&g_tracker, EntityAlive, &g_alive_ctx);
    }

    float living_pos[4][3];
    int living_players = CollectLivingPlayers(gbase, living_pos, NULL);

    if (!host) {
        /* H4: Transmit in_mission: 0 -> 1 immediately on Tick 1 in HF_STATE_MISSION (even if Director context is still settling). */
        SendClientStatusIfNeeded(
            now,
            (g_native_faction >= 0 && g_native_faction < CHAOS_FACTIONS) ? ChaosRequiredForeignMask(g_native_faction) : 0u);
        /* Client / late-joiner: consume host state snapshot & keep clock aligned cleanly, NEVER spawn/mutate */
        if (landed) clock_state.age += (double)dt;
        g_share_poll -= dt;
        if (HasChannelShare() && (!g_shared_applied || g_share_poll <= 0.0f)) {
            g_share_poll = 1.0f;
            ChaosSharedState st;
            if (hf->share_get(self, CHAOS_SHARE_KEY_STATE, &st, sizeof st) >= sizeof st) {
                ApplySharedState(&st);
            }
        }
        if (!landed && living_players > 0) {
            landed = 1;
            if (!g_shared_applied) clock_state.next = clock_state.age + (double)config.grace;
        }
        return;
    }

    /* Authoritative Host / Solo path below */
    if (g_prune_accum_s == 0.0f) {
        PruneStaleTrackedUnits(gbase, gsize, living_pos, living_players, clock_state.age);
    }
    RecycleFarEnemyStragglers(gbase, living_pos, living_players, clock_state.age, dt);
    ReconcilePeers(gbase);

    /* 250ms Host/Solo mutual-hostility perception narrowing */
    perception_poll -= dt;
    if (perception_poll <= 0.0f) {
        perception_poll = 0.25f;
        if (config.enabled || g_tracker.total_live > 0) {
            ChaosRefreshEmSnapshot(gbase, clock_state.age);
            UpdatePerceptionHostility(gbase);
        }
    }

    uintptr_t director = 0;
    int native = -1, diff = 0;
    if (!ReadDirectorContext(gbase, &director, &native, &diff)) return;

    ServicePendingWave(gbase, director, dt);
    ChaosSyncLiveDifficultyConfig(gbase);

    if (!landed && living_players > 0) {
        landed = 1;
        ChaosReset(&clock_state, &config);
        g_shared_dirty = 1;
        hf->log(self, "CHAOS_LANDING: host=1 native_faction=%d grace=%ds", native, config.grace);
    }

    if (ChaosIsUltrakillActiveInMission(gbase)) {
        ServiceUltrakillTheater(gbase, director, native, diff, living_pos, living_players, dt);
    } else if (config.enabled && g_spawn_queue.count > 0u) {
        DrainTheaterSpawnQueue(gbase, director);
    }

    {
        int live_diff_now = ChaosLiveMissionDifficulty(gbase);
        if (live_diff_now <= 0) live_diff_now = diff;
        ServiceTacticalAirRuns(gbase, ebase, esize, live_diff_now, living_pos, living_players, dt);
        ServiceAutonomousAtteWalker(gbase, ebase, esize, live_diff_now, living_pos, living_players, dt);
    }

    g_share_poll -= dt;
    if (g_shared_dirty || g_share_poll <= 0.0f) {
        g_share_poll = 2.0f;
        PublishSharedState(g_share_poll == 2.0f);
    }

    if (!ChaosDue(&clock_state, &config, dt, landed != 0)) return;
    g_shared_dirty = 1;

    int chosen = ChaosChooseForeignRotated(&config, native, diff, g_last_foreign_faction, NextRand());

    ChaosEvidence ev = {0};
    ev.host = (int)host;
    ev.mission = 1;
    ev.players = living_players > 0;
    ev.native_faction = native;
    ev.difficulty = diff;
    ev.closure = (chosen >= 0 && g_rosters_loaded[chosen]);
    ev.resident = (chosen >= 0 && g_faction_resident[chosen]);
    ev.peers = PeersReadyForFaction(gbase, chosen);
    ev.placement = 1;
    {
        uint8_t bypass_hr = 0u;
        int is_uk = ChaosIsUltrakillActiveInMission(gbase);
        ChaosRefreshEmSnapshot(gbase, clock_state.age);
        uint32_t uk_reserve = g_em_snap.gwnav_reserve ? g_em_snap.gwnav_reserve : UK_EM_RESERVE_SLOTS;
        uint32_t min_free = is_uk ? uk_reserve : 32u;
        if (is_uk && g_em_snap.valid && g_em_snap.free_slots >= uk_reserve &&
            g_em_snap.walking_ai_slots < UK_MAX_WALKING_AI_CAP) {
            bypass_hr = 1u;
        }
        ev.constructor = (chosen >= 0) &&
                         g_em_snap.valid &&
                         (g_em_snap.free_slots >= min_free) &&
                         (g_em_snap.walking_ai_slots < UK_MAX_WALKING_AI_CAP) &&
                         CallHasHeadroom(gbase, (void*)director, 3u, bypass_hr);
    }
    ev.arrival = g_pins_ok;
    ev.population_known = 1;
    for (int f = 0; f < CHAOS_FACTIONS; f++) ev.live[f] = ChaosEffectiveLiveFaction(&g_tracker, f);
    ev.total_live = ChaosEffectiveLiveTotal(&g_tracker);
    ev.mission_spawned = g_tracker.mission_spawned;

    const char* refuse_why = ChaosRefuse(&config, &ev, chosen);
    if (refuse_why && chosen >= 0 && chosen < CHAOS_FACTIONS && native >= 0 && native < CHAOS_FACTIONS &&
        (strcmp(refuse_why, "population_cap") == 0 ||
         strcmp(refuse_why, "faction_packages_not_resident") == 0 ||
         strcmp(refuse_why, "peer_and_late_join_barrier_unqualified") == 0)) {
        int alt = 3 - native - chosen;
        if (alt >= 0 && alt < CHAOS_FACTIONS && alt != native && alt != chosen &&
            ChaosWeight(&config, alt, diff) > 0) {
            ChaosEvidence alt_ev = ev;
            alt_ev.closure = g_rosters_loaded[alt];
            alt_ev.resident = g_faction_resident[alt];
            alt_ev.peers = PeersReadyForFaction(gbase, alt);
            if (ChaosRefuse(&config, &alt_ev, alt) == NULL) {
                chosen = alt;
                ev = alt_ev;
                refuse_why = NULL;
            }
        }
    }
    if (refuse_why) {
        int fac_live = (chosen >= 0 && chosen < CHAOS_FACTIONS) ? ev.live[chosen] : 0;
        hf->log(self, "CHAOS_SKIP: attempt=%u event=%s reason=%s faction_live=%d/%d total=%d/%d squad=%d spawned=%d/%d",
                clock_state.attempts, (chosen >= 0 && chosen < 3) ? chaos_names[chosen] : "none",
                refuse_why, fac_live, config.faction_cap, ev.total_live, config.total_cap,
                config.squad_size, ev.mission_spawned, config.mission_budget);
        return;
    }

    int eff_sq = ChaosEffectiveSquadSize(&config, &ev, chosen);
    if (eff_sq <= 0) return;

    float anchor[3], target[3];
    if (!FindSafeGroundAnchorEx(gbase, eff_sq, anchor, target)) {
        hf->log(self, "CHAOS_SKIP: attempt=%u event=%s reason=all_player_clearance_and_navmesh_unqualified",
                clock_state.attempts, chaos_names[chosen]);
        return;
    }

    DispatchChaosWave(gbase, director, chosen, diff, eff_sq, anchor, target);
}

HF_MOD_EXPORT int hellforge_mod_init(const HfApi* api, HfMod* mod)
{
    if (!api || api->version < 16 ||
        api->size < offsetof(HfApi, set_status) + sizeof api->set_status ||
        !api->build_id || !api->build_id() || strcmp(api->build_id(), "25480438") != 0 ||
        !api->log || !api->set_status || !api->session || !api->game_state ||
        !api->subscribe || !api->on_game_thread || !api->on || !api->mod_folder ||
        !api->read || !api->read_code || !api->write || !api->module_base)
        return 1;

    hf = api;
    self = mod;
    InterlockedExchange(&g_phase, PHASE_ACTIVE);
    config = ChaosDefaults();
    g_ini_config = config;
    memset(&g_session_config, 0, sizeof g_session_config);
    g_has_session_config = 0;
    g_d12_config_active = 0;
    g_host_options_path[0] = 0;
    g_host_options_mtime = 0ULL;
    g_hellforge_mods_path[0] = 0;
    g_hellforge_mods_mtime = 0ULL;
    g_detected_mods_mode = CD_MODE_AUTO;
    InterlockedExchange(&g_empire_mode_active, 0L);
    g_published_bridge_mtime = 0ULL;
    g_published_bridge_text[0] = 0;
    g_last_bridge_seen_mtime = 0ULL;
    g_last_bridge_seen_text[0] = 0;
    g_bridge_user_edit_mtime = 0ULL;
    g_host_opts = cd_host_options_defaults();
    memset(&g_d12_slot, 0, sizeof g_d12_slot);
    g_d12_sync_poll = 0.0f;
    g_last_mission_live_diff = 0;
    status_text[0] = 0;

    const char* folder = api->mod_folder(mod);
    if (!folder || strlen(folder) > 1800) return 1;
    snprintf(options, sizeof options, "%s\\options.ini", folder);
    snprintf(stop_file, sizeof stop_file, "%s\\chaos.stop", folder);
    LoadOptions(options, 0);
    cd_resolve_host_options_path(folder, g_host_options_path, sizeof g_host_options_path);
    cd_resolve_hellforge_mods_path(folder, g_hellforge_mods_path, sizeof g_hellforge_mods_path);
    LoadHostOptions(0);
    RefreshActivePackMode(0);

    char base[1600];
    DWORD n = GetEnvironmentVariableA("LOCALAPPDATA", base, sizeof base);
    if (n && n < sizeof base) {
        char directory[1900];
        snprintf(directory, sizeof directory, "%s\\Clonedivers", base);
        CreateDirectoryA(directory, NULL);
        snprintf(bridge, sizeof bridge, "%s\\ChaosInvasions.values", directory);
    }

    token = GetCurrentProcessId();
    g_prng_state ^= (uint32_t)token ^ 0xA5A55A5Au;
    Publish();
    ResetMissionState();
    state = api->game_state(mod);
    previous_role = (unsigned)Host();

    if (Killed()) return 0;

    if (api->on(mod, HF_EVENT_SESSION_END, OnSessionEnd, NULL) != 0) {
        InterlockedExchange(&g_phase, PHASE_ENDED);
        return 1;
    }
    (void)api->on(mod, HF_EVENT_GAME_LOADED, OnGameLoaded, NULL);
    (void)api->on(mod, HF_EVENT_ENTITIES_READY, OnEntitiesReady, NULL);
    ChaosEnsureD12SlotAndItemEx(1);
    if (!api->subscribe(mod, HF_ON_SPAWN, OnSpawn, NULL, 0) ||
        !api->subscribe(mod, HF_ON_MISSION_END, OnMissionEnd, NULL, 0) ||
        !api->on_game_thread(mod, Tick, NULL)) {
        InterlockedExchange(&g_phase, PHASE_ENDED);
        return 1;
    }
    if (HasChannelSend()) api->subscribe(mod, HF_ON_MESSAGE, OnMessage, NULL, 0);
    if (HasChannelShare()) api->subscribe(mod, HF_ON_SHARE, OnShare, NULL, 0);

    SetStatusOnce("Chaos Invasions 0.5.2 ready");
    api->log(mod, "CHAOS_INIT: version=0.5.2 build=25480438 sdk=16 preset=%s d12_name=\"%s\" enabled=%d cooldown=%d grace=%d cap=%d faction_cap=%d squad=%d budget=%d",
             cd_theater_preset_name(g_host_opts.theater_preset),
             ChaosD12DisplayName(),
             config.enabled, config.cooldown, config.grace, config.total_cap, config.faction_cap,
             config.squad_size, config.mission_budget);
    return 0;
}
