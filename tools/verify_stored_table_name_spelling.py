#!/usr/bin/env python3
"""Check exact TABLE name spelling before and after identity-bound resource renames."""
import argparse
from pathlib import Path
from verify_mleader_inputs import check
from verify_stored_table_fields import table, reject_mutation


def verify(tags, style, phase):
    wire = r"s\U+0074yle_reference" if style else r"displ\U+0061y"
    code = 7 if style else 2
    expected = [wire, wire] if phase == 0 else ["RENAMED_RESOURCE", wire]
    check([v for c, v in tags if c == code] == expected, "Unchanged wire spelling or private name changed")
    check(tags[-2:] == [[100, "PrivateNames"], [code, wire]], "Private name scope changed")


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument("directory", type=Path); args = parser.parse_args()
    expected = set(); negative = 0
    for binary in (False, True):
        for style in (False, True):
            for phase in (0, 1):
                name = f"stored-table-name-spelling-{style}-{binary}-{phase}.dxf"; expected.add(name)
                doc, tags = table(args.directory / name, binary if phase == 0 else not binary, 2018)
                verify(tags, style, phase)
                resource = "RENAMED_RESOURCE" if phase else "STYLE_REFERENCE" if style else "DISPLAY"
                check(doc.styles.has_entry(resource) if style else resource in doc.blocks, "Exact renamed resource missing")
                negative += reject_mutation(tags, 7 if style else 2, lambda value: "CORRUPTED", lambda changed: verify(changed, style, phase))
    check({p.name for p in args.directory.glob("stored-table-name-spelling-*.dxf")} == expected, "Expected every name-spelling output")
    check(negative == 8, "Expected eight actual parsed-packet corruption controls")
    print("PASS 8 TABLE resource-name outputs and 8 corruption controls; source spelling and private fields retained")


if __name__ == "__main__": main()
