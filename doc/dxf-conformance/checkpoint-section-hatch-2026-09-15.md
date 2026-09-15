# SECTION_MANAGER, HATCH and source identity checkpoint

[PR #90](https://github.com/wieslawsoltes/netDxf/pull/90) follows the merged
[recovery batch](checkpoint-recovery-2026-09-15.md). Its published implementation
is `a0b9fdb350988c456f9717dfe89dce62ffb77109`, with tree `83299c4ff79430bd4232ff258345e751a88f1c7b`. The local
implementation `98541734c6d21145e016518510be340ceb88bc90` has the same complete Git tree.
The [qualification receipt](section-hatch-qualification.json) records both
configurations, runtime dependency hashes, independent reviews and source checks.

## Resulting behavior

- [SECTION_MANAGER](section-manager.md) retains the stored update flag and ordered
  SECTION identities, preserving qualified public spelling, root ownership and
  source profile. Private forms stay opaque; unsupported editing, cloning,
  erasure and conversion fail through explicit validation.
- [HATCH source relations](hatch-source-relations.md) retain ordered boundary
  references and duplicate uses. Sources must be retained entities in the same
  block. Adoption validates before changing handles, membership or reactors;
  distinct paths may share entities, while each mutable path has one owner.
  Final unlink/removal clears loaded persistent backlinks.
- [Source identity](source-reference-identity.md) requires one physical common
  declaration. Discarded records cannot lend duplicate identities to retained
  targets. Eager validation also protects unreferenced entities from silent
  metadata loss. Nested private control groups cannot become common handles,
  reactors or extension-dictionary references.

Independent review preserved concrete before/after failures for case-insensitive
manager lookup, malformed entity identity, partial HATCH adoption, shared mutable
paths, stale backlinks, ambiguous declarations and nested private controls.
The [source ambiguity receipt](receipts/source-ambiguity-independent/README.md)
and [metadata-loss receipt](receipts/source-identity-metadata/README.md) complement
the separate module receipts and the full integrated suite.

## Qualification

| Check | Result |
| --- | --- |
| Full .NET 8 Debug | 27,555 unique cases; zero failures |
| Full .NET 8 Release | 27,555 unique cases; zero failures |
| Additional cases over PR #89 | 575 |
| Independent verifiers | All 98 in each configuration |
| Generated output per configuration | 3,591 DXFs and 96 LAS files |
| Release compilation | netstandard2.0, net471, net48, net6.0 and net8.0 |
| Additional Debug compilation | netstandard2.0 |
| Build errors | Zero; 561 existing XML-documentation warnings per target |
| Python ledger/runner tests | 18 passed |
| Field audit | Self-test passed; 99 IO files, 702 methods, 250 model/header files |
| Coverage ledger | 272 scoped rows across nine version columns |

Both configurations have result SHA-256
`98d761fba236442457f0469d3fe99f5bf2d7e30069abca06b6d6cbab0c8c6e94`. The
[implementation CI](https://github.com/wieslawsoltes/netDxf/actions/runs/34963947082)
passed all four Windows/Linux Debug/Release jobs; Linux Release also ran every
independent verifier. Final documentation-head CI is required before merge.

## Qualification boundaries

Native manager evidence covers one unchanged R2018 drawing; R2007+ alternatives
are separately identified synthetic schema cases. HATCH evidence uses exact
packets from six pinned native originals and disclosed independent producer
carriers. Stored packet fidelity does not establish native CAD execution,
rendering, regeneration or production-scale interoperability.

Full DXF support remains incomplete. Editable manager membership, automatic HATCH
geometry updates, periodic/affine geometry, editable TABLE and association graphs,
modern modeler data, historical typed formats and the other limits in the current
coverage ledger remain open. POLYFACE and polygon-mesh integrity work is tracked
in a separate increment.
