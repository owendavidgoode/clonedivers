#!/usr/bin/env python3
"""Exercise LEGO routing and native readers in fresh standalone Lua states only."""
from __future__ import annotations

import argparse
from collections.abc import Sequence
import importlib.util
import json
import logging
from pathlib import Path
import re
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("yoda_runtime_build", ROOT / "tools/yoda-death-runtime-build.py")
assert spec and spec.loader
B = importlib.util.module_from_spec(spec)
spec.loader.exec_module(B)


def preload(name: str, source: str) -> str:
    return f"package.preload[{B.lua(name)}]=function()return assert(loadstring({B.lua(source)}, {B.lua('@'+name)}))()end\n"


def prefix(prepared: Path) -> tuple[str, list[dict[str, Any]]]:
    modules: dict[str, Path] = {}
    def visit(name: str) -> None:
        if name in modules or not name.startswith("hd2runtime/"):
            return
        path = B.OLD / (name.removeprefix("hd2runtime/") + ".lua")
        if not path.is_file():
            raise FileNotFoundError(path)
        modules[name] = path
        for dep in re.findall(r"require\s*\(?\s*['\"]([^'\"]+)['\"]", path.read_text()):
            visit(dep)
    for name in ("hd2runtime/runtime/event_world", "hd2runtime/runtime/event_sources", "hd2runtime/runtime/events",
                 "hd2runtime/runtime/native_view", "hd2runtime/core/fingerprint", "hd2runtime/core/bytes"):
        visit(name)
    result = "".join(preload(name, path.read_text()) for name, path in sorted(modules.items()))
    fixture = B.OLD / "tests/event_world_fixture.lua"
    # Only export the fixture's local sparse-memory allocator; never touch its sealed source.
    source = fixture.read_text().replace("return W\n", "W.alloc=alloc\nreturn W\n")
    result += preload("yoda_fixture", source)
    for name in ("profile", "world", "identity", "routing", "sound"):
        result += preload("codex_lego_yoda/" + name, (prepared / (name + ".lua")).read_text())
    return result, [B.pin(path) for path in modules.values()] + [B.pin(fixture)]


SETUP = r'''
local W=require('yoda_fixture')
local D=require('codex_lego_yoda/profile')
local world=require('codex_lego_yoda/world')
local identity=require('codex_lego_yoda/identity')
local routing=require('codex_lego_yoda/routing')
local sound=require('codex_lego_yoda/sound')
local function unhex(h)return(h:gsub('..',function(p)return string.char(tonumber(p,16))end))end
local function putpin(pin,base)W.write(base+pin.rva,unhex(pin.hex))end
for _,pin in ipairs(D.identity.pins)do putpin(pin,W.GAME)end
for _,pin in ipairs(D.sound.selectorPins)do putpin(pin,pin.module=='exe'and W.EXE or W.GAME)end
local plugin=W.alloc(D.sound.plugin.imageSize)
W.write(plugin,'MZ');W.write(plugin+60,W.u32(128));W.write(plugin+128,'PE\0\0')
W.write(plugin+128+80,W.u32(D.sound.plugin.imageSize))
for _,pin in ipairs(D.sound.plugin.pins)do putpin(pin,plugin)end
local original_module=W.runtime.module
W.runtime.module=function(name)if name==D.sound.plugin.module then return plugin end;return original_module(name)end
local reads=0
local original_read=W.runtime.read
W.runtime.read=function(at,n)reads=reads+1;return original_read(at,n)end
world.set_runtime(W.runtime)
local LOCAL,REMOTE='ABCD000000000001','ABCD000000000002'
W.add{entity=100,type=W.AVATAR,unit=7100,owned=true,life=0,health=125}
W.unit(7100,1,2,3)
W.players({{peer=LOCAL,avatar=100}},LOCAL);W.state(4)
local MG,R,K=D.identity.manager,D.identity.record,D.identity.kit
local manager=W.alloc(0x1100)
local kit_array=W.alloc(4*8)
local kit1,kit2=W.alloc(K.read),W.alloc(K.read)
W.write(kit1+K.id,W.u32(0xB513FD54));W.write(kit1+K.type,W.u32(0))
W.write(kit2+K.id,W.u32(0xE9ADD047));W.write(kit2+K.type,W.u32(0))
W.write(kit_array,W.u64(kit1)..W.u64(kit2))
W.write(W.GAME+MG.globalRva,W.u64(manager))
W.write(manager+MG.kits,W.u64(kit_array));W.write(manager+MG.kitCount,W.u32(2))
W.write(manager+MG.recordCount,W.u32(1))
local armor_slots=W.alloc(16*8)
W.write(armor_slots+(10%16)*8,W.u32(10)..W.u32(0))
W.write(manager+MG.map,W.u64(armor_slots)..W.u32(16)..W.u32(0)..W.u32(1))
local descriptor=W.alloc(0x20)
W.write(descriptor+MG.descriptorEntity,W.u32(10))
W.write(manager+MG.descriptors,W.u64(descriptor))
local applied=manager+MG.applied
W.write(applied,W.u32(0));W.write(applied+R.armorKit,W.u32(0xB513FD54))
W.write(applied+R.helmetKit,W.u32(0x12345678))
-- The UI sound context is the same native game object as the SDK game-state root.
local ctx=require('hd2runtime/core/bytes').pointer(W.read(W.GAME+D.sound.uiSound.context,8),0)
local game_world,wwise_world=W.alloc(64),W.alloc(64)
W.write(W.GAME+D.sound.uiSound.context,W.u64(ctx))
W.write(ctx+D.sound.uiSound.world,W.u64(game_world))
W.write(ctx+D.sound.uiSound.wwiseWorld,W.u64(wwise_world))
local app=W.alloc(D.sound.worldList.worldArray+16)
local app_array=W.alloc(16)
W.write(W.EXE+D.sound.worldList.application,W.u64(app))
W.write(app+D.sound.worldList.worldCount,W.u32(1))
W.write(app+D.sound.worldList.worldArray,W.u64(app_array));W.write(app_array,W.u64(game_world))
local engine_world,engine_ww=newproxy(true),newproxy(true)
local posted,has_calls={},{}
local resident=true
stingray={Application={worlds=function()return{engine_world}end},
 Wwise={wwise_world=function(w)assert(w==engine_world);return engine_ww end,
 has_event=function(name)has_calls[#has_calls+1]=name;return resident end},
 WwiseWorld={trigger_event=function(ww,name,pos)
  assert(ww==engine_ww);posted[#posted+1]={name=name,position=pos};return 77 end},
 Vector3=setmetatable({},{__call=function(_,x,y,z)return{x=x,y=y,z=z}end}),
 Script={temp_count=function()return 2 end,set_temp_count=function(n)assert(n==2)end}}
local now,session,context=1,'fixture-world:mission1',true
local adapter={context=function()return context end,now=function()return now end,session=function()return session end,
 read_all=identity.read_all,validate=identity.validate,available=sound.available,play=sound.play}
local spec={playback_qualified=true,assets_qualified=true,event=D.sound.eventName}
local route=routing.new(adapter,spec)
local function death()return{peer=LOCAL,avatar_id=100,local_player=true,observed='dead_state',cause={source='native'},position={x=1,y=2,z=3}}end
local function alive()local ok,why=route.observe();assert(ok,why)end
local function dead()W.set(100,{life=2,health=0});now=now+0.1 end
local function no_post(event,code)
 local result,why=route.on_death(event or death());assert(not result and why==code, tostring(why)..' expected '..code)
 assert(#posted==0)
end
'''


CASES: list[tuple[str, str]] = [
    ("normal_brawny_native_post", "alive();dead();local r,c=route.on_death(death());assert(r and c=='PLAYED'and r.style=='lego_stormtrooper'and#posted==1 and posted[1].name==D.sound.eventName)"),
    ("normal_lean_arbitrary_helmet", "W.write(applied,W.u32(1));alive();dead();assert(select(2,route.on_death(death()))=='PLAYED')"),
    ("bikini_brawny_normal_helmet", "W.write(applied+R.armorKit,W.u32(0xE9ADD047));W.write(applied+R.helmetKit,W.u32(0x5C3087D2));alive();dead();local r,c=route.on_death(death());assert(c=='PLAYED'and r.style=='lego_bikini_stormtrooper')"),
    ("bikini_lean_any_helmet", "W.write(applied,W.u32(1));W.write(applied+R.armorKit,W.u32(0xE9ADD047));alive();dead();assert(select(2,route.on_death(death()))=='PLAYED')"),
    ("ordinary_body_lego_helmet", "W.write(applied+R.armorKit,W.u32(0x11111111));W.write(applied+R.helmetKit,W.u32(0x2F748B84));assert(not route.observe());dead();no_post(nil,'NO_MATCHING_LEGO_AVATAR')"),
    ("other_player_only_lego", "alive();dead();local e=death();e.local_player=false;e.peer=REMOTE;no_post(e,'INVALID_DEATH')"),
    ("avatar_removed_rejected", "alive();dead();local e=death();e.observed='avatar_removed';no_post(e,'UNKNOWN_DEATH_OBSERVATION')"),
    ("synthetic_mod_cause", "alive();dead();local e=death();e.cause.source='mod';no_post(e,'NOT_NATIVE_DEATH')"),
    ("missing_cause", "alive();dead();local e=death();e.cause=nil;no_post(e,'NOT_NATIVE_DEATH')"),
    ("malformed_cause", "alive();dead();local e=death();e.cause=5;no_post(e,'NOT_NATIVE_DEATH')"),
    ("no_alive_baseline", "dead();no_post(nil,'NO_MATCHING_LEGO_AVATAR')"),
    ("stale_observation", "alive();dead();now=2;no_post(nil,'STALE_ARMOR_OBSERVATION')"),
    ("backwards_clock", "alive();dead();now=0.5;no_post(nil,'STALE_ARMOR_OBSERVATION')"),
    ("nan_clock", "alive();dead();now=0/0;no_post(nil,'INVALID_CLOCK')"),
    ("infinite_clock", "alive();dead();now=1/0;no_post(nil,'INVALID_CLOCK')"),
    ("mission_epoch_changed", "alive();dead();session='fixture-world:mission2';no_post(nil,'NO_MATCHING_LEGO_AVATAR')"),
    ("context_changed", "alive();dead();context=false;no_post(nil,'CONTEXT_REFUSED')"),
    ("peer_malformed", "alive();dead();local e=death();e.peer='abcdefghijklmnop';no_post(e,'INVALID_DEATH')"),
    ("different_avatar", "alive();dead();local e=death();e.avatar_id=101;no_post(e,'NO_MATCHING_LEGO_AVATAR')"),
    ("not_really_dead", "alive();now=1.1;no_post(nil,'NOT_DEAD')"),
    ("despawn_not_dead", "alive();W.remove(100);now=1.1;no_post(nil,'NOT_DEAD')"),
    ("ship_transition", "alive();dead();W.state(3);no_post(nil,'NOT_IN_MISSION')"),
    ("applied_record_changed", "alive();dead();W.write(applied+R.helmetKit,W.u32(0x2F748B84));no_post(nil,'APPLIED_ARMOR_CHANGED')"),
    ("applied_body_changed", "alive();dead();W.write(applied,W.u32(1));no_post(nil,'APPLIED_ARMOR_CHANGED')"),
    ("body_invalid", "W.write(applied,W.u32(2));assert(not route.observe());dead();no_post(nil,'NO_MATCHING_LEGO_AVATAR')"),
    ("armor_is_helmet_category", "W.write(kit1+K.type,W.u32(1));assert(not route.observe());dead();no_post(nil,'NO_MATCHING_LEGO_AVATAR')"),
    ("duplicate_kit_id", "W.write(kit2+K.id,W.u32(0xB513FD54));assert(not route.observe());dead();no_post(nil,'NO_MATCHING_LEGO_AVATAR')"),
    ("applied_descriptor_wrong_entity", "W.write(descriptor+MG.descriptorEntity,W.u32(11));assert(not route.observe());dead();no_post(nil,'NO_MATCHING_LEGO_AVATAR')"),
    ("applied_index_out_of_range", "W.write(armor_slots+(10%16)*8+4,W.u32(4));assert(not route.observe());dead();no_post(nil,'NO_MATCHING_LEGO_AVATAR')"),
    ("cached_kit_pointer_changed", "alive();W.write(kit_array,W.u64(kit2));dead();no_post(nil,'APPLIED_ARMOR_CHANGED')"),
    ("cached_kit_category_changed", "alive();W.write(kit1+K.type,W.u32(1));dead();no_post(nil,'APPLIED_ARMOR_CHANGED')"),
    ("kit_manager_changed", "alive();W.write(W.GAME+MG.globalRva,W.u64(0));dead();no_post(nil,'WORLD_CHANGED')"),
    ("position_missing", "alive();dead();local e=death();e.position=nil;no_post(e,'NO_DEATH_POSITION')"),
    ("position_wrong_type", "alive();dead();local e=death();e.position=42;no_post(e,'NO_DEATH_POSITION')"),
    ("position_nan", "alive();dead();local e=death();e.position.x=0/0;no_post(e,'NO_DEATH_POSITION')"),
    ("position_infinite", "alive();dead();local e=death();e.position.y=1/0;no_post(e,'NO_DEATH_POSITION')"),
    ("position_far", "alive();dead();local e=death();e.position.z=100001;no_post(e,'NO_DEATH_POSITION')"),
    ("deduplicate", "alive();dead();assert(select(2,route.on_death(death()))=='PLAYED');assert(select(2,route.on_death(death()))=='ALREADY_HANDLED'and#posted==1)"),
    ("post_failure_never_retried", "stingray.WwiseWorld.trigger_event=function()error('fixture')end;alive();dead();no_post(nil,'POST_FAILED');no_post(nil,'ALREADY_HANDLED')"),
    ("event_not_resident", "resident=false;alive();dead();no_post(nil,'EVENT_NOT_RESIDENT')"),
    ("assets_unqualified", "spec.assets_qualified=false;alive();dead();no_post(nil,'ASSETS_UNQUALIFIED')"),
    ("diagnostic_only", "spec.playback_qualified=false;alive();dead();assert(select(2,route.on_death(death()))=='DIAGNOSTIC_ONLY'and#posted==0)"),
    ("identity_native_instruction_changed", "local p=D.identity.pins[1];W.write(W.GAME+p.rva,string.rep('X',#p.hex/2));assert(not identity.read_all())"),
    ("gameworld_instruction_changed", "local p=D.sound.selectorPins[1];W.write((p.module=='exe'and W.EXE or W.GAME)+p.rva,string.rep('X',#p.hex/2));local r,c=sound.available(D.sound.eventName);assert(not r and c=='GAME_WORLD_PIN_CHANGED')"),
    ("plugin_instruction_changed", "local p=D.sound.plugin.pins[1];W.write(plugin+p.rva,string.rep('X',#p.hex/2));local r,c=sound.available(D.sound.eventName);assert(not r and c=='PLUGIN_PIN_CHANGED')"),
    ("plugin_image_changed", "W.write(plugin+128+80,W.u32(1));local r,c=sound.available(D.sound.eventName);assert(not r and c=='PLUGIN_IMAGE_CHANGED')"),
    ("plugin_missing", "W.runtime.module=function(name)if name==D.sound.plugin.module then return nil end;return original_module(name)end;assert(select(2,sound.available(D.sound.eventName))=='PLUGIN_NOT_LOADED')"),
    ("wrong_event_literal", "local r,c=sound.play('other_event',{position={1,2,3}});assert(not r and c=='WRONG_EVENT'and#has_calls==0 and#posted==0)"),
    ("numeric_event_rejected", "assert(select(2,sound.available(4055635130))=='WRONG_EVENT'and#has_calls==0)"),
    ("gameworld_duplicate", "W.write(app+D.sound.worldList.worldCount,W.u32(2));W.write(app_array+8,W.u64(game_world));assert(select(2,sound.available(D.sound.eventName))=='DUPLICATE_GAME_WORLD')"),
    ("gameworld_not_listed", "W.write(app_array,W.u64(wwise_world));assert(select(2,sound.available(D.sound.eventName))=='GAME_WORLD_NOT_LISTED')"),
    ("engine_world_mismatch", "stingray.Application.worlds=function()return{}end;assert(select(2,sound.available(D.sound.eventName))=='ENGINE_WORLD_MISMATCH')"),
    ("engine_world_not_userdata", "stingray.Application.worlds=function()return{{}}end;assert(select(2,sound.available(D.sound.eventName))=='ENGINE_WORLD_MISMATCH')"),
    ("sound_api_missing", "stingray.WwiseWorld=nil;assert(select(2,sound.available(D.sound.eventName))=='NO_SOUND_API')"),
    ("sound_bad_position", "assert(select(2,sound.play(D.sound.eventName,{position={1,2,0/0}}))=='BAD_POSITION'and#has_calls==0)"),
    ("identity_cached_bounded_reads", "alive();local before=reads;assert(identity.read_all());assert(reads-before<100,'native pins and kit table unexpectedly rescanned: '..(reads-before))"),
    ("sdk_dead_state_dispatch", r'''
require('hd2runtime/runtime/event_world').set_runtime(W.runtime)
local events=require('hd2runtime/runtime/events')
require('hd2runtime/runtime/event_sources')
adapter.now=events.now
adapter.session=function()local mission,epoch=events.mission();return mission and('sdk:'..epoch)or nil end
local dispatched=0
events.subscribe('player_died',function(e)
 dispatched=dispatched+1;local r,c=route.on_death(e);assert(r and c=='PLAYED',c)
end,{owner='mods/codex/yoda_fixture'})
events.timer('every',0.1,function()route.observe()end,{owner='mods/codex/yoda_fixture'})
update(0.1);update(0.1);update(0.1)
W.set(100,{life=2,health=0});update(0.1)
assert(dispatched==1 and#posted==1,'actual SDK failed confirmed dead-state route')
update(0.1);assert(#posted==1)
'''),
]


def run(prepared: Path, output: Path) -> dict[str, Any]:
    if output.exists() or not output.resolve().is_relative_to(B.AREA):
        raise ValueError("Fresh scoped output required")
    pre, inputs = prefix(prepared)
    vm = B.runner()
    results = []
    for name, body in CASES:
        script = pre + SETUP + "\n" + body + "\nassert(#posted<=1)\nreturn 'passed'\n"
        try:
            outcome = vm.execute(script.encode()).decode()
        except Exception as exc:
            raise RuntimeError(f"Fixture {name} failed") from exc
        assert outcome == "passed", (name, outcome)
        results.append({"name": name, "passed": True, "freshStandaloneState": True})
    output.mkdir(parents=True)
    (output / "fixture-setup.lua").write_text(SETUP, encoding="utf-8")
    (output / "cases.json").write_text(json.dumps(CASES, indent=2) + "\n", encoding="utf-8")
    report = {"passed": True, "tool": B.pin(Path(__file__)), "prepared": B.pin(prepared / "report.json"),
              "modules": [B.pin(prepared / (name + ".lua")) for name in ("profile", "world", "identity", "routing", "sound")],
              "sdkInputs": inputs, "fixtures": results, "fixtureCount": len(results),
              "nativeProcessRead": False, "nativeCallExecuted": False, "liveChanges": False,
              "evidence": "Actual pinned 0.28.1 SDK sparse-memory event fixture and standalone Lua VM. Native engine calls are synthetic records.",
              "limits": ["Loaded-game native instruction proofs and real Wwise event residency remain unobserved.",
                         "Coverage requires native dead_state; instantaneous gib/removal between polls intentionally produces no scream."]}
    path = output / "report.json"
    path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return B.pin(path)


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prepared", type=Path, default=B.AREA / "prepared-v2")
    parser.add_argument("--out", type=Path, default=B.AREA / "fixtures-v1")
    logging.basicConfig(level=logging.INFO)
    try:
        args = parser.parse_args(argv)
        print(json.dumps(run(args.prepared, args.out)))
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        logging.exception("Offline fixture failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
