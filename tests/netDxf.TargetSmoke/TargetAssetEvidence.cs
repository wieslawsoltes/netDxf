// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using netDxf;

internal static class TargetAssetEvidence
{
    private static string Required(string name)
    {
        string? value = Environment.GetEnvironmentVariable("NETDXF_SMOKE_" + name);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Missing target evidence: " + name);
        return value;
    }

    private static string Framework(Assembly assembly)
    {
        var value = (TargetFrameworkAttribute?)Attribute.GetCustomAttribute(assembly, typeof(TargetFrameworkAttribute));
        return value?.FrameworkName ?? throw new InvalidOperationException("Missing target framework metadata");
    }

    private static string Moniker(string target)
    {
        switch (target)
        {
            case "netstandard2.0": return ".NETStandard,Version=v2.0";
            case "net471": return ".NETFramework,Version=v4.7.1";
            case "net48": return ".NETFramework,Version=v4.8";
            case "net6.0": return ".NETCoreApp,Version=v6.0";
            case "net8.0": return ".NETCoreApp,Version=v8.0";
            default: throw new InvalidOperationException("Unexpected target: " + target);
        }
    }

    // Called only after all shared package assertions have completed successfully.
    internal static void Complete(int scenarios)
    {
        if (scenarios != 12) throw new InvalidOperationException("Incomplete package scenario matrix");
        string asset = Required("ASSET"), runtime = Required("RUNTIME");
        string assetFramework = Framework(typeof(DxfDocument).Assembly);
        string consumerFramework = Framework(Assembly.GetExecutingAssembly());
        if (assetFramework != Moniker(asset) || consumerFramework != Moniker(runtime))
            throw new InvalidOperationException("The loaded asset or compiled consumer is not the requested target");
        string assemblyPath = typeof(DxfDocument).Assembly.Location;
        string assemblyHash;
        using (var algorithm = SHA256.Create())
        using (var stream = File.OpenRead(assemblyPath))
            assemblyHash = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        if (assemblyHash != Required("ASSEMBLY_SHA256"))
            throw new InvalidOperationException("Loaded assembly bytes differ from the selected NuGet asset");

        string frameworkRelease = "";
#if NETFRAMEWORK
        object? release = Microsoft.Win32.Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", null);
        int number = release is int item ? item : 0;
        if (Environment.Version.Major != 4 || number < (runtime == "net471" ? 461308 : 528040))
            throw new InvalidOperationException("Required .NET Framework runtime is not installed");
        frameworkRelease = number.ToString(CultureInfo.InvariantCulture);
#else
        int major = runtime == "net6.0" ? 6 : runtime == "net8.0" ? 8 : -1;
        if (Environment.Version.Major != major || Environment.Version.Minor != 0)
            throw new InvalidOperationException("Runtime major/minor differs from the requested execution target");
#endif
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["schema"] = "1", ["passed"] = "true", ["scenarios"] = scenarios.ToString(CultureInfo.InvariantCulture),
            ["host"] = Required("HOST"), ["runtime"] = runtime, ["asset"] = asset,
            ["commit"] = Required("COMMIT"), ["tree"] = Required("TREE"),
            ["package_sha256"] = Required("PACKAGE_SHA256"), ["assembly_sha256"] = assemblyHash,
            ["asset_framework"] = assetFramework, ["consumer_framework"] = consumerFramework,
            ["runtime_version"] = Environment.Version.ToString(), ["framework_release"] = frameworkRelease,
            ["assembly_path"] = assemblyPath
        };
        // CreateNew prevents a stale receipt from being silently overwritten.
        using (var stream = new FileStream(Required("RECEIPT"), FileMode.CreateNew, FileAccess.Write))
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            writer.WriteStartDocument(); writer.WriteStartElement("netdxf-target-smoke");
            foreach (var pair in values) writer.WriteAttributeString(pair.Key, pair.Value);
            writer.WriteEndElement(); writer.WriteEndDocument();
        }
        Console.WriteLine("PASS: exact package asset " + asset + "; consumer " + runtime + "; CLR " + Environment.Version
            + "; framework release " + frameworkRelease + "; SHA256 " + assemblyHash);
    }
}
