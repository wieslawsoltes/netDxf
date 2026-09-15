# PolygonMesh qualification receipts

The frozen production/test change is `13a937b`; `qualification.json` pins full
source snapshot and worktree-base commit IDs, assembly hashes, commands, and
outcome counts. The separate reviewer receipt distinguishes captured binary
provenance from assembly informational-version metadata; these binaries were
built while the source change was pending, before its final commit.

| Check | Debug | Release |
| --- | ---: | ---: |
| Focused module tests | 727 passed, 0 failed | 727 passed, 0 failed |
| Independently parsed required output documents | 116 | 116 |
| Actual corrupted ASCII/binary documents rejected by the gate | 12 | 12 |
| Native fourteen-packet provenance chains | 2 | 2 |
| ezdxf audit errors / repairs | 0 / 0 | 0 / 0 |
| Frozen external probe after the change | 160 passed, 0 failed | 160 passed, 0 failed |
| Frozen external probe before the change | 64 passed, 96 failed | 64 passed, 96 failed |

The netstandard2.0 Release API build also completed with zero errors and the 561
existing CS1591 documentation warnings. This is focused local qualification;
merged compatibility suites and platform CI belong to the integration receipt.

`Debug` and `Release` contain complete named test outcomes, console logs, and
independent gate receipts with a hash for every required output.
`independent-review` contains the separate reviewer's frozen probe source,
runner, exact input archive, native inventory, before/after results, and scope
qualifications. The probe source and binary hashes were unchanged across the
four final before/after runs. Its earlier exception-taxonomy correction is
explained in its receipt and was applied before rerunning both baseline and
candidate assemblies.

The verification-only follow-up also checks each output's declared DXF version.
Both output gates were rerun after that assertion was added. There are no further
production or conformance-test changes after the frozen source commit.

See `doc/dxf-conformance/POLYGONMESH_CARDINALITY.md` for the admission profile,
control/sample distinction, model range bound, and native evidence exclusions.
