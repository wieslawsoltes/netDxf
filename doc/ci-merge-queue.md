# Merge-queue validation without publication

Build/package, complete DXF conformance and full release rehearsal now respond
to `merge_group.checks_requested` for the `netstandard` target. Existing PR,
push, dispatch and reusable-workflow entry points remain. Queue events have no
path filter, so a required check cannot silently disappear for a particular
queued change. The reusable workflows test GitHub's queue commit (`github.sha`)
and retain the existing source, package and runtime evidence checks.

Queue rehearsals force dry-run planning in both workflow configuration and the
Python planner. Even a false DRY_RUN value cannot make a merge-group/PR event
publish. The source-identity check still rejects an unrelated checkout. Tagged
push and explicit tagged workflow-dispatch publication contracts are unchanged.
Only PR and queue release rehearsals may be cancelled as obsolete; actual tag
and manual release operations remain non-cancellable. The NuGet publishing
workflow has no queue trigger, and publication privileges/environments remain
unchanged. This does not enable or modify a repository merge-queue ruleset.

Six added pipeline tests cover triggers, complete gate reuse, all validation
ref/dry-run combinations, source mismatch, preserved tag/manual behavior, and
passing the actual trigger into the CLI planner. The previous PR-only cancellation
assertion now includes the queue case while preserving tag/manual exclusions.
Unit simulation is not a real GitHub merge-group run or a native platform test.
The actual hosted PR rehearsal must qualify the updated workflow before merge.

[GitHub's event reference](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#merge_group)
and [merge-queue CI requirements](https://docs.github.com/en/enterprise-cloud@latest/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/managing-a-merge-queue)
explain the separate queue event and the need for required checks on its commit.
