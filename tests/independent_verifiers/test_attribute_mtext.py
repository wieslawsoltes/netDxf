"""Checker controls are separate from actual C# library execution."""
from pathlib import Path
import sys
import tempfile
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools'))
import verify_attribute_mtext as checker

class AttributeMTextCheckerTests(unittest.TestCase):
    def test_content_chunks_and_terminal_are_independent_of_layout_fields(self):
        row=[(0,'ATTRIB'),(1,'fallback'),(101,'Embedded Object'),(3,'rich'),(40,3.),(1,'\\Ptext'),(7,'BODY')]
        fields,_=checker.payload(row)
        self.assertEqual('rich\\Ptext',fields[1]);self.assertEqual(3.,fields[40]);self.assertEqual('BODY',fields[7])
    def test_duplicate_missing_and_late_terminal_reject(self):
        base=[(0,'ATTRIB'),(101,'Embedded Object'),(40,2.),(1,'content')]
        for row in (base+[(1,'again')],base+[(3,'late')],base+[(40,2.)],base[:-1],base+[(101,'Embedded Object')]):
            with self.assertRaises(ValueError):checker.payload(row)
    def test_orientation_order_is_checked(self):
        prefix=[(0,'ATTRIB'),(101,'Embedded Object'),(1,'text')]
        a=checker.payload(prefix+[(11,1.),(21,0.),(31,0.),(50,90.)])
        b=checker.payload(prefix+[(50,90.),(11,1.),(21,0.),(31,0.)])
        self.assertEqual(a[0],b[0])
        with self.assertRaises(ValueError):checker.equal_payload(a,b)
    def test_utf8_text_transport(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'text.dxf';path.write_text('0\nATTRIB\n101\nEmbedded Object\n1\nZażółć 世界 😀\n0\nEOF\n',encoding='utf-8')
            self.assertEqual('Zażółć 世界 😀',checker.payload(checker.records(checker.load_tags(path))[0])[0][1])
    def test_exact_output_inventory(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)
            with self.assertRaises(ValueError):checker.inventory(root)
            for kind,values in [('independent',range(6)),('authored',range(1,10))]:
                for n in values:
                    for b in (False,True):
                        for s in range(3):(root/f'attribute-mtext-{kind}-{n}-{b}-{s}.dxf').touch()
            self.assertEqual(90,len(checker.inventory(root)))
            (root/'attribute-mtext-extra.dxf').touch()
            with self.assertRaises(ValueError):checker.inventory(root)
if __name__=='__main__':unittest.main()
