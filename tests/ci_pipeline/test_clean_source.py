"""Exercise source receipts against real disposable Git repositories, not mocked status."""
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('clean_pipeline', ROOT / 'tools/ci/pipeline.py')
pipeline = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pipeline)


class CleanSourceTests(unittest.TestCase):
    def test_real_checkout_changes_reject_source_receipts(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            def git(*args):
                return subprocess.check_output(['git', *args], cwd=root, text=True, stderr=subprocess.DEVNULL).strip()
            git('init', '-q')
            git('config', 'user.name', 'Source Guard Test')
            git('config', 'user.email', 'source-guard@example.invalid')
            git('config', 'core.autocrlf', 'false')
            (root / '.gitignore').write_text('artifacts/\nobj/\n', encoding='utf-8')
            source = root / 'source.cs'
            source.write_text('class Source {}\n', encoding='utf-8')
            git('add', '.'); git('commit', '-qm', 'test baseline')
            expected = {'commit': git('rev-parse', 'HEAD'), 'tree': git('rev-parse', 'HEAD^{tree}')}
            with patch.object(pipeline, 'ROOT', root):
                self.assertEqual(expected, pipeline.identity())
                for mutation in ('unstaged', 'staged', 'deleted', 'untracked', 'renamed', 'ignore-file'):
                    with self.subTest(mutation=mutation):
                        if mutation in ('unstaged', 'staged'):
                            source.write_text('class Changed {}\n', encoding='utf-8')
                            if mutation == 'staged': git('add', 'source.cs')
                        elif mutation == 'deleted': source.unlink()
                        elif mutation == 'untracked': (root / 'extra.cs').write_text('class Extra {}', encoding='utf-8')
                        elif mutation == 'renamed': git('mv', 'source.cs', 'renamed.cs')
                        else: (root / '.gitignore').write_text('*\n', encoding='utf-8')
                        try:
                            with self.assertRaisesRegex(ValueError, 'dirty'):
                                pipeline.identity()
                        finally:
                            # Cleanup only this throwaway repository, including when testing the old code.
                            git('reset', '--hard', '-q', 'HEAD')
                            for name in ('extra.cs', 'renamed.cs'):
                                (root / name).unlink(missing_ok=True)
                        self.assertEqual(expected, pipeline.identity())
                # Legitimate ignored build products do not invalidate source identity.
                (root / 'artifacts').mkdir(); (root / 'artifacts/result.json').write_text('{}', encoding='utf-8')
                (root / 'obj').mkdir(); (root / 'obj/build.cache').write_text('cache', encoding='utf-8')
                self.assertEqual(expected, pipeline.identity())


if __name__ == '__main__':
    unittest.main()
