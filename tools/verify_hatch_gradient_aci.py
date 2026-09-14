#!/usr/bin/env python3
"""Verify gradient ACI identity/presence with independent ezdxf tag decoding.

Development only. The ezdxf 1.4.4 semantic loader associates optional ACI values
by occurrence rather than group-463 stop boundaries. Therefore second-only ACI
identity is checked against ordered tags, and that object-model caveat is reported
explicitly, not silently counted as semantic validation of both ACI slots.
"""
from __future__ import annotations
import argparse
import io
import math
from pathlib import Path
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader


def hatch_tags(data: bytes):
    # These generated fixtures contain only ASCII text; no code-page inference is
    # asserted by this feature verifier. Binary decoding is wholly ezdxf's codec.
    tags = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('ascii')))
    active = None
    for tag in tags:
        if tag.code == 0:
            if active is not None:
                yield active
            active = [] if tag.value == 'HATCH' else None
        if active is not None:
            active.append(tag)
    if active is not None:
        yield active


def indices_and_rgb(tags):
    indices, rgb, controls = [], [], []
    for tag in tags:
        if tag.code == 463:
            controls.append(float(tag.value))
            indices.append(None)
        elif tag.code == 63:
            if not indices or indices[-1] is not None:
                raise ValueError('Orphan or duplicate ACI tag')
            indices[-1] = int(tag.value)
        elif tag.code == 421:
            rgb.append(int(tag.value) & 0xFFFFFF)
    if controls != [0.0, 1.0] or rgb != [0x123456, 0xABCDEF]:
        raise ValueError('Stop boundaries or authoritative RGB values changed')
    return tuple(indices)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    versions = {'AutoCad2004': 'AC1018', 'AutoCad2007': 'AC1021', 'AutoCad2010': 'AC1024',
                'AutoCad2013': 'AC1027', 'AutoCad2018': 'AC1032'}
    expected = {f'hatch-gradient-aci-{version}-{binary}-{presence}.dxf'
                for version in versions for binary in ('False', 'True') for presence in range(4)}
    paths = sorted(args.artifacts.glob('hatch-gradient-aci-*.dxf'))
    if {path.name for path in paths} != expected:
        raise ValueError('Expected exactly 40 profile/transport/presence fixtures')
    known_reader_caveats = 0
    for path in paths:
        presence = int(path.stem.rsplit('-', 1)[1])
        indices = (17 if presence & 1 else None, 231 if presence & 2 else None)
        data = path.read_bytes()
        if data.startswith(b'AutoCAD Binary DXF') != ('-True-' in path.name):
            raise ValueError(f'{path}: transport label mismatch')
        records = list(hatch_tags(data))
        if len(records) != 2 or any(indices_and_rgb(record) != indices for record in records):
            raise ValueError(f'{path}: ordered stop-specific ACI identity/presence changed')
        doc = ezdxf.readfile(path)
        version = next(v for v in versions if v in path.name)
        if doc.dxfversion != versions[version]:
            raise ValueError(f'{path}: output profile changed')
        hatches = list(doc.modelspace().query('HATCH'))
        if len(hatches) != 2:
            raise ValueError(f'{path}: expected original and nested/exploded clone')
        for hatch in hatches:
            g = hatch.gradient
            if g is None or (g.kind, g.name, g.one_color) != (1, 'LINEAR', 1):
                raise ValueError(f'{path}: gradient kind or mode changed')
            actual = (g.aci1, g.aci2)
            if actual != indices:
                if ezdxf.__version__ == '1.4.4' and indices == (None, 231) and actual == (231, None):
                    known_reader_caveats += 1
                else:
                    raise ValueError(f'{path}: unexpected independent object-model ACI discrepancy {actual}')
            if tuple(g.color1) != (0x12, 0x34, 0x56) or tuple(g.color2) != (0xAB, 0xCD, 0xEF):
                raise ValueError(f'{path}: RGB stop changed')
            if not math.isclose(g.rotation, 37, abs_tol=1e-10) or (g.centered, g.tint) != (0.375, 0.35):
                raise ValueError(f'{path}: angle/shift/tint changed')
            if hatch.dxf.elevation.z != 2.5 or list(hatch.seeds) != [(2, 3)]:
                raise ValueError(f'{path}: elevation/seed changed')
            if len(hatch.paths) != 1 or not hatch.paths[0].is_closed or len(hatch.paths[0].vertices) != 4:
                raise ValueError(f'{path}: boundary changed')
            if [(t.code, t.value) for t in hatch.get_xdata('DOUBLE_TEST')] != [(1000, 'after pattern')]:
                raise ValueError(f'{path}: following XData changed')
        lines = list(doc.modelspace().query('LINE'))
        if len(lines) != 1 or tuple(lines[0].dxf.start) != (20, 30, 40) or tuple(lines[0].dxf.end) != (21, 31, 41):
            raise ValueError(f'{path}: following LINE changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} audit errors / {len(audit.fixes)} repairs')
    print(f'PASS ezdxf {ezdxf.__version__}: 40 files / 80 stop-specific ACI pairs and RGB gradients; zero audit errors/repairs')
    print(f'Object-model caveat: {known_reader_caveats} second-only ACI pairs are misassigned by ezdxf 1.4.4; ordered-tag identity is independently verified.')


if __name__ == '__main__':
    main()
