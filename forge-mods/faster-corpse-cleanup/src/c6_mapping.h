/* Exact Megapack 0.2.0 mapping acquisition; standalone experimental source. MIT.
 * Caller: existing game-thread setup only, once after choosing the native route.
 * SDK15 session lifetime only; no live replacement/hot-reload support. */
#ifndef C6_MAPPING_H
#define C6_MAPPING_H
#include "hellforge_mod.h"
#include <wchar.h>
typedef struct C6Mapping {void *module;uintptr_t base;int attempted,closed;} C6Mapping;
/* Actual inventory, file digest, module reference/path and mapped probe checks.
 * 1 retained exact mapping; 0 refused. No provider export/function is called. */
int c6_mapping_acquire(C6Mapping *,const HfApi *,HfMod *);
void c6_mapping_close(C6Mapping *);
#endif
