# Independent common entity inputs

These twelve files are authored by ezdxf 1.4.4, covering R2000/R2004/R2007/R2010/R2013/R2018 in ASCII and binary form. They contain LINE, ATTDEF and ATTRIB common proxy packets. The 164-byte cache decodes through ezdxf's independent proxy parser to one six-vertex POLYLINE. Color names and shadow modes are included only in their admitted profiles. A following POINT and external XData exercise record boundaries.

Run `python tests/fixtures/common-entity-data/generate.py` from the repository root to reproduce the corpus. `manifest.json` records the producer version and file/payload hashes. The generator enables ezdxf's fixed test metadata. Regeneration must use the recorded ezdxf version when comparing byte hashes.

For binary files the generator compiles the ezdxf ASCII output with ezdxf's typed tag compiler, then writes those tags with its BinaryTagWriter. This avoids the ezdxf 1.4.4 high-level proxy exporter passing hexadecimal strings directly to its binary chunk writer. Both the producer inputs and netDxf roundtrip outputs are independently audited; the production library has no ezdxf dependency.
