/* SEAF Squad Pool: puts the custom squad compositions into the game's own SEAF
 * squad pool. It never edits planets, effects or the mission tag: on missions
 * that already carry the SEAF tag it appends roster entries to the mission
 * director, then equips the six troopers of each squad the game drops.
 * Shuttles of a player-called delivery belong to seaf_squads.cpp, not to it. */
#include "engine.h"
#include "equipment.h"
#include "maintenance.h"
#include "loadouts.h"
#include "assets.h"
#include "hunters.h"
#include "../options_menu.h"
#include "../heavy_gate_and_pacing.h"
#include "../tm_tick.h"
#include <cstddef>
#include <deque>
#include <mutex>

/* heavy_seaf_presence.cpp: 1 when the original SEAF Squad Stratagems mod is
 * installed and its four squad call-ins were detected. Its author requires that
 * mod for the custom compositions, so the pool only acts while it is loaded. */
extern "C" int heavy_seaf_support_loaded(void);
/* seaf_squads.cpp: 1 for a shuttle or trooper of a player-called delivery. */
extern "C" int heavy_seaf_squad_owned(uint32_t id);

namespace seaf {
constexpr uint32_t SEAF_TAG = 0xba586f51u;
constexpr uintptr_t DIRECTOR_RVA = 0x3326D10u;
constexpr uintptr_t ROSTER_RVA = 0x4FD78u, ROSTER_COUNT_RVA = 0x518ACu;
constexpr uintptr_t TAG_LIST_RVA = 0x518D4u, TAG_COUNT_RVA = 0x51914u;
constexpr uintptr_t SEAF_DEF_RVA = 0x32D4D10u;
constexpr uint64_t pelican_resource = 0x3EC790806D0B0540ull;
constexpr uint64_t cargo_resource = 0x75F7E96AF7DCD303ull;
constexpr unsigned ROLE_RIFLEMAN = 0xC229E29Cu, ROLE_SPECIALIST = 0xFA856D17u, ROLE_MEDIC = 0x8C724EA4u;
constexpr unsigned ENTRY_SIZE = 0xD8, MAX_ENTRIES = 32;

/* Composition rows, in the same order as the loadout rows of that squad. */
struct Composition { const char *key, *title; unsigned rows; unsigned role[4], troopers[4]; const Loadout* roster; };
static const Composition squads[3] = {
    {"stealth", "SEAF Stealth Squad", 2, {ROLE_RIFLEMAN, ROLE_MEDIC}, {5, 1}, stealth_loadouts},
    {"riot", "SEAF Riot Control Squad", 2, {ROLE_RIFLEMAN, ROLE_MEDIC}, {4, 2}, riot_loadouts},
    {"tank", "SEAF Tank Hunter Squad", 3, {ROLE_RIFLEMAN, ROLE_SPECIALIST, ROLE_MEDIC}, {4, 1, 1}, tank_loadouts},
};

static uint64_t soldier_resource[11];
static const char* const soldier_path[11] = {
    "content/fac_super_earth/cha_seaf/cha_battlefront_seaf",
    "content/fac_super_earth/cha_seaf/cha_battlefront_seaf_2",
    "content/fac_super_earth/cha_seaf/cha_battlefront_seaf_3",
    "content/fac_super_earth/cha_seaf/cha_battlefront_seaf_4",
    "content/fac_super_earth/cha_seaf/cha_battlefront_seaf_leader",
    "content/fac_super_earth/cha_seaf/cha_battlefront_seaf_leader_2",
    "content/fac_super_earth/cha_seaf/cha_battlefront_seaf_medic",
    "content/fac_super_earth/cha_seaf/cha_battlefront_seaf_medic_2",
    "content/fac_super_earth/cha_seaf/cha_battlefront_seaf_medic_3",
    "content/fac_super_earth/cha_seaf/cha_battlefront_seaf_specialist",
    "content/fac_super_earth/cha_seaf/cha_battlefront_seaf_specialist_2",
};
/* 0..3 riflemen, 4..5 leaders, 6..8 medics, 9..10 specialists */
static int soldier_index(uint64_t resource) {
    for (int n = 0; n < 11; n++) if (resource && resource == soldier_resource[n]) return n;
    return -1;
}
static unsigned role_of_index(int n) {
    if (n < 0) return 0;
    if (n < 4) return ROLE_RIFLEMAN;
    if (n < 6) return 0;              /* leader: only the native patrol has one */
    if (n < 9) return ROLE_MEDIC;
    return ROLE_SPECIALIST;
}
static const char* name_of_index(int n) {
    static const char* const short_name[11] = {"rifleman", "rifleman_2", "rifleman_3", "rifleman_4",
        "leader", "leader_2", "medic", "medic_2", "medic_3", "specialist", "specialist_2"};
    return n >= 0 && n < 11 ? short_name[n] : "?";
}

/* A weapon constructed before its packages are resident renders as the game's
 * pink missing-asset placeholder (and is later replaced by the native gun). */
static bool assets_ready, assets_failed;
static float assets_elapsed, assets_wait;
static void AssetsStep(float dt) {
    if (assets_ready || assets_failed || !heavy_seaf_support_loaded()) return;
    assets_elapsed += dt;
    assets_wait -= dt;
    if (assets_wait > 0) return;
    assets_wait = .5f;
    try {
        bool resident = true;
        for (uint64_t package : asset_packages) {
            int status = load_squad_package(package);
            if (status == 0 || (status != HF_PACKAGE_RESIDENT && assets_elapsed > 90))
                throw std::runtime_error(status == 0 ? "a weapon package was refused"
                                                     : "the weapon packages did not load within 90 s");
            resident &= status == HF_PACKAGE_RESIDENT;
        }
        if (resident) {
            assets_ready = true;
            api->log(mod, "POOL_PACKAGES: %u weapon packages resident",
                     (unsigned)(sizeof asset_packages / sizeof asset_packages[0]));
        }
    } catch (const std::exception& error) {
        assets_failed = true;
        api->log(mod, "POOL_PACKAGES_STOPPED: %s; drops are left as the game made them", error.what());
    }
}

/* ---------------------------------------------------------------- spawns */
struct SpawnNote { uint32_t entity; uint64_t resource; int index; ULONGLONG at; };
static std::mutex spawn_lock;
static std::deque<SpawnNote> inbox;     /* filled by the spawn callback, emptied by the update */
static std::deque<SpawnNote> history;   /* soldier spawns kept for the fallback grouping */
static volatile LONG mission_active;
static unsigned spawn_logged;

static void OnSpawn(HfMod*, const HfEventInfo* event, void*) {
    if (!mission_active || event == NULL || event->size < sizeof(HfSpawnEvent) || event->kind != HF_ON_SPAWN) return;
    const HfSpawnEvent* spawn = (const HfSpawnEvent*)event;
    if (spawn->resource != pelican_resource && spawn->resource != cargo_resource &&
        soldier_index(spawn->resource) < 0) return;
    std::lock_guard<std::mutex> guard(spawn_lock);
    if (inbox.size() >= 512) inbox.pop_front();
    inbox.push_back({spawn->entity, spawn->resource, soldier_index(spawn->resource), GetTickCount64()});
}

/* ---------------------------------------------------------------- roster */
struct Director { uintptr_t ai; uint32_t count, tags; bool tag; };
static bool read_director(Director& d) {
    uintptr_t ai = 0;
    if (api->read(game + DIRECTOR_RVA, &ai, 8) || ai < 0x10000 || ai >= 0x800000000000ull) return false;
    uint32_t count = 0, tags = 0, list[17] = {0};
    if (api->read(ai + ROSTER_COUNT_RVA, &count, 4) || count > MAX_ENTRIES) return false;
    if (api->read(ai + TAG_COUNT_RVA, &tags, 4) || tags > 16) return false;
    if (api->read(ai + TAG_LIST_RVA, list, sizeof list)) return false;
    bool have = false;
    for (uint32_t n = 0; n < tags; n++) if (list[n] == SEAF_TAG) have = true;
    d = {ai, count, tags, have};
    return true;
}
static bool read_entry(uintptr_t ai, unsigned n, unsigned char* entry) {
    return !api->read(ai + ROSTER_RVA + (uintptr_t)n * ENTRY_SIZE, entry, ENTRY_SIZE);
}
static bool entry_rows(const unsigned char* entry, unsigned* rows) {
    uint32_t count = 0;
    memcpy(&count, entry + 0x60, 4);
    if (!count || count > 8) return false;
    *rows = count;
    return true;
}
static void entry_roles(const unsigned char* entry, unsigned rows, unsigned role[6]) {
    unsigned at = 0;
    for (unsigned r = 0; r < rows && at < 6; r++) {
        uint32_t id = 0, troopers = 0;
        memcpy(&id, entry + r * 12, 4);
        memcpy(&troopers, entry + r * 12 + 4, 4);
        for (uint32_t n = 0; n < troopers && at < 6; n++) role[at++] = id;
    }
    for (; at < 6; at++) role[at] = 0;
}
/* 0 stealth, 1 riot, 2 tank, 3 native patrol, -1 unknown */
static int identify(const unsigned role[6]) {
    unsigned r = 0, s = 0, m = 0, other = 0;
    for (unsigned n = 0; n < 6; n++) {
        if (role[n] == ROLE_RIFLEMAN) r++;
        else if (role[n] == ROLE_SPECIALIST) s++;
        else if (role[n] == ROLE_MEDIC) m++;
        else other++;
    }
    if (r == 5 && m == 1 && !s && !other) return 0;
    if (r == 4 && m == 2 && !s && !other) return 1;
    if (r == 4 && s == 1 && m == 1 && !other) return 2;
    if (r == 2 && s == 2 && m == 1 && other == 1) return 3;
    return -1;
}
static bool definition_entry(unsigned char* entry) {
    unsigned char def[0x118];
    if (api->read(game + SEAF_DEF_RVA, def, sizeof def)) return false;
    uint32_t mark = 0;
    memcpy(&mark, def + 0x60, 4);
    if (mark != SEAF_TAG) return false;
    uint32_t rows = 0;
    for (unsigned r = 0; r < 8; r++) {
        uint32_t id = 0, troopers = 0;
        memcpy(&id, def + r * 12, 4);
        memcpy(&troopers, def + r * 12 + 4, 4);
        if (!id) continue;
        if (!troopers || troopers > 12) return false;
        rows++;
    }
    if (!rows) return false;
    memset(entry, 0, ENTRY_SIZE);
    memcpy(entry, def, 0x60);
    memcpy(entry + 0x60, &rows, 4);
    uintptr_t at = game + SEAF_DEF_RVA;
    memcpy(entry + 0xa8, &at, 8);
    memcpy(entry + 0xb0, def + 0x100, 8);
    memcpy(entry + 0xb8, def + 0xfc, 4);
    float one = 1.0f;
    memcpy(entry + 0xc0, &one, 4);
    memcpy(entry + 0xc4, def + 0x108, 12);
    return true;
}
static bool build_entry(const unsigned char* base, const Composition& squad, unsigned char* entry) {
    memcpy(entry, base, ENTRY_SIZE);
    memset(entry, 0, 0x60);
    unsigned total = 0;
    for (unsigned r = 0; r < squad.rows; r++) {
        uint32_t id = squad.role[r], troopers = squad.troopers[r];
        memcpy(entry + r * 12, &id, 4);
        memcpy(entry + r * 12 + 4, &troopers, 4);
        total += troopers;
    }
    if (total != 6) return false;
    uint32_t rows = squad.rows;
    memcpy(entry + 0x60, &rows, 4);
    float one = 1.0f;
    memcpy(entry + 0xc0, &one, 4);
    return true;
}
static bool write_entry(uintptr_t ai, unsigned n, const unsigned char* entry) {
    uintptr_t at = ai + ROSTER_RVA + (uintptr_t)n * ENTRY_SIZE;
    unsigned char back[ENTRY_SIZE];
    if (api->write(at, entry, ENTRY_SIZE)) return false;
    return !api->read(at, back, sizeof back) && !memcmp(back, entry, ENTRY_SIZE);
}

struct PoolState { bool decided, refused, done, tag_seen; unsigned tries; unsigned long long last; unsigned before; };
static PoolState pool;
static unsigned long long mission_at;

static void PoolStep(bool mission_running) {
    if (!mission_running || !mission_at || !host()) return;
    unsigned long long now = GetTickCount64();
    if (now - mission_at < 1500) return;
    if (pool.decided && (pool.refused || pool.done)) return;
    if (!heavy_seaf_support_loaded()) {
        /* The compositions are used with that mod's permission, on condition
         * that it is installed and enabled. */
        pool.decided = true; pool.refused = true;
        api->log(mod, "POOL: SEAF Squad Stratagems is not loaded; the SEAF roster keeps the game's own composition");
        return;
    }
    if (pool.last && now - pool.last < 3000) return;
    pool.last = now;
    if (pool.tries++ > 10) { pool.decided = true; pool.refused = true; return; }
    Director d{};
    if (!read_director(d)) {
        if (pool.tries == 1) api->log(mod, "POOL: the mission director is not readable yet");
        return;
    }
    if (!d.tag) {
        if (pool.tries > 8) {
            pool.decided = true; pool.refused = true;
            api->log(mod, "POOL: this mission has no SEAF tag after %u checks; the pool is left alone", pool.tries);
        }
        return;
    }
    if (!pool.tag_seen) {
        pool.tag_seen = true;
        api->log(mod, "POOL: director %llx, SEAF tag present, roster %u", (unsigned long long)d.ai, d.count);
    }
    /* The game stamps the tag and its own entry at mission start; appending
     * before it has done so would be undone by its write. Give it ~18 s. */
    if (!d.count && pool.tries <= 6) {
        if (pool.tries == 6) api->log(mod, "POOL: the SEAF tag is present but the roster is still empty; giving the game more time");
        return;
    }
    /* which of our compositions are already in the roster? */
    bool have[3] = {false, false, false};
    unsigned char first[ENTRY_SIZE];
    bool have_first = d.count > 0 && read_entry(d.ai, 0, first);
    for (unsigned n = 0; n < d.count; n++) {
        unsigned char entry[ENTRY_SIZE];
        unsigned rows = 0, role[6];
        if (!read_entry(d.ai, n, entry) || !entry_rows(entry, &rows)) {
            pool.decided = true; pool.refused = true;
            api->log(mod, "POOL: roster entry %u does not look like a SEAF entry; the pool is left alone", n);
            return;
        }
        entry_roles(entry, rows, role);
        int kind = identify(role);
        if (kind >= 0 && kind < 3) have[kind] = true;
        if (n == 0) continue;
    }
    if (have[0] && have[1] && have[2]) {
        pool.decided = true; pool.done = true;
        api->log(mod, "POOL: all three compositions are already in the roster (%u entries)", d.count);
        return;
    }
    /* The template is the game's own entry (which carries every field the
     * spawner reads); with an empty roster it is built from the definition. */
    unsigned char base[ENTRY_SIZE];
    if (have_first) memcpy(base, first, ENTRY_SIZE);
    else if (!definition_entry(base)) {
        pool.decided = true; pool.refused = true;
        api->log(mod, "POOL: the game's SEAF squad definition did not check out; the roster is left alone");
        return;
    }
    unsigned want = 0;
    for (unsigned n = 0; n < 3; n++) if (!have[n]) want++;
    if (d.count + want > MAX_ENTRIES) {
        pool.decided = true; pool.refused = true;
        api->log(mod, "POOL: the roster is full (%u entries); nothing added", d.count);
        return;
    }
    unsigned next = d.count;
    unsigned added = 0;
    /* With an empty roster the native patrol entry joins as well. */
    if (!d.count) {
        if (!write_entry(d.ai, next, base)) { pool.refused = true; api->log(mod, "POOL: could not write the patrol entry"); return; }
        next++; added++;
    }
    for (unsigned n = 0; n < 3; n++) {
        if (have[n]) continue;
        unsigned char entry[ENTRY_SIZE];
        if (!build_entry(base, squads[n], entry) || !write_entry(d.ai, next, entry)) {
            pool.refused = true;
            api->log(mod, "POOL: could not write the %s entry", squads[n].key);
            return;
        }
        next++; added++;
    }
    if (api->write(d.ai + ROSTER_COUNT_RVA, &next, 4)) { pool.refused = true; api->log(mod, "POOL: could not publish the roster count"); return; }
    pool.before = d.count;
    pool.decided = true; pool.done = true;
    api->log(mod, "POOL: roster %u -> %u entries (%u added): %s%s%s%s", d.count, next, added,
        d.count ? "patrol kept" : "patrol", have[0] ? ", stealth was there" : ", stealth",
        have[1] ? ", riot was there" : ", riot", have[2] ? ", tank was there" : ", tank");
}

/* --------------------------------------------------------------- census */
static bool alive(uint32_t id) {
    auto health = instance(53634696, 4144, 4168, 4184, 440, id);
    return read<int32_t>(health.row + 20) > 0;
}

/* Every SEAF trooper seen spawning is counted until it dies, so the wave timer
 * can hold a wave on a field the game is already carrying. Fresh spawns get a
 * grace period because a just-created soldier is not readable yet. */
struct CensusRow { uint32_t id; ULONGLONG at; };
static std::vector<CensusRow> census;
static int census_live;
static ULONGLONG census_at;

static void census_add(uint32_t id) {
    if (!id) return;
    for (const auto& row : census) if (row.id == id) return;
    if (census.size() >= 192) census.erase(census.begin());
    census.push_back({id, GetTickCount64()});
    census_at = 0;
}

static int census_update(ULONGLONG now) {
    if (census_at && now - census_at < 2000) return census_live;
    census_at = now;
    int living = 0;
    for (size_t n = 0; n < census.size();) {
        bool here = now - census[n].at <= 5000;
        if (!here) {
            try { here = alive(census[n].id); } catch (...) {}
        }
        if (here) { living++; n++; } else census.erase(census.begin() + n);
    }
    census_live = living;
    return living;
}

extern "C" int heavy_seaf_live_troops(void) {
    if (!mission_active) return 0;
    return census_update(GetTickCount64());
}

static int passenger_group(uint32_t shuttle, uint32_t ids[6], unsigned* extra) {
    try {
        auto group = instance(0x3326CC0, 0x20, 0x38, 0x48, 0x3D8, shuttle);
        uint32_t seen[16] = {0};
        unsigned n = 0;
        for (unsigned k = 0; k < 16; k++) {
            uint32_t id = read<uint32_t>(group.row + 0x280 + k * 4);
            if (!id || id == 0xffffffffu) continue;
            bool duplicate = false;
            for (unsigned j = 0; j < n; j++) if (seen[j] == id) duplicate = true;
            if (duplicate) continue;
            if (n < 16) seen[n++] = id;
        }
        *extra = n;
        if (n < 6) return 0;
        memcpy(ids, seen, 6 * 4);
        return n == 6 ? 1 : -1;
    } catch (...) {
        return -2;
    }
}
static bool group_roles(const uint32_t ids[6], unsigned role[6]) {
    for (unsigned n = 0; n < 6; n++) {
        try {
            Descriptor d = descriptor(ids[n]);
            int index = soldier_index(d.type);
            if (index < 0) return false;
            role[n] = role_of_index(index);
        } catch (...) {
            return false;
        }
    }
    return true;
}
static unsigned live = 0;
static const unsigned max_live = 96;
struct Tracked { Soldier soldier; unsigned squad; unsigned id; const Loadout* spec; unsigned reapplied; };
static std::vector<Tracked> tracked;

struct Pending {
    uint32_t shuttle; ULONGLONG at; bool said, fallback, grouped, said_assets, roles_matched; int kind;
    uint32_t ids[6]; unsigned role[6]; unsigned picked[6];
    unsigned next_slot; unsigned retries; ULONGLONG equip_at; ULONGLONG next_try_at;
};
static std::vector<Pending> pending;
static std::vector<uint32_t> claimed;

static bool match_squad_roles(Pending& p) {
    const Loadout* roster = squads[p.kind].roster;
    bool used[6] = {false};
    for (unsigned n = 0; n < 6; n++) p.picked[n] = 0xffffffffu;
    for (unsigned n = 0; n < 6; n++) {
        for (unsigned i = 0; i < 6; i++) {
            if (!used[i] && p.role[i] == roster[n].role) {
                p.picked[n] = i;
                used[i] = true;
                break;
            }
        }
        if (p.picked[n] == 0xffffffffu) return false;
    }
    p.roles_matched = true;
    p.next_slot = 0;
    return true;
}

/* Equips at most ONE passenger soldier in a single game tick (consuming the shared
 * 1-unit/tick budget) so a 6-trooper squad spreads cleanly across 6 frames. */
static int equip_one_passenger(Pending& p, unsigned n) {
    const Loadout& spec = squads[p.kind].roster[n];
    uint32_t id = p.ids[p.picked[n]];
    Descriptor d = descriptor(id);
    if ((d.flags & 1) == 0) return -1;
    auto inv = inventory(id);
    if (empty(read<uint32_t>(inv.row))) return 0; /* inventory still initializing: back off */
    if (!alive(id)) return -1;
    descriptor(read<uint32_t>(inv.row));

    if (spec.role == ROLE_SPECIALIST) {
        require(descriptor(read<uint32_t>(inv.row)).type == spec.primary &&
                descriptor(read<uint32_t>(inv.row + 8)).type == spec.support,
                "native MG-43/EAT loadout changed");
    } else {
        if (descriptor(read<uint32_t>(inv.row)).type != spec.primary) replace(id, spec.primary);
        if (spec.backpack && (empty(read<uint32_t>(inv.row + 12)) || descriptor(read<uint32_t>(inv.row + 12)).type != spec.backpack))
            replace(id, spec.backpack, 6);
        inv = inventory(id);
        uint32_t support = read<uint32_t>(inv.row + 8);
        if (spec.support) {
            if (empty(support) || descriptor(support).type != spec.support) replace(id, spec.support, 3);
        } else remove_support(id);
    }
    if (spec.grenade) grenades(id, spec.grenade, spec.count);
    else if (p.kind == 0) grenades(id, read<uint64_t>(inventory(id).row + 40), 0);

    inv = inventory(id);
    Soldier t{};
    t.identity = descriptor(id);
    t.primary = read<uint32_t>(inv.row);
    t.primary_network = descriptor(t.primary).network;
    t.weapon = spec.primary;
    t.support = read<uint32_t>(inv.row + 8);
    t.backpack = read<uint32_t>(inv.row + 12);
    if (!empty(t.support)) t.support_network = descriptor(t.support).network;
    if (!empty(t.backpack)) t.backpack_network = descriptor(t.backpack).network;
    t.squad = (unsigned)p.kind;
    if (t.weapon == recoilless) buffer_rr(t, true);
    if (live < max_live) { tracked.push_back({t, (unsigned)p.kind, id, &spec, 0}); live++; }
    return 1;
}

static bool claimed_id(uint32_t id) {
    for (auto c : claimed) if (c == id) return true;
    return false;
}
static void claim(const uint32_t ids[6]) { for (unsigned n = 0; n < 6; n++) claimed.push_back(ids[n]); }

static bool reconcile(Tracked& tr) {
    auto inv = inventory(tr.soldier.identity.id);
    uint32_t primary = read<uint32_t>(inv.row);
    if (primary == tr.soldier.primary) return true;
    Descriptor now = descriptor(primary);
    if (now.type == tr.soldier.weapon) {
        tr.soldier.primary = primary;
        tr.soldier.primary_network = now.network;
        tr.soldier.previous = ammo(primary);
        tr.soldier.observed = true;
        return true;
    }
    if (tr.spec && tr.reapplied < 2 && HeavyEquipConsumeOne()) {
        unsigned bc = HeavyBreadcrumbEnter(HEAVY_BC_POOL_EQUIP, 0u, tr.soldier.identity.id, 99u);
        try {
            replace(tr.soldier.identity.id, tr.spec->primary);
            auto restored = read<uint32_t>(inventory(tr.soldier.identity.id).row);
            if (descriptor(restored).type == tr.soldier.weapon) {
                tr.soldier.primary = restored;
                tr.soldier.primary_network = descriptor(restored).network;
                tr.soldier.previous = ammo(restored);
                tr.soldier.observed = true;
                tr.reapplied++;
                HeavyBreadcrumbLeave(bc, 1);
                api->log(mod, "POOL_REAPPLIED: %u: primary restored to %016llx", tr.soldier.identity.id,
                         (unsigned long long)tr.soldier.weapon);
                return true;
            }
            HeavyBreadcrumbLeave(bc, -1);
        } catch (...) {
            HeavyBreadcrumbLeave(bc, -1);
        }
    }
    return false;
}

static bool fallback_group(const Pending& p, uint32_t ids[6], unsigned role[6]) {
    /* Six SEAF troopers spawned around the shuttle: used when the delivery's
     * passenger table is not there for this call-in. */
    std::vector<SpawnNote> candidates;
    {
        std::lock_guard<std::mutex> guard(spawn_lock);
        for (auto& note : history)
            if (note.index >= 0 && !claimed_id(note.entity) && !heavy_seaf_squad_owned(note.entity) &&
                note.at + 1500 >= p.at && note.at <= p.at + 4000)
                candidates.push_back(note);
    }
    if (candidates.size() != 6) return false;
    for (unsigned n = 0; n < 6; n++) { ids[n] = candidates[n].entity; role[n] = role_of_index(candidates[n].index); }
    return true;
}

static void ProcessShuttle(Pending& p) {
    if (heavy_seaf_squad_owned(p.shuttle)) { p.at = 0; return; }  /* player-called delivery */
    if (!heavy_seaf_support_loaded()) { p.at = 0; return; }       /* compositions require that mod */
    ULONGLONG now = GetTickCount64();
    if (!p.grouped) {
        uint32_t ids[6] = {0};
        unsigned extra = 0;
        int found = passenger_group(p.shuttle, ids, &extra);
        unsigned role[6] = {0};
        bool have_roles = found == 1 && group_roles(ids, role);
        if (!have_roles) {
            if (now - p.at < 2500) return;                      /* the drop is still being prepared */
            if (!p.fallback) {
                p.fallback = true;
                if (fallback_group(p, ids, role)) have_roles = true;
            }
            if (!have_roles) {
                if (!p.said) {
                    p.said = true;
                    api->log(mod, "SQUAD: shuttle %u: no six-passenger group (%d, count %u); left as the game made it",
                             p.shuttle, found, extra);
                }
                if (now - p.at < 12000 && found != -1) return;
                p.at = 0;
                return;
            }
        }
        int kind = identify(role);
        if (kind == 3) {
            api->log(mod, "SQUAD: shuttle %u: native patrol composition; left as the game made it", p.shuttle);
            claim(ids);
            p.at = 0;
            return;
        }
        if (kind < 0) {
            api->log(mod, "SQUAD: shuttle %u: unknown composition (%u %u %u %u %u %u); left as the game made it",
                     p.shuttle, role[0], role[1], role[2], role[3], role[4], role[5]);
            claim(ids);
            p.at = 0;
            return;
        }
        p.grouped = true;
        p.kind = kind;
        memcpy(p.ids, ids, sizeof p.ids);
        memcpy(p.role, role, sizeof p.role);
    }
    /* Constructing a weapon before its packages are resident gives the pink
     * placeholder and a later native replacement, so hold the drop until they are in. */
    if (!assets_ready) {
        if (!assets_failed && now - p.at < 120000) return;
        if (!p.said_assets) {
            p.said_assets = true;
            api->log(mod, "SQUAD: shuttle %u: weapon packages are not resident; left as the game made it", p.shuttle);
        }
        claim(p.ids);
        p.at = 0;
        return;
    }
    if (!p.equip_at) p.equip_at = now;
    if (!p.roles_matched) {
        if (!match_squad_roles(p)) {
            api->log(mod, "SQUAD_STOPPED: %s; shuttle %u: loadout row does not match composition",
                     squads[p.kind].title, p.shuttle);
            claim(p.ids);
            p.at = 0;
            return;
        }
    }
    if (now < p.next_try_at) return;
    if (p.next_slot < 6u) {
        if (!HeavyEquipConsumeOne()) return; /* Cap strictly to <= 1 unit constructed/equipped per tick! */
        uint32_t target_id = p.ids[p.picked[p.next_slot]];
        unsigned bc = HeavyBreadcrumbEnter(HEAVY_BC_POOL_EQUIP, p.shuttle, target_id, p.next_slot);
        try {
            int rc = equip_one_passenger(p, p.next_slot);
            double ms = HeavyBreadcrumbLeave(bc, rc > 0 ? 1 : (rc == 0 ? 0 : -1));
            if (ms >= 50.0) {
                api->log(mod, "SQUAD_EQUIP_SLOW: shuttle %u slot %u entity %u took %.1f ms",
                         p.shuttle, p.next_slot, target_id, ms);
            }
            if (rc > 0) {
                p.next_slot++;
                p.retries = 0;
            } else if (rc == 0) {
                if (now - p.equip_at > HEAVY_EQUIP_MAX_WINDOW_MS || ++p.retries > HEAVY_EQUIP_MAX_RETRIES) {
                    api->log(mod, "SQUAD_STOPPED: %s; shuttle %u: passenger %u inventory did not initialize within budget",
                             squads[p.kind].title, p.shuttle, target_id);
                    claim(p.ids);
                    p.at = 0;
                    return;
                }
                p.next_try_at = now + HEAVY_EQUIP_RETRY_BACKOFF_MS;
                return;
            } else {
                api->log(mod, "SQUAD_STOPPED: %s; shuttle %u: passenger %u no longer valid/alive",
                         squads[p.kind].title, p.shuttle, target_id);
                claim(p.ids);
                p.at = 0;
                return;
            }
        } catch (const std::exception& error) {
            HeavyBreadcrumbLeave(bc, -1);
            if (now - p.equip_at > HEAVY_EQUIP_MAX_WINDOW_MS || ++p.retries > HEAVY_EQUIP_MAX_RETRIES) {
                api->log(mod, "SQUAD_STOPPED: %s; shuttle %u (slot %u): %s",
                         squads[p.kind].title, p.shuttle, p.next_slot, error.what());
                claim(p.ids);
                p.at = 0;
                return;
            }
            p.next_try_at = now + HEAVY_EQUIP_RETRY_BACKOFF_MS;
            return;
        }
    }
    if (p.next_slot >= 6u) {
        api->log(mod, "SQUAD_EQUIPPED: %s; shuttle %u; six passengers (1/tick paced)", squads[p.kind].title, p.shuttle);
        claim(p.ids);
        p.at = 0;
    }
}

/* --------------------------------------------------------------- mission */
static void ResetMission() {
    std::lock_guard<std::mutex> guard(spawn_lock);
    inbox.clear();
    history.clear();
    pending.clear();
    tracked.clear();
    claimed.clear();
    census.clear();
    census_live = 0;
    census_at = 0;
    live = 0;
    pool = PoolState{};
    mission_at = GetTickCount64();
    spawn_logged = 0;
    assets_ready = assets_failed = false;
    assets_elapsed = assets_wait = 0.f;
    hunters::reset();
}
static void DrainSpawns() {
    std::deque<SpawnNote> fresh;
    {
        std::lock_guard<std::mutex> guard(spawn_lock);
        fresh.swap(inbox);
    }
    for (auto& note : fresh) {
        if (note.index >= 0) {
            census_add(note.entity);
            std::lock_guard<std::mutex> guard(spawn_lock);
            if (history.size() >= 512) history.pop_front();
            history.push_back(note);
        }
        if (note.resource == pelican_resource) {
            if (heavy_seaf_squad_owned(note.entity)) continue;
            if (pending.size() < 24) {
                Pending entry{};
                entry.shuttle = note.entity;
                entry.at = note.at;
                pending.push_back(entry);
                if (spawn_logged < 200) { api->log(mod, "SPAWN: pelican %u", note.entity); spawn_logged++; }
            }
            continue;
        }
        if (spawn_logged < 200) {
            api->log(mod, "SPAWN: %s %u", note.index >= 0 ? name_of_index(note.index) : "cargo", note.entity);
            spawn_logged++;
        }
        if (spawn_logged == 200) api->log(mod, "SPAWN: further spawn lines this mission are skipped");
    }
}
static void Tick(HfMod*, float dt, void*) {
    auto state = api->game_state(mod);
    static unsigned previous = HF_STATE_NONE;
    static bool was_host = false;
    if (state != previous) {
        if (state == HF_STATE_MISSION) { ResetMission(); mission_active = host() ? 1 : 0; }
        else if (previous == HF_STATE_MISSION) { mission_active = 0; ResetMission(); was_host = false; }
        previous = state;
    }
    if (state != HF_STATE_MISSION) return;
    bool is_host = host();
    if (!is_host) {
        if (was_host || mission_active) { mission_active = 0; ResetMission(); was_host = false; }
        return;
    }
    if (!was_host) { ResetMission(); mission_active = 1; was_host = true; }
    try {
        hunters::frame(dt);
        DrainSpawns();
        PoolStep(true);
        AssetsStep(dt);
        for (auto& shuttle : pending) if (shuttle.at) ProcessShuttle(shuttle);
        for (size_t n = 0; n < pending.size();) {
            if (!pending[n].at) pending.erase(pending.begin() + n); else ++n;
        }
        for (size_t n = 0; n < tracked.size();) {
            try {
                auto& t = tracked[n];
                bool hunter = t.squad == 2 && OptionsTankHunters();  /* the Tank Hunter squad */
                if (t.soldier.weapon == 0x587878FB76F4B9B1ull) {     /* native MG-43 gunner */
                    if (hunter) hunters::maintain(t.soldier, dt);
                    n++;
                    continue;
                }
                if (!reconcile(t)) {
                    api->log(mod, "SQUAD_MAINTENANCE_STOPPED: %u: tracked primary changed", t.id);
                    hunters::forget(t.id);
                    tracked.erase(tracked.begin() + n);
                    if (live) live--;
                    continue;
                }
                maintain(t.soldier, dt);
                if (hunter) hunters::maintain(t.soldier, dt);
                ++n;
            } catch (const std::exception& error) {
                api->log(mod, "SQUAD_MAINTENANCE_STOPPED: %u: %s", tracked[n].id, error.what());
                hunters::forget(tracked[n].id);
                tracked.erase(tracked.begin() + n);
                if (live) live--;
            }
        }
    } catch (const std::exception& error) {
        api->log(mod, "POOL_STOPPED: %s", error.what());
    }
}

extern "C" int heavy_squad_pool_init(const HfApi* hf, HfMod* owner) {
    using namespace seaf;
    api = hf;
    mod = owner;
    try {
        require(hf && hf->version >= 9 && hf->size >= offsetof(HfApi, subscribe) + sizeof(hf->subscribe),
                "HellForge SDK 9 is required");
        require(hf->build_id() && !strcmp(hf->build_id(), "25480438"), "game layout has not been validated");
        game = hf->module_base("game.dll", NULL);
        require(game, "game module unavailable");
        prove();
        for (unsigned n = 0; n < 11; n++) {
            soldier_resource[n] = hf->resource_hash(soldier_path[n]);
            require(soldier_resource[n], "a SEAF soldier resource name was not resolved");
        }
        require(tm_tick_join(hf, owner, Tick, NULL, "SEAF Squad Pool") != 0, "shared game-thread update unavailable");
        require(hf->subscribe(owner, HF_ON_SPAWN, OnSpawn, NULL, 0) != 0, "spawn subscription failed");
        hf->log(owner, "SEAF Squad Pool ready: stealth, riot and tank join the SEAF roster while SEAF Squad Stratagems is loaded");
        return 0;
    } catch (const std::exception& error) {
        if (hf) hf->log(owner, "POOL_REGISTRATION_FAILED: %s", error.what());
        return 1;
    }
}
}
