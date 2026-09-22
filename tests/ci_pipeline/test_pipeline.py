import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('pipeline', ROOT/'tools/ci/pipeline.py')
p = importlib.util.module_from_spec(spec)
spec.loader.exec_module(p)


class PipelineTests(unittest.TestCase):
    def test_versions(self):
        for value in ('3.0.2', '3.1.0-rc.1', '0.0.0-ci.0'):
            self.assertEqual(value, p.version(value))
        for value in ('', 'v3.0.2', '03.0.2', '3.0', '3.0.2+build', '3.0.2-01', '3.0.2\nx=y', '$(id)', '../3.0.2', '65535.0.0'):
            with self.subTest(value=value), self.assertRaises(ValueError): p.version(value)

    def test_checksum_inventory_and_mutations(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            f = directory/'package.nupkg'; f.write_bytes(b'package')
            p.seal(directory); p.verify(directory)
            f.write_bytes(b'changed')
            with self.assertRaises(ValueError): p.verify(directory)
            f.write_bytes(b'package')
            extra = directory/'extra'; extra.write_text('extra')
            with self.assertRaises(ValueError): p.verify(directory)
            extra.unlink(); f.unlink()
            with self.assertRaises(ValueError): p.verify(directory)

    def test_bad_checksum_names(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            for name in ('../escape', '/absolute', 'SHA256SUMS', '.', '..'):
                (directory/'SHA256SUMS').write_text('0'*64+'  '+name+'\n')
                with self.subTest(name=name), self.assertRaises(ValueError): p.verify(directory)

    def test_result_fail_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)/'results.json'
            for records in ([], [{'name':'a','passed':False}], [{'name':'a','passed':True}]*2, [{'name':'a','passed':1}]):
                path.write_text(json.dumps(records))
                with self.assertRaises(ValueError): p.test_results(path)
            path.write_text('[{"name":"a","passed":true}]'); self.assertEqual(1,len(p.test_results(path)))

    def test_no_branch_publication(self):
        sha = 'a'*40
        with patch.object(p, 'git', return_value=sha):
            with self.assertRaises(ValueError): p.release_plan('branch','netstandard',sha,False)

    def test_moved_tag_and_unmerged_tag_rejected(self):
        sha = 'a'*40
        with patch.object(p, 'git', side_effect=[sha,'b'*40]):
            with self.assertRaises(ValueError): p.release_plan('tag','v3.0.2',sha,False)
        with patch.object(p, 'git', return_value=sha), patch.object(p.subprocess,'run',side_effect=p.subprocess.CalledProcessError(1,['git'])):
            with self.assertRaises(p.subprocess.CalledProcessError): p.release_plan('tag','v3.0.2',sha,False)

    def test_publishing_and_prerelease(self):
        sha = 'a'*40
        with patch.object(p, 'git', return_value=sha), patch.object(p.subprocess, 'run'):
            result = p.release_plan('tag','v3.0.2-rc.1',sha,False)
            self.assertTrue(result['publish']); self.assertTrue(result['prerelease'])
            self.assertFalse(p.release_plan('tag','v3.0.2',sha,True)['publish'])



class QualificationTests(unittest.TestCase):
    def test_complete_and_incomplete_release_evidence(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); packages = root/'packages'; packages.mkdir(); evidence=root/'tests'
            identity={'commit':'a'*40,'tree':'b'*40}
            (packages/'build.json').write_text(json.dumps({**identity,'version':'3.0.2','package':p.PACKAGE,'frameworks':p.TFMS}))
            p.seal(packages)
            for host in ('ubuntu-latest','windows-latest'):
                for config in ('Debug','Release'):
                    d=evidence/f'dxf-conformance-{host}-{config}'; (d/'conformance').mkdir(parents=True)
                    (d/'ci-source.json').write_text(json.dumps(identity))
                    (d/'conformance/results.json').write_text('[{"name":"one","passed":true}]')
            folder=evidence/'dxf-conformance-ubuntu-latest-Release/independent';folder.mkdir()
            reports=[{'script':s.relative_to(ROOT).as_posix(),'passed':True,'exit_code':0,'timed_out':False} for s in (ROOT/'tools').glob('verify_*.py')]
            result={'results':reports,'passed':len(reports),'failed':0}
            (folder/'results.json').write_text(json.dumps(result))
            with patch.object(p,'identity',return_value=identity):
                p.qualify(packages,evidence);p.verify(packages)
                release=evidence/'dxf-conformance-windows-latest-Release/conformance/results.json'
                release.write_text('[{"name":"substitution","passed":true}]')
                with self.assertRaises(ValueError):p.qualify(packages,evidence)
                release.write_text('[{"name":"one","passed":true}]')
                reports[0]['timed_out']=True;(folder/'results.json').write_text(json.dumps(result))
                with self.assertRaises(ValueError):p.qualify(packages,evidence)
                reports[0]['timed_out']=False;reports.pop();(folder/'results.json').write_text(json.dumps(result))
                with self.assertRaises(ValueError):p.qualify(packages,evidence)
                (evidence/'dxf-conformance-windows-latest-Debug/ci-source.json').write_text(json.dumps({**identity,'commit':'c'*40}))
                with self.assertRaises(ValueError):p.qualify(packages,evidence)

    def test_release_receipt_matrix_is_complete(self):
        import copy
        build={'package':p.PACKAGE,'frameworks':list(p.TFMS)}
        report={**build,'conformance':[{'host':host,'configuration':config,'count':10,'sha256':'d'*64}
                  for host in ('ubuntu-latest','windows-latest') for config in ('Debug','Release')],
                'independent_verifiers':len(list((ROOT/'tools').glob('verify_*.py'))),
                'independent_report_sha256':'e'*64}
        p.validate_qualification(report,build)
        for mutation in ('duplicate','zero','unequal','wrong-target','missing-verifier','bad-hash'):
            broken=copy.deepcopy(report)
            if mutation=='duplicate':broken['conformance'][3]=broken['conformance'][0]
            elif mutation=='zero':broken['conformance'][0]['count']=0
            elif mutation=='unequal':broken['conformance'][0]['count']=11
            elif mutation=='wrong-target':broken['frameworks'].pop()
            elif mutation=='missing-verifier':broken['independent_verifiers']-=1
            else:broken['conformance'][0]['sha256']='not-a-hash'
            with self.subTest(mutation=mutation),self.assertRaises(ValueError):p.validate_qualification(broken,build)

    def test_package_target_and_source_validation(self):
        import zipfile
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/'test.nupkg'
            def make(targets=p.TFMS,sha='a'*40,ver='3.0.2'):
                with zipfile.ZipFile(path,'w') as z:
                    z.writestr('test.nuspec',f'<package><metadata><id>{p.PACKAGE}</id><version>{ver}</version><repository url="{p.REPOSITORY}" commit="{sha}"/><license>MIT</license></metadata></package>')
                    for tfm in targets:
                        z.writestr(f'lib/{tfm}/{p.PACKAGE}.dll',b'MZ'+b'0'*100)
                        z.writestr(f'lib/{tfm}/{p.PACKAGE}.xml','<doc/>')
            make();p.inspect_package(path,'3.0.2','a'*40)
            for args in ({'targets':p.TFMS[:-1]},{'sha':'b'*40},{'ver':'3.0.3'}):
                make(**args)
                with self.assertRaises(ValueError):p.inspect_package(path,'3.0.2','a'*40)

if __name__ == '__main__': unittest.main()
