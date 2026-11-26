/* Host/joiner authority gate + clonedivers-host-options.json unit tests for
 * Faster Corpse Cleanup 1.0.6-clonedivers.1 (SDK 16, build 25480438). */
#define CC_UNIT_TEST 1
#define C6_NATIVE_EXPERIMENTAL_ENABLED 0
#include "../src/corpse_cleanup.c"
#include <stdarg.h>

static unsigned g_checks = 0;
static unsigned g_failed = 0;

#define CHECK(cond, msg) do { \
    ++g_checks; \
    if (!(cond)) { \
        ++g_failed; \
        fprintf(stderr, "FAIL [%s:%d]: %s\n", __FILE__, __LINE__, (msg)); \
    } \
} while (0)

/* ---- Synthetic memory & HfApi mock ---- */
static uint32_t g_mock_role = HF_SESSION_SOLO;
static uint32_t g_mock_state = HF_STATE_MISSION;
static char g_mock_folder[512];
static unsigned g_mock_write_calls = 0;
static unsigned g_mock_remove_calls = 0;
static uint32_t g_last_removed_entity = 0;

/* Mock HealthComponentData record for cc_targets[0] */
static uint8_t g_health_record[CC_STRIDE];

/* Mock game.dll / entity-registry / death-queue arena */
static uintptr_t g_fake_game_base = 0x0000018000000000ULL;
static uint8_t g_fake_registry[0xF40000];
static uint8_t g_fake_buckets[64 * 8];
static uint32_t g_fake_pending_rows[64];
static uint8_t g_fake_queue[0x22000];

static void mock_log(HfMod* m, const char* fmt, ...)
{
    (void)m; (void)fmt;
}

static const char* mock_build_id(void)
{
    return "25480438";
}

static const char* mock_mod_folder(HfMod* m)
{
    (void)m;
    return g_mock_folder;
}

static const char* mock_mod_id(HfMod* m)
{
    (void)m;
    return "local.faster-corpse-cleanup";
}

static int mock_mods(HfMod* m, uint32_t index, HfModInfo* out)
{
    (void)m;
    if (!out || out->size != sizeof *out) return 0;
    if (index == 0u) {
        memset(out, 0, sizeof *out);
        out->size = sizeof *out;
        out->id = "local.faster-corpse-cleanup";
        out->kind = HF_MOD_CODE;
        out->loaded = 1u;
        return 1;
    }
    return 0;
}

static uintptr_t mock_component_record(const char* comp, uint64_t res, uint32_t* stride_out)
{
    if (strcmp(comp, "HealthComponentData") == 0 && res == cc_targets[0].resource) {
        if (stride_out) *stride_out = CC_STRIDE;
        return (uintptr_t)g_health_record;
    }
    return 0;
}

static int mock_read(uintptr_t at, void* out, size_t n)
{
    if (at >= (uintptr_t)g_health_record && at + n <= (uintptr_t)g_health_record + sizeof g_health_record) {
        memcpy(out, (const void*)at, n);
        return 0;
    }
    if (at == g_fake_game_base + 0x346BF98u && n == 8) {
        uintptr_t p = (uintptr_t)g_fake_registry;
        memcpy(out, &p, 8);
        return 0;
    }
    if (at == g_fake_game_base + 0x346D500u && n == 8) {
        uintptr_t p = (uintptr_t)g_fake_queue;
        memcpy(out, &p, 8);
        return 0;
    }
    if (at >= (uintptr_t)g_fake_registry && at + n <= (uintptr_t)g_fake_registry + sizeof g_fake_registry) {
        memcpy(out, (const void*)at, n);
        return 0;
    }
    if (at >= (uintptr_t)g_fake_buckets && at + n <= (uintptr_t)g_fake_buckets + sizeof g_fake_buckets) {
        memcpy(out, (const void*)at, n);
        return 0;
    }
    if (at >= (uintptr_t)g_fake_pending_rows && at + n <= (uintptr_t)g_fake_pending_rows + sizeof g_fake_pending_rows) {
        memcpy(out, (const void*)at, n);
        return 0;
    }
    if (at >= (uintptr_t)g_fake_queue && at + n <= (uintptr_t)g_fake_queue + sizeof g_fake_queue) {
        memcpy(out, (const void*)at, n);
        return 0;
    }
    return 1;
}

static int mock_write(uintptr_t at, const void* src, size_t n)
{
    if (at >= (uintptr_t)g_health_record && at + n <= (uintptr_t)g_health_record + sizeof g_health_record) {
        ++g_mock_write_calls;
        memcpy((void*)at, src, n);
        return 0;
    }
    return 1;
}

static void mock_session(HfMod* m, HfSession* out)
{
    (void)m;
    memset(out, 0, sizeof *out);
    out->role = g_mock_role;
    out->version = 1u;
}

static uint32_t mock_game_state(HfMod* m)
{
    (void)m;
    return g_mock_state;
}

static uintptr_t mock_module_base(const char* name, size_t* size_out)
{
    if (strcmp(name, "game.dll") == 0) {
        if (size_out) *size_out = 74727424u;
        return g_fake_game_base;
    }
    return 0;
}

static int mock_code_matches(uintptr_t addr, const char* pattern)
{
    (void)addr; (void)pattern;
    return 1;
}

static void noop_queue_update(uintptr_t q, float dt)
{
    (void)q; (void)dt;
}

static int mock_hook(HfMod* m, uintptr_t target, void* detour, void** orig)
{
    (void)m; (void)target; (void)detour;
    *orig = (void*)noop_queue_update;
    return 0;
}

static HfEventCallback g_on_loaded_cb = NULL;
static HfEventCallback g_on_session_end_cb = NULL;
static int mock_on(HfMod* m, HfEvent ev, HfEventCallback cb, void* user)
{
    (void)m; (void)user;
    if (ev == HF_EVENT_GAME_LOADED) g_on_loaded_cb = cb;
    if (ev == HF_EVENT_SESSION_END) g_on_session_end_cb = cb;
    return 0;
}

static HfTickCallback g_game_tick_cb = NULL;
static void* g_game_tick_user = NULL;
static int mock_on_game_thread(HfMod* m, HfTickCallback cb, void* user)
{
    (void)m;
    g_game_tick_cb = cb;
    g_game_tick_user = user;
    return 1;
}

static void mock_remove_entity(uintptr_t root, uint32_t entity)
{
    (void)root;
    ++g_mock_remove_calls;
    g_last_removed_entity = entity;
}

static void setup_corpse_in_queue(uint32_t unit_id, uint32_t entity_id, uint32_t kind, uint8_t auth_flags)
{
    memset(g_fake_registry, 0, sizeof g_fake_registry);
    memset(g_fake_buckets, 0, sizeof g_fake_buckets);
    memset(g_fake_pending_rows, 0, sizeof g_fake_pending_rows);
    memset(g_fake_queue, 0, sizeof g_fake_queue);

    /* Entity map at root + 0xF1AEB0: { entries, count=64, empty=0xFFFFFFFF, mult=1 } */
    uintptr_t entries = (uintptr_t)g_fake_buckets;
    uint32_t count = 64u, empty = 0xFFFFFFFFu, mult = 1u;
    memcpy(g_fake_registry + 0xF1AEB0u, &entries, 8);
    memcpy(g_fake_registry + 0xF1AEB8u, &count, 4);
    memcpy(g_fake_registry + 0xF1AEBCu, &empty, 4);
    memcpy(g_fake_registry + 0xF1AEC0u, &mult, 4);

    for (uint32_t i = 0; i < count; ++i) {
        memcpy(g_fake_buckets + i * 8u, &empty, 4);
    }
    uint32_t slot = (entity_id * mult) & (count - 1u);
    uint32_t desc_idx = 3u;
    memcpy(g_fake_buckets + slot * 8u, &entity_id, 4);
    memcpy(g_fake_buckets + slot * 8u + 4u, &desc_idx, 4);

    /* Descriptor at root + 0xF32F18 + 24*desc_idx:
     * +0: u64 resource, +8: u32 entity, +12: u32 unit, +20: u8 authority bit */
    uint8_t* desc = g_fake_registry + 0xF32F18u + 24u * desc_idx;
    uint64_t res = cc_targets[0].resource;
    memcpy(desc + 0, &res, 8);
    memcpy(desc + 8, &entity_id, 4);
    memcpy(desc + 12, &unit_id, 4);
    desc[20] = auth_flags;

    /* Pending removal list at root + 0xF3EF18 (count=0) and +0xF3EF20 (ptr) */
    uint32_t pcount = 0u;
    uintptr_t plist = (uintptr_t)g_fake_pending_rows;
    memcpy(g_fake_registry + 0xF3EF18u, &pcount, 4);
    memcpy(g_fake_registry + 0xF3EF20u, &plist, 8);

    /* Queue row 0 at queue + 0x18: { unit, entity, kind, remaining=30.0f } */
    uint32_t extra_count = 0u;
    memcpy(g_fake_queue + 0x818u, &extra_count, 4);
    float rem = 30.0f;
    memcpy(g_fake_queue + 0x18u + 0u, &unit_id, 4);
    memcpy(g_fake_queue + 0x18u + 4u, &entity_id, 4);
    memcpy(g_fake_queue + 0x18u + 8u, &kind, 4);
    memcpy(g_fake_queue + 0x18u + 12u, &rem, 4);
}

static int write_text_with_mtime(const char* path, const char* text, uint64_t mtime_100ns)
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

static void run_all_target_sweeps(int frames)
{
    for (int f = 0; f < frames; ++f) {
        next_pass = 0;
        cc_tick(self, 0.016f, NULL);
    }
}

int main(int argc, char** argv)
{
    const char* root = (argc > 1) ? argv[1] : "D:\\ClaudeWork\\workstreams-v21\\code-mods\\faster-corpse-cleanup\\build\\fixture";
    char mods_dir[512], unpacked_pkg[512], unpacked_mod[512], host_json[512];
    snprintf(mods_dir, sizeof mods_dir, "%s\\mods", root);
    snprintf(unpacked_pkg, sizeof unpacked_pkg, "%s\\mods\\.unpacked\\pkg999", root);
    snprintf(unpacked_mod, sizeof unpacked_mod, "%s\\mods\\.unpacked\\pkg999\\faster-corpse-cleanup", root);
    snprintf(host_json, sizeof host_json, "%s\\mods\\clonedivers-host-options.json", root);

    CreateDirectoryA(root, NULL);
    CreateDirectoryA(mods_dir, NULL);
    {
        char up_base[512];
        snprintf(up_base, sizeof up_base, "%s\\mods\\.unpacked", root);
        CreateDirectoryA(up_base, NULL);
    }
    CreateDirectoryA(unpacked_pkg, NULL);
    CreateDirectoryA(unpacked_mod, NULL);
    DeleteFileA(host_json);

    snprintf(g_mock_folder, sizeof g_mock_folder, "%s", unpacked_mod);
    memcpy(g_health_record + CC_DECAY_OFFSET, &cc_targets[0].native, sizeof(CcDecay));
    cc_test_remove_entity_override = mock_remove_entity;

    HfApi api;
    memset(&api, 0, sizeof api);
    api.size = sizeof api;
    api.version = 16;
    api.log = mock_log;
    api.build_id = mock_build_id;
    api.mod_id = mock_mod_id;
    api.mod_folder = mock_mod_folder;
    api.mods = mock_mods;
    api.component_record = mock_component_record;
    api.read = mock_read;
    api.write = mock_write;
    api.on = mock_on;
    api.on_game_thread = mock_on_game_thread;
    api.hook = mock_hook;
    api.code_matches = mock_code_matches;
    api.module_base = mock_module_base;
    api.game_state = mock_game_state;
    api.session = mock_session;

    const HfApi* api_ptr = &api;
    HfMod* fake_mod = (HfMod*)&api_ptr;

    /* 1. Reject SDK < 16 or missing api->session */
    api.version = 15;
    CHECK(hellforge_mod_init(&api, fake_mod) == 1, "refuse SDK < 16");
    api.version = 16;
    api.session = NULL;
    CHECK(hellforge_mod_init(&api, fake_mod) == 1, "refuse missing api->session");
    api.session = mock_session;

    /* 2. Init on HF_SESSION_JOINER with clonedivers-host-options.json enabled -> strict NO-OP! */
    const uint64_t t_base = 134100000000000000ULL;
    CHECK(write_text_with_mtime(
              host_json,
              "{\"format\":1,\"chaosIntensity\":\"standard\",\"heavySeaf\":\"standard\",\"fasterCorpseCleanup\":true}",
              t_base),
          "write initial host options JSON");

    g_mock_role = HF_SESSION_JOINER;
    g_mock_state = HF_STATE_MISSION;
    CHECK(hellforge_mod_init(&api, fake_mod) == 0, "init succeeds on SDK 16");
    CHECK(g_on_loaded_cb != NULL && g_on_session_end_cb != NULL, "GAME_LOADED & SESSION_END registered");
    g_on_loaded_cb(fake_mod, HF_EVENT_GAME_LOADED, NULL);
    CHECK(original_queue_update != NULL, "factory lifecycle hook installed");

    setup_corpse_in_queue(0x101u, 0x202u, 2u, 1u);
    run_all_target_sweeps(160);
    for (int i = 0; i < 40; ++i) {
        cc_factory_update((uintptr_t)g_fake_queue, 0.25f);
    }
    CHECK(g_mock_write_calls == 0, "HF_SESSION_JOINER performs ZERO HealthComponentData decay writes");
    CHECK(g_mock_remove_calls == 0, "HF_SESSION_JOINER performs ZERO native_remove_entity calls");

    /* 3. Unknown / uninitialized session role (0xFFFFFFFFu / 3u) also strictly NO-OP */
    g_mock_role = 0xFFFFFFFFu;
    run_all_target_sweeps(160);
    for (int i = 0; i < 40; ++i) {
        cc_factory_update((uintptr_t)g_fake_queue, 0.25f);
    }
    CHECK(g_mock_write_calls == 0, "unknown session role performs ZERO decay writes");
    CHECK(g_mock_remove_calls == 0, "unknown session role performs ZERO native_remove_entity calls");

    /* 4. Promote to HF_SESSION_HOST -> applies HealthComponentData write AND phase-2 entity removal! */
    g_mock_role = HF_SESSION_HOST;
    run_all_target_sweeps((int)CC_COUNT(cc_targets) + 4);
    CcDecay current_decay;
    memcpy(&current_decay, g_health_record + CC_DECAY_OFFSET, sizeof current_decay);
    CHECK(g_mock_write_calls == 1, "HF_SESSION_HOST writes HealthComponentData decay timer");
    CHECK(current_decay.minimum == 5.0f && current_decay.maximum == 5.0f, "decay clamped to 5.0s");
    CHECK(edits[0].held == 1, "edit[0] marked held on host");

    for (int i = 0; i < 30; ++i) {
        cc_factory_update((uintptr_t)g_fake_queue, 0.25f);
    }
    CHECK(g_mock_remove_calls >= 1 && g_last_removed_entity == 0x202u,
          "HF_SESSION_HOST invokes native_remove_entity on aged phase-2 corpse");

    /* 5. Demote HOST -> JOINER -> immediately restores native decay timer and stops removals! */
    unsigned prev_removes = g_mock_remove_calls;
    g_mock_role = HF_SESSION_JOINER;
    cc_tick(self, 0.016f, NULL);
    memcpy(&current_decay, g_health_record + CC_DECAY_OFFSET, sizeof current_decay);
    CHECK(memcmp(&current_decay, &cc_targets[0].native, sizeof current_decay) == 0,
          "HOST->JOINER demotion immediately restores native HealthComponentData decay");
    CHECK(edits[0].held == 0, "edit[0] no longer held after demotion");
    for (int i = 0; i < 30; ++i) {
        cc_factory_update((uintptr_t)g_fake_queue, 0.25f);
    }
    CHECK(g_mock_remove_calls == prev_removes, "no further removals after demotion to JOINER");

    /* 6. HF_SESSION_SOLO + clonedivers-host-options.json live toggle & mtime precedence */
    g_mock_role = HF_SESSION_SOLO;
    run_all_target_sweeps((int)CC_COUNT(cc_targets) + 4);
    CHECK(edits[0].held == 1, "HF_SESSION_SOLO applies decay timer");

    /* Launcher writes fasterCorpseCleanup=false with newer mtime */
    CHECK(write_text_with_mtime(
              host_json,
              "{\"format\":1,\"chaosIntensity\":\"standard\",\"heavySeaf\":\"standard\",\"fasterCorpseCleanup\":false}",
              t_base + 10000000ULL),
          "write fasterCorpseCleanup=false");
    g_next_host_opts_poll = 0;
    run_all_target_sweeps((int)CC_COUNT(cc_targets) + 4);
    CHECK(config.enabled == 0, "fasterCorpseCleanup=false disables config.enabled");
    memcpy(&current_decay, g_health_record + CC_DECAY_OFFSET, sizeof current_decay);
    CHECK(memcmp(&current_decay, &cc_targets[0].native, sizeof current_decay) == 0,
          "fasterCorpseCleanup=false restores native decay");

    /* Player re-enables in-game via Mod Options Menu at t_base + 20s */
    cc_test_now_filetime_override = t_base + 200000000ULL;
    cc_menu_changed(enabled_id, 1.0, NULL);
    CHECK(config.enabled == 1, "in-game Mod Options Menu toggle enables cleanup");

    /* Stale/older host options write (t_base + 15s < t_base + 20s) does NOT clobber newer session edit */
    CHECK(write_text_with_mtime(
              host_json,
              "{\"format\":1,\"chaosIntensity\":\"standard\",\"heavySeaf\":\"standard\",\"fasterCorpseCleanup\":false}",
              t_base + 150000000ULL),
          "write older host options mtime");
    g_next_host_opts_poll = 0;
    cc_tick(self, 0.016f, NULL);
    CHECK(config.enabled == 1, "newer in-game Mod Options edit wins over older host options file");

    /* Newer Launcher Apply (t_base + 30s > t_base + 20s) overrides session edit */
    CHECK(write_text_with_mtime(
              host_json,
              "{\"format\":1,\"chaosIntensity\":\"standard\",\"heavySeaf\":\"standard\",\"fasterCorpseCleanup\":false}",
              t_base + 300000000ULL),
          "write newer Launcher Apply host options mtime");
    g_next_host_opts_poll = 0;
    cc_tick(self, 0.016f, NULL);
    CHECK(config.enabled == 0, "newer Launcher Apply mtime overrides earlier session edit");

    /* 7. >128 wreck-row cliff fix + Level 12 ULTRAKILL 0.0s multi-removal Turbo-Reaper */
    CHECK(write_text_with_mtime(
              host_json,
              "{\"format\":1,\"chaosIntensity\":\"total-war\",\"heavySeaf\":\"constant\",\"theaterPreset\":\"level12-ultrakill\",\"fasterCorpseCleanup\":true}",
              t_base + 400000000ULL),
          "write level12-ultrakill host options");
    g_next_host_opts_poll = 0;
    run_all_target_sweeps((int)CC_COUNT(cc_targets) + 4);
    CHECK(config.enabled == 1 && g_corpse_turbo_reaper == 1, "level12-ultrakill enables turbo reaper");
    memcpy(&current_decay, g_health_record + CC_DECAY_OFFSET, sizeof current_decay);
    CHECK(fabsf(current_decay.minimum - 0.15f) < 1e-4f && fabsf(current_decay.maximum - 0.15f) < 1e-4f,
          "level12-ultrakill sets 0.15s rapid phase-1->phase-2 decay");

    /* Populate 300 wrecks (> old 128 cliff!), placing 4 owned phase-2 corpses at indices 250..253 */
    setup_corpse_in_queue(0x101u, 0x202u, 2u, 1u);
    memset(g_fake_queue + 0x18u, 0, 32u * 64u);
    uint32_t big_count = 300u;
    memcpy(g_fake_queue + 0x818u, &big_count, 4);
    for (uint32_t k = 0; k < 4u; ++k) {
        uint32_t uid = 0x500u + k;
        uint32_t eid = 0x900u + k;
        uint32_t kind = 2u;
        float rem = 45.0f;
        uint32_t didx = 10u + k;
        uint8_t* desc = g_fake_registry + 0xF32F18u + 24u * didx;
        uint64_t res = cc_targets[0].resource;
        memcpy(desc + 0, &res, 8);
        memcpy(desc + 8, &eid, 4);
        memcpy(desc + 12, &uid, 4);
        desc[20] = 1u;
        uint8_t* row = g_fake_queue + 0x820u + (size_t)(250u + k) * 64u;
        memcpy(row + 0, &uid, 4);
        memcpy(row + 4, &eid, 4);
        memcpy(row + 8, &kind, 4);
        memcpy(row + 12, &rem, 4);
    }
    unsigned before_turbo_removes = g_mock_remove_calls;
    cc_factory_update((uintptr_t)g_fake_queue, 0.016f);
    CHECK(g_mock_remove_calls >= before_turbo_removes + 4u,
          "count=300 (>128) + level12-ultrakill reaps all 4 phase-2 corpses in 1 frame at 0.0s delay");

    /* 8. HF_EVENT_SESSION_END disarms cleanly */
    g_on_session_end_cb(fake_mod, HF_EVENT_SESSION_END, NULL);
    CHECK(cc_stopped() == 1, "SESSION_END stops tick & hook observation");

    DeleteFileA(host_json);
    printf("corpse_cleanup_test: %u checks, %u failed\n", g_checks, g_failed);
    return g_failed ? 1 : 0;
}
