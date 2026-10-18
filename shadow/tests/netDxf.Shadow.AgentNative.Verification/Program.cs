using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;

internal static class Program
{
    private static int checks;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly List<int> ChildProcesses = [];

    public static async Task<int> Main(string[] args)
    {
        // Run the same managed executable as a portable command fixture. No shell, external
        // command or platform-specific quoting assumptions are needed by these scenarios.
        if (args is ["--child", ..]) return await RunChildAsync(args[1..]);
        var root = Path.Combine(Path.GetTempPath(), "netdxf-agent-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            VerifyFiles(Path.Combine(root, "files"));
            VerifyImport(Path.Combine(root, "import"));
            VerifyBinaryFiles(Path.Combine(root, "binary"));
            VerifyPaths(Path.Combine(root, "paths"), Path.Combine(root, "outside"));
            await VerifyCommandsAsync(Path.Combine(root, "commands"));
            Console.WriteLine($"Native agent verification passed: {checks} checks covering actual disk CAS, external edits, binary files, transactional import, path grants, precise process arguments, output draining and process-tree cancellation.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            foreach (var pid in ChildProcesses)
            {
                try { using var process = Process.GetProcessById(pid); if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private static void VerifyFiles(string root)
    {
        Directory.CreateDirectory(root);
        var workspace = new DirectoryAgentWorkspace(root, maximumBytes: 4096, maximumFiles: 8);
        var first = workspace.Write("src/sample.cs", "first line\nneedle café\nlast line", expectedVersion: 0);
        Check(File.ReadAllText(Path.Combine(root, "src", "sample.cs"), Utf8) == first.Content && first.Version > 0,
            "new text files are written to the granted disk directory with a CAS version");
        Check(!File.ReadAllBytes(Path.Combine(root, "src", "sample.cs")).Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }),
            "native text writes are UTF-8 without adding a BOM");
        Check(workspace.Read(first.Path).Version == first.Version && workspace.List().Single().Version == first.Version,
            "unchanged files retain the same version across metadata and content reads");
        var page = workspace.Read(first.Path, 6, 4);
        Check(page.Content == "line" && page.Offset == 6 && page.HasMore && page.TotalCharacters == first.Content.Length,
            "native reads return bounded character pages");
        Check(workspace.Read(first.Path, first.Content.Length, 10).Content.Length == 0,
            "an end-of-file page is a valid empty result");
        Expect<ArgumentOutOfRangeException>(() => workspace.Read(first.Path, -1), "negative read offsets fail");
        Expect<ArgumentOutOfRangeException>(() => workspace.Read(first.Path, 0, 0), "zero read length fails");
        Expect<InvalidOperationException>(() => workspace.Write(first.Path, "overwrite", 0), "create-only writes cannot overwrite an existing file");
        var revised = workspace.Write(first.Path, "replacement\nneedle café", first.Version);
        Check(revised.Version > first.Version && workspace.Read(first.Path).Content == revised.Content,
            "replacement writes advance the version and replace the complete file");
        Expect<InvalidOperationException>(() => workspace.Write(first.Path, "stale", first.Version), "stale write CAS fails");
        Check(workspace.Read(first.Path).Content == revised.Content, "failed write CAS preserves file contents");

        var file = Path.Combine(root, "src", "sample.cs");
        var timestamp = File.GetLastWriteTimeUtc(file);
        File.WriteAllText(file, "external\nneedle café", Utf8);
        File.SetLastWriteTimeUtc(file, timestamp);
        Expect<InvalidOperationException>(() => workspace.Write(first.Path, "stale external", revised.Version),
            "hash-based CAS detects external edits even when their file timestamp is preserved");
        var external = workspace.Read(first.Path);
        Check(external.Content.StartsWith("external", StringComparison.Ordinal) && external.Version > revised.Version,
            "external edits are visible and receive a new version");
        var match = workspace.Search("NEEDLE", "src/", limit: 1).Single();
        Check(match.Path == first.Path && match.Line == 2 && match.Text == "needle café", "native search preserves paths and line numbers with a bounded result count");
        Expect<InvalidOperationException>(() => workspace.Delete(first.Path, revised.Version), "delete refuses an externally changed version");
        workspace.Delete(first.Path, external.Version);
        Check(!File.Exists(file) && workspace.List().Count == 0, "current-version delete removes the actual file");
        Expect<FileNotFoundException>(() => workspace.Read(first.Path), "deleted files cannot be read");
        var recreated = workspace.Write(first.Path, "new generation", 0);
        Check(recreated.Version > external.Version, "delete and recreate does not resurrect an old CAS version");
        Expect<InvalidOperationException>(() => workspace.Write("oversized.txt", new string('x', 4097), 0), "per-file byte budget fails before creation");
        Check(!File.Exists(Path.Combine(root, "oversized.txt")), "oversized writes leave no file behind");
        Expect<ArgumentException>(() => workspace.Write(first.Path, "invalid\0text", recreated.Version), "NUL-containing text is rejected before mutation");
        Check(workspace.Read(first.Path).Content == recreated.Content && workspace.Read(first.Path).Version == recreated.Version,
            "invalid text leaves content and CAS version unchanged");
        Check(!Directory.EnumerateFiles(root, ".shadow-agent-*", SearchOption.AllDirectories).Any(), "successful and rejected writes leave no staging files");
    }

    private static void VerifyImport(string root)
    {
        Directory.CreateDirectory(root);
        var workspace = new DirectoryAgentWorkspace(root, maximumBytes: 1024, maximumFiles: 8);
        var before = workspace.Write("a.txt", new string('a', 400), 0);
        workspace.Write("unrelated.txt", new string('u', 500), 0);
        var date = DateTimeOffset.UtcNow;
        // Incoming text alone fits. The second write fails against unrelated existing bytes,
        // after the first replacement was applied, exercising actual rollback rather than validation.
        Expect<InvalidOperationException>(() => workspace.Import([
            new("a.txt", new string('b', 300), 1, date),
            new("new.txt", new string('n', 400), 2, date)
        ]), "import rolls back an earlier write when the aggregate disk budget fails later");
        Check(workspace.Read("a.txt").Content == before.Content && workspace.Read("unrelated.txt").Content == new string('u', 500)
            && !File.Exists(Path.Combine(root, "new.txt")), "failed import restores originals and preserves unrelated files");
        var originalContents = workspace.Export().ToDictionary(f => f.Path, f => f.Content);
        Expect<ArgumentException>(() => workspace.Import([
            new("a.txt", "would change", 1, date), new("a.txt", "duplicate", 2, date)
        ]), "duplicate import paths are rejected before writing any file");
        Check(workspace.Export().All(f => originalContents[f.Path] == f.Content), "duplicate import leaves the disk snapshot unchanged");
        Expect<ArgumentException>(() => workspace.Import([
            new("a.txt", "would change", 1, date), new("../escape.txt", "outside", 2, date)
        ]), "every import path is validated before the first mutation");
        Check(workspace.Read("a.txt").Content == before.Content, "invalid import paths leave the earlier valid file untouched");
        Expect<ArgumentException>(() => workspace.Import([
            new("a.txt", "would change", 1, date), new("bad.txt", "bad\0text", 2, date)
        ]), "binary text in imports is rejected without losing rollback coverage");
        Check(workspace.Read("a.txt").Content == before.Content && !File.Exists(Path.Combine(root, "bad.txt")),
            "invalid text imports leave no partial content or binary file");
        var priorVersion = workspace.Read("a.txt").Version;
        workspace.Import([new("a.txt", "small replacement", 1, date), new("new.txt", "new file", 2, date)]);
        Check(workspace.Read("a.txt").Content == "small replacement" && workspace.Read("new.txt").Content == "new file"
            && workspace.Read("unrelated.txt").Content.Length == 500, "successful imports replace supplied paths while preserving unrelated files");
        Check(workspace.Read("a.txt").Version > priorVersion, "imports generate fresh local CAS versions rather than trusting serialized versions");
        Expect<InvalidOperationException>(() => workspace.Write("a.txt", "stale", priorVersion), "pre-import versions cannot overwrite imported content");

        var constrainedRoot = Path.Combine(root, "counted"); Directory.CreateDirectory(constrainedRoot);
        var constrained = new DirectoryAgentWorkspace(constrainedRoot, maximumBytes: 1024, maximumFiles: 1);
        constrained.Write("only.txt", "only", 0);
        Expect<InvalidOperationException>(() => constrained.Write("second.txt", "second", 0), "file count capacity prevents creating additional files");
        Check(constrained.List().Count == 1 && !File.Exists(Path.Combine(constrainedRoot, "second.txt")), "file count rejection is atomic");
    }

    private static void VerifyBinaryFiles(string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "drawing.dxf"), [0, 0xff, 0x80, 0x31]);
        File.WriteAllBytes(Path.Combine(root, "invalid-utf8.bin"), [0xc3, 0x28]);
        File.WriteAllText(Path.Combine(root, "notes.txt"), "searchable needle", Utf8);
        var workspace = new DirectoryAgentWorkspace(root, maximumBytes: 4096, maximumFiles: 8);
        var files = workspace.List();
        var binary = files.Single(f => f.Path == "drawing.dxf");
        Check(binary.IsBinary && binary.Bytes == 4 && binary.Characters == 0 && binary.Version > 0,
            "binary files appear as bounded metadata with byte counts and CAS versions");
        Check(files.Single(f => f.Path == "invalid-utf8.bin").IsBinary && !files.Single(f => f.Path == "notes.txt").IsBinary,
            "invalid UTF-8 and ordinary UTF-8 are classified independently");
        Expect<InvalidOperationException>(() => workspace.Read("drawing.dxf"), "text reads reject binary content explicitly");
        Check(workspace.Search("needle").Single().Path == "notes.txt", "search skips binary files and still searches text");
        Check(workspace.Export().Single().Path == "notes.txt" && File.ReadAllBytes(Path.Combine(root, "drawing.dxf")).Length == 4,
            "text checkpoint exports omit binary content and preserve the actual binary file");
        File.WriteAllBytes(Path.Combine(root, "drawing.dxf"), [0, 0xff, 0x80, 0x32]);
        Expect<InvalidOperationException>(() => workspace.Delete(binary.Path, binary.Version), "binary deletes detect external changes by version");
        binary = workspace.List().Single(f => f.Path == binary.Path);
        workspace.Delete(binary.Path, binary.Version);
        Check(!File.Exists(Path.Combine(root, binary.Path)), "explicit current-version deletion supports binary files");
        var invalid = workspace.List().Single(f => f.Path == "invalid-utf8.bin");
        var text = workspace.Write(invalid.Path, "explicit replacement", invalid.Version);
        Check(text.Content == "explicit replacement" && !workspace.List().Single(f => f.Path == invalid.Path).IsBinary,
            "explicit CAS writes can intentionally replace a binary file with text");
    }

    private static void VerifyPaths(string root, string outside)
    {
        Directory.CreateDirectory(root); Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "external.txt"), "outside grant", Utf8);
        var workspace = new DirectoryAgentWorkspace(root, maximumBytes: 4096, maximumFiles: 2);
        foreach (var path in new[] { "../escape", "/absolute", "a/../../escape", "a//b", "./a", "a/./b", "a\\b", "C:/escape", "a\nfile" })
            Expect<ArgumentException>(() => workspace.Write(path, "escape", 0), $"invalid path '{path.Replace("\n", "\\n", StringComparison.Ordinal)}' is refused");
        foreach (var name in new[] { ".git", "bin", "obj", "node_modules" })
        {
            var nested = Path.Combine(root, name, "deep"); Directory.CreateDirectory(nested);
            for (var index = 0; index < 5; index++) File.WriteAllText(Path.Combine(nested, index + ".txt"), new string('x', 2000), Utf8);
        }
        workspace.Write("visible.txt", "visible", 0);
        Check(workspace.List().Select(f => f.Path).SequenceEqual(["visible.txt"]), "enumeration prunes build and dependency trees before file counting or reading");
        Check(workspace.Export().Count == 1 && workspace.Search("visible").Count == 1, "excluded directory trees do not exhaust export or search budgets");

        var linkedDirectory = Path.Combine(root, "linked");
        try { Directory.CreateSymbolicLink(linkedDirectory, outside); }
        catch (Exception error) when (error is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            Console.WriteLine("Symlink scenarios skipped because this host cannot create a test link: " + error.GetType().Name);
            return;
        }
        Expect<ArgumentException>(() => workspace.Read("linked/external.txt"), "read paths cannot traverse a symlink out of the grant");
        Expect<ArgumentException>(() => workspace.Write("linked/new.txt", "outside", 0), "new writes cannot traverse a symlink out of the grant");
        Expect<ArgumentException>(() => new DirectoryAgentWorkspace(linkedDirectory), "a linked directory cannot become a workspace root");
        Expect<ArgumentException>(() => new LocalAgentCommandRunner(linkedDirectory), "a linked directory cannot become a native command root");
        Check(File.ReadAllText(Path.Combine(outside, "external.txt"), Utf8) == "outside grant" && !File.Exists(Path.Combine(outside, "new.txt")),
            "refused link operations leave files outside the grant untouched");
        File.CreateSymbolicLink(Path.Combine(root, "linked-file.txt"), Path.Combine(outside, "external.txt"));
        Expect<ArgumentException>(() => workspace.Read("linked-file.txt"), "direct file symlinks are refused");
        Check(workspace.List().Count == 1, "linked files and directory trees are skipped during enumeration");
    }

    private static async Task VerifyCommandsAsync(string root)
    {
        Directory.CreateDirectory(root);
        var nested = Path.Combine(root, "nested"); Directory.CreateDirectory(nested);
        var runner = new LocalAgentCommandRunner(root, maximumOutputCharacters: 1024);
        var arguments = new[] { "", "with spaces", "double\"quote", "single'quote", "trailing\\", "$HOME $(touch injected)", "`touch injected`", "; & | > <", "line\nbreak", "café 日本語" };
        var precise = await runner.ExecuteAsync(SelfCommand(["argv", .. arguments], "nested"));
        using (var result = JsonDocument.Parse(precise.StandardOutput))
        {
            Check(result.RootElement.GetProperty("arguments").EnumerateArray().Select(x => x.GetString()).SequenceEqual(arguments),
                "native argument arrays preserve whitespace, empty values, quotes, newlines, Unicode and shell metacharacters exactly");
            Check(Path.GetFullPath(result.RootElement.GetProperty("directory").GetString()!) == Path.GetFullPath(nested),
                "commands run in the explicitly selected workspace subdirectory");
        }
        Check(precise.ExitCode == 0 && !precise.Truncated && !precise.TimedOut && precise.ElapsedMilliseconds >= 0,
            "successful command receipts report exit, duration and bounded-output status");
        Check(!File.Exists(Path.Combine(nested, "injected")), "metacharacter arguments are passed literally without a shell");
        var stdin = await runner.ExecuteAsync(SelfCommand(["stdin"]));
        Check(stdin.StandardOutput == "eof:0" && stdin.ExitCode == 0, "child standard input is closed so ordinary commands cannot wait for interactive input");
        var noisy = await runner.ExecuteAsync(SelfCommand(["output"]));
        Check(noisy.ExitCode == 23 && !noisy.TimedOut, "large concurrent stdout and stderr streams drain completely without deadlock");
        Check(noisy.Truncated && noisy.StandardOutput == new string('o', 1024) && noisy.StandardError == new string('e', 1024),
            "stdout and stderr retain the exact bounded prefix and mark truncation");
        Check(noisy.ElapsedMilliseconds < 15000, "output truncation continues draining a finite process promptly");

        var registry = new McpToolRegistry(); AgentCommandTools.Register(registry, runner);
        var countFile = Path.Combine(root, "runs.txt");
        var command = SelfCommand(["once", countFile]);
        var receipt = await registry.ExecuteAsync("workspace_execute", McpJson.Element(command));
        Check(receipt.IsError && File.ReadAllText(countFile, Utf8) == "1", "failed native tool executions run once and return a model-visible error receipt without retries");
        using (var json = JsonDocument.Parse(receipt.Text)) Check(json.RootElement.GetProperty("exitCode").GetInt32() == 17, "native tool failures preserve the actual exit code");
        Check(!registry.Tools.Single().ReadOnly, "native commands are always advertised as requiring write permission");
        await ExpectAsync<ArgumentException>(() => runner.ExecuteAsync(SelfCommand(["argv"], "../outside")).AsTask(), "commands cannot choose a working directory outside the grant");
        await ExpectAsync<ArgumentException>(() => runner.ExecuteAsync(SelfCommand(["argv", "nul\0arg"])).AsTask(), "NUL command arguments fail before process creation");
        await ExpectAsync<ArgumentException>(() => runner.ExecuteAsync(SelfCommand(["argv"]) with { TimeoutSeconds = 0 }).AsTask(), "invalid native timeout budgets fail before process creation");

        var timeoutState = Path.Combine(root, "timeout.json");
        var timedOut = await runner.ExecuteAsync(SelfCommand(["tree", timeoutState], timeoutSeconds: 2));
        var timeoutTree = await ReadTreeAsync(timeoutState); ChildProcesses.AddRange([timeoutTree.Parent, timeoutTree.Child]);
        Check(timedOut.TimedOut && timedOut.ExitCode != 0 && timedOut.ElapsedMilliseconds < 12000,
            "a timed out process tree is terminated and returns a bounded timeout receipt");
        await CheckStoppedAsync(timeoutTree, "timeout terminates both the command and its descendant");

        using var cancellation = new CancellationTokenSource();
        var canceledState = Path.Combine(root, "canceled.json");
        var canceledTask = runner.ExecuteAsync(SelfCommand(["tree", canceledState]), cancellation.Token).AsTask();
        var canceledTree = await ReadTreeAsync(canceledState); ChildProcesses.AddRange([canceledTree.Parent, canceledTree.Child]);
        cancellation.Cancel();
        await ExpectAsync<OperationCanceledException>(async () => await canceledTask, "caller cancellation is propagated after stopping the native command");
        await CheckStoppedAsync(canceledTree, "caller cancellation terminates the entire child process tree");
        var afterCancel = await runner.ExecuteAsync(SelfCommand(["argv", "next run"]));
        Check(afterCancel.ExitCode == 0 && !afterCancel.TimedOut, "cancellation releases native execution serialization for subsequent commands");

        using var alreadyCanceled = new CancellationTokenSource(); alreadyCanceled.Cancel();
        var neverStarted = Path.Combine(root, "never-started.txt");
        await ExpectAsync<OperationCanceledException>(() => runner.ExecuteAsync(SelfCommand(["once", neverStarted]), alreadyCanceled.Token).AsTask(),
            "an already canceled command is rejected before it starts");
        Check(!File.Exists(neverStarted), "pre-start cancellation has no process side effects");
    }

    private static AgentCommand SelfCommand(IReadOnlyList<string> arguments, string workingDirectory = "", int timeoutSeconds = 20)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the running verification executable.");
        var prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? new[] { Assembly.GetExecutingAssembly().Location } : [];
        return new(executable, [.. prefix, "--child", .. arguments], workingDirectory, timeoutSeconds);
    }

    private static async Task<int> RunChildAsync(string[] args)
    {
        switch (args[0])
        {
            case "argv":
                Console.Write(JsonSerializer.Serialize(new { arguments = args[1..], directory = Environment.CurrentDirectory }));
                return 0;
            case "stdin":
                var input = await Console.In.ReadToEndAsync(); Console.Write("eof:" + input.Length); return 0;
            case "output":
                await Task.WhenAll(WriteManyAsync(Console.Out, 'o'), WriteManyAsync(Console.Error, 'e')); return 23;
            case "once":
                var previous = File.Exists(args[1]) ? int.Parse(File.ReadAllText(args[1], Utf8)) : 0;
                File.WriteAllText(args[1], (previous + 1).ToString(), Utf8); Console.Error.Write("planned failure"); return 17;
            case "tree":
                var child = SelfCommand(["leaf"]);
                var start = new ProcessStartInfo(child.Executable) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var argument in child.Arguments) start.ArgumentList.Add(argument);
                using (var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start a descendant fixture."))
                {
                    File.WriteAllText(args[1], JsonSerializer.Serialize(new ProcessTree(Environment.ProcessId, process.Id)), Utf8);
                    await process.WaitForExitAsync();
                }
                return 0;
            case "leaf":
                await Task.Delay(TimeSpan.FromSeconds(60)); return 0;
            default: throw new ArgumentException("Unknown child fixture.");
        }
    }

    private static async Task WriteManyAsync(TextWriter writer, char value)
    {
        var chunk = new string(value, 4096);
        for (var index = 0; index < 96; index++) await writer.WriteAsync(chunk);
        await writer.FlushAsync();
    }

    private sealed record ProcessTree(int Parent, int Child);

    private static async Task<ProcessTree> ReadTreeAsync(string path)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            try
            {
                if (File.Exists(path)) return JsonSerializer.Deserialize<ProcessTree>(await File.ReadAllTextAsync(path, Utf8))!;
            }
            catch (IOException) { }
            catch (JsonException) { }
            await Task.Delay(20);
        }
        throw new InvalidOperationException("The process-tree fixture did not start within its deadline.");
    }

    private static async Task CheckStoppedAsync(ProcessTree tree, string message)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (!IsRunning(tree.Parent) && !IsRunning(tree.Child)) { Check(true, message); return; }
            await Task.Delay(20);
        }
        Check(false, message);
    }

    private static bool IsRunning(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            if (process.HasExited) return false;
            // A stopped orphan may remain as a zombie until the CI host's init reaps it.
            // A zombie has no executing code or open pipes; do not confuse it with a live child.
            if (OperatingSystem.IsLinux())
            {
                var state = File.ReadAllText($"/proc/{id}/stat");
                var nameEnd = state.LastIndexOf(')');
                if (nameEnd >= 0 && state.Length > nameEnd + 2 && state[nameEnd + 2] == 'Z') return false;
            }
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Verification failed: " + message);
        checks++;
    }

    private static void Expect<TException>(Action operation, string message) where TException : Exception
    {
        try { operation(); }
        catch (TException) { checks++; return; }
        throw new InvalidOperationException("Verification failed (expected " + typeof(TException).Name + "): " + message);
    }

    private static async Task ExpectAsync<TException>(Func<Task> operation, string message) where TException : Exception
    {
        try { await operation(); }
        catch (TException) { checks++; return; }
        throw new InvalidOperationException("Verification failed (expected " + typeof(TException).Name + "): " + message);
    }
}
