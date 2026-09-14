# OLE2FRAME optional metadata presence

## Reproduced loss on the PR #80 implementation

The reader accepts absent version, description, upper/lower corner, relationship and tile-mode tags, but the writer previously emitted all six fields with defaults. The public getters alone could not distinguish an omitted point from an explicit zero point, or no description from an explicitly empty one. Cloning also lost that distinction.

The implementation adds `Ole2FrameMetadataFields` and a read-only `MetadataFields` property. Existing public constructor signatures and default-valued getters remain unchanged. The constructor selects `All`; the reader retains the precise subset found in the packet; the writer emits exactly that subset. A supplied point must still include all three components. Binary length, canonical payload chunking, required `OLE` terminator, following XData, validation, literal-backslash escaping and exact-identity-only transforms preserve the PR #80 contract.

`WithMetadataFields(selection)` explicitly creates a copied snapshot with a different serialization selection. It does not change values or binary content, and it does not copy database identity. Dormant values are preserved within that snapshot and its clones, but an omitted value is intentionally not serialized. Reloading such output exposes the same defaults as before. Selecting `All` after import explicitly opts into serializing those current defaults; no automatic invention occurs. Unknown flag bits reject before touching the original.

```csharp
// Existing constructor and properties are unchanged.
var frame = new Ole2Frame(bytes, upperCorner, lowerCorner);
var binaryOnlyHints = frame.WithMetadataFields(Ole2FrameMetadataFields.None);
var copy = (Ole2Frame)binaryOnlyHints.Clone();
// copy.MetadataFields == None. The binary packet and required terminator are still emitted.
```

## Version and pipeline contract

All six admitted typed profiles (2000/2004/2007/2010/2013/2018) retain the selected metadata presence in text and binary. R12/R13/R14 typed input remains unadmitted. This corrects an existing accepted-input round-trip; it does not add historical dialects, private extensions, OLE activation, rendering or payload-aware geometric editing. Default-valued properties are distinct from wire presence. Exact byte formatting remains the separate raw pipeline's responsibility.

## Regression evidence

Base: PR #80 source tree `a919d8864705287c50be3f728c5bec9baae59ba1`. The 768 independent-wire cases cover every one of the 64 metadata-presence combinations, all six profiles and both transports. On unchanged production: **17,527 passed / 756 failed**, Debug and Release. Corrected production with the identical tests: **18,283 passed / zero failed**, both configurations. Sixty-seven additional API cases cover all 64 explicit selections, clone isolation, dormant values, compatibility defaults and invalid flags. The final integration count is recorded separately in the delivery report; baseline totals are not substituted for integration results.

Tests retain source identity/common data and following LINE/XData, deep clones, nested INSERTs and three alternating persistence cycles. The independent verifier uses ezdxf stored tags—not netDxf getters—to inspect 96 drawings / 192 field selections, exact payload bytes, present values and absence. Its clean DXF structural audits do not certify native OLE content. Local runs use a separately compiled strong-named production assembly and .NET 8.0.31. Windows/SDK/netstandard2.0 and native AutoCAD execution for this increment remain unexecuted here.

## Primary sources

- Autodesk OLE2FRAME published table and sample: https://help.autodesk.com/cloudhelp/2019/ENU/AutoCAD-DXF/files/GUID-77747CE6-82C6-4452-97ED-4CEEB38BE960.htm . It distinguishes informational metadata from the authoritative binary object; it is not a guarantee that all producers emit every redundant field.
- ezdxf maintainer's preservation observations: https://github.com/mozman/ezdxf/discussions/1097 . The correction's decisive evidence is independently encoded accepted input and exact output tags, not an assumption that all historically declared profiles admit all native OLE variants.
