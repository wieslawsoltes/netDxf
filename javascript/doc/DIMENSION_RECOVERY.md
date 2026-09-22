# Dimension continuation recovery

The September 22, 2026 continuation starts from PR #98 head
`698d83eb9eb9f0f06e82c49da6f421e68fdef71d`.

The surviving `netDxf-source-7444128.zip` archive has SHA-256
`fd966f62ae7f0687d6da8e14857d81dce35e7010ffbaffc7f9b09d5319be815d`.
Its archived checksums pass, and restoring every tracked file reproduces tree
`6e3c2082cc274a9c2ef79f42219ee4666de4b259` exactly. Running the retained
source lowerer with the pinned SDK 8.0.425 / runtime 8.0.31 reproduces the published
head tree `2f5b8896d837eab9d3d9eff12348242404c31808` exactly.

No uncommitted checkout or later integration patch survived in the available
files. The missing integration is rebuilt from the pinned sources, not described
as a byte-for-byte recovery of unavailable work. Earlier reported counts of 814
supplemental tests are not evidence for this reconstructed checkpoint.

This checkpoint exports the Dimension base, eight concrete dimension classes,
and DimensionBlock from the default and Node package entries. It fixes typed
style-string adaptation so an explicit empty BoxedString override does not
suppress the default radial R or diametric diameter symbol. The object-valued
override itself keeps the original box and reference identity.

Local checks with Node 22.16.0 on Linux: 19 new focused regressions pass; all
769 supplemental tests pass without skips or TODOs; the existing Release
dimension-style corpus matches all 3,182 scenarios / 14,341 operations against
the unchanged native assembly. These checks are not a claim that the unfinished
concrete-dimension, browser, typed-document or full-port gates pass.

The next integration must independently compare concrete dimensions and block
contents, include package/browser checks, bind generated outputs to the audited
manifest, and preserve every numerical or native-process failure. The original
C# sources, tests and fixtures remain unchanged. PR #98 stays draft and the
package stays private; no npm publication, merge or force push is requested.
