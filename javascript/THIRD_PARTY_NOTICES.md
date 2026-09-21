# Third-party code

The original netDxf port remains under the MIT license in `LICENSE`.

`runtime/reference-math/` contains native JavaScript adaptations of the IBM
Accurate Mathematical Library from GNU libc (glibc), copyright (C) 2001–2022
Free Software Foundation, Inc. These files are licensed under the **GNU Lesser
General Public License, version 2.1 or, at your option, any later version**.
The accompanying generator is distributed under the same license.

The adaptation retains separately rounded binary64 operations and explicitly
implements the fused arithmetic used by the selected sine/cosine path. C errno,
floating-point status flags and rounding-mode controls are not JavaScript APIs.
The port assumes round-to-nearest, ties-to-even arithmetic.

Full license texts are in `runtime/reference-math/LICENSE.LGPL-2.1` and
`runtime/reference-math/LICENSE.GPL-2`. Original preferred sources are included
under `third_party/glibc-math/`, along with their notices. The pin, paths and
SHA-256 hashes are recorded in `tools/ReferenceMath/source-manifest.json`.
The reproducible generator is `tools/ReferenceMath/generate.py`.

Upstream source: https://github.com/bminor/glibc/tree/f94f6d8a3572840d3ba42ab9ace3ea522c99c0c2

The package-level license expression describes the aggregate as
`MIT AND LGPL-2.1-or-later AND BSL-1.0`; the mathematical files must not be relabeled MIT.
The math implementation and its tables are readable, modifiable native source.
They perform no network access, runtime compilation, dynamic source evaluation,
WebAssembly execution or native DXF/geometry calls. JavaScript BigInt is used
internally for correctly rounded software fused multiply-add.

## Exponential and logarithm continuation

The exp/log runtime additionally adapts the hash-pinned glibc 2.35 dbl-64
`e_exp.c`, `e_log.c`, their data tables, `math_config.h`, and x86-64 FMA
selectors. The original preferred sources are retained under
`third_party/glibc-math/`, with the same LGPL-2.1-or-later license and
copyright notices. `tools/ReferenceMath/exp-log-manifest.json` pins all
seven added sources; `generate-exp-log.py --check` reproduces the three
runtime outputs from those sources and the supplied adaptation template.
The JavaScript adapter targets the existing round-to-nearest x86-64 FMA
reference profile, not every native libm or rounding mode.

## Geometric Tools surface evaluation

`netDxf/GTE/` mirrors the pinned netDxf C# translations of Geometric Tools
(David Eberly, copyright 1998-2022). Original Geometric Tools portions retain
the Boost Software License 1.0, included in `netDxf/GTE/LICENSE.BSL-1.0`.
The netDxf translation and JavaScript adaptation retain their MIT notices.
The aggregate package expression is now `MIT AND LGPL-2.1-or-later AND BSL-1.0`.

## Generic dictionary capacity sizing

The prime-size table and prime-selection helper in `runtime/GenericDictionary.js`
adapt the .NET Foundation's MIT-licensed `HashHelpers.cs`:
https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Private.CoreLib/src/System/Collections/HashHelpers.cs
The dictionary's publicly observed semantics are compared against the separately
pinned .NET 8.0.31 runtime, not inferred from the upstream table.

Copyright (c) .NET Foundation and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
