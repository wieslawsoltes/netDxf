# Core build, test, package and release operations

## Exactly two workflows

`ci-build.yml` is the only PR, merge-queue and main-branch CI entry point. It
contains all-target compilation, the complete conformance matrix, package
creation, isolated installed-package tests, the eight packaged-target runtime
profiles, retained runtime evidence, and final source-bound qualification.
It runs on PRs targeting `netstandard`, queue `checks_requested` events targeting
that branch, and pushes to `netstandard`. It has no path filter: changes to
consumers, tests, workflows or documentation still receive the required checks.
Branch-push runs that duplicate a PR run are removed. Manual invocation and
`workflow_call` are retained.

`release.yml` handles version-tag pushes, manually requested rehearsals/drafts,
and publication of already qualified NuGet assets. It calls the same CI workflow
for a tag or manual build. It does not start another PR or queue rehearsal: the
CI workflow already produces the complete `qualified-release` artifact.

The standalone `dxf-conformance.yml` and `nuget-publish.yml` files are removed.
Their behavior is relocated, not bypassed: the entire conformance job body is
retained verbatim, and the NuGet steps retain tag/source/checksum verification,
protected environment, existing opt-in and duplicate-version failure behavior.
No production regression test or independent verifier is removed for cleanup.
Historical workflow names can remain visible in GitHub Actions history; deleting
a YAML file does not erase prior runs. This change does not alter repository
rulesets: administrators requiring old workflow-prefixed check names must select
the corresponding consolidated checks rather than disable test requirements.

## Build and qualification

All five existing targets (`netstandard2.0`, `net471`, `net48`, `net6.0`, `net8.0`)
compile on Linux and Windows in Debug and Release. The complete DXF conformance
suite executes on .NET 8 in those four configurations, including every independent
reader and audit stage. Its filter remains explicitly empty. Framework compilation
on Linux does not claim execution there.

Package tests restore from an isolated candidate-only feed/cache, without a
project reference. The eight explicit packaged-target profiles check their actual
loaded DLL against the candidate archive. Framework profiles run on the installed
compatible Windows CLR, not separate historical CLR installations. The ordinary
consumer also checks normal NuGet target selection.

The `qualify` job requires successful conformance and runtime-evidence jobs; the
latter depends on the package and all-target build chain. It downloads the same
reports and invokes the unchanged source/checksum/runtime validators. Neither
failure suppression nor `always()` permits qualification after a failed gate.
Runtime reports remain sealed in the strict 25-entry `runtime-evidence.zip`.

The CI workflow is read-only and has no publication credentials. External actions
remain commit-pinned. In-progress cancellation is enabled only for PR/queue CI; it is disabled for
tagged and manual release runs. GitHub's default concurrency policy still allows
only one pending run per group, so a newer request can replace an older pending
request. This configuration does not promise an unlimited FIFO release queue.
The reusable workflow's concurrency namespace is distinct from its caller's.

## Release draft or nonpublishing rehearsal

Merge and qualify the intended source before creating a new immutable `vVERSION`
tag, such as `v3.0.2`. Tag pushes validate version spelling, tag target and
`netstandard` ancestry, rerun core CI on that exact source, and then create a
**draft** GitHub release behind the `release` environment. Its assets include the
package, portable symbols, source archive, source-bound test and runtime evidence,
release notes and checksums. Existing assets/releases are not overwritten.

For a rehearsal, run **Release** with `dry_run=true` and `publish_nuget=false`
(the defaults). A branch run uses a CI prerelease; a tag run uses the selected tag
version. No write step runs. To create a draft manually, select an existing
release tag and set `dry_run=false`, leaving `publish_nuget=false`.

## Optional NuGet publication without rebuilding

The `publish-nuget` job requires repository variable `NUGET_PUBLISH_ENABLED=true`.
It runs on a `release: published` event or a manual Release invocation with
`publish_nuget=true`. These publication-only events skip the planner/build/draft
chain. They fetch already published release assets without creating new bytes.
Draft releases and branch refs reject. Exact source, live tag, complete qualified
receipt, retained runtime evidence, package, symbols and checksums are revalidated
immediately before any push. A live-tag check is not an atomic remote lock.

For manual validation of existing assets, select their tag, set
`publish_nuget=true` and retain `dry_run=true`; the actual push is skipped. A
manual push additionally requires `dry_run=false`. A published-release event is
itself an explicit publication request, still subject to the repository opt-in
and `nuget` environment. The default setup publishes nothing merely by merging.

Maintainers configure required reviewers/tag restrictions for `release` and
`nuget`, plus a scoped `NUGET_API_KEY` secret in `nuget`. Environment names in YAML
do not install those protections. No credentials, repository settings or
permissions are escalated by this cleanup. The API key is available only to the
actual push step. A partial package/symbol push is a failure needing inspection;
duplicate versions are never silently accepted.

## Validation

```sh
python -m unittest discover -s tests/ci_pipeline -p 'test_*.py'
dotnet restore netDxf/netDxf.csproj
dotnet build netDxf/netDxf.csproj -c Release --no-restore
dotnet run --project tests/netDxf.Conformance -c Release
python tools/run_independent_verifiers.py artifacts/conformance
```

Workflow tests verify the exact two-file inventory, verbatim moved conformance
job, acyclic dependency graph, event routing, real publication-gate expressions,
manual dry-run exclusion, credentials and pins. Existing release-planner CLI tests
continue to run in real disposable Git repositories. YAML checks and local tests
are not proof of a hosted queue/runtime/release execution. Pipeline cleanup does
not establish native AutoCAD acceptance or full all-version parity.

References: [GitHub workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows),
[reusable workflows](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows),
[workflow syntax and permissions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax).
