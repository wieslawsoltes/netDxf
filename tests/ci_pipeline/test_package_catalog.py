"""The complete package family and installed-consumer evidence fail closed."""
from pathlib import Path
import copy
import importlib.util
import json
import re
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ci'))
import package_catalog
import pipeline
import shadow_packages
from package_fixtures import write_package, write_catalog


class PackageCatalogTests(unittest.TestCase):
    def test_every_source_library_and_direct_dependency_is_reviewed(self):
        package_catalog.validate_source(ROOT)
        self.assertEqual(33, len(package_catalog.PACKAGES))
        self.assertEqual(32, sum(item.frameworks == ('net8.0',) for item in package_catalog.PACKAGES))

    def test_new_library_or_changed_dependency_is_not_silently_published(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for item in package_catalog.PACKAGES:
                path = root / item.project; path.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(ROOT / item.project, path)
            package_catalog.validate_source(root)
            path = root / 'shadow/src/new/new.csproj'; path.parent.mkdir(parents=True)
            path.write_text('<Project/>')
            with self.assertRaisesRegex(ValueError, 'Source libraries'):
                package_catalog.validate_source(root)
            path.unlink()
            path = root / package_catalog.BY_ID['netDxf.Shadow.Exchange.Dwg'].project
            path.write_text(path.read_text().replace('Version="3.8.0"', 'Version="3.9.0"'))
            with self.assertRaisesRegex(ValueError, 'Source dependencies'):
                package_catalog.validate_source(root)

    def test_exact_package_and_symbol_archive_inventory(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); write_catalog(root)
            pipeline.inspect_packages(root, '3.0.2', 'a'*40)
            for extension in ('nupkg', 'snupkg'):
                item = package_catalog.PACKAGES[-1]
                path = root / f'{item.id}.3.0.2.{extension}'
                original = path.read_bytes(); path.unlink()
                with self.assertRaisesRegex(ValueError, 'package inventory'):
                    pipeline.inspect_packages(root, '3.0.2', 'a'*40)
                path.write_bytes(original)
                unexpected = root / ('extra.' + extension); unexpected.write_bytes(original)
                with self.assertRaisesRegex(ValueError, 'package inventory'):
                    pipeline.inspect_packages(root, '3.0.2', 'a'*40)
                unexpected.unlink()

    def test_shared_internal_and_pinned_external_nuspec_dependencies(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'test.nupkg'
            for item in (package_catalog.BY_ID['netDxf.Shadow.NetDxf'],
                         package_catalog.BY_ID['netDxf.Shadow.Rendering.SkiaSharp']):
                write_package(path, item)
                pipeline.inspect_package(path, '3.0.2', 'a'*40, package_id=item.id)
                for mutation in ('version', 'missing', 'extra', 'framework', 'assets', 'exclude-runtime', 'exclude-compile'):
                    def change(metadata):
                        group = metadata.find('./dependencies/group')
                        if mutation == 'version': group[0].set('version', '1.0.0')
                        elif mutation == 'missing': group.remove(group[0])
                        elif mutation == 'extra': ET.SubElement(group, 'dependency', id='Unexpected', version='1.0.0')
                        elif mutation == 'framework': group.set('targetFramework', 'net9.0')
                        elif mutation == 'assets': group[0].set('include', 'build')
                        else: group[0].set('exclude', mutation.removeprefix('exclude-'))
                    write_package(path, item, mutate=change)
                    with self.subTest(package=item.id, mutation=mutation), self.assertRaises(ValueError):
                        pipeline.inspect_package(path, '3.0.2', 'a'*40, package_id=item.id)

    def test_documentation_license_symbols_and_archive_paths_are_verified(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'test.nupkg'; item = package_catalog.PACKAGES[1]
            mutations = {
                'README.md': b'', 'LICENSE': b'', '../escape': b'escape',
                'lib/net8.0/netDxf.Shadow.xml': b'<doc><assembly><name>Other</name></assembly></doc>',
                'lib/net8.0/netDxf.Shadow.dll': b'XX'+b'0'*150,
            }
            for name, payload in mutations.items():
                write_package(path, item)
                with zipfile.ZipFile(path) as archive:
                    entries = {entry: archive.read(entry) for entry in archive.namelist()}
                entries[name] = payload
                with zipfile.ZipFile(path, 'w') as archive:
                    for entry, data in entries.items(): archive.writestr(entry, data)
                with self.subTest(entry=name), self.assertRaises(ValueError):
                    pipeline.inspect_package(path, '3.0.2', 'a'*40, package_id=item.id)
            write_package(path, item, symbols=True)
            with zipfile.ZipFile(path) as archive:
                entries = {entry: archive.read(entry) for entry in archive.namelist()}
            entries['lib/net8.0/netDxf.Shadow.pdb'] = b'legacy'+b'0'*150
            with zipfile.ZipFile(path, 'w') as archive:
                for entry, data in entries.items(): archive.writestr(entry, data)
            with self.assertRaisesRegex(ValueError, 'portable PDB'):
                pipeline.inspect_package(path, '3.0.2', 'a'*40, symbols=True, package_id=item.id)

    def test_manifest_binds_package_targets_and_dependency_versions(self):
        build = {'version':'4.0.0-preview.1', 'package':pipeline.PACKAGE, 'frameworks':list(pipeline.TFMS),
                 'packages':package_catalog.manifest('4.0.0-preview.1')}
        pipeline.validate_build_manifest(build)
        for mutation in ('missing', 'duplicate', 'dependency', 'target'):
            candidate = copy.deepcopy(build)
            if mutation == 'missing': candidate['packages'].pop()
            elif mutation == 'duplicate': candidate['packages'][-1] = candidate['packages'][0]
            elif mutation == 'dependency': candidate['packages'][2]['dependencies']['netDxf.Shadow'] = '0.1.0'
            else: candidate['packages'][1]['frameworks'] = ['net9.0']
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                pipeline.validate_build_manifest(candidate)

    def test_release_document_inventory_matches_the_catalog(self):
        text = (ROOT / 'doc/CI-RELEASE.md').read_text(encoding='utf-8')
        rows = re.findall(r'^\| `([^`]+)` \| (.+) \|$', text, re.MULTILINE)
        self.assertEqual({item.id: item.frameworks for item in package_catalog.PACKAGES},
                         {name: tuple(re.findall(r'`([^`]+)`', targets)) for name, targets in rows})
        self.assertIn(str(len(package_catalog.PACKAGES) * 2) + ' package archives', text)

    def test_static_asset_payload_mounts_imports_and_notices_cannot_disappear(self):
        for identity in ('netDxf.Shadow.Studio.Web', 'netDxf.Shadow.Markdown'):
            item = package_catalog.BY_ID[identity]
            with tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary) / 'web.nupkg'; write_package(path, item)
                pipeline.inspect_package(path, '3.0.2', 'a'*40, package_id=item.id)
                with zipfile.ZipFile(path) as archive:
                    original = {name: archive.read(name) for name in archive.namelist()}
                payload = next(name for name in original if name.startswith('staticwebassets/'))
                for mutation in ('missing', 'changed', 'extra', 'missing-import', 'wrong-import', 'wrong-mount', 'missing-notice'):
                    if mutation == 'missing-notice' and identity != 'netDxf.Shadow.Markdown':
                        continue
                    entries = dict(original)
                    if mutation == 'missing': del entries[payload]
                    elif mutation == 'changed': entries[payload] += b'changed'
                    elif mutation == 'extra': entries['staticwebassets/unreviewed.js'] = b'unexpected'
                    elif mutation == 'missing-import': del entries['buildTransitive/' + item.id + '.props']
                    elif mutation == 'wrong-import': entries['buildTransitive/' + item.id + '.props'] = b'<Project />'
                    elif mutation == 'wrong-mount':
                        name = 'build/Microsoft.AspNetCore.StaticWebAssets.props'
                        root = ET.fromstring(entries[name]); root.find('.//BasePath').text = 'wrong-prefix'
                        entries[name] = ET.tostring(root)
                    else: del entries['THIRD-PARTY-NOTICES.txt']
                    with zipfile.ZipFile(path, 'w') as archive:
                        for name, data in entries.items(): archive.writestr(name, data)
                    with self.subTest(package=identity, mutation=mutation), self.assertRaises(ValueError):
                        pipeline.inspect_package(path, '3.0.2', 'a'*40, package_id=item.id)


class ShadowConsumerTests(unittest.TestCase):
    def test_consumer_uses_only_exact_installed_catalog_package_references(self):
        root = ET.parse(ROOT / 'tests/netDxf.Shadow.PackageSmoke/netDxf.Shadow.PackageSmoke.csproj')
        references = root.findall('./ItemGroup/PackageReference')
        self.assertFalse(root.findall('.//ProjectReference'))
        self.assertEqual(set(package_catalog.BY_ID), {item.get('Include') for item in references})
        self.assertEqual(33, len(references))
        self.assertTrue(all(item.get('Version') == '[$(PackageVersion)]' for item in references))
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'nuget.config'
            shadow_packages.source_mapping(path, Path(temporary))
            config = ET.parse(path)
            self.assertIsNotNone(config.find('./packageSources/clear'))
            self.assertIsNotNone(config.find('./fallbackPackageFolders/clear'))
            local = config.findall('./packageSourceMapping/packageSource[@key="candidate"]/package')
            self.assertEqual(set(package_catalog.BY_ID), {item.get('pattern') for item in local})
            public = config.findall('./packageSourceMapping/packageSource[@key="public"]/package')
            self.assertEqual(['*'], [item.get('pattern') for item in public])

    def test_source_bound_smoke_receipt_rejects_substitution_and_missing_work(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); write_catalog(root)
            source = {'commit':'a'*40, 'tree':'b'*40}
            log = root / shadow_packages.LOG; log.write_text('Synthetic receipt validation, not execution.\n')
            packages, hashes = shadow_packages.package_hashes(root, '3.0.2')
            data = {'schema':1, 'passed':True, 'version':'3.0.2', **source, 'host':'Linux', 'runtime_version':'8.0.31',
                    'scenarios':list(shadow_packages.SCENARIOS), 'packages':packages, 'log_sha256':pipeline.digest(log),
                    'assemblies':[{'id':key, 'sha256':value, 'framework':'.NETCoreApp,Version=v8.0',
                                   'informational_version':'3.0.2+'+'a'*40} for key,value in hashes.items()]}
            receipt = root / shadow_packages.RECEIPT
            receipt.write_text(json.dumps(data))
            self.assertEqual(33, shadow_packages.verify(root, '3.0.2', source)['packages'])
            for mutation in ('source', 'version', 'missing-scenario', 'duplicate-scenario', 'missing-assembly',
                             'wrong-assembly', 'wrong-target', 'wrong-info-version', 'wrong-package', 'wrong-log', 'failed', 'wrong-runtime'):
                candidate = copy.deepcopy(data)
                if mutation == 'source': candidate['commit'] = 'c'*40
                elif mutation == 'version': candidate['version'] = '3.0.3'
                elif mutation == 'missing-scenario': candidate['scenarios'].pop()
                elif mutation == 'duplicate-scenario': candidate['scenarios'][1] = candidate['scenarios'][0]
                elif mutation == 'missing-assembly': candidate['assemblies'].pop()
                elif mutation == 'wrong-assembly': candidate['assemblies'][1]['sha256'] = 'c'*64
                elif mutation == 'wrong-target': candidate['assemblies'][1]['framework'] = '.NETStandard,Version=v2.0'
                elif mutation == 'wrong-info-version': candidate['assemblies'][1]['informational_version'] = '0.1.0'
                elif mutation == 'wrong-package': candidate['packages'][1]['sha256'] = 'c'*64
                elif mutation == 'wrong-log': candidate['log_sha256'] = 'c'*64
                elif mutation == 'wrong-runtime': candidate['runtime_version'] = '9.0.0'
                else: candidate['passed'] = False
                receipt.write_text(json.dumps(candidate))
                with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                    shadow_packages.verify(root, '3.0.2', source)

    def test_consumer_process_failure_is_not_masked(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); log = root / 'run.log'
            command = [sys.executable, '-c', 'import sys; print("synthetic failure"); sys.exit(17)']
            with self.assertRaises(shadow_packages.subprocess.CalledProcessError) as raised:
                shadow_packages.execute(command, log, dict(shadow_packages.os.environ), root)
            self.assertEqual(17, raised.exception.returncode)
            self.assertIn('synthetic failure', log.read_text())


if __name__ == '__main__':
    unittest.main()
