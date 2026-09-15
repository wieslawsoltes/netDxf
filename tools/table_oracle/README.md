# Pinned TABLE reader qualification

This harness reads five unchanged upstream sample files with ACadSharp. It compares
the resulting flat `ACAD_TABLE` grids and literal values against an independent
ezdxf/raw-tag view of the same bytes. The fixture inventory and SHA-256 digests are
in [fixtures.json](fixtures.json).

The qualification is limited to the flat entity representation. The pinned
ACadSharp reader constructs a detached `TableEntity.Content` with handle zero and
does not retain the actual XRECORD-owned `TABLECONTENT` records. Its handling of
`TABLEGEOMETRY` and `CELLSTYLEMAP` is incomplete. Successful reads therefore do not
qualify those schemas, their association, native display regeneration, formulas,
dates, block cells, or arbitrary cell styles.

## Reproduce the reader build

Use the following exact revisions without changing their source:

- [ACadSharp f6a7f1e7e502c6fe9d1e840e576b6124ec0ded11](https://github.com/DomCR/ACadSharp/tree/f6a7f1e7e502c6fe9d1e840e576b6124ec0ded11)
- Its [CSUtilities submodule b1f53ee2c68143173100d031adcdb275f524aea9](https://github.com/DomCR/CSUtilities/tree/b1f53ee2c68143173100d031adcdb275f524aea9)

The proof run used .NET SDK 8.0.408. That SDK needs the `LangVersion=preview`
override for this source's parameter-collection declarations. The build changes
only compiler options; it does not patch the independent reader.

```sh
dotnet msbuild ACadSharp/src/ACadSharp/ACadSharp.csproj /restore /t:Build \
  /p:TargetFrameworks=net8.0 /p:Configuration=Debug /p:LangVersion=preview \
  /p:GeneratePackageOnBuild=false /m:1 /nr:false /p:UseSharedCompilation=false
dotnet build tools/table_oracle/Oracle.csproj \
  -p:ACadSharpAssembly=/absolute/path/ACadSharp/src/ACadSharp/bin/Debug/net8.0/ACadSharp.dll
```

Download the five source URLs listed in `fixtures.json`, preserving their original
bytes. Invoke `Oracle.dll` with all five paths. It writes `snapshots.json` in the
current directory, including notifications, exact source hashes, typed cells,
and the failed registered-`TABLECONTENT` lookups. Reads use `Failsafe=false` and
do not create default database objects.

```sh
dotnet tools/table_oracle/bin/Debug/net8.0/Oracle.dll \
  sources/acad_table_simple.dxf sources/acad_table_with_blk_ref.dxf \
  sources/sample_AC1018_ascii.dxf sources/sample_AC1021_ascii.dxf \
  sources/sample_AC1024_ascii.dxf
python tools/table_oracle/verify_sources.py snapshots.json sources
```

`verify_sources.py` requires ezdxf 1.4.4, used for the recorded proof. It verifies
source hashes, eight typed grids, row/column sizes, display-block and style
references, 100 string/numeric comparisons including legacy empty strings, value
flags and formatted strings, all eight native roundtrip ownership envelopes, and
zero ezdxf audit changes. Point/date/handle variants are deliberately excluded
from the literal-value assertion. The executable results and limitations are
summarized in [qualification.json](qualification.json).

The R2004 samples have additional `ACAD_ROUNDTRIP_PRE2007_TABLE` and
`ACAD_ROUNDTRIP_PRE2007_TABLECELL` sections with a third owned `DATATABLE` object.
The exact packets are recorded in `fixtures.json`; they must remain unbound until
that composite schema is implemented.
