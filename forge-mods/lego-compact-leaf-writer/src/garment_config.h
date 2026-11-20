#ifndef LG_GARMENT_CONFIG_H
#define LG_GARMENT_CONFIG_H

#include <ctype.h>
#include <errno.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "compact_math.h"
#include "leaf_plan.h"

#define LG_MAX_CONFIG_BYTES (16u * 1024u * 1024u)

typedef struct {
    const char *p;
    const char *end;
    char err[192];
} LgJson;

static void LgSetErr(LgJson *j, const char *msg) {
    if (!j->err[0]) {
        snprintf(j->err, sizeof j->err, "%s", msg ? msg : "json_error");
    }
}

static void LgSkipWs(LgJson *j) {
    while (j->p < j->end) {
        unsigned char c = (unsigned char)*j->p;
        if (c == ' ' || c == '\t' || c == '\r' || c == '\n') j->p++;
        else break;
    }
}

static int LgConsumeChar(LgJson *j, char ch) {
    LgSkipWs(j);
    if (j->p < j->end && *j->p == ch) {
        j->p++;
        return 1;
    }
    return 0;
}

static int LgParseString(LgJson *j, char *out, size_t cap) {
    LgSkipWs(j);
    if (j->p >= j->end || *j->p != '"') {
        LgSetErr(j, "expected_string");
        return 0;
    }
    j->p++;
    size_t len = 0;
    while (j->p < j->end) {
        char c = *j->p++;
        if (c == '"') {
            if (out && cap) {
                size_t copy = len < cap ? len : cap - 1u;
                out[copy] = 0;
            }
            return 1;
        }
        if ((unsigned char)c < 0x20u) {
            LgSetErr(j, "control_char_in_string");
            return 0;
        }
        if (c == '\\') {
            if (j->p >= j->end) { LgSetErr(j, "unterminated_escape"); return 0; }
            char e = *j->p++;
            switch (e) {
                case '"': c = '"'; break;
                case '\\': c = '\\'; break;
                case '/': c = '/'; break;
                case 'b': c = '\b'; break;
                case 'f': c = '\f'; break;
                case 'n': c = '\n'; break;
                case 'r': c = '\r'; break;
                case 't': c = '\t'; break;
                default: LgSetErr(j, "unsupported_escape"); return 0;
            }
        }
        if (out && len + 1u < cap) out[len] = c;
        len++;
    }
    LgSetErr(j, "unterminated_string");
    return 0;
}

static int LgParseU64(LgJson *j, uint64_t *out) {
    LgSkipWs(j);
    if (j->p >= j->end || !isdigit((unsigned char)*j->p)) {
        LgSetErr(j, "expected_unsigned_int");
        return 0;
    }
    uint64_t v = 0;
    while (j->p < j->end && isdigit((unsigned char)*j->p)) {
        uint64_t d = (uint64_t)(*j->p - '0');
        if (v > (UINT64_MAX - d) / 10ull) { LgSetErr(j, "int_overflow"); return 0; }
        v = v * 10ull + d;
        j->p++;
    }
    if (j->p < j->end && (*j->p == '.' || *j->p == 'e' || *j->p == 'E')) {
        LgSetErr(j, "unexpected_float_for_int");
        return 0;
    }
    *out = v;
    return 1;
}

static int LgParseFloat(LgJson *j, float *out) {
    LgSkipWs(j);
    if (j->p >= j->end) { LgSetErr(j, "expected_number"); return 0; }
    const char *start = j->p;
    if (*j->p == '-') j->p++;
    if (j->p >= j->end || !isdigit((unsigned char)*j->p)) { LgSetErr(j, "expected_number"); return 0; }
    while (j->p < j->end && isdigit((unsigned char)*j->p)) j->p++;
    if (j->p < j->end && *j->p == '.') {
        j->p++;
        if (j->p >= j->end || !isdigit((unsigned char)*j->p)) { LgSetErr(j, "invalid_fraction"); return 0; }
        while (j->p < j->end && isdigit((unsigned char)*j->p)) j->p++;
    }
    if (j->p < j->end && (*j->p == 'e' || *j->p == 'E')) {
        j->p++;
        if (j->p < j->end && (*j->p == '+' || *j->p == '-')) j->p++;
        if (j->p >= j->end || !isdigit((unsigned char)*j->p)) { LgSetErr(j, "invalid_exponent"); return 0; }
        while (j->p < j->end && isdigit((unsigned char)*j->p)) j->p++;
    }
    size_t n = (size_t)(j->p - start);
    if (n == 0 || n >= 64u) { LgSetErr(j, "number_too_long"); return 0; }
    char buf[64];
    memcpy(buf, start, n);
    buf[n] = 0;
    errno = 0;
    char *ep = NULL;
    double d = strtod(buf, &ep);
    if (errno || ep != buf + n || !isfinite(d)) { LgSetErr(j, "nonfinite_number"); return 0; }
    *out = (float)d;
    return 1;
}

static int LgParseHex64(const char *s, uint64_t *out) {
    if (!s || !*s) return 0;
    if (s[0] == '0' && (s[1] == 'x' || s[1] == 'X')) s += 2;
    size_t n = strlen(s);
    if (n == 0 || n > 16u) return 0;
    uint64_t v = 0;
    for (size_t i = 0; i < n; i++) {
        unsigned char c = (unsigned char)s[i];
        uint64_t d;
        if (c >= '0' && c <= '9') d = (uint64_t)(c - '0');
        else if (c >= 'a' && c <= 'f') d = (uint64_t)(c - 'a' + 10);
        else if (c >= 'A' && c <= 'F') d = (uint64_t)(c - 'A' + 10);
        else return 0;
        v = (v << 4) | d;
    }
    *out = v;
    return v != 0ULL;
}

typedef struct {
    lp_asset asset;
    uint32_t mesh_node;
    uint32_t pin_mask;
    int has_ordinal, has_mesh_node, has_bones, has_groups;
    int has_map, has_bind, has_counts, has_palette;
    int pinned_mode;
} LgParsedMesh;

static int LgParseAlias(LgJson *j, cm_alias *out) {
    if (!LgConsumeChar(j, '{')) { LgSetErr(j, "alias_expected_object"); return 0; }
    uint64_t src = 0, dst = 0;
    int hasSrc = 0, hasDst = 0;
    if (!LgConsumeChar(j, '}')) {
        for (;;) {
            char key[32];
            if (!LgParseString(j, key, sizeof key) || !LgConsumeChar(j, ':')) return 0;
            if (!strcmp(key, "source")) { if (!LgParseU64(j, &src)) return 0; hasSrc = 1; }
            else if (!strcmp(key, "node")) { if (!LgParseU64(j, &dst)) return 0; hasDst = 1; }
            else { LgSetErr(j, "alias_unknown_key"); return 0; }
            if (LgConsumeChar(j, '}')) break;
            if (!LgConsumeChar(j, ',')) { LgSetErr(j, "alias_syntax"); return 0; }
        }
    }
    if (!hasSrc || !hasDst || src >= CM_SOURCE || dst >= 128u) {
        LgSetErr(j, "alias_bounds");
        return 0;
    }
    out->source = (uint32_t)src;
    out->node = (uint32_t)dst;
    return 1;
}

static int LgParseMesh(LgJson *j, LgParsedMesh *pm, uint64_t resource, uint32_t node_count) {
    memset(pm, 0, sizeof *pm);
    pm->asset.resource = resource;
    if (!LgConsumeChar(j, '{')) { LgSetErr(j, "mesh_expected_object"); return 0; }
    if (!LgConsumeChar(j, '}')) {
        for (;;) {
            char key[32];
            if (!LgParseString(j, key, sizeof key) || !LgConsumeChar(j, ':')) return 0;
            if (!strcmp(key, "ordinal")) {
                uint64_t v = 0; if (!LgParseU64(j, &v) || v < 1u || v > 4u) { LgSetErr(j, "mesh_ordinal"); return 0; }
                pm->asset.ordinal = (uint32_t)v; pm->has_ordinal = 1;
            } else if (!strcmp(key, "mesh_node")) {
                uint64_t v = 0; if (!LgParseU64(j, &v) || v >= node_count) { LgSetErr(j, "mesh_node_bounds"); return 0; }
                pm->mesh_node = (uint32_t)v; pm->has_mesh_node = 1;
            } else if (!strcmp(key, "skin_mode")) {
                char mode[32]; if (!LgParseString(j, mode, sizeof mode)) return 0;
                if (!strcmp(mode, "pinned")) pm->pinned_mode = 1;
                else if (!strcmp(mode, "structural")) pm->pinned_mode = 0;
                else { LgSetErr(j, "mesh_skin_mode"); return 0; }
            } else if (!strcmp(key, "bones")) {
                uint64_t v = 0; if (!LgParseU64(j, &v) || !v || v > LP_BONES) { LgSetErr(j, "mesh_bones"); return 0; }
                pm->asset.bones = (uint32_t)v; pm->has_bones = 1;
            } else if (!strcmp(key, "groups")) {
                uint64_t v = 0; if (!LgParseU64(j, &v) || !v || v > LP_GROUPS) { LgSetErr(j, "mesh_groups"); return 0; }
                pm->asset.groups = (uint32_t)v; pm->has_groups = 1;
            } else if (!strcmp(key, "map")) {
                if (!LgConsumeChar(j, '[')) { LgSetErr(j, "mesh_map_array"); return 0; }
                uint32_t idx = 0;
                if (!LgConsumeChar(j, ']')) {
                    for (;;) {
                        uint64_t v = 0;
                        if (idx >= LP_BONES || !LgParseU64(j, &v) || v >= node_count) { LgSetErr(j, "mesh_map_entry"); return 0; }
                        pm->asset.map[idx++] = (uint32_t)v;
                        if (LgConsumeChar(j, ']')) break;
                        if (!LgConsumeChar(j, ',')) { LgSetErr(j, "mesh_map_syntax"); return 0; }
                    }
                }
                pm->has_map = (int)idx + 1;
            } else if (!strcmp(key, "bind_bits")) {
                if (!LgConsumeChar(j, '[')) { LgSetErr(j, "mesh_bind_array"); return 0; }
                uint32_t idx = 0;
                if (!LgConsumeChar(j, ']')) {
                    for (;;) {
                        uint64_t v = 0;
                        if (idx >= LP_BONES * 12u || !LgParseU64(j, &v) || v > UINT32_MAX) { LgSetErr(j, "mesh_bind_entry"); return 0; }
                        pm->asset.bind[idx++] = (uint32_t)v;
                        if (LgConsumeChar(j, ']')) break;
                        if (!LgConsumeChar(j, ',')) { LgSetErr(j, "mesh_bind_syntax"); return 0; }
                    }
                }
                pm->has_bind = (int)idx + 1;
            } else if (!strcmp(key, "bind")) {
                if (!LgConsumeChar(j, '[')) { LgSetErr(j, "mesh_bind_array"); return 0; }
                uint32_t idx = 0;
                if (!LgConsumeChar(j, ']')) {
                    for (;;) {
                        float fv = 0.0f; uint32_t bits = 0;
                        if (idx >= LP_BONES * 12u || !LgParseFloat(j, &fv)) { LgSetErr(j, "mesh_bind_float"); return 0; }
                        memcpy(&bits, &fv, 4u);
                        pm->asset.bind[idx++] = bits;
                        if (LgConsumeChar(j, ']')) break;
                        if (!LgConsumeChar(j, ',')) { LgSetErr(j, "mesh_bind_syntax"); return 0; }
                    }
                }
                pm->has_bind = (int)idx + 1;
            } else if (!strcmp(key, "counts")) {
                if (!LgConsumeChar(j, '[')) { LgSetErr(j, "mesh_counts_array"); return 0; }
                uint32_t idx = 0;
                if (!LgConsumeChar(j, ']')) {
                    for (;;) {
                        uint64_t v = 0;
                        if (idx >= LP_GROUPS || !LgParseU64(j, &v) || !v || v > LP_BONES) { LgSetErr(j, "mesh_counts_entry"); return 0; }
                        pm->asset.counts[idx++] = (uint32_t)v;
                        if (LgConsumeChar(j, ']')) break;
                        if (!LgConsumeChar(j, ',')) { LgSetErr(j, "mesh_counts_syntax"); return 0; }
                    }
                }
                pm->has_counts = (int)idx + 1;
            } else if (!strcmp(key, "palette")) {
                if (!LgConsumeChar(j, '[')) { LgSetErr(j, "mesh_palette_array"); return 0; }
                uint32_t gidx = 0;
                if (!LgConsumeChar(j, ']')) {
                    for (;;) {
                        if (gidx >= LP_GROUPS || !LgConsumeChar(j, '[')) { LgSetErr(j, "mesh_palette_group"); return 0; }
                        uint32_t pidx = 0;
                        if (!LgConsumeChar(j, ']')) {
                            for (;;) {
                                uint64_t v = 0;
                                if (pidx >= LP_BONES || !LgParseU64(j, &v) || v >= LP_BONES) { LgSetErr(j, "mesh_palette_entry"); return 0; }
                                pm->asset.palette[gidx][pidx++] = (uint32_t)v;
                                if (LgConsumeChar(j, ']')) break;
                                if (!LgConsumeChar(j, ',')) { LgSetErr(j, "mesh_palette_syntax"); return 0; }
                            }
                        }
                        if (!pm->has_counts) pm->asset.counts[gidx] = pidx;
                        else if (pidx != pm->asset.counts[gidx]) { LgSetErr(j, "mesh_palette_count_mismatch"); return 0; }
                        gidx++;
                        if (LgConsumeChar(j, ']')) break;
                        if (!LgConsumeChar(j, ',')) { LgSetErr(j, "mesh_palette_syntax"); return 0; }
                    }
                }
                pm->has_palette = (int)gidx + 1;
            } else {
                LgSetErr(j, "mesh_unknown_key");
                return 0;
            }
            if (LgConsumeChar(j, '}')) break;
            if (!LgConsumeChar(j, ',')) { LgSetErr(j, "mesh_syntax"); return 0; }
        }
    }
    if (!pm->has_ordinal || !pm->has_mesh_node) { LgSetErr(j, "mesh_missing_required"); return 0; }
    if (!pm->has_bones) pm->asset.bones = 1u;
    if (!pm->has_groups) pm->asset.groups = 1u;
    if (pm->has_map) {
        if ((uint32_t)(pm->has_map - 1) != pm->asset.bones) { LgSetErr(j, "mesh_map_len"); return 0; }
        pm->pin_mask |= LP_PIN_MAP;
    }
    if (pm->has_bind) {
        if ((uint32_t)(pm->has_bind - 1) != pm->asset.bones * 12u) { LgSetErr(j, "mesh_bind_len"); return 0; }
        pm->pin_mask |= LP_PIN_BIND;
    }
    if (pm->has_counts && (uint32_t)(pm->has_counts - 1) != pm->asset.groups) { LgSetErr(j, "mesh_counts_len"); return 0; }
    if (pm->has_palette) {
        if ((uint32_t)(pm->has_palette - 1) != pm->asset.groups) { LgSetErr(j, "mesh_palette_len"); return 0; }
        for (uint32_t g = 0; g < pm->asset.groups; g++) {
            for (uint32_t k = 0; k < pm->asset.counts[g]; k++) {
                if (pm->asset.palette[g][k] >= pm->asset.bones) { LgSetErr(j, "mesh_palette_bone_oob"); return 0; }
            }
        }
        pm->pin_mask |= LP_PIN_PALETTE;
    }
    if (pm->pinned_mode && (pm->pin_mask != LP_PIN_ALL || !pm->has_bones || !pm->has_groups)) {
        LgSetErr(j, "mesh_pinned_incomplete");
        return 0;
    }
    return 1;
}

static int LgParseAndRegisterGarment(LgJson *j, uint32_t *loaded_out) {
    if (!LgConsumeChar(j, '{')) { LgSetErr(j, "garment_expected_object"); return 0; }
    char resHex[32] = {0};
    uint64_t nodeCount64 = 0;
    cm_alias aliases[CM_MAX_ALIASES];
    uint32_t aliasCount = 0;
    LgParsedMesh meshes[4];
    uint32_t meshCount = 0;
    uint32_t meshNodes[4] = {UINT32_MAX, UINT32_MAX, UINT32_MAX, UINT32_MAX};
    int hasRes = 0, hasNodes = 0, hasAliases = 0, hasMeshes = 0;

    /* Deferred parse of aliases/meshes since keys in JSON object may appear in any order. */
    const char *objStart = j->p - 1;
    (void)objStart;
    /* First scan allows node_count after/before meshes cleanly by two tiny passes or requiring node_count <= 128 on array bounds */
    uint32_t maxNodeUpper = 128u;
    if (!LgConsumeChar(j, '}')) {
        for (;;) {
            char key[32];
            if (!LgParseString(j, key, sizeof key) || !LgConsumeChar(j, ':')) return 0;
            if (!strcmp(key, "resource")) {
                if (!LgParseString(j, resHex, sizeof resHex)) return 0;
                hasRes = 1;
            } else if (!strcmp(key, "name") || !strcmp(key, "faction")) {
                char tmp[80];
                if (!LgParseString(j, tmp, sizeof tmp)) return 0;
            } else if (!strcmp(key, "node_count")) {
                if (!LgParseU64(j, &nodeCount64) || !nodeCount64 || nodeCount64 > 128u) {
                    LgSetErr(j, "garment_node_count");
                    return 0;
                }
                hasNodes = 1;
            } else if (!strcmp(key, "aliases")) {
                if (!LgConsumeChar(j, '[')) { LgSetErr(j, "aliases_array"); return 0; }
                aliasCount = 0;
                if (!LgConsumeChar(j, ']')) {
                    for (;;) {
                        if (aliasCount >= CM_MAX_ALIASES) { LgSetErr(j, "aliases_overflow"); return 0; }
                        if (!LgParseAlias(j, &aliases[aliasCount++])) return 0;
                        if (LgConsumeChar(j, ']')) break;
                        if (!LgConsumeChar(j, ',')) { LgSetErr(j, "aliases_syntax"); return 0; }
                    }
                }
                hasAliases = 1;
            } else if (!strcmp(key, "meshes")) {
                if (!LgConsumeChar(j, '[')) { LgSetErr(j, "meshes_array"); return 0; }
                meshCount = 0;
                if (!LgConsumeChar(j, ']')) {
                    for (;;) {
                        if (meshCount >= 4u) { LgSetErr(j, "meshes_overflow"); return 0; }
                        if (!LgParseMesh(j, &meshes[meshCount], 1ULL, hasNodes ? (uint32_t)nodeCount64 : maxNodeUpper)) return 0;
                        uint32_t ord = meshes[meshCount].asset.ordinal;
                        if (meshNodes[ord - 1u] != UINT32_MAX) { LgSetErr(j, "duplicate_mesh_ordinal"); return 0; }
                        meshNodes[ord - 1u] = meshes[meshCount].mesh_node;
                        meshCount++;
                        if (LgConsumeChar(j, ']')) break;
                        if (!LgConsumeChar(j, ',')) { LgSetErr(j, "meshes_syntax"); return 0; }
                    }
                }
                hasMeshes = 1;
            } else {
                LgSetErr(j, "garment_unknown_key");
                return 0;
            }
            if (LgConsumeChar(j, '}')) break;
            if (!LgConsumeChar(j, ',')) { LgSetErr(j, "garment_syntax"); return 0; }
        }
    }

    uint64_t resource = 0;
    if (!hasRes || !hasNodes || !hasAliases || !hasMeshes || !meshCount || !LgParseHex64(resHex, &resource)) {
        LgSetErr(j, "garment_missing_required");
        return 0;
    }
    uint32_t nc = (uint32_t)nodeCount64;
    for (uint32_t m = 0; m < meshCount; m++) {
        meshes[m].asset.resource = resource;
        if (meshes[m].mesh_node >= nc) { LgSetErr(j, "mesh_node_exceeds_node_count"); return 0; }
        for (uint32_t b = 0; b < meshes[m].asset.bones; b++) {
            if ((meshes[m].pin_mask & LP_PIN_MAP) && meshes[m].asset.map[b] >= nc) {
                LgSetErr(j, "mesh_map_exceeds_node_count");
                return 0;
            }
        }
    }
    if (!cm_register_dynamic_garment(resource, nc, aliasCount, aliases, meshNodes)) {
        LgSetErr(j, "garment_register_conflict_or_bounds");
        return 0;
    }
    for (uint32_t m = 0; m < meshCount; m++) {
        if (!lp_register_asset(&meshes[m].asset, meshes[m].pin_mask)) {
            LgSetErr(j, "mesh_asset_register_failed");
            return 0;
        }
    }
    if (loaded_out) (*loaded_out)++;
    return 1;
}

static int lg_load_garments_json(const char *json_text, size_t len, uint32_t *loaded_out, char *err_buf, size_t err_cap) {
    if (loaded_out) *loaded_out = 0;
    if (err_buf && err_cap) err_buf[0] = 0;
    if (!json_text || !len || len > LG_MAX_CONFIG_BYTES) {
        if (err_buf && err_cap) snprintf(err_buf, err_cap, "empty_or_oversized_json");
        return 0;
    }

    LgJson j;
    j.p = json_text;
    j.end = json_text + len;
    j.err[0] = 0;

    if (!LgConsumeChar(&j, '{')) { LgSetErr(&j, "root_expected_object"); goto fail; }
    uint64_t schemaVer = 0;
    int hasVer = 0, hasGarments = 0;
    uint32_t loaded = 0;

    if (!LgConsumeChar(&j, '}')) {
        for (;;) {
            char key[32];
            if (!LgParseString(&j, key, sizeof key) || !LgConsumeChar(&j, ':')) goto fail;
            if (!strcmp(key, "schema_version")) {
                if (!LgParseU64(&j, &schemaVer) || schemaVer != 1u) { LgSetErr(&j, "unsupported_schema_version"); goto fail; }
                hasVer = 1;
            } else if (!strcmp(key, "kit_pack") || !strcmp(key, "$schema")) {
                char tmp[256];
                if (!LgParseString(&j, tmp, sizeof tmp)) goto fail;
            } else if (!strcmp(key, "garments")) {
                if (!LgConsumeChar(&j, '[')) { LgSetErr(&j, "garments_array"); goto fail; }
                if (!LgConsumeChar(&j, ']')) {
                    for (;;) {
                        if (!LgParseAndRegisterGarment(&j, &loaded)) goto fail;
                        if (LgConsumeChar(&j, ']')) break;
                        if (!LgConsumeChar(&j, ',')) { LgSetErr(&j, "garments_syntax"); goto fail; }
                    }
                }
                hasGarments = 1;
            } else {
                LgSetErr(&j, "root_unknown_key");
                goto fail;
            }
            if (LgConsumeChar(&j, '}')) break;
            if (!LgConsumeChar(&j, ',')) { LgSetErr(&j, "root_syntax"); goto fail; }
        }
    }
    LgSkipWs(&j);
    if (j.p != j.end) { LgSetErr(&j, "trailing_tokens"); goto fail; }
    if (!hasVer || !hasGarments) { LgSetErr(&j, "missing_schema_version_or_garments"); goto fail; }
    if (loaded_out) *loaded_out = loaded;
    return 1;

fail:
    cm_reset_dynamic_garments();
    lp_reset_dynamic_assets();
    if (err_buf && err_cap) snprintf(err_buf, err_cap, "%s", j.err[0] ? j.err : "parse_failed");
    return 0;
}

static int lg_load_garments_file(const char *path, int *found_out, uint32_t *loaded_out, char *err_buf, size_t err_cap) {
    if (found_out) *found_out = 0;
    if (loaded_out) *loaded_out = 0;
    if (err_buf && err_cap) err_buf[0] = 0;
    if (!path || !*path) return 0;

    FILE *f = fopen(path, "rb");
    if (!f) return 1; /* absent optional config uses baked tables cleanly */
    if (found_out) *found_out = 1;

    if (fseek(f, 0, SEEK_END) != 0) { fclose(f); return 0; }
    long sz = ftell(f);
    if (sz <= 0 || (unsigned long)sz > LG_MAX_CONFIG_BYTES || fseek(f, 0, SEEK_SET) != 0) {
        fclose(f);
        if (err_buf && err_cap) snprintf(err_buf, err_cap, "file_size_bounds");
        return 0;
    }
    char *buf = (char *)malloc((size_t)sz + 1u);
    if (!buf) { fclose(f); return 0; }
    size_t got = fread(buf, 1, (size_t)sz, f);
    fclose(f);
    if (got != (size_t)sz) {
        free(buf);
        if (err_buf && err_cap) snprintf(err_buf, err_cap, "file_read_error");
        return 0;
    }
    buf[got] = 0;
    int ok = lg_load_garments_json(buf, got, loaded_out, err_buf, err_cap);
    free(buf);
    return ok;
}

#endif
