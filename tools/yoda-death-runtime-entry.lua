local Context=require('codex_lego_yoda/context')
local Identity=require('codex_lego_yoda/identity')
local Routing=require('codex_lego_yoda/routing')
local Sound=require('codex_lego_yoda/sound')
local World=require('codex_lego_yoda/world')
local Events=require('hd2runtime/runtime/events')
local mod=hd2.mod('codex_lego_yoda_death')
local runtime
local state={status='waiting_for_empire',runtimeAcceptance=false,scope='local applied LEGO body only'}
local armed=false
local function log(message)mod:log('LEGO Yoda death: '..message)end
local adapter={
 context=function()return armed end,
 read_all=Identity.read_all,validate=Identity.validate,available=Sound.available,play=Sound.play,
 now=Events.now,
 session=function()
  local epoch,in_mission=hd2.events.mission_id()
  local world=World.open()
  if not in_mission or not world then return nil end
  return world.key..':mission:'..tostring(epoch)
 end,
}
local route=Routing.new(adapter,{playback_qualified=true,assets_qualified=true,event='clonedivers_lego_yoda_death'})
local function recheck()
 if not armed then return nil,'NOT_ARMED' end
 local ok,why=pcall(Context.recheck,runtime)
 if not ok then armed=false;route.reset();state.status='disarmed';return nil,tostring(why)end
 return true
end
local elapsed,last_reason=0,nil
mod:every(0.1,function()
 if not armed then
  if state.status=='disarmed'then return end
  elapsed=elapsed+0.1;if elapsed<2.5 then return end;elapsed=0
  local ok,why=pcall(function()
   runtime=runtime or require('hd2runtime/runtime/windows_readonly')()
   Context.require(runtime)
  end)
  if not ok then
   if tostring(why)~=last_reason then last_reason=tostring(why);log('waiting: '..last_reason)end
   return
  end
  armed=true;state.status='armed';log('armed for the local LEGO Stormtrooper and LEGO bikini body')
 end
 local ok,why=route.observe();state.lastObservation=ok and 'observed'or why
end)
mod:on('mission_started',function()
 route.reset()
 local ok,why=recheck()
 if not ok and why~='NOT_ARMED'then log('mission refused: '..why)end
 if ok then route.observe()end
end)
mod:on('mission_ended',function()route.reset()end)
mod:on('player_died',function(event)
 if event.local_player~=true then return end
 local ok,why=recheck()
 if not ok then log('death refused: '..tostring(why));return end
 local decision,code=route.on_death(event)
 state.lastDeath={status=code,style=decision and decision.style}
 if code=='PLAYED'then log('posted once for '..decision.style)
 else log('death refused: '..tostring(code)..'; observation='..tostring(state.lastObservation))end
end)
state.routing=route
return state
