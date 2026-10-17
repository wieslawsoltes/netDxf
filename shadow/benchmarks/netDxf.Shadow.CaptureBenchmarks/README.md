# DXF import and snapshot capture benchmark

This executable constructs synthetic geometry in memory, serializes it as text and/or
binary DXF, and measures three synchronous phases separately:

1. Typed DXF import through `DxfDocument.Load`.
2. Initial detached snapshot capture through `new ShadowSession(document)`.
3. Full source validation through an unchanged `session.Refresh()`.

Each phase reports elapsed milliseconds and managed bytes allocated on the executing
thread. Fixture construction, serialization, forced collection, and result formatting
are excluded. Every iteration checks the imported/captured entity counts, capture
diagnostics, and no-op snapshot identity. Detailed numerical, mutation, ownership, and
public-member parity assertions remain in `CapturePerformanceVerification` and
`CaptureTraversalVerification`.

Run from the repository root:

```sh
dotnet run --project shadow/benchmarks/netDxf.Shadow.CaptureBenchmarks -c Release -- --entities 10000 --iterations 5 --warmup 2 --encoding both
```

The output is newline-delimited JSON: runtime/configuration, individual samples, and
per-phase medians. Use `--encoding text` or `--encoding binary` to select one format;
`--warmup 0 --iterations 1` also includes first-use metadata initialization. Counts from
1 to 100,000 are accepted. No input files are required or read.

`--workload lines` is the default grid of independent line entities. Use
`--workload shared-blocks` to instantiate the same block repeatedly; its eight lines
and eight arcs exercise repeated block/owner/style references and empty per-insert
collections. In that workload `--entities` counts top-level inserts, and the checks
also require every block member to remain in the captured graph. For example:

```sh
dotnet run --project shadow/benchmarks/netDxf.Shadow.CaptureBenchmarks -c Release -- --entities 10000 --iterations 5 --warmup 2 --encoding both --workload shared-blocks
```

For a steady-state comparison, build both revisions first, stop other CPU-intensive
work, use the same SDK/runtime/environment and arguments, and run the revisions
sequentially. Allocation counts are usually more stable than elapsed time. Optional
`DOTNET_TieredCompilation=0` removes background tier promotion from native comparisons;
report that setting when publishing measurements. This managed workload does not
measure browser download, WebAssembly initialization, scene compilation, or rendering.
