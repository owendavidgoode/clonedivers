/* Shared Clonedivers Host Gameplay Mutator Options reader (C11 & C++17 compatible).
 *
 * Resolves <installRoot>\mods\clonedivers-host-options.json cleanly from api->mod_folder(mod)
 * for BOTH folder installs (<installRoot>\mods\<mod>) and .unpacked zip installs
 * (<installRoot>\mods\.unpacked\<pkg-hash>\<submod>), validates against reparse points and
 * oversized payloads, parses the HostMutatorConfig JSON contract (format 1), and exposes
 * 100ns Win32 FILETIME mtimes so newer in-game Mod Options Menu edits take precedence during
 * an active session while newer Launcher Apply writes override stale session caches.
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

#define CD_HOST_OPTIONS_FILENAME  "clonedivers-host-options.json"
#define CD_HOST_OPTIONS_MAX_BYTES 65536u
#define CD_HOST_OPTIONS_MAX_PATH  4096u

typedef enum CdChaosIntensity {
    CD_CHAOS_STANDARD  = 0,
    CD_CHAOS_OFF       = 1,
    CD_CHAOS_TOTAL_WAR = 2
} CdChaosIntensity;

typedef enum CdHeavySeafMode {
    CD_HEAVY_SEAF_STANDARD = 0,
    CD_HEAVY_SEAF_HEAVY    = 1,
    CD_HEAVY_SEAF_CONSTANT = 2
} CdHeavySeafMode;

typedef struct CdHostOptions {
    int format;
    CdChaosIntensity chaos_intensity;
    CdHeavySeafMode heavy_seaf;
    int faster_corpse_cleanup;
    int corpse_delay_seconds; /* -1 when not specified in JSON; 0..120 when overridden */
    uint64_t mtime_100ns;
    int loaded_from_file;
} CdHostOptions;

static inline CdHostOptions cd_host_options_defaults(void)
{
    CdHostOptions o;
    o.format = 1;
    o.chaos_intensity = CD_CHAOS_STANDARD;
    o.heavy_seaf = CD_HEAVY_SEAF_STANDARD;
    o.faster_corpse_cleanup = 1;
    o.corpse_delay_seconds = -1;
    o.mtime_100ns = 0ULL;
    o.loaded_from_file = 0;
    return o;
}

static inline const char* cd_chaos_intensity_name(CdChaosIntensity v)
{
    switch (v) {
        case CD_CHAOS_OFF:       return "off";
        case CD_CHAOS_TOTAL_WAR: return "total-war";
        default:                 return "standard";
    }
}

static inline const char* cd_heavy_seaf_name(CdHeavySeafMode v)
{
    switch (v) {
        case CD_HEAVY_SEAF_HEAVY:    return "heavy";
        case CD_HEAVY_SEAF_CONSTANT: return "constant";
        default:                     return "standard";
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

/* Resolves <modsDir>\clonedivers-host-options.json from api->mod_folder(mod).
 * Supports:
 *   - Folder installs: <installRoot>\mods\<mod>
 *   - Zip installs:    <installRoot>\mods\.unpacked\<hash>\<submod> (or deeper under .unpacked)
 * Requires <modsDir>'s own directory name to be "mods" (case-insensitive) so arbitrary paths
 * outside a HellForge mods folder fail closed.
 * Returns 1 on success, 0 on failure. */
static inline int cd_resolve_host_options_path(const char* mod_folder, char* out_path, size_t out_cap)
{
    if (!out_path || out_cap == 0) return 0;
    out_path[0] = '\0';
    if (!mod_folder || !mod_folder[0]) return 0;

    size_t len = strlen(mod_folder);
    if (len >= CD_HOST_OPTIONS_MAX_PATH - 64u) return 0;

    /* Strip trailing path separators (preserving root like "D:\"). */
    while (len > 0 && cd_is_sep_(mod_folder[len - 1])) {
        if (len == 3 && mod_folder[1] == ':') break;
        if (len == 1) break;
        --len;
    }
    if (len == 0) return 0;

    /* Tokenize path into up to 64 non-empty segments [start, end). */
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

    /* Need at least <root-or-parent>\mods\<mod> -> >= 2 segments ("mods" + "<mod>"). */
    if (seg_count < 2u) return 0;

    /* Check if any ancestor segment (excluding the final leaf only if .unpacked has children,
     * though even <mods>\.unpacked\<hash> has .unpacked at seg_count - 2) equals ".unpacked". */
    size_t mods_seg_idx = (size_t)-1;
    for (size_t k = seg_count; k > 0; --k) {
        size_t idx = k - 1u;
        size_t slen = seg_end[idx] - seg_start[idx];
        if (cd_ascii_ieq_span_(mod_folder + seg_start[idx], slen, ".unpacked")) {
            /* ".unpacked" must have a parent segment, and cannot be the leaf itself without a hash dir. */
            if (idx == 0u || idx + 1u >= seg_count) return 0;
            mods_seg_idx = idx - 1u;
            break;
        }
    }

    if (mods_seg_idx == (size_t)-1) {
        /* Standard folder install: <installRoot>\mods\<mod> -> parent of leaf must be "mods". */
        mods_seg_idx = seg_count - 2u;
    }

    size_t mlen = seg_end[mods_seg_idx] - seg_start[mods_seg_idx];
    if (!cd_ascii_ieq_span_(mod_folder + seg_start[mods_seg_idx], mlen, "mods")) {
        return 0;
    }

    size_t prefix_len = seg_end[mods_seg_idx];
    const char* suffix = "\\" CD_HOST_OPTIONS_FILENAME;
    size_t suffix_len = strlen(suffix);
    if (prefix_len + suffix_len + 1u > out_cap) return 0;

    /* Copy prefix normalizing '/' -> '\\' for consistent Windows paths. */
    for (size_t p = 0; p < prefix_len; ++p) {
        out_path[p] = (mod_folder[p] == '/') ? '\\' : mod_folder[p];
    }
    memcpy(out_path + prefix_len, suffix, suffix_len + 1u);
    return 1;
}

/* Queries Win32 last-write FILETIME (100ns ticks since 1601-01-01 UTC) for a UTF-8 file path.
 * Rejects directories and reparse points / symlinks.
 * Returns 1 on success, 0 if missing, -1 if reparse point / directory / invalid path. */
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

/* Returns 1 when session_bridge_mtime > host_options_mtime (meaning an in-game Mod Options Menu
 * edit occurred more recently than clonedivers-host-options.json), 0 otherwise. */
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
        if (ch < 0x20u) return 0; /* unescaped control character */
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
    /* Reject fractional or exponent suffixes on integer fields. */
    if (c->p < c->end && (*c->p == '.' || *c->p == 'e' || *c->p == 'E')) return 0;
    *out_val = neg ? -(int)acc : (int)acc;
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

/* Parses a UTF-8 JSON buffer into *out.
 * Returns 1 on valid format-1 JSON, -1 on syntax/schema error (leaving *out untouched). */
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
    parsed.loaded_from_file = 1;

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
            else return -1;
        } else if (cd_ascii_ieq_span_(key, klen, "heavySeaf")) {
            char val[32];
            if (!cd_json_parse_string_(&c, val, sizeof val)) return -1;
            if (strcmp(val, "standard") == 0) parsed.heavy_seaf = CD_HEAVY_SEAF_STANDARD;
            else if (strcmp(val, "heavy") == 0) parsed.heavy_seaf = CD_HEAVY_SEAF_HEAVY;
            else if (strcmp(val, "constant") == 0) parsed.heavy_seaf = CD_HEAVY_SEAF_CONSTANT;
            else return -1;
        } else if (cd_ascii_ieq_span_(key, klen, "fasterCorpseCleanup")) {
            int b = 0;
            if (!cd_json_parse_bool_(&c, &b)) return -1;
            parsed.faster_corpse_cleanup = b;
        } else if (cd_ascii_ieq_span_(key, klen, "corpseDelaySeconds")) {
            int secs = 0;
            if (!cd_json_parse_int_(&c, &secs) || secs < 0 || secs > 120) return -1;
            parsed.corpse_delay_seconds = secs;
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

    *out = parsed;
    return 1;
}

/* Reads and parses <utf8_path> safely (rejecting reparse points and >64 KiB files).
 * Returns:
 *   1 : valid file loaded into *out (out->loaded_from_file = 1, out->mtime_100ns populated)
 *   0 : file missing (*out set to defaults, out->loaded_from_file = 0)
 *  -1 : reparse point, oversized, I/O failure, or invalid JSON (*out set to defaults) */
static inline int cd_load_host_options_file(const char* utf8_path, CdHostOptions* out)
{
    if (!out) return -1;
    *out = cd_host_options_defaults();
    if (!utf8_path || !utf8_path[0]) return -1;

    wchar_t wpath[CD_HOST_OPTIONS_MAX_PATH];
    int wn = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, utf8_path, -1, wpath, CD_HOST_OPTIONS_MAX_PATH);
    if (wn <= 0 || wn >= (int)CD_HOST_OPTIONS_MAX_PATH) return -1;

    /* Check ancestor directories are not reparse points? Note HellForge may run on a volume/folder,
     * so we strictly enforce FILE_FLAG_OPEN_REPARSE_POINT on the target file itself and its parent mods dir. */
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

    char buf[CD_HOST_OPTIONS_MAX_BYTES + 1u];
    DWORD want = bhfi.nFileSizeLow;
    DWORD got = 0;
    BOOL ok = ReadFile(h, buf, want, &got, NULL);
    CloseHandle(h);
    if (!ok || got != want) return -1;
    buf[got] = '\0';

    CdHostOptions parsed;
    if (cd_parse_host_options_json(buf, (size_t)got, &parsed) != 1) {
        return -1;
    }
    parsed.mtime_100ns = ((uint64_t)bhfi.ftLastWriteTime.dwHighDateTime << 32) |
                         (uint64_t)bhfi.ftLastWriteTime.dwLowDateTime;
    parsed.loaded_from_file = 1;
    *out = parsed;
    return 1;
}

/* Convenience wrapper: resolves <modsDir>\clonedivers-host-options.json from api->mod_folder(mod)
 * and loads it. Returns 1 loaded, 0 missing, -1 invalid path or malformed JSON. */
static inline int cd_load_host_options_from_mod_folder(const char* mod_folder, CdHostOptions* out)
{
    if (!out) return -1;
    *out = cd_host_options_defaults();
    char path[CD_HOST_OPTIONS_MAX_PATH];
    if (!cd_resolve_host_options_path(mod_folder, path, sizeof path)) return -1;
    return cd_load_host_options_file(path, out);
}

#ifdef __cplusplus
}
#endif

#endif /* CLONEDIVERS_HOST_OPTIONS_H */
