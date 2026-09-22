# Typed document ownership checkpoint

This continuation starts from PR #98 commit `64293d8` and preserves its pinned
C# reference, original tests and shared fixtures. No previous uncommitted typed
document patch survived; this implementation is reconstructed from the actual
pinned sources. It is a new in-memory registration engine, not recovered bytes.

## Implemented core

`DxfDocument` now initializes its original default tables, active viewport, layer
state manager, model-space block/layout, classes and raster variables. Handles,
resource canonicalization, references, renaming and document ownership are real
runtime state. The package entries export the original-path collection classes.

Supported detached entities can be added to the current layout, indexed by
handle, moved through explicit removal, and removed while releasing resource and
metadata references. Blocks, INSERT attributes, groups, paper-space layouts,
dimension-generated blocks, layer/text/linetype/style dependencies and XData
registries share the existing typed models instead of replacing them.

The document's named-object database initializes lazily, including the source's
observable initialization during entity/block removal. It supports dictionary
ownership registration, extension dictionaries, reference-handle reservation,
validation and ownership-graph cloning. The original database partials for
specialized containers, erasure, SUN/section/GEODATA/output helpers are not all
implemented at this checkpoint.

Layer-state snapshots, selective restore/update, rename/remove, independent
cloning and portable LAS text adapters are provided. `SupportFolders` and LAS/LIN
file operations use explicit Node hosts. The portable entry has no filesystem
access. File-share/locking guarantees, exhaustive malformed LAS parsing and
Windows device-path behavior need independent qualification; passing in-memory
checks do not establish those filesystem claims.

## Scope boundaries

This is **not** complete typed `DxfDocument` parity. Typed DXF reader/writer,
Load/Save/SaveAtomic transport and version-conversion integration are not yet
provided by this new document layer. The existing separate `DxfRawDocument`
transport remains available and unchanged; raw transport does not substitute for
typed document transport.

Stored/private entity families and retained polyline-record adoption are rejected
before list insertion rather than silently losing their backing records.
MULTILEADER, SECTION and ACAD_TABLE registration are not admitted by this initial
core. Native classes not yet ported are not represented by throwing stubs.
The completion/publication gate remains blocked, and PR #98 stays a draft.

## Initial verification

The input-only `document-ownership-corpus.mjs` contains **53 scenarios / 450
operations**. An independent C# reflection controller observes the unchanged
native document, registry order, handles, collection membership, ownership,
reference counts, renames, model/paper space and dimension-block regeneration.
The JavaScript controller calls the actual production APIs. No source output
is embedded in the implementation or used to normalize comparison results.

The initial Release corpus matches all 450 operations. Sixteen new focused
supplemental tests pass. The pre-existing 826 supplemental tests also passed
after the initial document integration; that earlier run is not relabeled as
final-tree evidence after subsequent changes. Fresh whole-suite, expanded
corpus, browser, offline-package and hosted qualification are separate work.

File presence is not exhaustive API/signature or behavioral qualification. No
new original test identity is claimed for the supplemental tests. Existing
numerical, browser and filesystem failures are not waived by this checkpoint.
