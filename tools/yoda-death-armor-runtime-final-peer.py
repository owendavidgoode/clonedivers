#!/usr/bin/env python3
"""Run independent LEGO policy fixtures against the prepared private reader."""
from __future__ import annotations

import importlib.util
from pathlib import Path


def main() -> None:
    peer = Path(__file__).with_name("yoda-death-armor-runtime-peer.py")
    spec = importlib.util.spec_from_file_location("yoda_independent_final_peer", peer)
    if spec is None or spec.loader is None:
        raise ImportError("Independent frozen fixture module")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.IDENTITY = module.IDENTITY.replace("name=='hd2runtime/runtime/event_world'", "name=='codex_lego_yoda/world'")
    module.IDENTITY = module.IDENTITY.replace("local validneg={", "local validneg={\n {'cached-category-changed',function(s,p,players,e,f)f.u32(f.kit1+f.kitType,1)end,'APPLIED_ARMOR_CHANGED'},")
    module.ROUTING = module.ROUTING.replace("local negatives={", "local negatives={\n {'non-table-cause',function(s,r,e)e.cause=123 end,'NOT_NATIVE_DEATH'},\n {'non-table-position',function(s,r,e)e.position=123 end,'NO_DEATH_POSITION'},")
    module.__file__ = __file__
    module.main()


if __name__ == "__main__":
    main()
