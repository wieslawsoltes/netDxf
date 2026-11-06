# Diagnostic DWG exchange

This project provides an explicit stream-only interchange adapter around pinned
ACadSharp 3.8.0. The native netDxf model and immutable shadow infrastructure
remain the editor's source of truth. No external converter executable, implicit
file-system lookup, or proprietary codec download is performed.

`DrawingExchange.Convert` stages the conversion in detached documents, validates
signatures and quotas, rereads the actual encoded output, compares per-block
entity/definition inventories, and validates a modern DXF transport through the
actual netDxf reader. Any non-informational codec notification is a refusal.
Unknown and unsupported entity families are rejected rather than dropped.

## Output profiles

The adapter declares AC1015, AC1018, AC1024, AC1027 and AC1032. Each profile must
pass the registered native shared-block/geometry round-trip test in CI before
release. AC1021 is not a writer profile of the pinned provider. AC1014 output
is explicitly unavailable: the attempted shared-INSERT round trip reported a
missing block handle on reread. Its refusal is covered by a separate test. This
is not a repaired AC1014 codec or permission to substitute a newer version.
Read admission is separate from the writer list; an ACdddd signature selects
the DWG reader, which still must pass the strict conversion checks.

The chosen DWG profile is retained in the result and in the output signature.
The validation-only DXF transport uses AC1032 independently of that choice.
Native inventory counts both entity collections and attribute definitions, plus
the paper-space metadata VIEWPORT stored separately on `Layout.Viewport` by
netDxf. That viewport is not mistaken for lost model-space drawing geometry.

## Studio integration

Open accepts DXF and DWG; bytes, not filename extensions, choose the decoder.
`SAVEAS` or Output -> Save drawing as exposes DXF and host-declared DWG profiles.
Quick Save and Ctrl+S remain explicit native DXF downloads with `.dxf` names.
The format dialog retains the original drawing receipt while capabilities and
modal input are asynchronous. Stale saves are rejected, not redirected to a
different drawing. Import validates the receipt both before conversion and
before native adoption. Failed encoding does not advance the hub's saved stamp.
No origin, loopback, mutation-header, revision or workspace guard is disabled.

## Limits and boundaries

Defaults are 20 MiB input, 64 MiB output, 100,000 entities, 4,096 blocks and
128 diagnostics. Stream I/O polls cancellation and enforces output size. Third-
party decompression is **not** a hard process-memory or CPU sandbox. This local
single-user integration should not be exposed as an unrestricted public upload
service. No automatic resource/network resolution is added.

Inventory equivalence is not proof of complete geometric, application-dictionary
or proprietary-metadata fidelity. Opaque solids, proxies, images and underlays
need their own preservation contracts. Keep the original file; this is not full
AutoCAD/DWG losslessness or native AutoCAD acceptance certification. A download
is not durable server storage. Existing DXF paths and history remain usable when
an exchange operation is refused.

The native tests retain real DWG/DXF artifacts for each declared profile. The
real-origin Chromium integration covers Save As, signature-based reopening,
independent DXF audit of authored block/hatch geometry and negative HTTP guards.
These are executable qualification gates, not results inferred from this README.
See `THIRD-PARTY-NOTICES.txt` for the codec's license and attribution.

## Hatch semantic transfer

Hatch island style and ordered seeds have explicit preservation checks because
entity counts and visible geometry alone cannot detect changes to these fields.

The pinned ACadSharp 3.8.0 `DxfSectionReaderBase.readHatch` ignores group 75 and
reads the initial elevation XY pair as a seed, before group 98. The adapter
reads the original DXF bytes with netDxf, verifies the full per-block inventory,
and transfers only the original island style and exact seed list into the
codec's detached document. Matching uses owning block plus nonzero numeric
handle, never entity order or guessed defaults. Ambiguous/missing identities
are refusals. No malformed-file recovery or third-party source patch is hidden
inside this operation; no codec/version substitution is performed.

Before writing, after encoded-output reread, and after the final native DXF
transport, the adapter checks hatch identity, island/solid/associative flags,
plane, path count, patterned-fill metadata, and ordered seed coordinates. It
refuses any other divergence instead of repairing it. Coordinates/scalars use
16 binary ULP tolerance; cyclic angle equivalence uses 1e-12 radians. Solid
fills do not require pattern-only fields which DWG does not serialize.

Seed capture has a separate aggregate `MaximumHatchSeedPoints` budget (100,000
by default, maximum 1,000,000), finite-value checks and cancellation polling.
The additional native DXF parsing is confined to detached streams. Existing
byte, entity, block and codec-diagnostic limits and dirty-state guards remain.
Third-party parsing is still not a hard process-memory sandbox.

Thirty native profile/style/source-format cases exercise five declared DWG
writer profiles, all three island policies, ASCII/binary DXF input, model and
named-block hatches, empty/nonempty seeds and three consecutive round trips.
A separate test checks the aggregate seed budget. The real-origin browser's
independent DXF audit now compares authored island style and ordered seeds too.

These targeted semantic gates are not a proof of every boundary curve,
gradient, application dictionary or proprietary attribute. The documented
strict exchange capability boundary and original-file retention guidance
still apply. AC1014 and AC1021 output are not newly enabled by this change.
