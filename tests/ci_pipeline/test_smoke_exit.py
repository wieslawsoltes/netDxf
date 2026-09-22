"""Exercise the real package-consumer workflow body without invoking the SDK."""
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import textwrap
import unittest

ROOT = Path(__file__).resolve().parents[2]


class SmokeExitTests(unittest.TestCase):
    def test_package_smoke_preserves_process_failure(self):
        workflow = (ROOT / '.github/workflows/ci-build.yml').read_text(encoding='utf-8')
        marker = '      - name: Consume only the freshly produced package\n'
        self.assertEqual(1, workflow.count(marker))
        step = workflow.split(marker, 1)[1].split('\n      - ', 1)[0]
        # An unspecified shell is bash -e, which returns tee's status instead.
        shell = re.search(r'^        shell: (.+)$', step, re.MULTILINE)
        self.assertIsNotNone(shell, 'Package smoke must explicitly select a pipefail shell')
        self.assertEqual('bash', shell.group(1))
        script = textwrap.dedent(step.split('        run: |\n', 1)[1])
        self.assertIn('| tee artifacts/packages/smoke.log', script)
        bash = shutil.which('bash')
        if os.name == 'nt':
            installed = Path(os.environ.get('ProgramFiles', 'C:/Program Files')) / 'Git/bin/bash.exe'
            if installed.is_file():
                bash = str(installed)
        self.assertIsNotNone(bash, 'GitHub-hosted runners require Bash for this workflow')
        probe = '''dotnet() {
          if [ "$1" = restore ]; then return "$RESTORE_EXIT"; fi
          printf 'SMOKE_PROBE_EXECUTED\\n'
          return "$SMOKE_EXIT"
        }
        '''
        for restore, smoke, expected in ((0, 0, 0), (0, 23, 23), (17, 0, 17)):
            with self.subTest(restore=restore, smoke=smoke), tempfile.TemporaryDirectory() as temp:
                directory = Path(temp)
                (directory / 'artifacts/packages').mkdir(parents=True)
                env = {**os.environ, 'VERSION': '3.0.1-ci.0',
                       'RESTORE_EXIT': str(restore), 'SMOKE_EXIT': str(smoke)}
                result = subprocess.run([bash, '--noprofile', '--norc', '-eo', 'pipefail', '-c',
                                         textwrap.dedent(probe) + script], cwd=directory, env=env,
                                        capture_output=True, text=True, timeout=20)
                self.assertEqual(expected, result.returncode, result.stdout + result.stderr)
                log = directory / 'artifacts/packages/smoke.log'
                if restore:
                    self.assertFalse(log.exists(), 'A failed restore must not run the consumer')
                else:
                    self.assertEqual('SMOKE_PROBE_EXECUTED\n', log.read_text(encoding='utf-8'))


if __name__ == '__main__':
    unittest.main()
