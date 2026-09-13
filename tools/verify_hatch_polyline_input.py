#!/usr/bin/env python3
"""Independently check twelve sparse-bulge HATCH exports with optional ezdxf.

Run: python tools/verify_hatch_polyline_input.py artifacts/conformance
Values are checked before audit; this script never modifies or saves the files.
"""
from pathlib import Path
import argparse


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    import ezdxf
    files = sorted(args.artifacts.glob('hatch-sparse-bulges-*.dxf'))
    if len(files) != 12:
        raise ValueError(f'Expected twelve version/transport fixtures, found {len(files)}')
    expected = [(0.0, 0.0, 0.5), (10.0, 0.0, 0.0), (10.0, 10.0, 0.5), (0.0, 10.0, 0.0)]
    for path in files:
        document = ezdxf.readfile(path)
        hatches = list(document.modelspace().query('HATCH'))
        if len(hatches) != 1:
            raise ValueError(f'{path.name}: HATCH count changed')
        hatch = hatches[0]
        if len(hatch.paths) != 1:
            raise ValueError(f'{path.name}: boundary count changed')
        boundary = hatch.paths[0]
        if not boundary.is_closed or list(boundary.vertices) != expected:
            raise ValueError(f'{path.name}: sparse bulges or closure changed')
        if list(hatch.seeds) != [(2.0, 3.0)] or hatch.dxf.elevation.z != 2.5:
            raise ValueError(f'{path.name}: adjacent fields changed')
        if [(t.code, t.value) for t in hatch.get_xdata('DOUBLE_TEST')] != [(1000, 'after pattern')]:
            raise ValueError(f'{path.name}: following XData changed')
        audit = document.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path.name}: {len(audit.errors)} errors, {len(audit.fixes)} repairs')
        print(f'PASS {path.name}: sparse bulges and adjacent fields; no repairs')
    print(f'Independent reader: ezdxf {ezdxf.__version__}')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
