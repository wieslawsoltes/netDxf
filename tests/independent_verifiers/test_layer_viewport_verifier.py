"""Checker self-tests use synthetic/oracle drawings, not netDxf execution evidence."""
from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path
import sys
import tempfile
import unittest

import ezdxf

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))
import verify_layer_viewport_defaults as verifier
import verify_layer_state_mutations as state_verifier


def packets(profile="AutoCad12", stage="source"):
    result = [(0, "SECTION"), (2, "HEADER"), (9, "$ACADVER"), (1, verifier.PROFILES[profile]),
              (0, "ENDSEC"), (0, "SECTION"), (2, "TABLES"), (0, "TABLE"), (2, "LAYER"), (70, 17),
              (0, "LAYER"), (2, "0"), (70, 0), (62, 7), (6, "CONTINUOUS")]
    for flags in range(8):
        for visible in (False, True):
            value = flags | 64 if stage == "source" else flags ^ 2
            result += [(0, "LAYER"), (2, verifier.layer_name(flags, visible)), (70, value),
                       (62, 5 if visible else -5), (6, "CONTINUOUS")]
    result += [(0, "ENDTAB"), (0, "ENDSEC"), (0, "SECTION"), (2, "ENTITIES")]
    for flags in range(8):
        for visible in (False, True):
            x = flags * 2 + int(visible)
            for y in (2, 12):
                result += [(0, "LINE"), (8, verifier.layer_name(flags, visible)),
                           (10, float(x)), (20, float(y)), (30, float(y + 1)),
                           (11, x + .5), (21, float(y + 3)), (31, float(y + 4))]
    return result + [(0, "ENDSEC"), (0, "EOF")]


def oracle_drawings(directory):
    """Use ezdxf to exercise checker loading/auditing, without invoking netDxf."""
    for profile, version in verifier.PROFILES.items():
        for transport in ("text", "binary"):
            doc = ezdxf.new(version)
            doc.layers.remove("Defpoints")
            for flags in range(8):
                for visible in (False, True):
                    name = verifier.layer_name(flags, visible)
                    doc.layers.new(name, dxfattribs={"flags": flags | 64, "color": 5 if visible else -5})
                    x = flags * 2 + int(visible)
                    for y in (2, 12):
                        doc.modelspace().add_line((x, y, y + 1), (x + .5, y + 3, y + 4), dxfattribs={"layer": name})
            for stage in verifier.STAGES:
                if stage == "output":
                    for flags in range(8):
                        for visible in (False, True):
                            doc.layers.get(verifier.layer_name(flags, visible)).dxf.flags = flags ^ 2
                binary = (transport == "binary") != (stage == "output")
                path = directory / f"layer-viewport-default-{profile}-{transport}-{stage}.dxf"
                doc.saveas(path, fmt="bin" if binary else "asc")


class LayerViewportVerifierTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory()
        cls.directory = Path(cls.temp.name)
        oracle_drawings(cls.directory)

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def test_all_packet_profiles_stages_and_corruption_controls(self):
        for profile in verifier.PROFILES:
            for stage in verifier.STAGES:
                with self.subTest(profile=profile, stage=stage):
                    tags = packets(profile, stage)
                    verifier.check_tags(tags, profile, stage)
                    self.assertEqual(9, verifier.controls(tags, profile, stage))

    def test_every_layer_lost_default_rejected(self):
        for profile in verifier.PROFILES:
            for stage in verifier.STAGES:
                tags = packets(profile, stage)
                for index, tag in enumerate(tags):
                    if tag[0] != 70 or index == 9 or tags[index - 1] == (2, "0"):
                        continue
                    with self.subTest(profile=profile, stage=stage, index=index):
                        corrupted = list(tags); corrupted[index] = (70, int(tag[1]) ^ 2)
                        with self.assertRaises(ValueError):
                            verifier.check_tags(corrupted, profile, stage)

    def test_retained_projection_ignores_only_the_edited_flags(self):
        before = verifier.check_tags(packets(), "AutoCad12", "source")
        after = verifier.check_tags(packets(stage="output"), "AutoCad12", "output")
        self.assertEqual(before, after)
        damaged = packets(stage="output")
        index = damaged.index((2, "VP_0_0"))
        damaged.insert(index, (5, "ABCD"))
        self.assertNotEqual(before, verifier.check_tags(damaged, "AutoCad12", "output"))

    def test_unknown_profile_and_stage_rejected(self):
        for profile, stage in (("AutoCad14", "source"), ("AutoCad12", "other")):
            with self.assertRaises(ValueError):
                verifier.check_tags(packets(), profile, stage)

    def test_duplicate_layer_inventory_rejected(self):
        tags = packets()
        index = tags.index((2, "VP_0_1"))
        tags[index] = (2, "VP_0_0")
        with self.assertRaises(ValueError):
            verifier.check_tags(tags, "AutoCad12", "source")

    def test_geometry_inventory_rejected(self):
        tags = packets(); index = tags.index((0, "LINE"))
        end = next(i for i in range(index + 1, len(tags)) if tags[i][0] == 0)
        for damaged in (tags[:index] + tags[end:], tags[:index] + tags[index:end] + tags[index:]):
            with self.assertRaises(ValueError):
                verifier.check_tags(damaged, "AutoCad12", "source")

    def test_all_42_oracle_drawings_and_audits(self):
        output = StringIO()
        with redirect_stdout(output):
            verifier.main(self.directory)
        self.assertIn("42 drawings", output.getvalue())
        self.assertIn("380 corruption/inventory controls rejected", output.getvalue())

    def test_missing_or_extra_drawings_rejected(self):
        path = self.directory / min(verifier.names())
        original = path.read_bytes()
        path.unlink()
        try:
            with self.assertRaises(ValueError): verifier.main(self.directory)
        finally:
            path.write_bytes(original)
        extra = self.directory / "layer-viewport-default-extra.dxf"
        extra.write_bytes(b"extra")
        try:
            with self.assertRaises(ValueError): verifier.main(self.directory)
        finally:
            extra.unlink()

    def test_wrong_transport_rejected(self):
        path = self.directory / "layer-viewport-default-AutoCad12-text-source.dxf"
        original = path.read_bytes()
        wrong = self.directory / "layer-viewport-default-AutoCad12-binary-source.dxf"
        path.write_bytes(wrong.read_bytes())
        try:
            with self.assertRaisesRegex(ValueError, "Wrong transport"):
                verifier.main(self.directory)
        finally:
            path.write_bytes(original)

    def test_saved_state_five_flag_truth_table(self):
        layers = {"0": "10", "Walls": "11"}
        linetypes = {"Continuous": "12", "OLD_DASH": "13", "NEW_DASH": "14"}
        for selected in range(32):
            for changed in (False, True):
                row = state_verifier.expected_state("A", "B", selected, changed, layers, linetypes)
                flags = [value for code, value in row if code == 90][-1]
                expected = 63 if not changed else ((63 & ~selected) | (2 & selected))
                self.assertEqual(expected, flags)


if __name__ == "__main__":
    unittest.main()
