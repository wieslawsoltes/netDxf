# DWG hatch semantic transfer

Inventory/count equality is not sufficient to validate an exchange. An additional
field audit of the first passing browser file found that hatch island style
changed from Outer to Normal, and its seed list gained (0,0). Geometry and all
old tests still passed. These are now explicit data-preservation gates.

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
