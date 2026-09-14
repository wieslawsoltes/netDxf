#!/usr/bin/env python3
"""Independently verify the 72 combined database conformance exports with ezdxf.

The expected corpus comes from IntegratedDatabaseTests.cs. Common owners,
extension pointers, reactors and full XRECORD payloads are checked on the wire:
ezdxf clears linked MTEXT owners while loading, and its high-level XRECORD view
can truncate application payloads containing group 100.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import io
from pathlib import Path
import re

import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler


VERSIONS = {
    2000: "AC1015", 2004: "AC1018", 2007: "AC1021",
    2010: "AC1024", 2013: "AC1027", 2018: "AC1032",
}
APPLICATION = "INTEGRATED_META"
CONFIGURATION = "Integrated plan"
PROFILE = re.compile(
    r"integrated-(database|local-clone|mapping-source|mapped-clone)"
    r"-AutoCad(2000|2004|2007|2010|2013|2018)-(False|True)(?:-([012]))?\.dxf"
)


def check(condition, message):
    if not condition:
        raise ValueError(message)


@dataclass(frozen=True)
class Profile:
    kind: str
    year: int
    initial_binary: bool
    cycle: int | None

    @property
    def binary(self):
        flip = self.kind in {"local-clone", "mapped-clone"} or self.cycle == 1
        return self.initial_binary != flip


def profile_for(name):
    match = PROFILE.fullmatch(name)
    check(match is not None, "Unexpected integrated fixture name: " + name)
    kind, year, binary, cycle = match.groups()
    check((kind == "database") == (cycle is not None), "Invalid cycle suffix: " + name)
    return Profile(kind, int(year), binary == "True", int(cycle) if cycle is not None else None)


def expected_names():
    names = set()
    for year in VERSIONS:
        for binary in (False, True):
            suffix = f"AutoCad{year}-{binary}"
            names.update(f"integrated-database-{suffix}-{cycle}.dxf" for cycle in range(3))
            names.update(f"integrated-{kind}-{suffix}.dxf"
                         for kind in ("local-clone", "mapping-source", "mapped-clone"))
    return names


def check_inventory(directory):
    paths = sorted(directory.glob("integrated-*.dxf"))
    actual, expected = {path.name for path in paths}, expected_names()
    missing, extra = expected - actual, actual - expected
    check(not missing and not extra,
          "Expected exactly 72 integrated fixtures; missing: "
          + (", ".join(sorted(missing)) or "none")
          + "; unexpected: " + (", ".join(sorted(extra)) or "none"))
    return paths


def read_wire(path):
    data = path.read_bytes()
    binary = data.startswith(b"AutoCAD Binary DXF")
    loader = binary_tags_loader(data) if binary else ascii_tags_loader(
        io.StringIO(data.decode("utf-8-sig"), newline=None))
    tags = list(tag_compiler(loader))
    versions = [tags[index + 1].value for index, tag in enumerate(tags[:-1])
                if tag.code == 9 and tag.value == "$ACADVER" and tags[index + 1].code == 1]
    check(len(versions) == 1, "Missing or repeated $ACADVER header")
    records = []
    for tag in tags:
        if tag.code == 0:
            records.append([])
        if records:
            records[-1].append(tag)
    by_handle = {}
    for record in records:
        prefix = common_tags(record)
        handles = [tag.value for tag in prefix if tag.code in (5, 105)]
        if not handles:
            continue
        check(len(handles) == 1 and handles[0] not in by_handle,
              "Duplicate physical object handle: " + repr(handles))
        by_handle[handles[0]] = record
    return binary, versions[0], by_handle


def common_tags(record):
    end = next((index for index, tag in enumerate(record) if tag.code == 100), len(record))
    return record[1:end]


def common_metadata(record):
    """Separate direct group-330 owner from group-102 reactor/extension blocks."""
    owners, reactors, extensions, group = [], [], [], None
    for tag in common_tags(record):
        if tag.code == 102:
            if tag.value == "}":
                check(group is not None, "Unbalanced common application group")
                group = None
            else:
                check(group is None, "Nested common application group")
                group = tag.value
        elif group == "{ACAD_REACTORS" and tag.code == 330:
            reactors.append(tag.value)
        elif group == "{ACAD_XDICTIONARY" and tag.code == 360:
            extensions.append(tag.value)
        elif group is None and tag.code == 330:
            owners.append(tag.value)
    check(group is None and len(owners) == 1 and len(extensions) <= 1,
          "Invalid common owner/extension packet")
    return owners[0], reactors, extensions


def dictionary_entries(record):
    entries = []
    for index, tag in enumerate(record):
        if tag.code == 1001:
            break
        if tag.code == 3:
            check(index + 1 < len(record) and record[index + 1].code in (350, 360),
                  "Dictionary name is not followed by a 350/360 entry")
            edge = record[index + 1]
            entries.append((tag.value, edge.code, edge.value))
    return entries


def raw_xdata(record, application):
    data, active, matches = [], False, 0
    for tag in record:
        if tag.code == 1001:
            active = tag.value == application
            matches += active
        elif active:
            data.append((tag.code, tag.value))
    check(matches == 1, "Missing or duplicate XData application: " + application)
    return data


def xrecord_payload(record):
    check(record[0].value == "XRECORD", "Expected physical XRECORD")
    marker = next((index for index, tag in enumerate(record)
                   if tag.code == 100 and tag.value == "AcDbXrecord"), None)
    check(marker is not None, "Missing AcDbXrecord subclass")
    payload = record[marker + 1:]
    check(payload and payload[0].code == 280, "Missing XRECORD cloning header")
    # Group 100 in the application payload is data, not a stopping condition.
    end = next((index for index, tag in enumerate(payload) if tag.code == 1001), len(payload))
    return [(tag.code, tag.value) for tag in payload[1:end]]


class Inspector:
    def __init__(self, path):
        self.path = path
        self.profile = profile_for(path.name)
        binary, version, self.records = read_wire(path)
        check(binary == self.profile.binary, "Actual DXF transport does not match cycle/clone profile")
        check(version == VERSIONS[self.profile.year], "Wire DXF version does not match fixture profile")
        self.doc = ezdxf.readfile(path)
        check(self.doc.dxfversion == version, "Independent reader reports a different DXF version")
        self.identities = {}

    def record(self, entity, kind=None):
        handle = entity.dxf.handle
        check(handle in self.records, "Object does not have a physical record: " + handle)
        check(self.doc.entitydb.get(handle) is entity, "Reference does not resolve to the actual object: " + handle)
        record = self.records[handle]
        check(record[0].value == (kind or entity.dxftype()), "Physical record type changed: " + handle)
        return record

    def remember(self, role, entity):
        self.record(entity)
        self.identities[role] = entity.dxf.handle

    def owner(self, entity, owner):
        expected = owner.dxf.handle
        actual, _, _ = common_metadata(self.record(entity))
        check(actual == expected, "Wire owner changed: " + entity.dxf.handle)
        check(entity.dxf.owner == expected, "Independent reader owner changed: " + entity.dxf.handle)

    def reactors(self, entity, expected):
        handles = [target.dxf.handle for target in expected]
        _, actual, _ = common_metadata(self.record(entity))
        check(actual == handles and entity.get_reactors() == handles, "Persistent reactor targets/order changed")
        for target in expected:
            self.record(target)

    def xdata(self, entity, label, target):
        expected = [(1000, label), (1005, target.dxf.handle)]
        check(raw_xdata(self.record(entity), APPLICATION) == expected, "Wire XData label/1005 target changed")
        check([(tag.code, tag.value) for tag in entity.get_xdata(APPLICATION)] == expected,
              "Independent reader XData label/1005 target changed")
        self.record(target)

    def payload(self, entity, expected):
        check(xrecord_payload(self.record(entity, "XRECORD")) == expected,
              "Complete ordered XRECORD payload changed: " + entity.dxf.handle)
        for code, handle in expected:
            if code in (330, 340):
                target = self.doc.entitydb.get(handle)
                check(target is not None, "XRECORD pointer is unresolved: " + handle)
                self.record(target)

    def extension(self, role, entity, state, peer):
        extension = entity.get_extension_dict().dictionary
        self.record(extension, "DICTIONARY")
        _, _, pointers = common_metadata(self.record(entity))
        check(pointers == [extension.dxf.handle], "Wire extension dictionary pointer changed")
        self.owner(extension, entity)
        check(set(extension.keys()) == {"LINKS"}, "Extension dictionary keys changed")
        links = extension["LINKS"]
        check(dictionary_entries(self.record(extension)) == [("LINKS", 360, links.dxf.handle)],
              "Extension dictionary edge changed")
        self.owner(links, extension)
        self.payload(links, [(1, "cross-module extension"), (330, state.dxf.handle), (340, peer.dxf.handle)])
        self.remember(role + ".extension", extension)
        self.remember(role + ".links", links)

    def graph(self, key, mode_value, left, right, main, linked):
        graph = self.doc.rootdict[key]
        self.record(graph, "DICTIONARY")
        self.owner(graph, self.doc.rootdict)
        check(set(graph.keys()) == {"STATE", "STATE_ALIAS", "MODE"}, "Named graph keys changed")
        state, mode = graph["STATE"], graph["MODE"]
        check(state is graph["STATE_ALIAS"], "Named aliases do not resolve to one actual object")
        check(dictionary_entries(self.record(graph)) == [
            ("STATE", 360, state.dxf.handle), ("STATE_ALIAS", 350, state.dxf.handle),
            ("MODE", 360, mode.dxf.handle)], "Named dictionary 350/360 edge order changed")
        check(graph.dxf.hard_owned == 1, "Dictionary-wide hard ownership changed")
        self.owner(state, graph)
        self.owner(mode, graph)
        self.record(mode, "DICTIONARYVAR")
        check(mode.dxf.schema == 0 and mode.dxf.value == mode_value, "Independent graph mode changed")
        self.payload(state, [
            (1, "view and text state"), (330, left.dxf.handle), (340, right.dxf.handle),
            (340, main.dxf.handle), (330, mode.dxf.handle),
            *((340, column.dxf.handle) for column in linked), (320, "FEDCBA")])
        self.reactors(state, [right])
        self.xdata(graph, "named graph", mode)
        self.xdata(state, "state record", left)
        for suffix, entity in (("", graph), (".state", state), (".mode", mode)):
            self.remember(key + suffix, entity)
        return state

    def inspect(self):
        doc = self.doc
        check(APPLICATION in doc.appids, "Shared XData application registry missing")
        tables = list(doc.viewports)
        check(len(tables) == 3 and [table.dxf.name for table in tables] == ["*Active", CONFIGURATION, CONFIGURATION],
              "Physical VPORT records or repeated-name ordering changed")
        left, right = tables[1:]
        check(left is not right and left.dxf.handle != right.dxf.handle, "Repeated VPORT identities collapsed")
        wire_tables = [record for record in self.records.values() if record[0].value == "VPORT"]
        check(len(wire_tables) == 3, "Unexpected physical VPORT count")
        for role, tile, center, lower, upper in (
            ("left", left, (3, 4, 0), (0, 0, 0), (.5, 1, 0)),
            ("right", right, (8, 9, 0), (.5, 0, 0), (1, 1, 0)),
        ):
            self.remember(role, tile)
            self.owner(tile, doc.viewports.head)
            check(tuple(tile.dxf.center) == center, "VPORT center/order changed")
            check(tuple(tile.dxf.lower_left) == lower and tuple(tile.dxf.upper_right) == upper,
                  "VPORT tile rectangle changed")

        texts = list(doc.modelspace().query("MTEXT"))
        check(len(texts) == 1, "Expected one main MTEXT after independent linked-column resolution")
        main = texts[0]
        self.remember("main", main)
        self.owner(main, doc.modelspace().block_record)
        columns = main.columns
        check(columns is not None and int(columns.column_type) == 1 and columns.count == 2,
              "Static two-column definition changed")
        check(not columns.auto_height and not columns.reversed_column_flow and not columns.heights,
              "Static column height/flow mode changed")
        check(columns.width == 12 and columns.gutter_width == 2 and columns.total_width == 26,
              "MTEXT column width/gutter changed")
        check(columns.defined_height == 20 and columns.total_height == 20, "MTEXT column heights changed")
        check(tuple(main.dxf.insert) == (2, 3, 0) and main.dxf.char_height == 2, "MTEXT placement/height changed")
        linked = list(columns.linked_columns)
        physical_texts = {handle for handle, record in self.records.items() if record[0].value == "MTEXT"}
        if self.profile.year < 2018:
            check(main.text == "Left" and len(linked) == 1 and linked[0].text == "Right",
                  "Legacy linked text order/content changed")
            self.remember("linked", linked[0])
            check(main.dxf.handle != linked[0].dxf.handle, "Linked MTEXT reused main identity")
            check(tuple(linked[0].dxf.insert) == (16, 3, 0) and linked[0].dxf.char_height == 2,
                  "Linked column placement/height changed")
            # ezdxf deliberately clears the subordinate entity's in-memory owner.
            check(common_metadata(self.record(linked[0]))[0] == common_metadata(self.record(main))[0],
                  "Linked columns do not share the actual wire block owner")
        else:
            check(main.text == "LeftRight" and not linked, "Embedded text/link storage changed")
        check(physical_texts == {main.dxf.handle, *(column.dxf.handle for column in linked)},
              "Physical MTEXT entities differ from the resolved main/linked identities")

        expected_graphs = {"INTEGRATED"}
        if self.profile.kind == "local-clone":
            expected_graphs.add("LOCAL_COPY")
        elif self.profile.kind == "mapped-clone":
            expected_graphs.add("IMPORTED")
        actual_graphs = set(doc.rootdict.keys()) & {"INTEGRATED", "LOCAL_COPY", "IMPORTED", "MISSING_MAP"}
        check(actual_graphs == expected_graphs, "Unexpected graph copy or rejected MISSING_MAP survived")
        root_edges = {name: (code, handle) for name, code, handle in dictionary_entries(self.record(doc.rootdict))}
        for key in expected_graphs:
            check(root_edges.get(key) == (360, doc.rootdict[key].dxf.handle), "Named-root hard ownership edge changed")
        state = self.graph("INTEGRATED", "original", left, right, main, linked)
        self.extension("left", left, state, main)
        self.extension("right", right, state, left)
        self.extension("main", main, state, right)
        self.reactors(left, [state])
        self.reactors(main, [right])
        self.xdata(left, "left viewport", state)
        self.xdata(right, "right viewport", main)
        self.xdata(main, "main text", right)
        for key in sorted(expected_graphs - {"INTEGRATED"}):
            self.graph(key, "local copy" if key == "LOCAL_COPY" else "imported copy", left, right, main, linked)
            original = {self.identities["INTEGRATED" + suffix] for suffix in ("", ".state", ".mode")}
            copied = {self.identities[key + suffix] for suffix in ("", ".state", ".mode")}
            check(len(original) == len(copied) == 3 and original.isdisjoint(copied),
                  "Clone graph/state/mode reused original identities")
        check(len(set(self.identities.values())) == len(self.identities), "Independent object identities collapsed")
        audit = doc.audit()
        check(not audit.errors and not audit.fixes,
              f"Independent audit found {len(audit.errors)} errors/{len(audit.fixes)} repairs")
        return self.identities


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    try:
        paths = check_inventory(args.directory)
        identities = {}
        for path in paths:
            try:
                identities[path.name] = Inspector(path).inspect()
            except Exception as error:
                raise ValueError(path.name + ": " + str(error)) from error
            print("PASS " + path.name)
        for year in VERSIONS:
            for binary in (False, True):
                suffix = f"AutoCad{year}-{binary}"
                baseline = identities[f"integrated-database-{suffix}-0.dxf"]
                for cycle in (1, 2):
                    check(identities[f"integrated-database-{suffix}-{cycle}.dxf"] == baseline,
                          f"Physical object identities changed across mixed persistence: {suffix}, cycle {cycle}")
                clone = identities[f"integrated-local-clone-{suffix}.dxf"]
                check({key: clone.get(key) for key in baseline} == baseline,
                      "Local cloning changed original database identities: " + suffix)
                source = identities[f"integrated-mapping-source-{suffix}.dxf"]
                mapped = identities[f"integrated-mapped-clone-{suffix}.dxf"]
                external_roles = ("left", "right", "main", "linked") if year < 2018 else ("left", "right", "main")
                for role in external_roles:
                    check(source[role] != mapped[role],
                          "Mapping fixture did not change the external handle: " + suffix + ", " + role)
        print(f"PASS ezdxf {ezdxf.__version__}: 72 integrated fixtures; exact version/transport profiles; "
              "stable mixed-cycle identities; complete cross-module graphs; zero audit errors/repairs")
    except (ValueError, OSError) as error:
        parser.exit(1, "FAIL " + str(error) + "\n")


if __name__ == "__main__":
    main()
