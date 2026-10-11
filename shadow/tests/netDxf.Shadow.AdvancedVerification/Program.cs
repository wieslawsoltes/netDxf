using System.Diagnostics;

var tests = new List<(string Name, Action Run)>();
StructuralVerification.Register(tests);
IncrementalVerification.Register(tests);
AppearanceVerification.Register(tests);
RichTextVerification.Register(tests);
GalleryVerification.Register(tests);
ViewportVerification.Register(tests);
ShxVerification.Register(tests);
ResourceRevisionVerification.Register(tests);
var failures = 0;
foreach (var test in tests)
{
    var timer = Stopwatch.StartNew();
    try { test.Run(); Console.WriteLine($"PASS {test.Name} ({timer.Elapsed.TotalMilliseconds:F1} ms)"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {test.Name}\n{error}"); }
}
Console.WriteLine($"Advanced verification: {tests.Count - failures}/{tests.Count} passed.");
return failures == 0 ? 0 : 1;
