# LAYER_INDEX application-packet fixtures

The twelve `ixmilia-*.dxf.gz` files contain unchanged output from the checked-in
producer using IxMilia.Dxf 0.8.4. `manifest.json` pins the package archive,
compressed files and decoded bytes with SHA-256 hashes. The separately named
schema source commit documents the generated object's field model; it is not
asserted to be the package's build commit, which its NuGet metadata does not name.

These originals write two LAYER_INDEX objects and four hard-owned IDBUFFER
objects. Names, handles and counts are grouped arrays in ordinal correspondence.
The buffers contain 3, 0, 1 and 1 references, with a repeated entity in the first
buffer. Both text and binary originals are present for each current export profile.

The original producer draws have unrelated invalid DIMSTYLE/STYLE scaffolding
that prevents their complete typed import, and one known root dictionary owner
repair reported by ezdxf. The original files are retained as source evidence;
whole-file native or netDxf interoperability is not claimed.

Run `python tests/fixtures/layer-index/extract_fixtures.py` from the repository to
recreate the `extracted-*.dxf` inputs. This uses independent ezdxf 1.4.4 scaffolding
and inserts all six source application records with explicit 5/330/360 handle
mapping. It asserts exact application packet equality after mapping and zero
audit errors or repairs. Generated CLASS declarations are sorted; source packet
order is unchanged. Outputs are deterministic across Python hash seeds.

The extraction inputs qualify public stored fields, counts and ownership. They
do not qualify native AutoCAD index construction, regeneration or evaluation.
