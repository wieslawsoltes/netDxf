#!/usr/bin/env python3
"""Independently verify 48 HATCH edge-packet exports using optional ezdxf.

Run: python tools/verify_hatch_edge_packets.py artifacts/conformance
Checks stored primitives, not closed-area topology; never modifies or saves files.
"""
from pathlib import Path
import argparse


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    import ezdxf
    files = sorted(args.artifacts.glob('hatch-edge-dispatch-*.dxf'))
    if len(files) != 48:
        raise ValueError(f'Expected 48 fixtures, found {len(files)}')
    for path in files:
        document = ezdxf.readfile(path)
        hatches = list(document.modelspace().query('HATCH'))
        if len(hatches) != 1 or len(hatches[0].paths) != 1 or len(hatches[0].paths[0].edges) != 1:
            raise ValueError(f'{path.name}: boundary shape changed')
        hatch = hatches[0]
        edge = hatch.paths[0].edges[0]
        kind = int(path.stem.rsplit('-', 1)[1])
        if kind == 1:
            valid = tuple(edge.start) == (0.0, 0.0) and tuple(edge.end) == (10.0, 10.0)
        elif kind == 2:
            valid = tuple(edge.center) == (1.0, 2.0) and edge.radius == 3.0 and edge.ccw and \
                edge.start_angle == 0.0 and edge.end_angle == 360.0
        elif kind == 3:
            valid = tuple(edge.center) == (1.0, 2.0) and tuple(edge.major_axis) == (3.0, 0.0) and \
                edge.ratio == 0.5 and edge.ccw and edge.start_angle == 0.0 and edge.end_angle == 360.0
        else:
            valid = edge.degree == 2 and not edge.rational and not edge.periodic and \
                edge.knot_values == [0.0, 0.0, 0.0, 1.0, 1.0, 1.0] and \
                [tuple(p) for p in edge.control_points] == [(0.0, 0.0), (5.0, 10.0), (10.0, 0.0)]
        if not valid:
            raise ValueError(f'{path.name}: edge primitive values changed')
        if list(hatch.seeds) != [(2.0, 3.0)] or hatch.dxf.elevation.z != 2.5:
            raise ValueError(f'{path.name}: surrounding HATCH fields changed')
        if [(t.code, t.value) for t in hatch.get_xdata('DOUBLE_TEST')] != [(1000, 'after pattern')]:
            raise ValueError(f'{path.name}: following XData changed')
        print(f'PASS {path.name}: edge primitive and adjacent data')
    print(f'Independent reader: ezdxf {ezdxf.__version__}; no closed-area or native AutoCAD qualification')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
