# Structural CELLSTYLEMAP authoring and portable definitions

This increment adds public construction and replacement of the qualified
CELLSTYLEMAP grammar. It builds on [nested formatting edits](cell-style-format-editing.md),
not an opaque tag rewrite. A caller can create a map, insert/remove/reorder its
entries through complete structural replacement, change frame presence and
ordered grid definitions, and transfer public definitions into another document
with explicit resource mapping. The existing entry, name and formatting APIs
continue to work on the resulting snapshots.

## API and supported grammar

`DxfCellStyleMapEntryDefinition` contains a signed stored identifier/type, decoded
name and immutable `DxfCellStyleFormatDefinition`. The format definition contains
stored TABLEFORMAT/CONTENTFORMAT values, explicit data and margin presence,
optional STYLE, and zero through six `DxfCellGridFormatDefinition` objects.
A grid contains a nonzero stored mask, immutable scalar values and optional
LTYPE. Duplicate identifiers, names and nonzero masks retain their order. These
are storage definitions; no fixed data/title/header role or edge uniqueness is
inferred from them.

The grammar and field labels are the same as the preceding nested-format module:
five [pinned source drawings](../../tools/table_oracle/fixtures.json) and the
[LibreDWG wire specification at a fixed commit](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/src/dwg2.spec).
The implementation is original. Present data requires table/content values;
zero data flags require all data, margins, grids and references absent. Margin
flags must agree with margin presence. Grid enumeration is copied, limited to
six and disposed before a definition is published. Floating-point and text
validation reuse the existing value objects. Flags and numeric codes retain
stored values rather than guessed rendering semantics.

```csharp
// Author an explicit empty format. This does not create a TABLE or TABLESTYLE.
var entry = new DxfCellStyleMapEntryDefinition(
    id: 1, storedType: 0, name: "Custom",
    format: new DxfCellStyleFormatDefinition(storedType: 5));

DxfStoredCellStyleMap map = document.Objects.CreateCellStyleMap(
    document.Objects.Root, "MY_CELL_STYLES", new[] { entry });

// Structural replacement can change count/order/IDs and data/frame presence.
map.ReplaceStructure(new[] { entry, entry });
```

An authored map has a hard-owner dictionary entry and its dictionary as a
persistent reactor. The owner must already be a registered dictionary in the
same document. Duplicate/managed names, a conflicting CELLSTYLEMAP CLASS,
invalid graphs, unsupported profiles and exhausted allocation fail before a map
is registered. A successful ordinary creation allocates one map handle and
registers one object; it does not create STYLE/LTYPE resources. The writer retains
compatible CLASS metadata or supplies a missing class and computes its count.
The known TABLESTYLE extension-dictionary map slot updates the parent's typed
map reference using the dictionary's case-insensitive name rules.

R2004, R2007, R2010, R2013 and R2018 are admitted target profiles. R2000 rejects
public authoring. Historical raw profiles do not acquire a typed authoring API.
Creation under an arbitrary dictionary is public object storage, not proof that
an application will treat that dictionary as its active table-style catalog.

## Snapshot replacement and validation

`ReplaceStructure` accepts a complete list of definitions. It changes entry
count/order, identifiers/types, data/margin frame presence, grid count/order and
stored masks in addition to values. Common metadata, XData, identity, source
owner and source version remain fixed. Removed resource uses are released;
remaining repeated uses retain their order and removal guards. Earlier payload,
entry/format and reference-membership snapshots remain unchanged. Old entry or
grid edits cannot silently target replacement snapshots.

The method refuses a map with any unqualified existing format. It does not
silently discard private or extended fields. Export through `FromEntry`/`FromFormat`
also requires complete projection, and rejects nonzero wrong-kind resource
handles rather than converting them into null references. Explicit null resource
slots stay null.

Definitions are fully enumerated and disposed before checking source/target
registration and graph state. All three map replacement APIs share reentry
rejection, including a nested exception caught by the caller. Creation has a
separate database-level guard. Callbacks may explicitly add resources before
validation, but independent callback changes to the document are not rolled
back by a rejected operation. No callbacks run during the final state swap or
internal map registration. Concurrent-thread mutation and recovery from process
termination or memory exhaustion are not transactional guarantees.

Entry enumeration is bounded by `MaximumAuthoredEntries` (65,536), with a separate
1,048,576-tag record budget. Full formats can reach the tag budget earlier. Common
metadata/reactors/XData are included when replacing a structure. Decoded and
escaped strings retain the existing 1,048,576-code-unit limits. Literal
backslashes are escaped once; pre-R2007 non-ASCII text is encoded using DXF UTF-16
escapes. Binary/text newline rules remain those of ordinary save.

Structural serialization is canonical for this public grammar. A byte-value-
identical canonical request preserves the current snapshots, including bitwise
negative-zero distinctions. Export and structural reapplication can canonicalize
otherwise equivalent Unicode/handle spellings. This is different from the
existing scoped edit APIs' unchanged-tag preservation contract.

## Explicit cross-document and cross-profile transfer

`DxfCellStyleMapEntryDefinition.FromEntry` exports public entry/format data.
`DxfCellStyleFormatDefinition.RemapResources` returns a new definition using a
caller-supplied STYLE/LTYPE mapping. The callback runs once per distinct non-null
source resource identity within each format. A null result or wrong resource
kind rejects; multiple source resources may deliberately map to one target.
Destination membership is checked when the definition is applied. Same-name
foreign resources are not imported or rebound implicitly.

```csharp
var definition = DxfCellStyleMapEntryDefinition.FromEntry(sourceMap.Entries[0]);
var transferredFormat = definition.Format.RemapResources(resource =>
{
    if (resource is TextStyle)
        return destination.TextStyles["MappedStyle"];
    return destination.Linetypes["MappedLinetype"];
});
var transferredEntry = new DxfCellStyleMapEntryDefinition(
    definition.Id, definition.StoredType, definition.Name, transferredFormat);
var transferredMap = destination.Objects.CreateCellStyleMap(
    destination.Objects.Root, "TRANSFERRED_CELL_STYLES", new[] { transferredEntry });
```

The new map serializes using the destination profile, with target handles and
profile-appropriate string encoding. The source document is unchanged. This is
conversion of a declared public format definition, not mutation of a loaded
map's `SourceVersion`, not arbitrary DXF document conversion, and not recursive
resource/owner-graph import. Font resources, linetype dependencies, common map
metadata, reactors and TABLE consumers are not copied by definition export.
Resource objects referenced by definitions remain mutable; snapshots freeze
membership and values, not the future lifetime or name of the resource itself.

## Verification

`map-structure/` tests cover six authoring shapes in each admitted profile and
both transports, all 25 native-source-family/target-profile combinations in both
transports, and four structural operations on each native-family map. Native
source loading uses two whole original drawings and three explicitly documented
[complete carriers](../../tests/fixtures/table-content/manifest.json). It does
not represent all five inputs as whole unmodified originals.

Boundary tests cover constructor limits, bounded/disposed iterators, cross-API
reentry, stale snapshot rejection, unsupported formats, wrong-kind export,
resource registration changes during callbacks, taken dictionary slots,
case-insensitive parent binding, allocation exhaustion, metadata preservation,
null resources, mask order and multiple authored CLASS instances. Existing
editing/lifecycle tests remain enabled.

```sh
DXF_TEST_FILTER=map-structure/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_cell_map_structure.py artifacts/conformance
```

The independent ezdxf verifier requires exactly 190 files: 60 authored maps,
50 transfers and 40 native before/after pairs. It derives expected authored
packets separately, checks each pinned native source's SHA-256 digest, remaps
only qualified resource fields for transfer, and calculates structural native
edits from independently parsed whole-entry frames. It validates created owner
links, physical identities, resource existence, transport/profile and general
DXF audits. For native restructuring it compares all ordered physical records,
not just the edited map. Only the existing writer's verified empty
ACAD_LAYERSTATES dictionary identity is normalized. HEADER time/seed fields and
CLASS records are outside that physical-record comparator and have separate
API/writer tests.

Against the initial implementation artifact, the gate passes 150 checks and
rejects 38,290 actual-output corruptions, including common identities and owner
links, missing resources, all map payload fields, and unrelated physical-record
changes. All 110 created/transferred files independently audit with no errors or
repairs. These are storage/graph checks, not native AutoCAD rendering results.
The initial implementation head `18c8b03e78880e49192ea30ad788bd2681380f9e`
passed 36,315 unique Linux Debug cases, including 425 additions. The subsequent
boundary tests and parent-cache fix require final-head CI; final qualification
and artifact hashes are recorded in PR #100 rather than substituted by this
implementation-stage evidence.

## Consumer consistency and remaining compliance work

Structural editing is explicit and does not interpret application references to
entry identifiers or synchronize duplicate TABLESTYLE/TABLECONTENT/TABLE data.
Deleting an entry or changing its role/identifier may require corresponding
consumer edits by the caller. Stored-only tests proving other records unchanged
do not prove the resulting table has the intended native appearance. Automatic
cross-object formatting synchronization and TABLE layout/regeneration remain
separate, unfinished capabilities.

The aggregate [297-row matrix](version-feature-matrix.md) remains pinned to PR95.
Later [row settings](table-row-settings.md), [nested formatting](cell-style-format-editing.md)
and this authoring/transfer contract are scoped additions, not a promotion of
broad families to complete. Private schemas, general document-version conversion,
recursive dependency import, full color semantics, format-expression evaluation
and native AutoCAD execution remain unfinished. Generic stored-map clone and
erasure still reject under their prior schema guards; emptying a map's entry
structure does not disable those guards.
