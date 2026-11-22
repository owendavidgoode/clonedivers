/* Corpse-only settings transport core. No foreign callback subscription. MIT. */
#include "c6_client.h"
#include <math.h>
#include <string.h>
static int ready(C6Client *c)
{return c&&c->calls.ready&&c->calls.register_row&&c->calls.get&&c->calls.set&&c->calls.ready(c->calls.context);}
static C6ClientRow *row(C6Client *c,const char *id)
{if(!c||!id||c->count>C6_CLIENT_ROWS)return NULL;for(unsigned i=0;i<c->count;i++)if(!strcmp(c->rows[i].owned.id,id))return &c->rows[i];return NULL;}
static int equal(const C6OwnedOption *a,const C6OwnedOption *b)
{
    return a->type==b->type&&a->default_value==b->default_value&&a->min==b->min&&a->max==b->max&&a->step==b->step&&
      !strcmp(a->id,b->id)&&!strcmp(a->mod_id,b->mod_id)&&!strcmp(a->mod,b->mod)&&!strcmp(a->label,b->label)&&!strcmp(a->description,b->description);
}
static int valid(const C6OwnedOption *o,double v)
{return isfinite(v)&&(o->type==CR_MENU_TOGGLE?(v==0||v==1):(o->type==CR_MENU_SLIDER&&v>=o->min&&v<=o->max));}
int c6_client_register(C6Client *c,const CrMenuOption *option)
{
    C6OwnedOption owned;
    if(!c||!option||(option->type!=CR_MENU_TOGGLE&&option->type!=CR_MENU_SLIDER)||!c6_prepare(option,&owned)||!ready(c))return 0;
    C6ClientRow *r=row(c,owned.id);
    if(r){if(!equal(&r->owned,&owned))return 0;if(r->registered)return 1;}
    else {if(c->count>=C6_CLIENT_ROWS)return 0;r=&c->rows[c->count++];r->owned=owned;}
    C6SpecView view;if(!c6_view(&r->owned,&view))return 0;
    if(!c->calls.register_row(c->calls.context,r->owned.id,&view.spec)||!ready(c))return 0;
    r->registered=1;return 1;
}
int c6_client_get(C6Client *c,const char *id,double *out)
{
    C6ClientRow *r=row(c,id);double value=NAN;
    if(!out||!r||!r->registered||!ready(c)||!c->calls.get(c->calls.context,id,&value)||!ready(c))return 0; /* Caller validates the complete three-field sample. */
    *out=value;return 1;
}
int c6_client_set(C6Client *c,const char *id,double value)
{
    C6ClientRow *r=row(c,id);
    return r&&r->registered&&valid(&r->owned,value)&&ready(c)&&c->calls.set(c->calls.context,id,value)&&ready(c);
}
