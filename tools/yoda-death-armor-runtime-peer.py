#!/usr/bin/env python3
"""Independently exercise frozen LEGO identity and routing in offline Lua."""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
AREA = ROOT / "dist/empire-yoda-death-2026-10-08"
OLD = ROOT / "dist/empire-next-2026-10-05/vehicles/primary-runtime-v1/source/HD2Runtime-96ab2d258d867a5df4f22bb7b3321d84d28de21d"


def pin(path: Path) -> dict[str, Any]:
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": digest}


ROUTING = r"""
local results={}
local function test(name,fn) fn();results[#results+1]=name end
local function eq(actual,expected)assert(actual==expected,tostring(actual)..' ~= '..tostring(expected))end
local function fixture(armor,body,helmet)
 local state={now=100,context=true,session='mission:one',valid=true,resident=true,posted=0,play=true}
 local row={peer='0123456789abcdef',avatar=456,entity=123,is_local=true,alive=true,armor_id=armor or'B513FD54',body_type=body or 0,helmet_id=helmet or'00000001'}
 local records={row}
 local adapter={context=function()return state.context end,now=function()return state.now end,
 session=function()return state.session end,read_all=function()return records end,
 validate=function()return state.valid,state.why end,available=function()return state.resident end,
 play=function(name,opts)state.posted=state.posted+1;state.last={name,opts};return state.play end}
 local spec={playback_qualified=true,assets_qualified=true,event='clonedivers_lego_yoda_death'}
 local gate=M.new(adapter,spec)
 local event={peer=row.peer,avatar_id=row.avatar,local_player=true,observed='dead_state',cause={source='native'},position={x=1,y=2,z=3}}
 eq(gate.observe(),true)
 return gate,state,row,event,spec,records
end
for _,armor in ipairs({'B513FD54','E9ADD047'})do
 for body=0,1 do
  for _,helmet in ipairs({'5C3087D2','2F748B84','00000001','00000000'})do
   test('body-controls:'..armor..':'..body..':'..helmet,function()
    local gate,s,r,e=fixture(armor,body,helmet);local d,code=gate.on_death(e)
    eq(code,'PLAYED');eq(d.armor_id,armor);eq(s.posted,1)
    eq(s.last[1],'clonedivers_lego_yoda_death');eq(s.last[2].position[1],1)
    eq(select(2,gate.on_death(e)),'ALREADY_HANDLED');eq(s.posted,1)
   end)
  end
 end
end
for _,helmet in ipairs({'5C3087D2','2F748B84'})do
 test('ordinary-body-lego-helmet:'..helmet,function()
  local gate,s,r,e=fixture('12345678',0,helmet)
  eq(select(2,gate.on_death(e)),'NO_MATCHING_LEGO_AVATAR');eq(s.posted,0)
 end)
end
local negatives={
 {'nonlocal',function(s,r,e)e.local_player=false end,'INVALID_DEATH'},
 {'wrong-avatar',function(s,r,e)e.avatar_id=457 end,'NO_MATCHING_LEGO_AVATAR'},
 {'wrong-peer',function(s,r,e)e.peer='1123456789abcdef' end,'NO_MATCHING_LEGO_AVATAR'},
 {'nonhex-peer',function(s,r,e)e.peer='z123456789abcdef' end,'INVALID_DEATH'},
 {'missing-cause',function(s,r,e)e.cause=nil end,'NOT_NATIVE_DEATH'},
 {'synthetic-cause',function(s,r,e)e.cause.source='synthetic' end,'NOT_NATIVE_DEATH'},
 {'other-observation',function(s,r,e)e.observed='removed' end,'UNKNOWN_DEATH_OBSERVATION'},
 {'world-change',function(s,r,e)s.session='mission:two' end,'NO_MATCHING_LEGO_AVATAR'},
 {'too-old',function(s,r,e)s.now=100.500001 end,'STALE_ARMOR_OBSERVATION'},
 {'backwards-clock',function(s,r,e)s.now=99.99 end,'STALE_ARMOR_OBSERVATION'},
 {'nan-clock',function(s,r,e)s.now=0/0 end,'INVALID_CLOCK'},
 {'infinite-clock',function(s,r,e)s.now=math.huge end,'INVALID_CLOCK'},
 {'changed-applied-record',function(s,r,e)s.valid=false;s.why='APPLIED_ARMOR_CHANGED' end,'APPLIED_ARMOR_CHANGED'},
 {'no-position',function(s,r,e)e.position=nil end,'NO_DEATH_POSITION'},
 {'nan-position',function(s,r,e)e.position.x=0/0 end,'NO_DEATH_POSITION'},
 {'infinite-position',function(s,r,e)e.position.y=math.huge end,'NO_DEATH_POSITION'},
 {'far-position',function(s,r,e)e.position.z=100001 end,'NO_DEATH_POSITION'},
 {'asset-gate',function(s,r,e,spec)spec.assets_qualified=false end,'ASSETS_UNQUALIFIED'},
 {'event-not-resident',function(s,r,e)s.resident=false end,'EVENT_NOT_RESIDENT'},
 {'context-gate',function(s,r,e)s.context=false end,'CONTEXT_REFUSED'},
}
for _,n in ipairs(negatives)do test(n[1],function()
 local gate,s,r,e,spec=fixture();n[2](s,r,e,spec)
 eq(select(2,gate.on_death(e)),n[3]);eq(s.posted,0)
end)end
test('post-failure-no-retry',function()
 local gate,s,r,e=fixture();s.play=false
 eq(select(2,gate.on_death(e)),'NOT_PLAYED');eq(s.posted,1)
 eq(select(2,gate.on_death(e)),'ALREADY_HANDLED');eq(s.posted,1)
end)
test('diagnostic-only-no-audio',function()
 local gate,s,r,e,spec=fixture();spec.playback_qualified=false
 eq(select(2,gate.on_death(e)),'DIAGNOSTIC_ONLY');eq(s.posted,0)
end)
test('dead-before-event-keeps-last-live-proof',function()
 local gate,s,r,e=fixture();r.alive=false;s.now=100.1
 eq(gate.observe(),true);eq(select(2,gate.on_death(e)),'PLAYED');eq(s.posted,1)
end)
test('armor-changed-to-ordinary-clears-proof',function()
 local gate,s,r,e=fixture();r.armor_id='12345678';gate.observe()
 eq(select(2,gate.on_death(e)),'NO_MATCHING_LEGO_AVATAR');eq(s.posted,0)
end)
test('observe-nan-clears-proof',function()
 local gate,s,r,e=fixture();s.now=0/0
 eq(select(2,gate.observe()),'INVALID_CLOCK');s.now=100
 eq(select(2,gate.on_death(e)),'NO_MATCHING_LEGO_AVATAR');eq(s.posted,0)
end)
return table.concat(results,'\n')
"""

IDENTITY = r"""
local results={}
local function eq(a,b)assert(a==b,tostring(a)..' ~= '..tostring(b))end
local function fixture(armor,body,helmet)
 local mem={};local MG,R,K=P.identity.manager,P.identity.record,P.identity.kit
 local game,manager,kits,kit1,kit2,slots,descriptor=0x100000,0x200000,0x300000,0x400000,0x410000,0x500000,0x600000
 local function write(at,text)for i=1,#text do mem[at+i-1]=text:byte(i)end end
 local function u32(at,n)write(at,string.char(n%256,math.floor(n/256)%256,math.floor(n/65536)%256,math.floor(n/16777216)%256))end
 local function pointer(at,n)u32(at,n);u32(at+4,0)end
 local function zero(at,n)write(at,string.rep('\0',n))end
 local function read(at,n)local out={};for i=0,n-1 do if not mem[at+i]then return nil end;out[#out+1]=string.char(mem[at+i])end;return table.concat(out)end
 local state={mission=true,life=0,prove=true}
 local player={peer='0123456789abcdef',entity=123,avatar=456,['local']=true}
 local world={game=game,key='fixture-world',view={}}
 world.view.read=read
 world.view.proves=function()return state.prove end
 world.view.u32=function(at)local v=read(at,4);return v and B.u32(v,0)end
 world.view.pointer=function(at)local v=read(at,8);return v and B.u32(v,0)+B.u32(v,4)*4294967296 end
 pointer(game+MG.globalRva,manager);zero(manager,48);pointer(manager+MG.kits,kits);u32(manager+MG.kitCount,2);u32(manager+MG.recordCount,1)
 pointer(kits,kit1);pointer(kits+8,kit2);zero(kit1,K.read);zero(kit2,K.read)
 u32(kit1+K.id,armor or 0xb513fd54);u32(kit1+K.type,0);u32(kit2+K.id,0x12345678);u32(kit2+K.type,0)
 pointer(manager+MG.map,slots);u32(manager+MG.map+8,4);u32(manager+MG.map+12,0xffffffff);u32(manager+MG.map+16,1)
 for i=0,3 do u32(slots+i*8,0xffffffff);u32(slots+i*8+4,0xffffffff)end
 u32(slots+3*8,123);u32(slots+3*8+4,0)
 pointer(manager+MG.descriptors,descriptor);u32(descriptor+MG.descriptorEntity,123)
 local applied=manager+MG.applied;zero(applied,MG.appliedStride);u32(applied,body or 0);u32(applied+R.armorKit,armor or 0xb513fd54);u32(applied+R.helmetKit,helmet or 1)
 local players={player}
 local W={open=function()return world end,game_state=function()return{mission=state.mission}end,
 players=function()return players end,entity_state=function(_,id)if id==456 then return{life=state.life}end end,
 mul32=function(a,b)return(a*b)%4294967296 end}
 local old=require;require=function(name)
  if name=='hd2runtime/runtime/event_world'then return W end
  if name=='codex_lego_yoda/profile'then return P end
  if name=='hd2runtime/core/bytes'then return B end
  error('unqualified require:'..name)
 end
 local M=load_identity();require=old
 local event={local_player=true,observed='dead_state'}
 return M,state,player,players,event,{u32=u32,kit1=kit1,kit2=kit2,kitType=K.type,kitID=K.id,
 manager=manager,MG=MG,descriptor=descriptor,applied=applied,R=R,world=world}
end
local function test(name,fn)fn();results[#results+1]=name end
for _,armor in ipairs({0xb513fd54,0xe9add047})do for body=0,1 do
 for _,helmet in ipairs({0x5c3087d2,0x2f748b84,1,0})do
 test('identity-body:'..string.format('%08X',armor)..':'..body..':'..string.format('%08X',helmet),function()
  local M,s,p,players,e,f=fixture(armor,body,helmet);local list,why=M.read_all();assert(list,why)
  eq(list[1].armor_id,string.format('%08X',armor));eq(list[1].helmet_id,string.format('%08X',helmet));eq(list[1].body_type,body);eq(list[1].alive,true)
  s.life=2;eq(M.validate(list[1],e),true)
 end)
end end end
local readneg={
 {'wrong-kit-category',function(s,p,players,f)f.u32(f.kit1+f.kitType,1)end,'NO_LOCAL_APPLIED_AVATAR'},
 {'duplicate-kit-id',function(s,p,players,f)f.u32(f.kit2+f.kitID,0xb513fd54)end,'NO_LOCAL_APPLIED_AVATAR'},
 {'invalid-body-type',function(s,p,players,f)f.u32(f.applied,2)end,'NO_LOCAL_APPLIED_AVATAR'},
 {'descriptor-entity-mismatch',function(s,p,players,f)f.u32(f.descriptor+f.MG.descriptorEntity,124)end,'NO_LOCAL_APPLIED_AVATAR'},
 {'no-local',function(s,p,players,f)p['local']=false end,'NO_LOCAL_PLAYER'},
 {'ambiguous-local',function(s,p,players,f)players[2]=p end,'AMBIGUOUS_LOCAL_PLAYER'},
 {'five-players',function(s,p,players,f)for i=2,5 do players[i]={}end end,'TOO_MANY_PLAYERS'},
 {'not-mission',function(s,p,players,f)s.mission=false end,'NOT_IN_MISSION'},
 {'loaded-proof-fails',function(s,p,players,f)s.prove=false end,'IDENTITY_NATIVE_PIN_CHANGED'},
 {'empty-records',function(s,p,players,f)f.u32(f.manager+f.MG.recordCount,0)end,'NO_LOCAL_APPLIED_AVATAR'},
}
for _,n in ipairs(readneg)do test(n[1],function()
 local M,s,p,players,e,f=fixture();n[2](s,p,players,f);local list,why=M.read_all();eq(list,nil);eq(why,n[3])
end)end
local validneg={
 {'changed-armor',function(s,p,players,e,f)f.u32(f.applied+f.R.armorKit,0x12345678)end,'APPLIED_ARMOR_CHANGED'},
 {'changed-helmet',function(s,p,players,e,f)f.u32(f.applied+f.R.helmetKit,0x5c3087d2)end,'APPLIED_ARMOR_CHANGED'},
 {'changed-body-type',function(s,p,players,e,f)f.u32(f.applied,1)end,'APPLIED_ARMOR_CHANGED'},
 {'changed-world',function(s,p,players,e,f)f.world.key='other-world'end,'WORLD_CHANGED'},
 {'changed-avatar',function(s,p,players,e,f)p.avatar=457 end,'AVATAR_CHANGED'},
 {'changed-entity',function(s,p,players,e,f)p.entity=124 end,'PLAYER_CHANGED'},
 {'still-alive',function(s,p,players,e,f)s.life=0 end,'NOT_DEAD'},
 {'lost-local',function(s,p,players,e,f)p['local']=false end,'PLAYER_CHANGED'},
 {'unknown-observation',function(s,p,players,e,f)e.observed='removed'end,'UNKNOWN_DEATH_OBSERVATION'},
}
for _,n in ipairs(validneg)do test(n[1],function()
 local M,s,p,players,e,f=fixture();local list,why=M.read_all();assert(list,why);s.life=2;n[2](s,p,players,e,f)
 local ok,code=M.validate(list[1],e);eq(ok,nil);eq(code,n[3])
end)end
return table.concat(results,'\n')
"""


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", default="candidate-v1")
    parser.add_argument("--output", default="runtime-peer-v1")
    args = parser.parse_args()
    source = AREA / "runtime" / args.candidate
    output = AREA / "armor" / args.output
    if output.exists() or not output.resolve().is_relative_to(AREA / "armor"):
        raise ValueError("Fresh armor peer output required")
    spec = importlib.util.spec_from_file_location("yoda_armor_peer_lua", OLD / "sdk/tools/lua_runner.py")
    if spec is None or spec.loader is None:
        raise ImportError("Pinned offline Lua runner")
    vm = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(vm)
    profile = (source / "profile.lua").read_text(encoding="utf-8")
    identity = (source / "identity.lua").read_text(encoding="utf-8")
    routing = (source / "routing.lua").read_text(encoding="utf-8")
    byte_path = AREA / "runtime/primary-v1/source/core/bytes.lua"
    byte_source = byte_path.read_text(encoding="utf-8")
    routing_code = "local M=(function()\n" + routing + "\nend)()\n" + ROUTING
    identity_code = "local P=(function()\n" + profile + "\nend)()\nlocal B=(function()\n" + byte_source + "\nend)()\nlocal function load_identity()\n" + identity + "\nend\n" + IDENTITY
    routing_result = vm.execute(routing_code.encode())
    identity_result = vm.execute(identity_code.encode())
    routing_cases = (routing_result.decode() if isinstance(routing_result, bytes) else routing_result).splitlines()
    identity_cases = (identity_result.decode() if isinstance(identity_result, bytes) else identity_result).splitlines()
    output.mkdir(parents=True)
    (output / "routing-fixtures.lua").write_text(routing_code, encoding="utf-8")
    (output / "identity-fixtures.lua").write_text(identity_code, encoding="utf-8")
    report = {"schemaVersion": 1, "status": "passed", "candidate": args.candidate,
              "modules": [pin(source / (name + ".lua")) for name in ("identity", "routing", "sound", "profile")],
              "sourceReport": pin(source / "report.json"), "bytePrimary": pin(byte_path),
              "runner": pin(OLD / "sdk/tools/lua_runner.py"),
              "routingCases": routing_cases, "identityCases": identity_cases,
              "fixtureCount": len(routing_cases) + len(identity_cases),
              "fixtures": [pin(output / "routing-fixtures.lua"), pin(output / "identity-fixtures.lua")],
              "scope": "Independent offline policy and simulated native-record reader fixtures. Loaded instruction and engine sound bindings are mocked; no actual game-process proof or playback asserted.",
              "policy": "Two exact applied body armor IDs only; both body types; helmet independence; unchanged full applied record at death; local native dead-state event; freshness; single attempt; exact resident event and finite position.",
              "liveChanges": False, "runtimeAccepted": False, "tool": pin(Path(__file__))}
    path = output / "report.json"
    path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"report": pin(path), "fixtureCount": report["fixtureCount"]}))


if __name__ == "__main__":
    main()
