# Independent HATCH source-relation review

The unchanged standalone probe in `Program.cs` reproduced 16 defective outcomes
before the HATCH source-closure fixes and verified corrected outcomes for all 16
afterwards. No source files in the implementation owner's worktree or root
integration repository were edited by this reviewer.

| Evidence | SHA-256 |
| --- | --- |
| Unchanged probe source | `dbf6e1719ba0a99a68070b99bed02d2ab458f56311d55278b44097393ee43910` |
| Baseline Debug library | `94f14189e72ab1b541992c157f2718aa1d133258b1c75d4bd8cf85c82eaeef37` |
| Corrected Debug library | `8d1f2a514dc317e112ee20a369bf2c34bc5b4018ed189418625b9c1742c2b63f` |
| Baseline receipt | `a5e5e6bf30d0660b0e272cc58e72235ccdb23094126230e422e62d910a503ac0` |
| Corrected receipt | `a77b1e9668b35798be7d4d80fff14fbbc63be0c089206b24781f3b20c94db93f` |

`before.json` and `after.json` contain every actual result, including exception
types/messages and relevant object states. `review-result.json` records the
16-pass outcome and evidence hashes. The probe executes through .NET 8 on Linux
against the library DLL. Its source and inputs were unchanged between the two
reported executions; only the referenced library DLL changed.

## Counterexamples and corrected behavior

| Cases | Trigger | Baseline defect | Corrected behavior |
| ---: | --- | --- | --- |
| 2 | Add a hatch, or add a path to an attached hatch, whose source list contains a new LINE followed by a detached source-bound Polyline3D from another document | Foreign adoption throws after destination membership, handle allocation, source ownership and, for the attached path, path/reactor state have already changed | Admission rejects before destination membership, allocation, path count, source ownership or reactor count changes |
| 1 | Reuse the same path object in two hatches and unlink the first | The second hatch remains associative but its source list becomes empty while its source backlink remains | The second constructor rejects the shared path instance before mutation |
| 1 | Repeat the same path object twice in one hatch and unlink | Two initial source uses release only one reactor | Repeated path-instance construction rejects before adding any reactor |
| 6 | Three malformed source identities in ASCII and binary: a discarded unsupported physical record plus an accepted LINE with the same handle; two equal common group-5 declarations; two differing common group-5 declarations | Every drawing loads and binds the HATCH to a seemingly valid source despite ambiguous physical/common identity | Every case rejects contextually during HATCH source binding |
| 4 | Save and reload an associative hatch, then unlink or remove it, in ASCII and binary | Unlink leaves an obsolete persistent source backlink; removal leaves an unregistered reactor and makes the next save fail | Both operations clear the final automatic and persistent association backlinks and the resulting drawing saves successfully |
| 2 | Reuse an already bound path in a constructor with the opposite associative flag | Constructing the second hatch generates or clears source contours belonging to the first hatch | Constructor preflight rejects while preserving source and reactor counts |

The unique-path-instance rule deliberately distinguishes path ownership from
source reuse. Distinct paths and hatches may still share a source entity, and a
source list may retain duplicate entries. Reusing the same mutable path object
is refused because unlinking, regeneration and removal otherwise mutate the
same contour collection through multiple owners.

The loaded-backlink cases distinguish `EntityObject.Reactors`, which account for
active hatch source uses, from common `PersistentReactors` imported from saved
DXF. Cleanup is required only when the last automatic association use ends;
other active paths and unrelated reactors must remain intact. The owner's
regression suite covers those shared-source and preservation controls.

## Review scope

Source inspection covered the HATCH reader's accepted-source lookup and
same-block checks, source-count framing, non-associative nonempty lists, writer
registration/ownership preflight, detached source adoption, path ownership,
constructor mutation order and loaded association cleanup. The implementation
owner additionally corrected nested private control-group tracking so stricter
HATCH identity checks do not treat a nested private group-5 value as a common
physical declaration.

This probe is additional independent counterexample evidence. It does not
replace the owner's six-profile native/producer qualification, full Debug and
Release conformance suite, independent packet/audit verifier, or integration CI.
It does not execute native CAD software, evaluate hatch geometry, qualify every
possible entity metadata graph, or claim transactionality for arbitrary batches
of user callbacks or collection operations.

The final corrected DLL also replaces missing-entity-handle Debug.Assert termination with contextual parse rejection. The same 16-case probe was rerun successfully against that final DLL; no probe scenario changed.
