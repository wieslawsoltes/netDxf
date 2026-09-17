"""Tests of the independent reference checker, NOT executions of modified C#."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools'))
import verify_standard_field_evaluator as r
from verify_stored_fields import source_records

class DateReferenceTests(unittest.TestCase):
    def test_numeric_date(self):self.assertEqual('2026-09-17 13:05:09',r.expected_date(r.CLOCK_TICKS,r.DATE_MASKS[0]))
    def test_gregorian_boundaries(self):
        self.assertEqual('0001-01-01 00:00:00',r.expected_date(0,r.DATE_MASKS[0]))
        self.assertEqual('9999-12-31 23:59:59',r.expected_date(r.DATE_INPUTS[-1],r.DATE_MASKS[0]))
    def test_names(self):
        self.assertEqual('17 17 Thu Thursday',r.expected_date(r.CLOCK_TICKS,r.DATE_MASKS[1]))
        self.assertEqual('9 09 Sep September',r.expected_date(r.CLOCK_TICKS,r.DATE_MASKS[2]))
    def test_midnight_and_noon(self):
        self.assertEqual('12 12 0 00 0 00 0 00 A AM',r.expected_date(0,r.DATE_MASKS[4]))
        self.assertEqual('12 12 12 12 30 30 59 59 P PM',r.expected_date(r.DATE_INPUTS[2],r.DATE_MASKS[4]))
    def test_literals(self):
        self.assertEqual("'d'",r.expected_date(0,r.DATE_MASKS[6]))
        self.assertEqual('clock 13:05:09',r.expected_date(r.CLOCK_TICKS,r.DATE_MASKS[5]))
    def test_unknown_mask(self):
        with self.assertRaises(ValueError):r.expected_date(0,'PrivateMask')
    def test_bad_ticks(self):
        for v in (-1,3155378976000000000,1.0):
            with self.assertRaises(ValueError):r.clock_from_ticks(v)

class AngleReferenceTests(unittest.TestCase):
    def test_units(self):
        expected=('90.00','90°0\'0"','100.00','1.57','N 0°0\'0" E')
        for mode in range(5):self.assertEqual(expected[mode],r.expected_angle(r.bits(r.math.pi/2),mode,4 if mode in (1,4) else 2))
    def test_exact_rounding(self):
        self.assertEqual('0.12',r.expected_angle(r.bits(.125),3,2))
        self.assertEqual('-0.12',r.expected_angle(r.bits(-.125),3,2))
        self.assertEqual('0.00',r.expected_angle(r.bits(-0.0),3,2))
    def test_negative_bearing(self):self.assertEqual('S 45° E',r.expected_angle(r.bits(-r.math.pi/4),4,0))
    def test_large_turn(self):self.assertEqual('N 90° E',r.expected_angle(r.bits(-2*r.math.pi),4,0))
    def test_nonfinite(self):
        with self.assertRaises(ValueError):r.expected_angle(r.bits(float('inf')),3,2)

class InventoryTests(unittest.TestCase):
    @staticmethod
    def model_matrix():
        rows=[]
        for kind,value,expression in sorted(r.format_inventory()):
            if kind=='date':rows.append(dict(kind=kind,ticks=value,expression=expression,result=r.expected_date(int(value),expression)))
            else:rows.append(dict(kind=kind,bits=value,expression=expression,result=r.expected_angle(value,int(expression[3]),int(expression[7]))))
        return rows
    def test_exact_matrix(self):
        self.assertEqual(408,len(r.format_inventory()));self.assertEqual(408,r.check_format_matrix(self.model_matrix()))
    def test_duplicate_missing(self):
        rows=self.model_matrix()
        for bad in (rows[:-1],rows+[rows[0]],[rows[0]]+rows[:-1]):
            with self.assertRaises(ValueError):r.check_format_matrix(bad)
    def test_unknown_input(self):
        with self.assertRaises(ValueError):r.check_format_row(dict(self.model_matrix()[0],expression='private'))
    def test_output_inventory(self):
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)
            with self.assertRaises(ValueError):r.check_inventory(path)
            for name in r.drawing_inventory():(path/name).touch()
            (path/'standard-field-formats.json').write_text('[]');r.check_inventory(path)
            (path/'standard-fields-extra.dxf').touch()
            with self.assertRaises(ValueError):r.check_inventory(path)
    def test_modeled_record_corruptions(self):
        manifest=json.loads((r.ROOT/'tests/fixtures/field-oracle/manifest.json').read_text());count=0
        for item in manifest['files']:
            native=source_records(item)
            for mode in r.MODES:
                before=r.synthetic_source(native,mode);after=r.expected_records(before,item['profile'],mode,native)
                count+=r.check_pair(before,after,item['profile'],mode,native)
                bad=copy.deepcopy(after);bad['14E'][-1][1]+=1
                with self.assertRaises(ValueError):r.check_pair(before,bad,item['profile'],mode,native)
        self.assertEqual(911,count) # Pinned four-FIELD models; not C# output.

if __name__=='__main__':unittest.main()
