#!/usr/bin/env python3
"""Independently verify eight API-authored MULTILEADER content alternatives."""
from pathlib import Path
import argparse
import ezdxf
from verify_mleader_inputs import VERSIONS, records, body, nested_context, decode_once, check

TEXT = r"literal \U+0041; Żółć 測試; \Pparagraph"


def inspect(path, year, binary):
    check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Actual transport changed")
    doc = ezdxf.readfile(path)
    check(doc.dxfversion == VERSIONS[year], "Actual DXF profile changed")
    wire = records(path)
    entities = list(doc.modelspace())
    check(len(entities) == 3 and all(e.dxftype() == "MULTILEADER" for e in entities),
          "Expected exactly three authored MULTILEADER entities")
    check([e.dxf.content_type for e in entities] == [0, 2, 1], "No-content/text/block order changed")
    styles = doc.rootdict["ACAD_MLEADERSTYLE"]
    # ezdxf creates a missing Standard MLEADERSTYLE during loading without an
    # audit repair. Inspect the actual dictionary/object records for inventory.
    check([value for code, value in wire[styles.dxf.handle] if code == 3] == ["Authored"],
          "Authored style dictionary wire inventory changed")
    check(sum(record[0] == [0, "MLEADERSTYLE"] for record in wire.values()) == 1,
          "Authored style object wire inventory changed")
    style = styles["Authored"]
    check(style.dxftype() == "MLEADERSTYLE" and style.dxf.owner == styles.dxf.handle,
          "Authored style ownership changed")
    standard = doc.styles.get("Standard")
    continuous = doc.linetypes.get("Continuous")
    check(style.dxf.text_style_handle == standard.dxf.handle, "Style text-style reference changed")
    check(not any(code == 293 for code, _ in body(wire[style.dxf.handle], "AcDbMLeaderStyle")),
          "Unspecified block scaling flag was invented")
    for entity in entities:
        check(entity.dxf.owner == doc.modelspace().block_record_handle, "Entity owner changed")
        check(entity.dxf.style_handle == style.dxf.handle
              and entity.dxf.text_style_handle == standard.dxf.handle
              and entity.dxf.leader_linetype_handle == continuous.dxf.handle,
              "Required entity reference graph changed")
        check(not entity.arrow_heads and not entity.block_attribs, "Repeated overrides were invented")
        packet = body(wire[entity.dxf.handle], "AcDbMLeader")
        check(packet[0] == [270, 2], "Payload version changed")
        check(nested_context(packet, year) == (0, 0), "Empty authored context gained leaders")
        check(tuple(entity.context.plane_x_axis) == (1, 0, 0)
              and tuple(entity.context.plane_y_axis) == (0, 1, 0), "Context plane defaults changed")
    empty, text, block = entities
    check(empty.context.mtext is None and empty.context.block is None, "No-content alternative changed")
    check(text.context.block is None and text.context.mtext is not None, "Text alternative changed")
    mtext = text.context.mtext
    check(decode_once(mtext.default_content) == TEXT, "Literal Unicode-looking escape or Unicode text changed")
    check(mtext.style_handle == standard.dxf.handle, "Embedded STYLE target changed")
    check(mtext.column_type == 2 and mtext.column_width == 5.25 and mtext.column_gutter_width == .625,
          "Embedded column fields changed")
    check(mtext.column_sizes == [3.125, 0.0], "Repeated column heights or explicit zero changed")
    packet = body(wire[text.dxf.handle], "AcDbMLeader")
    check([value for code, value in packet if code == 144] == [3.125, 0.0], "Repeated144 wire values changed")
    check([value for code, value in packet if code == 304] == [TEXT], "Stored MTEXT string changed")
    check(block.context.mtext is None and block.context.block is not None, "Block alternative changed")
    content = doc.blocks["AuthoredBlock"]
    check(block.context.block.block_record_handle == content.block_record_handle,
          "Embedded BLOCK_RECORD reference changed")
    check(block.context.block._matrix == [], "Absent block transformation matrix was invented")
    check(not any(code == 47 for code, _ in body(wire[block.dxf.handle], "AcDbMLeader")),
          "Absent block matrix appeared on wire")
    geometry = list(content)
    check(len(geometry) == 1 and geometry[0].dxftype() == "LINE"
          and tuple(geometry[0].dxf.start) == (0, 0, 0) and tuple(geometry[0].dxf.end) == (1, 0, 0),
          "Referenced block geometry changed")
    for name, count, entity_flag in (("MULTILEADER", 3, 1), ("MLEADERSTYLE", 1, 0)):
        cls = doc.classes.get(name)
        check(cls.dxf.instance_count == count and cls.dxf.is_an_entity == entity_flag,
              name + " CLASS count/type changed")
    audit = doc.audit()
    check(not audit.errors and not audit.fixes,
          f"ezdxf audit reported {len(audit.errors)} errors/{len(audit.fixes)} repairs")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    expected = {f"mleader-AutoCad{year}-{kind}.dxf" for year in VERSIONS for kind in ("text", "binary")}
    check({p.name for p in args.directory.glob("mleader-AutoCad*.dxf")} == expected,
          "Expected exactly eight authored MLEADER profile/transport outputs")
    for year in VERSIONS:
        for binary in (False, True):
            path = args.directory / f"mleader-AutoCad{year}-{'binary' if binary else 'text'}.dxf"
            try:
                inspect(path, year, binary)
            except Exception as error:
                raise ValueError(path.name + ": " + str(error)) from error
            print("PASS " + path.name)
    print(f"PASS ezdxf {ezdxf.__version__}: eight authored drawings, 24 no-content/text/block contexts, "
          "column heights, absent matrices, literal text, style/resource graphs and zero audit changes")


if __name__ == "__main__":
    main()
