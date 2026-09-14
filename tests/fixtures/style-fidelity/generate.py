"""Independent STYLE fixtures from ezdxf 1.4.4; no netDxf code is used."""
from pathlib import Path
import hashlib
import io
import json
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, tag_compiler
from ezdxf.lldxf.tagwriter import TagWriter, BinaryTagWriter

ROOT = Path(__file__).parent
YEARS = (2000, 2004, 2007, 2010, 2013, 2018)
FONT_FLAGS = -216910762  # signed 0xF3123456; complete lower and unknown upper bits
FAMILY = 'Independent 青'

def main():
    ezdxf.options.write_fixed_meta_data_for_testing = True
    manifest = {'producer': 'ezdxf ' + ezdxf.__version__, 'files': {}}
    for year in YEARS:
        doc = ezdxf.new(f'R{year}')
        doc.appids.new('STYLE_QA')
        rich = doc.styles.new('QA_RICH', dxfattribs={'font': 'Arial.ttf', 'flags': 0x4074, 'generation_flags': 0x4006, 'height': 1.25, 'last_height': 9.75})
        rich.set_xdata('ACAD', [(1000, FAMILY), (1071, FONT_FLAGS), (1002, '{'), (1000, 'unrelated suffix'), (1071, 73), (1004, b'\x00\xff\x19'), (1002, '}')])
        rich.set_xdata('STYLE_QA', [(1000, 'external style data'), (1070, 19)])
        doc.styles.new('QA_SHX', dxfattribs={'font': 'asia.shx', 'bigfont': 'bigfont.shx', 'flags': 128, 'generation_flags': 8, 'last_height': 0})
        empty = doc.styles.new('QA_EMPTY', dxfattribs={'font': '', 'last_height': 0})
        empty.set_xdata('ACAD', [(1000, ''), (1071, 0)])
        absent = doc.styles.new('QA_ABSENT', dxfattribs={'font': 'extensionless'})
        absent.set_xdata('ACAD', [(1002, '{'), (1000, 'not a font'), (1071, 1234), (1002, '}')])
        shape = doc.styles.add_shx('qa-shapes.shx', dxfattribs={'flags': 0x4175, 'generation_flags': 0x4016, 'last_height': 3.125})
        shape.dxf.flags = 0x4175
        shape.dxf.last_height = 3.125
        shape.set_xdata('STYLE_QA', [(1000, 'shape data')])
        doc.modelspace().add_text('Independent STYLE references', dxfattribs={'style': 'QA_RICH', 'height': 2.5})
        doc.modelspace().add_mtext('SHX reference', dxfattribs={'style': 'QA_SHX'})
        doc.modelspace().add_point((123, 456, 789))
        stream = io.StringIO(); doc.write(stream)
        tags = list(tag_compiler(ascii_tags_loader(io.StringIO(stream.getvalue()))))
        # Group42 is optional. Remove only this selected record's tag from the independent export.
        records=[]; current=[]
        for tag in tags:
            if tag.code == 0 and current: records.append(current); current=[]
            current.append(tag)
        if current: records.append(current)
        tags=[]
        for record in records:
            if record[0].value == 'STYLE' and any(t.code == 2 and t.value == 'QA_ABSENT' for t in record):
                record = [t for t in record if t.code != 42]
            tags.extend(record)
        for binary in (False, True):
            path = ROOT / f'ezdxf-style-R{year}-{binary}.dxf'
            if binary:
                with path.open('wb') as out:
                    writer = BinaryTagWriter(out, dxfversion=doc.dxfversion, encoding=doc.output_encoding)
                    writer.write_signature(); writer.write_tags(tags)
            else:
                with path.open('w', encoding=doc.output_encoding, errors='dxfreplace', newline='') as out:
                    TagWriter(out, dxfversion=doc.dxfversion).write_tags(tags)
            audit = ezdxf.readfile(path).audit()
            assert not audit.errors and not audit.fixes, (path, audit.errors, audit.fixes)
            manifest['files'][path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    (ROOT / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print('12 independent STYLE fixtures; zero audit errors or repairs.')

if __name__ == '__main__': main()
