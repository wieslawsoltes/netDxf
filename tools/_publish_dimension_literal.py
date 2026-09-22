from pathlib import Path
import subprocess
expected = {
    'netDxf/IO/DxfReader.cs': '7195d3eaae2edc0da385e618093f03175209d68c',
    'tests/netDxf.Conformance/Program.cs': '54f9f24e9bb5e4d4afafcb43d99b8e5d1ec06ebd',
}
for path, sha in expected.items():
    if subprocess.check_output(['git', 'hash-object', path], text=True).strip() != sha:
        raise RuntimeError('Source moved: ' + path)
def replace(path, old, new):
    p = Path(path); data = p.read_bytes()
    if data.count(old) != 1: raise RuntimeError('Ambiguous edit: ' + path)
    p.write_bytes(data.replace(old, new))
replace('netDxf/IO/DxfReader.cs',
    b"                        if (string.IsNullOrEmpty(userText.Trim(' ', '\\t')))\n                        {\n                            userText = string.Empty;\n                        }\n",
    b"                        // Group 1 is literal text: exactly one blank suppresses the label.\n                        // Other whitespace is user content, not the default measurement.\n")
replace('tests/netDxf.Conformance/Program.cs',
    b'        RegisterDimensionTextBlockTests();',
    b'        RegisterDimensionTextBlockTests();\n        RegisterDimensionTextLiteralTests();')
Path('tools/_publish_dimension_literal.py').unlink()
Path('.github/workflows/publish-dimension-literal.yml').unlink()
