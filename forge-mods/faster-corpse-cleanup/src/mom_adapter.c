/* Original Corpse client code. Copyright (c) 2026 enochlophobianumerophobia. MIT.
 * Uses the one reviewed MOM declaration, not C6Hypothesis as a foreign ABI.
 * No subscription call, automatic module discovery, or lifetime proof. */
#include "mom_adapter.h"
#include <string.h>
static int allowed(CcMom020 *c)
{return c&&c->table&&c->guard&&c->guard(c->context,c->table);}
static int ready(void *context)
{
 CcMom020 *c=context;
 if(!allowed(c))return 0;
 const VpmMomTable *t=c->table;
 if(t->native_version!=VPM_MOM_NATIVE_VERSION||t->table_bytes!=VPM_MOM_TABLE_BYTES||
    t->opaque_word_08!=VPM_MOM_OPAQUE_WORD_08||t->opaque_word_0c!=VPM_MOM_OPAQUE_WORD_0C||
    !t->register_option||!t->get||!t->set||!t->observed_ready)return 0;
 int observed=t->observed_ready();
 return allowed(c)&&observed==1;
}
static int register_row(void *context,const char *id,const C6SpecHypothesis *s)
{
 CcMom020 *c=context;
 if(!ready(c)||!s||!id||c->client.count>C6_CLIENT_ROWS)return 0;
 unsigned i=0;
 for(;i<c->client.count;i++)if(!strcmp(c->client.rows[i].owned.id,id))break;
 if(i==c->client.count)return 0;
 /* Strings borrow only from the client's permanent owned row or literals.
  * Descriptor itself is permanent too, unlike the old stack-only spec view.
  * Existing C6Client rejects changed duplicate specs before reaching here. */
 VpmMomDescriptor *d=&c->descriptors[i];
 memset(d,0,sizeof *d);
 d->type=s->kind;d->label=s->label;d->mod=s->mod;d->mod_id=s->mod_id;d->description=s->description;
 d->has_default=s->has_default;d->default_value=s->default_value;
 d->has_min=s->has_min;d->has_max=s->has_max;d->has_step=s->has_step;
 d->min=s->min;d->max=s->max;d->step=s->step;d->gap=s->gap;
 const char *error=NULL;
 int result=c->table->register_option(id,d,&error);
 return allowed(c)&&result==1; /* Borrowed error is never read/freed. */
}
static int get_row(void *context,const char *id,double *value)
{
 CcMom020 *c=context;
 if(!ready(c)||!value)return 0;
 double observed=0;
 int result=c->table->get(id,&observed);
 if(!allowed(c)||result!=1)return 0;
 *value=observed;return 1; /* Complete-batch validation remains caller-owned. */
}
static int set_row(void *context,const char *id,double value)
{
 CcMom020 *c=context;
 if(!ready(c))return 0;
 const char *error=NULL;
 int result=c->table->set(id,value,&error);
 return allowed(c)&&result==1;
}
void cc_mom020_init(CcMom020 *c,const VpmMomTable *t,CcMom020Guard g,void *p)
{
 memset(c,0,sizeof *c);c->table=t;c->guard=g;c->context=p;
 c->client.calls=(C6ClientCalls){c,ready,register_row,get_row,set_row};
}
int cc_mom020_register(CcMom020 *c,const CrMenuOption *o)
{return c&&c6_client_register(&c->client,o);}
int cc_mom020_get(CcMom020 *c,const char *id,double *v)
{return c&&c6_client_get(&c->client,id,v);}
int cc_mom020_set(CcMom020 *c,const char *id,double v)
{return c&&c6_client_set(&c->client,id,v);}
