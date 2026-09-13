#!/usr/bin/env python3
"""Verify all 32 HATCH boundary flag combinations in twelve exported fixtures.

Run: python tools/verify_hatch_boundary_flags.py artifacts/conformance
Requires optional ezdxf; does not modify or save drawings.
"""
from pathlib import Path
import argparse


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    import ezdxf
    files = sorted(args.artifacts.glob('hatch-path-flags-*.dxf'))
    if len(files) != 12:
        raise ValueError(f'Expected twelve fixtures, found {len(files)}')
    for path in files:
        document = ezdxf.readfile(path)
        hatches = list(document.modelspace().query('HATCH'))
        if len(hatches) != 32:
            raise ValueError(f'{path.name}: expected 32 classified hatches')
        for flags, hatch in enumerate(hatches):
            if len(hatch.paths) != 1:
                raise ValueError(f'{path.name}: path count changed at {flags}')
            boundary = hatch.paths[0]
            if boundary.path_type_flags != flags:
                raise ValueError(f'{path.name}: flags {flags} became {boundary.path_type_flags}')
            if flags & 2:
                if not boundary.is_closed or len(boundary.vertices) != 4:
                    raise ValueError(f'{path.name}: polyline representation changed at {flags}')
            elif len(boundary.edges) != 4:
                raise ValueError(f'{path.name}: edge representation changed at {flags}')
            if list(hatch.seeds) != [(2.0, 3.0)] or hatch.dxf.elevation.z != 2.5:
                raise ValueError(f'{path.name}: adjacent fields changed at {flags}')
        audit = document.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path.name}: {len(audit.errors)} errors, {len(audit.fixes)} repairs')
        print(f'PASS {path.name}: all 32 classifications; no repairs')
    print(f'Independent reader: ezdxf {ezdxf.__version__}')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
