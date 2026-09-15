# LIGHTLIST stored metadata

`DxfLightList(int storedVersion)` models the published `AcDbLightList` packet for DXF 2007, 2010, 2013 and 2018, in ASCII and binary. The required constructor argument is the exact signed group-90 version value. The API supplies no default and makes no claim about which values Autodesk recognizes. Negative values and all other Int32 values remain unchanged.

## Public contract

| Wire data | Public model | Behavior |
| --- | --- | --- |
| First payload90 | `StoredVersion` | Explicit uninterpreted Int32 |
| Second payload90 | `Entries.Count` | Actual number of complete pairs |
| Repeated5 | `DxfLightListEntry.Light` | Exact registered `Light` object reference |
| Following1 | `DxfLightListEntry.Name` | Independent stored name, including an empty string |

Entries retain order and duplicate targets. Renaming a LIGHT does not overwrite a stored entry name. Entry values are immutable; replace an entry to change its target or name. Names reject null, NUL, CR/LF and malformed UTF-16 before collection mutation. Unicode and literal DXF-looking backslash sequences round-trip.

Objects use the existing generic dictionary, extension dictionary, reactor and XData APIs. No `ACAD_LIGHT` attachment helper is provided: the published description mentions this NOD entry but does not establish a concrete application layout. A custom dictionary in the corpus is a storage test, not a native application placement claim.

The references participate in database validation and owned-graph cloning. A cross-document clone requires exact destination LIGHT mappings; missing or wrong-type mappings fail before allocation. Names remain unchanged while payload group5 handles follow their mapped LIGHT targets. These fields are references, not object identities or SORTENTSTABLE sort keys. The raw generic handle index remains schema conservative; this module adds typed object reference interpretation only.

## Parser and profile boundary

The recognized body is exactly100 `AcDbLightList`,90 version,90 count, followed by count pairs of5 LIGHT handle and1 name. The count is checked against actual tag count without allocating from a declared size. Missing/duplicate public fields, incorrect pair order, wrong or unresolved LIGHT targets, invalid names and nonterminal XData fail. Unknown fields or private subclass markers preserve the entire object as `DxfOpaqueObject`, including its common metadata and raw XData. DXF2000/2004 input also remains opaque; typed save into those profiles fails before output.

The writer declares `LIGHTLIST` / `AcDbLightList` / `SCENEOE`, nongraphical, with proxy flags1025 when a declaration is needed. This default follows the pinned independently published LibreCAD writer declaration, not a native fixture. Existing compatible declarations retain their application and flags; their instance count follows physical LIGHTLIST objects and reaches zero after the last object disappears. Incompatible private declarations remain untouched when no typed instance requires them.

## Independent evidence and limits

The [Autodesk LIGHTLIST page](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-C4E7FFF8-C3ED-43DD-854D-304F87FFCF06.htm) and [official2007 reference PDF](https://damassets.autodesk.net/content/dam/autodesk/www/developer-network/platform-technologies/autocad-dxf-archive/acad_dxf_2007.pdf) publish the packet. Neither establishes a default, valid range or interpretation for its version number. The2007 publication supports this conservative export floor; it is not an introduction-date claim.

Pinned independent implementations corroborate the grammar:

- [IxMilia.Dxf LIGHTLIST source](https://github.com/ixmilia/dxf/blob/3ab0f9d6d3f14a6f6fa924e111e8e3af1065c567/src/IxMilia.Dxf/Objects/DxfLightList.cs) writes real light handles and names, but discards names when reading and uses library-defined synthetic version values. The committed producer uses NuGet0.8.4, separately identified and hashed in its manifest; the source pin is research evidence, not a claim that the package was built at that commit.
- [LibreDWG schema](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/src/dwg2.spec) has version/count and repeated light-handle/name fields without constraining a version value.
- [LibreCAD class registrar](https://github.com/LibreCAD/LibreCAD/blob/0b2d9b2f3c57f337541f01f797b6d7d71e3743b8/libraries/libdxfrw/src/intern/dwgwriter.h) publishes the class/application/proxy defaults used for newly declared objects. Its DWG registration is independent implementation evidence, not native DXF interoperability evidence.

The eight pinned LibreDWG examples and samples listed in `tests/fixtures/lightlist/schema-assessment.json` contained zero LIGHTLIST objects. Four contained real LIGHT entities. This bounded corpus search does not prove that no native sample exists elsewhere.

The eight source fixtures are explicitly **synthetic IxMilia.Dxf0.8.4 output with documented low-level augmentations**. They cover four profiles and both original transports, five explicitly chosen raw version values, an empty list, duplicate LIGHT targets and independently stored names. The generator repairs known unrelated producer scaffold problems (missing root owner, empty optional handle strings and unscoped default STYLE1071), adds common metadata, and replaces derived names with declared independent names. The manifest records original and final byte hashes and every augmentation. No native AutoCAD-created input, native LIGHTLIST execution or semantic-version validity is claimed.

The mandatory `tools/verify_lightlist.py` requires exactly32 output drawings:16 external round-trips,8 authored graphs and8 mapped clones. It uses independent low-level parsing because ezdxf has no LIGHTLIST model and IxMilia's reader drops the names. It checks exact grammar, signed versions, identities, ordered references, names, dictionary aliases, extension ownership, XData, class declarations, LIGHT vectors and following LINE. Zero ezdxf audit changes qualify only the ancillary common graph.

## Qualification receipt

At production checkpoint `6bcc867` with the stricter independent gate from `4e4870e`:

| Check | Result |
| --- | --- |
| Focused LIGHTLIST conformance | 189 passed, 0 failed |
| Full Debug conformance | 22,236 passed, 0 failed |
| Full Release conformance | 22,236 passed, 0 failed |
| Release build targets | netstandard2.0, net471, net48, net6.0, net8.0 compiled |
| Independent Debug gates | 66 scripts passed, 0 failed |
| Independent Release gates | 66 scripts passed, 0 failed |
| LIGHTLIST gate in each configuration | 32 required drawings, 112 physical LIGHTLIST objects |

The189 focused cases include120 recognized malformed packets,24 private-extension fallbacks,16 independent-source round-trips,8 authored graphs,8 mapped clones,8 class contracts,4 older-profile checks and1 authoring-validation scenario. Additional independent negative probes reject an altered stored name and a payload group5 that points at the LIGHTLIST object instead of its LIGHT. No native AutoCAD-created LIGHTLIST or native application behavior is part of these results.
