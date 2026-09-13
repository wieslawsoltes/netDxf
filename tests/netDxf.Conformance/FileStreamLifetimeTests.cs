using System.Runtime;
using System.Text;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterFileStreamLifetimeTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"file-lifetime/success/{v}/{b}", () => FileLifetimeSuccess(v, b));
                Run($"file-lifetime/setup-null/{v}/{b}", () => FileLifetimeSetup(v, b, false));
                Run($"file-lifetime/setup-enumeration/{v}/{b}", () => FileLifetimeSetup(v, b, true));
                Run($"file-lifetime/malformed-load/{v}/{b}", () => FileLifetimeMalformed(v, b));
                Run($"file-lifetime/failed-save/{v}/{b}", () => FileLifetimeFailedSave(v, b));
            }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            Run($"file-lifetime/unsupported-version/{b}", () => FileLifetimeUnsupported(b));
            Run($"file-lifetime/caller-streams/{b}", () => FileLifetimeCallerStreams(b));
        }
        Run("file-lifetime/open-failures", FileLifetimeOpenFailures);
    }

    private static byte[] FileLifetimeFixture(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version);
        document.Entities.Add(new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)));
        using var output = new MemoryStream();
        Check(document.Save(output, binary), "Lifetime fixture save failed.");
        return output.ToArray();
    }

    private static void WithLifetimeFile(Action<string> test)
    {
        string directory = Path.Combine(Path.GetTempPath(), "netDxf-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "drawing.dxf");
        bool success = false;
        bool noGc = GC.TryStartNoGCRegion(16 * 1024 * 1024, disallowFullBlockingGC: true);
        try
        {
            test(file);
            CheckLifetimeFileReleased(file);
            success = true;
        }
        finally
        {
            if (noGc && GCSettings.LatencyMode == GCLatencyMode.NoGCRegion) GC.EndNoGCRegion();
            // Finalizers must not mask the assertion. Only release leaked baseline handles
            // AFTER failure has been observed, so a red run can clean up its temporary files.
            if (!success) { GC.Collect(); GC.WaitForPendingFinalizers(); }
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CheckLifetimeFileReleased(string path)
    {
        if (!File.Exists(path)) return;
        if (OperatingSystem.IsLinux())
        {
            // Unix unlink succeeds for open files, and FileShare is not a portable leak oracle.
            foreach (string fd in Directory.EnumerateFiles("/proc/self/fd"))
            {
                string? target = new FileInfo(fd).LinkTarget;
                Check(target != path, "The file overload left an open descriptor: " + path);
            }
        }
        // This checks Windows sharing immediately, without waiting for GC/finalization.
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static void FileLifetimeSuccess(DxfVersion version, bool binary) => WithLifetimeFile(file =>
    {
        File.WriteAllBytes(file, FileLifetimeFixture(version, binary));
        string directory = Path.GetDirectoryName(file)!;
        var document = DxfDocument.Load(file, new[] { directory }) ?? throw new InvalidOperationException("File load failed.");
        Equal("drawing", document.Name, "Loaded file name");
        Equal(directory, document.SupportFolders.WorkingFolder, "Loaded working folder");
        Equal(new Vector3(1, 2, 3), document.Entities.Lines.Single().StartPoint, "Loaded geometry");
        CheckLifetimeFileReleased(file);
        Equal(version, DxfDocument.CheckDxfFileVersion(file, out bool detected), "File probe version");
        Equal(binary, detected, "File probe transport");
        CheckLifetimeFileReleased(file);
        Check(document.Save(file, !binary), "File save failed.");
        Equal("drawing", document.Name, "Saved file name");
        Equal(directory, document.SupportFolders.WorkingFolder, "Saved working folder");
        CheckLifetimeFileReleased(file);
        Equal(version, DxfDocument.CheckDxfFileVersion(file), "Version-only file probe");
        var loaded = DxfDocument.Load(file) ?? throw new InvalidOperationException("Saved file reload failed.");
        Equal(new Vector3(4, 5, 6), loaded.Entities.Lines.Single().EndPoint, "Reloaded geometry");
    });

    private static IEnumerable<string> ThrowingSupportFolders()
    {
        yield return "a valid first folder";
        throw new InvalidOperationException("Injected support-folder enumeration failure.");
    }

    private static void FileLifetimeSetup(DxfVersion version, bool binary, bool enumeration) => WithLifetimeFile(file =>
    {
        File.WriteAllBytes(file, FileLifetimeFixture(version, binary));
        if (enumeration) Throws<InvalidOperationException>(() => DxfDocument.Load(file, ThrowingSupportFolders()));
        else Throws<ArgumentNullException>(() => DxfDocument.Load(file, null!));
        // Setup exceptions must propagate in both configurations, not become Release null.
    });

    private static void FileLifetimeMalformed(DxfVersion version, bool binary) => WithLifetimeFile(file =>
    {
        byte[] bytes = FileLifetimeFixture(version, binary);
        // Truncate at a complete tag boundary, independent of timestamps, locale and
        // platform line endings. An arbitrary byte midpoint can instead split a value.
        if (binary)
        {
            byte[] eof = { 0, 0, (byte)'E', (byte)'O', (byte)'F', 0 };
            Check(bytes.TakeLast(eof.Length).SequenceEqual(eof), "Binary fixture EOF framing changed.");
            File.WriteAllBytes(file, bytes.Take(bytes.Length - eof.Length).ToArray());
        }
        else
        {
            string text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Replace("\r\n", "\n");
            string[] lines = text.TrimEnd('\n').Split('\n');
            Equal("0", lines[^2].Trim(), "Text fixture EOF group code");
            Equal("EOF", lines[^1], "Text fixture EOF value");
            File.WriteAllText(file, string.Join("\n", lines.Take(lines.Length - 2)) + "\n", new UTF8Encoding(false));
        }
#if DEBUG
        Throws<EndOfStreamException>(() => DxfDocument.Load(file));
#else
        Check(DxfDocument.Load(file) == null, "Malformed file was accepted.");
#endif
    });

    private static DxfDocument FileLifetimeConflictingDocument(DxfVersion version)
    {
        var document = new DxfDocument(version);
        document.Classes.Add(new DxfClass("RASTERVARIABLES", "WrongCpp", "Lifetime test"));
        return document;
    }

    private static void FileLifetimeFailedSave(DxfVersion version, bool binary) => WithLifetimeFile(file =>
    {
        File.WriteAllText(file, "previous file contents");
        var document = FileLifetimeConflictingDocument(version);
#if DEBUG
        Throws<InvalidDataException>(() => document.Save(file, binary));
#else
        Check(!document.Save(file, binary), "Conflicting file export unexpectedly succeeded.");
#endif
        // Preserve the existing File.Create contract: this fix closes the file; it does NOT
        // promise transactional replacement or preservation of the old destination bytes.
        Equal(0L, new FileInfo(file).Length, "File.Create truncation policy changed");
    });

    private static void FileLifetimeUnsupported(bool binary) => WithLifetimeFile(file =>
    {
        using var fixture = new MemoryStream(); object writer = NewCodeWriter(fixture, binary);
        foreach (var tag in new (short Code, string Value)[]
        { (0, "SECTION"), (2, "HEADER"), (9, "$ACADVER"), (1, "AC1009"), (0, "ENDSEC"), (0, "EOF") })
            Invoke(writer, "Write", tag.Code, tag.Value);
        Invoke(writer, "Flush"); File.WriteAllBytes(file, fixture.ToArray());
        Throws<DxfVersionNotSupportedException>(() => DxfDocument.Load(file));
    });

    private static void FileLifetimeCallerStreams(bool binary)
    {
        using var input = new MemoryStream(FileLifetimeFixture(DxfVersion.AutoCad2018, binary));
        var document = DxfDocument.Load(input) ?? throw new InvalidOperationException("Caller input failed.");
        Check(input.CanRead, "File lifetime change closed caller input.");
        using var output = new MemoryStream();
        Check(document.Save(output, binary), "Caller output failed.");
        Check(output.CanWrite, "File lifetime change closed caller output.");
        using var failed = new MemoryStream();
#if DEBUG
        Throws<InvalidDataException>(() => FileLifetimeConflictingDocument(DxfVersion.AutoCad2018).Save(failed, binary));
#else
        Check(!FileLifetimeConflictingDocument(DxfVersion.AutoCad2018).Save(failed, binary), "Invalid caller save succeeded.");
#endif
        Check(failed.CanWrite, "Failed save closed caller output.");
    }

    private static void FileLifetimeOpenFailures() => WithLifetimeFile(file =>
    {
        Throws<FileNotFoundException>(() => DxfDocument.Load(file));
        Throws<FileNotFoundException>(() => DxfDocument.CheckDxfFileVersion(file));
        Throws<DirectoryNotFoundException>(() => new DxfDocument().Save(Path.Combine(file, "missing", "drawing.dxf")));
    });
}
