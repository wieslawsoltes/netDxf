# Hosted drawing-header evidence — c2d3581

[Header workflow 35577650194](https://github.com/wieslawsoltes/netDxf/actions/runs/35577650194) completed successfully for commit `c2d35810443788fad62ffc5be45d6001b9161964`, tree `7cc18a81ed25f19f18c25065ab19a1c206c9ba6e`. All four jobs compile the unchanged pinned production assembly, pass **665 scenarios / 6,596 exact operations**, and pass the **28 focused header/time tests**. This is header qualification, not full C# port completion.

The four artifact ZIPs were downloaded, SHA-256 checked and their result reports inspected. Every report has `completed: true`, zero failures and no fatal error. The runtime fingerprint matches the local tested source on all platforms. Platform-specific verifier digests are recorded below; path ordering in the existing fingerprint routine differs on Windows.

| Platform | Configuration | Artifact | ZIP SHA-256 |
| --- | --- | ---: | --- |
| Ubuntu 22.04 | Release | 10628925403 | `97e1fa7d92f5c8f8b4cbdd5263b52482cf57bada2b50e4ad983a3971645e9da6` |
| Ubuntu 22.04 | Debug | 10627659191 | `5eabe5dc135dc254ab89e60e129c2671f9edaad90526ee113ded5b279d4393bf` |
| Windows 2022 | Release | 10628394971 | `c1b31dba51f6917d07121464b12067ba2f7a011367be1e40bd8df8a3d52a5ce0` |
| Windows 2022 | Debug | 10628768696 | `320ac7683752335581866ef84a5a67c9e70b3e216c657c6fb459f5ccb0c26f5d` |

Runtime fingerprint: `5a253e79e2675b8635f8f60f82a4eaceedf847da14e58eb425c05bcfa1f63f14`.

POSIX verifier: `35fe90660169c6d8fbcb61bb62636ed7ca439ff8540a4cc8e5220b58b2fbed76`.

Windows verifier: `3d1f0bad39742828516c99e0cb2c955ae97271395ffd4d99731ea4bfbc9bddcb`.

The C# source pin, SDK/runtime, field contracts, explicit clock/username treatment, local browser results, full-suite accounting and remaining failures are documented in [HEADERS.md](HEADERS.md). Live constructor clocks and unavailable browser operating-system identity remain explicit host adaptations. Broader JavaScript workflow `35577650021` is independent evidence; its pending or failing categories cannot be inferred to pass from this focused matrix. Later documentation-only commits do not replace the executable source commit identified above.
