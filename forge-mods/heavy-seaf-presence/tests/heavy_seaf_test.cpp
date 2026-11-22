/* Unit tests for Heavy SEAF Presence 1.2.0-clonedivers.5:
 *   1. 10-argument ActivateStratagemBeacon (0x6ABDB0) ABI contract (verifying s7..s10 == 0 under stack poison)
 *   2. Positive mission-ground landing gate (immune to stale ship spawn, 15s grace, 4->5 reset, demotion reset)
 *   3. 1-unit/tick synchronous squad equip budget & QPC ring-buffer breadcrumbs
 *   4. clonedivers-host-options.json ("standard" / "heavy" / "constant") + default constant=0 + mtime precedence
 */
#define HEAVY_SEAF_UNIT_TEST 1
#include "../src/heavy_seaf_presence.cpp"
#include "../src/options_menu.cpp"
#include "../src/tm_tick.cpp"

/* Stubs for squad delivery externs so heavy_seaf_presence + options_menu link cleanly in isolation */
extern "C" int heavy_seaf_squads_init(const HfApi*, HfMod*) { return 0; }
extern "C" int heavy_squad_pool_init(const HfApi*, HfMod*) { return 0; }
extern "C" int heavy_seaf_squads_ready(void) { return 1; }
extern "C" int heavy_seaf_squads_can_spawn(void) { return 1; }
extern "C" int heavy_seaf_spawn_squad(unsigned, const float*) { return 1; }
extern "C" int heavy_seaf_support_call(unsigned, unsigned, const float*) { return 1; }
extern "C" int heavy_seaf_live_troops(void) { return 0; }

static unsigned g_checks = 0;
static unsigned g_failed = 0;

#define CHECK(cond, msg) do { \
    ++g_checks; \
    if (!(cond)) { \
        ++g_failed; \
        fprintf(stderr, "FAIL [%s:%d]: %s\n", __FILE__, __LINE__, (msg)); \
    } \
} while (0)

/* ---- Mock game.dll arena & HfApi ---- */
static uint32_t g_mock_role = HF_SESSION_SOLO;
static uint32_t g_mock_state = HF_STATE_SHIP;
static char g_mock_folder[512];
static uint32_t g_mock_player_life = 3u; /* 3 even on ship -> reproduces stale ship spawn! */
static uint32_t g_mock_mission_mode = 1u; /* 1 on ship, 3 on mission ground */
static int32_t g_mock_avatar_hp = 100;

static uintptr_t g_fake_game = 0x0000019000000000ULL;
static uint8_t g_ai_director[0x52000];
static uint8_t g_context_obj[0xAF000];
static uint8_t g_settings_obj[0x400];
static uint8_t g_players_obj[0x600];
static uint8_t g_player_rec[32];
static uint32_t g_player_map_buckets[16 * 2];
static uint8_t g_factory_obj[0xF34000];
static uint32_t g_net_map_buckets[16 * 2];
static uint8_t g_user_obj[0xC000];
static uint8_t g_transform_src[0x4900];
static uint32_t g_transform_buckets[16 * 2];
static uint8_t g_transform_rows[4 * 0xA4];
static uint8_t g_health_mgr[0x1100];
static uint32_t g_health_buckets[16 * 2];
static uint8_t g_health_rows[4 * 0x1B8];
static uint8_t g_strat_def_116[0x200];
static uint8_t g_strat_row_custom[0x200];
static uint64_t g_strat_payload_buf[2] = { 0x1122334455667788ULL, 0ULL };
static uint8_t g_strat_manager[144];

/* Recorded 10-arg ActivateStratagemBeacon call values */
static unsigned g_activate_calls = 0;
static int32_t g_last_element = 0;
static uint32_t g_last_thrower = 0;
static uint32_t g_last_type = 0;
static uint64_t g_last_s7 = 0xFFFFFFFFFFFFFFFFULL;
static uint64_t g_last_s8 = 0xFFFFFFFFFFFFFFFFULL;
static uint64_t g_last_s9 = 0xFFFFFFFFFFFFFFFFULL;
static uint64_t g_last_s10 = 0xFFFFFFFFFFFFFFFFULL;

static uint64_t mock_activate_10arg(void* manager, int32_t element, uint32_t thrower,
                                    const float* position, void* record, uint32_t type,
                                    uint64_t s7, uint64_t s8, uint64_t s9, uint64_t s10)
{
    (void)manager; (void)position; (void)record;
    ++g_activate_calls;
    g_last_element = element;
    g_last_thrower = thrower;
    g_last_type = type;
    g_last_s7 = s7;
    g_last_s8 = s8;
    g_last_s9 = s9;
    g_last_s10 = s10;
    return 1ULL;
}

static unsigned char mock_search_site(void* mgr_copy, uint32_t zero, const void* src, float* approach, int flag)
{
    (void)zero; (void)src; (void)flag;
    uintptr_t ctl = 0;
    memcpy(&ctl, (const unsigned char*)mgr_copy + 0x68, 8);
    float landing[3] = { approach[0] + 5.0f, approach[1] + 5.0f, approach[2] };
    memcpy((void*)(ctl + 0x650), landing, 12);
    return 1;
}

/* Poison stack slots before calling CallSeaf / Tick to catch any uninitialized tail-arg leak! */
static __attribute__((noinline)) void poison_stack(void)
{
    volatile uint64_t junk[32];
    for (int i = 0; i < 32; ++i) junk[i] = 0xDEADBEEFCAFEBABEULL ^ (uint64_t)i;
    (void)junk[0];
}

static void init_mock_world(void)
{
    memset(g_ai_director, 0, sizeof g_ai_director);
    memset(g_context_obj, 0, sizeof g_context_obj);
    memset(g_settings_obj, 0, sizeof g_settings_obj);
    memset(g_players_obj, 0, sizeof g_players_obj);
    memset(g_player_rec, 0, sizeof g_player_rec);
    memset(g_factory_obj, 0, sizeof g_factory_obj);
    memset(g_user_obj, 0, sizeof g_user_obj);
    memset(g_transform_src, 0, sizeof g_transform_src);
    memset(g_transform_rows, 0, sizeof g_transform_rows);
    memset(g_health_mgr, 0, sizeof g_health_mgr);
    memset(g_health_rows, 0, sizeof g_health_rows);
    memset(g_strat_def_116, 0, sizeof g_strat_def_116);
    memset(g_strat_row_custom, 0, sizeof g_strat_row_custom);
    memset(g_strat_manager, 0, sizeof g_strat_manager);

    /* Director: 1 SEAF roster entry + SEAF_TAG */
    uint32_t rcount = 1u, tcount = 1u, seaf_tag = SEAF_TAG;
    memcpy(g_ai_director + 0x518ACu, &rcount, 4);
    memcpy(g_ai_director + 0x51914u, &tcount, 4);
    memcpy(g_ai_director + 0x518D4u, &seaf_tag, 4);

    /* Context -> Settings (mode at +0x1E4, section ptr at +0x2C8) */
    uintptr_t settings_ptr = (uintptr_t)g_settings_obj;
    memcpy(g_context_obj + 0xAE288u, &settings_ptr, 8);
    uintptr_t section_ptr = 0x0000019012340000ULL;
    memcpy(g_settings_obj + 0x1E4u, &g_mock_mission_mode, 4);
    memcpy(g_settings_obj + 0x2C8u, &section_ptr, 8);

    /* LocalUser peer = 0xABCD12345678ULL, placed in Co-op Slot 2 (testing non-slot-0!) */
    uint64_t local_peer = 0xABCD12345678ULL;
    memcpy(g_user_obj + 0xB398u, &local_peer, 8);

    uint32_t pcount = 3u, locals = 1u;
    memcpy(g_players_obj + 0x84u, &pcount, 4);
    memcpy(g_players_obj + 0x88u, &locals, 4);

    /* Slot 2 = local player: owner=0x77, net=0x33, char_id=0x555 */
    uint32_t owner_id = 0x77u, net_id = 0x33u, char_id = 0x555u;
    memcpy(g_player_rec + 8u, &owner_id, 4);
    uintptr_t prec_ptr = (uintptr_t)g_player_rec;
    memcpy(g_players_obj + 0x2C8u + 2u * 0x38u, &local_peer, 8);
    memcpy(g_players_obj + 0x2E0u + 2u * 0x38u, &g_mock_player_life, 4);
    memcpy(g_players_obj + 0x3A8u + 2u * 0x20u, &net_id, 4);
    memcpy(g_players_obj + 0xE8u + 2u * 8u, &prec_ptr, 8);

    /* Player owner map at players + 0xD0 */
    for (int i = 0; i < 32; ++i) g_player_map_buckets[i] = 0xFFFFFFFFu;
    g_player_map_buckets[(owner_id & 15u) * 2u] = owner_id;
    g_player_map_buckets[(owner_id & 15u) * 2u + 1u] = 2u;
    uintptr_t pmap_ptr = (uintptr_t)g_player_map_buckets;
    uint32_t map_meta[3] = { 16u, 0xFFFFFFFFu, 1u };
    memcpy(g_players_obj + 0xD0u, &pmap_ptr, 8);
    memcpy(g_players_obj + 0xD8u, map_meta, 12);

    /* Factory network->entity map at factory + 0xF22EC8 */
    for (int i = 0; i < 32; ++i) g_net_map_buckets[i] = 0xFFFFFFFFu;
    g_net_map_buckets[(net_id & 15u) * 2u] = net_id;
    g_net_map_buckets[(net_id & 15u) * 2u + 1u] = 5u;
    uintptr_t nmap_ptr = (uintptr_t)g_net_map_buckets;
    memcpy(g_factory_obj + 0xF22EC8u, &nmap_ptr, 8);
    memcpy(g_factory_obj + 0xF22ED0u, map_meta, 12);
    memcpy(g_factory_obj + 0xF32F18u + 5u * 24u + 8u, &char_id, 4);
    memcpy(g_factory_obj + 0xF32F18u + 5u * 24u + 16u, &net_id, 4);

    /* Transform map for char_id=0x555 -> position (100, 200, 15) */
    for (int i = 0; i < 32; ++i) g_transform_buckets[i] = 0xFFFFFFFFu;
    g_transform_buckets[(char_id & 15u) * 2u] = char_id;
    g_transform_buckets[(char_id & 15u) * 2u + 1u] = 1u;
    uintptr_t tmap_ptr = (uintptr_t)g_transform_buckets;
    uintptr_t trows_ptr = (uintptr_t)g_transform_rows;
    memcpy(g_transform_src + 0x48A0u, &tmap_ptr, 8);
    memcpy(g_transform_src + 0x48A8u, map_meta, 12);
    memcpy(g_transform_src + 0x48D0u, &trows_ptr, 8);
    float pos[3] = { 100.0f, 200.0f, 15.0f };
    memcpy(g_transform_rows + 1u * 0xA4u, pos, 12);

    /* HealthManager for char_id=0x555 */
    for (int i = 0; i < 32; ++i) g_health_buckets[i] = 0xFFFFFFFFu;
    g_health_buckets[(char_id & 15u) * 2u] = char_id;
    g_health_buckets[(char_id & 15u) * 2u + 1u] = 1u;
    uint32_t hcap = 4u;
    uintptr_t hmap_ptr = (uintptr_t)g_health_buckets;
    uintptr_t hrows_ptr = (uintptr_t)g_health_rows;
    memcpy(g_health_mgr + 0x1020u, &hcap, 4);
    memcpy(g_health_mgr + 0x1030u, &hmap_ptr, 8);
    memcpy(g_health_mgr + 0x1038u, map_meta, 12);
    memcpy(g_health_mgr + 0x1058u, &hrows_ptr, 8);
    uint32_t hlife = 0u;
    memcpy(g_health_rows + 1u * 0x1B8u + 0x14u, &g_mock_avatar_hp, 4);
    memcpy(g_health_rows + 1u * 0x1B8u + 0x19Cu, &hlife, 4);

    /* Stratagem 116 definition & StratagemManager */
    uint32_t stype = 116u, sdeliv = 6u, smark = 0xC0u, swaves = 0u, spcount = 1u;
    uint64_t spayload = (uint64_t)(uintptr_t)g_strat_payload_buf;
    uint64_t spkg = 0x9988776655443322ULL;
    memcpy(g_strat_def_116 + 0x00u, &stype, 4);
    memcpy(g_strat_def_116 + 0x3Cu, &sdeliv, 4);
    memcpy(g_strat_def_116 + 0x78u, &smark, 4);
    memcpy(g_strat_def_116 + 0x98u, &spayload, 8);
    memcpy(g_strat_def_116 + 0xA0u, &spcount, 4);
    memcpy(g_strat_def_116 + 0xA8u, &spkg, 8);
    memcpy(g_strat_def_116 + 0x12Cu, &swaves, 4);

    uintptr_t world_ptr = 0x0000019099990000ULL;
    memcpy(g_strat_manager + 8u, &world_ptr, 8);
}

static int mock_read(uintptr_t at, void* out, size_t n)
{
    static const unsigned char SEARCH_BYTES[] = { 0x40,0x55,0x53,0x56,0x57,0x41,0x54,0x41,0x55,0x41,0x56,0x48,0x8d,0x6c,0x24,0x80,0x48,0x81,0xec,0x80,0x01,0x00,0x00,0x48,0x8b,0x05,0xf2,0x30,0xf9,0x01 };
    static const unsigned char QUERY_BYTES[] = { 0x48,0x8b,0xc4,0x48,0x89,0x58,0x20,0x55,0x56,0x57,0x41,0x54,0x41,0x55,0x41,0x56,0x41,0x57,0x48,0x8d,0xa8,0x48,0xfe,0xff,0xff };
    static const unsigned char DISPATCH_BYTES[] = { 0x48,0x89,0x5c,0x24,0x18,0x55,0x56,0x57,0x41,0x54,0x41,0x55,0x41,0x56,0x41,0x57,0x48,0x8d,0xac,0x24,0xb0,0xfc,0xff,0xff,0x48,0x81,0xec,0x50,0x04,0x00,0x00,0x48,0x8b,0x05,0x3a,0x02,0xf9,0x01 };

    /* Keep dynamic mock globals synced into structs */
    memcpy(g_settings_obj + 0x1E4u, &g_mock_mission_mode, 4);
    memcpy(g_players_obj + 0x2E0u + 2u * 0x38u, &g_mock_player_life, 4);
    memcpy(g_health_rows + 1u * 0x1B8u + 0x14u, &g_mock_avatar_hp, 4);

    if (at == g_fake_game + 0x6A8F00u && n <= sizeof SEARCH_BYTES) { memcpy(out, SEARCH_BYTES, n); return 0; }
    if (at == g_fake_game + 0x13CAA80u && n <= sizeof QUERY_BYTES) { memcpy(out, QUERY_BYTES, n); return 0; }
    if (at == g_fake_game + 0x6ABDB0u && n <= sizeof DISPATCH_BYTES) { memcpy(out, DISPATCH_BYTES, n); return 0; }
    if (at == g_fake_game + DIRECTOR_RVA && n == 8) { uintptr_t p = (uintptr_t)g_ai_director; memcpy(out, &p, 8); return 0; }
    if (at == g_fake_game + 0x3326340u && n == 8) { uintptr_t p = (uintptr_t)g_context_obj; memcpy(out, &p, 8); return 0; }
    if (at == g_fake_game + 0x3483C34u && n == 4) { uint32_t inv = 0xFFFFFFFFu; memcpy(out, &inv, 4); return 0; }
    if (at == g_fake_game + 0x3326468u && n == 8) { uintptr_t p = (uintptr_t)g_players_obj; memcpy(out, &p, 8); return 0; }
    if (at == g_fake_game + 0x346BF98u && n == 8) { uintptr_t p = (uintptr_t)g_factory_obj; memcpy(out, &p, 8); return 0; }
    if (at == g_fake_game + 0x347CEF0u && n == 8) { uintptr_t p = (uintptr_t)g_user_obj; memcpy(out, &p, 8); return 0; }
    if (at == g_fake_game + 0x3326558u && n == 8) { uintptr_t p = (uintptr_t)g_transform_src; memcpy(out, &p, 8); return 0; }
    if (at == g_fake_game + 0x3326688u && n == 8) { uintptr_t p = (uintptr_t)g_health_mgr; memcpy(out, &p, 8); return 0; }
    if (at == g_fake_game + 0x37CB600u + 116u * 8u && n == 8) { uintptr_t p = (uintptr_t)g_strat_def_116; memcpy(out, &p, 8); return 0; }
    if (at == g_fake_game + 0x33266B0u && n == 8) { uintptr_t p = (uintptr_t)g_strat_manager; memcpy(out, &p, 8); return 0; }

    struct Region { uintptr_t base; size_t size; };
    const Region regions[] = {
        { (uintptr_t)g_ai_director, sizeof g_ai_director },
        { (uintptr_t)g_context_obj, sizeof g_context_obj },
        { (uintptr_t)g_settings_obj, sizeof g_settings_obj },
        { (uintptr_t)g_players_obj, sizeof g_players_obj },
        { (uintptr_t)g_player_rec, sizeof g_player_rec },
        { (uintptr_t)g_player_map_buckets, sizeof g_player_map_buckets },
        { (uintptr_t)g_factory_obj, sizeof g_factory_obj },
        { (uintptr_t)g_net_map_buckets, sizeof g_net_map_buckets },
        { (uintptr_t)g_user_obj, sizeof g_user_obj },
        { (uintptr_t)g_transform_src, sizeof g_transform_src },
        { (uintptr_t)g_transform_buckets, sizeof g_transform_buckets },
        { (uintptr_t)g_transform_rows, sizeof g_transform_rows },
        { (uintptr_t)g_health_mgr, sizeof g_health_mgr },
        { (uintptr_t)g_health_buckets, sizeof g_health_buckets },
        { (uintptr_t)g_health_rows, sizeof g_health_rows },
        { (uintptr_t)g_strat_def_116, sizeof g_strat_def_116 },
        { (uintptr_t)g_strat_row_custom, sizeof g_strat_row_custom },
        { (uintptr_t)g_strat_payload_buf, sizeof g_strat_payload_buf },
        { (uintptr_t)g_strat_manager, sizeof g_strat_manager },
    };
    for (const auto& r : regions) {
        if (at >= r.base && at + n <= r.base + r.size) {
            memcpy(out, (const void*)at, n);
            return 0;
        }
    }
    return 1;
}

static int mock_write(uintptr_t at, const void* src, size_t n)
{
    if (at >= (uintptr_t)g_strat_row_custom && at + n <= (uintptr_t)g_strat_row_custom + sizeof g_strat_row_custom) {
        memcpy((void*)at, src, n);
        return 0;
    }
    if (at >= (uintptr_t)g_ai_director && at + n <= (uintptr_t)g_ai_director + sizeof g_ai_director) {
        memcpy((void*)at, src, n);
        return 0;
    }
    return 1;
}

static void mock_log(HfMod*, const char*, ...) {}
static const char* mock_build_id(void) { return "25480438"; }
static const char* mock_mod_folder(HfMod*) { return g_mock_folder; }
static void mock_session(HfMod*, HfSession* out) { memset(out, 0, sizeof *out); out->role = g_mock_role; out->version = 1u; }
static uint32_t mock_game_state(HfMod*) { return g_mock_state; }
static uintptr_t mock_module_base(const char* name, size_t* sz)
{
    if (strcmp(name, "game.dll") == 0) { if (sz) *sz = 74727424u; return g_fake_game; }
    return 0;
}
static int mock_code_matches(uintptr_t, const char*) { return 1; }
static uint32_t mock_add_stratagem(HfMod*, const HfStratagem*) { return 501u; }
static uintptr_t mock_stratagem_row(uint32_t id) { return id == 501u ? (uintptr_t)g_strat_row_custom : 0u; }
static int mock_subscribe(HfMod*, uint32_t, HfListener, void*, int32_t) { return 1; }
static HfEventCallback g_session_end_cb = NULL;
static int mock_on(HfMod*, HfEvent ev, HfEventCallback cb, void*) { if (ev == HF_EVENT_SESSION_END) g_session_end_cb = cb; return 0; }
static HfTickCallback g_tick_cb = NULL;
static int mock_on_game_thread(HfMod*, HfTickCallback cb, void*) { g_tick_cb = cb; return 1; }

static int write_file_mtime(const char* path, const char* text, uint64_t mtime_100ns)
{
    FILE* f = fopen(path, "wb");
    if (!f) return 0;
    size_t n = strlen(text);
    size_t wr = fwrite(text, 1, n, f);
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

int main(int argc, char** argv)
{
    const char* root = (argc > 1) ? argv[1] : "D:\\ClaudeWork\\workstreams-v21\\code-mods\\heavy-seaf-presence\\build\\fixture";
    char mods_dir[512], unpacked_base[512], unpacked_pkg[512], unpacked_mod[512], ini_path[512], host_json[512];
    snprintf(mods_dir, sizeof mods_dir, "%s\\mods", root);
    snprintf(unpacked_base, sizeof unpacked_base, "%s\\mods\\.unpacked", root);
    snprintf(unpacked_pkg, sizeof unpacked_pkg, "%s\\mods\\.unpacked\\seaf5", root);
    snprintf(unpacked_mod, sizeof unpacked_mod, "%s\\mods\\.unpacked\\seaf5\\heavy", root);
    snprintf(ini_path, sizeof ini_path, "%s\\options.ini", unpacked_mod);
    snprintf(host_json, sizeof host_json, "%s\\mods\\clonedivers-host-options.json", root);

    CreateDirectoryA(root, NULL);
    CreateDirectoryA(mods_dir, NULL);
    CreateDirectoryA(unpacked_base, NULL);
    CreateDirectoryA(unpacked_pkg, NULL);
    CreateDirectoryA(unpacked_mod, NULL);
    SetEnvironmentVariableA("LOCALAPPDATA", root);
    DeleteFileA(host_json);

    CHECK(write_file_mtime(ini_path, "constant=0\nsupport_squads=0\ntank_hunters=0\ninterval_seconds=120\nsquads_per_wave=1\n", 134000000000000000ULL),
          "write default options.ini");
    snprintf(g_mock_folder, sizeof g_mock_folder, "%s", unpacked_mod);

    init_mock_world();
    g_test_search_override = mock_search_site;
    g_test_activate_override = mock_activate_10arg;

    HfApi api{};
    api.size = sizeof api;
    api.version = 16;
    api.log = mock_log;
    api.build_id = mock_build_id;
    api.mod_folder = mock_mod_folder;
    api.session = mock_session;
    api.game_state = mock_game_state;
    api.module_base = mock_module_base;
    api.read = mock_read;
    api.write = mock_write;
    api.code_matches = mock_code_matches;
    api.add_stratagem = mock_add_stratagem;
    api.stratagem_row = mock_stratagem_row;
    api.subscribe = mock_subscribe;
    api.on = mock_on;
    api.on_game_thread = mock_on_game_thread;

    const HfApi* api_ptr = &api;
    HfMod* fake_mod = (HfMod*)&api_ptr;

    /* 1. Default startup without clonedivers-host-options.json -> constant MUST default to 0! */
    api.version = 15;
    CHECK(hellforge_mod_init(&api, fake_mod) == 1, "refuse SDK < 16");
    api.version = 16;
    CHECK(hellforge_mod_init(&api, fake_mod) == 0, "init succeeds on SDK 16");
    CHECK(OptionsConstant() == 0, "constant defaults to 0 when host options absent");
    CHECK(OptionsExtraSquads() == 0, "extra_squads defaults to 0");

    /* 2. Host options presets ("standard", "heavy", "constant") + mtime precedence */
    const uint64_t t0 = 134300000000000000ULL;
    CHECK(write_file_mtime(host_json, "{\"format\":1,\"chaosIntensity\":\"standard\",\"heavySeaf\":\"heavy\",\"fasterCorpseCleanup\":true}", t0),
          "write heavySeaf=heavy");
    OptionsMenuPoll(1.5f);
    CHECK(OptionsConstant() == 0 && OptionsSupportSquads() == 1 && OptionsExtraSquads() == 2 && OptionsSquadsPerWave() == 2,
          "heavySeaf=heavy sets support=1, extra=2 while keeping constant=0!");

    /* Live in-game MODS page edit via HeavySeafMenu.values with newer mtime (t0 + 40s) */
    CHECK(write_file_mtime(g_bridge_path, "constant=0\nsupport_squads=0\ntank_hunters=0\ninterval_seconds=180\nsquads_per_wave=3\n", t0 + 400000000ULL),
          "write newer session bridge edit");
    OptionsMenuPoll(1.5f);
    CHECK(OptionsWaveIntervalMs() == 180000 && OptionsSquadsPerWave() == 3, "session bridge edit applied");

    /* Older host options write (t0 + 20s < t0 + 40s) must NOT overwrite session edit */
    CHECK(write_file_mtime(host_json, "{\"format\":1,\"chaosIntensity\":\"standard\",\"heavySeaf\":\"constant\",\"fasterCorpseCleanup\":true}", t0 + 200000000ULL),
          "write stale host options");
    OptionsMenuPoll(1.5f);
    CHECK(OptionsConstant() == 0 && OptionsWaveIntervalMs() == 180000, "newer session edit wins over older host options");

    /* Newer Launcher Apply (t0 + 80s > t0 + 40s) -> "constant" activates constant=1! */
    CHECK(write_file_mtime(host_json, "{\"format\":1,\"chaosIntensity\":\"standard\",\"heavySeaf\":\"constant\",\"fasterCorpseCleanup\":true}", t0 + 800000000ULL),
          "write newer host options constant");
    OptionsMenuPoll(1.5f);
    CHECK(OptionsConstant() == 1 && OptionsSupportSquads() == 1 && OptionsSquadsPerWave() == 1 && OptionsWaveIntervalMs() == 90000,
          "newer Launcher Apply enables constant mode");

    /* 3. Positive Mission-Ground Landing Gate & Stale Ship-Spawn Immunity + 10-arg 0x6ABDB0 ABI */
    /* On ship (state=HF_STATE_SHIP) with player life==3 (reproducing stale ship spawn!): */
    g_mock_state = HF_STATE_SHIP;
    g_mock_mission_mode = 1u;
    g_mock_player_life = 3u;
    g_test_now_ms_override = 100000ULL;
    Tick(fake_mod, 2.5f, NULL);
    CHECK(g_landing_gate.landed == 0 && g_activate_calls == 0, "ship state keeps landed=0 despite life==3");

    /* Enter HF_STATE_MISSION while still dropping in Hellpod (mission_mode != 3 -> not on ground yet) */
    g_mock_state = HF_STATE_MISSION;
    g_mock_mission_mode = 2u; /* Hellpod drop, not ground mode 3 */
    g_test_now_ms_override = 105000ULL;
    Tick(fake_mod, 2.5f, NULL);
    g_test_now_ms_override = 108000ULL;
    Tick(fake_mod, 2.5f, NULL); /* ArmMission succeeds here, 6.7s before ground control! */
    g_test_now_ms_override = 125000ULL; /* +20s in Hellpod/cutscene -> STILL must NOT dispatch! */
    Tick(fake_mod, 2.5f, NULL);
    CHECK(g_landing_gate.landed == 0 && g_activate_calls == 0,
          "zero automatic dispatches before positive mission-ground host presence (fixes historical r4 bug!)");

    /* Host lands on mission ground at t = 130,000 ms (mode == 3, slot 2 local peer alive) */
    g_mock_mission_mode = 3u;
    g_test_now_ms_override = 130000ULL;
    Tick(fake_mod, 2.5f, NULL);
    CHECK(g_landing_gate.landed == 1 && g_landing_gate.grace_ready == 0 && g_activate_calls == 0,
          "landing gate armed at t=130000ms, holding during 15s grace");

    /* At +14,900 ms (144,900 ms) -> still inside 15s grace -> 0 dispatches! */
    g_test_now_ms_override = 144900ULL;
    Tick(fake_mod, 2.5f, NULL);
    CHECK(g_landing_gate.grace_ready == 0 && g_activate_calls == 0, "holds strictly through 14.9s");

    /* At +15,000 ms (145,000 ms) with poisoned stack -> dispatches wave 1 via 10-arg 0x6ABDB0! */
    poison_stack();
    g_test_now_ms_override = 145000ULL;
    Tick(fake_mod, 2.5f, NULL);
    CHECK(g_landing_gate.grace_ready == 1 && g_activate_calls == 1u,
          "wave 1 dispatches cleanly after 15,000 ms post-landing grace");
    CHECK(g_last_element == -1 && g_last_thrower == 0x77u && g_last_type == 116u,
          "slot-2 local host owner (0x77) and stratagem 116 passed accurately");
    CHECK(g_last_s7 == 0ULL && g_last_s8 == 0ULL && g_last_s9 == 0ULL && g_last_s10 == 0ULL,
          "all 4 tail stack/register args (s7..s10) deterministically zeroed despite poisoned caller stack!");

    /* Joiner check: demote HOST -> JOINER -> resets gate & performs zero dispatches */
    g_mock_role = HF_SESSION_JOINER;
    g_test_now_ms_override = 250000ULL;
    Tick(fake_mod, 2.5f, NULL);
    CHECK(g_landing_gate.landed == 0 && g_activate_calls == 1u, "HF_SESSION_JOINER resets gate and dispatches nothing");

    /* Mission exit (state 4 -> 5) resets landing gate cleanly */
    g_mock_role = HF_SESSION_SOLO;
    g_mock_state = 5u;
    g_test_now_ms_override = 255000ULL;
    Tick(fake_mod, 2.5f, NULL);
    CHECK(g_landing_gate.landed == 0 && g_landing_gate.grace_ready == 0, "state != HF_STATE_MISSION resets landed=0");

    /* 4. Verify 1-unit/tick synchronous equip pacing & QPC breadcrumbs */
    unsigned equipped_slots = 0;
    for (unsigned frame = 0; frame < 6u; ++frame) {
        HeavyEquipBeginFrame();
        unsigned equipped_this_frame = 0;
        /* Simulate 2 concurrent 6-passenger shuttles trying to equip all 6 soldiers in one tick */
        for (unsigned attempt = 0; attempt < 12u; ++attempt) {
            if (HeavyEquipConsumeOne()) {
                unsigned bc = HeavyBreadcrumbEnter(HEAVY_BC_POOL_EQUIP, 900u, 1000u + equipped_slots, equipped_slots);
                double dt_ms = HeavyBreadcrumbLeave(bc, 1);
                CHECK(dt_ms >= 0.0, "QPC breadcrumb duration non-negative");
                ++equipped_this_frame;
                ++equipped_slots;
            }
        }
        CHECK(equipped_this_frame == 1u, "strictly 1 soldier unit equipped per game frame");
    }
    CHECK(equipped_slots == 6u, "6-passenger squad completes cleanly across 6 frames");

    unsigned bc_count = 0, bc_head = 0;
    const HeavyBreadcrumb* ring = HeavyBreadcrumbRing(&bc_count, &bc_head);
    CHECK(ring != NULL && bc_count >= 7u, "breadcrumb ring recorded CallSeaf + 6 soldier equips without disk flush");

    /* 5. Session end stops worker flag */
    if (g_session_end_cb) g_session_end_cb(fake_mod, HF_EVENT_SESSION_END, NULL);
    CHECK(WorkerStopped() == 1, "HF_EVENT_SESSION_END signals WorkerStopped");

    DeleteFileA(host_json);
    printf("heavy_seaf_test: %u checks, %u failed\n", g_checks, g_failed);
    return g_failed ? 1 : 0;
}
