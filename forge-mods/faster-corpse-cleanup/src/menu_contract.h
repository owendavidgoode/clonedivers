/* Corpse-owned client contract, not a foreign ABI. MIT. */
#ifndef CR_MENU_CONTRACT_H
#define CR_MENU_CONTRACT_H
#include <stdint.h>
#define CR_MENU_TOGGLE 1u
#define CR_MENU_CHOICE 2u
#define CR_MENU_SLIDER 3u
#define CR_MENU_MAX_CHOICES 16u
#define CR_MENU_MAX_OPTIONS 26u
typedef void (*CrMenuChanged)(const char *,double,void *);
typedef struct CrMenuOption {
    uint32_t size,type;
    const char *id,*mod_id,*mod_name,*label,*description;
    double default_value,min,max,step;
    uint32_t choice_count;
    const char *choices[CR_MENU_MAX_CHOICES];
    CrMenuChanged on_change;void *user;
} CrMenuOption;

typedef struct CrMenuApi {
    int (*connect)(void);
    uint64_t (*epoch)(void);
    int (*register_option)(const CrMenuOption *);
    int (*get)(const char *,double *);
    int (*set)(const char *,double);
    int (*ready)(void);
} CrMenuApi;
#endif
