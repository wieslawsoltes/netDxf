#!/usr/bin/env python3
"""Verify late-metadata HATCH exports with independent ezdxf (development only).

Run: python tools/verify_hatch_pattern_order.py artifacts/conformance
The source fixtures use 37 degree rotation, scale 0.25, two nonparallel PAT lines.
This verifies exported geometry and audit results, not native AutoCAD execution.
"""
import argparse
import math
from pathlib import Path
import ezdxf


def near(actual, expected):
    return len(actual) == len(expected) and all(
        math.isfinite(a) and math.isclose(a, e, rel_tol=1e-10, abs_tol=1e-10)
        for a, e in zip(actual, expected))


def rotate(x, y, degrees, scale):
    a = math.radians(degrees)
    return (scale * (x * math.cos(a) - y * math.sin(a)),
            scale * (x * math.sin(a) + y * math.cos(a)))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    paths = sorted(args.artifacts.glob('hatch-pattern-order-*.dxf'))
    if len(paths) != 12:
        raise ValueError(f'Expected 12 fixtures, found {len(paths)}')
    expected = [
        [48.5, *rotate(1.5, -2.25, 37, .25), *rotate(.5, 2, 48.5, .25), .3125, -.1875, 0],
        [210.25, *rotate(-3, 4.5, 37, .25), *rotate(-.25, .125, 210.25, .25), .5],
    ]
    for path in paths:
        doc = ezdxf.readfile(path)
        hatches = list(doc.modelspace().query('HATCH'))
        if len(hatches) != 2:
            raise ValueError(f'{path}: expected original and clone')
        for hatch in hatches:
            if (hatch.dxf.pattern_angle, hatch.dxf.pattern_scale, hatch.dxf.pattern_type,
                    hatch.dxf.pattern_double) != (37, .25, 2, 1):
                raise ValueError(f'{path}: changed global metadata')
            if hatch.pattern is None or len(hatch.pattern.lines) != 2:
                raise ValueError(f'{path}: incorrect pattern lines')
            for line, values in zip(hatch.pattern.lines, expected):
                if not near([line.angle, *line.base_point, *line.offset, *line.dash_length_items], values):
                    raise ValueError(f'{path}: line geometry was rotated/scaled twice')
            if len(hatch.paths) != 1 or not hatch.paths[0].is_closed or len(hatch.paths[0].vertices) != 4:
                raise ValueError(f'{path}: lost counted boundary packet')
            if list(hatch.seeds) != [(2, 3)] or hatch.dxf.elevation.z != 2.5 or hatch.dxf.pixel_size != .125:
                raise ValueError(f'{path}: lost adjacent geometry')
            if list(hatch.get_xdata('DOUBLE_TEST'))[0].value != 'after pattern':
                raise ValueError(f'{path}: lost following XData')
        lines = list(doc.modelspace().query('LINE'))
        if len(lines) != 1 or tuple(lines[0].dxf.start) != (20, 30, 40):
            raise ValueError(f'{path}: lost following entity')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} errors, {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 12 files / 24 hatches / 48 rotated and scaled lines, no errors/repairs')


if __name__ == '__main__':
    main()
