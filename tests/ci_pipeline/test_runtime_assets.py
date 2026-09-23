"""Target-asset pipeline regressions; external runtimes execute in hosted matrix jobs."""
from pathlib import Path
import json
import re
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools' / 'ci'))
import runtime_assets as target
import pipeline


class RuntimeAssetsTests(unittest.TestCase):
    def values(self, key):
        host, runtime, asset = key
        return {'schema': '1', 'passed': 'true', 'scenarios': '12', 'host': host,
                'runtime': runtime, 'asset': asset, 'commit': 'a' * 40, 'tree': 'b' * 40,
                'package_sha256': 'c' * 64, 'assembly_sha256': 'd' * 64,
                'asset_framework': target.MONIKERS[asset], 'consumer_framework': target.MONIKERS[runtime],
                'runtime_version': '4.0.30319.42000' if runtime in ('net471', 'net48') else ('6.0.36' if runtime == 'net6.0' else '8.0.25'),
                'framework_release': '533325' if runtime in ('net471', 'net48') else '',
                'assembly_path': '/test/netDxf.netstandard.dll'}

    def save(self, path, data):
        ET.ElementTree(ET.Element('netdxf-target-smoke', data)).write(path, encoding='utf-8', xml_declaration=True)

    def test_all_profiles_validate(self):
        self.assertEqual(8, len(target.MATRIX)); self.assertEqual(8, len(set(target.MATRIX)))
        self.assertEqual(set(pipeline.TFMS), {key[2] for key in target.MATRIX})
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'receipt.xml'
            for key in target.MATRIX:
                with self.subTest(profile=key):
                    data = self.values(key); self.save(path, data)
                    self.assertEqual(data, target.validate_receipt(path, data))

    def test_bad_receipts_reject(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'receipt.xml'
            base = self.values(('ubuntu-latest', 'net8.0', 'netstandard2.0'))
            for field, value in (('passed', 'false'), ('scenarios', '11'), ('schema', '2'),
                ('asset_framework', target.MONIKERS['net8.0']), ('consumer_framework', target.MONIKERS['net6.0']),
                ('runtime_version', '9.0.1'), ('framework_release', '533325'), ('commit', 'x'*40),
                ('tree', 'b'*39), ('package_sha256', 'wrong'), ('assembly_sha256', 'wrong'), ('assembly_path', 'other.dll')):
                with self.subTest(field=field):
                    data = dict(base); data[field] = value; self.save(path, data)
                    with self.assertRaises(ValueError): target.validate_receipt(path, {})
            for field in base:
                data = dict(base); del data[field]; self.save(path, data)
                with self.subTest(missing=field), self.assertRaises(ValueError): target.validate_receipt(path, {})
            data = dict(base, unexpected='value'); self.save(path, data)
            with self.assertRaises(ValueError): target.validate_receipt(path, {})
            self.save(path, base)
            with self.assertRaises(ValueError): target.validate_receipt(path, {'assembly_sha256': 'e'*64})
            path.unlink()
            with self.assertRaises(ValueError): target.validate_receipt(path, {})

    def test_framework_runtime_is_observed_not_invented(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'receipt.xml'
            for runtime, minimum in (('net471', 461308), ('net48', 528040)):
                data = self.values(('windows-latest', runtime, runtime))
                data['framework_release'] = str(minimum - 1); self.save(path, data)
                with self.assertRaises(ValueError): target.validate_receipt(path, {})
                data['framework_release'] = str(minimum); self.save(path, data)
                self.assertEqual(str(minimum), target.validate_receipt(path, {})['framework_release'])
                data['runtime_version'] = '8.0.25'; self.save(path, data)
                with self.assertRaises(ValueError): target.validate_receipt(path, {})

    def test_real_process_failure_is_not_a_success_marker(self):
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / 'run.log'
            import os
            target.execute([sys.executable, '-c', 'print("positive process")'], log, dict(os.environ))
            with self.assertRaises(subprocess.CalledProcessError) as failure:
                target.execute([sys.executable, '-c', 'print("PASS: misleading output"); raise SystemExit(23)'], log, dict(os.environ))
            self.assertEqual(23, failure.exception.returncode)
            self.assertIn('misleading output', log.read_text())

    def test_candidate_package_has_exclusive_exact_source(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'nuget.config'
            target.source_mapping(path, Path(directory) / 'candidate & local')
            root = ET.parse(path)
            self.assertIsNotNone(root.find('./packageSources/clear'))
            sources = {e.get('key'): e.get('value') for e in root.findall('./packageSources/add')}
            self.assertEqual({'candidate', 'public'}, set(sources))
            self.assertTrue(sources['candidate'].endswith('candidate & local'))
            mappings = {e.get('key'): [p.get('pattern') for p in e.findall('package')]
                        for e in root.findall('./packageSourceMapping/packageSource')}
            self.assertEqual({'candidate': [pipeline.PACKAGE], 'public': ['*']}, mappings)

    def test_aggregate_rejects_missing_duplicate_or_changed_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory); package = folder / 'candidate.nupkg'; package.write_bytes(b'unit-test candidate')
            build = {'commit': 'a'*40, 'tree': 'b'*40}; hashes = {t: 'd'*64 for t in pipeline.TFMS}
            reports = folder / 'reports'; reports.mkdir()
            for index, key in enumerate(target.MATRIX):
                work = reports / str(index); work.mkdir()
                data = self.values(key); data['package_sha256'] = pipeline.digest(package)
                receipt = work / 'receipt.xml'; self.save(receipt, data)
                log = work / 'run.log'; log.write_text('controlled unit-test process log')
                result = {'schema': 1, 'passed': True, 'observed': data,
                          'receipt_sha256': pipeline.digest(receipt), 'log_sha256': pipeline.digest(log)}
                (work / 'result.json').write_text(json.dumps(result))
            with patch.object(target, 'package_inputs', return_value=(build, package, hashes)):
                summary = target.collect(folder, reports, folder / 'good.json')
                self.assertEqual(8, summary['profiles']); self.assertEqual(96, summary['scenario_executions'])
                one = reports / '0' / 'result.json'; saved = one.read_bytes(); one.unlink()
                with self.assertRaises(ValueError): target.collect(folder, reports, folder / 'missing.json')
                one.write_bytes(saved)
                extra = reports / 'extra'; extra.mkdir(); (extra / 'result.json').write_bytes(saved)
                with self.assertRaises(ValueError): target.collect(folder, reports, folder / 'duplicate.json')
                (extra / 'result.json').unlink()
                receipt = reports / '0' / 'receipt.xml'; data = self.values(target.MATRIX[0]); self.save(receipt, data)
                with self.assertRaises(ValueError): target.collect(folder, reports, folder / 'changed.json')

    def test_actual_workflow_matrix_and_shared_assertion_body(self):
        text = (ROOT / '.github/workflows/ci-build.yml').read_text()
        section = text.split('# Package target/runtime matrix begins', 1)[1].split('# Package target/runtime matrix ends', 1)[0]
        rows = [json.loads(s) for s in re.findall(r'^\s*-\s*(\{[^\n]+\})\s*$', section, re.MULTILINE)]
        self.assertEqual(set(target.MATRIX), {(r['os'], r['runtime'], r['asset']) for r in rows})
        self.assertEqual(len(target.MATRIX), len(rows))
        self.assertIn('python tools/ci/runtime_assets.py run', text)
        self.assertIn('python tools/ci/runtime_assets.py collect', text)
        self.assertIn('needs: package', text); self.assertIn('needs: runtime-smoke', text)
        project = ET.parse(ROOT / 'tests/netDxf.TargetSmoke/netDxf.TargetSmoke.csproj')
        self.assertEqual([], project.findall('.//ProjectReference'))
        self.assertEqual('../netDxf.PackageSmoke/Program.cs', project.find('.//Compile').get('Include'))
        reference = project.find('.//PackageReference')
        self.assertEqual(pipeline.PACKAGE, reference.get('Include'))
        self.assertEqual('compile;runtime', reference.get('ExcludeAssets'))
        self.assertIn('$(PkgnetDxf_netstandard)/lib/$(NetDxfAssetTarget)/', project.findtext('.//Reference/HintPath'))
        smoke = (ROOT / 'tests/netDxf.PackageSmoke/Program.cs').read_text()
        self.assertIn('TargetAssetEvidence.Complete(count);', smoke)
        self.assertIn('Math.BitDecrement(360.0)', smoke)


if __name__ == '__main__':
    unittest.main()
