/* One HellForge game-thread slot for all of the author's mod DLLs (plain C; needs only hellforge_mod.h).
 *
 * WHY. HellForge allows at most 16 on_game_thread callbacks per process. Each of these mods used one (Universal Mod
 * Menu two), so a big mod list filled the table and the next mod's registration was refused ("on_game_thread: mods
 * registered 16 callbacks already"). With this file, every DLL built with it shares ONE HellForge callback.
 *
 * HOW. The first DLL to call tm_tick_join creates the process's tick hub (in its own static data) and publishes it in
 * the process environment variable TM_TICK_HUB_ENV ("<pid>:<address>", checked against the process id, as the engine
 * hub of tm_engine.h); the others find it. Every DLL adds its callbacks (its HfTickCallback, its own HfMod* and user)
 * to the hub. The hub holds HellForge's slot once ONE DLL got it: while it has none, the joining DLL asks HellForge
 * (on_game_thread(its mod, hub->run, hub)); when HellForge accepts, that DLL is the slot's owner, and from then on the
 * hub's dispatcher runs every registered callback, in registration order, every game update, with HellForge's dt and
 * each callback's own HfMod* and user. When HellForge refuses (its table is full), the DLL's callbacks stay registered
 * ("waiting"): the next DLL that joins asks again, and as soon as any of them gets the slot every waiting callback
 * runs too. A DLL may also call tm_tick_join again later (its next HellForge event) to ask again itself.
 *
 * Each callback is a plain C call; the mods' callbacks protect themselves (tm_pcall), so nothing is caught here. A
 * callback can be switched off (tm_tick_leave) and is skipped from then on. A DLL built without this file uses its own
 * HellForge slot and is unaffected. When the hub is full, or cannot be found or made, tm_tick_join falls back to the
 * DLL's own on_game_thread. The hub's code stays in the DLL that made it: HellForge never unloads a mod that stayed
 * loaded, so a DLL calls tm_tick_join only where it will stay loaded (after its own checks; never before returning
 * an error from hellforge_mod_init). */
#ifndef TM_TICK_H
#define TM_TICK_H
/* (Windows only, as the HellForge SDK; the Linux tests define TM_TICK_PORTABLE and pass -D__declspec(x)=) */
#if defined(_WIN32) || defined(TM_TICK_PORTABLE)
#define TM_TICK_AVAILABLE 1
#include <stddef.h>
#include <stdint.h>
#include "hellforge_mod.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Returns 1 when fn runs every game update from now on (this DLL got the shared slot: "owner"; another DLL holds it:
 * "joined"; or the fallback: the DLL's own on_game_thread), 0 when no slot could be had yet: fn stays registered and
 * starts as soon as any DLL gets the slot (call again later to ask HellForge again). Logs one line through hf->log:
 * "game-thread tick: shared (owner)", "game-thread tick: shared (joined; ...)", or why it waits. Calling it again for
 * the same fn and user registers nothing new (it asks HellForge again while the hub has no slot). Call it from the
 * loader's thread (hellforge_mod_init or a HellForge event), as on_game_thread. */
int tm_tick_join(const HfApi* hf, HfMod* mod, HfTickCallback fn, void* user, const char* name);
/* fn (with user) is skipped from now on (its registration stays); 1 when it was registered */
int tm_tick_leave(HfTickCallback fn, void* user);
/* how this DLL's callbacks run: "owner", "joined", "waiting", "own" (fallback: its own slot), "" (not joined) */
const char* tm_tick_role(void);

/* ---- the hub's ABI (version 1; append-only: a later version only adds fields at the end, `size` says which) -------- */
#define TM_TICK_HUB_ENV "HD2_TM_TICK_HUB_v1"
#define TM_TICK_HUB_MAGIC 0x4B434954U /* "TICK" */
#define TM_TICK_HUB_VERSION 1
#define TM_TICK_HUB_MAXCLIENTS 48
enum { TM_TICK_CLIENT_OFF = 1 }; /* tm_tick_client.flags: skip this callback */
enum { TM_TICK_Q_CLIENTS = 1, TM_TICK_Q_RUNS, TM_TICK_Q_OWNED };

typedef struct tm_tick_client tm_tick_client;
/* One per callback, in the registering DLL's static data (valid for the session). */
struct tm_tick_client
{
    uint32_t size;          /* sizeof(tm_tick_client) as the registering DLL knows it */
    uint32_t flags;         /* TM_TICK_CLIENT_*, written by the DLL at any time */
    HfTickCallback fn;
    HfMod* mod;             /* passed to fn (each DLL's own) */
    void* user;
    const char* name;       /* for logs */
    int32_t index;          /* written by the hub: 0-based registration order */
    uint32_t reserved;
};

typedef struct tm_tick_hub
{
    uint32_t magic;         /* TM_TICK_HUB_MAGIC */
    uint32_t version;       /* TM_TICK_HUB_VERSION of the creating DLL's build */
    uint32_t size;          /* sizeof(tm_tick_hub) of the creating DLL's build */
    uint32_t pid;
    /* registers a client (once; again returns its index); -1 when full or malformed */
    int (*add)(tm_tick_client* client);
    /* the dispatcher: the HfTickCallback a DLL registers with on_game_thread(its mod, run, the hub); it runs every
     * registered client that is not off, in registration order, with dt. Not reentrant (a nested call does nothing). */
    HfTickCallback run;
    /* the client whose DLL holds HellForge's slot (set by that DLL right after on_game_thread accepted run), else NULL */
    tm_tick_client* volatile owner;
    long long (*query)(int what); /* TM_TICK_Q_*: clients / dispatcher runs / slot held */
    uint32_t reserved[2];
} tm_tick_hub;
/* The process's hub (finds it, or creates it with this DLL's code). */
const tm_tick_hub* tm_tick_hub_get(void);
#ifdef __cplusplus
}
#endif
#endif
#endif
