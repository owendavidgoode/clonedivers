-- HD2-Addon: mods/jarvis/melee_picker
-- Dedicated Melee Slot picker 1.0.11, for Bingus Shared Loader v15+ and Dedicated Melee Slot 1.5.2+.
--
-- One clickable box on the pre-mission player card names the extra melee weapon of
-- Dedicated Melee Slot (the HellForge code mod). A click opens a short list
-- (CQC-42 Machete, CQC-19 Stun Lance, CQC-2 Saber, CQC-30 Stun Baton, CQC-5 Combat
-- Hatchet, CQC-72 Entrenchment Tool, or none); a pick is written to melee-slot-weapon.txt (Documents\HellForge and, for a sandboxed game, the Logs folder),
-- which that mod reads. This mod touches no game data: it only draws the box with the
-- game's own Gui and writes the one text file.
-- Drawing, signatures, card finding and the mouse are SAPP's proven technique.
--
-- Log:      Logs/MeleePicker.log (in the CowboyBingus log folder)
-- Settings: Logs/MeleePicker.cfg (where the box and the list sit; box and list can be
--           dragged with the left mouse button and the new spot is saved)

if rawget(_G, "JarvisMeleePicker") then return true end
local REVISION = "Dedicated Melee Slot picker 1.5.2-clonedivers.2"
local state = { revision = REVISION, frames = 0, status = "starting" }
rawset(_G, "JarvisMeleePicker", state)

local ffi = require("ffi")
local bit = require("bit")
local DIR = (os.getenv("LOCALAPPDATA") or ".") .. "/CowboyBingus/Helldivers2/Logs/"
local LOG = DIR .. "MeleePicker.log"
pcall(function () local old = DIR .. "MeleePicker.previous.log" os.remove(old) os.rename(LOG, old) end)
pcall(function () local f = io.open(LOG, "w") if f then f:write(REVISION .. "\n") f:close() end end)
local lines = 0
local function log(text)
	state.status = text
	lines = lines + 1
	if lines > 600 then return end
	pcall(function ()
		local f = io.open(LOG, "a")
		if f then f:write("[frame " .. state.frames .. "] " .. text, "\n") f:close() end
	end)
end

-- ------------------------------------------------------------------ data
-- Filled in by build.py from data/sigs.json.
local SIGS = --@SIGS@

-- The melee weapons the code mod knows, in list order. id = what melee-slot-weapon.txt holds.
-- Each weapon's stats and traits are copied from the game's own armory screen (read 2026-11-01).
local MELEES = {
	{ id = "machete",    label = "CQC-42 MACHETE", short = "MACHETE",
	  stats = { { "DAMAGE", "300" }, { "ATTACK SPEED", "0.50" }, { "STAMINA DRAIN", "005" } },
	  traits = { "MEDIUM ARMOR PENETRATING", "ONE HANDED", "MELEE" } },
	{ id = "stun-lance", label = "CQC-19 STUN LANCE", short = "STUN LANCE",
	  stats = { { "DAMAGE", "165" }, { "ATTACK SPEED", "1.00" }, { "STAMINA DRAIN", "003" } },
	  traits = { "MEDIUM ARMOR PENETRATING", "ONE HANDED", "STUN", "MELEE" } },
	{ id = "saber",      label = "CQC-2 SABER", short = "SABER",
	  stats = { { "DAMAGE", "188" }, { "ATTACK SPEED", "1.28" }, { "STAMINA DRAIN", "003" } },
	  traits = { "MEDIUM ARMOR PENETRATING", "ONE HANDED", "MELEE" } },
	{ id = "stun-baton", label = "CQC-30 STUN BATON", short = "STUN BATON",
	  stats = { { "DAMAGE", "120" }, { "ATTACK SPEED", "1.28" }, { "STAMINA DRAIN", "003" } },
	  traits = { "MEDIUM ARMOR PENETRATING", "ONE HANDED", "STUN", "MELEE" } },
	{ id = "hatchet",    label = "CQC-5 COMBAT HATCHET", short = "HATCHET",
	  stats = { { "DAMAGE", "240" }, { "ATTACK SPEED", "1.00" }, { "STAMINA DRAIN", "004" } },
	  traits = { "MEDIUM ARMOR PENETRATING", "ONE HANDED", "MELEE" } },
	{ id = "shovel",     label = "CQC-72 ENTRENCHMENT TOOL", short = "ENTRENCH TOOL",
	  stats = { { "DAMAGE", "165" }, { "ATTACK SPEED", "1.28" }, { "STAMINA DRAIN", "003" } },
	  traits = { "MEDIUM ARMOR PENETRATING", "ONE HANDED", "STUN", "MELEE" } },
	{ id = "none",       label = "NO EXTRA MELEE", short = "NONE" },
}
-- The stats panel appears after the cursor (or the W/S highlight) has rested on one weapon this long, in seconds.
local TIP = { delay = 0.5, wall = os.clock }
local ALIASES = { ["cqc-42"] = "machete", ["cqc-19"] = "stun-lance", ["cqc-2"] = "saber", ["cqc-30"] = "stun-baton", ["cqc-5"] = "hatchet", ["cqc-72"] = "shovel" }

-- ------------------------------------------------------------------ memory
pcall(ffi.cdef, [[
typedef struct {
  void *base; void *allocation_base; uint32_t allocation_protection;
  uint16_t partition; uint16_t reserved; size_t size;
  uint32_t state; uint32_t protection; uint32_t type;
} JarvisMPRegion;]])
pcall(ffi.cdef, "size_t VirtualQuery(const void *address, void *region, size_t size);")
pcall(ffi.cdef, "void *GetModuleHandleA(const char *name);")
pcall(ffi.cdef, "short GetAsyncKeyState(int key);")
pcall(ffi.cdef, "void *GetForegroundWindow(void);")
pcall(ffi.cdef, "uint32_t GetWindowThreadProcessId(void *window, uint32_t *pid);")
pcall(ffi.cdef, "uint32_t GetCurrentProcessId(void);")
pcall(ffi.cdef, "int GetCursorPos(void *);")
pcall(ffi.cdef, "int ScreenToClient(void *, void *);")
pcall(ffi.cdef, "int GetClientRect(void *, void *);")
local kernel = ffi.load("kernel32")
local user = ffi.load("user32")
local MEM_COMMIT = 0x1000
local READABLE = { [0x02] = true, [0x04] = true, [0x08] = true, [0x20] = true, [0x40] = true, [0x80] = true }
local LOW, HIGH = 0x10000, 0x7FFFFFFFFFFF
local region = ffi.new("JarvisMPRegion[1]")
local RSIZE = ffi.sizeof(region[0])

local function query(addr)
	if type(addr) ~= "number" or addr < LOW or addr > HIGH then return nil end
	if kernel.VirtualQuery(ffi.cast("void *", addr), region, RSIZE) ~= RSIZE then return nil end
	local r = region[0]
	return tonumber(ffi.cast("uintptr_t", r.base)), tonumber(r.size),
		r.state == MEM_COMMIT and READABLE[tonumber(r.protection)] == true, tonumber(r.protection)
end

local function readable(a, n)
	local stop = a + n
	while a < stop do
		local b, s, ok = query(a)
		if not ok then return false end
		a = b + s
	end
	return true
end

local function u8(a) return ffi.cast("const uint8_t *", a)[0] end
local function u32(a) return tonumber(ffi.cast("const uint32_t *", a)[0]) end
local function f32(a) return tonumber(ffi.cast("const float *", a)[0]) end
local function ptr(a)
	local p = ffi.cast("const uint32_t *", a)
	local lo, hi = tonumber(p[0]), tonumber(p[1])
	if hi >= 32768 then return nil end
	local v = lo + hi * 4294967296
	if v < LOW then return nil end
	return v
end
local function need(a, n, what)
	if not (a and readable(a, n)) then error(what .. " unreadable", 0) end
	return a
end

-- ------------------------------------------------------------------ keys
local pidbox = ffi.new("uint32_t[1]")
local function game_focused()
	local w = user.GetForegroundWindow()
	if w == nil then return false end
	user.GetWindowThreadProcessId(w, pidbox)
	return pidbox[0] == kernel.GetCurrentProcessId()
end

-- List navigation keys: one step on the press, then steps while held. A key
-- marked -1 (already down when the list opened) must be let go first.
local VK_W, VK_S, VK_SPACE = 0x57, 0x53, 0x20
local NAV_KEYS = { VK_W, VK_S, VK_SPACE }
local nav = {}
local function nav_block()
	for _, vk in ipairs(NAV_KEYS) do nav[vk] = -1 end
end
local function nav_step(vk, repeats)
	local down = bit.band(user.GetAsyncKeyState(vk), 0x8000) ~= 0
	local n = nav[vk] or 0
	if not down then n = 0 elseif n ~= -1 then n = n + 1 end
	nav[vk] = n
	return (n == 1 or (repeats and n > 24 and n % 5 == 0)) and game_focused()
end

-- ------------------------------------------------------------------ signatures
local function unhex(h)
	local t = {}
	for i = 1, #h, 2 do t[#t + 1] = tonumber(h:sub(i, i + 1), 16) end
	return t
end

local base, image_size
local scan = nil
local globals = nil

local function prepare_scan()
	local handle = kernel.GetModuleHandleA("game.dll")
	if handle == nil then error("game.dll not loaded", 0) end
	base = tonumber(ffi.cast("uintptr_t", handle))
	need(base, 0x1000, "game.dll header")
	local pe = base + u32(base + 0x3C)
	need(pe, 0x100, "game.dll PE header")
	if u32(pe) ~= 0x00004550 then error("game.dll PE signature missing", 0) end
	image_size = u32(pe + 0x50)
	if image_size < 0x100000 or image_size > 0x20000000 then error("game.dll size rejected", 0) end
	state.pe_timestamp = u32(pe + 8)
	for _, s in ipairs(SIGS) do
		s.bytes, s.maskb, s.anchorb = unhex(s.hex), unhex(s.mask), unhex(s.anchor)
		local a = s.anchorb
		s.key = a[1] + a[2] * 256 + a[3] * 65536 + a[4] * 16777216
		s.hits = {}
	end
	scan = { cursor = base }
	log(string.format("game.dll at 0x%X, size %d, stamp %d; scanning for %d signatures", base, image_size, state.pe_timestamp, #SIGS))
end

local function matches(s, at)
	local p = ffi.cast("const uint8_t *", at)
	local b, m = s.bytes, s.maskb
	for i = 1, #b do
		if m[i] ~= 0 and p[i - 1] ~= b[i] then return false end
	end
	return true
end

local SLICE = 0.004
local function scan_step()
	local deadline = os.clock() + SLICE
	local stop_all = base + image_size
	while os.clock() < deadline do
		if scan.cursor >= stop_all then return true end
		local b, s, ok = query(scan.cursor)
		if not b or s <= 0 then return true end
		local rend = math.min(b + s, stop_all)
		if ok then
			local from = scan.cursor
			local to = math.min(rend, from + 262144)
			local p = ffi.cast("const uint8_t *", from)
			local last = to - from - 4
			for i = 0, last do
				local v = p[i] + p[i + 1] * 256 + p[i + 2] * 65536 + p[i + 3] * 16777216
				for k = 1, #SIGS do
					local sig = SIGS[k]
					if v == sig.key then
						local start = from + i - sig.anchor_offset
						if start >= base and start + #sig.bytes <= stop_all
							and readable(start, #sig.bytes) and matches(sig, start) then
							sig.hits[#sig.hits + 1] = start
						end
					end
				end
			end
			scan.cursor = (to < rend) and (to - 3) or rend
		else
			scan.cursor = rend
		end
	end
	return false
end

local function resolve_globals()
	globals = {}
	for _, s in ipairs(SIGS) do
		local seen, count, first = {}, 0, nil
		for _, at in ipairs(s.hits) do
			if not seen[at] then seen[at] = true; count = count + 1; first = first or at end
		end
		if count ~= 1 and s.optional then
			log(string.format("signature %s matched %d times; the feature that needs '%s' is off", s.name, count, s.symbol))
		elseif count ~= 1 then
			error(string.format("signature %s matched %d times (need exactly 1)", s.name, count), 0)
		end
		if count == 1 then
			local rel = u32(first + s.extract_offset)
			if rel >= 2147483648 then rel = rel - 4294967296 end
			local slot = first + s.extract_end + rel
			if slot < base or slot + 8 > base + image_size then
				error("signature " .. s.name .. " points outside game.dll", 0)
			end
			if globals[s.symbol] and globals[s.symbol] ~= slot then
				error("signatures disagree about " .. s.symbol, 0)
			end
			globals[s.symbol] = slot
			log(string.format("signature %s at game.dll+0x%X -> %s slot game.dll+0x%X", s.name, first - base, s.symbol, slot - base))
		end
	end
end

-- ------------------------------------------------------------------ game input
-- The game's own "consume input" function (as SAPP and HD2 Transmog use it): makes the game's
-- menus ignore the menu keys for a frame, so W/S/Space in the list do not also move the game's menu.
local MENU_ACTIONS = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 15, 18, 19, 27, 28, 29, 30 }
local EXECUTABLE = { [0x10] = true, [0x20] = true, [0x40] = true, [0x80] = true }
local consume_type = ffi.typeof("void (*)(void *, uint64_t, float)")
local action_values = {}
local native = {
	executable = function (at)
		local _, _, _, protection = query(at)
		return EXECUTABLE[protection] == true
	end,
	consume = function (at, holder, action)
		local value = action_values[action]
		if not value then value = ffi.new("uint64_t", action * 4294967296); action_values[action] = value end
		ffi.cast(consume_type, at)(ffi.cast("void *", holder), value, -1)
	end,
}
local input = nil
local function resolve_input()
	local fn = globals and globals.input_consume
	if not fn then return end
	local ok, why = pcall(function ()
		local LENGTH = 155
		need(fn, LENGTH, "input function")
		if not native.executable(fn) then error("input function is not code", 0) end
		local p = ffi.cast("const uint8_t *", fn)
		local global, marks = nil, {}
		for i = 0, LENGTH - 8 do
			if i >= 20 and p[i] == 0x48 and p[i + 1] == 0x8B and p[i + 2] == 0x0D then
				if global then error("more than one global load in the input function", 0) end
				local rel = u32(fn + i + 3)
				if rel >= 2147483648 then rel = rel - 4294967296 end
				global = fn + i + 7 + rel
			end
			if p[i + 1] == 0x03 and p[i + 2] == 0 and p[i + 3] == 0 then marks[p[i]] = true end
		end
		if not (marks[0x28] and marks[0x2C] and marks[0x34]) then error("input function body differs", 0) end
		if not global then error("input global not found", 0) end
		if global < base or global + 8 > base + image_size or global % 8 ~= 0 then error("input global outside game.dll", 0) end
		need(global, 8, "input global")
		input = { fn = fn, global = global }
	end)
	if ok then log(string.format("input: consume function at game.dll+0x%X, input global game.dll+0x%X", fn - base, input.global - base))
	else log("input: key blocking off, " .. tostring(why)) end
end

local block = { dead = false, frames = 0, said = false }
local function block_keys()
	if block.dead or not input then return end
	local ok, why = pcall(function ()
		local holder = ptr(need(input.global, 8, "input global"))
		if not holder then return end
		need(holder, 0x340, "input singleton")
		for _, action in ipairs(MENU_ACTIONS) do native.consume(input.fn, holder, action) end
		block.frames = block.frames + 1
		if not block.said then block.said = true; log(string.format("input: blocking the game's menu keys while the list is open (singleton 0x%X)", holder)) end
	end)
	if not ok then
		block.dead = true
		log("input: key blocking stopped, " .. tostring(why))
	end
end

-- ------------------------------------------------------------------ the choice
-- melee-slot-weapon.txt, the file Dedicated Melee Slot reads. Two homes, because a sandboxed
-- game (HellForge 0.3.0 default) cannot write Documents\HellForge but can write its own Logs folder:
--   Documents\HellForge\melee-slot-weapon.txt         (best effort; works unsandboxed and on 0.2.0)
--   %LOCALAPPDATA%\CowboyBingus\Helldivers2\Logs\...   (always tried; the code mod reads it too)
-- A pick writes both; the code mod and this box use whichever file is newer.
local FILE = "melee-slot-weapon.txt"
local function doc_path()
	local home = os.getenv("USERPROFILE")
	if not home or home == "" then return nil end
	return home .. "\\Documents\\HellForge\\" .. FILE
end
local function env_path() return DIR .. FILE end

-- Last-write time of a file as one number (0 when it is missing or the time cannot be read).
local mtime_ok = pcall(function ()
	ffi.cdef[[
	typedef struct { unsigned long lo, hi; } JMP_FT;
	typedef struct { unsigned long attrs; JMP_FT created, accessed, written; unsigned long sizeHi, sizeLo; } JMP_FAD;
	int __stdcall GetFileAttributesExA(const char *name, int level, void *info);
	]]
end)
local function mtime(path)
	if not mtime_ok or not path then return 0 end
	local ok, t = pcall(function ()
		local info = ffi.new("JMP_FAD")
		if ffi.C.GetFileAttributesExA(path, 0, info) == 0 then return 0 end
		return tonumber(info.written.hi) * 4294967296 + tonumber(info.written.lo)
	end)
	return ok and t or 0
end

local function read_file(path)
	if not path then return nil end
	local f = io.open(path, "r")
	if not f then return nil end
	local text = f:read("*a") or ""
	f:close()
	return text
end

-- The text the code mod will use: the newer of the two files (the Logs one if the times cannot be read).
local function current_text()
	local a, b = read_file(doc_path()), read_file(env_path())
	if a and b then
		if mtime(doc_path()) > mtime(env_path()) then return a end
		return b
	end
	return b or a
end

-- The melee id now in the file: one of MELEES' ids, "other" for a hex id, "none" for a missing or empty file.
local function read_choice()
	local text = current_text()
	if not text then return "none" end
	local word = text:match("^%s*([^\r\n]-)%s*$") or ""
	word = word:lower():gsub("[ _]", "-")
	if word == "" then return "none" end
	word = ALIASES[word] or word
	for _, m in ipairs(MELEES) do if m.id == word then return m.id end end
	return "other"
end

local function write_to(path, id)
	if not path then return false end
	local f = io.open(path, "wb")
	if not f then return false end
	f:write(id, "\r\n")
	f:close()
	return true
end

local function write_choice(id)
	local okEnv = write_to(env_path(), id)
	local okDoc = write_to(doc_path(), id)
	if not okEnv and not okDoc then log("pick not saved: neither " .. env_path() .. " nor Documents\\HellForge could be written") return false end
	if not okDoc then log("pick: Documents\\HellForge is not writable here (sandbox); saved to the Logs folder instead, the code mod reads it") end
	if read_choice() ~= id then log("pick written but reads back differently: " .. id) return false end
	log("pick saved: " .. id)
	return true
end

local function label_of(id)
	for _, m in ipairs(MELEES) do if m.id == id then return m.label end end
	if id == "other" then return "CUSTOM (by id)" end
	return "?"
end

local function short_of(id)
	for _, m in ipairs(MELEES) do if m.id == id then return m.short end end
	if id == "other" then return "CUSTOM" end
	return "?"
end

-- ------------------------------------------------------------------ settings
-- Positions are in card units (the card is 420 wide), measured from the
-- lower-left corner of the EQUIPMENT button; x goes right, y goes up.
local CFG = DIR .. "MeleePicker.cfg"
local SETTINGS = {
	{ "box_x", 213, "melee box: left edge (213 = right half of the card, lined up with SAPP's passive box)" },
	{ "box_y", 82, "melee box: bottom edge (negative = below the EQUIPMENT button; SAPP's boxes sit at -44, this is the row under them)" },
	{ "box_w", 201, "melee box: width" },
	{ "box_h", 26, "melee box: height" },
	{ "box_text", 15, "melee box: text size" },
	{ "equip_y", 370, "the Equipment page is showing when the button sits at or above this screen height" },
	{ "eq_box_x", 213, "melee box on the Equipment page: left edge" },
	{ "eq_box_y", 78, "melee box on the Equipment page: bottom edge (SAPP's box is at 46)" },
	{ "grid_x", 213, "list: left edge" },
	{ "grid_y", 111, "list: bottom edge" },
	{ "cell_w", 205, "list: width of one row" },
	{ "cell_h", 22, "list: height of one row" },
	{ "grid_text", 14, "list: text size" },
	{ "box_alpha", 215, "box: how solid its black background is (0 = see-through, 255 = solid)" },
	{ "grid_alpha", 238, "list: how solid its black background is" },
	{ "drag", 1, "1 = the box and the list can be dragged with the left mouse button and the new spot is saved here, 0 = locked" },
	{ "hold_diverkit", 1, "1 = DiverKit ignores the mouse while it is on the box or the list is open, 0 = off" },
	{ "block_keys", 1, "1 = keep W/S/Space away from the game's own menu while the list is open, 0 = off" },
}
local cfg, cfg_text, cfg_rev = {}, nil, 0
for _, s in ipairs(SETTINGS) do cfg[s[1]] = s[2] end

local function parse_cfg(text)
	local out = {}
	for _, s in ipairs(SETTINGS) do out[s[1]] = s[2] end
	for line in text:gmatch("[^\r\n]+") do
		local key, value = line:match("^%s*([%a_]+)%s*=%s*(%-?[%d%.]+)")
		value = tonumber(value)
		if key and out[key] ~= nil and value and value > -5000 and value < 5000 then out[key] = value end
	end
	out.box_alpha = math.max(0, math.min(255, math.floor(out.box_alpha)))
	out.grid_alpha = math.max(0, math.min(255, math.floor(out.grid_alpha)))
	for _, key in ipairs({ "box_w", "box_h", "box_text", "cell_w", "cell_h", "grid_text" }) do
		out[key] = math.max(6, out[key])
	end
	return out
end

local function write_cfg(values)
	pcall(function ()
		local w = io.open(CFG, "wb")
		if not w then return end
		w:write("# Melee Picker: where the box and the list sit on the player card.\r\n")
		w:write("# Card units (the card is 420 wide), from the lower-left corner of the\r\n")
		w:write("# EQUIPMENT button; x goes right, y goes up. Read again every 2 seconds\r\n")
		w:write("# while the card is on screen. Delete this file to get the defaults back.\r\n")
		for _, s in ipairs(SETTINGS) do
			w:write(string.format("%s = %s\r\n", s[1], tostring(values and values[s[1]] or s[2])), "#   " .. s[3] .. "\r\n")
		end
		w:close()
	end)
end

local function load_cfg()
	local f = io.open(CFG, "r")
	if not f then
		if cfg_text == nil then
			cfg_text = ""
			write_cfg(nil)
		end
		return false
	end
	local text = f:read("*a") or ""
	f:close()
	if text == cfg_text then return false end
	cfg_text = text
	cfg = parse_cfg(text)
	cfg_rev = cfg_rev + 1
	return true
end

-- ------------------------------------------------------------------ player card
-- Where the local player's card is on the loadout screen (layout from DiverKit's screen check, as SAPP).
-- nil while the card is not up, is animating, or shows something unexpected.
local function find_card()
	if not (globals and globals.manager and readable(globals.manager, 8)) then return nil end
	local manager = ptr(globals.manager)
	if not manager or not readable(manager + 0x62a0, 24) then return nil end
	local bucket = manager + 0x62a0
	if u32(bucket) ~= 1 or u32(bucket + 16) ~= 0xe5 then return nil end
	local owner = ptr(bucket + 8)
	if not owner or not readable(owner, 0x2810) then return nil end
	if u32(owner + 8) ~= 1 then return nil end                       -- 1 = the stratagem loadout view
	if not readable(owner + 0x273990, 1) then return nil end
	if u8(owner + 0x273990) ~= 0 or u8(owner + 0x2808) ~= 0 then return nil end   -- screen animating
	local slot = u32(owner + 0x27d0)
	if slot >= 4 then return nil end
	local payload = owner + slot * 0x9f0 + 0x10
	if not readable(payload, 0x140) then return nil end
	local card
	for i = 0, 3 do
		local candidate = owner + 0x53a78 + i * 0x1ee18
		if readable(candidate + 0x1edf0, 16) and ptr(candidate + 0x1edf0) == payload
			and u32(candidate + 0x1edfc) == slot then
			if card then return nil end
			card = candidate
		end
	end
	if not card then return nil end
	local widget = card + 0x1b820                                     -- the EQUIPMENT button
	if not readable(widget, 164) or bit.band(u32(widget), 0x10) == 0 then return nil end
	local opacity, sx, sy = f32(widget + 84), f32(widget + 100), f32(widget + 140)
	if not (opacity >= 0.995 and opacity <= 1.01 and sx >= 0.3 and sx <= 4 and math.abs(sx - sy) < 0.01) then
		return nil
	end
	local x, y, w, h = f32(widget + 148), f32(widget + 156), f32(widget + 36) * sx, f32(widget + 40) * sy
	if not (x >= 0 and x <= 32768 and y >= 0 and y <= 32768 and w >= 100 and w <= 32768 and h >= 10 and h <= 32768) then
		return nil
	end
	return { x = x, y = y, w = w, h = h, slot = slot, card = card, payload = payload }
end

-- ------------------------------------------------------------------ box and list
-- Drawn with the game's own Gui, the way SAPP and DiverKit draw: a screen surface in the menu
-- world, the game's font, and the mouse read from Windows. A surface keeps what was drawn on it,
-- so it is only rebuilt when something visible changes.
local engine = rawget(_G, "stingray")
local ui = { open = false, dead = false, visible = false, notes = 0 }

local function id_hex(a)
	local p = ffi.cast("const uint32_t *", a)
	local lo, hi = tonumber(p[0]), tonumber(p[1])
	if lo == 0 and hi == 0 then return nil end
	return string.format("%08x%08x", hi, lo)
end

local function read_handles()
	if not (globals and globals.font and globals.atlas and globals.material) then return nil, "font signatures not found" end
	if not (readable(globals.font, 8) and readable(globals.atlas, 8) and readable(globals.material, 8)) then
		return nil, "font slots unreadable"
	end
	local holder = ptr(globals.material)
	if not holder or not readable(holder + 24, 8) then return nil, "font material not ready" end
	local font, atlas, material = id_hex(globals.font), id_hex(globals.atlas), id_hex(holder + 24)
	if not (font and atlas and material) then return nil, "font not ready" end
	return { font = font, atlas = atlas, material = material, key = font .. atlas .. material }
end

local function ui_clear()
	if ui.gui and ui.world and engine and engine.Application and engine.World then
		local ok, worlds = pcall(engine.Application.worlds)
		local alive = false
		if ok and type(worlds) == "table" then
			for _, w in ipairs(worlds) do if w == ui.world then alive = true end end
		end
		if alive then pcall(engine.World.destroy_gui, ui.world, ui.gui) end
	end
	ui.gui, ui.world, ui.sig = nil, nil, nil
end

local TITLE_H = 20
local function layout(card, gw, gh, open, drag)
	local s = card.w / 420
	local out = { s = s, regions = {}, cells = {}, open = open }
	local function clamp(v, low, high) return math.max(low, math.min(v, math.max(low, high))) end
	local function moved(key) if drag and drag.key == key then return drag.dx, drag.dy end return 0, 0 end
	if open then
		local rows = #MELEES
		local w, h = cfg.cell_w * s, (rows * cfg.cell_h + TITLE_H) * s
		local dx, dy = moved("grid")
		local x = clamp(card.x + cfg.grid_x * s + dx, 2, gw - w - 2)
		local y = clamp(card.y + cfg.grid_y * s + dy, 2, gh - h - 2)
		out.grid = { x = x, y = y, w = w, h = h, key = "grid" }
		out.regions[#out.regions + 1] = out.grid
		for i, m in ipairs(MELEES) do
			local cell = { x = x, y = y + h - TITLE_H * s - i * cfg.cell_h * s, w = w, h = cfg.cell_h * s, key = "m:" .. m.id, id = m.id }
			out.cells[#out.cells + 1] = cell
			out.regions[#out.regions + 1] = cell
		end
	end
	local bw, bh = cfg.box_w * s, cfg.box_h * s
	local equip = card.y / s >= cfg.equip_y
	local bx, by = equip and cfg.eq_box_x or cfg.box_x, equip and cfg.eq_box_y or cfg.box_y
	local dx, dy = moved("box")
	out.equip = equip
	out.box = { x = clamp(card.x + bx * s + dx, 0, gw - bw), y = clamp(card.y + by * s + dy, 0, gh - bh),
		w = bw, h = bh, key = "box", text = cfg.box_text }
	out.regions[#out.regions + 1] = out.box
	return out
end

local function hit(regions, x, y)
	for i = #regions, 1, -1 do
		local r = regions[i]
		if x >= r.x and x < r.x + r.w and y >= r.y and y < r.y + r.h then return r.key end
	end
end

-- Mouse position in Gui units (origin bottom-left) and the left button.
local point, client = ffi.new("int32_t[2]"), ffi.new("int32_t[4]")
local point_arg, client_arg = ffi.cast("void *", point), ffi.cast("void *", client)
local function pointer_sample(gw, gh)
	local window = user.GetForegroundWindow()
	if window == nil then return nil end
	user.GetWindowThreadProcessId(window, pidbox)
	if pidbox[0] ~= kernel.GetCurrentProcessId() then return nil end
	if user.GetCursorPos(point_arg) == 0 or user.ScreenToClient(window, point_arg) == 0
		or user.GetClientRect(window, client_arg) == 0 then return nil end
	local w, h = client[2] - client[0], client[3] - client[1]
	local x, y = point[0], point[1]
	if w <= 0 or h <= 0 or x < 0 or y < 0 or x >= w or y >= h then return nil end
	local down
	local mouse = engine and engine.Mouse
	local ok, value = false, nil
	if mouse then ok, value = pcall(function () return mouse.button(mouse.button_id("left")) end) end
	if ok then down = value == true or (type(value) == "number" and value > 0)
	else down = bit.band(user.GetAsyncKeyState(1), 0x8000) ~= 0 end
	return { x = x * gw / w, y = (h - y) * gh / h, down = down }
end

-- A click is a press and a release over the same region. A button already held when the card
-- appears or the window regains focus never counts.
local function pointer_step(sample, over)
	if not sample then ui.previous, ui.armed = nil, nil return nil end
	local clicked
	if ui.previous ~= nil then
		if sample.down and not ui.previous then ui.armed = over end
		if not sample.down and ui.previous then
			if over and over == ui.armed then clicked = over end
			ui.armed = nil
		end
	end
	ui.previous = sample.down
	return clicked
end

local function draw(card, lay, handles, hover, gw, gh, current, tip)
	local App, World, Gui = engine.Application, engine.World, engine.Gui
	local target
	local main = App.main_world()
	for _, w in ipairs(App.worlds() or {}) do if w ~= main then target = w break end end
	if not target then ui_clear() return end
	local sig = string.format("%.1f,%.1f,%.1f,%.1f|%d,%d|%s|%s|%s|%s|%d", card.x, card.y, card.w, card.h, gw, gh, handles.key,
		tostring(current), tostring(hover), tostring(lay.open), cfg_rev)
		.. "|tip=" .. tostring(tip)
		.. string.format("|%.1f,%.1f|%.1f,%.1f", lay.box.x, lay.box.y, lay.grid and lay.grid.x or 0, lay.grid and lay.grid.y or 0)
	if ui.world == target and ui.sig == sig then return end
	ui_clear()
	local gui = World.create_screen_gui(target, "scale", 1, 1)
	if not gui then error("screen surface unavailable", 0) end
	ui.gui, ui.world = gui, target
	local ids = engine.IdString64.from_hex
	local font, material = ids(handles.font), ids(handles.material)
	local ink = Gui.material(gui, material)
	if not ink then error("font material unavailable", 0) end
	-- the font material's settings, as DiverKit sets them for plain text
	local function slot(h) return ids(h .. "00000000") end
	for _, h in ipairs({ "8035c266", "5e8455fe", "309e7783", "82b803a8" }) do
		engine.Material.set_scalar(ink, slot(h), 0)
	end
	engine.Material.set_vector2(ink, slot("e13777ce"), engine.Vector2(1, -1))
	engine.Material.set_vector4(ink, slot("7701209e"), engine.Color(0, 0, 0, 0))
	engine.Material.set_texture(ink, slot("88bac99b"), ids(handles.atlas))
	local function color(r, g, b, a) return engine.Color(a or 255, r, g, b) end
	local gold, white, muted = color(255, 225, 0), color(238, 242, 246), color(153, 171, 184)
	local function rect(r, c, z)
		Gui.rect(gui, engine.Vector3(r.x, r.y, z or 960), engine.Vector2(r.w, r.h), c)
	end
	local function outline(r, c, t)
		rect({ x = r.x, y = r.y, w = r.w, h = t }, c, 962)
		rect({ x = r.x, y = r.y + r.h - t, w = r.w, h = t }, c, 962)
		rect({ x = r.x, y = r.y, w = t, h = r.h }, c, 962)
		rect({ x = r.x + r.w - t, y = r.y, w = t, h = r.h }, c, 962)
	end
	local function measure(value, size)
		local lo, hi = Gui.text_extents(gui, value, font, size)
		if not (lo and hi) then return #value * size * 0.5, 0 end
		return engine.Vector2.x(hi) - engine.Vector2.x(lo), engine.Vector2.x(lo)
	end
	-- one line of text inside r, shrunk to fit its width; middle = centered, else from the left
	local function text(value, r, size, c, pad, middle, z)
		local room = r.w - 2 * pad
		local lo, hi = Gui.text_extents(gui, value, font, size)
		if lo and hi then
			local wide = engine.Vector2.x(hi) - engine.Vector2.x(lo)
			if wide > room and wide > 0 then size = size * room / wide end
		end
		local x = r.x + pad
		if middle then
			local width, left = measure(value, size)
			x = math.max(x, r.x + (r.w - width) / 2 - left)
		end
		Gui.text(gui, value, font, size, material, engine.Vector3(x, r.y + (r.h - size * 0.7) / 2, z or 963), c)
	end
	local s = lay.s
	local t = math.max(1, s)
	local box = lay.box
	rect(box, color(0, 0, 0, cfg.box_alpha))
	outline(box, (hover == "box" or lay.open) and gold or muted, t)
	text("MELEE: " .. short_of(current), box, box.text * s, white, 6 * s, true)
	if lay.grid then
		local grid = lay.grid
		rect(grid, color(0, 0, 0, cfg.grid_alpha))
		outline(grid, gold, t)
		text("EXTRA MELEE   W S + SPACE", { x = grid.x, y = grid.y + grid.h - TITLE_H * s, w = grid.w, h = TITLE_H * s }, 12 * s, muted, 6 * s)
		for _, cell in ipairs(lay.cells) do
			if hover == cell.key then rect(cell, color(70, 70, 70, 255), 961) end
			local on = cell.id == current
			if on then rect(cell, gold, 962) end
			if hover == cell.key then
				local frame = math.max(2, 2 * s)
				outline({ x = cell.x - frame, y = cell.y - frame, w = cell.w + 2 * frame, h = cell.h + 2 * frame }, gold, frame)
			end
			text(label_of(cell.id), cell, cfg.grid_text * s, on and color(12, 12, 12) or white, 6 * s, true)
		end
	end
	if tip then
		-- the stats panel: just right of the list, top level with the highlighted row (right of the box when the list is closed); on the left when the screen has no room
		local id = tip == "box" and current or tip:sub(3)
		local entry
		for _, m in ipairs(MELEES) do if m.id == id then entry = m end end
		if entry and entry.stats then
			local size, head = cfg.grid_text * s, (cfg.grid_text + 2) * s
			local pw = 280 * s
			local lh = size * 1.5
			local rows = 1 + #entry.stats + 1 + #entry.traits
			local gap = lh * 0.4
			local ph = 8 * s + head * 1.6 + rows * lh + gap
			local row, edge = lay.box, lay.box
			if lay.grid then
				edge = lay.grid
				for _, cell in ipairs(lay.cells) do if cell.key == tip then row = cell end end
			end
			local px, py = edge.x + edge.w + 8 * s, row.y + row.h - ph
			if px + pw > gw then px = edge.x - pw - 8 * s end
			px = math.max(0, math.min(px, gw - pw))
			py = math.max(0, math.min(py, gh - ph))
			local panel = { x = px, y = py, w = pw, h = ph }
			rect(panel, color(0, 0, 0, math.max(cfg.grid_alpha, 235)), 970)
			for _, r in ipairs({ { panel.x, panel.y, panel.w, t }, { panel.x, panel.y + panel.h - t, panel.w, t },
					{ panel.x, panel.y, t, panel.h }, { panel.x + panel.w - t, panel.y, t, panel.h } }) do
				rect({ x = r[1], y = r[2], w = r[3], h = r[4] }, gold, 971)
			end
			local cursor = panel.y + panel.h - head * 1.6 - 4 * s
			text(entry.label, { x = panel.x, y = cursor, w = panel.w, h = head * 1.6 }, head, gold, 8 * s, false, 972)
			local function line(value, c, extra)
				cursor = cursor - lh - (extra or 0)
				text(value, { x = panel.x, y = cursor, w = panel.w, h = lh }, size, c, 8 * s, false, 972)
			end
			line("STATS", muted)
			for _, st in ipairs(entry.stats) do
				line(st[1], white)
				local width = measure(st[2], size)
				text(st[2], { x = panel.x + panel.w - 16 * s - width - 2, y = cursor, w = width + 16 * s + 4, h = lh }, size, white, 8 * s, false, 972)
			end
			line("WEAPON TRAITS", muted, gap)
			for _, tr in ipairs(entry.traits) do line(tr, white) end
		end
	end
	ui.sig = sig
end

local function ui_note(text)
	if text == ui.last_note or ui.notes >= 60 then return end
	ui.last_note, ui.notes = text, ui.notes + 1
	log(text)
end

-- Holds DiverKit still, or lets it go. Only a flag this mod cleared itself is ever set again.
local function hold_diverkit(want)
	local dk = rawget(_G, "CodexLoadouts")
	if type(dk) ~= "table" then ui.held = false return end
	if want then
		ui.free_frames = 0
		if dk.visible == true then
			dk.visible = false
			ui.held = true
			if not ui.held_said then ui.held_said = true; log("diverkit: held still while the mouse is on the box or the list is open") end
		end
	elseif ui.held then
		ui.free_frames = (ui.free_frames or 0) + 1
		if ui.free_frames >= 3 then
			if dk.visible == false then dk.visible = true end
			ui.held = false
		end
	end
end

local function drag_drop(card, lay)
	local s = lay.s
	local function units(v) return math.floor(v / s * 10 + 0.5) / 10 end
	if ui.drag.key == "box" then
		local kx, ky = lay.equip and "eq_box_x" or "box_x", lay.equip and "eq_box_y" or "box_y"
		cfg[kx], cfg[ky] = units(lay.box.x - card.x), units(lay.box.y - card.y)
		log(string.format("moved: box on the %s page to %s,%s", lay.equip and "Equipment" or "loadout", tostring(cfg[kx]), tostring(cfg[ky])))
	elseif lay.grid then
		cfg.grid_x, cfg.grid_y = units(lay.grid.x - card.x), units(lay.grid.y - card.y)
		log(string.format("moved: list to %s,%s", tostring(cfg.grid_x), tostring(cfg.grid_y)))
	end
	write_cfg(cfg)
	cfg_rev = cfg_rev + 1
end

local function ui_hide()
	if ui.visible then ui_note("box: hidden (card not on screen)") end
	ui_clear()
	ui.visible, ui.open, ui.previous, ui.armed, ui.sel = false, nil, nil, nil, nil
	ui.press, ui.drag, ui.was_down = nil, nil, nil
end

local function ui_step()
	if not (engine and engine.Gui and engine.Application and engine.World) then
		ui_note("box off: the game's Gui is unavailable")
		ui.dead = true
		return
	end
	if not ui.visible and state.frames % 10 ~= 0 then return end
	local card = find_card()
	if not card then ui_hide() return end
	local gw, gh = engine.Gui.resolution()
	-- same guard as SAPP and DiverKit: the button sits near the top while the screen slides in
	if gh - (card.y + card.h) < 146 * (card.w / 420) then ui_hide() return end
	if not ui.handles or state.frames % 60 == 0 then
		local handles, why = read_handles()
		if not handles then ui_note("box: waiting, " .. tostring(why)) ui_hide() return end
		ui.handles = handles
	end
	if not ui.visible or state.frames % 120 == 0 then load_cfg() end
	if not ui.visible then
		ui.visible = true
		ui.choice = read_choice()
		ui_note(string.format("box: card at %.1f,%.1f size %.1fx%.1f, screen %dx%d, slot %d, melee now %s",
			card.x, card.y, card.w, card.h, gw, gh, card.slot, tostring(ui.choice)))
	end
	if state.frames % 60 == 0 then ui.choice = read_choice() or ui.choice end
	local function relayout() return layout(card, gw, gh, ui.open, nil) end
	local lay = layout(card, gw, gh, ui.open, ui.drag)
	local sample = pointer_sample(gw, gh)
	local hover = sample and hit(lay.regions, sample.x, sample.y) or nil
	local clicked = pointer_step(sample, hover or (ui.open and sample and "outside") or nil)
	-- a press on the box or on the list's own background that then moves is a drag, not a click
	if sample and cfg.drag ~= 0 then
		if sample.down and ui.was_down == false and (hover == "box" or hover == "grid") then
			ui.press = { key = hover, x = sample.x, y = sample.y }
		elseif sample.down and ui.press then
			local dx, dy = sample.x - ui.press.x, sample.y - ui.press.y
			if ui.drag or math.abs(dx) + math.abs(dy) > 6 * lay.s then ui.drag = { key = ui.press.key, dx = dx, dy = dy } end
		elseif not sample.down and ui.press then
			if ui.drag then
				drag_drop(card, lay)
				clicked = nil
				ui.drag = nil
				lay = relayout()
				hover = hit(lay.regions, sample.x, sample.y)
			end
			ui.press = nil
		end
		ui.was_down = sample.down
	else
		ui.press, ui.drag, ui.was_down = nil, nil, nil
	end
	local function pick(id)
		if write_choice(id) then ui.choice = id end
		ui.open, ui.sel = nil, nil
	end
	if clicked then
		if clicked == "box" then ui.open = not ui.open
		elseif clicked == "outside" then ui.open = false
		elseif clicked:sub(1, 2) == "m:" then pick(clicked:sub(3)) end
		lay = relayout()
		hover = sample and hit(lay.regions, sample.x, sample.y) or nil
	end
	if ui.open then
		-- one highlight, moved by the mouse or by W/S; Space picks it
		local sel = ui.sel
		if not sel then
			sel = 1
			for i, m in ipairs(MELEES) do if m.id == ui.choice then sel = i end end
		end
		if sample and (sample.x ~= ui.mx or sample.y ~= ui.my) and hover and hover:find("^m:") then
			for i, m in ipairs(MELEES) do if "m:" .. m.id == hover then sel = i end end
		end
		local kw, ks, kpick = nav_step(VK_W, true), nav_step(VK_S, true), nav_step(VK_SPACE, false)
		if kw then sel = sel - 1 if sel < 1 then sel = #MELEES end end
		if ks then sel = sel + 1 if sel > #MELEES then sel = 1 end end
		ui.sel = sel
		if kpick then
			pick(MELEES[sel].id)
			lay = relayout()
			hover = sample and hit(lay.regions, sample.x, sample.y) or nil
		else
			hover = "m:" .. MELEES[sel].id
		end
	end
	if not ui.open then
		ui.sel = nil
		nav_block()
	end
	-- while the list is open, and a few frames after it closes so the key that closed it is not heard either
	if ui.open then ui.block_until = state.frames + 10 end
	if cfg.block_keys ~= 0 and ui.block_until and state.frames <= ui.block_until then block_keys() end
	if sample then ui.mx, ui.my = sample.x, sample.y end
	-- held from the moment the mouse is on the box, so DiverKit sees neither the press nor the release
	local busy = ui.open or hover ~= nil or ui.drag ~= nil or (sample ~= nil and sample.down and ui.armed ~= nil)
	if cfg.hold_diverkit ~= 0 and busy then hold_diverkit(true)
	elseif not (sample and sample.down) then hold_diverkit(false) end
	-- the stats panel: after the cursor (or the highlight) has rested on one weapon for TIP.delay seconds
	local tip_key = hover and not ui.drag and not ui.press and (hover == "box" or hover:find("^m:")) and hover or nil
	if tip_key ~= ui.tip_key then ui.tip_key, ui.tip_at, ui.tip_shown = tip_key, tip_key and TIP.wall() or nil, false end
	if tip_key and not ui.tip_shown and TIP.wall() - ui.tip_at >= TIP.delay then ui.tip_shown = true end
	local tip = tip_key and ui.tip_shown and tip_key or nil
	draw(card, lay, ui.handles, hover, gw, gh, ui.choice, tip)
end

-- ------------------------------------------------------------------ driver
local previous_update = rawget(_G, "update")
if type(previous_update) ~= "function" then
	log("stopped: game update callback unavailable")
	return true
end

local phase, wait = "start", 240
local function tick()
	state.frames = state.frames + 1
	if phase == "dead" then return end
	if wait > 0 then wait = wait - 1 return end
	if phase == "start" then
		prepare_scan(); phase = "scan"
	elseif phase == "scan" then
		if scan_step() then
			resolve_globals(); resolve_input()
			phase = "watch"
			log("DISCOVERY OK - on the player card: click the MELEE box and pick a weapon")
		end
	elseif phase == "watch" then
		if not ui.dead then
			local ok, err = pcall(ui_step)
			if not ok then
				ui.dead = true
				log("box stopped on error: " .. tostring(err))
				pcall(ui_clear)
			end
		end
	end
end

-- after the game's own update (and the mods before this one) has run
local function after(ok, ...)
	local fine, err = pcall(tick)
	if not fine then
		log("stopped on error: " .. tostring(err))
		phase = "dead"
	end
	if not ok then error((...), 0) end
	return ...
end
rawset(_G, "update", function (...) return after(pcall(previous_update, ...)) end)

local previous_shutdown = rawget(_G, "shutdown")
if type(previous_shutdown) == "function" then
	rawset(_G, "shutdown", function (...)
		pcall(ui_clear)
		return previous_shutdown(...)
	end)
end

log("loaded (" .. REVISION .. ")")
return true
