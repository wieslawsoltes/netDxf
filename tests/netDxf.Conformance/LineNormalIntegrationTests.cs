// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using netDxf;
using netDxf.Entities;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void LineReviewInjectInvalidNormal(Line line, Vector3 invalid)
    {
        // Direction assignment now rejects before mutation. First verify that
        // contract, then explicitly construct malformed private state so the
        // original LINE transform rejection and rollback checks still execute.
        long[] before = LineReviewBits(line);
        byte[]? proxy = line.ProxyGraphics;
        Throws<ArgumentException>(() => line.Normal = invalid);
        Check(before.SequenceEqual(LineReviewBits(line)), "Rejected LINE normal assignment changed geometry");
        Check(proxy == null ? line.ProxyGraphics == null : proxy.SequenceEqual(line.ProxyGraphics!),
            "Rejected LINE normal assignment changed proxy graphics");
        FieldInfo field = typeof(EntityObject).GetField("normal", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The test-only normal fault-injection field is absent");
        field.SetValue(line, invalid);
    }
}
