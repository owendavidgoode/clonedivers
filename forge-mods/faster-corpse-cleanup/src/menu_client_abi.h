/* Health Rework's client-side wire declarations, authored for interoperability.
 * Copyright (c) 2026 enochlophobianumerophobia. MIT; see LICENSE.txt.
 * This is not the provider's header or implementation. Field layout is fixed
 * by the external version-1 binary interface; see CLIENT_INTERFACE.md. */
#ifndef CR_MENU_CLIENT_ABI_H
#define CR_MENU_CLIENT_ABI_H
#include <stdint.h>
#include <stddef.h>
#include <stdlib.h>
#include <string.h>
#ifdef _WIN32
#include <windows.h>
#else
#include <unistd.h>
#endif

#define CR_WIRE_ENV "HD2_UMM_API_v1"
#define CR_WIRE_VERSION 1u
#define CR_WIRE_RETURN_SLOTS 8
#define CR_WIRE_ARRAY 1u
enum { CR_WIRE_NIL=0, CR_WIRE_BOOL=1, CR_WIRE_NUMBER=2,
       CR_WIRE_STRING=3, CR_WIRE_TABLE=4, CR_WIRE_FUNCTION=5 };
typedef struct CrWireValue CrWireValue;
typedef struct CrWirePair CrWirePair;
typedef struct CrWireTable CrWireTable;
typedef struct CrWireArena CrWireArena;
typedef struct CrWireCallback CrWireCallback;

/* Tags and payload widths are protocol facts, not local object ownership. */
struct CrWireValue {
    uint32_t type, len;
    union { int32_t b; double n; const char *s; CrWireTable *t; CrWireCallback *fn; } u;
};
struct CrWirePair { CrWireValue key, value; };
struct CrWireTable { uint32_t count, flags; CrWirePair *pairs; };
struct CrWireArena { void *(*alloc)(CrWireArena *,size_t); void *impl; };
struct CrWireCallback {
    uint32_t size, refs;
    int (*call)(CrWireCallback *,int,CrWireValue *,CrWireArena *,CrWireValue *,int,uint32_t);
    void (*retain)(CrWireCallback *);
    void (*release)(CrWireCallback *);
    const void *owner;
    uintptr_t user;
};
typedef struct CrWireApi {
    uint32_t version, size, pid, extensions;
    int (*snapshot)(const char *,CrWireArena *,CrWireValue *,uint64_t *);
    uint64_t (*generation)(void);
    const char *build;
} CrWireApi;

_Static_assert(sizeof(void *)==8,"The verified menu contract is x64");
_Static_assert(sizeof(CrWireValue)==16 && offsetof(CrWireValue,u)==8,"value wire layout");
_Static_assert(sizeof(CrWirePair)==32 && offsetof(CrWirePair,value)==16,"pair wire layout");
_Static_assert(sizeof(CrWireTable)==16 && offsetof(CrWireTable,pairs)==8,"table wire layout");
_Static_assert(sizeof(CrWireArena)==16 && offsetof(CrWireArena,impl)==8,"arena wire layout");
_Static_assert(sizeof(CrWireCallback)==48 && offsetof(CrWireCallback,call)==8 &&
    offsetof(CrWireCallback,retain)==16 && offsetof(CrWireCallback,release)==24 &&
    offsetof(CrWireCallback,owner)==32 && offsetof(CrWireCallback,user)==40,"callback wire layout");
_Static_assert(sizeof(CrWireApi)==40 && offsetof(CrWireApi,snapshot)==16 &&
    offsetof(CrWireApi,generation)==24 && offsetof(CrWireApi,build)==32,"provider wire layout");

/* Validate only addresses in this process. No target process is opened. */
static int cr_wire_readable(const void *address,size_t bytes)
{
    uintptr_t begin=(uintptr_t)address;
    if(!begin || bytes>UINTPTR_MAX-begin)return 0;
#ifdef _WIN32
    uintptr_t end=begin+bytes;
    while(begin<end) {
        MEMORY_BASIC_INFORMATION region;
        if(!VirtualQuery((const void *)begin,&region,sizeof region) || region.State!=MEM_COMMIT ||
           (region.Protect&(PAGE_GUARD|PAGE_NOACCESS)))return 0;
        DWORD modes=PAGE_READONLY|PAGE_READWRITE|PAGE_WRITECOPY|PAGE_EXECUTE_READ|
            PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY;
        if(!(region.Protect&modes))return 0;
        uintptr_t start=(uintptr_t)region.BaseAddress;
        if(region.RegionSize>UINTPTR_MAX-start)return 0;
        uintptr_t limit=start+region.RegionSize;
        if(begin<start || limit<=begin)return 0;
        begin=limit<end?limit:end;
    }
#endif
    return 1;
}
static int cr_wire_callable(uintptr_t address)
{
    if(!address)return 0;
#ifdef _WIN32
    MEMORY_BASIC_INFORMATION r;
    if(!VirtualQuery((const void *)address,&r,sizeof r) || r.State!=MEM_COMMIT ||
       (r.Protect&(PAGE_GUARD|PAGE_NOACCESS)))return 0;
    return (r.Protect&(PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY))!=0;
#else
    return 1;
#endif
}
static int cr_wire_unsigned(const char *first,const char *last,unsigned radix,uint64_t limit,uint64_t *out)
{
    if(first==last)return 0;
    uint64_t value=0;
    for(const char *p=first;p!=last;++p) {
        unsigned digit;
        if(*p>='0'&&*p<='9')digit=(unsigned)(*p-'0');
        else if(*p>='a'&&*p<='f')digit=(unsigned)(*p-'a')+10;
        else if(*p>='A'&&*p<='F')digit=(unsigned)(*p-'A')+10;
        else return 0;
        if(digit>=radix || value>(limit-digit)/radix)return 0;
        value=value*radix+digit;
    }
    *out=value;return 1;
}
/* The provider owns its process-lifetime descriptor. Refresh and calls occur
 * only on HellForge's game thread, as required by the provider contract. */
static const CrWireApi *cr_wire_discover(void)
{
    char token[64];size_t length;uint32_t self_pid;
#ifdef _WIN32
    DWORD used=GetEnvironmentVariableA(CR_WIRE_ENV,token,(DWORD)sizeof token);
    if(!used || used>=sizeof token)return NULL;
    length=used;self_pid=GetCurrentProcessId();
#else
    const char *source=getenv(CR_WIRE_ENV);
    if(!source)return NULL;
    for(length=0;length<sizeof token && source[length];++length){}
    if(!length || length==sizeof token)return NULL;
    memcpy(token,source,length+1);self_pid=(uint32_t)getpid();
#endif
    const char *separator=memchr(token,':',length);
    if(!separator)return NULL;
    uint64_t owner=0,location=0;
    if(!cr_wire_unsigned(token,separator,10,UINT32_MAX,&owner) || owner!=self_pid ||
       !cr_wire_unsigned(separator+1,token+length,16,UINTPTR_MAX,&location) ||
       !location || location%_Alignof(CrWireApi))return NULL;
    const CrWireApi *api=(const CrWireApi *)(uintptr_t)location;
    if(!cr_wire_readable(api,sizeof *api))return NULL;
    if(api->version<CR_WIRE_VERSION || api->size<sizeof *api || api->pid!=self_pid ||
       !cr_wire_callable((uintptr_t)api->snapshot) || !cr_wire_callable((uintptr_t)api->generation))return NULL;
    return api;
}
#endif
