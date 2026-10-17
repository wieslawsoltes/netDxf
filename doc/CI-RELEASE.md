# Build, package, qualification and release operations

## Release set and shared version

All twelve library packages use the same version. The checked-in version is
`4.0.0-preview.1`, declared once in the root `Directory.Build.props`; the release
tag is `v4.0.0-preview.1`. Keep the `preview.1` suffix in package versions and
include the leading `v` only in the Git tag. Future releases change the shared
version for the entire set, including the core package.

| Package ID | Target frameworks |
| --- | --- |
| `netDxf.netstandard` | `netstandard2.0`, `net471`, `net48`, `net6.0`, `net8.0` |
| `netDxf.Shadow` | `net8.0` |
| `netDxf.Shadow.NetDxf` | `net8.0` |
| `netDxf.Shadow.Geometry` | `net8.0` |
| `netDxf.Shadow.Graph` | `net8.0` |
| `netDxf.Shadow.Dimensions` | `net8.0` |
| `netDxf.Shadow.Rendering` | `net8.0` |
| `netDxf.Shadow.Rendering.Shx` | `net8.0` |
| `netDxf.Shadow.Rendering.SkiaSharp` | `net8.0` |
| `netDxf.Shadow.Editor` | `net8.0` |
| `netDxf.Shadow.Drafting` | `net8.0` |
| `netDxf.Shadow.Exchange.Dwg` | `net8.0` |

Each library produces a `.nupkg` and a matching `.snupkg`: 24 package archives
per release. Shared package metadata includes the package README, MIT license,
repository identity, XML documentation and portable symbols. Internal package
dependencies use the common release version. The core package keeps its existing
five targets and assembly signing; the shadow libraries target .NET 8. Samples,
benchmarks, tests and tools are not release packages.

## Two core workflows and Studio Pages

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

`shadow-studio-pages.yml` is the separate Studio Pages build/deployment workflow.
Its site deployment remains isolated from core package qualification and NuGet
publication. There are two core workflows and this independent Pages workflow.

The standalone `dxf-conformance.yml` and `nuget-publish.yml` files are removed.
Their behavior is relocated, not bypassed: the entire conformance job body is
retained, and the NuGet steps retain tag/source/checksum verification, protected
environment, explicit opt-in and duplicate-version failure behavior.
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

The shadow installed-package consumer restores all twelve candidate packages
from an isolated feed/cache, with public feeds used only for external
dependencies. It exercises the packaged assemblies without project references,
including shadow capture/synchronization, editing, geometry, graph, dimensions,
SHX, SVG, native SkiaSharp/HarfBuzz rendering and DWG exchange. Its
`shadow-smoke.json` receipt and `shadow-smoke.log` bind the results to the source
commit/tree, candidate package hashes and loaded assembly hashes. Package
qualification requires the exact twelve-package and twelve-symbol inventory;
missing, additional, mismatched-version or mismatched-source packages fail.

The `qualify` job requires successful conformance and runtime-evidence jobs; the
latter depends on the package and all-target build chain. It downloads the same
reports and invokes the unchanged source/checksum/runtime validators. Neither
failure suppression nor `always()` permits qualification after a failed gate.
Core runtime reports remain sealed in the strict 25-entry `runtime-evidence.zip`.
The shadow package-consumer receipt is retained alongside that archive and is
revalidated during qualification and immediately before publication.

The CI workflow is read-only and has no publication credentials. External actions
remain commit-pinned. In-progress cancellation is enabled only for PR/queue CI; it is disabled for
tagged and manual release runs. GitHub's default concurrency policy still allows
only one pending run per group, so a newer request can replace an older pending
request. This configuration does not promise an unlimited FIFO release queue.
The reusable workflow's concurrency namespace is distinct from its caller's.

## One-time trusted publishing setup

NuGet publication uses GitHub OIDC through the official `NuGet/login` action.
The repository does not need a long-lived NuGet API key. Configure the NuGet
policy and GitHub repository settings below before publishing the first release.

### 1. Create the policy on nuget.org

Sign in to [nuget.org](https://www.nuget.org/) as the profile that will publish
the packages. The existing `netDxf.netstandard` package is owned by the
[`wieslawsoltes` profile](https://www.nuget.org/profiles/wieslawsoltes). Open the
profile menu, choose **Trusted Publishing**, and create a GitHub Actions policy.

| Policy field | Value |
| --- | --- |
| Policy name | `netDxf-GitHub` (a descriptive name of your choice) |
| Package owner | `wieslawsoltes`, or the owner authorized for all twelve packages |
| Repository Owner | `wieslawsoltes` |
| Repository | `netDxf` |
| Workflow File | `release.yml` |
| Environment | `nuget` |

The workflow field is the filename only; it identifies
`.github/workflows/release.yml`. The environment identifies the publishing job's
`environment: nuget` setting. Under **Select Scopes**, enable **Push** and choose
**Push new packages and package versions**. Leave unlisting disabled: this
workflow only publishes.

In **Glob Patterns and Packages (Separate by a new line)**, enter all three
lines:

```text
netDxf.netstandard
netDxf.Shadow
netDxf.Shadow.*
```

The separate `netDxf.Shadow` entry is necessary: `netDxf.Shadow.*` matches its
child package names but not the root package. The new-package push scope allows
the first release of the shadow package IDs through trusted publishing. No
initial upload with a manually generated API key is required. Existing package
IDs still require the selected owner's publishing rights.

Save the policy. `NUGET_USER` below must be the NuGet **profile username** that
created the policy, not an email address or an organization name. A policy can
select an organization as package owner while the action authenticates the
individual profile that created it. See the official
[NuGet trusted publishing guide](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
and the [NuGet Gallery policy form](https://github.com/NuGet/NuGetGallery/blob/main/src/NuGetGallery/Views/Users/TrustedPublishing.cshtml).

### 2. Configure the GitHub repository

From this checkout, authenticate the GitHub CLI with repository administration
access, then run the idempotent setup command. For the current NuGet owner:

```sh
gh auth login
python tools/ci/setup_nuget.py --nuget-user wieslawsoltes
```

Use `--nuget-user <profile-name>` when another authorized NuGet profile created
the policy. The setup command configures the repository-side environments and
variables; the NuGet policy is created separately in step 1. Repeating repository
setup does not publish a release or push a package.

To inspect the configuration without changing it, append `--dry-run`:

```sh
python tools/ci/setup_nuget.py --nuget-user wieslawsoltes --dry-run
```

The helper reads all environment and variable pages before making changes. It
preserves every existing environment, including reviewers, timers, custom rules
and bypass settings. Newly created environments receive a `v*` tag rule. Existing
custom tag rules must allow the tag derived from the shared version; use
`--tag v4.0.0-preview.1` to select a specific tag for verification. Existing
protected-branches-only settings or a custom policy without a verifiable matching
tag rule stop setup before writes rather than being broadened. Permission and
API errors are reported; a failed lookup is never interpreted as absence.

The helper verifies the environment settings and `NUGET_USER` before setting
`NUGET_PUBLISH_ENABLED=true` as its final update. If setup stops after creating an
environment but before installing its tag rule, inspect that partial environment
in GitHub before retrying; an empty custom-rule list does not establish a tag
restriction.

The required repository configuration is:

| Setting | Value / purpose |
| --- | --- |
| GitHub environment `release` | Controls creation of qualified draft GitHub releases |
| GitHub environment `nuget` | Restricts NuGet authentication and publication to the named environment |
| Repository Actions variable `NUGET_USER` | NuGet profile username, normally `wieslawsoltes` |
| Repository Actions variable `NUGET_PUBLISH_ENABLED` | Literal `true` to enable the publication job |
| Environment deployment rule | New environments allow tags matching `v*`; existing rules must permit the intended release tag |

Keep `NUGET_PUBLISH_ENABLED` at repository scope: the workflow evaluates it before
the publishing job enters the environment. Set it to `false` to disable
publication while retaining build and qualification checks. `NUGET_USER` is a
profile name, so it is stored as a configuration variable. The temporary API key
exists only inside an actual publishing job.

### Manual equivalent

If configuring the repository in the GitHub UI, open
[repository settings](https://github.com/wieslawsoltes/netDxf/settings):

1. Under **Environments**, create or open `release` and `nuget`.
2. For each new environment, select **Selected branches and tags** under
   deployment restrictions and add a **Tag** rule with pattern `v*`. For an
   existing environment, verify that the intended release tag is permitted and
   retain its current rules. Retain reviewer, timer and other protection rules.
   Maintainers can add required reviewers here if release approval is part of
   their process.
3. Under **Secrets and variables → Actions → Variables**, set the repository
   variable `NUGET_USER` to the NuGet profile username.
4. After saving the NuGet policy, set repository variable
   `NUGET_PUBLISH_ENABLED` to `true`.

With environments already configured, the variable steps can also be performed
with the GitHub CLI:

```sh
gh variable set NUGET_USER --repo wieslawsoltes/netDxf --body wieslawsoltes
gh variable set NUGET_PUBLISH_ENABLED --repo wieslawsoltes/netDxf --body true
```

The environment names in YAML alone do not configure deployment protections.
Repository ownership or administration access is required for environment
settings; see [GitHub's environment configuration instructions](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments).

## Release draft or nonpublishing rehearsal

Merge and qualify the intended source before creating a new immutable `vVERSION`
tag, initially `v4.0.0-preview.1`. Tag pushes validate version spelling, tag target and
`netstandard` ancestry, rerun core CI on that exact source, and then create a
**draft** GitHub release behind the `release` environment. Its assets include all
twelve packages and their portable symbols, source archive, source-bound test
and runtime evidence, shadow installed-package evidence, release notes and
checksums. Existing assets/releases are not overwritten.

After the version change is merged into `netstandard`, create the first tag from
the intended qualified commit:

```sh
git switch netstandard
git pull --ff-only
git tag -a v4.0.0-preview.1 -m "netDxf 4.0.0-preview.1"
git push origin v4.0.0-preview.1
```

Wait for **Release** and its reusable CI jobs to succeed, inspect the resulting
draft and its qualified assets, then publish the draft as a GitHub prerelease.
That published-release event starts NuGet publication when the opt-in and
environment conditions are satisfied. Creating the tag creates a draft only;
merging the packaging changes does not publish the version.

For a rehearsal, run **Release** with `dry_run=true` and `publish_nuget=false`
(the defaults). A branch run uses a CI prerelease; a tag run uses the selected tag
version. No write step runs. To create a draft manually, select an existing
release tag and set `dry_run=false`, leaving `publish_nuget=false`.

## NuGet publication without rebuilding

The `publish-nuget` job requires repository variable `NUGET_PUBLISH_ENABLED=true`.
It runs on a `release: published` event or a manual Release invocation with
`publish_nuget=true`. These publication-only events skip the planner/build/draft
chain. They fetch already published release assets without creating new bytes.
Draft releases and branch refs reject. Exact source, live tag, complete qualified
receipt, retained core runtime and shadow package evidence, every package,
symbols and checksums are revalidated immediately before any push. A live-tag
check is not an atomic remote lock.

For manual validation of existing assets, select their tag, set
`publish_nuget=true` and retain `dry_run=true`; the actual push is skipped. A
manual push additionally requires `dry_run=false`. A published-release event is
itself an explicit publication request, still subject to the repository opt-in
and `nuget` environment. The default setup publishes nothing merely by merging.

Only the publishing job receives `id-token: write`, alongside `contents: read`.
After successful asset validation, and only when an actual push is requested,
the commit-pinned `NuGet/login` action exchanges GitHub's OIDC token for a
short-lived NuGet API key using `NUGET_USER`. Both login and push are skipped
during a dry run, so a publication rehearsal does not require a working NuGet
login. The action's `NUGET_API_KEY` output is passed to the push step and is
masked in workflow logs. The current pin is the official `v1.2.0` release,
commit `8d196754b4036150537f80ac539e15c2f1028841`.

NuGet's temporary API key lasts one hour; authentication therefore occurs after
qualification and immediately before upload. The push uses the NuGet V3 service
index, `https://api.nuget.org/v3/index.json`. Each `.nupkg` is accompanied by its
same-version `.snupkg`, which NuGet pushes automatically. See the
[official login action](https://github.com/NuGet/login) and
[symbol publishing documentation](https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg).

The release is successful only when all twelve package and symbol uploads
succeed. Publication to NuGet is not atomic across packages. A partial push is a
failure that needs inspection; duplicate versions are never silently accepted,
and the workflow does not use `--skip-duplicate`. Inspect package and symbol
status before retrying a partially published release. Changed artifacts require
a new common version and a new qualified release. Do not rebuild or replace
assets under an already published version.

## Validation

```sh
python -m unittest discover -s tests/ci_pipeline -p 'test_*.py'
python tools/ci/pipeline.py pack --version 4.0.0-preview.1
python tools/ci/pipeline.py shadow-smoke --version 4.0.0-preview.1
dotnet restore netDxf/netDxf.csproj
dotnet build netDxf/netDxf.csproj -c Release --no-restore
dotnet run --project tests/netDxf.Conformance -c Release
python tools/run_independent_verifiers.py artifacts/conformance
```

Workflow tests verify the two core workflows and isolated Studio Pages inventory, retained conformance
job, acyclic dependency graph, event routing, real publication-gate expressions,
manual dry-run exclusion, OIDC permissions, authentication conditions and pins.
Package tests cover the twelve-package inventory, common version, internal
dependencies, source metadata, portable symbols and retained installed-package
evidence. Existing release-planner CLI tests
continue to run in real disposable Git repositories. YAML checks and local tests
are not proof of a hosted queue/runtime/release execution. Pipeline cleanup does
not establish native AutoCAD acceptance or full all-version parity.

References: [GitHub workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows),
[reusable workflows](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows),
[workflow syntax and permissions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax),
[NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing),
[NuGet package versions](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning),
[dotnet nuget push](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push).
