/* Shared Clonedivers Host Gameplay Mutator Options reader (C11 & C++17 compatible).
 *
 * Resolves <installRoot>\mods\clonedivers-host-options.json cleanly from api->mod_folder(mod)
 * for BOTH folder installs (<installRoot>\mods\<mod>) and .unpacked zip installs
 * (<installRoot>\mods\.unpacked\<pkg-hash>\<submod>), validates against reparse points and
 * oversized payloads, parses the HostMutatorConfig JSON contract (format 1, including additive
 * Level 12 ULTRAKILL & Clone Wars theater fields), and exposes 100ns Win32 FILETIME mtimes so
 * newer in-game Mod Options Menu edits take precedence during an active session while newer
 * Launcher Apply writes override stale session caches.
 */
#ifndef CLONEDIVERS_HOST_OPTIONS_H
#define CLONEDIVERS_HOST_OPTIONS_H

#include <ctype.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <windows.h>

#ifdef __cplusplus
extern "C" {
#endif

#define CD_HOST_OPTIONS_FILENAME   "clonedivers-host-options.json"
#define CD_HELLFORGE_MODS_FILENAME "hellforge-mods.json"
#define CD_HOST_OPTIONS_MAX_BYTES  65536u
#define CD_HELLFORGE_MODS_MAX_BYTES 262144u
#define CD_HOST_OPTIONS_MAX_PATH   4096u

typedef enum CdChaosIntensity {
    CD_CHAOS_STANDARD   = 0,
    CD_CHAOS_OFF        = 1,
    CD_CHAOS_TOTAL_WAR  = 2,
    CD_CHAOS_ULTRAKILL  = 3,
    CD_CHAOS_CLONE_WARS = 4
} CdChaosIntensity;

typedef enum CdHeavySeafMode {
    CD_HEAVY_SEAF_STANDARD   = 0,
    CD_HEAVY_SEAF_HEAVY      = 1,
    CD_HEAVY_SEAF_CONSTANT   = 2,
    CD_HEAVY_SEAF_CLONE_WARS = 3
} CdHeavySeafMode;

typedef enum CdTheaterPreset {
    CD_THEATER_STANDARD          = 0,
    CD_THEATER_LEVEL12_ULTRAKILL = 1,
    CD_THEATER_CLONE_WARS        = 2
} CdTheaterPreset;

typedef enum CdActiveMode {
    CD_MODE_AUTO    = 0,
    CD_MODE_CLONES  = 1,
    CD_MODE_EMPIRE  = 2,
    CD_MODE_REGULAR = 3
} CdActiveMode;

typedef struct CdHostOptions {
    int format;
    CdChaosIntensity chaos_intensity;
    CdHeavySeafMode heavy_seaf;
    CdTheaterPreset theater_preset;
    CdActiveMode active_mode;
    int faster_corpse_cleanup;
    int corpse_delay_seconds;    /* -1 when omitted; 0..120 when overridden */
    int corpse_turbo_reaper;     /* 1 when explicit or theater_preset != standard */
    int target_active_units;     /* -1 when omitted; 12..8000 when overridden */
    int virtual_theater_budget;  /* -1 when omitted; 12..20000 when overridden */
    int spawn_radius_min_m;      /* -1 when omitted; 28..400 when overridden */
    int spawn_radius_max_m;      /* -1 when omitted; 45..950 when overridden */
    int seaf_wave_ceiling;       /* -1 when omitted; 6..600 when overridden */
    int map_radius_permille;     /* -1 when omitted; 380..950 when overridden (920 = 0.92f = 942m) */
    uint64_t mtime_100ns;
    int loaded_from_file;
} CdHostOptions;

static inline CdHostOptions cd_host_options_defaults(void)
{
    CdHostOptions o;
    o.format = 1;
    o.chaos_intensity = CD_CHAOS_STANDARD;
    o.heavy_seaf = CD_HEAVY_SEAF_STANDARD;
    o.theater_preset = CD_THEATER_CLONE_WARS;
    o.active_mode = CD_MODE_AUTO;
    o.faster_corpse_cleanup = 1;
    o.corpse_delay_seconds = -1;
    o.corpse_turbo_reaper = 1;
    o.target_active_units = -1;
    o.virtual_theater_budget = -1;
    o.spawn_radius_min_m = -1;
    o.spawn_radius_max_m = -1;
    o.seaf_wave_ceiling = -1;
    o.map_radius_permille = -1;
    o.mtime_100ns = 0ULL;
    o.loaded_from_file = 0;
    return o;
}

#define CD_LG_CONTEXT_RVA     0x3326340u
#define CD_LG_CONTEXT_OFF     0xAE288u
#define CD_LG_DIFFICULTY_OFF  0xF9u

typedef int (*CdMemReadFn)(uintptr_t address, void* buffer, size_t size);

/* Reads live mission difficulty (1..15) from [[game.dll+0x3326340]+0xAE288]+0xF9, or 0 outside a level. */
static inline int cd_read_live_mission_difficulty(CdMemReadFn read_fn, uintptr_t game_base)
{
    if (!read_fn || game_base < 0x10000u) return 0;
    uint64_t ctx = 0ULL, lg = 0ULL;
    uint8_t diff = 0u;
    if (read_fn(game_base + CD_LG_CONTEXT_RVA, &ctx, sizeof ctx) != 0) return 0;
    if (ctx < 0x10000ULL || (ctx >> 32) > 0x7FFFULL) return 0;
    if (read_fn((uintptr_t)ctx + CD_LG_CONTEXT_OFF, &lg, sizeof lg) != 0) return 0;
    if (lg < 0x10000ULL || (lg >> 32) > 0x7FFFULL) return 0;
    if (read_fn((uintptr_t)lg + CD_LG_DIFFICULTY_OFF, &diff, sizeof diff) != 0) return 0;
    return (diff >= 1u && diff <= 15u) ? (int)diff : 0;
}

/* True when Clone Wars / ULTRAKILL theater overrides should run in a mission at `live_diff`:
 * - Difficulty 12+ (`live_diff >= 12`): ALWAYS active unless Chaos is explicitly turned off.
 * - Explicit "level12-ultrakill" / "ultrakill": active on every difficulty when forced.
 * - Default "clone-wars": active ONLY on Difficulty 12+ (`live_diff >= 12`), keeping D1..D11 normal! */
static inline int cd_host_options_active_for_difficulty(const CdHostOptions* o, int live_diff)
{
    if (!o) return (live_diff >= 12) ? 1 : 0;
    if (o->chaos_intensity == CD_CHAOS_OFF) return 0;
    if (live_diff >= 12) return 1;
    if (o->theater_preset == CD_THEATER_LEVEL12_ULTRAKILL || o->chaos_intensity == CD_CHAOS_ULTRAKILL)
        return 1;
    return 0;
}

static inline CdChaosIntensity cd_effective_chaos_for_difficulty(const CdHostOptions* o, int live_diff)
{
    if (!o) return (live_diff >= 12) ? CD_CHAOS_CLONE_WARS : CD_CHAOS_STANDARD;
    if (o->chaos_intensity == CD_CHAOS_OFF) return CD_CHAOS_OFF;
    if (cd_host_options_active_for_difficulty(o, live_diff)) {
        if (o->chaos_intensity == CD_CHAOS_CLONE_WARS || o->theater_preset == CD_THEATER_CLONE_WARS)
            return CD_CHAOS_CLONE_WARS;
        return CD_CHAOS_ULTRAKILL;
    }
    if (o->chaos_intensity == CD_CHAOS_TOTAL_WAR) return CD_CHAOS_TOTAL_WAR;
    return CD_CHAOS_STANDARD;
}

static inline CdHeavySeafMode cd_effective_heavy_seaf_for_difficulty(const CdHostOptions* o, int live_diff)
{
    if (!o) return (live_diff >= 12) ? CD_HEAVY_SEAF_CLONE_WARS : CD_HEAVY_SEAF_STANDARD;
    if (live_diff >= 12) return CD_HEAVY_SEAF_CLONE_WARS;
    if (o->theater_preset == CD_THEATER_LEVEL12_ULTRAKILL) {
        if (o->heavy_seaf == CD_HEAVY_SEAF_CLONE_WARS) return CD_HEAVY_SEAF_CLONE_WARS;
        return (o->heavy_seaf == CD_HEAVY_SEAF_STANDARD) ? CD_HEAVY_SEAF_CONSTANT : o->heavy_seaf;
    }
    if (o->heavy_seaf == CD_HEAVY_SEAF_CLONE_WARS) return CD_HEAVY_SEAF_STANDARD;
    return o->heavy_seaf;
}

static inline int cd_host_options_is_ultrakill(const CdHostOptions* o)
{
    if (!o) return 0;
    return (o->theater_preset != CD_THEATER_STANDARD ||
            o->chaos_intensity == CD_CHAOS_ULTRAKILL ||
            o->chaos_intensity == CD_CHAOS_CLONE_WARS) ? 1 : 0;
}

static inline CdChaosIntensity cd_effective_chaos_intensity(const CdHostOptions* o)
{
    if (!o) return CD_CHAOS_STANDARD;
    if (o->chaos_intensity == CD_CHAOS_OFF) return CD_CHAOS_OFF;
    if (o->chaos_intensity == CD_CHAOS_CLONE_WARS || o->theater_preset == CD_THEATER_CLONE_WARS)
        return CD_CHAOS_CLONE_WARS;
    if (o->chaos_intensity == CD_CHAOS_ULTRAKILL || o->theater_preset == CD_THEATER_LEVEL12_ULTRAKILL)
        return CD_CHAOS_ULTRAKILL;
    return o->chaos_intensity;
}

static inline CdHeavySeafMode cd_effective_heavy_seaf(const CdHostOptions* o)
{
    if (!o) return CD_HEAVY_SEAF_STANDARD;
    if (o->heavy_seaf == CD_HEAVY_SEAF_CLONE_WARS) return CD_HEAVY_SEAF_CLONE_WARS;
    if (o->theater_preset != CD_THEATER_STANDARD && o->heavy_seaf == CD_HEAVY_SEAF_CONSTANT)
        return CD_HEAVY_SEAF_CLONE_WARS;
    return o->heavy_seaf;
}

static inline const char* cd_chaos_intensity_name(CdChaosIntensity v)
{
    switch (v) {
        case CD_CHAOS_OFF:        return "off";
        case CD_CHAOS_TOTAL_WAR:  return "total-war";
        case CD_CHAOS_ULTRAKILL:  return "ultrakill";
        case CD_CHAOS_CLONE_WARS: return "clone-wars";
        default:                  return "standard";
    }
}

static inline const char* cd_heavy_seaf_name(CdHeavySeafMode v)
{
    switch (v) {
        case CD_HEAVY_SEAF_HEAVY:      return "heavy";
        case CD_HEAVY_SEAF_CONSTANT:   return "constant";
        case CD_HEAVY_SEAF_CLONE_WARS: return "clone-wars";
        default:                       return "standard";
    }
}

static inline const char* cd_theater_preset_name(CdTheaterPreset v)
{
    switch (v) {
        case CD_THEATER_LEVEL12_ULTRAKILL: return "level12-ultrakill";
        case CD_THEATER_CLONE_WARS:        return "clone-wars";
        default:                           return "standard";
    }
}

static inline const char* cd_active_mode_name(CdActiveMode v)
{
    switch (v) {
        case CD_MODE_CLONES:  return "clonedivers";
        case CD_MODE_EMPIRE:  return "empire";
        case CD_MODE_REGULAR: return "regular";
        default:              return "auto";
    }
}

static inline int cd_ascii_ieq_span_(const char* a, size_t alen, const char* b)
{
    size_t blen = strlen(b);
    if (alen != blen) return 0;
    for (size_t i = 0; i < alen; ++i) {
        unsigned char ca = (unsigned char)a[i];
        unsigned char cb = (unsigned char)b[i];
        if (tolower(ca) != tolower(cb)) return 0;
    }
    return 1;
}

static inline int cd_is_sep_(char c)
{
    return c == '\\' || c == '/';
}

static inline int cd_resolve_mods_sibling_path(const char* mod_folder, const char* filename, char* out_path, size_t out_cap)
{
    if (!out_path || out_cap == 0) return 0;
    out_path[0] = '\0';
    if (!mod_folder || !mod_folder[0] || !filename || !filename[0]) return 0;

    size_t len = strlen(mod_folder);
    if (len >= CD_HOST_OPTIONS_MAX_PATH - 64u) return 0;

    while (len > 0 && cd_is_sep_(mod_folder[len - 1])) {
        if (len == 3 && mod_folder[1] == ':') break;
        if (len == 1) break;
        --len;
    }
    if (len == 0) return 0;

    size_t seg_start[64];
    size_t seg_end[64];
    size_t seg_count = 0;
    size_t i = 0;
    while (i < len) {
        while (i < len && cd_is_sep_(mod_folder[i])) ++i;
        if (i >= len) break;
        if (seg_count >= 64u) return 0;
        size_t s = i;
        while (i < len && !cd_is_sep_(mod_folder[i])) ++i;
        seg_start[seg_count] = s;
        seg_end[seg_count] = i;
        ++seg_count;
    }

    if (seg_count < 2u) return 0;

    size_t mods_seg_idx = (size_t)-1;
    for (size_t k = seg_count; k > 0; --k) {
        size_t idx = k - 1u;
        size_t slen = seg_end[idx] - seg_start[idx];
        if (cd_ascii_ieq_span_(mod_folder + seg_start[idx], slen, ".unpacked")) {
            if (idx == 0u || idx + 1u >= seg_count) return 0;
            mods_seg_idx = idx - 1u;
            break;
        }
    }

    if (mods_seg_idx == (size_t)-1) {
        mods_seg_idx = seg_count - 2u;
    }

    size_t mlen = seg_end[mods_seg_idx] - seg_start[mods_seg_idx];
    if (!cd_ascii_ieq_span_(mod_folder + seg_start[mods_seg_idx], mlen, "mods")) {
        return 0;
    }

    size_t prefix_len = seg_end[mods_seg_idx];
    size_t fn_len = strlen(filename);
    if (prefix_len + 1u + fn_len + 1u > out_cap) return 0;

    for (size_t p = 0; p < prefix_len; ++p) {
        out_path[p] = (mod_folder[p] == '/') ? '\\' : mod_folder[p];
    }
    out_path[prefix_len] = '\\';
    memcpy(out_path + prefix_len + 1u, filename, fn_len + 1u);
    return 1;
}

static inline int cd_resolve_host_options_path(const char* mod_folder, char* out_path, size_t out_cap)
{
    return cd_resolve_mods_sibling_path(mod_folder, CD_HOST_OPTIONS_FILENAME, out_path, out_cap);
}

static inline int cd_resolve_hellforge_mods_path(const char* mod_folder, char* out_path, size_t out_cap)
{
    return cd_resolve_mods_sibling_path(mod_folder, CD_HELLFORGE_MODS_FILENAME, out_path, out_cap);
}

static inline int cd_get_file_mtime_100ns(const char* utf8_path, uint64_t* mtime_out)
{
    if (mtime_out) *mtime_out = 0ULL;
    if (!utf8_path || !utf8_path[0]) return 0;

    wchar_t wpath[CD_HOST_OPTIONS_MAX_PATH];
    int wn = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, utf8_path, -1, wpath, CD_HOST_OPTIONS_MAX_PATH);
    if (wn <= 0 || wn >= (int)CD_HOST_OPTIONS_MAX_PATH) return -1;

    WIN32_FILE_ATTRIBUTE_DATA fad;
    if (!GetFileAttributesExW(wpath, GetFileExInfoStandard, &fad)) {
        DWORD err = GetLastError();
        if (err == ERROR_FILE_NOT_FOUND || err == ERROR_PATH_NOT_FOUND) return 0;
        return -1;
    }
    if ((fad.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
        (fad.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) {
        return -1;
    }
    if (mtime_out) {
        *mtime_out = ((uint64_t)fad.ftLastWriteTime.dwHighDateTime << 32) |
                     (uint64_t)fad.ftLastWriteTime.dwLowDateTime;
    }
    return 1;
}

static inline int cd_session_override_is_newer(uint64_t host_options_mtime, uint64_t session_bridge_mtime)
{
    if (session_bridge_mtime == 0ULL) return 0;
    if (host_options_mtime == 0ULL) return 1;
    return session_bridge_mtime > host_options_mtime ? 1 : 0;
}

/* ---- Minimal strict RFC-8259 JSON parser for CdHostOptions ---- */

typedef struct CdJsonCursor_ {
    const char* p;
    const char* end;
} CdJsonCursor_;

static inline void cd_json_skip_ws_(CdJsonCursor_* c)
{
    while (c->p < c->end) {
        char ch = *c->p;
        if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n') ++c->p;
        else break;
    }
}

static inline int cd_json_parse_null_(CdJsonCursor_* c)
{
    cd_json_skip_ws_(c);
    if ((size_t)(c->end - c->p) >= 4u && memcmp(c->p, "null", 4) == 0) {
        c->p += 4;
        return 1;
    }
    return 0;
}

static inline int cd_json_parse_string_(CdJsonCursor_* c, char* out, size_t out_cap)
{
    cd_json_skip_ws_(c);
    if (c->p >= c->end || *c->p != '"') return 0;
    ++c->p;
    size_t used = 0;
    while (c->p < c->end) {
        unsigned char ch = (unsigned char)*c->p++;
        if (ch == '"') {
            if (out && out_cap > 0) out[used < out_cap ? used : (out_cap - 1u)] = '\0';
            return 1;
        }
        if (ch < 0x20u) return 0;
        if (ch == '\\') {
            if (c->p >= c->end) return 0;
            unsigned char esc = (unsigned char)*c->p++;
            char mapped = 0;
            switch (esc) {
                case '"':  mapped = '"'; break;
                case '\\': mapped = '\\'; break;
                case '/':  mapped = '/'; break;
                case 'b':  mapped = '\b'; break;
                case 'f':  mapped = '\f'; break;
                case 'n':  mapped = '\n'; break;
                case 'r':  mapped = '\r'; break;
                case 't':  mapped = '\t'; break;
                case 'u': {
                    if ((size_t)(c->end - c->p) < 4u) return 0;
                    uint32_t code = 0;
                    for (int k = 0; k < 4; ++k) {
                        unsigned char hc = (unsigned char)c->p[k];
                        code <<= 4;
                        if (hc >= '0' && hc <= '9') code |= (uint32_t)(hc - '0');
                        else if (hc >= 'a' && hc <= 'f') code |= (uint32_t)(hc - 'a' + 10);
                        else if (hc >= 'A' && hc <= 'F') code |= (uint32_t)(hc - 'A' + 10);
                        else return 0;
                    }
                    c->p += 4;
                    mapped = (code < 0x80u && code != 0u) ? (char)code : '?';
                    break;
                }
                default: return 0;
            }
            if (out && used + 1u < out_cap) out[used] = mapped;
            ++used;
        } else {
            if (out && used + 1u < out_cap) out[used] = (char)ch;
            ++used;
        }
    }
    return 0;
}

static inline int cd_json_parse_int_(CdJsonCursor_* c, int* out_val)
{
    cd_json_skip_ws_(c);
    if (c->p >= c->end) return 0;
    int neg = 0;
    if (*c->p == '-') { neg = 1; ++c->p; }
    if (c->p >= c->end || *c->p < '0' || *c->p > '9') return 0;
    if (*c->p == '0' && (c->p + 1 < c->end) && c->p[1] >= '0' && c->p[1] <= '9') return 0;
    int64_t acc = 0;
    while (c->p < c->end && *c->p >= '0' && *c->p <= '9') {
        acc = acc * 10 + (*c->p - '0');
        if (acc > 0x7FFFFFFFLL) return 0;
        ++c->p;
    }
    if (c->p < c->end && (*c->p == '.' || *c->p == 'e' || *c->p == 'E')) return 0;
    *out_val = neg ? -(int)acc : (int)acc;
    return 1;
}

static inline int cd_json_parse_opt_bounded_int_(CdJsonCursor_* c, int lo, int hi, int* out_val)
{
    if (cd_json_parse_null_(c)) return 1;
    int v = 0;
    if (!cd_json_parse_int_(c, &v) || v < lo || v > hi) return 0;
    *out_val = v;
    return 1;
}

static inline int cd_json_parse_bool_(CdJsonCursor_* c, int* out_bool)
{
    cd_json_skip_ws_(c);
    if ((size_t)(c->end - c->p) >= 4u && memcmp(c->p, "true", 4) == 0) {
        c->p += 4;
        *out_bool = 1;
        return 1;
    }
    if ((size_t)(c->end - c->p) >= 5u && memcmp(c->p, "false", 5) == 0) {
        c->p += 5;
        *out_bool = 0;
        return 1;
    }
    return 0;
}

static inline int cd_json_skip_value_(CdJsonCursor_* c, int depth)
{
    if (depth > 16) return 0;
    cd_json_skip_ws_(c);
    if (c->p >= c->end) return 0;
    char ch = *c->p;
    if (ch == '"') {
        return cd_json_parse_string_(c, NULL, 0);
    }
    if (ch == '{') {
        ++c->p;
        cd_json_skip_ws_(c);
        if (c->p < c->end && *c->p == '}') { ++c->p; return 1; }
        for (;;) {
            if (!cd_json_parse_string_(c, NULL, 0)) return 0;
            cd_json_skip_ws_(c);
            if (c->p >= c->end || *c->p != ':') return 0;
            ++c->p;
            if (!cd_json_skip_value_(c, depth + 1)) return 0;
            cd_json_skip_ws_(c);
            if (c->p >= c->end) return 0;
            if (*c->p == '}') { ++c->p; return 1; }
            if (*c->p != ',') return 0;
            ++c->p;
        }
    }
    if (ch == '[') {
        ++c->p;
        cd_json_skip_ws_(c);
        if (c->p < c->end && *c->p == ']') { ++c->p; return 1; }
        for (;;) {
            if (!cd_json_skip_value_(c, depth + 1)) return 0;
            cd_json_skip_ws_(c);
            if (c->p >= c->end) return 0;
            if (*c->p == ']') { ++c->p; return 1; }
            if (*c->p != ',') return 0;
            ++c->p;
        }
    }
    if (ch == 't') {
        if ((size_t)(c->end - c->p) < 4u || memcmp(c->p, "true", 4) != 0) return 0;
        c->p += 4;
        return 1;
    }
    if (ch == 'f') {
        if ((size_t)(c->end - c->p) < 5u || memcmp(c->p, "false", 5) != 0) return 0;
        c->p += 5;
        return 1;
    }
    if (ch == 'n') {
        if ((size_t)(c->end - c->p) < 4u || memcmp(c->p, "null", 4) != 0) return 0;
        c->p += 4;
        return 1;
    }
    if (ch == '-' || (ch >= '0' && ch <= '9')) {
        if (*c->p == '-') ++c->p;
        if (c->p >= c->end || *c->p < '0' || *c->p > '9') return 0;
        if (*c->p == '0') ++c->p;
        else while (c->p < c->end && *c->p >= '0' && *c->p <= '9') ++c->p;
        if (c->p < c->end && *c->p == '.') {
            ++c->p;
            if (c->p >= c->end || *c->p < '0' || *c->p > '9') return 0;
            while (c->p < c->end && *c->p >= '0' && *c->p <= '9') ++c->p;
        }
        if (c->p < c->end && (*c->p == 'e' || *c->p == 'E')) {
            ++c->p;
            if (c->p < c->end && (*c->p == '+' || *c->p == '-')) ++c->p;
            if (c->p >= c->end || *c->p < '0' || *c->p > '9') return 0;
            while (c->p < c->end && *c->p >= '0' && *c->p <= '9') ++c->p;
        }
        return 1;
    }
    return 0;
}

static inline int cd_parse_host_options_json(const char* text, size_t len, CdHostOptions* out)
{
    if (!text || !out || len == 0 || len > CD_HOST_OPTIONS_MAX_BYTES) return -1;

    const unsigned char* u = (const unsigned char*)text;
    size_t offset = 0;
    if (len >= 3u && u[0] == 0xEFu && u[1] == 0xBBu && u[2] == 0xBFu) {
        offset = 3u;
    }

    CdJsonCursor_ c;
    c.p = text + offset;
    c.end = text + len;

    cd_json_skip_ws_(&c);
    if (c.p >= c.end || *c.p != '{') return -1;
    ++c.p;

    CdHostOptions parsed = cd_host_options_defaults();
    parsed.theater_preset = CD_THEATER_STANDARD;
    parsed.corpse_turbo_reaper = 0;
    parsed.loaded_from_file = 1;
    int explicit_turbo = -1;

    cd_json_skip_ws_(&c);
    if (c.p < c.end && *c.p == '}') {
        ++c.p;
        cd_json_skip_ws_(&c);
        if (c.p != c.end) return -1;
        *out = parsed;
        return 1;
    }

    for (;;) {
        char key[64];
        if (!cd_json_parse_string_(&c, key, sizeof key)) return -1;
        size_t klen = strlen(key);
        cd_json_skip_ws_(&c);
        if (c.p >= c.end || *c.p != ':') return -1;
        ++c.p;

        if (cd_ascii_ieq_span_(key, klen, "format")) {
            int fmt = 0;
            if (!cd_json_parse_int_(&c, &fmt) || fmt != 1) return -1;
            parsed.format = fmt;
        } else if (cd_ascii_ieq_span_(key, klen, "chaosIntensity")) {
            char val[32];
            if (!cd_json_parse_string_(&c, val, sizeof val)) return -1;
            if (strcmp(val, "standard") == 0) parsed.chaos_intensity = CD_CHAOS_STANDARD;
            else if (strcmp(val, "off") == 0) parsed.chaos_intensity = CD_CHAOS_OFF;
            else if (strcmp(val, "total-war") == 0) parsed.chaos_intensity = CD_CHAOS_TOTAL_WAR;
            else if (strcmp(val, "ultrakill") == 0) parsed.chaos_intensity = CD_CHAOS_ULTRAKILL;
            else if (strcmp(val, "clone-wars") == 0) parsed.chaos_intensity = CD_CHAOS_CLONE_WARS;
            else return -1;
        } else if (cd_ascii_ieq_span_(key, klen, "heavySeaf")) {
            char val[32];
            if (!cd_json_parse_string_(&c, val, sizeof val)) return -1;
            if (strcmp(val, "standard") == 0) parsed.heavy_seaf = CD_HEAVY_SEAF_STANDARD;
            else if (strcmp(val, "heavy") == 0) parsed.heavy_seaf = CD_HEAVY_SEAF_HEAVY;
            else if (strcmp(val, "constant") == 0) parsed.heavy_seaf = CD_HEAVY_SEAF_CONSTANT;
            else if (strcmp(val, "clone-wars") == 0) parsed.heavy_seaf = CD_HEAVY_SEAF_CLONE_WARS;
            else return -1;
        } else if (cd_ascii_ieq_span_(key, klen, "theaterPreset")) {
            if (!cd_json_parse_null_(&c)) {
                char val[32];
                if (!cd_json_parse_string_(&c, val, sizeof val)) return -1;
                if (strcmp(val, "standard") == 0) parsed.theater_preset = CD_THEATER_STANDARD;
                else if (strcmp(val, "level12-ultrakill") == 0 || strcmp(val, "ultrakill") == 0)
                    parsed.theater_preset = CD_THEATER_LEVEL12_ULTRAKILL;
                else if (strcmp(val, "clone-wars") == 0)
                    parsed.theater_preset = CD_THEATER_CLONE_WARS;
                else return -1;
            }
        } else if (cd_ascii_ieq_span_(key, klen, "activeMode")) {
            if (!cd_json_parse_null_(&c)) {
                char val[32];
                if (!cd_json_parse_string_(&c, val, sizeof val)) return -1;
                if (strcmp(val, "empire") == 0) parsed.active_mode = CD_MODE_EMPIRE;
                else if (strcmp(val, "clonedivers") == 0 || strcmp(val, "commandos") == 0)
                    parsed.active_mode = CD_MODE_CLONES;
                else if (strcmp(val, "regular") == 0) parsed.active_mode = CD_MODE_REGULAR;
                else if (strcmp(val, "auto") == 0) parsed.active_mode = CD_MODE_AUTO;
                else return -1;
            }
        } else if (cd_ascii_ieq_span_(key, klen, "fasterCorpseCleanup")) {
            int b = 0;
            if (!cd_json_parse_bool_(&c, &b)) return -1;
            parsed.faster_corpse_cleanup = b;
        } else if (cd_ascii_ieq_span_(key, klen, "corpseDelaySeconds")) {
            if (!cd_json_parse_opt_bounded_int_(&c, 0, 120, &parsed.corpse_delay_seconds)) return -1;
        } else if (cd_ascii_ieq_span_(key, klen, "corpseTurboReaper")) {
            if (!cd_json_parse_null_(&c)) {
                int b = 0;
                if (!cd_json_parse_bool_(&c, &b)) return -1;
                explicit_turbo = b;
            }
        } else if (cd_ascii_ieq_span_(key, klen, "targetActiveUnits")) {
            if (!cd_json_parse_opt_bounded_int_(&c, 12, 8000, &parsed.target_active_units)) return -1;
        } else if (cd_ascii_ieq_span_(key, klen, "virtualTheaterBudget")) {
            if (!cd_json_parse_opt_bounded_int_(&c, 12, 20000, &parsed.virtual_theater_budget)) return -1;
        } else if (cd_ascii_ieq_span_(key, klen, "spawnRadiusMinM")) {
            if (!cd_json_parse_opt_bounded_int_(&c, 28, 400, &parsed.spawn_radius_min_m)) return -1;
        } else if (cd_ascii_ieq_span_(key, klen, "spawnRadiusMaxM")) {
            if (!cd_json_parse_opt_bounded_int_(&c, 45, 950, &parsed.spawn_radius_max_m)) return -1;
        } else if (cd_ascii_ieq_span_(key, klen, "seafWaveCeiling")) {
            if (!cd_json_parse_opt_bounded_int_(&c, 6, 600, &parsed.seaf_wave_ceiling)) return -1;
        } else if (cd_ascii_ieq_span_(key, klen, "mapRadiusPermille")) {
            if (!cd_json_parse_opt_bounded_int_(&c, 380, 950, &parsed.map_radius_permille)) return -1;
        } else {
            if (!cd_json_skip_value_(&c, 1)) return -1;
        }

        cd_json_skip_ws_(&c);
        if (c.p >= c.end) return -1;
        if (*c.p == '}') {
            ++c.p;
            break;
        }
        if (*c.p != ',') return -1;
        ++c.p;
    }

    cd_json_skip_ws_(&c);
    if (c.p != c.end) return -1;

    if (parsed.spawn_radius_min_m > 0 && parsed.spawn_radius_max_m > 0 &&
        parsed.spawn_radius_min_m > parsed.spawn_radius_max_m) {
        return -1;
    }

    if (explicit_turbo >= 0) {
        parsed.corpse_turbo_reaper = explicit_turbo;
    } else if (cd_host_options_is_ultrakill(&parsed)) {
        parsed.corpse_turbo_reaper = 1;
    }

    *out = parsed;
    return 1;
}

static inline int cd_load_host_options_file(const char* utf8_path, CdHostOptions* out)
{
    if (!out) return -1;
    *out = cd_host_options_defaults();
    if (!utf8_path || !utf8_path[0]) return -1;

    wchar_t wpath[CD_HOST_OPTIONS_MAX_PATH];
    int wn = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, utf8_path, -1, wpath, CD_HOST_OPTIONS_MAX_PATH);
    if (wn <= 0 || wn >= (int)CD_HOST_OPTIONS_MAX_PATH) return -1;

    WIN32_FILE_ATTRIBUTE_DATA fad;
    if (!GetFileAttributesExW(wpath, GetFileExInfoStandard, &fad)) {
        DWORD err = GetLastError();
        if (err == ERROR_FILE_NOT_FOUND || err == ERROR_PATH_NOT_FOUND) return 0;
        return -1;
    }
    if ((fad.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
        (fad.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) {
        return -1;
    }

    HANDLE h = CreateFileW(
        wpath,
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT,
        NULL);
    if (h == INVALID_HANDLE_VALUE) {
        DWORD err = GetLastError();
        if (err == ERROR_FILE_NOT_FOUND || err == ERROR_PATH_NOT_FOUND) return 0;
        return -1;
    }

    BY_HANDLE_FILE_INFORMATION bhfi;
    if (!GetFileInformationByHandle(h, &bhfi) ||
        (bhfi.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
        (bhfi.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0 ||
        bhfi.nFileSizeHigh != 0 ||
        bhfi.nFileSizeLow == 0 ||
        bhfi.nFileSizeLow > CD_HOST_OPTIONS_MAX_BYTES) {
        CloseHandle(h);
        return -1;
    }

    DWORD want = bhfi.nFileSizeLow;
    HANDLE heap = GetProcessHeap();
    char* buf = heap ? (char*)HeapAlloc(heap, 0, (SIZE_T)want + 1u) : NULL;
    if (!buf) {
        CloseHandle(h);
        return -1;
    }
    DWORD got = 0;
    BOOL ok = ReadFile(h, buf, want, &got, NULL);
    CloseHandle(h);
    if (!ok || got != want) {
        HeapFree(heap, 0, buf);
        return -1;
    }
    buf[got] = '\0';

    CdHostOptions parsed;
    int parse_rc = cd_parse_host_options_json(buf, (size_t)got, &parsed);
    HeapFree(heap, 0, buf);
    if (parse_rc != 1) {
        return -1;
    }
    parsed.mtime_100ns = ((uint64_t)bhfi.ftLastWriteTime.dwHighDateTime << 32) |
                         (uint64_t)bhfi.ftLastWriteTime.dwLowDateTime;
    parsed.loaded_from_file = 1;
    *out = parsed;
    return 1;
}

static inline int cd_load_host_options_from_mod_folder(const char* mod_folder, CdHostOptions* out)
{
    if (!out) return -1;
    *out = cd_host_options_defaults();
    char path[CD_HOST_OPTIONS_MAX_PATH];
    if (!cd_resolve_host_options_path(mod_folder, path, sizeof path)) return -1;
    return cd_load_host_options_file(path, out);
}

static inline int cd_is_sw_overhaul_pack_id_(const char* s)
{
    if (!s) return 0;
    return strncmp(s, "pack.ef3458f5-f409-4f24-8933-", 29u) == 0 ||
           strncmp(s, "pack.b7b312b3-d764-4c7e-a287-", 29u) == 0;
}

static inline int cd_parse_sw_pack_array_(CdJsonCursor_* c, char out_ids[32][80], size_t* out_count)
{
    *out_count = 0u;
    if (cd_json_parse_null_(c)) return 1;
    cd_json_skip_ws_(c);
    if (c->p >= c->end || *c->p != '[') return 0;
    ++c->p;
    cd_json_skip_ws_(c);
    if (c->p < c->end && *c->p == ']') { ++c->p; return 1; }
    for (;;) {
        char item[160];
        if (!cd_json_parse_string_(c, item, sizeof item)) return 0;
        if (cd_is_sw_overhaul_pack_id_(item)) {
            if (*out_count >= 32u || strlen(item) >= 80u) return 0;
            memcpy(out_ids[*out_count], item, strlen(item) + 1u);
            ++(*out_count);
        }
        cd_json_skip_ws_(c);
        if (c->p >= c->end) return 0;
        if (*c->p == ']') { ++c->p; return 1; }
        if (*c->p != ',') return 0;
        ++c->p;
    }
}

/* Parses <installRoot>\mods\hellforge-mods.json to determine which Star Wars overhaul pack
 * is enabled in the active mod order (1 = resolved Empire/Clones/Regular, 0 = order omitted
 * or ambiguous, -1 = malformed JSON). */
static inline int cd_parse_hellforge_mods_mode(const char* text, size_t len, CdActiveMode* out)
{
    if (out) *out = CD_MODE_AUTO;
    if (!text || !out || len == 0 || len > CD_HELLFORGE_MODS_MAX_BYTES) return -1;

    const unsigned char* u = (const unsigned char*)text;
    size_t offset = 0;
    if (len >= 3u && u[0] == 0xEFu && u[1] == 0xBBu && u[2] == 0xBFu) {
        offset = 3u;
    }

    CdJsonCursor_ c;
    c.p = text + offset;
    c.end = text + len;

    cd_json_skip_ws_(&c);
    if (c.p >= c.end || *c.p != '{') return -1;
    ++c.p;

    char disabled_sw[32][80];
    char order_sw[32][80];
    size_t disabled_count = 0u;
    size_t order_count = 0u;
    int saw_order = 0;

    cd_json_skip_ws_(&c);
    if (c.p < c.end && *c.p == '}') {
        ++c.p;
        cd_json_skip_ws_(&c);
        return (c.p == c.end) ? 0 : -1;
    }

    for (;;) {
        char key[64];
        if (!cd_json_parse_string_(&c, key, sizeof key)) return -1;
        size_t klen = strlen(key);
        cd_json_skip_ws_(&c);
        if (c.p >= c.end || *c.p != ':') return -1;
        ++c.p;

        if (cd_ascii_ieq_span_(key, klen, "disabled")) {
            if (!cd_parse_sw_pack_array_(&c, disabled_sw, &disabled_count)) return -1;
        } else if (cd_ascii_ieq_span_(key, klen, "order")) {
            saw_order = 1;
            if (!cd_parse_sw_pack_array_(&c, order_sw, &order_count)) return -1;
        } else {
            if (!cd_json_skip_value_(&c, 1)) return -1;
        }

        cd_json_skip_ws_(&c);
        if (c.p >= c.end) return -1;
        if (*c.p == '}') {
            ++c.p;
            break;
        }
        if (*c.p != ',') return -1;
        ++c.p;
    }

    cd_json_skip_ws_(&c);
    if (c.p != c.end) return -1;
    if (!saw_order) return 0;

    int has_empire = 0;
    int has_clones = 0;
    for (size_t i = 0; i < order_count; ++i) {
        int is_disabled = 0;
        for (size_t d = 0; d < disabled_count; ++d) {
            if (strcmp(order_sw[i], disabled_sw[d]) == 0) {
                is_disabled = 1;
                break;
            }
        }
        if (is_disabled) continue;
        if (strncmp(order_sw[i], "pack.ef3458f5-f409-4f24-8933-", 29u) == 0) has_empire = 1;
        else if (strncmp(order_sw[i], "pack.b7b312b3-d764-4c7e-a287-", 29u) == 0) has_clones = 1;
    }

    if (has_empire && !has_clones) { *out = CD_MODE_EMPIRE; return 1; }
    if (has_clones && !has_empire) { *out = CD_MODE_CLONES; return 1; }
    if (!has_empire && !has_clones) { *out = CD_MODE_REGULAR; return 1; }
    return 0;
}

static inline int cd_detect_hellforge_mods_mode_file(const char* utf8_path, uint64_t* mtime_out, CdActiveMode* out)
{
    if (mtime_out) *mtime_out = 0ULL;
    if (out) *out = CD_MODE_AUTO;
    if (!utf8_path || !utf8_path[0] || !out) return -1;

    wchar_t wpath[CD_HOST_OPTIONS_MAX_PATH];
    int wn = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, utf8_path, -1, wpath, CD_HOST_OPTIONS_MAX_PATH);
    if (wn <= 0 || wn >= (int)CD_HOST_OPTIONS_MAX_PATH) return -1;

    WIN32_FILE_ATTRIBUTE_DATA fad;
    if (!GetFileAttributesExW(wpath, GetFileExInfoStandard, &fad)) {
        DWORD err = GetLastError();
        if (err == ERROR_FILE_NOT_FOUND || err == ERROR_PATH_NOT_FOUND) return 0;
        return -1;
    }
    if ((fad.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
        (fad.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) {
        return -1;
    }

    HANDLE h = CreateFileW(
        wpath,
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT,
        NULL);
    if (h == INVALID_HANDLE_VALUE) {
        DWORD err = GetLastError();
        if (err == ERROR_FILE_NOT_FOUND || err == ERROR_PATH_NOT_FOUND) return 0;
        return -1;
    }

    BY_HANDLE_FILE_INFORMATION bhfi;
    if (!GetFileInformationByHandle(h, &bhfi) ||
        (bhfi.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
        (bhfi.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0 ||
        bhfi.nFileSizeHigh != 0 ||
        bhfi.nFileSizeLow == 0 ||
        bhfi.nFileSizeLow > CD_HELLFORGE_MODS_MAX_BYTES) {
        CloseHandle(h);
        return -1;
    }

    DWORD want = bhfi.nFileSizeLow;
    HANDLE heap = GetProcessHeap();
    char* buf = heap ? (char*)HeapAlloc(heap, 0, (SIZE_T)want + 1u) : NULL;
    if (!buf) {
        CloseHandle(h);
        return -1;
    }
    DWORD got = 0;
    BOOL ok = ReadFile(h, buf, want, &got, NULL);
    CloseHandle(h);
    if (!ok || got != want) {
        HeapFree(heap, 0, buf);
        return -1;
    }
    buf[got] = '\0';

    CdActiveMode detected = CD_MODE_AUTO;
    int rc = cd_parse_hellforge_mods_mode(buf, (size_t)got, &detected);
    HeapFree(heap, 0, buf);
    if (rc < 0) return -1;

    if (mtime_out) {
        *mtime_out = ((uint64_t)bhfi.ftLastWriteTime.dwHighDateTime << 32) |
                     (uint64_t)bhfi.ftLastWriteTime.dwLowDateTime;
    }
    *out = detected;
    return rc;
}

#ifdef __cplusplus
}
#endif

#endif /* CLONEDIVERS_HOST_OPTIONS_H */
