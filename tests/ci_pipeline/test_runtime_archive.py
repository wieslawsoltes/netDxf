"""Synthetic receipts test validation only; they are not native runtime executions."""
from pathlib import Path
import copy
import hashlib
import io
import json
import os
import stat
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ci'))
import pipeline
import runtime_assets
import runtime_release


class RuntimeArchiveTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.packages = self.root/'packages'; self.packages.mkdir()
        self.reports = self.root/'reports'; self.reports.mkdir()
        self.identity = {'commit':'a'*40,'tree':'b'*40}
        self.identity_patch = patch.object(pipeline,'identity',return_value=self.identity)
        self.identity_patch.start(); self.addCleanup(self.identity_patch.stop)
        self.build = {**self.identity,'package':pipeline.PACKAGE,'frameworks':list(pipeline.TFMS),'version':'3.0.2'}
        (self.packages/'build.json').write_text(json.dumps(self.build))
        self.dlls = {t:b'MZ'+(t*50).encode() for t in pipeline.TFMS}
        for extension in ('nupkg','snupkg'):
            path = self.packages/f'{pipeline.PACKAGE}.3.0.2.{extension}'
            with zipfile.ZipFile(path,'w') as z:
                z.writestr('test.nuspec',f'<package><metadata><id>{pipeline.PACKAGE}</id><version>3.0.2</version><repository url="{pipeline.REPOSITORY}" commit="{self.identity["commit"]}"/><license>MIT</license></metadata></package>')
                for target in pipeline.TFMS:
                    if extension == 'nupkg':
                        z.writestr(f'lib/{target}/{pipeline.PACKAGE}.dll',self.dlls[target])
                        z.writestr(f'lib/{target}/{pipeline.PACKAGE}.xml','<doc/>')
                    else:z.writestr(f'lib/{target}/{pipeline.PACKAGE}.pdb',b'BSJB'+b'0'*101)
        package_hash = pipeline.digest(self.packages/f'{pipeline.PACKAGE}.3.0.2.nupkg')
        for index,(host,runtime,asset) in enumerate(runtime_assets.MATRIX):
            directory = self.reports/str(index); directory.mkdir()
            framework = runtime in ('net471','net48')
            data = {'schema':'1','passed':'true','scenarios':'12','host':host,'runtime':runtime,'asset':asset,
                **self.identity,'package_sha256':package_hash,'assembly_sha256':hashlib.sha256(self.dlls[asset]).hexdigest(),
                'asset_framework':runtime_assets.MONIKERS[asset],'consumer_framework':runtime_assets.MONIKERS[runtime],
                'runtime_version':'4.0.30319.42000' if framework else ('6.0.36' if runtime=='net6.0' else '8.0.31'),
                'framework_release':'533325' if framework else '', 'assembly_path':'/unit-test/netDxf.netstandard.dll'}
            ET.ElementTree(ET.Element('netdxf-target-smoke',data)).write(directory/'receipt.xml',encoding='utf-8',xml_declaration=True)
            (directory/'run.log').write_text('Synthetic validation fixture; no runtime execution claimed.\n')
            result={'schema':1,'passed':True,'observed':data,'receipt_sha256':pipeline.digest(directory/'receipt.xml'),
                'log_sha256':pipeline.digest(directory/'run.log')}
            (directory/'result.json').write_text(json.dumps(result))
        pipeline.seal(self.packages)
        self.archive=self.root/'runtime.zip'

    def capture(self):
        return runtime_release.capture(self.packages,self.reports,self.archive)

    def entries(self):
        with zipfile.ZipFile(self.archive) as z:return [(copy.copy(i),z.read(i)) for i in z.infolist()]

    def replace_archive(self,entries):
        with zipfile.ZipFile(self.archive,'w') as z:
            for info,data in entries:z.writestr(info,data)

    def test_complete_archive_replays_and_is_deterministic(self):
        result=self.capture()
        self.assertEqual(8,result['profiles']); self.assertEqual(96,result['scenario_executions'])
        self.assertEqual(result,runtime_release.verify(self.packages,self.archive))
        other=self.root/'second.zip'; runtime_release.capture(self.packages,self.reports,other)
        self.assertEqual(self.archive.read_bytes(),other.read_bytes())
        with zipfile.ZipFile(other) as z:
            self.assertEqual(25,len(z.namelist()))
            self.assertEqual(set(runtime_release.inventory()),set(z.namelist()))
        with self.assertRaises(ValueError):self.capture()

    def test_missing_duplicate_extra_and_unsafe_entries_reject(self):
        self.capture(); original=self.archive.read_bytes(); entries=self.entries()
        cases=[entries[:-1],entries+[entries[1]],entries+[(zipfile.ZipInfo('../escape'),b'x')],
               [(zipfile.ZipInfo('/absolute'),entries[0][1])]+entries[1:]]
        for index,broken in enumerate(cases):
            with self.subTest(index=index):
                self.replace_archive(broken)
                with self.assertRaises(ValueError):runtime_release.verify(self.packages,self.archive)
                self.archive.write_bytes(original)

    def test_linked_and_oversized_archives_and_members_reject(self):
        self.capture(); original=self.archive.read_bytes(); entries=self.entries()
        info,data=entries[1]; info.external_attr=(stat.S_IFLNK|0o777)<<16
        self.replace_archive(entries)
        with self.assertRaises(ValueError):runtime_release.verify(self.packages,self.archive)
        self.archive.write_bytes(original)
        for name in ('MAX_ARCHIVE_BYTES','MAX_TOTAL_BYTES','MAX_LOG_BYTES'):
            with self.subTest(limit=name),patch.object(runtime_release,name,1),self.assertRaises(ValueError):
                runtime_release.verify(self.packages,self.archive)
        link=self.root/'link.zip'
        try:link.symlink_to(self.archive)
        except OSError:pass # Windows CI may not grant symbolic-link creation.
        else:
            with self.assertRaises(ValueError):runtime_release.verify(self.packages,link)

    def test_changed_receipt_or_log_rejects_even_with_valid_zip_crc(self):
        self.capture(); original=self.archive.read_bytes()
        for suffix in ('receipt.xml','run.log','result.json'):
            entries=self.entries()
            for i,(info,data) in enumerate(entries):
                if info.filename.endswith('/'+suffix):
                    entries[i]=(info,json.dumps(dict(json.loads(data),passed=False)).encode() if suffix=='result.json' else data+b' ');break
            self.replace_archive(entries)
            with self.subTest(suffix=suffix),self.assertRaises(ValueError):runtime_release.verify(self.packages,self.archive)
            self.archive.write_bytes(original)

    def test_summary_and_self_consistent_wrong_asset_are_not_trusted(self):
        self.capture(); original=self.archive.read_bytes()
        entries=self.entries()
        for i,(info,data) in enumerate(entries):
            if info.filename==runtime_release.SUMMARY_NAME:
                summary=json.loads(data);summary['profiles']=7;entries[i]=(info,json.dumps(summary).encode())
        self.replace_archive(entries)
        with self.assertRaises(ValueError):runtime_release.verify(self.packages,self.archive)
        self.archive.write_bytes(original)
        # Rewrite all local hashes/observations coherently, but use another DLL.
        work=self.reports/'0';report=json.loads((work/'result.json').read_text())
        data=report['observed']; data['assembly_sha256']=hashlib.sha256(self.dlls['net8.0']).hexdigest()
        ET.ElementTree(ET.Element('netdxf-target-smoke',data)).write(work/'receipt.xml',encoding='utf-8',xml_declaration=True)
        report['receipt_sha256']=pipeline.digest(work/'receipt.xml');(work/'result.json').write_text(json.dumps(report))
        with self.assertRaises(ValueError):runtime_release.capture(self.packages,self.reports,self.root/'wrong.zip')
        self.assertFalse((self.root/'wrong.zip').exists())

    def test_source_and_package_are_bound(self):
        self.capture()
        with patch.object(pipeline,'identity',return_value={**self.identity,'commit':'f'*40}),self.assertRaises(ValueError):
            runtime_release.verify(self.packages,self.archive)
        package=self.packages/f'{pipeline.PACKAGE}.3.0.2.nupkg'
        with zipfile.ZipFile(package,'a') as z:z.writestr('different-content',b'changed')
        pipeline.seal(self.packages)
        with self.assertRaises(ValueError):runtime_release.verify(self.packages,self.archive)

    def conformance(self):
        evidence=self.root/'conformance'
        for host in ('ubuntu-latest','windows-latest'):
            for config in ('Debug','Release'):
                directory=evidence/f'dxf-conformance-{host}-{config}'
                (directory/'conformance').mkdir(parents=True)
                (directory/'ci-source.json').write_text(json.dumps(self.identity))
                (directory/'conformance/results.json').write_text('[{"name":"synthetic-unit-case","passed":true}]')
        directory=evidence/'dxf-conformance-ubuntu-latest-Release/independent';directory.mkdir()
        records=[{'script':p.relative_to(ROOT).as_posix(),'passed':True,'exit_code':0,'timed_out':False} for p in (ROOT/'tools').glob('verify_*.py')]
        (directory/'results.json').write_text(json.dumps({'results':records,'passed':len(records),'failed':0}))
        return evidence

    def test_qualification_requires_archive_and_publication_replays_it(self):
        evidence=self.conformance()
        with self.assertRaises(ValueError):pipeline.qualify(self.packages,evidence,self.archive)
        self.assertFalse((self.packages/'qualification.json').exists())
        self.capture(); pipeline.qualify(self.packages,evidence,self.archive)
        report=json.loads((self.packages/'qualification.json').read_text())
        self.assertEqual(8,report['runtime_evidence']['profiles']); self.assertEqual(96,report['runtime_evidence']['scenario_executions'])
        with patch.dict(os.environ,{'RELEASE_TAG':'v3.0.2'}):
            pipeline.verify_release(self.packages)
            retained=self.packages/runtime_release.ARCHIVE_NAME
            saved=retained.read_bytes(); retained.unlink();pipeline.seal(self.packages)
            with self.assertRaises(ValueError):pipeline.verify_release(self.packages)
            retained.write_bytes(saved);pipeline.seal(self.packages)
            report['runtime_evidence']['sha256']='0'*64
            (self.packages/'qualification.json').write_text(json.dumps(report));pipeline.seal(self.packages)
            with self.assertRaises(ValueError):pipeline.verify_release(self.packages)

    def test_workflows_create_download_and_require_retained_evidence(self):
        build=(ROOT/'.github/workflows/ci-build.yml').read_text()
        section=build.split('  runtime-evidence:',1)[1]
        self.assertLess(section.index('python tools/ci/runtime_assets.py collect'),section.index('python tools/ci/runtime_release.py capture'))
        self.assertLess(section.index('python tools/ci/runtime_release.py capture'),section.index('actions/upload-artifact@'))
        release=(ROOT/'.github/workflows/release.yml').read_text().split('  qualify:',1)[1].split('  github-release:',1)[0]
        self.assertIn('name: runtime-qualification',release);self.assertIn('path: artifacts/release-runtime',release)
        self.assertLess(release.index('name: runtime-qualification'),release.index('python tools/ci/pipeline.py qualify'))
        publication=(ROOT/'tools/ci/publication.py').read_text()
        self.assertIn('pipeline.verify_release',publication)


if __name__=='__main__':unittest.main()
