# Build, test and release operations

## Workflows

`ci-build.yml` builds **all five existing library targets** (netstandard2.0,
net471, net48, net6.0, net8.0) in Debug and Release on Linux and Windows.
Framework reference-assembly packages allow compilation on Linux; compiling a
.NET Framework target is not the same as running it on Windows. Existing
compiler documentation warnings are retained, not suppressed. The workflow
then builds a NuGet package and portable-symbol package and runs a separate
net8.0 consumer **from that local package feed**, without a project reference
or a cached package of the same name. The consumer exercises six DXF versions
and both transports, suppression, explicit affix clearing, code-8 alternate
units and graph integrity. Normal CI produces `3.0.1-ci.RUN` packages only as
workflow artifacts; it does not publish them.

The existing `dxf-conformance.yml` retains every regression, independent checker
and audit stage and adds `workflow_call` plus source provenance. It still runs
on the same branches and PRs. Its full-suite filter is explicitly empty. Both
workflows support reuse by releases, have distinct concurrency groups, and do
not cancel running tag builds. Every external action is pinned to the reviewed
commit behind its existing major version; upgrade pins deliberately.

## Release validation and draft creation

1. Merge the intended source and version notes into `netstandard`.
2. Create an immutable new tag such as `v3.0.2` or `v3.1.0-rc.1` at that commit,
   then push that tag. Do not reuse `v3.0.1` or move an existing release tag.
3. `release.yml` validates canonical version spelling, tag identity and ancestry
   on `netstandard`, runs all build/package jobs and the complete conformance
   matrix on that exact source, and requires all independent verifiers.
4. After successful qualification, it creates a **draft GitHub release** with
   the package, symbols, source archive, build metadata, qualification report,
   smoke log, notes and SHA-256 checksums. Review the draft before publishing.

No tag or release is created merely by merging these workflows. Creating a
public release version is a separate maintainer action. Existing releases and
assets are never overwritten or silently skipped. If draft creation already
succeeded, do not rerun it as a way to publish NuGet; use the separate publication
workflow described below. A failed upload requires inspection of the draft
before retrying; no blanket `--clobber` or duplicate-success policy is used.

Pipeline-changing pull requests automatically run the complete Release flow in
nonpublishing mode, including both reusable matrices and the final artifact gate.
PR events cannot pass the tag-publication plan.

For a nonpublishing rehearsal, manually run **Release** with `dry_run=true`
(the default). A branch run builds a CI prerelease; a tag run uses its version.
The same qualification executes and produces `qualified-release` artifacts,
while all write jobs are skipped. Manual `dry_run=false` requires selecting an
existing release tag. All dynamic inputs pass through environment variables and
strict validation, not direct interpolation into shell commands.

## Optional NuGet publication

`nuget-publish.yml` is deliberately disabled until the repository variable
`NUGET_PUBLISH_ENABLED` equals `true`. It runs when a GitHub release is published,
or manually when the operator selects the release tag. It refuses drafts and
branch refs, downloads the **already qualified release assets**, verifies their
checksums, exact source, tag/version equality, full configuration receipt, package
identity, five-framework inventory and portable
symbols, then pushes the package and its accompanying symbols. It does not
rebuild different bytes in a credential-bearing job.

Before enabling publication, maintainers must configure the `nuget` environment
with required reviewers and an appropriately scoped `NUGET_API_KEY` secret.
Configure required reviewers/tag restrictions for the `release` environment as
well. An environment name in YAML does **not** install these protections. These
repository settings and credentials are not changed by this implementation.
Build/test jobs use read-only permissions and no publishing secrets; only the
GitHub draft-creation job receives contents:write. NuGet secrets exist only in
the publication step. A partially successful package/symbol push is reported as
failure and needs inspection; duplicate versions are not automatically accepted.

## Local validation

```sh
python -m unittest discover -s tests/ci_pipeline -p 'test_*.py'
dotnet restore netDxf/netDxf.csproj
dotnet build netDxf/netDxf.csproj -c Release --no-restore
# Use the exact packaging/consumer commands in ci-build.yml for an offline-feed smoke test.
```

Build/package results do not establish full AutoCAD parity, native application
acceptance, native font/visual equivalence or test execution on every library
runtime. Conformance scope remains documented in `doc/dxf-conformance`.
The project version is not bumped automatically, and JavaScript draft work is
not merged or published by these pipelines.

References: [reusable workflows](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows),
[workflow permissions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax),
[NuGet symbol packages](https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg).
