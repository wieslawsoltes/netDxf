# Literal characters in tolerance MTEXT stacks

The tolerance/limit formatter now escapes stack-row characters using the stack
command's character grammar instead of the surrounding MTEXT Unicode syntax.
Previously, a fraction such as `1/2` was stored inside `\S` as `1\U+002F2`.
An independent MTEXT parser reads that row as `1U+002F2`, not `1/2`. A `#` decimal
separator could instead become a fraction divider and split the wrong rows.

The shared `StackLiteral` routine emits character escapes for slash, hash,
semicolon, backslash and braces. Literal carets use an escaped caret followed by
a space so the outer caret-decoding pass does not consume the next character.
Backslashes are escaped first, so subsequently generated escapes are not doubled.
The actual tolerance divider still uses the existing `^ ` spelling. Primary and
alternate deviation/limit rows share this path. Numeric conversions, allowances,
precision, signed-zero handling, sparse native overrides, entity geometry and
all public APIs are unchanged. Literal/suppressed user text still bypasses the
measurement expression. There is no second serializer or general MTEXT rewrite.

## Qualification

The 107-case focused harness includes 11 raw/encoded lexeme probes, 24
fractional/architectural family cases, and 72 document cases. The six non-angular
families include arc-length dimensions in direct API tests. Document fixtures
use aligned dimensions in modelspace, a paper layout and a referenced block;
all six existing typed profiles and both input/output transports are covered.
Each drawing contains 15 primary/alternate deviations/limits and punctuation
separator variants. Clones, repeated regeneration, source settings, handles,
other-application XData, caller stream ownership and following geometry remain
checked. No previous test assertion, registration or checker is removed.

The required independent corpus contains 216 drawings, 3,240 dimension records,
and the 11 emitted lexemes. The checker reads the actual MTEXT packets and
feeds their contents to ezdxf's MTEXT parser. It verifies each decoded upper and
lower row, the divider, relative height and restored suffix scope. Expected
rows derive from nominal 10, allowances 1/2 and 1/4 and alternate multiplier 2,
not from captured production output. It also validates styles, overrides,
owners and graphs. Actual packet/parser mutations include the former Unicode
encoding, wrong row contents, missing divider space, altered height, removed or
duplicated content and incomplete inventories. The package consumer separately
checks fractional generation and reload against the installed NuGet package.

Execution counts, complete-suite results and source/artifact identities are
recorded in the PR. A passing same-library round trip alone is not evidence that
a consumer decodes a formatting command correctly. Independent token parsing is
still not native AutoCAD execution or visual/font/layout qualification.

```sh
DXF_TEST_FILTER=tolerance-stack-escape/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_tolerance_stack_escape.py artifacts/conformance
```

## References and remaining boundaries

- [Autodesk stacked-character semantics](https://help.autodesk.com/cloudhelp/2022/ENU/AutoCAD-LT-MAC/files/GUID-6CF9CA16-4FEF-41DF-8A6A-3C72589F5405.htm): slash, hash and caret have distinct stacking roles.
- [ezdxf MTEXT internals](https://ezdxf.readthedocs.io/en/stable/dxfinternals/entities/mtext.html): stack terminators, escaping, caret decoding and delimiter rules.

This selected grammar correction does not reconcile all alternate/angular
formatting policies or the other open tolerance drafts. Native font metrics,
fit/collision/leader/viewport behavior, historical typed dialects/pre-R11,
private FIELD/TABLE/cache regeneration, transactional rollback, dependency
imports, version conversion and AutoCAD open/AUDIT/save/reopen remain separate
work. Existing build, test, release and optional NuGet pipelines are retained;
no release publication or repository permission changes are required here.
