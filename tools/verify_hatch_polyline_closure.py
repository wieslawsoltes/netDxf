#!/usr/bin/env python3
"""Verify 12 HATCH closure fixtures with the optional ezdxf development package.

Run the C# conformance suite, then:
    python tools/verify_hatch_polyline_closure.py artifacts/conformance
This script never modifies or saves the drawings.
"""
from pathlib import Path
import argparse


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    import ezdxf
    files = sorted(args.artifacts.glob('hatch-closure-*.dxf'))
    if len(files) != 12:
        raise ValueError(f'Expected 12 version/transport fixtures, found {len(files)}')
    expected = [(0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0)]
    for path in files:
        document = ezdxf.readfile(path)
        hatches = list(document.modelspace().query('HATCH'))
        if len(hatches) != 2:
            raise ValueError(f'{path.name}: expected original and cloned HATCH')
        for hatch in hatches:
            if len(hatch.paths) != 1:
                raise ValueError(f'{path.name}: path count changed')
            boundary = hatch.paths[0]
            if not boundary.is_closed or [(v[0], v[1]) for v in boundary.vertices] != expected:
                raise ValueError(f'{path.name}: closed polyline geometry changed')
        audit = document.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path.name}: {len(audit.errors)} errors, {len(audit.fixes)} repairs')
        print(f'PASS {path.name}: both closed boundaries; no audit errors/repairs')
    print(f'Independent reader: ezdxf {ezdxf.__version__}')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
