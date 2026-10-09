-- Conditional policy prototype. No voice selection or shared death-cue replacement.
-- A reviewed integration supplies the context/asset gates; defaults never play.
local M={}
local ARMOR={B513FD54='lego_stormtrooper',E9ADD047='lego_bikini_stormtrooper'}
local function finite(n)return type(n)=='number'and n==n and math.abs(n)<=100000 end
local function clock(n)return type(n)=='number'and n==n and n>=0 and n<1e12 end
local function valid_session(n)return type(n)=='string'and#n>0 and#n<=256 end
local function peer(n)return type(n)=='string'and#n==16 and n:match('^%x+$')end
local function avatar(n)return type(n)=='number'and n>0 and n<4294967296 and n%1==0 end
function M.new(adapter,spec)
    assert(type(adapter)=='table'and type(spec)=='table','explicit adapter and spec required')
    local cache,consumed={},{}
    local self={observations=0,qualified_deaths=0,posts=0,refusals=0}
    local function refuse(code)self.refusals=self.refusals+1;return nil,code end
    function self.reset()cache={};consumed={}end
    function self.observe()
        if not adapter.context()then self.reset();return nil,'CONTEXT_REFUSED' end
        local records,why=adapter.read_all()
        if not records then self.reset();return nil,why end
        local session=adapter.session()
        if not valid_session(session)then self.reset();return nil,'NO_SESSION' end
        local now,next_cache=adapter.now(),{}
        if not clock(now)then self.reset();return nil,'INVALID_CLOCK' end
        for _,row in ipairs(records)do
            if peer(row.peer)and avatar(row.avatar)and row.alive==true and row.is_local==true then
                if ARMOR[row.armor_id]then
                    row.observed_at=now;row.session=session
                    next_cache[row.peer]=row
                    self.observations=self.observations+1
                end
            elseif row.peer and cache[row.peer]then
                -- A dead state can be read just before player_died is flushed.
                next_cache[row.peer]=cache[row.peer]
            end
        end
        cache=next_cache
        return true
    end
    function self.on_death(event)
        if not adapter.context()then self.reset();return refuse('CONTEXT_REFUSED')end
        if type(event)~='table'or event.local_player~=true or not avatar(event.avatar_id)or not peer(event.peer)then
            return refuse('INVALID_DEATH')
        end
        local row=cache[event.peer]
        if not row or row.avatar~=event.avatar_id or row.session~=adapter.session()then return refuse('NO_MATCHING_LEGO_AVATAR')end
        if type(event.cause)~='table'or event.cause.source~='native'then return refuse('NOT_NATIVE_DEATH')end
        if event.observed~='dead_state'then return refuse('UNKNOWN_DEATH_OBSERVATION')end
        local now=adapter.now()
        if not clock(now)or not clock(row.observed_at)then return refuse('INVALID_CLOCK')end
        local age=now-row.observed_at
        if age<0 or age>0.5 then return refuse('STALE_ARMOR_OBSERVATION')end
        local key=row.session..':'..event.peer..':'..tostring(event.avatar_id)
        if consumed[key]then return refuse('ALREADY_HANDLED')end
        local verified,why=adapter.validate(row,event)
        if not verified then return refuse(why or'IDENTITY_REFUSED')end
        local p=event.position
        if type(p)~='table'or not finite(p.x)or not finite(p.y)or not finite(p.z)then return refuse('NO_DEATH_POSITION')end
        local decision={style=ARMOR[row.armor_id],peer=event.peer,avatar_id=event.avatar_id,
            armor_id=row.armor_id,position={p.x,p.y,p.z},session=row.session}
        self.qualified_deaths=self.qualified_deaths+1
        if spec.playback_qualified~=true then return decision,'DIAGNOSTIC_ONLY' end
        if spec.assets_qualified~=true or type(spec.event)~='string'or#spec.event==0 then return refuse('ASSETS_UNQUALIFIED')end
        if adapter.available(spec.event)~=true then return refuse('EVENT_NOT_RESIDENT')end
        consumed[key]=true -- never retry a post failure into a repeated death scream
        local played,code=adapter.play(spec.event,{position=decision.position})
        if not played then return refuse(code or'NOT_PLAYED')end
        self.posts=self.posts+1
        return decision,'PLAYED'
    end
    return self
end
return M
