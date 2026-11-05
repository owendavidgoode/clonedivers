/* MIT. A source-first SDK mod for the owner's separate HellForge installation. */
#include "hellforge_mod.h"
#include "mine_boundary.h"
#include <windows.h>

static const HfApi* hf;
static volatile LONG skipped;
static int installed;

static void Report(HfMod* mod, const HfEventInfo* event, void* user)
{
    (void)user;
    if (installed) hf->log(mod, "MINE_BOUNDARY_EVENT kind=%u skipped_total=%ld capacity=%u", event->kind,
        InterlockedCompareExchange(&skipped, 0, 0), (unsigned)MINE_CAPACITY);
}

static void Install(HfMod* mod, HfEvent event, void* user)
{
    (void)event; (void)user;
    if (installed) return;
    size_t size = 0;
    uintptr_t game = hf->module_base("game.dll", &size);
    unsigned char actual[sizeof mine_original];
    if (!game || size <= MINE_FINISH || hf->read(game + MINE_SITE, actual, sizeof actual) ||
        memcmp(actual, mine_original, sizeof actual))
    { hf->log(mod, "MINE_BOUNDARY_REFUSED append code differs; nothing changed"); return; }
    unsigned char* stub = (unsigned char*)VirtualAlloc(NULL, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!stub) { hf->log(mod, "MINE_BOUNDARY_REFUSED no stub memory; nothing changed"); return; }
    size_t n = MineStub(stub, game + MINE_CONTINUE, game + MINE_FINISH, (uintptr_t)&skipped);
    DWORD previous;
    if (!VirtualProtect(stub, 4096, PAGE_EXECUTE_READ, &previous) || !FlushInstructionCache(GetCurrentProcess(), stub, n))
    { VirtualFree(stub, 0, MEM_RELEASE); hf->log(mod, "MINE_BOUNDARY_REFUSED stub protection failed; nothing changed"); return; }
    unsigned char jump[14]; MineJump(jump, (uintptr_t)stub);
    if (hf->write(game + MINE_SITE, jump, sizeof jump))
    { hf->log(mod, "MINE_BOUNDARY_REFUSED SDK write failed; stub retained until process exit"); return; }
    /* Once published, keep the stub alive until the process ends even if
     * readback fails. No thread may ever jump into released executable memory. */
    if (hf->read(game + MINE_SITE, actual, sizeof jump) || memcmp(actual, jump, sizeof jump))
    { hf->log(mod, "MINE_BOUNDARY_VERIFY_FAILED check the session before playing"); return; }
    installed = 1;
    hf->log(mod, "MINE_BOUNDARY_READY game25480438 capacity=%u guarded=game.dll+0x%X retained_valid_entries=true", (unsigned)MINE_CAPACITY, MINE_SITE);
}

HF_MOD_EXPORT int hellforge_mod_init(const HfApi* api, HfMod* mod)
{
    if (!api || api->version < 8 || api->size < offsetof(HfApi, subscribe) + sizeof api->subscribe ||
        !api->build_id() || strcmp(api->build_id(), "25480438")) return 1;
    hf = api;
    if (!hf->subscribe(mod, HF_ON_MISSION_START, Report, NULL, 0) || !hf->subscribe(mod, HF_ON_MISSION_END, Report, NULL, 0)) return 1;
    return hf->on(mod, HF_EVENT_GAME_LOADED, Install, NULL);
}
