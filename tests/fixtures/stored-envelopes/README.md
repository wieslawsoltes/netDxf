# Synthetic stored-object envelopes

These 24 committed files are independently authored public-schema envelopes. They are not native AutoCAD spatial indexes or embedded VBA projects.

| Kind | Profiles | Transports | Files |
| --- | --- | --- | --- |
| SPATIAL_INDEX | 2000, 2004, 2007, 2010, 2013, 2018 | ASCII and binary | 12 |
| VBA_PROJECT | 2000, 2004, 2007, 2010, 2013, 2018 | ASCII and binary | 12 |

Run `python tests/fixtures/stored-envelopes/generate.py` with ezdxf 1.4.4 to reproduce the files and manifest. The generator uses fixed metadata, creates ownership/reactor/extension/XData graphs through ezdxf, and writes independently of netDxf.

SPATIAL_INDEX has no typed ezdxf producer. The generator registers common-metadata XRECORD shells, then replaces their payloads with the published `AcDbIndex` / timestamp 40 / empty `AcDbSpatialIndex` schema. The two timestamps are `2451544.5000000005` and negative zero. This replacement is explicit schema authoring, not native index generation.

For VBA_PROJECT, the generator first uses ezdxf's native byte-envelope exporter. It then applies an explicit raw patch to physical chunks, retaining the declared count and concatenated bytes. The main 300-byte pattern has chunk lengths `[0, 127, 3, 0, 127, 43, 0]`; the two empty payloads have zero chunks and two empty chunks respectively. ezdxf's binary tag writer omits zero-length byte strings, so the patch writes those group-310 records with an explicit zero length. The resulting files parse independently with zero audits. The synthetic bytes are never executed or interpreted as VBA.

`manifest.json` pins every file by SHA256 and records source profiles, transport, timestamp bits or exact chunks, object handles, owner, extension dictionary, following XRECORD and LINE identities. `corpus-assessment.json` records an independent search of 14 upstream DXF files at the pinned Git commit: neither object had an actual OBJECTS instance; SPATIAL_INDEX hits were CLASS declarations. See [the module qualification](../../../doc/dxf-conformance/stored-envelopes.md) for primary sources and limits.

The independent verifier `python tools/verify_stored_envelopes.py <artifacts>` requires all 48 cross-transport roundtrips and 12 authored exports. It checks exact public payloads and common graph relationships. It uses ezdxf's VBA loader only to compare concatenated bytes: that loader ignores count 90, and its clone implementation is not an oracle.
