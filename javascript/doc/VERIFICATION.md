# Verification, evidence, and completion gates

## Fixed reference

`baseline.json` pins C# commit `3496ab91893a1e4ec9261b4833479f1799149cdc`, SDK 8.0.425, runtime/reference pack 8.0.31, 510 library source files, 193 conformance source files, and 399 original DXF fixtures. Its source fingerprint includes support assets. Changing the oracle requires a reviewed baseline update; the tools do not silently roll forward to another runtime or current branch.

`tools/dotnet.mjs` builds the actual C# source through the SDK Roslyn compiler, without NuGet restore or a replacement DXF implementation. Debug and Release use their respective conditional symbols and optimization setting. This qualifies the .NET 8 oracle, not every original target framework. The original test suite contains 35,309 runtime case identities at this pin.

## Evidence categories

| Evidence | What it proves | What it does not prove |
|---|---|---|
| Source inventory | Exact paths, hashes, declared types/members, enum values, fixture list | That an absent or partial JS implementation is complete |
| Mirrored conformance | The original named test cases actually ported and executed in JS | Coverage of missing cases or CLR-only adaptations |
| Supplemental JS units | Transaction rollback, failure injection, lifecycle, Unicode, deep graphs, packaging/gate behavior | More original C# test identities |
| Raw differential | Same accept/reject outcome, float bits, tag order, indexes, and exact serialized bytes | Typed JavaScript document/entity support |
| Handle differential | Same contextual occurrences/diagnostics, traversal, remap outcomes, and resulting bytes | Interpretation of opaque/private references |
| OBJECTS differential | Same schema views, transaction results, aliases/ownership, errors, and emitted bytes | General typed OBJECTS or private-schema parity |
| Typed .NET controls | JS raw edits/remaps can be read back by the .NET typed reader in those scenarios | A port of the typed JS construction/editing API |
| Browser differential | Native browser ESM execution matches .NET digests for the selected raw APIs and all shared fixtures | Every browser, worker/thread model, or missing typed feature |
| Offline package install | The npm tarball contains required production modules and runs without fetching dependencies | Publication, semver/API stability, or full completion |
| Benchmarks | Reproducible measured costs for stated operations/workloads | A .NET speedup claim or comprehensive performance qualification |

Both successful parsing and intentional rejections are counted and reported. A rejected fixture is not silently omitted from the corpus. Original files are never rewritten to make a port pass.

## Exact output versus normalized output

The raw API has two different paths: unchanged same-transport output reproduces original bytes; edited/cross-transport output invokes serialization and may change lexical formatting. Tests label these paths separately. For serialization, .NET and JavaScript receive identical input tags/bytes/operations and their resulting **full byte sequences must agree**.

There is no comparator that hides differences by sorting tags, renumbering handles, deleting dates/GUIDs, rounding doubles, stripping whitespace, or ignoring unknown data. Typed .NET fixtures are created once and the exact resulting bytes feed both raw implementations; their original nondeterministic metadata is not independently regenerated for an equality claim. Float values travel as binary64 bit strings and Int64 as decimal strings. Semantic views are compared independently of byte output.

## Commands and report paths

Run the commands in the parent README with `CONFIGURATION=Release` and repeat with `Debug`. A separate pinned checkout is selected with `NETDXF_SOURCE_ROOT`. Important output paths are relative to `javascript/`:

- `artifacts/inventory/source-inventory.json`: complete Roslyn inventory and unchanged fixture hashes.
- `artifacts/dotnet-release/` or `dotnet-debug/`: original results and pin/configuration metadata.
- `artifacts/conformance/`: JS original-case results and execution metadata.
- `artifacts/differential/<configuration>/`, `handles-differential/`, `objects-differential/`, and `casing-differential/`: completion flag, exact-comparison counts, failure evidence, and fingerprints.
- `artifacts/unit/`, `browser/`, and `package/`: supplemental execution reports.
- `artifacts/benchmark/results.json`: input shape, environment, warmups, all samples, median/p95, and memory observations.
- `artifacts/verification/<configuration>/report.json`: implemented-scope result, every missing original case/source path, and outstanding completion gates.

No report with a fatal error, incomplete run, missing corpus, stale runtime/verifier fingerprint, failing/duplicate/skipped/TODO case, or filtered original suite satisfies verification. The JSONL client rejects oracle exits, malformed or unsolicited output, and timeouts. Its shutdown promise is registered before execution so early process termination cannot leave cleanup indefinitely pending or hide the original failure. Supplemental tests deliberately inject those failures.

Browser evidence is derived from .NET outputs, not JavaScript expected values. A corpus generator records canonical complete-result SHA-256 digests; a real Chromium page runs the production ESM modules, including both emitted byte streams within each result. The browser tests every shared fixture through raw, handle, and object views, plus authored transaction workflows. Code hashes are checked again after browser execution.

## Gates

**Implemented-scope verification** is `node tools/verify.mjs`. It requires the complete pinned .NET case inventory, all currently mirrored JS cases, all shared fixtures in each applicable differential runner, successful browser/package/supplemental checks, generated-file integrity, and current performance evidence. It reports original-suite coverage and missing source paths; it does not claim full completion.

**Full-port completion** is `node tools/verify.mjs --require-complete`, also used by `prepublishOnly`. It remains a failing gate during this partial port, including when implemented-scope checks pass. The current gate explicitly records the outstanding typed API, test/example, signature/semantic, filesystem/atomic-save, and complete runtime/performance qualifications. Before enabling it, replace the remaining-gate declarations with actual executable evidence; do not simply flip `fullParityVerified` in a JSON file.

CI runs Debug and Release implemented-scope jobs with read-only repository permissions and uploads evidence even after failure. A separate `full-port completion` job checks the Release report and fails unless full parity is verified. Its blocked status is expected during this draft PR and is **not** reported as a passing check. The package remains private, the PR remains a draft, and no npm publication is performed.

## Remaining work

The typed `DxfDocument`, entity/table/math/style/collection model and typed writer/reader remain major missing areas. Original typed test factories, all sample scenarios, exhaustive API/member/signature migration, file save/atomic replacement, broad external stream behavior, and every platform/performance acceptance threshold still need implementation and qualification. Raw stored schemas preserve their documented limitations. None of this evidence is a native AutoCAD interoperability certificate.
