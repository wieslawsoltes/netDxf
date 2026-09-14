using netDxf;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterTypedCommentTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        {
            DxfVersion v = version;
            foreach (string mode in new[] { "every-tag", "runs", "section", "header", "table", "subclass", "geometry", "xdata", "eof", "leading" })
            {
                string m = mode;
                Run($"typed/comments/{v}/{m}", () => TypedCommentRoundTrip(v, m));
            }
            Run($"typed/comments/{v}/binary-control", () => TypedCommentBinaryControl(v));
            foreach (bool comment in new[] { false, true })
            {
                bool c = comment;
                Run($"typed/comments/{v}/malformed/{c}", () => TypedCommentMalformed(v, c));
            }
        }
    }

    private static List<DxfTag> TypedCommentTags(DxfVersion version, string mode)
    {
        var original = HatchPatternValidationTags(version);
        var result = new List<DxfTag> { new(999, "leading source comment") };
        string section = "";
        for (int i = 0; i < original.Count; i++)
        {
            var tag = original[i];
            if (tag.Code == 2 && i > 0 && original[i - 1].Code == 0 && Equals(original[i - 1].Value, "SECTION")) section = (string)tag.Value;
            bool inject = mode is "every-tag" or "runs" ||
                (mode == "section" && tag.Code is 0 or 2) || (mode == "header" && section == "HEADER") ||
                (mode == "table" && section == "TABLES") || (mode == "subclass" && tag.Code == 100) ||
                (mode == "geometry" && section == "ENTITIES" && tag.Code is >= 10 and < 100) ||
                (mode == "xdata" && tag.Code >= 1000) || (mode == "eof" && tag.Code == 0 && Equals(tag.Value, "EOF"));
            if (inject)
                for (int n = 0; n < (mode == "runs" ? 33 : 1); n++)
                    result.Add(new(999, "0 SECTION 2 HEADER 0 EOF are comment data " + n));
            result.Add(tag);
        }
        return result;
    }

    private static void TypedCommentRoundTrip(DxfVersion version, string mode)
    {
        var tags = TypedCommentTags(version, mode);
        string[] leading = tags.TakeWhile(t => t.Code == 999).Select(t => (string)t.Value).ToArray();
        byte[] bytes = RawFixtureBytes(tags, false);
        using var input = new MemoryStream(bytes);
        Equal(version, DxfDocument.CheckDxfFileVersion(input, out bool binary), "Commented version probe"); Check(!binary, "Text detected as binary.");
        input.Position = 0;
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Valid commented drawing rejected.");
        Check(doc.Comments.SequenceEqual(leading), "Leading comments changed or interstitial comments leaked into header.");
        HatchPatternCheck(doc, "canonical", 1);
        Equal(new Vector3(20, 30, 40), doc.Entities.Lines.Single().StartPoint, "Comment shifted point fields");
        foreach (bool format in new[] { false, true })
        {
            using var output = new MemoryStream(); Check(doc.Save(output, format), "Commented drawing export failed."); output.Position = 0;
            var raw = DxfRawDocument.Load(output);
            string[] emitted = raw.Tags.Where(t => t.Code == 999).Select(t => (string)t.Value).ToArray();
            Check(emitted.SequenceEqual(format ? Array.Empty<string>() : leading), "Typed comment preservation policy changed.");
            output.Position = 0; var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Commented drawing reload failed.");
            HatchPatternCheck(loaded, "canonical", 1);
            Equal(new Vector3(20, 30, 40), loaded.Entities.Lines.Single().StartPoint, "Following entity changed");
        }
        input.Position = 0;
        var untouched = DxfRawDocument.Load(input);
        using var rawOutput = new MemoryStream(); untouched.Save(rawOutput);
        Check(bytes.SequenceEqual(rawOutput.ToArray()), "Typed comment fix changed exact raw preservation.");
        if (mode == "every-tag") File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"typed-comments-{version}.dxf"), bytes);
        Check(input.CanRead, "Comment wrapper closed caller stream.");
    }

    private static void TypedCommentBinaryControl(DxfVersion version)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchPatternValidationTags(version), true));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Binary behavior changed.");
        HatchPatternCheck(doc, "canonical", 1); Equal(0, doc.Comments.Count, "Binary invented comments");
    }

    private static void TypedCommentMalformed(DxfVersion version, bool comment)
    {
        var tags = TypedCommentTags(version, comment ? "every-tag" : "leading");
        int count = tags.FindIndex(t => t.Code == 78); tags[count] = new(78, (short)-1);
        using var input = new MemoryStream(RawFixtureBytes(tags, false));
#if DEBUG
        try { DxfDocument.Load(input); throw new InvalidOperationException("Comments hid a malformed HATCH count."); }
        catch (InvalidDataException error) { Check(error.Message.Contains("HATCH", StringComparison.Ordinal), "Comment filtering lost the underlying diagnostic."); }
#else
        Check(DxfDocument.Load(input) == null, "Comments hid malformed input.");
#endif
    }
}
