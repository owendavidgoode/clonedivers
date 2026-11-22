/* C6-GUARD-r01: read-only reconstruction, exact Megapack 0.2.0 only. MIT.
 * This is NOT a provider-call authorization or an author-supported API.
 * Caller must separately retain the exact loaded module, bind supported HF SDK15
 * inventory/path, and invoke only on its existing game-thread callback.
 * No provider function pointer or call appears in this module. */
#ifndef C6_GUARD_H
#define C6_GUARD_H
#include <stddef.h>
#include <stdint.h>
/* Same success convention as HfApi.read: zero means copied all requested bytes. */
typedef int (*C6Read)(void *,uintptr_t,void *,size_t);
typedef struct C6Guard {
 uintptr_t base,component,backend;
 uint32_t generation,thread;
 int image_checked,latched,dead;
} C6Guard;
/* Mapped text digest + exact header/table/definition fingerprint, not whole-file hash. */
int c6_image_probe(C6Read read,void *context,uintptr_t base,size_t image_size);
/* 1 stable observed readiness, 0 not initially ready, -1 terminal refusal.
 * Generation is read from the exact provider component, never invented.
 * Double reads detect observed transitions; they are not an inter-thread lock.
 * Do not reset a guard to reopen a changed component within this client lifetime. */
int c6_guard_observe(C6Guard *,C6Read,void *,uintptr_t,size_t,uint32_t caller_thread);
#endif
