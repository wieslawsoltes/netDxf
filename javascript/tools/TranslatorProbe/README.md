# Whole-source compiler feasibility probe

This development-only experiment submits **all 510 unmodified C# source files** to an independently maintained native JavaScript compiler. It evaluates whether existing compiler infrastructure can accelerate the remaining typed database and original test translation. Its outputs are written only to ignored artifacts, never into the production `netDxf/` JavaScript mirror, and never counted as API/file/test parity.

Pinned experiment packages: `Transpose.Compiler.Library` 26.9.5288 and `Transpose.BCL` 26.9.4872. The compiler package is Apache-2.0 licensed; see the package metadata and retained package notices before adopting or redistributing anything. This does **not** add a production JavaScript dependency. The production lowerer, source pin, and .NET 8 differential oracle remain unchanged.

The probe uses a .NET 10 development host. The workflow records the resolved SDK/runtime and dependency lockfile. No claim of a repeatable accepted production toolchain is made from this experiment. Compilation success alone is insufficient: public casing, exact source-file layout, overload dispatch, value semantics, the complete original test suite, binary/text exact DXF comparisons, encoding/numerics, portability, and performance must all be qualified before any adoption.

References: https://docs.curiosity.ai/transpose/reference/nuget-packages and https://www.nuget.org/packages/Transpose.Compiler.Library/26.9.5288 .
