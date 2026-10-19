using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Agents.Evaluation;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.NetDxf;

// Never run on normal verification/CI. The key is accepted only through the process
// environment; model selection is mandatory and is never replaced after a failure.
var settings = EvaluationOptions.Parse(args);
if (settings.Help)
{
    Console.WriteLine("""
        Shadow real-provider CAD evaluation (paid API requests require --live).
          --list                              List fixed evaluation cases; no API access
          --self-test                         Verify native graders; no API access
          --live --provider NAME --model MODEL  Explicit openai|anthropic provider and model
          --input-usd-per-million N           Verified conservative uncached input rate (required)
          --output-usd-per-million N          Verified conservative output rate (required)
          --budget-ledger FILE                Shared cost ledger across provider runs (required)
          --max-cost-usd N                    Ledger ceiling, >0..20, default 20
          --case create|edit|graph|all         Default: all (three isolated drawings)
          --repeats N                         1..10 fresh drawings per case, default 1
          --cache-hints on|off                 Default on; off does not disable automatic provider caches
          --output DIRECTORY                  Default: artifacts/shadow-agent-live/<unique run>
          --max-turns N                       1..40, default 12 per case
          --max-output-tokens N               256..8192, default 2048 per inference
          --timeout-seconds N                 10..900, default 240 per case
        No provider retries, model fallback, shell tools, or external files are exposed.
        """);
    return;
}
if (settings.List)
{
    foreach (var scenario in EvaluationCases.All) Console.WriteLine($"{scenario.Id}: {scenario.Description}\n{scenario.Prompt}\n");
    return;
}
if (settings.SelfTest)
{
    EvaluationCases.SelfTest();
    await BudgetVerification.RunAsync();
    Console.WriteLine("PASS: native graders, negative controls, DXF round trips, topology references, and shared budget reservation/reconciliation.");
    return;
}
if (!settings.Live || string.IsNullOrWhiteSpace(settings.Model) || settings.Provider is not ("openai" or "anthropic"))
    throw new ArgumentException("Live evaluation requires --live, explicit --provider openai|anthropic, and --model. Use --help or --list without API access.");
if (settings.InputRate is null || settings.OutputRate is null || string.IsNullOrWhiteSpace(settings.BudgetLedger))
    throw new ArgumentException("Live evaluation requires verified conservative --input-usd-per-million and --output-usd-per-million rates plus --budget-ledger. Unknown prices are not assumed.");
var keyVariable = settings.Provider == "openai" ? "OPENAI_API_KEY" : "ANTHROPIC_API_KEY";
var apiKey = Environment.GetEnvironmentVariable(keyVariable);
if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException($"Set {keyVariable} in the process environment; do not pass credentials on the command line.");
string Redact(string value) => value.Replace(apiKey, "[redacted]", StringComparison.Ordinal);
var output = Path.GetFullPath(settings.Output ?? Path.Combine("artifacts", "shadow-agent-live", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]));
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new ArgumentException("Use a new or empty output directory to preserve previous evaluation receipts.");
Directory.CreateDirectory(output);
var jsonOptions = new JsonSerializerOptions(McpJson.Options) { WriteIndented = true };
var scenarios = EvaluationCases.All.Where(scenario => settings.Case == "all" || scenario.Id == settings.Case).ToArray();
var fixtures = scenarios.ToDictionary(scenario => scenario.Id, EvaluationFixture.Freeze, StringComparer.Ordinal);
var requested = scenarios.SelectMany(scenario => Enumerable.Range(1, settings.Repeats).Select(repeat => (Scenario: scenario, Repeat: repeat))).ToArray();
var reports = new List<object>();
var overallPassed = true;
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
Console.WriteLine($"Model: {settings.Model}; cases: {scenarios.Length} × {settings.Repeats} repeats; maximum {settings.MaxTurns} inferences/case, {settings.MaxOutputTokens} output tokens/inference; output: {output}");
foreach (var item in requested)
{
    var scenario = item.Scenario;
    var directory = Path.Combine(output, settings.Repeats == 1 ? scenario.Id : scenario.Id + "-" + item.Repeat.ToString("D2")); Directory.CreateDirectory(directory);
    var fixture = fixtures[scenario.Id];
    var budgetOptions = new EvaluationBudgetOptions
    {
        Provider = settings.Provider, Model = settings.Model, BudgetLedger = settings.BudgetLedger!, InputRate = settings.InputRate,
        OutputRate = settings.OutputRate, MaxCostUsd = settings.MaxCostUsd, MaxOutputTokens = settings.MaxOutputTokens,
        // The OpenAI input count includes cached input. Preserve the full Anthropic reservation until the caller
        // supplies an adapter with a verified cache-accounting convention and rates covering cache writes.
        CacheTokensAdditionalToInput = settings.Provider == "openai" ? false : null,
        RateSource = "Explicit caller-supplied conservative rates; no live pricing lookup",
    };
    var target = new AgentSessionEvaluationTarget(new(settings.Provider + ":" + settings.Model, "agent-session", settings.Provider, settings.Model,
        settings.CacheHints ? "harness-hints-on" : "harness-hints-off", new Dictionary<string, string>
        { ["maxTurns"] = settings.MaxTurns.ToString(), ["maxOutputTokens"] = settings.MaxOutputTokens.ToString(), ["maxRetries"] = "0", ["timeoutSeconds"] = settings.TimeoutSeconds.ToString() }),
        () => new BudgetChatClient(ShadowAgentProviders.Create(new() { Provider = settings.Provider, Model = settings.Model, ApiKey = apiKey, OptimizePromptCaching = settings.CacheHints }), budgetOptions),
        new() { Model = settings.Model, PermissionMode = AgentPermissionMode.FullAccess,
            MaxTurns = settings.MaxTurns, MaxOutputTokens = settings.MaxOutputTokens, MaxRetries = 0,
            MaxToolCallsPerTurn = 32, ContextWindowTokens = 128000, CompactionThreshold = .9,
            InferenceTimeout = TimeSpan.FromSeconds(Math.Min(180, settings.TimeoutSeconds)), ToolTimeout = TimeSpan.FromSeconds(30) }, Redact);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
    timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
    var report = await new EvaluationRunner().RunAsync(fixture, target, repeat: item.Repeat, cancellationToken: timeout.Token);
    var status = report.Status; overallPassed &= report.Passed; reports.Add(report);
    File.WriteAllBytes(Path.Combine(directory, "seed.dxf"), fixture.ExportSeed());
    if (report.DrawingDxf is not null) File.WriteAllBytes(Path.Combine(directory, "drawing.dxf"), report.DrawingDxf);
    File.WriteAllText(Path.Combine(directory, "report.json"), Redact(JsonSerializer.Serialize(report, jsonOptions)));
    File.WriteAllText(Path.Combine(directory, "events.json"), Redact(JsonSerializer.Serialize(report.Events, jsonOptions)));
    if (report.Checkpoint is not null) File.WriteAllText(Path.Combine(directory, "checkpoint.json"), Redact(report.Checkpoint));
    Console.WriteLine($"[{scenario.Id}] {(report.Passed ? "PASS" : "FAIL")}: {report.ElapsedMilliseconds / 1000:F1}s, {report.UndoTransactions} transactions, {report.Telemetry.InputTokens?.ToString() ?? "unknown"} input / {report.Telemetry.OutputTokens?.ToString() ?? "unknown"} output tokens");
    foreach (var check in report.Checks.Where(check => !check.Passed)) Console.WriteLine("  FAIL: " + check.Name);
    // An API/configuration failure must not launch more billed requests for other cases.
    if (status == "error" || stop.IsCancellationRequested) break;
}
File.WriteAllText(Path.Combine(output, "summary.json"), Redact(JsonSerializer.Serialize(new
{
    schemaVersion = 1, completedAt = DateTimeOffset.UtcNow, provider = settings.Provider, model = settings.Model,
    passed = overallPassed && reports.Count == requested.Length, requestedCases = scenarios.Select(scenario => scenario.Id), requestedRuns = requested.Length, repeats = settings.Repeats,
    budgets = new { settings.MaxTurns, settings.MaxOutputTokens, settings.TimeoutSeconds, settings.MaxCostUsd, settings.InputRate, settings.OutputRate, ledger = Path.GetFullPath(settings.BudgetLedger!) }, results = reports,
}, jsonOptions)));
Environment.ExitCode = overallPassed && reports.Count == requested.Length ? 0 : 1;

internal sealed record EvaluationOptions
{
    public bool Help { get; init; }
    public bool List { get; init; }
    public bool SelfTest { get; init; }
    public bool Live { get; init; }
    public string Model { get; init; } = "";
    public string Provider { get; init; } = "";
    public string? BudgetLedger { get; init; }
    public decimal? InputRate { get; init; }
    public decimal? OutputRate { get; init; }
    public decimal MaxCostUsd { get; init; } = 20;
    public string Case { get; init; } = "all";
    public string? Output { get; init; }
    public int Repeats { get; init; } = 1;
    public bool CacheHints { get; init; } = true;
    public int MaxTurns { get; init; } = 12;
    public int MaxOutputTokens { get; init; } = 2048;
    public int TimeoutSeconds { get; init; } = 240;
    public static EvaluationOptions Parse(string[] args)
    {
        var value = new EvaluationOptions { Help = args.Length == 0 };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i];
            if (!seen.Add(option)) throw new ArgumentException("Duplicate option: " + option);
            string Next() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing value for " + option);
            int Number(int minimum, int maximum)
            {
                if (!int.TryParse(Next(), out var number) || number < minimum || number > maximum) throw new ArgumentException($"{option} requires {minimum}..{maximum}.");
                return number;
            }
            decimal Price(decimal maximum)
            {
                if (!decimal.TryParse(Next(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0 || amount > maximum)
                    throw new ArgumentException($"{option} requires a positive amount no greater than {maximum}.");
                return amount;
            }
            value = option switch
            {
                "--help" => value with { Help = true }, "--list" => value with { List = true },
                "--self-test" => value with { SelfTest = true }, "--live" => value with { Live = true },
                "--model" => value with { Model = Next() }, "--case" => value with { Case = Next() },
                "--repeats" => value with { Repeats = Number(1, 10) },
                "--cache-hints" => value with { CacheHints = Next() switch { "on" => true, "off" => false, _ => throw new ArgumentException("Use --cache-hints on|off.") } },
                "--provider" => value with { Provider = Next() }, "--budget-ledger" => value with { BudgetLedger = Next() },
                "--input-usd-per-million" => value with { InputRate = Price(1000) }, "--output-usd-per-million" => value with { OutputRate = Price(1000) },
                "--max-cost-usd" => value with { MaxCostUsd = Price(20) },
                "--output" => value with { Output = Next() }, "--max-turns" => value with { MaxTurns = Number(1, 40) },
                "--max-output-tokens" => value with { MaxOutputTokens = Number(256, 8192) },
                "--timeout-seconds" => value with { TimeoutSeconds = Number(10, 900) },
                _ => throw new ArgumentException("Unknown option: " + option),
            };
        }
        if (value.Case is not ("all" or "create" or "edit" or "graph")) throw new ArgumentException("Unknown evaluation case.");
        if (new[] { value.Help, value.List, value.SelfTest, value.Live }.Count(flag => flag) > 1) throw new ArgumentException("Choose only one of --help, --list, --self-test, or --live.");
        return value;
    }
}
