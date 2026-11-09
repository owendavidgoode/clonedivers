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

typedef struct Snapshot
{
    uint64_t peer, observed;
    uint32_t player, avatar, network, kit, body;
    uintptr_t record;
    unsigned char applied[68];
    int visual;
} Snapshot;
static const HfApi* hf;
static uintptr_t game;
static int proven, ready;
static volatile LONG gameLoaded;
static Snapshot saved;
static uint64_t lastSample, sampleEvery = 100;
static uint32_t consumedAvatar, consumedNetwork;
static uint64_t consumedPeer;
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

/* Match one local slot by peer, not by slot number. Player entity is not the Helldiver avatar. */
static int Local(HfMod* mod, Snapshot* out, uint32_t* lifecycle)
{
    uintptr_t players = (uintptr_t)hf_global_PlayerManager(mod), user = (uintptr_t)hf_global_LocalUser(mod);
    uint32_t count; uint64_t peer;
    if (!Read(players + 0x84, &count, 4) || count > 4 || !Read(user + 0xB398, &peer, 8) || !peer) return 0;
    int found = 0;
    for (uint32_t i = 0; i < count; i++)
    {
        uint64_t candidate; uintptr_t descriptor;
        if (!Read(players + 0x2C8 + 0x38 * i, &candidate, 8)) return 0;
        if (candidate != peer) continue;
        if (found++ || !Pointer(players + 0xE8 + 8 * i, &descriptor) || !Read(descriptor + 8, &out->player, 4) ||
            !Read(players + 0x2E0 + 0x38 * i, lifecycle, 4) || !Read(players + 0x3A8 + 0x20 * i, &out->network, 4)) return 0;
    }
    out->peer = peer;
    return found == 1 && out->player && out->player != UINT32_MAX;
}

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
    for (int v = 0; v < 4; v++) if (out->kit == visuals[v].kit && out->body == visuals[v].body) out->visual = v;
    if (out->visual < 0 || !proofs[out->visual].valid || !Pointer(manager, &kits) || !Read(manager + 8, &kitCount, 4) || !kitCount || kitCount > 2048) return 0;
    int matches = 0;
    for (uint32_t i = 0; i < kitCount; i++)
    {
        uintptr_t kit; uint32_t id, type;
        if (!Pointer(kits + 8u * i, &kit) || !Read(kit, &id, 4)) return 0;
        if (id != out->kit) continue;
        if (!Read(kit + 40, &type, 4) || type != 0) return 0;
        matches++;
    }
    return matches == 1;
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

static void Tick(HfMod* mod, float dt, void* user)
{
    (void)dt; (void)user;
    if (!ready || !InterlockedCompareExchange(&gameLoaded, 0, 0) || !Prove(mod) || hf->game_state(mod) != HF_STATE_MISSION)
    { memset(&saved, 0, sizeof saved); return; }
    uint64_t clock = GetTickCount64();
    if (clock - lastSample < sampleEvery) return;
    lastSample = clock;
    Snapshot now = {0}; uint32_t lifecycle, life;
    if (!Local(mod, &now, &lifecycle)) { memset(&saved, 0, sizeof saved); return; }
    /* Preserve the last alive observation while the death event is delivered, never indefinitely. */
    if (lifecycle != 3) return;
    now.avatar = hf->entity_of_network(mod, now.network);
    if (!now.avatar || now.avatar == UINT32_MAX || !Life(mod, now.avatar, &life)) { memset(&saved, 0, sizeof saved); return; }
    if (life >= 2) return;
    if (!Armor(&now)) { memset(&saved, 0, sizeof saved); return; }
    now.observed = GetTickCount64();
    if (saved.avatar != now.avatar || saved.network != now.network || saved.kit != now.kit || saved.body != now.body)
        hf->log(mod, "eligible local avatar=%u network=%u kit=%08X body=%u", now.avatar, now.network, now.kit, now.body);
    saved = now;
}

static void Down(HfMod* mod, const HfPlayerEvent* event, void* user)
{
    (void)user;
    if (!event->local) { hf->log(mod, "skip remote death"); return; }
    if (!ready || !saved.avatar || hf->game_state(mod) != HF_STATE_MISSION || event->peer != saved.peer || event->entity != saved.player ||
        GetTickCount64() - saved.observed > 500) { hf->log(mod, "skip death: no fresh local LEGO observation"); return; }
    if (consumedPeer == saved.peer && consumedAvatar == saved.avatar && consumedNetwork == saved.network)
    { hf->log(mod, "skip duplicate death avatar=%u", saved.avatar); return; }
    Snapshot now = {0}; uint32_t lifecycle, life;
    if (!Local(mod, &now, &lifecycle) || lifecycle != 2 || now.peer != saved.peer || now.player != saved.player ||
        !Armor(&now) || now.record != saved.record || memcmp(now.applied, saved.applied, sizeof now.applied) ||
        !Life(mod, saved.avatar, &life) || life < 2 || !Unchanged(&proofs[saved.visual]))
    { hf->log(mod, "skip death: identity, dead state or staged visuals changed/unavailable"); return; }
    consumedPeer = saved.peer; consumedAvatar = saved.avatar; consumedNetwork = saved.network;
    /* Keep the buffer for the whole session. A refused playback is consumed too: no retries. */
    int played = play((LPCWSTR)audio, NULL, SND_MEMORY | SND_ASYNC | SND_NODEFAULT | SND_NOSTOP);
    hf->log(mod, "yoda attempt avatar=%u kit=%08X body=%u result=%s", saved.avatar, saved.kit, saved.body, played ? "started" : "refused");
}

static void State(HfMod* mod, const HfStateEvent* event, void* user)
{
    (void)user;
    if (event->to != HF_STATE_MISSION)
    { memset(&saved, 0, sizeof saved); consumedPeer = 0; consumedAvatar = consumedNetwork = 0; }
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
    HANDLE file = OpenRead(wave); LARGE_INTEGER size;
    int audioOk = file != INVALID_HANDLE_VALUE && GetFileSizeEx(file, &size) && size.QuadPart == sizeof audio &&
        ReadAt(file, 0, audio, sizeof audio) && Hash(audio, sizeof audio, YODA_WAV_SHA256);
    if (file != INVALID_HANDLE_VALUE) CloseHandle(file);
    int qualified = ScanVisuals(directory);
    if (!audioOk || !qualified)
    { hf->log(mod, "off: WAV qualified=%d r45 torso variants=%d/4", audioOk, qualified); hf->set_status(mod, "off: exact r45 LEGO torso assets or WAV absent"); return 0; }
    if (hf_on_game_loaded(mod, Loaded, NULL) || hf_on_player_down(mod, Down, NULL, 0) ||
        hf_on_state(mod, State, NULL, 0) || hf_on_game_thread(mod, Tick, NULL)) return 1;
    ready = 1;
    hf->log(mod, "armed: r45 torso variants=%d/4; local mission death only; WAV via Windows PlaySound (non-positional)", qualified);
    hf->set_status(mod, "waiting for a confirmed local LEGO death (r45 assets)");
    return 0;
}
