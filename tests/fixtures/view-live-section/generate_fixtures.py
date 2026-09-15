#!/usr/bin/env python3
"""Actual ezdxf VIEW writer with an explicitly authored SECTIONOBJECT carrier.

The VIEW group 334 is assigned through the producer's public DXF namespace.
The SECTIONOBJECT is supplied as uninterpreted tags because ezdxf does not model
its geometry. No native AutoCAD source or application execution is claimed.
"""
import hashlib
import json
from pathlib import Path
import ezdxf
from ezdxf.entities.dxfentity import DXFTagStorage
from ezdxf.lldxf.extendedtags import ExtendedTags

def main():
    assert ezdxf.__version__ == '1.4.4'
    root = Path(__file__).resolve().parent
    entries = []
    for year in (2007, 2010, 2013, 2018):
        doc = ezdxf.new('R' + str(year))
        # A declared synthetic carrier, not an independent SECTION codec claim.
        packet = [(0,'SECTIONOBJECT'), (100,'AcDbEntity'), (8,'0'),
                  (100,'AcDbSection'), (90,1), (91,0), (1,'Carrier section'),
                  (10,0.0), (20,0.0), (30,1.0), (40,10.0), (41,-5.0),
                  (70,50), (62,7), (92,2), (11,-4.0), (21,0.0), (31,0.0),
                  (11,4.0), (21,0.0), (31,0.0), (93,0), (360,'0')]
        text = ''.join(f'{code}\n{value}\n' for code,value in packet)
        section = DXFTagStorage.load(ExtendedTags.from_text(text), doc)
        doc.modelspace().add_entity(section)
        section.dxf.owner = doc.modelspace().block_record_handle
        view = doc.views.new('LiveSectionProducer')
        view.dxf.live_selection_handle = section.dxf.handle
        empty = doc.views.new('NullSectionProducer')
        empty.dxf.live_selection_handle = '0'
        doc.views.new('AbsentSectionProducer')
        # The producer writes its default elevation even without UCS enablement.
        # Request a complete associated UCS through the producer API so these
        # fixtures also satisfy netDxf's established conditional UCS grammar.
        for named_view in doc.views:
            named_view.dxf.ucs = 1
            named_view.dxf.ucs_origin = (0, 0, 0)
            named_view.dxf.ucs_xaxis = (1, 0, 0)
            named_view.dxf.ucs_yaxis = (0, 1, 0)
            named_view.dxf.ucs_ortho_type = 0
        for binary in (False, True):
            path = root / f'ezdxf-view-live-section-R{year}-{"binary" if binary else "ascii"}.dxf'
            doc.saveas(path, fmt='bin' if binary else 'asc')
            restored = ezdxf.readfile(path)
            assert restored.views.get('LiveSectionProducer').dxf.live_selection_handle == section.dxf.handle
            assert restored.views.get('NullSectionProducer').dxf.live_selection_handle == '0'
            assert not restored.views.get('AbsentSectionProducer').dxf.hasattr('live_selection_handle')
            audit = restored.audit()
            assert not audit.errors and not audit.fixes
            entries.append({'path':path.name, 'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
                            'profile':doc.dxfversion, 'binary':binary,
                            'view_handle':view.dxf.handle, 'section_handle':section.dxf.handle,
                            'audit_errors':0, 'audit_repairs':0})
    (root/'manifest.json').write_text(json.dumps({'producer':'ezdxf 1.4.4',
        'view_producer_api':'views.new + dxf.live_selection_handle',
        'section_carrier':'Explicitly authored SECTIONOBJECT packet stored by DXFTagStorage',
        'native_cad_execution':False, 'fixtures':entries}, indent=2)+'\n')
    print(json.dumps({'fixtures':len(entries),'audit_errors':0,'audit_repairs':0}))

if __name__ == '__main__': main()
