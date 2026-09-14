# Raw OBJECTS views and transactions

`DxfRawObjectStore` edits recognized records within an immutable `DxfRawDocument`.
It retains unrelated entities, sections, private classes, application payloads,
and raw tags. It does not pass the drawing through `DxfDocument` and does not
evaluate CAD objects or load application extensions.

```csharp
using netDxf.IO;

using var input = File.OpenRead("drawing.dxf");
var source = DxfRawDocument.Load(input);
var store = DxfRawObjectStore.Open(source);
using var edit = store.BeginEdit();
string root = edit.EnsureRootDictionary();
string project = edit.CreateDictionary(root, "PROJECT_METADATA");
string revision = edit.CreateVariable(project, "Revision", "A.3");
string record = edit.CreateXRecord(project, "ApplicationData", new[]
{
    new DxfTag(1, "Project drawing"),
    new DxfTag(90, 42),
    new DxfTag(330, revision)
});
var updated = edit.Commit();
updated.SaveAtomic("drawing-with-metadata.dxf");
```

`Commit()` returns a new snapshot; the original remains available. A semantic
no-op returns the original snapshot, retaining its original bytes. Saving is a
separate operation. Call the raw document's existing atomic-save API when a file
replacement must be atomic. Text comments are retained; converting a text DXF
containing comment tags to binary requires the caller's explicit removal policy.

## Supported records and operations

| Record | View and editing |
| --- | --- |
| `DICTIONARY` | Ordered, case-insensitive named lookup; original key spelling; aliases; nullable ownership/cloning flags; creation, rename, replacement, unlink, subtree clone/delete |
| `ACDBDICTIONARYWDFLT` | Dictionary operations plus explicit default reference; creation supplies an owned default placeholder |
| `XRECORD` | Ordered immutable typed tags, independent cloning flag, payload replacement; 100/101/102 are ordinary application payload values |
| `ACDBPLACEHOLDER` | Owned inert object creation and cloning |
| `DICTIONARYVAR` | Value/schema presence, portable Unicode escaping, and explicit absent-versus-empty values |
| `IDBUFFER` | Ordered soft pointers retaining nulls and duplicates |
| `SORTENTSTABLE` | Block/entity draw order, arbitrary sort keys, extension dictionary creation, and optional `$SORTENTS` regeneration flag |
| Other/private/malformed records | `DxfRawOpaqueStoredObject` reports its reason and retains the exact tag sequence; unrelated edits preserve it |

Object editing requires an explicit AutoCAD 2000 or newer profile. New draw-order
tables require AutoCAD 2004 or newer. Editing updates `$HANDSEED`, required
`CLASSES` declarations and class instance counts. `DxfRawObjectStoreOptions`
provides object, per-object/payload-tag, and staged-record budgets.

Autodesk documents XRECORD payload group codes 1–369 except 5 and 105. This raw
editor uses that range for authoring. Values 100 and 101 inside the application
payload do not start additional subclasses or embedded-object grammar. Code 102
does not start a common control group there. Code 280 is the cloning flag only
at the beginning of the `AcDbXrecord` body; later values belong to the payload.
Records containing unsupported payload codes remain opaque. See
[Autodesk's XRECORD group-code reference](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-DXF/files/GUID-24668FAF-AE03-41AE-AFA4-276C3692827F.htm).

## Ownership, references, and failure behavior

Each public mutation runs as an atomic staging operation. Argument failures,
exceptions from application iterators, cancellation, budget exhaustion, or
reentrant calls roll back that operation, including handle reservations and
parent links. Earlier successful operations remain staged. A rejected commit
keeps staging available for correction. A successful commit closes the
transaction; further reads, writes, or commits throw `ObjectDisposedException`.
Disposal discards uncommitted edits and can be called repeatedly. Transactions
are single-threaded and must not be shared between concurrent callers.

New handles avoid every exposed existing handle value, including unresolved
references, opaque handle slots, arbitrary values, and supplied new pointers.
An unresolved pointer therefore cannot accidentally become valid because a
later object receives the same handle. Edited references must resolve uniquely
at commit. Existing unrelated private or unresolved data is retained without
being certified valid.

Dictionary links do not transfer ownership. Both group-350 and group-360 entries
must target another object whose common owner is already that dictionary,
regardless of the dictionary's hard-owner flag. Multiple names may alias the same
owned target. Self-links and cross-owner aliases reject before staging; commit
also rejects them in an edited imported dictionary. Use XRECORD or IDBUFFER
pointers for references to objects owned elsewhere. Existing unrelated raw
dictionaries retain their original data without being certified valid. Explicit unlink
operations retain the target and its common-owner field; they do not silently
erase an object that another application may use. To remove the owned subtree,
pass `deleteOwnedTree: true`. Deletion rejects incoming external or opaque
exposed references and refuses unknown owned schemas; failed combined
unlink/delete restores the parent entry.

`EnsureExtensionDictionary` and `RemoveExtensionDictionary` operate on the
recognized common `{ACAD_XDICTIONARY` control group. They preserve unrelated
entity fields and require matching dictionary ownership. Existing private
schemas are preserved, but unsupported grammar is not rewritten speculatively.

`CloneDictionaryTree` allocates all owned identities before translating exposed
links, so aliases and forward references remain consistent. It follows common
ownership instead of dictionary-name reachability. Internal ownership,
dictionary, pointer, extension-dictionary, and XData 1005 links are translated;
external references remain external. Arbitrary 320-series handles, draw-order
sort keys, and handle-looking strings/binary data remain unchanged. Reactors
are omitted from the clone. Private common control groups and unknown owned
schemas reject the clone. This API clones within one document and does not
implement cross-document symbol-table conflict resolution.

## Validation

The conformance runner registers `objects/` tests for all six supported DXF
versions and text/binary transports, three persistence cycles, all six cloning
flags, aliases/defaults, extension dictionaries, draw order, payload isolation,
Unicode, iterative deep-tree cloning/deletion, failure rollback, lifecycle,
cancelled transactions, unknown-object preservation, XData remapping, and
external-reference deletion guards. Two hash-pinned drawings authored with ezdxf
exercise independent read/edit/clone behavior, including application payload
after code 100. Malformed known packets remain opaque during unrelated edits.
Generated `object-store-*.dxf` fixtures
support independent reader validation.

These APIs provide storage and graph operations for the listed schemas. They
do not provide arbitrary proprietary-object evaluation, CAD geometry/modeler
services, automatic reactor semantics, or universal DXF object coverage.
