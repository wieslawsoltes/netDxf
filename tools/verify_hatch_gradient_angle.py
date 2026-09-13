#!/usr/bin/env python3
"""Check retained gradient-angle exports with independent ezdxf (development only).

Run: python tools/verify_hatch_gradient_angle.py artifacts/conformance
Checks angle and adjacent drawing structure, not complete gradient/color semantics.
"""
import argparse
import math
from pathlib import Path
import ezdxf


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    paths = sorted(args.artifacts.glob('hatch-gradient-angle-*.dxf'))
    if len(paths) != 10:
        raise ValueError(f'Expected 10 gradient-capable version/transport files, found {len(paths)}')
    for path in paths:
        doc = ezdxf.readfile(path)
        hatches = list(doc.modelspace().query('HATCH'))
        if len(hatches) != 2:
            raise ValueError(f'{path}: original and clone required')
        for hatch in hatches:
            gradient = hatch.gradient
            if gradient is None or gradient.kind != 1 or gradient.name != 'LINEAR':
                raise ValueError(f'{path}: missing gradient')
            if not math.isclose(gradient.rotation, 37, abs_tol=1e-10, rel_tol=1e-10):
                raise ValueError(f'{path}: gradient angle overwritten: {gradient.rotation}')
            if hatch.dxf.solid_fill != 1 or list(hatch.seeds) != [(2, 3)] or hatch.dxf.elevation.z != 2.5:
                raise ValueError(f'{path}: adjacent HATCH metadata lost')
            if len(hatch.paths) != 1 or not hatch.paths[0].is_closed or len(hatch.paths[0].vertices) != 4:
                raise ValueError(f'{path}: gradient boundary changed')
            if list(hatch.get_xdata('DOUBLE_TEST'))[0].value != 'after pattern':
                raise ValueError(f'{path}: following XData lost')
        lines = list(doc.modelspace().query('LINE'))
        if len(lines) != 1 or tuple(lines[0].dxf.start) != (20, 30, 40):
            raise ValueError(f'{path}: following entity lost')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 10 files / 20 gradient angles; zero audit errors/repairs')


if __name__ == '__main__':
    main()
