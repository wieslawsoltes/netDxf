#!/usr/bin/env python3
"""Verify inert OLE2FRAME persistence; never activates or decodes native OLE data."""
import argparse
from pathlib import Path
import struct
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts', type=Path)
    args = parser.parse_args()
    files = sorted(args.artifacts.glob('ole2frame-*.dxf'))
    names = {f'ole2frame-AutoCad{v}-{b}.dxf' for v in (2000, 2004, 2007, 2010, 2013, 2018) for b in ('False', 'True')}
    if {p.name for p in files} != names:
        raise ValueError('Exactly twelve OLE2FRAME profile/transport fixtures required')
    expected = bytes(i * 131 % 256 for i in range(255))
    for path in files:
        doc = ezdxf.readfile(path)
        entities = list(doc.modelspace().query('OLE2FRAME'))
        if len(entities) != 2:
            raise ValueError(f'{path}: original and clone required')
        for entity in entities:
            if entity.binary_data() != expected:
                raise ValueError(f'{path}: opaque bytes changed')
            tags = entity.acdb_ole2frame
            for code, value in ((70, 2), (71, 2), (72, 0), (90, 255), (1, 'OLE'), (3, 'Picture Żółć')):
                actual = tags.get_first_value(code)
                if code == 3: actual = decode_dxf_unicode(actual)
                if actual != value:
                    raise ValueError(f'{path}: field {code} changed: {tags.get_first_value(code)!r}')
            upper = tags.get_first_value(10)
            if struct.pack('<d', upper[0]) != struct.pack('<d', 1.0000000000000002) or tuple(upper)[1:] != (6, -2):
                raise ValueError(f'{path}: upper corner precision changed')
            if tuple(tags.get_first_value(11)) != (8, -4, -2):
                raise ValueError(f'{path}: lower corner changed')
            if [len(t.value) for t in tags if t.code == 310] != [127, 127, 1]:
                raise ValueError(f'{path}: binary chunk framing changed')
            if entity.dxf.color != 5 or list(entity.get_xdata('OLE_TEST'))[0].value != 'after binary data':
                raise ValueError(f'{path}: adjacent metadata changed')
        line = list(doc.modelspace().query('LINE'))
        if len(line) != 1 or tuple(line[0].dxf.start) != (10, 20, 30):
            raise ValueError(f'{path}: following LINE changed')
        audit = doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} graph errors / {len(audit.fixes)} repairs')
        print(f'PASS {path.name}')
    print(f'PASS ezdxf {ezdxf.__version__}: 12 files / 24 inert frames; exact payloads, zero graph audit errors/repairs')
    print('Opaque sample bytes are not certified as valid native OLE objects. No payload activation or native rendering was performed.')


if __name__ == '__main__':
    main()
