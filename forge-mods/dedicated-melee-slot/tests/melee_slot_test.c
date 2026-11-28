/* Comprehensive C11 unit & offline-capture verification suite for Dedicated Melee Slot 1.5.2-clonedivers.5.
 * Exercises ../src/melee_slot.c across:
 *   1. Offline PE pin verification against capture-game.dll-00001000.bin (0xFD9710, 0x12FE350, 0x8BF870).
 *   2. All 25 build-25480438 secondary weapon entity resources + HfContent modded secondary discovery,
 *      and strict rejection of primary/support/grenade/enemy spawns.
 *   3. Co-op local authority gate (spawnData[0] == 0 remote peer pistol ignored; spawnData[0] == 1 cloned
 *      even > 2000ms after mission start on mid-mission joiner hot-drop), network_id = 0x7FFF sentinel,
 *      and GetLastError() + SSE MXCSR preservation in SpawnReqDetour.
 *   4. Multi-slot (slots 0..3) LocalUser + PlayerManager + EntityManager avatar resolution (proving
 *      HfPlayerEvent.entity player-entity ID never poisons g_avatar).
 *   5. Preservation of ALL 4 player slots' +0x2E8 renderer words across AttachCopy (0x8BF870).
 *   6. Full secondary <-> melee toggle state machine (DoSwap + SwapStages 1 -> 3 -> 2 -> 4).
 *   7. Death / reinforcement drop & re-arm + input_settings.config custom keybind parsing + clean thread stop.
 *   8. Wilderness vs City package preload (HF_STATE_PREPARE_MISSION + HF_ON_MISSION_START without avatar wait),
 *      RefcountedPackageSystem <75% headroom & dedup, metadata-only (part_count==0) residency, and deferred
 *      game-thread copy spawning once all package parts reach state == 4 (eliminating wilderness purple '?'). */

#define DMS_UNIT_TEST 1
#include "../src/melee_slot.c"

static int g_tests_run = 0;
static int g_tests_failed = 0;

#define TEST_CHECK(cond, msg) do { \
    g_tests_run++; \
    if (!(cond)) { \
        fprintf(stderr, "[FAIL] %s (line %d): %s\n", __func__, __LINE__, (msg)); \
        g_tests_failed++; \
        return; \
    } \
} while (0)

/* ---- Mock Game/Exe Memory & HfApi ------------------------------------------------------------------------------- */

#define MOCK_MAX_ENGINE_PKGS 64u
#define MOCK_MAX_PARTS_PER_PKG 4u

typedef struct {
    uint8_t part_bytes[MOCK_MAX_PARTS_PER_PKG][0x24];
    uintptr_t part_ptrs[MOCK_MAX_PARTS_PER_PKG];
    uint8_t pkg_bytes[0x30];
} MockEnginePkg;

static uint8_t g_mock_game[0x3500000];
static uint8_t g_mock_exe[0x1B00000];
static uint8_t g_mock_engine_obj[0x500];
static uint8_t g_mock_pkg_mgr[0x300];
static uint8_t g_mock_pkg_res[0x100];
static uintptr_t g_mock_pkg_list[MOCK_MAX_ENGINE_PKGS];
static MockEnginePkg g_mock_pkgs[MOCK_MAX_ENGINE_PKGS];
static uint32_t g_mock_pkg_count = 0u;

static uint8_t g_mock_lu[0xC000];
static uint8_t g_mock_pm[0x1000];
static uint8_t g_mock_em[0xF40000];
static uint8_t g_mock_em_buckets[64 * 8];
static uint8_t g_mock_inv[0x200];
static uint8_t g_mock_inv_buckets[64 * 8];
static uint8_t g_mock_inv_records[8 * 0x30];
static uint8_t g_mock_ent_state[0x200];
static uint8_t g_mock_es_buckets[64 * 8];
static uint8_t g_mock_es_records[8 * 0x30];
static uint8_t g_mock_pkg_sys[0x100];
static uint8_t g_mock_pkg_ref_entries[64 * 16];

static uint32_t g_next_spawn_id = 500u;
static int g_spawn_calls = 0;
static uint64_t g_last_spawn_res = 0ULL;
static uint8_t g_last_spawn_info[0x60];
static int g_corrupt_csr_in_spawn = 0;
static int g_force_zero_clone_spawn = 0;
static int g_attach_calls = 0;
static int g_pkg_calls = 0;
static int g_hf_load_pkg_calls = 0;
static int g_auto_resident_on_req = 1;
static int g_sim_refmap_updates = 0;
static uint64_t g_last_pkg_req_id = 0ULL;
static uint64_t g_custom_mod_sec_ent = 0xCAFE000012345678ULL;
static char g_mock_mod_folder[MAX_PATH + 128];

static void SyncMockEnginePkgHeader(void)
{
    uintptr_t eng_p = (uintptr_t)g_mock_engine_obj;
    uintptr_t mgr_p = (uintptr_t)g_mock_pkg_mgr;
    uintptr_t res_p = (uintptr_t)g_mock_pkg_res;
    uintptr_t list_p = (uintptr_t)g_mock_pkg_list;
    memcpy(g_mock_exe + PKG_EXE_ENGINE, &eng_p, sizeof(eng_p));
    memcpy(g_mock_engine_obj + 0x400u, &mgr_p, sizeof(mgr_p));
    memcpy(g_mock_pkg_mgr + 0x208u, &res_p, sizeof(res_p));
    memcpy(g_mock_pkg_res + 0x80u, &g_mock_pkg_count, 4u);
    memcpy(g_mock_pkg_res + 0x88u, &list_p, sizeof(list_p));
}

static int UpsertMockEnginePkg(uint64_t pkg_id, uint32_t part_count, uint32_t part_state)
{
    if (part_count > MOCK_MAX_PARTS_PER_PKG) part_count = MOCK_MAX_PARTS_PER_PKG;
    uint32_t idx = g_mock_pkg_count;
    for (uint32_t i = 0u; i < g_mock_pkg_count; i++) {
        uint64_t existing = 0ULL;
        memcpy(&existing, g_mock_pkgs[i].pkg_bytes + 0x10u, 8u);
        if (existing == pkg_id) {
            idx = i;
            break;
        }
    }
    if (idx == g_mock_pkg_count) {
        if (g_mock_pkg_count >= MOCK_MAX_ENGINE_PKGS) return -1;
        g_mock_pkg_count++;
    }
    MockEnginePkg* mp = &g_mock_pkgs[idx];
    memset(mp, 0, sizeof(*mp));
    for (uint32_t p = 0u; p < part_count; p++) {
        memcpy(mp->part_bytes[p] + 0x1Cu, &part_state, 4u);
        mp->part_ptrs[p] = (uintptr_t)mp->part_bytes[p];
    }
    uintptr_t parts_arr = (uintptr_t)mp->part_ptrs;
    memcpy(mp->pkg_bytes + 0x10u, &pkg_id, 8u);
    memcpy(mp->pkg_bytes + 0x18u, &part_count, 4u);
    memcpy(mp->pkg_bytes + 0x20u, &parts_arr, sizeof(parts_arr));
    g_mock_pkg_list[idx] = (uintptr_t)mp->pkg_bytes;
    SyncMockEnginePkgHeader();
    return (int)idx;
}

static void SetMockEnginePkgPartState(uint64_t pkg_id, uint32_t part_idx, uint32_t state)
{
    for (uint32_t i = 0u; i < g_mock_pkg_count; i++) {
        uint64_t existing = 0ULL;
        memcpy(&existing, g_mock_pkgs[i].pkg_bytes + 0x10u, 8u);
        if (existing == pkg_id && part_idx < MOCK_MAX_PARTS_PER_PKG) {
            memcpy(g_mock_pkgs[i].part_bytes[part_idx] + 0x1Cu, &state, 4u);
            return;
        }
    }
}

static int MockRead(uintptr_t addr, void* out, size_t sz)
{
    if (addr < 0x10000u || !out) return 1;
    uintptr_t cur = addr, end = addr + sz;
    if (end < cur) return 1;
    while (cur < end) {
        MEMORY_BASIC_INFORMATION mbi;
        if (VirtualQuery((const void*)cur, &mbi, sizeof(mbi)) != sizeof(mbi)) return 1;
        if (mbi.State != MEM_COMMIT || (mbi.Protect & (PAGE_NOACCESS | PAGE_GUARD)) != 0) return 1;
        uintptr_t reg_end = (uintptr_t)mbi.BaseAddress + mbi.RegionSize;
        if (reg_end <= cur) return 1;
        cur = reg_end;
    }
    memcpy(out, (const void*)addr, sz);
    return 0;
}

static int MockWrite(uintptr_t addr, const void* data, size_t sz)
{
    if (addr < 0x10000u || !data) return 1;
    memcpy((void*)addr, data, sz);
    return 0;
}

static void MockLog(HfMod* m, const char* fmt, ...)
{
    (void)m;
    (void)fmt;
}

static const char* MockBuildId(void) { return "25480438"; }
static const char* MockModFolder(HfMod* m)
{
    (void)m;
    return g_mock_mod_folder[0] ? g_mock_mod_folder : NULL;
}
static uintptr_t MockModuleBase(const char* name, size_t* sz)
{
    if (!name) return 0;
    if (strcmp(name, "game.dll") == 0) {
        if (sz) *sz = sizeof(g_mock_game);
        return (uintptr_t)g_mock_game;
    }
    if (strcmp(name, "helldivers2.exe") == 0) {
        if (sz) *sz = sizeof(g_mock_exe);
        return (uintptr_t)g_mock_exe;
    }
    return 0;
}

static int MockLoadPackage(HfMod* m, uint64_t package)
{
    (void)m;
    (void)package;
    g_hf_load_pkg_calls++;
    return g_auto_resident_on_req ? HF_PACKAGE_RESIDENT : HF_PACKAGE_LOADING;
}

static uint32_t MockEntityOfNetwork(HfMod* m, uint32_t net)
{
    (void)m;
    if (net == 0x22u) return 0x7001u; /* initial avatar */
    if (net == 0x23u) return 0x7002u; /* reinforced avatar */
    return 0xFFFFFFFFu;
}

static uintptr_t MockGlobal(HfMod* m, const char* name)
{
    (void)m;
    if (strcmp(name, "LocalUser") == 0) return (uintptr_t)(g_mock_game + LOCALUSER_GLOBAL);
    if (strcmp(name, "PlayerManager") == 0) return (uintptr_t)(g_mock_game + PLAYERS_GLOBAL);
    return 0;
}

static int MockContent(HfMod* m, uint32_t index, HfContent* out)
{
    (void)m;
    if (index == 0u) {
        memset(out, 0, sizeof(*out));
        out->size = sizeof(*out);
        out->kind = HF_KIND_ITEM;
        out->entity = g_custom_mod_sec_ent;
        out->base_entity = 0x05E4E5C2DB6E44A2ULL; /* Copies P-2 Peacemaker */
        return 1;
    }
    return 0;
}

static HfListener g_ls_weapon, g_ls_state, g_ls_mstart, g_ls_mend, g_ls_pspawn, g_ls_pdown;
static HfEventCallback g_cb_session_end;
static HfTickCallback g_cb_tick;

static int MockSubscribe(HfMod* m, uint32_t kind, HfListener cb, void* user, int32_t prio)
{
    (void)m; (void)user; (void)prio;
    if (kind == HF_ON_WEAPON) g_ls_weapon = cb;
    else if (kind == HF_ON_STATE) g_ls_state = cb;
    else if (kind == HF_ON_MISSION_START) g_ls_mstart = cb;
    else if (kind == HF_ON_MISSION_END) g_ls_mend = cb;
    else if (kind == HF_ON_PLAYER_SPAWN) g_ls_pspawn = cb;
    else if (kind == HF_ON_PLAYER_DOWN) g_ls_pdown = cb;
    return 1;
}

static int MockOn(HfMod* m, HfEvent ev, HfEventCallback cb, void* user)
{
    (void)m; (void)user;
    if (ev == HF_EVENT_SESSION_END) g_cb_session_end = cb;
    return 0;
}

static int MockOnGameThread(HfMod* m, HfTickCallback cb, void* user)
{
    (void)m; (void)user;
    g_cb_tick = cb;
    return 1;
}

static uint64_t MockOrigSpawnReq(uint64_t a, uint64_t b, uint64_t c, uint64_t d, uint64_t e, uint64_t f)
{
    (void)d; (void)e; (void)f;
    g_spawn_calls++;
    g_last_spawn_res = b;
    memcpy(g_last_spawn_info, (const void*)(uintptr_t)c, sizeof(g_last_spawn_info));
    /* Simulate real SpawnReq writing its stack frame pointer into [c + 0x50] */
    uint64_t fake_stack = 0x7FFDEAD00000ULL;
    memcpy((void*)((uintptr_t)c + 0x50u), &fake_stack, 8);
    if (g_corrupt_csr_in_spawn) {
        SetLastError(0xBADF00Du);
    }
    if (g_force_zero_clone_spawn && InterlockedCompareExchange(&g_spawningClone, 0, 0) != 0) {
        uint32_t zero_eid = 0u;
        memcpy((void*)(uintptr_t)a, &zero_eid, 4);
        return a;
    }
    uint32_t eid = g_next_spawn_id++;
    memcpy((void*)(uintptr_t)a, &eid, 4);
    return a;
}

static void MockAttachFn(uint64_t tbl, uint64_t entity, uint64_t owner, uint64_t flag, uint64_t e, uint64_t f)
{
    (void)tbl; (void)owner; (void)flag; (void)e; (void)f;
    g_attach_calls++;
    /* Simulate 0x8BF870 clobbering BOTH slot 0's +0x2E8 word AND slot 2's +0x2E8 word! */
    uint32_t bad = (uint32_t)entity;
    memcpy(g_mock_pm + 0u * PLAYERS_SLOT_STRIDE + PLAYERS_WORD_OFF, &bad, 4);
    memcpy(g_mock_pm + 2u * PLAYERS_SLOT_STRIDE + PLAYERS_WORD_OFF, &bad, 4);
}

static void MockPkgReqFn(uint64_t sys, uint64_t id_ptr, uint64_t one, uint64_t d, uint64_t e, uint64_t f)
{
    (void)sys; (void)one; (void)d; (void)e; (void)f;
    g_pkg_calls++;
    uint64_t pid = 0ULL;
    if (id_ptr >= 0x10000ULL) {
        memcpy(&pid, (const void*)(uintptr_t)id_ptr, 8u);
    }
    g_last_pkg_req_id = pid;
    if (pid != 0ULL) {
        UpsertMockEnginePkg(pid, 2u, g_auto_resident_on_req ? 4u : 2u);
        if (g_sim_refmap_updates) {
            uint64_t entries_u64 = 0ULL;
            uint32_t cap = 0u;
            memcpy(&entries_u64, g_mock_pkg_sys + 0u, 8u);
            memcpy(&cap, g_mock_pkg_sys + 8u, 4u);
            if (entries_u64 >= 0x10000ULL && cap > 0u && cap <= 64u) {
                uint8_t* entries = (uint8_t*)(uintptr_t)entries_u64;
                int updated = 0;
                for (uint32_t i = 0u; i < cap; i++) {
                    uint64_t k = 0ULL;
                    uint32_t r = 0u;
                    memcpy(&k, entries + i * 16u + 0u, 8u);
                    memcpy(&r, entries + i * 16u + 8u, 4u);
                    if (k == pid && r != 0u) {
                        r++;
                        memcpy(entries + i * 16u + 8u, &r, 4u);
                        updated = 1;
                        break;
                    }
                }
                if (!updated) {
                    for (uint32_t i = 0u; i < cap; i++) {
                        uint64_t k = 0ULL;
                        uint32_t r = 0u;
                        memcpy(&k, entries + i * 16u + 0u, 8u);
                        memcpy(&r, entries + i * 16u + 8u, 4u);
                        if (k == 0ULL || r == 0u) {
                            r = 1u;
                            memcpy(entries + i * 16u + 0u, &pid, 8u);
                            memcpy(entries + i * 16u + 8u, &r, 4u);
                            break;
                        }
                    }
                }
            }
        }
    }
}

static void InitHashHeader(uint8_t* hdr, uint8_t* buckets, uint32_t cap)
{
    memset(buckets, 0xFF, cap * 8u);
    uint64_t bptr = (uint64_t)(uintptr_t)buckets;
    uint32_t empty = 0xFFFFFFFFu, mult = 1u;
    memcpy(hdr + 0, &bptr, 8);
    memcpy(hdr + 8, &cap, 4);
    memcpy(hdr + 12, &empty, 4);
    memcpy(hdr + 16, &mult, 4);
}

static void PutHashEntry(uint8_t* buckets, uint32_t cap, uint32_t key, uint32_t val)
{
    uint32_t slot = key % cap;
    while (*(uint32_t*)(buckets + slot * 8u) != 0xFFFFFFFFu && *(uint32_t*)(buckets + slot * 8u) != key) {
        slot = (slot + 1u) % cap;
    }
    memcpy(buckets + slot * 8u, &key, 4);
    memcpy(buckets + slot * 8u + 4u, &val, 4);
}

static HfApi g_api;
static struct { const HfApi* api; } g_mod_inst;

static void SetupHarnessEx(const char* cfg_dir, const char* weapon_choice, int auto_resident, int start_mission)
{
    snprintf(g_testConfigDir, sizeof(g_testConfigDir), "%s", cfg_dir ? cfg_dir : "");
    if (cfg_dir && *cfg_dir && weapon_choice) {
        char p[MAX_PATH + 64];
        snprintf(p, sizeof(p), "%s\\melee-slot-weapon.txt", cfg_dir);
        FILE* f = fopen(p, "wb");
        if (f) {
            fprintf(f, "%s\r\n", weapon_choice);
            fclose(f);
        }
    }

    memset(&g_api, 0, sizeof(g_api));
    g_api.version = 20;
    g_api.size = sizeof(HfApi);
    g_api.log = MockLog;
    g_api.build_id = MockBuildId;
    g_api.mod_folder = MockModFolder;
    g_api.module_base = MockModuleBase;
    g_api.read = MockRead;
    g_api.write = MockWrite;
    g_api.on = MockOn;
    g_api.subscribe = MockSubscribe;
    g_api.on_game_thread = MockOnGameThread;
    g_api.load_package = MockLoadPackage;
    g_api.entity_of_network = MockEntityOfNetwork;
    g_api.global = MockGlobal;
    g_api.content = MockContent;
    g_mod_inst.api = &g_api;

    memset(g_mock_pkg_sys, 0, sizeof(g_mock_pkg_sys));
    memset(g_mock_pkg_ref_entries, 0, sizeof(g_mock_pkg_ref_entries));
    g_mock_pkg_count = 0u;
    SyncMockEnginePkgHeader();

    uintptr_t lu_p = (uintptr_t)g_mock_lu;
    uintptr_t pm_p = (uintptr_t)g_mock_pm;
    uintptr_t em_p = (uintptr_t)g_mock_em;
    uintptr_t inv_p = (uintptr_t)g_mock_inv;
    uintptr_t es_p = (uintptr_t)g_mock_ent_state;
    uintptr_t pk_p = (uintptr_t)g_mock_pkg_sys;
    memcpy(g_mock_game + LOCALUSER_GLOBAL, &lu_p, sizeof(lu_p));
    memcpy(g_mock_game + PLAYERS_GLOBAL, &pm_p, sizeof(pm_p));
    memcpy(g_mock_game + ENTITIES_GLOBAL, &em_p, sizeof(em_p));
    memcpy(g_mock_game + INV_GLOBAL, &inv_p, sizeof(inv_p));
    memcpy(g_mock_game + ENT_STATE_GLOBAL, &es_p, sizeof(es_p));
    memcpy(g_mock_game + PKG_SYSTEM_GLOBAL, &pk_p, sizeof(pk_p));

    /* Configure 4-player co-op session with local peer in slot 2! */
    memset(g_mock_lu, 0, sizeof(g_mock_lu));
    memset(g_mock_pm, 0, sizeof(g_mock_pm));
    uint64_t local_peer = 0xAABBCCDDEEFF0011ULL;
    uint32_t pcount = 4u;
    memcpy(g_mock_lu + LOCALUSER_PEER_OFF, &local_peer, 8);
    memcpy(g_mock_pm + PLAYERS_COUNT_OFF, &pcount, 4);
    for (uint32_t s = 0; s < 4u; s++) {
        uint64_t peer = (s == 2u) ? local_peer : (0x1000ULL + s);
        uint32_t life = PLAYER_LIFE_SPAWNED;
        uint32_t net = (s == 2u) ? 0x22u : (0x10u + s);
        uint32_t word = 0x9000u + s;
        memcpy(g_mock_pm + PLAYERS_PEER_OFF + s * PLAYERS_SLOT_STRIDE, &peer, 8);
        memcpy(g_mock_pm + PLAYERS_LIFE_OFF + s * PLAYERS_SLOT_STRIDE, &life, 4);
        memcpy(g_mock_pm + PLAYERS_WORD_OFF + s * PLAYERS_SLOT_STRIDE, &word, 4);
        memcpy(g_mock_pm + PLAYERS_NET_OFF + s * PLAYERS_NET_STRIDE, &net, 4);
    }

    InitHashHeader(g_mock_em + EM_ENTITY_MAP, g_mock_em_buckets, 64u);
    InitHashHeader(g_mock_inv + 0x28u, g_mock_inv_buckets, 64u);
    uintptr_t inv_recs = (uintptr_t)g_mock_inv_records;
    memcpy(g_mock_inv + 0x50u, &inv_recs, sizeof(inv_recs));

    InitHashHeader(g_mock_ent_state + 0x20u, g_mock_es_buckets, 64u);
    uintptr_t es_recs = (uintptr_t)g_mock_es_records;
    memcpy(g_mock_ent_state + 0x40u, &es_recs, sizeof(es_recs));

    g_next_spawn_id = 500u;
    g_spawn_calls = 0;
    g_force_zero_clone_spawn = 0;
    g_attach_calls = 0;
    g_pkg_calls = 0;
    g_hf_load_pkg_calls = 0;
    g_auto_resident_on_req = auto_resident;
    g_mock_mod_folder[0] = '\0';
    InterlockedExchange(&g_testSentKeyDowns, 0);
    InterlockedExchange(&g_testSentKeyUps, 0);

    int rc = hellforge_mod_init(&g_api, (HfMod*)&g_mod_inst);
    if (rc != 0) abort();
    g_spawnReq = MockOrigSpawnReq;
    g_attachFn = MockAttachFn;
    g_pkgReq = MockPkgReqFn;
    g_regOk = 1;

    if (start_mission) {
        HfEventInfo mstart = {sizeof(HfStateEvent), HF_ON_MISSION_START, 1};
        g_ls_mstart((HfMod*)&g_mod_inst, &mstart, NULL);
    }
}

static void SetupHarness(const char* cfg_dir, const char* weapon_choice)
{
    SetupHarnessEx(cfg_dir, weapon_choice, 1, 1);
}

/* ---- Tests ------------------------------------------------------------------------------------------------------ */

static void test_offline_game_dll_pins_if_present(void)
{
    const char* path =
        "D:\\ClaudeWork\\clonedivers-lab\\camera-evidence-astra\\camera-capture-mission\\capture-game.dll-00001000.bin";
    FILE* f = fopen(path, "rb");
    if (!f) return;

    struct { uint32_t rva; uint8_t bytes[19]; size_t len; const char* name; } pins[] = {
        {SPAWN_REQ_RVA, {0x4c,0x8b,0xdc,0x49,0x89,0x5b,0x10,0x49,0x89,0x73,0x20,0x57,0x48,0x81,0xec,0xa0,0x00,0x00,0x00}, 19, "SPAWN_REQ"},
        {PKG_REQUEST_RVA, {0x45,0x85,0xc0,0x0f,0x84,0x75,0x01,0x00,0x00,0x41,0x56,0x41,0x57,0x48,0x83,0xec,0x38}, 17, "PKG_REQUEST"},
        {ATTACH_RVA, {0x4c,0x8b,0xdc,0x55,0x53,0x41,0x56,0x49,0x8d,0x6b,0xa8,0x48,0x81,0xec,0x40,0x01,0x00,0x00}, 18, "ATTACH"},
    };
    for (size_t i = 0; i < 3u; i++) {
        uint8_t buf[32];
        TEST_CHECK(fseek(f, (long)(pins[i].rva - 0x1000u), SEEK_SET) == 0, pins[i].name);
        TEST_CHECK(fread(buf, 1, pins[i].len, f) == pins[i].len, pins[i].name);
        TEST_CHECK(memcmp(buf, pins[i].bytes, pins[i].len) == 0, pins[i].name);
    }
    fclose(f);
}

static void test_secondary_table_and_modded_weapons(const char* dir)
{
    SetupHarness(dir, "saber");
    for (size_t i = 0; i < 25u; i++) {
        if (i > 0) TEST_CHECK(kVanillaSecondaryEntities[i - 1] < kVanillaSecondaryEntities[i], "secondary table sorted");
        TEST_CHECK(IsSecondaryWeaponResource(kVanillaSecondaryEntities[i]) == 1, "vanilla secondary matched");
    }
    TEST_CHECK(IsSecondaryWeaponResource(g_custom_mod_sec_ent) == 1, "HfContent custom secondary matched");
    TEST_CHECK(IsSecondaryWeaponResource(0x968211C0033DCE64ULL) == 0, "AR-23 Liberator primary rejected");
    TEST_CHECK(IsSecondaryWeaponResource(0xA8CFFB316F0B5C5FULL) == 0, "AC-8 Autocannon support rejected");
}

static void test_coop_hotjoin_authority_netid_attach_and_swap(const char* dir)
{
    SetupHarness(dir, "machete");
    TEST_CHECK(g_pkg_calls == 9, "HF_ON_MISSION_START preloaded all 9 Machete packages immediately without waiting for avatar");

    /* 1. Verify HF_ON_PLAYER_SPAWN does NOT poison g_avatar with p->entity (Player entity = 0x9999)! */
    HfPlayerEvent pev = {{sizeof(pev), HF_ON_PLAYER_SPAWN, 2}, 0xAABBCCDDEEFF0011ULL, 0x9999u, 1u};
    g_ls_pspawn((HfMod*)&g_mod_inst, (const HfEventInfo*)&pev, NULL);
    TEST_CHECK(g_avatar == 0x7001u, "resolved true Helldiver avatar 0x7001 in co-op slot 2, ignoring Player entity 0x9999");

    /* 2. Remote teammate spawns a P-4 Senator (authority == 0) -> MUST BE IGNORED! */
    uint8_t remote_info[0x200];
    memset(remote_info, 0, sizeof(remote_info));
    remote_info[0] = 0; /* remote authority */
    uint32_t remote_net = 0x111u, out_id = 0u;
    memcpy(remote_info + 4u, &remote_net, 4);
    SpawnReqDetour((uint64_t)(uintptr_t)&out_id, 0x8D3D52A3B2F19402ULL, (uint64_t)(uintptr_t)remote_info, 0, 0, 0);
    TEST_CHECK(g_spawn_calls == 1 && g_probeEntity == 0u, "remote teammate secondary spawn ignored");

    /* 3. Local primary weapon spawns (authority == 1, AR-23 Liberator) -> MUST BE IGNORED! */
    uint8_t local_info[0x200];
    memset(local_info, 0, sizeof(local_info));
    local_info[0] = 1;
    uint32_t pistol_net = 0x333u;
    memcpy(local_info + 4u, &pistol_net, 4);
    SpawnReqDetour((uint64_t)(uintptr_t)&out_id, 0x968211C0033DCE64ULL, (uint64_t)(uintptr_t)local_info, 0, 0, 0);
    TEST_CHECK(g_spawn_calls == 2 && g_probeEntity == 0u, "local primary weapon spawn ignored");

    /* 4. Local secondary spawns (e.g. 45s into hot-join, seq > 2!): P-4 Senator -> clones CQC-42 Machete with net_id = 0x7FFF!
     *    Also verify GetLastError() & SSE MXCSR preservation! */
    unsigned int orig_csr = _mm_getcsr();
    SetLastError(0x44556677u);
    SpawnReqDetour((uint64_t)(uintptr_t)&out_id, 0x8D3D52A3B2F19402ULL, (uint64_t)(uintptr_t)local_info, 0, 0, 0);
    TEST_CHECK(GetLastError() == 0x44556677u && _mm_getcsr() == orig_csr, "GetLastError and MXCSR preserved across SpawnReqDetour");
    TEST_CHECK(g_spawn_calls == 4, "secondary + extra melee clone spawned synchronously when resident");
    TEST_CHECK(g_probeOriginal == 502u && g_probeEntity == 503u, "tracked original pistol 502 and melee clone 503");

    uint32_t clone_net = 0u;
    uint64_t clone_stack_ptr = 1ULL;
    memcpy(&clone_net, g_last_spawn_info + 4u, 4);
    memcpy(&clone_stack_ptr, g_last_spawn_info + 0x50u, 8);
    TEST_CHECK(g_last_spawn_info[0] == 1u && clone_net == NET_ID_NONE && clone_stack_ptr == 0ULL,
        "melee clone spawned with local authority=1 and non-networked sentinel 0x7FFF (no EVP_NETWORK_MAP collision)");

    /* 5. Populate EntityManager descriptor + ENT_STATE_GLOBAL ownership + InventoryRecord for avatar 0x7001,
     *    run AttachCopy, and verify ALL 4 player slots' +0x2E8 renderer words are preserved! */
    PutHashEntry(g_mock_es_buckets, 64u, 502u, 0u);
    PutHashEntry(g_mock_es_buckets, 64u, 503u, 1u);
    uint32_t owner_av = 0x7001u;
    memcpy(g_mock_es_records + 0u * 0x30u + 4u, &owner_av, 4);

    PutHashEntry(g_mock_em_buckets, 64u, 503u, 0u);
    uint64_t machete_res = 0x792D5D2A340FD6E6ULL;
    uint32_t clone_eid = 503u, clone_unit = 0x400099u;
    memcpy(g_mock_em + EM_DESCRIPTORS + 0u, &machete_res, 8);
    memcpy(g_mock_em + EM_DESCRIPTORS + 8u, &clone_eid, 4);
    memcpy(g_mock_em + EM_DESCRIPTORS + 12u, &clone_unit, 4);

    PutHashEntry(g_mock_inv_buckets, 64u, 0x7001u, 0u);
    uint32_t pri_eid = 501u, sec_eid = 502u, sel = 2u;
    memcpy(g_mock_inv_records + 0u, &pri_eid, 4);
    memcpy(g_mock_inv_records + 4u, &sec_eid, 4);
    memcpy(g_mock_inv_records + 0x1Cu, &sel, 4);

    g_probeTick = GetTickCount64() - 2000ULL;
    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    TEST_CHECK(g_attach_calls == 1 && g_regDone == 1, "AttachCopy attached melee to co-op slot-2 avatar");
    TEST_CHECK(g_pkg_calls == 9, "no duplicate RefcountedPackageSystem requests after session preload");

    for (uint32_t s = 0; s < 4u; s++) {
        uint32_t w = 0u;
        memcpy(&w, g_mock_pm + s * PLAYERS_SLOT_STRIDE + PLAYERS_WORD_OFF, 4);
        TEST_CHECK(w == (0x9000u + s), "preserved +0x2E8 renderer word across all 4 co-op slots 0..3");
    }

    /* 6. Toggle pistol (502) -> melee (503) via DoSwap + SwapStages! */
    InterlockedExchange(&g_tapFlag, 1);
    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    TEST_CHECK(g_swapStage == 1 && g_injectSel == 1, "DoSwap requested holster to primary (key 1)");

    sel = 1u;
    memcpy(g_mock_inv_records + 0x1Cu, &sel, 4);
    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    uint32_t cur_sec = 0u;
    memcpy(&cur_sec, g_mock_inv_records + 4u, 4);
    TEST_CHECK(cur_sec == 503u && g_injectSel == 2 && g_swapStage == 4, "wrote melee 503 into secondary slot and requested draw (key 2)");

    sel = 2u;
    memcpy(g_mock_inv_records + 0x1Cu, &sel, 4);
    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    TEST_CHECK(g_swapStage == 0, "completed swap to melee");

    /* 7. Death & reinforcement into new avatar 0x7002 -> drops old copy & re-arms cleanly! */
    uint32_t dead_life = 2u;
    memcpy(g_mock_pm + PLAYERS_LIFE_OFF + 2u * PLAYERS_SLOT_STRIDE, &dead_life, 4);
    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    TEST_CHECK(g_probeEntity == 0u && g_armed == 1, "death dropped old melee and re-armed for reinforcement");
}

static void EnsureDirTree(const char* path)
{
    char tmp[MAX_PATH + 192];
    snprintf(tmp, sizeof(tmp), "%s", path);
    for (char* p = tmp + 3; *p; p++) {
        if (*p == '\\' || *p == '/') {
            char ch = *p;
            *p = '\0';
            CreateDirectoryA(tmp, NULL);
            *p = ch;
        }
    }
    CreateDirectoryA(tmp, NULL);
}

static void test_keybind_parser_and_clean_thread_shutdown(const char* dir)
{
    SetupHarness(dir, "hatchet");
    const char* cfg =
        "ChangeEquipmentPrimary = [\n"
        "  { device_type = \"Keyboard\" input = \"q\" trigger = \"Press\" threshold = 0.0 }\n"
        "]\n"
        "ChangeEquipmentSecondary = [\n"
        "  { device_type = \"Keyboard\" input = \"e\" trigger = \"LongPress\" threshold = 0.25 }\n"
        "]\n";
    ParseSlotKeysText(cfg);
    TEST_CHECK(g_keyScan[1] == 0x10 && g_keyScan[2] == 0x12, "parsed Q (0x10) and E (0x12) scan codes");
    TEST_CHECK(g_keyMs[1] == 25 && g_keyMs[2] == 350, "parsed Press (25ms) and LongPress (350ms) hold durations");
    ParseSlotKeysText(""); /* Reset defaults */
    TEST_CHECK(g_keyScan[1] == 0x02 && g_keyScan[2] == 0x03, "restored default 1/2 scan codes");

    /* L3: Verify custom install root (<installRoot>\userdata\profiles\*\cloud\input_settings.config)
     * derived from BOTH folder and .unpacked mod_folder layouts, plus USERPROFILE\Documents\HellForge fallback! */
    char resolved_root[MAX_PATH + 128];
    TEST_CHECK(ResolveInstallRootFromModFolder("D:\\CustomForge\\mods\\dedicated-melee-slot", resolved_root, sizeof(resolved_root)) == 1 &&
               strcmp(resolved_root, "D:\\CustomForge") == 0,
               "L3: resolved install root from folder install");
    TEST_CHECK(ResolveInstallRootFromModFolder("D:/CustomForge/mods/.unpacked/pkg999/dedicated-melee-slot/", resolved_root, sizeof(resolved_root)) == 1 &&
               strcmp(resolved_root, "D:\\CustomForge") == 0,
               "L3: resolved install root from .unpacked zip install");

    if (dir && *dir) {
        char custom_cloud[MAX_PATH + 192], custom_cfg[MAX_PATH + 224];
        char user_base[MAX_PATH + 64], docs_cloud[MAX_PATH + 192], docs_cfg[MAX_PATH + 224];
        snprintf(custom_cloud, sizeof(custom_cloud), "%s\\CustomHF\\userdata\\profiles\\pilot1\\cloud", dir);
        snprintf(custom_cfg, sizeof(custom_cfg), "%s\\input_settings.config", custom_cloud);
        snprintf(user_base, sizeof(user_base), "%s\\FakeUser", dir);
        snprintf(docs_cloud, sizeof(docs_cloud), "%s\\Documents\\HellForge\\userdata\\profiles\\default\\cloud", user_base);
        snprintf(docs_cfg, sizeof(docs_cfg), "%s\\input_settings.config", docs_cloud);
        EnsureDirTree(custom_cloud);
        EnsureDirTree(docs_cloud);

        const char* custom_text =
            "ChangeEquipmentPrimary = [ { device_type = \"Keyboard\" input = \"f\" trigger = \"Press\" threshold = 0.0 } ]\n"
            "ChangeEquipmentSecondary = [ { device_type = \"Keyboard\" input = \"v\" trigger = \"Hold\" threshold = 0.40 } ]\n";
        const char* docs_text =
            "ChangeEquipmentPrimary = [ { device_type = \"Keyboard\" input = \"z\" trigger = \"Press\" threshold = 0.0 } ]\n"
            "ChangeEquipmentSecondary = [ { device_type = \"Keyboard\" input = \"x\" trigger = \"Press\" threshold = 0.0 } ]\n";
        FILE* fc = fopen(custom_cfg, "wb");
        if (fc) { fwrite(custom_text, 1, strlen(custom_text), fc); fclose(fc); }
        FILE* fd = fopen(docs_cfg, "wb");
        if (fd) { fwrite(docs_text, 1, strlen(docs_text), fd); fclose(fd); }

        char old_userprofile[MAX_PATH];
        DWORD had_up = GetEnvironmentVariableA("USERPROFILE", old_userprofile, sizeof(old_userprofile));
        SetEnvironmentVariableA("USERPROFILE", user_base);

        snprintf(g_mock_mod_folder, sizeof(g_mock_mod_folder),
            "%s\\CustomHF\\mods\\.unpacked\\hash123\\dedicated-melee-slot", dir);
        g_lastKeysPath[0] = '\0';
        memset(&g_lastKeysWhen, 0, sizeof(g_lastKeysWhen));
        LoadSlotKeys();
        TEST_CHECK(g_keyScan[1] == 0x21 && g_keyScan[2] == 0x2F && g_keyMs[2] == 500,
            "L3: LoadSlotKeys loaded custom F/V (500ms) keybinds from custom install root userdata");

        /* Fallback to USERPROFILE\Documents\HellForge when custom install root has no userdata config */
        snprintf(g_mock_mod_folder, sizeof(g_mock_mod_folder), "%s\\EmptyHF\\mods\\dedicated-melee-slot", dir);
        g_lastKeysPath[0] = '\0';
        memset(&g_lastKeysWhen, 0, sizeof(g_lastKeysWhen));
        LoadSlotKeys();
        TEST_CHECK(g_keyScan[1] == 0x2C && g_keyScan[2] == 0x2D,
            "L3: LoadSlotKeys fell back cleanly to USERPROFILE\\Documents\\HellForge userdata");

        if (had_up > 0 && had_up < sizeof(old_userprofile)) {
            SetEnvironmentVariableA("USERPROFILE", old_userprofile);
        }
    }

    /* H3b: Start KeyThread, enter a 1500ms LongPress hold, and verify hellforge_mod_unload wakes g_stopEvent
     * immediately (< 250ms), emits KEYUP cleanly, and joins KeyThread within the 2000ms timeout! */
    ParseSlotKeysText("");
    InterlockedExchange(&g_keyMs[1], 1500);
    InterlockedExchange(&g_stopThreads, 0);
    if (g_stopEvent == NULL) g_stopEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
    else ResetEvent(g_stopEvent);
    InterlockedExchange(&g_testSentKeyDowns, 0);
    InterlockedExchange(&g_testSentKeyUps, 0);
    g_testForceFocused = 1; /* Safe: DMS_UNIT_TEST mocks DmsSendInputKey without calling Win32 SendInput! */
    g_keyThread = CreateThread(NULL, 0, KeyThread, NULL, 0, NULL);
    TEST_CHECK(g_keyThread != NULL, "KeyThread started (H3a: no InvThread)");

    InterlockedExchange(&g_injectSel, 1);
    for (int wait_i = 0; wait_i < 50 && InterlockedCompareExchange(&g_testSentKeyDowns, 0, 0) == 0; wait_i++) {
        Sleep(5);
    }
    TEST_CHECK(InterlockedCompareExchange(&g_testSentKeyDowns, 0, 0) == 1 &&
               InterlockedCompareExchange(&g_testSentKeyUps, 0, 0) == 0,
               "H3b: KeyThread entered 1500ms hold sleep after keydown");

    ULONGLONG t_unload0 = GetTickCount64();
    TEST_CHECK(hellforge_mod_unload(&g_api, (HfMod*)&g_mod_inst, HF_UNLOAD_REMOVE) == 0,
               "H3b: clean unload signaled g_stopEvent & joined KeyThread");
    ULONGLONG unload_ms = GetTickCount64() - t_unload0;
    TEST_CHECK(unload_ms < 400ULL, "H3b: g_stopEvent woke 1500ms hold immediately on unload (< 400ms)");
    TEST_CHECK(InterlockedCompareExchange(&g_testSentKeyUps, 0, 0) == 1,
               "H3b: KeyThread emitted KEYUP cleanly before exiting on early wake");
    TEST_CHECK(g_keyThread == NULL && g_stopEvent == NULL && g_spawnScratch == NULL,
               "H3b: handles and scratch buffer cleaned");
    g_testForceFocused = 0;
}

static void test_h3c_h3d_failed_spawn_and_failed_attach_recovery(const char* dir)
{
    /* 1. H3d: When g_spawnReq returns copy == 0 on drop, mod MUST NOT permanently disarm for the mission! */
    SetupHarness(dir, "machete");
    HfPlayerEvent pev = {{sizeof(pev), HF_ON_PLAYER_SPAWN, 1}, 0xAABBCCDDEEFF0011ULL, 0x9999u, 1u};
    g_ls_pspawn((HfMod*)&g_mod_inst, (const HfEventInfo*)&pev, NULL);
    TEST_CHECK(g_avatar == 0x7001u, "resolved initial avatar");

    uint8_t local_info[0x200];
    memset(local_info, 0, sizeof(local_info));
    local_info[0] = 1;
    uint32_t pistol_net = 0x333u, out_id = 0u;
    memcpy(local_info + 4u, &pistol_net, 4);

    g_force_zero_clone_spawn = 1;
    SpawnReqDetour((uint64_t)(uintptr_t)&out_id, 0x8D3D52A3B2F19402ULL, (uint64_t)(uintptr_t)local_info, 0, 0, 0);
    TEST_CHECK(g_probeEntity == 0u && g_probeOriginal == 0u && g_regDone == 1 && g_armed == 1,
        "H3d: copy == 0 cleared probe state cleanly and stayed armed for next reinforcement/respawn");

    /* Reinforce / respawn with g_force_zero_clone_spawn = 0 -> must succeed cleanly! */
    g_force_zero_clone_spawn = 0;
    SpawnReqDetour((uint64_t)(uintptr_t)&out_id, 0x8D3D52A3B2F19402ULL, (uint64_t)(uintptr_t)local_info, 0, 0, 0);
    TEST_CHECK(g_probeOriginal == 501u && g_probeEntity == 502u && g_regDone == 0,
        "H3d: reinforcement secondary spawned melee clone 502 cleanly after earlier copy == 0 failure");

    /* 2. H3c: Simulate UnitOfEntity failure in AttachCopy (502 present in ENT_STATE_GLOBAL, but missing/corrupt in EM_ENTITY_MAP):
     *    AttachCopy MUST validate UnitOfEntity BEFORE setting g_regDone = 1, clear g_probeEntity = 0, and NEVER allow DoSwap
     *    to write unattached 502 into the secondary inventory slot (+4)! */
    PutHashEntry(g_mock_es_buckets, 64u, 501u, 0u);
    PutHashEntry(g_mock_es_buckets, 64u, 502u, 1u);
    uint32_t owner_av = 0x7001u;
    memcpy(g_mock_es_records + 0u * 0x30u + 4u, &owner_av, 4);

    /* Deliberately register WRONG resource ID in EM_DESCRIPTORS for 502 so UnitOfEntity check fails! */
    PutHashEntry(g_mock_em_buckets, 64u, 502u, 0u);
    uint64_t wrong_res = 0xDEADDEADDEADDEADULL;
    uint32_t clone_eid = 502u, clone_unit = 0x400099u;
    memcpy(g_mock_em + EM_DESCRIPTORS + 0u, &wrong_res, 8);
    memcpy(g_mock_em + EM_DESCRIPTORS + 8u, &clone_eid, 4);
    memcpy(g_mock_em + EM_DESCRIPTORS + 12u, &clone_unit, 4);

    PutHashEntry(g_mock_inv_buckets, 64u, 0x7001u, 0u);
    uint32_t pri_eid = 499u, sec_eid = 501u, sel = 2u;
    memcpy(g_mock_inv_records + 0u, &pri_eid, 4);
    memcpy(g_mock_inv_records + 4u, &sec_eid, 4);
    memcpy(g_mock_inv_records + 0x1Cu, &sel, 4);

    g_probeTick = GetTickCount64() - 2000ULL;
    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    TEST_CHECK(g_attach_calls == 0 && g_probeEntity == 0u && g_probeOriginal == 0u && g_armed == 1,
        "H3c: failed UnitOfEntity aborted AttachCopy, cleared g_probeEntity=0, and re-armed");

    /* Tap secondary key while drawn -> DoSwap MUST NOT trigger or corrupt secondary slot! */
    InterlockedExchange(&g_tapFlag, 1);
    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    uint32_t cur_sec = 0u;
    memcpy(&cur_sec, g_mock_inv_records + 4u, 4);
    TEST_CHECK(g_swapStage == 0 && cur_sec == 501u,
        "H3c: DoSwap refused to swap after failed attach; original pistol 501 untouched in secondary slot");

    TEST_CHECK(hellforge_mod_unload(&g_api, (HfMod*)&g_mod_inst, HF_UNLOAD_REMOVE) == 0, "clean unload after H3c/H3d");
}

static void test_hellforge_05_relocated_4096_entity_manager(const char* temp_dir)
{
    SetupHarness(temp_dir, "saber");
    g_api.version = 20u;
    g_api.entity_of_network = NULL; /* exercise fallback ResolveEntityFromNetId via relocated EM */

    uint8_t* arena = (uint8_t*)calloc(1u, 0x2000000u);
    TEST_CHECK(arena != NULL, "allocated relocated 4,096-record EM slab for melee slot");
    uint8_t* reloc_recs = arena + 0x1500000u; /* 0x1500000 is divisible by 24 */
    int32_t off1 = 0x1500000;
    int32_t off8 = off1 / 8;
    uint32_t cap = 4096u;

    uintptr_t arena_em = (uintptr_t)arena;
    memcpy(g_mock_game + ENTITIES_GLOBAL, &arena_em, sizeof(arena_em));

    uint8_t s_rec8[7] = {0x48u, 0x8Du, 0x80u, 0, 0, 0, 0};
    uint8_t s_rec1[7] = {0x49u, 0x8Du, 0x85u, 0, 0, 0, 0};
    uint8_t s_cnt1[7] = {0x8Bu, 0xD0u, 0xBAu, 0, 0, 0, 0};
    uint8_t s_cnt2[7] = {0xF3u, 0x00u, 0x3Du, 0, 0, 0, 0};
    memcpy(s_rec8 + 3u, &off8, 4u);
    memcpy(s_rec1 + 3u, &off1, 4u);
    memcpy(s_cnt1 + 3u, &cap, 4u);
    memcpy(s_cnt2 + 3u, &cap, 4u);
    memcpy(g_mock_game + CD_EM_SITE_REC8_RVA, s_rec8, 7u);
    memcpy(g_mock_game + CD_EM_SITE_REC1_RVA, s_rec1, 7u);
    memcpy(g_mock_game + CD_EM_SITE_CNT1_RVA, s_cnt1, 7u);
    memcpy(g_mock_game + CD_EM_SITE_CNT2_RVA, s_cnt2, 7u);

    uint8_t net_buckets[64 * 8];
    InitHashHeader(arena + EM_ENTITY_MAP, g_mock_em_buckets, 64u);
    InitHashHeader(arena + EM_NETWORK_MAP, net_buckets, 64u);
    const uint32_t kSlot = 3200u;
    const uint32_t kNet = 0x22u;
    const uint32_t kEnt = 0x7099u;
    const uint32_t kUnit = 0x400123u;
    const uint64_t kRes = 0x8D3D52A3B2F19402ULL;
    PutHashEntry(net_buckets, 64u, kNet, kSlot);
    PutHashEntry(g_mock_em_buckets, 64u, kEnt, kSlot);
    memcpy(reloc_recs + (size_t)kSlot * 24u + 0u, &kRes, 8u);
    memcpy(reloc_recs + (size_t)kSlot * 24u + 8u, &kEnt, 4u);
    memcpy(reloc_recs + (size_t)kSlot * 24u + 12u, &kUnit, 4u);
    memcpy(reloc_recs + (size_t)kSlot * 24u + 16u, &kNet, 4u);

    TEST_CHECK(ResolveEntityFromNetId((uintptr_t)g_mock_game, kNet) == kEnt,
               "ResolveEntityFromNetId resolves relocated slot 3,200 (> 2,048)");
    uint64_t out_res = 0ULL;
    uint32_t out_unit = 0u;
    TEST_CHECK(UnitOfEntity((uintptr_t)g_mock_game, kEnt, &out_res, &out_unit) == 1 &&
               out_res == kRes && out_unit == kUnit,
               "UnitOfEntity resolves relocated slot 3,200 (> 2,048)");

    uintptr_t orig_em = (uintptr_t)g_mock_em;
    memcpy(g_mock_game + ENTITIES_GLOBAL, &orig_em, sizeof(orig_em));
    memset(g_mock_game + CD_EM_SITE_REC8_RVA, 0, 7u);
    memset(g_mock_game + CD_EM_SITE_REC1_RVA, 0, 7u);
    memset(g_mock_game + CD_EM_SITE_CNT1_RVA, 0, 7u);
    memset(g_mock_game + CD_EM_SITE_CNT2_RVA, 0, 7u);
    TEST_CHECK(hellforge_mod_unload(&g_api, (HfMod*)&g_mod_inst, HF_UNLOAD_REMOVE) == 0,
               "clean unload after HellForge 0.5 relocated EM test");
    free(arena);
}

static void test_wilderness_preload_and_residency_gated_deferred_spawn(const char* dir)
{
    if (!dir || !*dir) return;

    /* Start in non-mission state with wilderness streaming (g_auto_resident_on_req = 0 -> parts enter state = 2 LOADING). */
    SetupHarnessEx(dir, "saber", 0, 0);
    TEST_CHECK(g_pkg_calls == 0 && g_avatar == 0u, "clean boot before HF_STATE_PREPARE_MISSION");

    /* 1. Entering HF_STATE_PREPARE_MISSION (6) pre-requests all 7 saber packages immediately with g_avatar == 0
     *    and ZERO non-catalogue hf->load_package calls! */
    HfStateEvent prep_ev = {{sizeof(prep_ev), HF_ON_STATE, 1}, HF_STATE_SHIP, HF_STATE_PREPARE_MISSION};
    g_ls_state((HfMod*)&g_mod_inst, (const HfEventInfo*)&prep_ev, NULL);
    TEST_CHECK(g_avatar == 0u && g_pkg_calls == 7 && g_hf_load_pkg_calls == 0,
        "HF_STATE_PREPARE_MISSION preloaded all 7 saber packages via RefcountedPackageSystem with 0 non-catalogue load_package calls");

    /* 1b. Verify unreadable PKG_EXE_ENGINE fails closed (returns 0 = not resident) without calling hf->load_package! */
    uintptr_t saved_eng = 0u;
    memcpy(&saved_eng, g_mock_exe + PKG_EXE_ENGINE, sizeof(saved_eng));
    memset(g_mock_exe + PKG_EXE_ENGINE, 0, sizeof(saved_eng));
    size_t uninit_res_cnt = 99u;
    uint64_t dummy_pkg = 0x3cfc68e6bf19f67dULL;
    TEST_CHECK(AreMeleePackagesResident((HfMod*)&g_mod_inst, &dummy_pkg, 1u, &uninit_res_cnt) == 0 &&
               uninit_res_cnt == 0u && g_hf_load_pkg_calls == 0,
        "uninitialized PKG_EXE_ENGINE defers safely (0 resident, 0 load_package calls)");
    memcpy(g_mock_exe + PKG_EXE_ENGINE, &saved_eng, sizeof(saved_eng));

    /* 2. Verify metadata-only package (part_count == 0) matches exe+0x321330 and reports resident = 1! */
    UpsertMockEnginePkg(0x1122334455667788ULL, 0u, 0u);
    uintptr_t elist = 0u;
    uint32_t ecount = 0u;
    TEST_CHECK(CanReadEnginePackageList((uintptr_t)g_mock_exe, &elist, &ecount) == 1, "engine package list readable");
    TEST_CHECK(EnginePackageResidentFromList(elist, ecount, 0x1122334455667788ULL) == 1,
        "metadata-only package (part_count == 0) reports resident = 1");

    /* 3. Verify RefcountedPackageSystem <75% headroom guard & already-held dedup! */
    uint64_t ref_ptr = (uint64_t)(uintptr_t)g_mock_pkg_ref_entries;
    uint32_t ref_cap = 16u;
    memcpy(g_mock_pkg_sys + 0u, &ref_ptr, 8u);
    memcpy(g_mock_pkg_sys + 8u, &ref_cap, 4u);
    /* Pre-mark machete's first package 0x8f295b4d04c3a06c as already held (refcount = 2) in RefcountedPackageSystem */
    uint64_t held_machete0 = 0x8f295b4d04c3a06cULL;
    uint32_t refs2 = 2u;
    memcpy(g_mock_pkg_ref_entries + 0u, &held_machete0, 8u);
    memcpy(g_mock_pkg_ref_entries + 8u, &refs2, 4u);

    /* Player switches picker to 'machete' (9 packages) during PREPARE_MISSION loadout!
     * Finding 2: dedicated-melee-slot acquires its OWN +1 ref once per session slot even when
     * held_machete0 is already in RefcountedPackageSystem (7 + 9 = 16), then dedups on repeat ticks! */
    char p[MAX_PATH + 64];
    snprintf(p, sizeof(p), "%s\\melee-slot-weapon.txt", dir);
    FILE* f = fopen(p, "wb");
    if (f) { fprintf(f, "machete\r\n"); fclose(f); }
    g_nextPkgCheckTick = 0ULL;
    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    TEST_CHECK(g_pkg_calls == 16, "PREPARE_MISSION Tick acquired own +1 ref for all 9 machete packages including externally-held slot");
    g_nextPkgCheckTick = 0ULL;
    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    TEST_CHECK(g_pkg_calls == 16, "repeat PREPARE_MISSION Tick did not double-request session-held packages");

    /* Now fill 12 of 16 slots (75% full: (12 + 1) * 4 = 52 >= 48) and switch to 'shovel' -> g_pkgReq refused safely! */
    for (uint32_t fill = 1u; fill < 12u; fill++) {
        uint64_t dummy_id = 0xABCD000000000000ULL + fill;
        uint32_t r1 = 1u;
        memcpy(g_mock_pkg_ref_entries + fill * 16u + 0u, &dummy_id, 8u);
        memcpy(g_mock_pkg_ref_entries + fill * 16u + 8u, &r1, 4u);
    }
    f = fopen(p, "wb");
    if (f) { fprintf(f, "shovel\r\n"); fclose(f); }
    g_nextPkgCheckTick = 0ULL;
    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    TEST_CHECK(g_pkg_calls == 16, "RefcountedPackageSystem >=75% load factor refused unsafe g_pkgReq");

    /* Clear ref map saturation and switch back to 'machete' for wilderness drop test! */
    memset(g_mock_pkg_sys, 0, sizeof(g_mock_pkg_sys));
    f = fopen(p, "wb");
    if (f) { fprintf(f, "machete\r\n"); fclose(f); }

    /* 4. Enter mission on a wilderness map where machete's 9 packages are still streaming (state = 2 != 4)! */
    HfEventInfo mstart = {sizeof(HfStateEvent), HF_ON_MISSION_START, 2};
    g_ls_mstart((HfMod*)&g_mod_inst, &mstart, NULL);
    HfPlayerEvent pev = {{sizeof(pev), HF_ON_PLAYER_SPAWN, 2}, 0xAABBCCDDEEFF0011ULL, 0x9999u, 1u};
    g_ls_pspawn((HfMod*)&g_mod_inst, (const HfEventInfo*)&pev, NULL);
    TEST_CHECK(g_avatar == 0x7001u, "avatar 0x7001 spawned on wilderness map");

    /* Stack-allocated SpawnInfo + 0x900-byte stack context that we will WIPE right after SpawnReqDetour returns
     * to prove deferred spawn never reads dangling caller stack memory! */
    uint8_t stack_ctx[0x900];
    uint8_t stack_info[0x200];
    memset(stack_ctx, 0x5Au, sizeof(stack_ctx));
    memset(stack_info, 0, sizeof(stack_info));
    stack_info[0] = 1u;
    uint32_t pistol_net = 0x333u, out_id = 0u;
    uint64_t ctx_addr = (uint64_t)(uintptr_t)stack_ctx;
    memcpy(stack_info + 4u, &pistol_net, 4u);
    memcpy(stack_info + 0x48u, &ctx_addr, 8u);

    SpawnReqDetour((uint64_t)(uintptr_t)&out_id, 0x8D3D52A3B2F19402ULL, (uint64_t)(uintptr_t)stack_info, 0, 0, 0);
    /* Destroy caller stack buffers immediately! */
    memset(stack_ctx, 0xDDu, sizeof(stack_ctx));
    memset(stack_info, 0xDDu, sizeof(stack_info));

    TEST_CHECK(out_id == 500u && g_spawn_calls == 1,
        "wilderness drop spawned ONLY real pistol 500 while melee packages are still streaming (no purple '?')");
    TEST_CHECK(g_pendingSpawn == 1 && g_probeEntity == 0u && g_armed == 0,
        "extra melee copy gated in g_pendingSpawn until all 9 packages reach state == 4");

    /* Mark 8 of 9 machete packages resident (part 0 & part 1 = 4), leaving the 9th package's part 1 at state = 2! */
    uint64_t machete_pkgs[DMS_MAX_REQ_PACKAGES];
    size_t machete_cnt = ParsePackageIdList(kMelee[0].packages, machete_pkgs, DMS_MAX_REQ_PACKAGES);
    TEST_CHECK(machete_cnt == 9u, "parsed 9 machete packages");
    for (size_t i = 0u; i < 9u; i++) {
        SetMockEnginePkgPartState(machete_pkgs[i], 0u, 4u);
        SetMockEnginePkgPartState(machete_pkgs[i], 1u, (i < 8u) ? 4u : 2u);
    }

    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    TEST_CHECK(g_spawn_calls == 1 && g_pendingSpawn == 1 && g_probeEntity == 0u,
        "Tick still waits when 8/9 packages are resident and 9th has 1 part still streaming");

    /* Final part of 9th package finishes streaming -> state = 4! */
    SetMockEnginePkgPartState(machete_pkgs[8], 1u, 4u);
    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    TEST_CHECK(g_spawn_calls == 2 && g_pendingSpawn == 0 && g_probeOriginal == 500u && g_probeEntity == 501u,
        "Tick spawned extra melee 501 on game thread the instant 9/9 packages became resident");

    uint32_t clone_net = 0u;
    uint64_t clone_ctx = 0ULL;
    memcpy(&clone_net, g_last_spawn_info + 4u, 4u);
    memcpy(&clone_ctx, g_last_spawn_info + 0x48u, 8u);
    TEST_CHECK(clone_net == NET_ID_NONE &&
               clone_ctx == (uint64_t)(uintptr_t)(g_spawnScratch + 0x5000u) &&
               *(uint8_t*)(uintptr_t)clone_ctx == 0x5Au,
        "deferred spawn used persistent 0x8A0+ scratch context (0x5A intact) with net_id = 0x7FFF");

    /* Attach & verify full readiness after residency-gated deferred spawn! */
    PutHashEntry(g_mock_es_buckets, 64u, 500u, 0u);
    PutHashEntry(g_mock_es_buckets, 64u, 501u, 1u);
    uint32_t owner_av = 0x7001u;
    memcpy(g_mock_es_records + 0u * 0x30u + 4u, &owner_av, 4u);
    PutHashEntry(g_mock_em_buckets, 64u, 501u, 0u);
    uint64_t machete_res = 0x792D5D2A340FD6E6ULL;
    uint32_t clone_eid = 501u, clone_unit = 0x400555u;
    memcpy(g_mock_em + EM_DESCRIPTORS + 0u, &machete_res, 8u);
    memcpy(g_mock_em + EM_DESCRIPTORS + 8u, &clone_eid, 4u);
    memcpy(g_mock_em + EM_DESCRIPTORS + 12u, &clone_unit, 4u);
    PutHashEntry(g_mock_inv_buckets, 64u, 0x7001u, 0u);
    uint32_t pri_eid = 499u, sec_eid = 500u, sel = 2u;
    memcpy(g_mock_inv_records + 0u, &pri_eid, 4u);
    memcpy(g_mock_inv_records + 4u, &sec_eid, 4u);
    memcpy(g_mock_inv_records + 0x1Cu, &sel, 4u);

    g_probeTick = GetTickCount64() - 2000ULL;
    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    TEST_CHECK(g_attach_calls == 1 && g_regDone == 1, "deferred wilderness melee 501 attached cleanly");

    /* 5. Verify death while g_pendingSpawn == 1 cancels stale pending spawn and re-arms cleanly! */
    DropAttachedMeleeForRespawn("reset for death-during-pending test");
    SetMockEnginePkgPartState(machete_pkgs[8], 1u, 2u); /* make non-resident again */
    memset(stack_info, 0, sizeof(stack_info));
    stack_info[0] = 1u;
    memcpy(stack_info + 4u, &pistol_net, 4u);
    SpawnReqDetour((uint64_t)(uintptr_t)&out_id, 0x8D3D52A3B2F19402ULL, (uint64_t)(uintptr_t)stack_info, 0, 0, 0);
    TEST_CHECK(g_pendingSpawn == 1 && g_armed == 0, "second wilderness spawn deferred");

    HfPlayerEvent pdown = {{sizeof(pdown), HF_ON_PLAYER_DOWN, 3}, 0xAABBCCDDEEFF0011ULL, 0x9999u, 1u};
    g_ls_pdown((HfMod*)&g_mod_inst, (const HfEventInfo*)&pdown, NULL);
    TEST_CHECK(g_pendingSpawn == 0 && g_probeEntity == 0u && g_armed == 1,
        "HF_ON_PLAYER_DOWN cancelled pending wilderness spawn and re-armed cleanly");

    TEST_CHECK(hellforge_mod_unload(&g_api, (HfMod*)&g_mod_inst, HF_UNLOAD_REMOVE) == 0,
        "clean unload after wilderness deferred spawn suite");
}

static void test_finding2_multimission_external_ref_unload_recovery(const char* dir)
{
    if (!dir || !*dir) return;

    SetupHarnessEx(dir, "machete", 1, 0);
    g_sim_refmap_updates = 1;

    uint64_t machete_pkgs[DMS_MAX_REQ_PACKAGES];
    size_t machete_cnt = ParsePackageIdList(kMelee[0].packages, machete_pkgs, DMS_MAX_REQ_PACKAGES);
    TEST_CHECK(machete_cnt == 9u, "Finding 2: parsed 9 machete packages");

    uint64_t ref_ptr = (uint64_t)(uintptr_t)g_mock_pkg_ref_entries;
    uint32_t ref_cap = 32u;
    memcpy(g_mock_pkg_sys + 0u, &ref_ptr, 8u);
    memcpy(g_mock_pkg_sys + 8u, &ref_cap, 4u);

    /* Co-op teammate already holds machete_pkgs[0] (refs = 1) on Mission 1 before our mission start! */
    uint32_t ext_refs = 1u;
    memcpy(g_mock_pkg_ref_entries + 0u, &machete_pkgs[0], 8u);
    memcpy(g_mock_pkg_ref_entries + 8u, &ext_refs, 4u);

    /* Mission 1 start: we MUST still acquire our own +1 ref on machete_pkgs[0] (bringing refs 1 -> 2)! */
    HfEventInfo m1_start = {sizeof(HfStateEvent), HF_ON_MISSION_START, 1};
    g_ls_mstart((HfMod*)&g_mod_inst, &m1_start, NULL);
    uint32_t slot0_refs = 0u;
    memcpy(&slot0_refs, g_mock_pkg_ref_entries + 8u, 4u);
    TEST_CHECK(g_pkg_calls == 9 && slot0_refs == 2u,
        "Finding 2: Mission 1 acquired own +1 ref alongside external holder (refs 1 -> 2)");

    /* Mission 1 ends and co-op teammate leaves (decrementing external ref 2 -> 1; package stays alive!). */
    HfEventInfo m1_end = {sizeof(HfStateEvent), HF_ON_MISSION_END, 2};
    g_ls_mend((HfMod*)&g_mod_inst, &m1_end, NULL);
    slot0_refs--;
    memcpy(g_mock_pkg_ref_entries + 8u, &slot0_refs, 4u);
    TEST_CHECK(PkgSystemRefMapQuery((uintptr_t)g_mock_pkg_sys, machete_pkgs[0]) == 2,
        "Finding 2: machete_pkgs[0] remains held (refs = 1) after external teammate unloads");

    /* Now test defense-in-depth recovery: simulate an engine transition wiping machete_pkgs[0] from the
     * RefcountedPackageSystem map AND evicting its parts to non-resident (state = 2) on a Mission 2
     * wilderness drop while g_sessionHeldSysReq[0] == 1 and g_pkgDone == 1! */
    HfEventInfo m2_start = {sizeof(HfStateEvent), HF_ON_MISSION_START, 3};
    g_ls_mstart((HfMod*)&g_mod_inst, &m2_start, NULL);
    TEST_CHECK(g_pkg_calls == 9, "Finding 2: Mission 2 start made 0 redundant g_pkgReq calls while refs intact");

    HfPlayerEvent pev = {{sizeof(pev), HF_ON_PLAYER_SPAWN, 3}, 0xAABBCCDDEEFF0011ULL, 0x9999u, 1u};
    g_ls_pspawn((HfMod*)&g_mod_inst, (const HfEventInfo*)&pev, NULL);

    /* External/transition wipe drops machete_pkgs[0] from refmap and unloads part 0 in engine list! */
    memset(g_mock_pkg_ref_entries + 0u, 0, 16u);
    SetMockEnginePkgPartState(machete_pkgs[0], 0u, 2u);
    g_auto_resident_on_req = 0; /* g_pkgReq will start streaming (state = 2) */

    uint8_t stack_info[0x200];
    memset(stack_info, 0, sizeof(stack_info));
    stack_info[0] = 1u;
    uint32_t pistol_net = 0x444u, out_id = 0u;
    memcpy(stack_info + 4u, &pistol_net, 4u);
    g_spawn_calls = 0;
    g_last_pkg_req_id = 0ULL;
    SpawnReqDetour((uint64_t)(uintptr_t)&out_id, 0x8D3D52A3B2F19402ULL, (uint64_t)(uintptr_t)stack_info, 0, 0, 0);
    memcpy(&slot0_refs, g_mock_pkg_ref_entries + 8u, 4u);
    TEST_CHECK(g_pendingSpawn == 1 && g_spawn_calls == 1,
        "Finding 2: Mission 2 wilderness drop deferred melee spawn when machete_pkgs[0] non-resident");
    TEST_CHECK(g_pkg_calls == 10 && g_last_pkg_req_id == machete_pkgs[0] && slot0_refs == 1u,
        "Finding 2: SpawnReqDetour immediately re-verified refmap and re-requested ONLY dropped machete_pkgs[0]");

    /* Also wipe machete_pkgs[1] AFTER SpawnReqDetour while g_pendingSpawn == 1 -> ProcessPendingSpawn MUST
     * detect PkgSystemRefMapQuery != 2 despite g_pkgDone == 1, clear g_sessionHeldSysReq[1], and re-request it! */
    memset(g_mock_pkg_ref_entries + 16u, 0, 16u);
    SetMockEnginePkgPartState(machete_pkgs[1], 0u, 2u);
    g_last_pkg_req_id = 0ULL;
    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    uint32_t slot1_refs = 0u;
    memcpy(&slot1_refs, g_mock_pkg_ref_entries + 16u + 8u, 4u);
    TEST_CHECK(g_pkg_calls == 11 && g_last_pkg_req_id == machete_pkgs[1] && slot1_refs == 1u,
        "Finding 2: ProcessPendingSpawn re-verified refmap, cleared stale held flag, and re-requested ONLY dropped machete_pkgs[1]");

    /* Streaming finishes -> next Tick spawns extra melee copy cleanly without stranding! */
    SetMockEnginePkgPartState(machete_pkgs[0], 0u, 4u);
    SetMockEnginePkgPartState(machete_pkgs[0], 1u, 4u);
    SetMockEnginePkgPartState(machete_pkgs[1], 0u, 4u);
    SetMockEnginePkgPartState(machete_pkgs[1], 1u, 4u);
    g_cb_tick((HfMod*)&g_mod_inst, 0.016f, NULL);
    TEST_CHECK(g_pendingSpawn == 0 && g_spawn_calls == 2 && g_probeEntity != 0u,
        "Finding 2: Mission 2 wilderness deferred spawn completed immediately once re-acquired package finished streaming");

    g_sim_refmap_updates = 0;
    TEST_CHECK(hellforge_mod_unload(&g_api, (HfMod*)&g_mod_inst, HF_UNLOAD_REMOVE) == 0,
        "clean unload after Finding 2 multi-mission recovery suite");
}

int main(int argc, char** argv)
{
    const char* temp_dir = argc > 1 ? argv[1] : NULL;
    test_offline_game_dll_pins_if_present();
    test_secondary_table_and_modded_weapons(temp_dir);
    test_coop_hotjoin_authority_netid_attach_and_swap(temp_dir);
    test_keybind_parser_and_clean_thread_shutdown(temp_dir);
    test_h3c_h3d_failed_spawn_and_failed_attach_recovery(temp_dir);
    test_hellforge_05_relocated_4096_entity_manager(temp_dir);
    test_wilderness_preload_and_residency_gated_deferred_spawn(temp_dir);
    test_finding2_multimission_external_ref_unload_recovery(temp_dir);

    printf("dedicated-melee-slot C11 unit suite: %d checks run, %d failed\n", g_tests_run, g_tests_failed);
    return g_tests_failed == 0 ? 0 : 1;
}
