-- Uses code / research from HD2Runtime by SkyeShade.
-- https://github.com/SkyeShade/HD2Runtime (41184521, LICENSE sections 2/3).
-- Minimal reader derived from player_passives.lua; no whole SDK backport.
-- The local player's APPLIED body armor only. No calls or memory writes.
local world_module=require('codex_lego_yoda/world')
local D=require('codex_lego_yoda/profile').identity
local b=require('hd2runtime/core/bytes')
local M={}
local MG,R,K=D.manager,D.record,D.kit
local code_proven,kit_cache
local ARMOR={[0xB513FD54]=true,[0xE9ADD047]=true}
local function u32(n)return type(n)=='number'and n%1==0 and n>0 and n<4294967296 end
local function pointer(raw,at)
    if not raw or #raw<at+8 then return nil end
    local lo,hi=b.u32(raw,at),b.u32(raw,at+4)
    if hi>0x7FFF then return nil end
    local n=lo+hi*4294967296
    return n>=65536 and n or nil
end
local function open()
    local world,why=world_module.open()
    if not world then return nil,why end
    if code_proven~=world then
        for _,pin in ipairs(D.pins)do
            if not world.view.proves(world.game+pin.rva,pin.hex)then return nil,'IDENTITY_NATIVE_PIN_CHANGED' end
        end
        code_proven=world
    end
    local address=world.view.pointer(world.game+MG.globalRva)
    local head=address and world.view.read(address,0x30)
    local count=address and world.view.u32(address+MG.recordCount)
    if not head or not count or count>MG.capacity then return nil,'CUSTOMIZATION_UNAVAILABLE' end
    local kits=pointer(head,MG.kits)
    local kit_count=b.u32(head,MG.kitCount)
    if not kits or kit_count==0 or kit_count>2048 then return nil,'KIT_TABLE_UNAVAILABLE' end
    return world,{address=address,kits=kits,kit_count=kit_count,records=count}
end
local function kit(world,m,id)
    if not ARMOR[id]then return nil end
    if not kit_cache or kit_cache.world~=world or kit_cache.manager~=m.address
        or kit_cache.kits~=m.kits or kit_cache.count~=m.kit_count then
        kit_cache={world=world,manager=m.address,kits=m.kits,count=m.kit_count,ids={}}
    end
    local cached=kit_cache.ids[id]
    if cached then
        local current=world.view.pointer(m.kits+cached.index*8)
        local row=current and world.view.read(current,K.read)
        return current==cached.pointer and row and b.u32(row,K.id)==id and b.u32(row,K.type)==0
    end
    local raw=world.view.read(m.kits,m.kit_count*8)
    if not raw then return nil end
    local found
    for index=0,m.kit_count-1 do
        local p=pointer(raw,index*8)
        local row=p and world.view.read(p,K.read)
        if not row then return nil end
        if b.u32(row,K.id)==id then
            if found or b.u32(row,K.type)~=0 then return nil end
            found={index=index,pointer=p}
        end
    end
    if found then kit_cache.ids[id]=found;return true end
    return nil
end
local function record(world,m,player)
    if not(u32(player.entity)and type(player.peer)=='string'and#player.peer==16)then return nil end
    local raw=world.view.read(m.address+MG.map,20)
    local slots=pointer(raw,0)
    if not slots then return nil end
    local capacity,empty,multiplier=b.u32(raw,8),b.u32(raw,12),b.u32(raw,16)
    if capacity==0 or capacity>64 or capacity%2~=0 then return nil end
    local entries=world.view.read(slots,capacity*8)
    if not entries then return nil end
    local start,index=world_module.mul32(player.entity,multiplier)
    for probe=0,capacity-1 do
        local at=((start+probe)%capacity)*8
        local key=b.u32(entries,at)
        if key==player.entity then index=b.u32(entries,at+4);break end
        if key==empty then break end
    end
    if not index or index>=m.records or index>=MG.capacity then return nil end
    local descriptor=world.view.pointer(m.address+MG.descriptors+index*8)
    if not descriptor or world.view.u32(descriptor+MG.descriptorEntity)~=player.entity then return nil end
    local bytes=world.view.read(m.address+MG.applied+index*MG.appliedStride,MG.appliedStride)
    if not bytes then return nil end
    local armor=b.u32(bytes,R.armorKit)
    local body=b.u32(bytes,0)
    if not kit(world,m,armor)or(body~=0 and body~=1)then return nil end
    return {world_key=world.key,peer=player.peer,entity=player.entity,avatar=player.avatar,
        is_local=player['local']==true,armor_id=string.format('%08X',armor),body_type=body,
        helmet_id=string.format('%08X',b.u32(bytes,R.helmetKit)),record_index=index,record_bytes=bytes}
end
function M.read_all()
    local world,m=open()
    if not world then return nil,m end
    local state=world_module.game_state(world)
    if not(state and state.mission)then return nil,'NOT_IN_MISSION' end
    local me
    local players=world_module.players(world,true)
    if #players>4 then return nil,'TOO_MANY_PLAYERS' end
    for _,player in ipairs(players)do
        if player['local']==true then
            if me then return nil,'AMBIGUOUS_LOCAL_PLAYER' end
            me=player
        end
    end
    if not me then return nil,'NO_LOCAL_PLAYER' end
    local item=record(world,m,me)
    local health=me.avatar and world_module.entity_state(world,me.avatar)
    if not item or not health then return nil,'NO_LOCAL_APPLIED_AVATAR' end
    item.alive=health.life<2
    return {item}
end
function M.validate(saved,event)
    local world,m=open()
    if not world or world.key~=saved.world_key then return nil,'WORLD_CHANGED' end
    local state=world_module.game_state(world)
    if not(state and state.mission)then return nil,'NOT_IN_MISSION' end
    local match
    for _,player in ipairs(world_module.players(world,true))do
        if player['local']==true and player.peer==saved.peer then
            if match then return nil,'AMBIGUOUS_PLAYER' end
            match=player
        end
    end
    if not match or match.entity~=saved.entity then return nil,'PLAYER_CHANGED' end
    if event.observed=='dead_state'and event.local_player==true then
        if match.avatar~=saved.avatar then return nil,'AVATAR_CHANGED' end
        local health=world_module.entity_state(world,saved.avatar)
        if not health or health.life<2 then return nil,'NOT_DEAD' end
    else return nil,'UNKNOWN_DEATH_OBSERVATION' end
    local current=record(world,m,match)
    if not current or current.record_bytes~=saved.record_bytes or current.record_index~=saved.record_index then
        return nil,'APPLIED_ARMOR_CHANGED'
    end
    return true
end
return M
