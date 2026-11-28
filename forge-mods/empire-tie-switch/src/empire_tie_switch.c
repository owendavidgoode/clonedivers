/* SDK 16 native driver using the exported bridge's raw-field ownership/replay layer.
 * 0.4.0: Adds Clonedivers r26 Republic air wing closure support (ARC-170 strafe/110mm, BTL-B Y-Wing
 * airstrike/500kg/cluster, LAAT/i Gunship smoke/gas/napalm) alongside EmpireDivers r52 TIE Fighter/Bomber.
 * 0.3.1: Expands staged-closure verification across all 5 texture tiers ([0]=full, [1]=lighter,
 * [2]=reduced1024, [3]=reduced512, [4]=potato256) with cross-part tier coherence.
 * 0.3.0: Acquires/holds 0x2C26BC4C6592FA14 (eagle_base) residency via SDK load_package() first, falling back to
 * build-25480438 pinned game.dll RefcountedPackageSystem when kRefPackages omits eagle_base
 * (fail-closed if pins mismatch), plus 4-player concurrent Eagle sortie tracking. */
#include "hellforge_mod.h"
#include "hellforge_lua.h"
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#include "assets.h"
#include "clone_eagle_assets.h"
#include "asset_gate.h"
#include "package_pin.h"

#define FLIGHT 0x4e401cc877093220ull
#define EAGLE_PACKAGE 0x2c26bc4c6592fa14ull
#define UNIT "UnitComponentData"
#define EAGLE "EagleComponentData"

typedef struct Delivery { const char* name; uint64_t entity, model, cloneModel; } Delivery;
static const Delivery deliveries[] = {
    {"strafe",    0x23a60681dd4383ecull, TIE_FIGHTER, CLONE_FIGHTER},
    {"airstrike", 0x2ea01cb1676aca29ull, TIE_BOMBER,  CLONE_BOMBER},
    {"smoke",     0x1b3bcadabc7ef8d6ull, TIE_BOMBER,  CLONE_GUNSHIP},
    {"500kg",     0xe44b691dc039a505ull, TIE_BOMBER,  CLONE_BOMBER},
    {"cluster",   0x9d4f7cb4eb34515dull, TIE_BOMBER,  CLONE_BOMBER},
    {"gas",       0xdbb286ad7ed9df96ull, TIE_BOMBER,  CLONE_GUNSHIP},
    {"napalm",    0x27bb558c893383ccull, TIE_BOMBER,  CLONE_GUNSHIP},
    {"110mm",     0x397792815583da29ull, TIE_FIGHTER, CLONE_FIGHTER}
};
#define DELIVERY_COUNT (sizeof deliveries / sizeof deliveries[0])

static uint64_t DeliveryModel(size_t i)
{
    return (g_activePack == TIE_PACK_CLONES) ? deliveries[i].cloneModel : deliveries[i].model;
}

static const char* IdleReadyStatus(void)
{
    return (g_activePack == TIE_PACK_CLONES)
        ? "ARC-170 strafe/110mm, Y-Wing bombing, LAAT/i support; package resident"
        : "Fighter strafe/110mm, Bomber bombing; package resident";
}

typedef struct Snapshot { uintptr_t unitAt, eagleAt; unsigned char unit[136], eagle[152]; } Snapshot;

typedef struct TieSortie {
    int active;
    uint32_t sortieId;
    uint32_t entity;
    uint32_t network;
    uint32_t authority;
    int deliveryIdx;
    int playerSlot;
    uint64_t resource;
    uint64_t model;
    uint64_t startedMs;
} TieSortie;

typedef struct TieBeacon {
    int active;
    uint32_t type;
    uint32_t thrower;
    int playerSlot;
    float position[3];
    uint64_t timestampMs;
} TieBeacon;

static const HfApi* hf;
static HfMod* bridgeMod;
static int (*bridgeInfo)(HfLuaInfo*);
static HfMod* (*bridgeRegister)(const char*);
static int (*bridgeSet)(HfMod*, const char*, uint64_t, uint32_t, uint32_t, double, uint64_t, uint32_t);
static int (*bridgeClear)(HfMod*, const char*, uint64_t, uint32_t, uint32_t);
static int (*bridgeGet)(HfMod*, const char*, uint64_t, uint32_t, uint32_t, HfLuaValue*);
static volatile LONG entitiesReady;
static int armed, failed, applied, lastPackage = -1;
static uint64_t lastCheck;

/* Build-25480438 direct RefcountedPackageSystem fallback state (only when hf->load_package lacks eagle_base). */
static int directPkgMode;      /* 0=try SDK load_package, 1=pinned direct RefcountedPackageSystem, -1=refused */
static int directPinsChecked;
static int directPinsOk;
static int directRequested;
static int directResident;
static uint64_t directStartMs;
static void (*directReqOverride)(void*, const uint64_t*, uint32_t);
static uint8_t (*directIdleOverride)(void*);

/* 4-player concurrent Eagle sortie tracker */
static TieSortie g_sorties[TIE_MAX_SORTIES];
static TieBeacon g_beacons[TIE_MAX_BEACONS];
static uint32_t g_nextSortieId = 1u;
static unsigned g_beaconCursor;
static unsigned g_activeSortieCount;
static unsigned g_peakConcurrentSorties;

static int SnapshotAll(Snapshot out[DELIVERY_COUNT])
{
    for (size_t i = 0; i < DELIVERY_COUNT; i++)
    {
        uint32_t unitStride = 0, eagleStride = 0;
        out[i].unitAt = hf->component_record(UNIT, deliveries[i].entity, &unitStride);
        out[i].eagleAt = hf->component_record(EAGLE, deliveries[i].entity, &eagleStride);
        /* component_record refuses shared records. Also require disjoint delivery storage. */
        if (!out[i].unitAt || !out[i].eagleAt || unitStride != 136 || eagleStride != 152 ||
            hf->read(out[i].unitAt, out[i].unit, 136) || hf->read(out[i].eagleAt, out[i].eagle, 152)) return 0;
        for (size_t j = 0; j < i; j++) if (out[i].unitAt == out[j].unitAt || out[i].eagleAt == out[j].eagleAt) return 0;
    }
    return 1;
}

static uint64_t Model(const Snapshot* s) { uint64_t value; memcpy(&value, s->unit, 8); return value; }
static int ModelsCorrect(const Snapshot all[DELIVERY_COUNT])
{
    for (size_t i = 0; i < DELIVERY_COUNT; i++) if (Model(&all[i]) != DeliveryModel(i)) return 0;
    return 1;
}

static int ClearAll(HfMod* mod)
{
    if (!bridgeMod || !bridgeClear) return 1;
    int ok = 1;
    for (size_t i = 0; i < DELIVERY_COUNT; i++)
        if (!bridgeClear(bridgeMod, UNIT, deliveries[i].entity, 0, HF_TYPE_U64)) ok = 0;
    hf->log(mod, "cleared bridge model edits result=%d", ok);
    return ok;
}

static void ClearSorties(void)
{
    memset(g_sorties, 0, sizeof g_sorties);
    memset(g_beacons, 0, sizeof g_beacons);
    g_activeSortieCount = 0;
}

static void RefreshSortieCount(void)
{
    unsigned count = 0;
    for (unsigned i = 0; i < TIE_MAX_SORTIES; i++) if (g_sorties[i].active) count++;
    g_activeSortieCount = count;
    if (count > g_peakConcurrentSorties) g_peakConcurrentSorties = count;
}

static void UpdateActiveSortieStatus(HfMod* mod)
{
    if (!armed || failed || !hf || !hf->set_status) return;
    if (g_activeSortieCount > 0)
    {
        char status[128];
        snprintf(status, sizeof status, "%s active sorties=%u (peak=%u); package resident",
                 g_activePack == TIE_PACK_CLONES ? "Republic air wing" : "TIE",
                 g_activeSortieCount, g_peakConcurrentSorties);
        hf->set_status(mod, status);
    }
    else hf->set_status(mod, IdleReadyStatus());
}

static void PruneExpiredSorties(HfMod* mod, uint64_t now)
{
    int changed = 0;
    for (unsigned i = 0; i < TIE_MAX_SORTIES; i++)
    {
        if (!g_sorties[i].active) continue;
        if (now >= g_sorties[i].startedMs && (now - g_sorties[i].startedMs) >= TIE_SORTIE_TTL_MS)
        {
            hf->log(mod, "sortie end id=%u delivery=%s entity=%u reason=ttl_expired",
                g_sorties[i].sortieId, deliveries[g_sorties[i].deliveryIdx].name, g_sorties[i].entity);
            memset(&g_sorties[i], 0, sizeof g_sorties[i]);
            changed = 1;
        }
    }
    if (changed)
    {
        RefreshSortieCount();
        UpdateActiveSortieStatus(mod);
    }
}

static int ResolvePlayerSlot(HfMod* mod, uint32_t entity)
{
    if (!hf || !hf->read || !hf->global || !hf->entity_of_network ||
        !entity || entity == UINT32_MAX) return -1;
    uintptr_t pm = (uintptr_t)hf_global_PlayerManager(mod);
    if (pm < 65536) return -1;
    uint32_t count = 0;
    if (hf->read(pm + 0x84u, &count, 4) != 0 || count == 0 || count > 4u) return -1;
    for (uint32_t i = 0; i < count; i++)
    {
        uint32_t net = 0;
        if (hf->read(pm + 0x3A8u + 0x20u * i, &net, 4) != 0 || net == 0x7FFFu) continue;
        if (hf->entity_of_network(mod, net) == entity) return (int)i;
    }
    return -1;
}

static void Fail(HfMod* mod, const char* why)
{
    failed = 1; armed = 0;
    ClearAll(mod);
    ClearSorties();
    hf->log(mod, "off: %s", why);
    hf->set_status(mod, why);
}

static int Apply(HfMod* mod)
{
    Snapshot before[DELIVERY_COUNT], after[DELIVERY_COUNT];
    if (!SnapshotAll(before))
    { Fail(mod, "delivery records absent, shared or wrong stride"); return 0; }
    for (size_t i = 0; i < DELIVERY_COUNT; i++)
    {
        uint64_t target = DeliveryModel(i);
        uint64_t current = Model(&before[i]);
        HfLuaValue live;
        memset(&live, 0, sizeof live);
        if (!bridgeGet(bridgeMod, UNIT, deliveries[i].entity, 0, HF_TYPE_U64, &live) || live.now_u64 != current ||
            (current != FLIGHT && !(applied && current == target)))
        { Fail(mod, "delivery model conflict or unreadable bridge field"); return 0; }
    }
    for (size_t i = 0; i < DELIVERY_COUNT; i++)
    {
        uint64_t target = DeliveryModel(i);
        if (!bridgeSet(bridgeMod, UNIT, deliveries[i].entity, 0, HF_TYPE_U64, 0, target, 0))
        { Fail(mod, "model assignment refused, rollback requested"); return 0; }
    }
    if (!SnapshotAll(after) || !ModelsCorrect(after))
    { Fail(mod, "model readback failed, rollback requested"); return 0; }
    for (size_t i = 0; i < DELIVERY_COUNT; i++)
    {
        uint64_t target = DeliveryModel(i);
        if (after[i].unitAt != before[i].unitAt || after[i].eagleAt != before[i].eagleAt ||
            memcmp(after[i].unit+8, before[i].unit+8, 128) || memcmp(after[i].eagle, before[i].eagle, 152))
        { Fail(mod, "delivery storage or non-model controls changed"); return 0; }
        hf->log(mod, "assigned delivery=%s entity=%016llx model=%016llx unit=%p eagle=%p controls=exact package=resident",
            deliveries[i].name, (unsigned long long)deliveries[i].entity, (unsigned long long)target,
            (void*)after[i].unitAt, (void*)after[i].eagleAt);
    }
    applied = armed = 1;
    hf->set_status(mod, IdleReadyStatus());
    return 1;
}

static void CallPkgRequest(uintptr_t gbase, void* sys, const uint64_t* ids, uint32_t count)
{
    if (directReqOverride) { directReqOverride(sys, ids, count); return; }
    typedef void (*PkgReqFn)(void*, const uint64_t*, uint32_t);
    ((PkgReqFn)(gbase + TIE_PKG_REQUEST_RVA))(sys, ids, count);
}

static int CallPkgsIdle(uintptr_t gbase)
{
    if (directIdleOverride) return (int)directIdleOverride(NULL);
    typedef uint8_t (*PkgIdleFn)(void*);
    return (int)(((PkgIdleFn)(gbase + TIE_PKG_IDLE_RVA))(NULL));
}

/* Acquire/hold 0x2C26BC4C6592FA14 via build-25480438 pinned RefcountedPackageSystem when hf->load_package lacks it. */
static int AcquireEagleDirect(HfMod* mod, uint64_t now)
{
    if (directPkgMode < 0) return 0;
    if (directResident) return HF_PACKAGE_RESIDENT;

    if (!hf || !hf->read || !hf->read_code || !hf->module_base) {
        directPkgMode = -1;
        Fail(mod, "missing SDK read_code/module_base for direct eagle_base package pins");
        return 0;
    }

    size_t gsize = 0, esize = 0;
    uintptr_t gbase = hf->module_base("game.dll", &gsize);
    uintptr_t ebase = hf->module_base("helldivers2.exe", &esize);

    if (!directPinsChecked) {
        char err[192];
        err[0] = 0;
        directPinsChecked = 1;
        directPinsOk = TieVerifyPackagePins(hf->read_code, gbase, gsize, ebase, esize, err, sizeof err);
        if (!directPinsOk) {
            directPkgMode = -1;
            Fail(mod, err[0] ? err : "build 25480438 package pins mismatch; safe fallback disabled");
            return 0;
        }
        hf->log(mod, "verified build 25480438 RefcountedPackageSystem pins (game+0x%X, game+0x%X, exe+0x%X, exe+0x%X)",
            TIE_PKG_REQUEST_RVA, TIE_PKG_IDLE_RVA, TIE_EXE_HAS_LOADED_RVA, TIE_EXE_FIND_RVA);
    }
    if (!directPinsOk) { directPkgMode = -1; return 0; }

    /* Already resident in helldivers2.exe list? Fast-complete without mutating refmap unless needed. */
    if (!directRequested && TiePackageResidentExe(hf->read, ebase, esize, EAGLE_PACKAGE)) {
        directRequested = 1;
        directResident = 1;
        hf->log(mod, "Eagle package=%016llx already resident in engine list (direct fallback)",
            (unsigned long long)EAGLE_PACKAGE);
        return HF_PACKAGE_RESIDENT;
    }

    if (!directRequested) {
        uintptr_t pkgSys = 0, mapEntries = 0;
        uint32_t mapCap = 0, used = 0;
        uint64_t emptyKey = 0;
        int alreadyPresent = 0;
        if (!TieReadPtr(hf->read, gbase + TIE_PKG_SYSTEM_RVA, &pkgSys) ||
            !TieReadPtr(hf->read, pkgSys + 0x00u, &mapEntries) ||
            hf->read(pkgSys + 0x08u, &mapCap, 4) != 0 ||
            hf->read(pkgSys + 0x10u, &emptyKey, 8) != 0 ||
            mapCap < 16u || mapCap > 65536u || (mapCap & (mapCap - 1u)) != 0u) {
            if (now - directStartMs > TIE_PKG_TIMEOUT_MS) {
                directPkgMode = -1;
                Fail(mod, "RefcountedPackageSystem unavailable for direct eagle_base load (timeout)");
                return 0;
            }
            return HF_PACKAGE_LOADING;
        }

        uint8_t* mapBuf = (uint8_t*)malloc((size_t)mapCap * 16u);
        if (!mapBuf || hf->read(mapEntries, mapBuf, (size_t)mapCap * 16u) != 0) {
            free(mapBuf);
            directPkgMode = -1;
            Fail(mod, "unable to inspect game package reference map");
            return 0;
        }
        int headroom = TieRefMapHasHeadroom(mapBuf, mapCap, emptyKey, EAGLE_PACKAGE, &used, &alreadyPresent);
        free(mapBuf);

        if (!headroom) {
            directPkgMode = -1;
            char msg[160];
            snprintf(msg, sizeof msg, "Eagle package refmap >= 75%% full (%u/%u); refusing direct request", used, mapCap);
            Fail(mod, msg);
            return 0;
        }

        if (!alreadyPresent) {
            uint64_t pkgId = EAGLE_PACKAGE;
            CallPkgRequest(gbase, (void*)pkgSys, &pkgId, 1u);
            hf->log(mod, "direct RefcountedPackageRequest(%016llx) issued (map used=%u/%u)",
                (unsigned long long)EAGLE_PACKAGE, used, mapCap);
        } else {
            hf->log(mod, "direct RefcountedPackageSystem already holds %016llx (map used=%u/%u)",
                (unsigned long long)EAGLE_PACKAGE, used, mapCap);
        }
        directRequested = 1;
        directStartMs = now;
    }

    if (TiePackageResidentExe(hf->read, ebase, esize, EAGLE_PACKAGE) && CallPkgsIdle(gbase)) {
        directResident = 1;
        hf->log(mod, "direct Eagle package=%016llx verified resident in engine list",
            (unsigned long long)EAGLE_PACKAGE);
        return HF_PACKAGE_RESIDENT;
    }

    if (now >= directStartMs && (now - directStartMs) > TIE_PKG_TIMEOUT_MS) {
        directPkgMode = -1;
        Fail(mod, "direct eagle_base package load timed out after 90s");
        return 0;
    }
    return HF_PACKAGE_LOADING;
}

static void Ready(HfMod* mod, HfEvent event, void* user)
{
    (void)event; (void)user;
    hf->log(mod, "entity data ready; pending definition/replay verification");
    InterlockedExchange(&entitiesReady, 1);
}

static void StateChange(HfMod* mod, const HfStateEvent* event, void* user)
{
    (void)user;
    if (event && event->to != HF_STATE_MISSION && g_activeSortieCount > 0)
    {
        hf->log(mod, "cleared %u active Eagle sorties on mission exit (to=%u)", g_activeSortieCount, event->to);
        ClearSorties();
        if (armed && !failed && hf && hf->set_status)
            hf->set_status(mod, IdleReadyStatus());
    }
}

static void Tick(HfMod* mod, float dt, void* user)
{
    (void)dt; (void)user;
    if (failed || !entitiesReady) return;
    if (!bridgeMod)
    {
        bridgeMod = bridgeRegister("clonedivers.empire-tie-switch");
        if (!bridgeMod) { Fail(mod, "lua bridge registration failed"); return; }
    }
    uint64_t now = GetTickCount64();
    PruneExpiredSorties(mod, now);
    if (now - lastCheck < 1000) return;
    lastCheck = now;

    int package = 0;
    if (directPkgMode == 0)
    {
        package = hf->load_package ? hf->load_package(mod, EAGLE_PACKAGE) : 0;
        if (package == 0)
        {
            hf->log(mod, "hf->load_package(%016llx) returned 0 (kRefPackages miss); trying build 25480438 direct package fallback",
                (unsigned long long)EAGLE_PACKAGE);
            directPkgMode = 1;
            directStartMs = now;
            package = AcquireEagleDirect(mod, now);
            if (failed) return;
        }
    }
    else if (directPkgMode == 1)
    {
        package = AcquireEagleDirect(mod, now);
        if (failed) return;
    }

    if (package != lastPackage)
    {
        hf->log(mod, "Eagle package=%016llx state=%d direct=%d closure_parts=%zu",
            (unsigned long long)EAGLE_PACKAGE, package, directPkgMode, activeAssetCount);
        lastPackage = package;
    }
    if (!package) { Fail(mod, "Eagle package request failed"); return; }
    if (package != HF_PACKAGE_RESIDENT) return;
    int inMission = armed && hf->size >= offsetof(HfApi, game_state) + sizeof hf->game_state &&
                    hf->game_state && hf->game_state(mod) == HF_STATE_MISSION;
    if (!inMission && !AssetsUnchanged()) { Fail(mod, assetError); return; }
    if (!armed) { Apply(mod); return; }
    Snapshot current[DELIVERY_COUNT];
    if (!SnapshotAll(current) || !ModelsCorrect(current)) Fail(mod, "delivery model ownership lost");
}

static void Spawn(HfMod* mod, const HfSpawnEvent* event, void* user)
{
    (void)user;
    if (!event) return;
    uint64_t now = GetTickCount64();
    PruneExpiredSorties(mod, now);

    for (size_t i = 0; i < DELIVERY_COUNT; i++) if (event->resource == deliveries[i].entity)
    {
        Snapshot current[DELIVERY_COUNT];
        int correct = armed && SnapshotAll(current) && ModelsCorrect(current);
        uint64_t expectedModel = DeliveryModel(i);

        int matchedSlot = -1;
        int bestBeacon = -1;
        uint64_t bestTime = 0;
        for (unsigned b = 0; b < TIE_MAX_BEACONS; b++)
        {
            if (!g_beacons[b].active) continue;
            if (now >= g_beacons[b].timestampMs && (now - g_beacons[b].timestampMs) <= 10000ull &&
                g_beacons[b].timestampMs >= bestTime)
            {
                bestBeacon = (int)b;
                bestTime = g_beacons[b].timestampMs;
            }
        }
        if (bestBeacon >= 0)
        {
            matchedSlot = g_beacons[bestBeacon].playerSlot;
            g_beacons[bestBeacon].active = 0;
        }

        int slotIdx = -1;
        int oldestIdx = 0;
        for (unsigned s = 0; s < TIE_MAX_SORTIES; s++)
        {
            if (g_sorties[s].active && g_sorties[s].entity == event->entity) { slotIdx = (int)s; break; }
            if (!g_sorties[s].active && slotIdx < 0) slotIdx = (int)s;
            if (g_sorties[s].startedMs < g_sorties[oldestIdx].startedMs) oldestIdx = (int)s;
        }
        if (slotIdx < 0) slotIdx = oldestIdx;

        TieSortie* slot = &g_sorties[slotIdx];
        slot->active = 1;
        slot->sortieId = g_nextSortieId++;
        slot->entity = event->entity;
        slot->network = event->network;
        slot->authority = event->authority;
        slot->deliveryIdx = (int)i;
        slot->playerSlot = matchedSlot;
        slot->resource = event->resource;
        slot->model = expectedModel;
        slot->startedMs = now;
        RefreshSortieCount();
        UpdateActiveSortieStatus(mod);

        hf->log(mod, "spawn delivery=%s resource=%016llx entity=%u network=%u authority=%u slot=%d expected_model=%016llx definition_verified=%d package_state=%d sortie_id=%u active_sorties=%u",
            deliveries[i].name, (unsigned long long)event->resource, event->entity, event->network,
            event->authority, matchedSlot, (unsigned long long)expectedModel, correct, lastPackage,
            slot->sortieId, g_activeSortieCount);
    }
}

static void Stratagem(HfMod* mod, const HfStratagemEvent* event, void* user)
{
    (void)user;
    if (!event) return;
    uint64_t now = GetTickCount64();
    int pslot = ResolvePlayerSlot(mod, event->thrower);
    TieBeacon* b = &g_beacons[g_beaconCursor % TIE_MAX_BEACONS];
    g_beaconCursor++;
    b->active = 1;
    b->type = event->type;
    b->thrower = event->thrower;
    b->playerSlot = pslot;
    memcpy(b->position, event->position, sizeof b->position);
    b->timestampMs = now;
    hf->log(mod, "beacon type=%u thrower=%u slot=%d armed=%d package_state=%d active_sorties=%u",
        event->type, event->thrower, pslot, armed, lastPackage, g_activeSortieCount);
}

HF_MOD_EXPORT int hellforge_mod_unload(const HfApi* api, HfMod* mod, uint32_t reason)
{
    (void)api; (void)reason;
    if (bridgeMod) ClearAll(mod);
    ClearSorties();
    armed = 0; applied = 0;
    return 0;
}

HF_MOD_EXPORT int hellforge_mod_init(const HfApi* api, HfMod* mod)
{
    hf = api;
    if (api->version < 16 || api->size < offsetof(HfApi, entity_of_network)+sizeof api->entity_of_network ||
        strcmp(api->build_id(), "25480438")) return 1;
    HMODULE loader = GetModuleHandleW(L"hellforge_modloader.dll");
#define BIND(v, name) v = (void*)(loader ? GetProcAddress(loader, name) : NULL)
    BIND(bridgeInfo, "hellforge_lua_info"); BIND(bridgeRegister, "hellforge_lua_mod");
    BIND(bridgeSet, "hellforge_lua_set"); BIND(bridgeClear, "hellforge_lua_clear"); BIND(bridgeGet, "hellforge_lua_get");
#undef BIND
    HfLuaInfo info;
    memset(&info, 0, sizeof info);
    info.size = sizeof info;
    if (!bridgeInfo || !bridgeRegister || !bridgeSet || !bridgeClear || !bridgeGet || !bridgeInfo(&info) ||
        info.sdk < 16 || info.bridge != 3 || !info.build || strcmp(info.build, "25480438")) return 1;
    HMODULE bcrypt = LoadLibraryW(L"bcrypt.dll");
    openHash = bcrypt ? (void*)GetProcAddress(bcrypt, "BCryptOpenAlgorithmProvider") : NULL;
    hashBytes = bcrypt ? (void*)GetProcAddress(bcrypt, "BCryptHash") : NULL;
    if (!openHash || !hashBytes || openHash(&sha, BCRYPT_SHA256_ALGORITHM, NULL, 0) < 0) return 1;
    wchar_t executable[1024], directory[1100]; DWORD length = GetModuleFileNameW(NULL, executable, 1024);
    wchar_t* slash = wcsrchr(executable, L'\\');
    if (!length || length >= 1024 || !slash) return 1;
    *slash = 0;
    if (swprintf(directory, 1100, L"%ls\\..\\data", executable) < 0) return 1;
    if (!ScanAssets(directory))
    { hf->log(mod, "off: %s", assetError); hf->set_status(mod, assetError); return 0; }
    if (hf_on_entities_ready(mod, Ready, NULL) || hf_on_game_thread(mod, Tick, NULL) ||
        hf_on_spawn(mod, Spawn, NULL, 0) || hf_on_stratagem(mod, Stratagem, NULL, 0)) return 1;
    hf_on_state(mod, StateChange, NULL, 0);
    hf->log(mod, "qualified staged closure parts=%zu pack=%s; waiting for Eagle package residency (v0.4.0)",
        activeAssetCount, g_activePack == TIE_PACK_CLONES ? "clones" : "empire");
    return 0;
}
