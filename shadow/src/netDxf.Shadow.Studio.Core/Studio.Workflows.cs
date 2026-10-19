using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using netDxf.Shadow.Workflows;

namespace netDxf.Shadow.Studio;

public sealed record CadWorkflowStartRequest(long Revision, string Name);
public sealed record CadWorkflowReplayRequest(long Revision, WorkflowScenario Scenario);
internal sealed record StudioWorkflowBaseline(string Dxf, string Layout, ImmutableArray<string> Modules, ImmutableArray<string> Fonts,
    ImmutableArray<string> Selection, string LinetypeAlignment = "Periodic", string AnnotationMode = "Drawing",
    double AnnotationPaperUnits = 1, double AnnotationDrawingUnits = 1, string PerspectiveMode = "Disabled", double WorkPlaneZ = 0);

public sealed partial class Studio
{
    private WorkflowRecorder? workflowRecorder;
    private bool workflowRecording;
    private bool workflowSuppressed;
    private string? workflowError;
    private ShadowSnapshot? workflowLastSnapshot;
    private string? workflowLastWorkspace;
    private long workflowLastView, workflowLastFonts, workflowLastModules;
    private string? workflowLastLayout;
    private ImmutableHashSet<string> workflowLastSelection = [];
    private readonly Dictionary<string, int> workflowHistoryFloors = new(StringComparer.Ordinal);

    public object CadWorkflowStart(CadWorkflowStartRequest request)
    {
        lock (gate)
        {
            Check(request.Revision); RequireLive();
            if (workflowRecording) throw new InvalidOperationException("Stop the current workflow recording first.");
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
            if (request.Name.Length > 200) throw new ArgumentException("Workflow names are limited to 200 characters.", nameof(request));
            // Saving follows the same native dimension-picture synchronization as ordinary DXF export.
            workflowSuppressed = true; byte[] bytes;
            try { bytes = Save(); } finally { workflowSuppressed = false; }
            if (bytes.Length > 8 * 1024 * 1024) throw new ArgumentException("Workflow DXF baselines are limited to 8 MiB.");
            var baseline = new StudioWorkflowBaseline(Convert.ToBase64String(bytes), layout,
                cadModules.States.Where(m => m.Enabled).Select(m => m.Descriptor.Id).Order(StringComparer.Ordinal).ToImmutableArray(),
                CurrentFonts.Fonts.Keys.Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
                CadSelected(Scene()).Select(WorkflowReference).ToImmutableArray(), PatternAlignment.ToString(), Annotation.Mode.ToString(),
                Annotation.Scale?.PaperUnits ?? 1, Annotation.Scale?.DrawingUnits ?? 1, PerspectiveView.Mode, PerspectiveView.WorkPlaneZ);
            var data = JsonSerializer.Serialize(baseline, StudioJson.CreateOptions());
            workflowRecorder = new(request.Name, new("studio-dxf-v1", data, WorkflowHash(Encoding.UTF8.GetBytes(data))));
            workflowError = null; workflowRecording = true; RememberWorkflowState();
            workflowHistoryFloors.Clear(); workflowHistoryFloors[WorkspaceId] = editor.State.UndoCount;
            return new { cad = CadState(), workflow = CadWorkflowStatus() };
        }
    }
    public object CadWorkflowStatus()
    {
        lock (gate) return new { recording = workflowRecording, name = workflowRecorder?.Name,
            steps = workflowRecorder?.Count ?? 0, error = workflowError, hasRecording = workflowRecorder is not null };
    }
    public object CadWorkflowStop(RevisionRequest request)
    {
        lock (gate) { Check(request.Revision); workflowRecording = false; return CadWorkflowStatus(); }
    }
    public WorkflowScenario CadWorkflowExport()
    {
        lock (gate) return workflowRecorder?.Snapshot() ?? throw new InvalidOperationException("No workflow has been recorded.");
    }
    public WorkflowRunReport CadWorkflowReplay(CadWorkflowReplayRequest request, CancellationToken token = default)
    {
        StudioWorkflowDriver driver;
        lock (gate)
        {
            Check(request.Revision);
            // Replay has its own drawing, history, branches and tokens. Immutable decoded font resources may be shared.
            var fonts = CurrentFonts;
            driver = new(() =>
            {
                var isolated = new Studio(false);
                foreach (var (name, font) in fonts.Fonts)
                    if (font.BigFont is not null && fonts.BigFontMaps.TryGetValue(name, out var map)) isolated.shxFonts.RegisterBigFont(name, font, map);
                    else isolated.shxFonts.Register(name, font);
                return isolated;
            });
        }
        try { return WorkflowRunner.RunAsync(request.Scenario, driver, cancellationToken: token).GetAwaiter().GetResult(); }
        finally { driver.DisposeAsync().GetAwaiter().GetResult(); }
    }
    public object CadWorkflowRoundTrip(RevisionRequest request)
    {
        lock (gate)
        {
            Check(request.Revision); RequireLive(); workflowSuppressed = true;
            try { var bytes = Save(); Load(new MemoryStream(bytes)); }
            finally { workflowSuppressed = false; }
            // Load intentionally clears undo/redo and selection, matching a real DXF reload.
            workflowHistoryFloors.Clear(); workflowHistoryFloors[WorkspaceId] = 0;
            RecordWorkflowOperation("roundtrip", new { }); return CadState();
        }
    }
    /// <summary>Called only after successful semantic operations, before publication. Recording failure never reverses an accepted edit.</summary>
    private void RecordWorkflowOperation(string operation, object arguments)
    {
        if (!workflowRecording || workflowSuppressed || workflowRecorder is null) return;
        try
        {
            if (operation == "history" && workflowHistoryFloors.TryGetValue(WorkspaceId, out var floor) && editor.State.UndoCount < floor)
                throw new InvalidOperationException("Undo crossed the recording baseline; previous source history is not part of this workflow.");
            var encoded = EncodeWorkflowArguments(JsonSerializer.SerializeToElement(arguments, WorkflowWireJson.Options));
            workflowRecorder.Record(operation, encoded, WorkflowObservation());
            RememberWorkflowState();
            if (!workflowHistoryFloors.ContainsKey(WorkspaceId)) workflowHistoryFloors[WorkspaceId] = 0;
        }
        catch (Exception error) { workflowError = "Recording stopped: " + error.GetBaseException().Message; workflowRecording = false; }
    }
    private void RememberWorkflowState()
    {
        workflowLastSnapshot = Current; workflowLastWorkspace = WorkspaceId; workflowLastView = viewVersion;
        workflowLastFonts = CurrentFonts.Revision; workflowLastModules = moduleConfigurationVersion; workflowLastLayout = layout;
        workflowLastSelection = editor.State.Selection;
    }
    private void CheckWorkflowPublication()
    {
        if (workflowRecording && !workflowSuppressed && (!ReferenceEquals(workflowLastSnapshot, Current) || workflowLastWorkspace != WorkspaceId || workflowLastView != viewVersion || workflowLastFonts != CurrentFonts.Revision
            || workflowLastModules != moduleConfigurationVersion || workflowLastLayout != layout || !workflowLastSelection.SetEquals(editor.State.Selection)))
        { workflowError = "Recording stopped: this operation is not supported by workflow replay. Earlier successful steps remain exportable."; workflowRecording = false; }
    }
    internal static string WorkflowHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
