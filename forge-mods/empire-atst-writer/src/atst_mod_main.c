/* SDK-16 entry point for clonedivers.empire-atst-writer (build 25480438 pinned). */
#define WIN32_LEAN_AND_MEAN
#include "hellforge_mod.h"
#include "atst_code_pins.h"
#include "atst_writer_core.h"

#include <stddef.h>
#include <stdint.h>
#include <string.h>

#define AT_MOD_ID "clonedivers.empire-atst-writer"

static const HfApi *g_hf = NULL;
static HfMod *g_mod = NULL;
static at_writer_state g_writer;

static int sdk_safe_read(uintptr_t addr, void *buf, size_t sz, void *user) {
    const HfApi *api = (const HfApi *)user;
    if (!api || !api->read) return -1;
    return api->read(addr, buf, sz);
}

static uintptr_t sdk_peer_module_lookup(const char *module_name, size_t *out_size, void *user) {
    const HfApi *api = (const HfApi *)user;
    if (!api || !api->module_base || !module_name) return 0u;
    return api->module_base(module_name, out_size);
}

static uint32_t Hook512B80(void *mesh, void *pool, void *graph) {
    uintptr_t caller = (uintptr_t)__builtin_return_address(0);
    return at_writer_dispatch_512B80(&g_writer, mesh, pool, graph, caller, NULL);
}

static uint32_t Hook5B2990(
    void *dst,
    const void *factor,
    const void *graph,
    const void *bind,
    const void *palette,
    const void *map,
    uint32_t count) {
    uintptr_t caller = (uintptr_t)__builtin_return_address(0);
    return at_writer_leaf_5B2990(&g_writer, dst, factor, graph, bind, palette, map, count, caller);
}

static uint64_t Hook1E7CD0(uintptr_t world, void *packet, uintptr_t aux) {
    return at_writer_retire_1E7CD0(&g_writer, world, packet, aux);
}

static void HookCameraStep(void *app) {
    DWORD saved_err = GetLastError();
    uint32_t saved_csr = _mm_getcsr();
    at_writer_step_pilots(&g_writer, 0u);
    _mm_setcsr(saved_csr);
    SetLastError(saved_err);
    at_camera_step_fn orig = __atomic_load_n(&g_writer.step_next, __ATOMIC_ACQUIRE);
    if (orig) orig(app);
}

static uint64_t HookCameraSubStep(void *rcx, void *rdx, void *r8, uint64_t r9, void *arg5) {
    uintptr_t caller_ret = (uintptr_t)__builtin_return_address(0);
    if (caller_ret == g_writer.engine_base + AT_RVA_CAMERA_STEP_RET) {
        DWORD saved_err = GetLastError();
        uint32_t saved_csr = _mm_getcsr();
        at_writer_step_pilots(&g_writer, 0u);
        _mm_setcsr(saved_csr);
        SetLastError(saved_err);
    }
    at_camera_sub_step_fn orig = __atomic_load_n(&g_writer.sub_step_next, __ATOMIC_ACQUIRE);
    return orig ? orig(rcx, rdx, r8, r9, arg5) : 0ULL;
}

static uint64_t g_qpf = 0u;
static uint64_t g_tick_us_sum = 0u;
static uint64_t g_tick_us_max = 0u;
static uint64_t g_tick_count = 0u;
static uint64_t g_next_poll_ms = 0u;
static uint64_t g_next_health_ms = 0u;
static uint32_t g_health_row = 0u;
static volatile LONG g_burst_frames = 120;
static uint32_t g_prev_game_state = 0u;

static void OnSpawn(HfMod *mod, const HfEventInfo *event, void *user) {
    (void)mod;
    (void)event;
    (void)user;
    /* Burst-scan immediately on any unit spawn so newly dropped EXO-45/49/51/55 walkers on
     * 126k-unit Mega City maps admit on frame 0-3 (<50 ms) instead of waiting on a 5 Hz sweep. */
    InterlockedExchange(&g_burst_frames, 90);
}

static void WriterTick(HfMod *mod, float dt, void *user) {
    (void)dt;
    (void)user;
    if (g_hf && g_hf->size >= offsetof(HfApi, game_state) + sizeof(g_hf->game_state) && g_hf->game_state && mod) {
        uint32_t st = g_hf->game_state(mod);
        if (st != g_prev_game_state) {
            g_prev_game_state = st;
            if (st == HF_STATE_MISSION || st == HF_STATE_PREPARE_MISSION) {
                InterlockedExchange(&g_burst_frames, 120);
            }
        }
    }
    int run_burst = 0;
    if (InterlockedCompareExchange(&g_burst_frames, 0, 0) > 0) {
        InterlockedDecrement(&g_burst_frames);
        run_burst = 1;
    }
    uint64_t now = GetTickCount64();
    if (run_burst || now >= g_next_poll_ms) {
        if (!run_burst) {
            g_next_poll_ms = now + 200u;
        }
        LARGE_INTEGER t0, t1;
        if (!g_qpf) {
            LARGE_INTEGER f;
            QueryPerformanceFrequency(&f);
            g_qpf = f.QuadPart ? (uint64_t)f.QuadPart : 1u;
        }
        QueryPerformanceCounter(&t0);
        /* Burst mode (on mission entry & HF_ON_SPAWN): 32,768-slot slice at 60 Hz; steady state: 5 Hz 4,096-slot slice. */
        at_writer_tick(&g_writer, run_burst ? 32768u : 4096u);
        QueryPerformanceCounter(&t1);
        uint64_t us = (uint64_t)((t1.QuadPart - t0.QuadPart) * 1000000LL / (LONGLONG)g_qpf);
        g_tick_us_sum += us;
        g_tick_count++;
        if (us > g_tick_us_max) g_tick_us_max = us;
    }

    if (now >= g_next_health_ms && g_hf && g_hf->log && mod && g_health_row < 600u) {
        g_next_health_ms = now + 30000u;
        g_health_row++;
        uint64_t avg_us = g_tick_count ? (g_tick_us_sum / g_tick_count) : 0u;
        uint64_t max_us = g_tick_us_max;
        g_tick_us_sum = 0u;
        g_tick_us_max = 0u;
        g_tick_count = 0u;
        g_hf->log(mod, "ATST_WRITER health row=%u walkers=%u subs=%u pilot_lifts=%u pilot_hides=%u leaf_fail=%u math_fail=%u chain_fail=%u env_fail=%u scav=%u ret=%u tick_us_avg=%llu tick_us_max=%llu",
                  g_health_row, __atomic_load_n(&g_writer.fast.count, __ATOMIC_ACQUIRE),
                  g_writer.substitutions, g_writer.pilot.lifted_frames,
                  __atomic_load_n(&g_writer.pilot.hidden_leaf_calls, __ATOMIC_RELAXED),
                  g_writer.leaf_failures, g_writer.math_failures,
                  g_writer.chain_failures, g_writer.env_failures,
                  g_writer.reg.scavenged, g_writer.reg.retirements,
                  (unsigned long long)avg_us, (unsigned long long)max_us);
    }
}

static void OnSessionEnd(HfMod *mod, HfEvent event, void *user) {
    (void)event;
    (void)user;
    at_writer_shutdown(&g_writer);
    if (g_hf && g_hf->log && mod) {
        g_hf->log(mod, "ATST_WRITER session_end subs=%u pass=%u retired=%u",
                  g_writer.substitutions, g_writer.passthroughs, g_writer.reg.retirements);
    }
}

static void OnGameLoaded(HfMod *mod, HfEvent event, void *user) {
    (void)event;
    (void)user;
    const HfApi *hf = g_hf;
    if (!hf || !mod) return;

    const char *build = hf->build_id ? hf->build_id() : NULL;
    if (!build || strcmp(build, AT_EXPECTED_BUILD) != 0) {
        hf->log(mod, "ATST_WRITER refused: build_mismatch got=%s expected=%s",
                build ? build : "null", AT_EXPECTED_BUILD);
        return;
    }

    uintptr_t engine = hf->module_base ? hf->module_base("helldivers2.exe", NULL) : 0u;
    if (!engine) {
        hf->log(mod, "ATST_WRITER refused: helldivers2.exe missing");
        return;
    }

    uint32_t failed_rva = 0;
    if (!atst_verify_code_pins(hf->read_code, engine, &failed_rva)) {
        hf->log(mod, "ATST_WRITER refused: code_pin_mismatch rva=0x%08X", failed_rva);
        return;
    }

    if (!at_writer_init(&g_writer, engine, sdk_safe_read, (void *)hf)) {
        hf->log(mod, "ATST_WRITER refused: writer_init_failed");
        return;
    }
    at_writer_set_peer_lookup(&g_writer, sdk_peer_module_lookup, (void *)hf);

    if (hf->hook_ex(mod, engine + AT_RVA_LEAF_SKIN, (void *)Hook5B2990, (void **)&g_writer.leaf_next, AT_HOOK_PRIORITY) != 0 ||
        !g_writer.leaf_next) {
        hf->log(mod, "ATST_WRITER refused: hook_5B2990_failed");
        return;
    }
    g_writer.approved_leaf_next = (at_leaf_skin_fn)InterlockedCompareExchangePointer(
        (PVOID *)&g_writer.leaf_next, NULL, NULL);

    if (hf->hook_ex(mod, engine + AT_RVA_SKIN_JOB, (void *)Hook512B80, (void **)&g_writer.skin_next, AT_HOOK_PRIORITY) != 0 ||
        !g_writer.skin_next) {
        hf->log(mod, "ATST_WRITER refused: hook_512B80_failed");
        return;
    }
    g_writer.approved_skin_next = (at_skin_job_fn)InterlockedCompareExchangePointer(
        (PVOID *)&g_writer.skin_next, NULL, NULL);

    if (hf->hook_ex(mod, engine + AT_RVA_RENDER_REMOVE, (void *)Hook1E7CD0, (void **)&g_writer.remove_next, AT_HOOK_PRIORITY) != 0 ||
        !g_writer.remove_next) {
        hf->log(mod, "ATST_WRITER refused: hook_1E7CD0_failed");
        return;
    }
    g_writer.approved_remove_next = (at_render_remove_fn)InterlockedCompareExchangePointer(
        (PVOID *)&g_writer.remove_next, NULL, NULL);

    g_writer.pilot.set_local_pose = (at_set_local_pose_fn)(engine + AT_RVA_SET_LOCAL_POSE);
    g_writer.pilot.update_unit = (at_update_unit_fn)(engine + AT_RVA_UPDATE_UNIT);
    g_writer.pilot.main_world = (at_main_world_fn)(engine + AT_RVA_MAIN_WORLD);

    if (hf->hook_ex(mod, engine + AT_RVA_CAMERA_STEP, (void *)HookCameraStep, (void **)&g_writer.step_next, AT_HOOK_PILOT_PRIORITY) == 0 &&
        g_writer.step_next) {
        g_writer.step_hooked_rva = AT_RVA_CAMERA_STEP;
    } else if (hf->hook_ex(mod, engine + AT_RVA_CAMERA_SUB_STEP, (void *)HookCameraSubStep, (void **)&g_writer.sub_step_next, AT_HOOK_PILOT_PRIORITY) == 0 &&
               g_writer.sub_step_next) {
        g_writer.step_hooked_rva = AT_RVA_CAMERA_SUB_STEP;
    } else {
        hf->log(mod, "ATST_WRITER refused: hook_camera_step_failed");
        return;
    }

    if (!hf->on_game_thread(mod, WriterTick, NULL)) {
        hf->log(mod, "ATST_WRITER refused: tick_registration_failed");
        return;
    }
    int spawn_sub = 0;
    if (hf->size >= offsetof(HfApi, subscribe) + sizeof(hf->subscribe) && hf->subscribe != NULL) {
        spawn_sub = hf->subscribe(mod, HF_ON_SPAWN, OnSpawn, NULL, 0) ? 1 : 0;
    }
    InterlockedExchange(&g_burst_frames, 120);

    uintptr_t init_peer_base = (uintptr_t)InterlockedCompareExchangePointer(
        (PVOID *)&g_writer.peer_leaf_base, NULL, NULL);
    size_t init_peer_size = (size_t)InterlockedCompareExchangePointer(
        (PVOID *)&g_writer.peer_leaf_size, NULL, NULL);
    InterlockedExchange(&g_writer.ready, 1);
    hf->log(mod, "ATST_WRITER ready version=0.2.7 build=%s s2c_nodes=%u max_walkers=%u spawn_sub=%d step_rva=0x%X units=79E4B3D2DA5E45E3,C2D449ECF7FACAB1,7B2326F6FD9C8069,35DBF54F016F3624 engine=0x%llX peer_leaf=0x%llX peer_size=0x%llX hook_priority=%d",
            AT_EXPECTED_BUILD, AT_S2C_NODES, AT_MAX_WALKERS, spawn_sub, g_writer.step_hooked_rva,
            (unsigned long long)engine,
            (unsigned long long)init_peer_base,
            (unsigned long long)init_peer_size,
            (int)AT_HOOK_PRIORITY);
}

HF_MOD_EXPORT int hellforge_mod_unload(const HfApi *api, HfMod *mod, uint32_t reason) {
    (void)reason;
    at_writer_shutdown(&g_writer);
    if (api && mod && g_writer.engine_base &&
        api->size >= offsetof(HfApi, unhook) + sizeof(api->unhook) && api->unhook) {
        if (g_writer.step_hooked_rva) api->unhook(mod, g_writer.engine_base + g_writer.step_hooked_rva);
        if (g_writer.remove_next) api->unhook(mod, g_writer.engine_base + AT_RVA_RENDER_REMOVE);
        if (g_writer.skin_next) api->unhook(mod, g_writer.engine_base + AT_RVA_SKIN_JOB);
        if (g_writer.leaf_next) api->unhook(mod, g_writer.engine_base + AT_RVA_LEAF_SKIN);
    }
    return 0;
}

HF_MOD_EXPORT int hellforge_mod_init(const HfApi *api, HfMod *mod) {
    if (!api || !mod || api->version < 16u ||
        api->size < offsetof(HfApi, read_code) + sizeof(api->read_code)) {
        return 1;
    }
    if (!api->build_id || !api->mod_id || !api->load_phase || !api->read ||
        !api->read_code || !api->module_base || !api->on || !api->on_game_thread ||
        !api->hook_ex || !api->log) {
        return 1;
    }
    if (api->load_phase(mod) != HF_LOAD_BOOT) return 1;

    const char *build = api->build_id();
    const char *id = api->mod_id(mod);
    if (!build || strcmp(build, AT_EXPECTED_BUILD) != 0 ||
        !id || strcmp(id, AT_MOD_ID) != 0) {
        return 1;
    }

    g_hf = api;
    g_mod = mod;

    if (api->on(mod, HF_EVENT_SESSION_END, OnSessionEnd, NULL) != 0) return 1;
    if (api->on(mod, HF_EVENT_GAME_LOADED, OnGameLoaded, NULL) != 0) return 1;
    return 0;
}
