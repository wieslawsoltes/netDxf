#!/usr/bin/env python3
"""Independently verify authored gradient color pairs, tint and mode (ezdxf, dev only)."""
import argparse
import math
from pathlib import Path
import struct
import ezdxf


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    paths = sorted(args.artifacts.glob('hatch-gradient-state-*.dxf'))
    if len(paths) != 20:
        raise ValueError(f'Expected 20 profile/transport/mode outputs; found {len(paths)}')
    for path in paths:
        doc = ezdxf.readfile(path)
        expected_single = path.stem.endswith('-True')
        hatches = list(doc.modelspace().query('HATCH'))
        if len(hatches) != 2:
            raise ValueError(f'{path}: expected original and exploded clone')
        seeds = []
        for hatch in hatches:
            g = hatch.gradient
            if g is None or g.kind != 1 or g.name != 'LINEAR' or g.number_of_colors != 2:
                raise ValueError(f'{path}: gradient framing changed')
            if g.one_color != int(expected_single) or struct.pack('<d', g.tint) != struct.pack('<d', 0.35):
                raise ValueError(f'{path}: dialog mode/tint lost')
            if tuple(g.color1) != (0x12, 0x34, 0x56) or tuple(g.color2) != (0xAB, 0xCD, 0xEF):
                raise ValueError(f'{path}: authored RGB pair recomputed')
            if g.centered != 0.375 or not math.isclose(g.rotation, 37, abs_tol=1e-10):
                raise ValueError(f'{path}: shift or rotation lost')
            if hatch.dxf.elevation.z != 2.5 or len(hatch.paths) != 1 or not hatch.paths[0].is_closed:
                raise ValueError(f'{path}: boundary or elevation changed')
            if len(hatch.paths[0].vertices) != 4 or len(hatch.seeds) != 1:
                raise ValueError(f'{path}: boundary/seed count changed')
            seeds.append(tuple(hatch.seeds[0]))
            if list(hatch.get_xdata('DOUBLE_TEST'))[0].value != 'after pattern':
                raise ValueError(f'{path}: following XData lost')
        if sorted(seeds) != [(2, 3), (12, 23)]:
            raise ValueError(f'{path}: clone/explosion coordinate data lost')
        lines = list(doc.modelspace().query('LINE'))
        if len(lines) != 1 or tuple(lines[0].dxf.start) != (20, 30, 40):
            raise ValueError(f'{path}: following LINE changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} audit errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 20 files / 40 authored gradient color states; zero audit errors/repairs')


if __name__ == '__main__':
    main()
