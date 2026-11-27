/* Rows on the MODS page + Clonedivers Launcher host options integration.
 * Precedence:
 *   1. options.ini defaults (constant=0 by default)
 *   2. <modsDir>\clonedivers-host-options.json ("standard" / "heavy" / "constant") on SOLO/HOST
 *   3. Newer live session edits on the MODS page (HeavySeafMenu.values / UMM callback)
 *      via Win32 FILETIME mtime comparison; a newer Launcher Apply overrides stale session caches. */
#include "options_menu.h"
#include "heavy_gate_and_pacing.h"
#include "umm_api.h"

#include <malloc.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define MOD_ID "local.heavy-seaf-presence"
#define MOD_NAME "Heavy SEAF Presence Everywhere"

static const HfApi* g_hf;
static HfMod* g_self;
static int g_constant = 0;
static int g_support_squads = 0;
static int g_tank_hunters = 0;
static int g_interval = 120;
static int g_squads = 1;
static int g_extra_squads = 0;
static int g_wave_ceiling = 24;
static int g_clone_wars = 0;
static int g_registered;

static char g_host_opts_path[CD_HOST_OPTIONS_MAX_PATH];
static uint64_t g_host_opts_mtime;
static uint64_t g_session_edit_mtime;
static int g_host_opts_loaded;

static int MenuIsHostOrSolo(void)
{
    HfSession s;
    if (g_hf == NULL || g_self == NULL || g_hf->session == NULL) return 0;
    memset(&s, 0, sizeof s);
    s.role = 0xFFFFFFFFu;
    g_hf->session(g_self, &s);
    return s.role == HF_SESSION_SOLO || s.role == HF_SESSION_HOST;
}

static uint64_t MenuNowFileTime100ns(void)
{
    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    return ((uint64_t)ft.dwHighDateTime << 32) | (uint64_t)ft.dwLowDateTime;
}

static void BridgeWrite(void);

static void Save(void)
{
    /* Mod folder may be read-only during active HellForge 0.4.0 sessions; keep bridge synced
     * and only best-effort update options.ini if writable. */
    char path[520];
    FILE* file;
    const char* folder = g_hf ? g_hf->mod_folder(g_self) : NULL;
    BridgeWrite();
    if (folder == NULL) return;
    snprintf(path, sizeof path, "%s\\options.ini", folder);
    file = fopen(path, "w");
    if (file == NULL) return;
    fprintf(file, "constant=%d\nsupport_squads=%d\ntank_hunters=%d\ninterval_seconds=%d\nsquads_per_wave=%d\n",
        g_constant ? 1 : 0, g_support_squads ? 1 : 0, g_tank_hunters ? 1 : 0, g_interval, g_squads);
    fclose(file);
}

static void Load(void)
{
    char path[520];
    char line[64];
    FILE* file;
    const char* folder = g_hf->mod_folder(g_self);
    g_constant = 0;
    g_support_squads = 0;
    g_tank_hunters = 0;
    g_interval = 120;
    g_squads = 1;
    g_extra_squads = 0;
    g_wave_ceiling = 24;
    g_clone_wars = 0;
    if (folder == NULL) return;
    snprintf(path, sizeof path, "%s\\options.ini", folder);
    file = fopen(path, "r");
    if (file == NULL) return;
    while (fgets(line, sizeof line, file) != NULL) {
        if (strncmp(line, "constant=1", 10) == 0) g_constant = 1;
        else if (strncmp(line, "constant=0", 10) == 0) g_constant = 0;
        else if (strncmp(line, "support_squads=1", 16) == 0) g_support_squads = 1;
        else if (strncmp(line, "support_squads=0", 16) == 0) g_support_squads = 0;
        else if (strncmp(line, "tank_hunters=1", 14) == 0) g_tank_hunters = 1;
        else if (strncmp(line, "tank_hunters=0", 14) == 0) g_tank_hunters = 0;
        else if (strncmp(line, "interval_seconds=", 17) == 0) {
            int seconds = atoi(line + 17);
            if (seconds >= 10 && seconds <= 900) g_interval = seconds;
        } else if (strncmp(line, "squads_per_wave=", 16) == 0) {
            int squads = atoi(line + 16);
            if (squads >= 1 && squads <= 6) g_squads = squads;
        }
    }
    fclose(file);
}

/* ------------------------------------------------------------- Lua bridge */
#define BRIDGE_FILE "HeavySeafMenu.values"

static char g_bridge_path[600];
static char g_bridge_seen[160];

static void BridgeLocate(void)
{
    char base[480];
    char folder[560];
    DWORD n = GetEnvironmentVariableA("LOCALAPPDATA", base, (DWORD)sizeof base);
    g_bridge_path[0] = 0;
    if (n == 0 || n >= (DWORD)sizeof base) return;
    snprintf(folder, sizeof folder, "%s\\CowboyBingus", base);
    CreateDirectoryA(folder, NULL);
    snprintf(folder, sizeof folder, "%s\\CowboyBingus\\Helldivers2", base);
    CreateDirectoryA(folder, NULL);
    snprintf(folder, sizeof folder, "%s\\CowboyBingus\\Helldivers2\\Logs", base);
    CreateDirectoryA(folder, NULL);
    snprintf(g_bridge_path, sizeof g_bridge_path,
        "%s\\CowboyBingus\\Helldivers2\\Logs\\" BRIDGE_FILE, base);
}

static void BridgeWrite(void)
{
    FILE* file;
    if (g_bridge_path[0] == 0) return;
    snprintf(g_bridge_seen, sizeof g_bridge_seen,
        "constant=%d\nsupport_squads=%d\ntank_hunters=%d\ninterval_seconds=%d\nsquads_per_wave=%d\n",
        g_constant ? 1 : 0, g_support_squads ? 1 : 0, g_tank_hunters ? 1 : 0, g_interval, g_squads);
    file = fopen(g_bridge_path, "wb");
    if (file == NULL) return;
    fputs(g_bridge_seen, file);
    fclose(file);
}

static void ApplyNumber(int which, int value, const char* from);
static void Apply(int which, int on, const char* from);

static void BridgePoll(void)
{
    FILE* file;
    char text[192];
    size_t n;
    int constant, support, tank, interval, squads;
    if (g_bridge_path[0] == 0) return;
    file = fopen(g_bridge_path, "rb");
    if (file == NULL) return;
    n = fread(text, 1, sizeof text - 1, file);
    fclose(file);
    text[n] = 0;
    if (n == 0 || strcmp(text, g_bridge_seen) == 0) return;
    if (sscanf(text,
            "constant=%d\nsupport_squads=%d\ntank_hunters=%d\ninterval_seconds=%d\nsquads_per_wave=%d",
            &constant, &support, &tank, &interval, &squads) != 5)
        return;
    memcpy(g_bridge_seen, text, n + 1);
    {
        uint64_t bm = 0ULL;
        if (cd_get_file_mtime_100ns(g_bridge_path, &bm) == 1 && bm != 0ULL)
            g_session_edit_mtime = bm;
        else
            g_session_edit_mtime = MenuNowFileTime100ns();
    }
    ApplyNumber(3, interval, "MODS page");
    ApplyNumber(4, squads, "MODS page");
    Apply(0, constant, "MODS page");
    Apply(1, support, "MODS page");
    Apply(2, tank, "MODS page");
}

static void LoadHostOptions(int is_poll)
{
    uint64_t hm = 0ULL;
    CdHostOptions ho;
    int c, sup, iv, sq, ex;
    if (!g_host_opts_path[0]) return;
    if (is_poll && !MenuIsHostOrSolo()) return;
    if (cd_get_file_mtime_100ns(g_host_opts_path, &hm) != 1) return;
    if (is_poll && hm == g_host_opts_mtime) return;
    g_host_opts_mtime = hm;
    if (is_poll && cd_session_override_is_newer(hm, g_session_edit_mtime)) return;
    if (cd_load_host_options_file(g_host_opts_path, &ho) != 1) return;

    g_host_opts_loaded = 1;
    CdHeavySeafMode eff_seaf = cd_effective_heavy_seaf(&ho);
    int cw = (eff_seaf == CD_HEAVY_SEAF_CLONE_WARS) ? 1 : 0;
    int wc = (ho.seaf_wave_ceiling > 0) ? ho.seaf_wave_ceiling : (cw ? 180 : 24);
    HeavyApplyHostMode(eff_seaf, &c, &sup, &iv, &sq, &ex);
    if (c != g_constant || sup != g_support_squads || iv != g_interval || sq != g_squads ||
        ex != g_extra_squads || wc != g_wave_ceiling || cw != g_clone_wars) {
        g_constant = c;
        g_support_squads = sup;
        g_interval = iv;
        g_squads = sq;
        g_extra_squads = ex;
        g_wave_ceiling = wc;
        g_clone_wars = cw;
        BridgeWrite();
        if (g_hf && g_self) {
            g_hf->log(g_self,
                "Host options (%s): heavySeaf=%s -> constant=%d support_squads=%d interval=%ds squads_per_wave=%d extra_squads=%d ceiling=%d",
                is_poll ? "reloaded" : "startup", cd_heavy_seaf_name(eff_seaf),
                g_constant, g_support_squads, g_interval, g_squads, g_extra_squads, g_wave_ceiling);
        }
    }
}

static void ApplyNumber(int which, int value, const char* from)
{
    if (which == 3) {
        if (value < 10 || value > 900 || value == g_interval) return;
        g_interval = value;
        Save();
        g_hf->log(g_self, "Seconds between waves %d (%s).", g_interval, from);
        return;
    }
    if (value < 1 || value > 6 || value == g_squads) return;
    g_squads = value;
    Save();
    g_hf->log(g_self, "Squads per wave %d (%s).", g_squads, from);
}

static void Apply(int which, int on, const char* from)
{
    if (which == 2) {
        if (on == g_tank_hunters) return;
        g_tank_hunters = on ? 1 : 0;
        Save();
        g_hf->log(g_self, "Tank Hunter heavy targeting %s (%s). %s",
            g_tank_hunters ? "on" : "off", from,
            g_tank_hunters ? "Steering is not active on this build yet: the switch changes nothing."
                           : "Tank Hunter squads target like any other SEAF squad.");
        return;
    }
    if (which == 1) {
        if (on == g_support_squads) return;
        g_support_squads = on ? 1 : 0;
        Save();
        g_hf->log(g_self, "SEAF Squad Stratagems wave pool %s (%s). %s",
            g_support_squads ? "on" : "off", from,
            g_support_squads ? "When its mod is loaded, the next wave can call one of its four squads."
                             : "Waves use the game's own SEAF drop; its roster already carries the stealth, riot and tank squads.");
        return;
    }
    if (on == g_constant) return;
    g_constant = on ? 1 : 0;
    Save();
    g_hf->log(g_self, "Constant drops %s (%s). %s",
        g_constant ? "on" : "off", from,
        g_constant ? "SEAF lands on the timer after mission ground entry (+15s grace)."
                   : "SEAF lands from native patrols and the SEAF Reinforcement stratagem.");
}

typedef struct Arena {
    umm_arena base;
    void* blocks[64];
    int count;
} Arena;

static void* ArenaAlloc(umm_arena* self, size_t bytes)
{
    Arena* arena = (Arena*)self->impl;
    void* block;
    if (arena->count >= (int)(sizeof arena->blocks / sizeof arena->blocks[0])) return NULL;
    block = _aligned_malloc(bytes ? bytes : 1, 16);
    if (block == NULL) return NULL;
    memset(block, 0, bytes ? bytes : 1);
    arena->blocks[arena->count++] = block;
    return block;
}

static void ArenaInit(Arena* arena)
{
    arena->base.alloc = ArenaAlloc;
    arena->base.impl = arena;
    arena->count = 0;
}

static void ArenaFree(Arena* arena)
{
    int i;
    for (i = 0; i < arena->count; i++) _aligned_free(arena->blocks[i]);
    arena->count = 0;
}

static umm_value Str(const char* text)
{
    umm_value value;
    memset(&value, 0, sizeof value);
    value.type = UMM_STR;
    value.len = (uint32_t)strlen(text);
    value.u.s = text;
    return value;
}

static umm_value Bool(int on)
{
    umm_value value;
    memset(&value, 0, sizeof value);
    value.type = UMM_BOOL;
    value.u.b = on ? 1 : 0;
    return value;
}

static umm_value Num(double number)
{
    umm_value value;
    memset(&value, 0, sizeof value);
    value.type = UMM_NUM;
    value.u.n = number;
    return value;
}

static const umm_value* Field(const umm_value* table, const char* key)
{
    uint32_t i;
    if (table == NULL || table->type != UMM_TAB || table->u.t == NULL) return NULL;
    for (i = 0; i < table->u.t->count; i++) {
        const umm_pair* pair = &table->u.t->pairs[i];
        if (pair->key.type == UMM_STR && strcmp(pair->key.u.s, key) == 0) return &pair->value;
    }
    return NULL;
}

static const char g_owner = 0;

static void Keep(umm_callback* callback) { callback->refs++; }
static void Drop(umm_callback* callback) { if (callback->refs) callback->refs--; }

static int OnChange(umm_callback* callback, int argc, umm_value* argv, umm_arena* out, umm_value* rets, int maxrets, uint32_t flags)
{
    int row = (int)callback->user;
    (void)out;
    (void)rets;
    (void)maxrets;
    (void)flags;
    if (argc < 1) return 0;
    g_session_edit_mtime = MenuNowFileTime100ns();
    if (row >= 3) {
        if (argv[0].type != UMM_NUM) return 0;
        ApplyNumber(row, (int)(argv[0].u.n + 0.5), "MODS page");
        return 0;
    }
    if (argv[0].type != UMM_BOOL) return 0;
    Apply(row, argv[0].u.b != 0, "MODS page");
    return 0;
}

static umm_callback g_callback[5] = {
    { sizeof(umm_callback), 0, OnChange, Keep, Drop, &g_owner, 0 },
    { sizeof(umm_callback), 0, OnChange, Keep, Drop, &g_owner, 1 },
    { sizeof(umm_callback), 0, OnChange, Keep, Drop, &g_owner, 2 },
    { sizeof(umm_callback), 0, OnChange, Keep, Drop, &g_owner, 3 },
    { sizeof(umm_callback), 0, OnChange, Keep, Drop, &g_owner, 4 },
};

static int Call(const umm_value* fn, int argc, umm_value* argv, Arena* out, umm_value* rets)
{
    if (fn == NULL || fn->type != UMM_FN || fn->u.fn == NULL || fn->u.fn->call == NULL) return -2;
    return fn->u.fn->call(fn->u.fn, argc, argv, &out->base, rets, UMM_MAXRETS, 0);
}

static int Register(const umm_api* api)
{
    Arena snap;
    Arena out;
    umm_value menu;
    uint64_t instance = 0;
    const umm_value* api_table;
    const umm_value* reg;
    const umm_value* get;
    const umm_value* set_fn;
    const umm_value* on_change;
    static const char* ids[5] = { "constant_drops", "support_squads", "tank_hunters", "interval_seconds", "squads_per_wave" };
    static const char* labels[5] = { "CONSTANT DROPS", "SEAF SQUAD STRATAGEMS IN WAVES", "TANK HUNTER SQUADS GO FOR HEAVIES", "SECONDS BETWEEN WAVES", "SQUADS PER WAVE" };
    static const char* descriptions[5] = {
        "On: automatic SEAF waves land after mission ground entry (+15s grace) and again every interval_seconds while fewer than 24 SEAF are alive. Off (default): SEAF lands from native modifier patrols and SEAF Reinforcement.",
        "Requires SEAF Squad Stratagems. On: every squad in a wave is randomly Patrol, Stealth, Riot Control or Tank Hunter when that mod is loaded. Off: waves use the game's regular SEAF squad.",
        "Not active yet on this build: the squad's AI record is not found, so this switch currently changes nothing. Kept off until that is fixed.",
        "Time between automatic waves, 10 to 900 seconds. A new value applies from the next wave; no restart. Constant drops off makes this moot.",
        "Squads in each automatic wave, 1 to 3. Each squad is a full Pelican landing, so fewer squads per wave means shorter hitches."
    };
    static const int defaults[5] = { 0, 0, 0, 120, 1 };
    static int row_ready[5];
    umm_pair pairs[9];
    umm_table spec;
    umm_value args[2];
    umm_value rets[UMM_MAXRETS];
    int row;
    int count;
    int added;

    ArenaInit(&snap);
    if (!api->snapshot("ModOptionsMenu", &snap.base, &menu, &instance)) {
        ArenaFree(&snap);
        return 0;
    }
    api_table = Field(&menu, "api");
    if (api_table == NULL || api_table->type != UMM_TAB) api_table = &menu;
    reg = Field(api_table, "register_option");
    get = Field(api_table, "get");
    set_fn = Field(api_table, "set");
    on_change = Field(api_table, "on_change");
    if (reg == NULL || get == NULL || on_change == NULL) {
        ArenaFree(&snap);
        return 0;
    }

    for (row = 0; row < 5; row++) {
        if (row_ready[row]) continue;
        pairs[0].key = Str("mod");
        pairs[0].value = Str(MOD_NAME);
        pairs[1].key = Str("mod_id");
        pairs[1].value = Str(MOD_ID);
        pairs[2].key = Str("label");
        pairs[2].value = Str(labels[row]);
        pairs[3].key = Str("description");
        pairs[3].value = Str(descriptions[row]);
        pairs[4].key = Str("type");
        if (row < 3) {
            pairs[4].value = Str("toggle");
            pairs[5].key = Str("default");
            pairs[5].value = Bool(defaults[row]);
            spec.count = 6;
        } else {
            pairs[4].value = Str("slider");
            pairs[5].key = Str("default");
            pairs[5].value = Num(defaults[row]);
            pairs[6].key = Str("min");
            pairs[6].value = Num(row == 3 ? 10 : 1);
            pairs[7].key = Str("max");
            pairs[7].value = Num(row == 3 ? 900 : 3);
            pairs[8].key = Str("step");
            pairs[8].value = Num(row == 3 ? 5 : 1);
            spec.count = 9;
        }
        spec.flags = 0;
        spec.pairs = pairs;
        args[0] = Str(ids[row]);
        memset(&args[1], 0, sizeof args[1]);
        args[1].type = UMM_TAB;
        args[1].u.t = &spec;

        ArenaInit(&out);
        count = Call(reg, 2, args, &out, rets);
        added = count >= 1 && rets[0].type == UMM_BOOL && rets[0].u.b;
        if (!added) {
            g_hf->log(g_self, "MODS page refused %s (%s)", labels[row],
                count >= 2 && rets[1].type == UMM_STR ? rets[1].u.s : "no reason given");
            ArenaFree(&out);
            ArenaFree(&snap);
            return 1;
        }
        ArenaFree(&out);

        args[0] = Str(ids[row]);
        memset(&args[1], 0, sizeof args[1]);
        args[1].type = UMM_FN;
        args[1].u.fn = &g_callback[row];
        ArenaInit(&out);
        count = Call(on_change, 2, args, &out, rets);
        added = count >= 1 && rets[0].type == UMM_BOOL && rets[0].u.b;
        ArenaFree(&out);
        if (!added) {
            ArenaFree(&snap);
            return 0;
        }

        if (g_host_opts_loaded && set_fn != NULL) {
            int cur_val = (row == 0) ? g_constant : (row == 1) ? g_support_squads :
                          (row == 2) ? g_tank_hunters : (row == 3) ? g_interval : g_squads;
            args[0] = Str(ids[row]);
            args[1] = (row < 3) ? Bool(cur_val) : Num(cur_val);
            ArenaInit(&out);
            (void)Call(set_fn, 2, args, &out, rets);
            ArenaFree(&out);
        } else {
            args[0] = Str(ids[row]);
            ArenaInit(&out);
            count = Call(get, 1, args, &out, rets);
            if (count >= 1 && row < 3 && rets[0].type == UMM_BOOL) Apply(row, rets[0].u.b != 0, "MODS page");
            else if (count >= 1 && row >= 3 && rets[0].type == UMM_NUM) ApplyNumber(row, (int)(rets[0].u.n + 0.5), "MODS page");
            ArenaFree(&out);
        }
        row_ready[row] = 1;
    }
    ArenaFree(&snap);
    g_hf->log(g_self, "MODS page rows 5 of 5 ready (Universal Mod Menu %s)", api->build != NULL ? api->build : "");
    return 1;
}

void OptionsMenuStart(const HfApi* api, HfMod* mod)
{
    const char* folder;
    g_hf = api;
    g_self = mod;
    g_host_opts_path[0] = 0;
    g_host_opts_mtime = 0ULL;
    g_session_edit_mtime = 0ULL;
    g_host_opts_loaded = 0;
    Load();
    folder = g_hf ? g_hf->mod_folder(mod) : NULL;
    if (folder != NULL) {
        cd_resolve_host_options_path(folder, g_host_opts_path, sizeof g_host_opts_path);
        if (MenuIsHostOrSolo()) {
            LoadHostOptions(0);
        }
    }
    BridgeLocate();
    BridgeWrite();
    g_hf->log(mod, "Constant drops %s (every %d s, %d squads per wave, extra_squads=%d); SEAF Squad Stratagems pool %s; Tank Hunter heavy targeting %s.",
        g_constant ? "on" : "off", g_interval, g_squads, g_extra_squads,
        g_support_squads ? "on" : "off", g_tank_hunters ? "on" : "off");
}

void OptionsMenuPoll(float dt)
{
    static float wait;
    static int misses;
    const umm_api* api;
    wait -= dt;
    if (wait > 0.0f) return;
    wait = 1.0f;
    BridgePoll();
    LoadHostOptions(1);
    if (g_registered) return;
    api = umm_api_find();
    if (api == NULL) {
        if (++misses == 8)
            g_hf->log(g_self, "Universal Mod Menu not found. Using Lua bridge + clonedivers-host-options.json: constant=%d, support_squads=%d, tank_hunters=%d, interval_seconds=%d, squads_per_wave=%d.",
                g_constant ? 1 : 0, g_support_squads ? 1 : 0, g_tank_hunters ? 1 : 0, g_interval, g_squads);
        return;
    }
    g_registered = Register(api);
}

int OptionsConstant(void)
{
    return g_constant;
}

int OptionsSupportSquads(void)
{
    return g_support_squads;
}

int OptionsTankHunters(void)
{
    return g_tank_hunters;
}

int OptionsWaveIntervalMs(void)
{
    return g_interval * 1000;
}

int OptionsSquadsPerWave(void)
{
    return g_squads;
}

int OptionsExtraSquads(void)
{
    return g_extra_squads;
}

int OptionsWaveCeiling(void)
{
    return g_wave_ceiling > 0 ? g_wave_ceiling : 24;
}

int OptionsCloneWars(void)
{
    return g_clone_wars;
}
