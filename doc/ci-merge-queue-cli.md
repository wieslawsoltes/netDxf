# Real-process merge-queue planner verification

PR #199 independently merged merge-group routing and nonpublishing validation
while this continuation was being tested. Its three workflow files, planner,
queue cancellation policy, six pipeline tests and sparse tolerance-projection
suite are retained unchanged. The earlier local queue implementation is preserved
in the local checkpoint history, not replayed over that newer upstream source.

This increment adds seven real-process/routing tests alongside the original tests.
Each behavioral test copies the actual current planner into a disposable Git
repository, commits a minimal valid project, and executes its command-line entry
point with explicit event/ref/commit metadata. It does not mock Git, the planner,
its event environment or emitted workflow outputs.

The tests verify the exact source-bound nonpublishing queue receipt, wrong-commit
and dirty-checkout refusal, both PR and queue events with all ref/dry-run settings,
and unchanged tag/manual planning. As defined by the merged planner, validation
events force dry-run rather than rejecting a false input. Tests require false
publication output even for a simulated tagged validation event. Real tags in the
disposable repository test only plan calculation, never remote publication.

Routing assertions retain checks_requested for the netstandard target in build,
conformance and release rehearsal, read-only defaults and the no-publication gate.
The merged PR-or-queue cancellation policy stays in place; tag and manual runs are
not made cancellable. NuGet publication gains no queue trigger. These tests do not
enable rulesets or claim an actual hosted merge_group event ran for this candidate.

An initial new dirty-source fixture replaced project XML with invalid text, so it
hit the XML parser before the intended source-integrity diagnostic. It now edits
the version in valid XML. Rejection and absence-of-output assertions are retained.
The preliminary local planner's stricter ref/dry-run refusal expectations were
explicitly reconciled with #199's safe forced-dry-run contract, not described as
unchanged incoming text. Existing upstream tests were not weakened or replaced.

See [GitHub workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#merge_group)
and [merge-queue requirements](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/managing-a-merge-queue).
