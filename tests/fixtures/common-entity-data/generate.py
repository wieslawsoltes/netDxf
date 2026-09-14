"""Independent ezdxf 1.4.4 producer; run from the repository root to regenerate."""
from pathlib import Path
import hashlib
import json
import io
import struct
import ezdxf
from ezdxf.proxygraphic import ProxyGraphic
from ezdxf.lldxf.tagger import ascii_tags_loader, tag_compiler
from ezdxf.lldxf.tagwriter import BinaryTagWriter

ROOT = Path(__file__).parent
VERTICES = [(float(i), float(i * i), 0.0) for i in range(6)]
body = struct.pack('<L', len(VERTICES)) + b''.join(struct.pack('<3d', *v) for v in VERTICES)
PROXY = b'\0' * 8 + struct.pack('<2L', len(body) + 8, 6) + body
assert len(PROXY) == 164
assert len(list(ProxyGraphic(PROXY).virtual_entities())) == 1

def decorate(entity, year):
    entity.proxy_graphic = PROXY
    if year >= 2004: entity.dxf.color_name = 'ACME$青'
    if year >= 2007: entity.dxf.shadow_mode = 3


def main():
    ezdxf.options.store_proxy_graphics = True
    ezdxf.options.load_proxy_graphics = True
    ezdxf.options.write_fixed_meta_data_for_testing = True
    manifest = {'producer': 'ezdxf ' + ezdxf.__version__, 'proxy_sha256': hashlib.sha256(PROXY).hexdigest(), 'files': {}}
    for year in (2000, 2004, 2007, 2010, 2013, 2018):
        doc = ezdxf.new('R' + str(year))
        doc.appids.new('COMMON_DATA_QA')
        line = doc.modelspace().add_line((-2, 3, 4), (5, -6, 7))
        decorate(line, year)
        line.set_xdata('COMMON_DATA_QA', [(1000, 'after common data'), (1070, 73)])
        block = doc.blocks.new('COMMON_BLOCK')
        definition = block.add_attdef('TAG', (1, 2), text='original')
        decorate(definition, year)
        block.add_circle((0, 0), 2)
        insert = doc.modelspace().add_blockref('COMMON_BLOCK', (10, 20))
        insert.add_auto_attribs({'TAG': 'insert value'})
        decorate(insert.attribs[0], year)
        doc.modelspace().add_point((123, 456, 789))
        for binary in (False, True):
            name = f'ezdxf-common-R{year}-{binary}.dxf'
            path = ROOT / name
            if not binary:
                doc.saveas(path, fmt='asc')
            else:
                # ezdxf1.4.4 proxy export passes hex strings to its binary writer.
                # Compile the independent ASCII output through ezdxf's low-level
                # typed tag compiler before binary encoding to avoid that producer bug.
                source = ROOT / f'ezdxf-common-R{year}-False.dxf'
                tags = tag_compiler(ascii_tags_loader(io.StringIO(source.read_text(encoding=doc.output_encoding))))
                with path.open('wb') as stream:
                    writer = BinaryTagWriter(stream, dxfversion=doc.dxfversion, encoding=doc.output_encoding)
                    writer.write_signature()
                    writer.write_tags(tags)
            audit = ezdxf.readfile(path).audit()
            assert not audit.errors and not audit.fixes
            manifest['files'][name] = hashlib.sha256(path.read_bytes()).hexdigest()
    (ROOT / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print('Generated12 independent drawings; valid proxy polyline command; zero audit errors/repairs.')

if __name__ == '__main__': main()
