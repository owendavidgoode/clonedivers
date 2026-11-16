#include "../src/chaos_invasions.c"
#include <stdarg.h>

static unsigned checks, subscriptions, ticks, fail_subscription, skipped_logs, fallback_logs, landing_logs, preload_ready_logs;
static unsigned spawn_logs, spawn_unmapped_logs, transport_spawn_logs, wave_complete_logs, wave_timeout_logs;
static unsigned role = HF_SESSION_SOLO, game_state = HF_STATE_MISSION;
static char fixture[1800];

#define CHECK(x) do { ++checks; if (!(x)) { printf("FAIL line %d: %s\n", __LINE__, #x); exit(1); } } while (0)

/* ---- Mocked SDK & Synthetic World Fixtures --------------------------------------------------------------------- */

static int MockRead(uintptr_t address, void* buffer, size_t size)
{
    if (!address || !buffer) return 1;
    memcpy(buffer, (const void*)address, size);
    return 0;
}

static int MockReadCode(uintptr_t address, void* buffer, size_t size)
{
    return MockRead(address, buffer, size);
}

static int MockWrite(uintptr_t address, const void* data, size_t size)
{
    if (!address || !data) return 1;
    memcpy((void*)address, data, size);
    return 0;
}

static const char* Build(void) { return "25480438"; }
static const char* Folder(HfMod* m) { (void)m; return fixture; }
static void Session(HfMod* m, HfSession* s) { (void)m; memset(s, 0, sizeof *s); s->role = role; }
static unsigned GameState(HfMod* m) { (void)m; return game_state; }

static void Log(HfMod* m, const char* format, ...)
{
    (void)m;
    char buf[512];
    va_list ap;
    va_start(ap, format);
    vsnprintf(buf, sizeof buf, format, ap);
    va_end(ap);
    if (strstr(buf, "CHAOS_SKIP")) ++skipped_logs;
    if (strstr(buf, "CHAOS_TRANSPORT_FALLBACK")) ++fallback_logs;
    if (strstr(buf, "CHAOS_LANDING")) ++landing_logs;
    if (strstr(buf, "CHAOS_PRELOAD_READY")) ++preload_ready_logs;
    if (strstr(buf, "CHAOS_SPAWN:")) ++spawn_logs;
    if (strstr(buf, "CHAOS_SPAWN_UNMAPPED:")) ++spawn_unmapped_logs;
    if (strstr(buf, "CHAOS_TRANSPORT_SPAWN:")) ++transport_spawn_logs;
    if (strstr(buf, "CHAOS_WAVE_COMPLETE:") && strstr(buf, "result=complete")) ++wave_complete_logs;
    if (strstr(buf, "CHAOS_WAVE_COMPLETE:") && strstr(buf, "result=timeout")) ++wave_timeout_logs;
}

static void Status(HfMod* m, const char* s) { (void)m; (void)s; }

static int Subscribe(HfMod* m, uint32_t k, HfListener cb, void* u, int32_t p)
{
    (void)m; (void)k; (void)cb; (void)u; (void)p;
    ++subscriptions;
    return subscriptions == fail_subscription ? 0 : 1;
}

static int OnEvent(HfMod* m, HfEvent e, HfEventCallback cb, void* u)
{
    (void)m; (void)e; (void)cb; (void)u;
    return 0;
}

static int OnTick(HfMod* m, HfTickCallback cb, void* u)
{
    (void)m; (void)cb; (void)u;
    ++ticks;
    return 1;
}

static int g_mock_smoke_state = 0;
static unsigned g_mock_explosion_calls = 0;
static uint32_t g_mock_explosion_type = 0;

static int MockLoadPackage(HfMod* m, uint64_t pkg)
{
    (void)m;
    if (pkg == CHAOS_PKG_G3_SMOKE) {
        g_mock_smoke_state = 1;
        return HF_PACKAGE_RESIDENT;
    }
    return 0;
}

static uintptr_t MockModuleBase(const char* name, size_t* size_out)
{
    if (name && strcmp(name, "helldivers2.exe") == 0) return ExeBase(size_out);
    return GameBase(size_out);
}

static int MockSpawnExplosion(HfMod* m, uint32_t type, const float pos[3])
{
    (void)m; (void)pos;
    ++g_mock_explosion_calls;
    g_mock_explosion_type = type;
    return HF_ACT_REQUESTED;
}

/* Fake game.dll & helldivers2.exe memory arenas */
static uint8_t* g_arena_game;
static uint8_t* g_arena_exe;
static const size_t kArenaGameSize = 0x3500000u;
static const size_t kArenaExeSize  = 0x1B50000u;

typedef struct TestPkgRecord {
    uint64_t id;
    uint32_t parts;
    void* part_ptrs[1050];
    struct { uint32_t pad[7]; uint32_t state; uint64_t id; } part_objs[1050];
} TestPkgRecord;

static uint8_t* g_director_mem;
static uint8_t* g_em_mem;
static uint8_t* g_exp_queue_mem;
static uint8_t* g_pm_mem;
static uint8_t* g_emgr_mem;
static uint8_t* g_tm_mem;
static uint8_t* g_hm_mem;
static uint8_t* g_fac_mgr_mem;
static uint8_t* g_perc_mgr_mem;
static uint8_t* g_pkg_sys_mem;
static uint8_t* g_pass_comp_mem;

typedef struct RefSlot { uint64_t key; uint64_t ref; } RefSlot;
static RefSlot g_ref_slots[256];
static uint8_t g_bot_rows[61][0x80];
static uint8_t g_squid_rows[46][0x80];
static uint8_t g_bug_rows[45][0x80];

static struct { uint64_t tag; uint64_t pkg0; uint64_t pkg1; uint64_t pad; } g_lookup_recs[64];
static uint64_t g_lookup_hashes[64];
static int g_lookup_count = 0;

/* Engine package list */
typedef struct FakeExePart { uint64_t pad0; uint64_t pad1; uint64_t part_id; uint32_t pad3; uint32_t state; } FakeExePart;
typedef struct FakeExePkg {
    uint64_t pad0;
    uint64_t pad1;
    uint64_t id;        /* +0x10 */
    uint32_t parts;     /* +0x18 */
    uint32_t pad2;
    void** part_ptrs;   /* +0x20 */
    FakeExePart part_storage[16];
    void* ptr_storage[16];
} FakeExePkg;

static FakeExePkg g_exe_pkgs[64];
static void* g_exe_pkg_ptrs[64];
static uint32_t g_exe_pkg_count = 0;
static uint8_t g_exe_engine_mem[0x500];
static uint8_t g_exe_mgr_mem[0x300];
static uint8_t g_exe_res_mem[0x100];

/* Hash table helper storage for PM / EM / TM / Factions / Passenger */
typedef struct KV32 { uint32_t k; uint32_t v; } KV32;
static KV32 g_em_net_buckets[32];
static KV32 g_tm_buckets[32];
static KV32 g_fac_buckets[32];
static KV32 g_pass_buckets[32];
static uint8_t g_tm_data[32][0x308];

typedef struct FacDesc { uint64_t resource; uint32_t entity; uint8_t pad[8]; uint8_t flags; } FacDesc;
static FacDesc g_fac_descs_storage[16];
static void* g_fac_desc_ptrs[16];
static uint32_t g_fac_masks[16];
static FacDesc g_perc_descs_storage[16];
static void* g_perc_desc_ptrs[16];
static uint8_t* g_perc_records_mem;
static FacDesc g_pass_descs_storage[16];
static void* g_pass_desc_ptrs[16];
static uint8_t g_pass_cargo_mem[16 * CHAOS_CARGO_STRIDE];
static uint8_t g_pass_seats_mem[16 * CHAOS_SEATS_STRIDE];

/* Mock call counters & captures */
static unsigned g_transport_calls = 0, g_roster_calls = 0, g_fx_calls = 0, g_burst_calls = 0;
static uint64_t g_last_ship = 0;
static uint32_t g_scoped_director_faction_during_transport = 0;
static uint64_t g_scoped_director_roster_during_transport = 0;
static float g_last_transport_pos[3], g_last_transport_drop[3], g_last_transport_target[3];
static int g_fire_sync_transport_onspawn = 0;
static int g_force_reject_last_role = 0;
static uint32_t g_last_roster_flags = 0, g_last_roster_kind = 0;
static float g_last_roster_delays[8];
static uint8_t g_mock_idle = 1;
static int g_mock_headroom = 1;
static int g_mock_alive = 1;
static int g_force_anchor_z_bad = 0;

static const uint8_t* MockLookupEntityPkgs(uint64_t hash)
{
    for (int i = 0; i < g_lookup_count; i++) {
        if (g_lookup_hashes[i] == hash) return (const uint8_t*)&g_lookup_recs[i];
    }
    return NULL;
}

static void AddExeResidentPkg(uint64_t id, uint32_t parts, uint32_t pstate)
{
    for (uint32_t i = 0; i < g_exe_pkg_count; i++) {
        if (g_exe_pkgs[i].id == id) {
            g_exe_pkgs[i].parts = parts;
            for (uint32_t p = 0; p < (parts < 16u ? parts : 16u); p++) g_exe_pkgs[i].part_storage[p].state = pstate;
            return;
        }
    }
    uint32_t idx = g_exe_pkg_count++;
    memset(&g_exe_pkgs[idx], 0, sizeof g_exe_pkgs[idx]);
    g_exe_pkgs[idx].id = id;
    g_exe_pkgs[idx].parts = parts;
    g_exe_pkgs[idx].part_ptrs = g_exe_pkgs[idx].ptr_storage;
    uint32_t alloc = parts < 16u ? parts : 16u;
    for (uint32_t p = 0; p < alloc; p++) {
        g_exe_pkgs[idx].part_storage[p].part_id = id;
        g_exe_pkgs[idx].part_storage[p].state = pstate;
        g_exe_pkgs[idx].ptr_storage[p] = &g_exe_pkgs[idx].part_storage[p];
    }
    /* When testing parts > 1024, reuse slot 0 safely */
    for (uint32_t p = alloc; p < parts && p < 16u; p++) {
        g_exe_pkgs[idx].ptr_storage[p] = &g_exe_pkgs[idx].part_storage[0];
    }
    g_exe_pkg_ptrs[idx] = &g_exe_pkgs[idx];
    memcpy(g_exe_res_mem + 0x80u, &g_exe_pkg_count, 4);
}

static void MockPkgRequest(void* sys, const uint64_t* ids, uint32_t count)
{
    (void)sys;
    for (uint32_t k = 0; k < count; k++) {
        uint64_t pkg = ids[k];
        int found = 0;
        for (int i = 0; i < 256; i++) {
            if (g_ref_slots[i].key == pkg) { ++g_ref_slots[i].ref; found = 1; break; }
        }
        if (!found) {
            for (int i = 0; i < 256; i++) {
                if (g_ref_slots[i].key == 0ULL) { g_ref_slots[i].key = pkg; g_ref_slots[i].ref = 1ULL; break; }
            }
        }
        AddExeResidentPkg(pkg, 2u, 4u);
    }
}

static uint8_t MockPkgsIdle(void* unused) { (void)unused; return g_mock_idle; }
static uint8_t MockHasHeadroom(void* d, uint32_t k, uint8_t b) { (void)d; (void)k; (void)b; return (uint8_t)g_mock_headroom; }
static int MockAlive(uint32_t e) { (void)e; return g_mock_alive; }

static float* MockProjectAnchor(void* em, float out[3], const float in[3], int radius)
{
    (void)em; (void)radius;
    out[0] = in[0];
    out[1] = in[1];
    out[2] = g_force_anchor_z_bad ? (in[2] + 40.0f) : (in[2] + 2.0f);
    return out;
}

static void MockSpawnTransport(void* em, uint32_t eid, const uint32_t* roles, uint32_t count, const float pos[3],
                               float spacing, const float drop_pos[3], const float target_pos[3], uint64_t ship)
{
    (void)em; (void)eid; (void)spacing;
    ++g_transport_calls;
    g_last_ship = ship;
    if (pos) memcpy(g_last_transport_pos, pos, 12);
    if (drop_pos) memcpy(g_last_transport_drop, drop_pos, 12);
    if (target_pos) memcpy(g_last_transport_target, target_pos, 12);
    memcpy(&g_scoped_director_faction_during_transport, g_director_mem + 0x518BCu, 4);
    memcpy(&g_scoped_director_roster_during_transport, g_director_mem + 0x660u, 8);

    /* Simulate native 0xFDC140 -> 0x687E40 -> 0x683E50 -> 0x953150([director+0x660]):
     * 0x8B61F0 zeroes cargo.faction = 0, 0x683E50 resolves roles against [director+0x660]. */
    if (g_pass_comp_mem && count <= 16u) {
        uint32_t active = 0;
        memcpy(&active, g_pass_comp_mem + 0x10u, 4);
        if (active < 16u) {
            uint32_t slot = active;
            uint32_t ship_eid = 777u + (uint32_t)g_transport_calls;
            g_pass_descs_storage[slot].resource = ship;
            g_pass_descs_storage[slot].entity = ship_eid;
            g_pass_desc_ptrs[slot] = &g_pass_descs_storage[slot];
            g_pass_buckets[ship_eid % 32u].k = ship_eid;
            g_pass_buckets[ship_eid % 32u].v = slot;

            uint8_t* cargo = g_pass_cargo_mem + (size_t)slot * CHAOS_CARGO_STRIDE;
            uint8_t* seats = g_pass_seats_mem + (size_t)slot * CHAOS_SEATS_STRIDE;
            memset(cargo, 0, CHAOS_CARGO_STRIDE);
            memset(seats, 0, CHAOS_SEATS_STRIDE);
            uint32_t zero_fac = 0u, kind3 = 3u;
            memcpy(cargo + 0x00u, &zero_fac, 4); /* 0x8B61F0 zeroes context+0x628 */
            memcpy(cargo + 0x04u, &count, 4);
            memcpy(cargo + 0x48u, &kind3, 4);
            memcpy(seats + 0x3CCu, &count, 4);

            uintptr_t roster_ptr = (uintptr_t)g_scoped_director_roster_during_transport;
            uintptr_t rows_ptr = 0;
            uint32_t rcount = 0;
            if (roster_ptr) {
                memcpy(&rows_ptr, (const void*)roster_ptr, 8);
                memcpy(&rcount, (const void*)(roster_ptr + 8u), 4);
            }
            for (uint32_t k = 0; k < count; k++) {
                uint32_t resolved = 0xFFFFFFFFu;
                if (!(g_force_reject_last_role && k + 1u == count) && rows_ptr && rcount <= 512u) {
                    for (uint32_t r = 0; r < rcount; r++) {
                        uint32_t rr = 0;
                        memcpy(&rr, (const void*)(rows_ptr + (uintptr_t)r * 0x80u), 4);
                        if (rr == roles[k]) { resolved = r; break; }
                    }
                }
                memcpy(cargo + 0x08u + (uintptr_t)k * 4u, &resolved, 4);
                memcpy(seats + 0x80u + (uintptr_t)k * 4u, &resolved, 4);
            }
            active = slot + 1u;
            memcpy(g_pass_comp_mem + 0x10u, &active, 4);

            if (g_fire_sync_transport_onspawn) {
                HfSpawnEvent ship_ev = {0};
                ship_ev.info.size = sizeof ship_ev;
                ship_ev.info.kind = HF_ON_SPAWN;
                ship_ev.entity = ship_eid;
                ship_ev.resource = ship;
                ship_ev.authority = 1;
                OnSpawn(self, (const HfEventInfo*)&ship_ev, NULL);
            }
        }
    }
}

static void* MockSpawnRosterRow(void* dir, const void* row, const float pos[3], uint32_t kind, uint32_t flags,
                                const float target_pos[3], const float* quat4, const void* extra, float delay, uint32_t z)
{
    (void)dir; (void)row; (void)pos; (void)target_pos; (void)quat4; (void)extra; (void)z;
    if (g_roster_calls < 8u) g_last_roster_delays[g_roster_calls] = delay;
    ++g_roster_calls;
    g_last_roster_flags = flags;
    g_last_roster_kind = kind;
    return NULL;
}

static void MockParticleFx(void* q, uint64_t res, const float pos[3], uint32_t fx, uint32_t se, uint32_t oe, uint64_t cr, uint32_t te)
{
    (void)q; (void)res; (void)pos; (void)fx; (void)se; (void)oe; (void)cr; (void)te;
    ++g_fx_calls;
}

static void MockBreachBurst(void* q, const float pos[3], uint32_t fx, uint32_t se, uint32_t oe, uint64_t z, uint32_t te)
{
    (void)q; (void)pos; (void)fx; (void)se; (void)oe; (void)z; (void)te;
    ++g_burst_calls;
}

static void InitMockGameArenas(uint32_t native_director_mask, uint32_t difficulty)
{
    if (!g_arena_game) g_arena_game = (uint8_t*)calloc(1, kArenaGameSize);
    else memset(g_arena_game, 0, kArenaGameSize);
    if (!g_arena_exe) g_arena_exe = (uint8_t*)calloc(1, kArenaExeSize);
    else memset(g_arena_exe, 0, kArenaExeSize);

    if (!g_director_mem) g_director_mem = (uint8_t*)calloc(1, 0x60000u);
    else memset(g_director_mem, 0, 0x60000u);
    if (!g_em_mem) g_em_mem = (uint8_t*)calloc(1, 0x1000u);
    else memset(g_em_mem, 0, 0x1000u);
    if (!g_exp_queue_mem) g_exp_queue_mem = (uint8_t*)calloc(1, 0x100u);
    else memset(g_exp_queue_mem, 0, 0x100u);
    if (!g_pm_mem) g_pm_mem = (uint8_t*)calloc(1, 0x1000u);
    else memset(g_pm_mem, 0, 0x1000u);
    if (!g_emgr_mem) g_emgr_mem = (uint8_t*)calloc(1, 0xF50000u);
    else memset(g_emgr_mem, 0, 0xF50000u);
    if (!g_tm_mem) g_tm_mem = (uint8_t*)calloc(1, 0x200u);
    else memset(g_tm_mem, 0, 0x200u);
    if (!g_hm_mem) g_hm_mem = (uint8_t*)calloc(1, 0x2000u);
    else memset(g_hm_mem, 0, 0x2000u);
    if (!g_fac_mgr_mem) g_fac_mgr_mem = (uint8_t*)calloc(1, 0x13000u);
    else memset(g_fac_mgr_mem, 0, 0x13000u);
    if (!g_perc_mgr_mem) g_perc_mgr_mem = (uint8_t*)calloc(1, 0x200u);
    else memset(g_perc_mgr_mem, 0, 0x200u);
    if (!g_perc_records_mem) g_perc_records_mem = (uint8_t*)calloc(16, CHAOS_PERCEPTION_STRIDE);
    else memset(g_perc_records_mem, 0, 16u * CHAOS_PERCEPTION_STRIDE);
    if (!g_pkg_sys_mem) g_pkg_sys_mem = (uint8_t*)calloc(1, 0x40u);
    else memset(g_pkg_sys_mem, 0, 0x40u);
    if (!g_pass_comp_mem) g_pass_comp_mem = (uint8_t*)calloc(1, 0x100u);
    else memset(g_pass_comp_mem, 0, 0x100u);

    /* Copy all game.dll 32-byte code pins + 2 exe pins */
    for (int i = 0; i < CPIN_COUNT; i++) {
        memcpy(g_arena_game + kChaosCodePins[i].rva, kChaosCodePins[i].bytes, 32);
    }
    memcpy(g_arena_exe + CHAOS_EXE_PKG_HAS_LOADED_RVA, kExeHasLoadedPin, 32);
    memcpy(g_arena_exe + CHAOS_EXE_PKG_FIND_RVA, kExeFindPin, 32);

    /* Static rosters */
    float w1 = 1.0f;
    for (int r = 0; r < 61; r++) {
        uint32_t role_id = 0x1000u + (uint32_t)r;
        uint64_t eh = 0x4019623142351cb6ull + (uint64_t)r;
        if (r == 0) eh = 0x4019623142351cb6ull; /* Automaton known hash */
        memcpy(g_bot_rows[r], &role_id, 4);
        memcpy(g_bot_rows[r] + 8, &eh, 8);
        for (int d = 0; d < 10; d++) memcpy(g_bot_rows[r] + 0x30 + d * 4, &w1, 4);
    }
    for (int r = 0; r < 46; r++) {
        uint32_t role_id = 0x2000u + (uint32_t)r;
        uint64_t eh = 0x905809a4c28d8a45ull + (uint64_t)r;
        if (r == 4) eh = 0x905809a4c28d8a45ull; /* Voteless known hash */
        memcpy(g_squid_rows[r], &role_id, 4);
        memcpy(g_squid_rows[r] + 8, &eh, 8);
        for (int d = 0; d < 10; d++) memcpy(g_squid_rows[r] + 0x30 + d * 4, &w1, 4);
    }
    for (int r = 0; r < 45; r++) {
        uint32_t role_id = 0x3000u + (uint32_t)r;
        uint64_t eh = 0x51eea86bf6997e4eull + (uint64_t)r;
        if (r == 0) eh = 0x51eea86bf6997e4eull; /* Scavenger known hash */
        memcpy(g_bug_rows[r], &role_id, 4);
        memcpy(g_bug_rows[r] + 8, &eh, 8);
        for (int d = 0; d < 10; d++) memcpy(g_bug_rows[r] + 0x30 + d * 4, &w1, 4);
    }
    void* br = g_bot_rows; uint32_t bc = 61u;
    void* sr = g_squid_rows; uint32_t sc = 46u;
    void* ur = g_bug_rows; uint32_t uc = 45u;
    memcpy(g_arena_game + CHAOS_ROSTER_BOTS_RVA, &br, 8); memcpy(g_arena_game + CHAOS_ROSTER_BOTS_RVA + 8, &bc, 4);
    memcpy(g_arena_game + CHAOS_ROSTER_SQUIDS_RVA, &sr, 8); memcpy(g_arena_game + CHAOS_ROSTER_SQUIDS_RVA + 8, &sc, 4);
    memcpy(g_arena_game + CHAOS_ROSTER_BUGS_RVA, &ur, 8); memcpy(g_arena_game + CHAOS_ROSTER_BUGS_RVA + 8, &uc, 4);

    /* Director (+0x518BC, +0x518C4, +0x660) */
    void* p = g_director_mem;
    memcpy(g_arena_game + CHAOS_DIRECTOR_RVA, &p, 8);
    memcpy(g_director_mem + 0x518BCu, &native_director_mask, 4);
    memcpy(g_director_mem + 0x518C4u, &difficulty, 4);
    uint32_t native_rva = CHAOS_ROSTER_BUGS_RVA;
    if (native_director_mask == CHAOS_NATIVE_BOTS_BIT) native_rva = CHAOS_ROSTER_BOTS_RVA;
    else if (native_director_mask == CHAOS_NATIVE_SQUIDS_BIT) native_rva = CHAOS_ROSTER_SQUIDS_RVA;
    uint64_t dir_roster = (uint64_t)(uintptr_t)(g_arena_game + native_rva);
    memcpy(g_director_mem + 0x660u, &dir_roster, 8);

    /* PassengerComponent (game+0x3326CC0) */
    memset(g_pass_buckets, 0xFF, sizeof g_pass_buckets);
    memset(g_pass_descs_storage, 0, sizeof g_pass_descs_storage);
    memset(g_pass_desc_ptrs, 0, sizeof g_pass_desc_ptrs);
    memset(g_pass_cargo_mem, 0, sizeof g_pass_cargo_mem);
    memset(g_pass_seats_mem, 0, sizeof g_pass_seats_mem);
    uint32_t cap32 = 32u, empty32 = 0xFFFFFFFFu, mult32 = 1u;
    void* pbptr = g_pass_buckets;
    void* pdptr = g_pass_desc_ptrs;
    void* psptr = g_pass_seats_mem;
    void* pcptr = g_pass_cargo_mem;
    memcpy(g_pass_comp_mem + 0x20u + 0, &pbptr, 8);
    memcpy(g_pass_comp_mem + 0x20u + 8, &cap32, 4);
    memcpy(g_pass_comp_mem + 0x20u + 12, &empty32, 4);
    memcpy(g_pass_comp_mem + 0x20u + 16, &mult32, 4);
    memcpy(g_pass_comp_mem + 0x38u, &pdptr, 8);
    memcpy(g_pass_comp_mem + 0x48u, &psptr, 8);
    memcpy(g_pass_comp_mem + 0x50u, &pcptr, 8);
    p = g_pass_comp_mem;
    memcpy(g_arena_game + CHAOS_PASSENGER_RVA, &p, 8);

    /* Register unit package lookups */
    g_lookup_count = 0;
    uint64_t extra_pairs[][2] = {
        {CHAOS_ENTITY_DROPSHIP, 0xAAAA000100000001ull},
        {CHAOS_ENTITY_WARPSHIP, 0xBBBB000100000001ull},
        {0x4019623142351cb6ull, 0xAAAA000200000002ull},
        {0x905809a4c28d8a45ull, 0xBBBB000200000002ull},
        {0x51eea86bf6997e4eull, 0xCCCC000200000002ull},
    };
    for (int i = 0; i < 5; i++) {
        g_lookup_hashes[g_lookup_count] = extra_pairs[i][0];
        g_lookup_recs[g_lookup_count].pkg0 = extra_pairs[i][1];
        g_lookup_recs[g_lookup_count].pkg1 = 0ULL;
        ++g_lookup_count;
    }

    /* Package system (cap=256) */
    memset(g_ref_slots, 0, sizeof g_ref_slots);
    p = g_ref_slots; uint32_t pcap = 256u; uint64_t empty64 = 0ULL;
    memcpy(g_pkg_sys_mem + 0, &p, 8);
    memcpy(g_pkg_sys_mem + 8, &pcap, 4);
    memcpy(g_pkg_sys_mem + 16, &empty64, 8);
    p = g_pkg_sys_mem; memcpy(g_arena_game + CHAOS_PKG_SYSTEM_RVA, &p, 8);

    /* Exe package list */
    g_exe_pkg_count = 0;
    memset(g_exe_engine_mem, 0, sizeof g_exe_engine_mem);
    memset(g_exe_mgr_mem, 0, sizeof g_exe_mgr_mem);
    memset(g_exe_res_mem, 0, sizeof g_exe_res_mem);
    p = g_exe_engine_mem; memcpy(g_arena_exe + CHAOS_EXE_ENGINE_RVA, &p, 8);
    p = g_exe_mgr_mem; memcpy(g_exe_engine_mem + 0x400u, &p, 8);
    p = g_exe_res_mem; memcpy(g_exe_mgr_mem + 0x208u, &p, 8);
    p = g_exe_pkg_ptrs; memcpy(g_exe_res_mem + 0x88u, &p, 8);

    /* EncounterManager */
    float spacing = 4.5f; uint32_t qtr = 0u;
    memcpy(g_em_mem + 0x918u, &qtr, 4);
    memcpy(g_em_mem + 0x91Cu, &spacing, 4);
    p = g_em_mem; memcpy(g_arena_game + CHAOS_ENCOUNTER_RVA, &p, 8);

    /* FX queue & invalid sentinel */
    p = g_exp_queue_mem; memcpy(g_arena_game + CHAOS_FX_QUEUE_RVA, &p, 8);
    uint32_t inv = 0xFFFFFFFFu;
    memcpy(g_arena_game + CHAOS_INVALID_SPAWN_RVA, &inv, 4);

    /* 1 living player entity 101 at (100, 200, 10) */
    uint32_t pcount = 1u, plife = 3u, pnet = 1u, pslot = 0u, pavatar = 101u;
    memset(g_em_net_buckets, 0xFF, sizeof g_em_net_buckets);
    memset(g_tm_buckets, 0xFF, sizeof g_tm_buckets);
    p = g_pm_mem; memcpy(g_arena_game + CHAOS_PLAYERS_RVA, &p, 8);
    memcpy(g_pm_mem + 0x84u, &pcount, 4);
    memcpy(g_pm_mem + 0x2E0u, &plife, 4);
    memcpy(g_pm_mem + 0x3A8u, &pnet, 4);
    p = g_emgr_mem; memcpy(g_arena_game + CHAOS_ENTITIES_RVA, &p, 8);
    void* nbptr = g_em_net_buckets;
    g_em_net_buckets[pnet % 32u].k = pnet; g_em_net_buckets[pnet % 32u].v = pslot;
    memcpy(g_emgr_mem + 0xF22EC8u + 0, &nbptr, 8);
    memcpy(g_emgr_mem + 0xF22EC8u + 8, &cap32, 4);
    memcpy(g_emgr_mem + 0xF22EC8u + 12, &empty32, 4);
    memcpy(g_emgr_mem + 0xF22EC8u + 16, &mult32, 4);
    memcpy(g_emgr_mem + 0xF32F20u, &pavatar, 4);

    p = g_tm_mem; memcpy(g_arena_game + CHAOS_TRANSFORM_RVA, &p, 8);
    void* tbptr = g_tm_buckets; void* tdptr = g_tm_data;
    g_tm_buckets[pavatar % 32u].k = pavatar; g_tm_buckets[pavatar % 32u].v = 0u;
    memcpy(g_tm_mem + 0x40u + 0, &tbptr, 8);
    memcpy(g_tm_mem + 0x40u + 8, &cap32, 4);
    memcpy(g_tm_mem + 0x40u + 12, &empty32, 4);
    memcpy(g_tm_mem + 0x40u + 16, &mult32, 4);
    memcpy(g_tm_mem + 0x68u, &tdptr, 8);
    float ppos[3] = {100.0f, 200.0f, 10.0f};
    memcpy(g_tm_data[0] + 0x2E0u, ppos, 12);

    /* Wire mock callbacks */
    memset(&g_mock, 0, sizeof g_mock);
    g_mock.game_base = (uintptr_t)g_arena_game;
    g_mock.game_size = kArenaGameSize;
    g_mock.exe_base = (uintptr_t)g_arena_exe;
    g_mock.exe_size = kArenaExeSize;
    g_mock.lookup_entity_pkgs = MockLookupEntityPkgs;
    g_mock.pkg_request = MockPkgRequest;
    g_mock.pkgs_idle = MockPkgsIdle;
    g_mock.has_headroom = MockHasHeadroom;
    g_mock.project_anchor = MockProjectAnchor;
    g_mock.spawn_transport = MockSpawnTransport;
    g_mock.spawn_roster_row = MockSpawnRosterRow;
    g_mock.particle_fx = MockParticleFx;
    g_mock.breach_burst = MockBreachBurst;
    g_mock.alive_override = MockAlive;
}

static void ResetMockCountersAndModState(void)
{
    g_pins_checked = g_pins_ok = g_exe_pins_ok = 0;
    g_native_faction = -1;
    memset(g_rosters_loaded, 0, sizeof g_rosters_loaded);
    memset(g_preload_pkgs, 0, sizeof g_preload_pkgs);
    memset(g_preload_count, 0, sizeof g_preload_count);
    memset(g_preload_requested, 0, sizeof g_preload_requested);
    memset(g_preload_built, 0, sizeof g_preload_built);
    memset(g_faction_resident, 0, sizeof g_faction_resident);
    g_bot_smoke_resident = 0;
    g_mock_smoke_state = 0;
    g_mock_explosion_calls = 0;
    g_mock_explosion_type = 0;
    g_transport_calls = g_roster_calls = g_fx_calls = g_burst_calls = 0;
    g_last_ship = 0;
    g_scoped_director_faction_during_transport = 0;
    g_scoped_director_roster_during_transport = 0;
    memset(g_last_transport_pos, 0, sizeof g_last_transport_pos);
    memset(g_last_transport_drop, 0, sizeof g_last_transport_drop);
    memset(g_last_transport_target, 0, sizeof g_last_transport_target);
    g_fire_sync_transport_onspawn = 1;
    g_force_reject_last_role = 0;
    g_mock_idle = 1;
    g_mock_headroom = 1;
    g_mock_alive = 1;
    g_force_anchor_z_bad = 0;
    skipped_logs = fallback_logs = landing_logs = preload_ready_logs = 0;
    spawn_logs = spawn_unmapped_logs = transport_spawn_logs = wave_complete_logs = wave_timeout_logs = 0;
    stopped = 0;
    ResetMissionState();
}

static void Seconds(int n) { for (int i = 0; i < n; i++) Tick(self, 1.0f, NULL); }

/* ---- Pure Policy, Pin, RefMap, Anchor, Tracker, Perception Unit Tests ------------------------------------------ */

static void TestPolicyAndGuards(void)
{
    ChaosConfig c = ChaosDefaults();
    ChaosConfig before = c;
    CHECK(c.enabled == 1 && c.cooldown == 150 && c.grace == 45);
    CHECK(c.total_cap == 12 && c.faction_cap == 6 && c.squad_size == 4 && c.mission_budget == 36);
    CHECK(c.bots_weight == 100 && c.squids_weight == 80 && c.bugs_weight == 100);

    const char* invalid[] = {
        "enabled=2", "total_cap=25", "squad_size=0", "bug_weight=-1", "squid_weight=201",
        "cooldown_seconds=29", "cooldown_seconds=901", "landing_grace_seconds=14", "landing_grace_seconds=301",
        "mission_budget=0", "unknown=1", "enabled=1junk", "enabled=99999999999999999999999", "enabled=", "=1"
    };
    for (unsigned i = 0; i < sizeof invalid / sizeof *invalid; i++) {
        CHECK(!ChaosParse(&c, invalid[i]));
        CHECK(!memcmp(&c, &before, sizeof c));
    }
    CHECK(ChaosParse(&c, "cooldown_seconds=30\r\n") && c.cooldown == 30);
    CHECK(ChaosParse(&c, "landing_grace_seconds=15\n") && c.grace == 15);
    CHECK(ChaosParse(&c, "cooldown_seconds=900") && c.cooldown == 900);
    CHECK(ChaosParse(&c, "landing_grace_seconds=300") && c.grace == 300);

    for (int native = 0; native < 3; native++) {
        for (int d = 1; d <= 11; d++) {
            int chosen = ChaosChoose(&c, native, d, 12345u);
            CHECK(chosen >= 0 && chosen < 3 && chosen != native);
        }
    }
    CHECK(ChaosWeight(&c, CHAOS_SQUIDS, 3) == 56 && ChaosWeight(&c, CHAOS_SQUIDS, 5) == 80 &&
          ChaosWeight(&c, CHAOS_SQUIDS, 9) == 112);

    /* Ref-map < 75% capacity guard */
    uint8_t map[128 * 16];
    uint32_t used = 0;
    int already = 0;
    memset(map, 0, sizeof map);
    for (uint32_t i = 0; i < 94; i++) {
        uint64_t k = 0x1000ULL + i;
        memcpy(map + i * 16u, &k, 8);
    }
    CHECK(ChaosRefMapHasHeadroom(map, 128u, 0ULL, 0x9999ULL, &used, &already) == 1 && used == 94u && !already);
    CHECK(ChaosRefMapHasHeadroom(map, 128u, 0ULL, 0x1005ULL, &used, &already) == 1 && already == 1);
    /* Adding 95th entry makes post-insert 96/128 == 75.0% -> must refuse */
    uint64_t k95 = 0x2000ULL;
    memcpy(map + 94u * 16u, &k95, 8);
    CHECK(ChaosRefMapHasHeadroom(map, 128u, 0ULL, 0x9999ULL, &used, &already) == 0 && used == 95u);
    CHECK(ChaosRefMapHasHeadroom(map, 128u, 0ULL, 0x1005ULL, &used, &already) == 1);

    /* Anchor geometry check: 38..62m from target player, >=28m from all living, |dz|<=25m */
    float living[2][3] = {{100.0f, 200.0f, 10.0f}, {150.0f, 240.0f, 10.0f}};
    float good[3] = {150.0f, 200.0f, 12.0f};      /* 50m from P0, 40m from P1, dz=2 */
    float too_close_all[3] = {150.0f, 215.0f, 10.0f}; /* 52.2m from P0, 25m from P1 -> fail */
    float too_far[3] = {170.0f, 200.0f, 10.0f};   /* 70m from P0 -> fail */
    float too_near[3] = {130.0f, 200.0f, 10.0f};  /* 30m from P0 -> fail */
    float too_steep[3] = {150.0f, 200.0f, 36.0f}; /* dz=26m -> fail */
    float zero_pt[3] = {0.0f, 0.0f, 0.0f};
    CHECK(ChaosAnchorValid(good, living[0], living, 2) == 1);
    CHECK(ChaosAnchorValid(too_close_all, living[0], living, 2) == 0);
    CHECK(ChaosAnchorValid(too_far, living[0], living, 2) == 0);
    CHECK(ChaosAnchorValid(too_near, living[0], living, 2) == 0);
    CHECK(ChaosAnchorValid(too_steep, living[0], living, 2) == 0);
    CHECK(ChaosAnchorValid(zero_pt, living[0], living, 2) == 0);

    /* Population tracker & reservation */
    ChaosTracker tr;
    ChaosTrackerReset(&tr);
    ChaosTrackerReserveWave(&tr, CHAOS_BOTS, 4);
    CHECK(ChaosEffectiveLiveTotal(&tr) == 4 && tr.mission_spawned == 4);
    CHECK(ChaosTrackerRecordSpawn(&tr, CHAOS_BOTS, 1001u, 1) == 1);
    CHECK(ChaosTrackerRecordSpawn(&tr, CHAOS_BOTS, 1001u, 1) == 0); /* dedupe */
    CHECK(tr.live[CHAOS_BOTS] == 1 && tr.reserved[CHAOS_BOTS] == 3 && tr.mission_spawned == 4);
    ChaosTrackerReleaseUnusedReservations(&tr, CHAOS_BOTS);
    CHECK(tr.reserved[CHAOS_BOTS] == 0 && tr.mission_spawned == 1 && tr.live[CHAOS_BOTS] == 1);

    /* Mutual hostility ignore_mask narrowing (3/4/5-way) */
    uint32_t pmask = 0x1u, mbots = 0x2u, msquids = 0x4u, mbugs = 0x8u;
    uint32_t all_e = mbots | msquids | mbugs;
    CHECK(ChaosNarrowIgnoreMask(0x1Eu, mbots, all_e, pmask) == (0x10u | mbots));   /* ignores neutral+bots, fights bugs+squids+players */
    CHECK(ChaosNarrowIgnoreMask(0x1Eu, msquids, all_e, pmask) == (0x10u | msquids));
    CHECK(ChaosNarrowIgnoreMask(0x1Eu, mbugs, all_e, pmask) == (0x10u | mbugs));

    /* Enemy classifier checks */
    CHECK(ChaosEnemyFamily(0xDB90077E76FAA025ull) == CHAOS_BOTS && ChaosIsTransportResource(0xDB90077E76FAA025ull));
    CHECK(ChaosEnemyFamily(0x74E2285C01DA4F71ull) == CHAOS_SQUIDS && ChaosIsTransportResource(0x74E2285C01DA4F71ull));
    CHECK(ChaosEnemyFamily(0x51EEA86BF6997E4Eull) == CHAOS_BUGS && !ChaosIsTransportResource(0x51EEA86BF6997E4Eull));
    CHECK(ChaosEnemyFamily(CHAOS_ENTITY_AVATAR) == -1);
}

static void TestFailClosedPinsAndResidencyGate(void)
{
    InitMockGameArenas(CHAOS_NATIVE_BUGS_BIT, 5u);
    ResetMockCountersAndModState();

    uint32_t bad_rva = 0;
    const char* bad_name = NULL;
    CHECK(ChaosVerifyCodePins(MockReadCode, (uintptr_t)g_arena_game, kArenaGameSize, &bad_rva, &bad_name) == 1);

    /* Flip 1 byte in each of the 10 pins and confirm fail-closed detection */
    for (int i = 0; i < CPIN_COUNT; i++) {
        g_arena_game[kChaosCodePins[i].rva + 7u] ^= 0xFFu;
        CHECK(ChaosVerifyCodePins(MockReadCode, (uintptr_t)g_arena_game, kArenaGameSize, &bad_rva, &bad_name) == 0);
        CHECK(bad_rva == kChaosCodePins[i].rva);
        g_arena_game[kChaosCodePins[i].rva + 7u] ^= 0xFFu;
    }

    /* Fail-closed Tick when pin fails: zero calls & zero writes */
    g_arena_game[kChaosCodePins[CPIN_SPAWN_TRANSPORT].rva] ^= 0x01u;
    role = HF_SESSION_HOST;
    game_state = HF_STATE_MISSION;
    Seconds(60);
    CHECK(!g_pins_ok && !g_transport_calls && !g_roster_calls && !g_fx_calls);

    /* Restore pin and verify preloader checks both ArePackagesIdle and parts <= 1024 / state == 4 */
    g_arena_game[kChaosCodePins[CPIN_SPAWN_TRANSPORT].rva] ^= 0x01u;
    ResetMockCountersAndModState();
    CHECK(ProveChaosRuntime() == 1);

    g_mock_idle = 0; /* Packages still streaming */
    PreloadForeignTick((uintptr_t)g_arena_game, (uintptr_t)g_arena_exe, kArenaExeSize, 1.0f);
    CHECK(!g_faction_resident[CHAOS_BOTS] && !g_faction_resident[CHAOS_SQUIDS] && preload_ready_logs == 0u);

    g_mock_idle = 1;
    PreloadForeignTick((uintptr_t)g_arena_game, (uintptr_t)g_arena_exe, kArenaExeSize, 1.0f);
    CHECK(g_faction_resident[CHAOS_BOTS] && g_faction_resident[CHAOS_SQUIDS] && !g_preload_built[CHAOS_BUGS] &&
          preload_ready_logs == 2u);

    /* Unrelated engine streaming flips idle=0, then recovers idle=1 -> latched residency stays 1 and logs once */
    g_mock_idle = 0;
    PreloadForeignTick((uintptr_t)g_arena_game, (uintptr_t)g_arena_exe, kArenaExeSize, 1.0f);
    CHECK(g_faction_resident[CHAOS_BOTS] && g_faction_resident[CHAOS_SQUIDS] && preload_ready_logs == 2u);
    g_mock_idle = 1;
    PreloadForeignTick((uintptr_t)g_arena_game, (uintptr_t)g_arena_exe, kArenaExeSize, 1.0f);
    CHECK(g_faction_resident[CHAOS_BOTS] && g_faction_resident[CHAOS_SQUIDS] && preload_ready_logs == 2u);

    /* Clear mission state -> verify parts == 1025 and state != 4 fail residency even when idle=1 */
    ResetMissionState();
    AddExeResidentPkg(kBaseFactionPackages[CHAOS_BOTS][0], 1025u, 4u);
    PreloadForeignTick((uintptr_t)g_arena_game, (uintptr_t)g_arena_exe, kArenaExeSize, 1.0f);
    CHECK(!g_faction_resident[CHAOS_BOTS] && g_faction_resident[CHAOS_SQUIDS]);

    ResetMissionState();
    AddExeResidentPkg(kBaseFactionPackages[CHAOS_BOTS][0], 4u, 3u);
    PreloadForeignTick((uintptr_t)g_arena_game, (uintptr_t)g_arena_exe, kArenaExeSize, 1.0f);
    CHECK(!g_faction_resident[CHAOS_BOTS]);
}

static void TestSpawnersFallbacksAndJoiner(void)
{
    /* Native = Bugs -> test Joiner preloads only, no spawns; then Host Bots Dropship + 22s fallback */
    InitMockGameArenas(CHAOS_NATIVE_BUGS_BIT, 5u);
    ResetMockCountersAndModState();
    config = ChaosDefaults();
    config.grace = 15;
    config.cooldown = 40;
    config.squids_weight = 0; /* force Bots only */
    config.bots_weight = 100;
    Publish();
    ChaosReset(&clock_state, &config);

    role = HF_SESSION_JOINER;
    game_state = HF_STATE_MISSION;
    Seconds(20);
    CHECK(g_faction_resident[CHAOS_BOTS] && g_faction_resident[CHAOS_SQUIDS]); /* peer preloaded */
    CHECK(g_transport_calls == 0 && g_roster_calls == 0 && g_fx_calls == 0); /* never spawns as joiner */

    /* Switch to Host -> resets mission state, waits for living player -> latches landed on game thread without HF_ON_PLAYER_SPAWN */
    role = HF_SESSION_HOST;
    g_mock_alive = 0;
    Seconds(2);
    CHECK(!landed && landing_logs == 0u);
    g_mock_alive = 1;
    g_fire_sync_transport_onspawn = 1;
    g_force_reject_last_role = 1; /* simulate SelectRosterIndex returning -1 on 4th slot -> Verify repair */
    Seconds(1);
    CHECK(landed == 1u && landing_logs == 1u && g_transport_calls == 0);
    Seconds(13);
    CHECK(g_transport_calls == 0);
    Seconds(1);
    CHECK(landing_logs == 1u && g_transport_calls == 1 && g_last_ship == CHAOS_ENTITY_DROPSHIP);
    CHECK(g_scoped_director_faction_during_transport == CHAOS_NATIVE_BOTS_BIT);
    CHECK(g_scoped_director_roster_during_transport == (uint64_t)(uintptr_t)(g_arena_game + CHAOS_ROSTER_BOTS_RVA));
    CHECK(ReadU32((uintptr_t)g_director_mem + 0x518BCu, 0u) == CHAOS_NATIVE_BUGS_BIT); /* restored immediately */
    uint64_t restored_roster = 0;
    memcpy(&restored_roster, g_director_mem + 0x660u, 8);
    CHECK(restored_roster == (uint64_t)(uintptr_t)(g_arena_game + CHAOS_ROSTER_BUGS_RVA));
    /* Verify arg7 = anchor (drop_pos) and arg8 = player_pos (target_pos) */
    CHECK(memcmp(g_last_transport_drop, g_last_transport_pos, 12) == 0);
    CHECK(fabsf(g_last_transport_target[0] - 100.0f) < 0.01f &&
          fabsf(g_last_transport_target[1] - 200.0f) < 0.01f &&
          fabsf(g_last_transport_target[2] - 10.0f) < 0.01f);
    CHECK(g_mock_explosion_calls == 1 && g_mock_explosion_type == CHAOS_EXPLOSION_G3_SMOKE);
    /* Verify PassengerComponent cargo slot 0 was patched to faction=3 (Bots) & rejected slot repaired */
    uint32_t pass_fac = 0, pass_cnt = 0;
    memcpy(&pass_fac, g_pass_cargo_mem + 0x00u, 4);
    memcpy(&pass_cnt, g_pass_cargo_mem + 0x04u, 4);
    CHECK(pass_fac == 3u && pass_cnt == 4u && g_pending.cargo_patched == 1);
    for (uint32_t k = 0; k < 4u; k++) {
        uint32_t cr = 0, sr = 0;
        memcpy(&cr, g_pass_cargo_mem + 0x08u + (size_t)k * 4u, 4);
        memcpy(&sr, g_pass_seats_mem + 0x80u + (size_t)k * 4u, 4);
        CHECK(cr <= 20u && sr == cr);
    }

    /* Advance 22s with 0 unloaded troops -> triggers 22s fallback to SpawnRosterRow(0x3C) */
    HfSpawnEvent ship_ev = {0};
    ship_ev.info.size = sizeof ship_ev;
    ship_ev.entity = 777u; ship_ev.resource = CHAOS_ENTITY_DROPSHIP; ship_ev.authority = 1;
    OnSpawn(self, (const HfEventInfo*)&ship_ev, NULL); /* transport hull must NOT count as unloaded troop! */
    CHECK(g_pending.ship_seen == 2 && g_pending.unloaded_count == 0 && transport_spawn_logs == 2u);
    Seconds(21);
    CHECK(g_roster_calls == 0);
    Seconds(1);
    CHECK(g_roster_calls == 4 && g_last_roster_flags == 0x3Cu && g_last_roster_kind == 3u && fallback_logs == 1u);
    /* Wait 8s after fallback with 0 spawns -> timeout closes wave and releases reservations */
    Seconds(8);
    CHECK(!g_pending.active && wave_timeout_logs == 1u && g_tracker.mission_spawned == 0);

    /* Squid scoped director=4 + roster=Squids + immediate restore + non-sync OnSpawn cargo patch + unload logs */
    InitMockGameArenas(CHAOS_NATIVE_BUGS_BIT, 5u);
    ResetMockCountersAndModState();
    g_fire_sync_transport_onspawn = 0; /* exercise post-call PassengerComponent prev_active fallback */
    config = ChaosDefaults(); config.grace = 15; config.cooldown = 30;
    config.bots_weight = 0; config.squids_weight = 100;
    Publish();
    ChaosReset(&clock_state, &config);
    role = HF_SESSION_HOST;
    Seconds(15);
    CHECK(landed == 1u && landing_logs == 1u);
    CHECK(g_transport_calls == 1 && g_last_ship == CHAOS_ENTITY_WARPSHIP);
    CHECK(g_scoped_director_faction_during_transport == CHAOS_NATIVE_SQUIDS_BIT);
    CHECK(g_scoped_director_roster_during_transport == (uint64_t)(uintptr_t)(g_arena_game + CHAOS_ROSTER_SQUIDS_RVA));
    CHECK(ReadU32((uintptr_t)g_director_mem + 0x518BCu, 0u) == CHAOS_NATIVE_BUGS_BIT);
    memcpy(&restored_roster, g_director_mem + 0x660u, 8);
    CHECK(restored_roster == (uint64_t)(uintptr_t)(g_arena_game + CHAOS_ROSTER_BUGS_RVA));
    CHECK(memcmp(g_last_transport_drop, g_last_transport_pos, 12) == 0);
    CHECK(fabsf(g_last_transport_target[0] - 100.0f) < 0.01f &&
          fabsf(g_last_transport_target[1] - 200.0f) < 0.01f);
    memcpy(&pass_fac, g_pass_cargo_mem + 0x00u, 4);
    memcpy(&pass_cnt, g_pass_cargo_mem + 0x04u, 4);
    CHECK(pass_fac == 4u && pass_cnt == 4u && g_pending.cargo_patched == 1);
    for (uint32_t k = 0; k < 4u; k++) {
        uint32_t cr = 0;
        memcpy(&cr, g_pass_cargo_mem + 0x08u + (size_t)k * 4u, 4);
        CHECK(cr <= 15u);
    }

    /* Exercise OnSpawn diagnostics while active: unmapped (auth=1 & auth=0), native ignored, then 4 Squid troops complete wave */
    HfSpawnEvent bad_ev = {0};
    bad_ev.info.size = sizeof bad_ev;
    bad_ev.entity = 9001u; bad_ev.resource = 0x1122334455667788ULL; bad_ev.authority = 1;
    OnSpawn(self, (const HfEventInfo*)&bad_ev, NULL);
    bad_ev.entity = 9002u; bad_ev.resource = 0x905809a4c28d8a45ULL; bad_ev.authority = 0;
    OnSpawn(self, (const HfEventInfo*)&bad_ev, NULL);
    bad_ev.entity = 9003u; bad_ev.resource = 0x51eea86bf6997e4eULL; bad_ev.authority = 1; /* native Bug ignored */
    OnSpawn(self, (const HfEventInfo*)&bad_ev, NULL);
    CHECK(spawn_unmapped_logs == 2u && spawn_logs == 0u && g_pending.unloaded_count == 0);

    for (uint32_t s = 0; s < 4u; s++) {
        HfSpawnEvent tr_ev = {0};
        tr_ev.info.size = sizeof tr_ev;
        tr_ev.entity = 1200u + s;
        tr_ev.resource = (s == 3u) ? g_rosters[CHAOS_SQUIDS][0].entity_hash : 0x905809a4c28d8a45ULL;
        tr_ev.authority = 1;
        OnSpawn(self, (const HfEventInfo*)&tr_ev, NULL);
    }
    CHECK(spawn_logs == 4u && g_pending.unloaded_count == 4 && g_tracker.live[CHAOS_SQUIDS] == 4);
    Seconds(1);
    CHECK(!g_pending.active && wave_complete_logs == 1u && wave_timeout_logs == 0u && fallback_logs == 0u);

    /* Late foreign spawn logs mode=late cleanly without active wave */
    HfSpawnEvent late_ev = {0};
    late_ev.info.size = sizeof late_ev;
    late_ev.entity = 1300u; late_ev.resource = 0x4019623142351cb6ULL; late_ev.authority = 1;
    OnSpawn(self, (const HfEventInfo*)&late_ev, NULL);
    CHECK(spawn_logs == 5u && g_tracker.live[CHAOS_BOTS] == 1 && g_tracker.mission_spawned == 5);

    /* Leaving mission to ship resets landed and latched residency safely on game thread */
    game_state = HF_STATE_SHIP;
    Seconds(1);
    CHECK(!landed && !g_faction_resident[CHAOS_BOTS] && !g_faction_resident[CHAOS_SQUIDS]);
    game_state = HF_STATE_MISSION;

    /* Bug breach on Automaton planet -> TriggerParticleFx(0x7B) + TriggerBreachBurst(0x7B) + SpawnRosterRow(0x24, kind=3, ~3.5s) */
    InitMockGameArenas(CHAOS_NATIVE_BOTS_BIT, 5u);
    ResetMockCountersAndModState();
    config = ChaosDefaults(); config.grace = 15; config.cooldown = 30;
    config.squids_weight = 0; config.bugs_weight = 100;
    Publish();
    ChaosReset(&clock_state, &config);
    role = HF_SESSION_HOST;
    Seconds(15);
    CHECK(g_transport_calls == 0 && g_fx_calls == 1 && g_burst_calls == 1);
    CHECK(g_roster_calls == 4 && g_last_roster_flags == 0x24u && g_last_roster_kind == 3u);
    CHECK(g_last_roster_delays[0] == 0.0f && fabsf(g_last_roster_delays[3] - 3.5f) < 0.01f);

    /* Busy transport queue (q >= 8) falls back immediately to SpawnRosterRow(0x3C) */
    InitMockGameArenas(CHAOS_NATIVE_BUGS_BIT, 5u);
    ResetMockCountersAndModState();
    config = ChaosDefaults(); config.grace = 15; config.cooldown = 30;
    config.bots_weight = 100; config.squids_weight = 0;
    Publish();
    ChaosReset(&clock_state, &config);
    uint32_t busy_q = 8u;
    memcpy(g_em_mem + 0x918u, &busy_q, 4);
    role = HF_SESSION_HOST;
    Seconds(15);
    CHECK(g_transport_calls == 0 && g_roster_calls == 4 && g_last_roster_flags == 0x3Cu && g_pending.used_fallback == 1);
}

static void TestPerceptionMultiWay(void)
{
    InitMockGameArenas(CHAOS_NATIVE_BUGS_BIT, 5u);
    ResetMockCountersAndModState();

    /* Setup BHV_FACTIONS: 0=Player, 1=Sentry(friendly), 2=Bug, 3=Bot, 4=Squid */
    void* p = g_fac_mgr_mem; memcpy(g_arena_game + CHAOS_FACTIONS_RVA, &p, 8);
    p = g_perc_mgr_mem; memcpy(g_arena_game + CHAOS_PERCEPTION_RVA, &p, 8);

    uint32_t fcount = 5u;
    uint32_t cap = 32u, empty = 0xFFFFFFFFu, mult = 1u;
    void* fdescs = g_fac_desc_ptrs;
    void* fmasks = g_fac_masks;
    void* fbptr = g_fac_buckets;
    memset(g_fac_buckets, 0xFF, sizeof g_fac_buckets);
    memcpy(g_fac_mgr_mem + 0x12040u, &fcount, 4);
    memcpy(g_fac_mgr_mem + 0x12050u + 0, &fbptr, 8);
    memcpy(g_fac_mgr_mem + 0x12050u + 8, &cap, 4);
    memcpy(g_fac_mgr_mem + 0x12050u + 12, &empty, 4);
    memcpy(g_fac_mgr_mem + 0x12050u + 16, &mult, 4);
    memcpy(g_fac_mgr_mem + 0x12068u, &fdescs, 8);
    memcpy(g_fac_mgr_mem + 0x12078u, &fmasks, 8);

    uint64_t resources[5] = {
        CHAOS_ENTITY_AVATAR,
        0x1111222233334444ull, /* Sentry / SEAF */
        0x51eea86bf6997e4eull, /* Scavenger (Bug) */
        0x4019623142351cb6ull, /* Trooper (Bot) */
        0x905809a4c28d8a45ull  /* Voteless (Squid) */
    };
    uint32_t entities[5] = {101u, 102u, 201u, 301u, 401u};
    uint32_t fmask_vals[5] = {0x1u, 0x1u, 0x2u, 0x4u, 0x8u};
    uint32_t orig_perc_ignores[5] = {0x1u, 0x1u, 0x1Eu, 0x1Eu, 0x1Eu}; /* Enemies ignore 0x2|0x4|0x8|0x10 */

    for (uint32_t i = 0; i < 5; i++) {
        memset(&g_fac_descs_storage[i], 0, sizeof g_fac_descs_storage[i]);
        g_fac_descs_storage[i].resource = resources[i];
        g_fac_descs_storage[i].entity = entities[i];
        g_fac_desc_ptrs[i] = &g_fac_descs_storage[i];
        g_fac_masks[i] = fmask_vals[i];
        uint32_t slot = entities[i] % 32u;
        g_fac_buckets[slot].k = entities[i];
        g_fac_buckets[slot].v = i;

        g_perc_descs_storage[i] = g_fac_descs_storage[i];
        g_perc_desc_ptrs[i] = &g_perc_descs_storage[i];
        uint8_t* rec = g_perc_records_mem + (size_t)i * CHAOS_PERCEPTION_STRIDE;
        uint8_t active = 1u;
        memcpy(rec + 0x13E8u, &orig_perc_ignores[i], 4);
        memcpy(rec + 0x13F0u, &active, 1);
    }

    void* pdescs = g_perc_desc_ptrs;
    void* precs = g_perc_records_mem;
    memcpy(g_perc_mgr_mem + 0x20u, &fcount, 4);
    memcpy(g_perc_mgr_mem + 0x48u, &pdescs, 8);
    memcpy(g_perc_mgr_mem + 0x50u, &precs, 8);

    UpdatePerceptionHostility((uintptr_t)g_arena_game);

    /* Verify BHV_FACTIONS completely untouched */
    for (int i = 0; i < 5; i++) CHECK(g_fac_masks[i] == fmask_vals[i]);
    /* Verify Player & Sentry/SEAF perceivers untouched */
    CHECK(*(uint32_t*)(g_perc_records_mem + 0u * CHAOS_PERCEPTION_STRIDE + 0x13E8u) == 0x1u);
    CHECK(*(uint32_t*)(g_perc_records_mem + 1u * CHAOS_PERCEPTION_STRIDE + 0x13E8u) == 0x1u);
    /* Verify Bug (0x2), Bot (0x4), Squid (0x8) keep own faction bit + neutral 0x10, clear other enemy bits */
    CHECK(*(uint32_t*)(g_perc_records_mem + 2u * CHAOS_PERCEPTION_STRIDE + 0x13E8u) == 0x12u);
    CHECK(*(uint32_t*)(g_perc_records_mem + 3u * CHAOS_PERCEPTION_STRIDE + 0x13E8u) == 0x14u);
    CHECK(*(uint32_t*)(g_perc_records_mem + 4u * CHAOS_PERCEPTION_STRIDE + 0x13E8u) == 0x18u);
}

int main(int argc, char** argv)
{
    CHECK(argc == 2 && strlen(argv[1]) < sizeof fixture);
    strcpy(fixture, argv[1]);
    CHECK(SetEnvironmentVariableA("LOCALAPPDATA", fixture));

    TestPolicyAndGuards();

    InitMockGameArenas(CHAOS_NATIVE_BUGS_BIT, 5u);
    ResetMockCountersAndModState();

    HfApi api = {0};
    api.version = 16;
    api.size = sizeof api;
    api.build_id = Build;
    api.mod_folder = Folder;
    api.session = Session;
    api.game_state = GameState;
    api.log = Log;
    api.set_status = Status;
    api.subscribe = Subscribe;
    api.on_game_thread = OnTick;
    api.on = OnEvent;
    api.read = MockRead;
    api.read_code = MockReadCode;
    api.write = MockWrite;
    api.module_base = MockModuleBase;
    api.load_package = MockLoadPackage;
    api.spawn_explosion = MockSpawnExplosion;

    CHECK(hellforge_mod_init(NULL, (HfMod*)1) == 1);
    api.version = 15; CHECK(hellforge_mod_init(&api, (HfMod*)1) == 1); api.version = 16;
    api.size = offsetof(HfApi, set_status); CHECK(hellforge_mod_init(&api, (HfMod*)1) == 1); api.size = sizeof api;
    fail_subscription = 1; CHECK(hellforge_mod_init(&api, (HfMod*)1) == 1 && !ticks);
    subscriptions = fail_subscription = 0;
    CHECK(hellforge_mod_init(&api, (HfMod*)1) == 0 && subscriptions == 1 && ticks == 1);

    TestFailClosedPinsAndResidencyGate();
    TestSpawnersFallbacksAndJoiner();
    TestPerceptionMultiWay();

    /* Kill switch latch & token mismatch tests */
    FILE* f = fopen(bridge, "wb"); CHECK(f != NULL);
    fprintf(f, "session=%lu\nenabled=0\n", token + 1);
    fclose(f);
    Seconds(1);
    CHECK(config.enabled == 1); /* wrong PID token ignored */

    f = fopen(stop_file, "wb"); CHECK(f != NULL); fclose(f);
    Seconds(1);
    CHECK(stopped == 1);
    CHECK(DeleteFileA(stop_file));
    Seconds(5);
    CHECK(stopped == 1); /* latched until session restart */

    printf("PASS %u checks; 0.2.0 policy, %u code pins, preloader, Bot/Squid/Bug spawners+fallback, PassengerComponent, and 3/4/5-way perception verified\n",
           checks, (unsigned)CPIN_COUNT);
    return 0;
}
