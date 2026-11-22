/* Original Corpse client code. Copyright (c) 2026 enochlophobianumerophobia. MIT.
 * Transport-only component: no discovery, module loading or admission policy.
 * A production caller must supply a separately qualified guard and retain this
 * entire object for the provider's lifetime. Owned tests supply owned tables. */
#ifndef CC_MOM020_ADAPTER_H
#define CC_MOM020_ADAPTER_H
#include "vpm_mom.h"
#include "c6_client.h"
typedef int (*CcMom020Guard)(void *,const VpmMomTable *);
typedef struct CcMom020 {
    C6Client client;
    const VpmMomTable *table;
    CcMom020Guard guard;
    void *context;
    VpmMomDescriptor descriptors[C6_CLIENT_ROWS];
} CcMom020;
void cc_mom020_init(CcMom020 *,const VpmMomTable *,CcMom020Guard,void *);
int cc_mom020_register(CcMom020 *,const CrMenuOption *);
int cc_mom020_get(CcMom020 *,const char *,double *);
int cc_mom020_set(CcMom020 *,const char *,double);
#endif
