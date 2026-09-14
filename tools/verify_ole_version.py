#!/usr/bin/env python3
"""Verify legacy OLEFRAME version presence through ezdxf's ordered tag storage.

The payload is synthetic: this verifies metadata/transport, not native OLE validity.
"""
import argparse
from pathlib import Path
import ezdxf


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    files = sorted(args.artifacts.glob('oleversion-*.dxf'))
    expected = {f'oleversion-AutoCad{v}-{binary}-{present}.dxf'
                for v in (2000, 2004, 2007, 2010, 2013, 2018)
                for binary in ('False', 'True') for present in ('False', 'True')}
    if {p.name for p in files} != expected:
        raise ValueError('Expected exactly 24 profile/transport/presence fixtures')
    for path in files:
        present = path.stem.endswith('-True')
        doc = ezdxf.readfile(path)
        frames = list(doc.modelspace().query('OLEFRAME'))
        if len(frames) != 2:
            raise ValueError(f'{path}: original and independent clone required')
        for frame in frames:
            packet = next(s for s in frame.xtags.subclasses
                          if s and s[0].code == 100 and s[0].value == 'AcDbOleFrame')
            versions = [t.value for t in packet if t.code == 70]
            if versions != ([1] if present else []):
                raise ValueError(f'{path}: version presence/value changed: {versions}')
            if [t.value for t in packet if t.code == 90] != [128]:
                raise ValueError(f'{path}: byte count changed')
            payload = b''.join(t.value for t in packet if t.code == 310)
            if payload != bytes((i * 131) % 256 for i in range(128)):
                raise ValueError(f'{path}: private bytes changed')
            if [t.value for t in packet if t.code == 1] != ['OLE']:
                raise ValueError(f'{path}: required terminator changed')
            if frame.xtags.get_subclass('AcDbEntity').get_first_value(62) != 5 or list(frame.get_xdata('LEGACY_OLE'))[0].value != 'after legacy bytes':
                raise ValueError(f'{path}: neighboring metadata changed')
        lines = list(doc.modelspace().query('LINE'))
        if len(lines) != 1 or tuple(lines[0].dxf.start) != (10, 20, 30):
            raise ValueError(f'{path}: following LINE changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 24 files / 48 exact legacy version-presence states; zero graph errors/repairs')


if __name__ == '__main__':
    main()
