#!/usr/bin/env python3
"""Generate independent MULTILEADER/MLEADERSTYLE fixtures with ezdxf1.4.4.

Builder-created geometry is augmented through ezdxf's public entity/context
models to expose nested breaks, redundant independent values and handle edges.
These are storage fixtures; no native CAD appearance qualification is implied.
"""
from pathlib import Path
import hashlib
import io
import json
import math
import ezdxf
from ezdxf import colors
from ezdxf.entities.mleader import ArrowHeadData
from ezdxf.lldxf.tagger import ascii_tags_loader, tag_compiler
from ezdxf.math import Vec2, Vec3, Matrix44
from ezdxf.render.mleader import ConnectionSide, TextAlignment, BlockAlignment

ROOT = Path(__file__).resolve().parent
YEARS = (2007, 2010, 2013, 2018)


def json_value(value):
    if isinstance(value, (Vec2, Vec3)):
        return list(value)
    if isinstance(value, bytes):
        return {"hex": value.hex()}
    if isinstance(value, dict):
        return {key: json_value(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [json_value(item) for item in value]
    if hasattr(value, "__dict__"):
        return json_value(vars(value))
    return value


def captured_tags(path, handles):
    result, current, handle = {}, [], None
    for tag in tag_compiler(ascii_tags_loader(io.StringIO(path.read_text(), newline=None))):
        if tag.code == 0:
            if handle in handles:
                result[handle] = current
            current, handle = [], None
        if tag.code == 5 and handle is None:
            handle = tag.value
        current.append([tag.code, json_value(tag.value)])
    return result


def generate(year):
    doc = ezdxf.new(f"R{year}")
    doc.appids.new("QA_MLEADER")
    text_style = doc.styles.new("QA_TEXT", dxfattribs={"font": "txt.shx", "width": .875})
    linetype = doc.linetypes.new("QA_LEADER", dxfattribs={"description": "independent dashed leader", "pattern": [.75, .5, -.25]})
    content = doc.blocks.new("QA_MLEADER_CONTENT")
    content.add_lwpolyline([(-2, -1), (3, -1), (3, 2), (-2, 2)], close=True)
    attdef = content.add_attdef("LABEL", insert=(.25, .5), text="default", dxfattribs={"height": .75, "style": text_style.dxf.name})
    arrow = doc.blocks.new("QA_MLEADER_ARROW")
    arrow.add_solid([(0, 0), (-1, -.3), (-1, .3)])
    style = doc.mleader_styles.duplicate_entry("Standard", "QA_STYLE")
    style.dxf.update({"name": "QA_STYLE", "unknown1": 2, "content_type": 2,
        "leader_linetype_handle": linetype.dxf.handle, "leader_line_color": colors.encode_raw_color(colors.RGB(19, 71, 133)),
        "leader_lineweight": 35, "landing_gap_size": .625, "dogleg_length": 3.75,
        "arrow_head_handle": arrow.block_record_handle, "arrow_head_size": 1.125,
        "text_style_handle": text_style.dxf.handle, "char_height": 1.5,
        "default_text_content": "Style Żółć C:\\fixtures\\part.dxf", "text_color": colors.encode_raw_color(3),
        "text_left_attachment_type": 1, "text_right_attachment_type": 3,
        "text_alignment_type": 2, "text_angle_type": 0, "has_text_frame": 1,
        "block_record_handle": content.block_record_handle, "block_scale_x": 1.25,
        "block_scale_y": 1.75, "block_scale_z": .625, "block_rotation": .375,
        "block_color": colors.encode_raw_color(colors.RGB(129, 31, 17)), "scale": 1.25,
        "break_gap_size": .875, "text_attachment_direction": 0,
        "text_bottom_attachment_type": 9, "text_top_attachment_type": 10})
    # Unknown298 is intentionally outside this fixture's qualified schema.
    for entry in doc.mleader_styles:
        entry[1].dxf.discard("unknown2")
    msp = doc.modelspace()
    builder = msp.add_multileader_mtext("QA_STYLE")
    builder.set_content("MText Żółć 測試\\Psecond C:\\fixtures\\part.dxf", char_height=1.75,
                        style="QA_TEXT", alignment=TextAlignment.center,
                        color=colors.RGB(23, 91, 177))
    builder.set_leader_properties(color=colors.RGB(71, 29, 201), linetype="QA_LEADER", lineweight=50)
    builder.set_arrow_properties("DOT", size=.875)
    builder.set_connection_properties(landing_gap=.45, dogleg_length=2.75)
    builder.add_leader_line(ConnectionSide.left, [Vec2(-20, -10), Vec2(-10, -5), Vec2(-5, 0)])
    builder.add_leader_line(ConnectionSide.left, [Vec2(-20, 12), Vec2(-11, 7)])
    builder.add_leader_line(ConnectionSide.right, [Vec2(35, 10), Vec2(25, 5)])
    builder.build(insert=Vec2(6, 3), rotation=17.5)
    text = builder.multileader
    text.context.char_height = 2.125
    text.context.arrow_head_size = 1.0625
    text.context.landing_gap_size = .8125
    text.context.mtext.width = 27.25
    text.context.mtext.defined_height = 8.5
    text.context.mtext.line_spacing_factor = 1.375
    text.context.mtext.line_spacing_style = 2
    text.context.mtext.has_bg_fill = 1
    text.context.mtext.bg_color = colors.encode_raw_color(colors.RGB(211, 223, 239))
    text.context.mtext.bg_scale_factor = 1.875
    text.context.mtext.bg_transparency = 0x0200007F
    text.context.mtext.text_direction *= 2
    text.context.plane_x_axis *= 3
    text.context.plane_y_axis *= 4
    first = text.context.leaders[0]
    first.breaks = [Vec3(-4, .5, 0), Vec3(-3.5, .5, 0), Vec3(-3, .5, 0), Vec3(-2.5, .5, 0)]
    first.lines[0].breaks = [0, Vec3(-18, -9, 0), Vec3(-17, -8.5, 0),
                            1, Vec3(-8, -3, 0), Vec3(-7, -2, 0)]
    first.lines[0].color = colors.encode_raw_color(colors.RGB(241, 71, 11))
    text.arrow_heads = [ArrowHeadData(0, arrow.block_record_handle), ArrowHeadData(2, arrow.block_record_handle)]
    text.dxf.leader_extend_to_text = 1
    block_builder = msp.add_multileader_block("QA_STYLE")
    block_builder.set_content("QA_MLEADER_CONTENT", color=colors.RGB(21, 151, 91),
                              scale=1.375, alignment=BlockAlignment.insertion_point)
    block_builder.set_attribute("LABEL", "Block 測試 C:\\fixtures\\block.dxf", width=.8125)
    block_builder.set_leader_properties(color=5, linetype="QA_LEADER", lineweight=25)
    block_builder.add_leader_line(ConnectionSide.left, [Vec2(10, -25), Vec2(18, -18)])
    block_builder.add_leader_line(ConnectionSide.right, [Vec2(45, -20), Vec2(38, -17)])
    block_builder.build(insert=Vec2(28, -10), rotation=-22.5)
    block = block_builder.multileader
    block.context.block.scale = Vec3(1.25, 1.75, .625)
    block.context.block.extrusion = Vec3(0, 0, 2)
    block.context.block.matrix44 = Matrix44((1.25, .125, .25, 0, -.5, 1.75, .375, 0, .625, -.75, 2, 0, 28, -10, 3.5, 1))
    block.dxf.block_scale_vector = Vec3(2, 3, 4)
    block.dxf.block_rotation = .625
    sentinel = msp.add_line((20, 30, 40), (50, 60, 70))
    for label, entity in (("text", text), ("block", block)):
        entity.set_xdata("QA_MLEADER", [(1000, label), (1005, sentinel.dxf.handle), (1004, bytes([0, 17, 255]))])
    style.set_xdata("QA_MLEADER", [(1000, "style"), (1005, text.dxf.handle)])
    path = ROOT / f"independent-mleader-R{year}.dxf"
    doc.saveas(path)
    loaded = ezdxf.readfile(path)
    audit = loaded.audit()
    assert not audit.errors and not audit.fixes, (audit.errors, audit.fixes)
    handles = {entity.dxf.handle for entity in (text, block, style, sentinel)}
    return {"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "year": year, "version": doc.dxfversion,
            "handles": {"text": text.dxf.handle, "block": block.dxf.handle, "style": style.dxf.handle,
                        "line": sentinel.dxf.handle, "text_style": text_style.dxf.handle,
                        "linetype": linetype.dxf.handle, "content_block": content.block_record_handle,
                        "arrow_block": arrow.block_record_handle, "attdef": attdef.dxf.handle},
            "wire_records": captured_tags(path, handles),
            "contexts": {label: json_value(loaded.entitydb[entity.dxf.handle].context)
                         for label, entity in (("text", text), ("block", block))}}


def main():
    assert ezdxf.__version__ == "1.4.4", "Regenerate and review provenance when producer changes"
    manifest = {"producer": "ezdxf 1.4.4", "source": "generate_fixtures.py",
        "primary_sources": ["https://ezdxf.readthedocs.io/en/stable/tutorials/mleader.html",
                            "https://ezdxf.readthedocs.io/en/stable/dxfentities/mleader.html"],
        "qualification": "Native ezdxf builders plus public context/model edits; no wire patching; no native AutoCAD rendering claim.",
        "fixtures": [generate(year) for year in YEARS]}
    (ROOT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    print("Generated four independent MULTILEADER/MLEADERSTYLE producer fixtures")


if __name__ == "__main__":
    main()
