# Stored SECTION entities and owned settings

`netDxf.Entities.Section` retains a section plane's public stored values and its
owned geometry-settings object for R2007, R2010, R2013 and R2018, in ASCII and
binary DXF. R2000 and R2004 reject before the section is added or written. This
increment does not generate cuts, evaluate section states, produce destination
files, or regenerate proxy graphics.

The default constructor uses `SECTIONOBJECT`, the spelling in the pinned native
example. `new Section("SECTION")` explicitly selects the spelling in Autodesk's
published reference and IxMilia's independent producer. These are the only
accepted constructor values. `CodeName` retains the selected or loaded spelling
through save and clone; neither reader nor writer silently substitutes it.

## Stored schema

The implementation follows Autodesk's [2009 DXF Reference](https://damassets.autodesk.net/content/dam/autodesk/www/developer-network/platform-technologies/autocad-dxf-archive/acad_dxf_2009.pdf),
printed pages 139–140 and 223–225, and the pinned producer packets described below.

| Public data | DXF field | Stored behavior |
| --- | --- | --- |
| State and flags | 90, 91 | Independent integers; their effects are not evaluated |
| Name | 1 | Explicit Unicode string, including empty text |
| Vertical direction | 10/20/30 | Complete finite vector; no normalization |
| Top and bottom heights | 40, 41 | Independent finite signed values |
| Indicator transparency | 70 | Stored short value |
| `StoredIndicatorColor` | 63 | Nullable documented indicator ACI slot |
| `StoredNativeIndicatorColor` | 62 in `AcDbSection` | Nullable observed native indicator slot, independent of common entity `Color` |
| `IndicatorColorName` | 411 | Optional string; null and explicit empty text remain distinct |
| Vertices and back-line vertices | 92 + 11/21/31; 93 + 12/22/32 | Separate ordered collections; counts derive from the actual collections |
| `GeometrySettings` | 360 | Owned object reference with reciprocal common owner |
| `HasStoredGeometrySettings` | physical 360 presence | Distinguishes absence from an explicitly stored null handle |

Each vertex collection admits at most 1,048,576 vertices. Input also bounds its
payload tags and validates each declared count against both that limit and the
remaining packet. Incomplete triples, duplicate known scalar fields, misplaced
fields, missing mandatory fields, nonfinite coordinates, malformed known packets
and unrecognized entity payload extensions reject. The qualified entity grammar
uses the published scalar order and interleaved complete vertex triples. Text
rejects NUL, line breaks and unpaired UTF-16 surrogates. Numeric state, flags and
indicator values are retained without adding undocumented enum interpretations.

The common entity fields, optional color name/shadow mode, proxy bytes and XData
use the existing common metadata codecs. The parser keeps the native group-62
indicator separate from the common entity group-62 color. Optional indicator
fields are never materialized from defaults. Only exact identity transforms are
accepted; callers editing coordinates must explicitly manage redundant stored
values and the independently retained proxy cache.

## Ownership and lifecycle

The native packet pairs entity `228` group 360 with settings `22A`, whose common
330 owner is `228`. The implementation treats this as ownership. A loaded target
must be an actual retained source settings object with that reciprocal owner.
It cannot resolve to a generated default merely because a handle number matches.
Numeric nonzero handle spellings resolve to the same registered identity; numeric
null spellings remain null in reference processing.

`document.Objects.SetSectionSettings(section, settings)` attaches a detached,
unowned `DxfSectionSettings` to a registered section. It checks the complete
settings admission and reference graph before registration and does not replace
an occupied slot. `SECTIONSETTINGS` and the native `SECTION_SETTINGS` object
spellings retain their own source identity. The settings grammar, finite-value
rules, bounded bundle counts and explicit whole-object private fallback are
qualified separately in [section-settings.md](section-settings.md).

`document.Objects.CloneSection(source, destinationBlock, mappings)` copies the
section and its entire typed owned object graph, including settings, both levels
of extension dictionaries, dictionary aliases, reactors, XData and XRECORD data.
It remaps self references and internal handles on detached copies before committing
the destination. Cross-document external targets need explicit mappings, including
the entity layer and linetype; the source section and containing block map
automatically. The commit registers planned identities and block membership without
invoking application callbacks. Internal metadata copies preserve APPID graph
cycles and independent binary values through plain stored registry copies. Resource
and APPID public `Clone` overrides are not invoked during ownership transactions;
explicit public `Clone` calls retain their existing override behavior. Unknown
opaque owned objects reject before mutation.
The simple `Section.Clone()` accepts independent stored values without owned graphs
or nonnull common reference metadata; callers use the graph API for references.

Ordinary entity or containing-block removal refuses a section that still owns
registered objects. Settings source references and incoming section metadata also
protect their targets from ordinary removal. `document.Objects.EraseSection`
preflights all typed descendants and exposed incoming references, then permanently
erases the section and owned graph. External entities/resources are retained.
Erased section and object handles remain available for inspection, and the erased
instances cannot be adopted or cloned again. Incoming opaque exposed handles block
erasure; data hidden inside private strings or binary packets is not interpreted.
The original native SECTION_MANAGER is retained opaque, so its incoming reference
can legitimately prevent erasing the original section. An independent cloned
section without that incoming manager reference can be erased.

## Evidence and mandatory gates

The original `LiveSection1.dxf` is pinned from
[LibreDWG commit 34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/test/test-data/2018/LiveSection1.dxf).
The complete 146,350-byte source is retained without any carrier transformation.
Its SHA-256 is `2046c85bdbd3ad1b63273c74e3aee20f28e774a50cc6296b064e835c23295183`.
The source and both exported transports audit with zero ezdxf errors or repairs.
The gate compares the complete decoded ordered `AcDbSection` and
`AcDbSectionSettings` bodies, common proxy bytes, original handles, reciprocal
ownership and output CLASS identities/counts. Complete-file loading is exercised;
this does not claim byte identity or semantic qualification for every other family
present in that file.

Run the focused registrations `RegisterSectionTests`,
`RegisterSectionLifecycleTests`, `RegisterSectionProducerTests` and
`RunSectionSettingsTests` in both Debug and
Release, followed by both mandatory gates:

```sh
python tools/verify_section.py <artifact-directory>
python tools/verify_section_settings.py <artifact-directory>
```

The eight independently produced IxMilia 0.8.4 entity packets cover all four
profiles and both source transports. Their original bytes and the producer source
are pinned in `tests/fixtures/section-producer`. An independent ezdxf carrier changes
only identity 1D to F1000 and inserts the absent common model-space owner 330.
Every other common field and the complete ordered `AcDbSection` body remain exact,
including documented indicator 63, color-name 411, both vertex lists and absent 360.
The source common transparency 440 exposed percentage-rounding loss; the bounded
[packed transparency correction](transparency-packed.md) preserves those exact bits.
The gate proves that declared extraction map against each original packet and
requires both output transports from every extraction. These fixtures qualify
exact extracted packets, without claiming whole-original IxMilia file imports.
Regeneration reproduces the same gzip, carrier and manifest bytes across five
independent Python hash seeds.

The SECTION gate requires all 70 output files and all eight pinned producer
extractions. It separately reports 14 unmodified native-name output audits,
56 adapted documented-name output audits and eight adapted source-carrier audits. It verifies independently
stored values, exact optional-field presence, Unicode and protected literal escape
text, owned reference sequences including nulls and duplicates, binary metadata,
clone identity separation, erasure and surviving external LINE geometry. Deliberate
corruptions of copies of actual output packets must be rejected by the same checks.

There is an explicit independent-reader boundary: ezdxf mistakes entity `SECTION`
for a nested structural file section. The gate verifies original raw packets first,
then changes only that entity type and its matching CLASS name in memory for an
adapted structural audit. These adapted audits are reported separately. Native
`SECTIONOBJECT` outputs are audited without an adapter, including the complete
pinned source round trips. Neither category proves geometric cut correctness or
AutoCAD application execution.

The isolated final Debug qualification passed 2,083 focused cases with normal
compiler warning diagnostics, including existing lazy-database preflight and
strict DXF EOF regressions. Both SECTION gates passed on fresh output artifacts;
552 additional transparency artifacts feed its independent mandatory gate.
`section-qualification.json` records source checkpoints, DLL hashes, gate scopes,
independent review outcomes and output hashes. Combined Release and full-suite
qualification are recorded by the integration branch, separately from this
isolated Debug receipt.

The same isolated outputs also pass the [independent transparency gate](transparency-independent-gate.md): all 456 DXF and 96 LAS files, with 4,920 mutations of actual output fields rejected. The exact receipt and artifact hashes are included in `section-qualification.json`.
