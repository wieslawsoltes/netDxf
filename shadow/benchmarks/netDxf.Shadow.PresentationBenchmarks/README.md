# Synthetic Studio presentation benchmark

This executable constructs a drawing in memory and compares the complete CAD state
serialization using reflection or generated writers, returning either a JSON string
or UTF-8 bytes. It requires no input files. The default 8,000 synthetic lines exercise
the existing 5,000 CAD rows and 2,000 diagnostic rows, Unicode layer names, and
double-precision geometry. Every output is checked against the original serializer
for exact equality; no fields or rows are removed.

Run from the repository root:

```sh
dotnet run --project shadow/benchmarks/netDxf.Shadow.PresentationBenchmarks -c Release -- --entities 8000 --iterations 21 --warmup 15
```

Geometry creation, DXF import, capture, first scene compilation, validation, and
console output are outside the measured intervals. The immutable presentation
metadata is already populated. All serializer variants warm together, and their
measurement order rotates to reduce first-use and tier-promotion bias. Output is
newline-delimited JSON with environment settings, workload size, individual samples,
and medians. Managed allocations are measured on the executing thread.

The string variants end at the first JSON string; they do not include the additional
escaping of that string inside the JavaScript interop envelope. The UTF-8 variants
end at the exact byte array used by browser replies. Browser interop, Fetch response
construction, parsing, and UI presentation require the actual WebAssembly browser
verification and are not represented by these native timings.

Build before measuring, run without concurrent CPU-intensive work, and keep runtime
and GC settings constant. Optional `DOTNET_TieredCompilation=0` and
`DOTNET_gcConcurrent=0` reduce background work in native comparisons; report those
settings alongside results. The directory can be copied unchanged to an older
checkout: the generated serializer is discovered at runtime and is reported as
unsupported when unavailable.

`StudioPresentationVerification` separately checks the generated contracts against
reflection for every sample layout, custom converters and serializer option
fallbacks, numeric-string request deserialization, source changes, history,
selection, retained snapshots, and document replacement.
