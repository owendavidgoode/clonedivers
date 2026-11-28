/* Dedicated Melee Slot 1.5.2-clonedivers.4 (vibeslacker.dedicated-melee-slot).
 * Original work Copyright (c) 2026 vibeslacker (MIT License, tag v1.5.2 + LoadSlotKeys from v1.6.3).
 * Clonedivers 4-player co-op & SDK-16 patches Copyright (c) 2026 Clonedivers Contributors (MIT License).
 *
 * How it works on Helldivers 2 build 25480438 (HellForge Mod SDK 16):
 *  - Right after the game spawns the local Helldiver's authoritative secondary weapon (matched against all 25
 *    build-25480438 secondary weapon entities and HfContent modded secondaries, across initial drop, mid-mission
 *    hot-join, and reinforcements), the mod spawns ONE local extra melee weapon (network_id = 0x7FFF sentinel so
 *    EntityManager's network map keeps the real pistol's slot intact), attaches it to the local Helldiver (RVA
 *    0x8BF870, preserving all 4 player slots' +0x2E8 renderer words), and requests its model packages.
 *  - Tapping the secondary weapon key again while the secondary is already drawn holsters to primary, swaps the
 *    secondary inventory slot (+4) between the real secondary and the extra melee, and re-draws secondary. */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <xmmintrin.h>

#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "hellforge_mod.h"
#include "clonedivers_em_layout.h"

#define DMS_EXPECTED_BUILD    "25480438"
#define INV_GLOBAL            0x3326738u
#define ENT_STATE_GLOBAL      0x3326DC0u
#define ENTITIES_GLOBAL       0x346BF98u
#define PLAYERS_GLOBAL        0x3326468u
#define LOCALUSER_GLOBAL      0x347CEF0u
#define PKG_SYSTEM_GLOBAL     0x347CEB0u

#define SPAWN_REQ_RVA         0x0FD9710u
#define ATTACH_RVA            0x08BF870u
#define PKG_REQUEST_RVA       0x12FE350u
#define PKG_EXE_ENGINE        0x1A10208u

#define LOCALUSER_PEER_OFF    0xB398u
#define PLAYERS_COUNT_OFF     0x84u
#define PLAYERS_PEER_OFF      0x2C8u
#define PLAYERS_LIFE_OFF      0x2E0u
#define PLAYERS_WORD_OFF      0x2E8u
#define PLAYERS_SLOT_STRIDE   0x38u
#define PLAYERS_NET_OFF       0x3A8u
#define PLAYERS_NET_STRIDE    0x20u
#define PLAYERS_MAX           4u
#define NET_ID_NONE           0x7FFFu
#define PLAYER_LIFE_SPAWNED   3u

#define EM_NETWORK_MAP        0xF22EC8u
#define EM_NETWORK_ENTITIES   0xF32F20u
#define EM_ENTITY_MAP         0xF1AEB0u
#define EM_DESCRIPTORS        0xF32F18u

#define SWAP_WAIT_MS          2500u
#define HOLSTER_MS            0u
#define DRAW_MS               0u
#define SCRATCH_ALLOC_SIZE    0x8000u

typedef uint64_t (*SpawnReqFn)(uint64_t a, uint64_t b, uint64_t c, uint64_t d, uint64_t e, uint64_t f);
typedef void (*AttachFn)(uint64_t table, uint64_t entity, uint64_t owner, uint64_t flag, uint64_t e, uint64_t f);
typedef void (*PkgReqFn)(uint64_t a, uint64_t b, uint64_t c, uint64_t d, uint64_t e, uint64_t f);

static HfMod* g_mod;
static const HfApi* g_hf;
static SpawnReqFn g_spawnReq;
static AttachFn g_attachFn;
static PkgReqFn g_pkgReq;
static int g_hookInstalled;

/* Avatar / probe / swap state: mutated strictly on the game thread (Tick / callbacks / SpawnReqDetour). */
static volatile LONG g_avatar;
static volatile LONG g_weaponEventAvatar;
static volatile LONG g_probeEntity;
static volatile LONG g_probeOriginal;
static volatile LONG g_missionOn;
static volatile LONG g_armed;          /* 1 = ready to clone beside the next local secondary spawn */
static volatile LONG g_spawningClone;  /* recursion guard around our own g_spawnReq call */
static volatile LONG g_regDone;
static volatile LONG g_regOk;
static volatile LONG g_pkgDone;
static volatile LONG g_stopThreads;
static volatile ULONGLONG g_probeTick;

static volatile LONG g_tapFlag;
static volatile LONG g_swapStage;
static volatile LONG g_injectSel;      /* 1 = primary key, 2 = secondary key */
static volatile LONG g_retries;
static volatile ULONGLONG g_swapDeadline;
static volatile ULONGLONG g_swapAt;
static volatile ULONGLONG g_lastPress;
static volatile ULONGLONG g_swapStarted;
static volatile uint32_t g_swapTarget;
static volatile uint64_t g_copyResource;
static volatile uint64_t g_secondaryRes;

static uint8_t* g_spawnScratch;
static HANDLE g_stopEvent;
static HANDLE g_keyThread;
static FILETIME g_lastKeysWhen;
static char g_lastKeysPath[MAX_PATH + 160];

/* Configurable slot keys (defaults: 1 = scan 0x02 / VK '1', 2 = scan 0x03 / VK '2'). */
static volatile LONG g_keyScan[3] = {0, 0x02, 0x03};
static volatile LONG g_keyVk[3]   = {0, '1',  '2'};
static volatile LONG g_keyMs[3]   = {0, 25,   25};

#ifdef DMS_UNIT_TEST
static char g_testConfigDir[MAX_PATH];
static int g_testForceFocused = 1;
static volatile LONG g_testSentKeyDowns = 0;
static volatile LONG g_testSentKeyUps = 0;
#endif

/* Sorted table of all 25 build-25480438 armory-type-3 secondary weapon entity resource IDs (hellforge_content.h). */
static const uint64_t kVanillaSecondaryEntities[25] = {
    0x05E4E5C2DB6E44A2ULL, /* HF_ENTITY_P2_PEACEMAKER */
    0x0B882808C6F498E8ULL, /* HF_ENTITY_P35_REEDUCATOR */
    0x14D5D4506056C7A4ULL, /* HF_ENTITY_P33_MISSILE_PISTOL */
    0x1A437158E1B8D2A1ULL, /* HF_ENTITY_P113_VERDICT */
    0x2B28E17FFED05F7CULL, /* HF_ENTITY_SG22_BUSHWHACKER */
    0x3575AABC5F1F9326ULL, /* HF_ENTITY_P19_REDEEMER */
    0x3F92BA65EF65CCA9ULL, /* HF_ENTITY_P72_CRISPER */
    0x416D053372C4E433ULL, /* HF_ENTITY_LAS58 */
    0x4D58C77087B774C5ULL, /* HF_ENTITY_M6CSOCOM_PISTOL */
    0x52CDBFBACA3CB397ULL, /* HF_ENTITY_CQC30 */
    0x52E4334E6A128CAFULL, /* HF_ENTITY_GP31_GRENADE_PISTOL */
    0x75816077C139C850ULL, /* HF_ENTITY_CQC5 */
    0x792D5D2A340FD6E6ULL, /* HF_ENTITY_CQC42 */
    0x7B06196E90154C88ULL, /* HF_ENTITY_LAS7_DAGGER */
    0x8D3D52A3B2F19402ULL, /* HF_ENTITY_P4_SENATOR */
    0x9EB160830321BFD6ULL, /* HF_ENTITY_GP20_ULTIMATUM */
    0xAA69A60D74A3EC54ULL, /* HF_ENTITY_PLAS15_LOYALIST */
    0xBDE1F2534280300DULL, /* HF_ENTITY_P41_OMBUDSMAN */
    0xC780BCD79547DA0FULL, /* HF_ENTITY_P69_VETO */
    0xCF8934FF6567A42DULL, /* HF_ENTITY_P92_WARRANT */
    0xD6B1FB05B9109353ULL, /* HF_ENTITY_P11_STIM_PISTOL */
    0xDBB6C961C59FADC1ULL, /* HF_ENTITY_P40K_BOLT_PISTOL */
    0xE3B6AEDD07FCB464ULL, /* HF_ENTITY_CQC19_STUN_LANCE */
    0xE91F569C2AD8AF01ULL, /* HF_ENTITY_P34_BREACHER */
    0xFCD8A6E67EAC635AULL, /* HF_ENTITY_CQC2 */
};

static int IsVanillaSecondaryEntity(uint64_t res)
{
    if (res == 0ULL) return 0;
    size_t lo = 0, hi = sizeof(kVanillaSecondaryEntities) / sizeof(kVanillaSecondaryEntities[0]);
    while (lo < hi) {
        size_t mid = lo + (hi - lo) / 2u;
        uint64_t cur = kVanillaSecondaryEntities[mid];
        if (cur == res) return 1;
        if (cur < res) lo = mid + 1u;
        else hi = mid;
    }
    return 0;
}

static uint64_t g_customSecondaries[64];
static uint32_t g_customSecondaryCount;
static int g_customSecondariesCached;

static void RefreshCustomSecondaries(void)
{
    g_customSecondaryCount = 0u;
    g_customSecondariesCached = 0;
    if (g_hf != NULL && g_mod != NULL &&
        g_hf->size >= offsetof(HfApi, content) + sizeof(void*) && g_hf->content != NULL) {
        for (uint32_t i = 0; i < 512u; i++) {
            HfContent c;
            memset(&c, 0, sizeof(c));
            c.size = (uint32_t)sizeof(c);
            if (!g_hf->content(g_mod, i, &c)) break;
            if (c.kind == HF_KIND_ITEM && c.entity != 0ULL && IsVanillaSecondaryEntity(c.base_entity)) {
                if (g_customSecondaryCount < 64u) {
                    g_customSecondaries[g_customSecondaryCount++] = c.entity;
                }
            }
        }
        g_customSecondariesCached = 1;
    }
}

static int IsSecondaryWeaponResource(uint64_t res)
{
    if (res == 0ULL) return 0;
    if (IsVanillaSecondaryEntity(res)) return 1;
    if (res == g_secondaryRes) return 1;
    if (!g_customSecondariesCached) RefreshCustomSecondaries();
    for (uint32_t i = 0; i < g_customSecondaryCount; i++) {
        if (g_customSecondaries[i] == res) return 1;
    }
    return 0;
}

/* ---- Safe Memory & Hash-Map Helpers ------------------------------------------------------------------------------- */

static int Ptr(uintptr_t at, uintptr_t* out)
{
    uint64_t v = 0;
    if (g_hf == NULL || g_hf->read == NULL || at < 0x10000u) return 0;
    if (g_hf->read(at, &v, 8) != 0 || v < 0x10000ULL || (v >> 32) > 0x7FFFULL) return 0;
    *out = (uintptr_t)v;
    return 1;
}

static int MapGet(uintptr_t header, uint32_t key, uint32_t* value)
{
    uint8_t h[20];
    if (key == 0 || g_hf == NULL || g_hf->read == NULL || g_hf->read(header, h, sizeof(h)) != 0) return 0;
    uint64_t buckets = 0;
    uint32_t capacity = 0, empty = 0, multiplier = 0;
    memcpy(&buckets, h, 8);
    memcpy(&capacity, h + 8, 4);
    memcpy(&empty, h + 12, 4);
    memcpy(&multiplier, h + 16, 4);
    if (buckets < 0x10000ULL || capacity == 0u || capacity > 1048576u) return 0;
    uint32_t start = key * multiplier;
    uint32_t probes = capacity < 4096u ? capacity : 4096u;
    for (uint32_t i = 0; i < probes; i++) {
        uint32_t b[2];
        if (g_hf->read((uintptr_t)buckets + (uintptr_t)((start + i) % capacity) * 8u, b, 8) != 0) return 0;
        if (b[0] == key) {
            if (b[1] == 0xFFFFFFFFu) return 0;
            *value = b[1];
            return 1;
        }
        if (b[0] == empty) return 0;
    }
    return 0;
}

static int InvRecord(uintptr_t game, uint32_t avatar, uintptr_t* inv, uintptr_t* record)
{
    uintptr_t records = 0;
    uint32_t index = 0;
    if (!avatar || !Ptr(game + INV_GLOBAL, inv) || !MapGet(*inv + 0x28u, avatar, &index) ||
        index >= 0x10000u || !Ptr(*inv + 0x50u, &records)) {
        return 0;
    }
    *record = records + (uintptr_t)index * 0x30u;
    return 1;
}

static int GameWindowFocused(void)
{
#ifdef DMS_UNIT_TEST
    if (g_testForceFocused >= 0) return g_testForceFocused;
#endif
    HWND fg = GetForegroundWindow();
    if (!fg) return 0;
    DWORD pid = 0;
    GetWindowThreadProcessId(fg, &pid);
    return pid == GetCurrentProcessId() ? 1 : 0;
}

/* ---- Multi-Slot (0..3) Co-op Local Helldiver Avatar Resolution ---------------------------------------------------- */

static uint32_t ResolveEntityFromNetId(uintptr_t game, uint32_t net_id)
{
    if (net_id == 0u || net_id == NET_ID_NONE) return 0u;
    if (g_hf != NULL && g_mod != NULL &&
        g_hf->size >= offsetof(HfApi, entity_of_network) + sizeof(void*) &&
        g_hf->entity_of_network != NULL) {
        uint32_t ent = g_hf->entity_of_network(g_mod, net_id);
        if (ent != 0u && ent != 0xFFFFFFFFu) return ent;
    }
    uintptr_t em = 0;
    uint32_t slot = 0, ent = 0;
    if (game != 0 && g_hf != NULL && Ptr(game + ENTITIES_GLOBAL, &em) && MapGet(em + EM_NETWORK_MAP, net_id, &slot)) {
        CdEmLayout layout = cd_em_resolve_hf(game, g_hf->read);
        uintptr_t rec = (slot < layout.capacity) ? cd_em_record_at(em, layout.records_offset, slot) : 0u;
        if (rec != 0u && g_hf->read(rec + 8u, &ent, 4) == 0 &&
            ent != 0u && ent != 0xFFFFFFFFu) {
            return ent;
        }
    }
    return 0u;
}

/* Scans PlayerManager slots 0..3 matching LocalUser's peer id (+0xB398).
 * Returns:
 *   1 = local peer found & spawned (lifecycle == 3) with *out_avatar resolved
 *   0 = local peer found, NOT spawned (dead / waiting to reinforce / loadout)
 *  -1 = PlayerManager or LocalUser unreadable / local peer not in list yet */
static int QueryLocalPlayerAvatar(uintptr_t game, uint32_t* out_avatar)
{
    *out_avatar = 0u;
    if (!game) return -1;

    uintptr_t lu = g_mod ? (uintptr_t)hf_global_LocalUser(g_mod) : 0;
    uintptr_t pm = g_mod ? (uintptr_t)hf_global_PlayerManager(g_mod) : 0;
    if (!lu && !Ptr(game + LOCALUSER_GLOBAL, &lu)) return -1;
    if (!pm && !Ptr(game + PLAYERS_GLOBAL, &pm)) return -1;

    uint64_t local_peer = 0;
    uint32_t count = 0;
    if (g_hf->read(lu + LOCALUSER_PEER_OFF, &local_peer, 8) != 0 || local_peer == 0ULL) return -1;
    if (g_hf->read(pm + PLAYERS_COUNT_OFF, &count, 4) != 0 || count == 0u || count > PLAYERS_MAX) return -1;

    for (uint32_t i = 0; i < count; i++) {
        uint64_t peer = 0;
        if (g_hf->read(pm + PLAYERS_PEER_OFF + (uintptr_t)i * PLAYERS_SLOT_STRIDE, &peer, 8) != 0) continue;
        if (peer != local_peer) continue;

        uint32_t life = 0;
        if (g_hf->read(pm + PLAYERS_LIFE_OFF + (uintptr_t)i * PLAYERS_SLOT_STRIDE, &life, 4) != 0 ||
            life != PLAYER_LIFE_SPAWNED) {
            return 0;
        }
        uint32_t net_id = NET_ID_NONE;
        if (g_hf->read(pm + PLAYERS_NET_OFF + (uintptr_t)i * PLAYERS_NET_STRIDE, &net_id, 4) != 0 ||
            net_id == NET_ID_NONE) {
            return 0;
        }
        uint32_t av = ResolveEntityFromNetId(game, net_id);
        if (av != 0u) {
            *out_avatar = av;
            return 1;
        }
        return 0;
    }
    return -1;
}

static void DropAttachedMeleeForRespawn(const char* reason)
{
    int had_probe = (InterlockedExchange(&g_probeEntity, 0) != 0);
    InterlockedExchange(&g_probeOriginal, 0);
    InterlockedExchange(&g_regDone, 1);
    InterlockedExchange(&g_pkgDone, 0);
    InterlockedExchange(&g_swapStage, 0);
    LONG prev_armed = InterlockedExchange(&g_armed, 1);
    if (g_hf && g_hf->log && reason && (had_probe || prev_armed == 0)) {
        g_hf->log(g_mod, "respawn (%s): dropped stale melee copy; re-armed for next secondary spawn", reason);
    }
}

static void SyncLocalAvatarAndRespawn(uintptr_t game)
{
    uint32_t resolved = 0u;
    int status = QueryLocalPlayerAvatar(game, &resolved);
    uint32_t prev = (uint32_t)InterlockedCompareExchange(&g_avatar, 0, 0);
    uint32_t probe = (uint32_t)InterlockedCompareExchange(&g_probeEntity, 0, 0);
    LONG reg_done = InterlockedCompareExchange(&g_regDone, 0, 0);
    LONG armed = InterlockedCompareExchange(&g_armed, 0, 0);

    if (status == 1 && resolved != 0u) {
        if (prev != resolved) {
            /* If an old melee was attached to a previous avatar (or a prior spawn failed and left g_armed == 0
             * with no pending attach), drop and re-arm; if g_regDone == 0 && probe != 0, the reinforcement
             * secondary JUST spawned on this exact avatar! */
            if (g_missionOn && prev != 0u && ((probe != 0u && reg_done != 0) || (probe == 0u && armed == 0))) {
                DropAttachedMeleeForRespawn("new Helldiver avatar");
            }
            InterlockedExchange(&g_avatar, (LONG)resolved);
        }
    } else if (status == 0) {
        /* Local player is dead / waiting to reinforce (lifecycle != 3). */
        if (g_missionOn && ((probe != 0u && reg_done != 0) || (prev != 0u && probe == 0u && armed == 0))) {
            DropAttachedMeleeForRespawn("Helldiver down");
        }
        InterlockedExchange(&g_avatar, 0);
    } else {
        uint32_t wev = (uint32_t)InterlockedCompareExchange(&g_weaponEventAvatar, 0, 0);
        if (wev != 0u && g_missionOn) {
            InterlockedExchange(&g_avatar, (LONG)wev);
        }
    }

    /* Also check emptied InventoryRecord on death before lifecycle transitions. */
    uint32_t avatar = (uint32_t)InterlockedCompareExchange(&g_avatar, 0, 0);
    probe = (uint32_t)InterlockedCompareExchange(&g_probeEntity, 0, 0);
    reg_done = InterlockedCompareExchange(&g_regDone, 0, 0);
    if (g_missionOn && avatar != 0u && probe != 0u && reg_done != 0) {
        uintptr_t inv = 0, rec = 0;
        uint32_t w[2] = {0, 0};
        if (InvRecord(game, avatar, &inv, &rec) && g_hf->read(rec, w, sizeof(w)) == 0) {
            if (w[0] == 0u && w[1] == 0u) {
                DropAttachedMeleeForRespawn("inventory emptied");
            }
        }
    }
}

/* ---- Config & Melee Definitions ----------------------------------------------------------------------------------- */

static int OpenConfig(const char* dir, const char* name, HANDLE* out, FILETIME* when)
{
    char path[MAX_PATH + 128];
    path[0] = '\0';
#ifdef DMS_UNIT_TEST
    if (g_testConfigDir[0] != '\0') {
        if (dir[0] != 'U') return 0;
        snprintf(path, sizeof(path), "%s\\%s", g_testConfigDir, name);
    } else
#endif
    if (dir[0] == 'U') {
        DWORD n = GetEnvironmentVariableA("USERPROFILE", path, MAX_PATH);
        if (n == 0 || n >= MAX_PATH) return 0;
        snprintf(path + strlen(path), sizeof(path) - strlen(path), "\\Documents\\HellForge\\%s", name);
    } else {
        DWORD n = GetEnvironmentVariableA("LOCALAPPDATA", path, MAX_PATH);
        if (n == 0 || n >= MAX_PATH) return 0;
        snprintf(path + strlen(path), sizeof(path) - strlen(path), "\\CowboyBingus\\Helldivers2\\Logs\\%s", name);
    }
    HANDLE h = CreateFileA(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return 0;
    memset(when, 0, sizeof(*when));
    GetFileTime(h, NULL, NULL, when);
    *out = h;
    return 1;
}

static int ReadConfig(const char* name, char* text, DWORD size)
{
    HANDLE a = INVALID_HANDLE_VALUE, b = INVALID_HANDLE_VALUE, h;
    FILETIME ta, tb;
    int okA = OpenConfig("U", name, &a, &ta);
    int okB = OpenConfig("L", name, &b, &tb);
    if (!okA && !okB) return 0;
    if (okA && okB) {
        if (CompareFileTime(&tb, &ta) > 0) {
            CloseHandle(a);
            h = b;
        } else {
            CloseHandle(b);
            h = a;
        }
    } else {
        h = okA ? a : b;
    }
    DWORD got = 0;
    memset(text, 0, size);
    ReadFile(h, text, size - 1u, &got, NULL);
    CloseHandle(h);
    return 1;
}

typedef struct {
    const char* name;
    const char* alias;
    uint64_t resource;
    const char* packages;
} MeleeInfo;

static const MeleeInfo kMelee[] = {
    { "machete",    "cqc-42", 0x792D5D2A340FD6E6ull, "8f295b4d04c3a06c 1538ce9a1eaee5d8 15f53d03eba8ed2f 4436a04638261bc4 9b6d3eb4c698079b "
                                                       "b727a8ed2075eeb7 b7567b8c3013c576 db719d81aa6e13f7 f5014993c8402d87" },
    { "stun-lance", "cqc-19", 0xE3B6AEDD07FCB464ull, "1f251a64eba0f770 56aa04adbbeea69f 5811f7986ed3c168 61cfc905a70886ab 69a0588d8634765b "
                                                       "75a938c3382765a4 961b0e91ff7f7ac1 c496e7b7fe1988aa" },
    { "saber",      "cqc-2",  0xFCD8A6E67EAC635Aull, "3cfc68e6bf19f67d 519980a75ebd50ad 732c5526b8d0d87e 9c1a4f2727e37e43 a5b0f1d352402864 "
                                                       "bbde37ac8e06e209 f4dc2361985c3026" },
    { "stun-baton", "cqc-30", 0x52CDBFBACA3CB397ull, "f3c65eaab75f1fc2 11b6940091b3ee80 3b9e4d171fbb6160 405b1a417f98ac5e 4d455c1bb18c8564 "
                                                       "b4910c2012022e5c" },
    { "hatchet",    "cqc-5",  0x75816077C139C850ull, "1982853d15d996da 055f887c3bc616fe 17c3fae85b906d79 2be8dbbfc211e97f 473b8f1e3b759384 "
                                                       "681275af9e93cb2c 69a0588d8634765b 93b8833ac9229a55 a68c0263a22a26dd ac1950d1411393f2 "
                                                       "b6e4fb7620b72aee c98cdbe584ec4dfa" },
    { "shovel",     "cqc-72", 0xE85E623F93F96FB3ull, "49de16e1343250e4" },
};
static const char* g_meleeName = "";
static const char* g_meleePackages = "";

static uint64_t ReadMeleeResource(void)
{
    char text[64], word[32];
    size_t n = 0;
    g_meleeName = "";
    g_meleePackages = "";
    if (!ReadConfig("melee-slot-weapon.txt", text, sizeof(text))) return 0ULL;
    const char* t = text;
    while (*t == ' ' || *t == '\t' || *t == '\r' || *t == '\n') t++;
    for (; *t && *t != '\r' && *t != '\n' && n + 1u < sizeof(word); t++, n++) {
        word[n] = (*t == ' ' || *t == '_') ? '-' : (*t >= 'A' && *t <= 'Z') ? (char)(*t + 32) : *t;
    }
    while (n && word[n - 1u] == '-') n--;
    word[n] = '\0';
    if (strcmp(word, "none") == 0 || word[0] == '\0') return 0ULL;
    for (size_t i = 0; i < sizeof(kMelee) / sizeof(kMelee[0]); i++) {
        if (strcmp(word, kMelee[i].name) == 0 || strcmp(word, kMelee[i].alias) == 0) {
            g_meleeName = kMelee[i].name;
            g_meleePackages = kMelee[i].packages;
            return kMelee[i].resource;
        }
    }
    t = word;
    if (t[0] == '0' && t[1] == 'x') t += 2;
    char* end = NULL;
    uint64_t v = strtoull(t, &end, 16);
    return (end && end != t && *end == '\0') ? v : 0ULL;
}

static volatile LONG g_dumps;
static void DumpResident(const char* why)
{
    uintptr_t exe = (uintptr_t)GetModuleHandleW(NULL), engine, manager, resources, list;
    uint32_t count = 0;
    char probe[8];
    if (g_dumps >= 6 || !ReadConfig("melee-slot-probe.txt", probe, sizeof(probe))) return;
    InterlockedIncrement(&g_dumps);
    if (!Ptr(exe + PKG_EXE_ENGINE, &engine) || !Ptr(engine + 0x400u, &manager) || !Ptr(manager + 0x208u, &resources) ||
        g_hf->read(resources + 0x80u, &count, 4) != 0 || count > 8192u || !Ptr(resources + 0x88u, &list)) {
        g_hf->log(g_mod, "PKGRES %s: engine list unreadable", why);
        return;
    }
    for (int pass = 0; pass < 2; pass++) {
        char line[12 * 18 + 1];
        int inLine = 0, total = 0;
        line[0] = '\0';
        for (uint32_t i = 0; i < count; i++) {
            uintptr_t package, partList;
            uint64_t pid = 0;
            uint32_t n = 0, loaded = 0;
            if (!Ptr(list + (uintptr_t)i * 8u, &package) || g_hf->read(package + 0x10u, &pid, 8) != 0) continue;
            if (g_hf->read(package + 0x18u, &n, 4) != 0 || n > 64u) n = 0;
            if (n && Ptr(package + 0x20u, &partList)) {
                for (uint32_t k = 0; k < n; k++) {
                    uintptr_t part;
                    uint32_t st = 0;
                    if (Ptr(partList + (uintptr_t)k * 8u, &part) && g_hf->read(part + 0x1Cu, &st, 4) == 0 && st == 4u) {
                        loaded++;
                    }
                }
            }
            if ((loaded == n && n) != (pass == 0)) continue;
            size_t len = strlen(line);
            snprintf(line + len, sizeof(line) - len, "%08x%08x ",
                (unsigned)(pid >> 32), (unsigned)(pid & 0xFFFFFFFFu));
            total++;
            if (++inLine == 12) {
                g_hf->log(g_mod, "PKGRES %s %s: %s", why, pass == 0 ? "resident" : "loading", line);
                inLine = 0;
                line[0] = '\0';
            }
        }
        if (inLine) g_hf->log(g_mod, "PKGRES %s %s: %s", why, pass == 0 ? "resident" : "loading", line);
        g_hf->log(g_mod, "PKGRES %s %s: %d package(s) of %u in the engine list",
            why, pass == 0 ? "resident" : "loading", total, count);
    }
}

/* ---- Events ------------------------------------------------------------------------------------------------------- */

static void ResetMissionState(int arm_mission)
{
    InterlockedExchange(&g_avatar, 0);
    InterlockedExchange(&g_weaponEventAvatar, 0);
    InterlockedExchange(&g_probeEntity, 0);
    InterlockedExchange(&g_probeOriginal, 0);
    InterlockedExchange(&g_regDone, 1);
    InterlockedExchange(&g_pkgDone, 0);
    InterlockedExchange(&g_swapStage, 0);
    g_secondaryRes = 0ULL;
    g_customSecondariesCached = 0;
    InterlockedExchange(&g_tapFlag, 0);
    InterlockedExchange(&g_injectSel, 0);
    InterlockedExchange(&g_armed, arm_mission ? 1 : 0);
    InterlockedExchange(&g_missionOn, arm_mission ? 1 : 0);
}

static void OnWeapon(HfMod* mod, const HfEventInfo* info, void* user)
{
    (void)mod;
    (void)user;
    const HfWeaponEvent* w = (const HfWeaponEvent*)info;
    if (w && w->avatar != 0u && g_missionOn) {
        InterlockedExchange(&g_weaponEventAvatar, (LONG)w->avatar);
        InterlockedCompareExchange(&g_avatar, (LONG)w->avatar, 0);
    }
}

static void OnMission(HfMod* mod, const HfEventInfo* info, void* user)
{
    (void)user;
    if (!info) return;
    if (info->kind == HF_ON_MISSION_START) {
        ResetMissionState(1);
        HF_API(mod)->log(mod, "mission start: armed for local secondary weapon spawn");
    } else {
        ResetMissionState(0);
        HF_API(mod)->log(mod, "mission end: disarmed");
    }
}

static void OnState(HfMod* mod, const HfEventInfo* info, void* user)
{
    (void)mod;
    (void)user;
    const HfStateEvent* st = (const HfStateEvent*)info;
    if (!st) return;
    if (st->to == HF_STATE_MISSION) {
        if (!g_missionOn) ResetMissionState(1);
    } else if (g_missionOn) {
        /* Immediate disarm on 4 -> 5 (MISSION -> PREPARE_SHIP) teardown. */
        ResetMissionState(0);
    }
}

static void OnPlayer(HfMod* mod, const HfEventInfo* info, void* user)
{
    (void)user;
    const HfPlayerEvent* p = (const HfPlayerEvent*)info;
    if (!p || !p->local || !g_missionOn) return;
    if (info->kind == HF_ON_PLAYER_DOWN) {
        uint32_t probe = (uint32_t)InterlockedCompareExchange(&g_probeEntity, 0, 0);
        LONG reg_done = InterlockedCompareExchange(&g_regDone, 0, 0);
        LONG armed = InterlockedCompareExchange(&g_armed, 0, 0);
        if ((probe != 0u && reg_done != 0) || (probe == 0u && armed == 0)) {
            DropAttachedMeleeForRespawn("HF_ON_PLAYER_DOWN");
        }
        InterlockedExchange(&g_avatar, 0);
    } else if (info->kind == HF_ON_PLAYER_SPAWN) {
        /* Resolve the Helldiver AVATAR entity (never assign p->entity, which is the Player entity!). */
        uintptr_t game = HF_API(mod)->module_base("game.dll", NULL);
        SyncLocalAvatarAndRespawn(game);
    }
}

static void OnSessionEnd(HfMod* mod, HfEvent event, void* user)
{
    (void)mod;
    (void)event;
    (void)user;
    InterlockedExchange(&g_stopThreads, 1);
    InterlockedExchange(&g_missionOn, 0);
    InterlockedExchange(&g_armed, 0);
    if (g_stopEvent != NULL) {
        SetEvent(g_stopEvent);
    }
}

/* ---- Spawn Extra Melee Beside Authoritative Local Secondary ------------------------------------------------------- */

static uint64_t SpawnReqDetour(uint64_t a, uint64_t b, uint64_t c, uint64_t d, uint64_t e, uint64_t f)
{
    /* Ultra-fast register-only reject: 100% of enemy, SEAF, corpse, projectile, and primary spawns
     * exit immediately in ~4 ns with ZERO ReadProcessMemory syscalls and ZERO TEB/MXCSR serializations. */
    if (InterlockedCompareExchange(&g_armed, 0, 0) == 0 ||
        InterlockedCompareExchange(&g_missionOn, 0, 0) == 0 ||
        InterlockedCompareExchange(&g_spawningClone, 0, 0) != 0 ||
        a <= 0x10000ULL || c <= 0x10000ULL ||
        !IsSecondaryWeaponResource(b)) {
        return g_spawnReq(a, b, c, d, e, f);
    }

    DWORD saved_err = GetLastError();
    unsigned int saved_csr = _mm_getcsr();

    uint64_t resource = b;
    uint8_t before[0x58];
    int match = 0;
    if (g_hf->read((uintptr_t)c, before, sizeof(before)) == 0) {
        /* before[0] is SpawnInfo.authority: 1 = locally owned/simulated, 0 = remote peer/enemy! */
        if (before[0] != 0u) {
            match = 1;
        }
    }

    _mm_setcsr(saved_csr);
    SetLastError(saved_err);
    uint64_t r = g_spawnReq(a, b, c, d, e, f);
    if (!match) return r;

    saved_err = GetLastError();
    saved_csr = _mm_getcsr();

    InterlockedExchange(&g_armed, 0);
    g_secondaryRes = resource;

    uint64_t melee = ReadMeleeResource();
    if (melee == 0ULL) {
        InterlockedExchange(&g_armed, 1);
        g_hf->log(g_mod, "no melee set in melee-slot-weapon.txt; staying armed for next respawn");
        _mm_setcsr(saved_csr);
        SetLastError(saved_err);
        return r;
    }
    if (melee == resource) {
        InterlockedExchange(&g_armed, 1);
        g_hf->log(g_mod, "configured melee matches equipped secondary (%s); skipping duplicate clone",
            *g_meleeName ? g_meleeName : "same resource");
        _mm_setcsr(saved_csr);
        SetLastError(saved_err);
        return r;
    }

    uint32_t original = 0u, copy = 0u;
    g_hf->read((uintptr_t)a, &original, 4);

    if (g_spawnScratch == NULL) {
        g_spawnScratch = (uint8_t*)VirtualAlloc(NULL, SCRATCH_ALLOC_SIZE, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    }
    uint8_t* region = g_spawnScratch;
    uint8_t* data = region ? region + 0x1000u : NULL;
    uint64_t ctxPtr = 0ULL;
    size_t span = 0u, ctxSize = 0u;
    memcpy(&ctxPtr, before + 0x48u, 8);
    if (region != NULL) {
        memset(region, 0, SCRATCH_ALLOC_SIZE);
        for (size_t n = 0x2000u; n >= 0x100u && span == 0u; n -= 0x100u) {
            if (g_hf->read((uintptr_t)c, data, n) == 0) span = n;
        }
        if (span > 0u) {
            /* Restore pre-call [before+0..0x58] so [data+0x50] is clean (original SpawnReq sets [c+0x50] to its own stack)
             * AND set [data+4] = 0x7FFF (non-networked sentinel at SpawnEntity 0xFDC263) so we never clobber the real
             * secondary pistol's network_id in EntityManager's EVP_NETWORK_MAP during co-op! */
            memcpy(data, before, sizeof(before));
            uint32_t no_net = NET_ID_NONE;
            memcpy(data + 4u, &no_net, 4);
            uint64_t zero_stack = 0ULL;
            memcpy(data + 0x50u, &zero_stack, 8);

            uint64_t mine = 0ULL;
            if (ctxPtr >= c && ctxPtr < c + span) {
                mine = (uint64_t)(uintptr_t)data + (ctxPtr - c);
            } else if (ctxPtr > 0x10000ULL) {
                uint8_t* spare = region + 0x5000u;
                size_t got = 0u;
                for (size_t n = 0x600u; n >= 0x100u && got == 0u; n -= 0x100u) {
                    if (g_hf->read((uintptr_t)ctxPtr, spare, n) == 0) got = n;
                }
                mine = got ? (uint64_t)(uintptr_t)spare : ctxPtr;
            }
            memcpy(data + 0x48u, &mine, 8);
            ctxSize = span;
        }
    }

    if (ctxSize == 0u) {
        InterlockedExchange(&g_probeEntity, 0);
        InterlockedExchange(&g_probeOriginal, 0);
        InterlockedExchange(&g_regDone, 1);
        InterlockedExchange(&g_armed, 1);
        g_hf->log(g_mod, "spawn skipped: spawn context unreadable; staying armed for next respawn");
        _mm_setcsr(saved_csr);
        SetLastError(saved_err);
        return r;
    }

    InterlockedExchange(&g_spawningClone, 1);
    g_spawnReq((uint64_t)(uintptr_t)&copy, melee, (uint64_t)(uintptr_t)data, d, e, f);
    InterlockedExchange(&g_spawningClone, 0);

    if (copy == 0u || original == 0u) {
        /* H3d: Never permanently disarm the mission if g_spawnReq returned copy == 0; re-arm cleanly for next respawn! */
        InterlockedExchange(&g_probeEntity, 0);
        InterlockedExchange(&g_probeOriginal, 0);
        InterlockedExchange(&g_regDone, 1);
        InterlockedExchange(&g_armed, 1);
        g_hf->log(g_mod, "spawn failed: extra melee %s returned entity %u (orig %u); re-armed for next respawn",
            *g_meleeName ? g_meleeName : "(by id)", copy, original);
        _mm_setcsr(saved_csr);
        SetLastError(saved_err);
        return r;
    }

    InterlockedExchange(&g_probeOriginal, (LONG)original);
    g_probeTick = GetTickCount64();
    g_copyResource = melee;
    InterlockedExchange(&g_regDone, 0);
    InterlockedExchange(&g_probeEntity, (LONG)copy);
    g_hf->log(g_mod, "spawned extra melee %s (%08x%08x) as local entity %u beside secondary (%08x%08x, entity %u)",
        *g_meleeName ? g_meleeName : "(by id)",
        (unsigned)(melee >> 32), (unsigned)(melee & 0xFFFFFFFFu), copy,
        (unsigned)(resource >> 32), (unsigned)(resource & 0xFFFFFFFFu), original);
    DumpResident("at melee clone spawn");

    _mm_setcsr(saved_csr);
    SetLastError(saved_err);
    return r;
}

/* ---- Attach Extra Melee & Preserve All 4 Slots' +0x2E8 Words ------------------------------------------------------ */

static int UnitOfEntity(uintptr_t game, uint32_t entity, uint64_t* resource, uint32_t* unit)
{
    uintptr_t em = 0, rec = 0;
    uint32_t index = 0;
    uint8_t d[24];
    if (!entity || !g_hf || !Ptr(game + ENTITIES_GLOBAL, &em) || !MapGet(em + EM_ENTITY_MAP, entity, &index)) {
        return 0;
    }
    CdEmLayout layout = cd_em_resolve_hf(game, g_hf->read);
    if (index >= layout.capacity) return 0;
    rec = cd_em_record_at(em, layout.records_offset, index);
    if (!rec || g_hf->read(rec, d, sizeof(d)) != 0) {
        return 0;
    }
    uint32_t named = 0;
    memcpy(&named, d + 8u, 4);
    if (named != entity) return 0;
    memcpy(resource, d, 8);
    memcpy(unit, d + 12u, 4);
    return 1;
}

static void AttachCopy(HfMod* mod)
{
    const HfApi* hf = HF_API(mod);
    uint32_t copy = (uint32_t)InterlockedCompareExchange(&g_probeEntity, 0, 0);
    uint32_t avatar = (uint32_t)InterlockedCompareExchange(&g_avatar, 0, 0);
    uint32_t owner = 0u, state = 2u;
    ULONGLONG now = GetTickCount64();
    if (InterlockedCompareExchange(&g_regDone, 0, 0) != 0 || !copy || !avatar || !g_missionOn || !g_regOk || !g_attachFn) {
        return;
    }
    if (now < g_probeTick + 1500ULL) return;

    uintptr_t game = hf->module_base("game.dll", NULL), tbl = 0, recs = 0, inv = 0, irec = 0;
    uint32_t index = 0u, orig = (uint32_t)InterlockedCompareExchange(&g_probeOriginal, 0, 0), oindex = 0u;
    if (!game || !Ptr(game + ENT_STATE_GLOBAL, &tbl) || !MapGet(tbl + 0x20u, copy, &index) ||
        index >= 0x100000u || !Ptr(tbl + 0x40u, &recs)) {
        if (now > g_probeTick + 8000ULL) {
            DropAttachedMeleeForRespawn("melee entity missing from state table");
        }
        return;
    }

    /* Wait until the loadout builder assigns our Helldiver avatar as owner of the original secondary.
     * If after 8 s a locally spawned secondary belongs to someone else (e.g. a non-player prop), re-arm cleanly! */
    if (!MapGet(tbl + 0x20u, orig, &oindex) || oindex >= 0x100000u ||
        hf->read(recs + (uintptr_t)oindex * 0x30u + 4u, &owner, 4) != 0 || owner != avatar) {
        if (now > g_probeTick + 8000ULL && owner != 0u && owner != avatar) {
            DropAttachedMeleeForRespawn("probed secondary owned by non-local entity");
        }
        return;
    }
    if (!InvRecord(game, avatar, &inv, &irec)) return;

    /* H3c: Validate UnitOfEntity BEFORE setting g_regDone = 1, and clear g_probeEntity = 0 on failure so DoSwap
     * can NEVER write an unattached/invalid entity ID into the player's secondary weapon slot! */
    uint64_t have = 0ULL;
    uint32_t unit = 0u;
    if (!UnitOfEntity(game, copy, &have, &unit) || have != g_copyResource || !unit) {
        InterlockedExchange(&g_probeEntity, 0);
        InterlockedExchange(&g_probeOriginal, 0);
        InterlockedExchange(&g_regDone, 1);
        InterlockedExchange(&g_armed, 1);
        hf->log(mod, "attach skipped: entity %u is not expected melee; cleared probe and re-armed", copy);
        return;
    }

    /* Snapshot ALL 4 player slots' +0x2E8 renderer words before calling 0x8BF870 (which writes slot 0 / edi +0x2E8). */
    uintptr_t pm = g_mod ? (uintptr_t)hf_global_PlayerManager(g_mod) : 0;
    if (!pm) Ptr(game + PLAYERS_GLOBAL, &pm);
    uint32_t words_before[PLAYERS_MAX] = {0};
    int have_word[PLAYERS_MAX] = {0};
    if (pm != 0) {
        for (uint32_t s = 0; s < PLAYERS_MAX; s++) {
            uintptr_t waddr = pm + (uintptr_t)s * PLAYERS_SLOT_STRIDE + PLAYERS_WORD_OFF;
            if (hf->read(waddr, &words_before[s], 4) == 0) have_word[s] = 1;
        }
    }

    g_attachFn((uint64_t)tbl, copy, avatar, 0, 0, 0);

    int restored = 0;
    if (pm != 0) {
        for (uint32_t s = 0; s < PLAYERS_MAX; s++) {
            if (!have_word[s]) continue;
            uintptr_t waddr = pm + (uintptr_t)s * PLAYERS_SLOT_STRIDE + PLAYERS_WORD_OFF;
            uint32_t after = 0u;
            if (hf->read(waddr, &after, 4) == 0 && after != words_before[s]) {
                hf->write(waddr, &words_before[s], 4);
                restored++;
            }
        }
    }

    hf->write(recs + (uintptr_t)index * 0x30u, &state, 4);
    InterlockedExchange(&g_regDone, 1);
    hf->log(mod, "melee entity %u attached to local Helldiver avatar %u (%d player word(s) restored)",
        copy, avatar, restored);
}

static void RequestPackages(HfMod* mod)
{
    static ULONGLONG nextCheck;
    static char requestedFor[40];
    ULONGLONG now = GetTickCount64();
    if (!InterlockedCompareExchange(&g_avatar, 0, 0) || !g_pkgReq || now < nextCheck) return;
    nextCheck = now + 500ULL;

    char text[2048], key[40];
    int fromFile = ReadConfig("melee-slot-package.txt", text, sizeof(text));
    ReadMeleeResource();
    snprintf(key, sizeof(key), "%s", fromFile ? "(file)" : g_meleeName);
    if (InterlockedCompareExchange(&g_pkgDone, 0, 0) != 0 && strcmp(key, requestedFor) == 0) return;
    InterlockedExchange(&g_pkgDone, 1);
    snprintf(requestedFor, sizeof(requestedFor), "%s", key);
    if (!fromFile) {
        if (!*g_meleePackages) return;
        snprintf(text, sizeof(text), "%s", g_meleePackages);
    }
    uintptr_t game = HF_API(mod)->module_base("game.dll", NULL), system = 0;
    if (!game || !Ptr(game + PKG_SYSTEM_GLOBAL, &system)) return;

    int requested = 0;
    for (char* p = text; *p && requested < 40;) {
        while (*p == ' ' || *p == '\r' || *p == '\n' || *p == '\t') p++;
        if (!*p) break;
        char* end = p;
        uint64_t id = strtoull(p, &end, 16);
        if (end == p || end - p != 16) {
            while (*p && *p != ' ' && *p != '\n' && *p != '\r') p++;
            continue;
        }
        p = end;
        g_pkgReq((uint64_t)system, (uint64_t)(uintptr_t)&id, 1, 0, 0, 0);
        requested++;
    }
    HF_API(mod)->log(mod, "packages: %d requested for %s", requested, key[0] ? key : "(by id)");
}

/* ---- Secondary <-> Extra Melee Swap State Machine ----------------------------------------------------------------- */

static void DoSwap(HfMod* mod)
{
    const HfApi* hf = HF_API(mod);
    uintptr_t game = hf->module_base("game.dll", NULL), inv = 0, rec = 0;
    uint32_t avatar = (uint32_t)InterlockedCompareExchange(&g_avatar, 0, 0);
    uint32_t clone = (uint32_t)InterlockedCompareExchange(&g_probeEntity, 0, 0);
    uint32_t orig = (uint32_t)InterlockedCompareExchange(&g_probeOriginal, 0, 0);
    uint32_t cur = 0u, selection = 0u;
    if (!g_missionOn || !game || !avatar || !clone || !orig ||
        InterlockedCompareExchange(&g_regDone, 0, 0) == 0 || g_swapStage) {
        return;
    }
    if (!InvRecord(game, avatar, &inv, &rec) ||
        hf->read(rec + 4u, &cur, 4) != 0 ||
        hf->read(rec + 0x1Cu, &selection, 4) != 0) {
        return;
    }
    if (selection != 2u) return;
    if (cur != clone && cur != orig) return;

    g_swapTarget = (cur == clone) ? orig : clone;
    g_swapStarted = GetTickCount64();
    g_swapDeadline = g_swapStarted + SWAP_WAIT_MS + 1500ULL;
    g_retries = 0;
    g_lastPress = g_swapStarted;
    g_swapStage = 1;
    InterlockedExchange(&g_injectSel, 1);
}

static void SwapStages(HfMod* mod)
{
    const HfApi* hf = HF_API(mod);
    ULONGLONG now = GetTickCount64();
    if (!g_swapStage) return;
    uintptr_t game = hf->module_base("game.dll", NULL), inv = 0, rec = 0;
    uint32_t avatar = (uint32_t)InterlockedCompareExchange(&g_avatar, 0, 0), selection = 0u;
    if (!g_missionOn || !game || !avatar || !InvRecord(game, avatar, &inv, &rec) ||
        hf->read(rec + 0x1Cu, &selection, 4) != 0) {
        g_swapStage = 0;
        return;
    }
    if (g_swapStage == 1) {
        if (selection != 1u) {
            if (now - g_lastPress > 600ULL && g_retries < 2) {
                g_retries++;
                g_lastPress = now;
                InterlockedExchange(&g_injectSel, 1);
                return;
            }
            if (now > g_swapDeadline) g_swapStage = 0;
            return;
        }
        g_swapAt = now + HOLSTER_MS;
        g_swapStage = 3;
    }
    if (g_swapStage == 3) {
        if (now < g_swapAt) return;
        if (selection != 1u) {
            g_swapStage = 0;
            return;
        }
        uint32_t target = g_swapTarget;
        if (!target || hf->write(rec + 4u, &target, 4) != 0) {
            g_swapStage = 0;
            return;
        }
        g_swapAt = now + DRAW_MS;
        g_swapStage = 2;
    }
    if (g_swapStage == 2) {
        if (now < g_swapAt) return;
        InterlockedExchange(&g_injectSel, 2);
        g_swapDeadline = now + SWAP_WAIT_MS;
        g_swapStage = 4;
        return;
    }
    if (selection == 2u || now > g_swapDeadline) {
        uint32_t after = 0u;
        uint32_t clone = (uint32_t)InterlockedCompareExchange(&g_probeEntity, 0, 0);
        hf->read(rec + 4u, &after, 4);
        hf->log(mod, "swap to %s complete (%llu ms)",
            (clone != 0u && after == clone) ? "melee" : "pistol",
            (unsigned long long)(now - g_swapStarted));
        g_swapStage = 0;
    }
}

static void Tick(HfMod* mod, float dt, void* user)
{
    (void)dt;
    (void)user;
    if (!g_missionOn) return;
    uintptr_t game = HF_API(mod)->module_base("game.dll", NULL);
#ifndef DMS_UNIT_TEST
    static uint32_t s_syncDiv;
    int needFastSync = (InterlockedCompareExchange(&g_avatar, 0, 0) == 0) ||
                       (InterlockedCompareExchange(&g_regDone, 0, 0) == 0) ||
                       (g_swapStage != 0) ||
                       (((++s_syncDiv) & 7u) == 0u);
    if (game && needFastSync) SyncLocalAvatarAndRespawn(game);
#else
    if (game) SyncLocalAvatarAndRespawn(game);
#endif
    AttachCopy(mod);
    RequestPackages(mod);
    if (InterlockedExchange(&g_tapFlag, 0)) DoSwap(mod);
    SwapStages(mod);
}

/* ---- Custom Slot Keybind Reader (Backported from v1.6.3) & Focus-Safe KeyThread ---------------------------------- */

typedef struct { const char* name; WORD scan; } KeyName;
static const KeyName kKeyNames[] = {
    {"1",2},{"2",3},{"3",4},{"4",5},{"5",6},{"6",7},{"7",8},{"8",9},{"9",10},{"0",11},
    {"q",0x10},{"w",0x11},{"e",0x12},{"r",0x13},{"t",0x14},{"y",0x15},{"u",0x16},{"i",0x17},{"o",0x18},{"p",0x19},
    {"a",0x1E},{"s",0x1F},{"d",0x20},{"f",0x21},{"g",0x22},{"h",0x23},{"j",0x24},{"k",0x25},{"l",0x26},
    {"z",0x2C},{"x",0x2D},{"c",0x2E},{"v",0x2F},{"b",0x30},{"n",0x31},{"m",0x32},
    {"space",0x39},{"tab",0x0F},{"enter",0x1C},{"backspace",0x0E},{"caps lock",0x3A},{"backtick",0x29},{"minus",0x0C},{"equals",0x0D},
    {"left ctrl",0x1D},{"left shift",0x2A},{"left alt",0x38},{"right shift",0x36},
    {"f1",0x3B},{"f2",0x3C},{"f3",0x3D},{"f4",0x3E},{"f5",0x3F},{"f6",0x40},{"f7",0x41},{"f8",0x42},{"f9",0x43},{"f10",0x44},{"f11",0x57},{"f12",0x58},
};

static int CfgField(const char* s, const char* e, const char* key, char* out, int size)
{
    size_t kl = strlen(key);
    for (const char* p = s; p + kl < e; p++) {
        if (memcmp(p, key, kl) != 0 ||
            (p > s && p[-1] != ' ' && p[-1] != '\t' && p[-1] != '\n' && p[-1] != '\r' && p[-1] != '{')) {
            continue;
        }
        const char* q = p + kl;
        while (q < e && (*q == ' ' || *q == '\t')) q++;
        if (q >= e || *q != '=') continue;
        q++;
        while (q < e && (*q == ' ' || *q == '\t')) q++;
        int n = 0;
        if (q < e && *q == '"') {
            q++;
            while (q < e && *q != '"' && n < size - 1) out[n++] = *q++;
        } else {
            while (q < e && *q != ' ' && *q != '\t' && *q != '\n' && *q != '\r' && *q != '}' && n < size - 1) {
                out[n++] = *q++;
            }
        }
        out[n] = '\0';
        return 1;
    }
    return 0;
}

static int CfgActionKey(const char* text, const char* action, WORD* scan, LONG* ms)
{
    size_t al = strlen(action);
    for (const char* p = strstr(text, action); p; p = strstr(p + 1, action)) {
        if (p > text && p[-1] != '\n' && p[-1] != '\t' && p[-1] != ' ') continue;
        const char* q = p + al;
        while (*q == ' ' || *q == '\t') q++;
        if (*q != '=') continue;
        const char* end = strchr(q, ']');
        if (!end) return 0;
        for (const char* b = strchr(q, '{'); b && b < end; b = strchr(b + 1, '{')) {
            const char* c = strchr(b, '}');
            if (!c || c > end) break;
            char device[32], input[32], trigger[32], threshold[32];
            if (!CfgField(b, c, "device_type", device, sizeof(device)) || strcmp(device, "Keyboard") != 0) continue;
            if (!CfgField(b, c, "input", input, sizeof(input))) continue;
            if (!CfgField(b, c, "trigger", trigger, sizeof(trigger))) snprintf(trigger, sizeof(trigger), "Press");
            if (!CfgField(b, c, "threshold", threshold, sizeof(threshold))) snprintf(threshold, sizeof(threshold), "0");
            for (size_t k = 0; k < sizeof(kKeyNames) / sizeof(kKeyNames[0]); k++) {
                if (strcmp(input, kKeyNames[k].name) == 0) {
                    *scan = kKeyNames[k].scan;
                    double t = atof(threshold);
                    *ms = (strcmp(trigger, "LongPress") == 0 || strcmp(trigger, "Hold") == 0)
                              ? (LONG)(t * 1000.0 + 100.0)
                              : 25;
                    if (*ms > 1500) *ms = 1500;
                    if (*ms < 25) *ms = 25;
                    return 1;
                }
            }
            return 0;
        }
        return 0;
    }
    return 0;
}

static void ParseSlotKeysText(const char* text)
{
    static const char* actions[3] = {NULL, "ChangeEquipmentPrimary", "ChangeEquipmentSecondary"};
    static const WORD defScan[3] = {0, 2, 3};
    static const LONG defVk[3] = {0, '1', '2'};
    for (int sel = 1; sel <= 2; sel++) {
        WORD scan = defScan[sel];
        LONG ms = 25;
        int ok = text ? CfgActionKey(text, actions[sel], &scan, &ms) : 0;
        if (!ok) {
            scan = defScan[sel];
            ms = 25;
        }
        UINT vk = MapVirtualKeyA(scan, MAPVK_VSC_TO_VK);
        LONG newVk = ok ? (LONG)(vk ? vk : (UINT)defVk[sel]) : defVk[sel];
        InterlockedExchange(&g_keyScan[sel], (LONG)scan);
        InterlockedExchange(&g_keyVk[sel], newVk);
        InterlockedExchange(&g_keyMs[sel], ms);
    }
}

static int DmsIsSep(char c)
{
    return c == '\\' || c == '/';
}

static int DmsAsciiIeqSpan(const char* a, size_t alen, const char* b)
{
    size_t blen = strlen(b);
    if (alen != blen) return 0;
    for (size_t i = 0; i < alen; i++) {
        char ca = (a[i] >= 'A' && a[i] <= 'Z') ? (char)(a[i] + 32) : a[i];
        char cb = (b[i] >= 'A' && b[i] <= 'Z') ? (char)(b[i] + 32) : b[i];
        if (ca != cb) return 0;
    }
    return 1;
}

/* Derives <installRoot> from api->mod_folder(mod) for both folder installs (<installRoot>\mods\<mod>)
 * and .unpacked zip installs (<installRoot>\mods\.unpacked\<hash>[\<submod>]). */
static int ResolveInstallRootFromModFolder(const char* mod_folder, char* out_root, size_t out_cap)
{
    if (!out_root || out_cap == 0) return 0;
    out_root[0] = '\0';
    if (!mod_folder || !mod_folder[0]) return 0;

    size_t len = strlen(mod_folder);
    while (len > 0 && DmsIsSep(mod_folder[len - 1])) {
        if (len == 3 && mod_folder[1] == ':') break;
        if (len == 1) break;
        --len;
    }
    if (len == 0) return 0;

    size_t seg_start[64], seg_end[64], seg_count = 0, i = 0;
    while (i < len) {
        while (i < len && DmsIsSep(mod_folder[i])) ++i;
        if (i >= len) break;
        if (seg_count >= 64u) return 0;
        size_t s = i;
        while (i < len && !DmsIsSep(mod_folder[i])) ++i;
        seg_start[seg_count] = s;
        seg_end[seg_count] = i;
        ++seg_count;
    }
    if (seg_count < 3u) return 0;

    size_t mods_idx = (size_t)-1;
    for (size_t k = seg_count; k > 0; --k) {
        size_t idx = k - 1u;
        if (DmsAsciiIeqSpan(mod_folder + seg_start[idx], seg_end[idx] - seg_start[idx], ".unpacked")) {
            if (idx < 2u || idx + 1u >= seg_count) return 0;
            mods_idx = idx - 1u;
            break;
        }
    }
    if (mods_idx == (size_t)-1) {
        mods_idx = seg_count - 2u;
    }
    if (mods_idx == 0u || !DmsAsciiIeqSpan(mod_folder + seg_start[mods_idx], seg_end[mods_idx] - seg_start[mods_idx], "mods")) {
        return 0;
    }

    size_t root_len = seg_end[mods_idx - 1u];
    if (root_len == 0 || root_len + 1u > out_cap) return 0;
    for (size_t p = 0; p < root_len; p++) {
        out_root[p] = (mod_folder[p] == '/') ? '\\' : mod_folder[p];
    }
    out_root[root_len] = '\0';
    return 1;
}

static int FindNewestInputSettingsInInstallRoot(
    const char* install_root,
    char* best_out,
    size_t best_cap,
    FILETIME* best_when_out)
{
    if (!install_root || !install_root[0] || !best_out || best_cap == 0 || !best_when_out) return 0;
    char pattern[MAX_PATH + 192], candidate[MAX_PATH + 256];
    snprintf(pattern, sizeof(pattern), "%s\\userdata\\profiles\\*", install_root);

    WIN32_FIND_DATAA fd;
    HANDLE f = FindFirstFileA(pattern, &fd);
    if (f == INVALID_HANDLE_VALUE) return 0;

    int found = 0;
    FILETIME best_when = {0, 0};
    do {
        if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || fd.cFileName[0] == '.') continue;
        WIN32_FILE_ATTRIBUTE_DATA info;
        snprintf(candidate, sizeof(candidate), "%s\\userdata\\profiles\\%s\\cloud\\input_settings.config",
            install_root, fd.cFileName);
        if (GetFileAttributesExA(candidate, GetFileExInfoStandard, &info) &&
            !(info.dwFileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) &&
            (!found || CompareFileTime(&info.ftLastWriteTime, &best_when) > 0)) {
            best_when = info.ftLastWriteTime;
            snprintf(best_out, best_cap, "%s", candidate);
            found = 1;
        }
    } while (FindNextFileA(f, &fd));
    FindClose(f);

    if (found) *best_when_out = best_when;
    return found;
}

static void LoadSlotKeys(void)
{
    char best[MAX_PATH + 256];
    best[0] = '\0';
    FILETIME bestWhen = {0, 0};

    /* L3: Prefer the active HellForge install root derived from api->mod_folder(mod) (e.g. D:\HellForge),
     * falling back to %USERPROFILE%\Documents\HellForge when no custom-root profile config is present. */
    const char* mod_folder = (g_hf != NULL && g_mod != NULL && g_hf->mod_folder != NULL) ? g_hf->mod_folder(g_mod) : NULL;
    char install_root[MAX_PATH + 128];
    if (ResolveInstallRootFromModFolder(mod_folder, install_root, sizeof(install_root))) {
        FindNewestInputSettingsInInstallRoot(install_root, best, sizeof(best), &bestWhen);
    }

    if (!best[0]) {
        char user_root[MAX_PATH + 64], docs_hf[MAX_PATH + 128];
        DWORD n = GetEnvironmentVariableA("USERPROFILE", user_root, MAX_PATH);
        if (n > 0 && n < MAX_PATH) {
            snprintf(docs_hf, sizeof(docs_hf), "%s\\Documents\\HellForge", user_root);
            FindNewestInputSettingsInInstallRoot(docs_hf, best, sizeof(best), &bestWhen);
        }
    }

    if (!best[0]) return;
    if (strcmp(best, g_lastKeysPath) == 0 && CompareFileTime(&bestWhen, &g_lastKeysWhen) == 0) return;
    snprintf(g_lastKeysPath, sizeof(g_lastKeysPath), "%s", best);
    g_lastKeysWhen = bestWhen;

    HANDLE h = CreateFileA(best, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING,
                           FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, NULL);
    if (h == INVALID_HANDLE_VALUE) {
        g_lastKeysWhen.dwLowDateTime = 0;
        g_lastKeysWhen.dwHighDateTime = 0;
        return;
    }
    HANDLE heap = GetProcessHeap();
    char* text = heap ? (char*)HeapAlloc(heap, HEAP_ZERO_MEMORY, 65536u) : NULL;
    if (!text) {
        CloseHandle(h);
        return;
    }
    DWORD got = 0;
    ReadFile(h, text, 65535u, &got, NULL);
    CloseHandle(h);
    text[got] = '\0';
    ParseSlotKeysText(text);
    HeapFree(heap, 0, text);
}

/* Interruptible wait backed by g_stopEvent so KeyThread wakes in < 1 ms on session end or DLL unload. */
static int WaitStopMs(DWORD ms)
{
    if (InterlockedCompareExchange(&g_stopThreads, 0, 0) != 0) return 1;
    if (g_stopEvent != NULL) {
        return WaitForSingleObject(g_stopEvent, ms) == WAIT_OBJECT_0 ? 1 : 0;
    }
    DWORD waited = 0u;
    while (waited < ms) {
        if (InterlockedCompareExchange(&g_stopThreads, 0, 0) != 0) return 1;
        DWORD step = (ms - waited) < 16u ? (ms - waited) : 16u;
        Sleep(step);
        waited += step;
    }
    return InterlockedCompareExchange(&g_stopThreads, 0, 0) != 0;
}

static void DmsSendInputKey(WORD scan, DWORD flags)
{
#ifdef DMS_UNIT_TEST
    (void)scan;
    if (flags & KEYEVENTF_KEYUP) InterlockedIncrement(&g_testSentKeyUps);
    else InterlockedIncrement(&g_testSentKeyDowns);
#else
    INPUT in;
    memset(&in, 0, sizeof(in));
    in.type = INPUT_KEYBOARD;
    in.ki.wScan = scan;
    in.ki.dwFlags = flags;
    SendInput(1, &in, sizeof(in));
#endif
}

static int DmsIsVkDown(int vk)
{
#ifdef DMS_UNIT_TEST
    (void)vk;
    return 0;
#else
    return vk != 0 ? ((GetAsyncKeyState(vk) & 0x8000) != 0) : 0;
#endif
}

static DWORD WINAPI KeyThread(LPVOID unused)
{
    (void)unused;
    int was2 = 0;
    ULONGLONG nextKeys = 0ULL;
    while (InterlockedCompareExchange(&g_stopThreads, 0, 0) == 0) {
        if (WaitStopMs(16)) break;

        ULONGLONG now = GetTickCount64();
        if (now >= nextKeys) {
            nextKeys = now + 2000ULL;
            LoadSlotKeys();
        }

        int focused = GameWindowFocused();
        LONG sel = InterlockedExchange(&g_injectSel, 0);
        if (sel >= 1 && sel <= 2 && focused) {
            WORD scan = (WORD)InterlockedCompareExchange(&g_keyScan[sel], 0, 0);
            int vk2 = (int)InterlockedCompareExchange(&g_keyVk[2], 0, 0);
            DWORD hold_ms = (DWORD)InterlockedCompareExchange(&g_keyMs[sel], 0, 0);
            if (sel == 1 && vk2 != 0) {
                for (int w = 0; w < 25 && DmsIsVkDown(vk2); w++) {
                    if (WaitStopMs(16)) break;
                }
            }
            if (GameWindowFocused() && InterlockedCompareExchange(&g_stopThreads, 0, 0) == 0) {
                DmsSendInputKey(scan, KEYEVENTF_SCANCODE);
                WaitStopMs(hold_ms >= 25u ? hold_ms : 25u);
                DmsSendInputKey(scan, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP);
                if (InterlockedCompareExchange(&g_stopThreads, 0, 0) != 0) break;
            }
        }

        if (!focused) {
            was2 = 0;
            continue;
        }
        int vk_sec = (int)InterlockedCompareExchange(&g_keyVk[2], 0, 0);
        int down2 = DmsIsVkDown(vk_sec);
        uint32_t avatar = (uint32_t)InterlockedCompareExchange(&g_avatar, 0, 0);
        uint32_t probe = (uint32_t)InterlockedCompareExchange(&g_probeEntity, 0, 0);
        LONG reg_done = InterlockedCompareExchange(&g_regDone, 0, 0);
        if (down2 && !was2 && g_missionOn && avatar != 0u && probe != 0u && reg_done != 0) {
            uintptr_t game = g_hf->module_base("game.dll", NULL), inv = 0, rec = 0;
            uint32_t selection = 0u;
            if (game && InvRecord(game, avatar, &inv, &rec) &&
                g_hf->read(rec + 0x1Cu, &selection, 4) == 0 && selection == 2u) {
                InterlockedExchange(&g_tapFlag, 1);
            }
        }
        was2 = down2;
    }
    return 0;
}

/* ---- Load & Unload ------------------------------------------------------------------------------------------------ */

static void GameLoaded(HfMod* mod, HfEvent event, void* user)
{
    (void)event;
    (void)user;
    if (g_hookInstalled) return;
    const HfApi* hf = HF_API(mod);
    const char* build = hf->build_id ? hf->build_id() : "";
    if (!build || strcmp(build, DMS_EXPECTED_BUILD) != 0) {
        hf->log(mod, "unsupported build '%s' (expected %s); staying idle", build ? build : "?", DMS_EXPECTED_BUILD);
        return;
    }
    uintptr_t game = hf->module_base("game.dll", NULL);
    if (!game) {
        hf->log(mod, "game.dll not found; staying idle");
        return;
    }
    if (!hf->code_matches(game + SPAWN_REQ_RVA, "4c 8b dc 49 89 5b 10 49 89 73 20 57 48 81 ec a0 00 00 00")) {
        hf->log(mod, "spawn function 0xFD9710 is not the reviewed one; staying idle");
        return;
    }
    int rc = 1;
    if (hf->size >= offsetof(HfApi, hook_ex) + sizeof(void*) && hf->hook_ex != NULL) {
        rc = hf->hook_ex(mod, game + SPAWN_REQ_RVA, (void*)SpawnReqDetour, (void**)&g_spawnReq, 0);
    } else {
        rc = hf->hook(mod, game + SPAWN_REQ_RVA, (void*)SpawnReqDetour, (void**)&g_spawnReq);
    }
    if (rc != 0) {
        hf->log(mod, "spawn hook: FAILED");
        return;
    }
    g_hookInstalled = 1;

    if (hf->code_matches(game + PKG_REQUEST_RVA, "45 85 c0 0f 84 75 01 00 00 41 56 41 57 48 83 ec 38")) {
        g_pkgReq = (PkgReqFn)(game + PKG_REQUEST_RVA);
    } else {
        hf->log(mod, "package function 0x12FE350 mismatch; package requests disabled");
    }

    g_regOk = hf->code_matches(game + ATTACH_RVA, "4c 8b dc 55 53 41 56 49 8d 6b a8 48 81 ec 40 01 00 00");
    if (g_regOk) {
        g_attachFn = (AttachFn)(game + ATTACH_RVA);
    } else {
        hf->log(mod, "attach function 0x8BF870 mismatch; melee attach disabled");
    }

    if (hf->size >= offsetof(HfApi, game_state) + sizeof(void*) && hf->game_state != NULL &&
        hf->game_state(mod) == HF_STATE_MISSION) {
        ResetMissionState(1);
    }
    hf->log(mod, "Dedicated Melee Slot 1.5.2-clonedivers.4 hooks ready");
}

HF_MOD_EXPORT int hellforge_mod_init(const HfApi* hf, HfMod* mod)
{
    if (!hf || !mod || hf->version < 13u) return 1;
    g_mod = mod;
    g_hf = hf;
    g_hookInstalled = 0;
    memset(&g_lastKeysWhen, 0, sizeof(g_lastKeysWhen));
    g_lastKeysPath[0] = '\0';
    InterlockedExchange(&g_stopThreads, 0);
    if (g_stopEvent != NULL) {
        CloseHandle(g_stopEvent);
        g_stopEvent = NULL;
    }
    g_stopEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
    ResetMissionState(0);

    int ok = hf->subscribe(mod, HF_ON_WEAPON, OnWeapon, NULL, 0);
    hf->subscribe(mod, HF_ON_STATE, OnState, NULL, 100);
    hf->subscribe(mod, HF_ON_MISSION_START, OnMission, NULL, 100);
    hf->subscribe(mod, HF_ON_MISSION_END, OnMission, NULL, 100);
    hf->subscribe(mod, HF_ON_PLAYER_SPAWN, OnPlayer, NULL, 0);
    hf->subscribe(mod, HF_ON_PLAYER_DOWN, OnPlayer, NULL, 0);
    if (hf->version >= 16u) {
        hf->on(mod, HF_EVENT_SESSION_END, OnSessionEnd, NULL);
    }
    hf->on(mod, HF_EVENT_GAME_LOADED, GameLoaded, NULL);

    int tick = (hf->size >= offsetof(HfApi, on_game_thread) + sizeof(void*) && hf->on_game_thread != NULL)
                   ? hf->on_game_thread(mod, Tick, NULL)
                   : 0;
#ifndef DMS_UNIT_TEST
    /* H3a: No InvThread! SyncLocalAvatarAndRespawn runs strictly on the game thread via Tick & OnPlayer. */
    g_keyThread = CreateThread(NULL, 0, KeyThread, NULL, 0, NULL);
#endif
    hf->log(mod, "Dedicated Melee Slot 1.5.2-clonedivers.4 loaded (weapon=%d, tick=%d)", ok, tick);
    return 0;
}

HF_MOD_EXPORT int hellforge_mod_unload(const HfApi* hf, HfMod* mod, uint32_t reason)
{
    (void)reason;
    InterlockedExchange(&g_stopThreads, 1);
    if (g_stopEvent != NULL) {
        SetEvent(g_stopEvent);
    }
    ResetMissionState(0);

    if (g_keyThread) {
        WaitForSingleObject(g_keyThread, 2000);
        CloseHandle(g_keyThread);
        g_keyThread = NULL;
    }
    if (g_stopEvent != NULL) {
        CloseHandle(g_stopEvent);
        g_stopEvent = NULL;
    }
    if (g_hookInstalled && hf != NULL && hf->size >= offsetof(HfApi, unhook) + sizeof(void*) && hf->unhook != NULL) {
        uintptr_t game = hf->module_base ? hf->module_base("game.dll", NULL) : 0;
        if (game) hf->unhook(mod, game + SPAWN_REQ_RVA);
    }
    g_hookInstalled = 0;
    if (g_spawnScratch) {
        VirtualFree(g_spawnScratch, 0, MEM_RELEASE);
        g_spawnScratch = NULL;
    }
    return 0;
}
