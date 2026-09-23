# Publication-time source and tag verification

The existing build, conformance, release and optional NuGet workflows remain.
Immediately before draft GitHub-release creation or NuGet pushing, publication
now checks the complete qualified bundle (source identity, receipt, package
metadata, targets, symbols and checksums), then resolves the current remote tag.
A moved/deleted tag, unreadable API response or non-commit target stops the job.
A local tag checked during release planning is not treated as current remote
state after a long test run or protected-environment approval.

Lightweight and annotated tags are supported. Annotated tag objects are peeled
with a bounded, cycle-checked traversal. Endpoint paths are constructed from
validated identifiers, never from URLs supplied in API responses. GitHub CLI
uses the job-scoped GH_TOKEN; checkout credentials remain unpersisted. Existing
permissions, protected environments, version eligibility, draft behavior and
opt-in API-key NuGet publication are unchanged. No actual package publication is
needed to run the regression tests.

This is a last-moment consistency check, not an atomic server-side lock. A tag
could still be moved after the final query. Maintainers should configure tag
rules and release immutability as appropriate. The workflow does not configure
repository rules or claim to eliminate that residual race. In particular,
`gh release create --verify-tag` only checks existence, not equality with the
commit from which the assets were built.

Tests exercise lightweight/nested tags, invalid/cyclic/deep targets, remote
failures, moved tags, damaged assets and both real workflow call sites. Hosted
build jobs run these tests on Linux and Windows. Publication itself remains
unexecuted until an authorized tag/release operation.

Primary references: [GitHub CLI release create](https://cli.github.com/manual/gh_release_create)
and [Git references](https://docs.github.com/en/rest/git/refs#get-a-reference).
