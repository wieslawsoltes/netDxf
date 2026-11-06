# Historical contracts and audits

The former conformance folder mixed API contracts with per-PR narratives,
dated gap lists, qualification JSON, generated comparisons, logs and compressed
outputs. Those records remain available unchanged in Git history. The topic
guides in the [index](README.md) are the maintained documentation.

| Historical material | Immutable snapshot |
|---|---|
| Feature notes and detailed packet/geometry contracts before consolidation | [Conformance directory at `8ee6418`](https://github.com/wieslawsoltes/netDxf/tree/8ee6418acea8fb0c91777aeaa68db073317e2421/doc/dxf-conformance) |
| September coverage ledger and generated version comparison | [Ledger](https://github.com/wieslawsoltes/netDxf/blob/8ee6418acea8fb0c91777aeaa68db073317e2421/doc/dxf-conformance/coverage.json), [comparison](https://github.com/wieslawsoltes/netDxf/blob/8ee6418acea8fb0c91777aeaa68db073317e2421/doc/dxf-conformance/version-feature-matrix.md) |
| Previous major-gap assessment and its recorded implementation baseline | [Gap report](https://github.com/wieslawsoltes/netDxf/blob/8ee6418acea8fb0c91777aeaa68db073317e2421/doc/dxf-conformance/remaining-major-gaps.md) |
| Review receipts and standalone probes removed by earlier cleanup passes | [Reports at `1dcbe751`](https://github.com/wieslawsoltes/netDxf/tree/1dcbe75182ea5b61e1bf07aca118f2f95a0de806/doc/dxf-conformance), [tools](https://github.com/wieslawsoltes/netDxf/tree/1dcbe75182ea5b61e1bf07aca118f2f95a0de806/tools) |

The ledger records a September 2026 implementation snapshot, not current feature
coverage. Its source pins, counts and qualification boundaries have not been
rewritten to describe a newer tree. Later feature notes also have their own
source identities. Read the note's implementation revision and subsequent code
before treating an old limitation as current.

To inspect a former file locally without restoring the old documentation tree:

```sh
git show 8ee6418:doc/dxf-conformance/atomic-file-save.md
git show 8ee6418:doc/dxf-conformance/coverage.json
```

To reproduce an old probe or regenerate the old matrix, use a separate checkout
of the relevant commit with its recorded dependencies and inputs. The generator
at `8ee6418` expects the historical ledger and its local evidence files. Recreating
that report does not qualify the current branch. Current execution and package
evidence comes from the [verification workflow](verification.md).
