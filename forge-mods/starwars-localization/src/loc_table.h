/* Star Wars Localization Bridge — Stingray 0xF0000011 + TEXT (0x0d972bab10b40fd3)
 * archive parser and ForgeCredits-preserving diff table. Pure C11 + Win32 FindFirstFileA. */
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

#define SW_LOC_ARCHIVE_MAGIC 0xF0000011u
#define SW_LOC_US_TEXT_NAME  0x7C7587B563F10985ULL
#define SW_LOC_TEXT_TYPE     0x0D972BAB10B40FD3ULL
#define SW_LOC_MAX_TYPES     100000u
#define SW_LOC_MAX_FILES     1000000u
#define SW_LOC_MAX_TEXT_BYTES (16u * 1024u * 1024u)
#define SW_LOC_ARCHIVE_PREFIX "9ba626afa44a3aa3.patch_"

typedef enum SwLocStatus {
    SW_LOC_NO_PATCHES = 0,
    SW_LOC_ALREADY_TOPMOST = 1,
    SW_LOC_VANILLA_ONLY = 2,
    SW_LOC_DIFF_LOADED = 3,
    SW_LOC_ERROR = 4
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
    int scanned_patches;
    int base_patch_index;
    int pack_patch_index;
    uint32_t base_string_count;
    uint32_t pack_string_count;
    uint32_t diff_count;
    uint32_t preserved_forge_credits;
} SwLocLoadReport;

typedef struct SwRawTextView {
    uint8_t* blob;
    size_t size;
    uint32_t count;
    const uint32_t* keys;
    const uint32_t* offsets;
    int keys_sorted;
} SwRawTextView;

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

static inline void SwLocFreeTable(SwLocTable* tbl) {
    if (tbl == NULL) return;
    free(tbl->entries);
    free(tbl->pool);
    memset(tbl, 0, sizeof *tbl);
}

/* Extracts US English TEXT (0x7c7587b563f10985 / 0x0d972bab10b40fd3) from a Stingray 0xF0000011 patch file.
 * Returns 1 if found and valid, 0 if not present, -1 on I/O or format error. */
static inline int SwLocReadUsTextFromPatch(const char* patch_path, SwRawTextView* out) {
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
        if (name_hash == SW_LOC_US_TEXT_NAME && type_hash == SW_LOC_TEXT_TYPE) {
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

    uint32_t count = SwLocReadU32(blob + 8);
    uint64_t table_end = 16ULL + 8ULL * (uint64_t)count;
    if (table_end > (uint64_t)main_size) {
        free(blob);
        return -1;
    }

    const uint32_t* keys = (const uint32_t*)(blob + 16);
    const uint32_t* offsets = (const uint32_t*)(blob + 16 + 4u * count);
    int sorted = 1;
    for (uint32_t i = 0; i < count; i++) {
        if (i > 0 && keys[i] < keys[i - 1]) sorted = 0;
        uint32_t off = offsets[i];
        if (off >= main_size) {
            free(blob);
            return -1;
        }
        /* Ensure NUL terminator exists within blob bounds. */
        const uint8_t* nul = (const uint8_t*)memchr(blob + off, 0, main_size - off);
        if (nul == NULL) {
            free(blob);
            return -1;
        }
    }

    out->blob = blob;
    out->size = main_size;
    out->count = count;
    out->keys = keys;
    out->offsets = offsets;
    out->keys_sorted = sorted;
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

static inline int SwLocViewHasForgeCredits(const SwRawTextView* view) {
    if (view == NULL) return 0;
    for (uint32_t i = 0; i < view->count; i++) {
        const char* s = (const char*)(view->blob + view->offsets[i]);
        if (SwLocContainsCI(s, "forge credit")) return 1;
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

/* Builds sorted diff table (pack - base) while preserving every baseline string that mentions "forge credit". */
static inline int SwLocBuildDiff(
    const SwRawTextView* pack,
    const SwRawTextView* base,
    SwLocTable* out,
    uint32_t* out_preserved_fc)
{
    memset(out, 0, sizeof *out);
    if (out_preserved_fc != NULL) *out_preserved_fc = 0;
    if (pack == NULL || base == NULL || pack->count == 0) return 0;

    uint32_t diff_count = 0;
    size_t pool_bytes = 0;
    uint32_t preserved_fc = 0;

    for (uint32_t i = 0; i < pack->count; i++) {
        uint32_t id = pack->keys[i];
        const char* pstr = (const char*)(pack->blob + pack->offsets[i]);
        if (pstr[0] == '\0') continue;
        const char* bstr = SwLocRawLookup(base, id);
        if (bstr != NULL && strcmp(pstr, bstr) == 0) continue;
        if (bstr != NULL && SwLocContainsCI(bstr, "forge credit")) {
            preserved_fc++;
            continue;
        }
        diff_count++;
        pool_bytes += strlen(pstr) + 1u;
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
    for (uint32_t i = 0; i < pack->count; i++) {
        uint32_t id = pack->keys[i];
        const char* pstr = (const char*)(pack->blob + pack->offsets[i]);
        if (pstr[0] == '\0') continue;
        const char* bstr = SwLocRawLookup(base, id);
        if (bstr != NULL && strcmp(pstr, bstr) == 0) continue;
        if (bstr != NULL && SwLocContainsCI(bstr, "forge credit")) continue;
        size_t len = strlen(pstr) + 1u;
        memcpy(pool + pos, pstr, len);
        entries[idx].id = id;
        entries[idx].text = pool + pos;
        idx++;
        pos += len;
    }

    qsort(entries, idx, sizeof(SwLocEntry), SwLocCmpEntry);

    /* Deduplicate if the source TEXT had duplicate keys (keep last). */
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

/* Scans <data_dir>\9ba626afa44a3aa3.patch_* and loads diff table if ForgeCredits clobbered a pack TEXT patch. */
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

    /* Find topmost patch containing US TEXT. */
    SwRawTextView base_view = {0};
    int base_pos = -1;
    for (int i = count - 1; i >= 0; i--) {
        int rc = SwLocReadUsTextFromPatch(list[i].path, &base_view);
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
    report->base_string_count = base_view.count;

    if (!SwLocViewHasForgeCredits(&base_view)) {
        SwLocFreeView(&base_view);
        free(list);
        report->status = SW_LOC_ALREADY_TOPMOST;
        return 0;
    }

    /* Topmost US TEXT is ForgeCredits. Find highest non-ForgeCredits US TEXT patch underneath it. */
    SwRawTextView pack_view = {0};
    int pack_pos = -1;
    for (int i = base_pos - 1; i >= 0; i--) {
        SwRawTextView cand = {0};
        int rc = SwLocReadUsTextFromPatch(list[i].path, &cand);
        if (rc == 1) {
            if (!SwLocViewHasForgeCredits(&cand)) {
                pack_view = cand;
                pack_pos = i;
                break;
            }
            SwLocFreeView(&cand);
        }
    }

    if (pack_pos < 0) {
        SwLocFreeView(&base_view);
        free(list);
        report->status = SW_LOC_VANILLA_ONLY;
        return 0;
    }

    report->pack_patch_index = list[pack_pos].index;
    report->pack_string_count = pack_view.count;

    int build_rc = SwLocBuildDiff(&pack_view, &base_view, out, &report->preserved_forge_credits);
    SwLocFreeView(&pack_view);
    SwLocFreeView(&base_view);
    free(list);

    if (build_rc != 0) {
        report->status = SW_LOC_ERROR;
        return -1;
    }

    report->diff_count = out->count;
    report->status = out->count > 0 ? SW_LOC_DIFF_LOADED : SW_LOC_VANILLA_ONLY;
    return 0;
}

#endif /* SW_LOC_TABLE_H */
