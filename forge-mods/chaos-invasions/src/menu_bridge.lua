-- HD2-Addon: mods/clonedivers/chaos_invasions
-- Session menu bridge via ModOptionsMenu + %LOCALAPPDATA%/Clonedivers/ChaosInvasions.values
local rows = {
    {'enabled','ENABLE CHAOS INVASIONS',1,0,1,'Toggle cross-faction Chaos waves during missions (150s default cooldown, 45s landing grace).'},
    {'cooldown_seconds','EVENT COOLDOWN (SECONDS)',150,10,900,'Seconds between foreign Chaos waves after landing grace expires.'},
    {'landing_grace_seconds','LANDING GRACE (SECONDS)',45,6,300,'Initial seconds after landing before the first Chaos wave can trigger.'},
    {'total_cap','CHAOS POPULATION CAP (EXCLUDES SEAF)',12,1,640,'Maximum living Chaos-spawned foreign enemies across all invader factions.'},
    {'faction_cap','CHAOS CAP PER INVADER FACTION',6,1,400,'Maximum living Chaos-spawned enemies allowed for a single foreign faction.'},
    {'squad_size','UNITS PER EVENT',4,1,32,'Enemies dispatched per Chaos swarm cluster, Dropship, Warp Ship, or Terminid breach.'},
    {'mission_budget','MAXIMUM CHAOS UNITS PER MISSION',36,1,25000,'Total Chaos enemies allowed across an entire mission.'},
    {'droid_weight','DROID INVASION WEIGHT',100,0,200,'Relative weight for Automaton / CIS Dropship & Swarm invasions when mission faction is not Bots.'},
    {'squid_weight','SQUID SHIP WEIGHT',80,0,200,'Relative weight for Illuminate / Covenant invasions when mission faction is not Squids.'},
    {'bug_weight','BUG BREACH WEIGHT',100,0,200,'Relative weight for Terminid Swarm invasions when mission faction is not Bugs.'},
    {'air_runs_enabled','TACTICAL AIR & GUNSHIP RUNS (D11/D12)',1,0,1,'Enable periodic friendly Eagle Storm fighter runs and enemy gunship strafing runs on Difficulty 11 & 12.'},
    {'atte_walker_support','AUTONOMOUS REPUBLIC AT-TE SUPPORT (D12 CLONES)',1,0,1,'Deploy an autonomous outer-perimeter Republic AT-TE fire-support walker prototype on Level 12 Clone Wars.'},
}
local base = os and os.getenv and os.getenv('LOCALAPPDATA')
local path = base and (base .. '/Clonedivers/ChaosInvasions.values')
local function read()
    if not path then return nil end
    local f = io.open(path,'rb')
    if not f then return nil end
    local text = f:read(2048) or ''
    f:close()
    local values = {}
    for key,value in text:gmatch('([%w_]+)=(%d+)') do values[key]=tonumber(value) end
    if not values.session then return nil end
    for _,row in ipairs(rows) do
        local n=values[row[1]]
        if not n or n<row[4] or n>row[5] then return nil end
    end
    return values
end
local registered=false
local function step()
    if registered then return end
    local menu=rawget(_G,'ModOptionsMenu')
    if type(menu)~='table' or type(menu.register_option)~='function' or
       type(menu.on_change)~='function' or type(menu.set)~='function' then return end
    local initial=read()
    if not initial then return end
    -- All closures retain this session token; stale UI cannot change another session.
    local session=initial.session
    registered=true
    for _,row in ipairs(rows) do
        local key,label,default,lo,hi,description=unpack(row)
        local id='chaos_invasions.'..key
        local toggle=(key=='enabled' or key=='air_runs_enabled' or key=='atte_walker_support')
        local default_value=default
        if toggle then default_value=default~=0 end
        local ok=menu.register_option(id,{
            mod='Chaos Invasions',mod_id='clonedivers.chaos-invasions',
            type=toggle and 'toggle' or 'slider',label=label,
            default=default_value,min=lo,max=hi,step=1,
            description=description,
        })
        if ok then
            local value=initial[key]
            if toggle then value=value~=0 end
            menu.set(id,value)
            menu.on_change(id,function(first,second)
                local value=first
                if first==id then value=second end
                if type(value)=='boolean' then value=value and 1 or 0 end
                if type(value)~='number' or value~=value or value<lo or value>hi or value%1~=0 then return end
                local current=read()
                if not current or current.session~=session then return end
                current[key]=value
                local f=io.open(path,'wb')
                if not f then return end
                f:write('session=',tostring(session),'\n')
                for _,r in ipairs(rows) do f:write(r[1],'=',tostring(current[r[1]]),'\n') end
                f:close()
            end)
        end
    end
end
local loader=rawget(_G,'CowboyBingusModLoader')
if type(loader)=='table' and type(loader.after_startup)=='function' then loader.after_startup(step) end
local previous=rawget(_G,'update')
update=function(...)
    if not registered then pcall(step) end
    if type(previous)=='function' then return previous(...) end
end
