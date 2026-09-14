#!/usr/bin/env python3
"""Verify OLE2FRAME optional-field presence independently using ezdxf's stored tags.
The opaque payload is synthetic, inert and not validated as an OLE compound object.
"""
import argparse
from pathlib import Path
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifacts',type=Path)
    args=parser.parse_args()
    files=sorted(args.artifacts.glob('olemetadata-*.dxf'))
    masks=(0,1,2,4,8,16,32,63)
    names={f'olemetadata-AutoCad{v}-{b}-{m}.dxf' for v in (2000,2004,2007,2010,2013,2018)
           for b in ('False','True') for m in masks}
    if {p.name for p in files}!=names:
        raise ValueError('Expected exactly 96 profile/transport/presence fixtures')
    payload=bytes(i*131%256 for i in range(33))
    sentinel=object()
    expected=(2,'Picture Żółć',(1.0000000000000002,6,-2),(8,-4,-2),3,0)
    for path in files:
        mask=int(path.stem.rsplit('-',1)[1])
        doc=ezdxf.readfile(path)
        frames=list(doc.modelspace().query('OLE2FRAME'))
        if len(frames)!=2:raise ValueError(f'{path}: missing original or clone')
        for frame in frames:
            tags=frame.acdb_ole2frame
            for bit,code in enumerate((70,3,10,11,71,72)):
                value=tags.get_first_value(code,sentinel)
                if bool(mask&(1<<bit)) != (value is not sentinel):
                    raise ValueError(f'{path}: absent/default field confused at {code}')
                if value is sentinel:continue
                if code==3:value=decode_dxf_unicode(value)
                if code in (10,11):value=tuple(value)
                if value!=expected[bit]:raise ValueError(f'{path}: present field {code} changed')
            if frame.binary_data()!=payload or tags.get_first_value(90)!=33 or tags.get_first_value(1)!='OLE':
                raise ValueError(f'{path}: required binary packet changed')
            if list(frame.get_xdata('OLE_TEST'))[0].value!='after binary data' or frame.dxf.color!=5:
                raise ValueError(f'{path}: adjacent data changed')
        lines=list(doc.modelspace().query('LINE'))
        if len(lines)!=1 or tuple(lines[0].dxf.start)!=(10,20,30):
            raise ValueError(f'{path}: following LINE changed')
        audit=doc.audit()
        if audit.errors or audit.fixes:
            raise ValueError(f'{path}: {len(audit.errors)} structural errors / {len(audit.fixes)} repairs')
        print('PASS',path.name)
    print(f'PASS ezdxf {ezdxf.__version__}: 96 drawings / 192 optional-field packets; zero structural DXF audit errors/repairs; OLE semantics not evaluated')


if __name__=='__main__':
    main()
