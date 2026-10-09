-- Uses code / research from HD2Runtime by SkyeShade.
-- https://github.com/SkyeShade/HD2Runtime (41184521, LICENSE sections 2/3).
-- Minimal positional-post backport from sound_events/ui_sound.
-- Only the one reviewed additive event is accepted. No global controls/bank loads.
local world_module=require('codex_lego_yoda/world')
local b=require('hd2runtime/core/bytes')
local D=require('codex_lego_yoda/profile').sound
local M={}
local proven
local function callable(v)
    if type(v)=='function'then return true end
    local mt=(type(v)=='table'or type(v)=='userdata')and getmetatable(v)
    return type(mt)=='table'and type(rawget(mt,'__call'))=='function'
end
local function pointer(world,at)
    local raw=at and world.view.read(at,8)
    if not raw or b.u32(raw,4)>0x7FFF then return nil end
    local n=b.u32(raw,0)+b.u32(raw,4)*4294967296
    return n>=65536 and n or nil
end
local function prove(world)
    local runtime=world.runtime
    local handle=runtime.module(D.plugin.module)
    if not handle then return nil,'PLUGIN_NOT_LOADED' end
    local base=runtime.address(handle)
    if proven and proven.world==world and proven.base==base then return true end
    for _,pin in ipairs(D.selectorPins)do
        local module=pin.module=='exe'and world.exe or world.game
        if not world.view.proves(module+pin.rva,pin.hex)then return nil,'GAME_WORLD_PIN_CHANGED' end
    end
    local header=world.view.read(base,4096)
    if not header or header:sub(1,2)~='MZ'then return nil,'PLUGIN_HEADER' end
    local pe=b.u32(header,60)
    if pe<64 or pe+88>#header or header:sub(pe+1,pe+4)~='PE\0\0'or b.u32(header,pe+80)~=D.plugin.imageSize then
        return nil,'PLUGIN_IMAGE_CHANGED'
    end
    for _,pin in ipairs(D.plugin.pins)do
        if not world.view.proves(base+pin.rva,pin.hex)then return nil,'PLUGIN_PIN_CHANGED' end
    end
    proven={world=world,base=base}
    return true
end
local function bindings()
    local world,why=world_module.open()
    if not world then return nil,why end
    local ok,code=prove(world)
    if not ok then return nil,code end
    local S=rawget(_G,'stingray')
    if not(type(S)=='table'and type(S.Application)=='table'and callable(S.Application.worlds)
        and type(S.Wwise)=='table'and callable(S.Wwise.wwise_world)and callable(S.Wwise.has_event)
        and type(S.WwiseWorld)=='table'and callable(S.WwiseWorld.trigger_event)and callable(S.Vector3))then
        return nil,'NO_SOUND_API'
    end
    local context=world.view.pointer(world.game+D.uiSound.context)
    local game_world=context and pointer(world,context+D.uiSound.world)
    local wwise_world=context and pointer(world,context+D.uiSound.wwiseWorld)
    if not game_world or not wwise_world then return nil,'NO_GAME_WORLD' end
    local O=D.worldList
    local app=world.view.pointer(world.exe+O.application)
    local count=app and world.view.u32(app+O.worldCount)
    local array=app and world.view.pointer(app+O.worldArray)
    if not(count and count>0 and count<=64 and array)then return nil,'BAD_WORLD_LIST' end
    local index
    for i=0,count-1 do
        local p=pointer(world,array+i*8)
        if not p then return nil,'WORLD_LIST_UNREADABLE' end
        if p==game_world then
            if index then return nil,'DUPLICATE_GAME_WORLD' end
            index=i+1
        end
    end
    if not index then return nil,'GAME_WORLD_NOT_LISTED' end
    local read,list=pcall(S.Application.worlds)
    if not read or type(list)~='table'or#list~=count or type(list[index])~='userdata'then return nil,'ENGINE_WORLD_MISMATCH' end
    return {S=S,world=list[index]}
end
function M.available(name)
    if name~=D.eventName then return nil,'WRONG_EVENT' end
    local api,why=bindings()
    if not api then return nil,why end
    local ok,has=pcall(api.S.Wwise.has_event,name)
    return ok and has==true
end
function M.play(name,opts)
    if name~=D.eventName then return nil,'WRONG_EVENT' end
    local p=type(opts)=='table'and opts.position
    if type(p)~='table'or#p~=3 then return nil,'BAD_POSITION' end
    for i=1,3 do if type(p[i])~='number'or p[i]~=p[i]or math.abs(p[i])>100000 then return nil,'BAD_POSITION' end end
    local api,why=bindings()
    if not api then return nil,why end
    local S=api.S
    local known,has=pcall(S.Wwise.has_event,name)
    if not known or has~=true then return nil,'EVENT_NOT_RESIDENT' end
    -- Exact primary engine_gui.temp_scope ownership: return Vector3 temporaries.
    local script=type(S.Script)=='table'and S.Script
    local mark
    if script and callable(script.temp_count)and callable(script.set_temp_count)then
        local ok,n=pcall(script.temp_count)
        if ok and type(n)=='number'and n>=0 and n<=1048575 and n%1==0 then mark=n end
    end
    local ok,playing=pcall(function()
        local ww=S.Wwise.wwise_world(api.world)
        return S.WwiseWorld.trigger_event(ww,name,S.Vector3(p[1],p[2],p[3]))
    end)
    if mark then pcall(script.set_temp_count,mark)end
    if not ok then return nil,'POST_FAILED' end
    if type(playing)~='number'or playing%1~=0 or playing<=0 then return nil,'NOT_PLAYED' end
    return {playing=playing,event=name,position={p[1],p[2],p[3]}}
end
function M.reset_for_tests()proven=nil end
return M
