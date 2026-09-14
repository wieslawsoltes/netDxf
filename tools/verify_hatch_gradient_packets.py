#!/usr/bin/env python3
"""Independently validate normalized gradient-packet exports; development only."""
import argparse
import math
import struct
from pathlib import Path
import ezdxf


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    paths = sorted(parser.parse_args().artifacts.glob('hatch-gradient-packet-*.dxf'))
    if len(paths) != 10:
        raise ValueError(f'Expected ten profile/transport fixtures, found {len(paths)}')
    for path in paths:
        doc = ezdxf.readfile(path)
        hatches = list(doc.modelspace().query('HATCH'))
        if len(hatches) != 2:
            raise ValueError(f'{path}: expected original and cloned HATCH')
        for hatch in hatches:
            g = hatch.gradient
            if g is None or g.kind != 1 or g.name != 'LINEAR' or g.one_color != 1:
                raise ValueError(f'{path}: wrong gradient kind/name/dialog mode')
            if tuple(g.color1) != (0x12, 0x34, 0x56) or tuple(g.color2) != (0xAB, 0xCD, 0xEF):
                raise ValueError(f'{path}: authored RGB stops changed')
            if not math.isclose(g.rotation, 37, abs_tol=1e-10) or g.centered != 0.375:
                raise ValueError(f'{path}: angle/shift changed')
            if struct.pack('<d', g.tint) != struct.pack('<d', 0.35):
                raise ValueError(f'{path}: authored binary64 tint changed')
            if list(hatch.seeds) != [(2, 3)] or hatch.dxf.elevation.z != 2.5:
                raise ValueError(f'{path}: seeds/elevation changed')
            if len(hatch.paths) != 1 or not hatch.paths[0].is_closed or len(hatch.paths[0].vertices) != 4:
                raise ValueError(f'{path}: boundary changed')
            if hatch.get_xdata('DOUBLE_TEST')[0].value != 'after pattern':
                raise ValueError(f'{path}: following XData changed')
        lines = list(doc.modelspace().query('LINE'))
        if len(lines) != 1 or tuple(lines[0].dxf.start) != (20, 30, 40):
            raise ValueError(f'{path}: following LINE changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 10 files / 20 gradients; zero audit errors/repairs')


if __name__ == '__main__':
    main()
