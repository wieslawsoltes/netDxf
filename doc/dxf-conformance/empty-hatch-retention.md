# Empty HATCH input retention and safe typed export

> Merged as [PR #73](https://github.com/wieslawsoltes/netDxf/pull/73). Final-head [CI 34865164429](https://github.com/wieslawsoltes/netDxf/actions/runs/34865164429) passed Linux/Windows Debug/Release, actual netstandard2.0 builds, ledger checks and the Linux source audit; the retained Linux Debug report has **16,507 passed / zero failed**. The evidence section below retains the original standalone local experiment, not the later accumulated suite count. See the [merged execution checkpoint](checkpoint-merged-2026-09-14.md) for source hashes and qualification limits.

## No silent entity loss

A HATCH with an absent/zero boundary count and no path records is now retained by
`DxfDocument.Load`: identity, common state, fill pattern/gradient, seeds, elevation,
XData and following entities are preserved. No synthetic boundary is invented.
Clones retain that authored state independently. The reader's malformed-count,
gradient-packet, finite-value and physical-EOF checks remain active.

Typed export remains deliberately conservative: a HATCH must have at least one
boundary. Empty HATCH output rejects before document preprocessing or destination
stream writes, across all registered model/paper/nested/unreferenced blocks. This
replaces the previous Debug assertion/Release silent omission. Callers must add a
boundary, explicitly remove the entity, or use the separate raw API for original-data
preservation. This is NOT a claim that a zero-boundary HATCH renders meaningfully or
is valid in every AutoCAD version. It is typed read retention plus explicit rejection,
not full typed empty-HATCH round-trip support.

## All six typed profiles

AC1015, AC1018, AC1021, AC1024, AC1027 and AC1032 behave identically in text and binary.
Historical typed admission is unchanged; raw preservation remains separate. The old
file-path Save wrapper may truncate its destination before preflight. Use the separate
SaveAtomic feature when preserving an existing file across export rejection matters.
Public existing Debug exceptions / Release false returns remain unchanged.

## Executed regression evidence

48 independently encoded input cases and 144 export-preflight cases were added.
48 existing zero/absent count assertions were deliberately changed from expecting
silent deletion to expecting retention; the other valid/malformed framing and EOF
controls were not relaxed. Focused unchanged-production read/framing runs: **360 pass /
96 fail**, both configurations. The unchanged Release export suite: **0 pass /144 fail**.
The old Debug export branch contains a terminating Debug.Assert and was not executed
as a purported complete red run. Corrected focused suite: **600 pass /0 fail**.

Full signed-library .NET 8 suite: **15,784 passed /0 failed**, Debug and Release.
Repaired fixtures gain an explicit circle boundary and successfully save/reload.
A raw load/save still retains the original empty HATCH bytes exactly. No clean
independent-reader AUDIT for an intentionally empty HATCH is claimed.

Base: merged PR #68, `cf533ba32d6c732e475192ee021b78f938eda0cb`.
The original local experiment did not execute Windows/SDK/netstandard CI. The merged PR subsequently passed those gates as recorded above. Native AutoCAD remains unexecuted.
Geometric closure/containment, associative dependencies and arbitrary affine plane
handling remain separate. Autodesk HATCH group91 is the path-count field; this
increment does not infer extra historical legality from accepting its absence/zero.
