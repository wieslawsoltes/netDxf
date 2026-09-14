# Typed named-object database

`DxfDocument.Objects` and `DxfDocument.NamedObjects` integrate application dictionaries into the document's typed reader, writer and handle registry. The model supports `DICTIONARY`, `ACDBDICTIONARYWDFLT`, `XRECORD`, `DICTIONARYVAR` and `ACDBPLACEHOLDER`. Objects created through this API are returned by `GetObjectByHandle`; their identities remain stable across text and binary saves in the six supported format families, R2000 through R2018.

Existing `Groups`, `Layouts`, `MlineStyles`, image/underlay definition collections and the layer-state manager retain their existing APIs. Their reserved root names cannot be replaced through `NamedObjects`. The public root contains application entries; the writer combines these with the collection entries in one named object dictionary.

## Authoring

```csharp
using netDxf;
using netDxf.IO;
using netDxf.Objects;

var document = new DxfDocument();
var settings = new DxfDictionary();
var revision = new DxfDictionaryVariable { Schema = 0, Value = "Review" };
var payload = new DxfXRecord();
payload.Data.Add(new DxfTag(1, "Application data"));
payload.Data.Add(new DxfTag(90, 12));
payload.Data.Add(new DxfTag(310, new byte[] { 1, 2, 3 }));
settings.Add("REVISION", revision);
settings.Add("DATA", payload);
settings.Add("DATA_ALIAS", payload, hardOwner: false);
document.NamedObjects.Add("MY_APPLICATION", settings);

// Register first, then author a pointer to the assigned identity.
payload.Data.Add(new DxfTag(340, revision.Handle));
var diagnostics = document.Objects.Validate();
document.Save("application.dxf");
```

Names are case insensitive. Entries retain their ordering, spelling and individual 350/360 ownership code; aliases share object identity. Group 280 is retained independently, and a target is treated as hard-owned when either the dictionary flag is set or the link uses 360. Both soft and hard dictionary ownership links must agree with the target's one owner. Multiple names may alias the same object within that owner; cross-dictionary aliases and dictionary self-entries are rejected because independent CAD auditing removes them. XRECORD soft pointers can form non-owning reference cycles. Graphical entities cannot be dictionary entries; an XRECORD can hold their pointer handles instead.

Adding an unowned detached graph establishes its owners and registers its objects. Adding a foreign registered graph fails; use `Objects.Clone` to copy it. `Remove(name)` removes a dictionary link only. It neither destroys objects nor changes handles. Removing the last owning entry makes validation fail until an entry is restored; this prevents saving a silently orphaned owned object. This API does not provide cascade erasure; the raw OBJECTS transaction API supplies explicit graph-editing operations.

## XRECORD data and preservation

`DxfXRecord.Data` is an editable collection of immutable `DxfTag` values. Each code requires its exact CLR representation: for example, 40 takes `double`, 70 takes `short`, 90 takes `int`, 160 takes `long`, 290 takes `bool`, and 310 takes `byte[]`. Binary input and output arrays are copied. Authored binary chunks are limited to 127 bytes.

Public authoring follows the documented payload range 1–369, excluding identity codes 5 and 105. The initial subclass marker and cloning flag are managed separately. Subsequent payload groups 100, 102 and 280 remain application data, including repeated values; they are not mistaken for common metadata.

The reader additionally retains already-present, defined normal-object tags above 369 and below 999. This accommodates existing layer-state fields such as 370 and 440. Their primitive representation is retained without claiming a private application schema. Clone and serialization preserve these existing tags through an internal path; public insertion or replacement of newly authored extended codes is rejected. This differs deliberately from the raw OBJECTS module's stricter schema classification.

Dictionary names, variables and XRECORD strings round-trip Unicode and literal backslash escape sequences. CR/LF-containing payload strings require binary transport. Text saving rejects these strings before writing destination bytes.

## Extension dictionaries and reactors

```csharp
var line = new netDxf.Entities.Line(Vector3.Zero, Vector3.UnitX);
document.Entities.Add(line);
var extension = new DxfDictionary();
extension.Add("DETAILS", new DxfXRecord());
document.Objects.SetExtensionDictionary(line, extension);
payload.PersistentReactors.Add(revision);
```

Extension dictionaries are attached only after the owner is registered. The reader resolves object ownership, extension dictionaries and persistent reactors in separate passes, so targets can follow their referrers in the file. Common metadata is recognized only before the first subclass marker. Metadata works on supported entities, table records and typed database objects. The LAYER table's extension dictionary is reserved for the existing layer-state manager; attach application data to an individual layer record instead.

`PersistentReactors` stores application reactor references. Writers combine them with automatic entity, underlay, image and layer-state reactors into a single deduplicated control group. Missing source reactor targets fail explicitly. Generated image-definition reactors remain managed by the existing image pipeline.

The general `EntityObject.Clone` implementations do not automatically migrate an attached application object database. Copy typed ownership subtrees with the explicit database clone operation and provide mappings for any separately cloned entities.

## Cloning and validation

```csharp
DxfDictionary copy = document.Objects.Clone(
    settings, document.NamedObjects, "MY_APPLICATION_COPY");

var destination = new DxfDocument();
DxfDictionary imported = destination.Objects.Clone(
    settings, destination.NamedObjects, "MY_APPLICATION");
```

Cloning first allocates detached shells for the entire ownership subtree, including extension dictionaries. It validates and snapshots external mappings before registering the copies. A second pass remaps dictionary entries, aliases, defaults, owner links, extension links, persistent reactors, XRECORD pointer/ownership handles and XData 1005 references. Arbitrary 320–329 handle values are preserved verbatim. Payload arrays and XData/application-registry objects are copied; source registries keep their handles and owners.

For a cross-document reference outside the cloned ownership subtree, pass an `IReadOnlyDictionary<DxfObject, DxfObject>` mapping each source target to a registered destination target. Missing mappings fail before destination registration. Same-document clones preserve external references. Destination-name collisions are rejected; stored cloning flags do not execute automatic INSERT/XREF merge-name policies.

`Validate()` reports registration mismatches, missing or cyclic ownership, absent owning entries, competing hard ownership, invalid dictionary/default targets, dangling XRECORD pointers, dangling typed-object XData 1005 references, and invalid extensions/reactors. The writer checks this before preprocessing and again before output. Newly exposed XRECORD and imported opaque reference handles are reserved above the allocator to prevent later object creation from accidentally satisfying a dangling reference. In-place edits to the legacy mutable XData record list are checked at validation/save; they do not have per-edit allocator callbacks.

Loaded object types without a dedicated schema are represented as `DxfOpaqueObject`. Their ordered subclass payload and recognized common metadata are preserved, but private application semantics are not evaluated. Cloning an ownership subtree containing an opaque object fails explicitly. This module is not an arbitrary private-object, ACIS, constraint or application-service implementation. Known collection dictionaries continue to use their collection-specific editing behavior; application additions to their internal membership are outside this API. Use `DxfRawDocument` when complete tag-level preservation, including unrecognized common-header application groups, is required.

## Verification and references

The conformance suite covers each supported format family in both transports, then cross-transport resaving; alias identity; default lookups; payload 100/102/280; binary defensive copies; Unicode; extension dictionaries on objects/entities/layers; managed layer-state coexistence; graph-clone remapping; foreign application-registry isolation; rejected graphs; and handle reservation. `tools/verify_named_objects.py artifacts/conformance` independently checks the twelve authored fixtures with ezdxf, including exact low-level payload tags and zero audit errors or repairs.

The implementation uses the [Autodesk XRECORD reference](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-DXF/files/GUID-24668FAF-AE03-41AE-AFA4-276C3692827F.htm) and the [ezdxf dictionary/object documentation](https://ezdxf.readthedocs.io/en/stable/dxfobjects/dictionary.html). Independent ezdxf-created R2000 and R2018 fixtures also verify inbound mixed 350/360 aliases, nested dictionaries, default objects, arbitrary application payload markers and entity extensions.
