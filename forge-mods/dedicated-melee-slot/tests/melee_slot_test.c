/* Comprehensive C11 unit & offline-capture verification suite for Dedicated Melee Slot 1.5.2-clonedivers.2.
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
 *   7. Death / reinforcement drop & re-arm + input_settings.config custom keybind parsing + clean thread stop. */

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

/* ---- Mock Game Memory & HfApi ----------------------------------------------------------------------------------- */

static uint8_t g_mock_game[0x3500000];
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

static uint32_t g_next_spawn_id = 500u;
static int g_spawn_calls = 0;
static uint64_t g_last_spawn_res = 0ULL;
static uint8_t g_last_spawn_info[0x60];
static int g_corrupt_csr_in_spawn = 0;
static int g_force_zero_clone_spawn = 0;
static int g_attach_calls = 0;
static int g_pkg_calls = 0;
static uint64_t g_custom_mod_sec_ent = 0xCAFE000012345678ULL;
static char g_mock_mod_folder[MAX_PATH + 128];

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
    if (sz) *sz = sizeof(g_mock_game);
    if (strcmp(name, "game.dll") == 0) return (uintptr_t)g_mock_game;
    return 0;
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
    (void)sys; (void)id_ptr; (void)one; (void)d; (void)e; (void)f;
    g_pkg_calls++;
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

static void SetupHarness(const char* cfg_dir, const char* weapon_choice)
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
    g_api.version = 16;
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
    g_api.entity_of_network = MockEntityOfNetwork;
    g_api.global = MockGlobal;
    g_api.content = MockContent;
    g_mod_inst.api = &g_api;

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
    g_mock_mod_folder[0] = '\0';
    InterlockedExchange(&g_testSentKeyDowns, 0);
    InterlockedExchange(&g_testSentKeyUps, 0);

    int rc = hellforge_mod_init(&g_api, (HfMod*)&g_mod_inst);
    if (rc != 0) abort();
    g_spawnReq = MockOrigSpawnReq;
    g_attachFn = MockAttachFn;
    g_pkgReq = MockPkgReqFn;
    g_regOk = 1;

    HfEventInfo mstart = {sizeof(HfStateEvent), HF_ON_MISSION_START, 1};
    g_ls_mstart((HfMod*)&g_mod_inst, &mstart, NULL);
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
    TEST_CHECK(g_spawn_calls == 4, "secondary + extra melee clone spawned");
    TEST_CHECK(g_probeOriginal == 502u && g_probeEntity == 503u, "tracked original pistol 502 and melee clone 503");

    uint32_t clone_net = 0u;
    uint64_t clone_stack_ptr = 1ULL;
    memcpy(&clone_net, g_last_spawn_info + 4u, 4);
    memcpy(&clone_stack_ptr, g_last_spawn_info + 0x50u, 8);
    TEST_CHECK(g_last_spawn_info[0] == 1u && clone_net == NET_ID_NONE,
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
    TEST_CHECK(g_pkg_calls == 9, "requested all 9 Machete model packages");

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
    TEST_CHECK(ResolveInstallRootFromModFolder("D:\\HellForge\\mods\\dedicated-melee-slot", resolved_root, sizeof(resolved_root)) == 1 &&
               strcmp(resolved_root, "D:\\HellForge") == 0,
               "L3: resolved D:\\HellForge from folder install");
    TEST_CHECK(ResolveInstallRootFromModFolder("D:/HellForge/mods/.unpacked/pkg999/dedicated-melee-slot/", resolved_root, sizeof(resolved_root)) == 1 &&
               strcmp(resolved_root, "D:\\HellForge") == 0,
               "L3: resolved D:\\HellForge from .unpacked zip install");

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

int main(int argc, char** argv)
{
    const char* temp_dir = argc > 1 ? argv[1] : NULL;
    test_offline_game_dll_pins_if_present();
    test_secondary_table_and_modded_weapons(temp_dir);
    test_coop_hotjoin_authority_netid_attach_and_swap(temp_dir);
    test_keybind_parser_and_clean_thread_shutdown(temp_dir);
    test_h3c_h3d_failed_spawn_and_failed_attach_recovery(temp_dir);

    printf("dedicated-melee-slot C11 unit suite: %d checks run, %d failed\n", g_tests_run, g_tests_failed);
    return g_tests_failed == 0 ? 0 : 1;
}
