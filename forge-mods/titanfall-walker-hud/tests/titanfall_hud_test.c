/* Comprehensive C11 unit & mock-HfDraw verification suite for titanfall-walker-hud 0.1.0.
 * Exercises:
 *   1. INI parsing, UTF-8 BOM, bounds clamping, and VK_F8 (0x77) editor collision refusal.
 *   2. Compass heading [0..360), pitch [-89..+89], and signed shortest hull-vs-look delta wrap.
 *   3. Game-thread telemetry pipeline (LocalUser, PlayerManager, EntityManager, HealthManager HP/MaxHP,
 *      MotionManager m/s speed, MountManager + MagazineRounds / MagazineCapacity ordnance readout).
 *   4. Smart walker gating: 198-node Clonedivers AT-TE admitted across all 4 walker IDs, 106-node /
 *      high-pelvis Empire 2.28x AT-ST skipped by default (and admitted when enable_on_atst = true),
 *      and non-walker mounts (Hellpod / Pelican / FRV) rejected.
 *   5. Non-modal HfScreen open/close transitions, 0.55s boot shutter, 0.20s closing animation, and
 *      aspect-adaptive HfDraw primitives on 16:9 (1920x1080) and 21:9 (2520x1080), including critical HP alert.
 *   6. F7 (0x76) 1P Forward-Canopy / 3P Tactical dual-view camera toggle, synchronous Havok cliff-wall
 *      raycast clamping, unoverwritten-pose drift guard, disembark & 4->5 teardown pose restoration,
 *      and GetLastError() + SSE MXCSR preservation. */

#define TFH_UNIT_TEST 1
#include "../src/titanfall_walker_hud.c"

#include <stdarg.h>
#include <stdlib.h>

static int g_checks = 0;
static int g_failed = 0;

#define CHECK(cond, msg) do { \
    g_checks++; \
    if (!(cond)) { \
        fprintf(stderr, "FAIL [%s:%d]: %s\n", __func__, __LINE__, (msg)); \
        g_failed++; \
        return; \
    } \
} while (0)

#define CHECK_NEAR(a, b, eps, msg) CHECK(fabsf((a) - (b)) <= (eps), (msg))

/* ---- Mock Stingray Units, Game Managers, and HfDraw Host -------------------------------------------------------- */

typedef struct MockGraph {
    uint8_t pad0[0x10];
    uint32_t node_count;      /* +0x10 */
    uint32_t pad14;
    uintptr_t link_parent;    /* +0x18 */
    uintptr_t local_poses;    /* +0x20 */
    uintptr_t world_poses;    /* +0x28 */
    uint8_t pad30[0x10];
    uintptr_t node_names;     /* +0x40 */
} MockGraph;

typedef struct MockCamera {
    uint8_t pad0[0x34];
    float vertical_fov;       /* +0x34 */
} MockCamera;

typedef struct MockUnit {
    uintptr_t vtable;         /* +0x00 */
    uint32_t ref;             /* +0x08 */
    uint32_t pad0C;
    uint8_t pad10[0x20];
    uintptr_t res_ptr;        /* +0x30 */
    uint64_t res_storage;
    MockGraph graph;
    float local_poses[2][16];
    float world_poses[200][16];
    uint32_t node_names[200];
    int cam_count;
    MockCamera cam;
} MockUnit;

static uintptr_t MockVtblGraph(uintptr_t u) { return (uintptr_t)&((MockUnit*)u)->graph; }
static int MockVtblCamCount(uintptr_t u) { return ((MockUnit*)u)->cam_count; }
static uintptr_t MockVtblCamera(uintptr_t u, uint32_t idx) { return idx == 0u ? (uintptr_t)&((MockUnit*)u)->cam : 0u; }

static uint8_t g_mock_exe[TFH_EXE_UNIT_VTABLE + 0x200];
static uint8_t g_mock_game[0x1000];
static uint8_t g_mock_world[0x231000];
static uintptr_t g_mock_units[8];
static uint32_t g_mock_unit_cnt;

static uintptr_t g_cell_local_user;
static uintptr_t g_cell_player_mgr;
static uintptr_t g_cell_entity_mgr;
static uintptr_t g_cell_health_mgr;
static uintptr_t g_cell_motion_mgr;
static uintptr_t g_cell_mount_mgr;
static uintptr_t g_cell_weapon_mgr;

static uint8_t g_mock_lu[0xC000];
static uint8_t g_mock_pm[0x800];
static uint8_t g_mock_em[0xF40000];
static uint8_t g_mock_em_net_tbl[64 * 8];
static uint8_t g_mock_em_ent_tbl[64 * 8];

static uint8_t g_mock_hm[0x1100];
static uint8_t g_mock_hm_tbl[64 * 8];
static uint8_t g_mock_hm_recs[8 * TFH_HM_RECORD_STRIDE];
static uint8_t g_mock_hm_ext[8 * TFH_HM_EXT_STRIDE];

static uint8_t g_mock_mm[0x4900];
static uint8_t g_mock_mm_tbl[64 * 8];
static uint8_t g_mock_mm_recs[8 * TFH_MM_RECORD_STRIDE];

static uint8_t g_mock_mounts[0x100];
static uint8_t g_mock_mounts_tbl[64 * 8];
static uint32_t g_mock_mounts_recs[8][6];
static uint8_t g_mock_wmgr[0x40];

static MockUnit g_u_cam;
static MockUnit g_u_avatar;
static MockUnit g_u_walker;

static uint32_t g_mock_state = HF_STATE_MISSION;
static uint32_t g_mock_browse_cam = HF_BROWSE_CAMERA_IDLE;
static HfScreen g_registered_screen;
static int g_screen_add_calls = 0;
static int g_screen_open_state = 0;
static int g_screen_show_calls = 0;

static HfTickCallback g_gt_tick;
static HfStateListener g_ev_state;
static HfSpawnListener g_ev_spawn;
static HfEventCallback g_ev_session_end;

static int g_set_pose_calls = 0;
static int g_update_unit_calls = 0;
static int g_mutate_cam_in_place = 1;
static float g_last_pose[16];
static int g_ray_hit = 0;
static TfhVec3 g_ray_hit_pt;
static int g_corrupt_env_in_raycast = 0;
static DWORD g_orig_seen_err = 0;
static unsigned int g_orig_seen_csr = 0;

static void MockLog(HfMod* m, const char* fmt, ...) { (void)m; (void)fmt; }
static const char* MockBuildId(void) { return "25480438"; }
static uint32_t MockGameState(HfMod* m) { (void)m; return g_mock_state; }
static int MockRead(uintptr_t addr, void* buf, size_t sz)
{
    if (addr < 0x10000u || !buf) return 1;
    memcpy(buf, (const void*)addr, sz);
    return 0;
}
static int MockCodeMatches(uintptr_t addr, const char* pat) { (void)addr; (void)pat; return 1; }
static int MockHookEx(HfMod* m, uintptr_t t, void* d, void** o, int32_t p)
{
    (void)m; (void)t; (void)d; (void)o; (void)p;
    return 0;
}
static int MockOn(HfMod* m, HfEvent ev, HfEventCallback cb, void* u)
{
    (void)m; (void)u;
    if (ev == HF_EVENT_SESSION_END) g_ev_session_end = cb;
    return 0;
}
static int MockSubscribe(HfMod* m, uint32_t kind, HfListener cb, void* u, int32_t p)
{
    (void)m; (void)u; (void)p;
    if (kind == HF_ON_STATE) g_ev_state = (HfStateListener)(void (*)(void))cb;
    else if (kind == HF_ON_SPAWN) g_ev_spawn = (HfSpawnListener)(void (*)(void))cb;
    return 1;
}
static int MockOnGameThread(HfMod* m, HfTickCallback cb, void* u)
{
    (void)m; (void)u;
    g_gt_tick = cb;
    return 1;
}
static int MockBrowseState(HfMod* m, HfBrowseState* out)
{
    (void)m;
    memset(out, 0, sizeof(*out));
    out->size = sizeof(*out);
    out->camera = g_mock_browse_cam;
    return 1;
}
static int MockAddScreen(HfMod* m, const HfScreen* s)
{
    (void)m;
    g_screen_add_calls++;
    g_registered_screen = *s;
    return 7;
}
static void MockScreenShow(HfMod* m, int id, int open)
{
    (void)m;
    if (id == 7) {
        g_screen_show_calls++;
        g_screen_open_state = open;
        if (g_registered_screen.opened) g_registered_screen.opened(g_registered_screen.user, open);
    }
}

static int32_t MockMagRounds(void* wmgr, uint32_t ent)
{
    (void)wmgr;
    if (ent == 0x9001u) return 14;
    if (ent == 0x9002u) return 750;
    return 0;
}
static int32_t MockMagCap(void* unused, uint32_t ent)
{
    (void)unused;
    if (ent == 0x9001u) return 14;
    if (ent == 0x9002u) return 1000;
    return 0;
}

static uintptr_t MockFunction(HfMod* m, const char* name)
{
    (void)m;
    if (strcmp(name, "MagazineRounds") == 0) return (uintptr_t)MockMagRounds;
    if (strcmp(name, "MagazineCapacity") == 0) return (uintptr_t)MockMagCap;
    return 0u;
}
static uintptr_t MockGlobal(HfMod* m, const char* name)
{
    (void)m;
    if (strcmp(name, "LocalUser") == 0) return (uintptr_t)&g_cell_local_user;
    if (strcmp(name, "PlayerManager") == 0) return (uintptr_t)&g_cell_player_mgr;
    if (strcmp(name, "EntityManager") == 0) return (uintptr_t)&g_cell_entity_mgr;
    if (strcmp(name, "HealthManager") == 0) return (uintptr_t)&g_cell_health_mgr;
    if (strcmp(name, "MotionManager") == 0) return (uintptr_t)&g_cell_motion_mgr;
    if (strcmp(name, "MountManager") == 0) return (uintptr_t)&g_cell_mount_mgr;
    if (strcmp(name, "WeaponManager") == 0) return (uintptr_t)&g_cell_weapon_mgr;
    return 0u;
}

static void MockOrigStep(void* app)
{
    (void)app;
    g_orig_seen_err = GetLastError();
    g_orig_seen_csr = _mm_getcsr();
}
static void MockSetLocalPose(uint32_t ref, int32_t node, const float pose[16])
{
    if (node == 0) {
        g_set_pose_calls++;
        memcpy(g_last_pose, pose, sizeof(g_last_pose));
        if (g_mutate_cam_in_place && ref == g_u_cam.ref) {
            memcpy(g_u_cam.local_poses[0], pose, sizeof(g_last_pose));
        }
    }
}
static void MockUpdateUnit(uintptr_t world, uintptr_t unit) { (void)world; (void)unit; g_update_unit_calls++; }
static uintptr_t MockLookupUnit(uint32_t ref)
{
    if (ref == g_u_cam.ref) return (uintptr_t)&g_u_cam;
    if (ref == g_u_avatar.ref) return (uintptr_t)&g_u_avatar;
    if (ref == g_u_walker.ref) return (uintptr_t)&g_u_walker;
    return 0u;
}
static uintptr_t MockMainWorld(void) { return (uintptr_t)g_mock_world; }
static int MockRaycast(uint32_t q, const float from[3], const float dir[3], float len, int z, float res[16], int cl)
{
    (void)from; (void)dir; (void)len; (void)z; (void)cl;
    if (g_corrupt_env_in_raycast) {
        SetLastError(0xDEADBEEFu);
        _mm_setcsr(_mm_getcsr() ^ 0x8000u);
    }
    if (q != TFH_RAYCAST_QUERY_ID || !g_ray_hit) return 0;
    res[0] = g_ray_hit_pt.x;
    res[1] = g_ray_hit_pt.y;
    res[2] = g_ray_hit_pt.z;
    return 1;
}

/* Mock HfDraw counters */
static struct {
    uint32_t rects;
    uint32_t lines;
    uint32_t polygons;
    uint32_t circles;
    uint32_t texts;
    uint32_t clips_pushed;
    uint32_t clips_popped;
    char seen_texts[64][96];
} g_draw_stats;

static void DrawRect(float x0, float y0, float x1, float y1, HfColour f, HfColour o, float t)
{
    (void)x0; (void)y0; (void)x1; (void)y1; (void)f; (void)o; (void)t;
    g_draw_stats.rects++;
}
static void DrawLines(const HfPoint* pts, uint32_t cnt, HfColour c, float t, int cl)
{
    (void)pts; (void)cnt; (void)c; (void)t; (void)cl;
    g_draw_stats.lines++;
}
static void DrawPolygon(const HfPoint* pts, uint32_t cnt, HfColour f)
{
    (void)pts; (void)cnt; (void)f;
    g_draw_stats.polygons++;
}
static void DrawCircle(float x, float y, float r, HfColour f, HfColour o, float t)
{
    (void)x; (void)y; (void)r; (void)f; (void)o; (void)t;
    g_draw_stats.circles++;
}
static float DrawTextFn(float x, float y, const char* utf8, float sz, uint32_t face, uint32_t align, HfColour col)
{
    (void)x; (void)y; (void)sz; (void)face; (void)align; (void)col;
    if (utf8) {
        snprintf(g_draw_stats.seen_texts[g_draw_stats.texts % 64u], 96u, "%s", utf8);
    }
    g_draw_stats.texts++;
    return utf8 ? (float)strlen(utf8) * 8.0f : 0.0f;
}
static float DrawTextWidth(const char* utf8, float sz, uint32_t face)
{
    (void)sz; (void)face;
    return utf8 ? (float)strlen(utf8) * 8.0f : 0.0f;
}
static void DrawClipPush(float x0, float y0, float x1, float y1)
{
    (void)x0; (void)y0; (void)x1; (void)y1;
    g_draw_stats.clips_pushed++;
}
static void DrawClipPop(void) { g_draw_stats.clips_popped++; }

static int SeenDrawText(const char* needle)
{
    uint32_t lim = g_draw_stats.texts < 64u ? g_draw_stats.texts : 64u;
    for (uint32_t i = 0u; i < lim; i++) {
        if (strstr(g_draw_stats.seen_texts[i], needle) != NULL) return 1;
    }
    return 0;
}

static void InitMapHeader(uint8_t* mgr, uint32_t at, uint8_t* tbl, uint32_t cap, uint32_t empty)
{
    uint64_t ptr = (uint64_t)(uintptr_t)tbl;
    uint32_t mult = 2654435761u;
    for (uint32_t i = 0u; i < cap; i++) {
        uint32_t inv = 0xFFFFFFFFu;
        memcpy(tbl + i * 8u + 0u, &empty, 4u);
        memcpy(tbl + i * 8u + 4u, &inv, 4u);
    }
    memcpy(mgr + at + 0u, &ptr, 8u);
    memcpy(mgr + at + 8u, &cap, 4u);
    memcpy(mgr + at + 12u, &empty, 4u);
    memcpy(mgr + at + 16u, &mult, 4u);
}

static void InsertMapKey(uint8_t* tbl, uint32_t cap, uint32_t empty, uint32_t key, uint32_t val)
{
    uint32_t mult = 2654435761u;
    for (uint32_t i = 0u; i < cap; i++) {
        uint32_t s = (i + mult * key) & (cap - 1u);
        uint32_t cur = 0u;
        memcpy(&cur, tbl + s * 8u, 4u);
        if (cur == empty || cur == key) {
            memcpy(tbl + s * 8u + 0u, &key, 4u);
            memcpy(tbl + s * 8u + 4u, &val, 4u);
            return;
        }
    }
}

static void InitWorldPose(float m[16], float x, float y, float z)
{
    memset(m, 0, 64u);
    m[0] = 1.0f; m[5] = 1.0f; m[10] = 1.0f;
    m[12] = x; m[13] = y; m[14] = z; m[15] = 1.0f;
}

static void InitLocalPosePlusY(float p[16], float x, float y, float z)
{
    memset(p, 0, 64u);
    p[0] = 1.0f; p[4] = 1.0f; p[8] = 1.0f;
    p[9] = x; p[10] = y; p[11] = z;
    p[12] = 1.0f; p[13] = 1.0f; p[14] = 1.0f;
}

static void InitMockUnitObj(MockUnit* u, uint32_t ref, uint64_t res, uint32_t nodes, int cams)
{
    memset(u, 0, sizeof(*u));
    u->vtable = (uintptr_t)(g_mock_exe + TFH_EXE_UNIT_VTABLE);
    u->ref = ref;
    u->res_storage = res;
    u->res_ptr = (uintptr_t)&u->res_storage;
    u->cam_count = cams;
    u->cam.vertical_fov = 0.96f;
    u->graph.node_count = nodes;
    u->graph.local_poses = (uintptr_t)u->local_poses;
    u->graph.world_poses = (uintptr_t)u->world_poses;
    u->graph.node_names = (uintptr_t)u->node_names;
    for (uint32_t i = 0u; i < nodes && i < 200u; i++) {
        InitWorldPose(u->world_poses[i], 0.0f, 0.0f, 0.0f);
    }
    InitLocalPosePlusY(u->local_poses[0], 0.0f, 0.0f, 0.0f);
}

static HfApi g_api;
static struct { const HfApi* api; } g_mod_obj;

static void ResetHarness(uint64_t walker_res, uint32_t walker_nodes, float pelvis_z)
{
    memset(g_mock_exe, 0, sizeof(g_mock_exe));
    uintptr_t fn_g = (uintptr_t)MockVtblGraph;
    uintptr_t fn_cc = (uintptr_t)MockVtblCamCount;
    uintptr_t fn_c = (uintptr_t)MockVtblCamera;
    memcpy(g_mock_exe + TFH_EXE_UNIT_VTABLE + TFH_UNIT_VTABLE_GRAPH, &fn_g, 8u);
    memcpy(g_mock_exe + TFH_EXE_UNIT_VTABLE + TFH_UNIT_VTABLE_CAM_COUNT, &fn_cc, 8u);
    memcpy(g_mock_exe + TFH_EXE_UNIT_VTABLE + TFH_UNIT_VTABLE_CAMERA, &fn_c, 8u);

    memset(&g_api, 0, sizeof(g_api));
    g_api.version = 20;
    g_api.size = sizeof(HfApi);
    g_api.log = MockLog;
    g_api.build_id = MockBuildId;
    g_api.read = MockRead;
    g_api.code_matches = MockCodeMatches;
    g_api.hook_ex = MockHookEx;
    g_api.on = MockOn;
    g_api.subscribe = MockSubscribe;
    g_api.on_game_thread = MockOnGameThread;
    g_api.game_state = MockGameState;
    g_api.browse_state = MockBrowseState;
    g_api.global = MockGlobal;
    g_api.function = MockFunction;
    g_api.add_screen = MockAddScreen;
    g_api.screen_show = MockScreenShow;
    g_mod_obj.api = &g_api;

    g_mock_state = HF_STATE_MISSION;
    g_mock_browse_cam = HF_BROWSE_CAMERA_IDLE;
    g_screen_add_calls = 0;
    g_screen_open_state = 0;
    g_screen_show_calls = 0;
    g_set_pose_calls = 0;
    g_update_unit_calls = 0;
    g_mutate_cam_in_place = 1;
    g_ray_hit = 0;
    g_corrupt_env_in_raycast = 0;
    g_testSimulatedVkDown = 0u;

    InitMockUnitObj(&g_u_cam, 0x500001u, 0x1111ULL, 1u, 1);
    InitLocalPosePlusY(g_u_cam.local_poses[0], 0.0f, -4.5f, 3.8f);
    InitWorldPose(g_u_cam.world_poses[0], 0.0f, -4.5f, 3.8f);
    InitWorldPose((float*)(g_mock_world + TFH_WORLD_RENDER_POSE), 0.0f, -4.5f, 3.8f);

    InitMockUnitObj(&g_u_avatar, 0x500002u, TFH_RES_HELLDIVER_AVATAR, 107u, 0);
    InitMockUnitObj(&g_u_walker, 0x500003u, walker_res, walker_nodes, 0);
    g_u_walker.node_names[12] = TFH_NODE_HASH_WALKER_PELVIS;
    InitWorldPose(g_u_walker.world_poses[0], 0.0f, 2.0f, 0.0f);
    InitWorldPose(g_u_walker.world_poses[12], 0.0f, 2.0f, pelvis_z);

    g_mock_units[0] = (uintptr_t)&g_u_cam;
    g_mock_units[1] = (uintptr_t)&g_u_avatar;
    g_mock_units[2] = (uintptr_t)&g_u_walker;
    g_mock_unit_cnt = 3u;
    uintptr_t uarr = (uintptr_t)g_mock_units;
    memcpy(g_mock_world + TFH_WORLD_UNITS_COUNT, &g_mock_unit_cnt, 4u);
    memcpy(g_mock_world + TFH_WORLD_UNITS_ARRAY, &uarr, 8u);

    /* Populate game globals */
    memset(g_mock_lu, 0, sizeof(g_mock_lu));
    memset(g_mock_pm, 0, sizeof(g_mock_pm));
    memset(g_mock_em, 0, sizeof(g_mock_em));
    memset(g_mock_hm, 0, sizeof(g_mock_hm));
    memset(g_mock_hm_recs, 0, sizeof(g_mock_hm_recs));
    memset(g_mock_hm_ext, 0, sizeof(g_mock_hm_ext));
    memset(g_mock_mm, 0, sizeof(g_mock_mm));
    memset(g_mock_mm_recs, 0, sizeof(g_mock_mm_recs));
    memset(g_mock_mounts, 0, sizeof(g_mock_mounts));
    memset(g_mock_mounts_recs, 0, sizeof(g_mock_mounts_recs));

    g_cell_local_user = (uintptr_t)g_mock_lu;
    g_cell_player_mgr = (uintptr_t)g_mock_pm;
    g_cell_entity_mgr = (uintptr_t)g_mock_em;
    g_cell_health_mgr = (uintptr_t)g_mock_hm;
    g_cell_motion_mgr = (uintptr_t)g_mock_mm;
    g_cell_mount_mgr = (uintptr_t)g_mock_mounts;
    g_cell_weapon_mgr = (uintptr_t)g_mock_wmgr;

    uint64_t peer = 0xAABBCCDDEEFF0011ULL;
    uint32_t pcnt = 2u, life = TFH_PLAYER_LIFE_SPAWNED, net = 0x33u;
    memcpy(g_mock_lu + TFH_LOCALUSER_PEER_OFFSET, &peer, 8u);
    memcpy(g_mock_pm + TFH_PLAYERS_COUNT_OFFSET, &pcnt, 4u);
    memcpy(g_mock_pm + TFH_PLAYERS_PEER_OFFSET + 1u * TFH_PLAYERS_SLOT_STRIDE, &peer, 8u);
    memcpy(g_mock_pm + TFH_PLAYERS_LIFE_OFFSET + 1u * TFH_PLAYERS_SLOT_STRIDE, &life, 4u);
    memcpy(g_mock_pm + TFH_PLAYERS_NET_OFFSET + 1u * TFH_PLAYERS_NET_STRIDE, &net, 4u);

    InitMapHeader(g_mock_em, TFH_EM_NETWORK_MAP, g_mock_em_net_tbl, 32u, TFH_NET_ID_NONE);
    InitMapHeader(g_mock_em, TFH_EM_ENTITY_MAP, g_mock_em_ent_tbl, 32u, 0xFFFFFFFFu);

    TfhEntityDescriptor d_av = {TFH_RES_HELLDIVER_AVATAR, 0x1001u, g_u_avatar.ref, net, 1u};
    TfhEntityDescriptor d_wk = {walker_res, 0x2002u, g_u_walker.ref, 0x44u, 1u};
    memcpy(g_mock_em + TFH_EM_DESCRIPTORS + 1u * TFH_EM_DESC_STRIDE, &d_av, sizeof(d_av));
    memcpy(g_mock_em + TFH_EM_DESCRIPTORS + 2u * TFH_EM_DESC_STRIDE, &d_wk, sizeof(d_wk));
    InsertMapKey(g_mock_em_net_tbl, 32u, TFH_NET_ID_NONE, net, 1u);
    InsertMapKey(g_mock_em_ent_tbl, 32u, 0xFFFFFFFFu, 0x1001u, 1u);
    InsertMapKey(g_mock_em_ent_tbl, 32u, 0xFFFFFFFFu, 0x2002u, 2u);

    /* HealthManager: avatar (idx 0: 125/125), walker (idx 1: 1350/1500) */
    uint32_t hm_cnt = 2u;
    uintptr_t hm_rptr = (uintptr_t)g_mock_hm_recs, hm_eptr = (uintptr_t)g_mock_hm_ext;
    memcpy(g_mock_hm + TFH_HM_COUNT_OFFSET, &hm_cnt, 4u);
    memcpy(g_mock_hm + TFH_HM_RECORDS_PTR, &hm_rptr, 8u);
    memcpy(g_mock_hm + TFH_HM_EXT_PTR, &hm_eptr, 8u);
    InitMapHeader(g_mock_hm, TFH_HM_ENTITY_MAP, g_mock_hm_tbl, 32u, 0xFFFFFFFFu);
    InsertMapKey(g_mock_hm_tbl, 32u, 0xFFFFFFFFu, 0x1001u, 0u);
    InsertMapKey(g_mock_hm_tbl, 32u, 0xFFFFFFFFu, 0x2002u, 1u);
    int32_t av_hp = 125, wk_hp = 1350, wk_max = 1500;
    memcpy(g_mock_hm_recs + 0u * TFH_HM_RECORD_STRIDE + TFH_HM_HEALTH_OFFSET, &av_hp, 4u);
    memcpy(g_mock_hm_recs + 1u * TFH_HM_RECORD_STRIDE + TFH_HM_HEALTH_OFFSET, &wk_hp, 4u);
    memcpy(g_mock_hm_ext + 1u * TFH_HM_EXT_STRIDE + TFH_HM_HEALTH_OFFSET, &wk_max, 4u);

    /* MotionManager: walker velocity (3.0, 4.0, 0.0) -> 5.0 m/s */
    uint32_t mm_cnt = 2u;
    uintptr_t mm_rptr = (uintptr_t)g_mock_mm_recs;
    memcpy(g_mock_mm + TFH_MM_COUNT_OFFSET, &mm_cnt, 4u);
    memcpy(g_mock_mm + TFH_MM_RECORDS_PTR, &mm_rptr, 8u);
    InitMapHeader(g_mock_mm, TFH_MM_ENTITY_MAP, g_mock_mm_tbl, 32u, 0xFFFFFFFFu);
    InsertMapKey(g_mock_mm_tbl, 32u, 0xFFFFFFFFu, 0x2002u, 1u);
    float wk_vel[3] = {3.0f, 4.0f, 0.0f};
    memcpy(g_mock_mm_recs + 1u * TFH_MM_RECORD_STRIDE, wk_vel, sizeof(wk_vel));

    /* MountManager: walker mounts 0x9001 (14/14 rockets) and 0x9002 (750/1000 gatling) */
    uintptr_t mnt_rptr = (uintptr_t)g_mock_mounts_recs;
    memcpy(g_mock_mounts + TFH_MOUNT_RECORDS_PTR, &mnt_rptr, 8u);
    InitMapHeader(g_mock_mounts, TFH_MOUNT_ENTITY_MAP, g_mock_mounts_tbl, 32u, 0xFFFFFFFFu);
    InsertMapKey(g_mock_mounts_tbl, 32u, 0xFFFFFFFFu, 0x2002u, 0u);
    g_mock_mounts_recs[0][0] = 0x9001u;
    g_mock_mounts_recs[0][1] = 0x9002u;

    if (hellforge_mod_init(&g_api, (HfMod*)&g_mod_obj) != 0) abort();
    tfh_test_bind_engine(
        (uintptr_t)g_mock_exe,
        (uintptr_t)g_mock_game,
        MockOrigStep,
        MockSetLocalPose,
        MockUpdateUnit,
        MockLookupUnit,
        MockMainWorld,
        MockRaycast,
        1);

    HfSpawnEvent sp = {{sizeof(sp), HF_ON_SPAWN, 1}, 0x2002u, 0x44u, walker_res, 1u};
    if (g_ev_spawn) g_ev_spawn((HfMod*)&g_mod_obj, &sp, NULL);
}

static void TestOptionsAndAngles(const char* temp_dir)
{
    TfhOptions o;
    tfh_options_defaults(&o);
    CHECK(o.enabled == 1 && o.hud_enabled == 1 && o.enable_on_atst == 0, "default flags");
    CHECK(o.dual_view_vk == 0x76u, "default dual_view_vk is F7 (0x76), avoiding F8 editor");

    const char* txt =
        "\xEF\xBB\xBF[general]\r\n"
        "hud_opacity = 0.05 ; clamp to 0.15\n"
        "enable_on_atst = true\n"
        "[camera]\n"
        "dual_view_vk = 0x77 ; F8 must be rejected to protect HellForge live editor!\n"
        "fp_forward_offset = 2.60\n"
        "fp_up_offset = 3.10\n";
    tfh_options_parse_text(&o, txt);
    CHECK(o.enable_on_atst == 1, "parsed enable_on_atst");
    CHECK_NEAR(o.hud_opacity, 0.15f, 1e-5f, "clamped min hud_opacity");
    CHECK(o.dual_view_vk == 0x76u, "VK_F8 (0x77) collision refused, keeping safe 0x76");
    CHECK_NEAR(o.fp_forward_offset, 2.60f, 1e-5f, "parsed fp_forward_offset");
    CHECK_NEAR(o.fp_up_offset, 3.10f, 1e-5f, "parsed fp_up_offset");

    if (temp_dir && *temp_dir) {
        char p[512];
        snprintf(p, sizeof(p), "%s\\opts.ini", temp_dir);
        FILE* f = fopen(p, "wb");
        if (f) {
            fwrite(txt, 1, strlen(txt), f);
            fclose(f);
            TfhOptions loaded;
            CHECK(tfh_options_load_file(&loaded, p) == 1, "loaded options file");
            CHECK_NEAR(loaded.fp_up_offset, 3.10f, 1e-5f, "file fp_up_offset");
        }
    }

    float hdg = 0.0f, pch = 0.0f;
    tfh_angles_from_forward(tfh_v3(0.0f, 1.0f, 0.0f), &hdg, &pch);
    CHECK_NEAR(hdg, 0.0f, 1e-3f, "North (+Y) == 0 deg");
    CHECK_NEAR(pch, 0.0f, 1e-3f, "level pitch == 0 deg");
    tfh_angles_from_forward(tfh_v3(1.0f, 0.0f, 0.0f), &hdg, &pch);
    CHECK_NEAR(hdg, 90.0f, 1e-3f, "East (+X) == 90 deg");
    tfh_angles_from_forward(tfh_v3(-1.0f, 0.0f, 0.0f), &hdg, &pch);
    CHECK_NEAR(hdg, 270.0f, 1e-3f, "West (-X) == 270 deg");
    CHECK_NEAR(tfh_wrap_delta_deg(10.0f, 350.0f), 20.0f, 1e-3f, "wrap +20 deg across 0/360");
    CHECK_NEAR(tfh_wrap_delta_deg(350.0f, 10.0f), -20.0f, 1e-3f, "wrap -20 deg across 0/360");
}

static void TestAtteHudLifecycleTelemetryAndAtstGate(void)
{
    /* Verify all 4 walker resource IDs activate cleanly on 198-node AT-TE */
    for (size_t w = 0u; w < 4u; w++) {
        ResetHarness(kTfhWalkerResources[w], 198u, 2.06f);
        CHECK(g_screen_add_calls == 1 && g_registered_screen.flags == 0u && g_registered_screen.key == 0u,
              "registered non-modal screen with key=0");

        /* On foot -> HUD stays closed */
        g_gt_tick((HfMod*)&g_mod_obj, 0.016f, NULL);
        TfhStepDetour(NULL);
        CHECK(g_screen_open_state == 0, "HUD stays closed while Helldiver is on foot");

        /* Board AT-TE -> HUD opens automatically */
        g_u_avatar.graph.link_parent = g_u_walker.graph.world_poses;
        TfhStepDetour(NULL);
        g_gt_tick((HfMod*)&g_mod_obj, 0.016f, NULL); /* Populates walker HP, velocity & mount ammo */
        CHECK(g_screen_open_state == 1, "HUD opens automatically on boarding 198-node AT-TE");
    }

    /* Exercise HfScreen.draw across boot sequence & steady state on 16:9 and 21:9 */
    HfDraw draw;
    memset(&draw, 0, sizeof(draw));
    draw.size = sizeof(draw);
    draw.width = 1920.0f;
    draw.height = 1080.0f;
    draw.pixels = 1.0f;
    draw.rect = DrawRect;
    draw.lines = DrawLines;
    draw.polygon = DrawPolygon;
    draw.circle = DrawCircle;
    draw.text = DrawTextFn;
    draw.text_width = DrawTextWidth;
    draw.clip_push = DrawClipPush;
    draw.clip_pop = DrawClipPop;

    HfInput in;
    memset(&in, 0, sizeof(in));
    in.size = sizeof(in);

    memset(&g_draw_stats, 0, sizeof(g_draw_stats));
    CHECK(g_registered_screen.draw(NULL, &draw, &in, 0.10f) == 1, "boot frame draws");
    CHECK(SeenDrawText("ROTHANA HEAVY ENG."), "boot sequence displays Rothana AT-TE OS banner");
    CHECK(g_draw_stats.clips_pushed == g_draw_stats.clips_popped && g_draw_stats.clips_pushed == 1u,
          "balanced clip_push / clip_pop on compass tape");

    /* Advance past 0.55s boot (6 x 0.10s frames) into full steady-state HUD on 21:9 ultrawide (2520x1080) */
    draw.width = 2520.0f;
    for (int bf = 0; bf < 5; bf++) {
        CHECK(g_registered_screen.draw(NULL, &draw, &in, 0.10f) == 1, "boot ramp frame");
    }
    memset(&g_draw_stats, 0, sizeof(g_draw_stats));
    CHECK(g_registered_screen.draw(NULL, &draw, &in, 0.10f) == 1, "steady-state 21:9 frame draws");
    CHECK(g_draw_stats.polygons >= 6u && g_draw_stats.lines >= 15u && g_draw_stats.rects >= 6u,
          "emitted canopy bezels, 6-leg hexapod schematic, compass & ordnance bars");
    CHECK(SeenDrawText("ARMOR INTEGRITY  90%  [1350 / 1500]"), "rendered exact HealthManager 1350/1500 HP");
    CHECK(SeenDrawText("VEL   05.0 M/S"), "rendered exact MotionManager 5.0 m/s ground speed");
    CHECK(SeenDrawText("14 / 14") && SeenDrawText("750 / 1000"), "rendered both MountManager weapon bays");
    CHECK(SeenDrawText("CHASSIS // AT-TE HEXAPOD"), "rendered AT-TE hexapod label");

    /* Drop walker HP to 300 / 1500 (20%) -> critical warning banner triggers */
    int32_t crit_hp = 300;
    memcpy(g_mock_hm_recs + 1u * TFH_HM_RECORD_STRIDE + TFH_HM_HEALTH_OFFSET, &crit_hp, 4u);
    g_gt_tick((HfMod*)&g_mod_obj, 0.016f, NULL);
    memset(&g_draw_stats, 0, sizeof(g_draw_stats));
    CHECK(g_registered_screen.draw(NULL, &draw, &in, 0.05f) == 1, "critical hull frame draws");
    CHECK(SeenDrawText("WARNING // HULL CRITICAL"), "critical hull warning activates below 30% HP");

    /* Disembark -> screen_show(0) + smooth 0.20s closing animation to 0 */
    g_u_avatar.graph.link_parent = 0u;
    TfhStepDetour(NULL);
    CHECK(g_screen_open_state == 0, "HUD closes immediately upon disembarking AT-TE");
    in.closing = 1u;
    CHECK(g_registered_screen.draw(NULL, &draw, &in, 0.10f) == 1, "mid-shutdown animation frame returns 1");
    CHECK(g_registered_screen.draw(NULL, &draw, &in, 0.15f) == 0, "completed shutdown animation returns 0");

    /* 106-node Empire AT-ST walker MUST stay closed by default, and open only when enable_on_atst = true */
    ResetHarness(kTfhWalkerResources[0], TFH_ATST_S2C_NODES, 4.70f);
    g_u_avatar.graph.link_parent = g_u_walker.graph.world_poses;
    g_gt_tick((HfMod*)&g_mod_obj, 0.016f, NULL);
    TfhStepDetour(NULL);
    CHECK(g_screen_open_state == 0 && g_set_pose_calls == 0, "106-node Empire AT-ST skipped by default");

    g_opts.enable_on_atst = 1;
    TfhStepDetour(NULL);
    CHECK(g_screen_open_state == 1, "106-node Empire AT-ST opens when enable_on_atst = true");
}

static void TestDualViewFirstPersonRaycastAndRestore(void)
{
    ResetHarness(kTfhWalkerResources[1], 198u, 2.06f);
    g_u_avatar.graph.link_parent = g_u_walker.graph.world_poses;
    g_gt_tick((HfMod*)&g_mod_obj, 0.016f, NULL);

    /* Frame 1: starts in 3P Tactical (zero camera pose overrides) */
    TfhStepDetour(NULL);
    CHECK(g_set_pose_calls == 0 && g_cam.first_person == 0, "starts in 3P Tactical without overriding camera");

    /* Press F7 (0x76) -> switches to 1P Forward-Canopy viewport (walker at Y=2.0, Z=0.0 -> Y=4.35, Z=2.85) */
    g_testSimulatedVkDown = 0x76u;
    g_corrupt_env_in_raycast = 1;
    unsigned int orig_csr = _mm_getcsr();
    SetLastError(0x24681357u);

    TfhStepDetour(NULL);
    g_testSimulatedVkDown = 0u;
    CHECK(g_set_pose_calls == 1 && g_cam.first_person == 1, "F7 toggled into 1P Forward-Canopy view");
    CHECK_NEAR(g_last_pose[9], 0.0f, 1e-4f, "1P canopy X");
    CHECK_NEAR(g_last_pose[10], 2.0f + g_opts.fp_forward_offset, 1e-4f, "1P canopy Y (forward)");
    CHECK_NEAR(g_last_pose[11], 0.0f + g_opts.fp_up_offset, 1e-4f, "1P canopy Z (up)");
    CHECK(g_orig_seen_err == 0x24681357u && GetLastError() == 0x24681357u, "GetLastError preserved");
    CHECK(g_orig_seen_csr == orig_csr && _mm_getcsr() == orig_csr, "SSE MXCSR preserved");

    /* 20 paused frames (engine does not overwrite local_poses[0]) -> zero cumulative drift! */
    for (int f = 0; f < 20; f++) {
        TfhStepDetour(NULL);
    }
    CHECK_NEAR(g_last_pose[10], 2.0f + g_opts.fp_forward_offset, 1e-4f, "unoverwritten-pose guard prevents drift");

    /* Cliff wall 1.20m ahead of walker spine (Y = 3.20m) -> Havok raycast clamps 1P camera to 3.20 - 0.28 = 2.92m */
    g_ray_hit = 1;
    g_ray_hit_pt = tfh_v3(0.0f, 3.20f, g_opts.fp_up_offset);
    TfhStepDetour(NULL);
    CHECK_NEAR(g_last_pose[10], 3.20f - g_opts.wall_safety_margin, 1e-4f, "1P canopy clamped safely before cliff wall");
    g_ray_hit = 0;

    /* Press F7 again -> restores exact unmodified 3P base pose (-4.5, 3.8)! */
    g_set_pose_calls = 0;
    g_testSimulatedVkDown = 0x76u;
    TfhStepDetour(NULL);
    g_testSimulatedVkDown = 0u;
    CHECK(g_cam.first_person == 0 && g_set_pose_calls == 1, "F7 back to 3P writes restored base pose");
    CHECK_NEAR(g_last_pose[10], -4.5f, 1e-4f, "restored exact 3P Y=-4.5m");
    CHECK_NEAR(g_last_pose[11], 3.8f, 1e-4f, "restored exact 3P Z=3.8m");

    /* Release F7 for 1 frame, then toggle 1P back on & trigger 4 -> 5 mission teardown */
    TfhStepDetour(NULL);
    g_testSimulatedVkDown = 0x76u;
    TfhStepDetour(NULL);
    g_testSimulatedVkDown = 0u;
    CHECK(g_cam.first_person == 1, "re-entered 1P canopy");

    HfStateEvent sev = {{sizeof(sev), HF_ON_STATE, 5}, HF_STATE_MISSION, HF_STATE_PREPARE_SHIP};
    g_mock_state = HF_STATE_PREPARE_SHIP;
    if (g_ev_state) g_ev_state((HfMod*)&g_mod_obj, &sev, NULL);
    CHECK(g_screen_open_state == 0 && InterlockedCompareExchange(&g_disarmed, 0, 0) == 1,
          "4 -> 5 teardown closes HUD and disarms atomically");
    CHECK(hellforge_mod_unload(&g_api, (HfMod*)&g_mod_obj, HF_UNLOAD_REMOVE) == 0, "clean unload");
}

int main(int argc, char** argv)
{
    const char* temp_dir = (argc > 1) ? argv[1] : "";
    TestOptionsAndAngles(temp_dir);
    TestAtteHudLifecycleTelemetryAndAtstGate();
    TestDualViewFirstPersonRaycastAndRestore();

    if (g_failed != 0) {
        fprintf(stderr, "titanfall-walker-hud unit suite FAILED (%d / %d)\n", g_failed, g_checks);
        return 1;
    }
    printf("titanfall-walker-hud v0.1.0 unit suite: %d checks run, 0 failed\n", g_checks);
    return 0;
}
