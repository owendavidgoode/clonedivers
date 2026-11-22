-- Lists accidental globals in a built Lua file: any global write, and any read of a
-- name that is not a standard global. No output means clean.
--   luajit check.lua build/passive_picker.built.lua   (run from this folder, stdin closed)
local f = assert(loadfile(arg[1]))
local known = {} for k in pairs(_G) do known[k] = true end
local bc = require("jit.util")
local seen, out = {}, {}
local function walk(fn)
  if seen[fn] then return end seen[fn] = true
  local info = bc.funcinfo(fn)
  for i = -1, -info.gcconsts, -1 do
    local k = bc.funck(fn, i)
    if type(k) == "proto" then walk(k) end
  end
  for pc = 1, info.bytecodes - 1 do
    local ins, m = bc.funcbc(fn, pc)
    local op = bit.band(ins, 0xff)
    local name = require("jit.vmdef").bcnames:sub(op * 6 + 1, op * 6 + 6):gsub("%s", "")
    if name == "GSET" or name == "GGET" then
      local d = bit.rshift(ins, 16)
      local k = bc.funck(fn, -d - 1)
      out[name .. " " .. tostring(k)] = true
    end
  end
end
walk(f)
local list = {} for k in pairs(out) do list[#list + 1] = k end table.sort(list)
for _, k in ipairs(list) do
  local kind, name = k:match("^(%S+) (.+)$")
  if kind == "GSET" or not known[name] then print(k) end
end
