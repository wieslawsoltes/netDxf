using System.Reflection;
using netDxf;
using netDxf.Collections;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterSupportFolderLookupTests()
    {
        Run("support-lookup/direct-file-precedence", SupportLookupDirect);
        Run("support-lookup/ordered-fallback", SupportLookupOrder);
        Run("support-lookup/relative-and-absolute-folders", SupportLookupPaths);
        Run("support-lookup/empty-and-missing", SupportLookupMissing);
        Run("support-lookup/process-directory-observer", SupportLookupDirectoryObserver);
        Run("support-lookup/independent-concurrent-documents", SupportLookupConcurrent);
        Run("support-lookup/platform-path-oracle", SupportLookupPathOracle);
        Run("support-lookup/failure-does-not-change-directory", SupportLookupFailure);
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"support-lookup/loaded-document/{v}/{b}", () => SupportLookupDocument(v, b));
            }
    }

    private static void WithSupportLookupFolders(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "netDxf-lookup-" + Guid.NewGuid().ToString("N"));
        string current = Environment.CurrentDirectory;
        Directory.CreateDirectory(root);
        try { test(root); }
        finally
        {
            // Restore even the old implementation's racing CWD writes before red-test cleanup.
            Environment.CurrentDirectory = current;
            Directory.Delete(root, true);
        }
    }

    private static string SupportLookupFile(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, name);
        File.WriteAllText(file, "resource fixture, not opened by FindFile");
        return Path.GetFullPath(file);
    }

    private static void SupportLookupDirect() => WithSupportLookupFolders(root =>
    {
        string drawing = Path.Combine(root, "drawing");
        string first = Path.Combine(drawing, "first"); string second = Path.Combine(root, "second");
        string expected = SupportLookupFile(drawing, "symbol.shx");
        SupportLookupFile(first, "symbol.shx"); SupportLookupFile(second, "symbol.shx");
        var folders = new SupportFolders(new[] { "first", second }) { WorkingFolder = drawing };
        Equal(expected, folders.FindFile("symbol.shx"), "Existing drawing-relative file was overridden");
        Equal(expected, folders.FindFile(expected), "Existing absolute file was overridden");
        Equal(expected, folders.FindFile("." + Path.DirectorySeparatorChar + "symbol.shx"), "Dot-relative file precedence");
    });

    private static void SupportLookupOrder() => WithSupportLookupFolders(root =>
    {
        string drawing = Path.Combine(root, "drawing"); Directory.CreateDirectory(drawing);
        string first = Path.Combine(drawing, "first"); string second = Path.Combine(root, "second");
        string firstFile = SupportLookupFile(first, "symbol.shx");
        string secondFile = SupportLookupFile(second, "symbol.shx");
        var folders = new SupportFolders(new[] { "missing", "first", second }) { WorkingFolder = drawing };
        Equal(firstFile, folders.FindFile(Path.Combine("lost", "symbol.shx")), "First matching support folder did not win");
        folders.Remove("first"); Equal(secondFile, folders.FindFile("symbol.shx"), "Support folder removal was ignored");
        folders.Insert(0, "first"); Equal(firstFile, folders.FindFile("symbol.shx"), "Support folder order was ignored");
    });

    private static void SupportLookupPaths() => WithSupportLookupFolders(root =>
    {
        string drawing = Path.Combine(root, "drawing"); Directory.CreateDirectory(drawing);
        string unicode = "Zażółć 東京.shx";
        string parent = SupportLookupFile(root, unicode);
        string child = SupportLookupFile(Path.Combine(drawing, "fonts"), "child.shx");
        var folders = new SupportFolders(new[] { "fonts", ".." }) { WorkingFolder = drawing };
        string current = Environment.CurrentDirectory;
        Equal(child, folders.FindFile(Path.Combine("invalid", "child.shx")), "Relative support folder base");
        Equal(parent, folders.FindFile(unicode), "Parent support folder and Unicode path");
        Equal(parent, folders.FindFile(Path.Combine("..", unicode)), "Parent direct path");
        Equal(current, Environment.CurrentDirectory, "Lookup changed process directory");
        // Resolve a relative WorkingFolder once against the caller's current process directory.
        Environment.CurrentDirectory = root;
        folders.WorkingFolder = "drawing";
        Equal(child, folders.FindFile("child.shx"), "Relative working folder");
        Equal(root, Environment.CurrentDirectory, "Relative working folder changed process directory");
    });

    private static void SupportLookupMissing() => WithSupportLookupFolders(root =>
    {
        var folders = new SupportFolders(new string[] { null!, "", "missing" }) { WorkingFolder = root };
        Equal("", folders.FindFile(null!), "Null filename should remain a miss");
        Equal("", folders.FindFile(""), "Empty filename should remain a miss");
        Equal("", folders.FindFile("absent.shx"), "Missing filename");
        Equal("", folders.FindFile(root + Path.DirectorySeparatorChar), "A directory is not a file");
        string expected = SupportLookupFile(root, "exists.shx");
        folders.WorkingFolder = Path.Combine(root, "not-created");
        Equal(expected, folders.FindFile(expected), "An absolute resource should not require the working directory to exist");
        Equal("", folders.FindFile("absent.shx"), "Nonexistent working folder should give a miss");
    });

    private static void SupportLookupDirectoryObserver() => WithSupportLookupFolders(root =>
    {
        string before = Environment.CurrentDirectory;
        var folders = new SupportFolders(Enumerable.Range(0, 5000).Select(i => "missing-" + i)) { WorkingFolder = root };
        using var ready = new ManualResetEventSlim();
        int done = 0, observedChange = 0, reads = 0;
        Task observer = Task.Run(() =>
        {
            ready.Set();
            while (Volatile.Read(ref done) == 0)
            {
                if (Environment.CurrentDirectory != before) Interlocked.Exchange(ref observedChange, 1);
                Interlocked.Increment(ref reads); Thread.Yield();
            }
        });
        try
        {
            Check(ready.Wait(TimeSpan.FromSeconds(10)), "Directory observer did not start.");
            Equal("", folders.FindFile("not-present.shx"), "Observer fixture unexpectedly found a file");
        }
        finally { Volatile.Write(ref done, 1); observer.GetAwaiter().GetResult(); }
        Check(reads > 0, "The observer did not sample the process directory.");
        Equal(0, observedChange, "Lookup temporarily changed the process-wide current directory");
        Equal(before, Environment.CurrentDirectory, "Lookup did not restore the process directory");
    });

    private static void SupportLookupConcurrent() => WithSupportLookupFolders(root =>
    {
        string before = Environment.CurrentDirectory;
        string a = Path.Combine(root, "A"), b = Path.Combine(root, "B");
        string fileA = SupportLookupFile(a, "shared-name.shx"), fileB = SupportLookupFile(b, "shared-name.shx");
        var first = new SupportFolders { WorkingFolder = a };
        var second = new SupportFolders { WorkingFolder = b };
        Parallel.For(0, 64, worker =>
        {
            var source = (worker & 1) == 0 ? first : second;
            string expected = (worker & 1) == 0 ? fileA : fileB;
            for (int i = 0; i < 50; i++) Equal(expected, source.FindFile("shared-name.shx"), "Concurrent drawings resolved each other's resources");
        });
        Equal(before, Environment.CurrentDirectory, "Concurrent lookups changed the process directory");
    });

    private static void SupportLookupPathOracle()
    {
        MethodInfo resolve = typeof(SupportFolders).GetMethod("ResolvePath", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Explicit-base resolver was not found.");
        string[] bases = OperatingSystem.IsWindows()
            ? new[] { @"C:\projects\drawings", @"C:\", @"\\server\share\projects\drawings", @"\\?\C:\projects\drawings",
                @"\\?\UNC\server\share\projects\drawings", @"\\.\C:\projects\drawings" }
            : new[] { "/", "/projects/drawings", "/projects/drawings/" };
        string[] paths = OperatingSystem.IsWindows()
            ? new[] { "", "file.shx", @"fonts\shape.shx", @".\file.shx", @"..\file.shx", @"..\..\..\file.shx",
                @"sub\..\file.shx", @"sub\.", @"sub\..", @"sub\..\", @"sub\\file.shx", "sub/../file.shx",
                @"\rooted.shx", "/rooted.shx", "C:drive.shx", "c:drive.shx", "D:drive.shx", "C:", "D:",
                @"D:\absolute.shx", @"\\other\share\absolute.shx", @"\\?\D:\literal\..\absolute.shx" }
            : new[] { "", "file.shx", "fonts/shape.shx", "./file.shx", "../file.shx", "../../../file.shx",
                "sub/../file.shx", "sub/.", "sub/..", "sub/../", "sub//file.shx", "/absolute.shx", @"back\slash.shx" };
        string current = Environment.CurrentDirectory;
        foreach (string basePath in bases)
            foreach (string path in paths)
            {
                // .NET 8 compares drive names case-sensitively here; current .NET uses
                // ordinal-ignore-case. Normalize only this oracle input for the same drive.
                string oraclePath = path;
                string root = Path.GetPathRoot(basePath)!;
                int driveOffset = root.StartsWith(@"\\?\", StringComparison.Ordinal) || root.StartsWith(@"\\.\", StringComparison.Ordinal) ? 4 : 0;
                if (OperatingSystem.IsWindows() && path.Length >= 2 && path[1] == ':' &&
                    root.Length > driveOffset + 1 && root[driveOffset + 1] == ':' &&
                    char.ToUpperInvariant(path[0]) == char.ToUpperInvariant(root[driveOffset]))
                    oraclePath = root[driveOffset] + path[1..];
                string expected = Path.GetFullPath(oraclePath, basePath);
                string actual = (string)resolve.Invoke(null, new object[] { path, basePath })!;
                Equal(expected, actual, $"Explicit-base path resolution for {path} relative to {basePath}");
            }
        Equal(current, Environment.CurrentDirectory, "Pure path resolution changed the process directory");
    }

    private static void SupportLookupFailure() => WithSupportLookupFolders(root =>
    {
        string current = Environment.CurrentDirectory;
        var folders = new SupportFolders { WorkingFolder = root };
        Throws<ArgumentException>(() => folders.FindFile("bad\0path.shx"));
        Equal(current, Environment.CurrentDirectory, "Invalid filename changed process directory");
        folders.WorkingFolder = "bad\0directory";
        Throws<ArgumentException>(() => folders.FindFile("absent.shx"));
        Equal(current, Environment.CurrentDirectory, "Invalid base path changed process directory");
        folders.WorkingFolder = root;
        folders.AddRange(new[] { "bad\0support" });
        Throws<ArgumentException>(() => folders.FindFile("absent.shx"));
        Equal(current, Environment.CurrentDirectory, "Invalid support path changed process directory");
    });

    private static void SupportLookupDocument(DxfVersion version, bool binary) => WithSupportLookupFolders(root =>
    {
        string drawing = Path.Combine(root, "drawing"); Directory.CreateDirectory(drawing);
        string expected = SupportLookupFile(Path.Combine(drawing, "fonts"), "shape-Żółć.shx");
        SupportLookupFile(Path.Combine(drawing, "later-fonts"), "shape-Żółć.shx");
        string path = Path.Combine(drawing, "drawing.dxf");
        var document = new DxfDocument(version);
        Check(document.Save(path, binary), "Support lookup document failed to save.");
        string current = Environment.CurrentDirectory;
        var loaded = DxfDocument.Load(path, new[] { "fonts", "later-fonts" }) ?? throw new InvalidOperationException("Support lookup document failed to load.");
        Equal(expected, loaded.SupportFolders.FindFile("shape-Żółć.shx"), "Loaded drawing used wrong relative resource base or priority");
        Equal(current, Environment.CurrentDirectory, "Loaded drawing's lookup changed process directory");
    });
}
