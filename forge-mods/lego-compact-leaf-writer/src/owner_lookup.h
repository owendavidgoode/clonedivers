/* Pinned raw-read association. No engine calls; iterates all PlayerManager slots 0..count-1. */
#include "clonedivers_em_layout.h"
#define HASH_PROBE_LIMIT 64u
#define HASH_CAPACITY_LIMIT 16384u
#define DESCRIPTOR_LIMIT 4096u
#define COOP_HASH_PROBE_LIMIT 512u
#define COOP_HASH_CAPACITY_LIMIT 1048576u
#define COOP_DESCRIPTOR_LIMIT 0x100000u
#define AVATAR_RESOURCE 0x4D1C334D294DFA97ull
static uintptr_t gameModule;
static const char *ownerFailure;
typedef struct HashHeader { uintptr_t buckets; uint32_t capacity, empty, multiplier; } HashHeader;
typedef struct Lookup { HashHeader header; uint32_t key, index, slot, probes; } Lookup;
typedef struct Descriptor { uint64_t resource; uint32_t entity, reference, network, flags; } Descriptor;
typedef struct Owner {
    uintptr_t game, players, context, entities, playerDescriptor;
    uint64_t peer, rowPeer;
    uint32_t invite, state, count, transition, player, network, life;
    uint8_t localFlag;
    Lookup networkLookup, entityLookup;
    Descriptor descriptor, descriptorAgain;
    Manager manager;
    Identity unit;
} Owner;
typedef struct CoopRoster {
    uintptr_t players, context, entities;
    uint64_t local_peer;
    uint32_t count, raw_count;
    coop_player_entry entries[COOP_MAX_PLAYERS];
    uint64_t row_peers[COOP_MAX_PLAYERS];
    uint32_t networks[COOP_MAX_PLAYERS];
    uint32_t lifes[COOP_MAX_PLAYERS];
} CoopRoster;
static int Span(uintptr_t at,size_t size)
{ return Address(at) && size && size<=0x0000800000000000ull-at; }
static int FailOwner(const char *reason) { ownerFailure=reason;return 0; }
static int HashLookupBounded(uintptr_t at,uint32_t key,Lookup *out,uint32_t capLimit,uint32_t probeLimit,uint32_t descLimit,int requirePow2)
{
    memset(out,0,sizeof *out);out->key=key;
    HashHeader after;memset(&after,0,sizeof after);
    if(!Read(at,&out->header,20))return FailOwner("lookup_header_unreadable");
    const HashHeader *h=&out->header;
    if(!h->capacity || h->capacity>capLimit || (requirePow2 && (h->capacity&(h->capacity-1))) ||
       !Span(h->buckets,(size_t)h->capacity*8))return FailOwner("lookup_header_bounds");
    if(key==h->empty)return FailOwner("lookup_key_is_empty");
    uint32_t product=key*h->multiplier;
    for(uint32_t probe=0;probe<h->capacity && probe<probeLimit;probe++) {
        uint32_t bucket[2];
        out->slot=requirePow2?((product+probe)&(h->capacity-1)):((uint32_t)(product+probe)%h->capacity);
        out->probes=probe+1;
        if(!Read(h->buckets+(uintptr_t)out->slot*8,bucket,8))return FailOwner("lookup_bucket_unreadable");
        if(bucket[0]==h->empty)return FailOwner("lookup_empty_before_match");
        if(bucket[0]==key) {
            if(bucket[1]>=descLimit)return FailOwner("lookup_value_bounds");
            out->index=bucket[1];
            if(!Read(at,&after,20) || memcmp(h,&after,sizeof after))return FailOwner("lookup_header_changed");
            return 1;
        }
    }
    return FailOwner(h->capacity>probeLimit?"lookup_probe_budget":"lookup_table_exhausted");
}
static CdEmLayout s_cachedEmLayout;
static int s_hasCachedEmLayout;
static CdEmLayout ResolveLiveEmLayout(void)
{
    if (!hf || !gameModule) return cd_em_default_layout();
    if (hf->read_code) {
        CdEmLayout byCode = cd_em_resolve_hf(gameModule, hf->read_code);
        if (byCode.relocated) {
            s_cachedEmLayout = byCode;
            s_hasCachedEmLayout = 1;
            return byCode;
        }
    }
    if (!invocationBudget && hf->read) {
        CdEmLayout byRead = cd_em_resolve_hf(gameModule, hf->read);
        s_cachedEmLayout = byRead;
        s_hasCachedEmLayout = 1;
        return byRead;
    }
    return s_hasCachedEmLayout ? s_cachedEmLayout : cd_em_default_layout();
}
static int HashLookupWithLimit(uintptr_t at,uint32_t key,Lookup *out,uint32_t descLimit)
{ return HashLookupBounded(at,key,out,HASH_CAPACITY_LIMIT,HASH_PROBE_LIMIT,descLimit,1); }
static inline int HashLookup(uintptr_t at,uint32_t key,Lookup *out)
{ return HashLookupWithLimit(at,key,out,DESCRIPTOR_LIMIT); }
static int CoopHashLookup(uintptr_t at,uint32_t key,Lookup *out)
{ return HashLookupBounded(at,key,out,COOP_HASH_CAPACITY_LIMIT,COOP_HASH_PROBE_LIMIT,COOP_DESCRIPTOR_LIMIT,0); }
static int CoopPlayersRead(const Manager *mgr,CoopRoster *out)
{
    memset(out,0,sizeof *out);ownerFailure="coop_players_unreadable";
    if(!Span(gameModule,0x347CEF8))return 0;
    /* Title screen / non-mission UI may leave PlayerManager or EntityManager unpopulated. */
    if(!Read(gameModule+0x3326468,&out->players,8) || !Address(out->players) || !Span(out->players,0x428))return 1;
    if(!Read(out->players+0x84,&out->raw_count,4))return 0;
    if(out->raw_count>COOP_MAX_PLAYERS)return FailOwner("player_count_bounds");
    if(Read(gameModule+0x347CEF0,&out->context,8) && Address(out->context) && Span(out->context,0xB3A0))
        Read(out->context+0xB398,&out->local_peer,8);
    if(!Read(gameModule+0x346BF98,&out->entities,8) || !Address(out->entities) || !Span(out->entities,0xF3EF18))return 1;
    CdEmLayout emLayout=ResolveLiveEmLayout();
    for(uint32_t i=0;i<out->raw_count;i++) {
        uint64_t rowPeer=0;uint32_t life=0,network=0x7FFF;uint8_t localFlag=0;uintptr_t pdesc=0;
        if(!Read(out->players+0x2C8+(uintptr_t)i*0x38,&rowPeer,8))continue;
        Read(out->players+0x2E0+(uintptr_t)i*0x38,&life,4);
        if(!Read(out->players+0x3A8+(uintptr_t)i*0x20,&network,4))network=0x7FFF;
        out->row_peers[i]=rowPeer;out->networks[i]=network;out->lifes[i]=life;
        if(Read(out->players+0xE8+(uintptr_t)i*8,&pdesc,8) && Address(pdesc) && Span(pdesc,21))
            Read(pdesc+20,&localFlag,1);
        if(network==0x7FFF)continue;
        Lookup nl;if(!CoopHashLookup(out->entities+0xF22EC8,network,&nl) || (emLayout.relocated && nl.index>=emLayout.capacity))continue;
        Descriptor d,dAgain;
        uintptr_t dAt=cd_em_record_at(out->entities,emLayout.records_offset,nl.index);
        if(!dAt || !Span(dAt,sizeof d) || !Read(dAt,&d,sizeof d) || !Read(dAt,&dAgain,sizeof dAgain) || memcmp(&d,&dAgain,sizeof d))continue;
        if(d.resource!=AVATAR_RESOURCE || !d.entity || d.network!=network)continue;
        uint32_t avatarIdx=d.reference&0x3FFFFFu;uint8_t avatarGen=(uint8_t)((d.reference>>22)&0xFFu);
        Identity id;
        if(!mgr || IdentityRead(mgr,avatarIdx,&id) || id.generation!=avatarGen || id.resource!=AVATAR_RESOURCE)continue;
        coop_player_entry *e=&out->entries[out->count++];
        memset(e,0,sizeof *e);
        e->peer=rowPeer;e->slot=i;e->life=life;e->network_id=network;
        e->local=(out->local_peer!=0)?(rowPeer!=0 && rowPeer==out->local_peer):((localFlag&1)!=0);
        e->avatar_unit=id.unit;e->avatar_handle=d.reference;e->avatar_gen=avatarGen;
    }
    ownerFailure="none";return 1;
}
static int OwnerReadSlot(const Manager *mgr,const CdEmLayout *emLayout,uint32_t slotIndex,uint32_t count,uintptr_t players,uintptr_t context,uintptr_t entities,uint64_t peer,Owner *out)
{
    memset(out,0,sizeof *out);
    out->players=players;out->context=context;out->entities=entities;out->count=count;out->peer=peer;out->manager=*mgr;
    if(!Read(players+0x2C8+(uintptr_t)slotIndex*0x38,&out->rowPeer,8) ||
       !Pointer(players+0xE8+(uintptr_t)slotIndex*8,&out->playerDescriptor) || !Span(out->playerDescriptor,21) ||
       !Read(out->playerDescriptor+8,&out->player,4) || !Read(out->playerDescriptor+20,&out->localFlag,1) ||
       !Read(players+0x3A8+(uintptr_t)slotIndex*0x20,&out->network,4) ||
       !Read(players+0x2E0+(uintptr_t)slotIndex*0x38,&out->life,4))return 0;
    if(out->network==0x7FFF)return FailOwner("slot_network_empty");
    uint32_t descLimit=emLayout?emLayout->bounded_capacity:DESCRIPTOR_LIMIT;
    int64_t descOff=emLayout?emLayout->records_offset:CD_EM_DEFAULT_OFFSET;
    if(!HashLookupWithLimit(out->entities+0xF22EC8,out->network,&out->networkLookup,descLimit))return 0;
    uintptr_t descriptor=cd_em_record_at(out->entities,descOff,out->networkLookup.index);
    if(!descriptor || !Span(descriptor,sizeof out->descriptor) || !Read(descriptor,&out->descriptor,sizeof out->descriptor))return 0;
    const Descriptor *d=&out->descriptor;
    if(d->resource!=AVATAR_RESOURCE || !d->entity || d->network!=out->network || !(d->flags&1))
        return FailOwner("descriptor_qualification");
    if(!HashLookupWithLimit(out->entities+0xF1AEB0,d->entity,&out->entityLookup,descLimit))return 0;
    if(out->networkLookup.index!=out->entityLookup.index)return FailOwner("descriptor_index_disagreement");
    if(!Read(descriptor,&out->descriptorAgain,sizeof out->descriptorAgain) ||
       memcmp(d,&out->descriptorAgain,sizeof *d))return FailOwner("descriptor_changed");
    uint32_t index=d->reference&0x3FFFFFu;
    if(IdentityRead(&out->manager,index,&out->unit) || out->unit.generation!=((d->reference>>22)&0xFF) ||
       out->unit.resource!=AVATAR_RESOURCE)return FailOwner("owner_reference_identity");
    return 1;
}
static int OwnerRead(Owner *out)
{
    memset(out,0,sizeof *out);ownerFailure="owner_unreadable";
    uintptr_t players=0,context=0,entities=0;uint32_t count=0;uint64_t peer=0;Manager mgr;
    if(!Pointer(gameModule+0x3326468,&players) || !Span(players,0x428) ||
       !Pointer(gameModule+0x347CEF0,&context) || !Span(context,0x159D0) ||
       !Pointer(gameModule+0x346BF98,&entities) || !Span(entities,0xF3EF18))return 0;
    if(!Read(players+0x84,&count,4) || !Read(context+0xB398,&peer,8))return 0;
    if(!count || count>COOP_MAX_PLAYERS)return FailOwner("player_count_qualification");
    if(!ManagerRead(&mgr) || memcmp(&mgr,&initial,sizeof initial))return FailOwner("owner_manager_changed");
    CdEmLayout emLayout=ResolveLiveEmLayout();
    Owner candidate;int found=-1;
    for(uint32_t i=0;i<count;i++) {
        if(!OwnerReadSlot(&mgr,&emLayout,i,count,players,context,entities,peer,&candidate))continue;
        if(found<0) {*out=candidate;found=(int)i;}
        if(peer && candidate.rowPeer==peer && (candidate.localFlag&1)){*out=candidate;ownerFailure="none";return 1;}
    }
    if(found>=0){ownerFailure="none";return 1;}
    return 0;
}
static int Associated(const Owner *before,const Owner *after,const Record *record)
{
    return !memcmp(before,after,sizeof *before) && record->parentPresent && record->reciprocal &&
        !record->chainStatus && !memcmp(&before->unit,&record->parent.id,sizeof before->unit);
}


