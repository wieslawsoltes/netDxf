# LAYER_FILTER and OBJECT_PTR stored envelopes

This module exposes the published stored fields of `LAYER_FILTER` and `OBJECT_PTR` through `DxfDocument.Objects`. Both envelopes support reading, editing, writing and graph cloning across the six tested DXF profiles (2000, 2004, 2007, 2010, 2013 and 2018), in text and binary transport.

The qualification covers field storage and common object metadata. It does not establish historical release legality, native AutoCAD filtering behavior, ASE database behavior, or an application-specific dictionary placement convention.

## API and stored fields

```csharp
var document = new DxfDocument();
var application = new DxfDictionary();
var filter = new DxfLayerFilter(new[] { "Walls", "Walls", "Unresolved" });
var pointer = new DxfObjectPointer();
application.Add("FILTER", filter);
application.Add("POINTER", pointer);
document.NamedObjects.Add("MY_APPLICATION", application);
filter.LayerNames.Add("Details");
```

| Object | Public storage | Wire representation |
| --- | --- | --- |
| `DxfLayerFilter` | Ordered, editable `Collection<string> LayerNames` | `100 AcDbFilter`, `100 AcDbLayerFilter`, then repeated group 8 strings |
| `DxfObjectPointer` | Inherited common metadata and XData | No public subclass or pointer payload |

Both objects require a dictionary owner. They use the normal dictionary adoption and `Objects.Clone` / `Objects.CloneObject` APIs. There is no automatic `ACAD_INDEX` placement, default `DC015` registration, or special ASE helper.

Layer names are stored text. The collection preserves order, duplicates, exact case, unresolved names, Unicode and literal strings such as `Literal\U+0041`. Renaming or removing a document layer does not modify these strings. Cloning does not resolve names through layer mappings. A constructor snapshots its input, and clones have independent name collections. Names must be nonempty single-line Unicode without NUL, CR, LF or unpaired surrogates; an empty collection is valid.

Handles, dictionary aliases, ownership, extension dictionaries, persistent reactors and XData use the existing typed object database. Two-pass cloning remaps internal references and requires explicit exact-identity mappings for external references. `DC015` is ordinary application data if supplied by the caller or file. The record name does not imply a separately modeled pointer field.

## Exact admission and opaque retention

A `LAYER_FILTER` becomes typed only when its payload contains the two exact public subclass markers followed exclusively by group 8 names and optional XData. A common-only `OBJECT_PTR` becomes typed when the recognized header is followed only by optional XData.

Other payloads stay `DxfOpaqueObject`, including different subclasses, the ezdxf group 330 filter variant, private header fields before or after the owner, and complete unfamiliar control groups. The targeted scanner preserves those fields instead of dropping pre-subclass data. A distinct additional bare group 330 after the first owner is retained as private opaque payload; it is not interpreted as a new public pointer. Existing opaque-object clone restrictions continue to apply.

Recognized malformed headers reject: duplicate identities, a repeated identical bare owner, repeated reactor or extension groups (including empty reactor groups), invalid recognized group content, nested or unterminated control groups, and stray closing braces. Unfamiliar fields do not suppress these checks. Invalid group 8 names in an otherwise exact public payload also reject. The scanner change is limited to these two record families.

## CLASS metadata

The writer creates the following declarations when a typed instance exists:

| Record | C++ name | Application name | Proxy flags | Was proxy | Entity |
| --- | --- | --- | --- | --- | --- |
| `LAYER_FILTER` | `AcDbLayerFilter` | `ObjectDBX Classes` | 0 | 0 | 0 |
| `OBJECT_PTR` | `CAseDLPNTableRecord` | Empty | 1 | 0 | 0 |

Autodesk's default-class table supplies the `OBJECT_PTR` C++ name and flags, but omits the application name; the generated empty string makes that omission explicit. No ASE application name is inferred. Supplied matching declarations retain application name, flags and proxy status. Conflicting C++ identities or entity classification reject before output when a typed instance is present. Instance counts then include all physical records of that type, including opaque records; aliases do not increase the count. Group 91 is omitted for the 2000 profile by the existing class writer.

Unused declarations and opaque-only record families preserve their supplied private class metadata, including their stored count. This avoids imposing a typed identity on data retained without interpretation.

## Evidence and limitations

The primary payload references are Autodesk's [LAYER_FILTER table](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-3B44DCFD-FA96-482B-8468-37B3C5B5F289.htm) and [OBJECT_PTR table](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-6D6885E2-281C-410A-92FB-8F6A7F54C9DF.htm). The latter names common object fields and application data, with no public pointer payload. The C++ identity and flags come from [Default Class Values](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-DBDBE57E-9045-46A5-9FB9-99CADEB81CF0.htm). The public field tables are also present in the [official 2009 DXF reference](https://damassets.autodesk.net/content/dam/autodesk/www/developer-network/platform-technologies/autocad-dxf-archive/acad_dxf_2009.pdf).

The installed ezdxf 1.4.4 implementation writes `LAYER_FILTER` names as group 330 handles, contrary to Autodesk's repeated group 8 strings. Its high-level parser therefore cannot verify the names in this module. Its version hints and the available corpus do not provide native historical qualification; the inspected independent repository corpus contained no actual records of either family. No arbitrary release gate is inferred from those hints.

The six checked-in source fixtures are clearly labeled **independently authored public-schema tags over ezdxf 1.4.4 document scaffolding**. The generator replaces placeholder record payloads at the ordered-tag level, updates class counts, and records SHA-256 hashes and object identities in a manifest. It does not load and re-save the modified payloads through ezdxf. An ezdxf audit supplies ancillary common-envelope evidence only.

`LayerFilterPointerTests.cs` registers 62 scenarios covering six-profile text/binary authoring and source roundtrips, opaque variants, name and metadata cloning, invalid names and headers, exact duplicate metadata rejection, and private/unused/mixed CLASS behavior. Several scenarios contain multiple independent transport or mutation checks.

`tools/verify_layer_filter_pointer.py` requires the exact 24 authored/external output filenames and six source hashes/profiles. It checks actual DXF version and transport signatures, low-level ordered group 8 payloads, exact empty envelopes, physical class counts, aliases, identities, ownership, reactors, XData references, extension XRECORD data, following LINE geometry and zero ancillary audit changes. It deliberately does not use ezdxf's high-level filter name interpretation.

```sh
python tools/verify_layer_filter_pointer.py artifacts/conformance
```
