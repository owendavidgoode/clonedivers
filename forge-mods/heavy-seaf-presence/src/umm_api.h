/* Universal Mod Menu: the cross-DLL ABI (version 1) through which other HellForge code mods use UMM's faithful C
 * translations of the megapack's Mod Options Menu (v1.2.1) and Mod Bindings Menu (v2.3).
 *
 * WHY. In the Bingus megapack every addon ran in one Lua state: an addon found the menus with
 * rawget(_G, 'ModOptionsMenu') / rawget(_G, 'ModBindingsMenu') and handed them plain tables and closures. Under
 * HellForge each mod is a DLL with its own tm runtime (heap, collector, globals), and DLLs of different builds must
 * interoperate. So UMM publishes its two tables through this plain C ABI, and every other DLL's runtime (tm_umm.c)
 * shows them in that DLL's tm globals as proxy tables that behave like the originals: same fields, same functions,
 * same arguments and results, callbacks both ways.
 *
 * PUBLISHING. UMM sets the process environment variable UMM_API_ENV = "<process id>:<address of a umm_api>" from its
 * hellforge_mod_init (so it is there before any mod's first game frame, whatever the load order). A client checks the
 * process id (an inherited variable of a parent process is ignored), version >= 1 and size. Everything runs on the
 * game thread: calls may be made from any game-thread callback of either side, and nest (a client callback that UMM
 * calls may call UMM again).
 *
 * VALUES (umm_value). nil, boolean, number (double), string (bytes + length, NUL-terminated as well), table (plain
 * data: its pairs, deep-copied, in the sender's pairs() order: array part 1..n first, then insertion order; a table
 * that appears twice in one transfer, or contains itself, arrives as one table, so identity and cycles survive one
 * transfer) and function (a umm_callback). Anything else (coroutines, engine handles, raw blocks) crosses as nil.
 * A receiver converts what it gets into its own runtime's values at once: received memory is only valid during the
 * call (arguments) or until the receiver resets its own arena (results it asked for).
 *
 * MEMORY (umm_arena). Who receives, allocates: a caller passes the arena results (and written-back arguments) go into;
 * the callee builds them with arena->alloc and the caller frees the arena after converting. Nothing is freed across
 * DLLs.
 *
 * CALLBACKS (umm_callback). A function crosses as a callback object made and owned by the DLL whose function it is
 * (the "owner"; its runtime keeps the function alive, pinned, while the callback exists). `call` runs it in the
 * owner's runtime. A receiver that keeps a callback beyond the call that handed it over calls retain() once, and
 * release() once when it no longer holds it (tm_umm.c: when the receiver's collector frees its proxy function, so a
 * registered on_change closure lives exactly as long as the menu keeps it). retain/release only count (plain C, safe
 * inside a collection); the owner frees an unreferenced callback later, at its own poll, never during a call. A
 * callback handed back to its owner is its owner's original function again (identity round-trips).
 *
 * CALLS. cb->call(cb, argc, argv, out, rets, maxrets, flags): >= 0 the number of results written to rets (in `out`),
 * or -1 when the function raised: rets[0] is the error value (normally a string), which the caller raises in its own
 * runtime (as Lua's error propagation through a call). With UMM_CALL_WRITEBACK the callee also replaces each table
 * argument argv[i] with that table's contents after the call (in `out`), and the caller updates its own original
 * table to match (keys set, changed, removed; nested tables updated in place): so a menu function that fills a table
 * the caller passed (ModBindingsMenu.poll(ids, out)) works as with the shared Lua table.
 *
 * TABLES (snapshot). api->snapshot(name, ...) gives the current fields of the published table `name` with its
 * functions as callbacks; the client copies them into its proxy table (raw fields: rawget(menu, 'revision') works)
 * and refreshes the proxy every frame (tm_umm_poll) and after every call into UMM, so changing fields (revision,
 * extended.count) read as in the original. `instance` identifies the table: a new number means UMM replaced it (the
 * original's global set again), and the client makes a new proxy table, so `found ~= menu` checks see a new menu.
 *
 * AVAILABILITY (the rule the originals' lookups rely on). Clients call tm_umm_attach() once from their start-up (game
 * thread, before their translated code first looks for the menus) and tm_umm_poll() once per frame (tm_modopts_poll,
 * which the runtime's frame driver already calls, does it once tm_umm_attach ran). UMM publishes from
 * hellforge_mod_init, before any game frame, so the proxies exist from every client's first frame in any load order,
 * exactly as in the megapack where the menus' chunks set their globals while the addons loaded. If UMM is not
 * installed the globals stay nil (the originals' "not installed" path). If UMM's tables appear later (not by
 * design), the proxies appear at the next poll and the originals' own retry logic (revision / identity checks,
 * REGISTER_FRAMES, after_startup) picks them up where they had one.
 *
 * SECTIONS (extension UMM_EXT_SECTIONS; the translated MOM must honour it; UMM's ModOptionsMenu table has the extra
 * field `sections = 1`). register_option(id, spec) accepts two more spec fields:
 *   spec.section          string or text function (MOM's translation.resolve rules, at most 40 characters): the row
 *                         goes into the category of that name instead of the mod's own. All mods naming the same
 *                         section share ONE category (one category button; counted once against max_mods).
 *   spec.mod_description  string or text function (at most 400 characters), optional: the mod's text in the section.
 * Inside a section each mod (identified as MOM identifies a mod's category: spec.mod_id, else the mod name as first
 * registered) is a PAGE. The section's rows are: first a choice row labelled "Mod" whose choices are the pages' mod
 * names (in the order the pages first registered; the row's value is the shown page, starting at the first; it is
 * view state, never saved and never reported through get/on_change), then the shown page's own rows in their
 * registration order (max_options applies per page). Only one page's rows are shown at a time. Changing the "Mod"
 * row shows the newly chosen page (the rows are built again), except that a change within 0.4 s (os.clock) of the
 * previous page change is undone: the row returns to the shown page (the rebuilt row must not take the same click
 * again). While the "Mod" row is selected its description is the shown page's mod_description, else the text "Shows
 * one mod's options at a time: change this row to pick another mod." Rows of a page behave as any MOM row (ids,
 * saved values, get, set, on_change, descriptions). A row registered again with the same id and an equal spec
 * (section included) is accepted as before; the same id with a different section is "registered differently".
 * UMM_CONVERSIONS_SECTION is the section of the user's conversions.
 *
 * COMPATIBILITY. UMM also publishes Vanilla Plus Native's HD2_MOD_OPTIONS_v1 API (hd2_mod_options.h) mapped onto
 * MOM rows (tm_umm_serve_v1), unless another mod of this process already published it, so conversion builds made for
 * Vanilla Plus Native keep their rows until they are rebuilt: each v1 mod is its own MODS entry (its Hd2ModOption.mod
 * name), not a section; a second register_option of a row only updates its value and callback. */
#ifndef UMM_API_H
#define UMM_API_H
#include <stddef.h>
#include <stdint.h>

#define UMM_API_ENV "HD2_UMM_API_v1"
#define UMM_API_VERSION 1
#define UMM_MAXARGS 16
#define UMM_MAXRETS 8
#define UMM_CONVERSIONS_SECTION "Beelzebub's Conversions"

enum { UMM_NIL = 0, UMM_BOOL, UMM_NUM, UMM_STR, UMM_TAB, UMM_FN };
enum { UMM_TF_ARRAY = 1 };                     /* umm_table.flags: the original's JSON array marker (tm TM_TF_ARRAY) */
enum { UMM_CALL_WRITEBACK = 1 };               /* call flags */
enum { UMM_EXT_SECTIONS = 1 };                 /* umm_api.extensions */

typedef struct umm_value umm_value;
typedef struct umm_pair umm_pair;
typedef struct umm_table umm_table;
typedef struct umm_callback umm_callback;
typedef struct umm_arena umm_arena;

struct umm_value                               /* 16 bytes */
{
    uint32_t type;                             /* UMM_* */
    uint32_t len;                              /* UMM_STR: the byte length */
    union
    {
        int32_t b;                             /* UMM_BOOL: 0 / 1 */
        double n;                              /* UMM_NUM */
        const char* s;                         /* UMM_STR: len bytes, then a NUL */
        umm_table* t;                          /* UMM_TAB */
        umm_callback* fn;                      /* UMM_FN */
    } u;
};
struct umm_pair { umm_value key, value; };
struct umm_table
{
    uint32_t count;                            /* pairs */
    uint32_t flags;                            /* UMM_TF_* */
    umm_pair* pairs;                           /* in the sender's pairs() order */
};

struct umm_arena
{
    void* (*alloc)(umm_arena* self, size_t bytes); /* zeroed, 16-byte aligned; NULL when out of memory */
    void* impl;
};

typedef int (*umm_call_fn)(umm_callback* self, int argc, umm_value* argv, umm_arena* out, umm_value* rets, int maxrets,
                           uint32_t flags);
struct umm_callback
{
    uint32_t size;                             /* sizeof(umm_callback) of the owner's build */
    uint32_t refs;                             /* retains outstanding (the owner's bookkeeping) */
    umm_call_fn call;
    void (*retain)(umm_callback* self);
    void (*release)(umm_callback* self);
    const void* owner;                         /* the owning DLL's side (identity: a callback back home is unwrapped) */
    uintptr_t user;                            /* the owner's */
};

typedef struct umm_api
{
    uint32_t version;                          /* UMM_API_VERSION */
    uint32_t size;                             /* sizeof(umm_api) of UMM's build */
    uint32_t pid;
    uint32_t extensions;                       /* UMM_EXT_* bits */
    /* The published table `name` ("ModOptionsMenu", "ModBindingsMenu"): 1 with its fields in *fields (a UMM_TAB, in
     * `out`) and its identity in *instance; 0 when UMM publishes no such table (the original's global is nil). */
    int (*snapshot)(const char* name, umm_arena* out, umm_value* fields, uint64_t* instance);
    /* changes when UMM publishes or replaces a table, and when a call into UMM (or UMM's own frame) changed the
     * published tables' fields (a fingerprint: four levels, functions by identity). A client refreshes its proxies
     * after a call only when it changed, and every frame regardless (a change deeper down is seen then). */
    uint64_t (*generation)(void);
    const char* build;                         /* UMM's version text, for logs */
} umm_api;

/* The API if UMM runs in this process, else NULL (cheap enough to call once a frame). */
#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#ifndef _WIN32
#include <unistd.h>
#endif
static inline const umm_api* umm_api_find(void)
{
    char text[64];
#ifdef _WIN32
    unsigned long n = GetEnvironmentVariableA(UMM_API_ENV, text, sizeof text);
    if (!n || n >= sizeof text) return NULL;
    unsigned long self = GetCurrentProcessId();
#else
    const char* e = getenv(UMM_API_ENV);
    if (!e || strlen(e) >= sizeof text) return NULL;
    strcpy(text, e);
    unsigned long self = (unsigned long)getpid();
#endif
    unsigned long pid = 0;
    unsigned long long at = 0;
    if (sscanf(text, "%lu:%llx", &pid, &at) != 2 || pid != self || !at) return NULL;
    const umm_api* api = (const umm_api*)(uintptr_t)at;
    if (api->version < 1 || api->size < offsetof(umm_api, build) + sizeof(const char*) || api->pid != self) return NULL;
    return api;
}
#endif
