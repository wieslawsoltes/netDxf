#!/usr/bin/env python3
"""Independent ezdxf verification of counted HATCH pattern-list fixtures.

Development-only dependency: ezdxf 1.4.4. Run the conformance tests first, then
python tools/verify_hatch_pattern_lists.py artifacts/conformance.
No fixture is modified, and no native AutoCAD execution is implied.
"""
from pathlib import Path
import argparse
import struct
import ezdxf


def exact(actual: float, expected: float) -> bool:
    return struct.pack('<d', actual) == struct.pack('<d', expected)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    files = sorted(args.artifacts.glob('hatch-pattern-lists-*.dxf'))
    if len(files) != 12:
        raise ValueError(f'Expected 12 version/transport fixtures, found {len(files)}')
    expected_lines = [
        (0.0, (1.5, -2.25), (0.5, 2.0), (1.25, -0.75, 0.0)),
        (0.0, (-3.0, 4.5), (-0.25, 0.125), (2.0,)),
    ]
    for path in files:
        doc = ezdxf.readfile(path)
        hatches = list(doc.modelspace().query('HATCH'))
        if len(hatches) != 2:
            raise ValueError(f'{path}: expected original and clone')
        for hatch in hatches:
            if (hatch.dxf.pattern_angle, hatch.dxf.pattern_scale, hatch.dxf.pattern_type,
                    hatch.dxf.pattern_double) != (0.0, 1.0, 2, 1):
                raise ValueError(f'{path}: pattern metadata changed')
            if hatch.pattern is None or len(hatch.pattern.lines) != 2:
                raise ValueError(f'{path}: wrong pattern line count')
            for line, (angle, origin, delta, dashes) in zip(hatch.pattern.lines, expected_lines):
                actual = [line.angle, *line.base_point, *line.offset, *line.dash_length_items]
                expected = [angle, *origin, *delta, *dashes]
                if len(actual) != len(expected) or not all(exact(a, e) for a, e in zip(actual, expected)):
                    raise ValueError(f'{path}: pattern geometry/dash bits changed')
            if hatch.dxf.elevation.z != 2.5 or list(hatch.seeds) != [(2.0, 3.0)]:
                raise ValueError(f'{path}: elevation/seed metadata changed')
            if len(hatch.paths) != 1 or list(hatch.get_xdata('DOUBLE_TEST'))[0].value != 'after pattern':
                raise ValueError(f'{path}: boundaries or following XData changed')
        lines = list(doc.modelspace().query('LINE'))
        if len(lines) != 1 or tuple(lines[0].dxf.start) != (20, 30, 40) or tuple(lines[0].dxf.end) != (21, 31, 41):
            raise ValueError(f'{path}: following LINE changed')
        auditor = doc.audit()
        if auditor.errors or auditor.fixes:
            raise ValueError(f'{path}: {len(auditor.errors)} errors, {len(auditor.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 12 files, 24 hatches, 48 pattern lines, no audit errors/repairs')


if __name__ == '__main__':
    main()
