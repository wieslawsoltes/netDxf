#!/usr/bin/env python3
"""Unchanged ezdxf 1.4.4 POLYFACE producer files with coordinate and face metadata."""
import hashlib
import io
import json
from pathlib import Path
import ezdxf
ROOT = Path(__file__).resolve().parent


def drawing(year):
    doc = ezdxf.new(f'R{year}')
    doc.appids.new('POLYFACE_META')
    for name in ('COORDINATE_ONLY', 'FACE_ONLY', 'END_ONLY'): doc.layers.new(name)
    doc.linetypes.new('FACE_DASH', dxfattribs={'description': 'face record resource', 'pattern': [0.75, 0.5, -0.25]})
    mesh = doc.modelspace().add_polyface()
    points = [(1, 2, 3), (4, 7, 11), (8, 12, 17), (13, 19, 23), (29, 31, 37), (41, 43, 47)]
    mesh.append_faces([points[:4], [points[3], points[4], points[5]]])
    coordinates = [v for v in mesh.vertices if not v.is_face_record]
    faces = [v for v in mesh.vertices if v.is_face_record]
    faces[0].dxf.vtx0 = -1; faces[0].dxf.vtx2 = -3; faces[1].dxf.vtx1 = -5
    first = coordinates[0]; first.dxf.layer = 'COORDINATE_ONLY'; first.dxf.color = 0
    first.dxf.ltscale = 1.75; first.dxf.lineweight = 35; first.dxf.invisible = 1
    first.dxf.start_width = 0; first.dxf.end_width = 2.5; first.dxf.vertex_identifier = 17
    first.set_xdata('POLYFACE_META', [(1000, 'coordinate metadata'), (1004, bytes([0, 1, 127, 255])), (1005, faces[0].dxf.handle)])
    first.set_reactors([faces[0].dxf.handle, coordinates[1].dxf.handle])
    face = faces[0]; face.dxf.layer = 'FACE_ONLY'; face.dxf.linetype = 'FACE_DASH'; face.dxf.color = 2
    face.dxf.ltscale = 2.25; face.dxf.lineweight = 40
    if year >= 2004:
        face.dxf.true_color = 0x123456; face.dxf.color_name = 'face color'; face.dxf.transparency = 0x020000FE
    face.set_reactors([first.dxf.handle])
    face.set_xdata('POLYFACE_META', [(1000, 'face metadata'), (1004, bytes([5, 0, 255])), (1005, coordinates[2].dxf.handle)])
    record = faces[1].new_extension_dict().add_xrecord('FACE_DATA')
    record.reset([(1, 'owned face metadata'), (330, first.dxf.handle), (310, bytes([5, 0, 255]))])
    end = mesh.seqend; end.dxf.layer = 'END_ONLY'; end.dxf.color = 0; end.set_reactors([face.dxf.handle])
    end.set_xdata('POLYFACE_META', [(1000, 'sequence metadata'), (1004, bytes([255, 0, 4]))])
    end_record = end.new_extension_dict().add_xrecord('SEQEND_DATA'); end_record.reset([(1, 'owned terminator'), (330, end.dxf.handle)])
    plain = doc.modelspace().add_polyface(); plain.append_faces([[(101, 102, 103), (104, 107, 111), (108, 112, 117), (113, 119, 123)]])
    plain_coordinates = [v for v in plain.vertices if not v.is_face_record]; plain_faces = [v for v in plain.vertices if v.is_face_record]
    plain_faces[0].dxf.layer = 'FACE_ONLY'; plain_faces[0].dxf.linetype = 'FACE_DASH'; plain_faces[0].dxf.color = 0
    plain_faces[0].set_xdata('POLYFACE_META', [(1000, 'clone face metadata'), (1004, bytes([1, 2, 255]))])
    plain_faces[0].dxf.vtx1 = -2; plain_coordinates[0].dxf.layer = 'COORDINATE_ONLY'; plain.seqend.dxf.color = 0
    doc.update_all(); doc.classes.classes = dict(sorted(doc.classes.classes.items()))
    return doc, {'mesh': mesh.dxf.handle, 'coordinates': [v.dxf.handle for v in coordinates], 'faces': [v.dxf.handle for v in faces], 'seqend': end.dxf.handle,
        'face_xrecord': record.dxf.handle, 'seqend_xrecord': end_record.dxf.handle, 'block_record': doc.modelspace().block_record_handle,
        'plain_mesh': plain.dxf.handle, 'plain_coordinates': [v.dxf.handle for v in plain_coordinates], 'plain_faces': [v.dxf.handle for v in plain_faces], 'plain_seqend': plain.seqend.dxf.handle}


def main():
    assert ezdxf.__version__ == '1.4.4'
    ezdxf.options.write_fixed_meta_data_for_testing = True
    fixtures = []
    for year in (2000, 2004, 2007, 2010, 2013, 2018):
        for binary in (False, True):
            doc, handles = drawing(year); stream = io.BytesIO() if binary else io.StringIO()
            doc.write(stream, fmt='bin' if binary else 'asc')
            data = stream.getvalue() if binary else stream.getvalue().encode(doc.output_encoding, errors='dxfreplace')
            path = ROOT / f"producer-R{year}-{'binary' if binary else 'ascii'}.dxf"; path.write_bytes(data)
            audit = ezdxf.readfile(path).audit(); assert not audit.errors and not audit.fixes
            fixtures.append({'file': path.name, 'year': year, 'binary': binary, 'sha256': hashlib.sha256(data).hexdigest(), 'handles': handles})
    (ROOT / 'manifest.json').write_text(json.dumps({'producer': 'ezdxf 1.4.4', 'source': 'https://github.com/mozman/ezdxf/tree/v1.4.4',
        'unchanged_producer_bytes': True, 'native_application_execution': False, 'owner_form': 'coordinate and face VERTEX -> containing BLOCK_RECORD; SEQEND -> POLYLINE', 'fixtures': fixtures}, indent=2) + '\n')


if __name__ == '__main__': main()
