# Health client interoperability contract

This document accompanies independently authored src/menu_client_abi.h. The developer had previously inspected the external interface and provider sources. This is not a claim of legal clean-room isolation. The old header and its explanatory prose/discovery helper are not shipped; the new discovery implementation was authored from the factual requirements below. The existing Health-owned menu adapter is retargeted to these declarations.

Reference provider: user-supplied Vanilla Plus Megapack1.1.2, external DLL SHA256c8dac6d042148ea8dc87d0f057e7b0470f72414731896c3bd6e989d0a54ba6fd. Published process-local version1 interface, ModOptionsMenu functions register_option/get/set/on_change, numeric slider values, booleans, strings, tables and retained callbacks are the compatibility requirements. No new provider API is introduced.

Windows x64 layout facts (default eight-byte pointer alignment):

| Wire record | Size | Field offsets |
|---|---:|---|
| Value | 16 | tag0, string length4, eight-byte payload8 |
| Pair | 32 | key0, value16 |
| Table | 16 | count0, flags4, pairs8 |
| Arena | 16 | allocation callback0, context8 |
| Callback | 48 | size0, refcount4, invoke8, retain16, release24, owner32, context40 |
| Provider | 40 | version0, size4, processID8, extension bits12, snapshot16, generation24, build text32 |

Value tags are nil0/bool1/number2/string3/table4/function5; array table flag1. An invoked callback receives (callback pointer, argument count, argument values, result arena, result slots, slot capacity, flags), returns a result count or -1, and uses zero flags for this client's operations. This client provides eight result slots. Snapshot takes (table name, arena, output value, output instanceID) and returns an int; generation takes no arguments and returns uint64.

Discovery reads HD2_UMM_API_v1 as the canonical decimal current-PID, colon, hexadecimal provider address. It rejects signs, whitespace, trailing data, overflow, missing fields, zero/misaligned/unreadable addresses, old version/short descriptor/wrong PID and missing/non-executable entrypoints on Windows. Descriptor lifetime and thread safety remain provider contract assumptions. All runtime discovery and menu operations use the mod's existing single game-thread dispatcher. The validated target is Windows x64; the portable fixture path does not establish equivalent memory-page checks on other operating systems.

The caller supplies zeroed, 16-byte-aligned result allocations and frees its arena only after consuming or retaining returned values. Provider functions are retained before old snapshot functions are released. A provider instance change invalidates registration/subscription state; a transient loss of the same instance does not append duplicate subscriptions. The mod's callback storage remains live for the process lifetime. The client does not free provider memory. Unknown or excessive callback result counts are refused. As with other native in-process ABIs, a provider violating pointer/lifetime contracts is outside the claimed safety model.

The matching HellForge0.3.0-beta SDK15 exposes the automatic fields through existing named set_live calls. Official source records Editor/LocalStats priority over code-layer edits:
https://github.com/SkyeShade/HellForge/blob/4a5373b88b8d001b58927edc27aa1b998578fe9f/native/mod-loader/fields.c
https://github.com/SkyeShade/HellForge/blob/4a5373b88b8d001b58927edc27aa1b998578fe9f/native/mod-loader/fields_live.c

Automatic mapping: Health offsets0/4 (160/+3), 1304(torso155),1856/2408(arms91),2960/3512(legs117), Inventory52(refill1), StatusEffectSettings row27 offset40(8s) and row26 offset40(20s). Head direct/DoT offsets716/720 match current torso offsets1268/1272 automatically. Inventory56(stim maximum) is not named in the installed Editor catalog; its existing guarded mod control remains. No Editor/core edits are made.
