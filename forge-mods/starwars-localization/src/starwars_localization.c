/* Star Wars Localization Bridge (clonedivers.starwars-localization 0.2.0)
 * Target: Helldivers 2 Steam build 25480438, HellForge Mod SDK 16-20.
 *
 * Restores EmpireDivers / Clonedivers US English TEXT overrides in memory whenever
 * HellForge's auto-generated forge-credits patch stages last and overwrites US English
 * TEXT resources in 9ba626afa44a3aa3, AND serves curated v2.3 overrides (100% vanilla HD2
 * item/weapon/booster/stratagem references, Clonedivers Overloaded Gonk Droid Hellbomb, and
 * EmpireDivers DS-1 Death Star) in both clobbered and unclobbered Star Wars configurations,
 * while keeping every Forge Credits string intact and never touching HellForge's engine-API
 * text vtable slot (table + 1000). */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "hellforge_mod.h"
#include "loc_table.h"

/* Reviewed build 25480438 game.dll engine API pointer offset (HellForge registry.c:83). */
#define SW_LOC_TARGET_BUILD "25480438"
#define SW_LOC_REG_ENGINE_API_RVA 0x3326308u
#define SW_LOC_ENGINE_API_TABLE_OFF 0x10u
#define SW_LOC_ENGINE_TEXT_SLOT_OFF 1000u

typedef const char* (*SwEngineTextFn)(uint32_t id, void* a, void* b, void* c);

static const HfApi* g_api;
static HfMod* g_mod;
static SwLocTable g_table;
static SwEngineTextFn g_orig_text;
static uintptr_t g_hooked_target;
static volatile LONG g_hook_state; /* 0 = pending, 1 = installing, 2 = hooked, -1 = failed */
static volatile LONG64 g_hits;
static volatile LONG64 g_misses;
static volatile LONG g_tick_attempts;
static volatile LONG g_logged_first_hit;
static uint32_t g_first_hit_id;
static const char* g_first_hit_text;
static float g_stats_elapsed;
static LONG64 g_last_logged_hits;

static const char* SwTextDetour(uint32_t id, void* a, void* b, void* c) {
    const char* hit = SwLocLookup(&g_table, id);
    if (hit != NULL) {
        if (InterlockedCompareExchange(&g_logged_first_hit, 1, 0) == 0) {
            g_first_hit_id = id;
            g_first_hit_text = hit;
            MemoryBarrier();
            InterlockedExchange(&g_logged_first_hit, 2);
        }
        InterlockedIncrement64(&g_hits);
        return hit;
    }
    InterlockedIncrement64(&g_misses);
    return g_orig_text != NULL ? g_orig_text(id, a, b, c) : "";
}

static int ResolveDataDirFromExe(char* out, size_t out_size) {
    char exe_path[MAX_PATH];
    DWORD len = GetModuleFileNameA(NULL, exe_path, (DWORD)(sizeof exe_path));
    if (len == 0 || len >= sizeof exe_path) return 0;

    /* Strip \helldivers2.exe -> <game>\bin */
    char* slash1 = strrchr(exe_path, '\\');
    if (slash1 == NULL) slash1 = strrchr(exe_path, '/');
    if (slash1 == NULL) return 0;
    *slash1 = '\0';

    /* Strip \bin -> <game> */
    char* slash2 = strrchr(exe_path, '\\');
    if (slash2 == NULL) slash2 = strrchr(exe_path, '/');
    if (slash2 == NULL) return 0;
    *slash2 = '\0';

    int n = snprintf(out, out_size, "%s\\data", exe_path);
    return n > 0 && (size_t)n < out_size;
}

static void TryInstallTextHook(const char* where) {
    if (g_table.count == 0 || g_api == NULL || g_mod == NULL) return;
    if (InterlockedCompareExchange(&g_hook_state, 0, 0) != 0) return;

    size_t game_size = 0;
    uintptr_t game = g_api->module_base("game.dll", &game_size);
    if (game == 0 || game_size <= SW_LOC_REG_ENGINE_API_RVA + 8u) return;

    uintptr_t api_ptr = 0;
    uintptr_t table_ptr = 0;
    uintptr_t text_fn = 0;
    if (g_api->read(game + SW_LOC_REG_ENGINE_API_RVA, &api_ptr, sizeof api_ptr) != 0 || api_ptr == 0) return;
    if (g_api->read(api_ptr + SW_LOC_ENGINE_API_TABLE_OFF, &table_ptr, sizeof table_ptr) != 0 || table_ptr == 0) return;
    if (g_api->read(table_ptr + SW_LOC_ENGINE_TEXT_SLOT_OFF, &text_fn, sizeof text_fn) != 0 || text_fn == 0) return;

    if (InterlockedCompareExchange(&g_hook_state, 1, 0) != 0) return;

    /* Verify text_fn is in readable executable code (helldivers2.exe or HellForge's TextDetour). */
    uint8_t probe[4] = {0, 0, 0, 0};
    if (g_api->read(text_fn, probe, sizeof probe) != 0) {
        InterlockedExchange(&g_hook_state, 0);
        return;
    }

    int rc = g_api->hook_ex(g_mod, text_fn, (void*)SwTextDetour, (void**)&g_orig_text, 0);
    if (rc != 0) {
        InterlockedExchange(&g_hook_state, -1);
        g_api->log(g_mod, "starwars-localization: hook_ex failed at 0x%llx (%s); staying fail-closed",
            (unsigned long long)text_fn, where);
        return;
    }

    g_hooked_target = text_fn;
    InterlockedExchange(&g_hook_state, 2);

    const char* owner = "unknown";
    uintptr_t owner_base = 0;
    HMODULE mod_handle = NULL;
    char mod_name[MAX_PATH];
    if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            (LPCSTR)text_fn, &mod_handle) && mod_handle != NULL) {
        owner_base = (uintptr_t)mod_handle;
        DWORD n = GetModuleFileNameA(mod_handle, mod_name, (DWORD)(sizeof mod_name));
        if (n > 0 && n < sizeof mod_name) {
            const char* slash = strrchr(mod_name, '\\');
            owner = slash != NULL ? slash + 1 : mod_name;
        }
    }
    g_api->log(g_mod,
        "starwars-localization: hooked engine text lookup at 0x%llx (%s+0x%llx) on %s — serving %u Star Wars overrides",
        (unsigned long long)text_fn,
        owner,
        owner_base != 0 && text_fn >= owner_base ? (unsigned long long)(text_fn - owner_base) : 0ULL,
        where,
        g_table.count);
}

static void OnEvent(HfMod* mod, HfEvent event, void* user) {
    (void)mod;
    (void)user;
    if (event == HF_EVENT_GAME_LOADED) {
        TryInstallTextHook("HF_EVENT_GAME_LOADED");
    } else if (event == HF_EVENT_ENTITIES_READY) {
        TryInstallTextHook("HF_EVENT_ENTITIES_READY");
    } else if (event == HF_EVENT_SESSION_END) {
        if (g_api != NULL && g_mod != NULL) {
            g_api->log(g_mod, "starwars-localization: session end stats hits=%lld misses=%lld overrides=%u",
                (long long)InterlockedCompareExchange64(&g_hits, 0, 0),
                (long long)InterlockedCompareExchange64(&g_misses, 0, 0),
                g_table.count);
        }
    }
}

static void OnGameThread(HfMod* mod, float dt, void* user) {
    (void)mod;
    (void)user;
    if (InterlockedCompareExchange(&g_hook_state, 0, 0) == 0) {
        LONG attempt = InterlockedIncrement(&g_tick_attempts);
        if (attempt <= 600) {
            TryInstallTextHook("on_game_thread");
        }
        return;
    }
    if (InterlockedCompareExchange(&g_logged_first_hit, 3, 2) == 2 && g_api != NULL && g_mod != NULL) {
        g_api->log(g_mod, "starwars-localization: first live override id=0x%08x -> \"%.64s\"",
            g_first_hit_id, g_first_hit_text != NULL ? g_first_hit_text : "");
    }
    if (dt > 0.0f && dt < 5.0f) {
        g_stats_elapsed += dt;
        if (g_stats_elapsed >= 30.0f) {
            g_stats_elapsed = 0.0f;
            LONG64 hits = InterlockedCompareExchange64(&g_hits, 0, 0);
            if (hits != g_last_logged_hits && g_api != NULL && g_mod != NULL) {
                g_last_logged_hits = hits;
                g_api->log(g_mod, "starwars-localization: health hits=%lld misses=%lld overrides=%u",
                    (long long)hits,
                    (long long)InterlockedCompareExchange64(&g_misses, 0, 0),
                    g_table.count);
            }
        }
    }
}

HF_MOD_EXPORT int hellforge_mod_init(const HfApi* api, HfMod* mod) {
    if (api == NULL || mod == NULL) return 1;
    if (api->version < 16u || api->size < offsetof(HfApi, hook_ex) + sizeof(api->hook_ex) || api->log == NULL) {
        return 1;
    }
    if (api->on == NULL || api->on_game_thread == NULL || api->module_base == NULL ||
        api->read == NULL || api->hook_ex == NULL || api->build_id == NULL) {
        api->log(mod, "starwars-localization: missing required SDK 16+ API pointers; staying inactive");
        return 0;
    }

    const char* build_id = api->build_id();
    if (build_id == NULL || strcmp(build_id, SW_LOC_TARGET_BUILD) != 0) {
        api->log(mod, "starwars-localization: unsupported game build '%s' (expected %s); staying inactive",
            build_id != NULL ? build_id : "<null>", SW_LOC_TARGET_BUILD);
        return 0;
    }

    g_api = api;
    g_mod = mod;
    g_hooked_target = 0;
    InterlockedExchange(&g_hook_state, 0);
    InterlockedExchange64(&g_hits, 0);
    InterlockedExchange64(&g_misses, 0);
    InterlockedExchange(&g_tick_attempts, 0);
    InterlockedExchange(&g_logged_first_hit, 0);
    g_stats_elapsed = 0.0f;
    g_last_logged_hits = 0;

    char data_dir[MAX_PATH];
    if (!ResolveDataDirFromExe(data_dir, sizeof data_dir)) {
        api->log(mod, "starwars-localization: could not resolve game data directory; staying inactive");
        return 0;
    }

    SwLocLoadReport report;
    if (SwLocLoadFromDataDir(data_dir, &g_table, &report) != 0) {
        api->log(mod, "starwars-localization: scan failed for %s; staying inactive", data_dir);
        return 0;
    }

    switch (report.status) {
        case SW_LOC_NO_PATCHES:
            api->log(mod, "starwars-localization: no US TEXT patches found in %s (%d archive(s) scanned); inactive",
                data_dir, report.scanned_patches);
            return 0;
        case SW_LOC_ALREADY_TOPMOST:
            api->log(mod, "starwars-localization: pack TEXT patch_%d is already topmost (%u strings, mode=%s); no hook needed",
                report.base_patch_index, report.base_string_count, SwLocModeName(report.mode));
            return 0;
        case SW_LOC_VANILLA_ONLY:
            api->log(mod, "starwars-localization: forge-credits patch_%d has no Star Wars TEXT beneath it; inactive",
                report.base_patch_index);
            return 0;
        case SW_LOC_CURATED_LOADED:
            api->log(mod,
                "starwars-localization: pack TEXT patch_%d is topmost (%s mode, %u strings); serving %u curated v2.3 overrides",
                report.base_patch_index,
                SwLocModeName(report.mode),
                report.base_string_count,
                report.curated_applied);
            break;
        case SW_LOC_DIFF_LOADED:
            api->log(mod,
                "starwars-localization: loaded %u overrides (%u curated v2.3, mode=%s) from patch_%d (%u strings) beneath forge-credits patch_%d (%u strings; %u Forge Credits labels preserved)",
                report.diff_count,
                report.curated_applied,
                SwLocModeName(report.mode),
                report.pack_patch_index,
                report.pack_string_count,
                report.base_patch_index,
                report.base_string_count,
                report.preserved_forge_credits);
            break;
        default:
            api->log(mod, "starwars-localization: unexpected status %d; staying inactive", (int)report.status);
            return 0;
    }

    api->on(mod, HF_EVENT_GAME_LOADED, OnEvent, NULL);
    api->on(mod, HF_EVENT_ENTITIES_READY, OnEvent, NULL);
    if (api->version >= 16u) {
        api->on(mod, HF_EVENT_SESSION_END, OnEvent, NULL);
    }
    api->on_game_thread(mod, OnGameThread, NULL);
    TryInstallTextHook("init");
    return 0;
}

HF_MOD_EXPORT int hellforge_mod_unload(const HfApi* api, HfMod* mod, uint32_t reason) {
    (void)reason;
    if (InterlockedCompareExchange(&g_hook_state, 0, 0) == 2 &&
        g_hooked_target != 0 && api != NULL &&
        api->size >= offsetof(HfApi, unhook) + sizeof(api->unhook) && api->unhook != NULL) {
        api->unhook(mod, g_hooked_target);
    }
    InterlockedExchange(&g_hook_state, 0);
    g_hooked_target = 0;
    g_orig_text = NULL;
    SwLocFreeTable(&g_table);
    return 0;
}
