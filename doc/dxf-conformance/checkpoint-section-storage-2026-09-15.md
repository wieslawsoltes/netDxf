# SECTION, style, field and sun storage checkpoint

PR #88 adds stored SECTION geometry and settings ownership, immutable TABLESTYLE
and FIELD packets, and typed SUN values with reciprocal registered view ownership.
It also corrects internal metadata callback behavior, TABLE resource spelling,
packed transparency retention and standalone LAS round trips. These are bounded
storage and lifecycle capabilities; they do not establish complete DXF support.

## Qualified source and execution

The published implementation is commit
`f9bf71739e7bd60d1e448848921ef153ada47294`, tree
`a64cc268cd1202b44b99f1c96e342bd8db900012`, based on the PR #87 merge
`ce3f1bf9394fa3b3192a68d59f203a41cd85335c`. Documentation and qualification
receipts follow that implementation pin. Module receipts identify their earlier
local focused-test snapshots; the combined [qualification receipt](section-storage-qualification.json)
is the authoritative record for the integrated source.

Both local net8.0 configurations pass **25,408 unique tests with zero failures**.
Their complete results JSON files are identical, SHA-256
`58e7025cc265f4e9cadd5b2677a642f7f5f5561a8c393803f2c86b267ff29945`.
Implementation CI run
[34943862740](https://github.com/wieslawsoltes/netDxf/actions/runs/34943862740)
also passes all 25,408 cases on Linux and Windows in Debug and Release. The six
existing platform-specific Infinity labels are normalized only when comparing
cross-platform case names. Linux Release CI passes all 86 independent gates.

| New regression group since PR #87 | Cases |
| --- | ---: |
| SECTION geometry, lifecycle and native/producer packets | 215 |
| SECTIONSETTINGS bundles and references | 161 |
| TABLESTYLE storage and boundaries | 89 |
| TABLE resource name spelling | 4 |
| SUN values and registered host ownership | 300 |
| Stored FIELD graphs and rejection boundaries | 76 |
| Packed transparency, ancillary XData, transfers and LAS | 653 |
| Internal metadata copy callbacks | 13 |
| Mixed SECTION/TABLESTYLE/SUN/FIELD graphs | 18 |
| **Total new cases** | **1,529** |

Each local configuration emits 3,145 DXF files and 96 LAS files. All 86 mandatory
independent scripts pass in both configurations, with open-file tracing recording
3,044 distinct DXF outputs actually inspected. The transparency gate separately
requires 456 DXF and 96 LAS files and rejects 4,920 corruptions of parsed output.
The mixed-family gate checks 42 outputs, eight exact native bodies and six
corruption controls. Script counts, output counts, corruption controls and API
test counts describe different checks and are not added together as a support
percentage.

All five signed Release targets compile: netstandard2.0, net471, net48, net6.0
and net8.0. Debug netstandard2.0 also compiles. Every target has zero errors and
the same 561 existing CS1591 documentation warnings. Runtime tests execute on
net8.0; compiling the other targets does not claim execution on every consumer
runtime. All 18 Python ledger/runner tests and the field-audit self-test pass.
The syntax inventory covers 87 IO files, 672 methods and 241 model/header files;
all recorded file hashes match the qualified production files. Syntax indexing
is an audit aid, not a semantic standards certificate.

## Storage and lifecycle contracts

- [SECTION](section.md) retains the documented SECTION and native SECTIONOBJECT
  spellings, finite stored plane values, optional indicator fields, vertex lists
  and reciprocal owned settings. R2007+ graph copying uses explicit external
  maps and commits without public clone callbacks; erasure preflights the owned
  graph and incoming references.
- [SECTIONSETTINGS](section-settings.md) retains ordered type/source/geometry
  bundles, bounded counts and documented/native marker variants. Integer flags,
  resource names and destination filenames remain inert stored values.
- [TABLESTYLE](stored-table-styles.md) retains complete source-bound packets,
  conservative projections, exact STYLE dependencies and opaque CELLSTYLEMAP
  ownership. Editing and map interpretation remain unsupported. Unchanged bound
  resource names preserve their original source spelling.
- [FIELD](stored-fields.md) retains immutable expressions, caches, ordered child
  ownership and exposed dependencies. Source profile conversion, cloning and
  owned-subtree erasure reject conservatively. Release null/false rejection
  results and Debug exceptions are both checked without weakening state or
  output assertions.
- [SUN](sun.md) retains version-one scalar values and reciprocal registered
  VIEW/VPORT/VIEWPORT ownership. VPORT/VIEWPORT require R2007+, named VIEW requires
  R2010+, and retained-only Layout.Viewport Id1 owners are excluded.
- [Transparency](transparency-packed.md) preserves imported packed bits and
  complete ancillary layer XData while retaining the existing effective-value
  API. [Independent checks](transparency-independent-gate.md) cover successful
  and failed edits, assignment, clones, explicit layer-state transfers and LAS
  output. Complete-pair physical EOF is local to the LAS codec; DXF EOF handling
  remains strict.

Full CI exposed a SECTION preflight regression: accessing the lazy OBJECTS
database allocated a handle before an invalid HATCH or output-setting save was
rejected. The validator now inspects registered objects directly. All 157
existing allocation-invariance regressions pass. Independent callback and
transparency reviews likewise retain demonstrated failing controls and their
fixed outcomes; these checks supplement the complete regression suite.

## Native evidence and remaining boundaries

The unchanged LiveSection1 source loads and saves in both transports, and its
SECTION/settings packets are compared exactly. Native SECTIONOBJECT outputs are
audited without adaptation. ezdxf interprets the documented entity spelling
SECTION as a structural delimiter, so those raw packets are checked before a
disclosed in-memory name adapter is used for structural audits.

TABLESTYLE evidence separates five source packet sets, ten scoped object-graph
carriers and four complete-source outputs. Native FIELD packets are exercised
through explicit carriers with disclosed host replacements; the unchanged
R2000 and R2018 originals encounter unrelated OLE2FRAME and modern ACIS limits.
SUN evidence qualifies stored native values and registered ownership, without
solar or rendering evaluation. Synthetic cross-family links are identified as
such. Native AutoCAD open/AUDIT/save/reopen has not been executed.

The [259-row comparison](version-feature-matrix.md) keeps whole-family partial
states separate from the qualified stored subsets. Remaining work includes
DIMASSOC, UCS-record base references, composite TABLE ownership, fuller table
backing schemas, material/rendering and surface families, modern SAB/ACDSDATA,
historical typed grammars, affine/associative geometry and native application
qualification. SECTION_MANAGER, SUNSTUDY, FIELD evaluation and editable
TABLESTYLE/CELLSTYLEMAP semantics remain open. The VPORT frozen-layer assessment
found no qualified positive packet and does not invent an implementation from a
conflicting group-code description.
