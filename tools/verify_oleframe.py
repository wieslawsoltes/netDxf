#!/usr/bin/env python3
"""Verify legacy OLEFRAME transport with independent ezdxf tag storage, never native activation."""
import argparse
from pathlib import Path
import ezdxf


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    paths = sorted(args.artifacts.glob('oleframe-*.dxf'))
    expected_names = {f'oleframe-AutoCad{v}-{b}.dxf' for v in (2000, 2004, 2007, 2010, 2013, 2018) for b in ('False', 'True')}
    if {p.name for p in paths} != expected_names:
        raise ValueError('Expected exactly twelve legacy OLEFRAME fixtures')
    expected = bytes(i * 131 % 256 for i in range(1025))
    for path in paths:
        doc = ezdxf.readfile(path)
        frames = list(doc.modelspace().query('OLEFRAME'))
        if len(frames) != 2:
            raise ValueError(f'{path}: original and clone required')
        for frame in frames:
            # ezdxf has no semantic legacy OLEFRAME model; inspect its retained tags explicitly.
            tags = frame.xtags.get_subclass('AcDbOleFrame')
            chunks = [tag.value for tag in tags if tag.code == 310]
            if b''.join(chunks) != expected or [len(c) for c in chunks] != [127] * 8 + [9]:
                raise ValueError(f'{path}: payload identity or framing changed')
            if tags.get_first_value(70) != 7 or tags.get_first_value(90) != 1025 or tags.get_first_value(1) != 'OLE':
                raise ValueError(f'{path}: stored metadata or terminator changed')
            if frame.xtags.get_subclass('AcDbEntity').get_first_value(62) != 5:
                raise ValueError(f'{path}: common color changed')
            if list(frame.get_xdata('LEGACY_OLE'))[0].value != 'after legacy bytes':
                raise ValueError(f'{path}: following XData changed')
        lines = list(doc.modelspace().query('LINE'))
        if len(lines) != 1 or tuple(lines[0].dxf.start) != (10, 20, 30):
            raise ValueError(f'{path}: following entity changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} graph errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 12 drawings / 24 legacy frame packets, exact bytes, zero graph audit errors/repairs')
    print('Legacy entity checks use ordered tag storage, not a semantic OLE model or native payload validation.')


if __name__ == '__main__':
    main()
