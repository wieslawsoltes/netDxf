#!/usr/bin/env python3
"""Independently check continuous gradient shift exports (development-only ezdxf).

Run: python tools/verify_hatch_gradient_shift.py artifacts/conformance
This checks metadata, not rendered interpolation or native AutoCAD behavior.
"""
import argparse
from pathlib import Path
import ezdxf


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    paths = sorted(args.artifacts.glob('hatch-gradient-shift-*.dxf'))
    if len(paths) != 10:
        raise ValueError(f'Expected 10 version/transport fixtures, found {len(paths)}')
    for path in paths:
        doc = ezdxf.readfile(path)
        hatches = list(doc.modelspace().query('HATCH'))
        if len(hatches) != 2:
            raise ValueError(f'{path}: original and exploded clone required')
        for hatch in hatches:
            gradient = hatch.gradient
            if gradient is None or gradient.kind != 1 or gradient.name != 'LINEAR':
                raise ValueError(f'{path}: gradient type changed')
            if gradient.centered != 0.5:
                raise ValueError(f'{path}: gradient shift quantized: {gradient.centered}')
            if gradient.rotation != 0.0 or gradient.one_color != 0:
                raise ValueError(f'{path}: unrelated gradient metadata changed')
            if hatch.dxf.elevation.z != 2.5 or hatch.dxf.solid_fill != 1:
                raise ValueError(f'{path}: elevation/fill changed')
            if len(hatch.paths) != 1 or not hatch.paths[0].is_closed or len(hatch.paths[0].vertices) != 4:
                raise ValueError(f'{path}: closed boundary changed')
            if list(hatch.get_xdata('DOUBLE_TEST'))[0].value != 'after pattern':
                raise ValueError(f'{path}: following XData changed')
        if [tuple(h.seeds[0]) for h in hatches] != [(2, 3), (12, 23)]:
            raise ValueError(f'{path}: original/exploded OCS seeds changed')
        lines = list(doc.modelspace().query('LINE'))
        if len(lines) != 1 or tuple(lines[0].dxf.start) != (20, 30, 40):
            raise ValueError(f'{path}: following LINE changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 10 files / 20 continuous shifts; zero audit errors/repairs')


if __name__ == '__main__':
    main()
