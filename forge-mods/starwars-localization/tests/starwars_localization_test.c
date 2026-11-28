/* Offline C11 unit & real-archive verification suite for starwars-localization 0.2.0. */
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

static size_t BodyLengthWithoutVanillaTag(const char* s) {
    if (s == NULL) return 0u;
    size_t len = strlen(s);
    const char* rep = strstr(s, " (Replaces ");
    if (rep != NULL && strchr(rep, ')') != NULL) {
        size_t pos = (size_t)(rep - s);
        if (pos < len) len = pos;
    }
    const char* van = strstr(s, " [Vanilla: ");
    if (van != NULL && strchr(van, ']') != NULL) {
        size_t pos = (size_t)(van - s);
        if (pos < len) len = pos;
    }
    return len;
}

static int HasStandaloneSeafLeak(const char* s) {
    if (s == NULL) return 0;
    size_t limit = BodyLengthWithoutVanillaTag(s);
    for (size_t idx = 0u; idx < limit; idx++) {
        const char* p = s + idx;
        if ((idx == 0u || !isalnum((unsigned char)p[-1])) &&
            idx + 4u <= limit &&
            tolower((unsigned char)p[0]) == 's' &&
            tolower((unsigned char)p[1]) == 'e' &&
            tolower((unsigned char)p[2]) == 'a' &&
            tolower((unsigned char)p[3]) == 'f' &&
            (idx + 4u == limit || !isalnum((unsigned char)p[4]))) {
            return 1;
        }
        if ((idx == 0u || !isalnum((unsigned char)p[-1])) &&
            idx + 7u <= limit &&
            tolower((unsigned char)p[0]) == 's' && p[1] == '.' &&
            tolower((unsigned char)p[2]) == 'e' && p[3] == '.' &&
            tolower((unsigned char)p[4]) == 'a' && p[5] == '.' &&
            tolower((unsigned char)p[6]) == 'f' &&
            (idx + 7u == limit || !isalnum((unsigned char)p[7]))) {
            return 1;
        }
    }
    return 0;
}

static int HasStandaloneDssLeak(const char* s) {
    if (s == NULL) return 0;
    size_t limit = BodyLengthWithoutVanillaTag(s);
    for (size_t idx = 0u; idx < limit; idx++) {
        const char* p = s + idx;
        if ((idx == 0u || !isalnum((unsigned char)p[-1])) &&
            idx + 3u <= limit &&
            tolower((unsigned char)p[0]) == 'd' &&
            tolower((unsigned char)p[1]) == 's' &&
            tolower((unsigned char)p[2]) == 's' &&
            (idx + 3u == limit || !isalnum((unsigned char)p[3]))) {
            return 1;
        }
    }
    if (SwLocContainsCI(s, "democracy space station")) return 1;
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

static void TestCuratedTablesInvariants(void) {
    CHECK(SW_EMPIRE_CURATED_COUNT >= 100u, "Empire curated table covers DSS, TIE, E-Web, boosters, and Hellbombs");
    CHECK(SW_CLONES_CURATED_COUNT >= 20u, "Clones curated table covers Gonk Droid Hellbomb, boosters, walkers, and FRV");

    for (uint32_t i = 0; i < SW_EMPIRE_CURATED_COUNT; i++) {
        if (i > 0) {
            CHECK(kSwEmpireCuratedOverrides[i].id > kSwEmpireCuratedOverrides[i - 1].id,
                "kSwEmpireCuratedOverrides strictly sorted ascending");
        }
        CHECK(kSwEmpireCuratedOverrides[i].id != SW_LOC_WARBOND_LOCKED_ID, "Empire curated never touches WARBOND LOCKED");
        CHECK(!SwLocContainsCI(kSwEmpireCuratedOverrides[i].text, "forge credit"), "Empire curated never touches Forge Credits");
        CHECK(!HasStandaloneSeafLeak(kSwEmpireCuratedOverrides[i].text), "Empire curated has 0 SEAF leaks");
        CHECK(!HasStandaloneDssLeak(kSwEmpireCuratedOverrides[i].text), "Empire curated has 0 DSS / Democracy Space Station leaks");
    }

    for (uint32_t i = 0; i < SW_CLONES_CURATED_COUNT; i++) {
        if (i > 0) {
            CHECK(kSwClonesCuratedOverrides[i].id > kSwClonesCuratedOverrides[i - 1].id,
                "kSwClonesCuratedOverrides strictly sorted ascending");
        }
        CHECK(kSwClonesCuratedOverrides[i].id != SW_LOC_WARBOND_LOCKED_ID, "Clones curated never touches WARBOND LOCKED");
        CHECK(!SwLocContainsCI(kSwClonesCuratedOverrides[i].text, "forge credit"), "Clones curated never touches Forge Credits");
        CHECK(!HasStandaloneSeafLeak(kSwClonesCuratedOverrides[i].text), "Clones curated has 0 SEAF leaks");
    }
}

static void TestSyntheticWorkflows(const char* fixture_dir) {
    char p1[MAX_PATH], p2[MAX_PATH], p3[MAX_PATH];
    SwLocTable tbl;
    SwLocLoadReport rep;

    /* 1. Empty directory -> NO_PATCHES */
    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "empty dir succeeds");
    CHECK(rep.status == SW_LOC_NO_PATCHES && tbl.count == 0, "empty dir reports NO_PATCHES");
    SwLocFreeTable(&tbl);

    /* 2. Generic non-Star-Wars modpack patch alone -> ALREADY_TOPMOST (0 hooks) */
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
    CHECK(rep.status == SW_LOC_ALREADY_TOPMOST && tbl.count == 0, "generic unclobbered pack needs 0 hooks");
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
    SwRawTextSet multi_set;
    memset(&multi_set, 0, sizeof multi_set);
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
    SwRawTextSet over_set;
    memset(&over_set, 0, sizeof over_set);
    CHECK(SwLocReadAllUsTextsFromPatch(p_over, &over_set) == -1 && over_set.count == 0u,
        ">64 US tables fails closed with -1 instead of silently truncating");
    DeleteFileA(p_over);

    /* 8. Remove pack patch_39 (Regular mode with ForgeCredits only) -> VANILLA_ONLY */
    DeleteFileA(p1);
    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "vanilla+fc scan succeeds");
    CHECK(rep.status == SW_LOC_VANILLA_ONLY && tbl.count == 0, "Regular mode with ForgeCredits installs 0 hooks");
    SwLocFreeTable(&tbl);
    DeleteFileA(p2);
    DeleteFileA(p3);
}

static void TestCuratedClonesAndEmpireWorkflows(const char* fixture_dir) {
    char p_pack[MAX_PATH], p_fc[MAX_PATH];
    snprintf(p_pack, sizeof p_pack, "%s\\9ba626afa44a3aa3.patch_38", fixture_dir);
    snprintf(p_fc, sizeof p_fc, "%s\\9ba626afa44a3aa3.patch_99", fixture_dir);

    SwLocTable tbl;
    SwLocLoadReport rep;

    /* A. Clonedivers pack TOPMOST (no forge-credits above it) -> SW_LOC_CURATED_LOADED serves Overloaded Gonk Droid! */
    const KvPair clones_ui[] = {
        { SW_LOC_MODE_PROBE_ID, "Republic Artillery: Explosive" },
        { 0x0772BEDBu, "Proton Bomb" },
        { 0xEDC80AFCu, "PROTON BOMB" },
        { 0xC9B6BF68u, "The Proton Bomb is a tactical nuke." },
    };
    CHECK(WriteSyntheticTextPatch(p_pack, clones_ui, 4u, 1) == 0, "wrote Clonedivers topmost patch_38");
    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "Clonedivers topmost scan succeeds");
    CHECK(rep.status == SW_LOC_CURATED_LOADED && rep.mode == SW_LOC_MODE_CLONES, "Clonedivers topmost loads curated overrides");
    CHECK(rep.curated_applied == SW_CLONES_CURATED_COUNT, "all Clones curated overrides loaded");
    CHECK(SwLocLookup(&tbl, 0x0772BEDBu) != NULL && strcmp(SwLocLookup(&tbl, 0x0772BEDBu), "Overloaded Gonk Droid") == 0,
        "Clonedivers Hellbomb title overridden from Proton Bomb to Overloaded Gonk Droid");
    CHECK(SwLocLookup(&tbl, 0xEDC80AFCu) != NULL && strcmp(SwLocLookup(&tbl, 0xEDC80AFCu), "OVERLOADED GONK DROID") == 0,
        "Clonedivers HELLBOMB caps overridden to OVERLOADED GONK DROID");
    CHECK(SwLocLookup(&tbl, 0xC9B6BF68u) != NULL && strstr(SwLocLookup(&tbl, 0xC9B6BF68u), "[Vanilla: NUX-223 Hellbomb]") != NULL,
        "Clonedivers Hellbomb description carries [Vanilla: NUX-223 Hellbomb]");
    CHECK(SwLocLookup(&tbl, 0x83BAA715u) != NULL && strstr(SwLocLookup(&tbl, 0x83BAA715u), "B-100 Portable Hellbomb") != NULL,
        "Clonedivers Portable Proton Bomb carries vanilla B-100 reference");
    CHECK(SwLocLookup(&tbl, 0x7A343F36u) != NULL && strstr(SwLocLookup(&tbl, 0x7A343F36u), "Firebomb Hellpods") != NULL,
        "Clonedivers Firebomb Drop Pods carries vanilla Firebomb Hellpods reference");
    SwLocFreeTable(&tbl);

    /* B. Clonedivers pack CLOBBERED by forge-credits -> SW_LOC_DIFF_LOADED merges pack diffs + curated overrides! */
    const KvPair fc_base[] = {
        { SW_LOC_MODE_PROBE_ID, "SEAF Artillery: Explosive" },
        { 0x0772BEDBu, "Hellbomb" },
        { 0x300u, "Forge Credits" },
        { SW_LOC_WARBOND_LOCKED_ID, "WARBOND LOCKED" },
    };
    CHECK(WriteSyntheticTextPatch(p_fc, fc_base, 4u, 1) == 0, "wrote forge-credits patch_99 above Clones");
    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "Clonedivers clobbered scan succeeds");
    CHECK(rep.status == SW_LOC_DIFF_LOADED && rep.mode == SW_LOC_MODE_CLONES, "Clonedivers clobbered loads diff + curated");
    CHECK(SwLocLookup(&tbl, SW_LOC_MODE_PROBE_ID) != NULL &&
          strcmp(SwLocLookup(&tbl, SW_LOC_MODE_PROBE_ID), "Republic Artillery: Explosive") == 0,
          "Clones pack diff restored alongside curated overrides");
    CHECK(SwLocLookup(&tbl, 0x0772BEDBu) != NULL &&
          strcmp(SwLocLookup(&tbl, 0x0772BEDBu), "Overloaded Gonk Droid") == 0,
          "Curated Overloaded Gonk Droid beats pack Proton Bomb in two-pointer merge");
    CHECK(SwLocLookup(&tbl, 0x300u) == NULL && SwLocLookup(&tbl, SW_LOC_WARBOND_LOCKED_ID) == NULL,
          "Forge Credits & WARBOND LOCKED remain protected");
    SwLocFreeTable(&tbl);

    /* C. EmpireDivers pack CLOBBERED by forge-credits -> Death Star, TIE descriptions, E-Web, and [Vanilla: NUX-223 Hellbomb]! */
    const KvPair empire_ui[] = {
        { SW_LOC_MODE_PROBE_ID, "Imperial Artillery: Explosive" },
        { 0x0772BEDBu, "Proton Bomb" },
        { 0x130F2257u, "DEMOCRACY SPACE STATION" },
        { 0x19573AB4u, "EAGLE STORM" },
    };
    CHECK(WriteSyntheticTextPatch(p_pack, empire_ui, 4u, 1) == 0, "wrote EmpireDivers patch_38");
    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "EmpireDivers clobbered scan succeeds");
    CHECK(rep.status == SW_LOC_DIFF_LOADED && rep.mode == SW_LOC_MODE_EMPIRE, "EmpireDivers clobbered loads diff + curated");
    CHECK(SwLocLookup(&tbl, 0x0772BEDBu) != NULL && strcmp(SwLocLookup(&tbl, 0x0772BEDBu), "Proton Bomb") == 0,
        "EmpireDivers keeps Proton Bomb display title");
    CHECK(SwLocLookup(&tbl, 0xC9B6BF68u) != NULL && strstr(SwLocLookup(&tbl, 0xC9B6BF68u), "[Vanilla: NUX-223 Hellbomb]") != NULL,
        "EmpireDivers Proton Bomb description carries [Vanilla: NUX-223 Hellbomb]");
    CHECK(SwLocLookup(&tbl, 0x130F2257u) != NULL && strcmp(SwLocLookup(&tbl, 0x130F2257u), "DS-1 DEATH STAR") == 0,
        "EmpireDivers DSS title -> DS-1 DEATH STAR");
    CHECK(SwLocLookup(&tbl, 0x8C431589u) != NULL && strcmp(SwLocLookup(&tbl, 0x8C431589u), "DS-1 Orbital Battle Station (Death Star)") == 0,
        "EmpireDivers Democracy Space Station -> DS-1 Orbital Battle Station (Death Star)");
    CHECK(SwLocLookup(&tbl, 0x19573AB4u) != NULL && strcmp(SwLocLookup(&tbl, 0x19573AB4u), "TIE FIGHTER SQUADRON SWEEP") == 0,
        "EmpireDivers EAGLE STORM -> TIE FIGHTER SQUADRON SWEEP");
    CHECK(SwLocLookup(&tbl, 0x3299ACE2u) != NULL && strcmp(SwLocLookup(&tbl, 0x3299ACE2u), "IMPERIAL FLEET BLOCKADE") == 0,
        "EmpireDivers ORBITAL BLOCKADE -> IMPERIAL FLEET BLOCKADE");
    CHECK(SwLocLookup(&tbl, 0x8ED98C34u) != NULL && strcmp(SwLocLookup(&tbl, 0x8ED98C34u), "SUPERLASER & TURBOLASER SUPPORT") == 0,
        "EmpireDivers HEAVY ORDNANCE DISTRIBUTION -> SUPERLASER & TURBOLASER SUPPORT");
    CHECK(SwLocLookup(&tbl, 0x358735B7u) != NULL && strstr(SwLocLookup(&tbl, 0x358735B7u), "(Replaces Eagle 500kg Bomb)") != NULL,
        "EmpireDivers TIE 500kg Bomb description carries (Replaces Eagle 500kg Bomb)");
    CHECK(SwLocLookup(&tbl, 0x87DB3F84u) != NULL && strstr(SwLocLookup(&tbl, 0x87DB3F84u), "(Replaces E/MG-101 HMG Emplacement)") != NULL,
        "EmpireDivers E-Web Emplacement description carries (Replaces E/MG-101 HMG Emplacement)");
    SwLocFreeTable(&tbl);

    /* D. EmpireDivers pack TOPMOST (no forge-credits) -> SW_LOC_CURATED_LOADED serves Death Star + TIE descriptions! */
    DeleteFileA(p_fc);
    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "EmpireDivers topmost scan succeeds");
    CHECK(rep.status == SW_LOC_CURATED_LOADED && rep.mode == SW_LOC_MODE_EMPIRE, "EmpireDivers topmost loads curated overrides");
    CHECK(SwLocLookup(&tbl, 0x130F2257u) != NULL && strcmp(SwLocLookup(&tbl, 0x130F2257u), "DS-1 DEATH STAR") == 0,
        "EmpireDivers topmost still serves DS-1 DEATH STAR");
    SwLocFreeTable(&tbl);
    DeleteFileA(p_pack);
}

static void AuditRealPackArchive(const char* label, const char* path, SwLocMode expected_mode, const char* expected_artillery) {
    SwRawTextSet set;
    memset(&set, 0, sizeof set);
    int rc = SwLocReadAllUsTextsFromPatch(path, &set);
    if (rc != 1) {
        printf("INFO: skipped optional real archive %s (rc=%d)\n", label, rc);
        return;
    }

    CHECK(set.count >= 14u, "real localization archive carries >= 14 US English TEXT tables");
    CHECK(SwLocDetectModeFromViews(set.views, set.count) == expected_mode, "real archive mode detected accurately");

    uint32_t total_strings = 0;
    uint32_t seaf_leaks = 0;
    const char* art_str = SwLocRawLookupAcrossViews(set.views, set.count, SW_LOC_MODE_PROBE_ID);

    for (uint32_t t = 0; t < set.count; t++) {
        const SwRawTextView* v = &set.views[t];
        total_strings += v->count;
        for (uint32_t i = 0; i < v->count; i++) {
            const char* s = (const char*)(v->blob + v->offsets[i]);
            if (HasStandaloneSeafLeak(s)) {
                seaf_leaks++;
            }
        }
    }

    CHECK(seaf_leaks == 0u, "zero SEAF / S.E.A.F leaks across real localization archive");
    CHECK(art_str != NULL && strcmp(art_str, expected_artillery) == 0, "40455008 Artillery shell title localized");

    printf("INFO: verified %s (%u tables, %u strings, seaf_leaks=%u, 40455008='%s')\n",
        label, set.count, total_strings, seaf_leaks, art_str ? art_str : "<null>");

    SwLocFreeSet(&set);
}

int main(int argc, char** argv) {
    if (argc < 2) {
        fprintf(stderr, "Usage: %s <fixture_dir>\n", argv[0]);
        return 2;
    }
    TestFilenameParsing();
    TestCuratedTablesInvariants();
    TestSyntheticWorkflows(argv[1]);
    TestCuratedClonesAndEmpireWorkflows(argv[1]);
    AuditRealPackArchive(
        "clones-r26-patch_38",
        "D:\\ClaudeWork\\swarm-packs\\clones-r26\\staged\\all-droids\\full\\9ba626afa44a3aa3.patch_38",
        SW_LOC_MODE_CLONES,
        "Republic Artillery: Explosive");
    if (g_failed != 0) {
        fprintf(stderr, "FAILED %d / %d checks\n", g_failed, g_checks);
        return 1;
    }
    printf("PASS %d checks\n", g_checks);
    return 0;
}
