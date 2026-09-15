# Independent membership runtime review

This is the unchanged independent harness and receipt produced after source
review, together with its exact result/audit JSON and compressed output drawings.
Each configuration passes 44 cases and audits all 12 outputs with zero errors and
zero repairs. The SHA256 manifest describes the uncompressed output bytes.

The reviewer copied the frozen production DLLs from the membership worktree when
its commit was `9fc30e0ec50903a00a19211a6d2226db24336964`. Those DLLs embed
`3.0.1+9fc30e0...` in AssemblyInformationalVersion. The module qualification later
rebuilt the same production source after test-only commits, embedding
`3.0.1+4f7c5e7...`. Their assembly hashes therefore differ; each receipt identifies
its actual reviewed binary, and no identical-binary claim is made.

The harness accepts the library path through MSBuild `DxfReviewLibrary`. Its
command-line arguments are the repository root and output directory. It uses
the pinned native SECTION source already in that repository. The original
result records contain the review's local workspace paths.
