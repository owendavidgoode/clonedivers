# First-fault telemetry v3 — wire contract

Identity: mod `clonedivers.lego-compact-leaf-writer`, version `0.0.5`, DLL `lego_compact_leaf_writer.dll`, ZIP `lego-compact-leaf-writer-0.0.5.zip`. Prefix and markers remain `LEGO_COMPACT_LEAF_WRITER` and `compact-leaf-writer.arm/.stop`. Successful raw/math/leaf protocol is unchanged.

Independent filename `leaf-fault-first.bin`, exactly 81920 bytes, little endian. All unused/reserved bytes zero. Never a successful sample or quota consumer. Fatal always requires cleanup regardless of evidence availability.

Header offsets (types u32 unless stated):

|Offset|Meaning|
|---|---|
|0|8 bytes `LGFAULT3`|
|8,12,16,20|version 3, header 256, total 81920, raw bytes 46144|
|24|signed effective capture status|
|28,32,36,40|target, ordinal, thread ID, PID|
|44|winning fatal reason|
|48,56|u64 creation FILETIME, continuous sequence (not diagnostic sequence)|
|64,72,80,88,96|u64 resource, Mesh, Pool, ORIGINAL R8, caller|
|104,108,112,116,120,124|return value, entry MXCSR, return MXCSR, entry LastError, return LastError, abandoned|
|128,136,144|u64 zeros: overlap unmeasured|
|152,156|payload used, delegate MXCSR|
|160|classification: 1 sampler, 2 initial plan, 3 recheck, 4 other|
|164,168|signed sampler status BEFORE plan, sampler used|
|172,176,180,184|plan phase, stage, error, read outcome|
|188|first differing byte index; FFFFFFFF if none|
|192|u64 last attempted read address|
|200,204,208|last read size, valid plan blocks, valid plan bytes|
|212,216,220|comparison length, probe offset, probe byte count <=64|
|224,228,232,236|u32 sampler appendix version1/offset73728/bytes8192/presence flags|
|240..255|zero|

Sections: raw payload at 256 length 46144; complete `sm_result` at 46400 length 12416; `gw_receipt` at 58816 length 48; serialized plan at 58864 length 8968; 8 zero bytes; expected probe at 67840 length 64; observed probe at 67904 length 64; zero padding67968..73727; sampler appendix73728..81919.

Serialized plan: six u64 graph/current-array/previous-array/bind/map/pool-base at relative 0..47; six u32 count/mesh-node/groups/output-count/valid-blocks/valid-bytes at 48..71; eight group entries at 72, stride24 (u64 palette, u32 count/current-offset/previous-offset/reserved-zero); 32 read entries at264, stride16 (u64 address,u32 offset,u32 bytes); evidence8192 at776. Only declared valid blocks/bytes are evidence. Failed read output is zeroed and never a valid block. A partial plan is not permission to use any pointer.

First winning fatal CAS alone claims the independent resident channel. Initialization state hides identity until reason, sequence and record ownership are published together. Only the exact winning continuous sequence may copy completed raw/math/leaf/plan data. No reclaim or overwrite. No-slot failure is explicitly unavailable; a delegate that never returns remains pending. No waiting in tick/cleanup.

State: 0 empty,1 initializing,2 reserved,3 copying,4 ready,5 written,6 write_failed. Only states >=2 expose identity. Tick attempts file creation exactly once after ready, validates owned directory/file, writes bounded bytes and closes successfully BEFORE acknowledgement:

`LEGO_COMPACT_LEAF_WRITER fault_evidence file=leaf-fault-first.bin bytes=81920 reason=N sequence=S classification=C capture_status=SIGNED plan_stage=N plan_error=N complete=1`

Pending once: `fault_pending state=N reason=N sequence=S record_owned=0|1 identity_ready=0|1`; initializing identity values are zero. Write failure: `fault_write_failed reason=N sequence=S`. Partial file is preserved, never acknowledged. Tick checks this channel even after success diagnostics close. Fatal-before-ready/file publication is an explicit missing/pending artifact, never a reason to delay cleanup.

Plan trace phase 0 none/1 capture/2 recheck; error 0 none/1 predicate bounds or unsupported value/2 failed read/3 mismatch; read outcome 0 none/1 success/2 failure. Stage enumeration is fixed below and implemented in `leaf_plan.c`. Probes describe already-observed bytes only, with zero padding; no new native reads are introduced.

This fault channel is unchanged telemetry. The v5 pose-policy selection is documented separately; it does not weaken identity, palette, matrix admission, route, timing, ownership or packing gates. A captured mismatch is evidence of the checked condition, not proof of its engine cause or cross-thread exclusion.

Capture stages: 1 asset/arguments; 2 selected counts/mesh node; 3 graph header; 4 current mesh row; 5 previous mesh row; 6 Mesh node/cache; 7 skin pointer; 8 bind pointer; 9 group count; 10 subrecord pointer; 11 output count; 12 group/output limits; 13 pool base; 14 skin header; 15 bone map; 16 inverse binds; 17 palette directory; 18 subrecords. For group i=0..7: stage20+4i directory selector/count/offset; 21+4i palette; 22+4i current output offset; 23+4i previous output offset. Recheck stage99 validates saved evidence bounds; 100+i rechecks saved block i=0..31. Error1 may retain a successfully read block whose scalar is invalid; those observed invalid counts/addresses are diagnostic data, not asserted safe bounds. Readers must bound serialization arrays by fixed capacities rather than trust invalid observed count/group values. Error3 includes a first-mismatch probe, error2 has no valid bytes from the failed read. Full expected map/bind/palette values remain in pinned source asset tables.

# Sampler first-fault appendix — normative v3 contract

Same writer ID/DLL/prefix/markers, version0.0.5 ZIP. Fixed fault file `leaf-fault-first.bin`, **81920 bytes**, magic `LGFAULT3`, header version3. Existing v1 fault sections/fields through67967 unchanged;67968..73727 remain zero. Header224/228/232/236 become u32 appendix-version1/offset73728/bytes8192/presence-flags;240..255zero. All other old reserved fields stay zero. Fault acknowledgement fields unchanged except bytes81920. Success records/math/leaf protocol unchanged.

The8192-byte appendix is explicitly serialized little endian, never a C structure dump. Unused bytes zero. All values come from existing successful or partially completed metadata reads; no new engine reads. A failed closing read leaves an initialized partial snapshot whose field validity comes from the nested read masks. Missing pending/valid bits never imply successful data. Copies occur before InvocationClearFailures and before plan capture clears shared diagnostics, under the same metadata lock; exact first-failure ownership remains unchanged.

No-capture/absent appendix still has `LGSMPL01`,version1,size8192 at0/8/12; presence0 and all bytes16..8191zero. Its sampler status is not bound to the primary sampler status. When snapshot-present(bit0), appendix status equals sampler status before plan. Successful closing read sets underlying reason `accepted`; it is not a refusal reason. Captured partial metadata is zero-initialized by RenderRead before any reads and its nested validity masks identify completed operations.

Appendix-relative layout:

|Offset|Contents|
|---|---|
|0|8-byte magic `LGSMPL01`|
|8,12|u32 version1,total8192|
|16|u32 presence mask below|
|20|i32 sampler status before plan|
|24,28,32,36|u32 closing attempted/success/before-valid/after-state(0none,1initialized partial,2complete)|
|40,44|u32 renderStage,budgetFailure|
|48..63|zero|
|64|underlying closing RenderRead reason,96-byte ASCII NUL-padded|
|160|final generic renderFailure reason,96-byte ASCII NUL-padded|
|256|closing-before RenderMeta1024|
|1280|closing-after RenderMeta1024|
|2304|Schema128|
|2432|Route384|
|2816|RenderFailure1280|
|4096|Consistency640|
|4736|Resolve128|
|4864|consistency kind64|
|4928|consistency reason96|
|5024|route reason96|
|5120|render-refusal reason96|
|5216..8191|zero|

Presence bits:0serialized snapshot;1closing attempted;2closing success;3before valid;4schema pending;5route pending;6render pending;7consistency pending;8resolve attempted nonzero;9budgetFailure;10after initialized. The producer sets/clears each attempt before any early return. Underlying reason is retained immediately after the closing RenderRead and before generic RF replaces it.

RenderMeta1024: RHeader96 at0,Resolved32at96,meshEntriesu64at128,graphHandle/meshes/registeredMeshes/unregisteredMeshesu32at136/140/144/148,8 MeshMeta96at152;920..1023zero. RHeader96 has manager/queue/world/handlesu64at0/8/16/24,capacity/mask/generation/liveu32at32/36/40/44,Vec16at48/64/80(lengthu32,capacityu32,datau64). Resolved32 has handle/index/type/nodesu32at0/4/8/12,object/vtableu64at16/24. MeshMeta96 has descriptor/association/sourceGraph/skinu64at0/8/16/24,handle/node/flags/registeredu32at32/36/40/44,Resolved32at48,Ref node/handleu32at80/84 andcacheu64at88.

Schema128:manageru64at0,raw80at8,failed/pending/loggedu32at88/92/96;100..127zero.

Route384:RHeader96at0,queue136at96,world32at232,backu64at264,stage/attempted/valid/checked/failed/pending/loggedu32at272/276/280/284/288/292/296;300..383zero.

RenderFailure1280:unit/association/expectedSource/resourceu64at0/8/16/24;unitNodes/ordinal/stage/attempted/valid/checked/failed/resolveKind/headerValid/graphValid/conflictOrdinal/pending/loggedu32at32/36/40/44/48/52/56/60/64/68/72/76/80;84..95zero;RenderMeta1024at96;Resolve128at1120;1248..1279zero.

Resolve128:attempted/valid/checked/failed/slot/requestedType/headerTypeu32at0/4/8/12/16/20/24;28..31zero;Resolved32at32;64..127zero.

Consistency640:old/new RHeader96at0/96;old/new Identity48at192/240 (unit/resourceAddress/resourceu64at0/8/16,index/generationu32at24/28,32..47zero);mask/ignored/oldValue/newValueu64at288/296/304/312;target/prior/offset/width/differences/stage/pending/logged/headers/parentsu32at320/324/328/332/336/340/344/348/352/356;360..639zero.

This retains complete original closing operands and all already-available nested diagnostics. In v0.0.5, only the qualified actual-call/no-group closing comparison may ignore the eight-byte header.queue field after both complete fresh RenderReads. All other912 bytes and all internal RenderRead checks remain exact. See QUEUE-POLICY.md; StableRender, native substitution and first-fatal semantics are unchanged.
