#!/usr/bin/env python3
"""Check atomically saved drawing contents; not a cross-platform filesystem proof."""
import argparse, struct
from pathlib import Path
import ezdxf

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('artifacts',type=Path);args=parser.parse_args()
    files=sorted(args.artifacts.glob('atomic-save-*.dxf'))
    expected={f'atomic-save-AutoCad{v}-{b}-{e}.dxf' for v in (2000,2004,2007,2010,2013,2018) for b in ('False','True') for e in ('False','True')}
    if {p.name for p in files} != expected: raise ValueError('Expected exactly 24 version/format/destination fixtures')
    versions={'2000':'AC1015','2004':'AC1018','2007':'AC1021','2010':'AC1024','2013':'AC1027','2018':'AC1032'}
    for path in files:
        doc=ezdxf.readfile(path);lines=list(doc.modelspace().query('LINE'))
        if len(lines)!=1: raise ValueError(f'{path}: LINE count')
        if struct.pack('<d',lines[0].dxf.start.x)!=struct.pack('<d',1e-20) or tuple(lines[0].dxf.start)[1:]!=(2,3) or tuple(lines[0].dxf.end)!=(4,5,6):raise ValueError(f'{path}: geometry changed')
        if doc.dxfversion!=versions[path.name.split('-')[2][7:]]:raise ValueError(f'{path}: wrong DXF version')
        audit=doc.audit()
        if audit.errors or audit.fixes:raise ValueError(f'{path}: {len(audit.errors)} errors/{len(audit.fixes)} repairs')
        print('PASS',path.name)
    print(f'PASS ezdxf {ezdxf.__version__}: 24 atomic-save drawings; zero audit errors/repairs')
if __name__=='__main__':main()
