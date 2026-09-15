# UCS record orthographic base: stored identity support

The published UCS-record schema describes group79 as always zero and also defines a base-UCS handle346 that only applies when79 is nonzero. The former reader followed the reserved-zero description and discarded346. This assessment concerns the UCS table record itself; the implemented VIEW/VPORT345/346 relationships and repeated UCS71/13/23/33 origin overrides are separate.

The [Autodesk UCS schema](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-1906E8A7-3393-4BF9-BD27-F9AE4352FB8B.htm) says that a missing346 with a nonzero79 uses WORLD. Its description of71 identifies per-orthographic origin overrides, rather than the record's own base relation. The public [AcDbUCSTableRecord methods](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-__MEMBERTYPE_Methods_AcDbUCSTableRecord.html) provide the axes, origin and per-view origin overrides, without a documented base-object assignment method. This contradiction prevents claiming native CAD support from the documentation alone.

Two pinned independent implementations provide explicit stored-schema evidence:

- [LibreDWG dwg.spec at34f02f54](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/src/dwg.spec) stores UCSORTHOVIEW79 and conditional hard-pointer base_ucs346 for UCS from R2000b. Its [UCS unit probe](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/test/unit-testing/ucs.c) inspects both fields separately from origin overrides.
- [IxMilia's table specification at3ab0f9d6](https://github.com/ixmilia/dxf/blob/3ab0f9d6d3f14a6f6fa924e111e8e3af1065c567/src/IxMilia.Dxf.Generator/Specs/TableSpec.xml) declares DxfUcs.OrthographicViewType79 and BaseUcsHandle346 from R2000, separately from OrthographicType71 and OrthographicOrigin13.

IxMilia.Dxf0.8.4 generated and reloaded the twelve committed files under `tests/fixtures/ucs-record-base`: six typed profiles, each in text and binary. Each file contains a real UCS E with79=5 and346=F, a base UCS F, and a WORLD-based UCS10 with79=2 and346 absent. The origin override71=6 remains independent. The producer allocates handles during save, so generation first allocates them, binds the actual parent handle, then saves and reloads. No DXF packet was patched. The manifest pins the package and output hashes and includes the generator.

No positive native UCS base packet was found. All217 repository fixtures parsed and contained12 UCS records, all without a nonzero79 or346. All67 files in the pinned LibreDWG corpus were downloaded and Git-blob verified;66 parse and contain no UCS records. The remaining R1.4 file starts with `EXTENTS,1` and is recorded as unparsed, not counted as a negative result. The detailed hashes and counts are in [ucs-base-assessment.json](ucs-base-assessment.json). This is evidence about the inspected corpus only.

The implementation exposes `UCS.OrthographicViewType` (group79, zero through six) and `UCS.BaseUcs` (nullable group346). `SetOrthographicBase(short viewType, UCS baseUcs = null)` changes the pair atomically. Zero clears the relationship and cannot accompany a target. Types one through six represent Top, Bottom, Front, Back, Left and Right. A nonzero type with no base uses the documented WORLD default; a physically present numeric-null346 survives load, save and clone until an explicit edit clears its presence.

A registered referring UCS can target only the actual registered UCS in the same document. The reader resolves canonical numeric source handles after table loading and checks the retained source instance, so forward references work and absent, discarded, wrong-kind or generated substitute targets fail. Unknown types outside zero through six and invalid conditional pairs reject clearly. Private102 groups, unknown later100 subclasses and data after the XData boundary do not supply the new relationship fields. A private1001 inside102 does not terminate subsequent public79/346 capture.

Existing UCS reference counts now include UCS-record dependencies alongside VIEW and VPORT users. A referenced target cannot be removed; removing the referring record releases its outgoing reference. Self references and cycles remain explicit stored pointers and do not trigger recursive evaluation or cascading deletion. Save preflight validates registered target identities and the stored pair before writing.

Detached UCS clones retain the actual base identity, type and physical-null presence. Adoption by another document rejects foreign targets before allocating a handle or changing collections. Map explicitly before adoption:

```csharp
var copy = (UCS)sourceUcs.Clone("COPY");
copy.SetOrthographicBase(copy.OrthographicViewType, destination.UCSs["SURVEY_BASE"]);
destination.UCSs.Add(copy);
```

This module stores identity and reference data. It does not activate a coordinate system, calculate an orthographic frame, transform coordinates, change existing axis normalization, or claim licensed native CAD acceptance. Group71 origin overrides remain distinct from group79.

The original producer drawings contain ten empty optional DIMSTYLE pointers340–344, which are not valid hexadecimal DXF handles. R2010 and later also contain two orphan STYLE1071=0 tags without the required application marker. `prepare_carriers.py` deletes exactly those ten empty pointer fields and the two orphan STYLE tags where present, preserving every remaining source byte, including complete UCS and LINE records. The twelve carrier files have separate pinned hashes; original producer files remain unchanged. The independent gate reconstructs each carrier from its original and rejects any additional byte change. This adaptation does not qualify unrelated producer DIMSTYLE or STYLE behavior.


The independent output gate reads the new UCS fields from raw tags because ezdxf1.4.4 does not model group79/346. It also runs an ancillary whole-file audit. Across the twenty R2004+ output drawings, that audit reports one repair per drawing for the original producer LINE20's transparency440=0; netDxf retains that stored value. The gate accepts only that exact entity, value and diagnostic, and reports the twenty repairs separately. It requires zero audit errors and zero UCS repairs. Any other repair fails. This observation does not establish new transparency behavior or native CAD acceptance.
