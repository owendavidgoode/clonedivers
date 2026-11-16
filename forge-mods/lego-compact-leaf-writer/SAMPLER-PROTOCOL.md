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
