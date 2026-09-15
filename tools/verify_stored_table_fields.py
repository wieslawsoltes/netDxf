#!/usr/bin/env python3
"""Verify documented TABLE name, FIELD and legacy text-chunk packets.

These are synthetic public-schema carriers; the pinned native corpus has no
STYLE-name overrides, FIELD handles or legacy group-2 cell continuations.
"""
import argparse
import copy
from pathlib import Path
import ezdxf
from verify_mleader_inputs import records, check


def table(path, binary, year):
    check(path.read_bytes().startswith(b"AutoCAD Binary DXF") == binary, "Wrong transport")
    doc = ezdxf.readfile(path)
    check(doc.dxfversion == {2004: "AC1018", 2007: "AC1021", 2018: "AC1032"}[year], "Wrong profile")
    found = [r for r in records(path).values() if r[0] == [0, "ACAD_TABLE"]]
    check(len(found) == 1, "TABLE type or multiplicity changed")
    return doc, found[0]


def verify_style(doc, tags, scope):
    public = scope in ("header", "cell", "mixed-subclasses")
    value = "RENAMED_Ω_STYLE" if public else r"S\U+0054YLE_REFERENCE"
    check([v for c, v in tags if c == 7] == ([value, r"S\U+0054YLE_REFERENCE"] if scope == "mixed-subclasses" else [value]), "Public/private STYLE spelling changed")
    check(doc.styles.has_entry("RENAMED_Ω_STYLE") == public, "STYLE dependency removal contract changed")


def verify_field(doc, tags, year, field):
    handles = [v for c, v in tags if c == 344]
    check(len(handles) == 1 and (handles[0] != "0") == field, "FIELD presence changed")
    if field:
        check(handles[0] in doc.entitydb and doc.entitydb[handles[0]].dxftype() == "XRECORD", "FIELD handle carrier changed")
    check([v for c, v in tags if c == 1] == (["stored text is not evaluated"] if year == 2004 else ["value"]), "Stored text beside FIELD was rewritten")


def verify_private_field(doc, tags, value_scope):
    handles = [v for c, v in tags if c == 344]
    check(len(handles) == 1 and handles[0] != "0" and handles[0] in doc.entitydb and doc.entitydb[handles[0]].dxftype() == "XRECORD", "Private handle carrier changed")
    check([v for c, v in tags if c == 1] == ["value"], "Public literal beside private FIELD changed")
    begin = tags.index([301, "CELL_VALUE"])
    field = tags.index([344, handles[0]])
    check(field > begin if value_scope else tags[field - 1] == [102, "{PRIVATE_TABLE"] and tags[field + 1] == [102, "}"], "Private FIELD scope changed")


def verify_chunks(tags, context):
    start = next(i for i, tag in enumerate(tags) if tag[0] == 171)
    chunks = [tag for tag in tags[start:] if tag[0] in (1, 2)]
    want = [[2, "x" * 250], [2, "y" * 250], [1, "tail"]]
    if context == "legacy-unicode": want = [[2, "x" * 248 + "\\U"], [1, "+03A9tail"]]
    if context == "legacy-private": want.insert(0, [2, "unrelated private text"])
    if context == "legacy-out-of-order": want.reverse()
    if context == "legacy-short-chunk": want[0] = [2, "xxx"]
    if context == "legacy-long-tail": want[-1] = [1, "z" * 251]
    check(chunks == want, "Legacy ordered raw chunks changed")
    check(tags[start] == [171, 2 if context == "block-cell" else 1], "Cell type changed")


def reject_mutation(tags, code, mutate, validator, after_cell=False):
    """Corrupt an actual parsed field and run the identical positive validator."""
    changed = copy.deepcopy(tags)
    start = next(i for i, tag in enumerate(changed) if tag[0] == 171) if after_cell else 0
    index = next(i for i in range(start, len(changed)) if changed[i][0] == code)
    changed[index][1] = mutate(changed[index][1])
    try:
        validator(changed)
    except ValueError:
        return 1
    raise AssertionError(f"Corrupted TABLE group {code} escaped the public-field validator")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    expected = set()
    checked = negative = 0
    for binary in (False, True):
        for scope in ("header", "cell", "private-subclass", "private-application", "private-value", "mixed-subclasses"):
            name = f"stored-table-style-{binary}-{scope}.dxf"; expected.add(name)
            doc, tags = table(args.directory / name, binary, 2018)
            verify_style(doc, tags, scope)
            negative += reject_mutation(tags, 7, lambda value: "CORRUPTED_STYLE", lambda changed: verify_style(doc, changed, scope))
            checked += 1
        for year in (2004, 2007, 2018):
            for field in (False, True):
                name = f"stored-table-field-AutoCad{year}-{binary}-{field}.dxf"; expected.add(name)
                doc, tags = table(args.directory / name, binary, year)
                verify_field(doc, tags, year, field)
                negative += reject_mutation(tags, 344, lambda value: "0" if value != "0" else "ABCDEF", lambda changed: verify_field(doc, changed, year, field))
                checked += 1
        for value_scope in (False, True):
            name = f"stored-table-field-private-{binary}-{value_scope}.dxf"; expected.add(name)
            doc, tags = table(args.directory / name, binary, 2018)
            verify_private_field(doc, tags, value_scope)
            negative += reject_mutation(tags, 344, lambda value: "0", lambda changed: verify_private_field(doc, changed, value_scope))
            checked += 1
        for context in ("legacy", "legacy-unicode", "legacy-private", "legacy-out-of-order", "legacy-short-chunk", "legacy-long-tail", "block-cell", "modern"):
            name = f"stored-table-chunks-{binary}-{context}.dxf"; expected.add(name)
            _, tags = table(args.directory / name, binary, 2018 if context == "modern" else 2004)
            verify_chunks(tags, context)
            negative += reject_mutation(tags, 2, lambda value: "!" + value[1:], lambda changed: verify_chunks(changed, context), after_cell=True)
            checked += 1
    actual = {p.name for p in args.directory.glob("stored-table-*.dxf") if p.name.startswith(("stored-table-style-", "stored-table-field-", "stored-table-chunks-"))}
    check(actual == expected, "Expected all 44 public-schema carriers")
    check(negative == checked == 44, "Expected a parsed-packet corruption control for every carrier")
    print(f"PASS {checked} synthetic TABLE public-schema carriers and {negative} corruption controls; no native or evaluated-field claim")


if __name__ == "__main__":
    main()
