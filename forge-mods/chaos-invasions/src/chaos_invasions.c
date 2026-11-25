#include "hellforge_mod.h"
#include <windows.h>
#include <psapi.h>
#include "policy.h"

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

#define CHAOS_PRELOAD_SETTLE_S       1.0f
#define CHAOS_PRELOAD_RATE_PER_S     6.0f
#define CHAOS_PRELOAD_FACTION_WAIT_S 8.0f

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
static char g_host_options_path[CD_HOST_OPTIONS_MAX_PATH];
static uint64_t g_host_options_mtime = 0ULL;
static uint64_t g_published_bridge_mtime = 0ULL;
static uint64_t g_bridge_user_edit_mtime = 0ULL;
static ChaosClock clock_state;
static ChaosTracker g_tracker;
static ChaosPendingWave g_pending;

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
static float g_last_wave_anchor[3] = {0.0f, 0.0f, 0.0f};
static int g_has_last_wave_anchor = 0;
static uint64_t g_chaos_owned_pkgs[128];
static uint32_t g_chaos_owned_pkg_count = 0u;

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
    if (g_chaos_owned_pkg_count < (uint32_t)(sizeof(g_chaos_owned_pkgs) / sizeof(g_chaos_owned_pkgs[0]))) {
        g_chaos_owned_pkgs[g_chaos_owned_pkg_count++] = pkg_id;
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
    uint8_t (*pkgs_idle)(void* unused);
    int (*alive_override)(uint32_t entity);
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

static int EntityAlive(uint32_t entity, void* ctx)
{
    (void)ctx;
#ifdef CHAOS_UNIT_TEST
    if (g_mock.alive_override) return g_mock.alive_override(entity);
#endif
    if (!entity || entity == 0xFFFFFFFFu) return 0;
    size_t esize = 0, gsize = 0;
    uintptr_t ebase = ExeBase(&esize);
    uintptr_t gbase = GameBase(&gsize);
    if (!ebase || esize < CHAOS_EXE_ENTITIES_RVA + 8u) return 0;

    uintptr_t ent_mgr = 0, gens = 0;
    uint32_t size = 0;
    uint32_t index = entity & 0x3FFFFFu;
    uint8_t gen = 0;
    if (!ReadPtr(ebase + CHAOS_EXE_ENTITIES_RVA, &ent_mgr) ||
        !ReadMem(ent_mgr + 0x80u, &size, sizeof size) || index >= size ||
        !ReadPtr(ent_mgr + 0x88u, &gens) ||
        !ReadMem(gens + index, &gen, sizeof gen) ||
        gen != ((entity >> 22) & 0xFFu))
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
    for (uint32_t i = 0; i < count; i++) {
        uint32_t life = ReadU32(pm + 0x2E0u + 0x38u * i, 0u);
        uint32_t net = ReadU32(pm + 0x3A8u + 0x20u * i, 0x7FFFu);
        uint32_t slot = 0, avatar = 0;
        if (life != 3u || net == 0x7FFFu || !em) continue;
        if (!HashFindU32(em + 0xF22EC8u, net, &slot)) continue;
        avatar = ReadU32(em + 0xF32F20u + (uintptr_t)slot * 24u, 0u);
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
    if (nf < 0 || tech < 1u || tech > 11u) return 0;
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

static void PreloadForeignFactionPackagesPaced(
    uintptr_t gbase,
    uintptr_t ebase,
    size_t esize,
    float dt,
    unsigned cur_state,
    int pace_requests)
{
    /* NEVER preload foreign enemy packages during HF_STATE_PREPARE_MISSION (or outside active HF_STATE_MISSION):
     * D11 Mega City generation + RenderTerrainLightingMaps peak-allocates 45.6 MB VirtualAlloc buffers during PrepareMission. */
    if (cur_state != HF_STATE_MISSION) return;

    /* On Solo / Host, skip foreign package preloading entirely while Chaos is disabled ("off" preset).
     * Joined clients still preload in HF_STATE_MISSION so they stay ready for the Host's active config. */
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
        hf->log(self, "CHAOS_MISSION_CONTEXT: native_faction=%s difficulty=%d host=%d",
                chaos_faction_names[native], diff, Host());
    }

    int req_budget = CHAOS_MAX_PRELOAD_PKGS * CHAOS_FACTIONS;
    if (pace_requests) {
        g_preload_mission_time += dt;
        if (g_preload_mission_time < CHAOS_PRELOAD_SETTLE_S) {
            SendClientStatusIfNeeded(cur_state, ChaosRequiredForeignMask(native));
            return;
        }
        g_preload_tokens += dt * CHAOS_PRELOAD_RATE_PER_S;
        float max_burst = (dt >= 0.25f) ? 4.0f : 2.0f;
        if (g_preload_tokens > max_burst) g_preload_tokens = max_burst;
        req_budget = (int)g_preload_tokens;
    }

    uintptr_t pkg_sys = 0, map_entries_addr = 0;
    uint32_t map_cap = 0;
    uint64_t empty_key = 0;
    uint8_t* map_buf = NULL;
    if (ReadPtr(gbase + CHAOS_PKG_SYSTEM_RVA, &pkg_sys) &&
        ReadPtr(pkg_sys + 0x00u, &map_entries_addr) &&
        ReadMem(pkg_sys + 0x08u, &map_cap, 4) &&
        ReadMem(pkg_sys + 0x10u, &empty_key, 8) &&
        map_cap >= 16u && map_cap <= 65536u && (map_cap & (map_cap - 1u)) == 0u) {
        map_buf = (uint8_t*)malloc((size_t)map_cap * 16u);
        if (map_buf && !ReadMem(map_entries_addr, map_buf, (size_t)map_cap * 16u)) {
            free(map_buf);
            map_buf = NULL;
        }
    }

    int ready_count = 0;
    int block_next_faction = 0;
    for (int f = 0; f < CHAOS_FACTIONS; f++) {
        if (f == native) continue;

        if (!block_next_faction) {
            BuildPreloadList(gbase, f);

            /* Best-effort catalogue smoke flare package for Bot dropship arrivals (in HF_STATE_MISSION only). */
            if (f == CHAOS_BOTS && !g_bot_smoke_resident && hf->load_package) {
                int ls = hf->load_package(self, CHAOS_PKG_G3_SMOKE);
                if (ls == HF_PACKAGE_RESIDENT) g_bot_smoke_resident = 1;
            }

            if (map_buf && g_preload_built[f]) {
                for (int i = 0; i < g_preload_count[f]; i++) {
                    if (g_preload_requested[f][i]) continue;
                    uint64_t pkg_id = g_preload_pkgs[f][i];
                    int pkg_state = PackageStateExe(ebase, esize, pkg_id);
                    /* Already resident in Stingray PackageManager: no request or token needed. */
                    if (pkg_state == 2) {
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
                    if (!already || (pkg_state == 0 && refcnt == 0ULL)) {
                        ChaosRecordOwnedPkg(pkg_id);
                        CallPkgRequest(gbase, (void*)pkg_sys, &pkg_id, 1u);
                        if (pace_requests) {
                            --req_budget;
                            g_preload_tokens -= 1.0f;
                            if (g_preload_tokens < 0.0f) g_preload_tokens = 0.0f;
                        }
                        if (!ReadMem(map_entries_addr, map_buf, (size_t)map_cap * 16u)) {
                            free(map_buf);
                            map_buf = NULL;
                            break;
                        }
                        g_preload_requested[f][i] = 1;
                    } else if (pkg_state == 0 && refcnt == 1ULL && ChaosOwnsPkg(pkg_id) && slot_idx < map_cap) {
                        /* Confirmed unloaded (pkg_state == 0, zero read errors) AND exclusively held by Chaos's
                         * own prior mission request (refcnt == 1 && ChaosOwnsPkg): reset ref_count -> 0 in-place
                         * (preserving open-addressing probe chain) so RefcountedPackageRequest transitions 0 -> 1. */
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
                            free(map_buf);
                            map_buf = NULL;
                            break;
                        }
                        g_preload_requested[f][i] = 1;
                    } else if (pkg_state >= 1) {
                        /* Streaming or resident in PackageManager with existing refmap entry; wait for residency. */
                        g_preload_requested[f][i] = 1;
                    }
                    /* When pkg_state == -1 (transient read failure) or refcnt > 1 / unowned, leave refmap untouched
                     * and keep g_preload_requested[f][i] == 0 so a clean later tick re-evaluates safely. */
                }
            }
        }

        if (g_preload_built[f] && !g_faction_resident[f]) {
            int res = 0, idle = 0;
            if (CheckFactionResidency(gbase, ebase, esize, f, &res, &idle)) {
                g_faction_resident[f] = 1;
                if (!g_preload_ready_logged[f]) {
                    g_preload_ready_logged[f] = 1;
                    hf->log(self, "CHAOS_PRELOAD_READY: faction=%s resident=%d/%d idle=%d host=%d",
                            chaos_faction_names[f], res, g_preload_count[f], idle, Host());
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

    if (map_buf) free(map_buf);

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
    for (int i = 0; i < CHAOS_MAX_TRACKED; i++) {
        ChaosTrackedUnit* u = &g_tracker.units[i];
        if (!u->active) continue;
        double age = now_age - u->spawn_age;
        if (age < 0.0) age = 0.0;

        if (check_health && gbase && gsize >= CHAOS_HEALTH_RVA + 8u) {
            if (EntityHasHealthRecord(gbase, gsize, u->entity)) {
                u->saw_health = 1u;
            } else if (!u->saw_health && age >= 5.0) {
                hf->log(self, "CHAOS_PRUNE_NO_HEALTH: faction=%s entity=%u age=%.1fs",
                        (u->faction < CHAOS_FACTIONS) ? chaos_faction_names[u->faction] : "unknown",
                        u->entity, age);
                u->active = 0u;
                changed = 1;
                continue;
            }
        }

        if (living_players > 0 && age >= 180.0 && gbase) {
            float pos[3] = {0.0f, 0.0f, 0.0f};
            if (EntityPosition(gbase, u->entity, pos)) {
                float min_d2 = 1e30f;
                for (int p = 0; p < living_players; p++) {
                    float dx = pos[0] - living_pos[p][0];
                    float dy = pos[1] - living_pos[p][1];
                    float d2 = dx * dx + dy * dy;
                    if (d2 < min_d2) min_d2 = d2;
                }
                if (min_d2 > 150.0f * 150.0f) {
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

static int FindSafeGroundAnchor(uintptr_t gbase, float out_anchor[3], float out_player[3])
{
    float players[4][3];
    int living = CollectLivingPlayers(gbase, players, NULL);
    uintptr_t em = 0;
    if (living <= 0 || !ReadPtr(gbase + CHAOS_ENCOUNTER_RVA, &em)) return 0;

    int start_p = (int)(NextRand() % (uint32_t)living);
    for (int attempt = 0; attempt < 16; attempt++) {
        int strict = (attempt < 10);
        float max_dz = strict ? 6.0f : 12.0f;
        int pi = (start_p + attempt) % living;
        float angle = (float)(NextRand() % 3600u) * (3.14159265f / 1800.0f);
        float radius = 40.0f + (float)(NextRand() % 200u) * 0.1f; /* 40..60m probe center */
        float seed[3] = {
            players[pi][0] + radius * cosf(angle),
            players[pi][1] + radius * sinf(angle),
            players[pi][2]
        };
        float candidate[3] = {0.0f, 0.0f, 0.0f};
        CallProjectAnchor(gbase, (void*)em, candidate, seed);
        if (!ChaosAnchorValidEx(candidate, players[pi], players, living, max_dz)) continue;
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
    if (sq > 6) sq = 6;
    memcpy(g_last_wave_anchor, anchor, 12);
    g_has_last_wave_anchor = 1;

    if (g_pending.active) ChaosTrackerReleaseUnusedReservations(&g_tracker, g_pending.faction);
    memset(&g_pending, 0, sizeof g_pending);
    g_pending.active = 1;
    g_pending.faction = faction;
    g_pending.difficulty = diff;
    g_pending.squad_size = sq;
    memcpy(g_pending.anchor, anchor, 12);
    memcpy(g_pending.target, player_pos, 12);
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
         * native roster and dereference NULL. DispatchStaggeredRoster replicates 64-bit entity hashes cleanly. */
        int can_transport = !has_remote_peers &&
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

    /* Terminids: TriggerParticleFx (0x7B) + TriggerBreachBurst (0x7B) + staggered SpawnRosterRow (flags=0x24, kind=3) */
    uintptr_t exp_q = 0;
    uint32_t invalid_ent = ReadU32(gbase + CHAOS_INVALID_SPAWN_RVA, 0xFFFFFFFFu);
    if (ReadPtr(gbase + CHAOS_FX_QUEUE_RVA, &exp_q)) {
        CallTriggerParticleFx(gbase, (void*)exp_q, anchor, CHAOS_BREACH_FX_ID, invalid_ent);
        CallTriggerBreachBurst(gbase, (void*)exp_q, anchor, CHAOS_BREACH_FX_ID, invalid_ent);
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

/* ---- 250ms Host/Solo Mutual Hostility Perception Pass ---------------------------------------------------------- */

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

    /* Never write BHV_FACTIONS: scan read-only to discover live faction bitmasks. */
    uint32_t player_mask = 0;
    uint32_t observed_masks[CHAOS_FACTIONS] = {0, 0, 0};

    for (uint32_t i = 0; i < fac_count; i++) {
        uintptr_t desc = 0;
        uint64_t resource = 0;
        uint32_t mask = 0;
        if (!ReadPtr(fac_descs + (uintptr_t)i * 8u, &desc) || !ReadMem(desc, &resource, 8) ||
            !ReadMem(fac_masks + (uintptr_t)i * 4u, &mask, 4) || mask == 0)
            continue;
        if (resource == CHAOS_ENTITY_AVATAR) {
            player_mask |= mask;
            continue;
        }
        int fam = ChaosEnemyFamilyEx(resource);
        if (fam >= 0 && !ChaosIsTransportResource(resource)) observed_masks[fam] |= mask;
    }

    uint32_t all_enemy_masks = observed_masks[0] | observed_masks[1] | observed_masks[2];
    if (all_enemy_masks == 0) return;

    unsigned narrowed_now = 0, enemy_perceivers = 0;
    for (uint32_t i = 0; i < perc_count; i++) {
        uintptr_t desc = 0;
        uint64_t resource = 0;
        uint32_t entity = 0;
        uint8_t desc_flags = 0, active = 0;
        if (!ReadPtr(perc_descs + (uintptr_t)i * 8u, &desc)) continue;
        if (!ReadMem(desc, &resource, 8) || !ReadMem(desc + 8u, &entity, 4) ||
            !ReadMem(desc + 0x14u, &desc_flags, 1) || (desc_flags & 2u))
            continue;

        uintptr_t rec_addr = perc_recs + (uintptr_t)i * CHAOS_PERCEPTION_STRIDE;
        if (!ReadMem(rec_addr + 0x13F0u, &active, 1) || !active) continue;

        int fam = ChaosEnemyFamilyEx(resource);
        if (fam < 0 || ChaosIsTransportResource(resource)) continue;

        uint32_t fidx = 0, own_mask = 0, cur_ignore = 0;
        if (!HashFindU32(fac_mgr + 0x12050u, entity, &fidx) || fidx >= fac_count ||
            !ReadMem(fac_masks + (uintptr_t)fidx * 4u, &own_mask, 4) || own_mask == 0 ||
            (own_mask & player_mask) != 0)
            continue;

        if (!ReadMem(rec_addr + 0x13E8u, &cur_ignore, 4)) continue;
        ++enemy_perceivers;

        uint32_t next_ignore = ChaosNarrowIgnoreMask(cur_ignore, own_mask, all_enemy_masks, player_mask);
        if (next_ignore != cur_ignore && WriteU32(rec_addr + 0x13E8u, next_ignore)) ++narrowed_now;
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

/* ---- Config Bridge, Kill Switch, Events & Game-Thread Loop ----------------------------------------------------- */

static void WriteValues(FILE* f)
{
    fprintf(f, "enabled=%d\ncooldown_seconds=%d\nlanding_grace_seconds=%d\ntotal_cap=%d\n"
               "faction_cap=%d\nsquad_size=%d\nmission_budget=%d\ndroid_weight=%d\n"
               "squid_weight=%d\nbug_weight=%d\n",
            config.enabled, config.cooldown, config.grace, config.total_cap, config.faction_cap,
            config.squad_size, config.mission_budget, config.bots_weight, config.squids_weight,
            config.bugs_weight);
}

static void Publish(void)
{
    FILE* f = bridge[0] ? fopen(bridge, "wb") : NULL;
    if (f) {
        fprintf(f, "session=%lu\n", token);
        WriteValues(f);
        fclose(f);
        cd_get_file_mtime_100ns(bridge, &g_published_bridge_mtime);
    }
}

static void LoadOptions(const char* path, int session_required)
{
    FILE* f = path[0] ? fopen(path, "rb") : NULL;
    char line[160];
    ChaosConfig next = config;
    int count = 0;
    unsigned long session = 0;
    if (!f) return;
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
    int valid = !ferror(f) && (!session_required || count == 10);
    fclose(f);
    if (!valid) return;
    if (!session_required) {
        g_ini_config = next;
    }
    if (memcmp(&config, &next, sizeof next) != 0) {
        if (session_required) {
            uint64_t bm = 0ULL;
            if (cd_get_file_mtime_100ns(path, &bm) == 1) g_bridge_user_edit_mtime = bm;
        }
        config = next;
        if (!config.enabled && g_pending.active) {
            ChaosTrackerReleaseUnusedReservations(&g_tracker, g_pending.faction);
            memset(&g_pending, 0, sizeof g_pending);
        }
        ChaosReset(&clock_state, &config);
        g_shared_dirty = 1;
        hf->log(self, "CHAOS_OPTIONS: enabled=%d cooldown=%d grace=%d cap=%d faction_cap=%d squad=%d budget=%d",
                config.enabled, config.cooldown, config.grace, config.total_cap, config.faction_cap,
                config.squad_size, config.mission_budget);
    }
}

static void LoadHostOptions(int is_poll)
{
    if (!g_host_options_path[0]) return;
    uint64_t hm = 0ULL;
    int st = cd_get_file_mtime_100ns(g_host_options_path, &hm);
    if (st != 1) return;
    if (is_poll && hm == g_host_options_mtime) return;
    g_host_options_mtime = hm;
    if (is_poll && cd_session_override_is_newer(hm, g_bridge_user_edit_mtime)) return;

    CdHostOptions ho;
    if (cd_load_host_options_file(g_host_options_path, &ho) != 1) return;
    ChaosConfig next;
    ChaosApplyHostIntensity(&next, &g_ini_config, ho.chaos_intensity);
    if (memcmp(&config, &next, sizeof next) != 0) {
        config = next;
        if (!config.enabled && g_pending.active) {
            ChaosTrackerReleaseUnusedReservations(&g_tracker, g_pending.faction);
            memset(&g_pending, 0, sizeof g_pending);
        }
        ChaosReset(&clock_state, &config);
        g_shared_dirty = 1;
        if (is_poll && bridge[0]) Publish();
        hf->log(self, "CHAOS_HOST_OPTIONS: intensity=%s enabled=%d cooldown=%d grace=%d cap=%d faction_cap=%d squad=%d budget=%d",
                cd_chaos_intensity_name(ho.chaos_intensity), config.enabled, config.cooldown, config.grace,
                config.total_cap, config.faction_cap, config.squad_size, config.mission_budget);
    }
}

static int Killed(void)
{
    if (!stopped && stop_file[0]) {
        DWORD a = GetFileAttributesA(stop_file);
        if (a != INVALID_FILE_ATTRIBUTES || GetLastError() != ERROR_FILE_NOT_FOUND) {
            stopped = 1;
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
    landed = 0;
    g_native_faction = -1;
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
    }

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

static void OnSessionEnd(HfMod* mod, HfEvent ev, void* user)
{
    (void)mod; (void)ev; (void)user;
    InterlockedExchange(&g_phase, PHASE_ENDED);
}

static void Tick(HfMod* mod, float dt, void* user)
{
    (void)mod; (void)user;
    if (!ReadyPhase() || !isfinite(dt) || dt < 0.0f || dt > 5.0f) return;

    unsigned now = hf->game_state(self);
    unsigned host = (unsigned)Host();
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
    } else if (now != state) {
        unsigned prev_state = state;
        state = now;
        previous_role = host;
        if (prev_state == HF_STATE_PREPARE_MISSION && now == HF_STATE_MISSION && !g_shared_applied) {
            landed = 0;
            memset(&g_pending, 0, sizeof g_pending);
            ChaosReset(&clock_state, &config);
            ChaosTrackerReset(&g_tracker);
        }
    } else if (host != previous_role) {
        HandleHostMigration(previous_role, host);
        previous_role = host;
    }

    options_poll -= dt;
    if (options_poll <= 0.0f) {
        options_poll = 1.0f;
        LoadOptions(bridge, 1);
        LoadHostOptions(1);
    }

    if (!in_flow_now) return;

    /* v0.3.2 D11 Mega City OOM Fix: NEVER touch native Director/rosters or preload foreign packages during
     * HF_STATE_PREPARE_MISSION while level generation & RenderTerrainLightingMaps allocate 45.6 MB buffers. */
    if (now == HF_STATE_PREPARE_MISSION) {
        if (!g_preload_deferred_logged) {
            g_preload_deferred_logged = 1;
            hf->log(self, "CHAOS_PRELOAD_DEFERRED: state=PREPARE_MISSION reason=defer_until_mission");
        }
        if (!host) {
            SendClientStatusIfNeeded(now, 0u);
        }
        return;
    }
    if (now != HF_STATE_MISSION) return;

    if (!ProveChaosRuntime()) return;

    size_t gsize = 0, esize = 0;
    uintptr_t gbase = GameBase(&gsize), ebase = ExeBase(&esize);
    if (!gbase) return;

    /* All-peer curated foreign package preloading (strictly in HF_STATE_MISSION, smoothly paced) */
    PreloadForeignFactionPackagesPaced(gbase, ebase, esize, dt, now, 1);

    ChaosTrackerPrune(&g_tracker, EntityAlive, NULL);

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
    PruneStaleTrackedUnits(gbase, gsize, living_pos, living_players, clock_state.age);
    ReconcilePeers(gbase);

    /* 250ms Host/Solo mutual-hostility perception narrowing */
    perception_poll -= dt;
    if (perception_poll <= 0.0f) {
        perception_poll = 0.25f;
        if (config.enabled || g_tracker.total_live > 0) {
            UpdatePerceptionHostility(gbase);
        }
    }

    uintptr_t director = 0;
    int native = -1, diff = 0;
    if (!ReadDirectorContext(gbase, &director, &native, &diff)) return;

    ServicePendingWave(gbase, director, dt);

    if (!landed && living_players > 0) {
        landed = 1;
        ChaosReset(&clock_state, &config);
        g_shared_dirty = 1;
        hf->log(self, "CHAOS_LANDING: host=1 native_faction=%d grace=%ds", native, config.grace);
    }

    g_share_poll -= dt;
    if (g_shared_dirty || g_share_poll <= 0.0f) {
        g_share_poll = 2.0f;
        PublishSharedState(g_share_poll == 2.0f);
    }

    if (!ChaosDue(&clock_state, &config, dt, landed != 0)) return;
    g_shared_dirty = 1;

    int chosen = ChaosChoose(&config, native, diff, NextRand());

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
    ev.constructor = (chosen >= 0) && CallHasHeadroom(gbase, (void*)director, 3u, 0u);
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
    if (!FindSafeGroundAnchor(gbase, anchor, target)) {
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
    g_host_options_path[0] = 0;
    g_host_options_mtime = 0ULL;
    g_published_bridge_mtime = 0ULL;
    g_bridge_user_edit_mtime = 0ULL;
    status_text[0] = 0;

    const char* folder = api->mod_folder(mod);
    if (!folder || strlen(folder) > 1800) return 1;
    snprintf(options, sizeof options, "%s\\options.ini", folder);
    snprintf(stop_file, sizeof stop_file, "%s\\chaos.stop", folder);
    LoadOptions(options, 0);
    cd_resolve_host_options_path(folder, g_host_options_path, sizeof g_host_options_path);
    LoadHostOptions(0);

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
    if (!api->subscribe(mod, HF_ON_SPAWN, OnSpawn, NULL, 0) ||
        !api->on_game_thread(mod, Tick, NULL)) {
        InterlockedExchange(&g_phase, PHASE_ENDED);
        return 1;
    }
    if (HasChannelSend()) api->subscribe(mod, HF_ON_MESSAGE, OnMessage, NULL, 0);
    if (HasChannelShare()) api->subscribe(mod, HF_ON_SHARE, OnShare, NULL, 0);

    SetStatusOnce("Chaos Invasions 0.3.5 ready");
    api->log(mod, "CHAOS_INIT: version=0.3.5 build=25480438 sdk=16 enabled=%d cooldown=%d grace=%d cap=%d faction_cap=%d squad=%d budget=%d",
             config.enabled, config.cooldown, config.grace, config.total_cap, config.faction_cap,
             config.squad_size, config.mission_budget);
    return 0;
}
