/* SPDX-License-Identifier: MIT
 * Independently authored binary-layout reconstruction for the exact distributed
 * Vanilla Plus Megapack 0.2.0 DLL, SHA256:
 * 6205b8c5699b9f07118b334cc4ee3e053a3023c9e973f94de1db18c68b31f02f
 * This is a client declaration, not an upstream SDK or execution permission.
 * Interface and lifecycle limits: CLIENT_INTERFACE.md in the mod package.
 */
#ifndef HEALTH_VPM_MOM_BINARY_V1_H
#define HEALTH_VPM_MOM_BINARY_V1_H
#include <stdint.h>
#include <stddef.h>

#if !defined(_WIN32) || !(defined(__x86_64__) || defined(_M_X64))
#error This reconstructed declaration is limited to Windows x64.
#endif

#define VPM_MOM_NATIVE_VERSION 1u
#define VPM_MOM_TABLE_BYTES 56u
#define VPM_MOM_DESCRIPTOR_BYTES 112u
/* Exact table fingerprint words. No capabilities or size-negotiation semantics
 * are assigned to these two words by this reconstruction. */
#define VPM_MOM_OPAQUE_WORD_08 0x70u
#define VPM_MOM_OPAQUE_WORD_0C 0x40u

/* All strings are NUL-terminated. Use literal type "toggle", "choice", or
 * "slider". Integer flags use 0 or 1. Zero-initialize the whole descriptor,
 * including padding, and explicitly supply mod and mod_id to avoid automatic
 * caller-module naming. Keep all caller storage alive: no general retention or
 * ownership guarantee is inferred here. The provider has no descriptor-size
 * argument; a shortened descriptor is never a supported prefix.
 */
typedef struct VpmMomDescriptor {
    const char *type;
    const char *label;
    const char *mod;
    const char *mod_id;
    const char *description;
    const char *const *choices;
    int32_t choice_count;
    int32_t has_default;
    double default_value;
    int32_t has_min;
    int32_t has_max;
    int32_t has_step;
    /* Four bytes of ordinary ABI padding before min. */
    double min;
    double max;
    double step;
    int32_t gap;
    /* Four bytes of ordinary ABI tail padding. */
} VpmMomDescriptor;

/* Win64 ABI: set's second argument is a double in XMM1, not an integer in RDX.
 * Register returns 1 for new or equivalent registration, 0 for refusal.
 * Get/set return 1 on success and 0 on refusal. Get copies one double on success
 * when value is non-NULL; it leaves the caller's output untouched on refusal.
 * Error storage is a pointer output, not a caller-owned text buffer. Treat the
 * error as borrowed/opaque; do not free it. Success need not clear *error.
 */
typedef int (*VpmMomRegister)(const char *id, const VpmMomDescriptor *, const char **error);
typedef int (*VpmMomGet)(const char *id, double *value);
typedef int (*VpmMomSet)(const char *id, double value, const char **error);

/* This predicate only observes provider component state 1 and a non-NULL
 * secondary cell. It supplies no lock, thread identity, unload or reload safety.
 * Do not use it alone as a foreign-call admission guard.
 */
typedef int (*VpmMomObservedReady)(void);

typedef struct VpmMomTable {
    uint32_t native_version;
    uint32_t table_bytes;
    uint32_t opaque_word_08;
    uint32_t opaque_word_0c;
    VpmMomRegister register_option;
    VpmMomGet get;
    VpmMomSet set;
    /* Slot exists, but callback invocation ABI/lifetime/removal is unqualified.
     * Deliberately expose no callable subscription type. */
    uintptr_t unsupported_subscription_entry;
    VpmMomObservedReady observed_ready;
} VpmMomTable;

/* Export vpm_mom_api compares ECX against 1 and returns a borrowed table pointer
 * only while its separate global byte is zero. Other version bits return NULL.
 * This declaration does not resolve discovery, module lifetime or thread rules.
 */
typedef const VpmMomTable *(*VpmMomExport)(uint32_t requested_version);

_Static_assert(sizeof(void *) == 8 && sizeof(int) == 4 && sizeof(double) == 8,
               "MOM Windows x64 primitive sizes");
_Static_assert(sizeof(VpmMomDescriptor) == VPM_MOM_DESCRIPTOR_BYTES,
               "MOM descriptor layout");
_Static_assert(sizeof(VpmMomTable) == VPM_MOM_TABLE_BYTES, "MOM table layout");
_Static_assert(offsetof(VpmMomDescriptor, default_value) == 0x38 &&
               offsetof(VpmMomDescriptor, min) == 0x50 &&
               offsetof(VpmMomDescriptor, gap) == 0x68, "MOM descriptor offsets");
_Static_assert(offsetof(VpmMomTable, register_option) == 0x10 &&
               offsetof(VpmMomTable, observed_ready) == 0x30, "MOM table offsets");
#endif
