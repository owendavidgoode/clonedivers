/* Standalone unit tests for clonedivers_host_options.h
 * Verifies folder & .unpacked path resolution, JSON parsing/validation,
 * symlink/oversize rejection, and mtime session-override precedence.
 */
#include "clonedivers_host_options.h"
#include <stdio.h>
#include <stdlib.h>

static unsigned g_checks = 0;
static unsigned g_failed = 0;

#define CHECK(cond, msg) do { \
    ++g_checks; \
    if (!(cond)) { \
        ++g_failed; \
        fprintf(stderr, "FAIL [%s:%d]: %s\n", __FILE__, __LINE__, (msg)); \
    } \
} while (0)

static int write_bytes(const char* path, const void* data, size_t len)
{
    FILE* f = fopen(path, "wb");
    if (!f) return 0;
    size_t wr = len ? fwrite(data, 1, len, f) : 0;
    fclose(f);
    return wr == len;
}

static int set_file_mtime(const char* path, uint64_t mtime_100ns)
{
    HANDLE h = CreateFileA(path, FILE_WRITE_ATTRIBUTES,
                           FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                           NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return 0;
    FILETIME ft;
    ft.dwLowDateTime = (DWORD)(mtime_100ns & 0xFFFFFFFFULL);
    ft.dwHighDateTime = (DWORD)(mtime_100ns >> 32);
    BOOL ok = SetFileTime(h, NULL, NULL, &ft);
    CloseHandle(h);
    return ok != 0;
}

static void test_path_resolution(void)
{
    char out[CD_HOST_OPTIONS_MAX_PATH];

    /* 1. Standard folder install */
    CHECK(cd_resolve_host_options_path("D:\\HellForge\\mods\\chaos-invasions", out, sizeof out) == 1,
          "folder install resolves");
    CHECK(strcmp(out, "D:\\HellForge\\mods\\clonedivers-host-options.json") == 0,
          "folder install exact path");

    /* 2. Folder install with forward slashes & trailing slash */
    CHECK(cd_resolve_host_options_path("D:/HellForge/Mods/faster-corpse-cleanup/", out, sizeof out) == 1,
          "forward slash + case-insensitive Mods resolves");
    CHECK(strcmp(out, "D:\\HellForge\\Mods\\clonedivers-host-options.json") == 0,
          "normalized backslashes on output");

    /* 3. .unpacked zip install (<installRoot>\mods\.unpacked\<hash>\<submod>) */
    CHECK(cd_resolve_host_options_path(
              "D:\\HellForge\\mods\\.unpacked\\009a4c97e30442e392012719ac3de890-6835354fff82\\chaos-invasions",
              out, sizeof out) == 1,
          ".unpacked/<hash>/<submod> resolves");
    CHECK(strcmp(out, "D:\\HellForge\\mods\\clonedivers-host-options.json") == 0,
          ".unpacked/<hash>/<submod> exact path");

    /* 4. .unpacked single-level zip install (<installRoot>\mods\.unpacked\<hash>) */
    CHECK(cd_resolve_host_options_path(
              "C:/Users/goode/AppData/Local/Clonedivers/forge/mods/.UNPACKED/pkg-1234/",
              out, sizeof out) == 1,
          ".UNPACKED/<hash> single-level resolves");
    CHECK(strcmp(out, "C:\\Users\\goode\\AppData\\Local\\Clonedivers\\forge\\mods\\clonedivers-host-options.json") == 0,
          ".UNPACKED/<hash> exact path");

    /* 5. Deeply nested .unpacked layout */
    CHECK(cd_resolve_host_options_path(
              "D:\\HellForge\\mods\\.unpacked\\hash1\\nested\\submod",
              out, sizeof out) == 1,
          "deep .unpacked layout resolves");
    CHECK(strcmp(out, "D:\\HellForge\\mods\\clonedivers-host-options.json") == 0,
          "deep .unpacked exact path");

    /* 6. Reject non-mods parent directories fail-closed */
    CHECK(cd_resolve_host_options_path("D:\\HellForge\\other\\chaos-invasions", out, sizeof out) == 0,
          "reject non-mods parent");
    CHECK(cd_resolve_host_options_path("D:\\HellForge\\other\\.unpacked\\hash\\mod", out, sizeof out) == 0,
          "reject .unpacked under non-mods parent");
    CHECK(cd_resolve_host_options_path("D:\\HellForge\\mods\\.unpacked", out, sizeof out) == 0,
          "reject bare .unpacked with no child");
    CHECK(cd_resolve_host_options_path("", out, sizeof out) == 0, "reject empty mod_folder");
    CHECK(cd_resolve_host_options_path(NULL, out, sizeof out) == 0, "reject NULL mod_folder");
    CHECK(cd_resolve_host_options_path("D:\\HellForge\\mods\\m", out, 12) == 0, "reject tiny output buffer");
}

static void test_json_parsing(void)
{
    CdHostOptions o = cd_host_options_defaults();

    /* Exact HostMutatorConfig indented output */
    const char* valid1 =
        "{\r\n"
        "  \"format\": 1,\r\n"
        "  \"chaosIntensity\": \"total-war\",\r\n"
        "  \"heavySeaf\": \"constant\",\r\n"
        "  \"fasterCorpseCleanup\": false,\r\n"
        "  \"updatedUtc\": \"2026-11-21T23:45:00.1234567+00:00\"\r\n"
        "}";
    CHECK(cd_parse_host_options_json(valid1, strlen(valid1), &o) == 1, "parse valid1");
    CHECK(o.format == 1, "format == 1");
    CHECK(o.chaos_intensity == CD_CHAOS_TOTAL_WAR, "chaosIntensity == total-war");
    CHECK(o.heavy_seaf == CD_HEAVY_SEAF_CONSTANT, "heavySeaf == constant");
    CHECK(o.faster_corpse_cleanup == 0, "fasterCorpseCleanup == false");
    CHECK(o.corpse_delay_seconds == -1, "corpseDelaySeconds unset == -1");

    /* Compact + case-insensitive keys + optional corpseDelaySeconds + UTF-8 BOM + extra nested metadata */
    const char valid2[] =
        "\xEF\xBB\xBF{\"Format\":1,\"ChaosIntensity\":\"off\",\"HeavySeaf\":\"heavy\","
        "\"FasterCorpseCleanup\":true,\"CorpseDelaySeconds\":12,\"extra\":{\"nested\":[1,2,true,null,\"ok\"]}}";
    CHECK(cd_parse_host_options_json(valid2, strlen(valid2), &o) == 1, "parse valid2 with BOM & nested fields");
    CHECK(o.chaos_intensity == CD_CHAOS_OFF, "chaosIntensity == off");
    CHECK(o.heavy_seaf == CD_HEAVY_SEAF_HEAVY, "heavySeaf == heavy");
    CHECK(o.faster_corpse_cleanup == 1, "fasterCorpseCleanup == true");
    CHECK(o.corpse_delay_seconds == 12, "corpseDelaySeconds == 12");

    /* Standard defaults when optional fields omitted */
    const char* minimal = "{ \"format\": 1 }";
    CHECK(cd_parse_host_options_json(minimal, strlen(minimal), &o) == 1, "parse minimal object");
    CHECK(o.chaos_intensity == CD_CHAOS_STANDARD, "default chaos == standard");
    CHECK(o.heavy_seaf == CD_HEAVY_SEAF_STANDARD, "default heavySeaf == standard");
    CHECK(o.faster_corpse_cleanup == 1, "default fasterCorpseCleanup == true");

    /* Invalid inputs must return -1 and NOT corrupt o */
    CdHostOptions snapshot = o;
    const char* bad_cases[] = {
        "{ \"format\": 2, \"chaosIntensity\": \"standard\" }",
        "{ \"format\": 1, \"chaosIntensity\": \"extreme\" }",
        "{ \"format\": 1, \"heavySeaf\": \"off\" }",
        "{ \"format\": 1, \"fasterCorpseCleanup\": \"true\" }",
        "{ \"format\": 1, \"corpseDelaySeconds\": 121 }",
        "{ \"format\": 1, \"corpseDelaySeconds\": -1 }",
        "{ \"format\": 1.5 }",
        "{ \"format\": 1 } trailing",
        "[]",
        "{",
        ""
    };
    for (size_t i = 0; i < sizeof bad_cases / sizeof bad_cases[0]; ++i) {
        CHECK(cd_parse_host_options_json(bad_cases[i], strlen(bad_cases[i]), &o) == -1, "reject invalid JSON case");
        CHECK(memcmp(&o, &snapshot, sizeof o) == 0, "failed parse leaves output untouched");
    }
}

static void test_filesystem_and_mtime(const char* root)
{
    char mods_dir[512], folder_mod[512], unpacked_dir[512], unpacked_mod[512];
    char json_path[512], bridge_path[512];

    snprintf(mods_dir, sizeof mods_dir, "%s\\mods", root);
    snprintf(folder_mod, sizeof folder_mod, "%s\\mods\\chaos-invasions", root);
    snprintf(unpacked_dir, sizeof unpacked_dir, "%s\\mods\\.unpacked", root);
    snprintf(unpacked_mod, sizeof unpacked_mod, "%s\\mods\\.unpacked\\abc123def\\heavy-seaf", root);
    snprintf(json_path, sizeof json_path, "%s\\mods\\clonedivers-host-options.json", root);
    snprintf(bridge_path, sizeof bridge_path, "%s\\ChaosInvasions.values", root);

    CreateDirectoryA(root, NULL);
    CreateDirectoryA(mods_dir, NULL);
    CreateDirectoryA(folder_mod, NULL);
    CreateDirectoryA(unpacked_dir, NULL);
    {
        char up_pkg[512];
        snprintf(up_pkg, sizeof up_pkg, "%s\\mods\\.unpacked\\abc123def", root);
        CreateDirectoryA(up_pkg, NULL);
    }
    CreateDirectoryA(unpacked_mod, NULL);
    DeleteFileA(json_path);

    /* Missing file -> returns 0 with defaults */
    CdHostOptions o;
    CHECK(cd_load_host_options_from_mod_folder(folder_mod, &o) == 0, "missing file returns 0");
    CHECK(o.loaded_from_file == 0 && o.chaos_intensity == CD_CHAOS_STANDARD &&
          o.heavy_seaf == CD_HEAVY_SEAF_STANDARD && o.faster_corpse_cleanup == 1,
          "missing file populates defaults");

    /* Write valid file and read via BOTH folder_mod and unpacked_mod */
    const char* payload =
        "{\"format\":1,\"chaosIntensity\":\"off\",\"heavySeaf\":\"constant\",\"fasterCorpseCleanup\":false}";
    CHECK(write_bytes(json_path, payload, strlen(payload)), "write clonedivers-host-options.json");
    uint64_t t_host = 134000000000000000ULL;
    CHECK(set_file_mtime(json_path, t_host), "set host options mtime");

    CdHostOptions o_folder, o_unpacked;
    CHECK(cd_load_host_options_from_mod_folder(folder_mod, &o_folder) == 1, "load via folder install");
    CHECK(cd_load_host_options_from_mod_folder(unpacked_mod, &o_unpacked) == 1, "load via .unpacked install");
    CHECK(o_folder.chaos_intensity == CD_CHAOS_OFF && o_unpacked.chaos_intensity == CD_CHAOS_OFF,
          "both layouts read chaosIntensity=off");
    CHECK(o_folder.heavy_seaf == CD_HEAVY_SEAF_CONSTANT && o_unpacked.heavy_seaf == CD_HEAVY_SEAF_CONSTANT,
          "both layouts read heavySeaf=constant");
    CHECK(o_folder.faster_corpse_cleanup == 0 && o_unpacked.faster_corpse_cleanup == 0,
          "both layouts read fasterCorpseCleanup=false");
    CHECK(o_folder.mtime_100ns == t_host && o_unpacked.mtime_100ns == t_host,
          "both layouts report exact Win32 FILETIME");

    /* Verify mtime precedence against in-game Mod Options Menu bridge file */
    CHECK(write_bytes(bridge_path, "session=1\n", 10), "write mock bridge file");
    CHECK(set_file_mtime(bridge_path, t_host - 50000000ULL), "older bridge mtime");
    uint64_t t_bridge = 0;
    CHECK(cd_get_file_mtime_100ns(bridge_path, &t_bridge) == 1, "read bridge mtime");
    CHECK(cd_session_override_is_newer(o_folder.mtime_100ns, t_bridge) == 0,
          "stale bridge does not override newer host options");

    CHECK(set_file_mtime(bridge_path, t_host + 50000000ULL), "newer bridge mtime");
    CHECK(cd_get_file_mtime_100ns(bridge_path, &t_bridge) == 1, "read updated bridge mtime");
    CHECK(cd_session_override_is_newer(o_folder.mtime_100ns, t_bridge) == 1,
          "newer in-game session edit takes precedence over older host options");

    /* Oversized (>64 KiB) file rejected fail-closed */
    char* big = (char*)malloc(70000);
    memset(big, ' ', 70000);
    memcpy(big, "{\"format\":1}", 12);
    CHECK(write_bytes(json_path, big, 70000), "write >64 KiB file");
    free(big);
    CHECK(cd_load_host_options_from_mod_folder(folder_mod, &o) == -1, "oversized file rejected with -1");
    CHECK(o.loaded_from_file == 0 && o.chaos_intensity == CD_CHAOS_STANDARD,
          "oversized rejection resets to safe defaults");

    DeleteFileA(json_path);
    DeleteFileA(bridge_path);
}

int main(int argc, char** argv)
{
    const char* fixture = (argc > 1) ? argv[1] : "D:\\ClaudeWork\\workstreams-v21\\code-mods\\shared\\build\\fixture";
    test_path_resolution();
    test_json_parsing();
    test_filesystem_and_mtime(fixture);
    printf("test_host_options: %u checks, %u failed\n", g_checks, g_failed);
    return g_failed ? 1 : 0;
}
