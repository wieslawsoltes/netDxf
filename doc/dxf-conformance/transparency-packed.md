# Exact packed transparency values

`Transparency.FromAlphaValue(int)` now retains the supplied 32-bit value in the
read-only nullable `StoredAlphaValue` property. `ToAlphaValue` returns those exact
bits while they remain stored, and `Clone` copies them independently. A successful
`Value` edit clears the stored value and uses the existing percentage encoder;
a rejected edit leaves both values intact. Percentage-authored transparency,
`ByLayer` and `ByBlock` have no stored packed input.

The existing percentage, index flags and equality behavior are preserved. They
are a legacy effective projection and can discard packed alpha precision or
interpret more than one packed value identically. `StoredAlphaValue` provides the
exact input without inventing semantics for unknown flag bits. Two instances can
therefore remain equal under the existing `Equals` contract while retaining
different packed values for export.

The demonstrated source is the eight pinned IxMilia 0.8.4 SECTION producer
packets in `tests/fixtures/section-producer`. Their common group 440 is
`0x02000000`; the earlier percentage conversion exported `0x01000000` instead.
The independent producer gate now requires the exact original group 440.
[ezdxf's transparency documentation](https://ezdxf.readthedocs.io/en/stable/concepts/transparency.html)
and its independently implemented raw alpha conversion describe the distinction
between packed storage and higher-level opacity. The implementation preserves
input bits and retains netDxf's existing effective API.

Focused tests cover all 256 low-byte values under the two observed high-byte
prefixes `0x01` and `0x02`, representative unknown flags, clone isolation, failed
and successful edits, and existing authored defaults. Actual LINE and MTEXT
common group-440 packets and LAYER `AcCmTransparency` XData are saved, loaded,
cloned and edited across R2000 through R2018 in both transports. The R2000 case
checks existing library compatibility behavior; the published DXF introduction of
entity transparency is R2004. Each wire case
includes eight distinct packed values. The layer writer also retains explicitly
imported opaque packed values and rewrites existing transparency XData after a
successful edit to opaque. No rendering or transparency flag interpretation is
introduced by this storage correction.

Boundary qualification also preserves the complete `AcCmTransparency` XData
packet, including ancillary text, binary data and duplicate integer slots. The
reader projects its last 1071 slot; an edit updates only that slot, preserving
all earlier slots and tag order. An untouched packet without 1071 remains
unchanged. An explicit successful percentage edit may append a previously absent
1071, and a failed edit leaves the packet untouched. Layer-state group 440 uses
retained packed bits before the legacy opaque-zero encoding; physically stored
zero remains observable while retaining its existing effective opaque value.
Independent before/after probes detected these boundary losses before release.

Whole-value assignment such as `layer.Transparency = new Transparency(0)` is
also an explicit edit and replaces the projected integer without discarding
ancillary data. Layer cloning copies pending edit state directly, preserving
untouched packets that have no transparency integer. Standalone LAS layer-state
Save/Load follows the same exact packed-value policy as DXF layer-state records.

The two existing carrier conventions for numeric zero remain distinct: a
layer-state zero projects to opaque, while the normal transparency API retains
its legacy ByBlock projection. Layer-state constructor capture, `CopyFrom` and
`CopyTo` preserve that effective meaning when crossing carriers by discarding an
ambiguous raw-zero cache and using the existing authored encoding. Same-carrier
round trips and clones retain raw zero exactly; transfer does not mutate the
source object or change the global `FromAlphaValue` interpretation.

LAS accepts physical end-of-file only after a complete pair, as emitted by its
standalone writer. Its reader supplies a local terminator for this convention;
a dangling group code remains invalid, and DXF readers retain strict explicit
EOF requirements. The mandatory independent transparency gate consumes the
shipping authored, cloned, edited, ancillary, transfer and LAS output artifacts.
