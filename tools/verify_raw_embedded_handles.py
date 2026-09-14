#!/usr/bin/env python3
"""Verify preserved embedded-object tails with ezdxf's independent tag decoders.

Run: python tools/verify_raw_embedded_handles.py artifacts/conformance
These synthetic opaque payloads are checked at tag level, not by semantic AUDIT.
"""
import argparse
import io
from pathlib import Path
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    files = sorted(args.artifacts.glob('handles-embedded-*.dxf'))
    names = {f'handles-embedded-{b}-{c}.dxf' for b in ('False', 'True') for c in ('False', 'True')}
    if {p.name for p in files} != names:
        raise ValueError('Expected all four transport/embedded-control fixtures')
    count = 0
    for path in files:
        data = path.read_bytes()
        tags = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('ascii'), newline=None))
        pairs = [(t.code, cast_tag_value(t.code, t.value)) for t in tags]
        start = pairs.index((0, 'MTEXT'))
        end = next(i for i in range(start + 1, len(pairs)) if pairs[i][0] == 0)
        marker = pairs.index((101, 'Embedded Object'), start, end)
        expected = [(70, 1), (330, 'FA'), (340, 'FE'), (360, '20'), (5, '30'), (320, '40'),
                    (1001, 'EMBEDDED_APP'), (1005, '20')]
        if path.stem.endswith('-True'):
            expected += [(102, '{ACAD_REACTORS'), (330, '20'), (102, '}'),
                         (102, 'application payload, not a control'), (100, 'AcDbEntity'),
                         (101, 'Embedded Object'), (5, 'A'), (360, 'FE')]
        if pairs[marker + 1:end] != expected:
            raise ValueError(f'{path}: opaque embedded data changed or was omitted')
        if pairs[end:end + 6] != [(0, 'POINT'), (5, 'B1'), (330, '10'), (100, 'AcDbEntity'), (100, 'AcDbPoint'), (10, 1.0)]:
            raise ValueError(f'{path}: unrelated remap or following record changed')
        if (1001, 'REAL_XDATA') not in pairs[end:] or (1005, '20') not in pairs[end:]:
            raise ValueError(f'{path}: following-record XData lost')
        if pairs[start + 1:marker] != [(5, 'A'), (102, '{ACAD_REACTORS'), (330, '20'), (102, '}'),
                                       (330, '10'), (100, 'AcDbEntity'), (100, 'AcDbMText'), (340, '20')]:
            raise ValueError(f'{path}: enclosing-object references changed')
        count += len(expected)
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 4 files / {count} exact ordered embedded tags; tag-level verification only')


if __name__ == '__main__':
    main()
