# Retained runtime evidence in qualified releases

The eight packaged-assembly runtime profiles introduced by PR #196 remain
mandatory build dependencies. Their complete process logs, XML receipts, result
records and aggregate summary now also travel with the qualified release, inside
`runtime-evidence.zip`. The ordinary consumer and all conformance gates remain.

## Capture, qualify, and verify again before publishing

After the existing runtime aggregate succeeds, the build captures exactly 25
files: three records per required profile and one aggregate summary. Capture
replays the archived bytes through the existing runtime validator before
publishing the archive. It does not merely trust an earlier summary or a PASS
line. Source commit/tree, candidate package hash, each selected DLL hash, target
frameworks, observed runtime, scenario counts and original receipt/log hashes
must agree. Archive timestamps and entry order are fixed for repeatability.

Release qualification downloads this artifact, repeats validation against its
actual candidate package, copies it into the release assets, and seals its digest
and profile/scenario totals into `qualification.json` and `SHA256SUMS`. A missing
runtime archive blocks qualification even when conformance evidence passes.
`verify_release`, used by both existing publication paths, replays that retained
archive and checks the qualification receipt again. Re-sealing a bundle after
substituting another DLL, runtime result, log or source does not satisfy these
semantic checks. There is no new permission, secret or publishing opt-in.

The archive has an exact fixed path inventory; extra/duplicate/traversal entries,
links, encrypted members and unsupported compression reject. Resource limits are
64 MiB for the archive and total decompressed data, 16 MiB per process log,
64 KiB per result, 16 KiB per XML receipt and 1 MiB for the summary. Validation
writes only these known names into a disposable temporary directory; archive
permissions and links are never extracted. A failure does not remove or reset
application source. This does not make the entire release operation transactional.

## Evidence boundaries

The original runtime validation remains authoritative for loaded-assembly and
runtime checks. Replaying receipts is not rerunning .NET processes. Hash binding
is not an independent cryptographic attestation against a malicious actor who
can rewrite all source, binaries and reports. This is not an atomic filesystem
snapshot or server-side release/tag lock. Existing protected-environment and
live-tag checks remain unchanged. Platform execution must still pass in hosted
CI before a new implementation is merged or released.

Eight added pipeline tests exercise real archive capture/replay and package-entry
hash validation with clearly labeled synthetic unit-test receipts. They cover
complete deterministic capture, missing/extra/duplicate/unsafe entries, links and
size bounds, changed receipts/logs, modified summary, coherently forged wrong-DLL
values, source/package mismatch, qualification/publication replay and actual
workflow wiring. The original conformance-only qualification fixture mocks the
new runtime boundary so all its previous assertions remain independently tested.
These synthetic records do not count as actual Windows or .NET 6 execution.
