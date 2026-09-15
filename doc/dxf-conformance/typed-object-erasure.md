# Terminal erasure of typed database objects

`DxfDocument.Objects.EraseOwnedTree(root)` permanently removes a registered typed
OBJECTS record and its complete ownership subtree. It supports the existing
R2000, R2004, R2007, R2010, R2013 and R2018 text and binary profiles. This is a
stored database operation; it does not run application callbacks or evaluate
private CAD dependencies.

```csharp
var settings = new DxfDictionary();
var record = new DxfXRecord();
record.Data.Add(new DxfTag(1, "application settings"));
settings.Add("SETTINGS", record);
document.NamedObjects.Add("MY_APPLICATION", settings);

document.Objects.EraseOwnedTree(settings);
// settings and record are terminal objects; MY_APPLICATION is removed.
```

## Ownership and dependency rules

The operation follows each object's common `Owner` relation, including children
owned by an XRECORD and extension dictionaries. Both 350 and 360 dictionary
aliases participate in ownership regardless of the independent group280 flag.
Multiple names pointing to one object do not erase it multiple times. Pointer
references to blocks, entities, table resources or other database objects do not
enlarge the subtree.

The caller-selected root's owning dictionary aliases are all removed. A
reciprocal `owner.ExtensionDictionary` attachment is cleared at the same time,
including a dictionary parent that also has ordinary aliases to that extension.
The host remains registered. An object whose last dictionary name was already
removed can still be erased; the operation does not require unrelated parts of
the document to pass complete validation first.

Every other exposed incoming reference rejects erasure before mutation. In
particular, an owning parent's `DxfDictionaryWithDefault.Default` is a separate
pointer: callers must clear or replace it explicitly before erasing its target.
The checked sources include:

- All retained common owners, extension attachments, persistent reactors and
  legacy entity reactors.
- All retained XData1005 packets, including INSERT attributes and the ID1
  paper-space viewport stored directly on a layout. These carriers are included
  even when absent from the document's primary object registry.
- Dictionary entries/defaults, semantic XRECORD handle tags, and each typed
  object's existing `DatabaseReferences` schema hook.
- Recursive MULTILEADER component references, including leaders in unused
  blocks, and standalone or embedded layout shade-plot references.
- Custom header values whose group codes represent handles.
- Every exposed handle-valued tag in surviving opaque object payloads.

Stored handle strings are compared numerically, so case and leading zeros cannot
hide an incoming dependency. Actual object references use reference identity;
same-name table objects from another document are distinct. Known arbitrary
320–329 values and SORTENTSTABLE sort keys remain ordinary stored values.

The live named object dictionary root and managed legacy ownership collections
cannot be erased through this API. Opaque objects inside the selected subtree
also reject erasure. Surviving opaque data is preserved; a matching exposed
handle blocks the operation. References hidden in private strings or binary
payloads cannot be proven absent. Applications that rely on such references
must apply their own schema knowledge before requesting erasure.

## Terminal state and failure behavior

All reference checks, ownership discovery, handle validation and APPID bookkeeping
checks finish before changing an alias, attachment or registration. Rejected
operations leave the document state and allocation seed unchanged. Ownership
traversal is iterative, so a deep ownership chain does not consume recursive
call-stack space.

On success, each removed object's `IsErased` becomes `true` and `Database`
becomes `null`. Original `Handle` values, payloads, XData, reactors and ownership
links entirely inside the erased graph remain available for inspection. The
selected root's external `Owner` becomes `null`. The tombstones are not frozen
value objects, but none can be registered, attached or used as clone sources
again. Erasure does not provide a resurrection or reusable-detachment operation.
Repeated erasure throws before making any further change.

The document's handle seed is never lowered or consumed by erasure. New objects
receive fresh identities, including after saving and reloading. Keeping the old
handles on terminal objects prevents retained XRECORD/XData handle strings from
silently acquiring a different identity through reattachment.

Registration removal uses the document's normal `AddedObjects.Remove` lifecycle
to release APPID use counts and document XData handlers. A shared APPID remains
in use until its last live user is removed. Unregistration uses each packet's
actual `ApplicationRegistry.Name`, which also permits erasure after a registry
rename when an older XData dictionary key remains. This does not claim a general
repair of XData dictionary rename/subscription behavior.

`DxfDictionary.Remove(name)` continues to remove one name only. Use
`EraseOwnedTree` when permanent removal of the owned records is intended.

## Qualification

The conformance suite exercises successful and rejected operations, aliases and
extension hosts, orphan repair, incoming references from every listed carrier,
APPID counts and handler release, terminal adoption rejection, numeric handle
spellings, same-name identity decoys, private payload boundaries, deep ownership
and handle allocation through repeated save/load cycles.

Six mandatory producer fixtures under `tests/fixtures/typed-erasure` come from
ezdxf1.4.4. Their generator records raw source hashes, exact record snapshots and
the explicit post-export change used to create mixed350/360 dictionary entries.
Each contains a five-object erasure subtree, shared aliases, common metadata,
XData, an owned extension dictionary and independent surviving data/geometry.

`python tools/verify_typed_erasure.py <artifacts>` requires all twelve profile and
transport outputs. It checks the actual profile/transport, producer provenance,
complete removed identity set, surviving ordered payload/XData/reactors/aliases,
following LINE geometry, shared APPID identity, fresh allocation and absence of
exposed references to erased handles. Every output must pass an independent
ezdxf audit with no errors or repairs. This qualifies stored graph behavior,
without claiming native application deletion or private dependency evaluation.
