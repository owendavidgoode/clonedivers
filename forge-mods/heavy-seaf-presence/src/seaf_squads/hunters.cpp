/* Tank Hunter squads hunt heavies and elites.
 *
 * The game's own target selection is steered the way the Smarter Guard Dogs & Sentries mod steers a dog: while a heavy
 * (armor 4 and up) is in one of the squad's soldiers' sight, every other enemy that soldier perceives has its faction
 * mask blanked in that soldier's perception record, so the AI no longer offers it, and the soldier's re-selection time
 * is brought forward. Every blanked mask is put back when the heavy goes, the soldier leaves, the mission ends or the
 * switch is turned off. Nothing in the game's code is changed.
 *
 * Layouts (game build 25480438, the mod's init checks it): the behaviours manager at game+53634880 (entity id map at
 * +64, records at +96, 504-byte records, the entity id at +104, the target at +24, the deadline at +152); the perception
 * manager at game+53634376 (entity id map at +48, records at +80, 5112-byte records; candidate groups at 784/792,
 * 2072/2080, 3360/3368 and singles at 4648, 4728, 4808; an 80-byte entry: id +0, sight byte +48, memory +64, score +68,
 * flags +72, faction mask +76); the entity registry at game+53636280 (map at +73808, rows at +73832); the clock at
 * game+53633864 (+24, microseconds). */
#include "hunters.h"
#include "heavy_types.h"
#include "../options_menu.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <unordered_map>
#include <vector>

namespace seaf { namespace hunters { namespace {

constexpr uintptr_t behaviours_rva = 53634880, perception_rva = 53634376, registry_rva = 53636280, clock_rva = 53633864;
constexpr unsigned behaviour_stride = 504, perception_stride = 5112;
constexpr size_t perception_bytes = 4888; /* the groups and the three singles, as the Guard Dog mod reads it */
constexpr float poll_seconds = .2f, heavy_hold = 1.5f, kick_gap = .5f;
constexpr uint32_t blank = 0x20202020; /* the faction mask is ANDed with the agent's filter: these bits match no faction */

template<class T> bool load(uintptr_t at, T& value)
{
    return at >= 65536 && at < 0x800000000000ull && api->read(at, &value, sizeof value) != 0;
}
bool load_ptr(uintptr_t at, uintptr_t& value)
{
    uintptr_t v{};
    if (!load(at, v) || v < 65536 || v >= 0x800000000000ull) return false;
    value = v;
    return true;
}
/* the game's id -> index maps: pointer at +0, capacity +8, empty +12, multiplier +16, 8-byte (key, value) entries */
int lookup(uintptr_t map, uint32_t key)
{
    uintptr_t entries;
    uint32_t count{}, empty{}, multiplier{};
    if (!load_ptr(map, entries) || !load(map + 8, count) || !load(map + 12, empty) || !load(map + 16, multiplier)) return -1;
    if (!count || count > 1048576 || (count & (count - 1)) || key == empty) return -1;
    uint32_t start = key * multiplier;
    for (uint32_t probe = 0; probe < count && probe < 4096; probe++) {
        uint32_t found{}, value{};
        uintptr_t at = entries + (uintptr_t)((start + probe) & (count - 1)) * 8;
        if (!load(at, found) || !load(at + 4, value)) return -1;
        if (found == key) return (int)value;
        if (found == empty) return -1;
    }
    return -1;
}
const HeavyType* heavy_of(uint64_t type)
{
    const HeavyType* lo = heavy_types;
    const HeavyType* hi = heavy_types + sizeof heavy_types / sizeof heavy_types[0];
    while (lo < hi) {
        const HeavyType* mid = lo + (hi - lo) / 2;
        if (mid->type < type) lo = mid + 1;
        else hi = mid;
    }
    return lo != heavy_types + sizeof heavy_types / sizeof heavy_types[0] && lo->type == type ? lo : nullptr;
}
/* the type hash in an entity's 24-byte row (its first 8 bytes) */
bool entity_type(uint32_t id, uint64_t& type)
{
    uintptr_t registry, rows;
    if (!load_ptr(game + registry_rva, registry) || !load_ptr(registry + 73832, rows)) return false;
    int index = lookup(registry + 73808, id);
    if (index < 0 || index >= 16384) return false;
    uintptr_t row;
    if (!load_ptr(rows + (uintptr_t)index * 8, row)) return false;
    uint32_t row_id{};
    if (!load(row + 8, row_id) || row_id != id) return false;
    return load(row, type);
}
struct Behaviour { uintptr_t addr{}; uint32_t ai{}, target{}; uint64_t deadline{}; };
bool behaviour_record(uint32_t id, Behaviour& out)
{
    uintptr_t manager;
    if (!load_ptr(game + behaviours_rva, manager)) return false;
    int index = lookup(manager + 64, id);
    if (index < 0 || index >= 16384) return false;
    uintptr_t records;
    if (!load_ptr(manager + 96, records)) return false;
    uintptr_t addr = records + (uintptr_t)index * behaviour_stride;
    uint32_t owner{}, ai{}, target{};
    uint64_t deadline{};
    if (!load(addr + 104, owner) || owner != id) return false;
    if (!load(addr, ai) || !ai) return false;
    if (!load(addr + 24, target) || !load(addr + 152, deadline)) return false;
    out = {addr, ai, target, deadline};
    return true;
}
bool perception_record(uint32_t id, uintptr_t& pbase)
{
    uintptr_t manager, records;
    if (!load_ptr(game + perception_rva, manager)) return false;
    int index = lookup(manager + 48, id);
    if (index < 0 || index >= 16384 || !load_ptr(manager + 80, records)) return false;
    pbase = records + (uintptr_t)index * perception_stride;
    return pbase >= 65536 && pbase < 0x800000000000ull;
}
struct Candidate {
    uint32_t id{}, mask{}, flags{};
    uintptr_t entry{}, mask_addr{};
    float score{}, memory{};
    bool visible{}, eligible{};
    uint64_t type{};
    const HeavyType* heavy{};
    bool seen_now() const { return visible || memory >= .5f; }
};
struct CandidateList { std::vector<Candidate> items; uint32_t filter{}; };
bool read_candidates(uintptr_t pbase, CandidateList& out)
{
    unsigned char buffer[perception_bytes];
    if (!api->read(pbase, buffer, sizeof buffer)) return false;
    auto u32 = [&](size_t at) { uint32_t v; memcpy(&v, buffer + at, 4); return v; };
    auto f32 = [&](size_t at) { float v; memcpy(&v, buffer + at, 4); return v; };
    uint32_t factions = u32(0);
    if (factions > 32) return false;
    uint32_t filter = 0;
    for (uint32_t j = 0; j < factions; j++) {
        uint32_t faction = u32(8 + j * 8);
        if (faction >= 32) return false;
        filter |= 1u << faction;
    }
    out.filter = filter;
    auto add = [&](size_t entry) {
        if (entry + 80 > sizeof buffer) return;
        uint32_t id = u32(entry);
        if (!id) return;
        float score = f32(entry + 68);
        if (!(score == score) || std::fabs(score) >= 100000.f) return;
        uint32_t flags = u32(entry + 72), mask = u32(entry + 76);
        uint64_t type{};
        bool known = entity_type(id, type);
        Candidate c;
        c.id = id;
        c.entry = pbase + entry;
        c.mask_addr = pbase + entry + 76;
        c.mask = mask;
        c.flags = flags;
        c.score = score;
        c.memory = f32(entry + 64);
        c.visible = buffer[entry + 48] != 0;
        c.type = type;
        c.heavy = known ? heavy_of(type) : nullptr;
        c.eligible = known && (flags & 1) && (mask & filter) && score > 0;
        out.items.push_back(c);
    };
    static constexpr size_t groups[][2] = {{784, 792}, {2072, 2080}, {3360, 3368}};
    for (const auto& group : groups) {
        uint32_t count = u32(group[0]);
        if (count > 16) return false;
        for (uint32_t k = 0; k < count; k++) add(group[1] + (size_t)k * 80);
    }
    static constexpr size_t singles[] = {4648, 4728, 4808};
    for (size_t one : singles)
        if (u32(one + 72) != 0) add(one);
    return true;
}
/* a blanked mask, with what it was and the entry it belongs to (the entry's id must still match, and the game must not
 * have refreshed the mask, before it is put back) */
struct Hidden { uintptr_t entry{}, addr{}; uint32_t id{}, before{}; };
struct State {
    float next{}, heavy_ago{99}, kick_wait{99};
    bool blocked{};
    bool logged_ai{}, logged_enemy{}, warned_ai{}, warned_perception{}, warned_write{}, warned_local{};
    uint64_t heavy_type{};
    unsigned heavy_armor{};
    std::vector<Hidden> hidden;
};
std::unordered_map<uint32_t, State> states;

bool unhide(const Hidden& h)
{
    uint32_t entry_id{}, mask{};
    if (!load(h.entry, entry_id) || entry_id != h.id) return true; /* the entry is another enemy now: the game's mask is its own */
    if (!load(h.addr, mask) || mask != blank) return true;         /* the game has refreshed the mask: nothing to put back */
    try { data_write(h.addr, &h.before, 4); } catch (...) { return false; }
    return true;
}
void restore_only(State& s)
{
    for (const auto& h : s.hidden) unhide(h);
    s.hidden.clear();
}
void warn_write(State& s)
{
    if (s.warned_write) return;
    s.warned_write = true;
    api->log(mod, "HUNTER_WRITE_REFUSED: could not change a perception mask");
}
/* the Guard Dog mod's hider: keep the masks of the enemies the game has moved or dropped, put back what is no longer
 * wanted, and blank what is */
void apply(State& s, const std::vector<Candidate>& candidates, const std::vector<uint32_t>& block)
{
    auto wanted = [&](uint32_t id) {
        for (uint32_t x : block) if (x == id) return true;
        return false;
    };
    std::unordered_map<uintptr_t, const Candidate*> by_addr;
    for (const auto& c : candidates) by_addr[c.mask_addr] = &c;
    std::vector<Hidden> moved;
    for (size_t n = 0; n < s.hidden.size();) {
        auto& h = s.hidden[n];
        auto it = by_addr.find(h.addr);
        if (it == by_addr.end() || it->second->id != h.id) {
            moved.push_back(h);
            s.hidden.erase(s.hidden.begin() + n);
        } else ++n;
    }
    for (const auto& h : moved) {
        for (const auto& c : candidates) {
            if (c.id != h.id || c.mask != blank) continue;
            bool tracked = false;
            for (const auto& t : s.hidden) if (t.addr == c.mask_addr) { tracked = true; break; }
            if (!tracked) {
                s.hidden.push_back({c.entry, c.mask_addr, c.id, h.before});
                break;
            }
        }
    }
    for (size_t n = 0; n < s.hidden.size();) {
        auto& h = s.hidden[n];
        auto it = by_addr.find(h.addr);
        if (it != by_addr.end() && it->second->mask != blank) h.before = it->second->mask;
        if (!wanted(h.id)) {
            unhide(h);
            s.hidden.erase(s.hidden.begin() + n);
        } else ++n;
    }
    for (const auto& c : candidates) {
        if (!wanted(c.id) || c.mask == blank) continue;
        uint32_t entry_id{}, mask_now{};
        if (!load(c.entry, entry_id) || entry_id != c.id) continue;
        if (!load(c.mask_addr, mask_now) || mask_now != c.mask) continue;
        try { data_write(c.mask_addr, &blank, 4); } catch (...) { warn_write(s); continue; }
        bool found = false;
        for (auto& h : s.hidden) {
            if (h.addr != c.mask_addr) continue;
            h.entry = c.entry;
            h.before = c.mask;
            found = true;
            break;
        }
        if (!found) s.hidden.push_back({c.entry, c.mask_addr, c.id, c.mask});
    }
}
/* make the AI choose again now, if its timer looks sane (the same clock and check the Guard Dog mod uses) */
void kick(uint32_t id, const Behaviour& rec, State& s)
{
    uintptr_t clock;
    if (!load_ptr(game + clock_rva, clock)) return;
    uint64_t now{};
    if (!load(clock + 24, now) || !now || now > 9007199254740991ull) return;
    uint32_t owner{}, ai{};
    uint64_t deadline{};
    if (!load(rec.addr + 104, owner) || owner != id || !load(rec.addr, ai) || ai != rec.ai) return;
    if (!load(rec.addr + 152, deadline) || !(deadline > now && deadline - now <= 1000000ull)) return;
    try { data_write(rec.addr + 152, &now, 8); } catch (...) { warn_write(s); return; }
    s.kick_wait = 0;
}
void poll(const Soldier& soldier, State& s)
{
    uint32_t id = soldier.identity.id;
    Descriptor d;
    try { d = descriptor(id); } catch (...) { return; }
    if (d.type != soldier.identity.type || d.network != soldier.identity.network) { forget(id); return; }
    if (!(d.flags & 1)) { forget(id); return; }
    /* the AI of another player's squad copy is not this game's to steer */
    if ((d.flags & 3) != 1) {
        if (!s.warned_local) {
            s.warned_local = true;
            api->log(mod, "HUNTER_SKIP: soldier %u is not run by this game (flags %08x): not steered", id, d.flags);
        }
        restore_only(s);
        return;
    }
    Behaviour rec;
    if (!behaviour_record(id, rec)) {
        if (!s.warned_ai) {
            s.warned_ai = true;
            api->log(mod, "HUNTER_NO_AI: soldier %u has no AI record yet: not steered", id);
        }
        restore_only(s);
        return;
    }
    uintptr_t pbase;
    if (!perception_record(id, pbase)) {
        if (!s.warned_perception) {
            s.warned_perception = true;
            api->log(mod, "HUNTER_NO_PERCEPTION: soldier %u has no perception record yet: not steered", id);
        }
        restore_only(s);
        return;
    }
    CandidateList list;
    if (!read_candidates(pbase, list)) return; /* being written this frame: try again at the next poll */
    if (!s.logged_ai) {
        s.logged_ai = true;
        unsigned enemies = 0;
        for (const auto& c : list.items) if (c.eligible) enemies++;
        api->log(mod, "HUNTER_AI: soldier %u is AI %u, perceives %u candidates (%u enemies)", id, rec.ai,
                 (unsigned)list.items.size(), enemies);
    }
    bool any_heavy = false, heavy_seen = false;
    unsigned armor = 0;
    uint64_t type = 0;
    std::vector<uint32_t> block;
    const Candidate* best = nullptr;
    for (const auto& c : list.items) {
        if (!c.eligible) continue;
        if (!best || c.score > best->score) best = &c;
        if (c.heavy) {
            any_heavy = true;
            if (c.seen_now() && c.heavy->armor >= armor) {
                heavy_seen = true;
                armor = c.heavy->armor;
                type = c.type;
            }
        } else block.push_back(c.id);
    }
    if (best && !s.logged_enemy) {
        s.logged_enemy = true;
        char what[96];
        if (best->heavy) snprintf(what, sizeof what, "%s (armor %u)", best->heavy->name, best->heavy->armor);
        else snprintf(what, sizeof what, "type %016llx, not a heavy", (unsigned long long)best->type);
        api->log(mod, "HUNTER_SEES: soldier %u (of %u candidates) strongest is %s", id, (unsigned)list.items.size(), what);
    }
    if (heavy_seen) {
        s.heavy_ago = 0;
        s.heavy_type = type;
        s.heavy_armor = armor;
    }
    bool active = any_heavy && (heavy_seen || s.heavy_ago <= heavy_hold);
    if (!active) block.clear();
    std::sort(block.begin(), block.end());
    block.erase(std::unique(block.begin(), block.end()), block.end());
    apply(s, list.items, block);
    if (active && !s.blocked) {
        s.blocked = true;
        const HeavyType* h = heavy_of(s.heavy_type);
        api->log(mod, "HUNTER_HUNT: soldier %u (AI %u) goes for %s (armor %u): %u other enemies ignored", id, rec.ai,
                 h ? h->name : "a heavy", s.heavy_armor, (unsigned)block.size());
    } else if (!active && s.blocked) {
        s.blocked = false;
        api->log(mod, "HUNTER_HUNT_END: soldier %u (AI %u): no heavy in sight, normal targeting", id, rec.ai);
    }
    if (!active || s.kick_wait < kick_gap) return;
    bool target_heavy = false;
    for (const auto& c : list.items)
        if (c.heavy && c.id == rec.target) { target_heavy = true; break; }
    if (!target_heavy) kick(id, rec, s);
}

} /* namespace */

void frame(float dt)
{
    (void)dt;
    if (!OptionsTankHunters()) reset();
}
void maintain(const Soldier& soldier, float dt)
{
    if (!soldier.identity.id) return;
    auto& s = states[soldier.identity.id];
    s.heavy_ago += dt;
    s.kick_wait += dt;
    s.next -= dt;
    if (s.next > 0) return;
    s.next = poll_seconds;
    try { poll(soldier, s); } catch (...) {}
}
void forget(uint32_t id)
{
    auto it = states.find(id);
    if (it == states.end()) return;
    for (const auto& h : it->second.hidden) unhide(h);
    states.erase(it);
}
void reset()
{
    for (auto& entry : states)
        for (const auto& h : entry.second.hidden) unhide(h);
    states.clear();
}

}} /* namespace seaf::hunters */
