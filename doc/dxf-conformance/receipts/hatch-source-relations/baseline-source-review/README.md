# Independent source-closure regression probe

The unchanged `Program.cs` probe loads 96 pinned DXF inputs through the compiled
library: six DXF profiles, ASCII and binary, edge and polyline paths, and four
source-list scenarios. `inputs.tar.gz` preserves every exact input and
`manifest.json` describes each case. Each positive input has one source reference.
The other scenarios replace it with an absent target or a target in a different
block, or set the HATCH non-associative while retaining its source list.

The baseline accepted all 96 files. Each of the 72 unsupported relationships
silently became an empty source list. The corrected library retains the 24
positive relationships and rejects all 72 unsupported relationships. The probe
source and inputs were unchanged between the recorded runs. `before.json` and
`after.json` include exact library SHA-256 values and every observed result.

To rerun, copy this folder into a temporary directory, extract `inputs.tar.gz`,
place the desired `netDxf.netstandard.dll` beside `Probe.csproj`, build the project
with .NET 8 and run it from that directory. The DLL is deliberately not committed.
The portable project file does not change the frozen probe source.

These producer controls establish the regression boundary; native evidence and
the broader source identity/lifecycle qualification are recorded separately in
the parent module receipts. They do not execute native CAD software or test
associative geometry regeneration.
