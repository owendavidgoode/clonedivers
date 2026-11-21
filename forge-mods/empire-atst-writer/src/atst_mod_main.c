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

static void WriterTick(HfMod *mod, float dt, void *user) {
    (void)mod;
    (void)dt;
    (void)user;
    /* Smooth rolling slice: 2048 slots/frame (~2s full D11 262k sweep at 60 FPS, no 4 Hz burst). */
    at_writer_tick(&g_writer, 2048u);
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

    if (!hf->on_game_thread(mod, WriterTick, NULL)) {
        hf->log(mod, "ATST_WRITER refused: tick_registration_failed");
        return;
    }

    uintptr_t init_peer_base = (uintptr_t)InterlockedCompareExchangePointer(
        (PVOID *)&g_writer.peer_leaf_base, NULL, NULL);
    size_t init_peer_size = (size_t)InterlockedCompareExchangePointer(
        (PVOID *)&g_writer.peer_leaf_size, NULL, NULL);
    InterlockedExchange(&g_writer.ready, 1);
    hf->log(mod, "ATST_WRITER ready build=%s s2c_nodes=%u max_walkers=%u unit=79E4B3D2DA5E45E3 engine=0x%llX peer_leaf=0x%llX peer_size=0x%llX hook_priority=%d",
            AT_EXPECTED_BUILD, AT_S2C_NODES, AT_MAX_WALKERS,
            (unsigned long long)engine,
            (unsigned long long)init_peer_base,
            (unsigned long long)init_peer_size,
            (int)AT_HOOK_PRIORITY);
}

HF_MOD_EXPORT int hellforge_mod_init(const HfApi *api, HfMod *mod) {
    if (!api || !mod || api->version != HF_MOD_SDK_VERSION ||
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
