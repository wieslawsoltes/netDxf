# Refuse source receipts for dirty checkouts

Build metadata, post-test provenance, package manifests, release qualification
and downloaded-release validation all obtain their commit/tree pair through
`tools/ci/pipeline.py:identity`. This now first requires a clean Git index and
working tree, including non-ignored untracked files. Previously it returned HEAD
regardless of local edits, allowing an assembly or test run from modified files
to be attributed to the unmodified commit/source archive.

The check rejects staged/unstaged edits, deletion, rename, additional source and
changes to ignore rules. Ignored artifacts, compiler caches and normal build
outputs remain permitted. No clean/reset operation is run against the user's
checkout. The error requires the source edits to be committed before a receipt
can be produced. Workflow permissions, credentials, publication switches and
all existing build/test stages are unchanged.

The regression uses a real disposable Git repository on each CI platform. It
asserts the exact clean commit/tree, each dirty case, and acceptance of ignored
build outputs. The existing release-plan mock now models an empty status query;
its publication/tag assertions are retained.

This is an integrity precondition, not an atomic snapshot or sandbox: it cannot
prove that another process did not change and restore source during execution,
and it does not treat ignored build inputs as committed source. Real release
publication and NuGet credentials still require the setup in CI-RELEASE.md.
