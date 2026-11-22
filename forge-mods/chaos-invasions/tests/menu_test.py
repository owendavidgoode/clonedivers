#!/usr/bin/env python3
"""Exercise the shipped Chaos 0.3.0 Lua bridge with the installed standalone LuaJIT DLL."""
from __future__ import annotations

import ctypes
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
DLL = Path('D:/HellForge/game/bin/lua51.dll')


def main() -> int:
    lib = ctypes.CDLL(str(DLL))
    lib.luaL_newstate.restype = ctypes.c_void_p
    lib.luaL_openlibs.argtypes = [ctypes.c_void_p]
    lib.luaL_loadstring.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
    lib.luaL_loadstring.restype = ctypes.c_int
    lib.lua_pcall.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_int]
    lib.lua_pcall.restype = ctypes.c_int
    lib.lua_tolstring.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_size_t)]
    lib.lua_tolstring.restype = ctypes.c_char_p
    lib.lua_close.argtypes = [ctypes.c_void_p]

    source = (ROOT / 'src/menu_bridge.lua').read_text(encoding='utf-8')
    harness = r'''
local checks=0
local function check(v) checks=checks+1; assert(v,'menu check '..checks) end
local text='session=123\nenabled=1\ncooldown_seconds=150\nlanding_grace_seconds=45\ntotal_cap=12\nfaction_cap=6\nsquad_size=4\nmission_budget=36\ndroid_weight=100\nsquid_weight=80\nbug_weight=100\n'
local specs,values,callbacks={},{},{}
local writes=0
local old_calls=0
local function env()
    local e={}
    for k,v in pairs(_G) do e[k]=v end
    e._G=e
    e.os={getenv=function(k) check(k=='LOCALAPPDATA'); return 'X:/fixture' end}
    e.io={open=function(path,mode)
        check(path=='X:/fixture/Clonedivers/ChaosInvasions.values')
        if mode=='rb' then
            return {read=function(_,n) check(n==2048); return text end,close=function() end}
        end
        check(mode=='wb'); writes=writes+1; text=''
        return {write=function(_,...)
            for i=1,select('#',...) do text=text..tostring(select(i,...)) end
        end,close=function() end}
    end}
    e.ModOptionsMenu={
        register_option=function(id,s) check(not specs[id]); specs[id]=s; return true end,
        set=function(id,v) values[id]=v end,
        on_change=function(id,cb) callbacks[id]=cb end,
    }
    e.update=function() old_calls=old_calls+1 end
    return e
end
local e=env()
local chunk=assert(loadstring(SOURCE,'chaos-menu')); setfenv(chunk,e); chunk()
e.update()
local n=0; for _ in pairs(specs) do n=n+1 end
check(n==10); check(old_calls==1)
check(specs['chaos_invasions.enabled'].label=='ENABLE CHAOS INVASIONS')
check(specs['chaos_invasions.enabled'].mod=='Chaos Invasions')
check(specs['chaos_invasions.enabled'].mod_id=='clonedivers.chaos-invasions')
check(specs['chaos_invasions.enabled'].default==true)
check(values['chaos_invasions.enabled']==true)
check(specs['chaos_invasions.cooldown_seconds'].min==30 and specs['chaos_invasions.cooldown_seconds'].max==900)
check(specs['chaos_invasions.landing_grace_seconds'].min==15 and specs['chaos_invasions.landing_grace_seconds'].max==300)
check(values['chaos_invasions.cooldown_seconds']==150)
check(values['chaos_invasions.landing_grace_seconds']==45)
check(values['chaos_invasions.squad_size']==4)
check(values['chaos_invasions.mission_budget']==36)
check(values['chaos_invasions.squid_weight']==80)
check(not specs['chaos_invasions.enabled'].description:find('BLOCKED',1,true))
callbacks['chaos_invasions.enabled'](false)
check(text:find('enabled=0',1,true)~=nil); check(writes==1)
callbacks['chaos_invasions.cooldown_seconds']('chaos_invasions.cooldown_seconds',30)
check(text:find('cooldown_seconds=30',1,true)~=nil); check(writes==2)
callbacks['chaos_invasions.landing_grace_seconds'](15)
check(text:find('landing_grace_seconds=15',1,true)~=nil); check(writes==3)
local before=text
callbacks['chaos_invasions.cooldown_seconds'](29)
callbacks['chaos_invasions.landing_grace_seconds'](14)
callbacks['chaos_invasions.total_cap'](25)
callbacks['chaos_invasions.squad_size'](2.5)
callbacks['chaos_invasions.bug_weight'](0/0)
check(text==before and writes==3)
text=text:gsub('session=123','session=456')
callbacks['chaos_invasions.enabled'](true)
check(writes==3)
local other=env(); other.ModOptionsMenu=nil
chunk=assert(loadstring(SOURCE,'chaos-menu-absent')); setfenv(chunk,other); chunk(); other.update()
check(old_calls==2)
return checks
'''
    program = 'local SOURCE=' + json.dumps(source) + '\n' + harness
    state = lib.luaL_newstate()
    if not state:
        raise MemoryError('luaL_newstate failed')
    try:
        lib.luaL_openlibs(state)
        rc = lib.luaL_loadstring(state, program.encode('utf-8'))
        if not rc:
            rc = lib.lua_pcall(state, 0, 1, 0)
        value = lib.lua_tolstring(state, -1, None)
        message = value.decode('utf-8', errors='replace') if value else '(no result)'
        if rc:
            raise RuntimeError(message)
        count = int(message)
    finally:
        lib.lua_close(state)

    proof = {
        'passed': True,
        'checks': count,
        'version': '0.3.2',
        'runtime': str(DLL),
        'runtime_sha256': hashlib.sha256(DLL.read_bytes()).hexdigest(),
        'source_sha256': hashlib.sha256(source.encode()).hexdigest(),
        'io_mocked': True,
        'game_launched': False,
    }
    (ROOT / 'build/menu-tests.json').write_text(json.dumps(proof, indent=2) + '\n', encoding='utf-8')
    print(f'PASS {count} LuaJIT menu checks (0.3.2); mocked IO; no game launch')
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        sys.exit(130)
