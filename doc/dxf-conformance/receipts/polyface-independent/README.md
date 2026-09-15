# Frozen independent POLYFACE review evidence

`review-report.md` describes the scope and comparison. `review-receipt.json`
retains the independent reviewer's original hashes and results. Both original
probe sources, project files, fixture generators, manifests, before/after result
files and output-gate results are included without modification.

`packet-evidence.tar.gz` contains all 732 preserved input and output DXF packets
at their original receipt-relative paths. Its hash is recorded in
`packet-evidence.json`. Extract the archive into this directory to inspect the
frozen packets or rerun the wire gate. The original supplemental Debug abort log
is retained as `supplement/before-abort.txt`; its bytes match the original
`before.log` hash in the supplemental seal.

The compiled libraries and probe executables are identified by hashes in the
reviewer's receipt. To rebuild a probe, provide the chosen netDxf library as
`baseline/netDxf.netstandard.dll`, then build `probe/Probe.csproj` or
`supplement/probe/Probe.csproj` using .NET 8. Each probe accepts its corpus directory
and a new output directory as positional arguments. Keep the included before/after
directories unchanged when rerunning.

For example, after extracting the packet archive, the independent decoder can
recheck the preserved main Debug outputs without netDxf:

```sh
python verify_probe_outputs.py . after-debug
python verify_probe_outputs.py supplement supplement/after-debug
```

Use the frozen packets for the exact comparison. Running the fixture generator
again creates a new producer run; its incidental drawing metadata may differ.
This evidence qualifies explicitly authored public grammar and API behavior,
including metadata context isolation. It adds no native AutoCAD validation or
arbitrary child-metadata preservation guarantee.

The [merged-build rerun](merged-65e7374/README.md) adds the unchanged-probe
qualification after POLYFACE and PolygonMesh integration: all 279 checks and
204 independently decoded outputs pass in both Debug and Release. Its receipt
distinguishes compiled source revision from the reviewed documentation revision.
