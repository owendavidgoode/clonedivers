/* Fake game/bridge memory, real archive parser and Windows hashing. No game process is opened. */
#include <windows.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#undef NDEBUG
#include <assert.h>

static wchar_t testExePath[1024];
static ULONGLONG testClockMs = 1000;
#define GetTickCount64() testClockMs
static HMODULE testLoaderHandle = (HMODULE)(uintptr_t)0x12345678u;
static FARPROC testBridgeInfoFn, testBridgeRegisterFn, testBridgeSetFn, testBridgeClearFn, testBridgeGetFn;

static HMODULE TestGetModuleHandleW(LPCWSTR name)
{
    if (name && !wcscmp(name, L"hellforge_modloader.dll")) return testLoaderHandle;
    return GetModuleHandleW(name);
}
static FARPROC TestGetProcAddress(HMODULE module, LPCSTR name)
{
    if (module == testLoaderHandle && name)
    {
        if (!strcmp(name, "hellforge_lua_info")) return testBridgeInfoFn;
        if (!strcmp(name, "hellforge_lua_mod")) return testBridgeRegisterFn;
        if (!strcmp(name, "hellforge_lua_set")) return testBridgeSetFn;
        if (!strcmp(name, "hellforge_lua_clear")) return testBridgeClearFn;
        if (!strcmp(name, "hellforge_lua_get")) return testBridgeGetFn;
        return NULL;
    }
    return GetProcAddress(module, name);
}
static DWORD TestGetModuleFileNameW(HMODULE module, LPWSTR buffer, DWORD size)
{
    if (!module && testExePath[0])
    {
        size_t len = wcslen(testExePath);
        if (len >= size) return 0;
        wcscpy(buffer, testExePath);
        return (DWORD)len;
    }
    return GetModuleFileNameW(module, buffer, size);
}
#define GetModuleHandleW TestGetModuleHandleW
#define GetProcAddress TestGetProcAddress
#define GetModuleFileNameW TestGetModuleFileNameW
#include "../src/empire_tie_switch.c"
#undef GetModuleHandleW
#undef GetProcAddress
#undef GetModuleFileNameW

struct HfMod { const HfApi* api; };
static HfApi api;
static struct HfMod mod, luaBridgeMod;
static unsigned char unitData[DELIVERY_COUNT][136], eagleData[DELIVERY_COUNT][152];
static uint64_t originals[DELIVERY_COUNT];
static int owned[DELIVERY_COUNT], setCalls, clearCalls, badSet = -1, shared = -1, badStride, clobber, checks;
static int packageState, fakeModCount = 2, registerCalls, failRegister;
static int onReadyCalls, onTickCalls, onSpawnCalls, onStratagemCalls;
static HfEventCallback registeredReady;
static void* registeredReadyUser;
static HfTickCallback registeredTick;
static void* registeredTickUser;
static char lastLogLine[512], lastStatusLine[256];

/* Fake memory/module harness for build-25480438 direct package pins + 4-player PlayerManager. */
#define FAKE_GAME_BASE ((uintptr_t)0x7FF000000000ull)
#define FAKE_EXE_BASE  ((uintptr_t)0x7FF100000000ull)
static int useFakeModules, badPinIndex = -1, directReqCalls, fakeIdleState = 1;
static uint64_t lastDirectReqPkg;
static unsigned char fakePkgSys[32], fakeEngine[0x410], fakeManager[0x220], fakeResources[0x98], fakePkgObj[0x30], fakePartObj[0x30];
static unsigned char fakePlayers[0x440];
static uintptr_t fakePkgList[4], fakePartPtrs[2];
static uint8_t fakeMapEntries[64 * 16];
static uintptr_t fakePlayersPtr = (uintptr_t)fakePlayers;

#define CHECK(x) do { assert(x); checks++; } while (0)
static void Put32Mem(void* p, size_t at, uint32_t v) { memcpy((char*)p + at, &v, 4); }
static void Put64Mem(void* p, size_t at, uint64_t v) { memcpy((char*)p + at, &v, 8); }
static int Index(uint64_t id)
{ for (size_t i = 0; i < DELIVERY_COUNT; i++) if (deliveries[i].entity == id) return (int)i; return -1; }
static uintptr_t Record(const char* component, uint64_t entity, uint32_t* stride)
{
    int i = Index(entity);
    if (i < 0 || shared == i) return 0;
    int unit = !strcmp(component, UNIT);
    *stride = badStride ? 1 : unit ? 136 : 152;
    return (uintptr_t)(unit ? unitData[i] : eagleData[i]);
}
static uintptr_t FakeModuleBase(const char* name, size_t* size)
{
    if (!useFakeModules) { if (size) *size = 0; return 0; }
    if (!strcmp(name, "game.dll")) { if (size) *size = TIE_GAME_MIN_IMAGE_SIZE; return FAKE_GAME_BASE; }
    if (!strcmp(name, "helldivers2.exe")) { if (size) *size = TIE_EXE_MIN_IMAGE_SIZE; return FAKE_EXE_BASE; }
    return 0;
}
static int FakeReadCode(uintptr_t p, void* out, size_t size)
{
    if (!useFakeModules) return 1;
    const uint8_t* expect = NULL;
    int idx = -1;
    if (p == FAKE_GAME_BASE + TIE_PKG_REQUEST_RVA && size == 32) { expect = kTiePkgRequestPin; idx = 0; }
    else if (p == FAKE_GAME_BASE + TIE_PKG_IDLE_RVA && size == 32) { expect = kTiePkgIdlePin; idx = 1; }
    else if (p == FAKE_EXE_BASE + TIE_EXE_HAS_LOADED_RVA && size == 32) { expect = kTieExeHasLoadedPin; idx = 2; }
    else if (p == FAKE_EXE_BASE + TIE_EXE_FIND_RVA && size == 32) { expect = kTieExeFindPin; idx = 3; }
    if (!expect) return 1;
    memcpy(out, expect, 32);
    if (badPinIndex == idx) ((uint8_t*)out)[0] ^= 0xFFu;
    return 0;
}
static int Read(uintptr_t p, void* out, size_t size)
{
    if (useFakeModules && p == FAKE_GAME_BASE + TIE_PKG_SYSTEM_RVA && size == 8)
    {
        uintptr_t v = (uintptr_t)fakePkgSys;
        memcpy(out, &v, 8);
        return 0;
    }
    if (useFakeModules && p == FAKE_EXE_BASE + TIE_EXE_ENGINE_RVA && size == 8)
    {
        uintptr_t v = (uintptr_t)fakeEngine;
        memcpy(out, &v, 8);
        return 0;
    }
    if (p < 65536) return 1;
    memcpy(out, (void*)p, size);
    return 0;
}
static void FakeDirectReq(void* sys, const uint64_t* ids, uint32_t count)
{
    CHECK(sys == (void*)fakePkgSys && ids != NULL && count == 1);
    directReqCalls++;
    lastDirectReqPkg = ids[0];
}
static uint8_t FakeDirectIdle(void* unused) { (void)unused; return (uint8_t)fakeIdleState; }
static uintptr_t FakeGlobal(HfMod* m, const char* name)
{
    (void)m;
    if (!strcmp(name, "PlayerManager")) return (uintptr_t)&fakePlayersPtr;
    return 0;
}
static uint32_t FakeEntityOfNet(HfMod* m, uint32_t network)
{
    (void)m;
    if (network >= 500u && network <= 503u) return 600u + (network - 500u);
    return UINT32_MAX;
}
static void Log(HfMod* m, const char* format, ...)
{
    (void)m;
    va_list args;
    va_start(args, format);
    vsnprintf(lastLogLine, sizeof lastLogLine, format, args);
    va_end(args);
}
static void Status(HfMod* m, const char* value)
{
    (void)m;
    snprintf(lastStatusLine, sizeof lastStatusLine, "%s", value ? value : "");
}
static const char* FakeBuildId(void) { return "25480438"; }
static int FakeOn(HfMod* m, HfEvent event, HfEventCallback callback, void* user)
{
    (void)m;
    if (event == HF_EVENT_ENTITIES_READY) { registeredReady = callback; registeredReadyUser = user; onReadyCalls++; }
    return 0;
}
static int FakeOnGameThread(HfMod* m, HfTickCallback callback, void* user)
{
    (void)m;
    registeredTick = callback; registeredTickUser = user; onTickCalls++;
    return 1;
}
static int FakeSubscribe(HfMod* m, uint32_t kind, HfListener listener, void* user, int32_t priority)
{
    (void)m; (void)listener; (void)user; (void)priority;
    if (kind == HF_ON_SPAWN) onSpawnCalls++;
    else if (kind == HF_ON_STRATAGEM) onStratagemCalls++;
    return 1;
}
static int FakeBridgeInfo(HfLuaInfo* info)
{
    if (!info || info->size < sizeof *info) return 0;
    info->bridge = 3; info->sdk = 16; info->build = "25480438";
    return 1;
}
static HfMod* FakeBridgeRegister(const char* name)
{
    CHECK(name && !strcmp(name, "clonedivers.empire-tie-switch"));
    registerCalls++;
    if (failRegister) return NULL;
    fakeModCount++;
    return (HfMod*)&luaBridgeMod;
}
static int Package(HfMod* m, uint64_t id) { (void)m; CHECK(id == EAGLE_PACKAGE); return packageState; }
static int Set(HfMod* m, const char* component, uint64_t entity, uint32_t offset, uint32_t type, double number, uint64_t value, uint32_t flags)
{
    (void)number; CHECK(m != NULL && m == bridgeMod && !strcmp(component, UNIT) && !offset && type == HF_TYPE_U64 && !flags);
    int i = Index(entity); setCalls++;
    if (badSet == i) return 0;
    if (!owned[i]) memcpy(&originals[i], unitData[i], 8);
    owned[i] = 1; memcpy(unitData[i], &value, 8);
    if (clobber && i == (int)DELIVERY_COUNT-1) eagleData[0][20] ^= 1;
    return HF_APPLIES_RESPAWN;
}
static int Clear(HfMod* m, const char* component, uint64_t entity, uint32_t offset, uint32_t type)
{
    (void)component; (void)offset; (void)type;
    CHECK(m != NULL && m == bridgeMod);
    int i = Index(entity); clearCalls++;
    if (owned[i]) memcpy(unitData[i], &originals[i], 8);
    owned[i] = 0;
    return HF_APPLIES_RESPAWN;
}
static int Get(HfMod* m, const char* component, uint64_t entity, uint32_t offset, uint32_t type, HfLuaValue* value)
{
    (void)component; (void)offset; (void)type;
    CHECK(m != NULL && m == bridgeMod);
    int i = Index(entity); memcpy(&value->now_u64, unitData[i], 8); return HF_APPLIES_RESPAWN;
}
static void ResetFakePackageMemory(uint32_t mapCap, uint32_t usedCount, int includeEagle, uint32_t partState)
{
    memset(fakePkgSys, 0, sizeof fakePkgSys);
    memset(fakeEngine, 0, sizeof fakeEngine);
    memset(fakeManager, 0, sizeof fakeManager);
    memset(fakeResources, 0, sizeof fakeResources);
    memset(fakePkgObj, 0, sizeof fakePkgObj);
    memset(fakePartObj, 0, sizeof fakePartObj);
    memset(fakeMapEntries, 0, sizeof fakeMapEntries);

    uint64_t emptyKey = 0xFFFFFFFFFFFFFFFFull;
    for (uint32_t i = 0; i < mapCap; i++) Put64Mem(fakeMapEntries, (size_t)i * 16u, emptyKey);
    for (uint32_t i = 0; i < usedCount; i++)
    {
        uint64_t k = (includeEagle && i == 0) ? EAGLE_PACKAGE : (0x1000000000000000ull + i);
        Put64Mem(fakeMapEntries, (size_t)i * 16u, k);
    }
    Put64Mem(fakePkgSys, 0x00u, (uintptr_t)fakeMapEntries);
    Put32Mem(fakePkgSys, 0x08u, mapCap);
    Put64Mem(fakePkgSys, 0x10u, emptyKey);

    Put64Mem(fakeEngine, 0x400u, (uintptr_t)fakeManager);
    Put64Mem(fakeManager, 0x208u, (uintptr_t)fakeResources);
    fakePkgList[0] = (uintptr_t)fakePkgObj;
    Put32Mem(fakeResources, 0x80u, 1u);
    Put64Mem(fakeResources, 0x88u, (uintptr_t)fakePkgList);
    Put64Mem(fakePkgObj, 0x10u, EAGLE_PACKAGE);
    Put32Mem(fakePkgObj, 0x18u, 1u);
    fakePartPtrs[0] = (uintptr_t)fakePartObj;
    Put64Mem(fakePkgObj, 0x20u, (uintptr_t)fakePartPtrs);
    Put64Mem(fakePartObj, 0x10u, EAGLE_PACKAGE);
    Put32Mem(fakePartObj, 0x1Cu, partState);
}

static void ResetDataAndFlags(void)
{
    for (size_t i = 0; i < DELIVERY_COUNT; i++)
    {
        memset(unitData[i], (int)i+1, 136); memset(eagleData[i], (int)i+30, 152);
        uint64_t model = FLIGHT; memcpy(unitData[i], &model, 8);
    }
    memset(owned, 0, sizeof owned); setCalls = clearCalls = badStride = clobber = 0;
    shared = badSet = -1; applied = armed = failed = 0;
    entitiesReady = 0; lastCheck = 0; lastPackage = -1; packageState = HF_PACKAGE_LOADING;
    lastLogLine[0] = 0; lastStatusLine[0] = 0;
    testClockMs = 1000;
    directPkgMode = directPinsChecked = directPinsOk = directRequested = directResident = 0;
    directStartMs = 0;
    directReqCalls = 0; lastDirectReqPkg = 0; fakeIdleState = 1; badPinIndex = -1;
    directReqOverride = FakeDirectReq;
    directIdleOverride = FakeDirectIdle;
    ClearSorties();
    g_nextSortieId = 1u;
    g_peakConcurrentSorties = 0;
    memset(fakePlayers, 0, sizeof fakePlayers);
    ResetFakePackageMemory(16u, 4u, 0, 0u);
}
static void Reset(void)
{
    ResetDataAndFlags();
    memset(&api, 0, sizeof api);
    api.version = HF_MOD_SDK_VERSION; api.size = sizeof api;
    api.build_id = FakeBuildId; api.on = FakeOn; api.on_game_thread = FakeOnGameThread; api.subscribe = FakeSubscribe;
    api.component_record = Record; api.read = Read; api.read_code = FakeReadCode; api.module_base = FakeModuleBase;
    api.global = FakeGlobal; api.entity_of_network = FakeEntityOfNet;
    api.log = Log; api.set_status = Status; api.load_package = Package;
    mod.api = hf = &api; bridgeMod = (HfMod*)&luaBridgeMod;
    bridgeInfo = FakeBridgeInfo; bridgeRegister = FakeBridgeRegister; bridgeSet = Set; bridgeClear = Clear; bridgeGet = Get;
    registerCalls = 0; failRegister = 0; fakeModCount = 2;
    useFakeModules = 1;
    onReadyCalls = onTickCalls = onSpawnCalls = onStratagemCalls = 0;
    registeredReady = NULL; registeredReadyUser = NULL; registeredTick = NULL; registeredTickUser = NULL;
}
static void CopyFileChecked(const wchar_t* from, const wchar_t* to) { CHECK(CopyFileW(from, to, FALSE)); }
static void LinkArchives(const wchar_t* source, const wchar_t* dest, int includePatch2000)
{
    wchar_t pattern[1200]; WIN32_FIND_DATAW entry;
    swprintf(pattern, 1200, L"%ls\\*.patch_*", source);
    HANDLE scan = FindFirstFileW(pattern, &entry); CHECK(scan != INVALID_HANDLE_VALUE);
    do
    {
        if ((!includePatch2000 && wcsstr(entry.cFileName, L".patch_2000")) || entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
        wchar_t from[1200], to[1200];
        swprintf(from, 1200, L"%ls\\%ls", source, entry.cFileName);
        swprintf(to, 1200, L"%ls\\%ls", dest, entry.cFileName);
        CHECK(CreateHardLinkW(to, from, NULL));
    } while (FindNextFileW(scan, &entry));
    FindClose(scan);
}
static void SeedFixture(const wchar_t* source, const wchar_t* fixture) { LinkArchives(source, fixture, 0); }
static void CheckAllEightDeliveries(const Snapshot all[DELIVERY_COUNT])
{
    static const struct { const char* name; uint64_t entity, model; } expected[DELIVERY_COUNT] = {
        {"strafe", 0x23a60681dd4383ecull, TIE_FIGHTER},
        {"airstrike", 0x2ea01cb1676aca29ull, TIE_BOMBER},
        {"smoke", 0x1b3bcadabc7ef8d6ull, TIE_BOMBER},
        {"500kg", 0xe44b691dc039a505ull, TIE_BOMBER},
        {"cluster", 0x9d4f7cb4eb34515dull, TIE_BOMBER},
        {"gas", 0xdbb286ad7ed9df96ull, TIE_BOMBER},
        {"napalm", 0x27bb558c893383ccull, TIE_BOMBER},
        {"110mm", 0x397792815583da29ull, TIE_FIGHTER},
    };
    CHECK(DELIVERY_COUNT == 8);
    CHECK(ModelsCorrect(all));
    for (size_t i = 0; i < DELIVERY_COUNT; i++)
    {
        CHECK(!strcmp(deliveries[i].name, expected[i].name));
        CHECK(deliveries[i].entity == expected[i].entity);
        CHECK(deliveries[i].model == expected[i].model);
        CHECK(Model(&all[i]) == expected[i].model);
    }
}
static void TestInitAndLazyBridgeRegistration(const wchar_t* stagedData, const wchar_t* fixtureRoot)
{
    wchar_t initRoot[1200], initBin[1200], initData[1200];
    swprintf(initRoot, 1200, L"%ls\\init_root", fixtureRoot);
    swprintf(initBin, 1200, L"%ls\\bin", initRoot);
    swprintf(initData, 1200, L"%ls\\data", initRoot);
    CHECK(CreateDirectoryW(initRoot, NULL));
    CHECK(CreateDirectoryW(initBin, NULL));
    CHECK(CreateDirectoryW(initData, NULL));
    LinkArchives(stagedData, initData, 1);
    CHECK(swprintf(testExePath, 1024, L"%ls\\helldivers2.exe", initBin) > 0);

    testBridgeInfoFn = (FARPROC)(uintptr_t)FakeBridgeInfo;
    testBridgeRegisterFn = (FARPROC)(uintptr_t)FakeBridgeRegister;
    testBridgeSetFn = (FARPROC)(uintptr_t)Set;
    testBridgeClearFn = (FARPROC)(uintptr_t)Clear;
    testBridgeGetFn = (FARPROC)(uintptr_t)Get;

    /* 1. ClearAll/Fail before bridgeMod registration must safely return 1 without calling bridgeClear(NULL, ...). */
    Reset();
    bridgeMod = NULL;
    CHECK(ClearAll((HfMod*)&mod) == 1 && clearCalls == 0);
    Fail((HfMod*)&mod, "pre-bridge failure");
    CHECK(failed && !armed && clearCalls == 0 && !strcmp(lastStatusLine, "pre-bridge failure"));

    /* 2. hellforge_mod_init() must succeed with 0 bridgeRegister calls (g_modCount unchanged during init). */
    Reset();
    bridgeMod = NULL; bridgeInfo = NULL; bridgeRegister = NULL; bridgeSet = NULL; bridgeClear = NULL; bridgeGet = NULL;
    fakeModCount = 2;
    CHECK(hellforge_mod_init(&api, (HfMod*)&mod) == 0);
    CHECK(registerCalls == 0 && fakeModCount == 2 && bridgeMod == NULL);
    CHECK(onReadyCalls == 1 && onTickCalls == 1 && onSpawnCalls == 1 && onStratagemCalls == 1);
    CHECK(registeredReady != NULL && registeredTick != NULL);
    CHECK(strstr(lastLogLine, "qualified staged closure parts=135") != NULL);

    /* 3. Tick() before Ready() must not register bridgeMod. */
    registeredTick((HfMod*)&mod, 0.016f, registeredTickUser);
    CHECK(registerCalls == 0 && fakeModCount == 2 && bridgeMod == NULL && !setCalls && lastPackage == -1);

    /* 4. Ready() enables Tick(), which registers bridgeMod exactly once. */
    registeredReady((HfMod*)&mod, HF_EVENT_ENTITIES_READY, registeredReadyUser);
    CHECK(entitiesReady == 1 && registerCalls == 0 && fakeModCount == 2 && bridgeMod == NULL);
    registeredTick((HfMod*)&mod, 0.016f, registeredTickUser);
    CHECK(registerCalls == 1 && fakeModCount == 3 && bridgeMod == (HfMod*)&luaBridgeMod);
    CHECK(lastPackage == HF_PACKAGE_LOADING && !setCalls);

    /* 5. Repeated Tick() within 1000 ms and once Eagle package becomes resident never re-registers bridgeMod. */
    registeredTick((HfMod*)&mod, 0.016f, registeredTickUser);
    CHECK(registerCalls == 1 && fakeModCount == 3 && !setCalls);
    lastCheck = 0; packageState = HF_PACKAGE_RESIDENT;
    registeredTick((HfMod*)&mod, 0.016f, registeredTickUser);
    CHECK(registerCalls == 1 && fakeModCount == 3 && armed && applied && !failed && setCalls == DELIVERY_COUNT);
    Snapshot after[DELIVERY_COUNT];
    CHECK(SnapshotAll(after));
    CheckAllEightDeliveries(after);
    lastCheck = 0;
    registeredTick((HfMod*)&mod, 0.016f, registeredTickUser);
    CHECK(registerCalls == 1 && fakeModCount == 3 && armed && !failed && setCalls == DELIVERY_COUNT);

    /* 6. Lazy bridgeRegister failure in Tick() calls Fail without calling bridgeClear(NULL, ...) or retrying. */
    Reset();
    bridgeMod = NULL; entitiesReady = 1; failRegister = 1;
    Tick((HfMod*)&mod, 0.016f, NULL);
    CHECK(registerCalls == 1 && fakeModCount == 2 && bridgeMod == NULL && failed && !armed && clearCalls == 0);
    CHECK(!strcmp(lastStatusLine, "lua bridge registration failed"));
    Tick((HfMod*)&mod, 0.016f, NULL);
    CHECK(registerCalls == 1 && clearCalls == 0);

    testExePath[0] = 0;
}
int wmain(int argc, wchar_t** argv)
{
    if (argc == 3 && !wcscmp(argv[1], L"--scan"))
    {
        HMODULE bcrypt = LoadLibraryW(L"bcrypt.dll");
        openHash = bcrypt ? (void*)GetProcAddress(bcrypt, "BCryptOpenAlgorithmProvider") : NULL;
        hashBytes = bcrypt ? (void*)GetProcAddress(bcrypt, "BCryptHash") : NULL;
        CHECK(openHash && hashBytes && openHash(&sha, BCRYPT_SHA256_ALGORITHM, NULL, 0) >= 0);
        int ok = ScanAssets(argv[2]);
        if (ok) printf("qualified staged closure parts=%zu\n", ASSET_COUNT);
        else printf("off: %s\n", assetError);
        return ok ? 0 : 1;
    }
    CHECK(argc == 3);
    Reset(); Snapshot before[DELIVERY_COUNT], after[DELIVERY_COUNT];
    CHECK(SnapshotAll(before)); CHECK(Apply((HfMod*)&mod)); CHECK(armed && applied && !failed);
    CHECK(SnapshotAll(after));
    CheckAllEightDeliveries(after);
    for (size_t i = 0; i < DELIVERY_COUNT; i++)
    { CHECK(!memcmp(before[i].unit+8, after[i].unit+8, 128)); CHECK(!memcmp(before[i].eagle, after[i].eagle, 152)); }
    CHECK(Apply((HfMod*)&mod)); CHECK(!failed); /* idempotent and reload-replayed values */
    for (size_t i = 0; i < DELIVERY_COUNT; i++) memcpy(unitData[i], &originals[i], 8);
    CHECK(Apply((HfMod*)&mod)); CHECK(SnapshotAll(after));
    CheckAllEightDeliveries(after);
    CHECK(ClearAll((HfMod*)&mod)); CHECK(SnapshotAll(after));
    for (size_t i = 0; i < DELIVERY_COUNT; i++) CHECK(Model(&after[i]) == FLIGHT);
    for (int at = 0; at < (int)DELIVERY_COUNT; at++)
    {
        Reset(); badSet = at; CHECK(!Apply((HfMod*)&mod)); CHECK(failed && !armed && clearCalls == DELIVERY_COUNT);
        CHECK(SnapshotAll(after));
        for (size_t i = 0; i < DELIVERY_COUNT; i++) CHECK(Model(&after[i]) == FLIGHT && !owned[i]);
        Reset(); shared = at; CHECK(!Apply((HfMod*)&mod)); CHECK(!setCalls && !armed);
    }
    Reset(); uint64_t conflict = 0x1122334455667788ull; memcpy(unitData[3], &conflict, 8);
    CHECK(!Apply((HfMod*)&mod)); CHECK(failed && !setCalls); CHECK(!memcmp(unitData[3], &conflict, 8));
    Reset(); badStride = 1; CHECK(!Apply((HfMod*)&mod)); CHECK(!setCalls);
    Reset(); clobber = 1; CHECK(!Apply((HfMod*)&mod)); CHECK(failed && !armed);
    unsigned index;
    CHECK(PatchName(L"9ba626afa44a3aa3.patch_10", &index) && index == 10);
    CHECK(!PatchName(L"9ba626afa44a3aa3.patch_0.stream", &index));
    CHECK(!PatchName(L"9ba626afa44a3aa3.patch_", &index));
    HMODULE bcrypt = LoadLibraryW(L"bcrypt.dll"); CHECK(bcrypt != NULL);
    openHash = (void*)GetProcAddress(bcrypt, "BCryptOpenAlgorithmProvider"); hashBytes = (void*)GetProcAddress(bcrypt, "BCryptHash");
    CHECK(openHash && hashBytes && openHash(&sha, BCRYPT_SHA256_ALGORITHM, NULL, 0) >= 0);
    CHECK(ScanAssets(argv[1])); CHECK(AssetsUnchanged());
    Reset(); Tick((HfMod*)&mod, 0, NULL); CHECK(!setCalls && lastPackage == -1);
    Ready((HfMod*)&mod, HF_EVENT_ENTITIES_READY, NULL);
    Tick((HfMod*)&mod, 0, NULL); CHECK(!setCalls && lastPackage == HF_PACKAGE_LOADING);
    lastCheck = 0; packageState = HF_PACKAGE_RESIDENT;
    Tick((HfMod*)&mod, 0, NULL); CHECK(armed && setCalls == DELIVERY_COUNT);
    lastCheck = 0; Tick((HfMod*)&mod, 0, NULL); CHECK(setCalls == DELIVERY_COUNT && armed);
    memcpy(unitData[3], &conflict, 8); lastCheck = 0;
    Tick((HfMod*)&mod, 0, NULL); CHECK(failed && !armed);

    /* 0.3.0: stock 0.4.0-beta kRefPackages miss (load_package == 0) with mismatched pin fails closed safely. */
    Reset(); entitiesReady = 1; packageState = 0; badPinIndex = 0;
    Tick((HfMod*)&mod, 0, NULL); CHECK(failed && !setCalls && !directReqCalls);

    CHECK(CreateDirectoryW(argv[2], NULL));
    TestInitAndLazyBridgeRegistration(argv[1], argv[2]);
    Reset(); entitiesReady = 1; packageState = HF_PACKAGE_RESIDENT;
    Tick((HfMod*)&mod, 0, NULL); CHECK(armed);
    SeedFixture(argv[1], argv[2]);
    CHECK(!ScanAssets(argv[2])); /* old base alone cannot qualify */
    CHECK(strstr(assetError, "resource=") && strstr(assetError, "type=") && strstr(assetError, "part=") && strstr(assetError, "expected_sha256="));
    const wchar_t* suffixes[] = {L"", L".stream", L".gpu_resources"}; wchar_t from[1200], to[1200];
    for (int i = 0; i < 3; i++)
    {
        swprintf(from, 1200, L"%ls\\9ba626afa44a3aa3.patch_2000%ls", argv[1], suffixes[i]);
        swprintf(to, 1200, L"%ls\\9ba626afa44a3aa3.patch_2009%ls", argv[2], suffixes[i]); CopyFileChecked(from, to);
    }
    CHECK(ScanAssets(argv[2])); /* staging renumber is accepted */
    CHECK(!assetError[0]);
    swprintf(to, 1200, L"%ls\\9ba626afa44a3aa3.patch_2009", argv[2]);
    HANDLE missing = CreateFileW(to, GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING, 0, NULL);
    CHECK(missing != INVALID_HANDLE_VALUE);
    uint32_t head[3]; CHECK(ReadAt(missing, 0, head, sizeof head)); int removed = 0;
    for (uint32_t r = 0; r < head[2]; r++)
    {
        uint64_t offset = 72ull+32ull*head[1]+80ull*r; ArchiveRow row;
        CHECK(ReadAt(missing, offset, &row, sizeof row));
        if (row.q[0] != TIE_BOMBER || row.q[1] != 0xe0a48d0be9a7453full) continue;
        LARGE_INTEGER at; at.QuadPart = (LONGLONG)offset; DWORD wrote; uint64_t other = 0x1122334455667788ull;
        CHECK(SetFilePointerEx(missing, at, NULL, FILE_BEGIN));
        CHECK(WriteFile(missing, &other, 8, &wrote, NULL) && wrote == 8); removed++;
    }
    CloseHandle(missing); CHECK(removed == 1); CHECK(!ScanAssets(argv[2]));
    CHECK(strstr(assetError, "missing staged resource resource=81787742b65c43f4 type=e0a48d0be9a7453f part=0"));
    swprintf(from, 1200, L"%ls\\9ba626afa44a3aa3.patch_2000", argv[1]); CopyFileChecked(from, to);
    CHECK(ScanAssets(argv[2]));
    swprintf(to, 1200, L"%ls\\9ba626afa44a3aa3.patch_2009.gpu_resources", argv[2]);
    CHECK(DeleteFileW(to)); CHECK(!ScanAssets(argv[2]));
    CHECK(strstr(assetError, "payload file missing or unreadable") && strstr(assetError, "part=2") && strstr(assetError, ".gpu_resources"));
    swprintf(from, 1200, L"%ls\\9ba626afa44a3aa3.patch_2000.gpu_resources", argv[1]); CopyFileChecked(from, to);
    CHECK(ScanAssets(argv[2]));
    swprintf(from, 1200, L"%ls\\9ba626afa44a3aa3.patch_2009", argv[2]);
    swprintf(to, 1200, L"%ls\\9ba626afa44a3aa3.patch_2010", argv[2]); CopyFileChecked(from, to);
    for (int i = 1; i < 3; i++)
    {
        swprintf(from, 1200, L"%ls\\9ba626afa44a3aa3.patch_2009%ls", argv[2], suffixes[i]);
        swprintf(to, 1200, L"%ls\\9ba626afa44a3aa3.patch_2010%ls", argv[2], suffixes[i]); CopyFileChecked(from, to);
    }
    CHECK(ScanAssets(argv[2]));
    swprintf(to, 1200, L"%ls\\9ba626afa44a3aa3.patch_2010.gpu_resources", argv[2]);
    HANDLE file = CreateFileW(to, GENERIC_WRITE, 0, NULL, OPEN_EXISTING, 0, NULL); CHECK(file != INVALID_HANDLE_VALUE);
    LARGE_INTEGER at; at.QuadPart = 1000; CHECK(SetFilePointerEx(file, at, NULL, FILE_BEGIN));
    DWORD wrote; CHECK(WriteFile(file, "bad!", 4, &wrote, NULL) && wrote == 4); CloseHandle(file);
    CHECK(!AssetsUnchanged());
    CHECK(strstr(assetError, "staged payload file missing or changed") && strstr(assetError, "part=2"));
    CHECK(!ScanAssets(argv[2])); /* higher patch's changed GPU wins */
    CHECK(strstr(assetError, "hash mismatch") && strstr(assetError, "part=2") && strstr(assetError, "patch_2010.gpu_resources"));
    lastCheck = 0; Tick((HfMod*)&mod, 0, NULL); CHECK(failed && !armed && clearCalls == DELIVERY_COUNT);
    swprintf(from, 1200, L"%ls\\9ba626afa44a3aa3.patch_2009", argv[2]);
    swprintf(to, 1200, L"%ls\\aaaaaaaaaaaaaaaa.patch_0", argv[2]); CopyFileChecked(from, to);
    CHECK(!ScanAssets(argv[2])); /* cross-family ownership ambiguous */
    CHECK(strstr(assetError, "ambiguous archive provider") && strstr(assetError, "resource="));
    /* 0.3.0 Direct eagle_base (0x2C26BC4C6592FA14) residency fallback & 4-player concurrent sortie tests */
    /* A. Happy path: stock hf->load_package returns 0, pins match, refmap has headroom (<75%), direct load completes. */
    CHECK(ScanAssets(argv[1]));
    Reset();
    entitiesReady = 1; packageState = 0;
    Tick((HfMod*)&mod, 0.016f, NULL);
    CHECK(!failed && directPkgMode == 1 && directPinsOk == 1 && directReqCalls == 1);
    CHECK(lastDirectReqPkg == EAGLE_PACKAGE && lastPackage == HF_PACKAGE_LOADING && !armed && !setCalls);
    /* Second tick while part state != 4 stays loading without duplicate directReq calls. */
    testClockMs += 1000;
    Tick((HfMod*)&mod, 0.016f, NULL);
    CHECK(!failed && directReqCalls == 1 && lastPackage == HF_PACKAGE_LOADING && !armed);
    /* Engine marks eagle_base part state=4 & idle=1 -> residency proven -> delivers 8 TIE model switches. */
    Put32Mem(fakePartObj, 0x1Cu, 4u);
    testClockMs += 1000;
    Tick((HfMod*)&mod, 0.016f, NULL);
    CHECK(!failed && directResident == 1 && lastPackage == HF_PACKAGE_RESIDENT && armed && setCalls == DELIVERY_COUNT);

    /* B. All 4 build-25480438 pins fail closed with zero crash and zero directReq calls. */
    for (int p = 0; p < 4; p++)
    {
        Reset();
        entitiesReady = 1; packageState = 0; badPinIndex = p;
        Tick((HfMod*)&mod, 0.016f, NULL);
        CHECK(failed && !armed && directPkgMode == -1 && directReqCalls == 0);
        CHECK(strstr(lastStatusLine, "pin mismatch") != NULL);
    }

    /* C. Refmap >= 75% full (12/16 slots occupied without eagle_base) refuses direct request safely. */
    Reset();
    ResetFakePackageMemory(16u, 12u, 0, 0u);
    entitiesReady = 1; packageState = 0;
    Tick((HfMod*)&mod, 0.016f, NULL);
    CHECK(failed && !armed && directReqCalls == 0 && strstr(lastStatusLine, "75%") != NULL);

    /* D. If eagle_base is already in the 12/16 map, headroom check permits completing once resident. */
    Reset();
    ResetFakePackageMemory(16u, 12u, 1, 4u);
    entitiesReady = 1; packageState = 0;
    Tick((HfMod*)&mod, 0.016f, NULL);
    CHECK(!failed && armed && directResident == 1 && directReqCalls == 0);

    /* E. Direct load 90s timeout fails cleanly without hanging. */
    Reset();
    entitiesReady = 1; packageState = 0;
    Tick((HfMod*)&mod, 0.016f, NULL);
    CHECK(!failed && directRequested == 1 && !directResident);
    testClockMs += TIE_PKG_TIMEOUT_MS + 1000ull;
    Tick((HfMod*)&mod, 0.016f, NULL);
    CHECK(failed && !armed && strstr(lastStatusLine, "timed out") != NULL);

    /* F. 4-player concurrent Eagle sortie tracking (Fighter + Bomber across slots 0..3 + remote spawn + TTL + exit + unload). */
    Reset();
    ResetFakePackageMemory(16u, 4u, 0, 4u);
    entitiesReady = 1; packageState = HF_PACKAGE_RESIDENT;
    Tick((HfMod*)&mod, 0.016f, NULL);
    CHECK(armed && !failed);
    Put32Mem(fakePlayers, 0x84u, 4u);
    for (uint32_t k = 0; k < 4u; k++) Put32Mem(fakePlayers, 0x3A8u + 0x20u * k, 500u + k);
    for (uint32_t k = 0; k < 4u; k++)
    {
        HfStratagemEvent bev = {{sizeof bev, HF_ON_STRATAGEM, k + 1u}, 10u + k, 600u + k, {10.0f * k, 0.0f, 5.0f}};
        Stratagem((HfMod*)&mod, &bev, NULL);
        size_t delIdx = (k % 2u == 0u) ? 0u : 3u; /* alternate strafe Fighter & 500kg Bomber */
        HfSpawnEvent sev = {{sizeof sev, HF_ON_SPAWN, k + 1u}, 9000u + k, 800u + k, deliveries[delIdx].entity, (k == 0u) ? 1u : 0u};
        Spawn((HfMod*)&mod, &sev, NULL);
        CHECK(g_sorties[k].active && g_sorties[k].playerSlot == (int)k && g_sorties[k].model == deliveries[delIdx].model);
    }
    CHECK(g_activeSortieCount == 4u && g_peakConcurrentSorties == 4u);
    /* Remote fifth concurrent sortie without local beacon also tracks concurrently in slot 4. */
    {
        HfSpawnEvent sevRemote = {{sizeof sevRemote, HF_ON_SPAWN, 9u}, 9005u, 805u, deliveries[1].entity, 0u};
        Spawn((HfMod*)&mod, &sevRemote, NULL);
        CHECK(g_activeSortieCount == 5u && g_peakConcurrentSorties == 5u);
    }
    /* TTL expiry prunes concurrent sorties and restores idle status. */
    testClockMs += TIE_SORTIE_TTL_MS + 100ull;
    Tick((HfMod*)&mod, 0.016f, NULL);
    CHECK(g_activeSortieCount == 0u);
    CHECK(!strcmp(lastStatusLine, "Fighter strafe/110mm, Bomber bombing; package resident"));
    /* Leaving mission clears active sorties immediately, and hellforge_mod_unload clears bridge edits. */
    {
        HfSpawnEvent sevExit = {{sizeof sevExit, HF_ON_SPAWN, 10u}, 9010u, 810u, deliveries[7].entity, 1u};
        Spawn((HfMod*)&mod, &sevExit, NULL);
        CHECK(g_activeSortieCount == 1u);
        HfStateEvent leaveMission = {{sizeof leaveMission, HF_ON_STATE, 11u}, HF_STATE_MISSION, HF_STATE_SHIP};
        StateChange((HfMod*)&mod, &leaveMission, NULL);
        CHECK(g_activeSortieCount == 0u);
        CHECK(hellforge_mod_unload(&api, (HfMod*)&mod, HF_UNLOAD_REMOVE) == 0);
        CHECK(!armed && !applied);
        Snapshot cleared[DELIVERY_COUNT];
        CHECK(SnapshotAll(cleared));
        for (size_t i = 0; i < DELIVERY_COUNT; i++) CHECK(Model(&cleared[i]) == FLIGHT);
    }

    printf("%d checks passed\n", checks);
    return 0;
}
