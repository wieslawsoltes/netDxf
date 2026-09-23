# Executing every packaged target asset

The build workflow retains all previous compilation, package and full DXF
conformance gates. It additionally executes the same installed-package smoke
assertions against each assembly in the produced nupkg. Linux runs net6.0 and
net8.0 consumers and a net8.0 consumer explicitly selecting netstandard2.0.
Windows repeats those three and runs consumers compiled for net471 and net48.
There are eight host/asset/runtime combinations, each exercising the same twelve
six-version/text-binary scenarios. This is smoke coverage, not the complete
conformance suite on every runtime.

The existing PackageSmoke still tests ordinary NuGet target selection. TargetSmoke
links its assertion body, restores only the candidate netDxf package, and explicitly
references the requested asset from that restored package. This allows testing
netstandard2.0 without silently selecting the nearer net8.0 assembly. Before
writing success evidence, the executing consumer verifies both framework metadata
and the SHA-256 of its actually loaded library against the selected nupkg entry.
Its executable target and actual CLR version are recorded separately.

Each job has an empty isolated package cache and a source-mapped NuGet configuration.
The exact netDxf.netstandard ID maps only to the downloaded candidate feed; public
sources can supply reference/targeting dependencies, never replace that candidate.
Source commit/tree and all package checksums are validated before execution.
Failed restore, build, execution, incomplete scenarios or mismatched evidence fail
the job. Missing, duplicate or extra matrix receipts fail aggregate qualification.
No process failure is inferred solely from text or hidden by a pipe.

.NET Framework 4.x updates replace the older CLR installation in place. The net471
and net48 assets therefore execute on the Windows runner's installed compatible
.NET Framework, not separate historical 4.7.1 and 4.8 installations. The registry
Release number and CLR version identify that runtime. .NET 6 is retained solely
for testing an existing legacy target; its end-of-support warning remains visible.
.NET Standard is an API target and is exercised on .NET 8, not called a runtime.

The portable predecessor-of-360 fixture uses a binary64 bit decrement on every
target; .NET 6/8 additionally compare it to Math.BitDecrement. Framework built-in
code pages are used on .NET Framework; the existing provider registration remains
on modern .NET. None of the prior package behavioral assertions is removed.

The reusable build workflow fails if any target execution fails, so a release
rehearsal cannot qualify successfully after a failed runtime job. Complete per-job
receipts, logs and aggregate evidence are retained as separate Actions artifacts;
these are not added to the existing sealed NuGet/publication bundle. No credentials,
publication switch, tag, registry package or repository permission is changed.

Execution counts belong to the PR and actual hosted logs, not this specification.
Native AutoCAD acceptance, full historical runtime qualification, every API path,
font/fit/leader rendering and complete all-version DXF parity remain unqualified.

Primary references:
- https://learn.microsoft.com/dotnet/framework/install/versions-and-dependencies
- https://learn.microsoft.com/nuget/consume-packages/package-source-mapping
