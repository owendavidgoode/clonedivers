/* INI configuration parser and fail-closed bounds validation for empire-camera-pullback. */
#ifndef ECP_PULLBACK_OPTIONS_H
#define ECP_PULLBACK_OPTIONS_H

#include <ctype.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct EcpOptions {
    int enabled;
    int ship_walk_enabled;
    int allow_experimental_spring_arm; /* Fail-closed flag; reserved no-op in 0.1.1. */
    int walker_enabled;
    int lego_enabled;
    uint32_t toggle_vk;

    float blend_in_seconds;
    float wall_safety_margin;
    float fov_min;
    float walker_fov_min;
    float fov_max;
    float sights_distance_min;
    float on_foot_distance_max;

    float walker_pullback_distance;
    float walker_lift_z;
    float atst_scaled_pullback_distance;
    float atst_scaled_lift_z;

    float lego_pullback_distance;
    float lego_lift_z;
} EcpOptions;

static inline void ecp_options_defaults(EcpOptions* out)
{
    if (!out) return;
    memset(out, 0, sizeof(*out));
    out->enabled = 1;
    out->ship_walk_enabled = 0;
    out->allow_experimental_spring_arm = 0;
    out->walker_enabled = 1;
    out->lego_enabled = 1;
    out->toggle_vk = 0u;

    out->blend_in_seconds = 0.18f;
    out->wall_safety_margin = 0.28f;
    out->fov_min = 0.85f;
    out->walker_fov_min = 0.70f;
    out->fov_max = 1.65f;
    out->sights_distance_min = 0.55f;
    out->on_foot_distance_max = 5.50f;

    out->walker_pullback_distance = 2.80f;
    out->walker_lift_z = 1.65f;
    out->atst_scaled_pullback_distance = 4.10f;
    out->atst_scaled_lift_z = 3.35f;

    out->lego_pullback_distance = 0.25f;
    out->lego_lift_z = -0.06f;
}

static inline float ecp_clampf(float v, float lo, float hi, float fallback)
{
    if (!isfinite(v)) return fallback;
    if (v < lo) return lo;
    if (v > hi) return hi;
    return v;
}

static inline void ecp_options_sanitize(EcpOptions* opts)
{
    if (!opts) return;
    opts->enabled = opts->enabled ? 1 : 0;
    opts->ship_walk_enabled = opts->ship_walk_enabled ? 1 : 0;
    opts->allow_experimental_spring_arm = opts->allow_experimental_spring_arm ? 1 : 0;
    opts->walker_enabled = opts->walker_enabled ? 1 : 0;
    opts->lego_enabled = opts->lego_enabled ? 1 : 0;
    if (opts->toggle_vk > 0xFEu) opts->toggle_vk = 0u;

    opts->blend_in_seconds = ecp_clampf(opts->blend_in_seconds, 0.0f, 2.0f, 0.18f);
    opts->wall_safety_margin = ecp_clampf(opts->wall_safety_margin, 0.05f, 2.0f, 0.28f);
    opts->fov_min = ecp_clampf(opts->fov_min, 0.35f, 1.40f, 0.85f);
    opts->walker_fov_min = ecp_clampf(opts->walker_fov_min, 0.35f, 1.40f, 0.70f);
    opts->fov_max = ecp_clampf(opts->fov_max, 0.90f, 2.60f, 1.65f);
    if (opts->fov_max < opts->fov_min) opts->fov_max = opts->fov_min + 0.10f;
    opts->sights_distance_min = ecp_clampf(opts->sights_distance_min, 0.20f, 1.50f, 0.55f);
    opts->on_foot_distance_max = ecp_clampf(opts->on_foot_distance_max, 2.00f, 12.00f, 5.50f);

    opts->walker_pullback_distance = ecp_clampf(opts->walker_pullback_distance, -2.0f, 12.0f, 2.80f);
    opts->walker_lift_z = ecp_clampf(opts->walker_lift_z, -2.0f, 8.0f, 1.65f);
    opts->atst_scaled_pullback_distance = ecp_clampf(opts->atst_scaled_pullback_distance, -2.0f, 14.0f, 4.10f);
    opts->atst_scaled_lift_z = ecp_clampf(opts->atst_scaled_lift_z, -2.0f, 10.0f, 3.35f);

    opts->lego_pullback_distance = ecp_clampf(opts->lego_pullback_distance, -1.5f, 4.0f, 0.25f);
    opts->lego_lift_z = ecp_clampf(opts->lego_lift_z, -1.5f, 2.5f, -0.06f);
}

static inline char* ecp_trim(char* s)
{
    while (*s && isspace((unsigned char)*s)) s++;
    size_t len = strlen(s);
    while (len > 0 && isspace((unsigned char)s[len - 1])) {
        s[--len] = '\0';
    }
    return s;
}

static inline int ecp_ieq(const char* a, const char* b)
{
    while (*a && *b) {
        if (tolower((unsigned char)*a) != tolower((unsigned char)*b)) return 0;
        a++;
        b++;
    }
    return *a == '\0' && *b == '\0';
}

static inline int ecp_parse_bool(const char* val, int fallback)
{
    if (ecp_ieq(val, "1") || ecp_ieq(val, "true") || ecp_ieq(val, "yes") || ecp_ieq(val, "on")) return 1;
    if (ecp_ieq(val, "0") || ecp_ieq(val, "false") || ecp_ieq(val, "no") || ecp_ieq(val, "off")) return 0;
    return fallback;
}

static inline float ecp_parse_float(const char* val, float fallback)
{
    char* end = NULL;
    float v = strtof(val, &end);
    if (end == val || !isfinite(v)) return fallback;
    while (*end) {
        if (!isspace((unsigned char)*end)) return fallback;
        end++;
    }
    return v;
}

static inline uint32_t ecp_parse_u32(const char* val, uint32_t fallback)
{
    char* end = NULL;
    unsigned long v = strtoul(val, &end, 0);
    if (end == val) return fallback;
    while (*end) {
        if (!isspace((unsigned char)*end)) return fallback;
        end++;
    }
    return (uint32_t)v;
}

static inline void ecp_options_parse_text(EcpOptions* opts, const char* text)
{
    if (!opts || !text) return;
    if ((unsigned char)text[0] == 0xEFu && (unsigned char)text[1] == 0xBBu && (unsigned char)text[2] == 0xBFu) {
        text += 3;
    }
    char line[512];
    const char* p = text;
    while (*p) {
        size_t n = 0;
        while (p[n] && p[n] != '\n' && p[n] != '\r') n++;
        size_t copy = n < sizeof(line) - 1u ? n : sizeof(line) - 1u;
        memcpy(line, p, copy);
        line[copy] = '\0';
        p += n;
        while (*p == '\r' || *p == '\n') p++;

        for (size_t i = 0; line[i]; i++) {
            if (line[i] == ';' || line[i] == '#') {
                line[i] = '\0';
                break;
            }
        }
        char* s = ecp_trim(line);
        if (*s == '\0' || *s == '[') continue;
        char* eq = strchr(s, '=');
        if (!eq) continue;
        *eq = '\0';
        char* key = ecp_trim(s);
        char* val = ecp_trim(eq + 1);
        if (*key == '\0' || *val == '\0') continue;

        if (ecp_ieq(key, "enabled")) opts->enabled = ecp_parse_bool(val, opts->enabled);
        else if (ecp_ieq(key, "ship_walk_enabled")) opts->ship_walk_enabled = ecp_parse_bool(val, opts->ship_walk_enabled);
        else if (ecp_ieq(key, "allow_experimental_spring_arm"))
            opts->allow_experimental_spring_arm = ecp_parse_bool(val, opts->allow_experimental_spring_arm);
        else if (ecp_ieq(key, "walker_enabled")) opts->walker_enabled = ecp_parse_bool(val, opts->walker_enabled);
        else if (ecp_ieq(key, "lego_enabled")) opts->lego_enabled = ecp_parse_bool(val, opts->lego_enabled);
        else if (ecp_ieq(key, "toggle_vk") || ecp_ieq(key, "toggle_key"))
            opts->toggle_vk = ecp_parse_u32(val, opts->toggle_vk);
        else if (ecp_ieq(key, "blend_in_seconds")) opts->blend_in_seconds = ecp_parse_float(val, opts->blend_in_seconds);
        else if (ecp_ieq(key, "wall_safety_margin")) opts->wall_safety_margin = ecp_parse_float(val, opts->wall_safety_margin);
        else if (ecp_ieq(key, "fov_min")) opts->fov_min = ecp_parse_float(val, opts->fov_min);
        else if (ecp_ieq(key, "walker_fov_min")) opts->walker_fov_min = ecp_parse_float(val, opts->walker_fov_min);
        else if (ecp_ieq(key, "fov_max")) opts->fov_max = ecp_parse_float(val, opts->fov_max);
        else if (ecp_ieq(key, "sights_distance_min")) opts->sights_distance_min = ecp_parse_float(val, opts->sights_distance_min);
        else if (ecp_ieq(key, "on_foot_distance_max")) opts->on_foot_distance_max = ecp_parse_float(val, opts->on_foot_distance_max);
        else if (ecp_ieq(key, "walker_pullback_distance"))
            opts->walker_pullback_distance = ecp_parse_float(val, opts->walker_pullback_distance);
        else if (ecp_ieq(key, "walker_lift_z")) opts->walker_lift_z = ecp_parse_float(val, opts->walker_lift_z);
        else if (ecp_ieq(key, "atst_scaled_pullback_distance"))
            opts->atst_scaled_pullback_distance = ecp_parse_float(val, opts->atst_scaled_pullback_distance);
        else if (ecp_ieq(key, "atst_scaled_lift_z"))
            opts->atst_scaled_lift_z = ecp_parse_float(val, opts->atst_scaled_lift_z);
        else if (ecp_ieq(key, "lego_pullback_distance"))
            opts->lego_pullback_distance = ecp_parse_float(val, opts->lego_pullback_distance);
        else if (ecp_ieq(key, "lego_lift_z")) opts->lego_lift_z = ecp_parse_float(val, opts->lego_lift_z);
    }
    ecp_options_sanitize(opts);
}

static inline int ecp_options_load_file(EcpOptions* opts, const char* path)
{
    ecp_options_defaults(opts);
    if (!path || !*path) return 0;
    FILE* f = fopen(path, "rb");
    if (!f) return 0;
    if (fseek(f, 0, SEEK_END) != 0) {
        fclose(f);
        return 0;
    }
    long sz = ftell(f);
    if (sz <= 0 || sz > 65536L || fseek(f, 0, SEEK_SET) != 0) {
        fclose(f);
        return 0;
    }
    char* buf = (char*)malloc((size_t)sz + 1u);
    if (!buf) {
        fclose(f);
        return 0;
    }
    size_t got = fread(buf, 1u, (size_t)sz, f);
    fclose(f);
    buf[got] = '\0';
    ecp_options_parse_text(opts, buf);
    free(buf);
    return 1;
}

#endif /* ECP_PULLBACK_OPTIONS_H */
