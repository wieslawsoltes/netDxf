#!/usr/bin/env python3
"""Independent ezdxf check of HATCH pixel-size fixtures. Development-only dependency.

Run the conformance suite first, then: python tools/verify_hatch_pixel_size.py artifacts/conformance
No drawings are changed, and no AutoCAD execution is implied.
"""
from pathlib import Path
import argparse
import struct
import ezdxf


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    files = sorted(args.artifacts.glob('hatch-pixel-*.dxf'))
    if len(files) != 12:
        raise ValueError(f'Expected 12 version/transport fixtures, found {len(files)}')
    for path in files:
        doc = ezdxf.readfile(path)
        hatches = list(doc.modelspace().query('HATCH'))
        if len(hatches) != 2:
            raise ValueError(f'{path}: expected original and clone')
        for hatch in hatches:
            if struct.pack('<d', hatch.dxf.pixel_size) != struct.pack('<d', 0.125):
                raise ValueError(f'{path}: pixel size changed')
            if hatch.dxf.elevation.z != 2.5 or list(hatch.seeds) != [(2.0, 3.0)]:
                raise ValueError(f'{path}: geometry/seed metadata changed')
            if len(hatch.paths) != 1 or list(hatch.get_xdata('DOUBLE_TEST'))[0].value != 'after pattern':
                raise ValueError(f'{path}: boundaries or XData changed')
        auditor = doc.audit()
        if auditor.errors or auditor.fixes:
            raise ValueError(f'{path}: {len(auditor.errors)} errors, {len(auditor.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 12 files, 24 hatches, no audit errors/repairs')


if __name__ == '__main__':
    main()
