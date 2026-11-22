/* MOM-C6-EXP-r02: inferred, offline-only candidate; NOT an author ABI header.
 * Copyright (c) 2026 enochlophobianumerophobia. MIT; see LICENSE.
 * No function pointer, DLL resolver or foreign call is defined here. */
#ifndef C6_SPIKE_H
#define C6_SPIKE_H
#include <stddef.h>
#include <stdint.h>
#include "menu_contract.h"

typedef struct C6SpecHypothesis {
    const char *kind, *label, *mod, *mod_id, *description;
    const char *const *choices;
    int32_t choice_count, has_default;
    double default_value;
    int32_t has_min, has_max, has_step;
    double min, max, step;
    int32_t gap;
} C6SpecHypothesis;
typedef struct C6OwnedOption {
    char id[96], mod_id[96], mod[96], label[96], description[512];
    char choices[16][96];
    uint32_t type, choice_count;
    double default_value, min, max, step;
} C6OwnedOption;
/* View owns only its pointer array; strings borrow from stable owned storage. */
typedef struct C6SpecView { C6SpecHypothesis spec; const char *choices[16]; } C6SpecView;
int c6_prepare(const CrMenuOption *input, C6OwnedOption *out);
int c6_view(const C6OwnedOption *owned, C6SpecView *out);
#endif
