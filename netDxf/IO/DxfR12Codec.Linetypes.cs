// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using netDxf.Tables;

namespace netDxf.IO
{
    public static partial class DxfR12Codec
    {
        private static string LinetypeName(string name)
        {
            ResourceName(name);
            if (string.Equals(name, Linetype.ByLayerName, StringComparison.OrdinalIgnoreCase)) return "BYLAYER";
            if (string.Equals(name, Linetype.ByBlockName, StringComparison.OrdinalIgnoreCase)) return "BYBLOCK";
            if (string.Equals(name, Linetype.DefaultName, StringComparison.OrdinalIgnoreCase)) return "CONTINUOUS";
            return name;
        }

        private static string LinetypeDescription(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            foreach (char c in text)
                if (c < ' ' || c == '\x7f')
                    throw new NotSupportedException("R12 linetype descriptions cannot contain control characters.");
            return text;
        }

        private static double PatternLength(IEnumerable<double> segments)
        {
            double total = 0;
            foreach (double length in segments) total = Finite(total + Math.Abs(Finite(length)));
            return total;
        }

        private sealed class LinetypePacket
        {
            internal string Name, Description;
            internal double[] Pattern;
            internal double Total;
            internal bool IsSelector { get { return this.Name == "BYLAYER" || this.Name == "BYBLOCK"; } }

            internal static LinetypePacket Capture(Linetype value)
            {
                if (value == null || value.GetType() != typeof(Linetype))
                    throw new NotSupportedException("R12 interchange requires a plain Linetype.");
                Metadata(value);
                string name = LinetypeName(value.Name), description = LinetypeDescription(value.Description);
                if (value.Segments.Count > short.MaxValue)
                    throw new NotSupportedException("The linetype element count exceeds the signed R12 count field.");
                var pattern = new double[value.Segments.Count];
                for (int i = 0; i < pattern.Length; i++)
                {
                    LinetypeSegment segment = value.Segments[i];
                    if (segment == null || segment.GetType() != typeof(LinetypeSimpleSegment))
                        throw new NotSupportedException("R12 linetypes cannot contain embedded TEXT or SHAPE elements.");
                    pattern[i] = Finite(segment.Length);
                }
                if ((name == "BYLAYER" || name == "BYBLOCK" || name == "CONTINUOUS") && pattern.Length != 0)
                    throw new NotSupportedException("A built-in linetype cannot carry a custom pattern.");
                if ((name == "BYLAYER" || name == "BYBLOCK") && description.Length != 0)
                    throw new NotSupportedException("Selection-only BYLAYER/BYBLOCK metadata cannot be retained in an authored table.");
                // Both spellings represent the library's conventional CONTINUOUS default.
                if (name == "CONTINUOUS" && description.Length == 0) description = "Solid line";
                return new LinetypePacket { Name = name, Description = description, Pattern = pattern, Total = PatternLength(pattern) };
            }

            internal bool SameSettings(LinetypePacket other)
            {
                if (this.Description != other.Description || this.Pattern.Length != other.Pattern.Length) return false;
                for (int i = 0; i < this.Pattern.Length; i++)
                    if (BitConverter.DoubleToInt64Bits(this.Pattern[i]) != BitConverter.DoubleToInt64Bits(other.Pattern[i])) return false;
                return true;
            }
        }

        private sealed partial class PrimitiveWriter
        {
            private readonly Dictionary<string, LinetypePacket> linePatterns = new Dictionary<string, LinetypePacket>(StringComparer.OrdinalIgnoreCase);
            private readonly List<LinetypePacket> orderedLinePatterns = new List<LinetypePacket>();

            private string RegisterLinetype(Linetype value, bool layer = false)
            {
                LinetypePacket packet = LinetypePacket.Capture(value);
                if (layer && packet.IsSelector)
                    throw new NotSupportedException("A layer linetype must resolve to a concrete pattern.");
                if (packet.IsSelector) return packet.Name;
                if (this.linePatterns.TryGetValue(packet.Name, out LinetypePacket existing))
                {
                    if (!existing.SameSettings(packet)) throw new InvalidOperationException("Conflicting same-named R12 linetype definitions.");
                    return existing.Name;
                }
                if (this.linePatterns.Count == short.MaxValue)
                    throw new NotSupportedException("R12 LTYPE table count limit exceeded.");
                if (packet.Pattern.Length > this.options.MaximumTags || packet.Description.Length > this.options.MaximumStringLength)
                    throw new InvalidDataException("R12 linetype exceeds the raw processing budget.");
                this.linePatterns.Add(packet.Name, packet); this.orderedLinePatterns.Add(packet);
                return packet.Name;
            }

            private void WriteLinetypes(List<DxfTag> destination)
            {
                if (!this.linePatterns.ContainsKey("CONTINUOUS")) this.RegisterLinetype(Linetype.Continuous);
                this.Add(destination, 0, "TABLE"); this.Add(destination, 2, "LTYPE");
                this.Add(destination, 70, (short)this.linePatterns.Count);
                // Retain the previous primitive-only packet and deterministic ordering.
                this.WriteLinetype(destination, this.linePatterns["CONTINUOUS"]);
                foreach (LinetypePacket packet in this.orderedLinePatterns)
                    if (packet.Name != "CONTINUOUS") this.WriteLinetype(destination, packet);
                this.Add(destination, 0, "ENDTAB");
            }

            private void WriteLinetype(List<DxfTag> destination, LinetypePacket packet)
            {
                this.Add(destination, 0, "LTYPE"); this.Add(destination, 2, packet.Name); this.Add(destination, 70, (short)0);
                this.Add(destination, 3, packet.Description); this.Add(destination, 72, (short)65);
                this.Add(destination, 73, (short)packet.Pattern.Length); this.Add(destination, 40, packet.Total);
                foreach (double segment in packet.Pattern) this.Add(destination, 49, segment);
            }
        }

        private static Dictionary<string, Linetype> ReadLinetypes(DxfRawDocument document)
        {
            var result = new Dictionary<string, Linetype>(StringComparer.OrdinalIgnoreCase);
            bool sawTable = false;
            foreach (DxfRawSection section in document.Sections)
            {
                if (!string.Equals(section.Name, "TABLES", StringComparison.OrdinalIgnoreCase)) continue;
                string table = null;
                foreach (DxfRawRecord record in section.Records)
                {
                    if (string.Equals(record.Name, "TABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (table != null) throw new FormatException("Nested table declarations are invalid.");
                        table = new Fields(record).Text(2, null, true);
                        if (string.Equals(table, "LTYPE", StringComparison.OrdinalIgnoreCase))
                        {
                            if (sawTable) throw new FormatException("Duplicate LTYPE tables are ambiguous.");
                            sawTable = true;
                        }
                    }
                    else if (string.Equals(record.Name, "ENDTAB", StringComparison.OrdinalIgnoreCase))
                    {
                        if (table == null) throw new FormatException("Unmatched ENDTAB.");
                        table = null;
                    }
                    else if (string.Equals(record.Name, "LTYPE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.Equals(table, "LTYPE", StringComparison.OrdinalIgnoreCase))
                            throw new FormatException("An LTYPE record is outside its table.");
                        var pattern = new List<double>();
                        var fields = new Fields(record, pattern);
                        string name = LinetypeName(fields.Text(2, null, true)); fields.Identity();
                        string description = LinetypeDescription(fields.Text(3, ""));
                        short flags = fields.Integer(70, 0), alignment = fields.Integer(72, 65), count = fields.Integer(73, 0);
                        double declared = fields.Number(40, 0), total = PatternLength(pattern);
                        if ((flags & ~64) != 0 || alignment != 65)
                            throw new NotSupportedException("External or unsupported R12 linetype flags/alignment.");
                        if (count < 0 || count != pattern.Count || declared < 0)
                            throw new FormatException("R12 linetype count or length is inconsistent.");
                        // Text producers can round the redundant total independently of the
                        // signed elements. Preserve element bits; recompute this total on output.
                        double tolerance = Math.Max(declared, total) * 1e-12 + double.Epsilon * (count + 1);
                        if (Math.Abs(declared - total) > tolerance)
                            throw new FormatException("R12 total pattern length disagrees with its elements.");
                        fields.Finish();
                        if ((name == "CONTINUOUS" || name == "BYLAYER" || name == "BYBLOCK") && count != 0)
                            throw new NotSupportedException("Built-in linetypes cannot carry custom patterns.");
                        if ((name == "BYLAYER" || name == "BYBLOCK") && description.Length != 0)
                            throw new NotSupportedException("Custom selector descriptions require raw preservation.");
                        if (result.ContainsKey(name)) throw new FormatException("Duplicate linetype names are ambiguous.");
                        var segments = new List<LinetypeSegment>(count);
                        foreach (double length in pattern) segments.Add(new LinetypeSimpleSegment(length));
                        result.Add(name, new Linetype(name, segments, description));
                    }
                }
                if (table != null) throw new FormatException("Missing ENDTAB.");
            }
            if (!result.ContainsKey("CONTINUOUS")) result.Add("CONTINUOUS", Linetype.Continuous);
            if (!result.ContainsKey("BYLAYER")) result.Add("BYLAYER", Linetype.ByLayer);
            if (!result.ContainsKey("BYBLOCK")) result.Add("BYBLOCK", Linetype.ByBlock);
            return result;
        }

        private static Linetype ResolveLinetype(string name, Dictionary<string, Linetype> patterns, bool layer = false)
        {
            name = LinetypeName(name);
            if (layer && (name == "BYLAYER" || name == "BYBLOCK"))
                throw new NotSupportedException("A layer linetype must resolve to a concrete pattern.");
            if (!patterns.TryGetValue(name, out Linetype result))
                throw new FormatException("Undefined R12 linetype: " + name);
            return result;
        }
    }
}
