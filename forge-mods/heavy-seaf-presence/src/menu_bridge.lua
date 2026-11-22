-- HD2-Addon: mods/kohl/heavy_seaf_presence
-- MODS tab rows for Heavy SEAF Presence Everywhere, for a menu that serves
-- _G.ModOptionsMenu to Lua mods (the native Vanilla Plus Megapack's Mod Options
-- Menu does; the Universal Mod Menu does not, and there the DLL registers its own
-- rows through its C API, so nothing here runs).
-- Values cross to the DLL as a file: the DLL writes the current ones at start,
-- this addon registers the rows and writes them back when the player applies.

local MOD_NAME = 'Heavy SEAF Presence Everywhere'
local MOD_ID = 'local.heavy-seaf-presence'
local VALUES = 'HeavySeafMenu.values'

local ROWS = {
    { id = 'heavy_seaf.constant_drops', key = 'constant', type = 'toggle',
      label = 'CONSTANT DROPS', default = false,
      description = 'On: automatic SEAF waves land after mission ground entry (+15s grace) and every interval_seconds while fewer than 24 SEAF are alive. Off (default): SEAF lands from native modifier patrols and when you call SEAF Reinforcement.' },
    { id = 'heavy_seaf.support_squads', key = 'support_squads', type = 'toggle',
      label = 'SEAF SQUAD STRATAGEMS IN WAVES', default = false,
      description = "Requires SEAF Squad Stratagems. On: every squad in a wave is randomly Patrol, Stealth, Riot Control or Tank Hunter when that mod is loaded. Off: waves use the game's regular SEAF squad." },
    { id = 'heavy_seaf.tank_hunters', key = 'tank_hunters', type = 'toggle',
      label = 'TANK HUNTER SQUADS GO FOR HEAVIES', default = false,
      description = "Not active yet on this build: the squad's AI record is not found, so this switch currently changes nothing. Kept off until that is fixed." },
    { id = 'heavy_seaf.interval', key = 'interval_seconds', type = 'slider',
      label = 'SECONDS BETWEEN WAVES', default = 120, min = 10, max = 900, step = 5,
      description = 'How often constant drops call a wave. A new value applies to the next wave.' },
    { id = 'heavy_seaf.squads', key = 'squads_per_wave', type = 'slider',
      label = 'SQUADS PER WAVE', default = 1, min = 1, max = 3, step = 1,
      description = 'How many squads one wave calls in. A new value applies to the next wave.' },
}

local log_handle
local function log(line)
    local loader = rawget(_G, 'CowboyBingusModLoader')
    if not log_handle and loader and type(loader.open_log) == 'function' then
        log_handle = loader.open_log('HeavySeafMenu.log')
    end
    if log_handle then
        log_handle:write(line .. '\n')
        log_handle:flush()
    end
end

local function values_path()
    local loader = rawget(_G, 'CowboyBingusModLoader')
    local directory = loader and loader.log_directory
    if type(directory) ~= 'string' then
        local getenv = os and os.getenv
        local base = getenv and getenv('LOCALAPPDATA')
        if not base or base == '' then return nil end
        directory = base .. '/CowboyBingus/Helldivers2/Logs'
    end
    return directory .. '/' .. VALUES
end

local function read_values()
    local path = values_path()
    local found = {}
    if not path then return found end
    local file = io.open(path, 'rb')
    if not file then return found end
    local text = file:read('*a') or ''
    file:close()
    for key, value in text:gmatch('([%w_]+)=(%-?%d+)') do
        found[key] = tonumber(value)
    end
    return found
end

local function write_values(values)
    local path = values_path()
    if not path then return end
    local file = io.open(path, 'wb')
    if not file then return end
    file:write('constant=', tostring(values.constant or 0), '\n')
    file:write('support_squads=', tostring(values.support_squads or 0), '\n')
    file:write('tank_hunters=', tostring(values.tank_hunters or 0), '\n')
    file:write('interval_seconds=', tostring(values.interval_seconds or 120), '\n')
    file:write('squads_per_wave=', tostring(values.squads_per_wave or 1), '\n')
    file:close()
end

local function number(value)
    if type(value) == 'boolean' then return value and 1 or 0 end
    if type(value) == 'number' then return math.floor(value + 0.5) end
    return nil
end

-- The Universal Mod Menu sets this in the process environment; the DLL then
-- registers the rows itself.
local function umm_present()
    local getenv = os and os.getenv
    local value = getenv and getenv('HD2_UMM_API_v1')
    return value ~= nil and value ~= ''
end

local UMM = umm_present()
local registered = false

local function step()
    if registered then return end
    if UMM then
        registered = true
        return
    end
    local menu = rawget(_G, 'ModOptionsMenu')
    if type(menu) ~= 'table' or type(menu.register_option) ~= 'function'
        or type(menu.get) ~= 'function' or type(menu.on_change) ~= 'function' then
        return
    end
    registered = true
    local values = read_values()
    -- The DLL writes this file at start, before any addon loads. Without it the
    -- rows still register, but nothing here may overwrite options.ini.
    local have_file = values.constant ~= nil
    for _, row in ipairs(ROWS) do
        local spec = {
            type = row.type, label = row.label, mod = MOD_NAME, mod_id = MOD_ID,
            default = row.default, description = row.description,
        }
        if row.type == 'slider' then
            spec.min, spec.max, spec.step = row.min, row.max, row.step
        end
        local ok, reason = menu.register_option(row.id, spec)
        if not ok then
            log('row ' .. row.id .. ' not added: ' .. tostring(reason))
        else
            local initial = values[row.key]
            if initial ~= nil then
                if row.type == 'toggle' then initial = initial ~= 0 end
                menu.set(row.id, initial)
            end
            menu.on_change(row.id, function(first, second)
                -- MOM calls fn(value, id); older copies pass (id, value).
                local value = first
                if first == row.id then value = second end
                local changed = read_values()
                for _, other in ipairs(ROWS) do
                    if other.id == row.id then
                        local next_value = number(value)
                        if next_value ~= nil then changed[other.key] = next_value end
                    elseif changed[other.key] == nil then
                        changed[other.key] = number(menu.get(other.id))
                    end
                end
                write_values(changed)
            end)
        end
    end
    local current = {}
    for _, row in ipairs(ROWS) do
        local value = number(menu.get(row.id))
        if value == nil then value = values[row.key] end
        if value == nil or (row.type == 'slider' and value == 0) then
            value = number(row.default)
        end
        current[row.key] = value
    end
    if have_file then
        write_values(current)
        log('rows added to the MODS page (' .. MOD_NAME .. ').')
    else
        log('rows added to the MODS page (' .. MOD_NAME .. '); ' .. VALUES ..
            ' was not there, so the menu shows defaults until the next session.')
    end
end

-- after_startup is the loader's way to register once every mod has started;
-- older loaders (v18 and before) have none, so the first updates carry it.
local loader = rawget(_G, 'CowboyBingusModLoader')
if type(loader) == 'table' and type(loader.after_startup) == 'function' then
    loader.after_startup(step)
end

local attempts = 0
local previous_update = rawget(_G, 'update')
update = function(...)
    if not registered then
        attempts = attempts + 1
        pcall(step)
        if not registered and attempts == 300 then
            log('ModOptionsMenu not found; the rows wait for it. The pack serves it as _G.ModOptionsMenu.')
        end
    end
    if type(previous_update) == 'function' then return previous_update(...) end
end
