# Retained source identity and common metadata review

The final integration audit found a silent-loss path after strict source lookup
was introduced. `ImportDatabaseObjects` resolved each common-metadata owner with
`GetObjectBySourceHandle`, then skipped the metadata when that lookup returned
null. A retained LINE whose physical handle was also declared by a discarded
unknown entity therefore lost its persistent-reactor list. No other object had
to reference the LINE for this loss to occur.

The unchanged standalone `Program.cs` reproduces eight cases: ASCII/binary,
the discarded declaration before/after the retained LINE, and a LINE with/without
common reactor metadata. All eight loaded in both baseline configurations. In
each configuration the four reactor-bearing cases silently changed from one
persistent reactor to zero. The other four cases showed that unreferenced
retained entities with ambiguous declarations were still admitted.

Fix `a4332d209a0f1eb5d995131d56fe659da4092173` validates the complete accepted
source-record map before applying common metadata, including before the
object-free early return. Ambiguous accepted declarations now reject even when
no separate relationship forces a source lookup. The per-lookup strict identity
check remains in place. The rechecked integration also contains nested database
control-group fix `45358eaec23fd83e8eabdeafc12d882d5f0bc853`.

The frozen probe SHA-256 is
`2e466169a5d0700ec497ce04aa5c990119556c8085acfb6f87cd3832fd7a86f1`.
`before-debug.json`, `before-release.json`, `after-debug.json` and
`after-release.json` preserve each observed result and exact library hash.
Both corrected configurations reject all eight cases. Debug reports the
ambiguous physical identity through `FormatException`; Release returns null.
`before-inputs.tar.gz` and `input-sha256.json` retain the eight baseline input
files as additional evidence. The probe regenerates its graph and mutations on
each run; the source itself is unchanged across the recorded executions.

Commit `9d6f99b` adds eight corresponding negative conformance cases plus two
positive cases that retain the exact registered reactor and LINE geometry
through mixed-transport save/reload cycles. The filter is
`DXF_TEST_FILTER=source-identity-metadata/`. Execution of these ten registered
cases is delegated to the final combined integration build; redundant owner
builds were stopped at the integration owner's request. These receipts claim
the completed standalone rechecks, not a completed owner conformance run.

The `hatch-integration-recheck` directory preserves a second unchanged probe:
all sixteen HATCH adoption, path ownership, ambiguity and backlink outcomes pass
in both final integrated builds. Every observable membership, handle, contour
and reactor state matches the corrected isolated HATCH baseline. Expected
diagnostic differences are recorded separately: duplicate common entity handles
now use the generic Debug error, and Release malformed loads return null.

To rerun either standalone probe, copy its directory to a temporary location,
place the chosen `netDxf.netstandard.dll` beside `Probe.csproj`, build with .NET 8
and execute from that directory. The portable project includes only its own
`Program.cs`; the DLL is not committed. Review and execution did not edit the
integration owner's source worktree.

The ledger audit found 272 unique feature IDs. The HATCH row retains the boundary
between stored source relationships and automatic associative regeneration;
the manager row distinguishes native R2018 evidence from synthetic R2007+
schema cases and retains immutable lifecycle limits. No additional material
source or ledger defect was found within this bounded review. Broad conformance
and native CAD/rendering behavior remain outside these standalone probes.
