/* Offline prototype. Read-only game research: HellForge 0.4 passives.c / event_sources.c / globals.json,
 * derived from HD2Runtime by SkyeShade (see CREDITS.md). No engine sound calls, hooks or memory writes. */
#include "hellforge_mod.h"
#include <windows.h>
#include <mmsystem.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#include "assets.h"
#include "visual_gate.h"
#include "passive_pins.h"

#define YODA_SLOTS 4u
#define YODA_CONSUMED_RING 8u

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
static int proven, ready;
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
static BOOL (WINAPI *play)(LPCWSTR, HMODULE, DWORD);

static int Read(uintptr_t p, void* out, size_t n) { return p >= 65536 && !hf->read(p, out, n); }
static int Pointer(uintptr_t p, uintptr_t* out) { return Read(p, out, 8) && *out >= 65536; }

static int Prove(HfMod* mod)
{
    if (proven) return proven > 0;
    game = hf->module_base("game.dll", NULL);
    if (!game) return 0;
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
    if (!ready || !InterlockedCompareExchange(&gameLoaded, 0, 0) || !Prove(mod) || hf->game_state(mod) != HF_STATE_MISSION)
    { ClearSavedState(); return; }
    uint64_t clock = GetTickCount64();
    if (clock - lastSample < sampleEvery) return;
    lastSample = clock;
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
    HANDLE file = OpenRead(wave); LARGE_INTEGER size;
    int audioOk = file != INVALID_HANDLE_VALUE && GetFileSizeEx(file, &size) && size.QuadPart == sizeof audio &&
        ReadAt(file, 0, audio, sizeof audio) && Hash(audio, sizeof audio, YODA_WAV_SHA256);
    if (file != INVALID_HANDLE_VALUE) CloseHandle(file);
    int qualified = ScanVisuals(directory);
    if (!audioOk || !qualified)
    { hf->log(mod, "off: WAV qualified=%d LEGO torso variants=%d/%zu", audioOk, qualified, visualCount); hf->set_status(mod, "off: exact Empire/Clonedivers LEGO torso assets or WAV absent"); return 0; }
    if (hf_on_game_loaded(mod, Loaded, NULL) || hf_on_player_down(mod, Down, NULL, 0) ||
        hf_on_state(mod, State, NULL, 0) || hf_on_game_thread(mod, Tick, NULL)) return 1;
    ready = 1;
    hf->log(mod, "armed: version=0.2.2 LEGO torso variants=%d/%zu; 4-player co-op per-slot local death only", qualified, visualCount);
    hf->set_status(mod, "waiting for a confirmed local LEGO death (Empire / Clonedivers)");
    return 0;
}
