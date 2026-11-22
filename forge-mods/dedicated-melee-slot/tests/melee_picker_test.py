#!/usr/bin/env python3
"""Verify melee_picker.built.lua via D:\\HellForge\\game\\bin\\lua51.dll, patch_920 binary layout, and game.dll sigs."""
from __future__ import annotations

import ctypes
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
BUILT_LUA = ROOT / "build" / "picker" / "melee_picker.built.lua"
PATCH_FILE = ROOT / "data" / "9ba626afa44a3aa3.patch_920"
SIGS_FILE = ROOT / "picker" / "data" / "sigs.json"
LUA51_DLL = Path(r"D:\HellForge\game\bin\lua51.dll")
GAME_CAPTURE = Path(
    r"D:\ClaudeWork\clonedivers-lab\camera-evidence-astra\camera-capture-mission\capture-game.dll-00001000.bin"
)

sys.path.insert(0, str(ROOT / "picker"))
import pack  # noqa: E402


def check(cond: bool, msg: str, counter: list[int]) -> None:
    counter[0] += 1
    if not cond:
        raise AssertionError(f"FAIL: {msg}")


def verify_lua51_syntax_and_runtime(lua_bytes: bytes, counter: list[int]) -> None:
    check(LUA51_DLL.is_file(), f"lua51.dll exists at {LUA51_DLL}", counter)
    lua = ctypes.CDLL(str(LUA51_DLL))

    lua.luaL_newstate.argtypes = []
    lua.luaL_newstate.restype = ctypes.c_void_p
    lua.luaL_openlibs.argtypes = [ctypes.c_void_p]
    lua.luaL_openlibs.restype = None
    lua.luaL_loadbuffer.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t, ctypes.c_char_p]
    lua.luaL_loadbuffer.restype = ctypes.c_int
    lua.lua_tolstring.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_size_t)]
    lua.lua_tolstring.restype = ctypes.c_char_p
    lua.lua_pcall.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_int]
    lua.lua_pcall.restype = ctypes.c_int
    lua.lua_close.argtypes = [ctypes.c_void_p]
    lua.lua_close.restype = None

    state = lua.luaL_newstate()
    check(bool(state), "luaL_newstate succeeded", counter)
    try:
        lua.luaL_openlibs(state)
        rc = lua.luaL_loadbuffer(state, lua_bytes, len(lua_bytes), b"@melee_picker.built.lua")
        if rc != 0:
            sz = ctypes.c_size_t(0)
            err = lua.lua_tolstring(state, -1, ctypes.byref(sz))
            raise AssertionError(f"luaL_loadbuffer failed ({rc}): {err.decode('utf-8', 'replace') if err else ''}")
        check(rc == 0, "luaL_loadbuffer == 0 on melee_picker.built.lua", counter)
    finally:
        lua.lua_close(state)

    # Execute melee_picker.built.lua inside lua51.dll across co-op slots 0..3 & nil/disconnect edge cases!
    tmp_sandbox = ROOT / "build" / "tmp" / "lua_env"
    (tmp_sandbox / "CowboyBingus" / "Helldivers2" / "Logs").mkdir(parents=True, exist_ok=True)
    (tmp_sandbox / "Documents" / "HellForge").mkdir(parents=True, exist_ok=True)
    sandbox_str = str(tmp_sandbox).replace("\\", "/")

    harness_lua = (f'local SANDBOX = "{sandbox_str}"\n').encode("utf-8") + b"""
os.getenv = function(k)
  if k == "LOCALAPPDATA" or k == "USERPROFILE" then return SANDBOX end
  return nil
end

local mem = {}
local function w32(a, v)
  mem[a] = v % 256
  mem[a+1] = math.floor(v / 256) % 256
  mem[a+2] = math.floor(v / 65536) % 256
  mem[a+3] = math.floor(v / 16777216) % 256
end
local function w64(a, lo, hi)
  w32(a, lo); w32(a+4, hi or 0)
end
local f32_map = {}

package.preload["bit"] = function()
  return {
    band = function(a, b)
      local r, p = 0, 1
      for _ = 1, 32 do
        if (a % 2 == 1) and (b % 2 == 1) then r = r + p end
        a = math.floor(a / 2); b = math.floor(b / 2); p = p * 2
      end
      return r
    end
  }
end

local valid_max = 0x900000
package.preload["ffi"] = function()
  local C_tbl = {
    GetFileAttributesExA = function() return 0 end
  }
  return {
    cdef = function() end,
    sizeof = function() return 48 end,
    typeof = function() return "fn" end,
    new = function(t)
      if t == "JarvisMPRegion[1]" then
        return { [0] = { base = 0x10000, size = valid_max, state = 0x1000, protection = 0x04 } }
      end
      return { [0] = 0, [1] = 0, [2] = 1920, [3] = 1080, written = { lo = 0, hi = 0 } }
    end,
    cast = function(ct, addr)
      if ct == "uintptr_t" then return addr end
      if ct == "const uint8_t *" then
        return setmetatable({}, { __index = function(_, k) return mem[addr + k] or 0 end })
      end
      if ct == "const uint32_t *" then
        return setmetatable({}, { __index = function(_, k)
          local a = addr + k * 4
          return (mem[a] or 0) + (mem[a+1] or 0)*256 + (mem[a+2] or 0)*65536 + (mem[a+3] or 0)*16777216
        end })
      end
      if ct == "const float *" then
        return setmetatable({}, { __index = function(_, k) return f32_map[addr + k * 4] or 0.0 end })
      end
      return addr
    end,
    load = function(name)
      if name == "kernel32" then
        return {
          VirtualQuery = function(addr, reg, sz)
            if type(addr) == "number" and addr >= 0x10000 and addr < valid_max then
              reg[0].base = 0x10000; reg[0].size = valid_max; reg[0].state = 0x1000; reg[0].protection = 0x04
              return sz
            end
            return 0
          end,
          GetModuleHandleA = function() return 0x100000 end,
          GetCurrentProcessId = function() return 4242 end,
        }
      end
      return {
        GetForegroundWindow = function() return 1 end,
        GetWindowThreadProcessId = function(_, box) box[0] = 4242 return 1 end,
        GetAsyncKeyState = function() return 0 end,
        GetCursorPos = function(p) p[0] = 50; p[1] = 50 return 1 end,
        ScreenToClient = function() return 1 end,
        GetClientRect = function(w, c) c[0] = 0; c[1] = 0; c[2] = 1920; c[3] = 1080 return 1 end,
      }
    end,
    C = C_tbl,
  }
end

local gui_created = 0
local gui_destroyed = 0
local worlds_list = { "main_w", "ui_w" }
_G.stingray = {
  Application = {
    main_world = function() return "main_w" end,
    worlds = function() return worlds_list end,
  },
  World = {
    create_screen_gui = function() gui_created = gui_created + 1 return "gui_" .. gui_created end,
    destroy_gui = function() gui_destroyed = gui_destroyed + 1 end,
  },
  Gui = {
    resolution = function() return 1920, 1080 end,
    material = function() return "ink_1" end,
    rect = function() end,
    text_extents = function() return {x=0}, {x=40} end,
    text = function() end,
  },
  IdString64 = { from_hex = function(h) return h end },
  Material = { set_scalar = function() end, set_vector2 = function() end, set_vector4 = function() end, set_texture = function() end },
  Vector2 = setmetatable({ x = function(v) return v.x end }, { __call = function(_, x, y) return {x=x, y=y} end }),
  Vector3 = function(x, y, z) return {x=x, y=y, z=z} end,
  Color = function(a, r, g, b) return {a=a, r=r, g=g, b=b} end,
}
_G.update = function() end
_G.shutdown = function() end

-- Stage minimal fake PE + ui_manager signature at 0x101000 so prepare_scan -> scan_step -> watch runs!
local base = 0x100000
w32(base + 0x3C, 0x80)
w32(base + 0x80, 0x00004550) -- PE\0\0
w32(base + 0x80 + 0x50, 0x100000) -- image_size = 1 MB

-- Place ui_manager signature at 0x101000 pointing to slot 0x180000 (rel = 0x180000 - (0x101000 + 12) = 0x7EFF4)
local sig_bytes = {
  0x38,0x51,0x29,0x74,0x47,0x4c,0x8b,0x05, 0xf4,0xef,0x07,0x00,
  0x33,0xc0,0x88,0x51,0x29,0x41,0x39,0x80,0x70,0x3a,0x00,0x00,
  0x76,0x32,0x66,0x0f,0x1f,0x44,0x00,0x00,0x8b,0xc8,0x48,0x03,
  0xc9,0x41,0x81,0xbc,0xc8,0x80,0x3a,0x00,0x00,0x23,0x01,0x00,0x00
}
for idx, bv in ipairs(sig_bytes) do mem[0x101000 + idx - 1] = bv end
-- Place optional font/atlas/material sigs too so read_handles succeeds!
local mat_sig = {
  0x48,0x8b,0xcb,0xe8,0,0,0,0, 0xba,0xa1,0xe7,0x43,0xd1,0x48,0x8b,0xcb,0xe8,0,0,0,0,
  0x48,0x8b,0x15, 0x00,0x00,0x00,0x00, 0x45,0x33,0xc0,0x48,0x8b,0xcb,0x48,0x8b,0x52,0x28,
  0xe8,0,0,0,0, 0xf3,0x0f,0x10,0x05,0,0,0,0
}
for idx, bv in ipairs(mat_sig) do mem[0x102000 + idx - 1] = bv end
w32(0x102000 + 24, 0x180010 - (0x102000 + 28))

local font_sig = {
  0xc7,0x44,0x24,0x78,0x00,0x00,0x80,0x3f,0x41,0xff,0x92,0x38,0x01,0x00,0x00,
  0x48,0x8b,0x05,0,0,0,0, 0x48,0x8d,0x15,0,0,0,0, 0x48,0x8b,0x1d, 0,0,0,0,
  0x41,0x0f,0x28,0xdc,0x48,0x8b,0x4e,0x08,0x4c,0x8b,0xc3
}
for idx, bv in ipairs(font_sig) do mem[0x103000 + idx - 1] = bv end
w32(0x103000 + 32, 0x180020 - (0x103000 + 36))

local atlas_sig = {
  0xf3,0x44,0x0f,0x59,0x25,0,0,0,0, 0x48,0x8b,0x3d,0,0,0,0,
  0xf3,0x45,0x0f,0x10,0x8e,0x24,0x01,0x00,0x00, 0x48,0x8b,0x35, 0,0,0,0,
  0x41,0x0f,0x28,0xcc,0x45,0x0f,0x28,0xdc,0xf3,0x0f,0x59,0x0d,0,0,0,0
}
for idx, bv in ipairs(atlas_sig) do mem[0x104000 + idx - 1] = bv end
w32(0x104000 + 28, 0x180030 - (0x104000 + 32))

-- Font/atlas/material IDs
w64(0x180010, 0x180100, 0) -- holder ptr
w64(0x180100 + 24, 0x11112222, 0x33334444)
w64(0x180020, 0x55556666, 0x77778888)
w64(0x180030, 0x9999aaaa, 0xbbbbcccc)

-- UI manager -> owner at 0x300000
local mgr = 0x220000
local owner = 0x300000
w64(0x180000, mgr, 0)
w32(mgr + 0x62a0, 1)
w64(mgr + 0x62a0 + 8, owner, 0)
w32(mgr + 0x62a0 + 16, 0xe5)
w32(owner + 8, 1) -- stratagem loadout view
mem[owner + 0x273990] = 0
mem[owner + 0x2808] = 0

local function setup_card_for_slot(slot, card_idx)
  for i = 0, 3 do
    local cand = owner + 0x53a78 + i * 0x1ee18
    w64(cand + 0x1edf0, 0, 0)
    w32(cand + 0x1edfc, 99)
  end
  w32(owner + 0x27d0, slot)
  local payload = owner + slot * 0x9f0 + 0x10
  local cand = owner + 0x53a78 + card_idx * 0x1ee18
  w64(cand + 0x1edf0, payload, 0)
  w32(cand + 0x1edfc, slot)
  local widget = cand + 0x1b820
  w32(widget, 0x10)
  f32_map[widget + 36] = 420.0
  f32_map[widget + 40] = 40.0
  f32_map[widget + 84] = 1.0
  f32_map[widget + 100] = 1.0
  f32_map[widget + 140] = 1.0
  f32_map[widget + 148] = 120.0 + slot * 450.0
  f32_map[widget + 156] = 200.0
end
""" + b"\nlocal chunk_ok = assert(loadstring([===[" + lua_bytes + b"]===]))()\nassert(chunk_ok == true)\n" + b"""
assert(type(_G.JarvisMeleePicker) == "table")

-- Advance past 240-frame startup wait + signature scan into watch phase!
setup_card_for_slot(0, 0)
for _ = 1, 260 do _G.update() end
assert(_G.JarvisMeleePicker.status:find("slot 0") ~= nil, "slot 0 card not detected: " .. tostring(_G.JarvisMeleePicker.status))

-- Now test co-op slots 1, 2, and 3 (including shuffled card index after peer leave/join)!
for _, pair in ipairs({ {1, 2}, {2, 3}, {3, 1} }) do
  -- Hide card for 1 frame so ui.visible resets cleanly, then show new co-op slot!
  w32(owner + 8, 0)
  _G.update()
  w32(owner + 8, 1)
  setup_card_for_slot(pair[1], pair[2])
  for _ = 1, 12 do _G.update() end
  assert(_G.JarvisMeleePicker.status:find("slot " .. pair[1]) ~= nil,
    "co-op slot " .. pair[1] .. " failed: " .. tostring(_G.JarvisMeleePicker.status))
end

-- Test disconnect / UI world abrupt removal without error!
worlds_list = { "main_w" }
w32(owner + 8, 0)
_G.update()
_G.shutdown()
assert(gui_created >= 4, "expected GUI draw across all 4 slots")
"""

    state2 = lua.luaL_newstate()
    try:
        lua.luaL_openlibs(state2)
        rc = lua.luaL_loadbuffer(state2, harness_lua, len(harness_lua), b"@lua_harness")
        check(rc == 0, "Lua runtime harness compiled", counter)
        prc = lua.lua_pcall(state2, 0, 0, 0)
        if prc != 0:
            sz = ctypes.c_size_t(0)
            err = lua.lua_tolstring(state2, -1, ctypes.byref(sz))
            raise AssertionError(f"Lua runtime harness pcall failed: {err.decode('utf-8', 'replace') if err else ''}")
        check(prc == 0, "melee_picker.built.lua executed slots 0..3 & disconnect teardown cleanly under lua51.dll", counter)
    finally:
        lua.lua_close(state2)


def verify_patch_920_archive(lua_bytes: bytes, counter: list[int]) -> None:
    check(PATCH_FILE.is_file(), "data/9ba626afa44a3aa3.patch_920 exists", counter)
    check((ROOT / "data" / "9ba626afa44a3aa3.patch_920.stream").stat().st_size == 0, ".stream empty", counter)
    check((ROOT / "data" / "9ba626afa44a3aa3.patch_920.gpu_resources").stat().st_size == 0, ".gpu_resources empty", counter)

    # Non-collision check against reserved patch numbers:
    # 0 (Bingus Shared Loader), 0..37 (Clonedivers), 900 (Heavy SEAF), 910 (Chaos Invasions), 1000..1036 (EmpireDivers)
    patch_num = int(PATCH_FILE.suffix.split("_")[1])
    forbidden = {0, 900, 910} | set(range(0, 38)) | set(range(1000, 1037))
    check(patch_num == 920 and patch_num not in forbidden, "patch_920 avoids all reserved archive slots", counter)

    raw = PATCH_FILE.read_bytes()
    magic, type_cnt, file_cnt = struct.unpack_from("<3I", raw, 0)
    total_sz = struct.unpack_from("<Q", raw, 0x20)[0]
    check((magic, type_cnt, file_cnt) == (0xF0000011, 1, 1), "Stingray 0xF0000011 archive header", counter)
    check(total_sz == len(raw) and len(raw) % 16 == 0, "16-byte aligned archive total size", counter)

    name_hash, type_hash, data_off = struct.unpack_from("<3Q", raw, 0x68)
    payload_sz = struct.unpack_from("<I", raw, 0x68 + 56)[0]
    check(name_hash == pack.murmur64a(b"mods/jarvis/melee_picker"), "addon resource hash mods/jarvis/melee_picker", counter)
    check(type_hash == pack.LUA_TYPE, "Stingray lua resource type hash", counter)

    payload = raw[data_off:data_off + payload_sz]
    src_len, src_kind = struct.unpack_from("<2I", payload, 0)
    check((src_len, src_kind) == (len(lua_bytes), 2), "Lua source wrapper header (len, 2)", counter)
    check(payload[8:] == lua_bytes, "packed Lua source matches melee_picker.built.lua byte-for-byte", counter)


def verify_source_hardening_and_slots(lua_text: str, counter: list[int]) -> None:
    check(lua_text.startswith("-- HD2-Addon: mods/jarvis/melee_picker\n"), "Bingus HD2-Addon header line", counter)
    check('local REVISION = "Dedicated Melee Slot picker 1.5.2-clonedivers.2"' in lua_text,
          "1.5.2-clonedivers.2 revision tag in melee_picker.built.lua", counter)
    check("--@SIGS@" not in lua_text, "--@SIGS@ placeholder expanded", counter)
    check("if not (globals and globals.manager and readable(globals.manager, 8)) then return nil end" in lua_text,
          "nil-safe globals.manager guard in find_card()", counter)
    check("for i = 0, 3 do" in lua_text and "local slot = u32(owner + 0x27d0)" in lua_text,
          "multi-slot 0..3 player card resolution in find_card()", counter)
    check("pcall(engine.World.destroy_gui, ui.world, ui.gui)" in lua_text,
          "pcall-guarded destroy_gui on world teardown", counter)
    check("if not (engine and engine.Gui and engine.Application and engine.World) then" in lua_text,
          "full engine namespace guard in ui_step()", counter)


def verify_sigs_against_game_dll(counter: list[int]) -> None:
    sigs = json.loads(SIGS_FILE.read_text(encoding="utf-8"))
    check(len(sigs) == 5, "5 signatures in sigs.json", counter)
    if not GAME_CAPTURE.is_file():
        return
    gbin = GAME_CAPTURE.read_bytes()
    expected_targets = {
        "ui_manager": 0x3326E68,
        "ui_material": 0x37C5478,
        "ui_font": 0x3772268,
        "ui_atlas": 0x3772EE8,
        "input_consume_call": 0x12FDE90,
    }
    for s in sigs:
        b = bytes.fromhex(s["hex"])
        m = bytes.fromhex(s["mask"])
        anchor = bytes.fromhex(s["anchor"])
        ao = s["anchor_offset"]
        hits = []
        pos = 0
        while True:
            idx = gbin.find(anchor, pos)
            if idx < 0:
                break
            start = idx - ao
            if 0 <= start and start + len(b) <= len(gbin):
                if all((m[k] == 0 or gbin[start + k] == b[k]) for k in range(len(b))):
                    rva = 0x1000 + start
                    rel = int.from_bytes(
                        gbin[start + s["extract_offset"]:start + s["extract_offset"] + 4],
                        "little",
                        signed=True,
                    )
                    hits.append((rva, rva + s["extract_end"] + rel))
            pos = idx + 1
        check(len(hits) == 1, f"signature {s['name']} unique match in game.dll build 25480438", counter)
        check(hits[0][1] == expected_targets[s["name"]], f"signature {s['name']} resolved expected RVA", counter)


def main() -> int:
    counter = [0]
    check(BUILT_LUA.is_file(), f"built Lua exists at {BUILT_LUA}", counter)
    lua_bytes = BUILT_LUA.read_bytes()
    lua_text = lua_bytes.decode("utf-8")

    verify_lua51_syntax_and_runtime(lua_bytes, counter)
    verify_patch_920_archive(lua_bytes, counter)
    verify_source_hardening_and_slots(lua_text, counter)
    verify_sigs_against_game_dll(counter)

    print(f"melee_picker verification suite: {counter[0]} checks passed, 0 failed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
