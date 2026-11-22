/* Released 1.0.5 UMM transport bodies unchanged. See inherited attribution. */
#ifndef CC_MENU_UMM_ADAPTER_H
#define CC_MENU_UMM_ADAPTER_H
#include "menu_client_abi.h"
#include "menu_contract.h"
typedef struct CrMenuAllocation {void *memory;struct CrMenuAllocation *next;} CrMenuAllocation;
typedef struct CrMenuArena {CrWireArena api;CrMenuAllocation *head;} CrMenuArena;
static void *cr_menu_alloc(CrWireArena *api,size_t bytes)
{
    CrMenuArena *a=api->impl;
    CrMenuAllocation *p=malloc(sizeof *p);if(!p)return NULL;
    p->memory=calloc(1,bytes?bytes:16);if(!p->memory){free(p);return NULL;}
    p->next=a->head;a->head=p;return p->memory;
}
static void cr_menu_free(CrMenuArena *a)
{while(a->head){CrMenuAllocation *p=a->head;a->head=p->next;free(p->memory);free(p);}}
static CrWireValue cr_menu_string(const char *s)
{return (CrWireValue){.type=CR_WIRE_STRING,.len=(uint32_t)strlen(s),.u.s=s};}
static CrWireValue cr_menu_number(double n)
{return (CrWireValue){.type=CR_WIRE_NUMBER,.u.n=n};}
static CrWireValue cr_menu_field(CrWireValue table,const char *key)
{
    if(table.type==CR_WIRE_TABLE&&table.u.t)
        for(unsigned i=0;i<table.u.t->count;++i)
        {CrWirePair *p=&table.u.t->pairs[i];if(p->key.type==CR_WIRE_STRING&&p->key.len==strlen(key)&&!memcmp(p->key.u.s,key,p->key.len))return p->value;}
    return (CrWireValue){0};
}
static const CrWireApi *cr_menu_provider;
static const CrWireApi *cr_menu_registration_provider;
static uint64_t cr_menu_instance,cr_menu_generation,cr_menu_epoch;
static uint64_t cr_menu_registration_instance;
enum {CR_MENU_REGISTER,CR_MENU_GET,CR_MENU_SET,CR_MENU_ON_CHANGE,CR_MENU_FUNCTIONS};
static CrWireCallback *cr_menu_functions[CR_MENU_FUNCTIONS];
typedef struct CrMenuRow {
    char id[96];uint32_t type;
    int registered,subscribed;
    CrMenuChanged changed;void *user;
    CrWireCallback callback;
} CrMenuRow;
static CrMenuRow cr_menu_rows[CR_MENU_MAX_OPTIONS];
static unsigned cr_menu_row_count;
static void cr_menu_retain(CrWireCallback *cb){++cb->refs;}
static void cr_menu_release(CrWireCallback *cb){if(cb->refs)--cb->refs;}
static int cr_menu_changed(CrWireCallback *cb,int argc,CrWireValue *argv,CrWireArena *out,CrWireValue *rets,int maxrets,uint32_t flags)
{
    (void)out;(void)rets;(void)maxrets;(void)flags;
    CrMenuRow *r=(CrMenuRow *)cb->user;
    if(argc<2||argv[1].type!=CR_WIRE_STRING||argv[1].len!=strlen(r->id)||memcmp(argv[1].u.s,r->id,argv[1].len))return 0;
    double value;
    if(r->type==CR_MENU_TOGGLE)
    {if(argv[0].type!=CR_WIRE_BOOL)return 0;value=argv[0].u.b!=0;}
    else
    {if(argv[0].type!=CR_WIRE_NUMBER||!isfinite(argv[0].u.n))return 0;value=argv[0].u.n-(r->type==CR_MENU_CHOICE?1:0);}
    if(r->changed)r->changed(r->id,value,r->user);
    return 0;
}
static void cr_menu_disconnect(void)
{
    for(unsigned i=0;i<CR_MENU_FUNCTIONS;++i)
    {if(cr_menu_functions[i])cr_menu_functions[i]->release(cr_menu_functions[i]);cr_menu_functions[i]=NULL;}
    if(cr_menu_instance){++cr_menu_epoch;cr_menu_instance=0;}
    cr_menu_provider=NULL;cr_menu_generation=0;
}
static int cr_menu_connect(void)
{
    const CrWireApi *api=cr_wire_discover();
    if(!api||!api->snapshot||!api->generation){cr_menu_disconnect();return 0;}
    uint64_t generation=api->generation();
    if(api==cr_menu_provider&&cr_menu_instance&&generation==cr_menu_generation)return 1;
    CrMenuArena arena={0};arena.api=(CrWireArena){cr_menu_alloc,&arena};
    CrWireValue fields={0};uint64_t instance=0;
    static const char *const names[]={"register_option","get","set","on_change"};
    CrWireCallback *functions[CR_MENU_FUNCTIONS]={0};int ok=api->snapshot("ModOptionsMenu",&arena.api,&fields,&instance)&&instance;
    for(unsigned i=0;ok&&i<CR_MENU_FUNCTIONS;++i)
    {CrWireValue v=cr_menu_field(fields,names[i]);
     if(v.type!=CR_WIRE_FUNCTION||!v.u.fn||v.u.fn->size<sizeof(CrWireCallback)||!v.u.fn->call||!v.u.fn->retain||!v.u.fn->release)ok=0;
     else functions[i]=v.u.fn;}
    if(ok)
    {
        /* Retain before releasing the old snapshot: a collection may free
         * an export once its last retained reference goes away. */
        for(unsigned i=0;i<CR_MENU_FUNCTIONS;++i)functions[i]->retain(functions[i]);
        if(api!=cr_menu_provider||instance!=cr_menu_instance)++cr_menu_epoch;
        /* A transient lookup/snapshot failure does not remove the provider's
         * existing subscriptions. Reset them only for a different table. */
        if(api!=cr_menu_registration_provider||instance!=cr_menu_registration_instance)
        {
            for(unsigned i=0;i<cr_menu_row_count;++i)cr_menu_rows[i].registered=cr_menu_rows[i].subscribed=0;
            cr_menu_registration_provider=api;cr_menu_registration_instance=instance;
        }
        for(unsigned i=0;i<CR_MENU_FUNCTIONS;++i)
        {if(cr_menu_functions[i])cr_menu_functions[i]->release(cr_menu_functions[i]);cr_menu_functions[i]=functions[i];}
        cr_menu_provider=api;cr_menu_instance=instance;cr_menu_generation=generation;
    }
    cr_menu_free(&arena);if(!ok)cr_menu_disconnect();return ok;
}
static int cr_menu_ready(void){return cr_menu_instance!=0;}
static CrMenuRow *cr_menu_row(const char *id)
{for(unsigned i=0;i<cr_menu_row_count;++i)if(!strcmp(id,cr_menu_rows[i].id))return &cr_menu_rows[i];return NULL;}
static int cr_menu_call(unsigned fn,int argc,CrWireValue *argv,CrWireValue *result)
{
    if(!cr_menu_ready()||fn>=CR_MENU_FUNCTIONS)return 0;
    CrMenuArena arena={0};arena.api=(CrWireArena){cr_menu_alloc,&arena};CrWireValue rets[CR_WIRE_RETURN_SLOTS]={0};
    CrWireCallback *cb=cr_menu_functions[fn];
    int count=cb->call(cb,argc,argv,&arena.api,rets,CR_WIRE_RETURN_SLOTS,0);
    int ok=count>0&&(rets[0].type==CR_WIRE_BOOL||rets[0].type==CR_WIRE_NUMBER);
    if(ok&&result)*result=rets[0];
    if(count<0||(count>1&&rets[0].type==CR_WIRE_BOOL&&!rets[0].u.b))
    {CrWireValue error=rets[count<0?0:1];if(error.type==CR_WIRE_STRING)hf->log(self,"Mod menu: %.*s",(int)error.len,error.u.s);}
    cr_menu_free(&arena);return ok;
}
static CrWireValue cr_menu_value(uint32_t type,double value)
{
    if(type==CR_MENU_TOGGLE)return (CrWireValue){.type=CR_WIRE_BOOL,.u.b=value!=0};
    return cr_menu_number(value+(type==CR_MENU_CHOICE?1:0));
}
static int cr_menu_set(const char *id,double value)
{
    CrMenuRow *r=cr_menu_row(id);if(!r)return 0;
    CrWireValue args[]={cr_menu_string(id),cr_menu_value(r->type,value)},out;
    return cr_menu_call(CR_MENU_SET,2,args,&out)&&out.type==CR_WIRE_BOOL&&out.u.b;
}
static int cr_menu_get(const char *id,double *value)
{
    CrMenuRow *r=cr_menu_row(id);if(!r)return 0;
    CrWireValue arg=cr_menu_string(id),out;
    if(!cr_menu_call(CR_MENU_GET,1,&arg,&out))return 0;
    if(r->type==CR_MENU_TOGGLE){if(out.type!=CR_WIRE_BOOL)return 0;*value=out.u.b!=0;}
    else{if(out.type!=CR_WIRE_NUMBER||!isfinite(out.u.n))return 0;*value=out.u.n-(r->type==CR_MENU_CHOICE?1:0);}
    return 1;
}
static int cr_menu_register(const CrMenuOption *s)
{
    if(!s||s->size!=sizeof *s||!s->id||strlen(s->id)>=96||!s->on_change||!cr_menu_ready())return 0;
    CrMenuRow *r=cr_menu_row(s->id);
    if(!r)
    {
        if(cr_menu_row_count>=CR_MENU_MAX_OPTIONS)return 0;
        r=&cr_menu_rows[cr_menu_row_count++];snprintf(r->id,sizeof r->id,"%s",s->id);r->type=s->type;
        r->callback=(CrWireCallback){.size=sizeof(CrWireCallback),.call=cr_menu_changed,
            .retain=cr_menu_retain,.release=cr_menu_release,.owner=cr_menu_rows,.user=(uintptr_t)r};
    }
    r->changed=s->on_change;r->user=s->user;
    if(!r->registered)
    {
        CrWirePair pairs[11],choices[CR_MENU_MAX_CHOICES];unsigned count=0;
#define CR_MENU_FIELD(k,v) (pairs[count++]=(CrWirePair){cr_menu_string(k),(v)})
        CR_MENU_FIELD("mod",cr_menu_string(s->mod_name));CR_MENU_FIELD("mod_id",cr_menu_string(s->mod_id));
        CR_MENU_FIELD("label",cr_menu_string(s->label));CR_MENU_FIELD("description",cr_menu_string(s->description));
        CR_MENU_FIELD("type",cr_menu_string(s->type==CR_MENU_TOGGLE?"toggle":s->type==CR_MENU_CHOICE?"choice":"slider"));
        CR_MENU_FIELD("default",cr_menu_value(s->type,s->default_value));
        CrWireTable list={.count=s->choice_count,.flags=CR_WIRE_ARRAY,.pairs=choices};
        if(s->type==CR_MENU_CHOICE)
        {
            if(s->choice_count<2||s->choice_count>CR_MENU_MAX_CHOICES)return 0;
            for(unsigned i=0;i<s->choice_count;++i)choices[i]=(CrWirePair){cr_menu_number(i+1),cr_menu_string(s->choices[i])};
            CR_MENU_FIELD("choices",((CrWireValue){.type=CR_WIRE_TABLE,.u.t=&list}));
        }
        if(s->type==CR_MENU_SLIDER)
        {CR_MENU_FIELD("min",cr_menu_number(s->min));CR_MENU_FIELD("max",cr_menu_number(s->max));CR_MENU_FIELD("step",cr_menu_number(s->step));}
        CrWireTable spec={.count=count,.pairs=pairs};
        CrWireValue args[]={cr_menu_string(s->id),{.type=CR_WIRE_TABLE,.u.t=&spec}},out;
        r->registered=cr_menu_call(CR_MENU_REGISTER,2,args,&out)&&out.type==CR_WIRE_BOOL&&out.u.b;
        if(!r->registered)return 0;
#undef CR_MENU_FIELD
    }
    if(!r->subscribed)
    {
        CrWireValue args[]={cr_menu_string(s->id),{.type=CR_WIRE_FUNCTION,.u.fn=&r->callback}},out;
        r->subscribed=cr_menu_call(CR_MENU_ON_CHANGE,2,args,&out)&&out.type==CR_WIRE_BOOL&&out.u.b;
    }
    return r->subscribed;
}
static uint64_t cc_umm_epoch(void){return cr_menu_epoch;}
static const CrMenuApi cc_umm_menu_api={cr_menu_connect,cc_umm_epoch,cr_menu_register,cr_menu_get,cr_menu_set,cr_menu_ready};
#endif
