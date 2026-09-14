# Inert OLE2FRAME persistence

`Ole2Frame` stores the published AcDbOle2Frame metadata and uninterpreted bytes. The entity is integrated into document registration/removal, active-layout enumeration, blocks, cloning, text and binary persistence across the six existing 2000+ typed profiles. No older typed dialect is enabled.

## Deliberately inert contract

The constructor copies the supplied bytes; `GetBinaryData()` returns a separate copy. Version, user-type description, WCS upper-left/lower-right corners, object relationship and tile-mode descriptor are immutable. Common layer/color/visibility properties remain ordinary entity metadata. Links are never followed, COM is never invoked, and no embedded data is executed, rendered or interpreted.

Autodesk says these metadata values are read-only and redundant with private OLE binary content, which AutoCAD obtains from the object. Consequently `TransformBy` accepts **only exact identity** and rejects every other matrix/translation before mutation, including through transformed INSERT explosion. Changing the bounding corners alone would claim a transformation that the actual payload has not undergone. The stored tile-mode descriptor is retained independently of current ownership; placing a copied frame in another space does not rewrite its private object.

## Framing and validation

The reader requires AcDbOle2Frame, a nonnegative group-90 byte count, exact accumulated data length, and a group-1 OLE terminator. Metadata may be reordered; supplied corners must be complete, and duplicate scalar fields reject. Missing metadata other than length/terminator uses documented model defaults (version2, embedded type, model tile mode, empty description and zero corners). Unknown private extensions reject instead of disappearing; raw preservation remains the route for such records.

Storage grows only from actual consumed chunks, never the declared count. Group310 chunk lengths are bounded by127 bytes. The writer canonicalizes chunk boundaries while preserving every byte, emits the exact derived count and terminator, then XData. CR/LF/NUL in descriptions reject, and literal backslashes survive Unicode-escape decoding. Malformed lengths, missing/invalid terminators, extra data after termination, incomplete points and private markers are covered. File and document resource budgets beyond existing typed-reader behavior remain separate work.

The Autodesk table labels group3 as binary length, but its own example uses `Paintbrush Picture`; this implementation stores group3 as the string user-type description and group90 as the integer byte count. It does not reinterpret an arbitrary description as a count.

## Executed evidence

349 new registered C# cases cover six profiles, text/binary, three object relationships, lengths0/1/127/128/255/1025, noncanonical input chunking, reversed metadata/comments, exact binary64 corners, Unicode, literal escapes, original/clone repeated transport changes, XData, following LINE, model/paper/nested/unused blocks, clone isolation, removal, constructor validation and conservative transformations. Full local separately compiled signed-library runs: **17,515 passed / zero failed**, Debug and Release on .NET8.0.31.

An initial invalid-input fixture tried to construct a NaN DxfTag and was correctly rejected before reaching the parser. The final fixture injects nonfinite bytes after encoding instead; codec validation was not weakened. New public APIs cannot compile against the preceding assembly, so no unchanged-production API red count is invented.

`verify_ole2frame.py` uses ezdxf1.4.4 to load twelve exported drawings /24 frames, checking exact payloads, all emitted metadata, canonical chunking, XData and following LINE, with zero graph-audit errors/repairs. It explicitly uses the independent DXF Unicode decoder on opaque group3 tags. These synthetic bytes are not certified native OLE documents; graph audit does not validate payloads. Native AutoCAD/OLE rendering has not been executed. Final-head Windows/Linux SDK builds and netstandard2.0 must pass before merge.

```csharp
var frame = new Ole2Frame(bytes,
    new Vector3(0, 10, 0), new Vector3(20, 0, 0),
    description: "Stored object", objectType: OleObjectType.Embedded);
document.Entities.Add(frame);
byte[] detachedCopy = frame.GetBinaryData();
```

Primary reference: [Autodesk OLE2FRAME](https://help.autodesk.com/cloudhelp/2019/ENU/AutoCAD-DXF/files/GUID-77747CE6-82C6-4452-97ED-4CEEB38BE960.htm). Other OLE entity families, private schema mutation, link refresh, rendering and complete binary-native validation remain separate.
