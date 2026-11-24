/* Extensible torso SHA-256 registry: Empire LEGO Stormtroopers (compact r47 & r45/r47 stilt),
 * Clonedivers LEGO Clones (clonedivers-r19-full), plus runtime JSON extensions (yoda-torsos.json). */
#pragma once
#include <ctype.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define YODA_MAX_VISUALS 256

typedef struct Visual
{
    uint32_t kit, body;
    uint64_t unit;
    const char* mainHash;
    const char* gpuHash;
    const char* altMainHash;
    const char* altGpuHash;
} Visual;

static Visual visuals[YODA_MAX_VISUALS] = {
    /* Empire LEGO Stormtroopers (r47 compact & r45/r47 stilt) */
    {0xB513FD54u, 0, 0xBB11C8041C14C747ull,
     "7c9cb810b8fa044851d90adac9d5a8a093792467eb3a9a04ad9a6c48c998f4de",
     "7d0ad2e335f6a5f11cab6b37b6919976e70d80e08a4a837111e201d481de0e96",
     "704ded9d44af5ee3fd28bd50ecb3f98ad1744c09ddb397874a42e4830bfec038",
     "21bc3b68fed040bd75fdf01368f5088b5ece1cae6cc7a62620c0efe8858f116b"},
    {0xB513FD54u, 1, 0x003CC8A527711DA5ull,
     "d8e1070c153a2357a80d07b90d10c3b7042e08e9a16491c1d6a3acde2571cb19",
     "6cb7e860ba2dcf714b890d47d49d54511b0d37962fc60d0da59cd689a05b9984",
     "b73338ea30579df367359f46787592fb9227f73eb48162cedb0919ea759fbf1f",
     "796b9d936ff6564b0d0afeb13da59e92c283c6657802443726bbab5912a66dd4"},
    {0xE9ADD047u, 0, 0xC1141B792C1B621Aull,
     "eff78f1e43a002a5d9fae8ed22067bbbfb2a94195c79e4de497e231e45b7f227",
     "ab93205dd5a2b301e378b37953658ee975b4ab4bf0f6977fd916778cd8c9750c",
     "c8e20b20ce61415877e1f24ae303a863693dbaa6fa837075805a13e476efbc65",
     "594cfe1cb1e7f936fc6516b2437050380abdac4515d6f2dc53b5dea67072fbb1"},
    {0xE9ADD047u, 1, 0x09AD5784B873E58Full,
     "52f5781e8a81cd69675fddef685e0df1b7574e4be5e80cabe96968424031d9a1",
     "36ddc288a9afcf64bcee0cd7bae7a96119f01baf0fb1889838fe1fc56bde7362",
     "4abe6b85ceb5e16e87bfddc9eeef3ce1835b9e82f3187fabfbc70827affab78c",
     "17976f4fe30b26aa2d50d73861aea6f392dc4df887a1d3dc229e32701074647e"},

    /* Clonedivers LEGO Clones (clonedivers-r19-full: patch_0 & patch_298 variants) */
    {0xB513FD54u, 0, 0xBB11C8041C14C747ull,
     "4b4a641bb83575eaeb452db5ecbccefaf8e868957f994a98f0e7aa6198517bde",
     "e04b9b14658300e743f5377c35637cdd16578f8303616c66883ca44ca4ea58f7",
     NULL, NULL},
    {0xB513FD54u, 1, 0x003CC8A527711DA5ull,
     "06a7c12bf132914c529531af354c63ff557eaba2f5d5c14bf2cfe7e9df91e39b",
     "e9891c812ee32dcd0d727a0498bc6f7bd37a270580a4bd4527bc49fa9fea61b1",
     NULL, NULL},
    {0xE9ADD047u, 0, 0xC1141B792C1B621Aull,
     "a3cb62ef373ac875375b3b32729705e46658b11ab864208e44870974b84871b9",
     "65ecc2e1bdd17a9076ef15f41aec5cb4d0332eae48a9df9c7f44d5c7584b1e17",
     "6524ef0e19fd5189d4c200097acba3d9c3826928632befc38cb622043e802a68",
     "65ecc2e1bdd17a9076ef15f41aec5cb4d0332eae48a9df9c7f44d5c7584b1e17"},
    {0xE9ADD047u, 1, 0x09AD5784B873E58Full,
     "39ce950ea78878c8a9bd6895fb21c10088affa99e9b2b9eb44a2735ed08a28bf",
     "b77ba7bcc3b71cefe57db1369ea80cacf8e013661c4c46597e2b9940cfefd587",
     "7dfa66a5747fce31201ea7ac0a6b8d2c7e7d220325e942955d2b28b7a67ced8e",
     "b77ba7bcc3b71cefe57db1369ea80cacf8e013661c4c46597e2b9940cfefd587"}
};
#define YODA_BUILTIN_VISUALS 8u
static size_t visualCount = YODA_BUILTIN_VISUALS;
static char extraMainHashes[YODA_MAX_VISUALS][65];
static char extraGpuHashes[YODA_MAX_VISUALS][65];
static char extraAltMainHashes[YODA_MAX_VISUALS][65];
static char extraAltGpuHashes[YODA_MAX_VISUALS][65];

static int IsHex64(const char* s)
{
    if (!s || strlen(s) != 64u) return 0;
    for (size_t i = 0; i < 64u; i++) {
        unsigned char c = (unsigned char)s[i];
        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return 0;
    }
    return 1;
}

static int RegisterVisualPair(uint32_t kit, uint32_t body, uint64_t unit,
                              const char* mainHash, const char* gpuHash,
                              const char* altMainHash, const char* altGpuHash)
{
    if (visualCount >= YODA_MAX_VISUALS || !unit || !IsHex64(mainHash) || !IsHex64(gpuHash)) return 0;
    if ((altMainHash && !altGpuHash) || (!altMainHash && altGpuHash)) return 0;
    if (altMainHash && (!IsHex64(altMainHash) || !IsHex64(altGpuHash))) return 0;

    size_t slot = visualCount++;
    memcpy(extraMainHashes[slot], mainHash, 65u);
    memcpy(extraGpuHashes[slot], gpuHash, 65u);
    Visual* v = &visuals[slot];
    v->kit = kit;
    v->body = body;
    v->unit = unit;
    v->mainHash = extraMainHashes[slot];
    v->gpuHash = extraGpuHashes[slot];
    if (altMainHash) {
        memcpy(extraAltMainHashes[slot], altMainHash, 65u);
        memcpy(extraAltGpuHashes[slot], altGpuHash, 65u);
        v->altMainHash = extraAltMainHashes[slot];
        v->altGpuHash = extraAltGpuHashes[slot];
    } else {
        v->altMainHash = NULL;
        v->altGpuHash = NULL;
    }
    return 1;
}

static int YodaJsonStr(const char* obj, const char* objEnd, const char* key, char* out, size_t cap)
{
    char pat[48];
    snprintf(pat, sizeof pat, "\"%s\"", key);
    const char* k = strstr(obj, pat);
    if (!k || k >= objEnd) return 0;
    const char* colon = strchr(k + strlen(pat), ':');
    if (!colon || colon >= objEnd) return 0;
    const char* p = colon + 1;
    while (p < objEnd && (*p == ' ' || *p == '\t' || *p == '\r' || *p == '\n')) p++;
    if (p >= objEnd || *p != '"') return 0;
    p++;
    const char* q = strchr(p, '"');
    if (!q || q >= objEnd) return 0;
    size_t n = (size_t)(q - p);
    if (n >= cap) return 0;
    memcpy(out, p, n);
    out[n] = 0;
    return 1;
}

static int YodaJsonU64(const char* obj, const char* objEnd, const char* key, uint64_t* out)
{
    char buf[40];
    if (YodaJsonStr(obj, objEnd, key, buf, sizeof buf)) {
        const char* s = buf;
        int base = 10;
        if (s[0] == '0' && (s[1] == 'x' || s[1] == 'X')) {
            s += 2;
            base = 16;
        } else {
            size_t len = strlen(s);
            int hasHexAlpha = 0, allHex = (len > 0 && len <= 16u);
            for (size_t i = 0; i < len; i++) {
                unsigned char c = (unsigned char)s[i];
                if ((c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')) hasHexAlpha = 1;
                else if (!(c >= '0' && c <= '9')) allHex = 0;
            }
            if (allHex && (hasHexAlpha || len == 8u || len == 16u)) base = 16;
        }
        char* end = NULL;
        uint64_t v = _strtoui64(s, &end, base);
        if (end != s && *end == 0) { *out = v; return 1; }
        return 0;
    }
    char pat[48];
    snprintf(pat, sizeof pat, "\"%s\"", key);
    const char* k = strstr(obj, pat);
    if (!k || k >= objEnd) return 0;
    const char* colon = strchr(k + strlen(pat), ':');
    if (!colon || colon >= objEnd) return 0;
    const char* p = colon + 1;
    while (p < objEnd && (*p == ' ' || *p == '\t' || *p == '\r' || *p == '\n')) p++;
    if (p >= objEnd || !isdigit((unsigned char)*p)) return 0;
    char* end = NULL;
    uint64_t v = _strtoui64(p, &end, 10);
    if (end == p) return 0;
    *out = v;
    return 1;
}

static void LoadExtraTorsos(const wchar_t* modFolder)
{
    visualCount = YODA_BUILTIN_VISUALS;
    if (!modFolder || !*modFolder) return;
    wchar_t path[1100];
    if (swprintf(path, 1100, L"%ls\\yoda-torsos.json", modFolder) < 0) return;
    HANDLE f = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) return;
    LARGE_INTEGER sz;
    if (!GetFileSizeEx(f, &sz) || sz.QuadPart <= 0 || sz.QuadPart > 1048576) { CloseHandle(f); return; }
    char* buf = (char*)malloc((size_t)sz.QuadPart + 1u);
    DWORD got = 0;
    int ok = buf && ReadFile(f, buf, (DWORD)sz.QuadPart, &got, NULL) && got == (DWORD)sz.QuadPart;
    CloseHandle(f);
    if (!ok) { free(buf); return; }
    buf[got] = 0;

    const char* torsosKey = strstr(buf, "\"torsos\"");
    const char* arrStart = torsosKey ? strchr(torsosKey + 8, '[') : NULL;
    const char* cur = arrStart ? arrStart : buf;
    while ((cur = strchr(cur, '{')) != NULL) {
        const char* end = strchr(cur + 1, '}');
        if (!end) break;
        uint64_t kit = 0, body = 0, unit = 0;
        char mainHex[72] = {0}, gpuHex[72] = {0}, altMainHex[72] = {0}, altGpuHex[72] = {0};
        if (YodaJsonU64(cur, end, "kit", &kit) &&
            YodaJsonU64(cur, end, "body", &body) &&
            YodaJsonU64(cur, end, "unit", &unit) &&
            YodaJsonStr(cur, end, "main_sha256", mainHex, sizeof mainHex) &&
            YodaJsonStr(cur, end, "gpu_sha256", gpuHex, sizeof gpuHex)) {
            for (size_t i = 0; mainHex[i]; i++) mainHex[i] = (char)tolower((unsigned char)mainHex[i]);
            for (size_t i = 0; gpuHex[i]; i++) gpuHex[i] = (char)tolower((unsigned char)gpuHex[i]);
            int hasAltM = YodaJsonStr(cur, end, "alt_main_sha256", altMainHex, sizeof altMainHex);
            int hasAltG = YodaJsonStr(cur, end, "alt_gpu_sha256", altGpuHex, sizeof altGpuHex);
            for (size_t i = 0; altMainHex[i]; i++) altMainHex[i] = (char)tolower((unsigned char)altMainHex[i]);
            for (size_t i = 0; altGpuHex[i]; i++) altGpuHex[i] = (char)tolower((unsigned char)altGpuHex[i]);
            RegisterVisualPair((uint32_t)kit, (uint32_t)body, unit, mainHex, gpuHex,
                               (hasAltM && hasAltG) ? altMainHex : NULL,
                               (hasAltM && hasAltG) ? altGpuHex : NULL);
        }
        cur = end + 1;
    }
    free(buf);
}

#define YODA_WAV_SHA256 "f5f49f73e435113295fef77add8c4941b2b6fd7f0c1958ba1a8b3a9d038f4cef"
