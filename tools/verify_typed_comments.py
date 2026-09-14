#!/usr/bin/env python3
"""Verify comment-interleaved ASCII inputs with independent ezdxf (development only)."""
import argparse
from pathlib import Path
import ezdxf


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    paths = sorted(args.artifacts.glob('typed-comments-*.dxf'))
    names = {f'typed-comments-AutoCad{year}.dxf' for year in (2000, 2004, 2007, 2010, 2013, 2018)}
    if {p.name for p in paths} != names:
        raise ValueError('Expected exactly six comment-interleaved ASCII inputs')
    for path in paths:
        # Fixture presence is checked separately from independent semantic interpretation.
        lines = path.read_text(encoding='utf-8-sig').splitlines()
        if sum(line.strip() == '999' for line in lines[::2]) < 50:
            raise ValueError(f'{path}: missing interstitial comment coverage')
        doc = ezdxf.readfile(path)
        hatches = list(doc.modelspace().query('HATCH'))
        entities = list(doc.modelspace().query('LINE'))
        if len(hatches) != 1 or len(entities) != 1:
            raise ValueError(f'{path}: entity count changed')
        hatch = hatches[0]
        if hatch.dxf.pattern_name != 'U' or hatch.dxf.pattern_double != 1 or hatch.dxf.elevation.z != 2.5:
            raise ValueError(f'{path}: HATCH scalar fields changed')
        if list(hatch.seeds) != [(2, 3)] or len(hatch.paths) != 1:
            raise ValueError(f'{path}: HATCH counted data changed')
        boundary = hatch.paths[0]
        if not boundary.is_closed or [tuple(p[:2]) for p in boundary.vertices] != [(0, 0), (10, 0), (10, 10), (0, 10)]:
            raise ValueError(f'{path}: boundary changed')
        actual = [(line.angle, tuple(line.base_point), tuple(line.offset), list(line.dash_length_items)) for line in hatch.pattern.lines]
        expected = [(0.0, (1.5, -2.25), (0.5, 2.0), [1.25, -0.75, 0.0]),
                    (0.0, (-3.0, 4.5), (-0.25, 0.125), [2.0])]
        if actual != expected or list(hatch.get_xdata('DOUBLE_TEST'))[0].value != 'after pattern':
            raise ValueError(f'{path}: pattern or XData changed')
        if tuple(entities[0].dxf.start) != (20, 30, 40) or tuple(entities[0].dxf.end) != (21, 31, 41):
            raise ValueError(f'{path}: following LINE changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 6 comment-interleaved inputs; zero audit errors/repairs')


if __name__ == '__main__':
    main()
