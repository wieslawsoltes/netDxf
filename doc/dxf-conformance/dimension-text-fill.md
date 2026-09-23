# Generated dimension text fills

The shared dimension-block renderer now gives its generated MTEXT labels an
indexed background when the effective `TextFillColor` is present. A base color
survives unrelated overrides; a selected replacement wins; an explicitly null
override disables the fill. Empty dictionaries continue to inherit. Generated
backgrounds are independent value snapshots: later edits to a style color,
clone, or another generated label do not change an earlier label's mask.

The renderer touches only its newly created top-level labels, including split
and manually combined text. It never traverses custom arrow blocks. Label
strings, geometry, text height, attachment and other styling are unchanged.
Suppressed text does not create an invisible stand-in label or mask entity.

## Representation and target profile

DIMTFILLCLR supports indexed colors in the existing public API/serializer. The
mask therefore uses the source color's index, including BYBLOCK (0), BYLAYER
(256), and the existing indexed fallback of an RGB AciColor. No new true-color,
color-book, transparency, frame or drawing-window-color mode is invented. The
MTEXT mask uses the existing default scale factor 1.5; exact native mask extents,
font metrics, pixel occlusion and print output are not qualified here.

The existing MTEXT writer conservatively requires R2007+ for backgrounds. Owned
R2000/R2004 dimension generation retains its previous unmasked labels and stored
DIMSTYLE/DSTYLE fill settings. That is an explicit legacy rendering limitation,
not support for a forbidden earlier MTEXT mask. Detached builds have no target
profile and generate masks. Adopting a prebuilt masked block into a legacy
document does not erase it: the unchanged MTEXT writer preflight rejects it.
When a document version changes from modern to legacy, saving an existing mask
likewise rejects. Explicit dimension Update regenerates the legacy unmasked
block; switching back and updating restores the stored fill.

During adoption the destination block may not yet expose its document owner.
The document passes its actual profile through the existing internal builder
entry, and mask projection happens before publishing the generated block. No
temporary public owner assignment, active-layout switch or global context is
used. Public Build signatures, input/output codecs and version gates are unchanged.

## Verification

The focused harness has 483 cases: 192 detached/direct/generic/clone and manual
cases, eight custom-arrow/source-color isolation cases, 282 version/transport/
container cases, and one explicit profile-transition/preflight case. Twelve
variants cover no fill, inheritance, unrelated overrides, replacement, clearing,
indexed/RGB-fallback colors and BYBLOCK/BYLAYER. All eight dimension families use
the shared path; ARC_DIMENSION retains R2004+ eligibility. Legacy/model/paper/
referenced-block adoption, repeated Update, handles, other-application XData,
object graph validation and a following LINE are checked.

The complete 846-drawing corpus has 10,152 dimensions and generated labels.
The independent checker inspects actual style/DSTYLE/mask packets and performs
an ezdxf read and repair-free audit. Packet mutations and missing/extra inventory
controls must reject. Existing baseline assertions and verifiers remain. The
same compiled focused harness is run against the preceding packaged library
and the changed library; executed results belong to the PR and evidence report.

The first new API fixture used an indexer assignment to insert an absent
override, but that indexer only replaces existing values. The fixture now uses
Remove/Add; no production dictionary behavior or assertion was changed. No
failed initial execution is counted as a pass.

The installed-package assertion body tests filled/cleared generation, all six
versions and both transports, so the existing eight package/runtime profiles
exercise this behavior without a project reference. No additional workflow,
credential, release tag or registry publication is introduced.

## Primary references and boundaries

- [Autodesk DIMTFILL](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-LT/files/GUID-4E38E29F-DE85-4791-A2E7-4DC22842B1B4.htm): explicit color/no-fill semantics. Drawing-window color remains outside this API.
- [Autodesk MTEXT group codes](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-5E5DB93B-F8D3-4433-ADF7-E92E250D2BAB.htm): background fields 90, 45, 63 and optional true-color/name/transparency fields.
- [Autodesk background mask](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-LT-MAC/files/GUID-3448A24E-E18B-4C8C-B8AB-84F4CD4EBC81.htm): scale-factor margin definition.

This is generated mask-data support, not native AutoCAD open/AUDIT/save/reopen,
font/fit/leader or viewport equivalence. Historical typed dialects/pre-R11,
complete FIELD/TABLE/private cache regeneration, transaction-wide rollback,
dependency-complete import and general version conversion remain separate work.
