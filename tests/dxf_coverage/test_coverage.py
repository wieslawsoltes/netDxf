"""Documentation integrity tests; not a substitute for format conformance fixtures."""
from __future__ import annotations
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("coverage_generator", ROOT / "tools/generate_dxf_coverage.py")
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class CoverageTests(unittest.TestCase):
    def setUp(self) -> None:
        self.data = json.loads(MODULE.MANIFEST.read_text(encoding="utf-8"))

    def reject(self, change) -> None:
        change(self.data)
        with self.assertRaises(ValueError):
            MODULE.validate(self.data, ROOT)

    def test_current_ledger(self) -> None:
        MODULE.validate(self.data, ROOT)

    def test_deterministic_render(self) -> None:
        before = copy.deepcopy(self.data)
        first = MODULE.render(self.data)
        self.assertEqual(first, MODULE.render(self.data))
        self.assertEqual(before, self.data)
        self.assertEqual(first, MODULE.OUTPUT.read_text(encoding="utf-8"))

    def test_duplicate_feature(self) -> None:
        self.reject(lambda d: d["features"].append(copy.deepcopy(d["features"][0])))

    def test_missing_version_cell(self) -> None:
        self.reject(lambda d: d["features"][0]["status"].pop("AC1009"))

    def test_unknown_status(self) -> None:
        self.reject(lambda d: d["features"][0]["status"].update(AC1032="complete"))

    def test_missing_evidence(self) -> None:
        self.reject(lambda d: d["features"][0].update(evidence=["NOT_FOUND"]))

    def test_evidence_path_traversal(self) -> None:
        self.reject(lambda d: d["evidence"]["B"].update(path="../outside.md"))

    def test_missing_evidence_file(self) -> None:
        self.reject(lambda d: d["evidence"]["B"].update(path="doc/not-a-real-file.md"))

    def test_typed_historical_claim(self) -> None:
        self.reject(lambda d: d["features"][0]["status"].update(AC1009="T"))

    def test_duplicate_profile(self) -> None:
        self.reject(lambda d: d["profiles"].append(copy.deepcopy(d["profiles"][0])))

    def test_wrong_source_pin(self) -> None:
        self.reject(lambda d: d["source"].update(commit="netstandard"))

    def test_conflicting_admission(self) -> None:
        self.reject(lambda d: d["unadmitted_profiles"].append({"id": "AC1032"}))

    def test_no_completeness_from_generator(self) -> None:
        self.reject(lambda d: d["qualification"].update(full_standard_complete=True))

    def test_markdown_cell_escaping(self) -> None:
        self.assertEqual("a\\|b c d", MODULE.cell("a|b\nc\rd"))

    def test_evidence_symlink_escape(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / "repo").mkdir()
            (path / "outside.md").write_text("not repository evidence")
            try:
                (path / "repo/link.md").symlink_to(path / "outside.md")
            except (OSError, NotImplementedError):
                self.skipTest("Platform does not permit this test process to create symbolic links")
            self.data["evidence"] = {"B": {"path": "link.md", "label": "escaped"}}
            with self.assertRaises(ValueError):
                MODULE.validate(self.data, path / "repo")


if __name__ == "__main__":
    unittest.main()
