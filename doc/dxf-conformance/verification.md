# Verification and evidence

The [CI workflow](../../.github/workflows/ci-build.yml) is the source of truth for
SDK versions and required checks. The [release guide](../CI-RELEASE.md) describes
the full build, package and runtime matrices. Python tools are development
dependencies, not core library runtime dependencies.

## Run from the repository root

Use a Python environment with the pinned independent-reader dependency:

```sh
python -m pip install -r tools/requirements-independent.txt
python -m unittest discover -s tests/ci_pipeline -p 'test_*.py'
python -m unittest discover -s tests/independent_verifiers -p 'test_*.py'
python -m unittest discover -s tests/standard_fields -p 'test_*.py'
python -m unittest discover -s tests/field_hosts -p 'test_*.py'
dotnet run --project tests/netDxf.Conformance -c Release
python tools/run_independent_verifiers.py artifacts/conformance
python tools/verify_repository_hygiene.py
```

The conformance harness is an executable, so invoke it with `dotnet run`.
CI runs it in both Debug and Release on Linux and Windows. Independent verifiers
consume the generated drawings and manifests; run the complete conformance suite
before the full verifier runner. The runner discovers `tools/verify_*.py` and
fails on missing inputs, timeouts or unsuccessful checks.

For focused development, `DXF_TEST_FILTER` selects a test-name prefix and
`DXF_TEST_ARTIFACTS` selects the output directory. Clear the filter for full
qualification. A focused run does not produce every fixture required by the
full independent runner. On macOS, use a canonical temporary directory if path
tests encounter the `/var` versus `/private/var` alias.

## What each check establishes

| Evidence | Scope |
|---|---|
| Conformance cases | The tested library API behavior and failure policy |
| Same-library save/load | Internal consistency for those inputs |
| Independent DXF readers and numeric/packet oracles | The specified exported-wire and interpretation checks |
| Verifier self-tests | That a checker rejects its constructed corruptions; not an additional library execution |
| Installed-package tests | Actual candidate assemblies and selected scenarios on the recorded runtime |
| Source hygiene | Documentation links and workflow inventory; not CAD behavior |

Compilation for a framework is not execution of the full suite on that runtime.
Stored bytes and passing checks do not certify native appearance, private
application semantics or full AutoCAD parity. Native AutoCAD execution is not a
requirement of the repository's current CI acceptance scope.

## Keep evidence with the run

Use the CI report tied to the exact tested commit for current counts and results.
Keep failures, independent-reader reports and package identities together with
the run; do not copy their output into documentation as a new permanent report.
The old coverage ledger was a source-pinned historical audit, not live coverage;
it and its generator are available through [history](history.md).

Generated fixtures and reports belong in ignored `artifacts/`; compiler outputs
belong in project `bin/` and `obj/`. Remove those outputs and Python caches after
validation when no longer needed. Preserve the checked-in inputs, manifests and
licensing information under [tests/fixtures](../../tests/fixtures). A fixture's
stored/inflated hashes and producer provenance are part of reproducibility, not
disposable build output.
