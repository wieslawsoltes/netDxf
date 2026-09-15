# Merged POLYFACE independent rerun

The unchanged frozen independent probes pass all **279 checks in each configuration** against the combined POLYFACE and PolygonMesh build. Independent ezdxf decoding verifies all **204 saved outputs per configuration**, and each corpus gate rejects six deliberate decoded-output corruptions.

| Corpus | Original passing / failing | Merged Debug | Merged Release | Saved outputs checked per configuration |
| --- | --- | --- | --- | --- |
| Main grammar, clone and write admission | 50 / 143 (Debug) | 193 / 0 | 193 / 0 | 120 |
| Supplemental private context and magnitude | 13 / 73 (Release) | 86 / 0 | 86 / 0 | 84 |

The original supplemental Debug process abort remains part of the preserved baseline; its completed baseline uses the unchanged Release library. There are 216 failing original checks across the comparable completed baselines. All retained positive controls pass after integration.

The DLLs were compiled from `93f37735db35aae25bec6fcaef15cc0406e8368d`; both embed informational version `3.0.1+93f37735db35aae25bec6fcaef15cc0406e8368d`. The reviewed clean checkout was `65e7374f8f07c2f44cc03ef0f5563da016658218`. Only documentation and the Python output gate changed between those revisions; no production files under `netDxf/` changed. The actual source hashes and DLL identities are recorded in `review-receipt.json`.

- Debug DLL SHA256: `f85f25e948be658c012c2b3d7b22a84de3e180227c3e5f124168c1ca14622e25`
- Release DLL SHA256: `2747a0db34d160003fd5f7e232cb3f58ba957aae4cbc194f75eec0db0c007e22`

Both original compiled probe programs, case expectations, fixture manifests and baseline seals remained unchanged for this rerun. The receipt preserves the copied runtime identities as hashes; executable artifacts are excluded from this source evidence package. The original corpus and probe source remain in the parent receipt directory.

`merged-outputs.tar.gz` contains all 408 saved packets at their original paths relative to this directory. `packet-evidence.json` records the archive and individual packet hashes. Extract the archive here to inspect the bytes or rerun the independent gates from the parent receipt directory:

```sh
python verify_probe_outputs.py . merged-65e7374/debug/main/outputs
python verify_probe_outputs.py supplement merged-65e7374/debug/supplement/outputs
python verify_probe_outputs.py . merged-65e7374/release/main/outputs
python verify_probe_outputs.py supplement merged-65e7374/release/supplement/outputs
```

Each gate checks parent kind, physical child topology, ordered coordinates, signed active-prefix indices, SEQEND and the following sentinel LINE. Its six negative controls mutate decoded output records. This establishes independent wire checks for the bounded behavior; it does not establish native AutoCAD AUDIT, rendering, editing, or private child metadata preservation.

`sha256.json` seals every packaged file in this directory except itself. The receipt's artifact paths refer to the original execution directory, where runtime files were also hashed; ordinary result files and output packets are preserved here.
