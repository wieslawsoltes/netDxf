# Independent raw OBJECTS fixtures

`raw-objects-R2000.dxf` and `raw-objects-R2018.dxf` were generated independently
with ezdxf 1.4.4 for the raw OBJECTS transaction tests. They contain nested
dictionaries, same-target aliases, dictionary variables, an XRECORD default,
and an entity extension dictionary referencing a named XRECORD. The application
payload includes ordinary codes 100/102/280 and data following them. Both files
had zero errors and zero repairs when initially audited by ezdxf.

The fixtures are pinned by SHA-256 in `ObjectStoreIndependent`. Tests verify
payload tags directly; ezdxf's high-level XRECORD API splits application code100
as a subclass and does not expose the following payload, so it is insufficient
for this particular preservation assertion. Raw DXF tags remain the evidence.

These fixtures do not claim AutoCAD application or native CAD qualification.
