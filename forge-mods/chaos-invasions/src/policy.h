/* Chaos Invasions 0.3.3 policy, native pins, geometry/cap/map guards, co-op sync packets, and faction helpers.
 * Pure C helpers here have no engine dependencies and are directly unit-tested offline. */
#ifndef CHAOS_POLICY_H
#define CHAOS_POLICY_H

#include <errno.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "clonedivers_host_options.h"

enum { CHAOS_BOTS = 0, CHAOS_SQUIDS = 1, CHAOS_BUGS = 2, CHAOS_FACTIONS = 3 };

enum {
    CHAOS_NATIVE_BUGS_BIT = 2u,
    CHAOS_NATIVE_SQUIDS_BIT = 4u,
    CHAOS_NATIVE_BOTS_BIT = 8u
};

typedef struct ChaosConfig {
    int enabled, cooldown, grace, total_cap, faction_cap, squad_size, mission_budget;
    int bots_weight, squids_weight, bugs_weight;
} ChaosConfig;

static const char* const chaos_names[] = {"droid_invasion", "squid_ships", "bug_breach"};
static const char* const chaos_faction_names[] = {"Bots", "Squids", "Bugs"};

static ChaosConfig ChaosDefaults(void)
{
    ChaosConfig c = {1, 150, 45, 12, 6, 4, 36, 100, 80, 100};
    return c;
}

/* Applies Clonedivers Launcher host intensity preset ("off" / "standard" / "total-war")
 * within bounded ChaosOption / menu_bridge.lua invariants while keeping mod ContentHash identical. */
static inline void ChaosApplyHostIntensity(ChaosConfig* out, const ChaosConfig* base_ini, CdChaosIntensity mode)
{
    ChaosConfig base = base_ini ? *base_ini : ChaosDefaults();
    *out = base;
    if (mode == CD_CHAOS_OFF) {
        out->enabled = 0;
    } else if (mode == CD_CHAOS_TOTAL_WAR) {
        out->enabled = 1;
        out->cooldown = 75;
        out->grace = 30;
        out->total_cap = 20;
        out->faction_cap = 10;
        out->squad_size = 6;
        out->mission_budget = 72;
        out->bots_weight = 100;
        out->squids_weight = 100;
        out->bugs_weight = 100;
    } else {
        out->enabled = 1;
    }
}

/* Full-line, bounded integers. Reject unknown keys, overflow and trailing junk. */
static int ChaosOption(ChaosConfig* c, const char* key, int n)
{
#define OPTION(name, field, lo, hi) \
    if (!strcmp(key, name)) { if (n < (lo) || n > (hi)) return 0; c->field = n; return 1; }
    OPTION("enabled", enabled, 0, 1)
    OPTION("cooldown_seconds", cooldown, 30, 900)
    OPTION("landing_grace_seconds", grace, 15, 300)
    OPTION("total_cap", total_cap, 1, 24)
    OPTION("faction_cap", faction_cap, 1, 12)
    OPTION("squad_size", squad_size, 1, 6)
    OPTION("mission_budget", mission_budget, 1, 96)
    OPTION("droid_weight", bots_weight, 0, 200)
    OPTION("squid_weight", squids_weight, 0, 200)
    OPTION("bug_weight", bugs_weight, 0, 200)
#undef OPTION
    return 0;
}

static int ChaosParse(ChaosConfig* c, const char* line)
{
    char key[48];
    const char* eq = strchr(line, '=');
    char* end;
    long n;
    if (!eq || eq == line || (size_t)(eq - line) >= sizeof key) return 0;
    memcpy(key, line, (size_t)(eq - line));
    key[eq - line] = 0;
    errno = 0;
    n = strtol(eq + 1, &end, 10);
    if (errno || end == eq + 1 || n < 0 || n > 1000) return 0;
    while (*end == ' ' || *end == '\r' || *end == '\n' || *end == '\t') ++end;
    return !*end && ChaosOption(c, key, (int)n);
}

typedef struct ChaosEvidence {
    int host, mission, players, native_faction, difficulty;
    int closure, resident, peers, placement, constructor, arrival, population_known;
    int live[3], total_live, mission_spawned;
} ChaosEvidence;

typedef struct ChaosClock {
    double age, next;
    unsigned attempts;
} ChaosClock;

static void ChaosReset(ChaosClock* s, const ChaosConfig* c)
{
    memset(s, 0, sizeof *s);
    s->next = (double)c->grace;
}

static int ChaosDue(ChaosClock* s, const ChaosConfig* c, double dt, int active)
{
    if (!active || !isfinite(dt) || dt < 0.0 || dt > 5.0) return 0;
    s->age += dt;
    if (s->age < s->next) return 0;
    s->next = s->age + (double)c->cooldown;
    ++s->attempts;
    return 1;
}

/* Tech-level bands affect relative weighting. One event per cooldown, never a burst. */
static int ChaosWeight(const ChaosConfig* c, int type, int difficulty)
{
    static const int factors[3][3] = {{100, 70, 100}, {100, 100, 120}, {120, 140, 130}};
    int base[3] = {c->bots_weight, c->squids_weight, c->bugs_weight};
    if (type < 0 || type >= 3 || difficulty < 1 || difficulty > 11) return 0;
    return base[type] * factors[difficulty <= 3 ? 0 : difficulty <= 6 ? 1 : 2][type] / 100;
}

static inline int ChaosChoose(const ChaosConfig* c, int native, int difficulty, uint32_t draw)
{
    int weights[3], total = 0;
    if (native < 0 || native >= 3) return -1;
    for (int i = 0; i < 3; i++) {
        weights[i] = (i == native) ? 0 : ChaosWeight(c, i, difficulty);
        total += weights[i];
    }
    if (!total) return -1;
    int at = (int)(draw % (unsigned)total);
    for (int i = 0; i < 3; i++) {
        if (at < weights[i]) return i;
        at -= weights[i];
    }
    return -1;
}

static inline const char* ChaosRefuse(const ChaosConfig* c, const ChaosEvidence* e, int type)
{
    if (!c->enabled) return "disabled";
    if (!e->host) return "not_host";
    if (!e->mission || !e->players) return "no_live_mission_players";
    if (e->native_faction < 0 || e->native_faction >= 3 || e->difficulty < 1 || e->difficulty > 11)
        return "mission_context_unqualified";
    if (type < 0 || type >= 3 || type == e->native_faction) return "not_foreign_faction";
    if (!e->closure) return "faction_package_closure_unqualified";
    if (!e->resident) return "faction_packages_not_resident";
    if (!e->peers) return "peer_and_late_join_barrier_unqualified";
    if (!e->population_known) return "owned_population_unknown";
    if (e->total_live < 0 || e->live[type] < 0 || e->mission_spawned < 0 ||
        e->total_live > c->total_cap - c->squad_size ||
        e->live[type] > c->faction_cap - c->squad_size)
        return "population_cap";
    if (e->mission_spawned > c->mission_budget - c->squad_size) return "mission_budget";
    if (!e->placement) return "all_player_clearance_and_navmesh_unqualified";
    if (!e->constructor) return "networked_enemy_constructor_unqualified";
    if (!e->arrival) return "faction_arrival_unqualified";
    return NULL;
}

static int ChaosFactionFromDirectorMask(uint32_t mask)
{
    if (mask == CHAOS_NATIVE_BOTS_BIT) return CHAOS_BOTS;
    if (mask == CHAOS_NATIVE_SQUIDS_BIT) return CHAOS_SQUIDS;
    if (mask == CHAOS_NATIVE_BUGS_BIT) return CHAOS_BUGS;
    return -1;
}

static uint32_t ChaosDirectorMaskFromFaction(int faction)
{
    if (faction == CHAOS_BOTS) return CHAOS_NATIVE_BOTS_BIT;
    if (faction == CHAOS_SQUIDS) return CHAOS_NATIVE_SQUIDS_BIT;
    if (faction == CHAOS_BUGS) return CHAOS_NATIVE_BUGS_BIT;
    return 0u;
}

static uint32_t ChaosCargoFactionFromFaction(int faction)
{
    if (faction == CHAOS_BOTS) return 3u;
    if (faction == CHAOS_SQUIDS) return 4u;
    if (faction == CHAOS_BUGS) return 2u;
    return 0u;
}

/* ---- Fail-closed 32-byte runtime code pins (game.dll build 25480438) ---- */

typedef struct ChaosCodePin {
    uint32_t rva;
    const char* name;
    uint8_t bytes[32];
} ChaosCodePin;

enum {
    CPIN_SELECT_ROSTER = 0,
    CPIN_SPAWN_ROSTER_ROW,
    CPIN_HAS_HEADROOM,
    CPIN_SPAWN_TRANSPORT,
    CPIN_PROJECT_ANCHOR,
    CPIN_PARTICLE_FX,
    CPIN_BREACH_BURST,
    CPIN_LOOKUP_ENTITY_PKGS,
    CPIN_PKG_REQUEST,
    CPIN_PKGS_IDLE,
    CPIN_PASSENGER_INIT_SLOT,
    CPIN_PASSENGER_RESOLVE_CARGO,
    CPIN_PASSENGER_UNLOAD_FACTION,
    CPIN_COUNT
};

static const ChaosCodePin kChaosCodePins[CPIN_COUNT] = {
    {0x0953150u, "SelectRosterIndex",
     {0x48, 0x89, 0x5c, 0x24, 0x10, 0x44, 0x88, 0x4c, 0x24, 0x20, 0x55, 0x56, 0x57, 0x41, 0x54, 0x41,
      0x55, 0x41, 0x56, 0x41, 0x57, 0x48, 0x83, 0xec, 0x40, 0x4d, 0x8b, 0xe0, 0x8b, 0xda, 0x4c, 0x8b}},
    {0x0953580u, "SpawnRosterRow",
     {0x40, 0x55, 0x53, 0x56, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57, 0x48, 0x8d, 0xac,
      0x24, 0xa8, 0xfd, 0xff, 0xff, 0x48, 0x81, 0xec, 0x58, 0x03, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xbc}},
    {0x0952eb0u, "HasHeadroom",
     {0x48, 0x89, 0x5c, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x10, 0x57, 0x48, 0x83, 0xec, 0x20, 0x4c,
      0x8b, 0x15, 0xa2, 0x35, 0x9d, 0x02, 0x45, 0x33, 0xc9, 0x41, 0x0f, 0xb6, 0xf0, 0x8b, 0xfa, 0x48}},
    {0x08b61f0u, "SpawnTransportEncounter",
     {0x48, 0x89, 0x5c, 0x24, 0x20, 0x55, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57, 0x48, 0x8d,
      0xac, 0x24, 0x60, 0xf6, 0xff, 0xff, 0x48, 0x81, 0xec, 0xa0, 0x0a, 0x00, 0x00, 0x0f, 0x29, 0xb4}},
    {0x08b5c80u, "ProjectGroundAnchor",
     {0x48, 0x8b, 0xc4, 0x48, 0x89, 0x58, 0x18, 0x55, 0x56, 0x57, 0x41, 0x56, 0x41, 0x57, 0x48, 0x81,
      0xec, 0xe0, 0x00, 0x00, 0x00, 0x0f, 0x29, 0x70, 0xc8, 0x0f, 0x29, 0x78, 0xb8, 0x44, 0x0f, 0x29}},
    {0x13c6d30u, "TriggerParticleFx",
     {0x48, 0x8b, 0xc4, 0x48, 0x89, 0x58, 0x08, 0x48, 0x89, 0x70, 0x18, 0x48, 0x89, 0x50, 0x10, 0x57,
      0x48, 0x81, 0xec, 0x90, 0x00, 0x00, 0x00, 0x8b, 0x94, 0x24, 0xd8, 0x00, 0x00, 0x00, 0x48, 0x8b}},
    {0x0be1340u, "TriggerBreachBurst",
     {0x4c, 0x8b, 0xdc, 0x45, 0x89, 0x43, 0x18, 0x48, 0x81, 0xec, 0x98, 0x00, 0x00, 0x00, 0x48, 0x8b,
      0x05, 0xbb, 0xac, 0xa5, 0x01, 0x48, 0x33, 0xc4, 0x48, 0x89, 0x84, 0x24, 0x80, 0x00, 0x00, 0x00}},
    {0x04f8af0u, "LookupEntityPackages",
     {0x48, 0x85, 0xc9, 0x74, 0x71, 0x48, 0x8b, 0x05, 0x9c, 0x34, 0xf7, 0x02, 0x44, 0x8b, 0xc1, 0x4c,
      0x8b, 0x90, 0xe0, 0x26, 0xf1, 0x00, 0x48, 0xb8, 0x75, 0x64, 0xcf, 0x40, 0x54, 0x3a, 0xb2, 0x67}},
    {0x12fe350u, "RefcountedPackageRequest",
     {0x45, 0x85, 0xc0, 0x0f, 0x84, 0x75, 0x01, 0x00, 0x00, 0x41, 0x56, 0x41, 0x57, 0x48, 0x83, 0xec,
      0x38, 0x48, 0x89, 0x5c, 0x24, 0x50, 0x4c, 0x8b, 0xfa, 0x48, 0x89, 0x6c, 0x24, 0x58, 0x4c, 0x8b}},
    {0x12fe680u, "ArePackagesIdle",
     {0x48, 0x83, 0xec, 0x28, 0x48, 0x8b, 0x05, 0x7d, 0x7c, 0x02, 0x02, 0x48, 0x8b, 0x48, 0x10, 0xff,
      0x91, 0x40, 0x03, 0x00, 0x00, 0x85, 0xc0, 0x0f, 0x94, 0xc0, 0x48, 0x83, 0xc4, 0x28, 0xc3, 0xcc}},
    {0x0687e5eu, "PassengerInitSlot",
     {0x8b, 0x42, 0x08, 0x4d, 0x8b, 0xf9, 0x3b, 0x05, 0xca, 0xbd, 0xdf, 0x02, 0x4d, 0x8b, 0xe8, 0x48,
      0x8b, 0x2d, 0x4c, 0xee, 0xc9, 0x02, 0x4c, 0x8b, 0xf2, 0x74, 0x4f, 0x44, 0x8b, 0x55, 0x28, 0x33}},
    {0x0683e50u, "PassengerResolveCargo",
     {0x40, 0x53, 0x55, 0x56, 0x48, 0x81, 0xec, 0xa0, 0x00, 0x00, 0x00, 0x48, 0x8b, 0x05, 0xae, 0x81,
      0xfb, 0x01, 0x48, 0x33, 0xc4, 0x48, 0x89, 0x84, 0x24, 0x80, 0x00, 0x00, 0x00, 0x49, 0x8b, 0x69}},
    {0x06852f0u, "PassengerUnloadFaction",
     {0x74, 0x48, 0x3b, 0xc8, 0x74, 0x48, 0xff, 0xc2, 0x41, 0x3b, 0xd1, 0x72, 0xe3, 0xb8, 0xff, 0xff,
      0xff, 0xff, 0x8b, 0xc8, 0x43, 0x8b, 0x04, 0x2c, 0x4c, 0x6b, 0xf9, 0x1c, 0x4c, 0x03, 0x7f, 0x48}},
};

typedef int (*ChaosReadCodeFn)(uintptr_t address, void* buffer, size_t size);

static int ChaosVerifyCodePins(ChaosReadCodeFn read_code, uintptr_t game_base, size_t game_size,
                               uint32_t* failed_rva, const char** failed_name)
{
    if (failed_rva) *failed_rva = 0u;
    if (failed_name) *failed_name = NULL;
    if (!read_code || !game_base || game_size < 0x3485000u) return 0;
    for (unsigned i = 0; i < CPIN_COUNT; i++) {
        uint8_t got[32];
        const ChaosCodePin* p = &kChaosCodePins[i];
        if ((size_t)p->rva + sizeof got > game_size ||
            read_code(game_base + p->rva, got, sizeof got) != 0 ||
            memcmp(got, p->bytes, sizeof got) != 0) {
            if (failed_rva) *failed_rva = p->rva;
            if (failed_name) *failed_name = p->name;
            return 0;
        }
    }
    return 1;
}

/* ---- Refcounted package map <75% guard & deduplication ---- */

static int ChaosRefMapHasHeadroom(const uint8_t* entries, uint32_t capacity, uint64_t empty_key,
                                  uint64_t pkg_id, uint32_t* used_out, int* already_present)
{
    uint32_t used = 0;
    int present = 0;
    if (used_out) *used_out = 0;
    if (already_present) *already_present = 0;
    if (!entries || capacity < 16u || capacity > 65536u || (capacity & (capacity - 1u)) != 0u ||
        pkg_id == 0ULL || pkg_id == empty_key)
        return 0;
    for (uint32_t i = 0; i < capacity; i++) {
        uint64_t key = 0;
        memcpy(&key, entries + (size_t)i * 16u, sizeof key);
        if (key != empty_key && key != 0ULL) {
            ++used;
            if (key == pkg_id) present = 1;
        }
    }
    if (used_out) *used_out = used;
    if (already_present) *already_present = present;
    /* Keep ref map strictly <75% full after inserting a new package. */
    return present || ((uint64_t)(used + 1u) * 4ULL < (uint64_t)capacity * 3ULL);
}

/* ---- Ground anchor spatial guard (Z up in Helldivers 2 world space) ---- */

static int ChaosAnchorValid(const float anchor[3], const float target_player[3],
                            const float (*living_players)[3], int living_count)
{
    if (!anchor || !target_player || !living_players || living_count <= 0) return 0;
    for (int i = 0; i < 3; i++) {
        if (!isfinite(anchor[i]) || fabsf(anchor[i]) > 50000.0f) return 0;
        if (!isfinite(target_player[i]) || fabsf(target_player[i]) > 50000.0f) return 0;
    }
    if (anchor[0] == 0.0f && anchor[1] == 0.0f && anchor[2] == 0.0f) return 0;
    float dx = anchor[0] - target_player[0];
    float dy = anchor[1] - target_player[1];
    float dz = anchor[2] - target_player[2];
    float d2 = dx * dx + dy * dy;
    if (d2 < 38.0f * 38.0f || d2 > 62.0f * 62.0f || fabsf(dz) > 25.0f) return 0;
    for (int i = 0; i < living_count; i++) {
        float px = living_players[i][0], py = living_players[i][1], pz = living_players[i][2];
        if (!isfinite(px) || !isfinite(py) || !isfinite(pz)) continue;
        float pdx = anchor[0] - px;
        float pdy = anchor[1] - py;
        if (pdx * pdx + pdy * pdy < 28.0f * 28.0f) return 0;
    }
    return 1;
}

/* ---- Enemy entity classifier (sorted 64-bit resource hashes -> CHAOS_BOTS/SQUIDS/BUGS) ---- */

typedef struct ChaosEnemyEntry {
    uint64_t resource;
    uint8_t faction; /* CHAOS_BOTS=0, CHAOS_SQUIDS=1, CHAOS_BUGS=2 */
} ChaosEnemyEntry;

static const ChaosEnemyEntry kChaosEnemies[] = {
    {0x0002ba767df856f3ull, 0u}, {0x089833d2880d9e06ull, 0u}, {0x08e6ffc2474287bdull, 1u}, {0x09dfb04b2e578bc3ull, 0u},
    {0x09fe0be51a23396cull, 0u}, {0x0ab7b92b131c228cull, 2u}, {0x0adc9f9173ad8e1dull, 1u}, {0x10081acef6163ef6ull, 2u},
    {0x137988cea16458f7ull, 0u}, {0x16023e554fee830eull, 0u}, {0x1897bdd32105d2dcull, 0u}, {0x19e18b46ec55d94aull, 1u},
    {0x1a7fcdff98c664b0ull, 2u}, {0x1b5b9ac4f96b36e5ull, 0u}, {0x1e66ee1f6f7fd00eull, 0u}, {0x1f0a91729c0004e0ull, 2u},
    {0x20b9c7734daead65ull, 1u}, {0x215ce160a17be4cdull, 0u}, {0x257d805caa7e10c0ull, 0u}, {0x262351741c53ff0cull, 1u},
    {0x282eb766c1ffa6a1ull, 0u}, {0x2a12104f2853ae16ull, 0u}, {0x2cf3488c4845f8bdull, 0u}, {0x30cf04b2ec8c9bd4ull, 0u},
    {0x30f2dee2333f227aull, 0u}, {0x31bae74d2f064d8dull, 0u}, {0x32541fc4ec7c9cdcull, 2u}, {0x32cdeada234fb8dfull, 0u},
    {0x33b3e0ce8d179eecull, 0u}, {0x36aa99cce5e60146ull, 2u}, {0x3aff5fd7d5450b99ull, 2u}, {0x3d0e03e2d574e1caull, 2u},
    {0x3e0537d606438feaull, 0u}, {0x4019623142351cb6ull, 0u}, {0x426b3df007be9f18ull, 0u}, {0x44458a2c52b002fbull, 1u},
    {0x453fe22c634eb30full, 1u}, {0x49674794a165eda8ull, 0u}, {0x4e342031a9d5c4f9ull, 1u}, {0x4e97fb073bdc7a4bull, 2u},
    {0x51eea86bf6997e4eull, 2u}, {0x52018deb9ab6827eull, 0u}, {0x53d8919d7b8abd67ull, 0u}, {0x54e107dacf6929cbull, 0u},
    {0x57eed0eac346cd9dull, 0u}, {0x58b2b86c11369241ull, 0u}, {0x5ca832447445c0baull, 2u}, {0x5d142c3a73ebc634ull, 0u},
    {0x6021e22338333d88ull, 2u}, {0x604a794ec45bb820ull, 1u}, {0x611ba777783b08a2ull, 0u}, {0x63df3d07b7424588ull, 0u},
    {0x64090088502435ddull, 2u}, {0x64ba5f030b114ec1ull, 0u}, {0x672f7da17f3ba34aull, 2u}, {0x67dc32dca4f02d33ull, 1u},
    {0x6b202392f4ab605eull, 2u}, {0x6dab2eadf5d8b692ull, 0u}, {0x728421351d440ebcull, 2u}, {0x72a83e49ced6db3dull, 2u},
    {0x746a7f3beda32699ull, 0u}, {0x74e2285c01da4f71ull, 1u}, {0x78e1497571012c47ull, 1u}, {0x7acb4bd753b71de0ull, 1u},
    {0x7b48cacdbacb3881ull, 0u}, {0x7ece5304f868f6b3ull, 0u}, {0x82a87ad8d595b2baull, 0u}, {0x843d18d4b5512b63ull, 0u},
    {0x856e9710e45e760full, 0u}, {0x883401af2a98a5f6ull, 2u}, {0x8fb47dd28b398cf7ull, 0u}, {0x8fc2784308e9ba99ull, 0u},
    {0x8ff0a839830a7692ull, 2u}, {0x905809a4c28d8a45ull, 1u}, {0x9076eeed17fcee35ull, 0u}, {0x91ebd77931110afcull, 0u},
    {0x960b48a421a3faaaull, 2u}, {0x96110f9d6b010e02ull, 1u}, {0x9647b00cc3a9d36full, 0u}, {0x965eae5a51acdd4aull, 1u},
    {0x96ba14c9ebb49ce1ull, 0u}, {0x9926876b2375a1bbull, 0u}, {0x9a8a3aae287b230cull, 2u}, {0x9d8827fed763650eull, 0u},
    {0x9e2e17f2ccccafddull, 2u}, {0x9f57782f00e6ed20ull, 0u}, {0xa05bd1ec67b3ac4cull, 2u}, {0xa1f37bf2a40fbde4ull, 2u},
    {0xa35207c6f2150806ull, 2u}, {0xa381a11c07d3eb94ull, 2u}, {0xa4552f97033392f4ull, 0u}, {0xa6a68d8af177f3a1ull, 0u},
    {0xa71aafd82c6ebc92ull, 0u}, {0xaab438596f5e8fd9ull, 2u}, {0xabdb2e2a0479d8caull, 0u}, {0xac60e78435098c9dull, 1u},
    {0xae57fcdb49f74e98ull, 0u}, {0xae63e525853d7044ull, 0u}, {0xaeaef7a1851e6c9dull, 0u}, {0xaf0f9b3a163787a5ull, 0u},
    {0xaf8529d541954e88ull, 1u}, {0xb056f8fc74aba02dull, 0u}, {0xb2a6fa1e4284c7e6ull, 2u}, {0xb4ed319b39f5457bull, 0u},
    {0xb5dbc0c240c921adull, 0u}, {0xb8580cac32214fc0ull, 0u}, {0xb92435fbf60f0748ull, 0u}, {0xb940c889d77f8219ull, 0u},
    {0xbc242702fb46b7e7ull, 0u}, {0xbe39e313a1e46bb9ull, 2u}, {0xbe743b2faa3a6e26ull, 0u}, {0xc0df2a134bd15ad5ull, 1u},
    {0xc2b9eb21b06f8378ull, 0u}, {0xc626d2bb495a202dull, 0u}, {0xc6449ffd9ea3779cull, 0u}, {0xc9bcccb0a54a82a4ull, 0u},
    {0xcbb1ba3366009c3aull, 0u}, {0xcc188f0c80505c6cull, 1u}, {0xcc7022fdd172089bull, 2u}, {0xccae5264acd591b7ull, 2u},
    {0xcd28a27a79be53d5ull, 0u}, {0xd1e990baf22d5a52ull, 2u}, {0xd37e8d120d2836e3ull, 0u}, {0xd465d9c7f77a07cbull, 2u},
    {0xd522fd4748d443a5ull, 2u}, {0xd5792f6856b06ba4ull, 0u}, {0xd63fcbff0851b7afull, 1u}, {0xd8cbc4a807a6d035ull, 0u},
    {0xd9511e9f6bd62e3full, 2u}, {0xda40bb347c7447f2ull, 1u}, {0xdac7d53fe0749f5full, 1u}, {0xdb90077e76faa025ull, 0u},
    {0xdb964631be1cf501ull, 2u}, {0xdcf8e74212fbee3bull, 2u}, {0xdfbacbd977a948dcull, 2u}, {0xe0353177f1329573ull, 0u},
    {0xe2e62fdd39407fe3ull, 0u}, {0xe44ec9f9b3fe1d2aull, 0u}, {0xe683d2ca5618d74aull, 0u}, {0xe8f19a0aa958e46dull, 1u},
    {0xeacee39fa017b495ull, 2u}, {0xef04cb84d097a497ull, 2u}, {0xef570293245a17c2ull, 0u}, {0xf0b26fa9258128d3ull, 2u},
    {0xf1610ac48cdc5240ull, 1u}, {0xf196069e7e14c497ull, 1u}, {0xf22d027b37bef107ull, 1u}, {0xf540ca9d9d4a422eull, 2u},
    {0xf66d0bad8693779aull, 0u}, {0xf79cd8bb654397dfull, 2u}, {0xf8131632aa867107ull, 0u}, {0xf8b5a81a86d5d4ebull, 0u},
    {0xf96257fcae258caaull, 0u}, {0xfb9937035d652c43ull, 0u}, {0xfc8dec78be8ab47dull, 0u}, {0xfd5247653c897803ull, 2u},
    {0xfeb484ee972469f7ull, 0u}, {0xff5cc825b9571052ull, 0u},
};

static int ChaosIsTransportResource(uint64_t resource)
{
    return resource == 0xDB90077E76FAA025ull || /* cyborg_dropship */
           resource == 0xD84FFC32A6E640A8ull || /* Automaton Dropship */
           resource == 0x74E2285C01DA4F71ull || /* illuminate_dropship */
           resource == 0x2AD2E055DAD21F6Eull;   /* illuminate_invasionship */
}

static int ChaosEnemyFamily(uint64_t resource)
{
    size_t lo = 0, hi = sizeof kChaosEnemies / sizeof kChaosEnemies[0];
    while (lo < hi) {
        size_t mid = lo + (hi - lo) / 2;
        if (kChaosEnemies[mid].resource < resource) lo = mid + 1;
        else hi = mid;
    }
    if (lo < sizeof kChaosEnemies / sizeof kChaosEnemies[0] && kChaosEnemies[lo].resource == resource)
        return (int)kChaosEnemies[lo].faction;
    return -1;
}

/* ---- Bounded Chaos population & reservation tracker ---- */

#define CHAOS_MAX_TRACKED 96

typedef struct ChaosTrackedUnit {
    uint32_t entity;
    uint8_t faction;
    uint8_t active;
} ChaosTrackedUnit;

typedef struct ChaosTracker {
    ChaosTrackedUnit units[CHAOS_MAX_TRACKED];
    int live[CHAOS_FACTIONS];
    int total_live;
    int reserved[CHAOS_FACTIONS];
    int mission_spawned;
} ChaosTracker;

typedef int (*ChaosAliveFn)(uint32_t entity, void* ctx);

static void ChaosTrackerReset(ChaosTracker* t)
{
    memset(t, 0, sizeof *t);
}

static void ChaosTrackerPrune(ChaosTracker* t, ChaosAliveFn alive_fn, void* ctx)
{
    memset(t->live, 0, sizeof t->live);
    t->total_live = 0;
    for (int i = 0; i < CHAOS_MAX_TRACKED; i++) {
        if (!t->units[i].active) continue;
        if (alive_fn && !alive_fn(t->units[i].entity, ctx)) {
            t->units[i].active = 0;
            continue;
        }
        int f = (int)t->units[i].faction;
        if (f >= 0 && f < CHAOS_FACTIONS) {
            ++t->live[f];
            ++t->total_live;
        }
    }
}

static int ChaosTrackerHasEntity(const ChaosTracker* t, uint32_t entity)
{
    for (int i = 0; i < CHAOS_MAX_TRACKED; i++)
        if (t->units[i].active && t->units[i].entity == entity) return 1;
    return 0;
}

static int ChaosTrackerRecordSpawn(ChaosTracker* t, int faction, uint32_t entity, int consume_reservation)
{
    if (faction < 0 || faction >= CHAOS_FACTIONS || entity == 0 || entity == 0xFFFFFFFFu) return 0;
    if (ChaosTrackerHasEntity(t, entity)) return 0;
    for (int i = 0; i < CHAOS_MAX_TRACKED; i++) {
        if (!t->units[i].active) {
            t->units[i].entity = entity;
            t->units[i].faction = (uint8_t)faction;
            t->units[i].active = 1u;
            ++t->live[faction];
            ++t->total_live;
            if (consume_reservation && t->reserved[faction] > 0) --t->reserved[faction];
            else ++t->mission_spawned;
            return 1;
        }
    }
    return 0;
}

static void ChaosTrackerReserveWave(ChaosTracker* t, int faction, int count)
{
    if (faction < 0 || faction >= CHAOS_FACTIONS || count <= 0) return;
    t->reserved[faction] += count;
    t->mission_spawned += count;
}

static void ChaosTrackerReleaseUnusedReservations(ChaosTracker* t, int faction)
{
    if (faction < 0 || faction >= CHAOS_FACTIONS || t->reserved[faction] <= 0) return;
    t->mission_spawned -= t->reserved[faction];
    if (t->mission_spawned < 0) t->mission_spawned = 0;
    t->reserved[faction] = 0;
}

static int ChaosEffectiveLiveTotal(const ChaosTracker* t)
{
    return t->total_live + t->reserved[0] + t->reserved[1] + t->reserved[2];
}

static int ChaosEffectiveLiveFaction(const ChaosTracker* t, int faction)
{
    if (faction < 0 || faction >= CHAOS_FACTIONS) return 0;
    return t->live[faction] + t->reserved[faction];
}

/* ---- Mutual hostility ignore_mask narrowing ---- */

static uint32_t ChaosNarrowIgnoreMask(uint32_t cur_ignore, uint32_t own_mask,
                                      uint32_t all_enemy_masks, uint32_t player_mask)
{
    uint32_t foreign_enemy_masks = all_enemy_masks & ~own_mask & ~player_mask;
    return cur_ignore & ~foreign_enemy_masks;
}

/* ---- SDK 16 Co-op Sync & Peer Preload Readiness Packets ------------------------------------------------------- */

#define CHAOS_SHARE_KEY_STATE 0x4348414F53303330ULL /* "CHAOS030" */
#define CHAOS_SYNC_MAGIC      0x334F4843u           /* "CHO3" */
#define CHAOS_SYNC_VERSION    3u

#define CHAOS_MAX_PEERS            4
#define CHAOS_LATE_JOIN_HELLO_S    10.0
#define CHAOS_PEER_PREPARE_GRACE_S 120.0
#define CHAOS_PEER_PRELOAD_GRACE_S 30.0

enum {
    CHAOS_MSG_PEER_STATUS = 1u
};

typedef struct ChaosSharedState {
    uint32_t magic;
    uint16_t version;
    uint8_t native_faction; /* 0..2 or 0xFF */
    uint8_t landed;         /* 0 or 1 */
    uint16_t mission_spawned;
    uint16_t attempts;
    uint32_t epoch;
    int32_t cooldown_rem_ms;
    uint32_t last_wave_seq;
    uint8_t active_wave;
    uint8_t active_faction;
    uint8_t reserved_pad[6];
} ChaosSharedState;

typedef struct ChaosPeerMsg {
    uint32_t magic;
    uint16_t version;
    uint8_t kind;          /* CHAOS_MSG_PEER_STATUS */
    uint8_t resident_mask; /* bit f set when foreign faction f is resident */
    uint8_t in_mission;    /* 1 in HF_STATE_MISSION, 0 in PREPARE/other */
    uint8_t reserved[3];
    uint32_t epoch;
} ChaosPeerMsg;

#if defined(__STDC_VERSION__) && __STDC_VERSION__ >= 201112L
_Static_assert(sizeof(ChaosSharedState) == 32, "ChaosSharedState must fit HF_SHARE_VALUE_MAX (64)");
_Static_assert(sizeof(ChaosPeerMsg) == 16, "ChaosPeerMsg fixed wire layout");
#endif

static int ChaosSharedStateValid(const ChaosSharedState* s)
{
    return s && s->magic == CHAOS_SYNC_MAGIC && s->version == CHAOS_SYNC_VERSION;
}

static int ChaosPeerMsgValid(const ChaosPeerMsg* m)
{
    return m && m->magic == CHAOS_SYNC_MAGIC && m->version == CHAOS_SYNC_VERSION &&
           m->kind == CHAOS_MSG_PEER_STATUS;
}

static uint8_t ChaosRequiredForeignMask(int native_faction)
{
    uint8_t m = 0;
    for (int f = 0; f < CHAOS_FACTIONS; f++) {
        if (f != native_faction) m |= (uint8_t)(1u << f);
    }
    return m;
}

#endif
