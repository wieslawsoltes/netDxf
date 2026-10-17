# Retained metadata import benchmark

This executable creates a synthetic drawing in memory with independent lines, one
block insert with two attributes, XData, and persistent references to the owned
attributes. It measures typed DXF import and application registry reference queries
separately. Text and binary encodings use the same generated document.

Run from the repository root:

```sh
dotnet run --project benchmarks/netDxf.MetadataBenchmarks -c Release -- --entities 10000 --references 64 --iterations 5 --warmup 2 --encoding both
```

`--references` selects the number of lines with two distinct persistent references each.
`--entities` accepts 1 to 100,000, and the reference count must not exceed it.
`--encoding` accepts `text`, `binary`, or `both`. No input files are read or written.

Newline-delimited JSON reports runtime settings, encoded size, individual samples,
and medians. Timed regions exclude fixture generation, serialization, collection,
validation, and formatting. Allocation counts cover the executing managed thread.
Every iteration verifies exact line coordinates, entity counts, reactor order,
target object identity, and XData reference identity and use count.

For comparisons, build both revisions first and run them sequentially with identical
arguments, runtime, and environment while other CPU-intensive work is stopped.
`DOTNET_TieredCompilation=0` can remove native tier-promotion variability; report that
setting alongside results. Vary the entity and reference counts to distinguish
ordinary token parsing from retained-member reference resolution. This benchmark
does not measure snapshot capture, scene compilation, browser startup, or rendering.
