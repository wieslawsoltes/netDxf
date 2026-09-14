"""Independent producer: ezdxf 1.4.4; never imports or invokes netDxf."""
import hashlib
import json
from pathlib import Path
import ezdxf

HERE = Path(__file__).resolve().parent
STYLE = dict(dimtsz=1.375, dimtvp=-0.625, dimupt=1, dimrnd=0.125, dimalt=0)
HEADER = {'$DIMTSZ': 0.0, '$DIMTVP': 0.875, '$DIMUPT': 0}
OVERRIDES = [dict(dimtsz=2.75, dimtvp=-1.125, dimupt=0, dimrnd=0.25, dimalt=1),
             dict(dimtsz=0.0, dimtvp=0.0, dimupt=1, dimrnd=0.5, dimalt=0)]

def main():
    assert ezdxf.__version__ == '1.4.4'
    fixtures = []
    for year in (2000, 2004, 2007, 2010, 2013, 2018):
        doc = ezdxf.new(f'R{year}')
        doc.dimstyles.new('PARITY', dxfattribs=dict(STYLE))
        doc.header['$DIMSTYLE'] = 'PARITY'
        for name, value in HEADER.items(): doc.header[name] = value
        msp = doc.modelspace()
        handles = []
        for index, override in enumerate(OVERRIDES):
            # Render the geometry first, then set exactly the stored DSTYLE payload.
            dim = msp.add_linear_dim(base=(0, 3+index*4), p1=(0, 0), p2=(10, 0), dimstyle='PARITY')
            dim.render()
            dim.dimension.set_acad_dstyle(override)
            handles.append(dim.dimension.dxf.handle)
        line = msp.add_line((17.25, -4.5, 2.0), (18.5, 9.25, -3.0))
        path = HERE / f'independent-dimstyle-R{year}.dxf'
        doc.saveas(path)
        loaded = ezdxf.readfile(path)
        audit = loaded.audit()
        assert not audit.errors and not audit.fixes
        for name, value in STYLE.items(): assert loaded.dimstyles.get('PARITY').dxf.get(name) == value
        for name, value in HEADER.items(): assert loaded.header[name] == value
        for handle, override in zip(handles, OVERRIDES):
            values = loaded.entitydb[handle].get_acad_dstyle(loaded.dimstyles.get('PARITY'))
            assert values == override, values
        fixtures.append(dict(file=path.name, sha256=hashlib.sha256(path.read_bytes()).hexdigest(), year=year,
                             dimension_handles=handles, line_handle=line.dxf.handle))
    (HERE/'manifest.json').write_text(json.dumps(dict(producer='ezdxf 1.4.4', style=STYLE, header=HEADER, overrides=OVERRIDES, fixtures=fixtures), indent=2)+'\n')

if __name__ == '__main__': main()
