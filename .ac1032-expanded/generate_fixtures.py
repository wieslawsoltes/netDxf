"""Generate explicit multiline attribute inputs using ezdxf 1.4.4, not netDxf output."""
from pathlib import Path
import hashlib
import json
import sys
import ezdxf
from ezdxf.entities import MText


def main(directory):
    if ezdxf.__version__ != '1.4.4':
        raise RuntimeError('Fixture provenance requires ezdxf 1.4.4')
    directory.mkdir(parents=True, exist_ok=True)
    entries = []
    for long_content in (False, True):
        content = ('Independent café αβγ\\PNext line' if not long_content
                   else 'α' * 249 + 'β\\P' + '字' * 300 + ' final')
        for binary in (False, True):
            doc = ezdxf.new('R2018')
            doc.styles.new('EMBEDDED_SOURCE', dxfattribs={'font': 'txt.shx'})
            block = doc.blocks.new('INDEPENDENT_ATTRIBUTES')
            definition = block.add_attdef('VALUE', (1, 2, 3), 'Fallback definition', dxfattribs={'height': 9, 'prompt': 'Independent prompt'})
            block.add_line((0, 0), (2, 0))
            root = doc.modelspace().add_blockref(block.name, (0, 0))
            instance = root.add_attrib('VALUE', 'Fallback instance', (8, 9, 10), dxfattribs={'height': 6})
            for is_definition, host in ((True, definition), (False, instance)):
                mtext = MText.new(dxfattribs={'insert': (30, 40, 5), 'char_height': 2.5,
                    'width': 12., 'defined_height': 7., 'attachment_point': 5,
                    'style': 'EMBEDDED_SOURCE', 'text_direction': (1, 0, 0), 'extrusion': (0, 0, 1),
                    'line_spacing_style': 2, 'line_spacing_factor': 1.2})
                mtext.text = content + (' default' if is_definition else ' instance')
                host.set_mtext(mtext, graphic_properties=False)
                # The two namespaces intentionally disagree. Preserve the actual file,
                # rather than accepting an import that copies one side onto the other.
                host.dxf.text = 'Fallback definition' if is_definition else 'Fallback instance'
                host.dxf.height = 9 if is_definition else 6
                host.dxf.width = .8 if is_definition else 1.3
                host.dxf.insert = (1, 2, 3) if is_definition else (8, 9, 10)
                host.dxf.align_point = host.dxf.insert
                host.dxf.halign = 2 if is_definition else 1
                host.dxf.valign = 2 if is_definition else 1
                host.dxf.style = 'Standard'
                host.dxf.text_generation_flag = 6 if is_definition else 0
                host.set_xdata('ATTR_TEST', [(1000, 'unrelated application data'), (1070, 23)])
            doc.appids.new('ATTR_TEST')
            name = f'independent-{("long" if long_content else "short")}-{("binary" if binary else "text")}.dxf'
            path = directory / name
            doc.saveas(path, fmt='bin' if binary else 'asc')
            loaded = ezdxf.readfile(path)
            audit = loaded.audit()
            if audit.errors or audit.fixes:
                raise RuntimeError(f'Independent seed needs audit repair: {name}')
            for entity in (loaded.blocks.get(block.name).query('ATTDEF').first,
                           list(loaded.modelspace().query('INSERT'))[0].attribs[0]):
                if not entity.has_embedded_mtext_entity:
                    raise RuntimeError('Independent producer omitted embedded content')
            entries.append({'file': name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                            'binary': binary, 'long': long_content, 'content': content})
    (directory / 'manifest.json').write_text(json.dumps({'producer': 'ezdxf', 'version': ezdxf.__version__,
        'format': 'AC1032', 'native_autocad_execution': False, 'files': entries}, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')
    print('Generated and audited', len(entries), 'independent AC1032 fixtures')


if __name__ == '__main__':
    main(Path(sys.argv[1]))
