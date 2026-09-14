#!/usr/bin/env python3
"""Compare eight MULTILEADER exports with four independent ezdxf producer inputs."""
from pathlib import Path
import argparse
import hashlib
import io
import json
import struct
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler
from ezdxf.math import Vec2, Vec3
from ezdxf.entities.mleader import acdb_mleader_style

VERSIONS = {2007: "AC1021", 2010: "AC1024", 2013: "AC1027", 2018: "AC1032"}


def check(value, message):
    if not value:
        raise ValueError(message)


def json_value(value):
    if isinstance(value, (Vec2, Vec3)):
        return list(value)
    if isinstance(value, bytes):
        return {"hex": value.hex()}
    if isinstance(value, dict):
        return {key: json_value(item) for key, item in value.items()}
    if isinstance(value, (tuple, list)):
        return [json_value(item) for item in value]
    if hasattr(value, "__dict__"):
        return json_value(vars(value))
    return value


def exact(value):
    if isinstance(value, float):
        return struct.pack("<d", value if value else 0.0)
    if isinstance(value, list):
        return [exact(item) for item in value]
    if isinstance(value, dict):
        return {key: exact(item) for key, item in value.items()}
    return value


def decode_once(text):
    return decode_dxf_unicode(text).encode("utf-16-le", "surrogatepass").decode("utf-16-le")


def context_semantics(value):
    # ezdxf's MLEADER context and repeated attribute strings retain DXF Unicode
    # escapes, unlike ordinary MTEXT text accessors. Decode their wire strings
    # exactly once, independently of the already-decoded raw-body comparison.
    if isinstance(value, str):
        return decode_once(value)
    if isinstance(value, list):
        return [context_semantics(item) for item in value]
    if isinstance(value, dict):
        return {key: context_semantics(item) for key, item in value.items()}
    return value


def records(path):
    data = path.read_bytes()
    loader = binary_tags_loader(data) if data.startswith(b"AutoCAD Binary DXF") else (
        ascii_tags_loader(io.StringIO(data.decode("utf-8-sig"), newline=None)))
    result, current, handle, subclass_seen = {}, [], None, False
    for tag in tag_compiler(loader):
        if tag.code == 0:
            if handle is not None:
                check(handle not in result, "Duplicate record identity")
                result[handle] = current
            current, handle, subclass_seen = [], None, False
        if tag.code == 5 and not subclass_seen:
            check(handle is None, "Repeated record handle")
            handle = tag.value
        if tag.code == 100:
            subclass_seen = True
        current.append([tag.code, json_value(tag.value)])
    return result


def body(record, name):
    start = record.index([100, name])
    return [[code, decode_once(value) if isinstance(value, str) else value]
            for code, value in record[start + 1:]]


def nested_context(tags, year):
    stack, nodes, lines, starts, ends = [], 0, 0, [], []
    for index, (code, value) in enumerate(tags):
        if code == 300 and value == "CONTEXT_DATA{":
            check(not stack, "Context nested inside another packet")
            stack.append("context")
            starts.append(index)
        elif code == 302 and value == "LEADER{" and stack:
            check(stack[-1] == "context", "Leader node outside context")
            stack.append("node")
            nodes += 1
        elif code == 304 and value == "LEADER_LINE{" and stack and stack[-1] == "node":
            stack.append("line")
            lines += 1
        elif stack and code in (301, 303, 305):
            check(value == "}" and {301: "context", 303: "node", 305: "line"}[code] == stack[-1],
                  "Mismatched nested packet delimiter")
            closing = stack.pop()
            if closing == "context":
                ends.append(index)
        if year == 2007 and stack:
            check(code not in (271, 272, 273), "Later attachment field in R2007 context")
    check(not stack and len(starts) == len(ends) == 1, "Expected exactly one balanced context")
    check(starts[0] < ends[0], "Reversed context boundaries")
    outer = tags[:starts[0]] + tags[ends[0] + 1:]
    if year == 2007:
        check(not any(code in (271, 272, 273) for code, _ in outer), "Later entity attachment field in R2007")
    if year < 2013:
        check(not any(code == 295 for code, _ in outer), "R2013 entity extension flag in older profile")
    return nodes, lines


def compare(source, output, fixture, binary, validate_classes=True):
    year = fixture["year"]
    check(output.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Transport differs from output name")
    before, after = ezdxf.readfile(source), ezdxf.readfile(output)
    check(after.dxfversion == VERSIONS[year], "Actual DXF version changed")
    old_records, new_records = records(source), records(output)
    handles = fixture["handles"]
    check(len(list(after.modelspace().query("MULTILEADER"))) == 2, "Expected text and block MULTILEADER")
    check(len(list(after.modelspace().query("LINE"))) == 1, "Following LINE inventory changed")
    for label in ("text", "block"):
        handle = handles[label]
        old, new = before.entitydb[handle], after.entitydb.get(handle)
        check(new is not None and new.dxftype() == "MULTILEADER", label + ": object identity/type changed")
        check(old.dxf.owner == new.dxf.owner == after.modelspace().block_record_handle,
              label + ": entity ownership changed")
        old_body, new_body = body(old_records[handle], "AcDbMLeader"), body(new_records[handle], "AcDbMLeader")
        check(exact(new_body) == exact(old_body), label + ": exact nested/common payload, field presence or XData changed")
        nodes, lines = nested_context(new_body, year)
        check(nodes == 2 and lines == (3 if label == "text" else 2), label + ": node/line inventory changed")
        check(exact(json_value(old.context)) == exact(fixture["contexts"][label]), "Source context disagrees with provenance manifest")
        check(exact(context_semantics(json_value(new.context))) == exact(context_semantics(fixture["contexts"][label])),
              label + ": high-level context semantics changed")
        fields = set(old.dxf.all_existing_dxf_attribs()) | set(new.dxf.all_existing_dxf_attribs())
        check(all((getattr(old.dxf, field).casefold() == getattr(new.dxf, field).casefold())
                  if field in ("linetype", "layer") else getattr(old.dxf, field) == getattr(new.dxf, field)
                  for field in fields),
              label + ": common entity/MLEADER attributes changed")
        check(old.arrow_heads == new.arrow_heads and
              exact(context_semantics(json_value(old.block_attribs))) == exact(context_semantics(json_value(new.block_attribs))),
              label + ": arrow override or block attribute association changed")
        check(list(new.get_xdata("QA_MLEADER")) == list(old.get_xdata("QA_MLEADER")), label + ": XData changed")
        for attr, kind in (("style_handle", "MLEADERSTYLE"), ("leader_linetype_handle", "LTYPE"),
                           ("text_style_handle", "STYLE"), ("arrow_head_handle", "BLOCK_RECORD"),
                           ("block_record_handle", "BLOCK_RECORD")):
            target = new.dxf.get(attr)
            if target and target != "0":
                check(after.entitydb[target].dxftype() == kind, label + ": wrong reference target for " + attr)
        for arrow in new.arrow_heads:
            check(after.entitydb[arrow.handle].dxftype() == "BLOCK_RECORD", "Arrow override target is not a block record")
        for attribute in new.block_attribs:
            check(after.entitydb[attribute.handle].dxftype() == "ATTDEF", "Block attribute does not target ATTDEF")
    old_style, new_style = before.mleader_styles.get("QA_STYLE"), after.mleader_styles.get("QA_STYLE")
    check(new_style.dxf.handle == handles["style"] and old_style.dxf.owner == new_style.dxf.owner,
          "Style identity/ownership changed")
    check(after.rootdict["ACAD_MLEADERSTYLE"]["QA_STYLE"] is new_style, "Named MLEADERSTYLE dictionary edge changed")
    old_style_body = body(old_records[handles["style"]], "AcDbMLeaderStyle")
    new_style_body = body(new_records[handles["style"]], "AcDbMLeaderStyle")
    old_fields = {code: value for code, value in old_style_body if code < 1000}
    new_fields = {code: value for code, value in new_style_body if code < 1000}
    check(len(new_fields) == sum(code < 1000 for code, _ in new_style_body), "Duplicate style scalar")
    check(all(code in new_fields and exact(value) == exact(new_fields[code])
              for code, value in old_fields.items()), "Stored MLEADERSTYLE field changed")
    # Scalar field order is not semantically significant. The typed writer may
    # materialize absent defaults, but never replace a source's explicit value.
    defaults = {attribute.code: attribute.default for attribute in acdb_mleader_style.attribs.values()}
    check(all(code in defaults and exact(value) == exact(defaults[code])
              for code, value in new_fields.items() if code not in old_fields),
          "Added MLEADERSTYLE field is not the independent schema default")
    check([tag for tag in old_style_body if tag[0] >= 1000] ==
          [tag for tag in new_style_body if tag[0] >= 1000], "Style XData changed")
    for label, kind in (("text_style", "STYLE"), ("linetype", "LTYPE"), ("content_block", "BLOCK_RECORD"),
                        ("arrow_block", "BLOCK_RECORD"), ("attdef", "ATTDEF")):
        check(after.entitydb[handles[label]].dxftype() == kind, "Referenced resource identity/type changed: " + label)
    line = after.entitydb[handles["line"]]
    check(tuple(line.dxf.start) == (20, 30, 40) and tuple(line.dxf.end) == (50, 60, 70), "Following LINE geometry changed")
    if validate_classes:
        # The native producer omits MULTILEADER CLASS and writes style count0.
        # Require correct declarations in library outputs independently.
        for kind, count, entity_flag in (("MULTILEADER", 2, 1), ("MLEADERSTYLE", 2, 0)):
            cls = next(item for item in after.classes if item.dxf.name == kind)
            check(cls.dxf.instance_count == count and cls.dxf.is_an_entity == entity_flag,
                  kind + ": CLASS count/type declaration changed")
    audit = after.audit()
    check(not audit.errors and not audit.fixes,
          f"ezdxf audit reported {len(audit.errors)} errors/{len(audit.fixes)} repairs")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--sources", type=Path)
    args = parser.parse_args()
    sources = args.sources or Path(__file__).resolve().parents[1] / "tests/fixtures/mleader"
    if not sources.is_dir():
        sources = Path(__file__).resolve().parent
    manifest = json.loads((sources / "manifest.json").read_text())
    check(len(manifest["fixtures"]) == 4, "Expected four independent source profiles")
    expected = {f"independent-mleader-R{year}-{kind}.dxf" for year in VERSIONS for kind in ("text", "binary")}
    check({path.name for path in args.directory.glob("independent-mleader-R*.dxf")} == expected,
          "Expected all eight independent MLEADER roundtrips")
    for fixture in manifest["fixtures"]:
        source = sources / fixture["file"]
        check(hashlib.sha256(source.read_bytes()).hexdigest() == fixture["sha256"], "Independent source digest changed")
        wire = records(source)
        for handle, expected_record in fixture["wire_records"].items():
            check(exact(wire[handle]) == exact(expected_record), "Source wire snapshot disagrees with manifest")
        for binary in (False, True):
            output = args.directory / f"independent-mleader-R{fixture['year']}-{'binary' if binary else 'text'}.dxf"
            try:
                compare(source, output, fixture, binary)
            except Exception as error:
                raise ValueError(output.name + ": " + str(error)) from error
            print("PASS " + output.name)
    print(f"PASS ezdxf {ezdxf.__version__}: eight external MLEADER roundtrips, 16 contexts, 32 nodes, "
          "40 leader lines; exact nested packets, resource graphs, style records and zero audit errors/repairs")


if __name__ == "__main__":
    main()
