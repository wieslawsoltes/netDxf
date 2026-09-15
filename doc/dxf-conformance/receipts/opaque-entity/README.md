# Focused standalone opaque entity qualification

The [module receipt](../../opaque-entity-qualification.json) identifies the exact production source, test checkpoint, runtime libraries and case counts. `debug-results.json` and `release-results.json` contain all 528 passing case names and are byte-identical. The separate `source-identity-results.json` records ten existing regression cases in Debug.

The packet archive contains 144 exact files under `debug/` and `release/`: each configuration has 12 ordinary declared inputs, 24 unchanged packet outputs, 12 escaped CLASS inputs and 24 escaped CLASS outputs. `packet-manifest.json` records every original byte length and SHA-256. Archive members were read back and hash-checked after packaging.

Extract the archive to a new directory and run the mandatory physical gate against each configuration:

```sh
tar -xzf opaque-entity-qualified-packets.tar.gz -C /absolute/path/new-output
python tools/verify_opaque_entities.py /absolute/path/new-output/debug
python tools/verify_opaque_entities.py /absolute/path/new-output/release
```

Run the Python commands from the repository root. Each gate checks 24 complete original/written unknown packets, 24 literal CLASS outputs, and 16 deliberate corruptions. Stored gate logs capture both successful runs. The immutable source packet tests are declared-schema evidence; the corpus supplied no native standalone unknown entity sample.

The build-log archive contains successful Debug, Release and all-target builds; its manifest preserves their byte hashes. The all-target build occurred after a test-only commit, so assembly source-revision metadata differs from the exact earlier runtime-qualified libraries. The receipt records both sets of hashes and the identical production source subtree. These build hashes do not replace the exact Debug/Release hashes named by the independent review.
