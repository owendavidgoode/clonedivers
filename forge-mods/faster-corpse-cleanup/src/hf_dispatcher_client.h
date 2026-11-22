/* Shared Dispatcher Client SDK 0.2.0 candidate.
 * Original MIT client code only. See README.md for startup and lifetime limits.
 * Any implementation macro selects all four layers in this translation unit.
 */
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0600
#elif _WIN32_WINNT < 0x0600
#error "Shared Dispatcher startup requires Windows Vista threadpool APIs or later."
#endif
#if defined(HF_DISPATCHER_CLIENT_IMPLEMENTATION) || defined(HFD_STARTUP_CORE_IMPLEMENTATION) || defined(HFD_SHARED_STARTUP_IMPLEMENTATION) || defined(HFD_AUTOMATIC_STARTUP_IMPLEMENTATION)
#ifndef HF_DISPATCHER_CLIENT_IMPLEMENTATION
#define HF_DISPATCHER_CLIENT_IMPLEMENTATION
#endif
#ifndef HFD_STARTUP_CORE_IMPLEMENTATION
#define HFD_STARTUP_CORE_IMPLEMENTATION
#endif
#ifndef HFD_SHARED_STARTUP_IMPLEMENTATION
#define HFD_SHARED_STARTUP_IMPLEMENTATION
#endif
#ifndef HFD_AUTOMATIC_STARTUP_IMPLEMENTATION
#define HFD_AUTOMATIC_STARTUP_IMPLEMENTATION
#endif
#endif

/* Shared Dispatcher Client SDK 0.2.0, ABI 1.0. Windows x64 / HellForge SDK 15.
 * Copy this file into your mod. Define HF_DISPATCHER_CLIENT_IMPLEMENTATION in
 * exactly ONE C or C++ translation unit before including it. Include normally
 * elsewhere. This contains client code only, never the provider.
 *
 * Keep one fallback-capable, zero-initialized static HfdAdapter per mod.
 * Initialize business state before activate. Keep adapter storage resident for
 * the session; cancel DRAINED releases business context, not adapter storage.
 * BUSY/PENDING are not permission to free, unload or register another route.
 * See README.md for startup ordering, passive states and cancellation.
 */
/*
 * MIT License
 *
 * Copyright (c) 2026 enochlophobianumerophobia
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

#ifndef HF_DISPATCHER_CLIENT_H
#define HF_DISPATCHER_CLIENT_H
#if !defined(_WIN32) || !defined(_WIN64)
#error "Shared Dispatcher Client SDK requires Windows x64."
#endif
#include "hellforge_mod.h"
#include <windows.h>
#define HF_DISPATCHER_CLIENT_VERSION "0.2.0"
#ifdef __cplusplus
extern "C" {
#endif
/* Original Shared Dispatcher API. MIT. Copyright (c) 2026 enochlophobianumerophobia. */
#ifndef HF_DISPATCHER_H
#define HF_DISPATCHER_H
#include "hellforge_mod.h"
#define HFD_MAGIC UINT32_C(0x48464431)
#define HFD_MAJOR 1u
#define HFD_MINOR 0u
#define HFD_CAPACITY 64u
#define HFD_FEATURE_SERIAL UINT32_C(1)
#define HFD_FEATURE_DRAIN UINT32_C(2)
#define HFD_FEATURES (HFD_FEATURE_SERIAL | HFD_FEATURE_DRAIN)
#define HFD_OK 0
#define HFD_UNSUPPORTED 1
#define HFD_INVALID 2
#define HFD_REFUSED 3
#define HFD_PENDING 4
#define HFD_DRAINED 5
#define HFD_STALE 6
#define HFD_UNCERTAIN 7
#define HFD_BUSY 8 /* Bounded atomic attempt lost a race. Cancellation NOT confirmed;
                    * retain context, retry explicitly; never free/fallback on BUSY. */
#define HFD_UNINITIALIZED 0u
#define HFD_REGISTERING 1u
#define HFD_REGISTERED 2u
#define HFD_RUNNING 3u
#define HFD_PROVIDER_REFUSED 4u

typedef struct HfdToken {
    uint64_t session;
    uint64_t generation;
    uint32_t slot;
    uint32_t reserved;
} HfdToken;
/* Caller-owned context stays valid through PENDING or BUSY cancellation. Callback code is pinned by enroll.
 * Enrollment copies this structure, is inactive, and retains nothing on non-OK return. */
typedef struct HfdClient {
    uint32_t size;
    uint32_t reserved;
    HfMod* mod;
    HfTickCallback callback;
    void* user;
} HfdClient;
typedef struct HfdStatus {
    uint32_t size;
    uint32_t state;
    uint32_t active;
    uint32_t capacity;
    uint64_t first_tick; /* 0 or 1: observed HF callback, not a time value. */
    uint64_t reentrant_ticks;
} HfdStatus;
typedef struct HfdApi {
    uint32_t size;
    uint32_t magic;
    uint32_t major;
    uint32_t minor;
    uint32_t pointer_bits;
    uint32_t features;
    uint64_t session;
    int (*enroll)(const HfdClient*, HfdToken*);
    int (*activate)(HfdToken);
    int (*cancel)(HfdToken); /* One bounded atomic transition. BUSY is NOT a drain acknowledgment.
                             * PENDING means cancellation installed with invocation still in flight.
                             * STALE after reuse also proves no old invocation remains, but must
                             * never authorize operations on the replacement enrollment. */
    int (*status)(HfdStatus*);
} HfdApi;
typedef int (*HfdQuery)(uint32_t major, uint32_t minor, uint32_t out_size, HfdApi* out);
/* Query success means ABI availability, not registration or business readiness. Unsupported/invalid
 * queries do not enroll or write output. API, provider and all callable code remain resident until exit.
 * Tokens are process-local identities, not security boundaries. No client may reset/reinitialize provider.
 * Tick/activate/cancel/status contain no waiting lock or retry spin. Query/init/enroll are control-plane
 * operations with OS initialization/residency costs, not frame callback work. Status is sampled telemetry. */
/* Clients resolve HfdQuery dynamically; no provider export is declared here. */
#endif

#ifndef HFD_CLIENT_ADAPTER_H
#define HFD_CLIENT_ADAPTER_H
#define HFD_ROUTE_NEW 0u
#define HFD_ROUTE_CHOOSING 1u
#define HFD_ROUTE_SHARED 2u
#define HFD_ROUTE_DIRECT 3u
#define HFD_ROUTE_INACTIVE 4u
#define HFD_ROUTE_WAITING 5u
#define HFD_REASON_NONE 0u
#define HFD_REASON_ABSENT 1u
#define HFD_REASON_UNSUPPORTED 2u
#define HFD_REASON_REFUSED 3u
#define HFD_REASON_UNCERTAIN 4u
#define HFD_REASON_WAITING 5u
#define HFD_REASON_HF_REFUSED 6u
typedef struct HfdAdapter {
    SRWLOCK lock;
    volatile LONG route, reason, stopped, pending_cancel;
    volatile LONG gate; /* One-shot business lifecycle; no reset/reinitialization. */
    HfdClient client;
    HfdApi provider;
    HfdToken token;
} HfdAdapter;
/* Adapter storage must remain resident for the session, even after cancel drains business context. */
uint32_t hfd_adapter_start(HfdAdapter*, const HfApi*, const HfdClient*);
/* Shared-only admission: never consumes a raw HF callback slot. */
uint32_t hfd_adapter_start_shared(HfdAdapter*, const HfApi*, const HfdClient*);
int hfd_adapter_activate(HfdAdapter*);
int hfd_adapter_cancel(HfdAdapter*);
#endif

#ifdef __cplusplus
}
#endif
#endif /* HF_DISPATCHER_CLIENT_H */

#if defined(HF_DISPATCHER_CLIENT_IMPLEMENTATION) && !defined(HF_DISPATCHER_CLIENT_IMPLEMENTED)
#define HF_DISPATCHER_CLIENT_IMPLEMENTED
#include <tlhelp32.h>
#ifdef __cplusplus
extern "C" {
#endif
/* Original client adapter. MIT. No dispatcher implementation/state. */
enum { HFD_CLIENT_DETAIL_IDLE, HFD_CLIENT_DETAIL_READY, HFD_CLIENT_DETAIL_RUNNING, HFD_CLIENT_DETAIL_CANCEL, HFD_CLIENT_DETAIL_STOPPED };
static LONG hfd_client_detail_load(volatile LONG* at) { return InterlockedCompareExchange(at,0,0); }
static void hfd_client_detail_clear(void* memory,size_t size) {
    volatile unsigned char* bytes=(volatile unsigned char*)memory;
    for(size_t i=0;i<size;++i)bytes[i]=0;
}
static int hfd_client_detail_discover(HfdApi* out) {
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, GetCurrentProcessId());
    if (snapshot == INVALID_HANDLE_VALUE) return HFD_UNCERTAIN;
    MODULEENTRY32W entry; hfd_client_detail_clear(&entry,sizeof entry); entry.dwSize = sizeof entry;
    HMODULE provider = NULL;
    unsigned matches = 0, visited = 0;
    BOOL present = Module32FirstW(snapshot, &entry);
    while (present && visited < 4096) {
        ++visited;
        if (CompareStringOrdinal(entry.szModule, -1, L"hf_shared_dispatcher.dll", -1, TRUE) == CSTR_EQUAL) {
            ++matches; provider = entry.hModule;
        }
        present = Module32NextW(snapshot, &entry);
    }
    DWORD error = GetLastError();
    CloseHandle(snapshot);
    if (present || error != ERROR_NO_MORE_FILES || matches > 1) return HFD_UNCERTAIN;
    if (!matches) return HFD_REFUSED; /* Definitive absence at this observation. */
    HMODULE pinned;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                           (LPCWSTR)(uintptr_t)provider, &pinned)) return HFD_UNCERTAIN;
    HfdQuery query = (HfdQuery)(uintptr_t)GetProcAddress(pinned, "hfd_query");
    if (!query) return HFD_UNSUPPORTED; /* No enrollment API was invoked. */
    int result = query(1, 0, sizeof *out, out);
    if (result == HFD_UNSUPPORTED) return HFD_UNSUPPORTED;
    if (result != HFD_OK || out->size < sizeof *out || out->magic != HFD_MAGIC ||
        out->major != 1 || out->pointer_bits != 64 || (out->features & HFD_FEATURES) != HFD_FEATURES ||
        !out->session || !out->enroll || !out->activate || !out->cancel || !out->status)
        return HFD_UNCERTAIN;
    return HFD_OK;
}
static void hfd_client_detail_trampoline(HfMod* mod, float dt, void* user) {
    HfdAdapter* adapter = (HfdAdapter*)user;
    if (hfd_client_detail_load(&adapter->stopped) ||
        InterlockedCompareExchange(&adapter->gate,HFD_CLIENT_DETAIL_RUNNING,HFD_CLIENT_DETAIL_READY)!=HFD_CLIENT_DETAIL_READY) return;
    HfdClient client = adapter->client;
    client.callback(mod, dt, client.user);
    LONG seen=InterlockedCompareExchange(&adapter->gate,HFD_CLIENT_DETAIL_READY,HFD_CLIENT_DETAIL_RUNNING);
    if(seen==HFD_CLIENT_DETAIL_CANCEL)InterlockedExchange(&adapter->gate,HFD_CLIENT_DETAIL_STOPPED);
}
static uint32_t hfd_client_detail_shared_join(HfdAdapter* adapter) {
    HfdClient bridge; hfd_client_detail_clear(&bridge,sizeof bridge);
    bridge.size = sizeof bridge; bridge.mod = adapter->client.mod;
    bridge.callback = hfd_client_detail_trampoline; bridge.user = adapter;
    int result = adapter->provider.enroll(&bridge, &adapter->token);
    if (result == HFD_OK) return HFD_ROUTE_SHARED;
    if (result == HFD_PENDING) return HFD_ROUTE_WAITING;
    if (result == HFD_REFUSED) {
        InterlockedExchange(&adapter->reason,HFD_REASON_REFUSED);
        return HFD_ROUTE_NEW; /* Proven no enrollment; caller may select direct. */
    }
    InterlockedExchange(&adapter->reason,HFD_REASON_UNCERTAIN);
    return HFD_ROUTE_INACTIVE;
}
static uint32_t hfd_client_detail_start_with_policy(HfdAdapter* adapter, const HfApi* hf, const HfdClient* client, int allow_direct) {
    if (!adapter || !hf || !client || client->size < sizeof *client || client->reserved ||
        !client->mod || !client->callback || hf->version < 15 ||
        hf->size < offsetof(HfApi, on_game_thread) + sizeof hf->on_game_thread ||
        !hf->on_game_thread) return HFD_ROUTE_INACTIVE;
    AcquireSRWLockExclusive(&adapter->lock);
    if (adapter->stopped || (adapter->route != HFD_ROUTE_NEW && adapter->route != HFD_ROUTE_WAITING)) {
        uint32_t route = adapter->route;
        ReleaseSRWLockExclusive(&adapter->lock); return route;
    }
    int retry_shared_only = hfd_client_detail_load(&adapter->route) == HFD_ROUTE_WAITING;
    InterlockedExchange(&adapter->route,HFD_ROUTE_CHOOSING);
    adapter->client = *client;
    /* An uncertain enrollment may already retain the bridge. Pin both callable modules
     * BEFORE invoking any provider API, without relying on its eventual return/pinning. */
    HMODULE trampoline_module, business_module;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                           (LPCWSTR)(uintptr_t)hfd_client_detail_trampoline, &trampoline_module) ||
        !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                           (LPCWSTR)(uintptr_t)client->callback, &business_module)) {
        InterlockedExchange(&adapter->route,HFD_ROUTE_INACTIVE); InterlockedExchange(&adapter->reason,HFD_REASON_UNCERTAIN);
        ReleaseSRWLockExclusive(&adapter->lock); return HFD_ROUTE_INACTIVE;
    }
    /* No function below calls back synchronously into this adapter. Enrollment is inactive. */
    int found = retry_shared_only ? HFD_OK : hfd_client_detail_discover(&adapter->provider);
    uint32_t route = HFD_ROUTE_INACTIVE;
    if (found == HFD_OK) {
        route = hfd_client_detail_shared_join(adapter);
        if (route == HFD_ROUTE_WAITING) InterlockedExchange(&adapter->reason,HFD_REASON_WAITING);
        if (retry_shared_only && route == HFD_ROUTE_NEW) route = HFD_ROUTE_INACTIVE;
    } else if (found == HFD_UNSUPPORTED) {
        InterlockedExchange(&adapter->reason,HFD_REASON_UNSUPPORTED); route = HFD_ROUTE_NEW;
    } else if (found == HFD_REFUSED) {
        InterlockedExchange(&adapter->reason,HFD_REASON_ABSENT); route = HFD_ROUTE_NEW;
    } else InterlockedExchange(&adapter->reason,HFD_REASON_UNCERTAIN);
    /* Multi-channel probes must not multiply the mod's raw HF registrations.
     * Preserve the observed absence/refusal reason when shared-only is passive. */
    if (route == HFD_ROUTE_NEW && !allow_direct) route = HFD_ROUTE_INACTIVE;
    if (route == HFD_ROUTE_NEW) {
        HMODULE pinned;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                               (LPCWSTR)(uintptr_t)hfd_client_detail_trampoline, &pinned)) {
            route = HFD_ROUTE_INACTIVE; InterlockedExchange(&adapter->reason,HFD_REASON_UNCERTAIN);
        } else {
            /* Publish final route only after the HF call returns. The hfd_client_detail_trampoline starts inactive.
             * Unlock during the external call so a synchronous/concurrent callback cannot deadlock. */
            ReleaseSRWLockExclusive(&adapter->lock);
            int admitted = hf->on_game_thread(client->mod, hfd_client_detail_trampoline, adapter);
            AcquireSRWLockExclusive(&adapter->lock);
            route = admitted == 1 ? HFD_ROUTE_DIRECT : HFD_ROUTE_INACTIVE;
            if (admitted != 1) InterlockedExchange(&adapter->reason,admitted == 0 ? HFD_REASON_HF_REFUSED : HFD_REASON_UNCERTAIN);
        }
    }
    InterlockedExchange(&adapter->route,route);
    if (hf->log) hf->log(client->mod, "%s reason=%u ready=0",
        route == HFD_ROUTE_SHARED ? "SHARED" : route == HFD_ROUTE_DIRECT ? "DIRECT_FALLBACK" : "INACTIVE",
        adapter->reason);
    ReleaseSRWLockExclusive(&adapter->lock);
    return route;
}
uint32_t hfd_adapter_start(HfdAdapter* adapter, const HfApi* hf, const HfdClient* client) {
    return hfd_client_detail_start_with_policy(adapter,hf,client,1);
}
uint32_t hfd_adapter_start_shared(HfdAdapter* adapter, const HfApi* hf, const HfdClient* client) {
    return hfd_client_detail_start_with_policy(adapter,hf,client,0);
}
int hfd_adapter_activate(HfdAdapter* adapter) {
    if (!adapter) return HFD_INVALID;
    if(hfd_client_detail_load(&adapter->stopped))return HFD_REFUSED;
    LONG route=hfd_client_detail_load(&adapter->route);
    int result=route==HFD_ROUTE_DIRECT?HFD_OK:route==HFD_ROUTE_SHARED?
        adapter->provider.activate(adapter->token):HFD_REFUSED;
    if(result!=HFD_OK)return result;
    LONG seen=InterlockedCompareExchange(&adapter->gate,HFD_CLIENT_DETAIL_READY,HFD_CLIENT_DETAIL_IDLE);
    return seen==HFD_CLIENT_DETAIL_IDLE||seen==HFD_CLIENT_DETAIL_READY||seen==HFD_CLIENT_DETAIL_RUNNING?HFD_OK:HFD_REFUSED;
}
int hfd_adapter_cancel(HfdAdapter* adapter) {
    if (!adapter) return HFD_INVALID;
    InterlockedExchange(&adapter->stopped,1);
    LONG old=hfd_client_detail_load(&adapter->gate);
    int own=HFD_DRAINED;
    if(old==HFD_CLIENT_DETAIL_CANCEL)own=HFD_PENDING;
    else if(old!=HFD_CLIENT_DETAIL_STOPPED){
        LONG target=old==HFD_CLIENT_DETAIL_RUNNING?HFD_CLIENT_DETAIL_CANCEL:HFD_CLIENT_DETAIL_STOPPED;
        LONG seen=InterlockedCompareExchange(&adapter->gate,target,old);
        if(seen==old)own=target==HFD_CLIENT_DETAIL_CANCEL?HFD_PENDING:HFD_DRAINED;
        else own=seen==HFD_CLIENT_DETAIL_STOPPED?HFD_DRAINED:seen==HFD_CLIENT_DETAIL_CANCEL?HFD_PENDING:HFD_BUSY;
    }
    LONG route=hfd_client_detail_load(&adapter->route);
    int provider=HFD_DRAINED;
    if(route==HFD_ROUTE_CHOOSING)provider=HFD_PENDING;
    else if(route==HFD_ROUTE_SHARED){
        provider=adapter->provider.cancel(adapter->token);
        if(provider==HFD_STALE&&hfd_client_detail_load(&adapter->pending_cancel))provider=HFD_DRAINED;
        if(provider==HFD_PENDING)InterlockedExchange(&adapter->pending_cancel,1);
    }
    if(own==HFD_BUSY||provider==HFD_BUSY)return HFD_BUSY;
    if(own==HFD_PENDING||provider==HFD_PENDING)return HFD_PENDING;
    return provider;
}

#ifdef __cplusplus
}
#endif
#endif /* implementation */


/* Original internal recovery pump candidate. MIT, same copyright/license as
 * the original SDK; MIT license included above. Candidate startup controller.
 * See README.md. One zero-initialized static object per fallback-capable mod;
 * preserve storage/code for the session. Never mix legacy adapter operations
 * with this controller. Injected discovery must supply resident/pinned code.
 */
#if defined(HFD_STARTUP_CORE_IMPLEMENTATION)
#ifndef HF_DISPATCHER_CLIENT_IMPLEMENTATION
#define HF_DISPATCHER_CLIENT_IMPLEMENTATION
#endif
#endif
#ifndef HFD_STARTUP_CORE_H
#define HFD_STARTUP_CORE_H
#ifdef __cplusplus
extern "C" {
#endif
#define HFD_REC_ARMED 0
#define HFD_REC_RECOVERING 1
#define HFD_REC_RESERVED 2
#define HFD_REC_DIRECT_PENDING 3
#define HFD_REC_SHARED 4
#define HFD_REC_DIRECT 5
#define HFD_REC_INACTIVE 6
#define HFD_REC_QUARANTINED 7
#define HFD_REC_STOPPED 8
typedef struct HfdRecoveryOps {
    int (*discover)(HfdApi*,void*);
    ULONGLONG (*now_ms)(void*);
    void* user;
} HfdRecoveryOps;
typedef struct HfdRecoveryLimits {
    /* raw_attempts==0 is a shared-only control pump. It never calls any HF
     * registration API, including after safe absence/refusal or deadline. */
    uint32_t shared_attempts,raw_attempts,activation_attempts;
    ULONGLONG shared_window_ms,total_window_ms;
} HfdRecoveryLimits;
typedef struct HfdRecovery {
    HfdAdapter adapter;
    volatile LONG initialized,controller,phase,ready,stopped,owned,uncertain;
    const HfApi* hf;
    HfdRecoveryOps ops;
    HfdRecoveryLimits limits;
    ULONGLONG started_ms;
    volatile LONG shared_tries,raw_tries,activation_tries;
    LONG begun,api_ready; /* Access only while owning controller. */
} HfdRecovery;
int hfd_recovery_init(HfdRecovery*,const HfApi*,const HfdClient*,const HfdRecoveryOps*,const HfdRecoveryLimits*);
/* Caller supplies a separately qualified control trigger, never a frame tick.
 * This pump provides no automatic wake, event subscription or timer binding. */
int hfd_recovery_step(HfdRecovery*);
int hfd_recovery_ready(HfdRecovery*);
int hfd_recovery_cancel(HfdRecovery*);
#ifdef __cplusplus
}
#endif
#endif

#if defined(HFD_STARTUP_CORE_IMPLEMENTATION) && !defined(HFD_STARTUP_CORE_IMPLEMENTED)
#define HFD_STARTUP_CORE_IMPLEMENTED
#ifdef __cplusplus
extern "C" {
#endif
static LONG hfd_recovery_detail_load(volatile LONG* at) {
    return InterlockedCompareExchange(at,0,0);
}
static void hfd_recovery_detail_copy(void* destination,const void* source,size_t size) {
    volatile unsigned char* to=(volatile unsigned char*)destination;
    const unsigned char* from=(const unsigned char*)source;
    /* Keep the standalone client free of compiler-generated CRT copy calls. */
    for(size_t i=0;i<size;++i)to[i]=from[i];
}
static int hfd_recovery_detail_close_gate(HfdRecovery* state) {
    HfdAdapter* a=&state->adapter;
    InterlockedExchange(&a->stopped,1);
    LONG old=hfd_recovery_detail_load(&a->gate);
    if(old==HFD_CLIENT_DETAIL_STOPPED)return HFD_DRAINED;
    if(old==HFD_CLIENT_DETAIL_CANCEL)return HFD_PENDING;
    LONG target=old==HFD_CLIENT_DETAIL_RUNNING?HFD_CLIENT_DETAIL_CANCEL:HFD_CLIENT_DETAIL_STOPPED;
    LONG seen=InterlockedCompareExchange(&a->gate,target,old);
    if(seen==old)return target==HFD_CLIENT_DETAIL_CANCEL?HFD_PENDING:HFD_DRAINED;
    return seen==HFD_CLIENT_DETAIL_STOPPED?HFD_DRAINED:seen==HFD_CLIENT_DETAIL_CANCEL?HFD_PENDING:HFD_BUSY;
}
static void hfd_recovery_detail_open_gate(HfdRecovery* state) {
    if(hfd_recovery_detail_load(&state->stopped)||!hfd_recovery_detail_load(&state->ready))return;
    LONG phase=hfd_recovery_detail_load(&state->phase);
    if(phase!=HFD_REC_SHARED&&phase!=HFD_REC_DIRECT)return;
    /* Stop closes this one-shot gate permanently, including a concurrent open. */
    (void)InterlockedCompareExchange(&state->adapter.gate,HFD_CLIENT_DETAIL_READY,HFD_CLIENT_DETAIL_IDLE);
}
static int hfd_recovery_detail_api_valid(const HfdApi* api) {
    return api->size>=sizeof *api&&api->magic==HFD_MAGIC&&api->major==HFD_MAJOR&&
        api->pointer_bits==64&&(api->features&HFD_FEATURES)==HFD_FEATURES&&
        api->session&&api->enroll&&api->activate&&api->cancel&&api->status;
}
static int hfd_recovery_detail_token_valid(HfdRecovery* state) {
    HfdToken token=state->adapter.token;
    return token.session==state->adapter.provider.session&&token.generation&&
        token.slot<HFD_CAPACITY&&!token.reserved;
}
static int hfd_recovery_detail_quarantine(HfdRecovery* state,int unknown_admission) {
    if(unknown_admission)InterlockedExchange(&state->uncertain,1);
    InterlockedExchange(&state->adapter.reason,HFD_REASON_UNCERTAIN);
    InterlockedExchange(&state->adapter.route,HFD_ROUTE_INACTIVE);
    InterlockedExchange(&state->phase,HFD_REC_QUARANTINED);
    return HFD_UNCERTAIN;
}
static int hfd_recovery_detail_inactive(HfdRecovery* state) {
    InterlockedExchange(&state->adapter.route,HFD_ROUTE_INACTIVE);
    InterlockedExchange(&state->phase,HFD_REC_INACTIVE);
    return HFD_REFUSED;
}
/* Controller owned. Unknown admission has no usable cancellation identity.
 * Activation failures remain cancellable through the already validated token. */
static int hfd_recovery_detail_drain(HfdRecovery* state) {
    int own=hfd_recovery_detail_close_gate(state);
    if(hfd_recovery_detail_load(&state->uncertain))return HFD_UNCERTAIN;
    int provider=HFD_DRAINED;
    if(hfd_recovery_detail_load(&state->owned)) {
        provider=state->adapter.provider.cancel(state->adapter.token);
        if(provider==HFD_DRAINED||provider==HFD_STALE) {
            /* A known, validated generation has no remaining invocation. */
            InterlockedExchange(&state->owned,0);provider=HFD_DRAINED;
        } else if(provider!=HFD_PENDING&&provider!=HFD_BUSY) {
            return hfd_recovery_detail_quarantine(state,1);
        }
    }
    if(own==HFD_BUSY||provider==HFD_BUSY)return HFD_BUSY;
    if(own==HFD_PENDING||provider==HFD_PENDING)return HFD_PENDING;
    InterlockedExchange(&state->phase,HFD_REC_STOPPED);
    return HFD_DRAINED;
}
int hfd_recovery_init(HfdRecovery* state,const HfApi* hf,const HfdClient* client,
                      const HfdRecoveryOps* ops,const HfdRecoveryLimits* limits) {
    if(!state||!hf||!client||!ops||!limits||client->size<sizeof *client||client->reserved||
       !client->mod||!client->callback||!ops->discover||!ops->now_ms||hf->version<15||
       hf->size<offsetof(HfApi,on_game_thread)+sizeof hf->on_game_thread||(!hf->on_game_thread&&limits->raw_attempts)||
       !limits->shared_attempts||limits->shared_attempts>64||limits->raw_attempts>64||
       !limits->activation_attempts||limits->activation_attempts>64||!limits->shared_window_ms||
       limits->shared_window_ms>limits->total_window_ms||limits->total_window_ms>30000)return HFD_INVALID;
    if(InterlockedCompareExchange(&state->controller,1,0))return HFD_BUSY;
    int result=HFD_REFUSED;
    if(!hfd_recovery_detail_load(&state->stopped)&&!hfd_recovery_detail_load(&state->initialized)) {
        InterlockedExchange(&state->initialized,1);
        state->hf=hf;state->adapter.client=*client;state->ops=*ops;state->limits=*limits;
        HMODULE bridge_module,business_module;
        if(GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
                             (LPCWSTR)(uintptr_t)hfd_client_detail_trampoline,&bridge_module)&&
           GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
                             (LPCWSTR)(uintptr_t)client->callback,&business_module)) {
            InterlockedExchange(&state->phase,HFD_REC_ARMED);result=HFD_OK;
        } else (void)hfd_recovery_detail_inactive(state);
        InterlockedExchange(&state->initialized,2);
    }
    if(hfd_recovery_detail_load(&state->stopped))result=hfd_recovery_detail_drain(state)==HFD_DRAINED?HFD_REFUSED:HFD_PENDING;
    InterlockedExchange(&state->controller,0);return result;
}
int hfd_recovery_ready(HfdRecovery* state) {
    if(!state)return HFD_INVALID;
    if(hfd_recovery_detail_load(&state->stopped))return HFD_REFUSED;
    InterlockedExchange(&state->ready,1);
    hfd_recovery_detail_open_gate(state);
    LONG phase=hfd_recovery_detail_load(&state->phase);
    return phase==HFD_REC_SHARED||phase==HFD_REC_DIRECT?HFD_OK:
        phase==HFD_REC_QUARANTINED?HFD_UNCERTAIN:phase==HFD_REC_INACTIVE?HFD_REFUSED:HFD_PENDING;
}
static int hfd_recovery_detail_step_owned(HfdRecovery* state) {
    if(hfd_recovery_detail_load(&state->stopped))return hfd_recovery_detail_drain(state);
    LONG phase=hfd_recovery_detail_load(&state->phase);
    if(phase==HFD_REC_SHARED||phase==HFD_REC_DIRECT)return HFD_OK;
    if(phase==HFD_REC_QUARANTINED)return HFD_UNCERTAIN;
    if(phase==HFD_REC_INACTIVE||phase==HFD_REC_STOPPED)return HFD_REFUSED;
    ULONGLONG now=state->ops.now_ms(state->ops.user);
    if(!state->begun){state->started_ms=now;state->begun=1;}
    ULONGLONG elapsed=now-state->started_ms;
    if(elapsed>=state->limits.total_window_ms) {
        return state->owned?hfd_recovery_detail_quarantine(state,0):hfd_recovery_detail_inactive(state);
    }
    if(phase==HFD_REC_RESERVED) {
        if((uint32_t)state->activation_tries>=state->limits.activation_attempts)return hfd_recovery_detail_quarantine(state,0);
        InterlockedIncrement(&state->activation_tries);
        int result=state->adapter.provider.activate(state->adapter.token);
        if(result==HFD_OK) {
            InterlockedExchange(&state->adapter.route,HFD_ROUTE_SHARED);
            InterlockedExchange(&state->adapter.reason,HFD_REASON_NONE);
            InterlockedExchange(&state->phase,HFD_REC_SHARED);
            hfd_recovery_detail_open_gate(state);return HFD_OK;
        }
        if((result==HFD_BUSY||result==HFD_PENDING)&&
           (uint32_t)state->activation_tries<state->limits.activation_attempts)return HFD_PENDING;
        return hfd_recovery_detail_quarantine(state,0);
    }
    if(phase==HFD_REC_DIRECT_PENDING) {
        if((uint32_t)state->raw_tries>=state->limits.raw_attempts)return hfd_recovery_detail_inactive(state);
        InterlockedIncrement(&state->raw_tries);
        int result=state->hf->on_game_thread(state->adapter.client.mod,hfd_client_detail_trampoline,&state->adapter);
        if(result==1) {
            InterlockedExchange(&state->adapter.route,HFD_ROUTE_DIRECT);
            InterlockedExchange(&state->phase,HFD_REC_DIRECT);
            hfd_recovery_detail_open_gate(state);return HFD_OK;
        }
        if(result!=0)return hfd_recovery_detail_quarantine(state,1);
        InterlockedExchange(&state->adapter.reason,HFD_REASON_HF_REFUSED);
        return (uint32_t)state->raw_tries<state->limits.raw_attempts?HFD_PENDING:hfd_recovery_detail_inactive(state);
    }
    if(elapsed>=state->limits.shared_window_ms||(uint32_t)state->shared_tries>=state->limits.shared_attempts) {
        if(!state->limits.raw_attempts)return hfd_recovery_detail_inactive(state);
        InterlockedExchange(&state->phase,HFD_REC_DIRECT_PENDING);return HFD_PENDING;
    }
    InterlockedExchange(&state->phase,HFD_REC_RECOVERING);
    InterlockedExchange(&state->adapter.route,HFD_ROUTE_WAITING);
    InterlockedIncrement(&state->shared_tries);
    int result=HFD_OK;
    if(!state->api_ready) {
        HfdApi api;hfd_client_detail_clear(&api,sizeof api);
        result=state->ops.discover(&api,state->ops.user);
        if(result==HFD_OK) {
            if(!hfd_recovery_detail_api_valid(&api))return hfd_recovery_detail_quarantine(state,1);
            hfd_recovery_detail_copy(&state->adapter.provider,&api,sizeof api);state->api_ready=1;
        } else if(result!=HFD_REFUSED&&result!=HFD_UNSUPPORTED)return hfd_recovery_detail_quarantine(state,1);
        else InterlockedExchange(&state->adapter.reason,result==HFD_REFUSED?HFD_REASON_ABSENT:HFD_REASON_UNSUPPORTED);
    }
    if(result==HFD_OK) {
        /* Never pass business user directly across the retained API boundary. */
        HfdClient bridge;hfd_client_detail_clear(&bridge,sizeof bridge);
        bridge.size=sizeof bridge;bridge.mod=state->adapter.client.mod;
        bridge.callback=hfd_client_detail_trampoline;bridge.user=&state->adapter;
        HfdToken token;hfd_client_detail_clear(&token,sizeof token);
        result=state->adapter.provider.enroll(&bridge,&token);
        if(result==HFD_OK) {
            state->adapter.token=token;InterlockedExchange(&state->owned,1);
            if(!hfd_recovery_detail_token_valid(state))return hfd_recovery_detail_quarantine(state,1);
            InterlockedExchange(&state->phase,HFD_REC_RESERVED);return HFD_PENDING;
        }
        if(result!=HFD_PENDING&&result!=HFD_REFUSED)return hfd_recovery_detail_quarantine(state,1);
        InterlockedExchange(&state->adapter.reason,result==HFD_PENDING?HFD_REASON_WAITING:HFD_REASON_REFUSED);
    }
    if((uint32_t)state->shared_tries>=state->limits.shared_attempts) {
        if(!state->limits.raw_attempts)return hfd_recovery_detail_inactive(state);
        InterlockedExchange(&state->phase,HFD_REC_DIRECT_PENDING);
    }
    return HFD_PENDING;
}
int hfd_recovery_step(HfdRecovery* state) {
    if(!state)return HFD_INVALID;
    if(InterlockedCompareExchange(&state->controller,1,0))return HFD_BUSY;
    int result=hfd_recovery_detail_load(&state->initialized)==2?hfd_recovery_detail_step_owned(state):HFD_INVALID;
    /* A late external return cannot lose a concurrently latched stop/token. */
    if(hfd_recovery_detail_load(&state->stopped))result=hfd_recovery_detail_drain(state);
    InterlockedExchange(&state->controller,0);return result;
}
int hfd_recovery_cancel(HfdRecovery* state) {
    if(!state)return HFD_INVALID;
    InterlockedExchange(&state->stopped,1);
    int own=hfd_recovery_detail_close_gate(state);
    if(InterlockedCompareExchange(&state->controller,1,0))return own==HFD_BUSY?HFD_BUSY:HFD_PENDING;
    int result=hfd_recovery_detail_drain(state);
    InterlockedExchange(&state->controller,0);return result;
}
#ifdef __cplusplus
}
#endif
#endif


/* Original MIT automatic SHARED-ONLY startup candidate. Not released.
 * Copyright (c) 2026 enochlophobianumerophobia. MIT license included above.
 * Call begin from mod_init BEFORE installing this mod's concurrent callbacks.
 * No raw HF registration or HF logging occurs during recovery. Keep this
 * static storage/code resident for the session, including after DRAINED.
 * This does not implement the separate initialization-time fallback policy.
 */
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0600
#elif _WIN32_WINNT < 0x0600
#error "Shared startup requires Windows threadpool APIs (Vista or later)."
#endif
#if defined(HFD_SHARED_STARTUP_IMPLEMENTATION)
#define HFD_STARTUP_CORE_IMPLEMENTATION
#endif
#ifndef HFD_SHARED_STARTUP_H
#define HFD_SHARED_STARTUP_H
#ifdef __cplusplus
extern "C" {
#endif
#define HFD_STARTUP_THREAD_CAPACITY 16
typedef struct HfdSharedStartup {
    HfdRecovery core;
    volatile LONG initialized,actor,event_seen,stopped,timer_allocations,timer_wakes;
    PTP_TIMER timer; /* Access only by actor owner. Close is asynchronous. */
    DWORD interval_ms;
    volatile LONG timer_signal,fault;
    volatile LONG initial_control_thread,timer_callbacks,timer_thread_overflow;
    volatile LONG timer_threads[HFD_STARTUP_THREAD_CAPACITY];
} HfdSharedStartup;
/* HFD_OK means one load-event listener is armed, not provider admission/tick.
 * limits.raw_attempts must be zero. No new event is registered by a worker. */
int hfd_shared_startup_begin(HfdSharedStartup*,const HfApi*,const HfdClient*,const HfdRecoveryLimits*,DWORD);
int hfd_shared_startup_ready(HfdSharedStartup*);
int hfd_shared_startup_cancel(HfdSharedStartup*);
#ifdef __cplusplus
}
#endif
#endif

#if defined(HFD_SHARED_STARTUP_IMPLEMENTATION) && !defined(HFD_SHARED_STARTUP_IMPLEMENTED)
#define HFD_SHARED_STARTUP_IMPLEMENTED
#ifdef __cplusplus
extern "C" {
#endif
static int hfd_shared_detail_discover(HfdApi* api,void* user) {
    (void)user;return hfd_client_detail_discover(api);
}
static ULONGLONG hfd_shared_detail_now(void* user) {
    (void)user;return GetTickCount64();
}
static int hfd_shared_detail_terminal(HfdSharedStartup* state) {
    LONG p=hfd_recovery_detail_load(&state->core.phase);
    return p==HFD_REC_SHARED||p==HFD_REC_DIRECT||p==HFD_REC_INACTIVE||
        p==HFD_REC_QUARANTINED||p==HFD_REC_STOPPED;
}
static void hfd_shared_detail_close_timer(HfdSharedStartup* state) {
    PTP_TIMER timer=state->timer;
    state->timer=NULL;
    if(timer) {
        SetThreadpoolTimer(timer,NULL,0,0);
        /* No blocking wait. Already queued callbacks may still touch this
         * pinned static tombstone; they cannot rearm or reopen business. */
        CloseThreadpoolTimer(timer);
    }
}
static void hfd_shared_detail_run(HfdSharedStartup*,int);
static void hfd_shared_detail_observe_timer(HfdSharedStartup* state) {
    LONG id=(LONG)GetCurrentThreadId();
    InterlockedIncrement(&state->timer_callbacks);
    /* Monotonic bounded union. Numeric IDs are historical observations, not
     * live OS-thread counts or exclusive ownership; IDs may be reused. */
    for(unsigned i=0;i<HFD_STARTUP_THREAD_CAPACITY;++i) {
        LONG seen=hfd_recovery_detail_load(&state->timer_threads[i]);
        if(seen==id)return;
        if(!seen) {
            seen=InterlockedCompareExchange(&state->timer_threads[i],id,0);
            if(!seen||seen==id)return;
        }
    }
    InterlockedExchange(&state->timer_thread_overflow,1);
}
static VOID CALLBACK hfd_shared_detail_timer(PTP_CALLBACK_INSTANCE instance,void* user,PTP_TIMER timer) {
    (void)instance;(void)timer;
    HfdSharedStartup* state=(HfdSharedStartup*)user;
    /* Even ignored queued callbacks are real pool work. Only pinned static
     * telemetry is touched after stop, never the released business context. */
    hfd_shared_detail_observe_timer(state);
    hfd_shared_detail_run(state,1);
}
static void hfd_shared_detail_process(HfdSharedStartup* state) {
    LONG wake=InterlockedExchange(&state->timer_signal,0);
    if(hfd_recovery_detail_load(&state->stopped)) {
        (void)hfd_recovery_cancel(&state->core);
        hfd_shared_detail_close_timer(state);return;
    }
    if(hfd_recovery_detail_load(&state->initialized)!=2||
       !hfd_recovery_detail_load(&state->event_seen))return;
    if(hfd_shared_detail_terminal(state)) {hfd_shared_detail_close_timer(state);return;}
    /* Counts consumed timer notifications, coalesced while another controller
     * owns the actor. Ignored late callbacks are not new recovery attempts. */
    if(wake)InterlockedIncrement(&state->timer_wakes);
    int result=hfd_recovery_step(&state->core);
    /* Two distinct bounded API calls on the ready-provider fast path. Avoid
     * creating a pool timer just to activate the successfully owned token. */
    if(result==HFD_PENDING&&hfd_recovery_detail_load(&state->core.phase)==HFD_REC_RESERVED)
        result=hfd_recovery_step(&state->core);
    if(hfd_recovery_detail_load(&state->stopped)||hfd_shared_detail_terminal(state)) {
        if(hfd_recovery_detail_load(&state->stopped))(void)hfd_recovery_cancel(&state->core);
        hfd_shared_detail_close_timer(state);return;
    }
    if(result!=HFD_PENDING&&result!=HFD_BUSY) {
        InterlockedExchange(&state->fault,result);
        InterlockedExchange(&state->stopped,1);
        (void)hfd_recovery_cancel(&state->core);
        hfd_shared_detail_close_timer(state);return;
    }
    if(!state->timer) {
        state->timer=CreateThreadpoolTimer(hfd_shared_detail_timer,state,NULL);
        if(!state->timer) {
            InterlockedExchange(&state->fault,HFD_REFUSED);
            InterlockedExchange(&state->stopped,1);
            (void)hfd_recovery_cancel(&state->core);return;
        }
        InterlockedIncrement(&state->timer_allocations);
    }
    LARGE_INTEGER relative;relative.QuadPart=-(LONGLONG)state->interval_ms*10000;
    FILETIME due;due.dwLowDateTime=relative.LowPart;due.dwHighDateTime=(DWORD)relative.HighPart;
    SetThreadpoolTimer(state->timer,&due,0,0); /* One shot, never periodic. */
}
static void hfd_shared_detail_owned(HfdSharedStartup* state) {
    for(;;) {
        /* A producer finding actor==1 changes it to2. Consume that signal
         * BEFORE processing; atomic release cannot lose a later producer. */
        (void)InterlockedCompareExchange(&state->actor,1,2);
        hfd_shared_detail_process(state);
        if(InterlockedCompareExchange(&state->actor,0,1)==1)return;
        /* Only the single event and one-shot timer produce work. Core attempt
         * caps/deadline terminate it; repeated events are rejected earlier. */
    }
}
static void hfd_shared_detail_run(HfdSharedStartup* state,int timer_signal) {
    if(hfd_recovery_detail_load(&state->stopped)||hfd_shared_detail_terminal(state))return;
    if(timer_signal)InterlockedExchange(&state->timer_signal,1);
    LONG old=InterlockedCompareExchange(&state->actor,1,0);
    if(!old){hfd_shared_detail_owned(state);return;}
    /* One atomic retry after a release race, never a waiting spin. */
    if(old==1) {
        old=InterlockedCompareExchange(&state->actor,2,1);
        if(!old&&InterlockedCompareExchange(&state->actor,1,0)==0)
            hfd_shared_detail_owned(state);
    }
}
static void hfd_shared_detail_event(HfMod* mod,HfEvent event,void* user) {
    HfdSharedStartup* state=(HfdSharedStartup*)user;
    if(event!=HF_EVENT_GAME_LOADED||mod!=state->core.adapter.client.mod||
       hfd_recovery_detail_load(&state->stopped))return;
    if(InterlockedCompareExchange(&state->event_seen,1,0))return;
    InterlockedExchange(&state->initial_control_thread,(LONG)GetCurrentThreadId());
    hfd_shared_detail_run(state,0);
}
int hfd_shared_startup_begin(HfdSharedStartup* state,const HfApi* hf,const HfdClient* client,
                             const HfdRecoveryLimits* limits,DWORD interval) {
    if(!state)return HFD_INVALID;
    /* Cover the entire initializer, including caller-argument reads, before
     * publishing initialized. A completed stop returns without reading them. */
    if(InterlockedCompareExchange(&state->actor,1,0))return HFD_BUSY;
    int result=HFD_REFUSED;
    if(!hfd_recovery_detail_load(&state->stopped)&&!hfd_recovery_detail_load(&state->initialized)) {
        if(!hf||!client||!limits||!hf->on||limits->raw_attempts||!interval||interval>1000)
            result=HFD_INVALID;
        else if(!InterlockedCompareExchange(&state->initialized,1,0)) {
            state->interval_ms=interval;
            HfdRecoveryOps ops={hfd_shared_detail_discover,hfd_shared_detail_now,NULL};
            result=hfd_recovery_init(&state->core,hf,client,&ops,limits);
            if(result==HFD_OK&&!hfd_recovery_detail_load(&state->stopped)) {
                /* State and code pins are ready BEFORE on(), which may call back now.
                 * actor holds it pending until the registration result is known. */
                result=hf->on(client->mod,HF_EVENT_GAME_LOADED,hfd_shared_detail_event,state);
                if(result==0){InterlockedExchange(&state->initialized,2);result=HFD_OK;}
                else result=result==1?HFD_REFUSED:HFD_UNCERTAIN;
            }
            if(result!=HFD_OK) {
                InterlockedExchange(&state->fault,result);InterlockedExchange(&state->stopped,1);
                (void)hfd_recovery_cancel(&state->core);
            }
        }
    }
    hfd_shared_detail_owned(state);
    return hfd_recovery_detail_load(&state->stopped)?(result==HFD_OK?HFD_REFUSED:result):result;
}
int hfd_shared_startup_ready(HfdSharedStartup* state) {
    if(!state)return HFD_INVALID;
    if(hfd_recovery_detail_load(&state->stopped))return HFD_REFUSED;
    return hfd_recovery_ready(&state->core);
}
int hfd_shared_startup_cancel(HfdSharedStartup* state) {
    if(!state)return HFD_INVALID;
    InterlockedExchange(&state->stopped,1);
    int result=hfd_recovery_cancel(&state->core);
    if(InterlockedCompareExchange(&state->actor,1,0))return result==HFD_BUSY?HFD_BUSY:HFD_PENDING;
    hfd_shared_detail_close_timer(state);
    (void)InterlockedExchange(&state->timer_signal,0);
    InterlockedExchange(&state->actor,0);
    return result;
}
#ifdef __cplusplus
}
#endif
#endif


/* Original MIT initialization-roster startup candidate. Not released/accepted.
 * Copyright (c) 2026 enochlophobianumerophobia. MIT license included above.
 * Call begin from hellforge_mod_init, before this mod installs concurrent
 * callbacks/hooks. Keep state and code resident for the session. DRAINED
 * releases business context only. Never mix operations on embedded adapters.
 * Listed but unusable provider stays passive; there is no late HF fallback.
 */
#if defined(HFD_AUTOMATIC_STARTUP_IMPLEMENTATION)
#define HFD_SHARED_STARTUP_IMPLEMENTATION
#endif
#ifndef HFD_AUTOMATIC_STARTUP_H
#define HFD_AUTOMATIC_STARTUP_H
#ifdef __cplusplus
extern "C" {
#endif
#define HFD_PLAN_UNKNOWN 0
#define HFD_PLAN_ABSENT 1
#define HFD_PLAN_PRESENT 2
#define HFD_PROVIDER_MOD_ID "local.hf-shared-dispatcher"
typedef struct HfdStartup {
    HfdSharedStartup binding;
    volatile LONG initialized,provider_plan,reason; /* reason: initial planner result */
    volatile LONG actor,stopped;
} HfdStartup;
/* Individually atomic sampled fields, not a global point-in-time snapshot.
 * Timer IDs include ignored late callbacks on the resident tombstone. A set
 * overflow means the list/count are incomplete; never report it as zero. */
typedef struct HfdStartupStatus {
    uint32_t size,plan,phase,route,reason,ready,stopped,owned;
    uint32_t shared_attempts,raw_attempts,activation_attempts,timer_allocations,timer_wakes;
    uint32_t initial_control_thread,timer_callbacks,timer_thread_count,timer_thread_overflow;
    uint32_t timer_threads[HFD_STARTUP_THREAD_CAPACITY];
} HfdStartupStatus;
/* shared_limits.raw_attempts MUST be0. DIRECT is selected only during this
 * initializer when complete native-plan enumeration proves absence. HFD_OK
 * may mean an event is armed; inspect binding.core for admission/readiness.
 * All outcomes require resident state; only confirmed cancel DRAINED permits
 * freeing business context. Do not infer cleanup from a non-OK begin result. */
int hfd_startup_begin(HfdStartup*,const HfApi*,const HfdClient*,const HfdRecoveryLimits*,DWORD);
int hfd_startup_ready(HfdStartup*);
int hfd_startup_cancel(HfdStartup*);
int hfd_startup_get_status(HfdStartup*,HfdStartupStatus*);
#ifdef __cplusplus
}
#endif
#endif

#if defined(HFD_AUTOMATIC_STARTUP_IMPLEMENTATION) && !defined(HFD_AUTOMATIC_STARTUP_IMPLEMENTED)
#define HFD_AUTOMATIC_STARTUP_IMPLEMENTED
#ifdef __cplusplus
extern "C" {
#endif
static int hfd_startup_detail_id(const char* id) {
    if(!id)return 0;
    for(unsigned i=0;i<=64;++i) {
        char c=id[i];
        if(!c)return i>0;
        int alpha=c>='a'&&c<='z',digit=c>='0'&&c<='9';
        if(i==64||(!alpha&&!digit&&(i==0||(c!='.'&&c!='_'&&c!='-'))))return 0;
    }
    return 0;
}
static int hfd_startup_detail_equal(const char* a,const char* b) {
    while(*a&&*a==*b){++a;++b;}return *a==*b;
}
static LONG hfd_startup_detail_plan(const HfApi* hf,HfMod* caller) {
    if(hf->version<15||hf->size<offsetof(HfApi,mods)+sizeof hf->mods||!hf->mods||!hf->mod_id)
        return HFD_PLAN_UNKNOWN;
    const char* reported=hf->mod_id(caller);
    if(!hfd_startup_detail_id(reported))return HFD_PLAN_UNKNOWN;
    char own_id[65];unsigned n=0;do{own_id[n]=reported[n];}while(reported[n++]);
    unsigned own=0,provider=0;
    /* SDK15's matching loader has MAX_MODS192, with its complete native plan
     * present before SetupMod. One extra call must observe the end sentinel.
     * Public enumeration exposes later/failed loaded0 entries; loaded is NOT presence. */
    for(uint32_t i=0;i<=192;++i) {
        HfModInfo info;hfd_client_detail_clear(&info,sizeof info);info.size=sizeof info;
        int result=hf->mods(caller,i,&info);
        if(result==0)return own==1&&provider<=1?(provider?HFD_PLAN_PRESENT:HFD_PLAN_ABSENT):HFD_PLAN_UNKNOWN;
        if(result!=1||i==192||info.size!=sizeof info||!hfd_startup_detail_id(info.id)||info.loaded>1||
           (info.kind!=HF_MOD_CODE&&info.kind!=HF_MOD_STATS&&info.kind!=HF_MOD_LUA&&info.kind!=HF_MOD_EDITOR))return HFD_PLAN_UNKNOWN;
        if(hfd_startup_detail_equal(info.id,own_id)) {
            if(++own>1||info.kind!=HF_MOD_CODE)return HFD_PLAN_UNKNOWN;
        }
        if(hfd_startup_detail_equal(info.id,HFD_PROVIDER_MOD_ID)) {
            if(++provider>1||info.kind!=HF_MOD_CODE)return HFD_PLAN_UNKNOWN;
        }
    }
    return HFD_PLAN_UNKNOWN;
}
static int hfd_startup_detail_declared_absent(HfdApi* api,void* user) {
    (void)api;(void)user;return HFD_REFUSED; /* Qualified plan, no module lookup. */
}
static void hfd_startup_detail_inactive(HfdStartup* state,LONG reason) {
    InterlockedExchange(&state->reason,reason);
    InterlockedExchange(&state->binding.core.adapter.reason,reason);
    InterlockedExchange(&state->binding.core.adapter.route,HFD_ROUTE_INACTIVE);
    InterlockedExchange(&state->binding.core.phase,HFD_REC_INACTIVE);
}
static int hfd_startup_detail_direct(HfdStartup* state,const HfApi* hf,const HfdClient* client,
                                     const HfdRecoveryLimits* shared_limits) {
    HfdSharedStartup* binding=&state->binding;
    if(InterlockedCompareExchange(&binding->actor,1,0))return HFD_BUSY;
    int result=HFD_REFUSED;
    if(!hfd_recovery_detail_load(&binding->stopped)) {
        InterlockedExchange(&binding->initialized,1);
        HfdRecoveryLimits limits=*shared_limits;
        limits.shared_attempts=1;limits.raw_attempts=1;limits.activation_attempts=1;
        HfdRecoveryOps ops={hfd_startup_detail_declared_absent,hfd_shared_detail_now,NULL};
        result=hfd_recovery_init(&binding->core,hf,client,&ops,&limits);
        if(result==HFD_OK) {
            result=hfd_recovery_step(&binding->core);
            if(result==HFD_PENDING&&hfd_recovery_detail_load(&binding->core.phase)==HFD_REC_DIRECT_PENDING)
                result=hfd_recovery_step(&binding->core);
            InterlockedExchange(&binding->initialized,2);
        }
    }
    hfd_shared_detail_owned(binding); /* Releases actor; no event/timer exists. */
    return result==HFD_DRAINED?HFD_REFUSED:result;
}
int hfd_startup_begin(HfdStartup* state,const HfApi* hf,const HfdClient* client,
                       const HfdRecoveryLimits* limits,DWORD interval) {
    if(!state)return HFD_INVALID;
    /* Own the entire planner before publishing progress or reading arguments.
     * A completed stop must never be followed by new caller-argument reads. */
    if(InterlockedCompareExchange(&state->actor,1,0))return HFD_BUSY;
    int result=HFD_REFUSED;
    if(!hfd_recovery_detail_load(&state->stopped)&&!hfd_recovery_detail_load(&state->initialized)) {
        InterlockedExchange(&state->initialized,1);
        if(!hf||!client||!limits||client->size<sizeof *client||client->reserved||!client->mod||!client->callback||
           limits->raw_attempts||!limits->shared_attempts||limits->shared_attempts>64||
           !limits->activation_attempts||limits->activation_attempts>64||!limits->shared_window_ms||
           limits->shared_window_ms>limits->total_window_ms||limits->total_window_ms>30000||!interval||interval>1000) {
            hfd_startup_detail_inactive(state,HFD_REASON_UNCERTAIN);result=HFD_INVALID;
        } else {
            LONG plan=hfd_startup_detail_plan(hf,client->mod);
            InterlockedExchange(&state->provider_plan,plan);
            if(plan==HFD_PLAN_UNKNOWN) {
                hfd_startup_detail_inactive(state,HFD_REASON_UNCERTAIN);result=HFD_UNCERTAIN;
            } else if(!hfd_recovery_detail_load(&state->stopped)) {
                if(plan==HFD_PLAN_PRESENT)
                    result=hfd_shared_startup_begin(&state->binding,hf,client,limits,interval);
                else result=hfd_startup_detail_direct(state,hf,client,limits);
                InterlockedExchange(&state->reason,hfd_recovery_detail_load(&state->binding.core.adapter.reason));
            }
        }
    }
    if(hfd_recovery_detail_load(&state->stopped)) {
        (void)hfd_shared_startup_cancel(&state->binding);
        if(result==HFD_OK||result==HFD_DRAINED)result=HFD_REFUSED;
    }
    InterlockedExchange(&state->actor,0);return result;
}
int hfd_startup_ready(HfdStartup* state) {
    if(!state)return HFD_INVALID;
    if(hfd_recovery_detail_load(&state->stopped))return HFD_REFUSED;
    return hfd_shared_startup_ready(&state->binding);
}
int hfd_startup_cancel(HfdStartup* state) {
    if(!state)return HFD_INVALID;
    InterlockedExchange(&state->stopped,1);
    int result=hfd_shared_startup_cancel(&state->binding);
    return result==HFD_DRAINED&&hfd_recovery_detail_load(&state->actor)?HFD_PENDING:result;
}
int hfd_startup_get_status(HfdStartup* state,HfdStartupStatus* out) {
    if(!state||!out||out->size<sizeof *out)return HFD_INVALID;
    HfdStartupStatus value;hfd_client_detail_clear(&value,sizeof value);
    HfdSharedStartup* binding=&state->binding;HfdRecovery* core=&binding->core;
    value.size=sizeof value;
    value.plan=(uint32_t)hfd_recovery_detail_load(&state->provider_plan);
    value.phase=(uint32_t)hfd_recovery_detail_load(&core->phase);
    value.route=(uint32_t)hfd_recovery_detail_load(&core->adapter.route);
    value.reason=(uint32_t)hfd_recovery_detail_load(&core->adapter.reason);
    value.ready=(uint32_t)hfd_recovery_detail_load(&core->ready);
    value.stopped=hfd_recovery_detail_load(&state->stopped)||hfd_recovery_detail_load(&binding->stopped)||hfd_recovery_detail_load(&core->stopped);
    value.owned=(uint32_t)hfd_recovery_detail_load(&core->owned);
    value.shared_attempts=(uint32_t)hfd_recovery_detail_load(&core->shared_tries);
    value.raw_attempts=(uint32_t)hfd_recovery_detail_load(&core->raw_tries);
    value.activation_attempts=(uint32_t)hfd_recovery_detail_load(&core->activation_tries);
    value.timer_allocations=(uint32_t)hfd_recovery_detail_load(&binding->timer_allocations);
    value.timer_wakes=(uint32_t)hfd_recovery_detail_load(&binding->timer_wakes);
    value.initial_control_thread=(uint32_t)hfd_recovery_detail_load(&binding->initial_control_thread);
    value.timer_callbacks=(uint32_t)hfd_recovery_detail_load(&binding->timer_callbacks);
    value.timer_thread_overflow=(uint32_t)hfd_recovery_detail_load(&binding->timer_thread_overflow);
    for(unsigned i=0;i<HFD_STARTUP_THREAD_CAPACITY;++i) {
        value.timer_threads[i]=(uint32_t)hfd_recovery_detail_load(&binding->timer_threads[i]);
        if(value.timer_threads[i])++value.timer_thread_count;
    }
    hfd_recovery_detail_copy(out,&value,sizeof value);
    return HFD_OK; /* No SDK call, CRT copy or business-context access. */
}
#ifdef __cplusplus
}
#endif
#endif
