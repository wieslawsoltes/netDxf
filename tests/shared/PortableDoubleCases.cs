// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BigInteger = System.Numerics.BigInteger;
using System.Reflection;
using System.Linq;
using System.Text;
using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Qualification
{
    // Input-only exact midpoint corpus shared by conformance and every installed
    // package profile. Expected bits do not come from a floating-point parser.
    internal static class PortableDoubleCases
    {
        internal sealed class Case
        {
            internal readonly string Id, Token;
            internal readonly ulong? Bits;
            internal Case(string id, string token, ulong? bits) { Id = id; Token = token; Bits = bits; }
        }

        internal static IEnumerable<Case> All()
        {
            for (int exponent = 0; exponent < 2047; exponent++)
            {
                ulong lower = ((ulong)exponent << 52) | ((ulong)exponent * 0x1f123bb5UL);
                foreach (Case item in Boundary("exponent/" + exponent, lower, 1)) yield return item;
            }
            ulong[] edges = { 0, 1, 2, 0xffffffffffffeUL, 0xfffffffffffffUL,
                0x10000000000000UL, 0x3fefffffffffffffUL, 0x3ff0000000000000UL,
                0x3ff0000000000001UL, 0x7feffffffffffffeUL, 0x7fefffffffffffffUL };
            for (int i = 0; i < edges.Length; i++)
                foreach (Case item in Boundary("edge/" + i, edges[i], 1)) yield return item;
            foreach (int i in new[] { 0, 4, 7, 10 })
                foreach (Case item in Boundary("tail/" + i, edges[i], 1301)) yield return item;

            string[] valid = { "0", "-0", "+0.0", " \t-00.000e+23\t ", ".5", "1.",
                "  +1.5e+0\t", "4.9406564584124654e-324", "-1e-99999999999999999999",
                "1.7976931348623157e308", "9007199254740993", "3.1415926535897931",
                "0e" + new string('9', 4000), "0." + new string('0', 99999) + "1e100000" };
            ulong[] expected = { 0, 0x8000000000000000UL, 0, 0x8000000000000000UL,
                0x3fe0000000000000UL, 0x3ff0000000000000UL, 0x3ff8000000000000UL, 1,
                0x8000000000000000UL, 0x7fefffffffffffffUL, 0x4340000000000000UL,
                0x400921fb54442d18UL, 0, 0x3ff0000000000000UL };
            for (int i = 0; i < valid.Length; i++) yield return new Case("literal/" + i, valid[i], expected[i]);
            string[] invalid = { "", " ", "+", "-", ".", "e1", "1e", "1e+", "--0", "1.2.3",
                "1,000", "0x1", "NaN", "Infinity", "-Infinity", "1e309", "1e99999999999999999999",
                "1 2", "1e2x", "-0\0", "\u00a01", "1\u00a0", "\u0661", "1\u2003",
                "0e" + new string('9', 4000) + "x", "0." + new string('0', 99999) + "x" };
            for (int i = 0; i < invalid.Length; i++) yield return new Case("invalid/" + i, invalid[i], null);
        }

        private static IEnumerable<Case> Boundary(string id, ulong lower, int tail)
        {
            int exponent = (int)(lower >> 52);
            BigInteger mantissa = exponent == 0 ? lower : (lower & 0xfffffffffffffUL) | (1UL << 52);
            int power = exponent == 0 ? -1075 : exponent - 1076;
            BigInteger coefficient = 2 * mantissa + 1;
            if (power < 0) coefficient *= BigInteger.Pow(5, -power);
            else { coefficient <<= power; power = 0; }
            coefficient *= BigInteger.Pow(10, tail);
            power -= tail;
            for (int delta = -1; delta <= 1; delta++)
            {
                ulong rounded = delta < 0 || (delta == 0 && (lower & 1) == 0) ? lower : lower + 1;
                string token = (coefficient + delta).ToString(CultureInfo.InvariantCulture)
                    + "e" + power.ToString(CultureInfo.InvariantCulture);
                for (int sign = 0; sign < 2; sign++)
                    yield return new Case(id + "/" + delta + "/" + sign, sign == 0 ? token : "-" + token,
                        rounded == 0x7ff0000000000000UL ? (ulong?)null : rounded | ((ulong)sign << 63));
            }
        }

        internal delegate bool Parser(string text, out double value);
        internal static Parser GetParser(Assembly assembly, string name)
        {
            MethodInfo method = assembly.GetType("netDxf.IO.DxfDoubleParser", true)!
                .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
            return (Parser)Delegate.CreateDelegate(typeof(Parser), method);
        }

        internal static string? ParseBits(Parser parser, string token)
        {
            return parser(token, out double value) ? Hex(unchecked((ulong)BitConverter.DoubleToInt64Bits(value))) : null;
        }
        internal static string Hex(ulong bits) { return bits.ToString("x16", CultureInfo.InvariantCulture); }
        internal static string? Expected(Case item) { return item.Bits.HasValue ? Hex(item.Bits.Value) : null; }

        internal static string? CodecBits(Assembly assembly, Case item)
        {
            Type type = assembly.GetType("netDxf.IO.TextCodeValueReader", true)!;
            using (var text = new StringReader("10\n" + item.Token + "\n0\nEOF\n"))
            {
                object reader = Activator.CreateInstance(type, text)!;
                MethodInfo next = type.GetMethod("Next")!;
                try { next.Invoke(reader, null); }
                catch (TargetInvocationException error) when (error.InnerException is FormatException) { return null; }
                double value = (double)type.GetMethod("ReadDouble", Type.EmptyTypes)!.Invoke(reader, null)!;
                next.Invoke(reader, null);
                if ((string)type.GetMethod("ReadString", Type.EmptyTypes)!.Invoke(reader, null)! != "EOF")
                    throw new InvalidOperationException("Numeric parsing consumed the following record: " + item.Id);
                return Hex(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));
            }
        }

        internal static Case[] WireCases()
        {
            var rows = new List<Case>();
            ulong[] edges = { 0, 1, 0xfffffffffffffUL, 0x3ff0000000000000UL, 0x7fefffffffffffffUL };
            for (int i = 0; i < edges.Length; i++)
                rows.AddRange(Boundary("wire/" + i, edges[i], 1).Where(item => item.Bits.HasValue));
            return rows.ToArray();
        }

        internal static void VerifyWire(DxfVersion version, bool binary, string? directory)
        {
            Case[] rows = WireCases();
            var seed = new DxfDocument(version);
            var points = new List<Point>();
            for (int i = 0; i < rows.Length; i++)
            {
                var point = new Point(new Vector3(123.125 + i, 2, 3));
                points.Add(point); seed.Entities.Add(point);
            }
            var following = new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6));
            seed.Entities.Add(following);
            using (var stream = new MemoryStream())
            {
                if (!seed.Save(stream, false)) throw new InvalidOperationException("Decimal seed save");
                string[] lines = Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n").Split('\n');
                int index = -1, replacements = 0;
                bool pointRecord = false;
                for (int i = 0; i + 1 < lines.Length; i += 2)
                {
                    int code = int.Parse(lines[i], CultureInfo.InvariantCulture);
                    if (code == 0) { pointRecord = lines[i + 1] == "POINT"; if (pointRecord) index++; }
                    if (pointRecord && code == 10) { lines[i + 1] = rows[index].Token; replacements++; }
                }
                if (replacements != rows.Length) throw new InvalidOperationException("Decimal input inventory");
                byte[] source = Encoding.UTF8.GetBytes(string.Join("\n", lines));
                byte[] before = (byte[])source.Clone();
                string prefix = "portable-double-wire-" + version + "-" + (binary ? "binary" : "text");
                if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + "-source.dxf"), source);
                using (var input = new MemoryStream(source))
                {
                    var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Decimal input load");
                    if (!input.CanRead || !source.SequenceEqual(before)) throw new InvalidOperationException("Decimal source changed");
                    VerifyPoints(loaded, points, following, rows);
                    using (var output = new MemoryStream())
                    {
                        if (!loaded.Save(output, binary) || !output.CanWrite) throw new InvalidOperationException("Decimal output save");
                        byte[] bytes = output.ToArray();
                        if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + "-output.dxf"), bytes);
                        output.Position = 0;
                        var reloaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Decimal output load");
                        VerifyPoints(reloaded, points, following, rows);
                        using (var final = new MemoryStream())
                        {
                            if (!reloaded.Save(final, false)) throw new InvalidOperationException("Decimal final save");
                            if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + "-resave.dxf"), final.ToArray());
                            final.Position = 0;
                            VerifyPoints(DxfDocument.Load(final) ?? throw new InvalidOperationException("Decimal final load"), points, following, rows);
                        }
                    }
                }
            }
        }

        private static void VerifyPoints(DxfDocument document, List<Point> seeds, Line line, Case[] rows)
        {
            if (document.Entities.Points.Count() != rows.Length || document.Entities.Lines.Count() != 1)
                throw new InvalidOperationException("Decimal entity inventory");
            for (int i = 0; i < rows.Length; i++)
            {
                var point = document.GetObjectByHandle(seeds[i].Handle) as Point;
                if (point == null)
                    throw new InvalidOperationException("Missing decimal POINT handle: " + seeds[i].Handle);
                string actual = Hex(unchecked((ulong)BitConverter.DoubleToInt64Bits(point.Position.X)));
                if (actual != Expected(rows[i]) || point.Position.Y != 2 || point.Position.Z != 3)
                    throw new InvalidOperationException("Decimal typed point: " + rows[i].Id
                        + " expected=" + Expected(rows[i]) + " actual=" + actual
                        + " Y=" + point.Position.Y.ToString("R", CultureInfo.InvariantCulture)
                        + " Z=" + point.Position.Z.ToString("R", CultureInfo.InvariantCulture));
                var clone = (Point)point.Clone();
                if (BitConverter.DoubleToInt64Bits(clone.Position.X) != BitConverter.DoubleToInt64Bits(point.Position.X))
                    throw new InvalidOperationException("Decimal clone bits");
            }
            var following = document.GetObjectByHandle(line.Handle) as Line;
            if (following == null || following.StartPoint != line.StartPoint || following.EndPoint != line.EndPoint)
                throw new InvalidOperationException("Decimal following geometry/handle");
        }

        internal static void VerifyDispatch(Assembly assembly)
        {
            // Exercise both sides of the short-token fast-path threshold with
            // valid/invalid tokens, signed underflow, overflow, and exact ties.
            string[] tokens = { "1", "-0", "-1e-9999", "1e309", "1x", "0\0", "9007199254740993",
                "1.00000000000000011102230246251565404236316680908203125" };
            ulong?[] expected = { 0x3ff0000000000000UL, 0x8000000000000000UL,
                0x8000000000000000UL, null, null, null, 0x4340000000000000UL, 0x3ff0000000000000UL };
            Parser portable = GetParser(assembly, "TryParsePortable"), selected = GetParser(assembly, "TryParse");
            foreach (int length in new[] { 63, 64, 65 })
            for (int i = 0; i < tokens.Length; i++)
            {
                var item = new Case("dispatch/" + length + "/" + i, tokens[i].PadLeft(length), expected[i]);
                if (ParseBits(portable, item.Token) != Expected(item) || ParseBits(selected, item.Token) != Expected(item)
                    || CodecBits(assembly, item) != Expected(item))
                    throw new InvalidOperationException("Decimal parser dispatch: " + item.Id);
            }
        }

        // Consumer grammar/length limits remain independent of the low-level DXF token codec.
        internal static IEnumerable<Case> NumericConsumerCases()
        {
            foreach (Case item in All())
                if (item.Bits.HasValue && item.Token.Length <= 4050
                    && !item.Token.Any(char.IsWhiteSpace)) yield return item;
        }

        internal static ulong[] VerifyNumericConsumer(Case item)
        {
            ulong expected = item.Bits!.Value;
            // Formula evaluation deliberately canonicalizes every zero to positive zero.
            ulong formulaExpected = (expected & 0x7fffffffffffffffUL) == 0 ? 0 : expected;
            double scalar = netDxf.Tables.DxfTableFormula.ParseScalar("=" + item.Token).EvaluateScalar();
            double table = netDxf.Tables.DxfTableFormula.Parse("=" + item.Token).Evaluate(1, 1,
                address => { throw new InvalidOperationException("Literal invoked a table resolver"); });
            var format = netDxf.Units.DxfValueFormat.Parse("%lu2%pr8%ct8[" + item.Token + "]");
            double factor = (double)typeof(netDxf.Units.DxfValueFormat)
                .GetField("factor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(format)!;
            if (unchecked((ulong)BitConverter.DoubleToInt64Bits(scalar)) != formulaExpected
                || unchecked((ulong)BitConverter.DoubleToInt64Bits(table)) != formulaExpected
                || unchecked((ulong)BitConverter.DoubleToInt64Bits(factor)) != expected)
                throw new InvalidOperationException("TABLE/FIELD numeric consumer rounding: " + item.Id);
            // Check the public formatter too; parsing the known binary64 spelling avoids
            // comparing platform-dependent default double formatting directly.
            string canonical = BitConverter.Int64BitsToDouble(unchecked((long)expected))
                .ToString("G17", CultureInfo.InvariantCulture);
            var reference = netDxf.Units.DxfValueFormat.Parse("%lu2%pr8%ct8[" + canonical + "]");
            if (format.Format(1.0) != reference.Format(1.0))
                throw new InvalidOperationException("Conversion-factor display: " + item.Id);
            return new[] { unchecked((ulong)BitConverter.DoubleToInt64Bits(scalar)),
                unchecked((ulong)BitConverter.DoubleToInt64Bits(table)),
                unchecked((ulong)BitConverter.DoubleToInt64Bits(factor)) };
        }

        internal static void VerifyInstalled(Assembly assembly)
        {
            VerifyDispatch(assembly);
            foreach (Case item in NumericConsumerCases()) VerifyNumericConsumer(item);
            Parser portable = GetParser(assembly, "TryParsePortable"), selected = GetParser(assembly, "TryParse");
            foreach (Case item in All())
            {
                string? expected = Expected(item);
                if (ParseBits(portable, item.Token) != expected || ParseBits(selected, item.Token) != expected
                    || CodecBits(assembly, item) != expected)
                    throw new InvalidOperationException("Installed-package decimal rounding: " + item.Id);
            }
            foreach (DxfVersion version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004,
                DxfVersion.AutoCad2007, DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 })
                foreach (bool binary in new[] { false, true }) VerifyWire(version, binary, null);
        }
    }
}
