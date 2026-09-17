# Immutable binary FIELD values

This supplements [FIELD result persistence](field-results.md). Cached values and
named evaluator data can additionally project `DxfFieldBinaryValue`. Its public
constructor copies the supplied bytes, the indexer is read-only, and `ToArray()`
returns an independent copy. Equality and hashing compare byte contents. The
`DxfFieldResult` constructor also accepts a `byte[]` and snapshots it immediately.
An empty Buffer remains distinct from a null cached value.

The [Autodesk data-type enum](https://help.autodesk.com/cloudhelp/2022/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_DataType.html)
defines Buffer as 128 and Date as 8. They are not interchangeable. The
[FIELD group-code reference](https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-DXF/files/GUID-51B921F2-16CA-4948-AC75-196198DD1796.htm)
identifies group 92 as the buffer size and repeated group 310 as its bytes.
Newly written buffers use the exact declared byte length and chunks of up to
127 bytes. Empty output emits no chunks. Existing equal buffers retain original
packet segmentation, including the admitted optional empty chunk for empty input.

```csharp
var result = new DxfFieldResult(new byte[] { 0, 127, 255 }, "3 bytes");
// Apply through the same explicit ancestor-complete batch or owned-tree API.
// The displayed text is caller-provided; bytes are never interpreted or executed.
```

Projection checks actual accumulated chunk lengths before allocating the declared
buffer. Negative/excessive lengths, missing/repeated sizes, truncated/overlong
payloads and invalid empty chunks refuse the projection without dropping the
retained source packet. Buffers are limited to 1,048,576 bytes individually;
combined projected binary data in one FIELD is limited to 4,194,304 bytes, and
submitted binary results across one transaction to 16,777,216 bytes. Those limits
are independent of the existing text, tag, node and depth limits. Byte sequences
are inert: they neither acquire hidden object references nor become native code.

Success, explicit failed outcomes, retained-cache failures, named-data retention,
null/scalar transitions and equality-based no-ops all use the existing atomic
FIELD transaction. Resource identities, ownership, evaluator code and unselected
FIELD trees remain unchanged. This is not a private binary-schema decoder or a
native evaluator. Date, object-ID and point cache kinds remain separately guarded.

## Evidence and independent verification

The recovered binary implementation adds 178 C# cases. Its Buffer inputs are
explicit synthetic modifications of the two pinned compact/modern native-shaped
FIELD carriers, not new native producer samples. The six profile families test
the declared storage behavior; they do not establish arbitrary native conversion.

`verify_field_binary_values.py` requires exactly 192 files, or 96 before/after
pairs: eight lengths (0, 1, 126, 127, 128, 254, 255 and 1024) in six source profiles
and both transports. The before FIELD bodies must match the pinned producer
packets. Expected buffer bytes and canonical chunk boundaries are calculated
independently of production code. The parent literal display is checked together
with the child cache, and every other ordered physical record must remain exact.
Only the prior strictly checked empty ACAD_LAYERSTATES identity is normalized;
HEADER time/seed and CLASS records are outside that comparison.

The same whole-record comparator rejects changes to every emitted FIELD tag,
missing chunks, changed bytes, wrong type/length, additional chunks, missing
participating records and alterations to every unrelated record. All 192 drawings
also undergo ezdxf audit without errors or repairs. Against the recovered head
`9102db0b4b16d4b89c903a6df61b88dc794d1714`, this gate passed all 96 pairs and
rejected 13,096 actual-output corruptions. The existing 142 independent gates
were also replayed successfully on that exact Debug artifact. Final-head CI
results are recorded in PR105 rather than inferred from the earlier checkpoint.

```sh
python tools/verify_field_binary_values.py artifacts/conformance
```

Native AutoCAD execution, host display synchronization, universal FIELD semantics,
private binary interpretation and full DXF compliance are not claimed.
