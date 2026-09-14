# Inert legacy OLEFRAME persistence

`OleFrame` implements the published legacy AcDbOleFrame packet independently of `Ole2Frame`: version70, byte-count90, repeated binary310 and the group1 `OLE` terminator. The model exposes immutable `OleVersion`, `BinaryDataLength` and `GetBinaryData()`, with defensive copies at construction/getter/clone boundaries. Version defaults to1 if absent; nonnegative values are retained rather than coerced to1. Empty payloads are permitted. No OLE2-only bounding corners or relationship fields are invented.

The reader allows reordered version/count fields, including a count after actual chunks, while enforcing uniqueness, nonnegative/exact length, <=127-byte chunks, mandatory termination and XData only after the packet. It grows storage from consumed bytes, never from a declared count. Unknown extensions reject rather than being discarded. The writer emits canonical chunks and retains exact payload bytes, although original chunk partitioning is not retained.

Document registration/removal, active-layout enumeration, model/paper/nested/unreferenced blocks, independent clones and coexistence with OLE2FRAME are implemented across the six existing2000+ typed profiles. Earlier typed dialects remain rejected. Raw pipelines are separate; these tests do not certify historical payload compatibility.

The payload is **never activated, linked, decoded or rendered**. Exact identity is the only accepted transformation. Every other transform, including through moved/scaled INSERT explosion, rejects before mutation because netDxf cannot truthfully transform geometry stored inside an uninterpreted payload. Common layer/color metadata remains editable independently of native OLE data.

## Executed evidence

289 new registered C# cases exercise six profiles and both transports, six payload sizes, early/late count/version fields, noncanonical chunks, comments, repeated persistence, exact payloads, clones/XData/following LINE, four placements, OLE2 coexistence, fourteen invalid-packet variants and API/nonmutation controls. Full local signed-library execution: **17,804 passed / zero failed**, Debug and Release. The new public API cannot compile against the preceding assembly, so no old-assembly API red count is claimed.

The independent `verify_oleframe.py` uses ezdxf1.4.4's **generic tag storage**, not a semantic legacy OLE model. It checks twelve drawings /24 packets, exact bytes, metadata/chunk framing, common color, XData and adjacent LINE; graph AUDIT reports zero errors/repairs. Synthetic bytes are not certified valid native OLE objects. Final-head CI must execute Linux/Windows Debug/Release and actual netstandard2.0 builds before merging. Native AutoCAD/OLE rendering remains unexecuted.

```csharp
var frame = new OleFrame(bytes, oleVersion: 1);
document.Entities.Add(frame);
byte[] detached = frame.GetBinaryData();
```

Primary reference: [Autodesk OLEFRAME group codes](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-4A10EF68-35A3-4961-8B15-1222ECE5E8C6.htm). This completes the scoped inert legacy packet, not native OLE editing, application-specific extensions or the entire DXF standard.
