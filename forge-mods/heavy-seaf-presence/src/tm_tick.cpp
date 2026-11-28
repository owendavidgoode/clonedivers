/* One HellForge game-thread slot for all of the author's mod DLLs (tm_tick.h). Plain C: no tm runtime. */
#include "tm_tick.h"
#ifdef TM_TICK_AVAILABLE

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#else
#include <unistd.h>
#endif

/* ---- the hub (used only in the DLL that created it) ------------------------------------------------------------- */
static tm_tick_hub hubOwn;
static tm_tick_client* volatile hubClients[TM_TICK_HUB_MAXCLIENTS];
static volatile int32_t hubCount;
static long long hubRuns;
static int hubRunning;

static int HubAdd(tm_tick_client* c)
{
    if (!c || c->size < offsetof(tm_tick_client, reserved) || !c->fn) return -1;
    for (int i = 0; i < hubCount; i++) if (hubClients[i] == c) return i;
    if (hubCount >= TM_TICK_HUB_MAXCLIENTS) return -1;
    c->index = hubCount;
    hubClients[hubCount] = c;
    hubCount = hubCount + 1; /* (published after the entry: a dispatcher running on the game thread sees it whole) */
    return c->index;
}

static void HubRun(HfMod* mod, float dt, void* user)
{
    (void)mod, (void)user;
    if (hubRunning) return;
    hubRunning = 1;
    hubRuns++;
    int n = hubCount;
    for (int i = 0; i < n; i++)
    {
        tm_tick_client* c = hubClients[i];
        if (!c || (c->flags & TM_TICK_CLIENT_OFF)) continue;
        c->fn(c->mod, dt, c->user);
    }
    hubRunning = 0;
}

static long long HubQuery(int what)
{
    switch (what)
    {
    case TM_TICK_Q_CLIENTS: return hubCount;
    case TM_TICK_Q_RUNS: return hubRuns;
    case TM_TICK_Q_OWNED: return hubOwn.owner != NULL;
    default: return 0;
    }
}

/* ---- finding / creating the hub (the process environment) ------------------------------------------------------- */
static unsigned Pid(void)
{
#ifdef _WIN32
    return (unsigned)GetCurrentProcessId();
#else
    return (unsigned)getpid();
#endif
}

static tm_tick_hub* FindHub(void)
{
    char v[96];
#ifdef _WIN32
    DWORD n = GetEnvironmentVariableA(TM_TICK_HUB_ENV, v, (DWORD)sizeof v);
    if (!n || n >= sizeof v) return NULL;
#else
    const char* e = getenv(TM_TICK_HUB_ENV);
    if (!e || strlen(e) >= sizeof v) return NULL;
    strcpy(v, e);
#endif
    unsigned pid = 0;
    unsigned long long at = 0;
    if (sscanf(v, "%u:%llx", &pid, &at) != 2 || pid != Pid() || !at) return NULL;
    tm_tick_hub* h = (tm_tick_hub*)(uintptr_t)at;
#ifdef _WIN32
    MEMORY_BASIC_INFORMATION mbi;
    if (!VirtualQuery(h, &mbi, sizeof mbi) || mbi.State != MEM_COMMIT) return NULL;
#endif
    if (h->magic != TM_TICK_HUB_MAGIC || h->pid != Pid() || h->version < 1 ||
        h->size < offsetof(tm_tick_hub, query) + sizeof h->query || !h->add || !h->run || !h->query)
        return NULL;
    return h;
}

static void PublishHub(void)
{
    char v[96];
    snprintf(v, sizeof v, "%u:%llx", Pid(), (unsigned long long)(uintptr_t)&hubOwn);
#ifdef _WIN32
    SetEnvironmentVariableA(TM_TICK_HUB_ENV, v);
#else
    setenv(TM_TICK_HUB_ENV, v, 1);
#endif
}

static tm_tick_hub* hub;

const tm_tick_hub* tm_tick_hub_get(void)
{
    if (hub) return hub;
    hub = FindHub();
    if (!hub)
    {
        hubOwn.magic = TM_TICK_HUB_MAGIC, hubOwn.version = TM_TICK_HUB_VERSION, hubOwn.size = sizeof hubOwn;
        hubOwn.pid = Pid();
        hubOwn.add = HubAdd, hubOwn.run = HubRun, hubOwn.query = HubQuery, hubOwn.owner = NULL;
        PublishHub();
        hub = &hubOwn;
    }
    return hub;
}

/* ---- this DLL's side ------------------------------------------------------------------------------------------- */
enum { MAXMINE = 4 };
static tm_tick_client mine[MAXMINE];
static int nMine;
static int ownSlot; /* the fallback: some callback of this DLL on its own on_game_thread */

/* the fallback's trampoline (so tm_tick_leave works there too) */
static void OwnTick(HfMod* mod, float dt, void* user)
{
    (void)mod;
    tm_tick_client* c = (tm_tick_client*)user;
    if (!(c->flags & TM_TICK_CLIENT_OFF)) c->fn(c->mod, dt, c->user);
}

static int Mine(const tm_tick_client* c) { return c >= mine && c < mine + MAXMINE; }

static int CanTick(const HfApi* hf)
{
    return hf && hf->version >= 6 && hf->size >= offsetof(HfApi, on_game_thread) + sizeof hf->on_game_thread &&
           hf->on_game_thread;
}

int tm_tick_join(const HfApi* hf, HfMod* mod, HfTickCallback fn, void* user, const char* name)
{
    if (!hf || !fn) return 0;
    if (!CanTick(hf)) { hf->log(mod, "game-thread tick: this HellForge has no on_game_thread"); return 0; }
    tm_tick_client* c = NULL;
    for (int i = 0; i < nMine; i++) if (mine[i].fn == fn && mine[i].user == user) c = &mine[i];
    int fresh = !c;
    if (!c)
    {
        if (nMine >= MAXMINE) return 0;
        c = &mine[nMine++];
        memset(c, 0, sizeof *c);
        c->size = sizeof *c, c->fn = fn, c->mod = mod, c->user = user, c->name = name ? name : "mod", c->index = -1;
    }
    c->flags &= ~(uint32_t)TM_TICK_CLIENT_OFF;
    if (c->index == -2) return 1; /* (on this DLL's own slot already) */
    tm_tick_hub* h = (tm_tick_hub*)tm_tick_hub_get();
    if (!h || h->add(c) < 0)
    {
        /* no shared hub (full, or unusable): this callback takes a slot of its own */
        if (!hf->on_game_thread(mod, OwnTick, c)) { hf->log(mod, "game-thread tick: the shared tick is full and HellForge refused a slot of its own"); return 0; }
        c->index = -2, ownSlot = 1;
        hf->log(mod, "game-thread tick: own HellForge slot (the shared tick is full)");
        return 1;
    }
    if (h->owner)
    {
        if (fresh || c->reserved) /* (reserved: this DLL's own note that the callback was waiting) */
        {
            if (Mine(h->owner)) hf->log(mod, "game-thread tick: shared (owner)");
            else hf->log(mod, "game-thread tick: shared (joined; %s holds the HellForge slot, %lld callbacks on it)",
                         h->owner->name ? h->owner->name : "another mod", h->query(TM_TICK_Q_CLIENTS));
        }
        c->reserved = 0;
        return 1;
    }
    if (hf->on_game_thread(mod, h->run, h))
    {
        h->owner = c;
        c->reserved = 0;
        long long waiting = h->query(TM_TICK_Q_CLIENTS) - 1;
        if (waiting > 0) hf->log(mod, "game-thread tick: shared (owner; %lld waiting callbacks start too)", waiting);
        else hf->log(mod, "game-thread tick: shared (owner)");
        return 1;
    }
    c->reserved = 1;
    hf->log(mod, "game-thread tick: HellForge refused the shared slot (modloader.log says why); waiting: it starts as soon as "
                 "one of the author's other mods gets it");
    return 0;
}

int tm_tick_leave(HfTickCallback fn, void* user)
{
    for (int i = 0; i < nMine; i++)
        if (mine[i].fn == fn && mine[i].user == user) { mine[i].flags |= TM_TICK_CLIENT_OFF; return 1; }
    return 0;
}

const char* tm_tick_role(void)
{
    int inHub = 0;
    for (int i = 0; i < nMine; i++) inHub |= mine[i].index >= 0;
    if (!inHub) return ownSlot ? "own" : "";
    if (!hub || !hub->owner) return "waiting";
    return Mine(hub->owner) ? "owner" : "joined";
}
#endif
