/* Offline C11 unit & real-archive verification suite for starwars-localization. */
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

static int WriteSyntheticTextPatch(const char* path, const KvPair* pairs, uint32_t count, int include_us_text) {
    FILE* fp = fopen(path, "wb");
    if (fp == NULL) return -1;

    uint8_t hdr[72];
    memset(hdr, 0, sizeof hdr);
    uint32_t magic = SW_LOC_ARCHIVE_MAGIC;
    uint32_t nt = 1;
    uint32_t nf = include_us_text ? 1u : 0u;
    memcpy(hdr + 0, &magic, 4);
    memcpy(hdr + 4, &nt, 4);
    memcpy(hdr + 8, &nf, 4);
    fwrite(hdr, 1, sizeof hdr, fp);

    uint8_t type_desc[32];
    memset(type_desc, 0, sizeof type_desc);
    fwrite(type_desc, 1, sizeof type_desc, fp);

    if (!include_us_text) {
        fclose(fp);
        return 0;
    }

    /* Build TEXT payload */
    size_t str_bytes = 0;
    for (uint32_t i = 0; i < count; i++) str_bytes += strlen(pairs[i].text) + 1u;
    uint32_t blob_size = 16u + 8u * count + (uint32_t)str_bytes;
    uint8_t* blob = (uint8_t*)calloc(1, blob_size);
    if (blob == NULL) { fclose(fp); return -1; }

    uint32_t ver = 0x54584554u;
    uint32_t lang = 1u;
    memcpy(blob + 0, &ver, 4);
    memcpy(blob + 4, &lang, 4);
    memcpy(blob + 8, &count, 4);

    uint32_t cur_off = 16u + 8u * count;
    for (uint32_t i = 0; i < count; i++) {
        memcpy(blob + 16u + 4u * i, &pairs[i].id, 4);
        memcpy(blob + 16u + 4u * count + 4u * i, &cur_off, 4);
        size_t len = strlen(pairs[i].text) + 1u;
        memcpy(blob + cur_off, pairs[i].text, len);
        cur_off += (uint32_t)len;
    }

    uint8_t row[80];
    memset(row, 0, sizeof row);
    uint64_t name_h = SW_LOC_US_TEXT_NAME;
    uint64_t type_h = SW_LOC_TEXT_TYPE;
    uint64_t main_off = 72ULL + 32ULL + 80ULL;
    memcpy(row + 0, &name_h, 8);
    memcpy(row + 8, &type_h, 8);
    memcpy(row + 16, &main_off, 8);
    memcpy(row + 56, &blob_size, 4);
    fwrite(row, 1, sizeof row, fp);
    fwrite(blob, 1, blob_size, fp);
    free(blob);
    fclose(fp);
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

    /* 2. Modpack patch alone (not clobbered) -> ALREADY_TOPMOST */
    const KvPair pack_pairs[] = {
        { 100u, "K-2SO" },
        { 200u, "IMPERIAL NOVA TROOPER" },
        { 300u, "Super Credits" },
        { 400u, "Unchanged String" },
    };
    snprintf(p1, sizeof p1, "%s\\9ba626afa44a3aa3.patch_39", fixture_dir);
    CHECK(WriteSyntheticTextPatch(p1, pack_pairs, 4, 1) == 0, "wrote pack patch_39");

    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "unclobbered scan succeeds");
    CHECK(rep.status == SW_LOC_ALREADY_TOPMOST && tbl.count == 0, "unclobbered pack needs 0 hooks");
    SwLocFreeTable(&tbl);

    /* 3. Non-TEXT patch_50 above patch_39 -> still ALREADY_TOPMOST */
    snprintf(p2, sizeof p2, "%s\\9ba626afa44a3aa3.patch_50", fixture_dir);
    CHECK(WriteSyntheticTextPatch(p2, NULL, 0, 0) == 0, "wrote non-text patch_50");
    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "non-text above pack succeeds");
    CHECK(rep.status == SW_LOC_ALREADY_TOPMOST && rep.base_patch_index == 39, "skips non-text patch_50");
    SwLocFreeTable(&tbl);

    /* 4. ForgeCredits staged LAST at patch_71 -> restores K-2SO & Nova Trooper, preserves Forge Credits! */
    const KvPair fc_pairs[] = {
        { 100u, "B-27 Fortified Commando" },
        { 200u, "RE-824 BEARER OF THE STANDARD" },
        { 300u, "Forge Credits" },
        { 400u, "Unchanged String" },
        { 500u, "FORGE CREDITS PACK" },
    };
    snprintf(p3, sizeof p3, "%s\\9ba626afa44a3aa3.patch_71", fixture_dir);
    CHECK(WriteSyntheticTextPatch(p3, fc_pairs, 5, 1) == 0, "wrote forge-credits patch_71");

    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "clobbered scan succeeds");
    CHECK(rep.status == SW_LOC_DIFF_LOADED, "detects forge-credits clobber");
    CHECK(rep.base_patch_index == 71 && rep.pack_patch_index == 39, "resolved exact patch indices 71 & 39");
    CHECK(rep.diff_count == 2u && rep.preserved_forge_credits == 1u, "2 Star Wars diffs + 1 Forge Credits preserved");
    CHECK(SwLocLookup(&tbl, 100u) != NULL && strcmp(SwLocLookup(&tbl, 100u), "K-2SO") == 0, "100 -> K-2SO");
    CHECK(SwLocLookup(&tbl, 200u) != NULL && strcmp(SwLocLookup(&tbl, 200u), "IMPERIAL NOVA TROOPER") == 0, "200 -> Nova Trooper");
    CHECK(SwLocLookup(&tbl, 300u) == NULL, "300 Forge Credits NOT overridden");
    CHECK(SwLocLookup(&tbl, 400u) == NULL, "400 unchanged string omitted");
    CHECK(SwLocLookup(&tbl, 999u) == NULL, "999 unknown/AddText ID falls through");
    SwLocFreeTable(&tbl);

    /* 5. Remove pack patch_39 (Regular mode with ForgeCredits only) -> VANILLA_ONLY */
    DeleteFileA(p1);
    CHECK(SwLocLoadFromDataDir(fixture_dir, &tbl, &rep) == 0, "vanilla+fc scan succeeds");
    CHECK(rep.status == SW_LOC_VANILLA_ONLY && tbl.count == 0, "Regular mode with ForgeCredits installs 0 hooks");
    SwLocFreeTable(&tbl);
}

static void TestRealArchivesIfPresent(void) {
    const char* fc_path = "D:\\HellForge\\temp\\forge-credits\\26aeb5b374b65371\\9ba626afa44a3aa3.patch_0";
    const char* emp_path = "D:\\ClaudeWork\\workstreams-v21\\assets\\modules\\localization-empire\\full\\9ba626afa44a3aa3.patch_0";

    SwRawTextView fc = {0}, emp = {0};
    int r_fc = SwLocReadUsTextFromPatch(fc_path, &fc);
    int r_emp = SwLocReadUsTextFromPatch(emp_path, &emp);
    if (r_fc != 1 || r_emp != 1) {
        SwLocFreeView(&fc);
        SwLocFreeView(&emp);
        printf("INFO: skipped real archive check (fc=%d emp=%d)\n", r_fc, r_emp);
        return;
    }

    CHECK(SwLocViewHasForgeCredits(&fc) == 1, "real forge-credits archive contains Forge Credits");
    CHECK(SwLocViewHasForgeCredits(&emp) == 0, "real localization-empire archive does not contain Forge Credits");

    SwLocTable tbl;
    uint32_t preserved = 0;
    CHECK(SwLocBuildDiff(&emp, &fc, &tbl, &preserved) == 0, "real diff build succeeds");
    CHECK(tbl.count >= 500u, "real Empire diff has >= 500 Star Wars strings");
    CHECK(preserved >= 1u, "real diff preserved >= 1 Forge Credits string");

    /* Verify K-2SO (id 2574245188 / 1185066006 from localization_diff_empire.csv) is restored! */
    const char* k2_title = SwLocLookup(&tbl, 2574245188u);
    const char* k2_upper = SwLocLookup(&tbl, 1185066006u);
    CHECK(k2_title != NULL && strstr(k2_title, "K-2SO") != NULL, "real diff restores K-2SO title (2574245188)");
    CHECK(k2_upper != NULL && strstr(k2_upper, "K-2SO") != NULL, "real diff restores K-2SO upper (1185066006)");

    printf("INFO: real Empire diff loaded %u overrides (%u Forge Credits preserved); 2574245188='%s'\n",
        tbl.count, preserved, k2_title ? k2_title : "<null>");

    SwLocFreeTable(&tbl);
    SwLocFreeView(&fc);
    SwLocFreeView(&emp);
}

int main(int argc, char** argv) {
    if (argc < 2) {
        fprintf(stderr, "Usage: %s <fixture_dir>\n", argv[0]);
        return 2;
    }
    TestFilenameParsing();
    TestSyntheticWorkflows(argv[1]);
    TestRealArchivesIfPresent();
    if (g_failed != 0) {
        fprintf(stderr, "FAILED %d / %d checks\n", g_failed, g_checks);
        return 1;
    }
    printf("PASS %d checks\n", g_checks);
    return 0;
}
