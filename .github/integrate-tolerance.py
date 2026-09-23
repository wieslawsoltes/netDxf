from pathlib import Path
R = Path('.')
def edit(name, old, new):
    p = R / name
    b = p.read_bytes(); a = old.encode(); n = new.encode()
    if b'\r\n' in b:
        a = a.replace(b'\n', b'\r\n'); n = n.replace(b'\n', b'\r\n')
    assert b.count(a) == 1, (name, b.count(a))
    p.write_bytes(b.replace(a, n))

edit('tests/netDxf.Conformance/Program.cs', '        RegisterToleranceLabelTests();', '        RegisterToleranceFidelityTests();\n        RegisterToleranceLabelTests();\n        RegisterToleranceSparseSymmetryTests();')
edit('netDxf/Tables/DimensionToleranceSettings.cs', '''            return settings.DisplayMethod == DimensionStyleTolerancesDisplayMethod.Symmetrical
                ? settings.UpperLimit : settings.LowerLimit;''', '''            // Preserve exact bits of equal bounds (notably +0/-0); only an inactive
            // unequal lower property needs projection to the symmetric upper allowance.
            return settings.DisplayMethod == DimensionStyleTolerancesDisplayMethod.Symmetrical
                && settings.UpperLimit != settings.LowerLimit ? settings.UpperLimit : settings.LowerLimit;''')
edit('netDxf/IO/DxfWriter.cs', '            bool writeTolerance = false;', '''            bool writeToleranceMethod = false;
            bool writeToleranceUpper = false;
            bool writeToleranceLower = false;''')
edit('netDxf/IO/DxfWriter.cs', '''                    case DimensionStyleOverrideType.TolerancesDisplayMethod:
                    case DimensionStyleOverrideType.TolerancesLowerLimit:
                    case DimensionStyleOverrideType.TolerancesUpperLimit:
                        writeTolerance = true;
                        break;''', '''                    case DimensionStyleOverrideType.TolerancesDisplayMethod:
                        writeToleranceMethod = true;
                        break;
                    case DimensionStyleOverrideType.TolerancesLowerLimit:
                        writeToleranceLower = true;
                        break;
                    case DimensionStyleOverrideType.TolerancesUpperLimit:
                        writeToleranceUpper = true;
                        break;''')
edit('netDxf/IO/DxfWriter.cs', '''            if (writeTolerance)
            {
                // Store a complete effective mode/value group without changing sparse source entries.
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short) 71));
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Int16, DimensionToleranceSettings.ToleranceFlag(tolerance)));
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short) 72));
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Int16,
                    tolerance.DisplayMethod == DimensionStyleTolerancesDisplayMethod.Limits ? (short) 1 : (short) 0));
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short) 47));
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Real, tolerance.UpperLimit));
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short) 48));
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Real, DimensionToleranceSettings.Lower(tolerance)));
            }''', '''            // Preserve independent native field absence (PR #185). Only synthesize
            // a lower field when symmetric semantics cannot use the inherited lower.
            if ((writeToleranceMethod || writeToleranceUpper || writeToleranceLower)
                && tolerance.DisplayMethod == DimensionStyleTolerancesDisplayMethod.Symmetrical
                && DimensionToleranceSettings.Lower(style.Tolerances) != tolerance.UpperLimit)
                writeToleranceLower = true;
            if (writeToleranceMethod)
            {
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short) 71));
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Int16, DimensionToleranceSettings.ToleranceFlag(tolerance)));
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short) 72));
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Int16,
                    tolerance.DisplayMethod == DimensionStyleTolerancesDisplayMethod.Limits ? (short) 1 : (short) 0));
            }
            if (writeToleranceUpper)
            {
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short) 47));
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Real, tolerance.UpperLimit));
            }
            if (writeToleranceLower)
            {
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short) 48));
                xdataEntry.XDataRecord.Add(new XDataRecord(XDataCode.Real, DimensionToleranceSettings.Lower(tolerance)));
            }''')
edit('netDxf/IO/DxfReader.cs', '''                                    dimtp = (double) data.Value;
                                    hasToleranceValue = true;''', '''                                    dimtp = (double) data.Value;
                                    overrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.TolerancesUpperLimit, dimtp));
                                    hasToleranceValue = true;''')
edit('netDxf/IO/DxfReader.cs', '''                                    dimtm = (double) data.Value;
                                    hasToleranceValue = true;''', '''                                    dimtm = (double) data.Value;
                                    overrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.TolerancesLowerLimit, dimtm));
                                    hasToleranceValue = true;''')
edit('netDxf/IO/DxfReader.cs', '''                overrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.TolerancesDisplayMethod,
                    DimensionToleranceSettings.Decode(effectiveTol, effectiveLim, dimtp, dimtm)));
                overrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.TolerancesUpperLimit, dimtp));
                overrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.TolerancesLowerLimit, dimtm));''', '''                var method = DimensionToleranceSettings.Decode(effectiveTol, effectiveLim, dimtp, dimtm);
                // Missing numeric fields remain inherited. A numeric-only packet needs
                // a derived method only when it changes the inherited presentation.
                if (dimtol >= 0 || dimlim >= 0 || method != baseStyle.Tolerances.DisplayMethod)
                    overrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.TolerancesDisplayMethod, method));''')
edit('tests/netDxf.Conformance/ToleranceLabelTests.cs', '''                    SameDoubleBits(upper, (double)overrides[DimensionStyleOverrideType.TolerancesUpperLimit].Value, "Foreign effective upper");
                    SameDoubleBits(lower, (double)overrides[DimensionStyleOverrideType.TolerancesLowerLimit].Value, "Foreign effective lower");''', '''                    bool hasUpper = change is 0 or 1, hasLower = change == 2;
                    Equal(hasUpper, overrides.ContainsType(DimensionStyleOverrideType.TolerancesUpperLimit), "Sparse foreign upper presence");
                    Equal(hasLower, overrides.ContainsType(DimensionStyleOverrideType.TolerancesLowerLimit), "Sparse foreign lower presence");
                    SameDoubleBits(upper, hasUpper ? (double)overrides[DimensionStyleOverrideType.TolerancesUpperLimit].Value
                        : CompositeStyle(target).Tolerances.UpperLimit, "Foreign effective upper");
                    SameDoubleBits(lower, hasLower ? (double)overrides[DimensionStyleOverrideType.TolerancesLowerLimit].Value
                        : CompositeStyle(target).Tolerances.LowerLimit, "Foreign effective lower");''')
edit('tests/netDxf.Conformance/ToleranceLabelTests.cs', '''                    Equal(expected, (DimensionStyleTolerancesDisplayMethod)overrides[DimensionStyleOverrideType.TolerancesDisplayMethod].Value, "Foreign effective mode");
                    Equal(4, overrides.Count, "Complete group plus unrelated scalar");''', '''                    bool hasMethod = change >= 3 || expected != (DimensionStyleTolerancesDisplayMethod)m;
                    Equal(hasMethod, overrides.ContainsType(DimensionStyleOverrideType.TolerancesDisplayMethod), "Sparse foreign method presence");
                    Equal(expected, hasMethod ? (DimensionStyleTolerancesDisplayMethod)overrides[DimensionStyleOverrideType.TolerancesDisplayMethod].Value
                        : CompositeStyle(target).Tolerances.DisplayMethod, "Foreign effective mode");
                    Equal(1 + (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasMethod ? 1 : 0), overrides.Count, "Sparse values plus unrelated scalar");''')
p = R / 'doc/dxf-conformance/tolerance-dimension-labels.md'
s = p.read_text()
a = s.index('If any of the three public components is overridden,'); b = s.index('## Verification', a)
s = s[:a] + '''Native tolerance fields remain independently sparse, preserving PR #185's
inheritance contract. Mode overrides emit the flag pair; numeric overrides emit
only their selected bound. There is one necessary projection: if explicit
Symmetrical semantics need a different lower allowance than the inherited native
lower, the writer adds that lower field. An explicitly stored equal lower keeps
its exact bits, including a negative zero. Neither this projection nor saving
changes the source dictionary. Other unselected fields remain absent.

On reading, missing numeric fields remain inherited rather than being copied
into the override dictionary. Explicit flags produce the effective method using
inherited values for any absent flag or bound. Numeric-only input produces a
method override only when the exact native bound relationship changes the base
presentation; this lets an unequal pair inherited from a symmetric base render
as a deviation. An untouched presentation continues to inherit. An explicit
symmetric method with an inactive unequal lower still projects to equal native
allowances. Equal-value Deviation and Symmetrical are indistinguishable in native
flags and load as Symmetrical. These documented projections do not fabricate
geometric epsilon or alter public signatures and version gates.

PR #185 merged while this feature was in final verification. Its three new
source/test/checker files are retained unchanged, and its entire sparse-presence
matrix remains registered. The initial feature candidate materialized complete
numeric groups; the integration instead preserves the newly merged sparse
contract. The feature's 576 foreign-input cases now assert exact absence as well
as the same effective values and modes. An additional 48-case matrix verifies
minimal symmetric projection, repeat-save field inventories and signed-zero
preservation. The earlier candidate's passing reports are historical, not final
qualification of this integrated source.

''' + s[b:]
s = s.replace('The focused suite has 1,084 cases:', 'The feature suite has 1,132 cases:').replace('cases. The SurveyorUnits case', 'cases, plus 48 sparse-symmetry integration cases. The SurveyorUnits case')
p.write_text(s)
