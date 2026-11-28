/* Configuration parser, bounds clamping, and defaults for titanfall-walker-hud 0.1.0. */
#ifndef TFH_OPTIONS_H
#define TFH_OPTIONS_H

#include <ctype.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct TfhOptions {
    int enabled;
    int hud_enabled;
    int enable_on_atst;
    float hud_opacity;
    uint32_t hud_toggle_vk;

    int dual_view_enabled;
    uint32_t dual_view_vk;
    int start_in_first_person;
    float fp_forward_offset;
    float fp_up_offset;
    float wall_safety_margin;
} TfhOptions;

static inline void tfh_options_defaults(TfhOptions* o)
{
    if (!o) return;
    memset(o, 0, sizeof(*o));
    o->enabled = 1;
    o->hud_enabled = 1;
    o->enable_on_atst = 0;
    o->hud_opacity = 0.90f;
    o->hud_toggle_vk = 0u;

    o->dual_view_enabled = 1;
    o->dual_view_vk = 0x76u; /* VK_F7 (avoids VK_F8 HellForge live stats editor) */
    o->start_in_first_person = 0;
    o->fp_forward_offset = 2.35f;
    o->fp_up_offset = 2.85f;
    o->wall_safety_margin = 0.28f;
}

static inline float tfh_clampf(float v, float lo, float hi, float def_val)
{
    if (!isfinite(v)) return def_val;
    if (v < lo) return lo;
    if (v > hi) return hi;
    return v;
}

static inline int tfh_str_ieq(const char* a, const char* b)
{
    if (!a || !b) return 0;
    while (*a && *b) {
        if (tolower((unsigned char)*a) != tolower((unsigned char)*b)) return 0;
        a++;
        b++;
    }
    return *a == '\0' && *b == '\0';
}

static inline int tfh_parse_bool(const char* val, int fallback)
{
    if (!val || !*val) return fallback;
    if (tfh_str_ieq(val, "1") || tfh_str_ieq(val, "true") || tfh_str_ieq(val, "yes") || tfh_str_ieq(val, "on")) {
        return 1;
    }
    if (tfh_str_ieq(val, "0") || tfh_str_ieq(val, "false") || tfh_str_ieq(val, "no") || tfh_str_ieq(val, "off")) {
        return 0;
    }
    return fallback;
}

static inline void tfh_options_parse_text(TfhOptions* o, const char* text)
{
    if (!o || !text) return;
    /* Strip UTF-8 BOM if present */
    if ((unsigned char)text[0] == 0xEFu && (unsigned char)text[1] == 0xBBu && (unsigned char)text[2] == 0xBFu) {
        text += 3;
    }

    const char* p = text;
    while (*p) {
        const char* eol = p;
        while (*eol && *eol != '\n' && *eol != '\r') eol++;
        size_t len = (size_t)(eol - p);
        char line[512];
        if (len >= sizeof(line)) len = sizeof(line) - 1u;
        memcpy(line, p, len);
        line[len] = '\0';

        /* Strip comments (; or #) */
        for (size_t i = 0; line[i]; i++) {
            if (line[i] == ';' || line[i] == '#') {
                line[i] = '\0';
                break;
            }
        }

        char* s = line;
        while (*s && isspace((unsigned char)*s)) s++;
        if (*s != '\0' && *s != '[') {
            char* eq = strchr(s, '=');
            if (eq) {
                *eq = '\0';
                char* key = s;
                char* val = eq + 1;
                char* kend = eq - 1;
                while (kend >= key && isspace((unsigned char)*kend)) *kend-- = '\0';
                while (*val && isspace((unsigned char)*val)) val++;
                char* vend = val + strlen(val);
                while (vend > val && isspace((unsigned char)vend[-1])) *--vend = '\0';

                if (tfh_str_ieq(key, "enabled")) {
                    o->enabled = tfh_parse_bool(val, o->enabled);
                } else if (tfh_str_ieq(key, "hud_enabled")) {
                    o->hud_enabled = tfh_parse_bool(val, o->hud_enabled);
                } else if (tfh_str_ieq(key, "enable_on_atst")) {
                    o->enable_on_atst = tfh_parse_bool(val, o->enable_on_atst);
                } else if (tfh_str_ieq(key, "hud_opacity")) {
                    char* endp = NULL;
                    float fv = strtof(val, &endp);
                    if (endp != val) o->hud_opacity = tfh_clampf(fv, 0.15f, 1.00f, 0.90f);
                } else if (tfh_str_ieq(key, "hud_toggle_vk")) {
                    char* endp = NULL;
                    unsigned long vk = strtoul(val, &endp, 0);
                    if (endp != val && vk <= 0xFEu && vk != 0x77u) o->hud_toggle_vk = (uint32_t)vk;
                } else if (tfh_str_ieq(key, "dual_view_enabled")) {
                    o->dual_view_enabled = tfh_parse_bool(val, o->dual_view_enabled);
                } else if (tfh_str_ieq(key, "dual_view_vk")) {
                    char* endp = NULL;
                    unsigned long vk = strtoul(val, &endp, 0);
                    if (endp != val && vk <= 0xFEu && vk != 0x77u) o->dual_view_vk = (uint32_t)vk;
                } else if (tfh_str_ieq(key, "start_in_first_person")) {
                    o->start_in_first_person = tfh_parse_bool(val, o->start_in_first_person);
                } else if (tfh_str_ieq(key, "fp_forward_offset")) {
                    char* endp = NULL;
                    float fv = strtof(val, &endp);
                    if (endp != val) o->fp_forward_offset = tfh_clampf(fv, -4.00f, 8.00f, 2.35f);
                } else if (tfh_str_ieq(key, "fp_up_offset")) {
                    char* endp = NULL;
                    float fv = strtof(val, &endp);
                    if (endp != val) o->fp_up_offset = tfh_clampf(fv, 0.50f, 9.00f, 2.85f);
                } else if (tfh_str_ieq(key, "wall_safety_margin")) {
                    char* endp = NULL;
                    float fv = strtof(val, &endp);
                    if (endp != val) o->wall_safety_margin = tfh_clampf(fv, 0.05f, 2.00f, 0.28f);
                }
            }
        }

        p = eol;
        while (*p == '\r' || *p == '\n') p++;
    }
}

static inline int tfh_options_load_file(TfhOptions* o, const char* path)
{
    tfh_options_defaults(o);
    if (!path || !*path) return 0;
    FILE* fp = fopen(path, "rb");
    if (!fp) return 0;
    if (fseek(fp, 0, SEEK_END) != 0) { fclose(fp); return 0; }
    long sz = ftell(fp);
    if (sz <= 0 || sz > 65536L) { fclose(fp); return 0; }
    if (fseek(fp, 0, SEEK_SET) != 0) { fclose(fp); return 0; }
    char* buf = (char*)malloc((size_t)sz + 1u);
    if (!buf) { fclose(fp); return 0; }
    size_t rd = fread(buf, 1, (size_t)sz, fp);
    fclose(fp);
    buf[rd] = '\0';
    tfh_options_parse_text(o, buf);
    free(buf);
    return 1;
}

#endif /* TFH_OPTIONS_H */
