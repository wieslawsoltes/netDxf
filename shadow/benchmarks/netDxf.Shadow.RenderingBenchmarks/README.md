# Synthetic rendering benchmark

This executable creates synthetic geometry and labels in memory and measures the
rendering stages independently. No input files are required or read.

| Phase | What is timed |
| --- | --- |
| Connected lines | Compilation of exact analytic lines, including render-item creation and spatial indexing. |
| Cold text layout | A fresh native text engine and complete scene compilation, with repeated words and 80 distinct numbers. The immutable synthetic source is reused. |
| Camera redraw | Drawing to an existing CPU Skia surface with a different pan and zoom on every iteration. This includes new picture recording, all visible geometry, and rasterization. |
| SVG | Complete SVG serialization with invariant round-trip coordinate precision. |
| PNG / RGBA | Rendering to a new surface plus the selected lossless transport. The camera is held constant so the already recorded picture is reusable. |

Run from the repository root:

```sh
dotnet run --project shadow/benchmarks/netDxf.Shadow.RenderingBenchmarks -c Release -- --case all --iterations 5 --warmup 2
```

The default workloads contain 200,000 connected line segments, 1,500 labels, and
500,000 path vertices. Use `--case lines`, `--case text`, or `--case paths` to isolate a
stage, and `--lines`, `--texts`, or `--points` to change its size. Frames are 1200 × 900.
The text workload uses the host's `sans-serif` font; keep fonts and operating system
constant for a comparison. `--warmup 0 --iterations 1` exposes first-use work that
occurs inside each timed phase. Scene construction and prepared redraw resources
remain outside the measured interval; this is not a whole-process startup measurement.

Output is newline-delimited JSON containing runtime/GC settings, workload counts,
individual samples, and medians. Managed allocations are measured on the executing
thread; native Skia allocations and GPU memory are not included. Input construction,
forced collection between samples, output validation, and JSON formatting are outside
the timed intervals. Redraw and export keep complete geometry and the same sampling,
antialiasing, typography, opacity, clipping, and paint settings.

For a comparison, build both revisions before measuring, stop other CPU-intensive
work, and run the executables sequentially with the same environment and arguments.
Optional `DOTNET_TieredCompilation=0` and `DOTNET_gcConcurrent=0` remove background tier
promotion and concurrent GC from native comparisons; report these settings with the
results. Allocation counts are usually more stable than elapsed time. To test an older
checkout, copy this benchmark directory into the same relative location there. The
optional raw-pixel method is discovered at runtime, so an older backend reports that
phase as unsupported while retaining all other workloads.

This measures native .NET rendering. It does not measure browser download, WebAssembly
startup, JavaScript transport, image decoding, or canvas presentation. Exact scalar-path,
HarfBuzz, font-invalidation, clipping, selection, transparency, and decoded-PNG parity
checks are maintained separately in `RenderingPerformanceVerification`.
