# Independent LWPOLYLINE fidelity corpus

These six committed text DXF files were authored by a separate verification agent using ezdxf 1.4.4. No netDxf writer was used. They cover R2000 and R2018, each with absent, explicitly zero, and positive constant width. Variable widths deliberately conflict with the positive constant width and preserve separate absence versus zero per component. R2018 also contains vertex identifiers 0, -17, 2147483647, and 42.

`generate_fixtures.py` creates an ezdxf document and replaces only its LWPOLYLINE subclass with the explicit packet. That patch is necessary because ezdxf's typed LWPOLYLINE model neither retains group91 nor individual optional40/41 presence. Both the patched source and the reloaded geometry were checked using ezdxf; all sources audit with zero errors and repairs. The manifest pins SHA-256 hashes and expected values. The corpus includes bulged geometry, thickness/elevation, trailing XData, and a following LINE.

The generator documents the production recipe. DXF document metadata includes generated timestamps/GUIDs, so regeneration is semantically equivalent but need not reproduce identical bytes; the committed manifest pins this exact corpus. Run generation only intentionally with ezdxf1.4.4 and review changed fixtures/manifest together.

The normal C# suite loads all six sources and emits both transports. `tools/verify_lwpolyline_fidelity.py` checks source hashes, raw field presence and values, independent geometry, and audit results. Signed zero is treated as numeric zero; byte-for-byte DXF formatting is not claimed.

See `doc/dxf-conformance/lwpolyline-fidelity.md` for primary references, effective-width precedence qualification, public API, and version restrictions.
