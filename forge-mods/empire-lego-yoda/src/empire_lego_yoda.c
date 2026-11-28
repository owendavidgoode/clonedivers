/* Offline prototype. Read-only game research: HellForge 0.4/0.5 passives.c / event_sources.c / event_polls.c / globals.json,
 * derived from HD2Runtime by SkyeShade (see CREDITS.md). No engine sound calls, hooks or memory writes. */
#include "hellforge_mod.h"
#include <windows.h>
#include <mmsystem.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#include "assets.h"
#include "visual_gate.h"
#include "passive_pins.h"
#if defined(__has_include) && __has_include("../../shared/clonedivers_em_layout.h")
#include "../../shared/clonedivers_em_layout.h"
#else
#include "clonedivers_em_layout.h"
#endif

#define YODA_SLOTS 4u
#define YODA_CONSUMED_RING 8u

/* Read-only avatar scene-graph constants (build 25480438, shared with lego-compact-leaf-writer & event_polls.c) */
#define SALUTE_AVATAR_RESOURCE     0x4D1C334D294DFA97ull
#define SALUTE_AVATAR_NODES        107u
#define SALUTE_EM_RVA              0x346BF98u
#define SALUTE_EM_ENTITY_MAP       0xF1AEB0u
#define SALUTE_INVENTORY_RVA       0x3326738u
#define SALUTE_EXE_UNITS_RVA       0x1A100F0u
#define SALUTE_EXE_UNIT_VTABLE_RVA 0x1676D80u
#define SALUTE_MAX_WAV_BYTES       (4u * 1024u * 1024u)
#define SALUTE_HOLD_TICKS_REQUIRED 3u
#define SALUTE_RELEASE_TICKS       2u
#define SALUTE_COOLDOWN_MS         1500ull

/* Helldiver avatar (4d1c334d294dfa97) verified node indices */
#define AV_NODE_ROOT       4u
#define AV_NODE_HIPS       9u
#define AV_NODE_CHEST      28u
#define AV_NODE_L_SHOULDER 44u
#define AV_NODE_L_HAND     47u
#define AV_NODE_R_SHOULDER 67u
#define AV_NODE_R_ELBOW    69u
#define AV_NODE_R_HAND     71u
#define AV_NODE_HEAD       90u

typedef struct Snapshot
{
    uint64_t peer, observed;
    uint32_t player, avatar, network, kit, body;
    uintptr_t record;
    unsigned char applied[68];
    int visual;
    uint32_t slot;
} Snapshot;

typedef struct SlotTracker
{
    int active;
    Snapshot saved;
    uint64_t consumedPeer;
    uint32_t consumedAvatar, consumedNetwork;
    int reportedPollRejection;
} SlotTracker;

typedef struct ConsumedTuple
{
    uint64_t peer;
    uint32_t avatar, network;
} ConsumedTuple;

static const HfApi* hf;
static uintptr_t game;
static uintptr_t exeBase;
static int proven, ready;
static int saluteReady;
static volatile LONG gameLoaded;
static Snapshot saved;
static SlotTracker slotStates[YODA_SLOTS];
static ConsumedTuple consumedRing[YODA_CONSUMED_RING];
static unsigned consumedRingCursor;
static int activeSlot = -1;
static uint64_t lastSample, sampleEvery = 100;
static uint32_t consumedAvatar, consumedNetwork;
static uint64_t consumedPeer;
static int reportedPollRejection;
static unsigned char audio[29454];
static unsigned char* saluteAudio;
static uint32_t saluteAudioSize;
static uint32_t saluteHoldCount;
static uint32_t saluteReleaseCount;
static int saluteLatched;
static uint32_t saluteLastAvatar;
static float saluteLastRoot[3];
static int saluteHasLastRoot;
static uint64_t saluteLastSampleMs;
static uint64_t saluteLastFiredMs;
static BOOL (WINAPI *play)(LPCWSTR, HMODULE, DWORD);

static int Read(uintptr_t p, void* out, size_t n) { return p >= 65536 && !hf->read(p, out, n); }
static int Pointer(uintptr_t p, uintptr_t* out) { return Read(p, out, 8) && *out >= 65536; }

static void ResetSaluteTracker(void)
{
    saluteHoldCount = 0;
    saluteReleaseCount = 0;
    saluteLatched = 0;
    saluteLastAvatar = 0;
    saluteHasLastRoot = 0;
    saluteLastSampleMs = 0;
    memset(saluteLastRoot, 0, sizeof saluteLastRoot);
}

static int ValidateWaveBuffer(const unsigned char* buf, uint32_t size)
{
    if (!buf || size < 44u || size > SALUTE_MAX_WAV_BYTES) return 0;
    if (memcmp(buf, "RIFF", 4) != 0 || memcmp(buf + 8, "WAVE", 4) != 0) return 0;
    uint32_t riffSize = 0;
    memcpy(&riffSize, buf + 4, 4);
    if (riffSize < 36u) return 0;

    int hasFmt = 0, hasData = 0;
    uint32_t at = 12u;
    while (at + 8u <= size)
    {
        char tag[4];
        uint32_t chunkSz = 0;
        memcpy(tag, buf + at, 4);
        memcpy(&chunkSz, buf + at + 4u, 4);
        if (chunkSz > size - (at + 8u)) return 0;
        if (memcmp(tag, "fmt ", 4) == 0)
        {
            if (chunkSz < 16u) return 0;
            uint16_t fmtTag = 0, channels = 0, bits = 0;
            uint32_t sr = 0;
            memcpy(&fmtTag, buf + at + 8u, 2);
            memcpy(&channels, buf + at + 10u, 2);
            memcpy(&sr, buf + at + 12u, 4);
            memcpy(&bits, buf + at + 22u, 2);
            if (!(fmtTag == 1u || fmtTag == 3u || fmtTag == 0xFFFEu)) return 0;
            if (channels < 1u || channels > 2u || sr < 8000u || sr > 96000u || (bits != 8u && bits != 16u && bits != 24u && bits != 32u))
                return 0;
            hasFmt = 1;
        }
        else if (memcmp(tag, "data", 4) == 0)
        {
            if (chunkSz == 0u) return 0;
            hasData = 1;
        }
        uint32_t step = 8u + chunkSz + (chunkSz & 1u);
        if (step > size - at) { at = size; break; }
        at += step;
    }
    return hasFmt && hasData;
}

static int LoadOptionalSaluteWave(const wchar_t* modFolder)
{
    if (saluteAudio) { free(saluteAudio); saluteAudio = NULL; }
    saluteAudioSize = 0;
    saluteReady = 0;
    if (!modFolder || !*modFolder) return 0;
    wchar_t path[1100];
    if (swprintf(path, 1100, L"%ls\\audio\\stormtrooper-salute.wav", modFolder) < 0) return 0;
    HANDLE f = OpenRead(path);
    if (f == INVALID_HANDLE_VALUE) return 0;
    LARGE_INTEGER sz;
    int ok = GetFileSizeEx(f, &sz) && sz.QuadPart >= 44 && sz.QuadPart <= (LONGLONG)SALUTE_MAX_WAV_BYTES;
    unsigned char* buf = ok ? (unsigned char*)malloc((size_t)sz.QuadPart) : NULL;
    ok = buf && ReadAt(f, 0, buf, (DWORD)sz.QuadPart) && ValidateWaveBuffer(buf, (uint32_t)sz.QuadPart);
    CloseHandle(f);
    if (!ok)
    {
        free(buf);
        return 0;
    }
    saluteAudio = buf;
    saluteAudioSize = (uint32_t)sz.QuadPart;
    saluteReady = 1;
    return 1;
}

static int Prove(HfMod* mod)
{
    if (proven) return proven > 0;
    game = hf->module_base("game.dll", NULL);
    if (!game) return 0;
    if (!exeBase) exeBase = hf->module_base("helldivers2.exe", NULL);
    for (size_t i = 0; i < sizeof kPassivePins / sizeof kPassivePins[0]; i++)
    {
        unsigned char bytes[8];
        if (hf->read_code(game + kPassivePins[i].rva, bytes, kPassivePins[i].size) || memcmp(bytes, kPassivePins[i].bytes, kPassivePins[i].size))
        {
            proven = -1;
            hf->log(mod, "off: customization code pin changed game.dll+0x%X", kPassivePins[i].rva);
            hf->set_status(mod, "off: customization code differs from build 25480438 research");
            return 0;
        }
    }
    proven = 1;
    return 1;
}

/* Match local slot across 0..count-1 by peer (supports 1-4 co-op players, slot 0..3 compaction-safe). */
static int LocalSlot(HfMod* mod, Snapshot* out, uint32_t* lifecycle, int* slotOut)
{
    uintptr_t players = (uintptr_t)hf_global_PlayerManager(mod), user = (uintptr_t)hf_global_LocalUser(mod);
    uint32_t count; uint64_t peer;
    if (!Read(players + 0x84, &count, 4) || count > YODA_SLOTS || !Read(user + 0xB398, &peer, 8) || !peer) return 0;
    int found = 0, matchedSlot = -1;
    for (uint32_t i = 0; i < count; i++)
    {
        uint64_t candidate; uintptr_t descriptor;
        if (!Read(players + 0x2C8 + 0x38 * i, &candidate, 8)) return 0;
        if (candidate != peer) continue;
        if (found++ || !Pointer(players + 0xE8 + 8 * i, &descriptor) || !Read(descriptor + 8, &out->player, 4) ||
            !Read(players + 0x2E0 + 0x38 * i, lifecycle, 4) || !Read(players + 0x3A8 + 0x20 * i, &out->network, 4)) return 0;
        matchedSlot = (int)i;
    }
    out->peer = peer;
    out->slot = matchedSlot >= 0 ? (uint32_t)matchedSlot : 0u;
    if (slotOut) *slotOut = matchedSlot;
    return found == 1 && matchedSlot >= 0 && out->player && out->player != UINT32_MAX;
}

static uintptr_t s_kitCacheMgr;
static uint32_t s_kitCacheCount;
static uint32_t s_kitCacheId;
static uintptr_t s_kitCachePtr;
static int s_kitCacheValid;

static int Armor(Snapshot* out)
{
    uintptr_t manager, slots, descriptor, kits;
    uint32_t count, capacity, empty, multiplier, index = UINT32_MAX, carried, kitCount;
    if (!Pointer(game + 0x33264F8u, &manager) || !Read(manager + 0x8E8, &count, 4) || count > 4 ||
        !Pointer(manager + 0x930, &slots) || !Read(manager + 0x938, &capacity, 4) || !capacity || capacity > 64 || capacity % 2 ||
        !Read(manager + 0x93C, &empty, 4) || !Read(manager + 0x940, &multiplier, 4)) return 0;
    uint32_t start = out->player * multiplier;
    for (uint32_t i = 0; i < capacity; i++)
    {
        uint32_t entry[2];
        if (!Read(slots + ((start + i) % capacity) * 8u, entry, 8)) return 0;
        if (entry[0] == out->player) { index = entry[1]; break; }
        if (entry[0] == empty) break;
    }
    if (index >= count || !Pointer(manager + 0x948 + 8u * index, &descriptor) || !Read(descriptor + 8, &carried, 4) || carried != out->player) return 0;
    out->record = manager + 0x96C + 0x44u * index;
    if (!Read(out->record, out->applied, sizeof out->applied)) return 0;
    memcpy(&out->body, out->applied, 4); memcpy(&out->kit, out->applied + 12, 4);
    out->visual = -1;
    for (size_t v = 0; v < visualCount; v++)
        if (out->kit == visuals[v].kit && out->body == visuals[v].body && proofs[v].valid)
        { out->visual = (int)v; break; }
    if (out->visual < 0 || !Pointer(manager, &kits) || !Read(manager + 8, &kitCount, 4) || !kitCount || kitCount > 2048) return 0;
    if (s_kitCacheValid && saved.avatar != 0 && saved.kit == out->kit &&
        s_kitCacheMgr == kits && s_kitCacheCount == kitCount && s_kitCacheId == out->kit)
    {
        uint32_t cachedId = 0, cachedType = 0;
        if (Read(s_kitCachePtr, &cachedId, 4) && cachedId == out->kit &&
            Read(s_kitCachePtr + 40, &cachedType, 4) && cachedType == 0)
            return 1;
        s_kitCacheValid = 0;
    }
    int matches = 0;
    uintptr_t matchedKit = 0;
    for (uint32_t i = 0; i < kitCount; i++)
    {
        uintptr_t kit; uint32_t id, type;
        if (!Pointer(kits + 8u * i, &kit) || !Read(kit, &id, 4)) return 0;
        if (id != out->kit) continue;
        if (!Read(kit + 40, &type, 4) || type != 0) return 0;
        matchedKit = kit;
        matches++;
    }
    if (matches == 1)
    {
        s_kitCacheMgr = kits;
        s_kitCacheCount = kitCount;
        s_kitCacheId = out->kit;
        s_kitCachePtr = matchedKit;
        s_kitCacheValid = 1;
        return 1;
    }
    return 0;
}

static int Life(HfMod* mod, uint32_t avatar, uint32_t* life)
{
    static uintptr_t lastHealth;
    static uint32_t lastAvatar, lastIndex = UINT32_MAX;
    uintptr_t health = (uintptr_t)hf_global_HealthManager(mod), descriptors, records;
    uint32_t count;
    if (!Read(health + 0x1020, &count, 4) || count > 16384 || !Pointer(health + 0x1048, &descriptors) || !Pointer(health + 0x1058, &records)) return 0;
    if (health == lastHealth && avatar == lastAvatar && lastIndex < count)
    {
        uintptr_t descriptor; uint32_t entity;
        if (Pointer(descriptors + 8u * lastIndex, &descriptor) && Read(descriptor + 8, &entity, 4) && entity == avatar)
            return Read(records + 0x1B8u * lastIndex + 0x19C, life, 4);
    }
    /* Fast O(1) HealthManager + 0x1030 hash-map lookup when populated (3,000-unit safe), falling back cleanly if unpopulated. */
    uintptr_t buckets = 0;
    uint32_t hcap = 0, hempty = 0, hmult = 0;
    if (count > 0 && Pointer(health + 0x1030, &buckets) &&
        Read(health + 0x1038, &hcap, 4) && hcap > 0 && hcap <= 262144u && (hcap & (hcap - 1u)) == 0u &&
        Read(health + 0x103C, &hempty, 4) && Read(health + 0x1040, &hmult, 4))
    {
        uint32_t start = avatar * hmult;
        uint32_t probes = hcap < 256u ? hcap : 256u;
        for (uint32_t p = 0; p < probes; p++)
        {
            uint32_t entry[2];
            if (!Read(buckets + (uintptr_t)((start + p) & (hcap - 1u)) * 8u, entry, 8)) break;
            if (entry[0] == avatar)
            {
                uint32_t idx = entry[1];
                uintptr_t descriptor; uint32_t entity;
                if (idx < count && Pointer(descriptors + 8u * idx, &descriptor) &&
                    Read(descriptor + 8, &entity, 4) && entity == avatar &&
                    Read(records + 0x1B8u * idx + 0x19C, life, 4))
                {
                    lastHealth = health; lastAvatar = avatar; lastIndex = idx;
                    return 1;
                }
                break;
            }
            if (entry[0] == hempty) break;
        }
    }
    int found = 0;
    for (uint32_t i = 0; i < count; i++)
    {
        uintptr_t descriptor; uint32_t entity;
        if (!Pointer(descriptors + 8u * i, &descriptor)) continue;
        if (!Read(descriptor + 8, &entity, 4)) return 0;
        if (entity != avatar) continue;
        if (found++ || !Read(records + 0x1B8u * i + 0x19C, life, 4)) return 0;
        lastHealth = health; lastAvatar = avatar; lastIndex = i;
    }
    return found == 1;
}

/* Bounded u32 hash-map lookup matching HellForge event_polls.c EvpHash */
static int MapLookupU32(uintptr_t header, uint32_t key, uint32_t* valueOut)
{
    uint8_t h[20];
    if (!key || !Read(header, h, sizeof h)) return 0;
    uint64_t buckets = 0;
    uint32_t capacity = 0, empty = 0, multiplier = 0;
    memcpy(&buckets, h, 8);
    memcpy(&capacity, h + 8, 4);
    memcpy(&empty, h + 12, 4);
    memcpy(&multiplier, h + 16, 4);
    if (buckets < 0x10000ull || (buckets >> 47) != 0ull || !capacity || capacity > 1048576u || key == empty) return 0;
    uint32_t start = key * multiplier;
    uint32_t probes = capacity < 256u ? capacity : 256u;
    int isPow2 = (capacity & (capacity - 1u)) == 0u;
    for (uint32_t i = 0; i < probes; i++)
    {
        uint32_t slot = isPow2 ? ((start + i) & (capacity - 1u)) : ((start + i) % capacity);
        uint32_t pair[2];
        if (!Read((uintptr_t)buckets + (uintptr_t)slot * 8u, pair, 8)) return 0;
        if (pair[0] == key)
        {
            if (pair[1] == UINT32_MAX) return 0;
            *valueOut = pair[1];
            return 1;
        }
        if (pair[0] == empty) return 0;
    }
    return 0;
}

static int HoldingThrowableOrStratagem(HfMod* mod, uint32_t avatar)
{
    uintptr_t inv = (uintptr_t)hf_global_InventoryManager(mod);
    if (!inv && game) Pointer(game + SALUTE_INVENTORY_RVA, &inv);
    if (!inv) return 0;
    uint32_t recIdx = 0, selection = 0;
    uintptr_t records = 0;
    if (MapLookupU32(inv + 0x28u, avatar, &recIdx) && recIdx < 0x10000u &&
        Pointer(inv + 0x50u, &records) &&
        Read(records + (uintptr_t)recIdx * 0x30u + 0x1Cu, &selection, 4))
    {
        /* selection 4 or 5: held grenade / stratagem ball / carryable item */
        if (selection == 4u || selection == 5u) return 1;
    }
    return 0;
}

static int ReadAvatarWorldPoses(HfMod* mod, uint32_t avatar, float outPos[SALUTE_AVATAR_NODES][3])
{
    if (!avatar || avatar == UINT32_MAX) return 0;
    if (!exeBase) exeBase = hf->module_base("helldivers2.exe", NULL);
    if (!exeBase || !game) return 0;

    uintptr_t em = (uintptr_t)hf_global_EntityManager(mod);
    if (!em && !Pointer(game + SALUTE_EM_RVA, &em)) return 0;

    CdEmLayout layout = cd_em_resolve_hf(game, hf->read_code);
    uint32_t descIdx = 0;
    if (!MapLookupU32(em + SALUTE_EM_ENTITY_MAP, avatar, &descIdx) || descIdx >= layout.capacity) return 0;

    uintptr_t descAt = cd_em_record_at(em, layout.records_offset, descIdx);
    struct { uint64_t resource; uint32_t entity, reference, network, flags; } d, dAgain;
    if (!descAt || !Read(descAt, &d, sizeof d) || !Read(descAt, &dAgain, sizeof dAgain) || memcmp(&d, &dAgain, sizeof d) != 0)
        return 0;
    if (d.resource != SALUTE_AVATAR_RESOURCE || d.entity != avatar || !d.reference) return 0;

    uint32_t unitIdx = d.reference & 0x3FFFFFu;
    uint8_t unitGen = (uint8_t)((d.reference >> 22) & 0xFFu);

    uintptr_t um = 0, slots = 0, gens = 0, unit = 0, vtable = 0, world = 0;
    uint32_t ucap = 0, nodes = 0;
    uint8_t gen = 0;
    if (!Pointer(exeBase + SALUTE_EXE_UNITS_RVA, &um) ||
        !Read(um + 0x98u, &ucap, 4) || !ucap || ucap > 262144u || unitIdx >= ucap ||
        !Pointer(um + 0xA0u, &gens) || !Read(gens + unitIdx, &gen, 1) || gen != unitGen ||
        !Pointer(um + 0x88u, &slots) || !Pointer(slots + (uintptr_t)unitIdx * 8u, &unit) ||
        !Pointer(unit, &vtable) || vtable != exeBase + SALUTE_EXE_UNIT_VTABLE_RVA ||
        !Read(unit + 0x70u, &nodes, 4) || nodes != SALUTE_AVATAR_NODES ||
        !Pointer(unit + 0x88u, &world))
        return 0;

    float mats[SALUTE_AVATAR_NODES][16];
    if (!Read(world, mats, sizeof mats)) return 0;
    for (uint32_t i = 0; i < SALUTE_AVATAR_NODES; i++)
    {
        float x = mats[i][12], y = mats[i][13], z = mats[i][14];
        if (!isfinite(x) || !isfinite(y) || !isfinite(z) || fabsf(x) > 100000.0f || fabsf(y) > 100000.0f || fabsf(z) > 100000.0f)
            return 0;
        outPos[i][0] = x;
        outPos[i][1] = y;
        outPos[i][2] = z;
    }
    return 1;
}

static float VecDot(const float a[3], const float b[3]) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
static void VecSub(const float a[3], const float b[3], float out[3]) { out[0] = a[0] - b[0]; out[1] = a[1] - b[1]; out[2] = a[2] - b[2]; }
static float VecLen(const float v[3]) { return sqrtf(VecDot(v, v)); }
static float VecDist(const float a[3], const float b[3]) { float d[3]; VecSub(a, b, d); return VecLen(d); }

/* Matches both Helldivers 2 Super Earth Salute (right forearm vertical, fist raised at head height)
 * and Casual Salute (right hand at right temple), while rejecting weapon aim, reload, two-handed emotes,
 * stratagem/grenade throws, sprinting, and downed/ragdolled postures. */
static int IsSaluteGeometry(const float pos[SALUTE_AVATAR_NODES][3])
{
    float spine[3], up[3], shSpan[3], rightAxis[3];
    VecSub(pos[AV_NODE_HEAD], pos[AV_NODE_HIPS], spine);
    float spineLen = VecLen(spine);
    if (spineLen < 0.45f || spineLen > 1.35f) return 0;
    up[0] = spine[0] / spineLen; up[1] = spine[1] / spineLen; up[2] = spine[2] / spineLen;
    if (up[2] < 0.85f) return 0; /* Must be standing upright */

    VecSub(pos[AV_NODE_R_SHOULDER], pos[AV_NODE_L_SHOULDER], shSpan);
    float shLen = VecLen(shSpan);
    if (shLen < 0.22f || shLen > 0.75f) return 0;
    rightAxis[0] = shSpan[0] / shLen; rightAxis[1] = shSpan[1] / shLen; rightAxis[2] = shSpan[2] / shLen;

    /* Forward axis = up x rightAxis (+Y in bind pose) */
    float fwd[3] = {
        up[1] * rightAxis[2] - up[2] * rightAxis[1],
        up[2] * rightAxis[0] - up[0] * rightAxis[2],
        up[0] * rightAxis[1] - up[1] * rightAxis[0]
    };

    float rHandChest[3], rHandHead[3], rElbowChest[3], rForearm[3], headLHand[3];
    VecSub(pos[AV_NODE_R_HAND], pos[AV_NODE_CHEST], rHandChest);
    VecSub(pos[AV_NODE_R_HAND], pos[AV_NODE_HEAD], rHandHead);
    VecSub(pos[AV_NODE_R_ELBOW], pos[AV_NODE_CHEST], rElbowChest);
    VecSub(pos[AV_NODE_R_HAND], pos[AV_NODE_R_ELBOW], rForearm);
    VecSub(pos[AV_NODE_HEAD], pos[AV_NODE_L_HAND], headLHand);

    /* 1. Right hand raised high to head/helmet band */
    float rHandAboveChest = VecDot(rHandChest, up);
    float rHandRelHeadUp = VecDot(rHandHead, up);
    float rHandHeadDist = VecLen(rHandHead);
    if (rHandAboveChest < 0.22f || rHandRelHeadUp < -0.15f || rHandRelHeadUp > 0.24f) return 0;
    if (rHandHeadDist < 0.08f || rHandHeadDist > 0.62f) return 0;

    /* 2. Right elbow raised near shoulder level and extended right; forearm angled up */
    float rElbowAboveChest = VecDot(rElbowChest, up);
    float rElbowRight = VecDot(rElbowChest, rightAxis);
    float rHandRight = VecDot(rHandHead, rightAxis);
    float forearmLen = VecLen(rForearm);
    if (rElbowAboveChest < -0.06f || rElbowRight < 0.22f || rHandRight < 0.03f) return 0;
    if (forearmLen < 0.16f || forearmLen > 0.45f || VecDot(rForearm, up) < 0.08f) return 0;

    /* 3. Reject cocked-back throw / far-forward handshake or high-five */
    float rHandFwd = VecDot(rHandHead, fwd);
    if (rHandFwd < -0.18f || rHandFwd > 0.30f) return 0;

    /* 4. Left hand strictly down at side (rejects two-handed rifles, reloads, Hug, binoculars) */
    float lHandBelowHead = VecDot(headLHand, up);
    float handsApart = VecDist(pos[AV_NODE_L_HAND], pos[AV_NODE_R_HAND]);
    if (lHandBelowHead < 0.36f || handsApart < 0.45f) return 0;

    return 1;
}

static void PollSalute(HfMod* mod, uint32_t gameState, uint64_t clock)
{
    if (!saluteReady || !saluteAudio) return;
    if (gameState != HF_STATE_SHIP && gameState != HF_STATE_MISSION)
    {
        ResetSaluteTracker();
        return;
    }

    Snapshot now = {0};
    uint32_t lifecycle = 0, life = 0;
    int slotIdx = -1;
    if (!LocalSlot(mod, &now, &lifecycle, &slotIdx))
    {
        ResetSaluteTracker();
        return;
    }
    if (gameState == HF_STATE_MISSION && lifecycle != 3u)
    {
        ResetSaluteTracker();
        return;
    }
    now.avatar = hf->entity_of_network(mod, now.network);
    if (!now.avatar || now.avatar == UINT32_MAX)
    {
        ResetSaluteTracker();
        return;
    }
    if (gameState == HF_STATE_MISSION && (!Life(mod, now.avatar, &life) || life != 0u))
    {
        ResetSaluteTracker();
        return;
    }
    if (saluteLastAvatar != now.avatar)
    {
        ResetSaluteTracker();
        saluteLastAvatar = now.avatar;
    }

    float poses[SALUTE_AVATAR_NODES][3];
    if (!ReadAvatarWorldPoses(mod, now.avatar, poses))
    {
        ResetSaluteTracker();
        return;
    }

    /* Require stationary stance (< 0.40 m/s horizontal root speed) */
    int stationary = 1;
    if (saluteHasLastRoot && clock > saluteLastSampleMs)
    {
        float dtSec = (float)(clock - saluteLastSampleMs) * 0.001f;
        if (dtSec > 0.005f && dtSec < 1.0f)
        {
            float dx = poses[AV_NODE_ROOT][0] - saluteLastRoot[0];
            float dy = poses[AV_NODE_ROOT][1] - saluteLastRoot[1];
            float horizSpeed = sqrtf(dx * dx + dy * dy) / dtSec;
            if (horizSpeed > 0.45f) stationary = 0;
        }
    }
    memcpy(saluteLastRoot, poses[AV_NODE_ROOT], sizeof saluteLastRoot);
    saluteHasLastRoot = 1;
    saluteLastSampleMs = clock;

    int blockedItem = (gameState == HF_STATE_MISSION) ? HoldingThrowableOrStratagem(mod, now.avatar) : 0;
    int inSalute = stationary && !blockedItem && IsSaluteGeometry(poses);

    if (inSalute)
    {
        saluteReleaseCount = 0;
        if (saluteHoldCount < 1000u) saluteHoldCount++;
        if (!saluteLatched && saluteHoldCount >= SALUTE_HOLD_TICKS_REQUIRED)
        {
            saluteLatched = 1;
            if (!saluteLastFiredMs || clock - saluteLastFiredMs >= SALUTE_COOLDOWN_MS)
            {
                saluteLastFiredMs = clock;
                int played = play((LPCWSTR)saluteAudio, NULL, SND_MEMORY | SND_ASYNC | SND_NODEFAULT | SND_NOSTOP);
                hf->log(mod, "salute attempt slot=%u avatar=%u state=%u result=%s",
                        now.slot, now.avatar, gameState, played ? "started" : "refused");
            }
        }
    }
    else
    {
        saluteHoldCount = 0;
        if (saluteLatched)
        {
            saluteReleaseCount++;
            if (saluteReleaseCount >= SALUTE_RELEASE_TICKS)
            {
                saluteLatched = 0;
                saluteReleaseCount = 0;
            }
        }
    }
}

static void SyncActiveSlotState(int sidx, const Snapshot* now)
{
    if (sidx < 0 || (unsigned)sidx >= YODA_SLOTS) return;
    if (!slotStates[sidx].active || slotStates[sidx].saved.peer != now->peer ||
        slotStates[sidx].saved.player != now->player || slotStates[sidx].saved.network != now->network)
    {
        for (int k = 0; k < (int)YODA_SLOTS; k++)
        {
            if (k == sidx || !slotStates[k].active) continue;
            if (slotStates[k].saved.peer == now->peer &&
                slotStates[k].saved.player == now->player &&
                slotStates[k].saved.network == now->network)
            {
                slotStates[sidx] = slotStates[k];
                slotStates[sidx].saved.slot = (uint32_t)sidx;
                memset(&slotStates[k], 0, sizeof slotStates[k]);
                saved = slotStates[sidx].saved;
                consumedPeer = slotStates[sidx].consumedPeer;
                consumedAvatar = slotStates[sidx].consumedAvatar;
                consumedNetwork = slotStates[sidx].consumedNetwork;
                break;
            }
        }
    }
    for (int k = 0; k < (int)YODA_SLOTS; k++)
        if (k != sidx) memset(&slotStates[k], 0, sizeof slotStates[k]);
    activeSlot = sidx;
}

static void ClearSavedState(void)
{
    memset(&saved, 0, sizeof saved);
    s_kitCacheValid = 0;
    if (activeSlot >= 0 && (unsigned)activeSlot < YODA_SLOTS)
    {
        slotStates[activeSlot].active = 0;
        memset(&slotStates[activeSlot].saved, 0, sizeof slotStates[activeSlot].saved);
    }
}

static int SameSlot(const Snapshot* now)
{
    return now->peer == saved.peer && now->player == saved.player && now->network == saved.network;
}

static int Consumed(void)
{
    if (consumedPeer == saved.peer && consumedAvatar == saved.avatar && consumedNetwork == saved.network) return 1;
    for (unsigned i = 0; i < YODA_CONSUMED_RING; i++)
    {
        if (consumedRing[i].peer && consumedRing[i].peer == saved.peer &&
            consumedRing[i].avatar == saved.avatar && consumedRing[i].network == saved.network)
            return 1;
    }
    return 0;
}

static void RecordConsumed(uint64_t peer, uint32_t avatar, uint32_t network)
{
    consumedPeer = peer; consumedAvatar = avatar; consumedNetwork = network;
    if (activeSlot >= 0 && (unsigned)activeSlot < YODA_SLOTS)
    {
        slotStates[activeSlot].consumedPeer = peer;
        slotStates[activeSlot].consumedAvatar = avatar;
        slotStates[activeSlot].consumedNetwork = network;
    }
    consumedRing[consumedRingCursor % YODA_CONSUMED_RING] = (ConsumedTuple){peer, avatar, network};
    consumedRingCursor++;
}

/* Both routes must identify the same dead local avatar. Resolve its ID and read the manager's
 * health record by ID, without retaining or dereferencing an avatar, unit or actor pointer. */
static const char* ConfirmDeath(HfMod* mod, Snapshot* now, uint32_t lifecycle, uint64_t age, const char* route)
{
    if (age > 500) return "stale LEGO observation";
    if (Consumed()) return "duplicate death";
    if (lifecycle != 2) return "local slot is not waiting to respawn";
    if (!SameSlot(now) || hf->entity_of_network(mod, now->network) != saved.avatar) return "local avatar identity changed/unavailable";
    uint32_t life;
    if (!Life(mod, saved.avatar, &life)) return "native health unavailable";
    if (life < 2) return "native health has not confirmed death";
    if (!Armor(now) || now->record != saved.record || memcmp(now->applied, saved.applied, sizeof now->applied)) return "applied outfit changed/unavailable";
    if (!Unchanged(&proofs[saved.visual])) return "staged visuals changed/unavailable";
    RecordConsumed(saved.peer, saved.avatar, saved.network);
    /* Keep the buffer for the whole session. A refused playback is consumed too: no retries. */
    int played = play((LPCWSTR)audio, NULL, SND_MEMORY | SND_ASYNC | SND_NODEFAULT | SND_NOSTOP);
    hf->log(mod, "yoda attempt slot=%u avatar=%u kit=%08X body=%u result=%s route=%s",
            saved.slot, saved.avatar, saved.kit, saved.body, played ? "started" : "refused", route);
    return NULL;
}

static void Tick(HfMod* mod, float dt, void* user)
{
    (void)dt; (void)user;
    if ((!ready && !saluteReady) || !InterlockedCompareExchange(&gameLoaded, 0, 0) || !Prove(mod))
    { ClearSavedState(); ResetSaluteTracker(); return; }

    uint32_t gstate = hf->game_state(mod);
    uint64_t clock = GetTickCount64();
    if (clock - lastSample < sampleEvery) return;
    lastSample = clock;

    if (saluteReady)
        PollSalute(mod, gstate, clock);

    if (!ready || gstate != HF_STATE_MISSION)
    { ClearSavedState(); return; }

    Snapshot now = {0}; uint32_t lifecycle, life; int slotIdx = -1;
    if (!LocalSlot(mod, &now, &lifecycle, &slotIdx)) { ClearSavedState(); return; }
    SyncActiveSlotState(slotIdx, &now);
    /* The SDK only emits Down on consecutive 3 -> 2 samples. Check the same local life
     * independently while its eligible observation is still fresh. Never infer death from disappearance. */
    if (saved.avatar && !SameSlot(&now)) ClearSavedState();
    if (lifecycle == 2 && saved.avatar && !Consumed())
    {
        uint64_t age = clock - saved.observed;
        const char* why = ConfirmDeath(mod, &now, lifecycle, age, "local-poll");
        if (why && !reportedPollRejection)
        {
            hf->log(mod, "skip death: route=local-poll slot=%u avatar=%u network=%u age_ms=%llu %s",
                    now.slot, saved.avatar, saved.network, (unsigned long long)age, why);
            reportedPollRejection = 1;
        }
        if (age > 500) ClearSavedState();
        return;
    }
    if (lifecycle != 3) return;
    now.avatar = hf->entity_of_network(mod, now.network);
    if (!now.avatar || now.avatar == UINT32_MAX || !Life(mod, now.avatar, &life)) { ClearSavedState(); return; }
    if (life >= 2) return;
    if (!Armor(&now)) { ClearSavedState(); return; }
    now.observed = GetTickCount64();
    if (saved.avatar != now.avatar || saved.network != now.network || saved.kit != now.kit || saved.body != now.body)
        hf->log(mod, "eligible local slot=%u avatar=%u network=%u kit=%08X body=%u visual=%d",
                now.slot, now.avatar, now.network, now.kit, now.body, now.visual);
    saved = now;
    slotStates[slotIdx].active = 1;
    slotStates[slotIdx].saved = now;
    slotStates[slotIdx].reportedPollRejection = 0;
    reportedPollRejection = 0;
}

static void Down(HfMod* mod, const HfPlayerEvent* event, void* user)
{
    (void)user;
    if (!event->local) { hf->log(mod, "skip remote death"); return; }
    if (!ready || !saved.avatar || hf->game_state(mod) != HF_STATE_MISSION || event->peer != saved.peer || event->entity != saved.player)
    { hf->log(mod, "skip death: route=sdk-down update=%llu no matching local LEGO observation", (unsigned long long)event->info.update); return; }
    Snapshot now = {0}; uint32_t lifecycle; int slotIdx = -1;
    uint64_t age = GetTickCount64() - saved.observed;
    const char* why = LocalSlot(mod, &now, &lifecycle, &slotIdx)
        ? (SyncActiveSlotState(slotIdx, &now), ConfirmDeath(mod, &now, lifecycle, age, "sdk-down"))
        : "local slot unavailable";
    if (why) hf->log(mod, "skip death: route=sdk-down update=%llu slot=%u avatar=%u age_ms=%llu %s",
                     (unsigned long long)event->info.update, saved.slot, saved.avatar, (unsigned long long)age, why);
}

static void State(HfMod* mod, const HfStateEvent* event, void* user)
{
    (void)mod; (void)user;
    ResetSaluteTracker();
    if (event->to != HF_STATE_MISSION)
    {
        memset(&saved, 0, sizeof saved);
        s_kitCacheValid = 0;
        memset(slotStates, 0, sizeof slotStates);
        memset(consumedRing, 0, sizeof consumedRing);
        consumedRingCursor = 0; activeSlot = -1;
        consumedPeer = 0; consumedAvatar = consumedNetwork = 0;
    }
}

static void Loaded(HfMod* mod, HfEvent event, void* user)
{
    (void)mod; (void)event; (void)user;
    InterlockedExchange(&gameLoaded, 1);
}

HF_MOD_EXPORT int hellforge_mod_init(const HfApi* api, HfMod* mod)
{
    hf = api;
    if (api->version < 16 || api->size < offsetof(HfApi, entity_of_network) + sizeof api->entity_of_network || strcmp(api->build_id(), "25480438")) return 1;
    HMODULE bcrypt = LoadLibraryW(L"bcrypt.dll"), winmm = LoadLibraryW(L"winmm.dll");
    openHash = bcrypt ? (void*)GetProcAddress(bcrypt, "BCryptOpenAlgorithmProvider") : NULL;
    hashBytes = bcrypt ? (void*)GetProcAddress(bcrypt, "BCryptHash") : NULL;
    play = winmm ? (void*)GetProcAddress(winmm, "PlaySoundW") : NULL;
    if (!openHash || !hashBytes || !play || openHash(&sha, BCRYPT_SHA256_ALGORITHM, NULL, 0) < 0) return 1;
    wchar_t executable[1024], directory[1100], wave[1100], folder[1024];
    DWORD length = GetModuleFileNameW(NULL, executable, 1024);
    wchar_t* slash = wcsrchr(executable, L'\\');
    if (!length || length >= 1024 || !slash) return 1;
    *slash = 0;
    if (swprintf(directory, 1100, L"%ls\\..\\data", executable) < 0 ||
        !MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, api->mod_folder(mod), -1, folder, 1024) ||
        swprintf(wave, 1100, L"%ls\\audio\\classic-lego-yoda-death.wav", folder) < 0) return 1;
    LoadExtraTorsos(folder);
    LoadOptionalSaluteWave(folder);
    HANDLE file = OpenRead(wave); LARGE_INTEGER size;
    int audioOk = file != INVALID_HANDLE_VALUE && GetFileSizeEx(file, &size) && size.QuadPart == sizeof audio &&
        ReadAt(file, 0, audio, sizeof audio) && Hash(audio, sizeof audio, YODA_WAV_SHA256);
    if (file != INVALID_HANDLE_VALUE) CloseHandle(file);
    int qualified = ScanVisuals(directory);
    ready = (audioOk && qualified) ? 1 : 0;
    if (!ready && !saluteReady)
    {
        hf->log(mod, "off: WAV qualified=%d LEGO torso variants=%d/%zu saluteWAV=%d", audioOk, qualified, visualCount, saluteReady);
        hf->set_status(mod, "off: exact Empire/Clonedivers LEGO torso assets and salute WAV absent");
        return 0;
    }
    if (hf_on_game_loaded(mod, Loaded, NULL) || hf_on_player_down(mod, Down, NULL, 0) ||
        hf_on_state(mod, State, NULL, 0) || hf_on_game_thread(mod, Tick, NULL)) return 1;
    hf->log(mod, "armed: version=0.2.3 yoda=%d (LEGO torso variants=%d/%zu) salute=%d (%u B)",
            ready, qualified, visualCount, saluteReady, saluteAudioSize);
    if (ready && saluteReady)
        hf->set_status(mod, "armed: LEGO death + Stormtrooper Salute active");
    else if (ready)
        hf->set_status(mod, "waiting for a confirmed local LEGO death (Empire / Clonedivers)");
    else
        hf->set_status(mod, "armed: Stormtrooper Salute active (LEGO death off)");
    return 0;
}
