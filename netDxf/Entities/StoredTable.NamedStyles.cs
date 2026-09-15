// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Entities
{
    public sealed partial class StoredTable
    {
        private readonly Dictionary<DxfTag, Tuple<TextStyle, string>> namedStyles = new Dictionary<DxfTag, Tuple<TextStyle, string>>();
        private void ResolveNamedStyles()
        {
            // Group 7 has public STYLE semantics only in the recognized AcDbTable representation.
            int tableStart = this.payload.ToList().FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbTable");
            if (tableStart < 0) return;
            var header = this.payload.Skip(tableStart + 1).TakeWhile(t => t.Code != 100 && t.Code != 171).ToList();
            var flags = header.Where(t => t.Code == 90).ToList();
            if (flags.Count != 1 || (int)flags[0].Value != 22) return;
            bool table = false, value = false;
            int applicationDepth = 0;
            foreach (DxfTag tag in this.payload)
            {
                if (tag.Code == 100) { table = (string)tag.Value == "AcDbTable"; continue; }
                if (!table) continue;
                if (tag.Code == 102)
                {
                    string marker = (string)tag.Value;
                    if (marker.StartsWith("{", StringComparison.Ordinal)) applicationDepth++;
                    else if (marker == "}" && applicationDepth > 0) applicationDepth--;
                    continue;
                }
                if (applicationDepth > 0) continue;
                if (tag.Code == 301 && (string)tag.Value == "CELL_VALUE") { value = true; continue; }
                if (tag.Code == 304 && (string)tag.Value == "ACVALUE_END") { value = false; continue; }
                if (value || tag.Code != 7) continue;
                string name = this.decode((string)tag.Value);
                if (!this.source.TextStyles.TryGetValue(name, out TextStyle style)) continue;
                this.namedStyles.Add(tag, Tuple.Create(style, style.Name));
                this.references.Add(style);
            }
        }
        private string ChangedTextStyleName(DxfTag tag)
        {
            if (!this.namedStyles.TryGetValue(tag, out Tuple<TextStyle, string> binding)) return null;
            return binding.Item1.Name == binding.Item2 ? null : binding.Item1.Name;
        }
        private void ValidateNamedStyles()
        {
            foreach (TextStyle style in this.namedStyles.Values.Select(v => v.Item1))
                if (!ReferenceEquals(this.source.GetObjectByHandle(style.Handle), style))
                    throw new InvalidOperationException("A stored TABLE text-style dependency is no longer registered.");
        }
    }
}
