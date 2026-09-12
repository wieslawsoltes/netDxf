using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetDxf.FieldAudit;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test")
            {
                SelfTest();
                Console.WriteLine("Field audit self-test passed.");
                return 0;
            }
            if (args.Length is < 2 or > 3)
            {
                Console.Error.WriteLine("Usage: netDxf.FieldAudit <repository-root> <output-directory> [source-revision]");
                return 2;
            }
            string root = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);
            string revision = args.Length == 3 ? args[2] : GitRevision(root);
            string io = Path.Combine(root, "netDxf", "IO");
            if (!Directory.Exists(io)) throw new DirectoryNotFoundException(io);
            var files = Directory.GetFiles(io, "*.cs").OrderBy(x => x, StringComparer.Ordinal)
                .Select(path => Analyze(root, path)).ToArray();
            var modelFiles = new[] { "Entities", "Objects", "Tables", "Header" }.ToDictionary(
                directory => directory,
                directory => Directory.GetFiles(Path.Combine(root, "netDxf", directory), "*.cs")
                    .OrderBy(x => x, StringComparer.Ordinal).Select(path => AnalyzeModel(root, path)).ToArray());
            Directory.CreateDirectory(output);
            var report = new
            {
                schemaVersion = 1,
                sourceRevision = revision,
                warning = "Syntax inventory, not semantic or standards conformance. Only literal group codes in a switch on .Code and literal first arguments of .chunk.Write or chunk.Write are indexed. Conditional reachability, assignments, helper calls, derived fields, unknown-tag preservation, and target-version validity require review. File hashes identify the actual bytes analyzed; a revision label alone does not prove a clean worktree.",
                files,
                models = modelFiles
            };
            File.WriteAllText(Path.Combine(output, "field-inventory.json"), JsonSerializer.Serialize(report, Json) + "\n");
            File.WriteAllText(Path.Combine(output, "field-inventory.md"), Markdown(revision, files, modelFiles));
            Console.WriteLine($"Indexed {files.Length} IO files, {files.Sum(f => f.Methods.Length)} methods and {modelFiles.Values.Sum(f => f.Length)} model/header files.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void SelfTest()
    {
        string root = Path.Combine(Path.GetTempPath(), "netdxf-field-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "Fixture.cs");
            File.WriteAllText(path, """
                class Fixture
                {
                    public int Height { get; set; }
                    public int Width => 1;
                    private int Hidden { get; set; }
                    void Read()
                    {
                        string braces = "} case 777: {";
                        // case 778: and chunk.Write(779, 0) are comments.
                        switch (this.chunk.Code)
                        {
                            case 10:
                                switch (mode) { case 999: break; }
                                this.ReadHelper();
                                break;
                            case 20: break;
                        }
                        if (version >= DxfVersion.AutoCad2010) this.chunk.Write(90, count);
                        chunk.Write(dynamicCode, value);
                        switch (kind) { case DxfObjectCode.Line: break; }
                    }
                }
                """);
            FileEvidence evidence = Analyze(root, path);
            MethodEvidence method = evidence.Methods.Single();
            Require(method.ReadCases.Select(t => t.Code).SequenceEqual(new int?[] { 10, 20 }), "Nested and quoted numeric cases must not count.");
            Require(method.Writes.Length == 2 && method.Writes[0].Code == 90 && method.Writes[1].Code == null, "Literal and dynamic writes must remain distinct.");
            Require(method.VersionConditions.Length == 1 && method.VersionConditions[0].Text.Contains("AutoCad2010", StringComparison.Ordinal), "Version condition was lost.");
            Require(method.Dispatch.Length == 1 && method.Dispatch[0].Text.Contains("DxfObjectCode.Line", StringComparison.Ordinal), "Record dispatch was lost.");
            Require(method.HelperCalls.SequenceEqual(new[] { "ReadHelper" }), "Helper calls were lost.");
            Require(evidence.Sha256 == Hash(path), "Source hash mismatch.");
            ModelEvidence model = AnalyzeModel(root, path);
            Require(model.Properties.Length == 2 && model.Properties[0].HasSetter && !model.Properties[1].HasSetter, "Public property filtering failed.");
            Require(Markdown("test", new[] { evidence }, new Dictionary<string, ModelEvidence[]>()).Contains("(+ dynamic codes; see JSON)", StringComparison.Ordinal), "Dynamic writes disappeared from Markdown.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string GitRevision(string root)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("rev-parse");
        start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start git.");
        string revision = process.StandardOutput.ReadToEnd().Trim();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
        return revision;
    }

    private static CompilationUnitSyntax Parse(string path)
    {
        // Include the usual runtime/debug branch. Other preprocessor variants must
        // be audited independently; IO record fields currently have no such split.
        var options = new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: new[] { "DEBUG", "TRACE", "NET8_0", "NETCOREAPP" });
        SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), options, path);
        var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
        return tree.GetCompilationUnitRoot();
    }

    private static FileEvidence Analyze(string root, string path)
    {
        CompilationUnitSyntax syntax = Parse(path);
        var methods = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Select(method =>
        {
            var read = method.DescendantNodes().OfType<SwitchStatementSyntax>()
                .Where(s => s.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "Code")
                .SelectMany(s => s.Sections.SelectMany(section => section.Labels.OfType<CaseSwitchLabelSyntax>()))
                .Where(label => label.Value is LiteralExpressionSyntax)
                .Select(label => new TagEvidence(Integer(label.Value), Line(label), label.Parent?.ToString() ?? ""))
                .Where(e => e.Code.HasValue).ToArray();
            var writeCalls = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(call => call.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "Write"
                    && (member.Expression.ToString() == "this.chunk" || member.Expression.ToString() == "chunk")
                    && call.ArgumentList.Arguments.Count >= 2).ToArray();
            var write = writeCalls.Select(call => new TagEvidence(Integer(call.ArgumentList.Arguments[0].Expression), Line(call), call.ToString())).ToArray();
            var gates = method.DescendantNodes().OfType<IfStatementSyntax>()
                .Where(statement => statement.Condition.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()
                    .Any(member => member.Expression.ToString() == "DxfVersion"))
                .Select(statement => new LocatedText(Line(statement), statement.Condition.ToString())).ToArray();
            var dispatch = method.DescendantNodes().OfType<CaseSwitchLabelSyntax>()
                .Where(label => label.Value is MemberAccessExpressionSyntax member
                    && new[] { "DxfObjectCode", "EntityType", "HeaderVariableCode" }.Contains(member.Expression.ToString(), StringComparer.Ordinal))
                .Select(label => new LocatedText(Line(label), label.Parent?.ToString() ?? label.ToString())).ToArray();
            string[] helpers = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(call => call.Expression is MemberAccessExpressionSyntax member && member.Expression is ThisExpressionSyntax)
                .Select(call => ((MemberAccessExpressionSyntax)call.Expression).Name.Identifier.ValueText)
                .Where(name => name.StartsWith("Read", StringComparison.Ordinal) || name.StartsWith("Write", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            return new MethodEvidence(method.Identifier.ValueText, method.ParameterList.ToString(), Line(method), EndLine(method), read, write, gates, dispatch, helpers);
        }).ToArray();
        return new FileEvidence(Relative(root, path), Hash(path), methods);
    }

    private static ModelEvidence AnalyzeModel(string root, string path)
    {
        CompilationUnitSyntax syntax = Parse(path);
        var properties = syntax.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Where(property => property.Modifiers.Any(SyntaxKind.PublicKeyword))
            .Select(property => new PropertyEvidence(property.Identifier.ValueText, property.Type.ToString(),
                property.AccessorList?.Accessors.Any(accessor => accessor.IsKind(SyntaxKind.SetAccessorDeclaration)) == true,
                Line(property))).ToArray();
        return new ModelEvidence(Relative(root, path), Hash(path), properties);
    }

    private static int? Integer(ExpressionSyntax expression)
    {
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NumericLiteralExpression))
            return Convert.ToInt32(literal.Token.Value, CultureInfo.InvariantCulture);
        return null;
    }

    private static string Markdown(string revision, FileEvidence[] files, Dictionary<string, ModelEvidence[]> models)
    {
        var text = new StringBuilder("# DXF group-code and model inventory\n\n");
        text.Append("Source revision: `").Append(revision).Append("`. See JSON for SHA-256 hashes, exact case bodies, write expressions, dispatch and version conditions.\n\n");
        text.Append("**This is syntax evidence, not a coverage percentage or a conformance certificate.** A code can be consumed but ignored, emitted as a constant, conditionally omitted, handled in a helper, or valid only in a particular DXF version. A numeric case in an unrelated switch is deliberately not counted. Dynamic write-code expressions remain explicit in JSON. Unknown fields and ordering must be tested independently.\n\n");
        foreach (FileEvidence file in files)
        {
            text.Append("## ").Append(file.Path).Append("\n\n");
            text.Append("| Method | Direct read cases on `.Code` | Direct literal write codes | Version conditions |\n|---|---|---|---|\n");
            foreach (MethodEvidence method in file.Methods)
            {
                if (method.ReadCases.Length + method.Writes.Length + method.VersionConditions.Length + method.Dispatch.Length == 0) continue;
                string link = "https://github.com/wieslawsoltes/netDxf/blob/" + revision + "/" + file.Path + "#L" + method.StartLine + "-L" + method.EndLine;
                text.Append("| [").Append(method.Name).Append("](").Append(link).Append(") | ")
                    .Append(Codes(method.ReadCases)).Append(" | ").Append(Codes(method.Writes)).Append(" | ")
                    .Append(string.Join("; ", method.VersionConditions.Select(gate => Escape(gate.Text)))).Append(" |\n");
            }
            text.AppendLine();
        }
        foreach (var group in models)
        {
            text.Append("## Public properties: ").Append(group.Key).Append("\n\n");
            text.Append("Declared properties only; inherited properties, methods and wire semantics are not inferred.\n\n| Source | Public properties |\n|---|---|\n");
            foreach (ModelEvidence model in group.Value.Where(m => m.Properties.Length != 0))
                text.Append("| ").Append(model.Path).Append(" | ").Append(string.Join("; ", model.Properties.Select(p => Escape(p.Type + " " + p.Name + (p.HasSetter ? " { get; set; }" : " { get; }"))))).Append(" |\n");
            text.AppendLine();
        }
        return text.ToString();
    }

    private static string Codes(TagEvidence[] tags)
        => string.Join(", ", tags.Where(t => t.Code.HasValue).Select(t => t.Code!.Value).Distinct().OrderBy(x => x))
            + (tags.Any(t => !t.Code.HasValue) ? " (+ dynamic codes; see JSON)" : "");
    private static string Escape(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    private static int EndLine(SyntaxNode node) => node.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed record TagEvidence(int? Code, int Line, string Text);
    private sealed record LocatedText(int Line, string Text);
    private sealed record MethodEvidence(string Name, string Parameters, int StartLine, int EndLine, TagEvidence[] ReadCases, TagEvidence[] Writes, LocatedText[] VersionConditions, LocatedText[] Dispatch, string[] HelperCalls);
    private sealed record FileEvidence(string Path, string Sha256, MethodEvidence[] Methods);
    private sealed record PropertyEvidence(string Name, string Type, bool HasSetter, int Line);
    private sealed record ModelEvidence(string Path, string Sha256, PropertyEvidence[] Properties);
}
