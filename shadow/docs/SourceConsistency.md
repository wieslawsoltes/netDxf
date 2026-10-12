# Source consistency and transaction reentrancy

`ShadowSession` publishes detached immutable snapshots. Mutable netDxf instances remain
at the serialized source boundary; `Current` remains a lock-free read in both modes.

## Validation policies

The existing constructor signature is retained, but its default policy is now
`ShadowSourceValidationMode.FullGraph`:

```csharp
var session = new ShadowSession(document);
```

Before a nonempty property batch performs source writes, the session captures the
reachable public graph and compares it with `Current`. An unrelated entity change,
resource change, collection insertion/removal, or different property on the same entity
rejects the batch with `ShadowConflictException`. The rejected batch does not publish,
advance the revision, implicitly rebase, or overwrite the external change.

This is a behavioral and performance change for the original constructor. A full audit
is proportional to the captured objects, properties and collection values, and allocates
capture scratch state even when structurally shared nodes are retained. Do not attribute
incremental scalar-edit benchmark results to full-graph auditing.

For exclusively owned sources, opt into the original incremental property policy:

```csharp
var session = new ShadowSession(
    document,
    options: null,
    sourceValidation: ShadowSourceValidationMode.EditedProperties);
```

This mode still validates document identity, snapshot revision, immutable preconditions,
writability, referenced IDs and the live values of edited properties. Audited local
setters keep the existing dirty-object capture path. Other setters may require full
capture after writing. **Unrelated unrefreshed external mutations are not detected by
property batches in this mode.** All mutable access must be owned and serialized through
the session, or explicitly reconciled before an edit. The 10,000-entity allocation test
now selects this policy explicitly; its allocation and 9,999-item reuse assertions are
unchanged.

Structural patches always use full-graph validation, regardless of property policy.
They can change collection membership, resources, ownership and identity relationships.

## Recovery after an external change

An external change must be serialized against session source operations; a source-policy
check does not make netDxf safe for unsynchronized concurrent mutation.

```csharp
// After a serialized external mutation, explicitly reconcile it.
ShadowSnapshot refreshed = session.Refresh();

// Recreate edits from this snapshot. Do not retry an old revision unchanged.
var id = refreshed.DrawingEntities().First().Id!.Value;
var oldValue = refreshed.Read(id)["StartPoint"].Value;
var batch = ShadowEditBatch.Create(refreshed,
    new ShadowPropertyEdit(id, "StartPoint", oldValue,
        ShadowValue.From(new Vec3(10, 0))));
ShadowSnapshot committed = session.Apply(batch);
```

The example assumes the selected entity has a writable `StartPoint` property, such as a
line. Existing retained snapshots remain unchanged. Editor histories that no longer
match the current revision must be explicitly reset or reconstructed by the caller.

Empty property batches retain the existing behavior: validate document/revision and
return `Current` without scanning source or advancing revision. They are not a refresh
operation. Structural no-ops likewise retain the existing early return.

`RefreshProperties` remains a trusted, nonstructural operation. Its dirty-object list
must cover every changed object; choosing `FullGraph` does not make a partial refresh
complete or turn it into a mutation notification stream.

## Reentrancy

A monitor serializes threads but permits nested entry on the same thread. The session
therefore also maintains an allocation-free, stack-scoped operation guard around
`Apply` (both overloads), `Refresh`, `RefreshProperties`, and `MutateSource`.

Source callbacks may read the last published `Current`, but cannot begin another source
operation while capture, validation, writes, rollback or reconciliation are active. A
nested source operation throws `InvalidOperationException` before performing its work.
The guard resets on success, rejected preconditions and exceptions.

`ReadSource` remains available for serialized live inspection; its callback must not
mutate or retain mutable source objects. Within a mutation callback, live source reads
may observe in-progress source state, whereas `Current` still exposes the last published
immutable snapshot. Schedule follow-on writes after the outer operation returns; the
adapter does not queue or silently reorder them.

`MutateSource` remains explicitly nontransactional. If its callback mutates an object
then throws (including an uncaught reentrancy rejection), the existing `finally`
reconciles those source changes before propagating the exception. This does not claim
rollback of arbitrary callbacks. Failure to reconcile still fail-stops the session.

## Boundaries

Full-graph validation compares captured public state. It cannot detect a change that
was completely reverted before the audit (ABA), private/unrepresented state, or changes
hidden by an unreadable-property diagnostic. It is not a native-resource or original-DXF
byte comparison. There is no new polling thread, reflection notification subscription,
rendering dependency, or mutable state retained in snapshots.

## Verification

Run both existing executable suites from the repository root:

```sh
dotnet run --project shadow/tests/netDxf.Shadow.Verification/netDxf.Shadow.Verification.csproj -c Release -p:TargetFrameworks=net8.0
dotnet run --project shadow/tests/netDxf.Shadow.AdvancedVerification/netDxf.Shadow.AdvancedVerification.csproj -c Release -p:TargetFrameworks=net8.0
```

`SourceConsistencyVerification` registers 31 cases: 13 standalone cases, plus four
policy-parameterized cases and five reentrancy variants for each of the two policies.
The cases exercise source conflicts before writes, immutable snapshot retention,
explicit recovery, stale revisions, atomic batches, competing writers, constructor
validation, exception cleanup, and nested source operations.
