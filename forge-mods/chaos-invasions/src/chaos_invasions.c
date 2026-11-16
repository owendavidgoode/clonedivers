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
#define CHAOS_TRANSPORT_TIMEOUT_S 12.0
#define CHAOS_BREACH_TIMEOUT_S    8.0
#define CHAOS_BREACH_STAGGER_S    3.5f

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
    int faction;
    int difficulty;
    int unloaded_count;
    int ship_seen;
    int cargo_patched;
    int squad_size;
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
static int g_preload_refmap_warned[CHAOS_FACTIONS];
static int g_bot_smoke_resident = 0;

static uint32_t g_last_faction_masks[CHAOS_FACTIONS];
static uint32_t g_last_player_mask = 0;
static unsigned g_perception_narrowed_total = 0;

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

static int PackageResidentExe(uintptr_t ebase, size_t esize, uint64_t pkg_id)
{
    uintptr_t engine = 0, manager = 0, resources = 0, list = 0;
    uint32_t count = 0;
    if (!ebase || esize < CHAOS_EXE_ENGINE_RVA + 8u || !g_exe_pins_ok || !pkg_id) return 0;
    if (!ReadPtr(ebase + CHAOS_EXE_ENGINE_RVA, &engine) ||
        !ReadPtr(engine + 0x400u, &manager) ||
        !ReadPtr(manager + 0x208u, &resources) ||
        !ReadMem(resources + 0x80u, &count, 4) || count == 0 || count > 8192u ||
        !ReadPtr(resources + 0x88u, &list))
        return 0;

    for (uint32_t i = 0; i < count; i++) {
        uintptr_t pkg = 0, parts_arr = 0;
        uint64_t id = 0;
        uint32_t parts = 0;
        if (!ReadPtr(list + (uintptr_t)i * 8u, &pkg)) continue;
        if (!ReadMem(pkg + 0x10u, &id, 8)) continue;
        int matched = (id == pkg_id);
        if (!ReadMem(pkg + 0x18u, &parts, 4) || parts == 0 || parts > 1024u) continue;
        if (!ReadPtr(pkg + 0x20u, &parts_arr)) continue;

        if (!matched) {
            for (uint32_t p = 0; p < parts; p++) {
                uintptr_t part = 0;
                uint64_t part_id = 0;
                if (ReadPtr(parts_arr + (uintptr_t)p * 8u, &part) &&
                    ReadMem(part + 0x10u, &part_id, 8) && part_id == pkg_id) {
                    matched = 1;
                    break;
                }
            }
        }
        if (!matched) continue;

        for (uint32_t p = 0; p < parts; p++) {
            uintptr_t part = 0;
            uint32_t pstate = 0;
            if (!ReadPtr(parts_arr + (uintptr_t)p * 8u, &part) ||
                !ReadMem(part + 0x1Cu, &pstate, 4) || pstate != 4u)
                return 0;
        }
        return 1;
    }
    return 0;
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

static void PreloadForeignTick(uintptr_t gbase, uintptr_t ebase, size_t esize, float dt)
{
    int native = -1, diff = 0;
    if (!ReadDirectorContext(gbase, NULL, &native, &diff)) return;
    if (g_native_faction != native) {
        g_native_faction = native;
        hf->log(self, "CHAOS_MISSION_CONTEXT: native_faction=%s difficulty=%d", chaos_faction_names[native], diff);
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
    for (int f = 0; f < CHAOS_FACTIONS; f++) {
        if (f == native) continue;
        BuildPreloadList(gbase, f);

        /* Best-effort catalogue smoke flare package for Bot dropship arrivals */
        if (f == CHAOS_BOTS && !g_bot_smoke_resident && hf->load_package) {
            int ls = hf->load_package(self, CHAOS_PKG_G3_SMOKE);
            if (ls == HF_PACKAGE_RESIDENT) g_bot_smoke_resident = 1;
        }

        if (map_buf && g_preload_built[f]) {
            for (int i = 0; i < g_preload_count[f]; i++) {
                if (g_preload_requested[f][i]) continue;
                uint64_t pkg_id = g_preload_pkgs[f][i];
                uint32_t used = 0;
                int already = 0;
                if (!ChaosRefMapHasHeadroom(map_buf, map_cap, empty_key, pkg_id, &used, &already)) {
                    if (!g_preload_refmap_warned[f]) {
                        g_preload_refmap_warned[f] = 1;
                        hf->log(self, "CHAOS_PRELOAD_REFUSED: faction=%s pkg=%016llx reason=refmap_75pct used=%u cap=%u",
                                chaos_faction_names[f], (unsigned long long)pkg_id, used, map_cap);
                    }
                    break;
                }
                if (!already) {
                    CallPkgRequest(gbase, (void*)pkg_sys, &pkg_id, 1u);
                    if (!ReadMem(map_entries_addr, map_buf, (size_t)map_cap * 16u)) {
                        free(map_buf);
                        map_buf = NULL;
                        break;
                    }
                }
                g_preload_requested[f][i] = 1;
            }
        }

        int was = g_faction_resident[f];
        int res = 0, idle = 0;
        g_faction_resident[f] = CheckFactionResidency(gbase, ebase, esize, f, &res, &idle);
        if (!was && g_faction_resident[f]) {
            hf->log(self, "CHAOS_PRELOAD_READY: faction=%s resident=%d/%d idle=%d",
                    chaos_faction_names[f], res, g_preload_count[f], idle);
        }
        if (g_faction_resident[f]) ++ready_count;
    }

    if (map_buf) free(map_buf);

    preload_log_poll -= dt;
    if (preload_log_poll <= 0.0f) {
        preload_log_poll = 15.0f;
        if (ready_count < 2) SetStatusOnce("Preloading foreign Chaos packages...");
        else if (Host()) SetStatusOnce("Chaos Invasions active (foreign factions ready)");
        else SetStatusOnce("Chaos client: foreign packages resident");
    }
}

/* ---- Ground Anchor Finder -------------------------------------------------------------------------------------- */

static int FindSafeGroundAnchor(uintptr_t gbase, float out_anchor[3], float out_player[3])
{
    float players[4][3];
    int living = CollectLivingPlayers(gbase, players, NULL);
    uintptr_t em = 0;
    if (living <= 0 || !ReadPtr(gbase + CHAOS_ENCOUNTER_RVA, &em)) return 0;

    int start_p = (int)(NextRand() % (uint32_t)living);
    for (int attempt = 0; attempt < 12; attempt++) {
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
        if (ChaosAnchorValid(candidate, players[pi], players, living)) {
            memcpy(out_anchor, candidate, 12);
            memcpy(out_player, players[pi], 12);
            return 1;
        }
    }
    return 0;
}

/* ---- Wave Dispatch & Staggered Fallback ------------------------------------------------------------------------ */

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
            ChaosTrackerRecordSpawn(&g_tracker, faction, eid, 1)) {
            ++dispatched;
        }
    }
    hf->log(self, "CHAOS_ROSTER_DISPATCH: faction=%s flags=0x%x count=%d immediate=%d anchor=(%.1f,%.1f,%.1f)",
            chaos_faction_names[faction], flags, count, dispatched, anchor[0], anchor[1], anchor[2]);
}

static void DispatchChaosWave(uintptr_t gbase, uintptr_t director, int faction, int diff,
                              const float anchor[3], const float player_pos[3])
{
    int sq = config.squad_size;
    if (sq < 1) sq = 1;
    if (sq > 6) sq = 6;

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
        int can_transport = cargo_faction != 0u &&
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
            hf->log(self, "CHAOS_WAVE_DISPATCH: event=%s mode=transport ship=%016llx q=%u count=%d cargo_patched=%d anchor=(%.1f,%.1f,%.1f)",
                    chaos_names[faction], (unsigned long long)ship, q_transports, sq,
                    g_pending.cargo_patched, anchor[0], anchor[1], anchor[2]);
            return;
        }

        /* Transport slot busy or unreadable: fall back immediately to staggered SpawnRosterRow (0x3C) */
        g_pending.is_transport = 0;
        g_pending.timeout = CHAOS_BREACH_TIMEOUT_S;
        DispatchStaggeredRoster(gbase, director, faction, diff, sq, 0x3Cu, anchor, player_pos, 2.0f);
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
    hf->log(self, "CHAOS_WAVE_DISPATCH: event=bug_breach mode=breach_fx count=%d anchor=(%.1f,%.1f,%.1f)",
            sq, anchor[0], anchor[1], anchor[2]);
}

static void ServicePendingWave(uintptr_t gbase, uintptr_t director, float dt)
{
    if (!g_pending.active) return;
    g_pending.elapsed += (double)dt;
    if (g_pending.unloaded_count >= g_pending.squad_size) {
        g_pending.active = 0;
        return;
    }
    if (g_pending.elapsed < g_pending.timeout) return;

    if (g_pending.is_transport && g_pending.unloaded_count == 0) {
        hf->log(self, "CHAOS_TRANSPORT_FALLBACK: faction=%s ship_seen=%d unloaded=0 after=%.1fs; calling SpawnRosterRow(0x3C)",
                chaos_faction_names[g_pending.faction], g_pending.ship_seen, g_pending.elapsed);
        g_pending.is_transport = 0;
        g_pending.elapsed = 0.0;
        g_pending.timeout = CHAOS_BREACH_TIMEOUT_S;
        DispatchStaggeredRoster(gbase, director, g_pending.faction, g_pending.difficulty, g_pending.squad_size,
                                0x3Cu, g_pending.anchor, g_pending.target, 2.0f);
        return;
    }

    ChaosTrackerReleaseUnusedReservations(&g_tracker, g_pending.faction);
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
        int fam = ChaosEnemyFamily(resource);
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

        int fam = ChaosEnemyFamily(resource);
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
    if (valid && memcmp(&config, &next, sizeof next) != 0) {
        config = next;
        ChaosReset(&clock_state, &config);
        hf->log(self, "CHAOS_OPTIONS: enabled=%d cooldown=%d grace=%d cap=%d faction_cap=%d squad=%d budget=%d",
                config.enabled, config.cooldown, config.grace, config.total_cap, config.faction_cap,
                config.squad_size, config.mission_budget);
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
    memset(g_preload_refmap_warned, 0, sizeof g_preload_refmap_warned);
    g_last_player_mask = 0;
    ChaosReset(&clock_state, &config);
    ChaosTrackerReset(&g_tracker);
}

static void State(HfMod* mod, const HfEventInfo* info, void* user)
{
    (void)mod; (void)user;
    if (!ReadyPhase() || !info || info->size < sizeof(HfStateEvent)) return;
    const HfStateEvent* e = (const HfStateEvent*)info;
    state = e->to;
    ResetMissionState();
}

static void Player(HfMod* mod, const HfEventInfo* info, void* user)
{
    (void)mod; (void)user;
    if (!ReadyPhase() || !info || info->size < sizeof(HfPlayerEvent)) return;
    const HfPlayerEvent* e = (const HfPlayerEvent*)info;
    if (!e->entity || hf->game_state(self) != HF_STATE_MISSION || !Host()) return;
    if (!landed) {
        landed = 1;
        ChaosReset(&clock_state, &config);
        hf->log(self, "CHAOS_LANDING: host=1 native_faction=%d grace=%ds", g_native_faction, config.grace);
    }
}

static void OnSpawn(HfMod* mod, const HfEventInfo* info, void* user)
{
    (void)mod; (void)user;
    if (!ReadyPhase() || !info || info->size < sizeof(HfSpawnEvent) || hf->game_state(self) != HF_STATE_MISSION || !Host())
        return;
    const HfSpawnEvent* e = (const HfSpawnEvent*)info;
    if (!e->authority) return;
    int fam = ChaosEnemyFamily(e->resource);
    if (fam < 0) return;

    if (ChaosIsTransportResource(e->resource)) {
        if (g_pending.active && g_pending.faction == fam) {
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
        return;
    }

    if (g_pending.active && g_pending.faction == fam) {
        ++g_pending.unloaded_count;
        ChaosTrackerRecordSpawn(&g_tracker, fam, e->entity, 1);
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
    if (now != state || host != previous_role) {
        state = now;
        previous_role = host;
        ResetMissionState();
    }

    options_poll -= dt;
    if (options_poll <= 0.0f) {
        options_poll = 1.0f;
        LoadOptions(bridge, 1);
    }

    if (now != HF_STATE_MISSION) return;
    if (!ProveChaosRuntime()) return;

    size_t gsize = 0, esize = 0;
    uintptr_t gbase = GameBase(&gsize), ebase = ExeBase(&esize);
    if (!gbase) return;

    /* All-peer curated foreign package preloading */
    PreloadForeignTick(gbase, ebase, esize, dt);

    if (!host) return;

    /* 250ms Host/Solo mutual-hostility perception narrowing */
    perception_poll -= dt;
    if (perception_poll <= 0.0f) {
        perception_poll = 0.25f;
        UpdatePerceptionHostility(gbase);
    }

    uintptr_t director = 0;
    int native = -1, diff = 0;
    if (!ReadDirectorContext(gbase, &director, &native, &diff)) return;

    ChaosTrackerPrune(&g_tracker, EntityAlive, NULL);
    ServicePendingWave(gbase, director, dt);

    if (!ChaosDue(&clock_state, &config, dt, landed != 0)) return;

    int chosen = ChaosChoose(&config, native, diff, NextRand());
    float living_pos[4][3];
    int living_players = CollectLivingPlayers(gbase, living_pos, NULL);

    ChaosEvidence ev = {0};
    ev.host = (int)host;
    ev.mission = 1;
    ev.players = living_players > 0;
    ev.native_faction = native;
    ev.difficulty = diff;
    ev.closure = (chosen >= 0 && g_rosters_loaded[chosen]);
    ev.resident = (chosen >= 0 && g_faction_resident[chosen]);
    ev.peers = 1;
    ev.placement = 1;
    ev.constructor = (chosen >= 0) && CallHasHeadroom(gbase, (void*)director, 3u, 0u);
    ev.arrival = g_pins_ok;
    ev.population_known = 1;
    for (int f = 0; f < CHAOS_FACTIONS; f++) ev.live[f] = ChaosEffectiveLiveFaction(&g_tracker, f);
    ev.total_live = ChaosEffectiveLiveTotal(&g_tracker);
    ev.mission_spawned = g_tracker.mission_spawned;

    const char* refuse_why = ChaosRefuse(&config, &ev, chosen);
    if (refuse_why) {
        hf->log(self, "CHAOS_SKIP: attempt=%u event=%s reason=%s total=%d/%d spawned=%d/%d",
                clock_state.attempts, (chosen >= 0 && chosen < 3) ? chaos_names[chosen] : "none",
                refuse_why, ev.total_live, config.total_cap, ev.mission_spawned, config.mission_budget);
        return;
    }

    float anchor[3], target[3];
    if (!FindSafeGroundAnchor(gbase, anchor, target)) {
        hf->log(self, "CHAOS_SKIP: attempt=%u event=%s reason=all_player_clearance_and_navmesh_unqualified",
                clock_state.attempts, chaos_names[chosen]);
        return;
    }

    DispatchChaosWave(gbase, director, chosen, diff, anchor, target);
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
    status_text[0] = 0;

    const char* folder = api->mod_folder(mod);
    if (!folder || strlen(folder) > 1800) return 1;
    snprintf(options, sizeof options, "%s\\options.ini", folder);
    snprintf(stop_file, sizeof stop_file, "%s\\chaos.stop", folder);
    LoadOptions(options, 0);

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
    if (!api->subscribe(mod, HF_ON_STATE, State, NULL, 0) ||
        !api->subscribe(mod, HF_ON_PLAYER_SPAWN, Player, NULL, 0) ||
        !api->subscribe(mod, HF_ON_SPAWN, OnSpawn, NULL, 0) ||
        !api->on_game_thread(mod, Tick, NULL)) {
        InterlockedExchange(&g_phase, PHASE_ENDED);
        return 1;
    }

    SetStatusOnce("Chaos Invasions 0.2.0 ready");
    api->log(mod, "CHAOS_INIT: version=0.2.0 build=25480438 sdk=16 enabled=%d cooldown=%d grace=%d cap=%d faction_cap=%d squad=%d budget=%d",
             config.enabled, config.cooldown, config.grace, config.total_cap, config.faction_cap,
             config.squad_size, config.mission_budget);
    return 0;
}
