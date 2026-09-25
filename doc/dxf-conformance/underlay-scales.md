# UNDERLAY stored scale fidelity

This extends the affine work in #208 to the typed reader, writer, clone and
public scalar API for PDFUNDERLAY, DWFUNDERLAY and DGNUNDERLAY. The earlier
reader removed X/Y signs with Math.Abs, replaced near-zero values with 1 and
ignored group 43. Those repairs changed the geometry of otherwise untouched
references and discarded stored Z data.

## Storage and editing

`Scale` remains a Vector2, preserving the existing public signature. Its X/Y
components now accept every finite, exactly nonzero binary64 value, including
negative and subnormal numbers. Admission is independent of MathHelper.Epsilon.
The uniform-scale constructor remains positive-only and additionally rejects
NaN and infinities. These are explicit admission changes; zero assignments
remain invalid. Rejected assignments preserve all entity state and graphics.

The added `ScaleZ` property stores DXF group 43, defaults to 1 and follows the
same finite/nonzero editing rule. It is an independent stored scalar, not a
third page axis. The existing two-dimensional page geometry and clipping
coordinates do not use it; affine page transforms retain it unchanged. Native
3D/depth interpretation of this value is not asserted.

The reader has a separate restoration path. It preserves all finite scale
values, including positive/negative zero, rather than repairing a collapsed
source page into a different one. Clone copies the stored values directly so
that public admission does not prevent copying retained degenerate data.
Identity and translation preserve these stored values; a nonidentity linear
transform of a collapsed X/Y page still rejects before mutation. Nonfinite
wire values reject. This does not certify zero scales as valid for native CAD
rendering: preservation of finite source data and editing admission differ.

Absent scale fields default individually to 1. The writer emits all three
fields and preserves their values, not absence or source number spelling.
Changed Scale/ScaleZ clears common proxy graphics. Exact no-op assignments of
admitted values preserve absent, empty and populated caches; refused zero or
nonfinite assignments preserve the current cache. Loading, cloning and
unchanged saving retain stored caches. Definitions, clipping, handles,
ownership and unrelated XData are not rewritten by the scalar restoration.

The earlier affine numerical policies remain: the shared plane rank threshold,
1e-12 orthogonality/reconstruction tests, and the existing near-zero affine
output refusal are not relaxed. Scalar storage can retain values smaller than
the affine generator accepts. No general exact/intermediate-overflow guarantee
or configurable native rendering tolerance is introduced.

## Verification design

The new focused suite defines 150 cases: 48 version/transport/container
matrices, 24 scalar API cases, 45 refused assignments, five invalid constructor
cases, 18 physical nonfinite-input cases, eight reflection/Z-retention cases
one epsilon-independent storage case and one all-double-group text-zero spelling case. Executed counts belong in the PR
qualification evidence, not an assumption made from this definition.

Each document matrix contains 171 references: three definition types, three
planes and 19 scalar variants. Variants include all eight ordinary sign
combinations, all-fields and single-field defaults, small finite values,
minimum subnormals, maximum finite values, both zero signs, minimum normals and
adjacent-to-one values. Input scale packets are independently injected into a
valid seed graph with the existing fixture codec; they are not sourced from
the new typed writer. The 48 matrices emit 144 files (source and two resaves),
24,624 total UNDERLAY records, with an additional in-memory save/reload cycle.

Tests require exact binary64 scale bits, input lifetime/bytes, handles, clones,
identity/translation/refusal behavior, clipping, appearance, proxies, XData,
following LINE and graph validation. The independent verifier derives scalar
expectations from specified values/bit patterns, checks physical scalar fields
and omitted-field fixtures, and separately loads the files with ezdxf. It
requires exact file/record inventories, correct ownership/definitions, ordered
clipping/proxy packets and zero database errors/repairs. Deliberately changed
scalar, geometry, appearance, metadata and cache packets must be rejected.

The installed-package shared body checks signed subnormals, ScaleZ, no-op
caches, typed save/load, retained negative zero and invalid assignment without
increasing its existing twelve DXF scenarios. It is reused by the ordinary
consumer and all eight exact-assembly/runtime profiles. The two consolidated
workflows and every earlier test/checker remain unchanged; the independent
runner discovers the new verifier automatically.

## Legacy text-parser correction

Initial hosted package execution on net471 and net48 caught retained negative
zero being lost during the underlay load/clone/identity path. The shared text
codec now restores the leading minus sign when a successfully parsed finite
value is zero. Syntax, embedded-NUL and nonfinite checks still run first; no
malformed token is accepted by this correction. The existing writer already
emits negative zero explicitly. Both typed and raw readers use this codec.

Regression tests cover signed/unsigned zero spellings with exponents and
whitespace, neighboring nonzero controls, every double group in conformance,
and invalid-token refusals. Installed-package execution checks the actual
selected codec assembly as well as the original unchanged underlay round-trip
assertion. This fixes sign retention, not general correctly-rounded decimal
parsing on legacy runtimes. Final-head execution evidence is required.

## Remaining boundaries

Native PDF/DWF/DGN decoding/rendering, native AutoCAD open/AUDIT/save/reopen,
external-file availability, native zero-scale acceptance, source lexical or
optional-field preservation, definition/deep clipping mutations and general
version conversion are not qualified. The six typed profiles do not constitute
support for every historical dialect. FIELD/TABLE/private cache regeneration,
dependency-complete imports and the separate JavaScript full port remain
separate work. This increment does not establish full AutoCAD parity.

Primary references:
- Autodesk UNDERLAY group codes (41/42/43, OCS insertion and normal):
  https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-3EC8FBCC-A85A-4B0B-93CD-C6C785959077.htm
- ezdxf independent underlay model and unscaled clipping coordinates:
  https://ezdxf.readthedocs.io/en/stable/dxfentities/underlay.html
