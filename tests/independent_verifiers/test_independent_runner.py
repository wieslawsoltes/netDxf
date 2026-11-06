"""A failing or hung independent verifier must fail the release gate."""
import importlib.util
from pathlib import Path
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("independent_runner", ROOT / "tools/run_independent_verifiers.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class IndependentRunnerTests(unittest.TestCase):
    def execute(self, source, timeout=10):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            script = root / "verify_probe.py"
            script.write_text(source, encoding="utf-8")
            old_root = MODULE.ROOT
            MODULE.ROOT = root
            try:
                result = MODULE.run_verifier(script, root, root, timeout)
                log = (root / result["log"]).read_text(encoding="utf-8")
                return result, log
            finally:
                MODULE.ROOT = old_root

    def test_failed_verifier_cannot_pass(self):
        result, log = self.execute("import sys\nprint('fixture mismatch')\nsys.exit(7)\n")
        self.assertFalse(result["passed"])
        self.assertEqual(7, result["exit_code"])
        self.assertIn("fixture mismatch", log)

    def test_timeout_retains_evidence_and_fails(self):
        result, log = self.execute("import time\nprint('started', flush=True)\ntime.sleep(10)\n", timeout=0.5)
        self.assertFalse(result["passed"])
        self.assertTrue(result["timed_out"])
        self.assertIn("exceeded", log)

    def test_completed_verifier_retains_stdout_and_stderr(self):
        result, log = self.execute("import sys\nprint('checked fixture')\nprint('audit note', file=sys.stderr)\n")
        self.assertTrue(result["passed"])
        self.assertFalse(result["timed_out"])
        self.assertIn("checked fixture", log)
        self.assertIn("audit note", log)


if __name__ == "__main__":
    unittest.main()
