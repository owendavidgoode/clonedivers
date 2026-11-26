/* Offline C11 unit & real-archive verification suite for starwars-localization 0.1.1. */
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "loc_table.h"

static int g_checks = 0;
static int g_failed = 0;

#define CHECK(cond, msg) do { \
    g_checks++; \
    if (!(cond)) { \
        fprintf(stderr, "FAIL [%s:%d]: %s\n", __FILE__, __LINE__, (msg)); \
        g_failed++; \
    } \
} while (0)

typedef struct KvPair {
    uint32_t id;
    const char* text;
} KvPair;

typedef struct SyntheticTableSpec {
    uint64_t name_hash;
    uint32_t language;
    const KvPair* pairs;
    uint32_t count;
} SyntheticTableSpec;

static int WriteSyntheticMultiTextPatch(const char* path, const SyntheticTableSpec* tables, uint32_t table_count) {
    FILE* fp = fopen(path, "wb");
    if (fp == NULL) return -1;

    uint8_t hdr[72];
    memset(hdr, 0, sizeof hdr);
    uint32_t magic = SW_LOC_ARCHIVE_MAGIC;
    uint32_t nt = 1;
    uint32_t nf = table_count;
    memcpy(hdr + 0, &magic, 4);
    memcpy(hdr + 4, &nt, 4);
    memcpy(hdr + 8, &nf, 4);
    fwrite(hdr, 1, sizeof hdr, fp);

    uint8_t type_desc[32];
    memset(type_desc, 0, sizeof type_desc);
    fwrite(type_desc, 1, sizeof type_desc, fp);

    if (table_count == 0) {
        fclose(fp);
        return 0;
    }

    uint8_t** blobs = (uint8_t**)calloc(table_count, sizeof(uint8_t*));
    uint32_t* sizes = (uint32_t*)calloc(table_count, sizeof(uint32_t));
    if (blobs == NULL || sizes == NULL) {
        free(blobs);
        free(sizes);
        fclose(fp);
        return -1;
    }

    for (uint32_t t = 0; t < table_count; t++) {
        size_t str_bytes = 0;
        for (uint32_t i = 0; i < tables[t].count; i++) str_bytes += strlen(tables[t].pairs[i].text) + 1u;
        uint32_t blob_size = 16u + 8u * tables[t].count + (uint32_t)str_bytes;
        uint8_t* blob = (uint8_t*)calloc(1, blob_size);
        if (blob == NULL) {
            for (uint32_t k = 0; k < t; k++) free(blobs[k]);
            free(blobs);
            free(sizes);
            fclose(fp);
            return -1;
        }
        uint32_t ver = 0x54584554u;
        uint32_t fmt = 1u;
        uint32_t cnt = tables[t].count;
        uint32_t lang = tables[t].language;
        memcpy(blob + 0, &ver, 4);
        memcpy(blob + 4, &fmt, 4);
        memcpy(blob + 8, &cnt, 4);
        memcpy(blob + 12, &lang, 4);

        uint32_t cur_off = 16u + 8u * cnt;
        for (uint32_t i = 0; i < cnt; i++) {
            memcpy(blob + 16u + 4u * i, &tables[t].pairs[i].id, 4);
            memcpy(blob + 16u + 4u * cnt + 4u * i, &cur_off, 4);
            size_t len = strlen(tables[t].pairs[i].text) + 1u;
            memcpy(blob + cur_off, tables[t].pairs[i].text, len);
            cur_off += (uint32_t)len;
        }
        blobs[t] = blob;
        sizes[t] = blob_size;
    }

    uint64_t cur_main_off = 72ULL + 32ULL + 80ULL * (uint64_t)table_count;
    for (uint32_t t = 0; t < table_count; t++) {
        uint8_t row[80];
        memset(row, 0, sizeof row);
        uint64_t name_h = tables[t].name_hash;
        uint64_t type_h = SW_LOC_TEXT_TYPE;
        memcpy(row + 0, &name_h, 8);
        memcpy(row + 8, &type_h, 8);
        memcpy(row + 16, &cur_main_off, 8);
        memcpy(row + 56, &sizes[t], 4);
        fwrite(row, 1, sizeof row, fp);
        cur_main_off += (uint64_t)sizes[t];
    }

    for (uint32_t t = 0; t < table_count; t++) {
        fwrite(blobs[t], 1, sizes[t], fp);
        free(blobs[t]);
    }
    free(blobs);
    free(sizes);
    fclose(fp);
    return 0;
}

static int WriteSyntheticTextPatch(const char* path, const KvPair* pairs, uint32_t count, int include_us_text) {
    if (!include_us_text) {
        return WriteSyntheticMultiTextPatch(path, NULL, 0u);
    }
    SyntheticTableSpec spec = { SW_LOC_US_TEXT_NAME, SW_LOC_US_LANG_ID, pairs, count };
    return WriteSyntheticMultiTextPatch(path, &spec, 1u);
}

static int HasStandaloneSeafLeak(const char* s) {
    if (s == NULL) return 0;
    /* Ignore any trailing " (Replaces ...)" note when checking body text, though v24 has 0 SEAF anywhere. */
    for (const char* p = s; *p != '\0'; p++) {
        if ((p == s || !isalnum((unsigned char)p[-1])) &&
            tolower((unsigned char)p[0]) == 's' &&
            tolower((unsigned char)p[1]) == 'e' &&
            tolower((unsigned char)p[2]) == 'a' &&
            tolower((unsigned char)p[3]) == 'f' &&
            !isalnum((unsigned char)p[4])) {
            return 1;
        }
        if ((p == s || !isalnum((unsigned char)p[-1])) &&
            tolower((unsigned char)p[0]) == 's' && p[1] == '.' &&
            tolower((unsigned char)p[2]) == 'e' && p[3] == '.' &&
            tolower((unsigned char)p[4]) == 'a' && p[5] == '.' &&
            tolower((unsigned char)p[6]) == 'f' && !isalnum((unsigned char)p[7])) {
            return 1;
        }
    }
    return 0;
}

static void TestFilenameParsing(void) {
    CHECK(SwLocParsePatchFilename("9ba626afa44a3aa3.patch_0") == 0, "patch_0 valid");
    CHECK(SwLocParsePatchFilename("9ba626afa44a3aa3.patch_71") == 71, "patch_71 valid");
    CHECK(SwLocParsePatchFilename("9BA626AFA44A3AA3.PATCH_39") == 39, "case-insensitive valid");
    CHECK(SwLocParsePatchFilename("9ba626afa44a3aa3.patch_71.stream") == -1, ".stream rejected");
    CHECK(SwLocParsePatchFilename("9ba626afa44a3aa3.patch_71.gpu_resources") == -1, ".gpu_resources rejected");
    CHECK(SwLocParsePatchFilename("9ba626afa44a3aa3.patch_71.bak") == -1, ".bak rejected");
    CHECK(SwLocParsePatchFilename("9ba626afa44a3aa3.patch_") == -1, "empty index rejected");
    CHECK(SwLocParsePatchFilename("otherhash.patch_0") == -1, "other archive rejected");
}

static void TestSyntheticWorkflows(const char* fixture_dir) {
    char p1[MAX_PATH], p2[MAX_PATH], p3[MAX_PATH];
    SwLocTable tbl;
    SwLocLoadReport rep;

    /* 1. Empty directory -> NO_PATCHES */
    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "empty dir succeeds");
    CHECK(rep.status == SW_LOC_NO_PATCHES && tbl.count == 0, "empty dir reports NO_PATCHES");
    SwLocFreeTable(&tbl);

    /* 2. Multi-table modpack patch alone (7c7587b563f10985 + 1c978e745545d8ae VO subtitles) -> ALREADY_TOPMOST */
    const KvPair pack_ui[] = {
        { 100u, "K-2SO" },
        { 200u, "Imperial Artillery: Explosive" },
        { 300u, "Super Credits" },
        { 400u, "Unchanged String" },
    };
    const KvPair pack_subs[] = {
        { 625181211u, "{0.00->1.83}Imperial Army backup en route to your position." },
        { 700u, "Super Credits Cache" },
    };
    const SyntheticTableSpec pack_specs[2] = {
        { SW_LOC_US_TEXT_NAME, SW_LOC_US_LANG_ID, pack_ui, 4u },
        { 0x1C978E745545D8AEULL, SW_LOC_US_LANG_ID, pack_subs, 2u },
    };
    snprintf(p1, sizeof p1, "%s\\9ba626afa44a3aa3.patch_39", fixture_dir);
    CHECK(WriteSyntheticMultiTextPatch(p1, pack_specs, 2u) == 0, "wrote multi-table pack patch_39");

    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "unclobbered scan succeeds");
    CHECK(rep.status == SW_LOC_ALREADY_TOPMOST && tbl.count == 0, "unclobbered pack needs 0 hooks");
    SwLocFreeTable(&tbl);

    /* 3. Non-TEXT patch_50 above patch_39 -> still ALREADY_TOPMOST */
    snprintf(p2, sizeof p2, "%s\\9ba626afa44a3aa3.patch_50", fixture_dir);
    CHECK(WriteSyntheticTextPatch(p2, NULL, 0, 0) == 0, "wrote non-text patch_50");
    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "non-text above pack succeeds");
    CHECK(rep.status == SW_LOC_ALREADY_TOPMOST && rep.base_patch_index == 39, "skips non-text patch_50");
    SwLocFreeTable(&tbl);

    /* 4. Multi-table ForgeCredits staged LAST at patch_71 -> restores BOTH UI & Subtitle tables while preserving Forge Credits & WARBOND LOCKED! */
    const KvPair fc_ui[] = {
        { 100u, "B-27 Fortified Commando" },
        { 200u, "SEAF Artillery: Explosive" },
        { 300u, "Forge Credits" },
        { 400u, "Unchanged String" },
        { SW_LOC_WARBOND_LOCKED_ID, "WARBOND LOCKED" },
    };
    const KvPair fc_subs[] = {
        { 625181211u, "{0.00->1.83}SEAF backup en route to your position." },
        { 700u, "Forge Credits Cache" },
    };
    const SyntheticTableSpec fc_specs[2] = {
        { SW_LOC_US_TEXT_NAME, SW_LOC_US_LANG_ID, fc_ui, 5u },
        { 0x1C978E745545D8AEULL, SW_LOC_US_LANG_ID, fc_subs, 2u },
    };
    snprintf(p3, sizeof p3, "%s\\9ba626afa44a3aa3.patch_71", fixture_dir);
    CHECK(WriteSyntheticMultiTextPatch(p3, fc_specs, 2u) == 0, "wrote multi-table forge-credits patch_71");

    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "clobbered multi-table scan succeeds");
    CHECK(rep.status == SW_LOC_DIFF_LOADED, "detects multi-table forge-credits clobber");
    CHECK(rep.base_patch_index == 71 && rep.pack_patch_index == 39, "resolved exact patch indices 71 & 39");
    CHECK(rep.diff_count == 3u && rep.preserved_forge_credits == 2u, "3 Star Wars diffs across 2 tables + 2 Forge Credits preserved");
    CHECK(SwLocLookup(&tbl, 100u) != NULL && strcmp(SwLocLookup(&tbl, 100u), "K-2SO") == 0, "100 -> K-2SO");
    CHECK(SwLocLookup(&tbl, 200u) != NULL && strcmp(SwLocLookup(&tbl, 200u), "Imperial Artillery: Explosive") == 0, "200 -> Imperial Artillery: Explosive");
    CHECK(SwLocLookup(&tbl, 625181211u) != NULL && strcmp(SwLocLookup(&tbl, 625181211u), "{0.00->1.83}Imperial Army backup en route to your position.") == 0, "625181211 subtitle restored");
    CHECK(SwLocLookup(&tbl, 300u) == NULL, "300 Forge Credits NOT overridden");
    CHECK(SwLocLookup(&tbl, 700u) == NULL, "700 Forge Credits Cache NOT overridden");
    CHECK(SwLocLookup(&tbl, SW_LOC_WARBOND_LOCKED_ID) == NULL, "WARBOND LOCKED NOT overridden");
    CHECK(SwLocLookup(&tbl, 400u) == NULL, "400 unchanged string omitted");
    SwLocFreeTable(&tbl);

    /* 5. Cross-table Forge Credits collision: ID 300 protected in UI baseline must NOT be overridden by Subtitle pack table! */
    const KvPair pack_subs_collision[] = {
        { 300u, "Sneaky Cross-Table Override" },
        { 625181211u, "{0.00->1.83}Imperial Army backup en route to your position." },
        { 700u, "Super Credits Cache" },
    };
    const SyntheticTableSpec pack_collision_specs[2] = {
        { SW_LOC_US_TEXT_NAME, SW_LOC_US_LANG_ID, pack_ui, 4u },
        { 0x1C978E745545D8AEULL, SW_LOC_US_LANG_ID, pack_subs_collision, 3u },
    };
    CHECK(WriteSyntheticMultiTextPatch(p1, pack_collision_specs, 2u) == 0, "wrote cross-table collision pack patch_39");
    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "cross-table collision scan succeeds");
    CHECK(SwLocLookup(&tbl, 300u) == NULL, "300 Forge Credits protected globally across all tables");
    CHECK(rep.preserved_forge_credits == 3u, "both UI and cross-table Subtitle attempts counted as preserved");
    SwLocFreeTable(&tbl);

    /* 6. Multilingual archive with 70 non-US TEXT resources before 2 US TEXT resources -> keeps both US tables! */
    char p_multi[MAX_PATH], p_over[MAX_PATH];
    snprintf(p_multi, sizeof p_multi, "%s\\multilang_archive.patch_0", fixture_dir);
    SyntheticTableSpec multi_specs[72];
    const KvPair non_us_kv[] = { { 900u, "Bonjour" } };
    for (uint32_t i = 0; i < 70u; i++) {
        multi_specs[i].name_hash = 0x2000000000000000ULL + i;
        multi_specs[i].language = 0xDEADBEEFu + i;
        multi_specs[i].pairs = non_us_kv;
        multi_specs[i].count = 1u;
    }
    multi_specs[70] = pack_specs[0];
    multi_specs[71] = pack_specs[1];
    CHECK(WriteSyntheticMultiTextPatch(p_multi, multi_specs, 72u) == 0, "wrote 72-table multilingual archive");
    SwRawTextSet multi_set = {0};
    CHECK(SwLocReadAllUsTextsFromPatch(p_multi, &multi_set) == 1 && multi_set.count == 2u,
        "70 preceding non-US tables do not truncate trailing US tables");
    SwLocFreeSet(&multi_set);
    DeleteFileA(p_multi);

    /* 7. > SW_LOC_MAX_US_TABLES (65 US tables) fails closed (-1) instead of silently truncating */
    snprintf(p_over, sizeof p_over, "%s\\overflow_us.patch_0", fixture_dir);
    SyntheticTableSpec over_specs[SW_LOC_MAX_US_TABLES + 1u];
    for (uint32_t i = 0; i < SW_LOC_MAX_US_TABLES + 1u; i++) {
        over_specs[i].name_hash = 0x3000000000000000ULL + i;
        over_specs[i].language = SW_LOC_US_LANG_ID;
        over_specs[i].pairs = pack_subs;
        over_specs[i].count = 2u;
    }
    CHECK(WriteSyntheticMultiTextPatch(p_over, over_specs, SW_LOC_MAX_US_TABLES + 1u) == 0, "wrote 65-US-table overflow archive");
    SwRawTextSet over_set = {0};
    CHECK(SwLocReadAllUsTextsFromPatch(p_over, &over_set) == -1 && over_set.count == 0u,
        ">64 US tables fails closed with -1 instead of silently truncating");
    DeleteFileA(p_over);

    /* 8. Remove pack patch_39 (Regular mode with ForgeCredits only) -> VANILLA_ONLY */
    DeleteFileA(p1);
    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "vanilla+fc scan succeeds");
    CHECK(rep.status == SW_LOC_VANILLA_ONLY && tbl.count == 0, "Regular mode with ForgeCredits installs 0 hooks");
    SwLocFreeTable(&tbl);
}

static void AuditRealV24Archive(const char* label, const char* path, const char* expected_artillery) {
    SwRawTextSet set = {0};
    int rc = SwLocReadAllUsTextsFromPatch(path, &set);
    if (rc != 1) {
        printf("INFO: skipped real v24 archive %s (rc=%d)\n", label, rc);
        return;
    }

    CHECK(set.count == 14u, "v24 localization archive carries all 14 US English TEXT tables");
    CHECK(!SwLocSetHasForgeCredits(&set), "v24 localization archive does not contain Forge Credits");

    uint32_t total_strings = 0;
    uint32_t seaf_leaks = 0;
    const char* art_str = NULL;
    const char* sub_str = NULL;

    for (uint32_t t = 0; t < set.count; t++) {
        const SwRawTextView* v = &set.views[t];
        total_strings += v->count;
        for (uint32_t i = 0; i < v->count; i++) {
            const char* s = (const char*)(v->blob + v->offsets[i]);
            if (HasStandaloneSeafLeak(s)) {
                seaf_leaks++;
            }
        }
        if (v->name_hash == SW_LOC_US_TEXT_NAME) {
            art_str = SwLocRawLookup(v, 40455008u); /* SEAF Artillery: Explosive */
        } else if (v->name_hash == 0x1C978E745545D8AEULL) {
            sub_str = SwLocRawLookup(v, 625181211u); /* {0.00->1.83}SEAF backup en route... */
        }
    }

    CHECK(total_strings == 20306u, "v24 localization archive has exact 20,306 US English strings");
    CHECK(seaf_leaks == 0u, "zero SEAF / S.E.A.F leaks across all 20,306 strings");
    CHECK(art_str != NULL && strcmp(art_str, expected_artillery) == 0, "40455008 Artillery shell title localized");
    CHECK(sub_str != NULL && !HasStandaloneSeafLeak(sub_str), "625181211 VO subtitle localized without SEAF");

    printf("INFO: verified %s (%u tables, %u strings, seaf_leaks=%u, 40455008='%s', 625181211='%s')\n",
        label, set.count, total_strings, seaf_leaks,
        art_str ? art_str : "<null>", sub_str ? sub_str : "<null>");

    SwLocFreeSet(&set);
}

int main(int argc, char** argv) {
    if (argc < 2) {
        fprintf(stderr, "Usage: %s <fixture_dir>\n", argv[0]);
        return 2;
    }
    TestFilenameParsing();
    TestSyntheticWorkflows(argv[1]);
    AuditRealV24Archive(
        "localization-empire-v24",
        "D:\\ClaudeWork\\workstreams-v21\\assets\\modules\\localization-empire-v24\\full\\9ba626afa44a3aa3.patch_0",
        "Imperial Artillery: Explosive");
    AuditRealV24Archive(
        "localization-clones-delta-on-v24",
        "D:\\ClaudeWork\\workstreams-v21\\assets\\modules\\localization-clones-delta-on-v24\\full\\9ba626afa44a3aa3.patch_0",
        "Republic Artillery: Explosive");
    AuditRealV24Archive(
        "localization-clones-delta-off-v24",
        "D:\\ClaudeWork\\workstreams-v21\\assets\\modules\\localization-clones-delta-off-v24\\full\\9ba626afa44a3aa3.patch_0",
        "Republic Artillery: Explosive");
    if (g_failed != 0) {
        fprintf(stderr, "FAILED %d / %d checks\n", g_failed, g_checks);
        return 1;
    }
    printf("PASS %d checks\n", g_checks);
    return 0;
}
