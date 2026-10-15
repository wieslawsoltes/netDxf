using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

var tests = new List<(string Name, Action Run)>();
MathVerification.Register(tests);
DraftingVerification.Register(tests);
StructuralVerification.Register(tests);
MergeVerification.Register(tests);
StudioWorkspaceVerification.Register(tests);
SourceSaveVerification.Register(tests);
CustomHeaderVerification.Register(tests);
ModelAnnotationVerification.Register(tests);
SourceConsistencyVerification.Register(tests);
SourceBoundaryVerification.Register(tests);
SourcePreparationVerification.Register(tests);
IncrementalVerification.Register(tests);
AppearanceVerification.Register(tests);
LinetypeSourceVerification.Register(tests);
LinetypeFittingVerification.Register(tests);
LinetypeContinuityVerification.Register(tests);
LinetypeScaleVerification.Register(tests);
LinetypeComponentScaleVerification.Register(tests);
LinetypePlannerVerification.Register(tests);
LinetypeFrameVerification.Register(tests);
LinetypeRenderingVerification.Register(tests);
LinetypeStudioVerification.Register(tests);
LinetypeSnappingVerification.Register(tests);
HatchIslandVerification.Register(tests);
RichTextVerification.Register(tests);
MTextColorVerification.Register(tests);
ParagraphControlVerification.Register(tests);
ParagraphLayoutVerification.Register(tests);
ParagraphIntegrationVerification.Register(tests);
GalleryVerification.Register(tests);
ViewportVerification.Register(tests);
ShxVerification.Register(tests);
VerticalShxVerification.Register(tests);
BigFontVerification.Register(tests);
BigFontMapFileVerification.Register(tests);
ResourceRevisionVerification.Register(tests);
AffineVerification.Register(tests);
DimensionVerification.Register(tests);
StudioTransformVerification.Register(tests);
LinetypeTransportVerification.Register(tests);
var results = new List<VerificationResult>(tests.Count);
var failures = 0;
foreach (var test in tests)
{
    var timer = Stopwatch.StartNew();
    string? failure = null;
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name} ({timer.Elapsed.TotalMilliseconds:F1} ms)");
    }
    catch (Exception error)
    {
        failures++;
        failure = error.ToString();
        Console.Error.WriteLine($"FAIL {test.Name}\n{failure}");
    }
    results.Add(new(test.Name, failure is null, timer.Elapsed.TotalMilliseconds, failure));
}
Console.WriteLine($"Advanced verification: {tests.Count - failures}/{tests.Count} passed.");
Directory.CreateDirectory("artifacts");
File.WriteAllText("artifacts/advanced-verification.json", JsonSerializer.Serialize(new
{
    schemaVersion = 1,
    source = Environment.GetEnvironmentVariable("GITHUB_SHA"),
    head = Environment.GetEnvironmentVariable("GITHUB_HEAD_REF"),
    runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),
    runAttempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"),
    framework = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    total = tests.Count,
    passed = tests.Count - failures,
    failed = failures,
    results
}, new JsonSerializerOptions { WriteIndented = true }));
return failures == 0 ? 0 : 1;

internal sealed record VerificationResult(string Name, bool Passed, double ElapsedMilliseconds, string? Error);
