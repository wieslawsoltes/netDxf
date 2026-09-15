# Polyface retained-record qualification receipt

This receipt qualifies production commit `133747c78d4477ebff865e15c757e07265ac062c`
with the test-only snapshot correction in `c96324c`. The production file hashes
are recorded separately so a later integration commit can establish the same
source content without relying on commit ancestry or assembly version strings.

The final conformance run uses `DXF_TEST_FILTER=poly` in Debug and Release.
Each configuration contains 2,578 cases: 568 retained Polyface cases, 345 existing
Polyface grammar cases, 387 retained PolygonMesh cases, 727 existing PolygonMesh
cases, 382 retained Polyline3D cases and 169 Polyline3D topology cases. The six
independent output gates validate 612 drawings per configuration and detect
850 actual corruptions. Output manifests record the exact emitted file hashes;
negative malformed-input fixtures are identified separately in their pinned
fixture manifest.

The separate independent review contributes 60 primary probes and 12 normal
probes per configuration, with 48 audited drawings per configuration and zero
errors or repairs. Its source, results and summary are copied unchanged under
`independent/`. The twelve invalid normal outputs under
`independent/polyface-normal-expanded-before/` are deliberately invalid
**before-fix evidence**, not successful output fixtures: each was accepted by
save/clone/adoption before the fix and rejected by reload. The corrected code
rejects all twelve cases before output or handle allocation.

The normal regression covers both authored and retained Polyface meshes,
including NaN, infinity and zero normals, save, cloning and adoption. It does not
change the inherited general-purpose `EntityObject.Normal` setter. Polyface
operations validate the resulting state explicitly and never repair it silently.
The null face-layer removal fix is also covered by loaded and authored lifecycle
controls.

The final test-only correction snapshots the lazily initialized object database
before its allocation seed. This prevents the assertion setup itself from
allocating the named-object root after recording the seed. It changes no
production behavior.

Production builds use .NET SDK 8.0.408. Each configuration reports the existing
561 CS1591 documentation warnings and no compilation errors. The final
conformance-only rebuild reports no warnings or errors. Transient MSBuild
startup failures in this environment were retried before compilation; only
successful build logs are included. Root integration owns the repository-wide
suite, target-version diagnostic integration and builds of the other target
frameworks.

## Reproduction

Build `tests/netDxf.Conformance/netDxf.Conformance.csproj` in the desired
configuration, run the conformance DLL from the repository root with
`DXF_TEST_FILTER=poly` and a new `DXF_TEST_ARTIFACTS` directory, then run all six
`verify_*.py` tools listed in `qualification.json` against that directory.
Python gates require ezdxf 1.4.4 and compare raw packets before its higher-level
object projection can normalize child ownership or layer inheritance.

Each independent C# harness is retained as `Program.cs.txt` and
`Probe.csproj.txt`. Copy those two files to a new directory using their ordinary
`.cs` and `.csproj` names, and build with:

```sh
dotnet build Probe.csproj /p:DxfReviewLibrary=/absolute/path/netDxf.netstandard.dll
```

Run the main probe with the repository root and output directory:

```sh
dotnet bin/Debug/net8.0/Probe.dll /absolute/repository /absolute/probe-output
```

The normal probe takes the same first two arguments and a third argument
`fixed` for corrected-library checks. Omitting `fixed` records the pre-fix
behavior and retains any invalid output files. The copied
`audit_probe_outputs.py` script audits a probe output directory. Fixture source
hashes, library hashes, source commits and result hashes remain in the unchanged
independent summary.
