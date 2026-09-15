# Independent POLYFACE review

The frozen independent probes pass all **279 checks in Debug and Release** against the final semantic candidate. The original code failed 216 of those checks across the comparable completed baselines. Independent ezdxf decoding also verifies 204 saved outputs in each configuration and detects six deliberate decoded-output corruptions per corpus.

| Corpus | Original passing / failing | Candidate Debug | Candidate Release | Saved outputs checked per configuration |
| --- | --- | --- | --- | --- |
| Main: grammar, clone, writer admission | 50 / 143 (Debug) | 193 / 0 | 193 / 0 | 120 |
| Supplemental: private context and magnitude | 13 / 73 (Release) | 86 / 0 | 86 / 0 | 84 |

The original supplemental Debug run terminates at the old reader's missing-face assertion after private group70 values corrupt child classification. Its original log is preserved. The completed supplemental baseline therefore uses the unchanged Release library; candidate Debug completes the same frozen cases successfully.

The main corpus covers six versions (R2000, R2004, R2007, R2010, R2013 and R2018), ASCII and binary transports, signed one-based references, first-zero termination, missing and reordered public slots, duplicate public slots, active out-of-range references, ignored values after termination, advisory counts, and face/coordinate interleaving. API checks cover inherited null Layer/Color, independent cloned face indices and events, assigned-resource clone independence, and invalid mutable indices rejected before destination writes or handle allocation.

The supplemental corpus isolates private102 groups, nested groups, unknown subclasses, resumption at a known public subclass, and the XData boundary. Its magnitude checks establish that -32768 resolves without overflow when coordinate32768 exists, while an unreferenced coordinate32769 does not impose a new admission restriction. These checks do not claim preservation of private child metadata.

Both probes and both fixture manifests were frozen before evaluating the candidate. Their hashes still match. The separate Python output gate independently decodes saved bytes and verifies parent kind, physical child topology, signed active-prefix indices, coordinate order, SEQEND and the following sentinel LINE; it detects mutations in each of those six categories.

Production commit: `d4adade4eaeae339525321d0b9edd995a3c8cf48`. Candidate DLLs were compiled before final XML-comment and line-ending cleanup, so this report pins final semantic candidate DLLs and current source hashes without describing them as an exact build of the final commit. A rerun on the final integrated build can supply that additional identity check.

This is a spec-backed implementation and independent producer/output validation. It is not native AutoCAD AUDIT, display, or edit validation. Existing minimum mesh admission and unrelated child metadata handling remain outside this bounded review.

Primary references: [Autodesk VERTEX group codes](https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-DXF/files/GUID-0741E831-599E-4CBF-91E1-8ADBCFD6556D.htm) and [Autodesk Polyface Meshes](https://help.autodesk.com/cloudhelp/2016/ENU/AutoCAD-DXF/files/GUID-96B6288E-F413-46C0-968A-A314171C0AAE.htm).

`review-receipt.json` records exact source, DLL, frozen-input, result and output hashes. `frozen-probe.json` and `supplement/frozen-probe.json` retain their original baseline seals. `verify_probe_outputs.py CORPUS OUTPUTS` reruns the independent wire gate.
