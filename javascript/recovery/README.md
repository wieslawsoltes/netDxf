# Recovery of the local foundations checkpoint

The unpushed local checkpoint `fb10b22859441e960b28d0f17ac37ac367b64d6a` was based on `e02555dd78324d873cc12edd94d3c9e01c0ac35d`. Before recovery the PR independently advanced to `72e84d05deb9bf6613f98897809b3f9e303202b8`, adding another implementation of the same geometry classes and new collection/formatting work.

This integration preserves the newer reproducible NativePort-generated geometry implementations and their manifest, rather than replacing them with older handwritten alternatives. The saved and current implementations were reconciled through the recovered stateful .NET differential corpus. The complete original 56-file cumulative patch is retained here as `foundations-fb10b22.patch.xz`, outside the production package. Decompress it with `xz -dc foundations-fb10b22.patch.xz > foundations.patch`; the decompressed SHA-256 is `7773e68f1eadcb236be68544e0bf1043edd3fd8679cb9c937db8f56549d7eef1`. It must only be applied to its original base, not blindly to this branch. Historical alternatives are not included as a second production implementation.

Recovered into active code: UnitHelper, XDataRecord, all 625 actual Decimal-derived unit conversion factors, four original raw-test files, and the additional 5,185-scenario/49,421-operation foundation corpus. The corpus now calls the current implementations through explicit overload and struct-copy adapters. It also exposed missing DxfClassCollection.TryGetValue, nullable-item overloads and duplicate-key exception metadata, which are corrected here.

The adjacent historical summary describes the original local run only. Its GitHub push status, counts, fingerprints, platform and failures are **not current verification results**. Fresh evidence is generated from the integrated tree. All original alternative files are committed in that recovery patch, while only the reconciled implementations ship.

Compressed archive SHA-256: `bd4da4bb2e3bafac5a32f04a21c448788db2b9bfa9d1e7afc5c7f449a13f4d06`. This binary artifact is recovery source data, never imported or executed by the port.
