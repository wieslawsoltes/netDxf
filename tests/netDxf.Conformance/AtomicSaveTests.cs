using System.Reflection;
using System.Runtime.ExceptionServices;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static DxfVersion[] AtomicRawVersions =>
        new[] { DxfVersion.AutoCad12, DxfVersion.AutoCad13, DxfVersion.AutoCad14 }.Concat(SupportedVersions).ToArray();

    private static void RegisterAtomicSaveTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (bool existing in new[] { false, true })
                {
                    DxfVersion v = version; bool b = binary, e = existing;
                    Run($"atomic/typed/success/{v}/{b}/{e}", () => AtomicTypedSuccess(v, b, e));
                    Run($"atomic/typed/failure/{v}/{b}/{e}", () => AtomicTypedFailure(v, b, e));
                    Run($"atomic/typed/cancellation/{v}/{b}/{e}", () => AtomicTypedCancellation(v, b, e));
                }
        foreach (DxfVersion version in AtomicRawVersions)
            foreach (bool binary in new[] { false, true })
                foreach (bool existing in new[] { false, true })
                {
                    DxfVersion v = version; bool b = binary, e = existing;
                    Run($"atomic/raw/exact/{v}/{b}/{e}", () => AtomicRawExact(v, b, e));
                    Run($"atomic/raw/invalid-transport/{v}/{b}/{e}", () => AtomicRawInvalid(v, b, e));
                }
        foreach (bool existing in new[] { false, true })
            foreach (int failure in Enumerable.Range(0, 4))
            {
                bool e = existing; int f = failure;
                Run($"atomic/staged-failure/{e}/{f}", () => AtomicStagedFailure(e, f));
            }
        Run("atomic/open-path-errors", AtomicPathErrors);
        Run("atomic/symlink-and-readonly", AtomicLinksAndReadOnly);
        Run("atomic/reader-observes-old-or-new", AtomicReaderVisibility);
        Run("atomic/raw/budget-preserves-file", AtomicRawBudget);
    }

    private static void WithAtomicDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "netdxf-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(Path.Combine(directory, "Zażółć drawing.dxf")); }
        finally { Directory.Delete(directory, true); }
    }

    private static readonly byte[] AtomicOriginal = Enumerable.Range(0, 257).Select(i => (byte)i).ToArray();
    private static void AtomicPrepare(string path, bool existing)
    {
        if (existing) File.WriteAllBytes(path, AtomicOriginal);
    }
    private static void AtomicUnchanged(string path, bool existing)
    {
        Equal(existing, File.Exists(path), "Atomic failure changed destination existence");
        if (existing) Check(AtomicOriginal.SequenceEqual(File.ReadAllBytes(path)), "Atomic failure changed old file bytes.");
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, ".netdxf-*.tmp").Any(), "Staging file leaked.");
        CheckLifetimeFileReleased(path);
    }

    private static void AtomicTypedSuccess(DxfVersion version, bool binary, bool existing) => WithAtomicDirectory(path =>
    {
        AtomicPrepare(path, existing);
        var doc = new DxfDocument(version); doc.Comments.Clear();
        doc.Entities.Add(new Line(new Vector3(1e-20, 2, 3), new Vector3(4, 5, 6)));
        doc.SaveAtomic(path, binary);
        Equal("Zażółć drawing", doc.Name, "Atomic document name");
        Equal(Path.GetDirectoryName(path), doc.SupportFolders.WorkingFolder, "Atomic working folder");
        var loaded = DxfDocument.Load(path) ?? throw new InvalidOperationException("Atomic typed reload failed.");
        Equal(version, loaded.DrawingVariables.AcadVer, "Atomic typed version");
        SameDoubleBits(1e-20, loaded.Entities.Lines.Single().StartPoint.X, "Atomic typed geometry");
        CheckLifetimeFileReleased(path);
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, ".netdxf-*.tmp").Any(), "Success leaked staging file.");
        File.Copy(path, Path.Combine(ArtifactDirectory, $"atomic-save-{version}-{binary}-{existing}.dxf"), overwrite: true);
    });

    private static void AtomicTypedFailure(DxfVersion version, bool binary, bool existing) => WithAtomicDirectory(path =>
    {
        AtomicPrepare(path, existing);
        var doc = FileLifetimeConflictingDocument(version);
        doc.Name = "original name";
        string originalFolder = doc.SupportFolders.WorkingFolder;
        Throws<InvalidDataException>(() => doc.SaveAtomic(path, binary)); // Same contract in Release.
        Equal("original name", doc.Name, "Failed atomic save changed name");
        Equal(originalFolder, doc.SupportFolders.WorkingFolder, "Failed atomic save changed working folder");
        AtomicUnchanged(path, existing);
    });

    private static void AtomicTypedCancellation(DxfVersion version, bool binary, bool existing) => WithAtomicDirectory(path =>
    {
        AtomicPrepare(path, existing);
        var doc = new DxfDocument(version) { Name = "not touched" };
        string handles = doc.DrawingVariables.HandleSeed;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Throws<OperationCanceledException>(() => doc.SaveAtomic(path, binary, cancellation.Token));
        Equal(handles, doc.DrawingVariables.HandleSeed, "Pre-cancellation allocated handles");
        Equal("not touched", doc.Name, "Pre-cancellation changed name");
        AtomicUnchanged(path, existing);
    });

    private static DxfRawDocument AtomicRawSource(DxfVersion version, bool binary, bool invalid = false)
    {
        var tags = new List<DxfTag>
        {
            new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"), new(1, version switch { DxfVersion.AutoCad12 => "AC1009", DxfVersion.AutoCad13 => "AC1012", DxfVersion.AutoCad14 => "AC1014", _ => HeaderVersion(version) }),
            new(9, "$DWGCODEPAGE"), new(3, "ANSI_1252"), new(0, "ENDSEC"),
            new(0, "SECTION"), new(2, "ENTITIES"), new(0, "LINE"), new(5, "AB"),
            new(10, 1e-20), new(20, -0.0), new(30, 3.0), new(11, 4.0), new(21, 5.0), new(31, 6.0)
        };
        if (invalid) tags.Add(binary ? new DxfTag(1, "binary\nmultiline") : new DxfTag(999, "comment"));
        tags.Add(new(0, "ENDSEC")); tags.Add(new(0, "EOF"));
        return DxfRawDocument.Create(tags, binary);
    }

    private static void AtomicRawExact(DxfVersion version, bool binary, bool existing) => WithAtomicDirectory(path =>
    {
        var authored = AtomicRawSource(version, binary);
        using var input = new MemoryStream(); authored.Save(input); byte[] expected = input.ToArray();
        input.Position = 0; var raw = DxfRawDocument.Load(input);
        AtomicPrepare(path, existing); raw.SaveAtomic(path);
        Check(expected.SequenceEqual(File.ReadAllBytes(path)), "Same-transport raw atomic save lost byte identity.");
        raw.SaveAtomic(path, !binary);
        using var changed = File.OpenRead(path); SameRawTags(raw.Tags, DxfRawDocument.Load(changed).Tags);
        Check(raw.HasOriginalBytes, "Raw atomic save mutated snapshot.");
        Check(input.CanRead, "Raw save closed unrelated caller stream.");
    });

    private static void AtomicRawInvalid(DxfVersion version, bool binary, bool existing) => WithAtomicDirectory(path =>
    {
        AtomicPrepare(path, existing);
        var raw = AtomicRawSource(version, binary, true);
        Throws<NotSupportedException>(() => raw.SaveAtomic(path, !binary));
        AtomicUnchanged(path, existing);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Throws<OperationCanceledException>(() => raw.SaveAtomic(path, binary, cancellation.Token));
        AtomicUnchanged(path, existing);
    });

    private static void InvokeAtomicWriter(string path, Action<FileStream> write, CancellationToken token = default)
    {
        MethodInfo method = typeof(DxfDocument).Assembly.GetType("netDxf.IO.DxfAtomicFile", true)!
            .GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic)!;
        try { method.Invoke(null, new object[] { path, write, token }); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private static void AtomicStagedFailure(bool existing, int mode) => WithAtomicDirectory(path =>
    {
        AtomicPrepare(path, existing);
        using var cancellation = new CancellationTokenSource();
        Action<FileStream> write = stream =>
        {
            Equal(Path.GetDirectoryName(path), Path.GetDirectoryName(stream.Name), "Staging file not adjacent");
            stream.Write(new byte[123456]);
            if (mode == 0) throw new IOException("Injected disk write failure");
            if (mode == 1) { cancellation.Cancel(); return; }
            if (mode == 2) { stream.Dispose(); return; } // Fails the real flush.
            if (existing) File.Delete(path); else File.WriteAllBytes(path, AtomicOriginal); // Race detection.
        };
        if (mode == 1) Throws<OperationCanceledException>(() => InvokeAtomicWriter(path, write, cancellation.Token));
        else if (mode == 2) Throws<ObjectDisposedException>(() => InvokeAtomicWriter(path, write));
        else Throws<IOException>(() => InvokeAtomicWriter(path, write));
        AtomicUnchanged(path, mode == 3 ? !existing : existing);
    });

    private static void AtomicPathErrors() => WithAtomicDirectory(path =>
    {
        var doc = new DxfDocument();
        Throws<ArgumentNullException>(() => doc.SaveAtomic(null!));
        Throws<ArgumentException>(() => doc.SaveAtomic(""));
        Throws<IOException>(() => doc.SaveAtomic(Path.GetDirectoryName(path)!));
        Throws<DirectoryNotFoundException>(() => doc.SaveAtomic(Path.Combine(path, "missing", "a.dxf")));
        AtomicUnchanged(path, false);
    });

    private static void AtomicLinksAndReadOnly() => WithAtomicDirectory(path =>
    {
        File.WriteAllBytes(path, AtomicOriginal); File.SetAttributes(path, FileAttributes.ReadOnly);
        try { Throws<UnauthorizedAccessException>(() => new DxfDocument().SaveAtomic(path)); }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        AtomicUnchanged(path, true);
        if (!OperatingSystem.IsWindows()) // Windows symlink creation needs an external privilege setting.
        {
            string link = path + ".link"; File.CreateSymbolicLink(link, path);
            Throws<NotSupportedException>(() => new DxfDocument().SaveAtomic(link));
            Check(new FileInfo(link).LinkTarget == path, "Atomic save replaced link.");
            AtomicUnchanged(path, true);
            File.Delete(link); File.CreateSymbolicLink(link, path + ".missing");
            Throws<NotSupportedException>(() => new DxfDocument().SaveAtomic(link));
            Check(!File.Exists(path + ".missing"), "Atomic save followed dangling link.");
        }
    });

    private static void AtomicReaderVisibility() => WithAtomicDirectory(path =>
    {
        AtomicPrepare(path, true);
        using var opened = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] replacement = Enumerable.Repeat((byte)71, 300000).ToArray();
        InvokeAtomicWriter(path, stream =>
        {
            stream.Write(replacement, 0, replacement.Length / 2);
            Check(AtomicOriginal.SequenceEqual(File.ReadAllBytes(path)), "Uncommitted prefix visible.");
            stream.Write(replacement, replacement.Length / 2, replacement.Length / 2);
        });
        Check(replacement.SequenceEqual(File.ReadAllBytes(path)), "Commit contains mixed old/new contents.");
        using var original = new MemoryStream(); opened.CopyTo(original);
        Check(AtomicOriginal.SequenceEqual(original.ToArray()), "Existing reader did not retain old file contents.");
    });

    private static void AtomicRawBudget() => WithAtomicDirectory(path =>
    {
        AtomicPrepare(path, true);
        var source = AtomicRawSource(DxfVersion.AutoCad2018, false);
        var limited = DxfRawDocument.Create(source.Tags, false, new DxfRawOptions(16, 1000, 100));
        Throws<InvalidDataException>(() => limited.SaveAtomic(path));
        AtomicUnchanged(path, true);
    });
}
