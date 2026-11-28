/* Star Wars Localization Bridge (0.2.0) — Stingray 0xF0000011 + TEXT (0x0d972bab10b40fd3)
 * multi-table US English archive parser, ForgeCredits-preserving diff table, and curated
 * v2.3 Clonedivers / EmpireDivers overrides (100% vanilla HD2 references, Overloaded Gonk Droid,
 * and EmpireDivers DS-1 Death Star).
 * Pure C11 + Win32 FindFirstFileA. */
#ifndef SW_LOC_TABLE_H
#define SW_LOC_TABLE_H

#include <ctype.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#endif

#include "sw_curated_overrides.h"

#define SW_LOC_ARCHIVE_MAGIC       0xF0000011u
#define SW_LOC_US_TEXT_NAME        0x7C7587B563F10985ULL
#define SW_LOC_TEXT_TYPE           0x0D972BAB10B40FD3ULL
#define SW_LOC_US_LANG_ID          0x03F97B57u
#define SW_LOC_SYNTH_LANG_ID       1u
#define SW_LOC_WARBOND_LOCKED_ID   0xF8AD8A16u
#define SW_LOC_MAX_TYPES           100000u
#define SW_LOC_MAX_FILES           1000000u
#define SW_LOC_MAX_US_TABLES       64u
#define SW_LOC_MAX_TEXT_BYTES      (16u * 1024u * 1024u)
#define SW_LOC_ARCHIVE_PREFIX      "9ba626afa44a3aa3.patch_"

typedef enum SwLocStatus {
    SW_LOC_NO_PATCHES = 0,
    SW_LOC_ALREADY_TOPMOST = 1,
    SW_LOC_VANILLA_ONLY = 2,
    SW_LOC_DIFF_LOADED = 3,
    SW_LOC_ERROR = 4,
    SW_LOC_CURATED_LOADED = 5
} SwLocStatus;

typedef struct SwLocEntry {
    uint32_t id;
    const char* text;
} SwLocEntry;

typedef struct SwLocTable {
    SwLocEntry* entries;
    uint32_t count;
    char* pool;
    size_t pool_size;
} SwLocTable;

typedef struct SwLocLoadReport {
    SwLocStatus status;
    SwLocMode mode;
    int scanned_patches;
    int base_patch_index;
    int pack_patch_index;
    uint32_t base_string_count;
    uint32_t pack_string_count;
    uint32_t diff_count;
    uint32_t preserved_forge_credits;
    uint32_t curated_applied;
} SwLocLoadReport;

typedef struct SwRawTextView {
    uint64_t name_hash;
    uint32_t language;
    uint8_t* blob;
    size_t size;
    uint32_t count;
    const uint32_t* keys;
    const uint32_t* offsets;
    int keys_sorted;
} SwRawTextView;

typedef struct SwRawTextSet {
    SwRawTextView* views;
    uint32_t count;
} SwRawTextSet;

static inline const char* SwLocModeName(SwLocMode mode) {
    switch (mode) {
        case SW_LOC_MODE_EMPIRE: return "empire";
        case SW_LOC_MODE_CLONES: return "clones";
        default: return "none";
    }
}

static inline uint32_t SwLocGetCuratedSpan(SwLocMode mode, const SwCuratedOverride** out_arr) {
    if (mode == SW_LOC_MODE_EMPIRE) {
        if (out_arr != NULL) *out_arr = kSwEmpireCuratedOverrides;
        return SW_EMPIRE_CURATED_COUNT;
    }
    if (mode == SW_LOC_MODE_CLONES) {
        if (out_arr != NULL) *out_arr = kSwClonesCuratedOverrides;
        return SW_CLONES_CURATED_COUNT;
    }
    if (out_arr != NULL) *out_arr = NULL;
    return 0u;
}

static inline uint32_t SwLocReadU32(const uint8_t* p) {
    uint32_t v;
    memcpy(&v, p, 4);
    return v;
}

static inline uint64_t SwLocReadU64(const uint8_t* p) {
    uint64_t v;
    memcpy(&v, p, 8);
    return v;
}

static inline int SwLocContainsCI(const char* haystack, const char* needle) {
    if (haystack == NULL || needle == NULL || needle[0] == '\0') return 0;
    size_t nlen = strlen(needle);
    for (const char* p = haystack; *p != '\0'; p++) {
        size_t i = 0;
        while (i < nlen && p[i] != '\0' &&
               tolower((unsigned char)p[i]) == tolower((unsigned char)needle[i])) {
            i++;
        }
        if (i == nlen) return 1;
    }
    return 0;
}

static inline void SwLocFreeView(SwRawTextView* view) {
    if (view == NULL) return;
    free(view->blob);
    memset(view, 0, sizeof *view);
}

static inline void SwLocFreeSet(SwRawTextSet* set) {
    if (set == NULL) return;
    for (uint32_t i = 0; i < set->count; i++) {
        SwLocFreeView(&set->views[i]);
    }
    free(set->views);
    memset(set, 0, sizeof *set);
}

static inline void SwLocFreeTable(SwLocTable* tbl) {
    if (tbl == NULL) return;
    free(tbl->entries);
    free(tbl->pool);
    memset(tbl, 0, sizeof *tbl);
}

static inline int SwLocParseBlobIntoView(uint64_t name_hash, uint8_t* blob, uint32_t main_size, SwRawTextView* out) {
    memset(out, 0, sizeof *out);
    if (main_size < 16u) return -1;

    uint32_t count = SwLocReadU32(blob + 8);
    uint32_t lang = SwLocReadU32(blob + 12);
    uint64_t table_end = 16ULL + 8ULL * (uint64_t)count;
    if (table_end > (uint64_t)main_size) return -1;

    const uint32_t* keys = (const uint32_t*)(blob + 16);
    const uint32_t* offsets = (const uint32_t*)(blob + 16 + 4u * count);
    int sorted = 1;
    for (uint32_t i = 0; i < count; i++) {
        if (i > 0 && keys[i] < keys[i - 1]) sorted = 0;
        uint32_t off = offsets[i];
        if (off >= main_size) return -1;
        const uint8_t* nul = (const uint8_t*)memchr(blob + off, 0, main_size - off);
        if (nul == NULL) return -1;
    }

    out->name_hash = name_hash;
    out->language = lang;
    out->blob = blob;
    out->size = main_size;
    out->count = count;
    out->keys = keys;
    out->offsets = offsets;
    out->keys_sorted = sorted;
    return 1;
}

/* Extracts a specific TEXT resource (`target_name_hash` / 0x0d972bab10b40fd3) from a Stingray 0xF0000011 patch file.
 * Returns 1 if found and valid, 0 if not present, -1 on I/O or format error. */
static inline int SwLocReadNamedTextFromPatch(const char* patch_path, uint64_t target_name_hash, SwRawTextView* out) {
    memset(out, 0, sizeof *out);
    FILE* fp = fopen(patch_path, "rb");
    if (fp == NULL) return -1;

    if (fseek(fp, 0, SEEK_END) != 0) { fclose(fp); return -1; }
    long fsize_long = ftell(fp);
    if (fsize_long < 72L) { fclose(fp); return -1; }
    uint64_t file_size = (uint64_t)fsize_long;
    if (fseek(fp, 0, SEEK_SET) != 0) { fclose(fp); return -1; }

    uint8_t header[72];
    if (fread(header, 1, sizeof header, fp) != sizeof header) { fclose(fp); return -1; }

    uint32_t magic = SwLocReadU32(header + 0);
    uint32_t nt = SwLocReadU32(header + 4);
    uint32_t nf = SwLocReadU32(header + 8);
    if (magic != SW_LOC_ARCHIVE_MAGIC || nt > SW_LOC_MAX_TYPES || nf > SW_LOC_MAX_FILES) {
        fclose(fp);
        return -1;
    }

    uint64_t dir_off = 72ULL + 32ULL * (uint64_t)nt;
    uint64_t dir_bytes = 80ULL * (uint64_t)nf;
    if (dir_off > file_size || dir_bytes > file_size - dir_off) {
        fclose(fp);
        return -1;
    }

    if (fseek(fp, (long)dir_off, SEEK_SET) != 0) { fclose(fp); return -1; }

    int found = 0;
    uint64_t main_off = 0;
    uint32_t main_size = 0;
    uint8_t row[80];
    for (uint32_t i = 0; i < nf; i++) {
        if (fread(row, 1, sizeof row, fp) != sizeof row) { fclose(fp); return -1; }
        uint64_t name_hash = SwLocReadU64(row + 0);
        uint64_t type_hash = SwLocReadU64(row + 8);
        if (name_hash == target_name_hash && type_hash == SW_LOC_TEXT_TYPE) {
            main_off = SwLocReadU64(row + 16);
            main_size = SwLocReadU32(row + 56);
            found = 1;
            break;
        }
    }

    if (!found) {
        fclose(fp);
        return 0;
    }

    if (main_size < 16u || main_size > SW_LOC_MAX_TEXT_BYTES ||
        main_off > file_size || (uint64_t)main_size > file_size - main_off) {
        fclose(fp);
        return -1;
    }

    uint8_t* blob = (uint8_t*)malloc(main_size);
    if (blob == NULL) { fclose(fp); return -1; }

    if (fseek(fp, (long)main_off, SEEK_SET) != 0 || fread(blob, 1, main_size, fp) != main_size) {
        free(blob);
        fclose(fp);
        return -1;
    }
    fclose(fp);

    if (SwLocParseBlobIntoView(target_name_hash, blob, main_size, out) != 1) {
        free(blob);
        return -1;
    }
    return 1;
}

/* Extracts primary US English TEXT (0x7c7587b563f10985 / 0x0d972bab10b40fd3). */
static inline int SwLocReadUsTextFromPatch(const char* patch_path, SwRawTextView* out) {
    return SwLocReadNamedTextFromPatch(patch_path, SW_LOC_US_TEXT_NAME, out);
}

/* Extracts ALL US English TEXT resources (language == 0x03F97B57, synthetic 0/1, or 0x7c7587b563f10985)
 * from a Stingray 0xF0000011 patch file so multi-table ForgeCredits clobbers are fully restored.
 * Returns 1 if >= 1 US TEXT found, 0 if none present, -1 on I/O or format error. */
static inline int SwLocReadAllUsTextsFromPatch(const char* patch_path, SwRawTextSet* out_set) {
    memset(out_set, 0, sizeof *out_set);
    FILE* fp = fopen(patch_path, "rb");
    if (fp == NULL) return -1;

    if (fseek(fp, 0, SEEK_END) != 0) { fclose(fp); return -1; }
    long fsize_long = ftell(fp);
    if (fsize_long < 72L) { fclose(fp); return -1; }
    uint64_t file_size = (uint64_t)fsize_long;
    if (fseek(fp, 0, SEEK_SET) != 0) { fclose(fp); return -1; }

    uint8_t header[72];
    if (fread(header, 1, sizeof header, fp) != sizeof header) { fclose(fp); return -1; }

    uint32_t magic = SwLocReadU32(header + 0);
    uint32_t nt = SwLocReadU32(header + 4);
    uint32_t nf = SwLocReadU32(header + 8);
    if (magic != SW_LOC_ARCHIVE_MAGIC || nt > SW_LOC_MAX_TYPES || nf > SW_LOC_MAX_FILES) {
        fclose(fp);
        return -1;
    }

    uint64_t dir_off = 72ULL + 32ULL * (uint64_t)nt;
    uint64_t dir_bytes = 80ULL * (uint64_t)nf;
    if (dir_off > file_size || dir_bytes > file_size - dir_off) {
        fclose(fp);
        return -1;
    }

    if (fseek(fp, (long)dir_off, SEEK_SET) != 0) { fclose(fp); return -1; }

    if (nf == 0) {
        fclose(fp);
        return 0;
    }

    typedef struct TextLoc { uint64_t name_hash; uint64_t main_off; uint32_t main_size; } TextLoc;
    TextLoc* locs = (TextLoc*)calloc(nf, sizeof(TextLoc));
    if (locs == NULL) { fclose(fp); return -1; }
    uint32_t n_locs = 0;

    uint8_t row[80];
    for (uint32_t i = 0; i < nf; i++) {
        if (fread(row, 1, sizeof row, fp) != sizeof row) { free(locs); fclose(fp); return -1; }
        uint64_t name_hash = SwLocReadU64(row + 0);
        uint64_t type_hash = SwLocReadU64(row + 8);
        if (type_hash == SW_LOC_TEXT_TYPE) {
            locs[n_locs].name_hash = name_hash;
            locs[n_locs].main_off = SwLocReadU64(row + 16);
            locs[n_locs].main_size = SwLocReadU32(row + 56);
            n_locs++;
        }
    }

    if (n_locs == 0) {
        free(locs);
        fclose(fp);
        return 0;
    }

    SwRawTextView* views = (SwRawTextView*)calloc(SW_LOC_MAX_US_TABLES, sizeof(SwRawTextView));
    if (views == NULL) { free(locs); fclose(fp); return -1; }

    uint32_t kept = 0;
    for (uint32_t i = 0; i < n_locs; i++) {
        uint64_t m_off = locs[i].main_off;
        uint32_t m_sz = locs[i].main_size;
        if (m_sz < 16u || m_sz > SW_LOC_MAX_TEXT_BYTES || m_off > file_size || (uint64_t)m_sz > file_size - m_off) {
            for (uint32_t k = 0; k < kept; k++) SwLocFreeView(&views[k]);
            free(views);
            free(locs);
            fclose(fp);
            return -1;
        }
        uint8_t hdr16[16];
        if (fseek(fp, (long)m_off, SEEK_SET) != 0 || fread(hdr16, 1, 16, fp) != 16) {
            for (uint32_t k = 0; k < kept; k++) SwLocFreeView(&views[k]);
            free(views);
            free(locs);
            fclose(fp);
            return -1;
        }
        uint32_t lang = SwLocReadU32(hdr16 + 12);
        int is_us = (locs[i].name_hash == SW_LOC_US_TEXT_NAME || lang == SW_LOC_US_LANG_ID || lang == SW_LOC_SYNTH_LANG_ID);
        if (!is_us) continue;

        if (kept >= SW_LOC_MAX_US_TABLES) {
            for (uint32_t k = 0; k < kept; k++) SwLocFreeView(&views[k]);
            free(views);
            free(locs);
            fclose(fp);
            return -1;
        }

        uint8_t* blob = (uint8_t*)malloc(m_sz);
        if (blob == NULL) {
            for (uint32_t k = 0; k < kept; k++) SwLocFreeView(&views[k]);
            free(views);
            free(locs);
            fclose(fp);
            return -1;
        }
        if (fseek(fp, (long)m_off, SEEK_SET) != 0 || fread(blob, 1, m_sz, fp) != m_sz) {
            free(blob);
            for (uint32_t k = 0; k < kept; k++) SwLocFreeView(&views[k]);
            free(views);
            free(locs);
            fclose(fp);
            return -1;
        }
        if (SwLocParseBlobIntoView(locs[i].name_hash, blob, m_sz, &views[kept]) != 1) {
            free(blob);
            for (uint32_t k = 0; k < kept; k++) SwLocFreeView(&views[k]);
            free(views);
            free(locs);
            fclose(fp);
            return -1;
        }
        kept++;
    }
    free(locs);
    fclose(fp);

    if (kept == 0) {
        free(views);
        return 0;
    }
    out_set->views = views;
    out_set->count = kept;
    return 1;
}

static inline const char* SwLocRawLookup(const SwRawTextView* view, uint32_t id) {
    if (view == NULL || view->count == 0) return NULL;
    if (view->keys_sorted) {
        uint32_t lo = 0, hi = view->count;
        while (lo < hi) {
            uint32_t mid = lo + (hi - lo) / 2u;
            uint32_t k = view->keys[mid];
            if (k == id) return (const char*)(view->blob + view->offsets[mid]);
            if (k < id) lo = mid + 1u;
            else hi = mid;
        }
        return NULL;
    }
    for (uint32_t i = 0; i < view->count; i++) {
        if (view->keys[i] == id) return (const char*)(view->blob + view->offsets[i]);
    }
    return NULL;
}

static inline const char* SwLocRawLookupAcrossViews(const SwRawTextView* views, uint32_t view_count, uint32_t id) {
    if (views == NULL) return NULL;
    for (uint32_t i = 0; i < view_count; i++) {
        const char* s = SwLocRawLookup(&views[i], id);
        if (s != NULL) return s;
    }
    return NULL;
}

static inline SwLocMode SwLocDetectModeFromViews(const SwRawTextView* views, uint32_t view_count) {
    if (views == NULL || view_count == 0) return SW_LOC_MODE_NONE;
    for (uint32_t i = 0; i < view_count; i++) {
        const char* probe = SwLocRawLookup(&views[i], SW_LOC_MODE_PROBE_ID);
        if (probe != NULL) {
            if (strcmp(probe, "Imperial Artillery: Explosive") == 0) return SW_LOC_MODE_EMPIRE;
            if (strcmp(probe, "Republic Artillery: Explosive") == 0) return SW_LOC_MODE_CLONES;
        }
    }
    return SW_LOC_MODE_NONE;
}

static inline int SwLocIsProtectedHellForgeEntry(uint32_t id, const char* s) {
    if (id == SW_LOC_WARBOND_LOCKED_ID) return 1;
    if (s != NULL && SwLocContainsCI(s, "forge credit")) return 1;
    return 0;
}

static inline int SwLocViewHasForgeCredits(const SwRawTextView* view) {
    if (view == NULL) return 0;
    for (uint32_t i = 0; i < view->count; i++) {
        const char* s = (const char*)(view->blob + view->offsets[i]);
        if (SwLocIsProtectedHellForgeEntry(view->keys[i], s)) return 1;
    }
    return 0;
}

static inline int SwLocSetHasForgeCredits(const SwRawTextSet* set) {
    if (set == NULL) return 0;
    for (uint32_t i = 0; i < set->count; i++) {
        if (SwLocViewHasForgeCredits(&set->views[i])) return 1;
    }
    return 0;
}

static int SwLocCmpEntry(const void* a, const void* b) {
    const SwLocEntry* ea = (const SwLocEntry*)a;
    const SwLocEntry* eb = (const SwLocEntry*)b;
    if (ea->id < eb->id) return -1;
    if (ea->id > eb->id) return 1;
    return 0;
}

static inline int SwLocIsProtectedAcrossBases(uint32_t id, const SwRawTextView* bases, uint32_t base_count) {
    if (id == SW_LOC_WARBOND_LOCKED_ID) return 1;
    if (bases == NULL) return 0;
    for (uint32_t b = 0; b < base_count; b++) {
        const char* bstr = SwLocRawLookup(&bases[b], id);
        if (bstr != NULL && SwLocContainsCI(bstr, "forge credit")) return 1;
    }
    return 0;
}

/* Deterministic O(N + M) sorted two-pointer merge of mode-specific curated v2.3 overrides onto an existing
 * sorted SwLocTable (or empty SwLocTable when the Star Wars pack is already topmost).
 *  - Curated overrides win over pack entries on equal ID.
 *  - Protected Forge Credits & WARBOND LOCKED IDs across `protected_bases` are NEVER overridden.
 *  - When `skip_if_equal_in_bases` != 0 (unclobbered pack already topmost), curated entries already byte-identical
 *    to the topmost pack's text are omitted so no redundant overrides are held. */
static inline int SwLocApplyCuratedOverrides(
    SwLocMode mode,
    const SwRawTextView* protected_bases,
    uint32_t protected_base_count,
    int skip_if_equal_in_bases,
    SwLocTable* io_tbl,
    uint32_t* out_curated_applied,
    uint32_t* io_preserved_fc)
{
    if (out_curated_applied != NULL) *out_curated_applied = 0u;
    const SwCuratedOverride* curated = NULL;
    uint32_t curated_count = SwLocGetCuratedSpan(mode, &curated);
    if (curated == NULL || curated_count == 0u) return 0;

    /* Pass 1: compute exact merged entry count and string pool size. */
    uint32_t a = 0u, b = 0u;
    uint32_t merged_count = 0u;
    size_t merged_pool = 0u;
    uint32_t applied = 0u;

    while (a < io_tbl->count || b < curated_count) {
        if (b >= curated_count || (a < io_tbl->count && io_tbl->entries[a].id < curated[b].id)) {
            merged_count++;
            merged_pool += strlen(io_tbl->entries[a].text) + 1u;
            a++;
        } else if (a >= io_tbl->count || curated[b].id < io_tbl->entries[a].id) {
            uint32_t cid = curated[b].id;
            const char* ctxt = curated[b].text;
            if (SwLocIsProtectedAcrossBases(cid, protected_bases, protected_base_count)) {
                if (io_preserved_fc != NULL) (*io_preserved_fc)++;
            } else {
                const char* existing = skip_if_equal_in_bases
                    ? SwLocRawLookupAcrossViews(protected_bases, protected_base_count, cid)
                    : NULL;
                if (existing == NULL || strcmp(existing, ctxt) != 0) {
                    merged_count++;
                    merged_pool += strlen(ctxt) + 1u;
                    applied++;
                }
            }
            b++;
        } else {
            /* Equal ID in both io_tbl and curated: curated override wins unless protected. */
            uint32_t cid = curated[b].id;
            const char* ctxt = curated[b].text;
            if (SwLocIsProtectedAcrossBases(cid, protected_bases, protected_base_count)) {
                if (io_preserved_fc != NULL) (*io_preserved_fc)++;
            } else {
                merged_count++;
                merged_pool += strlen(ctxt) + 1u;
                applied++;
            }
            a++;
            b++;
        }
    }

    if (applied == 0u) {
        return 0;
    }
    if (merged_count == 0u) {
        SwLocFreeTable(io_tbl);
        return 0;
    }

    SwLocEntry* new_entries = (SwLocEntry*)malloc((size_t)merged_count * sizeof(SwLocEntry));
    char* new_pool = (char*)malloc(merged_pool);
    if (new_entries == NULL || new_pool == NULL) {
        free(new_entries);
        free(new_pool);
        return -1;
    }

    /* Pass 2: populate merged sorted entries & pool. */
    a = 0u;
    b = 0u;
    uint32_t out_idx = 0u;
    size_t pos = 0u;
    while (a < io_tbl->count || b < curated_count) {
        if (b >= curated_count || (a < io_tbl->count && io_tbl->entries[a].id < curated[b].id)) {
            const char* s = io_tbl->entries[a].text;
            size_t len = strlen(s) + 1u;
            memcpy(new_pool + pos, s, len);
            new_entries[out_idx].id = io_tbl->entries[a].id;
            new_entries[out_idx].text = new_pool + pos;
            out_idx++;
            pos += len;
            a++;
        } else if (a >= io_tbl->count || curated[b].id < io_tbl->entries[a].id) {
            uint32_t cid = curated[b].id;
            const char* ctxt = curated[b].text;
            if (!SwLocIsProtectedAcrossBases(cid, protected_bases, protected_base_count)) {
                const char* existing = skip_if_equal_in_bases
                    ? SwLocRawLookupAcrossViews(protected_bases, protected_base_count, cid)
                    : NULL;
                if (existing == NULL || strcmp(existing, ctxt) != 0) {
                    size_t len = strlen(ctxt) + 1u;
                    memcpy(new_pool + pos, ctxt, len);
                    new_entries[out_idx].id = cid;
                    new_entries[out_idx].text = new_pool + pos;
                    out_idx++;
                    pos += len;
                }
            }
            b++;
        } else {
            uint32_t cid = curated[b].id;
            const char* ctxt = curated[b].text;
            if (!SwLocIsProtectedAcrossBases(cid, protected_bases, protected_base_count)) {
                size_t len = strlen(ctxt) + 1u;
                memcpy(new_pool + pos, ctxt, len);
                new_entries[out_idx].id = cid;
                new_entries[out_idx].text = new_pool + pos;
                out_idx++;
                pos += len;
            }
            a++;
            b++;
        }
    }

    SwLocFreeTable(io_tbl);
    io_tbl->entries = new_entries;
    io_tbl->count = out_idx;
    io_tbl->pool = new_pool;
    io_tbl->pool_size = merged_pool;
    if (out_curated_applied != NULL) *out_curated_applied = applied;
    return 0;
}

/* Builds sorted diff table across N (pack_views[k] - base_views[k]) pairs while preserving every HellForge
 * Forge Credits / WARBOND LOCKED baseline string across ALL baseline US tables. */
static inline int SwLocBuildMultiDiffWithProtectedBases(
    const SwRawTextView* pack_views,
    const SwRawTextView* base_views,
    uint32_t pair_count,
    const SwRawTextView* protected_bases,
    uint32_t protected_base_count,
    SwLocTable* out,
    uint32_t* out_preserved_fc)
{
    memset(out, 0, sizeof *out);
    if (out_preserved_fc != NULL) *out_preserved_fc = 0;
    if (pack_views == NULL || base_views == NULL || pair_count == 0) return 0;

    const SwRawTextView* all_bases = protected_bases != NULL ? protected_bases : base_views;
    uint32_t all_base_count = protected_bases != NULL ? protected_base_count : pair_count;

    uint32_t diff_count = 0;
    size_t pool_bytes = 0;
    uint32_t preserved_fc = 0;

    for (uint32_t p = 0; p < pair_count; p++) {
        const SwRawTextView* pack = &pack_views[p];
        const SwRawTextView* base = &base_views[p];
        for (uint32_t i = 0; i < pack->count; i++) {
            uint32_t id = pack->keys[i];
            const char* pstr = (const char*)(pack->blob + pack->offsets[i]);
            if (pstr[0] == '\0') continue;
            const char* bstr = SwLocRawLookup(base, id);
            if (bstr != NULL && strcmp(pstr, bstr) == 0) continue;
            if (SwLocIsProtectedAcrossBases(id, all_bases, all_base_count)) {
                preserved_fc++;
                continue;
            }
            diff_count++;
            pool_bytes += strlen(pstr) + 1u;
        }
    }

    if (out_preserved_fc != NULL) *out_preserved_fc = preserved_fc;
    if (diff_count == 0) return 0;

    SwLocEntry* entries = (SwLocEntry*)malloc((size_t)diff_count * sizeof(SwLocEntry));
    char* pool = (char*)malloc(pool_bytes);
    if (entries == NULL || pool == NULL) {
        free(entries);
        free(pool);
        return -1;
    }

    uint32_t idx = 0;
    size_t pos = 0;
    for (uint32_t p = 0; p < pair_count; p++) {
        const SwRawTextView* pack = &pack_views[p];
        const SwRawTextView* base = &base_views[p];
        for (uint32_t i = 0; i < pack->count; i++) {
            uint32_t id = pack->keys[i];
            const char* pstr = (const char*)(pack->blob + pack->offsets[i]);
            if (pstr[0] == '\0') continue;
            const char* bstr = SwLocRawLookup(base, id);
            if (bstr != NULL && strcmp(pstr, bstr) == 0) continue;
            if (SwLocIsProtectedAcrossBases(id, all_bases, all_base_count)) continue;
            size_t len = strlen(pstr) + 1u;
            memcpy(pool + pos, pstr, len);
            entries[idx].id = id;
            entries[idx].text = pool + pos;
            idx++;
            pos += len;
        }
    }

    qsort(entries, idx, sizeof(SwLocEntry), SwLocCmpEntry);

    uint32_t unique = 0;
    for (uint32_t i = 0; i < idx; i++) {
        if (unique > 0 && entries[unique - 1].id == entries[i].id) {
            entries[unique - 1] = entries[i];
        } else {
            entries[unique++] = entries[i];
        }
    }

    out->entries = entries;
    out->count = unique;
    out->pool = pool;
    out->pool_size = pool_bytes;
    return 0;
}

static inline int SwLocBuildMultiDiff(
    const SwRawTextView* pack_views,
    const SwRawTextView* base_views,
    uint32_t pair_count,
    SwLocTable* out,
    uint32_t* out_preserved_fc)
{
    return SwLocBuildMultiDiffWithProtectedBases(
        pack_views, base_views, pair_count, base_views, pair_count, out, out_preserved_fc);
}

/* Single-view helper wrapper. */
static inline int SwLocBuildDiff(
    const SwRawTextView* pack,
    const SwRawTextView* base,
    SwLocTable* out,
    uint32_t* out_preserved_fc)
{
    if (pack == NULL || base == NULL || pack->count == 0) {
        memset(out, 0, sizeof *out);
        if (out_preserved_fc != NULL) *out_preserved_fc = 0;
        return 0;
    }
    return SwLocBuildMultiDiff(pack, base, 1u, out, out_preserved_fc);
}

static inline const char* SwLocLookup(const SwLocTable* tbl, uint32_t id) {
    if (tbl == NULL || tbl->count == 0) return NULL;
    uint32_t lo = 0, hi = tbl->count;
    while (lo < hi) {
        uint32_t mid = lo + (hi - lo) / 2u;
        uint32_t k = tbl->entries[mid].id;
        if (k == id) return tbl->entries[mid].text;
        if (k < id) lo = mid + 1u;
        else hi = mid;
    }
    return NULL;
}

/* Parses strictly "9ba626afa44a3aa3.patch_<digits>" (no companion suffixes). Returns patch index or -1. */
static inline int SwLocParsePatchFilename(const char* filename) {
    if (filename == NULL) return -1;
    const size_t plen = strlen(SW_LOC_ARCHIVE_PREFIX);
    for (size_t i = 0; i < plen; i++) {
        if (tolower((unsigned char)filename[i]) != tolower((unsigned char)SW_LOC_ARCHIVE_PREFIX[i])) {
            return -1;
        }
    }
    const char* digits = filename + plen;
    if (digits[0] == '\0') return -1;
    int val = 0;
    for (const char* p = digits; *p != '\0'; p++) {
        if (*p < '0' || *p > '9') return -1;
        if (val > 100000) return -1;
        val = val * 10 + (*p - '0');
    }
    return val;
}

typedef struct SwPatchCandidate {
    int index;
    char path[MAX_PATH];
} SwPatchCandidate;

static int SwLocCmpPatch(const void* a, const void* b) {
    const SwPatchCandidate* pa = (const SwPatchCandidate*)a;
    const SwPatchCandidate* pb = (const SwPatchCandidate*)b;
    return pa->index - pb->index;
}

/* Scans <data_dir>\9ba626afa44a3aa3.patch_*, restores multi-table diff if ForgeCredits clobbered pack US TEXTs,
 * and merges curated v2.3 Clones/Empire overrides in both clobbered and unclobbered Star Wars pack configurations. */
static inline int SwLocLoadFromDataDir(const char* data_dir, SwLocTable* out, SwLocLoadReport* report) {
    memset(out, 0, sizeof *out);
    memset(report, 0, sizeof *report);
    report->base_patch_index = -1;
    report->pack_patch_index = -1;
    if (data_dir == NULL || data_dir[0] == '\0') {
        report->status = SW_LOC_ERROR;
        return -1;
    }

    char pattern[MAX_PATH];
    int n = snprintf(pattern, sizeof pattern, "%s\\%s*", data_dir, SW_LOC_ARCHIVE_PREFIX);
    if (n <= 0 || (size_t)n >= sizeof pattern) {
        report->status = SW_LOC_ERROR;
        return -1;
    }

    WIN32_FIND_DATAA fd;
    HANDLE h = FindFirstFileA(pattern, &fd);
    if (h == INVALID_HANDLE_VALUE) {
        report->status = SW_LOC_NO_PATCHES;
        return 0;
    }

    SwPatchCandidate* list = NULL;
    int count = 0;
    int cap = 0;
    do {
        if ((fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) continue;
        int idx = SwLocParsePatchFilename(fd.cFileName);
        if (idx < 0) continue;
        if (count >= cap) {
            int next_cap = cap == 0 ? 32 : cap * 2;
            SwPatchCandidate* grown = (SwPatchCandidate*)realloc(list, (size_t)next_cap * sizeof(SwPatchCandidate));
            if (grown == NULL) {
                free(list);
                FindClose(h);
                report->status = SW_LOC_ERROR;
                return -1;
            }
            list = grown;
            cap = next_cap;
        }
        int pn = snprintf(list[count].path, sizeof list[count].path, "%s\\%s", data_dir, fd.cFileName);
        if (pn > 0 && (size_t)pn < sizeof list[count].path) {
            list[count].index = idx;
            count++;
        }
    } while (FindNextFileA(h, &fd));
    FindClose(h);

    report->scanned_patches = count;
    if (count == 0) {
        free(list);
        report->status = SW_LOC_NO_PATCHES;
        return 0;
    }

    qsort(list, (size_t)count, sizeof(SwPatchCandidate), SwLocCmpPatch);

    /* Find topmost patch containing at least one US English TEXT resource; fail closed on corrupt/overflowed patch. */
    SwRawTextSet base_set;
    memset(&base_set, 0, sizeof base_set);
    int base_pos = -1;
    for (int i = count - 1; i >= 0; i--) {
        int rc = SwLocReadAllUsTextsFromPatch(list[i].path, &base_set);
        if (rc < 0) {
            free(list);
            report->status = SW_LOC_ERROR;
            return -1;
        }
        if (rc == 1) {
            base_pos = i;
            break;
        }
    }

    if (base_pos < 0) {
        free(list);
        report->status = SW_LOC_NO_PATCHES;
        return 0;
    }

    report->base_patch_index = list[base_pos].index;
    uint32_t base_total_strings = 0;
    for (uint32_t b = 0; b < base_set.count; b++) {
        base_total_strings += base_set.views[b].count;
    }
    report->base_string_count = base_total_strings;

    if (!SwLocSetHasForgeCredits(&base_set)) {
        SwLocMode top_mode = SwLocDetectModeFromViews(base_set.views, base_set.count);
        report->mode = top_mode;
        if (top_mode != SW_LOC_MODE_NONE) {
            int cur_rc = SwLocApplyCuratedOverrides(
                top_mode, base_set.views, base_set.count, 1, out, &report->curated_applied, &report->preserved_forge_credits);
            SwLocFreeSet(&base_set);
            free(list);
            if (cur_rc != 0) {
                SwLocFreeTable(out);
                report->status = SW_LOC_ERROR;
                return -1;
            }
            if (out->count > 0) {
                report->pack_patch_index = report->base_patch_index;
                report->pack_string_count = report->base_string_count;
                report->diff_count = out->count;
                report->status = SW_LOC_CURATED_LOADED;
                return 0;
            }
        } else {
            SwLocFreeSet(&base_set);
            free(list);
        }
        report->status = SW_LOC_ALREADY_TOPMOST;
        return 0;
    }

    /* Topmost US TEXT patch is ForgeCredits. For each US TEXT resource in base_set, find highest non-ForgeCredits
     * patch beneath it carrying the same name_hash so all 14 US TEXT tables are restored seamlessly! */
    SwRawTextView* matched_packs = (SwRawTextView*)calloc(base_set.count, sizeof(SwRawTextView));
    SwRawTextView* matched_bases = (SwRawTextView*)calloc(base_set.count, sizeof(SwRawTextView));
    if (matched_packs == NULL || matched_bases == NULL) {
        free(matched_packs);
        free(matched_bases);
        SwLocFreeSet(&base_set);
        free(list);
        report->status = SW_LOC_ERROR;
        return -1;
    }

    uint32_t matched_pairs = 0;
    int max_pack_patch_idx = -1;
    uint32_t total_pack_strings = 0;

    for (uint32_t b = 0; b < base_set.count; b++) {
        uint64_t target_nh = base_set.views[b].name_hash;
        for (int i = base_pos - 1; i >= 0; i--) {
            SwRawTextView cand;
            memset(&cand, 0, sizeof cand);
            int rc = SwLocReadNamedTextFromPatch(list[i].path, target_nh, &cand);
            if (rc == 1) {
                if (!SwLocViewHasForgeCredits(&cand)) {
                    matched_packs[matched_pairs] = cand;
                    matched_bases[matched_pairs] = base_set.views[b];
                    matched_pairs++;
                    if (list[i].index > max_pack_patch_idx) {
                        max_pack_patch_idx = list[i].index;
                    }
                    total_pack_strings += cand.count;
                    break;
                }
                SwLocFreeView(&cand);
            }
        }
    }

    if (matched_pairs == 0) {
        free(matched_packs);
        free(matched_bases);
        SwLocFreeSet(&base_set);
        free(list);
        report->status = SW_LOC_VANILLA_ONLY;
        return 0;
    }

    report->pack_patch_index = max_pack_patch_idx;
    report->pack_string_count = total_pack_strings;
    report->mode = SwLocDetectModeFromViews(matched_packs, matched_pairs);

    int build_rc = SwLocBuildMultiDiffWithProtectedBases(
        matched_packs, matched_bases, matched_pairs, base_set.views, base_set.count, out, &report->preserved_forge_credits);

    if (build_rc == 0 && report->mode != SW_LOC_MODE_NONE) {
        build_rc = SwLocApplyCuratedOverrides(
            report->mode, base_set.views, base_set.count, 0, out, &report->curated_applied, &report->preserved_forge_credits);
    }

    for (uint32_t m = 0; m < matched_pairs; m++) {
        SwLocFreeView(&matched_packs[m]);
    }
    free(matched_packs);
    free(matched_bases);
    SwLocFreeSet(&base_set);
    free(list);

    if (build_rc != 0) {
        SwLocFreeTable(out);
        report->status = SW_LOC_ERROR;
        return -1;
    }

    report->diff_count = out->count;
    report->status = out->count > 0 ? SW_LOC_DIFF_LOADED : SW_LOC_VANILLA_ONLY;
    return 0;
}

#endif /* SW_LOC_TABLE_H */
