"""ezdxf oracle drawings exercise the checker, not the modified C# implementation."""
from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path
import sys
import tempfile
import unittest
import ezdxf

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))
import verify_viewport_frozen_layers as verifier


def oracle_drawings(directory):
    for profile, version in verifier.PROFILES.items():
        for transport in ("text", "binary"):
            doc = ezdxf.new(version); doc.layers.remove("Defpoints")
            doc.layers.new("FROZEN_A", dxfattribs={"flags": 2})
            doc.layers.new("FROZEN_B")
            layout = doc.layouts.new("FrozenWire")
            first = layout.add_viewport((10, 20), (6, 4), (0, 0), 250, dxfattribs={"layer": "0"})
            second = layout.add_viewport((40, 50), (8, 5), (0, 0), 250, dxfattribs={"layer": "0"})
            first.frozen_layers = ["FROZEN_A"]; second.frozen_layers = ["FROZEN_B"]
            for stage in verifier.STAGES:
                if stage == "output":
                    first.frozen_layers = ["FROZEN_B"]
                    doc.layers.get("FROZEN_A").dxf.flags = 0; doc.layers.get("FROZEN_B").dxf.flags = 2
                path = directory / f"viewport-frozen-{profile}-{transport}-{stage}.dxf"
                binary = (transport == "binary") != (stage == "output")
                doc.saveas(path, fmt="bin" if binary else "asc")


class ViewportFrozenVerifierTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(); cls.directory = Path(cls.temp.name)
        oracle_drawings(cls.directory)

    @classmethod
    def tearDownClass(cls): cls.temp.cleanup()

    def test_complete_oracle_matrix(self):
        output = StringIO()
        with redirect_stdout(output): verifier.main(self.directory)
        self.assertIn("36 drawings", output.getvalue())
        self.assertIn("326 corruption/inventory controls rejected", output.getvalue())

    def test_all_raw_packets_and_corruption_controls(self):
        for profile in verifier.PROFILES:
            for transport in ("text", "binary"):
                for stage in verifier.STAGES:
                    with self.subTest(profile=profile, transport=transport, stage=stage):
                        tags = verifier.load_tags(self.directory / f"viewport-frozen-{profile}-{transport}-{stage}.dxf")
                        verifier.check_tags(tags, profile, stage)
                        self.assertEqual(9, verifier.controls(tags, profile, stage))

    def test_unselected_packet_change(self):
        tags = verifier.load_tags(self.directory / "viewport-frozen-AutoCad2018-text-output.dxf")
        original = verifier.check_tags(tags, "AutoCad2018", "output")
        at = next(i for i, tag in enumerate(tags) if tag == (2, "FROZEN_A"))
        color = next(i for i in range(at, len(tags)) if tags[i][0] == 62)
        bad = tags.copy(); bad[color] = (62, 4)
        self.assertNotEqual(original, verifier.check_tags(bad, "AutoCad2018", "output"))

    def test_old_membership_at_output_rejected(self):
        tags = verifier.load_tags(self.directory / "viewport-frozen-AutoCad2018-text-source.dxf")
        with self.assertRaises(ValueError): verifier.check_tags(tags, "AutoCad2018", "output")

    def test_missing_file_rejected(self):
        path = self.directory / "viewport-frozen-AutoCad2018-text-source.dxf"
        hidden = path.with_suffix(".hidden"); path.rename(hidden)
        try:
            with self.assertRaises(ValueError): verifier.main(self.directory)
        finally: hidden.rename(path)

    def test_extra_file_rejected(self):
        path = self.directory / "viewport-frozen-extra.dxf"; path.write_bytes(b"invalid")
        try:
            with self.assertRaises(ValueError): verifier.main(self.directory)
        finally: path.unlink()


if __name__ == "__main__": unittest.main()
