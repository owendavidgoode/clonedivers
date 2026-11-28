#include "../src/chaos_invasions.c"
#include <stdarg.h>

static unsigned checks, subscriptions, ticks, fail_subscription, skipped_logs, fallback_logs, landing_logs, preload_ready_logs;
static unsigned preload_deferred_logs, g_pkg_request_calls, g_pkg_requested_items, g_lookup_pkg_calls;
static unsigned g_pkg_release_calls, g_pkg_released_items, g_pkg_double_free_errors;
static unsigned g_remove_entity_calls, g_req_entity_spawn_calls, g_activate_beacon_calls;
static uint32_t g_last_removed_entity, g_last_spawned_entity, g_last_beacon_type;
static uint64_t g_last_spawned_resource;
static unsigned spawn_logs, spawn_unmapped_logs, transport_spawn_logs, wave_complete_logs, wave_timeout_logs;
static unsigned peer_observe_logs, host_migration_logs, peer_leave_logs;
static unsigned role = HF_SESSION_SOLO, game_state = HF_STATE_MISSION;
static char fixture[1800];
static uint32_t g_mock_dead_entities[32];
static int g_mock_dead_count = 0;
static int g_mock_auth_deny_entity = 0;
static uint8_t g_mock_local_user_mem[0xC000];
static uint64_t g_mock_last_send_to = 0;
static uint32_t g_mock_send_calls = 0;
static uint8_t g_mock_last_send_buf[256];
static uint32_t g_mock_last_send_bytes = 0;
static int g_mock_share_has_row = 0;
static uint64_t g_mock_share_key = 0;
static uint8_t g_mock_share_val[64];
static uint32_t g_mock_share_bytes = 0;

#define CHECK(x) do { ++checks; if (!(x)) { printf("FAIL line %d: %s\n", __LINE__, #x); exit(1); } } while (0)

/* ---- Mocked SDK & Synthetic World Fixtures --------------------------------------------------------------------- */

static int g_mock_fail_em_bulk_read = 0;
static int g_mock_fail_write_countdown = -1;
static uintptr_t g_mock_corrupt_readback_addr = 0u;
static int g_mock_corrupt_readback_once = 0;
static int g_mock_fail_all_writes_after_readback = 0;
static int g_mock_writing_active = 0;
static unsigned g_mock_write_calls_seen = 0;
static char g_mock_logs_dir[1700] = {0};
static HfEventCallback g_mock_on_game_loaded = NULL;
static HfEventCallback g_mock_on_entities_ready = NULL;

static int MockRead(uintptr_t address, void* buffer, size_t size)
{
    if (!address || !buffer) return 1;
    if (g_mock_fail_em_bulk_read &&
        (size == UK_EM_DESCRIPTOR_CAP * UK_EM_DESCRIPTOR_STRIDE ||
         size == UK_EM_MAX_DESCRIPTOR_CAP * UK_EM_DESCRIPTOR_STRIDE)) return 1;
    memcpy(buffer, (const void*)address, size);
    if (g_mock_corrupt_readback_once == 1 && address == g_mock_corrupt_readback_addr && size > 0u) {
        ((uint8_t*)buffer)[0] ^= 0xFFu;
        g_mock_corrupt_readback_once = 0;
        if (g_mock_fail_all_writes_after_readback) g_mock_writing_active = -1;
    }
    return 0;
}

static int MockReadCode(uintptr_t address, void* buffer, size_t size)
{
    return MockRead(address, buffer, size);
}

static int MockWrite(uintptr_t address, const void* data, size_t size)
{
    if (!address || !data) return 1;
    ++g_mock_write_calls_seen;
    if (g_mock_writing_active < 0) return 1;
    if (g_mock_fail_write_countdown >= 0) {
        if (g_mock_fail_write_countdown == 0) {
            g_mock_fail_write_countdown = -1;
            return 1;
        }
        --g_mock_fail_write_countdown;
    }
    memcpy((void*)address, data, size);
    if (g_mock_corrupt_readback_addr && address == g_mock_corrupt_readback_addr && g_mock_corrupt_readback_once == 2) {
        /* Arm one-shot readback mismatch strictly AFTER bytes were mutated in memory! */
        g_mock_corrupt_readback_once = 1;
    }
    return 0;
}

static const char* MockLogsFolder(HfMod* m)
{
    (void)m;
    return g_mock_logs_dir[0] ? g_mock_logs_dir : NULL;
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
    if (strstr(buf, "CHAOS_PRELOAD_DEFERRED")) ++preload_deferred_logs;
    if (strstr(buf, "CHAOS_SPAWN:")) ++spawn_logs;
    if (strstr(buf, "CHAOS_SPAWN_UNMAPPED:")) ++spawn_unmapped_logs;
    if (strstr(buf, "CHAOS_TRANSPORT_SPAWN:")) ++transport_spawn_logs;
    if (strstr(buf, "CHAOS_WAVE_COMPLETE:") && strstr(buf, "result=complete")) ++wave_complete_logs;
    if (strstr(buf, "CHAOS_WAVE_COMPLETE:") && strstr(buf, "result=timeout")) ++wave_timeout_logs;
    if (strstr(buf, "CHAOS_PEER_OBSERVE:")) ++peer_observe_logs;
    if (strstr(buf, "CHAOS_HOST_MIGRATION:")) ++host_migration_logs;
    if (strstr(buf, "CHAOS_PEER_LEAVE:")) ++peer_leave_logs;
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
    (void)m; (void)u;
    if (e == HF_EVENT_GAME_LOADED) g_mock_on_game_loaded = cb;
    else if (e == HF_EVENT_ENTITIES_READY) g_mock_on_entities_ready = cb;
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
static const size_t kArenaGameSize = 0x4800000u;
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
    ++g_lookup_pkg_calls;
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
    ++g_pkg_request_calls;
    g_pkg_requested_items += count;
    for (uint32_t k = 0; k < count; k++) {
        uint64_t pkg = ids[k];
        int found = 0;
        uint64_t new_ref = 0ULL;
        for (int i = 0; i < 256; i++) {
            if (g_ref_slots[i].key == pkg) {
                new_ref = ++g_ref_slots[i].ref;
                found = 1;
                break;
            }
        }
        if (!found) {
            for (int i = 0; i < 256; i++) {
                if (g_ref_slots[i].key == 0ULL) {
                    g_ref_slots[i].key = pkg;
                    g_ref_slots[i].ref = 1ULL;
                    new_ref = 1ULL;
                    break;
                }
            }
        }
        /* Match RefcountedPackageRequest (game.dll+0x12fe47f): PackageManager->Load() runs ONLY when ref == 1! */
        if (new_ref == 1ULL) {
            AddExeResidentPkg(pkg, 2u, 4u);
        }
    }
}

static void MockPkgRelease(void* sys, const uint64_t* ids, uint32_t count)
{
    (void)sys;
    ++g_pkg_release_calls;
    g_pkg_released_items += count;
    for (uint32_t k = 0; k < count; k++) {
        uint64_t pkg = ids[k];
        int found = 0;
        for (int i = 0; i < 256; i++) {
            if (g_ref_slots[i].key == pkg) {
                found = 1;
                if (g_ref_slots[i].ref == 0ULL) {
                    ++g_pkg_double_free_errors;
                } else {
                    --g_ref_slots[i].ref;
                    if (g_ref_slots[i].ref == 0ULL) {
                        AddExeResidentPkg(pkg, 2u, 0u); /* Unloaded when ref_count reaches 0 */
                    }
                }
                break;
            }
        }
        if (!found) {
            ++g_pkg_double_free_errors;
        }
    }
}

static void MockRemoveEntity(void* em_root, uint32_t entity)
{
    ++g_remove_entity_calls;
    g_last_removed_entity = entity;
    if (g_mock_dead_count < 32) {
        g_mock_dead_entities[g_mock_dead_count++] = entity;
    }
    if (em_root) {
        for (uint32_t i = 0; i < 2048u; ++i) {
            uint8_t* slot = (uint8_t*)em_root + UK_EM_DESCRIPTORS_OFF + (size_t)i * 24u;
            uint32_t eid = 0u;
            memcpy(&eid, slot + 8u, 4u);
            if (eid == entity) {
                memset(slot, 0, 24u);
                break;
            }
        }
    }
}

static uint32_t* MockRequestEntitySpawn(uint32_t* out_entity, uint64_t resource, void* creation)
{
    ++g_req_entity_spawn_calls;
    g_last_spawned_resource = resource;
    uint32_t eid = 44000u + (uint32_t)g_req_entity_spawn_calls;
    g_last_spawned_entity = eid;
    if (out_entity) *out_entity = eid;
    if (creation) {
        float pos[3] = {0.0f, 0.0f, 0.0f};
        memcpy(pos, (const uint8_t*)creation + 8u + 48u, 12u);
        uint32_t tslot = eid % 32u;
        if (tslot != 0u) {
            g_tm_buckets[tslot].k = eid;
            g_tm_buckets[tslot].v = tslot;
            memcpy(g_tm_data[tslot] + 0x2E0u, pos, 12u);
        }
    }
    return out_entity;
}

static int MockActivateBeacon(uint32_t strat_type, const float pos[3])
{
    (void)pos;
    ++g_activate_beacon_calls;
    g_last_beacon_type = strat_type;
    return 1;
}

static uint8_t MockPkgsIdle(void* unused) { (void)unused; return g_mock_idle; }
static uint8_t MockHasHeadroom(void* d, uint32_t k, uint8_t b) { (void)d; (void)k; (void)b; return (uint8_t)g_mock_headroom; }
static int MockAlive(uint32_t e)
{
    if (!g_mock_alive) return 0;
    for (int i = 0; i < g_mock_dead_count; i++) {
        if (g_mock_dead_entities[i] == e) return 0;
    }
    return 1;
}

static int MockIsAuthority(HfMod* m, uint32_t entity)
{
    (void)m;
    if (g_mock_auth_deny_entity && entity == (uint32_t)g_mock_auth_deny_entity) return 0;
    return Host() ? 1 : 0;
}

static int MockSend(HfMod* m, uint64_t to, const void* data, uint32_t bytes)
{
    (void)m;
    if (!data || bytes > sizeof g_mock_last_send_buf) return 1;
    ++g_mock_send_calls;
    g_mock_last_send_to = to;
    g_mock_last_send_bytes = bytes;
    memcpy(g_mock_last_send_buf, data, bytes);
    return 0;
}

static int MockShareSet(HfMod* m, uint64_t key, const void* value, uint32_t bytes)
{
    (void)m;
    if (!Host() || bytes > sizeof g_mock_share_val) return 1;
    if (bytes == 0u) {
        g_mock_share_has_row = 0;
        g_mock_share_bytes = 0;
        return 0;
    }
    g_mock_share_key = key;
    g_mock_share_bytes = bytes;
    memcpy(g_mock_share_val, value, bytes);
    g_mock_share_has_row = 1;
    return 0;
}

static uint32_t MockShareGet(HfMod* m, uint64_t key, void* value, uint32_t capacity)
{
    (void)m;
    if (!g_mock_share_has_row || key != g_mock_share_key) return 0u;
    uint32_t n = g_mock_share_bytes < capacity ? g_mock_share_bytes : capacity;
    if (value && n) memcpy(value, g_mock_share_val, n);
    return g_mock_share_bytes;
}

static int MockShareRow(HfMod* m, uint32_t index, uint64_t* key, void* value, uint32_t capacity, uint32_t* bytes)
{
    (void)m;
    if (index != 0u || !g_mock_share_has_row) return 0;
    if (key) *key = g_mock_share_key;
    uint32_t n = g_mock_share_bytes < capacity ? g_mock_share_bytes : capacity;
    if (value && n) memcpy(value, g_mock_share_val, n);
    if (bytes) *bytes = g_mock_share_bytes;
    return 1;
}

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
    g_mock.pkg_release = MockPkgRelease;
    g_mock.pkgs_idle = MockPkgsIdle;
    g_mock.has_headroom = MockHasHeadroom;
    g_mock.project_anchor = MockProjectAnchor;
    g_mock.spawn_transport = MockSpawnTransport;
    g_mock.spawn_roster_row = MockSpawnRosterRow;
    g_mock.particle_fx = MockParticleFx;
    g_mock.breach_burst = MockBreachBurst;
    g_mock.alive_override = MockAlive;
    g_mock.remove_entity = MockRemoveEntity;
    g_mock.request_entity_spawn = MockRequestEntitySpawn;
    g_mock.activate_beacon_override = MockActivateBeacon;
}

static void ResetMockCountersAndModState(void)
{
    ResetMissionState();
    g_pins_checked = g_pins_ok = g_exe_pins_ok = 0;
    g_native_faction = -1;
    memset(g_rosters_loaded, 0, sizeof g_rosters_loaded);
    memset(g_preload_pkgs, 0, sizeof g_preload_pkgs);
    memset(g_preload_count, 0, sizeof g_preload_count);
    memset(g_preload_requested, 0, sizeof g_preload_requested);
    memset(g_preload_built, 0, sizeof g_preload_built);
    memset(g_faction_resident, 0, sizeof g_faction_resident);
    memset(g_chaos_owned_pkgs, 0, sizeof g_chaos_owned_pkgs);
    g_chaos_owned_pkg_count = 0u;
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
    skipped_logs = fallback_logs = landing_logs = preload_ready_logs = preload_deferred_logs = 0;
    g_pkg_request_calls = g_pkg_requested_items = g_lookup_pkg_calls = 0;
    g_pkg_release_calls = g_pkg_released_items = g_pkg_double_free_errors = 0;
    g_remove_entity_calls = g_req_entity_spawn_calls = g_activate_beacon_calls = 0;
    g_last_removed_entity = g_last_spawned_entity = g_last_beacon_type = 0u;
    g_last_spawned_resource = 0ULL;
    spawn_logs = spawn_unmapped_logs = transport_spawn_logs = wave_complete_logs = wave_timeout_logs = 0;
    peer_observe_logs = host_migration_logs = peer_leave_logs = 0;
    g_mock_dead_count = 0;
    g_mock_auth_deny_entity = 0;
    g_mock_send_calls = 0;
    g_mock_last_send_to = 0;
    g_mock_last_send_bytes = 0;
    g_mock_share_has_row = 0;
    g_mock_share_key = 0;
    g_mock_share_bytes = 0;
    memset(g_mock_local_user_mem, 0, sizeof g_mock_local_user_mem);
    uint64_t lp = 1001ULL;
    memcpy(g_mock_local_user_mem + 0xB398u, &lp, 8);
    if (g_arena_game) {
        void* uptr = g_mock_local_user_mem;
        memcpy(g_arena_game + CHAOS_LOCAL_USER_RVA, &uptr, 8);
    }
    stopped = 0;
    g_has_session_config = 0;
    g_d12_config_active = 0;
    g_last_bridge_seen_mtime = 0ULL;
    g_last_bridge_seen_text[0] = 0;
    g_hellforge_mods_path[0] = 0;
    g_hellforge_mods_mtime = 0ULL;
    g_detected_mods_mode = CD_MODE_AUTO;
    InterlockedExchange(&g_empire_mode_active, 0L);
    g_host_opts = cd_host_options_defaults();
    g_ultrakill.d11_snapshot_valid = 0;
    g_ultrakill.d12_applied = 0;
    ResetMissionState();
    if (hf) state = hf->game_state(self);
    previous_role = (unsigned)Host();
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
    CHECK(c.air_runs_enabled == 1 && c.atte_walker_support == 1);

    const char* invalid[] = {
        "enabled=2", "total_cap=641", "squad_size=0", "squad_size=33", "bug_weight=-1", "squid_weight=201",
        "cooldown_seconds=9", "cooldown_seconds=901", "landing_grace_seconds=5", "landing_grace_seconds=301",
        "mission_budget=0", "mission_budget=25001", "air_runs_enabled=2", "atte_walker_support=2",
        "unknown=1", "enabled=1junk", "enabled=99999999999999999999999", "enabled=", "=1"
    };
    for (unsigned i = 0; i < sizeof invalid / sizeof *invalid; i++) {
        CHECK(!ChaosParse(&c, invalid[i]));
        CHECK(!memcmp(&c, &before, sizeof c));
    }
    CHECK(ChaosParse(&c, "cooldown_seconds=10\r\n") && c.cooldown == 10);
    CHECK(ChaosParse(&c, "landing_grace_seconds=6\n") && c.grace == 6);
    CHECK(ChaosParse(&c, "squad_size=24\n") && c.squad_size == 24);
    CHECK(ChaosParse(&c, "mission_budget=25000\n") && c.mission_budget == 25000);
    CHECK(ChaosParse(&c, "air_runs_enabled=0\n") && c.air_runs_enabled == 0);
    CHECK(ChaosParse(&c, "atte_walker_support=0\n") && c.atte_walker_support == 0);
    CHECK(ChaosParse(&c, "air_runs_enabled=1\n") && c.air_runs_enabled == 1);
    CHECK(ChaosParse(&c, "atte_walker_support=1\n") && c.atte_walker_support == 1);
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
    PreloadForeignFactionPackages((uintptr_t)g_arena_game, (uintptr_t)g_arena_exe, kArenaExeSize, 1.0f, HF_STATE_MISSION);
    CHECK(!g_faction_resident[CHAOS_BOTS] && !g_faction_resident[CHAOS_SQUIDS] && preload_ready_logs == 0u);

    g_mock_idle = 1;
    PreloadForeignFactionPackages((uintptr_t)g_arena_game, (uintptr_t)g_arena_exe, kArenaExeSize, 1.0f, HF_STATE_MISSION);
    CHECK(g_faction_resident[CHAOS_BOTS] && g_faction_resident[CHAOS_SQUIDS] && !g_preload_built[CHAOS_BUGS] &&
          preload_ready_logs == 2u);

    /* Unrelated engine streaming flips idle=0, then recovers idle=1 -> latched residency stays 1 and logs once */
    g_mock_idle = 0;
    PreloadForeignFactionPackages((uintptr_t)g_arena_game, (uintptr_t)g_arena_exe, kArenaExeSize, 1.0f, HF_STATE_MISSION);
    CHECK(g_faction_resident[CHAOS_BOTS] && g_faction_resident[CHAOS_SQUIDS] && preload_ready_logs == 2u);
    g_mock_idle = 1;
    PreloadForeignFactionPackages((uintptr_t)g_arena_game, (uintptr_t)g_arena_exe, kArenaExeSize, 1.0f, HF_STATE_MISSION);
    CHECK(g_faction_resident[CHAOS_BOTS] && g_faction_resident[CHAOS_SQUIDS] && preload_ready_logs == 2u);

    /* Clear mission state -> verify parts == 1025 and state != 4 fail residency even when idle=1 */
    ResetMissionState();
    AddExeResidentPkg(kBaseFactionPackages[CHAOS_BOTS][0], 1025u, 4u);
    PreloadForeignFactionPackages((uintptr_t)g_arena_game, (uintptr_t)g_arena_exe, kArenaExeSize, 1.0f, HF_STATE_MISSION);
    CHECK(!g_faction_resident[CHAOS_BOTS] && g_faction_resident[CHAOS_SQUIDS]);

    ResetMissionState();
    AddExeResidentPkg(kBaseFactionPackages[CHAOS_BOTS][0], 4u, 3u);
    PreloadForeignFactionPackages((uintptr_t)g_arena_game, (uintptr_t)g_arena_exe, kArenaExeSize, 1.0f, HF_STATE_MISSION);
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

    /* Leave mission cleanly -> fresh Host waits for living player -> latches landed on game thread without HF_ON_PLAYER_SPAWN */
    game_state = HF_STATE_SHIP;
    Seconds(1);
    role = HF_SESSION_HOST;
    game_state = HF_STATE_MISSION;
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

static void SetMockLobbyPeers(uint32_t count, const uint64_t peers[4])
{
    memcpy(g_pm_mem + 0x84u, &count, 4);
    for (uint32_t i = 0; i < 4u; i++) {
        uint64_t p = (i < count) ? peers[i] : 0ULL;
        memcpy(g_pm_mem + 0x2C8u + (uintptr_t)i * 0x38u, &p, 8);
    }
}

static void TestHostClientSplitAndLateJoin(HfApi* channel_api)
{
    /* 1. Prepare Mission -> Mission preload gating on Client (HF_SESSION_JOINER):
     *    v0.3.2: ZERO foreign package preloads during HF_STATE_PREPARE_MISSION; preloads smoothly in HF_STATE_MISSION. */
    InitMockGameArenas(CHAOS_NATIVE_BUGS_BIT, 5u);
    ResetMockCountersAndModState();
    config = ChaosDefaults(); config.grace = 15; config.cooldown = 30;
    config.bots_weight = 100; config.squids_weight = 0;
    Publish();

    uint64_t peers4[4] = {1001ULL, 2002ULL, 3003ULL, 4004ULL};
    SetMockLobbyPeers(4u, peers4);

    role = HF_SESSION_JOINER;
    game_state = HF_STATE_PREPARE_MISSION;
    Seconds(5);
    CHECK(g_pkg_request_calls > 0u && g_lookup_pkg_calls > 0u);
    CHECK(g_mock_send_calls >= 1u && g_mock_last_send_to == HF_TO_HOST);
    ChaosPeerMsg sent_msg;
    memcpy(&sent_msg, g_mock_last_send_buf, sizeof sent_msg);
    CHECK(ChaosPeerMsgValid(&sent_msg));
    CHECK(sent_msg.in_mission == 0u);
    CHECK(g_transport_calls == 0 && g_roster_calls == 0);

    /* Entering active mission as Joiner paces foreign package preload cleanly and reports in_mission=1 */
    game_state = HF_STATE_MISSION;
    /* Simulate Host already in mission with shared state snapshot */
    ChaosSharedState host_st = {0};
    host_st.magic = CHAOS_SYNC_MAGIC;
    host_st.version = CHAOS_SYNC_VERSION;
    host_st.native_faction = CHAOS_BUGS;
    host_st.landed = 1u;
    host_st.mission_spawned = 12u;
    host_st.attempts = 2u;
    host_st.epoch = 5u;
    host_st.cooldown_rem_ms = 25000;
    g_mock_share_key = CHAOS_SHARE_KEY_STATE;
    g_mock_share_bytes = sizeof host_st;
    memcpy(g_mock_share_val, &host_st, sizeof host_st);
    g_mock_share_has_row = 1;

    Seconds(1);
    CHECK(landed == 1u && g_tracker.mission_spawned == 12 && clock_state.attempts == 2u);
    CHECK(fabs((clock_state.next - clock_state.age) - 25.0) < 0.05);
    Seconds(4);
    CHECK(g_faction_resident[CHAOS_BOTS] && g_faction_resident[CHAOS_SQUIDS]);
    memcpy(&sent_msg, g_mock_last_send_buf, sizeof sent_msg);
    CHECK(sent_msg.in_mission == 1u &&
          (sent_msg.resident_mask & ((1u << CHAOS_BOTS) | (1u << CHAOS_SQUIDS))) ==
              ((1u << CHAOS_BOTS) | (1u << CHAOS_SQUIDS)));
    CHECK(g_transport_calls == 0 && g_roster_calls == 0 && g_fx_calls == 0 && g_burst_calls == 0);

    /* 2. Host + Late Joiner barrier: Host blocks spawn while late-joining peer still preloading, then unblocks */
    game_state = HF_STATE_SHIP;
    Seconds(1);
    InitMockGameArenas(CHAOS_NATIVE_BUGS_BIT, 5u);
    ResetMockCountersAndModState();
    config = ChaosDefaults(); config.grace = 15; config.cooldown = 30;
    config.bots_weight = 100; config.squids_weight = 0;
    Publish();
    role = HF_SESSION_HOST;
    game_state = HF_STATE_MISSION;
    uint64_t host_only[4] = {1001ULL, 0ULL, 0ULL, 0ULL};
    SetMockLobbyPeers(1u, host_only);
    Seconds(10); /* Host landed at t=1, clock age ~9s */
    CHECK(landed == 1u && g_mock_share_has_row == 1);

    /* Peer 2002 joins late at t=10s and sends resident_mask=0 (still loading) */
    SetMockLobbyPeers(2u, peers4);
    Seconds(1); /* clock age ~10s, peer registered */
    HfMessageEvent join_msg = {0};
    join_msg.info.size = sizeof join_msg;
    join_msg.info.kind = HF_ON_MESSAGE;
    join_msg.from = 2002ULL;
    ChaosPeerMsg peer_loading = {CHAOS_SYNC_MAGIC, CHAOS_SYNC_VERSION, CHAOS_MSG_PEER_STATUS, 0u, 1u, {0}, 1u};
    memcpy(join_msg.data, &peer_loading, sizeof peer_loading);
    join_msg.bytes = sizeof peer_loading;
    OnMessage(self, (const HfEventInfo*)&join_msg, NULL);

    /* Advance past grace=15s -> Host must hold off spawning (peer_and_late_join_barrier_unqualified) */
    Seconds(6);
    CHECK(skipped_logs >= 1u && g_transport_calls == 0 && g_roster_calls == 0);

    /* Peer 3003 also joins, then disconnects cleanly while 2002 finishes Bots preload */
    SetMockLobbyPeers(3u, peers4);
    Seconds(1);
    SetMockLobbyPeers(2u, peers4); /* 3003 disconnects */
    ChaosPeerMsg peer_ready = {CHAOS_SYNC_MAGIC, CHAOS_SYNC_VERSION, CHAOS_MSG_PEER_STATUS,
                               (uint8_t)(1u << CHAOS_BOTS), 1u, {0}, 1u};
    memcpy(join_msg.data, &peer_ready, sizeof peer_ready);
    OnMessage(self, (const HfEventInfo*)&join_msg, NULL);

    /* Advance to next cooldown check -> unblocked now that 2002 ready & 3003 pruned; co-op uses staggered roster */
    Seconds(30);
    CHECK(peer_leave_logs >= 1u && g_transport_calls == 0u && g_roster_calls == 4u);

    /* Verify is_authority=0 on an entity prevents host cargo/tracker recording */
    g_mock_auth_deny_entity = 7777;
    HfSpawnEvent spoof = {0};
    spoof.info.size = sizeof spoof;
    spoof.entity = 7777u; spoof.resource = g_rosters[CHAOS_BOTS][0].entity_hash; spoof.authority = 1u;
    OnSpawn(self, (const HfEventInfo*)&spoof, NULL);
    CHECK(!ChaosTrackerHasEntity(&g_tracker, 7777u));
    (void)channel_api;
}

static void TestMidMissionHostMigration(void)
{
    InitMockGameArenas(CHAOS_NATIVE_BUGS_BIT, 5u);
    role = HF_SESSION_JOINER;
    game_state = HF_STATE_SHIP;
    ResetMockCountersAndModState();
    config = ChaosDefaults(); config.grace = 45; config.cooldown = 40;
    config.total_cap = 8; config.faction_cap = 4; config.squad_size = 4; config.mission_budget = 10;
    config.bots_weight = 100; config.squids_weight = 0;
    Publish();

    uint64_t peers2[4] = {1001ULL, 2002ULL, 0ULL, 0ULL};
    SetMockLobbyPeers(2u, peers2);

    /* Client enters active mission as Joiner and preloads foreign factions */
    game_state = HF_STATE_MISSION;
    Seconds(5);
    CHECK(g_faction_resident[CHAOS_BOTS] && g_faction_resident[CHAOS_SQUIDS] && landed == 1u);

    /* Host had spawned 4 Bots that died earlier + 4 live Bots now replicated (authority=0), then synced shared row mission_spawned=8 */
    for (uint32_t i = 0; i < 4u; i++) {
        HfSpawnEvent rev = {0};
        rev.info.size = sizeof rev;
        rev.entity = 5000u + i;
        rev.resource = g_rosters[CHAOS_BOTS][0].entity_hash;
        rev.authority = 0u; /* replicated from old host */
        OnSpawn(self, (const HfEventInfo*)&rev, NULL);
    }
    ChaosSharedState st = {0};
    st.magic = CHAOS_SYNC_MAGIC; st.version = CHAOS_SYNC_VERSION;
    st.native_faction = CHAOS_BUGS; st.landed = 1u;
    st.mission_spawned = 8u; st.attempts = 2u; st.epoch = 3u;
    st.cooldown_rem_ms = 12000; /* 12s remaining on cooldown */
    HfShareEvent sev = {0};
    sev.info.size = sizeof sev; sev.info.kind = HF_ON_SHARE;
    sev.key = CHAOS_SHARE_KEY_STATE; sev.bytes = sizeof st;
    memcpy(sev.value, &st, sizeof st);
    OnShare(self, (const HfEventInfo*)&sev, NULL);
    CHECK(peer_observe_logs == 4u && g_tracker.live[CHAOS_BOTS] == 4 && g_tracker.mission_spawned == 8);
    CHECK(g_transport_calls == 0 && g_roster_calls == 0);

    /* Kill one replicated enemy BEFORE migration -> verifies prune tracks live deaths */
    g_mock_dead_entities[g_mock_dead_count++] = 5003u;
    Seconds(1); /* clock_rem drops 12 -> 11s; entity 5003 pruned */
    CHECK(g_tracker.live[CHAOS_BOTS] == 3 && g_tracker.mission_spawned == 8);

    /* Old Host disconnects: Client is promoted to Host MID-MISSION! */
    uint64_t migrated_only[4] = {1001ULL, 0ULL, 0ULL, 0ULL};
    SetMockLobbyPeers(1u, migrated_only);
    role = HF_SESSION_HOST;
    Seconds(1); /* Migration occurs cleanly */
    CHECK(host_migration_logs == 1u);
    CHECK(landed == 1u && landing_logs == 0u); /* did NOT re-trigger CHAOS_LANDING or 45s grace! */
    CHECK(g_faction_resident[CHAOS_BOTS] && g_faction_resident[CHAOS_SQUIDS]);
    CHECK(g_tracker.live[CHAOS_BOTS] == 3 && g_tracker.mission_spawned == 8);

    /* Advance remaining 10s of cooldown: 9s -> nothing spawns; at +1s -> refused by inherited live[BOTS]=3 (rem=1 < min_wave=2)! */
    Seconds(9);
    CHECK(g_transport_calls == 0);
    Seconds(1);
    CHECK(g_transport_calls == 0 && skipped_logs >= 1u);

    /* Kill another inherited Bot (live=2 -> rem=2 >= min_wave=2): next cooldown fires partial 2-bot wave & hits mission_budget=10! */
    g_mock_dead_entities[g_mock_dead_count++] = 5002u;
    Seconds(40);
    CHECK(g_transport_calls == 1 && g_pending.squad_size == 2 && g_tracker.mission_spawned == 10);
    /* Complete Wave 2 dropship unload cleanly (2 Bots spawn -> live=4, mission_spawned=10) */
    for (uint32_t s = 0; s < 2u; s++) {
        HfSpawnEvent w2 = {0};
        w2.info.size = sizeof w2;
        w2.entity = 6000u + s;
        w2.resource = g_rosters[CHAOS_BOTS][0].entity_hash;
        w2.authority = 1u;
        OnSpawn(self, (const HfEventInfo*)&w2, NULL);
    }
    Seconds(1);
    CHECK(!g_pending.active && g_tracker.live[CHAOS_BOTS] == 4 && g_tracker.mission_spawned == 10);
    /* Kill all live enemies so only mission_budget=10 (NOT population_cap) blocks next wave */
    g_mock_dead_entities[g_mock_dead_count++] = 5000u;
    g_mock_dead_entities[g_mock_dead_count++] = 5001u;
    for (uint32_t s = 0; s < 2u; s++) g_mock_dead_entities[g_mock_dead_count++] = 6000u + s;
    /* Next cooldown triggers at t=96s (39 more 1s ticks) -> must refuse for mission_budget (rem=0 < 2) */
    Seconds(39);
    CHECK(g_transport_calls == 1 && skipped_logs >= 2u);

    /* Test Host -> Joiner demotion cancels pending wave safely without crashing or spawning */
    game_state = HF_STATE_SHIP;
    role = HF_SESSION_HOST;
    ResetMockCountersAndModState();
    config = ChaosDefaults(); config.grace = 10; config.cooldown = 20;
    config.bots_weight = 100; config.squids_weight = 0; Publish();
    SetMockLobbyPeers(1u, migrated_only);
    game_state = HF_STATE_MISSION;
    Seconds(10);
    CHECK(g_pending.active == 1 && g_tracker.reserved[CHAOS_BOTS] == 4);
    role = HF_SESSION_JOINER;
    Seconds(1);
    CHECK(host_migration_logs == 1u && !g_pending.active && g_tracker.reserved[CHAOS_BOTS] == 0);
}

static int WriteTextWithMtime(const char* path, const char* content, uint64_t mtime_100ns)
{
    FILE* f = fopen(path, "wb");
    if (!f) return 0;
    size_t n = strlen(content);
    size_t wr = fwrite(content, 1, n, f);
    fclose(f);
    if (wr != n) return 0;
    HANDLE h = CreateFileA(path, FILE_WRITE_ATTRIBUTES,
                           FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                           NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return 0;
    FILETIME ft;
    ft.dwLowDateTime = (DWORD)(mtime_100ns & 0xFFFFFFFFULL);
    ft.dwHighDateTime = (DWORD)(mtime_100ns >> 32);
    BOOL ok = SetFileTime(h, NULL, NULL, &ft);
    CloseHandle(h);
    return ok != 0;
}

static void TestHostOptionsPresetsAndPrecedence(HfApi* api)
{
    char saved_fixture[1800];
    strcpy(saved_fixture, fixture);

    char mods_dir[1600], folder_mod[1650], unpacked_base[1650], unpacked_pkg[1700], unpacked_mod[1750], host_json[1700];
    snprintf(mods_dir, sizeof mods_dir, "%s\\mods", saved_fixture);
    snprintf(folder_mod, sizeof folder_mod, "%s\\mods\\chaos-invasions", saved_fixture);
    snprintf(unpacked_base, sizeof unpacked_base, "%s\\mods\\.unpacked", saved_fixture);
    snprintf(unpacked_pkg, sizeof unpacked_pkg, "%s\\mods\\.unpacked\\hash031", saved_fixture);
    snprintf(unpacked_mod, sizeof unpacked_mod, "%s\\mods\\.unpacked\\hash031\\chaos-invasions", saved_fixture);
    snprintf(host_json, sizeof host_json, "%s\\mods\\clonedivers-host-options.json", saved_fixture);

    CreateDirectoryA(mods_dir, NULL);
    CreateDirectoryA(folder_mod, NULL);
    CreateDirectoryA(unpacked_base, NULL);
    CreateDirectoryA(unpacked_pkg, NULL);
    CreateDirectoryA(unpacked_mod, NULL);

    const uint64_t t0 = 134200000000000000ULL;

    /* 1. Boot with .unpacked install and chaosIntensity = "off" -> disabled=1, zero spawns in mission! */
    CHECK(WriteTextWithMtime(
        host_json,
        "{\"format\":1,\"chaosIntensity\":\"off\",\"heavySeaf\":\"standard\",\"fasterCorpseCleanup\":true}",
        t0));
    strcpy(fixture, unpacked_mod);
    InitMockGameArenas(CHAOS_NATIVE_BUGS_BIT, 5u);
    role = HF_SESSION_SOLO;
    game_state = HF_STATE_MISSION;
    ResetMockCountersAndModState();
    subscriptions = ticks = 0;
    CHECK(hellforge_mod_init(api, (HfMod*)1) == 0);
    CHECK(config.enabled == 0 && config.cooldown == 150 && config.total_cap == 12);
    Seconds(60);
    CHECK(g_transport_calls == 0 && g_roster_calls == 0 && skipped_logs >= 1u);

    /* 2. Live switch via clonedivers-host-options.json -> "total-war" */
    CHECK(WriteTextWithMtime(
        host_json,
        "{\"format\":1,\"chaosIntensity\":\"total-war\",\"heavySeaf\":\"standard\",\"fasterCorpseCleanup\":true}",
        t0 + 10000000ULL));
    Seconds(2);
    CHECK(config.enabled == 1 && config.cooldown == 45 && config.grace == 8 &&
          config.total_cap == 64 && config.faction_cap == 40 &&
          config.squad_size == 20 && config.mission_budget == 960 &&
          config.squids_weight == 100);
    /* Verify 8s grace triggers a 20-unit outer swarm wave (6 vanguard + 14 queued)! */
    config.bots_weight = 100; config.squids_weight = 0; Publish();
    g_bridge_user_edit_mtime = 0ULL;
    ChaosReset(&clock_state, &config);
    Seconds(8);
    CHECK(g_roster_calls >= 6u && g_pending.squad_size == 6);

    /* 3. Verify mtime precedence: newer in-game Mod Options Menu edit in ChaosInvasions.values wins */
    char bridge_edit[512];
    snprintf(bridge_edit, sizeof bridge_edit,
             "session=%lu\nenabled=1\ncooldown_seconds=120\nlanding_grace_seconds=45\ntotal_cap=12\n"
             "faction_cap=6\nsquad_size=4\nmission_budget=36\ndroid_weight=100\nsquid_weight=80\nbug_weight=100\n"
             "air_runs_enabled=1\natte_walker_support=1\n",
             token);
    CHECK(WriteTextWithMtime(bridge, bridge_edit, t0 + 50000000ULL));
    Seconds(2);
    CHECK(config.cooldown == 120 && config.squad_size == 4);

    /* Older host options write (t0 + 30s < t0 + 50s) does NOT overwrite session edit */
    CHECK(WriteTextWithMtime(
        host_json,
        "{\"format\":1,\"chaosIntensity\":\"off\",\"heavySeaf\":\"standard\",\"fasterCorpseCleanup\":true}",
        t0 + 30000000ULL));
    Seconds(2);
    CHECK(config.enabled == 1 && config.cooldown == 120);

    /* Newer Launcher Apply (t0 + 80s > t0 + 50s) with folder install -> "standard" overrides session edit */
    strcpy(fixture, folder_mod);
    cd_resolve_host_options_path(fixture, g_host_options_path, sizeof g_host_options_path);
    CHECK(WriteTextWithMtime(
        host_json,
        "{\"format\":1,\"chaosIntensity\":\"standard\",\"heavySeaf\":\"standard\",\"fasterCorpseCleanup\":true}",
        t0 + 80000000ULL));
    Seconds(2);
    CHECK(config.enabled == 1 && config.cooldown == 150 && config.grace == 45 &&
          config.total_cap == 12 && config.squad_size == 4 && config.mission_budget == 36);

    DeleteFileA(host_json);
    strcpy(fixture, saved_fixture);
    g_host_options_path[0] = 0;
    g_host_options_mtime = 0ULL;
    g_bridge_user_edit_mtime = 0ULL;
    Publish();
}

static void TestPrepareMissionZeroPreloadAndPacedMissionQueue(void)
{
    /* v2.3: Verify disabled Host ("off") performs 0 preloads during HF_STATE_PREPARE_MISSION,
     * while active Solo/Host/Joiner pre-stream foreign packages during HF_STATE_PREPARE_MISSION (Hellpod drop)
     * after 1.5s settle at <= 2 requests/frame and preserve residency seamlessly into HF_STATE_MISSION! */
    InitMockGameArenas(CHAOS_NATIVE_BOTS_BIT, 11u);
    role = HF_SESSION_SOLO;
    game_state = HF_STATE_SHIP;
    ResetMockCountersAndModState();
    ChaosApplyHostIntensity(&config, &g_ini_config, CD_CHAOS_OFF);
    Publish();
    game_state = HF_STATE_PREPARE_MISSION;
    Seconds(15);
    CHECK(g_pkg_request_calls == 0u && g_pkg_requested_items == 0u && g_lookup_pkg_calls == 0u);

    /* Now test active Host pre-streaming during HF_STATE_PREPARE_MISSION at real 60 FPS (dt = 1/60s):
     * (a) 0 requests during 1.5s settle, (b) <= 2 requests/tick, (c) Squids completes before Bugs starts,
     * (d) transitioning HF_STATE_PREPARE_MISSION -> HF_STATE_MISSION keeps both foreign factions resident immediately! */
    InitMockGameArenas(CHAOS_NATIVE_BOTS_BIT, 11u);
    role = HF_SESSION_HOST;
    game_state = HF_STATE_SHIP;
    ResetMockCountersAndModState();
    ChaosApplyHostIntensity(&config, &g_ini_config, CD_CHAOS_TOTAL_WAR);
    Publish();

    game_state = HF_STATE_PREPARE_MISSION;
    const float dt60 = 1.0f / 60.0f;
    for (int f = 0; f < 88; f++) {
        Tick(self, dt60, NULL);
    }
    CHECK(g_pkg_request_calls == 0u && !g_preload_built[CHAOS_SQUIDS] && !g_preload_built[CHAOS_BUGS]);

    unsigned max_per_tick = 0u;
    int saw_squids_before_bugs = 0;
    for (int f = 0; f < 420; f++) {
        unsigned before = g_pkg_request_calls;
        Tick(self, dt60, NULL);
        unsigned delta = g_pkg_request_calls - before;
        if (delta > max_per_tick) max_per_tick = delta;
        if (g_preload_built[CHAOS_SQUIDS] && !g_preload_built[CHAOS_BUGS]) {
            saw_squids_before_bugs = 1;
        }
    }
    CHECK(max_per_tick >= 1u && max_per_tick <= 2u);
    CHECK(saw_squids_before_bugs == 1);
    CHECK(g_faction_resident[CHAOS_SQUIDS] && g_faction_resident[CHAOS_BUGS]);
    CHECK(g_mock_smoke_state == 0); /* Smoke flare deferred until HF_STATE_MISSION */

    game_state = HF_STATE_MISSION;
    Tick(self, dt60, NULL);
    CHECK(g_faction_resident[CHAOS_SQUIDS] && g_faction_resident[CHAOS_BUGS]);
}

static void TestH4SlowD11JoinerBarrierAndImmediateMissionSync(void)
{
    /* 1. H4 Joiner: Immediate in_mission: 0 -> 1 transmission on the VERY FIRST 60Hz tick in HF_STATE_MISSION
     *    (even if Director is momentarily NULL). */
    InitMockGameArenas(CHAOS_NATIVE_BOTS_BIT, 11u);
    role = HF_SESSION_JOINER;
    game_state = HF_STATE_PREPARE_MISSION;
    ResetMockCountersAndModState();
    config = ChaosDefaults();
    Publish();

    uint64_t peers2[4] = {1001ULL, 2002ULL, 0ULL, 0ULL};
    SetMockLobbyPeers(2u, peers2);

    /* Spend 1s in HF_STATE_PREPARE_MISSION (within 1.5s settle) -> sends {resident_mask=0, in_mission=0} once */
    Seconds(1);
    CHECK(g_mock_send_calls == 1u);
    ChaosPeerMsg msg;
    memcpy(&msg, g_mock_last_send_buf, sizeof msg);
    CHECK(ChaosPeerMsgValid(&msg) && msg.resident_mask == 0u && msg.in_mission == 0u);

    /* Spend another 44s in PrepareMission -> preloads foreign packages while keeping in_mission == 0 */
    Seconds(44);
    memcpy(&msg, g_mock_last_send_buf, sizeof msg);
    CHECK(ChaosPeerMsgValid(&msg) && msg.in_mission == 0u);
    unsigned sends_before_mission = g_mock_send_calls;

    /* Temporarily zero Director pointer on Frame 1 of HF_STATE_MISSION to prove Tick 1 STILL sends in_mission=1! */
    void* saved_dir = NULL;
    memcpy(&saved_dir, g_arena_game + CHAOS_DIRECTOR_RVA, 8);
    void* null_dir = NULL;
    memcpy(g_arena_game + CHAOS_DIRECTOR_RVA, &null_dir, 8);

    game_state = HF_STATE_MISSION;
    Tick(self, 1.0f / 60.0f, NULL); /* Single 16.6ms frame! */
    CHECK(g_mock_send_calls == sends_before_mission + 1u);
    memcpy(&msg, g_mock_last_send_buf, sizeof msg);
    CHECK(ChaosPeerMsgValid(&msg) && msg.in_mission == 1u);
    memcpy(g_arena_game + CHAOS_DIRECTOR_RVA, &saved_dir, 8);

    /* 2. H4 Host: Slow D11 Mega City joiner spending 55s (> 30s!) in PrepareMission (in_mission=0) MUST NOT expire at 30s,
     *    and gets a fresh 30s post-entry preload grace window upon transitioning in_mission: 0 -> 1! */
    game_state = HF_STATE_SHIP;
    Seconds(1);
    InitMockGameArenas(CHAOS_NATIVE_BUGS_BIT, 11u);
    role = HF_SESSION_HOST;
    game_state = HF_STATE_MISSION;
    ResetMockCountersAndModState();
    config = ChaosDefaults();
    ChaosApplyHostIntensity(&config, &config, CD_CHAOS_TOTAL_WAR);
    config.grace = 30;
    config.cooldown = 30;
    config.bots_weight = 100;
    config.squids_weight = 0;
    Publish();
    ChaosReset(&clock_state, &config);

    SetMockLobbyPeers(2u, peers2);
    Seconds(5); /* Host landed, foreign packages preloaded, clock age ~4s */
    CHECK(landed == 1u && g_faction_resident[CHAOS_BOTS]);

    /* Peer 2002 sends hello in HF_STATE_PREPARE_MISSION (in_mission = 0, resident_mask = 0) */
    HfMessageEvent peer_ev = {0};
    peer_ev.info.size = sizeof peer_ev;
    peer_ev.info.kind = HF_ON_MESSAGE;
    peer_ev.from = 2002ULL;
    ChaosPeerMsg peer_prep = {CHAOS_SYNC_MAGIC, CHAOS_SYNC_VERSION, CHAOS_MSG_PEER_STATUS, 0u, 0u, {0}, 1u};
    memcpy(peer_ev.data, &peer_prep, sizeof peer_prep);
    peer_ev.bytes = sizeof peer_prep;
    OnMessage(self, (const HfEventInfo*)&peer_ev, NULL);

    /* Advance 50s (total 55s > 30s CHAOS_PEER_PRELOAD_GRACE_S!) while joiner is STILL in PrepareMission (in_mission=0):
     * Host MUST hold off spawning at t=30s! */
    Seconds(50);
    CHECK(g_transport_calls == 0u && g_roster_calls == 0u && skipped_logs >= 1u);

    /* Now at t=55s, Joiner enters HF_STATE_MISSION (in_mission: 0 -> 1, resident_mask still 0):
     * Host resets mission_entered_age so the 30s preload grace window starts fresh from t=55s! */
    ChaosPeerMsg peer_entered = {CHAOS_SYNC_MAGIC, CHAOS_SYNC_VERSION, CHAOS_MSG_PEER_STATUS, 0u, 1u, {0}, 1u};
    memcpy(peer_ev.data, &peer_entered, sizeof peer_entered);
    OnMessage(self, (const HfEventInfo*)&peer_ev, NULL);

    /* Advance +20s (t=75s; next cooldown check fires at t=60s, where since_mission = 5s < 30s -> still held!) */
    Seconds(20);
    CHECK(g_transport_calls == 0u && g_roster_calls == 0u && skipped_logs >= 2u);

    /* Joiner finishes preloading Bots -> sends resident_mask = (1 << CHAOS_BOTS), in_mission = 1 -> unblocks next wave! */
    ChaosPeerMsg peer_ready = {CHAOS_SYNC_MAGIC, CHAOS_SYNC_VERSION, CHAOS_MSG_PEER_STATUS,
                               (uint8_t)(1u << CHAOS_BOTS), 1u, {0}, 1u};
    memcpy(peer_ev.data, &peer_ready, sizeof peer_ready);
    OnMessage(self, (const HfEventInfo*)&peer_ev, NULL);
    Seconds(20); /* Passes t=90s cooldown check -> spawns via co-op staggered roster! */
    CHECK(g_transport_calls == 0u && g_roster_calls >= 4u);

    /* 3. H4 Safety Cap: A hung peer stuck in PrepareMission (in_mission=0) holds up to 120s, then unblocks past 120s! */
    game_state = HF_STATE_SHIP;
    Seconds(1);
    InitMockGameArenas(CHAOS_NATIVE_BUGS_BIT, 11u);
    role = HF_SESSION_HOST;
    game_state = HF_STATE_MISSION;
    ResetMockCountersAndModState();
    config = ChaosDefaults();
    config.grace = 30;
    config.cooldown = 30;
    config.bots_weight = 100;
    config.squids_weight = 0;
    Publish();
    ChaosReset(&clock_state, &config);
    SetMockLobbyPeers(2u, peers2);
    Seconds(5); /* clock age ~4s */
    memcpy(peer_ev.data, &peer_prep, sizeof peer_prep);
    OnMessage(self, (const HfEventInfo*)&peer_ev, NULL);

    Seconds(110); /* clock age ~114s, since_join ~114s < 120s -> still blocked across t=30, 60, 90! */
    CHECK(g_transport_calls == 0u && g_roster_calls == 0u && skipped_logs >= 3u);
    Seconds(15);  /* clock age ~129s > 120s safety cap -> t=120s / 129s unblocks via co-op staggered roster! */
    CHECK(g_transport_calls == 0u && g_roster_calls >= 4u);
}

static void TestV034BotDropDeadlockAndCityHardening(void)
{
    /* 1. Partial headroom & alternate-faction fallback on native Squids (total-war: faction_cap=40, total_cap=64, squad_size=20) */
    ChaosConfig tw = ChaosDefaults();
    ChaosApplyHostIntensity(&tw, &tw, CD_CHAOS_TOTAL_WAR);
    CHECK(tw.faction_cap == 40 && tw.total_cap == 64 && tw.squad_size == 20);
    ChaosEvidence ev = {0};
    ev.host = ev.mission = ev.players = ev.closure = ev.resident = ev.peers = 1;
    ev.placement = ev.constructor = ev.arrival = ev.population_known = 1;
    ev.native_faction = CHAOS_SQUIDS;
    ev.difficulty = 11;
    ev.live[CHAOS_BOTS] = 15; ev.total_live = 15; ev.mission_spawned = 26;
    CHECK(ChaosRefuse(&tw, &ev, CHAOS_BOTS) == NULL);
    CHECK(ChaosEffectiveSquadSize(&tw, &ev, CHAOS_BOTS) == 20);
    ev.live[CHAOS_BOTS] = 36; ev.total_live = 36;
    CHECK(ChaosRefuse(&tw, &ev, CHAOS_BOTS) == NULL);
    CHECK(ChaosEffectiveSquadSize(&tw, &ev, CHAOS_BOTS) == 4);
    ev.live[CHAOS_BOTS] = 39; ev.total_live = 39; /* rem=1 < min_wave=2 -> refuses Bots, allows Bugs */
    CHECK(strcmp(ChaosRefuse(&tw, &ev, CHAOS_BOTS), "population_cap") == 0);
    CHECK(ChaosRefuse(&tw, &ev, CHAOS_BUGS) == NULL);

    /* Live Tick() fallback: Bots capped at 39/40 on native Squids with both weights>0 -> falls back to Bug swarm! */
    InitMockGameArenas(CHAOS_NATIVE_SQUIDS_BIT, 11u);
    role = HF_SESSION_SOLO;
    game_state = HF_STATE_MISSION;
    ResetMockCountersAndModState();
    ChaosApplyHostIntensity(&config, &g_ini_config, CD_CHAOS_TOTAL_WAR);
    config.grace = 15; config.cooldown = 30;
    config.bots_weight = 199; config.bugs_weight = 1; /* strongly favors Bots */
    Publish();
    ChaosReset(&clock_state, &config);
    for (uint32_t i = 0; i < 39u; i++) {
        CHECK(ChaosTrackerRecordSpawnAt(&g_tracker, CHAOS_BOTS, 8000u + i, 0, 10.0));
    }
    Seconds(16);
    CHECK(g_roster_calls >= 6 && g_transport_calls == 0);

    /* 2. Paired sub-entity 0x426b3df007be9f18 is NOT double-counted & corpse wrapper 0x62928899dac02979 suppressed */
    CHECK(ChaosEnemyFamily(0x426b3df007be9f18ull) == -1);
    CHECK(ChaosIsKnownCorpseWrapper(0x62928899DAC02979ull) == 1);
    InitMockGameArenas(CHAOS_NATIVE_SQUIDS_BIT, 11u);
    role = HF_SESSION_SOLO;
    game_state = HF_STATE_MISSION;
    ResetMockCountersAndModState();
    ChaosApplyHostIntensity(&config, &g_ini_config, CD_CHAOS_TOTAL_WAR);
    config.grace = 15; config.cooldown = 30; config.squad_size = 6;
    config.bots_weight = 100; config.bugs_weight = 0;
    Publish();
    ChaosReset(&clock_state, &config);
    Seconds(16);
    CHECK(g_transport_calls == 1 && g_pending.active && g_pending.squad_size == 6);
    HfSpawnEvent corpse_ev = {0};
    corpse_ev.info.size = sizeof corpse_ev;
    corpse_ev.entity = 8500u; corpse_ev.resource = 0x62928899DAC02979ull; corpse_ev.authority = 1;
    OnSpawn(self, (const HfEventInfo*)&corpse_ev, NULL);
    CHECK(spawn_unmapped_logs == 0u);
    HfSpawnEvent sub_ev = {0};
    sub_ev.info.size = sizeof sub_ev;
    sub_ev.entity = 8501u; sub_ev.resource = 0x426b3df007be9f18ull; sub_ev.authority = 1;
    OnSpawn(self, (const HfEventInfo*)&sub_ev, NULL);
    CHECK(g_pending.unloaded_count == 0 && g_tracker.live[CHAOS_BOTS] == 0);
    for (uint32_t s = 0; s < 6u; s++) {
        HfSpawnEvent b_ev = {0};
        b_ev.info.size = sizeof b_ev;
        b_ev.entity = 8600u + s; b_ev.resource = 0x0002ba767df856f3ull; b_ev.authority = 1;
        OnSpawn(self, (const HfEventInfo*)&b_ev, NULL);
    }
    CHECK(g_pending.unloaded_count == 6 && g_tracker.live[CHAOS_BOTS] == 6 && g_tracker.mission_spawned == 6);

    /* 3. Mega City tight |dz| anchor guard */
    float player[3] = {100.0f, 200.0f, 5.0f};
    float players[1][3] = {{100.0f, 200.0f, 5.0f}};
    float roof[3] = {145.0f, 200.0f, 23.0f};   /* |dz| = 18m rooftop */
    float street[3] = {145.0f, 200.0f, 9.0f};  /* |dz| = 4m street */
    CHECK(!ChaosAnchorValidEx(roof, player, players, 1, 6.0f));
    CHECK(!ChaosAnchorValid(roof, player, players, 1));
    CHECK(ChaosAnchorValidEx(street, player, players, 1, 6.0f));
}

static void TestV035Mission2PreloadRearmAndCoopSafeRoster(void)
{
    /* 1. ChaosRefMapInspect collision-chain & stale refcount inspection */
    uint8_t entries[16 * 16];
    memset(entries, 0xFF, sizeof entries);
    const uint64_t empty_k = 0xFFFFFFFFFFFFFFFFULL;
    const uint64_t pkg_a = 0xfdf011daecf24312ULL;
    const uint64_t pkg_b = 0x9e430d763af434c1ULL;
    const uint64_t one = 1ULL, zero = 0ULL;
    memcpy(entries + 3u * 16u + 0u, &pkg_a, 8);
    memcpy(entries + 3u * 16u + 8u, &one, 8);
    memcpy(entries + 4u * 16u + 0u, &pkg_b, 8);
    memcpy(entries + 4u * 16u + 8u, &one, 8);
    uint32_t used = 0, slot_a = 0, slot_b = 0;
    uint64_t rc_a = 0, rc_b = 0;
    int pres_a = 0, pres_b = 0;
    CHECK(ChaosRefMapInspect(entries, 16u, empty_k, pkg_a, &used, &pres_a, &slot_a, &rc_a));
    CHECK(used == 2u && pres_a == 1 && slot_a == 3u && rc_a == 1ULL);
    /* Resetting pkg_a.ref_count -> 0 in-place preserves key & collision slot_b */
    memcpy(entries + (size_t)slot_a * 16u + 8u, &zero, 8);
    CHECK(ChaosRefMapInspect(entries, 16u, empty_k, pkg_b, &used, &pres_b, &slot_b, &rc_b));
    CHECK(used == 2u && pres_b == 1 && slot_b == 4u && rc_b == 1ULL);

    /* 2. v2.3 Symmetric RefcountedPackageRelease on ResetMissionState (#43/#45/#46) + Mission-2 reload + mid-mission re-arm */
    InitMockGameArenas(CHAOS_NATIVE_SQUIDS_BIT, 11u);
    role = HF_SESSION_SOLO;
    game_state = HF_STATE_MISSION;
    ResetMockCountersAndModState();
    ChaosApplyHostIntensity(&config, &g_ini_config, CD_CHAOS_TOTAL_WAR);
    config.air_runs_enabled = 0;
    Publish();
    Seconds(6);
    CHECK(g_faction_resident[CHAOS_BOTS] && g_faction_resident[CHAOS_BUGS]);
    int bots_pkgs = g_preload_count[CHAOS_BOTS];
    int bugs_pkgs = g_preload_count[CHAOS_BUGS];
    CHECK(bots_pkgs >= 4 && bugs_pkgs >= 4);
    uint32_t owned_m1 = g_chaos_owned_pkg_count;
    CHECK(owned_m1 == (uint32_t)(bots_pkgs + bugs_pkgs));

    /* Simulate one shared package (ref == 2) held by an external engine system before mission teardown */
    uint64_t shared_pkg = g_preload_pkgs[CHAOS_BOTS][0];
    for (uint32_t s = 0; s < 256u; s++) {
        if (g_ref_slots[s].key == shared_pkg) g_ref_slots[s].ref = 2ULL;
    }

    /* Return to Ship: ResetMissionState() MUST call RefcountedPackageRelease (0x12FE4E0) on all owned packages!
     * Exclusive packages go 1 -> 0 (unloaded), shared_pkg goes 2 -> 1 (preserved), and repeated ResetMissionState() has 0 double-frees! */
    g_pkg_release_calls = 0u;
    g_pkg_double_free_errors = 0u;
    game_state = HF_STATE_SHIP;
    Seconds(1);
    ResetMissionState(); /* second reset must be a safe no-op */
    CHECK(!g_faction_resident[CHAOS_BOTS] && !g_faction_resident[CHAOS_BUGS]);
    CHECK(g_chaos_owned_pkg_count == 0u);
    CHECK(g_pkg_release_calls == owned_m1 && g_pkg_double_free_errors == 0u);
    for (uint32_t s = 0; s < 256u; s++) {
        if (g_ref_slots[s].key == shared_pkg) CHECK(g_ref_slots[s].ref == 1ULL);
        else if (g_ref_slots[s].key != 0ULL) CHECK(g_ref_slots[s].ref == 0ULL);
    }

    /* Enter Mission 2: shared_pkg is still resident (skips CallPkgRequest), while the (owned_m1 - 1) unloaded packages request cleanly! */
    g_pkg_request_calls = 0;
    g_pkg_requested_items = 0;
    game_state = HF_STATE_MISSION;
    Seconds(6);
    CHECK(g_pkg_request_calls == owned_m1 - 1u && g_pkg_requested_items == owned_m1 - 1u);
    CHECK(g_faction_resident[CHAOS_BOTS] && g_faction_resident[CHAOS_BUGS]);

    /* 3. Mid-mission eviction watchdog + safety guards: inconclusive PackageStateExe (-1), shared refcnt > 1, or unowned package must NEVER overwrite ref_count! */
    uint64_t saved_unloaded[3] = {
        g_preload_pkgs[CHAOS_BOTS][1],
        g_preload_pkgs[CHAOS_BOTS][2],
        g_preload_pkgs[CHAOS_BUGS][0]
    };
    AddExeResidentPkg(saved_unloaded[0], 2u, 0u); /* mid-mission evicted, Chaos-owned ref=1 */
    AddExeResidentPkg(saved_unloaded[1], 2u, 0u); /* mid-mission evicted, shared ref=2 */
    AddExeResidentPkg(saved_unloaded[2], 2u, 0u); /* mid-mission evicted, unowned */
    for (uint32_t s = 0; s < 256u; s++) {
        if (g_ref_slots[s].key == saved_unloaded[1]) g_ref_slots[s].ref = 2ULL;
    }
    for (uint32_t oi = 0; oi < g_chaos_owned_pkg_count; oi++) {
        if (g_chaos_owned_pkgs[oi] == saved_unloaded[2]) {
            g_chaos_owned_pkgs[oi] = g_chaos_owned_pkgs[--g_chaos_owned_pkg_count];
            break;
        }
    }
    /* Corrupt manager->resources pointer temporarily so PackageStateExe returns -1 (inconclusive read) */
    uintptr_t saved_res_ptr = 0;
    memcpy(&saved_res_ptr, g_exe_mgr_mem + 0x208u, 8);
    uintptr_t zero_ptr = 0;
    memcpy(g_exe_mgr_mem + 0x208u, &zero_ptr, 8);
    CHECK(PackageStateExe((uintptr_t)g_arena_exe, kArenaExeSize, saved_unloaded[0]) == -1);
    g_pkg_request_calls = 0;
    Seconds(6); /* crosses 5.0s residency watchdog while PackageStateExe == -1 */
    CHECK(g_pkg_request_calls == 0u);
    for (uint32_t s = 0; s < 256u; s++) {
        if (g_ref_slots[s].key == saved_unloaded[0]) CHECK(g_ref_slots[s].ref == 1ULL);
        if (g_ref_slots[s].key == saved_unloaded[1]) CHECK(g_ref_slots[s].ref == 2ULL);
        if (g_ref_slots[s].key == saved_unloaded[2]) CHECK(g_ref_slots[s].ref == 1ULL);
    }
    /* Restore valid resources pointer: 5.0s watchdog catches eviction and re-arms ONLY saved_unloaded[0] (Chaos-owned & ref==1)! */
    memcpy(g_exe_mgr_mem + 0x208u, &saved_res_ptr, 8);
    Seconds(6);
    CHECK(g_pkg_request_calls == 1u);
    for (uint32_t s = 0; s < 256u; s++) {
        if (g_ref_slots[s].key == saved_unloaded[1]) CHECK(g_ref_slots[s].ref == 2ULL);
        if (g_ref_slots[s].key == saved_unloaded[2]) CHECK(g_ref_slots[s].ref == 1ULL);
    }
}

static HfDifficultySettings g_mock_d11_row;
static uintptr_t g_mock_d11_ptr_override = 0u;
static unsigned g_mock_proj_calls = 0;
static uintptr_t MockDifficultyRow(uint32_t level)
{
    if (level == 11u) return g_mock_d11_ptr_override ? g_mock_d11_ptr_override : (uintptr_t)&g_mock_d11_row;
    return 0u;
}
static int MockSpawnProjectile(HfMod* m, uint32_t type, const float pos[3], const float dir[3])
{
    (void)m; (void)type; (void)pos; (void)dir;
    ++g_mock_proj_calls;
    return 1;
}
static const char* MockOrigText(uint32_t id, void* a, void* b, void* c)
{
    (void)id; (void)a; (void)b; (void)c;
    return "HELL FORGE";
}

static void TestV040UltrakillD12PoolsAndCloneWarsTheater(HfApi* api)
{
    /* 1. Havok kPhysicsPools uncap: verifies vanilla & HellForge 0.1.47 pins, setup-already-ran guard, & pin corruption fail-closed */
    InitMockGameArenas(CHAOS_NATIVE_BOTS_BIT, 11u);
    ResetMockCountersAndModState();
    for (unsigned i = 0; i < 6u; i++) {
        const UkPhysicsPoolPatch* p = &kUkPhysicsPools[i];
        memcpy(g_arena_game + p->rva, p->prefix, p->value_off);
        uint32_t v = (i & 1u) ? p->hf0147_val : p->vanilla_val;
        memcpy(g_arena_game + p->rva + p->value_off, &v, 4u);
    }
    uint64_t zero_setup = 0ULL;
    memcpy(g_arena_game + UK_PHYSICS_SETUP_RVA, &zero_setup, 8u);
    CHECK(UltrakillPatchPoolsWithOps((uintptr_t)g_arena_game, kArenaGameSize, MockRead, MockWrite) == 1);
    for (unsigned i = 0; i < 6u; i++) {
        uint32_t got = 0u;
        memcpy(&got, g_arena_game + kUkPhysicsPools[i].rva + kUkPhysicsPools[i].value_off, 4u);
        CHECK(got == kUkPhysicsPools[i].ultrakill_val);
    }
    /* Setup already ran -> returns 0 without writing */
    uint64_t done_setup = 0x12345678ULL;
    memcpy(g_arena_game + UK_PHYSICS_SETUP_RVA, &done_setup, 8u);
    CHECK(UltrakillPatchPoolsWithOps((uintptr_t)g_arena_game, kArenaGameSize, MockRead, MockWrite) == 0);
    /* Corrupted opcode byte -> fails closed with -1 */
    memcpy(g_arena_game + UK_PHYSICS_SETUP_RVA, &zero_setup, 8u);
    g_arena_game[kUkPhysicsPools[2].rva] ^= 0xFFu;
    CHECK(UltrakillPatchPoolsWithOps((uintptr_t)g_arena_game, kArenaGameSize, MockRead, MockWrite) == -1);
    g_arena_game[kUkPhysicsPools[2].rva] ^= 0xFFu;

    /* 2. O(1) 4,096-unit ChaosTracker hash table stress check (2,500 live units) */
    ChaosTracker big_tr;
    ChaosTrackerReset(&big_tr);
    for (uint32_t e = 10000u; e < 12500u; ++e) {
        CHECK(ChaosTrackerRecordSpawnAt(&big_tr, (int)(e % 3u), e, 0, 1.0) == 1);
        CHECK(ChaosTrackerHasEntity(&big_tr, e) == 1);
        CHECK(ChaosTrackerRecordSpawnAt(&big_tr, (int)(e % 3u), e, 0, 1.0) == 0);
    }
    CHECK(big_tr.total_live == 2500 && big_tr.mission_spawned == 2500);
    big_tr.units[0].active = 0u;
    big_tr.units[1249].active = 0u;
    ChaosTrackerRecount(&big_tr);
    CHECK(big_tr.total_live == 2498 && !ChaosTrackerHasEntity(&big_tr, 10000u) && ChaosTrackerHasEntity(&big_tr, 12499u));

    /* 3. Level 12 ULTRAKILL DifficultySettings row-11 mutation, HUD text override, 942m map area, & 2,000-3,000 unit Clone Wars theater */
    memset(&g_mock_d11_row, 0, sizeof g_mock_d11_row);
    g_mock_d11_row.unlock_item = 0xA0AE653Au;
    g_mock_d11_row.level = 11u;
    g_mock_d11_row.name_text = 0x55AA0011u;
    g_mock_d11_row.description_text = 0x55AA0012u;
    g_mock_d11_row.encounter_points = 1500.0f;
    g_mock_d11_row.guardforce_points = 1500.0f;
    g_mock_d11_row.spawner_cooldown = 1.0f;
    for (int i = 0; i < 4; i++) g_mock_d11_row.map_radius[i] = 0.76f;
    for (int f = 0; f < 3; f++) { g_mock_d11_row.patrol_interval[f] = 35.0f; g_mock_d11_row.patrol_cap[f] = 60u; }

    api->difficulty_row = MockDifficultyRow;
    api->spawn_projectile = MockSpawnProjectile;
    g_ultrakill.orig_text = MockOrigText;
    g_mock_proj_calls = 0;

    /* Wire Context -> LevelGenerator (lg+0x1E4 mode=3, lg+0x2D70 radius=0.76, lg+0x2D74 border=0.93) */
    uint8_t* mock_ctx = (uint8_t*)calloc(1, 0xAF000u);
    uint8_t* mock_lg = (uint8_t*)calloc(1, 0x3000u);
    void* p_ctx = mock_ctx;
    void* p_lg = mock_lg;
    uint32_t lg_mode = 3u;
    float r_init = 0.76f, r_border = 0.93f;
    memcpy(g_arena_game + UK_CONTEXT_RVA, &p_ctx, 8);
    memcpy(mock_ctx + UK_CONTEXT_LG_OFF, &p_lg, 8);
    memcpy(mock_lg + UK_LG_MODE_OFF, &lg_mode, 4);
    memcpy(mock_lg + UK_LG_PLAYABLE_RADIUS_OFF, &r_init, 4);
    memcpy(mock_lg + UK_LG_BORDER_RADIUS_OFF, &r_border, 4);

    char saved_fixture[1800], unpacked_mod[1750], host_json[1700];
    strcpy(saved_fixture, fixture);
    snprintf(unpacked_mod, sizeof unpacked_mod, "%s\\mods\\.unpacked\\hash031\\chaos-invasions", saved_fixture);
    snprintf(host_json, sizeof host_json, "%s\\mods\\clonedivers-host-options.json", saved_fixture);
    CreateDirectoryA(unpacked_mod, NULL);

    const uint64_t t0 = 134400000000000000ULL;
    CHECK(WriteTextWithMtime(
        host_json,
        "{\"format\":1,\"theaterPreset\":\"level12-ultrakill\",\"chaosIntensity\":\"clone-wars\",\"heavySeaf\":\"clone-wars\",\"fasterCorpseCleanup\":true,\"virtualTheaterBudget\":1600,\"mapRadiusPermille\":920}",
        t0));
    mock_lg[CD_LG_DIFFICULTY_OFF] = 12u;
    strcpy(fixture, unpacked_mod);
    role = HF_SESSION_SOLO;
    game_state = HF_STATE_MISSION;
    ResetMockCountersAndModState();
    subscriptions = ticks = 0;
    CHECK(hellforge_mod_init(api, (HfMod*)1) == 0);

    CHECK(g_ultrakill.d12_applied == 1);
    CHECK(g_mock_d11_row.encounter_points == 1500.0f && g_mock_d11_row.patrol_cap[0] == 24u);
    CHECK(g_mock_d11_row.major_objectives == 1u && g_mock_d11_row.optional_objectives == 5u);
    CHECK(fabsf(g_mock_d11_row.map_radius[0] - 0.685f) < 0.001f);
    CHECK(strcmp(UltrakillTextDetour(0x55AA0011u, NULL, NULL, NULL), "CLONE WARS") == 0);
    CHECK(strcmp(UltrakillTextDetour(0x55AA0012u, NULL, NULL, NULL), "12") == 0);
    CHECK(strcmp(UltrakillTextDetour(0x99999999u, NULL, NULL, NULL), "HELL FORGE") == 0);

    /* Run frames at 60 FPS -> verifies paced <= 4 SpawnRosterRow/frame, lg+0x2D70 -> 0.685f (~701 m), and 1,600+ virtual battalion units! */
    unsigned max_roster_per_frame = 0u;
    for (int f = 0; f < 180; f++) {
        unsigned before = g_roster_calls;
        Tick(self, 1.0f / 60.0f, NULL);
        unsigned delta = g_roster_calls - before;
        if (delta > max_roster_per_frame) max_roster_per_frame = delta;
    }
    float live_r = 0.0f;
    memcpy(&live_r, mock_lg + UK_LG_PLAYABLE_RADIUS_OFF, 4);
    CHECK(fabsf(live_r - 0.685f) < 0.002f);
    CHECK(max_roster_per_frame <= UK_MAX_SPAWNS_PER_FRAME);
    CHECK(g_ultrakill.multi_ring_spawns_total >= 12u);
    CHECK(g_ultrakill.virtual_units_total >= 1580u && g_ultrakill.virtual_units_total <= 2200u);

    /* Simulate EntityManager crossing governed reserve ceiling (900 live slots -> 1,148 free < 1,180 reserve): must hold real spawns! */
    for (uint32_t i = 0; i < 900u; i++) {
        uint64_t r = 0x0002ba767df856f3ull;
        uint32_t eid = 20000u + i;
        memcpy(g_emgr_mem + UK_EM_DESCRIPTORS_OFF + (size_t)i * 24u + 0u, &r, 8);
        memcpy(g_emgr_mem + UK_EM_DESCRIPTORS_OFF + (size_t)i * 24u + 8u, &eid, 4);
    }
    g_em_snap.valid = 0;
    ChaosSpawnJob test_job = {0};
    test_job.faction = CHAOS_BOTS;
    test_job.row_slot = 0u;
    test_job.flags = 0x3Cu;
    ChaosSpawnQueuePush(&g_spawn_queue, &test_job);
    unsigned before_full = g_roster_calls;
    DrainTheaterSpawnQueue((uintptr_t)g_arena_game, (uintptr_t)g_director_mem);
    CHECK(g_em_snap.valid == 1 && g_em_snap.free_slots == 1148u);
    CHECK(g_roster_calls == before_full); /* Zero EntityManager spawns when free_slots < 1,180! */

    /* Simulate transient EntityManager occupancy bulk-read failure (g_mock_fail_em_bulk_read == 1 -> g_em_snap.valid == 0
     * while CollectLivingPlayers still sees living_players == 1 and ChaosDue fires through full Tick()):
     * ALL population governors AND the due-wave scheduler (ev.constructor / bypass_hr) MUST fail closed! */
    memset(g_emgr_mem + UK_EM_DESCRIPTORS_OFF, 0, 2048u * 24u);
    float low_budget = 100.0f;
    memcpy(g_director_mem + UK_DIR_REINFORCE_BUDGET_OFF, &low_budget, 4);
    g_ultrakill.budget_refresh_s = 0.0f;
    g_ultrakill.surge_accum_s = 1.0f;
    g_ultrakill.salvo_budget_tokens = 2.0f;
    clock_state.next = clock_state.age; /* Force scheduled Chaos wave due on the very next Tick()! */
    g_em_snap.valid = 1;
    g_em_snap.captured_age = -10.0;
    g_mock_fail_em_bulk_read = 1;
    unsigned roster_before_fail = g_roster_calls;
    unsigned transport_before_fail = g_transport_calls;
    unsigned proj_before_fail = g_mock_proj_calls;
    unsigned skip_before_fail = skipped_logs;
    uint32_t q_before_fail = g_spawn_queue.count;
    for (int f = 0; f < 30; f++) {
        Tick(self, 0.5f, NULL);
    }
    float budget_after_fail = 0.0f;
    memcpy(&budget_after_fail, g_director_mem + UK_DIR_REINFORCE_BUDGET_OFF, 4);
    CHECK(g_em_snap.valid == 0);
    CHECK(g_roster_calls == roster_before_fail); /* Neither theater queue nor due-wave scheduler spawned roster units! */
    CHECK(g_transport_calls == transport_before_fail); /* Due-wave scheduler did not dispatch transport waves either! */
    CHECK(skipped_logs > skip_before_fail); /* Due-wave refused with constructor_unqualified! */
    CHECK(g_spawn_queue.count == q_before_fail); /* New platoon admission & virtual promotion blocked! */
    CHECK(fabsf(budget_after_fail - 100.0f) < 0.01f); /* Director budget NOT topped up to 900 without valid occupancy! */
    CHECK(g_mock_proj_calls == proj_before_fail); /* Virtual projectiles blocked when occupancy read fails! */
    g_mock_fail_em_bulk_read = 0;
    g_em_snap.captured_age = -10.0;

    /* Host-role transitions WITHOUT options-file edits must synchronize HfDifficultySettings row 11 immediately! */
    /* Mid-mission demotion: SOLO -> JOINER restores baseline D11 row and flushes spawn queue */
    role = HF_SESSION_JOINER;
    Tick(self, 1.0f / 60.0f, NULL);
    CHECK(g_ultrakill.d12_applied == 0);
    CHECK(fabsf(g_mock_d11_row.map_radius[0] - 0.76f) < 0.001f);
    CHECK(strcmp(UltrakillTextDetour(0x55AA0011u, NULL, NULL, NULL), "HELL FORGE") == 0);
    CHECK(g_spawn_queue.count == 0u);

    /* Mid-mission promotion: JOINER -> HOST re-applies Level 12 row without file edit */
    role = HF_SESSION_HOST;
    Tick(self, 1.0f / 60.0f, NULL);
    CHECK(g_ultrakill.d12_applied == 1);
    CHECK(fabsf(g_mock_d11_row.map_radius[0] - 0.685f) < 0.001f);
    CHECK(strcmp(UltrakillTextDetour(0x55AA0011u, NULL, NULL, NULL), "CLONE WARS") == 0);

    /* Ship-state lobby transitions: HOST -> JOINER -> SOLO also synchronize D12 row immediately */
    game_state = HF_STATE_SHIP;
    Tick(self, 1.0f / 60.0f, NULL);
    role = HF_SESSION_JOINER;
    Tick(self, 1.0f / 60.0f, NULL);
    CHECK(g_ultrakill.d12_applied == 0 && fabsf(g_mock_d11_row.map_radius[0] - 0.76f) < 0.001f);
    role = HF_SESSION_SOLO;
    Tick(self, 1.0f / 60.0f, NULL);
    CHECK(g_ultrakill.d12_applied == 1 && fabsf(g_mock_d11_row.map_radius[0] - 0.685f) < 0.001f);
    game_state = HF_STATE_MISSION;

    /* Switch back to standard mode -> restores Difficulty 11 row byte-for-byte! */
    CHECK(WriteTextWithMtime(
        host_json,
        "{\"format\":1,\"theaterPreset\":\"standard\",\"chaosIntensity\":\"standard\",\"heavySeaf\":\"standard\",\"fasterCorpseCleanup\":true}",
        t0 + 50000000ULL));
    Seconds(2);
    CHECK(g_ultrakill.d12_applied == 0);
    CHECK(g_mock_d11_row.encounter_points == 1500.0f && fabsf(g_mock_d11_row.map_radius[0] - 0.76f) < 0.001f);

    DeleteFileA(host_json);
    strcpy(fixture, saved_fixture);
    g_host_options_path[0] = 0;
    g_host_options_mtime = 0ULL;
    g_host_opts = cd_host_options_defaults();
    memset(g_arena_game + UK_CONTEXT_RVA, 0, 8u);
    free(mock_ctx);
    free(mock_lg);
}

static uintptr_t D12_MS_ABI CallD12ExtraStubWithIdx(const void* stub, uint32_t idx, uintptr_t bar_ptr)
{
    uintptr_t out_ptr = 0u;
    __asm__ __volatile__(
        "movl %[idx], %%eax\n\t"
        "movq %[bar], %%rcx\n\t"
        "callq *%[stub]\n\t"
        "movq %%rax, %[out]\n\t"
        : [out] "=r"(out_ptr)
        : [stub] "r"(stub), [idx] "r"(idx), [bar] "r"(bar_ptr)
        : "rax", "rcx", "rdx", "r8", "memory", "cc");
    return out_ptr;
}

typedef struct D12BoardExecResult {
    uint64_t rax_out;
    uint64_t rdx_out;
    uint64_t r11_out;
    uint64_t rdi_out;
    uint64_t rcx_out;
    uint64_t r8_out;
    uint64_t r9_out;
    uint64_t r10_out;
    uint32_t hit_skip;
    float xmm_after[6][4];
} D12BoardExecResult;

typedef struct D12BoardExecCtx {
    uint64_t entry;
    uint64_t stack_slot20;
    uint64_t arg38;
    uint64_t arg40;
    uint64_t init_rbx;
    uint64_t init_rsi;
    uint64_t init_r14;
    uint64_t init_r12;
    uint64_t init_rdx;
    uint64_t init_r11;
    float xmm_in[6][4];
    D12BoardExecResult* out;
} D12BoardExecCtx;

static void ExecuteEmittedBoardStub(
    const void* stub_entry,
    uint32_t kind,
    uint32_t level,
    int32_t index,
    uintptr_t war_ptr,
    uint32_t planet,
    uint32_t default_slot,
    const float xmm_init[6][4],
    D12BoardExecResult* res)
{
    memset(res, 0, sizeof *res);
    D12BoardExecCtx ctx;
    memset(&ctx, 0, sizeof ctx);
    ctx.entry = (uint64_t)(uintptr_t)stub_entry;
    ctx.stack_slot20 = 0x778899AABBCCDDE0ULL;
    ctx.arg38 = (kind == 2u) ? (uint64_t)planet : (uint64_t)war_ptr;
    ctx.arg40 = (uint64_t)planet;
    ctx.init_rbx = (uint64_t)level;
    ctx.init_rsi = (uint64_t)(uint32_t)index;
    ctx.init_r14 = (uint64_t)level;
    ctx.init_r12 = (kind == 1u) ? (uint64_t)planet : (uint64_t)war_ptr;
    ctx.init_rdx = (uint64_t)default_slot;
    ctx.init_r11 = (uint64_t)default_slot;
    memcpy(ctx.xmm_in, xmm_init, sizeof ctx.xmm_in);
    ctx.out = res;

    register const D12BoardExecCtx* r15_ctx __asm__("r15") = &ctx;
    __asm__ __volatile__(
        "pushq %%rbp\n\t"
        "subq $0x60, %%rsp\n\t"
        "movq 0x08(%%r15), %%rax\n\t"
        "movq %%rax, 0x20(%%rsp)\n\t"
        "movq 0x10(%%r15), %%rax\n\t"
        "movq %%rax, 0x38(%%rsp)\n\t"
        "movq 0x18(%%r15), %%rax\n\t"
        "movq %%rax, 0x40(%%rsp)\n\t"
        "movups 0x50(%%r15), %%xmm0\n\t"
        "movups 0x60(%%r15), %%xmm1\n\t"
        "movups 0x70(%%r15), %%xmm2\n\t"
        "movups 0x80(%%r15), %%xmm3\n\t"
        "movups 0x90(%%r15), %%xmm4\n\t"
        "movups 0xA0(%%r15), %%xmm5\n\t"
        "movq 0x20(%%r15), %%rbx\n\t"
        "movq 0x28(%%r15), %%rsi\n\t"
        "movq 0x30(%%r15), %%r14\n\t"
        "movq 0x38(%%r15), %%r12\n\t"
        "movq 0x40(%%r15), %%rdx\n\t"
        "movq 0x48(%%r15), %%r11\n\t"
        "movq $0x1122334455667788, %%rcx\n\t"
        "movq $0x2233445566778899, %%r8\n\t"
        "movq $0x33445566778899AA, %%r9\n\t"
        "movq $0x445566778899AABB, %%r10\n\t"
        "callq *0x00(%%r15)\n\t"
        "movq 0xB0(%%r15), %%r12\n\t"
        "movq %%rax, 0x00(%%r12)\n\t"
        "movq %%rdx, 0x08(%%r12)\n\t"
        "movq %%r11, 0x10(%%r12)\n\t"
        "movq %%rdi, 0x18(%%r12)\n\t"
        "movq %%rcx, 0x20(%%r12)\n\t"
        "movq %%r8,  0x28(%%r12)\n\t"
        "movq %%r9,  0x30(%%r12)\n\t"
        "movq %%r10, 0x38(%%r12)\n\t"
        "movl %%ebp, 0x40(%%r12)\n\t"
        "movups %%xmm0, 0x44(%%r12)\n\t"
        "movups %%xmm1, 0x54(%%r12)\n\t"
        "movups %%xmm2, 0x64(%%r12)\n\t"
        "movups %%xmm3, 0x74(%%r12)\n\t"
        "movups %%xmm4, 0x84(%%r12)\n\t"
        "movups %%xmm5, 0x94(%%r12)\n\t"
        "addq $0x60, %%rsp\n\t"
        "popq %%rbp\n\t"
        :
        : "r"(r15_ctx)
        : "rax", "rbx", "rcx", "rdx", "rsi", "rdi",
          "r8", "r9", "r10", "r11", "r12", "r14",
          "xmm0", "xmm1", "xmm2", "xmm3", "xmm4", "xmm5",
          "memory", "cc");
}

static int g_test_reg_race_mode = 0;
static int g_test_reg_race_fired = 0;
static HANDLE g_test_reg_worker_thread = NULL;
static volatile LONG g_test_reg_worker_started = 0;
static int g_test_reg_worker_rc = 0;
static char g_test_reg_worker_logs_dir[1700];

static DWORD WINAPI MockConcurrentRegistryReplacerThread(LPVOID param)
{
    int mode = (int)(intptr_t)param;
    InterlockedExchange(&g_test_reg_worker_started, 1);
    if (mode == 2 || mode == 3) {
        /* Cooperative atomic-replacement splice writer serialized via registered-items.json.lock */
        const char* needle = (mode == 2) ? "\"mixId\":77665544" : "\"mixId\":88776655";
        const char* entry  = (mode == 2)
            ? "{\"mixId\":77665544,\"copyOf\":54321,\"mod\":\"test.moved\",\"unit\":\"mov\"}"
            : "{\"mixId\":88776655,\"copyOf\":65432,\"mod\":\"test.stage1\",\"unit\":\"st1\"}";
        g_test_reg_worker_rc = D12RegisterRegistryItemEx(g_test_reg_worker_logs_dir, needle, entry, 0, NULL);
    } else if (mode == 4) {
        /* Lock-serialized full-snapshot atomic replacement (e.g. rewriting full registry without D12) at stage 1 */
        wchar_t wdir[MAX_PATH], wpath[MAX_PATH], wother[MAX_PATH];
        MultiByteToWideChar(CP_UTF8, 0, g_test_reg_worker_logs_dir, -1, wdir, MAX_PATH - 48);
        swprintf(wpath, MAX_PATH, L"%ls\\registered-items.json", wdir);
        swprintf(wother, MAX_PATH, L"%ls\\registered-items.json.snapshot", wdir);
        D12RegistryLock lk;
        if (!D12AcquireRegistryLock(wdir, &lk)) {
            g_test_reg_worker_rc = 0;
            return 0;
        }
        static const char kSnapshotJson[] =
            "{\"format\":1,\"items\":["
            "{\"mixId\":44332211,\"copyOf\":98765,\"mod\":\"test.snap\",\"unit\":\"snp\"},"
            "{\"kind\":\"difficulty\",\"mixId\":3369641098,\"copyOf\":2695783738,\"level\":11,\"mod\":\"skyeshade.hell-forge\",\"name\":\"hell_forge\"}"
            "]}\n";
        HANDLE hm_w = CreateFileW(wother, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        DWORD wr = 0u;
        BOOL ok = (hm_w != INVALID_HANDLE_VALUE) &&
                  WriteFile(hm_w, kSnapshotJson, (DWORD)strlen(kSnapshotJson), &wr, NULL) &&
                  wr == (DWORD)strlen(kSnapshotJson);
        if (hm_w != INVALID_HANDLE_VALUE) CloseHandle(hm_w);
        if (ok) ok = D12ReplaceFilePosixAtomic(wother, wpath);
        D12ReleaseRegistryLock(&lk);
        g_test_reg_worker_rc = ok ? 1 : 0;
    }
    return 0;
}

static void MockConcurrentRegistryWriterHook(const char* logs_folder_utf8, int stage)
{
    if (g_test_reg_race_fired) return;
    wchar_t wdir[MAX_PATH], wpath[MAX_PATH];
    MultiByteToWideChar(CP_UTF8, 0, logs_folder_utf8, -1, wdir, MAX_PATH - 48);
    swprintf(wpath, MAX_PATH, L"%ls\\registered-items.json", wdir);

    if (g_test_reg_race_mode == 1 && stage == 1) {
        /* Exact HellForge registry.c WriteRegisteredItems() pattern: in-place CREATE_ALWAYS + FILE_SHARE_READ
         * occurring ONE instruction before D12's POSIX rename! */
        g_test_reg_race_fired = 1;
        static const char kConcurrentJson[] =
            "{\"format\":1,\"items\":["
            "{\"mixId\":99887766,\"copyOf\":12345,\"mod\":\"test.weapon\",\"unit\":\"wep\"},"
            "{\"kind\":\"difficulty\",\"mixId\":3369641098,\"copyOf\":2695783738,\"level\":11,\"mod\":\"skyeshade.hell-forge\",\"name\":\"hell_forge\"}"
            "]}\n";
        HANDLE hf_w = CreateFileW(wpath, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        CHECK(hf_w != INVALID_HANDLE_VALUE);
        DWORD wr = 0u;
        CHECK(WriteFile(hf_w, kConcurrentJson, (DWORD)strlen(kConcurrentJson), &wr, NULL) != 0);
        CloseHandle(hf_w);
    } else if ((g_test_reg_race_mode == 2 && stage == 0) ||
               ((g_test_reg_race_mode == 3 || g_test_reg_race_mode == 4) && stage == 1)) {
        /* Spawn concurrent thread performing atomic registry replacement at stage 0 (mode 2) or stage 1 (modes 3 & 4) */
        g_test_reg_race_fired = 1;
        snprintf(g_test_reg_worker_logs_dir, sizeof g_test_reg_worker_logs_dir, "%s", logs_folder_utf8);
        InterlockedExchange(&g_test_reg_worker_started, 0);
        g_test_reg_worker_rc = 0;
        g_test_reg_worker_thread = CreateThread(NULL, 0u, MockConcurrentRegistryReplacerThread,
                                                (LPVOID)(intptr_t)g_test_reg_race_mode, 0u, NULL);
        CHECK(g_test_reg_worker_thread != NULL);
        while (InterlockedCompareExchange(&g_test_reg_worker_started, 0, 0) == 0) Sleep(1u);
        Sleep(15u); /* Let worker thread contend directly on registered-items.json.lock while D12 is at stage 0/1! */
    }
}

static void TestV050GenuineDifficulty12SlotAndGating(HfApi* api)
{
    /* 1. Verify D12RegisterUnlockItemInFolder splices level 12 alongside existing HellForge level 11 entry */
    char logs_dir[1700], reg_file[1760];
    snprintf(logs_dir, sizeof logs_dir, "%s\\d12-logs", fixture);
    snprintf(reg_file, sizeof reg_file, "%s\\registered-items.json", logs_dir);
    CreateDirectoryA(logs_dir, NULL);
    FILE* rf = fopen(reg_file, "wb");
    CHECK(rf != NULL);
    fputs("{\"format\":1,\"items\":[{\"kind\":\"difficulty\",\"mixId\":111111,\"copyOf\":2695783738,\"level\":11,\"mod\":\"skyeshade.hell-forge\",\"name\":\"hell_forge\"}]}\n", rf);
    fclose(rf);

    CHECK(D12RegisterUnlockItemInFolder(logs_dir) == 2);
    CHECK(D12RegisterUnlockItemInFolder(logs_dir) == 1); /* Idempotent! */
    rf = fopen(reg_file, "rb");
    CHECK(rf != NULL);
    char reg_buf[1024] = {0};
    size_t rlen = fread(reg_buf, 1, sizeof(reg_buf) - 1u, rf);
    fclose(rf);
    reg_buf[rlen] = '\0';
    CHECK(strstr(reg_buf, "\"mixId\":111111") != NULL);
    CHECK(strstr(reg_buf, "\"mixId\":3440558350,\"copyOf\":2695783738,\"level\":12") != NULL);

    /* 2. Verify D12ComputeBoardSlot across standard (<=6 regions), Super Earth (7 regions), and Cyberstan (8 regions) */
    uint8_t std_regions[6 * 0x18] = {0};
    for (uint32_t r = 0; r < 6u; ++r) {
        uint32_t p = 42u;
        memcpy(std_regions + r * 0x18u + 0u, &p, 4u);
        memcpy(std_regions + r * 0x18u + 4u, &r, 4u);
    }
    CHECK(D12ComputeBoardSlot(1u, 10u, 2, std_regions, 6u, 42u, 29u) == 29u);
    CHECK(D12ComputeBoardSlot(1u, 11u, 0, std_regions, 6u, 42u, 30u) == 100u);
    CHECK(D12ComputeBoardSlot(0u, 11u, 5, std_regions, 6u, 42u, 0u) == 108u);
    CHECK(D12ComputeBoardSlot(1u, 12u, 0, std_regions, 6u, 42u, 33u) == 90u);
    CHECK(D12ComputeBoardSlot(1u, 12u, 2, std_regions, 6u, 42u, 35u) == 92u);
    CHECK(D12ComputeBoardSlot(0u, 12u, 0, std_regions, 6u, 42u, 0u) == 93u);
    CHECK(D12ComputeBoardSlot(0u, 12u, 5, std_regions, 6u, 42u, 0u) == 98u);
    CHECK(D12ComputeBoardSlot(0u, 12u, 6, std_regions, 6u, 42u, 0u) == D12_BOARD_SKIP);

    /* 7-region Super Earth Mega Cities (regions 0..6) */
    uint8_t se_regions[7 * 0x18] = {0};
    for (uint32_t r = 0; r < 7u; ++r) {
        uint32_t p = 1u;
        memcpy(se_regions + r * 0x18u + 0u, &p, 4u);
        memcpy(se_regions + r * 0x18u + 4u, &r, 4u);
    }
    CHECK(D12ComputeBoardSlot(0u, 11u, 6, se_regions, 7u, 1u, 0u) == 109u);
    CHECK(D12ComputeBoardSlot(0u, 12u, 0, se_regions, 7u, 1u, 0u) == 31u);
    CHECK(D12ComputeBoardSlot(0u, 12u, 6, se_regions, 7u, 1u, 0u) == 91u);
    CHECK(D12ComputeBoardSlot(1u, 12u, 0, se_regions, 7u, 1u, 33u) == D12_BOARD_SKIP);

    /* 8-region Cyberstan Megafactories (regions 0..7) */
    uint8_t cy_regions[8 * 0x18] = {0};
    for (uint32_t r = 0; r < 8u; ++r) {
        uint32_t p = 77u;
        memcpy(cy_regions + r * 0x18u + 0u, &p, 4u);
        memcpy(cy_regions + r * 0x18u + 4u, &r, 4u);
    }
    CHECK(D12ComputeBoardSlot(0u, 11u, 7, cy_regions, 8u, 77u, 0u) == 100u);
    CHECK(D12ComputeBoardSlot(0u, 12u, 7, cy_regions, 8u, 77u, 0u) == 101u);
    CHECK(D12ComputeBoardSlot(1u, 12u, 0, cy_regions, 8u, 77u, 33u) == D12_BOARD_SKIP);

    /* 3. Full end-to-end D12TryInstallNativeSlot against mock HellForge d_code + 15-row difficulty table */
    uint8_t* hf_table = D12AllocNearGame((uintptr_t)g_arena_game, kArenaGameSize, 0x8000u, 0u);
    uint8_t* hf_code  = D12AllocNearGame((uintptr_t)g_arena_game, kArenaGameSize, 0x1000u, (uintptr_t)hf_table);
    uint8_t* hf_board = D12AllocNearGame((uintptr_t)g_arena_game, kArenaGameSize, 0x1000u, (uintptr_t)hf_code);
    CHECK(hf_table != NULL && hf_code != NULL && hf_board != NULL);
    memset(hf_table, 0, 0x8000u);
    memset(hf_code, 0x90, 0x1000u);

    /* Populate row 11 ("HELL FORGE") inside moved 15-row table */
    HfDifficultySettings* row11 = (HfDifficultySettings*)(hf_table + 10u * D12_ROW_BYTES);
    row11->unlock_item = 0x11111111u;
    row11->level = 11u;
    row11->name_text = 0x55AA0011u;
    row11->description_text = 0x55AA0012u;
    row11->encounter_points = 1100.0f;
    row11->guardforce_points = 900.0f;
    row11->spawner_cooldown = 14.0f;
    for (int i = 0; i < 4; ++i) row11->map_radius[i] = 0.76f;
    ((uint8_t*)row11)[0xECu] = 11u;

    /* Populate moved bands & distance after 15 rows */
    uint8_t* hf_bands = hf_table + 15u * D12_ROW_BYTES;
    uint8_t* hf_dist  = hf_bands + 11u * 8u;
    uint64_t sh_band = 0xCAFEBABE12345678ULL;
    memcpy(hf_bands + 10u * 8u, &sh_band, 8u);
    float d11_dist[3] = {0.76f, 0.76f, 0.76f};
    memcpy(hf_dist + 10u * 12u, d11_dist, 12u);

    int32_t band_disp = (int32_t)((int64_t)(uintptr_t)hf_bands - (int64_t)(uintptr_t)g_arena_game);
    int32_t dist_disp = (int32_t)((int64_t)(uintptr_t)hf_dist  - (int64_t)(uintptr_t)g_arena_game);
    static const uint8_t kMockBandPrefix[4] = {0x49, 0x8B, 0x94, 0xC6};
    memcpy(g_arena_game + D12_BAND_SITE_RVA, kMockBandPrefix, 4u);
    memcpy(g_arena_game + D12_BAND_SITE_RVA + 4u, &band_disp, 4u);
    for (size_t i = 0u; i < 7u; ++i) {
        memcpy(g_arena_game + kD12DistanceSites[i].rva, kD12DistanceSites[i].prefix, kD12DistanceSites[i].disp_at);
        memcpy(g_arena_game + kD12DistanceSites[i].rva + kD12DistanceSites[i].disp_at, &dist_disp, 4u);
    }

    /* Wire d->code ExtraWidget (+0x00), kIconStub (+0x40), companion iconChecks (+0x9D0..+0xA00), kRadiiStub (+0xC40), and kBarSites[0] -> d->code + 0x100 */
    memcpy(hf_code + 0x000u, kD12ExtraWidgetTemplate, sizeof kD12ExtraWidgetTemplate);
    static const uint8_t kMockIconHead[5]      = {0x83, 0xFE, 0x0B, 0x0F, 0x85};
    static const uint8_t kMockIconChk0[6]      = {0x83, 0x7F, 0x04, 0x0B, 0x0F, 0x85};
    static const uint8_t kMockIconChk12[7]     = {0x83, 0x7C, 0x33, 0x04, 0x0B, 0x0F, 0x85};
    static const uint8_t kMockIconChk3[7]      = {0x83, 0x7C, 0x2B, 0x04, 0x0B, 0x0F, 0x85};
    static const uint8_t kMockRadiiHead[9]     = {0x41, 0x80, 0xBD, 0xF9, 0x00, 0x00, 0x00, 0x0B, 0x75};
    memcpy(hf_code + 0x040u, kMockIconHead, sizeof kMockIconHead);
    memcpy(hf_code + 0x9D0u, kMockIconChk0, sizeof kMockIconChk0);
    memcpy(hf_code + 0x9E0u, kMockIconChk12, sizeof kMockIconChk12);
    memcpy(hf_code + 0x9F0u, kMockIconChk12, sizeof kMockIconChk12);
    memcpy(hf_code + 0xA00u, kMockIconChk3, sizeof kMockIconChk3);
    memcpy(hf_code + 0xC40u + 14u, kMockRadiiHead, sizeof kMockRadiiHead);

    D12WriteE9JumpPad(api, (uintptr_t)g_arena_game + D12_BAR_SITE0_RVA, (uintptr_t)(hf_code + 0x100u), 8u);
    D12WriteE9JumpPad(api, (uintptr_t)g_arena_game + D12_BOARD_REG_SITE_RVA, (uintptr_t)(hf_board + 0x00u), 6u);
    D12WriteE9JumpPad(api, (uintptr_t)g_arena_game + D12_BOARD_PLN_SITE_RVA, (uintptr_t)(hf_board + 0x80u), 7u);
    D12WriteE9JumpPad(api, (uintptr_t)g_arena_game + D12_BOARD_DEF_SITE_RVA, (uintptr_t)(hf_board + 0x100u), 7u);
    g_arena_game[D12_BOARD_REG_MAX_RVA] = 0xBDu;
    uint32_t eleven = 11u;
    memcpy(g_arena_game + D12_BOARD_REG_MAX_RVA + 1u, &eleven, 4u);
    memcpy(g_arena_game + 0x5E4FAFu, kD12HfInPlace16Pin, 16u);
    memcpy(g_arena_game + 0x5E503Fu, kD12HfInPlace16Pin, 16u);
    memcpy(g_arena_game + 0x17C6AA3u, kD12HfInPlace12Pin, 12u);

    memcpy(g_arena_game + D12_OBJECTIVES_RVA + 0x28u, &eleven, 4u);
    memcpy(g_arena_game + D12_OPERATIONS_RVA + 0x10u, &eleven, 4u);
    memcpy(g_arena_game + D12_MISSIONS_RVA + 0x10u, &eleven, 4u);
    for (size_t i = 0u; i < 14u; ++i) {
        memcpy(g_arena_game + kD12DifficultyBounds[i].rva, kD12DifficultyBounds[i].prefix, kD12DifficultyBounds[i].imm_at);
        memcpy(g_arena_game + kD12DifficultyBounds[i].rva + kD12DifficultyBounds[i].imm_at, &eleven, kD12DifficultyBounds[i].width);
    }

    /* Temporarily point MockDifficultyRow(11) at row11 inside hf_table */
    uintptr_t prev_d11_override = g_mock_d11_ptr_override;
    g_mock_d11_ptr_override = (uintptr_t)row11;

    CdHostOptions def_opts = cd_host_options_defaults();
    CHECK(def_opts.theater_preset == CD_THEATER_CLONE_WARS);

    /* 3a. Verify fail-closed prerequisite checks (including distance & bound opcode prefixes) AND full transactional rollback! */
    {
        /* Distance site displacement mismatch refuses before touching anything */
        int32_t bad_disp = dist_disp + 0x100;
        memcpy(g_arena_game + kD12DistanceSites[4].rva + kD12DistanceSites[4].disp_at, &bad_disp, 4u);
        D12NativeSlotState bad_s = {0};
        CHECK(D12TryInstallNativeSlot(&bad_s, api, self, (uintptr_t)g_arena_game, kArenaGameSize, &def_opts) == -1);
        CHECK(bad_s.state == -1 && bad_s.rw_mem == NULL && bad_s.rx_mem == NULL);
        memcpy(g_arena_game + kD12DistanceSites[4].rva + kD12DistanceSites[4].disp_at, &dist_disp, 4u);

        /* Distance site opcode prefix corruption (while keeping disp32 intact!) refuses before touching anything */
        g_arena_game[kD12DistanceSites[2].rva] ^= 0xFFu;
        memset(&bad_s, 0, sizeof bad_s);
        CHECK(D12TryInstallNativeSlot(&bad_s, api, self, (uintptr_t)g_arena_game, kArenaGameSize, &def_opts) == -1);
        CHECK(bad_s.state == -1 && bad_s.rw_mem == NULL && bad_s.rx_mem == NULL);
        g_arena_game[kD12DistanceSites[2].rva] ^= 0xFFu;

        /* Difficulty bound opcode prefix corruption (while keeping imm == 11 intact!) refuses before touching anything */
        g_arena_game[kD12DifficultyBounds[3].rva] ^= 0xFFu;
        memset(&bad_s, 0, sizeof bad_s);
        CHECK(D12TryInstallNativeSlot(&bad_s, api, self, (uintptr_t)g_arena_game, kArenaGameSize, &def_opts) == -1);
        CHECK(bad_s.state == -1 && bad_s.rw_mem == NULL && bad_s.rx_mem == NULL);
        g_arena_game[kD12DifficultyBounds[3].rva] ^= 0xFFu;

        /* Corrupted companion stub slots (including leading opcode/ModRM/SIB bytes 0..2 at +0x9D0..+0xA00!)
         * MUST fail closed before touching anything! */
        hf_code[0x9D0u] ^= 0xFFu;
        memset(&bad_s, 0, sizeof bad_s);
        CHECK(D12TryInstallNativeSlot(&bad_s, api, self, (uintptr_t)g_arena_game, kArenaGameSize, &def_opts) == -1);
        CHECK(bad_s.state == -1 && bad_s.rw_mem == NULL && bad_s.rx_mem == NULL);
        hf_code[0x9D0u] ^= 0xFFu;

        static const uint32_t kChkSlots[3] = {0x9E0u, 0x9F0u, 0xA00u};
        for (size_t si = 0u; si < 3u; ++si) {
            for (size_t bi = 0u; bi < 3u; ++bi) {
                hf_code[kChkSlots[si] + bi] ^= 0xFFu;
                memset(&bad_s, 0, sizeof bad_s);
                CHECK(D12TryInstallNativeSlot(&bad_s, api, self, (uintptr_t)g_arena_game, kArenaGameSize, &def_opts) == -1);
                CHECK(bad_s.state == -1 && bad_s.rw_mem == NULL && bad_s.rx_mem == NULL);
                hf_code[kChkSlots[si] + bi] ^= 0xFFu;
            }
        }

        /* Injected partial write failures at step 4 (mid distance-site redirect) and step 22 (mid kD12DifficultyBounds)
         * MUST roll back EVERY prior patch byte-for-byte and leave all bounds == 11! */
        int fail_points[2] = {4, 22};
        for (int fp = 0; fp < 2; ++fp) {
            D12NativeSlotState rb_s = {0};
            g_mock_fail_write_countdown = fail_points[fp];
            CHECK(D12TryInstallNativeSlot(&rb_s, api, self, (uintptr_t)g_arena_game, kArenaGameSize, &def_opts) == -1);
            g_mock_fail_write_countdown = -1;
            CHECK(rb_s.state == -1 && rb_s.rw_mem == NULL && rb_s.rx_mem == NULL && rb_s.row12 == NULL);

            int32_t chk_band = 0, chk_dist4 = 0;
            memcpy(&chk_band, g_arena_game + D12_BAND_SITE_RVA + 4u, 4u);
            memcpy(&chk_dist4, g_arena_game + kD12DistanceSites[4].rva + kD12DistanceSites[4].disp_at, 4u);
            CHECK(chk_band == band_disp && chk_dist4 == dist_disp);
            CHECK(hf_code[0x000u] == 0x52u && hf_code[0x040u + 4u] == 0x85u && hf_code[0xC40u + 22u] == 0x75u);
            CHECK(memcmp(g_arena_game + 0x5E4FAFu, kD12HfInPlace16Pin, 16u) == 0);
            for (size_t bi = 0u; bi < 14u; ++bi) {
                uint32_t bval = 0u;
                memcpy(&bval, g_arena_game + kD12DifficultyBounds[bi].rva + kD12DifficultyBounds[bi].imm_at, kD12DifficultyBounds[bi].width);
                CHECK(bval == 11u);
            }
            HfDifficultySettings* r12_after = (HfDifficultySettings*)((uint8_t*)row11 + D12_ROW_BYTES);
            CHECK(r12_after->level == 0u);
        }

        /* Post-write readback mismatch on E9 jump pad (0x5E4FAF):
         *   Case A: write mutates 0x5E4FAF -> readback fails -> rollback succeeds & verifies -> memory freed cleanly.
         *   Case B: write mutates 0x5E4FAF -> readback fails AND rollback writes also fail -> RW/RX pages MUST be RETAINED (never freed while referenced)! */
        {
            D12NativeSlotState rb_ok = {0};
            g_mock_corrupt_readback_addr = (uintptr_t)g_arena_game + 0x5E4FAFu;
            g_mock_corrupt_readback_once = 2;
            g_mock_fail_all_writes_after_readback = 0;
            CHECK(D12TryInstallNativeSlot(&rb_ok, api, self, (uintptr_t)g_arena_game, kArenaGameSize, &def_opts) == -1);
            g_mock_corrupt_readback_addr = 0u;
            g_mock_corrupt_readback_once = 0;
            CHECK(rb_ok.state == -1 && rb_ok.rw_mem == NULL && rb_ok.rx_mem == NULL);
            CHECK(memcmp(g_arena_game + 0x5E4FAFu, kD12HfInPlace16Pin, 16u) == 0);

            uint8_t saved_row12_zero[D12_ROW_BYTES];
            memcpy(saved_row12_zero, (const uint8_t*)row11 + D12_ROW_BYTES, D12_ROW_BYTES);
            D12NativeSlotState rb_retain = {0};
            g_mock_corrupt_readback_addr = (uintptr_t)g_arena_game + 0x5E4FAFu;
            g_mock_corrupt_readback_once = 2;
            g_mock_fail_all_writes_after_readback = 1;
            g_mock_writing_active = 0;
            CHECK(D12TryInstallNativeSlot(&rb_retain, api, self, (uintptr_t)g_arena_game, kArenaGameSize, &def_opts) == -1);
            g_mock_corrupt_readback_addr = 0u;
            g_mock_corrupt_readback_once = 0;
            g_mock_fail_all_writes_after_readback = 0;
            g_mock_writing_active = 0;
            /* Because rollback writes failed while 0x5E4FAF still jumps into rx_mem, rw_mem & rx_mem MUST be retained! */
            CHECK(rb_retain.state == -1 && rb_retain.rw_mem != NULL && rb_retain.rx_mem != NULL);
            CHECK(g_arena_game[0x5E4FAFu] == 0xE9u);

            /* Manually restore fixture memory and free retained test allocations before proceeding */
            memcpy(g_arena_game + D12_BAND_SITE_RVA + 4u, &band_disp, 4u);
            for (size_t di = 0u; di < 7u; ++di) {
                memcpy(g_arena_game + kD12DistanceSites[di].rva + kD12DistanceSites[di].disp_at, &dist_disp, 4u);
            }
            memcpy(hf_code + 0x000u, kD12ExtraWidgetTemplate, sizeof kD12ExtraWidgetTemplate);
            memcpy(g_arena_game + 0x5E4FAFu, kD12HfInPlace16Pin, 16u);
            memcpy((uint8_t*)row11 + D12_ROW_BYTES, saved_row12_zero, D12_ROW_BYTES);
            VirtualFree(rb_retain.rw_mem, 0, MEM_RELEASE);
            VirtualFree(rb_retain.rx_mem, 0, MEM_RELEASE);
        }
    }

    D12NativeSlotState d12s = {0};
    CHECK(D12TryInstallNativeSlot(&d12s, api, self, (uintptr_t)g_arena_game, kArenaGameSize, &def_opts) == 1);
    CHECK(d12s.state == 1 && d12s.row12 == (HfDifficultySettings*)((uint8_t*)row11 + D12_ROW_BYTES));

    /* Verify Row 11 ("HELL FORGE") is 100% UNTOUCHED while Row 12 ("CLONE WARS") is populated with 1-primary / 5-optional! */
    CHECK(row11->level == 11u && fabsf(row11->encounter_points - 1100.0f) < 0.01f && fabsf(row11->map_radius[0] - 0.76f) < 0.001f);
    CHECK(d12s.row12->level == 12u && d12s.row12->unlock_item == D12_UNLOCK_ITEM_ID);
    CHECK(d12s.row12->name_text == D12_NAME_TEXT_ID && d12s.row12->description_text == D12_NUM_TEXT_ID);
    CHECK(((const uint8_t*)d12s.row12)[0xECu] == 11u);
    CHECK(d12s.row12->major_objectives == 1u && d12s.row12->optional_objectives == 5u);
    CHECK(fabsf(d12s.row12->encounter_points - 1500.0f) < 0.01f && fabsf(d12s.row12->map_radius[0] - 0.685f) < 0.001f);

    /* Verify kIconStub, companion iconChecks (+0x9D0..+0xA00), & kRadiiStub flipped jne (0x85/0x75) -> jb (0x82/0x72), and bounds/ranges widened 11 -> 12 */
    CHECK(hf_code[0x040u + 4u] == 0x82u);
    CHECK(hf_code[0x9D0u + 5u] == 0x82u);
    CHECK(hf_code[0x9E0u + 6u] == 0x82u);
    CHECK(hf_code[0x9F0u + 6u] == 0x82u);
    CHECK(hf_code[0xA00u + 6u] == 0x82u);
    CHECK(hf_code[0xC40u + 22u] == 0x72u);
    uint32_t obj_hi = 0u, op_hi = 0u, mis_hi = 0u, max_bound = 0u;
    memcpy(&obj_hi, g_arena_game + D12_OBJECTIVES_RVA + 0x28u, 4u);
    memcpy(&op_hi, g_arena_game + D12_OPERATIONS_RVA + 0x10u, 4u);
    memcpy(&mis_hi, g_arena_game + D12_MISSIONS_RVA + 0x10u, 4u);
    memcpy(&max_bound, g_arena_game + kD12DifficultyBounds[0].rva + kD12DifficultyBounds[0].imm_at, 4u);
    CHECK(obj_hi == 12u && op_hi == 12u && mis_hi == 12u && max_bound == 12u);

    /* Verify UltrakillTextDetour serves D12 text on ALL roles (including Joiner) while keeping D11 as "HELL FORGE",
     * and serves "LEVEL 12: STORMTROOPER" in EmpireDivers vs "LEVEL 12: CLONE WARS" in Clonedivers / Regular! */
    g_d12_slot.state = 1;
    role = HF_SESSION_JOINER;
    CHECK(strcmp(UltrakillTextDetour(D12_NAME_TEXT_ID, NULL, NULL, NULL), "CLONE WARS") == 0);
    CHECK(strcmp(UltrakillTextDetour(D12_NUM_TEXT_ID, NULL, NULL, NULL), "12") == 0);
    CHECK(strcmp(UltrakillTextDetour(0x55AA0011u, NULL, NULL, NULL), "HELL FORGE") == 0);

    /* Explicit activeMode="empire" in CdHostOptions -> "STORMTROOPER" on both Joiner & Host */
    {
        CdHostOptions parsed_emp;
        const char* emp_json = "{\"format\":1,\"theaterPreset\":\"clone-wars\",\"chaosIntensity\":\"total-war\",\"heavySeaf\":\"constant\",\"activeMode\":\"empire\",\"fasterCorpseCleanup\":true}";
        CHECK(cd_parse_host_options_json(emp_json, strlen(emp_json), &parsed_emp) == 1);
        CHECK(parsed_emp.active_mode == CD_MODE_EMPIRE);
        CHECK(cd_effective_chaos_for_difficulty(&parsed_emp, 10) == CD_CHAOS_TOTAL_WAR);
        CHECK(cd_effective_chaos_for_difficulty(&parsed_emp, 12) == CD_CHAOS_CLONE_WARS);
        CHECK(cd_effective_heavy_seaf_for_difficulty(&parsed_emp, 10) == CD_HEAVY_SEAF_CONSTANT);
        CHECK(cd_effective_heavy_seaf_for_difficulty(&parsed_emp, 12) == CD_HEAVY_SEAF_CLONE_WARS);

        CdHostOptions bad_mode;
        const char* bad_json = "{\"format\":1,\"activeMode\":\"rebel-alliance\",\"fasterCorpseCleanup\":true}";
        CHECK(cd_parse_host_options_json(bad_json, strlen(bad_json), &bad_mode) == -1);

        g_host_opts = parsed_emp;
        RefreshActivePackMode(0);
        CHECK(strcmp(UltrakillTextDetour(D12_NAME_TEXT_ID, NULL, NULL, NULL), "STORMTROOPER") == 0);

        /* Forced level12-ultrakill displays "ULTRAKILL" */
        g_host_opts.theater_preset = CD_THEATER_LEVEL12_ULTRAKILL;
        CHECK(strcmp(UltrakillTextDetour(D12_NAME_TEXT_ID, NULL, NULL, NULL), "ULTRAKILL") == 0);

        /* hellforge-mods.json auto-detection (including stray disabled demo Empire pack + enabled r54 Empire pack) */
        CdActiveMode det_mode = CD_MODE_AUTO;
        const char* hf_emp_mods =
            "{\"format\":1,\"disabled\":[\"pack.b7b312b3-d764-4c7e-a287-3ce7e6b65851\",\"pack.ef3458f5-f409-4f24-8933-05fdc10a2101\"],"
            "\"order\":[\"clonedivers.chaos-invasions\",\"pack.b7b312b3-d764-4c7e-a287-3ce7e6b65851\",\"pack.ef3458f5-f409-4f24-8933-05fdc10a0061\"]}";
        CHECK(cd_parse_hellforge_mods_mode(hf_emp_mods, strlen(hf_emp_mods), &det_mode) == 1 && det_mode == CD_MODE_EMPIRE);

        const char* hf_clones_mods =
            "{\"format\":1,\"disabled\":[\"pack.ef3458f5-f409-4f24-8933-05fdc10a0061\"],"
            "\"order\":[\"clonedivers.chaos-invasions\",\"pack.b7b312b3-d764-4c7e-a287-3ce7e6b65851\",\"pack.ef3458f5-f409-4f24-8933-05fdc10a0061\"]}";
        CHECK(cd_parse_hellforge_mods_mode(hf_clones_mods, strlen(hf_clones_mods), &det_mode) == 1 && det_mode == CD_MODE_CLONES);

        const char* hf_reg_mods =
            "{\"format\":1,\"disabled\":[\"pack.ef3458f5-f409-4f24-8933-05fdc10a0061\",\"pack.b7b312b3-d764-4c7e-a287-3ce7e6b65851\"],"
            "\"order\":[\"clonedivers.chaos-invasions\",\"pack.b7b312b3-d764-4c7e-a287-3ce7e6b65851\",\"pack.ef3458f5-f409-4f24-8933-05fdc10a0061\"]}";
        CHECK(cd_parse_hellforge_mods_mode(hf_reg_mods, strlen(hf_reg_mods), &det_mode) == 1 && det_mode == CD_MODE_REGULAR);

        g_host_opts = cd_host_options_defaults();
        RefreshActivePackMode(0);
        CHECK(strcmp(UltrakillTextDetour(D12_NAME_TEXT_ID, NULL, NULL, NULL), "CLONE WARS") == 0);
    }
    g_d12_slot.state = 0;

    /* 3b. Execute the actual PAGE_EXECUTE_READ machine-code stubs on the CPU (d12_extra + all 3 D12EmitBoardStub kinds) */
    uintptr_t w10_a = CallD12ExtraStubWithIdx(d12s.rx_mem + 0x080u, 10u, 0xAAAABBBB0001ULL);
    uintptr_t w11_a = CallD12ExtraStubWithIdx(d12s.rx_mem + 0x080u, 11u, 0xAAAABBBB0001ULL);
    uintptr_t w11_b = CallD12ExtraStubWithIdx(d12s.rx_mem + 0x080u, 11u, 0xAAAABBBB0002ULL);
    uintptr_t w11_a_again = CallD12ExtraStubWithIdx(d12s.rx_mem + 0x080u, 11u, 0xAAAABBBB0001ULL);
    uintptr_t expected_w11_base = (uintptr_t)d12s.rw_mem + D12_BAR_SLOTS * 8u;
    CHECK(w10_a != w11_a);
    CHECK(w11_a == expected_w11_base);
    CHECK(w11_b == expected_w11_base + D12_WIDGET_BYTES);
    CHECK(w11_a_again == expected_w11_base);

    uint8_t* exec_page = (uint8_t*)VirtualAlloc(NULL, 0x1000u, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    uint8_t* mock_war = (uint8_t*)calloc(1, 0x50000u);
    CHECK(exec_page != NULL && mock_war != NULL);
    memset(exec_page, 0xCC, 0x1000u);

    /* Wrapper at +0x000..0x060: save caller rbp, clear ebp=0, jmp stub (because game.dll jumps, not calls, into stub!)
     * Back pad at +0x500: xor ebp, ebp; pop rbp; ret (0x31, 0xED, 0x5D, 0xC3) -> hit_skip == 0
     * Skip pad at +0x540: pop rbp; mov ebp, 1; ret (0x5D, 0xBD, 0x01,0x00,0x00,0x00, 0xC3) ->Wait: keep caller rbp intact?
     * Even simpler: ExecuteEmittedBoardStub calls wrapper (+0x000/+0x020/+0x040) which pushes rbp, sets rsp frame so
     * [entry_rsp+0x20..0x40] match ExecuteEmittedBoardStub's stack! Wait: if ExecuteEmittedBoardStub does `jmp` via
     * pushing back/skip return or if back_pad simply `ret`s because `ExecuteEmittedBoardStub` `callq`ed stub?
     * Check stack offsets in D12EmitBoardStub:
     *   entry_rsp: [entry_rsp+0x38] -> [rbp+0x40] (after `push rbp`), [entry_rsp+0x40] -> [rbp+0x48]!
     *   When ExecuteEmittedBoardStub executes `callq *%[entry]`, `callq` pushes an 8-byte return address!
     *   Wait: in ExecuteEmittedBoardStub above, before `callq`:
     *     `0x20(%rsp)` is at `caller_rsp + 0x20`. After `callq` pushes 8 bytes, `entry_rsp = caller_rsp - 8`!
     *     So at `entry_rsp`:
     *       `[entry_rsp + 0x00]` = return address pushed by `callq`!
     *       `[entry_rsp + 0x28]` = `caller_rsp + 0x20`!
     *   Whereas in `game.dll`, `game.dll` reaches stub via `E9 jmp` (NO return address pushed!).
     *   So if wrapper at `+0x000` is entered via `callq`, wrapper's `[rsp]` IS the return address to `ExecuteEmittedBoardStub`!
     *   If wrapper pops that return address into a scratch slot (e.g. `pop rax; mov [rip+saved_ret], rax; mov rax, 3; jmp stub`),
     *   then `entry_rsp == caller_rsp` EXACTLY as in `game.dll`'s `E9 jmp`!
     *   And `back_pad` (`mov dword ptr [rip+hit_flag], 0; jmp [rip+saved_ret]`) and
     *   `skip_pad` (`mov dword ptr [rip+hit_flag], 1; jmp [rip+saved_ret]`) return cleanly without touching ANY register or stack slot! */
    uint8_t* rw_scratch = (uint8_t*)VirtualAlloc(NULL, 0x1000u, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    CHECK(rw_scratch != NULL);
    /* Wait: rip-relative needs near alloc; we can use D12AllocNearGame for both exec_page and rw_scratch! */
    VirtualFree(exec_page, 0, MEM_RELEASE);
    VirtualFree(rw_scratch, 0, MEM_RELEASE);
    exec_page = D12AllocNearGame((uintptr_t)g_arena_game, kArenaGameSize, 0x1000u, 0u);
    rw_scratch = D12AllocNearGame((uintptr_t)g_arena_game, kArenaGameSize, 0x1000u, (uintptr_t)exec_page);
    CHECK(exec_page != NULL && rw_scratch != NULL);
    memset(exec_page, 0xCC, 0x1000u);
    memset(rw_scratch, 0, 0x1000u);

    uintptr_t fn_va = (uintptr_t)(void*)&D12BoardSlotFromSite;
    uintptr_t back_va = (uintptr_t)(exec_page + 0x500u);
    uintptr_t skip_va = (uintptr_t)(exec_page + 0x540u);
    size_t sz0 = D12EmitBoardStub(exec_page + 0x200u, 0u, (uintptr_t)(exec_page + 0x200u), fn_va, back_va, skip_va);
    size_t sz1 = D12EmitBoardStub(exec_page + 0x2C0u, 1u, (uintptr_t)(exec_page + 0x2C0u), fn_va, back_va, skip_va);
    size_t sz2 = D12EmitBoardStub(exec_page + 0x380u, 2u, (uintptr_t)(exec_page + 0x380u), fn_va, back_va, skip_va);
    CHECK(sz0 == 174u && sz1 == 169u && sz2 == 170u);

    /* Emit 3 entry trampolines at +0x000, +0x040, +0x080:
     *   pop rax                        ; 58  (consumes callq return address so RSP == game.dll E9-jmp RSP!)
     *   mov [rip + saved_ret], rax     ; 48 89 05 disp32
     *   mov eax, 3                     ; B8 03 00 00 00 (arm RAX=3 crash canary!)
     *   jmp stub_k                     ; E9 disp32 */
    for (uint32_t k = 0u; k < 3u; ++k) {
        uint8_t* tr = exec_page + k * 0x40u;
        uintptr_t stub_target = (uintptr_t)(exec_page + 0x200u + k * 0xC0u);
        tr[0] = 0x58u;
        tr[1] = 0x48u; tr[2] = 0x89u; tr[3] = 0x05u;
        int32_t disp_ret = (int32_t)((int64_t)(uintptr_t)(rw_scratch + 0x00u) - (int64_t)(uintptr_t)(tr + 8u));
        memcpy(tr + 4u, &disp_ret, 4u);
        tr[8] = 0xB8u; tr[9] = 0x03u; tr[10] = 0x00u; tr[11] = 0x00u; tr[12] = 0x00u;
        tr[13] = 0xE9u;
        int32_t disp_stub = (int32_t)((int64_t)stub_target - (int64_t)(uintptr_t)(tr + 18u));
        memcpy(tr + 14u, &disp_stub, 4u);
    }

    /* back_va (+0x500): mov ebp, 0; jmp qword ptr [rip + saved_ret] */
    exec_page[0x500u] = 0xBDu; memset(exec_page + 0x501u, 0, 4u);
    exec_page[0x505u] = 0xFFu; exec_page[0x506u] = 0x25u;
    int32_t disp_back_ret = (int32_t)((int64_t)(uintptr_t)(rw_scratch + 0x00u) - (int64_t)(uintptr_t)(exec_page + 0x50Bu));
    memcpy(exec_page + 0x507u, &disp_back_ret, 4u);

    /* skip_va (+0x540): mov ebp, 1; jmp qword ptr [rip + saved_ret] */
    exec_page[0x540u] = 0xBDu; exec_page[0x541u] = 1u; memset(exec_page + 0x542u, 0, 3u);
    exec_page[0x545u] = 0xFFu; exec_page[0x546u] = 0x25u;
    int32_t disp_skip_ret = (int32_t)((int64_t)(uintptr_t)(rw_scratch + 0x00u) - (int64_t)(uintptr_t)(exec_page + 0x54Bu));
    memcpy(exec_page + 0x547u, &disp_skip_ret, 4u);

    DWORD old_exec_prot = 0;
    CHECK(VirtualProtect(exec_page, 0x1000u, PAGE_EXECUTE_READ, &old_exec_prot) != 0);
    FlushInstructionCache(GetCurrentProcess(), exec_page, 0x1000u);

    /* Populate mock_war so both kind==0 (base+0x23018 / +0x26018) and kind==1,2 (ptr-0x23008 / -0x20008) work:
     *Note: war_ptr for kind==0 is mock_war; war_ptr for kind==1,2 is mock_war + 0x46020 -> regions at +0x23018, count at +0x26018! */
    memcpy(mock_war + 0x23018u, std_regions, sizeof std_regions);
    uint32_t mock_reg_cnt = 6u;
    memcpy(mock_war + 0x26018u, &mock_reg_cnt, 4u);

    const float xmm_canary[6][4] = {
        { 1.25f,  -2.50f,  3.75f,  -4.50f},
        {11.25f, -12.50f, 13.75f, -14.50f},
        {21.25f, -22.50f, 23.75f, -24.50f},
        {31.25f, -32.50f, 33.75f, -34.50f},
        {41.25f, -42.50f, 43.75f, -44.50f},
        {51.25f, -52.50f, 53.75f, -54.50f},
    };
    D12BoardExecResult bres;

    /* Kind 0 (Region): D12 index 4 on 6-region planet 42 -> slot 97 */
    ExecuteEmittedBoardStub(exec_page + 0x000u, 0u, 12u, 4, (uintptr_t)mock_war, 42u, 7u, xmm_canary, &bres);
    CHECK(bres.hit_skip == 0u && bres.rax_out == 97u && bres.rdx_out == 97u && bres.rdi_out == 97u * 0x5Cu);
    CHECK(bres.rcx_out == 0x1122334455667788ULL && bres.r8_out == 0x2233445566778899ULL &&
          bres.r9_out == 0x33445566778899AAULL && bres.r10_out == 0x445566778899AABBULL);
    CHECK(memcmp(bres.xmm_after, xmm_canary, sizeof xmm_canary) == 0);

    /* Kind 0 (Region): D12 index 6 out-of-range -> D12_BOARD_SKIP -> jumps to skip_va and loads rax = [rsp+0x20]! */
    ExecuteEmittedBoardStub(exec_page + 0x000u, 0u, 12u, 6, (uintptr_t)mock_war, 42u, 7u, xmm_canary, &bres);
    CHECK(bres.hit_skip == 1u && bres.rax_out == 0x778899AABBCCDDE0ULL);
    CHECK(memcmp(bres.xmm_after, xmm_canary, sizeof xmm_canary) == 0);

    /* Kind 1 (Planet campaign — exact site that crashed before fix!): D12 index 2 -> slot 92, D11 index 1 -> slot 101 */
    ExecuteEmittedBoardStub(exec_page + 0x040u, 1u, 12u, 2, (uintptr_t)(mock_war + 0x46020u), 42u, 35u, xmm_canary, &bres);
    CHECK(bres.hit_skip == 0u && bres.rax_out == 92u && bres.r11_out == 92u && bres.rdi_out == 92u * 0x5Cu);
    CHECK(memcmp(bres.xmm_after, xmm_canary, sizeof xmm_canary) == 0);

    ExecuteEmittedBoardStub(exec_page + 0x040u, 1u, 11u, 1, (uintptr_t)(mock_war + 0x46020u), 42u, 31u, xmm_canary, &bres);
    CHECK(bres.hit_skip == 0u && bres.rax_out == 101u && bres.r11_out == 101u && bres.rdi_out == 101u * 0x5Cu);
    CHECK(memcmp(bres.xmm_after, xmm_canary, sizeof xmm_canary) == 0);

    /* Kind 2 (Planet defence): D12 index 0 -> slot 90; D10 passthrough -> default_slot 27 */
    ExecuteEmittedBoardStub(exec_page + 0x080u, 2u, 12u, 0, (uintptr_t)(mock_war + 0x46020u), 42u, 33u, xmm_canary, &bres);
    CHECK(bres.hit_skip == 0u && bres.rax_out == 90u && bres.r11_out == 90u && bres.rdi_out == 90u * 0x5Cu);
    CHECK(memcmp(bres.xmm_after, xmm_canary, sizeof xmm_canary) == 0);

    ExecuteEmittedBoardStub(exec_page + 0x080u, 2u, 10u, 0, (uintptr_t)(mock_war + 0x46020u), 42u, 27u, xmm_canary, &bres);
    CHECK(bres.hit_skip == 0u && bres.rax_out == 27u && bres.r11_out == 27u && bres.rdi_out == 27u * 0x5Cu);
    CHECK(memcmp(bres.xmm_after, xmm_canary, sizeof xmm_canary) == 0);

    VirtualFree(exec_page, 0, MEM_RELEASE);
    VirtualFree(rw_scratch, 0, MEM_RELEASE);
    free(mock_war);

    /* 4. Verify live difficulty gating of default "clone-wars": D11 stays Standard (cooldown=150, cap=12), D12 activates Full Clone Wars! */
    role = HF_SESSION_SOLO;
    game_state = HF_STATE_MISSION;
    InitMockGameArenas(CHAOS_NATIVE_BOTS_BIT, 11u);
    ResetMockCountersAndModState();
    uint8_t* mock_ctx = (uint8_t*)calloc(1, 0xAF000u);
    uint8_t* mock_lg  = (uint8_t*)calloc(1, 0x3000u);
    void* p_ctx = mock_ctx;
    void* p_lg  = mock_lg;
    memcpy(g_arena_game + UK_CONTEXT_RVA, &p_ctx, 8u);
    memcpy(mock_ctx + UK_CONTEXT_LG_OFF, &p_lg, 8u);

    char unpacked_mod[1750], host_json[1700], saved_fixture[1800];
    strcpy(saved_fixture, fixture);
    snprintf(unpacked_mod, sizeof unpacked_mod, "%s\\mods\\.unpacked\\hash031\\chaos-invasions", saved_fixture);
    snprintf(host_json, sizeof host_json, "%s\\mods\\clonedivers-host-options.json", saved_fixture);
    CHECK(WriteTextWithMtime(
        host_json,
        "{\"format\":1,\"theaterPreset\":\"clone-wars\",\"chaosIntensity\":\"clone-wars\",\"heavySeaf\":\"clone-wars\",\"fasterCorpseCleanup\":true}",
        134500000000000000ULL));
    strcpy(fixture, unpacked_mod);

    mock_lg[CD_LG_DIFFICULTY_OFF] = 11u; /* Playing Difficulty 11 ("HELL FORGE") */
    subscriptions = ticks = 0;
    CHECK(hellforge_mod_init(api, (HfMod*)1) == 0);
    Tick(self, 1.0f / 60.0f, NULL);
    CHECK(config.cooldown == 150 && config.total_cap == 12 && g_ultrakill.virtual_units_total == 0u);

    mock_lg[CD_LG_DIFFICULTY_OFF] = 12u; /* Playing Difficulty 12 ("CLONE WARS")! */
    for (int f = 0; f < 30; ++f) Tick(self, 1.0f / 60.0f, NULL);
    CHECK(config.cooldown == 10 && config.grace == 7 && config.total_cap == 620 && g_ultrakill.virtual_units_total >= 1900u);

    /* 5. Verify full startup sequence: HF_EVENT_GAME_LOADED -> StratagemsInstalled overwrites registered-items.json ->
     *    Ship Tick / HF_EVENT_ENTITIES_READY automatically restores mixId=3440558350 while keeping stratagems + D11! */
    {
        snprintf(g_mock_logs_dir, sizeof g_mock_logs_dir, "%s", logs_dir);
        api->logs_folder = MockLogsFolder;
        DeleteFileA(reg_file);
        game_state = HF_STATE_SHIP;
        subscriptions = ticks = 0;
        g_mock_on_game_loaded = NULL;
        g_mock_on_entities_ready = NULL;
        CHECK(hellforge_mod_init(api, (HfMod*)1) == 0);
        CHECK(g_mock_on_game_loaded != NULL && g_mock_on_entities_ready != NULL);
        g_mock_on_game_loaded(self, HF_EVENT_GAME_LOADED, NULL);

        /* Simulate HellForge's StratagemsInstalled (WriteRegisteredItems) overwriting registered-items.json 7s later */
        Sleep(20);
        FILE* ow = fopen(reg_file, "wb");
        CHECK(ow != NULL);
        fputs("{\"format\":1,\"items\":["
              "{\"kind\":\"stratagem\",\"mixId\":4179658842,\"mod\":\"local.heavy-seaf-presence\",\"name\":\"seaf_reinforcement\"},"
              "{\"kind\":\"difficulty\",\"mixId\":3369641098,\"copyOf\":2695783738,\"level\":11,\"mod\":\"skyeshade.hell-forge\",\"name\":\"hell_forge\"}"
              "]}\n", ow);
        fclose(ow);

        /* 0.30s on Ship -> g_d12_sync_poll detects changed size/mtime and re-splices mixId=3440558350 automatically! */
        Tick(self, 0.30f, NULL);
        FILE* vf = fopen(reg_file, "rb");
        CHECK(vf != NULL);
        char vbuf[2048] = {0};
        size_t vlen = fread(vbuf, 1, sizeof(vbuf) - 1u, vf);
        fclose(vf);
        vbuf[vlen] = '\0';
        CHECK(strstr(vbuf, "\"mixId\":4179658842") != NULL);
        CHECK(strstr(vbuf, "\"mixId\":3369641098") != NULL);
        CHECK(strstr(vbuf, "\"mixId\":3440558350,\"copyOf\":2695783738,\"level\":12") != NULL);

        /* Simulate a second rewrite of identical byte length or later mtime right before HF_EVENT_ENTITIES_READY */
        ow = fopen(reg_file, "wb");
        CHECK(ow != NULL);
        fputs("{\"format\":1,\"items\":[{\"kind\":\"difficulty\",\"mixId\":3369641098,\"copyOf\":2695783738,\"level\":11,\"mod\":\"skyeshade.hell-forge\",\"name\":\"hell_forge\"}]}\n", ow);
        fclose(ow);
        g_mock_on_entities_ready(self, HF_EVENT_ENTITIES_READY, NULL);
        vf = fopen(reg_file, "rb");
        CHECK(vf != NULL);
        memset(vbuf, 0, sizeof vbuf);
        vlen = fread(vbuf, 1, sizeof(vbuf) - 1u, vf);
        fclose(vf);
        vbuf[vlen] = '\0';
        CHECK(strstr(vbuf, "\"mixId\":3369641098") != NULL);
        CHECK(strstr(vbuf, "\"mixId\":3440558350,\"copyOf\":2695783738,\"level\":12") != NULL);

        /* Simulate an atomic MoveFileExW replacement with IDENTICAL byte length & IDENTICAL forced mtime:
         * NTFS file index check MUST still detect the replacement on a normal non-forced poll (`force_read=0`)! */
        {
            char same_len_tmp[1780];
            snprintf(same_len_tmp, sizeof same_len_tmp, "%s\\registered-items.json.samelen", logs_dir);
            BY_HANDLE_FILE_INFORMATION before_info;
            wchar_t wreg[MAX_PATH], wsame[MAX_PATH];
            MultiByteToWideChar(CP_UTF8, 0, reg_file, -1, wreg, MAX_PATH);
            MultiByteToWideChar(CP_UTF8, 0, same_len_tmp, -1, wsame, MAX_PATH);
            CHECK(D12QueryPathHandleInfo(wreg, &before_info) == 1);

            char* pad_json = (char*)malloc((size_t)before_info.nFileSizeLow + 1u);
            CHECK(pad_json != NULL);
            const char* head_no_d12 = "{\"format\":1,\"items\":[{\"kind\":\"difficulty\",\"mixId\":3369641098,\"copyOf\":2695783738,\"level\":11,\"mod\":\"skyeshade.hell-forge\",\"name\":\"hell_forge\"}]}";
            size_t hlen = strlen(head_no_d12);
            CHECK(before_info.nFileSizeLow > hlen + 2u);
            memcpy(pad_json, head_no_d12, hlen);
            memset(pad_json + hlen, ' ', (size_t)before_info.nFileSizeLow - hlen - 1u);
            pad_json[before_info.nFileSizeLow - 1u] = '\n';

            FILE* sf = fopen(same_len_tmp, "wb");
            CHECK(sf != NULL);
            CHECK(fwrite(pad_json, 1, before_info.nFileSizeLow, sf) == before_info.nFileSizeLow);
            fclose(sf);
            free(pad_json);

            HANDLE hSet = CreateFileW(wsame, FILE_WRITE_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                                      NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
            CHECK(hSet != INVALID_HANDLE_VALUE);
            CHECK(SetFileTime(hSet, NULL, NULL, &before_info.ftLastWriteTime) != 0);
            CloseHandle(hSet);

            CHECK(MoveFileExW(wsame, wreg, MOVEFILE_REPLACE_EXISTING) != 0);
            CHECK(D12SyncUnlockItemInFolder(&g_d12_slot, logs_dir, 0) == 2);
            vf = fopen(reg_file, "rb");
            CHECK(vf != NULL);
            memset(vbuf, 0, sizeof vbuf);
            vlen = fread(vbuf, 1, sizeof(vbuf) - 1u, vf);
            fclose(vf);
            vbuf[vlen] = '\0';
            CHECK(strstr(vbuf, "\"mixId\":3440558350") != NULL);

            /* Review #3 P1a: Exclusive open failure (ERROR_SHARING_VIOLATION) on an existing registry MUST
             * fail closed (return 0) and NEVER fall through as an absent registry or overwrite existing items! */
            ow = fopen(reg_file, "wb");
            CHECK(ow != NULL);
            fputs("{\"format\":1,\"items\":[{\"mixId\":55443322,\"copyOf\":111,\"mod\":\"keep.me\",\"unit\":\"u\"}]}\n", ow);
            fclose(ow);
            HANDLE hExcl = CreateFileW(wreg, GENERIC_READ | GENERIC_WRITE, 0 /* exclusive! */,
                                       NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
            CHECK(hExcl != INVALID_HANDLE_VALUE);
            CHECK(D12RegisterUnlockItemInFolder(logs_dir) == 0);
            CloseHandle(hExcl);
            vf = fopen(reg_file, "rb");
            CHECK(vf != NULL);
            memset(vbuf, 0, sizeof vbuf);
            vlen = fread(vbuf, 1, sizeof(vbuf) - 1u, vf);
            fclose(vf);
            vbuf[vlen] = '\0';
            CHECK(strstr(vbuf, "\"mixId\":55443322") != NULL);
            CHECK(strstr(vbuf, "\"mixId\":3440558350") == NULL);

            /* Review #3 P1b: Concurrent HellForge WriteRegisteredItems() (CREATE_ALWAYS + FILE_SHARE_READ)
             * firing ONE instruction before MoveFileExW (stage == 1) MUST NOT lose mixId=99887766! */
            ow = fopen(reg_file, "wb");
            CHECK(ow != NULL);
            fputs("{\"format\":1,\"items\":[{\"kind\":\"difficulty\",\"mixId\":3369641098,\"copyOf\":2695783738,\"level\":11,\"mod\":\"skyeshade.hell-forge\",\"name\":\"hell_forge\"}]}\n", ow);
            fclose(ow);
            g_test_reg_race_mode = 1;
            g_test_reg_race_fired = 0;
            g_d12_test_registry_hook = MockConcurrentRegistryWriterHook;
            CHECK(D12RegisterUnlockItemInFolder(logs_dir) == 2);
            g_d12_test_registry_hook = NULL;
            CHECK(g_test_reg_race_fired == 1);
            vf = fopen(reg_file, "rb");
            CHECK(vf != NULL);
            memset(vbuf, 0, sizeof vbuf);
            vlen = fread(vbuf, 1, sizeof(vbuf) - 1u, vf);
            fclose(vf);
            vbuf[vlen] = '\0';
            CHECK(strstr(vbuf, "\"mixId\":99887766") != NULL);
            CHECK(strstr(vbuf, "\"mixId\":3369641098") != NULL);
            CHECK(strstr(vbuf, "\"mixId\":3440558350") != NULL);

            /* Review #3 P1c & Review #5 P1: Concurrent atomic-replacement registry writers at BOTH stage 0 (mode 2)
             * AND stage 1 (modes 3 & 4) serialize via non-recursive LockFileEx on registered-items.json.lock with zero lost items! */
            ow = fopen(reg_file, "wb");
            CHECK(ow != NULL);
            fputs("{\"format\":1,\"items\":[{\"kind\":\"difficulty\",\"mixId\":3369641098,\"copyOf\":2695783738,\"level\":11,\"mod\":\"skyeshade.hell-forge\",\"name\":\"hell_forge\"}]}\n", ow);
            fclose(ow);
            g_test_reg_race_mode = 2;
            g_test_reg_race_fired = 0;
            g_test_reg_worker_thread = NULL;
            g_d12_test_registry_hook = MockConcurrentRegistryWriterHook;
            CHECK(D12RegisterUnlockItemInFolder(logs_dir) == 2);
            g_d12_test_registry_hook = NULL;
            CHECK(g_test_reg_race_fired == 1 && g_test_reg_worker_thread != NULL);
            CHECK(WaitForSingleObject(g_test_reg_worker_thread, 5000u) == WAIT_OBJECT_0);
            CloseHandle(g_test_reg_worker_thread);
            g_test_reg_worker_thread = NULL;
            CHECK(g_test_reg_worker_rc == 2);
            vf = fopen(reg_file, "rb");
            CHECK(vf != NULL);
            memset(vbuf, 0, sizeof vbuf);
            vlen = fread(vbuf, 1, sizeof(vbuf) - 1u, vf);
            fclose(vf);
            vbuf[vlen] = '\0';
            CHECK(strstr(vbuf, "\"mixId\":77665544") != NULL);
            CHECK(strstr(vbuf, "\"mixId\":3369641098") != NULL);
            CHECK(strstr(vbuf, "\"mixId\":3440558350") != NULL);

            /* Stage 1 (mode 3): concurrent splice-and-POSIX-replace writer arriving right before D12ReplaceFilePosixAtomic */
            ow = fopen(reg_file, "wb");
            CHECK(ow != NULL);
            fputs("{\"format\":1,\"items\":[{\"kind\":\"difficulty\",\"mixId\":3369641098,\"copyOf\":2695783738,\"level\":11,\"mod\":\"skyeshade.hell-forge\",\"name\":\"hell_forge\"}]}\n", ow);
            fclose(ow);
            g_test_reg_race_mode = 3;
            g_test_reg_race_fired = 0;
            g_d12_test_registry_hook = MockConcurrentRegistryWriterHook;
            CHECK(D12RegisterUnlockItemInFolder(logs_dir) == 2);
            g_d12_test_registry_hook = NULL;
            CHECK(g_test_reg_race_fired == 1 && g_test_reg_worker_thread != NULL);
            CHECK(WaitForSingleObject(g_test_reg_worker_thread, 5000u) == WAIT_OBJECT_0);
            CloseHandle(g_test_reg_worker_thread);
            g_test_reg_worker_thread = NULL;
            CHECK(g_test_reg_worker_rc == 2);
            vf = fopen(reg_file, "rb");
            CHECK(vf != NULL);
            memset(vbuf, 0, sizeof vbuf);
            vlen = fread(vbuf, 1, sizeof(vbuf) - 1u, vf);
            fclose(vf);
            vbuf[vlen] = '\0';
            CHECK(strstr(vbuf, "\"mixId\":88776655") != NULL);
            CHECK(strstr(vbuf, "\"mixId\":3369641098") != NULL);
            CHECK(strstr(vbuf, "\"mixId\":3440558350") != NULL);

            /* Stage 1 (mode 4): concurrent full-snapshot atomic replacement arriving right before D12ReplaceFilePosixAtomic
             * is NEVER overwritten by D12's stale snapshot, and D12SyncUnlockItemInFolder re-splices 3440558350 cleanly! */
            ow = fopen(reg_file, "wb");
            CHECK(ow != NULL);
            fputs("{\"format\":1,\"items\":[{\"kind\":\"difficulty\",\"mixId\":3369641098,\"copyOf\":2695783738,\"level\":11,\"mod\":\"skyeshade.hell-forge\",\"name\":\"hell_forge\"}]}\n", ow);
            fclose(ow);
            g_test_reg_race_mode = 4;
            g_test_reg_race_fired = 0;
            g_d12_test_registry_hook = MockConcurrentRegistryWriterHook;
            CHECK(D12SyncUnlockItemInFolder(&g_d12_slot, logs_dir, 1) == 2);
            g_d12_test_registry_hook = NULL;
            CHECK(g_test_reg_race_fired == 1 && g_test_reg_worker_thread != NULL);
            CHECK(WaitForSingleObject(g_test_reg_worker_thread, 5000u) == WAIT_OBJECT_0);
            CloseHandle(g_test_reg_worker_thread);
            g_test_reg_worker_thread = NULL;
            CHECK(g_test_reg_worker_rc == 1);
            CHECK(D12SyncUnlockItemInFolder(&g_d12_slot, logs_dir, 0) == 2);
            vf = fopen(reg_file, "rb");
            CHECK(vf != NULL);
            memset(vbuf, 0, sizeof vbuf);
            vlen = fread(vbuf, 1, sizeof(vbuf) - 1u, vf);
            fclose(vf);
            vbuf[vlen] = '\0';
            CHECK(strstr(vbuf, "\"mixId\":44332211") != NULL);
            CHECK(strstr(vbuf, "\"mixId\":3369641098") != NULL);
            CHECK(strstr(vbuf, "\"mixId\":3440558350") != NULL);

            /* Review #4 P1: Forced unsupported POSIX rename (`FileRenameInfoEx` unavailable) MUST fail closed
             * without closing `hIn` prior to replacement and without overwriting existing registry items! */
            ow = fopen(reg_file, "wb");
            CHECK(ow != NULL);
            fputs("{\"format\":1,\"items\":[{\"mixId\":66554433,\"copyOf\":222,\"mod\":\"posix.req\",\"unit\":\"p\"}]}\n", ow);
            fclose(ow);
            g_d12_test_force_posix_unsupported = 1;
            CHECK(D12RegisterUnlockItemInFolder(logs_dir) == 0);
            g_d12_test_force_posix_unsupported = 0;
            vf = fopen(reg_file, "rb");
            CHECK(vf != NULL);
            memset(vbuf, 0, sizeof vbuf);
            vlen = fread(vbuf, 1, sizeof(vbuf) - 1u, vf);
            fclose(vf);
            vbuf[vlen] = '\0';
            CHECK(strstr(vbuf, "\"mixId\":66554433") != NULL);
            CHECK(strstr(vbuf, "\"mixId\":3440558350") == NULL);
        }

        api->logs_folder = NULL;
        g_mock_logs_dir[0] = '\0';
        game_state = HF_STATE_MISSION;
    }

    /* 6. Review #4 P2: Consecutive missions WITHOUT `clonedivers-host-options.json` (`!g_host_opts.loaded_from_file`):
     *    D12 -> Ship -> D5 MUST restore `options.ini` (including custom settings & `enabled=0`), AND preserve explicit
     *    in-session `ChaosInvasions.values` edits across D5 -> Ship -> D12 (multi-second poll) -> Ship -> D5! */
    {
        DeleteFileA(host_json);
        char ini_path[1820];
        snprintf(ini_path, sizeof ini_path, "%s\\options.ini", unpacked_mod);

        /* 6a. Custom `options.ini` (enabled=1, cooldown=180, cap=14, squad=5, budget=40), no host-options JSON */
        FILE* inf = fopen(ini_path, "wb");
        CHECK(inf != NULL);
        fputs("enabled=1\ncooldown_seconds=180\nlanding_grace_seconds=50\ntotal_cap=14\nfaction_cap=8\nsquad_size=5\nmission_budget=40\n", inf);
        fclose(inf);

        strcpy(fixture, unpacked_mod);
        role = HF_SESSION_SOLO;
        game_state = HF_STATE_MISSION;
        mock_lg[CD_LG_DIFFICULTY_OFF] = 5u;
        ResetMockCountersAndModState();
        subscriptions = ticks = 0;
        CHECK(hellforge_mod_init(api, (HfMod*)1) == 0);
        CHECK(g_host_opts.loaded_from_file == 0);
        Tick(self, 1.0f / 60.0f, NULL);
        CHECK(config.enabled == 1 && config.cooldown == 180 && config.total_cap == 14 &&
              config.squad_size == 5 && config.mission_budget == 40);

        /* Deploy to D12 -> activates Clone Wars D12 config */
        game_state = HF_STATE_SHIP;
        Tick(self, 1.0f / 60.0f, NULL);
        game_state = HF_STATE_MISSION;
        mock_lg[CD_LG_DIFFICULTY_OFF] = 12u;
        for (int f = 0; f < 10; ++f) Tick(self, 1.0f / 60.0f, NULL);
        CHECK(config.enabled == 1 && config.cooldown == 10 && config.grace == 7 && config.total_cap == 620 &&
              config.squad_size == 24 && config.mission_budget == 25000);

        /* Return D12 -> Ship -> D5: MUST restore custom `options.ini` settings (NOT retain D12 settings)! */
        game_state = HF_STATE_SHIP;
        Tick(self, 1.0f / 60.0f, NULL);
        CHECK(config.enabled == 1 && config.cooldown == 180 && config.total_cap == 14 &&
              config.squad_size == 5 && config.mission_budget == 40);
        game_state = HF_STATE_MISSION;
        mock_lg[CD_LG_DIFFICULTY_OFF] = 5u;
        for (int f = 0; f < 10; ++f) Tick(self, 1.0f / 60.0f, NULL);
        CHECK(config.enabled == 1 && config.cooldown == 180 && config.total_cap == 14 &&
              config.squad_size == 5 && config.mission_budget == 40);

        /* Explicit session edit via `ChaosInvasions.values` on D5 -> D12 (multi-second poll!) -> Ship -> D5 */
        char session_edit[512];
        snprintf(session_edit, sizeof session_edit,
                 "session=%lu\nenabled=1\ncooldown_seconds=95\nlanding_grace_seconds=35\ntotal_cap=16\n"
                 "faction_cap=10\nsquad_size=5\nmission_budget=48\ndroid_weight=110\nsquid_weight=70\nbug_weight=90\n"
                 "air_runs_enabled=1\natte_walker_support=1\n",
                 token);
        CHECK(WriteTextWithMtime(bridge, session_edit, 134600000000000000ULL));
        Seconds(2);
        CHECK(config.cooldown == 95 && config.total_cap == 16 && config.squad_size == 5 && config.mission_budget == 48);

        /* Transition D5 -> Ship -> D12 and tick 3 full seconds (past options_poll!) so bridge poll runs on D12 */
        game_state = HF_STATE_SHIP;
        Tick(self, 1.0f / 60.0f, NULL);
        game_state = HF_STATE_MISSION;
        mock_lg[CD_LG_DIFFICULTY_OFF] = 12u;
        Seconds(3);
        CHECK(config.cooldown == 10 && config.total_cap == 620 && config.mission_budget == 25000);

        /* Transition D12 -> Ship -> D5: MUST restore explicit session settings (95 / 16 / 5 / 48)! */
        game_state = HF_STATE_SHIP;
        Tick(self, 1.0f / 60.0f, NULL);
        CHECK(config.cooldown == 95 && config.total_cap == 16 && config.squad_size == 5 && config.mission_budget == 48);
        game_state = HF_STATE_MISSION;
        mock_lg[CD_LG_DIFFICULTY_OFF] = 5u;
        Seconds(2);
        CHECK(config.cooldown == 95 && config.total_cap == 16 && config.squad_size == 5 && config.mission_budget == 48);

        /* 6b. `options.ini` with `enabled=0` and no host-options JSON: D12 -> Ship -> D5 restores `enabled=0`! */
        inf = fopen(ini_path, "wb");
        CHECK(inf != NULL);
        fputs("enabled=0\ncooldown_seconds=200\n", inf);
        fclose(inf);
        ResetMockCountersAndModState();
        subscriptions = ticks = 0;
        mock_lg[CD_LG_DIFFICULTY_OFF] = 12u;
        CHECK(hellforge_mod_init(api, (HfMod*)1) == 0);
        CHECK(config.enabled == 0 && config.cooldown == 200);
        for (int f = 0; f < 5; ++f) Tick(self, 1.0f / 60.0f, NULL);
        CHECK(config.enabled == 1 && config.cooldown == 10);
        game_state = HF_STATE_SHIP;
        Tick(self, 1.0f / 60.0f, NULL);
        game_state = HF_STATE_MISSION;
        mock_lg[CD_LG_DIFFICULTY_OFF] = 5u;
        Tick(self, 1.0f / 60.0f, NULL);
        CHECK(config.enabled == 0 && config.cooldown == 200);

        DeleteFileA(ini_path);
    }

    DeleteFileA(host_json);
    DeleteFileA(reg_file);
    {
        char lock_file[1780];
        snprintf(lock_file, sizeof lock_file, "%s\\registered-items.json.lock", logs_dir);
        DeleteFileA(lock_file);
    }
    RemoveDirectoryA(logs_dir);
    strcpy(fixture, saved_fixture);
    g_host_options_path[0] = 0;
    g_host_options_mtime = 0ULL;
    g_host_opts = cd_host_options_defaults();
    config = ChaosDefaults();
    g_ini_config = config;
    g_has_session_config = 0;
    g_d12_config_active = 0;
    Publish();
    g_mock_d11_ptr_override = prev_d11_override;
    memset(g_arena_game + UK_CONTEXT_RVA, 0, 8u);
    free(mock_ctx);
    free(mock_lg);
    if (d12s.rw_mem) VirtualFree(d12s.rw_mem, 0, MEM_RELEASE);
    if (d12s.rx_mem) VirtualFree(d12s.rx_mem, 0, MEM_RELEASE);
    VirtualFree(hf_board, 0, MEM_RELEASE);
    VirtualFree(hf_code, 0, MEM_RELEASE);
    VirtualFree(hf_table, 0, MEM_RELEASE);
}

static void TestV230OvernightDeliverables(HfApi* api)
{
    /* 1. Co-op UltrakillTargetRealAiCap high density (no 420 slash!) & <= 620 GwNav::Bot Director valve */
    CdHostOptions ho = cd_host_options_defaults();
    CHECK(UltrakillTargetRealAiCap(&ho, 0) == 610);
    CHECK(UltrakillTargetRealAiCap(&ho, 1) == 600);
    ho.target_active_units = 900;
    CHECK(UltrakillTargetRealAiCap(&ho, 0) == (int)UK_MAX_WALKING_AI_CAP);
    CHECK(UltrakillTargetRealAiCap(&ho, 1) == (int)UK_MAX_WALKING_AI_CAP);

    /* 2. Verify D12_SELECTION_END_RVA (0x175784F, HellForge MixMission 2-primary hook) stays == 11 (0x75 jne)
     *    while D12 row gets 1-primary / 5-optional (<= 14 HUD/map markers < DIF_MARKER_SLOTS = 16)! */
    {
        InitMockGameArenas(CHAOS_NATIVE_BOTS_BIT, 11u);
        ResetMockCountersAndModState();
        uint8_t* mix_stub = D12AllocNearGame((uintptr_t)g_arena_game, kArenaGameSize, 0x1000u, 0u);
        CHECK(mix_stub != NULL);
        memset(mix_stub, 0x90, 0x1000u);
        static const uint8_t kMixCheck[5] = {0x83, 0x79, 0x10, 0x0B, 0x75}; /* cmp dword [rcx+0x10], 11; jne */
        memcpy(mix_stub + 19u, kMixCheck, sizeof kMixCheck);
        CHECK(D12WriteE9JumpPad(api, (uintptr_t)g_arena_game + D12_SELECTION_END_RVA, (uintptr_t)mix_stub, 7u) == 1);

        HfDifficultySettings base11 = {0};
        base11.level = 11u;
        base11.major_objectives = 1u;
        base11.optional_objectives = 8u;
        for (int i = 0; i < 4; ++i) base11.map_radius[i] = 0.76f;
        HfDifficultySettings row12_test = {0};
        CdHostOptions cw_opts = cd_host_options_defaults();
        UltrakillApplyDifficultyRow12(&row12_test, (const uint8_t*)&base11, &cw_opts);
        CHECK(row12_test.major_objectives == 1u && row12_test.optional_objectives == 5u);
        CHECK(fabsf(row12_test.map_radius[0] - 0.685f) < 0.001f);
        CHECK(mix_stub[19u + 4u] == 0x75u); /* D11 MixMission check remains strictly == 11! */
        VirtualFree(mix_stub, 0, MEM_RELEASE);
    }

    /* 3. End-to-end D12 Clone Wars simulation:
     *    - PREPARE_MISSION preloading + 7s grace
     *    - Tight <= 580/620 walking-AI Director patrol/reinforce valve
     *    - Outer cohesive SWARM clusters (18-32 units at 95-185 m) & multi-faction foreign rotation
     *    - > 260 m straggler recycling via RemoveEntity (0xFDC310)
     *    - 140-380 m virtual laser salvos
     *    - Periodic friendly Eagle Storm (30u) + enemy gunship strafing runs
     *    - Autonomous friendly Republic AT-TE walker (EXO-49 0xC2D449ECF7FACAB1) on D12 Clones + clean Empire/Ship teardown! */
    InitMockGameArenas(CHAOS_NATIVE_BOTS_BIT, 12u);
    ResetMockCountersAndModState();
    memset(&g_mock_d11_row, 0, sizeof g_mock_d11_row);
    g_mock_d11_row.level = 11u;
    g_mock_d11_row.major_objectives = 1u;
    g_mock_d11_row.optional_objectives = 8u;
    for (int i = 0; i < 4; ++i) g_mock_d11_row.map_radius[i] = 0.76f;
    for (int f = 0; f < 3; ++f) {
        g_mock_d11_row.patrol_cap[f] = 24u;
        g_mock_d11_row.patrol_interval[f] = 30.0f;
    }
    api->difficulty_row = MockDifficultyRow;
    api->spawn_projectile = MockSpawnProjectile;

    uint8_t* mock_ctx = (uint8_t*)calloc(1, 0xAF000u);
    uint8_t* mock_lg  = (uint8_t*)calloc(1, 0x3000u);
    void* p_ctx = mock_ctx;
    void* p_lg  = mock_lg;
    uint32_t lg_mode = 3u;
    float r_init = 0.76f, r_border = 0.93f;
    memcpy(g_arena_game + UK_CONTEXT_RVA, &p_ctx, 8u);
    memcpy(mock_ctx + UK_CONTEXT_LG_OFF, &p_lg, 8u);
    memcpy(mock_lg + UK_LG_MODE_OFF, &lg_mode, 4u);
    memcpy(mock_lg + UK_LG_PLAYABLE_RADIUS_OFF, &r_init, 4u);
    memcpy(mock_lg + UK_LG_BORDER_RADIUS_OFF, &r_border, 4u);
    mock_lg[CD_LG_DIFFICULTY_OFF] = 12u;

    role = HF_SESSION_SOLO;
    game_state = HF_STATE_PREPARE_MISSION;
    subscriptions = ticks = 0u;
    CHECK(hellforge_mod_init(api, (HfMod*)1) == 0);
    g_d12_slot.state = 1;
    g_d12_slot.row12 = &g_mock_d11_row;
    memcpy(g_d12_slot.row12_baseline, &g_mock_d11_row, sizeof g_mock_d11_row);
    D12PopulateRow12(&g_d12_slot, &g_host_opts);

    /* Pre-stream foreign packages (Squids + Bugs) during Hellpod drop (HF_STATE_PREPARE_MISSION) */
    for (int f = 0; f < 420; ++f) Tick(self, 1.0f / 60.0f, NULL);
    CHECK(g_faction_resident[CHAOS_SQUIDS] && g_faction_resident[CHAOS_BUGS]);

    /* Touch down in HF_STATE_MISSION -> 7s grace, autonomous AT-TE deploys, air runs fire, both foreign factions rotate! */
    game_state = HF_STATE_MISSION;
    g_mock_proj_calls = 0u;
    uint32_t squids_enqueued = 0u, bugs_enqueued = 0u;
    for (int sec = 0; sec < 45; ++sec) {
        for (int f = 0; f < 10; ++f) {
            Tick(self, 0.1f, NULL);
            for (uint32_t q = 0; q < g_spawn_queue.count; ++q) {
                uint8_t fac = g_spawn_queue.jobs[(g_spawn_queue.head + q) % UK_SPAWN_QUEUE_CAP].faction;
                if (fac == CHAOS_SQUIDS) ++squids_enqueued;
                else if (fac == CHAOS_BUGS) ++bugs_enqueued;
            }
        }
    }
    CHECK(squids_enqueued > 0u && bugs_enqueued > 0u); /* Both off-planet factions dropped consistently! */
    CHECK(g_ultrakill.friendly_air_runs_total >= 1u && g_activate_beacon_calls >= 1u &&
          g_last_beacon_type == HF_STRATAGEM_EAGLE_STRAFING_RUN);
    CHECK(g_ultrakill.enemy_air_runs_total >= 1u);
    CHECK(g_atte.active == 1 && g_atte.entity != 0u && g_last_spawned_resource == CHAOS_ENTITY_EXO49_ATTE);
    CHECK(g_ultrakill.atte_salvos_total >= 1u);
    CHECK(g_ultrakill.salvos_fired_total >= 1u);

    /* Verify straggler > 260 m from player (100, 200, 10) is recycled via RemoveEntity (0xFDC310) */
    uint32_t far_eid = 30015u;
    uint32_t fslot = far_eid % 32u;
    g_tm_buckets[fslot].k = far_eid;
    g_tm_buckets[fslot].v = fslot;
    float far_pos[3] = {420.0f, 200.0f, 10.0f}; /* 320 m > 260 m */
    memcpy(g_tm_data[fslot] + 0x2E0u, far_pos, 12u);
    CHECK(ChaosTrackerRecordSpawnAt(&g_tracker, CHAOS_BUGS, far_eid, 0, 0.0) == 1);
    unsigned rem_before = g_remove_entity_calls;
    Seconds(2);
    CHECK(g_remove_entity_calls > rem_before && !ChaosTrackerHasEntity(&g_tracker, far_eid));

    /* Verify Director patrol/reinforce valve closes at walking_ai >= 580 and hard cap blocks spawns at 620 */
    float init_reinf = 500.0f;
    memcpy(g_director_mem + UK_DIR_REINFORCE_BUDGET_OFF, &init_reinf, 4u);
    /* Keep slot 0 as player avatar 101; populate slots 1..620 with walking troopers and 621..670 with corpses/Pelicans */
    uint64_t av_res = 0x4D1C334D294DFA97ull;
    uint32_t av_eid = 101u;
    memcpy(g_emgr_mem + UK_EM_DESCRIPTORS_OFF + 0u, &av_res, 8u);
    memcpy(g_emgr_mem + UK_EM_DESCRIPTORS_OFF + 8u, &av_eid, 4u);
    for (uint32_t i = 1u; i <= 620u; ++i) {
        uint64_t r = 0x0002ba767df856f3ull; /* Walking trooper */
        uint32_t e = 50000u + i;
        memcpy(g_emgr_mem + UK_EM_DESCRIPTORS_OFF + (size_t)i * 24u + 0u, &r, 8u);
        memcpy(g_emgr_mem + UK_EM_DESCRIPTORS_OFF + (size_t)i * 24u + 8u, &e, 4u);
    }
    /* Add 50 corpse wrappers + Pelicans: must NOT count as walking_ai! */
    for (uint32_t i = 621u; i <= 670u; ++i) {
        uint64_t r = (i & 1u) ? 0x62928899DAC02979ull : 0x3EC790806D0B0540ull;
        uint32_t e = 50000u + i;
        memcpy(g_emgr_mem + UK_EM_DESCRIPTORS_OFF + (size_t)i * 24u + 0u, &r, 8u);
        memcpy(g_emgr_mem + UK_EM_DESCRIPTORS_OFF + (size_t)i * 24u + 8u, &e, 4u);
    }
    g_em_snap.valid = 0;
    ChaosRefreshEmSnapshot((uintptr_t)g_arena_game, clock_state.age + 10.0);
    CHECK(g_em_snap.live_slots == 671u && g_em_snap.walking_ai_slots == 620u);
    UltrakillSyncMissionTheater((uintptr_t)g_arena_game, (uintptr_t)g_director_mem, 0.1f);
    float clamped_reinf = -1.0f;
    memcpy(&clamped_reinf, g_director_mem + UK_DIR_REINFORCE_BUDGET_OFF, 4u);
    CHECK(clamped_reinf == 0.0f && g_d12_slot.row12->patrol_cap[0] == 0u);
    ChaosSpawnJob cap_job = {0};
    cap_job.faction = CHAOS_BUGS; cap_job.row_slot = 0u; cap_job.flags = 0x24u;
    ChaosSpawnQueuePush(&g_spawn_queue, &cap_job);
    unsigned r_before_cap = g_roster_calls;
    DrainTheaterSpawnQueue((uintptr_t)g_arena_game, (uintptr_t)g_director_mem);
    CHECK(g_roster_calls == r_before_cap); /* Zero spawns admitted at walking_ai == 620! */

    /* Switching to Empire mode despawns Clones AT-TE immediately; Ship transition releases ALL owned packages (0 leaks)! */
    InterlockedExchange(&g_empire_mode_active, 1L);
    Tick(self, 0.1f, NULL);
    CHECK(g_atte.active == 0 && g_atte.entity == 0u);
    InterlockedExchange(&g_empire_mode_active, 0L);

    g_pkg_double_free_errors = 0u;
    game_state = HF_STATE_SHIP;
    Tick(self, 0.2f, NULL);
    CHECK(g_chaos_owned_pkg_count == 0u && g_pkg_double_free_errors == 0u);
    for (uint32_t s = 0; s < 256u; ++s) {
        if (g_ref_slots[s].key != 0ULL) CHECK(g_ref_slots[s].ref == 0ULL);
    }

    g_d12_slot.state = 0;
    g_d12_slot.row12 = NULL;
    memset(g_arena_game + UK_CONTEXT_RVA, 0, 8u);
    free(mock_ctx);
    free(mock_lg);
    InitMockGameArenas(CHAOS_NATIVE_BUGS_BIT, 5u);
    ResetMockCountersAndModState();
    subscriptions = ticks = 0u;
    CHECK(hellforge_mod_init(api, (HfMod*)1) == 0);
}

static void TestV052HellForge05RelocatedEmAndPools(HfApi* api)
{
    InitMockGameArenas(CHAOS_NATIVE_BOTS_BIT, 11u);
    ResetMockCountersAndModState();

    /* 1. Verify SDK 20 handshake */
    api->version = 20u;
    subscriptions = ticks = 0u;
    CHECK(hellforge_mod_init(api, (HfMod*)1) == 0);

    /* 2. Verify Engine 0.1.58 physics pool values (0x10000 ragdoll_actors, 0x800 ragdolls) patch cleanly */
    uintptr_t gbase = (uintptr_t)g_arena_game;
    uint64_t zero_setup = 0ULL;
    memcpy(g_arena_game + UK_PHYSICS_SETUP_RVA, &zero_setup, sizeof zero_setup);
    for (unsigned i = 0; i < 6u; ++i) {
        const UkPhysicsPoolPatch* p = &kUkPhysicsPools[i];
        memcpy(g_arena_game + p->rva, p->prefix, p->value_off);
        memcpy(g_arena_game + p->rva + p->value_off, &p->hf0158_val, 4u);
    }
    CHECK(UltrakillPatchPoolsWithOps(gbase, kArenaGameSize, MockRead, MockWrite) == 1);
    for (unsigned i = 0; i < 6u; ++i) {
        const UkPhysicsPoolPatch* p = &kUkPhysicsPools[i];
        uint32_t after = 0u;
        memcpy(&after, g_arena_game + p->rva + p->value_off, 4u);
        CHECK(after == p->ultrakill_val);
    }

    /* 3. Verify relocated 4,096-record EntityManager (negative & positive offsets), GwNav reserve scaling (3,228),
     *    and player avatar resolution at descriptor slot 3,000 (> 2,048). */
    uint8_t* big_block = (uint8_t*)calloc(1, 0x1200000u);
    CHECK(big_block != NULL);
    uint8_t* em_root = big_block + 0x100000u; /* records relocated at -0x80000 (-524,288, multiple of 24? align!) */
    int32_t neg_off = -((int32_t)(24 * 16384)); /* -393,216: exact multiple of 24 and 8 */
    uint8_t* reloc_recs = em_root + neg_off;
    void* p_em = em_root;
    memcpy(g_arena_game + CHAOS_ENTITIES_RVA, &p_em, 8u);
    uint32_t inv_ent = 0xFFFFFFFFu;
    memcpy(g_arena_game + UK_INVALID_ENTITY_RVA, &inv_ent, 4u);

    /* Populate the 4 SDK-20 moved-EntityManager witness sites */
    int32_t neg_off8 = neg_off / 8;
    uint32_t cap4096 = 4096u;
    uint8_t s_rec8[7] = {0x48u, 0x8Du, 0x80u, 0, 0, 0, 0};
    uint8_t s_rec1[7] = {0x49u, 0x8Du, 0x85u, 0, 0, 0, 0};
    uint8_t s_cnt1[7] = {0x8Bu, 0xD0u, 0xBAu, 0, 0, 0, 0};
    uint8_t s_cnt2[7] = {0xF3u, 0x00u, 0x3Du, 0, 0, 0, 0};
    memcpy(s_rec8 + 3u, &neg_off8, 4u);
    memcpy(s_rec1 + 3u, &neg_off, 4u);
    memcpy(s_cnt1 + 3u, &cap4096, 4u);
    memcpy(s_cnt2 + 3u, &cap4096, 4u);
    memcpy(g_arena_game + CD_EM_SITE_REC8_RVA, s_rec8, 7u);
    memcpy(g_arena_game + CD_EM_SITE_REC1_RVA, s_rec1, 7u);
    memcpy(g_arena_game + CD_EM_SITE_CNT1_RVA, s_cnt1, 7u);
    memcpy(g_arena_game + CD_EM_SITE_CNT2_RVA, s_cnt2, 7u);

    for (uint32_t i = 0; i < 4096u; ++i) {
        memcpy(reloc_recs + (size_t)i * 24u + 8u, &inv_ent, 4u);
    }
    for (uint32_t i = 0; i < 500u; ++i) {
        uint64_t r = 0x1111222233334444ULL;
        uint32_t e = 0x2000u + i;
        memcpy(reloc_recs + (size_t)i * 24u + 0u, &r, 8u);
        memcpy(reloc_recs + (size_t)i * 24u + 8u, &e, 4u);
    }
    /* Put living player avatar at descriptor slot 3,000 (> 2,048!) via 8,192-slot network map */
    uint8_t* net_buckets = (uint8_t*)calloc(8192u, 8u);
    CHECK(net_buckets != NULL);
    memset(net_buckets, 0xFF, 8192u * 8u);
    uint32_t net_id = 0x77u, slot3000 = 3000u, avatar_eid = 0x4242u;
    uint64_t av_res = 0x4D1C334D294DFA97ULL;
    memcpy(reloc_recs + (size_t)slot3000 * 24u + 0u, &av_res, 8u);
    memcpy(reloc_recs + (size_t)slot3000 * 24u + 8u, &avatar_eid, 4u);
    memcpy(reloc_recs + (size_t)slot3000 * 24u + 16u, &net_id, 4u);
    memcpy(net_buckets + (size_t)(net_id & 8191u) * 8u + 0u, &net_id, 4u);
    memcpy(net_buckets + (size_t)(net_id & 8191u) * 8u + 4u, &slot3000, 4u);
    uintptr_t nb_ptr = (uintptr_t)net_buckets;
    uint32_t nb_cap = 8192u, nb_empty = 0xFFFFFFFFu, nb_mult = 1u;
    memcpy(em_root + 0xF22EC8u + 0u, &nb_ptr, 8u);
    memcpy(em_root + 0xF22EC8u + 8u, &nb_cap, 4u);
    memcpy(em_root + 0xF22EC8u + 12u, &nb_empty, 4u);
    memcpy(em_root + 0xF22EC8u + 16u, &nb_mult, 4u);

    /* Configure PlayerManager slot 0 -> net_id 0x77 and TransformManager for avatar_eid */
    uintptr_t pm = 0, tm = 0, tm_buckets = 0, tm_rows = 0;
    memcpy(&pm, g_arena_game + CHAOS_PLAYERS_RVA, 8u);
    memcpy(&tm, g_arena_game + CHAOS_TRANSFORM_RVA, 8u);
    uint32_t pcnt = 1u, plife = 3u;
    memcpy((void*)(pm + 0x84u), &pcnt, 4u);
    memcpy((void*)(pm + 0x2E0u), &plife, 4u);
    memcpy((void*)(pm + 0x3A8u), &net_id, 4u);
    memcpy(&tm_buckets, (const void*)(tm + 0x40u), 8u);
    memcpy(&tm_rows, (const void*)(tm + 0x68u), 8u);
    uint32_t tm_cap = 0u, tm_mult = 0u, tm_idx = 0u;
    memcpy(&tm_cap, (const void*)(tm + 0x48u), 4u);
    memcpy(&tm_mult, (const void*)(tm + 0x50u), 4u);
    uint32_t bslot = (avatar_eid * tm_mult) & (tm_cap - 1u);
    memcpy((void*)(tm_buckets + (size_t)bslot * 8u + 0u), &avatar_eid, 4u);
    memcpy((void*)(tm_buckets + (size_t)bslot * 8u + 4u), &tm_idx, 4u);
    float av_pos[3] = {12.0f, -34.0f, 5.0f};
    memcpy((void*)(tm_rows + 0x2E0u), av_pos, 12u);

    memset(&g_em_snap, 0, sizeof g_em_snap);
    g_tracker.total_live = 0;
    ChaosRefreshEmSnapshot(gbase, 10.0);
    CHECK(g_em_snap.valid == 1);
    CHECK(g_em_snap.em_cap == 4096u);
    CHECK(g_em_snap.gwnav_reserve == 3396u);
    CHECK(g_em_snap.live_slots == 501u);
    CHECK(g_em_snap.walking_ai_slots == 500u); /* avatar_eid excluded from walking_ai_slots */
    CHECK(g_em_snap.free_slots == 4096u - 501u);

    float out_pos[4][3] = {{0}};
    uint32_t out_av[4] = {0};
    int living = CollectLivingPlayers(gbase, out_pos, out_av);
    CHECK(living == 1 && out_av[0] == avatar_eid && fabsf(out_pos[0][0] - 12.0f) < 1e-3f);

    memset(g_arena_game + CD_EM_SITE_REC8_RVA, 0, 7u);
    memset(g_arena_game + CD_EM_SITE_REC1_RVA, 0, 7u);
    memset(g_arena_game + CD_EM_SITE_CNT1_RVA, 0, 7u);
    memset(g_arena_game + CD_EM_SITE_CNT2_RVA, 0, 7u);
    api->version = 16u;
    free(net_buckets);
    free(big_block);
    InitMockGameArenas(CHAOS_NATIVE_BUGS_BIT, 5u);
    ResetMockCountersAndModState();
    subscriptions = ticks = 0u;
    CHECK(hellforge_mod_init(api, (HfMod*)1) == 0);
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

    /* Re-init with full SDK 16 mod channel + authority callbacks for co-op & migration tests */
    api.is_authority = MockIsAuthority;
    api.send = MockSend;
    api.share_set = MockShareSet;
    api.share_get = MockShareGet;
    api.share_row = MockShareRow;
    subscriptions = ticks = 0;
    CHECK(hellforge_mod_init(&api, (HfMod*)1) == 0 && subscriptions == 3 && ticks == 1);
    TestHostClientSplitAndLateJoin(&api);
    TestMidMissionHostMigration();
    TestHostOptionsPresetsAndPrecedence(&api);
    TestPrepareMissionZeroPreloadAndPacedMissionQueue();
    TestH4SlowD11JoinerBarrierAndImmediateMissionSync();
    TestV034BotDropDeadlockAndCityHardening();
    TestV035Mission2PreloadRearmAndCoopSafeRoster();
    TestV040UltrakillD12PoolsAndCloneWarsTheater(&api);
    TestV050GenuineDifficulty12SlotAndGating(&api);
    TestV052HellForge05RelocatedEmAndPools(&api);
    TestV230OvernightDeliverables(&api);

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

    printf("PASS %u checks; 0.3.4 co-op policy, partial-headroom/fallback, Mega City anchor & stranded pruner, %u code pins verified\n",
           checks, (unsigned)CPIN_COUNT);
    return 0;
}
