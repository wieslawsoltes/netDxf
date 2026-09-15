# HATCH source boundary relation evidence

The 24 input drawings cover DXF R2000, R2004, R2007, R2010, R2013 and R2018 in
ASCII and binary transports. Each drawing contains a following modelspace LINE
from `(1, 2, 3)` to `(4, 5, 6)`, plus an unreferenced block named
`OTHER_SOURCE_BLOCK` whose LINE `403` provides a source from a different owner
for rejection tests.

The 12 `native-*` drawings contain the native `HATCH 24F` and `LWPOLYLINE 8E`
entity packets extracted from the version-matched original
`tests/fixtures/dimassoc/originals-gzip/example_YEAR.dxf.gz`. The originals are
pinned to LibreDWG/libredwg commit
`34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43`; the manifest records original gzip,
decompressed source, git blob, extracted packet and adapted packet hashes.
These are native entity packets in minimal independently generated carriers,
not claims that the entire original native document was parsed or reproduced.
The entity-level owner `330` changes from `1F` to carrier modelspace
BLOCK_RECORD `17`. The original layer `Tavolo 3` is defined in the carrier.
All other native entity text pairs remain unchanged in the ASCII fixture,
including source list `[8E]`, six stored line edges, associative flag `1`,
and the source's persistent reactor `24F`. Binary fixtures serialize the same
typed tags with ezdxf's binary tag writer. Following LINE `300` is a candidate
with the same owner as the native HATCH.

The 12 `producer-*` drawings are actual ezdxf 1.4.4 API output. Both HATCHes
contain an edge path with four LINE edges and a closed polyline path with
stored nonzero bulges. The HATCH source lists intentionally contain duplicates
and share entities across paths and HATCHes. All listed source entities belong
to modelspace and both HATCHes have associative flag `1`.

| HATCH | Edge path sources, flags `1` | Polyline path sources, flags `18` |
| --- | --- | --- |
| `3A2` | `3A0`, `3A0`, `3A1` | `3A1`, `3A0` |
| `3A3` | `3A1`, `3A0`, `3A1` | `3A0`, `3A0` |

The sources `3A0` and `3A1` are LWPOLYLINE entities. Following LINE `3A4`
is an additional candidate with the same owner. These packets qualify stored
source identity, count, ordering, duplicate preservation and owner closure;
their source geometry need not describe a uniquely evaluable associative
construction.

Run `python tests/fixtures/hatch-source-relations/generate.py` from the
repository root to regenerate. The script pins ezdxf 1.4.4, enables fixed
metadata and reexecutes with Python hash seed `0` to stabilize CLASS ordering.
The manifest pins the generator, independent verifier/helper, producer modules,
every input file, all original sources and the expected stored inventory.
All generated inputs independently reload with zero audit errors or repairs.

After the C# conformance suite writes output drawings, run
`python tools/verify_hatch_source_relations.py ARTIFACT_DIRECTORY`.
The gate requires all 48 filenames
`hatch-source-{native|producer}-AutoCadYEAR-{inputBinary}-{outputBinary}.dxf`
with Boolean tokens `False` or `True`. It checks actual transport signatures,
version, exact source identities/counts/order/duplicates, raw path flags and
geometry, independent typed associations, actual common block ownership,
following geometry and zero audit errors/repairs. It also directly compares
the fixture native packets with the original gzipped source packets, allowing
only the declared owner adaptation. Three negative controls corrupt actual
ASCII output bytes by dropping a duplicate and adjusting its count, flipping
the associative flag, or substituting a different valid source identity.
Each must fail the same verification used for positive outputs. Missing a
mandatory output fails before a success receipt can be printed.

Native CAD execution, graphical rendering, curve evaluation and propagation
of geometry changes through associative relationships are outside this scope.
