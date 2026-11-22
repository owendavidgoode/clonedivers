#!/usr/bin/env python3
"""Build Melee Picker into non-colliding data/9ba626afa44a3aa3.patch_920 for unified HellForge packaging."""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import pack

PATCH_SUFFIX = ".patch_920"


def lua_str(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def main():
    with open(os.path.join(HERE, "data", "sigs.json"), encoding="utf-8") as fh:
        sigs = json.load(fh)
    sig_lua = "{\n" + "".join(
        "\t{ name = %s, symbol = %s, anchor_offset = %d, extract_offset = %d, extract_end = %d, optional = %s,\n"
        "\t  hex = %s,\n\t  mask = %s,\n\t  anchor = %s },\n" % (
            lua_str(s["name"]), lua_str(s["symbol"]), s["anchor_offset"], s["extract_offset"], s["extract_end"],
            "true" if s.get("optional") else "false", lua_str(s["hex"]), lua_str(s["mask"]), lua_str(s["anchor"]))
        for s in sigs) + "}"
    with open(os.path.join(HERE, "src", "melee_picker.lua"), encoding="utf-8") as fh:
        src = fh.read()
    if src.count("--@SIGS@") != 1:
        sys.exit("placeholder --@SIGS@ must appear exactly once")
    src = src.replace("--@SIGS@", sig_lua)

    build_dir = os.path.join(ROOT, "build", "picker")
    os.makedirs(build_dir, exist_ok=True)
    built_lua_path = os.path.join(build_dir, "melee_picker.built.lua")
    with open(built_lua_path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(src)

    name, blob = pack.build(src.encode("utf-8"))
    data_dir = os.path.join(ROOT, "data")
    os.makedirs(data_dir, exist_ok=True)
    base = os.path.join(data_dir, pack.ARCHIVE + PATCH_SUFFIX)
    with open(base, "wb") as fh:
        fh.write(blob)
    for ext in (".gpu_resources", ".stream"):
        with open(base + ext, "wb") as fh:
            pass
    print("built %s: %d bytes of Lua -> %s (%d archive bytes)" % (
        name, len(src.encode("utf-8")), base, len(blob)))


if __name__ == "__main__":
    main()
