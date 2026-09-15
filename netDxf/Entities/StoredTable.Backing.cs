// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.IO;
using netDxf.Objects;

namespace netDxf.Entities
{
    public sealed partial class StoredTable
    {
        private Func<string, string> decode;
        /// <summary>Gets the retained TABLECONTENT owned through this table's roundtrip extension, when uniquely identified.</summary>
        public DxfOpaqueObject BackingContent { get; private set; }
        /// <summary>Gets literal agreement with the recognized backing cell envelopes, or null when no complete comparison is possible.</summary>
        /// <remarks>This checks stored scalar values only. It does not qualify layout, styles, formulas, fields or regeneration.</remarks>
        public bool? BackingLiteralValuesAgree { get; private set; }
        private void ResolveBacking()
        {
            var candidates = this.source.Objects.Items.OfType<DxfOpaqueObject>().Where(o => o.CodeName == "TABLECONTENT" &&
                o.Owner is DxfXRecord && ReferenceEquals(o.Owner.Owner, this.ExtensionDictionary)).ToList();
            if (this.ExtensionDictionary == null || candidates.Count != 1) return;
            this.BackingContent = candidates[0];
            if (this.Grid == null) return;
            var tags = this.BackingContent.Tags.ToList();
            var starts = tags.Select((t, i) => new { t, i }).Where(x => x.t.Code == 1 && (string)x.t.Value == "LINKEDTABLEDATACELL_BEGIN").Select(x => x.i).ToList();
            if (starts.Count != this.Grid.Cells.Count) return;
            bool agree = true;
            for (int i = 0; i < starts.Count; i++)
            {
                if (this.Grid.Cells[i].StoredType != 1) return;
                if (this.Grid.Cells[i].Tags.Count(t => t.Code == 301 && (string)t.Value == "CELL_VALUE") != 1) return;
                int? entityType = this.Grid.Cells[i].ValueType;
                if (entityType != 0 && entityType != 1 && entityType != 2 && entityType != 4) return;
                int end = tags.FindIndex(starts[i] + 1, t => t.Code == 309 && (string)t.Value == "LINKEDTABLEDATACELL_END");
                if (end < 0 || (i + 1 < starts.Count && end >= starts[i + 1])) return;
                var body = tags.GetRange(starts[i] + 1, end - starts[i] - 1);
                var counts = body.Where(t => t.Code == 95).ToList();
                if (counts.Count != 1 || (int)counts[0].Value < 0 || (int)counts[0].Value > 1) return;
                int marker = body.FindIndex(t => t.Code == 300 && (string)t.Value == "VALUE");
                if ((int)counts[0].Value == 0)
                {
                    if (marker >= 0) return;
                    agree &= !this.Grid.Cells[i].HasLiteralValue || this.Grid.Cells[i].LiteralValue == null;
                    continue;
                }
                if (marker < 0 || body.Count(t => t.Code == 300 && (string)t.Value == "VALUE") != 1) return;
                int valueEnd = body.FindIndex(marker + 1, t => t.Code == 304 && (string)t.Value == "ACVALUE_END");
                if (valueEnd < 0) return;
                var cellTags = new List<DxfTag> { new DxfTag(171, (short)1), new DxfTag(301, "CELL_VALUE") };
                cellTags.AddRange(body.Skip(marker + 1).Take(valueEnd - marker));
                StoredTableCell value;
                try { value = new StoredTableCell(cellTags, this.decode, this.SourceVersion); }
                catch (System.IO.InvalidDataException) { return; }
                if (value.ValueType != 0 && value.ValueType != 1 && value.ValueType != 2 && value.ValueType != 4) return;
                var entityCell = this.Grid.Cells[i];
                if (value.HasLiteralValue != entityCell.HasLiteralValue) agree = false;
                else if (value.HasLiteralValue && !Equals(value.LiteralValue, entityCell.LiteralValue)) agree = false;
            }
            this.BackingLiteralValuesAgree = agree;
        }
    }
}
